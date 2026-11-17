Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Media
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

''' <summary>Un numero di un contatto, con l'etichetta da mostrare (es. "Cellulare").</summary>
Public Class NumeroContatto
    Public Property Etichetta As String = ""
    Public Property Numero As String = ""
End Class

''' <summary>
''' Menu del telefono: Componi, Chiamate recenti, Chiamate perse, piu' il menu della chiamata in
''' corso (attesa / termina). Ogni funzione verifica prima che ci sia un telefono collegato in
''' Bluetooth. Va creata sul thread dell'interfaccia, come MenuRubrica.
''' </summary>
Public Class MenuTelefono
    Implements IDisposable

    ' Posizione degli indicatori in +CIEV. E' l'ordine standard di AT+CIND=? (service, call,
    ' callsetup, callheld, signal, roam, battchg): se un telefono usa un ordine diverso va
    ' letto da AT+CIND=? durante l'handshake.
    Private Const IndiceChiamata As Integer = 2
    Private Const IndiceImpostazione As Integer = 3
    Private Const IndiceInAttesa As Integer = 4
    Private Const NumeroMassimoChiamate As Integer = 50

    Private ReadOnly log As New wgcontrols.Logger()
    Private ReadOnly _telefono As WGTelefono
    Private ReadOnly _telefonoEsterno As Boolean
    Private _marshal As System.Windows.Forms.Control

    Private _menuPrincipale As WGMenu
    Private _campiNumero As Campi
    Private _listaTelefoni As WGMenuLista
    Private _listaNumeri As WGMenuLista
    Private _listaCronologia As WGMenuLista
    Private _listaChiamataInCorso As WGMenuLista
    Private _attesa As WGProcessoInCorso

    Private _chiuso As Boolean = False
    Private _idTelefono As String = ""
    Private _azionePendenteTelefono As Action(Of String) = Nothing
    Private _cronologiaCorrente As List(Of ChiamataRecente)
    Private _numeriInScelta As List(Of NumeroContatto)
    Private _nomeInScelta As String = ""
    Private _richiamaNumero As String = ""
    Private _richiamaNome As String = ""

    ' Stato della chiamata in corso
    Private _chiamataAttiva As Boolean = False
    Private _inAttesa As Boolean = False
    Private _vistoChiamata As Boolean = False
    Private _vistoImpostazione As Boolean = False
    Private _comandoInCorso As Boolean = False
    Private _numeroInChiamata As String = ""
    Private _nomeInChiamata As String = ""

    Private Const IntervalloAscoltoIniziale As Integer = 3000
    Private Const IntervalloAscoltoBase As Integer = 15000
    Private Const IntervalloAscoltoMassimo As Integer = 300000
    Private Const IntervalloDopoDisconnessione As Integer = 5000
    Private Const AttesaIdentificativoMs As Integer = 800
    Private Const NomeApplicazione As String = "Telefono"

    Private Shared ReadOnly logCondiviso As New wgcontrols.Logger()
    Private Shared ReadOnly _bloccoCondiviso As New Object()
    Private Shared _telefonoCondiviso As WGTelefono
    Private Shared _istanza As MenuTelefono
    Private Shared _idSessione As String = ""

    Public Shared NotificaChiamataPersa As Action(Of String)

    Private _gestisceInArrivo As Boolean = False
    Private _timerAscolto As System.Windows.Forms.Timer
    Private _timerDomanda As System.Windows.Forms.Timer
    Private _verificaAscoltoInCorso As Boolean = False
    Private _intervalloAscolto As Integer = IntervalloAscoltoBase
    Private _ascoltoTelefonoTrovato As Boolean = False
    Private _ascoltoRiuscito As Boolean = False

    Private ReadOnly _bloccoIdentificativo As New Object()
    Private _numeroRicevuto As String = ""
    Private _nomeRicevuto As String = ""
    Private _inArrivo As Boolean = False
    Private _esitoInArrivo As String = ""
    Private _numeroInArrivo As String = ""
    Private _nomeInArrivo As String = ""
    Private _listaInArrivo As WGMenuLista
    Private _esitoRisposta As Boolean = False
    Private _esitoRifiuto As Boolean = False
    Private _squillo As SoundPlayer
    Private _flussoSquillo As MemoryStream

    Public Sub New()
        _telefono = TelefonoCondiviso()
        _telefonoEsterno = True
        Inizializza()
    End Sub

    ''' <summary>Usa un WGTelefono gia' esistente (quello resta di proprieta' del chiamante).</summary>
    Public Sub New(telefono As WGTelefono)
        If telefono Is Nothing Then
            _telefono = New WGTelefono()
            _telefonoEsterno = False
        Else
            _telefono = telefono
            _telefonoEsterno = True
        End If
        Inizializza()
    End Sub

    Private Sub Inizializza()
        Try
            _marshal = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshal.Handle
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.Inizializza marshal: " & ex.Message)
        End Try
        Try
            AddHandler _telefono.StatoChiamataCambiato, AddressOf StatoChiamataRicevuto
            AddHandler _telefono.IdentificativoChiamante, AddressOf IdentificativoRicevuto
            AddHandler _telefono.TelefonoDisconnesso, AddressOf TelefonoDisconnessoRicevuto
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.Inizializza eventi: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property ChiamataInCorso As Boolean
        Get
            Return _chiamataAttiva
        End Get
    End Property

    ' ============================================================================================
    ' INFRASTRUTTURA (stessi schemi di MenuRubrica)
    ' ============================================================================================
    Private Sub EseguiSuInterfaccia(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _marshal Is Nothing OrElse _marshal.IsDisposed OrElse Not _marshal.IsHandleCreated Then
                azione.Invoke()
                Return
            End If
            If _marshal.InvokeRequired Then
                _marshal.BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As ObjectDisposedException
        Catch ex As InvalidOperationException
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.EseguiSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraAttesa(testo As String)
        Try
            ChiudiAttesa()
            _attesa = New WGProcessoInCorso()
            _attesa.Avvia(testo)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.MostraAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiAttesa()
        Try
            If _attesa IsNot Nothing Then
                _attesa.Fine()
                _attesa = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiAttesa: " & ex.Message)
            _attesa = Nothing
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggi As New WGMessaggi()
            messaggi.Messaggio(testo)
            messaggi.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function ElementoScelto(lista As WGMenuLista, idEvento As String) As String
        Try
            If lista IsNot Nothing Then
                Dim selezione As List(Of String) = lista.ottieniElementi()
                If selezione IsNot Nothing AndAlso selezione.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(selezione(0)) Then Return selezione(0)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ElementoScelto: " & ex.Message)
        End Try
        Return If(idEvento, "")
    End Function

    ' ============================================================================================
    ' MENU PRINCIPALE
    ' ============================================================================================
    Public Sub Avvia()
        Try
            _chiuso = False
            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Componi", AddressOf VoceComponi)
            _menuPrincipale.aggiungi("Chiamate recenti", AddressOf VoceChiamateRecenti)
            _menuPrincipale.aggiungi("Chiamate perse", AddressOf VoceChiamatePerse)
            _menuPrincipale.avvia("Telefono")
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.Avvia: " & ex.Message)
            MostraMessaggio("Non è stato possibile aprire il telefono: " & ex.Message)
        End Try
    End Sub

    Public Sub ApriChiamatePerse()
        Try
            VoceChiamatePerse(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ApriChiamatePerse: " & ex.Message)
        End Try
    End Sub

    Private Shared Function TelefonoCondiviso() As WGTelefono
        Try
            SyncLock _bloccoCondiviso
                If _telefonoCondiviso Is Nothing Then _telefonoCondiviso = New WGTelefono()
                Return _telefonoCondiviso
            End SyncLock
        Catch ex As Exception
            logCondiviso.ScriviLog("MenuTelefono.TelefonoCondiviso: " & ex.Message)
            Return New WGTelefono()
        End Try
    End Function

    Public Shared Function Istanza() As MenuTelefono
        Try
            SyncLock _bloccoCondiviso
                If _istanza Is Nothing Then _istanza = New MenuTelefono()
                Return _istanza
            End SyncLock
        Catch ex As Exception
            logCondiviso.ScriviLog("MenuTelefono.Istanza: " & ex.Message)
            Return New MenuTelefono()
        End Try
    End Function

    Public Shared Sub AvviaAscoltoChiamate()
        Try
            Istanza().AttivaAscolto()
        Catch ex As Exception
            logCondiviso.ScriviLog("MenuTelefono.AvviaAscoltoChiamate: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaAscolto()
        Try
            If _gestisceInArrivo Then Return
            _gestisceInArrivo = True
            _timerDomanda = New System.Windows.Forms.Timer()
            _timerDomanda.Interval = AttesaIdentificativoMs
            AddHandler _timerDomanda.Tick, AddressOf TimerDomandaTick
            _timerAscolto = New System.Windows.Forms.Timer()
            _timerAscolto.Interval = IntervalloAscoltoIniziale
            AddHandler _timerAscolto.Tick, AddressOf TimerAscoltoTick
            _timerAscolto.Start()
            ' La ricerca dei telefoni in background di WGBluetooth anticipa il controllo del
            ' vivavoce appena un telefono compare. Se il Bluetooth e' spento o non trova nulla
            ' l'evento non arriva: niente voci e niente errori nel log.
            AddHandler WGBluetooth.TelefonoTrovatoInSfondo, AddressOf TelefonoTrovatoInSfondo
            WGBluetooth.AvviaRicercaTelefoniInBackground()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.AttivaAscolto: " & ex.Message)
        End Try
    End Sub

    ' Un telefono trovato dalla ricerca in background: si controlla subito la sessione vivavoce,
    ' senza aspettare il prossimo giro del timer.
    Private Sub TelefonoTrovatoInSfondo(id As String, nome As String, connesso As Boolean)
        Try
            If Not _gestisceInArrivo OrElse _verificaAscoltoInCorso Then Return
            EseguiSuInterfaccia(Sub() TimerAscoltoTick(Nothing, EventArgs.Empty))
        Catch
        End Try
    End Sub

    Private Sub TimerAscoltoTick(sender As Object, e As EventArgs)
        Try
            If _verificaAscoltoInCorso Then Return
            If _telefono.SessioneChiamateAperta Then
                _intervalloAscolto = IntervalloAscoltoBase
                _timerAscolto.Interval = _intervalloAscolto
                Return
            End If
            _verificaAscoltoInCorso = True
            _timerAscolto.Stop()
            Task.Run(New Func(Of Task)(AddressOf LavoroAscolto))
        Catch ex As Exception
            _verificaAscoltoInCorso = False
            log.ScriviLog("MenuTelefono.TimerAscoltoTick: " & ex.Message)
        End Try
    End Sub

    Private Async Function LavoroAscolto() As Task
        Dim trovato As Boolean = False
        Dim riuscito As Boolean = False
        Try
            Dim trovati As List(Of TelefonoCollegato) = Await _telefono.TrovaTelefoniCollegati()
            If trovati IsNot Nothing AndAlso trovati.Count > 0 Then
                trovato = True
                Dim id As String = trovati(0).Id
                Dim preferito As String = _idTelefono
                For Each t As TelefonoCollegato In trovati
                    If String.Equals(t.Id, preferito, StringComparison.Ordinal) Then
                        id = t.Id
                        Exit For
                    End If
                Next
                riuscito = Await AssicuraSessione(id, False)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroAscolto: " & ex.Message)
        End Try
        _ascoltoTelefonoTrovato = trovato
        _ascoltoRiuscito = riuscito
        EseguiSuInterfaccia(AddressOf CompletaAscolto)
    End Function

    Private Sub CompletaAscolto()
        Try
            _verificaAscoltoInCorso = False
            If _ascoltoRiuscito OrElse Not _ascoltoTelefonoTrovato Then
                _intervalloAscolto = IntervalloAscoltoBase
            Else
                _intervalloAscolto = Math.Min(_intervalloAscolto * 2, IntervalloAscoltoMassimo)
            End If
            If _timerAscolto IsNot Nothing Then
                _timerAscolto.Interval = _intervalloAscolto
                _timerAscolto.Start()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaAscolto: " & ex.Message)
        End Try
    End Sub

    Private Sub TelefonoDisconnessoRicevuto()
        Try
            EseguiSuInterfaccia(AddressOf TelefonoDisconnessoSuInterfaccia)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TelefonoDisconnessoRicevuto: " & ex.Message)
        End Try
    End Sub

    Private Sub TelefonoDisconnessoSuInterfaccia()
        Try
            If Not _gestisceInArrivo Then Return
            If _inArrivo Then ChiudiChiamataInArrivo(False)
            If _timerAscolto Is Nothing OrElse _verificaAscoltoInCorso Then Return
            _intervalloAscolto = IntervalloAscoltoBase
            _timerAscolto.Stop()
            _timerAscolto.Interval = IntervalloDopoDisconnessione
            _timerAscolto.Start()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TelefonoDisconnessoSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub IdentificativoRicevuto(numero As String, nome As String)
        Try
            SyncLock _bloccoIdentificativo
                _numeroRicevuto = If(numero, "").Trim()
                _nomeRicevuto = If(nome, "").Trim()
            End SyncLock
            EseguiSuInterfaccia(AddressOf IdentificativoSuInterfaccia)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.IdentificativoRicevuto: " & ex.Message)
        End Try
    End Sub

    Private Sub IdentificativoSuInterfaccia()
        Try
            If Not _gestisceInArrivo OrElse _chiamataAttiva Then Return
            Dim numero As String
            Dim nome As String
            SyncLock _bloccoIdentificativo
                numero = _numeroRicevuto
                nome = _nomeRicevuto
            End SyncLock
            If Not _inArrivo Then SegnaleChiamataInArrivo()
            If Not _inArrivo Then Return
            If numero.Length > 0 Then _numeroInArrivo = numero
            If nome.Length > 0 Then _nomeInArrivo = nome
            If _listaInArrivo IsNot Nothing Then _listaInArrivo.impostaTitolo(TitoloInArrivo())
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.IdentificativoSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Function ChiChiamaInArrivo() As String
        Try
            If _nomeInArrivo.Length > 0 AndAlso _numeroInArrivo.Length > 0 AndAlso Not String.Equals(_nomeInArrivo, _numeroInArrivo, StringComparison.Ordinal) Then Return _nomeInArrivo & ", " & _numeroInArrivo
            If _nomeInArrivo.Length > 0 Then Return _nomeInArrivo
            If _numeroInArrivo.Length > 0 Then Return _numeroInArrivo
            Return "un numero sconosciuto"
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiChiamaInArrivo: " & ex.Message)
            Return "un numero sconosciuto"
        End Try
    End Function

    Private Function TitoloInArrivo() As String
        Return "Chiamata in arrivo da " & ChiChiamaInArrivo() & ". Vuoi rispondere?"
    End Function

    Private Sub SegnaleChiamataInArrivo()
        Try
            If Not _gestisceInArrivo OrElse _chiamataAttiva OrElse _inArrivo Then Return
            _inArrivo = True
            _esitoInArrivo = ""
            _numeroInArrivo = ""
            _nomeInArrivo = ""
            AvviaSquillo()
            If _timerDomanda IsNot Nothing Then
                _timerDomanda.Stop()
                _timerDomanda.Start()
            Else
                MostraDomandaInArrivo()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.SegnaleChiamataInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub TimerDomandaTick(sender As Object, e As EventArgs)
        Try
            _timerDomanda.Stop()
            If _inArrivo AndAlso _listaInArrivo Is Nothing AndAlso _esitoInArrivo.Length = 0 AndAlso Not _chiamataAttiva Then MostraDomandaInArrivo()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TimerDomandaTick: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriApplicazioneTelefono()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(NomeApplicazione)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ApriApplicazioneTelefono: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraDomandaInArrivo()
        Try
            ChiudiListaInArrivo()
            ApriApplicazioneTelefono()
            _listaInArrivo = New WGMenuLista()
            _listaInArrivo.multiselezioneConsentita = False
            _listaInArrivo.impostaTitolo(TitoloInArrivo())
            _listaInArrivo.aggiungiElemento("Sì, rispondi", "si")
            _listaInArrivo.aggiungiElemento("No, rifiuta", "no")
            AddHandler _listaInArrivo.ElementoSelezionato, AddressOf DomandaInArrivoSelezionata
            AddHandler _listaInArrivo.escPremuto, AddressOf DomandaInArrivoEsc
            _listaInArrivo.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.MostraDomandaInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub StaccaListaInArrivo()
        Try
            If _listaInArrivo Is Nothing Then Return
            RemoveHandler _listaInArrivo.ElementoSelezionato, AddressOf DomandaInArrivoSelezionata
            RemoveHandler _listaInArrivo.escPremuto, AddressOf DomandaInArrivoEsc
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.StaccaListaInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaInArrivo()
        Try
            If _listaInArrivo Is Nothing Then Return
            StaccaListaInArrivo()
            _listaInArrivo.abbandona()
            _listaInArrivo = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiListaInArrivo: " & ex.Message)
            _listaInArrivo = Nothing
        End Try
    End Sub

    Private Sub DomandaInArrivoSelezionata(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaInArrivo, idEvento)
            If Not _inArrivo OrElse _esitoInArrivo.Length > 0 Then Return
            Select Case id
                Case "si"
                    RispondiChiamataInArrivo()
                Case "no"
                    RifiutaChiamataInArrivo()
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.DomandaInArrivoSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub DomandaInArrivoEsc()
        Try
            If Not _inArrivo OrElse _esitoInArrivo.Length > 0 Then Return
            RifiutaChiamataInArrivo()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.DomandaInArrivoEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub RispondiChiamataInArrivo()
        Try
            _esitoInArrivo = "risposta"
            FermaSquillo()
            Task.Run(New Action(AddressOf LavoroRispondi))
        Catch ex As Exception
            _esitoInArrivo = ""
            log.ScriviLog("MenuTelefono.RispondiChiamataInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRispondi()
        Dim ok As Boolean = False
        Try
            ok = _telefono.Rispondi()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroRispondi: " & ex.Message)
        End Try
        _esitoRisposta = ok
        EseguiSuInterfaccia(AddressOf CompletaRisposta)
    End Sub

    Private Sub CompletaRisposta()
        Try
            If _chiamataAttiva OrElse Not _inArrivo Then Return
            If _esitoRisposta Then
                PassaAChiamataInCorso()
            Else
                _esitoInArrivo = ""
                sintesivocale.GetInstance().LeggiTesto("Non è stato possibile rispondere dal computer. Riprova oppure rispondi dal telefono")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaRisposta: " & ex.Message)
        End Try
    End Sub

    Private Sub PassaAChiamataInCorso()
        Try
            If _chiamataAttiva Then Return
            If _timerDomanda IsNot Nothing Then _timerDomanda.Stop()
            FermaSquillo()
            _numeroInChiamata = _numeroInArrivo
            _nomeInChiamata = _nomeInArrivo
            If _numeroInChiamata.Length = 0 AndAlso _nomeInChiamata.Length = 0 Then _nomeInChiamata = "numero sconosciuto"
            _inArrivo = False
            _esitoInArrivo = ""
            _chiamataAttiva = True
            _inAttesa = False
            _vistoChiamata = True
            _vistoImpostazione = True
            _comandoInCorso = False
            If _listaInArrivo IsNot Nothing Then
                ChiudiListaChiamataInCorso()
                StaccaListaInArrivo()
                _listaChiamataInCorso = _listaInArrivo
                _listaInArrivo = Nothing
                AddHandler _listaChiamataInCorso.ElementoSelezionato, AddressOf ChiamataInCorsoSelezionata
                AddHandler _listaChiamataInCorso.escPremuto, AddressOf ChiamataInCorsoEsc
            ElseIf _listaChiamataInCorso Is Nothing Then
                ApriApplicazioneTelefono()
            End If
            MostraChiamataInCorso()
            sintesivocale.GetInstance().LeggiTesto(TitoloChiamata())
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.PassaAChiamataInCorso: " & ex.Message)
        End Try
    End Sub

    Private Sub RifiutaChiamataInArrivo()
        Try
            _esitoInArrivo = "rifiuto"
            FermaSquillo()
            ChiudiListaInArrivo()
            sintesivocale.GetInstance().LeggiTesto("Chiamata rifiutata")
            Task.Run(New Action(AddressOf LavoroRifiuta))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.RifiutaChiamataInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRifiuta()
        Dim ok As Boolean = False
        Try
            ok = _telefono.Riaggancia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroRifiuta: " & ex.Message)
        End Try
        _esitoRifiuto = ok
        EseguiSuInterfaccia(AddressOf CompletaRifiuto)
    End Sub

    Private Sub CompletaRifiuto()
        Try
            If Not _inArrivo Then Return
            If _esitoRifiuto Then
                _inArrivo = False
                _esitoInArrivo = ""
            Else
                sintesivocale.GetInstance().LeggiTesto("Il telefono non ha rifiutato la chiamata")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaRifiuto: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiChiamataInArrivo(persa As Boolean)
        Try
            If _timerDomanda IsNot Nothing Then _timerDomanda.Stop()
            FermaSquillo()
            ChiudiListaInArrivo()
            Dim chi As String = ChiChiamaInArrivo()
            _inArrivo = False
            _esitoInArrivo = ""
            If Not persa Then Return
            Dim testo As String = "Chiamata persa da " & chi
            Dim notifica As Action(Of String) = NotificaChiamataPersa
            If notifica IsNot Nothing Then
                notifica(testo)
            Else
                sintesivocale.GetInstance().LeggiTesto(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiChiamataInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaSquillo()
        Try
            FermaSquillo()
            Dim percorso As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Ring01.wav")
            If File.Exists(percorso) Then
                _squillo = New SoundPlayer(percorso)
            Else
                _flussoSquillo = CreaSquilloSintetico()
                _squillo = New SoundPlayer(_flussoSquillo)
            End If
            _squillo.PlayLooping()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.AvviaSquillo: " & ex.Message)
            FermaSquillo()
        End Try
    End Sub

    Private Sub FermaSquillo()
        Try
            If _squillo IsNot Nothing Then
                _squillo.Stop()
                _squillo.Dispose()
                _squillo = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.FermaSquillo: " & ex.Message)
            _squillo = Nothing
        End Try
        Try
            If _flussoSquillo IsNot Nothing Then
                _flussoSquillo.Dispose()
                _flussoSquillo = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.FermaSquillo flusso: " & ex.Message)
            _flussoSquillo = Nothing
        End Try
    End Sub

    Private Function CreaSquilloSintetico() As MemoryStream
        Dim flusso As New MemoryStream()
        Try
            Dim frequenzaCampionamento As Integer = 22050
            Dim durataSecondi As Integer = 3
            Dim campioni As Integer = frequenzaCampionamento * durataSecondi
            Dim byteDati As Integer = campioni * 2
            Dim scrittore As New BinaryWriter(flusso, Encoding.ASCII, True)
            scrittore.Write(Encoding.ASCII.GetBytes("RIFF"))
            scrittore.Write(36 + byteDati)
            scrittore.Write(Encoding.ASCII.GetBytes("WAVE"))
            scrittore.Write(Encoding.ASCII.GetBytes("fmt "))
            scrittore.Write(16)
            scrittore.Write(CShort(1))
            scrittore.Write(CShort(1))
            scrittore.Write(frequenzaCampionamento)
            scrittore.Write(frequenzaCampionamento * 2)
            scrittore.Write(CShort(2))
            scrittore.Write(CShort(16))
            scrittore.Write(Encoding.ASCII.GetBytes("data"))
            scrittore.Write(byteDati)
            For i As Integer = 0 To campioni - 1
                Dim t As Double = i / CDbl(frequenzaCampionamento)
                Dim acceso As Boolean = (t < 0.4) OrElse (t >= 0.6 AndAlso t < 1.0)
                Dim valore As Double = 0
                If acceso Then valore = 0.3 * (Math.Sin(2 * Math.PI * 440 * t) + Math.Sin(2 * Math.PI * 480 * t)) / 2
                scrittore.Write(CShort(Math.Round(valore * Short.MaxValue)))
            Next
            scrittore.Flush()
            scrittore.Dispose()
            flusso.Position = 0
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CreaSquilloSintetico: " & ex.Message)
        End Try
        Return flusso
    End Function

    ' ============================================================================================
    ' VERIFICA DEL TELEFONO COLLEGATO (prima di qualsiasi funzione)
    ' ============================================================================================
    ' Cerca i telefoni associati, collegati in Bluetooth e con vivavoce. Se ce n'e' uno lo usa, se
    ' ce ne sono piu' di uno chiede quale (e ricorda la scelta finche' resta collegato), se non ce
    ' n'e' nessuno avvisa. Solo allora esegue "azione", passandole l'id del telefono.
    Private Sub VerificaTelefonoEPoi(azione As Action(Of String))
        Try
            If azione Is Nothing Then Return
            MostraAttesa("Verifica del telefono collegato in corso, attendere")
            Task.Run(Function() LavoroVerificaTelefono(azione))
        Catch ex As Exception
            ChiudiAttesa()
            log.ScriviLog("MenuTelefono.VerificaTelefonoEPoi: " & ex.Message)
        End Try
    End Sub

    Private Async Function LavoroVerificaTelefono(azione As Action(Of String)) As Task
        Dim trovati As List(Of TelefonoCollegato) = Nothing
        Try
            trovati = Await _telefono.TrovaTelefoniCollegati()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroVerificaTelefono: " & ex.Message)
        End Try
        EseguiSuInterfaccia(Sub() TelefoniTrovati(trovati, azione))
    End Function

    Private Sub TelefoniTrovati(trovati As List(Of TelefonoCollegato), azione As Action(Of String))
        Try
            ChiudiAttesa()
            If _chiuso Then Return

            If trovati Is Nothing OrElse trovati.Count = 0 Then
                _idTelefono = ""
                MostraMessaggio("Non c'è nessun telefono collegato in Bluetooth. Collega il telefono, controlla che sia associato a questo computer e che le chiamate in vivavoce siano consentite, poi riprova.")
                Return
            End If

            If trovati.Count = 1 Then
                _idTelefono = trovati(0).Id
                azione(_idTelefono)
                Return
            End If

            For Each t As TelefonoCollegato In trovati
                If String.Equals(t.Id, _idTelefono, StringComparison.Ordinal) Then
                    azione(_idTelefono)
                    Return
                End If
            Next

            MostraListaTelefoni(trovati, azione)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TelefoniTrovati: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraListaTelefoni(trovati As List(Of TelefonoCollegato), azione As Action(Of String))
        Try
            ChiudiListaTelefoni()
            _azionePendenteTelefono = azione
            _listaTelefoni = New WGMenuLista()
            _listaTelefoni.multiselezioneConsentita = False
            _listaTelefoni.impostaTitolo("Quale telefono vuoi usare?")
            For Each t As TelefonoCollegato In trovati
                _listaTelefoni.aggiungiElemento(t.Nome, t.Id)
            Next
            AddHandler _listaTelefoni.ElementoSelezionato, AddressOf TelefonoSelezionato
            AddHandler _listaTelefoni.escPremuto, AddressOf ListaTelefoniAbbandonata
            _listaTelefoni.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.MostraListaTelefoni: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaTelefoni()
        Try
            If _listaTelefoni Is Nothing Then Return
            RemoveHandler _listaTelefoni.ElementoSelezionato, AddressOf TelefonoSelezionato
            RemoveHandler _listaTelefoni.escPremuto, AddressOf ListaTelefoniAbbandonata
            _listaTelefoni.abbandona()
            _listaTelefoni = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiListaTelefoni: " & ex.Message)
            _listaTelefoni = Nothing
        End Try
    End Sub

    Private Sub ListaTelefoniAbbandonata()
        Try
            ChiudiListaTelefoni()
            _azionePendenteTelefono = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ListaTelefoniAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub TelefonoSelezionato(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaTelefoni, idEvento)
            ChiudiListaTelefoni()
            Dim azione As Action(Of String) = _azionePendenteTelefono
            _azionePendenteTelefono = Nothing
            If String.IsNullOrWhiteSpace(id) OrElse azione Is Nothing Then Return
            _idTelefono = id
            azione(id)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TelefonoSelezionato: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' COMPONI
    ' ============================================================================================
    Private Sub VoceComponi(sender As Object, e As EventArgs)
        Try
            VerificaTelefonoEPoi(Sub(id) ApriCampoNumero())
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.VoceComponi: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoNumero()
        Try
            If _chiamataAttiva Then
                MostraMessaggio("C'è già una chiamata in corso.")
                Return
            End If
            _campiNumero = New Campi()
            _campiNumero.SalvaSuEscape = False
            _campiNumero.Aggiungi("Numero da chiamare", "tel.numero", True, False, "")
            AddHandler _campiNumero.Fine, AddressOf NumeroConfermato
            AddHandler _campiNumero.Chiusura, AddressOf NumeroAnnullato
            _campiNumero.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ApriCampoNumero: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiNumero()
        Try
            If _campiNumero Is Nothing Then Return
            RemoveHandler _campiNumero.Fine, AddressOf NumeroConfermato
            RemoveHandler _campiNumero.Chiusura, AddressOf NumeroAnnullato
            _campiNumero = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LiberaCampiNumero: " & ex.Message)
            _campiNumero = Nothing
        End Try
    End Sub

    Private Sub NumeroAnnullato(sender As Object, e As EventArgs)
        LiberaCampiNumero()
    End Sub

    Private Sub NumeroConfermato(sender As Object, e As EventArgs)
        Try
            Dim numero As String = ""
            If _campiNumero IsNot Nothing Then numero = _campiNumero.RecuperaValore("tel.numero")
            LiberaCampiNumero()
            AvviaChiamataSuTelefono(_idTelefono, numero, "")
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.NumeroConfermato: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' CHIAMATE RECENTI E PERSE
    ' ============================================================================================
    Private Sub VoceChiamateRecenti(sender As Object, e As EventArgs)
        Try
            VerificaTelefonoEPoi(Sub(id) CaricaCronologia(id, False))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.VoceChiamateRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceChiamatePerse(sender As Object, e As EventArgs)
        Try
            VerificaTelefonoEPoi(Sub(id) CaricaCronologia(id, True))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.VoceChiamatePerse: " & ex.Message)
        End Try
    End Sub

    Private Sub CaricaCronologia(id As String, soloPerse As Boolean)
        Try
            MostraAttesa("Lettura delle chiamate dal telefono in corso, attendere")
            Task.Run(Function() LavoroCronologia(id, soloPerse))
        Catch ex As Exception
            ChiudiAttesa()
            log.ScriviLog("MenuTelefono.CaricaCronologia: " & ex.Message)
        End Try
    End Sub

    Private Async Function LavoroCronologia(id As String, soloPerse As Boolean) As Task
        Dim elenco As List(Of ChiamataRecente) = Nothing
        Try
            elenco = Await _telefono.ImportaCronologia(id, soloPerse, NumeroMassimoChiamate)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroCronologia: " & ex.Message)
        End Try
        EseguiSuInterfaccia(Sub() CronologiaCaricata(elenco, soloPerse))
    End Function

    Private Sub CronologiaCaricata(elenco As List(Of ChiamataRecente), soloPerse As Boolean)
        Try
            ChiudiAttesa()
            If _chiuso Then Return
            If elenco Is Nothing Then
                MostraMessaggio("Non è stato possibile leggere le chiamate dal telefono. Controlla che sul telefono sia consentito a questo computer l'accesso ai contatti e alla cronologia delle chiamate.")
                Return
            End If
            If elenco.Count = 0 Then
                MostraMessaggio(If(soloPerse, "Non ci sono chiamate perse.", "Non ci sono chiamate recenti."))
                Return
            End If

            ChiudiListaCronologia()
            _cronologiaCorrente = elenco
            _listaCronologia = New WGMenuLista()
            _listaCronologia.multiselezioneConsentita = False
            _listaCronologia.impostaTitolo(If(soloPerse, "Chiamate perse", "Chiamate recenti"))
            For i As Integer = 0 To elenco.Count - 1
                ' Il tipo (persa, ricevuta, effettuata) serve nelle recenti, dove le chiamate sono miste: nelle perse sarebbe ridondante.
                _listaCronologia.aggiungiElemento(DescriviChiamata(elenco(i), Not soloPerse), i.ToString())
            Next
            AddHandler _listaCronologia.ElementoSelezionato, AddressOf ChiamataCronologiaSelezionata
            AddHandler _listaCronologia.escPremuto, AddressOf ListaCronologiaAbbandonata
            _listaCronologia.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CronologiaCaricata: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviChiamata(c As ChiamataRecente, mostraTipo As Boolean) As String
        Try
            Dim testo As New StringBuilder()
            Dim nome As String = If(c.Nome, "").Trim()
            Dim numero As String = If(c.Numero, "").Trim()
            If nome.Length > 0 AndAlso Not String.Equals(nome, numero, StringComparison.Ordinal) Then
                testo.Append(nome)
                If numero.Length > 0 Then testo.Append(", ").Append(numero)
            ElseIf numero.Length > 0 Then
                testo.Append(numero)
            Else
                testo.Append("Numero privato")
            End If
            If mostraTipo Then
                Select Case c.Tipo
                    Case TipoChiamata.Persa : testo.Append(", persa")
                    Case TipoChiamata.Ricevuta : testo.Append(", ricevuta")
                    Case TipoChiamata.Effettuata : testo.Append(", effettuata")
                End Select
            End If
            If c.DataOra.HasValue Then testo.Append(", ").Append(c.DataOra.Value.ToString("dd/MM/yyyy HH:mm"))
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.DescriviChiamata: " & ex.Message)
            Return "Chiamata"
        End Try
    End Function

    Private Sub ChiudiListaCronologia()
        Try
            If _listaCronologia Is Nothing Then Return
            RemoveHandler _listaCronologia.ElementoSelezionato, AddressOf ChiamataCronologiaSelezionata
            RemoveHandler _listaCronologia.escPremuto, AddressOf ListaCronologiaAbbandonata
            _listaCronologia.abbandona()
            _listaCronologia = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiListaCronologia: " & ex.Message)
            _listaCronologia = Nothing
        End Try
    End Sub

    Private Sub ListaCronologiaAbbandonata()
        ChiudiListaCronologia()
    End Sub

    ' Invio su una chiamata: si propone di richiamare quel numero (con conferma).
    Private Sub ChiamataCronologiaSelezionata(idEvento As String)
        Try
            Dim testoIndice As String = ElementoScelto(_listaCronologia, idEvento)
            Dim indice As Integer
            Dim elenco As List(Of ChiamataRecente) = _cronologiaCorrente
            ChiudiListaCronologia()
            If elenco Is Nothing OrElse Not Integer.TryParse(testoIndice, indice) OrElse indice < 0 OrElse indice >= elenco.Count Then Return

            Dim c As ChiamataRecente = elenco(indice)
            If String.IsNullOrWhiteSpace(c.Numero) Then
                MostraMessaggio("Questa chiamata non ha un numero, quindi non si può richiamare.")
                Return
            End If
            _richiamaNumero = c.Numero
            _richiamaNome = c.Nome
            Dim chi As String = If(String.IsNullOrWhiteSpace(c.Nome), c.Numero, c.Nome & ", " & c.Numero)
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaRichiama
            domanda.avvia("Vuoi chiamare " & chi & "?")
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiamataCronologiaSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaRichiama(rispostaDomanda As DialogResult)
        Try
            If rispostaDomanda <> DialogResult.Yes Then Return
            Chiama(_richiamaNumero, _richiamaNome)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.RispostaRichiama: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' API PUBBLICA PER ALTRE CLASSI (es. MenuRubrica)
    ' ============================================================================================
    ''' <summary>Chiama un numero, dopo aver verificato il telefono collegato.</summary>
    Public Sub Chiama(numero As String, Optional nome As String = "")
        Try
            VerificaTelefonoEPoi(Sub(id) AvviaChiamataSuTelefono(id, numero, nome))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.Chiama: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Chiama un contatto: se ha piu' numeri fa scegliere quale chiamare (dopo aver verificato il
    ''' telefono collegato).
    ''' </summary>
    Public Sub ChiamaContatto(nome As String, numeri As List(Of NumeroContatto))
        Try
            If numeri Is Nothing OrElse numeri.Count = 0 Then
                MostraMessaggio("Questo contatto non ha numeri di telefono.")
                Return
            End If
            VerificaTelefonoEPoi(Sub(id) ScegliNumeroEChiama(nome, numeri))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiamaContatto: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliNumeroEChiama(nome As String, numeri As List(Of NumeroContatto))
        Try
            If numeri.Count = 1 Then
                AvviaChiamataSuTelefono(_idTelefono, numeri(0).Numero, nome)
                Return
            End If
            ChiudiListaNumeri()
            _numeriInScelta = numeri
            _nomeInScelta = nome
            _listaNumeri = New WGMenuLista()
            _listaNumeri.multiselezioneConsentita = False
            _listaNumeri.impostaTitolo("Quale numero vuoi chiamare?")
            For i As Integer = 0 To numeri.Count - 1
                Dim voce As String = numeri(i).Numero
                If Not String.IsNullOrWhiteSpace(numeri(i).Etichetta) Then voce = numeri(i).Etichetta & ", " & numeri(i).Numero
                _listaNumeri.aggiungiElemento(voce, i.ToString())
            Next
            AddHandler _listaNumeri.ElementoSelezionato, AddressOf NumeroDaChiamareSelezionato
            AddHandler _listaNumeri.escPremuto, AddressOf ListaNumeriAbbandonata
            _listaNumeri.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ScegliNumeroEChiama: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaNumeri()
        Try
            If _listaNumeri Is Nothing Then Return
            RemoveHandler _listaNumeri.ElementoSelezionato, AddressOf NumeroDaChiamareSelezionato
            RemoveHandler _listaNumeri.escPremuto, AddressOf ListaNumeriAbbandonata
            _listaNumeri.abbandona()
            _listaNumeri = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiListaNumeri: " & ex.Message)
            _listaNumeri = Nothing
        End Try
    End Sub

    Private Sub ListaNumeriAbbandonata()
        ChiudiListaNumeri()
    End Sub

    Private Sub NumeroDaChiamareSelezionato(idEvento As String)
        Try
            Dim testoIndice As String = ElementoScelto(_listaNumeri, idEvento)
            Dim numeri As List(Of NumeroContatto) = _numeriInScelta
            Dim nome As String = _nomeInScelta
            ChiudiListaNumeri()
            Dim indice As Integer
            If numeri Is Nothing OrElse Not Integer.TryParse(testoIndice, indice) OrElse indice < 0 OrElse indice >= numeri.Count Then Return
            AvviaChiamataSuTelefono(_idTelefono, numeri(indice).Numero, nome)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.NumeroDaChiamareSelezionato: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' AVVIO DELLA CHIAMATA
    ' ============================================================================================
    Private Function ContieneCifre(numero As String) As Boolean
        If String.IsNullOrEmpty(numero) Then Return False
        Return numero.Any(Function(c) Char.IsDigit(c))
    End Function

    Private Sub AvviaChiamataSuTelefono(id As String, numero As String, nome As String)
        Try
            If _chiamataAttiva Then
                MostraMessaggio("C'è già una chiamata in corso.")
                Return
            End If
            If String.IsNullOrWhiteSpace(id) Then
                MostraMessaggio("Nessun telefono selezionato.")
                Return
            End If
            If Not ContieneCifre(numero) Then
                MostraMessaggio("Inserisci un numero di telefono valido.")
                Return
            End If

            ' Lo stato va preparato PRIMA di inviare il comando, cosi' gli eventi che arrivano
            ' subito dopo (anche una chiamata rifiutata) vengono interpretati correttamente.
            _numeroInChiamata = numero.Trim()
            _nomeInChiamata = If(nome, "").Trim()
            _chiamataAttiva = True
            _inAttesa = False
            _vistoChiamata = False
            _vistoImpostazione = False
            _comandoInCorso = False

            MostraAttesa("Chiamata in corso, attendere")
            Task.Run(Function() LavoroChiamata(id, numero))
        Catch ex As Exception
            ChiudiAttesa()
            _chiamataAttiva = False
            log.ScriviLog("MenuTelefono.AvviaChiamataSuTelefono: " & ex.Message)
        End Try
    End Sub

    Private Async Function AssicuraSessione(id As String, forza As Boolean) As Task(Of Boolean)
        Try
            If Not forza AndAlso _telefono.SessioneChiamateAperta AndAlso String.Equals(_idSessione, id, StringComparison.Ordinal) Then Return True
            _idSessione = ""
            Dim ok As Boolean = Await _telefono.ApriChiamate(id)
            If ok Then _idSessione = id
            Return ok
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.AssicuraSessione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function LavoroChiamata(id As String, numero As String) As Task
        Dim ok As Boolean = False
        Try
            Dim sessioneGiaAperta As Boolean = _telefono.SessioneChiamateAperta AndAlso String.Equals(_idSessione, id, StringComparison.Ordinal)
            ok = Await AssicuraSessione(id, False)
            If ok Then ok = _telefono.Chiama(numero)
            ' Una sessione "vecchia" potrebbe essere caduta senza che ce ne siamo accorti: si
            ' ricollega e si riprova una volta sola.
            If Not ok AndAlso sessioneGiaAperta Then
                ok = Await AssicuraSessione(id, True)
                If ok Then ok = _telefono.Chiama(numero)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroChiamata: " & ex.Message)
            ok = False
        End Try
        Dim esito As Boolean = ok
        EseguiSuInterfaccia(Sub() CompletaChiamata(esito))
    End Function

    Private Sub CompletaChiamata(ok As Boolean)
        Try
            ChiudiAttesa()
            If Not ok Then
                AzzeraStatoChiamata()
                MostraMessaggio("Non è stato possibile avviare la chiamata. Verifica che il telefono sia collegato e raggiungibile.")
                Return
            End If
            If _chiamataAttiva Then
                MostraChiamataInCorso()
            Else
                ' Terminata prima ancora che il menu comparisse (rifiutata, occupato...).
                MostraMessaggio("La chiamata non è andata a buon fine.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaChiamata: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' EVENTI DEL TELEFONO (arrivano dal thread di lettura: si passa all'interfaccia)
    ' ============================================================================================
    Private Sub StatoChiamataRicevuto(stato As String)
        Try
            Dim copia As String = stato
            EseguiSuInterfaccia(Sub() ElaboraStato(copia))
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.StatoChiamataRicevuto: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraStato(stato As String)
        Try
            If String.IsNullOrWhiteSpace(stato) Then Return

            If String.Equals(stato, "in_arrivo", StringComparison.Ordinal) Then
                SegnaleChiamataInArrivo()
                Return
            End If

            If String.Equals(stato, "terminata", StringComparison.Ordinal) Then
                If _inArrivo Then
                    ChiudiChiamataInArrivo(_esitoInArrivo.Length = 0)
                    Return
                End If
                If _chiamataAttiva Then TerminaChiamataLocale("Chiamata terminata.")
                Return
            End If

            Dim parti() As String = stato.Split(","c)
            If parti.Length < 2 Then Return
            Dim indice As Integer
            Dim valore As Integer
            If Not Integer.TryParse(parti(0).Trim(), indice) OrElse Not Integer.TryParse(parti(1).Trim(), valore) Then Return

            If _gestisceInArrivo AndAlso Not _chiamataAttiva Then
                If indice = IndiceImpostazione AndAlso valore = 1 Then
                    SegnaleChiamataInArrivo()
                    Return
                End If
                If _inArrivo Then
                    If indice = IndiceChiamata AndAlso valore = 1 Then
                        PassaAChiamataInCorso()
                        Return
                    End If
                    If indice = IndiceImpostazione AndAlso valore = 0 Then
                        ChiudiChiamataInArrivo(_esitoInArrivo.Length = 0)
                        Return
                    End If
                End If
            End If

            If Not _chiamataAttiva Then Return

            Select Case indice
                Case IndiceChiamata
                    If valore = 1 Then
                        _vistoChiamata = True
                    ElseIf valore = 0 AndAlso _vistoChiamata Then
                        TerminaChiamataLocale("Chiamata terminata.")
                    End If

                Case IndiceImpostazione
                    If valore > 0 Then
                        _vistoImpostazione = True
                    ElseIf valore = 0 AndAlso _vistoImpostazione AndAlso Not _vistoChiamata Then
                        TerminaChiamataLocale("La chiamata non è stata completata.")
                    End If

                Case IndiceInAttesa
                    Dim inAttesaOra As Boolean = (valore = 2)
                    If inAttesaOra <> _inAttesa Then
                        _inAttesa = inAttesaOra
                        If _listaChiamataInCorso IsNot Nothing Then MostraChiamataInCorso()
                    End If
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ElaboraStato: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' MENU DELLA CHIAMATA IN CORSO (resta aperto finche' la chiamata non termina)
    ' ============================================================================================
    Private Function TitoloChiamata() As String
        Dim chi As String = If(String.IsNullOrWhiteSpace(_nomeInChiamata), _numeroInChiamata, _nomeInChiamata)
        Return If(_inAttesa, "Chiamata in attesa con ", "Chiamata in corso con ") & chi
    End Function

    ' Se la lista e' gia' aperta si ricaricano solo gli elementi (pulisci + reinserimento), come
    ' fanno gli elenchi di MenuRubrica: il menu non viene mai chiuso e riaperto.
    Private Sub MostraChiamataInCorso()
        Try
            If Not _chiamataAttiva Then Return
            Dim giaAperta As Boolean = (_listaChiamataInCorso IsNot Nothing)
            If Not giaAperta Then
                _listaChiamataInCorso = New WGMenuLista()
                _listaChiamataInCorso.multiselezioneConsentita = False
                AddHandler _listaChiamataInCorso.ElementoSelezionato, AddressOf ChiamataInCorsoSelezionata
                AddHandler _listaChiamataInCorso.escPremuto, AddressOf ChiamataInCorsoEsc
            Else
                Try
                    _listaChiamataInCorso.pulisci()
                Catch exPulisci As Exception
                    log.ScriviLog("MenuTelefono.MostraChiamataInCorso pulisci: " & exPulisci.Message)
                End Try
            End If

            _listaChiamataInCorso.impostaTitolo(TitoloChiamata())
            _listaChiamataInCorso.aggiungiElemento(If(_inAttesa, "Riprendi la chiamata", "Metti in attesa"), "attesa")
            _listaChiamataInCorso.aggiungiElemento("Termina la chiamata", "termina")

            If Not giaAperta Then _listaChiamataInCorso.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.MostraChiamataInCorso: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaChiamataInCorso()
        Try
            If _listaChiamataInCorso Is Nothing Then Return
            RemoveHandler _listaChiamataInCorso.ElementoSelezionato, AddressOf ChiamataInCorsoSelezionata
            RemoveHandler _listaChiamataInCorso.escPremuto, AddressOf ChiamataInCorsoEsc
            _listaChiamataInCorso.abbandona()
            _listaChiamataInCorso = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiudiListaChiamataInCorso: " & ex.Message)
            _listaChiamataInCorso = Nothing
        End Try
    End Sub

    ' Esc non chiude il menu: durante la chiamata resta aperto, e si chiude da solo quando la
    ' chiamata termina (da qui con "Termina la chiamata", oppure dall'altro capo).
    Private Sub ChiamataInCorsoEsc()
    End Sub

    Private Sub AzzeraStatoChiamata()
        _chiamataAttiva = False
        _inAttesa = False
        _vistoChiamata = False
        _vistoImpostazione = False
        _comandoInCorso = False
    End Sub

    ' Chiude il menu e azzera lo stato PRIMA di mostrare il messaggio (che e' modale e lascia
    ' girare altri eventi).
    Private Sub TerminaChiamataLocale(messaggio As String)
        Try
            AzzeraStatoChiamata()
            ChiudiListaChiamataInCorso()
            If Not String.IsNullOrWhiteSpace(messaggio) Then MostraMessaggio(messaggio)
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.TerminaChiamataLocale: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiamataInCorsoSelezionata(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaChiamataInCorso, idEvento)
            If _comandoInCorso Then Return
            Select Case id
                Case "attesa"
                    CambiaAttesa()
                Case "termina"
                    TerminaChiamata()
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.ChiamataInCorsoSelezionata: " & ex.Message)
        End Try
    End Sub

    ' --- Attesa / ripresa -------------------------------------------------------------------
    Private Sub CambiaAttesa()
        Try
            _comandoInCorso = True
            Dim mettere As Boolean = Not _inAttesa
            Task.Run(Sub() LavoroAttesa(mettere))
        Catch ex As Exception
            _comandoInCorso = False
            log.ScriviLog("MenuTelefono.CambiaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroAttesa(mettere As Boolean)
        Dim ok As Boolean = False
        Try
            ok = If(mettere, _telefono.MettiInAttesa(), _telefono.RiprendiChiamata())
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroAttesa: " & ex.Message)
        End Try
        Dim esito As Boolean = ok
        EseguiSuInterfaccia(Sub() CompletaAttesa(esito, mettere))
    End Sub

    Private Sub CompletaAttesa(ok As Boolean, mettere As Boolean)
        Try
            _comandoInCorso = False
            If Not _chiamataAttiva Then Return
            If ok Then
                _inAttesa = mettere
                MostraChiamataInCorso()
                MostraMessaggio(If(mettere, "Chiamata messa in attesa.", "Chiamata ripresa."))
            Else
                MostraMessaggio("Il telefono non ha eseguito il comando. Potrebbe non supportare la messa in attesa dal vivavoce.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaAttesa: " & ex.Message)
        End Try
    End Sub

    ' --- Termina ----------------------------------------------------------------------------
    Private Sub TerminaChiamata()
        Try
            _comandoInCorso = True
            Task.Run(Sub() LavoroTermina())
        Catch ex As Exception
            _comandoInCorso = False
            log.ScriviLog("MenuTelefono.TerminaChiamata: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroTermina()
        Dim ok As Boolean = False
        Try
            ok = _telefono.Riaggancia()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.LavoroTermina: " & ex.Message)
        End Try
        Dim esito As Boolean = ok
        EseguiSuInterfaccia(Sub() CompletaTermina(esito))
    End Sub

    Private Sub CompletaTermina(ok As Boolean)
        Try
            _comandoInCorso = False
            ' Se nel frattempo e' arrivato l'evento di fine chiamata, e' gia' stato tutto chiuso.
            If Not _chiamataAttiva Then Return
            If ok Then
                TerminaChiamataLocale("Chiamata terminata.")
            Else
                MostraMessaggio("Non è stato possibile terminare la chiamata. Riprova.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.CompletaTermina: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _chiuso = True
            Try
                RemoveHandler _telefono.StatoChiamataCambiato, AddressOf StatoChiamataRicevuto
                RemoveHandler _telefono.IdentificativoChiamante, AddressOf IdentificativoRicevuto
                RemoveHandler _telefono.TelefonoDisconnesso, AddressOf TelefonoDisconnessoRicevuto
            Catch exEventi As Exception
                log.ScriviLog("MenuTelefono.Dispose eventi: " & exEventi.Message)
            End Try
            Try
                If _timerAscolto IsNot Nothing Then
                    _timerAscolto.Stop()
                    RemoveHandler _timerAscolto.Tick, AddressOf TimerAscoltoTick
                    _timerAscolto.Dispose()
                    _timerAscolto = Nothing
                End If
                If _timerDomanda IsNot Nothing Then
                    _timerDomanda.Stop()
                    RemoveHandler _timerDomanda.Tick, AddressOf TimerDomandaTick
                    _timerDomanda.Dispose()
                    _timerDomanda = Nothing
                End If
            Catch exTimer As Exception
                log.ScriviLog("MenuTelefono.Dispose timer: " & exTimer.Message)
            End Try
            _gestisceInArrivo = False
            FermaSquillo()
            ChiudiListaInArrivo()
            SyncLock _bloccoCondiviso
                If _istanza Is Me Then _istanza = Nothing
            End SyncLock
            ChiudiAttesa()
            ChiudiListaTelefoni()
            ChiudiListaNumeri()
            ChiudiListaCronologia()
            ChiudiListaChiamataInCorso()
            LiberaCampiNumero()
            ' Chiudere la sessione non interrompe la chiamata sul telefono.
            If Not _telefonoEsterno Then _telefono.Dispose()
            If _marshal IsNot Nothing Then _marshal.Dispose()
        Catch ex As Exception
            log.ScriviLog("MenuTelefono.Dispose: " & ex.Message)
        End Try
    End Sub

End Class