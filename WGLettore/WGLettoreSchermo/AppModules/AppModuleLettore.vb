Imports UIAutomationClient

' Classe base per i moduli applicativi specifici che personalizzano il comportamento del lettore per le diverse applicazioni.
Friend MustInherit Class AppModuleLettore

    Private ReadOnly _lettore As WGLettoreSchermo
    Private _anteprimaTick As Integer
    Private _anteprimaChiave As String = ""
    Private _anteprimaElemento As IUIAutomationElement
    Private _ultimaAnteprima As String = ""

    Protected Sub New(lettore As WGLettoreSchermo, nomeApp As String)
        _lettore = lettore
        NomeApp = If(nomeApp, "").Trim().ToLowerInvariant()
    End Sub

    Public ReadOnly Property NomeApp As String

    Public Overridable ReadOnly Property EPredefinito As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Return testoBase
    End Function

    Public Overridable Function AllFocusSoppresso(elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function PrimoPianoIgnorato(finestra As IntPtr) As Boolean
        Return False
    End Function

    Public Overridable Function FocusMsaaIgnorato(finestra As IntPtr) As Boolean
        Return False
    End Function

    Public Overridable Function ReindirizzaFuoco(elemento As IUIAutomationElement) As IUIAutomationElement
        Return Nothing
    End Function

    Public Overridable Function AntenatoDaTacere(elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function AllSelezione(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return ""
            If EApplicazioneBrowser Then Return ""
            Dim descrittore As DescrittoreElementi = Lettore.DescrittoreApp
            If descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloScheda Then Return ""
            If Not descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSelezionato) Then Return ""
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
            If CInt(pid) <> descrittore.IdProcesso(elemento) AndAlso Not Lettore.FinestraContenitoreApp(primoPiano) Then Return ""
            Dim fuoco As IUIAutomationElement = Lettore.ElementoConFocusApp(False)
            If fuoco IsNot Nothing AndAlso Lettore.StessoElementoApp(fuoco, elemento) Then Return ""
            Return descrittore.Descrivi(elemento)
        Catch ex As Exception
            Lettore.ScriviLogApp("AppModuleLettore.AllSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Overridable Function AllNotifica(evento As EventoLettore) As Boolean
        Return False
    End Function

    Public Overridable Function TestoBarraStatoApp(finestra As IntPtr) As String
        Return ""
    End Function

    Public Overridable Function EVoceApp(elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function EElencoApp(elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function AllAvviso(evento As EventoLettore) As Boolean
        Return False
    End Function

    Public Overridable Function AllMenuAperto(evento As EventoLettore) As Boolean
        Return False
    End Function

    Public Overridable Sub AllTastoAlt()
    End Sub

    Public Overridable Function ElementoTasto(elemento As IUIAutomationElement) As IUIAutomationElement
        Return Nothing
    End Function

    Public Overridable Function EventoSoppresso(elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Return False
    End Function

    Public Overridable Function FormatoApp(elemento As IUIAutomationElement) As String
        Return ""
    End Function

    Public Overridable Function RigaApp(elemento As IUIAutomationElement) As String
        Return ""
    End Function

    Public Overridable Function ColonnaApp(elemento As IUIAutomationElement) As String
        Return ""
    End Function

    Public Overridable Sub AllFocusFine(elemento As IUIAutomationElement)
        Try
            _anteprimaTick = 0
            _anteprimaElemento = Nothing
            _anteprimaChiave = ""
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return
            If Not Lettore.DescrittoreApp.VoceDiComposito(elemento) Then Return
            If Lettore.DescrittoreApp.EVoceAlbero(elemento) Then Return
            If Not LetturaRiquadroCollegato.ConRiquadroCollegato(elemento, Lettore.AutomazioneApp, Lettore.DescrittoreApp, Lettore.WalkerControlloApp, Lettore.RichiestaCacheApp) Then Return
            _anteprimaElemento = elemento
            _anteprimaChiave = Lettore.ChiaveElementoApp(elemento)
            _anteprimaTick = Environment.TickCount + 280
        Catch ex As Exception
            Lettore.ScriviLogApp("AppModuleLettore.AllFocusFine: " & ex.Message)
        End Try
    End Sub

    Public Overridable Sub AllBattito()
        Try
            If _anteprimaTick = 0 OrElse Environment.TickCount - _anteprimaTick < 0 Then Return
            _anteprimaTick = 0
            Dim elemento As IUIAutomationElement = _anteprimaElemento
            Dim chiave As String = _anteprimaChiave
            _anteprimaElemento = Nothing
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return
            Dim vivo As IUIAutomationElement = Lettore.ElementoConFocusApp(False)
            If vivo Is Nothing OrElse Lettore.ChiaveElementoApp(vivo) <> chiave Then Return
            Dim testo As String = LetturaRiquadroCollegato.Anteprima(LetturaRiquadroCollegato.TestoDelRiquadro(elemento, Lettore.AutomazioneApp, Lettore.DescrittoreApp, Lettore.WalkerControlloApp, Lettore.RichiestaCacheApp, 700))
            If testo = "" Then Return
            If Lettore.UltimoNomeApp <> "" AndAlso String.Equals(testo, Lettore.UltimoNomeApp, StringComparison.CurrentCultureIgnoreCase) Then Return
            If String.Equals(testo, _ultimaAnteprima, StringComparison.CurrentCulture) AndAlso Environment.TickCount - Lettore.UltimoFocusTickApp < 1500 Then Return
            _ultimaAnteprima = testo
            Lettore.ParlaApp("dettaglio, " & testo, False)
        Catch ex As Exception
            Lettore.ScriviLogApp("AppModuleLettore.AllBattito: " & ex.Message)
        End Try
    End Sub

    Public Overridable Function FraseElencoApp(lista As IUIAutomationElement, frase As String) As String
        Return ""
    End Function

    Public Overridable ReadOnly Property EApplicazioneBrowser As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable Function TitoloPagina(titolo As String) As String
        Return If(titolo, "")
    End Function

    Protected ReadOnly Property Lettore As WGLettoreSchermo
        Get
            Return _lettore
        End Get
    End Property

    Public Shared Function Crea(nomeEseguibile As String, lettore As WGLettoreSchermo) As AppModuleLettore
        Dim nome As String = If(nomeEseguibile, "").Trim().ToLowerInvariant()
        Select Case nome
            Case "explorer"
                Return New ExplorerAppModule(lettore)
            Case "notepad"
                Return New NotepadAppModule(lettore)
            Case "calculatorapp", "calculator"
                Return New CalcolatriceAppModule(lettore, nome)
            Case "thunderbird"
                Return New ThunderbirdAppModule(lettore)
            Case "excel"
                Return New ExcelAppModule(lettore)
            Case "ms-teams", "teams"
                Return New TeamsAppModule(lettore)
            Case "windowsterminal", "wt", "openconsole", "conhost", "cmd", "powershell", "pwsh", "wsl", "wslhost", "putty", "mintty", "wezterm-gui", "alacritty", "bash", "ubuntu"
                Return New TerminaleAppModule(lettore, nome)
            Case "winword", "outlook"
                Return New WordAppModule(lettore, nome)
            Case "powerpnt"
                Return New PowerPointAppModule(lettore)
            Case "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "chromium", "waterfox", "librewolf", "iexplore", "zen", "arc"
                Return New BrowserAppModule(lettore, nome)
            Case Else
                Dim esterno As ModuloEsternoLettore = ModuliEsterni.Trova(nome)
                If esterno IsNot Nothing Then Return New AppModuleEsterno(lettore, nome, esterno)
                Return New AppModulePredefinito(lettore, nome)
        End Select
    End Function

    Private NotInheritable Class AppModulePredefinito
        Inherits AppModuleLettore

        Public Sub New(lettore As WGLettoreSchermo, nomeApp As String)
            MyBase.New(lettore, nomeApp)
        End Sub

        Public Overrides ReadOnly Property EPredefinito As Boolean
            Get
                Return True
            End Get
        End Property
    End Class

End Class