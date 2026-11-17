Imports System.Drawing
Imports System.Windows.Forms
Imports wgcontrols

' Gestione dei messaggi a schermo associati a tasti specifici, con supporto per interazione tattile e da tastiera.
Public Class WGTasto
    Inherits UserControl

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private _logger As New Logger()
    Private _sintesiVocale As sintesivocale
    Private _messaggioCorrente As String = ""
    Private _mostrato As Boolean = False

    Public Event Interrotto(sender As Object, e As EventArgs)

    ''' <summary>
    ''' Da dove e' stato aperto il pannello. Cambia l'istruzione mostrata e pronunciata: con la
    ''' tastiera a schermo non esiste una tastiera fisica da premere, quindi va indicato il gesto.
    ''' </summary>
    Public Enum OrigineTasto
        Tastiera
        TastieraTouch
    End Enum

    Private Const IstruzioneTastieraTouch As String = "Tocca due volte con due dita per interrompere."

    Private _origineCorrente As OrigineTasto = OrigineTasto.Tastiera
    Private _controlloTouchRiassegnato As Boolean = False

    Public Sub New()
        Try
            Me.DoubleBuffered = True
            Me.SetStyle(ControlStyles.ResizeRedraw, True)
            Me.BackColor = Color.Black
            Me.Dock = DockStyle.Fill
            _sintesiVocale = sintesivocale.GetInstance()
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.New: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Mostrato As Boolean
        Get
            Try
                Return _mostrato
            Catch ex As Exception
                _logger.ScriviLog("WGTasto.Mostrato Get: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Sub Mostra(messaggio As String)
        Try
            Mostra(messaggio, OrigineTasto.Tastiera)
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.Mostra: " & ex.Message)
        End Try
    End Sub

    Public Sub Mostra(messaggio As String, origine As OrigineTasto)
        Try
            _origineCorrente = origine
            _messaggioCorrente = ComponiMessaggio(messaggio, origine)

            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return

            contenitore.MostraControllo(Me)
            _mostrato = True
            Me.Invalidate()

            AssumiControlloTouch()

            If _sintesiVocale IsNot Nothing AndAlso Not String.IsNullOrEmpty(_messaggioCorrente) Then
                _sintesiVocale.LeggiTesto(_messaggioCorrente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.Mostra (origine): " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Sostituisce l'istruzione finale con quella adatta all'origine. Il testo ricevuto dal
    ''' chiamante viene ripulito della parte che invita a premere un tasto, che con la tastiera a
    ''' schermo non ha senso.
    ''' </summary>
    Private Function ComponiMessaggio(messaggio As String, origine As OrigineTasto) As String
        Try
            Dim testo As String = If(messaggio, "").Trim()
            If origine <> OrigineTasto.TastieraTouch Then Return testo

            Dim posizione As Integer = testo.IndexOf("Premi un tasto", StringComparison.OrdinalIgnoreCase)
            If posizione >= 0 Then testo = testo.Substring(0, posizione).Trim()
            If testo.Length = 0 Then Return IstruzioneTastieraTouch
            Return testo & " " & IstruzioneTastieraTouch
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.ComponiMessaggio: " & ex.Message)
            Return If(messaggio, "")
        End Try
    End Function

    ''' <summary>
    ''' Con l'apertura dalla tastiera a schermo il controllo dei gesti appartiene alla tastiera,
    ''' quindi i tocchi non arriverebbero a questo pannello. Lo si riassegna all'apertura e lo si
    ''' restituisce alla chiusura.
    ''' </summary>
    Private Sub AssumiControlloTouch()
        Try
            If _origineCorrente <> OrigineTasto.TastieraTouch Then Return
            If _touk Is Nothing Then Return
            If Not Me.IsHandleCreated Then Return
            _touk.RiassegnaControllo(Me.Handle)
            _controlloTouchRiassegnato = True
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.AssumiControlloTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub RestituisciControlloTouch()
        Try
            If Not _controlloTouchRiassegnato Then Return
            _controlloTouchRiassegnato = False
            WGTastieraTouch.RiprendiControlloTouch()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.RestituisciControlloTouch: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaMessaggio(messaggio As String)
        Try
            _messaggioCorrente = ComponiMessaggio(messaggio, _origineCorrente)
            Me.Invalidate()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.AggiornaMessaggio: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            If Not _mostrato Then Return
            _mostrato = False
            RestituisciControlloTouch()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                contenitore.RimuoviControllo(Me)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.Chiudi: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            MyBase.OnPaint(e)
            Dim g As Graphics = e.Graphics
            g.Clear(Me.BackColor)

            Dim testo As String = _messaggioCorrente
            If String.IsNullOrEmpty(testo) Then Return

            Using formato As New StringFormat With {
                .Alignment = StringAlignment.Center,
                .LineAlignment = StringAlignment.Center,
                .Trimming = StringTrimming.Word
            }
                Using carattere As New Font(Me.Font.FontFamily, 22, FontStyle.Bold)
                    Dim areaTesto As New Rectangle(40, 40, Me.ClientSize.Width - 80, Me.ClientSize.Height - 80)
                    g.DrawString(testo, carattere, Brushes.White, areaTesto, formato)
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.OnPaint: " & ex.Message)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If Not _mostrato Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False
            Return contenitore.PuoRicevereInput(Me)
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.PuoRicevere: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If Not PuoRicevere() Then Return
            If tasto = Keys.None Then Return
            tasto = Keys.None
            SegnalaInterruzione()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto._tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        Try
            If Not PuoRicevere() Then Return
            SegnalaInterruzione()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto._touk_TapDoppio: " & ex.Message)
        End Try
    End Sub


    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        Try
            If Not PuoRicevere() Then Return
            SegnalaInterruzione()
        Catch ex As Exception
            _logger.ScriviLog("WGTasto._touk_EscapeGesto: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnalaInterruzione()
        Try
            RaiseEvent Interrotto(Me, EventArgs.Empty)
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.SegnalaInterruzione: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If _mostrato Then Chiudi()
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    _tastiera = Nothing
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    _touk = Nothing
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGTasto.Dispose: " & ex.Message)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

End Class