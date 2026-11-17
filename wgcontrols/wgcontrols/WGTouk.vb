Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports wgcontrols

Public Class WGTouk
    Implements IMessageFilter

    Private Shared log As New Logger()
    Private Shared _istanza As WGTouk
    Private Shared ReadOnly bloccoIstanza As New Object()
    Private Shared ReadOnly orologio As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()

    Public Enum ModalitaNavigazioneTouch
        Riga
        Paragrafo
        Parola
        Carattere
        Campo
    End Enum

    Public Enum SensibilitaTouch
        Bassa
        Media
        Alta
    End Enum

    Public Enum OrientamentoSchermo
        Verticale
        Orizzontale
    End Enum

    Private Enum DirezioneGesto
        Nessuna
        Destra
        Sinistra
        Su
        Giu
    End Enum

    Private Const WM_POINTERDOWN As Integer = &H246
    Private Const WM_POINTERUP As Integer = &H247
    Private Const WM_POINTERUPDATE As Integer = &H245
    Private Const WM_POINTERCAPTURECHANGED As Integer = &H24C
    Private Const WM_POINTERLEAVE As Integer = &H24A

    Private Const PT_TOUCH As UInteger = 2

    Private Const POINTER_FLAG_INCONTACT As UInteger = &H4UI
    Private Const POINTER_FLAG_PRIMARY As UInteger = &H2000UI
    Private Const POINTER_FLAG_CONFIDENCE As UInteger = &H4000UI
    Private Const POINTER_FLAG_CANCELED As UInteger = &H8000UI
    Private Const POINTER_FLAG_UP As UInteger = &H40000UI

    Private Const TOUCH_MASK_CONTACTAREA As UInteger = &H1UI

    Private Const MDT_EFFECTIVE_DPI As Integer = 0
    Private Const MDT_RAW_DPI As Integer = 2
    Private Const MONITOR_DEFAULTTONEAREST As UInteger = 2

    Private Const SM_DIGITIZER As Integer = 94
    Private Const SM_MAXIMUMTOUCHES As Integer = 95
    Private Const NID_INTEGRATED_TOUCH As Integer = &H1
    Private Const NID_EXTERNAL_TOUCH As Integer = &H2
    Private Const NID_READY As Integer = &H80

    Private Const TABLET_DISABLE_PRESSANDHOLD As UInteger = &H1
    Private Const TABLET_DISABLE_PENTAPFEEDBACK As UInteger = &H8
    Private Const TABLET_DISABLE_PENBARRELFEEDBACK As UInteger = &H10
    Private Const TABLET_DISABLE_TOUCHUIFORCEON As UInteger = &H100
    Private Const TABLET_DISABLE_TOUCHUIFORCEOFF As UInteger = &H200
    Private Const TABLET_DISABLE_TOUCHSWITCH As UInteger = &H8000
    Private Const TABLET_DISABLE_FLICKS As UInteger = &H10000
    Private Const TABLET_DISABLE_SMOOTHSCROLLING As UInteger = &H80000
    Private Const TABLET_DISABLE_FLICKFALLBACKKEYS As UInteger = &H100000

    Private Const SogliaFlickMmPredefinita As Double = 8.0
    Private Const SogliaTapMmPredefinita As Double = 5.0
    Private Const SogliaDoppioTapMmPredefinita As Double = 8.0
    Private Const SogliaEsplorazioneMmPredefinita As Double = 1.5
    Private Const SogliaVariazioneFraDitaMmPredefinita As Double = 6.0
    Private Const LatoMassimoContattoMmPredefinito As Double = 22.0
    Private Const TempoMassimoFlickMsPredefinito As Double = 1500
    Private Const SogliaDoppioTapMsPredefinita As Double = 350
    Private Const SogliaHoldMsPredefinita As Double = 450
    Private Const SogliaHoldMsMinima As Double = 400
    Private Const SogliaHoldMsMassima As Double = 550
    Private Const SogliaMovimentoHoldPixelMinima As Integer = 10
    Private Const SogliaMovimentoHoldPixelMassima As Integer = 16
    Private Const SogliaTapDueDitaMsPredefinita As Double = 700
    Private Const SogliaDoppioTapDueDitaMsPredefinita As Double = 700
    Private Const SogliaClassificazioneEsplorazioneMsPredefinita As Double = 400
    Private Const SogliaAnnuncioEsplorazioneMsPredefinita As Double = 150
    Private Const PermanenzaEsplorazioneMsPredefinita As Double = 50
    Private Const FinestraSimultaneitaMsPredefinita As Double = 200
    Private Const FinestraVelocitaMsPredefinita As Double = 90
    Private Const VelocitaMinimaFlickMmMsPredefinita As Double = 0.25
    Private Const RapportoVelocitaFinaleFlickLento As Double = 0.4
    Private Const CampioniContattoPerCalibrazione As Integer = 24
    Private Const MassimoCampioniContatto As Integer = 64
    Private Const FattoreScartoContatto As Double = 2.2
    Private Const PermanenzaMinimaMs As Double = 30
    Private Const PermanenzaMassimaMs As Double = 80
    Private Const ConfermeElementoRichieste As Integer = 2
    Private Const RettilineitaMinimaPredefinita As Double = 0.75
    Private Const RettilineitaMinimaFlickLentoPredefinita As Double = 0.9
    Private Const DominanzaAssePredefinita As Double = 1.5
    Private Const RapportoPercorsoTapPredefinito As Double = 2.0

    Private Const IntervalloSilenzioPuntatoreMs As Double = 1500
    Private Const IntervalloTimeoutPuntatoreMs As Double = 8000
    Private Const IntervalloGuardiaFuocoMs As Integer = 120
    Private Const IntervalloSorveglianzaDispositivoMs As Integer = 3000
    Private Const IntervalloVerificaEsplorazioneMs As Integer = 40
    Private Const MassimoDitaGesto As Integer = 3
    Private Const MassimoCampioniStorico As Integer = 512

    Public Property SogliaFlickMm As Double = SogliaFlickMmPredefinita
    Public Property SogliaTapMm As Double = SogliaTapMmPredefinita
    Public Property SogliaDoppioTapMm As Double = SogliaDoppioTapMmPredefinita
    Public Property SogliaEsplorazioneMm As Double = SogliaEsplorazioneMmPredefinita
    Public Property SogliaVariazioneFraDitaMm As Double = SogliaVariazioneFraDitaMmPredefinita
    Public Property LatoMassimoContattoMm As Double = LatoMassimoContattoMmPredefinito
    Public Property TempoMassimoFlickMs As Double = TempoMassimoFlickMsPredefinito
    Public Property SogliaDoppioTapMs As Double = SogliaDoppioTapMsPredefinita
    Public Property SogliaTapDueDitaMs As Double = SogliaTapDueDitaMsPredefinita
    Public Property SogliaDoppioTapDueDitaMs As Double = SogliaDoppioTapDueDitaMsPredefinita
    Public Property SogliaClassificazioneEsplorazioneMs As Double = SogliaClassificazioneEsplorazioneMsPredefinita
    Public Property SogliaAnnuncioEsplorazioneMs As Double = SogliaAnnuncioEsplorazioneMsPredefinita
    Public Property PermanenzaEsplorazioneMs As Double = PermanenzaEsplorazioneMsPredefinita
    Public Property FinestraSimultaneitaMs As Double = FinestraSimultaneitaMsPredefinita
    Public Property FinestraVelocitaMs As Double = FinestraVelocitaMsPredefinita
    Public Property VelocitaMinimaFlickMmMs As Double = VelocitaMinimaFlickMmMsPredefinita
    Public Property RettilineitaMinima As Double = RettilineitaMinimaPredefinita
    Public Property RettilineitaMinimaFlickLento As Double = RettilineitaMinimaFlickLentoPredefinita
    Public Property DominanzaAsse As Double = DominanzaAssePredefinita
    Public Property RapportoPercorsoTap As Double = RapportoPercorsoTapPredefinito

    ''' <summary>
    ''' Con questa proprieta' attiva le grandezze che dipendono dal digitalizzatore non vengono
    ''' lette da una costante ma ricavate dall'uso: la dimensione tipica di un dito su questo
    ''' schermo, la cadenza con cui il dispositivo consegna i campioni e la velocita' raggiunta
    ''' dentro il gesto stesso. Un numero fisso andava bene solo sul dispositivo su cui era stato
    ''' misurato, e su un altro digitalizzatore rifiutava tocchi validi o lasciava passare il
    ''' palmo. Va disattivata soltanto per imporre a mano i valori delle singole proprieta'.
    ''' </summary>
    Public Property AdattaAlDispositivo As Boolean = True

    Private sogliaHoldMsInterna As Double = SogliaHoldMsPredefinita

    Public Property SogliaHoldMs As Double
        Get
            Return sogliaHoldMsInterna
        End Get
        Set(value As Double)
            Try
                If value <= 0 Then Return
                Dim valoreLimitato As Double = value
                If valoreLimitato < SogliaHoldMsMinima Then valoreLimitato = SogliaHoldMsMinima
                If valoreLimitato > SogliaHoldMsMassima Then valoreLimitato = SogliaHoldMsMassima
                sogliaHoldMsInterna = valoreLimitato
                Dim intervalloHold As Integer = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
                timerAttesaHold.Interval = intervalloHold
                timerHoldPrimario.Interval = intervalloHold
            Catch ex As Exception
                log.ScriviLog($"SogliaHoldMs Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Private sensibilitaInterna As SensibilitaTouch = SensibilitaTouch.Media

    ''' <summary>
    ''' Preset che riscrive in blocco le soglie partendo dai valori predefiniti. Le singole
    ''' proprieta' restano scrivibili dopo l'assegnazione del preset, quindi una configurazione
    ''' salvata puo' contenere sia il preset che le eventuali correzioni fatte a mano.
    ''' </summary>
    Public Property Sensibilita As SensibilitaTouch
        Get
            Return sensibilitaInterna
        End Get
        Set(value As SensibilitaTouch)
            Try
                sensibilitaInterna = value
                ApplicaSensibilita()
            Catch ex As Exception
                log.ScriviLog($"Sensibilita Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Private Sub ApplicaSensibilita()
        Try
            Dim fattoreDistanza As Double = 1.0
            Dim fattoreTempo As Double = 1.0
            Dim fattoreVelocita As Double = 1.0

            Select Case sensibilitaInterna
                Case SensibilitaTouch.Bassa
                    fattoreDistanza = 1.4
                    fattoreTempo = 1.25
                    fattoreVelocita = 0.8
                Case SensibilitaTouch.Alta
                    fattoreDistanza = 0.7
                    fattoreTempo = 0.85
                    fattoreVelocita = 1.2
            End Select

            SogliaFlickMm = SogliaFlickMmPredefinita * fattoreDistanza
            SogliaTapMm = SogliaTapMmPredefinita * fattoreDistanza
            SogliaDoppioTapMm = SogliaDoppioTapMmPredefinita * fattoreDistanza
            SogliaVariazioneFraDitaMm = SogliaVariazioneFraDitaMmPredefinita * fattoreDistanza
            TempoMassimoFlickMs = TempoMassimoFlickMsPredefinito * fattoreTempo
            SogliaDoppioTapMs = SogliaDoppioTapMsPredefinita * fattoreTempo
            SogliaTapDueDitaMs = SogliaTapDueDitaMsPredefinita * fattoreTempo
            SogliaDoppioTapDueDitaMs = SogliaDoppioTapDueDitaMsPredefinita * fattoreTempo
            SogliaClassificazioneEsplorazioneMs = SogliaClassificazioneEsplorazioneMsPredefinita * fattoreTempo
            Dim holdCalcolata As Double = SogliaHoldMsPredefinita * fattoreTempo
            If holdCalcolata < SogliaHoldMsMinima Then holdCalcolata = SogliaHoldMsMinima
            If holdCalcolata > SogliaHoldMsMassima Then holdCalcolata = SogliaHoldMsMassima
            SogliaHoldMs = holdCalcolata
            VelocitaMinimaFlickMmMs = VelocitaMinimaFlickMmMsPredefinita * fattoreVelocita
        Catch ex As Exception
            log.ScriviLog($"ApplicaSensibilita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Funzione con cui l'applicazione dichiara che cosa si trova sotto un punto dello schermo.
    ''' Quando e' assegnata, l'esplorazione tattile annuncia soltanto al cambio di elemento e dopo
    ''' una breve permanenza, quindi un tasto sfiorato di striscio non viene letto; se resta a
    ''' Nothing vale la sola regola della distanza minima percorsa. Viene richiamata dal filtro
    ''' dei messaggi, percio' deve limitarsi a una prova di appartenenza e tornare subito.
    ''' </summary>
    Public Property IdentificaElemento As Func(Of Point, String)

    Private _puntiPerMillimetro As Double = 96 / 25.4
    Private puntiPerMillimetroGesto As Double = 96 / 25.4
    Private ReadOnly puntiPerMillimetroPerMonitor As New Dictionary(Of IntPtr, Double)

    Private ReadOnly Property SogliaFlickPixel As Double
        Get
            Try
                Return Math.Max(12, SogliaFlickMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"SogliaFlickPixel: {ex.Message}")
                Return 12
            End Try
        End Get
    End Property

    Private ReadOnly Property SogliaTapPixel As Double
        Get
            Try
                Return Math.Max(5, SogliaTapMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"SogliaTapPixel: {ex.Message}")
                Return 5
            End Try
        End Get
    End Property

    Private ReadOnly Property SogliaDoppioTapPixel As Double
        Get
            Try
                Return Math.Max(12, SogliaDoppioTapMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"SogliaDoppioTapPixel: {ex.Message}")
                Return 12
            End Try
        End Get
    End Property

    Private ReadOnly Property SogliaEsplorazionePixel As Double
        Get
            Try
                Return Math.Max(4, SogliaEsplorazioneMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"SogliaEsplorazionePixel: {ex.Message}")
                Return 4
            End Try
        End Get
    End Property

    Public ReadOnly Property SogliaMovimentoHoldPixel As Integer
        Get
            Try
                Dim slop As Integer = Math.Max(SystemInformation.DragSize.Width, SystemInformation.DragSize.Height)
                If slop < SogliaMovimentoHoldPixelMinima Then slop = SogliaMovimentoHoldPixelMinima
                If slop > SogliaMovimentoHoldPixelMassima Then slop = SogliaMovimentoHoldPixelMassima
                Return slop
            Catch ex As Exception
                log.ScriviLog($"SogliaMovimentoHoldPixel: {ex.Message}")
                Return 12
            End Try
        End Get
    End Property

    Public ReadOnly Property InEsplorazione As Boolean
        Get
            Try
                Return inEsplorazioneInterno
            Catch ex As Exception
                log.ScriviLog($"InEsplorazione Get: {ex.Message}")
                Return False
            End Try
        End Get
    End Property

    Private ReadOnly Property SogliaVariazioneFraDitaPixel As Double
        Get
            Try
                Return Math.Max(8, SogliaVariazioneFraDitaMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"SogliaVariazioneFraDitaPixel: {ex.Message}")
                Return 8
            End Try
        End Get
    End Property

    Private ReadOnly Property LatoMassimoContattoPixel As Double
        Get
            Try
                Return Math.Max(20, LatoMassimoContattoMm * puntiPerMillimetroGesto)
            Catch ex As Exception
                log.ScriviLog($"LatoMassimoContattoPixel: {ex.Message}")
                Return 20
            End Try
        End Get
    End Property

    Public Event FlickAvanti()
    Public Event FlickIndietro()
    Public Event FlickSu()
    Public Event FlickGiu()
    Public Event RichiestaLetturaCompleta()
    ''' <summary>
    ''' Versioni dedicate dei gesti che riguardano il documento. Vengono sollevate al posto di
    ''' FlickAvanti, FlickIndietro e RichiestaLetturaCompleta quando il gesto parte dentro l'area
    ''' indicata da ZonaDocumento: i gesti classici in quel caso NON vengono sollevati, cosi' la
    ''' tastiera touch e gli altri ascoltatori globali non reagiscono a un gesto fatto sul testo.
    ''' </summary>
    Public Event FlickAvantiDocumento()
    Public Event FlickIndietroDocumento()
    Public Event RichiestaLetturaCompletaDocumento()
    ''' <summary>
    ''' Sollevato appena un dito tocca lo schermo, con le coordinate SULLO SCHERMO del punto
    ''' toccato. Serve all'esplorazione tattile: ogni controllo verifica se il punto cade dentro
    ''' di se' e annuncia cosa c'e' sotto il dito. Passa dalle coordinate schermo perche' il
    ''' rilevamento e' ora su tutta l'applicazione e non su una singola finestra.
    ''' </summary>
    Public Event EsplorazioneTocco(xSchermo As Integer, ySchermo As Integer)
    ''' <summary>
    ''' Sollevato quando l'ultimo dito lascia lo schermo dopo un'esplorazione a un dito, con le
    ''' coordinate del punto in cui e' stato staccato. Serve a chi vuole confermare al rilascio
    ''' quello che era stato esplorato (la tastiera in modalita' "Tocco" scrive qui la lettera).
    ''' </summary>
    Public Event EsplorazioneTerminata(xSchermo As Integer, ySchermo As Integer)
    Public Event TapSingolo()
    Public Event TapDoppio()
    Public Event TapDoppioProlungato()
    ''' <summary>
    ''' Hold sul contatto primario: dito fermo oltre SogliaHoldMs e sotto la soglia di movimento.
    ''' Non viene sollevato al solo Down, ne' se il dito ha gia' superato lo slop (esplorazione).
    ''' </summary>
    Public Event PressioneProlungata(xSchermo As Integer, ySchermo As Integer)
    Public Event EscapeGesto()
    ' Tre tocchi con due dita: elenco delle applicazioni. Dopo il secondo tocco l'Esc aspetta un attimo il terzo.
    Public Event TriploTapDueDita()
    ' Flick con due dita verso l'alto: la "casa" (menu applicazioni), come la barra in fondo dell'iPhone.
    Public Event FlickDueDitaSu()
    ' Flick con due dita a destra (avanti = vero) o a sinistra: applicazione successiva o precedente.
    Public Event FlickDueDitaLaterale(avanti As Boolean)
    ' Flick con tre dita a sinistra (avanti = vero, la pagina dopo, come sfogliare) o a destra: pagine della home.
    Public Event FlickTreDitaLaterale(avanti As Boolean)
    ' Flick verticale con tre dita quando il rotore non serve: in giù (giu = vero) il centro notifiche,
    ' in su il centro di controllo. Il rotore serve solo con davanti un campo di testo (RotoreTestoServe)
    ' o il browser (il suo gestore del rotore); dalla barra in alto dello schermo valgono sempre i centri.
    Public Event FlickTreDitaVerticale(giu As Boolean)

    ''' <summary>Vero se davanti c'è un campo di testo, dove il rotore sceglie riga, parola, carattere (lo imposta WGContenitore).</summary>
    Public Property RotoreTestoServe As Func(Of Boolean)
    Public Event ModalitaNavigazioneCambiata()
    Public Event StatoCambiato()
    Public Event RichiestaSilenzio()
    Public Event OrientamentoCambiato()

    Private Class InfoPuntatore
        Public XIniziale As Double
        Public YIniziale As Double
        Public XCorrente As Double
        Public YCorrente As Double
        Public XPrecedente As Double
        Public YPrecedente As Double
        Public OraInizio As Double
        Public OraUltimaNotizia As Double
        Public TempoUltimoCampione As Double
        Public GestitoComeHold As Boolean
        Public EPrimario As Boolean
        Public ClassificatoComeEsplorazione As Boolean
        Public ContaNelGesto As Boolean
        Public DistanzaMassima As Double
        Public Percorso As Double
        Public LatoContatto As Double
    End Class

    Private Class CampioneVelocita
        Public Tempo As Double
        Public X As Double
        Public Y As Double
    End Class

    Private Class RettangoliDispositivo
        Public AreaDispositivo As RECT
        Public AreaSchermo As RECT
        Public Valida As Boolean
    End Class

    Private ReadOnly puntatoriAttivi As New Dictionary(Of UInteger, InfoPuntatore)
    Private puntatoriDiTocco As New HashSet(Of UInteger)
    Private ReadOnly rettangoliPerDispositivo As New Dictionary(Of IntPtr, RettangoliDispositivo)
    Private ReadOnly campioniVelocita As New List(Of CampioneVelocita)

    ' Numero massimo di dita toccate contemporaneamente durante il gesto in corso: si aggiorna
    ' ogni volta che un nuovo dito tocca lo schermo e si usa per decidere il tipo di gesto SOLO
    ' quando l'ultimo dito si solleva (vedi GestisciPointerUp) — non quando si solleva ciascun
    ' dito preso singolarmente, altrimenti un gesto a due o tre dita rischia di essere scambiato
    ' per un gesto a un dito ogni volta che le dita non si sollevano nello stesso istante esatto,
    ' cosa che in pratica non succede quasi mai.
    Private numeroMassimoDitaGestoCorrente As Integer = 0

    Private gestoGiaClassificatoComeScorrimento As Boolean = False
    Private gestoAnnullato As Boolean = False
    Private dispositivoUsaConfidenza As Boolean = False
    Private massimoDitaSupportate As Integer = MassimoDitaGesto
    Private ReadOnly latiContattoOsservati As New List(Of Double)
    Private latoTipicoContatto As Double = 0
    Private ReadOnly intervalliFraCampioni As New List(Of Double)
    Private intervalloTipicoFraCampioni As Double = 0
    Private velocitaMassimaGesto As Double = 0
    Private confermeElemento As Integer = 0

    Private ReadOnly OrdineModalitaNavigazione As ModalitaNavigazioneTouch() = {
        ModalitaNavigazioneTouch.Riga,
        ModalitaNavigazioneTouch.Paragrafo,
        ModalitaNavigazioneTouch.Parola,
        ModalitaNavigazioneTouch.Carattere,
        ModalitaNavigazioneTouch.Campo
    }

    Private modalitaNavigazioneCorrente As ModalitaNavigazioneTouch = ModalitaNavigazioneTouch.Riga

    Private xInizioGestoCorrente As Double = 0
    Private yInizioGestoCorrente As Double = 0
    Private gestoCorrenteSuTastieraTouch As Boolean = False
    Private ultimoGestoConclusoSuTastieraTouch As Boolean = False

    ''' <summary>
    ''' Punto sullo schermo in cui e' iniziato il gesto attualmente in corso o appena concluso.
    ''' I gesti di flick non portano con se' le coordinate, quindi chi riceve l'evento le legge
    ''' da qui per capire se il gesto e' partito dentro la propria area e reagire solo in quel
    ''' caso: senza questo controllo ogni controllo iscritto reagisce a ogni gesto, ovunque sia
    ''' stato fatto.
    ''' </summary>
    Public ReadOnly Property PuntoInizioGesto As Point
        Get
            Try
                Return New Point(CInt(Math.Round(xInizioGestoCorrente)), CInt(Math.Round(yInizioGestoCorrente)))
            Catch ex As Exception
                log.ScriviLog($"PuntoInizioGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return Point.Empty
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Controllo gestito piu' interno che si trova sotto il punto indicato. Scende nei figli
    ''' anche quando questi non possiedono un handle proprio, cosa che WindowFromPoint da sola
    ''' non fa: un controllo disabilitato o disegnato dal padre restituisce l'handle del padre e
    ''' il gesto risultava di nessuno.
    ''' </summary>
    Private Function ControlloSottoIlPunto(puntoSchermo As Point) As Control
        Try
            Dim finestra As IntPtr = WindowFromPoint(New PUNTOSCHERMO() With {.x = puntoSchermo.X, .y = puntoSchermo.Y})
            If finestra = IntPtr.Zero Then Return Nothing

            Dim trovato As Control = Control.FromChildHandle(finestra)
            If trovato Is Nothing Then trovato = Control.FromHandle(finestra)
            If trovato Is Nothing OrElse trovato.IsDisposed Then Return Nothing

            Dim protezione As Integer = 0
            Do While protezione < 32
                protezione += 1
                If Not trovato.IsHandleCreated Then Exit Do
                Dim figlio As Control = trovato.GetChildAtPoint(trovato.PointToClient(puntoSchermo), GetChildAtPointSkip.Invisible)
                If figlio Is Nothing OrElse figlio.IsDisposed Then Exit Do
                trovato = figlio
            Loop

            Return trovato
        Catch ex As Exception
            log.ScriviLog($"ControlloSottoIlPunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Indica se il gesto in corso e' iniziato dentro l'area occupata dal controllo indicato.
    ''' Restituisce False se il controllo non e' utilizzabile, cosi' chi chiama puo' ignorare
    ''' il gesto senza dover ripetere ogni volta i controlli di validita'.
    ''' </summary>
    Public Function GestoIniziatoDentro(controllo As Control) As Boolean
        Try
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return False
            If Not controllo.IsHandleCreated OrElse Not controllo.Visible Then Return False

            Dim puntoSchermo As New Point(CInt(Math.Round(xInizioGestoCorrente)), CInt(Math.Round(yInizioGestoCorrente)))
            Dim punto As Point = controllo.PointToClient(puntoSchermo)
            If punto.X < 0 OrElse punto.Y < 0 Then Return False
            If punto.X >= controllo.Width OrElse punto.Y >= controllo.Height Then Return False

            ' Stare dentro l'area non basta: se un menu o un altro controllo si e' sovrapposto al
            ' campo, il gesto appartiene a chi sta sopra. Senza questa verifica i flick continuano
            ' a essere consegnati al campo coperto e la lista in primo piano non riceve nulla.
            Dim sottoIlDito As Control = ControlloSottoIlPunto(puntoSchermo)
            If sottoIlDito Is Nothing Then Return True
            If ReferenceEquals(sottoIlDito, controllo) Then Return True
            If controllo.Contains(sottoIlDito) Then Return True
            If sottoIlDito.Contains(controllo) Then Return True

            Return False
        Catch ex As Exception
            log.ScriviLog($"GestoIniziatoDentro: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se il gesto in corso, o quello appena concluso, e' sulla tastiera a schermo.
    ''' Il valore resta valido anche dopo il pointer-up: gli eventi sono consegnati in
    ''' differita, e un nuovo pointer-down (secondo tocco) non deve far trattare il doppio
    ''' tocco su una lettera come Invio. Solo il tasto Invio della tastiera passa al campo
    ''' successivo; la lettera si conferma al pointer-up.
    ''' </summary>
    Public Function GestoIniziatoSuTastieraTouch() As Boolean
        Try
            If gestoCorrenteSuTastieraTouch Then Return True
            If ultimoGestoConclusoSuTastieraTouch Then Return True
            Return PuntoSuTastieraTouch(xInizioGestoCorrente, yInizioGestoCorrente)
        Catch ex As Exception
            log.ScriviLog($"GestoIniziatoSuTastieraTouch: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function PuntoSuTastieraTouch(x As Double, y As Double) As Boolean
        Try
            Dim puntoSchermo As New Point(CInt(Math.Round(x)), CInt(Math.Round(y)))
            If WGTastieraTouch.ContienePuntoSchermo(puntoSchermo.X, puntoSchermo.Y) Then Return True
            Dim sotto As Control = ControlloSottoIlPunto(puntoSchermo)
            While sotto IsNot Nothing
                If TypeOf sotto Is WGTastieraTouch Then Return True
                sotto = sotto.Parent
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog($"PuntoSuTastieraTouch: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private zonaDocumentoCorrente As Control

    ''' <summary>
    ''' Controllo che rappresenta l'area del documento sotto le dita, tipicamente il campo di
    ''' scrittura mentre la tastiera touch e' a schermo. Finche' e' impostato, i gesti che
    ''' partono dentro quest'area vengono consegnati solo agli eventi Documento e non a quelli
    ''' globali. Va azzerato quando l'area non e' piu' valida.
    ''' </summary>
    Public Property ZonaDocumento As Control
        Get
            Return zonaDocumentoCorrente
        End Get
        Set(value As Control)
            Try
                zonaDocumentoCorrente = value
            Catch ex As Exception
                log.ScriviLog($"ZonaDocumento Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Private Function GestoNellaZonaDocumento() As Boolean
        Try
            If zonaDocumentoCorrente Is Nothing Then Return False
            Return GestoIniziatoDentro(zonaDocumentoCorrente)
        Catch ex As Exception
            log.ScriviLog($"GestoNellaZonaDocumento: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Modalita' di navigazione touch corrente, condivisa da tutta l'applicazione: si cambia
    ''' con il gesto a tre dita (vedi GestisciPointerUp) e vale ovunque, campi singoli e
    ''' schermate multicampo compresi, cosi' non serve sceglierla di nuovo passando da un
    ''' contesto all'altro.
    ''' </summary>
    Public Property ModalitaNavigazione As ModalitaNavigazioneTouch
        Get
            Return modalitaNavigazioneCorrente
        End Get
        Set(value As ModalitaNavigazioneTouch)
            Try
                modalitaNavigazioneCorrente = value
            Catch ex As Exception
                log.ScriviLog($"ModalitaNavigazione Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    ' Chi ha un rotore suo (il browser: intestazioni, link, pulsanti...) lo registra qui: il flick con
    ' tre dita va a lui, che restituisce vero se lo ha usato, invece di cambiare la modalità del testo.
    ' Si chiama sul thread dell'interfaccia, dove si concludono i gesti.
    Private ReadOnly gestoriRotore As New List(Of Func(Of Boolean, Boolean))()

    Public Sub AggiungiGestoreRotore(gestore As Func(Of Boolean, Boolean))
        If gestore IsNot Nothing AndAlso Not gestoriRotore.Contains(gestore) Then gestoriRotore.Add(gestore)
    End Sub

    Public Sub RimuoviGestoreRotore(gestore As Func(Of Boolean, Boolean))
        gestoriRotore.Remove(gestore)
    End Sub

    Private Function RotoreGestitoAltrove(avanti As Boolean) As Boolean
        For Each gestore As Func(Of Boolean, Boolean) In gestoriRotore.ToArray()
            Try
                If gestore(avanti) Then Return True
            Catch ex As Exception
                log.ScriviLog($"RotoreGestitoAltrove: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        Next
        Return False
    End Function

    Private Sub CiclaModalitaNavigazione(avanti As Boolean)
        Try
            Dim indiceCorrente As Integer = Array.IndexOf(OrdineModalitaNavigazione, modalitaNavigazioneCorrente)
            If indiceCorrente < 0 Then indiceCorrente = 0

            Dim nuovoIndice As Integer
            If avanti Then
                nuovoIndice = (indiceCorrente + 1) Mod OrdineModalitaNavigazione.Length
            Else
                nuovoIndice = (indiceCorrente - 1 + OrdineModalitaNavigazione.Length) Mod OrdineModalitaNavigazione.Length
            End If

            modalitaNavigazioneCorrente = OrdineModalitaNavigazione(nuovoIndice)
            AnnunciaModalitaNavigazione()
            Solleva(ModalitaNavigazioneCambiataEvent)
        Catch ex As Exception
            log.ScriviLog($"CiclaModalitaNavigazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AnnunciaModalitaNavigazione()
        Try
            Dim voce As sintesivocale = sintesivocale.GetInstance()
            If voce Is Nothing Then Return

            Dim testo As String
            Select Case modalitaNavigazioneCorrente
                Case ModalitaNavigazioneTouch.Campo
                    testo = "Campo"
                Case ModalitaNavigazioneTouch.Carattere
                    testo = "Carattere"
                Case ModalitaNavigazioneTouch.Riga
                    testo = "Riga"
                Case ModalitaNavigazioneTouch.Parola
                    testo = "Parola"
                Case ModalitaNavigazioneTouch.Paragrafo
                    testo = "Paragrafo"
                Case Else
                    testo = ""
            End Select

            If Not String.IsNullOrEmpty(testo) Then voce.LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog($"AnnunciaModalitaNavigazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Zittisce la sintesi. Viene chiamata appena il secondo dito tocca lo schermo, non al
    ''' rilascio: l'operazione e' innocua e ripetibile, e il silenzio immediato e' quello che
    ''' l'utente si aspetta dal tocco a due dita. Tutti gli altri gesti a due dita partono cosi'
    ''' da una sintesi ferma.
    ''' </summary>
    Private Sub FermaVoce()
        Try
            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then lettura.FermaVoce()
        Catch ex As Exception
            log.ScriviLog($"FermaVoce: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try

        Try
            Solleva(RichiestaSilenzioEvent)
        Catch ex As Exception
            log.ScriviLog($"FermaVoce (evento): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private ultimoTapOra As Double = 0
    Private ultimoTapX As Double = 0
    Private ultimoTapY As Double = 0
    Private inAttesaSecondoTap As Boolean = False
    Private ultimoTapDueDitaOra As Double = 0
    Private inAttesaSecondoTapDueDita As Boolean = False
    Private Const AttesaTerzoTapDueDitaMs As Integer = 350
    Private WithEvents timerEscapeDueDita As New Timer() With {.Interval = AttesaTerzoTapDueDitaMs}

    Private WithEvents timerAttesaHold As New Timer()
    Private WithEvents timerHoldPrimario As New Timer()
    Private pointerIdSecondoTap As UInteger
    Private pointerIdHoldPrimario As UInteger
    Private inEsplorazioneInterno As Boolean = False
    Private holdPrimarioEmesso As Boolean = False

    Private touchAttivoInterno As Boolean = False
    Private attivazioneRichiesta As Boolean = False
    Private finestraRichiesta As IntPtr = IntPtr.Zero
    Private contestoSincronizzazione As Threading.SynchronizationContext

    Private Const SW_RESTORE As Integer = 9
    Private Const VT_BOOL As UShort = 11US
    Private Const VARIANT_TRUE As Short = -1S

    Private ReadOnly HWND_TOPMOST As New IntPtr(-1)
    Private Const SWP_NOACTIVATE As UInteger = &H10UI
    Private Const SWP_SHOWWINDOW As UInteger = &H40UI

    Private mantieniSchermoInteroInterno As Boolean = True

    ''' <summary>
    ''' Tiene la finestra del programma a schermo intero e sopra a tutte le altre. E' la difesa
    ''' che sostituisce l'impossibile disattivazione dei gesti di bordo: quei gesti non si possono
    ''' bloccare da dentro il programma, ma se nessuna altra finestra e' raggiungibile con il dito
    ''' non hanno nulla su cui agire, e la barra delle applicazioni resta coperta.
    ''' </summary>
    Public Property MantieniSchermoIntero As Boolean
        Get
            Return mantieniSchermoInteroInterno
        End Get
        Set(value As Boolean)
            Try
                mantieniSchermoInteroInterno = value
                If value Then ApplicaSchermoIntero()
            Catch ex As Exception
                log.ScriviLog($"MantieniSchermoIntero Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    ''' <summary>
    ''' Porta la finestra a coprire l'intero schermo, barra delle applicazioni compresa, e la
    ''' fissa sopra le altre. Viene richiamata anche dalla guardia, perche' un'altra applicazione
    ''' che passa davanti o una barra che ricompare renderebbero di nuovo toccabile cio' che sta
    ''' fuori dal programma.
    ''' </summary>
    Public Sub ApplicaSchermoIntero()
        Try
            If Not mantieniSchermoInteroInterno Then Return

            Dim bersaglio As IntPtr = FinestraDaRiportareInPrimoPiano()
            If bersaglio = IntPtr.Zero Then Return

            DisattivaGestiDiBordoSeServe(bersaglio)

            Dim area As Rectangle = Screen.PrimaryScreen.Bounds
            Dim schermo As Screen = Screen.FromHandle(bersaglio)
            If schermo IsNot Nothing Then area = schermo.Bounds

            Dim attuale As New RECT()
            If GetWindowRect(bersaglio, attuale) Then
                If attuale.Left = area.Left AndAlso attuale.Top = area.Top AndAlso
                   (attuale.Right - attuale.Left) = area.Width AndAlso
                   (attuale.Bottom - attuale.Top) = area.Height Then
                    SetWindowPos(bersaglio, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE Or &H1UI Or &H2UI)
                    Return
                End If
            End If

            SetWindowPos(bersaglio, HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, SWP_SHOWWINDOW Or SWP_NOACTIVATE)
        Catch ex As Exception
            log.ScriviLog($"ApplicaSchermoIntero: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private WithEvents timerGuardiaFuoco As New Timer()
    Private WithEvents timerSorveglianzaDispositivo As New Timer()
    Private mantieniFuocoInterno As Boolean = True

    ''' <summary>
    ''' Quando e' attiva, se il fuoco finisce a una finestra estranea al programma questo viene
    ''' riportato subito in primo piano. Serve perche' il programma parla con la propria sintesi
    ''' interna e disattiva i lettori di schermo: un fuoco finito altrove non verrebbe annunciato,
    ''' e l'utente continuerebbe a digitare dentro un altro programma senza accorgersene, con il
    ''' rischio di modificare o perdere documenti esterni.
    ''' Va disattivata soltanto se il programma deve volutamente cedere il controllo a un'altra
    ''' applicazione.
    ''' </summary>
    Public Property MantieniFuocoNelProgramma As Boolean
        Get
            Return mantieniFuocoInterno
        End Get
        Set(value As Boolean)
            Try
                mantieniFuocoInterno = value
            Catch ex As Exception
                log.ScriviLog($"MantieniFuocoNelProgramma Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Private Function FinestraDaRiportareInPrimoPiano() As IntPtr
        Try
            If finestraPrincipale <> IntPtr.Zero AndAlso IsWindow(finestraPrincipale) Then Return finestraPrincipale

            Dim formaAttiva As Form = Form.ActiveForm
            If formaAttiva IsNot Nothing AndAlso Not formaAttiva.IsDisposed AndAlso formaAttiva.IsHandleCreated Then Return formaAttiva.Handle

            For Each formaAperta As Form In Application.OpenForms
                If formaAperta IsNot Nothing AndAlso Not formaAperta.IsDisposed AndAlso formaAperta.Visible AndAlso formaAperta.IsHandleCreated Then
                    Return formaAperta.Handle
                End If
            Next

            Return IntPtr.Zero
        Catch ex As Exception
            log.ScriviLog($"FinestraDaRiportareInPrimoPiano: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return IntPtr.Zero
        End Try
    End Function

    ''' <summary>
    ''' Riporta il programma davanti a tutto. Windows concede il primo piano solo al processo che
    ''' possiede l'input, percio' ci si aggancia temporaneamente al thread della finestra estranea:
    ''' senza quell'aggancio SetForegroundWindow verrebbe ignorata e la finestra si limiterebbe a
    ''' lampeggiare sulla barra delle applicazioni.
    ''' </summary>
    Private Sub RiportaProgrammaInPrimoPiano()
        Try
            Dim bersaglio As IntPtr = FinestraDaRiportareInPrimoPiano()
            If bersaglio = IntPtr.Zero Then Return

            If IsIconic(bersaglio) Then ShowWindow(bersaglio, SW_RESTORE)

            Dim finestraEstranea As IntPtr = GetForegroundWindow()
            Dim processoEstraneo As UInteger = 0
            Dim threadEstraneo As UInteger = 0
            If finestraEstranea <> IntPtr.Zero Then
                threadEstraneo = GetWindowThreadProcessId(finestraEstranea, processoEstraneo)
            End If

            Dim threadCorrente As UInteger = GetCurrentThreadId()
            Dim agganciato As Boolean = False

            If threadEstraneo <> 0 AndAlso threadEstraneo <> threadCorrente Then
                agganciato = AttachThreadInput(threadCorrente, threadEstraneo, True)
            End If

            BringWindowToTop(bersaglio)
            SetForegroundWindow(bersaglio)

            If agganciato Then AttachThreadInput(threadCorrente, threadEstraneo, False)
        Catch ex As Exception
            log.ScriviLog($"RiportaProgrammaInPrimoPiano: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub timerGuardiaFuoco_Tick(sender As Object, e As EventArgs) Handles timerGuardiaFuoco.Tick
        Try
            If Not touchAttivoInterno Then Return

            ' Questa e' una guardia indipendente da quella di WGContenitore/WGTastiera (serve al
            ' touch, non alla tastiera), ma deve rispettare la stessa sospensione: altrimenti,
            ' durante un'uscita voluta (Win+D/Win+M, o il fallback del servizio), questa avrebbe
            ' ripreso a riportare il programma in primo piano/schermo intero senza saperne nulla,
            ' vanificando l'uscita.
            If WGContenitore.GuardiaFocusSospesa Then Return

            ' I contatti rimasti in elenco senza piu' dare notizie vanno tolti subito, non al
            ' tocco successivo: finche' restano, il gesto seguente risulta fatto con un dito in
            ' piu' di quelli realmente appoggiati e viene classificato male.
            ScartaPuntatoriScaduti()

            If ApplicazioneInPrimoPiano() Then
                ApplicaSchermoIntero()
                Return
            End If

            If Not mantieniFuocoInterno Then Return

            AzzeraStatoGesto()
            RiportaProgrammaInPrimoPiano()
            ApplicaSchermoIntero()
        Catch ex As Exception
            log.ScriviLog($"timerGuardiaFuoco_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Sorveglia la comparsa e la scomparsa del digitalizzatore. Un touch screen collegato dopo
    ''' l'avvio del programma prima non veniva mai visto, perche' la disponibilita' era verificata
    ''' una sola volta dentro Attiva.
    ''' </summary>
    Private Sub timerSorveglianzaDispositivo_Tick(sender As Object, e As EventArgs) Handles timerSorveglianzaDispositivo.Tick
        Try
            If Not attivazioneRichiesta Then
                timerSorveglianzaDispositivo.Stop()
                Return
            End If

            If TouchDisponibile Then
                If Not touchAttivoInterno Then AttivaSeDisponibile()
            Else
                If touchAttivoInterno Then DisattivaInterno()
            End If
        Catch ex As Exception
            log.ScriviLog($"timerSorveglianzaDispositivo_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub CambioImpostazioniSchermo(sender As Object, e As EventArgs)
        Try
            puntiPerMillimetroPerMonitor.Clear()
            rettangoliPerDispositivo.Clear()
            _puntiPerMillimetro = 0
            ApplicaSchermoIntero()
            VerificaOrientamento()
        Catch ex As Exception
            log.ScriviLog($"CambioImpostazioniSchermo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub New()
        Try
            timerAttesaHold.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
            timerHoldPrimario.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
            timerEsplorazione.Interval = IntervalloVerificaEsplorazioneMs
            timerGuardiaFuoco.Interval = IntervalloGuardiaFuocoMs
            timerSorveglianzaDispositivo.Interval = IntervalloSorveglianzaDispositivoMs
        Catch ex As Exception
            log.ScriviLog($"New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function GetInstance() As WGTouk
        Try
            SyncLock bloccoIstanza
                If _istanza Is Nothing Then
                    _istanza = New WGTouk()
                End If
                Return _istanza
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"GetInstance: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return _istanza
        End Try
    End Function

    Public ReadOnly Property TouchDisponibile As Boolean
        Get
            Try
                Dim valore As Integer = GetSystemMetrics(SM_DIGITIZER)
                Return ((valore And NID_READY) <> 0) AndAlso (((valore And NID_INTEGRATED_TOUCH) <> 0) OrElse ((valore And NID_EXTERNAL_TOUCH) <> 0))
            Catch ex As Exception
                log.ScriviLog($"TouchDisponibile Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property TouchAttivo As Boolean
        Get
            Try
                Return touchAttivoInterno
            Catch ex As Exception
                log.ScriviLog($"TouchAttivo Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return False
            End Try
        End Get
    End Property

    Private Const QuotaTastieraVerticale As Double = 0.4
    Private Const QuotaTastieraOrizzontale As Double = 0.5
    Private orientamentoBloccatoInterno As Boolean = False
    Private ultimoOrientamentoFisico As OrientamentoSchermo = OrientamentoSchermo.Orizzontale

    ''' <summary>
    ''' Con il blocco attivo i controlli mantengono la disposizione fissa di sempre, qualunque
    ''' sia la rotazione dello schermo. Senza blocco menu, campi e tastiera si dispongono in
    ''' base al rapporto fra larghezza e altezza dell'area in cui si trovano.
    ''' </summary>
    Public Property OrientamentoBloccato As Boolean
        Get
            Return orientamentoBloccatoInterno
        End Get
        Set(value As Boolean)
            Try
                If orientamentoBloccatoInterno = value Then Return
                orientamentoBloccatoInterno = value
                Solleva(OrientamentoCambiatoEvent)
            Catch ex As Exception
                log.ScriviLog($"OrientamentoBloccato Set: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Public ReadOnly Property LayoutAdattivo As Boolean
        Get
            Return Not orientamentoBloccatoInterno
        End Get
    End Property

    Public ReadOnly Property Orientamento As OrientamentoSchermo
        Get
            Return OrientamentoFisico()
        End Get
    End Property

    Public Function UsaLayoutOrizzontale(larghezza As Integer, altezza As Integer) As Boolean
        Try
            If orientamentoBloccatoInterno Then Return False
            Return larghezza > 0 AndAlso larghezza >= altezza
        Catch ex As Exception
            log.ScriviLog($"UsaLayoutOrizzontale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function UsaLayoutVerticale(larghezza As Integer, altezza As Integer) As Boolean
        Try
            If orientamentoBloccatoInterno Then Return False
            Return larghezza > 0 AndAlso altezza > larghezza
        Catch ex As Exception
            log.ScriviLog($"UsaLayoutVerticale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function QuotaAltezzaTastiera(larghezza As Integer, altezza As Integer, quotaSeBloccato As Double) As Double
        Try
            If orientamentoBloccatoInterno Then Return quotaSeBloccato
            If larghezza > 0 AndAlso larghezza >= altezza Then Return QuotaTastieraOrizzontale
            Return QuotaTastieraVerticale
        Catch ex As Exception
            log.ScriviLog($"QuotaAltezzaTastiera: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return quotaSeBloccato
        End Try
    End Function

    Private Function OrientamentoFisico() As OrientamentoSchermo
        Try
            Dim schermo As Screen = Nothing
            If finestraPrincipale <> IntPtr.Zero AndAlso IsWindow(finestraPrincipale) Then
                schermo = Screen.FromHandle(finestraPrincipale)
            End If
            If schermo Is Nothing Then schermo = Screen.PrimaryScreen
            If schermo Is Nothing Then Return OrientamentoSchermo.Orizzontale
            If schermo.Bounds.Height > schermo.Bounds.Width Then Return OrientamentoSchermo.Verticale
            Return OrientamentoSchermo.Orizzontale
        Catch ex As Exception
            log.ScriviLog($"OrientamentoFisico: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return OrientamentoSchermo.Orizzontale
        End Try
    End Function

    Private Sub VerificaOrientamento()
        Try
            Dim attuale As OrientamentoSchermo = OrientamentoFisico()
            If attuale = ultimoOrientamentoFisico Then Return
            ultimoOrientamentoFisico = attuale
            Solleva(OrientamentoCambiatoEvent)
        Catch ex As Exception
            log.ScriviLog($"VerificaOrientamento: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Finestra principale su cui applicare le impostazioni tablet di Windows. Il rilevamento
    ''' dei gesti NON dipende piu' da questa finestra: avviene tramite un filtro dei messaggi
    ''' installato sull'intera applicazione (vedi PreFilterMessage), cosi' il tocco viene visto
    ''' qualunque sia la finestra a fuoco, campi annidati compresi. Prima invece il gestore era
    ''' agganciato a una sola finestra per volta, e questo rendeva impossibile far convivere il
    ''' movimento dentro un campo con il cambio di campo gestito dalla schermata che lo contiene.
    ''' </summary>
    Private finestraPrincipale As IntPtr = IntPtr.Zero

    Private ReadOnly finestreConGestiDiBordoDisattivati As New HashSet(Of IntPtr)

    ''' <summary>
    ''' Imposta System.EdgeGesture.DisableTouchWhenFullscreen sulla finestra indicata. Con questa
    ''' proprieta' Windows non applica i gesti di bordo mentre la finestra e' attiva e a schermo
    ''' intero, cioe' mentre le sue dimensioni corrispondono alla risoluzione dello schermo.
    ''' Non servono privilegi di amministratore ne' modifiche al registro di configurazione.
    ''' </summary>
    Public Function DisattivaGestiDiBordoPerFinestra(finestra As IntPtr) As Boolean
        Dim archivio As IPropertyStore = Nothing

        Try
            If finestra = IntPtr.Zero OrElse Not IsWindow(finestra) Then
                log.ScriviLog("DisattivaGestiDiBordoPerFinestra: finestra non valida, gesti di bordo non disattivati")
                Return False
            End If

            Dim identificativoInterfaccia As New Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")
            Dim oggetto As Object = Nothing
            Dim esito As Integer = SHGetPropertyStoreForWindow(finestra, identificativoInterfaccia, oggetto)

            If esito <> 0 OrElse oggetto Is Nothing Then
                log.ScriviLog($"DisattivaGestiDiBordoPerFinestra: SHGetPropertyStoreForWindow non riuscita, HRESULT 0x{esito:X8}, ultimo errore {Marshal.GetLastWin32Error()}")
                Return False
            End If

            archivio = TryCast(oggetto, IPropertyStore)
            If archivio Is Nothing Then
                log.ScriviLog("DisattivaGestiDiBordoPerFinestra: interfaccia IPropertyStore non ottenuta")
                Return False
            End If

            Dim chiave As New PROPERTYKEY()
            chiave.fmtid = New Guid("32CE38B2-2C9A-41B1-9BC5-B3784394AA44")
            chiave.pid = 2UI

            Dim valore As New PROPVARIANT()
            valore.vt = VT_BOOL
            valore.valoreBooleano = VARIANT_TRUE

            archivio.SetValue(chiave, valore)

            finestreConGestiDiBordoDisattivati.Add(finestra)
            Return True
        Catch ex As Exception
            log.ScriviLog($"DisattivaGestiDiBordoPerFinestra: impostazione non riuscita, gesti di bordo ancora attivi. {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        Finally
            Try
                If archivio IsNot Nothing Then Marshal.ReleaseComObject(archivio)
            Catch exRilascio As Exception
                log.ScriviLog($"DisattivaGestiDiBordoPerFinestra (rilascio): {exRilascio.Message}")
            End Try
        End Try
    End Function

    Private Sub DisattivaGestiDiBordoSeServe(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero Then Return
            If finestreConGestiDiBordoDisattivati.Contains(finestra) Then Return
            DisattivaGestiDiBordoPerFinestra(finestra)
        Catch ex As Exception
            log.ScriviLog($"DisattivaGestiDiBordoSeServe: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub Attiva(finestra As IntPtr)
        Try
            attivazioneRichiesta = True
            finestraRichiesta = finestra
            timerSorveglianzaDispositivo.Interval = IntervalloSorveglianzaDispositivoMs
            timerSorveglianzaDispositivo.Start()
            AttivaSeDisponibile()
        Catch ex As Exception
            log.ScriviLog($"Attiva: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AttivaSeDisponibile()
        Try
            If touchAttivoInterno Then Return
            If Not attivazioneRichiesta Then Return
            If Not TouchDisponibile Then Return

            finestraPrincipale = finestraRichiesta
            contestoSincronizzazione = Threading.SynchronizationContext.Current

            massimoDitaSupportate = LeggiMassimoDitaSupportate()
            If massimoDitaSupportate < MassimoDitaGesto Then
                log.ScriviLog($"AttivaSeDisponibile: il digitalizzatore riferisce al massimo {massimoDitaSupportate} contatti, i gesti che ne richiedono di piu' non saranno disponibili")
            End If

            ApplicaImpostazioniFinestra(finestraPrincipale)
            DisattivaGestiDiBordoPerFinestra(finestraPrincipale)

            Try
                AddHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, AddressOf CambioImpostazioniSchermo
            Catch exEventi As Exception
                log.ScriviLog($"AttivaSeDisponibile (eventi schermo): {exEventi.Message}")
            End Try

            Application.AddMessageFilter(Me)
            timerAttesaHold.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
            timerHoldPrimario.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
            timerEsplorazione.Interval = IntervalloVerificaEsplorazioneMs
            timerGuardiaFuoco.Interval = IntervalloGuardiaFuocoMs
            timerGuardiaFuoco.Start()
            ApplicaSchermoIntero()
            AzzeraStatoGesto()
            touchAttivoInterno = True
            Solleva(StatoCambiatoEvent)
        Catch ex As Exception
            log.ScriviLog($"AttivaSeDisponibile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub Disattiva()
        Try
            attivazioneRichiesta = False
            If timerSorveglianzaDispositivo.Enabled Then timerSorveglianzaDispositivo.Stop()
            DisattivaInterno()
        Catch ex As Exception
            log.ScriviLog($"Disattiva: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub DisattivaInterno()
        Try
            If Not touchAttivoInterno Then Return
            If timerGuardiaFuoco.Enabled Then timerGuardiaFuoco.Stop()
            finestreConGestiDiBordoDisattivati.Clear()

            Try
                RemoveHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, AddressOf CambioImpostazioniSchermo
            Catch exEventi As Exception
                log.ScriviLog($"DisattivaInterno (eventi schermo): {exEventi.Message}")
            End Try

            Application.RemoveMessageFilter(Me)
            If finestraPrincipale <> IntPtr.Zero Then
                RemoveProp(finestraPrincipale, "MicrosoftTabletPenServiceProperty")
                finestraPrincipale = IntPtr.Zero
            End If
            AzzeraStatoGesto()
            touchAttivoInterno = False
            Solleva(StatoCambiatoEvent)
        Catch ex As Exception
            log.ScriviLog($"DisattivaInterno: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Non fa piu' nulla: il rilevamento dei gesti avviene ora su tutta l'applicazione tramite
    ''' filtro dei messaggi, quindi non esiste piu' una "finestra agganciata" da cambiare. Il
    ''' metodo resta per non dover modificare i punti che lo chiamavano.
    ''' </summary>
    Public Sub RiassegnaControllo(nuovaFinestra As IntPtr)
        Try
            If nuovaFinestra = IntPtr.Zero Then Exit Sub
            ApplicaImpostazioniFinestra(nuovaFinestra)
        Catch ex As Exception
            log.ScriviLog($"RiassegnaControllo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApplicaImpostazioniFinestra(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero Then Exit Sub
            SetProp(finestra, "MicrosoftTabletPenServiceProperty", New IntPtr(CInt(TABLET_DISABLE_PRESSANDHOLD Or TABLET_DISABLE_PENTAPFEEDBACK Or TABLET_DISABLE_PENBARRELFEEDBACK Or TABLET_DISABLE_TOUCHUIFORCEON Or TABLET_DISABLE_TOUCHUIFORCEOFF Or TABLET_DISABLE_TOUCHSWITCH Or TABLET_DISABLE_FLICKS Or TABLET_DISABLE_SMOOTHSCROLLING Or TABLET_DISABLE_FLICKFALLBACKKEYS)))
        Catch ex As Exception
            log.ScriviLog($"ApplicaImpostazioniFinestra: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Vero solo se la finestra in primo piano appartiene a questo programma. I gesti vanno
    ''' interpretati soltanto quando WinGabriel e' davvero l'applicazione in uso: senza questo
    ''' controllo il tocco verrebbe interpretato anche mentre l'utente sta lavorando con un
    ''' altro programma, interferendo con il resto del sistema.
    ''' </summary>
    Public Function ControlloConFuoco() As Control
        Try
            Dim formaAttiva As Form = Form.ActiveForm
            If formaAttiva Is Nothing Then
                For Each formaAperta As Form In Application.OpenForms
                    If formaAperta IsNot Nothing AndAlso Not formaAperta.IsDisposed AndAlso formaAperta.Visible Then
                        formaAttiva = formaAperta
                        Exit For
                    End If
                Next
            End If
            If formaAttiva Is Nothing OrElse formaAttiva.IsDisposed Then Return Nothing

            Dim corrente As Control = formaAttiva
            Dim contenitore As ContainerControl = TryCast(corrente, ContainerControl)
            Do While contenitore IsNot Nothing
                Dim figlio As Control = contenitore.ActiveControl
                If figlio Is Nothing OrElse figlio.IsDisposed Then Exit Do
                corrente = figlio
                contenitore = TryCast(figlio, ContainerControl)
            Loop

            Return corrente
        Catch ex As Exception
            log.ScriviLog($"ControlloConFuoco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function EDestinatarioDiretto(candidato As Control) As Boolean
        Try
            If candidato Is Nothing OrElse candidato.IsDisposed Then Return False
            Dim conFuoco As Control = ControlloConFuoco()
            If conFuoco Is Nothing Then Return False
            Return ReferenceEquals(conFuoco, candidato)
        Catch ex As Exception
            log.ScriviLog($"EDestinatarioDiretto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function EContenitoreDelFuoco(candidato As Control) As Boolean
        Try
            If candidato Is Nothing OrElse candidato.IsDisposed Then Return False
            Dim conFuoco As Control = ControlloConFuoco()
            If conFuoco Is Nothing Then Return False
            If ReferenceEquals(conFuoco, candidato) Then Return False
            Return candidato.Contains(conFuoco)
        Catch ex As Exception
            log.ScriviLog($"EContenitoreDelFuoco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function NessunDestinatarioInterno(candidato As Control, tipoInterno As Type) As Boolean
        Try
            If candidato Is Nothing OrElse candidato.IsDisposed Then Return False
            Dim conFuoco As Control = ControlloConFuoco()
            If conFuoco Is Nothing Then Return True
            If tipoInterno Is Nothing Then Return True
            Return Not tipoInterno.IsInstanceOfType(conFuoco)
        Catch ex As Exception
            log.ScriviLog($"NessunDestinatarioInterno: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private processoCorrenteMemorizzato As UInteger = 0

    Private Function ApplicazioneInPrimoPiano() As Boolean
        Try
            Dim finestraAttiva As IntPtr = GetForegroundWindow()
            If finestraAttiva = IntPtr.Zero Then Return False

            Dim processoFinestra As UInteger = 0
            GetWindowThreadProcessId(finestraAttiva, processoFinestra)
            If processoFinestra = 0 Then Return False

            If processoCorrenteMemorizzato = 0 Then
                processoCorrenteMemorizzato = CUInt(Diagnostics.Process.GetCurrentProcess().Id)
            End If

            Return processoFinestra = processoCorrenteMemorizzato
        Catch ex As Exception
            log.ScriviLog($"ApplicazioneInPrimoPiano: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function OraMs() As Double
        Try
            Return orologio.Elapsed.TotalMilliseconds
        Catch ex As Exception
            log.ScriviLog($"OraMs: {ex.Message}")
            Return 0
        End Try
    End Function

    Private scostamentoOrologioValido As Boolean = False
    Private scostamentoOrologioMs As Double = 0

    ''' <summary>
    ''' Converte il contatore ad alta risoluzione allegato al contatto nella stessa base di tempo
    ''' dell'orologio interno. Il timestamp hardware misura quando il dito e' stato campionato dal
    ''' digitalizzatore, non quando il messaggio e' arrivato: la differenza vale parecchi
    ''' millisecondi e falsava ogni stima di velocita'.
    ''' </summary>
    Private Function TempoCampione(contatore As ULong) As Double
        Try
            Dim ora As Double = OraMs()
            If contatore = 0UL Then Return ora

            Dim frequenza As Double = CDbl(Diagnostics.Stopwatch.Frequency)
            If frequenza <= 0 Then Return ora

            Dim tempoContatore As Double = (CDbl(contatore) / frequenza) * 1000.0

            If Not scostamentoOrologioValido Then
                scostamentoOrologioMs = ora - tempoContatore
                scostamentoOrologioValido = True
            End If

            Dim convertito As Double = tempoContatore + scostamentoOrologioMs
            If Math.Abs(convertito - ora) > 1000 Then
                scostamentoOrologioMs = ora - tempoContatore
                convertito = ora
            End If

            Return convertito
        Catch ex As Exception
            log.ScriviLog($"TempoCampione: {ex.Message}")
            Return OraMs()
        End Try
    End Function

    Private Function PuntiPerMillimetro() As Double
        Try
            If _puntiPerMillimetro > 0 Then Return _puntiPerMillimetro
            Using grafica As Graphics = Graphics.FromHwnd(IntPtr.Zero)
                Dim puntiPerPollice As Double = grafica.DpiX
                If puntiPerPollice <= 0 Then puntiPerPollice = 96
                _puntiPerMillimetro = puntiPerPollice / 25.4
            End Using
            Return _puntiPerMillimetro
        Catch ex As Exception
            log.ScriviLog($"PuntiPerMillimetro: {ex.Message}")
            _puntiPerMillimetro = 96 / 25.4
            Return _puntiPerMillimetro
        End Try
    End Function

    ''' <summary>
    ''' Punti per millimetro del monitor su cui si trova il punto indicato. Le coordinate dei
    ''' contatti arrivano in pixel fisici, quindi la conversione deve usare la densita' fisica di
    ''' quel monitor: un unico valore ricavato dallo schermo principale sbagliava tutte le soglie
    ''' appena il portatile veniva collegato a uno schermo con scala diversa.
    ''' </summary>
    Private Function PuntiPerMillimetroDelPunto(x As Double, y As Double) As Double
        Try
            Dim punto As New PUNTOSCHERMO() With {.x = CInt(Math.Round(x)), .y = CInt(Math.Round(y))}
            Dim monitor As IntPtr = MonitorFromPoint(punto, MONITOR_DEFAULTTONEAREST)
            If monitor = IntPtr.Zero Then Return PuntiPerMillimetro()

            Dim memorizzato As Double = 0
            If puntiPerMillimetroPerMonitor.TryGetValue(monitor, memorizzato) Then
                If memorizzato > 0 Then Return memorizzato
            End If

            Dim puntiPerPollice As Double = 0

            Try
                Dim dpiOrizzontale As UInteger = 0
                Dim dpiVerticale As UInteger = 0
                If GetDpiForMonitor(monitor, MDT_RAW_DPI, dpiOrizzontale, dpiVerticale) = 0 Then
                    If dpiOrizzontale >= 50UI AndAlso dpiOrizzontale <= 600UI Then puntiPerPollice = CDbl(dpiOrizzontale)
                End If
                If puntiPerPollice <= 0 Then
                    dpiOrizzontale = 0
                    dpiVerticale = 0
                    If GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, dpiOrizzontale, dpiVerticale) = 0 Then
                        If dpiOrizzontale >= 50UI AndAlso dpiOrizzontale <= 600UI Then puntiPerPollice = CDbl(dpiOrizzontale)
                    End If
                End If
            Catch exDpi As Exception
                log.ScriviLog($"PuntiPerMillimetroDelPunto (GetDpiForMonitor non disponibile): {exDpi.Message}")
            End Try

            If puntiPerPollice <= 0 Then
                Dim risultatoBase As Double = PuntiPerMillimetro()
                puntiPerMillimetroPerMonitor(monitor) = risultatoBase
                Return risultatoBase
            End If

            Dim risultato As Double = puntiPerPollice / 25.4
            puntiPerMillimetroPerMonitor(monitor) = risultato
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"PuntiPerMillimetroDelPunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return PuntiPerMillimetro()
        End Try
    End Function

    Private Function LeggiInfoPuntatore(idPuntatore As UInteger, ByRef informazioni As POINTER_INFO) As Boolean
        Try
            informazioni = New POINTER_INFO()
            Return GetPointerInfo(idPuntatore, informazioni)
        Catch ex As Exception
            log.ScriviLog($"LeggiInfoPuntatore: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function LeggiInfoTocco(idPuntatore As UInteger, ByRef informazioni As POINTER_TOUCH_INFO) As Boolean
        Try
            informazioni = New POINTER_TOUCH_INFO()
            Return GetPointerTouchInfo(idPuntatore, informazioni)
        Catch ex As Exception
            log.ScriviLog($"LeggiInfoTocco: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function RettangoliDelDispositivo(dispositivo As IntPtr) As RettangoliDispositivo
        Try
            If dispositivo = IntPtr.Zero Then Return Nothing

            Dim memorizzati As RettangoliDispositivo = Nothing
            If rettangoliPerDispositivo.TryGetValue(dispositivo, memorizzati) Then Return memorizzati

            Dim nuovi As New RettangoliDispositivo()
            Dim areaDispositivo As New RECT()
            Dim areaSchermo As New RECT()

            If GetPointerDeviceRects(dispositivo, areaDispositivo, areaSchermo) Then
                nuovi.AreaDispositivo = areaDispositivo
                nuovi.AreaSchermo = areaSchermo
                nuovi.Valida = (areaDispositivo.Right - areaDispositivo.Left) > 0 AndAlso
                               (areaDispositivo.Bottom - areaDispositivo.Top) > 0 AndAlso
                               (areaSchermo.Right - areaSchermo.Left) > 0 AndAlso
                               (areaSchermo.Bottom - areaSchermo.Top) > 0
            Else
                nuovi.Valida = False
            End If

            rettangoliPerDispositivo(dispositivo) = nuovi
            Return nuovi
        Catch ex As Exception
            log.ScriviLog($"RettangoliDelDispositivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Posizione del contatto con risoluzione migliore del pixel. Il digitalizzatore campiona in
    ''' centesimi di millimetro, e quella misura arriva nel campo himetric: convertita con i
    ''' rettangoli del dispositivo restituisce frazioni di pixel, mentre le coordinate intere di
    ''' lParam quantizzavano ogni spostamento e rendevano rumorosa la stima della velocita'.
    ''' </summary>
    Private Function PosizioneFine(informazioni As POINTER_INFO) As PointF
        Try
            Dim rettangoli As RettangoliDispositivo = RettangoliDelDispositivo(informazioni.sourceDevice)

            If rettangoli IsNot Nothing AndAlso rettangoli.Valida Then
                Dim himetricX As Double = informazioni.ptHimetricLocationRaw.x
                Dim himetricY As Double = informazioni.ptHimetricLocationRaw.y

                If himetricX = 0 AndAlso himetricY = 0 Then
                    himetricX = informazioni.ptHimetricLocation.x
                    himetricY = informazioni.ptHimetricLocation.y
                End If

                If himetricX <> 0 OrElse himetricY <> 0 Then
                    Dim larghezzaDispositivo As Double = rettangoli.AreaDispositivo.Right - rettangoli.AreaDispositivo.Left
                    Dim altezzaDispositivo As Double = rettangoli.AreaDispositivo.Bottom - rettangoli.AreaDispositivo.Top
                    Dim larghezzaSchermo As Double = rettangoli.AreaSchermo.Right - rettangoli.AreaSchermo.Left
                    Dim altezzaSchermo As Double = rettangoli.AreaSchermo.Bottom - rettangoli.AreaSchermo.Top

                    Dim x As Double = rettangoli.AreaSchermo.Left + ((himetricX - rettangoli.AreaDispositivo.Left) * larghezzaSchermo / larghezzaDispositivo)
                    Dim y As Double = rettangoli.AreaSchermo.Top + ((himetricY - rettangoli.AreaDispositivo.Top) * altezzaSchermo / altezzaDispositivo)

                    Dim distanzaDalPixel As Double = Math.Abs(x - informazioni.ptPixelLocationRaw.x) + Math.Abs(y - informazioni.ptPixelLocationRaw.y)
                    If distanzaDalPixel <= 8 Then Return New PointF(CSng(x), CSng(y))
                End If
            End If

            If informazioni.ptPixelLocationRaw.x <> 0 OrElse informazioni.ptPixelLocationRaw.y <> 0 Then
                Return New PointF(informazioni.ptPixelLocationRaw.x, informazioni.ptPixelLocationRaw.y)
            End If

            Return New PointF(informazioni.ptPixelLocation.x, informazioni.ptPixelLocation.y)
        Catch ex As Exception
            log.ScriviLog($"PosizioneFine: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New PointF(informazioni.ptPixelLocation.x, informazioni.ptPixelLocation.y)
        End Try
    End Function

    Private Function Mediana(valori As List(Of Double)) As Double
        Try
            If valori Is Nothing OrElse valori.Count = 0 Then Return 0

            Dim ordinati As New List(Of Double)(valori)
            ordinati.Sort()

            Dim meta As Integer = ordinati.Count \ 2
            If (ordinati.Count Mod 2) = 1 Then Return ordinati(meta)
            Return (ordinati(meta - 1) + ordinati(meta)) / 2
        Catch ex As Exception
            log.ScriviLog($"Mediana: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Raccoglie la dimensione dei contatti accettati e ne tiene la mediana, che e' la misura di
    ''' un dito su questo schermo. La mediana regge anche se qualche palmo entra nel campione,
    ''' perche' i contatti larghi restano una minoranza; finche' il campione e' troppo piccolo
    ''' nessun contatto viene scartato per dimensione, cosi' un digitalizzatore che riferisce
    ''' l'area in modo inconsueto non blocca il tocco appena il programma parte.
    ''' </summary>
    Private Sub RegistraLatoContatto(lato As Double)
        Try
            If lato <= 0 Then Return

            latiContattoOsservati.Add(lato)
            While latiContattoOsservati.Count > MassimoCampioniContatto
                latiContattoOsservati.RemoveAt(0)
            End While

            If latiContattoOsservati.Count < CampioniContattoPerCalibrazione Then Return
            latoTipicoContatto = Mediana(latiContattoOsservati)
        Catch ex As Exception
            log.ScriviLog($"RegistraLatoContatto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Cadenza con cui il digitalizzatore consegna i campioni, misurata sull'uso. Da essa dipende
    ''' la permanenza richiesta prima di annunciare un elemento: su un dispositivo che campiona a
    ''' bassa frequenza un tempo fisso breve non lascia arrivare nemmeno due campioni, su uno
    ''' rapido fa aspettare per niente.
    ''' </summary>
    Private Sub RegistraIntervalloFraCampioni(intervallo As Double)
        Try
            If intervallo <= 0 OrElse intervallo > 200 Then Return

            intervalliFraCampioni.Add(intervallo)
            While intervalliFraCampioni.Count > MassimoCampioniContatto
                intervalliFraCampioni.RemoveAt(0)
            End While

            If intervalliFraCampioni.Count < CampioniContattoPerCalibrazione Then Return
            intervalloTipicoFraCampioni = Mediana(intervalliFraCampioni)
        Catch ex As Exception
            log.ScriviLog($"RegistraIntervalloFraCampioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function PermanenzaRichiesta() As Double
        Try
            If Not AdattaAlDispositivo Then Return PermanenzaEsplorazioneMs
            If intervalloTipicoFraCampioni <= 0 Then Return PermanenzaEsplorazioneMs

            Dim valore As Double = intervalloTipicoFraCampioni * 2
            If valore < PermanenzaMinimaMs Then valore = PermanenzaMinimaMs
            If valore > PermanenzaMassimaMs Then valore = PermanenzaMassimaMs
            Return valore
        Catch ex As Exception
            log.ScriviLog($"PermanenzaRichiesta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return PermanenzaEsplorazioneMsPredefinita
        End Try
    End Function

    ''' <summary>
    ''' Dita che il digitalizzatore e' in grado di riferire, mai piu' di quelle che i gesti usano.
    ''' Su un dispositivo da due punti il terzo contatto non arriva affatto, e senza questo limite
    ''' il conteggio restava in attesa di un dito che nessuno avrebbe mai consegnato.
    ''' </summary>
    Private Function LeggiMassimoDitaSupportate() As Integer
        Try
            Dim valore As Integer = GetSystemMetrics(SM_MAXIMUMTOUCHES)
            If valore <= 0 Then Return MassimoDitaGesto
            Return Math.Max(1, Math.Min(MassimoDitaGesto, valore))
        Catch ex As Exception
            log.ScriviLog($"LeggiMassimoDitaSupportate: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return MassimoDitaGesto
        End Try
    End Function

    ''' <summary>
    ''' Vero se il contatto va considerato un dito intenzionale. Il palmo appoggiato sul bordo
    ''' dello schermo produceva contatti che portavano il conteggio a tre e cambiavano modalita'
    ''' di navigazione da sola: la dimensione del contatto, confrontata con quella tipica di un
    ''' dito su questo schermo, e il giudizio del sistema li scartano. La confidenza viene usata
    ''' come motivo di scarto solo sui dispositivi che quel segnale lo impostano davvero,
    ''' altrimenti su un digitalizzatore che non lo valorizza nessun tocco risulterebbe valido.
    ''' </summary>
    Private Function ContattoAccettabile(idPuntatore As UInteger, ByRef latoContatto As Double) As Boolean
        Try
            latoContatto = 0

            Dim informazioniTocco As New POINTER_TOUCH_INFO()
            If Not LeggiInfoTocco(idPuntatore, informazioniTocco) Then Return True

            If (informazioniTocco.pointerInfo.pointerFlags And POINTER_FLAG_CANCELED) <> 0UI Then Return False

            If (informazioniTocco.pointerInfo.pointerFlags And POINTER_FLAG_CONFIDENCE) <> 0UI Then
                dispositivoUsaConfidenza = True
            ElseIf dispositivoUsaConfidenza Then
                Return False
            End If

            If (informazioniTocco.touchMask And TOUCH_MASK_CONTACTAREA) <> 0UI Then
                Dim larghezza As Double = informazioniTocco.rcContact.Right - informazioniTocco.rcContact.Left
                Dim altezza As Double = informazioniTocco.rcContact.Bottom - informazioniTocco.rcContact.Top
                latoContatto = Math.Max(larghezza, altezza)

                If latoContatto > 0 Then
                    If AdattaAlDispositivo Then
                        If latoTipicoContatto > 0 AndAlso latoContatto > (latoTipicoContatto * FattoreScartoContatto) Then Return False
                    ElseIf latoContatto > LatoMassimoContattoPixel Then
                        Return False
                    End If

                    RegistraLatoContatto(latoContatto)
                End If
            End If

            Return True
        Catch ex As Exception
            log.ScriviLog($"ContattoAccettabile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Vero se il messaggio riguarda un contatto delle dita e non una penna o il mouse. Serve a
    ''' bloccare i soli messaggi di tocco lasciando passare gli altri dispositivi di puntamento.
    ''' Al rilascio il puntatore puo' gia' non essere piu' interrogabile, percio' gli identificatori
    ''' riconosciuti all'appoggio restano annotati fino alla fine del contatto.
    ''' </summary>
    Private Function EMessaggioDiTocco(idPuntatore As UInteger) As Boolean
        Try
            If puntatoriDiTocco.Contains(idPuntatore) Then Return True

            Dim tipoPuntatore As UInteger = 0
            If Not GetPointerType(idPuntatore, tipoPuntatore) Then Return False
            If tipoPuntatore <> PT_TOUCH Then Return False

            puntatoriDiTocco.Add(idPuntatore)
            Return True
        Catch ex As Exception
            log.ScriviLog($"EMessaggioDiTocco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        Try
            If Not touchAttivoInterno Then Return False

            Select Case m.Msg
                Case WM_POINTERDOWN, WM_POINTERUPDATE, WM_POINTERUP, WM_POINTERCAPTURECHANGED, WM_POINTERLEAVE
                Case Else
                    Return False
            End Select

            Dim idPuntatore As UInteger = CUInt(m.WParam.ToInt64() And &HFFFFL)

            If Not ApplicazioneInPrimoPiano() Then
                AzzeraStatoGesto()
                Return False
            End If

            Dim eTocco As Boolean = EMessaggioDiTocco(idPuntatore)

            Select Case m.Msg
                Case WM_POINTERDOWN
                    If eTocco Then GestisciPointerDown(idPuntatore, m.LParam)
                Case WM_POINTERUPDATE
                    If eTocco Then GestisciPointerUpdate(idPuntatore, m.LParam)
                Case WM_POINTERUP
                    If eTocco Then GestisciPointerUp(idPuntatore, m.LParam)
                    puntatoriDiTocco.Remove(idPuntatore)
                Case WM_POINTERCAPTURECHANGED
                    ' Sul distacco Windows manda spesso CAPTURECHANGED prima di POINTERUP. Trattarlo
                    ' come annullamento toglieva il contatto e la lettera non veniva mai confermata.
                    If eTocco Then GestisciFineContatto(idPuntatore, m.LParam, True)
                Case WM_POINTERLEAVE
                    If eTocco Then GestisciFineContatto(idPuntatore, m.LParam, False)
            End Select

            ' I messaggi di tocco vengono dichiarati gestiti e NON proseguono verso la finestra.
            ' Lasciandoli passare, DefWindowProc li promuove a messaggi di mouse: il dito diventa
            ' un clic che sposta il fuoco su cio' che sta sotto, e Windows smette di consegnare i
            ' contatti successivi al primo, rendendo impossibili i gesti a due e tre dita. Tutta
            ' l'interazione a tocco passa quindi dagli eventi di questa classe.
            Return eTocco
        Catch ex As Exception
            log.ScriviLog($"PreFilterMessage: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return False
    End Function

    ' Il gesto viene misurato sul CENTROIDE, cioe' il punto medio di tutte le dita appoggiate in
    ' quel momento: e' il criterio usato dai sistemi touch dei telefoni e l'unico che descriva
    ' davvero uno spostamento fatto con piu' dita insieme. Quando un dito si aggiunge o si stacca
    ' il centroide salta di colpo, quindi in quell'istante il riferimento viene ricalcolato senza
    ' accumulare il salto: si accumula solo il movimento avvenuto a numero di dita costante.
    Private centroideUltimoX As Double = 0
    Private centroideUltimoY As Double = 0
    Private numeroDitaCentroide As Integer = 0
    Private spostamentoGestoX As Double = 0
    Private spostamentoGestoY As Double = 0
    Private percorsoGesto As Double = 0
    Private distanzaMassimaGesto As Double = 0
    Private spostamentoMassimoDito As Double = 0
    Private distanzaFraDitaIniziale As Double = 0
    Private variazioneMassimaFraDita As Double = 0
    Private oraInizioGesto As Double = 0
    Private oraUltimoMovimento As Double = 0
    Private velocitaUltimoMovimento As Double = 0

    Private WithEvents timerEsplorazione As New Timer()

    Private xUltimaEsplorazione As Double = 0
    Private yUltimaEsplorazione As Double = 0
    Private esplorazioneMaiRiferita As Boolean = True
    Private identificativoRiferito As String = Nothing
    Private identificativoCandidato As String = Nothing
    Private oraCandidato As Double = 0

    ''' <summary>
    ''' Scatta a intervalli brevi mentre un solo dito e' appoggiato. Serve un tempo perche' un
    ''' dito immobile non produce messaggi di movimento, e senza questo un appoggio fermo non
    ''' annuncerebbe nulla; la stessa cadenza fa maturare la permanenza necessaria prima di
    ''' annunciare un elemento appena sfiorato.
    ''' </summary>
    Private Sub timerEsplorazione_Tick(sender As Object, e As EventArgs) Handles timerEsplorazione.Tick
        Try
            Dim contatto As InfoPuntatore = UnicoDitoDelGesto()
            If contatto Is Nothing Then
                timerEsplorazione.Stop()
                Return
            End If

            ValutaEsplorazione(contatto.XCorrente, contatto.YCorrente, OraMs())
        Catch ex As Exception
            log.ScriviLog($"timerEsplorazione_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function UnicoDitoDelGesto() As InfoPuntatore
        Try
            Dim trovato As InfoPuntatore = Nothing
            Dim quantita As Integer = 0

            For Each coppia As KeyValuePair(Of UInteger, InfoPuntatore) In puntatoriAttivi
                If Not coppia.Value.ContaNelGesto Then Continue For
                quantita += 1
                If quantita > 1 Then Return Nothing
                trovato = coppia.Value
            Next

            If quantita <> 1 Then Return Nothing
            Return trovato
        Catch ex As Exception
            log.ScriviLog($"UnicoDitoDelGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function ContaDitaNelGesto() As Integer
        Try
            Dim quantita As Integer = 0
            For Each coppia As KeyValuePair(Of UInteger, InfoPuntatore) In puntatoriAttivi
                If coppia.Value.ContaNelGesto Then quantita += 1
            Next
            Return quantita
        Catch ex As Exception
            log.ScriviLog($"ContaDitaNelGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Function IdentificativoSottoIlDito(x As Double, y As Double) As String
        Try
            Dim funzione As Func(Of Point, String) = IdentificaElemento
            If funzione Is Nothing Then Return Nothing
            Return funzione(New Point(CInt(Math.Round(x)), CInt(Math.Round(y))))
        Catch ex As Exception
            log.ScriviLog($"IdentificativoSottoIlDito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Decide se annunciare cio' che sta sotto il dito. Con l'identificativo dell'elemento la
    ''' decisione segue l'elemento e non la distanza: l'annuncio arriva al cambio di elemento e
    ''' solo se il dito vi resta sopra per la permanenza richiesta, cosi' un tasto attraversato
    ''' di striscio resta muto e sul confine fra due tasti la voce non rimbalza. Senza quella
    ''' funzione vale la vecchia regola dello spostamento minimo.
    ''' </summary>
    Private Sub ValutaEsplorazione(x As Double, y As Double, ora As Double)
        Try
            If gestoAnnullato Then Return
            If (ora - oraInizioGesto) < SogliaAnnuncioEsplorazioneMs Then Return

            Dim identificativo As String = IdentificativoSottoIlDito(x, y)

            If identificativo IsNot Nothing Then
                If String.Equals(identificativo, identificativoRiferito) Then
                    identificativoCandidato = Nothing
                    confermeElemento = 0
                    Return
                End If

                If Not String.Equals(identificativo, identificativoCandidato) Then
                    identificativoCandidato = identificativo
                    oraCandidato = ora
                    confermeElemento = 1
                    Return
                End If

                confermeElemento += 1
                If confermeElemento < ConfermeElementoRichieste Then Return
                If (ora - oraCandidato) < PermanenzaRichiesta() Then Return

                identificativoRiferito = identificativo
                identificativoCandidato = Nothing
                confermeElemento = 0
                RiferisciEsplorazione(x, y)
                Return
            End If

            If Not esplorazioneMaiRiferita Then
                Dim scartoX As Double = x - xUltimaEsplorazione
                Dim scartoY As Double = y - yUltimaEsplorazione
                Dim soglia As Double = SogliaEsplorazionePixel
                If ((scartoX * scartoX) + (scartoY * scartoY)) < (soglia * soglia) Then Return
            End If

            RiferisciEsplorazione(x, y)
        Catch ex As Exception
            log.ScriviLog($"ValutaEsplorazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub RiferisciEsplorazione(x As Double, y As Double)
        Try
            xUltimaEsplorazione = x
            yUltimaEsplorazione = y
            esplorazioneMaiRiferita = False
            Solleva(EsplorazioneToccoEvent, CInt(Math.Round(x)), CInt(Math.Round(y)))
        Catch ex As Exception
            log.ScriviLog($"RiferisciEsplorazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AzzeraMisureGesto(ora As Double)
        Try
            spostamentoGestoX = 0
            spostamentoGestoY = 0
            percorsoGesto = 0
            distanzaMassimaGesto = 0
            spostamentoMassimoDito = 0
            distanzaFraDitaIniziale = 0
            variazioneMassimaFraDita = 0
            centroideUltimoX = 0
            centroideUltimoY = 0
            numeroDitaCentroide = 0
            velocitaUltimoMovimento = 0
            velocitaMassimaGesto = 0
            confermeElemento = 0
            gestoAnnullato = False
            oraInizioGesto = ora
            oraUltimoMovimento = ora
            campioniVelocita.Clear()
            identificativoRiferito = Nothing
            identificativoCandidato = Nothing
            oraCandidato = ora
        Catch ex As Exception
            log.ScriviLog($"AzzeraMisureGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub RegistraCampioneVelocita(tempo As Double)
        Try
            If campioniVelocita.Count > 0 Then
                RegistraIntervalloFraCampioni(tempo - campioniVelocita(campioniVelocita.Count - 1).Tempo)
            End If

            Dim campione As New CampioneVelocita()
            campione.Tempo = tempo
            campione.X = spostamentoGestoX
            campione.Y = spostamentoGestoY
            campioniVelocita.Add(campione)

            Dim limite As Double = tempo - (FinestraVelocitaMs * 3)
            While campioniVelocita.Count > 2 AndAlso campioniVelocita(0).Tempo < limite
                campioniVelocita.RemoveAt(0)
            End While

            While campioniVelocita.Count > MassimoCampioniStorico
                campioniVelocita.RemoveAt(0)
            End While

            Dim velocitaAttuale As Double = VelocitaFinestra()
            If velocitaAttuale > velocitaMassimaGesto Then velocitaMassimaGesto = velocitaAttuale
        Catch ex As Exception
            log.ScriviLog($"RegistraCampioneVelocita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Velocita' media dell'ultima parte del gesto, in millimetri al millisecondo. Un solo
    ''' campione, quello dell'ultimo messaggio prima del distacco, era il valore meno affidabile
    ''' di tutto il gesto: il dito sta gia' rallentando o scivolando, e la stima cambiava da un
    ''' tentativo all'altro.
    ''' </summary>
    Private Function MisuraFinestraFinale(ByRef spostamento As Double, ByRef intervallo As Double) As Boolean
        Try
            spostamento = 0
            intervallo = 0

            If campioniVelocita.Count < 2 Then Return False

            Dim ultimo As CampioneVelocita = campioniVelocita(campioniVelocita.Count - 1)
            Dim primo As CampioneVelocita = campioniVelocita(0)

            For indice As Integer = campioniVelocita.Count - 1 To 0 Step -1
                If (ultimo.Tempo - campioniVelocita(indice).Tempo) >= FinestraVelocitaMs Then
                    primo = campioniVelocita(indice)
                    Exit For
                End If
                primo = campioniVelocita(indice)
            Next

            intervallo = ultimo.Tempo - primo.Tempo
            If intervallo <= 0 Then Return False

            Dim scartoX As Double = ultimo.X - primo.X
            Dim scartoY As Double = ultimo.Y - primo.Y
            spostamento = Math.Sqrt((scartoX * scartoX) + (scartoY * scartoY))
            Return True
        Catch ex As Exception
            log.ScriviLog($"MisuraFinestraFinale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function SpostamentoFinestraFinale() As Double
        Try
            Dim spostamento As Double = 0
            Dim intervallo As Double = 0
            If Not MisuraFinestraFinale(spostamento, intervallo) Then Return 0
            Return spostamento
        Catch ex As Exception
            log.ScriviLog($"SpostamentoFinestraFinale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Function VelocitaFinestra() As Double
        Try
            Dim spostamento As Double = 0
            Dim intervallo As Double = 0
            If Not MisuraFinestraFinale(spostamento, intervallo) Then Return velocitaUltimoMovimento

            Dim densita As Double = puntiPerMillimetroGesto
            If densita <= 0 Then densita = PuntiPerMillimetro()

            Return (spostamento / densita) / intervallo
        Catch ex As Exception
            log.ScriviLog($"VelocitaFinestra: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return velocitaUltimoMovimento
        End Try
    End Function

    Private Sub AggiornaDistanzaFraDita()
        Try
            Dim primo As InfoPuntatore = Nothing
            Dim secondo As InfoPuntatore = Nothing
            Dim quantita As Integer = 0

            For Each coppia As KeyValuePair(Of UInteger, InfoPuntatore) In puntatoriAttivi
                If Not coppia.Value.ContaNelGesto Then Continue For
                quantita += 1
                If quantita = 1 Then
                    primo = coppia.Value
                ElseIf quantita = 2 Then
                    secondo = coppia.Value
                End If
            Next

            If quantita <> 2 OrElse primo Is Nothing OrElse secondo Is Nothing Then Return

            Dim distanza As Double = Math.Sqrt(((primo.XCorrente - secondo.XCorrente) ^ 2) + ((primo.YCorrente - secondo.YCorrente) ^ 2))

            If distanzaFraDitaIniziale <= 0 Then
                distanzaFraDitaIniziale = distanza
                Return
            End If

            variazioneMassimaFraDita = Math.Max(variazioneMassimaFraDita, Math.Abs(distanza - distanzaFraDitaIniziale))
        Catch ex As Exception
            log.ScriviLog($"AggiornaDistanzaFraDita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Ricalcola il centroide delle dita che partecipano al gesto. Con accumula a True lo
    ''' spostamento rispetto al centroide precedente entra nel totale del gesto e aggiorna la
    ''' velocita', ma solo se nel frattempo il numero di dita non e' cambiato; con accumula a
    ''' False il centroide viene solo riallineato, ed e' quello che serve quando un dito si
    ''' appoggia o si stacca.
    ''' </summary>
    Private Sub AggiornaCentroide(accumula As Boolean, tempo As Double)
        Try
            Dim sommaX As Double = 0
            Dim sommaY As Double = 0
            Dim quantita As Integer = 0

            For Each coppia As KeyValuePair(Of UInteger, InfoPuntatore) In puntatoriAttivi
                If Not coppia.Value.ContaNelGesto Then Continue For
                sommaX += coppia.Value.XCorrente
                sommaY += coppia.Value.YCorrente
                quantita += 1
            Next

            If quantita = 0 Then
                numeroDitaCentroide = 0
                Return
            End If

            Dim nuovoX As Double = sommaX / quantita
            Dim nuovoY As Double = sommaY / quantita

            If accumula AndAlso numeroDitaCentroide = quantita Then
                Dim deltaX As Double = nuovoX - centroideUltimoX
                Dim deltaY As Double = nuovoY - centroideUltimoY
                spostamentoGestoX += deltaX
                spostamentoGestoY += deltaY

                Dim passo As Double = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY))
                percorsoGesto += passo
                distanzaMassimaGesto = Math.Max(distanzaMassimaGesto, Math.Sqrt((spostamentoGestoX * spostamentoGestoX) + (spostamentoGestoY * spostamentoGestoY)))

                Dim tempoTrascorso As Double = tempo - oraUltimoMovimento
                If tempoTrascorso > 0 Then
                    Dim densita As Double = puntiPerMillimetroGesto
                    If densita <= 0 Then densita = PuntiPerMillimetro()
                    velocitaUltimoMovimento = (passo / densita) / tempoTrascorso
                    oraUltimoMovimento = tempo
                End If

                RegistraCampioneVelocita(tempo)
            End If

            centroideUltimoX = nuovoX
            centroideUltimoY = nuovoY
            numeroDitaCentroide = quantita
        Catch ex As Exception
            log.ScriviLog($"AggiornaCentroide: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiornaCampione(info As InfoPuntatore, x As Double, y As Double, tempo As Double)
        Try
            If info Is Nothing Then Return

            info.XPrecedente = info.XCorrente
            info.YPrecedente = info.YCorrente
            info.XCorrente = x
            info.YCorrente = y
            info.OraUltimaNotizia = OraMs()
            info.TempoUltimoCampione = tempo

            Dim passo As Double = Math.Sqrt(((info.XCorrente - info.XPrecedente) ^ 2) + ((info.YCorrente - info.YPrecedente) ^ 2))
            info.Percorso += passo

            Dim distanzaDallInizio As Double = Math.Sqrt(((info.XCorrente - info.XIniziale) ^ 2) + ((info.YCorrente - info.YIniziale) ^ 2))
            If distanzaDallInizio > info.DistanzaMassima Then info.DistanzaMassima = distanzaDallInizio
            If info.ContaNelGesto AndAlso info.DistanzaMassima > spostamentoMassimoDito Then spostamentoMassimoDito = info.DistanzaMassima
        Catch ex As Exception
            log.ScriviLog($"AggiornaCampione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Ripercorre i campioni che Windows ha fuso in un solo messaggio. Durante uno scorrimento
    ''' rapido il sistema consegna un messaggio ogni parecchi campioni, e quelli intermedi
    ''' andavano perduti: percorso, rettilineita' e velocita' risultavano misurati su una frazione
    ''' del movimento reale. I campioni piu' vecchi di quello gia' elaborato vengono ignorati,
    ''' cosi' nessun tratto viene contato due volte.
    ''' </summary>
    Private Function ElaboraStorico(idPuntatore As UInteger, info As InfoPuntatore) As Boolean
        Try
            If info Is Nothing Then Return False

            Dim quantita As UInteger = 0
            If Not GetPointerInfoHistoryConteggio(idPuntatore, quantita, IntPtr.Zero) Then Return False
            If quantita <= 1UI Then Return False
            If quantita > CUInt(MassimoCampioniStorico) Then quantita = CUInt(MassimoCampioniStorico)

            Dim campioni As POINTER_INFO() = New POINTER_INFO(CInt(quantita) - 1) {}
            Dim disponibili As UInteger = quantita
            If Not GetPointerInfoHistory(idPuntatore, disponibili, campioni) Then Return False
            If disponibili <= 1UI Then Return False
            If disponibili > quantita Then disponibili = quantita

            Dim elaborati As Boolean = False

            For indice As Integer = CInt(disponibili) - 1 To 0 Step -1
                Dim campione As POINTER_INFO = campioni(indice)
                Dim tempo As Double = TempoCampione(campione.PerformanceCount)
                If tempo <= info.TempoUltimoCampione Then Continue For

                Dim posizione As PointF = PosizioneFine(campione)
                AggiornaCampione(info, posizione.X, posizione.Y, tempo)
                AggiornaCentroide(True, tempo)
                AggiornaDistanzaFraDita()
                elaborati = True
            Next

            Return elaborati
        Catch ex As Exception
            log.ScriviLog($"ElaboraStorico: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function PosizioneDalMessaggio(idPuntatore As UInteger, lParam As IntPtr, ByRef tempo As Double) As PointF
        Try
            Dim informazioni As New POINTER_INFO()
            If LeggiInfoPuntatore(idPuntatore, informazioni) Then
                tempo = TempoCampione(informazioni.PerformanceCount)
                Return PosizioneFine(informazioni)
            End If

            tempo = OraMs()
            Return New PointF(EstraiCoordinataX(lParam), EstraiCoordinataY(lParam))
        Catch ex As Exception
            log.ScriviLog($"PosizioneDalMessaggio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            tempo = OraMs()
            Return New PointF(EstraiCoordinataX(lParam), EstraiCoordinataY(lParam))
        End Try
    End Function

    Private Sub GestisciPointerDown(idPuntatore As UInteger, lParam As IntPtr)
        Try
            ScartaPuntatoriScaduti()

            Dim tempo As Double = 0
            Dim posizione As PointF = PosizioneDalMessaggio(idPuntatore, lParam, tempo)
            Dim ora As Double = OraMs()

            Dim ditaPrecedenti As Integer = ContaDitaNelGesto()
            Dim nuovoGesto As Boolean = (ditaPrecedenti = 0)

            If nuovoGesto Then
                puntiPerMillimetroGesto = PuntiPerMillimetroDelPunto(posizione.X, posizione.Y)
            End If

            Dim latoContatto As Double = 0
            If Not ContattoAccettabile(idPuntatore, latoContatto) Then
                If Not nuovoGesto Then gestoAnnullato = True
                puntatoriDiTocco.Remove(idPuntatore)
                Return
            End If

            Dim info As New InfoPuntatore()
            info.XIniziale = posizione.X
            info.YIniziale = posizione.Y
            info.XCorrente = posizione.X
            info.YCorrente = posizione.Y
            info.XPrecedente = posizione.X
            info.YPrecedente = posizione.Y
            info.OraInizio = ora
            info.OraUltimaNotizia = ora
            info.TempoUltimoCampione = tempo
            info.GestitoComeHold = False
            info.EPrimario = False
            info.ClassificatoComeEsplorazione = False
            info.LatoContatto = latoContatto

            Dim informazioniPuntatore As New POINTER_INFO()
            If LeggiInfoPuntatore(idPuntatore, informazioniPuntatore) Then
                info.EPrimario = (informazioniPuntatore.pointerFlags And POINTER_FLAG_PRIMARY) <> 0UI
            ElseIf nuovoGesto Then
                info.EPrimario = True
            End If

            ' Un dito che si appoggia molto dopo l'inizio del gesto, tipicamente il pollice che
            ' tiene il tablet, non deve trasformare un flick a un dito in un gesto a due o tre
            ' dita. Entra nel conteggio solo se arriva entro la finestra di simultaneita', e resta
            ' comunque seguito perche' il suo rilascio va riconosciuto.
            info.ContaNelGesto = nuovoGesto OrElse
                                 ((ora - oraInizioGesto) <= FinestraSimultaneitaMs AndAlso ditaPrecedenti < massimoDitaSupportate)

            puntatoriAttivi(idPuntatore) = info

            If nuovoGesto Then
                gestoGiaClassificatoComeScorrimento = False
                xInizioGestoCorrente = posizione.X
                yInizioGestoCorrente = posizione.Y
                gestoCorrenteSuTastieraTouch = PuntoSuTastieraTouch(posizione.X, posizione.Y)
                AzzeraMisureGesto(ora)
                esplorazioneMaiRiferita = True
                numeroMassimoDitaGestoCorrente = 0
            End If

            If info.ContaNelGesto Then
                numeroMassimoDitaGestoCorrente = Math.Max(numeroMassimoDitaGestoCorrente, ContaDitaNelGesto())
            End If

            AggiornaCentroide(False, tempo)
            AggiornaDistanzaFraDita()

            Dim ditaAttuali As Integer = ContaDitaNelGesto()

            If ditaAttuali = 1 AndAlso info.ContaNelGesto Then
                ' Non si riferisce nulla all'appoggio del dito sul documento. Farlo spostava il
                ' fuoco sulla cella toccata prima ancora che il gesto iniziasse, cosi' ogni flick
                ' ripartiva da li' invece che dalla posizione corrente. Sulla tastiera a schermo
                ' invece il tasto va selezionato gia' al pointer-down: i messaggi di tocco non
                ' diventano MouseDown, e un tap piu' breve della soglia di annuncio altrimenti
                ' arriverebbe al pointer-up senza tasto da confermare.
                timerEsplorazione.Stop()
                timerEsplorazione.Interval = IntervalloVerificaEsplorazioneMs
                timerEsplorazione.Start()
                If GestoIniziatoSuTastieraTouch() Then
                    RiferisciEsplorazione(posizione.X, posizione.Y)
                End If

                If info.EPrimario Then
                    AvviaHoldPrimario(idPuntatore)
                End If

                If inAttesaSecondoTap Then
                    Dim distanza As Double = Math.Sqrt(((posizione.X - ultimoTapX) ^ 2) + ((posizione.Y - ultimoTapY) ^ 2))
                    Dim tempoTrascorso As Double = ora - ultimoTapOra
                    If tempoTrascorso <= SogliaDoppioTapMs AndAlso distanza <= SogliaDoppioTapPixel Then
                        pointerIdSecondoTap = idPuntatore
                        inAttesaSecondoTap = False
                        timerAttesaHold.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
                        timerAttesaHold.Start()
                    End If
                End If
            ElseIf ditaAttuali >= 2 Then
                timerEsplorazione.Stop()
                If timerAttesaHold.Enabled Then timerAttesaHold.Stop()
                AzzeraHoldPrimario()
            End If

            If ditaAttuali = 2 AndAlso info.ContaNelGesto Then FermaVoce()
        Catch ex As Exception
            log.ScriviLog($"GestisciPointerDown: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub GestisciPointerUpdate(idPuntatore As UInteger, lParam As IntPtr)
        Try
            If Not puntatoriAttivi.ContainsKey(idPuntatore) Then Exit Sub

            Dim info As InfoPuntatore = puntatoriAttivi(idPuntatore)

            Dim informazioni As New POINTER_INFO()
            If LeggiInfoPuntatore(idPuntatore, informazioni) Then
                If (informazioni.pointerFlags And POINTER_FLAG_CANCELED) <> 0UI Then
                    ScartaPuntatore(idPuntatore, True)
                    Exit Sub
                End If
                ' UPDATE senza contatto e' il dito che si stacca, non un contatto da buttare:
                ' scartarlo qui faceva arrivare POINTERUP a elenco vuoto e la lettera non
                ' veniva confermata.
                If (informazioni.pointerFlags And POINTER_FLAG_INCONTACT) = 0UI OrElse
                   (informazioni.pointerFlags And POINTER_FLAG_UP) <> 0UI Then
                    GestisciPointerUp(idPuntatore, lParam)
                    Exit Sub
                End If
            End If

            Dim storicoElaborato As Boolean = ElaboraStorico(idPuntatore, info)

            If Not storicoElaborato Then
                Dim tempo As Double = 0
                Dim posizione As PointF = PosizioneDalMessaggio(idPuntatore, lParam, tempo)
                AggiornaCampione(info, posizione.X, posizione.Y, tempo)
                AggiornaCentroide(True, tempo)
                AggiornaDistanzaFraDita()
            End If

            If timerAttesaHold.Enabled AndAlso idPuntatore = pointerIdSecondoTap Then
                If info.DistanzaMassima > SogliaTapPixel Then timerAttesaHold.Stop()
            End If

            If info.EPrimario AndAlso Not info.ClassificatoComeEsplorazione AndAlso info.DistanzaMassima > SogliaMovimentoHoldPixel Then
                info.ClassificatoComeEsplorazione = True
                inEsplorazioneInterno = True
                If timerHoldPrimario.Enabled Then timerHoldPrimario.Stop()
            End If

            ' L'esplorazione tattile riguarda il dito singolo: con piu' dita appoggiate si sta
            ' facendo un gesto, non esplorando cosa c'e' sotto il dito.
            If ContaDitaNelGesto() = 1 AndAlso info.ContaNelGesto Then
                Dim ora As Double = OraMs()

                ' La distinzione la fa la sola durata: uno scorrimento rapido si esaurisce ben
                ' prima della soglia, mentre un dito ancora appoggiato dopo quel tempo sta
                ' esplorando. Richiedere anche che il movimento sia lento era sbagliato: chi fa
                ' scorrere il dito sulla tastiera a velocita' normale non soddisfaceva mai quella
                ' condizione, nessun tasto veniva annunciato e sembrava che fra un tasto e l'altro
                ' ci fosse il vuoto.
                If Not gestoGiaClassificatoComeScorrimento Then
                    If (ora - oraInizioGesto) >= SogliaClassificazioneEsplorazioneMs Then
                        gestoGiaClassificatoComeScorrimento = True
                    End If
                End If

                ValutaEsplorazione(info.XCorrente, info.YCorrente, ora)
            End If
        Catch ex As Exception
            log.ScriviLog($"GestisciPointerUpdate: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub GestisciPointerUp(idPuntatore As UInteger, lParam As IntPtr)
        Try
            If Not puntatoriAttivi.ContainsKey(idPuntatore) Then Exit Sub

            Dim info As InfoPuntatore = puntatoriAttivi(idPuntatore)

            Dim tempo As Double = 0
            Dim posizione As PointF = PosizioneDalMessaggio(idPuntatore, lParam, tempo)
            Dim posizioneValida As Boolean = (lParam <> IntPtr.Zero) OrElse (posizione.X <> 0.0F OrElse posizione.Y <> 0.0F)

            If Not info.GestitoComeHold Then
                If Not ElaboraStorico(idPuntatore, info) Then
                    If posizioneValida Then
                        AggiornaCampione(info, posizione.X, posizione.Y, tempo)
                        AggiornaCentroide(True, tempo)
                        AggiornaDistanzaFraDita()
                    End If
                End If
            End If

            If idPuntatore = pointerIdHoldPrimario Then AzzeraHoldPrimario()
            puntatoriAttivi.Remove(idPuntatore)
            puntatoriDiTocco.Remove(idPuntatore)

            ConcludiContattoSollevato(idPuntatore, info, tempo)
        Catch ex As Exception
            log.ScriviLog($"GestisciPointerUp: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' LEAVE e CAPTURECHANGED non sono un annullamento se il dito si e' semplicemente staccato.
    ''' Solo CANCELED interrompe il gesto senza confermare la lettera. CAPTURECHANGED sul
    ''' distacco va trattato come pointer-up, altrimenti la lettera trovata in esplorazione
    ''' non viene scritta.
    ''' </summary>
    Private Sub GestisciFineContatto(idPuntatore As UInteger, lParam As IntPtr, eCatturaPersa As Boolean)
        Try
            If Not puntatoriAttivi.ContainsKey(idPuntatore) Then
                puntatoriDiTocco.Remove(idPuntatore)
                Return
            End If

            Dim informazioni As New POINTER_INFO()
            If LeggiInfoPuntatore(idPuntatore, informazioni) Then
                If (informazioni.pointerFlags And POINTER_FLAG_CANCELED) <> 0UI Then
                    ScartaPuntatore(idPuntatore, True)
                    Return
                End If
                If (informazioni.pointerFlags And POINTER_FLAG_INCONTACT) <> 0UI AndAlso Not eCatturaPersa Then
                    Return
                End If
            End If

            GestisciPointerUp(idPuntatore, lParam)
        Catch ex As Exception
            log.ScriviLog($"GestisciFineContatto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Chiude il contatto gia' rimosso da puntatoriAttivi. Con un hold sulla tastiera si
    ''' conferma comunque la lettera: se il menu accenti non e' partito, alzare il dito deve
    ''' scrivere; se e' partito, la tastiera ignora la conferma.
    ''' </summary>
    Private Sub ConcludiContattoSollevato(idPuntatore As UInteger, info As InfoPuntatore, tempo As Double)
        Try
            If info Is Nothing Then Return

            If ContaDitaNelGesto() > 0 Then
                AggiornaCentroide(False, tempo)
                AggiornaDistanzaFraDita()
                Return
            End If

            timerEsplorazione.Stop()
            If idPuntatore = pointerIdHoldPrimario Then AzzeraHoldPrimario()
            inEsplorazioneInterno = False

            If Not info.ContaNelGesto Then
                numeroMassimoDitaGestoCorrente = 0
                numeroDitaCentroide = 0
                Return
            End If

            If info.GestitoComeHold Then
                numeroMassimoDitaGestoCorrente = 0
                numeroDitaCentroide = 0
                gestoGiaClassificatoComeScorrimento = False
                ConfermaEsplorazioneSeSuTastiera(info.XCorrente, info.YCorrente)
                Return
            End If

            ConcludiGesto(idPuntatore, info.XCorrente, info.YCorrente, tempo)
        Catch ex As Exception
            log.ScriviLog($"ConcludiContattoSollevato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function GestoRiguardaTastieraTouch(x As Double, y As Double) As Boolean
        Try
            If gestoCorrenteSuTastieraTouch Then Return True
            If PuntoSuTastieraTouch(xInizioGestoCorrente, yInizioGestoCorrente) Then Return True
            Return PuntoSuTastieraTouch(x, y)
        Catch ex As Exception
            log.ScriviLog($"GestoRiguardaTastieraTouch: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub ConfermaEsplorazioneSeSuTastiera(x As Double, y As Double)
        Try
            Dim suTastiera As Boolean = GestoRiguardaTastieraTouch(x, y)
            ultimoGestoConclusoSuTastieraTouch = suTastiera
            If Not suTastiera Then Return
            Solleva(EsplorazioneTerminataEvent, CInt(Math.Round(x)), CInt(Math.Round(y)))
        Catch ex As Exception
            log.ScriviLog($"ConfermaEsplorazioneSeSuTastiera: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function DirezionePrevalente(deltaX As Double, deltaY As Double) As DirezioneGesto
        Try
            Dim assolutoX As Double = Math.Abs(deltaX)
            Dim assolutoY As Double = Math.Abs(deltaY)
            Dim dominanza As Double = Math.Max(1.0, DominanzaAsse)

            If assolutoX >= (assolutoY * dominanza) Then
                If deltaX > 0 Then Return DirezioneGesto.Destra
                Return DirezioneGesto.Sinistra
            End If

            If assolutoY >= (assolutoX * dominanza) Then
                If deltaY < 0 Then Return DirezioneGesto.Su
                Return DirezioneGesto.Giu
            End If

            Return DirezioneGesto.Nessuna
        Catch ex As Exception
            log.ScriviLog($"DirezionePrevalente: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return DirezioneGesto.Nessuna
        End Try
    End Function

    ''' <summary>
    ''' Classifica il gesto appena concluso. Il tap richiede che nessuna delle misure di
    ''' escursione sia superata, non solo lo spostamento finale: un dito che esce e torna al
    ''' punto di partenza valeva come tap, e due dita che si stringono lasciano il centroide
    ''' fermo, quindi un pizzico risultava un tocco a due dita. Il flick chiede direzione
    ''' dominante e traiettoria rettilinea, cosi' un gesto obliquo o tortuoso non viene tirato
    ''' per forza su un asse; la velocita' misurata sulla fine del movimento permette anche il
    ''' flick lento ma deciso di chi ha poca mobilita' nella mano, purche' il dito sia ancora in
    ''' corsa quando si stacca.
    ''' </summary>
    Private Sub ConcludiGesto(idPuntatore As UInteger, x As Double, y As Double, tempo As Double)
        Try
            Dim numeroDita As Integer = numeroMassimoDitaGestoCorrente
            Dim eraScorrimento As Boolean = gestoGiaClassificatoComeScorrimento
            Dim annullato As Boolean = gestoAnnullato

            numeroMassimoDitaGestoCorrente = 0
            numeroDitaCentroide = 0
            gestoGiaClassificatoComeScorrimento = False

            Dim deltaX As Double = spostamentoGestoX
            Dim deltaY As Double = spostamentoGestoY
            Dim distanzaNetta As Double = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY))
            Dim durataGesto As Double = tempo - oraInizioGesto
            If durataGesto < 0 Then durataGesto = OraMs() - oraInizioGesto

            Dim rettilineita As Double = 1.0
            If percorsoGesto > 0 Then rettilineita = distanzaNetta / percorsoGesto

            Dim velocitaFinale As Double = VelocitaFinestra()
            Dim direzione As DirezioneGesto = DirezionePrevalente(deltaX, deltaY)

            Dim velocitaAmmessa As Boolean
            If eraScorrimento Then
                Dim spostamentoFinale As Double = SpostamentoFinestraFinale()
                Dim ancoraInCorsa As Boolean = (velocitaMassimaGesto <= 0) OrElse
                                               (velocitaFinale >= (velocitaMassimaGesto * RapportoVelocitaFinaleFlickLento))
                velocitaAmmessa = (rettilineita >= RettilineitaMinimaFlickLento) AndAlso
                                  (spostamentoFinale >= SogliaEsplorazionePixel) AndAlso
                                  ancoraInCorsa
            Else
                velocitaAmmessa = (durataGesto <= TempoMassimoFlickMs) OrElse (velocitaFinale >= VelocitaMinimaFlickMmMs)
            End If

            Dim eFlickGesto As Boolean = (Not annullato) AndAlso
                                         (direzione <> DirezioneGesto.Nessuna) AndAlso
                                         (distanzaNetta >= SogliaFlickPixel) AndAlso
                                         (rettilineita >= RettilineitaMinima) AndAlso
                                         velocitaAmmessa

            Dim eTapGesto As Boolean = (Not annullato) AndAlso
                                       (distanzaMassimaGesto <= SogliaTapPixel) AndAlso
                                       (percorsoGesto <= (SogliaTapPixel * RapportoPercorsoTap)) AndAlso
                                       (spostamentoMassimoDito <= SogliaTapPixel) AndAlso
                                       (variazioneMassimaFraDita <= SogliaVariazioneFraDitaPixel)

            If annullato Then
                inAttesaSecondoTap = False
                inAttesaSecondoTapDueDita = False
                If timerAttesaHold.Enabled Then timerAttesaHold.Stop()
                ' Un CAPTURECHANGED sul distacco arrivava come annullamento: sulla tastiera
                ' alzare il dito deve comunque confermare la lettera trovata.
                If numeroDita = 1 Then ConfermaEsplorazioneSeSuTastiera(x, y)
                Exit Sub
            End If

            If numeroDita = 1 Then
                Dim suTastiera As Boolean = GestoRiguardaTastieraTouch(x, y)
                If timerAttesaHold.Enabled AndAlso idPuntatore = pointerIdSecondoTap Then
                    suTastiera = suTastiera OrElse ultimoGestoConclusoSuTastieraTouch
                End If
                ultimoGestoConclusoSuTastieraTouch = suTastiera

                ' Sulla tastiera alzare il dito conferma sempre il tasto, anche se il distacco
                ' viene classificato come flick: altrimenti uno scarto di pochi pixel non scriveva
                ' la lettera. Fuori dalla tastiera il flick non deve digitare.
                If suTastiera OrElse Not eFlickGesto Then
                    Solleva(EsplorazioneTerminataEvent, CInt(Math.Round(x)), CInt(Math.Round(y)))
                End If

                If timerAttesaHold.Enabled AndAlso idPuntatore = pointerIdSecondoTap Then
                    timerAttesaHold.Stop()
                    inAttesaSecondoTap = False
                    Solleva(TapDoppioEvent)
                    Exit Sub
                End If

                If suTastiera Then
                    If eTapGesto Then
                        ultimoTapX = x
                        ultimoTapY = y
                        ultimoTapOra = OraMs()
                        inAttesaSecondoTap = True
                        Solleva(TapSingoloEvent)
                    Else
                        inAttesaSecondoTap = False
                    End If
                ElseIf eFlickGesto Then
                    inAttesaSecondoTap = False
                    Dim nelDocumento As Boolean = GestoNellaZonaDocumento()

                    Select Case direzione
                        Case DirezioneGesto.Destra
                            If nelDocumento Then
                                Solleva(FlickAvantiDocumentoEvent)
                            Else
                                Solleva(FlickAvantiEvent)
                            End If
                        Case DirezioneGesto.Sinistra
                            If nelDocumento Then
                                Solleva(FlickIndietroDocumentoEvent)
                            Else
                                Solleva(FlickIndietroEvent)
                            End If
                        Case DirezioneGesto.Su
                            Solleva(FlickSuEvent)
                        Case DirezioneGesto.Giu
                            Solleva(FlickGiuEvent)
                    End Select
                ElseIf eTapGesto Then
                    ultimoTapX = x
                    ultimoTapY = y
                    ultimoTapOra = OraMs()
                    inAttesaSecondoTap = True
                    Solleva(TapSingoloEvent)
                Else
                    inAttesaSecondoTap = False
                End If
            ElseIf numeroDita = 2 Then
                If eTapGesto AndAlso durataGesto <= SogliaTapDueDitaMs Then
                    Dim tempoTrascorso As Double = OraMs() - ultimoTapDueDitaOra
                    If timerEscapeDueDita.Enabled Then
                        ' Terzo tocco: l'elenco delle applicazioni prende il posto dell'Esc.
                        timerEscapeDueDita.Stop()
                        inAttesaSecondoTapDueDita = False
                        Solleva(TriploTapDueDitaEvent)
                    ElseIf inAttesaSecondoTapDueDita AndAlso tempoTrascorso <= SogliaDoppioTapDueDitaMs Then
                        inAttesaSecondoTapDueDita = False
                        timerEscapeDueDita.Stop()
                        timerEscapeDueDita.Start()
                    Else
                        ultimoTapDueDitaOra = OraMs()
                        inAttesaSecondoTapDueDita = True
                    End If
                ElseIf eFlickGesto AndAlso direzione = DirezioneGesto.Giu Then
                    inAttesaSecondoTapDueDita = False
                    If GestoNellaZonaDocumento() Then
                        Solleva(RichiestaLetturaCompletaDocumentoEvent)
                    Else
                        Solleva(RichiestaLetturaCompletaEvent)
                    End If
                ElseIf eFlickGesto AndAlso direzione = DirezioneGesto.Su Then
                    inAttesaSecondoTapDueDita = False
                    Solleva(FlickDueDitaSuEvent)
                ElseIf eFlickGesto AndAlso (direzione = DirezioneGesto.Destra OrElse direzione = DirezioneGesto.Sinistra) Then
                    inAttesaSecondoTapDueDita = False
                    Solleva(FlickDueDitaLateraleEvent, direzione = DirezioneGesto.Destra)
                Else
                    inAttesaSecondoTapDueDita = False
                End If
            ElseIf numeroDita = 3 Then
                If eFlickGesto Then
                    If direzione = DirezioneGesto.Sinistra OrElse direzione = DirezioneGesto.Destra Then
                        Solleva(FlickTreDitaLateraleEvent, direzione = DirezioneGesto.Sinistra)
                        Exit Sub
                    End If
                    If direzione = DirezioneGesto.Giu OrElse direzione = DirezioneGesto.Su Then
                        Dim giu As Boolean = (direzione = DirezioneGesto.Giu)
                        ' Dalla barra in alto: sempre centro notifiche o centro di controllo.
                        If GestoPartitoDallaBarraInAlto() Then
                            Solleva(FlickTreDitaVerticaleEvent, giu)
                            Exit Sub
                        End If
                        ' Rotore, solo dove serve: dal basso verso l'alto (Su) la modalita' PRECEDENTE,
                        ' dall'alto verso il basso la successiva. Il browser davanti lo gestisce da se'.
                        If RotoreGestitoAltrove(giu) Then Exit Sub
                        If RotoreDelTestoServe() Then
                            CiclaModalitaNavigazione(giu)
                        Else
                            Solleva(FlickTreDitaVerticaleEvent, giu)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"ConcludiGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function RotoreDelTestoServe() As Boolean
        Try
            Dim verifica As Func(Of Boolean) = RotoreTestoServe
            Return verifica Is Nothing OrElse verifica.Invoke()
        Catch ex As Exception
            log.ScriviLog($"RotoreDelTestoServe: {ex.Message}")
            Return True
        End Try
    End Function

    ' Il gesto è partito dalla fascia in alto dello schermo (la barra con ora, Wi-Fi e batteria):
    ' il 12 per cento dell'altezza, almeno 60 punti.
    Private Function GestoPartitoDallaBarraInAlto() As Boolean
        Try
            Dim inizio As Point = PuntoInizioGesto
            Dim schermo As Rectangle = Screen.FromPoint(inizio).Bounds
            Dim fascia As Integer = Math.Max(60, CInt(schermo.Height * 0.12))
            Return inizio.Y - schermo.Top <= fascia
        Catch ex As Exception
            log.ScriviLog($"GestoPartitoDallaBarraInAlto: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub timerEscapeDueDita_Tick(sender As Object, e As EventArgs) Handles timerEscapeDueDita.Tick
        Try
            timerEscapeDueDita.Stop()
            Solleva(EscapeGestoEvent)
        Catch ex As Exception
            log.ScriviLog($"timerEscapeDueDita_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub timerAttesaHold_Tick(sender As Object, e As EventArgs) Handles timerAttesaHold.Tick
        Try
            timerAttesaHold.Stop()
            If gestoAnnullato Then Return
            If Not puntatoriAttivi.ContainsKey(pointerIdSecondoTap) Then Return

            Dim info As InfoPuntatore = puntatoriAttivi(pointerIdSecondoTap)
            If info.DistanzaMassima > SogliaTapPixel Then Return

            info.GestitoComeHold = True
            Solleva(TapDoppioProlungatoEvent)
        Catch ex As Exception
            log.ScriviLog($"timerAttesaHold_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaHoldPrimario(idPuntatore As UInteger)
        Try
            If timerHoldPrimario.Enabled Then timerHoldPrimario.Stop()
            pointerIdHoldPrimario = idPuntatore
            holdPrimarioEmesso = False
            inEsplorazioneInterno = False
            timerHoldPrimario.Interval = Math.Max(1, CInt(Math.Round(sogliaHoldMsInterna)))
            timerHoldPrimario.Start()
        Catch ex As Exception
            log.ScriviLog($"AvviaHoldPrimario: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AzzeraHoldPrimario()
        Try
            If timerHoldPrimario.Enabled Then timerHoldPrimario.Stop()
            pointerIdHoldPrimario = 0UI
            holdPrimarioEmesso = False
        Catch ex As Exception
            log.ScriviLog($"AzzeraHoldPrimario: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub timerHoldPrimario_Tick(sender As Object, e As EventArgs) Handles timerHoldPrimario.Tick
        Try
            timerHoldPrimario.Stop()
            If gestoAnnullato Then Return
            If holdPrimarioEmesso Then Return
            If inEsplorazioneInterno Then Return
            If Not puntatoriAttivi.ContainsKey(pointerIdHoldPrimario) Then Return

            Dim info As InfoPuntatore = puntatoriAttivi(pointerIdHoldPrimario)
            If info Is Nothing Then Return
            If Not info.EPrimario Then Return
            If info.ClassificatoComeEsplorazione Then Return
            If info.DistanzaMassima > SogliaMovimentoHoldPixel Then
                info.ClassificatoComeEsplorazione = True
                inEsplorazioneInterno = True
                Return
            End If

            info.GestitoComeHold = True
            holdPrimarioEmesso = True
            Solleva(PressioneProlungataEvent, CInt(Math.Round(info.XCorrente)), CInt(Math.Round(info.YCorrente)))
        Catch ex As Exception
            log.ScriviLog($"timerHoldPrimario_Tick: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AzzeraStatoGesto()
        Try
            puntatoriAttivi.Clear()
            puntatoriDiTocco.Clear()
            numeroMassimoDitaGestoCorrente = 0
            gestoGiaClassificatoComeScorrimento = False
            inAttesaSecondoTap = False
            inAttesaSecondoTapDueDita = False
            esplorazioneMaiRiferita = True
            gestoCorrenteSuTastieraTouch = False
            ultimoGestoConclusoSuTastieraTouch = False
            AzzeraMisureGesto(OraMs())
            If timerAttesaHold.Enabled Then timerAttesaHold.Stop()
            If timerEsplorazione.Enabled Then timerEsplorazione.Stop()
            AzzeraHoldPrimario()
            inEsplorazioneInterno = False
        Catch ex As Exception
            log.ScriviLog($"AzzeraStatoGesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ScartaPuntatore(idPuntatore As UInteger, annullaGesto As Boolean)
        Try
            If Not puntatoriAttivi.ContainsKey(idPuntatore) Then
                puntatoriDiTocco.Remove(idPuntatore)
                Return
            End If

            Dim info As InfoPuntatore = puntatoriAttivi(idPuntatore)
            Dim contavaNelGesto As Boolean = info.ContaNelGesto
            If idPuntatore = pointerIdHoldPrimario Then AzzeraHoldPrimario()
            puntatoriAttivi.Remove(idPuntatore)
            puntatoriDiTocco.Remove(idPuntatore)

            If annullaGesto AndAlso contavaNelGesto Then gestoAnnullato = True

            If Not annullaGesto Then
                ConcludiContattoSollevato(idPuntatore, info, OraMs())
                Return
            End If

            If ContaDitaNelGesto() = 0 Then
                numeroMassimoDitaGestoCorrente = 0
                numeroDitaCentroide = 0
                gestoGiaClassificatoComeScorrimento = False
                inAttesaSecondoTap = False
                If timerAttesaHold.Enabled Then timerAttesaHold.Stop()
                If timerEsplorazione.Enabled Then timerEsplorazione.Stop()
                AzzeraHoldPrimario()
                inEsplorazioneInterno = False
            Else
                AggiornaCentroide(False, OraMs())
                AggiornaDistanzaFraDita()
            End If
        Catch ex As Exception
            log.ScriviLog($"ScartaPuntatore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Toglie dall'elenco i contatti che non esistono piu'. Il segnale buono e' la bandiera di
    ''' contatto restituita dal sistema: un dito ancora appoggiato la porta sempre, quindi la sua
    ''' assenza dice che il contatto e' finito e il messaggio di rilascio non e' arrivato. I tempi
    ''' di silenzio restano come ultima difesa per i dispositivi che non rispondono
    ''' all'interrogazione.
    ''' </summary>
    Private Sub ScartaPuntatoriScaduti()
        Try
            If puntatoriAttivi.Count = 0 Then Return

            Dim ora As Double = OraMs()
            Dim daRimuovere As New List(Of UInteger)
            Dim annullare As Boolean = False

            Dim daConcludere As New List(Of UInteger)

            For Each coppia As KeyValuePair(Of UInteger, InfoPuntatore) In puntatoriAttivi
                Dim informazioni As New POINTER_INFO()
                If LeggiInfoPuntatore(coppia.Key, informazioni) Then
                    If (informazioni.pointerFlags And POINTER_FLAG_CANCELED) <> 0UI Then
                        daRimuovere.Add(coppia.Key)
                        If coppia.Value.ContaNelGesto Then annullare = True
                        Continue For
                    End If
                    If (informazioni.pointerFlags And POINTER_FLAG_INCONTACT) = 0UI Then
                        daConcludere.Add(coppia.Key)
                        Continue For
                    End If
                    ' Si guarda solo da quanto tempo il contatto tace, mai da quanto e' appoggiato.
                    ' Scartare un puntatore dopo un tempo fisso dall'inizio toglieva di mezzo il dito
                    ' di chi esplora la tastiera con calma: superati gli otto secondi l'elenco restava
                    ' vuoto, il ramo a un dito non veniva piu' eseguito e l'esplorazione ammutoliva.
                    If (ora - coppia.Value.OraUltimaNotizia) > IntervalloTimeoutPuntatoreMs Then daConcludere.Add(coppia.Key)
                    Continue For
                End If

                If (ora - coppia.Value.OraUltimaNotizia) > IntervalloSilenzioPuntatoreMs Then daConcludere.Add(coppia.Key)
            Next

            If daRimuovere.Count = 0 AndAlso daConcludere.Count = 0 Then Return

            For Each chiave As UInteger In daRimuovere
                ScartaPuntatore(chiave, True)
            Next

            For Each chiave As UInteger In daConcludere
                GestisciPointerUp(chiave, IntPtr.Zero)
            Next

            If annullare Then gestoAnnullato = True
        Catch ex As Exception
            log.ScriviLog($"ScartaPuntatoriScaduti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Consegna un evento agli ascoltatori uno per uno e fuori dal filtro dei messaggi. Con
    ''' RaiseEvent un solo ascoltatore che lanciava un'eccezione interrompeva la catena e tutti
    ''' quelli dopo di lui non ricevevano il gesto; e il lavoro svolto dentro il filtro, per
    ''' esempio una finestra aperta o una lettura sincrona, tratteneva la pompa dei messaggi
    ''' mentre le dita erano ancora appoggiate, tanto che i rilasci arrivavano in ritardo o
    ''' andavano perduti.
    ''' </summary>
    Private Sub Solleva(gestore As [Delegate], ParamArray argomenti() As Object)
        Try
            If gestore Is Nothing Then Return

            Dim destinatari As [Delegate]() = gestore.GetInvocationList()
            If destinatari Is Nothing OrElse destinatari.Length = 0 Then Return

            Dim valori As Object() = argomenti
            If valori Is Nothing Then valori = New Object() {}

            Dim contesto As Threading.SynchronizationContext = contestoSincronizzazione
            If contesto Is Nothing Then
                ConsegnaEvento(destinatari, valori)
                Return
            End If

            contesto.Post(Sub(stato As Object) ConsegnaEvento(destinatari, valori), Nothing)
        Catch ex As Exception
            log.ScriviLog($"Solleva: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ConsegnaEvento(destinatari As [Delegate](), argomenti As Object())
        Try
            For Each destinatario As [Delegate] In destinatari
                Try
                    If destinatario Is Nothing Then Continue For

                    Dim bersaglio As Control = TryCast(destinatario.Target, Control)
                    If bersaglio IsNot Nothing AndAlso bersaglio.IsDisposed Then Continue For

                    destinatario.DynamicInvoke(argomenti)
                Catch exDestinatario As Exception
                    log.ScriviLog($"ConsegnaEvento (ascoltatore): {exDestinatario.Message}{Environment.NewLine}{exDestinatario.StackTrace}")
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog($"ConsegnaEvento: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function EstraiCoordinataX(lParam As IntPtr) As Integer
        Try
            Dim valoreCompleto As UInteger = CUInt(lParam.ToInt64() And &HFFFFFFFFL)
            Return ConvertiA16BitConSegno(CUShort(valoreCompleto And &HFFFFUI))
        Catch ex As Exception
            log.ScriviLog($"EstraiCoordinataX: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Function EstraiCoordinataY(lParam As IntPtr) As Integer
        Try
            Dim valoreCompleto As UInteger = CUInt(lParam.ToInt64() And &HFFFFFFFFL)
            Return ConvertiA16BitConSegno(CUShort((valoreCompleto >> 16) And &HFFFFUI))
        Catch ex As Exception
            log.ScriviLog($"EstraiCoordinataY: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Function ConvertiA16BitConSegno(valore As UShort) As Short
        Try
            If valore > 32767 Then
                Return CShort(CInt(valore) - 65536)
            Else
                Return CShort(valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"ConvertiA16BitConSegno: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetPointerType(pointerId As UInteger, ByRef pointerType As UInteger) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetPointerInfo(pointerId As UInteger, ByRef pointerInfo As POINTER_INFO) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetPointerTouchInfo(pointerId As UInteger, ByRef touchInfo As POINTER_TOUCH_INFO) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetPointerInfoHistory", SetLastError:=True)>
    Private Shared Function GetPointerInfoHistory(pointerId As UInteger, ByRef entriesCount As UInteger, pointerInfo As POINTER_INFO()) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetPointerInfoHistory", SetLastError:=True)>
    Private Shared Function GetPointerInfoHistoryConteggio(pointerId As UInteger, ByRef entriesCount As UInteger, pointerInfo As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetPointerDeviceRects(device As IntPtr, ByRef pointerDeviceRect As RECT, ByRef displayRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function MonitorFromPoint(punto As PUNTOSCHERMO, dwFlags As UInteger) As IntPtr
    End Function

    <DllImport("shcore.dll")>
    Private Shared Function GetDpiForMonitor(hmonitor As IntPtr, dpiType As Integer, ByRef dpiX As UInteger, ByRef dpiY As UInteger) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function SetProp(hWnd As IntPtr, lpString As String, hData As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function RemoveProp(hWnd As IntPtr, lpString As String) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, X As Integer, Y As Integer, cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function WindowFromPoint(punto As PUNTOSCHERMO) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsChild(hWndParent As IntPtr, hWnd As IntPtr) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PUNTOSCHERMO
        Public x As Integer
        Public y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINTER_INFO
        Public pointerType As UInteger
        Public pointerId As UInteger
        Public frameId As UInteger
        Public pointerFlags As UInteger
        Public sourceDevice As IntPtr
        Public hwndTarget As IntPtr
        Public ptPixelLocation As PUNTOSCHERMO
        Public ptHimetricLocation As PUNTOSCHERMO
        Public ptPixelLocationRaw As PUNTOSCHERMO
        Public ptHimetricLocationRaw As PUNTOSCHERMO
        Public dwTime As UInteger
        Public historyCount As UInteger
        Public InputData As Integer
        Public dwKeyStates As UInteger
        Public PerformanceCount As ULong
        Public ButtonChangeType As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINTER_TOUCH_INFO
        Public pointerInfo As POINTER_INFO
        Public touchFlags As UInteger
        Public touchMask As UInteger
        Public rcContact As RECT
        Public rcContactRaw As RECT
        Public orientation As UInteger
        Public pressure As UInteger
    End Structure

    <DllImport("shell32.dll", SetLastError:=True)>
    Private Shared Function SHGetPropertyStoreForWindow(hwnd As IntPtr, ByRef riid As Guid, <MarshalAs(UnmanagedType.Interface)> ByRef ppv As Object) As Integer
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PROPERTYKEY
        Public fmtid As Guid
        Public pid As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PROPVARIANT
        Public vt As UShort
        Public riservato1 As UShort
        Public riservato2 As UShort
        Public riservato3 As UShort
        Public valoreBooleano As Short
        Public riempimento1 As Short
        Public riempimento2 As Integer
        Public riempimento3 As Long
    End Structure

    <ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPropertyStore
        Sub GetCount(<Out> ByRef numeroProprieta As UInteger)
        Sub GetAt(indice As UInteger, ByRef chiave As PROPERTYKEY)
        Sub GetValue(ByRef chiave As PROPERTYKEY, ByRef valore As PROPVARIANT)
        Sub SetValue(ByRef chiave As PROPERTYKEY, ByRef valore As PROPVARIANT)
        Sub Commit()
    End Interface

End Class

''' <summary>
''' Implementata dai controlli che disegnano piu' elementi al proprio interno, come la
''' tastiera touch o una lista di voci. Restituisce un identificativo stabile dell'elemento
''' che si trova sotto il punto indicato, oppure Nothing se in quel punto non c'e' niente da
''' annunciare. WGTouk lo interroga tramite IdentificaElemento per annunciare al cambio di
''' elemento invece che a ogni millimetro percorso dal dito.
''' </summary>
Public Interface IWGElementoTattile
    Function IdentificativoElementoTattile(puntoSchermo As Point) As String
End Interface