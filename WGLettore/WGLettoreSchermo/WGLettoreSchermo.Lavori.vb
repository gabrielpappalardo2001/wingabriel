Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports UIAutomationClient

' Automazione rapida, gestione dei ritardi per i tasti, sorveglianza degli eventi di fuoco e monitoraggio di altri lettori di
' schermo attivi.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const TempoMassimoRapido As UInteger = 400UI
    Private Const IntervalloSfondoAttivo As Integer = 25
    Private Const IntervalloSfondoRiposo As Integer = 250

    Private _automazioneRapida As IUIAutomation
    Private _richiestaCacheRapida As IUIAutomationCacheRequest
    Private _walkerRapido As IUIAutomationTreeWalker
    Private _ritardoTasti As RitardoTasti
    Private ReadOnly _svegliaSfondo As New AutoResetEvent(False)
    Private _ultimoEventoArrivo As Integer
    Private _ultimaRaccoltaCom As Integer
    Private _raccoltaComFatta As Boolean
    Private _ultimoControlloLettori As Integer
    Private _controlloLettoriFatto As Boolean
    Private _altriLettoriAnnunciati As String = ""

    Private Sub InizializzaAutomazioneRapida()
        Try
            _automazioneRapida = Nothing
            _richiestaCacheRapida = Nothing
            _walkerRapido = Nothing
            Dim rapida As IUIAutomation = CreaAutomazione()
            If rapida Is Nothing Then Return
            Dim seconda As IUIAutomation2 = TryCast(rapida, IUIAutomation2)
            If seconda IsNot Nothing Then
                seconda.ConnectionTimeout = TempoMassimoRapido
                seconda.TransactionTimeout = TempoMassimoRapido
            End If
            Dim richiesta As IUIAutomationCacheRequest = rapida.CreateCacheRequest()
            For Each idProprieta As Integer In DescrittoreElementi.ProprietaRichieste
                Try
                    richiesta.AddProperty(idProprieta)
                Catch exProprieta As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.InizializzaAutomazioneRapida (" & idProprieta.ToString() & ")", exProprieta)
                End Try
            Next
            Dim camminatore As IUIAutomationTreeWalker = rapida.ControlViewWalker
            _richiestaCacheRapida = richiesta
            _walkerRapido = camminatore
            _automazioneRapida = rapida
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InizializzaAutomazioneRapida: " & ex.Message)
            _automazioneRapida = Nothing
            _richiestaCacheRapida = Nothing
            _walkerRapido = Nothing
        End Try
    End Sub

    Private Sub RilasciaAutomazioneRapida()
        Try
            _automazioneRapida = Nothing
            _richiestaCacheRapida = Nothing
            _walkerRapido = Nothing
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RilasciaAutomazioneRapida: " & ex.Message)
        End Try
    End Sub

    Private Function AutomazioneRapida() As IUIAutomation
        Dim rapida As IUIAutomation = _automazioneRapida
        If rapida IsNot Nothing AndAlso _richiestaCacheRapida IsNot Nothing Then Return rapida
        Return _automazione
    End Function

    Private Function CacheRapida() As IUIAutomationCacheRequest
        Dim richiesta As IUIAutomationCacheRequest = _richiestaCacheRapida
        If _automazioneRapida IsNot Nothing AndAlso richiesta IsNot Nothing Then Return richiesta
        Return _richiestaCache
    End Function

    Private Function CamminatoreRapido() As IUIAutomationTreeWalker
        Dim camminatore As IUIAutomationTreeWalker = _walkerRapido
        If _automazioneRapida IsNot Nothing AndAlso camminatore IsNot Nothing Then Return camminatore
        Return _walkerControllo
    End Function

    Private Function FuocoNotoValido() As IUIAutomationElement
        Try
            Dim ultimo As IUIAutomationElement = _ultimoElemento
            If ultimo Is Nothing OrElse _descrittore Is Nothing Then Return Nothing
            If Not ElementoFoglia(ultimo) Then Return Nothing
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(ultimo))) Then Return Nothing
            If ultimo.CurrentHasKeyboardFocus = 0 Then Return Nothing
            Return ultimo
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FuocoNotoValido", ex)
            Return Nothing
        End Try
    End Function

    Private Function ElementoFoglia(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return False
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloElementoDati,
                     DescrittoreElementi.ControlloVoceMenu, DescrittoreElementi.ControlloPulsante, DescrittoreElementi.ControlloCasellaControllo,
                     DescrittoreElementi.ControlloPulsanteOpzione, DescrittoreElementi.ControlloModifica, DescrittoreElementi.ControlloCollegamento,
                     DescrittoreElementi.ControlloScheda, DescrittoreElementi.ControlloDispositivoScorrimento, DescrittoreElementi.ControlloSelettoreNumerico,
                     DescrittoreElementi.ControlloPulsanteDiviso
                    Return True
            End Select
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ElementoFoglia", ex)
            Return False
        End Try
    End Function

    Private Function FuocoDaEvento(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If Not ElementoFoglia(elemento) Then Return False
            Return elemento.CurrentHasKeyboardFocus <> 0
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FuocoDaEvento", ex)
            Return False
        End Try
    End Function

    Private NotInheritable Class OperazioneRitardata
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _chiave As String
        Private ReadOnly _ritardo As Integer
        Private ReadOnly _azione As Action
        Private ReadOnly _sostituisci As Boolean

        Public Sub New(lettore As WGLettoreSchermo, chiave As String, ritardo As Integer, azione As Action, sostituisci As Boolean)
            _lettore = lettore
            _chiave = If(chiave, "")
            _ritardo = ritardo
            _azione = azione
            _sostituisci = sostituisci
        End Sub

        Public Sub Attendi(stato As Object)
            Try
                If _ritardo > 0 Then Thread.Sleep(_ritardo)
                If _lettore Is Nothing OrElse _azione Is Nothing Then Return
                If Volatile.Read(_lettore._inEsecuzione) = 0 Then Return
                _lettore.EseguiSuOperazioni(_chiave, _azione, _sostituisci)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.OperazioneRitardata.Attendi", ex)
            End Try
        End Sub
    End Class

    Private Sub ProgrammaOperazione(chiave As String, ritardo As Integer, azione As Action, Optional sostituisci As Boolean = False)
        Try
            If azione Is Nothing Then Return
            Dim richiesta As New OperazioneRitardata(Me, chiave, ritardo, azione, sostituisci)
            ThreadPool.QueueUserWorkItem(AddressOf richiesta.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProgrammaOperazione: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaTasto
        Public Evento As EventoLettore
        Public Tick As Integer
        Public Ritardo As Integer
        Public Sequenza As Integer
        Public Navigazione As Boolean
    End Class

    Private NotInheritable Class EsecuzioneTasto
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _richiesta As RichiestaTasto

        Public Sub New(lettore As WGLettoreSchermo, richiesta As RichiestaTasto)
            _lettore = lettore
            _richiesta = richiesta
        End Sub

        Public Sub Esegui()
            Try
                If _lettore Is Nothing OrElse _richiesta Is Nothing Then Return
                If _richiesta.Navigazione AndAlso _richiesta.Sequenza <> Volatile.Read(_lettore._sequenzaTastoNav) AndAlso MillisecondiDa(_richiesta.Tick) > 250L Then Return
                _lettore.ElaboraTasto(_richiesta.Evento)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.EsecuzioneTasto.Esegui", ex)
            End Try
        End Sub
    End Class

    Private NotInheritable Class RitardoTasti
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _coda As New BlockingCollection(Of RichiestaTasto)()
        Private _thread As Thread
        Private _attivo As Integer

        Public Sub New(lettore As WGLettoreSchermo)
            _lettore = lettore
        End Sub

        Public Sub Avvia()
            Try
                If Interlocked.CompareExchange(_attivo, 1, 0) <> 0 Then Return
                _thread = New Thread(AddressOf Ciclo)
                _thread.IsBackground = True
                _thread.Name = "WGLettoreSchermoTasti"
                _thread.Start()
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RitardoTasti.Avvia", ex)
            End Try
        End Sub

        Public Sub Ferma()
            Try
                Interlocked.Exchange(_attivo, 0)
                If Not _coda.IsAddingCompleted Then _coda.CompleteAdding()
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RitardoTasti.Ferma", ex)
            End Try
        End Sub

        Public Function Aggiungi(richiesta As RichiestaTasto) As Boolean
            Try
                If richiesta Is Nothing OrElse Volatile.Read(_attivo) = 0 OrElse _coda.IsAddingCompleted Then Return False
                Return _coda.TryAdd(richiesta)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RitardoTasti.Aggiungi", ex)
                Return False
            End Try
        End Function

        Private Sub Ciclo()
            While Volatile.Read(_attivo) = 1
                Try
                    Dim richiesta As RichiestaTasto = Nothing
                    If Not _coda.TryTake(richiesta, 500) Then Continue While
                    If richiesta Is Nothing Then Continue While
                    Dim restante As Long = CLng(richiesta.Ritardo) - MillisecondiDa(richiesta.Tick)
                    If restante > 0L AndAlso restante <= 100L Then Thread.Sleep(CInt(restante))
                    Dim esecuzione As New EsecuzioneTasto(_lettore, richiesta)
                    _lettore.EseguiSuOperazioni("", AddressOf esecuzione.Esegui)
                Catch ex As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.RitardoTasti.Ciclo", ex)
                End Try
            End While
        End Sub
    End Class

    Private Sub AvviaRitardoTasti()
        Try
            Dim ritardo As New RitardoTasti(Me)
            ritardo.Avvia()
            _ritardoTasti = ritardo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AvviaRitardoTasti: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaRitardoTasti()
        Try
            Dim ritardo As RitardoTasti = _ritardoTasti
            _ritardoTasti = Nothing
            If ritardo IsNot Nothing Then ritardo.Ferma()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FermaRitardoTasti: " & ex.Message)
        End Try
    End Sub

    Private Sub AccodaTasto(evento As EventoLettore)
        Try
            If evento Is Nothing Then Return
            Dim richiesta As New RichiestaTasto()
            richiesta.Evento = evento
            richiesta.Tick = Environment.TickCount
            richiesta.Navigazione = evento.Azione = AzioneTastoLettore.Navigazione
            If richiesta.Navigazione Then richiesta.Sequenza = Interlocked.Increment(_sequenzaTastoNav)
            richiesta.Ritardo = AttesaTasto(evento)
            If EcoRapida(evento) Then evento.EcoDetto = True
            Dim ritardo As RitardoTasti = _ritardoTasti
            If ritardo IsNot Nothing AndAlso ritardo.Aggiungi(richiesta) Then Return
            Dim esecuzione As New EsecuzioneTasto(Me, richiesta)
            EseguiSuOperazioni("", AddressOf esecuzione.Esegui)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaTasto: " & ex.Message)
        End Try
    End Sub

    Private Function EcoRapida(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing OrElse evento.Azione <> AzioneTastoLettore.Modifica OrElse evento.Ctrl OrElse evento.Alt Then Return False
            If Not EcoCaratteri OrElse _campi Is Nothing OrElse _descrittore Is Nothing Then Return False
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() OrElse MenuNativoInCorso() Then Return False
            Dim istantanea As IstantaneaCampo = _istantanea
            Dim ultimo As IUIAutomationElement = _ultimoElemento
            If istantanea Is Nothing OrElse Not istantanea.HaCursore OrElse ultimo Is Nothing Then Return False
            If Not (_descrittore.ECampoScrittura(ultimo) OrElse _descrittore.EditabileConTesto(ultimo)) Then Return False
            Dim frase As String = _campi.EcoTasto(evento.CodiceTasto, evento.Maiuscolo, evento.AltGr, istantanea.Protetto AndAlso Not LeggiPassword)
            If frase = "" Then Return False
            Volatile.Write(_origineUtente, 1)
            Try
                Parla(frase, True)
            Finally
                Volatile.Write(_origineUtente, 0)
            End Try
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EcoRapida: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AttesaTasto(evento As EventoLettore) As Integer
        Try
            Select Case evento.Azione
                Case AzioneTastoLettore.Copia, AzioneTastoLettore.NavigazioneWeb, AzioneTastoLettore.SelezioneVoce
                    Return 0
            End Select
            If MenuNativoInCorso() Then Return 0
            Dim ultimo As IUIAutomationElement = _ultimoElemento
            If ultimo IsNot Nothing AndAlso ElementoWeb(ultimo) Then Return 16
            Return 8
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttesaTasto: " & ex.Message)
            Return 8
        End Try
    End Function

    Private NotInheritable Class RichiestaFuocoDopoTasto
        Private ReadOnly _lettore As WGLettoreSchermo
        Public Prima As IUIAutomationElement
        Public PrimaMenu As Boolean
        Public Elemento As IUIAutomationElement
        Public Ctrl As Boolean
        Public SequenzaTasto As Integer
        Public SequenzaFuoco As Integer
        Public Tentativo As Integer

        Public Sub New(lettore As WGLettoreSchermo)
            _lettore = lettore
        End Sub

        Public Sub Esegui()
            Try
                If _lettore IsNot Nothing Then _lettore.ProvaFuocoDopoTasto(Me)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RichiestaFuocoDopoTasto.Esegui", ex)
            End Try
        End Sub
    End Class

    Private Sub ControllaFuocoDopoTasto(elemento As IUIAutomationElement, ctrl As Boolean)
        Try
            Dim richiesta As New RichiestaFuocoDopoTasto(Me)
            richiesta.Prima = _ultimoElemento
            richiesta.PrimaMenu = _menuNativoAperto AndAlso EVoceMenuNativa(richiesta.Prima)
            richiesta.Elemento = elemento
            richiesta.Ctrl = ctrl
            richiesta.SequenzaTasto = Volatile.Read(_sequenzaTastoNav)
            richiesta.SequenzaFuoco = Volatile.Read(_sequenzaFocus)
            richiesta.Tentativo = 1
            ProvaFuocoDopoTasto(richiesta)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaFuocoDopoTasto: " & ex.Message)
        End Try
    End Sub

    Private Sub ProvaFuocoDopoTasto(richiesta As RichiestaFuocoDopoTasto)
        Try
            If richiesta Is Nothing Then Return
            If richiesta.Tentativo > 1 Then
                If Volatile.Read(_sequenzaTastoNav) <> richiesta.SequenzaTasto Then Return
                If Volatile.Read(_sequenzaFocus) <> richiesta.SequenzaFuoco Then Return
                If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            End If
            Dim vivo As IUIAutomationElement = ElementoConFocus()
            If richiesta.PrimaMenu AndAlso vivo IsNot Nothing AndAlso Not EVoceMenuNativa(vivo) Then Return
            If vivo IsNot Nothing AndAlso Not StessoElemento(vivo, richiesta.Prima) Then
                Dim evento As New EventoLettore()
                evento.Tipo = TipoEventoLettore.Focus
                evento.Elemento = vivo
                evento.Sequenza = Volatile.Read(_sequenzaFocus)
                ElaboraFocus(evento)
                Return
            End If
            If richiesta.Tentativo >= 4 Then
                AnnunciaVoceAlbero(richiesta.Elemento, richiesta.Ctrl)
                Return
            End If
            richiesta.Tentativo += 1
            ProgrammaOperazione("fuocoDopoTasto", 12, AddressOf richiesta.Esegui, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProvaFuocoDopoTasto: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaSelezioneVoce
        Private ReadOnly _lettore As WGLettoreSchermo
        Public Voce As IUIAutomationSelectionItemPattern
        Public Prima As Boolean
        Public Tentativo As Integer

        Public Sub New(lettore As WGLettoreSchermo)
            _lettore = lettore
        End Sub

        Public Sub Esegui()
            Try
                If _lettore IsNot Nothing Then _lettore.ControllaSelezioneVoce(Me)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RichiestaSelezioneVoce.Esegui", ex)
            End Try
        End Sub
    End Class

    Private Sub ControllaSelezioneVoce(richiesta As RichiestaSelezioneVoce)
        Try
            If richiesta Is Nothing OrElse richiesta.Voce Is Nothing Then Return
            richiesta.Tentativo += 1
            Dim adesso As Boolean = richiesta.Voce.CurrentIsSelected <> 0
            If adesso = richiesta.Prima AndAlso richiesta.Tentativo < 6 Then
                ProgrammaOperazione("selezioneVoce", 30, AddressOf richiesta.Esegui, True)
                Return
            End If
            Parla(If(adesso, "selezionato", "deselezionato"), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaSelezioneVoce: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaAvviso
        Private ReadOnly _lettore As WGLettoreSchermo
        Public Elemento As IUIAutomationElement
        Public Tentativo As Integer

        Public Sub New(lettore As WGLettoreSchermo, elemento As IUIAutomationElement)
            _lettore = lettore
            Me.Elemento = elemento
            Tentativo = 1
        End Sub

        Public Sub Esegui()
            Try
                If _lettore IsNot Nothing Then _lettore.ProvaAvviso(Me)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RichiestaAvviso.Esegui", ex)
            End Try
        End Sub
    End Class

    Private Sub ProvaAvviso(richiesta As RichiestaAvviso)
        Try
            If richiesta Is Nothing OrElse richiesta.Elemento Is Nothing Then Return
            If Volatile.Read(_leggiAvvisi) = 0 Then Return
            Dim testo As String = TestoAvviso(richiesta.Elemento)
            If testo = "" Then
                If richiesta.Tentativo >= 3 Then Return
                richiesta.Tentativo += 1
                ProgrammaOperazione("", 150, AddressOf richiesta.Esegui)
                Return
            End If
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(testo, _ultimoAvviso, StringComparison.CurrentCulture) AndAlso MillisecondiDa(_ultimoAvvisoTick) < 4000L Then Return
            _ultimoAvviso = testo
            _ultimoAvvisoTick = adesso
            Parla("Avviso, " & testo, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProvaAvviso: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaCopiaCampoDopo()
        Try
            Dim testo As String = ""
            If _campi IsNot Nothing Then testo = _campi.TestoSelezionato(ElementoConFocus())
            If testo = "" Then
                Parla("Nessuna selezione", True)
                Return
            End If
            Parla("Copiato, " & Accorcia(testo), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaCopiaCampoDopo: " & ex.Message)
        End Try
    End Sub

    Private Function IntervalloSfondo() As Integer
        Try
            If Volatile.Read(_continuaAttiva) = 1 Then Return IntervalloSfondoAttivo
            If MillisecondiDa(Volatile.Read(_ultimoEventoArrivo)) < 2000L Then Return IntervalloSfondoAttivo
            If Volatile.Read(_titoloInAttesa) = 1 Then Return IntervalloSfondoAttivo
            If _sincronizzaFuocoElemento IsNot Nothing Then Return IntervalloSfondoAttivo
            Dim navigazione As NavigazioneWeb = _navigazione
            If Volatile.Read(_tastiVirtuali) = 1 AndAlso navigazione IsNot Nothing AndAlso navigazione.IndiceDaCompletare Then Return IntervalloSfondoAttivo
            SyncLock _bloccoRegioniVive
                If _regioniVivePendenti.Count > 0 Then Return IntervalloSfondoAttivo
            End SyncLock
            If _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.TracciamentoMouse Then Return 100
            Return IntervalloSfondoRiposo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.IntervalloSfondo: " & ex.Message)
            Return IntervalloSfondoAttivo
        End Try
    End Function

    Private Sub SegnaArrivoEvento(evento As EventoLettore)
        Try
            Volatile.Write(_ultimoEventoArrivo, Environment.TickCount)
            If evento Is Nothing Then Return
            If evento.Tipo = TipoEventoLettore.Tasto OrElse evento.Tipo = TipoEventoLettore.Comando Then _svegliaSfondo.Set()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnaArrivoEvento: " & ex.Message)
        End Try
    End Sub

    Private Function EventiRecenti(millisecondi As Integer) As Boolean
        Return MillisecondiDa(Volatile.Read(_ultimoEventoArrivo)) < CLng(millisecondi)
    End Function

    Private Sub RaccogliOggettiCom(primoPiano As IntPtr)
        Try
            If Not FinestraChromium(primoPiano) AndAlso Not EBrowserGecko(primoPiano) Then Return
            Dim trascorso As Long = If(_raccoltaComFatta, MillisecondiDa(_ultimaRaccoltaCom), Long.MaxValue)
            Dim navigazione As NavigazioneWeb = _navigazione
            Dim consigliata As Boolean = navigazione IsNot Nothing AndAlso navigazione.RaccoltaConsigliata
            If Not ((consigliata AndAlso trascorso >= 15000L) OrElse trascorso >= 60000L) Then Return
            If navigazione IsNot Nothing Then navigazione.AzzeraRaccolta()
            _ultimaRaccoltaCom = Environment.TickCount
            _raccoltaComFatta = True
            GC.Collect(2, GCCollectionMode.Forced, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RaccogliOggettiCom: " & ex.Message)
        End Try
    End Sub

    Private Sub SorvegliaAltriLettori()
        Try
            If _controlloLettoriFatto AndAlso MillisecondiDa(_ultimoControlloLettori) < 10000L Then Return
            _ultimoControlloLettori = Environment.TickCount
            _controlloLettoriFatto = True
            Dim attivi As New List(Of String)()
            If ProcessoAttivo("nvda") Then attivi.Add("NVDA")
            If ProcessoAttivo("jfw") Then attivi.Add("JAWS")
            If ProcessoAttivo("Narrator") Then attivi.Add("l'Assistente vocale")
            If ProcessoAttivo("ZoomText") Then attivi.Add("ZoomText")
            Dim elenco As String = String.Join("|", attivi)
            If elenco = _altriLettoriAnnunciati Then Return
            If attivi.Count = 0 Then
                _altriLettoriAnnunciati = ""
                Return
            End If
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            _altriLettoriAnnunciati = elenco
            Parla(FraseAltriLettori(attivi), False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SorvegliaAltriLettori: " & ex.Message)
        End Try
    End Sub

    Private Shared Function FraseAltriLettori(attivi As List(Of String)) As String
        Try
            If attivi Is Nothing OrElse attivi.Count = 0 Then Return ""
            Dim nomi As String
            If attivi.Count = 1 Then
                nomi = attivi(0)
                Return "Attenzione, è attivo anche " & nomi & ". Le voci si sovrappongono."
            End If
            nomi = String.Join(", ", attivi.GetRange(0, attivi.Count - 1)) & " e " & attivi(attivi.Count - 1)
            Return "Attenzione, sono attivi anche " & nomi & ". Le voci si sovrappongono."
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FraseAltriLettori", ex)
            Return ""
        End Try
    End Function

    Private Shared Function ProcessoAttivo(nome As String) As Boolean
        Dim processi As Process() = Nothing
        Try
            processi = Process.GetProcessesByName(nome)
            Return processi IsNot Nothing AndAlso processi.Length > 0
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ProcessoAttivo (" & nome & ")", ex)
            Return False
        Finally
            If processi IsNot Nothing Then
                For Each processo As Process In processi
                    Try
                        processo.Dispose()
                    Catch exRilascio As Exception
                        RegistroErrori.Scrivi("WGLettoreSchermo.ProcessoAttivo (rilascio)", exRilascio)
                    End Try
                Next
            End If
        End Try
    End Function

    Private NotInheritable Class ArrestoThread
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _agganci As AgganciLettore
        Private ReadOnly _lavoro As Thread
        Private ReadOnly _operazioni As CodaOperazioni
        Private ReadOnly _sfondo As Thread

        Public Sub New(lettore As WGLettoreSchermo, agganci As AgganciLettore, lavoro As Thread, operazioni As CodaOperazioni, sfondo As Thread)
            _lettore = lettore
            _agganci = agganci
            _lavoro = lavoro
            _operazioni = operazioni
            _sfondo = sfondo
        End Sub

        Public Sub Esegui()
            Try
                If _lettore IsNot Nothing Then _lettore.AttendiArrestoThread(_agganci, _lavoro, _operazioni, _sfondo)
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.ArrestoThread.Esegui", ex)
            End Try
        End Sub
    End Class

    Private NotInheritable Class RicercaFinestraCommutatore
        Private ReadOnly _lettore As WGLettoreSchermo
        Public Trovata As IntPtr = IntPtr.Zero

        Public Sub New(lettore As WGLettoreSchermo)
            _lettore = lettore
        End Sub

        Public Function Procedura(maniglia As IntPtr, parametro As IntPtr) As Boolean
            Try
                If Not NativoLettore.IsWindowVisible(maniglia) Then Return True
                If _lettore.ClasseCommutatore(_lettore.ClasseFinestra(maniglia)) Then
                    Trovata = maniglia
                    Return False
                End If
                Return True
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RicercaFinestraCommutatore.Procedura", ex)
                Return True
            End Try
        End Function
    End Class

    Private NotInheritable Class RicercaFinestraTitolo
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _titolo As String
        Private ReadOnly _saltaCommutatore As Boolean
        Public Trovata As IntPtr = IntPtr.Zero

        Public Sub New(lettore As WGLettoreSchermo, titolo As String, saltaCommutatore As Boolean)
            _lettore = lettore
            _titolo = titolo
            _saltaCommutatore = saltaCommutatore
        End Sub

        Public Function Procedura(maniglia As IntPtr, parametro As IntPtr) As Boolean
            Try
                If Not NativoLettore.IsWindowVisible(maniglia) Then Return True
                If _saltaCommutatore AndAlso _lettore.ClasseCommutatore(_lettore.ClasseFinestra(maniglia)) Then Return True
                If String.Equals(_lettore.TitoloFinestra(maniglia), _titolo, StringComparison.CurrentCulture) Then
                    Trovata = maniglia
                    Return False
                End If
                Return True
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RicercaFinestraTitolo.Procedura", ex)
                Return True
            End Try
        End Function
    End Class

    Private Sub LeggiVoceCommutatore(elemento As IUIAutomationElement)
        Try
            If VoceCommutatoreValida(elemento) Then
                AnnunciaCommutatore(elemento)
            Else
                LeggiCommutatore(2)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiVoceCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaCommutatoreEvento(elemento As IUIAutomationElement)
        Try
            AnnunciaCommutatore(elemento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaCommutatoreEvento: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiCommutatoreDue()
        Try
            LeggiCommutatore(2)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiCommutatoreDue: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiCommutatoreUtente(generazione As Integer)
        Try
            Volatile.Write(_origineUtente, 1)
            Try
                LeggiCommutatore(3, True, generazione)
            Finally
                Volatile.Write(_origineUtente, 0)
            End Try
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiCommutatoreUtente: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoAltAlModulo()
        Try
            OttieniModuloForegroundApp().AllTastoAlt()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoAltAlModulo: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaTestoModificato(evento As EventoLettore)
        Try
            If evento Is Nothing Then Return
            If TestoAggiornatoNuovo(evento) Then Return
            If TastoRecente(260) Then Return
            If evento.Testo <> "" Then Parla(evento.Testo, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaTestoModificato: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriMenuDaEvento(evento As EventoLettore)
        Try
            If evento Is Nothing Then Return
            If ProvaAprireMenuVirtuale(evento.Elemento) Then
                AnnunciaVoceMenuVirtuale(True)
            Else
                Parla(If(evento.Testo = "", "menu", "menu, " & evento.Testo), True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriMenuDaEvento: " & ex.Message)
        End Try
    End Sub

    Private Sub RicercaDopoUscita(testo As String)
        Try
            AttendiUscitaWinGabriel(900)
            EseguiRicerca(testo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RicercaDopoUscita: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiOcrElemento(elemento As IUIAutomationElement)
        Try
            If _ocr Is Nothing OrElse elemento Is Nothing Then Return
            Dim letto As String = _ocr.LeggiElemento(_automazione, elemento)
            If letto <> "" Then Parla(letto, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiOcrElemento: " & ex.Message)
        End Try
    End Sub

    Private Sub CercaMenuDaBarra(voceBarra As IUIAutomationElement)
        Try
            Dim trovato As IUIAutomationElement = TrovaMenuDaBarra(voceBarra)
            If trovato Is Nothing Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.MenuBarraTrovato
            evento.Elemento = trovato
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CercaMenuDaBarra: " & ex.Message)
        End Try
    End Sub

End Class