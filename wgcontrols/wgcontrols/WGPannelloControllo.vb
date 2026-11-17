' ============================================================================================
' WGPannelloControllo - il centro di controllo della modalità smart, disegnato da WinGabriel
' ============================================================================================
'
' Come il pannello che scende dall'alto su tablet e telefoni, ma fatto tutto qui (niente interfacce
' di Windows, che la sintesi di WinGabriel non legge):
'   - interruttori Wi-Fi e Bluetooth;
'   - cursori della luminosità dello schermo e del volume;
'   - brano precedente, riproduci o pausa, brano successivo (dell'ascolto del centro di controllo,
'     altrimenti con i tasti multimediali per gli altri lettori);
'   - dove si ascolta l'audio, non disturbare;
'   - sotto, tutte le voci del centro di controllo di sempre: batteria, verifica della connessione,
'     operazioni in corso (copie, download, ricerche...), ascolto, voci delle applicazioni.
' Tasti: frecce destra e sinistra, Tab e Maiuscolo Tab per spostarsi; sui cursori frecce su e giù
' regolano, altrove passano alla riga sopra o sotto; Invio o Spazio attivano; Esc chiude.
' Touch: un colpo a destra o a sinistra per spostarsi, in su o in giù sui cursori per regolare,
' doppio tocco per attivare, toccare e scorrere col dito per esplorare, tre dita in su per chiudere,
' come per aprirlo; doppio tocco con due dita per chiudere.
'
'   WGPannelloControllo.Mostra()                  ' apre, o chiude se è già aperto
'   WGPannelloControllo.LeggiRadio = ...           ' dal programma principale (WGConnessioni)
'   WGPannelloControllo.CommutaRadio = ...
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class WGPannelloControllo
    Inherits UserControl
    Implements IWGAnnuncioIngresso

    Private Shared ReadOnly log As New Logger()
    Private Shared _aperto As WGPannelloControllo

    ''' <summary>Stato della radio (bluetooth = vero per il Bluetooth): acceso, spento, Nothing se non c'è.</summary>
    Public Shared Property LeggiRadio As Func(Of Boolean, Task(Of Boolean?))
    ''' <summary>Accende o spegne la radio (con attesa e domande); restituisce lo stato nuovo, Nothing se non riuscito.</summary>
    Public Shared Property CommutaRadio As Func(Of Boolean, Task(Of Boolean?))
    ''' <summary>Non disturbare: descrizione breve e scelta (dal centro notifiche).</summary>
    Public Shared Property TestoNonDisturbare As Func(Of String)
    Public Shared Property ScegliNonDisturbare As Action

    Private Enum TipoComando
        Interruttore
        Cursore
        Pulsante
    End Enum

    Private Class Comando
        Public Chiave As String = ""
        Public Tipo As TipoComando
        Public Etichetta As Func(Of String)
        ' Per il cursore la percentuale (-1 = non regolabile), per l'interruttore 1 acceso, 0 spento, -1 sconosciuto.
        Public Livello As Func(Of Integer)
        Public Attiva As Action
        Public Regola As Action(Of Integer)
        Public Riga As Integer
    End Class

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private WithEvents _timer As New Timer() With {.Interval = 1500}
    Private ReadOnly _comandi As New List(Of Comando)()
    Private _indice As Integer
    Private _primaRiga As Integer
    Private _suoni As WGSuoni
    Private _wifi As Boolean?
    Private _bluetooth As Boolean?
    Private _luminosita As Integer = -2
    Private ReadOnly _rettangoli As New Dictionary(Of Integer, Rectangle)()

    Public Sub New()
        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.Opaque, True)
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer, False)
        Me.DoubleBuffered = False
        Me.AccessibleName = "Centro di controllo"
        Me.AccessibleRole = AccessibleRole.None
        CaricaAspetto()
        Costruisci()
    End Sub

#Region "Apertura e chiusura"

    ''' <summary>Apre il centro di controllo; se è già aperto lo chiude (lo stesso gesto apre e chiude).</summary>
    Public Shared Sub Mostra()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            If contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New MethodInvoker(AddressOf Mostra))
                Return
            End If
            If _aperto IsNot Nothing AndAlso Not _aperto.IsDisposed Then
                ' Davanti: lo stesso gesto lo chiude. Rimasto dietro (in un'applicazione lasciata, per
                ' esempio tornando alla home): lo si porta davanti invece di chiuderlo.
                If _aperto.PuoRicevere() Then
                    _aperto.Chiudi()
                    Return
                End If
                If contenitore.VaiAllApplicazioneDi(_aperto) Then
                    _aperto.AnnunciaIngresso()
                    Return
                End If
                _aperto.Chiudi()
            End If
            Dim pannello As New WGPannelloControllo()
            _aperto = pannello
            pannello.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Mostra: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property Aperto As Boolean
        Get
            Return _aperto IsNot Nothing AndAlso Not _aperto.IsDisposed
        End Get
    End Property

    Private Sub Avvia()
        _tastiera = WGTastiera.GetInstance()
        _touk = WGTouk.GetInstance()
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If contenitore IsNot Nothing Then contenitore.MostraControllo(Me)
        _timer.Start()
        AggiornaStatiAsync()
        sintesivocale.GetInstance().LeggiTesto("Centro di controllo. " & Parlato(_indice))
    End Sub

    Private Sub Chiudi()
        Try
            _timer.Stop()
            _tastiera = Nothing
            _touk = Nothing
            If _aperto Is Me Then _aperto = Nothing
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        sintesivocale.GetInstance().LeggiTesto("Centro di controllo. " & Parlato(_indice))
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Return contenitore IsNot Nothing AndAlso contenitore.PuoRicevereInput(Me)
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "Comandi"

    ' I comandi fissi in cima, poi le voci del centro di controllo, che cambiano (operazioni in corso).
    ' La selezione resta sulla stessa voce anche quando l'elenco cambia.
    Private Sub Costruisci()
        Dim chiaveScelta As String = If(_indice >= 0 AndAlso _indice < _comandi.Count, _comandi(_indice).Chiave, "")
        _comandi.Clear()
        _comandi.Add(New Comando With {.Chiave = "wifi", .Tipo = TipoComando.Interruttore, .Riga = 0,
                     .Etichetta = Function() "Wi-Fi", .Livello = Function() If(_wifi.HasValue, If(_wifi.Value, 1, 0), -1),
                     .Attiva = Sub() MenuRadio(False)})
        _comandi.Add(New Comando With {.Chiave = "bluetooth", .Tipo = TipoComando.Interruttore, .Riga = 0,
                     .Etichetta = Function() "Bluetooth", .Livello = Function() If(_bluetooth.HasValue, If(_bluetooth.Value, 1, 0), -1),
                     .Attiva = Sub() MenuRadio(True)})
        _comandi.Add(New Comando With {.Chiave = "luminosita", .Tipo = TipoComando.Cursore, .Riga = 1,
                     .Etichetta = Function() "Luminosità", .Livello = Function() _luminosita,
                     .Regola = Sub(passo) RegolaLuminosita(passo)})
        _comandi.Add(New Comando With {.Chiave = "volume", .Tipo = TipoComando.Cursore, .Riga = 2,
                     .Etichetta = Function() If(Muto(), "Volume, muto", "Volume"), .Livello = Function() VolumeAttuale(),
                     .Regola = Sub(passo) RegolaVolume(passo)})
        _comandi.Add(New Comando With {.Chiave = "precedente", .Tipo = TipoComando.Pulsante, .Riga = 3,
                     .Etichetta = Function() "Brano precedente", .Attiva = Sub() WGCentroDiControllo.GetInstance().BranoPrecedente()})
        _comandi.Add(New Comando With {.Chiave = "riproduci", .Tipo = TipoComando.Pulsante, .Riga = 3,
                     .Etichetta = Function() If(WGCentroDiControllo.GetInstance().AscoltoInCorso AndAlso Not WGCentroDiControllo.GetInstance().AscoltoInPausa, "Pausa", "Riproduci"),
                     .Attiva = Sub() WGCentroDiControllo.GetInstance().RiproduciPausa()})
        _comandi.Add(New Comando With {.Chiave = "successivo", .Tipo = TipoComando.Pulsante, .Riga = 3,
                     .Etichetta = Function() "Brano successivo", .Attiva = Sub() WGCentroDiControllo.GetInstance().BranoSuccessivo()})
        _comandi.Add(New Comando With {.Chiave = "audio", .Tipo = TipoComando.Pulsante, .Riga = 4,
                     .Etichetta = Function() "Audio su " & WGUscitaAudio.NomeAttuale(), .Attiva = Sub() WGUscitaAudio.Scegli()})
        Dim riga As Integer = 5
        If TestoNonDisturbare IsNot Nothing AndAlso ScegliNonDisturbare IsNot Nothing Then
            _comandi.Add(New Comando With {.Chiave = "nondisturbare", .Tipo = TipoComando.Pulsante, .Riga = riga,
                         .Etichetta = Function() "Non disturbare: " & TestoNonDisturbare.Invoke(), .Attiva = Sub() ScegliNonDisturbare.Invoke()})
            riga += 1
        End If
        Try
            Dim numero As Integer = 0
            For Each voce As Tuple(Of String, Action) In WGCentroDiControllo.GetInstance().VociOperazioni()
                Dim testo As String = voce.Item1
                Dim azione As Action = voce.Item2
                ' La chiave è il testo senza numeri, così una percentuale che cambia non sposta la selezione.
                Dim chiave As String = "voce|" & New String(testo.Where(Function(c) Not Char.IsDigit(c)).ToArray())
                _comandi.Add(New Comando With {.Chiave = chiave & "|" & numero, .Tipo = TipoComando.Pulsante, .Riga = riga,
                             .Etichetta = Function() testo, .Attiva = azione})
                riga += 1
                numero += 1
            Next
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Costruisci: " & ex.Message)
        End Try
        Dim trovato As Integer = _comandi.FindIndex(Function(c) c.Chiave = chiaveScelta)
        _indice = If(trovato >= 0, trovato, Math.Max(0, Math.Min(_indice, _comandi.Count - 1)))
    End Sub

    Private Function Parlato(indice As Integer) As String
        If indice < 0 OrElse indice >= _comandi.Count Then Return ""
        Dim c As Comando = _comandi(indice)
        Dim etichetta As String = SicuroTesto(c.Etichetta)
        Select Case c.Tipo
            Case TipoComando.Interruttore
                Dim livello As Integer = SicuroNumero(c.Livello)
                Return etichetta & ", " & If(livello = 1, "acceso", If(livello = 0, "spento", "stato in lettura")) & ", interruttore"
            Case TipoComando.Cursore
                Dim livello As Integer = SicuroNumero(c.Livello)
                If livello = -2 Then Return etichetta & ", in lettura"
                If livello < 0 Then Return etichetta & ", non regolabile su questo schermo"
                Return etichetta & " " & livello & " per cento, regolabile"
            Case Else
                Return etichetta & ", pulsante"
        End Select
    End Function

    Private Shared Function SicuroTesto(f As Func(Of String)) As String
        Try
            Return If(f Is Nothing, "", If(f.Invoke(), ""))
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.SicuroTesto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function SicuroNumero(f As Func(Of Integer)) As Integer
        Try
            Return If(f Is Nothing, -1, f.Invoke())
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.SicuroNumero: " & ex.Message)
            Return -1
        End Try
    End Function

    ' Stato delle radio e luminosità si leggono in sottofondo: il pannello si apre subito.
    Private Async Sub AggiornaStatiAsync()
        Try
            If _luminosita = -2 Then
                _luminosita = Await Task.Run(Function() WGLuminosita.Leggi())
            End If
            If LeggiRadio IsNot Nothing Then
                _wifi = Await LeggiRadio.Invoke(False)
                _bluetooth = Await LeggiRadio.Invoke(True)
            End If
            If Not Me.IsDisposed Then Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.AggiornaStatiAsync: " & ex.Message)
        End Try
    End Sub

    ' Wi-Fi e Bluetooth: invece di commutare alla cieca si apre un menu, che si legge come tutti i menu,
    ' con accendi o spegni e con l'elenco delle reti o dei dispositivi, da cui si gestisce tutto il resto.
    Private Sub MenuRadio(bluetooth As Boolean)
        ApriMenuRadio(bluetooth, If(bluetooth, _bluetooth, _wifi), Sub() Commuta(bluetooth))
    End Sub

    ' Il menu di Wi-Fi o Bluetooth, uguale dal pannello e dalle barre di stato di scrivania e home.
    Private Shared Sub ApriMenuRadio(bluetooth As Boolean, stato As Boolean?, commuta As Action)
        Try
            Dim nome As String = If(bluetooth, "Bluetooth", "Wi-Fi")
            Dim apri As Action = If(bluetooth, WGScrivania.ApriBluetooth, WGScrivania.ApriConnessione)
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.TitoloComeMenu = False
            lista.impostaTitolo(nome & If(stato.HasValue, If(stato.Value, ", acceso", ", spento"), ""))
            If CommutaRadio IsNot Nothing AndAlso stato.HasValue Then
                lista.aggiungiElemento(If(stato.Value, "Spegni il " & nome, "Accendi il " & nome), "commuta")
            End If
            If apri IsNot Nothing Then
                lista.aggiungiElemento(If(bluetooth, "Elenco dispositivi Bluetooth", "Elenco reti Wi-Fi"), "elenco")
            End If
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          lista.abbandona()
                                                          If id = "commuta" Then
                                                              commuta()
                                                          ElseIf id = "elenco" AndAlso apri IsNot Nothing Then
                                                              apri()
                                                          End If
                                                      Catch exScelta As Exception
                                                          log.ScriviLog("WGPannelloControllo.ApriMenuRadio scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.ApriMenuRadio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Dalle barre di stato della scrivania e della home: legge lo stato della radio (con l'attesa) e apre
    ''' lo stesso menu del pannello, con accendi o spegni e l'elenco delle reti o dei dispositivi.
    ''' </summary>
    ' Ultimo stato letto delle due radio (0 Wi-Fi, 1 Bluetooth): così dalle barre di stato il menu si apre
    ' subito, senza attese prima, come dal pannello. Si rilegge in sottofondo a ogni apertura.
    Private Shared ReadOnly _statoRadioNoto As Boolean?() = {Nothing, Nothing}
    Private Shared ReadOnly _statoRadioLetto As Boolean() = {False, False}

    ''' <summary>Legge in sottofondo lo stato di Wi-Fi e Bluetooth (da chiamare quando si disegna una barra di stato).</summary>
    Public Shared Async Sub PreparaStatoRadio()
        Try
            If LeggiRadio Is Nothing Then Return
            For indice As Integer = 0 To 1
                _statoRadioNoto(indice) = Await LeggiRadio.Invoke(indice = 1)
                _statoRadioLetto(indice) = True
            Next
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.PreparaStatoRadio: " & ex.Message)
        End Try
    End Sub

    Public Shared Async Sub MostraMenuRadio(bluetooth As Boolean)
        Try
            Dim nome As String = If(bluetooth, "Bluetooth", "Wi-Fi")
            Dim indiceRadio As Integer = If(bluetooth, 1, 0)
            Dim stato As Boolean? = _statoRadioNoto(indiceRadio)
            ' Solo la prima volta, se lo stato non è ancora stato letto, lo si legge adesso con l'attesa.
            If Not _statoRadioLetto(indiceRadio) AndAlso LeggiRadio IsNot Nothing Then
                stato = Await WGProcessoInCorso.EseguiAsync("Controllo del " & nome, Function() LeggiRadio.Invoke(bluetooth))
                _statoRadioNoto(indiceRadio) = stato
                _statoRadioLetto(indiceRadio) = True
            End If
            ApriMenuRadio(bluetooth, stato, Async Sub()
                                                Try
                                                    If CommutaRadio Is Nothing Then Return
                                                    Dim nuovo As Boolean? = Await CommutaRadio.Invoke(bluetooth)
                                                    If Not nuovo.HasValue Then Return
                                                    _statoRadioNoto(indiceRadio) = nuovo
                                                    ' Detto per ultimo, dopo il ritorno alla scrivania, così niente lo copre.
                                                    Await Task.Delay(600)
                                                    sintesivocale.GetInstance().LeggiTesto(nome & If(nuovo.Value, " acceso", " spento"))
                                                Catch exCommuta As Exception
                                                    log.ScriviLog("WGPannelloControllo.MostraMenuRadio commuta: " & exCommuta.Message)
                                                End Try
                                            End Sub)
            ' Per la prossima volta: lo stato vero, letto in sottofondo (qualcuno può averlo cambiato da fuori).
            PreparaStatoRadio()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.MostraMenuRadio: " & ex.Message)
        End Try
    End Sub

    Private Async Sub Commuta(bluetooth As Boolean)
        Try
            If CommutaRadio Is Nothing Then Return
            Dim nuovo As Boolean? = Await CommutaRadio.Invoke(bluetooth)
            If nuovo.HasValue Then
                If bluetooth Then _bluetooth = nuovo Else _wifi = nuovo
            End If
            If Me.IsDisposed Then Return
            Me.Invalidate()
            ' Tornati sul pannello si sente lo stato nuovo dell'interruttore, per ultimo, così niente lo copre.
            If nuovo.HasValue Then
                Await Task.Delay(500)
                Dim indice As Integer = _comandi.FindIndex(Function(c) c.Chiave = If(bluetooth, "bluetooth", "wifi"))
                If indice >= 0 AndAlso PuoRicevere() Then sintesivocale.GetInstance().LeggiTesto(Parlato(indice))
            End If
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Commuta: " & ex.Message)
        End Try
    End Sub

    Private Sub RegolaLuminosita(passo As Integer)
        If _luminosita < 0 Then
            sintesivocale.GetInstance().LeggiTesto(If(_luminosita = -2, "Luminosità in lettura", "La luminosità di questo schermo non si può regolare da qui"))
            Return
        End If
        Dim nuova As Integer = Math.Max(0, Math.Min(100, _luminosita + passo))
        If WGLuminosita.Imposta(nuova) Then _luminosita = nuova
        sintesivocale.GetInstance().LeggiTesto(_luminosita & " per cento")
    End Sub

    Private Shared Function Muto() As Boolean
        Try
            Return WGAudio.AltoparlanteDisattivato()
        Catch
            Return False
        End Try
    End Function

    Private Shared Function VolumeAttuale() As Integer
        Try
            Return WGAudio.VolumeAltoparlante()
        Catch
            Return -1
        End Try
    End Function

    Private Sub RegolaVolume(passo As Integer)
        Try
            If WGAudio.AltoparlanteDisattivato() Then WGAudio.ImpostaAltoparlanteDisattivato(False)
            WGAudio.ImpostaVolumeAltoparlante(Math.Max(0, Math.Min(100, WGAudio.VolumeAltoparlante() + passo)))
            sintesivocale.GetInstance().LeggiTesto(WGAudio.VolumeAltoparlante() & " per cento")
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.RegolaVolume: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Movimento e azioni"

    Private Sub Bordo()
        Try
            If _suoni Is Nothing Then _suoni = New WGSuoni()
            _suoni.LimiteRaggiunto()
        Catch
        End Try
        LeggiCorrente()
    End Sub

    Private Sub LeggiCorrente()
        sintesivocale.GetInstance().LeggiTesto(Parlato(_indice))
    End Sub

    Private Sub VaiA(indice As Integer)
        If indice < 0 OrElse indice >= _comandi.Count Then
            Bordo()
            Return
        End If
        _indice = indice
        TieniVisibile()
        Me.Invalidate()
        LeggiCorrente()
    End Sub

    ' Prima voce della riga sopra o sotto.
    Private Sub CambiaRiga(direzione As Integer)
        If _comandi.Count = 0 Then Return
        Dim rigaAttuale As Integer = _comandi(_indice).Riga
        Dim destinazione As Integer = If(direzione > 0,
            _comandi.FindIndex(Function(c) c.Riga > rigaAttuale),
            _comandi.FindIndex(Function(c) c.Riga = _comandi.Where(Function(x) x.Riga < rigaAttuale).Select(Function(x) x.Riga).DefaultIfEmpty(-1).Max()))
        VaiA(destinazione)
    End Sub

    Private Sub Regola(passo As Integer)
        Dim c As Comando = _comandi(_indice)
        If c.Tipo <> TipoComando.Cursore OrElse c.Regola Is Nothing Then Return
        c.Regola.Invoke(passo)
        Me.Invalidate()
    End Sub

    Private Sub AttivaCorrente()
        If _indice < 0 OrElse _indice >= _comandi.Count Then Return
        Dim c As Comando = _comandi(_indice)
        If c.Tipo = TipoComando.Cursore Then
            LeggiCorrente()
            Return
        End If
        If c.Attiva Is Nothing Then Return
        Try
            c.Attiva.Invoke()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.AttivaCorrente: " & ex.Message)
        End Try
        Me.Invalidate()
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse Not PuoRicevere() Then Return
            Dim codice As Keys = tasto And Keys.KeyCode
            Dim modificatori As Keys = _tastiera.ModificatoriUltimoTasto
            If (modificatori And (Keys.Control Or Keys.Alt)) <> Keys.None Then Return
            Dim maiuscolo As Boolean = (modificatori And Keys.Shift) = Keys.Shift
            Dim cursore As Boolean = _comandi.Count > 0 AndAlso _comandi(_indice).Tipo = TipoComando.Cursore
            Select Case codice
                Case Keys.Right : VaiA(_indice + 1)
                Case Keys.Left : VaiA(_indice - 1)
                Case Keys.Tab : VaiA(_indice + If(maiuscolo, -1, 1))
                Case Keys.Up : If cursore Then Regola(10) Else CambiaRiga(-1)
                Case Keys.Down : If cursore Then Regola(-10) Else CambiaRiga(1)
                Case Keys.Home : VaiA(0)
                Case Keys.End : VaiA(_comandi.Count - 1)
                Case Keys.Enter, Keys.Space : AttivaCorrente()
                Case Keys.Escape : Chiudi()
                Case Keys.Insert : sintesivocale.GetInstance().LeggiTesto("Centro di controllo. " & Parlato(_indice))
                Case Else : Return
            End Select
            tasto = Keys.None
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvanti() Handles _touk.FlickAvanti
        If PuoRicevere() Then VaiA(_indice + 1)
    End Sub

    Private Sub _touk_FlickIndietro() Handles _touk.FlickIndietro
        If PuoRicevere() Then VaiA(_indice - 1)
    End Sub

    Private Sub _touk_FlickSu() Handles _touk.FlickSu
        If PuoRicevere() Then Regola(10)
    End Sub

    Private Sub _touk_FlickGiu() Handles _touk.FlickGiu
        If PuoRicevere() Then Regola(-10)
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        If PuoRicevere() Then AttivaCorrente()
    End Sub

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        If PuoRicevere() Then Chiudi()
    End Sub

    Private Sub _touk_EsplorazioneTocco(x As Integer, y As Integer) Handles _touk.EsplorazioneTocco
        Try
            If Not PuoRicevere() Then Return
            Dim punto As Point = Me.PointToClient(New Point(x, y))
            For Each coppia As KeyValuePair(Of Integer, Rectangle) In _rettangoli
                If coppia.Value.Contains(punto) Then
                    If coppia.Key <> _indice Then VaiA(coppia.Key)
                    Return
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.EsplorazioneTocco: " & ex.Message)
        End Try
    End Sub

    ' Ogni secondo e mezzo si aggiornano le voci (percentuali delle operazioni, brano, stato delle radio).
    Private _giri As Integer

    Private Sub _timer_Tick(sender As Object, e As EventArgs) Handles _timer.Tick
        Try
            If Me.IsDisposed Then Return
            Costruisci()
            _giri += 1
            If _giri Mod 4 = 0 Then AggiornaStatiAsync()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Tick: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Me.Visible AndAlso Not Me.IsDisposed Then Me.Invalidate()
    End Sub

#End Region

#Region "Disegno con le API di Windows (GDI), come la griglia"

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PAINTSTRUCT
        Public hdc As IntPtr
        Public fErase As Integer
        Public rcPaint As RECT
        Public fRestore As Integer
        Public fIncUpdate As Integer
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=32)>
        Public rgbReserved As Byte()
    End Structure

    <DllImport("user32.dll")> Private Shared Function BeginPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function EndPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleDC(hDC As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleBitmap(hDC As IntPtr, w As Integer, h As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SelectObject(hDC As IntPtr, h As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteObject(h As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteDC(hDC As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function BitBlt(hDest As IntPtr, x As Integer, y As Integer, w As Integer, h As Integer, hSrc As IntPtr, xs As Integer, ys As Integer, rop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateSolidBrush(colore As Integer) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function FillRect(hDC As IntPtr, ByRef r As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function FrameRect(hDC As IntPtr, ByRef r As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetBkMode(hDC As IntPtr, modo As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetTextColor(hDC As IntPtr, colore As Integer) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)> Private Shared Function DrawText(hDC As IntPtr, s As String, n As Integer, ByRef r As RECT, formato As UInteger) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFontW(h As Integer, w As Integer, esc As Integer, ori As Integer, peso As Integer, it As UInteger, sott As UInteger, barr As UInteger,
                                        cs As UInteger, op As UInteger, cp As UInteger, q As UInteger, pf As UInteger, nome As String) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function GetDC(hWnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetDeviceCaps(hDC As IntPtr, indice As Integer) As Integer
    End Function

    Private Const WM_PAINT As Integer = &HF
    Private Const WM_ERASEBKGND As Integer = &H14
    Private Const SRCCOPY As Integer = &HCC0020
    Private Const DT_CENTER As UInteger = &H1
    Private Const DT_VCENTER As UInteger = &H4
    Private Const DT_SINGLELINE As UInteger = &H20
    Private Const DT_NOPREFIX As UInteger = &H800
    Private Const DT_END_ELLIPSIS As UInteger = &H8000

    Private _hFont As IntPtr
    Private _hFontTitolo As IntPtr
    Private _dimensione As Single = 18
    Private _coloreSfondo As Integer = &H202020
    Private _coloreTesto As Integer = &HFFFFFF
    Private _coloreCasella As Integer = &H3A3A3A
    Private _coloreSelezione As Integer = &H7A4A1A
    Private _coloreFuoco As Integer = &H33CCFF
    Private _coloreAcceso As Integer = &H50B000
    Private _coloreSpento As Integer = &H707070
    Private _altezzaTitolo As Integer = 60
    Private _altezzaRiga As Integer = 90
    Private _margine As Integer = 12

    ' Come i pannelli dei telefoni: scuro, con caselle arrotondate a vista e colori forti per acceso e fuoco.
    ' Carattere e dimensione sono quelli dei menu di WinGabriel; con il contrasto alto i colori si rafforzano.
    Private Sub CaricaAspetto()
        Try
            Dim dpi As Integer = 96
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            If hDC <> IntPtr.Zero Then
                dpi = GetDeviceCaps(hDC, 90)
                ReleaseDC(IntPtr.Zero, hDC)
            End If
            Dim scala As Single = dpi / 96.0F
            Dim carattere As String = "Segoe UI"
            Dim config As New WGConfig()
            Dim valore As Single
            _dimensione = 18 * scala
            If config.EsisteId("FontDimensione") AndAlso Single.TryParse(config.LeggiValore("FontDimensione"), valore) Then _dimensione = valore * scala
            If config.EsisteId("MenuCarattere") AndAlso Not String.IsNullOrEmpty(config.LeggiValore("MenuCarattere")) Then carattere = config.LeggiValore("MenuCarattere")
            Dim contrastoAlto As Boolean = False
            If config.EsisteId("ContrastoAlto") Then Boolean.TryParse(config.LeggiValore("ContrastoAlto"), contrastoAlto)
            If contrastoAlto Then
                _coloreSfondo = &H0
                _coloreCasella = &H0
                _coloreSelezione = &HFF0000
                _coloreFuoco = &HFFFF
            End If
            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hFontTitolo <> IntPtr.Zero Then DeleteObject(_hFontTitolo)
            _hFont = CreateFontW(CInt(-Math.Round(_dimensione * dpi / 72.0F)), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, carattere)
            _hFontTitolo = CreateFontW(CInt(-Math.Round(_dimensione * 1.3F * dpi / 72.0F)), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, carattere)
            _altezzaTitolo = Math.Max(40, CInt(_dimensione * 3.2F))
            _altezzaRiga = Math.Max(60, CInt(_dimensione * 4.5F))
            _margine = Math.Max(8, CInt(_dimensione / 2))
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.CaricaAspetto: " & ex.Message)
        End Try
    End Sub

    Private Function RigheTotali() As Integer
        Return If(_comandi.Count = 0, 0, _comandi.Max(Function(c) c.Riga) + 1)
    End Function

    Private Function RigheVisibili() As Integer
        Return Math.Max(1, (Me.ClientSize.Height - _altezzaTitolo - _margine) \ (_altezzaRiga + _margine))
    End Function

    Private Sub TieniVisibile()
        If _comandi.Count = 0 Then Return
        Dim riga As Integer = _comandi(_indice).Riga
        Dim visibili As Integer = RigheVisibili()
        If riga < _primaRiga Then _primaRiga = riga
        If riga >= _primaRiga + visibili Then _primaRiga = riga - visibili + 1
        _primaRiga = Math.Max(0, Math.Min(_primaRiga, Math.Max(0, RigheTotali() - visibili)))
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_PAINT Then
            Disegna()
            m.Result = IntPtr.Zero
            Return
        End If
        If m.Msg = WM_ERASEBKGND Then
            m.Result = New IntPtr(1)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        TieniVisibile()
        Me.Invalidate()
    End Sub

    Private Sub Testo(hDC As IntPtr, s As String, r As RECT, formato As UInteger)
        If String.IsNullOrEmpty(s) Then Return
        DrawText(hDC, s, s.Length, r, formato Or DT_NOPREFIX)
    End Sub

    Private Sub Riempi(hDC As IntPtr, r As RECT, colore As Integer)
        Dim pennello As IntPtr = CreateSolidBrush(colore)
        FillRect(hDC, r, pennello)
        DeleteObject(pennello)
    End Sub

    Private Sub Cornice(hDC As IntPtr, r As RECT, colore As Integer, spessore As Integer)
        Dim pennello As IntPtr = CreateSolidBrush(colore)
        For i As Integer = 0 To spessore - 1
            Dim b As New RECT With {.Left = r.Left + i, .Top = r.Top + i, .Right = r.Right - i, .Bottom = r.Bottom - i}
            FrameRect(hDC, b, pennello)
        Next
        DeleteObject(pennello)
    End Sub

    Private Sub Disegna()
        Dim ps As New PAINTSTRUCT()
        Dim hDC As IntPtr = BeginPaint(Me.Handle, ps)
        If hDC = IntPtr.Zero Then Return
        Dim memDC As IntPtr = IntPtr.Zero
        Dim bitmap As IntPtr = IntPtr.Zero
        Dim vecchioBitmap As IntPtr = IntPtr.Zero
        Try
            Dim larghezza As Integer = Me.ClientSize.Width
            Dim altezza As Integer = Me.ClientSize.Height
            If larghezza <= 0 OrElse altezza <= 0 Then Return
            memDC = CreateCompatibleDC(hDC)
            bitmap = CreateCompatibleBitmap(hDC, larghezza, altezza)
            vecchioBitmap = SelectObject(memDC, bitmap)
            SetBkMode(memDC, 1)
            Riempi(memDC, New RECT With {.Right = larghezza, .Bottom = altezza}, _coloreSfondo)

            Dim vecchioFont As IntPtr = SelectObject(memDC, _hFontTitolo)
            SetTextColor(memDC, _coloreTesto)
            Testo(memDC, "Centro di controllo", New RECT With {.Left = 20, .Right = larghezza - 20, .Bottom = _altezzaTitolo}, DT_SINGLELINE Or DT_VCENTER)
            SelectObject(memDC, _hFont)

            _rettangoli.Clear()
            Dim quanteRighe As Integer = RigheVisibili()
            For riga As Integer = _primaRiga To Math.Min(RigheTotali() - 1, _primaRiga + quanteRighe)
                Dim indici As List(Of Integer) = Enumerable.Range(0, _comandi.Count).Where(Function(i) _comandi(i).Riga = riga).ToList()
                If indici.Count = 0 Then Continue For
                Dim top As Integer = _altezzaTitolo + _margine + (riga - _primaRiga) * (_altezzaRiga + _margine)
                If top >= altezza Then Exit For
                Dim larghezzaCasella As Integer = (larghezza - _margine * (indici.Count + 1)) \ indici.Count
                For posizione As Integer = 0 To indici.Count - 1
                    Dim i As Integer = indici(posizione)
                    Dim casella As New RECT With {.Left = _margine + posizione * (larghezzaCasella + _margine), .Top = top}
                    casella.Right = casella.Left + larghezzaCasella
                    casella.Bottom = top + _altezzaRiga
                    _rettangoli(i) = Rectangle.FromLTRB(casella.Left, casella.Top, casella.Right, casella.Bottom)
                    DisegnaComando(memDC, _comandi(i), casella, i = _indice)
                Next
            Next
            SelectObject(memDC, vecchioFont)
            BitBlt(hDC, 0, 0, larghezza, altezza, memDC, 0, 0, SRCCOPY)
        Catch ex As Exception
            log.ScriviLog("WGPannelloControllo.Disegna: " & ex.Message)
        Finally
            If memDC <> IntPtr.Zero Then
                If vecchioBitmap <> IntPtr.Zero Then SelectObject(memDC, vecchioBitmap)
                DeleteDC(memDC)
            End If
            If bitmap <> IntPtr.Zero Then DeleteObject(bitmap)
            EndPaint(Me.Handle, ps)
        End Try
    End Sub

    Private Sub DisegnaComando(hDC As IntPtr, c As Comando, casella As RECT, selezionato As Boolean)
        Riempi(hDC, casella, If(selezionato, _coloreSelezione, _coloreCasella))
        If selezionato Then Cornice(hDC, casella, _coloreFuoco, 4)
        SetTextColor(hDC, _coloreTesto)
        Dim etichetta As String = SicuroTesto(c.Etichetta)
        Dim altezza As Integer = casella.Bottom - casella.Top
        Select Case c.Tipo
            Case TipoComando.Interruttore
                ' Nome a sinistra, interruttore a destra: verde con il pallino a destra se acceso.
                Dim livello As Integer = SicuroNumero(c.Livello)
                Dim hInt As Integer = Math.Max(20, altezza \ 3)
                Dim wInt As Integer = hInt * 2
                Dim interr As New RECT With {.Right = casella.Right - 16, .Top = casella.Top + (altezza - hInt) \ 2}
                interr.Left = interr.Right - wInt
                interr.Bottom = interr.Top + hInt
                Riempi(hDC, interr, If(livello = 1, _coloreAcceso, _coloreSpento))
                Dim pallino As New RECT With {.Top = interr.Top + 3, .Bottom = interr.Bottom - 3}
                pallino.Left = If(livello = 1, interr.Right - hInt + 3, interr.Left + 3)
                pallino.Right = pallino.Left + hInt - 6
                Riempi(hDC, pallino, &HFFFFFF)
                Testo(hDC, etichetta & If(livello = 1, "  acceso", If(livello = 0, "  spento", "")),
                      New RECT With {.Left = casella.Left + 16, .Top = casella.Top, .Right = interr.Left - 8, .Bottom = casella.Bottom}, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
            Case TipoComando.Cursore
                ' Nome e percentuale a sinistra, barra con la parte piena e il cursore a destra.
                Dim livello As Integer = SicuroNumero(c.Livello)
                Dim zonaTesto As New RECT With {.Left = casella.Left + 16, .Top = casella.Top, .Right = casella.Left + (casella.Right - casella.Left) \ 3, .Bottom = casella.Bottom}
                Testo(hDC, etichetta & If(livello >= 0, "  " & livello & "%", If(livello = -2, "", "  non regolabile")), zonaTesto, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                Dim barra As New RECT With {.Left = zonaTesto.Right + 8, .Right = casella.Right - 24, .Top = casella.Top + altezza \ 2 - 5, .Bottom = casella.Top + altezza \ 2 + 5}
                Riempi(hDC, barra, _coloreSpento)
                If livello >= 0 AndAlso barra.Right > barra.Left Then
                    Dim piena As RECT = barra
                    piena.Right = barra.Left + CInt((barra.Right - barra.Left) * livello / 100.0)
                    Riempi(hDC, piena, _coloreFuoco)
                    Dim manopola As New RECT With {.Left = piena.Right - 8, .Right = piena.Right + 8, .Top = barra.Top - 10, .Bottom = barra.Bottom + 10}
                    Riempi(hDC, manopola, &HFFFFFF)
                End If
            Case Else
                Testo(hDC, etichetta, New RECT With {.Left = casella.Left + 12, .Top = casella.Top, .Right = casella.Right - 12, .Bottom = casella.Bottom}, DT_CENTER Or DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
        End Select
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            _timer.Stop()
            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hFontTitolo <> IntPtr.Zero Then DeleteObject(_hFontTitolo)
            _hFont = IntPtr.Zero
            _hFontTitolo = IntPtr.Zero
        Catch
        End Try
        MyBase.Dispose(disposing)
    End Sub

#End Region

End Class
