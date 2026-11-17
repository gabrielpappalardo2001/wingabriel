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

' Controllo principale dei singoli campi di immissione, con gestione della tastiera, degli eventi di chiusura e dell'accessibilità
' vocale.
Partial Public Class WGCampo
    Inherits System.Windows.Forms.UserControl
    Implements IWGControlloConSchede
    Implements IWGAnnuncioIngresso
    Implements IWGChiudibile

    Public Sub RichiediChiusura() Implements IWGChiudibile.RichiediChiusura
        campochiudi(Me, EventArgs.Empty)
    End Sub

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        AnnunciaTitoloETipo()
    End Sub

    ' Testi dell'editor: Control Tab passa da un testo all'altro della stessa applicazione.
    Private Shared _contatoreDocumenti As Integer = 0
    Public Property EDocumento As Boolean = False
    Public ReadOnly Property OrdineApertura As Integer = System.Threading.Interlocked.Increment(_contatoreDocumenti)

    Private _nomeDocumento As String = ""
    Public Property NomeDocumento As String
        Get
            Return If(_nomeDocumento <> "", _nomeDocumento, If(titolo, ""))
        End Get
        Set(value As String)
            _nomeDocumento = If(value, "")
        End Set
    End Property

    ' Annuncio di ingresso: titolo e tipo di campo ogni volta che il campo compare (apertura, ritorno da un
    ' menu, cambio di applicazione). Si tace quando WGWord o WGCampoMovimento stanno già leggendo qualcosa
    ' di voluto, come la posizione raggiunta con una ricerca o con "vai a".
    Private _tickUltimoAnnuncioIngresso As Integer = 0

    Friend Function DescrizioneTipoCampo() As String
        Try
            Dim tipo As String
            Dim solaLettura As Boolean
            If isWordMode Then
                tipo = If(_pathPdf <> "", "documento PDF", "documento Word")
                solaLettura = campoWord IsNot Nothing AndAlso campoWord.SolaLettura
            ElseIf campo IsNot Nothing Then
                tipo = If(campo.solonumeri, "campo numerico", If(campo.multiriga, "area di testo", "campo di testo"))
                solaLettura = campo.ReadOnly
            Else
                Return "campo di testo"
            End If
            Return tipo & If(solaLettura, ", sola lettura", "")
        Catch ex As Exception
            log.ScriviLog("WGCampo.DescrizioneTipoCampo: " & ex.Message)
            Return "campo di testo"
        End Try
    End Function

    Public Sub AnnunciaTitoloETipo()
        Try
            _tickUltimoAnnuncioIngresso = Environment.TickCount
            Dim nome As String = NomeDocumento
            lettura.LeggiTesto(If(nome <> "", nome & ", ", "") & DescrizioneTipoCampo(), escludiPunteggiatura:=True)
        Catch ex As Exception
            log.ScriviLog("WGCampo.AnnunciaTitoloETipo: " & ex.Message)
        End Try
    End Sub

    Private Async Sub AnnunciaIngressoRinviato()
        Try
            Await Task.Delay(120)
            If Me.IsDisposed OrElse Not Me.Visible OrElse MenuOpzioniAperto Then Return
            If Environment.TickCount - _tickUltimoAnnuncioIngresso < 1500 Then Return
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso campoWord.ConsumaSoppressioneAnnuncioRientro() Then Return
            If lettura.StaParlando Then Return
            AnnunciaTitoloETipo()
        Catch ex As Exception
            log.ScriviLog("WGCampo.AnnunciaIngressoRinviato: " & ex.Message)
        End Try
    End Sub

    Public Function CambiaScheda(avanti As Boolean) As Boolean Implements IWGControlloConSchede.CambiaScheda
        Try
            If Not EDocumento Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return False
            Dim testi As List(Of WGCampo) = contenitore.ControlliDellaStessaApplicazione(Me).OfType(Of WGCampo)().Where(Function(c) c.EDocumento AndAlso Not c.IsDisposed).OrderBy(Function(c) c.OrdineApertura).ToList()
            If Not testi.Contains(Me) Then testi.Add(Me)
            If testi.Count < 2 Then
                lettura.LeggiTesto("Un solo testo aperto")
                Return True
            End If
            Dim posizione As Integer = (testi.IndexOf(Me) + If(avanti, 1, -1) + testi.Count) Mod testi.Count
            Dim prossimo As WGCampo = testi(posizione)
            contenitore.MostraControllo(prossimo)
            lettura.LeggiTesto("Testo " & (posizione + 1) & " di " & testi.Count & ": " & prossimo.NomeDocumento)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCampo.CambiaScheda: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared log As New Logger()
    Private WithEvents menuopzioni As WGListaView
    Private _azioneOpzionePendente As EventHandler

    Private Structure Opzione
        Public Testo As String
        Public Azione As EventHandler
    End Structure

    Private listaOpzioni As New List(Of Opzione)
    Private listaOpzioniFormattazione As New List(Of Opzione)
    Private listaOpzioniEsterne As New List(Of Opzione)

    Private MenuOpzioniAperto As Boolean = False
    Public Property SopprimiChiusuraAutomatica As Boolean = False
    ''' <summary>Se vero, in un campo di sola lettura il tasto Invio non è consumato dal campo ma solleva InvioPremuto (per esempio per passare alla domanda successiva).</summary>
    Private _invioSollevaEvento As Boolean = False
    Public Property InvioSollevaEvento As Boolean
        Get
            Return _invioSollevaEvento
        End Get
        Set(value As Boolean)
            _invioSollevaEvento = value
        End Set
    End Property
    Public Event InvioPremuto As EventHandler

    ' Il campo di testo gestisce Invio da sé prima del campo che lo contiene: l'evento arriva da lui.
    Private Sub campo_InvioInSolaLettura(sender As Object, e As EventArgs) Handles campo.InvioInSolaLettura
        Try
            If Not _invioSollevaEvento Then Return
            Me.BeginInvoke(Sub()
                               Try
                                   RaiseEvent InvioPremuto(Me, EventArgs.Empty)
                               Catch exInvio As Exception
                                   log.ScriviLog("campo_InvioInSolaLettura (InvioPremuto): " & exInvio.Message & Environment.NewLine & exInvio.StackTrace)
                               End Try
                           End Sub)
        Catch ex As Exception
            log.ScriviLog("campo_InvioInSolaLettura: " & ex.Message)
        End Try
    End Sub
    Private _isMenuFormattazioneAperto As Boolean = False
    Private ultimoelementoopzioni As Integer
    Private WithEvents campo As WGCampoMovimento
    Private WithEvents campoWord As WGWord
    Private isWordMode As Boolean = False

    Public ReadOnly Property InModalitaWord As Boolean
        Get
            Try
                Return isWordMode
            Catch ex As Exception
                log.ScriviLog("InModalitaWord: " & ex.Message)
                Return False
            End Try
        End Get
    End Property
    Private _wordSolaLettura As Boolean = False
    Private _pathPdf As String = ""
    Private _solaLetturaPdf As Boolean = True

    Public Property WordSolaLettura As Boolean
        Get
            Try
                Return _wordSolaLettura
            Catch ex As Exception
                log.ScriviLog("WordSolaLettura Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _wordSolaLettura = value
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    campoWord.SolaLettura = value
                End If
            Catch ex As Exception
                log.ScriviLog(ex.Message)
            End Try
        End Set
    End Property
    Private pathWord As String = ""
    Private pathRtf As String = ""
    Private titolo As String
    Private utils As WGFormUtils = WGFormUtils.GetInstance()

    Private lettura As sintesivocale = sintesivocale.GetInstance()

    Private config As New WGConfig()
    Private tastoGiaPremuto As Boolean = False
    Private isRunning As Boolean = False
    Private isClosing As Boolean = False
    Private _titoloHeight As Integer = 60
    Private WithEvents autoSaveTimer As System.Windows.Forms.Timer

    Private campiForm As Campi

    Private _threadMonitorVisibilita As Thread
    Private _monitorVisibilitaInEsecuzione As Boolean = True

    Private WithEvents _tastiera As WGTastiera
    Private ultimoChiusuraMenu As Integer = 0
    Private _sopprimiLetturaTitolo As Boolean = False
    
    Private _shiftPremutoDaHook As Boolean = False
    Private _altPremutoDaHook As Boolean = False

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EnableWindow(hWnd As IntPtr, bEnable As Boolean) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(ByVal hWnd As IntPtr, ByVal Msg As Integer, ByVal wParam As Integer, ByVal lParam As Integer) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAsyncKeyState(ByVal vKey As Integer) As Short
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5
    Private Const VK_RMENU As Integer = &HA5

    Public Event chiusura As EventHandler

    Public Sub New()
        Try
            Me.DoubleBuffered = True
            ' MonitorVisibilitaLoop non fa piu' nulla: creare un thread per ogni campo aperto
            ' costava un megabyte di stack a vuoto, quindi non viene piu' avviato.
            _monitorVisibilitaInEsecuzione = False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AgganciaTastieraGlobale()
        Try
            If _tastiera Is Nothing Then
                _tastiera = WGTastiera.GetInstance()
                _touk = WGTouk.GetInstance()
            End If
        Catch ex As Exception
            log.ScriviLog("AgganciaTastieraGlobale: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftPremuto(sender As Object, e As EventArgs) Handles _tastiera.ShiftPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
            _shiftPremutoDaHook = True
        Catch ex As Exception
            log.ScriviLog("_tastiera_ShiftPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ShiftRilasciato
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
            _shiftPremutoDaHook = False
        Catch ex As Exception
            log.ScriviLog("_tastiera_ShiftRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltRilasciato
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
            _altPremutoDaHook = False
        Catch ex As Exception
            log.ScriviLog("_tastiera_AltRilasciato: " & ex.Message)
        End Try
    End Sub

Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
    Try
        If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
        If tasto.None = True Then Exit Sub

        Dim tastoBase As Keys = tasto And Keys.KeyCode
        Dim ctrlPremuto As Boolean = (tastoBase = Keys.LControlKey OrElse tastoBase = Keys.RControlKey)
        Dim isAltGr As Boolean = (ctrlPremuto AndAlso _altPremutoDaHook)

        If ctrlPremuto AndAlso Not isAltGr Then
            Dim campoAttivo As Boolean = Me.ContainsFocus
            If campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso campo.Focused Then campoAttivo = True
            If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso campoWord.ContainsFocus Then campoAttivo = True
            If campoAttivo Then InterrompiLetturaContinuaPerControl()
        End If

        If _altPremutoDaHook AndAlso Not isAltGr AndAlso tastoBase <> Keys.Oem3 AndAlso tastoBase <> Keys.Oem7 Then
            Return
        End If

        If tastoBase = Keys.F1 AndAlso Not My.Computer.Keyboard.ShiftKeyDown Then
            tasto = Keys.None
            If isWordMode Then
                Me.BeginInvoke(New MethodInvoker(AddressOf ApriGuidaWord))
            Else
                Me.BeginInvoke(New MethodInvoker(AddressOf ApriGuidaCampoTesto))
            End If
            Return
        End If

        If MenuOpzioniAperto Then
            If tasto = Keys.Enter Then
                tasto = Keys.None
                Dim listaDaUsare As List(Of Opzione) = If(_isMenuFormattazioneAperto, listaOpzioniFormattazione, listaOpzioni)
                If menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed AndAlso
                   menuopzioni.SelectedIndex >= 0 AndAlso
                   menuopzioni.SelectedIndex < listaDaUsare.Count Then
                    Dim azione = listaDaUsare(menuopzioni.SelectedIndex).Azione
                    MenuOpzioniAperto = False
                    Me.BeginInvoke(Sub()
                                       Try
                                           ChiudiMenuOpzioni()
                                           If azione IsNot Nothing Then azione.Invoke(Nothing, EventArgs.Empty)
                                       Catch exAzione As Exception
                                           log.ScriviLog("_tastiera_KeyDown (esecuzione opzione): " & exAzione.Message & Environment.NewLine & exAzione.StackTrace)
                                       End Try
                                   End Sub)
                End If
            ElseIf tasto = Keys.Escape Then
                tasto = Keys.None
                Me.BeginInvoke(Sub() ChiudiMenuOpzioniConAnnuncioTitolo())
            End If
        Else
            If tasto = Keys.Enter AndAlso InvioSollevaEvento AndAlso Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso campo.ReadOnly Then
                tasto = Keys.None
                If Math.Abs(CLng(Environment.TickCount) - CLng(ultimoChiusuraMenu)) < 300 Then Return
                Me.BeginInvoke(Sub()
                                   Try
                                       RaiseEvent InvioPremuto(Me, EventArgs.Empty)
                                   Catch exInvio As Exception
                                       log.ScriviLog("_tastiera_KeyDown (InvioPremuto): " & exInvio.Message & Environment.NewLine & exInvio.StackTrace)
                                   End Try
                               End Sub)
            ElseIf tasto = Keys.Escape Then
                tasto = Keys.None
                If Math.Abs(CLng(Environment.TickCount) - CLng(ultimoChiusuraMenu)) < 300 Then Return
                If Not My.Computer.Keyboard.CtrlKeyDown Then
                        Dim wordSolaLettura As Boolean = isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso campoWord.SolaLettura
                        Dim campoSolaLettura As Boolean = Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso campo.ReadOnly
                    Dim campoNonMultiriga As Boolean = Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso Not campo.multiriga

                    SopprimiChiusuraAutomatica = False
                    RaiseEvent chiusura(Me, Nothing)
                    If SopprimiChiusuraAutomatica Then Return

                    If isWordMode Then
                        chiudi()
                    ElseIf wordSolaLettura OrElse campoSolaLettura OrElse campoNonMultiriga Then
                        chiudi()
                    ElseIf Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                        If campo.ReadOnly OrElse Not campo.multiriga Then
                            chiudi()
                        End If
                    End If
                End If
            End If
        End If

    Catch ex As Exception
        log.ScriviLog("Tastiera KeyDown Error: " & ex.Message)
    End Try
End Sub

    Private Sub InterrompiLetturaContinuaPerControl()
        Try
            If Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                campo.InterrompiLetturaContinua()
            End If
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso campoWord._gestoreMovimento IsNot Nothing Then
                If campoWord._gestoreMovimento.inLetturaContinua Then
                    campoWord._gestoreMovimento.FermaLetturaContinua()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGCampo.InterrompiLetturaContinuaPerControl: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriGuidaCampoTesto()
        Try
            WGGuida.MostraGuidaCampoTesto()
        Catch ex As Exception
            log.ScriviLog("ApriGuidaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriGuidaWord()
        Try
            WGGuida.MostraGuidaWord()
        Catch ex As Exception
            log.ScriviLog("ApriGuidaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciInvioOpzioniCampo(sender As Object, e As EventArgs) Handles menuopzioni.InvioPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not MenuOpzioniAperto Then Return
            Dim listaDaUsare As List(Of Opzione) = If(_isMenuFormattazioneAperto, listaOpzioniFormattazione, listaOpzioni)
            If menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed AndAlso menuopzioni.SelectedIndex >= 0 AndAlso menuopzioni.SelectedIndex < listaDaUsare.Count Then
                _azioneOpzionePendente = listaDaUsare(menuopzioni.SelectedIndex).Azione
                MenuOpzioniAperto = False
                Me.BeginInvoke(New MethodInvoker(AddressOf EseguiOpzionePendente))
            End If
        Catch ex As Exception
            log.ScriviLog("GestisciInvioOpzioniCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiOpzionePendente()
        Try
            ChiudiMenuOpzioni()
            Dim azione As EventHandler = _azioneOpzionePendente
            _azioneOpzionePendente = Nothing
            If azione IsNot Nothing Then azione.Invoke(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("EseguiOpzionePendente: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEscOpzioniCampo(sender As Object, e As EventArgs) Handles menuopzioni.EscPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not MenuOpzioniAperto Then Return
            Me.BeginInvoke(New MethodInvoker(AddressOf ChiudiMenuOpzioni))
        Catch ex As Exception
            log.ScriviLog("GestisciEscOpzioniCampo: " & ex.Message)
        End Try
    End Sub
    
    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return

            _altPremutoDaHook = True
            Dim foregroundWnd As IntPtr = GetForegroundWindow()
            Dim isTarget As Boolean = False

            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                    If foregroundWnd = contenitore.Handle Then
                        Dim schermata As Control = Me
                        While schermata.Parent IsNot Nothing AndAlso Not (TypeOf schermata.Parent Is WGContenitore)
                            schermata = schermata.Parent
                        End While
                        If contenitore.PuoRicevereInput(schermata) AndAlso Me.ContainsFocus Then
                            isTarget = True
                        End If
                    End If
                End If
            Catch exCont As Exception
                log.ScriviLog("_tastiera_AltPremuto (verifica contenitore): " & exCont.Message)
            End Try

            If Not isTarget AndAlso foregroundWnd = Me.Handle Then isTarget = True
            If Not isTarget AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso foregroundWnd = campoWord.Handle Then isTarget = True
            If Not isTarget AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso foregroundWnd = campo.Handle Then isTarget = True

            If isTarget AndAlso Not MenuOpzioniAperto AndAlso Me.Visible AndAlso Not isClosing Then
                Dim isRightAlt As Boolean = (GetAsyncKeyState(VK_RMENU) And &H8000) <> 0
                If Not isRightAlt AndAlso Me.IsHandleCreated Then
                    ' Un Invoke sincrono qui bloccherebbe il thread degli eventi della tastiera per tutta
                    ' la durata di un eventuale dialogo modale, che pero' si chiude solo da tastiera.
                    Me.BeginInvoke(New MethodInvoker(Sub()
                                                         Try
                                                             apriopzioni(False)
                                                         Catch exApri As Exception
                                                             log.ScriviLog("_tastiera_AltPremuto (apriopzioni): " & exApri.Message)
                                                         End Try
                                                     End Sub))
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("Tastiera AltPremuto Error: " & ex.Message)
        End Try
    End Sub

    ' Su tastiera italiana AltGr coincide con il tasto Alt destro: la stessa pressione che apre
    ' il menu delle opzioni serve anche a comporre la chiocciola e gli altri caratteri di terzo
    ' livello. Se AltGr e' appena stato usato per scrivere un carattere la richiesta di apertura
    ' del menu va ignorata, sia in modalita' word sia nel campo di testo semplice.
    Private Function AperturaMenuDestroDaIgnorare() As Boolean
        Try
            Dim tastieraDaControllare As WGTastiera = _tastiera

            If tastieraDaControllare Is Nothing Then
                Try
                    tastieraDaControllare = WGTastiera.GetInstance()
                Catch exIstanza As Exception
                    log.ScriviLog("AperturaMenuDestroDaIgnorare (istanza tastiera): " & exIstanza.Message)
                    Return False
                End Try
            End If

            If tastieraDaControllare Is Nothing Then Return False

            Return tastieraDaControllare.AltGrUsatoPerCarattereDiRecente()
        Catch ex As Exception
            log.ScriviLog("AperturaMenuDestroDaIgnorare: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub campo_menudestroaperto(sender As Object, e As EventArgs) Handles campo.menudestroapertura
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse isClosing OrElse Not Me.IsHandleCreated Then Return
            If AperturaMenuDestroDaIgnorare() Then
                Return
            End If
            Me.BeginInvoke(New MethodInvoker(Sub()
                                                 Try
                                                     apriopzioni(True)
                                                 Catch exApri As Exception
                                                     log.ScriviLog("campo_menudestroaperto (apriopzioni): " & exApri.Message)
                                                 End Try
                                             End Sub))
        Catch ex As Exception
            log.ScriviLog("campo_menudestroaperto: " & ex.Message)
        End Try
    End Sub

    Private Sub campoWord_menudestroaperto(sender As Object, e As EventArgs) Handles campoWord.menudestroaperto
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse isClosing OrElse Not Me.IsHandleCreated Then Return
            If AperturaMenuDestroDaIgnorare() Then
                Return
            End If
            Me.BeginInvoke(New MethodInvoker(Sub()
                                                 Try
                                                     apriopzioni(True)
                                                 Catch exApri As Exception
                                                     log.ScriviLog("campoWord_menudestroaperto (apriopzioni): " & exApri.Message)
                                                 End Try
                                             End Sub))
        Catch ex As Exception
            log.ScriviLog("campoWord_menudestroaperto: " & ex.Message)
        End Try
    End Sub

    Private Sub MonitorVisibilitaLoop()
        Return
    End Sub

    Public Function HaFormattazioniAttive() As Boolean
        Try
            If isWordMode Then
                Return False
            End If
            If campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                Dim rtf As String = campo.Rtf
                If String.IsNullOrEmpty(rtf) Then Return False
                If rtf.Contains("\b") OrElse rtf.Contains("\i") OrElse rtf.Contains("\ul") OrElse rtf.Contains("\strike") OrElse rtf.Contains("\cf") Then
                    Return True
                End If
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub InizializzaCampoRTF()
        Try
            If campo Is Nothing Then
                campo = New WGCampoMovimento()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InizializzaCampoWord()
        Try
            If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then Return
            If Not Me.IsHandleCreated Then Return

            campoWord = New WGWord()
            campoWord.Dock = DockStyle.None
            campoWord.Visible = True
            campoWord.Enabled = True

            If Not String.IsNullOrEmpty(pathWord) Then
                campoWord.LoadDocx(pathWord)
            ElseIf String.IsNullOrEmpty(_pathPdf) Then
                campoWord.NuovoDocumento(titolo)
            End If

            campoWord.SolaLettura = _wordSolaLettura

            If Not Me.Controls.Contains(campoWord) Then
                Me.Controls.Add(campoWord)
            End If

            campoWord.BringToFront()
            indiceAzioneTouch = -1
            PosizionaControlliTouch()

            campoWord.Focus()
            SetFocus(campoWord.Handle)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub EseguiImportazionePdfConAttesa(percorsoPdf As String, solaLetturaPdf As Boolean)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return

            Dim processoincorso As New WGProcessoInCorso()
            processoincorso.avvia("Caricamento pdf in corso, attendere")

            Try
                campoWord.ImportaPdf(percorsoPdf)
                campoWord.SolaLettura = solaLetturaPdf
            Catch exImport As Exception
                log.ScriviLog("EseguiImportazionePdfConAttesa import: " & exImport.Message)
            Finally
                Try
                    processoincorso.fine()
                Catch exFine As Exception
                    log.ScriviLog("EseguiImportazionePdfConAttesa fine: " & exFine.Message)
                End Try
            End Try
        Catch ex As Exception
            log.ScriviLog("EseguiImportazionePdfConAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub autoSaveTimer_Tick(sender As Object, e As EventArgs) Handles autoSaveTimer.Tick
        Try
            If isWordMode Then
                If Not String.IsNullOrEmpty(pathWord) AndAlso campoWord IsNot Nothing AndAlso campoWord.HaModifiche() Then
                    SalvaWord(pathWord)
                End If
            Else
                If Not String.IsNullOrEmpty(pathRtf) AndAlso campo IsNot Nothing AndAlso campo.Modified Then
                    SaveFile(pathRtf)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Function HaModifiche() As Boolean
        Try
            If isWordMode Then
                If campoWord IsNot Nothing Then Return campoWord.HaModifiche()
            Else
                 If campo IsNot Nothing Then Return campo.Modified
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Sub campo_RichiestaChiusura(sender As Object, e As EventArgs) Handles campo.RichiestaChiusura
        Try
            SopprimiChiusuraAutomatica = False
            RaiseEvent chiusura(Me, Nothing)
            If SopprimiChiusuraAutomatica Then Return
            chiudi()
        Catch ex As Exception
            log.ScriviLog("campo_RichiestaChiusura: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            ' Con disposing a False si e' sul thread dei finalizzatori: toccare oggetti gestiti li'
            ' e' illegittimo e una eccezione su quel thread chiude il processo.
            If disposing Then
                rilasciatastiera()
                If Not isClosing Then
                    isClosing = True
                    Try
                        If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
                    Catch exRimuovi As Exception
                        log.ScriviLog("Dispose (RimuoviWGForm): " & exRimuovi.Message)
                    End Try
                End If
                _monitorVisibilitaInEsecuzione = False

                ' Ogni rilascio va protetto per conto suo: se uno fallisce gli altri devono comunque
                ' avvenire, altrimenti restano agganciati alla tastiera globale handler di un controllo
                ' ormai distrutto, che poi ricevono ogni tasto premuto per il resto della sessione.
                Try
                    If autoSaveTimer IsNot Nothing Then
                        autoSaveTimer.Stop()
                        autoSaveTimer.Dispose()
                    End If
                Catch exTimer As Exception
                    log.ScriviLog("Dispose (autoSaveTimer): " & exTimer.Message)
                End Try

                Try
                    If campo IsNot Nothing Then campo.Dispose()
                Catch exCampo As Exception
                    log.ScriviLog("Dispose (campo): " & exCampo.Message)
                End Try

                Try
                    If campoWord IsNot Nothing Then campoWord.Dispose()
                Catch exCampoWord As Exception
                    log.ScriviLog("Dispose (campoWord): " & exCampoWord.Message)
                End Try

                Try
                    If menuopzioni IsNot Nothing Then menuopzioni.Dispose()
                Catch exMenu As Exception
                    log.ScriviLog("Dispose (menuopzioni): " & exMenu.Message)
                End Try
                menuopzioni = Nothing

                Try
                    If _dettaturaCampo IsNot Nothing Then
                        RemoveHandler _dettaturaCampo.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampo
                        RemoveHandler _dettaturaCampo.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampo
                        _dettaturaCampo.Dispose()
                        _dettaturaCampo = Nothing
                    End If
                Catch exDettatura As Exception
                    log.ScriviLog("Dispose (dettatura): " & exDettatura.Message)
                End Try

                Try
                    If _tastoDettaturaCampo IsNot Nothing Then
                        RemoveHandler _tastoDettaturaCampo.Interrotto, AddressOf OnTastoDettaturaCampoInterrotto
                        If Not _tastoDettaturaCampo.IsDisposed Then _tastoDettaturaCampo.Dispose()
                        _tastoDettaturaCampo = Nothing
                    End If
                Catch exTasto As Exception
                    log.ScriviLog("Dispose (tastoDettatura): " & exTasto.Message)
                End Try

                Try
                    If _touk IsNot Nothing Then
                        If _touk.ZonaDocumento Is campoWord OrElse _touk.ZonaDocumento Is campo Then _touk.ZonaDocumento = Nothing
                        RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvantiTesto
                        RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietroTesto
                        RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                        _touk = Nothing
                    End If
                    If _tastieraTouch IsNot Nothing Then
                        RemoveHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                        RemoveHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.SuggerimentoScelto, AddressOf OnSuggerimentoSceltoDaTastieraTouch
                        RemoveHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
                        If Not _tastieraTouch.IsDisposed Then _tastieraTouch.Dispose()
                        _tastieraTouch = Nothing
                    End If
                Catch exTastieraTouch As Exception
                    log.ScriviLog("Dispose (tastieraTouch): " & exTastieraTouch.Message)
                End Try

                Try
                    If _tastiera IsNot Nothing Then
                        RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                        RemoveHandler _tastiera.ShiftPremuto, AddressOf _tastiera_ShiftPremuto
                        RemoveHandler _tastiera.ShiftRilasciato, AddressOf _tastiera_ShiftRilasciato
                        RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                        RemoveHandler _tastiera.AltRilasciato, AddressOf _tastiera_AltRilasciato
                    End If
                Catch exTastiera As Exception
                    log.ScriviLog("Dispose (tastiera): " & exTastiera.Message)
                End Try
                _tastiera = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private WithEvents _touk As WGTouk
    Private _tastieraTouch As WGTastieraTouch

    Private Sub attaccatastiera()
        Try
            If campo IsNot Nothing Then
                campo.AttaccaTastiera()
            End If
            MostraTastieraTouchSeAttiva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraTastieraTouchSeAttiva()
        Try
            Dim campoValido As Boolean = (Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso Not campo.ReadOnly) OrElse
                                          (isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso Not campoWord.SolaLettura)
            If Not campoValido Then Return
            If _touk Is Nothing OrElse Not _touk.TouchAttivo Then Return

            If _tastieraTouch Is Nothing Then
                _tastieraTouch = New WGTastieraTouch()
                AddHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                AddHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.SuggerimentoScelto, AddressOf OnSuggerimentoSceltoDaTastieraTouch
                AddHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
            End If

            ' L'altezza non viene imposta qui: la decide Mostra in base alla grandezza tasti
            ' scelta dall'utente in Impostazioni, Touch. Fissarla dall'esterno annullava quella
            ' scelta e i tasti restavano piccoli anche per chi li aveva chiesti grandi.
            _tastieraTouch.Dock = DockStyle.None

            If Not Me.Controls.Contains(_tastieraTouch) Then
                Me.Controls.Add(_tastieraTouch)
            End If

            _tastieraTouch.Mostra()
            _tastieraTouch.BringToFront()
            PosizionaControlliTouch()
            AggiornaZonaDocumentoTouch()
        Catch ex As Exception
            log.ScriviLog("MostraTastieraTouchSeAttiva: " & ex.Message)
        End Try
    End Sub

    Private Sub PosizionaControlliTouch()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim larghezza As Integer = Me.ClientSize.Width
            Dim altezza As Integer = Me.ClientSize.Height
            If larghezza <= 0 OrElse altezza <= 0 Then Return

            Dim altezzaTastiera As Integer = 0
            If _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible AndAlso Me.Controls.Contains(_tastieraTouch) Then
                ' Si parte dall'altezza decisa da Mostra secondo la grandezza tasti scelta, ma
                ' non le si concede piu' del sessanta per cento: Mostra applica un'altezza minima
                ' DOPO aver limitato al contenitore, quindi su aree piccole restituisce l'intera
                ' altezza e la tastiera coprirebbe tutto, lasciando il campo irraggiungibile.
                ' L'altezza e la posizione le decide la tastiera, che applica la grandezza tasti
                ' scelta e i propri limiti. Qui si legge il risultato per dimensionare il campo,
                ' senza imporre un secondo limite che darebbe una tastiera piu' bassa alla prima
                ' apertura rispetto a quella ottenuta dopo il menu delle opzioni.
                _tastieraTouch.Dock = DockStyle.None
                _tastieraTouch.ApplicaAltezzaVoluta()
                _tastieraTouch.BringToFront()
                altezzaTastiera = _tastieraTouch.Height
                If altezzaTastiera > altezza Then altezzaTastiera = altezza
                If altezzaTastiera < 1 Then altezzaTastiera = 1
            End If

            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
                If Not Me.Controls.Contains(campoWord) Then Return

                Dim altezzaCampoWord As Integer = altezza - altezzaTastiera
                If altezzaCampoWord < 1 Then altezzaCampoWord = 1

                campoWord.Dock = DockStyle.None
                campoWord.SetBounds(0, 0, larghezza, altezzaCampoWord)
            Else
                If campo Is Nothing OrElse campo.IsDisposed Then Return
                If Not Me.Controls.Contains(campo) Then Return

                Dim larghezzaCampo As Integer = larghezza - 20
                If larghezzaCampo < 1 Then larghezzaCampo = 1

                Dim altezzaCampoTesto As Integer = altezza - altezzaTastiera - _titoloHeight - 20
                If altezzaCampoTesto < 1 Then altezzaCampoTesto = 1

                campo.Dock = DockStyle.None
                campo.SetBounds(10, _titoloHeight + 10, larghezzaCampo, altezzaCampoTesto)
            End If

            AggiornaZonaDocumentoTouch()
        Catch ex As Exception
            log.ScriviLog("PosizionaControlliTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaZonaDocumentoTouch()
        Try
            If _touk Is Nothing Then Return

            Dim tastieraVisibile As Boolean = _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible
            If Not tastieraVisibile Then
                If _touk.ZonaDocumento Is campoWord OrElse _touk.ZonaDocumento Is campo Then _touk.ZonaDocumento = Nothing
                Return
            End If

            If isWordMode Then
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then _touk.ZonaDocumento = campoWord
            Else
                If campo IsNot Nothing AndAlso Not campo.IsDisposed Then _touk.ZonaDocumento = campo
            End If
        Catch ex As Exception
            log.ScriviLog("AggiornaZonaDocumentoTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub RidimensionamentoCampoTouch(sender As Object, e As EventArgs) Handles Me.Resize
        Try
            PosizionaControlliTouch()
        Catch ex As Exception
            log.ScriviLog("RidimensionamentoCampoTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastieraTouch()
        Try
            If _tastieraTouch IsNot Nothing Then _tastieraTouch.Nascondi()
            PosizionaControlliTouch()
            AggiornaZonaDocumentoTouch()
        Catch ex As Exception
            log.ScriviLog("NascondiTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Function TestoPrimaDelCursoreCampo() As String
        Try
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return ""
                Return campoWord.TestoPrimaDelCursore()
            End If
            If campo Is Nothing OrElse campo.IsDisposed Then Return ""
            Dim posizione As Integer = campo.SelectionStart
            If posizione < 0 Then posizione = 0
            If posizione > campo.Text.Length Then posizione = campo.Text.Length
            Return campo.Text.Substring(0, posizione)
        Catch ex As Exception
            log.ScriviLog("TestoPrimaDelCursoreCampo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiornaSuggerimentiTastiera()
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then Return
            _tastieraTouch.AggiornaSuggerimenti(TestoPrimaDelCursoreCampo())
        Catch ex As Exception
            log.ScriviLog("AggiornaSuggerimentiTastiera: " & ex.Message)
        End Try
    End Sub

    Private Sub OnSuggerimentoSceltoDaTastieraTouch(parola As String, caratteriDaSostituire As Integer)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String, Integer)(AddressOf OnSuggerimentoSceltoDaTastieraTouch), parola, caratteriDaSostituire)
                Return
            End If
            If String.IsNullOrEmpty(parola) Then Return

            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed OrElse campoWord.SolaLettura Then Return
                For i As Integer = 1 To caratteriDaSostituire
                    campoWord.GestisciKeyDown(Keys.Back)
                Next
                For Each carattereTesto As Char In parola
                    campoWord.GestisciChar(carattereTesto)
                Next
                campoWord.GestisciChar(" "c)
            Else
                If campo Is Nothing OrElse campo.IsDisposed OrElse campo.ReadOnly Then Return
                Dim posizione As Integer = campo.SelectionStart
                If caratteriDaSostituire > 0 AndAlso posizione >= caratteriDaSostituire Then
                    campo.Select(posizione - caratteriDaSostituire, caratteriDaSostituire)
                    campo.SelectedText = ""
                End If
                campo.SelectedText = parola & " "
            End If

            AggiornaSuggerimentiTastiera()
        Catch ex As Exception
            log.ScriviLog("OnSuggerimentoSceltoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnCarattereInseritoDaTastieraTouch(carattere As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnCarattereInseritoDaTastieraTouch), carattere)
                Return
            End If
            If String.IsNullOrEmpty(carattere) Then Return
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed OrElse campoWord.SolaLettura Then Return
                campoWord.InserisciCarattereSenzaLettura(carattere(0))
            Else
                If campo Is Nothing OrElse campo.IsDisposed OrElse campo.ReadOnly Then Return

                ' Dopo l'inserimento serve l'eco di digitazione, che e' quello che legge la lettera
                ' appena scritta ed e' gia' quanto fa la tastiera fisica. Scrivendo il solo testo,
                ' come avveniva prima, la lettera compariva nel campo senza essere annunciata.
                campo.InserisciCarattereSenzaLettura(carattere(0))
            End If

            Try
                AggiornaSuggerimentiTastiera()
            Catch exSuggerimenti As Exception
                log.ScriviLog("OnCarattereInseritoDaTastieraTouch (suggerimenti): " & exSuggerimenti.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("OnCarattereInseritoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnEliminaRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnEliminaRichiestoDaTastieraTouch))
                Return
            End If
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed OrElse campoWord.SolaLettura Then Return
                campoWord.GestisciKeyDown(Keys.Back)
            Else
                If campo Is Nothing OrElse campo.IsDisposed OrElse campo.ReadOnly Then Return
                If campo.SelectionLength > 0 Then
                    campo.SelectedText = ""
                ElseIf campo.SelectionStart > 0 Then
                    campo.Select(campo.SelectionStart - 1, 1)
                    campo.SelectedText = ""
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("OnEliminaRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnInvioRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnInvioRichiestoDaTastieraTouch))
                Return
            End If
            If isWordMode Then
                ' L'editor Word e' sempre multiriga: l'invio va a capo nel documento.
                If campoWord Is Nothing OrElse campoWord.IsDisposed OrElse campoWord.SolaLettura Then Return
                campoWord.GestisciKeyDown(Keys.Enter)
            Else
                If campo Is Nothing OrElse campo.IsDisposed OrElse campo.ReadOnly Then Return
                If campo.multiriga Then
                    ' Campo multiriga: l'invio va a capo nel testo.
                    campo.SelectedText = Environment.NewLine
                Else
                    ' Campo a riga singola: non c'e' un "a capo" possibile, quindi l'invio
                    ' conferma e passa oltre, come il tasto Invio della tastiera fisica.
                    Dim tastoInvio As Keys = Keys.Enter
                    _tastiera_KeyDown(tastoInvio)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("OnInvioRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiudiRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnChiudiRichiestoDaTastieraTouch))
                Return
            End If
            NascondiTastieraTouch()
        Catch ex As Exception
            log.ScriviLog("OnChiudiRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTestoDettatoDaTastieraTouch(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTestoDettatoDaTastieraTouch), testo)
                Return
            End If
            If String.IsNullOrEmpty(testo) Then Return
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed OrElse campoWord.SolaLettura Then Return
                For Each carattereTesto As Char In testo
                    campoWord.GestisciChar(carattereTesto)
                Next
            Else
                If campo Is Nothing OrElse campo.IsDisposed OrElse campo.ReadOnly Then Return
                campo.SelectedText = testo
            End If
        Catch ex As Exception
            log.ScriviLog("OnTestoDettatoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub rilasciatastiera()
        Try
            If campo IsNot Nothing Then
                campo.RilasciaTastiera()
            End If
            NascondiTastieraTouch()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private azioniTouch As New List(Of Opzione)
    Private indiceAzioneTouch As Integer = -1

    ''' <summary>
    ''' Prepara l'elenco delle azioni disponibili sul campo corrente senza aprire il menu. La
    ''' costruzione viene rifatta solo all'inizio di un nuovo giro, perche' rigenerare la lista a
    ''' ogni gesto ripeterebbe inutilmente il lavoro di CostruisciMenuPrincipale.
    ''' </summary>
    Private Sub PreparaAzioniTouch()
        Try
            If isWordMode Then
                CostruisciMenuPrincipaleWord()
            Else
                CostruisciMenuPrincipaleRtf()
            End If

            azioniTouch.Clear()
            azioniTouch.AddRange(listaOpzioni)

            If indiceAzioneTouch >= azioniTouch.Count Then indiceAzioneTouch = -1
        Catch ex As Exception
            log.ScriviLog("PreparaAzioniTouch: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Scorre le azioni del campo corrente e ne annuncia il nome, senza aprire il menu opzioni:
    ''' le azioni restano parte del documento, come le azioni di VoiceOver. Il flick verso il
    ''' basso passa alla successiva, quello verso l'alto alla precedente, e l'elenco e' circolare.
    ''' </summary>
    Private Sub ScorriAzioniTouch(avanti As Boolean)
        Try
            If MenuOpzioniAperto Then Return
            If Not GestoTouchDestinatoAlCampo() Then Return

            If indiceAzioneTouch < 0 OrElse azioniTouch.Count = 0 Then PreparaAzioniTouch()
            If azioniTouch.Count = 0 Then Return

            If indiceAzioneTouch < 0 Then
                indiceAzioneTouch = If(avanti, 0, azioniTouch.Count - 1)
            ElseIf avanti Then
                indiceAzioneTouch = (indiceAzioneTouch + 1) Mod azioniTouch.Count
            Else
                indiceAzioneTouch = (indiceAzioneTouch - 1 + azioniTouch.Count) Mod azioniTouch.Count
            End If

            If lettura IsNot Nothing Then lettura.LeggiTesto(azioniTouch(indiceAzioneTouch).Testo)
        Catch ex As Exception
            log.ScriviLog("ScorriAzioniTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiAzioneTouchSelezionata()
        Try
            If MenuOpzioniAperto Then Return
            If Not GestoTouchDestinatoAlCampo() Then Return
            If indiceAzioneTouch < 0 OrElse indiceAzioneTouch >= azioniTouch.Count Then Return

            Dim azione As EventHandler = azioniTouch(indiceAzioneTouch).Azione
            If azione Is Nothing Then Return

            indiceAzioneTouch = -1
            azione.Invoke(Me, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("EseguiAzioneTouchSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickSuAzioni() Handles _touk.FlickSu
        Try
            ScorriAzioniTouch(avanti:=False)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickSuAzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickGiuAzioni() Handles _touk.FlickGiu
        Try
            ScorriAzioniTouch(avanti:=True)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickGiuAzioni: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Il doppio tocco a due dita chiude il campo, come il tasto Esc. Mancava del tutto: il gesto
    ''' veniva riconosciuto da WGTouk ma nessuno lo raccoglieva, e il campo restava aperto.
    ''' </summary>
    Private Sub _touk_EscapeGestoCampo() Handles _touk.EscapeGesto
        Try
            If isClosing OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            If MenuOpzioniAperto Then Return
            If Not GestoTouchDestinatoAlCampo() Then Return
            campochiudi(Me, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("_touk_EscapeGestoCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_TapDoppioAzioni() Handles _touk.TapDoppio
        Try
            If WGTastieraTouch.GestoSullaTastieraAttiva(_touk) Then Return
            If _touk IsNot Nothing AndAlso _touk.GestoIniziatoSuTastieraTouch() Then Return
            EseguiAzioneTouchSelezionata()
        Catch ex As Exception
            log.ScriviLog("_touk_TapDoppioAzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvantiTesto() Handles _touk.FlickAvanti
        Try
            EseguiMovimentoTouchSecondoModalita(avanti:=True)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickAvantiTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickIndietroTesto() Handles _touk.FlickIndietro
        Try
            EseguiMovimentoTouchSecondoModalita(avanti:=False)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickIndietroTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvantiDocumento() Handles _touk.FlickAvantiDocumento
        Try
            EseguiMovimentoTouchSecondoModalita(avanti:=True)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickAvantiDocumento: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickIndietroDocumento() Handles _touk.FlickIndietroDocumento
        Try
            EseguiMovimentoTouchSecondoModalita(avanti:=False)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickIndietroDocumento: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_RichiestaLetturaCompletaDocumento() Handles _touk.RichiestaLetturaCompletaDocumento
        Try
            EseguiLetturaCompletaTouch()
        Catch ex As Exception
            log.ScriviLog("_touk_RichiestaLetturaCompletaDocumento: " & ex.Message)
        End Try
    End Sub

    Private Function GestoTouchDestinatoAlCampo() As Boolean
        Try
            If _touk Is Nothing Then Return False

            Dim controlloCampo As Control = Nothing
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return False
                controlloCampo = campoWord
            Else
                If campo Is Nothing OrElse campo.IsDisposed Then Return False
                controlloCampo = campo
            End If

            If _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible Then
                If _touk.GestoIniziatoDentro(_tastieraTouch) Then Return False
                Return _touk.GestoIniziatoDentro(controlloCampo)
            End If

            Return controlloCampo.Focused
        Catch ex As Exception
            log.ScriviLog("GestoTouchDestinatoAlCampo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AssicuraFuocoAlCampo()
        Try
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
                If campoWord.Focused Then Return
                campoWord.Focus()
                SetFocus(campoWord.Handle)
            Else
                If campo Is Nothing OrElse campo.IsDisposed Then Return
                If campo.Focused Then Return
                campo.Focus()
                SetFocus(campo.Handle)
            End If
        Catch ex As Exception
            log.ScriviLog("AssicuraFuocoAlCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiMovimentoTouchSecondoModalita(avanti As Boolean)
        Try
            If _touk Is Nothing Then Return
            If Not GestoTouchDestinatoAlCampo() Then Return
            AssicuraFuocoAlCampo()

            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
                Select Case _touk.ModalitaNavigazione
                    Case WGTouk.ModalitaNavigazioneTouch.Riga
                        campoWord.GestisciKeyDown(If(avanti, Keys.Down, Keys.Up))
                    Case WGTouk.ModalitaNavigazioneTouch.Paragrafo
                        If avanti Then
                            campoWord.SpostaParagrafoAvantiPubblica()
                        Else
                            campoWord.SpostaParagrafoIndietroPubblica()
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Parola
                        If avanti Then
                            campoWord.SpostaParolaAvantiPubblica()
                        Else
                            campoWord.SpostaParolaIndietroPubblica()
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Carattere
                        If avanti Then
                            campoWord.SpostaCarattereAvantiPubblica()
                        Else
                            campoWord.SpostaCarattereIndietroPubblica()
                        End If
                    Case Else
                        ' Campo: qui c'e' un solo campo Word, quindi non c'e' un altro campo tra
                        ' cui spostarsi. Dentro una tabella passiamo alla cella successiva o
                        ' precedente, fuori da una tabella si legge il documento intero invece di
                        ' ricadere sul movimento per carattere, che rendeva questa modalita'
                        ' indistinguibile da quella per carattere.
                        If campoWord.SiamoInTabella Then
                            If avanti Then
                                campoWord.VaiCellaSuccessivaPubblica()
                            Else
                                campoWord.VaiCellaPrecedentePubblica()
                            End If
                        Else
                            campoWord.AttivaDisattivaLetturaCompletaTouch()
                        End If
                End Select
            Else
                If campo Is Nothing OrElse campo.IsDisposed Then Return
                Select Case _touk.ModalitaNavigazione
                    Case WGTouk.ModalitaNavigazioneTouch.Riga
                        If avanti Then
                            campo.SpostaRigaGiuPubblica()
                        Else
                            campo.SpostaRigaSuPubblica()
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Paragrafo
                        If avanti Then
                            campo.SpostaParagrafoAvantiPubblica()
                        Else
                            campo.SpostaParagrafoIndietroPubblica()
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Parola
                        If avanti Then
                            campo.SpostaParolaAvantiPubblica()
                        Else
                            campo.SpostaParolaIndietroPubblica()
                        End If
                    Case WGTouk.ModalitaNavigazioneTouch.Carattere
                        If avanti Then
                            campo.SpostaCarattereAvantiPubblica()
                        Else
                            campo.SpostaCarattereIndietroPubblica()
                        End If
                    Case Else
                        ' Campo: qui ce n'e' uno solo, quindi non c'e' nulla tra cui spostarsi e
                        ' si legge il contenuto intero. Ricadere sul movimento per carattere
                        ' rendeva questa modalita' indistinguibile da quella per carattere: il
                        ' rotore annunciava il cambio ma il gesto si comportava allo stesso modo.
                        If lettura IsNot Nothing Then lettura.LeggiTesto(campo.Text)
                End Select
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiMovimentoTouchSecondoModalita: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_EsplorazioneTocco(xSchermo As Integer, ySchermo As Integer) Handles _touk.EsplorazioneTocco
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
            If _tastieraTouch Is Nothing OrElse Not _tastieraTouch.Visible Then Return

            Dim puntoTastiera As Point = _tastieraTouch.PointToClient(New Point(xSchermo, ySchermo))
            If puntoTastiera.X >= 0 AndAlso puntoTastiera.Y >= 0 AndAlso puntoTastiera.X < _tastieraTouch.Width AndAlso puntoTastiera.Y < _tastieraTouch.Height Then Return

            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
                Dim puntoWord As Point = campoWord.PointToClient(New Point(xSchermo, ySchermo))
                If puntoWord.X < 0 OrElse puntoWord.Y < 0 Then Return
                If puntoWord.X >= campoWord.Width OrElse puntoWord.Y >= campoWord.Height Then Return
                If Not campoWord.Focused Then
                    campoWord.Focus()
                    SetFocus(campoWord.Handle)
                End If
            Else
                If campo Is Nothing OrElse campo.IsDisposed Then Return
                Dim puntoCampo As Point = campo.PointToClient(New Point(xSchermo, ySchermo))
                If puntoCampo.X < 0 OrElse puntoCampo.Y < 0 Then Return
                If puntoCampo.X >= campo.Width OrElse puntoCampo.Y >= campo.Height Then Return
                If Not campo.Focused Then
                    campo.Focus()
                    SetFocus(campo.Handle)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("_touk_EsplorazioneTocco: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_RichiestaLetturaCompleta() Handles _touk.RichiestaLetturaCompleta
        Try
            EseguiLetturaCompletaTouch()
        Catch ex As Exception
            log.ScriviLog("_touk_RichiestaLetturaCompleta: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiLetturaCompletaTouch()
        Try
            If Not GestoTouchDestinatoAlCampo() Then Return
            AssicuraFuocoAlCampo()
            If isWordMode Then
                If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
                campoWord.AttivaDisattivaLetturaCompletaTouch()
                Return
            End If
            If campo Is Nothing OrElse campo.IsDisposed Then Return
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(campo.Text)
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiLetturaCompletaTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaFocus()
        Try
            ultimoChiusuraMenu = Environment.TickCount
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                campoWord.Focus()
                SetFocus(campoWord.Handle)
            ElseIf Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                campo.Focus()
                SetFocus(campo.Handle)
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ConvertiHtmlInRtf(ByVal html As String) As String
        Try
            Dim doc As New HtmlAgilityPack.HtmlDocument()
            doc.LoadHtml(html)

            Dim rtf As New System.Text.StringBuilder()

            rtf.Append("{\rtf1\ansi\ansicpg1252\deff0")
            rtf.Append("{\fonttbl{\f0 Arial;}{\f1 Courier New;}{\f2 Symbol;}}")
            rtf.Append("{\colortbl;\red0\green0\blue255;}")

            Dim olCounter As Integer = 0
            ElaboraNodo(doc.DocumentNode, rtf, olCounter, 0)

            rtf.Append("}")

            Dim finalRtf As String = rtf.ToString()
            finalRtf = System.Text.RegularExpressions.Regex.Replace(finalRtf, "^({\\rtf1.*?\\fs24\s+)\\par", "$1", RegexOptions.Singleline)
            finalRtf = System.Text.RegularExpressions.Regex.Replace(finalRtf, "(\s*\\par\s*){2,}", "\par ")
            finalRtf = System.Text.RegularExpressions.Regex.Replace(finalRtf, "\s*\\par\s*(\})$", "$1")

            Return finalRtf
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Const PROFONDITA_MASSIMA_HTML As Integer = 200

    ' Un HTML con annidamento patologico farebbe scendere la ricorsione fino allo
    ' StackOverflowException, che non e' intercettabile e chiude il processo senza log: la
    ' profondita' va quindi limitata prima di ricorrere.
    Private Sub ElaboraNodo(node As HtmlAgilityPack.HtmlNode, rtf As System.Text.StringBuilder, ByRef olCounter As Integer, listLevel As Integer, Optional profondita As Integer = 0)
        Try
            If node Is Nothing Then
                Return
            End If

            If profondita > PROFONDITA_MASSIMA_HTML Then
                Return
            End If

            Select Case node.NodeType
                Case HtmlAgilityPack.HtmlNodeType.Document
                    rtf.Append("\pard\sa200\sl276\slmult1\f0\fs24 ")
                    If node.HasChildNodes Then
                        For Each child As HtmlAgilityPack.HtmlNode In node.ChildNodes
                            ElaboraNodo(child, rtf, olCounter, listLevel, profondita + 1)
                        Next
                    End If

                Case HtmlAgilityPack.HtmlNodeType.Text
                    Dim textParts() As String = WebUtility.HtmlDecode(node.InnerText).Split(":"c)
                    For i As Integer = 0 To textParts.Length - 1
                        rtf.Append(CodificaRtf(textParts(i)))
                        If i < textParts.Length - 1 Then
                            rtf.Append(":\line ")
                        End If
                    Next

                Case HtmlAgilityPack.HtmlNodeType.Element
                    Dim stylePrefix As String = ""
                    Dim styleSuffix As String = ""
                    Dim align As String = OttieniAllineamentoRtf(node)

                    Select Case node.Name.ToLower()
                        Case "p", "div"
                            stylePrefix = "\par" & align & "\pard\sa200\sl276\slmult1 "
                        Case "h1"
                            stylePrefix = "\par" & align & "\pard\sa200\sl276\slmult1 {\b\fs48 "
                            styleSuffix = "}\pard\fs24\b0 "
                        Case "h2"
                            stylePrefix = "\par" & align & "\pard\sa200\sl276\slmult1 {\b\fs36 "
                            styleSuffix = "}\pard\fs24\b0 "
                        Case "h3"
                            stylePrefix = "\par" & align & "\pard\sa200\sl276\slmult1 {\b\fs28 "
                            styleSuffix = "}\pard\fs24\b0 "
                        Case "h4"
                            stylePrefix = "\par" & align & "\pard\sa200\sl276\slmult1 {\b\fs24 "
                            styleSuffix = "}\pard\fs24\b0 "
                        Case "b", "strong"
                            stylePrefix = "{\b "
                            styleSuffix = "}"
                        Case "i", "em"
                            stylePrefix = "{\i "
                            styleSuffix = "}"
                        Case "u"
                            stylePrefix = "{\ul "
                            styleSuffix = "}"
                        Case "s", "strike", "del"
                            stylePrefix = "{\strike "
                            styleSuffix = "}"
                        Case "sup"
                            stylePrefix = "{\super "
                            styleSuffix = "}"
                        Case "sub"
                            stylePrefix = "{\sub "
                            styleSuffix = "}"
                        Case "br"
                            stylePrefix = "\line "
                        Case "hr"
                            stylePrefix = "\par\pard\brdrb\brdrs\brdrw10\par\pard "
                        Case "ol", "ul"
                            stylePrefix = "\par\pard "
                            styleSuffix = "\par\pard "
                        Case "li"
                            Dim indentation As Integer = 720 + (listLevel * 400)
                            styleSuffix = "\par"
                            If node.ParentNode IsNot Nothing AndAlso node.ParentNode.Name.ToLower() = "ol" Then
                                stylePrefix = $"\pard\fi-360\li{indentation}{olCounter}.\tab "
                                olCounter += 1
                            Else
                                stylePrefix = $"\pard\fi-360\li{indentation}\u8226\tab "
                            End If
                        Case "a"
                            stylePrefix = ""
                            styleSuffix = ""
                        Case "blockquote"
                            stylePrefix = "{\pard\li720\ri720 "
                            styleSuffix = "\par}"
                        Case "pre", "code"
                            stylePrefix = "{\f1 "
                            styleSuffix = "}\f0 "
                        Case "table"
                            stylePrefix = "\par "
                            styleSuffix = "\par "
                        Case "tr"
                            Dim cells As System.Collections.Generic.List(Of HtmlAgilityPack.HtmlNode) = node.SelectNodes("td|th")?.ToList()
                            If cells IsNot Nothing Then
                                Dim cellWidth As Integer = 9000 / Math.Max(1, cells.Count)
                                Dim currentPos As Integer = 0
                                stylePrefix = "\trowd\trgaph108\trleft-108"
                                For i As Integer = 1 To cells.Count
                                    currentPos += cellWidth
                                    stylePrefix &= "\clbrdrb\brdrs\brdrw10\clbrdrl\brdrs\brdrw10\clbrdrt\brdrs\brdrw10\cellx" & currentPos.ToString()
                                Next
                            End If
                            styleSuffix = "\row "
                        Case "th"
                            stylePrefix = "{\b\intbl "
                            styleSuffix = "}\cell "
                        Case "td"
                            stylePrefix = "\intbl "
                            styleSuffix = "\cell "
                    End Select

                    rtf.Append(stylePrefix)

                    If node.HasChildNodes Then
                        Dim nextLevel As Integer = listLevel
                        Dim isList As Boolean = (node.Name.ToLower() = "ul" OrElse node.Name.ToLower() = "ol")
                        If isList Then
                            nextLevel += 1
                        End If

                        If node.Name.ToLower() = "ol" Then
                            Dim nestedOlCounter As Integer = 1
                            For Each child As HtmlAgilityPack.HtmlNode In node.ChildNodes
                                ElaboraNodo(child, rtf, nestedOlCounter, nextLevel, profondita + 1)
                            Next
                        Else
                            For Each child As HtmlAgilityPack.HtmlNode In node.ChildNodes
                                ElaboraNodo(child, rtf, olCounter, nextLevel, profondita + 1)
                            Next
                        End If
                    End If

                    rtf.Append(styleSuffix)
            End Select
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Function CodificaRtf(text As String) As String
        Try
            If String.IsNullOrEmpty(text) Then
                Return ""
            End If

            Dim sb As New System.Text.StringBuilder(text.Length * 2)
            Dim i As Integer = 0

            Do While i < text.Length
                Dim i_val As Integer = i
                Dim c As Char = text(i_val)

                If c = "\"c OrElse c = "{"c OrElse c = "}"c Then
                    sb.Append("\"c).Append(c)
                    i += 1
                Else
                    Dim codepoint As Integer = 0
                    Dim skipNext As Boolean = False

                    If Char.IsHighSurrogate(c) AndAlso i + 1 < text.Length AndAlso Char.IsLowSurrogate(text(i + 1)) Then
                        codepoint = Char.ConvertToUtf32(c, text(i + 1))
                        skipNext = True
                    Else
                        codepoint = Convert.ToInt32(c)
                    End If

                    If codepoint < 128 Then
                         sb.Append(c)
                    ElseIf codepoint <= 65535 Then
                        Dim signedVal As Integer = codepoint
                        If signedVal > 32767 Then signedVal -= 65536
                        sb.Append("\u" & signedVal.ToString() & "?")
                    Else
                        sb.Append("\u" & codepoint.ToString() & "?")
                    End If

                    If skipNext Then
                        i += 2
                    Else
                        i += 1
                    End If
                 End If
            Loop

            Return sb.ToString()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Function OttieniAllineamentoRtf(node As HtmlAgilityPack.HtmlNode) As String
        Try
            Dim alignAttr As String = node.GetAttributeValue("align", "").ToLower()
            Select Case alignAttr
                Case "center"
                    Return "\qc "
                Case "right"
                    Return "\qr "
                Case Else
                    Return "\ql "
            End Select
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return "\ql "
        End Try
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        Try
            MyBase.OnLoad(e)
            attaccatastiera()
            If Not isWordMode Then
                Dim msg As String = titolo
                If campo IsNot Nothing AndAlso Not campo.ReadOnly Then msg &= " Campo di editazione"
                lettura.LeggiTesto(msg, escludiPunteggiatura:=True)
             End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As ThreadStateException
             log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub init(titolocampo As String, Optional stringa As String = "", Optional multilinea As Boolean = False, Optional solalettura As Boolean = False, Optional numeri As Boolean = False, Optional campoobbligatorio As Boolean = False)
        Try
            InizializzaCampoRTF()
            campo.solonumeri = numeri
            campo.obbligatorio = campoobbligatorio

            If multilinea = True Then
                campo.multiriga = multilinea
            End If
  
             If campoobbligatorio Then
                titolo = titolocampo & " * "
             Else
                titolo = titolocampo
            End If

            campo.Text = stringa
            campo.ReadOnly = solalettura
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As OutOfMemoryException
             log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                  Throw
             Else
            End If
        End Try
    End Sub

    Public Sub NuovoWord(titolocampo As String, Optional solalettura As Boolean = False)
        Try
            titolo = titolocampo
            isWordMode = True
            _wordSolaLettura = solalettura
            pathWord = ""

            If campo IsNot Nothing Then
                campo.IsWordMode = True
            End If

            If Me.Controls.Contains(campo) Then
                Me.Controls.Remove(campo)
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub CaricaWord(titolocampo As String, percorso As String, Optional solalettura As Boolean = False)
        Try
            titolo = titolocampo
            pathWord = percorso
            isWordMode = True
            _wordSolaLettura = solalettura

        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub ImportaDocx(titolocampo As String, percorso As String, Optional solalettura As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                Dim m As New WGMessaggi()
                m.messaggio("File non trovato: " & percorso)
                m.ShowDialog()
                Return
            End If
            Dim nomeFile As String = System.IO.Path.GetFileName(percorso)
            titolo = If(String.IsNullOrWhiteSpace(titolocampo), nomeFile, titolocampo)
            pathWord = percorso
            isWordMode = True
            _wordSolaLettura = solalettura

            If campo IsNot Nothing Then
                campo.IsWordMode = True
            End If

            If Me.Controls.Contains(campo) Then
                Me.Controls.Remove(campo)
            End If

            If Me.IsHandleCreated Then
                InizializzaCampoWord()
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub SalvaWord(percorso As String)
        Try
             If isWordMode AndAlso campoWord IsNot Nothing Then
                pathWord = percorso
                campoWord.SaveDocx(percorso)
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private campiPercorsoSalvataggio As Campi

    Public Event SalvataggioCompletato(percorso As String)

    Public Sub SalvaDocumento(Optional percorsoProposto As String = "", Optional chiudiDopoSalvataggio As Boolean = False)
        Try
            If Not isWordMode OrElse campoWord Is Nothing OrElse campoWord.IsDisposed Then
                Dim msgNessun As New WGMessaggi()
                msgNessun.messaggio("Nessun documento Word aperto da salvare.")
                msgNessun.ShowDialog()
                Return
            End If

            Dim menuFormato As New WGMenu()
            menuFormato.aggiungi("Word (.docx)", Sub() EseguiSalvataggioConFormato(".docx", percorsoProposto, chiudiDopoSalvataggio))
            menuFormato.aggiungi("OpenDocument Text (.odt)", Sub() EseguiSalvataggioConFormato(".odt", percorsoProposto, chiudiDopoSalvataggio))
            menuFormato.avvia("Scegli il formato di salvataggio", True)
        Catch ex As Exception
            log.ScriviLog("SalvaDocumento eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiSalvataggioConFormato(estensione As String, percorsoProposto As String, chiudiDopoSalvataggio As Boolean)
        Try
            If Not isWordMode OrElse campoWord Is Nothing OrElse campoWord.IsDisposed Then Return

            Dim percorsoBase As String = If(Not String.IsNullOrEmpty(percorsoProposto), percorsoProposto, pathWord)

            If String.IsNullOrEmpty(percorsoBase) Then
                campiPercorsoSalvataggio = New Campi()
                campiPercorsoSalvataggio.Aggiungi("Percorso completo del file da salvare", "percorso", True)
                AddHandler campiPercorsoSalvataggio.Fine, Sub() ConfermaPercorsoSalvataggio(estensione, chiudiDopoSalvataggio)
                campiPercorsoSalvataggio.avvia()
            Else
                Dim percorsoFinale As String = AssicuraEstensione(percorsoBase, estensione)
                CompletaSalvataggio(percorsoFinale, chiudiDopoSalvataggio)
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiSalvataggioConFormato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaPercorsoSalvataggio(estensione As String, chiudiDopoSalvataggio As Boolean)
        Try
            If campiPercorsoSalvataggio Is Nothing Then Return

            ' L'handler e' agganciato con una lambda e non e' quindi rimovibile: si azzera subito il
            ' riferimento cosi' una seconda emissione dell'evento non lavora su un oggetto distrutto.
            Dim campiDaChiudere As Campi = campiPercorsoSalvataggio
            campiPercorsoSalvataggio = Nothing

            Dim percorsoScelto As String = campiDaChiudere.RecuperaValore("percorso")

            Try
                campiDaChiudere = Nothing
            Catch exChiusura As Exception
                log.ScriviLog("ConfermaPercorsoSalvataggio (chiusura campi): " & exChiusura.Message)
            End Try

            If String.IsNullOrWhiteSpace(percorsoScelto) Then Return

            percorsoScelto = AssicuraEstensione(percorsoScelto, estensione)
            CompletaSalvataggio(percorsoScelto, chiudiDopoSalvataggio)
        Catch ex As Exception
            log.ScriviLog("ConfermaPercorsoSalvataggio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private WithEvents domandaSovrascriviSalvataggio As WGDomanda
    Private _percorsoInAttesaSovrascrittura As String = ""
    Private _chiudiDopoSalvataggioInAttesa As Boolean = False

    Public Event RichiediNuovoPercorsoSalvataggio(nomeSuggerito As String)

    Private Sub CompletaSalvataggio(percorso As String, chiudiDopoSalvataggio As Boolean)
        Try
            If System.IO.File.Exists(percorso) Then
                _percorsoInAttesaSovrascrittura = percorso
                _chiudiDopoSalvataggioInAttesa = chiudiDopoSalvataggio
                domandaSovrascriviSalvataggio = New WGDomanda()
                domandaSovrascriviSalvataggio.avvia("Esiste già un file chiamato """ & System.IO.Path.GetFileName(percorso) & """. Vuoi sovrascriverlo?", True)
                Return
            End If

            EseguiSalvataggioEffettivo(percorso, chiudiDopoSalvataggio)
        Catch ex As Exception
            log.ScriviLog("CompletaSalvataggio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub domandaSovrascriviSalvataggio_rispostadomanda(rispostadom As DialogResult) Handles domandaSovrascriviSalvataggio.rispostadomanda
        Try
            Dim percorso As String = _percorsoInAttesaSovrascrittura
            Dim chiudiDopo As Boolean = _chiudiDopoSalvataggioInAttesa
            _percorsoInAttesaSovrascrittura = ""

            If rispostadom = DialogResult.Yes Then
                EseguiSalvataggioEffettivo(percorso, chiudiDopo)
            Else
                Dim nomeVecchio As String = System.IO.Path.GetFileNameWithoutExtension(percorso)
                RaiseEvent RichiediNuovoPercorsoSalvataggio(nomeVecchio)
            End If
        Catch ex As Exception
            log.ScriviLog("domandaSovrascriviSalvataggio_rispostadomanda eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSalvataggioEffettivo(percorso As String, chiudiDopoSalvataggio As Boolean)
        Try
            SalvaWord(percorso)
            RaiseEvent SalvataggioCompletato(percorso)

            Dim msgOk As New WGMessaggi()
            msgOk.messaggio("Documento salvato in " & percorso)
            msgOk.ShowDialog()

            If chiudiDopoSalvataggio Then
                Me.chiudi()
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiSalvataggioEffettivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function DeterminaPercorsoSalvataggioProposto(estensione As String) As String
        Try
            Dim nomeBase As String = If(String.IsNullOrWhiteSpace(titolo), "Documento", titolo)
            For Each carattereNonValido As Char In System.IO.Path.GetInvalidFileNameChars()
                nomeBase = nomeBase.Replace(carattereNonValido, "_"c)
            Next
            Dim cartellaDocumenti As String = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            Return System.IO.Path.Combine(cartellaDocumenti, nomeBase & estensione)
        Catch ex As Exception
            log.ScriviLog("DeterminaPercorsoSalvataggioProposto eccezione: " & ex.Message)
            Return "Documento" & estensione
        End Try
    End Function

    Private Function AssicuraEstensione(percorso As String, estensione As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return percorso
            Dim estensioneAttuale As String = System.IO.Path.GetExtension(percorso)
            If estensioneAttuale.Equals(estensione, StringComparison.OrdinalIgnoreCase) Then Return percorso
            Return System.IO.Path.ChangeExtension(percorso, estensione)
        Catch ex As Exception
            log.ScriviLog("AssicuraEstensione eccezione: " & ex.Message)
            Return percorso
        End Try
    End Function

    Public Sub EsportaHtml(percorso As String)
        Try
            If Not isWordMode OrElse campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            If String.IsNullOrWhiteSpace(percorso) Then Return
            campoWord.EsportaHtml(percorso)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub Initrtf(titolocampo As String, Optional rtfString As String = "", Optional multilinea As Boolean = False, Optional solalettura As Boolean = False, Optional numeri As Boolean = False, Optional campoobbligatorio As Boolean = False)
        Try
            InizializzaCampoRTF()
            campo.solonumeri = numeri
            campo.obbligatorio = campoobbligatorio

            If multilinea = True Then
                campo.multiriga = multilinea
            End If
            If campoobbligatorio Then
                titolo = titolocampo & " * "
            Else
                titolo = titolocampo
            End If

            campo.Rtf = rtfString
            campo.ReadOnly = solalettura
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As OutOfMemoryException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                Throw
            Else
            End If
        End Try
    End Sub

    Public Sub CaricaHtml(titolocampo As String, htmlString As String)
        Try
            Dim rtfText As String = ConvertiHtmlInRtf(htmlString)
            Initrtf(titolocampo, rtfText, True, True, False, False)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Public Async Sub avvia()
        Try

            If Me.DesignMode() Then
                Exit Sub
            End If

            AgganciaTastieraGlobale()

            If lettura.NvdaInEsecuzione() AndAlso campo IsNot Nothing Then
                campo.AccessibleName = titolo
            End If

            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.MostraControllo(Me)

            If isWordMode Then
                Dim processoincorso As New WGProcessoInCorso()
                processoincorso.avvia("Caricamento documento in corso, attendere")
                Me.Visible = False
                Try
                    Await Task.Run(Sub()
                                       Thread.Sleep(50)
                                   End Sub)
                Catch ex As Exception
                    log.ScriviLog("avvia (Task.Run attesa): " & ex.Message)
                End Try
                Try
                    processoincorso.fine()
                Catch exFine As Exception
                    log.ScriviLog("avvia (chiusura processo in corso): " & exFine.Message)
                End Try
                Me.Visible = True
                Me.BringToFront()
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    campoWord.Focus()
                    SetFocus(campoWord.Handle)
                End If
                AnnunciaTitoloETipo()
            Else
                Me.Visible = True
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                Throw
            Else
            End If
        End Try
    End Sub

    Public Sub aggiungiopzione(testoVoce As String, evento As EventHandler)
        Try
            If String.IsNullOrEmpty(testoVoce) OrElse evento Is Nothing Then Return
            Dim opz As New Opzione
            opz.Testo = testoVoce
            opz.Azione = evento
            listaOpzioniEsterne.Add(opz)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub aggiungiopzioneFormattazione(testoVoce As String, evento As EventHandler)
        Try
            If String.IsNullOrEmpty(testoVoce) OrElse evento Is Nothing Then Return
            Dim opz As New Opzione
            opz.Testo = testoVoce
            opz.Azione = evento
            listaOpzioniFormattazione.Add(opz)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub menuopzioni_SelectedIndexChanged(sender As Object, e As EventArgs)
        Try
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()

                If isWordMode Then
                    If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                        EnableWindow(campoWord.Handle, True)
                        ShowWindow(campoWord.Handle, SW_SHOW)
                    End If
                ElseIf campo IsNot Nothing Then
                    If Not campo.IsDisposed Then
                        EnableWindow(campo.Handle, True)
                        ShowWindow(campo.Handle, SW_SHOW)
                    End If
                End If

                If menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed Then
                    EnableWindow(menuopzioni.Handle, False)
                    ShowWindow(menuopzioni.Handle, SW_HIDE)
                End If
            End If
        End Try
    End Sub

    Private Sub CostruisciMenuPrincipaleWord()
        Try
            listaOpzioni.Clear()
            listaOpzioni.AddRange(listaOpzioniEsterne)
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return

            If campoWord IsNot Nothing AndAlso Not campoWord.SolaLettura Then
                aggiungiopzioneInterna("Inserisci", AddressOf MostraMenuInserisci)
            End If
            If Not HaOpzioneEsterna("Stampa") Then
                aggiungiopzioneInterna("Stampa", AddressOf StampaCampo)
            End If
            aggiungiopzioneInterna("Converti in mp3", AddressOf ConvertiCampoInMp3)
            aggiungiopzioneInterna("Riassumi il testo", AddressOf RiassumiCampoConAi)
            aggiungiopzioneInterna(VoceTraduci(), AddressOf TraduciCampo)
            aggiungiopzioneInterna(If(campoWord IsNot Nothing AndAlso campoWord.SolaLettura, "Trova", "Trova e sostituisci"), AddressOf MostraMenuTrovaSostituisci)
            aggiungiopzioneInterna("Note", AddressOf MostraMenuNoteCommentiWord)
            If campoWord IsNot Nothing AndAlso Not campoWord.SolaLettura Then
                aggiungiopzioneInterna("Formato pagina", AddressOf MostraMenuFormatoPaginaWord)
                aggiungiopzioneInterna("Elimina tabelle duplicate", AddressOf EliminaTabelleDuplicateWord)
                If WGDettatura.SistemaSupportato Then
                    aggiungiopzioneInterna("Dettatura", AddressOf AvviaDettaturaCampo)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function HaOpzioneEsterna(nome As String) As Boolean
        Try
            If listaOpzioniEsterne Is Nothing Then Return False
            For Each opz As Opzione In listaOpzioniEsterne
                If String.Equals(opz.Testo, nome, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("HaOpzioneEsterna: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Sub RiassumiCampoConAi(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ottienitesto()
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Il campo è vuoto, non c'è nulla da riassumere.")
                msgVuoto.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msgConfig As New WGMessaggi()
                msgConfig.Messaggio("Nessuna chiave API configurata per l'intelligenza artificiale. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msgConfig.ShowDialog()
                Return
            End If

            Dim processo As New WGProcessoInCorso()
            processo.Avvia("Sto elaborando il riassunto, attendere", False)

            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiTestoAsync(testo)

            processo.Fine()

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Non è stato possibile ottenere il riassunto: " & If(risultato IsNot Nothing, risultato.Item2, "errore sconosciuto"))
                msgErrore.ShowDialog()
                Return
            End If

            Dim campoRisultato As New WGCampo()
            campoRisultato.init("Riassunto", risultato.Item1, True, True, False, False)
            campoRisultato.avvia()
        Catch ex As Exception
            log.ScriviLog("RiassumiCampoConAi: " & ex.Message)
        End Try
    End Sub

    Private Function TestoSelezionatoCampo() As String
        Try
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then Return campoWord.TestoSelezionato()
            If campo IsNot Nothing AndAlso Not campo.IsDisposed Then Return campo.SelectedText
            Return ""
        Catch ex As Exception
            log.ScriviLog("TestoSelezionatoCampo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function VoceTraduci() As String
        Dim lingua As String = WGTraduttore.ALingua(WGTraduttore.LinguaDestinazione)
        Return If(String.IsNullOrWhiteSpace(TestoSelezionatoCampo()), "Traduci il testo " & lingua, "Traduci il testo selezionato " & lingua)
    End Function

    ' Traduce la selezione se c'è, altrimenti tutto il testo, e mostra la traduzione in un campo di sola lettura.
    Private Sub TraduciCampo(sender As Object, e As EventArgs)
        Try
            Dim selezione As String = TestoSelezionatoCampo()
            Dim testo As String = If(String.IsNullOrWhiteSpace(selezione), OttieniTestoPerMp3(), selezione)
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Il campo è vuoto, non c'è nulla da tradurre.")
                msgVuoto.ShowDialog()
                Return
            End If
            WGTraduttore.TraduciEMostra(testo, If(String.IsNullOrWhiteSpace(selezione), "Traduzione", "Traduzione della selezione"))
        Catch ex As Exception
            log.ScriviLog("TraduciCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub aggiungiopzioneInterna(testo As String, evento As EventHandler)
        Try
            Dim opz As New Opzione
            opz.Testo = testo
            opz.Azione = evento
            listaOpzioni.Add(opz)
        Catch ex As Exception
            log.ScriviLog("aggiungiopzioneInterna: " & ex.Message)
        End Try
    End Sub

    Private Sub CostruisciMenuFormattazioneWord()
        Try
            listaOpzioniFormattazione.Clear()
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return

            If campoWord IsNot Nothing AndAlso Not campoWord.SolaLettura Then
                aggiungiopzioneFormattazioneInterna("Carattere", AddressOf MostraMenuCarattere)
                aggiungiopzioneFormattazioneInterna("Paragrafo", AddressOf MostraMenuParagrafo)
                aggiungiopzioneFormattazioneInterna("Operazioni paragrafo", AddressOf MostraMenuOperazioniParagrafoWord)
            End If

            If campoWord IsNot Nothing Then
                Dim stato = campoWord.OttieniStatoCursore()
                If stato IsNot Nothing AndAlso stato.InTabella Then
                    If Not campoWord.SolaLettura Then
                        aggiungiopzioneFormattazioneInterna("Tabella", AddressOf MostraMenuTabellaFormattazione)
                    End If
                    aggiungiopzioneFormattazioneInterna("Cella", AddressOf MostraMenuCellaWord)
                End If

                Dim isLink As Boolean = False

                SyncLock campoWord._elementsLock
                    If campoWord._caretIndex >= 0 AndAlso campoWord._caretIndex < campoWord._elements.Count Then
                        Dim el = campoWord._elements(campoWord._caretIndex)
                        Dim checkPara = Sub(p As DocParagraph)
                                            Dim charCount As Integer = 0
                                            For Each r In p.Runs
                                                If Not r.IsImage Then
                                                    Dim runLen As Integer = r.Text.Length
                                                    If campoWord._caretCharIndex >= charCount AndAlso campoWord._caretCharIndex < charCount + runLen Then
                                                        If Not String.IsNullOrEmpty(r.LinkUrl) Then
                                                            isLink = True
                                                        End If
                                                        Exit For
                                                    End If
                                                    charCount += runLen
                                                End If
                                            Next
                                        End Sub
                        If TypeOf el Is DocParagraph Then
                            checkPara(DirectCast(el, DocParagraph))
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            If campoWord._tblRow >= 0 AndAlso campoWord._tblRow < tbl.Rows.Count AndAlso campoWord._tblCol >= 0 AndAlso campoWord._tblCol < tbl.Rows(campoWord._tblRow).Cells.Count Then
                                Dim cell = tbl.Rows(campoWord._tblRow).Cells(campoWord._tblCol)
                                If campoWord._tblPara >= 0 AndAlso campoWord._tblPara < cell.Paragraphs.Count Then
                                    checkPara(cell.Paragraphs(campoWord._tblPara))
                                End If
                            End If
                        End If
                    End If
                End SyncLock

                If Not campoWord.SolaLettura Then
                    Dim bookmarkNotaCorrente As String = campoWord.EBookmarkNotaAllaPosizioneCorrente()

                    If isLink AndAlso String.IsNullOrEmpty(bookmarkNotaCorrente) Then
                        aggiungiopzioneFormattazioneInterna("Link", AddressOf MostraMenuLinkFormattazione)
                        aggiungiopzioneFormattazioneInterna("Elimina collegamento", AddressOf EliminaCollegamentoWordMenu)
                    ElseIf isLink Then
                        aggiungiopzioneFormattazioneInterna("Link", AddressOf MostraMenuLinkFormattazione)
                    End If

                    If Not String.IsNullOrEmpty(bookmarkNotaCorrente) Then
                        aggiungiopzioneFormattazioneInterna("Elimina nota", AddressOf EliminaNotaWord)
                    Else
                        aggiungiopzioneFormattazioneInterna("Inserisci nota", AddressOf InserisciNotaAPiePaginaWord)
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub aggiungiopzioneFormattazioneInterna(testo As String, evento As EventHandler)
        Try
            Dim opz As New Opzione
            opz.Testo = testo
            opz.Azione = evento
            listaOpzioniFormattazione.Add(opz)
        Catch ex As Exception
            log.ScriviLog("aggiungiopzioneFormattazioneInterna: " & ex.Message)
        End Try
    End Sub

    Private Sub CostruisciMenuPrincipaleRtf()
        Try
            listaOpzioni.Clear()
            listaOpzioni.AddRange(listaOpzioniEsterne)
            If campo Is Nothing OrElse campo.IsDisposed Then Return

            If Not HaOpzioneEsterna("Stampa") Then
                aggiungiopzioneInterna("Stampa", AddressOf StampaCampo)
            End If
            aggiungiopzioneInterna("Converti in mp3", AddressOf ConvertiCampoInMp3)
            aggiungiopzioneInterna("Riassumi il testo", AddressOf RiassumiCampoConAi)
            aggiungiopzioneInterna(VoceTraduci(), AddressOf TraduciCampo)
            aggiungiopzioneInterna(If(campo.ReadOnly, "Trova", "Trova e sostituisci"), AddressOf MostraMenuTrovaSostituisciRtf)
            If Not campo.ReadOnly Then
                If campo.multiriga Then
                    aggiungiopzioneInterna("Aggiungi parola selezionata al dizionario", AddressOf aggiungidizionario)
                    aggiungiopzioneInterna("Correggi testo (F7)", AddressOf EseguiControlloOrtograficoDaMenu)
                End If
                If WGDettatura.SistemaSupportato Then
                    aggiungiopzioneInterna("Dettatura", AddressOf AvviaDettaturaCampo)
                End If
            End If
            aggiungiopzioneInterna("Chiudi campo", AddressOf campochiudi)
        Catch ex As Exception
            log.ScriviLog("CostruisciMenuPrincipaleRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub CostruisciMenuFormattazioneRtf()
        Try
            listaOpzioniFormattazione.Clear()
            If campo Is Nothing OrElse campo.IsDisposed Then Return

            If Not campo.ReadOnly Then
                aggiungiopzioneFormattazioneInterna("Annulla", AddressOf annullaRtf)
                aggiungiopzioneFormattazioneInterna("Taglia", AddressOf TagliaTesto)
            End If
            aggiungiopzioneFormattazioneInterna("Copia", AddressOf CopiaTesto)
            If Not campo.ReadOnly Then
                aggiungiopzioneFormattazioneInterna("Incolla", AddressOf IncollaTesto)
                aggiungiopzioneFormattazioneInterna("Ripristina", AddressOf ripristinaRtf)
            End If
            aggiungiopzioneFormattazioneInterna("Seleziona riga corrente", AddressOf SelezionaRigaCorrente)
            aggiungiopzioneFormattazioneInterna("Seleziona tutto", AddressOf SelezionaTutto)

            If Not campo.ReadOnly Then
                aggiungiopzioneFormattazioneInterna("Carattere", AddressOf MostraMenuCarattereRtf)
                aggiungiopzioneFormattazioneInterna("Paragrafo", AddressOf MostraMenuParagrafoRtf)
            End If
        Catch ex As Exception
            log.ScriviLog("CostruisciMenuFormattazioneRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub vaia()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Vai a riga", AddressOf vaiARigaMenu)
            menu.aggiungi("Vai a pagina", AddressOf vaiAPaginaMenu)
            menu.avvia("Vai a", True)
        Catch ex As Exception
            log.ScriviLog("vaia: " & ex.Message)
        End Try
    End Sub

    Private Sub apriopzioni(Optional isFormattazione As Boolean = False)
        Try
            If MenuOpzioniAperto OrElse isClosing OrElse Me.IsDisposed OrElse Me.Disposing Then
                Exit Sub
            End If
            _altPremutoDaHook = False
            _shiftPremutoDaHook = False
            If isWordMode AndAlso campoWord IsNot Nothing Then
                campoWord.disattivahook()
                campoWord.Visible = False
                campoWord.Enabled = False

                If isFormattazione Then
                    CostruisciMenuFormattazioneWord()
                Else
                    CostruisciMenuPrincipaleWord()
                End If
            ElseIf Not isWordMode AndAlso campo IsNot Nothing Then
                campo.disattivahook()
                campo.Visible = False
                campo.Enabled = False
                If isFormattazione Then
                    CostruisciMenuFormattazioneRtf()
                Else
                    CostruisciMenuPrincipaleRtf()
                End If
            End If

            _isMenuFormattazioneAperto = isFormattazione
            Dim listaDaUsare As List(Of Opzione) = If(isFormattazione, listaOpzioniFormattazione, listaOpzioni)

            If listaDaUsare.Count > 0 Then
                If menuopzioni IsNot Nothing Then
                    ' Un'apertura precedente fallita a meta' puo' aver lasciato la lista agganciata a Me.Controls.
                    Try
                        If Not menuopzioni.IsDisposed Then
                            Me.Controls.Remove(menuopzioni)
                            menuopzioni.Dispose()
                        End If
                    Catch exVecchio As Exception
                        log.ScriviLog("apriopzioni (rimozione menu precedente): " & exVecchio.Message)
                    End Try
                    menuopzioni = Nothing
                End If

                menuopzioni = New WGListaView()
                menuopzioni.Location = New Point(10, _titoloHeight + 10)

                If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    menuopzioni.Width = campoWord.Width
                ElseIf Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                    menuopzioni.Width = campo.Width
                Else
                    menuopzioni.Width = Math.Max(100, Me.ClientSize.Width - 20)
                End If
                menuopzioni.Height = 200
                Me.Controls.Add(menuopzioni)

                For Each opz As Opzione In listaDaUsare
                    menuopzioni.Items.Add(opz.Testo)
                Next

                If menuopzioni.Items.Count > 0 Then
                    menuopzioni.SelectedIndex = 0
                End If

                MenuOpzioniAperto = True

                menuopzioni.Titolo = If(isFormattazione, "Menù Formattazione " & titolo, "Menù opzioni " & titolo)
                EnableWindow(menuopzioni.Handle, True)
                ShowWindow(menuopzioni.Handle, SW_SHOW)

                menuopzioni.Focus()
                SetFocus(menuopzioni.Handle)

                If isWordMode Then
                    If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                        EnableWindow(campoWord.Handle, False)
                        ShowWindow(campoWord.Handle, SW_HIDE)
                    End If
                ElseIf campo IsNot Nothing Then
                    If Not campo.IsDisposed Then
                        EnableWindow(campo.Handle, False)
                        ShowWindow(campo.Handle, SW_HIDE)
                    End If
                End If
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono opzioni disponibili")
                messaggio.ShowDialog()
                If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    ' Enabled/Visible vanno ripristinati PRIMA di Focus()/SetFocus(): vedi il
                    ' commento sullo stesso fix in ChiudiMenuOpzioni. Qui il campo era gia' stato
                    ' nascosto e disabilitato piu' sopra in questo stesso metodo (righe iniziali)
                    ' e, non essendoci opzioni, non passa mai dal ramo che lo riattiva: senza
                    ' questo ripristino il campo restava permanentemente invisibile e disabilitato.
                    campoWord.RiprendiFocus(False)
                ElseIf Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                    campo.Enabled = True
                    campo.Visible = True
                    campo.Focus()
                    SetFocus(campo.Handle)
                    campo.attivahook()
                    attaccatastiera()
                End If
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
                chiudi()
            End If
        End Try
    End Sub

    Private Sub ChiudiMenuOpzioniConAnnuncioTitolo()
        Try
            ChiudiMenuOpzioni()
            If lettura IsNot Nothing Then AnnunciaTitoloETipo()
        Catch ex As Exception
            log.ScriviLog("ChiudiMenuOpzioniConAnnuncioTitolo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuOpzioni()
        Try
            If isClosing Then
                Return
            End If

            MenuOpzioniAperto = False
            _isMenuFormattazioneAperto = False
            ultimoChiusuraMenu = Environment.TickCount

            If menuopzioni IsNot Nothing Then
                If Not menuopzioni.IsDisposed Then
                    EnableWindow(menuopzioni.Handle, False)
                    ShowWindow(menuopzioni.Handle, SW_HIDE)
                End If
                Me.Controls.Remove(menuopzioni)
                menuopzioni.Dispose()
                menuopzioni = Nothing
            End If

            If isWordMode = True Then
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    EnableWindow(campoWord.Handle, True)
                    ShowWindow(campoWord.Handle, SW_SHOW)
                    ' Enabled/Visible (proprieta' WinForms, non solo stato nativo della finestra)
                    ' vanno ripristinati PRIMA di chiedere il focus: WGWord.WndProc ignora
                    ' WM_SETFOCUS quando Me.Visible e' ancora False (vedi la guardia
                    ' "If Me.IsDisposed OrElse Not Me.Visible Then" in WGWord.vb), quindi se si
                    ' chiama Focus()/SetFocus() mentre Visible e' ancora False il caret non viene
                    ' ricreato e la notifica di rientro nel campo (UpdateCaretAndNotify) non
                    ' scatta mai: il campo appare selezionato ma resta sordo alla tastiera.
                    campoWord.RiprendiFocus(True)
                End If
            ElseIf campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                EnableWindow(campo.Handle, True)
                ShowWindow(campo.Handle, SW_SHOW)
                ' Stesso fix del ramo Word qui sopra.
                campo.Enabled = True
                campo.Visible = True
                campo.Focus()
                SetFocus(campo.Handle)
                campo.attivahook()
                attaccatastiera()
                If Not lettura.StaParlando Then campo.LeggiParolaSottoCursore()
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub chiudi()
        Try
            If isClosing Then
                Return
            End If

            isClosing = True

            rilasciatastiera()
            If isWordMode AndAlso campoWord IsNot Nothing Then
                campoWord.disattivahook()
            ElseIf Not isWordMode AndAlso campo IsNot Nothing Then
                campo.disattivahook()
            End If

            If autoSaveTimer IsNot Nothing Then
                autoSaveTimer.Stop()
                autoSaveTimer.Dispose()
                autoSaveTimer = Nothing
            End If

            If campo IsNot Nothing Then
                RemoveHandler campo.KeyPress, AddressOf numericocontrollo
                RemoveHandler campo.RichiestaChiusura, AddressOf campo_RichiestaChiusura
            End If


            RemoveHandler Me.VisibleChanged, AddressOf gestisciVisibilita
            RemoveHandler MyBase.Load, AddressOf WGFormCampi_Load

            If campoWord IsNot Nothing Then
                campoWord.Dispose()
                campoWord = Nothing
            End If

            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub inizializza()
        Try
            If Me.DesignMode() Then
                Exit Sub
            End If

            If menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed AndAlso menuopzioni.Items.Count > 0 Then
                menuopzioni.SelectedIndex = 0
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentOutOfRangeException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub numericocontrollo(sender As Object, e As KeyPressEventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.solonumeri AndAlso Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then
                e.Handled = True
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non sono consentite lettere o altri simboli, solo numeri")
                messaggio.ShowDialog()
            ElseIf campo.ReadOnly AndAlso Not Char.IsControl(e.KeyChar) Then
                e.Handled = True
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non è possibile scrivere in questo contesto poichè è un campo di sola lettura")
                messaggio.ShowDialog()
            Else
                campo.GestisciCharMessaggio(e.KeyChar)
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(ex.Message)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub InitializeComponent()
        Try
            If Me.DesignMode() Then
                Exit Sub
            End If
            Me.SuspendLayout()
            Me.ClientSize = New System.Drawing.Size(282, 253)
            Me.Name = "WGCampi"
            Me.ResumeLayout(False)
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub WGFormCampi_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Try
            If Me.DesignMode() Then
                Exit Sub
            End If

            autoSaveTimer = New System.Windows.Forms.Timer()
            autoSaveTimer.Interval = 600000
            autoSaveTimer.Start()


            If isWordMode Then
                InizializzaCampoWord()
                If Not String.IsNullOrEmpty(_pathPdf) AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    Dim pdfDaImportare As String = _pathPdf
                    Dim solaLetturaImport As Boolean = _solaLetturaPdf
                    _pathPdf = ""
                    Me.BeginInvoke(New MethodInvoker(Sub()
                                                         Try
                                                             If Me.IsDisposed OrElse Me.Disposing Then Return
                                                             Me.Visible = False
                                                             Try
                                                                 EseguiImportazionePdfConAttesa(pdfDaImportare, solaLetturaImport)
                                                             Finally
                                                                 If Not Me.IsDisposed AndAlso Not Me.Disposing Then
                                                                     Me.Visible = True
                                                                     Me.BringToFront()
                                                                 End If
                                                             End Try
                                                             If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                                                                 campoWord.Focus()
                                                                 SetFocus(campoWord.Handle)
                                                             End If
                                                         Catch exPdf As Exception
                                                             log.ScriviLog("WGFormCampi_Load (importazione pdf differita): " & exPdf.Message & Environment.NewLine & exPdf.StackTrace)
                                                         End Try
                                                     End Sub))
                End If
            ElseIf campo IsNot Nothing Then
                campo.Location = New Point(10, _titoloHeight + 10)
                campo.Width = Me.ClientSize.Width - 20
                campo.Height = Me.ClientSize.Height - _titoloHeight - 20
                campo.Font = New Font("Arial", 18, FontStyle.Regular, GraphicsUnit.Point, CType(0, Byte), False)
                campo.WordWrap = False
                campo.RightToLeft = RightToLeft.No
                Me.Controls.Add(campo)

                campo.Focus()
                SetFocus(campo.Handle)

                If campo.multiriga Then
                    campo.Multiline = True
                Else
                    campo.Multiline = False
                End If
                AddHandler campo.KeyPress, AddressOf numericocontrollo

                campo.SelectionStart = 0
            End If

        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As System.Runtime.InteropServices.COMException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() Then
                Throw
            Else
            End If
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Try
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

            If Not String.IsNullOrEmpty(titolo) AndAlso Not isWordMode Then
                Dim titoloRect As New Rectangle(0, 0, Me.Width, _titoloHeight)
                Dim sfumatoChiara As Color = If(WGTema.Scuro, Color.FromArgb(60, 60, 60), Color.FromArgb(225, 225, 225))
                Using lgb As New LinearGradientBrush(titoloRect, sfumatoChiara, WGTema.Sfondo, LinearGradientMode.Vertical)
                    g.FillRectangle(lgb, titoloRect)
                End Using

                Using sfTitolo As New StringFormat()
                    sfTitolo.Alignment = StringAlignment.Center
                    sfTitolo.LineAlignment = StringAlignment.Center

                    Using bTitolo As New SolidBrush(WGTema.Testo)
                        Using fontTitolo As New Font("Arial", 24, FontStyle.Regular)
                            g.DrawString(titolo, fontTitolo, bTitolo, titoloRect, sfTitolo)
                        End Using
                    End Using
                End Using
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub TagliaTestoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.CutSelection()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub CopiaTestoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.CopySelection()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub IncollaTestoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.PasteText()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SelezionaTuttoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.SelectAll()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AggiungiRigaTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AggiungiRigaTabella()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub RimuoviRigaTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim selezione As Tuple(Of Integer, Integer) = campoWord.OttieniSelezioneRigheTabella()
            If selezione IsNot Nothing Then
                campoWord.RimuoviRigheTabellaDaPosizione(selezione.Item1, selezione.Item2)
            Else
                campoWord.RimuoviRigaTabella()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private _campiQuantitaTabella As Campi = Nothing
    Private _tipoRimozioneTabella As String = ""

    ' Se l'utente apre la rimozione righe, esce senza confermare e poi apre quella colonne, il
    ' vecchio modulo resta agganciato e alla sua conferma agirebbe sul tipo sbagliato: va sganciato
    ' prima di crearne uno nuovo.
    Private Sub ChiudiCampiQuantitaTabella()
        Try
            If _campiQuantitaTabella Is Nothing Then Return

            Dim daChiudere As Campi = _campiQuantitaTabella
            _campiQuantitaTabella = Nothing

            Try
                RemoveHandler daChiudere.Fine, AddressOf FineQuantitaRimozioneTabella
            Catch exRimozione As Exception
                log.ScriviLog("ChiudiCampiQuantitaTabella (rimozione handler): " & exRimozione.Message)
            End Try

            Try
                daChiudere = Nothing
            Catch exSmaltimento As Exception
                log.ScriviLog("ChiudiCampiQuantitaTabella (smaltimento): " & exSmaltimento.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("ChiudiCampiQuantitaTabella: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviPiuRigheTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                Dim selezione As Tuple(Of Integer, Integer) = campoWord.OttieniSelezioneRigheTabella()
                If selezione IsNot Nothing Then
                    campoWord.RimuoviRigheTabellaDaPosizione(selezione.Item1, selezione.Item2)
                    Return
                End If
            End If
            ChiudiCampiQuantitaTabella()
            _tipoRimozioneTabella = "RIGHE"
            _campiQuantitaTabella = New Campi()
            _campiQuantitaTabella.Aggiungi("Quante righe vuoi rimuovere, a partire da quella corrente", "c.quantita", True)
            AddHandler _campiQuantitaTabella.Fine, AddressOf FineQuantitaRimozioneTabella
            _campiQuantitaTabella.Avvia()
        Catch ex As Exception
            log.ScriviLog("RimuoviPiuRigheTabellaMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviPiuColonneTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                Dim selezione As Tuple(Of Integer, Integer) = campoWord.OttieniSelezioneColonneTabella()
                If selezione IsNot Nothing Then
                    campoWord.RimuoviColonneTabellaDaPosizione(selezione.Item1, selezione.Item2)
                    Return
                End If
            End If
            ChiudiCampiQuantitaTabella()
            _tipoRimozioneTabella = "COLONNE"
            _campiQuantitaTabella = New Campi()
            _campiQuantitaTabella.Aggiungi("Quante colonne vuoi rimuovere, a partire da quella corrente", "c.quantita", True)
            AddHandler _campiQuantitaTabella.Fine, AddressOf FineQuantitaRimozioneTabella
            _campiQuantitaTabella.Avvia()
        Catch ex As Exception
            log.ScriviLog("RimuoviPiuColonneTabellaMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub FineQuantitaRimozioneTabella(sender As Object, e As EventArgs)
        Try
            If _campiQuantitaTabella Is Nothing Then Return
            Dim testoQuantita As String = _campiQuantitaTabella.RecuperaValore("c.quantita")
            RemoveHandler _campiQuantitaTabella.Fine, AddressOf FineQuantitaRimozioneTabella
            _campiQuantitaTabella = Nothing

            Dim quantita As Integer
            If Not Integer.TryParse(testoQuantita, quantita) OrElse quantita <= 0 Then
                RipristinaFocus()
                Return
            End If

            If campoWord IsNot Nothing Then
                If _tipoRimozioneTabella = "RIGHE" Then
                    campoWord.RimuoviRigheTabella(quantita)
                ElseIf _tipoRimozioneTabella = "COLONNE" Then
                    campoWord.RimuoviColonneTabella(quantita)
                End If
            End If
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("FineQuantitaRimozioneTabella: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiungiColonnaTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AggiungiColonnaTabella()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub RimuoviColonnaTabellaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim selezione As Tuple(Of Integer, Integer) = campoWord.OttieniSelezioneColonneTabella()
            If selezione IsNot Nothing Then
                campoWord.RimuoviColonneTabellaDaPosizione(selezione.Item1, selezione.Item2)
            Else
                campoWord.RimuoviColonnaTabella()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub OttieniStatoFormattazione(ByRef isBold As Boolean, ByRef isItalic As Boolean, ByRef isUnderline As Boolean, ByRef isStrike As Boolean, ByRef currentAlignment As String)
        isBold = False
        isItalic = False
        isUnderline = False
        isStrike = False
        currentAlignment = "Left"

        Try
            If campoWord IsNot Nothing Then
                SyncLock campoWord._elementsLock
                    If campoWord._caretIndex >= 0 AndAlso campoWord._caretIndex < campoWord._elements.Count Then
                        Dim el = campoWord._elements(campoWord._caretIndex)

                        If TypeOf el Is DocTable Then
                            Dim tbl = DirectCast(el, DocTable)
                            If campoWord._tblRow >= 0 AndAlso campoWord._tblRow < tbl.Rows.Count AndAlso campoWord._tblCol >= 0 AndAlso campoWord._tblCol < tbl.Rows(campoWord._tblRow).Cells.Count Then
                                Dim cell = tbl.Rows(campoWord._tblRow).Cells(campoWord._tblCol)
                                If campoWord._tblPara >= 0 AndAlso campoWord._tblPara < cell.Paragraphs.Count Then
                                    el = cell.Paragraphs(campoWord._tblPara)
                                End If
                            End If
                        End If

                        If TypeOf el Is DocParagraph Then
                            Dim para As DocParagraph = DirectCast(el, DocParagraph)
                            currentAlignment = para.Alignment

                            Dim charCount As Integer = 0
                            For Each r In para.Runs
                                Dim runLen = Math.Max(1, r.Text.Length)
                                If campoWord._caretCharIndex >= charCount AndAlso campoWord._caretCharIndex < charCount + runLen Then
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
                End SyncLock
            End If
        Catch ex As Exception
            log.ScriviLog("OttieniStatoFormattazione Error: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuCarattere(sender As Object, e As EventArgs)
        Try
            Dim isBold As Boolean, isItalic As Boolean, isUnderline As Boolean, isStrike As Boolean, align As String = ""
            OttieniStatoFormattazione(isBold, isItalic, isUnderline, isStrike, align)

            Dim m As New WGMenu()
            m.aggiungi("Tipo carattere", AddressOf cambiaTipoCarattere)
            m.aggiungi("Dimensione carattere", AddressOf cambiaDimensioneCarattere)
            m.aggiungi("Colore carattere", AddressOf cambiaColoreCarattere)
            m.aggiungi("Colore evidenziatore", AddressOf cambiaColoreEvidenziatore)

            m.aggiungi(If(isBold, "Rimuovi Grassetto", "Applica Grassetto"), AddressOf applicaGrassettoM)
            m.aggiungi(If(isItalic, "Rimuovi Corsivo", "Applica Corsivo"), AddressOf applicaCorsivoM)
            m.aggiungi(If(isUnderline, "Rimuovi Sottolineato", "Applica Sottolineato"), AddressOf applicaSottolineatoM)
            m.aggiungi(If(isStrike, "Rimuovi Barrato", "Applica Barrato"), AddressOf applicaBarratoM)

            m.aggiungi("Rimuovi Formattazione", AddressOf rimuoviFormattazioneM)
            m.avvia("Carattere", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuParagrafo(sender As Object, e As EventArgs)
        Try
            Dim isBold As Boolean, isItalic As Boolean, isUnderline As Boolean, isStrike As Boolean, align As String = ""
            OttieniStatoFormattazione(isBold, isItalic, isUnderline, isStrike, align)

            Dim m As New WGMenu()
            m.aggiungi(If(align.Equals("Left", StringComparison.OrdinalIgnoreCase), "Allinea a Sinistra (Attivo)", "Allinea a Sinistra"), AddressOf allineaSinistra)
            m.aggiungi(If(align.Equals("Center", StringComparison.OrdinalIgnoreCase), "Allinea al Centro (Attivo)", "Allinea al Centro"), AddressOf allineaCentro)
            m.aggiungi(If(align.Equals("Right", StringComparison.OrdinalIgnoreCase), "Allinea a Destra (Attivo)", "Allinea a Destra"), AddressOf allineaDestra)
            m.aggiungi(If(align.Equals("Justify", StringComparison.OrdinalIgnoreCase), "Giustifica Testo (Attivo)", "Giustifica Testo"), AddressOf allineaGiustificato)

            m.aggiungi("Aumenta Rientro", AddressOf aumentaRientroMenu)
            m.aggiungi("Riduci Rientro", AddressOf riduciRientroMenu)
            m.avvia("Paragrafo", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuTabellaFormattazione(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Aggiungi riga tabella", AddressOf AggiungiRigaTabellaMenu)
            m.aggiungi("Rimuovi riga tabella", AddressOf RimuoviRigaTabellaMenu)
            m.aggiungi("Rimuovi più righe tabella", AddressOf RimuoviPiuRigheTabellaMenu)
            m.aggiungi("Aggiungi colonna tabella", AddressOf AggiungiColonnaTabellaMenu)
            m.aggiungi("Rimuovi colonna tabella", AddressOf RimuoviColonnaTabellaMenu)
            m.aggiungi("Rimuovi più colonne tabella", AddressOf RimuoviPiuColonneTabellaMenu)
            m.avvia("Tabella", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuLinkFormattazione(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Modifica didascalia", AddressOf ModificaDidascaliaLink)
            m.aggiungi("Modifica url", AddressOf ModificaUrlLink)
            m.avvia("Link", True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModificaDidascaliaLink(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Nuova didascalia", "c.didascalia", True)
            AddHandler campiForm.Fine, AddressOf fineModificaDidascaliaLink
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineModificaDidascaliaLink(sender As Object, e As EventArgs)
        Try
            Dim nuova As String = campiForm.RecuperaValore("c.didascalia")
            If Not String.IsNullOrEmpty(nuova) Then
                UpdateCurrentLinkProperty(nuova, True)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineModificaDidascaliaLink
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModificaUrlLink(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Nuovo URL", "c.url", True)
            AddHandler campiForm.Fine, AddressOf fineModificaUrlLink
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineModificaUrlLink(sender As Object, e As EventArgs)
        Try
            Dim nuova As String = campiForm.RecuperaValore("c.url")
            If Not String.IsNullOrEmpty(nuova) Then
                UpdateCurrentLinkProperty(nuova, False)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineModificaUrlLink
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub UpdateCurrentLinkProperty(nuovoValore As String, isDidascalia As Boolean)
        Try
            If campoWord Is Nothing Then Exit Sub

            Dim updated As Boolean = False

            SyncLock campoWord._elementsLock
                If campoWord._caretIndex >= 0 AndAlso campoWord._caretIndex < campoWord._elements.Count Then
                    Dim el As Object = campoWord._elements(campoWord._caretIndex)

                    If TypeOf el Is DocParagraph Then
                        updated = UpdateLinkInParagraph(DirectCast(el, DocParagraph), nuovoValore, isDidascalia, campoWord._caretCharIndex)

                    ElseIf TypeOf el Is DocTable Then
                        Dim tbl As DocTable = DirectCast(el, DocTable)

                        If campoWord._tblRow >= 0 AndAlso campoWord._tblRow < tbl.Rows.Count Then
                            Dim row = tbl.Rows(campoWord._tblRow)

                            If campoWord._tblCol >= 0 AndAlso campoWord._tblCol < row.Cells.Count Then
                                Dim cell = row.Cells(campoWord._tblCol)

                                If campoWord._tblPara >= 0 AndAlso campoWord._tblPara < cell.Paragraphs.Count Then
                                    updated = UpdateLinkInParagraph(cell.Paragraphs(campoWord._tblPara), nuovoValore, isDidascalia, campoWord._caretCharIndex)
                                End If
                            End If
                        End If
                    End If
                End If
            End SyncLock

            If updated Then
                campoWord.AssestaParagrafi()
            End If

        Catch ex As Exception
            log.ScriviLog("UpdateCurrentLinkProperty: " & ex.ToString())
        End Try
    End Sub

    Private Function UpdateLinkInParagraph(p As DocParagraph, nuovoValore As String, isDidascalia As Boolean, caretCharIndex As Integer) As Boolean
        Try
            Dim updated As Boolean = False
            Dim charCount As Integer = 0

            For Each r In p.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then Continue For

                Dim runLen As Integer = If(r.Text IsNot Nothing, r.Text.Length, 0)

                If caretCharIndex >= charCount AndAlso caretCharIndex < charCount + runLen Then
                    If Not String.IsNullOrEmpty(r.LinkUrl) Then
                        If isDidascalia Then
                            r.Text = nuovoValore
                        Else
                            r.LinkUrl = nuovoValore
                        End If
                        updated = True
                    End If

                    Exit For
                End If

                charCount += runLen
            Next

            Return updated

        Catch ex As Exception
            log.ScriviLog("UpdateLinkInParagraph: " & ex.ToString())
            Return False
        End Try
    End Function
    Private Sub cambiaTipoCarattere(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                campoWord.MostraMenuFont()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub cambiaDimensioneCarattere(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                campoWord.MostraMenuDimensioni()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub cambiaColoreCarattere(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Inserisci colore", "c.colore", True)
            AddHandler campiForm.Fine, AddressOf fineColore
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineColore(sender As Object, e As EventArgs)
        Try
            Dim colStr As String = campiForm.RecuperaValore("c.colore")
            If Not String.IsNullOrEmpty(colStr) Then
                If campoWord IsNot Nothing Then
                    campoWord.ImpostaColoreFont(colStr)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineColore
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub cambiaColoreEvidenziatore(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Inserisci colore evidenziatore", "c.colore_evidenziatore", True)
            AddHandler campiForm.Fine, AddressOf fineColoreEvidenziatore
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineColoreEvidenziatore(sender As Object, e As EventArgs)
        Try
            Dim colStr As String = campiForm.RecuperaValore("c.colore_evidenziatore")
            If Not String.IsNullOrEmpty(colStr) Then
                If campoWord IsNot Nothing Then
                    campoWord.ImpostaEvidenziatore(colStr)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineColoreEvidenziatore
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub applicaGrassettoM(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaGrassetto()
        Catch ex As Exception
            log.ScriviLog("applicaGrassettoM: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaCorsivoM(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaCorsivo()
        Catch ex As Exception
            log.ScriviLog("applicaCorsivoM: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaSottolineatoM(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaSottolineato()
        Catch ex As Exception
            log.ScriviLog("applicaSottolineatoM: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaBarratoM(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaBarrato()
        Catch ex As Exception
            log.ScriviLog("applicaBarratoM: " & ex.Message)
        End Try
    End Sub

    Private Sub rimuoviFormattazioneM(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RimuoviFormattazione()
        Catch ex As Exception
            log.ScriviLog("rimuoviFormattazioneM: " & ex.Message)
        End Try
    End Sub

    Private Sub allineaSinistra(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ImpostaAllineamento("Left")
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub allineaCentro(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ImpostaAllineamento("Center")
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub allineaDestra(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ImpostaAllineamento("Right")
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub allineaGiustificato(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ImpostaAllineamento("Justify")
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuInserisci(sender As Object, e As EventArgs)
        Try
            Dim menuinserisci As New WGMenu()
            menuinserisci.aggiungi("Link", AddressOf inserisciLinkMenu)
            menuinserisci.aggiungi("Elenco puntato", AddressOf InserisciElencoPuntato)
            menuinserisci.aggiungi("Elenco numerato", AddressOf InserisciElencoNumerato)
            menuinserisci.aggiungi("Tabella", AddressOf inseriscitabella)
            menuinserisci.avvia("Menù inserisci", True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub inserisciLinkMenu(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Nome URL", "c.nome", True)
            campiForm.Aggiungi("URL", "c.url", True)
            AddHandler campiForm.Fine, AddressOf fineInserisciLink
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineInserisciLink(sender As Object, e As EventArgs)
        Try
            Dim nome As String = campiForm.RecuperaValore("c.nome")
            Dim url As String = campiForm.RecuperaValore("c.url")
            If Not String.IsNullOrEmpty(nome) AndAlso Not String.IsNullOrEmpty(url) Then
                If campoWord IsNot Nothing Then
                    campoWord.InserisciLink(nome, url)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineInserisciLink
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub aggiungiIndiceMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                campoWord.AggiungiAIndice()
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InserisciElencoPuntato(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciElencoPuntato()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InserisciElencoNumerato(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciElencoNumerato()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoTabelle(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.MostraElencoTabelle()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub inseriscitabella(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.AggiungiNumerico("Numero Righe", "c.righe", True)
            campiForm.AggiungiNumerico("Numero Colonne", "C.Colonne")
            AddHandler campiForm.Fine, AddressOf chiusuracampi
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub chiusuracampi(sender As Object, e As EventArgs)
        Try
            Dim righeStr As String = campiForm.RecuperaValore("c.righe")
            Dim colStr As String = campiForm.RecuperaValore("C.Colonne")
            Dim righe As Integer = 0
            Dim colonne As Integer = 0
            If Integer.TryParse(righeStr, righe) AndAlso righe > 0 Then
                If Not Integer.TryParse(colStr, colonne) OrElse colonne <= 0 Then colonne = 1
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    campoWord.InserisciTabella(righe, colonne)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        Finally
            ' Anche in caso di errore l'handler va sganciato e il focus deve tornare al documento,
            ' altrimenti l'utente resta senza nessun controllo attivo.
            Try
                RemoveHandler campiForm.Fine, AddressOf chiusuracampi
            Catch exRimozione As Exception
                log.ScriviLog("chiusuracampi (rimozione handler): " & exRimozione.Message)
            End Try
            RipristinaFocus()
        End Try
    End Sub

    Private Sub MostraMenuTrovaSostituisci(sender As Object, e As EventArgs)
        Try
            Dim menuTrova As New WGMenu()
            menuTrova.aggiungi("Ricerca", AddressOf ricercaWord)
            If campoWord IsNot Nothing AndAlso Not campoWord.SolaLettura Then
                menuTrova.aggiungi("Sostituzione", AddressOf sostituisciWord)
                menuTrova.aggiungi("Sostituzione intero documento", AddressOf sostituisciTuttoWord)
            End If
            menuTrova.avvia(If(campoWord IsNot Nothing AndAlso campoWord.SolaLettura, "Trova", "Trova e sostituisci"), True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ricercaWord(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Scrivi cosa cercare", "c.ricerca", True)
            AddHandler campiForm.Fine, AddressOf chiusuracampiricercaWord
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub chiusuracampiricercaWord(sender As Object, e As EventArgs)
        Try
            Dim testoDaCercare As String = campiForm.RecuperaValore("c.ricerca")
            If Not String.IsNullOrEmpty(testoDaCercare) Then
                If campoWord IsNot Nothing Then
                    campoWord.CercaTesto(testoDaCercare)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf chiusuracampiricercaWord
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub sostituisciWord(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Testo da cercare", "c.cerca", True)
            campiForm.Aggiungi("Sostituisci con", "c.sostituisci", True)
            AddHandler campiForm.Fine, AddressOf fineSostituisciWord
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineSostituisciWord(sender As Object, e As EventArgs)
        Try
            Dim cerca As String = campiForm.RecuperaValore("c.cerca")
            Dim sostituisci As String = campiForm.RecuperaValore("c.sostituisci")
            If Not String.IsNullOrEmpty(cerca) Then
                If campoWord IsNot Nothing Then
                    campoWord.SostituisciTesto(cerca, sostituisci)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineSostituisciWord
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub sostituisciTuttoWord(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Testo da cercare", "c.cerca", True)
            campiForm.Aggiungi("Sostituisci con", "c.sostituisci", True)
            AddHandler campiForm.Fine, AddressOf fineSostituisciTuttoWord
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineSostituisciTuttoWord(sender As Object, e As EventArgs)
        Try
            Dim cerca As String = campiForm.RecuperaValore("c.cerca")
            Dim sostituisci As String = campiForm.RecuperaValore("c.sostituisci")
            If Not String.IsNullOrEmpty(cerca) Then
                If campoWord IsNot Nothing Then
                    campoWord.SostituisciTesto(cerca, sostituisci)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineSostituisciTuttoWord
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuGestioneIndicePrincipale(sender As Object, e As EventArgs)
        Try
            Dim menuIndice As New WGMenu()
            If campoWord IsNot Nothing AndAlso Not campoWord.SolaLettura Then
                menuIndice.aggiungi("Aggiungi voce all'indice", AddressOf aggiungiIndiceMenu)
            End If

            If campoWord IsNot Nothing AndAlso campoWord.EsisteIndice() Then
                menuIndice.aggiungi("Naviga e gestisci voci dell'indice", AddressOf navigaIndiceMenu)
                menuIndice.aggiungi("A quale sezione appartiene il cursore?", AddressOf SezioneCorrenteIndiceMenu)
                menuIndice.aggiungi("Vai a sezione successiva", AddressOf VaiASezioneSuccessivaMenu)
                menuIndice.aggiungi("Vai a sezione precedente", AddressOf VaiASezionePrecedenteMenu)
                If Not campoWord.SolaLettura Then
                    menuIndice.aggiungi("Rimuovi voce corrente dall'indice", AddressOf RimuoviVoceCorrenteMenu)
                    menuIndice.aggiungi("Elimina indice completamente", AddressOf EliminaIndiceCompletamenteMenu)
                End If
            End If
            menuIndice.avvia("Gestione Indice", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SezioneCorrenteIndiceMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.OttieniSezioneCorrenteIndice()
        Catch ex As Exception
            log.ScriviLog("SezioneCorrenteIndiceMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub VaiASezioneSuccessivaMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiASezioneSuccessivaIndice()
        Catch ex As Exception
            log.ScriviLog("VaiASezioneSuccessivaMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub VaiASezionePrecedenteMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiASezionePrecedenteIndice()
        Catch ex As Exception
            log.ScriviLog("VaiASezionePrecedenteMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviVoceCorrenteMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RimuoviVoceCorrenteDaIndice()
        Catch ex As Exception
            log.ScriviLog("RimuoviVoceCorrenteMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaIndiceCompletamenteMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.EliminaIndiceCompletamente()
        Catch ex As Exception
            log.ScriviLog("EliminaIndiceCompletamenteMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub navigaIndiceMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.MostraMenuGestioneIndice()
        Catch ex As Exception
            log.ScriviLog("navigaIndiceMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuMaiuscoleMinuscole(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("TUTTO MAIUSCOLO", AddressOf maiuscoloMenu)
            m.aggiungi("tutto minuscolo", AddressOf minuscoloMenu)
            m.aggiungi("Iniziali Maiuscole", AddressOf inizialiMaiuscoleMenu)
            m.avvia("Maiuscole/Minuscole", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub maiuscoloMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ConvertiSelezioneMaiuscolo()
        Catch ex As Exception
            log.ScriviLog("maiuscoloMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub minuscoloMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ConvertiSelezioneMinuscolo()
        Catch ex As Exception
            log.ScriviLog("minuscoloMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub inizialiMaiuscoleMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ConvertiSelezioneInizialiMaiuscole()
        Catch ex As Exception
            log.ScriviLog("inizialiMaiuscoleMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub rimuoviRigheVuoteMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RimuoviRigheVuoteSuperflue()
        Catch ex As Exception
            log.ScriviLog("rimuoviRigheVuoteMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub rimuoviSpaziExtraMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RimuoviSpaziVuotiExtra()
        Catch ex As Exception
            log.ScriviLog("rimuoviSpaziExtraMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuGestioneIntestazionePiePagina(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim menu As New WGMenu()
            menu.aggiungi(If(campoWord.SolaLettura, "Gestisci intestazione, sola lettura", "Gestisci intestazione"), AddressOf intestazioneWordMenu)
            menu.aggiungi(If(campoWord.SolaLettura, "Gestisci piè di pagina, sola lettura", "Gestisci piè di pagina"), AddressOf piePaginaWordMenu)
            menu.avvia("Intestazione e piè di pagina", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuGestioneIntestazionePiePagina: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub intestazioneWordMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiModificaIntestazione()
        Catch ex As Exception
            log.ScriviLog("intestazioneWordMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub piePaginaWordMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiModificaPiePagina()
        Catch ex As Exception
            log.ScriviLog("piePaginaWordMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub aumentaRientroMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AumentaRientro()
        Catch ex As Exception
            log.ScriviLog("aumentaRientroMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub riduciRientroMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RiduciRientro()
        Catch ex As Exception
            log.ScriviLog("riduciRientroMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub annullaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.Undo()
        Catch ex As Exception
            log.ScriviLog("annullaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub ripristinaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.Redo()
        Catch ex As Exception
            log.ScriviLog("ripristinaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub annullaRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.Undo()
        Catch ex As Exception
            log.ScriviLog("annullaRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub ripristinaRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.Redo()
        Catch ex As Exception
            log.ScriviLog("ripristinaRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub vaiARigaMenu(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.AggiungiNumerico("Numero riga", "c.riga", True)
            AddHandler campiForm.Fine, AddressOf fineVaiARiga
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineVaiARiga(sender As Object, e As EventArgs)
        Try
            Dim rigaStr As String = campiForm.RecuperaValore("c.riga")
            Dim riga As Integer = 0
            If Integer.TryParse(rigaStr, riga) Then
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    campoWord.VaiARiga(riga)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                RemoveHandler campiForm.Fine, AddressOf fineVaiARiga
            Catch exRimozione As Exception
                log.ScriviLog("fineVaiARiga (rimozione handler): " & exRimozione.Message)
            End Try
            RipristinaFocus()
        End Try
    End Sub

    Private Sub vaiAPaginaMenu(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.AggiungiNumerico("Numero pagina", "c.pagina", True)
            AddHandler campiForm.Fine, AddressOf fineVaiAPagina
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineVaiAPagina(sender As Object, e As EventArgs)
        Try
            Dim pagStr As String = campiForm.RecuperaValore("c.pagina")
            Dim pagina As Integer = 0
            If Integer.TryParse(pagStr, pagina) Then
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    campoWord.VaiAPagina(pagina)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                RemoveHandler campiForm.Fine, AddressOf fineVaiAPagina
            Catch exRimozione As Exception
                log.ScriviLog("fineVaiAPagina (rimozione handler): " & exRimozione.Message)
            End Try
            RipristinaFocus()
        End Try
    End Sub

    Private Sub statisticheMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                campoWord.StatisticheDocumento()
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub recuperaTestiMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then
                campoWord.MostraTestiDaRecuperare()
            End If
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub aggiungidizionario(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then
                campo.AggiungiParolaSelezionataAlDizionario()
            End If
        Catch ex As Exception
            log.ScriviLog("aggiungidizionario: " & ex.Message)
        Finally
            ChiudiMenuOpzioni()
        End Try
    End Sub

    Private Sub EseguiControlloOrtograficoDaMenu(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then
                campo.EseguiControlloOrtografico(Nothing, EventArgs.Empty)
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiControlloOrtograficoDaMenu: " & ex.Message)
        Finally
            ChiudiMenuOpzioni()
        End Try
    End Sub

    Private Sub MostraMenuTrovaSostituisciRtf(sender As Object, e As EventArgs)
        Try
            ' Il menu viene costruito prima e aperto dopo: se nel frattempo il campo e' stato chiuso
            ' si esce prima di istanziare WGMenu, che altrimenti resterebbe agganciato alla tastiera.
            If campo Is Nothing OrElse campo.IsDisposed Then Return

            Dim solaLettura As Boolean = campo.ReadOnly
            Dim menuTrova As New WGMenu()
            menuTrova.aggiungi("Ricerca", AddressOf ricercaRtf)
            If Not solaLettura Then
                menuTrova.aggiungi("Sostituzione", AddressOf sostituisciRtf)
                menuTrova.aggiungi("Sostituzione intero documento", AddressOf sostituisciTuttoRtf)
            End If
            menuTrova.avvia(If(solaLettura, "Trova", "Trova e sostituisci"), True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ricercaRtf(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Scrivi cosa cercare", "c.ricerca", True)
            AddHandler campiForm.Fine, AddressOf fineRicercaRtf
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineRicercaRtf(sender As Object, e As EventArgs)
        Try
            Dim testoDaCercare As String = campiForm.RecuperaValore("c.ricerca")
            If Not String.IsNullOrEmpty(testoDaCercare) AndAlso campo IsNot Nothing Then
                campo.CercaTesto(testoDaCercare)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineRicercaRtf
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub sostituisciRtf(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Testo da cercare", "c.cerca", True)
            campiForm.Aggiungi("Sostituisci con", "c.sostituisci", True)
            AddHandler campiForm.Fine, AddressOf fineSostituisciRtf
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineSostituisciRtf(sender As Object, e As EventArgs)
        Try
            Dim cerca As String = campiForm.RecuperaValore("c.cerca")
            Dim sostituisci As String = campiForm.RecuperaValore("c.sostituisci")
            If Not String.IsNullOrEmpty(cerca) AndAlso campo IsNot Nothing Then
                campo.SostituisciTesto(cerca, sostituisci)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineSostituisciRtf
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub sostituisciTuttoRtf(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Testo da cercare", "c.cerca", True)
            campiForm.Aggiungi("Sostituisci con", "c.sostituisci", True)
            AddHandler campiForm.Fine, AddressOf fineSostituisciTuttoRtf
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineSostituisciTuttoRtf(sender As Object, e As EventArgs)
        Try
            Dim cerca As String = campiForm.RecuperaValore("c.cerca")
            Dim sostituisci As String = campiForm.RecuperaValore("c.sostituisci")
            If Not String.IsNullOrEmpty(cerca) AndAlso campo IsNot Nothing Then
                campo.SostituisciTestoTutto(cerca, sostituisci)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineSostituisciTuttoRtf
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuCarattereRtf(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tipo carattere", AddressOf cambiaTipoCarattereRtf)
            m.aggiungi("Dimensione carattere", AddressOf cambiaDimensioneCarattereRtf)
            m.aggiungi("Colore carattere", AddressOf cambiaColoreCarattereRtf)
            m.aggiungi("Colore sfondo evidenziatore", AddressOf cambiaColoreEvidenziatoreRtf)

            m.aggiungi("Applica/Rimuovi Grassetto", AddressOf applicaGrassettoRtf)
            m.aggiungi("Applica/Rimuovi Corsivo", AddressOf applicaCorsivoRtf)
            m.aggiungi("Applica/Rimuovi Sottolineato", AddressOf applicaSottolineatoRtf)
            m.aggiungi("Applica/Rimuovi Barrato", AddressOf applicaBarratoRtf)

            m.aggiungi("Rimuovi Formattazione", AddressOf rimuoviFormattazioneRtf)
            m.avvia("Carattere", True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuParagrafoRtf(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Allinea a Sinistra", AddressOf allineaSinistraRtf)
            m.aggiungi("Allinea al Centro", AddressOf allineaCentroRtf)
            m.aggiungi("Allinea a Destra", AddressOf allineaDestraRtf)
            m.avvia("Paragrafo", True)
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub cambiaTipoCarattereRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.MostraMenuFont()
        Catch ex As Exception
            log.ScriviLog("cambiaTipoCarattereRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub cambiaDimensioneCarattereRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.MostraMenuDimensioni()
        Catch ex As Exception
            log.ScriviLog("cambiaDimensioneCarattereRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub cambiaColoreCarattereRtf(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Inserisci colore", "c.colore", True)
            AddHandler campiForm.Fine, AddressOf fineColoreRtf
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineColoreRtf(sender As Object, e As EventArgs)
        Try
            Dim colStr As String = campiForm.RecuperaValore("c.colore")
            If Not String.IsNullOrEmpty(colStr) AndAlso campo IsNot Nothing Then
                campo.ImpostaColoreFont(colStr)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineColoreRtf
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub cambiaColoreEvidenziatoreRtf(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Inserisci colore sfondo", "c.colore_evidenziatore", True)
            AddHandler campiForm.Fine, AddressOf fineColoreEvidenziatoreRtf
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineColoreEvidenziatoreRtf(sender As Object, e As EventArgs)
        Try
            Dim colStr As String = campiForm.RecuperaValore("c.colore_evidenziatore")
            If Not String.IsNullOrEmpty(colStr) AndAlso campo IsNot Nothing Then
                campo.ImpostaEvidenziatore(colStr)
            End If
            RemoveHandler campiForm.Fine, AddressOf fineColoreEvidenziatoreRtf
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub applicaGrassettoRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ApplicaGrassetto()
        Catch ex As Exception
            log.ScriviLog("applicaGrassettoRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaCorsivoRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ApplicaCorsivo()
        Catch ex As Exception
            log.ScriviLog("applicaCorsivoRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaSottolineatoRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ApplicaSottolineato()
        Catch ex As Exception
            log.ScriviLog("applicaSottolineatoRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub applicaBarratoRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ApplicaBarrato()
        Catch ex As Exception
            log.ScriviLog("applicaBarratoRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub rimuoviFormattazioneRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.RimuoviFormattazione()
        Catch ex As Exception
            log.ScriviLog("rimuoviFormattazioneRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub allineaSinistraRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ImpostaAllineamento("Left")
        Catch ex As Exception
            log.ScriviLog("allineaSinistraRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub allineaCentroRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ImpostaAllineamento("Center")
        Catch ex As Exception
            log.ScriviLog("allineaCentroRtf: " & ex.Message)
        End Try
    End Sub

    Private Sub allineaDestraRtf(sender As Object, e As EventArgs)
        Try
            If campo IsNot Nothing Then campo.ImpostaAllineamento("Right")
        Catch ex As Exception
            log.ScriviLog("allineaDestraRtf: " & ex.Message)
        End Try
    End Sub

    Private _dettaturaCampo As WGDettatura
    Private _dettaturaCampoInCorso As Boolean = False
    Private _tastoDettaturaCampo As WGTasto

    Private Sub MostraTastoDettaturaCampo()
        Try
            If _tastoDettaturaCampo Is Nothing Then
                _tastoDettaturaCampo = New WGTasto()
                AddHandler _tastoDettaturaCampo.Interrotto, AddressOf OnTastoDettaturaCampoInterrotto
            End If
            _tastoDettaturaCampo.Mostra("Sto ascoltando. Parla ora. Premi un tasto qualsiasi per terminare la dettatura.")
        Catch ex As Exception
            log.ScriviLog("MostraTastoDettaturaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastoDettaturaCampo()
        Try
            If _tastoDettaturaCampo IsNot Nothing Then
                _tastoDettaturaCampo.Chiudi()
            End If
        Catch ex As Exception
            log.ScriviLog("NascondiTastoDettaturaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTastoDettaturaCampoInterrotto(sender As Object, e As EventArgs)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New EventHandler(AddressOf OnTastoDettaturaCampoInterrotto), sender, e)
                Return
            End If
            NascondiTastoDettaturaCampo()
            FermaDettaturaCampo()
        Catch ex As Exception
            log.ScriviLog("OnTastoDettaturaCampoInterrotto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaDettaturaCampo(sender As Object, e As EventArgs)
        Try
            If Not WGDettatura.SistemaSupportato Then Return
            Dim campoValido As Boolean = (Not isWordMode AndAlso campo IsNot Nothing AndAlso Not campo.IsDisposed AndAlso Not campo.ReadOnly) OrElse
                                          (isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso Not campoWord.SolaLettura)
            If Not campoValido Then Return

            If _dettaturaCampo Is Nothing Then
                _dettaturaCampo = New WGDettatura()
                AddHandler _dettaturaCampo.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampo
                AddHandler _dettaturaCampo.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampo
            End If

            If Not _dettaturaCampo.ModelloDisponibile Then
                lettura.LeggiTesto("Chiave API di Gemini non configurata")
                Return
            End If

            _dettaturaCampoInCorso = True
            MostraTastoDettaturaCampo()
            _dettaturaCampo.IniziaRegistrazione()
        Catch ex As Exception
            log.ScriviLog("AvviaDettaturaCampo: " & ex.Message)
            _dettaturaCampoInCorso = False
        End Try
    End Sub

    Private _processoDettaturaCampo As WGProcessoInCorso

    Private Sub MostraProcessoDettaturaCampo()
        Try
            If _processoDettaturaCampo Is Nothing Then
                _processoDettaturaCampo = New WGProcessoInCorso()
            End If
            _processoDettaturaCampo.Avvia("Riconoscimento del parlato in corso, attendere", False)
        Catch ex As Exception
            log.ScriviLog("MostraProcessoDettaturaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiProcessoDettaturaCampo()
        Try
            If _processoDettaturaCampo IsNot Nothing Then
                _processoDettaturaCampo.Fine()
            End If
        Catch ex As Exception
            log.ScriviLog("NascondiProcessoDettaturaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaDettaturaCampo()
        Try
            _dettaturaCampoInCorso = False
            NascondiTastoDettaturaCampo()
            If _dettaturaCampo IsNot Nothing Then
                MostraProcessoDettaturaCampo()
                _dettaturaCampo.FermaRegistrazione()
            End If
        Catch ex As Exception
            log.ScriviLog("FermaDettaturaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneCompletataCampo(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneCompletataCampo), testo)
                Return
            End If
            NascondiProcessoDettaturaCampo()
            If String.IsNullOrEmpty(testo) Then Return
            If isWordMode Then
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed AndAlso Not campoWord.SolaLettura Then
                    For Each carattereTesto As Char In testo
                        campoWord.GestisciChar(carattereTesto)
                    Next
                End If
            Else
                If campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                    campo.SelectedText = testo
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("OnTrascrizioneCompletataCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneFallitaCampo(messaggioErrore As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneFallitaCampo), messaggioErrore)
                Return
            End If
            NascondiProcessoDettaturaCampo()
            lettura.LeggiTesto(messaggioErrore)
        Catch ex As Exception
            log.ScriviLog("OnTrascrizioneFallitaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub campochiudi(sender As Object, e As EventArgs)
        Try
            If isClosing Then
                Return
            End If
            SopprimiChiusuraAutomatica = False
            RaiseEvent chiusura(Me, Nothing)
            If SopprimiChiusuraAutomatica Then Return
            chiudi()
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub TagliaTesto(sender As Object, e As EventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.ReadOnly = True Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il campo è di sola lettura, impossibile tagliare il testo")
                messaggio.ShowDialog()
            Else
                If campo.SelectedText <> "" Then
                    campo.Cut()
                Else
                    Dim messaggio As New WGMessaggi
                    messaggio.Messaggio("Nessun testo selezionato")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
                chiudi()
            End If
        End Try
    End Sub

    Private Sub CopiaTesto(sender As Object, e As EventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.SelectedText <> "" Then
                campo.Copy()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun testo selezionato")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ExternalException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
                chiudi()
            End If
        End Try
    End Sub

    Private Sub IncollaTesto(sender As Object, e As EventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.ReadOnly = True Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Impossibile incollare il testo, questo è un campo di sola lettura")
                messaggio.ShowDialog()
            Else
                If Clipboard.ContainsText Then
                    Dim testoDaIncollare As String = Clipboard.GetText()
                    If campo.solonumeri = True AndAlso Not Me.ContieneSoloNumeri(testoDaIncollare) Then
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Questo campo accetta solo numeri.")
                        messaggio.ShowDialog()
                        Return
                    End If

                    If campo.multiriga = False AndAlso Not TestoSingolaRiga(testoDaIncollare) Then
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Questo campo accetta solo una riga di testo.")
                        messaggio.ShowDialog()
                        Return
                    End If
                    campo.Paste()
                    campo.ApplicaMaiuscoleGlobali()
                End If
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As FormatException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ExternalException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message & ex.StackTrace)
                messaggio.ShowDialog()
                chiudi()
            End If
        End Try
    End Sub

    Private Sub SelezionaTutto(sender As Object, e As EventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.Text.Length > 0 Then
                campo.SelectAll()
                lettura.LeggiTesto("Selezionato tutto")
            Else
                Dim messaggio As New WGMessaggi
                messaggio.Messaggio("Non è presente testo da poter selezionare")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub SelezionaRigaCorrente(sender As Object, e As EventArgs)
        Try
            If campo Is Nothing Then Return
            If campo.Text.Length > 0 Then
                Dim posizioneCursore As Integer = campo.SelectionStart
                Dim indiceRiga As Integer = campo.GetLineFromCharIndex(posizioneCursore)
                Dim inizioRiga As Integer = campo.GetFirstCharIndexFromLine(indiceRiga)
                Dim fineRiga As Integer = campo.GetFirstCharIndexFromLine(indiceRiga + 1)

                If fineRiga = -1 Then
                    fineRiga = campo.Text.Length
                End If

                campo.Select(inizioRiga, fineRiga - inizioRiga)
            Else
                Dim messaggio As New WGMessaggi
                messaggio.Messaggio("Nessun testo da selezionare")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentOutOfRangeException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
            End If
        End Try
    End Sub

    Private Function TestoSingolaRiga(testo As String) As Boolean
        Try
            For Each c As Char In testo
                If c = Microsoft.VisualBasic.ControlChars.Cr OrElse c = Microsoft.VisualBasic.ControlChars.Lf Then
                    Return False
                End If
            Next
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
                chiudi()
                Return False
            End If
        End Try
    End Function

    Private Function ContieneSoloNumeri(testo As String) As Boolean
        Try
            Return testo.All(AddressOf Char.IsDigit)
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(ex.Message)
                messaggio.ShowDialog()
                chiudi()
                Return False
            End If
        End Try
    End Function

    Public Function ottienitesto() As String
        Try
            If campo IsNot Nothing Then Return campo.Text
            Return String.Empty
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Sub LoadFile(percorso As String)
        Try
            pathRtf = percorso
            If campo IsNot Nothing Then
                campo.LoadFile(percorso)
                campo.Modified = False
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub SaveFile(percorso As String)
        Try
            pathRtf = percorso
            If campo IsNot Nothing Then
                campo.SaveFile(percorso)
                campo.Modified = False
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}{ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub gestisciVisibilita(sender As Object, e As EventArgs) Handles Me.VisibleChanged
        Try
            If Me.Visible Then
                ultimoChiusuraMenu = Environment.TickCount
                If Not MenuOpzioniAperto Then AnnunciaIngressoRinviato()
                If Not MenuOpzioniAperto Then
                    If isWordMode AndAlso campoWord IsNot Nothing Then
                        campoWord.RiprendiFocus(False)
                    Else
                        If campo IsNot Nothing Then
                            campo.attivahook()
                            attaccatastiera()
                        End If
                        Me.Focus()
                        If campo IsNot Nothing AndAlso Not campo.IsDisposed Then
                            campo.Focus()
                            SetFocus(campo.Handle)
                        End If
                    End If
                End If
            Else
                If isWordMode AndAlso campoWord IsNot Nothing Then
                    campoWord.disattivahook()
                ElseIf Not isWordMode AndAlso campo IsNot Nothing Then
                    campo.disattivahook()
                    rilasciatastiera()
                End If
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}GestisciVisibilita - Errore InvalidOperation: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}GestisciVisibilita - Errore NullReference: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}GestisciVisibilita - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        End Try
    End Sub


    Private Sub MostraMenuNavigaWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Vai all'inizio del paragrafo", AddressOf VaiAInizioParagrafoWord)
            menu.aggiungi("Vai alla fine del paragrafo", AddressOf VaiAFineParagrafoWord)
            menu.aggiungi("Vai al paragrafo successivo", AddressOf VaiAParagrafoSuccessivoWord)
            menu.aggiungi("Vai al paragrafo precedente", AddressOf VaiAParagrafoPrecedenteWord)
            menu.aggiungi("Vai alla pagina successiva", AddressOf VaiAPaginaSuccessivaWord)
            menu.aggiungi("Vai alla pagina precedente", AddressOf VaiAPaginaPrecedenteWord)
            menu.aggiungi("Vai a immagine successiva", AddressOf VaiAImmagineSuccessivaWord)
            menu.aggiungi("Vai a immagine precedente", AddressOf VaiAImmaginePrecedenteWord)
            menu.aggiungi("Vai a tabella successiva", AddressOf VaiATabellaSuccessivaWord)
            menu.aggiungi("Vai a tabella precedente", AddressOf VaiATabellaPrecedenteWord)
            menu.aggiungi("Vai a intestazione successiva", AddressOf VaiAIntestazioneSuccessivaWord)
            menu.aggiungi("Vai a intestazione precedente", AddressOf VaiAIntestazionePrecedenteWord)
            menu.avvia("Naviga", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAInizioParagrafoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAInizioParagrafo()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAFineParagrafoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAFineParagrafo()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAParagrafoSuccessivoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAParagrafoSuccessivo()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAParagrafoPrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAParagrafoPrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAPaginaSuccessivaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAPaginaSuccessiva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAPaginaPrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAPaginaPrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAImmagineSuccessivaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAImmagineSuccessiva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAImmaginePrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAImmaginePrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiATabellaSuccessivaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiATabellaSuccessiva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiATabellaPrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiATabellaPrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAIntestazioneSuccessivaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAIntestazioneSuccessiva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub VaiAIntestazionePrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.VaiAIntestazionePrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraStrutturaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.MostraStrutturaDocumento()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuNoteCommentiWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim menu As New WGMenu()
            If Not campoWord.SolaLettura Then
                menu.aggiungi("Inserisci nota", AddressOf InserisciNotaAPiePaginaWord)
            End If
            If campoWord.EsistonoNote() Then
                menu.aggiungi(If(campoWord.SolaLettura, "Leggi note", "Leggi o modifica note"), AddressOf leggiModificaNoteWordMenu)
                If Not campoWord.SolaLettura Then
                    menu.aggiungi("Ripara note senza a capo", AddressOf ripariaNoteWordMenu)
                End If
            End If
            menu.avvia("Note", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub leggiModificaNoteWordMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiModificaNote()
        Catch ex As Exception
            log.ScriviLog("leggiModificaNoteWordMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ripariaNoteWordMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RiparaNoteSenzaACapo()
        Catch ex As Exception
            log.ScriviLog("ripariaNoteWordMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InserisciNotaAPiePaginaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciNotaAPiePagina()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub EliminaTabelleDuplicateWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim duplicate As Integer = campoWord.ContaTabelleDuplicate()
            If duplicate <= 0 Then
                campoWord.MostraMessaggioStato("Nessuna tabella duplicata trovata")
                Return
            End If
            Dim descrizione As String = If(duplicate = 1, "una tabella duplicata", duplicate.ToString() & " tabelle duplicate")
            Dim menuConferma As New WGMenu()
            menuConferma.aggiungi("Sì, elimina " & descrizione, AddressOf ConfermaEliminaTabelleDuplicate)
            menuConferma.aggiungi("No, non eliminare nulla", AddressOf AnnullaEliminaTabelleDuplicate)
            menuConferma.avvia("Trovate " & descrizione & ". Confermi l'eliminazione?", True)
        Catch ex As Exception
            log.ScriviLog("EliminaTabelleDuplicateWord: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaEliminaTabelleDuplicate(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim rimosse As Integer = campoWord.RimuoviTabelleDuplicate()
            If rimosse <= 0 Then
                campoWord.MostraMessaggioStato("Nessuna tabella duplicata eliminata")
            ElseIf rimosse = 1 Then
                campoWord.MostraMessaggioStato("Eliminata una tabella duplicata. Puoi annullare con control zeta")
            Else
                campoWord.MostraMessaggioStato("Eliminate " & rimosse.ToString() & " tabelle duplicate. Puoi annullare con control zeta")
            End If
        Catch ex As Exception
            log.ScriviLog("ConfermaEliminaTabelleDuplicate: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AnnullaEliminaTabelleDuplicate(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.MostraMessaggioStato("Operazione annullata")
        Catch ex As Exception
            log.ScriviLog("AnnullaEliminaTabelleDuplicate: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaNotaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.EliminaNotaAllaPosizioneCorrente()
        Catch ex As Exception
            log.ScriviLog("EliminaNotaWord: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaCollegamentoWordMenu(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.EliminaCollegamentoAllaPosizioneCorrente()
        Catch ex As Exception
            log.ScriviLog("EliminaCollegamentoWordMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ToggleLetturaPunteggiaturaScritturaWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim nuovoValore As Boolean = Not campoWord.LetturaPunteggiaturaScrittura
            campoWord.config.AggiungiValore("lettura_punteggiatura_scrittura", If(nuovoValore, "1", "0"))
            Dim msg As New WGMessaggi()
            msg.Messaggio(If(nuovoValore, "Lettura punteggiatura in scrittura attivata.", "Lettura punteggiatura in scrittura disattivata."))
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("ToggleLetturaPunteggiaturaScritturaWord: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InserisciNotaDiChiusuraWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciNotaDiChiusura()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InserisciCommentoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciCommento()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiCommentiParagrafoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiCommentiParagrafo()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub EliminaCommentoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.EliminaCommentoCorrente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    
    Private Sub SalvaSubitoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.SalvaSubito()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InfoDocumentoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InfoDocumento()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    ' ==========================================================================
    ' IMPOSTAZIONI DOCUMENTO (modalità Word)
    ' Raccoglie in un unico punto tutte le impostazioni modificabili per il
    ' singolo documento aperto (non le impostazioni generali dell'applicazione
    ' lette da WGConfig, e non quelle di lettura/eco/punteggiatura/correttore).
    ' ==========================================================================
    Private Sub MostraMenuImpostazioniDocumentoWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return

            Dim menu As New WGMenu()
            menu.aggiungi("Formato pagina", AddressOf MostraMenuFormatoPaginaWord)
            menu.aggiungi("Zoom", AddressOf MostraMenuZoomWord)

            If Not campoWord.SolaLettura Then
                menu.aggiungi("Stili titolo", AddressOf MostraMenuStiliWord)

                Dim stato = campoWord.OttieniStatoCursore()
                If stato IsNot Nothing AndAlso stato.InTabella Then
                    menu.aggiungi("Colore sfondo cella", AddressOf MostraMenuColoreSfondoCellaWord)
                End If
            End If

            menu.avvia("Impostazioni documento", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuImpostazioniDocumentoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuFormatoPaginaWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing OrElse campoWord.SolaLettura Then Return

            Dim menu As New WGMenu()
            For Each tipo As String In WGWordUtils.ElencoTipiPaginaDisponibili()
                Dim tipoCatturato As String = tipo
                menu.aggiungi(tipoCatturato, Sub() ApplicaFormatoPaginaWord(tipoCatturato))
            Next
            menu.avvia("Formato pagina", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuFormatoPaginaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaFormatoPaginaWord(tipoPagina As String)
        Try
            If campoWord IsNot Nothing Then campoWord.CambiaFormatoPagina(tipoPagina)
        Catch ex As Exception
            log.ScriviLog("ApplicaFormatoPaginaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuZoomWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return

            Dim menu As New WGMenu()
            menu.aggiungi("Aumenta zoom", AddressOf AumentaZoomWord)
            menu.aggiungi("Riduci zoom", AddressOf RiduciZoomWord)
            menu.avvia("Zoom", True)
        Catch ex As Exception
            log.ScriviLog("MostraMenuZoomWord: " & ex.Message)
        End Try
    End Sub

    Private Sub AumentaZoomWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AumentaZoom()
        Catch ex As Exception
            log.ScriviLog("AumentaZoomWord: " & ex.Message)
        End Try
    End Sub

    Private Sub RiduciZoomWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.RiduciZoom()
        Catch ex As Exception
            log.ScriviLog("RiduciZoomWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuColoreSfondoCellaWord(sender As Object, e As EventArgs)
        Try
            campiForm = New Campi()
            campiForm.Aggiungi("Colore sfondo cella", "c.colore_sfondo_cella", True)
            AddHandler campiForm.Fine, AddressOf fineColoreSfondoCellaWord
            campiForm.Avvia()
        Catch ex As Exception
            log.ScriviLog("MostraMenuColoreSfondoCellaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub fineColoreSfondoCellaWord(sender As Object, e As EventArgs)
        Try
            Dim colStr As String = campiForm.RecuperaValore("c.colore_sfondo_cella")
            If Not String.IsNullOrEmpty(colStr) Then
                If campoWord IsNot Nothing Then
                    campoWord.ImpostaColoreSfondoCella(colStr)
                End If
            End If
            RemoveHandler campiForm.Fine, AddressOf fineColoreSfondoCellaWord
            RipristinaFocus()
        Catch ex As Exception
            log.ScriviLog("fineColoreSfondoCellaWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuStiliWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing OrElse campoWord.SolaLettura Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Titolo 1", AddressOf ApplicaTitolo1Word)
            menu.aggiungi("Titolo 2", AddressOf ApplicaTitolo2Word)
            menu.aggiungi("Titolo 3", AddressOf ApplicaTitolo3Word)
            menu.aggiungi("Titolo 4", AddressOf ApplicaTitolo4Word)
            menu.aggiungi("Titolo 5", AddressOf ApplicaTitolo5Word)
            menu.aggiungi("Titolo 6", AddressOf ApplicaTitolo6Word)
            menu.avvia("Stili titolo", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo1Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(1)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo2Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(2)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo3Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(3)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo4Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(4)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo5Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(5)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTitolo6Word(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ApplicaStileTitolo(6)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuInserisciSpecialeWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing OrElse campoWord.SolaLettura Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Simbolo", AddressOf InserisciSimboloWord)
            menu.aggiungi("Carattere Unicode", AddressOf InserisciUnicodeWord)
            menu.avvia("Inserisci speciale", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InserisciSimboloWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.MostraMenuSimboli()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InserisciUnicodeWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.InserisciCarattereSpeciale()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AggiungiORimuoviIndiceWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AggiungiAIndice()
        Catch ex As Exception
            log.ScriviLog("AggiungiORimuoviIndiceWord: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuOperazioniParagrafoWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing OrElse campoWord.SolaLettura Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Sposta paragrafo in alto", AddressOf SpostaParagrafoSuWord)
            menu.aggiungi("Sposta paragrafo in basso", AddressOf SpostaParagrafoGiuWord)
            menu.aggiungi("Unisci con paragrafo precedente", AddressOf UnisciParagrafoPrecedenteWord)
            menu.aggiungi("Unisci con paragrafo successivo", AddressOf UnisciParagrafoSuccessivoWord)
            menu.aggiungi("Trasponi caratteri attorno al cursore", AddressOf TrasponiWord)
            menu.aggiungi("Capitalizza frase corrente", AddressOf CapitalizzaFraseWord)
            menu.avvia("Operazioni paragrafo", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SpostaParagrafoSuWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.SpostaParagrafoSu()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SpostaParagrafoGiuWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.SpostaParagrafoGiu()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub UnisciParagrafoPrecedenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.UnisciConParagrafoPrecedente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub UnisciParagrafoSuccessivoWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.UnisciConParagrafoSuccessivo()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub TrasponiWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.Trasponi()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub CapitalizzaFraseWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.CapitalizzaFrase()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuCellaWord(sender As Object, e As EventArgs)
        Try
            If campoWord Is Nothing Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Leggi cella corrente", AddressOf LeggiCellaCorrenteWord)
            menu.aggiungi("Leggi intestazione di riga", AddressOf LeggiIntestazioneRigaWord)
            menu.aggiungi("Leggi intestazione di colonna", AddressOf LeggiIntestazioneColonnaWord)
            menu.aggiungi("Leggi tutta la riga", AddressOf LeggiInteraRigaWord)
            menu.aggiungi("Leggi tutta la colonna", AddressOf LeggiInteraColonnaWord)
            If Not campoWord.SolaLettura Then
                menu.aggiungi("Allinea cella a sinistra", AddressOf AllineaCellaSinistraWord)
                menu.aggiungi("Allinea cella al centro", AddressOf AllineaCellaCentroWord)
                menu.aggiungi("Allinea cella a destra", AddressOf AllineaCellaDestraWord)
            End If
            menu.avvia("Cella", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiCellaCorrenteWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiCellaCorrente()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiIntestazioneRigaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiIntestazioneRiga()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiIntestazioneColonnaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiIntestazioneColonna()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiInteraRigaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiInteraRiga()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub LeggiInteraColonnaWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.LeggiInteraColonna()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AllineaCellaSinistraWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AllineaCella("left")
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AllineaCellaCentroWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AllineaCella("center")
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AllineaCellaDestraWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.AllineaCella("right")
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ContaParoleSelezioneWord(sender As Object, e As EventArgs)
        Try
            If campoWord IsNot Nothing Then campoWord.ContaParoleSelezione()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub
    Public Sub ApriPdf(file As String, Optional titolocampo As String = "", Optional solalettura As Boolean = True)
        Try
            If String.IsNullOrWhiteSpace(file) OrElse Not System.IO.File.Exists(file) Then
                Dim m As New WGMessaggi()
                m.Messaggio("File PDF non trovato: " & file)
                m.ShowDialog()
                Return
            End If

            Dim nomePdf As String = System.IO.Path.GetFileName(file)
            titolo = If(String.IsNullOrWhiteSpace(titolocampo), nomePdf, titolocampo)
            isWordMode = True
            _wordSolaLettura = solalettura
            _pathPdf = file
            _solaLetturaPdf = solalettura

            If campo IsNot Nothing Then
                campo.IsWordMode = True
            End If

            If Me.Controls.Contains(campo) Then
                Me.Controls.Remove(campo)
            End If

            If Me.IsHandleCreated Then
                InizializzaCampoWord()
                If campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                    Dim pdfDaImportare As String = _pathPdf
                    Dim solaLetturaImport As Boolean = _solaLetturaPdf
                    _pathPdf = ""
                    EseguiImportazionePdfConAttesa(pdfDaImportare, solaLetturaImport)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub
    
Public Sub EsportaPdf(percorso As String)
    Try
        If campoWord Is Nothing OrElse campoWord.IsDisposed Then
            Dim m As New WGMessaggi()
            m.messaggio("Nessun documento word aperto da esportare.")
            m.ShowDialog()
            Return
        End If
        Dim esito As Boolean = campoWord.EsportaPdf(percorso)
            
    Catch ex As Exception
        log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
    End Try
End Sub
    Public Sub EsportaRtf(percorso As String)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            campoWord.EsportaRtf(percorso)
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub
    Public Sub EsportaMd(percorso As String)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            campoWord.EsportaMd(percorso)
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub
    Public Sub EsportaOdt(percorso As String)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            Dim percorsoOdt As String = AssicuraEstensione(percorso, ".odt")
            SalvaWord(percorsoOdt)
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub
    Public Sub EsportaEpub(percorso As String)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            campoWord.EsportaEpub(percorso)
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub
    Public Sub EsportaTxt(percorso As String)
        Try
            If campoWord Is Nothing OrElse campoWord.IsDisposed Then Return
            campoWord.EsportaTxt(percorso)
        Catch ex As Exception
            log.ScriviLog("Errore: " & ex.Message & ex.StackTrace)
        End Try
    End Sub

End Class