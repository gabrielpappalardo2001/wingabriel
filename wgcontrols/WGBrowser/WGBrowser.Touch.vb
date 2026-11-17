' ============================================================================================
' WGBrowser - gesti del Touk, rotore e tastiera a schermo
' ============================================================================================
'
' Con lo schermo touch il browser si usa come VoiceOver su iPhone:
'   - flick a destra o a sinistra: elemento successivo o precedente;
'   - flick con tre dita in giù o in su: gira il rotore (intestazioni, link, pulsanti, campi,
'     tabelle, righe, parole, caratteri...); nei campi di testo il rotore ha caratteri, parole e righe;
'   - flick in giù o in su con un dito: elemento successivo o precedente del tipo scelto nel rotore;
'   - doppio tocco: attiva l'elemento; in un campo di testo mostra la tastiera a schermo;
'   - doppio tocco prolungato: menu opzioni del browser;
'   - doppio tocco con due dita: Esc; flick in giù con due dita: lettura continua.
' I gesti diventano i tasti corrispondenti e passano dalla stessa coda della tastiera fisica, così
' fanno esattamente quello che fanno i tasti.
' In un campo di testo, con il touch attivo, compare la tastiera a schermo (WGTastieraTouch) con
' suggerimenti e dettatura; scrive nel campo della pagina come la tastiera fisica.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports wgcontrols

Partial Public Class WGBrowser

    Private Class VoceRotore
        Public Nome As String
        ' Il tasto per andare avanti (indietro è lo stesso con Shift, se Maiuscole è vero).
        Public Tasto As Keys
        Public TastoIndietro As Keys
        Public Ctrl As Boolean
        Public ConShift As Boolean
        ' Voce speciale che apre il menu opzioni.
        Public Opzioni As Boolean

        Public Shared Function Lettera(nome As String, tasto As Keys) As VoceRotore
            Return New VoceRotore() With {.Nome = nome, .Tasto = tasto, .ConShift = True}
        End Function

        Public Shared Function Coppia(nome As String, avanti As Keys, indietro As Keys, Optional ctrl As Boolean = False) As VoceRotore
            Return New VoceRotore() With {.Nome = nome, .Tasto = avanti, .TastoIndietro = indietro, .Ctrl = ctrl}
        End Function
    End Class

    Private Shared ReadOnly RotorePagina As VoceRotore() = {
        VoceRotore.Lettera("Intestazioni", Keys.H),
        VoceRotore.Lettera("Link", Keys.K),
        VoceRotore.Lettera("Pulsanti", Keys.B),
        VoceRotore.Lettera("Campi del modulo", Keys.F),
        VoceRotore.Lettera("Campi di testo", Keys.E),
        VoceRotore.Lettera("Caselle combinate", Keys.C),
        VoceRotore.Lettera("Caselle di controllo", Keys.X),
        VoceRotore.Lettera("Pulsanti di opzione", Keys.O),
        VoceRotore.Lettera("Tabelle", Keys.T),
        VoceRotore.Lettera("Liste", Keys.L),
        VoceRotore.Lettera("Elementi di lista", Keys.I),
        VoceRotore.Lettera("Regioni", Keys.D),
        VoceRotore.Lettera("Immagini", Keys.G),
        VoceRotore.Lettera("Paragrafi", Keys.P),
        VoceRotore.Lettera("Elementi cliccabili", Keys.V),
        VoceRotore.Coppia("Righe", Keys.Down, Keys.Up),
        VoceRotore.Coppia("Parole", Keys.Right, Keys.Left, True),
        VoceRotore.Coppia("Caratteri", Keys.Right, Keys.Left),
        New VoceRotore() With {.Nome = "Opzioni del browser", .Opzioni = True}
    }

    Private Shared ReadOnly RotoreCampo As VoceRotore() = {
        VoceRotore.Coppia("Caratteri", Keys.Right, Keys.Left),
        VoceRotore.Coppia("Parole", Keys.Right, Keys.Left, True),
        VoceRotore.Coppia("Righe", Keys.Down, Keys.Up),
        New VoceRotore() With {.Nome = "Opzioni del campo", .Opzioni = True}
    }

    Private WithEvents _toukBrowser As WGTouk
    Private _gestoreRotore As Func(Of Boolean, Boolean)
    Private _indiceRotorePagina As Integer = 0
    Private _indiceRotoreCampo As Integer = 0
    Private _tastieraTouch As WGTastieraTouch
    ' L'utente ha chiuso la tastiera in questo campo: non la si rimostra finché non si esce dal campo.
    Private _tastieraChiusaDallUtente As Boolean
    Private WithEvents _timerTastieraTouch As New System.Windows.Forms.Timer() With {.Interval = 250}

    ''' <summary>
    ''' Finestra quasi trasparente sopra la pagina. La pagina è una finestra del motore WebView2, che
    ''' gira in un altro processo: i tocchi su di lei non passano dal filtro dei messaggi di WinGabriel,
    ''' quindi il Touk non li vede e la pagina li tratta da sola (la pressione lunga apre il suo menu
    ''' contestuale). Questa finestra appartiene a WinGabriel e prende i tocchi al posto della pagina;
    ''' non prende mai il fuoco e si vede appena (opacità minima, altrimenti Windows non le darebbe i tocchi).
    ''' </summary>
    Private Class SchermoTocchi
        Inherits Form

        Private Const WS_EX_NOACTIVATE As Integer = &H8000000
        Private Const WS_EX_TOOLWINDOW As Integer = &H80
        Private Const WM_MOUSEACTIVATE As Integer = &H21
        Private Const MA_NOACTIVATE As Integer = 3

        Public Sub New()
            Me.FormBorderStyle = FormBorderStyle.None
            Me.ShowInTaskbar = False
            Me.StartPosition = FormStartPosition.Manual
            Me.BackColor = System.Drawing.Color.Black
            Me.Opacity = 0.01
            Me.Text = ""
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim parametri As CreateParams = MyBase.CreateParams
                parametri.ExStyle = parametri.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
                Return parametri
            End Get
        End Property

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_MOUSEACTIVATE Then
                m.Result = New IntPtr(MA_NOACTIVATE)
                Return
            End If
            MyBase.WndProc(m)
        End Sub
    End Class

    Private _schermoTocchi As SchermoTocchi

    ' Lo schermo dei tocchi copre la pagina quando il touch è attivo e il browser è davanti, senza
    ' coprire la tastiera a schermo e senza esserci quando è aperto il menu opzioni.
    Private Sub AggiornaSchermoTocchi()
        Try
            Dim serve As Boolean = _toukBrowser IsNot Nothing AndAlso _toukBrowser.TouchAttivo AndAlso PuoRicevere() AndAlso
                                   Not _menuOpzioniAperto AndAlso Me.IsHandleCreated
            Dim modulo As Form = If(serve, Me.FindForm(), Nothing)
            If modulo Is Nothing Then
                If _schermoTocchi IsNot Nothing AndAlso Not _schermoTocchi.IsDisposed AndAlso _schermoTocchi.Visible Then _schermoTocchi.Hide()
                Return
            End If
            Dim area As System.Drawing.Rectangle = Me.RectangleToScreen(Me.ClientRectangle)
            If _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible Then
                Dim tastiera As System.Drawing.Rectangle = _tastieraTouch.RectangleToScreen(_tastieraTouch.ClientRectangle)
                area.Height = Math.Max(0, Math.Min(area.Height, tastiera.Top - area.Top))
            End If
            If area.Width <= 0 OrElse area.Height <= 0 Then
                If _schermoTocchi IsNot Nothing AndAlso Not _schermoTocchi.IsDisposed Then _schermoTocchi.Hide()
                Return
            End If
            If _schermoTocchi Is Nothing OrElse _schermoTocchi.IsDisposed Then _schermoTocchi = New SchermoTocchi()
            If _schermoTocchi.Owner IsNot modulo Then _schermoTocchi.Owner = modulo
            If _schermoTocchi.Bounds <> area Then _schermoTocchi.Bounds = area
            If Not _schermoTocchi.Visible Then _schermoTocchi.Show(modulo)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiornaSchermoTocchi: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaTouch()
        Try
            _toukBrowser = WGTouk.GetInstance()
            _gestoreRotore = AddressOf GiraRotore
            _toukBrowser.AggiungiGestoreRotore(_gestoreRotore)
            _timerTastieraTouch.Start()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AvviaTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaTouch()
        Try
            _timerTastieraTouch.Stop()
            _timerTastieraTouch.Dispose()
            If _schermoTocchi IsNot Nothing AndAlso Not _schermoTocchi.IsDisposed Then _schermoTocchi.Dispose()
            _schermoTocchi = Nothing
            If _toukBrowser IsNot Nothing AndAlso _gestoreRotore IsNot Nothing Then _toukBrowser.RimuoviGestoreRotore(_gestoreRotore)
            _toukBrowser = Nothing
            If _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed Then _tastieraTouch.Dispose()
            _tastieraTouch = Nothing
        Catch ex As Exception
            log.ScriviLog("WGBrowser.FermaTouch: " & ex.Message)
        End Try
    End Sub

#Region "Gesti"

    ' Il gesto è per il browser: è davanti, non c'è il menu opzioni e il dito non è sulla tastiera a schermo.
    Private Function GestoPerIlBrowser() As Boolean
        Try
            If _toukBrowser Is Nothing OrElse Not _toukBrowser.TouchAttivo Then Return False
            If Not PuoRicevere() OrElse _menuOpzioniAperto OrElse _attiva Is Nothing Then Return False
            If WGTastieraTouch.GestoSullaTastieraAttiva(_toukBrowser) Then Return False
            If _toukBrowser.GestoIniziatoSuTastieraTouch() Then Return False
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrowser.GestoPerIlBrowser: " & ex.Message)
            Return False
        End Try
    End Function

    Private ReadOnly Property InCampoDiTesto As Boolean
        Get
            Return _attiva IsNot Nothing AndAlso _attiva.InCampoDiTesto
        End Get
    End Property

    Private Sub AccodaGesto(tasto As Keys, Optional shift As Boolean = False, Optional ctrl As Boolean = False)
        Try
            Dim pressione As New PressioneTastoBrowser()
            pressione.Tasto = tasto
            pressione.Shift = shift
            pressione.Ctrl = ctrl
            codaTasti.Enqueue(pressione)
            If Not elaborazioneInCorso Then ElaboraCodaTasti()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AccodaGesto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Flick con tre dita: gira il rotore della pagina o, in un campo di testo, quello del campo.</summary>
    Private Function GiraRotore(avanti As Boolean) As Boolean
        Try
            If Not GestoPerIlBrowser() Then Return False
            Dim voci As VoceRotore() = If(InCampoDiTesto, RotoreCampo, RotorePagina)
            Dim passo As Integer = If(avanti, 1, -1)
            If InCampoDiTesto Then
                _indiceRotoreCampo = (_indiceRotoreCampo + passo + voci.Length) Mod voci.Length
                lettura.LeggiTesto(voci(_indiceRotoreCampo).Nome)
            Else
                _indiceRotorePagina = (_indiceRotorePagina + passo + voci.Length) Mod voci.Length
                lettura.LeggiTesto(voci(_indiceRotorePagina).Nome)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrowser.GiraRotore: " & ex.Message)
            Return False
        End Try
    End Function

    ' Flick in su o in giù con un dito: si muove secondo il rotore.
    Private Sub MuoviConRotore(avanti As Boolean)
        Dim voce As VoceRotore = If(InCampoDiTesto, RotoreCampo(_indiceRotoreCampo), RotorePagina(_indiceRotorePagina))
        If voce.Opzioni Then
            If _attiva IsNot Nothing AndAlso _attiva.Cursore IsNot Nothing Then _attiva.Cursore.FermaLetturaContinua()
            ApriOpzioni()
            Return
        End If
        If voce.ConShift Then
            AccodaGesto(voce.Tasto, shift:=Not avanti)
        Else
            AccodaGesto(If(avanti, voce.Tasto, voce.TastoIndietro), ctrl:=voce.Ctrl)
        End If
    End Sub

    Private Sub _toukBrowser_FlickGiu() Handles _toukBrowser.FlickGiu
        Try
            If GestoPerIlBrowser() Then MuoviConRotore(True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_FlickGiu: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_FlickSu() Handles _toukBrowser.FlickSu
        Try
            If GestoPerIlBrowser() Then MuoviConRotore(False)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_FlickSu: " & ex.Message)
        End Try
    End Sub

    ' Flick a destra e a sinistra: elemento successivo e precedente. Da un campo si esce e si prosegue.
    Private Sub SpostaElemento(avanti As Boolean)
        If _attiva IsNot Nothing AndAlso _attiva.InCampo Then _attiva.EsciDalCampoInSilenzio()
        AccodaGesto(If(avanti, Keys.Down, Keys.Up))
    End Sub

    Private Sub _toukBrowser_FlickAvanti() Handles _toukBrowser.FlickAvanti
        Try
            If GestoPerIlBrowser() Then SpostaElemento(True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_FlickAvanti: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_FlickIndietro() Handles _toukBrowser.FlickIndietro
        Try
            If GestoPerIlBrowser() Then SpostaElemento(False)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_FlickIndietro: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_TapDoppio() Handles _toukBrowser.TapDoppio
        Try
            If Not GestoPerIlBrowser() Then Return
            If InCampoDiTesto Then
                _tastieraChiusaDallUtente = False
                AggiornaTastieraTouch()
                lettura.LeggiTesto("Tastiera")
                Return
            End If
            AccodaGesto(Keys.Enter)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_TapDoppio: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_TapDoppioProlungato() Handles _toukBrowser.TapDoppioProlungato
        Try
            If Not GestoPerIlBrowser() Then Return
            If _attiva IsNot Nothing AndAlso _attiva.Cursore IsNot Nothing Then _attiva.Cursore.FermaLetturaContinua()
            ApriOpzioni()
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_TapDoppioProlungato: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_EscapeGesto() Handles _toukBrowser.EscapeGesto
        Try
            If GestoPerIlBrowser() Then AccodaGesto(Keys.Escape)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_EscapeGesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _toukBrowser_RichiestaLetturaCompleta() Handles _toukBrowser.RichiestaLetturaCompleta
        Try
            If GestoPerIlBrowser() Then AccodaGesto(Keys.Insert)
        Catch ex As Exception
            log.ScriviLog("WGBrowser._toukBrowser_RichiestaLetturaCompleta: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Tastiera a schermo"

    ' La tastiera compare nei campi di testo con il touch attivo e sparisce uscendo dal campo.
    Private Sub _timerTastieraTouch_Tick(sender As Object, e As EventArgs) Handles _timerTastieraTouch.Tick
        AggiornaTastieraTouch()
        AggiornaSchermoTocchi()
    End Sub

    Private Sub AggiornaTastieraTouch()
        Try
            If _chiuso OrElse Me.IsDisposed Then Return
            If Not InCampoDiTesto Then _tastieraChiusaDallUtente = False
            Dim serve As Boolean = _toukBrowser IsNot Nothing AndAlso _toukBrowser.TouchAttivo AndAlso PuoRicevere() AndAlso
                                   Not _menuOpzioniAperto AndAlso InCampoDiTesto AndAlso Not _tastieraChiusaDallUtente
            Dim visibile As Boolean = _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible
            If serve = visibile Then Return
            If serve Then MostraTastieraTouch() Else NascondiTastieraTouch()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiornaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraTastieraTouch()
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then
                _tastieraTouch = New WGTastieraTouch()
                AddHandler _tastieraTouch.CarattereInserito, AddressOf TastieraTouch_Carattere
                AddHandler _tastieraTouch.EliminaRichiesto, AddressOf TastieraTouch_Elimina
                AddHandler _tastieraTouch.InvioRichiesto, AddressOf TastieraTouch_Invio
                AddHandler _tastieraTouch.SuggerimentoScelto, AddressOf TastieraTouch_Suggerimento
                AddHandler _tastieraTouch.ChiudiRichiesto, AddressOf TastieraTouch_Chiudi
                AddHandler _tastieraTouch.TestoDettatoInserito, AddressOf TastieraTouch_Dettato
            End If
            _tastieraTouch.Dock = DockStyle.None
            If Not Me.Controls.Contains(_tastieraTouch) Then Me.Controls.Add(_tastieraTouch)
            _tastieraTouch.Mostra()
            _tastieraTouch.ApplicaAltezzaVoluta()
            _tastieraTouch.BringToFront()
            AggiornaSuggerimentiTouch()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastieraTouch()
        Try
            If _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed Then _tastieraTouch.Nascondi()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.NascondiTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaSuggerimentiTouch()
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed OrElse _attiva Is Nothing Then Return
            _tastieraTouch.AggiornaSuggerimenti(_attiva.TestoPrimaDelCursore())
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiornaSuggerimentiTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub TastieraTouch_Carattere(carattere As String)
        SuUIBrowser(Sub()
                        If _attiva Is Nothing Then Return
                        _attiva.InserisciDaTouch(carattere)
                        AggiornaSuggerimentiTouch()
                    End Sub)
    End Sub

    Private Sub TastieraTouch_Elimina()
        SuUIBrowser(Sub()
                        If _attiva Is Nothing Then Return
                        _attiva.CancellaDaTouch()
                        AggiornaSuggerimentiTouch()
                    End Sub)
    End Sub

    ' Invio passa dalla coda dei tasti: nei campi a riga singola invia il modulo, nei multiriga va a capo.
    Private Sub TastieraTouch_Invio()
        SuUIBrowser(Sub() AccodaGesto(Keys.Enter))
    End Sub

    Private Sub TastieraTouch_Suggerimento(parola As String, caratteriDaSostituire As Integer)
        SuUIBrowser(Async Sub()
                        If _attiva Is Nothing Then Return
                        Await _attiva.SostituisciParolaDaTouchAsync(parola, caratteriDaSostituire)
                        AggiornaSuggerimentiTouch()
                    End Sub)
    End Sub

    Private Sub TastieraTouch_Chiudi()
        SuUIBrowser(Sub()
                        _tastieraChiusaDallUtente = True
                        NascondiTastieraTouch()
                    End Sub)
    End Sub

    Private Sub TastieraTouch_Dettato(testo As String)
        SuUIBrowser(Async Sub()
                        If _attiva Is Nothing Then Return
                        Await _attiva.InserisciTestoDaTouchAsync(testo)
                        AggiornaSuggerimentiTouch()
                    End Sub)
    End Sub

    Private Sub SuUIBrowser(azione As Action)
        Try
            If _chiuso OrElse Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.SuUIBrowser: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Opzioni del campo"

    ' Nel menu opzioni, quando si è in un campo di testo della pagina, in cima ci sono le stesse
    ' funzioni dei campi di WinGabriel.
    Private Sub AggiungiOpzioniCampo(scheda As WGSchedaBrowser)
        Try
            If scheda Is Nothing OrElse Not scheda.InCampoDiTesto Then Return
            AggiungiOpzione("Modifica il testo in un campo di WinGabriel", AddressOf scheda.ModificaCampoConWinGabriel)
            If WGDettatura.SistemaSupportato Then AggiungiOpzione("Dettatura", AddressOf scheda.DettaNelCampo)
            If Not scheda.InCampoPassword Then
                AggiungiOpzione("Traduci il testo del campo", AddressOf scheda.TraduciTestoDelCampo)
                AggiungiOpzione("Sostituisci il testo con la traduzione " & WGTraduttore.ALingua(WGTraduttore.LinguaDestinazione), AddressOf scheda.SostituisciConTraduzione)
                AggiungiOpzione("Copia il testo del campo", AddressOf scheda.CopiaTestoDelCampo)
                AggiungiOpzione("Taglia il testo del campo", AddressOf scheda.TagliaTestoDelCampo)
            End If
            AggiungiOpzione("Incolla", AddressOf scheda.IncollaNelCampo)
            AggiungiOpzione("Seleziona tutto il testo", AddressOf scheda.SelezionaTuttoIlCampo)
            AggiungiOpzione("Cancella tutto il testo", AddressOf scheda.CancellaTuttoIlCampo)
            AggiungiOpzione("Leggi il contenuto del campo", AddressOf scheda.LeggiIlCampo)
            If _toukBrowser IsNot Nothing AndAlso _toukBrowser.TouchAttivo Then
                AggiungiOpzione("Mostra la tastiera a schermo", Sub()
                                                                   _tastieraChiusaDallUtente = False
                                                                   AggiornaTastieraTouch()
                                                               End Sub)
            End If
            AggiungiOpzione("Esci dal campo, Esc", AddressOf scheda.EsciDalCampoDaOpzioni)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiungiOpzioniCampo: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
