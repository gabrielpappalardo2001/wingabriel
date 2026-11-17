Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports wgcontrols

' Gestione del lettore multimediale tramite interfaccia touch e gesti. Disegna pulsanti, cursori e controlla gli stati di primo
' piano della finestra.
Public Class WGToccoPlayer
    Inherits Control

    Private Const PULSANTE_POSIZIONE As Integer = 0
    Private Const PULSANTE_PRECEDENTE As Integer = 1
    Private Const PULSANTE_PAUSA As Integer = 2
    Private Const PULSANTE_SUCCESSIVA As Integer = 3
    Private Const PULSANTE_VOLUME As Integer = 4
    Private Const PULSANTE_CODA As Integer = 5
    Private Const PULSANTE_ESCI As Integer = 6
    Private Const NUMERO_PULSANTI As Integer = 7

    Private Const ALTEZZA_MINIMA As Integer = 150
    Private Const ALTEZZA_MASSIMA As Integer = 320
    Private Const QUOTA_ALTEZZA As Double = 0.24
    Private Const MARGINE As Integer = 8
    Private Const SPAZIO As Integer = 6
    Private Const RAGGIO As Integer = 14

    Private ReadOnly log As New Logger()
    Private ReadOnly _proprietario As WGPlayerFormInt

    Private _touk As WGTouk
    Private _velo As WGVeloTocco
    Private _iscritto As Boolean = False
    Private _terminato As Boolean = False
    Private _indiceFuoco As Integer = PULSANTE_PAUSA
    Private _firmaStato As String = ""
    Private _tmrAggiorna As System.Windows.Forms.Timer
    Private _fontEtichetta As Font
    Private _fontValore As Font
    Private _celle As Rectangle()

    Public Sub New(proprietario As WGPlayerFormInt)
        Try
            _proprietario = proprietario

            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.Opaque, True)
            Me.SetStyle(ControlStyles.Selectable, False)
            Me.TabStop = False
            Me.BackColor = WGTema.Sfondo
            Me.ForeColor = WGTema.Testo
            Me.Visible = False
            Me.Dock = DockStyle.Bottom
            Me.Height = ALTEZZA_MINIMA

            _fontEtichetta = New Font("Segoe UI", 15.0F, FontStyle.Bold, GraphicsUnit.Point)
            _fontValore = New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Point)
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.New: " & ex.ToString())
        End Try
    End Sub

    Private Sub OnCambiatoTema(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(Sub() OnCambiatoTema(sender, e)))
                Return
            End If
            Me.BackColor = WGTema.Sfondo
            Me.ForeColor = WGTema.Testo
            Me.Invalidate(True)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.OnCambiatoTema: " & ex.ToString())
        End Try
    End Sub

    Public Sub Avvia()
        Try
            If _terminato Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _proprietario Is Nothing Then Return

            _touk = WGTouk.GetInstance()
            If _touk Is Nothing Then Return

            IscriviStato()
            AvviaTimer()
            VerificaStatoTouch()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Avvia: " & ex.ToString())
        End Try
    End Sub

    Public Sub Termina()
        Try
            _terminato = True
            DisiscriviGesti()
            DisiscriviStato()
            FermaTimer()
            DistruggiVelo()
            Me.Visible = False
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Termina: " & ex.ToString())
        End Try
    End Sub

    Public Sub Nascondi()
        Try
            NascondiTutto()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Nascondi: " & ex.ToString())
        End Try
    End Sub

    Public Function EFinestraDelVelo(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            If _velo Is Nothing OrElse _velo.IsDisposed Then Return False
            If Not _velo.IsHandleCreated Then Return False
            Return _velo.Handle = finestra
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.EFinestraDelVelo: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub IscriviStato()
        Try
            If _touk Is Nothing Then Return
            RemoveHandler _touk.StatoCambiato, AddressOf GestisciStatoTouchCambiato
            AddHandler _touk.StatoCambiato, AddressOf GestisciStatoTouchCambiato
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.IscriviStato: " & ex.ToString())
        End Try
    End Sub

    Private Sub DisiscriviStato()
        Try
            If _touk Is Nothing Then Return
            RemoveHandler _touk.StatoCambiato, AddressOf GestisciStatoTouchCambiato
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.DisiscriviStato: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciStatoTouchCambiato()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf VerificaStatoTouch))
                Return
            End If
            VerificaStatoTouch()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciStatoTouchCambiato: " & ex.ToString())
        End Try
    End Sub

    Private Sub VerificaStatoTouch()
        Try
            If _terminato Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _touk Is Nothing Then Return

            If _touk.TouchAttivo Then
                IscriviGesti()
            Else
                DisiscriviGesti()
                NascondiTutto()
            End If
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.VerificaStatoTouch: " & ex.ToString())
        End Try
    End Sub

    Private Sub IscriviGesti()
        Try
            If _iscritto Then Return
            If _touk Is Nothing Then Return

            AddHandler _touk.FlickAvanti, AddressOf GestisciFlickAvanti
            AddHandler _touk.FlickIndietro, AddressOf GestisciFlickIndietro
            AddHandler _touk.FlickSu, AddressOf GestisciFlickSu
            AddHandler _touk.FlickGiu, AddressOf GestisciFlickGiu
            AddHandler _touk.TapSingolo, AddressOf GestisciTapSingolo
            AddHandler _touk.TapDoppio, AddressOf GestisciTapDoppio
            AddHandler _touk.EscapeGesto, AddressOf GestisciEscapeGesto
            AddHandler _touk.RichiestaLetturaCompleta, AddressOf GestisciLetturaCompleta
            AddHandler _touk.EsplorazioneTocco, AddressOf GestisciEsplorazione

            _iscritto = True
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.IscriviGesti: " & ex.ToString())
        End Try
    End Sub

    Private Sub DisiscriviGesti()
        Try
            If Not _iscritto Then Return
            If _touk Is Nothing Then
                _iscritto = False
                Return
            End If

            RemoveHandler _touk.FlickAvanti, AddressOf GestisciFlickAvanti
            RemoveHandler _touk.FlickIndietro, AddressOf GestisciFlickIndietro
            RemoveHandler _touk.FlickSu, AddressOf GestisciFlickSu
            RemoveHandler _touk.FlickGiu, AddressOf GestisciFlickGiu
            RemoveHandler _touk.TapSingolo, AddressOf GestisciTapSingolo
            RemoveHandler _touk.TapDoppio, AddressOf GestisciTapDoppio
            RemoveHandler _touk.EscapeGesto, AddressOf GestisciEscapeGesto
            RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf GestisciLetturaCompleta
            RemoveHandler _touk.EsplorazioneTocco, AddressOf GestisciEsplorazione

            _iscritto = False
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.DisiscriviGesti: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaTimer()
        Try
            If _tmrAggiorna Is Nothing Then
                _tmrAggiorna = New System.Windows.Forms.Timer()
                _tmrAggiorna.Interval = 250
                AddHandler _tmrAggiorna.Tick, AddressOf AggiornaTick
            End If
            _tmrAggiorna.Stop()
            _tmrAggiorna.Start()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.AvviaTimer: " & ex.ToString())
        End Try
    End Sub

    Private Sub FermaTimer()
        Try
            If _tmrAggiorna IsNot Nothing Then
                _tmrAggiorna.Stop()
                RemoveHandler _tmrAggiorna.Tick, AddressOf AggiornaTick
                _tmrAggiorna.Dispose()
                _tmrAggiorna = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.FermaTimer: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaTick(sender As Object, e As EventArgs)
        Try
            If _terminato Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Not Consentito() Then
                NascondiTutto()
                Return
            End If

            AggiornaAltezza()

            If Not Me.Visible Then
                Me.Visible = True
                Me.BringToFront()
                _firmaStato = FirmaStatoCorrente()
                Me.Invalidate()
            Else
                MantieniInPrimoPiano()

                Dim firma As String = FirmaStatoCorrente()
                If firma <> _firmaStato Then
                    _firmaStato = firma
                    Me.Invalidate()
                End If
            End If

            AggiornaVelo()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.AggiornaTick: " & ex.ToString())
        End Try
    End Sub

    Private Sub NascondiTutto()
        Try
            If Me.Visible Then Me.Visible = False
            If _velo IsNot Nothing AndAlso Not _velo.IsDisposed AndAlso _velo.Visible Then _velo.Hide()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.NascondiTutto: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaAltezza()
        Try
            If Me.Parent Is Nothing Then Return

            Dim altezza As Integer = CInt(Me.Parent.ClientSize.Height * QUOTA_ALTEZZA)
            If altezza < ALTEZZA_MINIMA Then altezza = ALTEZZA_MINIMA
            If altezza > ALTEZZA_MASSIMA Then altezza = ALTEZZA_MASSIMA
            If altezza > Me.Parent.ClientSize.Height Then altezza = Me.Parent.ClientSize.Height

            If Me.Height <> altezza Then Me.Height = altezza
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.AggiornaAltezza: " & ex.ToString())
        End Try
    End Sub

    Private Sub MantieniInPrimoPiano()
        Try
            If Me.Parent Is Nothing Then Return
            If Me.Parent.Controls.GetChildIndex(Me) = 0 Then Return
            Me.BringToFront()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.MantieniInPrimoPiano: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaVelo()
        Try
            If _proprietario Is Nothing OrElse _proprietario.IsDisposed Then Return
            If Not _proprietario.IsHandleCreated Then Return

            Dim forma As Form = _proprietario.FindForm()
            If forma Is Nothing OrElse forma.IsDisposed OrElse Not forma.IsHandleCreated Then Return

            Dim area As Rectangle = forma.RectangleToScreen(forma.ClientRectangle)
            If area.Width <= 0 OrElse area.Height <= 0 Then area = _proprietario.RectangleToScreen(_proprietario.ClientRectangle)
            If area.Width <= 0 OrElse area.Height <= 0 Then Return

            If _velo Is Nothing OrElse _velo.IsDisposed Then
                _velo = New WGVeloTocco()
                _velo.Bounds = area
                _velo.Owner = forma
            End If

            If _velo.Owner IsNot forma Then _velo.Owner = forma
            If _velo.Bounds <> area Then _velo.Bounds = area

            _velo.Mostra()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.AggiornaVelo: " & ex.ToString())
        End Try
    End Sub

    Private Sub DistruggiVelo()
        Try
            If _velo Is Nothing Then Return
            If Not _velo.IsDisposed Then
                _velo.Owner = Nothing
                _velo.Hide()
                _velo.Dispose()
            End If
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.DistruggiVelo: " & ex.ToString())
        Finally
            _velo = Nothing
        End Try
    End Sub

    Private Function Consentito() As Boolean
        Try
            If _terminato Then Return False
            If Me.IsDisposed OrElse Me.Disposing Then Return False
            If _proprietario Is Nothing Then Return False
            If _touk Is Nothing OrElse Not _touk.TouchAttivo Then Return False
            If Not _iscritto Then Return False

            Dim attiva As Form = Form.ActiveForm
            If attiva IsNot Nothing Then
                Dim forma As Form = _proprietario.FindForm()
                If forma IsNot Nothing AndAlso attiva IsNot forma Then Return False
            End If

            Return _proprietario.ToccoAbilitato
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Consentito: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function Celle() As Rectangle()
        Try
            If _celle IsNot Nothing AndAlso _celle.Length = NUMERO_PULSANTI Then Return _celle

            Dim risultato(NUMERO_PULSANTI - 1) As Rectangle

            Dim larghezzaUtile As Integer = Me.ClientSize.Width - (MARGINE * 2) - (SPAZIO * (NUMERO_PULSANTI - 1))
            If larghezzaUtile < NUMERO_PULSANTI Then larghezzaUtile = NUMERO_PULSANTI

            Dim larghezzaCella As Integer = larghezzaUtile \ NUMERO_PULSANTI
            Dim altezzaCella As Integer = Me.ClientSize.Height - (MARGINE * 2)
            If altezzaCella < 10 Then altezzaCella = 10

            For indice As Integer = 0 To NUMERO_PULSANTI - 1
                Dim sinistra As Integer = MARGINE + (indice * (larghezzaCella + SPAZIO))
                risultato(indice) = New Rectangle(sinistra, MARGINE, larghezzaCella, altezzaCella)
            Next

            _celle = risultato
            Return _celle
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Celle: " & ex.ToString())
            Dim vuote(NUMERO_PULSANTI - 1) As Rectangle
            Return vuote
        End Try
    End Function

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            _celle = Nothing
            MyBase.OnResize(e)
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.OnResize: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            If e Is Nothing Then Return

            Dim disegno As Graphics = e.Graphics
            disegno.SmoothingMode = SmoothingMode.AntiAlias
            disegno.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit

            Using sfondo As New SolidBrush(WGTema.Sfondo)
                disegno.FillRectangle(sfondo, Me.ClientRectangle)
            End Using

            Using pennaCornice As New Pen(Color.FromArgb(70, 70, 70), 2.0F)
                disegno.DrawLine(pennaCornice, 0, 1, Me.ClientSize.Width, 1)
            End Using

            Dim elenco As Rectangle() = Celle()
            If elenco Is Nothing Then Return

            For indice As Integer = 0 To NUMERO_PULSANTI - 1
                DisegnaPulsante(disegno, indice, elenco(indice), indice = _indiceFuoco)
            Next
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.OnPaint: " & ex.ToString())
        End Try
    End Sub

    Private Sub DisegnaPulsante(disegno As Graphics, indice As Integer, area As Rectangle, aFuoco As Boolean)
        Try
            If area.Width <= 2 OrElse area.Height <= 2 Then Return
            If _fontEtichetta Is Nothing OrElse _fontValore Is Nothing Then Return

            Dim coloreSfondo As Color = If(WGTema.Scuro, Color.FromArgb(28, 28, 28), Color.FromArgb(235, 235, 235))
            Dim coloreBordo As Color = Color.FromArgb(200, 200, 200)
            Dim coloreTesto As Color = WGTema.Testo
            Dim spessore As Single = 2.0F

            If aFuoco Then
                coloreSfondo = Color.FromArgb(0, 62, 118)
                coloreBordo = Color.FromArgb(255, 214, 0)
                coloreTesto = Color.FromArgb(255, 235, 120)
                spessore = 5.0F
            End If

            Using percorso As GraphicsPath = PercorsoArrotondato(area, RAGGIO)
                Using pennello As New SolidBrush(coloreSfondo)
                    disegno.FillPath(pennello, percorso)
                End Using
                Using penna As New Pen(coloreBordo, spessore)
                    disegno.DrawPath(penna, percorso)
                End Using
            End Using

            Dim eCursore As Boolean = (indice = PULSANTE_POSIZIONE OrElse indice = PULSANTE_VOLUME)

            Using formato As New StringFormat()
                formato.Alignment = StringAlignment.Center
                formato.LineAlignment = StringAlignment.Center
                formato.Trimming = StringTrimming.EllipsisCharacter
                formato.FormatFlags = StringFormatFlags.NoWrap

                Using pennelloTesto As New SolidBrush(coloreTesto)
                    If eCursore Then
                        Dim altezzaEtichetta As Single = CSng(area.Height * 0.34)
                        Dim zonaEtichetta As New RectangleF(area.Left + 6, area.Top + 8, area.Width - 12, altezzaEtichetta)
                        Dim zonaValore As New RectangleF(area.Left + 6, area.Top + 8 + altezzaEtichetta, area.Width - 12, CSng(area.Height * 0.24))
                        disegno.DrawString(Etichetta(indice), _fontEtichetta, pennelloTesto, zonaEtichetta, formato)
                        disegno.DrawString(ValoreVisivo(indice), _fontValore, pennelloTesto, zonaValore, formato)
                        DisegnaCursore(disegno, indice, area, coloreBordo)
                    Else
                        Dim zonaUnica As New RectangleF(area.Left + 6, area.Top + 6, area.Width - 12, area.Height - 12)
                        disegno.DrawString(Etichetta(indice), _fontEtichetta, pennelloTesto, zonaUnica, formato)
                    End If
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.DisegnaPulsante: " & ex.ToString())
        End Try
    End Sub

    Private Sub DisegnaCursore(disegno As Graphics, indice As Integer, area As Rectangle, coloreBordo As Color)
        Try
            Dim altezzaBarra As Integer = 16
            Dim margineLaterale As Integer = 16
            Dim alto As Integer = area.Bottom - altezzaBarra - 18
            If alto <= area.Top Then Return

            Dim pista As New Rectangle(area.Left + margineLaterale, alto, area.Width - (margineLaterale * 2), altezzaBarra)
            If pista.Width <= 4 Then Return

            Using pennelloPista As New SolidBrush(Color.FromArgb(60, 60, 60))
                disegno.FillRectangle(pennelloPista, pista)
            End Using

            Dim quotaPiena As Double = Frazione(indice)
            If quotaPiena > 0 Then
                Dim larghezza As Integer = CInt(pista.Width * quotaPiena)
                If larghezza < 3 Then larghezza = 3
                If larghezza > pista.Width Then larghezza = pista.Width
                Using pennelloPieno As New SolidBrush(Color.FromArgb(0, 150, 230))
                    disegno.FillRectangle(pennelloPieno, New Rectangle(pista.Left, pista.Top, larghezza, pista.Height))
                End Using
            End If

            Using penna As New Pen(coloreBordo, 2.0F)
                disegno.DrawRectangle(penna, pista)
            End Using
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.DisegnaCursore: " & ex.ToString())
        End Try
    End Sub

    Private Function PercorsoArrotondato(area As Rectangle, raggio As Integer) As GraphicsPath
        Dim percorso As New GraphicsPath()
        Try
            Dim diametro As Integer = raggio * 2
            If diametro > area.Width Then diametro = Math.Max(2, area.Width - 1)
            If diametro > area.Height Then diametro = Math.Max(2, area.Height - 1)

            Dim rettangolo As New Rectangle(area.Left, area.Top, diametro, diametro)
            percorso.AddArc(rettangolo, 180, 90)
            rettangolo.X = area.Right - diametro - 1
            percorso.AddArc(rettangolo, 270, 90)
            rettangolo.Y = area.Bottom - diametro - 1
            percorso.AddArc(rettangolo, 0, 90)
            rettangolo.X = area.Left
            percorso.AddArc(rettangolo, 90, 90)
            percorso.CloseFigure()
            Return percorso
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.PercorsoArrotondato: " & ex.ToString())
            percorso.Reset()
            percorso.AddRectangle(area)
            Return percorso
        End Try
    End Function

    Private Function Frazione(indice As Integer) As Double
        Try
            If _proprietario Is Nothing Then Return 0
            If indice = PULSANTE_VOLUME Then Return _proprietario.ToccoVolume / 100.0
            If indice <> PULSANTE_POSIZIONE Then Return 0
            If Not _proprietario.ToccoSeekDisponibile Then Return 0

            Dim durata As Double = _proprietario.ToccoDurataSecondi
            If durata <= 0 Then Return 0

            Dim posizione As Double = _proprietario.ToccoPosizioneSecondi
            If posizione < 0 Then posizione = 0
            If posizione > durata Then posizione = durata
            Return posizione / durata
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Frazione: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function Etichetta(indice As Integer) As String
        Try
            Select Case indice
                Case PULSANTE_POSIZIONE
                    Return "Posizione"
                Case PULSANTE_PRECEDENTE
                    Return "Precedente"
                Case PULSANTE_PAUSA
                    If _proprietario IsNot Nothing AndAlso _proprietario.ToccoInPausa Then Return "Riprendi"
                    Return "Pausa"
                Case PULSANTE_SUCCESSIVA
                    Return "Successiva"
                Case PULSANTE_VOLUME
                    Return "Volume"
                Case PULSANTE_CODA
                    Return "Coda"
                Case PULSANTE_ESCI
                    Return "Esci"
                Case Else
                    Return ""
            End Select
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Etichetta: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ValoreVisivo(indice As Integer) As String
        Try
            If _proprietario Is Nothing Then Return ""

            If indice = PULSANTE_VOLUME Then Return _proprietario.ToccoVolume.ToString() & " per cento"

            If indice = PULSANTE_POSIZIONE Then
                If _proprietario.ToccoLive Then Return "in diretta"
                If Not _proprietario.ToccoSeekDisponibile Then Return "non disponibile"

                Dim durata As Double = _proprietario.ToccoDurataSecondi
                If durata <= 0 Then Return "non disponibile"

                Return TempoBreve(_proprietario.ToccoPosizioneSecondi) & " di " & TempoBreve(durata)
            End If

            Return ""
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.ValoreVisivo: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function TempoBreve(secondi As Double) As String
        Try
            If secondi < 0 Then secondi = 0
            Dim totale As Integer = CInt(Math.Floor(secondi))
            Dim ore As Integer = totale \ 3600
            Dim minuti As Integer = (totale Mod 3600) \ 60
            Dim resto As Integer = totale Mod 60

            If ore > 0 Then
                Return ore.ToString() & ":" & minuti.ToString("00") & ":" & resto.ToString("00")
            End If
            Return minuti.ToString() & ":" & resto.ToString("00")
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.TempoBreve: " & ex.ToString())
            Return "0:00"
        End Try
    End Function

    Private Function VoceCompleta(indice As Integer) As String
        Try
            If _proprietario Is Nothing Then Return ""

            Select Case indice
                Case PULSANTE_POSIZIONE
                    Dim descrizione As String = "Posizione della traccia, cursore."
                    If _proprietario.ToccoLive Then Return descrizione & " In diretta."
                    If Not _proprietario.ToccoSeekDisponibile Then Return descrizione & " Non ancora disponibile."

                    Dim durata As Double = _proprietario.ToccoDurataSecondi
                    If durata <= 0 Then Return descrizione & " Non ancora disponibile."

                    Return descrizione & " " & _proprietario.ToccoDescriviSecondi(CInt(Math.Floor(_proprietario.ToccoPosizioneSecondi))) & " di " & _proprietario.ToccoDescriviSecondi(CInt(Math.Floor(durata))) & "."
                Case PULSANTE_PRECEDENTE
                    Return "Traccia precedente."
                Case PULSANTE_PAUSA
                    If _proprietario.ToccoInPausa Then Return "Riprendi la riproduzione."
                    Return "Metti in pausa."
                Case PULSANTE_SUCCESSIVA
                    Return "Traccia successiva."
                Case PULSANTE_VOLUME
                    Return "Volume, cursore. " & _proprietario.ToccoVolume.ToString() & " per cento."
                Case PULSANTE_CODA
                    Return "Coda, elenco delle tracce."
                Case PULSANTE_ESCI
                    Return "Esci dal riproduttore."
                Case Else
                    Return ""
            End Select
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.VoceCompleta: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function FirmaStatoCorrente() As String
        Try
            If _proprietario Is Nothing Then Return ""
            Return _indiceFuoco.ToString() & "|" & If(_proprietario.ToccoInPausa, "1", "0") & "|" & _proprietario.ToccoVolume.ToString() & "|" & CInt(Math.Floor(_proprietario.ToccoPosizioneSecondi)).ToString() & "|" & CInt(Math.Floor(_proprietario.ToccoDurataSecondi)).ToString() & "|" & If(_proprietario.ToccoLive, "1", "0")
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.FirmaStatoCorrente: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub Annuncia(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim voce As sintesivocale = sintesivocale.GetInstance()
            If voce Is Nothing Then Return
            voce.LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Annuncia: " & ex.ToString())
        End Try
    End Sub

    Private Sub SpostaFuoco(passo As Integer)
        Try
            Dim nuovo As Integer = _indiceFuoco + passo
            While nuovo < 0
                nuovo += NUMERO_PULSANTI
            End While
            nuovo = nuovo Mod NUMERO_PULSANTI

            _indiceFuoco = nuovo
            _firmaStato = FirmaStatoCorrente()
            Me.Invalidate()
            Annuncia(VoceCompleta(_indiceFuoco))
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.SpostaFuoco: " & ex.ToString())
        End Try
    End Sub

    Private Sub PortaFuocoSu(indice As Integer, daAnnunciare As Boolean)
        Try
            If indice < 0 OrElse indice >= NUMERO_PULSANTI Then Return

            If indice = _indiceFuoco Then
                If daAnnunciare Then Annuncia(VoceCompleta(_indiceFuoco))
                Return
            End If

            _indiceFuoco = indice
            _firmaStato = FirmaStatoCorrente()
            Me.Invalidate()
            If daAnnunciare Then Annuncia(VoceCompleta(_indiceFuoco))
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.PortaFuocoSu: " & ex.ToString())
        End Try
    End Sub

    Private Function PulsanteDaPuntoSchermo(xSchermo As Integer, ySchermo As Integer) As Integer
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return -1
            If Not Me.Visible Then Return -1

            Dim punto As Point = Me.PointToClient(New Point(xSchermo, ySchermo))
            Dim elenco As Rectangle() = Celle()
            If elenco Is Nothing Then Return -1

            For indice As Integer = 0 To NUMERO_PULSANTI - 1
                If elenco(indice).Contains(punto) Then Return indice
            Next

            Return -1
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.PulsanteDaPuntoSchermo: " & ex.ToString())
            Return -1
        End Try
    End Function

    Private Function PulsanteSottoIlGesto() As Integer
        Try
            If _touk Is Nothing Then Return -1
            Dim punto As Point = _touk.PuntoInizioGesto
            If punto.IsEmpty Then Return -1
            Return PulsanteDaPuntoSchermo(punto.X, punto.Y)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.PulsanteSottoIlGesto: " & ex.ToString())
            Return -1
        End Try
    End Function

    Private Sub GestisciFlickAvanti()
        Try
            If Not Consentito() Then Return
            SpostaFuoco(1)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciFlickAvanti: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciFlickIndietro()
        Try
            If Not Consentito() Then Return
            SpostaFuoco(-1)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciFlickIndietro: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciFlickSu()
        Try
            If Not Consentito() Then Return
            AzionaCursore(1)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciFlickSu: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciFlickGiu()
        Try
            If Not Consentito() Then Return
            AzionaCursore(-1)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciFlickGiu: " & ex.ToString())
        End Try
    End Sub

    Private Sub AzionaCursore(direzione As Integer)
        Try
            If _proprietario Is Nothing Then Return

            If _indiceFuoco = PULSANTE_POSIZIONE Then
                Dim passo As Integer = _proprietario.ToccoPassoScorrimento
                If passo <= 0 Then passo = 30
                _proprietario.ToccoScorri(passo * direzione)
                _firmaStato = FirmaStatoCorrente()
                Me.Invalidate()
                Return
            End If

            If _indiceFuoco = PULSANTE_VOLUME Then
                Dim passoVolume As Integer = _proprietario.ToccoPassoVolume
                If passoVolume <= 0 Then passoVolume = 5
                _proprietario.ToccoCambiaVolume(passoVolume * direzione)
                _firmaStato = FirmaStatoCorrente()
                Me.Invalidate()
                Return
            End If

            Annuncia("Su " & Etichetta(_indiceFuoco) & " non c'è un cursore. Fai due tocchi per usarlo.")
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.AzionaCursore: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciTapSingolo()
        Try
            If Not Consentito() Then Return

            Dim sottoIlDito As Integer = PulsanteSottoIlGesto()
            If sottoIlDito >= 0 Then
                PortaFuocoSu(sottoIlDito, True)
                Return
            End If

            Annuncia(VoceCompleta(_indiceFuoco))
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciTapSingolo: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciTapDoppio()
        Try
            If Not Consentito() Then Return

            Dim sottoIlDito As Integer = PulsanteSottoIlGesto()
            If sottoIlDito >= 0 Then PortaFuocoSu(sottoIlDito, False)

            Aziona()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciTapDoppio: " & ex.ToString())
        End Try
    End Sub

    Private Sub Aziona()
        Try
            If _proprietario Is Nothing Then Return

            Select Case _indiceFuoco
                Case PULSANTE_POSIZIONE
                    Annuncia(VoceCompleta(PULSANTE_POSIZIONE))
                Case PULSANTE_PRECEDENTE
                    _proprietario.ToccoTracciaPrecedente()
                Case PULSANTE_PAUSA
                    _proprietario.ToccoPausaRiprendi()
                Case PULSANTE_SUCCESSIVA
                    _proprietario.ToccoTracciaSuccessiva()
                Case PULSANTE_VOLUME
                    Annuncia(VoceCompleta(PULSANTE_VOLUME))
                Case PULSANTE_CODA
                    _proprietario.ToccoApriCoda()
                Case PULSANTE_ESCI
                    _proprietario.ToccoEsci()
            End Select

            _firmaStato = FirmaStatoCorrente()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Aziona: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciEscapeGesto()
        Try
            If Not Consentito() Then Return
            _proprietario.ToccoEsci()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciEscapeGesto: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciLetturaCompleta()
        Try
            If Not Consentito() Then Return
            _proprietario.ToccoLeggiStato()
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciLetturaCompleta: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciEsplorazione(xSchermo As Integer, ySchermo As Integer)
        Try
            If Not Consentito() Then Return
            Dim indice As Integer = PulsanteDaPuntoSchermo(xSchermo, ySchermo)
            If indice < 0 Then Return
            PortaFuocoSu(indice, True)
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.GestisciEsplorazione: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            If disposing Then
                _terminato = True
                DisiscriviGesti()
                DisiscriviStato()
                FermaTimer()
                DistruggiVelo()

                If _fontEtichetta IsNot Nothing Then
                    _fontEtichetta.Dispose()
                    _fontEtichetta = Nothing
                End If
                If _fontValore IsNot Nothing Then
                    _fontValore.Dispose()
                    _fontValore = Nothing
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGToccoPlayer.Dispose: " & ex.ToString())
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

End Class

Public Class WGVeloTocco
    Inherits Form

    Private Const WS_EX_NOACTIVATE As Integer = &H8000000
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WM_MOUSEACTIVATE As Integer = &H21
    Private Const WM_NCACTIVATE As Integer = &H86
    Private Const MA_NOACTIVATE As Integer = 3

    Private Const SWP_NOSIZE As UInteger = &H1UI
    Private Const SWP_NOMOVE As UInteger = &H2UI
    Private Const SWP_NOACTIVATE As UInteger = &H10UI
    Private Const SW_SHOWNOACTIVATE As Integer = 4

    Private ReadOnly HWND_TOPMOST As New IntPtr(-1)

    <DllImport("user32.dll")>
    Private Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, X As Integer, Y As Integer, cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    Private ReadOnly log As New Logger()

    Public Sub New()
        Try
            Me.FormBorderStyle = FormBorderStyle.None
            Me.ShowInTaskbar = False
            Me.StartPosition = FormStartPosition.Manual
            Me.ControlBox = False
            Me.MinimizeBox = False
            Me.MaximizeBox = False
            Me.KeyPreview = False
            Me.BackColor = Color.Black
            Me.Opacity = 0.02
            Me.TopMost = True
            Me.Size = New Size(200, 200)
        Catch ex As Exception
            log.ScriviLog("WGVeloTocco.New: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As System.Windows.Forms.CreateParams
        Get
            Try
                Dim parametri As System.Windows.Forms.CreateParams = MyBase.CreateParams
                parametri.ExStyle = parametri.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
                Return parametri
            Catch ex As Exception
                log.ScriviLog("WGVeloTocco.CreateParams: " & ex.ToString())
                Return MyBase.CreateParams
            End Try
        End Get
    End Property

    Public Sub Mostra()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim finestraPrima As IntPtr = GetForegroundWindow()

            If Not Me.Visible Then
                Me.Show()
                If Me.IsHandleCreated Then ShowWindow(Me.Handle, SW_SHOWNOACTIVATE)
            End If

            If Me.IsHandleCreated Then
                SetWindowPos(Me.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE Or SWP_NOSIZE Or SWP_NOACTIVATE)

                If GetForegroundWindow() = Me.Handle AndAlso finestraPrima <> IntPtr.Zero AndAlso finestraPrima <> Me.Handle Then
                    If Not SetForegroundWindow(finestraPrima) Then
                        Dim pidCorrente As UInteger = 0
                        Dim threadPrima As UInteger = GetWindowThreadProcessId(finestraPrima, pidCorrente)
                        Dim threadMio As UInteger = GetCurrentThreadId()
                        Dim agganciato As Boolean = False
                        Try
                            If threadPrima <> 0UI AndAlso threadPrima <> threadMio Then
                                agganciato = AttachThreadInput(threadMio, threadPrima, True)
                            End If
                            SetForegroundWindow(finestraPrima)
                        Finally
                            If agganciato Then AttachThreadInput(threadMio, threadPrima, False)
                        End Try
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGVeloTocco.Mostra: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = WM_MOUSEACTIVATE Then
                m.Result = New IntPtr(MA_NOACTIVATE)
                Return
            End If

            If m.Msg = WM_NCACTIVATE Then
                m.Result = New IntPtr(1)
                Return
            End If

            MyBase.WndProc(m)
        Catch ex As Exception
            log.ScriviLog("WGVeloTocco.WndProc: " & ex.ToString())
        End Try
    End Sub

End Class