Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Modulo applicativo per la gestione e la lettura ottimizzata delle finestre e delle cartelle di Esplora file.
Friend NotInheritable Class ExplorerAppModule
    Inherits AppModuleLettore

    Private _ultimaVista As IUIAutomationElement
    Private _ultimaCartella As String = ""
    Private _tastoCartellaUsato As Integer
    Private _memoChiave As String = ""
    Private _memoTick As Integer
    Private _memoContenitore As IUIAutomationElement

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "explorer")
    End Sub

    Public Overrides Function EVoceApp(elemento As IUIAutomationElement) As Boolean
        Return EVoceVistaEsploratore(elemento)
    End Function

    Public Overrides Function EElencoApp(elemento As IUIAutomationElement) As Boolean
        Return EElencoVistaEsploratore(elemento)
    End Function

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Dim testo As String = testoBase
        Try
            If EVoceVistaEsploratore(elemento) Then
                testo = ConVistaNuova(ContenitoreVistaEsploratore(elemento), FraseVoceEsploratore(elemento, testoBase), ultimoElemento, True)
            ElseIf EElencoVistaEsploratore(elemento) Then
                testo = ConVistaNuova(elemento, FraseElencoVistaEsploratore(elemento, testoBase), ultimoElemento, False)
            ElseIf PulsanteStart(elemento) Then
                testo = TogliStatoSelezione(testoBase)
            End If
            Return TogliSegniDirezione(testo)
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.AllFocus: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Public Overrides Function AllFocusSoppresso(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            Dim descrittore As DescrittoreElementi = Lettore.DescrittoreApp
            Dim tipo As Integer = descrittore.TipoControllo(elemento)
            Dim classeUia As String = descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse)
            Dim classeFinestra As String = ClasseNativa(ManigliaDi(elemento))
            If tipo = DescrittoreElementi.ControlloFinestra AndAlso classeUia = "MultitaskingViewFrame" Then Return True
            If tipo = DescrittoreElementi.ControlloRiquadro AndAlso classeUia = "Windows.UI.Input.InputSite.WindowClass" Then Return True
            If FinestraDaIgnorare(classeUia) OrElse FinestraDaIgnorare(classeFinestra) Then Return True
            If tipo = DescrittoreElementi.ControlloRiquadro AndAlso (classeUia = "WorkerW" OrElse classeFinestra = "WorkerW") AndAlso descrittore.Nome(elemento) = "" Then Return True
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.AllFocusSoppresso: " & ex.Message)
        End Try
        Return False
    End Function

    Public Overrides Function PrimoPianoIgnorato(finestra As IntPtr) As Boolean
        Try
            Return FinestraDaIgnorare(ClasseNativa(finestra))
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.PrimoPianoIgnorato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Overrides Function FocusMsaaIgnorato(finestra As IntPtr) As Boolean
        Try
            Select Case ClasseNativa(finestra)
                Case "Search Box", "UniversalSearchBand"
                    Return True
            End Select
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.FocusMsaaIgnorato: " & ex.Message)
        End Try
        Return False
    End Function

    Public Overrides Function ReindirizzaFuoco(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.AutomazioneApp Is Nothing Then Return Nothing
            If Lettore.DescrittoreApp.TipoControllo(elemento) <> DescrittoreElementi.ControlloBarraStrumenti Then Return Nothing
            Dim maniglia As IntPtr = ManigliaDi(elemento)
            If ClasseNativa(maniglia) <> "ToolbarWindow32" Then Return Nothing
            If ClasseNativa(NativoLettore.GetAncestor(maniglia, NativoLettore.GA_PARENT)) <> "SysPager" Then Return Nothing
            SpostaMouseFuori(elemento)
            Dim condizione As IUIAutomationCondition = Lettore.AutomazioneApp.CreatePropertyCondition(30008, True)
            Return elemento.FindFirstBuildCache(TreeScope.TreeScope_Children, condizione, Lettore.RichiestaCacheApp)
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.ReindirizzaFuoco: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Overrides Function AntenatoDaTacere(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            If Lettore.DescrittoreApp.TipoControllo(elemento) <> DescrittoreElementi.ControlloBarraAvanzamento Then Return False
            Dim maniglia As IntPtr = ManigliaDi(elemento)
            If ClasseNativa(maniglia) <> "msctls_progress32" Then Return False
            Return ClasseNativa(NativoLettore.GetAncestor(maniglia, NativoLettore.GA_PARENT)) = "Address Band Root"
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.AntenatoDaTacere: " & ex.Message)
            Return False
        End Try
    End Function

    Public Overrides Function AllMenuAperto(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing OrElse evento.Elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            If MenuSenzaNome(evento.Elemento) Then evento.Testo = ""
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.AllMenuAperto: " & ex.Message)
        End Try
        Return False
    End Function

    Private Function ConVistaNuova(vista As IUIAutomationElement, testo As String, ultimoElemento As IUIAutomationElement, segna As Boolean) As String
        Try
            If vista Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.AutomazioneApp Is Nothing Then Return testo
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            Dim classe As String = ClasseNativa(primoPiano)
            Dim desktop As Boolean = classe = "Progman" OrElse classe = "WorkerW"
            If classe <> "CabinetWClass" AndAlso Not desktop Then Return testo
            Dim cartella As String = If(desktop, "", primoPiano.ToString() & "|" & TitoloNativo(primoPiano))
            Dim vistaPrima As IUIAutomationElement = ContenitoreVistaEsploratore(ultimoElemento)
            Dim daFuori As Boolean = vistaPrima Is Nothing OrElse Not StessaVista(vistaPrima, vista)
            If ultimoElemento IsNot Nothing AndAlso StessaVista(ultimoElemento, vista) Then daFuori = False
            Dim nuova As Boolean
            If desktop Then
                nuova = daFuori
            Else
                Dim tickTasto As Integer = 0
                Dim tastoCartella As Boolean = Lettore.TastoCambioCartellaApp(5000, tickTasto) AndAlso tickTasto <> _tastoCartellaUsato
                Dim precedente As IUIAutomationElement = _ultimaVista
                Dim cambiata As Boolean = precedente Is Nothing OrElse cartella <> _ultimaCartella OrElse Not StessaVista(precedente, vista)
                nuova = tastoCartella OrElse cambiata OrElse daFuori
                If segna Then
                    If tastoCartella Then _tastoCartellaUsato = tickTasto
                    _ultimaVista = vista
                    _ultimaCartella = cartella
                End If
            End If
            If Not nuova Then Return testo
            Dim parti As New List(Of String)()
            Dim nome As String = Lettore.DescrittoreApp.Nome(vista)
            If desktop AndAlso nome = "" Then nome = "Desktop"
            If nome <> "" Then parti.Add(nome)
            Dim modo As String = If(desktop, "elenco", ModoVista(vista))
            If modo <> "" Then parti.Add(modo)
            If parti.Count = 0 Then Return testo
            If String.IsNullOrEmpty(testo) Then Return String.Join(", ", parti)
            Return String.Join(", ", parti) & ", " & testo
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.ConVistaNuova: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function TitoloNativo(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim testo As New StringBuilder(512)
            NativoLettore.GetWindowText(finestra, testo, testo.Capacity)
            Return testo.ToString()
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.TitoloNativo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function StessaVista(prima As IUIAutomationElement, seconda As IUIAutomationElement) As Boolean
        Try
            Return Lettore.AutomazioneApp.CompareElements(prima, seconda) <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.StessaVista", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function ModoVista(vista As IUIAutomationElement) As String
        Try
            Dim viste As IUIAutomationMultipleViewPattern = TryCast(vista.GetCurrentPattern(10008), IUIAutomationMultipleViewPattern)
            If viste Is Nothing Then Return ""
            Dim nome As String = viste.GetViewName(viste.CurrentCurrentView)
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            nome = nome.Trim().ToLowerInvariant()
            If nome = "elenco" Then Return "visualizzazione a elenco"
            If nome.StartsWith("visualizzazione", StringComparison.Ordinal) Then Return nome
            Return "visualizzazione " & nome
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.ModoVista", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function FinestraDaIgnorare(classe As String) As Boolean
        Select Case classe
            Case "ForegroundStaging", "LauncherTipWnd", "ApplicationManager_DesktopShellWindow"
                Return True
        End Select
        Return False
    End Function

    Private Function ManigliaDi(elemento As IUIAutomationElement) As IntPtr
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return IntPtr.Zero
            Return New IntPtr(Lettore.DescrittoreApp.LeggiIntero(elemento, DescrittoreElementi.ProprietaManiglia, 0))
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.ManigliaDi: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function ClasseNativa(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim testo As New StringBuilder(256)
            NativoLettore.GetClassName(finestra, testo, testo.Capacity)
            Return testo.ToString()
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.ClasseNativa: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub SpostaMouseFuori(elemento As IUIAutomationElement)
        Try
            Dim rettangolo As tagRECT = elemento.CachedBoundingRectangle
            If rettangolo.right <= rettangolo.left OrElse rettangolo.bottom <= rettangolo.top Then Return
            Dim punto As New NativoLettore.PuntoCursore()
            If Not NativoLettore.GetCursorPos(punto) Then Return
            If punto.X >= rettangolo.left AndAlso punto.X <= rettangolo.right AndAlso punto.Y >= rettangolo.top AndAlso punto.Y <= rettangolo.bottom Then
                NativoLettore.SetCursorPos(0, 0)
            End If
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.SpostaMouseFuori: " & ex.Message)
        End Try
    End Sub

    Private Function PulsanteStart(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            If Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse) = "Start" Then Return True
            Return Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione) = "StartButton"
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.PulsanteStart: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function MenuSenzaNome(elemento As IUIAutomationElement) As Boolean
        Try
            Dim descrittore As DescrittoreElementi = Lettore.DescrittoreApp
            If descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse) = "#32768" Then Return True
            If descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloMenu Then Return False
            Select Case descrittore.Nome(elemento).ToLowerInvariant()
                Case "contesto", "context", "applicazione", "application"
                    Return True
            End Select
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.MenuSenzaNome: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function TogliSegniDirezione(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return testo
        Return testo.Replace(ChrW(&H200E), "").Replace(ChrW(&H200F), "")
    End Function

    Private Shared Function TogliStatoSelezione(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return testo
        Dim parti As String() = testo.Split(New String() {", "}, StringSplitOptions.None)
        Dim tenute As New List(Of String)()
        For Each parte As String In parti
            Dim pulita As String = parte.Trim().ToLowerInvariant()
            If pulita = "selezionato" OrElse pulita = "non selezionato" Then Continue For
            tenute.Add(parte)
        Next
        Return String.Join(", ", tenute)
    End Function

    Private Function FinestraCartelleEsploratore(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloFinestra Then Return False
            Select Case Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse)
                Case "CabinetWClass", "Progman", "WorkerW"
                    Return True
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.FinestraCartelleEsploratore", exAtteso)
        End Try
        Return False
    End Function

    Private Function ContenitoreVistaEsploratore(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return Nothing
            If Lettore.NomeEseguibileApp(CUInt(Lettore.DescrittoreApp.IdProcesso(elemento))) <> "explorer" Then Return Nothing
            Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(elemento)
            Dim voce As Boolean = tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero
            Dim contenitoreStesso As Boolean = Not voce AndAlso (LetturaAlberi.EContenitoreDiVoci(Lettore.DescrittoreApp, elemento) OrElse Lettore.DescrittoreApp.EAlbero(elemento))
            If Not voce AndAlso Not contenitoreStesso Then Return Nothing
            ' Il risultato vale per lo stesso elemento per un attimo: a ogni spostamento la domanda arriva più volte e la risalita costa.
            Dim chiave As String = Lettore.ChiaveElementoApp(elemento)
            If chiave <> "" AndAlso String.Equals(chiave, _memoChiave, StringComparison.Ordinal) AndAlso Environment.TickCount - _memoTick >= 0 AndAlso Environment.TickCount - _memoTick < 400 Then Return _memoContenitore
            Dim esito As IUIAutomationElement = CercaContenitoreVista(elemento, voce)
            _memoChiave = chiave
            _memoTick = Environment.TickCount
            _memoContenitore = esito
            Return esito
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.ContenitoreVistaEsploratore", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Function CercaContenitoreVista(elemento As IUIAutomationElement, voce As Boolean) As IUIAutomationElement
        Try
            Dim interrotta As Boolean = False
            Dim trovato As IUIAutomationElement = RisaliAllaFinestraCartelle(elemento, voce, interrotta)
            If trovato IsNot Nothing Then Return trovato
            ' Appena si entra in una cartella la catena degli antenati può non essere ancora pronta: se il primo piano è una finestra di cartelle
            ' (non il desktop, dove la catena finisce sempre così) si usa il contenitore più vicino, senza aspettare.
            If voce AndAlso interrotta AndAlso String.Equals(ClasseNativa(NativoLettore.GetForegroundWindow()), "CabinetWClass", StringComparison.Ordinal) Then
                Dim contenitore As IUIAutomationElement = Lettore.ContenitoreVociApp(elemento)
                If contenitore Is Nothing Then contenitore = Lettore.WalkerControlloApp.GetParentElementBuildCache(elemento, Lettore.RichiestaCacheApp)
                Return contenitore
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.CercaContenitoreVista", exAtteso)
        End Try
        Return Nothing
    End Function

    ' interrotta: la catena degli antenati si è fermata prima di arrivare a una finestra (di solito perché non è ancora pronta).
    Private Function RisaliAllaFinestraCartelle(elemento As IUIAutomationElement, voce As Boolean, ByRef interrotta As Boolean) As IUIAutomationElement
        interrotta = False
        Try
            Dim corrente As IUIAutomationElement = If(voce, Lettore.WalkerControlloApp.GetParentElementBuildCache(elemento, Lettore.RichiestaCacheApp), elemento)
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then
                    interrotta = True
                    Return Nothing
                End If
                If FinestraCartelleEsploratore(corrente) Then
                    Return If(voce, Lettore.ContenitoreVociApp(elemento), elemento)
                End If
                corrente = Lettore.WalkerControlloApp.GetParentElementBuildCache(corrente, Lettore.RichiestaCacheApp)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.RisaliAllaFinestraCartelle", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Function EVoceVistaEsploratore(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloElementoElenco AndAlso tipo <> DescrittoreElementi.ControlloElementoDati AndAlso tipo <> DescrittoreElementi.ControlloElementoAlbero Then Return False
            Return ContenitoreVistaEsploratore(elemento) IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.EVoceVistaEsploratore", exAtteso)
            Return False
        End Try
    End Function

    Private Function EElencoVistaEsploratore(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloElenco AndAlso tipo <> DescrittoreElementi.ControlloGrigliaDati AndAlso tipo <> DescrittoreElementi.ControlloTabella Then Return False
            Return ContenitoreVistaEsploratore(elemento) IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.EElencoVistaEsploratore", exAtteso)
            Return False
        End Try
    End Function

    Private Function FraseVoceEsploratore(elemento As IUIAutomationElement, testoBase As String) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return testoBase
            Dim parti As New List(Of String)()
            Dim nome As String = Lettore.DescrittoreApp.NomeEsteso(elemento)
            If nome = "" Then nome = testoBase
            If nome <> "" Then parti.Add(nome)
            If Lettore.DescrittoreApp.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSelezioneDisponibile) AndAlso
               Not Lettore.DescrittoreApp.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSelezionato) Then
                parti.Add("non selezionato")
            End If
            Dim tipoVoce As String = Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaTipoVoce)
            If tipoVoce <> "" AndAlso Not String.Equals(tipoVoce, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(tipoVoce.ToLowerInvariant())
            If Lettore.AnnunciaPosizioneElenchiApp Then
                Dim posizione As Integer = Lettore.DescrittoreApp.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
                Dim totale As Integer = Lettore.DescrittoreApp.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
                If posizione <= 0 OrElse totale <= 0 Then Lettore.PosizioneTraFratelliApp(elemento, posizione, totale)
                If posizione > 0 AndAlso totale > 0 Then parti.Add(posizione.ToString() & " di " & totale.ToString())
            End If
            Dim frasi As New List(Of String)()
            For Each parte As String In parti
                If Not String.IsNullOrWhiteSpace(parte) Then frasi.Add(parte.Trim())
            Next
            If frasi.Count = 0 Then Return testoBase
            Return String.Join(", ", frasi)
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.FraseVoceEsploratore: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Private Function FraseElencoVistaEsploratore(elemento As IUIAutomationElement, testoBase As String) As String
        Try
            Dim numero As Integer = ContaVociElenco(elemento)
            Dim parti As New List(Of String)()
            Dim selezionata As String = NomeVoceSelezionataElenco(elemento)
            If selezionata <> "" Then parti.Add(selezionata)
            If numero > 0 Then parti.Add(numero.ToString() & If(numero = 1, " elemento", " elementi"))
            If parti.Count = 0 Then Return testoBase
            Return String.Join(", ", parti)
        Catch ex As Exception
            Lettore.ScriviLogApp("ExplorerAppModule.FraseElencoVistaEsploratore: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Private Function NomeVoceSelezionataElenco(elenco As IUIAutomationElement) As String
        Try
            If elenco Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return ""
            Dim condizione As IUIAutomationCondition = Lettore.AutomazioneApp.CreatePropertyCondition(DescrittoreElementi.ProprietaSelezionato, True)
            Dim selezionati As IUIAutomationElementArray = elenco.FindAllBuildCache(TreeScope.TreeScope_Descendants, condizione, Lettore.RichiestaCacheApp)
            If selezionati Is Nothing OrElse selezionati.Length = 0 Then Return ""
            Dim limite As Integer = Math.Min(selezionati.Length, 5)
            Dim nomi As New List(Of String)()
            For indice As Integer = 0 To limite - 1
                Dim voce As IUIAutomationElement = selezionati.GetElement(indice)
                Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(voce)
                If tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero Then
                    Dim nomeVoce As String = Lettore.DescrittoreApp.Nome(voce)
                    If nomeVoce <> "" Then nomi.Add(nomeVoce)
                End If
            Next
            Return String.Join(", ", nomi)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.NomeVoceSelezionataElenco", exAtteso)
            Return ""
        End Try
    End Function

    Private Function ContaVociElenco(elenco As IUIAutomationElement) As Integer
        Try
            If elenco Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return 0
            Dim numero As Integer = 0
            Dim corrente As IUIAutomationElement = Lettore.WalkerControlloApp.GetFirstChildElementBuildCache(elenco, Lettore.RichiestaCacheApp)
            While corrente IsNot Nothing AndAlso numero < 200
                Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero Then numero += 1
                corrente = Lettore.WalkerControlloApp.GetNextSiblingElementBuildCache(corrente, Lettore.RichiestaCacheApp)
            End While
            Return numero
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ExplorerAppModule.ContaVociElenco", exAtteso)
            Return 0
        End Try
    End Function

End Class