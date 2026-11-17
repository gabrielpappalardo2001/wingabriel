Imports System.IO
Imports System.Threading
Imports WGLettoreSchermo
Imports Lettore = WGLettoreSchermo.WGLettoreSchermo

''' <summary>
''' Il programma del lettore di schermo: icona accanto all'orologio, voce, impostazioni, guida e comandi
''' che arrivano dalla pipe. Sul desktop sicuro non mostra l'icona e non chiude gli altri lettori.
''' </summary>
Friend NotInheritable Class ApplicazioneLettore
    Inherits ApplicationContext

    Private ReadOnly _opzioni As OpzioniAvvio
    Private ReadOnly _senzaPrivilegi As Boolean
    Private ReadOnly _ui As New Control()
    Private _lettore As Lettore
    Private _voce As IVoceLettore
    Private _icona As NotifyIcon
    Private ReadOnly _menuNativo As New MenuNativoLettore()
    Private _server As ControlloLettore
    Private _timer As System.Windows.Forms.Timer
    Private _avvisoDato As Boolean
    Private _finestra As FinestraImpostazioni

    Public Sub New(opzioni As OpzioniAvvio, senzaPrivilegi As Boolean)
        _opzioni = opzioni
        _senzaPrivilegi = senzaPrivilegi
    End Sub

    Public Sub Avvia()
        ScegliConfigurazione()
        PrimoAvvio.PreparaConfigurazione(_opzioni.Sicuro)
        Dim maniglia As IntPtr = _ui.Handle
        If Not _opzioni.Sicuro Then AvvioLettore.ChiudiAltriLettori()

        _voce = SceltaVoce.Crea(_opzioni.Sicuro)
        Dim voce As IVoceLettore = _voce
        VoceLettore.Fornitore = Function() voce

        _lettore = Lettore.GetInstance()
        AddHandler _lettore.MenuRichiesto, Sub() _ui.BeginInvoke(New Action(AddressOf MostraMenuIcona))
        _lettore.PreparaAvvio()
        Applica()
        _lettore.Avvia()
        _lettore.AllineaBlocMaiuscole()
        If _opzioni.Sicuro Then _lettore.AnnunciaAvvio("") Else _lettore.AnnunciaAvvio("Lettore di schermo caricato")

        If Not _opzioni.Sicuro Then
            CreaIcona()
            PrimoAvvio.ScaricaEspeakSeManca(Me)
        End If
        _server = New ControlloLettore(AddressOf GestisciComando)
        _server.Avvia()

        If Not _opzioni.Sicuro Then
            _timer = New System.Windows.Forms.Timer()
            _timer.Interval = 700
            AddHandler _timer.Tick, AddressOf AlTimer
            _timer.Start()
            If _opzioni.Impostazioni Then ApriImpostazioni()
            _ui.BeginInvoke(New Action(Sub()
                                           If Not _opzioni.Avvio Then PrimoAvvio.EseguiSeServe(Me)
                                           AvvioLettore.ChiediIconaDesktop()
                                           AvvioLettore.ControllaInSottofondo(_ui, AddressOf ChiudiTutto)
                                       End Sub))
        End If
    End Sub

    Public Sub Termina()
        Try
            If _timer IsNot Nothing Then _timer.Stop()
            If _server IsNot Nothing Then _server.Ferma()
            If _lettore IsNot Nothing Then _lettore.Ferma()
            Dim gestita As IVoceGestita = TryCast(_voce, IVoceGestita)
            If gestita IsNot Nothing Then gestita.Termina()
            If _icona IsNot Nothing Then
                _icona.Visible = False
                _icona.Dispose()
            End If
        Catch exErrore1 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore1.Message)
        End Try
    End Sub

    Private Sub ScegliConfigurazione()
        If _opzioni.Sicuro Then
            ConfigLettore.ImpostaPercorso(System.IO.Path.Combine(Installazione.CartellaDatiComune(), "config.xml"))
        ElseIf _opzioni.PercorsoConfig <> "" Then
            ConfigLettore.ImpostaPercorso(_opzioni.PercorsoConfig)
        End If
    End Sub

    Private Shared Function Chiave(nome As String) As String
        Return "lettore_schermo_" & nome
    End Function

    Private Shared Function Vero(nome As String, predefinito As Boolean) As Boolean
        Dim testo As String = ConfigLettore.Leggi(Chiave(nome), "").Trim()
        If testo.Equals("true", StringComparison.OrdinalIgnoreCase) OrElse testo = "1" Then Return True
        If testo.Equals("false", StringComparison.OrdinalIgnoreCase) OrElse testo = "0" Then Return False
        Return predefinito
    End Function

    Friend Sub Applica()
        Try
            _lettore.EcoCaratteri = Vero("eco_caratteri", True)
            _lettore.EcoParole = Vero("eco_parole", True)
            _lettore.LeggiPassword = Vero("password", False)
            _lettore.NavigazioneWebAbilitata = Vero("navigazione_web", True)
            _lettore.AnnuncioRegioni = ConfigLettore.Leggi(Chiave("regioni"), "").Trim()
            _lettore.LivelloIntestazioni = Vero("livello_titoli", True)
            _lettore.NavigazioneCiclica = Vero("ciclica", True)
            _lettore.LinkPrimaDelNome = Vero("link_prima", True)
            _lettore.LeggiColonne = Vero("colonne", False)
            _lettore.LeggiAvvisi = Vero("avvisi", True)
            _lettore.AnnunciaPosizioneElenchi = Vero("posizione_elenchi", True)
            Dim tasto As String = ConfigLettore.Leggi(Chiave("tasto"), "").Trim()
            _lettore.TastoModificatore = If(tasto = "", "entrambi", tasto)
            _lettore.SilenzioConWinGabriel = Not _opzioni.Sicuro
            _lettore.RicaricaImpostazioni()
            If Not _opzioni.Sicuro Then Installazione.CopiaImpostazioniPerSchermataDiAccesso()
            Dim gestita As IVoceGestita = TryCast(_voce, IVoceGestita)
            If gestita IsNot Nothing Then gestita.Configura()
        Catch exErrore2 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore2.Message)
        End Try
    End Sub

    Private Sub CreaIcona()
        ' Il menu lo annuncia il programma (voce per voce) e intanto il lettore tace: la voce la dice un sistema solo.
        AddHandler _menuNativo.VoceEvidenziata, AddressOf AnnunciaVoceMenu
        _icona = New NotifyIcon()
        _icona.Icon = SystemIcons.Information
        _icona.Text = "Lettore di schermo"
        AddHandler _icona.MouseUp, Sub(mittente, e) If e.Button = MouseButtons.Right Then _ui.BeginInvoke(New Action(AddressOf MostraMenuIcona))
        AddHandler _icona.DoubleClick, Sub() ApriImpostazioni()
        _icona.Visible = True
    End Sub

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(maniglia As IntPtr) As Boolean
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(maniglia As IntPtr, ByRef pid As UInteger) As UInteger
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(idA As UInteger, idB As UInteger, collega As Boolean) As Boolean
    End Function

    <System.Runtime.InteropServices.DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    Private _ancora As Form
    Private _timerAvvio As System.Threading.Timer

    Private Shared Function TestoVoceInstallazione() As String
        Return If(Installazione.InstallatoInProgrammi(), "Aggiorna l'installazione", "Installazione")
    End Function

    ' Le voci del menu, rifatte a ogni apertura: il testo dell'installazione cambia, e i componenti aggiungono le loro prima di "Componenti aggiuntivi".
    Private Function VociDelMenu() As List(Of VoceMenuNativo)
        Dim voci As New List(Of VoceMenuNativo)()
        voci.Add(New VoceMenuNativo("Guida", Sub() GuidaLettore.Mostra(Nothing)))
        voci.Add(New VoceMenuNativo("Impostazioni", AddressOf ApriImpostazioni))
        voci.Add(New VoceMenuNativo(TestoVoceInstallazione(), AddressOf PrimoAvvio.InstallaODaMenu))
        Try
            For Each voce As VoceMenuComponente In ComponentiAggiuntivi.VociDiMenu()
                voci.Add(New VoceMenuNativo(voce.Testo, voce.Azione))
            Next
            If ComponentiAggiuntivi.Presenti() Then voci.Add(New VoceMenuNativo("Componenti aggiuntivi", AddressOf ApriComponenti))
        Catch exErrore9 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore9.Message)
        End Try
        voci.Add(New VoceMenuNativo("Uscita", AddressOf ChiediUscita))
        Return voci
    End Function

    ' La voce del menu la dice il programma, non il lettore (che intanto tace): all'apertura "Lettore di schermo, menu" e la prima voce.
    Private Sub AnnunciaVoceMenu(testo As String, apertura As Boolean)
        Try
            If _lettore Is Nothing Then Return
            If apertura Then
                _lettore.AnnunciaDalProgramma(If(testo = "", "Lettore di schermo, menu", "Lettore di schermo, menu, " & testo))
            Else
                _lettore.AnnunciaDalProgramma(testo)
            End If
        Catch exErrore4 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore4.Message)
        End Try
    End Sub

    ' La finestrina d'appoggio da cui si apre il menu: si crea alla prima richiesta e poi si riusa. Non va creata all'avvio: appena creata
    ' prende il primo piano e toglie il fuoco al programma dell'utente, e il lettore non avrebbe più niente da leggere.
    Private Sub PreparaAncora()
        If _ancora IsNot Nothing AndAlso Not _ancora.IsDisposed Then Return
        _ancora = New Form()
        _ancora.FormBorderStyle = FormBorderStyle.None
        _ancora.ShowInTaskbar = False
        _ancora.StartPosition = FormStartPosition.Manual
        _ancora.Size = New Size(1, 1)
        _ancora.TopMost = True
        _ancora.Opacity = 0.01
        Dim creata As IntPtr = _ancora.Handle
    End Sub

    Friend Sub MostraMenuIcona()
        If _menuNativo.Aperto Then
            _menuNativo.Chiudi()
            Return
        End If
        Dim scelta As VoceMenuNativo = Nothing
        Try
            PreparaAncora()
            _lettore.AnnuncioProprioAttivo = True
            _ancora.Location = Cursor.Position
            _ancora.Show()
            PortaInPrimoPiano(_ancora)
            scelta = _menuNativo.Mostra(VociDelMenu(), _ancora)
            _ancora.Hide()
        Catch exErrore3 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore3.Message)
        Finally
            ' Chiuso il menu il lettore torna a parlare, così legge la finestra che la voce scelta apre.
            _lettore.AnnuncioProprioAttivo = False
        End Try
        Try
            If scelta IsNot Nothing Then scelta.Azione.Invoke() Else _lettore.RileggiPrimoPiano()
        Catch exErrore6 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore6.Message)
        End Try
    End Sub

    ' Aspetta (poco) che la finestra sia davvero quella in primo piano: solo allora un menu aperto da lei resta aperto.
    Private Shared Sub PortaInPrimoPiano(finestra As Form)
        Dim pid As UInteger = 0UI
        Dim threadAttivo As UInteger = GetWindowThreadProcessId(GetForegroundWindow(), pid)
        Dim threadMio As UInteger = GetCurrentThreadId()
        Dim collegato As Boolean = threadAttivo <> 0UI AndAlso threadAttivo <> threadMio AndAlso AttachThreadInput(threadMio, threadAttivo, True)
        Try
            SetForegroundWindow(finestra.Handle)
            finestra.Activate()
        Finally
            If collegato Then AttachThreadInput(threadMio, threadAttivo, False)
        End Try
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        While GetForegroundWindow() <> finestra.Handle AndAlso orologio.ElapsedMilliseconds < 400
            Application.DoEvents()
            System.Threading.Thread.Sleep(10)
        End While
    End Sub

    ' Cambiata la voce (per esempio eSpeak appena scaricato): si rilegge la configurazione e si sceglie di nuovo il motore.
    Friend Sub RicaricaVoce()
        Try
            _ui.BeginInvoke(New Action(AddressOf Applica))
        Catch exErrore10 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore10.Message)
        End Try
    End Sub

    Friend ReadOnly Property Sospeso As Boolean
        Get
            Return _lettore IsNot Nothing AndAlso _lettore.Sospeso
        End Get
    End Property

    Friend Sub AlternaSospensione()
        Try
            _lettore.Sospeso = Not _lettore.Sospeso
        Catch exErrore4 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore4.Message)
        End Try
    End Sub

    Friend Sub ApriImpostazioni()
        Try
            If _finestra Is Nothing OrElse _finestra.IsDisposed Then
                _finestra = New FinestraImpostazioni(Me)
            End If
            _finestra.Show()
            _finestra.WindowState = FormWindowState.Normal
            _finestra.Activate()
        Catch exErrore5 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore5.Message)
        End Try
    End Sub

    Private Sub ApriComponenti()
        Try
            Using finestra As New FinestraComponenti()
                AddHandler finestra.Shown, Sub() PortaInPrimoPiano(finestra)
                finestra.ShowDialog()
            End Using
        Catch exErrore6 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore6.Message)
        End Try
    End Sub

    ' La schermata dell'uscita: uscire, riavviare oppure riavviare senza componenti aggiuntivi.
    Friend Sub ChiediUscita()
        Try
            Using finestra As New FinestraUscita()
                ' La schermata si annuncia da sé e il lettore intanto tace: la voce la dice un sistema solo, subito.
                finestra.Annuncia = Sub(frase) _lettore.AnnunciaDalProgramma(frase)
                AddHandler finestra.Shown, Sub() PortaInPrimoPiano(finestra)
                _lettore.AnnuncioProprioAttivo = True
                Dim risultato As DialogResult
                Try
                    risultato = finestra.ShowDialog()
                Finally
                    _lettore.AnnuncioProprioAttivo = False
                End Try
                If risultato <> DialogResult.OK Then
                    _lettore.RileggiPrimoPiano()
                    Return
                End If
                Select Case finestra.Scelta
                    Case "riavvio" : Riavvia(False)
                    Case "riavvio-senza" : Riavvia(True)
                    Case Else : ChiudiTutto()
                End Select
            End Using
        Catch exErrore7 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore7.Message)
        End Try
    End Sub

    ' La nuova copia parte subito e aspetta che questa sia uscita (/attendi), poi riprende da capo.
    Private Sub Riavvia(senzaComponenti As Boolean)
        Try
            Dim argomenti As String = ""
            If _opzioni.Sicuro Then argomenti &= " /sicuro"
            If _opzioni.Avvio Then argomenti &= " /avvio"
            If _opzioni.PercorsoConfig <> "" Then argomenti &= " ""/config:" & _opzioni.PercorsoConfig & """"
            If senzaComponenti Then argomenti &= " /senzacomponenti"
            If Not NativoApp.Elevato() Then argomenti &= " /nonelevare"
            argomenti &= " /attendi:" & Process.GetCurrentProcess().Id.ToString()
            Dim info As New ProcessStartInfo(Application.ExecutablePath, argomenti.Trim())
            info.UseShellExecute = False
            info.WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath)
            Process.Start(info)
        Catch exErrore8 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore8.Message)
            Return
        End Try
        ChiudiTutto()
    End Sub

    Friend Sub ChiudiTutto()
        Termina()
        ExitThread()
    End Sub

    Private Sub AlTimer(mittente As Object, e As EventArgs)
        Try
            If _avvisoDato Then
                _timer.Stop()
                Return
            End If
            If _lettore.Sospeso OrElse _lettore.TaceperWinGabriel Then Return
            _avvisoDato = True
            _timer.Stop()
            Dim avviso As String = TestoAvviso()
            If avviso <> "" Then _lettore.Annuncia(avviso)
        Catch exErrore6 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore6.Message)
        End Try
    End Sub

    Private Function TestoAvviso() As String
        If NativoApp.Elevato() Then Return ""
        Dim testo As String = "Lettore di schermo attivo. Non è stato avviato come amministratore: non potrà leggere le applicazioni avviate come amministratore"
        If Installazione.ServizioInstallato() Then
            testo &= "."
        Else
            testo &= " né le schermate del controllo account utente."
        End If
        Return testo
    End Function

    Private Function GestisciComando(comando As String) As String
        Try
            Dim parti() As String = comando.Split(New Char() {" "c}, 2)
            Select Case parti(0).ToUpperInvariant()
                Case "STATO"
                    Return "OK|" & If(NativoApp.Elevato(), "amministratore", "utente") & "|" & If(_lettore IsNot Nothing AndAlso _lettore.Sospeso, "sospeso", "attivo")
                Case "IMPOSTAZIONI"
                    _ui.BeginInvoke(New MethodInvoker(AddressOf ApriImpostazioni))
                    Return "OK"
                Case "RICARICA"
                    _ui.BeginInvoke(New MethodInvoker(AddressOf Applica))
                    Return "OK"
                Case "CONFIG"
                    If parti.Length > 1 AndAlso parti(1).Trim() <> "" Then
                        Dim percorso As String = parti(1).Trim()
                        _ui.BeginInvoke(New MethodInvoker(Sub()
                                                              ConfigLettore.ImpostaPercorso(percorso)
                                                              Applica()
                                                          End Sub))
                    End If
                    Return "OK"
                Case "PAUSA"
                    _ui.BeginInvoke(New MethodInvoker(Sub() _lettore.Sospeso = True))
                    Return "OK"
                Case "RIPRENDI"
                    _ui.BeginInvoke(New MethodInvoker(Sub() _lettore.Sospeso = False))
                    Return "OK"
                Case "ESCI"
                    _ui.BeginInvoke(New MethodInvoker(AddressOf ChiudiTutto))
                    Return "OK"
            End Select
        Catch exErrore7 As Exception
            Trace.WriteLine("ApplicazioneLettore: " & exErrore7.Message)
        End Try
        Return "ERRORE"
    End Function

End Class
