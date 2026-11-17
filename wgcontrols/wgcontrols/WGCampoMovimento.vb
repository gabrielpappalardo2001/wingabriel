Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports HtmlAgilityPack
Imports SymSpell
Imports wgcontrols

Partial Public Class WGCampoMovimento
    Inherits RichTextBox

    Private Shared log As New Logger()
    Private utils As WGFormUtils = WGFormUtils.GetInstance()

    Private WithEvents lettura As sintesivocale = sintesivocale.GetInstance()
    Private impostazioniComuni As New WGImpostazioniComuni()

    Public Property LetturaPunteggiaturaLetturaCampo As Boolean
        Get
            Return impostazioniComuni.LetturaPunteggiaturaLettura
        End Get
        Set(value As Boolean)
            impostazioniComuni.LetturaPunteggiaturaLettura = value
        End Set
    End Property

    Public Property LetturaPunteggiaturaScritturaCampo As Boolean
        Get
            Return impostazioniComuni.LetturaPunteggiaturaScrittura
        End Get
        Set(value As Boolean)
            impostazioniComuni.LetturaPunteggiaturaScrittura = value
        End Set
    End Property

    Public Property LivelloScritturaPunteggiaturaCampo As Integer
        Get
            Return impostazioniComuni.LivelloScritturaPunteggiatura
        End Get
        Set(value As Integer)
            impostazioniComuni.LivelloScritturaPunteggiatura = value
        End Set
    End Property

    Private Function SimboloDaLeggereInScritturaCampo(c As Char) As Boolean
        Return LivelloScritturaPunteggiaturaCampo >= WGImpostazioniComuni.LivelloSimboloScrittura(c)
    End Function

    Public Property multiriga As Boolean = False
    Public Property solonumeri As Boolean = False
    Public Property obbligatorio As Boolean = False

    Public Property tempoAvvio As DateTime = DateTime.Now
    Public Property IsWordMode As Boolean = False

    Public Shadows Property Visible As Boolean
        Get
            Try
                Return MyBase.Visible
            Catch ex As Exception
                log.ScriviLog("Visible Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                MyBase.Visible = value
                If value Then
                    attivahook()
                Else
                    disattivahook()
                End If
            Catch ex As Exception
                log.ScriviLog("Visible Set: " & ex.Message)
            End Try
        End Set
    End Property

    Private isAutoCapitalizing As Boolean = False
    Private _spellChecker As SymSpell
    Private _dictionaryLoaded As Boolean = False
    Private _dictLock As New Object()

    Private Shared ReadOnly regexMaiuscole As New Regex("(^|[\.\!\?]\s+|\r\n|\r|\n)(\s*)([a-z])", RegexOptions.Multiline Or RegexOptions.Compiled)
    Private Shared ReadOnly regexParole As New Regex("\b[\p{L}']+\b", RegexOptions.Compiled)

    Private WithEvents _spellCheckTimer As New System.Windows.Forms.Timer()
    Private WithEvents _autoCapTimer As New System.Windows.Forms.Timer()
    Private WithEvents _timerLettura As New System.Windows.Forms.Timer()

    Private _spellCheckMenu As WGMenuLista
    Private _currentSpellCheckInfo As Tuple(Of String, Integer, Integer)
    Private _anchorPoint As Integer = -1
    Private _posizioneVirtualeVerticale As Integer = 0

    Private offsetLetturaContinua As Integer = 0
    Private _inLetturaContinuaValore As Boolean = False
    ' La lettura continua (F8) e' una proprieta' perche' il braille deve saperne l'inizio e la fine.
    Private Property inLetturaContinua As Boolean
        Get
            Return _inLetturaContinuaValore
        End Get
        Set(value As Boolean)
            If _inLetturaContinuaValore = value Then Return
            _inLetturaContinuaValore = value
            WGBraille.ImpostaLetturaContinua(Me, value)
        End Set
    End Property
    Private _letturaContinuaRiga As Integer = 0
    Private _isHookActive As Boolean = False

    ' Evita che il watchdog (_timerLettura) e il vero segnale di fine lettura (OnFineLettura)
    ' possano entrambi far avanzare la riga quasi contemporaneamente: solo il primo dei
    ' due che scatta per la riga corrente ha effetto, l'altro viene ignorato.
    Private ReadOnly _transizioneLetturaLock As New Object()
    Private _transizioneGestita As Boolean = False

    Private isShiftPremuto As Boolean = False
    Private _altDestraPremuto As Boolean = False
    Private _altDestroUsato As Boolean = False
    Private isCtrlSinistroPremuto As Boolean = False

    Private tastiera As WGTastiera = WGTastiera.GetInstance()

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(ByVal hWnd As IntPtr, ByVal Msg As Integer, ByVal wParam As IntPtr, ByVal lParam As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetKeyboardState(lpKeyState As Byte()) As Boolean
    End Function

    Private Const VK_RMENU As Integer = &HA5

    Public Event RichiestaChiusura As EventHandler
    ''' <summary>Invio premuto in un campo di sola lettura: il campo che lo contiene decide che cosa farne.</summary>
    Public Event InvioInSolaLettura As EventHandler
    Public Event menudestroapertura(sender As Object, e As EventArgs)

    Public Delegate Sub RichiestaVerificaTargetEventHandler(activeWindow As IntPtr, ByRef isTarget As Boolean)
    Public Event RichiestaVerificaTarget As RichiestaVerificaTargetEventHandler

    Public Sub New()
        Try
            WGBraille.Collega(Me)
            tempoAvvio = DateTime.Now
            Task.Run(AddressOf InizializzaCorrettoreOrtograficoAsync)
            _spellCheckTimer.Interval = IntervalloDebounceCorrettoreCampo
            _autoCapTimer.Interval = IntervalloMaiuscolaAutomaticaCampo
            _timerLettura.Interval = IntervalloPausaLetturaContinuaCampo

            Me.AccessibleRole = AccessibleRole.None
            Me.AccessibleName = Nothing
            Me.HideSelection = False
        Catch ex As Exception
            log.ScriviLog($"Errore nel costruttore WGCampoMovimento: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Try
            MyBase.OnGotFocus(e)
            If WGTastieraTouch.RiprendiControlloTouch() Then Return
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk IsNot Nothing Then touk.RiassegnaControllo(Me.Handle)
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.OnGotFocus: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            Const WM_GETTEXT As Integer = &HD
            Const EM_GETTEXTEX As Integer = &H45E

            If m.Msg = WM_GETTEXT OrElse m.Msg = EM_GETTEXTEX Then
                Return
            End If

            MyBase.WndProc(m)
        Catch ex As Exception
            log.ScriviLog("WndProc: " & ex.Message)
        End Try
    End Sub

    Public Sub attivahook()
        Try
            If Not _isHookActive Then
                _isHookActive = True
                AddHandler tastiera.ShiftPremuto, AddressOf tastiera_ShiftPremuto
                AddHandler tastiera.ShiftRilasciato, AddressOf tastiera_ShiftRilasciato
                AddHandler tastiera.AltDestroPremuto, AddressOf tastiera_AltDestroPremuto
                AddHandler tastiera.AltDestroRilasciato, AddressOf tastiera_AltDestroRilasciato
                AddHandler tastiera.KeyDown, AddressOf tastiera_KeyDown
                AddHandler tastiera.ControlSinistroPremuto, AddressOf GestioneControlSinistroPremuto
                AddHandler tastiera.ControlSinistroRilasciato, AddressOf GestioneControlSinistroRilasciato
                AddHandler tastiera.ControlDestroPremuto, AddressOf GestioneControlDestroPremuto
                AddHandler tastiera.ControlDestroRilasciato, AddressOf GestioneControlDestroRilasciato
            End If
        Catch ex As Exception
            log.ScriviLog("attivahook: " & ex.Message)
        End Try
    End Sub

    Public Sub disattivahook()
        Try
            If _isHookActive Then
                _isHookActive = False
                RemoveHandler tastiera.ShiftPremuto, AddressOf tastiera_ShiftPremuto
                RemoveHandler tastiera.ShiftRilasciato, AddressOf tastiera_ShiftRilasciato
                RemoveHandler tastiera.AltDestroPremuto, AddressOf tastiera_AltDestroPremuto
                RemoveHandler tastiera.AltDestroRilasciato, AddressOf tastiera_AltDestroRilasciato
                RemoveHandler tastiera.KeyDown, AddressOf tastiera_KeyDown
                RemoveHandler tastiera.ControlSinistroPremuto, AddressOf GestioneControlSinistroPremuto
                RemoveHandler tastiera.ControlSinistroRilasciato, AddressOf GestioneControlSinistroRilasciato
                RemoveHandler tastiera.ControlDestroPremuto, AddressOf GestioneControlDestroPremuto
                RemoveHandler tastiera.ControlDestroRilasciato, AddressOf GestioneControlDestroRilasciato
            End If
        Catch ex As Exception
            log.ScriviLog("disattivahook: " & ex.Message)
        End Try
    End Sub

    Public Sub AttaccaTastiera()
        Try
            attivahook()
        Catch ex As Exception
            log.ScriviLog("AttaccaTastiera: " & ex.Message)
        End Try
    End Sub

    Public Sub RilasciaTastiera()
        Try
            disattivahook()
            inLetturaContinua = False
            If _timerLettura IsNot Nothing AndAlso Not Me.IsDisposed Then _timerLettura.Stop()
            lettura.FermaVoce()
        Catch ex As Exception
            log.ScriviLog($"Errore in RilasciaTastiera: {ex.Message}")
        End Try
    End Sub

    Private Sub tastiera_ShiftPremuto()
        Try
            isShiftPremuto = True
        Catch ex As Exception
            log.ScriviLog("tastiera_ShiftPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_ShiftRilasciato()
        Try
            isShiftPremuto = False
        Catch ex As Exception
            log.ScriviLog("tastiera_ShiftRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_AltDestroPremuto()
        Try
            _altDestraPremuto = True
            _altDestroUsato = False
        Catch ex As Exception
            log.ScriviLog("tastiera_AltDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_AltDestroRilasciato()
        Try
        If _altDestraPremuto AndAlso Not isShiftPremuto AndAlso Not _altDestroUsato Then
            RaiseEvent menudestroapertura(Me, EventArgs.Empty)
        End If
        _altDestraPremuto = False
        _altDestroUsato = False
        Catch ex As Exception
            log.ScriviLog("tastiera_AltDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Function ConvertiTastoInCarattere(tasto As Keys) As String
        Try
            Dim virtualKey As UInteger = CUInt(tasto And Keys.KeyCode)

            ' I simboli sopra i numeri della tastiera italiana (!, ", £...) solo se e' davvero quella scelta in
            ' WinGabriel: con un'altra lingua di tastiera scelta nelle impostazioni si passa alla traduzione vera piu'
            ' sotto (WGTastiera.CarattereDaLayoutScelto), che da' il simbolo giusto per quella tastiera.
            If isShiftPremuto AndAlso virtualKey >= 48 AndAlso virtualKey <= 57 AndAlso WGTastiera.TastieraItalianaAttiva() Then
                Select Case virtualKey
                    Case 49 : Return "!"
                    Case 50 : Return """"
                    Case 51 : Return "£"
                    Case 52 : Return "$"
                    Case 53 : Return "%"
                    Case 54 : Return "&"
                    Case 55 : Return "/"
                    Case 56 : Return "("
                    Case 57 : Return ")"
                    Case 48 : Return "="
                End Select
            End If

            If virtualKey >= 16 AndAlso virtualKey <= 18 Then Return ""
            If virtualKey >= 33 AndAlso virtualKey <= 40 Then Return ""
            If virtualKey = 8 OrElse virtualKey = 9 OrElse virtualKey = 13 OrElse virtualKey = 27 OrElse virtualKey = 46 Then Return ""

            Dim keyboardState(255) As Byte

            If Not GetKeyboardState(keyboardState) Then
                Return ""
            End If

            Dim shift As Boolean = isShiftPremuto
            Dim caps As Boolean = (keyboardState(Keys.CapsLock) And 1) <> 0

            keyboardState(&H10) = CByte(If(shift, &H80, 0))
            keyboardState(&H11) = CByte(If(isCtrlSinistroPremuto, &H80, 0))
            keyboardState(&H12) = CByte(If((tasto And Keys.Alt) = Keys.Alt, &H80, 0))
            keyboardState(&H14) = CByte(If(caps, 1, 0))

            ' La tastiera usata per tradurre il tasto e' quella scelta in WinGabriel (Impostazioni, Tastiera), non
            ' quella che Windows ha attiva in quel momento.
            Dim risultatoLayout As String = WGTastiera.CarattereDaLayoutScelto(virtualKey, keyboardState)
            If risultatoLayout <> "" Then Return risultatoLayout

            If virtualKey = 32 Then Return " "

            Return ""
        Catch ex As Exception
            log.ScriviLog("ConvertiTastoInCarattere: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub _timerLettura_Tick(sender As Object, e As EventArgs) Handles _timerLettura.Tick
        Try
            _timerLettura.Stop()
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Not inLetturaContinua Then Return

            ' Prima di considerare la lettura "bloccata", verifichiamo lo stato reale del
            ' motore vocale. Se sta ancora effettivamente parlando (es. sta pronunciando le
            ' ultime parole di una frase lunga) non interrompiamo: diamo altro tempo, cosi'
            ' non vengono piu' tagliate le parole finali.
            If lettura IsNot Nothing AndAlso lettura.StaParlando Then
                _timerLettura.Start()
                Return
            End If

            SyncLock _transizioneLetturaLock
                If _transizioneGestita Then Return
                _transizioneGestita = True
            End SyncLock

            ' Nessun avanzamento rilevato dal motore vocale entro il tempo previsto.
            ' Invece di interrompere la lettura continua (F8), si prosegue con la riga
            ' successiva cosi' la lettura non si blocca in modo silenzioso.
            If lettura IsNot Nothing Then lettura.FermaVoce()

            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then
                inLetturaContinua = False
                Return
            End If

            _letturaContinuaRiga += 1
            Me.BeginInvoke(New Action(AddressOf LeggiRigaContinua))
        Catch ex As Exception
            log.ScriviLog("_timerLettura_Tick: " & ex.Message)
            inLetturaContinua = False
        End Try
    End Sub

    Private Sub GestisciParolaLetta(posizione As Integer, lunghezza As Integer) Handles lettura.OnParolaLetta
        If inLetturaContinua AndAlso Not Me.IsDisposed AndAlso Me.IsHandleCreated Then
            Me.BeginInvoke(New Action(Sub()
                                          Try
                                              _timerLettura.Stop()
                                              _timerLettura.Start()
                                              Dim primoCharRiga As Integer = Me.GetFirstCharIndexFromLine(_letturaContinuaRiga)
                                              If primoCharRiga >= 0 Then
                                                  Dim nuovaPos As Integer = primoCharRiga + posizione
                                                  If nuovaPos >= 0 AndAlso nuovaPos <= Me.TextLength Then
                                                      Me.Select(nuovaPos, 0)
                                                      Me.ScrollToCaret()
                                                  End If
                                              End If
                                          Catch ex As Exception
                                              log.ScriviLog("GestisciParolaLetta (BeginInvoke): " & ex.Message)
                                          End Try
                                      End Sub))
        End If
    End Sub

    Private Sub GestisciFineLettura() Handles lettura.OnFineLettura
        Try
            If Not inLetturaContinua Then
                If Not Me.IsDisposed Then _timerLettura.Stop()
                Return
            End If

            SyncLock _transizioneLetturaLock
                If _transizioneGestita Then Return
                _transizioneGestita = True
            End SyncLock

            If Not Me.IsDisposed Then _timerLettura.Stop()
            _letturaContinuaRiga += 1
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then
                inLetturaContinua = False
                Return
            End If
            Me.BeginInvoke(New Action(AddressOf LeggiRigaContinua))
        Catch ex As Exception
            log.ScriviLog("GestisciFineLettura: " & ex.Message)
            inLetturaContinua = False
            If Not Me.IsDisposed Then _timerLettura.Stop()
        End Try
    End Sub

    Private Sub LeggiRigaContinua()
        Try
            If Not inLetturaContinua Then Return
            If Me.IsDisposed Then
                inLetturaContinua = False
                Return
            End If
            Dim righe As String() = Me.Lines
            If righe Is Nothing Then
                inLetturaContinua = False
                _timerLettura.Stop()
                Return
            End If
            Dim testoRiga As String = Nothing
            Dim rigaTrovata As Integer = -1
            Dim idx As Integer = _letturaContinuaRiga
            While idx < righe.Length
                Dim r As String = righe(idx)
                If String.IsNullOrWhiteSpace(r) Then
                    ' In lettura continua le righe vuote vengono saltate senza essere lette
                    idx += 1
                    Continue While
                End If
                _letturaContinuaRiga = idx
                rigaTrovata = idx
                testoRiga = r
                Exit While
            End While
            If rigaTrovata = -1 Then
                inLetturaContinua = False
                _timerLettura.Stop()
                Return
            End If
            Dim primoChar As Integer = Me.GetFirstCharIndexFromLine(rigaTrovata)
            If primoChar >= 0 Then
                Me.Select(primoChar, 0)
                Me.ScrollToCaret()
            End If

            ' Nuova riga in lettura: da qui in poi timer e OnFineLettura possono
            ' nuovamente far avanzare la lettura (uno solo dei due, il primo che scatta).
            SyncLock _transizioneLetturaLock
                _transizioneGestita = False
            End SyncLock

            _timerLettura.Stop()
            ' Con la sola barra braille la riga resta sulla barra per il tempo scelto: il controllo di blocco non deve scattare prima.
            _timerLettura.Interval = WGBraille.IntervalloWatchdog(IntervalloPausaLetturaContinuaCampo)
            _timerLettura.Start()

            ' L'invio del testo al motore vocale viene eseguito su un thread separato per
            ' non rischiare di bloccare la UI in attesa di operazioni verso il motore TTS.
            Dim testoDaLeggere As String = testoRiga
            Task.Run(Sub()
                         Try
                             If Not inLetturaContinua Then Return
                             lettura.LeggiTesto(testoDaLeggere, False, "Normale", False)
                         Catch exLettura As Exception
                             log.ScriviLog("LeggiRigaContinua (invio asincrono): " & exLettura.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("LeggiRigaContinua: " & ex.Message)
            inLetturaContinua = False
            _timerLettura.Stop()
        End Try
    End Sub

    Private Sub InviaLetturaAsincrona(testo As String)
        Try
            If Not String.IsNullOrEmpty(testo) Then
                Task.Run(Sub() lettura.LeggiTesto(testo))
            End If
        Catch ex As Exception
            log.ScriviLog("InviaLetturaAsincrona: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RilasciaTastiera()

            If disposing Then
                If _spellCheckTimer IsNot Nothing Then
                    _spellCheckTimer.Stop()
                    _spellCheckTimer.Dispose()
                End If
                If _autoCapTimer IsNot Nothing Then
                    _autoCapTimer.Stop()
                    _autoCapTimer.Dispose()
                End If

                If _timerLettura IsNot Nothing Then
                    _timerLettura.Stop()
                    _timerLettura.Dispose()
                End If
                If _spellCheckMenu IsNot Nothing Then
                    _spellCheckMenu.abbandona()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("Dispose: " & ex.Message)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Public Sub IncollaSicuro()
        Try
            If Clipboard.ContainsText(TextDataFormat.Rtf) OrElse Clipboard.ContainsText(TextDataFormat.Text) Then
                Try
                    SendMessage(Me.Handle, 11, CType(0, IntPtr), IntPtr.Zero)
                    If Not Me.IsDisposed Then
                        _spellCheckTimer.Stop()
                        _autoCapTimer.Stop()
                    End If
                    Me.Paste()
                    InviaLetturaAsincrona("Incollato")
                Catch ex As OutOfMemoryException
                    Dim msgOOM As New WGMessaggi()
                    msgOOM.Messaggio("Memoria insufficiente per incollare tutto il contenuto. L'operazione è stata annullata per evitare blocchi.")
                    msgOOM.ShowDialog()
                Catch ex As Exception
                    log.ScriviLog("Errore incolla sicuro: " & ex.Message)
                Finally
                    SendMessage(Me.Handle, 11, CType(1, IntPtr), IntPtr.Zero)
                    Me.Invalidate()
                    If Not Me.IsDisposed Then
                        If multiriga Then
                            _autoCapTimer.Start()
                        End If
                        _spellCheckTimer.Start()
                    End If
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("Errore generale incolla sicuro: " & ex.Message)
        End Try
    End Sub

    Public Sub InserisciTabella(righe As Integer, colonne As Integer)
        Try
            If Me.ReadOnly Then Return

            Dim rtf As New StringBuilder()
            rtf.Append("{\rtf1\ansi\deff0 ")

            Dim cellWidth As Integer = 2000
            For r As Integer = 0 To righe - 1
                rtf.Append("\trowd \trgaph108 \trleft-108 ")
                For c As Integer = 0 To colonne - 1
                    Dim right As Integer = (c + 1) * cellWidth
                    rtf.Append("\clbrdrt\brdrs\brdrw15\clbrdrl\brdrs\brdrw15\clbrdrb\brdrs\brdrw15\clbrdrr\brdrs\brdrw15")
                    rtf.Append("\cellx" & right.ToString() & " ")
                Next
                For c As Integer = 0 To colonne - 1
                    If r = 0 Then
                        rtf.Append($"\pard\intbl Colonna{c + 1}\cell ")
                    Else
                        rtf.Append("\pard\intbl \cell ")
                    End If
                Next
                rtf.Append("\row ")
            Next
            rtf.Append("}")

            Me.SelectedRtf = rtf.ToString()
            InviaLetturaAsincrona($"Tabella inserita con {righe} righe e {colonne} colonne.")
        Catch ex As Exception
            log.ScriviLog("Errore InserisciTabella: " & ex.Message)
        End Try
    End Sub

    Private Function GestisciNavigazioneTabella(dirRiga As Integer, dirColonna As Integer) As Boolean
        Try
            Dim rigaCorrente As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
            Dim inizioRiga As Integer = Me.GetFirstCharIndexFromLine(rigaCorrente)
            Dim testoRiga As String = ""
            If rigaCorrente < Me.Lines.Length Then testoRiga = Me.Lines(rigaCorrente)

            If Not testoRiga.Contains(vbTab) Then
                Return False
            End If

            Dim colCorrente As Integer = 0
            For i As Integer = inizioRiga To Me.SelectionStart - 1
                If i < Me.TextLength AndAlso Me.Text(i) = vbTab Then
                    colCorrente += 1
                End If
            Next

            Dim nuovaRiga As Integer = rigaCorrente + dirRiga
            Dim nuovaCol As Integer = colCorrente + dirColonna

            If nuovaRiga < 0 OrElse nuovaRiga >= Me.Lines.Length Then
                System.Media.SystemSounds.Beep.Play()
                Return True
            End If

            Dim testoNuovaRiga As String = Me.Lines(nuovaRiga)
            If Not testoNuovaRiga.Contains(vbTab) Then
                InviaLetturaAsincrona("Fine della tabella.")
                Return True
            End If

            Dim celleNuovaRiga As String() = testoNuovaRiga.Split(vbTab)

            If nuovaCol < 0 Then
                System.Media.SystemSounds.Beep.Play()
                Return True
            End If

            If nuovaCol >= celleNuovaRiga.Length Then
                System.Media.SystemSounds.Beep.Play()
                Return True
            End If

            Dim cursoreDest As Integer = Me.GetFirstCharIndexFromLine(nuovaRiga)
            For i As Integer = 0 To nuovaCol - 1
                cursoreDest += celleNuovaRiga(i).Length + 1
            Next

            Me.SelectionStart = cursoreDest
            Me.SelectionLength = 0
            Me.ScrollToCaret()

            Dim testoCella As String = celleNuovaRiga(nuovaCol).Trim()
            If String.IsNullOrEmpty(testoCella) Then testoCella = "Vuota"

            Dim rigaIntestazione As Integer = nuovaRiga
            While rigaIntestazione > 0 AndAlso Me.Lines(rigaIntestazione - 1).Contains(vbTab)
                rigaIntestazione -= 1
            End While

            Dim nomeColonna As String = ""
            If rigaIntestazione < nuovaRiga Then
                Dim celleIntestazione As String() = Me.Lines(rigaIntestazione).Split(vbTab)
                If nuovaCol < celleIntestazione.Length Then
                    nomeColonna = celleIntestazione(nuovaCol).Trim()
                End If
            End If

            Dim rigaRelativa As Integer = nuovaRiga - rigaIntestazione + 1

            Dim annuncio As String = $"Riga {rigaRelativa}, Colonna {nuovaCol + 1}"
            If dirColonna <> 0 AndAlso Not String.IsNullOrEmpty(nomeColonna) Then
                annuncio &= $", {nomeColonna}"
            End If
            annuncio &= $", {testoCella}"

            InviaLetturaAsincrona(annuncio)
            inLetturaContinua = False
            Return True
        Catch ex As Exception
            log.ScriviLog("Errore GestisciNavigazioneTabella: " & ex.Message)
            Return False
        End Try
    End Function

    Private isCtrlDestroPremuto As Boolean = False

    Private Sub GestioneControlSinistroPremuto()
        Try
            isCtrlSinistroPremuto = True
            InterrompiLetturaContinuaDaControl()
        Catch ex As Exception
            log.ScriviLog("GestioneControlSinistroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub GestioneControlSinistroRilasciato()
        Try
            isCtrlSinistroPremuto = False
        Catch ex As Exception
            log.ScriviLog("GestioneControlSinistroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub GestioneControlDestroPremuto()
        Try
            isCtrlDestroPremuto = True
            InterrompiLetturaContinuaDaControl()
        Catch ex As Exception
            log.ScriviLog("GestioneControlDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub InterrompiLetturaContinuaDaControl()
        Try
            If _altDestraPremuto OrElse (tastiera IsNot Nothing AndAlso tastiera.AltDestroAttualmentePremuto) Then Return
            If Not _isHookActive Then Return
            If Not Me.Focused AndAlso Not Me.ContainsFocus Then Return
            InterrompiLetturaContinua()
        Catch ex As Exception
            log.ScriviLog("InterrompiLetturaContinuaDaControl: " & ex.Message)
        End Try
    End Sub

    Public Sub InterrompiLetturaContinua()
        Try
            If Not inLetturaContinua Then Return
            inLetturaContinua = False
            SyncLock _transizioneLetturaLock
                _transizioneGestita = True
            End SyncLock
            If Not Me.IsDisposed Then _timerLettura.Stop()
            If lettura IsNot Nothing Then lettura.FermaVoce()
        Catch ex As Exception
            log.ScriviLog("InterrompiLetturaContinua: " & ex.Message)
        End Try
    End Sub

    Private Sub GestioneControlDestroRilasciato()
        Try
            isCtrlDestroPremuto = False
        Catch ex As Exception
            log.ScriviLog("GestioneControlDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_KeyDown(ByRef tasti As Keys)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            Dim baseKeyGlobale As Keys = tasti And Keys.KeyCode
            If _altDestraPremuto OrElse tastiera.AltDestroAttualmentePremuto Then
                _altDestroUsato = True
            End If

            If Not Me.Focused OrElse Not _isHookActive Then Return

            Dim baseKeyForCheck As Keys = tasti And Keys.KeyCode

            If baseKeyForCheck <> Keys.Up AndAlso baseKeyForCheck <> Keys.Down Then
                _posizioneVirtualeVerticale = 0
            End If

            If baseKeyForCheck = Keys.Space AndAlso inLetturaContinua Then
                inLetturaContinua = False
                If Not Me.IsDisposed Then _timerLettura.Stop()
                lettura.FermaVoce()
                tasti = Keys.None
            End If

            If IsWordMode Then Return

            If isCtrlSinistroPremuto Then
                Select Case baseKeyForCheck
                    Case Keys.A
                        Me.SelectAll()
                        InviaLetturaAsincrona("Selezionato tutto")
                        tasti = Keys.None

                    Case Keys.C
                        If Me.SelectedText <> "" Then
                            Me.Copy()
                            InviaLetturaAsincrona("Copiato negli appunti")
                        Else
                            Dim messaggio As New WGMessaggi()
                            messaggio.Messaggio("Nessun testo selezionato")
                            messaggio.ShowDialog()
                        End If
                        tasti = Keys.None

                    Case Keys.X
                        If Not Me.ReadOnly Then
                            If Me.SelectedText <> "" Then
                                Me.Cut()
                                InviaLetturaAsincrona("Tagliato")
                            Else
                                Dim messaggio As New WGMessaggi()
                                messaggio.Messaggio("Nessun testo selezionato")
                                messaggio.ShowDialog()
                            End If
                        Else
                            Dim msg As New WGMessaggi()
                            msg.Messaggio("Il campo è di sola lettura")
                            msg.ShowDialog()
                        End If
                        tasti = Keys.None

                    Case Keys.V
                        If Not Me.ReadOnly Then
                            IncollaSicuro()
                        Else
                            Dim msg As New WGMessaggi()
                            msg.Messaggio("Il campo è di sola lettura")
                            msg.ShowDialog()
                        End If
                        tasti = Keys.None

                    Case Keys.Z
                        If Not Me.ReadOnly AndAlso Me.CanUndo Then
                            Me.Undo()
                            InviaLetturaAsincrona("Annullato")
                        End If
                        tasti = Keys.None

                    Case Keys.Y
                        If Not Me.ReadOnly AndAlso Me.CanRedo Then
                            Me.Redo()
                            InviaLetturaAsincrona("Ripristinato")
                        End If
                        tasti = Keys.None

                    Case Keys.G
                        If Not Me.ReadOnly AndAlso Me.SelectionFont IsNot Nothing Then
                            Dim f As Font = Me.SelectionFont
                            Dim newStyle As FontStyle = If(f.Bold, f.Style And Not FontStyle.Bold, f.Style Or FontStyle.Bold)
                            Me.SelectionFont = New Font(f, newStyle)
                            InviaLetturaAsincrona(If(f.Bold, "Grassetto rimosso", "Grassetto applicato"))
                        End If
                        tasti = Keys.None

                    Case Keys.S
                        If Not Me.ReadOnly AndAlso Me.SelectionFont IsNot Nothing Then
                            Dim f As Font = Me.SelectionFont
                            Dim newStyle As FontStyle = If(f.Underline, f.Style And Not FontStyle.Underline, f.Style Or FontStyle.Underline)
                            Me.SelectionFont = New Font(f, newStyle)
                            InviaLetturaAsincrona(If(f.Underline, "Sottolineatura rimossa", "Sottolineato applicato"))
                        End If
                        tasti = Keys.None

                    Case Keys.I
                        LeggiInfoFormattazione()
                        tasti = Keys.None

                    Case Keys.H
                        If isShiftPremuto Then
                            If Me.ReadOnly Then SpostatiAIntestazione(False)
                        Else
                            If Me.ReadOnly Then SpostatiAIntestazione(True)
                        End If
                        tasti = Keys.None
                End Select
            End If

            Dim strCarattere As String = ConvertiTastoInCarattere(tasti)

            GestisciTasto(tasti, strCarattere)

            If tasti = Keys.None Then Return

            If Not String.IsNullOrEmpty(strCarattere) AndAlso Not Char.IsControl(strCarattere(0)) Then
                Dim charInserito As Char = strCarattere(0)

                If isShiftPremuto AndAlso Char.IsLetter(charInserito) Then
                    charInserito = Char.ToUpper(charInserito)
                End If

                If Not Me.ReadOnly Then
                    Me.SelectedText = charInserito.ToString()
                    GestisciCharMessaggio(charInserito)
                End If

                tasti = Keys.None
            End If
        Catch ex As Exception
            log.ScriviLog("Tastiera KeyDown Error: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciTasto(ByRef tasto As Keys, caratterePremuto As String)
        Try
            Dim isAltDestra As Boolean = (_altDestraPremuto OrElse tastiera.AltDestroAttualmentePremuto) AndAlso WGTastiera.TastieraItalianaAttiva()
            Dim baseKey As Keys = tasto And Keys.KeyCode
            Dim isCtrl As Boolean = isCtrlSinistroPremuto

            If isAltDestra Then
                Dim insertChar As String = ""

                If baseKey = Keys.Oem3 OrElse caratterePremuto = "ò" OrElse caratterePremuto = "Ò" Then
                    insertChar = "@"
                ElseIf baseKey = Keys.Oem7 OrElse caratterePremuto = "à" OrElse caratterePremuto = "À" Then
                    insertChar = "#"
                ElseIf baseKey = Keys.Oem5 OrElse caratterePremuto = "è" OrElse caratterePremuto = "È" Then
                    insertChar = If(isShiftPremuto, "{", "[")
                ElseIf baseKey = Keys.Oemplus OrElse caratterePremuto = "+" Then
                    insertChar = If(isShiftPremuto, "}", "]")
                ElseIf baseKey = Keys.E OrElse caratterePremuto = "e" OrElse caratterePremuto = "E" Then
                    insertChar = "€"
                End If

                If wgcontrols.WGTastiera.LoggingVerboso Then
                End If

                If insertChar <> "" Then
                    _altDestroUsato = True
                    If Not Me.ReadOnly Then
                        Me.SelectedText = insertChar
                        GestisciCharMessaggio(insertChar(0))
                    End If
                    tasto = Keys.None
                    Return
                End If
            End If

            Select Case baseKey
                Case Keys.Left
                    inLetturaContinua = False
                    If isCtrl AndAlso isShiftPremuto Then
                        GestisciMovimentoOrizzontaleManuale(False, True, True)
                    ElseIf isCtrl Then
                        GestisciMovimentoOrizzontaleManuale(False, True, False)
                    ElseIf isShiftPremuto Then
                        GestisciMovimentoOrizzontaleManuale(False, False, True)
                    Else
                        GestisciMovimentoOrizzontaleManuale(False, False, False)
                    End If
                    tasto = Keys.None

                Case Keys.Right
                    inLetturaContinua = False
                    If isCtrl AndAlso isShiftPremuto Then
                        GestisciMovimentoOrizzontaleManuale(True, True, True)
                    ElseIf isCtrl Then
                        GestisciMovimentoOrizzontaleManuale(True, True, False)
                    ElseIf isShiftPremuto Then
                        GestisciMovimentoOrizzontaleManuale(True, False, True)
                    Else
                        GestisciMovimentoOrizzontaleManuale(True, False, False)
                    End If
                    tasto = Keys.None

                Case Keys.Up
                    inLetturaContinua = False
                    If isCtrl AndAlso isShiftPremuto Then
                        GestisciMovimentoVerticale(-1, True)
                    ElseIf isCtrl Then
                        GestisciMovimentoVerticale(-1, False)
                    ElseIf isShiftPremuto Then
                        GestisciMovimentoVerticale(-1, True)
                    Else
                        GestisciMovimentoVerticale(-1, False)
                    End If
                    tasto = Keys.None

                Case Keys.Down
                    inLetturaContinua = False
                    If isCtrl AndAlso isShiftPremuto Then
                        GestisciMovimentoVerticale(1, True)
                    ElseIf isCtrl Then
                        GestisciMovimentoVerticale(1, False)
                    ElseIf isShiftPremuto Then
                        GestisciMovimentoVerticale(1, True)
                    Else
                        GestisciMovimentoVerticale(1, False)
                    End If
                    tasto = Keys.None

                Case Keys.Tab
                    If isShiftPremuto Then
                        If GestisciNavigazioneTabella(0, -1) Then tasto = Keys.None
                    Else
                        If GestisciNavigazioneTabella(0, 1) Then tasto = Keys.None
                    End If

                Case Keys.Home
                    inLetturaContinua = False
                    If multiriga Then
                        Dim riga As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
                        Me.SelectionStart = Me.GetFirstCharIndexFromLine(riga)
                        Me.ScrollToCaret()
                        InviaLetturaAsincrona("Inizio riga")
                    End If
                    tasto = Keys.None

                Case Keys.End
                    inLetturaContinua = False
                    If multiriga Then
                        Dim riga As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
                        Dim primoCharProssima = Me.GetFirstCharIndexFromLine(riga + 1)

                        If primoCharProssima = -1 Then primoCharProssima = Me.TextLength
                        If primoCharProssima > 0 AndAlso Me.Text(primoCharProssima - 1) = vbLf Then
                            Me.SelectionStart = primoCharProssima - 1
                        Else
                            Me.SelectionStart = primoCharProssima
                        End If

                        Me.ScrollToCaret()
                        InviaLetturaAsincrona("Fine riga")
                    End If
                    tasto = Keys.None

                Case Keys.PageUp
                    inLetturaContinua = False
                    If multiriga Then GestisciPagina(-1)
                    tasto = Keys.None

                Case Keys.PageDown
                    inLetturaContinua = False
                    If multiriga Then GestisciPagina(1)
                    tasto = Keys.None

                Case Keys.Back
                    inLetturaContinua = False
                    If Not Me.ReadOnly Then
                        Dim pronunciaSegni As Boolean = True

                        If Me.SelectionLength > 0 Then
                            Dim testoEliminato As String = Me.SelectedText
                            Me.SelectedText = ""
                            If Not String.IsNullOrEmpty(testoEliminato) Then
                                If testoEliminato.Length = 1 Then
                                    lettura.LeggiTesto(TraduciCarattere(testoEliminato(0), SimboloDaLeggereInScritturaCampo(testoEliminato(0))))
                                Else
                                    lettura.LeggiTesto(testoEliminato)
                                End If
                            End If
                        ElseIf Me.SelectionStart > 0 Then
                            Dim charIndex As Integer = Me.SelectionStart - 1
                            Me.SelectionStart = charIndex
                            Me.SelectionLength = 1
                            Dim c As String = Me.SelectedText
                            Me.SelectedText = ""
                            If c.Length > 0 Then
                                lettura.LeggiTesto(TraduciCarattere(c(0), SimboloDaLeggereInScritturaCampo(c(0))))
                            End If
                        End If
                    End If
                    tasto = Keys.None

                Case Keys.Delete
                    inLetturaContinua = False
                    If Not Me.ReadOnly Then
                        Dim pronunciaSegni As Boolean = True

                        If Me.SelectionLength > 0 Then
                            Dim testoEliminato As String = Me.SelectedText
                            Me.SelectedText = ""
                            If Not String.IsNullOrEmpty(testoEliminato) Then
                                If testoEliminato.Length = 1 Then
                                    Dim cDel As Char = testoEliminato(0)
                                    If Char.IsLetter(cDel) AndAlso Char.IsUpper(cDel) Then
                                        lettura.LeggiTesto(cDel.ToString() & " maiuscola")
                                    Else
                                        lettura.LeggiTesto(TraduciCarattere(cDel, SimboloDaLeggereInScritturaCampo(cDel)))
                                    End If
                                Else
                                    lettura.LeggiTesto(testoEliminato)
                                End If
                            End If
                        ElseIf Me.SelectionStart < Me.TextLength Then
                            Me.SelectionLength = 1
                            Dim c As String = Me.SelectedText
                            Me.SelectedText = ""
                            If c.Length > 0 Then
                                Dim cDel As Char = c(0)
                                If Char.IsLetter(cDel) AndAlso Char.IsUpper(cDel) Then
                                    lettura.LeggiTesto(cDel.ToString() & " maiuscola")
                                Else
                                    lettura.LeggiTesto(TraduciCarattere(cDel, SimboloDaLeggereInScritturaCampo(cDel)))
                                End If
                            End If
                        End If
                    End If
                    tasto = Keys.None

                Case Keys.F8
                    If inLetturaContinua Then
                        inLetturaContinua = False
                        If Not Me.IsDisposed Then _timerLettura.Stop()
                        lettura.FermaVoce()
                    Else
                        LeggiTuttoDaCursore()
                    End If
                    tasto = Keys.None

                Case Keys.F6
                    inLetturaContinua = False
                    LeggeRigaCorrente()
                    tasto = Keys.None

                Case Keys.F7
                    inLetturaContinua = False
                    If Not Me.ReadOnly AndAlso multiriga Then
                        EseguiControlloOrtografico(Nothing, EventArgs.Empty)
                    End If
                    tasto = Keys.None

                Case Keys.Enter
                    If isCtrl Then
                        Try
                            Dim indiceRiga As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
                            Dim rigaCorrenteTesto As String = If(indiceRiga >= 0 AndAlso indiceRiga < Me.Lines.Length, Me.Lines(indiceRiga), "").Trim()
                            If rigaCorrenteTesto.StartsWith("http://") OrElse rigaCorrenteTesto.StartsWith("https://") OrElse rigaCorrenteTesto.StartsWith("www.") Then
                                Dim urlDaAprire As String = rigaCorrenteTesto
                                If urlDaAprire.StartsWith("www.") Then urlDaAprire = "https://" & urlDaAprire
                                WGImpostazioniComuni.ApriLinkConBrowserPreferito(urlDaAprire)
                                InviaLetturaAsincrona("Apertura link nel browser")
                            Else
                                InviaLetturaAsincrona("La riga corrente non è un link")
                            End If
                        Catch exLink As Exception
                            log.ScriviLog("Apertura link Ctrl+Invio: " & exLink.Message)
                        End Try
                        tasto = Keys.None
                        Return
                    End If
                    inLetturaContinua = False
                    If (DateTime.Now - tempoAvvio).TotalMilliseconds >= 200 Then
                        If obbligatorio AndAlso Not stringavalida(Me.Text) Then
                            Dim messaggio As New WGMessaggi()
                            messaggio.Messaggio("Inserire un valore è obbligatorio")
                            messaggio.ShowDialog()
                        ElseIf Not Me.ReadOnly AndAlso multiriga Then
                            Me.SelectedText = vbLf
                            InviaLetturaAsincrona("Nuova riga")
                        ElseIf Not Me.ReadOnly Then
                            RaiseEvent RichiestaChiusura(Me, EventArgs.Empty)
                        Else
                            RaiseEvent InvioInSolaLettura(Me, EventArgs.Empty)
                        End If
                    End If
                    tasto = Keys.None
            End Select
        Catch ex As Exception
            log.ScriviLog("Errore GestisciTasto: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaGrassetto()
        Try
            If Me.SelectionFont IsNot Nothing Then
                Dim f As Font = Me.SelectionFont
                Dim newStyle As FontStyle = If(f.Bold, f.Style And Not FontStyle.Bold, f.Style Or FontStyle.Bold)
                Me.SelectionFont = New Font(f, newStyle)
                InviaLetturaAsincrona(If(f.Bold, "Grassetto rimosso", "Grassetto applicato"))
            End If
        Catch ex As Exception
            log.ScriviLog("ApplicaGrassetto: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaCorsivo()
        Try
            If Me.SelectionFont IsNot Nothing Then
                Dim f As Font = Me.SelectionFont
                Dim newStyle As FontStyle = If(f.Italic, f.Style And Not FontStyle.Italic, f.Style Or FontStyle.Italic)
                Me.SelectionFont = New Font(f, newStyle)
                InviaLetturaAsincrona(If(f.Italic, "Corsivo rimosso", "Corsivo applicato"))
            End If
        Catch ex As Exception
            log.ScriviLog("ApplicaCorsivo: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaSottolineato()
        Try
            If Me.SelectionFont IsNot Nothing Then
                Dim f As Font = Me.SelectionFont
                Dim newStyle As FontStyle = If(f.Underline, f.Style And Not FontStyle.Underline, f.Style Or FontStyle.Underline)
                Me.SelectionFont = New Font(f, newStyle)
                InviaLetturaAsincrona(If(f.Underline, "Sottolineatura rimossa", "Sottolineato applicato"))
            End If
        Catch ex As Exception
            log.ScriviLog("ApplicaSottolineato: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaBarrato()
        Try
            If Me.SelectionFont IsNot Nothing Then
                Dim f As Font = Me.SelectionFont
                Dim newStyle As FontStyle = If(f.Strikeout, f.Style And Not FontStyle.Strikeout, f.Style Or FontStyle.Strikeout)
                Me.SelectionFont = New Font(f, newStyle)
                InviaLetturaAsincrona(If(f.Strikeout, "Barrato rimosso", "Barrato applicato"))
            End If
        Catch ex As Exception
            log.ScriviLog("ApplicaBarrato: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviFormattazione()
        Try
            Me.SelectionFont = New Font(FontPredefinitoCampo, DimensionePredefinitaCampo, FontStyle.Regular)
            Me.SelectionColor = WGTema.Testo
            Me.SelectionBackColor = Color.Transparent
            InviaLetturaAsincrona("Formattazione rimossa")
        Catch ex As Exception
            log.ScriviLog("RimuoviFormattazione: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaAllineamento(align As String)
        Try
            Select Case align.ToLower()
                Case "left"
                    Me.SelectionAlignment = HorizontalAlignment.Left
                    InviaLetturaAsincrona("Allineato a sinistra")
                Case "center"
                    Me.SelectionAlignment = HorizontalAlignment.Center
                    InviaLetturaAsincrona("Allineato al centro")
                Case "right"
                    Me.SelectionAlignment = HorizontalAlignment.Right
                    InviaLetturaAsincrona("Allineato a destra")
            End Select
        Catch ex As Exception
            log.ScriviLog("ImpostaAllineamento: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuFont()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Arial", Sub() Me.SelectionFont = New Font("Arial", Me.SelectionFont.Size, Me.SelectionFont.Style))
            m.aggiungi("Times New Roman", Sub() Me.SelectionFont = New Font("Times New Roman", Me.SelectionFont.Size, Me.SelectionFont.Style))
            m.aggiungi("Courier New", Sub() Me.SelectionFont = New Font("Courier New", Me.SelectionFont.Size, Me.SelectionFont.Style))
            m.aggiungi("Verdana", Sub() Me.SelectionFont = New Font("Verdana", Me.SelectionFont.Size, Me.SelectionFont.Style))
            m.avvia("Tipo carattere", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuFont: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuDimensioni()
        Try
            Dim m As New WGMenu()
            For i As Integer = DimensioneMinimaMenuFontCampo To DimensioneMassimaMenuFontCampo Step PassoMenuFontCampo
                Dim size = i
                m.aggiungi(size.ToString(), Sub() Me.SelectionFont = New Font(Me.SelectionFont.FontFamily, size, Me.SelectionFont.Style))
            Next
            m.avvia("Dimensione carattere", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuDimensioni: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaColoreFont(colStr As String)
        Try
            Dim c As Color = Color.FromName(colStr)
            If c.ToArgb() = 0 Then c = ColorTranslator.FromHtml(colStr)
            Me.SelectionColor = c
            InviaLetturaAsincrona("Colore impostato")
        Catch ex As Exception
            log.ScriviLog("ImpostaColoreFont: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaEvidenziatore(colStr As String)
        Try
            Dim c As Color = Color.FromName(colStr)
            If c.ToArgb() = 0 Then c = ColorTranslator.FromHtml(colStr)
            Me.SelectionBackColor = c
            InviaLetturaAsincrona("Colore evidenziatore impostato")
        Catch ex As Exception
            log.ScriviLog("ImpostaEvidenziatore: " & ex.Message)
        End Try
    End Sub

    Public Sub CercaTesto(testo As String)
        Try
            Dim pos As Integer = Me.Find(testo, Me.SelectionStart + Me.SelectionLength, RichTextBoxFinds.None)
            If pos = -1 Then
                pos = Me.Find(testo, 0, RichTextBoxFinds.None)
            End If
            If pos <> -1 Then
                Me.Select(pos, testo.Length)
                Me.ScrollToCaret()
                InviaLetturaAsincrona("Trovato " & testo)
            Else
                InviaLetturaAsincrona("Testo non trovato")
            End If
        Catch ex As Exception
            log.ScriviLog("CercaTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub SostituisciTesto(cerca As String, sost As String)
        Try
            If Me.SelectedText.Equals(cerca, StringComparison.OrdinalIgnoreCase) Then
                Me.SelectedText = sost
                InviaLetturaAsincrona("Sostituito")
            Else
                CercaTesto(cerca)
            End If
        Catch ex As Exception
            log.ScriviLog("SostituisciTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub SostituisciTestoTutto(cerca As String, sost As String)
        Try
            Dim count As Integer = 0
            Dim pos As Integer = Me.Find(cerca, 0, RichTextBoxFinds.None)
            While pos <> -1
                Me.Select(pos, cerca.Length)
                Me.SelectedText = sost
                count += 1
                pos = Me.Find(cerca, pos + sost.Length, RichTextBoxFinds.None)
            End While
            InviaLetturaAsincrona("Sostituite " & count & " occorrenze")
        Catch ex As Exception
            log.ScriviLog("SostituisciTestoTutto: " & ex.Message)
        End Try
    End Sub

    Public Sub LeggiInfoFormattazione()
        Try
            If Me.SelectionFont IsNot Nothing Then
                Dim f As Font = Me.SelectionFont
                Dim info As String = "Tipo carattere " & f.FontFamily.Name & " dimensione " & f.SizeInPoints.ToString()
                If f.Bold Then info &= " grassetto"
                If f.Italic Then info &= " corsivo"
                If f.Underline Then info &= " sottolineato"
                If f.Strikeout Then info &= " barrato"
                Dim colorHex As String = System.Drawing.ColorTranslator.ToHtml(Me.SelectionColor)
                info &= " colore " & colorHex
                InviaLetturaAsincrona(info)
            Else
                InviaLetturaAsincrona("Formattazione mista")
            End If
        Catch ex As Exception
            log.ScriviLog("LeggiInfoFormattazione Error: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Inserisce un carattere dalla tastiera a schermo e lo annuncia. L'annuncio passa da
    ''' GestisciCharMessaggio, la stessa strada della tastiera fisica, ma solo se l'impostazione
    ''' letta da EchoDigitazioneCampo ha un valore riconosciuto: se e' vuota nessun ramo di quella
    ''' funzione corrisponde e non verrebbe detto nulla, in silenzio. In quel caso si ricade sulla
    ''' chiave echo_digitazione, la stessa che usa il campo Word, dove la lettura funziona.
    ''' </summary>
    Public Sub InserisciCarattereConLettura(c As Char)
        Try
            If Me.ReadOnly Then Return
            If c = vbCr OrElse c = vbLf Then Return

            ' Il percorso della tastiera fisica solleva KeyPress prima di inserire, e chi ascolta
            ' quell'evento reagisce di conseguenza. Saltarlo rendeva le due strade diverse proprio
            ' nel punto che conta.
            Dim carattereDaInserire As Char = c
            Try
                Dim argomentiTasto As New KeyPressEventArgs(carattereDaInserire)
                MyBase.OnKeyPress(argomentiTasto)
                If argomentiTasto.Handled Then Return
                carattereDaInserire = argomentiTasto.KeyChar
            Catch exKeyPress As Exception
                log.ScriviLog("InserisciCarattereConLettura (KeyPress): " & exKeyPress.Message)
            End Try

            Me.SelectedText = carattereDaInserire.ToString()
            GestisciCharMessaggio(carattereDaInserire)
        Catch ex As Exception
            log.ScriviLog("InserisciCarattereConLettura: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Inserisce il carattere senza pronunciarlo. Serve alla tastiera a schermo, che annuncia
    ''' da se' lettere e parole: lasciando l'annuncio anche al campo, la seconda voce interrompe
    ''' la prima e le lettere ripetute non si sentono.
    ''' </summary>
    Public Sub InserisciCarattereSenzaLettura(c As Char)
        Try
            If Me.ReadOnly Then Return
            If c = vbCr OrElse c = vbLf Then Return

            Dim carattereDaInserire As Char = c
            Try
                Dim argomentiTasto As New KeyPressEventArgs(carattereDaInserire)
                MyBase.OnKeyPress(argomentiTasto)
                If argomentiTasto.Handled Then Return
                carattereDaInserire = argomentiTasto.KeyChar
            Catch exKeyPress As Exception
                log.ScriviLog("InserisciCarattereSenzaLettura (KeyPress): " & exKeyPress.Message)
            End Try

            Me.SelectedText = carattereDaInserire.ToString()
        Catch ex As Exception
            log.ScriviLog("InserisciCarattereSenzaLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaCarattereDiRipiego(c As Char)
        Try
            ' Si legge la stessa chiave usata dal campo Word, dove la lettura funziona. La
            ' sorgente propria del campo puo' valere "2", cioe' solo parole, e in quel caso la
            ' lettera non verrebbe mai annunciata pur essendo scritta: e' questa la differenza di
            ' comportamento fra i campi e Word. Resta come ripiego se la chiave non e' impostata.
            Dim impostazione As String = ""
            Try
                Dim configurazione As New WGConfig()
                impostazione = configurazione.LeggiValore("echo_digitazione")
            Catch exConfig As Exception
                log.ScriviLog("AnnunciaCarattereDiRipiego (config): " & exConfig.Message)
            End Try

            If String.IsNullOrWhiteSpace(impostazione) Then
                Try
                    impostazione = EchoDigitazioneCampo
                Catch exImpostazione As Exception
                    log.ScriviLog("AnnunciaCarattereDiRipiego (impostazione): " & exImpostazione.Message)
                End Try
            End If

            If impostazione = "0" Then Return

            Dim leggiCaratteri As Boolean = (impostazione = "1" OrElse impostazione = "3" OrElse String.IsNullOrWhiteSpace(impostazione))
            Dim leggiParole As Boolean = (impostazione = "2" OrElse impostazione = "3" OrElse String.IsNullOrWhiteSpace(impostazione))
            Dim fineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))

            If fineParola AndAlso leggiParole Then
                Dim parola As String = UltimaParolaPrimaDelCursore()
                If Not String.IsNullOrWhiteSpace(parola) Then
                    InviaLetturaAsincrona(parola)
                    Return
                End If
            End If

            If Not leggiCaratteri Then Return

            Dim tradotto As String = TraduciCarattere(c, True)
            If String.IsNullOrWhiteSpace(tradotto) Then tradotto = c.ToString()
            If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then tradotto = c.ToString() & " maiuscola"
            InviaLetturaAsincrona(tradotto)
        Catch ex As Exception
            log.ScriviLog("AnnunciaCarattereDiRipiego: " & ex.Message)
        End Try
    End Sub

    Private Function UltimaParolaPrimaDelCursore() As String
        Try
            Dim testo As String = Me.Text
            Dim cursore As Integer = Me.SelectionStart
            If cursore <= 0 OrElse cursore > testo.Length Then Return ""

            Dim i As Integer = cursore - 1
            If i >= 0 AndAlso i < testo.Length AndAlso (testo(i) = " "c OrElse Char.IsPunctuation(testo(i))) Then i -= 1
            While i >= 0 AndAlso Char.IsWhiteSpace(testo(i))
                i -= 1
            End While

            Dim fine As Integer = i + 1
            While i >= 0 AndAlso Not Char.IsWhiteSpace(testo(i)) AndAlso Not Char.IsPunctuation(testo(i))
                i -= 1
            End While

            Dim inizio As Integer = i + 1
            If fine > inizio Then Return testo.Substring(inizio, fine - inizio)
            Return ""
        Catch ex As Exception
            log.ScriviLog("UltimaParolaPrimaDelCursore: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub GestisciCharMessaggio(c As Char)
        Try
            If Me.ReadOnly Then Return
            If c = vbCr OrElse c = vbLf Then Return

            Dim echoSetting As String = EchoDigitazioneCampo

            Dim pronunciaSegni As Boolean = True

            If echoSetting = "0" Then Return

            Dim isFineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))
            Dim wordDaLeggere As String = ""
            Dim spazioDopoSpazio As Boolean = False

            If c = " "c Then
                Dim txtSpc As String = Me.Text
                Dim cursoreSpc As Integer = Me.SelectionStart
                Dim prevIdx As Integer = cursoreSpc - 2
                If prevIdx < 0 Then
                    spazioDopoSpazio = True
                ElseIf prevIdx < txtSpc.Length AndAlso Char.IsWhiteSpace(txtSpc(prevIdx)) Then
                    spazioDopoSpazio = True
                End If
            End If

            If ((echoSetting = "2" AndAlso isFineParola) OrElse (echoSetting = "3" AndAlso isFineParola)) AndAlso Not spazioDopoSpazio Then
                Dim txt As String = Me.Text
                Dim cursore As Integer = Me.SelectionStart
                If cursore > 0 Then
                    Dim i As Integer = cursore - 1
                    If i >= 0 AndAlso txt(i) = c Then i -= 1
                    While i >= 0 AndAlso Char.IsWhiteSpace(txt(i))
                        i -= 1
                    End While
                    Dim endWord As Integer = i + 1
                    While i >= 0 AndAlso Not Char.IsWhiteSpace(txt(i)) AndAlso Not Char.IsPunctuation(txt(i))
                        i -= 1
                    End While
                    Dim startWord As Integer = i + 1
                    If endWord > startWord Then
                        wordDaLeggere = txt.Substring(startWord, endWord - startWord)
                    End If
                End If
            End If

            If echoSetting = "3" Then
                If spazioDopoSpazio Then
                    InviaLetturaAsincrona("spazio")
                    Return
                End If

                Dim nomeSimbolo As String = TraduciCarattere(c, SimboloDaLeggereInScritturaCampo(c))
                If c = "@"c Then nomeSimbolo = "chiocciola"
                If c = "#"c Then nomeSimbolo = "cancelletto"
                If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then nomeSimbolo = c.ToString() & " maiuscola"

                Dim isSimbolo As Boolean = WGTastiera.SimboloDaLeggereSubito(c)
                Dim isFineParolaPerSimboli As Boolean = (isFineParola OrElse isSimbolo)

                If isFineParolaPerSimboli AndAlso wordDaLeggere.Length > 1 Then
                    If c = " "c Then
                        InviaLetturaAsincrona(wordDaLeggere & " spazio")
                    ElseIf isSimbolo Then
                        InviaLetturaAsincrona(wordDaLeggere & " " & nomeSimbolo)
                    Else
                        If pronunciaSegni Then
                            InviaLetturaAsincrona(wordDaLeggere & " " & TraduciCarattere(c, True))
                        Else
                            InviaLetturaAsincrona(wordDaLeggere & c.ToString())
                        End If
                    End If
                Else
                    InviaLetturaAsincrona(nomeSimbolo)
                End If
            Else
                If echoSetting = "2" AndAlso spazioDopoSpazio Then Return

                If c = "@"c Then
                    InviaLetturaAsincrona("chiocciola")
                ElseIf c = "#"c Then
                    InviaLetturaAsincrona("cancelletto")
                ElseIf Char.IsLetter(c) AndAlso Char.IsUpper(c) Then
                    InviaLetturaAsincrona(c.ToString() & " maiuscola")
                Else
                    If echoSetting = "1" Then
                        InviaLetturaAsincrona(TraduciCarattere(c, SimboloDaLeggereInScritturaCampo(c)))
                    ElseIf echoSetting = "2" Then
                        If isFineParola AndAlso wordDaLeggere.Length > 0 Then
                            If Char.IsPunctuation(c) Then
                                If pronunciaSegni Then
                                    InviaLetturaAsincrona(wordDaLeggere & " " & TraduciCarattere(c, True))
                                Else
                                    InviaLetturaAsincrona(wordDaLeggere & c.ToString())
                                End If
                            Else
                                InviaLetturaAsincrona(wordDaLeggere)
                            End If
                        End If
                    End If
                End If
            End If

        Catch ex As Exception
            log.ScriviLog("Errore GestisciCharMessaggio: " & ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraSoloCaratteri()
        Try
            impostazioniComuni.ImpostaEchoSoloCaratteri()
        Catch ex As Exception
            log.ScriviLog("ConfiguraSoloCaratteri: " & ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraSoloParole()
        Try
            impostazioniComuni.ImpostaEchoSoloParole()
        Catch ex As Exception
            log.ScriviLog("ConfiguraSoloParole: " & ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraCaratteriEParole()
        Try
            impostazioniComuni.ImpostaEchoCaratteriEParole()
        Catch ex As Exception
            log.ScriviLog("ConfiguraCaratteriEParole: " & ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraNessuno()
        Try
            impostazioniComuni.ImpostaEchoNessuno()
        Catch ex As Exception
            log.ScriviLog("ConfiguraNessuno: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Movimenti richiamabili dall'esterno per i gesti a tocco. Passano dagli stessi motori usati
    ''' dalle frecce, cosi' annunci e comportamento restano identici a quelli da tastiera. Servono
    ''' perche' simulare i tasti con SendKeys non permette di distinguere il movimento per parola:
    ''' quel percorso richiede il Control fisicamente premuto, cosa impossibile durante un gesto.
    ''' </summary>
    Public Sub SpostaCarattereAvantiPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoOrizzontaleManuale(True, False, False)
        Catch ex As Exception
            log.ScriviLog("SpostaCarattereAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCarattereIndietroPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoOrizzontaleManuale(False, False, False)
        Catch ex As Exception
            log.ScriviLog("SpostaCarattereIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParolaAvantiPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoOrizzontaleManuale(True, True, False)
        Catch ex As Exception
            log.ScriviLog("SpostaParolaAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParolaIndietroPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoOrizzontaleManuale(False, True, False)
        Catch ex As Exception
            log.ScriviLog("SpostaParolaIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaRigaGiuPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoVerticale(1, False)
        Catch ex As Exception
            log.ScriviLog("SpostaRigaGiuPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaRigaSuPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False
            GestisciMovimentoVerticale(-1, False)
        Catch ex As Exception
            log.ScriviLog("SpostaRigaSuPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoAvantiPubblica()
        Try
            SpostaParagrafoPubblica(True)
        Catch ex As Exception
            log.ScriviLog("SpostaParagrafoAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoIndietroPubblica()
        Try
            SpostaParagrafoPubblica(False)
        Catch ex As Exception
            log.ScriviLog("SpostaParagrafoIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Porta il cursore all'inizio del paragrafo successivo o precedente e ne legge il testo. Il
    ''' paragrafo e' il tratto compreso fra due ritorni a capo veri: le righe prodotte dal solo
    ''' ritorno a capo automatico non contano, altrimenti il movimento coinciderebbe con quello
    ''' per riga.
    ''' </summary>
    Private Sub SpostaParagrafoPubblica(avanti As Boolean)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            inLetturaContinua = False

            Dim testo As String = Me.Text
            If String.IsNullOrEmpty(testo) Then
                InviaLetturaAsincrona("vuoto")
                Return
            End If

            Dim posizione As Integer = Me.SelectionStart
            If posizione < 0 Then posizione = 0
            If posizione > testo.Length Then posizione = testo.Length

            Dim nuovaPosizione As Integer

            If avanti Then
                Dim prossimoACapo As Integer = testo.IndexOf(ControlChars.Lf, Math.Min(posizione, Math.Max(0, testo.Length - 1)))
                If prossimoACapo < 0 Then
                    InviaLetturaAsincrona("fine testo")
                    Return
                End If
                nuovaPosizione = prossimoACapo + 1
                If nuovaPosizione > testo.Length Then nuovaPosizione = testo.Length
            Else
                Dim inizioCorrente As Integer = testo.LastIndexOf(ControlChars.Lf, Math.Max(0, posizione - 1))
                If inizioCorrente < 0 Then
                    If posizione = 0 Then
                        InviaLetturaAsincrona("inizio testo")
                        Return
                    End If
                    nuovaPosizione = 0
                Else
                    Dim precedente As Integer = testo.LastIndexOf(ControlChars.Lf, Math.Max(0, inizioCorrente - 1))
                    nuovaPosizione = precedente + 1
                End If
            End If

            Me.SelectionStart = nuovaPosizione
            Me.SelectionLength = 0
            Me.ScrollToCaret()

            Dim fineParagrafo As Integer = testo.IndexOf(ControlChars.Lf, nuovaPosizione)
            If fineParagrafo < 0 Then fineParagrafo = testo.Length

            Dim contenuto As String = testo.Substring(nuovaPosizione, fineParagrafo - nuovaPosizione).Trim(ControlChars.Cr)
            If String.IsNullOrWhiteSpace(contenuto) Then
                InviaLetturaAsincrona("paragrafo vuoto")
            Else
                InviaLetturaAsincrona(contenuto)
            End If
        Catch ex As Exception
            log.ScriviLog("SpostaParagrafoPubblica: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciMovimentoOrizzontaleManuale(avanti As Boolean, ctrl As Boolean, shift As Boolean)
        Try
            Dim textLen As Integer = Me.TextLength
            Dim oldLen As Integer = Me.SelectionLength

            Dim cursoreAttuale As Integer
            If oldLen > 0 AndAlso _anchorPoint <> -1 Then
                If Me.SelectionStart = _anchorPoint Then
                    cursoreAttuale = Me.SelectionStart + oldLen
                Else
                    cursoreAttuale = Me.SelectionStart
                End If
            Else
                cursoreAttuale = Me.SelectionStart
            End If

            Dim readBoundary As String = ""
            If avanti AndAlso cursoreAttuale >= textLen Then
                readBoundary = "fine testo"
            ElseIf Not avanti AndAlso cursoreAttuale <= 0 Then
                readBoundary = "inizio testo"
            End If

            If Not shift Then
                _anchorPoint = -1
            ElseIf _anchorPoint = -1 Then
                _anchorPoint = Me.SelectionStart
            End If

            Dim nuovoCursore As Integer = cursoreAttuale

            If ctrl Then
                If avanti Then
                    nuovoCursore = TrovaInizioProssimaParola(cursoreAttuale)
                Else
                    nuovoCursore = TrovaInizioPrecedenteParola(cursoreAttuale)
                End If
            Else
                If avanti Then
                    nuovoCursore = Math.Min(cursoreAttuale + 1, textLen)
                Else
                    nuovoCursore = Math.Max(cursoreAttuale - 1, 0)
                End If
            End If

            Dim isDeselecting As Boolean = False
            If oldLen > 0 Then
                If (cursoreAttuale > _anchorPoint AndAlso nuovoCursore < cursoreAttuale) OrElse
                 (cursoreAttuale < _anchorPoint AndAlso nuovoCursore > cursoreAttuale) Then
                    isDeselecting = True
                End If
            End If

            Dim startRead As Integer = Math.Min(cursoreAttuale, nuovoCursore)
            Dim lenRead As Integer = Math.Abs(cursoreAttuale - nuovoCursore)

            If shift Then
                Dim start As Integer = Math.Min(_anchorPoint, nuovoCursore)
                Dim length As Integer = Math.Abs(_anchorPoint - nuovoCursore)
                Me.Select(start, length)
            Else
                Me.Select(nuovoCursore, 0)
            End If

            Me.ScrollToCaret()

            If shift Then
                If lenRead > 0 AndAlso startRead + lenRead <= Me.TextLength Then
                    Dim testoLetto As String = Me.Text.Substring(startRead, lenRead)
                    Dim prefix As String = If(isDeselecting, "Deselezionato ", "Selezionato ")
                    InviaLetturaAsincrona(prefix & testoLetto)
                ElseIf readBoundary <> "" Then
                    InviaLetturaAsincrona(readBoundary)
                End If
            Else
                If readBoundary <> "" AndAlso oldLen = 0 Then
                    InviaLetturaAsincrona(readBoundary)
                Else
                    If ctrl Then
                        LeggeParolaCorrente(False, nuovoCursore)
                    Else
                        LeggeCarattereCorrente(False, nuovoCursore)
                    End If
                End If
            End If

        Catch ex As Exception
            log.ScriviLog("Errore movimento manuale: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaInizioProssimaParola(startIndex As Integer) As Integer
        Try
            Dim text As String = Me.Text
            Dim len As Integer = text.Length
            If startIndex >= len Then Return len

            Dim i As Integer = startIndex
            While i < len AndAlso Not Char.IsWhiteSpace(text(i)) AndAlso Not Char.IsPunctuation(text(i))
                i += 1
            End While
            While i < len AndAlso (Char.IsWhiteSpace(text(i)) OrElse Char.IsPunctuation(text(i)))
                i += 1
            End While
            Return i
        Catch ex As Exception
            log.ScriviLog("TrovaInizioProssimaParola: " & ex.Message)
            Return startIndex
        End Try
    End Function

    Private Function TrovaInizioPrecedenteParola(startIndex As Integer) As Integer
        Try
            Dim text As String = Me.Text
            If startIndex <= 0 Then Return 0

            Dim i As Integer = startIndex - 1
            While i >= 0 AndAlso (Char.IsWhiteSpace(text(i)) OrElse Char.IsPunctuation(text(i)))
                i -= 1
            End While
            While i >= 0 AndAlso Not Char.IsWhiteSpace(text(i)) AndAlso Not Char.IsPunctuation(text(i))
                i -= 1
            End While
            Return i + 1
        Catch ex As Exception
            log.ScriviLog("TrovaInizioPrecedenteParola: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub GestisciLimiteVerticaleCampo(direzione As Integer)
        Try
            _posizioneVirtualeVerticale = direzione
            System.Media.SystemSounds.Beep.Play()
            InviaLetturaAsincrona(If(direzione < 0, "inizio testo", "fine testo"))
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.GestisciLimiteVerticaleCampo: " & ex.Message)
        End Try
    End Sub

    Private Function GestisciRientroDaConfineVirtuale(direzione As Integer) As Boolean
        Try
            If _posizioneVirtualeVerticale = -1 Then
                If direzione > 0 Then
                    _posizioneVirtualeVerticale = 0
                    LeggeRigaCorrente()
                Else
                    InviaLetturaAsincrona("inizio testo")
                End If
                Return True
            ElseIf _posizioneVirtualeVerticale = 1 Then
                If direzione < 0 Then
                    _posizioneVirtualeVerticale = 0
                    LeggeRigaCorrente()
                Else
                    InviaLetturaAsincrona("fine testo")
                End If
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.GestisciRientroDaConfineVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub GestisciMovimentoVerticale(direzione As Integer, selezione As Boolean)
        Try
            If GestisciRientroDaConfineVirtuale(direzione) Then Return

            Dim oldLen As Integer = Me.SelectionLength
            Dim cursoreAttuale As Integer

            If oldLen > 0 AndAlso _anchorPoint <> -1 Then
                If Me.SelectionStart = _anchorPoint Then
                    cursoreAttuale = Me.SelectionStart + oldLen
                Else
                    cursoreAttuale = Me.SelectionStart
                End If
            Else
                cursoreAttuale = Me.SelectionStart
            End If

            If Not selezione Then
                _anchorPoint = -1
            ElseIf _anchorPoint = -1 Then
                _anchorPoint = Me.SelectionStart
            End If

            Dim rigaCorrente As Integer = Me.GetLineFromCharIndex(cursoreAttuale)
            Dim nuovaRiga As Integer = rigaCorrente + direzione
            Dim totRighe As Integer = Me.GetLineFromCharIndex(Me.TextLength) + 1

            If nuovaRiga < 0 Then
                GestisciLimiteVerticaleCampo(-1)
                Return
            End If

            If nuovaRiga >= totRighe Then
                GestisciLimiteVerticaleCampo(1)
                Return
            End If

            Dim inizioRigaCorr As Integer = Me.GetFirstCharIndexFromLine(rigaCorrente)
            Dim offsetColonna As Integer = cursoreAttuale - inizioRigaCorr

            Dim inizioNuovaRiga As Integer = Me.GetFirstCharIndexFromLine(nuovaRiga)
            Dim lunghNuovaRiga As Integer = 0

            Dim inizioRigaSucc As Integer = Me.GetFirstCharIndexFromLine(nuovaRiga + 1)
            If inizioRigaSucc = -1 Then
                lunghNuovaRiga = Me.TextLength - inizioNuovaRiga
            Else
                lunghNuovaRiga = inizioRigaSucc - inizioNuovaRiga - 1
            End If
            If lunghNuovaRiga < 0 Then lunghNuovaRiga = 0

            Dim nuovoIndex As Integer = inizioNuovaRiga + Math.Min(offsetColonna, lunghNuovaRiga)
            If nuovoIndex > Me.TextLength Then nuovoIndex = Me.TextLength

            Dim isDeselecting As Boolean = False
            If oldLen > 0 Then
                If (cursoreAttuale > _anchorPoint AndAlso nuovoIndex < cursoreAttuale) OrElse
                 (cursoreAttuale < _anchorPoint AndAlso nuovoIndex > cursoreAttuale) Then
                    isDeselecting = True
                End If
            End If

            If selezione Then
                Dim start As Integer = Math.Min(_anchorPoint, nuovoIndex)
                Dim length As Integer = Math.Abs(_anchorPoint - nuovoIndex)
                Me.Select(start, length)
            Else
                Me.Select(nuovoIndex, 0)
            End If

            Me.ScrollToCaret()

            If selezione Then
                Dim testoLetto As String = ""
                If lunghNuovaRiga > 0 AndAlso inizioNuovaRiga + lunghNuovaRiga <= Me.TextLength Then
                    testoLetto = Me.Text.Substring(inizioNuovaRiga, lunghNuovaRiga)
                End If
                Dim prefix As String = If(isDeselecting, "Deselezionato ", "Selezionato ")
                InviaLetturaAsincrona(prefix & testoLetto)
            Else
                LeggeRigaCorrente()
            End If

        Catch ex As Exception
            log.ScriviLog("Errore movimento verticale: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciPagina(direzione As Integer)
        Try
            Dim righeDaSaltare As Integer = 15 * direzione
            Dim rigaCorrente As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
            Dim nuovaRiga As Integer = rigaCorrente + righeDaSaltare

            If nuovaRiga < 0 Then nuovaRiga = 0
            Dim totRighe As Integer = Me.GetLineFromCharIndex(Me.TextLength)

            If nuovaRiga > totRighe Then nuovaRiga = totRighe

            Dim nuovoIndice As Integer = Me.GetFirstCharIndexFromLine(nuovaRiga)

            Me.Select(nuovoIndice, 0)
            Me.ScrollToCaret()
            LeggeRigaCorrente()
        Catch ex As Exception
            log.ScriviLog("GestisciPagina: " & ex.Message)
        End Try
    End Sub

    Private Function TraduciCarattere(c As Char, Optional pronunciaSegni As Boolean = True) As String
        Try
            ' "emme" invece della sola lettera: a voce letta in fretta "m" ed "n" si confondono facilmente.
            If c = "m"c OrElse c = "M"c Then Return "emme"
            Return WGTastiera.NomeSimbolo(c)
        Catch ex As Exception
            log.ScriviLog("TraduciCarattere: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Private Sub LeggeCarattereCorrente(isSelection As Boolean, Optional customIndex As Integer = -1)
        Try
            Dim index As Integer = If(customIndex <> -1, customIndex, Me.SelectionStart)
            Dim pronunciaSegni As Boolean = LetturaPunteggiaturaLetturaCampo

            If index >= 0 AndAlso index < Me.TextLength Then
                Dim c As Char = Me.Text(index)
                If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then
                    InviaLetturaAsincrona(c.ToString() & " maiuscola")
                Else
                    InviaLetturaAsincrona(TraduciCarattere(c, pronunciaSegni))
                End If
            ElseIf index > 0 AndAlso index = Me.TextLength Then
                InviaLetturaAsincrona("Vuoto")
            End If
        Catch ex As Exception
            log.ScriviLog("LeggeCarattereCorrente: " & ex.Message)
        End Try
    End Sub

    Public Sub LeggiParolaSottoCursore()
        Try
            If Me.IsDisposed Then Return
            LeggeParolaCorrente(False)
        Catch ex As Exception
            log.ScriviLog("LeggiParolaSottoCursore: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggeParolaCorrente(isSelection As Boolean, Optional customIndex As Integer = -1)
        Try
            Dim wordInfo = OttieniParolaAlCursore(customIndex)
            If wordInfo IsNot Nothing Then
                Dim isLoaded As Boolean
                Dim checker As SymSpell
                SyncLock _dictLock
                    isLoaded = _dictionaryLoaded
                    checker = _spellChecker
                End SyncLock

                Dim parola As String = wordInfo.Item1.Trim()
                If isLoaded AndAlso Not WGDizionario.GetInstance().EsisteParola(parola) AndAlso Not checker.Lookup(parola.ToLower(), Verbosity.Top).Any(Function(x) x.term = parola.ToLower()) Then
                    InviaLetturaAsincrona("Errore ortografico " & parola)
                Else
                    InviaLetturaAsincrona(parola)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("LeggeParolaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggeRigaCorrente()
        Try
        If Me.TextLength = 0 Then
            lettura.LeggiTesto("Riga vuota")
            Return
        End If

        Dim lineIndex As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
        If lineIndex >= 0 AndAlso lineIndex < Me.Lines.Length Then
            Dim riga As String = Me.Lines(lineIndex)

            Dim infoIntestazione As String = ""
            Dim livello As Integer = OttieniLivelloIntestazione(lineIndex)
            If livello > 0 Then
                infoIntestazione = "Livello di intestazione " & livello & "."
            End If

            Dim sb As New StringBuilder()
            For Each c As Char In riga
                If c = "•"c OrElse AscW(c) = 8226 Then
                    sb.Append(" Elenco puntato ")
                Else
                    sb.Append(c)
                End If
            Next
            Dim rigaTesto As String = sb.ToString()

            If String.IsNullOrWhiteSpace(rigaTesto) Then
                lettura.LeggiTesto("Riga vuota")
            Else
                lettura.LeggiTesto(infoIntestazione & rigaTesto)
            End If
        Else
            lettura.LeggiTesto("Riga vuota")
        End If
        Catch ex As Exception
            log.ScriviLog("LeggeRigaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiTuttoDaCursore()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If lettura Is Nothing Then Return
            Dim rigaPartenza As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)
            If rigaPartenza < 0 Then rigaPartenza = 0
            _letturaContinuaRiga = rigaPartenza
            inLetturaContinua = True
            SyncLock _transizioneLetturaLock
                _transizioneGestita = False
            End SyncLock
            Me.BeginInvoke(New Action(AddressOf LeggiRigaContinua))
        Catch ex As Exception
            log.ScriviLog("LeggiTuttoDaCursore: " & ex.Message)
            inLetturaContinua = False
        End Try
    End Sub

    Private Function OttieniLivelloIntestazione(lineIndex As Integer) As Integer
        Try
            Dim charIndex As Integer = Me.GetFirstCharIndexFromLine(lineIndex)
            If charIndex = -1 Then Return 0

            Me.Select(charIndex, 1)
            Dim f As Font = Me.SelectionFont
            Me.Select(charIndex, 0)

            If f IsNot Nothing AndAlso f.Bold Then
                If f.SizeInPoints >= 23 Then Return 1
                If f.SizeInPoints >= 17 Then Return 2
                If f.SizeInPoints >= 13 Then Return 3
                If f.SizeInPoints >= 11 Then Return 4
            End If
            Return 0
        Catch ex As Exception
            log.ScriviLog("OttieniLivelloIntestazione: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub SpostatiAIntestazione(avanti As Boolean)
        Try
        inLetturaContinua = False
        Dim rigaCorr As Integer = Me.GetLineFromCharIndex(Me.SelectionStart)

        Dim stepDir As Integer = If(avanti, 1, -1)
        Dim rigaCheck As Integer = rigaCorr + stepDir

        While rigaCheck >= 0 AndAlso rigaCheck < Me.Lines.Length
            If OttieniLivelloIntestazione(rigaCheck) > 0 Then
                Me.SelectionStart = Me.GetFirstCharIndexFromLine(rigaCheck)
                Me.ScrollToCaret()
                LeggeRigaCorrente()
                Return
            End If
            rigaCheck += stepDir
        End While
        InviaLetturaAsincrona("Nessuna altra intestazione.")
        Catch ex As Exception
            log.ScriviLog("SpostatiAIntestazione: " & ex.Message)
        End Try
    End Sub

    Private Async Function InizializzaCorrettoreOrtograficoAsync() As Task
        Try
            If Not impostazioniComuni.AbilitaCorrettoreAttivo Then
                SyncLock _dictLock
                    _dictionaryLoaded = False
                End SyncLock
                Return
            End If

            Dim tempSpellChecker As New SymSpell(82765, 2, 7)
            Dim folderPath As String = Path.Combine(Application.StartupPath, "dictionary")
            Dim dictionaryPath As String = Path.Combine(folderPath, "it-100k.txt")

            If Not Directory.Exists(folderPath) Then
                Directory.CreateDirectory(folderPath)
            End If

            If Not File.Exists(dictionaryPath) Then
                Try
                    Using client As New HttpClient()
                        client.Timeout = TimeSpan.FromSeconds(15)
                        Dim urlDizionario As String = "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/it/it_50k.txt"
                        Dim content As String = Await client.GetStringAsync(urlDizionario)
                        File.WriteAllText(dictionaryPath, content)
                    End Using
                Catch exDownload As Exception
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore download dizionario: {exDownload.Message}")
                End Try
            End If

            Dim dictLoadedTemp As Boolean = False
            If File.Exists(dictionaryPath) Then
                If tempSpellChecker.LoadDictionary(dictionaryPath, 0, 1) Then
                    dictLoadedTemp = True
                Else
                End If
            Else
            End If

            SyncLock _dictLock
                _spellChecker = tempSpellChecker
                _dictionaryLoaded = dictLoadedTemp

                If _dictionaryLoaded Then
                    Try
                        Dim parolePersonali = WGDizionario.GetInstance().OttieniParole()
                        For Each parola As String In parolePersonali
                            _spellChecker.CreateDictionaryEntry(parola.ToLower(), 1)
                        Next
                    Catch ex As Exception
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante il caricamento del dizionario personale.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
                    End Try
                End If
            End SyncLock

        Catch ex As Exception
            SyncLock _dictLock
                _dictionaryLoaded = False
            End SyncLock
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante l'inizializzazione del correttore ortografico.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Function

    Public Sub EseguiControlloOrtografico(sender As Object, e As EventArgs)
        Try
            Dim isLoaded As Boolean
            Dim checker As SymSpell
            SyncLock _dictLock
                isLoaded = _dictionaryLoaded
                checker = _spellChecker
            End SyncLock

            If Not isLoaded OrElse checker Is Nothing Then
                Dim msg2 As New WGMessaggi()
                msg2.Messaggio("Dizionario per il controllo ortografico non disponibile o ancora in fase di caricamento.")
                msg2.ShowDialog()
                Return
            End If

            Dim startSearchIndex As Integer = Me.SelectionStart
            Dim text As String = Me.Text
            Dim textToSearch As String = text.Substring(startSearchIndex)

            Dim matches As MatchCollection = regexParole.Matches(textToSearch)

            If matches.Count = 0 AndAlso startSearchIndex > 0 Then
                textToSearch = text
                startSearchIndex = 0
                matches = regexParole.Matches(textToSearch)
            End If

            For i As Integer = 0 To matches.Count - 1
                Dim match As Match = matches(i)
                Dim originalWord As String = match.Value

                If IsNumeric(originalWord) OrElse originalWord.Equals("wingabriel", StringComparison.OrdinalIgnoreCase) OrElse WGDizionario.GetInstance().EsisteParola(originalWord) Then
                    Continue For
                End If

                Dim suggestions = checker.Lookup(originalWord.ToLower(), Verbosity.All)

                If suggestions.Count = 0 OrElse suggestions.First().term <> originalWord.ToLower() Then
                    Dim wordStartIndex As Integer = match.Index + startSearchIndex
                    Me.Select(wordStartIndex, match.Length)
                    Me.ScrollToCaret()
                    _currentSpellCheckInfo = Tuple.Create(originalWord, wordStartIndex, match.Length)
                    MostraMenuCorrezione(suggestions, originalWord)
                    Me.Focus()
                    Return
                End If
            Next

            Dim msg As New WGMessaggi()
            msg.Messaggio("Controllo ortografico completato. Nessun errore trovato.")
            msg.ShowDialog()
            Me.Focus()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in EseguiControlloOrtografico.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraMenuCorrezione(suggestions As List(Of SymSpell.SuggestItem), originalWord As String)
        Try
            If _spellCheckMenu IsNot Nothing AndAlso Not _spellCheckMenu.IsDisposed Then
                _spellCheckMenu.abbandona()
            End If

            _spellCheckMenu = New WGMenuLista()
            _spellCheckMenu.multiselezioneConsentita = False
            _spellCheckMenu.impostaTitolo($"Parola errata: {originalWord}. Scegli una correzione:")

            For Each suggestion In suggestions.Take(10)
                Dim correctedWord As String = suggestion.term
                If Not String.IsNullOrEmpty(originalWord) Then
                    If Char.IsUpper(originalWord(0)) AndAlso originalWord.Length > 1 AndAlso originalWord.Substring(1).All(Function(c) Char.IsLower(c)) Then
                        correctedWord = Char.ToUpper(correctedWord(0)) & correctedWord.Substring(1)
                    ElseIf originalWord.All(Function(c) Char.IsUpper(c)) AndAlso originalWord.Length > 1 Then
                        correctedWord = correctedWord.ToUpper()
                    End If
                End If
                _spellCheckMenu.aggiungiElemento(correctedWord, correctedWord)
            Next

            _spellCheckMenu.aggiungiOpzione("Aggiungi al dizionario", AddressOf ControlloOrtografico_Aggiungi)
            _spellCheckMenu.aggiungiOpzione("Ignora", AddressOf ControlloOrtografico_Ignora)
            _spellCheckMenu.aggiungiOpzione("Correggi manualmente", AddressOf ControlloOrtografico_Manuale)

            AddHandler _spellCheckMenu.ElementoSelezionato, AddressOf CorrezioneSelezionata_Suggerimento
            AddHandler _spellCheckMenu.escPremuto, AddressOf ChiudiMenuCorrezione

            _spellCheckMenu.avvia()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in MostraMenuCorrezione.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub CorrezioneSelezionata_Suggerimento()
        Try
            If _spellCheckMenu Is Nothing Then
                Return
            End If

            Dim idSelezionati As List(Of String) = _spellCheckMenu.ottieniElementi()
            If idSelezionati Is Nothing OrElse idSelezionati.Count = 0 Then
                Return
            End If

            Dim selectedWord As String = idSelezionati(0)
            ChiudiMenuCorrezione()
            ApplicaCorrezione(selectedWord)

            Await Task.Delay(500)

            Me.Focus()
            EseguiControlloOrtografico(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in CorrezioneSelezionata_Suggerimento.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub ControlloOrtografico_Aggiungi(sender As Object, e As EventArgs)
        Try
            Dim originalWord As String = ""
            If _currentSpellCheckInfo IsNot Nothing Then
                originalWord = _currentSpellCheckInfo.Item1
            End If

            ChiudiMenuCorrezione()

            If Not String.IsNullOrEmpty(originalWord) Then
                If WGDizionario.GetInstance().AggiungiParola(originalWord) Then
                    SyncLock _dictLock
                        If _spellChecker IsNot Nothing Then
                            _spellChecker.CreateDictionaryEntry(originalWord.ToLower(), 1)
                        End If
                    End SyncLock
                    Dim msg As New WGMessaggi()
                    msg.Messaggio($"'{originalWord}' aggiunto al dizionario.")
                    msg.ShowDialog()
                Else
                    Dim msgErr As New WGMessaggi()
                    msgErr.Messaggio($"Errore: Impossibile aggiungere '{originalWord}'.")
                    msgErr.ShowDialog()
                End If
            End If
            _currentSpellCheckInfo = Nothing

            Await Task.Delay(500)
            Me.Focus()
            EseguiControlloOrtografico(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ControlloOrtografico_Aggiungi.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub ControlloOrtografico_Ignora(sender As Object, e As EventArgs)
        Try
            ChiudiMenuCorrezione()
            _currentSpellCheckInfo = Nothing
            Await Task.Delay(500)
            Me.Focus()
            EseguiControlloOrtografico(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ControlloOrtografico_Ignora.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private _campiCorrezioneManuale As Campi
    Private _parolaCorrezioneManuale As String = ""

    Private Sub ControlloOrtografico_Manuale(sender As Object, e As EventArgs)
        Try
            Dim originalWord As String = ""
            If _currentSpellCheckInfo IsNot Nothing Then
                originalWord = _currentSpellCheckInfo.Item1
            End If

            ChiudiMenuCorrezione()

            If String.IsNullOrEmpty(originalWord) Then
                _currentSpellCheckInfo = Nothing
                Return
            End If

            _parolaCorrezioneManuale = originalWord
            _campiCorrezioneManuale = New Campi()
            _campiCorrezioneManuale.Aggiungi("Parola errata: " & originalWord & ". Scrivi la correzione", "correzione", True, False, originalWord)
            AddHandler _campiCorrezioneManuale.Fine, AddressOf CorrezioneManualeConfermata
            AddHandler _campiCorrezioneManuale.Chiusura, AddressOf CorrezioneManualeAnnullata
            _campiCorrezioneManuale.Avvia()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ControlloOrtografico_Manuale.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ScollegaCorrezioneManuale()
        Try
            If _campiCorrezioneManuale IsNot Nothing Then
                RemoveHandler _campiCorrezioneManuale.Fine, AddressOf CorrezioneManualeConfermata
                RemoveHandler _campiCorrezioneManuale.Chiusura, AddressOf CorrezioneManualeAnnullata
            End If
            _campiCorrezioneManuale = Nothing
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ScollegaCorrezioneManuale.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub CorrezioneManualeConfermata(sender As Object, e As EventArgs)
        Try
            Dim newWord As String = ""
            If _campiCorrezioneManuale IsNot Nothing Then newWord = If(_campiCorrezioneManuale.RecuperaValore("correzione"), "").Trim()
            ScollegaCorrezioneManuale()
            If Not String.IsNullOrEmpty(newWord) AndAlso newWord <> _parolaCorrezioneManuale Then
                ApplicaCorrezione(newWord)
            Else
                _currentSpellCheckInfo = Nothing
            End If

            Await Task.Delay(500)
            Me.Focus()
            EseguiControlloOrtografico(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in CorrezioneManualeConfermata.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub CorrezioneManualeAnnullata(sender As Object, e As EventArgs)
        Try
            ScollegaCorrezioneManuale()
            _currentSpellCheckInfo = Nothing
            Await Task.Delay(500)
            Me.Focus()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in CorrezioneManualeAnnullata.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiMenuCorrezione()
        Try
            If _spellCheckMenu IsNot Nothing Then
                RemoveHandler _spellCheckMenu.ElementoSelezionato, AddressOf CorrezioneSelezionata_Suggerimento
                RemoveHandler _spellCheckMenu.escPremuto, AddressOf ChiudiMenuCorrezione
                _spellCheckMenu.abbandona()
                _spellCheckMenu = Nothing
            End If
            Me.Focus()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ChiudiMenuCorrezione.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApplicaCorrezione(newWord As String)
        Try
            If _currentSpellCheckInfo Is Nothing Then
                Return
            End If

            Dim wordStartIndex As Integer = _currentSpellCheckInfo.Item2
            Dim matchLength As Integer = _currentSpellCheckInfo.Item3

            Me.Select(wordStartIndex, matchLength)
            Me.SelectedText = newWord
            Me.Select(wordStartIndex + newWord.Length, 0)

            _currentSpellCheckInfo = Nothing
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ApplicaCorrezione.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub PulisciEvidenziazioneControlloOrtografico()
        Try
            Dim currentSelectionStart As Integer = Me.SelectionStart
            Dim currentSelectionLength As Integer = Me.SelectionLength
            Dim defaultColor As Color = Me.ForeColor

            Me.SelectAll()
            Me.SelectionColor = defaultColor
            Me.Select(currentSelectionStart, currentSelectionLength)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in PulisciEvidenziazioneControlloOrtografico.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnTextChanged(e As EventArgs)
        MyBase.OnTextChanged(e)
        Try
            If isAutoCapitalizing Then
                Return
            End If

            If Not Me.IsDisposed Then
                _spellCheckTimer.Stop()
                _spellCheckTimer.Start()

                _autoCapTimer.Stop()
                If multiriga Then
                    _autoCapTimer.Start()
                End If
            End If

            MaiuscolaAutomatica()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in OnTextChanged.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MaiuscolaAutomatica()
        Try
            If Not MaiuscolaAutomaticaAttivaCampo Then Return

            If Me.ReadOnly OrElse solonumeri OrElse Not multiriga Then
                Return
            End If

            If isShiftPremuto Then
                Return
            End If

            Dim selStart As Integer = Me.SelectionStart
            If selStart = 0 Then
                Return
            End If

            Dim text As String = Me.Text
            If selStart > text.Length Then
                Return
            End If

            Dim charIndexToInspect As Integer = selStart - 1
            If charIndexToInspect < 0 Then
                Return
            End If

            Dim justTypedChar As Char = text(charIndexToInspect)
            If Not Char.IsLower(justTypedChar) Then
                Return
            End If

            Dim shouldCapitalize As Boolean = False
            Dim foundNonWhitespace As Boolean = False
            Dim precedingChar As Char = CChar(vbNullChar)

            Dim i As Integer = charIndexToInspect - 1

            While i >= 0
                If text(i) = vbLf OrElse text(i) = vbCr Then
                    shouldCapitalize = True
                    Exit While
                End If

                If Not Char.IsWhiteSpace(text(i)) Then
                    precedingChar = text(i)
                    foundNonWhitespace = True
                    Exit While
                End If
                i -= 1
            End While

            If shouldCapitalize Then
            ElseIf Not foundNonWhitespace Then
                shouldCapitalize = True
            ElseIf precedingChar = "." OrElse precedingChar = "?" OrElse precedingChar = "!" Then
                shouldCapitalize = True
            End If

            If shouldCapitalize Then
                Try
                    isAutoCapitalizing = True
                    Dim newChar As String = Char.ToUpper(justTypedChar).ToString()
                    Me.Select(charIndexToInspect, 1)
                    Me.SelectedText = newChar
                    Me.Select(selStart, 0)
                Finally
                    isAutoCapitalizing = False
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in MaiuscolaAutomatica.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub ApplicaMaiuscoleGlobali()
        Try
            If Not MaiuscolaAutomaticaAttivaCampo Then Return

            If Me.ReadOnly OrElse solonumeri OrElse Not multiriga Then
                Return
            End If
            If isAutoCapitalizing Then
                Return
            End If

            If isShiftPremuto Then
                Return
            End If

            Dim selStart As Integer = Me.SelectionStart
            Dim selLen As Integer = Me.SelectionLength

            Try
                isAutoCapitalizing = True
                Dim text As String = Me.Text
                Dim matches As MatchCollection = regexMaiuscole.Matches(text)

                For i As Integer = matches.Count - 1 To 0 Step -1
                    Dim m As Match = matches(i)
                    If m.Groups(3).Success Then
                        Dim charIndexToCapitalize As Integer = m.Groups(3).Index
                        Dim charToCapitalize As String = m.Groups(3).Value
                        If charIndexToCapitalize < Me.TextLength Then
                            Me.Select(charIndexToCapitalize, 1)
                            If Me.SelectedText = charToCapitalize Then
                                Me.SelectedText = charToCapitalize.ToUpper()
                            End If
                        End If
                    End If
                Next
            Finally
                Me.Select(selStart, selLen)
                isAutoCapitalizing = False
            End Try
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ApplicaMaiuscoleGlobali.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub _autoCapTimer_Tick(sender As Object, e As EventArgs) Handles _autoCapTimer.Tick
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            _autoCapTimer.Stop()
            ApplicaMaiuscoleGlobali()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in _autoCapTimer_Tick.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub _spellCheckTimer_Tick(sender As Object, e As EventArgs) Handles _spellCheckTimer.Tick
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            _spellCheckTimer.Stop()
            ControllaParolaAlCursore()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in _spellCheckTimer_Tick.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Function OttieniParolaAlCursore(Optional customIndex As Integer = -1) As Tuple(Of String, Integer, Integer)
        Try
            Dim cursorPos As Integer = If(customIndex <> -1, customIndex, Me.SelectionStart)
            Dim textLength As Integer = Me.TextLength

            If textLength = 0 OrElse cursorPos < 0 Then
                Return Nothing
            End If

            Dim text As String = Me.Text
            Dim wordSeparators As Char() = {" "c, vbCr, vbLf, vbTab, "."c, ","c, ";"c, ":"c, "!"c, "?"c, """"c, "("c, ")"c, "["c, "]"c, "{"c, "}"c, "•"c, "-"c, "/"c, "\"c, "|"c}

            Dim startIndex As Integer = cursorPos

            If startIndex > 0 AndAlso (startIndex = textLength OrElse wordSeparators.Contains(text(startIndex))) Then
                startIndex -= 1
            End If

            If startIndex >= 0 AndAlso startIndex < textLength AndAlso wordSeparators.Contains(text(startIndex)) Then
                Return Nothing
            End If

            Dim wordStart As Integer = text.LastIndexOfAny(wordSeparators, startIndex)
            wordStart = If(wordStart = -1, 0, wordStart + 1)

            Dim wordEnd As Integer = text.IndexOfAny(wordSeparators, wordStart)
            If wordEnd = -1 Then
                wordEnd = textLength
            End If

            If wordStart >= wordEnd Then
                Return Nothing
            End If

            Dim word As String = text.Substring(wordStart, wordEnd - wordStart)
            Return Tuple.Create(word, wordStart, word.Length)

        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in OttieniParolaAlCursore.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Sub ControllaParolaAlCursore()
        Try
            If isShiftPremuto OrElse Me.SelectionLength > 0 Then
                Return
            End If

            Dim isLoaded As Boolean
            Dim checker As SymSpell
            SyncLock _dictLock
                isLoaded = _dictionaryLoaded
                checker = _spellChecker
            End SyncLock

            If Not isLoaded OrElse checker Is Nothing OrElse Not multiriga OrElse Me.ReadOnly Then
                Return
            End If

            Dim currentSelectionStart As Integer = Me.SelectionStart
            Dim currentSelectionLength As Integer = Me.SelectionLength
            Dim currentSelectionColor As Color = Me.SelectionColor

            Dim wordInfo = OttieniParolaAlCursore()

            If wordInfo Is Nothing Then
                Return
            End If

            Dim word As String = wordInfo.Item1
            Dim startIndex As Integer = wordInfo.Item2
            Dim length As Integer = wordInfo.Item3

            If String.IsNullOrWhiteSpace(word) OrElse IsNumeric(word) OrElse word.Equals("wingabriel", StringComparison.OrdinalIgnoreCase) OrElse WGDizionario.GetInstance().EsisteParola(word) Then
                Me.Select(startIndex, length)
                If Me.SelectionColor = Color.Red Then
                    Me.SelectionColor = Me.ForeColor
                End If
            Else
                Dim suggestions = checker.Lookup(word.ToLower(), Verbosity.Top)
                Me.Select(startIndex, length)

                Dim isCorrect As Boolean = False
                If suggestions.Count > 0 Then
                    For Each suggestion In suggestions
                        If suggestion.term.Equals(word, StringComparison.OrdinalIgnoreCase) Then
                            isCorrect = True
                            Exit For
                        End If
                    Next
                End If

                If Not isCorrect Then
                    Me.SelectionColor = Color.Red
                Else
                    Me.SelectionColor = Me.ForeColor
                End If
            End If

            Me.Select(currentSelectionStart, currentSelectionLength)
            Me.SelectionColor = currentSelectionColor

        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in ControllaParolaAlCursore.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Function OttieniPosizioniIntestazioni() As System.Collections.Generic.List(Of Integer)
        Try
            Dim posizioni As New System.Collections.Generic.List(Of Integer)()
            If Me.TextLength = 0 Then
                Return posizioni
            End If
            For riga As Integer = 0 To Me.Lines.Length - 1
                Dim inizioRiga As Integer = Me.GetFirstCharIndexFromLine(riga)
                If inizioRiga = -1 Then
                    Continue For
                End If
                Me.Select(inizioRiga, 1)
                If Me.SelectionFont IsNot Nothing AndAlso Me.SelectionFont.Bold AndAlso Me.SelectionFont.SizeInPoints >= 20 Then
                    posizioni.Add(inizioRiga)
                End If
            Next
            Return posizioni
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in OttieniPosizioniIntestazioni.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return New System.Collections.Generic.List(Of Integer)()
        End Try
    End Function

    Private Sub VaiAIntestazioneSuccessiva()
        Try
            Dim posizioni = OttieniPosizioniIntestazioni()
            If Not posizioni.Any() Then
                Return
            End If
            Dim posCorrente = Me.SelectionStart
            Dim prossimaPosizione = posizioni.FirstOrDefault(Function(p) p > posCorrente)
            If prossimaPosizione <> 0 Then
                Me.SelectionStart = prossimaPosizione
            Else
                Me.SelectionStart = posizioni.First()
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in VaiAIntestazioneSuccessiva.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub VaiAIntestazionePrecedente()
        Try
            Dim posizioni = OttieniPosizioniIntestazioni()
            If Not posizioni.Any() Then
                Return
            End If
            Dim posCorrente = Me.SelectionStart
            Dim posizionePrecedente = posizioni.LastOrDefault(Function(p) p < posCorrente)
            If posizionePrecedente <> 0 Then
                Me.SelectionStart = posizionePrecedente
            Else
                Dim ultimaPosizione = posizioni.LastOrDefault()
                If ultimaPosizione <> posCorrente Then
                    Me.SelectionStart = ultimaPosizione
                End If
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in VaiAIntestazionePrecedente.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Function stringavalida(input As String) As Boolean
        Try
            input = input.Trim()
            If String.IsNullOrEmpty(input) Then
                Return False
            End If
            Dim regex As New System.Text.RegularExpressions.Regex("^[\p{P}\p{S}]+$")
            If regex.IsMatch(input) Then
                Return False
            End If
            Dim hasLetterOrDigit As Boolean = input.Any(Function(c) Char.IsLetterOrDigit(c))
            Return hasLetterOrDigit
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in stringavalida (ArgumentNullException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As RegexMatchTimeoutException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in stringavalida (RegexMatchTimeoutException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in stringavalida.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Sub AggiungiParolaSelezionataAlDizionario()
        Try
            Dim parolaDaAggiungere As String = Me.SelectedText.Trim()
            If String.IsNullOrWhiteSpace(parolaDaAggiungere) Then
                Dim msgErr As New WGMessaggi()
                msgErr.Messaggio("Nessuna parola selezionata.")
                msgErr.ShowDialog()
                Return
            End If
            If parolaDaAggiungere.Contains(" ") Then
                Dim msgErr As New WGMessaggi()
                msgErr.Messaggio("La selezione contiene spazi.È possibile aggiungere solo parole singole.")
                msgErr.ShowDialog()
                Return
            End If
            If WGDizionario.GetInstance().AggiungiParola(parolaDaAggiungere) Then
                SyncLock _dictLock
                    If _spellChecker IsNot Nothing Then
                        _spellChecker.CreateDictionaryEntry(parolaDaAggiungere.ToLower(), 1)
                    End If
                End SyncLock
                Dim msg As New WGMessaggi()
                msg.Messaggio($"'{parolaDaAggiungere}' aggiunto al dizionario.")
                msg.ShowDialog()
                PulisciEvidenziazioneControlloOrtografico()
                ControllaParolaAlCursore()
            Else
                Dim msgFail As New WGMessaggi()
                msgFail.Messaggio($"'{parolaDaAggiungere}' è già nel dizionario o c'è stato un errore.")
                msgFail.ShowDialog()
            End If
        Catch ex As Exception
            log.ScriviLog($"Errore in AggiungiParolaSelezionataAlDizionario: {ex.Message}")
        End Try
    End Sub

End Class