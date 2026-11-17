' ============================================================================================
' WGGriglia - elementi disposti su righe e colonne, come le icone del desktop o della home di un tablet
' ============================================================================================
'
' Frecce destra e sinistra: elemento successivo e precedente (anche passando alla riga dopo).
' Frecce su e giù: stessa colonna della riga sopra o sotto. Home e Fine: inizio e fine della riga;
' Control Home e Control Fine: primo e ultimo elemento. Una lettera porta al primo elemento che
' comincia così. Invio sceglie, Alt chiede le opzioni, Esc solleva EscPremuto. Gli altri tasti con
' Control, Backspace, Canc, F1 e F2 vanno a chi usa la griglia con l'evento Comando.
' Con VociPerPagina (la home: 10) gli elementi stanno su pagine, come sui tablet: due colonne in
' verticale e cinque in orizzontale, Pagina giù e Pagina su o tre dita a sinistra e a destra per
' sfogliarle, e i puntini delle pagine in fondo. Con VociDock c'è anche il dock, come sui tablet: una
' fila in fondo uguale su tutte le pagine (la home: agenda e rubrica); ci si arriva andando avanti
' dall'ultimo elemento della pagina o in giù dall'ultima riga, e i suoi elementi hanno gli indici
' dopo quelli della griglia (IndiceCorrente, ElementoScelto). La barra di stato (Tab) può stare in fondo o in alto,
' come sui telefoni, con le icone disegnate del Wi-Fi a tacche e della batteria.
' Touch: un colpo a destra o a sinistra per spostarsi, doppio tocco per scegliere, toccare e scorrere
' col dito per esplorare, un colpo in su per le opzioni, doppio tocco con due dita come Esc; nella
' barra di stato un colpo in su o in giù regola (per esempio il volume).
' Wi-Fi e Bluetooth della barra di stato aprono un menu disegnato dalla griglia stessa: WGGriglia.Menu.vb.
' Ogni spostamento dice solo il nome dell'elemento; ai bordi un suono.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Public Class ComandoGrigliaEventArgs
    Inherits EventArgs
    Public ReadOnly Codice As Keys
    Public ReadOnly Ctrl As Boolean
    ' Da mettere a vero se il tasto è stato usato.
    Public Gestito As Boolean

    Public Sub New(codice As Keys, ctrl As Boolean)
        Me.Codice = codice
        Me.Ctrl = ctrl
    End Sub
End Class

''' <summary>Icona disegnata accanto a una voce della barra di stato.</summary>
Public Class IconaStato
    ' "batteria" (Livello in percentuale, InCarica) o "wifi" (Livello in tacche da 0 a 4).
    Public Tipo As String = ""
    Public Livello As Integer
    Public InCarica As Boolean
End Class

''' <summary>Una voce della barra di stato della griglia (ora, volume, batteria...).</summary>
Public Class VoceStatoGriglia
    ' Testo breve da mostrare e leggere, calcolato ogni volta.
    Public Testo As Func(Of String)
    ' Frecce su e giù sulla voce (per esempio il volume); Nothing se la voce non si regola.
    Public Aumenta As Action
    Public Diminuisci As Action
    ' Invio sulla voce; Nothing per rileggerla soltanto.
    Public Invio As Action
    ' Invio (e le frecce su e giù, se ci sono) apre un'altra finestra o annuncia da sé (per esempio il
    ' Wi-Fi, l'uscita audio): allora la voce non si rilegge, per non parlare sopra la finestra.
    Public ApreFinestra As Boolean
    ' Icona disegnata prima del testo (Wi-Fi, batteria); Nothing per nessuna.
    Public Icona As Func(Of IconaStato)
    ' Testo corto da disegnare nella barra (per esempio "80%"); Nothing per disegnare Testo. Si legge sempre Testo.
    Public TestoBreve As Func(Of String)
End Class

Partial Public Class WGGriglia
    Inherits UserControl
    Implements IWGAnnuncioIngresso

    Public Event ElementoScelto(indice As Integer)
    Public Event EscPremuto()
    Public Event OpzioniRichieste()
    Public Event Comando(sender As Object, e As ComandoGrigliaEventArgs)

    Private Shared ReadOnly log As New Logger()
    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private ReadOnly _voci As New List(Of String)()
    Private ReadOnly _titolo As String
    Private _indice As Integer = 0
    Private _colonne As Integer = 4
    Private _primaVisibilita As Boolean = True
    Private _suoni As WGSuoni

    ''' <summary>Elementi per pagina (la home: 10); 0 = nessuna pagina, la griglia scorre.</summary>
    Public Property VociPerPagina As Integer
    ''' <summary>Barra di stato in alto, come sui telefoni, invece che in fondo.</summary>
    Public Property BarraStatoInAlto As Boolean
    ''' <summary>Elementi del dock, uguali su tutte le pagine; indici dopo quelli della griglia.</summary>
    Public Property VociDock As New List(Of String)()
    Private _inDock As Boolean
    Private _indiceDock As Integer

    ''' <summary>Parte dal dock, sull'elemento indicato (da chiamare prima di Avvia).</summary>
    Public Sub SelezionaDock(indice As Integer)
        If VociDock Is Nothing OrElse indice < 0 OrElse indice >= VociDock.Count Then Return
        _inDock = True
        _indiceDock = indice
    End Sub

    Private Function HaDock() As Boolean
        Return VociDock IsNot Nothing AndAlso VociDock.Count > 0
    End Function

    Public Sub New(titolo As String, voci As IEnumerable(Of String), Optional indiceIniziale As Integer = 0)
        _titolo = If(titolo, "")
        If voci IsNot Nothing Then _voci.AddRange(voci)
        _indice = Math.Max(0, Math.Min(indiceIniziale, _voci.Count - 1))
        ' Come le icone del desktop: con pochi elementi due colonne, così anche le frecce su e giù servono.
        _colonne = If(_voci.Count <= 8, 2, If(_voci.Count <= 18, 3, 4))
        ' Il disegno lo fa Disegna con GDI: niente doppio buffer né sfondo di Windows Forms.
        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.Opaque, True)
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer, False)
        Me.DoubleBuffered = False
        CaricaAspetto()
        Me.AccessibleName = _titolo
        Me.AccessibleRole = AccessibleRole.None
    End Sub

    ''' <summary>Elemento scelto; per il dock l'indice viene dopo gli elementi della griglia.</summary>
    Public ReadOnly Property IndiceCorrente As Integer
        Get
            Return If(_inDock AndAlso HaDock(), _voci.Count + _indiceDock, _indice)
        End Get
    End Property

    ''' <summary>Cambia il testo di un elemento senza ridisegnare la griglia (per esempio un conteggio).</summary>
    Public Sub AggiornaVoce(indice As Integer, testo As String)
        Try
            If indice < 0 OrElse indice >= _voci.Count Then Return
            _voci(indice) = If(testo, "")
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.AggiornaVoce: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.MostraControllo(Me)
            ' Il controllo nasce già visibile, quindi aggiungendolo al contenitore il cambio di visibilità
            ' non scatta: il primo annuncio, con il titolo, si programma qui.
            _primaVisibilita = False
            _annuncioConTitolo = True
            _tentativiAnnuncio = 0
            _timerAnnuncio.Stop()
            _timerAnnuncio.Interval = 400
            _timerAnnuncio.Start()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            _tastiera = Nothing
            _touk = Nothing
            _timerAnnuncio.Stop()
            _timerAnnuncio.Dispose()
            _timerStato.Stop()
            _timerStato.Dispose()
            _timerMenu.Stop()
            _timerMenu.Dispose()
            _menuAMano.Clear()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.Chiudi: " & ex.Message)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Return contenitore IsNot Nothing AndAlso contenitore.PuoRicevereInput(Me)
        Catch ex As Exception
            log.ScriviLog("WGGriglia.PuoRicevere: " & ex.Message)
            Return False
        End Try
    End Function

#Region "Righe, colonne e pagine"

    ' Con le pagine: due colonne in verticale e cinque in orizzontale, come la home dei tablet.
    Private Function Colonne() As Integer
        If VociPerPagina > 0 Then
            Dim colonnePagina As Integer = If(Me.ClientSize.Width > Me.ClientSize.Height, 5, 2)
            Return Math.Min(colonnePagina, VociPerPagina)
        End If
        Return _colonne
    End Function

    Private Function Righe() As Integer
        Return Math.Max(1, CInt(Math.Ceiling(_voci.Count / CDbl(Colonne()))))
    End Function

    Private Function Pagina(indice As Integer) As Integer
        Return If(VociPerPagina > 0, Math.Max(0, indice) \ VociPerPagina, 0)
    End Function

    Private Function PagineTotali() As Integer
        If VociPerPagina <= 0 Then Return 1
        Return Math.Max(1, CInt(Math.Ceiling(_voci.Count / CDbl(VociPerPagina))))
    End Function

    Private Function DescrizionePagina(indice As Integer) As String
        Return "Pagina " & (Pagina(indice) + 1) & " di " & PagineTotali()
    End Function

    ''' <summary>Pagina successiva (avanti) o precedente, dal primo elemento; ai bordi un suono.</summary>
    Public Sub CambiaPagina(avanti As Boolean)
        If VociPerPagina <= 0 OrElse _voci.Count = 0 Then Return
        Dim nuova As Integer = Pagina(_indice) + If(avanti, 1, -1)
        If nuova < 0 OrElse nuova >= PagineTotali() Then
            Bordo()
            Return
        End If
        _inBarraStato = False
        _inDock = False
        _indice = nuova * VociPerPagina
        Me.Invalidate()
        LeggiCorrente(DescrizionePagina(_indice) & ". ")
    End Sub

#End Region

    Private Sub LeggiCorrente(Optional prefisso As String = "")
        Try
            If _inDock AndAlso HaDock() Then
                sintesivocale.GetInstance().LeggiTesto(prefisso & VociDock(_indiceDock))
                Return
            End If
            If _voci.Count = 0 Then
                sintesivocale.GetInstance().LeggiTesto(prefisso & "vuota")
                Return
            End If
            sintesivocale.GetInstance().LeggiTesto(prefisso & _voci(_indice))
        Catch ex As Exception
            log.ScriviLog("WGGriglia.LeggiCorrente: " & ex.Message)
        End Try
    End Sub

    Private _tickAnnuncio As Integer = 0

    ' Il titolo, con la pagina se ci sono più pagine.
    Private Function TitoloParlato() As String
        Return _titolo & If(VociPerPagina > 0 AndAlso PagineTotali() > 1, ", " & DescrizionePagina(_indice), "") & ". "
    End Function

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        ' Con il menu della barra di stato aperto si torna lì, non sulla griglia; se è appena stato letto
        ' non lo si ricomincia da capo.
        If MenuAperto Then
            _annuncioConTitolo = False
            If _menuDaRileggere OrElse Environment.TickCount - _tickAnnuncio >= 1000 Then LeggiMenu(True)
            Return
        End If
        _tickAnnuncio = Environment.TickCount
        _annuncioConTitolo = False
        LeggiCorrente(TitoloParlato())
    End Sub

    Private Sub Bordo(Optional inBarraStato As Boolean = False)
        Try
            If _suoni Is Nothing Then _suoni = New WGSuoni()
            _suoni.LimiteRaggiunto()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.Bordo: " & ex.Message)
        End Try
        If inBarraStato Then LeggiStato() Else LeggiCorrente()
    End Sub

    Private Sub VaiA(indice As Integer)
        If _voci.Count = 0 Then Return
        If indice < 0 OrElse indice >= _voci.Count Then
            Bordo()
            Return
        End If
        Dim cambioPagina As Boolean = VociPerPagina > 0 AndAlso Pagina(indice) <> Pagina(_indice)
        _inDock = False
        _indice = indice
        TieniVisibileSelezionato()
        Me.Invalidate()
        LeggiCorrente(If(cambioPagina, DescrizionePagina(_indice) & ". ", ""))
    End Sub

    ' Su e giù restano nella pagina: oltre si sente il bordo (le pagine si sfogliano apposta); in giù
    ' dall'ultima riga si arriva al dock, in su dal dock si torna alla pagina.
    Private Sub VaiInVerticale(direzione As Integer)
        If _inDock Then
            If direzione < 0 Then EsciDalDock() Else Bordo()
            Return
        End If
        Dim destinazione As Integer = _indice + direzione * Colonne()
        Dim fuoriPagina As Boolean = destinazione >= _voci.Count OrElse (VociPerPagina > 0 AndAlso destinazione >= 0 AndAlso Pagina(destinazione) <> Pagina(_indice))
        If direzione > 0 AndAlso fuoriPagina AndAlso HaDock() Then
            EntraNelDock(0)
            Return
        End If
        If VociPerPagina > 0 AndAlso destinazione >= 0 AndAlso destinazione < _voci.Count AndAlso Pagina(destinazione) <> Pagina(_indice) Then
            Bordo()
            Return
        End If
        VaiA(destinazione)
    End Sub

    Private Sub EntraNelDock(indice As Integer)
        _inDock = True
        _indiceDock = Math.Max(0, Math.Min(indice, VociDock.Count - 1))
        Me.Invalidate()
        LeggiCorrente("Dock, ")
    End Sub

    ' Dal dock si torna all'ultimo elemento della pagina mostrata.
    Private Sub EsciDalDock()
        _inDock = False
        If _voci.Count = 0 Then
            Bordo()
            Return
        End If
        Dim ultimo As Integer = If(VociPerPagina > 0, Math.Min(_voci.Count - 1, (Pagina(_indice) + 1) * VociPerPagina - 1), _voci.Count - 1)
        _indice = ultimo
        Me.Invalidate()
        LeggiCorrente()
    End Sub

    ' Avanti e indietro: nella pagina, poi nel dock (se c'è); senza dock si passa alla pagina dopo.
    Private Sub Avanti()
        If _inDock Then
            If _indiceDock < VociDock.Count - 1 Then
                _indiceDock += 1
                Me.Invalidate()
                LeggiCorrente()
            Else
                Bordo()
            End If
            Return
        End If
        Dim ultimoPagina As Boolean = _indice >= _voci.Count - 1 OrElse (VociPerPagina > 0 AndAlso Pagina(_indice + 1) <> Pagina(_indice))
        If ultimoPagina AndAlso HaDock() Then
            EntraNelDock(0)
            Return
        End If
        ' Con le pagine, come sui tablet, avanti non passa alla pagina dopo: si sfoglia apposta.
        If ultimoPagina AndAlso VociPerPagina > 0 Then
            Bordo()
            Return
        End If
        VaiA(_indice + 1)
    End Sub

    Private Sub Indietro()
        If _inDock Then
            If _indiceDock > 0 Then
                _indiceDock -= 1
                Me.Invalidate()
                LeggiCorrente()
            Else
                EsciDalDock()
            End If
            Return
        End If
        ' All'inizio della pagina non si torna alla pagina prima: si sente il bordo.
        If VociPerPagina > 0 AndAlso _indice > 0 AndAlso Pagina(_indice - 1) <> Pagina(_indice) Then
            Bordo()
            Return
        End If
        VaiA(_indice - 1)
    End Sub

#Region "Barra di stato"

    ' Voci della barra di stato; vuota = nessuna barra. Col Tab si passa dalla griglia alla barra e ritorno.
    Private ReadOnly _vociStato As New List(Of VoceStatoGriglia)()
    Private _inBarraStato As Boolean
    Private _indiceStato As Integer
    Private WithEvents _timerStato As New Timer() With {.Interval = 30000}

    Public Sub ImpostaBarraStato(voci As IEnumerable(Of VoceStatoGriglia))
        _vociStato.Clear()
        If voci IsNot Nothing Then _vociStato.AddRange(voci.Where(Function(v) v IsNot Nothing AndAlso v.Testo IsNot Nothing))
        _indiceStato = 0
        If _vociStato.Count > 0 Then _timerStato.Start() Else _timerStato.Stop()
        Me.Invalidate()
    End Sub

    ' Ogni mezzo minuto (in alto ogni dieci secondi, per batteria e Wi-Fi) si ridisegna per chi vede.
    Private Sub _timerStato_Tick(sender As Object, e As EventArgs) Handles _timerStato.Tick
        If BarraStatoInAlto AndAlso _timerStato.Interval <> 10000 Then _timerStato.Interval = 10000
        If Me.Visible AndAlso Not Me.IsDisposed Then Me.Invalidate()
    End Sub

    Private Function TestoStato(indice As Integer) As String
        Try
            If indice < 0 OrElse indice >= _vociStato.Count Then Return ""
            Return If(_vociStato(indice).Testo(), "")
        Catch ex As Exception
            log.ScriviLog("WGGriglia.TestoStato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IconaDi(indice As Integer) As IconaStato
        Try
            If indice < 0 OrElse indice >= _vociStato.Count OrElse _vociStato(indice).Icona Is Nothing Then Return Nothing
            Return _vociStato(indice).Icona.Invoke()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.IconaDi: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub LeggiStato(Optional prefisso As String = "")
        sintesivocale.GetInstance().LeggiTesto(prefisso & TestoStato(_indiceStato))
    End Sub

    Private Sub EntraInBarraStato()
        _inBarraStato = True
        Me.Invalidate()
        LeggiStato("Barra di stato. ")
    End Sub

    Private Sub SpostaInBarraStato(direzione As Integer)
        Dim nuovo As Integer = _indiceStato + direzione
        If nuovo < 0 OrElse nuovo >= _vociStato.Count Then
            Bordo(True)
            Return
        End If
        _indiceStato = nuovo
        Me.Invalidate()
        LeggiStato()
    End Sub

    Private Sub RegolaInBarraStato(aumenta As Boolean)
        Dim voce As VoceStatoGriglia = _vociStato(_indiceStato)
        Dim azione As Action = If(aumenta, voce.Aumenta, voce.Diminuisci)
        If azione IsNot Nothing Then azione()
        Me.Invalidate()
        If azione Is Nothing OrElse Not voce.ApreFinestra Then LeggiStato()
    End Sub

    Private Sub InvioInBarraStato()
        Dim voce As VoceStatoGriglia = _vociStato(_indiceStato)
        ' Chi vuole aprire il menu disegnato dalla griglia (Wi-Fi, Bluetooth) la trova qui.
        _grigliaPerMenu = Me
        If voce.Invio IsNot Nothing Then voce.Invio()
        Me.Invalidate()
        If Not voce.ApreFinestra AndAlso Not MenuAperto Then LeggiStato()
    End Sub

    ' Tasti mentre si è nella barra di stato; vero se il tasto è stato usato.
    Private Function TastoBarraStato(codice As Keys) As Boolean
        Select Case codice
            Case Keys.Tab, Keys.Escape
                _inBarraStato = False
                Me.Invalidate()
                LeggiCorrente()
            Case Keys.Right : SpostaInBarraStato(1)
            Case Keys.Left : SpostaInBarraStato(-1)
            Case Keys.Home
                _indiceStato = 0
                Me.Invalidate()
                LeggiStato()
            Case Keys.End
                _indiceStato = _vociStato.Count - 1
                Me.Invalidate()
                LeggiStato()
            Case Keys.Up, Keys.Down : RegolaInBarraStato(codice = Keys.Up)
            Case Keys.Enter, Keys.Space : InvioInBarraStato()
            Case Keys.Insert : LeggiStato()
            Case Else
                Return False
        End Select
        Return True
    End Function

#End Region

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse Not PuoRicevere() Then Return
            Dim codice As Keys = tasto And Keys.KeyCode
            Dim ctrl As Boolean = (_tastiera.ModificatoriUltimoTasto And Keys.Control) = Keys.Control
            Dim gestito As Boolean = True
            ' Con il menu aperto i tasti sono tutti suoi: la griglia sotto non si muove.
            If MenuAperto Then
                If Not ctrl Then TastoNelMenu(codice)
                tasto = Keys.None
                Return
            End If
            If Not ctrl AndAlso _vociStato.Count > 0 Then
                If _inBarraStato Then
                    If TastoBarraStato(codice) Then
                        tasto = Keys.None
                        Return
                    End If
                ElseIf codice = Keys.Tab Then
                    ' Tab: barra di stato, come l'area di notifica della barra di Windows.
                    EntraInBarraStato()
                    tasto = Keys.None
                    Return
                End If
            End If
            If ctrl OrElse codice = Keys.Back OrElse codice = Keys.Delete OrElse codice = Keys.F1 OrElse codice = Keys.F2 Then
                Dim richiesta As New ComandoGrigliaEventArgs(codice, ctrl)
                RaiseEvent Comando(Me, richiesta)
                If richiesta.Gestito Then
                    tasto = Keys.None
                    Return
                End If
            End If
            Dim numeroColonne As Integer = Colonne()
            Select Case codice
                Case Keys.Right : Avanti()
                Case Keys.Left : Indietro()
                Case Keys.Down : VaiInVerticale(1)
                Case Keys.Up : VaiInVerticale(-1)
                Case Keys.Home
                    If _inDock Then EntraNelDock(0) Else VaiA(If(ctrl, 0, (_indice \ numeroColonne) * numeroColonne))
                Case Keys.End
                    If _inDock Then EntraNelDock(VociDock.Count - 1) Else VaiA(If(ctrl, _voci.Count - 1, Math.Min(_voci.Count - 1, (_indice \ numeroColonne) * numeroColonne + numeroColonne - 1)))
                Case Keys.PageDown
                    If VociPerPagina > 0 Then CambiaPagina(True) Else gestito = False
                Case Keys.PageUp
                    If VociPerPagina > 0 Then CambiaPagina(False) Else gestito = False
                Case Keys.Enter, Keys.Space
                    If _voci.Count > 0 OrElse _inDock Then RaiseEvent ElementoScelto(IndiceCorrente)
                Case Keys.Escape
                    RaiseEvent EscPremuto()
                Case Keys.Insert
                    LeggiCorrente(TitoloParlato())
                Case Else
                    gestito = Not ctrl AndAlso CercaIniziale(codice)
            End Select
            If gestito Then tasto = Keys.None
        Catch ex As Exception
            log.ScriviLog("WGGriglia.KeyDown: " & ex.Message)
        End Try
    End Sub

#Region "Touch"

    Private Sub _touk_FlickAvanti() Handles _touk.FlickAvanti
        If Not PuoRicevere() Then Return
        If MenuAperto Then
            SpostaNelMenu(1)
            Return
        End If
        If _inBarraStato Then SpostaInBarraStato(1) Else Avanti()
    End Sub

    Private Sub _touk_FlickIndietro() Handles _touk.FlickIndietro
        If Not PuoRicevere() Then Return
        If MenuAperto Then
            SpostaNelMenu(-1)
            Return
        End If
        If _inBarraStato Then SpostaInBarraStato(-1) Else Indietro()
    End Sub

    Private Sub _touk_FlickSu() Handles _touk.FlickSu
        If Not PuoRicevere() Then Return
        If MenuAperto Then
            OpzioniNelMenu()
        ElseIf _inBarraStato Then
            RegolaInBarraStato(True)
        Else
            RaiseEvent OpzioniRichieste()
        End If
    End Sub

    Private Sub _touk_FlickGiu() Handles _touk.FlickGiu
        If Not PuoRicevere() OrElse MenuAperto Then Return
        If _inBarraStato Then RegolaInBarraStato(False)
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        If Not PuoRicevere() Then Return
        If MenuAperto Then
            InvioNelMenu()
        ElseIf _inBarraStato Then
            InvioInBarraStato()
        ElseIf _voci.Count > 0 OrElse _inDock Then
            RaiseEvent ElementoScelto(IndiceCorrente)
        End If
    End Sub

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        If Not PuoRicevere() Then Return
        If MenuAperto Then
            IndietroNelMenu()
        ElseIf _inBarraStato Then
            _inBarraStato = False
            Me.Invalidate()
            LeggiCorrente()
        Else
            RaiseEvent EscPremuto()
        End If
    End Sub

    Private Sub _touk_FlickTreDitaLaterale(avanti As Boolean) Handles _touk.FlickTreDitaLaterale
        If PuoRicevere() AndAlso Not MenuAperto Then CambiaPagina(avanti)
    End Sub

    ' Toccando e scorrendo col dito si sente l'elemento sotto il dito (anche nella barra di stato).
    Private Sub _touk_EsplorazioneTocco(x As Integer, y As Integer) Handles _touk.EsplorazioneTocco
        Try
            If Not PuoRicevere() Then Return
            Dim punto As Point = Me.PointToClient(New Point(x, y))
            If EsploraMenu(punto) Then Return
            For Each coppia As KeyValuePair(Of Integer, Rectangle) In _rettangoliStato
                If coppia.Value.Contains(punto) Then
                    If Not _inBarraStato OrElse coppia.Key <> _indiceStato Then
                        _inBarraStato = True
                        _indiceStato = coppia.Key
                        Me.Invalidate()
                        LeggiStato()
                    End If
                    Return
                End If
            Next
            For Each coppia As KeyValuePair(Of Integer, Rectangle) In _rettangoliDock
                If coppia.Value.Contains(punto) Then
                    If _inBarraStato OrElse Not _inDock OrElse coppia.Key <> _indiceDock Then
                        _inBarraStato = False
                        _inDock = True
                        _indiceDock = coppia.Key
                        Me.Invalidate()
                        LeggiCorrente("Dock, ")
                    End If
                    Return
                End If
            Next
            For Each coppia As KeyValuePair(Of Integer, Rectangle) In _rettangoliVoci
                If coppia.Value.Contains(punto) Then
                    If _inBarraStato OrElse _inDock OrElse coppia.Key <> _indice Then
                        _inBarraStato = False
                        _inDock = False
                        _indice = coppia.Key
                        Me.Invalidate()
                        LeggiCorrente()
                    End If
                    Return
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGGriglia.EsplorazioneTocco: " & ex.Message)
        End Try
    End Sub

#End Region

    ' Ricerca mentre si scrive: le lettere premute a meno di un secondo l'una dall'altra formano l'inizio
    ' del nome cercato ("po" porta a "Posta"); una lettera sola porta al prossimo elemento che comincia così.
    Private _cercato As String = ""
    Private _tickUltimaLettera As Integer

    Private Function CercaIniziale(codice As Keys) As Boolean
        Dim lettera As Char
        If codice >= Keys.A AndAlso codice <= Keys.Z Then
            lettera = Char.ToLowerInvariant(ChrW(AscW("A"c) + (codice - Keys.A)))
        ElseIf codice >= Keys.D0 AndAlso codice <= Keys.D9 Then
            lettera = ChrW(AscW("0"c) + (codice - Keys.D0))
        Else
            Return False
        End If
        If _voci.Count = 0 Then Return True
        Dim adesso As Integer = Environment.TickCount
        If adesso - _tickUltimaLettera > 1000 Then _cercato = ""
        _tickUltimaLettera = adesso
        _cercato &= lettera
        ' Con più lettere si parte dall'elemento corrente (può già andare bene), con una sola dal successivo.
        Dim partenza As Integer = If(_cercato.Length > 1, 0, 1)
        For passo As Integer = partenza To _voci.Count - 1 + partenza
            Dim i As Integer = (_indice + passo) Mod _voci.Count
            If WGRicerca.Normalizza(_voci(i)).StartsWith(_cercato, StringComparison.Ordinal) Then
                If i = _indice Then
                    LeggiCorrente()
                Else
                    VaiA(i)
                End If
                Return True
            End If
        Next
        Return True
    End Function

    Private Sub _tastiera_AltPremuto() Handles _tastiera.AltPremuto
        Try
            If Not PuoRicevere() Then Return
            If MenuAperto Then
                OpzioniNelMenu()
                Return
            End If
            RaiseEvent OpzioniRichieste()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.AltPremuto: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        Try
            If Not Me.Visible OrElse Me.IsDisposed Then Return
            ' La prima volta dice il titolo; tornando da un'altra finestra solo l'elemento corrente.
            ' L'annuncio passa da un timer: alla prima apparizione, all'avvio di WinGabriel, la maniglia
            ' della finestra spesso non c'è ancora e gli altri annunci dell'avvio coprirebbero il titolo.
            _annuncioConTitolo = _annuncioConTitolo OrElse _primaVisibilita
            _primaVisibilita = False
            _timerAnnuncio.Stop()
            _timerAnnuncio.Interval = If(_annuncioConTitolo, 400, 60)
            _timerAnnuncio.Start()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.OnVisibleChanged: " & ex.Message)
        End Try
    End Sub

    Private WithEvents _timerAnnuncio As New Timer()
    Private _annuncioConTitolo As Boolean
    Private _tentativiAnnuncio As Integer

    Private Sub _timerAnnuncio_Tick(sender As Object, e As EventArgs) Handles _timerAnnuncio.Tick
        Try
            _timerAnnuncio.Stop()
            If Me.IsDisposed Then Return
            If Not Me.Visible Then
                ' All'avvio la finestra di WinGabriel può non essere ancora mostrata: si riprova per qualche secondo.
                _tentativiAnnuncio += 1
                If _annuncioConTitolo AndAlso _tentativiAnnuncio < 20 AndAlso Not IsNothing(Me.Parent) Then _timerAnnuncio.Start()
                Return
            End If
            Dim conTitolo As Boolean = _annuncioConTitolo
            _annuncioConTitolo = False
            ' Con il menu della barra di stato aperto lo rilegge il suo timer, quando la griglia torna davanti.
            If MenuAperto Then Return
            If Environment.TickCount - _tickAnnuncio < 600 Then Return
            ' Tornando da una finestra aperta dalla barra di stato si resta sulla barra, e si sente la sua voce.
            If _inBarraStato AndAlso _vociStato.Count > 0 AndAlso Not conTitolo Then
                LeggiStato()
            ElseIf conTitolo Then
                LeggiCorrente(TitoloParlato())
            Else
                LeggiCorrente()
            End If
        Catch ex As Exception
            log.ScriviLog("WGGriglia._timerAnnuncio_Tick: " & ex.Message)
        End Try
    End Sub

#Region "Disegno con le API di Windows, come WGListaView"

    ' Il disegno non passa da GDI+: WM_PAINT viene gestito direttamente con GDI (BeginPaint, un DC in
    ' memoria, FillRect, FrameRect, DrawText e un solo BitBlt finale), come fa il menu. Colori e
    ' carattere sono quelli dei menu nelle impostazioni (ColoreSfondo, ColoreTesto, ColoreSelezione,
    ' ColoreFuoco, MenuCarattere, FontDimensione, ContrastoAlto). Senza pagine, se gli elementi non
    ' entrano nella finestra la griglia scorre per righe tenendo sempre visibile quello selezionato;
    ' con le pagine le caselle si allargano per riempire la pagina.

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PAINTSTRUCT
        Public hdc As IntPtr
        Public fErase As Integer
        Public rcPaint As RECT
        Public fRestore As Integer
        Public fIncUpdate As Integer
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=32)>
        Public rgbReserved As Byte()
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function BeginPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function EndPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleDC(hDC As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleBitmap(hDC As IntPtr, nWidth As Integer, nHeight As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hDC As IntPtr, hObject As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function DeleteDC(hDC As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function BitBlt(hDestDC As IntPtr, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, hSrcDC As IntPtr, xSrc As Integer, ySrc As Integer, dwRop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateSolidBrush(crColor As Integer) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function FillRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")>
    Private Shared Function FrameRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hDC As IntPtr, iBkMode As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hDC As IntPtr, crColor As Integer) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DrawText(hDC As IntPtr, lpString As String, nCount As Integer, ByRef lpRect As RECT, uFormat As UInteger) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFontW(nHeight As Integer, nWidth As Integer, nEscapement As Integer, nOrientation As Integer, fnWeight As Integer,
                                        fdwItalic As UInteger, fdwUnderline As UInteger, fdwStrikeOut As UInteger, fdwCharSet As UInteger,
                                        fdwOutputPrecision As UInteger, fdwClipPrecision As UInteger, fdwQuality As UInteger,
                                        fdwPitchAndFamily As UInteger, lpszFace As String) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetDC(hWnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function GetDeviceCaps(hDC As IntPtr, nIndex As Integer) As Integer
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    Private Const WM_PAINT As Integer = &HF
    Private Const WM_ERASEBKGND As Integer = &H14
    Private Const SRCCOPY As Integer = &HCC0020
    Private Const TRASPARENTE As Integer = 1
    Private Const LOGPIXELSY As Integer = 90
    Private Const SM_CYSCREEN As Integer = 1
    Private Const DT_CENTER As UInteger = &H1
    Private Const DT_VCENTER As UInteger = &H4
    Private Const DT_WORDBREAK As UInteger = &H10
    Private Const DT_SINGLELINE As UInteger = &H20
    Private Const DT_CALCRECT As UInteger = &H400
    Private Const DT_NOPREFIX As UInteger = &H800
    Private Const DT_END_ELLIPSIS As UInteger = &H8000
    Private Const CLEARTYPE_QUALITY As UInteger = 5

    Private _hMemDC As IntPtr
    Private _hMemBitmap As IntPtr
    Private _hOldMemBitmap As IntPtr
    Private _memLarghezza As Integer
    Private _memAltezza As Integer
    Private _hFont As IntPtr
    Private _hFontTitolo As IntPtr
    Private _coloreSfondo As Integer = &HFFFFFF
    Private _coloreTesto As Integer = &H0
    Private _coloreSelezione As Integer = &HFFCCAA
    Private _coloreFuoco As Integer = &HFF0000
    Private _coloreSfondoTitolo As Integer = &H303030
    Private _coloreTestoTitolo As Integer = &HFFFFFF
    Private _coloreCasella As Integer = &HF0F0F0
    Private _altezzaTitolo As Integer = 60
    Private _altezzaCasella As Integer = 120
    Private _margine As Integer = 12
    ' Prima riga mostrata, quando gli elementi non entrano tutti (senza pagine).
    Private _primaRiga As Integer = 0
    ' Dove sono stati disegnati elementi e voci della barra di stato, per l'esplorazione col dito.
    Private ReadOnly _rettangoliVoci As New Dictionary(Of Integer, Rectangle)()
    Private ReadOnly _rettangoliStato As New Dictionary(Of Integer, Rectangle)()
    Private ReadOnly _rettangoliDock As New Dictionary(Of Integer, Rectangle)()

    ' Altezza della fila del dock in fondo (0 senza dock).
    Private Function AltezzaDock() As Integer
        Return If(HaDock(), Math.Max(50, CInt(_altezzaTitolo * 1.4)), 0)
    End Function

    Private Sub CaricaAspetto()
        Try
            Dim dpi As Integer = 96
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            If hDC <> IntPtr.Zero Then
                dpi = GetDeviceCaps(hDC, LOGPIXELSY)
                ReleaseDC(IntPtr.Zero, hDC)
            End If
            Dim scala As Single = dpi / 96.0F
            Dim altezzaSchermo As Integer = GetSystemMetrics(SM_CYSCREEN)
            Dim dimensione As Single = 14 * scala
            If altezzaSchermo >= 2160 Then
                dimensione = 48 * scala
            ElseIf altezzaSchermo >= 1440 Then
                dimensione = 32 * scala
            ElseIf altezzaSchermo >= 1080 Then
                dimensione = 24 * scala
            ElseIf altezzaSchermo >= 768 Then
                dimensione = 18 * scala
            End If
            Dim carattere As String = "Segoe UI"
            Dim contrastoAlto As Boolean = False
            Dim config As New WGConfig()
            Dim valore As Single
            ' Sfondo e testo seguono il tema chiaro/scuro di Windows; una voce di configurazione o l'alto contrasto,
            ' controllati sotto, restano piu' forti.
            If WGTema.Scuro Then
                _coloreSfondo = &H0
                _coloreTesto = &HFFFFFF
            Else
                _coloreSfondo = &HFFFFFF
                _coloreTesto = &H0
            End If
            If config.EsisteId("FontDimensione") AndAlso Single.TryParse(config.LeggiValore("FontDimensione"), valore) Then dimensione = valore * scala
            If config.EsisteId("ContrastoAlto") Then Boolean.TryParse(config.LeggiValore("ContrastoAlto"), contrastoAlto)
            If config.EsisteId("MenuCarattere") AndAlso Not String.IsNullOrEmpty(config.LeggiValore("MenuCarattere")) Then carattere = config.LeggiValore("MenuCarattere")
            If config.EsisteId("ColoreSfondo") Then Integer.TryParse(config.LeggiValore("ColoreSfondo"), _coloreSfondo)
            If config.EsisteId("ColoreTesto") Then Integer.TryParse(config.LeggiValore("ColoreTesto"), _coloreTesto)
            If config.EsisteId("ColoreSelezione") Then Integer.TryParse(config.LeggiValore("ColoreSelezione"), _coloreSelezione)
            If config.EsisteId("ColoreFuoco") Then Integer.TryParse(config.LeggiValore("ColoreFuoco"), _coloreFuoco)
            If contrastoAlto Then
                _coloreSfondo = &H0
                _coloreTesto = &HFFFFFF
                _coloreSelezione = &HFF0000
                _coloreFuoco = &HFFFF00
                _coloreSfondoTitolo = &H0
                _coloreTestoTitolo = &HFFFFFF
            End If
            ' Le caselle si staccano appena dallo sfondo: un po' più scure su sfondo chiaro, più chiare su sfondo scuro.
            _coloreCasella = Sfuma(_coloreSfondo, If(Luminosita(_coloreSfondo) > 128, -18, 28))

            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hFontTitolo <> IntPtr.Zero Then DeleteObject(_hFontTitolo)
            Dim altezzaCarattere As Integer = CInt(-Math.Round(dimensione * dpi / 72.0F))
            Dim altezzaCarattereTitolo As Integer = CInt(-Math.Round(dimensione * 1.5F * dpi / 72.0F))
            _hFont = CreateFontW(altezzaCarattere, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, carattere)
            _hFontTitolo = CreateFontW(altezzaCarattereTitolo, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, carattere)
            _altezzaTitolo = Math.Max(40, CInt(dimensione * 1.5F * 2.5F))
            _altezzaCasella = Math.Max(80, CInt(dimensione * 5))
            _margine = Math.Max(8, CInt(dimensione / 2))
        Catch ex As Exception
            log.ScriviLog("WGGriglia.CaricaAspetto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Luminosita(colore As Integer) As Integer
        Return ((colore And &HFF) + ((colore >> 8) And &HFF) + ((colore >> 16) And &HFF)) \ 3
    End Function

    Private Shared Function Sfuma(colore As Integer, delta As Integer) As Integer
        Dim r As Integer = Math.Max(0, Math.Min(255, (colore And &HFF) + delta))
        Dim g As Integer = Math.Max(0, Math.Min(255, ((colore >> 8) And &HFF) + delta))
        Dim b As Integer = Math.Max(0, Math.Min(255, ((colore >> 16) And &HFF) + delta))
        Return r Or (g << 8) Or (b << 16)
    End Function

    Private Function AltezzaBarraStato() As Integer
        Return If(_vociStato.Count > 0, Math.Max(36, CInt(_altezzaTitolo * 0.8)), 0)
    End Function

    ' Spazio per i puntini delle pagine in fondo (solo con più pagine).
    Private Function AltezzaPuntini() As Integer
        Return If(VociPerPagina > 0 AndAlso PagineTotali() > 1, Math.Max(24, _margine * 3), 0)
    End Function

    ' Dove comincia la zona delle caselle: sotto il titolo e, se è in alto, sotto la barra di stato.
    Private Function InizioCaselle() As Integer
        Return If(BarraStatoInAlto, AltezzaBarraStato(), 0) + _altezzaTitolo + _margine
    End Function

    Private Function AltezzaZonaCaselle() As Integer
        Return Me.ClientSize.Height - InizioCaselle() - If(BarraStatoInAlto, 0, AltezzaBarraStato()) - AltezzaPuntini() - AltezzaDock()
    End Function

    Private Function RigheVisibili() As Integer
        Return Math.Max(1, AltezzaZonaCaselle() \ (_altezzaCasella + _margine))
    End Function

    ' Fa scorrere la griglia perché la riga dell'elemento selezionato sia visibile (senza pagine).
    Private Sub TieniVisibileSelezionato()
        If VociPerPagina > 0 Then
            _primaRiga = 0
            Return
        End If
        Dim riga As Integer = _indice \ Colonne()
        Dim visibili As Integer = RigheVisibili()
        If riga < _primaRiga Then _primaRiga = riga
        If riga >= _primaRiga + visibili Then _primaRiga = riga - visibili + 1
        _primaRiga = Math.Max(0, Math.Min(_primaRiga, Math.Max(0, Righe() - visibili)))
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Select Case m.Msg
            Case WM_PAINT
                Disegna()
                m.Result = IntPtr.Zero
                Return
            Case WM_ERASEBKGND
                m.Result = New IntPtr(1)
                Return
        End Select
        MyBase.WndProc(m)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        TieniVisibileSelezionato()
        Me.Invalidate()
    End Sub

    Private Sub Disegna()
        Dim ps As New PAINTSTRUCT()
        Dim hDC As IntPtr = BeginPaint(Me.Handle, ps)
        If hDC = IntPtr.Zero Then Return
        ' Da qui ogni uscita passa da EndPaint, altrimenti il DC resta agganciato.
        Try
            Dim larghezza As Integer = Me.ClientSize.Width
            Dim altezza As Integer = Me.ClientSize.Height
            If larghezza <= 0 OrElse altezza <= 0 Then Return
            If _hFont = IntPtr.Zero Then CaricaAspetto()

            If larghezza <> _memLarghezza OrElse altezza <> _memAltezza OrElse _hMemDC = IntPtr.Zero Then
                RilasciaMemoria()
                _hMemDC = CreateCompatibleDC(hDC)
                If _hMemDC = IntPtr.Zero Then Return
                _hMemBitmap = CreateCompatibleBitmap(hDC, larghezza, altezza)
                If _hMemBitmap = IntPtr.Zero Then
                    DeleteDC(_hMemDC)
                    _hMemDC = IntPtr.Zero
                    Return
                End If
                _hOldMemBitmap = SelectObject(_hMemDC, _hMemBitmap)
                _memLarghezza = larghezza
                _memAltezza = altezza
            End If

            Dim pennelloSfondo As IntPtr = CreateSolidBrush(_coloreSfondo)
            Dim pennelloTitolo As IntPtr = CreateSolidBrush(_coloreSfondoTitolo)
            Dim pennelloCasella As IntPtr = CreateSolidBrush(_coloreCasella)
            Dim pennelloSelezione As IntPtr = CreateSolidBrush(_coloreSelezione)
            Dim pennelloFuoco As IntPtr = CreateSolidBrush(_coloreFuoco)
            Dim fontPrecedente As IntPtr = IntPtr.Zero
            Try
                Dim tutto As New RECT With {.Right = larghezza, .Bottom = altezza}
                FillRect(_hMemDC, tutto, pennelloSfondo)
                SetBkMode(_hMemDC, TRASPARENTE)

                Dim altezzaBarra As Integer = AltezzaBarraStato()
                Dim inizioTitolo As Integer = If(BarraStatoInAlto, altezzaBarra, 0)
                Dim zonaTitolo As New RECT With {.Top = inizioTitolo, .Right = larghezza, .Bottom = inizioTitolo + _altezzaTitolo}
                FillRect(_hMemDC, zonaTitolo, pennelloTitolo)
                fontPrecedente = SelectObject(_hMemDC, _hFontTitolo)
                SetTextColor(_hMemDC, _coloreTestoTitolo)
                Dim titoloMostrato As String = _titolo & If(VociPerPagina > 0 AndAlso PagineTotali() > 1, "  ·  " & DescrizionePagina(_indice), "")
                Dim testoTitolo As New RECT With {.Left = 20, .Top = inizioTitolo, .Right = larghezza - 20, .Bottom = inizioTitolo + _altezzaTitolo}
                DrawText(_hMemDC, titoloMostrato, titoloMostrato.Length, testoTitolo, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)

                SelectObject(_hMemDC, _hFont)
                SetTextColor(_hMemDC, _coloreTesto)
                _rettangoliVoci.Clear()
                Dim numeroColonne As Integer = Colonne()
                Dim larghezzaCasella As Integer = Math.Max(40, (larghezza - _margine * (numeroColonne + 1)) \ numeroColonne)
                Dim altezzaCasella As Integer = _altezzaCasella
                Dim primo As Integer
                Dim ultimo As Integer
                If VociPerPagina > 0 Then
                    ' Le caselle della pagina riempiono lo spazio, come le icone di un tablet.
                    Dim righePagina As Integer = Math.Max(1, CInt(Math.Ceiling(VociPerPagina / CDbl(numeroColonne))))
                    altezzaCasella = Math.Max(40, (AltezzaZonaCaselle() - _margine * righePagina) \ righePagina)
                    primo = Pagina(_indice) * VociPerPagina
                    ultimo = Math.Min(_voci.Count - 1, primo + VociPerPagina - 1)
                Else
                    primo = _primaRiga * numeroColonne
                    ultimo = Math.Min(_voci.Count - 1, (_primaRiga + RigheVisibili() + 1) * numeroColonne - 1)
                End If
                Dim inizio As Integer = InizioCaselle()
                For i As Integer = primo To ultimo
                    Dim riga As Integer = (i - primo) \ numeroColonne
                    Dim colonna As Integer = (i - primo) Mod numeroColonne
                    Dim casella As New RECT()
                    casella.Left = _margine + colonna * (larghezzaCasella + _margine)
                    casella.Top = inizio + riga * (altezzaCasella + _margine)
                    casella.Right = casella.Left + larghezzaCasella
                    casella.Bottom = casella.Top + altezzaCasella
                    If casella.Top >= altezza Then Exit For
                    _rettangoliVoci(i) = Rectangle.FromLTRB(casella.Left, casella.Top, casella.Right, casella.Bottom)
                    Dim selezionato As Boolean = (i = _indice) AndAlso Not _inBarraStato AndAlso Not _inDock
                    FillRect(_hMemDC, casella, If(i = _indice AndAlso Not _inDock, pennelloSelezione, pennelloCasella))
                    If selezionato Then
                        ' Bordo del fuoco spesso, come un rettangolo doppio.
                        For spessore As Integer = 0 To 3
                            Dim bordo As New RECT With {.Left = casella.Left + spessore, .Top = casella.Top + spessore, .Right = casella.Right - spessore, .Bottom = casella.Bottom - spessore}
                            FrameRect(_hMemDC, bordo, pennelloFuoco)
                        Next
                    End If
                    ' Testo centrato anche in verticale: prima si misura, poi si disegna.
                    Dim testo As String = _voci(i)
                    Dim zonaTesto As New RECT With {.Left = casella.Left + 10, .Top = casella.Top + 6, .Right = casella.Right - 10, .Bottom = casella.Bottom - 6}
                    Dim misura As RECT = zonaTesto
                    DrawText(_hMemDC, testo, testo.Length, misura, DT_CENTER Or DT_WORDBREAK Or DT_NOPREFIX Or DT_CALCRECT)
                    Dim altezzaTesto As Integer = Math.Min(misura.Bottom - misura.Top, zonaTesto.Bottom - zonaTesto.Top)
                    zonaTesto.Top += Math.Max(0, ((zonaTesto.Bottom - zonaTesto.Top) - altezzaTesto) \ 2)
                    DrawText(_hMemDC, testo, testo.Length, zonaTesto, DT_CENTER Or DT_WORDBREAK Or DT_NOPREFIX Or DT_END_ELLIPSIS)
                Next

                ' Dock: la fila in fondo, uguale su tutte le pagine, su uno sfondo suo.
                _rettangoliDock.Clear()
                Dim altezzaFilaDock As Integer = AltezzaDock()
                If altezzaFilaDock > 0 Then
                    Dim fondoDock As Integer = altezza - If(BarraStatoInAlto, 0, altezzaBarra)
                    Dim fascia As New RECT With {.Top = fondoDock - altezzaFilaDock, .Right = larghezza, .Bottom = fondoDock}
                    FillRect(_hMemDC, fascia, pennelloTitolo)
                    Dim quanti As Integer = VociDock.Count
                    Dim larghezzaDock As Integer = Math.Min(larghezzaCasella, (larghezza - _margine * (quanti + 1)) \ quanti)
                    Dim inizioDock As Integer = (larghezza - (quanti * larghezzaDock + (quanti - 1) * _margine)) \ 2
                    For d As Integer = 0 To quanti - 1
                        Dim casellaDock As New RECT With {.Left = inizioDock + d * (larghezzaDock + _margine), .Top = fascia.Top + _margine \ 2, .Bottom = fascia.Bottom - _margine \ 2}
                        casellaDock.Right = casellaDock.Left + larghezzaDock
                        _rettangoliDock(d) = Rectangle.FromLTRB(casellaDock.Left, casellaDock.Top, casellaDock.Right, casellaDock.Bottom)
                        Dim sceltoDock As Boolean = _inDock AndAlso d = _indiceDock AndAlso Not _inBarraStato
                        FillRect(_hMemDC, casellaDock, If(_inDock AndAlso d = _indiceDock, pennelloSelezione, pennelloCasella))
                        If sceltoDock Then
                            For spessore As Integer = 0 To 3
                                Dim bordoDock As New RECT With {.Left = casellaDock.Left + spessore, .Top = casellaDock.Top + spessore, .Right = casellaDock.Right - spessore, .Bottom = casellaDock.Bottom - spessore}
                                FrameRect(_hMemDC, bordoDock, pennelloFuoco)
                            Next
                        End If
                        SetTextColor(_hMemDC, _coloreTesto)
                        Dim testoDock As String = VociDock(d)
                        DrawText(_hMemDC, testoDock, testoDock.Length, casellaDock, DT_CENTER Or DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)
                    Next
                End If

                ' Puntini delle pagine: pieno quello della pagina mostrata.
                Dim spazioPuntini As Integer = AltezzaPuntini()
                If spazioPuntini > 0 Then
                    Dim pagine As Integer = PagineTotali()
                    Dim lato As Integer = Math.Max(8, spazioPuntini \ 3)
                    Dim yPuntini As Integer = altezza - If(BarraStatoInAlto, 0, altezzaBarra) - altezzaFilaDock - spazioPuntini \ 2 - lato \ 2
                    Dim xInizio As Integer = (larghezza - (pagine * lato * 2 - lato)) \ 2
                    For p As Integer = 0 To pagine - 1
                        Dim puntino As New RECT With {.Left = xInizio + p * lato * 2, .Top = yPuntini, .Right = xInizio + p * lato * 2 + lato, .Bottom = yPuntini + lato}
                        If p = Pagina(_indice) Then FillRect(_hMemDC, puntino, pennelloFuoco) Else FrameRect(_hMemDC, puntino, pennelloFuoco)
                    Next
                End If

                ' Barra di stato, in alto o in fondo, divisa in parti uguali; la voce scelta è evidenziata quando ci si è dentro.
                _rettangoliStato.Clear()
                If altezzaBarra > 0 Then
                    Dim topBarra As Integer = If(BarraStatoInAlto, 0, altezza - altezzaBarra)
                    Dim barra As New RECT With {.Top = topBarra, .Right = larghezza, .Bottom = topBarra + altezzaBarra}
                    FillRect(_hMemDC, barra, pennelloTitolo)
                    Dim larghezzaVoce As Integer = larghezza \ _vociStato.Count
                    For v As Integer = 0 To _vociStato.Count - 1
                        Dim zonaVoce As New RECT With {.Left = v * larghezzaVoce, .Top = barra.Top, .Right = (v + 1) * larghezzaVoce, .Bottom = barra.Bottom}
                        _rettangoliStato(v) = Rectangle.FromLTRB(zonaVoce.Left, zonaVoce.Top, zonaVoce.Right, zonaVoce.Bottom)
                        Dim scelta As Boolean = _inBarraStato AndAlso v = _indiceStato
                        If scelta Then
                            FillRect(_hMemDC, zonaVoce, pennelloSelezione)
                            FrameRect(_hMemDC, zonaVoce, pennelloFuoco)
                        End If
                        Dim coloreScritta As Integer = If(scelta, _coloreTesto, _coloreTestoTitolo)
                        SetTextColor(_hMemDC, coloreScritta)
                        zonaVoce.Left += 8
                        zonaVoce.Right -= 8
                        Dim icona As IconaStato = IconaDi(v)
                        If icona IsNot Nothing Then zonaVoce.Left = DisegnaIcona(_hMemDC, icona, zonaVoce, coloreScritta) + 8
                        Dim testoVoce As String = TestoStato(v)
                        Try
                            If _vociStato(v).TestoBreve IsNot Nothing Then testoVoce = If(_vociStato(v).TestoBreve.Invoke(), testoVoce)
                        Catch exBreve As Exception
                            log.ScriviLog("WGGriglia.TestoBreve: " & exBreve.Message)
                        End Try
                        DrawText(_hMemDC, testoVoce, testoVoce.Length, zonaVoce, If(icona IsNot Nothing, 0UI, DT_CENTER) Or DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)
                    Next
                End If

                ' Il menu aperto dalla barra di stato (Wi-Fi, Bluetooth) sta sopra a tutto il resto.
                If MenuAperto Then DisegnaMenuAMano(pennelloCasella, pennelloTitolo, pennelloSelezione, pennelloFuoco)
            Finally
                If fontPrecedente <> IntPtr.Zero Then SelectObject(_hMemDC, fontPrecedente)
                DeleteObject(pennelloSfondo)
                DeleteObject(pennelloTitolo)
                DeleteObject(pennelloCasella)
                DeleteObject(pennelloSelezione)
                DeleteObject(pennelloFuoco)
            End Try

            BitBlt(hDC, 0, 0, larghezza, altezza, _hMemDC, 0, 0, SRCCOPY)
        Catch ex As Exception
            log.ScriviLog("WGGriglia.Disegna: " & ex.Message)
        Finally
            EndPaint(Me.Handle, ps)
        End Try
    End Sub

    Private Sub Riempi(hDC As IntPtr, r As RECT, colore As Integer)
        Dim pennello As IntPtr = CreateSolidBrush(colore)
        FillRect(hDC, r, pennello)
        DeleteObject(pennello)
    End Sub

    Private Sub Contorno(hDC As IntPtr, r As RECT, colore As Integer)
        Dim pennello As IntPtr = CreateSolidBrush(colore)
        FrameRect(hDC, r, pennello)
        Dim interno As New RECT With {.Left = r.Left + 1, .Top = r.Top + 1, .Right = r.Right - 1, .Bottom = r.Bottom - 1}
        FrameRect(hDC, interno, pennello)
        DeleteObject(pennello)
    End Sub

    ' Icone disegnate a mano, come sui telefoni: il Wi-Fi a quattro tacche crescenti (piene quelle del
    ' segnale) e la batteria col suo livello (rossa sotto il 20 per cento, verde in carica). Restituisce
    ' dove finisce l'icona, per scriverci accanto il testo.
    Private Function DisegnaIcona(hDC As IntPtr, icona As IconaStato, zona As RECT, colore As Integer) As Integer
        Dim altezza As Integer = zona.Bottom - zona.Top
        Dim lato As Integer = Math.Max(12, CInt(altezza * 0.55))
        Dim basso As Integer = zona.Top + (altezza + lato) \ 2
        Dim spento As Integer = Sfuma(_coloreSfondoTitolo, 70)
        Select Case icona.Tipo
            Case "wifi"
                Dim larghezzaTacca As Integer = Math.Max(3, lato \ 5)
                For t As Integer = 0 To 3
                    Dim alta As Integer = lato * (t + 1) \ 4
                    Dim tacca As New RECT With {.Left = zona.Left + t * (larghezzaTacca + 2), .Top = basso - alta, .Right = zona.Left + t * (larghezzaTacca + 2) + larghezzaTacca, .Bottom = basso}
                    Riempi(hDC, tacca, If(t < icona.Livello, colore, spento))
                Next
                Return zona.Left + 4 * (larghezzaTacca + 2)
            Case "batteria"
                Dim larghezzaCorpo As Integer = lato * 2
                Dim corpo As New RECT With {.Left = zona.Left, .Top = basso - lato, .Right = zona.Left + larghezzaCorpo, .Bottom = basso}
                Contorno(hDC, corpo, colore)
                Dim polo As New RECT With {.Left = corpo.Right, .Top = corpo.Top + lato \ 3, .Right = corpo.Right + Math.Max(3, lato \ 5), .Bottom = corpo.Bottom - lato \ 3}
                Riempi(hDC, polo, colore)
                Dim livello As Integer = Math.Max(0, Math.Min(100, icona.Livello))
                Dim pieno As New RECT With {.Left = corpo.Left + 3, .Top = corpo.Top + 3, .Bottom = corpo.Bottom - 3}
                pieno.Right = pieno.Left + CInt((corpo.Right - corpo.Left - 6) * livello / 100.0)
                Dim coloreLivello As Integer = If(icona.InCarica, &H50B000, If(livello < 20, &H2020E0, colore))
                If pieno.Right > pieno.Left Then Riempi(hDC, pieno, coloreLivello)
                Return polo.Right
        End Select
        Return zona.Left
    End Function

    Private Sub RilasciaMemoria()
        If _hMemDC <> IntPtr.Zero Then
            If _hOldMemBitmap <> IntPtr.Zero Then SelectObject(_hMemDC, _hOldMemBitmap)
            If _hMemBitmap <> IntPtr.Zero Then DeleteObject(_hMemBitmap)
            DeleteDC(_hMemDC)
        End If
        _hMemDC = IntPtr.Zero
        _hMemBitmap = IntPtr.Zero
        _hOldMemBitmap = IntPtr.Zero
        _memLarghezza = 0
        _memAltezza = 0
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RilasciaMemoria()
            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hFontTitolo <> IntPtr.Zero Then DeleteObject(_hFontTitolo)
            _hFont = IntPtr.Zero
            _hFontTitolo = IntPtr.Zero
        Catch ex As Exception
            log.ScriviLog("WGGriglia.Dispose: " & ex.Message)
        End Try
        MyBase.Dispose(disposing)
    End Sub

#End Region

End Class
