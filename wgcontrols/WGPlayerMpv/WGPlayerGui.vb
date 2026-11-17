' ============================================================================================
' WGPlayerGUI - superficie video e barre di scorrimento del lettore multimediale
' ============================================================================================
'
' A cosa serve
'   E' il controllo WinForms che il lettore (WGPlayerInt / WGMpv) usa per mostrare il video e
'   per far vedere con il mouse titolo, avanzamento del brano e volume. Non riproduce nulla da
'   solo: espone soltanto una finestra figlia il cui handle viene passato a mpv, disegna le due
'   barre e avvisa chi lo ospita quando l'utente trascina il mouse.
'
' Come si usa
'   Dim gui As New WGPlayerGUI()
'   contenitore.Controls.Add(gui)
'   AddHandler gui.SeekRichiesto, AddressOf VaiAPercentuale
'   AddHandler gui.VolumeRichiesto, AddressOf ImpostaVolume
'   lettore.ImpostaFinestraVideo(gui.VideoHandle)
'   gui.Titolo = "Nome del brano"
'   gui.AggiornaStato(posizioneMs, durataMs, volume)
'
' Metodi ed elementi pubblici principali
'   VideoHandle       handle della superficie video da dare a mpv; IntPtr.Zero se la superficie
'                     non e' ancora pronta o e' gia' stata distrutta.
'   Titolo            testo mostrato nella fascia superiore.
'   AggiornaStato     aggiorna posizione, durata e volume e ridisegna; da chiamare dal thread
'                     dell'interfaccia (per esempio da un timer del form).
'   SeekRichiesto     evento con la percentuale (0..1) su cui l'utente ha cliccato nella barra
'                     del tempo.
'   VolumeRichiesto   evento con il volume (0..100) scelto sulla barra verticale a destra.
'
' Errori
'   Nessun metodo solleva eccezioni: ogni errore viene annotato nel log (dati\logs\log.txt) e la
'   chiamata termina senza effetto. VideoHandle restituisce IntPtr.Zero quando fallisce.
'
' Attenzione
'   E' un supporto visivo per chi vede: la vocalizzazione dei comandi resta a carico del lettore
'   che lo ospita. Chiamare AggiornaStato solo dal thread dell'interfaccia, altrimenti il
'   ridisegno non e' garantito. La durata e la posizione sono in millisecondi.
' ============================================================================================
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.ComponentModel
Imports WGDati
Imports wgcontrols
Friend Class WGPlayerGUI
    Inherits Control

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetCapture(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function ReleaseCapture() As Boolean
    End Function

    Public Event SeekRichiesto(percentuale As Double)
    Public Event VolumeRichiesto(valore As Integer)

    Private _videoSurface As Control
    Private _titolo As String = ""
    Private _durataTotale As Long = 0
    Private _posizioneCorrente As Long = 0
    Private _volumeCorrente As Integer = 100

    Private _titoloHeight As Integer = 40
    Private _sliderTimeHeight As Integer = 40
    Private _sliderVolWidth As Integer = 30

    Private _draggingTime As Boolean = False
    Private _draggingVol As Boolean = False

    Private _logger As logger

    Public Sub New()
        Try
            _logger = New Logger
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Me.BackColor = WGTema.Sfondo

            ' La superficie video resta sempre nera: e' l'area dove mpv disegna i fotogrammi, non un controllo dell'interfaccia.
            _videoSurface = New Control()
            _videoSurface.BackColor = Color.Black
            Me.Controls.Add(_videoSurface)
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            ' Il braille mostra la posizione nel brano e usa l'instradamento per muoversi, finche' il lettore e' visibile.
            AddHandler Me.VisibleChanged, Sub(s As Object, e As EventArgs) WGBraille.ImpostaContestoPlayer(Me.Visible AndAlso Me.IsHandleCreated)
            AddHandler Me.Disposed, Sub(s As Object, e As EventArgs)
                                         WGBraille.ImpostaContestoPlayer(False)
                                         RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
                                     End Sub
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.New] Exception: " & ex.ToString())
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
            Me.Invalidate(True)
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnCambiatoTema] Exception: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScriviLogSicuro(messaggio As String)
        Try
        Catch
            ' Se fallisce la scrittura del log non c'e' altro posto dove segnalarlo.
        End Try
    End Sub

    Public ReadOnly Property VideoHandle As IntPtr
        Get
            Try
                If _videoSurface Is Nothing OrElse _videoSurface.IsDisposed Then Return IntPtr.Zero
                Return _videoSurface.Handle
            Catch ex As Exception
                ScriviLogSicuro("[WGPlayerGUI.VideoHandle] Exception: " & ex.ToString())
                Return IntPtr.Zero
            End Try
        End Get
    End Property

    Public Property Titolo As String
        Get
            Return _titolo
        End Get
        Set(value As String)
            Try
                _titolo = value
                Me.Invalidate()
            Catch ex As Exception
                ScriviLogSicuro("[WGPlayerGUI.Titolo] Exception: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Sub AggiornaStato(posizione As Long, durata As Long, volume As Integer)
        Try
            _posizioneCorrente = posizione
            _durataTotale = durata
            _volumeCorrente = volume
            Me.Invalidate()
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.AggiornaStato] Exception: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            Dim videoWidth As Integer = Me.Width - _sliderVolWidth
            Dim videoHeight As Integer = Me.Height - _titoloHeight - _sliderTimeHeight
            If videoWidth < 0 Then videoWidth = 0
            If videoHeight < 0 Then videoHeight = 0

            If _videoSurface Is Nothing OrElse _videoSurface.IsDisposed Then Return
            _videoSurface.SetBounds(0, _titoloHeight, videoWidth, videoHeight)
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnResize] Exception: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            If e Is Nothing OrElse e.Graphics Is Nothing Then Return
            Dim g As Graphics = e.Graphics
            Dim carattere As Font = Me.Font
            If carattere Is Nothing Then Return

            Dim rectTitolo As New Rectangle(0, 0, Me.Width, _titoloHeight)
            g.FillRectangle(Brushes.Black, rectTitolo)
            Using sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                g.DrawString(If(_titolo, ""), carattere, Brushes.White, rectTitolo, sf)
            End Using

            Dim rectTime As New Rectangle(0, Me.Height - _sliderTimeHeight, Me.Width, _sliderTimeHeight)
            g.FillRectangle(Brushes.DarkGray, rectTime)
            If _durataTotale > 0 AndAlso Me.Width > 0 Then
                Dim frazione As Double = _posizioneCorrente / _durataTotale
                If frazione < 0 Then frazione = 0
                If frazione > 1 Then frazione = 1
                Dim widthPiena As Integer = CInt(frazione * Me.Width)
                If widthPiena > 0 Then
                    g.FillRectangle(Brushes.Red, 0, Me.Height - _sliderTimeHeight, widthPiena, _sliderTimeHeight)
                End If
            End If

            Dim tempoFormat As String = $"{FormattaTempo(_posizioneCorrente)} / {FormattaTempo(_durataTotale)}"
            g.DrawString(tempoFormat, carattere, Brushes.White, New PointF(10, Me.Height - _sliderTimeHeight + 10))

            Dim altezzaVol As Integer = Me.Height - _titoloHeight - _sliderTimeHeight
            If altezzaVol < 0 Then altezzaVol = 0
            Dim rectVol As New Rectangle(Me.Width - _sliderVolWidth, _titoloHeight, _sliderVolWidth, altezzaVol)
            g.FillRectangle(Brushes.DarkGray, rectVol)

            Dim volHeight As Integer = rectVol.Height
            Dim volPerc As Double = _volumeCorrente / 100.0
            If volPerc < 0 Then volPerc = 0
            If volPerc > 1 Then volPerc = 1
            Dim volFill As Integer = CInt(volPerc * volHeight)
            Dim yStart As Integer = rectVol.Y + (volHeight - volFill)

            If volFill > 0 Then
                g.FillRectangle(Brushes.Cyan, rectVol.X, yStart, rectVol.Width, volFill)
            End If
            g.DrawString("V", carattere, Brushes.Black, New PointF(rectVol.X + 5, rectVol.Y + 5))
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnPaint] Exception: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Try
            MyBase.OnMouseDown(e)

            Dim rectTime As New Rectangle(0, Me.Height - _sliderTimeHeight, Me.Width, _sliderTimeHeight)
            Dim rectVol As New Rectangle(Me.Width - _sliderVolWidth, _titoloHeight, _sliderVolWidth, Me.Height - _titoloHeight - _sliderTimeHeight)

            If rectTime.Contains(e.Location) Then
                _draggingTime = True
                Try
                    SetCapture(Me.Handle)
                Catch exDll As Exception
                    ScriviLogSicuro("[WGPlayerGUI.OnMouseDown.SetCapture] Exception: " & exDll.ToString())
                End Try
                CalcolaSeek(e.X)
            ElseIf rectVol.Contains(e.Location) Then
                _draggingVol = True
                Try
                    SetCapture(Me.Handle)
                Catch exDll As Exception
                    ScriviLogSicuro("[WGPlayerGUI.OnMouseDown.SetCapture] Exception: " & exDll.ToString())
                End Try
                CalcolaVolume(e.Y, rectVol)
            End If
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnMouseDown] Exception: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Try
            MyBase.OnMouseMove(e)
            If _draggingTime Then
                CalcolaSeek(e.X)
            ElseIf _draggingVol Then
                Dim rectVol As New Rectangle(Me.Width - _sliderVolWidth, _titoloHeight, _sliderVolWidth, Me.Height - _titoloHeight - _sliderTimeHeight)
                CalcolaVolume(e.Y, rectVol)
            End If
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnMouseMove] Exception: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        Try
            MyBase.OnMouseUp(e)
            Try
                ReleaseCapture()
            Catch exDll As Exception
                ScriviLogSicuro("[WGPlayerGUI.OnMouseUp.ReleaseCapture] Exception: " & exDll.ToString())
            End Try
            _draggingTime = False
            _draggingVol = False
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.OnMouseUp] Exception: " & ex.ToString())
        End Try
    End Sub

    Private Function FormattaTempo(millisecondi As Long) As String
        Try
            If millisecondi < 0 Then millisecondi = 0
            ' TimeSpan.FromMilliseconds solleva OverflowException con valori fuori scala.
            If millisecondi > 86400000L * 365L Then millisecondi = 0
            Return TimeSpan.FromMilliseconds(millisecondi).ToString("mm\:ss")
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.FormattaTempo] Exception: " & ex.ToString())
            Return "00:00"
        End Try
    End Function

    Private Sub CalcolaSeek(mouseX As Integer)
        Try
            If Me.Width <= 0 Then Return
            Dim perc As Double = mouseX / Me.Width
            If perc < 0 Then perc = 0
            If perc > 1 Then perc = 1
            RaiseEvent SeekRichiesto(perc)
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.CalcolaSeek] Exception: " & ex.ToString())
        End Try
    End Sub

    Private Sub CalcolaVolume(mouseY As Integer, rectVol As Rectangle)
        Try
            Dim relativeY As Integer = mouseY - rectVol.Y
            Dim height As Integer = rectVol.Height
            If height <= 0 Then Return

            Dim perc As Double = 1.0 - (relativeY / height)
            If perc < 0 Then perc = 0
            If perc > 1 Then perc = 1

            RaiseEvent VolumeRichiesto(CInt(perc * 100))
        Catch ex As Exception
            ScriviLogSicuro("[WGPlayerGUI.CalcolaVolume] Exception: " & ex.ToString())
        End Try
    End Sub

End Class