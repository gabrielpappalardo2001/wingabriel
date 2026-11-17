' ============================================================================================
' WGMidiRiproduzione - il controllo di chi suona: uno spartito, un file MIDI, uno strumento
' ============================================================================================
'
' Non è un menu. Come il riproduttore (WGPlayerInt) è un controllo a schermo intero con il titolo di quello che suona; Invio lo
' avvia e lo ferma, Esc lo chiude e Alt apre il menu delle opzioni, che sta sopra il controllo finché non si sceglie o si preme Esc.
' Mentre il menu delle opzioni è aperto il controllo non riceve altro: i tasti che suonano non suonano.
'
' Ha gli stessi membri che servivano a chi usava prima una WGMenuLista (titolo, opzioni, avvia, abbandona, Invio, Esc), così
' SessioneSpartito e SessioneStrumento lo usano senza cambiare altro.
'
' PuoRicevereInput si chiama dal gancio della tastiera, subito prima di far suonare una nota: deve essere istantaneo e non toccare
' l'interfaccia. Per questo non controlla la finestra in primo piano a ogni tasto: lo fa un timer ogni quaranta millisecondi, sul
' thread dell'interfaccia, e la risposta si legge da un valore già pronto.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms

Public Class WGMidiRiproduzione
    Inherits UserControl

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    Private NotInheritable Class VoceOpzione
        Public Nome As String = ""
        Public Evento As EventHandler
    End Class

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _elencoOpzioni As WGListaView
    Private ReadOnly _voci As New List(Of VoceOpzione)()
    Private ReadOnly _etichetta As New Label()
    Private ReadOnly _log As New Logger()
    Private _titolo As String = ""
    Private _testo As String = ""
    Private _idElemento As String = ""
    Private _opzioniAperte As Integer
    Private _chiusa As Integer
    Private _puoInput As Integer
    Private _timer As System.Windows.Forms.Timer

    ''' <summary>Invio: si avvia o si ferma quello che suona. Porta l'identificativo dato ad aggiungiElemento.</summary>
    Public Event ElementoSelezionato(id As String)
    Public Event escPremuto As EventHandler
    ''' <summary>Spazio premuto, a menu delle opzioni chiuso: chi ospita il controllo decide che farne (la fotocamera guarda l'ultimo video).</summary>
    Public Event SpazioPremuto As EventHandler
    ''' <summary>Il menu delle opzioni si è chiuso. daSelezione è vero se si è scelta una voce (e questa parte subito), falso se si è usciti con Esc.</summary>
    Public Event OpzioniChiuse(daSelezione As Boolean)
    ''' <summary>AltGr (Alt destro) premuto da solo, come in Word: chi ospita il controllo apre il suo menu delle modifiche.</summary>
    Public Event AltGrPremuto As EventHandler

    ''' <summary>Compatibilità con la lista: non fa niente.</summary>
    Public Property multiselezioneConsentita As Boolean

    Public Sub New()
        Try
            Me.Dock = DockStyle.Fill
            Me.BackColor = Color.Black
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint, True)
            _etichetta.Dock = DockStyle.Fill
            _etichetta.ForeColor = Color.White
            _etichetta.BackColor = Color.Black
            _etichetta.Font = New Font("Segoe UI", 36.0F, FontStyle.Bold)
            _etichetta.TextAlign = ContentAlignment.MiddleCenter
            _etichetta.AutoEllipsis = True
            Me.Controls.Add(_etichetta)
            _tastiera = WGTastiera.GetInstance()
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.New: " & ex.Message)
        End Try
    End Sub

    ' ---- quello che usa chi lo ospita ------------------------------------------------------------------------

    Public Function impostaTitolo(titolo As String) As Boolean
        _titolo = If(titolo, "")
        Try
            Me.AccessibleName = _titolo
        Catch
        End Try
        Return True
    End Function

    ''' <summary>Il testo del controllo, quello che si vede a schermo: il titolo del brano o il nome dello strumento.</summary>
    Public Sub aggiungiElemento(elemento As String, Optional id As String = "", Optional selezionato As Boolean = False)
        _testo = If(elemento, "")
        _idElemento = If(id, "")
        AggiornaEtichetta()
    End Sub

    Public Sub pulisci(Optional isSelectionToggle As Boolean = False)
        _testo = ""
        AggiornaEtichetta()
    End Sub

    Public Sub posiziona(id As String)
    End Sub

    ''' <summary>Chi usa il controllo ridefinisce qui le opzioni, ogni volta che si preme Alt: così compaiono solo quelle che servono in quel momento.</summary>
    Public Property AggiornaOpzioni As Action

    ''' <summary>Se è impostata, Alt sinistro premuto da solo esegue questa azione invece di aprire le opzioni.</summary>
    Public Property AltSinistroDiretto As Action

    Public Sub pulisciOpzioni()
        _voci.Clear()
    End Sub

    Public Sub aggiungiOpzione(testoVoce As String, evento As EventHandler)
        If String.IsNullOrWhiteSpace(testoVoce) Then Return
        If _voci.Any(Function(v) v.Nome = testoVoce) Then Return
        Dim nuova As New VoceOpzione()
        nuova.Nome = testoVoce
        nuova.Evento = evento
        _voci.Add(nuova)
    End Sub

    Public ReadOnly Property OpzioniAperte As Boolean
        Get
            Return Volatile.Read(_opzioniAperte) = 1
        End Get
    End Property

    Public ReadOnly Property Chiusa As Boolean
        Get
            Return Volatile.Read(_chiusa) = 1 OrElse Me.IsDisposed
        End Get
    End Property

    ''' <summary>Vero se il controllo è quello che riceve la tastiera adesso. Istantaneo: legge un valore che un timer tiene aggiornato.</summary>
    Public Function PuoRicevereInput() As Boolean
        Return Volatile.Read(_puoInput) = 1 AndAlso Volatile.Read(_chiusa) = 0
    End Function

    ' ---- l'anteprima a tutto schermo ----------------------------------------------------------------------------
    ' Chi ospita il controllo (per esempio la fotocamera) gli passa i fotogrammi, uno dopo l'altro: il controllo li disegna a tutto
    ' schermo, centrati e senza deformarli (così un video in verticale resta in verticale), e il testo della riga passa in una
    ' striscia in basso. I tasti non c'entrano: arrivano dalla tastiera di WinGabriel come sempre, e la lettura vocale non cambia.

    Private _anteprima As Bitmap
    Private ReadOnly _bloccoAnteprima As New Object()
    Private _invalidazionePendente As Integer
    Private _anteprimaAttiva As Boolean

    ''' <summary>Vero se il controllo sta mostrando un'anteprima (il testo sta nella striscia in basso).</summary>
    Public Property AnteprimaAttiva As Boolean
        Get
            Return _anteprimaAttiva
        End Get
        Set(valore As Boolean)
            If valore = _anteprimaAttiva Then Return
            _anteprimaAttiva = valore
            Try
                If Me.IsDisposed Then Return
                If Me.InvokeRequired Then
                    Me.BeginInvoke(New Action(Sub() AdattaEtichetta()))
                Else
                    AdattaEtichetta()
                End If
            Catch ex As Exception
                _log.ScriviLog("WGMidiRiproduzione.AnteprimaAttiva: " & ex.Message)
            End Try
        End Set
    End Property

    Private Sub AdattaEtichetta()
        If _anteprimaAttiva Then
            _etichetta.Dock = DockStyle.Bottom
            _etichetta.Height = 96
            _etichetta.Font = New Font("Segoe UI", 24.0F, FontStyle.Bold)
        Else
            _etichetta.Dock = DockStyle.Fill
            _etichetta.Font = New Font("Segoe UI", 36.0F, FontStyle.Bold)
            SyncLock _bloccoAnteprima
                If _anteprima IsNot Nothing Then
                    _anteprima.Dispose()
                    _anteprima = Nothing
                End If
            End SyncLock
        End If
        Me.Invalidate()
    End Sub

    ''' <summary>Mostra un fotogramma. Il controllo ne diventa il proprietario e lo elimina quando arriva il successivo.</summary>
    Public Sub ImpostaAnteprima(fotogramma As Bitmap)
        Try
            If Me.IsDisposed OrElse Not _anteprimaAttiva Then
                If fotogramma IsNot Nothing Then fotogramma.Dispose()
                Return
            End If
            SyncLock _bloccoAnteprima
                If _anteprima IsNot Nothing Then _anteprima.Dispose()
                _anteprima = fotogramma
            End SyncLock
            If Me.IsHandleCreated AndAlso Interlocked.Exchange(_invalidazionePendente, 1) = 0 Then
                Me.BeginInvoke(New Action(Sub()
                                              Interlocked.Exchange(_invalidazionePendente, 0)
                                              If Not Me.IsDisposed Then Me.Invalidate()
                                          End Sub))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.ImpostaAnteprima: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Una copia dell'ultimo fotogramma mostrato (Nothing se non ce n'è); chi la riceve la elimina.</summary>
    Public Function CopiaAnteprima() As Bitmap
        Try
            SyncLock _bloccoAnteprima
                If _anteprima Is Nothing Then Return Nothing
                Return New Bitmap(_anteprima)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.CopiaAnteprima: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            e.Graphics.Clear(Color.Black)
            If Not _anteprimaAttiva Then Return
            SyncLock _bloccoAnteprima
                If _anteprima Is Nothing Then Return
                Dim area As Rectangle = Me.ClientRectangle
                area.Height = Math.Max(1, area.Height - _etichetta.Height)
                Dim rapporto As Double = Math.Min(area.Width / CDbl(_anteprima.Width), area.Height / CDbl(_anteprima.Height))
                Dim larghezza As Integer = CInt(_anteprima.Width * rapporto)
                Dim altezza As Integer = CInt(_anteprima.Height * rapporto)
                ' la qualità prima della velocità: il fotogramma è già della misura giusta, quindi il ridimensionamento è lieve
                e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
                e.Graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
                e.Graphics.DrawImage(_anteprima, New Rectangle(area.X + (area.Width - larghezza) \ 2, area.Y + (area.Height - altezza) \ 2, larghezza, altezza))
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.OnPaint: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaEtichetta()
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf AggiornaEtichetta))
                Return
            End If
            _etichetta.Text = _testo
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.AggiornaEtichetta: " & ex.Message)
        End Try
    End Sub

    ' ---- mostrare e togliere ------------------------------------------------------------------------------------

    Public Sub avvia(Optional dialogo As Boolean = False)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return
            _etichetta.Text = _testo
            contenitore.MostraControllo(Me)
            _timer = New System.Windows.Forms.Timer()
            _timer.Interval = 40
            AddHandler _timer.Tick, AddressOf OnTimer
            _timer.Start()
            AggiornaPuoInput()
            AiutoMusica.AnnunciaInSottofondo(If(_testo <> "", _testo, _titolo))
            If dialogo Then
                While Not Me.IsDisposed AndAlso Me.Visible AndAlso Not contenitore.IsDisposed
                    Application.DoEvents()
                    Thread.Sleep(15)
                End While
            End If
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub abbandona()
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf abbandona))
                Return
            End If
            Interlocked.Exchange(_chiusa, 1)
            Interlocked.Exchange(_puoInput, 0)
            If _timer IsNot Nothing Then
                _timer.Stop()
                RemoveHandler _timer.Tick, AddressOf OnTimer
                _timer.Dispose()
                _timer = Nothing
            End If
            ChiudiOpzioni(False)
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.RimuoviControllo(Me)
            Catch ex As Exception
                _log.ScriviLog("WGMidiRiproduzione.abbandona (contenitore): " & ex.Message)
            End Try
            Me.Visible = False
            Me.Dispose()
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.abbandona: " & ex.Message)
        End Try
    End Sub

    ' ---- chi riceve la tastiera -------------------------------------------------------------------------------

    ''' <summary>Il controllo è visibile e in primo piano, e sopra non ci sono attese né finestre. Non si chiama dal gancio della tastiera.</summary>
    Private Function PuoElaborareInput() As Boolean
        Try
            If Volatile.Read(_chiusa) = 1 OrElse Me.IsDisposed OrElse Me.Disposing Then Return False
            If Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                If GetForegroundWindow() = contenitore.Handle Then
                    Dim schermata As Control = Me
                    While schermata.Parent IsNot Nothing AndAlso Not (TypeOf schermata.Parent Is WGContenitore)
                        schermata = schermata.Parent
                    End While
                    Return contenitore.PuoRicevereInput(schermata)
                End If
            End If
            Return GetForegroundWindow() = Me.Handle
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub AggiornaPuoInput()
        Interlocked.Exchange(_puoInput, If(PuoElaborareInput(), 1, 0))
    End Sub

    Private Sub OnTimer(sender As Object, e As EventArgs)
        AggiornaPuoInput()
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse Volatile.Read(_chiusa) = 1 Then Return
            If Not PuoElaborareInput() Then Return
            If Volatile.Read(_opzioniAperte) = 1 Then
                Select Case tasto
                    Case Keys.Enter, Keys.Return
                        Me.BeginInvoke(New Action(AddressOf SelezionaOpzione))
                    Case Keys.Escape
                        Me.BeginInvoke(New Action(Sub() ChiudiOpzioni(True)))
                End Select
                Return
            End If
            Select Case tasto
                Case Keys.Enter, Keys.Return
                    Dim id As String = _idElemento
                    Me.BeginInvoke(New Action(Sub() RaiseEvent ElementoSelezionato(id)))
                Case Keys.Escape
                    Me.BeginInvoke(New Action(Sub() RaiseEvent escPremuto(Me, EventArgs.Empty)))
                Case Keys.Space
                    Me.BeginInvoke(New Action(Sub() RaiseEvent SpazioPremuto(Me, EventArgs.Empty)))
            End Select
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Volatile.Read(_chiusa) = 1 OrElse Volatile.Read(_opzioniAperte) = 1 Then Return
            If Not PuoElaborareInput() Then Return
            Dim diretto As Action = AltSinistroDiretto
            If diretto IsNot Nothing Then
                Me.BeginInvoke(diretto)
                Return
            End If
            Me.BeginInvoke(New Action(AddressOf ApriOpzioni))
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltDestroPremuto
        Try
            If Volatile.Read(_chiusa) = 1 OrElse Volatile.Read(_opzioniAperte) = 1 Then Return
            If Not PuoElaborareInput() Then Return
            Me.BeginInvoke(New Action(Sub() RaiseEvent AltGrPremuto(Me, EventArgs.Empty)))
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.AltDestroPremuto: " & ex.Message)
        End Try
    End Sub

    ' ---- il menu delle opzioni ---------------------------------------------------------------------------------

    Private Sub ApriOpzioni()
        Try
            If Me.IsDisposed OrElse Volatile.Read(_opzioniAperte) = 1 Then Return
            If AggiornaOpzioni IsNot Nothing Then AggiornaOpzioni.Invoke()
            If _voci.Count = 0 Then Return
            Interlocked.Exchange(_opzioniAperte, 1)
            _elencoOpzioni = New WGListaView()
            _elencoOpzioni.Titolo = "Opzioni"
            _elencoOpzioni.SelectionMode = SelectionMode.One
            _elencoOpzioni.Dock = DockStyle.Fill
            _elencoOpzioni.Enabled = True
            For Each v As VoceOpzione In _voci
                _elencoOpzioni.Items.Add(v.Nome)
            Next
            Me.Controls.Add(_elencoOpzioni)
            _elencoOpzioni.BringToFront()
            _elencoOpzioni.Visible = True
            _elencoOpzioni.Focus()
            If _elencoOpzioni.Items.Count > 0 Then _elencoOpzioni.SelectedIndex = 0
        Catch ex As Exception
            Interlocked.Exchange(_opzioniAperte, 0)
            _log.ScriviLog("WGMidiRiproduzione.ApriOpzioni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SelezionaOpzione()
        Try
            If _elencoOpzioni Is Nothing OrElse _elencoOpzioni.SelectedItem Is Nothing Then Return
            Dim nome As String = _elencoOpzioni.SelectedItem.ToString()
            Dim voce As VoceOpzione = _voci.FirstOrDefault(Function(v) v.Nome = nome)
            ChiudiOpzioni(True, True)
            If voce IsNot Nothing AndAlso voce.Evento IsNot Nothing Then voce.Evento.Invoke(Me, EventArgs.Empty)
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.SelezionaOpzione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiOpzioni(riportaIlFuoco As Boolean, Optional daSelezione As Boolean = False)
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New Action(Sub() ChiudiOpzioni(riportaIlFuoco, daSelezione)))
                Return
            End If
            Interlocked.Exchange(_opzioniAperte, 0)
            Dim eraAperto As Boolean = _elencoOpzioni IsNot Nothing
            If _elencoOpzioni IsNot Nothing Then
                Me.Controls.Remove(_elencoOpzioni)
                _elencoOpzioni.Dispose()
                _elencoOpzioni = Nothing
            End If
            If riportaIlFuoco Then Me.Focus()
            If eraAperto AndAlso riportaIlFuoco Then RaiseEvent OpzioniChiuse(daSelezione)
        Catch ex As Exception
            _log.ScriviLog("WGMidiRiproduzione.ChiudiOpzioni: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            Interlocked.Exchange(_chiusa, 1)
            Interlocked.Exchange(_puoInput, 0)
            ' la tastiera è unica per tutto il programma: se il controllo non si scollega, a ogni sessione ne resta uno in più
            ' agganciato a ogni tasto premuto
            If disposing Then
                _tastiera = Nothing
                SyncLock _bloccoAnteprima
                    If _anteprima IsNot Nothing Then _anteprima.Dispose()
                    _anteprima = Nothing
                End SyncLock
            End If
        Catch
        End Try
        MyBase.Dispose(disposing)
    End Sub
End Class
