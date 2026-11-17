Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports wgcontrols

' Tastiera virtuale su schermo per dispositivi tattili, con layout accessibili, suggerimenti di parole e riscontro vocale.
Public Class WGTastieraTouch
    Inherits Control
    Implements IWGElementoTattile

    Private log As New Logger()

    Public Enum ModalitaDigitazione
        Tocco
        DoppioTocco
        Scorrimento
    End Enum

    Private Enum ModalitaTasti
        Lettere
        SimboliPaginaA
        SimboliPaginaB
    End Enum

    Private Class InfoTasto
        Public Testo As String
        Public NomeParlato As String
        Public ESuggerimento As Boolean
        Public Area As RectangleF
        Public EComando As Boolean
    End Class

    Private Const AltezzaBarraDettatura As Integer = 44
    Private Const AltezzaBarraSuggerimenti As Integer = 56
    Private Const AltezzaMinimaRigaTasti As Integer = 34
    Private Const QuotaMassimaAltezza As Double = 0.7
    Private Const QuotaAltezzaSchermo As Double = 0.36
    Private Const RapportoMassimoLarghezzaTasto As Single = 1.15F
    Private Const FattoreAltezzaRigaInferiore As Single = 1.35F
    Private Const MassimoLettereConsecutive As Integer = 2
    Private Const DurataSerieLettereMs As Integer = 2000

    ''' <summary>
    ''' Quota massima dell'altezza del contenitore che la tastiera puo' occupare. Gli ospiti che
    ''' impongono un limite proprio devono usare questo valore, non una costante locale.
    ''' </summary>
    Public Shared ReadOnly Property QuotaAltezzaMassima As Double
        Get
            Return QuotaMassimaAltezza
        End Get
    End Property

    ''' <summary>
    ''' Altezza sotto la quale le righe dei tasti non ricevono spazio sufficiente. Gli ospiti che
    ''' impongono un limite alla tastiera devono tenerne conto.
    ''' </summary>
    Public Shared ReadOnly Property AltezzaMinimaConsigliata As Integer
        Get
            Return AltezzaMinimaTastiera
        End Get
    End Property
    Private _altezzaSuggerimentiApplicata As Integer = 0
    ''' <summary>
    ''' Altezza minima sotto la quale la tastiera non e' utilizzabile: con meno spazio i tasti
    ''' diventano cosi' bassi da non essere piu' colpibili col dito.
    ''' </summary>
    Private Const AltezzaMinimaTastiera As Integer = 240

    ''' <summary>
    ''' Grandezza dei tasti scelta dall'utente. Determina quanta parte dello schermo occupa la
    ''' tastiera: su un portatile in modalita' normale servono tasti piu' contenuti, su un tablet
    ''' tasti piu' grandi. Si imposta da Impostazioni, Touch, Grandezza tasti.
    ''' </summary>
    Public Enum GrandezzaTasti
        Piccoli
        Medi
        Grandi
    End Enum

    Public Enum TolleranzaMovimento
        Bassa
        Media
        Alta
    End Enum

    Public Enum IntervalloLettura
        Breve
        Medio
        Lungo
    End Enum

    Private Const ChiaveConfigGrandezzaTasti As String = "tastiera_touch_grandezza"
    Private Const ChiaveConfigModalitaDigitazione As String = "tastiera_touch_modalita"
    Private Const ChiaveConfigTolleranzaMovimento As String = "tastiera_touch_tolleranza"
    Private Const ChiaveConfigIntervalloLettura As String = "tastiera_touch_intervallo"

    Public Shared Function LeggiTolleranzaMovimento() As TolleranzaMovimento
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigTolleranzaMovimento)
            Select Case If(valore, "").Trim().ToLower()
                Case "bassa"
                    Return TolleranzaMovimento.Bassa
                Case "alta"
                    Return TolleranzaMovimento.Alta
                Case Else
                    Return TolleranzaMovimento.Media
            End Select
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.LeggiTolleranzaMovimento: {ex.Message}")
            Return TolleranzaMovimento.Media
        End Try
    End Function

    Public Shared Sub SalvaTolleranzaMovimento(tolleranza As TolleranzaMovimento)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String
            Select Case tolleranza
                Case TolleranzaMovimento.Bassa
                    valore = "bassa"
                Case TolleranzaMovimento.Alta
                    valore = "alta"
                Case Else
                    valore = "media"
            End Select
            If Not cfg.EsisteId(ChiaveConfigTolleranzaMovimento) Then
                cfg.AggiungiValore(ChiaveConfigTolleranzaMovimento, valore)
            Else
                cfg.ModificaValore(ChiaveConfigTolleranzaMovimento, valore)
            End If
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.SalvaTolleranzaMovimento: {ex.Message}")
        End Try
    End Sub

    Public Shared Function LeggiIntervalloLettura() As IntervalloLettura
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigIntervalloLettura)
            Select Case If(valore, "").Trim().ToLower()
                Case "breve"
                    Return IntervalloLettura.Breve
                Case "lungo"
                    Return IntervalloLettura.Lungo
                Case Else
                    Return IntervalloLettura.Medio
            End Select
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.LeggiIntervalloLettura: {ex.Message}")
            Return IntervalloLettura.Medio
        End Try
    End Function

    Public Shared Sub SalvaIntervalloLettura(intervallo As IntervalloLettura)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String
            Select Case intervallo
                Case IntervalloLettura.Breve
                    valore = "breve"
                Case IntervalloLettura.Lungo
                    valore = "lungo"
                Case Else
                    valore = "medio"
            End Select
            If Not cfg.EsisteId(ChiaveConfigIntervalloLettura) Then
                cfg.AggiungiValore(ChiaveConfigIntervalloLettura, valore)
            Else
                cfg.ModificaValore(ChiaveConfigIntervalloLettura, valore)
            End If
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.SalvaIntervalloLettura: {ex.Message}")
        End Try
    End Sub

    Private Shared Function FrazioneIsteresiPerTolleranza(tolleranza As TolleranzaMovimento) As Single
        Select Case tolleranza
            Case TolleranzaMovimento.Bassa
                Return 0.12F
            Case TolleranzaMovimento.Alta
                Return 0.45F
            Case Else
                Return 0.28F
        End Select
    End Function

    Private Shared Function MillisecondiPerIntervallo(intervallo As IntervalloLettura) As Integer
        Select Case intervallo
            Case IntervalloLettura.Breve
                Return 90
            Case IntervalloLettura.Lungo
                Return 320
            Case Else
                Return 180
        End Select
    End Function

    Public Shared Function LeggiGrandezzaTasti() As GrandezzaTasti
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigGrandezzaTasti)
            Select Case If(valore, "").Trim().ToLower()
                Case "piccoli"
                    Return GrandezzaTasti.Piccoli
                Case "grandi"
                    Return GrandezzaTasti.Grandi
                Case Else
                    Return GrandezzaTasti.Medi
            End Select
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.LeggiGrandezzaTasti: {ex.Message}")
            Return GrandezzaTasti.Medi
        End Try
    End Function

    Public Shared Sub SalvaGrandezzaTasti(grandezza As GrandezzaTasti)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String
            Select Case grandezza
                Case GrandezzaTasti.Piccoli
                    valore = "piccoli"
                Case GrandezzaTasti.Grandi
                    valore = "grandi"
                Case Else
                    valore = "medi"
            End Select
            If Not cfg.EsisteId(ChiaveConfigGrandezzaTasti) Then
                cfg.AggiungiValore(ChiaveConfigGrandezzaTasti, valore)
            Else
                cfg.ModificaValore(ChiaveConfigGrandezzaTasti, valore)
            End If
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.SalvaGrandezzaTasti: {ex.Message}")
        End Try
    End Sub

    Public Shared Function LeggiModalitaDigitazioneSalvata() As ModalitaDigitazione
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigModalitaDigitazione)
            Select Case If(valore, "").Trim().ToLower()
                Case "doppiotocco"
                    Return ModalitaDigitazione.DoppioTocco
                Case "scorrimento"
                    Return ModalitaDigitazione.Scorrimento
                Case Else
                    Return ModalitaDigitazione.Tocco
            End Select
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.LeggiModalitaDigitazioneSalvata: {ex.Message}")
            Return ModalitaDigitazione.Tocco
        End Try
    End Function

    Public Shared Sub SalvaModalitaDigitazione(modalita As ModalitaDigitazione)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String
            Select Case modalita
                Case ModalitaDigitazione.DoppioTocco
                    valore = "doppiotocco"
                Case ModalitaDigitazione.Scorrimento
                    valore = "scorrimento"
                Case Else
                    valore = "tocco"
            End Select
            If Not cfg.EsisteId(ChiaveConfigModalitaDigitazione) Then
                cfg.AggiungiValore(ChiaveConfigModalitaDigitazione, valore)
            Else
                cfg.ModificaValore(ChiaveConfigModalitaDigitazione, valore)
            End If
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog($"WGTastieraTouch.SalvaModalitaDigitazione: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Quota dell'altezza del contenitore occupata dalla tastiera, secondo la grandezza scelta.
    ''' </summary>
    Private Shared Function QuotaAltezzaPerGrandezza(grandezza As GrandezzaTasti) As Double
        Select Case grandezza
            Case GrandezzaTasti.Piccoli
                Return 0.5
            Case GrandezzaTasti.Grandi
                Return QuotaMassimaAltezza
            Case Else
                Return 0.62
        End Select
    End Function

    ''' <summary>
    ''' Altezza di riga voluta per la grandezza scelta. L'altezza della tastiera si ricava da
    ''' questa misura moltiplicata per il numero di righe: dimensionarla come sola frazione del
    ''' contenitore produce tasti minuscoli quando il contenitore e' basso.
    ''' </summary>
    Private Shared Function AltezzaRigaPerGrandezza(grandezza As GrandezzaTasti) As Integer
        Select Case grandezza
            Case GrandezzaTasti.Piccoli
                Return 52
            Case GrandezzaTasti.Grandi
                Return 88
            Case Else
                Return 70
        End Select
    End Function

    ''' <summary>
    ''' Quando e' attivo le lettere vengono mostrate e inserite in maiuscolo. Si commuta con il
    ''' tasto maiuscolo della riga inferiore, come sui telefoni.
    ''' </summary>
    Private _suoni As WGSuoni
    Private ReadOnly _tastiSuggerimenti As New List(Of InfoTasto)
    Private _parolaParzialeCorrente As String = ""
    Private _ospiteAggiornaSuggerimenti As Boolean = False
    Private _maiuscoloAttivo As Boolean = False

    ''' <summary>
    ''' Tasto che era selezionato prima dell'ultimo tocco, con l'istante in cui e' avvenuto:
    ''' servono al doppio tocco per confermare la lettera scelta in precedenza invece di quella
    ''' finita per caso sotto il dito nel secondo tocco.
    ''' </summary>
    Private _tastoPrimaDellUltimoTocco As InfoTasto
    Private _oraUltimaEsplorazione As Integer = 0
    Private Const SogliaRipristinoSelezioneMs As Integer = 500

    Private WithEvents _touk As WGTouk
    Private _sintesiVocale As sintesivocale
    Private _dettatura As WGDettatura

    Private _modalitaTasti As ModalitaTasti = ModalitaTasti.Lettere
    Private _modalitaDigitazioneCorrente As ModalitaDigitazione = ModalitaDigitazione.Tocco

    Private _righeTasti As New List(Of List(Of InfoTasto))
    Private _tastoBarraChiudi As InfoTasto
    Private _tastoBarraOpzioni As InfoTasto

    Private _tastoEsploratoCorrente As InfoTasto
    Private _tastoSottoDito As InfoTasto
    Private _rilascioGiaConfermato As Boolean = False
    Private _oraRilascioConfermato As Integer = 0
    Private _oraPressioneLungaScattata As Integer = 0
    Private _menuAccentiAperto As Boolean = False
    Private Const DurataGuardiaRilascioMs As Integer = 400
    Private Const DurataGuardiaPressioneLungaMs As Integer = 3000
    Private _inTrascinamento As Boolean = False
    Private _inDettatura As Boolean = False
    Private _attiva As Boolean = False

    Private WithEvents _timerPressioneLunga As New Timer()
    Private _tastoPressioneLungaCorrente As InfoTasto
    Private _pressioneLungaScattata As Boolean = False
    Private _puntoPressioneX As Single
    Private _puntoPressioneY As Single
    Private Const DurataPressioneLungaMs As Integer = 450
    Private Const SogliaSpostamentoPressioneLunga As Single = 12
    Private _layoutGestitoDalContenitore As Boolean = False
    Private _esplorazioneInCorso As Boolean = False
    Private Const IntervalloTimerEsplorazioneMs As Integer = 40
    Private _frazioneIsteresiTasto As Single = 0.28F
    Private _intervalloLetturaMs As Integer = 180
    Private _ditoGiu As Boolean = False
    Private _tastoCandidato As InfoTasto
    Private _oraUltimoEventoTouk As Integer = 0
    Private _letteraSerieCorrente As String = ""
    Private _lettereConsecutive As Integer = 0
    Private _oraUltimaLetteraSerie As Integer = 0
    Private _oraUltimaLettura As Integer = 0
    Private WithEvents _timerAnnuncioEsplorazione As New Timer()

    Public Event CarattereInserito(carattere As String)
    Public Event EliminaRichiesto()
    Public Event InvioRichiesto()
    Public Event SuggerimentoScelto(parola As String, caratteriDaSostituire As Integer)
    Public Event ChiudiRichiesto()
    Public Event TestoDettatoInserito(testo As String)
    Public Event ErroreDettatura(messaggio As String)

    Public Sub New()
        Try
            Me.DoubleBuffered = True
            Me.SetStyle(ControlStyles.ResizeRedraw, True)
            Me.TabStop = False
            Me.ImeMode = ImeMode.Disable
            _sintesiVocale = sintesivocale.GetInstance()
            _touk = WGTouk.GetInstance()
            _suoni = New WGSuoni()
            _timerPressioneLunga.Interval = DurataHoldAllineataTouk()
            _timerAnnuncioEsplorazione.Interval = IntervalloTimerEsplorazioneMs
            RicostruisciLayout()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Property ModalitaDigitazioneAttuale As ModalitaDigitazione
        Get
            Try
                Return _modalitaDigitazioneCorrente
            Catch ex As Exception
                log.ScriviLog($"WGTastieraTouch.ModalitaDigitazioneAttuale Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return ModalitaDigitazione.Tocco
            End Try
        End Get
        Set(value As ModalitaDigitazione)
            Try
                _modalitaDigitazioneCorrente = value
            Catch ex As Exception
                log.ScriviLog($"WGTastieraTouch.ModalitaDigitazioneAttuale Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    ''' <summary>
    ''' Tastiera a schermo attualmente aperta. Serve ai campi di testo per non togliere a WGTouk
    ''' il controllo assegnato alla tastiera ogni volta che prendono il fuoco.
    ''' </summary>
    Private Shared _tastieraAttivaSuSchermo As WGTastieraTouch = Nothing

    Public Shared ReadOnly Property TastieraAttivaVisibile As Boolean
        Get
            Try
                If _tastieraAttivaSuSchermo Is Nothing Then Return False
                If _tastieraAttivaSuSchermo.IsDisposed Then Return False
                If Not _tastieraAttivaSuSchermo.Visible Then Return False
                Return True
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Vero se il gesto corrente di WGTouk e' partito sulla tastiera a schermo aperta. Serve ai
    ''' campi per non trattare il doppio tocco su una lettera come Invio: la lettera si scrive al
    ''' pointer-up, e solo il tasto Invio della tastiera passa al campo successivo.
    ''' </summary>
    Public Shared Function GestoSullaTastieraAttiva(touk As WGTouk) As Boolean
        Try
            If touk Is Nothing Then Return False
            If touk.GestoIniziatoSuTastieraTouch() Then Return True
            Dim avvio As Point = touk.PuntoInizioGesto
            Return ContienePuntoSchermo(avvio.X, avvio.Y)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"WGTastieraTouch.GestoSullaTastieraAttiva: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se il punto schermo cade sulla tastiera a schermo visibile, con un margine per lo
    ''' scarto del dito al distacco. Non usa WindowFromPoint: le coordinate himetric convertite
    ''' possono puntare al campo sopra la tastiera e il doppio tocco sulla lettera diventava Invio.
    ''' </summary>
    Public Shared Function ContienePuntoSchermo(x As Integer, y As Integer) As Boolean
        Try
            If Not TastieraAttivaVisibile Then Return False
            If Not _tastieraAttivaSuSchermo.IsHandleCreated Then Return False
            Dim locale As Point = _tastieraAttivaSuSchermo.PointToClient(New Point(x, y))
            Const margine As Integer = 24
            If locale.X < -margine OrElse locale.Y < -margine Then Return False
            If locale.X >= _tastieraAttivaSuSchermo.Width + margine Then Return False
            If locale.Y >= _tastieraAttivaSuSchermo.Height + margine Then Return False
            Return True
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"WGTastieraTouch.ContienePuntoSchermo: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Riporta a WGTouk il controllo della tastiera aperta. Da chiamare ogni volta che un campo
    ''' prende il fuoco, altrimenti i tocchi sulla tastiera non arrivano piu' a nessuno.
    ''' </summary>
    Public Shared Function RiprendiControlloTouch() As Boolean
        Try
            If Not TastieraAttivaVisibile Then Return False
            If Not _tastieraAttivaSuSchermo.IsHandleCreated Then Return False
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk Is Nothing Then Return False
            touk.RiassegnaControllo(_tastieraAttivaSuSchermo.Handle)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub GestoreCambioSchermo(sender As Object, e As EventArgs)
        Try
            If Not _attiva Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf RipristinaDopoInterruzione))
                Return
            End If
            RipristinaDopoInterruzione()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.GestoreCambioSchermo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Property LayoutGestitoDalContenitore As Boolean
        Get
            Return _layoutGestitoDalContenitore
        End Get
        Set(value As Boolean)
            _layoutGestitoDalContenitore = value
        End Set
    End Property

    Private Function DurataHoldAllineataTouk() As Integer
        Try
            If _touk IsNot Nothing AndAlso _touk.SogliaHoldMs > 0 Then Return Math.Max(1, CInt(Math.Round(_touk.SogliaHoldMs)))
        Catch
        End Try
        Return DurataPressioneLungaMs
    End Function

    Private Function SogliaMovimentoHoldAllineataTouk() As Single
        Try
            If _touk IsNot Nothing Then Return CSng(_touk.SogliaMovimentoHoldPixel)
        Catch
        End Try
        Return SogliaSpostamentoPressioneLunga
    End Function

    Public Sub Mostra()
        Try
            Me.Visible = True
            Me.Enabled = True
            _attiva = True
            _tastieraAttivaSuSchermo = Me
            Try
                RemoveHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, AddressOf GestoreCambioSchermo
                AddHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, AddressOf GestoreCambioSchermo
            Catch exEventi As Exception
                log.ScriviLog($"WGTastieraTouch.Mostra (eventi schermo): {exEventi.Message}")
            End Try
            _modalitaTasti = ModalitaTasti.Lettere
            _modalitaDigitazioneCorrente = LeggiModalitaDigitazioneSalvata()
            RicaricaConfigurazioneTastiera()
            _timerPressioneLunga.Interval = DurataHoldAllineataTouk()

            If Not _layoutGestitoDalContenitore Then ApplicaAltezzaVoluta()

            RicostruisciLayout()
            Me.Invalidate()
            Try
                If Me.IsHandleCreated AndAlso _touk IsNot Nothing Then
                    _touk.RiassegnaControllo(Me.Handle)
                End If
            Catch exTouk As Exception
                log.ScriviLog($"WGTastieraTouch.Mostra (RiassegnaControllo): {exTouk.Message}{Environment.NewLine}{exTouk.StackTrace}")
            End Try
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.Mostra: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Rilegge dalla configurazione i valori che regolano la risposta al movimento del dito.
    ''' Va chiamata a ogni apertura della tastiera, cosi' una modifica nelle impostazioni ha
    ''' effetto senza riavviare il programma.
    ''' </summary>
    Public Sub RicaricaConfigurazioneTastiera()
        Try
            _frazioneIsteresiTasto = FrazioneIsteresiPerTolleranza(LeggiTolleranzaMovimento())
            _intervalloLetturaMs = MillisecondiPerIntervallo(LeggiIntervalloLettura())
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RicaricaConfigurazioneTastiera: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private _inApplicazioneAltezza As Boolean = False

    Public Sub ApplicaAltezzaVoluta()
        Try
            If _layoutGestitoDalContenitore Then Return
            If _inApplicazioneAltezza Then Return
            _inApplicazioneAltezza = True
            If Me.Parent IsNot Nothing Then
                Dim altezzaContenitore As Integer = Me.Parent.ClientSize.Height
                If altezzaContenitore > 0 Then
                    Dim grandezza As GrandezzaTasti = LeggiGrandezzaTasti()
                    Dim quota As Double = QuotaAltezzaPerGrandezza(grandezza)
                    If _touk IsNot Nothing Then
                        Dim quotaMediaBase As Double = QuotaAltezzaPerGrandezza(GrandezzaTasti.Medi)
                        If quotaMediaBase > 0 Then
                            quota = quota * (_touk.QuotaAltezzaTastiera(Me.Parent.ClientSize.Width, altezzaContenitore, quotaMediaBase) / quotaMediaBase)
                        End If
                    End If

                    Dim numeroRighe As Integer = _righeTasti.Count
                    If numeroRighe <= 0 Then numeroRighe = 5
                    Dim altezzaDaTasti As Integer = (AltezzaRigaPerGrandezza(grandezza) * numeroRighe) + AltezzaBarraDettatura + AltezzaBarraSuggerimenti

                    ' Sotto la meta' dello schermo la tastiera non scende: e' la misura a cui si
                    ' scrive comodamente su un pannello touch, indipendentemente da quanto spazio
                    ' il campo ospitante avrebbe concesso.
                    Dim altezzaMezzoSchermo As Integer = 0
                    Try
                        Dim schermo As Screen = Screen.FromControl(Me)
                        Dim altezzaSchermoRiferimento As Integer = 0
                        If schermo IsNot Nothing Then altezzaSchermoRiferimento = schermo.Bounds.Height
                        If _touk IsNot Nothing AndAlso _touk.LayoutAdattivo Then
                            Dim formaOspite As Form = Me.FindForm()
                            If formaOspite IsNot Nothing AndAlso formaOspite.ClientSize.Height > 0 Then altezzaSchermoRiferimento = formaOspite.ClientSize.Height
                        End If
                        altezzaMezzoSchermo = CInt(altezzaSchermoRiferimento * QuotaAltezzaSchermo)
                    Catch exSchermo As Exception
                        log.ScriviLog($"WGTastieraTouch.ApplicaAltezzaVoluta (schermo): {exSchermo.Message}")
                    End Try

                    Dim altezzaVoluta As Integer = CInt(altezzaContenitore * quota)
                    If altezzaDaTasti > altezzaVoluta Then altezzaVoluta = altezzaDaTasti
                    If altezzaMezzoSchermo > altezzaVoluta Then altezzaVoluta = altezzaMezzoSchermo
                    Dim tettoTastiera As Integer = CInt(altezzaContenitore * QuotaMassimaAltezza)
                    If tettoTastiera < AltezzaMinimaTastiera Then tettoTastiera = AltezzaMinimaTastiera
                    If tettoTastiera > altezzaContenitore Then tettoTastiera = altezzaContenitore
                    If altezzaVoluta < AltezzaMinimaTastiera Then altezzaVoluta = AltezzaMinimaTastiera
                    If altezzaVoluta > tettoTastiera Then altezzaVoluta = tettoTastiera
                    If altezzaVoluta < 1 Then altezzaVoluta = 1
                    Dim larghezzaVoluta As Integer = Me.Parent.ClientSize.Width
                    If larghezzaVoluta <= 0 Then larghezzaVoluta = Me.Width
                    Dim yVoluta As Integer = altezzaContenitore - altezzaVoluta
                    If Me.Left <> 0 OrElse Me.Top <> yVoluta OrElse Me.Width <> larghezzaVoluta OrElse Me.Height <> altezzaVoluta Then
                        Me.SetBounds(0, yVoluta, larghezzaVoluta, altezzaVoluta)
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ApplicaAltezzaVoluta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        Finally
            _inApplicazioneAltezza = False
        End Try
    End Sub

    Public Sub Nascondi()
        Try
            _attiva = False
            If ReferenceEquals(_tastieraAttivaSuSchermo, Me) Then _tastieraAttivaSuSchermo = Nothing
            If _inDettatura AndAlso _dettatura IsNot Nothing Then
                _dettatura.Annulla()
                _inDettatura = False
            End If
            NascondiTastoDettatura()
            Me.Visible = False
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.Nascondi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub RicostruisciLayout()
        Try
            _righeTasti.Clear()

            Dim rigaNumeri As New List(Of InfoTasto)
            For Each c As Char In "1234567890"
                rigaNumeri.Add(New InfoTasto With {.Testo = c.ToString(), .NomeParlato = c.ToString()})
            Next
            _righeTasti.Add(rigaNumeri)

            Select Case _modalitaTasti
                Case ModalitaTasti.Lettere
                    AggiungiRigaLettere("QWERTYUIOP")
                    AggiungiRigaLettere("ASDFGHJKL")
                    AggiungiRigaLettereConMaiuscolo("ZXCVBNM")
                Case ModalitaTasti.SimboliPaginaA
                    AggiungiRigaSimboli("-/:;()€&@")
                    AggiungiRigaSimboli(".,?!'""«»§")
                    AggiungiRigaSimboli("°ª£¢¥%*+=")
                Case ModalitaTasti.SimboliPaginaB
                    AggiungiRigaSimboli("[]{}#^~|\")
                    AggiungiRigaSimboli("_<>©®™±÷×")
                    AggiungiRigaSimboli("¶µ¦¨¯´¸¹²")
            End Select

            Dim rigaInferiore As New List(Of InfoTasto)
            Dim tastoToggle As New InfoTasto With {.EComando = True}
            If _modalitaTasti = ModalitaTasti.Lettere Then
                tastoToggle.Testo = "123"
                tastoToggle.NomeParlato = "simboli"
            Else
                tastoToggle.Testo = "ABC"
                tastoToggle.NomeParlato = "lettere"
            End If
            rigaInferiore.Add(tastoToggle)
            rigaInferiore.Add(New InfoTasto With {.Testo = "Dett.", .NomeParlato = "avvia dettatura", .EComando = True})
            rigaInferiore.Add(New InfoTasto With {.Testo = " ", .NomeParlato = "spazio", .EComando = True})
            rigaInferiore.Add(New InfoTasto With {.Testo = ",", .NomeParlato = "virgola"})
            rigaInferiore.Add(New InfoTasto With {.Testo = ".", .NomeParlato = "punto"})
            rigaInferiore.Add(New InfoTasto With {.Testo = "⌫", .NomeParlato = "elimina", .EComando = True})
            rigaInferiore.Add(New InfoTasto With {.Testo = "⏎", .NomeParlato = "invio", .EComando = True})
            _righeTasti.Add(rigaInferiore)

            _tastoBarraChiudi = New InfoTasto With {.Testo = "Chiudi tastiera", .NomeParlato = "chiudi tastiera", .EComando = True}
            _tastoBarraOpzioni = New InfoTasto With {.Testo = "Opzioni", .NomeParlato = "opzioni", .EComando = True}

            CalcolaAree()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RicostruisciLayout: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiungiRigaLettere(lettere As String)
        Try
            Dim riga As New List(Of InfoTasto)
            For Each c As Char In lettere
                ' Le lettere seguono lo stato del tasto maiuscolo: in minuscolo per impostazione
                ' predefinita, come sui telefoni.
                Dim carattere As String = If(_maiuscoloAttivo, Char.ToUpper(c).ToString(), Char.ToLower(c).ToString())
                riga.Add(New InfoTasto With {.Testo = carattere, .NomeParlato = carattere})
            Next
            _righeTasti.Add(riga)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiungiRigaLettere: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiungiRigaLettereConMaiuscolo(lettere As String)
        Try
            Dim riga As New List(Of InfoTasto)
            Dim tastoMaiuscolo As New InfoTasto With {.Testo = "⇧", .EComando = True}
            tastoMaiuscolo.NomeParlato = If(_maiuscoloAttivo, "maiuscolo attivo", "maiuscolo")
            riga.Add(tastoMaiuscolo)
            For Each c As Char In lettere
                Dim carattere As String = If(_maiuscoloAttivo, Char.ToUpper(c).ToString(), Char.ToLower(c).ToString())
                riga.Add(New InfoTasto With {.Testo = carattere, .NomeParlato = carattere})
            Next
            _righeTasti.Add(riga)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiungiRigaLettereConMaiuscolo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiungiRigaSimboli(simboli As String)
        Try
            Dim riga As New List(Of InfoTasto)
            For Each c As Char In simboli
                riga.Add(New InfoTasto With {.Testo = c.ToString(), .NomeParlato = c.ToString()})
            Next
            _righeTasti.Add(riga)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiungiRigaSimboli: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub AggiornaSuggerimenti(testoPrimaDelCursore As String)
        Try
            _ospiteAggiornaSuggerimenti = True
            RicalcolaSuggerimenti(testoPrimaDelCursore)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiornaSuggerimenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiornaSuggerimentiInterni()
        Try
            If _ospiteAggiornaSuggerimenti Then Return
            RicalcolaSuggerimenti(_parolaEco)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiornaSuggerimentiInterni: {ex.Message}")
        End Try
    End Sub

    Private Sub RicalcolaSuggerimenti(testoPrimaDelCursore As String)
        Try
            _tastiSuggerimenti.Clear()
            _parolaParzialeCorrente = ""

            If Not WGPredizione.SuggerimentiAttivi() Then
                CalcolaAree()
                Me.Invalidate()
                Return
            End If

            Dim motore As WGPredizione = WGPredizione.GetInstance()
            If motore Is Nothing Then
                CalcolaAree()
                Me.Invalidate()
                Return
            End If

            _parolaParzialeCorrente = motore.EstraiParolaInCorso(If(testoPrimaDelCursore, ""))

            Dim proposte As List(Of String) = motore.OttieniSuggerimenti(If(testoPrimaDelCursore, ""))
            For Each proposta As String In proposte
                _tastiSuggerimenti.Add(New InfoTasto With {.Testo = proposta, .NomeParlato = proposta, .EComando = True, .ESuggerimento = True})
            Next

            CalcolaAree()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RicalcolaSuggerimenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function AltezzaSuggerimentiEffettiva() As Integer
        Try
            Return AltezzaBarraSuggerimenti
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AltezzaSuggerimentiEffettiva: {ex.Message}")
            Return AltezzaBarraSuggerimenti
        End Try
    End Function

    ''' <summary>
    ''' Numero di colonne della griglia, pari alla riga di caratteri con piu' tasti. Serve a dare
    ''' a tutte le righe la stessa larghezza di tasto, cosi' le righe piu' corte restano
    ''' incolonnate invece di allargarsi.
    ''' </summary>
    Private Function ColonneRiferimento() As Integer
        Try
            Dim massimo As Integer = 0
            For indiceRiga As Integer = 0 To _righeTasti.Count - 2
                If _righeTasti(indiceRiga).Count > massimo Then massimo = _righeTasti(indiceRiga).Count
            Next
            If massimo < 1 Then massimo = 10
            Return massimo
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ColonneRiferimento: {ex.Message}")
            Return 10
        End Try
    End Function

    Private Sub CalcolaAree()
        Try
            If Me.Width <= 0 OrElse Me.Height <= 0 Then Return

            Dim larghezzaMetaBarraChiudi As Single = Me.Width / 2.0F
            _tastoBarraChiudi.Area = New RectangleF(0, 0, larghezzaMetaBarraChiudi, AltezzaBarraDettatura)
            _tastoBarraOpzioni.Area = New RectangleF(larghezzaMetaBarraChiudi, 0, Me.Width - larghezzaMetaBarraChiudi, AltezzaBarraDettatura)

            Dim numeroRighe As Integer = _righeTasti.Count
            If numeroRighe = 0 Then Return

            ' La barra dei suggerimenti resta riservata anche da vuota, cosi' le righe dei tasti
            ' non saltano quando le proposte compaiono. Se pero' lo spazio non basta a tenere le
            ' righe colpibili col dito, la barra cede il posto ai tasti.
            Dim altezzaSuggerimenti As Integer = AltezzaSuggerimentiEffettiva()
            Dim altezzaMinimaTasti As Single = numeroRighe * AltezzaMinimaRigaTasti
            If (Me.Height - AltezzaBarraDettatura - altezzaSuggerimenti) < altezzaMinimaTasti Then altezzaSuggerimenti = 0

            If altezzaSuggerimenti > 0 AndAlso _tastiSuggerimenti.Count > 0 Then
                Dim larghezzaSuggerimento As Single = Me.Width / _tastiSuggerimenti.Count
                For indiceSuggerimento As Integer = 0 To _tastiSuggerimenti.Count - 1
                    _tastiSuggerimenti(indiceSuggerimento).Area = New RectangleF(indiceSuggerimento * larghezzaSuggerimento, AltezzaBarraDettatura, larghezzaSuggerimento, altezzaSuggerimenti)
                Next
            End If

            _altezzaSuggerimentiApplicata = altezzaSuggerimenti

            Dim inizioTasti As Integer = AltezzaBarraDettatura + altezzaSuggerimenti
            Dim altezzaDisponibile As Single = Me.Height - inizioTasti
            If altezzaDisponibile < numeroRighe Then altezzaDisponibile = numeroRighe

            ' L'ultima riga, quella dello spazio, e' piu' alta delle altre: sui tablet e' la riga
            ' che si colpisce piu' spesso e va tenuta ben separata da quelle delle lettere.
            Dim altezzaRiga As Single = altezzaDisponibile / (numeroRighe - 1 + FattoreAltezzaRigaInferiore)
            Dim altezzaRigaInferiore As Single = altezzaRiga * FattoreAltezzaRigaInferiore

            For indiceRiga As Integer = 0 To _righeTasti.Count - 1
                Dim riga As List(Of InfoTasto) = _righeTasti(indiceRiga)
                Dim yRiga As Single = inizioTasti + (indiceRiga * altezzaRiga)
                Dim altezzaRigaCorrente As Single = If(indiceRiga = _righeTasti.Count - 1, altezzaRigaInferiore, altezzaRiga)

                If indiceRiga = _righeTasti.Count - 1 Then
                    ' Ultima riga: toggle 123/ABC, maiuscolo, spazio, invio, elimina. Lo spazio
                    ' prende tutta la larghezza che avanza dagli altri, a larghezza fissa.
                    Dim larghezzaComando As Single = Me.Width / (riga.Count + 1)
                    Dim numeroComandi As Integer = Math.Max(0, riga.Count - 1)
                    Dim larghezzaSpazio As Single = Me.Width - (larghezzaComando * numeroComandi)
                    If larghezzaSpazio < larghezzaComando Then larghezzaSpazio = larghezzaComando

                    Dim xCorrente As Single = 0
                    For indiceTasto As Integer = 0 To riga.Count - 1
                        Dim larghezzaCorrente As Single = If(riga(indiceTasto).Testo = " ", larghezzaSpazio, larghezzaComando)
                        riga(indiceTasto).Area = New RectangleF(xCorrente, yRiga, larghezzaCorrente, altezzaRigaCorrente)
                        xCorrente += larghezzaCorrente
                    Next
                Else
                    ' La larghezza non supera una proporzione fissa rispetto all'altezza di riga,
                    ' altrimenti su schermi larghi i tasti diventano rettangoli schiacciati. Lo
                    ' spazio che avanza viene diviso ai due lati, cosi' la riga resta centrata.
                    Dim larghezzaTasto As Single = Me.Width / ColonneRiferimento()
                    Dim larghezzaMassima As Single = altezzaRiga * RapportoMassimoLarghezzaTasto
                    If _touk IsNot Nothing AndAlso _touk.LayoutAdattivo Then larghezzaMassima = Single.MaxValue
                    If larghezzaTasto > larghezzaMassima Then larghezzaTasto = larghezzaMassima
                    Dim margineLaterale As Single = (Me.Width - (larghezzaTasto * riga.Count)) / 2.0F
                    If margineLaterale < 0.0F Then margineLaterale = 0.0F
                    For indiceTasto As Integer = 0 To riga.Count - 1
                        riga(indiceTasto).Area = New RectangleF(margineLaterale + (indiceTasto * larghezzaTasto), yRiga, larghezzaTasto, altezzaRigaCorrente)
                    Next
                End If
            Next

            _ultimaLarghezzaAree = Me.Width
            _ultimaAltezzaAree = Me.Height
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.CalcolaAree: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        Try
            MyBase.OnVisibleChanged(e)
            If Not Me.Visible Then Return
            If Not _attiva Then Return
            ' Quando la tastiera torna visibile il contenitore puo' avere dimensioni diverse da
            ' quelle che aveva al momento della creazione: ricalcoliamo le aree dei tasti, senza
            ' le quali nessun tocco troverebbe nulla.
            CalcolaAree()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnVisibleChanged: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            If _attiva AndAlso Not _inApplicazioneAltezza AndAlso Not _layoutGestitoDalContenitore Then ApplicaAltezzaVoluta()
            CalcolaAree()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnResize: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Try
            MyBase.OnPaint(e)
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias

            Using sfondoBarra As New SolidBrush(Color.FromArgb(230, 230, 230))
                g.FillRectangle(sfondoBarra, New RectangleF(0, 0, Me.Width, AltezzaBarraDettatura))
            End Using

            DisegnaTasto(g, _tastoBarraChiudi, Color.FromArgb(210, 210, 210))
            DisegnaTasto(g, _tastoBarraOpzioni, Color.FromArgb(210, 210, 210))

            If _altezzaSuggerimentiApplicata > 0 Then
                Using sfondoSuggerimenti As New SolidBrush(Color.FromArgb(245, 245, 245))
                    g.FillRectangle(sfondoSuggerimenti, New RectangleF(0, AltezzaBarraDettatura, Me.Width, _altezzaSuggerimentiApplicata))
                End Using
                For Each suggerimento As InfoTasto In _tastiSuggerimenti
                    DisegnaTasto(g, suggerimento, Color.FromArgb(225, 235, 245))
                Next
            End If

            If _inDettatura Then
                Using pennaRossa As New Pen(Color.Red, 3)
                    g.DrawRectangle(pennaRossa, 1, 1, Me.Width - 2, AltezzaBarraDettatura - 2)
                End Using
            End If

            For Each riga As List(Of InfoTasto) In _righeTasti
                For Each tasto As InfoTasto In riga
                    Dim coloreSfondo As Color = If(tasto.EComando, Color.FromArgb(220, 220, 220), Color.White)
                    If ReferenceEquals(tasto, _tastoEsploratoCorrente) Then
                        coloreSfondo = Color.FromArgb(180, 200, 255)
                    End If
                    DisegnaTasto(g, tasto, coloreSfondo)
                Next
            Next
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnPaint: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub DisegnaTasto(g As Graphics, tasto As InfoTasto, coloreSfondo As Color)
        Try
            If tasto Is Nothing Then Return
            Dim margineOrizzontale As Single = Math.Max(3.0F, tasto.Area.Width * 0.06F)
            Dim margineVerticale As Single = Math.Max(3.0F, tasto.Area.Height * 0.07F)
            Dim larghezzaRidotta As Single = tasto.Area.Width - (margineOrizzontale * 2.0F)
            Dim altezzaRidotta As Single = tasto.Area.Height - (margineVerticale * 2.0F)
            If larghezzaRidotta < 1.0F Then larghezzaRidotta = 1.0F
            If altezzaRidotta < 1.0F Then altezzaRidotta = 1.0F
            Dim areaRidotta As New RectangleF(tasto.Area.X + margineOrizzontale, tasto.Area.Y + margineVerticale, larghezzaRidotta, altezzaRidotta)
            Using pennello As New SolidBrush(coloreSfondo)
                g.FillRectangle(pennello, areaRidotta)
            End Using
            Using penna As New Pen(Color.Gray)
                g.DrawRectangle(penna, Rectangle.Round(areaRidotta))
            End Using
            Using formato As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                Dim dimensioneCarattere As Single = Math.Max(11.0F, Math.Min(34.0F, altezzaRidotta * 0.42F))
                Using carattere As New Font(Me.Font.FontFamily, dimensioneCarattere, FontStyle.Regular)
                    g.DrawString(tasto.Testo, carattere, Brushes.Black, areaRidotta, formato)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.DisegnaTasto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private _ultimaLarghezzaAree As Integer = -1
    Private _ultimaAltezzaAree As Integer = -1

    ''' <summary>
    ''' Restituisce il tasto sotto al dito mantenendo quello gia' selezionato finche' il dito non
    ''' entra nel vicino oltre una frazione della dimensione del tasto stesso. Un tremore o una
    ''' deviazione involontaria vicino al confine non cambiano la selezione.
    ''' </summary>
    Private Function TrovaTastoConIsteresi(x As Single, y As Single) As InfoTasto
        Try
            Dim tasto As InfoTasto = TrovaTastoInPunto(x, y)
            If tasto Is Nothing Then Return Nothing
            If _tastoEsploratoCorrente Is Nothing Then Return tasto
            If ReferenceEquals(tasto, _tastoEsploratoCorrente) Then Return tasto

            Dim area As RectangleF = _tastoEsploratoCorrente.Area
            Dim margineOrizzontale As Single = area.Width * _frazioneIsteresiTasto
            Dim margineVerticale As Single = area.Height * _frazioneIsteresiTasto
            Dim areaAllargata As New RectangleF(area.X - margineOrizzontale, area.Y - margineVerticale, area.Width + (margineOrizzontale * 2.0F), area.Height + (margineVerticale * 2.0F))
            If areaAllargata.Contains(x, y) Then Return _tastoEsploratoCorrente

            Return tasto
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.TrovaTastoConIsteresi: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Annuncia il tasto su cui si trova il dito. Ogni tasto attraversato viene letto: se dalla
    ''' lettura precedente non e' ancora trascorso l'intervallo, il tasto resta in attesa e viene
    ''' letto appena l'intervallo scade, quindi nessuna lettera va persa durante lo scorrimento.
    ''' </summary>
    Private Sub RichiediEsplorazione(tasto As InfoTasto)
        Try
            If tasto Is Nothing Then Return

            If ReferenceEquals(tasto, _tastoEsploratoCorrente) Then
                AnnullaCandidato()
                Return
            End If

            ' Senza selezione in corso il dito si e' appena riappoggiato, quindi il tasto va reso
            ' subito selezione anziche' finire in coda: se il distacco arriva prima della lettura
            ' differita non resta niente da inserire, ed e' cosi' che andava persa la seconda
            ' lettera di una doppia battuta in fretta.
            If _tastoEsploratoCorrente IsNot Nothing Then
                If _oraUltimaLettura > 0 AndAlso (Environment.TickCount - _oraUltimaLettura) < _intervalloLetturaMs Then
                    _tastoCandidato = tasto
                    If Not _timerAnnuncioEsplorazione.Enabled Then _timerAnnuncioEsplorazione.Start()
                    Return
                End If
            End If

            AnnullaCandidato()
            EsploraTasto(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RichiediEsplorazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AnnullaCandidato()
        Try
            _tastoCandidato = Nothing
            _timerAnnuncioEsplorazione.Stop()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AnnullaCandidato: {ex.Message}")
        End Try
    End Sub

    Private Sub _timerAnnuncioEsplorazione_Tick(sender As Object, e As EventArgs) Handles _timerAnnuncioEsplorazione.Tick
        Try
            If _tastoCandidato Is Nothing Then
                _timerAnnuncioEsplorazione.Stop()
                Return
            End If
            If ReferenceEquals(_tastoCandidato, _tastoEsploratoCorrente) Then
                AnnullaCandidato()
                Return
            End If
            If _oraUltimaLettura > 0 AndAlso (Environment.TickCount - _oraUltimaLettura) < _intervalloLetturaMs Then Return

            Dim tastoDaAnnunciare As InfoTasto = _tastoCandidato
            AnnullaCandidato()
            EsploraTasto(tastoDaAnnunciare)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._timerAnnuncioEsplorazione_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Identificativo del tasto che si trova sotto il dito, chiesto da WGTouk durante
    ''' l'esplorazione tattile. Senza questa risposta WGTouk conosce solo la distanza percorsa
    ''' e annuncia ogni volta che il dito si sposta oltre la soglia, anche restando sullo stesso
    ''' tasto; conoscendo il tasto annuncia al cambio di tasto, e un tasto attraversato di
    ''' striscio non viene letto. Vale l'isteresi gia' usata dalla tastiera, cosi' la risposta
    ''' coincide con il tasto che verrebbe poi inserito.
    ''' </summary>
    Public Function IdentificativoElementoTattile(puntoSchermo As Point) As String Implements IWGElementoTattile.IdentificativoElementoTattile
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return Nothing
            If Not _attiva OrElse Not Me.Visible Then Return Nothing

            Dim puntoLocale As Point = Me.PointToClient(puntoSchermo)
            If puntoLocale.X < 0 OrElse puntoLocale.Y < 0 Then Return Nothing
            If puntoLocale.X >= Me.Width OrElse puntoLocale.Y >= Me.Height Then Return Nothing

            Dim tasto As InfoTasto = TrovaTastoConIsteresi(puntoLocale.X, puntoLocale.Y)
            If tasto Is Nothing Then Return Nothing

            Return IdentificativoTasto(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.IdentificativoElementoTattile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function IdentificativoTasto(tasto As InfoTasto) As String
        Try
            If tasto Is Nothing Then Return Nothing
            If ReferenceEquals(tasto, _tastoBarraChiudi) Then Return "barra:chiudi"
            If ReferenceEquals(tasto, _tastoBarraOpzioni) Then Return "barra:opzioni"

            For indice As Integer = 0 To _tastiSuggerimenti.Count - 1
                If ReferenceEquals(_tastiSuggerimenti(indice), tasto) Then Return "suggerimento:" & indice.ToString()
            Next

            For indiceRiga As Integer = 0 To _righeTasti.Count - 1
                Dim riga As List(Of InfoTasto) = _righeTasti(indiceRiga)
                If riga Is Nothing Then Continue For
                For indiceTasto As Integer = 0 To riga.Count - 1
                    If ReferenceEquals(riga(indiceTasto), tasto) Then Return "tasto:" & indiceRiga.ToString() & ":" & indiceTasto.ToString()
                Next
            Next

            If String.IsNullOrEmpty(tasto.Testo) Then Return Nothing
            Return "tasto:" & tasto.Testo
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.IdentificativoTasto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function TrovaTastoInPunto(x As Single, y As Single) As InfoTasto
        Try
            If Me.Width <> _ultimaLarghezzaAree OrElse Me.Height <> _ultimaAltezzaAree Then
                CalcolaAree()
            End If
            If _tastoBarraChiudi Is Nothing OrElse _tastoBarraOpzioni Is Nothing Then Return Nothing
            If _tastoBarraChiudi.Area.Contains(x, y) Then Return _tastoBarraChiudi
            If _tastoBarraOpzioni.Area.Contains(x, y) Then Return _tastoBarraOpzioni
            For Each suggerimento As InfoTasto In _tastiSuggerimenti
                If suggerimento.Area.Contains(x, y) Then Return suggerimento
            Next
            For Each riga As List(Of InfoTasto) In _righeTasti
                For Each tasto As InfoTasto In riga
                    If tasto.Area.Contains(x, y) Then Return tasto
                Next
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.TrovaTastoInPunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function TastoSottoPuntoSchermo(xSchermo As Integer, ySchermo As Integer) As InfoTasto
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return Nothing
            Dim puntoLocale As Point = Me.PointToClient(New Point(xSchermo, ySchermo))
            Const margine As Integer = 24
            If puntoLocale.X < -margine OrElse puntoLocale.Y < -margine Then Return Nothing
            If puntoLocale.X >= Me.Width + margine OrElse puntoLocale.Y >= Me.Height + margine Then Return Nothing
            If puntoLocale.X < 0 Then puntoLocale.X = 0
            If puntoLocale.Y < 0 Then puntoLocale.Y = 0
            If puntoLocale.X >= Me.Width Then puntoLocale.X = Me.Width - 1
            If puntoLocale.Y >= Me.Height Then puntoLocale.Y = Me.Height - 1
            Return TrovaTastoConIsteresi(CSng(puntoLocale.X), CSng(puntoLocale.Y))
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.TastoSottoPuntoSchermo: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Vero solo se la conferma appena registrata riguarda ancora il contatto in corso. La
    ''' guardia serve a non ripetere la lettera quando EsplorazioneTerminata e MouseUp arrivano
    ''' entrambi per lo stesso distacco, quindi vale pochi centesimi di secondo: oltre quel
    ''' tempo decade da se', e un rilascio perso da WGTouk non puo' piu' bloccare le lettere
    ''' successive.
    ''' </summary>
    Private Function GuardiaRilascioAttiva() As Boolean
        Try
            If Not _rilascioGiaConfermato Then Return False
            If _oraRilascioConfermato = 0 Then
                _rilascioGiaConfermato = False
                Return False
            End If
            If (Environment.TickCount - _oraRilascioConfermato) > DurataGuardiaRilascioMs Then
                _rilascioGiaConfermato = False
                _oraRilascioConfermato = 0
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.GuardiaRilascioAttiva: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub SegnaRilascioConfermato()
        Try
            _rilascioGiaConfermato = True
            _oraRilascioConfermato = Environment.TickCount
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.SegnaRilascioConfermato: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Apre un nuovo contatto: il dito si e' appena appoggiato, quindi tutto cio' che riguarda
    ''' il contatto precedente va azzerato. Il candidato in coda non viene toccato, perche' con
    ''' l'esplorazione lenta l'annuncio differito appartiene ancora al dito appoggiato.
    ''' </summary>
    Private Sub AvviaNuovoContatto()
        Try
            _rilascioGiaConfermato = False
            _oraRilascioConfermato = 0
            _pressioneLungaScattata = False
            _oraPressioneLungaScattata = 0
            _menuAccentiAperto = False
            _tastoSottoDito = Nothing
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AvviaNuovoContatto: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Vero se il distacco va ignorato perche' la pressione prolungata ha aperto il menu delle
    ''' lettere accentate. Fuori dalla finestra di tempo del gesto la segnalazione decade: senza
    ''' scadenza una pressione prolungata che non ha aperto nulla zittiva il rilascio successivo.
    ''' </summary>
    Private Function PressioneLungaDaConsumare() As Boolean
        Try
            If Not _pressioneLungaScattata Then Return False
            If _menuAccentiAperto Then Return True
            If _oraPressioneLungaScattata = 0 Then Return True
            If (Environment.TickCount - _oraPressioneLungaScattata) > DurataGuardiaPressioneLungaMs Then
                _pressioneLungaScattata = False
                _oraPressioneLungaScattata = 0
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.PressioneLungaDaConsumare: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Tasto da confermare al pointer-up. Vince il candidato in coda, cioe' il tasto su cui il
    ''' dito si era fermato e che l'intervallo di lettura non aveva ancora fatto annunciare:
    ''' quella coda non deve decidere cosa si scrive, altrimenti sollevando il dito subito dopo
    ''' esserci arrivati non veniva inserito nulla o veniva inserita la lettera precedente.
    ''' Segue il tasto annunciato dalla voce, scavalcato dal tasto sotto il punto di distacco
    ''' solo quando il dito ne e' uscito oltre la tolleranza impostata. Restano come ultime
    ''' riserve il tasto sotto il dito, il punto di appoggio e il punto di distacco, che servono
    ''' al tap breve, quello che non passa dall'esplorazione.
    ''' </summary>
    Private Function TastoDaConfermareAlRilascio(xSchermo As Integer, ySchermo As Integer) As InfoTasto
        Try
            If _tastoCandidato IsNot Nothing Then Return _tastoCandidato

            Dim tastoDistacco As InfoTasto = TastoSottoPuntoSchermo(xSchermo, ySchermo)

            If _tastoEsploratoCorrente IsNot Nothing Then
                If tastoDistacco IsNot Nothing AndAlso Not ReferenceEquals(tastoDistacco, _tastoEsploratoCorrente) Then
                    log.ScriviLog($"WGTastieraTouch.TastoDaConfermareAlRilascio: distacco fuori dal tasto annunciato, si conferma {tastoDistacco.Testo} invece di {_tastoEsploratoCorrente.Testo}")
                    Return tastoDistacco
                End If
                Return _tastoEsploratoCorrente
            End If

            If _tastoSottoDito IsNot Nothing Then Return _tastoSottoDito
            If _touk IsNot Nothing Then
                Dim avvio As Point = _touk.PuntoInizioGesto
                Dim tastoAvvio As InfoTasto = TastoSottoPuntoSchermo(avvio.X, avvio.Y)
                If tastoAvvio IsNot Nothing Then Return tastoAvvio
            End If
            Return tastoDistacco
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.TastoDaConfermareAlRilascio: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Scrive la lettera (o attiva il tasto) sul distacco del dito. Un solo commit per contatto:
    ''' EsplorazioneTerminata e MouseUp possono arrivare entrambi, e il secondo non deve ripetere
    ''' il carattere ne' saltare il primo se l'altro non e' mai arrivato.
    ''' </summary>
    Private Sub ConfermaLetteraAlRilascio(xSchermo As Integer, ySchermo As Integer)
        Try
            If GuardiaRilascioAttiva() Then Return
            If Not _attiva Then
                log.ScriviLog("WGTastieraTouch.ConfermaLetteraAlRilascio: tastiera non attiva, lettera non inserita")
                Return
            End If

            AnnullaPressioneLunga()
            If PressioneLungaDaConsumare() Then
                _pressioneLungaScattata = False
                _oraPressioneLungaScattata = 0
                _menuAccentiAperto = False
                SegnaRilascioConfermato()
                log.ScriviLog("WGTastieraTouch.ConfermaLetteraAlRilascio: distacco dopo pressione prolungata, lettera non inserita")
                Return
            End If

            If _tastoEsploratoCorrente Is Nothing AndAlso _tastoCandidato Is Nothing AndAlso _tastoSottoDito Is Nothing Then
                If Not ContienePuntoSchermo(xSchermo, ySchermo) Then
                    log.ScriviLog($"WGTastieraTouch.ConfermaLetteraAlRilascio: nessun tasto in memoria e distacco fuori dalla tastiera in {xSchermo},{ySchermo}")
                    Return
                End If
            End If

            Dim tasto As InfoTasto = TastoDaConfermareAlRilascio(xSchermo, ySchermo)
            If tasto Is Nothing Then
                log.ScriviLog($"WGTastieraTouch.ConfermaLetteraAlRilascio: nessun tasto individuato al distacco in {xSchermo},{ySchermo}")
                Return
            End If

            SegnaRilascioConfermato()
            AnnullaCandidato()

            If ReferenceEquals(tasto, _tastoBarraChiudi) Then
                RaiseEvent ChiudiRichiesto()
                Return
            End If
            If ReferenceEquals(tasto, _tastoBarraOpzioni) Then
                MostraMenuOpzioni()
                Return
            End If

            If RichiedeDoppioTocco(tasto) Then
                _tastoEsploratoCorrente = tasto
                Return
            End If

            AttivaTasto(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ConfermaLetteraAlRilascio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Try
            MyBase.OnMouseDown(e)

            If Not _attiva Then
                Try
                    Console.Beep(200, 150)
                Catch
                End Try
                Return
            End If

            If _inDettatura Then
                FermaDettatura()
                Return
            End If

            Dim tasto As InfoTasto = TrovaTastoInPunto(e.X, e.Y)
            If tasto Is Nothing Then
                Try
                    Console.Beep(500, 200)
                Catch
                End Try
                Return
            End If

            Try
                Console.Beep(1200, 60)
            Catch
            End Try

            If ReferenceEquals(tasto, _tastoBarraChiudi) Then
                SegnaRilascioConfermato()
                RaiseEvent ChiudiRichiesto()
                Return
            End If
            If ReferenceEquals(tasto, _tastoBarraOpzioni) Then
                SegnaRilascioConfermato()
                MostraMenuOpzioni()
                Return
            End If

            _ditoGiu = True
            AvviaNuovoContatto()
            _tastoSottoDito = tasto
            _esplorazioneInCorso = False
            AnnullaCandidato()
            _tastieraAttivaSuSchermo = Me
            Try
                If Me.IsHandleCreated AndAlso _touk IsNot Nothing Then _touk.RiassegnaControllo(Me.Handle)
            Catch exTouk As Exception
                log.ScriviLog($"WGTastieraTouch.OnMouseDown (RiassegnaControllo): {exTouk.Message}")
            End Try
            _pressioneLungaScattata = False
            AnnullaPressioneLunga()
            _puntoPressioneX = e.X
            _puntoPressioneY = e.Y
            Dim holdGestitoDaTouk As Boolean = (_touk IsNot Nothing AndAlso _touk.TouchAttivo)
            If Not holdGestitoDaTouk AndAlso Not tasto.EComando AndAlso HaVariantiAccentate(tasto.Testo) Then
                _tastoPressioneLungaCorrente = tasto
                _timerPressioneLunga.Interval = DurataHoldAllineataTouk()
                _timerPressioneLunga.Start()
            End If

            Select Case _modalitaDigitazioneCorrente
                Case ModalitaDigitazione.Tocco
                    ' Anche in modalita' "Tocco" il tasto va annunciato appena il dito lo sfiora:
                    ' senza voce non si puo' sapere cosa si sta per inserire. L'inserimento vero
                    ' avviene al rilascio (vedi OnMouseUp).
                    EsploraTasto(tasto)
                Case ModalitaDigitazione.DoppioTocco
                    EsploraTasto(tasto)
                Case ModalitaDigitazione.Scorrimento
                    _inTrascinamento = True
                    EsploraTasto(tasto)
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnMouseDown: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Try
            MyBase.OnMouseMove(e)
            If Not _attiva Then Return

            Dim deltaX As Single = e.X - _puntoPressioneX
            Dim deltaY As Single = e.Y - _puntoPressioneY
            If Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) > SogliaMovimentoHoldAllineataTouk() Then
                _esplorazioneInCorso = True
                AnnullaPressioneLunga()
            End If

            Dim ditoAppoggiato As Boolean = _ditoGiu OrElse e.Button = MouseButtons.Left

            If _modalitaDigitazioneCorrente = ModalitaDigitazione.Scorrimento Then
                If Not _inTrascinamento AndAlso Not ditoAppoggiato Then Return
            ElseIf _modalitaDigitazioneCorrente = ModalitaDigitazione.Tocco Then
                ' In modalita' "Tocco" il dito appoggiato che si sposta deve annunciare i tasti
                ' che incontra, come sui telefoni: si esplora la tastiera senza scriverci sopra,
                ' e si inserisce solo il tasto su cui si stacca il dito.
                If Not ditoAppoggiato Then Return
            Else
                Return
            End If

            Dim tastoSotto As InfoTasto = TrovaTastoConIsteresi(e.X, e.Y)
            If tastoSotto IsNot Nothing Then _tastoSottoDito = tastoSotto
            RichiediEsplorazione(tastoSotto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnMouseMove: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        Try
            MyBase.OnMouseUp(e)
            _ditoGiu = False
            _esplorazioneInCorso = False
            _inTrascinamento = False
            _oraUltimaLettura = 0
            If Not _attiva Then Return

            Dim puntoSchermo As Point = Me.PointToScreen(New Point(e.X, e.Y))
            ConfermaLetteraAlRilascio(puntoSchermo.X, puntoSchermo.Y)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnMouseUp: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        Try
            MyBase.OnMouseDoubleClick(e)
            If Not _attiva Then Return
            If _inDettatura Then Return

            Dim tasto As InfoTasto = TrovaTastoInPunto(e.X, e.Y)
            If tasto Is Nothing Then Return
            If ReferenceEquals(tasto, _tastoBarraChiudi) Then
                RaiseEvent ChiudiRichiesto()
                Return
            End If
            If ReferenceEquals(tasto, _tastoBarraOpzioni) Then
                MostraMenuOpzioni()
                Return
            End If
            ' Le lettere sono gia' state scritte al MouseUp. Il doppio clic resta solo per
            ' spazio, elimina e invio.
            If Not RichiedeDoppioTocco(tasto) Then Return
            AttivaTasto(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnMouseDoubleClick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub EsploraTasto(tasto As InfoTasto)
        Try
            _tastoEsploratoCorrente = tasto
            _oraUltimaLettura = Environment.TickCount
            Try
                If _suoni IsNot Nothing Then _suoni.TastoEsplorato()
            Catch exSuono As Exception
                log.ScriviLog($"WGTastieraTouch.EsploraTasto (suono): {exSuono.Message}")
            End Try
            If _sintesiVocale IsNot Nothing AndAlso tasto IsNot Nothing Then
                _sintesiVocale.LeggiTesto(tasto.NomeParlato)
            End If
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.EsploraTasto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Const ChiaveConfigEcoDigitazione As String = "echo_digitazione"

    Private _parolaEco As String = ""

    Private Function LeggiImpostazioneEco() As String
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(cfg.LeggiValore(ChiaveConfigEcoDigitazione), "").Trim()
            If valore = "0" OrElse valore = "1" OrElse valore = "2" OrElse valore = "3" Then Return valore
            Return "3"
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.LeggiImpostazioneEco: {ex.Message}")
            Return "3"
        End Try
    End Function

    Private Function NomeCarattereParlato(c As Char) As String
        Try
            Select Case c
                Case " "c
                    Return "spazio"
                Case "."c
                    Return "punto"
                Case ","c
                    Return "virgola"
                Case ";"c
                    Return "punto e virgola"
                Case ":"c
                    Return "due punti"
                Case "?"c
                    Return "punto interrogativo"
                Case "!"c
                    Return "punto esclamativo"
                Case "@"c
                    Return "chiocciola"
                Case "#"c
                    Return "cancelletto"
                Case "€"c
                    Return "euro"
                Case "£"c
                    Return "sterlina"
                Case "¥"c
                    Return "yen"
                Case "¢"c
                    Return "centesimo"
                Case "&"c
                    Return "e commerciale"
                Case "%"c
                    Return "percento"
                Case "*"c
                    Return "asterisco"
                Case "+"c
                    Return "piu'"
                Case "-"c
                    Return "trattino"
                Case "_"c
                    Return "trattino basso"
                Case "="c
                    Return "uguale"
                Case "/"c
                    Return "barra"
                Case "\"c
                    Return "barra rovesciata"
                Case "|"c
                    Return "barra verticale"
                Case "~"c
                    Return "tilde"
                Case "^"c
                    Return "circonflesso"
                Case "("c
                    Return "parentesi aperta"
                Case ")"c
                    Return "parentesi chiusa"
                Case "["c
                    Return "parentesi quadra aperta"
                Case "]"c
                    Return "parentesi quadra chiusa"
                Case "{"c
                    Return "parentesi graffa aperta"
                Case "}"c
                    Return "parentesi graffa chiusa"
                Case "<"c
                    Return "minore"
                Case ">"c
                    Return "maggiore"
                Case "«"c
                    Return "virgolette aperte"
                Case "»"c
                    Return "virgolette chiuse"
                Case "'"c
                    Return "apostrofo"
                Case """"c
                    Return "virgolette"
                Case "§"c
                    Return "paragrafo"
                Case "°"c
                    Return "grado"
                Case "ª"c
                    Return "ordinale femminile"
            End Select
            If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then Return c.ToString() & " maiuscola"
            Return c.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.NomeCarattereParlato: {ex.Message}")
            Return c.ToString()
        End Try
    End Function

    Private Sub AzzeraParolaEco()
        Try
            _parolaEco = ""
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AzzeraParolaEco: {ex.Message}")
        End Try
    End Sub

    Private Sub RimuoviUltimoCarattereEco()
        Try
            If _parolaEco.Length > 0 Then _parolaEco = _parolaEco.Substring(0, _parolaEco.Length - 1)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RimuoviUltimoCarattereEco: {ex.Message}")
        End Try
    End Sub

    Private Sub AnnunciaParolaCompletata(parola As String)
        Try
            If String.IsNullOrWhiteSpace(parola) Then Return
            If _sintesiVocale Is Nothing Then Return
            Dim impostazione As String = LeggiImpostazioneEco()
            If impostazione = "2" OrElse impostazione = "3" Then _sintesiVocale.LeggiTesto(parola)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AnnunciaParolaCompletata: {ex.Message}")
        End Try
    End Sub

    Private Sub AnnunciaInserimento(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return

            Dim carattere As Char = testo(0)
            Dim fineParola As Boolean = (carattere = " "c OrElse Char.IsPunctuation(carattere) OrElse Char.IsSymbol(carattere))

            If Not fineParola Then
                _parolaEco &= carattere
                If _sintesiVocale Is Nothing Then
                    AggiornaSuggerimentiInterni()
                    Return
                End If
                Dim impostazioneCarattere As String = LeggiImpostazioneEco()
                If impostazioneCarattere = "1" OrElse impostazioneCarattere = "3" Then
                    _sintesiVocale.LeggiTesto(NomeCarattereParlato(carattere))
                End If
                AggiornaSuggerimentiInterni()
                Return
            End If

            Dim parolaConclusa As String = _parolaEco.Trim()
            _parolaEco = ""
            AggiornaSuggerimentiInterni()

            If _sintesiVocale Is Nothing Then Return

            Dim impostazione As String = LeggiImpostazioneEco()
            If impostazione = "0" Then Return

            Dim nomeSegno As String = NomeCarattereParlato(carattere)

            If impostazione = "1" Then
                _sintesiVocale.LeggiTesto(nomeSegno)
                Return
            End If

            If parolaConclusa.Length = 0 Then
                If impostazione = "3" Then _sintesiVocale.LeggiTesto(nomeSegno)
                Return
            End If

            If impostazione = "2" Then
                _sintesiVocale.LeggiTesto(parolaConclusa)
            Else
                _sintesiVocale.LeggiTesto(parolaConclusa & " " & nomeSegno)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AnnunciaInserimento: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Azzera il conteggio delle lettere uguali di seguito. Va chiamato dai tasti che non sono
    ''' lettere, perche' interrompono la serie.
    ''' </summary>
    Private Sub AzzeraSerieLettere()
        Try
            _letteraSerieCorrente = ""
            _lettereConsecutive = 0
            _oraUltimaLetteraSerie = 0
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AzzeraSerieLettere: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Vero quando la lettera sarebbe la terza uguale di seguito. La doppia si scrive sempre, la
    ''' tripla no. La serie si interrompe con una lettera diversa, con un carattere che non e' una
    ''' lettera o dopo due secondi di pausa, cosi' chi vuole davvero la tripla la ottiene
    ''' rallentando fra la seconda e la terza.
    ''' </summary>
    Private Function SuperaLimiteLettereConsecutive(testo As String) As Boolean
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.Length <> 1 Then
                AzzeraSerieLettere()
                Return False
            End If
            If Not Char.IsLetter(testo(0)) Then
                AzzeraSerieLettere()
                Return False
            End If

            Dim serieValida As Boolean = _oraUltimaLetteraSerie > 0 AndAlso (Environment.TickCount - _oraUltimaLetteraSerie) <= DurataSerieLettereMs
            If serieValida AndAlso String.Equals(testo, _letteraSerieCorrente, StringComparison.OrdinalIgnoreCase) Then
                If _lettereConsecutive >= MassimoLettereConsecutive Then Return True
                _lettereConsecutive += 1
            Else
                _letteraSerieCorrente = testo
                _lettereConsecutive = 1
            End If

            _oraUltimaLetteraSerie = Environment.TickCount
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.SuperaLimiteLettereConsecutive: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero per i tasti che non devono partire al semplice distacco del dito. Spazio, elimina e
    ''' invio cambiano il testo in modo vistoso, quindi si attivano solo con il doppio tocco.
    ''' </summary>
    Private Function RichiedeDoppioTocco(tasto As InfoTasto) As Boolean
        Try
            If tasto Is Nothing Then Return False
            If tasto.ESuggerimento Then Return False
            Select Case tasto.Testo
                Case " ", "⌫", "⏎"
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RichiedeDoppioTocco: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub AttivaTasto(tasto As InfoTasto)
        Try
            If tasto Is Nothing Then Return

            Try
                If _suoni IsNot Nothing Then _suoni.TastoPremuto()
            Catch exSuono As Exception
                log.ScriviLog($"WGTastieraTouch.AttivaTasto (suono): {exSuono.Message}")
            End Try

            If tasto.ESuggerimento Then
                RaiseEvent SuggerimentoScelto(tasto.Testo, _parolaParzialeCorrente.Length)
                AzzeraSerieLettere()
                AzzeraParolaEco()
                AnnunciaParolaCompletata(tasto.Testo)
                _tastiSuggerimenti.Clear()
                _parolaParzialeCorrente = ""
                _tastoEsploratoCorrente = Nothing
                _tastoCandidato = Nothing
                _tastoSottoDito = Nothing
                AnnullaCandidato()
                CalcolaAree()
                Me.Invalidate()
                Return
            End If

            If tasto.Testo = "Dett." Then
                AvviaDettatura()
            ElseIf tasto.Testo = "⌫" Then
                RaiseEvent EliminaRichiesto()
                AzzeraSerieLettere()
                RimuoviUltimoCarattereEco()
                AggiornaSuggerimentiInterni()
            ElseIf tasto.Testo = "⏎" Then
                RaiseEvent InvioRichiesto()
                AzzeraSerieLettere()
                AzzeraParolaEco()
                AggiornaSuggerimentiInterni()
            ElseIf tasto.Testo = "⇧" Then
                _maiuscoloAttivo = Not _maiuscoloAttivo
                If _sintesiVocale IsNot Nothing Then
                    _sintesiVocale.LeggiTesto(If(_maiuscoloAttivo, "maiuscolo attivo", "maiuscolo disattivato"))
                End If
                RicostruisciLayout()
            ElseIf tasto.Testo = "123" OrElse tasto.Testo = "ABC" Then
                If _modalitaTasti = ModalitaTasti.Lettere Then
                    _modalitaTasti = ModalitaTasti.SimboliPaginaA
                Else
                    _modalitaTasti = ModalitaTasti.Lettere
                End If
                RicostruisciLayout()
            Else
                If SuperaLimiteLettereConsecutive(tasto.Testo) Then
                    log.ScriviLog($"WGTastieraTouch.AttivaTasto: terza lettera uguale di seguito ({tasto.Testo}) non inserita")
                    _tastoEsploratoCorrente = Nothing
                    _tastoCandidato = Nothing
                    _tastoSottoDito = Nothing
                    AnnullaCandidato()
                    Me.Invalidate()
                    Return
                End If
                RaiseEvent CarattereInserito(tasto.Testo)
                AnnunciaInserimento(tasto.Testo)
            End If

            _tastoEsploratoCorrente = Nothing
            _tastoCandidato = Nothing
            _tastoSottoDito = Nothing
            AnnullaCandidato()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AttivaTasto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub CambiaPagina(direzione As Integer)
        Try
            Select Case _modalitaTasti
                Case ModalitaTasti.Lettere
                    If direzione < 0 Then _modalitaTasti = ModalitaTasti.SimboliPaginaB
                Case ModalitaTasti.SimboliPaginaA
                    _modalitaTasti = If(direzione > 0, ModalitaTasti.SimboliPaginaB, ModalitaTasti.Lettere)
                Case ModalitaTasti.SimboliPaginaB
                    _modalitaTasti = If(direzione > 0, ModalitaTasti.Lettere, ModalitaTasti.SimboliPaginaA)
            End Select
            RicostruisciLayout()
            Me.Invalidate()
            If _sintesiVocale IsNot Nothing Then
                Dim nomePagina As String = If(_modalitaTasti = ModalitaTasti.Lettere, "lettere", If(_modalitaTasti = ModalitaTasti.SimboliPaginaA, "simboli pagina uno", "simboli pagina due"))
                _sintesiVocale.LeggiTesto(nomePagina)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.CambiaPagina: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraMenuOpzioni()
        Try
            Dim menuOpzioniTastiera As New WGMenu()
            menuOpzioniTastiera.aggiungi("Dettatura", AddressOf AvviaDettatura)
            menuOpzioniTastiera.aggiungi("Digitazione standard", AddressOf ImpostaDoppioTocco)
            menuOpzioniTastiera.aggiungi("Digitazione a tocco", AddressOf ImpostaTocco)
            menuOpzioniTastiera.avvia("Opzioni tastiera", True)
            RipristinaDopoInterruzione()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.MostraMenuOpzioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub RipristinaDopoInterruzione()
        Try
            If Not _attiva Then Return
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Me.Visible = True
            Me.Enabled = True
            ApplicaAltezzaVoluta()
            Me.BringToFront()
            RicostruisciLayout()
            CalcolaAree()
            Me.Invalidate()

            Try
                If Me.IsHandleCreated AndAlso _touk IsNot Nothing Then _touk.RiassegnaControllo(Me.Handle)
            Catch exTouk As Exception
                log.ScriviLog($"WGTastieraTouch.RipristinaDopoInterruzione (RiassegnaControllo): {exTouk.Message}")
            End Try
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.RipristinaDopoInterruzione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ImpostaTocco()
        Try
            _modalitaDigitazioneCorrente = ModalitaDigitazione.Tocco
            SalvaModalitaDigitazione(ModalitaDigitazione.Tocco)
            If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto("Digitazione a tocco")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ImpostaTocco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ImpostaDoppioTocco()
        Try
            _modalitaDigitazioneCorrente = ModalitaDigitazione.DoppioTocco
            SalvaModalitaDigitazione(ModalitaDigitazione.DoppioTocco)
            If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto("Digitazione standard")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ImpostaDoppioTocco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ImpostaScorrimento()
        Try
            _modalitaDigitazioneCorrente = ModalitaDigitazione.Scorrimento
            If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto("Digitazione a scorrimento")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.ImpostaScorrimento: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private _tastoDettatura As WGTasto

    Private Sub MostraTastoDettatura()
        Try
            If _tastoDettatura Is Nothing Then
                _tastoDettatura = New WGTasto()
                AddHandler _tastoDettatura.Interrotto, AddressOf OnTastoDettaturaInterrotto
            End If
            _tastoDettatura.Mostra("Sto ascoltando. Parla ora.", WGTasto.OrigineTasto.TastieraTouch)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.MostraTastoDettatura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub NascondiTastoDettatura()
        Try
            If _tastoDettatura IsNot Nothing Then _tastoDettatura.Chiudi()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.NascondiTastoDettatura: {ex.Message}")
        End Try
    End Sub

    Private Sub OnTastoDettaturaInterrotto(sender As Object, e As EventArgs)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New EventHandler(AddressOf OnTastoDettaturaInterrotto), sender, e)
                Return
            End If
            NascondiTastoDettatura()
            FermaDettatura()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnTastoDettaturaInterrotto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaDettatura()
        Try
            If _dettatura Is Nothing Then
                _dettatura = New WGDettatura()
                AddHandler _dettatura.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletata
                AddHandler _dettatura.TrascrizioneFallita, AddressOf OnTrascrizioneFallita
            End If

            If Not _dettatura.ModelloDisponibile Then
                If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto("Chiave API di Gemini non configurata")
                RaiseEvent ErroreDettatura("Chiave API di Gemini non configurata")
                Return
            End If

            _inDettatura = True
            MostraTastoDettatura()
            _dettatura.IniziaRegistrazione()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AvviaDettatura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            _inDettatura = False
        End Try
    End Sub

    Private Sub FermaDettatura()
        Try
            _inDettatura = False
            NascondiTastoDettatura()
            If _dettatura IsNot Nothing Then
                _dettatura.FermaRegistrazione()
            End If
            If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto("Elaborazione in corso")
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.FermaDettatura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnTrascrizioneCompletata(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneCompletata), testo)
                Return
            End If
            NascondiTastoDettatura()
            RaiseEvent TestoDettatoInserito(testo)
            AzzeraSerieLettere()
            AzzeraParolaEco()
            AggiornaSuggerimentiInterni()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnTrascrizioneCompletata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnTrascrizioneFallita(messaggioErrore As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTrascrizioneFallita), messaggioErrore)
                Return
            End If
            If _sintesiVocale IsNot Nothing Then _sintesiVocale.LeggiTesto(messaggioErrore)
            RaiseEvent ErroreDettatura(messaggioErrore)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.OnTrascrizioneFallita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Avvia e mantiene la pressione prolungata quando i tocchi arrivano da WGTouk. Il percorso
    ''' WinForms non riceve MouseDown in quel caso, quindi il timer non partiva mai e il menu
    ''' delle lettere accentate non compariva.
    ''' </summary>
    Private Sub AggiornaPressioneLungaDaTouk(tasto As InfoTasto, punto As Point)
        Try
            If _touk IsNot Nothing AndAlso _touk.InEsplorazione Then
                _esplorazioneInCorso = True
                AnnullaPressioneLunga()
                Return
            End If
            If _esplorazioneInCorso Then
                AnnullaPressioneLunga()
                Return
            End If
            If tasto Is Nothing Then
                AnnullaPressioneLunga()
                Return
            End If

            If Not ReferenceEquals(tasto, _tastoPressioneLungaCorrente) Then
                AnnullaPressioneLunga()
                Return
            End If

            Dim deltaX As Single = punto.X - _puntoPressioneX
            Dim deltaY As Single = punto.Y - _puntoPressioneY
            If Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) > SogliaMovimentoHoldAllineataTouk() Then
                _esplorazioneInCorso = True
                AnnullaPressioneLunga()
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AggiornaPressioneLungaDaTouk: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AnnullaPressioneLunga()
        Try
            _tastoPressioneLungaCorrente = Nothing
            _timerPressioneLunga.Stop()
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.AnnullaPressioneLunga: {ex.Message}")
        End Try
    End Sub

    Private Sub _timerPressioneLunga_Tick(sender As Object, e As EventArgs) Handles _timerPressioneLunga.Tick
        Try
            _timerPressioneLunga.Stop()
            If _esplorazioneInCorso Then Return
            If _touk IsNot Nothing AndAlso _touk.InEsplorazione Then Return
            If _tastoPressioneLungaCorrente Is Nothing Then Return
            Dim tastoDaAccentare As InfoTasto = _tastoPressioneLungaCorrente
            _tastoPressioneLungaCorrente = Nothing
            _pressioneLungaScattata = True
            _oraPressioneLungaScattata = Environment.TickCount
            MostraMenuAccenti(tastoDaAccentare)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._timerPressioneLunga_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_PressioneProlungata(xSchermo As Integer, ySchermo As Integer) Handles _touk.PressioneProlungata
        Try
            If Not _attiva OrElse Not Me.Visible Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If _esplorazioneInCorso Then Return
            If _touk IsNot Nothing AndAlso _touk.InEsplorazione Then Return

            Dim puntoLocale As Point = Me.PointToClient(New Point(xSchermo, ySchermo))
            If puntoLocale.X < 0 OrElse puntoLocale.Y < 0 Then Return
            If puntoLocale.X >= Me.Width OrElse puntoLocale.Y >= Me.Height Then Return

            Dim tasto As InfoTasto = TrovaTastoInPunto(puntoLocale.X, puntoLocale.Y)
            If tasto Is Nothing Then tasto = _tastoEsploratoCorrente
            If tasto Is Nothing Then Return
            If tasto.EComando OrElse tasto.ESuggerimento Then Return
            If Not HaVariantiAccentate(tasto.Testo) Then Return

            AnnullaPressioneLunga()
            _pressioneLungaScattata = True
            _oraPressioneLungaScattata = Environment.TickCount
            MostraMenuAccenti(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_PressioneProlungata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function HaVariantiAccentate(testo As String) As Boolean
        Try
            If String.IsNullOrEmpty(testo) Then Return False
            Select Case testo.ToUpperInvariant()
                Case "A", "E", "I", "O", "U", "C", "N"
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.HaVariantiAccentate: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Sub MostraMenuAccenti(tasto As InfoTasto)
        Try
            If tasto Is Nothing Then Return
            _menuAccentiAperto = True
            Dim menuAccenti As New WGMenu()
            Select Case tasto.Testo.ToUpperInvariant()
                Case "A"
                    menuAccenti.aggiungi("à a grave", AddressOf InserisciAGrave)
                    menuAccenti.aggiungi("á a acuto", AddressOf InserisciAAcuto)
                    menuAccenti.aggiungi("â a circonflesso", AddressOf InserisciACirconflesso)
                    menuAccenti.aggiungi("ä a dieresi", AddressOf InserisciADieresi)
                Case "E"
                    menuAccenti.aggiungi("è e grave", AddressOf InserisciEGrave)
                    menuAccenti.aggiungi("é e acuto", AddressOf InserisciEAcuto)
                    menuAccenti.aggiungi("ê e circonflesso", AddressOf InserisciECirconflesso)
                    menuAccenti.aggiungi("ë e dieresi", AddressOf InserisciEDieresi)
                Case "I"
                    menuAccenti.aggiungi("ì i grave", AddressOf InserisciIGrave)
                    menuAccenti.aggiungi("í i acuto", AddressOf InserisciIAcuto)
                    menuAccenti.aggiungi("î i circonflesso", AddressOf InserisciICirconflesso)
                    menuAccenti.aggiungi("ï i dieresi", AddressOf InserisciIDieresi)
                Case "O"
                    menuAccenti.aggiungi("ò o grave", AddressOf InserisciOGrave)
                    menuAccenti.aggiungi("ó o acuto", AddressOf InserisciOAcuto)
                    menuAccenti.aggiungi("ô o circonflesso", AddressOf InserisciOCirconflesso)
                    menuAccenti.aggiungi("ö o dieresi", AddressOf InserisciODieresi)
                Case "U"
                    menuAccenti.aggiungi("ù u grave", AddressOf InserisciUGrave)
                    menuAccenti.aggiungi("ú u acuto", AddressOf InserisciUAcuto)
                    menuAccenti.aggiungi("û u circonflesso", AddressOf InserisciUCirconflesso)
                    menuAccenti.aggiungi("ü u dieresi", AddressOf InserisciUDieresi)
                Case "C"
                    menuAccenti.aggiungi("ç c cediglia", AddressOf InserisciCCediglia)
                Case "N"
                    menuAccenti.aggiungi("ñ n tilde", AddressOf InserisciNTilde)
                Case Else
                    Return
            End Select
            menuAccenti.avvia("Lettere accentate", True)
            _menuAccentiAperto = False
        Catch ex As Exception
            _menuAccentiAperto = False
            log.ScriviLog($"WGTastieraTouch.MostraMenuAccenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciCarattereAccentato(carattere As String)
        Try
            AzzeraSerieLettere()
            RaiseEvent CarattereInserito(carattere)
            AnnunciaInserimento(carattere)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciCarattereAccentato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciAGrave()
        Try
            InserisciCarattereAccentato("à")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciAGrave: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciAAcuto()
        Try
            InserisciCarattereAccentato("á")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciAAcuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciACirconflesso()
        Try
            InserisciCarattereAccentato("â")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciACirconflesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciADieresi()
        Try
            InserisciCarattereAccentato("ä")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciADieresi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciEGrave()
        Try
            InserisciCarattereAccentato("è")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciEGrave: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciEAcuto()
        Try
            InserisciCarattereAccentato("é")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciEAcuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciECirconflesso()
        Try
            InserisciCarattereAccentato("ê")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciECirconflesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciEDieresi()
        Try
            InserisciCarattereAccentato("ë")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciEDieresi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciIGrave()
        Try
            InserisciCarattereAccentato("ì")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciIGrave: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciIAcuto()
        Try
            InserisciCarattereAccentato("í")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciIAcuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciICirconflesso()
        Try
            InserisciCarattereAccentato("î")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciICirconflesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciIDieresi()
        Try
            InserisciCarattereAccentato("ï")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciIDieresi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciOGrave()
        Try
            InserisciCarattereAccentato("ò")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciOGrave: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciOAcuto()
        Try
            InserisciCarattereAccentato("ó")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciOAcuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciOCirconflesso()
        Try
            InserisciCarattereAccentato("ô")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciOCirconflesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciODieresi()
        Try
            InserisciCarattereAccentato("ö")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciODieresi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciUGrave()
        Try
            InserisciCarattereAccentato("ù")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciUGrave: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciUAcuto()
        Try
            InserisciCarattereAccentato("ú")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciUAcuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciUCirconflesso()
        Try
            InserisciCarattereAccentato("û")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciUCirconflesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciUDieresi()
        Try
            InserisciCarattereAccentato("ü")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciUDieresi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciCCediglia()
        Try
            InserisciCarattereAccentato("ç")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciCCediglia: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub InserisciNTilde()
        Try
            InserisciCarattereAccentato("ñ")
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.InserisciNTilde: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_FlickAvanti() Handles _touk.FlickAvanti
        Try
            If Not _attiva OrElse Not Me.Visible Then Return
            If _inDettatura Then Return
            Try
                If _suoni IsNot Nothing Then _suoni.FlickAvanti()
            Catch exSuono As Exception
                log.ScriviLog($"WGTastieraTouch._touk_FlickAvanti (suono): {exSuono.Message}")
            End Try
            SpostaSelezioneDiUnTasto(1)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_FlickAvanti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_FlickIndietro() Handles _touk.FlickIndietro
        Try
            If Not _attiva OrElse Not Me.Visible Then Return
            If _inDettatura Then Return
            Try
                If _suoni IsNot Nothing Then _suoni.FlickIndietro()
            Catch exSuono As Exception
                log.ScriviLog($"WGTastieraTouch._touk_FlickIndietro (suono): {exSuono.Message}")
            End Try
            SpostaSelezioneDiUnTasto(-1)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_FlickIndietro: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Sposta la selezione al tasto successivo o precedente, scorrendo la tastiera come un unico
    ''' elenco: a fine riga si passa alla riga seguente. Il cambio di pagina fra lettere e simboli
    ''' resta affidato al tasto 123/ABC della riga inferiore.
    ''' </summary>
    Private Sub SpostaSelezioneDiUnTasto(direzione As Integer)
        Try
            Dim elencoTasti As New List(Of InfoTasto)
            For Each riga As List(Of InfoTasto) In _righeTasti
                For Each tasto As InfoTasto In riga
                    elencoTasti.Add(tasto)
                Next
            Next
            If elencoTasti.Count = 0 Then Return

            Dim indiceCorrente As Integer = -1
            If _tastoEsploratoCorrente IsNot Nothing Then
                indiceCorrente = elencoTasti.IndexOf(_tastoEsploratoCorrente)
            End If

            Dim nuovoIndice As Integer
            If indiceCorrente < 0 Then
                nuovoIndice = If(direzione > 0, 0, elencoTasti.Count - 1)
            Else
                nuovoIndice = indiceCorrente + direzione
                If nuovoIndice < 0 Then nuovoIndice = elencoTasti.Count - 1
                If nuovoIndice >= elencoTasti.Count Then nuovoIndice = 0
            End If

            _tastoPrimaDellUltimoTocco = Nothing
            EsploraTasto(elencoTasti(nuovoIndice))
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.SpostaSelezioneDiUnTasto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_EsplorazioneTocco(xSchermo As Integer, ySchermo As Integer) Handles _touk.EsplorazioneTocco
        Try
            If Not _attiva OrElse Not Me.Visible Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If _inDettatura Then
                FermaDettatura()
                Return
            End If

            _oraUltimoEventoTouk = Environment.TickCount
            _tastieraAttivaSuSchermo = Me
            If Not _ditoGiu Then AvviaNuovoContatto()
            _ditoGiu = True

            Dim puntoLocale As Point = Me.PointToClient(New Point(xSchermo, ySchermo))
            Const margine As Integer = 24
            If puntoLocale.X < -margine OrElse puntoLocale.Y < -margine Then Return
            If puntoLocale.X >= Me.Width + margine OrElse puntoLocale.Y >= Me.Height + margine Then Return
            If puntoLocale.X < 0 Then puntoLocale.X = 0
            If puntoLocale.Y < 0 Then puntoLocale.Y = 0
            If puntoLocale.X >= Me.Width Then puntoLocale.X = Me.Width - 1
            If puntoLocale.Y >= Me.Height Then puntoLocale.Y = Me.Height - 1

            Dim tasto As InfoTasto = TrovaTastoConIsteresi(puntoLocale.X, puntoLocale.Y)
            If tasto Is Nothing Then Return
            _tastoSottoDito = tasto

            _tastoPrimaDellUltimoTocco = _tastoEsploratoCorrente
            _oraUltimaEsplorazione = Environment.TickCount

            If _touk IsNot Nothing AndAlso _touk.InEsplorazione Then
                _esplorazioneInCorso = True
                AnnullaPressioneLunga()
            Else
                AggiornaPressioneLungaDaTouk(tasto, puntoLocale)
            End If

            If Not ReferenceEquals(tasto, _tastoEsploratoCorrente) AndAlso _tastoEsploratoCorrente IsNot Nothing Then
                Me.Invalidate()
            End If

            If ReferenceEquals(tasto, _tastoEsploratoCorrente) Then
                AnnullaCandidato()
                Return
            End If

            RichiediEsplorazione(tasto)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_EsplorazioneTocco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_EsplorazioneTerminata(xSchermo As Integer, ySchermo As Integer) Handles _touk.EsplorazioneTerminata
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            _oraUltimoEventoTouk = Environment.TickCount
            _esplorazioneInCorso = False
            _ditoGiu = False
            _oraUltimaLettura = 0

            If Not _attiva OrElse Not Me.Visible Then
                log.ScriviLog("WGTastieraTouch._touk_EsplorazioneTerminata: tastiera non attiva o non visibile, lettera non inserita")
                Return
            End If
            If _inDettatura Then Return
            _tastieraAttivaSuSchermo = Me

            ' Commit al pointer-up, in ogni modalita' di digitazione: alzare il dito scrive la
            ' lettera. La scelta del tasto sta in TastoDaConfermareAlRilascio.
            ConfermaLetteraAlRilascio(xSchermo, ySchermo)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_EsplorazioneTerminata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        Try
            If Not _attiva OrElse Not Me.Visible Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If _inDettatura Then Return

            _oraUltimoEventoTouk = Environment.TickCount

            ' Le lettere sono gia' state scritte al pointer-up. Il doppio tocco conferma solo
            ' spazio, elimina e invio: non deve reinserire la lettera ne' far avanzare il campo.
            _tastieraAttivaSuSchermo = Me
            Dim tastoDaAttivare As InfoTasto = _tastoEsploratoCorrente
            If tastoDaAttivare Is Nothing Then tastoDaAttivare = _tastoCandidato
            If tastoDaAttivare Is Nothing AndAlso _touk IsNot Nothing Then
                Dim avvio As Point = _touk.PuntoInizioGesto
                tastoDaAttivare = TastoSottoPuntoSchermo(avvio.X, avvio.Y)
            End If

            If tastoDaAttivare Is Nothing Then Return

            If ReferenceEquals(tastoDaAttivare, _tastoBarraChiudi) Then
                RaiseEvent ChiudiRichiesto()
                Return
            End If
            If ReferenceEquals(tastoDaAttivare, _tastoBarraOpzioni) Then
                MostraMenuOpzioni()
                Return
            End If

            If Not RichiedeDoppioTocco(tastoDaAttivare) Then Return

            AttivaTasto(tastoDaAttivare)
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch._touk_TapDoppio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub


    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If ReferenceEquals(_tastieraAttivaSuSchermo, Me) Then _tastieraAttivaSuSchermo = Nothing
                Try
                    RemoveHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, AddressOf GestoreCambioSchermo
                Catch exEventi As Exception
                    log.ScriviLog($"WGTastieraTouch.Dispose (eventi schermo): {exEventi.Message}")
                End Try
                If _timerPressioneLunga IsNot Nothing Then
                    _timerPressioneLunga.Stop()
                    _timerPressioneLunga.Dispose()
                End If
                If _timerAnnuncioEsplorazione IsNot Nothing Then
                    _timerAnnuncioEsplorazione.Stop()
                    _timerAnnuncioEsplorazione.Dispose()
                End If
                If _touk IsNot Nothing Then
                    RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvanti
                    RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietro
                End If
                If _dettatura IsNot Nothing Then
                    RemoveHandler _dettatura.TrascrizioneCompletata, AddressOf OnTrascrizioneCompletata
                    RemoveHandler _dettatura.TrascrizioneFallita, AddressOf OnTrascrizioneFallita
                    _dettatura.Dispose()
                    _dettatura = Nothing
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTastieraTouch.Dispose: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

End Class