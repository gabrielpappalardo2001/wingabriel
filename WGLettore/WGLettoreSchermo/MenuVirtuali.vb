Imports System.Collections.Generic
Imports System.Threading
Imports UIAutomationClient

' Gestione dei menu virtuali, barre dei menu e messaggi di chat con comandi dedicati.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const SchemaAttivazione As Integer = 10000
    Private Const SchemaEspansione As Integer = 10005
    Private Const SchemaVoceSelezione As Integer = 10010
    Private Const SchemaAlternato As Integer = 10015

    Private _menuVirtualeVoci As List(Of IUIAutomationElement)
    Private _menuVirtualeContenitore As IUIAutomationElement
    Private _menuVirtualeIndice As Integer
    Private _menuVirtualeTick As Integer
    Private _menuVirtualeMessaggi As Boolean
    Private _elencoMessaggiChiave As String = ""
    Private _elencoMessaggiNuovo As Boolean

    Private NotInheritable Class RichiestaTastoMenu
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _codice As Integer

        Public Sub New(lettore As WGLettoreSchermo, codice As Integer)
            _lettore = lettore
            _codice = codice
        End Sub

        Public Sub Esegui()
            Try
                _lettore.EseguiTastoMenuVirtuale(_codice)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaTastoMenu.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Friend ReadOnly Property MenuVirtualeAttivo As Boolean
        Get
            Return _menuVirtualeVoci IsNot Nothing AndAlso _menuVirtualeVoci.Count > 0
        End Get
    End Property

    Friend ReadOnly Property MenuVirtualeCatturaTasti As Boolean
        Get
            Return MenuVirtualeAttivo AndAlso Not _menuVirtualeMessaggi
        End Get
    End Property

    Friend Sub ChiudiMenuVirtualeDaTasto()
        Try
            ChiudiMenuVirtuale(False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ChiudiMenuVirtualeDaTasto: " & ex.Message)
        End Try
    End Sub

    Private Function MenuInPagina() As Boolean
        Try
            Return _navAttiva AndAlso _navigazione IsNot Nothing AndAlso EBrowser(NativoLettore.GetForegroundWindow())
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MenuInPagina: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LimiteMenuVirtuale(codice As Integer)
        Try
            If MenuInPagina() Then
                EsciVersoPagina(codice)
                Return
            End If
            If codice = &H26 Then
                _menuVirtualeIndice = _menuVirtualeVoci.Count - 1
            Else
                _menuVirtualeIndice = 0
            End If
            _menuVirtualeTick = Environment.TickCount
            AnnunciaVoceMenuVirtuale()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LimiteMenuVirtuale: " & ex.Message)
        End Try
    End Sub

    Private Sub EsciVersoPagina(codice As Integer)
        Dim voce As IUIAutomationElement = Nothing
        Try
            voce = _menuVirtualeVoci(_menuVirtualeIndice)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EsciVersoPagina (voce): " & ex.Message)
        End Try
        ChiudiMenuVirtuale(False)
        Try
            If _navigazione Is Nothing OrElse Not _navAttiva Then Return
            If voce IsNot Nothing Then
                _navigazione.Sincronizza(voce)
                _navForzataElemento = voce
            End If
            ImpostaModalita(True, False)
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Tasto
            evento.CodiceTasto = codice
            evento.Azione = AzioneTastoLettore.NavigazioneWeb
            ElaboraNavigazioneVirtuale(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EsciVersoPagina: " & ex.Message)
        End Try
    End Sub

    Private Function EseguiAzionePredefinita(voce As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing Then Return False
            Dim azione As IUIAutomationLegacyIAccessiblePattern = TryCast(voce.GetCurrentPattern(10018), IUIAutomationLegacyIAccessiblePattern)
            If azione Is Nothing Then Return False
            azione.DoDefaultAction()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiAzionePredefinita: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ProvaAprireMenuVirtuale(origine As IUIAutomationElement, Optional soloVisibili As Boolean = False) As Boolean
        Try
            If _descrittore Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            If origine Is Nothing Then Return False
            If Not EVirtualePerMenu(origine) Then Return False
            Dim contenitore As IUIAutomationElement = CercaContenitoreMenu(origine)
            If contenitore Is Nothing Then contenitore = CercaMenuNellaFinestra(origine, soloVisibili)
            If contenitore Is Nothing Then Return False
            Dim voci As List(Of IUIAutomationElement) = RaccogliVociMenu(contenitore)
            If voci Is Nothing OrElse voci.Count = 0 Then Return False
            _menuVirtualeContenitore = contenitore
            _menuVirtualeVoci = voci
            _menuVirtualeIndice = IndiceVoceConFuoco(voci, origine)
            _menuVirtualeTick = Environment.TickCount
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProvaAprireMenuVirtuale: " & ex.Message)
            ChiudiMenuVirtuale(False)
            Return False
        End Try
    End Function

    Friend Function ApriMenuVirtualeDaContenitore(contenitore As IUIAutomationElement) As Boolean
        Try
            If _descrittore Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            If contenitore Is Nothing Then Return False
            Dim voci As List(Of IUIAutomationElement) = RaccogliVociMenu(contenitore)
            If voci Is Nothing OrElse voci.Count = 0 Then Return False
            _menuVirtualeContenitore = contenitore
            _menuVirtualeVoci = voci
            _menuVirtualeIndice = IndiceVoceConFuoco(voci, Nothing)
            _menuVirtualeTick = Environment.TickCount
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriMenuVirtualeDaContenitore: " & ex.Message)
            ChiudiMenuVirtuale(False)
            Return False
        End Try
    End Function

    Private Function EVirtualePerMenu(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Select Case RegoleRuoli.NormalizzaFramework(_descrittore.Framework(elemento))
                Case "winform", "win32", "wpf", "mozilla"
                    Return False
            End Select
            If ElementoWeb(elemento) Then Return True
            Select Case _descrittore.Framework(elemento).ToLowerInvariant()
                Case "xaml", "winui", "uwp", "directui"
                    Return False
            End Select
            Return _descrittore.RuoloAria(elemento) <> ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EVirtualePerMenu", exAtteso)
            Return False
        End Try
    End Function

    Private Function CercaContenitoreMenu(origine As IUIAutomationElement) As IUIAutomationElement
        Try
            Dim corrente As IUIAutomationElement = origine
            For salto As Integer = 0 To 7
                If corrente Is Nothing Then Exit For
                If EContenitoreMenu(corrente) Then Return corrente
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCacheMenu)
            Next
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.CercaContenitoreMenu", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function CercaMenuNellaFinestra(origine As IUIAutomationElement, Optional soloVisibili As Boolean = False) As IUIAutomationElement
        Try
            Dim radice As IUIAutomationElement = origine
            For salto As Integer = 0 To 24
                Dim padre As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(radice, _richiestaCacheMenu)
                If padre Is Nothing Then Exit For
                radice = padre
            Next
            Dim daVedere As New Queue(Of IUIAutomationElement)()
            daVedere.Enqueue(radice)
            Dim visti As Integer = 0
            Dim inizio As Integer = Environment.TickCount
            While daVedere.Count > 0 AndAlso visti < 300 AndAlso Environment.TickCount - inizio < 400
                Dim corrente As IUIAutomationElement = daVedere.Dequeue()
                visti += 1
                If soloVisibili AndAlso _descrittore.FuoriSchermo(corrente) Then Continue While
                If EContenitoreMenu(corrente) AndAlso RaccogliVociMenu(corrente).Count > 0 Then Return corrente
                Dim figlio As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(corrente, _richiestaCacheMenu)
                While figlio IsNot Nothing
                    visti += 1
                    daVedere.Enqueue(figlio)
                    figlio = _walkerControllo.GetNextSiblingElementBuildCache(figlio, _richiestaCacheMenu)
                End While
            End While
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.CercaMenuNellaFinestra", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function EContenitoreMenu(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim tipoMenu As Integer = _descrittore.TipoControllo(elemento)
            If tipoMenu = DescrittoreElementi.ControlloMenu OrElse tipoMenu = DescrittoreElementi.ControlloBarraMenu Then Return True
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            If ruolo = "menu" OrElse ruolo = "menubar" Then Return True
            If ruolo = "listbox" OrElse ruolo = "grid" Then Return ContaVociMenuDirette(elemento) > 0
            If tipoMenu = DescrittoreElementi.ControlloRiquadro OrElse tipoMenu = DescrittoreElementi.ControlloGruppo OrElse
               tipoMenu = DescrittoreElementi.ControlloPersonalizzato Then Return ContaVociMenuDirette(elemento) >= 2
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EContenitoreMenu", exAtteso)
            Return False
        End Try
    End Function

    Private Function ContaVociMenuDirette(contenitore As IUIAutomationElement) As Integer
        Dim totale As Integer = 0
        Try
            Dim figlio As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(contenitore, _richiestaCacheMenu)
            While figlio IsNot Nothing AndAlso totale < 40
                If EVoceDiMenu(figlio) Then totale += 1
                figlio = _walkerControllo.GetNextSiblingElementBuildCache(figlio, _richiestaCacheMenu)
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ContaVociMenuDirette", exAtteso)
        End Try
        Return totale
    End Function

    Private Function EVoceDiMenu(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloVoceMenu Then Return True
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            Select Case ruolo
                Case "menuitem", "menuitemcheckbox", "menuitemradio", "option"
                    Return True
            End Select
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EVoceDiMenu", exAtteso)
            Return False
        End Try
    End Function

    Private Function EBarraMenu(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloBarraMenu Then Return True
            Return _descrittore.RuoloAria(elemento) = "menubar"
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EBarraMenu", exAtteso)
            Return False
        End Try
    End Function

    Private Function EVoceBarraMenu(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            If Not EVirtualePerMenu(elemento) Then Return False
            If EBarraMenu(elemento) Then Return True
            If Not EVoceDiMenu(elemento) Then Return False
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 2
                If corrente Is Nothing Then Return False
                If EBarraMenu(corrente) Then Return True
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCacheMenu)
            Next
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EVoceBarraMenu", exAtteso)
            Return False
        End Try
    End Function

    Friend Sub ProgrammaAperturaMenuDaBarra(voceBarra As IUIAutomationElement)
        Try
            If _operazioni Is Nothing OrElse _descrittore Is Nothing OrElse voceBarra Is Nothing Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() OrElse MenuVirtualeAttivo Then Return
            Dim chiamataBarra As New Chiamata(Of IUIAutomationElement)(AddressOf CercaMenuDaBarra, voceBarra)
            _operazioni.Esegui("menuBarra", AddressOf chiamataBarra.Esegui)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProgrammaAperturaMenuDaBarra: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaMenuDaBarra(voceBarra As IUIAutomationElement) As IUIAutomationElement
        Try
            If _descrittore Is Nothing OrElse _walkerControllo Is Nothing OrElse voceBarra Is Nothing Then Return Nothing
            For tentativo As Integer = 1 To 3
                If MenuVirtualeAttivo Then Return Nothing
                Dim sotto As IUIAutomationElement = CercaMenuPopUp(voceBarra, voceBarra)
                If sotto IsNot Nothing Then Return sotto
                Dim radice As IUIAutomationElement = voceBarra
                For salto As Integer = 0 To 24
                    Dim padre As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(radice, _richiestaCacheMenu)
                    If padre Is Nothing Then Exit For
                    radice = padre
                Next
                Dim nellaFinestra As IUIAutomationElement = CercaMenuPopUp(radice, voceBarra)
                If nellaFinestra IsNot Nothing Then Return nellaFinestra
                Thread.Sleep(200)
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TrovaMenuDaBarra: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function CercaMenuPopUp(radice As IUIAutomationElement, voceBarra As IUIAutomationElement) As IUIAutomationElement
        Try
            Dim daVedere As New Queue(Of IUIAutomationElement)()
            daVedere.Enqueue(radice)
            Dim visti As Integer = 0
            While daVedere.Count > 0 AndAlso visti < 300
                Dim corrente As IUIAutomationElement = daVedere.Dequeue()
                visti += 1
                If _descrittore.FuoriSchermo(corrente) Then Continue While
                If EContenitoreMenu(corrente) AndAlso Not EBarraMenu(corrente) AndAlso
                   Not EAntenato(corrente, voceBarra) AndAlso RaccogliVociMenu(corrente).Count > 0 Then Return corrente
                Dim figlio As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(corrente, _richiestaCacheMenu)
                While figlio IsNot Nothing
                    visti += 1
                    daVedere.Enqueue(figlio)
                    figlio = _walkerControllo.GetNextSiblingElementBuildCache(figlio, _richiestaCacheMenu)
                End While
            End While
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.CercaMenuPopUp", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function RaccogliVociMenu(contenitore As IUIAutomationElement) As List(Of IUIAutomationElement)
        Dim voci As New List(Of IUIAutomationElement)()
        Try
            Dim daVedere As New Queue(Of IUIAutomationElement)()
            daVedere.Enqueue(contenitore)
            Dim visti As Integer = 0
            While daVedere.Count > 0 AndAlso voci.Count < 100 AndAlso visti < 600
                Dim corrente As IUIAutomationElement = daVedere.Dequeue()
                visti += 1
                Dim figlio As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(corrente, _richiestaCacheMenu)
                While figlio IsNot Nothing
                    visti += 1
                    If EVoceDiMenu(figlio) Then
                        voci.Add(figlio)
                    ElseIf voci.Count < 100 AndAlso visti < 600 Then
                        daVedere.Enqueue(figlio)
                    End If
                    figlio = _walkerControllo.GetNextSiblingElementBuildCache(figlio, _richiestaCacheMenu)
                End While
            End While
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RaccogliVociMenu: " & ex.Message)
        End Try
        Return voci
    End Function

    Private Function IndiceVoceConFuoco(voci As List(Of IUIAutomationElement), origine As IUIAutomationElement) As Integer
        Try
            For indice As Integer = 0 To voci.Count - 1
                If StessaVoce(origine, voci(indice)) Then Return indice
            Next
            Dim fuoco As IUIAutomationElement = ElementoConFocus(False)
            If fuoco IsNot Nothing Then
                For indice As Integer = 0 To voci.Count - 1
                    If StessaVoce(fuoco, voci(indice)) Then Return indice
                Next
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.IndiceVoceConFuoco", exAtteso)
        End Try
        Return 0
    End Function

    Friend Function TrovaMenuMessaggioTeams(messaggio As IUIAutomationElement) As IUIAutomationElement
        Try
            If _descrittore Is Nothing OrElse _walkerControllo Is Nothing OrElse messaggio Is Nothing Then Return Nothing
            For tentativo As Integer = 1 To 5
                If MenuVirtualeAttivo AndAlso Not _menuVirtualeMessaggi Then Return Nothing
                Dim fuoco As IUIAutomationElement = ElementoConFocus(False)
                If fuoco IsNot Nothing AndAlso EVirtualePerMenu(fuoco) AndAlso (EVoceDiMenu(fuoco) OrElse EContenitoreMenu(fuoco)) Then
                    Dim dentro As IUIAutomationElement = CercaContenitoreMenu(fuoco)
                    If dentro Is Nothing Then dentro = CercaMenuNellaFinestra(fuoco, True)
                    If dentro IsNot Nothing AndAlso MenuVisibile(dentro, fuoco) Then Return dentro
                End If
                Dim nellaFinestra As IUIAutomationElement = CercaMenuNellaFinestra(messaggio, True)
                If nellaFinestra IsNot Nothing AndAlso MenuVisibile(nellaFinestra, fuoco) Then Return nellaFinestra
                Thread.Sleep(250)
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TrovaMenuMessaggioTeams: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function MenuVisibile(contenitore As IUIAutomationElement, fuoco As IUIAutomationElement) As Boolean
        Try
            If contenitore Is Nothing Then Return False
            If fuoco IsNot Nothing AndAlso (StessaVoce(fuoco, contenitore) OrElse EAntenato(contenitore, fuoco)) Then Return True
            Return Not _descrittore.FuoriSchermo(contenitore)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.MenuVisibile", exAtteso)
            Return False
        End Try
    End Function

    Private Sub AnnunciaVoceMenuVirtuale(Optional conTesta As Boolean = False)
        Try
            If Not MenuVirtualeAttivo Then Return
            Dim voce As IUIAutomationElement = _menuVirtualeVoci(_menuVirtualeIndice)
            Dim nome As String = _descrittore.Nome(voce)
            Dim parti As New List(Of String)()
            If _menuVirtualeMessaggi Then
                If conTesta Then
                    Dim nomeElenco As String = _descrittore.Nome(_menuVirtualeContenitore)
                    parti.Add(If(nomeElenco = "", "elenco", nomeElenco & ", elenco"))
                End If
                If nome <> "" Then parti.Add(nome)
                Dim posizioneAria As Integer = _descrittore.LeggiIntero(voce, DescrittoreElementi.ProprietaPosizione, 0)
                Dim totaleAria As Integer = _descrittore.LeggiIntero(voce, DescrittoreElementi.ProprietaTotale, 0)
                If posizioneAria > 0 AndAlso totaleAria > 0 Then
                    parti.Add(posizioneAria.ToString() & " di " & totaleAria.ToString())
                Else
                    parti.Add((_menuVirtualeIndice + 1).ToString() & " di " & _menuVirtualeVoci.Count.ToString())
                End If
            Else
                If conTesta Then parti.Add("menu")
                parti.Add(If(nome = "", "voce senza nome", nome))
                Dim stato As String = StatoVoceMenu(voce)
                If stato <> "" Then parti.Add(stato)
                parti.Add((_menuVirtualeIndice + 1).ToString() & " di " & _menuVirtualeVoci.Count.ToString())
            End If
            Dim frase As String = String.Join(", ", parti)
            MemorizzaElemento(voce, nome, _descrittore.ParteVariabile(voce))
            _revisione = voce
            _ultimoFocusTick = Environment.TickCount
            If nome <> "" AndAlso String.Equals(nome, _ultimoMenuMsaaNome, StringComparison.CurrentCultureIgnoreCase) AndAlso MillisecondiDa(Volatile.Read(_ultimoMenuMsaaTick)) < 700L Then Return
            Parla(frase, True)
            If _braille IsNot Nothing AndAlso BrailleAttivo Then _braille.Mostra(nome, 0)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AnnunciaVoceMenuVirtuale", ex)
            ChiudiMenuVirtuale(False)
        End Try
    End Sub

    Private Function StatoVoceMenu(voce As IUIAutomationElement) As String
        Try
            Dim parti As New List(Of String)()
            Dim tipo As Integer = _descrittore.TipoControllo(voce)
            Dim spuntata As String = _descrittore.ValoreAria(voce, "checked")
            If spuntata = "true" Then
                parti.Add("spuntata")
            ElseIf spuntata = "mixed" Then
                parti.Add("parziale")
            End If
            If tipo = DescrittoreElementi.ControlloCasellaControllo AndAlso spuntata = "" AndAlso _descrittore.LeggiBooleano(voce, DescrittoreElementi.ProprietaSelezionato) Then parti.Add("spuntata")
            If _descrittore.ValoreAria(voce, "haspopup") <> "" OrElse _descrittore.LeggiIntero(voce, DescrittoreElementi.ProprietaStatoEspansione, 0) = 1 Then parti.Add("sottomenu")
            If Not _descrittore.LeggiBooleano(voce, DescrittoreElementi.ProprietaAbilitato) Then parti.Add("non disponibile")
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.StatoVoceMenu", exAtteso)
            Return ""
        End Try
    End Function

    Friend Sub TastoMenuVirtuale(codice As Integer)
        Try
            If Not MenuVirtualeAttivo Then Return
            Dim richiesta As New RichiestaTastoMenu(Me, codice)
            EseguiSuOperazioni("", AddressOf richiesta.Esegui)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoMenuVirtuale: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiTastoMenuVirtuale(codice As Integer)
        Try
            If Not MenuVirtualeAttivo Then Return
            If MenuVirtualeSuBarra() Then
                Select Case codice
                    Case &H26, &H28, &HD
                        ApriSottomenuVirtuale()
                        Return
                    Case &H25
                        SpostaSullaBarra(-1)
                        Return
                    Case &H27
                        SpostaSullaBarra(1)
                        Return
                End Select
            End If
            If codice = &H25 OrElse codice = &H27 Then
                If _menuVirtualeMessaggi Then
                    RimandaTastoAllApplicazione(codice)
                    Return
                End If
                If codice = &H27 AndAlso HaSottomenu(_menuVirtualeVoci(_menuVirtualeIndice)) Then
                    ApriSottomenuVirtuale()
                    Return
                End If
                ChiudiMenuVirtuale(False)
                RimandaTastoAllApplicazione(codice)
                Return
            End If
            Select Case codice
                Case &H26
                    If _menuVirtualeIndice <= 0 AndAlso Not (_menuVirtualeMessaggi AndAlso AggiornaMessaggiTeams() AndAlso _menuVirtualeIndice > 0) Then
                        LimiteMenuVirtuale(codice)
                        Return
                    End If
                    _menuVirtualeIndice -= 1
                    _menuVirtualeTick = Environment.TickCount
                    AnnunciaVoceMenuVirtuale()
                    PortaFuocoSuMessaggio()
                Case &H28
                    If _menuVirtualeIndice >= _menuVirtualeVoci.Count - 1 AndAlso Not (_menuVirtualeMessaggi AndAlso AggiornaMessaggiTeams() AndAlso _menuVirtualeIndice < _menuVirtualeVoci.Count - 1) Then
                        LimiteMenuVirtuale(codice)
                        Return
                    End If
                    _menuVirtualeIndice += 1
                    _menuVirtualeTick = Environment.TickCount
                    AnnunciaVoceMenuVirtuale()
                    PortaFuocoSuMessaggio()
                Case &H24
                    _menuVirtualeIndice = 0
                    _menuVirtualeTick = Environment.TickCount
                    AnnunciaVoceMenuVirtuale()
                    PortaFuocoSuMessaggio()
                Case &H23
                    _menuVirtualeIndice = _menuVirtualeVoci.Count - 1
                    _menuVirtualeTick = Environment.TickCount
                    AnnunciaVoceMenuVirtuale()
                    PortaFuocoSuMessaggio()
                Case &HD
                    If _menuVirtualeMessaggi Then
                        AttivaMessaggioTeams()
                    ElseIf HaSottomenu(_menuVirtualeVoci(_menuVirtualeIndice)) Then
                        ApriSottomenuVirtuale()
                    Else
                        AttivaVoceMenuVirtuale()
                    End If
            End Select
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiTastoMenuVirtuale: " & ex.Message)
            ChiudiMenuVirtuale(False)
        End Try
    End Sub

    Private Function ProvaAprireElencoMessaggiTeams(origine As IUIAutomationElement) As Boolean
        Try
            If _descrittore Is Nothing OrElse _walkerControllo Is Nothing OrElse origine Is Nothing Then Return False
            If Not DescrittoreElementi.PrimoPianoTeams() Then Return False
            Dim messaggio As IUIAutomationElement = MessaggioTeamsDi(origine)
            If messaggio Is Nothing Then Return False
            Dim contenitore As IUIAutomationElement = ElencoDelMessaggio(messaggio)
            If contenitore Is Nothing Then Return False
            Dim voci As List(Of IUIAutomationElement) = RaccogliMessaggiTeams(contenitore)
            If voci.Count = 0 Then Return False
            Dim indice As Integer = PosizioneMessaggio(voci, messaggio)
            If indice < 0 Then Return False
            Dim chiave As String = ChiaveElemento(contenitore)
            _elencoMessaggiNuovo = chiave = "" OrElse Not String.Equals(chiave, _elencoMessaggiChiave, StringComparison.Ordinal)
            _elencoMessaggiChiave = chiave
            _menuVirtualeContenitore = contenitore
            _menuVirtualeVoci = voci
            _menuVirtualeIndice = indice
            _menuVirtualeTick = Environment.TickCount
            _menuVirtualeMessaggi = True
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProvaAprireElencoMessaggiTeams: " & ex.Message)
            ChiudiMenuVirtuale(False)
            Return False
        End Try
    End Function

    Private Function MessaggioTeamsDi(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            If _descrittore.MessaggioTeams(elemento, _walkerControllo, _richiestaCache) Then Return elemento
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloPulsante, DescrittoreElementi.ControlloPulsanteDiviso, DescrittoreElementi.ControlloCollegamento,
                     DescrittoreElementi.ControlloModifica, DescrittoreElementi.ControlloCasellaControllo, DescrittoreElementi.ControlloCasellaCombinata,
                     DescrittoreElementi.ControlloVoceMenu, DescrittoreElementi.ControlloMenu, DescrittoreElementi.ControlloImmagine
                    Return Nothing
            End Select
            Dim corrente As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(elemento, _richiestaCache)
            For salto As Integer = 1 To 3
                If corrente Is Nothing Then Return Nothing
                If _descrittore.MessaggioTeams(corrente, _walkerControllo, _richiestaCache) Then Return corrente
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MessaggioTeamsDi: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function ElencoDelMessaggio(messaggio As IUIAutomationElement) As IUIAutomationElement
        Try
            Dim corrente As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(messaggio, _richiestaCache)
            For salto As Integer = 0 To 3
                If corrente Is Nothing Then Return Nothing
                If ElencoMessaggi(corrente) Then Return corrente
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElencoDelMessaggio: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function ElencoMessaggi(elemento As IUIAutomationElement) As Boolean
        Try
            Select Case _descrittore.RuoloAria(elemento)
                Case "list", "feed", "listbox"
                    Return True
            End Select
            Return _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloElenco
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElencoMessaggi: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function RaccogliMessaggiTeams(contenitore As IUIAutomationElement) As List(Of IUIAutomationElement)
        Dim voci As New List(Of IUIAutomationElement)()
        Try
            If contenitore Is Nothing OrElse _automazione Is Nothing Then Return voci
            Dim tutti As IUIAutomationCondition = _automazione.CreateTrueCondition()
            Dim figli As IUIAutomationElementArray = contenitore.FindAllBuildCache(TreeScope.TreeScope_Children, tutti, _richiestaCache)
            If figli Is Nothing Then Return voci
            Dim visti As Integer = 0
            For indice As Integer = 0 To figli.Length - 1
                If visti >= 800 OrElse voci.Count >= 400 Then Exit For
                visti += 1
                Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                Dim ruolo As String = _descrittore.RuoloAria(figlio)
                Dim tipo As Integer = _descrittore.TipoControllo(figlio)
                If ruolo = "listitem" OrElse ruolo = "article" OrElse (ruolo = "" AndAlso (tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloGruppo OrElse tipo = DescrittoreElementi.ControlloPersonalizzato)) Then
                    voci.Add(figlio)
                ElseIf ruolo = "presentation" OrElse ruolo = "none" OrElse ruolo = "generic" Then
                    Dim nipoti As IUIAutomationElementArray = figlio.FindAllBuildCache(TreeScope.TreeScope_Children, tutti, _richiestaCache)
                    If nipoti Is Nothing Then Continue For
                    For posizione As Integer = 0 To nipoti.Length - 1
                        If visti >= 800 OrElse voci.Count >= 400 Then Exit For
                        visti += 1
                        Dim nipote As IUIAutomationElement = nipoti.GetElement(posizione)
                        Dim ruoloNipote As String = _descrittore.RuoloAria(nipote)
                        If ruoloNipote = "listitem" OrElse ruoloNipote = "article" Then voci.Add(nipote)
                    Next
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RaccogliMessaggiTeams: " & ex.Message)
        End Try
        Return voci
    End Function

    Private Function PosizioneMessaggio(voci As List(Of IUIAutomationElement), messaggio As IUIAutomationElement) As Integer
        Try
            If voci Is Nothing OrElse messaggio Is Nothing Then Return -1
            For indice As Integer = 0 To voci.Count - 1
                If StessaVoce(messaggio, voci(indice)) Then Return indice
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PosizioneMessaggio: " & ex.Message)
        End Try
        Return -1
    End Function

    Private Function AggiornaMessaggiTeams() As Boolean
        Try
            If Not _menuVirtualeMessaggi OrElse _menuVirtualeContenitore Is Nothing OrElse Not MenuVirtualeAttivo Then Return False
            Dim corrente As IUIAutomationElement = _menuVirtualeVoci(_menuVirtualeIndice)
            Dim voci As List(Of IUIAutomationElement) = RaccogliMessaggiTeams(_menuVirtualeContenitore)
            Dim indice As Integer = PosizioneMessaggio(voci, corrente)
            If indice < 0 Then Return False
            _menuVirtualeVoci = voci
            _menuVirtualeIndice = indice
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiornaMessaggiTeams: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub PortaFuocoSuMessaggio()
        Try
            If Not _menuVirtualeMessaggi OrElse Not MenuVirtualeAttivo Then Return
            Dim voce As IUIAutomationElement = _menuVirtualeVoci(_menuVirtualeIndice)
            _menuVirtualeTick = Environment.TickCount
            voce.SetFocus()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PortaFuocoSuMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaMessaggioTeams()
        Dim voce As IUIAutomationElement = Nothing
        Try
            voce = _menuVirtualeVoci(_menuVirtualeIndice)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttivaMessaggioTeams (voce): " & ex.Message)
        End Try
        ChiudiMenuVirtuale(False)
        If voce Is Nothing Then Return
        Try
            voce.SetFocus()
            Thread.Sleep(40)
            NativoLettore.keybd_event(CByte(&HD), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&HD), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttivaMessaggioTeams: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaVoceMenuVirtuale()
        Dim voce As IUIAutomationElement = Nothing
        Try
            voce = _menuVirtualeVoci(_menuVirtualeIndice)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AttivaVoceMenuVirtuale", exAtteso)
        End Try
        If voce Is Nothing Then
            ChiudiMenuVirtuale(False)
            Return
        End If
        Try
            Dim framework As String = _descrittore.Framework(voce)
            Dim gecko As Boolean = framework = "gecko" OrElse framework = "mozilla" OrElse framework = "firefox"
            If gecko AndAlso EseguiAzionePredefinita(voce) Then Return
            Dim xaml As Boolean = framework = "xaml" OrElse framework = "winui" OrElse framework = "uwp"
            If xaml AndAlso PremiInvioSuVoce(voce) Then Return
            Dim attiva As IUIAutomationInvokePattern = TryCast(voce.GetCurrentPattern(SchemaAttivazione), IUIAutomationInvokePattern)
            If attiva IsNot Nothing Then
                attiva.Invoke()
                Return
            End If
            If EseguiAzionePredefinita(voce) Then Return
            Dim alterna As IUIAutomationTogglePattern = TryCast(voce.GetCurrentPattern(SchemaAlternato), IUIAutomationTogglePattern)
            If alterna IsNot Nothing Then
                alterna.Toggle()
                Return
            End If
            Dim espandi As IUIAutomationExpandCollapsePattern = TryCast(voce.GetCurrentPattern(SchemaEspansione), IUIAutomationExpandCollapsePattern)
            If espandi IsNot Nothing Then
                espandi.Expand()
                Return
            End If
            voce.SetFocus()
            Thread.Sleep(40)
            NativoLettore.keybd_event(CByte(&HD), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&HD), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttivaVoceMenuVirtuale: " & ex.Message)
        Finally
            ChiudiMenuVirtuale(False)
        End Try
    End Sub

    Private Function PremiInvioSuVoce(voce As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing Then Return False
            voce.SetFocus()
            Thread.Sleep(60)
            NativoLettore.keybd_event(CByte(&HD), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&HD), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PremiInvioSuVoce: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function SeguiFuocoMenuVirtuale(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _menuVirtualeMessaggi Then
                Dim messaggio As IUIAutomationElement = MessaggioTeamsDi(elemento)
                If messaggio IsNot Nothing Then
                    Dim posizione As Integer = PosizioneMessaggio(_menuVirtualeVoci, messaggio)
                    If posizione >= 0 Then
                        If posizione <> _menuVirtualeIndice Then
                            _menuVirtualeIndice = posizione
                            _menuVirtualeTick = Environment.TickCount
                            AnnunciaVoceMenuVirtuale()
                        End If
                        Return True
                    End If
                End If
                If Environment.TickCount - _menuVirtualeTick < 300 Then Return True
                ChiudiMenuVirtuale(False)
                Return False
            End If
            For indice As Integer = 0 To _menuVirtualeVoci.Count - 1
                If StessaVoce(elemento, _menuVirtualeVoci(indice)) Then
                    If indice <> _menuVirtualeIndice Then
                        _menuVirtualeIndice = indice
                        _menuVirtualeTick = Environment.TickCount
                        AnnunciaVoceMenuVirtuale()
                    End If
                    Return True
                End If
            Next
            If StessaVoce(elemento, _menuVirtualeContenitore) OrElse EAntenato(elemento, _menuVirtualeContenitore) Then Return True
            If Environment.TickCount - _menuVirtualeTick < 150 Then Return True
            ChiudiMenuVirtuale(False)
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SeguiFuocoMenuVirtuale", exAtteso)
            ChiudiMenuVirtuale(False)
            Return False
        End Try
    End Function

    Private Sub ChiudiMenuVirtuale(annuncia As Boolean)
        _menuVirtualeVoci = Nothing
        _menuVirtualeContenitore = Nothing
        _menuVirtualeIndice = 0
        _menuVirtualeTick = 0
        _menuVirtualeMessaggi = False
    End Sub

    Private Sub ChiudiMenuVirtualeSilenzioso()
        Try
            ChiudiMenuVirtuale(False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ChiudiMenuVirtualeSilenzioso: " & ex.Message)
        End Try
    End Sub

    Private Function MenuVirtualeSuBarra() As Boolean
        Try
            If _menuVirtualeMessaggi OrElse _menuVirtualeContenitore Is Nothing Then Return False
            Return EBarraMenu(_menuVirtualeContenitore)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MenuVirtualeSuBarra: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function HaSottomenu(voce As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing Then Return False
            If _descrittore.ValoreAria(voce, "haspopup") <> "" Then Return True
            Dim espandi As IUIAutomationExpandCollapsePattern = TryCast(voce.GetCurrentPattern(SchemaEspansione), IUIAutomationExpandCollapsePattern)
            If espandi Is Nothing Then Return False
            Return espandi.CurrentExpandCollapseState <> ExpandCollapseState.ExpandCollapseState_LeafNode
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.HaSottomenu: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ApriSottomenuVirtuale()
        Dim voce As IUIAutomationElement = Nothing
        Try
            voce = _menuVirtualeVoci(_menuVirtualeIndice)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriSottomenuVirtuale (voce): " & ex.Message)
        End Try
        ChiudiMenuVirtuale(False)
        If voce Is Nothing Then Return
        Try
            Dim espandi As IUIAutomationExpandCollapsePattern = TryCast(voce.GetCurrentPattern(SchemaEspansione), IUIAutomationExpandCollapsePattern)
            If espandi IsNot Nothing Then
                espandi.Expand()
            Else
                Dim attiva As IUIAutomationInvokePattern = TryCast(voce.GetCurrentPattern(SchemaAttivazione), IUIAutomationInvokePattern)
                If attiva IsNot Nothing Then attiva.Invoke()
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriSottomenuVirtuale: " & ex.Message)
        End Try
        ProgrammaAperturaMenuDaBarra(voce)
    End Sub

    Private Sub RimandaTastoAllApplicazione(codice As Integer)
        Try
            Dim esteso As Integer = 0
            If codice >= &H21 AndAlso codice <= &H28 Then esteso = &H1
            NativoLettore.keybd_event(CByte(codice), 0, esteso, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(codice), 0, esteso Or NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RimandaTastoAllApplicazione: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaSullaBarra(passo As Integer)
        Try
            Dim nuovo As Integer = _menuVirtualeIndice + passo
            If nuovo < 0 OrElse nuovo > _menuVirtualeVoci.Count - 1 Then
                If _codaVoce IsNot Nothing Then _codaVoce.SuonaLimite()
                AnnunciaVoceMenuVirtuale()
                Return
            End If
            _menuVirtualeIndice = nuovo
            _menuVirtualeTick = Environment.TickCount
            AnnunciaVoceMenuVirtuale()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SpostaSullaBarra: " & ex.Message)
        End Try
    End Sub

End Class
