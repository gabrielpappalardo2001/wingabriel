Imports System.Collections.Generic
Imports System.Threading
Imports System.Runtime.InteropServices
Imports UIAutomationClient

' Registrazione e smistamento degli eventi UIA tra cui fuoco, proprietà, struttura, testo e notifiche.
Friend Delegate Sub RicevitoreEventoEsteso(evento As EventoLettore)

Friend NotInheritable Class RegistroEventiUia

    Private Const EventoTestoCambiato As Integer = 20015
    Private Const EventoMenuAperto As Integer = 20003
    Private Const EventoMenuChiuso As Integer = 20007
    Private Const EventoFinestraChiusa As Integer = 20017
    Private Const EventoTooltipAperto As Integer = 20000
    Private Const ProprietaNome As Integer = 30005
    Private Const ProprietaAbilitato As Integer = 30010
    Private Const ProprietaValore As Integer = 30045
    Private Const ProprietaSelezione As Integer = 30079
    Private Const ProprietaEspansione As Integer = 30070
    Private Const ProprietaAttivazione As Integer = 30086
    Private Const ProprietaIntervallo As Integer = 30047

    Private ReadOnly log As New Logger()
    Private ReadOnly _filtro As New FiltroEventi()
    Private ReadOnly _proprieta As Integer() = {ProprietaNome, ProprietaValore, ProprietaAttivazione, ProprietaEspansione, ProprietaAbilitato, ProprietaSelezione, ProprietaIntervallo}
    Private _automazione As IUIAutomation
    Private _cache As IUIAutomationCacheRequest
    Private _accoda As RicevitoreEventoEsteso
    Private _pid As Integer
    Private _notifica As GestoreNotificaUia
    Private _proprietaGestore As GestoreProprietaUia
    Private _struttura As GestoreStrutturaUia
    Private _testo As GestoreTestoUia
    Private _posizione As GestorePosizioneTestoUia
    Private _eventiRadice As GestoreEventoIdUia
    Private _radice As IUIAutomationElement
    Private _finestraRegistrata As IUIAutomationElement
    Private _testoRegistrato As IUIAutomationElement
    Private _fuocoRegistrato As IUIAutomationElement
    Private _fuocoRichiesto As IUIAutomationElement
    Private _fuocoInAttesa As Boolean
    Private _proprietaFuoco As GestoreProprietaUia
    Private _cacheMinima As IUIAutomationCacheRequest
    Private _modificheRegistrate As Integer
    Private _strutturaRegistrata As Boolean
    Private ReadOnly _proprietaFinestra As Integer() = {ProprietaIntervallo}
    Private _menuApertoTick As Integer
    Private _attivi As Boolean
    Private ReadOnly _bloccoRichieste As New Object()
    Private _finestraRichiesta As IUIAutomationElement
    Private _strutturaRichiesta As Boolean
    Private _strutturaVoluta As Boolean
    Private _testoRichiesto As IUIAutomationElement
    Private _finestraInAttesa As Boolean
    Private _testoInAttesa As Boolean
    Private _segnaleRegistrazioni As AutoResetEvent
    Private _threadRegistrazioni As Thread
    Private _registrazioniAttive As Integer

    Public Sub Avvia(automazione As IUIAutomation, cache As IUIAutomationCacheRequest, accoda As RicevitoreEventoEsteso, pidCorrente As Integer)
        Try
            _automazione = automazione
            _cache = cache
            _accoda = accoda
            _pid = pidCorrente
            If automazione Is Nothing Then Return
            _cacheMinima = Nothing
            Try
                Dim minima As IUIAutomationCacheRequest = automazione.CreateCacheRequest()
                minima.AddProperty(30002)
                minima.AddProperty(30020)
                _cacheMinima = minima
            Catch exMinima As Exception
                log.ScriviLog("RegistroEventiUia.Avvia (cache minima): " & exMinima.Message)
                _cacheMinima = Nothing
            End Try
            Dim sesta As IUIAutomation6 = TryCast(automazione, IUIAutomation6)
            If sesta IsNot Nothing Then
                Try
                    sesta.CoalesceEvents = CoalesceEventsOptions.CoalesceEventsOptions_Enabled
                Catch ex As Exception
                    log.ScriviLog("RegistroEventiUia.Avvia (accorpamento): " & ex.Message)
                End Try
            End If
            _radice = automazione.GetRootElementBuildCache(cache)
            _notifica = New GestoreNotificaUia(AddressOf RiceviNotifica)
            _proprietaGestore = New GestoreProprietaUia(AddressOf RiceviProprieta)
            _proprietaFuoco = New GestoreProprietaUia(AddressOf RiceviProprieta)
            _struttura = New GestoreStrutturaUia(AddressOf RiceviStruttura)
            _testo = New GestoreTestoUia(AddressOf RiceviModificaTesto)
            _posizione = New GestorePosizioneTestoUia(AddressOf RiceviPosizione)
            _eventiRadice = New GestoreEventoIdUia(AddressOf RiceviEventoRadice)
            Dim quinta As IUIAutomation5 = TryCast(automazione, IUIAutomation5)
            If quinta IsNot Nothing AndAlso _radice IsNot Nothing Then
                Try
                    quinta.AddNotificationEventHandler(_radice, TreeScope.TreeScope_Subtree, cache, _notifica)
                Catch ex As Exception
                    log.ScriviLog("RegistroEventiUia.Avvia (notifiche): " & ex.Message)
                End Try
            End If
            If _radice IsNot Nothing Then
                AggiungiEvento(EventoMenuAperto, _eventiRadice)
                AggiungiEvento(EventoMenuChiuso, _eventiRadice)
                AggiungiEvento(EventoFinestraChiusa, _eventiRadice)
                AggiungiEvento(EventoTooltipAperto, _eventiRadice)
            End If
            _attivi = True
            AvviaRegistrazioni()
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            _attivi = False
            FermaRegistrazioni()
            TogliFinestra()
            TogliFuoco()
            TogliTesto()
            If _automazione IsNot Nothing Then
                Dim quinta As IUIAutomation5 = TryCast(_automazione, IUIAutomation5)
                If quinta IsNot Nothing AndAlso _radice IsNot Nothing AndAlso _notifica IsNot Nothing Then
                    Try
                        quinta.RemoveNotificationEventHandler(_radice, _notifica)
                    Catch exNotifica As Exception
                        log.ScriviLog("RegistroEventiUia.Ferma (notifiche): " & exNotifica.Message)
                    End Try
                End If
                TogliEvento(EventoMenuAperto, _eventiRadice)
                TogliEvento(EventoMenuChiuso, _eventiRadice)
                TogliEvento(EventoFinestraChiusa, _eventiRadice)
                TogliEvento(EventoTooltipAperto, _eventiRadice)
            End If
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.Ferma: " & ex.Message)
        Finally
            _notifica = Nothing
            _proprietaGestore = Nothing
            _proprietaFuoco = Nothing
            _cacheMinima = Nothing
            _struttura = Nothing
            _testo = Nothing
            _posizione = Nothing
            _eventiRadice = Nothing
            _radice = Nothing
            _automazione = Nothing
            _cache = Nothing
        End Try
    End Sub

    Public Sub AggiornaFinestra(finestra As IUIAutomationElement, Optional conStruttura As Boolean = False)
        Try
            If Not _attivi OrElse finestra Is Nothing Then Return
            SyncLock _bloccoRichieste
                _finestraRichiesta = finestra
                _strutturaRichiesta = conStruttura
                _finestraInAttesa = True
            End SyncLock
            Dim segnale As AutoResetEvent = _segnaleRegistrazioni
            If segnale IsNot Nothing Then segnale.Set()
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.AggiornaFinestra: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaFuoco(elemento As IUIAutomationElement)
        Try
            If Not _attivi OrElse elemento Is Nothing Then Return
            SyncLock _bloccoRichieste
                _fuocoRichiesto = elemento
                _fuocoInAttesa = True
            End SyncLock
            Dim segnale As AutoResetEvent = _segnaleRegistrazioni
            If segnale IsNot Nothing Then segnale.Set()
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.AggiornaFuoco: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaTesto(elemento As IUIAutomationElement)
        Try
            If Not _attivi Then Return
            SyncLock _bloccoRichieste
                _testoRichiesto = elemento
                _testoInAttesa = True
            End SyncLock
            Dim segnale As AutoResetEvent = _segnaleRegistrazioni
            If segnale IsNot Nothing Then segnale.Set()
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.AggiornaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaRegistrazioni()
        Try
            If Interlocked.CompareExchange(_registrazioniAttive, 1, 0) <> 0 Then Return
            _segnaleRegistrazioni = New AutoResetEvent(False)
            _threadRegistrazioni = New Thread(AddressOf CicloRegistrazioni)
            _threadRegistrazioni.IsBackground = True
            _threadRegistrazioni.Name = "WGLettoreSchermoRegistrazioni"
            _threadRegistrazioni.SetApartmentState(ApartmentState.MTA)
            _threadRegistrazioni.Start()
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.AvviaRegistrazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaRegistrazioni()
        Try
            Interlocked.Exchange(_registrazioniAttive, 0)
            Dim segnale As AutoResetEvent = _segnaleRegistrazioni
            If segnale IsNot Nothing Then segnale.Set()
            Dim lavoro As Thread = _threadRegistrazioni
            If lavoro IsNot Nothing Then lavoro.Join(1000)
            _threadRegistrazioni = Nothing
            SyncLock _bloccoRichieste
                _finestraRichiesta = Nothing
                _testoRichiesto = Nothing
                _fuocoRichiesto = Nothing
                _finestraInAttesa = False
                _testoInAttesa = False
                _fuocoInAttesa = False
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.FermaRegistrazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloRegistrazioni()
        While Volatile.Read(_registrazioniAttive) = 1
            Try
                Dim segnale As AutoResetEvent = _segnaleRegistrazioni
                If segnale Is Nothing Then Exit While
                segnale.WaitOne(250)
                If Volatile.Read(_registrazioniAttive) = 0 Then Exit While
                Dim finestra As IUIAutomationElement = Nothing
                Dim testo As IUIAutomationElement = Nothing
                Dim conFinestra As Boolean = False
                Dim strutturaFinestra As Boolean = False
                Dim conTesto As Boolean = False
                Dim fuoco As IUIAutomationElement = Nothing
                Dim conFuoco As Boolean = False
                SyncLock _bloccoRichieste
                    If _fuocoInAttesa Then
                        fuoco = _fuocoRichiesto
                        conFuoco = True
                        _fuocoRichiesto = Nothing
                        _fuocoInAttesa = False
                    End If
                    If _finestraInAttesa Then
                        finestra = _finestraRichiesta
                        strutturaFinestra = _strutturaRichiesta
                        conFinestra = True
                        _finestraRichiesta = Nothing
                        _finestraInAttesa = False
                    End If
                    If _testoInAttesa Then
                        testo = _testoRichiesto
                        conTesto = True
                        _testoRichiesto = Nothing
                        _testoInAttesa = False
                    End If
                End SyncLock
                If conFinestra Then ApplicaFinestra(finestra, strutturaFinestra)
                If conFuoco Then ApplicaFuoco(fuoco)
                If conTesto Then ApplicaTesto(testo)
            Catch ex As Exception
                log.ScriviLog("RegistroEventiUia.CicloRegistrazioni: " & ex.Message)
            End Try
        End While
    End Sub

    Private Sub ApplicaFinestra(finestra As IUIAutomationElement, conStruttura As Boolean)
        Try
            If Not _attivi OrElse _automazione Is Nothing OrElse finestra Is Nothing Then Return
            If Stesso(finestra, _finestraRegistrata) AndAlso conStruttura = _strutturaVoluta Then Return
            TogliFinestra()
            _finestraRegistrata = finestra
            _strutturaVoluta = conStruttura
            Try
                _automazione.AddPropertyChangedEventHandler(finestra, TreeScope.TreeScope_Subtree, CacheEventi(), _proprietaGestore, _proprietaFinestra)
            Catch ex As Exception
                log.ScriviLog("RegistroEventiUia.ApplicaFinestra (proprietà): " & ex.Message)
            End Try
            _strutturaRegistrata = False
            If conStruttura AndAlso Not FinestraChromium(finestra) Then
                Try
                    _automazione.AddStructureChangedEventHandler(finestra, TreeScope.TreeScope_Subtree, CacheEventi(), _struttura)
                    _strutturaRegistrata = True
                Catch ex As Exception
                    log.ScriviLog("RegistroEventiUia.ApplicaFinestra (struttura): " & ex.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.ApplicaFinestra: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaTesto(elemento As IUIAutomationElement)
        Try
            If Not _attivi OrElse _automazione Is Nothing Then Return
            If elemento Is Nothing Then
                TogliTesto()
                Return
            End If
            If Stesso(elemento, _testoRegistrato) Then Return
            TogliTesto()
            _testoRegistrato = elemento
            Try
                _automazione.AddAutomationEventHandler(EventoTestoCambiato, elemento, TreeScope.TreeScope_Element, CacheEventi(), _testo)
            Catch ex As Exception
                log.ScriviLog("RegistroEventiUia.ApplicaTesto (testo): " & ex.Message)
            End Try
            _modificheRegistrate = 0
            Dim terza As IUIAutomation3 = TryCast(_automazione, IUIAutomation3)
            If terza IsNot Nothing Then
                For Each tipoModifica As TextEditChangeType In New TextEditChangeType() {
                    TextEditChangeType.TextEditChangeType_AutoCorrect,
                    TextEditChangeType.TextEditChangeType_Composition,
                    TextEditChangeType.TextEditChangeType_CompositionFinalized,
                    TextEditChangeType.TextEditChangeType_AutoComplete}
                    Try
                        terza.AddTextEditTextChangedEventHandler(elemento, TreeScope.TreeScope_Element, tipoModifica, CacheEventi(), _testo)
                        _modificheRegistrate += 1
                    Catch exModifica As Exception
                        log.ScriviLog("RegistroEventiUia.ApplicaTesto (modifiche): " & exModifica.Message)
                    End Try
                Next
            End If
            Dim sesta As IUIAutomation6 = TryCast(_automazione, IUIAutomation6)
            If sesta IsNot Nothing Then
                Try
                    sesta.AddActiveTextPositionChangedEventHandler(elemento, TreeScope.TreeScope_Subtree, CacheEventi(), _posizione)
                Catch ex As Exception
                    log.ScriviLog("RegistroEventiUia.ApplicaTesto (cursore): " & ex.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.ApplicaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliFinestra()
        Try
            If _automazione Is Nothing OrElse _finestraRegistrata Is Nothing Then Return
            Try
                _automazione.RemovePropertyChangedEventHandler(_finestraRegistrata, _proprietaGestore)
            Catch exProprieta As Exception
                log.ScriviLog("RegistroEventiUia.TogliFinestra (proprietà): " & exProprieta.Message)
            End Try
            If _strutturaRegistrata Then
                Try
                    _automazione.RemoveStructureChangedEventHandler(_finestraRegistrata, _struttura)
                Catch exStruttura As Exception
                    log.ScriviLog("RegistroEventiUia.TogliFinestra (struttura): " & exStruttura.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.TogliFinestra: " & ex.Message)
        Finally
            _finestraRegistrata = Nothing
            _strutturaRegistrata = False
            _strutturaVoluta = False
        End Try
    End Sub

    Private Sub ApplicaFuoco(elemento As IUIAutomationElement)
        Try
            If Not _attivi OrElse _automazione Is Nothing OrElse elemento Is Nothing OrElse _proprietaFuoco Is Nothing Then Return
            If Stesso(elemento, _fuocoRegistrato) Then Return
            TogliFuoco()
            _fuocoRegistrato = elemento
            _automazione.AddPropertyChangedEventHandler(elemento, TreeScope.TreeScope_Element, CacheEventi(), _proprietaFuoco, _proprieta)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.ApplicaFuoco: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliFuoco()
        Try
            If _automazione Is Nothing OrElse _fuocoRegistrato Is Nothing OrElse _proprietaFuoco Is Nothing Then Return
            _automazione.RemovePropertyChangedEventHandler(_fuocoRegistrato, _proprietaFuoco)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.TogliFuoco: " & ex.Message)
        Finally
            _fuocoRegistrato = Nothing
        End Try
    End Sub

    Private Function CacheEventi() As IUIAutomationCacheRequest
        Return If(_cacheMinima, _cache)
    End Function

    Private Function FinestraChromium(finestra As IUIAutomationElement) As Boolean
        Try
            If finestra Is Nothing Then Return False
            Dim maniglia As IntPtr = IntPtr.Zero
            Try
                maniglia = finestra.CachedNativeWindowHandle
            Catch exCache As Exception
                RegistroErrori.Scrivi("RegistroEventiUia.FinestraChromium", exCache)
                maniglia = finestra.CurrentNativeWindowHandle
            End Try
            If maniglia = IntPtr.Zero Then Return False
            Dim classe As New System.Text.StringBuilder(64)
            NativoLettore.GetClassName(maniglia, classe, classe.Capacity)
            Return classe.ToString().StartsWith("Chrome_", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.FinestraChromium: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub TogliTesto()
        Try
            If _automazione Is Nothing OrElse _testoRegistrato Is Nothing Then Return
            Try
                _automazione.RemoveAutomationEventHandler(EventoTestoCambiato, _testoRegistrato, _testo)
            Catch exTesto As Exception
                log.ScriviLog("RegistroEventiUia.TogliTesto (testo): " & exTesto.Message)
            End Try
            Dim terza As IUIAutomation3 = TryCast(_automazione, IUIAutomation3)
            If terza IsNot Nothing AndAlso _testo IsNot Nothing Then
                For giro As Integer = 1 To _modificheRegistrate
                    Try
                        terza.RemoveTextEditTextChangedEventHandler(_testoRegistrato, _testo)
                    Catch exModifica As Exception
                        If giro = 1 Then log.ScriviLog("RegistroEventiUia.TogliTesto (modifiche): " & exModifica.Message)
                        Exit For
                    End Try
                Next
            End If
            Dim sesta As IUIAutomation6 = TryCast(_automazione, IUIAutomation6)
            If sesta IsNot Nothing AndAlso _posizione IsNot Nothing Then
                Try
                    sesta.RemoveActiveTextPositionChangedEventHandler(_testoRegistrato, _posizione)
                Catch exPosizione As Exception
                    log.ScriviLog("RegistroEventiUia.TogliTesto (cursore): " & exPosizione.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.TogliTesto: " & ex.Message)
        Finally
            _testoRegistrato = Nothing
            _modificheRegistrate = 0
        End Try
    End Sub

    Private Sub AggiungiEvento(idEvento As Integer, gestore As IUIAutomationEventHandler)
        Try
            _automazione.AddAutomationEventHandler(idEvento, _radice, TreeScope.TreeScope_Subtree, _cache, gestore)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.AggiungiEvento (" & idEvento.ToString() & "): " & ex.Message)
        End Try
    End Sub

    Private Sub TogliEvento(idEvento As Integer, gestore As IUIAutomationEventHandler)
        Try
            If _automazione Is Nothing OrElse _radice Is Nothing OrElse gestore Is Nothing Then Return
            _automazione.RemoveAutomationEventHandler(idEvento, _radice, gestore)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.TogliEvento (" & idEvento.ToString() & "): " & ex.Message)
        End Try
    End Sub

    Private Function Stesso(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing OrElse _automazione Is Nothing Then Return False
            Return _automazione.CompareElements(primo, secondo) <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("RegistroEventiUia.Stesso", exAtteso)
            Return False
        End Try
    End Function

    Private Function Ignora(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return True
            Dim maniglia As IntPtr = ManigliaInCache(elemento)
            If maniglia <> IntPtr.Zero AndAlso NativoLettore.IsHungAppWindow(maniglia) Then Return True
            Dim pid As Object = Nothing
            Try
                pid = elemento.GetCachedPropertyValue(30002)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("RegistroEventiUia.Ignora", exAtteso)
                pid = elemento.GetCurrentPropertyValue(30002)
            End Try
            If TypeOf pid Is Integer AndAlso CInt(pid) = _pid Then Return True
            Return False
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("RegistroEventiUia.Ignora", exAtteso2)
            Return False
        End Try
    End Function

    Private Function ManigliaInCache(elemento As IUIAutomationElement) As IntPtr
        Try
            Return elemento.CachedNativeWindowHandle
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("RegistroEventiUia.ManigliaInCache", exAtteso)
            Return IntPtr.Zero
        End Try
    End Function

    Private Sub Pubblica(evento As EventoLettore)
        Try
            If evento Is Nothing OrElse _accoda Is Nothing Then Return
            _accoda.Invoke(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.Pubblica: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviNotifica(elemento As IUIAutomationElement, tipo As NotificationKind, elaborazione As NotificationProcessing, testo As String, attivita As String)
        Try
            If Ignora(elemento) Then Return
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim chiave As String = "notifica:" & If(attivita, "") & ":" & testo
            Dim finestra As Integer = 180
            If elaborazione = NotificationProcessing.NotificationProcessing_MostRecent OrElse elaborazione = NotificationProcessing.NotificationProcessing_ImportantMostRecent Then finestra = 80
            If Not _filtro.AccettaTesto(chiave, testo, finestra) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Notifica
            evento.Elemento = elemento
            evento.Testo = testo.Trim()
            evento.IdAttivita = If(attivita, "")
            evento.PrioritaVoce = PrioritaDiNotifica(elaborazione)
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviNotifica: " & ex.Message)
        End Try
    End Sub

    Private Function PrioritaDiNotifica(elaborazione As NotificationProcessing) As Integer
        Select Case elaborazione
            Case NotificationProcessing.NotificationProcessing_ImportantAll, NotificationProcessing.NotificationProcessing_ImportantMostRecent, NotificationProcessing.NotificationProcessing_ImportantCurrentThenMostRecent
                Return 3
            Case NotificationProcessing.NotificationProcessing_MostRecent
                Return 0
            Case Else
                Return 1
        End Select
    End Function

    Private Sub RiceviProprieta(elemento As IUIAutomationElement, idProprieta As Integer, nuovo As Object)
        Try
            If Ignora(elemento) Then Return
            Dim identita As String = "prop:" & idProprieta.ToString() & ":" & Chiave(elemento)
            Dim testo As String = Nothing
            If TypeOf nuovo Is String Then testo = CStr(nuovo)
            If idProprieta = ProprietaSelezione AndAlso TypeOf nuovo Is Boolean Then testo = If(CBool(nuovo), "selezionato", "non selezionato")
            If Not _filtro.AccettaTesto(identita, testo, 120) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.ProprietaCambiata
            evento.Elemento = elemento
            evento.IdProprieta = idProprieta
            evento.Testo = If(testo, "")
            evento.ChiaveSostituzione = identita
            evento.PrioritaVoce = 1
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviProprieta: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviStruttura(elemento As IUIAutomationElement, tipo As StructureChangeType, idRuntime As Array)
        Try
            If Ignora(elemento) Then Return
            Dim identita As String = "struttura:" & Chiave(elemento)
            If Not _filtro.Accetta(identita, 350) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.StrutturaCambiata
            evento.Elemento = elemento
            evento.IdProprieta = CInt(tipo)
            evento.ChiaveSostituzione = identita
            evento.PrioritaVoce = 0
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviStruttura: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviModificaTesto(elemento As IUIAutomationElement, testi As IList(Of String))
        Try
            If Ignora(elemento) Then Return
            Dim unito As String = ""
            If testi IsNot Nothing AndAlso testi.Count > 0 Then unito = String.Join(" ", testi)
            If Not _filtro.AccettaTesto("testo:" & Chiave(elemento), unito, 70) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.TestoModificato
            evento.Elemento = elemento
            evento.Testo = unito
            evento.PrioritaVoce = 1
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviModificaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviPosizione(elemento As IUIAutomationElement)
        Try
            If Ignora(elemento) Then Return
            If Not _filtro.Accetta("cursore:" & Chiave(elemento), 45) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.PosizioneTesto
            evento.Elemento = elemento
            evento.ChiaveSostituzione = "cursore:" & Chiave(elemento)
            evento.PrioritaVoce = 1
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviPosizione: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviEventoRadice(elemento As IUIAutomationElement, idEvento As Integer)
        Try
            If Ignora(elemento) Then Return
            Dim evento As New EventoLettore()
            evento.Elemento = elemento
            evento.Testo = Nome(elemento)
            Select Case idEvento
                Case EventoTooltipAperto
                    If String.IsNullOrWhiteSpace(evento.Testo) Then Return
                    If Not _filtro.AccettaTesto("tooltip:" & evento.Testo, evento.Testo, 12000) Then Return
                    evento.Tipo = TipoEventoLettore.Tooltip
                    evento.PrioritaVoce = 0
                Case EventoMenuAperto
                    If Not _filtro.Accetta("menu-aperto:" & Chiave(elemento), 120) Then Return
                    evento.Tipo = TipoEventoLettore.MenuAperto
                    evento.PrioritaVoce = 2
                    _menuApertoTick = Environment.TickCount
                Case EventoMenuChiuso
                    If Environment.TickCount - _menuApertoTick > 15000 Then Return
                    If Not _filtro.Accetta("menu-chiuso:" & Chiave(elemento), 120) Then Return
                    evento.Tipo = TipoEventoLettore.MenuChiuso
                    evento.PrioritaVoce = 1
                Case EventoFinestraChiusa
                    If String.IsNullOrWhiteSpace(evento.Testo) Then Return
                    If Not _filtro.AccettaTesto("chiusa:" & Chiave(elemento), evento.Testo, 250) Then Return
                    evento.Tipo = TipoEventoLettore.FinestraChiusa
                    evento.PrioritaVoce = 1
                Case Else
                    Return
            End Select
            Pubblica(evento)
        Catch ex As Exception
            log.ScriviLog("RegistroEventiUia.RiceviEventoRadice: " & ex.Message)
        End Try
    End Sub

    Private Function Nome(elemento As IUIAutomationElement) As String
        Try
            Dim valore As Object = elemento.GetCachedPropertyValue(ProprietaNome)
            If TypeOf valore Is String Then Return CStr(valore).Trim()
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("RegistroEventiUia.Nome", exAtteso)
            Return ""
        End Try
    End Function

    Private Function Chiave(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("RegistroEventiUia.Chiave", exAtteso)
            Return ""
        End Try
    End Function

End Class

<ComVisible(True)>
Friend NotInheritable Class GestoreEventoIdUia
    Implements IUIAutomationEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement, Integer)

    Public Sub New(destinazione As Action(Of IUIAutomationElement, Integer))
        _destinazione = destinazione
    End Sub

    Public Sub HandleAutomationEvent(sender As IUIAutomationElement, eventId As Integer) Implements IUIAutomationEventHandler.HandleAutomationEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, eventId)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreEventoIdUia.HandleAutomationEvent", exAtteso)
        End Try
    End Sub
End Class

<ComVisible(True)>
Friend NotInheritable Class GestoreNotificaUia
    Implements IUIAutomationNotificationEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement, NotificationKind, NotificationProcessing, String, String)

    Public Sub New(destinazione As Action(Of IUIAutomationElement, NotificationKind, NotificationProcessing, String, String))
        _destinazione = destinazione
    End Sub

    Public Sub HandleNotificationEvent(sender As IUIAutomationElement, kind As NotificationKind, processing As NotificationProcessing, displayString As String, activityId As String) Implements IUIAutomationNotificationEventHandler.HandleNotificationEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, kind, processing, displayString, activityId)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreNotificaUia.HandleNotificationEvent", exAtteso)
        End Try
    End Sub
End Class

<ComVisible(True)>
Friend NotInheritable Class GestoreProprietaUia
    Implements IUIAutomationPropertyChangedEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement, Integer, Object)

    Public Sub New(destinazione As Action(Of IUIAutomationElement, Integer, Object))
        _destinazione = destinazione
    End Sub

    Public Sub HandlePropertyChangedEvent(sender As IUIAutomationElement, propertyId As Integer, newValue As Object) Implements IUIAutomationPropertyChangedEventHandler.HandlePropertyChangedEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, propertyId, newValue)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreProprietaUia.HandlePropertyChangedEvent", exAtteso)
        End Try
    End Sub
End Class

<ComVisible(True)>
Friend NotInheritable Class GestoreStrutturaUia
    Implements IUIAutomationStructureChangedEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement, StructureChangeType, Array)

    Public Sub New(destinazione As Action(Of IUIAutomationElement, StructureChangeType, Array))
        _destinazione = destinazione
    End Sub

    Public Sub HandleStructureChangedEvent(sender As IUIAutomationElement, changeType As StructureChangeType, runtimeId As Array) Implements IUIAutomationStructureChangedEventHandler.HandleStructureChangedEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, changeType, runtimeId)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreStrutturaUia.HandleStructureChangedEvent", exAtteso)
        End Try
    End Sub
End Class

<ComVisible(True)>
Friend NotInheritable Class GestoreTestoUia
    Implements IUIAutomationEventHandler
    Implements IUIAutomationTextEditTextChangedEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement, IList(Of String))

    Public Sub New(destinazione As Action(Of IUIAutomationElement, IList(Of String)))
        _destinazione = destinazione
    End Sub

    Public Sub HandleAutomationEvent(sender As IUIAutomationElement, eventId As Integer) Implements IUIAutomationEventHandler.HandleAutomationEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, Nothing)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreTestoUia.HandleAutomationEvent", exAtteso)
        End Try
    End Sub

    Public Sub HandleTextEditTextChangedEvent(sender As IUIAutomationElement, textEditChangeType As TextEditChangeType, eventStrings As Array) Implements IUIAutomationTextEditTextChangedEventHandler.HandleTextEditTextChangedEvent
        Try
            Dim testi As New List(Of String)()
            If eventStrings IsNot Nothing Then
                For Each voce As Object In eventStrings
                    If TypeOf voce Is String AndAlso CStr(voce) <> "" Then testi.Add(CStr(voce))
                Next
            End If
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender, testi)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestoreTestoUia.HandleTextEditTextChangedEvent", exAtteso)
        End Try
    End Sub
End Class

<ComVisible(True)>
Friend NotInheritable Class GestorePosizioneTestoUia
    Implements IUIAutomationActiveTextPositionChangedEventHandler

    Private ReadOnly _destinazione As Action(Of IUIAutomationElement)

    Public Sub New(destinazione As Action(Of IUIAutomationElement))
        _destinazione = destinazione
    End Sub

    Public Sub HandleActiveTextPositionChangedEvent(sender As IUIAutomationElement, range As IUIAutomationTextRange) Implements IUIAutomationActiveTextPositionChangedEventHandler.HandleActiveTextPositionChangedEvent
        Try
            ComLettore.Rilascia(range)
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("GestorePosizioneTestoUia.HandleActiveTextPositionChangedEvent", exAtteso)
        End Try
    End Sub
End Class
