Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Windows.Forms
Imports wgcontrols

' ============================================================================================
' SchermoCitta - lo schermo di Porto Franco: una mappa vista dall'alto, disegnata a mano con GDI e centrata sul giocatore, con in alto
' le informazioni (strada, velocità, soldi, ricercato, ora e tempo, missione o lavoro, navigatore, radio). Fa girare il ciclo del gioco
' (una trentina di passi al secondo), legge i tasti tenuti premuti (accelerare e frenare, Shift per correre) e i comandi singoli, e legge
' a voce gli annunci del mondo. I tasti si possono cambiare dalle impostazioni; Ctrl più freccia sinistra o destra cambia corsia.
' ============================================================================================

Public NotInheritable Class SchermoCitta
    Inherits UserControl
    Implements IWGAnnuncioIngresso

    Public Event PausaRichiesta()
    Public Event TelefonoRichiesto()
    Public Event MetaRichiesta()
    Public Event LuogoRichiesto(l As Luogo)
    Public Event TastoCatturato(azione As String, k As Keys)
    ''' <summary>Si è premuto Alt: si apre il menu delle cose che si possono fare adesso.</summary>
    Public Event OpzioniRichieste()

    Private ReadOnly _mondo As Mondo
    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _timer As New Timer() With {.Interval = 33}
    Private ReadOnly _orologio As Stopwatch = Stopwatch.StartNew()
    Private _ultimo As Double
    Private _zoom As Double = 0.9
    Private ReadOnly _coda As New Queue(Of String)()
    ''' <summary>Gli ultimi annunci (letti o detti dalle voci del gioco), per rileggerli con il tasto apposta.</summary>
    Private ReadOnly _storia As New List(Of String)()
    Private _indiceStoria As Integer = -1
    Private _prossimaLettura As Double
    ''' <summary>Fermo (menu aperto): il mondo non avanza.</summary>
    Public InPausa As Boolean
    ''' <summary>Se non è vuota: il prossimo tasto premuto diventa il tasto di questa azione.</summary>
    Public CatturaPer As String = ""

    Private Const VK_SHIFT As Integer = &H10

    Public Sub New(mondo As Mondo)
        Try
            _mondo = mondo
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.Opaque, True)
            Me.DoubleBuffered = False
            Me.AccessibleName = mondo.Dati.Nome
            Me.AccessibleRole = AccessibleRole.None
            AddHandler _mondo.Annuncio, AddressOf Annuncio
            AddHandler _mondo.Sentito, AddressOf Ricorda
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.New", ex)
            Throw
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _tastiera = WGTastiera.GetInstance()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.MostraControllo(Me)
            _ultimo = _orologio.Elapsed.TotalSeconds
            _timer.Start()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Avvia", ex)
            Throw
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            _timer.Stop()
            _tastiera = Nothing
            RemoveHandler _mondo.Annuncio, AddressOf Annuncio
            RemoveHandler _mondo.Sentito, AddressOf Ricorda
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Chiudi", ex)
            Throw
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Return contenitore IsNot Nothing AndAlso contenitore.PuoRicevereInput(Me)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.PuoRicevere", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ la voce
    ''' <summary>Gli annunci urgenti si dicono subito (interrompendo); gli altri uno dopo l'altro.</summary>
    ''' <summary>Mette un testo tra gli annunci da rileggere (gli ultimi quaranta).</summary>
    Private Sub Ricorda(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            SyncLock _storia
                _storia.Add(testo)
                If _storia.Count > 40 Then _storia.RemoveAt(0)
                _indiceStoria = -1
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Ricorda", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Rilegge gli annunci andando indietro: la prima volta l'ultimo, poi quello prima, e così via.</summary>
    Private Sub Rileggi()
        Try
            Dim testo As String
            SyncLock _storia
                If _storia.Count = 0 Then
                    testo = "Non c'è ancora niente da rileggere."
                Else
                    If _indiceStoria < 0 Then
                        _indiceStoria = _storia.Count - 1
                    ElseIf _indiceStoria > 0 Then
                        _indiceStoria -= 1
                    Else
                        _indiceStoria = _storia.Count - 1
                    End If
                    testo = _storia(_indiceStoria)
                End If
            End SyncLock
            _coda.Clear()
            sintesivocale.GetInstance().LeggiTesto(testo)
            _prossimaLettura = _orologio.Elapsed.TotalSeconds + 0.6
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Rileggi", ex)
            Throw
        End Try
    End Sub

    Private Sub Annuncio(testo As String, urgente As Boolean)
        Try
            Ricorda(testo)
            If urgente Then
                _coda.Clear()
                Leggi(testo)
            Else
                _coda.Enqueue(testo)
                While _coda.Count > 4
                    _coda.Dequeue()
                End While
            End If
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Annuncio", ex)
            Throw
        End Try
    End Sub

    Public Sub Leggi(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            sintesivocale.GetInstance().LeggiTesto(testo)
            _prossimaLettura = _orologio.Elapsed.TotalSeconds + 0.6
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Leggi", ex)
            Throw
        End Try
    End Sub

    Private Sub ProssimoAnnuncio()
        Try
            If _coda.Count = 0 OrElse _orologio.Elapsed.TotalSeconds < _prossimaLettura Then Return
            If sintesivocale.GetInstance().StaParlando Then Return
            Leggi(_coda.Dequeue())
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.ProssimoAnnuncio", ex)
            Throw
        End Try
    End Sub

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        Try
            Leggi(_mondo.DoveSei())
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.AnnunciaIngresso", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il ciclo
    Private Sub _timer_Tick(sender As Object, e As EventArgs) Handles _timer.Tick
        Try
            Dim ora As Double = _orologio.Elapsed.TotalSeconds
            Dim dt As Double = ora - _ultimo
            _ultimo = ora
            If Not InPausa Then
                Dim c As New Comandi()
                ' i tasti tenuti premuti li dice WGTastiera: il suo hook blocca i tasti e GetAsyncKeyState non li vedrebbe
                If _tastiera IsNot Nothing AndAlso PuoRicevere() AndAlso CatturaPer = "" Then
                    c.Su = _tastiera.TastoTenuto(CInt(_mondo.Imp.Tasti("accelera")))
                    c.Giu = _tastiera.TastoTenuto(CInt(_mondo.Imp.Tasti("frena")))
                    c.Corsa = _tastiera.TastoTenuto(CInt(Keys.LShiftKey)) OrElse _tastiera.TastoTenuto(CInt(Keys.RShiftKey))
                End If
                _mondo.Passo(dt, c)
                Me.Invalidate()
            End If
            ProssimoAnnuncio()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta._timer_Tick", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------ i tasti
    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If InPausa OrElse CatturaPer <> "" OrElse Not PuoRicevere() Then Return
            RaiseEvent OpzioniRichieste()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta._tastiera_AltPremuto", ex)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse InPausa OrElse Not PuoRicevere() Then Return
            Dim codice As Keys = tasto And Keys.KeyCode
            Dim ctrl As Boolean = (_tastiera.ModificatoriUltimoTasto And Keys.Control) = Keys.Control
            If CatturaPer <> "" Then
                If codice = Keys.ShiftKey OrElse codice = Keys.ControlKey OrElse codice = Keys.Menu Then Return
                Dim azione As String = CatturaPer
                CatturaPer = ""
                tasto = Keys.None
                RaiseEvent TastoCatturato(azione, codice)
                Return
            End If
            If ctrl Then
                ' Ctrl più freccia: la corsia (non si cambia dalle impostazioni)
                If codice = Keys.Left Then
                    _mondo.CambiaCorsia(1)
                    tasto = Keys.None
                ElseIf codice = Keys.Right Then
                    _mondo.CambiaCorsia(0)
                    tasto = Keys.None
                End If
                Return
            End If
            Dim azione2 As String = _mondo.Imp.AzioneDi(codice)
            Dim gestito As Boolean = True
            ' in barca: le frecce cambiano corsia e restano solo i tasti che servono
            If _mondo.Barca IsNot Nothing Then
                Select Case azione2
                    Case "sinistra" : _mondo.Barca.CambiaCorsia(-1)
                    Case "destra" : _mondo.Barca.CambiaCorsia(1)
                    Case "missione", "dove", "veicolo" : Leggi(_mondo.Barca.Descrizione())
                    Case "rileggi" : Rileggi()
                    Case "pausa" : RaiseEvent PausaRichiesta()
                    Case "comandi" : Leggi("In barca: freccia su per dare gas, freccia giù per rallentare, frecce a sinistra e a destra per cambiare corsia. M dice dove sei e dov'è chi insegui.")
                End Select
                tasto = Keys.None
                Return
            End If
            gestito = Esegui(azione2, codice)
            If gestito Then tasto = Keys.None
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta._tastiera_KeyDown", ex)
        End Try
    End Sub

    ''' <summary>Fa un'azione del gioco (quelle dei tasti e quelle del menu Alt). Falso se l'azione non è una di queste.</summary>
    Public Function Esegui(azione As String, Optional codice As Keys = Keys.None) As Boolean
        Try
            Dim eseguito As Boolean = True
            Select Case azione
                Case "radar"
                    _mondo.Radar()
                Case "rileggi"
                    Rileggi()
                Case "frenoamano"
                    _mondo.FrenoAMano()
                Case "garage"
                    _mondo.MettiInGarage()
                Case "banda"
                    Dim b As String = _mondo.Vita.DescriviBanda()
                    Leggi(If(b = "", "Qui non comanda nessuna banda.", b))
                Case "accelera"
                    ' si legge tenuto premuto dal ciclo
                Case "frena"
                    If _mondo.APiedi Then _mondo.ScegliSvolta(2)
                Case "sinistra"
                    _mondo.ScegliSvolta(-1)
                Case "destra"
                    _mondo.ScegliSvolta(1)
                Case "sali"
                    _mondo.SaliScendi()
                Case "entra"
                    Dim l As Luogo = _mondo.LuogoVicino()
                    If l IsNot Nothing AndAlso Math.Abs(_mondo.Velocita) < 1 Then
                        RaiseEvent LuogoRichiesto(l)
                    ElseIf Not _mondo.APiedi Then
                        _mondo.Suona(MotoreAudio.Clacson(), 0.7F, 0)
                    Else
                        Leggi("Qui non c'è niente in cui entrare.")
                    End If
                Case "dove"
                    Leggi(_mondo.DoveSei())
                Case "veicolo"
                    If _mondo.APiedi Then
                        Leggi("Sei a piedi.")
                    Else
                        Dim v As Veicolo = _mondo.Veicolo
                        Leggi(v.Tipo.Nome & If(v.Tuo, If(v.Tipo.Maschile, ", il tuo", ", la tua"), "") & ", a " & CInt(Math.Abs(_mondo.Velocita) * 3.6).ToString() & " all'ora, limite " & _mondo.Pos.Tratto.Limite.ToString() & ". " & _mondo.SaluteVeicolo() & " Benzina: " & CInt(v.Benzina).ToString() & " su cento. " & _mondo.Economia.DescriviCarico(v))
                    End If
                Case "stato"
                    Leggi(_mondo.Partita.Soldi.ToString() & " euro in tasca" & If(_mondo.Partita.Banca > 0, " e " & _mondo.Partita.Banca.ToString() & " in banca", "") & ". Ricercato: " & _mondo.TestoStelle() & ". Capitolo " & _mondo.Missioni.CapitoloAttuale().ToString() & ", missioni fatte: " & _mondo.Partita.Completate.Count.ToString() & " su " & _mondo.Dati.Missioni.Count.ToString() & ".")
                Case "missione"
                    Dim l As String = _mondo.Lavori.Descrizione()
                    If l = "" Then l = _mondo.Economia.Descrizione()
                    Leggi(If(l <> "", l, _mondo.Missioni.Obiettivo()))
                Case "navigatore"
                    Leggi(_mondo.Indicazione())
                Case "meta"
                    RaiseEvent MetaRichiesta()
                Case "spegni"
                    If _mondo.Meta Is Nothing Then
                        Leggi("Il navigatore è già spento.")
                    Else
                        _mondo.ImpostaMeta(Nothing, True)
                        Leggi("Navigatore spento.")
                    End If
                Case "telefono"
                    RaiseEvent TelefonoRichiesto()
                Case "luoghi"
                    Leggi(_mondo.LuoghiVicini())
                Case "intorno"
                    Leggi(_mondo.Intorno())
                Case "radio"
                    _mondo.Radio.Cambia()
                Case "lavoro"
                    _mondo.Lavori.CominciaOFinisci()
                Case "ora"
                    Leggi(_mondo.OraETempo())
                Case "comandi"
                    Leggi(TestoComandi())
                Case "pausa"
                    RaiseEvent PausaRichiesta()
                Case Else
                    If codice = Keys.Oemplus OrElse codice = Keys.Add Then
                        _zoom = Math.Min(4, _zoom * 1.4)
                    ElseIf codice = Keys.OemMinus OrElse codice = Keys.Subtract Then
                        _zoom = Math.Max(0.15, _zoom / 1.4)
                    Else
                        eseguito = False
                    End If
            End Select
            Return eseguito
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Esegui", ex)
            Throw
        End Try
    End Function

    ''' <summary>L'elenco dei comandi con i tasti scelti.</summary>
    Private Function TestoComandi() As String
        Try
            Dim pezzi As New List(Of String)()
            For Each a In ImpostazioniCitta.Azioni
                pezzi.Add(ImpostazioniCitta.NomeTasto(_mondo.Imp.Tasti(a.Id)) & ": " & a.Nome.ToLowerInvariant())
            Next
            pezzi.Add("Ctrl più freccia sinistra: corsia opposta, per sorpassare; Ctrl più freccia destra: la tua corsia")
            pezzi.Add("Shift con la freccia per accelerare: correre, a piedi")
            Return String.Join(". ", pezzi) & "."
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.TestoComandi", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ il disegno
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            Dim w As Integer = Math.Max(1, Me.ClientSize.Width)
            Dim h As Integer = Math.Max(1, Me.ClientSize.Height)
            Dim hdcSchermo As IntPtr = e.Graphics.GetHdc()
            Dim memoria As IntPtr = Gdi.CreateCompatibleDC(hdcSchermo)
            Dim bitmap As IntPtr = Gdi.CreateCompatibleBitmap(hdcSchermo, w, h)
            Dim vecchia As IntPtr = Gdi.SelectObject(memoria, bitmap)
            Dim font As IntPtr = Gdi.CreateFontW(-Math.Max(14, h \ 40), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI")
            Dim vecchioFont As IntPtr = Gdi.SelectObject(memoria, font)
            Try
                Disegna(memoria, w, h)
                Gdi.BitBlt(hdcSchermo, 0, 0, w, h, memoria, 0, 0, Gdi.SRCCOPY)
            Finally
                Gdi.SelectObject(memoria, vecchioFont)
                Gdi.DeleteObject(font)
                Gdi.SelectObject(memoria, vecchia)
                Gdi.DeleteObject(bitmap)
                Gdi.DeleteDC(memoria)
                e.Graphics.ReleaseHdc(hdcSchermo)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.OnPaint", ex)
            ' un disegno sbagliato non deve fermare il gioco: si ridisegna al prossimo giro
        End Try
    End Sub

    Private Sub Disegna(dc As IntPtr, w As Integer, h As Integer)
        Try
            Dim m As Mondo = _mondo
            Dim s As Double = _zoom
            Dim cx As Integer = w \ 2
            Dim cy As Integer = h \ 2 + h \ 10
            Dim px As Double = m.X
            Dim py As Double = m.Y
            Dim sx As Func(Of Double, Integer) = Function(x) CInt(cx + (x - px) * s)
            Dim sy As Func(Of Double, Integer) = Function(y) CInt(cy - (y - py) * s)
            ' di notte i colori si scuriscono
            Dim scuro As Double = If(m.Notte, 0.55, 1.0)
            Dim C As Func(Of Integer, Integer, Integer, Integer) = Function(rr, gg, bb) Gdi.Rgb(CInt(rr * scuro), CInt(gg * scuro), CInt(bb * scuro))
            Gdi.Riempi(dc, New Gdi.RECT(0, 0, w, h), C(46, 64, 46))
            ' i quartieri, appena colorati
            Dim tinte As Integer()() = {New Integer() {60, 70, 60}, New Integer() {66, 64, 58}, New Integer() {58, 66, 72}, New Integer() {70, 62, 62}, New Integer() {56, 72, 56}, New Integer() {64, 64, 74}, New Integer() {72, 68, 56}, New Integer() {60, 60, 66}}
            For i As Integer = 0 To m.Dati.Quartieri.Count - 1
                Dim q As Quartiere = m.Dati.Quartieri(i)
                Dim tt As Integer() = tinte(i Mod tinte.Length)
                Gdi.Riempi(dc, New Gdi.RECT(sx(q.X0 - 50), sy(q.Y1 + 50), sx(q.X1 + 50), sy(q.Y0 - 50)), C(tt(0), tt(1), tt(2)))
            Next
            ' il mare a sud e il canale
            Dim riva As Integer = sy(-25)
            If riva < h Then Gdi.Riempi(dc, New Gdi.RECT(0, Math.Max(0, riva), w, h), C(30, 70, 130))
            For Each xc As Double In m.Dati.Canali
                Dim larg As Integer = CInt(Math.Max(3, 18 * s))
                Gdi.Riempi(dc, New Gdi.RECT(sx(xc) - larg \ 2, 0, sx(xc) + larg \ 2, h), C(40, 90, 150))
            Next
            If m.Dati.Canale > 0 Then
                Dim lc As Integer = CInt(Math.Max(3, 18 * s))
                Gdi.Riempi(dc, New Gdi.RECT(sx(m.Dati.Canale) - lc \ 2, 0, sx(m.Dati.Canale) + lc \ 2, h), C(40, 90, 150))
            End If
            ' le strade: arterie più chiare, galleria scura, ponti color pietra
            Dim larghezza As Integer = CInt(Math.Max(2, 9 * s))
            For Each t As Tratto In m.Dati.Tratti
                Dim colore As Integer = If(t.Galleria, C(50, 50, 50), If(t.Ponte, C(170, 150, 120), If(t.Strada.Arteria, C(150, 150, 140), C(110, 110, 108))))
                Gdi.Linea(dc, sx(t.A.X), sy(t.A.Y), sx(t.B.X), sy(t.B.Y), colore, If(t.Strada.Arteria, larghezza + 3, larghezza))
            Next
            ' i semafori
            For Each i As Incrocio In m.Dati.Incroci.Values
                If Not i.Semaforo Then Continue For
                Dim ix As Integer = sx(i.X), iy As Integer = sy(i.Y)
                If ix < -10 OrElse iy < -10 OrElse ix > w + 10 OrElse iy > h + 10 Then Continue For
                Dim verdeEO As Boolean = m.Verde(i, 0)
                Gdi.Cerchio(dc, ix, iy, CInt(Math.Max(2, 3 * s)), If(verdeEO, Gdi.Rgb(40, 220, 60), Gdi.Rgb(230, 40, 40)), Gdi.Rgb(0, 0, 0))
            Next
            ' la meta
            Dim meta As Luogo = If(m.Meta, If(m.Lavori.MetaAttuale, If(m.Economia.MetaAttuale, m.Missioni.LuogoDaRaggiungere)))
            ' le rotonde
            For Each i As Incrocio In m.Dati.Incroci.Values
                If Not i.Rotonda Then Continue For
                Dim ix As Integer = sx(i.X), iy As Integer = sy(i.Y)
                If ix < -20 OrElse iy < -20 OrElse ix > w + 20 OrElse iy > h + 20 Then Continue For
                Gdi.Cerchio(dc, ix, iy, CInt(Math.Max(4, 9 * s)), C(150, 150, 140), C(60, 110, 60))
            Next
            ' le guardie e gli oggetti da raccogliere della missione
            For Each g As Posizione In m.Missioni.Guardie.ToList()
                Gdi.Cerchio(dc, sx(g.X), sy(g.Y), CInt(Math.Max(3, 4 * s)), Gdi.Rgb(255, 60, 60), Gdi.Rgb(0, 0, 0))
            Next
            For Each o As Luogo In m.Missioni.Oggetti.ToList()
                Gdi.Cerchio(dc, sx(o.X), sy(o.Y), CInt(Math.Max(3, 4 * s)), Gdi.Rgb(255, 215, 0), Gdi.Rgb(0, 0, 0))
            Next
            If meta IsNot Nothing Then Gdi.Cerchio(dc, sx(meta.X), sy(meta.Y), CInt(Math.Max(7, 14 * s)), Gdi.Rgb(220, 40, 40), Gdi.Rgb(255, 255, 255))
            ' i luoghi
            Dim r As Integer = CInt(Math.Max(3, 5 * s))
            For Each l As Luogo In m.Dati.Luoghi.Values
                Dim lx As Integer = sx(l.X)
                Dim ly As Integer = sy(l.Y)
                If lx < -20 OrElse ly < -20 OrElse lx > w + 20 OrElse ly > h + 20 Then Continue For
                Dim bordo As Integer = If(m.Partita.Proprieta.Contains(l.Id), Gdi.Rgb(255, 215, 0), Gdi.Rgb(20, 20, 20))
                Gdi.Rettangolo(dc, lx - r, ly - r, lx + r, ly + r, ColoreLuogo(l.Tipo), bordo)
                If s >= 1.3 Then Gdi.Scrivi(dc, l.Nome, New Gdi.RECT(lx + r + 3, ly - 9, lx + 260, ly + 12), Gdi.Rgb(230, 230, 220), Gdi.DT_SINGLELINE)
            Next
            ' i veicoli
            Dim rv As Integer = CInt(Math.Max(3, 3.5 * s))
            For Each v As Veicolo In m.Veicoli
                If v Is m.Veicolo Then Continue For
                Dim vx As Integer = sx(v.X)
                Dim vy As Integer = sy(v.Y)
                If vx < -20 OrElse vy < -20 OrElse vx > w + 20 OrElse vy > h + 20 Then Continue For
                Dim colore As Integer
                Select Case v.Ruolo
                    Case RuoloVeicolo.Polizia : colore = If(CInt(m.Tempo * 4) Mod 2 = 0, Gdi.Rgb(40, 80, 255), Gdi.Rgb(255, 40, 40))
                    Case RuoloVeicolo.Parcheggiato : colore = If(v.Blocco, Gdi.Rgb(40, 80, 255), If(v.Tuo, Gdi.Rgb(90, 200, 90), Gdi.Rgb(150, 150, 160)))
                    Case RuoloVeicolo.Bersaglio : colore = Gdi.Rgb(255, 140, 0)
                    Case RuoloVeicolo.Banda : colore = Gdi.Rgb(200, 30, 200)
                    Case RuoloVeicolo.Relitto : colore = Gdi.Rgb(40, 40, 40)
                    Case Else : colore = C(235, 235, 235)
                End Select
                Gdi.Rettangolo(dc, vx - rv, vy - rv, vx + rv, vy + rv, colore, Gdi.Rgb(0, 0, 0))
            Next
            ' il giocatore: un triangolo che punta dove va (spostato a sinistra nella corsia opposta)
            Dim a As Double = m.Pos.Angolo
            Dim spost As Double = If(m.Corsia = 1, 4 * s, 0)
            Dim gx As Double = sx(px) + Math.Cos(a + Math.PI / 2) * spost
            Dim gy As Double = sy(py) - Math.Sin(a + Math.PI / 2) * spost
            Dim lato As Double = Math.Max(9, 9 * s)
            Dim punte(2) As Gdi.PUNTO
            For i As Integer = 0 To 2
                Dim ang As Double = a + {0, 2.5, -2.5}(i)
                Dim lung As Double = If(i = 0, lato * 1.4, lato)
                punte(i).X = CInt(gx + Math.Cos(ang) * lung)
                punte(i).Y = CInt(gy - Math.Sin(ang) * lung)
            Next
            Gdi.Poligono(dc, punte, If(m.APiedi, Gdi.Rgb(255, 255, 255), Gdi.Rgb(255, 220, 0)), Gdi.Rgb(0, 0, 0))
            ' le informazioni in alto
            Dim righe As New List(Of String)()
            righe.Add(m.Dati.Nome & " — " & m.Pos.Tratto.Strada.Nome & ", " & m.Dati.QuartiereDi(px, py).Nome & ", verso " & Mappa.Cardinale(m.Pos.Angolo) & "   " & m.OraETempo())
            righe.Add(If(m.APiedi, "A piedi", m.Veicolo.Tipo.Nome & ": " & CInt(Math.Abs(m.Velocita) * 3.6).ToString() & " km/h (limite " & m.Pos.Tratto.Limite.ToString() & ")" & If(m.Corsia = 1, ", corsia opposta", "") & ", benzina " & CInt(m.Veicolo.Benzina).ToString() & "%") &
                      "   Soldi: " & m.Partita.Soldi.ToString() & " €   Ricercato: " & If(m.Stelle = 0, "nessuno", New String("*"c, m.Stelle)) &
                      If(m.Radio.Descrizione() <> "", "   Radio: " & m.Radio.Descrizione(), ""))
            Dim lavoro As String = m.Lavori.Descrizione()
            If lavoro = "" Then lavoro = m.Economia.Descrizione()
            If lavoro = "" AndAlso m.Barca IsNot Nothing Then lavoro = m.Barca.Descrizione()
            righe.Add(If(lavoro <> "", lavoro, "Capitolo " & m.Missioni.CapitoloAttuale().ToString() & ". " & m.Missioni.Obiettivo()))
            If m.Meta IsNot Nothing Then righe.Add("Navigatore: " & m.Indicazione())
            Dim altezzaRiga As Integer = Math.Max(20, h \ 32)
            Gdi.Riempi(dc, New Gdi.RECT(0, 0, w, altezzaRiga * righe.Count + 12), Gdi.Rgb(15, 15, 20))
            For i As Integer = 0 To righe.Count - 1
                Gdi.Scrivi(dc, righe(i), New Gdi.RECT(10, 6 + i * altezzaRiga, w - 10, 6 + (i + 1) * altezzaRiga), Gdi.Rgb(240, 240, 230), Gdi.DT_SINGLELINE Or Gdi.DT_END_ELLIPSIS)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.Disegna", ex)
            Throw
        End Try
    End Sub

    Private Shared Function ColoreLuogo(tipo As String) As Integer
        Try
            Select Case tipo
                Case "casa", "rifugio" : Return Gdi.Rgb(90, 220, 90)
                Case "officina", "distributore" : Return Gdi.Rgb(240, 160, 40)
                Case "ospedale" : Return Gdi.Rgb(250, 250, 250)
                Case "commissariato" : Return Gdi.Rgb(40, 90, 240)
                Case "banca" : Return Gdi.Rgb(230, 200, 60)
                Case "concessionario", "garage" : Return Gdi.Rgb(60, 200, 200)
                Case "bar", "ristorante", "club", "pizzeria" : Return Gdi.Rgb(200, 90, 200)
                Case "stazione" : Return Gdi.Rgb(240, 240, 120)
                Case Else : Return Gdi.Rgb(170, 170, 150)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("SchermoCitta.ColoreLuogo", ex)
            Throw
        End Try
    End Function
End Class
