Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Applicazioni separate: ogni voce scelta dal menu iniziale apre un'applicazione con la sua pila di finestre
' (sottomenu, campi, editor, browser...). Windows Tab e Shift Windows Tab passano da un'applicazione
' all'altra mostrando l'ultima finestra aperta in ciascuna; il menu iniziale è sempre la prima applicazione.
' Le finestre restano nell'unica pila del contenitore: passando a un'applicazione le sue finestre vengono
' portate in cima nel loro ordine, così tutto il resto del contenitore continua a lavorare con la cima della pila.
Partial Public Class WGContenitore

    Private Const ChiaveApplicazioniSeparate As String = "applicazioni_separate"
    Private Const ApplicazioneMenuIniziale As Integer = 0

    Private Shared _applicazioniSeparate As Integer = -1
    Private _menuIniziale As Control
    ' Da quale applicazione è stata aperta ciascuna: chiudendola si torna lì (per esempio alla scrivania).
    Private ReadOnly _origineApplicazione As New Dictionary(Of Integer, Integer)()
    Private _elencoConGestoCollegato As Boolean
    Private ReadOnly _applicazioneDi As New Dictionary(Of Control, Integer)()
    Private ReadOnly _nomiApplicazioni As New Dictionary(Of Integer, String)()
    Private _applicazioneCorrente As Integer = ApplicazioneMenuIniziale
    Private _prossimaApplicazione As Integer = 1

    ''' <summary>Sistema delle applicazioni separate attivo (predefinito). Letto una volta e poi tenuto in memoria.</summary>
    Public Shared Property ApplicazioniSeparate As Boolean
        Get
            If _applicazioniSeparate < 0 Then
                Try
                    Dim valore As String = New WGConfig().LeggiValore(ChiaveApplicazioniSeparate)
                    _applicazioniSeparate = If(valore = "0", 0, 1)
                Catch ex As Exception
                    _logShared.ScriviLog("ApplicazioniSeparate: " & ex.Message)
                    _applicazioniSeparate = 1
                End Try
            End If
            ' Le applicazioni separate sono una novità della modalità smart: nella classica non ci sono.
            Return _applicazioniSeparate = 1 AndAlso WGModalita.Smart
        End Get
        Set(value As Boolean)
            _applicazioniSeparate = If(value, 1, 0)
            Try
                Dim configurazione As New WGConfig()
                configurazione.AggiungiValore(ChiaveApplicazioniSeparate, If(value, "1", "0"))
            Catch ex As Exception
                _logShared.ScriviLog("ApplicazioniSeparate scrittura: " & ex.Message)
            End Try
        End Set
    End Property

    ''' <summary>Indica il menu iniziale: le voci scelte da lì aprono nuove applicazioni.</summary>
    Friend Sub ImpostaMenuIniziale(menu As Control)
        Try
            If menu Is Nothing Then Return
            SyncLock _lockStack
                _menuIniziale = menu
                If Not _elencoConGestoCollegato Then
                    ' Triplo tocco con due dita: elenco delle applicazioni, come F10.
                    _elencoConGestoCollegato = True
                    WGAvvisiSistema.Avvia()
                    WGUscitaAudio.Avvia()
                    AddHandler WGTouk.GetInstance().TriploTapDueDita, Sub() MostraElencoApplicazioni()
                    ' Casa del tablet: due dita in su per il menu applicazioni, a destra e sinistra per cambiare applicazione.
                    AddHandler WGTouk.GetInstance().FlickDueDitaSu, Sub()
                                                                        If MessaggioAperto() Then Return
                                                                        ' In modalità tablet la casa è la home, al computer la scrivania.
                                                                        If WGModalitaTablet.Attiva() Then
                                                                            If Not WGHome.Vai() Then VaiAlMenuIniziale()
                                                                        ElseIf Not WGScrivania.Attiva OrElse Not WGScrivania.Vai() Then
                                                                            VaiAlMenuIniziale()
                                                                        End If
                                                                    End Sub
                    AddHandler WGTouk.GetInstance().FlickDueDitaLaterale, Sub(avanti As Boolean)
                                                                              If MessaggioAperto() Then Return
                                                                              NavigaApplicazione(avanti)
                                                                          End Sub
                    ' Tre dita in giù: il centro notifiche; in su: in modalità tablet il pannello del centro di
                    ' controllo (lo stesso gesto lo richiude), come su tablet e telefoni; al computer il menu di sempre. Con davanti un campo di testo o il browser le tre
                    ' dita sono il rotore e i centri si aprono partendo dalla barra in alto.
                    WGTouk.GetInstance().RotoreTestoServe = AddressOf DavantiCampoDiTesto
                    AddHandler WGTouk.GetInstance().FlickTreDitaVerticale, Sub(giu As Boolean)
                                                                               If Not WGModalita.Smart OrElse MessaggioAperto() OrElse EsistonoAtteseAttive() Then Return
                                                                               If giu Then
                                                                                   MostraCentroNotifiche()
                                                                               ElseIf WGModalitaTablet.Attiva() Then
                                                                                   WGPannelloControllo.Mostra()
                                                                               Else
                                                                                   WGCentroDiControllo.GetInstance()?.VisualizzaControllo()
                                                                               End If
                                                                           End Sub
                End If
                _applicazioneDi(menu) = ApplicazioneMenuIniziale
                _nomiApplicazioni(ApplicazioneMenuIniziale) = NomeMenuApplicazioni
                _applicazioneCorrente = ApplicazioneMenuIniziale
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ImpostaMenuIniziale: " & ex.Message)
        End Try
    End Sub

    Private Function ApplicazioneDi(controllo As Control) As Integer
        Dim applicazione As Integer = ApplicazioneMenuIniziale
        If controllo IsNot Nothing AndAlso _applicazioneDi.TryGetValue(controllo, applicazione) Then Return applicazione
        Return ApplicazioneMenuIniziale
    End Function

    ' Da chiamare in MostraControllo prima di mettere il controllo in cima.
    Private Sub AssegnaApplicazione(controllo As Control)
        Try
            If Not ApplicazioniSeparate OrElse _menuIniziale Is Nothing OrElse controllo Is Nothing Then Return
            ' Anche i sottomenu contano: la sessione riapre il menu più interno raggiunto.
            WGSessione.SalvaPresto()
            SyncLock _lockStack
                Dim esistente As Integer
                If _applicazioneDi.TryGetValue(controllo, esistente) Then
                    _applicazioneCorrente = esistente
                    Return
                End If
                Dim applicazione As Integer = _applicazioneCorrente
                If _nomeProssimaApplicazione <> "" AndAlso (DateTime.Now - _istanteNomeProssimaApplicazione).TotalSeconds > 10 Then _nomeProssimaApplicazione = ""
                If _nomeProssimaApplicazione <> "" AndAlso controllo IsNot _menuIniziale AndAlso Not EControlloMessaggio(controllo) Then
                    applicazione = _prossimaApplicazione
                    _prossimaApplicazione += 1
                    _origineApplicazione(applicazione) = _applicazioneCorrente
                    _riapertura(applicazione) = _riaperturaProssimaApplicazione
                    _riaperturaProssimaApplicazione = ""
                    _nomiApplicazioni(applicazione) = _nomeProssimaApplicazione
                    _nomeProssimaApplicazione = ""
                    _applicazioneDi(controllo) = applicazione
                    _applicazioneCorrente = applicazione
                    Return
                End If
                ' Una finestra aperta mentre in cima c'è il menu iniziale è una voce scelta da lì: nuova applicazione.
                ' I messaggi restano nell'applicazione in cui compaiono.
                If controllo IsNot _menuIniziale AndAlso Not EControlloMessaggio(controllo) AndAlso CimaPerApertura() Is _menuIniziale Then
                    applicazione = _prossimaApplicazione
                    _prossimaApplicazione += 1
                    _origineApplicazione(applicazione) = ApplicazioneMenuIniziale
                    _nomiApplicazioni(applicazione) = NomeNuovaApplicazione(controllo)
                    Dim menuIniziale As WGMenuInt = TryCast(_menuIniziale, WGMenuInt)
                    If menuIniziale IsNot Nothing AndAlso menuIniziale.VoceSelezionata <> "" Then _riapertura(applicazione) = "menu:" & menuIniziale.VoceSelezionata
                End If
                _applicazioneDi(controllo) = applicazione
                _applicazioneCorrente = applicazione
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AssegnaApplicazione: " & ex.Message)
        End Try
    End Sub

#Region "Menu applicazioni come applicazione normale (modalità smart)"

    ' In modalità smart il menu applicazioni è un'applicazione come le altre: all'avvio non è aperto (c'è la
    ' scrivania o la home), si apre con il tasto Windows e si chiude con Esc, Control F4, di nuovo il tasto
    ' Windows o dal multitasking; chiuso non compare nel multitasking né passando da un'applicazione
    ' all'altra, e chiudendolo si torna all'applicazione da cui lo si era aperto. Resta però pronto dietro,
    ' nascosto e zitto: da lui partono le voci aperte da scrivania, home, ricerca e ripresa della sessione.
    Private _menuApplicazioniAperto As Boolean = True
    Private _origineMenuApplicazioni As Integer = -1

    Private Shared Function MenuApplicazioniComeApplicazione() As Boolean
        Return WGModalita.Smart AndAlso ApplicazioniSeparate
    End Function

    Friend ReadOnly Property MenuApplicazioniAperto As Boolean
        Get
            Return Not MenuApplicazioniComeApplicazione() OrElse _menuApplicazioniAperto
        End Get
    End Property

    ''' <summary>All'avvio in modalità smart il menu applicazioni nasce chiuso (da chiamare prima di mostrarlo).</summary>
    Public Sub AvviaMenuApplicazioniChiuso()
        If MenuApplicazioniComeApplicazione() Then _menuApplicazioniAperto = False
    End Sub

    ' Il menu viene davanti: è aperto, e si ricorda da dove lo si è aperto per tornarci chiudendolo.
    Private Sub SegnaMenuApplicazioniAperto()
        If _applicazioneCorrente <> ApplicazioneMenuIniziale Then _origineMenuApplicazioni = _applicazioneCorrente
        _menuApplicazioniAperto = True
    End Sub

    ''' <summary>
    ''' Chiude il menu applicazioni (modalità smart): si torna all'applicazione da cui lo si era aperto,
    ''' altrimenti a scrivania o home, altrimenti all'ultima usata. Falso se non c'è nient'altro dove andare.
    ''' </summary>
    Public Function ChiudiMenuApplicazioni() As Boolean
        Try
            If Not MenuApplicazioniComeApplicazione() Then Return False
            If Me.InvokeRequired Then
                Return CBool(Me.Invoke(New Func(Of Boolean)(AddressOf ChiudiMenuApplicazioni)))
            End If
            Dim altre As List(Of Integer) = ApplicazioniPerUso().Where(Function(a) a <> ApplicazioneMenuIniziale).ToList()
            Dim destinazione As Integer = -1
            If _origineMenuApplicazioni > 0 AndAlso altre.Contains(_origineMenuApplicazioni) Then destinazione = _origineMenuApplicazioni
            If destinazione < 0 Then destinazione = altre.Where(Function(a) EApplicazioneProtetta(a)).DefaultIfEmpty(-1).First()
            If destinazione < 0 AndAlso altre.Count > 0 Then destinazione = altre(0)
            _menuApplicazioniAperto = False
            If destinazione >= 0 Then
                VaiAllApplicazione(destinazione)
                Return True
            End If
            ' Non c'è altro aperto: la scrivania, o la home in modalità tablet.
            If WGScrivania.Vai() Then Return True
            _menuApplicazioniAperto = True
            Return False
        Catch ex As Exception
            _log.ScriviLog("ChiudiMenuApplicazioni: " & ex.Message)
            _menuApplicazioniAperto = True
            Return False
        End Try
    End Function

    ''' <summary>Tasto Windows: apre il menu applicazioni; se è già davanti (modalità smart) lo chiude.</summary>
    Public Sub CommutaMenuApplicazioni()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf CommutaMenuApplicazioni))
                Return
            End If
            If MenuApplicazioniComeApplicazione() AndAlso _menuApplicazioniAperto AndAlso _applicazioneCorrente = ApplicazioneMenuIniziale AndAlso
               Not MessaggioAperto() Then
                If ChiudiMenuApplicazioni() Then Return
            End If
            VaiAlMenuIniziale()
        Catch ex As Exception
            _log.ScriviLog("CommutaMenuApplicazioni: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Apertura in sottofondo (ripresa della sessione)"

    ' Alla ripresa della sessione le applicazioni dell'ultima volta si aprono dietro: le loro finestre
    ' entrano nella pila, nell'applicazione giusta, ma restano nascoste (e quindi zitte); davanti resta
    ' quello che c'era (la scrivania o la home), che continua a ricevere tasti e gesti. Ci si va quando
    ' serve, con F10, Windows Tab o la ricerca, e solo allora si mostrano e si annunciano.
    Private _aperturaInSottofondo As Boolean
    Private _applicazioneDavanti As Integer = ApplicazioneMenuIniziale
    Private _ultimoInSottofondo As Control

    Friend ReadOnly Property AperturaInSottofondo As Boolean
        Get
            Return _aperturaInSottofondo
        End Get
    End Property

    Friend Sub IniziaAperturaInSottofondo()
        _applicazioneDavanti = _applicazioneCorrente
        _ultimoInSottofondo = _menuIniziale
        _aperturaInSottofondo = True
    End Sub

    ' Le attese delle applicazioni riaperte (per esempio la posta che si scarica) possono partire poco dopo
    ' l'ultima apertura: restano dietro anche quelle nate nei secondi subito dopo.
    Private _atteseInSottofondoFino As DateTime = DateTime.MinValue

    ''' <summary>Vero se un'attesa che parte adesso appartiene a un'applicazione riaperta in sottofondo.</summary>
    Friend Function AttesaInSottofondo() As Boolean
        Return _aperturaInSottofondo OrElse DateTime.Now < _atteseInSottofondoFino
    End Function

    Friend Sub FineAperturaInSottofondo()
        Try
            If Not _aperturaInSottofondo Then Return
            _atteseInSottofondoFino = DateTime.Now.AddSeconds(5)
            _aperturaInSottofondo = False
            _ultimoInSottofondo = Nothing
            ' Chi era davanti torna l'applicazione corrente, in cima e visibile.
            If ApplicazioniConFinestre().Contains(_applicazioneDavanti) Then PortaApplicazioneInCima(_applicazioneDavanti)
            MostraControlloInCima()
        Catch ex As Exception
            _log.ScriviLog("FineAperturaInSottofondo: " & ex.Message)
        End Try
    End Sub

    ' Durante l'apertura in sottofondo la "cima" per i sottomenu è l'ultima finestra riaperta, non quella davanti.
    Private Function CimaPerApertura() As Control
        If _aperturaInSottofondo AndAlso _ultimoInSottofondo IsNot Nothing AndAlso Not _ultimoInSottofondo.IsDisposed Then Return _ultimoInSottofondo
        Return CimaDellaPila()
    End Function

    ' Finestra di un'applicazione riaperta in sottofondo: resta dietro e le finestre di chi è davanti tornano
    ' in cima alla pila, senza cambiare l'applicazione in cui entrano le prossime finestre riaperte.
    Private Sub TieniInSottofondo(controllo As Control)
        _ultimoInSottofondo = controllo
        SyncLock _lockStack
            Dim davanti As List(Of Control) = _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c IsNot controllo AndAlso ApplicazioneDi(c) = _applicazioneDavanti).ToList()
            For Each c As Control In davanti
                _stackControlli.Remove(c)
            Next
            _stackControlli.AddRange(davanti)
        End SyncLock
    End Sub

#End Region

    Private Function CimaDellaPila() As Control
        For i As Integer = _stackControlli.Count - 1 To 0 Step -1
            Dim c As Control = _stackControlli(i)
            If c IsNot Nothing AndAlso Not c.IsDisposed Then Return c
        Next
        Return Nothing
    End Function

    Private Function NomeNuovaApplicazione(controllo As Control) As String
        Try
            Dim menuIniziale As WGMenuInt = TryCast(_menuIniziale, WGMenuInt)
            If menuIniziale IsNot Nothing AndAlso menuIniziale.VoceSelezionata <> "" Then Return menuIniziale.VoceSelezionata
            Dim descrizione As String = DescrizioneControllo(controllo)
            If descrizione <> "" Then Return descrizione
        Catch ex As Exception
            _log.ScriviLog("NomeNuovaApplicazione: " & ex.Message)
        End Try
        Return "Applicazione " & _prossimaApplicazione
    End Function

    Private Shared Function DescrizioneControllo(controllo As Control) As String
        Try
            Dim menu As WGMenuInt = TryCast(controllo, WGMenuInt)
            If menu IsNot Nothing Then Return menu.TitoloMenu
            Dim campo As WGCampo = TryCast(controllo, WGCampo)
            If campo IsNot Nothing Then Return campo.NomeDocumento
            If Not String.IsNullOrWhiteSpace(controllo.AccessibleName) Then Return controllo.AccessibleName
            Return If(controllo.Text, "")
        Catch ex As Exception
            _logShared.ScriviLog("DescrizioneControllo: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Porta in cima alla pila tutte le finestre dell'applicazione, nel loro ordine.
    Private Sub PortaApplicazioneInCima(applicazione As Integer)
        SyncLock _lockStack
            Dim finestre As List(Of Control) = _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione).ToList()
            If finestre.Count = 0 Then Return
            For Each c As Control In finestre
                _stackControlli.Remove(c)
            Next
            _stackControlli.AddRange(finestre)
            ' Un messaggio aperto resta sempre in cima alla pila, anche se si passa a un'altra applicazione (la scrivania che torna
            ' davanti, per esempio): se finisse sotto le finestre dell'applicazione appena portata davanti, queste lo coprirebbero.
            Dim messaggi As List(Of Control) = _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso _controlliMessaggio.Contains(c)).ToList()
            If messaggi.Count > 0 Then
                For Each m As Control In messaggi
                    _stackControlli.Remove(m)
                Next
                _stackControlli.AddRange(messaggi)
            End If
            _applicazioneCorrente = applicazione
            _ordineUso.Remove(applicazione)
            _ordineUso.Insert(0, applicazione)
        End SyncLock
    End Sub

    ' Applicazioni dalla più recente alla meno recente, per il passaggio rapido e per F10.
    Private ReadOnly _ordineUso As New List(Of Integer)()

    Private Function ApplicazioniPerUso() As List(Of Integer)
        SyncLock _lockStack
            Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
            Dim risultato As List(Of Integer) = _ordineUso.Where(Function(a) aperte.Contains(a)).ToList()
            risultato.AddRange(aperte.Where(Function(a) Not risultato.Contains(a)))
            Return risultato
        End SyncLock
    End Function

    ''' <summary>Il nome dell'applicazione in primo piano.</summary>
    Friend ReadOnly Property NomeApplicazioneCorrente As String
        Get
            Return NomeApplicazione(_applicazioneCorrente)
        End Get
    End Property

    ''' <summary>Vero se c'è un'applicazione aperta con questo nome.</summary>
    Friend Function ApplicazioneAperta(nome As String) As Boolean
        Try
            Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
            SyncLock _lockStack
                Return _nomiApplicazioni.Any(Function(c) aperte.Contains(c.Key) AndAlso String.Equals(c.Value, nome, StringComparison.OrdinalIgnoreCase))
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ApplicazioneAperta: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>I nomi delle applicazioni aperte, dalla più recente (per la ricerca globale).</summary>
    Friend Function NomiApplicazioniAperte() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            For Each applicazione As Integer In ApplicazioniPerUso()
                If applicazione = _applicazioneCorrente Then Continue For
                risultato.Add(NomeApplicazione(applicazione))
            Next
        Catch ex As Exception
            _log.ScriviLog("NomiApplicazioniAperte: " & ex.Message)
        End Try
        Return risultato
    End Function

    ' Ogni menu che compare insegna alla ricerca globale le sue voci, con le scelte per arrivarci.
    Private Sub ImparaMenuPerRicerca(controllo As Control)
        Try
            If Not WGModalita.Smart Then Return
            Dim menu As WGMenuInt = TryCast(controllo, WGMenuInt)
            If menu Is Nothing Then Return
            If menu Is _menuIniziale Then
                WGRicerca.ImparaMenu(New List(Of String)(), menu.VociMenu())
            ElseIf menu.PercorsoMenu IsNot Nothing AndAlso menu.PercorsoMenu.Count > 0 Then
                WGRicerca.ImparaMenu(menu.PercorsoMenu, menu.VociMenu())
            End If
        Catch ex As Exception
            _log.ScriviLog("ImparaMenuPerRicerca: " & ex.Message)
        End Try
    End Sub

    Private _altTabElenco As List(Of Integer)

    ''' <summary>Alt Tab con WinGabriel in primo piano: passa da un'applicazione all'altra, dalla più recente, ad ogni pressione di Tab finché Alt resta premuto. Alt Maiusc Tab va all'indietro.</summary>
    Friend Function AltTabTraApplicazioni(indietro As Boolean) As Boolean
        Try
            If Not ApplicazioniSeparate Then Return False
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False
            Me.BeginInvoke(New Action(Of Boolean)(AddressOf AltTabCicla), indietro)
            Return True
        Catch ex As Exception
            _log.ScriviLog("AltTabTraApplicazioni: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Alt rilasciato: il prossimo Alt Tab riparte dall'applicazione corrente.</summary>
    Friend Sub AltTabFine()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New MethodInvoker(AddressOf AzzeraElencoAltTab))
        Catch ex As Exception
            _log.ScriviLog("AltTabFine: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraElencoAltTab()
        _altTabElenco = Nothing
    End Sub

    Private Sub AltTabCicla(indietro As Boolean)
        Try
            If MessaggioAperto() Then
                sintesivocale.GetInstance().LeggiTesto("Prima rispondere al messaggio")
                Return
            End If
            PulisciApplicazioni()
            Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
            Dim elenco As New List(Of Integer)()
            If _altTabElenco Is Nothing Then
                elenco.AddRange(ApplicazioniPerUso())
            Else
                For Each applicazione As Integer In _altTabElenco
                    If aperte.Contains(applicazione) Then elenco.Add(applicazione)
                Next
            End If
            If elenco.Count < 2 Then
                _altTabElenco = Nothing
                sintesivocale.GetInstance().LeggiTesto("Nessun'altra applicazione aperta")
                Return
            End If
            _altTabElenco = elenco
            Dim posizione As Integer = elenco.IndexOf(_applicazioneCorrente)
            If posizione < 0 Then posizione = 0
            Dim passo As Integer = 1
            If indietro Then passo = -1
            Dim nuova As Integer = (posizione + passo + elenco.Count) Mod elenco.Count
            Dim giro As Boolean = False
            If indietro Then
                giro = nuova > posizione
            Else
                giro = nuova < posizione
            End If
            SuonoCambioApplicazione(Not indietro, giro)
            VaiAllApplicazione(elenco.Item(nuova))
        Catch ex As Exception
            _log.ScriviLog("AltTabCicla: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Control F10: torna all'applicazione usata prima di quella corrente, come Alt Tab premuto una volta.</summary>
    Public Sub VaiAllUltimaApplicazione()
        Try
            If Not ApplicazioniSeparate Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf VaiAllUltimaApplicazione))
                Return
            End If
            If MessaggioAperto() Then Return
            Dim precedente As Integer = ApplicazioniPerUso().Where(Function(a) a <> _applicazioneCorrente).DefaultIfEmpty(-1).First()
            If precedente < 0 Then
                sintesivocale.GetInstance().LeggiTesto("Non ci sono altre applicazioni aperte")
                Return
            End If
            VaiAllApplicazione(precedente)
        Catch ex As Exception
            _log.ScriviLog("VaiAllUltimaApplicazione: " & ex.Message)
        End Try
    End Sub

    ' Per la ripresa della sessione: come riaprire ogni applicazione ("menu:" con le voci da scegliere,
    ' "scrivania:" con l'elemento della scrivania). Le applicazioni senza modo di riaprirle non si ricordano.
    Private ReadOnly _riapertura As New Dictionary(Of Integer, String)()
    Private _riaperturaProssimaApplicazione As String = ""

    ''' <summary>Le applicazioni aperte, dalla meno recente alla più recente, come si riaprono.</summary>
    Friend Function DescriviSessione() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            SyncLock _lockStack
                For Each applicazione As Integer In ApplicazioniPerUso().AsEnumerable().Reverse()
                    If applicazione = ApplicazioneMenuIniziale OrElse EApplicazioneProtetta(applicazione) Then Continue For
                    Dim modo As String = ""
                    If Not _riapertura.TryGetValue(applicazione, modo) OrElse modo = "" Then Continue For
                    ' Per le applicazioni aperte dai menu si riapre il menu più interno raggiunto, non solo il primo.
                    If modo.StartsWith("menu:") Then
                        Dim piuInterno As WGMenuInt = _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione).OfType(Of WGMenuInt)().LastOrDefault(Function(m) m.PercorsoMenu IsNot Nothing AndAlso m.PercorsoMenu.Count > 0)
                        If piuInterno IsNot Nothing Then modo = "menu:" & String.Join(WGScrivania.SeparatoreMenu, piuInterno.PercorsoMenu)
                    End If
                    risultato.Add(modo)
                Next
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("DescriviSessione: " & ex.Message)
        End Try
        Return risultato
    End Function

    ' Da chiamare in RimuoviControllo: se l'applicazione resta senza finestre si torna al menu iniziale.
    Private Sub DopoRimozioneApplicazione(controllo As Control)
        Try
            If Not ApplicazioniSeparate Then Return
            WGSessione.SalvaPresto()
            Dim applicazione As Integer
            Dim destinazione As Integer
            SyncLock _lockStack
                applicazione = ApplicazioneDi(controllo)
                _applicazioneDi.Remove(controllo)
                If controllo Is _menuIniziale Then _menuIniziale = Nothing
                If applicazione = ApplicazioneMenuIniziale Then Return
                If _stackControlli.Any(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione) Then
                    ' Chiuso quello che un collegamento aveva aperto, i menu attraversati per arrivarci si chiudono.
                    If Not _menuDiPassaggio.Contains(controllo) Then ChiudiSeRestanoSoloMenuDiPassaggio(applicazione)
                    Return
                End If
                _nomiApplicazioni.Remove(applicazione)
                _riapertura.Remove(applicazione)
                _ordineUso.Remove(applicazione)
                Dim origine As Integer = ApplicazioneMenuIniziale
                If Not _origineApplicazione.TryGetValue(applicazione, origine) Then origine = ApplicazioneMenuIniziale
                _origineApplicazione.Remove(applicazione)
                If applicazione <> _applicazioneCorrente Then Return
                destinazione = DestinazioneDopoChiusura(origine)
            End SyncLock
            ' Non resta nient'altro: con il menu applicazioni chiuso si va alla scrivania (o alla home),
            ' altrimenti il menu si riapre.
            If destinazione = ApplicazioneMenuIniziale AndAlso Not MenuApplicazioniAperto Then
                ' Si aspetta 400 ms: se nel frattempo si apre un'altra finestra (l'applicazione corrente cambia) la
                ' scrivania resta dov'è.
                RitardaRitornoAllaScrivania(applicazione)
                Return
            End If
            PortaApplicazioneInCima(destinazione)
        Catch ex As Exception
            _log.ScriviLog("DopoRimozioneApplicazione: " & ex.Message)
        End Try
    End Sub

    Private Sub RitardaRitornoAllaScrivania(rimossa As Integer)
        Dim attesa As New System.Windows.Forms.Timer()
        attesa.Interval = RitardoRitornoScrivaniaMs
        AddHandler attesa.Tick, Sub(s, ev)
                                    attesa.Stop()
                                    attesa.Dispose()
                                    Try
                                        If Me.IsDisposed OrElse _applicazioneCorrente <> rimossa Then
                                            Return
                                        End If
                                        If WGScrivania.Vai() Then Return
                                        SegnaMenuApplicazioniAperto()
                                        PortaApplicazioneInCima(ApplicazioneMenuIniziale)
                                        MostraControlloInCima()
                                    Catch ex As Exception
                                        _log.ScriviLog("RitardaRitornoAllaScrivania: " & ex.Message)
                                    End Try
                                End Sub
        attesa.Start()
    End Sub

    ' Chiusa un'applicazione si torna a quella da cui era stata aperta: al menu applicazioni se la si
    ' era scelta da lì, alla scrivania se la si era aperta dalla scrivania. Se quella non c'è più, alla
    ' scrivania (come sul desktop di Windows) e senza scrivania al menu applicazioni.
    Private Function DestinazioneDopoChiusura(origine As Integer) As Integer
        Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
        If origine = ApplicazioneMenuIniziale AndAlso _menuIniziale IsNot Nothing AndAlso Not _menuIniziale.IsDisposed AndAlso MenuApplicazioniAperto Then Return origine
        If origine <> ApplicazioneMenuIniziale AndAlso aperte.Contains(origine) Then Return origine
        For Each coppia As KeyValuePair(Of Integer, String) In _nomiApplicazioni.ToList()
            If ApplicazioniProtette.Contains(coppia.Value) AndAlso aperte.Contains(coppia.Key) Then Return coppia.Key
        Next
        Return ApplicazioneMenuIniziale
    End Function

    Private Sub SuonoCambioApplicazione(avanti As Boolean, giro As Boolean)
        Try
            If _suoni Is Nothing Then _suoni = New WGSuoni()
            If giro Then
                _suoni.LimiteRaggiunto()
            ElseIf avanti Then
                _suoni.FlickAvanti()
            Else
                _suoni.FlickIndietro()
            End If
        Catch ex As Exception
            _log.ScriviLog("SuonoCambioApplicazione: " & ex.Message)
        End Try
    End Sub

    ' Il menu applicazioni chiuso (modalità smart) non conta come applicazione aperta.
    Private Function ApplicazioniConFinestre() As List(Of Integer)
        SyncLock _lockStack
            Dim menuAperto As Boolean = MenuApplicazioniAperto
            Return _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed).Select(Function(c) ApplicazioneDi(c)).Distinct().
                Where(Function(a) a <> ApplicazioneMenuIniziale OrElse menuAperto).OrderBy(Function(a) a).ToList()
        End SyncLock
    End Function

    ''' <summary>Applicazioni aperte oltre al menu iniziale.</summary>
    Public Function ApplicazioniAperte() As Integer
        Try
            If Not ApplicazioniSeparate Then Return 0
            Return ApplicazioniConFinestre().Where(Function(a) a <> ApplicazioneMenuIniziale AndAlso Not EApplicazioneProtetta(a)).Count()
        Catch ex As Exception
            _log.ScriviLog("ApplicazioniAperte: " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>Windows Tab (avanti) e Shift Windows Tab (indietro).</summary>
    Public Sub NavigaApplicazione(avanti As Boolean)
        Try
            If Not ApplicazioniSeparate Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of Boolean)(AddressOf NavigaApplicazione), avanti)
                Return
            End If
            If MessaggioAperto() Then
                sintesivocale.GetInstance().LeggiTesto("Prima rispondere al messaggio")
                Return
            End If
            PulisciApplicazioni()
            Dim applicazioni As List(Of Integer) = ApplicazioniConFinestre()
            If applicazioni.Count < 2 Then
                sintesivocale.GetInstance().LeggiTesto("Nessun'altra applicazione aperta")
                Return
            End If
            Dim posizione As Integer = applicazioni.IndexOf(_applicazioneCorrente)
            If posizione < 0 Then posizione = 0
            Dim nuova As Integer = (posizione + If(avanti, 1, -1) + applicazioni.Count) Mod applicazioni.Count
            ' Suono del passaggio; quando si ricomincia dall'altro capo dell'elenco un suono diverso.
            Dim giro As Boolean = If(avanti, nuova < posizione, nuova > posizione)
            SuonoCambioApplicazione(avanti, giro)
            posizione = nuova
            Dim destinazione As Integer = applicazioni(posizione)
            PortaApplicazioneInCima(destinazione)
            MostraControlloInCima()
            ' Menu ed elenchi, tornando visibili, leggono da soli titolo e voce corrente: un annuncio in più
            ' li farebbe sentire due volte. Per le altre finestre si dice l'applicazione e la finestra.
            Dim cima As Control = ControlloInCima()
            If cima Is Nothing Then Return
            ' Campi e browser si annunciano subito, senza attese: l'utente ha scelto di arrivarci.
            Dim annunciabile As IWGAnnuncioIngresso = TryCast(cima, IWGAnnuncioIngresso)
            If annunciabile IsNot Nothing Then
                annunciabile.AnnunciaIngresso()
                Return
            End If
            ' Menu ed elenchi leggono da soli titolo e voce appena tornano visibili.
            If ContieneElenco(cima) Then Return
            Dim nome As String = ""
            If Not _nomiApplicazioni.TryGetValue(destinazione, nome) Then nome = "Applicazione"
            Dim finestra As String = DescrizioneControllo(cima)
            sintesivocale.GetInstance().LeggiTesto(nome & If(finestra <> "" AndAlso finestra <> nome, ": " & finestra, ""))
        Catch ex As Exception
            _log.ScriviLog("NavigaApplicazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ContieneElenco(controllo As Control) As Boolean
        Try
            If TypeOf controllo Is WGListaView Then Return True
            For Each figlio As Control In controllo.Controls
                If figlio.Visible AndAlso ContieneElenco(figlio) Then Return True
            Next
        Catch ex As Exception
            _logShared.ScriviLog("ContieneElenco: " & ex.Message)
        End Try
        Return False
    End Function


#Region "Chiusura, elenco e ritorno alle applicazioni"

    ' Chiusura di un'applicazione: le sue finestre si chiudono dall'ultima alla prima come se l'utente le
    ' chiudesse, così un testo modificato chiede di essere salvato. Si aspetta che ogni finestra sparisca prima
    ' di passare alla successiva; se l'utente annulla una domanda di salvataggio la chiusura si ferma.
    Private _chiusuraApplicazione As Integer = -1
    Private _finestraInChiusura As Control
    Private _passiChiusura As Integer = 0
    Private _codaChiusura As New List(Of Integer)()
    Private _nomeInChiusura As String = ""
    Private _dopoChiusura As Action
    Private _seChiusuraAnnullata As Action
    Private Const MassimoPassiChiusura As Integer = 60

    Public Const NomeMenuApplicazioni As String = "Menu applicazioni"

    ' Applicazioni che, come il desktop di Windows, chiudendosi chiudono WinGabriel (con le solite domande): la scrivania.
    Public Shared ReadOnly ApplicazioniProtette As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Scrivania", "Home"}

    ''' <summary>Le voci del menu applicazioni (per la home).</summary>
    Friend Function VociMenuApplicazioni() As List(Of String)
        Dim menu As WGMenuInt = TryCast(_menuIniziale, WGMenuInt)
        If menu Is Nothing OrElse menu.IsDisposed Then Return New List(Of String)()
        Return menu.VociMenu()
    End Function

    Private Function EApplicazioneProtetta(applicazione As Integer) As Boolean
        Dim nome As String = ""
        Return _nomiApplicazioni.TryGetValue(applicazione, nome) AndAlso ApplicazioniProtette.Contains(nome)
    End Function

    Private Function NomeApplicazione(applicazione As Integer) As String
        Dim nome As String = ""
        If _nomiApplicazioni.TryGetValue(applicazione, nome) Then Return nome
        Return If(applicazione = ApplicazioneMenuIniziale, NomeMenuApplicazioni, "Applicazione")
    End Function

    Private Function CimaApplicazione(applicazione As Integer) As Control
        SyncLock _lockStack
            For i As Integer = _stackControlli.Count - 1 To 0 Step -1
                Dim c As Control = _stackControlli(i)
                If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione Then Return c
            Next
        End SyncLock
        Return Nothing
    End Function

    ''' <summary>Control F4: chiude l'applicazione corrente.</summary>
    Public Sub ChiudiApplicazioneCorrente()
        Try
            If Not ApplicazioniSeparate Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf ChiudiApplicazioneCorrente))
                Return
            End If
            If _applicazioneCorrente = ApplicazioneMenuIniziale Then
                If ChiudiMenuApplicazioni() Then Return
                sintesivocale.GetInstance().LeggiTesto("Il menu applicazioni non si chiude: non c'è nient'altro aperto. Per uscire da WinGabriel scegliere Esci")
                Return
            End If
            If EApplicazioneProtetta(_applicazioneCorrente) Then
                RichiediUscita()
                Return
            End If
            ChiudiApplicazioni(New List(Of Integer) From {_applicazioneCorrente}, Nothing, Nothing)
        Catch ex As Exception
            _log.ScriviLog("ChiudiApplicazioneCorrente: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude tutte le applicazioni tranne il menu iniziale, poi esegue dopo; se una chiusura viene annullata esegue seAnnullata.</summary>
    Public Sub ChiudiTutteLeApplicazioni(dopo As Action, seAnnullata As Action, Optional anchePreotette As Boolean = True)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Sub() ChiudiTutteLeApplicazioni(dopo, seAnnullata, anchePreotette)))
                Return
            End If
            Dim applicazioni As List(Of Integer) = ApplicazioniConFinestre().Where(Function(a) a <> ApplicazioneMenuIniziale AndAlso (anchePreotette OrElse Not EApplicazioneProtetta(a))).OrderByDescending(Function(a) a).ToList()
            If applicazioni.Count = 0 Then
                If dopo IsNot Nothing Then dopo()
                Return
            End If
            ChiudiApplicazioni(applicazioni, dopo, seAnnullata)
        Catch ex As Exception
            _log.ScriviLog("ChiudiTutteLeApplicazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiApplicazioni(applicazioni As List(Of Integer), dopo As Action, seAnnullata As Action)
        If _chiusuraApplicazione >= 0 Then
            sintesivocale.GetInstance().LeggiTesto("Chiusura già in corso")
            Return
        End If
        _codaChiusura = New List(Of Integer)(applicazioni)
        _dopoChiusura = dopo
        _seChiusuraAnnullata = seAnnullata
        ProssimaApplicazioneDaChiudere()
    End Sub

    Private Sub ProssimaApplicazioneDaChiudere()
        If _codaChiusura.Count = 0 Then
            _chiusuraApplicazione = -1
            _finestraInChiusura = Nothing
            Dim dopo As Action = _dopoChiusura
            _dopoChiusura = Nothing
            _seChiusuraAnnullata = Nothing
            If dopo IsNot Nothing Then dopo()
            Return
        End If
        _chiusuraApplicazione = _codaChiusura(0)
        _codaChiusura.RemoveAt(0)
        _nomeInChiusura = NomeApplicazione(_chiusuraApplicazione)
        _passiChiusura = 0
        PortaApplicazioneInCima(_chiusuraApplicazione)
        PassoChiusura()
    End Sub

    Private Sub PassoChiusura()
        Try
            If _chiusuraApplicazione < 0 Then Return
            Dim applicazione As Integer = _chiusuraApplicazione
            Dim cima As Control = CimaApplicazione(applicazione)
            If cima Is Nothing Then
                sintesivocale.GetInstance().LeggiTesto(_nomeInChiusura & " chiusa")
                _nomiApplicazioni.Remove(applicazione)
                _finestraInChiusura = Nothing
                ProssimaApplicazioneDaChiudere()
                Return
            End If
            _passiChiusura += 1
            If _passiChiusura > MassimoPassiChiusura Then
                AnnullaChiusura("La chiusura di " & _nomeInChiusura & " non riesce")
                Return
            End If
            _finestraInChiusura = cima
            RichiediChiusuraFinestra(cima)
            ControllaFinestraRimasta(cima)
        Catch ex As Exception
            _log.ScriviLog("PassoChiusura: " & ex.Message)
            AnnullaChiusura("")
        End Try
    End Sub

    ' Se la finestra non si è chiusa e non ha aperto una domanda sopra di sé, si toglie d'ufficio.
    Private Async Sub ControllaFinestraRimasta(finestra As Control)
        Try
            Await Threading.Tasks.Task.Delay(1500)
            If _chiusuraApplicazione < 0 OrElse _finestraInChiusura IsNot finestra Then Return
            If finestra.IsDisposed OrElse Not GestisceControllo(finestra) Then Return
            If CimaApplicazione(_chiusuraApplicazione) IsNot finestra Then Return
            RimuoviControllo(finestra)
            Try
                finestra.Dispose()
            Catch exDispose As Exception
                _log.ScriviLog("ControllaFinestraRimasta (dispose): " & exDispose.Message)
            End Try
            PassoChiusura()
        Catch ex As Exception
            _log.ScriviLog("ControllaFinestraRimasta: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RichiediChiusuraFinestra(finestra As Control)
        Try
            Dim chiudibile As IWGChiudibile = TryCast(finestra, IWGChiudibile)
            If chiudibile IsNot Nothing Then
                chiudibile.RichiediChiusura()
                Return
            End If
            Dim modulo As Form = TryCast(finestra, Form)
            If modulo IsNot Nothing Then
                modulo.Close()
                Return
            End If
            Dim contenitore As WGContenitore = Istanza
            If contenitore IsNot Nothing Then contenitore.RimuoviControllo(finestra)
            finestra.Dispose()
        Catch ex As Exception
            _logShared.ScriviLog("RichiediChiusuraFinestra: " & ex.Message)
        End Try
    End Sub

    ' Da RimuoviControllo: si prosegue quando sparisce la finestra richiesta; se sparisce invece una domanda
    ' (per esempio "salvare le modifiche?") e la finestra resta, l'utente ha annullato e ci si ferma.
    Private Sub ProseguiChiusuraDopoRimozione(controllo As Control)
        Try
            If _chiusuraApplicazione < 0 Then Return
            If controllo Is _finestraInChiusura Then
                Me.BeginInvoke(New MethodInvoker(AddressOf PassoChiusura))
                Return
            End If
            If ApplicazioneDi(controllo) = _chiusuraApplicazione Then VerificaAnnullamento(_finestraInChiusura)
        Catch ex As Exception
            _log.ScriviLog("ProseguiChiusuraDopoRimozione: " & ex.Message)
        End Try
    End Sub

    Private Async Sub VerificaAnnullamento(finestra As Control)
        Try
            Await Threading.Tasks.Task.Delay(800)
            If _chiusuraApplicazione < 0 OrElse _finestraInChiusura IsNot finestra OrElse finestra Is Nothing Then Return
            If finestra.IsDisposed OrElse Not GestisceControllo(finestra) Then Return
            If CimaApplicazione(_chiusuraApplicazione) IsNot finestra Then Return
            AnnullaChiusura("Chiusura annullata")
        Catch ex As Exception
            _log.ScriviLog("VerificaAnnullamento: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaChiusura(messaggio As String)
        _chiusuraApplicazione = -1
        _finestraInChiusura = Nothing
        _codaChiusura.Clear()
        _dopoChiusura = Nothing
        Dim seAnnullata As Action = _seChiusuraAnnullata
        _seChiusuraAnnullata = Nothing
        If messaggio <> "" Then sintesivocale.GetInstance().LeggiTesto(messaggio)
        If seAnnullata IsNot Nothing Then seAnnullata()
    End Sub

    Private Sub VaiAllApplicazione(applicazione As Integer, Optional annuncia As Boolean = True)
        PortaApplicazioneInCima(applicazione)
        MostraControlloInCima()
        ' Con un messaggio aperto davanti non si annuncia l'applicazione: coprirebbe la voce del messaggio.
        If annuncia AndAlso Not MessaggioAperto() Then AnnunciaApplicazione(applicazione)
    End Sub

    ''' <summary>
    ''' Vero se la finestra non deve venire davanti: appartiene già a un'applicazione che non è quella in
    ''' primo piano e nessuno ha chiesto di andarci (per andarci si usa VaiAllApplicazioneDi). Capita quando
    ''' un'applicazione dietro aggiorna un suo elenco o ridisegna la sua finestra: la finestra entra nella
    ''' pila, in cima a quelle della sua applicazione, e resta nascosta.
    ''' </summary>
    Private Function RestaDietro(controllo As Control) As Boolean
        Try
            If Not ApplicazioniSeparate OrElse _menuIniziale Is Nothing OrElse _aperturaInSottofondo Then Return False
            If controllo Is _menuIniziale OrElse EControlloMessaggio(controllo) Then Return False
            SyncLock _lockStack
                ' Un collegamento appena scelto (nome in attesa) può riportare davanti una finestra esistente.
                If _nomeProssimaApplicazione <> "" AndAlso (DateTime.Now - _istanteNomeProssimaApplicazione).TotalSeconds <= 10 Then Return False
                Dim sua As Integer
                If Not _applicazioneDi.TryGetValue(controllo, sua) OrElse sua = _applicazioneCorrente Then Return False
                ' Se davanti non c'è più niente (l'applicazione corrente si è appena chiusa) la finestra si mostra.
                If Not _stackControlli.Any(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c IsNot controllo AndAlso c.Visible AndAlso ApplicazioneDi(c) = _applicazioneCorrente) Then Return False
                _stackControlli.Remove(controllo)
                Dim ultima As Integer = _stackControlli.FindLastIndex(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = sua)
                _stackControlli.Insert(ultima + 1, controllo)
            End SyncLock
            controllo.Visible = False
            controllo.Dock = DockStyle.None
            controllo.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
            If Not Me.Controls.Contains(controllo) Then Me.Controls.Add(controllo)
            controllo.Bounds = New Rectangle(0, 0, LarghezzaContenutoDisponibile(), AltezzaContenutoDisponibile())
            controllo.Visible = False
            Return True
        Catch ex As Exception
            _log.ScriviLog("RestaDietro: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaApplicazione(applicazione As Integer)
        Dim cima As Control = ControlloInCima()
        If cima Is Nothing Then Return
        Dim annunciabile As IWGAnnuncioIngresso = TryCast(cima, IWGAnnuncioIngresso)
        If annunciabile IsNot Nothing Then
            annunciabile.AnnunciaIngresso()
            Return
        End If
        If ContieneElenco(cima) Then Return
        Dim nome As String = NomeApplicazione(applicazione)
        Dim finestra As String = DescrizioneControllo(cima)
        sintesivocale.GetInstance().LeggiTesto(nome & If(finestra <> "" AndAlso finestra <> nome, ": " & finestra, ""))
    End Sub

    ''' <summary>
    ''' Dal menu iniziale: se la voce scelta corrisponde a un'applicazione già aperta ci si torna invece di
    ''' aprirne un'altra uguale. Restituisce True se l'applicazione c'era.
    ''' </summary>
    Friend Function TornaAllApplicazioneAperta(nome As String) As Boolean
        Try
            If Not ApplicazioniSeparate OrElse String.IsNullOrWhiteSpace(nome) Then Return False
            Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
            For Each coppia As KeyValuePair(Of Integer, String) In _nomiApplicazioni.ToList()
                If coppia.Key = ApplicazioneMenuIniziale OrElse Not aperte.Contains(coppia.Key) Then Continue For
                If Not String.Equals(coppia.Value, nome, StringComparison.OrdinalIgnoreCase) Then Continue For
                VaiAllApplicazione(coppia.Key)
                Return True
            Next
        Catch ex As Exception
            _log.ScriviLog("TornaAllApplicazioneAperta: " & ex.Message)
        End Try
        Return False
    End Function

    Friend Function EMenuIniziale(controllo As Control) As Boolean
        Return controllo IsNot Nothing AndAlso controllo Is _menuIniziale
    End Function

    ' Davanti c'è un campo di testo (anche dentro un'altra finestra, come l'editor): lì il rotore serve.
    Private Function DavantiCampoDiTesto() As Boolean
        Try
            Return ContieneCampoDiTesto(ControlloInCima(), 0)
        Catch ex As Exception
            _log.ScriviLog("DavantiCampoDiTesto: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function ContieneCampoDiTesto(controllo As Control, profondita As Integer) As Boolean
        If controllo Is Nothing OrElse controllo.IsDisposed OrElse profondita > 6 Then Return False
        If TypeOf controllo Is WGCampo OrElse TypeOf controllo Is WGCampoTesto OrElse TypeOf controllo Is CampiFormInterno OrElse TypeOf controllo Is WGCampiMovimento Then Return True
        For Each figlio As Control In controllo.Controls
            If figlio.Visible AndAlso ContieneCampoDiTesto(figlio, profondita + 1) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Centro notifiche, che sta in una libreria a parte: lo collega il programma principale.</summary>
    Public Shared Property ApriCentroNotifiche As Action

    ''' <summary>Il manuale (libreria WGGuida), collegato dal programma principale: l'indice e un capitolo per chiave.</summary>
    Public Shared Property ApriManualeAzione As Action
    Public Shared Property ApriCapitoloManuale As Action(Of String)

    Public Shared ReadOnly Property ManualeDisponibile As Boolean
        Get
            Return ApriManualeAzione IsNot Nothing
        End Get
    End Property

    ''' <summary>Control F1 e la voce Manuale: il manuale, come applicazione a sé.</summary>
    Public Shared Sub ApriManuale()
        Try
            Dim c As WGContenitore = Istanza
            If c IsNot Nothing AndAlso c.InvokeRequired Then
                c.BeginInvoke(New MethodInvoker(AddressOf ApriManuale))
                Return
            End If
            If c IsNot Nothing AndAlso (c.MessaggioAperto() OrElse c.EsistonoAtteseAttive()) Then Return
            Dim apri As Action = ApriManualeAzione
            If apri IsNot Nothing Then apri()
        Catch ex As Exception
            _logShared.ScriviLog("ApriManuale: " & ex.Message)
        End Try
    End Sub

    ''' <summary>F1 di scrivania e home: il loro capitolo del manuale. Falso se il manuale non c'è.</summary>
    Public Shared Function ApriCapitolo(chiave As String) As Boolean
        Try
            Dim apri As Action(Of String) = ApriCapitoloManuale
            If apri Is Nothing Then Return False
            apri(chiave)
            Return True
        Catch ex As Exception
            _logShared.ScriviLog("ApriCapitolo: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Windows N e tre dita in giù dalla barra in alto: il centro notifiche.</summary>
    Public Sub MostraCentroNotifiche()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf MostraCentroNotifiche))
                Return
            End If
            If MessaggioAperto() OrElse EsistonoAtteseAttive() Then Return
            Dim apri As Action = ApriCentroNotifiche
            If apri IsNot Nothing Then apri()
        Catch ex As Exception
            _log.ScriviLog("MostraCentroNotifiche: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' F10 e tre tocchi con due dita: il multitasking, le applicazioni aperte dalla più recente. Invio o doppio
    ''' tocco vanno all'applicazione; un colpo dal basso verso l'alto dice "Chiudi" e il doppio tocco la chiude
    ''' (Chiudi è l'ultima azione, come nel menu opzioni); poi l'elenco si riapre con le applicazioni rimaste.
    ''' </summary>
    Public Sub MostraElencoApplicazioni()
        Try
            If Not ApplicazioniSeparate Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf MostraElencoApplicazioni))
                Return
            End If
            If MessaggioAperto() Then
                sintesivocale.GetInstance().LeggiTesto("Prima rispondere al messaggio")
                Return
            End If
            ' Dalla più recente, come Alt Tab: la selezione parte sulla precedente, così F10 e Invio ci riportano.
            Dim applicazioni As List(Of Integer) = ApplicazioniPerUso()
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            Dim daLeggere As Integer = NotificheDaLeggere()
            lista.impostaTitolo("Multitasking")
            Dim numeroVoci As Integer = 0
            Dim notifica As String = VoceUltimaNotifica
            If notifica <> "" Then
                lista.aggiungiElemento(notifica, "notifica")
                numeroVoci += 1
            End If
            Dim posizioneCorrente As Integer = 0
            Dim posizionePrecedente As Integer = -1
            For Each applicazione As Integer In applicazioni
                Dim cima As Control = CimaApplicazione(applicazione)
                Dim finestra As String = If(cima IsNot Nothing, DescrizioneControllo(cima), "")
                Dim nome As String = NomeApplicazione(applicazione)
                Dim voce As String = nome & If(finestra <> "" AndAlso finestra <> nome, ": " & finestra, "")
                If applicazione = _applicazioneCorrente Then
                    voce &= ", applicazione corrente"
                    posizioneCorrente = numeroVoci
                ElseIf posizionePrecedente < 0 Then
                    posizionePrecedente = numeroVoci
                End If
                lista.aggiungiElemento(voce, "app|" & applicazione)
                numeroVoci += 1
            Next
            Dim applicazioneScelta As Func(Of Integer) = Function()
                                                            Dim id As String = lista.ottieniElementi().FirstOrDefault()
                                                            Dim numero As Integer = -1
                                                            If id IsNot Nothing AndAlso id.StartsWith("app|") Then Integer.TryParse(id.Substring(4), numero)
                                                            Return numero
                                                        End Function
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      lista.abbandona()
                                                      If id = "notifica" Then
                                                          Dim azione As Action = AzioneUltimaNotifica
                                                          If azione IsNot Nothing Then azione()
                                                          Return
                                                      End If
                                                      Dim numero As Integer
                                                      If id IsNot Nothing AndAlso id.StartsWith("app|") AndAlso Integer.TryParse(id.Substring(4), numero) Then VaiAllApplicazione(numero)
                                                  End Sub
            lista.aggiungiOpzione("Chiudi tutte le applicazioni tranne il menu applicazioni, la home e la scrivania", Sub(s, e)
                                                                                                        lista.abbandona()
                                                                                                        ChiudiTutteLeApplicazioni(Sub() sintesivocale.GetInstance().LeggiTesto("Applicazioni chiuse"), Nothing, False)
                                                                                                    End Sub)
            ' Ultima azione: un solo colpo verso l'alto la fa dire, il doppio tocco la esegue.
            lista.aggiungiOpzione("Chiudi", Sub(s, e)
                                                Dim numero As Integer = applicazioneScelta()
                                                If numero < 0 Then Return
                                                If EApplicazioneProtetta(numero) Then
                                                    lista.abbandona()
                                                    RichiediUscita()
                                                ElseIf numero = ApplicazioneMenuIniziale Then
                                                    lista.abbandona()
                                                    If ChiudiMenuApplicazioni() Then
                                                        sintesivocale.GetInstance().LeggiTesto(NomeMenuApplicazioni & " chiuso")
                                                    Else
                                                        sintesivocale.GetInstance().LeggiTesto(NomeMenuApplicazioni & " non si chiude: non c'è nient'altro aperto")
                                                    End If
                                                Else
                                                    Dim nome As String = NomeApplicazione(numero)
                                                    lista.abbandona()
                                                    ChiudiApplicazioni(New List(Of Integer) From {numero},
                                                                       Sub()
                                                                           sintesivocale.GetInstance().LeggiTesto(nome & " chiusa")
                                                                           If ApplicazioniAperte() > 0 Then MostraElencoApplicazioni()
                                                                       End Sub, Nothing)
                                                End If
                                            End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            ' L'elenco appartiene all'applicazione in cui è stato aperto, non ne crea una nuova.
            If lista.Interno IsNot Nothing Then _applicazioneDi(lista.Interno) = _applicazioneCorrente
            lista.avvia()
            Dim posizioneIniziale As Integer = If(posizionePrecedente >= 0, posizionePrecedente, posizioneCorrente)
            If posizioneIniziale > 0 Then lista.selezionaElemento(posizioneIniziale)
        Catch ex As Exception
            _log.ScriviLog("MostraElencoApplicazioni: " & ex.Message)
        End Try
    End Sub

    Private Const ChiaveConfermaUscita As String = "conferma_uscita_applicazioni"

    ''' <summary>
    ''' Chiedere conferma uscendo se ci sono applicazioni aperte (predefinito: no; le applicazioni si chiudono
    ''' comunque una alla volta, con le domande di salvataggio dei testi modificati).
    ''' </summary>
    Public Shared Property ConfermaUscitaConApplicazioni As Boolean
        Get
            Try
                Return New WGConfig().LeggiValore(ChiaveConfermaUscita) = "1"
            Catch ex As Exception
                _logShared.ScriviLog("ConfermaUscitaConApplicazioni: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Dim configurazione As New WGConfig()
                configurazione.AggiungiValore(ChiaveConfermaUscita, If(value, "1", "0"))
            Catch ex As Exception
                _logShared.ScriviLog("ConfermaUscitaConApplicazioni scrittura: " & ex.Message)
            End Try
        End Set
    End Property

    ''' <summary>
    ''' I nomi delle applicazioni aperte, tranne menu applicazioni, scrivania e home (per l'uscita), comprese
    ''' quelle riaperte in sottofondo; dalla più recente.
    ''' </summary>
    Public Function ElencoApplicazioniAperte() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If Not ApplicazioniSeparate Then Return risultato
            PulisciApplicazioni()
            For Each applicazione As Integer In ApplicazioniPerUso()
                If applicazione = ApplicazioneMenuIniziale OrElse EApplicazioneProtetta(applicazione) Then Continue For
                risultato.Add(NomeApplicazione(applicazione))
            Next
        Catch ex As Exception
            _log.ScriviLog("ElencoApplicazioniAperte: " & ex.Message)
        End Try
        Return risultato
    End Function

    ''' <summary>Uscita da WinGabriel come dal menu applicazioni (Esc), con tutte le sue domande.</summary>
    Public Sub RichiediUscita()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf RichiediUscita))
                Return
            End If
            Dim menu As IWGChiudibile = TryCast(_menuIniziale, IWGChiudibile)
            If menu Is Nothing Then Return
            SegnaMenuApplicazioniAperto()
            PortaApplicazioneInCima(ApplicazioneMenuIniziale)
            MostraControlloInCima()
            menu.RichiediChiusura()
        Catch ex As Exception
            _log.ScriviLog("RichiediUscita: " & ex.Message)
        End Try
    End Sub

    ' Il conteggio viene dal centro notifiche, che sta in una libreria a parte: lo collega il programma principale.
    Public Shared Property ContaNotificheDaLeggere As Func(Of Integer)

    Public Shared Function NotificheDaLeggere() As Integer
        Try
            Dim conta As Func(Of Integer) = ContaNotificheDaLeggere
            Return If(conta Is Nothing, 0, conta())
        Catch ex As Exception
            _logShared.ScriviLog("NotificheDaLeggere: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Const TestoGuidaApplicazioni As String =
        "Gestione delle applicazioni in WinGabriel." & vbCrLf &
        "All'avvio ci si trova sulla scrivania. Ogni volta che apri qualcosa, l'applicazione entra in funzione e resta attiva in sottofondo anche mentre fai altro, ma la finestra è sempre la stessa." & vbCrLf &
        "Solo in modalità smart si preme il tasto Windows per aprire il menu applicazioni, che a quel punto si comporta come una normale app: all'avvio non è aperto, e puoi chiuderlo premendo Esc, Control F4 o di nuovo il tasto Windows, tornando subito a quello che stavi facendo prima." & vbCrLf &
        "Con Control Alt D passi subito alla scrivania." & vbCrLf &
        "Per passare da un'applicazione all'altra usa Windows Tab per andare avanti e Shift Windows Tab per andare indietro. Ogni app ti riporterà esattamente al punto in cui la stavi usando, e sentirai un suono specifico quando il giro ricomincia da capo." & vbCrLf &
        "F10 apre il multitasking, elencandoti le applicazioni aperte dalla più recente. In cima trovi l'ultima notifica, ma la selezione si sposta in automatico sull'applicazione precedente, così basta premere Invio per tornarci. Da lì con Invio apri un'app, mentre dal menu opzioni puoi chiuderne una in particolare o tutte insieme. Se usi lo schermo touch, scorri col dito verso l'alto per sentire Chiudi e fai doppio tocco per chiudere l'applicazione." & vbCrLf &
        "In modalità tablet, al posto della scrivania si usa la home. Lì si trovano solo le applicazioni, raggruppate dieci per pagina: per sfogliarle basta scorrere con tre dita verso destra o verso sinistra." & vbCrLf &
        "Se vuoi fare un salto rapidissimo all'applicazione usata un attimo prima, senza passare per i menu, premi Control F10." & vbCrLf &
        "Ecco alcune scorciatoie per il touch: scorrendo con due dita verso l'alto vai alla home, alla scrivania o al menu applicazioni. Due dita a destra o a sinistra ti fanno passare da un'app all'altra, mentre tre tocchi con due dita aprono il multitasking. Tre dita verso il basso aprono il centro notifiche e verso l'alto il centro di controllo. Attenzione però: nei campi di testo e nel browser tre dita in su e in giù muovono il rotore, quindi per aprire i due centri dovrai partire dalla barra in alto." & vbCrLf &
        "Nelle impostazioni, alla voce Sistema, puoi decidere alcune cose utili: se far riaprire in automatico all'avvio le app che avevi lasciato in esecuzione, gestire il non disturbare e personalizzare i suoni delle notifiche per ogni singola applicazione." & vbCrLf &
        "Control F4 chiude l'applicazione corrente, chiedendoti di salvare se ci sono testi modificati. Se lo premi mentre sei sulla scrivania, o se usi Esc, uscirai definitivamente da WinGabriel." & vbCrLf &
        "Usa Control Tab per spostarti tra i documenti aperti nell'editor, o per passare da una scheda all'altra quando navighi col browser." & vbCrLf &
        "Tutti gli avvisi di posta, agenda, browser o delle altre app si ritrovano nel centro notifiche, che si apre dal menu opzioni o dalla scrivania. Se un'app che sta in sottofondo ti manda una notifica, la sentirai annunciare col nome dell'applicazione accompagnato da un suono." & vbCrLf &
        "Quando decidi di uscire da WinGabriel, il sistema si occupa di chiudere le app rimaste aperte una per una, facendoti le domande di salvataggio necessarie."
    ''' <summary>Guida dei tasti delle applicazioni, in un campo di sola lettura.</summary>
    Public Shared Sub MostraGuidaApplicazioni()
        Try
            If ApriCapitolo("applicazioni") Then Return
            Dim campo As New WGCampo()
            campo.init("Guida delle applicazioni", TestoGuidaApplicazioni, True, True)
            campo.avvia()
        Catch ex As Exception
            _logShared.ScriviLog("MostraGuidaApplicazioni: " & ex.Message)
        End Try
    End Sub

    ' Le finestre chiuse non devono restare nell'elenco delle applicazioni: si tolgono con le altre pulizie.
    Friend Sub PulisciApplicazioni()
        Try
            SyncLock _lockStack
                For Each c As Control In _applicazioneDi.Keys.ToList()
                    If c Is Nothing OrElse c.IsDisposed Then _applicazioneDi.Remove(c)
                Next
                Dim vive As HashSet(Of Integer) = New HashSet(Of Integer)(_applicazioneDi.Values)
                vive.Add(ApplicazioneMenuIniziale)
                For Each id As Integer In _nomiApplicazioni.Keys.ToList()
                    If Not vive.Contains(id) AndAlso id <> _chiusuraApplicazione Then _nomiApplicazioni.Remove(id)
                Next
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PulisciApplicazioni: " & ex.Message)
        End Try
    End Sub

    ' Ultima notifica da mostrare in cima all'elenco delle applicazioni: la imposta il centro notifiche.
    Public Shared Property VoceUltimaNotifica As String = ""
    Public Shared Property AzioneUltimaNotifica As Action

    ''' <summary>Nome dell'applicazione a cui appartiene la finestra (per le notifiche), "" se non si sa.</summary>
    Public Function NomeApplicazioneDi(controllo As Control) As String
        Try
            If controllo Is Nothing OrElse Not ApplicazioniSeparate Then Return ""
            Dim applicazione As Integer
            If Not _applicazioneDi.TryGetValue(controllo, applicazione) Then Return ""
            Return NomeApplicazione(applicazione)
        Catch ex As Exception
            _log.ScriviLog("NomeApplicazioneDi: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Porta davanti l'applicazione a cui appartiene la finestra. False se la finestra non c'è più.</summary>
    Public Function VaiAllApplicazioneDi(controllo As Control, Optional annuncia As Boolean = True) As Boolean
        Try
            If controllo Is Nothing OrElse controllo.IsDisposed OrElse Not GestisceControllo(controllo) Then Return False
            If Not ApplicazioniSeparate Then
                MostraControllo(controllo)
                Return True
            End If
            VaiAllApplicazione(ApplicazioneDi(controllo), annuncia)
            Return True
        Catch ex As Exception
            _log.ScriviLog("VaiAllApplicazioneDi: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Applicazione in primo piano, per sapere se una notifica arriva da un'applicazione in sottofondo.</summary>
    Public Function EApplicazioneCorrente(controllo As Control) As Boolean
        If controllo Is Nothing OrElse Not ApplicazioniSeparate Then Return True
        Return ApplicazioneDi(controllo) = _applicazioneCorrente
    End Function

    Friend Function ESulMenuApplicazioni() As Boolean
        Return ApplicazioniSeparate AndAlso _menuIniziale IsNot Nothing AndAlso _applicazioneCorrente = ApplicazioneMenuIniziale
    End Function

    ''' <summary>Va al menu iniziale, come il tasto Windows.</summary>
    Public Sub VaiAlMenuIniziale()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf VaiAlMenuIniziale))
                Return
            End If
            If MessaggioAperto() Then Return
            If Not ApplicazioniSeparate OrElse _menuIniziale Is Nothing Then Return
            If _applicazioneCorrente = ApplicazioneMenuIniziale AndAlso MenuApplicazioniAperto Then
                sintesivocale.GetInstance().LeggiTesto(NomeMenuApplicazioni)
                Return
            End If
            SegnaMenuApplicazioniAperto()
            VaiAllApplicazione(ApplicazioneMenuIniziale)
        Catch ex As Exception
            _log.ScriviLog("VaiAlMenuIniziale: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Porta davanti la finestra indicata come applicazione a sé (la scrivania): se è già aperta ci si torna,
    ''' altrimenti il chiamante la crea e la mostra con creaEMostra.
    ''' </summary>
    Public Function VaiAllApplicazioneConNome(nome As String) As Boolean
        Try
            Dim aperte As List(Of Integer) = ApplicazioniConFinestre()
            For Each coppia As KeyValuePair(Of Integer, String) In _nomiApplicazioni.ToList()
                If aperte.Contains(coppia.Key) AndAlso String.Equals(coppia.Value, nome, StringComparison.OrdinalIgnoreCase) Then
                    VaiAllApplicazione(coppia.Key)
                    Return True
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog("VaiAllApplicazioneConNome: " & ex.Message)
        End Try
        Return False
    End Function

    ' La prossima finestra mostrata apre un'applicazione nuova con questo nome, anche se non viene dal menu iniziale.
    Private _nomeProssimaApplicazione As String = ""
    Private _istanteNomeProssimaApplicazione As DateTime = DateTime.MinValue

    ' Vale per la prossima finestra mostrata entro pochi secondi: se l'apertura non mostra nulla il nome non resta appeso.
    ''' <param name="riapertura">Come riaprirla alla ripresa della sessione ("menu:voci" o "scrivania:id"); vuoto per non ricordarla.</param>
    Public Sub ApriComeNuovaApplicazione(nome As String, Optional riapertura As String = "")
        ' In sottofondo la nuova applicazione parte dal menu iniziale, anche se davanti c'è altro.
        If _aperturaInSottofondo Then _ultimoInSottofondo = _menuIniziale
        _nomeProssimaApplicazione = If(nome, "")
        _riaperturaProssimaApplicazione = If(riapertura, "")
        _istanteNomeProssimaApplicazione = DateTime.Now
    End Sub

    ' Un menu aperto scegliendo una voce del menu applicazioni (o di un suo sottomenu) ricorda le voci per arrivarci,
    ' così dal menu opzioni la voce si può mettere sulla scrivania.
    Private Sub AssegnaPercorsoMenu(controllo As Control)
        Try
            Dim menu As WGMenuInt = TryCast(controllo, WGMenuInt)
            If menu Is Nothing OrElse menu.PercorsoMenu IsNot Nothing OrElse controllo Is _menuIniziale Then Return
            Dim cima As WGMenuInt
            SyncLock _lockStack
                If _stackControlli.Contains(controllo) Then Return
                cima = TryCast(CimaPerApertura(), WGMenuInt)
            End SyncLock
            If cima Is Nothing OrElse cima.EsegueOpzione Then Return
            Dim voce As String = cima.VoceSelezionata
            If voce = "" OrElse Not cima.ContieneVoce(voce) Then Return
            If cima Is _menuIniziale Then
                menu.PercorsoMenu = New List(Of String) From {voce}
            ElseIf cima.PercorsoMenu IsNot Nothing Then
                Dim percorso As New List(Of String)(cima.PercorsoMenu)
                percorso.Add(voce)
                menu.PercorsoMenu = percorso
            End If
        Catch ex As Exception
            _log.ScriviLog("AssegnaPercorsoMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Ripete le scelte fatte nel menu applicazioni e nei sottomenu (collegamento della scrivania a una voce di menu).
    ''' Falso se una voce non c'è più.
    ''' </summary>
    Friend Async Function EseguiPercorsoMenuAsync(voci As List(Of String)) As Task(Of Boolean)
        _percorsiInCorso += 1
        Dim riuscito As Boolean = False
        Try
            ' Le voci "Manuale" e "Informazioni sul programma" del menu principale ora stanno nel menu Aiuto: le scorciatoie
            ' salvate prima (home, scrivania) puntano ancora ai vecchi nomi.
            If voci IsNot Nothing AndAlso voci.Count > 0 Then
                If String.Equals(voci(0), "Manuale", StringComparison.OrdinalIgnoreCase) Then
                    voci = New List(Of String)({"Aiuto", "Manuale"})
                ElseIf String.Equals(voci(0), "Informazioni sul programma", StringComparison.OrdinalIgnoreCase) Then
                    voci = New List(Of String)({"Aiuto", "Informazioni"})
                End If
            End If
            riuscito = Await PercorriMenuAsync(voci)
        Catch ex As Exception
            _log.ScriviLog("EseguiPercorsoMenuAsync: " & ex.Message)
        Finally
            _menuDiPassaggioNascosti = False
            _percorsiInCorso = Math.Max(0, _percorsiInCorso - 1)
        End Try
        Try
            If Not riuscito Then
                ' I menu attraversati prima della voce che non c'è più non devono restare aperti, nascosti.
                ChiudiSeRestanoSoloMenuDiPassaggio(_applicazioneCorrente)
            End If
            ' Se la voce non ha aperto niente, in cima può essere rimasto un menu di passaggio nascosto:
            ' la rivelazione mostra quello che c'è in cima, come dopo ogni chiusura.
            Dim cima As Control
            SyncLock _lockStack
                cima = CimaDellaPila()
            End SyncLock
            If cima IsNot Nothing AndAlso Not cima.Visible AndAlso Not _aperturaInSottofondo Then
                _tmrRivelazione.Stop()
                _tmrRivelazione.Start()
            End If
        Catch ex As Exception
            _log.ScriviLog("EseguiPercorsoMenuAsync fine: " & ex.Message)
        End Try
        Return riuscito
    End Function

    ' Lavori appena partiti la cui attesa non è ancora comparsa (WGProcessoInCorso.EseguiAsync): finché ce
    ' ne sono, la finestra di sotto non viene rimessa davanti.
    Private _rivelazioniRimandate As Integer = 0

    Friend Sub RimandaRivelazione(attiva As Boolean)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of Boolean)(AddressOf RimandaRivelazione), attiva)
                Return
            End If
            _rivelazioniRimandate = Math.Max(0, _rivelazioniRimandate + If(attiva, 1, -1))
        Catch ex As Exception
            _log.ScriviLog("RimandaRivelazione: " & ex.Message)
        End Try
    End Sub

    ' Percorsi in corso (i menu di passaggio non vanno rivelati) e menu da tenere nascosti: quelli aperti
    ' dalle voci che non sono l'ultima del percorso.
    Private _percorsiInCorso As Integer = 0
    Private _menuDiPassaggioNascosti As Boolean = False

    Private Async Function PercorriMenuAsync(voci As List(Of String)) As Task(Of Boolean)
        Try
            Dim menu As WGMenuInt = TryCast(_menuIniziale, WGMenuInt)
            If menu Is Nothing OrElse voci Is Nothing OrElse voci.Count = 0 Then Return False
            For i As Integer = 0 To voci.Count - 1
                If menu Is Nothing OrElse menu.IsDisposed OrElse Not menu.ContieneVoce(voci(i)) Then Return False
                If i > 0 Then menu.PercorsoMenu = voci.Take(i).ToList()
                ' I menu attraversati (Web per arrivare a YouTube) sono di passaggio: chiuso quello che il
                ' collegamento ha aperto, si chiudono anche loro e si torna da dove si era partiti.
                If menu IsNot _menuIniziale Then
                    SyncLock _lockStack
                        _menuDiPassaggio.Add(menu)
                    End SyncLock
                End If
                ' Si vede e si sente solo la destinazione: i menu aperti dalle voci precedenti restano nascosti.
                _menuDiPassaggioNascosti = i < voci.Count - 1
                Dim eseguita As Boolean
                Try
                    eseguita = menu.EseguiVoce(voci(i))
                Finally
                    _menuDiPassaggioNascosti = False
                End Try
                If Not eseguita Then Return False
                If i = voci.Count - 1 Then Exit For
                ' Aspetta il sottomenu aperto dalla voce.
                Dim precedente As WGMenuInt = menu
                menu = Nothing
                Dim limite As DateTime = DateTime.Now.AddSeconds(8)
                While DateTime.Now < limite
                    Await Task.Delay(50)
                    Dim cima As WGMenuInt
                    SyncLock _lockStack
                        cima = TryCast(CimaPerApertura(), WGMenuInt)
                    End SyncLock
                    If cima IsNot Nothing AndAlso cima IsNot precedente AndAlso cima IsNot _menuIniziale AndAlso cima.ContieneVoce(voci(i + 1)) Then
                        menu = cima
                        Exit While
                    End If
                End While
            Next
            Return True
        Catch ex As Exception
            _log.ScriviLog("PercorriMenuAsync: " & ex.Message)
            Return False
        End Try
    End Function

    ' Menu aperti solo per arrivare alla voce di un collegamento: non sono una meta a cui tornare.
    Private ReadOnly _menuDiPassaggio As New HashSet(Of Control)()

    ' Se nell'applicazione restano solo menu di passaggio, si chiudono: l'applicazione finisce e si torna
    ' all'origine (la scrivania, la ricerca...). Vero se è stata avviata la chiusura.
    Private Function ChiudiSeRestanoSoloMenuDiPassaggio(applicazione As Integer) As Boolean
        Dim rimasti As List(Of Control)
        SyncLock _lockStack
            _menuDiPassaggio.RemoveWhere(Function(c) c Is Nothing OrElse c.IsDisposed)
            rimasti = _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione).ToList()
            If rimasti.Count = 0 OrElse Not rimasti.All(Function(c) _menuDiPassaggio.Contains(c)) Then Return False
        End SyncLock
        ' Fuori dalla rimozione in corso, per non chiudere finestre mentre la pila cambia.
        Me.BeginInvoke(New MethodInvoker(Sub()
                                             For i As Integer = rimasti.Count - 1 To 0 Step -1
                                                 Try
                                                     Dim chiudibile As IWGChiudibile = TryCast(rimasti(i), IWGChiudibile)
                                                     If chiudibile IsNot Nothing AndAlso Not rimasti(i).IsDisposed Then chiudibile.RichiediChiusura()
                                                 Catch ex As Exception
                                                     _log.ScriviLog("ChiudiSeRestanoSoloMenuDiPassaggio: " & ex.Message)
                                                 End Try
                                             Next
                                         End Sub))
        Return True
    End Function

    ''' <summary>La nuova finestra appartiene alla stessa applicazione di quella esistente (per ridisegnare un elenco).</summary>
    Friend Sub MettiNellaStessaApplicazione(nuovo As Control, esistente As Control)
        Try
            If nuovo Is Nothing OrElse esistente Is Nothing Then Return
            SyncLock _lockStack
                _applicazioneDi(nuovo) = ApplicazioneDi(esistente)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("MettiNellaStessaApplicazione: " & ex.Message)
        End Try
    End Sub

#End Region

    ''' <summary>Finestre della stessa applicazione del controllo, dalla più vecchia alla più recente.</summary>
    Public Function ControlliDellaStessaApplicazione(controllo As Control) As List(Of Control)
        Try
            SyncLock _lockStack
                If Not ApplicazioniSeparate Then Return _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed).ToList()
                Dim applicazione As Integer = ApplicazioneDi(controllo)
                Return _stackControlli.Where(Function(c) c IsNot Nothing AndAlso Not c.IsDisposed AndAlso ApplicazioneDi(c) = applicazione).ToList()
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ControlliDellaStessaApplicazione: " & ex.Message)
            Return New List(Of Control)()
        End Try
    End Function

End Class