' ============================================================================================
' WGPlayerFormInt - il riproduttore multimediale di WinGabriel
' ============================================================================================
'
' A cosa serve
'   E' la schermata che riproduce musica, video, radio, canali TV e video di YouTube, tutta
'   comandata da tastiera e vocalizzata. Tiene l'elenco degli elementi da riprodurre (uno o
'   molti), legge il titolo di quello in corso, gestisce volume, velocita', scorrimento,
'   ripetizione, riproduzione casuale, capitoli dei video e il menu delle opzioni con Alt.
'   Il suono lo mette WGMpv (o il lettore YouTube), qui c'e' il comando e la vocalizzazione.
'
' Come si usa
'   Dim lettore As New WGPlayerFormInt()
'   lettore.AggiungiElemento("C:\musicarano.mp3")
'   lettore.AggiungiElemento("https://www.youtube.com/watch?v=xxxxxxxxxxx")
'   AddHandler lettore.chiusura, AddressOf QuandoSiChiude
'   lettore.avvia("Musica")                  ' mostra la schermata dentro il contenitore
'
' Metodi ed eventi pubblici principali
'   AggiungiElemento  aggiunge un file, un indirizzo di radio, un canale TV o un video YouTube
'                     (riconosce da solo il tipo se non viene indicato); con un elenco di
'                     riproduzione .m3u o .pls aggiunge tutte le voci.
'   avvia             mostra il riproduttore; con dialogo:=True non torna finche' non si chiude.
'   AvviaAscoltoSottofondo  riproduce un solo elemento senza mostrare nulla.
'   ImpostaProviderCapitoli  collega chi sa recuperare capitoli e segmenti sponsor dei video.
'   aggiungiopzione / rimuoviopzione / opzioneesiste  aggiungono voci al menu delle opzioni.
'   chiusura, chiudiplayer, ErroreRadio  eventi di chiusura e di errore.
'
' Errori
'   Nessun metodo solleva eccezioni: i problemi finiscono nel log (dati\logs\log.txt) e vengono
'   annunciati a voce all'utente quando ha senso ("Impossibile riprodurre", "Nessun capitolo
'   disponibile", ...). Se un elemento non parte si passa da soli al successivo.
'
' Attenzione
'   Il riproduttore vive dentro WGContenitore: senza contenitore attivo avvia() non mostra
'   nulla. I tasti arrivano dalla tastiera globale (WGTastiera) e vengono elaborati solo quando
'   la schermata e' quella in primo piano. Gli eventi del lettore arrivano da thread di sfondo e
'   vengono riportati sul thread dell'interfaccia con BeginInvoke: i metodi pubblici vanno
'   comunque chiamati dal thread dell'interfaccia. I titoli dei video YouTube sono recuperati
'   con yt-dlp: se manca, il titolo resta l'indirizzo ma la riproduzione funziona lo stesso.
' ============================================================================================
Imports System.IO
Imports System.Net
Imports System.Threading
Imports System.Windows.Forms
Imports System.Diagnostics
Imports System.Linq
Imports wgcontrols
Imports System.Security
Imports System.Threading.Tasks
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Text
Imports WGDati
Imports WGDb
Public Class ErroreRadioPlayerEventArgs
    Inherits EventArgs
    Public ReadOnly Property Url As String
    Public ReadOnly Property Nome As String
    Public ReadOnly Property Motivo As String
    Public Sub New(u As String, n As String, m As String)
        Url = If(u, String.Empty)
        Nome = If(n, String.Empty)
        Motivo = If(m, String.Empty)
    End Sub
End Class

Public Class CapitoloInfo
    Public Property Title As String
    Public Property StartSeconds As Double
    Public Property EndSeconds As Double
End Class

Public Class SponsorSegmentoInfo
    Public Property Category As String
    Public Property Title As String
    Public Property StartSeconds As Double
    Public Property EndSeconds As Double
End Class

Public Interface ICapitoliProvider
    Function GetCapitoli(url As String, includiSponsorBlock As Boolean) As (Capitoli As List(Of CapitoloInfo), Sponsor As List(Of SponsorSegmentoInfo))
End Interface

''' <summary>Un commento di un video, con le risposte.</summary>
Public Class CommentoInfo
    Public Property Autore As String = ""
    Public Property Testo As String = ""
    Public Property Data As String = ""
    Public Property MiPiace As Long
    Public Property InEvidenza As Boolean
    Public Property DelCanale As Boolean
    Public Property Risposte As New List(Of CommentoInfo)
End Class

''' <summary>Chi sa leggere i commenti di un video e metterci mi piace o non mi piace (per YouTube, con l'account del browser di WinGabriel).</summary>
Public Interface IInterazioniVideoProvider
    ''' <summary>I commenti del video; Nothing se non è stato possibile leggerli (UltimoErrore dice perché).</summary>
    Function GetCommenti(url As String) As List(Of CommentoInfo)
    ''' <summary>reazione: "mipiace", "nonmipiace" oppure "togli". Falso se non è riuscito (UltimoErrore dice perché).</summary>
    Function Reagisci(url As String, reazione As String) As Boolean
    ReadOnly Property UltimoErrore As String
End Interface

Public Class WGPlayerFormInt
    Inherits UserControl

    Public Event chiudiplayer()

    Private pausa As Boolean = False

    Private WithEvents Menuopzioni As WGListaView
    Private WithEvents domanda As WGDomanda
    Private tempFolder As String = ""

    Private _mpv As WGMpv
    Private _ytPlayer As WGYoutubePlayer

    Private _mediaPlayerStateTimer As System.Windows.Forms.Timer

    ' Sorveglianza della fine dell'elemento. Non ci si affida solo alla segnalazione di mpv:
    ' se quella non arriva, o arriva in una forma che il filtro scarta, il brano finisce e
    ' non parte il successivo. Qui la fine si riconosce guardando la posizione, che e' il
    ' fatto vero e non un messaggio che puo' perdersi.
    Private _posizioneTickPrecedente As Double = -1.0
    Private _tickPosizioneFerma As Integer = 0
    Private _eraVicinoAllaFine As Boolean = False
    Private _fineGiaGestita As Boolean = False

    Private elementi As New System.Collections.Generic.List(Of elemento)
    Private currentIndex As Integer = -1
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private menuopzioniaperto As Boolean = False
    Private _wasPlayingBeforeOptions As Boolean = False
    Private _generatoreCasuale As New System.Random()
    Private _elementiCasualiOriginali As New System.Collections.Generic.List(Of elemento)
    Private _pendingElement As elemento

    Private _totalLength As Long = 0
    Private tempoUltimoSeek As DateTime = DateTime.MinValue
    Private _ytVelocita As Double = 1.0
    Private _ultimoSpazioInt As DateTime = DateTime.MinValue
    Private _youtubeProntoLetto As Boolean = False
    Private _leggiTitoloInSospeso As Boolean = False

    Private _transizioneInCorso As Boolean = False
    Private _inizioTransizione As DateTime = DateTime.MinValue
    Private _avvioRinviato As Boolean = False
    Private _mediaProntoInAttesa As Boolean = False
    Private _volumeCorrente As Integer = 100
    ' Serviva a non impazzire se il tasto restava premuto, ma settecento millisecondi sono
    ' un'eternita' quando si scorre un elenco di brani cercandone uno.
    Private Const INTERVALLO_MINIMO_CAMBIO_MS As Double = 220.0

    Private _tempoUltimoCambioElemento As DateTime = DateTime.MinValue
    Private _menuSecondario As WGMenu

    Private _ctrlSinistroGiu As Boolean = False
    Private _timerAvvisoRiconnessione As System.Windows.Forms.Timer
    Private Const MILLISECONDI_SOGLIA_AVVISO_RICONNESSIONE As Integer = 3000

    Private _chiusuraInCorso As Boolean = False
    Private _tempoUltimoAvvioRiproduzione As DateTime = DateTime.MinValue

    Private _attesaLetturaAttiva As Boolean = False
    Private _letturaIniziata As Boolean = False
    Private _inizioAttesaLettura As DateTime = DateTime.MinValue
    Private _tmrAttesaLettura As System.Windows.Forms.Timer

    Private _tmrAttesaTitolo As System.Windows.Forms.Timer
    Private _attesaTitoloUrl As String = Nothing
    Private _inizioAttesaTitolo As DateTime = DateTime.MinValue

    Private _tmrRipristinaPausa As System.Windows.Forms.Timer

    Private _tmrVigilanzaPausa As System.Windows.Forms.Timer
    Private _pausaRichiesta As Boolean = False
    Private _fineVigilanzaPausa As DateTime = DateTime.MinValue
    Private _fineAnnunciata As Boolean = False
    Private _barraTocco As WGToccoPlayer

    Private _titoliYoutube As New System.Collections.Generic.Dictionary(Of String, String)
    Private _titoliInCorso As New System.Collections.Generic.List(Of String)
    Private ReadOnly _lockTitoli As New Object()

    Private _elementoPreferito As Boolean = False

    Private _provider As ICapitoliProvider
    Private _interazioni As IInterazioniVideoProvider
    Private _listaCommenti As WGMenuLista
    Private _commentiMostrati As New List(Of CommentoInfo)()
    Private _listaCapitoli As WGMenuLista
    Private _listaBrani As WGMenuLista
    Private ReadOnly _lockCapitoli As New Object()
    Private ReadOnly _capitoliPerVideo As New System.Collections.Generic.Dictionary(Of String, List(Of CapitoloInfo))
    Private ReadOnly _sponsorPerVideo As New System.Collections.Generic.Dictionary(Of String, List(Of SponsorSegmentoInfo))
    Private ReadOnly _capitoliInCorso As New System.Collections.Generic.List(Of String)
    Private _ultimoIndiceCapitoloAnnunciato As Integer = -1
    Private _ultimoSegmentoSponsorSaltato As SponsorSegmentoInfo = Nothing

    Private ReadOnly _estensioniVideo As String() = {".avi", ".mp4", ".mkv", ".mov", ".wmv", ".mpg", ".mpeg", ".m4v", ".flv", ".webm", ".ts", ".vob", ".3gp", ".ogv", ".m2ts", ".divx", ".rmvb"}

    Public Event chiusura As EventHandler
    Public Event ErroreRadio As EventHandler(Of ErroreRadioPlayerEventArgs)

    Private ReadOnly _logger As Logger
    Private processoincorso As WGProcessoInCorso
    Private tmrTimeoutCaricamento As System.Windows.Forms.Timer

    Private _attesaLettorePronto As Boolean = False
    Private _inizioAttesaLettore As DateTime = DateTime.MinValue
    Private _ripristinoLettoreAvviato As Boolean = False
    Private tmrAttesaLettore As System.Windows.Forms.Timer

    Private Function LettoreInPreparazione() As Boolean
        Try
            If _attesaLettorePronto Then Return True
            If WGMpv.RipristinoLibreriaInCorso Then Return True
            Return False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub ChiudiProcessoCaricamento(Optional forza As Boolean = False)
        Try
            If Not forza AndAlso LettoreInPreparazione() Then
                If tmrTimeoutCaricamento IsNot Nothing Then
                    tmrTimeoutCaricamento.Stop()
                End If
                Return
            End If

            If tmrTimeoutCaricamento IsNot Nothing Then
                tmrTimeoutCaricamento.Stop()
            End If
            If processoincorso IsNot Nothing Then
                RemoveHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                processoincorso.Fine()
                processoincorso = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaTimeoutCaricamento()
        Try
            If tmrTimeoutCaricamento Is Nothing Then
                tmrTimeoutCaricamento = New System.Windows.Forms.Timer()
                tmrTimeoutCaricamento.Interval = 45000
                AddHandler tmrTimeoutCaricamento.Tick, AddressOf TimeoutCaricamentoTick
            End If
            tmrTimeoutCaricamento.Stop()
            tmrTimeoutCaricamento.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub TimeoutCaricamentoTick(sender As Object, e As EventArgs)
        Try
            tmrTimeoutCaricamento.Stop()

            If LettoreInPreparazione() Then
                tmrTimeoutCaricamento.Start()
                Return
            End If

            _transizioneInCorso = False
            _avvioRinviato = False
            _mediaProntoInAttesa = False
            If processoincorso IsNot Nothing Then
                ChiudiProcessoCaricamento(True)
                LeggiStatoTTS("Il caricamento sta impiegando più tempo del previsto. I comandi del riproduttore sono di nuovo attivi.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AssicuraProcessoCaricamento(messaggio As String)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            If processoincorso Is Nothing Then
                NascondiBarraTocco()
                processoincorso = New WGProcessoInCorso()
                AddHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                processoincorso.Avvia(messaggio, True)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaAttesaLettore(file As elemento)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            _pendingElement = file
            _attesaLettorePronto = True
            _inizioAttesaLettore = DateTime.Now

            AssicuraProcessoCaricamento("Sto preparando il componente di riproduzione, attendi")

            If tmrTimeoutCaricamento IsNot Nothing Then
                tmrTimeoutCaricamento.Stop()
            End If

            If Not _ripristinoLettoreAvviato Then
                _ripristinoLettoreAvviato = True
                Task.Run(New Func(Of Boolean)(AddressOf RipristinaLettoreInSottofondo))
            End If

            If tmrAttesaLettore Is Nothing Then
                tmrAttesaLettore = New System.Windows.Forms.Timer()
                tmrAttesaLettore.Interval = 700
                AddHandler tmrAttesaLettore.Tick, AddressOf AttesaLettoreTick
            End If
            tmrAttesaLettore.Stop()
            tmrAttesaLettore.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function RipristinaLettoreInSottofondo() As Boolean
        Try
            Return WGMpv.AssicuraLibreriaMpv(True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AttesaLettoreTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then
                FermaAttesaLettore()
                Return
            End If

            If WGMpv.RiavvioRichiesto Then
                FermaAttesaLettore()
                ChiudiProcessoCaricamento(True)
                _transizioneInCorso = False
                _avvioRinviato = False
                _mediaProntoInAttesa = False
                LeggiStatoTTS("Il componente di riproduzione è stato aggiornato. Chiudi e riapri WinGabriel per usarlo.")
                Return
            End If

            If WGMpv.RipristinoLibreriaInCorso Then Return

            Dim lettore As WGMpv = WGMpv.GetInstance()
            If lettore IsNot Nothing AndAlso lettore.MotoreAttivo Then
                FermaAttesaLettore()
                _ripristinoLettoreAvviato = False
                LeggiStatoTTS("Componente di riproduzione pronto.")
                AvviaRiproduzioneReale(_pendingElement)
                Return
            End If

            If DateTime.Now.Subtract(_inizioAttesaLettore).TotalSeconds > 300 Then
                FermaAttesaLettore()
                ChiudiProcessoCaricamento(True)
                _transizioneInCorso = False
                _avvioRinviato = False
                _mediaProntoInAttesa = False
                LeggiStatoTTS("Non riesco a preparare il componente di riproduzione. Controlla il collegamento a internet.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub FermaAttesaLettore()
        Try
            _attesaLettorePronto = False
            If tmrAttesaLettore IsNot Nothing Then
                tmrAttesaLettore.Stop()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private _ripetiTraccia As Boolean
    Private _ripetiPlaylist As Boolean
    Private _riproduzioneCasuale As Boolean
    Private _primoAvvioAvi As Boolean = True

    Private _clsBrani As New CLSBrani()
    Private _config As New WGConfig()

    Private Const WM_APPCOMMAND As Integer = &H319
    Private Const APPCOMMAND_MEDIA_NEXTTRACK As Integer = 11
    Private Const APPCOMMAND_MEDIA_PREVIOUSTRACK As Integer = 12
    Private Const APPCOMMAND_MEDIA_PLAY_PAUSE As Integer = 14

    Private Shared _istanzaCorrente As WGPlayerFormInt = Nothing

    Private WithEvents _tastiera As WGTastiera
    Private _isHookActive As Boolean = False

    Private Class opzione
        Public nome As String
        Public evento As EventHandler
    End Class
    Private _listaOpzioni As New List(Of opzione)
    Private _opzioniEsterne As New List(Of opzione)

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function WNetGetConnection(
        <MarshalAs(UnmanagedType.LPTStr)> ByVal localName As String,
        <MarshalAs(UnmanagedType.LPTStr)> ByVal remoteName As StringBuilder,
        ByRef length As Integer) As Integer
    End Function

    Private Function IsYoutubeCorrente() As Boolean
        Return currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso elementi(currentIndex).tipo = ElementType.YouTube
    End Function

    Private Function IsYoutubeConBrowser() As Boolean
        Return IsYoutubeCorrente()
    End Function

    Private Function IsYoutubeConMpv() As Boolean
        Return False
    End Function

    Private Function IsWebRadioCorrente() As Boolean
        Return currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso elementi(currentIndex).tipo = ElementType.WebRadio
    End Function

    Private Function IsStreamTvCorrente() As Boolean
        Return currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso elementi(currentIndex).tipo = ElementType.StreamTv
    End Function

    ''' <summary>True se l'elemento corrente è live (radio o TV) e non supporta seek/durata.</summary>
    Private Function IsLiveSenzaSeek() As Boolean
        Return IsWebRadioCorrente() OrElse IsStreamTvCorrente()
    End Function

    Private Function IsUrlStreamTv(url As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False
            Dim u = url.Trim().ToLowerInvariant()
            If u.Contains("mediapolis.rai.it") OrElse u.Contains("mediapolisevent.rai.it") Then Return True
            If u.Contains("mediaset") AndAlso (u.Contains(".m3u8") OrElse u.Contains(".isml") OrElse u.Contains("cdn.mediaset")) Then Return True
            If u.Contains("msf.cdn.mediaset") OrElse u.Contains("msr.cdn.mediaset") Then Return True
            If u.EndsWith(".m3u8") OrElse u.Contains(".m3u8?") Then Return True
            If u.Contains(".isml/") OrElse u.EndsWith(".mpd") OrElse u.Contains(".mpd?") Then Return True
            If u.StartsWith("rtmp://") OrElse u.StartsWith("rtsp://") OrElse u.StartsWith("mms://") Then Return True
            Return False
        Catch
            Return False
        End Try
    End Function

    Public Sub New()
        Try
            _logger = New Logger()
            InitializeComponent()

            Me.Dock = DockStyle.Fill
            Me.BackColor = WGTema.Sfondo
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema

            tempFolder = WGDatabase.CartellaDati("player")

            RegistraInterruzioneAscolto()

            _tastiera = WGTastiera.GetInstance()
        Catch ex As Exception
            If _logger IsNot Nothing Then _logger.ScriviLog(ex.ToString())
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
            If _logger IsNot Nothing Then _logger.ScriviLog("OnCambiatoTema: " & ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
        Catch ex As Exception
            If _logger IsNot Nothing Then _logger.ScriviLog("Dispose: " & ex.ToString())
        End Try
        MyBase.Dispose(disposing)
    End Sub

    Private Sub AttivaHook()
        Try
            _isHookActive = True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub DisattivaHook()
        Try
            _isHookActive = False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroPremuto
        Try
            _ctrlSinistroGiu = True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroRilasciato
        Try
            _ctrlSinistroGiu = False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function PuoElaborareInputPlayer() As Boolean
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return False
            If Not Me.IsHandleCreated Then Return False
            If Not Me.Visible Then Return False

            Dim foregroundWnd As IntPtr = GetForegroundWindow()

            Try
                If _barraTocco IsNot Nothing AndAlso Not _barraTocco.IsDisposed AndAlso _barraTocco.EFinestraDelVelo(foregroundWnd) Then
                    Dim contenitoreVelo As WGContenitore = WGContenitore.Istanza
                    If contenitoreVelo IsNot Nothing AndAlso Not contenitoreVelo.IsDisposed AndAlso contenitoreVelo.IsHandleCreated Then
                        foregroundWnd = contenitoreVelo.Handle
                    Else
                        foregroundWnd = Me.Handle
                    End If
                End If
            Catch exVelo As Exception
                _logger.ScriviLog("PuoElaborareInputPlayer (velo): " & exVelo.Message)
            End Try

            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                    If foregroundWnd = contenitore.Handle Then
                        Dim schermata As Control = Me
                        While schermata.Parent IsNot Nothing AndAlso Not (TypeOf schermata.Parent Is WGContenitore)
                            schermata = schermata.Parent
                        End While
                        Return contenitore.PuoRicevereInput(schermata)
                    End If
                End If
            Catch exCont As Exception
                _logger.ScriviLog("PuoElaborareInputPlayer (verifica contenitore): " & exCont.Message)
            End Try

            Return foregroundWnd = Me.Handle
        Catch ex As Exception
            _logger.ScriviLog("PuoElaborareInputPlayer: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None Then Return
            If Not PuoElaborareInputPlayer() Then Return

            If menuopzioniaperto Then
                Select Case tasto
                    Case Keys.Enter, Keys.Return
                        Me.BeginInvoke(New Action(Sub() SelezionaOpzioneCorrente()))
                    Case Keys.Escape
                        Me.BeginInvoke(New Action(Sub() chiudiopzioni()))
                End Select
                Return
            End If

            If Not _isHookActive Then
                If IsYoutubeConBrowser() AndAlso tasto = Keys.Escape AndAlso (_ytPlayer Is Nothing OrElse _ytPlayer.IsDisposed) Then
                    Me.BeginInvoke(New Action(Sub() chiudi(Nothing, Nothing)))
                End If
                Return
            End If

            Select Case tasto
                Case Keys.F1, Keys.Escape, Keys.B, Keys.F, Keys.T, Keys.R, Keys.D, Keys.V, Keys.F10,
                     Keys.Right, Keys.Left, Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown,
                     Keys.Space, Keys.Home, Keys.End
                    Dim tastoLocale As Keys = tasto
                    Dim controlloLocale As Boolean = _ctrlSinistroGiu
                    Me.BeginInvoke(New Action(Sub() GestisciComandoPlayer(tastoLocale, controlloLocale)))
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciComandoPlayer(tasto As Keys, conControllo As Boolean)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If menuopzioniaperto Then Return

            Select Case tasto
                Case Keys.F1
                    WGGuida.MostraGuidaPlayer()
                Case Keys.Escape
                    chiudi(Nothing, Nothing)
                Case Keys.B
                    If conControllo AndAlso elementi.Count > 1 Then RiproduciElementoPrecedente()
                Case Keys.F
                    If conControllo AndAlso elementi.Count > 1 Then RiproduciProssimoElemento(True)
                Case Keys.T
                    If conControllo Then LeggiTempoTrascorso()
                Case Keys.R
                    If conControllo Then LeggiTempoRimanente()
                Case Keys.D
                    If conControllo Then LeggiDurataTotale()
                Case Keys.V
                    If conControllo Then LeggiVolumeCorrente()
                Case Keys.Right
                    OnRightPressed()
                Case Keys.Left
                    OnLeftPressed()
                Case Keys.Up
                    OnUpPressed()
                Case Keys.Down
                    OnDownPressed()
                Case Keys.PageUp
                    OnPageUpPressed()
                Case Keys.PageDown
                    OnPageDownPressed()
                Case Keys.Space
                    OnSpacePressed()
                Case Keys.Home
                    OnHomePressed()
                Case Keys.End
                    OnEndPressed()
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub _ytPlayer_tastopremuto(tasto As Keys, conControllo As Boolean)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If menuopzioniaperto Then Return

            Dim tastoLocale As Keys = tasto
            Dim controlloLocale As Boolean = conControllo
            Me.BeginInvoke(New Action(Sub() GestisciComandoPlayer(tastoLocale, controlloLocale)))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Not _isHookActive AndAlso Not IsYoutubeConBrowser() Then Return
            If Not PuoElaborareInputPlayer() Then Return

            Me.BeginInvoke(New Action(Sub() apriopzioni()))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = WM_APPCOMMAND Then
                If Not menuopzioniaperto Then
                    Dim cmd As Integer = (m.LParam.ToInt32() >> 16) And &HFFFF
                    If cmd = APPCOMMAND_MEDIA_NEXTTRACK Then
                        Me.BeginInvoke(New Action(Sub() RiproduciProssimoElemento(True)))
                        m.Result = IntPtr.Zero
                        Return
                    ElseIf cmd = APPCOMMAND_MEDIA_PREVIOUSTRACK Then
                        Me.BeginInvoke(New Action(Sub() RiproduciElementoPrecedente()))
                        m.Result = IntPtr.Zero
                        Return
                    ElseIf cmd = APPCOMMAND_MEDIA_PLAY_PAUSE Then
                        Me.BeginInvoke(New Action(Sub() OnSpacePressed()))
                        m.Result = IntPtr.Zero
                        Return
                    End If
                End If
            End If
            MyBase.WndProc(m)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub InitializeComponent()
        Try
            Me.Size = New Size(800, 600)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub InizializzaConfigurazioni()
        Try
            If Not _config.EsisteId("volumedipartenza") Then _config.SalvaValore("volumedipartenza", "100")
            If Not _config.EsisteId("memorizzavolume") Then _config.SalvaValore("memorizzavolume", "true")
            If Not _config.EsisteId("passovolume") Then _config.SalvaValore("passovolume", "5")
            If Not _config.EsisteId("passoscorrimento") Then _config.SalvaValore("passoscorrimento", "30")
            If Not _config.EsisteId("passoscorrimentolungo") Then _config.SalvaValore("passoscorrimentolungo", "60")
            If Not _config.EsisteId("ricordaposizione") Then _config.SalvaValore("ricordaposizione", "false")
            If Not _config.EsisteId("leggititolo") Then _config.SalvaValore("leggititolo", "true")
            If Not _config.EsisteId("leggistato") Then _config.SalvaValore("leggistato", "true")
            If Not _config.EsisteId("leggidettaglibbrano") Then _config.SalvaValore("leggidettaglibbrano", "false")
            If Not _config.EsisteId("attendititolo") Then _config.SalvaValore("attendititolo", "false")
            If Not _config.EsisteId("avvioautomatico") Then _config.SalvaValore("avvioautomatico", "true")
            If Not _config.EsisteId("passovelocita") Then _config.SalvaValore("passovelocita", "0.1")
            If Not _config.EsisteId("ultimoVolume") Then _config.SalvaValore("ultimoVolume", "100")
            If Not _config.EsisteId("sogliafinebrano") Then _config.SalvaValore("sogliafinebrano", "10")
            If Not _config.EsisteId("sponsorblock_abilitato") Then _config.SalvaValore("sponsorblock_abilitato", "false")
            If Not _config.EsisteId("sponsorblock_annuncio") Then _config.SalvaValore("sponsorblock_annuncio", "false")
            If Not _config.EsisteId("lettura_capitoli_automatica") Then _config.SalvaValore("lettura_capitoli_automatica", "false")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LeggiConfigBool(id As String, defaultVal As Boolean) As Boolean
        Try
            Dim val = _config.LeggiValore(id)
            If String.IsNullOrEmpty(val) Then Return defaultVal
            Return val.ToLower() = "true" OrElse val = "1"
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return defaultVal
        End Try
    End Function

    Private Function LeggiConfigInt(id As String, defaultVal As Integer) As Integer
        Try
            Dim val = _config.LeggiValore(id)
            Dim res As Integer
            If Integer.TryParse(val, res) Then Return res
            Return defaultVal
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return defaultVal
        End Try
    End Function

    Private Function LeggiConfigDouble(id As String, defaultVal As Double) As Double
        Try
            Dim val = _config.LeggiValore(id)
            Dim res As Double
            If Double.TryParse(val, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, res) Then Return res
            Return defaultVal
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return defaultVal
        End Try
    End Function

    Private Sub LeggiStatoTTS(testo As String)
        Try
            If LeggiConfigBool("leggistato", True) AndAlso lettura IsNot Nothing Then
                lettura.LeggiTesto(testo)
            End If
            MostraSottotitoloSchermo(testo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraSottotitoloSchermo(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                contenitore.MostraSottotitolo(testo)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LeggiSempreTTS(testo As String)
        Try
            If lettura Is Nothing Then Return
            lettura.LeggiTesto(testo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function ArrayContiene(elenco As String(), valore As String) As Boolean
        Try
            If elenco Is Nothing OrElse valore Is Nothing Then Return False
            Return Array.IndexOf(elenco, valore) >= 0
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function IsElementoVideo(el As elemento) As Boolean
        Try
            If el.tipo = ElementType.YouTube Then Return True
            If el.tipo = ElementType.StreamTv Then Return True
            If el.tipo = ElementType.WebRadio Then Return False
            Dim est As String = System.IO.Path.GetExtension(el.path)
            If String.IsNullOrEmpty(est) Then Return False
            Return ArrayContiene(_estensioniVideo, est.ToLower())
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function DescriviSecondi(totaleSecondi As Integer) As String
        Try
            If totaleSecondi <= 0 Then Return "zero secondi"

            Dim ore As Integer = totaleSecondi \ 3600
            Dim minuti As Integer = (totaleSecondi Mod 3600) \ 60
            Dim secondi As Integer = totaleSecondi Mod 60

            Dim parti As New List(Of String)
            If ore > 0 Then
                If ore = 1 Then
                    parti.Add("un'ora")
                Else
                    parti.Add(ore.ToString() & " ore")
                End If
            End If
            If minuti > 0 Then
                If minuti = 1 Then
                    parti.Add("un minuto")
                Else
                    parti.Add(minuti.ToString() & " minuti")
                End If
            End If
            If secondi > 0 Then
                If secondi = 1 Then
                    parti.Add("un secondo")
                Else
                    parti.Add(secondi.ToString() & " secondi")
                End If
            End If

            If parti.Count = 0 Then Return "zero secondi"
            If parti.Count = 1 Then Return parti(0)

            Dim risultato As New StringBuilder()
            For i As Integer = 0 To parti.Count - 1
                If i = 0 Then
                    risultato.Append(parti(i))
                ElseIf i = parti.Count - 1 Then
                    risultato.Append(" e ")
                    risultato.Append(parti(i))
                Else
                    risultato.Append(", ")
                    risultato.Append(parti(i))
                End If
            Next
            Return risultato.ToString()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return totaleSecondi.ToString() & " secondi"
        End Try
    End Function

    Private Function DescriviDurata(durata As TimeSpan) As String
        Try
            If durata.TotalSeconds <= 0 Then Return "zero secondi"
            Return DescriviSecondi(CInt(Math.Floor(durata.TotalSeconds)))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "zero secondi"
        End Try
    End Function

    Private Function TempoDisponibileCorrente() As Boolean
        Try
            If IsLiveSenzaSeek() Then Return False
            If IsYoutubeConBrowser() Then
                If _ytPlayer Is Nothing Then Return False
                Return _ytPlayer.TempoDisponibile
            End If
            If _mpv Is Nothing Then Return False
            Return _mpv.Durata.TotalSeconds > 0
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function DurataCorrente() As TimeSpan
        Try
            If IsYoutubeConBrowser() Then
                If _ytPlayer Is Nothing Then Return TimeSpan.Zero
                Return _ytPlayer.Durata
            End If
            If _mpv Is Nothing Then Return TimeSpan.Zero
            Return _mpv.Durata
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return TimeSpan.Zero
        End Try
    End Function

    Private Function PosizioneCorrente() As TimeSpan
        Try
            If IsYoutubeConBrowser() Then
                If _ytPlayer Is Nothing Then Return TimeSpan.Zero
                Return _ytPlayer.PosizioneTemporale
            End If
            If _mpv Is Nothing Then Return TimeSpan.Zero
            Return _mpv.PosizioneTemporale
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return TimeSpan.Zero
        End Try
    End Function

    Private Function SecondiRimanenti() As Double
        Try
            If IsLiveSenzaSeek() Then Return -1
            If Not TempoDisponibileCorrente() Then Return -1

            Dim durata As Double = DurataCorrente().TotalSeconds
            If durata <= 0 Then Return -1

            Dim posizione As Double = PosizioneCorrente().TotalSeconds
            Dim rimanenti As Double = durata - posizione
            If rimanenti < 0 Then rimanenti = 0
            Return rimanenti
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return -1
        End Try
    End Function

    Private Function EsistonoElementiSuccessivi() As Boolean
        Try
            If elementi.Count = 0 Then Return False
            If _ripetiTraccia Then Return True
            If elementi.Count > 1 AndAlso (_ripetiPlaylist OrElse _riproduzioneCasuale) Then Return True
            Return currentIndex >= 0 AndAlso currentIndex < elementi.Count - 1
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub LeggiTempoTrascorso()
        Try
            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiSempreTTS("Il canale è in diretta, non c'è un tempo trascorso.")
                Else
                    LeggiSempreTTS("La radio è in diretta, non c'è un tempo trascorso.")
                End If
                Return
            End If
            If Not TempoDisponibileCorrente() Then
                LeggiSempreTTS("Il tempo non è ancora disponibile.")
                Return
            End If

            LeggiSempreTTS("Tempo trascorso: " & DescriviDurata(PosizioneCorrente()) & ".")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LeggiTempoRimanente()
        Try
            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiSempreTTS("Il canale è in diretta, non c'è un tempo rimanente.")
                Else
                    LeggiSempreTTS("La radio è in diretta, non c'è un tempo rimanente.")
                End If
                Return
            End If

            Dim rimanenti As Double = SecondiRimanenti()
            If rimanenti < 0 Then
                LeggiSempreTTS("La durata non è ancora disponibile.")
                Return
            End If

            If rimanenti < 1 Then
                LeggiSempreTTS("Siamo alla fine.")
                Return
            End If

            LeggiSempreTTS("Tempo rimanente: " & DescriviSecondi(CInt(Math.Floor(rimanenti))) & ".")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LeggiDurataTotale()
        Try
            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiSempreTTS("Il canale è in diretta, non ha una durata.")
                Else
                    LeggiSempreTTS("La radio è in diretta, non ha una durata.")
                End If
                Return
            End If

            Dim durata As TimeSpan = DurataCorrente()
            If durata.TotalSeconds <= 0 Then
                LeggiSempreTTS("La durata non è ancora disponibile.")
                Return
            End If

            LeggiSempreTTS("Durata totale: " & DescriviDurata(durata) & ".")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LeggiTempoTrascorsoMenu(sender As Object, e As EventArgs)
        LeggiTempoTrascorso()
    End Sub

    Private Sub LeggiTempoRimanenteMenu(sender As Object, e As EventArgs)
        LeggiTempoRimanente()
    End Sub

    Private Sub LeggiDurataTotaleMenu(sender As Object, e As EventArgs)
        LeggiDurataTotale()
    End Sub

    Private Sub SalvaPosizioneAttuale()
        Try
            If Not LeggiConfigBool("ricordaposizione", False) Then Return
            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                Dim isYouTube As Boolean = (elementi(currentIndex).tipo = ElementType.YouTube)
                If Not isYouTube AndAlso _mpv IsNot Nothing Then
                    Dim pos = _mpv.PosizioneTemporale.TotalSeconds
                    If pos > 0 Then
                        Dim hash = Math.Abs(elementi(currentIndex).path.GetHashCode()).ToString()
                        _config.SalvaValore("pos_" & hash, pos.ToString(Globalization.CultureInfo.InvariantCulture))
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImpostaProviderInterazioni(provider As IInterazioniVideoProvider)
        Try
            _interazioni = provider
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaProviderCapitoli(provider As ICapitoliProvider)
        Try
            _provider = provider
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Vero quando l'utente ha già scelto se interrompere la riproduzione in corso o metterla in coda.
    Private _avvioDeciso As Boolean

    ''' <summary>Vero quando il video ha la durata e ci si può spostare (per partire da un punto scelto).</summary>
    Public Function PuoSaltare() As Boolean
        Return TempoDisponibileCorrente()
    End Function

    ''' <summary>Si sposta a un punto del video senza annunciarlo.</summary>
    Public Sub SaltaA(secondi As Double)
        Try
            If Not TempoDisponibileCorrente() Then Return
            Dim durata As Double = DurataCorrente().TotalSeconds
            If durata > 2 AndAlso secondi > durata - 2 Then secondi = durata - 2
            If secondi < 0 Then secondi = 0
            tempoUltimoSeek = DateTime.Now
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.ScorriAssoluto(secondi)
            ElseIf _mpv IsNot Nothing Then
                _mpv.ScorriAssoluto(secondi)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub avvia(titolo As String, Optional dialogo As Boolean = False)
        Try
            ' Un solo file alla volta: se qualcosa suona già, si chiede se riprodurre dopo o interrompere.
            If Not _avvioDeciso Then
                _avvioDeciso = True
                If WGRiproduzione.RiproduzioneInCorso() Then
                    WGRiproduzione.ChiediSeInterrompere(Sub() avvia(titolo, dialogo), titolo)
                    Return
                End If
            End If

            Try
                Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
                If centro IsNot Nothing Then centro.TerminaAscoltoAudio()
            Catch exCentro As Exception
                _logger.ScriviLog(exCentro.ToString())
            End Try

            If _istanzaCorrente IsNot Nothing AndAlso _istanzaCorrente IsNot Me Then
                Try
                    Dim contPrecedente As WGContenitore = WGContenitore.Istanza
                    If contPrecedente IsNot Nothing AndAlso Not contPrecedente.IsDisposed Then
                        contPrecedente.RimuoviControllo(_istanzaCorrente)
                    End If
                    _istanzaCorrente.Visible = False
                    _istanzaCorrente.Dispose()
                Catch ex As Exception
                    _logger.ScriviLog(ex.ToString())
                End Try
                _istanzaCorrente = Nothing
            End If
            _istanzaCorrente = Me

            Dim contenitore As WGContenitore = WGContenitore.Istanza

            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                contenitore.MostraControllo(Me)
                If dialogo Then
                    While Not Me.IsDisposed AndAlso Me.Visible AndAlso Not contenitore.IsDisposed
                        Application.DoEvents()
                        System.Threading.Thread.Sleep(15)
                    End While
                End If
            Else
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Shadows Sub load(sender As Object, e As System.EventArgs) Handles MyBase.Load
        Try
            InizializzaConfigurazioni()

            AggiornaYtDlpInSottofondo()

            _mediaPlayerStateTimer = New System.Windows.Forms.Timer()
            _mediaPlayerStateTimer.Interval = 250
            AddHandler _mediaPlayerStateTimer.Tick, AddressOf OnMediaPlayerStateTimerTick
            _mediaPlayerStateTimer.Start()

            CreaBarraTocco()

            If elementi.Count > 0 AndAlso currentIndex = -1 Then
                RiproduciProssimoElemento(elementi.Count > 1)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Try
                Dim msgBox As New WGMessaggi()
                msgBox.Messaggio("Si è verificato un errore grave durante l'apertura del riproduttore.")
                msgBox.ShowDialog()
            Catch exMsg As Exception
                _logger.ScriviLog(exMsg.ToString())
            End Try
            FinalizzaChiusura()
        End Try
    End Sub

    Private Sub WGPlayerFormInt_VisibleChanged(sender As Object, e As EventArgs) Handles MyBase.VisibleChanged
        Try
            If Me.Visible Then
                If Not menuopzioniaperto Then
                    AttivaHook()
                End If
            Else
                DisattivaHook()
                NascondiBarraTocco()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Shared _ytPlayerSottofondoCorrente As WGYoutubePlayer

    Private Sub DistruggiYoutubeSottofondoPrecedente()
        Try
            If _ytPlayerSottofondoCorrente IsNot Nothing Then
                Try
                    If Not _ytPlayerSottofondoCorrente.IsDisposed Then
                        _ytPlayerSottofondoCorrente.SganciaDaPannelloVideo()
                        _ytPlayerSottofondoCorrente.Dispose()
                    End If
                Catch exVecchio As Exception
                    _logger.ScriviLog(exVecchio.ToString())
                End Try
                _ytPlayerSottofondoCorrente = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub InizializzaYoutube()
        Try
            If _ytPlayer Is Nothing Then
                DistruggiYoutubeSottofondoPrecedente()
                _ytPlayer = New WGYoutubePlayer()
                _ytPlayer.ImpostaVisibilita(False)
                Me.Controls.Add(_ytPlayer)
                AddHandler _ytPlayer.MediaCaricato, AddressOf OnYtMediaCaricato
                AddHandler _ytPlayer.VideoTerminato, AddressOf OnYtVideoTerminato
                AddHandler _ytPlayer.ErroreMedia, AddressOf OnYtErroreMedia
                AddHandler _ytPlayer.VideoNonDisponibile, AddressOf OnYtVideoNonDisponibile
                AddHandler _ytPlayer.altpremuto, AddressOf _ytPlayer_altpremuto
                AddHandler _ytPlayer.escpremuto, AddressOf _ytPlayer_escpremuto
                AddHandler _ytPlayer.tastopremuto, AddressOf _ytPlayer_tastopremuto
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub DistruggiYoutube()
        Try
            If _ytPlayer IsNot Nothing Then
                RemoveHandler _ytPlayer.MediaCaricato, AddressOf OnYtMediaCaricato
                RemoveHandler _ytPlayer.VideoTerminato, AddressOf OnYtVideoTerminato
                RemoveHandler _ytPlayer.ErroreMedia, AddressOf OnYtErroreMedia
                RemoveHandler _ytPlayer.VideoNonDisponibile, AddressOf OnYtVideoNonDisponibile
                RemoveHandler _ytPlayer.altpremuto, AddressOf _ytPlayer_altpremuto
                RemoveHandler _ytPlayer.escpremuto, AddressOf _ytPlayer_escpremuto
                RemoveHandler _ytPlayer.tastopremuto, AddressOf _ytPlayer_tastopremuto
                _ytPlayer.Pausa()
                _ytPlayer.disattivatastiera()
                Me.Controls.Remove(_ytPlayer)
                _ytPlayer.Dispose()
                _ytPlayer = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function InizializzaMpv() As Boolean
        Try
            If _mpv IsNot Nothing AndAlso Not _mpv.MotoreAttivo Then
                DistruggiMpv()
            End If

            If _mpv Is Nothing Then
                _mpv = WGMpv.GetInstance()
                If _mpv Is Nothing OrElse Not _mpv.MotoreAttivo Then
                    _mpv = Nothing
                    Return False
                End If
                _mpv.AllegaAFinestra(IntPtr.Zero)
                AddHandler _mpv.MediaCaricato, AddressOf OnMediaOpened
                AddHandler _mpv.EndFile, AddressOf OnMediaEnded
                AddHandler _mpv.ErroreMedia, AddressOf OnMediaFailed
                AddHandler _mpv.RiconnessioneStream, AddressOf OnRiconnessioneStream
                AddHandler _mpv.ErroreRadio, AddressOf OnErroreRadioMpv
                AddHandler _mpv.CambioProprieta, AddressOf OnMpvCambioProprietaCapitoli
            End If

            Return True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub DistruggiMpv()
        Try
            If _mpv IsNot Nothing Then
                RemoveHandler _mpv.MediaCaricato, AddressOf OnMediaOpened
                RemoveHandler _mpv.EndFile, AddressOf OnMediaEnded
                RemoveHandler _mpv.ErroreMedia, AddressOf OnMediaFailed
                RemoveHandler _mpv.RiconnessioneStream, AddressOf OnRiconnessioneStream
                RemoveHandler _mpv.ErroreRadio, AddressOf OnErroreRadioMpv
                RemoveHandler _mpv.CambioProprieta, AddressOf OnMpvCambioProprietaCapitoli
                _mpv.FermaRiproduzione()
                _mpv = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AgganciaVideoAPannello()
        Try
            If _mpv Is Nothing Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed OrElse Not contenitore.IsHandleCreated Then
                _mpv.AllegaAFinestra(IntPtr.Zero)
                Return
            End If

            Dim handlePannello As IntPtr = contenitore.OttieniHandlePannelloVideo()
            If handlePannello = IntPtr.Zero Then
                _mpv.AllegaAFinestra(IntPtr.Zero)
                Return
            End If

            contenitore.MostraPannelloVideo()
            _mpv.AllegaAFinestra(handlePannello)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AgganciaYoutubeSottofondoAPannello()
        Try
            If _ytPlayer Is Nothing Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed OrElse Not contenitore.IsHandleCreated Then Return

            Dim handlePannello As IntPtr = contenitore.OttieniHandlePannelloVideo()
            If handlePannello = IntPtr.Zero Then Return

            contenitore.MostraPannelloVideo()
            _ytPlayer.AgganciaAPannelloVideo(handlePannello)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SganciaVideoDaPannello()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                contenitore.NascondiPannelloVideo()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnRiconnessioneStream(sender As Object, e As RiconnessioneStreamEventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnRiconnessioneStream(sender, e)))
                Return
            End If
            If e Is Nothing Then Return
            If e.Tentativo = 1 Then
                AvviaTimerAvvisoRiconnessione()
            ElseIf e.Tentativo >= e.MaxTentativi Then
                FermaTimerAvvisoRiconnessione()
                LeggiStatoTTS("Impossibile riconnettersi allo stream.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaTimerAvvisoRiconnessione()
        Try
            FermaTimerAvvisoRiconnessione()
            _timerAvvisoRiconnessione = New System.Windows.Forms.Timer()
            _timerAvvisoRiconnessione.Interval = MILLISECONDI_SOGLIA_AVVISO_RICONNESSIONE
            AddHandler _timerAvvisoRiconnessione.Tick, AddressOf TimerAvvisoRiconnessione_Tick
            _timerAvvisoRiconnessione.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub TimerAvvisoRiconnessione_Tick(sender As Object, e As EventArgs)
        Try
            FermaTimerAvvisoRiconnessione()
            LeggiStatoTTS("Riconnessione allo stream in corso.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub FermaTimerAvvisoRiconnessione()
        Try
            If _timerAvvisoRiconnessione IsNot Nothing Then
                _timerAvvisoRiconnessione.Stop()
                RemoveHandler _timerAvvisoRiconnessione.Tick, AddressOf TimerAvvisoRiconnessione_Tick
                _timerAvvisoRiconnessione.Dispose()
                _timerAvvisoRiconnessione = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnPausaPerCache(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnPausaPerCache(sender, e)))
                Return
            End If
            LeggiStatoTTS("Sto caricando.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnRipresaDaCache(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnRipresaDaCache(sender, e)))
                Return
            End If
            LeggiStatoTTS("Riprendo la riproduzione.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnYtMediaCaricato(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnYtMediaCaricato(sender, e)))
                Return
            End If

            ChiudiProcessoCaricamento()
            FermaTimerAvvisoRiconnessione()

            If IsYoutubeConBrowser() Then
                If Not _youtubeProntoLetto Then
                    _youtubeProntoLetto = True

                    ApplicaVolume(_volumeCorrente)

                    LeggiStatoTTS("Video pronto.")

                    If _avvioRinviato Then
                        _mediaProntoInAttesa = True
                        If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                        pausa = True
                        _pausaRichiesta = True
                        AvviaVigilanzaPausa()
                    Else
                        AvviaRiproduzioneEffettiva()
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnMediaOpened(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnMediaOpened(sender, e)))
                Return
            End If

            If _mpv IsNot Nothing Then
                _totalLength = CLng(_mpv.Durata.TotalMilliseconds)

                If LeggiConfigBool("ricordaposizione", False) AndAlso currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                    If elementi(currentIndex).tipo = ElementType.File Then
                        Dim hash = Math.Abs(elementi(currentIndex).path.GetHashCode()).ToString()
                        Dim savedPosStr = _config.LeggiValore("pos_" & hash)
                        Dim savedPos As Double
                        If Not String.IsNullOrEmpty(savedPosStr) AndAlso Double.TryParse(savedPosStr, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, savedPos) Then
                            _mpv.ScorriAssoluto(savedPos)
                        End If
                    End If
                End If

                ApplicaVolume(_volumeCorrente)

                If currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso
                   elementi(currentIndex).tipo = ElementType.YouTube AndAlso Not _youtubeProntoLetto Then
                    _youtubeProntoLetto = True
                    LeggiStatoTTS("Video pronto.")
                End If

                If currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso elementi(currentIndex).tipo = ElementType.YouTube Then
                    _ultimoIndiceCapitoloAnnunciato = -1
                    _ultimoSegmentoSponsorSaltato = Nothing
                    If IsYoutubeConMpv() AndAlso _provider IsNot Nothing Then
                        AvviaPrefetchCapitoli(elementi(currentIndex).path)
                    End If
                End If

                ' Stream TV: non mettere mai in pausa all'avvio (audio e video devono partire subito)
                If IsStreamTvCorrente() Then
                    _avvioRinviato = False
                    _mediaProntoInAttesa = False
                    AvviaRiproduzioneEffettiva()
                ElseIf _avvioRinviato Then
                    _mediaProntoInAttesa = True
                    _mpv.Pausa()
                    pausa = True
                    _pausaRichiesta = True
                    AvviaVigilanzaPausa()
                Else
                    AvviaRiproduzioneEffettiva()
                End If
            End If

            Me.Focus()

            If Menuopzioni IsNot Nothing AndAlso menuopzioniaperto Then
                Menuopzioni.BringToFront()
            End If

            ChiudiProcessoCaricamento()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnMediaEnded(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnMediaEnded(sender, e)))
                Return
            End If

            If IsYoutubeConBrowser() Then Return
            If _fineGiaGestita Then Return
            If Not FineElementoAttendibile() Then Return

            _fineGiaGestita = True
            SalvaPosizioneAttuale()

            GestisciFineElemento()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciFineElemento()
        Try
            If _ripetiTraccia Then
                RiproduciElementoCorrente(False)
                Return
            End If

            If EsistonoElementiSuccessivi() Then
                RiproduciProssimoElemento(True)
                Return
            End If

            FermaPlayerCorrente()
            pausa = True
            _pausaRichiesta = True
            AvviaVigilanzaPausa()
            _transizioneInCorso = False
            _avvioRinviato = False
            AnnunciaFineRiproduzione()
            ' Finita la lista: parte quello che l'utente aveva messo in coda con "Riproduci dopo".
            WGRiproduzione.SegnalaFine()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
        End Try
    End Sub

    Private Sub AnnunciaFineRiproduzione()
        Try
            If _fineAnnunciata Then Return
            _fineAnnunciata = True

            Dim descrizione As String = "La riproduzione è terminata."

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                Dim el As elemento = elementi(currentIndex)
                If el.tipo = ElementType.WebRadio Then
                    descrizione = "La radio è stata interrotta."
                ElseIf el.tipo = ElementType.StreamTv Then
                    descrizione = "Il canale è stato interrotto."
                ElseIf IsElementoVideo(el) Then
                    descrizione = "Il video è finito."
                Else
                    descrizione = "Il brano è finito."
                End If
            End If

            If elementi.Count > 1 Then
                descrizione = descrizione & " È finito anche l'elenco di riproduzione: non ci sono altri elementi."
            End If

            LeggiStatoTTS(descrizione)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnMediaFailed(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnMediaFailed(sender, e)))
                Return
            End If

            ChiudiProcessoCaricamento()

            _transizioneInCorso = False
            _avvioRinviato = False
            _mediaProntoInAttesa = False

            If elementi.Count > 1 AndAlso EsistonoElementiSuccessivi() Then
                Dim nomeElemento As String = ""
                If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                    nomeElemento = elementi(currentIndex).nome
                End If
                If String.IsNullOrWhiteSpace(nomeElemento) Then
                    LeggiStatoTTS("Impossibile riprodurre l'elemento corrente. Passo al successivo.")
                Else
                    LeggiStatoTTS("Impossibile riprodurre """ & nomeElemento & """. Passo al successivo.")
                End If
                If _mpv IsNot Nothing Then
                    _mpv.FermaRiproduzione()
                End If
                RiproduciProssimoElemento(True)
                Return
            End If

            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore durante la riproduzione. La riproduzione è stata interrotta.")
            msgBox.ShowDialog()
            If _mpv IsNot Nothing Then
                _mpv.FermaRiproduzione()
            End If
            pausa = True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub


    Private Sub OnErroreRadioMpv(sender As Object, e As ErroreRadioEventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnErroreRadioMpv(sender, e)))
                Return
            End If

            Dim nome As String = ""
            Dim url As String = If(e IsNot Nothing, e.Url, "")
            Dim motivo As String = If(e IsNot Nothing, e.Motivo, "")

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                nome = elementi(currentIndex).nome
                If String.IsNullOrWhiteSpace(url) Then url = elementi(currentIndex).path
            End If

            RaiseEvent ErroreRadio(Me, New ErroreRadioPlayerEventArgs(url, nome, motivo))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnYtVideoTerminato(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnYtVideoTerminato(sender, e)))
                Return
            End If

            If _fineGiaGestita Then Return
            If Not FineElementoAttendibile() Then Return

            _fineGiaGestita = True
            SalvaPosizioneAttuale()

            If _ytPlayer IsNot Nothing Then
                _ytPlayer.Pausa()
            End If
            pausa = True
            _pausaRichiesta = True
            AvviaVigilanzaPausa()

            GestisciFineElemento()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnYtErroreMedia(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnYtErroreMedia(sender, e)))
                Return
            End If
            _transizioneInCorso = False
            _avvioRinviato = False
            _mediaProntoInAttesa = False

            If elementi.Count > 1 AndAlso EsistonoElementiSuccessivi() Then
                Dim nomeElemento As String = ""
                If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                    nomeElemento = elementi(currentIndex).nome
                End If
                If String.IsNullOrWhiteSpace(nomeElemento) Then
                    LeggiStatoTTS("Impossibile riprodurre l'elemento corrente. Passo al successivo.")
                Else
                    LeggiStatoTTS("Impossibile riprodurre """ & nomeElemento & """. Passo al successivo.")
                End If
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.Pausa()
                End If
                RiproduciProssimoElemento(True)
                Return
            End If

            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore durante la riproduzione del video di YouTube.")
            msgBox.ShowDialog()
            If _ytPlayer IsNot Nothing Then
                _ytPlayer.Pausa()
            End If
            pausa = True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnYtVideoNonDisponibile(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnYtVideoNonDisponibile(sender, e)))
                Return
            End If

            ChiudiProcessoCaricamento()

            _transizioneInCorso = False
            _avvioRinviato = False
            _mediaProntoInAttesa = False

            If elementi.Count > 1 AndAlso EsistonoElementiSuccessivi() Then
                Dim nomeElemento As String = ""
                If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                    nomeElemento = elementi(currentIndex).nome
                End If
                If String.IsNullOrWhiteSpace(nomeElemento) Then
                    LeggiStatoTTS("Il video corrente non è disponibile. Passo al successivo.")
                Else
                    LeggiStatoTTS("""" & nomeElemento & """ non è disponibile. Passo al successivo.")
                End If
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.Pausa()
                End If
                RiproduciProssimoElemento(True)
                Return
            End If

            LeggiStatoTTS("Il video scelto non è disponibile oppure non può essere riprodotto.")
            If _ytPlayer IsNot Nothing Then
                _ytPlayer.Pausa()
            End If
            pausa = True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub _ytPlayer_altpremuto(mittente As Object, e As EventArgs)
        Me.BeginInvoke(New Action(Sub() apriopzioni()))
    End Sub

    Private Sub _ytPlayer_escpremuto(mittente As Object, e As EventArgs)
        If Not menuopzioniaperto Then
            Me.BeginInvoke(New Action(Sub() chiudi(Nothing, Nothing)))
        End If
    End Sub

    Private Sub OnMediaPlayerStateTimerTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() OnMediaPlayerStateTimerTick(sender, e)))
                Return
            End If

            If pausa Then Return

            If Not IsYoutubeConBrowser() AndAlso _mpv IsNot Nothing Then
                _totalLength = CLng(_mpv.Durata.TotalMilliseconds)
            End If

            SorvegliaFineElemento()

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiElemento(path As String, Optional tipo As ElementType? = Nothing, Optional nome As String = Nothing, Optional userAgent As String = Nothing, Optional referer As String = Nothing, Optional headersExtra As String = Nothing)
        Try
            Dim finalPath As String = path
            Dim finalType As ElementType
            Dim isYtPlaylist As Boolean = False

            If finalPath.ToLower().Contains("youtube.com") AndAlso finalPath.ToLower().Contains("list=") Then
                isYtPlaylist = True
            End If

            If isYtPlaylist Then
                Task.Run(Sub() CaricaPlaylistYoutube(finalPath))
                Return
            End If

            If tipo.HasValue Then
                finalType = tipo.Value
                If finalType = ElementType.YouTube Then
                    finalPath = CleanYouTubeUrl(finalPath)
                End If
            Else
                Dim extension = System.IO.Path.GetExtension(finalPath).ToLower()
                If extension = ".m3u" OrElse extension = ".pls" Then
                    ParsePlaylist(finalPath)
                    Return
                ElseIf Uri.IsWellFormedUriString(finalPath, UriKind.Absolute) Then
                    Dim low = finalPath.ToLowerInvariant()
                    If low.Contains("youtube.com/watch?v=") OrElse low.Contains("youtu.be/") Then
                        finalType = ElementType.YouTube
                        finalPath = CleanYouTubeUrl(finalPath)
                    ElseIf IsUrlStreamTv(finalPath) Then
                        finalType = ElementType.StreamTv
                    Else
                        finalType = ElementType.WebRadio
                    End If
                Else
                    finalType = ElementType.File
                End If
            End If

            Dim item As New elemento With {
                .path = finalPath,
                .tipo = finalType,
                .nome = If(nome, String.Empty),
                .userAgent = If(userAgent, String.Empty),
                .referer = If(referer, String.Empty),
                .headersExtra = If(headersExtra, String.Empty)
            }
            elementi.Add(item)
            _elementiCasualiOriginali.Add(item)

            ' Precarica subito il titolo YouTube così è pronto al primo avvio
            If finalType = ElementType.YouTube Then
                AvviaRecuperoTitoloYoutube(finalPath)
            End If
        Catch ex As ArgumentException
            _logger.ScriviLog(ex.ToString())
        Catch ex As NotSupportedException
            _logger.ScriviLog(ex.ToString())
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub CaricaPlaylistYoutube(url As String)
        Dim procCorso As WGProcessoInCorso = Nothing
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            procCorso = New WGProcessoInCorso()
            Me.Invoke(Sub()
                          AddHandler procCorso.cancella, AddressOf AnnullaCaricamento
                          procCorso.Avvia("Sto leggendo l'elenco dei video, attendi", False)
                      End Sub)

            Dim psi As New ProcessStartInfo()
            psi.FileName = "yt-dlp"
            psi.Arguments = $"--flat-playlist --print id -- {UrlPerYtDlp(url)}"
            psi.RedirectStandardOutput = True
            psi.UseShellExecute = False
            psi.CreateNoWindow = True

            Dim output As String = Await LeggiUscitaProcessoAsync(psi)

            If String.IsNullOrWhiteSpace(output) Then
                psi.Arguments = $"--flat-playlist --get-id -- {UrlPerYtDlp(url)}"
                output = Await LeggiUscitaProcessoAsync(psi)
            End If

            If String.IsNullOrWhiteSpace(output) Then Return
            Dim ids() As String = output.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)

            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.Invoke(Sub()
                          For Each id In ids
                              If Not String.IsNullOrWhiteSpace(id) Then
                                  Dim ytUrl As String = $"https://www.youtube.com/watch?v={id.Trim()}"
                                  Dim item As New elemento With {.path = ytUrl, .tipo = ElementType.YouTube, .nome = String.Empty}
                                  elementi.Add(item)
                                  _elementiCasualiOriginali.Add(item)
                              End If
                          Next

                          ' Avvia subito il recupero titoli dei primi video (in particolare il primo)
                          Dim daPrecaricare As Integer = Math.Min(3, elementi.Count)
                          For i As Integer = 0 To daPrecaricare - 1
                              If elementi(i).tipo = ElementType.YouTube Then
                                  AvviaRecuperoTitoloYoutube(elementi(i).path)
                              End If
                          Next

                          If currentIndex = -1 AndAlso elementi.Count > 0 Then
                              RiproduciProssimoElemento(elementi.Count > 1)
                          End If
                      End Sub)

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        Finally
            Try
                If procCorso IsNot Nothing Then
                    Dim procDaChiudere As WGProcessoInCorso = procCorso
                    If Not Me.IsDisposed AndAlso Me.IsHandleCreated Then
                        Me.BeginInvoke(Sub()
                                           Try
                                               RemoveHandler procDaChiudere.cancella, AddressOf AnnullaCaricamento
                                               procDaChiudere.Fine()
                                           Catch exFine As Exception
                                               _logger.ScriviLog(exFine.ToString())
                                           End Try
                                       End Sub)
                    Else
                        RemoveHandler procDaChiudere.cancella, AddressOf AnnullaCaricamento
                        procDaChiudere.Fine()
                    End If
                End If
            Catch exFinally As Exception
                _logger.ScriviLog(exFinally.ToString())
            End Try
        End Try
    End Sub

    Private Async Function LeggiUscitaProcessoAsync(psi As ProcessStartInfo) As Task(Of String)
        Try
            If psi Is Nothing Then Return String.Empty
            Using p As Process = Process.Start(psi)
                If p Is Nothing Then Return String.Empty
                Dim testo As String = Await p.StandardOutput.ReadToEndAsync()
                ' Attesa con limite: senza limite un yt-dlp bloccato terrebbe aperto per sempre l'avviso di attesa.
                If Not p.WaitForExit(60000) Then
                    Try
                        p.Kill()
                    Catch exKill As Exception
                        _logger.ScriviLog(exKill.ToString())
                    End Try
                End If
                Return If(testo, String.Empty)
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return String.Empty
        End Try
    End Function

    Private Function CleanYouTubeUrl(url As String) As String
        Try
            Dim videoId As String = Nothing
            If url.Contains("youtube.com") Then
                Dim match As Match = Regex.Match(url, "v=([a-zA-Z0-9_-]+)")
                If match.Success Then
                    videoId = match.Groups(1).Value
                End If
            ElseIf url.Contains("youtu.be") Then
                Dim match As Match = Regex.Match(url, "youtu\.be/([a-zA-Z0-9_-]+)")
                If match.Success Then
                    videoId = match.Groups(1).Value
                End If
            End If

            If Not String.IsNullOrEmpty(videoId) Then
                Return $"https://www.youtube.com/watch?v={videoId}"
            End If

            Return url
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return url
        End Try
    End Function

    Private Sub ParsePlaylist(filePath As String)
        Try
            If Not System.IO.File.Exists(filePath) Then
                Throw New FileNotFoundException("File dell'elenco di riproduzione non trovato.", filePath)
            End If
            Using reader As New StreamReader(filePath)
                Dim line As String
                Do
                    line = reader.ReadLine()
                    If line IsNot Nothing Then
                        line = line.Trim()
                        If Not String.IsNullOrWhiteSpace(line) AndAlso Not line.StartsWith("#") Then
                            AggiungiElemento(line, ElementType.WebRadio)
                        End If
                    End If
                Loop Until line Is Nothing
            End Using
        Catch ex As FileNotFoundException
            _logger.ScriviLog(ex.ToString())
        Catch ex As SecurityException
            _logger.ScriviLog(ex.ToString())
        Catch ex As IOException
            _logger.ScriviLog(ex.ToString())
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciProssimoElemento(leggi As Boolean)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() RiproduciProssimoElemento(leggi))
                Return
            End If

            If (DateTime.Now - _tempoUltimoCambioElemento).TotalMilliseconds < INTERVALLO_MINIMO_CAMBIO_MS Then Return
            _tempoUltimoCambioElemento = DateTime.Now

            _transizioneInCorso = True
            _inizioTransizione = DateTime.Now

            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()

            SalvaPosizioneAttuale()
            SospendiTuttiIPlayer()

            If _ripetiTraccia Then
                RiproduciElementoCorrente(leggi)
                Return
            End If

            If _riproduzioneCasuale Then
                If elementi.Count > 0 Then
                    currentIndex = _generatoreCasuale.Next(0, elementi.Count)
                    RiproduciElementoCorrente(leggi)
                Else
                    FermaPlayerCorrente()
                    pausa = True
                    _transizioneInCorso = False
                    LeggiStatoTTS("Non ci sono altri elementi da riprodurre.")
                End If
                Return
            End If

            If currentIndex < elementi.Count - 1 Then
                currentIndex += 1
                RiproduciElementoCorrente(leggi)
            Else
                If _ripetiPlaylist Then
                    currentIndex = -1
                    _tempoUltimoCambioElemento = DateTime.MinValue
                    RiproduciProssimoElemento(leggi)
                Else
                    FermaPlayerCorrente()
                    pausa = True
                    _pausaRichiesta = True
                    AvviaVigilanzaPausa()
                    _transizioneInCorso = False
                    AnnunciaFineRiproduzione()
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
        End Try
    End Sub

    Private Shared _interruzioneRegistrata As Boolean

    ''' <summary>Fa sapere a WGRiproduzione come interrompere l'ascolto del lettore in primo piano. Si registra una volta sola.</summary>
    Private Shared Sub RegistraInterruzioneAscolto()
        Try
            If _interruzioneRegistrata Then Return
            _interruzioneRegistrata = True
            WGRiproduzione.RegistraInterruttore(AddressOf InterrompiAscoltoCorrente)
        Catch ex As Exception
            _interruzioneRegistrata = False
        End Try
    End Sub

    ''' <summary>Ferma e chiude il lettore in primo piano senza chiedere se continuare in sottofondo.</summary>
    Private Shared Sub InterrompiAscoltoCorrente()
        Dim p As WGPlayerFormInt = _istanzaCorrente
        If p Is Nothing OrElse p.IsDisposed OrElse p.Disposing Then Return
        p.InterrompiEChiudi()
    End Sub

    Private Sub InterrompiEChiudi()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() InterrompiEChiudi())
                Return
            End If
            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()
            SalvaPosizioneAttuale()
            _chiusuraInCorso = True
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
            ElseIf _mpv IsNot Nothing Then
                _mpv.FermaRiproduzione()
            End If
            FinalizzaChiusura()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            FinalizzaChiusura()
        End Try
    End Sub

    Private Sub FermaPlayerCorrente()
        Try
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
            ElseIf _mpv IsNot Nothing Then
                _mpv.FermaRiproduzione()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SospendiTuttiIPlayer()
        Try
            If _ytPlayer IsNot Nothing Then
                Try
                    _ytPlayer.Pausa()
                Catch exYt As Exception
                    _logger.ScriviLog(exYt.ToString())
                End Try
            End If

            If _mpv IsNot Nothing Then
                Try
                    If Not _mpv.InPausa Then _mpv.Pausa()
                Catch exMpv As Exception
                    _logger.ScriviLog(exMpv.ToString())
                End Try
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciElementoPrecedente()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() RiproduciElementoPrecedente())
                Return
            End If

            If (DateTime.Now - _tempoUltimoCambioElemento).TotalMilliseconds < INTERVALLO_MINIMO_CAMBIO_MS Then Return
            _tempoUltimoCambioElemento = DateTime.Now

            _transizioneInCorso = True
            _inizioTransizione = DateTime.Now

            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()

            SalvaPosizioneAttuale()
            SospendiTuttiIPlayer()

            If currentIndex > 0 Then
                currentIndex -= 1
                RiproduciElementoCorrente(True)
            Else
                _transizioneInCorso = False
                LeggiStatoTTS("Sei già al primo elemento dell'elenco.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
        End Try
    End Sub

    Private Function GetUNCPath(path As String) As String
        Try
            If path.Length < 3 OrElse path(1) <> ":"c Then
                Return path
            End If

            Dim sb As New StringBuilder(512)
            Dim size As Integer = sb.Capacity

            If WNetGetConnection(path.Substring(0, 2), sb, size) = 0 Then
                Return System.IO.Path.Combine(sb.ToString(), path.Substring(3))
            End If

            Return path
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return path
        End Try
    End Function

    Private Sub RiproduciElementoCorrente(Optional leggiTitolo As Boolean = True)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() RiproduciElementoCorrente(leggiTitolo))
                Return
            End If

            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then
                _transizioneInCorso = False
                Return
            End If

            _transizioneInCorso = True
            _inizioTransizione = DateTime.Now

            Dim centro = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing AndAlso centro.CiSonoDownloadDipendenzeInCorso() Then
                _leggiTitoloInSospeso = leggiTitolo
                If processoincorso IsNot Nothing Then
                    RemoveHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                    processoincorso.Fine()
                    processoincorso = Nothing
                End If
                NascondiBarraTocco()
                processoincorso = New WGProcessoInCorso()
                AddHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                processoincorso.Avvia("Sto scaricando i componenti necessari, attendi", False)
                AddHandler centro.DownloadDipendenzeCompletato, AddressOf DipendenzeCompletate
                Return
            End If

            ProcediRiproduzione(leggiTitolo)

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
        End Try
    End Sub

    Private Sub DipendenzeCompletate()
        Try
            Dim centro = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then
                RemoveHandler centro.DownloadDipendenzeCompletato, AddressOf DipendenzeCompletate
            End If
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New Action(Sub()
                                          If processoincorso IsNot Nothing Then
                                              RemoveHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                                              processoincorso.Fine()
                                              processoincorso = Nothing
                                          End If
                                          ProcediRiproduzione(_leggiTitoloInSospeso)
                                      End Sub))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function NomeRadioDaUrl(url As String) As String
        Try
            Dim indirizzo As New Uri(url)
            Return indirizzo.Host
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "radio"
        End Try
    End Function

    Private Function TitoloYoutubeDaCache(url As String) As String
        Try
            SyncLock _lockTitoli
                If _titoliYoutube.ContainsKey(url) Then
                    Return _titoliYoutube(url)
                End If
            End SyncLock
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub AvviaRecuperoTitoloYoutube(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return

            SyncLock _lockTitoli
                If _titoliYoutube.ContainsKey(url) Then Return
                If _titoliInCorso.Contains(url) Then Return
                _titoliInCorso.Add(url)
            End SyncLock

            Task.Run(Sub() RecuperaTitoloYoutubeInterno(url))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' L'indirizzo pronto per la riga di comando di yt-dlp: tra virgolette, senza virgolette interne né a capo (diventano %22 o si tolgono),
    ''' così non può chiudere l'argomento e aggiungere opzioni. Va sempre dopo "--".
    ''' </summary>
    Private Shared Function UrlPerYtDlp(url As String) As String
        Dim pulito As String = If(url, "").Trim().Replace("""", "%22").Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "")
        Return """" & pulito & """"
    End Function

    Private Sub RecuperaTitoloYoutubeInterno(url As String)
        Dim titolo As String = Nothing
        Try
            Dim psi As New ProcessStartInfo()
            psi.FileName = "yt-dlp"
            psi.Arguments = "--no-warnings --skip-download --print ""%(title)s"" -- " & UrlPerYtDlp(url)
            psi.RedirectStandardOutput = True
            psi.UseShellExecute = False
            psi.CreateNoWindow = True
            psi.StandardOutputEncoding = Encoding.UTF8

            Using p As Process = Process.Start(psi)
                If p Is Nothing Then Return
                Dim output As String = p.StandardOutput.ReadToEnd()
                If Not p.WaitForExit(20000) Then
                    Try
                        p.Kill()
                    Catch exKill As Exception
                        _logger.ScriviLog(exKill.ToString())
                    End Try
                End If

                If Not String.IsNullOrWhiteSpace(output) Then
                    Dim righe() As String = output.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    If righe.Length > 0 Then
                        titolo = righe(0).Trim()
                    End If
                End If
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        Finally
            Try
                SyncLock _lockTitoli
                    If _titoliInCorso.Contains(url) Then _titoliInCorso.Remove(url)
                    If Not String.IsNullOrWhiteSpace(titolo) Then
                        _titoliYoutube(url) = titolo
                    End If
                End SyncLock

                ' Se stiamo ancora aspettando proprio questo titolo, completa subito la lettura
                If Not String.IsNullOrWhiteSpace(titolo) AndAlso
                   Not String.IsNullOrWhiteSpace(_attesaTitoloUrl) AndAlso
                   String.Equals(_attesaTitoloUrl, url, StringComparison.OrdinalIgnoreCase) Then
                    If Not Me.IsDisposed AndAlso Me.IsHandleCreated Then
                        Me.BeginInvoke(New Action(Sub()
                                                      If String.Equals(_attesaTitoloUrl, url, StringComparison.OrdinalIgnoreCase) Then
                                                          AnnullaAttesaTitolo()
                                                          AvviaLetturaTitolo(TestoTitoloElemento(_pendingElement))
                                                      End If
                                                  End Sub))
                    End If
                End If
            Catch exFin As Exception
                _logger.ScriviLog(exFin.ToString())
            End Try
        End Try
    End Sub

    Private Sub PrecaricaTitoliVicini()
        Try
            If elementi.Count < 2 Then Return

            Dim successivo As Integer = currentIndex + 1
            If successivo < elementi.Count Then
                If elementi(successivo).tipo = ElementType.YouTube Then
                    AvviaRecuperoTitoloYoutube(elementi(successivo).path)
                End If
            ElseIf _ripetiPlaylist AndAlso elementi.Count > 0 Then
                If elementi(0).tipo = ElementType.YouTube Then
                    AvviaRecuperoTitoloYoutube(elementi(0).path)
                End If
            End If

            Dim precedente As Integer = currentIndex - 1
            If precedente >= 0 AndAlso precedente < elementi.Count Then
                If elementi(precedente).tipo = ElementType.YouTube Then
                    AvviaRecuperoTitoloYoutube(elementi(precedente).path)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function PrefissoPosizione(file As elemento) As String
        Try
            If elementi.Count <= 1 Then Return ""

            Dim etichetta As String = "Brano"
            If file.tipo = ElementType.WebRadio Then
                etichetta = "Radio"
            ElseIf file.tipo = ElementType.StreamTv Then
                etichetta = "Canale"
            ElseIf IsElementoVideo(file) Then
                etichetta = "Video"
            End If

            Return etichetta & " " & (currentIndex + 1).ToString() & " di " & elementi.Count.ToString() & ". "
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Function TestoTitoloElemento(file As elemento) As String
        Try
            Dim nome As String = Nothing

            ' Nome esplicito (es. canale TV) ha priorità
            If Not String.IsNullOrWhiteSpace(file.nome) Then
                nome = file.nome.Trim()
            ElseIf file.tipo = ElementType.YouTube Then
                nome = TitoloYoutubeDaCache(file.path)
                If String.IsNullOrWhiteSpace(nome) Then
                    nome = "video di YouTube"
                End If
            ElseIf file.tipo = ElementType.WebRadio Then
                nome = NomeRadioDaUrl(file.path)
            ElseIf file.tipo = ElementType.StreamTv Then
                nome = NomeCanaleDaUrl(file.path)
            Else
                nome = System.IO.Path.GetFileNameWithoutExtension(file.path)
            End If

            If String.IsNullOrWhiteSpace(nome) Then nome = "elemento senza titolo"

            Dim prefisso As String = PrefissoPosizione(file)

            If LeggiConfigBool("leggidettaglibbrano", False) AndAlso file.tipo = ElementType.File Then
                Return prefisso & "Ora ascolti: " & nome & "."
            End If

            Return prefisso & nome & "."
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "Elemento successivo."
        End Try
    End Function

    Private Function NomeCanaleDaUrl(url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return "canale televisivo"
            Dim u = url.ToLowerInvariant()
            If u.Contains("mediapolis.rai.it") OrElse u.Contains("raiplay") Then Return "canale Rai"
            If u.Contains("mediaset") Then Return "canale Mediaset"
            If u.Contains("la7") Then Return "canale La7"
            Try
                Dim host = New Uri(url).Host
                If Not String.IsNullOrWhiteSpace(host) Then Return host
            Catch
            End Try
            Return "canale televisivo"
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "canale televisivo"
        End Try
    End Function

    Private Sub ProcediRiproduzione(leggiTitolo As Boolean)
        Try
            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return

            Dim file As elemento = elementi(currentIndex)

            If (file.tipo = ElementType.WebRadio OrElse file.tipo = ElementType.StreamTv OrElse file.tipo = ElementType.YouTube) AndAlso Not IsNetworkAvailable() Then
                Dim msgBox As New WGMessaggi()
                If file.tipo = ElementType.StreamTv Then
                    msgBox.Messaggio("Non c'è collegamento a internet: non è possibile guardare il canale.")
                ElseIf file.tipo = ElementType.YouTube Then
                    msgBox.Messaggio("Non c'è collegamento a internet: non è possibile riprodurre il video.")
                Else
                    msgBox.Messaggio("Non c'è collegamento a internet: non è possibile ascoltare la radio.")
                End If
                msgBox.ShowDialog()
                If _mpv IsNot Nothing Then _mpv.FermaRiproduzione()
                If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                pausa = True
                _transizioneInCorso = False
                _avvioRinviato = False
                Return
            End If

            _pendingElement = file
            _mediaProntoInAttesa = False
            _fineAnnunciata = False

            ' Legge il titolo quando ci sono più elementi (playlist) e la lettura è abilitata
            Dim deveLeggere As Boolean = leggiTitolo AndAlso LeggiConfigBool("leggititolo", True) AndAlso elementi.Count > 1

            _avvioRinviato = deveLeggere

            AvviaRiproduzioneReale(file)

            If Not deveLeggere Then Return

            If file.tipo = ElementType.YouTube AndAlso String.IsNullOrWhiteSpace(TitoloYoutubeDaCache(file.path)) Then
                AvviaRecuperoTitoloYoutube(file.path)
                AvviaAttesaTitolo(file.path)
                Return
            End If

            AvviaLetturaTitolo(TestoTitoloElemento(file))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
            _avvioRinviato = False
        End Try
    End Sub

    Private Sub AvviaAttesaTitolo(url As String)
        Try
            _attesaTitoloUrl = url
            _inizioAttesaTitolo = DateTime.Now

            If _tmrAttesaTitolo Is Nothing Then
                _tmrAttesaTitolo = New System.Windows.Forms.Timer()
                _tmrAttesaTitolo.Interval = 200
                AddHandler _tmrAttesaTitolo.Tick, AddressOf AttesaTitoloTick
            End If

            _tmrAttesaTitolo.Stop()
            _tmrAttesaTitolo.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            AvviaLetturaTitolo(TestoTitoloElemento(_pendingElement))
        End Try
    End Sub

    Private Sub AnnullaAttesaTitolo()
        Try
            _attesaTitoloUrl = Nothing
            If _tmrAttesaTitolo IsNot Nothing Then
                _tmrAttesaTitolo.Stop()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AttesaTitoloTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                AnnullaAttesaTitolo()
                Return
            End If

            If String.IsNullOrWhiteSpace(_attesaTitoloUrl) Then
                AnnullaAttesaTitolo()
                Return
            End If

            Dim urlAtteso As String = _attesaTitoloUrl
            Dim titolo As String = TitoloYoutubeDaCache(urlAtteso)
            Dim trascorso As Double = (DateTime.Now - _inizioAttesaTitolo).TotalMilliseconds

            ' Due secondi e mezzo: oltre, il titolo si dice piu' tardi ma il video parte.
            If Not String.IsNullOrWhiteSpace(titolo) OrElse trascorso > 2500 Then
                AnnullaAttesaTitolo()
                AvviaLetturaTitolo(TestoTitoloElemento(_pendingElement))
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            AnnullaAttesaTitolo()
            AvviaLetturaTitolo("Elemento successivo.")
        End Try
    End Sub

    Private Sub AvviaLetturaTitolo(testo As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If String.IsNullOrWhiteSpace(testo) Then
                AvviaRiproduzioneReale(_pendingElement)
                Return
            End If

            RemoveHandler lettura.OnFineLettura, AddressOf GestisciFineLettura
            AddHandler lettura.OnFineLettura, AddressOf GestisciFineLettura

            _attesaLetturaAttiva = True
            _letturaIniziata = False
            _inizioAttesaLettura = DateTime.Now

            If _tmrAttesaLettura Is Nothing Then
                _tmrAttesaLettura = New System.Windows.Forms.Timer()
                _tmrAttesaLettura.Interval = 150
                AddHandler _tmrAttesaLettura.Tick, AddressOf AttesaLetturaTick
            End If
            _tmrAttesaLettura.Stop()
            _tmrAttesaLettura.Start()

            lettura.LeggiTesto(testo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            CompletaAttesaLettura()
        End Try
    End Sub

    Private Sub AttesaLetturaTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                AnnullaAttesaLettura()
                Return
            End If

            If Not _attesaLetturaAttiva Then
                If _tmrAttesaLettura IsNot Nothing Then _tmrAttesaLettura.Stop()
                Return
            End If

            Dim trascorso As Double = (DateTime.Now - _inizioAttesaLettura).TotalMilliseconds
            Dim staParlando As Boolean = False

            Try
                staParlando = lettura.StaParlando
            Catch exStato As Exception
                _logger.ScriviLog(exStato.ToString())
            End Try

            If staParlando Then
                _letturaIniziata = True
            End If

            If _letturaIniziata AndAlso Not staParlando Then
                CompletaAttesaLettura()
                Return
            End If

            If Not _letturaIniziata AndAlso trascorso > 3000 Then
                CompletaAttesaLettura()
                Return
            End If

            If trascorso > 45000 Then
                CompletaAttesaLettura()
                Return
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            CompletaAttesaLettura()
        End Try
    End Sub

    Private Sub GestisciFineLettura()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New Action(Sub() CompletaAttesaLettura()))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CompletaAttesaLettura()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Not _attesaLetturaAttiva Then Return
            _attesaLetturaAttiva = False
            _letturaIniziata = False

            If _tmrAttesaLettura IsNot Nothing Then _tmrAttesaLettura.Stop()
            RemoveHandler lettura.OnFineLettura, AddressOf GestisciFineLettura

            If _chiusuraInCorso Then Return

            If _mediaProntoInAttesa Then
                AvviaRiproduzioneEffettiva()
            Else
                _avvioRinviato = False
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _avvioRinviato = False
            _transizioneInCorso = False
        End Try
    End Sub

    Private Sub AnnullaAttesaLettura()
        Try
            _attesaLetturaAttiva = False
            _letturaIniziata = False
            If _tmrAttesaLettura IsNot Nothing Then _tmrAttesaLettura.Stop()
            RemoveHandler lettura.OnFineLettura, AddressOf GestisciFineLettura
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaVolumeIniziale()
        Try
            Dim volPartenza As Integer = LeggiConfigInt("volumedipartenza", 100)
            If LeggiConfigBool("memorizzavolume", True) Then
                volPartenza = LeggiConfigInt("ultimoVolume", 100)
            End If

            If volPartenza < 0 Then volPartenza = 0
            If volPartenza > 100 Then volPartenza = 100

            _volumeCorrente = volPartenza
            ApplicaVolume(_volumeCorrente)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AzzeraSorveglianzaFine()
        Try
            _posizioneTickPrecedente = -1.0
            _tickPosizioneFerma = 0
            _eraVicinoAllaFine = False
            _fineGiaGestita = False
            _fineAnnunciata = False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Chiamata ogni 250 millisecondi mentre si sta riproducendo. Riconosce due situazioni:
    ' la posizione arriva a ridosso della durata e li' si ferma, oppure eravamo a ridosso
    ' della fine e la posizione crolla a zero perche' mpv ha chiuso il file. In entrambi i
    ' casi l'elemento e' finito, che la segnalazione sia arrivata o no.
    Private Sub SorvegliaFineElemento()
        Try
            If _fineGiaGestita Then Return
            If _chiusuraInCorso Then Return
            If _transizioneInCorso Then Return
            If _avvioRinviato Then Return
            If IsLiveSenzaSeek() Then Return
            If Not TempoDisponibileCorrente() Then Return
            If (DateTime.Now - _tempoUltimoAvvioRiproduzione).TotalMilliseconds < 2000 Then Return

            Dim durata As Double = DurataCorrente().TotalSeconds
            If durata <= 0 Then Return

            Dim posizione As Double = PosizioneCorrente().TotalSeconds

            Dim ferma As Boolean = False
            If _posizioneTickPrecedente >= 0 Then
                ferma = Math.Abs(posizione - _posizioneTickPrecedente) < 0.05
            End If

            If ferma Then
                _tickPosizioneFerma += 1
            Else
                _tickPosizioneFerma = 0
            End If

            _posizioneTickPrecedente = posizione

            Dim vicinoAllaFine As Boolean = (posizione > 0 AndAlso (durata - posizione) <= 1.2)
            If vicinoAllaFine Then _eraVicinoAllaFine = True

            Dim finito As Boolean = False

            ' Arrivati in fondo e la posizione non si muove piu' da quasi un secondo.
            If vicinoAllaFine AndAlso _tickPosizioneFerma >= 3 Then finito = True

            ' Eravamo in fondo e adesso la posizione e' tornata a zero: file chiuso.
            If _eraVicinoAllaFine AndAlso posizione <= 0.05 AndAlso _tickPosizioneFerma >= 2 Then finito = True

            If Not finito Then Return

            _fineGiaGestita = True
            SalvaPosizioneAttuale()
            GestisciFineElemento()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function FineElementoAttendibile() As Boolean
        Try
            If _chiusuraInCorso Then Return False

            ' L'annuncio del titolo blocca la fine dell'elemento solo quando sta davvero
            ' tenendo ferma la riproduzione. Con l'annuncio detto sopra l'audio, un brano
            ' corto poteva finire mentre la voce parlava ancora e il passaggio saltava.
            If _attesaLetturaAttiva AndAlso _mediaProntoInAttesa Then Return False
            If _avvioRinviato Then Return False

            ' Stream TV e radio: non trattare i disconnect/reconnect come fine elemento
            If IsLiveSenzaSeek() Then Return False

            If _transizioneInCorso AndAlso (DateTime.Now - _inizioTransizione).TotalSeconds < 60 Then Return False

            Dim minimo As Double = 1500
            If IsYoutubeConBrowser() Then minimo = 6000

            If (DateTime.Now - _tempoUltimoAvvioRiproduzione).TotalMilliseconds < minimo Then Return False

            If TempoDisponibileCorrente() Then
                Dim durata As Double = DurataCorrente().TotalSeconds
                Dim posizione As Double = PosizioneCorrente().TotalSeconds

                ' Questo controllo serve a scartare le fini finte, quelle che arrivano da un
                ' errore o da una riconnessione a meta' brano. Ma quando il file finisce
                ' davvero mpv ha gia' chiuso lo stream e riporta posizione zero, mentre la
                ' durata resta quella di prima: il confronto diceva "manca ancora tutto" e
                ' il brano successivo non partiva mai. Una posizione a zero non e' una
                ' posizione valida, e in quel caso ci si fida della segnalazione di mpv.
                If durata > 0 AndAlso posizione > 0 AndAlso (durata - posizione) > 5 Then Return False
            End If

            Return True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AvviaRiproduzioneEffettiva()
        Try
            _avvioRinviato = False
            _mediaProntoInAttesa = False

            If Me.IsDisposed OrElse Me.Disposing Then
                _transizioneInCorso = False
                Return
            End If

            Dim autoAvvio As Boolean = LeggiConfigBool("avvioautomatico", True)

            If autoAvvio Then
                AnnullaVigilanzaPausa()
                If IsYoutubeConBrowser() Then
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Riproduci()
                ElseIf _mpv IsNot Nothing Then
                    _mpv.Riproduci()
                End If
                pausa = False
            Else
                ForzaPausa()
            End If

            _tempoUltimoAvvioRiproduzione = DateTime.Now
            AzzeraSorveglianzaFine()
            _transizioneInCorso = False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _transizioneInCorso = False
        End Try
    End Sub

    Private Sub ApplicaVolume(valore As Integer)
        Try
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.CambiaVolume(valore / 100.0)
            ElseIf _mpv IsNot Nothing Then
                _mpv.Volume = valore
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function DescriviVolume(valore As Integer) As String
        Try
            Return "Volume al " & valore.ToString() & " per cento."
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "Volume cambiato."
        End Try
    End Function

    Private Sub LeggiVolumeCorrente()
        Try
            LeggiSempreTTS(DescriviVolume(_volumeCorrente))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaRiproduzioneReale(file As elemento)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If String.IsNullOrWhiteSpace(file.path) Then Return

            AnnullaRipristinoPausa()
            ChiudiProcessoCaricamento()

            _tempoUltimoAvvioRiproduzione = DateTime.Now

            If (file.tipo = ElementType.WebRadio OrElse file.tipo = ElementType.YouTube OrElse file.tipo = ElementType.StreamTv) AndAlso Not _avvioRinviato Then
                NascondiBarraTocco()
                processoincorso = New WGProcessoInCorso()
                AddHandler processoincorso.cancella, AddressOf AnnullaCaricamento
                If file.tipo = ElementType.StreamTv Then
                    processoincorso.Avvia("Sto sintonizzando il canale, attendi", True)
                Else
                    processoincorso.Avvia("Sto caricando, attendi", True)
                End If
                AvviaTimeoutCaricamento()
            End If

            Dim finalPath As String = file.path
            Dim resolvedPath As String = GetUNCPath(finalPath)
            Dim extension As String = System.IO.Path.GetExtension(finalPath).ToLower()

            If extension = ".avi" AndAlso resolvedPath.StartsWith("\\") AndAlso Not String.IsNullOrWhiteSpace(tempFolder) Then
                Dim destFile As String = System.IO.Path.Combine(tempFolder, System.IO.Path.GetFileName(finalPath))
                System.IO.File.Copy(resolvedPath, destFile, True)
                finalPath = destFile
            End If

            If file.tipo = ElementType.YouTube Then
                _youtubeProntoLetto = False

                DistruggiMpv()
                InizializzaYoutube()
                DisattivaHook()
                ImpostaVolumeIniziale()
                SganciaVideoDaPannello()
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytVelocita = 1.0
                    _ytPlayer.RiproduciVideoYoutube(finalPath)
                    _ytPlayer.attivatastiera()
                End If
                pausa = _avvioRinviato
            ElseIf file.tipo = ElementType.StreamTv Then
                DistruggiYoutube()
                If Not InizializzaMpv() Then
                    AvviaAttesaLettore(file)
                    Return
                End If
                AttivaHook()
                ImpostaVolumeIniziale()
                AgganciaVideoAPannello()
                If _mpv IsNot Nothing Then
                    _mpv.ImpostaRadioCorrente("", False)
                    ' NON usare FermaRiproduzione: lo "stop" asincrono cancella il loadfile successivo
                    _mpv.PreparaNuovoCaricamento()
                    _mpv.RiproduciStreamTv(finalPath, file.userAgent, file.referer, file.headersExtra)
                End If
                pausa = False
            Else
                DistruggiYoutube()
                If Not InizializzaMpv() Then
                    AvviaAttesaLettore(file)
                    Return
                End If
                AttivaHook()
                ImpostaVolumeIniziale()
                SganciaVideoDaPannello()
                If _mpv IsNot Nothing Then
                    If file.tipo = ElementType.WebRadio Then
                        _mpv.ImpostaRadioCorrente(finalPath, True)
                        _mpv.PreparaNuovoCaricamento()
                    Else
                        _mpv.ImpostaRadioCorrente("", False)
                        _mpv.FermaRiproduzione()
                    End If
                    _mpv.CaricaFile(finalPath)
                End If
                pausa = _avvioRinviato
            End If

            Dim eBranoLocale As Boolean = file.tipo = ElementType.File AndAlso
                Not String.IsNullOrWhiteSpace(file.path) AndAlso
                Not file.path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso
                Not file.path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso
                Not file.path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) AndAlso
                Not file.path.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)

            If eBranoLocale Then
                Task.Run(Function() _clsBrani.AggiungiBranoRecenteAsync(file.path))
            End If
            AggiornaStatoPreferito()
            PrecaricaTitoliVicini()

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaYtDlpInSottofondo()
        Try
            Task.Run(Sub()
                         Try
                             Dim psi As New ProcessStartInfo()
                             psi.FileName = "yt-dlp"
                             psi.Arguments = "-U"
                             psi.RedirectStandardOutput = True
                             psi.RedirectStandardError = True
                             psi.UseShellExecute = False
                             psi.CreateNoWindow = True

                             Using proc As Process = Process.Start(psi)
                                 If proc IsNot Nothing Then
                                     ' I due flussi vanno letti insieme: leggendoli in fila il processo si blocca quando riempie il buffer.
                                     Dim letturaErrore As Task(Of String) = proc.StandardError.ReadToEndAsync()
                                     Dim output As String = proc.StandardOutput.ReadToEnd()
                                     If Not proc.WaitForExit(30000) Then
                                         Try
                                             proc.Kill()
                                         Catch exKill As Exception
                                             _logger.ScriviLog(exKill.ToString())
                                         End Try
                                     End If
                                     Dim erroreOutput As String = String.Empty
                                     If letturaErrore.Wait(5000) Then erroreOutput = If(letturaErrore.Result, String.Empty)
                                 End If
                             End Using
                         Catch exInterno As Exception
                             _logger.ScriviLog("Errore durante l'aggiornamento automatico di yt-dlp: " & exInterno.ToString())
                         End Try
                     End Sub)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaCaricamento(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() AnnullaCaricamento(sender, e))
                Return
            End If
            FermaAttesaLettore()
            chiudi(Nothing, Nothing)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub AggiornaStatoPreferito()
        Try
            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then
                _elementoPreferito = False
                Return
            End If

            Dim file As elemento = elementi(currentIndex)

            If file.tipo <> ElementType.File Then
                _elementoPreferito = False
                Return
            End If

            _elementoPreferito = Await _clsBrani.IsPreferitoAsync(file.path)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _elementoPreferito = False
        End Try
    End Sub

    Private Async Sub AggiungiPreferito(sender As Object, e As EventArgs)
        Try
            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return
            Dim path As String = elementi(currentIndex).path
            Await _clsBrani.AggiungiBranoPreferitoAsync(path)
            _elementoPreferito = True
            LeggiStatoTTS("Aggiunto ai preferiti.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub RimuoviPreferito(sender As Object, e As EventArgs)
        Try
            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return
            Dim path As String = elementi(currentIndex).path
            Await _clsBrani.RimuoviBranoPreferitoAsync(path)
            _elementoPreferito = False
            LeggiStatoTTS("Tolto dai preferiti.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ModificaESalvaVolume(delta As Integer, Optional annuncia As Boolean = False)
        Try
            Dim nuovoVol As Integer = _volumeCorrente + delta
            If nuovoVol < 0 Then nuovoVol = 0
            If nuovoVol > 100 Then nuovoVol = 100

            Dim giaAlLimite As Boolean = (nuovoVol = _volumeCorrente)
            _volumeCorrente = nuovoVol

            ApplicaVolume(_volumeCorrente)

            If LeggiConfigBool("memorizzavolume", True) Then
                _config.SalvaValore("ultimoVolume", _volumeCorrente.ToString())
            End If

            If annuncia Then
                If giaAlLimite AndAlso _volumeCorrente >= 100 Then
                    LeggiStatoTTS("Il volume è già al massimo.")
                ElseIf giaAlLimite AndAlso _volumeCorrente <= 0 Then
                    LeggiStatoTTS("Il volume è già al minimo.")
                Else
                    LeggiStatoTTS(DescriviVolume(_volumeCorrente))
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnUpPressed()
        Try
            Dim stepVol As Integer = LeggiConfigInt("passovolume", 5)
            ModificaESalvaVolume(stepVol)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnDownPressed()
        Try
            Dim stepVol As Integer = LeggiConfigInt("passovolume", 5)
            ModificaESalvaVolume(-stepVol)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnLeftPressed()
        Try
            If (DateTime.Now - tempoUltimoSeek).TotalMilliseconds < 300 Then Return
            tempoUltimoSeek = DateTime.Now

            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiStatoTTS("Non è possibile spostarsi indietro mentre guardi un canale in diretta.")
                Else
                    LeggiStatoTTS("Non è possibile spostarsi indietro mentre ascolti una radio.")
                End If
                Return
            End If

            Dim stepSeek = LeggiConfigInt("passoscorrimento", 30)

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Scorri(-stepSeek)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.Scorri(-stepSeek)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnRightPressed()
        Try
            If (DateTime.Now - tempoUltimoSeek).TotalMilliseconds < 300 Then Return
            tempoUltimoSeek = DateTime.Now

            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiStatoTTS("Non è possibile spostarsi avanti mentre guardi un canale in diretta.")
                Else
                    LeggiStatoTTS("Non è possibile spostarsi avanti mentre ascolti una radio.")
                End If
                Return
            End If

            Dim stepSeek = LeggiConfigInt("passoscorrimento", 30)

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Scorri(stepSeek)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.Scorri(stepSeek)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnPageUpPressed()
        Try
            If (DateTime.Now - tempoUltimoSeek).TotalMilliseconds < 300 Then Return
            tempoUltimoSeek = DateTime.Now

            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiStatoTTS("Non è possibile spostarsi indietro mentre guardi un canale in diretta.")
                Else
                    LeggiStatoTTS("Non è possibile spostarsi indietro mentre ascolti una radio.")
                End If
                Return
            End If

            Dim stepSeekLungo = LeggiConfigInt("passoscorrimentolungo", 60)

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Scorri(-stepSeekLungo)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.Scorri(-stepSeekLungo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnPageDownPressed()
        Try
            If (DateTime.Now - tempoUltimoSeek).TotalMilliseconds < 300 Then Return
            tempoUltimoSeek = DateTime.Now

            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiStatoTTS("Non è possibile spostarsi avanti mentre guardi un canale in diretta.")
                Else
                    LeggiStatoTTS("Non è possibile spostarsi avanti mentre ascolti una radio.")
                End If
                Return
            End If

            Dim stepSeekLungo = LeggiConfigInt("passoscorrimentolungo", 60)

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Scorri(stepSeekLungo)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.Scorri(stepSeekLungo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnHomePressed()
        Try
            If IsLiveSenzaSeek() Then Return

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.ScorriAssoluto(0)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.ScorriAssoluto(0)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnEndPressed()
        Try
            If IsLiveSenzaSeek() Then Return

            Dim dur As Double = DurataCorrente().TotalSeconds
            If dur <= 2 Then Return

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.ScorriAssoluto(dur - 2)
                Return
            End If

            If _mpv Is Nothing Then Return
            _mpv.ScorriAssoluto(dur - 2)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnSpacePressed()
        Try
            If (DateTime.Now - _ultimoSpazioInt).TotalMilliseconds < 300 Then Return
            _ultimoSpazioInt = DateTime.Now

            AnnullaRipristinoPausa()

            If IsYoutubeConBrowser() Then
                Dim inPausaReale As Boolean = pausa
                If _ytPlayer IsNot Nothing Then inPausaReale = _ytPlayer.InPausa

                If inPausaReale Then
                    If IsNetworkAvailable() Then
                        AnnullaVigilanzaPausa()
                        If _ytPlayer IsNot Nothing Then _ytPlayer.Riproduci()
                        pausa = False
                    Else
                        LeggiStatoTTS("Non c'è collegamento a internet: non è possibile riprendere.")
                    End If
                Else
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                    pausa = True
                    _pausaRichiesta = True
                    AvviaVigilanzaPausa()
                End If
                Return
            End If

            If _mpv Is Nothing Then Return

            If _mpv.InPausa Then
                AnnullaVigilanzaPausa()
                If IsWebRadioCorrente() OrElse IsStreamTvCorrente() Then
                    If IsNetworkAvailable() Then
                        If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                            AvviaRiproduzioneReale(elementi(currentIndex))
                        Else
                            _mpv.Riproduci()
                        End If
                    Else
                        LeggiStatoTTS("Non c'è collegamento a internet: non è possibile riprendere.")
                        Return
                    End If
                Else
                    _mpv.Riproduci()
                End If
                pausa = False
            Else
                _mpv.Pausa()
                pausa = True
                _pausaRichiesta = True
                AvviaVigilanzaPausa()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciElementoPrecedenteMenu(sender As Object, e As System.EventArgs)
        RiproduciElementoPrecedente()
    End Sub

    Private Sub RiproduciProssimoElementoMenu(sender As Object, e As System.EventArgs)
        RiproduciProssimoElemento(True)
    End Sub

    Private Sub IndietroBreveMenu(sender As Object, e As System.EventArgs)
        OnLeftPressed()
    End Sub

    Private Sub AvantiBreveMenu(sender As Object, e As System.EventArgs)
        OnRightPressed()
    End Sub

    Private Sub IndietroLungoMenu(sender As Object, e As System.EventArgs)
        OnPageUpPressed()
    End Sub

    Private Sub AvantiLungoMenu(sender As Object, e As System.EventArgs)
        OnPageDownPressed()
    End Sub

    Private Sub RiproduciPausaMenu(sender As Object, e As System.EventArgs)
        OnSpacePressed()
    End Sub

    Private Sub AumentaVolumeMenu(sender As Object, e As System.EventArgs)
        Try
            ModificaESalvaVolume(LeggiConfigInt("passovolume", 5), True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub DiminuisciVolumeMenu(sender As Object, e As System.EventArgs)
        Try
            ModificaESalvaVolume(-LeggiConfigInt("passovolume", 5), True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ResetVolumeMenu(sender As Object, e As System.EventArgs)
        Try
            Dim volPartenza As Integer = LeggiConfigInt("volumedipartenza", 100)
            If volPartenza < 0 Then volPartenza = 0
            If volPartenza > 100 Then volPartenza = 100

            _volumeCorrente = volPartenza
            ApplicaVolume(_volumeCorrente)

            If LeggiConfigBool("memorizzavolume", True) Then
                _config.SalvaValore("ultimoVolume", _volumeCorrente.ToString())
            End If

            LeggiStatoTTS("Volume riportato al valore iniziale. " & DescriviVolume(_volumeCorrente))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LeggiVolumeMenu(sender As Object, e As System.EventArgs)
        Try
            LeggiVolumeCorrente()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function VelocitaCorrente() As Double
        Try
            If IsYoutubeConBrowser() Then Return _ytVelocita
            If _mpv IsNot Nothing Then Return _mpv.Velocita
            Return 1.0
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return 1.0
        End Try
    End Function

    Private Sub LeggiVelocitaMenu(sender As Object, e As System.EventArgs)
        Try
            LeggiSempreTTS(DescriviVelocita(VelocitaCorrente()))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiMenuSecondario()
        Try
            If _menuSecondario IsNot Nothing Then
                RemoveHandler _menuSecondario.abbandonato, AddressOf MenuSecondarioChiuso
                _menuSecondario.chiudi()
                _menuSecondario = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MenuSecondarioChiuso(sender As Object, e As EventArgs)
        Try
            If _menuSecondario IsNot Nothing Then
                RemoveHandler _menuSecondario.abbandonato, AddressOf MenuSecondarioChiuso
                _menuSecondario = Nothing
            End If

            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _chiusuraInCorso Then Return

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytPlayer.attivatastiera()
                End If
            Else
                If Me.Visible Then AttivaHook()
            End If

            Me.Focus()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function PreparaMenuSecondario() As Boolean
        Try
            ChiudiMenuSecondario()

            NascondiBarraTocco()
            DisattivaHook()
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.disattivatastiera()
            End If

            _menuSecondario = New WGMenu()
            AddHandler _menuSecondario.abbandonato, AddressOf MenuSecondarioChiuso
            Return True
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _menuSecondario = Nothing
            Return False
        End Try
    End Function

    Private Sub AvviaPrefetchCapitoli(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            SyncLock _lockCapitoli
                If _capitoliPerVideo.ContainsKey(url) Then Return
                If _capitoliInCorso.Contains(url) Then Return
                _capitoliInCorso.Add(url)
            End SyncLock

            Dim urlLocale As String = url
            Dim t As New System.Threading.Thread(Sub() LavoroPrefetchCapitoli(urlLocale))
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub LavoroPrefetchCapitoli(url As String)
        Try
            If _provider Is Nothing Then Return
            Dim includiSponsor As Boolean = LeggiConfigBool("sponsorblock_abilitato", False)
            Dim risultato = _provider.GetCapitoli(url, includiSponsor)

            SyncLock _lockCapitoli
                _capitoliPerVideo(url) = risultato.Capitoli
                _sponsorPerVideo(url) = risultato.Sponsor
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        Finally
            Try
                SyncLock _lockCapitoli
                    _capitoliInCorso.Remove(url)
                End SyncLock
            Catch exFinally As Exception
                _logger.ScriviLog(exFinally.ToString())
            End Try
        End Try
    End Sub

    Private Function OttieniCapitoliVideo(url As String) As List(Of CapitoloInfo)
        Try
            SyncLock _lockCapitoli
                Dim capitoliCache As List(Of CapitoloInfo) = Nothing
                If _capitoliPerVideo.TryGetValue(url, capitoliCache) Then
                    Return capitoliCache
                End If
            End SyncLock

            If _provider Is Nothing Then Return New List(Of CapitoloInfo)()

            LeggiSempreTTS("Un momento, sto recuperando i capitoli.")

            Dim includiSponsor As Boolean = LeggiConfigBool("sponsorblock_abilitato", False)
            Dim risultato = _provider.GetCapitoli(url, includiSponsor)

            SyncLock _lockCapitoli
                _capitoliPerVideo(url) = risultato.Capitoli
                _sponsorPerVideo(url) = risultato.Sponsor
            End SyncLock

            Return risultato.Capitoli
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return New List(Of CapitoloInfo)()
        End Try
    End Function

    Private Function FormattaTempoCapitolo(secondi As Double) As String
        Try
            Dim ts As TimeSpan = TimeSpan.FromSeconds(Math.Max(0, secondi))
            If ts.Hours > 0 Then
                Return ts.Hours.ToString() & ":" & ts.Minutes.ToString("D2") & ":" & ts.Seconds.ToString("D2")
            Else
                Return ts.Minutes.ToString() & ":" & ts.Seconds.ToString("D2")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "0:00"
        End Try
    End Function

    Private Sub PreparaListaCapitoli()
        Try
            ChiudiListaCapitoli()

            NascondiBarraTocco()
            DisattivaHook()
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.disattivatastiera()
            End If

            _listaCapitoli = New WGMenuLista()
            _listaCapitoli.ciclono = True
            AddHandler _listaCapitoli.ElementoSelezionato, AddressOf CapitoloSelezionato
            AddHandler _listaCapitoli.escPremuto, AddressOf ListaCapitoliChiusa
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _listaCapitoli = Nothing
        End Try
    End Sub

    Private Sub ChiudiListaCapitoli()
        Try
            If _listaCapitoli IsNot Nothing Then
                RemoveHandler _listaCapitoli.ElementoSelezionato, AddressOf CapitoloSelezionato
                RemoveHandler _listaCapitoli.escPremuto, AddressOf ListaCapitoliChiusa
                _listaCapitoli.abbandona()
                _listaCapitoli = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ListaCapitoliChiusa(sender As Object, e As EventArgs)
        Try
            If _listaCapitoli IsNot Nothing Then
                RemoveHandler _listaCapitoli.ElementoSelezionato, AddressOf CapitoloSelezionato
                RemoveHandler _listaCapitoli.escPremuto, AddressOf ListaCapitoliChiusa
                _listaCapitoli.abbandona()
                _listaCapitoli = Nothing
            End If

            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _chiusuraInCorso Then Return

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytPlayer.attivatastiera()
                End If
            Else
                If Me.Visible Then AttivaHook()
            End If

            Me.Focus()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CapitoloSelezionato(id As String)
        Try
            Dim secondi As Double
            If Double.TryParse(id, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, secondi) Then
                If _mpv IsNot Nothing Then
                    _mpv.ScorriAssoluto(secondi)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' ============================================================================================
    ' ELENCO DEGLI ELEMENTI IN RIPRODUZIONE
    ' ============================================================================================

    Private Sub PreparaListaBrani()
        Try
            ChiudiListaBrani()

            NascondiBarraTocco()
            DisattivaHook()
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.disattivatastiera()
            End If

            _listaBrani = New WGMenuLista()
            _listaBrani.ciclono = True
            AddHandler _listaBrani.ElementoSelezionato, AddressOf BranoSelezionato
            AddHandler _listaBrani.escPremuto, AddressOf ListaBraniChiusa
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _listaBrani = Nothing
        End Try
    End Sub

    Private Sub ChiudiListaBrani()
        Try
            If _listaBrani IsNot Nothing Then
                RemoveHandler _listaBrani.ElementoSelezionato, AddressOf BranoSelezionato
                RemoveHandler _listaBrani.escPremuto, AddressOf ListaBraniChiusa
                _listaBrani.abbandona()
                _listaBrani = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ListaBraniChiusa(sender As Object, e As EventArgs)
        Try
            If _listaBrani IsNot Nothing Then
                RemoveHandler _listaBrani.ElementoSelezionato, AddressOf BranoSelezionato
                RemoveHandler _listaBrani.escPremuto, AddressOf ListaBraniChiusa
                _listaBrani.abbandona()
                _listaBrani = Nothing
            End If

            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _chiusuraInCorso Then Return

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytPlayer.attivatastiera()
                End If
            Else
                If Me.Visible Then AttivaHook()
            End If

            Me.Focus()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub BranoSelezionato(id As String)
        Try
            Dim indice As Integer = -1
            If Not Integer.TryParse(id, indice) Then Return
            If indice < 0 OrElse indice >= elementi.Count Then Return

            ChiudiListaBrani()

            If indice = currentIndex Then
                LeggiStatoTTS("È l'elemento che stai già ascoltando.")
                Return
            End If

            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()
            SalvaPosizioneAttuale()
            SospendiTuttiIPlayer()

            _tempoUltimoCambioElemento = DateTime.Now
            _transizioneInCorso = True
            _inizioTransizione = DateTime.Now
            currentIndex = indice
            RiproduciElementoCorrente(True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriElencoBrani(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriElencoBrani(sender, e)))
                Return
            End If

            If elementi Is Nothing OrElse elementi.Count = 0 Then
                LeggiStatoTTS("Non c'è nessun elemento da riprodurre.")
                Return
            End If

            PreparaListaBrani()
            If _listaBrani Is Nothing Then Return

            _listaBrani.impostaTitolo("Elementi in riproduzione")
            For indice As Integer = 0 To elementi.Count - 1
                Dim descrizione As String = NomeElementoPerElenco(elementi(indice))
                If indice = currentIndex Then descrizione = descrizione & ", in ascolto"
                _listaBrani.aggiungiElemento((indice + 1).ToString() & ". " & descrizione, indice.ToString())
            Next

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                _listaBrani.posiziona(currentIndex.ToString())
            End If

            _listaBrani.avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function NomeElementoPerElenco(file As elemento) As String
        Try
            If Not String.IsNullOrWhiteSpace(file.nome) Then Return file.nome.Trim()

            Select Case file.tipo
                Case ElementType.YouTube
                    Dim titolo As String = TitoloYoutubeDaCache(file.path)
                    If Not String.IsNullOrWhiteSpace(titolo) Then Return titolo
                    Return "video di YouTube"
                Case ElementType.WebRadio
                    Return NomeRadioDaUrl(file.path)
                Case ElementType.StreamTv
                    Return NomeCanaleDaUrl(file.path)
                Case Else
                    Dim nomeFile As String = System.IO.Path.GetFileNameWithoutExtension(file.path)
                    If String.IsNullOrWhiteSpace(nomeFile) Then Return "elemento senza titolo"
                    Return nomeFile
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "elemento senza titolo"
        End Try
    End Function

    ' ============================================================================================
    ' ANNUNCIO DEL TITOLO
    ' ============================================================================================

    Private Sub ApriMenuAnnuncioTitolo(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriMenuAnnuncioTitolo(sender, e)))
                Return
            End If

            If Not PreparaMenuSecondario() Then Return
            If _menuSecondario Is Nothing Then Return

            If LeggiConfigBool("leggititolo", True) Then
                _menuSecondario.aggiungi("Non annunciare il titolo", AddressOf DisattivaAnnuncioTitolo)
            Else
                _menuSecondario.aggiungi("Annuncia il titolo di ogni elemento", AddressOf AttivaAnnuncioTitolo)
            End If

            _menuSecondario.avvia("Annuncio del titolo", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AttivaAnnuncioTitolo(sender As Object, e As System.EventArgs)
        ImpostaOpzioneTitolo("leggititolo", True, "Il titolo verrà annunciato a ogni cambio di elemento.")
    End Sub

    Private Sub DisattivaAnnuncioTitolo(sender As Object, e As System.EventArgs)
        ImpostaOpzioneTitolo("leggititolo", False, "Il titolo non verrà più annunciato.")
    End Sub

    Private Sub ImpostaOpzioneTitolo(chiave As String, valore As Boolean, messaggio As String)
        Try
            If _config IsNot Nothing Then _config.SalvaValore(chiave, If(valore, "true", "false"))
            LeggiStatoTTS(messaggio)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' ============================================================================================
    ' COMMENTI, MI PIACE E NON MI PIACE
    ' ============================================================================================

    Private Function UrlElementoCorrente() As String
        If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then Return elementi(currentIndex).path
        Return ""
    End Function

    Private Async Sub ApriCommentiMenu(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _interazioni Is Nothing OrElse Not IsYoutubeCorrente() Then Return
            Dim url As String = UrlElementoCorrente()
            Dim provider As IInterazioniVideoProvider = _interazioni
            Dim commenti As List(Of CommentoInfo) = Await WGProcessoInCorso.EseguiAsync("Sto leggendo i commenti del video, attendi", Function() Task.Run(Function() provider.GetCommenti(url)))
            If Me.IsDisposed OrElse Me.Disposing OrElse _chiusuraInCorso Then Return
            If commenti Is Nothing Then
                LeggiStatoTTS(If(String.IsNullOrWhiteSpace(provider.UltimoErrore), "Non è stato possibile leggere i commenti.", provider.UltimoErrore))
                Return
            End If
            If commenti.Count = 0 Then
                LeggiStatoTTS("Questo video non ha commenti.")
                Return
            End If

            PreparaListaCommenti()
            If _listaCommenti Is Nothing Then Return
            _commentiMostrati = commenti
            _listaCommenti.impostaTitolo("Commenti, " & commenti.Count.ToString())
            For i As Integer = 0 To commenti.Count - 1
                _listaCommenti.aggiungiElemento(DescriviCommento(commenti(i)), i.ToString())
            Next
            _listaCommenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviCommento(c As CommentoInfo) As String
        Dim testo As String = If(c.Testo, "").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        Dim riga As String = If(String.IsNullOrWhiteSpace(c.Autore), "Anonimo", c.Autore.Trim()) & ": " & testo
        Dim coda As New List(Of String)()
        If c.InEvidenza Then coda.Add("in evidenza")
        If c.MiPiace > 0 Then coda.Add(c.MiPiace.ToString() & If(c.MiPiace = 1, " mi piace", " mi piace"))
        If c.Risposte IsNot Nothing AndAlso c.Risposte.Count > 0 Then coda.Add(c.Risposte.Count.ToString() & If(c.Risposte.Count = 1, " risposta", " risposte"))
        If Not String.IsNullOrWhiteSpace(c.Data) Then coda.Add(c.Data.Trim())
        If coda.Count > 0 Then riga &= ", " & String.Join(", ", coda)
        Return riga
    End Function

    Private Sub PreparaListaCommenti()
        Try
            ChiudiListaCommenti()
            NascondiBarraTocco()
            DisattivaHook()
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.disattivatastiera()
            End If
            _listaCommenti = New WGMenuLista()
            _listaCommenti.ciclono = True
            AddHandler _listaCommenti.ElementoSelezionato, AddressOf CommentoSelezionato
            AddHandler _listaCommenti.escPremuto, AddressOf ListaCommentiChiusa
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _listaCommenti = Nothing
        End Try
    End Sub

    Private Sub ChiudiListaCommenti()
        Try
            If _listaCommenti IsNot Nothing Then
                RemoveHandler _listaCommenti.ElementoSelezionato, AddressOf CommentoSelezionato
                RemoveHandler _listaCommenti.escPremuto, AddressOf ListaCommentiChiusa
                _listaCommenti.abbandona()
                _listaCommenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ListaCommentiChiusa(sender As Object, e As EventArgs)
        Try
            ChiudiListaCommenti()
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _chiusuraInCorso Then Return
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytPlayer.attivatastiera()
                End If
            Else
                If Me.Visible Then AttivaHook()
            End If
            Me.Focus()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Con Invio su un commento si legge intero, con le risposte, in un campo di sola lettura.
    Private Sub CommentoSelezionato(id As String)
        Try
            Dim n As Integer
            If Not Integer.TryParse(id, n) OrElse n < 0 OrElse n >= _commentiMostrati.Count Then Return
            Dim c As CommentoInfo = _commentiMostrati(n)
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine(DescriviIntestazioneCommento(c))
            testo.AppendLine()
            testo.AppendLine(If(c.Testo, "").Trim())
            If c.Risposte IsNot Nothing AndAlso c.Risposte.Count > 0 Then
                testo.AppendLine()
                testo.AppendLine(If(c.Risposte.Count = 1, "Una risposta:", c.Risposte.Count.ToString() & " risposte:"))
                For Each r As CommentoInfo In c.Risposte
                    testo.AppendLine()
                    testo.AppendLine(DescriviIntestazioneCommento(r))
                    testo.AppendLine(If(r.Testo, "").Trim())
                Next
            End If
            Dim campo As New WGCampo()
            campo.init("Commento di " & If(String.IsNullOrWhiteSpace(c.Autore), "anonimo", c.Autore.Trim()), testo.ToString().Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf), True, True)
            campo.avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviIntestazioneCommento(c As CommentoInfo) As String
        Dim parti As New List(Of String)()
        parti.Add(If(String.IsNullOrWhiteSpace(c.Autore), "Anonimo", c.Autore.Trim()) & If(c.DelCanale, " (autore del video)", ""))
        If c.InEvidenza Then parti.Add("in evidenza")
        If c.MiPiace > 0 Then parti.Add(c.MiPiace.ToString() & " mi piace")
        If Not String.IsNullOrWhiteSpace(c.Data) Then parti.Add(c.Data.Trim())
        Return String.Join(", ", parti)
    End Function

    Private Sub MiPiaceMenu(sender As Object, e As System.EventArgs)
        Reagisci("mipiace", "Sto mettendo mi piace al video, attendi", "Mi piace aggiunto.")
    End Sub

    Private Sub NonMiPiaceMenu(sender As Object, e As System.EventArgs)
        Reagisci("nonmipiace", "Sto mettendo non mi piace al video, attendi", "Non mi piace aggiunto.")
    End Sub

    Private Sub TogliReazioneMenu(sender As Object, e As System.EventArgs)
        Reagisci("togli", "Sto togliendo la tua reazione al video, attendi", "Reazione tolta.")
    End Sub

    Private Async Sub Reagisci(reazione As String, attesa As String, riuscito As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _interazioni Is Nothing OrElse Not IsYoutubeCorrente() Then Return
            Dim url As String = UrlElementoCorrente()
            Dim provider As IInterazioniVideoProvider = _interazioni
            Dim esito As Boolean = Await WGProcessoInCorso.EseguiAsync(attesa, Function() Task.Run(Function() provider.Reagisci(url, reazione)))
            If Me.IsDisposed OrElse Me.Disposing OrElse _chiusuraInCorso Then Return
            If esito Then
                LeggiStatoTTS(riuscito)
            Else
                LeggiStatoTTS(If(String.IsNullOrWhiteSpace(provider.UltimoErrore), "Non è stato possibile completare l'operazione.", provider.UltimoErrore))
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuCapitoli(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriMenuCapitoli(sender, e)))
                Return
            End If

            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return
            If Not IsYoutubeConMpv() Then
                LeggiStatoTTS("I capitoli sono disponibili solo con il lettore interno MPV.")
                Return
            End If
            If _provider Is Nothing Then
                LeggiStatoTTS("Impossibile recuperare i capitoli in questo momento.")
                Return
            End If

            Dim urlVideoCorrente As String = elementi(currentIndex).path
            Dim capitoli As List(Of CapitoloInfo) = OttieniCapitoliVideo(urlVideoCorrente)

            If capitoli Is Nothing OrElse capitoli.Count = 0 Then
                LeggiStatoTTS("Nessun capitolo disponibile per questo video.")
                Return
            End If

            PreparaListaCapitoli()
            If _listaCapitoli Is Nothing Then Return

            _listaCapitoli.impostaTitolo("Capitoli")
            For Each capitolo In capitoli
                _listaCapitoli.aggiungiElemento(FormattaTempoCapitolo(capitolo.StartSeconds) & " - " & capitolo.Title, capitolo.StartSeconds.ToString(Globalization.CultureInfo.InvariantCulture))
            Next

            _listaCapitoli.avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub OnMpvCambioProprietaCapitoli(sender As Object, e As PropertyChangedEventArgs)
        Try
            If e Is Nothing OrElse e.Nome <> "time-pos" Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return
            If elementi(currentIndex).tipo <> ElementType.YouTube Then Return
            If Not IsYoutubeConMpv() Then Return

            Dim posizioneAttuale As Double = 0
            If e.Valore IsNot Nothing Then posizioneAttuale = CDbl(e.Valore)

            Dim urlVideoCorrente As String = elementi(currentIndex).path

            If LeggiConfigBool("sponsorblock_abilitato", False) Then
                GestisciSponsorBlock(urlVideoCorrente, posizioneAttuale)
            End If

            If LeggiConfigBool("lettura_capitoli_automatica", False) Then
                GestisciLetturaCapitoloAutomatica(urlVideoCorrente, posizioneAttuale)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciSponsorBlock(url As String, posizione As Double)
        Try
            Dim segmenti As List(Of SponsorSegmentoInfo) = Nothing
            SyncLock _lockCapitoli
                _sponsorPerVideo.TryGetValue(url, segmenti)
            End SyncLock
            If segmenti Is Nothing OrElse segmenti.Count = 0 Then Return

            For Each segmento In segmenti
                If posizione >= segmento.StartSeconds AndAlso posizione < segmento.EndSeconds - 0.25 Then
                    If _ultimoSegmentoSponsorSaltato Is segmento Then Return
                    _ultimoSegmentoSponsorSaltato = segmento
                    If _mpv IsNot Nothing Then _mpv.ScorriAssoluto(segmento.EndSeconds)
                    If LeggiConfigBool("sponsorblock_annuncio", False) Then
                        LeggiSempreTTS("Sponsor saltato.")
                    End If
                    Return
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciLetturaCapitoloAutomatica(url As String, posizione As Double)
        Try
            Dim capitoli As List(Of CapitoloInfo) = Nothing
            SyncLock _lockCapitoli
                _capitoliPerVideo.TryGetValue(url, capitoli)
            End SyncLock
            If capitoli Is Nothing OrElse capitoli.Count = 0 Then Return

            For i As Integer = 0 To capitoli.Count - 1
                Dim capitolo = capitoli(i)
                Dim fineCapitolo As Double = If(capitolo.EndSeconds > 0, capitolo.EndSeconds, Double.MaxValue)
                If posizione >= capitolo.StartSeconds AndAlso posizione < fineCapitolo Then
                    If i <> _ultimoIndiceCapitoloAnnunciato Then
                        _ultimoIndiceCapitoloAnnunciato = i
                        LeggiSempreTTS(capitolo.Title)
                    End If
                    Return
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuVolume(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriMenuVolume(sender, e)))
                Return
            End If

            If Not PreparaMenuSecondario() Then Return
            If _menuSecondario Is Nothing Then Return

            Dim passo As Integer = LeggiConfigInt("passovolume", 5)

            _menuSecondario.aggiungi("Alza il volume di " & passo.ToString() & " per cento", AddressOf AumentaVolumeMenu)
            _menuSecondario.aggiungi("Abbassa il volume di " & passo.ToString() & " per cento", AddressOf DiminuisciVolumeMenu)
            _menuSecondario.aggiungi("Riporta il volume al valore iniziale", AddressOf ResetVolumeMenu)
            _menuSecondario.aggiungi("Volume attuale", AddressOf LeggiVolumeMenu)
            _menuSecondario.avvia("Volume", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuVelocita(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriMenuVelocita(sender, e)))
                Return
            End If

            If Not PreparaMenuSecondario() Then Return
            If _menuSecondario Is Nothing Then Return

            _menuSecondario.aggiungi("Riproduci più velocemente", AddressOf AumentaVelocitaMenu)
            _menuSecondario.aggiungi("Riproduci più lentamente", AddressOf DiminuisciVelocitaMenu)
            _menuSecondario.aggiungi("Torna alla velocità normale", AddressOf ResetVelocitaMenu)
            _menuSecondario.aggiungi("Velocità attuale", AddressOf LeggiVelocitaMenu)
            _menuSecondario.avvia("Velocità", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviMinutiSpegnimento(minuti As Integer) As String
        Select Case minuti
            Case 60 : Return "un'ora"
            Case 90 : Return "un'ora e mezza"
            Case 120 : Return "due ore"
        End Select
        Return minuti.ToString() & " minuti"
    End Function

    Private Sub ApriMenuSpegnimentoAscolto(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ApriMenuSpegnimentoAscolto(sender, e)))
                Return
            End If

            If Not PreparaMenuSecondario() Then Return
            If _menuSecondario Is Nothing Then Return

            For Each valore As Integer In New Integer() {5, 10, 15, 20, 30, 45, 60, 90, 120}
                Dim minuti As Integer = valore
                _menuSecondario.aggiungi(DescriviMinutiSpegnimento(minuti),
                    Sub(s, ev)
                        Try
                            WGRiproduzione.ProgrammaInterruzione(TimeSpan.FromMinutes(minuti))
                            ChiudiMenuSecondario()
                            LeggiStatoTTS("La musica si spegnerà tra " & DescriviMinutiSpegnimento(minuti) & ".")
                        Catch ex As Exception
                            _logger.ScriviLog(ex.ToString())
                        End Try
                    End Sub)
            Next
            If WGRiproduzione.InterruzioneProgrammata Then
                _menuSecondario.aggiungi("Annulla lo spegnimento",
                    Sub(s, ev)
                        Try
                            WGRiproduzione.AnnullaInterruzione()
                            ChiudiMenuSecondario()
                            LeggiStatoTTS("Spegnimento della musica annullato.")
                        Catch ex As Exception
                            _logger.ScriviLog(ex.ToString())
                        End Try
                    End Sub)
            End If
            _menuSecondario.avvia("Spegni la musica dopo", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function DescriviVelocita(valore As Double) As String
        Try
            Dim percentuale As Integer = CInt(Math.Round(valore * 100.0))
            Return "Velocità al " & percentuale.ToString() & " per cento."
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return "Velocità cambiata."
        End Try
    End Function

    Private Sub AumentaVelocitaMenu(sender As Object, e As System.EventArgs)
        Try
            Dim stepVel = LeggiConfigDouble("passovelocita", 0.1)
            If IsYoutubeConBrowser() Then
                _ytVelocita = Math.Min(4.0, _ytVelocita + stepVel)
                If _ytPlayer IsNot Nothing Then _ytPlayer.ImpostaVelocita(_ytVelocita)
                LeggiStatoTTS(DescriviVelocita(_ytVelocita))
            ElseIf _mpv IsNot Nothing Then
                _mpv.Velocita = Math.Min(4.0, _mpv.Velocita + stepVel)
                LeggiStatoTTS(DescriviVelocita(_mpv.Velocita))
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub DiminuisciVelocitaMenu(sender As Object, e As System.EventArgs)
        Try
            Dim stepVel = LeggiConfigDouble("passovelocita", 0.1)
            If IsYoutubeConBrowser() Then
                _ytVelocita = Math.Max(0.1, _ytVelocita - stepVel)
                If _ytPlayer IsNot Nothing Then _ytPlayer.ImpostaVelocita(_ytVelocita)
                LeggiStatoTTS(DescriviVelocita(_ytVelocita))
            ElseIf _mpv IsNot Nothing Then
                _mpv.Velocita = Math.Max(0.1, _mpv.Velocita - stepVel)
                LeggiStatoTTS(DescriviVelocita(_mpv.Velocita))
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ResetVelocitaMenu(sender As Object, e As System.EventArgs)
        Try
            If IsYoutubeConBrowser() Then
                _ytVelocita = 1.0
                If _ytPlayer IsNot Nothing Then _ytPlayer.ImpostaVelocita(_ytVelocita)
                LeggiStatoTTS("Velocità riportata a quella normale.")
            ElseIf _mpv IsNot Nothing Then
                _mpv.Velocita = 1.0
                LeggiStatoTTS("Velocità riportata a quella normale.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub VaiInizioMenu(sender As Object, e As EventArgs)
        OnHomePressed()
    End Sub

    Private Sub VaiFineMenu(sender As Object, e As EventArgs)
        OnEndPressed()
    End Sub

    Private Sub ToggleRipetiTraccia(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() ToggleRipetiTraccia(sender, e))
                Return
            End If
            _ripetiTraccia = Not _ripetiTraccia
            _ripetiPlaylist = False
            _riproduzioneCasuale = False

            If _ripetiTraccia Then
                LeggiStatoTTS("Da ora questo elemento verrà ripetuto in continuazione.")
            Else
                LeggiStatoTTS("Questo elemento non verrà più ripetuto.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ToggleRipetiPlaylist(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() ToggleRipetiPlaylist(sender, e))
                Return
            End If
            _ripetiPlaylist = Not _ripetiPlaylist
            _ripetiTraccia = False
            _riproduzioneCasuale = False

            If _ripetiPlaylist Then
                LeggiStatoTTS("Da ora l'elenco ricomincerà da capo quando finisce.")
            Else
                LeggiStatoTTS("L'elenco non ricomincerà più da capo.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ToggleRiproduzioneCasuale(sender As Object, e As System.EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() ToggleRiproduzioneCasuale(sender, e))
                Return
            End If
            _riproduzioneCasuale = Not _riproduzioneCasuale
            _ripetiTraccia = False
            _ripetiPlaylist = False

            If _riproduzioneCasuale Then
                LeggiStatoTTS("Da ora gli elementi verranno riprodotti in ordine casuale.")
                Dim n = elementi.Count
                While n > 1
                    n -= 1
                    Dim k = _generatoreCasuale.Next(n + 1)
                    Dim value = elementi(k)
                    elementi(k) = elementi(n)
                    elementi(n) = value
                End While

                currentIndex = -1
                If pausa Then
                    RiproduciProssimoElemento(True)
                End If
            Else
                LeggiStatoTTS("Gli elementi tornano nel loro ordine originale.")
                elementi.Clear()
                elementi.AddRange(_elementiCasualiOriginali)
                currentIndex = 0
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub aggiungiopzione(nome As String, evento As EventHandler)
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return
            If Not opzioneesiste(nome) Then
                _opzioniEsterne.Add(New opzione With {.nome = nome, .evento = evento})
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub rimuoviopzione(nome As String)
        Try
            Dim op = _opzioniEsterne.FirstOrDefault(Function(x) x.nome = nome)
            If op IsNot Nothing Then
                _opzioniEsterne.Remove(op)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function opzioneesiste(nome As String) As Boolean
        Try
            Return _opzioniEsterne.Any(Function(x) x.nome = nome)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AggiungiVoceMenu(nome As String, evento As EventHandler)
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return
            For Each esistente In _listaOpzioni
                If esistente.nome = nome Then Return
            Next
            _listaOpzioni.Add(New opzione With {.nome = nome, .evento = evento})
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CostruisciMenuOpzioni(eraInRiproduzione As Boolean)
        Try
            _listaOpzioni.Clear()

            Dim piuElementi As Boolean = elementi.Count > 1
            Dim isLive As Boolean = IsLiveSenzaSeek()

            If eraInRiproduzione Then
                AggiungiVoceMenu("Metti in pausa", AddressOf RiproduciPausaMenu)
            Else
                AggiungiVoceMenu("Riprendi la riproduzione", AddressOf RiproduciPausaMenu)
            End If

            If piuElementi Then
                AggiungiVoceMenu("Vai all'elemento precedente", AddressOf RiproduciElementoPrecedenteMenu)
                AggiungiVoceMenu("Vai all'elemento successivo", AddressOf RiproduciProssimoElementoMenu)
                AggiungiVoceMenu("Elenco degli elementi", AddressOf ApriElencoBrani)
            End If

            If Not isLive Then
                Dim passoBreve As Integer = LeggiConfigInt("passoscorrimento", 30)
                Dim passoLungo As Integer = LeggiConfigInt("passoscorrimentolungo", 60)

                AggiungiVoceMenu("Torna indietro di " & DescriviSecondi(passoBreve), AddressOf IndietroBreveMenu)
                AggiungiVoceMenu("Vai avanti di " & DescriviSecondi(passoBreve), AddressOf AvantiBreveMenu)

                If passoLungo <> passoBreve Then
                    AggiungiVoceMenu("Torna indietro di " & DescriviSecondi(passoLungo), AddressOf IndietroLungoMenu)
                    AggiungiVoceMenu("Vai avanti di " & DescriviSecondi(passoLungo), AddressOf AvantiLungoMenu)
                End If

                AggiungiVoceMenu("Vai all'inizio", AddressOf VaiInizioMenu)
                AggiungiVoceMenu("Vai alla fine", AddressOf VaiFineMenu)

                AggiungiVoceMenu("Tempo trascorso", AddressOf LeggiTempoTrascorsoMenu)
                AggiungiVoceMenu("Tempo rimanente", AddressOf LeggiTempoRimanenteMenu)
                AggiungiVoceMenu("Durata totale", AddressOf LeggiDurataTotaleMenu)
            End If

            If IsYoutubeConMpv() AndAlso _provider IsNot Nothing Then
                AggiungiVoceMenu("Capitoli", AddressOf ApriMenuCapitoli)
            End If

            If IsYoutubeCorrente() AndAlso _interazioni IsNot Nothing Then
                AggiungiVoceMenu("Commenti", AddressOf ApriCommentiMenu)
                AggiungiVoceMenu("Mi piace", AddressOf MiPiaceMenu)
                AggiungiVoceMenu("Non mi piace", AddressOf NonMiPiaceMenu)
                AggiungiVoceMenu("Togli mi piace o non mi piace", AddressOf TogliReazioneMenu)
            End If

            AggiungiVoceMenu("Volume", AddressOf ApriMenuVolume)
            AggiungiVoceMenu("Annuncio del titolo", AddressOf ApriMenuAnnuncioTitolo)

            If Not isLive Then
                AggiungiVoceMenu("Velocità", AddressOf ApriMenuVelocita)
            End If

            AggiungiVoceMenu("Spegni la musica dopo", AddressOf ApriMenuSpegnimentoAscolto)

            If _ripetiTraccia Then
                AggiungiVoceMenu("Non ripetere più questo elemento", AddressOf ToggleRipetiTraccia)
            Else
                AggiungiVoceMenu("Ripeti sempre questo elemento", AddressOf ToggleRipetiTraccia)
            End If

            If piuElementi Then
                If _ripetiPlaylist Then
                    AggiungiVoceMenu("Non ricominciare l'elenco quando finisce", AddressOf ToggleRipetiPlaylist)
                Else
                    AggiungiVoceMenu("Ricomincia l'elenco quando finisce", AddressOf ToggleRipetiPlaylist)
                End If

                If _riproduzioneCasuale Then
                    AggiungiVoceMenu("Torna all'ordine originale", AddressOf ToggleRiproduzioneCasuale)
                Else
                    AggiungiVoceMenu("Riproduci in ordine casuale", AddressOf ToggleRiproduzioneCasuale)
                End If
            End If

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count AndAlso elementi(currentIndex).tipo = ElementType.File Then
                If _elementoPreferito Then
                    AggiungiVoceMenu("Togli dai preferiti", AddressOf RimuoviPreferito)
                Else
                    AggiungiVoceMenu("Aggiungi ai preferiti", AddressOf AggiungiPreferito)
                End If
            End If

            For Each esterna In _opzioniEsterne
                AggiungiVoceMenu(esterna.nome, esterna.evento)
            Next
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub apriopzioni()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf apriopzioni))
                Return
            End If
            If menuopzioniaperto Then Return

            Dim ytConBrowser As Boolean = IsYoutubeConBrowser()

            NascondiBarraTocco()
            AnnullaRipristinoPausa()
            DisattivaHook()
            If ytConBrowser Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.disattivatastiera()
            End If

            _wasPlayingBeforeOptions = Not pausa

            If Not pausa Then
                If ytConBrowser Then
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                ElseIf _mpv IsNot Nothing Then
                    _mpv.Pausa()
                End If
                pausa = True
            End If

            menuopzioniaperto = True

            CostruisciMenuOpzioni(_wasPlayingBeforeOptions)

            Menuopzioni = New WGListaView()
            Menuopzioni.Titolo = "Opzioni"
            Menuopzioni.SelectionMode = SelectionMode.One
            Menuopzioni.Dock = DockStyle.Fill
            Menuopzioni.Enabled = True

            For Each opz In _listaOpzioni
                Menuopzioni.Items.Add(opz.nome)
            Next

            If Not lettura.NvdaInEsecuzione() Then
                Menuopzioni.Enabled = True
                Menuopzioni.AccessibleRole = AccessibleRole.None
            End If

            Me.Controls.Add(Menuopzioni)
            Menuopzioni.BringToFront()
            Menuopzioni.Visible = True
            Menuopzioni.Focus()

            If Menuopzioni.Items.Count > 0 Then
                Menuopzioni.SelectedIndex = 0
            End If

            If ytConBrowser Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.ImpostaVisibilita(False)
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SelezionaOpzioneCorrente()
        Try
            If Menuopzioni IsNot Nothing AndAlso Menuopzioni.SelectedItem IsNot Nothing Then
                Dim testoVoce As String = Menuopzioni.SelectedItem.ToString()
                Dim op = _listaOpzioni.FirstOrDefault(Function(x) x.nome = testoVoce)
                chiudiopzioni()
                If op IsNot Nothing Then
                    op.evento.Invoke(Me, EventArgs.Empty)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaRipristinoPausa()
        Try
            If _tmrRipristinaPausa Is Nothing Then
                _tmrRipristinaPausa = New System.Windows.Forms.Timer()
                _tmrRipristinaPausa.Interval = 400
                AddHandler _tmrRipristinaPausa.Tick, AddressOf RipristinaPausaTick
            End If
            _tmrRipristinaPausa.Stop()
            _tmrRipristinaPausa.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaRipristinoPausa()
        Try
            If _tmrRipristinaPausa IsNot Nothing Then
                _tmrRipristinaPausa.Stop()
            End If
            AnnullaVigilanzaPausa()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RipristinaPausaTick(sender As Object, e As EventArgs)
        Try
            If _tmrRipristinaPausa IsNot Nothing Then _tmrRipristinaPausa.Stop()
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If menuopzioniaperto Then Return
            If Not pausa Then Return

            ForzaPausa()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ForzaPausa()
        Try
            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
            ElseIf _mpv IsNot Nothing Then
                If Not _mpv.InPausa Then
                    _mpv.Pausa()
                End If
            End If
            pausa = True
            _pausaRichiesta = True
            AvviaVigilanzaPausa()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub chiudiopzioni()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() chiudiopzioni())
                Return
            End If

            menuopzioniaperto = False

            If Menuopzioni IsNot Nothing Then
                Me.Controls.Remove(Menuopzioni)
                Menuopzioni.Dispose()
                Menuopzioni = Nothing
            End If

            Dim ytConBrowser As Boolean = IsYoutubeConBrowser()

            If ytConBrowser Then
                If _ytPlayer IsNot Nothing Then
                    _ytPlayer.ImpostaVisibilita(True)
                    _ytPlayer.PortaInPrimoPiano()
                    _ytPlayer.attivatastiera()
                End If
            Else
                If Me.Visible Then
                    AttivaHook()
                End If
            End If

            If _wasPlayingBeforeOptions Then
                Dim richiedeRete As Boolean = IsWebRadioCorrente() OrElse IsStreamTvCorrente() OrElse ytConBrowser OrElse IsYoutubeConMpv()
                If richiedeRete Then
                    If Not IsNetworkAvailable() Then
                        Dim msgBox As New WGMessaggi()
                        msgBox.Messaggio("Non c'è collegamento a internet: non è possibile riprendere la riproduzione.")
                        msgBox.ShowDialog()
                        FermaPlayerCorrente()
                        pausa = True
                        GoTo fine
                    End If
                End If

                AnnullaVigilanzaPausa()
                If ytConBrowser Then
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Riproduci()
                ElseIf _mpv IsNot Nothing Then
                    _mpv.Riproduci()
                End If
                pausa = False
            Else
                ForzaPausa()
                AvviaRipristinoPausa()
            End If

fine:
            Me.Focus()

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub chiudi(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() chiudi(Nothing, Nothing))
                Return
            End If

            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()

            SalvaPosizioneAttuale()

            Dim riproduzioneInCorso As Boolean = False
            Dim ytConBrowser As Boolean = IsYoutubeConBrowser()

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                If ytConBrowser Then
                    If Not pausa Then riproduzioneInCorso = True
                ElseIf _mpv IsNot Nothing AndAlso Not _mpv.InPausa Then
                    riproduzioneInCorso = True
                End If
            End If

            Dim soglia As Integer = LeggiConfigInt("sogliafinebrano", 10)
            If soglia < 0 Then soglia = 0
            Dim rimanenti As Double = SecondiRimanenti()
            Dim vicinoAllaFine As Boolean = (rimanenti >= 0 AndAlso rimanenti <= soglia)
            Dim ciSonoAltri As Boolean = EsistonoElementiSuccessivi()
            Dim inutileChiedere As Boolean = vicinoAllaFine AndAlso Not ciSonoAltri

            If riproduzioneInCorso AndAlso Not inutileChiedere Then
                Try
                    If ytConBrowser Then
                        If _ytPlayer IsNot Nothing Then
                            _ytPlayer.Pausa()
                            pausa = True
                        End If
                    ElseIf _mpv IsNot Nothing Then
                        _mpv.Pausa()
                        pausa = True
                    End If
                Catch exPausa As Exception
                    _logger.ScriviLog(exPausa.ToString())
                End Try
                _pausaRichiesta = True
                AvviaVigilanzaPausa()
                NascondiBarraTocco()
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf RispostaDomandaChiusura
                domanda.avvia("Vuoi continuare ad ascoltare in sottofondo?")
            Else
                _chiusuraInCorso = True
                If ytConBrowser Then
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                Else
                    If _mpv IsNot Nothing Then _mpv.FermaRiproduzione()
                End If
                FinalizzaChiusura()
            End If

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            FinalizzaChiusura()
        End Try
    End Sub

    Private Sub RispostaDomandaChiusura(risposta As DialogResult)
        Try
            domanda = Nothing
            _chiusuraInCorso = True
            Dim ytConBrowser As Boolean = IsYoutubeConBrowser()

            If risposta = DialogResult.Yes Then
                AnnullaVigilanzaPausa()
                If ytConBrowser Then
                    If _ytPlayer IsNot Nothing Then
                        DistruggiYoutubeSottofondoPrecedente()
                        Try
                            RemoveHandler _ytPlayer.MediaCaricato, AddressOf OnYtMediaCaricato
                            RemoveHandler _ytPlayer.VideoTerminato, AddressOf OnYtVideoTerminato
                            RemoveHandler _ytPlayer.ErroreMedia, AddressOf OnYtErroreMedia
                            RemoveHandler _ytPlayer.VideoNonDisponibile, AddressOf OnYtVideoNonDisponibile
                            RemoveHandler _ytPlayer.altpremuto, AddressOf _ytPlayer_altpremuto
                            RemoveHandler _ytPlayer.escpremuto, AddressOf _ytPlayer_escpremuto
                            RemoveHandler _ytPlayer.tastopremuto, AddressOf _ytPlayer_tastopremuto
                            _ytPlayer.Riproduci()
                            pausa = False
                            _ytPlayer.ContinuaInSottofondo()
                            AgganciaYoutubeSottofondoAPannello()
                            _ytPlayerSottofondoCorrente = _ytPlayer
                        Catch exRipresaYt As Exception
                            _logger.ScriviLog(exRipresaYt.ToString())
                        End Try
                        _ytPlayer = Nothing
                    End If
                ElseIf _mpv IsNot Nothing Then
                    Try
                        _mpv.Riproduci()
                        pausa = False
                    Catch exRipresa As Exception
                        _logger.ScriviLog(exRipresa.ToString())
                    End Try
                    _mpv.ContinuaInSottofondo()
                    RemoveHandler _mpv.MediaCaricato, AddressOf OnMediaOpened
                    RemoveHandler _mpv.EndFile, AddressOf OnMediaEnded
                    RemoveHandler _mpv.ErroreMedia, AddressOf OnMediaFailed
                    _mpv = Nothing
                    Dim centro = WGCentroDiControllo.GetInstance()
                    If centro IsNot Nothing Then
                        centro.SincronizzaRiproduzioneBackground(elementi, indiceCorrente:=currentIndex)
                    End If
                End If
            Else
                If ytConBrowser Then
                    If _ytPlayer IsNot Nothing Then _ytPlayer.Pausa()
                ElseIf _mpv IsNot Nothing Then
                    _mpv.FermaRiproduzione()
                End If
            End If
            FinalizzaChiusura()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            FinalizzaChiusura()
        End Try
    End Sub

    Private Sub FinalizzaChiusura()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(Sub() FinalizzaChiusura())
                Return
            End If

            _chiusuraInCorso = True
            _transizioneInCorso = False
            _avvioRinviato = False
            _mediaProntoInAttesa = False

            If _istanzaCorrente Is Me Then
                _istanzaCorrente = Nothing
            End If

            ChiudiMenuSecondario()
            ChiudiListaCapitoli()
            ChiudiListaBrani()
            DistruggiBarraTocco()

            DisattivaHook()

            AnnullaAttesaLettura()
            AnnullaAttesaTitolo()
            AnnullaRipristinoPausa()
            FermaTimerAvvisoRiconnessione()
            FermaAttesaLettore()
            ChiudiProcessoCaricamento(True)

            If _tmrAttesaLettura IsNot Nothing Then
                RemoveHandler _tmrAttesaLettura.Tick, AddressOf AttesaLetturaTick
                _tmrAttesaLettura.Dispose()
                _tmrAttesaLettura = Nothing
            End If

            If tmrAttesaLettore IsNot Nothing Then
                RemoveHandler tmrAttesaLettore.Tick, AddressOf AttesaLettoreTick
                tmrAttesaLettore.Dispose()
                tmrAttesaLettore = Nothing
            End If

            If _tmrAttesaTitolo IsNot Nothing Then
                RemoveHandler _tmrAttesaTitolo.Tick, AddressOf AttesaTitoloTick
                _tmrAttesaTitolo.Dispose()
                _tmrAttesaTitolo = Nothing
            End If

            If _tmrRipristinaPausa IsNot Nothing Then
                RemoveHandler _tmrRipristinaPausa.Tick, AddressOf RipristinaPausaTick
                _tmrRipristinaPausa.Dispose()
                _tmrRipristinaPausa = Nothing
            End If

            If _tmrVigilanzaPausa IsNot Nothing Then
                _tmrVigilanzaPausa.Stop()
                RemoveHandler _tmrVigilanzaPausa.Tick, AddressOf VigilanzaPausaTick
                _tmrVigilanzaPausa.Dispose()
                _tmrVigilanzaPausa = Nothing
            End If

            If tmrTimeoutCaricamento IsNot Nothing Then
                tmrTimeoutCaricamento.Stop()
                RemoveHandler tmrTimeoutCaricamento.Tick, AddressOf TimeoutCaricamentoTick
                tmrTimeoutCaricamento.Dispose()
                tmrTimeoutCaricamento = Nothing
            End If

            If Menuopzioni IsNot Nothing Then
                Me.Controls.Remove(Menuopzioni)
                Menuopzioni.Dispose()
                Menuopzioni = Nothing
            End If
            _listaOpzioni.Clear()
            _opzioniEsterne.Clear()

            If _mediaPlayerStateTimer IsNot Nothing Then
                _mediaPlayerStateTimer.Stop()
                RemoveHandler _mediaPlayerStateTimer.Tick, AddressOf OnMediaPlayerStateTimerTick
                _mediaPlayerStateTimer.Dispose()
                _mediaPlayerStateTimer = Nothing
            End If

            RaiseEvent chiudiplayer()

            DistruggiMpv()
            DistruggiYoutube()
            WGRiproduzione.SegnalaFine()

            _clsBrani.Dispose()

            Try
                Dim contenitoreChiusura As WGContenitore = WGContenitore.Istanza
                If contenitoreChiusura IsNot Nothing AndAlso Not contenitoreChiusura.IsDisposed Then
                    contenitoreChiusura.RimuoviControllo(Me)
                End If
            Catch exContenitore As Exception
                _logger.ScriviLog(exContenitore.ToString())
            End Try

            Me.Visible = False
            Me.Dispose()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AvviaAscoltoSottofondo(path As String, Optional tipo As ElementType? = Nothing)
        Try
            If String.IsNullOrWhiteSpace(path) Then Return

            InizializzaConfigurazioni()

            Dim finalPath As String = path.Trim()
            Dim finalType As ElementType

            If tipo.HasValue Then
                finalType = tipo.Value
                If finalType = ElementType.YouTube Then
                    finalPath = CleanYouTubeUrl(finalPath)
                End If
            Else
                If Uri.IsWellFormedUriString(finalPath, UriKind.Absolute) Then
                    If finalPath.ToLower().Contains("youtube.com/watch?v=") OrElse finalPath.ToLower().Contains("youtu.be/") Then
                        finalType = ElementType.YouTube
                        finalPath = CleanYouTubeUrl(finalPath)
                    Else
                        finalType = ElementType.WebRadio
                    End If
                Else
                    finalType = ElementType.File
                End If
            End If

            elementi.Clear()
            _elementiCasualiOriginali.Clear()
            Dim item As New elemento With {.path = finalPath, .tipo = finalType}
            elementi.Add(item)
            _elementiCasualiOriginali.Add(item)
            currentIndex = 0

            If _mpv Is Nothing Then
                _mpv = WGMpv.GetInstance()
                If _mpv Is Nothing Then
                    Return
                End If
                AddHandler _mpv.MediaCaricato, AddressOf OnMediaOpened
                AddHandler _mpv.EndFile, AddressOf OnMediaEnded
                AddHandler _mpv.ErroreMedia, AddressOf OnMediaFailed
            End If

            _mpv.ContinuaInSottofondo()

            _avvioRinviato = False
            _mediaProntoInAttesa = False
            _transizioneInCorso = False

            ImpostaVolumeIniziale()

            _tempoUltimoAvvioRiproduzione = DateTime.Now

            If finalType = ElementType.YouTube Then
                _mpv.RiproduciVideoYoutube(finalPath)
            Else
                _mpv.CaricaFile(finalPath)
            End If

            pausa = False
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CreaBarraTocco()
        Try
            If _barraTocco IsNot Nothing Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim tocco As WGTouk = WGTouk.GetInstance()
            If tocco Is Nothing Then Return

            _barraTocco = New WGToccoPlayer(Me)
            Me.Controls.Add(_barraTocco)
            _barraTocco.BringToFront()

            RemoveHandler Me.Disposed, AddressOf BarraToccoProprietarioDismesso
            AddHandler Me.Disposed, AddressOf BarraToccoProprietarioDismesso

            _barraTocco.Avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            _barraTocco = Nothing
        End Try
    End Sub

    Private Sub NascondiBarraTocco()
        Try
            If _barraTocco Is Nothing Then Return
            If _barraTocco.IsDisposed Then Return
            _barraTocco.Nascondi()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub BarraToccoProprietarioDismesso(sender As Object, e As EventArgs)
        Try
            DistruggiBarraTocco()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub DistruggiBarraTocco()
        Try
            If _barraTocco Is Nothing Then Return

            _barraTocco.Termina()

            Try
                If Me.Controls.Contains(_barraTocco) Then Me.Controls.Remove(_barraTocco)
            Catch exRimozione As Exception
                _logger.ScriviLog(exRimozione.ToString())
            End Try

            If Not _barraTocco.IsDisposed Then _barraTocco.Dispose()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        Finally
            _barraTocco = Nothing
        End Try
    End Sub

    Private Sub AvviaVigilanzaPausa()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If _tmrVigilanzaPausa Is Nothing Then
                _tmrVigilanzaPausa = New System.Windows.Forms.Timer()
                _tmrVigilanzaPausa.Interval = 250
                AddHandler _tmrVigilanzaPausa.Tick, AddressOf VigilanzaPausaTick
            End If

            _fineVigilanzaPausa = DateTime.Now.AddSeconds(12)
            _tmrVigilanzaPausa.Stop()
            _tmrVigilanzaPausa.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaVigilanzaPausa()
        Try
            _pausaRichiesta = False
            _fineVigilanzaPausa = DateTime.MinValue
            If _tmrVigilanzaPausa IsNot Nothing Then _tmrVigilanzaPausa.Stop()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub VigilanzaPausaTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                If _tmrVigilanzaPausa IsNot Nothing Then _tmrVigilanzaPausa.Stop()
                Return
            End If

            If Not _pausaRichiesta OrElse DateTime.Now > _fineVigilanzaPausa Then
                If _tmrVigilanzaPausa IsNot Nothing Then _tmrVigilanzaPausa.Stop()
                Return
            End If

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing AndAlso Not _ytPlayer.IsDisposed Then
                    If Not _ytPlayer.InPausa Then
                        _ytPlayer.Pausa()
                        pausa = True
                    End If
                End If
                Return
            End If

            If _mpv IsNot Nothing Then
                If Not _mpv.InPausa Then
                    _mpv.Pausa()
                    pausa = True
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property ToccoAbilitato As Boolean
        Get
            Try
                If Me.IsDisposed OrElse Me.Disposing Then Return False
                If Not Me.IsHandleCreated Then Return False
                If Not Me.Visible Then Return False
                If _chiusuraInCorso Then Return False
                If _istanzaCorrente IsNot Me Then Return False
                If menuopzioniaperto Then Return False
                If _listaBrani IsNot Nothing Then Return False
                If _listaCapitoli IsNot Nothing Then Return False
                If _listaCommenti IsNot Nothing Then Return False
                If _menuSecondario IsNot Nothing Then Return False
                If domanda IsNot Nothing Then Return False
                If processoincorso IsNot Nothing Then Return False
                If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return False

                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                    Dim schermata As Control = Me
                    While schermata.Parent IsNot Nothing AndAlso Not (TypeOf schermata.Parent Is WGContenitore)
                        schermata = schermata.Parent
                    End While
                    If Not contenitore.GestisceControllo(schermata) Then Return False
                    If Not contenitore.PuoRicevereInput(schermata) Then Return False
                End If

                Return True
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoInPausa As Boolean
        Get
            Try
                If IsYoutubeConBrowser() Then
                    If _ytPlayer IsNot Nothing AndAlso Not _ytPlayer.IsDisposed Then Return _ytPlayer.InPausa
                    Return pausa
                End If
                If _mpv IsNot Nothing Then Return _mpv.InPausa
                Return pausa
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return pausa
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoLive As Boolean
        Get
            Try
                Return IsLiveSenzaSeek()
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoSeekDisponibile As Boolean
        Get
            Try
                If IsLiveSenzaSeek() Then Return False
                Return TempoDisponibileCorrente()
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoPosizioneSecondi As Double
        Get
            Try
                Return PosizioneCorrente().TotalSeconds
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoDurataSecondi As Double
        Get
            Try
                Return DurataCorrente().TotalSeconds
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoVolume As Integer
        Get
            Try
                Return _volumeCorrente
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoPassoScorrimento As Integer
        Get
            Try
                Return LeggiConfigInt("passoscorrimento", 30)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 30
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoPassoVolume As Integer
        Get
            Try
                Return LeggiConfigInt("passovolume", 5)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 5
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoPiuElementi As Boolean
        Get
            Try
                Return elementi IsNot Nothing AndAlso elementi.Count > 1
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property ToccoTitoloCorrente As String
        Get
            Try
                If currentIndex < 0 OrElse currentIndex >= elementi.Count Then Return ""
                Return NomeElementoPerElenco(elementi(currentIndex))
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return ""
            End Try
        End Get
    End Property

    Public Function ToccoDescriviSecondi(secondi As Integer) As String
        Try
            Return DescriviSecondi(secondi)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Public Sub ToccoPausaRiprendi()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim eraInPausa As Boolean = ToccoInPausa
            _ultimoSpazioInt = DateTime.MinValue
            OnSpacePressed()

            If eraInPausa Then
                LeggiSempreTTS("In riproduzione.")
            Else
                LeggiSempreTTS("In pausa.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoTracciaPrecedente()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If elementi Is Nothing OrElse elementi.Count <= 1 Then
                LeggiSempreTTS("C'è un solo elemento da riprodurre.")
                Return
            End If

            _tempoUltimoCambioElemento = DateTime.MinValue
            RiproduciElementoPrecedente()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoTracciaSuccessiva()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If elementi Is Nothing OrElse elementi.Count <= 1 Then
                LeggiSempreTTS("C'è un solo elemento da riprodurre.")
                Return
            End If

            _tempoUltimoCambioElemento = DateTime.MinValue
            RiproduciProssimoElemento(True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoScorri(secondi As Integer)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If secondi = 0 Then Return

            If IsLiveSenzaSeek() Then
                If IsStreamTvCorrente() Then
                    LeggiSempreTTS("Il canale è in diretta, non è possibile spostarsi.")
                Else
                    LeggiSempreTTS("La radio è in diretta, non è possibile spostarsi.")
                End If
                Return
            End If

            If Not TempoDisponibileCorrente() Then
                LeggiSempreTTS("Il tempo non è ancora disponibile.")
                Return
            End If

            Dim durata As Double = DurataCorrente().TotalSeconds
            Dim bersaglio As Double = PosizioneCorrente().TotalSeconds + secondi
            If bersaglio < 0 Then bersaglio = 0
            If durata > 2 AndAlso bersaglio > durata - 2 Then bersaglio = durata - 2

            tempoUltimoSeek = DateTime.Now

            If IsYoutubeConBrowser() Then
                If _ytPlayer IsNot Nothing Then _ytPlayer.ScorriAssoluto(bersaglio)
            ElseIf _mpv IsNot Nothing Then
                _mpv.ScorriAssoluto(bersaglio)
            End If

            LeggiSempreTTS(DescriviSecondi(CInt(Math.Floor(bersaglio))) & ".")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoCambiaVolume(delta As Integer)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If delta = 0 Then Return

            Dim volumePrecedente As Integer = _volumeCorrente
            ModificaESalvaVolume(delta, False)

            If _volumeCorrente = volumePrecedente Then
                If _volumeCorrente >= 100 Then
                    LeggiSempreTTS("Il volume è già al massimo.")
                    Return
                End If
                If _volumeCorrente <= 0 Then
                    LeggiSempreTTS("Il volume è già al minimo.")
                    Return
                End If
            End If

            LeggiSempreTTS(DescriviVolume(_volumeCorrente))
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoApriCoda()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            ApriElencoBrani(Nothing, Nothing)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoEsci()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            chiudi(Nothing, Nothing)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ToccoLeggiStato()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim parti As New List(Of String)

            If currentIndex >= 0 AndAlso currentIndex < elementi.Count Then
                Dim nome As String = NomeElementoPerElenco(elementi(currentIndex))
                If Not String.IsNullOrWhiteSpace(nome) Then parti.Add(nome)
            End If

            If IsLiveSenzaSeek() Then
                parti.Add("in diretta")
            ElseIf TempoDisponibileCorrente() Then
                parti.Add(DescriviDurata(PosizioneCorrente()) & " di " & DescriviDurata(DurataCorrente()))
            End If

            If ToccoInPausa Then
                parti.Add("in pausa")
            Else
                parti.Add("in riproduzione")
            End If

            parti.Add("volume al " & _volumeCorrente.ToString() & " per cento")

            LeggiSempreTTS(String.Join(", ", parti.ToArray()) & ".")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function IsNetworkAvailable() As Boolean
        Try
            Return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function
End Class