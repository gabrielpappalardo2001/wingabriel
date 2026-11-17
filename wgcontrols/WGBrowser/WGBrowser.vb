Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGBrowserManager
Imports wgcontrols

Public Class WGBrowser
    Inherits UserControl
    Implements IWGControlloConSchede
    Implements IWGAnnuncioIngresso
    Implements IWGControlloDownload
    Implements IWGChiudibile

    Public Sub RichiediChiusura() Implements IWGChiudibile.RichiediChiusura
        Chiudi()
    End Sub

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        AnnunciaRitornoAllaPagina(True)
    End Sub

    Private WithEvents tastiera As WGTastiera = WGTastiera.GetInstance()
    Private ReadOnly lettura As sintesivocale = sintesivocale.GetInstance()
    Private ReadOnly log As New Logger()

    Private ReadOnly _schede As New List(Of WGSchedaBrowser)()
    Private ReadOnly _inAttesaDownload As New List(Of WGSchedaBrowser)()
    Private ReadOnly _chiuse As New List(Of Tuple(Of String, String))()
    Private _attiva As WGSchedaBrowser = Nothing
    Private _registroDownload As WGRegistroDownload
    Private _gestore As WGGestoreBrowser
    Private _chiuso As Boolean = False
    Private _chiusuraConfermata As Boolean = False
    Private _istanteUltimoEsc As DateTime = DateTime.MinValue
    Private _istanteRitornoDaMenu As DateTime = DateTime.MinValue
    Private _prossimoTastoAllaPagina As Boolean = False
    Private _istantePassaggioRichiesto As DateTime = DateTime.MinValue
    Private _azioneMenuEseguita As Boolean = False
    Private WithEvents menuopzioni As WGListaView = Nothing
    Private ReadOnly _eventiOpzioni As New Dictionary(Of String, EventHandler)()
    Private _menuOpzioniAperto As Boolean = False
    Private _ultimaOpzione As String = ""
    Private _campoIndirizzo As WGCampoTesto
    Private _campoGuida As WGCampo
    Private ReadOnly codaTasti As New Queue(Of PressioneTastoBrowser)()
    Private _minutiDalSalvataggioAccessi As Integer = 0
    Private elaborazioneInCorso As Boolean = False
    Private _chiusuraInCorso As Boolean = False
    Private _istanteUltimoArresto As DateTime = DateTime.MinValue
    Private _moduloOspite As Form = Nothing
    Private WithEvents timerSospensione As New System.Windows.Forms.Timer() With {.Interval = 60000}

    ' Lettore: il browser ridotto a una sola pagina locale, per i messaggi della posta scritti con la grafica.
    Private _modoLettore As Boolean = False
    Private _fileLettore As String = ""
    Private _titoloLettore As String = ""
    Private _opzioniLettore As New List(Of Tuple(Of String, Action))()
    Private _chiusuraLettore As Action = Nothing

    Private Shared _istanzaAperta As WGBrowser = Nothing
    Private Shared _serviziRegistrati As Integer = 0

    Private Const MinutiPrimaDellaSospensione As Integer = 30
    Private Const MassimoSchedeChiuse As Integer = 25

    Public Event chiusura As EventHandler

    Private Shared Sub RegistraServiziGlobali()
        Try
            If Interlocked.Exchange(_serviziRegistrati, 1) = 1 Then Return
            AddHandler Application.ApplicationExit, AddressOf ChiusuraApplicazione
            WGImpostazioniBrowser.RegistraPreparazioneBackup("accessi ai siti del browser", Sub() WGAccessiSiti.Salva(8000))
            ' password e accessi si riscrivono in un formato leggibile su ogni computer collegato a Filen
            AddHandler WGSincronizzazioneCloud.SincronizzazioneTerminata, Sub(esito As WGSincronizzazioneCloud.EsitoSincronizzazione) RendiPortabiliInSottofondo()
            RendiPortabiliInSottofondo()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.RegistraServiziGlobali: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RendiPortabiliInSottofondo()
        Try
            Task.Run(Sub()
                         Try
                             If Not WGProtezioneDati.PortabilePossibile Then Return
                             WGArchivioBrowser.Istanza.RendiPortabiliIDati()
                             WGAccessiSiti.ForzaRiscrittura()
                         Catch exLavoro As Exception
                             Dim registroLavoro As New Logger()
                             registroLavoro.ScriviLog("WGBrowser.RendiPortabiliInSottofondo lavoro: " & exLavoro.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.RendiPortabiliInSottofondo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiusuraApplicazione(sender As Object, e As EventArgs)
        Try
            RemoveHandler Application.ApplicationExit, AddressOf ChiusuraApplicazione
            If IstanzaAperta IsNot Nothing Then WGAccessiSiti.Salva(3000)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.ChiusuraApplicazione: " & ex.Message)
        End Try
    End Sub

    Friend Shared ReadOnly Property CartellaBase As String
        Get
            Try
                Return Path.GetDirectoryName(GetType(WGBrowser).Assembly.Location)
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGBrowser.CartellaBase: " & ex.Message)
                Return Application.StartupPath
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property ArchitetturaArm As Boolean
        Get
            Try
                Return RuntimeInformation.ProcessArchitecture = Architecture.Arm64
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGBrowser.ArchitetturaArm: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Friend Shared ReadOnly Property CartellaArchitettura As String
        Get
            Dim nome As String = If(ArchitetturaArm, "arm64", If(Environment.Is64BitProcess, "x64", "x86"))
            Return Path.Combine(CartellaBase, nome)
        End Get
    End Property

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.Selectable, True)
            Me.DoubleBuffered = True
            Me.BackColor = WGTema.Sfondo
            Me.TabStop = True
            Me.AccessibleRole = AccessibleRole.Client
            Me.AccessibleName = " "
            _registroDownload = New WGRegistroDownload(Me)
            _gestore = New WGGestoreBrowser(AddressOf ApriDaGestore, _registroDownload)
            AddHandler WGImpostazioniBrowser.LinguaPagineCambiata, AddressOf LinguaPagineCambiata
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            AvviaTouch()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.New: " & ex.ToString())
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
            log.ScriviLog("WGBrowser.OnCambiatoTema: " & ex.ToString())
        End Try
    End Sub

    Public Shared ReadOnly Property IstanzaAperta As WGBrowser
        Get
            Try
                Dim istanza As WGBrowser = _istanzaAperta
                If istanza Is Nothing OrElse istanza._chiuso OrElse istanza._chiusuraInCorso OrElse istanza.IsDisposed Then Return Nothing
                Return istanza
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGBrowser.IstanzaAperta: " & ex.Message)
                Return Nothing
            End Try
        End Get
    End Property

    Public Shared Sub ApriBrowser(url As String)
        Try
            Dim istanza As WGBrowser = IstanzaAperta
            If istanza IsNot Nothing Then
                istanza.RiportaInPrimoPiano(url)
                Return
            End If
            Dim nuovo As New WGBrowser()
            nuovo.Avvia(url)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.ApriBrowser: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Vero se questo browser è il lettore di una pagina sola (per esempio un messaggio di posta).</summary>
    Friend ReadOnly Property ModoLettore As Boolean
        Get
            Return _modoLettore
        End Get
    End Property

    ''' <summary>Vero se l'indirizzo è la pagina del lettore, anche con un'ancora interna.</summary>
    Friend Function EIndirizzoDelLettore(uri As String) As Boolean
        Try
            Dim u As String = If(uri, "")
            If u = "" OrElse u.StartsWith("about:", StringComparison.OrdinalIgnoreCase) Then Return True
            Dim fine As Integer = u.IndexOfAny(New Char() {"#"c, "?"c})
            If fine >= 0 Then u = u.Substring(0, fine)
            Return u.StartsWith("file:", StringComparison.OrdinalIgnoreCase) AndAlso _fileLettore <> "" AndAlso
                   u.EndsWith("/" & Path.GetFileName(_fileLettore), StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.EIndirizzoDelLettore: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Il lettore dei contenuti HTML (i messaggi della posta scritti con la grafica): lo stesso browser, con
    ''' una pagina sola e senza schede, indirizzi, preferiti e impostazioni; in cima al menu delle opzioni le
    ''' azioni di chi lo apre. Niente viene caricato dalla rete e i collegamenti si aprono con il browser scelto.
    ''' Nothing se il motore del browser non c'è.
    ''' </summary>
    Public Shared Function ApriLettoreHtml(richiesta As WGRichiestaLettoreHtml) As IWGChiudibile
        Try
            If richiesta Is Nothing OrElse String.IsNullOrWhiteSpace(richiesta.Html) Then Return Nothing
            If WGMotoreBrowser.VersioneRuntime() = "" Then Return Nothing
            Dim lettore As New WGBrowser()
            If lettore.AvviaLettore(richiesta) Then Return lettore
            If Not lettore.IsDisposed Then lettore.Dispose()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.ApriLettoreHtml: " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Private Function AvviaLettore(richiesta As WGRichiestaLettoreHtml) As Boolean
        Try
            _modoLettore = True
            _titoloLettore = If(richiesta.Titolo, "").Trim()
            If richiesta.Opzioni IsNot Nothing Then _opzioniLettore = New List(Of Tuple(Of String, Action))(richiesta.Opzioni)
            If Not InizializzaMotore() Then Return False
            Dim cartella As String = Path.Combine(Path.GetTempPath(), "WinGabriel", "lettore")
            Directory.CreateDirectory(cartella)
            PulisciFileLettore(cartella)
            _fileLettore = Path.Combine(cartella, Guid.NewGuid().ToString("N") & ".html")
            File.WriteAllText(_fileLettore, richiesta.Html, New System.Text.UTF8Encoding(True))
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return False
            Dim forzaHandle As IntPtr = Me.Handle
            contenitore.MostraControllo(Me)
            ' Da qui in poi la chiusura avvisa chi ha aperto il lettore.
            _chiusuraLettore = richiesta.Chiusura
            lettura.LeggiTesto(If(_titoloLettore <> "", _titoloLettore & ". ", "") & "Caricamento del messaggio")
            If NuovaScheda(New Uri(_fileLettore).AbsoluteUri, False, Nothing, False) Is Nothing Then
                CompletaChiusura()
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AvviaLettore: " & ex.ToString())
            Return False
        End Try
    End Function

    ' I file dei lettori rimasti da una chiusura brusca del programma si tolgono all'apertura successiva.
    Private Shared Sub PulisciFileLettore(cartella As String)
        Try
            For Each vecchio As String In Directory.GetFiles(cartella, "*.html")
                Try
                    If (DateTime.Now - File.GetLastWriteTime(vecchio)).TotalHours > 12 Then File.Delete(vecchio)
                Catch exFile As Exception
                    Dim registroFile As New Logger()
                    registroFile.ScriviLog("WGBrowser.PulisciFileLettore file: " & exFile.Message)
                End Try
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.PulisciFileLettore: " & ex.Message)
        End Try
    End Sub

    Private Shared Function GestoreEsterno() As WGGestoreBrowser
        Dim istanza As WGBrowser = IstanzaAperta
        Dim controlloDownload As IWGControlloDownload = istanza
        Return New WGGestoreBrowser(Sub(indirizzo As String, inNuovaScheda As Boolean) ApriBrowser(indirizzo), controlloDownload)
    End Function

    Public Shared Sub MostraMenuBrowser()
        Try
            GestoreEsterno().MostraMenuApertura()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.MostraMenuBrowser: " & ex.ToString())
        End Try
    End Sub

    Public Shared Sub MostraConfigurazione()
        Try
            GestoreEsterno().MostraConfigurazione()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.MostraConfigurazione: " & ex.ToString())
        End Try
    End Sub

    Public Sub RiportaInPrimoPiano(url As String)
        Try
            If _chiuso OrElse Me.IsDisposed Then
                ApriBrowser(url)
                Return
            End If
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf RiportaInPrimoPiano), url)
                Return
            End If
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return
            ' Il browser può essere in un'applicazione rimasta dietro: ci si va (senza annuncio, parla qui sotto).
            If Not contenitore.VaiAllApplicazioneDi(Me, False) Then contenitore.MostraControllo(Me)
            If Not String.IsNullOrWhiteSpace(url) Then
                NuovaScheda(url, False, Nothing)
            ElseIf _schede.Count = 0 Then
                NuovaScheda(WGImpostazioniBrowser.PaginaIniziale, False, Nothing)
            Else
                If _attiva Is Nothing Then AttivaScheda(_schede(0), "")
                Dim numero As Integer = _schede.Count
                _tickUltimoRitorno = Environment.TickCount
                lettura.LeggiTesto("Browser, " & numero & If(numero = 1, " scheda aperta. ", " schede aperte. ") & Titolo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.RiportaInPrimoPiano: " & ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property Titolo As String
        Get
            Return If(_attiva IsNot Nothing, _attiva.Titolo, "")
        End Get
    End Property

    Public ReadOnly Property Indirizzo As String
        Get
            Return If(_attiva IsNot Nothing, _attiva.Indirizzo, "")
        End Get
    End Property

    Public ReadOnly Property NumeroSchede As Integer
        Get
            Return _schede.Count
        End Get
    End Property

    Friend ReadOnly Property RegistroDownload As WGRegistroDownload
        Get
            Return _registroDownload
        End Get
    End Property

    Public ReadOnly Property Gestore As WGGestoreBrowser
        Get
            Return _gestore
        End Get
    End Property

    Public Shared Function SembraIndirizzo(testo As String) As Boolean
        Try
            Dim t As String = If(testo, "").Trim()
            If t.Length = 0 OrElse t.Contains(" ") Then Return False
            Dim minuscolo As String = t.ToLowerInvariant()
            If minuscolo.StartsWith("http://") OrElse minuscolo.StartsWith("https://") OrElse minuscolo.StartsWith("file:") OrElse minuscolo.StartsWith("about:") OrElse minuscolo.StartsWith("data:") Then Return True
            If minuscolo.StartsWith("localhost") Then Return True
            If t.Length > 2 AndAlso t(1) = ":"c Then Return True
            Return t.Contains(".") AndAlso Not t.EndsWith(".") AndAlso Not t.StartsWith(".")
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.SembraIndirizzo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function NormalizzaIndirizzo(testo As String) As String
        Try
            Dim t As String = If(testo, "").Trim()
            If t.Length = 0 Then Return WGImpostazioniBrowser.PaginaIniziale
            Dim minuscolo As String = t.ToLowerInvariant()
            If minuscolo.StartsWith("http://") OrElse minuscolo.StartsWith("https://") OrElse minuscolo.StartsWith("file:") OrElse minuscolo.StartsWith("about:") OrElse minuscolo.StartsWith("data:") OrElse minuscolo.StartsWith("view-source:") Then Return t
            If t.Length > 2 AndAlso t(1) = ":"c AndAlso File.Exists(t) Then Return New Uri(t).AbsoluteUri
            If SembraIndirizzo(t) Then Return "https://" & t
            Return WGImpostazioniBrowser.IndirizzoRicerca(t)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowser.NormalizzaIndirizzo: " & ex.Message)
            Return WGImpostazioniBrowser.PaginaIniziale
        End Try
    End Function

    Private Function MotoreDisponibile(url As String) As Boolean
        Try
            If WGMotoreBrowser.VersioneRuntime() <> "" Then Return True
            log.ScriviLog("WGBrowser.MotoreDisponibile: motore Microsoft Edge WebView2 non trovato")
            WGMotoreBrowser.ProponiInstallazione(Sub()
                                                     Try
                                                         ApriBrowser(url)
                                                     Catch exRiprova As Exception
                                                         log.ScriviLog("WGBrowser.MotoreDisponibile riprova: " & exRiprova.Message)
                                                     End Try
                                                 End Sub)
            Return False
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MotoreDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function InizializzaMotore() As Boolean
        Try
            WGMotoreBrowser.RegistraThreadInterfaccia()
            RegistraServiziGlobali()
            WGMotoreBrowser.AccelerazioneGraficaAttiva = WGImpostazioniBrowser.AccelerazioneGrafica
            Dim avvioMotore As Task(Of Microsoft.Web.WebView2.Core.CoreWebView2Environment) = WGMotoreBrowser.AssicuraAmbienteAsync()
            Dim preparazioneBlocco As Task = Task.Run(Sub()
                                                          Try
                                                              Dim domini As Integer = WGBloccoPubblicita.NumeroDomini
                                                          Catch exBlocco As Exception
                                                              Dim registroBlocco As New Logger()
                                                              registroBlocco.ScriviLog("WGBrowser.InizializzaMotore blocco pubblicità: " & exBlocco.Message)
                                                          End Try
                                                      End Sub)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrowser.InizializzaMotore: " & ex.ToString())
            MostraErrore("Impossibile inizializzare il browser: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiornaPreferenzeSchede()
        Try
            For Each scheda As WGSchedaBrowser In _schede.ToList()
                scheda.AggiornaPreferenze()
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiornaPreferenzeSchede: " & ex.Message)
        End Try
    End Sub

    Friend ReadOnly Property FinestraOspite As IntPtr
        Get
            Try
                If _chiuso OrElse Me.IsDisposed Then Return IntPtr.Zero
                Return Me.Handle
            Catch ex As Exception
                log.ScriviLog("WGBrowser.FinestraOspite: " & ex.Message)
                Return IntPtr.Zero
            End Try
        End Get
    End Property

    Friend Sub RiprendiFocus()
        Try
            If _chiuso OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New Action(Sub()
                                          Try
                                              If _chiuso OrElse Me.IsDisposed OrElse Not Me.Visible Then Return
                                              If _menuOpzioniAperto AndAlso menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed Then
                                                  menuopzioni.Focus()
                                              Else
                                                  Me.Focus()
                                              End If
                                          Catch exFocus As Exception
                                              log.ScriviLog("WGBrowser.RiprendiFocus focus: " & exFocus.Message)
                                          End Try
                                      End Sub))
        Catch ex As Exception
            log.ScriviLog("WGBrowser.RiprendiFocus: " & ex.Message)
        End Try
    End Sub

    Friend Sub AttivaFinestraPrincipale()
        Try
            If _chiuso OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New Action(Sub()
                                          Try
                                              If _chiuso OrElse Me.IsDisposed Then Return
                                              Dim modulo As Form = Me.FindForm()
                                              If modulo IsNot Nothing AndAlso Not modulo.IsDisposed AndAlso modulo.Visible Then modulo.Activate()
                                          Catch exAttiva As Exception
                                              log.ScriviLog("WGBrowser.AttivaFinestraPrincipale attivazione: " & exAttiva.Message)
                                          End Try
                                          RiprendiFocus()
                                      End Sub))
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AttivaFinestraPrincipale: " & ex.Message)
        End Try
    End Sub

    Friend Function CoreNormale() As Microsoft.Web.WebView2.Core.CoreWebView2
        Try
            For Each scheda As WGSchedaBrowser In _schede.Concat(_inAttesaDownload).ToList()
                If scheda Is Nothing OrElse scheda.Privata OrElse scheda.Chiusa Then Continue For
                Dim core As Microsoft.Web.WebView2.Core.CoreWebView2 = scheda.Core
                If core IsNot Nothing Then Return core
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrowser.CoreNormale: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Function EseguiSuInterfacciaAsync(Of T)(funzione As Func(Of Task(Of T))) As Task(Of T)
        Try
            If funzione Is Nothing OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return Task.FromResult(Of T)(Nothing)
            If Not Me.InvokeRequired Then Return funzione()
            Dim attesa As New TaskCompletionSource(Of T)(TaskCreationOptions.RunContinuationsAsynchronously)
            Me.BeginInvoke(New Action(Async Sub()
                                          Try
                                              Dim risultato As T = Await funzione()
                                              attesa.TrySetResult(risultato)
                                          Catch exFunzione As Exception
                                              log.ScriviLog("WGBrowser.EseguiSuInterfacciaAsync funzione: " & exFunzione.Message)
                                              attesa.TrySetResult(Nothing)
                                          End Try
                                      End Sub))
            Return attesa.Task
        Catch ex As Exception
            log.ScriviLog("WGBrowser.EseguiSuInterfacciaAsync: " & ex.Message)
            Return Task.FromResult(Of T)(Nothing)
        End Try
    End Function

    Friend Sub AggiornaScriptIniziali()
        SuUI(Sub()
                 Try
                     For Each scheda As WGSchedaBrowser In _schede.ToList()
                         scheda.AggiornaScriptIniziale()
                     Next
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.AggiornaScriptIniziali: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub NotificaArrestoMotore()
        SuUI(Sub()
                 Try
                     If _chiuso OrElse _chiusuraInCorso Then Return
                     If (DateTime.Now - _istanteUltimoArresto).TotalSeconds < 10 Then Return
                     _istanteUltimoArresto = DateTime.Now
                     WGMotoreBrowser.AmbienteTerminato(Nothing)
                     Annuncia("Il motore del browser si è interrotto: le schede vengono riaperte")
                     For Each scheda As WGSchedaBrowser In _schede.ToList()
                         scheda.RiavviaDopoArresto()
                     Next
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.NotificaArrestoMotore: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Sub AggiornaFinestreOspitate()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim finestra As IntPtr = Me.Handle
            For Each scheda As WGSchedaBrowser In _schede.Concat(_inAttesaDownload).ToList()
                If scheda Is Nothing OrElse scheda.Pagina Is Nothing Then Continue For
                scheda.Pagina.AggiornaFinestraPadre(finestra)
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiornaFinestreOspitate: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
            AggiornaFinestreOspitate()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.OnHandleCreated: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnLocationChanged(e As EventArgs)
        Try
            MyBase.OnLocationChanged(e)
            AggiornaFinestreOspitate()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.OnLocationChanged: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnParentChanged(e As EventArgs)
        Try
            MyBase.OnParentChanged(e)
            Dim precedente As Form = _moduloOspite
            Dim nuovo As Form = Me.FindForm()
            If precedente IsNot nuovo Then
                If precedente IsNot Nothing Then RemoveHandler precedente.Move, AddressOf ModuloOspiteSpostato
                _moduloOspite = nuovo
                If nuovo IsNot Nothing Then AddHandler nuovo.Move, AddressOf ModuloOspiteSpostato
            End If
            AggiornaFinestreOspitate()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.OnParentChanged: " & ex.Message)
        End Try
    End Sub

    Private Sub ModuloOspiteSpostato(sender As Object, e As EventArgs)
        Try
            If _chiuso Then Return
            AggiornaFinestreOspitate()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ModuloOspiteSpostato: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia(url As String)
        Try
            Dim giaAperta As WGBrowser = IstanzaAperta
            If giaAperta IsNot Nothing AndAlso giaAperta IsNot Me Then
                giaAperta.RiportaInPrimoPiano(url)
                Me.Dispose()
                Return
            End If
            If Not MotoreDisponibile(url) OrElse Not InizializzaMotore() Then
                Me.Dispose()
                Return
            End If
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then contenitore = WGContenitore.IstanzaOCrea()
            If contenitore Is Nothing Then
                MostraErrore("Impossibile aprire il browser: il contenitore di WinGabriel non è disponibile.")
                Return
            End If
            Dim forzaHandle As IntPtr = Me.Handle
            _istanzaAperta = Me
            contenitore.MostraControllo(Me)
            timerSospensione.Start()
            Dim avvisoAccessi As String = WGAccessiSiti.AvvisoInSospeso()
            If avvisoAccessi <> "" Then avvisoAccessi = ". " & avvisoAccessi
            Dim salvate As List(Of String) = If(WGImpostazioniBrowser.RiapriSchede AndAlso String.IsNullOrWhiteSpace(url), WGImpostazioniBrowser.SchedeSalvate, New List(Of String)())
            If salvate.Count > 0 Then
                lettura.LeggiTesto("Browser. Riapertura di " & salvate.Count & If(salvate.Count = 1, " scheda", " schede") & avvisoAccessi)
                For i As Integer = 0 To salvate.Count - 1
                    NuovaScheda(salvate(i), False, Nothing, False)
                Next
                If _schede.Count > 0 Then AttivaScheda(_schede(0), "")
            Else
                lettura.LeggiTesto("Browser. Caricamento della pagina" & avvisoAccessi)
                NuovaScheda(If(String.IsNullOrWhiteSpace(url), WGImpostazioniBrowser.PaginaIniziale, url), False, Nothing, False)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Avvia: " & ex.ToString())
            MostraErrore("Errore avvio browser: " & ex.Message)
        End Try
    End Sub

    Public Sub Naviga(url As String)
        Try
            If _attiva Is Nothing Then
                NuovaScheda(url, False, Nothing)
                Return
            End If
            _attiva.Naviga(url)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Naviga: " & ex.Message)
        End Try
    End Sub

    Public Sub ApriIndirizzo(url As String, inNuovaScheda As Boolean)
        Try
            If inNuovaScheda OrElse _attiva Is Nothing Then
                NuovaScheda(url, False, Nothing)
            Else
                lettura.LeggiTesto("Caricamento della pagina")
                _attiva.Naviga(url)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ApriIndirizzo: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriDaGestore(url As String, inNuovaScheda As Boolean)
        SuUI(Sub() ApriIndirizzo(url, inNuovaScheda))
    End Sub

    Private Sub LinguaPagineCambiata(sender As Object, e As EventArgs)
        SuUI(Sub()
                 Try
                     If _chiuso Then Return
                     AggiornaPreferenzeSchede()
                     For Each scheda As WGSchedaBrowser In _schede.ToList()
                         scheda.RiapplicaIdentita()
                     Next
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.LinguaPagineCambiata: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Sub MostraErrore(testoErrore As String)
        Try
            Dim fm As New WGMessaggi()
            fm.Messaggio(testoErrore)
            fm.Show()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraErrore: " & ex.Message)
        End Try
    End Sub

    Friend Sub SuUI(azione As Action)
        Try
            If _chiuso OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("WGBrowser.SuUI chiuso: " & ex.Message)
        Catch ex As InvalidOperationException
            log.ScriviLog("WGBrowser.SuUI non valido: " & ex.Message)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.SuUI: " & ex.Message)
        End Try
    End Sub

    Friend Function Posticipa(azione As Action) As Boolean
        Try
            If azione Is Nothing OrElse _chiuso OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False
            Me.BeginInvoke(azione)
            Return True
        Catch ex As ObjectDisposedException
            log.ScriviLog("WGBrowser.Posticipa chiuso: " & ex.Message)
            Return False
        Catch ex As InvalidOperationException
            log.ScriviLog("WGBrowser.Posticipa non valido: " & ex.Message)
            Return False
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Posticipa: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Notifica del browser (download, notifiche dei siti): va nel centro notifiche di WinGabriel e viene letta,
    ''' con il nome dell'applicazione se il browser è in sottofondo.
    ''' </summary>
    Friend Sub Notifica(testo As String, Optional azione As Action = Nothing)
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse _chiuso Then Return
            WGNotifiche.WGCentroNotifiche.Pubblica("", testo, azione, Me, annuncia:=True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Notifica: " & ex.Message)
            Annuncia(testo)
        End Try
    End Sub

    Friend Function InSottofondo() As Boolean
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        Return contenitore IsNot Nothing AndAlso Not contenitore.EApplicazioneCorrente(Me)
    End Function

    ' Azioni delle notifiche: riportano al browser e poi aprono i download o la scheda che ha notificato.
    Friend Sub MostraDownloadDaNotifica()
        Try
            If _chiuso Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.VaiAllApplicazioneDi(Me)
            _gestore.MostraDownload()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraDownloadDaNotifica: " & ex.Message)
        End Try
    End Sub

    Friend Sub MostraSchedaDaNotifica(scheda As WGSchedaBrowser)
        Try
            If _chiuso Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.VaiAllApplicazioneDi(Me)
            If scheda IsNot Nothing AndAlso _schede.Contains(scheda) Then AttivaScheda(scheda, "")
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraSchedaDaNotifica: " & ex.Message)
        End Try
    End Sub

    Friend Sub Annuncia(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse _chiuso Then Return
            lettura.LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Annuncia: " & ex.Message)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If _chiuso OrElse _chiusuraInCorso OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then Return False
            Return c.PuoRicevereInput(Me)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.PuoRicevere: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function NumeroScheda(scheda As WGSchedaBrowser) As Integer
        Return _schede.IndexOf(scheda) + 1
    End Function

    Private Sub ChiudiSessionePrivata()
        Try
            WGSchedaBrowser.DimenticaSessionePrivata()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiudiSessionePrivata: " & ex.Message)
        End Try
    End Sub

    Private Function SchedePrivateAperte() As Boolean
        Try
            Return _schede.Any(Function(s) s.Privata) OrElse _inAttesaDownload.Any(Function(s) s.Privata)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.SchedePrivateAperte: " & ex.Message)
            Return True
        End Try
    End Function

    Friend Function NuovaScheda(url As String, sfondo As Boolean, apritore As WGSchedaBrowser, Optional attiva As Boolean = True, Optional privata As Boolean = False) As WGSchedaBrowser
        Try
            If _chiuso OrElse _chiusuraInCorso Then Return Nothing
            AggiornaPreferenzeSchede()
            Dim scheda As New WGSchedaBrowser(Me, url, privata OrElse (apritore IsNot Nothing AndAlso apritore.Privata))
            scheda.Opener = apritore
            InserisciScheda(scheda, apritore)
            scheda.Avvia()
            If sfondo Then
                scheda.SegnaAperturaInSfondo()
                If apritore IsNot Nothing Then Annuncia("Aperta in secondo piano la scheda " & NumeroScheda(scheda) & " di " & _schede.Count)
            ElseIf attiva Then
                AttivaScheda(scheda, If(_schede.Count > 1, "Nuova scheda", ""))
            End If
            If _attiva Is Nothing Then AttivaScheda(scheda, "")
            Return scheda
        Catch ex As Exception
            log.ScriviLog("WGBrowser.NuovaScheda: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub InserisciScheda(scheda As WGSchedaBrowser, apritore As WGSchedaBrowser)
        Try
            Dim posizione As Integer = If(apritore IsNot Nothing, _schede.IndexOf(apritore), -1)
            If posizione >= 0 Then
                Dim dopo As Integer = posizione + 1
                While dopo < _schede.Count AndAlso _schede(dopo).Opener Is apritore
                    dopo += 1
                End While
                _schede.Insert(dopo, scheda)
            Else
                _schede.Add(scheda)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.InserisciScheda: " & ex.Message)
            If Not _schede.Contains(scheda) Then _schede.Add(scheda)
        End Try
    End Sub

    Friend Sub AggiungiSchedaDaThread(scheda As WGSchedaBrowser, attiva As Boolean)
        SuUI(Sub()
                 Try
                     If _chiuso Then
                         scheda.Chiudi()
                         Return
                     End If
                     InserisciScheda(scheda, scheda.Opener)
                     If attiva Then
                         AttivaScheda(scheda, "Nuova finestra aperta dalla pagina")
                     Else
                         scheda.SegnaAperturaInSfondo()
                         Annuncia("La pagina ha aperto una scheda in secondo piano")
                     End If
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.AggiungiSchedaDaThread: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Sub AttivaScheda(scheda As WGSchedaBrowser, prefisso As String)
        Try
            If scheda Is Nothing OrElse Not _schede.Contains(scheda) Then Return
            If _attiva Is scheda Then
                If prefisso <> "" Then scheda.AnnunciaAttivazione(prefisso)
                Return
            End If
            Dim vecchia As WGSchedaBrowser = _attiva
            If vecchia IsNot Nothing Then vecchia.Attiva = False
            _attiva = scheda
            scheda.Attiva = True
            Dim testoPrefisso As String = prefisso
            If _schede.Count > 1 Then testoPrefisso = If(prefisso <> "", prefisso & ", ", "") & "scheda " & NumeroScheda(scheda) & " di " & _schede.Count
            If testoPrefisso <> "" OrElse vecchia IsNot Nothing Then scheda.AnnunciaAttivazione(testoPrefisso)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AttivaScheda: " & ex.Message)
        End Try
    End Sub

    Public Function CambiaScheda(avanti As Boolean) As Boolean Implements IWGControlloConSchede.CambiaScheda
        Try
            If _chiuso OrElse _schede.Count <= 1 OrElse _attiva Is Nothing Then Return False
            Dim indice As Integer = _schede.IndexOf(_attiva)
            Dim nuovo As Integer = If(avanti, (indice + 1) Mod _schede.Count, (indice - 1 + _schede.Count) Mod _schede.Count)
            AttivaScheda(_schede(nuovo), "")
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBrowser.CambiaScheda: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub VaiAllaScheda(numero As Integer)
        Try
            If numero < 1 OrElse numero > _schede.Count Then
                lettura.LeggiTesto("Non esiste la scheda " & numero & ". Schede aperte: " & _schede.Count)
                Return
            End If
            AttivaScheda(_schede(numero - 1), "")
        Catch ex As Exception
            log.ScriviLog("WGBrowser.VaiAllaScheda: " & ex.Message)
        End Try
    End Sub

    Friend Sub ChiudiScheda(scheda As WGSchedaBrowser, silenzioso As Boolean)
        Try
            If scheda Is Nothing Then Return
            Dim indice As Integer = _schede.IndexOf(scheda)
            If indice < 0 Then Return
            Dim indirizzoScheda As String = scheda.Indirizzo
            If Not scheda.Privata AndAlso WGArchivioBrowser.IndirizzoRegistrabile(indirizzoScheda) Then
                _chiuse.Add(Tuple.Create(scheda.Titolo, indirizzoScheda))
                While _chiuse.Count > MassimoSchedeChiuse
                    _chiuse.RemoveAt(0)
                End While
            End If
            _schede.RemoveAt(indice)
            If _registroDownload.DownloadDellaScheda(scheda) > 0 Then
                scheda.Attiva = False
                _inAttesaDownload.Add(scheda)
            Else
                scheda.Chiudi()
            End If
            For Each altra As WGSchedaBrowser In _schede
                If altra.Opener Is scheda Then altra.Opener = scheda.Opener
            Next
            If scheda.Privata AndAlso Not SchedePrivateAperte() Then
                ChiudiSessionePrivata()
                If _schede.Count > 0 Then Annuncia("Navigazione privata chiusa: cookie e dati della sessione privata cancellati")
            End If
            If _schede.Count = 0 Then
                Chiudi()
                Return
            End If
            If _attiva Is scheda Then
                _attiva = Nothing
                Dim prossima As WGSchedaBrowser = If(scheda.Opener IsNot Nothing AndAlso _schede.Contains(scheda.Opener), scheda.Opener, _schede(Math.Min(indice, _schede.Count - 1)))
                AttivaScheda(prossima, If(silenzioso, "Torno alla", "Scheda chiusa, ora"))
            ElseIf Not silenzioso Then
                Annuncia("Scheda chiusa")
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiudiScheda: " & ex.ToString())
        End Try
    End Sub

    Friend Sub SchedaChiusaDallaPagina(scheda As WGSchedaBrowser)
        Try
            If Not _schede.Contains(scheda) Then Return
            If _schede.Count = 1 Then
                Annuncia("La pagina ha chiuso la finestra")
                Chiudi()
                Return
            End If
            ChiudiScheda(scheda, True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.SchedaChiusaDallaPagina: " & ex.Message)
        End Try
    End Sub

    Friend Sub VerificaSchedeInAttesa()
        SuUI(Sub()
                 Try
                     For Each scheda As WGSchedaBrowser In _inAttesaDownload.ToList()
                         If _registroDownload.DownloadDellaScheda(scheda) = 0 Then
                             _inAttesaDownload.Remove(scheda)
                             scheda.Chiudi()
                             If scheda.Privata AndAlso Not SchedePrivateAperte() Then ChiudiSessionePrivata()
                         End If
                     Next
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.VerificaSchedeInAttesa: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Sub RiapriSchedaChiusa()
        Try
            If _chiuse.Count = 0 Then
                lettura.LeggiTesto("Nessuna scheda chiusa da riaprire")
                Return
            End If
            Dim ultima As Tuple(Of String, String) = _chiuse(_chiuse.Count - 1)
            _chiuse.RemoveAt(_chiuse.Count - 1)
            NuovaScheda(ultima.Item2, False, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.RiapriSchedaChiusa: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Da chiamare quando un menu aperto dal browser si chiude, per non passare alla pagina il tasto che lo ha chiuso.</summary>
    Friend Sub SegnaRitornoDaMenu()
        _istanteRitornoDaMenu = DateTime.Now
    End Sub

    ' La lista del menu delle opzioni e i menu di WinGabriel hanno un loro aggancio della tastiera e lasciano
    ' proseguire Esc e Invio dopo averli usati: senza questo filtro lo stesso Esc che chiude il menu arrivava
    ' alla pagina e, con una sola scheda aperta, chiudeva il browser; Invio attivava anche l'elemento della pagina.
    Private Function TastoDiChiusuraMenu(tasto As Keys) As Boolean
        Try
            If (DateTime.Now - _istanteRitornoDaMenu).TotalMilliseconds > 400 Then Return False
            Select Case tasto And Keys.KeyCode
                Case Keys.Escape, Keys.Enter, Keys.Back, Keys.Left, Keys.Right, Keys.Space
                    _istanteRitornoDaMenu = DateTime.MinValue
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGBrowser.TastoDiChiusuraMenu: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub RichiestaChiusuraDaEsc()
        Try
            If _schede.Count <= 1 OrElse (DateTime.Now - _istanteUltimoEsc).TotalSeconds < 2.5 Then
                Chiudi()
                Return
            End If
            _istanteUltimoEsc = DateTime.Now
            lettura.LeggiTesto("Ci sono " & _schede.Count & " schede aperte. Esc di nuovo per chiudere il browser, Control W per chiudere solo questa scheda")
        Catch ex As Exception
            log.ScriviLog("WGBrowser.RichiestaChiusuraDaEsc: " & ex.Message)
        End Try
    End Sub

    Friend Sub ApriPdfNelLettore(percorso As String)
        SuUI(Sub()
                 Try
                     If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
                     lettura.LeggiTesto("Apertura del PDF " & Path.GetFileNameWithoutExtension(percorso) & " nel lettore di WinGabriel")
                     Dim campo As New WGCampo()
                     AddHandler campo.chiusura, Sub(s As Object, e As EventArgs)
                                                    Try
                                                        campo.chiudi()
                                                    Catch exChiudi As Exception
                                                        log.ScriviLog("WGBrowser.ApriPdfNelLettore chiusura: " & exChiudi.Message)
                                                    End Try
                                                End Sub
                     campo.ApriPdf(percorso, Path.GetFileNameWithoutExtension(percorso), True)
                     campo.avvia()
                 Catch ex As Exception
                     log.ScriviLog("WGBrowser.ApriPdfNelLettore: " & ex.ToString())
                     MostraErrore("Impossibile aprire il PDF: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Public Function DownloadInCorso() As List(Of DownloadAttivoBrowser) Implements IWGControlloDownload.DownloadInCorso
        Return _registroDownload.DownloadInCorso()
    End Function

    Public Sub PausaDownload(id As Integer) Implements IWGControlloDownload.PausaDownload
        _registroDownload.PausaDownload(id)
    End Sub

    Public Sub RiprendiDownload(id As Integer) Implements IWGControlloDownload.RiprendiDownload
        _registroDownload.RiprendiDownload(id)
    End Sub

    Public Sub AnnullaDownload(id As Integer) Implements IWGControlloDownload.AnnullaDownload
        _registroDownload.AnnullaDownload(id)
    End Sub

    Private Sub timerSospensione_Tick(sender As Object, e As EventArgs) Handles timerSospensione.Tick
        Try
            If _chiuso Then Return
            _minutiDalSalvataggioAccessi += 1
            If _minutiDalSalvataggioAccessi >= 10 Then
                _minutiDalSalvataggioAccessi = 0
                Dim salvataggio As Task(Of Boolean) = WGAccessiSiti.SalvaAsync(Me)
            End If
            Dim attiveNonSospese As Integer = _schede.Where(Function(s) Not s.Sospesa).Count()
            If attiveNonSospese <= 3 Then Return
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            For Each scheda As WGSchedaBrowser In _schede.ToList()
                If scheda Is _attiva OrElse scheda.Sospesa OrElse scheda.EPopup OrElse scheda.ModalitaDirettaAttiva Then Continue For
                If (DateTime.Now - scheda.IstanteUltimoUso).TotalMinutes < MinutiPrimaDellaSospensione Then Continue For
                If _registroDownload.DownloadDellaScheda(scheda) > 0 Then Continue For
                Dim host As String = WGArchivioBrowser.HostDi(scheda.Indirizzo)
                If WGImpostazioniBrowser.SitoInControlloCompleto(host) Then Continue For
                If archivio IsNot Nothing AndAlso archivio.PermessoSito(WGArchivioBrowser.OrigineDi(scheda.Indirizzo), "notifiche") = "consenti" Then Continue For
                scheda.Sospendi()
                attiveNonSospese -= 1
                If attiveNonSospese <= 3 Then Exit For
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrowser.timerSospensione_Tick: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_AltPremuto() Handles tastiera.AltPremuto
        Try
            If Not PuoRicevere() Then Return
            If _menuOpzioniAperto Then Return
            If _attiva IsNot Nothing AndAlso _attiva.Cursore IsNot Nothing Then _attiva.Cursore.FermaLetturaContinua()
            ApriOpzioni()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.tastiera_AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub tastiera_KeyDown(ByRef tastoRicevuto As Keys) Handles tastiera.KeyDown
        Try
            If tastoRicevuto = Keys.None Then Return
            If Not PuoRicevere() Then Return
            If _menuOpzioniAperto Then
                GestisciTastoOpzioni(tastoRicevuto)
                Return
            End If
            If TastoDiChiusuraMenu(tastoRicevuto) Then
                tastoRicevuto = Keys.None
                Return
            End If
            Dim modificatori As Keys = tastiera.ModificatoriUltimoTasto
            Dim pressione As New PressioneTastoBrowser()
            pressione.Tasto = tastoRicevuto And Keys.KeyCode
            pressione.Shift = (modificatori And Keys.Shift) = Keys.Shift
            pressione.Ctrl = (modificatori And Keys.Control) = Keys.Control
            pressione.Alt = (modificatori And Keys.Alt) = Keys.Alt
            pressione.AltGr = tastiera.AltGrUltimoTasto
            If pressione.AltGr Then
                pressione.Ctrl = False
                pressione.Alt = False
            End If
            If _prossimoTastoAllaPagina Then
                _prossimoTastoAllaPagina = False
                pressione.Passato = (DateTime.Now - _istantePassaggioRichiesto).TotalSeconds <= 15
            End If
            tastoRicevuto = Keys.None
            codaTasti.Enqueue(pressione)
            If Not elaborazioneInCorso Then ElaboraCodaTasti()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Friend ReadOnly Property TastiInCoda As Integer
        Get
            Try
                Return codaTasti.Count
            Catch ex As Exception
                log.ScriviLog("WGBrowser.TastiInCoda: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Private Async Sub ElaboraCodaTasti()
        elaborazioneInCorso = True
        Try
            While codaTasti.Count > 0 AndAlso Not _chiuso
                Dim pressione As PressioneTastoBrowser = codaTasti.Dequeue()
                Try
                    If Not GestisciTastoGlobale(pressione) AndAlso _attiva IsNot Nothing Then Await _attiva.GestisciTastoAsync(pressione)
                Catch ex As Exception
                    log.ScriviLog("WGBrowser.ElaboraCodaTasti tasto: " & ex.ToString())
                End Try
            End While
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ElaboraCodaTasti: " & ex.ToString())
        Finally
            elaborazioneInCorso = False
        End Try
    End Sub

    Private Function GestisciTastoGlobale(p As PressioneTastoBrowser) As Boolean
        Try
            If p.Passato Then Return False
            Dim scheda As WGSchedaBrowser = _attiva
            Dim diretta As Boolean = scheda IsNot Nothing AndAlso scheda.ModalitaDirettaAttiva
            Dim inCampo As Boolean = scheda IsNot Nothing AndAlso scheda.InCampo
            Dim nessunModificatore As Boolean = Not p.Shift AndAlso Not p.Ctrl AndAlso Not p.Alt
            If p.Tasto = Keys.F9 AndAlso Not p.Alt AndAlso Not p.Ctrl Then
                If p.Shift Then
                    PassaProssimoTastoAllaPagina()
                ElseIf scheda IsNot Nothing Then
                    scheda.CommutaModalitaDiretta()
                End If
                Return True
            End If
            If p.Tasto = Keys.F8 AndAlso p.Shift AndAlso Not p.Ctrl AndAlso Not p.Alt Then
                WGAriaMonitor.MostraStorico()
                Return True
            End If
            If p.Tasto = Keys.F1 AndAlso nessunModificatore Then
                MostraGuida()
                Return True
            End If
            If _modoLettore AndAlso p.Ctrl AndAlso Not p.Alt Then
                ' Nel lettore niente schede, indirizzi, preferiti, cronologia e download; Control W lo chiude.
                Select Case p.Tasto
                    Case Keys.W
                        Chiudi()
                        Return True
                    Case Keys.T, Keys.N, Keys.D, Keys.B, Keys.H, Keys.J, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9
                        Return True
                    Case Keys.L, Keys.E
                        If p.Shift = (p.Tasto = Keys.E) Then Return True
                End Select
            End If
            If p.Ctrl AndAlso Not p.Alt Then
                Select Case p.Tasto
                    Case Keys.T
                        If p.Shift Then RiapriSchedaChiusa() Else ChiediNuovaScheda()
                        Return True
                    Case Keys.N
                        If p.Shift Then
                            ChiediNuovaSchedaPrivata()
                            Return True
                        End If
                    Case Keys.W, Keys.F4
                        If scheda IsNot Nothing Then ChiudiScheda(scheda, False)
                        Return True
                    Case Keys.L
                        If p.Shift AndAlso scheda IsNot Nothing Then
                            scheda.ModalitaLettura()
                        Else
                            ChiediIndirizzo()
                        End If
                        Return True
                    Case Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8
                        If Not p.Shift Then
                            VaiAllaScheda(CInt(p.Tasto) - CInt(Keys.D0))
                            Return True
                        End If
                    Case Keys.D9
                        If Not p.Shift Then
                            VaiAllaScheda(_schede.Count)
                            Return True
                        End If
                    Case Keys.P
                        If p.Shift AndAlso scheda IsNot Nothing Then
                            scheda.InserisciPassword()
                            Return True
                        End If
                End Select
                If Not diretta AndAlso Not inCampo Then
                    Select Case p.Tasto
                        Case Keys.D
                            If Not p.Shift AndAlso scheda IsNot Nothing Then
                                _gestore.AggiungiPreferito(scheda.Titolo, scheda.Indirizzo)
                                Return True
                            End If
                        Case Keys.B
                            _gestore.MostraPreferiti()
                            Return True
                        Case Keys.H
                            _gestore.MostraCronologia()
                            Return True
                        Case Keys.J
                            _gestore.MostraDownload()
                            Return True
                        Case Keys.E
                            If p.Shift Then
                                MostraElencoSchede()
                                Return True
                            End If
                    End Select
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.GestisciTastoGlobale: " & ex.Message)
        End Try
        Return False
    End Function

    Private Sub PassaProssimoTastoAllaPagina()
        Try
            _prossimoTastoAllaPagina = True
            _istantePassaggioRichiesto = DateTime.Now
            tastiera.PassaProssimoTasto()
            lettura.LeggiTesto("Il prossimo tasto va alla pagina")
        Catch ex As Exception
            log.ScriviLog("WGBrowser.PassaProssimoTastoAllaPagina: " & ex.Message)
        End Try
    End Sub

    Friend Sub ChiediIndirizzo()
        Try
            _campoIndirizzo = New WGCampoTesto()
            _campoIndirizzo.init("Indirizzo del sito da visitare o testo da cercare con " & WGImpostazioniBrowser.NomeMotore(), Indirizzo)
            AddHandler _campoIndirizzo.chiusura, Sub(s As Object, e As EventArgs) IndirizzoConfermato(False)
            _campoIndirizzo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiediIndirizzo: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediNuovaSchedaPrivata()
        Try
            _campoIndirizzo = New WGCampoTesto()
            _campoIndirizzo.init("Nuova scheda privata: indirizzo o testo da cercare, vuoto per la pagina iniziale. Non verranno salvati cronologia, cookie, password e permessi")
            AddHandler _campoIndirizzo.chiusura, Sub(s As Object, e As EventArgs) IndirizzoPrivatoConfermato()
            _campoIndirizzo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiediNuovaSchedaPrivata: " & ex.Message)
        End Try
    End Sub

    Private Sub IndirizzoPrivatoConfermato()
        Try
            Dim campo As WGCampoTesto = _campoIndirizzo
            _campoIndirizzo = Nothing
            If campo Is Nothing Then Return
            Dim testo As String = If(campo.OttieniTesto(), "").Trim()
            campo.chiudi()
            Dim destinazione As String
            If testo = "" Then
                destinazione = WGImpostazioniBrowser.PaginaIniziale
            ElseIf SembraIndirizzo(testo) Then
                destinazione = testo
            Else
                destinazione = WGImpostazioniBrowser.IndirizzoRicerca(testo)
            End If
            Dim scheda As WGSchedaBrowser = NuovaScheda(destinazione, False, Nothing, True, True)
            If scheda IsNot Nothing Then Annuncia("Navigazione privata")
        Catch ex As Exception
            log.ScriviLog("WGBrowser.IndirizzoPrivatoConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediNuovaScheda()
        Try
            _campoIndirizzo = New WGCampoTesto()
            _campoIndirizzo.init("Nuova scheda: indirizzo o testo da cercare, vuoto per la pagina iniziale")
            AddHandler _campoIndirizzo.chiusura, Sub(s As Object, e As EventArgs) IndirizzoConfermato(True)
            _campoIndirizzo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiediNuovaScheda: " & ex.Message)
        End Try
    End Sub

    Private Sub IndirizzoConfermato(inNuovaScheda As Boolean)
        Try
            Dim campo As WGCampoTesto = _campoIndirizzo
            _campoIndirizzo = Nothing
            If campo Is Nothing Then Return
            Dim testo As String = If(campo.OttieniTesto(), "").Trim()
            campo.chiudi()
            If testo = "" Then
                If inNuovaScheda Then NuovaScheda(WGImpostazioniBrowser.PaginaIniziale, False, Nothing)
                Return
            End If
            If Not inNuovaScheda AndAlso String.Equals(testo, Indirizzo, StringComparison.OrdinalIgnoreCase) Then Return
            If SembraIndirizzo(testo) Then
                ApriIndirizzo(testo, inNuovaScheda)
                Return
            End If
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            Dim suggerimenti As List(Of Tuple(Of String, String)) = If(archivio IsNot Nothing, archivio.Suggerimenti(testo, 12), New List(Of Tuple(Of String, String))())
            If suggerimenti.Count = 0 Then
                ApriIndirizzo(testo, inNuovaScheda)
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Risultati per " & testo)
            lista.aggiungiElemento("Cerca " & testo & " con " & WGImpostazioniBrowser.NomeMotore(), "cerca")
            Dim indirizzi As New Dictionary(Of String, String)()
            For i As Integer = 0 To suggerimenti.Count - 1
                Dim id As String = "s" & i.ToString()
                indirizzi(id) = suggerimenti(i).Item2
                lista.aggiungiElemento(suggerimenti(i).Item1 & ", " & WGGestoreBrowser.DescriviIndirizzo(suggerimenti(i).Item2), id)
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          lista.abbandona()
                                                          Dim destinazione As String = Nothing
                                                          If id = "cerca" Then
                                                              ApriIndirizzo(WGImpostazioniBrowser.IndirizzoRicerca(testo), inNuovaScheda)
                                                          ElseIf indirizzi.TryGetValue(id, destinazione) Then
                                                              ApriIndirizzo(destinazione, inNuovaScheda)
                                                          End If
                                                      Catch exScelta As Exception
                                                          log.ScriviLog("WGBrowser.IndirizzoConfermato scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 log.ScriviLog("WGBrowser.IndirizzoConfermato esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.IndirizzoConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoSchede()
        Try
            If _schede.Count = 0 Then Return
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Schede aperte: " & _schede.Count)
            Dim mappa As New Dictionary(Of String, WGSchedaBrowser)()
            For i As Integer = 0 To _schede.Count - 1
                Dim scheda As WGSchedaBrowser = _schede(i)
                Dim id As String = "t" & scheda.Id.ToString()
                mappa(id) = scheda
                lista.aggiungiElemento((i + 1) & ". " & scheda.DescrizioneBreve() & ", " & WGGestoreBrowser.DescriviIndirizzo(scheda.Indirizzo) & If(scheda Is _attiva, ", scheda attuale", ""), id)
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          lista.abbandona()
                                                          Dim scelta As WGSchedaBrowser = Nothing
                                                          If mappa.TryGetValue(id, scelta) Then AttivaScheda(scelta, "")
                                                      Catch exScelta As Exception
                                                          log.ScriviLog("WGBrowser.MostraElencoSchede scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            lista.aggiungiOpzione("Chiudi la scheda", Sub(s, e)
                                                          Try
                                                              Dim id As String = lista.ValoreSelezionato
                                                              If String.IsNullOrEmpty(id) Then
                                                                  Dim selezionati As List(Of String) = lista.ottieniElementi()
                                                                  If selezionati IsNot Nothing AndAlso selezionati.Count > 0 Then id = selezionati(0)
                                                              End If
                                                              Dim scelta As WGSchedaBrowser = Nothing
                                                              lista.abbandona()
                                                              If id IsNot Nothing AndAlso mappa.TryGetValue(id, scelta) Then ChiudiScheda(scelta, False)
                                                          Catch exChiudi As Exception
                                                              log.ScriviLog("WGBrowser.MostraElencoSchede chiudi: " & exChiudi.Message)
                                                          End Try
                                                      End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 log.ScriviLog("WGBrowser.MostraElencoSchede esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraElencoSchede: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiVoce(menu As WGMenu, testo As String, azione As Action)
        menu.aggiungi(testo, Sub(s, e)
                                 _azioneMenuEseguita = True
                                 SegnaRitornoDaMenu()
                                 Try
                                     azione()
                                 Catch ex As Exception
                                     log.ScriviLog("WGBrowser.AggiungiVoce " & testo & ": " & ex.ToString())
                                 End Try
                             End Sub)
    End Sub

    Private Sub AggiungiOpzione(testoVoce As String, azione As Action)
        Try
            If String.IsNullOrWhiteSpace(testoVoce) OrElse azione Is Nothing Then Return
            If _eventiOpzioni.ContainsKey(testoVoce) Then Return
            _eventiOpzioni(testoVoce) = Sub(s As Object, e As EventArgs)
                                            Try
                                                azione()
                                            Catch exAzione As Exception
                                                log.ScriviLog("WGBrowser.AggiungiOpzione " & testoVoce & ": " & exAzione.ToString())
                                            End Try
                                        End Sub
            menuopzioni.Items.Add(testoVoce)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AggiungiOpzione: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaOpzioni()
        Try
            _eventiOpzioni.Clear()
            menuopzioni.Items.Clear()
            Dim scheda As WGSchedaBrowser = _attiva
            AggiungiOpzioniCampo(scheda)
            If _modoLettore Then
                PreparaOpzioniLettore(scheda)
                Return
            End If
            AggiungiOpzione("Vai a un indirizzo, Control L", AddressOf ChiediIndirizzo)
            AggiungiOpzione("Nuova scheda, Control T", AddressOf ChiediNuovaScheda)
            AggiungiOpzione("Nuova scheda privata, Control Shift N", AddressOf ChiediNuovaSchedaPrivata)
            AggiungiOpzione("Schede aperte: " & _schede.Count & ", Control Shift E", AddressOf MostraElencoSchede)
            If scheda IsNot Nothing Then
                AggiungiOpzione("Chiudi questa scheda, Control W", Sub() ChiudiScheda(scheda, False))
                If _chiuse.Count > 0 Then AggiungiOpzione("Riapri l'ultima scheda chiusa, Control Shift T", AddressOf RiapriSchedaChiusa)
                AggiungiOpzione("Pagina precedente, Alt freccia sinistra", AddressOf scheda.NavigaIndietro)
                AggiungiOpzione("Pagina successiva, Alt freccia destra", AddressOf scheda.NavigaAvanti)
                AggiungiOpzione("Ricarica la pagina, Control R", AddressOf scheda.Ricarica)
                If scheda.CaricamentoInCorso Then AggiungiOpzione("Interrompi il caricamento", AddressOf scheda.Ferma)
                AggiungiOpzione("Pagina iniziale, Alt Home", Sub() scheda.Naviga(WGImpostazioniBrowser.PaginaIniziale))
                AggiungiOpzione("Cerca nella pagina, Control F", AddressOf scheda.CercaNellaPagina)
                AggiungiOpzione("Elenchi di intestazioni, link, campi e altri elementi, F7", AddressOf scheda.MostraElenchiElementi)
                AggiungiOpzione("Azioni sull'elemento corrente, tasto Applicazioni", AddressOf scheda.MostraAzioniElemento)
                If scheda.ImmagineCorrente IsNot Nothing Then
                    AggiungiOpzione("Descrivi l'immagine con l'intelligenza artificiale", AddressOf scheda.DescriviImmagineCorrente)
                    AggiungiOpzione("Riconosci il testo dell'immagine", AddressOf scheda.RiconosciTestoImmagineCorrente)
                End If
                AggiungiOpzione("Aggiungi ai preferiti, Control D", Sub() _gestore.AggiungiPreferito(scheda.Titolo, scheda.Indirizzo))
            End If
            AggiungiOpzione("Preferiti, Control B", Sub() _gestore.MostraPreferiti())
            AggiungiOpzione("Cronologia, Control H", Sub() _gestore.MostraCronologia())
            AggiungiOpzione("Download, Control J", Sub() _gestore.MostraDownload())
            If scheda IsNot Nothing Then
                AggiungiOpzione("Strumenti della pagina", Sub() MostraStrumentiPagina(scheda))
                AggiungiOpzione("Impostazioni di questo sito", AddressOf scheda.MostraImpostazioniSito)
                AggiungiOpzione(If(scheda.TraduzioneAttiva, "Mostra la pagina originale, Control Shift U", "Traduci la pagina " & WGTraduttore.ALingua(WGTraduttore.LinguaDestinazione) & ", Control Shift U"), AddressOf scheda.CommutaTraduzione)
                If scheda.HostPagina <> "" Then
                    AggiungiOpzione(If(WGImpostazioniBrowser.SitoNonTradotto(scheda.HostPagina), "Consenti di nuovo la traduzione di " & scheda.HostPagina, "Non tradurre mai le pagine di " & scheda.HostPagina), AddressOf scheda.CommutaEccezioneTraduzione)
                End If
                AggiungiOpzione(If(scheda.ModalitaDirettaAttiva, "Disattiva il controllo completo alla pagina, F9", "Dai il controllo completo alla pagina, F9"), AddressOf scheda.CommutaModalitaDiretta)
                AggiungiOpzione("Invia il prossimo tasto alla pagina, Shift F9", AddressOf PassaProssimoTastoAllaPagina)
            End If
            AggiungiOpzione("Annunci ricevuti dalle pagine, Shift F8", AddressOf WGAriaMonitor.MostraStorico)
            AggiungiOpzione("Password, dati dei moduli e permessi dei siti", Sub() _gestore.MostraPasswordEDati())
            AggiungiOpzione("Configurazione", Sub() _gestore.MostraConfigurazione())
            AggiungiOpzione("Altre impostazioni del browser", Sub() _gestore.MostraImpostazioni())
            AggiungiOpzione("Guida dei tasti, F1", AddressOf MostraGuida)
            AggiungiOpzione("Chiudi il browser", AddressOf Chiudi)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.PreparaOpzioni: " & ex.ToString())
        End Try
    End Sub

    ' Il lettore: prima le azioni di chi lo ha aperto (rispondi, inoltra...), poi soltanto quello che serve
    ' a leggere la pagina.
    Private Sub PreparaOpzioniLettore(scheda As WGSchedaBrowser)
        Try
            For Each voce As Tuple(Of String, Action) In _opzioniLettore
                AggiungiOpzione(voce.Item1, voce.Item2)
            Next
            If scheda IsNot Nothing Then
                AggiungiOpzione("Cerca nel messaggio, Control F", AddressOf scheda.CercaNellaPagina)
                AggiungiOpzione("Elenchi di intestazioni, link, tabelle e altri elementi, F7", AddressOf scheda.MostraElenchiElementi)
                AggiungiOpzione("Azioni sull'elemento corrente, tasto Applicazioni", AddressOf scheda.MostraAzioniElemento)
                If scheda.ImmagineCorrente IsNot Nothing Then
                    AggiungiOpzione("Descrivi l'immagine con l'intelligenza artificiale", AddressOf scheda.DescriviImmagineCorrente)
                    AggiungiOpzione("Riconosci il testo dell'immagine", AddressOf scheda.RiconosciTestoImmagineCorrente)
                End If
                AggiungiOpzione(If(scheda.TraduzioneAttiva, "Mostra il testo originale, Control Shift U", "Traduci il messaggio " & WGTraduttore.ALingua(WGTraduttore.LinguaDestinazione) & ", Control Shift U"), AddressOf scheda.CommutaTraduzione)
                AggiungiOpzione("Leggi dall'inizio", AddressOf scheda.LeggiDallInizio)
                AggiungiOpzione("Copia tutto il testo", AddressOf scheda.CopiaTestoPagina)
            End If
            AggiungiOpzione("Guida dei tasti, F1", AddressOf MostraGuida)
            AggiungiOpzione("Chiudi, Esc", AddressOf Chiudi)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.PreparaOpzioniLettore: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriOpzioni()
        Try
            If _menuOpzioniAperto OrElse _chiuso OrElse Me.IsDisposed Then Return
            If menuopzioni Is Nothing OrElse menuopzioni.IsDisposed Then
                menuopzioni = New WGListaView()
                menuopzioni.Visible = False
                menuopzioni.Enabled = False
                Me.Controls.Add(menuopzioni)
            End If
            PreparaOpzioni()
            If menuopzioni.Items.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono opzioni disponibili")
                messaggio.ShowDialog()
                Return
            End If
            _menuOpzioniAperto = True
            menuopzioni.Titolo = If(_modoLettore, "Opzioni", If(_attiva IsNot Nothing AndAlso _attiva.InCampoDiTesto, "Opzioni del campo e del browser", "Opzioni del browser"))
            menuopzioni.Visible = True
            menuopzioni.Enabled = True
            menuopzioni.Bounds = Me.ClientRectangle
            menuopzioni.BringToFront()
            menuopzioni.Focus()
            Dim indice As Integer = If(_ultimaOpzione = "", -1, menuopzioni.Items.IndexOf(_ultimaOpzione))
            menuopzioni.SelectedIndex = If(indice >= 0, indice, 0)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ApriOpzioni: " & ex.ToString())
            ChiudiOpzioni(False)
        End Try
    End Sub

    Private Sub ChiudiOpzioni(annuncia As Boolean)
        Try
            If Not _menuOpzioniAperto Then Return
            _menuOpzioniAperto = False
            SegnaRitornoDaMenu()
            If menuopzioni IsNot Nothing AndAlso Not menuopzioni.IsDisposed Then
                Try
                    Dim indice As Integer = menuopzioni.SelectedIndex
                    If indice >= 0 AndAlso indice < menuopzioni.Items.Count Then _ultimaOpzione = menuopzioni.Items(indice).ToString()
                Catch exIndice As Exception
                    log.ScriviLog("WGBrowser.ChiudiOpzioni indice: " & exIndice.Message)
                End Try
                menuopzioni.Visible = False
                menuopzioni.Enabled = False
            End If
            If Not _chiuso AndAlso Not Me.IsDisposed Then Me.Focus()
            If annuncia Then AnnunciaRitornoAllaPagina()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiudiOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiOpzioneCorrente()
        Try
            If Not _menuOpzioniAperto OrElse menuopzioni Is Nothing OrElse menuopzioni.IsDisposed Then Return
            Dim indice As Integer = menuopzioni.SelectedIndex
            If indice < 0 OrElse indice >= menuopzioni.Items.Count Then Return
            Dim testoVoce As String = menuopzioni.Items(indice).ToString()
            Dim evento As EventHandler = Nothing
            If Not _eventiOpzioni.TryGetValue(testoVoce, evento) OrElse evento Is Nothing Then Return
            ChiudiOpzioni(False)
            evento.Invoke(Me, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.EseguiOpzioneCorrente: " & ex.ToString())
        End Try
    End Sub

    Private Sub GestisciTastoOpzioni(ByRef tasto As Keys)
        Try
            Dim codice As Keys = tasto And Keys.KeyCode
            Dim modificatori As Keys = tastiera.ModificatoriUltimoTasto
            If codice = Keys.Tab AndAlso (modificatori And Keys.Alt) = Keys.Alt Then
                tasto = Keys.None
                ChiudiOpzioni(False)
                Return
            End If
            Select Case codice
                Case Keys.Enter, Keys.Right
                    tasto = Keys.None
                    EseguiOpzioneCorrente()
                Case Keys.Escape, Keys.Back, Keys.Left
                    tasto = Keys.None
                    ChiudiOpzioni(True)
                Case Keys.F1
                    tasto = Keys.None
            End Select
        Catch ex As Exception
            log.ScriviLog("WGBrowser.GestisciTastoOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub menuopzioni_InvioPremuto(sender As Object, e As EventArgs) Handles menuopzioni.InvioPremuto
        Try
            If Me.IsDisposed OrElse Not _menuOpzioniAperto Then Return
            EseguiOpzioneCorrente()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.menuopzioni_InvioPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub menuopzioni_Avanti(sender As Object, e As EventArgs) Handles menuopzioni.Avanti
        Try
            If Me.IsDisposed OrElse Not _menuOpzioniAperto Then Return
            EseguiOpzioneCorrente()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.menuopzioni_Avanti: " & ex.Message)
        End Try
    End Sub

    Private Sub menuopzioni_EscPremuto(sender As Object, e As EventArgs) Handles menuopzioni.EscPremuto
        Try
            If Me.IsDisposed OrElse Not _menuOpzioniAperto Then Return
            ChiudiOpzioni(True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.menuopzioni_EscPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub menuopzioni_Indietro(sender As Object, e As EventArgs) Handles menuopzioni.Indietro
        Try
            If Me.IsDisposed OrElse Not _menuOpzioniAperto Then Return
            ChiudiOpzioni(True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.menuopzioni_Indietro: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraStrumentiPagina(scheda As WGSchedaBrowser)
        Try
            _azioneMenuEseguita = False
            Dim menu As New WGMenu()
            AggiungiVoce(menu, "Modalità lettura dell'articolo, Control Shift L", AddressOf scheda.ModalitaLettura)
            AggiungiVoce(menu, "Lettura semplificata del sito WordPress", AddressOf scheda.LetturaSemplificataWordPress)
            AggiungiVoce(menu, "Leggi dall'inizio della pagina", AddressOf scheda.LeggiDallInizio)
            AggiungiVoce(menu, "Informazioni sulla pagina, Control I", AddressOf scheda.LeggiInformazioniPagina)
            AggiungiVoce(menu, "Copia l'indirizzo della pagina", AddressOf scheda.CopiaIndirizzo)
            AggiungiVoce(menu, "Copia tutto il testo della pagina", AddressOf scheda.CopiaTestoPagina)
            AggiungiVoce(menu, "Stampa la pagina, Control P", AddressOf scheda.Stampa)
            AggiungiVoce(menu, "Salva la pagina, Control S", AddressOf scheda.SalvaPagina)
            AggiungiVoce(menu, "Ingrandisci, Control più", Sub() scheda.CambiaZoom(1))
            AggiungiVoce(menu, "Riduci, Control meno", Sub() scheda.CambiaZoom(-1))
            AggiungiVoce(menu, "Dimensione normale, Control zero", Sub() scheda.CambiaZoom(0))
            AggiungiVoce(menu, "Riconosci il testo della parte visibile", AddressOf scheda.RiconosciTestoVista)
            AggiungiVoce(menu, "Descrivi la parte visibile con l'intelligenza artificiale", AddressOf scheda.DescriviVista)
            AggiungiVoce(menu, "Riassumi la pagina con l'intelligenza artificiale", AddressOf scheda.RiassumiPagina)
            AggiungiVoce(menu, "Traduci la pagina in italiano con l'intelligenza artificiale", AddressOf scheda.TraduciPagina)
            AggiungiVoce(menu, "Riproduci i video della pagina con il lettore di WinGabriel, Control Shift V", Sub() scheda.RiproduciConWinGabriel(""))
            AggiungiVoce(menu, "Riproduci il video con il lettore della pagina, Control Shift Y", AddressOf scheda.RiproduciDallaPagina)
            AggiungiVoce(menu, "Inserisci una password salvata, Control Shift P", AddressOf scheda.InserisciPassword)
            AggiungiVoce(menu, "Compila il modulo con i dati personali, Control Shift M", AddressOf scheda.CompilaModulo)
            If scheda.CondivisioneAttiva Then AggiungiVoce(menu, "Interrompi la condivisione dello schermo", AddressOf scheda.InterrompiCondivisione)
            AddHandler menu.abbandonato, AddressOf MenuStrumentiAbbandonato
            menu.avvia("Strumenti della pagina", True)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraStrumentiPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuStrumentiAbbandonato(sender As Object, e As EventArgs)
        Try
            SegnaRitornoDaMenu()
            If _azioneMenuEseguita Then Return
            SuUI(AddressOf AnnunciaRitornoAllaPagina)
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MenuStrumentiAbbandonato: " & ex.Message)
        End Try
    End Sub

    ' Tornando visibile (chiusura di un menu o di un campo aperto dal browser, cambio di applicazione)
    ' il browser dice il titolo della pagina e la riga corrente, come fanno i menu con titolo e voce.
    Private _primaVisibilita As Boolean = True
    Private _tickUltimoRitorno As Integer = 0

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        Try
            If Not Me.Visible OrElse _chiuso Then Return
            If _primaVisibilita Then
                _primaVisibilita = False
                Return
            End If
            AnnunciaRitornoRinviato()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.OnVisibleChanged: " & ex.Message)
        End Try
    End Sub

    Private Async Sub AnnunciaRitornoRinviato()
        Try
            Await Task.Delay(120)
            If _chiuso OrElse Me.IsDisposed OrElse Not Me.Visible OrElse _menuOpzioniAperto Then Return
            If Environment.TickCount - _tickUltimoRitorno < 1500 OrElse lettura.StaParlando Then Return
            AnnunciaRitornoAllaPagina()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AnnunciaRitornoRinviato: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaRitornoAllaPagina(Optional forza As Boolean = False)
        Try
            If _chiuso Then Return
            If Not forza AndAlso Environment.TickCount - _tickUltimoRitorno < 1000 Then Return
            _tickUltimoRitorno = Environment.TickCount
            Dim scheda As WGSchedaBrowser = _attiva
            If scheda Is Nothing Then Return
            Dim riga As RigaVirtuale = If(scheda.Cursore IsNot Nothing, scheda.Cursore.RigaCorrente, Nothing)
            lettura.LeggiTesto(If(_modoLettore, "", "Browser, ") & scheda.Titolo & If(riga IsNot Nothing AndAlso Not scheda.ModalitaDirettaAttiva, ". " & riga.CostruisciAnnuncio(False), ""))
        Catch ex As Exception
            log.ScriviLog("WGBrowser.AnnunciaRitornoAllaPagina: " & ex.Message)
        End Try
    End Sub

    Private Const TestoGuida As String =
        "Guida del browser di WinGabriel." & vbCrLf & vbCrLf &
        "Schede:" & vbCrLf &
        "Control Tab e Control Shift Tab: scheda successiva e precedente. Control da 1 a 8: vai a quella scheda, Control 9 all'ultima." & vbCrLf &
        "Control Shift N: nuova scheda privata. Nelle schede private non vengono salvati cronologia, cookie, accessi ai siti, password, zoom e permessi; i link aperti da una scheda privata restano privati. Chiudendo l'ultima scheda privata tutto quello che la sessione privata aveva raccolto viene cancellato. Il nome della scheda dice navigazione privata." & vbCrLf &
        "Control T: nuova scheda. Control W: chiude la scheda. Control Shift T: riapre l'ultima scheda chiusa. Control Shift E: elenco delle schede." & vbCrLf &
        "I link che aprono una nuova finestra, come gli accessi con Google o Facebook, si aprono in una nuova scheda; quando la pagina chiude la finestra si torna alla scheda di partenza." & vbCrLf &
        "Control Invio su un link: lo apre in una scheda in secondo piano. Shift Invio: in una nuova scheda." & vbCrLf & vbCrLf &
        "Navigazione nella pagina:" & vbCrLf &
        "Frecce su e giù: riga precedente e successiva. Frecce destra e sinistra: carattere per carattere. Control con le frecce orizzontali: parola per parola." & vbCrLf &
        "Home e Fine, oppure Control Home e Control Fine: inizio e fine della pagina. Pagina su e Pagina giù: quindici righe alla volta." & vbCrLf &
        "Invio o Spazio: attiva link, pulsanti ed elementi cliccabili, seleziona le caselle, entra nei campi per scrivere." & vbCrLf &
        "Tab e Shift Tab: elemento successivo e precedente come in ogni browser." & vbCrLf &
        "Lettere di navigazione rapida, con Shift per tornare indietro: H intestazione, da 1 a 6 intestazione di quel livello, K link, B pulsante o elemento cliccabile, V solo elementi cliccabili senza ruolo, F campo, E campo di modifica, C casella combinata, X casella di controllo, O pulsante di opzione, G immagine, J video, audio o area disegnata, R o D regione, L elenco, I elemento di elenco, T tabella, M voce di menu, Y scheda della pagina, A articolo, Q citazione, P paragrafo, W frame, N testo dopo un blocco di link, Z primo contenuto comparso dopo l'ultimo aggiornamento." & vbCrLf &
        "Regioni e articoli come in JAWS, impostazione attiva di serie: entrando con le frecce in una regione o in un articolo se ne sente prima il nome da solo e alla freccia successiva la riga; H e le altre lettere leggono solo l'elemento trovato, senza la regione; R, D e A leggono solo il nome della regione o dell'articolo e con freccia giù si legge la prima riga. Si può spegnere dalle altre impostazioni del browser." & vbCrLf &
        "Selezione: Shift con le frecce seleziona carattere per carattere e riga per riga, Control Shift con le frecce orizzontali parola per parola, Shift Home e Shift Fine fino all'inizio o alla fine della riga, Control Shift Home e Control Shift Fine fino all'inizio o alla fine della pagina; si sente il testo selezionato o deselezionato. Control A seleziona tutta la pagina. Control C o Control Insert copia la selezione. Esc o un tasto di spostamento annulla la selezione." & vbCrLf &
        "Tabelle: Control Alt con le frecce si sposta di cella in cella annunciando le intestazioni di riga e di colonna; Control Alt Spazio dice la posizione nella tabella." & vbCrLf &
        "I link interni, come Vai al contenuto, spostano la lettura sul punto indicato." & vbCrLf &
        "Quando cambia lingua il testo viene preceduto dal nome della lingua, per esempio In inglese." & vbCrLf &
        "Insert o F8: lettura continua dalla riga corrente, un tasto qualsiasi la ferma." & vbCrLf &
        "F7: elenchi di intestazioni, link, pulsanti, campi, regioni, tabelle, immagini e contenuti nuovi." & vbCrLf &
        "Tasto Applicazioni o Shift F10: azioni sull'elemento, anche quelle possibili solo col mouse: passare sopra, clic destro, doppio clic, trascinare e rilasciare, scorrere con la rotellina, descrivere un'immagine con l'intelligenza artificiale, riconoscerne il testo, comandare video e audio." & vbCrLf &
        "Backspace o Alt freccia sinistra: pagina precedente. Shift Backspace o Alt freccia destra: pagina successiva. Alt Home: pagina iniziale. Control R: ricarica." & vbCrLf &
        "Control L o Alt D: vai a un indirizzo, con i suggerimenti dai preferiti e dalla cronologia. Control F: cerca nella pagina, F3 o Control G: cerca ancora, con Shift all'indietro." & vbCrLf &
        "Control Shift L: modalità lettura. L'articolo della pagina viene estratto con lo stesso sistema dei quotidiani di WinGabriel, con Trafilatura quando è installato, e si apre nel campo di lettura con titolo, autore, data, sito, sottotitoli, elenchi e citazioni, senza menu né pubblicità; gli articoli divisi in più pagine vengono uniti. Dal menu Alt del campo si può riassumere o copiare l'articolo." & vbCrLf &
        "Control C: copia la selezione o, se non c'è, la riga. Control I: informazioni sulla pagina. Control P: stampa; se è la pagina a chiedere di stampare, WinGabriel chiede conferma e usa la sua stampante. Control S: salva come PDF o testo. Control più, meno e zero: zoom." & vbCrLf &
        "Control D: aggiunge ai preferiti. Control B: preferiti. Control H: cronologia. Control J: download." & vbCrLf &
        "Formule matematiche: si leggono in italiano, per esempio x al quadrato più 1, frazione con numeratore e denominatore, radice quadrata di. Invio su una formula la apre a pezzi: ogni voce è una parte, Invio entra nella parte, Esc torna indietro; se la pagina la fornisce, la prima voce copia la formula in LaTeX." & vbCrLf &
        "Control Shift K: verifiche anti-robot come reCAPTCHA, hCaptcha e Cloudflare. Il browser avvisa quando una pagina ne contiene una; Control Shift K porta sulla casella da selezionare o, se la verifica chiede di riconoscere delle immagini, passa alla verifica audio: si ascolta con Invio sul pulsante di riproduzione e si scrive nel campo quello che si è sentito." & vbCrLf &
        "Control F: cerca un testo nella pagina; F3 e Control G cercano il successivo, Shift F3 e Control Shift G il precedente. Si sente quale risultato è sul totale, il cursore va alla parola trovata e la pagina scorre fino a lì evidenziandola, anche quando il testo compare più volte nella stessa riga." & vbCrLf &
        "Immagini: quando il cursore è su un'immagine, il menu delle opzioni (Alt) permette di descriverla con l'intelligenza artificiale configurata in WinGabriel o di riconoscerne il testo. Nella configurazione del browser si può attivare la descrizione automatica delle immagini senza descrizione: vengono descritte quando il cursore ci arriva, una alla volta e al massimo 20 per pagina, perché ogni immagine consuma una richiesta dell'intelligenza artificiale." & vbCrLf &
        "Control Shift U: traduce la pagina nella lingua scelta nelle impostazioni del traduttore, oppure torna al testo originale. La pagina vera non cambia, quindi link, pulsanti e campi continuano a funzionare: cambia solo il testo che si legge. Si traducono subito le righe intorno al cursore, poi il resto della pagina. Nella configurazione del browser si sceglie se, per le pagine in un'altra lingua, chiedere per ogni sito, tradurre senza chiedere o tradurre solo a comando, e si possono indicare i siti da non tradurre mai; lo stesso si fa dal menu delle opzioni per il sito della pagina." & vbCrLf &
        "Control Shift P: inserisce una password salvata. Control Shift M: compila il modulo con i dati personali. Control Shift V: riproduce i video con il lettore di WinGabriel. Control Shift Y: riproduce il video con il lettore della pagina." & vbCrLf &
        "Le altre combinazioni con Alt vengono inviate alla pagina, per i tasti di scelta rapida dei siti." & vbCrLf &
        "Alt da solo: menu delle opzioni. Esc: chiude il browser; con più schede aperte chiede di premerlo due volte." & vbCrLf & vbCrLf &
        "Controllo completo alla pagina:" & vbCrLf &
        "F9: i tasti vanno al sito, compresi Esc, lettere e combinazioni con Alt. H e Shift H spostano comunque all'intestazione successiva e precedente, come in navigazione, tranne nei campi di testo, nelle caselle combinate e nelle applicazioni che gestiscono da sole la tastiera. Frecce, Pagina su, Pagina giù, Home e Fine leggono la pagina come in navigazione, con Shift selezionano. Quando il fuoco è su un elenco, un menu, un albero, una griglia, delle schede, dei pulsanti di opzione, una barra degli strumenti o un cursore, i tasti di spostamento, Invio, Spazio, Esc e le lettere tranne H passano dalla tastiera di WinGabriel: WinGabriel li manda all'elemento e legge subito la voce raggiunta, lo stato che cambia, come espanso o selezionato, oppure il nuovo valore; tenendo premuta una freccia legge l'ultima voce raggiunta. Nei campi di testo si scrive; nei video e nelle applicazioni che dichiarano di gestire da sole la tastiera i tasti vanno al sito così come sono. Dopo essersi spostati con le frecce nella pagina, Invio e Spazio attivano l'elemento letto e Tab parte da lì. Dalle altre impostazioni del browser si può scegliere di mandare sempre le frecce al sito, sempre con la lettura di quello che cambia. La lettura usa lo stesso albero di accessibilità della navigazione: entrando in finestre di dialogo, elenchi, menu, griglie e regioni se ne sente il nome, nelle tabelle le intestazioni di riga e di colonna. Si sentono i cambi di fuoco, l'elemento attivo delle liste, gli elementi selezionati, i cambi di stato, le notifiche della pagina, le regioni live e i nuovi contenuti, come i messaggi in arrivo nelle chat, anche quando il sito non li annuncia." & vbCrLf &
        "Insert o F8: legge l'elemento a fuoco. Control Shift frecce su e giù: legge le righe della pagina senza lasciare il controllo completo. Shift F8: elenco degli ultimi annunci, da copiare o rileggere. F9 di nuovo: torna alla navigazione. Il browser ricorda i siti in cui lo si lascia attivo." & vbCrLf &
        "Shift F9: invia alla pagina il prossimo tasto, anche quelli che WinGabriel usa per sé come F2, F3, F4, F5, F6 e Alt F4." & vbCrLf &
        vbCrLf &
        "Nei campi:" & vbCrLf &
        "Si scrive normalmente, anche con i tasti morti delle tastiere straniere per gli accenti; lettere e parole vengono ripetute secondo le impostazioni di eco della digitazione. Frecce, Home, Fine e Control con le frecce muovono il cursore e leggono. Shift con le frecce seleziona." & vbCrLf &
        "Frecce su e giù: nei campi con suggerimenti scorrono i suggerimenti; nelle aree di testo cambiano riga; negli altri campi leggono il contenuto." & vbCrLf &
        "Invio: conferma, cerca o invia il modulo; nelle aree di testo va a capo. Esc: chiude i suggerimenti o esce dal campo." & vbCrLf &
        "Control A, C, X, V, Z e Y: seleziona tutto, copia, taglia, incolla, annulla e ripristina. Le altre combinazioni con Control vanno alla pagina." & vbCrLf &
        "Alt in un campo di testo: in cima al menu ci sono le stesse funzioni dei campi di WinGabriel. Modifica il testo in un campo di WinGabriel apre il testo con tutte le funzioni dei campi, come controllo ortografico, trova e sostituisci e riassunto, e alla chiusura lo rimette nella pagina. Poi dettatura, traduzione, sostituzione del testo con la traduzione, copia, taglia, incolla, seleziona tutto, cancella tutto, lettura del contenuto e uscita dal campo." & vbCrLf & vbCrLf &
        "Con lo schermo touch:" & vbCrLf &
        "Flick a destra o a sinistra: elemento successivo o precedente; da un campo si esce e si prosegue." & vbCrLf &
        "Flick con tre dita in giù o in su: gira il rotore, come su iPhone: intestazioni, link, pulsanti, campi del modulo, campi di testo, caselle combinate, caselle di controllo, pulsanti di opzione, tabelle, liste, elementi di lista, regioni, immagini, paragrafi, elementi cliccabili, righe, parole, caratteri e opzioni del browser. Nei campi di testo il rotore ha caratteri, parole, righe e opzioni del campo." & vbCrLf &
        "Flick in giù o in su con un dito: elemento successivo o precedente del tipo scelto nel rotore; sulla voce opzioni apre il menu." & vbCrLf &
        "Doppio tocco: attiva l'elemento; in un campo di testo mostra la tastiera a schermo. Doppio tocco prolungato: menu delle opzioni. Doppio tocco con due dita: Esc. Tre tocchi con due dita: elenco delle applicazioni. Flick in giù con due dita: lettura continua." & vbCrLf &
        "Nei campi di testo compare da sola la tastiera a schermo di WinGabriel, con i suggerimenti e il tasto della dettatura; chiudendola resta chiusa fino all'uscita dal campo, e si riapre con un doppio tocco." & vbCrLf & vbCrLf &
        "Siti e servizi:" & vbCrLf &
        "Caricare file: il file si sceglie con la scelta dei file di WinGabriel, che mostra i tipi di file accettati dalla pagina. Salvando una pagina, o un download quando nelle impostazioni è attivo Chiedi dove salvare ogni download, si sceglie la cartella e poi si scrive il nome del file. Permessi per microfono, fotocamera, posizione e notifiche: il browser chiede e ricorda la scelta. La posizione è quella del computer." & vbCrLf &
        "Accessi ai siti: appena arriva un cookie, di accesso o di qualsiasi altro tipo, viene salvato subito, cifrato per il tuo utente di Windows, nella cartella dei dati di WinGabriel, così finisce nel backup; lo stesso salvataggio avviene alla chiusura del browser e prima di ogni backup. Se il browser riparte con un profilo nuovo, per esempio dopo una reinstallazione o un ripristino, gli accessi vengono rimessi da soli. Su un altro computer o con un altro utente di Windows non si possono aprire, come le password salvate, e bisogna accedere di nuovo ai siti." & vbCrLf &
        "Download: nella cartella scelta nelle impostazioni, con l'avanzamento annunciato; i PDF si aprono nel lettore di WinGabriel. Dall'elenco dei download, Control J, i file si aprono con WinGabriel." & vbCrLf &
        "Configurazione, dal menu Alt, dal menu Browser di WinGabriel o dalle impostazioni di WinGabriel alla voce Browser: blocco della pubblicità, siti in cui consentirla, cronologia attiva o no, lingua in cui chiedere le pagine e browser con cui aprire i link delle e-mail e dei campi di WinGabriel: questo browser, quello predefinito, Chrome, Firefox o Edge. I link di posta elettronica si aprono nella posta di WinGabriel." & vbCrLf &
        "Il browser usa il motore Microsoft Edge WebView2 di Windows, basato su Chromium: se sul computer manca, WinGabriel propone di scaricarlo e installarlo. Il browser si presenta ai siti come Chrome, cioè come il motore Chromium su cui è costruito, con gli stessi dati che manda un Chrome vero della stessa versione: così quello che dichiara coincide con l'impronta della connessione e Google non lo scambia per un programma automatico. Se Google rifiuta comunque l'accesso, il browser prova da solo a presentarsi come Firefox e lo ricorda per tutti i servizi Google; dalle impostazioni del sito si può cambiare. Se una verifica di sicurezza di un sito non procede, prova da solo l'altra identità. Su Netflix e sugli altri servizi di video e musica protetti il browser si presenta invece come Microsoft Edge, come fa Edge stesso, perché così il sito usa PlayReady; l'identità si può cambiare per ogni sito dalle impostazioni del sito, e dalle altre impostazioni del browser si può scegliere di presentarsi sempre come Edge." & vbCrLf &
        "Video: quelli di YouTube partono da soli nel lettore di WinGabriel, senza pubblicità, secondo la scelta Riproduzione video nelle altre impostazioni: apri il lettore di WinGabriel (chiudendolo torni alla pagina), riproduci in sottofondo (la pagina resta aperta per leggere titolo, descrizione e commenti) oppure riproduci dalla pagina (il video resta nel lettore del sito); quelli in formati che il browser non supporta passano da soli al lettore di WinGabriel, che ha tutti i codec. Control Shift V riproduce nel lettore di WinGabriel il video della pagina, Control Shift Y lo riproduce invece con il lettore della pagina. Entrambe le cose si possono spegnere dalle altre impostazioni del browser. Per i contenuti protetti il browser usa PlayReady, la protezione di Microsoft; Widevine di Google non è disponibile nel motore WebView2, quindi i servizi che accettano solo Widevine o solo i browser certificati possono rifiutare la riproduzione. PlayReady funziona con il motore WebView2 installato in Windows, non con la sua versione fissa copiata nella cartella di WinGabriel." & vbCrLf &
        "Riunioni online come Meet, Teams o Zoom: microfono e fotocamera si consentono dal menu che compare. Quando si condivide lo schermo, un elenco di WinGabriel chiede cosa mostrare: lo schermo intero oppure, con un motore abbastanza recente, una delle finestre aperte, indicate per titolo; Esc non condivide nulla. La finestra di Windows per scegliere cosa condividere non viene mostrata: il browser sceglie da solo quello indicato nell'elenco. Per un attimo il titolo della finestra condivisa riporta la dicitura condivisa da WinGabriel, poi torna com'era. Anche la barra di Windows che avvisa della condivisione in corso viene nascosta e il fuoco torna a WinGabriel. Per interrompere: Alt, Strumenti della pagina, Interrompi la condivisione dello schermo, oppure il comando della riunione. Si sente quando la condivisione inizia e quando termina. Le finestre che le riunioni aprono restano nelle schede del browser, e quelle di tipo immagine nell'immagine non vengono aperte, nemmeno dai comandi dei video." & vbCrLf &
        "Scelta dei colori: quando una pagina chiede un colore, invece della tavolozza di Windows compare un menu di WinGabriel con i colori per nome, il colore attuale e la possibilità di scrivere il codice esadecimale, per esempio #1A73E8." & vbCrLf &
        "Le applicazioni Flutter vengono rese accessibili da sole. Le aree disegnate e le immagini senza descrizione si possono leggere con il riconoscimento del testo o descrivere con l'intelligenza artificiale."

    Private Const TestoGuidaLettore As String =
        "Guida del messaggio." & vbCrLf & vbCrLf &
        "Il messaggio si legge come una pagina del browser di WinGabriel." & vbCrLf &
        "Frecce su e giù: riga precedente e successiva. Frecce destra e sinistra: carattere per carattere. Control con le frecce orizzontali: parola per parola. Home e Fine, oppure Control Home e Control Fine: inizio e fine. Pagina su e Pagina giù: quindici righe alla volta. Insert o F8: lettura continua." & vbCrLf &
        "Lettere di navigazione rapida, con Shift per tornare indietro: H intestazione, K link, B pulsante, T tabella, L elenco, G immagine, P paragrafo. F7: elenchi degli elementi." & vbCrLf &
        "Tabelle: Control Alt con le frecce si sposta di cella in cella." & vbCrLf &
        "Control F: cerca nel messaggio, F3 cerca ancora. Control Shift U: traduce il messaggio." & vbCrLf &
        "Invio su un link: si apre con il browser scelto nelle impostazioni; i link di posta preparano un nuovo messaggio." & vbCrLf &
        "Le immagini e gli altri contenuti che il messaggio vorrebbe scaricare da internet non vengono caricati: servirebbero soltanto a dire a chi ha scritto che il messaggio è stato aperto." & vbCrLf &
        "Alt: le opzioni, con rispondi, rispondi a tutti, inoltra, allegati, informazioni ed elimina. Esc o Control W: chiude il messaggio."

    Private Sub MostraGuida()
        Try
            _campoGuida = New WGCampo()
            _campoGuida.init(If(_modoLettore, "Guida del messaggio", "Guida del browser"), If(_modoLettore, TestoGuidaLettore, TestoGuida), True, True)
            AddHandler _campoGuida.chiusura, AddressOf GuidaChiusa
            _campoGuida.avvia()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.MostraGuida: " & ex.Message)
        End Try
    End Sub

    Private Sub GuidaChiusa(sender As Object, e As EventArgs)
        Try
            If _campoGuida IsNot Nothing Then _campoGuida.chiudi()
            _campoGuida = Nothing
        Catch ex As Exception
            log.ScriviLog("WGBrowser.GuidaChiusa: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaSessione()
        Try
            If Not WGImpostazioniBrowser.RiapriSchede Then
                WGImpostazioniBrowser.SchedeSalvate = New List(Of String)()
                Return
            End If
            Dim indirizzi As New List(Of String)()
            For Each scheda As WGSchedaBrowser In _schede
                If scheda.Privata Then Continue For
                Dim indirizzoScheda As String = scheda.Indirizzo
                If WGArchivioBrowser.IndirizzoRegistrabile(indirizzoScheda) Then indirizzi.Add(indirizzoScheda)
            Next
            WGImpostazioniBrowser.SchedeSalvate = indirizzi
        Catch ex As Exception
            log.ScriviLog("WGBrowser.SalvaSessione: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            If _chiuso OrElse _chiusuraInCorso Then Return
            Dim inCorso As Integer = _registroDownload.DownloadInCorso().Count
            If inCorso > 0 AndAlso Not _chiusuraConfermata Then
                Dim domanda As New WGDomanda()
                AddHandler domanda.rispostadomanda, Sub(risposta As DialogResult)
                                                        Try
                                                            If risposta <> DialogResult.Yes Then Return
                                                            _chiusuraConfermata = True
                                                            _registroDownload.AnnullaTutti()
                                                            SuUI(AddressOf Chiudi)
                                                        Catch exRisposta As Exception
                                                            log.ScriviLog("WGBrowser.Chiudi risposta: " & exRisposta.Message)
                                                        End Try
                                                    End Sub
                domanda.avvia(If(inCorso = 1, "C'è un download in corso", "Ci sono " & inCorso & " download in corso") & ": chiudendo il browser verrà interrotto. Chiudere comunque?")
                Return
            End If
            _chiusuraInCorso = True
            If Not _modoLettore Then SalvaSessione()
            timerSospensione.Stop()
            codaTasti.Clear()
            ChiudiDopoSalvataggio()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Chiudi: " & ex.ToString())
            CompletaChiusura()
        End Try
    End Sub

    Private Async Sub ChiudiDopoSalvataggio()
        Try
            If Not _modoLettore Then
                Dim salvataggio As Task(Of Boolean) = WGAccessiSiti.SalvaAsync(Me)
                Await Task.WhenAny(salvataggio, Task.Delay(4000))
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiudiDopoSalvataggio: " & ex.Message)
        End Try
        CompletaChiusura()
    End Sub

    Private Sub CompletaChiusura()
        Try
            If _chiuso Then Return
            _chiuso = True
            _chiusuraInCorso = False
            If _istanzaAperta Is Me Then _istanzaAperta = Nothing
            timerSospensione.Stop()
            codaTasti.Clear()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then contenitore.RimuoviControllo(Me)
            Catch exContenitore As Exception
                log.ScriviLog("WGBrowser.CompletaChiusura contenitore: " & exContenitore.Message)
            End Try
            ChiudiTutteLeSchede()
            ChiudiSessionePrivata()
            RaiseEvent chiusura(Me, EventArgs.Empty)
            If _modoLettore Then ChiusuraLettoreCompletata()
            Me.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.CompletaChiusura: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiusuraLettoreCompletata()
        Try
            Dim avviso As Action = _chiusuraLettore
            _chiusuraLettore = Nothing
            Try
                If _fileLettore <> "" AndAlso File.Exists(_fileLettore) Then File.Delete(_fileLettore)
            Catch exFile As Exception
                log.ScriviLog("WGBrowser.ChiusuraLettoreCompletata file: " & exFile.Message)
            End Try
            If avviso IsNot Nothing Then avviso()
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiusuraLettoreCompletata: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiTutteLeSchede()
        Try
            For Each scheda As WGSchedaBrowser In _schede.ToList()
                Try
                    scheda.Chiudi()
                Catch exScheda As Exception
                    log.ScriviLog("WGBrowser.ChiudiTutteLeSchede scheda: " & exScheda.Message)
                End Try
            Next
            _schede.Clear()
            For Each scheda As WGSchedaBrowser In _inAttesaDownload.ToList()
                Try
                    scheda.Chiudi()
                Catch exScheda As Exception
                    log.ScriviLog("WGBrowser.ChiudiTutteLeSchede attesa: " & exScheda.Message)
                End Try
            Next
            _inAttesaDownload.Clear()
            _attiva = Nothing
        Catch ex As Exception
            log.ScriviLog("WGBrowser.ChiudiTutteLeSchede: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                _chiuso = True
                If _istanzaAperta Is Me Then _istanzaAperta = Nothing
                RemoveHandler WGImpostazioniBrowser.LinguaPagineCambiata, AddressOf LinguaPagineCambiata
                RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
                If _moduloOspite IsNot Nothing Then
                    RemoveHandler _moduloOspite.Move, AddressOf ModuloOspiteSpostato
                    _moduloOspite = Nothing
                End If
                tastiera = Nothing
                FermaTouch()
                timerSospensione.Stop()
                timerSospensione.Dispose()
                ChiudiTutteLeSchede()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrowser.Dispose: " & ex.Message)
        End Try
        MyBase.Dispose(disposing)
    End Sub
End Class