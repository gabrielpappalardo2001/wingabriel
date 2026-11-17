Imports System
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

''' <summary>
''' L'attesa dei giochi: usa l'attesa del contenitore di WinGabriel (WGContenitore.MostraAttesa) per far vedere il testo sullo schermo,
''' legge i messaggi con la sintesi vocale e, a differenza di WGProcessoInCorso, con Esc (o con il gesto di uscita) non annulla da sola:
''' avvisa chi la usa, che chiede all'utente se vuole interrompere il gioco.
''' </summary>
Friend NotInheritable Class AttesaGiochi
    Implements IDisposable

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private _attiva As Boolean = False
    Private _testoLetto As String = ""
    Private ReadOnly _log As New Logger()

    ''' <summary>Si è premuto Esc (o fatto il gesto di uscita) mentre si aspettava.</summary>
    Public Event InterruzioneRichiesta As EventHandler

    Public Sub New()
        Try
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.New: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Attiva As Boolean
        Get
            Return _attiva
        End Get
    End Property

    ''' <summary>Mostra l'attesa con questo testo sullo schermo e legge testoParlato (se vuoto, il testo dello schermo).</summary>
    Public Sub Avvia(testoSchermo As String, Optional testoParlato As String = Nothing)
        Try
            _attiva = True
            _testoLetto = If(String.IsNullOrEmpty(testoParlato), testoSchermo, testoParlato)
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.MostraAttesa(testoSchermo)
            Leggi(_testoLetto)
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.Avvia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Cambia il testo sullo schermo (e lo legge: di solito è un messaggio nuovo della partita).</summary>
    Public Sub Aggiorna(testoSchermo As String, Optional testoParlato As String = Nothing)
        Try
            If Not _attiva Then Return
            _testoLetto = If(String.IsNullOrEmpty(testoParlato), testoSchermo, testoParlato)
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.AggiornaTestoAttesa(testoSchermo)
            Leggi(_testoLetto)
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.Aggiorna: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            If Not _attiva Then Return
            _attiva = False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.NascondiAttesa()
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Leggi(testo As String)
        Try
            If Not String.IsNullOrWhiteSpace(testo) Then sintesivocale.GetInstance().LeggiTesto(testo, escludiPunteggiatura:=True)
        Catch exLog As Exception
            RegistroErrori.Registra("AttesaGiochi.Leggi", exLog)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse Not _attiva Then Return
            Select Case tasto
                Case Keys.Escape
                    tasto = Keys.None
                    RaiseEvent InterruzioneRichiesta(Me, EventArgs.Empty)
                Case Keys.Up, Keys.Down
                    tasto = Keys.None
                    Leggi(_testoLetto)
            End Select
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        Try
            If _attiva Then RaiseEvent InterruzioneRichiesta(Me, EventArgs.Empty)
        Catch ex As Exception
            _log.ScriviLog("AttesaGiochi.EscapeGesto: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Ferma()
            _tastiera = Nothing
            _touk = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("AttesaGiochi.Dispose", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Esegue un lavoro mostrando l'attesa (solo se dura più di un attimo) e la toglie sempre alla fine, anche in caso di errore.
    ''' Per le operazioni brevi come entrare nei giochi, creare una partita, inviare una mossa.
    ''' </summary>
    Public Shared Async Function EseguiAsync(Of T)(testo As String, lavoro As Func(Of Task(Of T))) As Task(Of T)
        Try
            Dim attivita As Task(Of T) = lavoro()
            If Await Task.WhenAny(attivita, Task.Delay(350)) Is attivita Then Return Await attivita
            Using attesa As New AttesaGiochi()
                attesa.Avvia(testo)
                Return Await attivita
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("AttesaGiochi.EseguiAsync", ex)
            Throw
        End Try
    End Function

    Public Shared Async Function EseguiAsync(testo As String, lavoro As Func(Of Task)) As Task
        Try
            Dim attivita As Task = lavoro()
            If Await Task.WhenAny(attivita, Task.Delay(350)) Is attivita Then
                Await attivita
                Return
            End If
            Using attesa As New AttesaGiochi()
                attesa.Avvia(testo)
                Await attivita
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("AttesaGiochi.EseguiAsync", ex)
            Throw
        End Try
    End Function

End Class
