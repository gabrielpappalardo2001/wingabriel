Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Diagnostics
Imports wgcontrols

' Gestione delle schermate di attesa per operazioni lunghe, con feedback vocale, sovrimpressione grafica e tasti di annullamento.
Public Class WGProcessoInCorso
    Inherits UserControl

    Private ReadOnly _lock As New Object()
    Private ReadOnly _cicloAttesaLock As New Object()
    Private _soundCts As CancellationTokenSource
    Private _cicloAttesaCts As CancellationTokenSource
    Private _handlerFineLetturaAgganciato As Boolean = False
    Private _isDisposed As Boolean = False
    Private _haIncrementatoOverlay As Boolean = False
    Private _mostrato As Boolean = False

    Private messaggio As String
    Private testo As String
    Private percentuale As String = ""
    Private _ultimaPercentuale As Integer = -1
    Private annullabile As Boolean

    Public ReadOnly Property TestoAttesa As String
        Get
            Try
                Return (percentuale & " " & testo).Trim()
            Catch ex As Exception
                WriteLog.ScriviLog("TestoAttesa: " & ex.Message)
                Return ""
            End Try
        End Get
    End Property

    Private Shared _overlayAttiviCount As Integer = 0

    Private WriteLog As New Logger()
    Private lettura As sintesivocale
    Private suonoAttesa As WGSuoni
    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk

    Public Event cancella As EventHandler

    ''' <summary>
    ''' Mostra l'attesa con il testo indicato per tutta la durata del lavoro e la chiude sempre alla fine,
    ''' anche in caso di errore. Da usare in ogni punto del programma in cui l'utente aspetta.
    ''' </summary>
    ' Un lavoro che finisce entro questo tempo non fa partire l'attesa: il suo annuncio verrebbe troncato
    ' dopo una sillaba da quello che compare subito dopo (per esempio "Let" prima del cestino).
    Private Const MillisecondiPrimaDellAttesa As Integer = 350

    ' Nel tempo in cui si aspetta di vedere se il lavoro finisce subito il contenitore non deve rimettere
    ' davanti la finestra di sotto (chiuso un menu, la scrivania tornerebbe per un attimo, prendendo il
    ' fuoco e parlando sopra quello che sta per comparire): si comporta come se l'attesa ci fosse già.
    Private Shared Async Function FinisceSubitoAsync(attivita As Task) As Task(Of Boolean)
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If contenitore IsNot Nothing Then contenitore.RimandaRivelazione(True)
        Try
            Return Await Task.WhenAny(attivita, Task.Delay(MillisecondiPrimaDellAttesa)) Is attivita
        Finally
            If contenitore IsNot Nothing Then contenitore.RimandaRivelazione(False)
        End Try
    End Function

    Public Shared Async Function EseguiAsync(Of T)(testoAttesa As String, lavoro As Func(Of Task(Of T))) As Task(Of T)
        Dim attivita As Task(Of T) = lavoro()
        If Await FinisceSubitoAsync(attivita) Then Return Await attivita
        Dim processo As New WGProcessoInCorso()
        processo.Avvia(testoAttesa, False)
        Try
            Return Await attivita
        Finally
            processo.Fine()
        End Try
    End Function

    Public Shared Async Function EseguiAsync(testoAttesa As String, lavoro As Func(Of Task)) As Task
        Dim attivita As Task = lavoro()
        If Await FinisceSubitoAsync(attivita) Then
            Await attivita
            Return
        End If
        Dim processo As New WGProcessoInCorso()
        processo.Avvia(testoAttesa, False)
        Try
            Await attivita
        Finally
            processo.Fine()
        End Try
    End Function

    Private Const DT_CENTER As Integer = &H1
    Private Const DT_VCENTER As Integer = &H4
    Private Const DT_WORDBREAK As Integer = &H10

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function DrawText(hDC As IntPtr, lpString As String, nCount As Integer, ByRef lpRect As RECT, uFormat As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hdc As IntPtr, hgdiobj As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
    End Function

    Public Shared ReadOnly Property OverlayAttivo As Boolean
        Get
            Try
                Return Interlocked.CompareExchange(_overlayAttiviCount, 0, 0) > 0
            Catch ex As Exception
                Return False
            End Try
        End Get
    End Property

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Me.BackColor = Color.Black
            Me.ForeColor = Color.White
            Me.DoubleBuffered = True
            Me.TabStop = False
            Me.Dock = DockStyle.Fill
            Me.AccessibleRole = AccessibleRole.Client
            Me.AccessibleName = " "

            lettura = sintesivocale.GetInstance()
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
        Catch ex As Exception
            WriteLog.ScriviLog("WGProcessoInCorso.New: " & ex.Message)
        End Try
    End Sub

    Private Sub DecrementaContatoreOverlay()
        Try
            If _haIncrementatoOverlay Then
                Interlocked.Decrement(_overlayAttiviCount)
                _haIncrementatoOverlay = False
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("DecrementaContatoreOverlay: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None Then Return
            If programmadisattivato Then Return
            If _isDisposed Then Return
            If Not _mostrato Then Return
            If InSecondoPiano() Then Return

            Select Case tasto
                Case Keys.Escape
                    GestisciAnnullaOLeggi()
                    tasto = Keys.None
                Case Keys.Up, Keys.Down
                    InterrompiCicloAttesa()
                    If lettura IsNot Nothing Then
                        lettura.LeggiTesto(percentuale & " " & testo, escludiPunteggiatura:=True)
                    End If
                    tasto = Keys.None
            End Select
        Catch ex As Exception
            WriteLog.ScriviLog("_tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Function InSecondoPiano() As Boolean
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False
            Return contenitore.AttesaInSecondoPiano()
        Catch ex As Exception
            WriteLog.ScriviLog("InSecondoPiano: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub GestisciAnnullaOLeggi()
        Try
            If InSecondoPiano() Then Return
            If annullabile Then
                RaiseEvent cancella(Me, EventArgs.Empty)
                Fine()
            Else
                InterrompiCicloAttesa()
                If lettura IsNot Nothing Then
                    lettura.LeggiTesto(percentuale & " " & testo, escludiPunteggiatura:=True)
                End If
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("GestisciAnnullaOLeggi: " & ex.Message)
        End Try
    End Sub

    Private Function PuoRicevereTouch() As Boolean
        Try
            If _isDisposed OrElse Not _mostrato Then Return False
            If programmadisattivato Then Return False
            If InSecondoPiano() Then Return False
            Return True
        Catch ex As Exception
            WriteLog.ScriviLog("PuoRicevereTouch: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        Try
            If Not PuoRicevereTouch() Then Return
            GestisciAnnullaOLeggi()
        Catch ex As Exception
            WriteLog.ScriviLog("_touk_EscapeGesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickSu() Handles _touk.FlickSu
        Try
            If Not PuoRicevereTouch() Then Return
            InterrompiCicloAttesa()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(percentuale & " " & testo, escludiPunteggiatura:=True)
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("_touk_FlickSu: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickGiu() Handles _touk.FlickGiu
        Try
            If Not PuoRicevereTouch() Then Return
            InterrompiCicloAttesa()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(percentuale & " " & testo, escludiPunteggiatura:=True)
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("_touk_FlickGiu: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Try
            Dim hdc As IntPtr = e.Graphics.GetHdc()
            Try
                Dim rect As New RECT With {
                    .Left = 10,
                    .Top = 10,
                    .Right = Me.ClientSize.Width - 10,
                    .Bottom = Me.ClientSize.Height - 10
                }

                Using font As New Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Point)
                    Dim hFont As IntPtr = font.ToHfont()
                    Dim oldFont As IntPtr = SelectObject(hdc, hFont)

                    SetTextColor(hdc, ColorTranslator.ToWin32(Color.White))
                    SetBkMode(hdc, 1)

                    Dim testoCompleto As String = percentuale & " " & testo

                    DrawText(hdc, testoCompleto, -1, rect, DT_CENTER Or DT_VCENTER Or DT_WORDBREAK)

                    SelectObject(hdc, oldFont)
                    DeleteObject(hFont)
                End Using
            Finally
                e.Graphics.ReleaseHdc(hdc)
            End Try
        Catch ex As Exception
            WriteLog.ScriviLog("Errore disegno: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Vero se l'attesa è di un'applicazione riaperta in sottofondo alla ripresa della sessione: allora non
    ''' compare, non parla, non suona e non blocca chi è davanti; finisce da sola quando il lavoro è fatto.
    ''' </summary>
    Friend Property InSottofondo As Boolean

    Public Sub Avvia(msg As String, Optional annulla As Boolean = False)
        Try
            Dim contenitoreAttuale As WGContenitore = WGContenitore.Istanza
            InSottofondo = contenitoreAttuale IsNot Nothing AndAlso contenitoreAttuale.AttesaInSottofondo()
            If InSottofondo Then
                messaggio = msg
                testo = msg
                ' Registrata nel contenitore solo per coerenza con Fine: non compare e non parla.
                contenitoreAttuale.MostraControllo(Me)
                Return
            End If
            annullabile = annulla
            messaggio = msg
            testo = msg
            If annullabile Then
                testo &= " Per annullare l'operazione in corso premere il tasto escape"
            End If

            If Not _haIncrementatoOverlay Then
                Interlocked.Increment(_overlayAttiviCount)
                _haIncrementatoOverlay = True
            End If

            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then
                Return
            End If

            _mostrato = True
            contenitore.MostraControllo(Me)

            Try
                If Me.IsHandleCreated AndAlso _touk IsNot Nothing Then
                    _touk.RiassegnaControllo(Me.Handle)
                End If
            Catch exTouk As Exception
                WriteLog.ScriviLog("Avvia (RiassegnaControllo): " & exTouk.Message)
            End Try

            If lettura IsNot Nothing Then
                AgganciaFineLettura()
                lettura.LeggiTesto(percentuale & " " & testo, escludiPunteggiatura:=True)
            End If

            _soundCts = New CancellationTokenSource()
            Task.Run(Sub() RiproduciSuonoAttesaLoop(_soundCts.Token), _soundCts.Token)
        Catch ex As Exception
            WriteLog.ScriviLog("Avvia: " & ex.Message)
        End Try
    End Sub

    Private Sub AgganciaFineLettura()
        Try
            SyncLock _cicloAttesaLock
                If _handlerFineLetturaAgganciato Then Return
                If lettura Is Nothing Then Return
                AddHandler lettura.OnFineLettura, AddressOf Lettura_OnFineLettura
                _handlerFineLetturaAgganciato = True
            End SyncLock
        Catch ex As Exception
            WriteLog.ScriviLog("AgganciaFineLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub SganciaFineLettura()
        Try
            SyncLock _cicloAttesaLock
                If Not _handlerFineLetturaAgganciato Then Return
                If lettura IsNot Nothing Then
                    RemoveHandler lettura.OnFineLettura, AddressOf Lettura_OnFineLettura
                End If
                _handlerFineLetturaAgganciato = False
            End SyncLock
        Catch ex As Exception
            WriteLog.ScriviLog("SganciaFineLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub Lettura_OnFineLettura()
        Try
            If _isDisposed OrElse Not _mostrato Then Return
            If InSecondoPiano() Then Return
            AvviaProssimoCicloAttesa()
        Catch ex As Exception
            WriteLog.ScriviLog("Lettura_OnFineLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaProssimoCicloAttesa()
        Try
            Dim tokenLocale As CancellationToken

            SyncLock _cicloAttesaLock
                InterrompiCicloAttesaSenzaLock()
                _cicloAttesaCts = New CancellationTokenSource()
                tokenLocale = _cicloAttesaCts.Token
            End SyncLock

            Task.Run(Sub() AttendiELeggiAttendere(tokenLocale), tokenLocale)
        Catch ex As Exception
            WriteLog.ScriviLog("AvviaProssimoCicloAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub InterrompiCicloAttesa()
        Try
            SyncLock _cicloAttesaLock
                InterrompiCicloAttesaSenzaLock()
            End SyncLock
        Catch ex As Exception
            WriteLog.ScriviLog("InterrompiCicloAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub InterrompiCicloAttesaSenzaLock()
        Try
            If _cicloAttesaCts IsNot Nothing Then
                Try
                    _cicloAttesaCts.Cancel()
                    _cicloAttesaCts.Dispose()
                Catch exCancella As Exception
                    WriteLog.ScriviLog("InterrompiCicloAttesaSenzaLock: " & exCancella.Message)
                End Try
                _cicloAttesaCts = Nothing
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("InterrompiCicloAttesaSenzaLock (esterno): " & ex.Message)
        End Try
    End Sub

    Private Async Sub AttendiELeggiAttendere(token As CancellationToken)
        Try
            Await Task.Delay(10000, token)
            If token.IsCancellationRequested Then Return
            If _isDisposed OrElse Not _mostrato Then Return
            If InSecondoPiano() Then Return
            If lettura IsNot Nothing Then
                lettura.LeggiTesto("Attendere", escludiPunteggiatura:=True)
            End If
        Catch ex As TaskCanceledException
        Catch ex As Exception
            WriteLog.ScriviLog("AttendiELeggiAttendere: " & ex.Message)
        End Try
    End Sub

    Private Async Sub RiproduciSuonoAttesaLoop(token As CancellationToken)
        Try
            suonoAttesa = New WGSuoni()
            While Not token.IsCancellationRequested
                SyncLock _lock
                    Try
                        If Not InSecondoPiano() Then suonoAttesa.Attesa()
                    Catch exSuono As Exception
                        WriteLog.ScriviLog("RiproduciSuonoAttesaLoop (Attesa): " & exSuono.Message)
                    End Try
                End SyncLock
                Await Task.Delay(5000, token)
            End While
        Catch ex As TaskCanceledException
        Catch ex As Exception
            WriteLog.ScriviLog("RiproduciSuonoAttesaLoop: " & ex.Message)
        Finally
            SyncLock _lock
                If suonoAttesa IsNot Nothing Then
                    suonoAttesa.Dispose()
                    suonoAttesa = Nothing
                End If
            End SyncLock
        End Try
    End Sub

    Public Sub Fine()
        Try
            If _isDisposed Then Return
            _isDisposed = True
            _mostrato = False

            DecrementaContatoreOverlay()

            InterrompiCicloAttesa()
            SganciaFineLettura()

            If _soundCts IsNot Nothing Then
                Try
                    _soundCts.Cancel()
                    _soundCts.Dispose()
                Catch exCts As Exception
                    WriteLog.ScriviLog("Fine (CancellationTokenSource): " & exCts.Message)
                End Try
                _soundCts = Nothing
            End If

            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.RimuoviControllo(Me)
                End If
            Catch exCont As Exception
                WriteLog.ScriviLog("Fine (contenitore): " & exCont.Message)
            End Try

            Try
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                End If
            Catch exTastiera As Exception
                WriteLog.ScriviLog("Fine (rimozione hook tastiera): " & exTastiera.Message)
            End Try

            Try
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                End If
            Catch exTouk As Exception
                WriteLog.ScriviLog("Fine (rimozione hook touk): " & exTouk.Message)
            End Try

            Try
                Me.Dispose()
            Catch exDisp As Exception
                WriteLog.ScriviLog("Fine (Dispose): " & exDisp.Message)
            End Try
        Catch ex As Exception
            WriteLog.ScriviLog("Fine: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaStato(newPercentage As String)
        Try
            If _isDisposed Then Return
            If Me.IsHandleCreated AndAlso Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf AggiornaStato), newPercentage)
                Return
            End If

            SyncLock _lock
                percentuale = newPercentage
            End SyncLock

            AggiornaVisualizzazione()
        Catch ex As Exception
            WriteLog.ScriviLog("AggiornaStato: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaPercentuale(valore As Integer)
        Try
            If _isDisposed Then Return
            If Me.IsHandleCreated AndAlso Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of Integer)(AddressOf AggiornaPercentuale), valore)
                Return
            End If

            Dim limitato As Integer = Math.Max(0, Math.Min(100, valore))
            If limitato = _ultimaPercentuale Then Return
            _ultimaPercentuale = limitato

            Dim testoPercentuale As String = limitato.ToString(System.Globalization.CultureInfo.InvariantCulture) & " per cento"
            SyncLock _lock
                percentuale = testoPercentuale
            End SyncLock

            AggiornaVisualizzazione()

            If _mostrato AndAlso lettura IsNot Nothing AndAlso Not InSecondoPiano() Then
                InterrompiCicloAttesa()
                lettura.LeggiTesto(testoPercentuale, escludiPunteggiatura:=True)
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("AggiornaPercentuale: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaVisualizzazione()
        Try
            If _isDisposed Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If Not InSottofondo AndAlso contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                contenitore.AggiornaTestoAttesa((percentuale & " " & testo).Trim())
            End If
            If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then Me.Invalidate()
        Catch ex As Exception
            WriteLog.ScriviLog("AggiornaVisualizzazione: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
                End If
                InterrompiCicloAttesa()
                SganciaFineLettura()
                DecrementaContatoreOverlay()
            End If
        Catch ex As Exception
            WriteLog.ScriviLog("Dispose: " & ex.Message)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class