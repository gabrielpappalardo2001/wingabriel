Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGInternet

' Gestione completa dei campi di testo, inclusi la configurazione della tastiera, la dettatura vocale e i menu di opzioni.
Partial Public Class WGCampoTesto
    Inherits System.Windows.Forms.UserControl

    Private Shared log As New wgcontrols.Logger()
    Private WithEvents menuopzioni As New WGListaView
    Private optionEventsOpzioni As New Dictionary(Of String, EventHandler)()
    Private MenuOpzioniAperto As Boolean = False
    Private ultimoelementoopzioni As Integer
    Private WithEvents campo As WGCampoTestoRender
    Private titolo As String
    Private utils As WGFormUtils = WGFormUtils.GetInstance()
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private impostazioniComuni As New WGImpostazioniComuni()
    Private multiriga As Boolean = False
    Private solonumeri As Boolean = False
    Private tastoGiaPremuto As Boolean = False
    Private isRunning As Boolean = False
    Private obbligatorio As Boolean = False
    Private isClosing As Boolean = False
    Private _titoloHeight As Integer = 60
    Private inputAbilitato As Boolean = False

    Private _anchorPoint As Integer = -1
    Private tempoAvvio As DateTime = DateTime.Now

    Private _threadMonitorVisibilita As Thread
    Private _monitorVisibilitaInEsecuzione As Boolean = True

    Private WithEvents _tastiera As WGTastiera
    Private _isHookActive As Boolean = False

    Private _listaCaratteri As WGMenuLista
    Private _listaDimensioni As WGMenuLista
    Private _listaStampantiCampoTesto As WGMenuLista
    Private _listaColori As WGMenuLista

    Private WithEvents _touk As WGTouk
    Private _tastieraTouch As WGTastieraTouch
    Private _dettaturaCampoTesto As WGDettatura
    Private _dettaturaCampoTestoInCorso As Boolean = False

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowLong(hWnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowThreadProcessId(ByVal hWnd As IntPtr, ByRef lpdwProcessId As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EnableWindow(hWnd As IntPtr, bEnable As Boolean) As Boolean
    End Function

    Private Const GWL_STYLE As Integer = -16
    Private Const SW_MAXIMIZE As Integer = 3
    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5

    Public Event chiusura As EventHandler

    Public Sub New()
        Try
            Me.DoubleBuffered = True
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
            tempoAvvio = DateTime.Now

            _threadMonitorVisibilita = New Thread(AddressOf MonitorVisibilitaLoop)
            _threadMonitorVisibilita.IsBackground = True
            _threadMonitorVisibilita.Start()
        Catch ex As Exception
            log.ScriviLog("Errore costruttore WGCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If Not _isHookActive Then Return
            If tasto.None = True Then
                Exit Sub
            End If
            If WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return

            Dim shiftPremuto As Boolean = My.Computer.Keyboard.ShiftKeyDown
            Dim ctrlPremuto As Boolean = My.Computer.Keyboard.CtrlKeyDown
            Dim altPremuto As Boolean = My.Computer.Keyboard.AltKeyDown

            If tasto = Keys.Tab AndAlso altPremuto Then
                If MenuOpzioniAperto Then
                    ChiudiMenuOpzioni()
                End If
                Return
            End If

            If MenuOpzioniAperto Then
                Select Case tasto
                    Case Keys.Enter, Keys.Right
                        If menuopzioni.SelectedIndex >= 0 AndAlso menuopzioni.SelectedIndex < menuopzioni.Items.Count Then
                            Dim testoVoce As String = menuopzioni.SelectedItem.ToString()
                            If optionEventsOpzioni.ContainsKey(testoVoce) Then
                                ChiudiMenuOpzioni()
                                optionEventsOpzioni(testoVoce).Invoke(Nothing, EventArgs.Empty)
                            End If
                        End If
                    Case Keys.Escape, Keys.Left
                        ChiudiMenuOpzioni()
                End Select
                Return
            End If

            If campo Is Nothing Then Return

            Dim insertPremuto As Boolean = Control.IsKeyLocked(Keys.Insert)

            If tasto = Keys.Down AndAlso insertPremuto Then
                campo.LeggiTuttoDaInizioRigaCorrente()
                Return
            End If

            Select Case tasto
                Case Keys.Enter
                    If (DateTime.Now - tempoAvvio).TotalMilliseconds < 200 Then Return

                    If multiriga AndAlso Not campo.SolaLettura Then
                        lettura.LeggiTesto("Nuova riga")
                    Else
                        If obbligatorio AndAlso Not stringavalida(campo.TestoPiatto()) Then
                            Dim messaggio As New WGMessaggi()
                            messaggio.Messaggio("Inserire un valore è obbligatorio")
                            messaggio.ShowDialog()
                            Return
                        End If
                        RaiseEvent chiusura(Me, Nothing)
                    End If

                Case Keys.Escape
                    If (DateTime.Now - tempoAvvio).TotalMilliseconds < 200 Then Return
                    utils.escPremutoAltroControllo = True
                    chiudi()

                Case Keys.A
                    If ctrlPremuto Then SelezionaTutto()

                Case Keys.C
                    If ctrlPremuto Then CopiaTesto()

                Case Keys.X
                    If ctrlPremuto Then TagliaTesto()

                Case Keys.V
                    If ctrlPremuto Then IncollaTesto()

            End Select

        Catch ex As Exception
            log.ScriviLog("Errore Tastiera KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Not _isHookActive Then Return
            If WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return

            If Not MenuOpzioniAperto AndAlso Me.Visible AndAlso Not isClosing Then
                Me.Invoke(Sub() apriopzioni())
            End If
        Catch ex As Exception
            log.ScriviLog("Errore Tastiera AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub Campo_MenuDestroApertura(sender As Object, e As EventArgs)
        Try
            If Not _isHookActive Then Return
            If WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return

            If Not MenuOpzioniAperto AndAlso Me.Visible AndAlso Not isClosing Then
                Me.Invoke(Sub() apriopzioni())
            End If
        Catch ex As Exception
            log.ScriviLog("Errore Campo_MenuDestroApertura: " & ex.Message)
        End Try
    End Sub

    Private Sub MonitorVisibilitaLoop()
        Return
    End Sub

    Private Sub InizializzaCampoTesto()
        Try
            If campo Is Nothing Then
                campo = New WGCampoTestoRender()
                campo.AccessibleRole = AccessibleRole.None
                campo.AccessibleName = Nothing
                AddHandler campo.chiusuraConfermata, AddressOf CampoChiusuraConfermata
                AddHandler campo.chiusuraAnnullata, AddressOf CampoChiusuraAnnullata
                AddHandler campo.menudestroapertura, AddressOf Campo_MenuDestroApertura
            End If
        Catch ex As Exception
            log.ScriviLog($"Errore in InizializzaCampoTesto: {ex.Message}")
        End Try
    End Sub

    Private Sub CampoChiusuraConfermata(sender As Object, e As EventArgs)
        Try
            If obbligatorio AndAlso Not stringavalida(campo.TestoPiatto()) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Inserire un valore è obbligatorio")
                messaggio.ShowDialog()
                Return
            End If
            RaiseEvent chiusura(Me, Nothing)
        Catch ex As Exception
            log.ScriviLog("CampoChiusuraConfermata: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoChiusuraAnnullata(sender As Object, e As EventArgs)
        Try
            utils.escPremutoAltroControllo = True
            chiudi()
        Catch ex As Exception
            log.ScriviLog("CampoChiusuraAnnullata: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            rilasciatastiera()
            If _tastiera IsNot Nothing Then
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
            End If
            If disposing Then
                If Not isClosing Then
                    isClosing = True
                    Try
                        If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
                    Catch exRimuovi As Exception
                        log.ScriviLog($"Errore in Dispose (RimuoviWGForm): {exRimuovi.Message}")
                    End Try
                End If
                _monitorVisibilitaInEsecuzione = False
                If campo IsNot Nothing Then
                    RemoveHandler campo.chiusuraConfermata, AddressOf CampoChiusuraConfermata
                    RemoveHandler campo.chiusuraAnnullata, AddressOf CampoChiusuraAnnullata
                    RemoveHandler campo.menudestroapertura, AddressOf Campo_MenuDestroApertura
                    campo.Dispose()
                End If
                If menuopzioni IsNot Nothing Then
                    menuopzioni.Dispose()
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvantiTesto
                    RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietroTesto
                    RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                    _touk = Nothing
                End If
                If _tastieraTouch IsNot Nothing Then
                    RemoveHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                    RemoveHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                    RemoveHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                    RemoveHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                    RemoveHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
                    If Not _tastieraTouch.IsDisposed Then _tastieraTouch.Dispose()
                    _tastieraTouch = Nothing
                End If
                If _dettaturaCampoTesto IsNot Nothing Then
                    RemoveHandler _dettaturaCampoTesto.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoTesto
                    RemoveHandler _dettaturaCampoTesto.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoTesto
                    _dettaturaCampoTesto.Dispose()
                    _dettaturaCampoTesto = Nothing
                End If
                If _tastoDettaturaCampoTesto IsNot Nothing Then
                    RemoveHandler _tastoDettaturaCampoTesto.Interrotto, AddressOf OnTastoDettaturaCampoTestoInterrotto
                    If Not _tastoDettaturaCampoTesto.IsDisposed Then _tastoDettaturaCampoTesto.Dispose()
                    _tastoDettaturaCampoTesto = Nothing
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"Errore in Dispose: {ex.Message}")
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Sub attaccatastiera()
        Try
            _isHookActive = True
        Catch ex As Exception
            log.ScriviLog($"Errore in attaccatastiera: {ex.Message}")
        End Try
    End Sub

    Private Sub rilasciatastiera()
        Try
            _isHookActive = False
        Catch ex As Exception
            log.ScriviLog($"Errore in rilasciatastiera: {ex.Message}")
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

    Protected Overrides Sub OnLoad(e As EventArgs)
        Try
            MyBase.OnLoad(e)

            attaccatastiera()

            Dim msg As String = titolo
            If campo IsNot Nothing AndAlso Not campo.SolaLettura Then msg &= " Campo di editazione"
            lettura.LeggiTesto(msg, escludiPunteggiatura:=True)
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As ThreadStateException
            log.ScriviLog($"Errore [OnLoad]: ThreadStateException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [OnLoad]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [OnLoad]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Sub init(titolocampo As String, Optional stringa As String = "", Optional multilinea As Boolean = False, Optional solalettura As Boolean = False, Optional numeri As Boolean = False, Optional campoobbligatorio As Boolean = False)
        Try
            InizializzaCampoTesto()
            solonumeri = numeri
            obbligatorio = campoobbligatorio
            campo.Multiriga = multilinea
            campo.SolaLettura = solalettura
            Me.multiriga = multilinea

            If campoobbligatorio Then
                titolo = titolocampo & " * "
            Else
                titolo = titolocampo
            End If


            If Not String.IsNullOrEmpty(stringa) Then
                Dim seg As New SegmentoTesto()
                seg.Testo = stringa
                campo.ImpostaSegmenti(New List(Of SegmentoTesto) From {seg})
            End If
        Catch ex As ArgumentNullException
            log.ScriviLog($"Errore [init]: ArgumentNullException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As OutOfMemoryException
            log.ScriviLog($"Errore [init]: OutOfMemoryException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [init]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [init]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi
        End Try
    End Sub

    Public Sub InitConSegmenti(titolocampo As String, segmenti As List(Of SegmentoTesto), Optional multilinea As Boolean = False, Optional solalettura As Boolean = False, Optional numeri As Boolean = False, Optional campoobbligatorio As Boolean = False)
        Try
            InizializzaCampoTesto()
            solonumeri = numeri
            obbligatorio = campoobbligatorio
            campo.Multiriga = multilinea
            campo.SolaLettura = solalettura
            Me.multiriga = multilinea

            If campoobbligatorio Then
                titolo = titolocampo & " * "
            Else
                titolo = titolocampo
            End If


            If segmenti IsNot Nothing AndAlso segmenti.Count > 0 Then
                campo.ImpostaSegmenti(segmenti)
            End If
        Catch ex As Exception
            log.ScriviLog($"Errore [InitConSegmenti]: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi
        End Try
    End Sub

    Public Async Sub avvia()
        Try

            If Me.DesignMode() Then Exit Sub

            If lettura.NvdaInEsecuzione() AndAlso campo IsNot Nothing Then
                campo.AccessibleName = titolo
            End If
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.MostraControllo(Me)
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [avvia]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [avvia]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [avvia]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Public Function OttieniTesto() As String
        Try
            If campo IsNot Nothing Then Return campo.TestoPiatto()
            Return String.Empty
        Catch ex As Exception
            log.ScriviLog("OttieniTesto: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Function OttieniMetadatiTesto() As List(Of SegmentoTesto)
        Try
            If campo IsNot Nothing Then Return campo.MetadatiTesto()
            Return New List(Of SegmentoTesto)()
        Catch ex As Exception
            log.ScriviLog("OttieniMetadatiTesto: " & ex.Message)
            Return New List(Of SegmentoTesto)()
        End Try
    End Function

    Public Sub aggiungiopzione(testoVoce As String, evento As EventHandler)
        Try
            menuopzioni.Items.Add(testoVoce)
            optionEventsOpzioni(testoVoce) = evento
        Catch ex As ArgumentNullException
            log.ScriviLog($"Errore [aggiungiopzione]: ArgumentNullException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentException
            log.ScriviLog($"Errore [aggiungiopzione]: ArgumentException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [aggiungiopzione]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [aggiungiopzione]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub GestisciInvioOpzioniTesto(sender As Object, e As EventArgs) Handles menuopzioni.InvioPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not MenuOpzioniAperto Then Return
            If menuopzioni.SelectedIndex >= 0 AndAlso menuopzioni.SelectedIndex < menuopzioni.Items.Count Then
                Dim testoVoce As String = menuopzioni.SelectedItem.ToString()
                If optionEventsOpzioni.ContainsKey(testoVoce) Then
                    ChiudiMenuOpzioni()
                    optionEventsOpzioni(testoVoce).Invoke(Nothing, EventArgs.Empty)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("GestisciInvioOpzioniTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEscOpzioniTesto(sender As Object, e As EventArgs) Handles menuopzioni.EscPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not MenuOpzioniAperto Then Return
            ChiudiMenuOpzioni()
        Catch ex As Exception
            log.ScriviLog("GestisciEscOpzioniTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub menuopzioni_SelectedIndexChanged(sender As Object, e As EventArgs)
        Try
            ultimoelementoopzioni = menuopzioni.SelectedIndex
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [menuopzioni_SelectedIndexChanged]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [menuopzioni_SelectedIndexChanged]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [menuopzioni_SelectedIndexChanged]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verrificato un'errore: " & ex.Message)
            messaggio.ShowDialog()
            menuopzioni.Visible = False
            menuopzioni.Enabled = False
            If campo IsNot Nothing Then
                campo.Enabled = True
                campo.Visible = True
            End If
        End Try
    End Sub

    Private Sub apriopzioni()
        Try
            If MenuOpzioniAperto Then Exit Sub

            If menuopzioni.Items.Count > 0 Then
                MenuOpzioniAperto = True
                If campo IsNot Nothing Then
                    campo.Visible = False
                    campo.Enabled = False
                End If

                menuopzioni.Titolo = "Menù opzioni " & titolo
                menuopzioni.Visible = True
                menuopzioni.Enabled = True

                menuopzioni.Bounds = New Rectangle(0, _titoloHeight, Me.ClientSize.Width, Me.ClientSize.Height - _titoloHeight)
                menuopzioni.BringToFront()
                menuopzioni.Focus()

                inizializza()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono opzioni disponibili")
                messaggio.ShowDialog()
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [apriopzioni]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [apriopzioni]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [apriopzioni]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore " & ex.Message)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub ChiudiMenuOpzioni()
        Try
            If isClosing Then Return
            MenuOpzioniAperto = False
            menuopzioni.Visible = False
            menuopzioni.Enabled = False

            If campo IsNot Nothing Then
                EnableWindow(campo.Handle, True)
                ShowWindow(campo.Handle, SW_SHOW)
                SetFocus(campo.Handle)
            End If

            Dim msg As String = titolo
            If campo IsNot Nothing AndAlso Not campo.SolaLettura Then msg &= " Campo di editazione"
            lettura.LeggiTesto(msg, escludiPunteggiatura:=True)
        Catch ex As Exception
            log.ScriviLog($"Errore in ChiudiMenuOpzioni. Dettaglio: {ex.Message}")
        End Try
    End Sub

    Public Sub chiudi()
        Try
            If isClosing Then Return
            isClosing = True

            rilasciatastiera()

            If menuopzioni IsNot Nothing Then
                RemoveHandler menuopzioni.SelectedIndexChanged, AddressOf menuopzioni_SelectedIndexChanged
            End If
            RemoveHandler Me.VisibleChanged, AddressOf gestisciVisibilita
            RemoveHandler MyBase.Load, AddressOf WGFormCampi_Load

            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [chiudi]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [chiudi]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [chiudi]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub inizializza()
        Try
            If Me.DesignMode() Then Exit Sub

            If ultimoelementoopzioni < 0 OrElse ultimoelementoopzioni >= menuopzioni.Items.Count Then
                menuopzioni.SelectedIndex = 0
            Else
                menuopzioni.SelectedIndex = ultimoelementoopzioni
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [inizializza]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [inizializza]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As ArgumentOutOfRangeException
            log.ScriviLog($"Errore [inizializza]: ArgumentOutOfRangeException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [inizializza]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApriMenuCarattere()
        Try
            If campo Is Nothing OrElse campo.SolaLettura Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Tipo carattere", AddressOf TipoCarattere)
            menu.aggiungi("Dimensione carattere", AddressOf DimensioneCarattere)
            menu.aggiungi("Colore carattere", AddressOf ColoreCarattere)
            menu.avvia("Menu carattere", True)
        Catch ex As Exception
            log.ScriviLog("ApriMenuCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub TipoCarattere()
        Try
            _listaCaratteri = New WGMenuLista()
            _listaCaratteri.multiselezioneconsentita = False
            _listaCaratteri.AggiungiElemento("Arial", "Arial")
            _listaCaratteri.AggiungiElemento("Times New Roman", "Times New Roman")
            _listaCaratteri.AggiungiElemento("Verdana", "Verdana")
            _listaCaratteri.AggiungiElemento("Courier New", "Courier New")
            _listaCaratteri.AggiungiElemento("Calibri", "Calibri")
            _listaCaratteri.AggiungiElemento("Tahoma", "Tahoma")
            _listaCaratteri.AggiungiElemento("Georgia", "Georgia")
            _listaCaratteri.AggiungiElemento("Segoe UI", "Segoe UI")
            AddHandler _listaCaratteri.elementoselezionato, AddressOf TipoCarattereSelezionato
            AddHandler _listaCaratteri.escpremuto, AddressOf ChiudiListaCaratteri
            _listaCaratteri.avvia()
        Catch ex As Exception
            log.ScriviLog("TipoCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub TipoCarattereSelezionato(id As String)
        Try
            Dim elementi As List(Of String) = _listaCaratteri.OttieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                Dim nomeFont As String = elementi(0)
                If campo IsNot Nothing Then campo.ApplicaFont(nomeFont)
            End If
        Catch ex As Exception
            log.ScriviLog("TipoCarattereSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaCaratteri()
        Try
            If _listaCaratteri IsNot Nothing Then _listaCaratteri.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiListaCaratteri: " & ex.Message)
        End Try
    End Sub

    Private Sub DimensioneCarattere()
        Try
            _listaDimensioni = New WGMenuLista()
            _listaDimensioni.multiselezioneconsentita = False
            _listaDimensioni.AggiungiElemento("8 punti", "8")
            _listaDimensioni.AggiungiElemento("9 punti", "9")
            _listaDimensioni.AggiungiElemento("10 punti", "10")
            _listaDimensioni.AggiungiElemento("11 punti", "11")
            _listaDimensioni.AggiungiElemento("12 punti", "12")
            _listaDimensioni.AggiungiElemento("14 punti", "14")
            _listaDimensioni.AggiungiElemento("16 punti", "16")
            _listaDimensioni.AggiungiElemento("18 punti", "18")
            _listaDimensioni.AggiungiElemento("20 punti", "20")
            _listaDimensioni.AggiungiElemento("24 punti", "24")
            _listaDimensioni.AggiungiElemento("28 punti", "28")
            _listaDimensioni.AggiungiElemento("32 punti", "32")
            _listaDimensioni.AggiungiElemento("36 punti", "36")
            _listaDimensioni.AggiungiElemento("48 punti", "48")
            _listaDimensioni.AggiungiElemento("72 punti", "72")
            AddHandler _listaDimensioni.elementoselezionato, AddressOf DimensioneCarattereSelezionata
            AddHandler _listaDimensioni.escpremuto, AddressOf ChiudiListaDimensioni
            _listaDimensioni.avvia()
        Catch ex As Exception
            log.ScriviLog("DimensioneCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub DimensioneCarattereSelezionata(id As String)
        Try
            Dim elementi As List(Of String) = _listaDimensioni.OttieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                Dim dimensione As Single
                If Single.TryParse(elementi(0), dimensione) AndAlso dimensione > 0 Then
                    If campo IsNot Nothing Then campo.ApplicaDimensione(dimensione)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("DimensioneCarattereSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaDimensioni()
        Try
            If _listaDimensioni IsNot Nothing Then _listaDimensioni.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiListaDimensioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ColoreCarattere()
        Try
            _listaColori = New WGMenuLista()
            _listaColori.multiselezioneconsentita = False
            _listaColori.AggiungiElemento("Nero", "0,0,0")
            _listaColori.AggiungiElemento("Bianco", "255,255,255")
            _listaColori.AggiungiElemento("Rosso", "255,0,0")
            _listaColori.AggiungiElemento("Verde", "0,128,0")
            _listaColori.AggiungiElemento("Blu", "0,0,255")
            _listaColori.AggiungiElemento("Giallo", "255,255,0")
            _listaColori.AggiungiElemento("Arancione", "255,165,0")
            _listaColori.AggiungiElemento("Viola", "128,0,128")
            _listaColori.AggiungiElemento("Grigio", "128,128,128")
            _listaColori.AggiungiElemento("Marrone", "165,42,42")
            _listaColori.AggiungiElemento("Rosa", "255,192,203")
            _listaColori.AggiungiElemento("Celeste", "173,216,230")
            AddHandler _listaColori.elementoselezionato, AddressOf ColoreCarattereSelezionato
            AddHandler _listaColori.escpremuto, AddressOf ChiudiListaColori
            _listaColori.avvia()
        Catch ex As Exception
            log.ScriviLog("ColoreCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub ColoreCarattereSelezionato(id As String)
        Try
            Dim elementi As List(Of String) = _listaColori.OttieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                Dim parti As String() = elementi(0).Split(","c)
                If parti.Length = 3 Then
                    Dim r As Integer, g As Integer, b As Integer
                    If Integer.TryParse(parti(0), r) AndAlso Integer.TryParse(parti(1), g) AndAlso Integer.TryParse(parti(2), b) Then
                        If campo IsNot Nothing Then campo.ApplicaColore(Color.FromArgb(r, g, b))
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("ColoreCarattereSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaColori()
        Try
            If _listaColori IsNot Nothing Then _listaColori.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiListaColori: " & ex.Message)
        End Try
    End Sub

    Private Sub numericocontrollo(sender As Object, e As KeyPressEventArgs)
        Try
            If campo Is Nothing Then Return
            If solonumeri AndAlso Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then
                e.Handled = True
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non sono consentite lettere o altri simboli, solo numeri")
                messaggio.ShowDialog()
            ElseIf campo.SolaLettura AndAlso Not Char.IsControl(e.KeyChar) Then
                e.Handled = True
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non è possibile scrivere in questo contesto poichè è un campo di sola lettura")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [numericocontrollo]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [numericocontrollo]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [numericocontrollo]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore" & ex.Message)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub InitializeComponent()
        Try
            If Me.DesignMode() Then Exit Sub
            Me.SuspendLayout()
            Me.ClientSize = New System.Drawing.Size(282, 253)
            Me.Name = "WGCampi"
            Me.ResumeLayout(False)
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [InitializeComponent]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [InitializeComponent]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [InitializeComponent]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub WGFormCampi_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Try
            If Me.DesignMode() Then Exit Sub


            If campo IsNot Nothing Then
                campo.Location = New Point(10, _titoloHeight + 10)
                campo.Width = Me.ClientSize.Width - 20
                campo.Height = Me.ClientSize.Height - _titoloHeight - 20
                Me.Controls.Add(campo)
                campo.Focus()
                MostraTastieraTouchSeAttiva()

                menuopzioni.Location = New Point(10, _titoloHeight + 10)
                menuopzioni.Width = campo.Width
                menuopzioni.Height = 200
                menuopzioni.Visible = False
                If lettura.NvdaInEsecuzione() Then
                Else
                    menuopzioni.Enabled = False
                End If

                AddHandler menuopzioni.SelectedIndexChanged, AddressOf menuopzioni_SelectedIndexChanged
                Me.Controls.Add(menuopzioni)


                If campo.SolaLettura Then
                    aggiungiopzione("Stampa", AddressOf StampaCampoTesto)
                    aggiungiopzione("Copia", AddressOf CopiaTesto)
                    aggiungiopzione("Seleziona tutto", AddressOf SelezionaTutto)
                Else
                    aggiungiopzione("Stampa", AddressOf StampaCampoTesto)
                    aggiungiopzione("Taglia", AddressOf TagliaTesto)
                    aggiungiopzione("Copia", AddressOf CopiaTesto)
                    aggiungiopzione("Incolla", AddressOf IncollaTesto)
                    aggiungiopzione("Seleziona tutto", AddressOf SelezionaTutto)
                    If multiriga Then
                        aggiungiopzione("Carattere", AddressOf ApriMenuCarattere)
                    End If
                    If WGDettatura.SistemaSupportato Then
                        aggiungiopzione("Dettatura", AddressOf AvviaDettaturaCampoTesto)
                    End If
                End If

                aggiungiopzione("Chiudi campo", AddressOf campochiudi)
            End If
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [WGFormCampi_Load]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [WGFormCampi_Load]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As COMException
            log.ScriviLog($"Errore [WGFormCampi_Load]: COMException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [WGFormCampi_Load]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Try
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

            If Not String.IsNullOrEmpty(titolo) Then
                Dim titoloRect As New Rectangle(0, 0, Me.Width, _titoloHeight)
                Dim sfumatoChiara As Color = If(WGTema.Scuro, Color.FromArgb(60, 60, 60), Color.FromArgb(225, 225, 225))
                Using lgb As New LinearGradientBrush(titoloRect, sfumatoChiara, WGTema.Sfondo, LinearGradientMode.Vertical)
                    g.FillRectangle(lgb, titoloRect)
                End Using

                Dim sfTitolo As New StringFormat()
                sfTitolo.Alignment = StringAlignment.Center
                sfTitolo.LineAlignment = StringAlignment.Center

                Using bTitolo As New SolidBrush(WGTema.Testo)
                    Using fontTitolo As New Font("Arial", 24, FontStyle.Regular)
                        g.DrawString(titolo, fontTitolo, bTitolo, titoloRect, sfTitolo)
                    End Using
                End Using
            End If
        Catch ex As Exception
            log.ScriviLog($"Errore in OnPaint. Dettaglio: {ex.Message}")
        End Try
    End Sub

    Private Sub campochiudi()
        Try
            If isClosing Then Return
            RaiseEvent chiusura(Me, Nothing)
            chiudi()
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As NullReferenceException
            log.ScriviLog($"Errore [campochiudi]: NullReferenceException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [campochiudi]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private _tastoDettaturaCampoTesto As WGTasto

    Private Sub MostraTastoDettaturaCampoTesto()
        Try
            If _tastoDettaturaCampoTesto Is Nothing Then
                _tastoDettaturaCampoTesto = New WGTasto()
                AddHandler _tastoDettaturaCampoTesto.Interrotto, AddressOf OnTastoDettaturaCampoTestoInterrotto
            End If
            _tastoDettaturaCampoTesto.Mostra("Sto ascoltando. Parla ora. Premi un tasto qualsiasi per terminare la dettatura.")
        Catch ex As Exception
            log.ScriviLog("MostraTastoDettaturaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastoDettaturaCampoTesto()
        Try
            If _tastoDettaturaCampoTesto IsNot Nothing Then
                _tastoDettaturaCampoTesto.Chiudi()
            End If
        Catch ex As Exception
            log.ScriviLog("NascondiTastoDettaturaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTastoDettaturaCampoTestoInterrotto(sender As Object, e As EventArgs)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New EventHandler(AddressOf OnTastoDettaturaCampoTestoInterrotto), sender, e)
                Return
            End If
            NascondiTastoDettaturaCampoTesto()
            FermaDettaturaCampoTesto()
        Catch ex As Exception
            log.ScriviLog("OnTastoDettaturaCampoTestoInterrotto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaDettaturaCampoTesto()
        Try
            If Not WGDettatura.SistemaSupportato Then Return
            If campo Is Nothing OrElse campo.SolaLettura Then Return

            If _dettaturaCampoTesto Is Nothing Then
                _dettaturaCampoTesto = New WGDettatura()
                AddHandler _dettaturaCampoTesto.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletataCampoTesto
                AddHandler _dettaturaCampoTesto.TrascrizioneFallita, AddressOf OnTrascrizioneFallitaCampoTesto
            End If

            If Not _dettaturaCampoTesto.ModelloDisponibile Then
                lettura.LeggiTesto("Chiave API di Gemini non configurata")
                Return
            End If

            _dettaturaCampoTestoInCorso = True
            MostraTastoDettaturaCampoTesto()
            _dettaturaCampoTesto.IniziaRegistrazione()
        Catch ex As Exception
            log.ScriviLog("AvviaDettaturaCampoTesto: " & ex.Message)
            _dettaturaCampoTestoInCorso = False
        End Try
    End Sub

    Private _processoDettaturaCampoTesto As WGProcessoInCorso

    Private Sub MostraProcessoDettaturaCampoTesto()
        Try
            If _processoDettaturaCampoTesto Is Nothing Then
                _processoDettaturaCampoTesto = New WGProcessoInCorso()
            End If
            _processoDettaturaCampoTesto.Avvia("Riconoscimento del parlato in corso, attendere", False)
        Catch ex As Exception
            log.ScriviLog("MostraProcessoDettaturaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiProcessoDettaturaCampoTesto()
        Try
            If _processoDettaturaCampoTesto IsNot Nothing Then
                _processoDettaturaCampoTesto.Fine()
            End If
        Catch ex As Exception
            log.ScriviLog("NascondiProcessoDettaturaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaDettaturaCampoTesto()
        Try
            _dettaturaCampoTestoInCorso = False
            NascondiTastoDettaturaCampoTesto()
            If _dettaturaCampoTesto IsNot Nothing Then
                MostraProcessoDettaturaCampoTesto()
                _dettaturaCampoTesto.FermaRegistrazione()
            End If
        Catch ex As Exception
            log.ScriviLog("FermaDettaturaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneCompletataCampoTesto(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneCompletataCampoTesto), testo)
                Return
            End If
            NascondiProcessoDettaturaCampoTesto()
            If campo IsNot Nothing AndAlso Not String.IsNullOrEmpty(testo) Then
                campo.IncollaTestoEsterno(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("OnTrascrizioneCompletataCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTrascrizioneFallitaCampoTesto(messaggioErrore As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneFallitaCampoTesto), messaggioErrore)
                Return
            End If
            NascondiProcessoDettaturaCampoTesto()
            lettura.LeggiTesto(messaggioErrore)
        Catch ex As Exception
            log.ScriviLog("OnTrascrizioneFallitaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvantiTesto() Handles _touk.FlickAvanti
        Try
            If campo Is Nothing OrElse Not campo.Focused Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=True)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickAvantiTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickIndietroTesto() Handles _touk.FlickIndietro
        Try
            If campo Is Nothing OrElse Not campo.Focused Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=False)
        Catch ex As Exception
            log.ScriviLog("_touk_FlickIndietroTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraTastieraTouchSeAttiva()
        Try
            If campo Is Nothing OrElse campo.SolaLettura Then Return
            If _touk Is Nothing OrElse Not _touk.TouchAttivo Then Return

            If _tastieraTouch Is Nothing Then
                _tastieraTouch = New WGTastieraTouch()
                AddHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                AddHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
            End If

            ' La tastiera occupa esattamente la meta' inferiore dello schermo: sopra resta
            ' visibile e utilizzabile il campo su cui si sta scrivendo.
            _tastieraTouch.Dock = DockStyle.Bottom
            _tastieraTouch.Height = AltezzaTastieraTouchConsigliata()

            If Not Me.Controls.Contains(_tastieraTouch) Then
                Me.Controls.Add(_tastieraTouch)
            End If
            _tastieraTouch.BringToFront()
            _tastieraTouch.Mostra()

            ' Il campo resta sopra la tastiera, nella meta' superiore.
            campo.Height = Math.Max(1, Me.ClientSize.Height - _titoloHeight - 20 - _tastieraTouch.Height)
        Catch ex As Exception
            log.ScriviLog("MostraTastieraTouchSeAttiva: " & ex.Message)
        End Try
    End Sub

    Private Function AltezzaTastieraTouchConsigliata() As Integer
        Try
            Dim quota As Double = 0.4
            If _touk IsNot Nothing Then
                quota = _touk.QuotaAltezzaTastiera(Me.ClientSize.Width, Me.ClientSize.Height, 0.4)
            End If
            Return Math.Max(1, CInt(Me.ClientSize.Height * quota))
        Catch ex As Exception
            log.ScriviLog("AltezzaTastieraTouchConsigliata: " & ex.Message)
            Return Math.Max(1, CInt(Me.ClientSize.Height * 0.4))
        End Try
    End Function

    Private Sub RidimensionamentoTastieraTouch(sender As Object, e As EventArgs) Handles Me.Resize
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then Return
            If Not _tastieraTouch.Visible OrElse Not Me.Controls.Contains(_tastieraTouch) Then Return
            If campo Is Nothing OrElse campo.IsDisposed Then Return

            _tastieraTouch.Height = AltezzaTastieraTouchConsigliata()
            campo.Height = Math.Max(1, Me.ClientSize.Height - _titoloHeight - 20 - _tastieraTouch.Height)
        Catch ex As Exception
            log.ScriviLog("RidimensionamentoTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastieraTouch()
        Try
            If _tastieraTouch IsNot Nothing Then _tastieraTouch.Nascondi()
            If campo IsNot Nothing Then
                campo.Height = Me.ClientSize.Height - _titoloHeight - 20
            End If
        Catch ex As Exception
            log.ScriviLog("NascondiTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnCarattereInseritoDaTastieraTouch(carattere As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnCarattereInseritoDaTastieraTouch), carattere)
                Return
            End If
            If campo IsNot Nothing AndAlso Not campo.SolaLettura AndAlso Not String.IsNullOrEmpty(carattere) Then
                campo.IncollaTestoEsterno(carattere)
            End If
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
            If campo IsNot Nothing AndAlso Not campo.SolaLettura Then
                campo.EliminaIndietroPubblica()
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
            If campo Is Nothing OrElse campo.SolaLettura Then Return
            If multiriga Then
                ' Campo multiriga: l'invio va a capo nel testo.
                campo.IncollaTestoEsterno(Environment.NewLine)
            Else
                ' Campo a riga singola: non c'e' un "a capo" possibile, quindi l'invio conferma
                ' e passa oltre, esattamente come il tasto Invio della tastiera fisica.
                Dim tastoInvio As Keys = Keys.Enter
                _tastiera_KeyDown(tastoInvio)
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
            If campo IsNot Nothing AndAlso Not campo.SolaLettura AndAlso Not String.IsNullOrEmpty(testo) Then
                campo.IncollaTestoEsterno(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("OnTestoDettatoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiMovimentoTouchSecondoModalita(avanti As Boolean)
        Try
            If _touk Is Nothing OrElse campo Is Nothing Then Return
            Select Case _touk.ModalitaNavigazione
                Case WGTouk.ModalitaNavigazioneTouch.Riga
                    If avanti Then
                        campo.SpostaCaretGiuPubblica()
                    Else
                        campo.SpostaCaretSuPubblica()
                    End If
                Case Else
                    ' Campo, Azioni: qui c'e' un solo campo, quindi non c'e' nulla tra cui
                    ' spostarsi o ciclare. Ricadiamo sul movimento per carattere, cosi' il
                    ' gesto fa comunque qualcosa di utile invece di restare senza effetto.
                    If avanti Then
                        campo.SpostaCaretDestraPubblica()
                    Else
                        campo.SpostaCaretSinistraPubblica()
                    End If
            End Select
        Catch ex As Exception
            log.ScriviLog("EseguiMovimentoTouchSecondoModalita: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_RichiestaLetturaCompleta() Handles _touk.RichiestaLetturaCompleta
        Try
            If campo Is Nothing OrElse Not campo.Focused Then Return
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(OttieniTesto())
            End If
        Catch ex As Exception
            log.ScriviLog("_touk_RichiestaLetturaCompleta: " & ex.Message)
        End Try
    End Sub

    Private Sub TagliaTesto()
        Try
            If campo Is Nothing Then Return
            If campo.SolaLettura Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il campo è di sola lettura, impossibile tagliare il testo")
                messaggio.ShowDialog()
            Else
                Dim testoSelezionato As String = campo.OttieniTestoSelezionato()
                If Not String.IsNullOrEmpty(testoSelezionato) Then
                    Clipboard.SetText(testoSelezionato)
                    campo.EliminaSelezionePubblica()
                    lettura.LeggiTesto("Tagliato")
                Else
                    Dim messaggio As New WGMessaggi
                    messaggio.Messaggio("Nessun testo selezionato")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [TagliaTesto]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [TagliaTesto]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore : " & ex.Message)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub CopiaTesto()
        Try
            If campo Is Nothing Then Return
            Dim testoSelezionato As String = campo.OttieniTestoSelezionato()
            If Not String.IsNullOrEmpty(testoSelezionato) Then
                Clipboard.SetText(testoSelezionato)
                lettura.LeggiTesto("Copiato negli appunti")
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun testo selezionato")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [CopiaTesto]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Runtime.InteropServices.ExternalException
            log.ScriviLog($"Errore [CopiaTesto]: ExternalException - Clipboard. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [CopiaTesto]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub IncollaTesto()
        Try
            If campo Is Nothing Then Return
            If campo.SolaLettura Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Impossibile incollare il testo, questo è un campo di sola lettura")
                messaggio.ShowDialog()
            Else
                If Clipboard.ContainsText Then
                    Dim testoDaIncollare As String = Clipboard.GetText()
                    If solonumeri AndAlso Not Me.ContieneSoloNumeri(testoDaIncollare) Then
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Questo campo accetta solo numeri.")
                        messaggio.ShowDialog()
                        Return
                    End If
                    If Not multiriga AndAlso Not TestoSingolaRiga(testoDaIncollare) Then
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Questo campo accetta solo una riga di testo.")
                        messaggio.ShowDialog()
                        Return
                    End If
                    campo.IncollaTestoEsterno(testoDaIncollare)
                    lettura.LeggiTesto("Incollato")
                End If
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [IncollaTesto]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As FormatException
            log.ScriviLog($"Errore [IncollaTesto]: FormatException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Runtime.InteropServices.ExternalException
            log.ScriviLog($"Errore [IncollaTesto]: ExternalException - Clipboard. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [IncollaTesto]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore: " & ex.Message & ex.StackTrace)
            messaggio.ShowDialog()
            chiudi()
        End Try
    End Sub

    Private Sub SelezionaTutto()
        Try
            If campo Is Nothing Then Return
            If campo.TestoPiatto().Length > 0 Then
                campo.SelezionaTuttoPubblica()
                lettura.LeggiTesto("Selezionato tutto")
            Else
                Dim messaggio As New WGMessaggi
                messaggio.Messaggio("Non è presente testo da poter selezionare")
                messaggio.ShowDialog()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"Errore [SelezionaTutto]: InvalidOperationException. Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"Errore critico [SelezionaTutto]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Function TestoSingolaRiga(testo As String) As Boolean
        Try
            For Each c As Char In testo
                If c = vbCr OrElse c = vbLf Then
                    Return False
                End If
            Next
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"Errore [TestoSingolaRiga]: ArgumentNullException. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"Errore critico [TestoSingolaRiga]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function ContieneSoloNumeri(testo As String) As Boolean
        Try
            Return testo.All(AddressOf Char.IsDigit)
        Catch ex As ArgumentNullException
            log.ScriviLog($"Errore [ContieneSoloNumeri]: ArgumentNullException. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"Errore critico [ContieneSoloNumeri]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

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
            log.ScriviLog($"Errore [stringavalida]: ArgumentNullException. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As RegexMatchTimeoutException
            log.ScriviLog($"Errore [stringavalida]: RegexMatchTimeoutException. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"Errore critico [stringavalida]: Errore generico. Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Sub gestisciVisibilita(sender As Object, e As EventArgs) Handles Me.VisibleChanged
        Try
            If Me.Visible Then
                attaccatastiera()
                Me.Focus()
                If campo IsNot Nothing Then campo.Focus()
            Else
                rilasciatastiera()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("ObjectDisposed: " & ex.Message)
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"GestisciVisibilita - Errore InvalidOperation: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"GestisciVisibilita - Errore NullReference: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"GestisciVisibilita - Errore generico: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        End Try
    End Sub

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(ByVal hWnd As IntPtr, ByVal Msg As Integer, ByVal wParam As Integer, ByVal lParam As Integer) As IntPtr
    End Function

    Private Sub ApriLinkNelBrowser(url As String)
        Try
            WGImpostazioniComuni.ApriLinkConBrowserPreferito(url)
        Catch ex As Exception
            log.ScriviLog($"Errore [ApriLinkNelBrowser]: Impossibile aprire le link. Dettaglio: {ex.Message}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Impossibile aprire il link nel browser: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

End Class