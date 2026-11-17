Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports UIAutomationClient

' Nucleo principale del lettore di schermo, con la gestione del ciclo di lavoro, degli eventi di automazione, della navigazione e
' della configurazione delle opzioni di lettura.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const ClsidAutomazione8 As String = "e22ad333-b25f-460c-83d0-0581107395c9"
    Private Const ClsidAutomazione As String = "ff48dba4-60ef-4201-aa87-54103eef594e"
    Private Const AttesaCursore As Integer = 30
    Private Const LunghezzaMassimaLettura As Integer = 100000
    Private Const LunghezzaMassimaSelezione As Integer = 300
    Private Const MassimoTestiFinestra As Integer = 60
    Private Const TempoMassimoUia As UInteger = 1000UI
    Private Const EventoSelezioneElemento As Integer = 20012
    Private Const EventoFinestraAperta As Integer = 20016
    Private Const EventoAllertaSistema As Integer = 20023
    Private Const EventoRegioneViva As Integer = 20024
    Private Const EventoProprietaCambiata As Integer = 20027

    Private Shared ReadOnly logShared As New Logger()
    Private Shared ReadOnly _bloccoIstanza As New Object()
    Private Shared _istanza As WGLettoreSchermo

    Private ReadOnly log As New Logger()
    Private ReadOnly _bloccoAvvio As New Object()
    Private _coda As BlockingCollection(Of EventoLettore)
    Private ReadOnly _ultimiPerChiave As New System.Collections.Concurrent.ConcurrentDictionary(Of String, EventoLettore)(StringComparer.Ordinal)
    Private _threadLavoro As Thread
    Private _operazioni As CodaOperazioni
    Private _operazioniCommutatore As CodaOperazioni
    Private _operazioniPeriodiche As CodaOperazioni
    Private ReadOnly _bloccoEsecuzione As New Object()
    Private _threadSfondo As Thread
    Private ReadOnly _bloccoRegioniVive As New Object()
    Private _riavvioRichiesto As Integer
    Private _annunciaPosizioneElenchi As Integer = 1
    Private _agganci As AgganciLettore
    Private _avvioCompletato As ManualResetEventSlim
    Private _automazione As IUIAutomation
    Private _richiestaCache As IUIAutomationCacheRequest
    Private _gestoreFocus As GestoreFocusUia
    Private _gestoreSelezione As GestoreSelezioneUia
    Private _gestoreAllerta As GestoreSelezioneUia
    Private _gestoreFinestre As GestoreSelezioneUia
    Private _leggiAvvisi As Integer = 1
    Private _ultimoAvviso As String = ""
    Private _ultimoAvvisoTick As Integer
    Private Shared ReadOnly _eseguibili As New ConcurrentDictionary(Of UInteger, String)()
    Private _ultimoCommutatore As String = ""
    Private _ultimoCommutatoreTick As Integer
    Private _ultimoCommutatoreDaTasto As Boolean
    Private _commutatoreInCoda As Integer
    Private _startUltimoControllo As Integer
    Private _ultimaSelezioneStart As String = ""
    Private _commutatoreUltimoTasto As Integer
    Private _commutatoreGenerazione As Integer
    Private _commutatoreRadiceFinestra As IntPtr
    Private _commutatoreRadice As IUIAutomationElement
    Private _ultimaVoceCommutatoreChiave As String = ""
    Private _commutatoreAnnunciato As Integer
    Private _commutatoreAttivitaTick As Integer
    Private _ultimoFocusArrivo As Integer
    Private _ultimoAnnuncioFinestraTick As Integer
    Private _titoliFinestra As HashSet(Of String)
    Private _ultimoCommutatoreNome As String = ""
    Private _fuocoDifferito As IUIAutomationElement
    Private _cursoreDaTasti As Integer
    Private _tabRifiutato As IUIAutomationElement
    Private _tabGenerazione As Integer
    Private _fuoriDalDocumento As Integer
    Private _ultimoBersaglioTab As IUIAutomationElement
    Private _ultimoBersaglioGenerazione As Integer
    Private _rientroAnnunciatoTick As Integer = Integer.MinValue
    Private _popupGecko As Boolean
    Private _letturaPaginaInAttesa As Boolean
    Private ReadOnly _bloccoProgresso As New Object()
    Private _progressoFinestra As IntPtr
    Private _progressoOggetto As Integer
    Private _progressoFiglio As Integer
    Private _progressoInCoda As Integer
    Private _bipUltimo As Integer = -1
    Private _bipTick As Integer = Integer.MinValue
    Private _statoFinestra As IntPtr
    Private _statoOggetto As Integer
    Private _statoFiglio As Integer
    Private _statoInCoda As Integer
    Private ReadOnly _pagineLette As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly _nomiDesktop As New HashSet(Of String)(StringComparer.CurrentCultureIgnoreCase)
    Private ReadOnly _bloccoNomiDesktop As New Object()
    Private _nomiDesktopTick As Integer = Integer.MinValue
    Private _ultimoFuocoChiesto As Integer
    Private _ultimaAttivazioneWeb As Integer = Integer.MinValue
    Private _sequenzaAlPrimoPiano As Integer
    Private _primoPianoTick As Integer
    Private _ultimoMenuMsaaNome As String = ""
    Private _ultimoMenuMsaaTick As Integer
    Private _descrittore As DescrittoreElementi
    Private _ultimoElemento As IUIAutomationElement
    Private _ultimoNome As String = ""
    Private _ultimaParteVariabile As String = ""
    Private _sequenzaFocus As Integer
    Private _epocaSelezione As Integer
    Private _ultimoInAlbero As Integer
    Private _richiestaCacheVive As IUIAutomationCacheRequest
    Private _richiestaCacheEventi As IUIAutomationCacheRequest
    Private _richiestaCacheMenu As IUIAutomationCacheRequest
    Private _richiestaCacheCommutatore As IUIAutomationCacheRequest
    Private _sequenzaTastoNav As Integer
    Private Structure VoceNomeApplicazione
        Public Nome As String
        Public Tick As Integer
    End Structure
    Private Shared ReadOnly _nomiApplicazioneFinestra As New ConcurrentDictionary(Of IntPtr, VoceNomeApplicazione)()
    Private _walkerFinestre As IUIAutomationTreeWalker
    Private _richiestaCacheManiglia As IUIAutomationCacheRequest
    Private ReadOnly _regioniVivePendenti As New Queue(Of EventoLettore)()
    Private _ultimaElaboraRegioneViva As Integer
    Private _ultimoMovimentoVirtuale As Integer
    Private _sincronizzaFuocoElemento As IUIAutomationElement
    Private _cambioValoreInCoda As Integer
    Private _inEsecuzione As Integer
    Private _generazione As Integer
    Private _generazioneLavoro As Integer
    Private _generazioneSfondo As Integer
    Private _sospeso As Integer
    Private _ecoCaratteri As Integer = 1
    Private _ecoParole As Integer = 1
    Private _leggiPassword As Integer
    Private _navigazioneWebAbilitata As Integer = 1
    Private _tastiVirtuali As Integer
    Private _escScrittura As Integer
    Private _silenzioValoreFino As Integer
    Private _silenzioSelezioneFino As Integer
    Private _selezioneInCoda As Integer
    Private _ultimaSelezioneEsterna As String = ""
    Private _uiaFerma As IstantaneaCampo
    Private _tastoModificatore As String = "entrambi"
    Private _ultimoAllineamentoTasto As String = ""
    Private _campi As LetturaCampi
    Private _navigazione As NavigazioneWeb
    Private _istantanea As IstantaneaCampo
    Private _revisione As IUIAutomationElement
    Private _navAttiva As Boolean
    Private _navManuale As Boolean
    Private _navDisattivataPer As String = ""
    Private _ultimoCostruisci As Integer
    Private _catena As HashSet(Of String)
    Private _walkerControllo As IUIAutomationTreeWalker
    Private _ultimoFocusTick As Integer
    Private _ultimoTastoAltTick As Integer
    Private ReadOnly _bloccoTitolo As New Object()
    Private _titoloFinestra As IntPtr
    Private _titoloScadenza As Integer
    Private _titoloInAttesa As Integer
    Private _ultimoTitoloPagina As String = ""
    Private _ultimoTitoloTick As Integer
    Private _leggiColonne As Integer = 1
    Private Shared ReadOnly _nomiProgramma As New ConcurrentDictionary(Of UInteger, String)()
    Private _ultimoFocusNav As IUIAutomationElement
    Private _gestoreRegioniVive As GestoreSelezioneUia
    Private _leggiRegioniVive As Integer = 1
    Private _annunciaModalita As Integer = 1
    Private _modoFocusAnnunciato As Boolean
    Private _navForzataElemento As IUIAutomationElement
    Private _proteggiCursoreFino As Integer
    Private _silenziaFocusDa As Integer
    Private _silenziaFocusFino As Integer
    Private _ultimoAnnuncioAlbero As String = ""
    Private _valoreCasellaDetto As String = ""
    Private _valoreCasellaTick As Integer
    Private ReadOnly _regioniVive As New Dictionary(Of String, List(Of String))(StringComparer.Ordinal)
    Private _ultimaRegioneViva As String = ""
    Private _ultimaRegioneVivaTick As Integer
    Private ReadOnly _bloccoMsaa As New Object()
    Private _msaaTick As Integer
    Private ReadOnly _codaVivaIa2 As New ConcurrentQueue(Of RichiestaVivaIa2)()
    Private _vivaIa2InCoda As Integer
    Private _menuClassico As Boolean
    Private _menuNativoAperto As Boolean
    Private _fineMenuInCoda As Integer
    Private _menuSistemaAttivo As Integer
    Private _menuSistemaTick As Integer = Integer.MinValue
    Private _menuSorveglianzaTick As Integer
    Private _menuFuoriConteggio As Integer
    Private _msaaFinestra As IntPtr
    Private _msaaOggetto As Integer
    Private _msaaFiglio As Integer
    Private _focusMsaaInCoda As Integer
    Private _ultimaFraseMsaa As String = ""
    Private _messaggioGiaAnnunciato As Boolean
    Private Shared ReadOnly _processiShell As New ConcurrentDictionary(Of UInteger, Boolean)()
    Private Structure VoceCacheFinestra
        Public Maniglia As IntPtr
        Public Tick As Integer
    End Structure
    Private Shared ReadOnly _cacheFinestrePerTitolo As New ConcurrentDictionary(Of String, VoceCacheFinestra)(StringComparer.CurrentCulture)
    Private Shared ReadOnly _titoliRicordati As New ConcurrentDictionary(Of IntPtr, String)()
    Private _parlaVivaInCoda As Integer
    Private _vivaFinestraTick As Integer
    Private _vivaConteggio As Integer

    Private NotInheritable Class RaccoltaAltTab
        Public ReadOnly Finestre As New List(Of IntPtr)()

        Public Function Raccogli(finestra As IntPtr, parametro As IntPtr) As Boolean
            Try
                If Finestre.Count >= 200 Then Return False
                If Not NativoLettore.IsWindowVisible(finestra) Then Return True
                Dim stileEsteso As Integer = NativoLettore.GetWindowLong(finestra, -20)
                Dim proprietario As IntPtr = NativoLettore.GetWindow(finestra, NativoLettore.GW_OWNER)
                If (stileEsteso And &H80) <> 0 AndAlso (stileEsteso And &H40000) = 0 Then Return True
                If proprietario <> IntPtr.Zero AndAlso (stileEsteso And &H40000) = 0 Then Return True
                Dim nascosta As Integer = 0
                Try
                    If NativoLettore.DwmGetWindowAttribute(finestra, 14, nascosta, 4) = 0 AndAlso nascosta <> 0 Then Return True
                Catch exDwm As Exception
                    logShared.ScriviLog("WGLettoreSchermo.RaccoltaAltTab.Raccogli (dwm): " & exDwm.Message)
                    nascosta = 0
                End Try
                Dim classe As New StringBuilder(128)
                NativoLettore.GetClassName(finestra, classe, classe.Capacity)
                Select Case classe.ToString()
                    Case "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "TaskSwitcherOverlayWnd", "#32771", "ForegroundStaging"
                        Return True
                End Select
                Finestre.Add(finestra)
            Catch ex As Exception
                logShared.ScriviLog("WGLettoreSchermo.RaccoltaAltTab.Raccogli: " & ex.Message)
            End Try
            Return True
        End Function
    End Class

    Private Sub New()
    End Sub

    Public Shared Function GetInstance() As WGLettoreSchermo
        Try
            SyncLock _bloccoIstanza
                If _istanza Is Nothing Then _istanza = New WGLettoreSchermo()
            End SyncLock
            Return _istanza
        Catch ex As Exception
            logShared.ScriviLog("WGLettoreSchermo.GetInstance: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public ReadOnly Property InEsecuzione As Boolean
        Get
            Try
                Return Volatile.Read(_inEsecuzione) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InEsecuzione: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Property Sospeso As Boolean
        Get
            Try
                Return Volatile.Read(_sospeso) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.Sospeso (lettura): " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_sospeso, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.Sospeso (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property LeggiPaginaAlCaricamento As Boolean = True

    Public Property BipAvanzamento As Boolean = True

    Public Property PausaConMaiuscolo As Boolean = True

    Private _lettereRapideSpente As Integer
    Private _ultimoComandoCarattereTick As Integer = Integer.MinValue

    Friend ReadOnly Property LettereRapideSpente As Boolean
        Get
            Return Volatile.Read(_lettereRapideSpente) = 1
        End Get
    End Property

    Public Property EcoCaratteri As Boolean
        Get
            Try
                Return Volatile.Read(_ecoCaratteri) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.EcoCaratteri (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_ecoCaratteri, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.EcoCaratteri (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property EcoParole As Boolean
        Get
            Try
                Return Volatile.Read(_ecoParole) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.EcoParole (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_ecoParole, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.EcoParole (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property LeggiPassword As Boolean
        Get
            Try
                Return Volatile.Read(_leggiPassword) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiPassword (lettura): " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_leggiPassword, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiPassword (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property NavigazioneWebAbilitata As Boolean
        Get
            Try
                Return Volatile.Read(_navigazioneWebAbilitata) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.NavigazioneWebAbilitata (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_navigazioneWebAbilitata, If(value, 1, 0))
                If Not value Then ImpostaModalita(False, False)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.NavigazioneWebAbilitata (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property AnnuncioRegioni As String
        Get
            Select Case NavigazioneWeb.ModoRegioni
                Case 0
                    Return "mai"
                Case 2
                    Return "sempre"
                Case 3
                    Return "separata"
                Case Else
                    Return "nuova"
            End Select
        End Get
        Set(value As String)
            Try
                Dim modo As String = If(value, "").Trim().ToLowerInvariant()
                If modo = "mai" Then
                    NavigazioneWeb.ModoRegioni = 0
                ElseIf modo = "sempre" Then
                    NavigazioneWeb.ModoRegioni = 2
                ElseIf modo = "separata" Then
                    NavigazioneWeb.ModoRegioni = 3
                Else
                    NavigazioneWeb.ModoRegioni = 1
                End If
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.AnnuncioRegioni (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property LivelloIntestazioni As Boolean
        Get
            Return DescrittoreElementi.DireLivelloTitoli
        End Get
        Set(value As Boolean)
            DescrittoreElementi.DireLivelloTitoli = value
        End Set
    End Property

    Public Property LinkPrimaDelNome As Boolean
        Get
            Return DescrittoreElementi.LinkPrimaDelNome
        End Get
        Set(value As Boolean)
            DescrittoreElementi.LinkPrimaDelNome = value
        End Set
    End Property

    Public Property LeggiColonne As Boolean
        Get
            Try
                Return Volatile.Read(_leggiColonne) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiColonne (lettura): " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_leggiColonne, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiColonne (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property LeggiAvvisi As Boolean
        Get
            Try
                Return Volatile.Read(_leggiAvvisi) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiAvvisi (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_leggiAvvisi, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiAvvisi (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property LeggiRegioniVive As Boolean
        Get
            Try
                Return Volatile.Read(_leggiRegioniVive) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiRegioniVive (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_leggiRegioniVive, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.LeggiRegioniVive (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property AnnunciaCambioModalita As Boolean
        Get
            Try
                Return Volatile.Read(_annunciaModalita) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.AnnunciaCambioModalita (lettura): " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Interlocked.Exchange(_annunciaModalita, If(value, 1, 0))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.AnnunciaCambioModalita (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Public Property NascondiMenuChiusi As Boolean
        Get
            Return NavigazioneWeb.NascondiMenuChiusi
        End Get
        Set(value As Boolean)
            NavigazioneWeb.NascondiMenuChiusi = value
        End Set
    End Property

    Public Property AnnunciaPosizioneElenchi As Boolean
        Get
            Return Volatile.Read(_annunciaPosizioneElenchi) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_annunciaPosizioneElenchi, If(value, 1, 0))
        End Set
    End Property

    Public Property NavigazioneCiclica As Boolean
        Get
            Return NavigazioneWeb.Ciclica
        End Get
        Set(value As Boolean)
            NavigazioneWeb.Ciclica = value
        End Set
    End Property

    Public Property TastoModificatore As String
        Get
            Try
                Dim valore As String = Volatile.Read(_tastoModificatore)
                If String.IsNullOrWhiteSpace(valore) Then Return "entrambi"
                Return valore
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.TastoModificatore (lettura): " & ex.Message)
                Return "entrambi"
            End Try
        End Get
        Set(value As String)
            Try
                Dim modo As String = If(value, "").Trim().ToLowerInvariant()
                If modo <> "insert" AndAlso modo <> "capslock" AndAlso modo <> "entrambi" Then modo = "entrambi"
                Volatile.Write(_tastoModificatore, modo)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.TastoModificatore (scrittura): " & ex.Message)
            End Try
        End Set
    End Property

    Friend ReadOnly Property TastiVirtuali As Boolean
        Get
            Try
                Return Volatile.Read(_tastiVirtuali) = 1 AndAlso Volatile.Read(_sospeso) = 0 AndAlso Volatile.Read(_inEsecuzione) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.TastiVirtuali: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Friend ReadOnly Property EscDaScrittura As Boolean
        Get
            Try
                Return Volatile.Read(_escScrittura) = 1 AndAlso Volatile.Read(_sospeso) = 0 AndAlso Volatile.Read(_inEsecuzione) = 1
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.EscDaScrittura: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Sub AllineaBlocMaiuscole()
        Try
            Dim modo As String = TastoModificatore
            If modo = "insert" Then
                _ultimoAllineamentoTasto = modo
                Return
            End If
            If _ultimoAllineamentoTasto = modo Then Return
            _ultimoAllineamentoTasto = modo
            If Not NativoLettore.BlocMaiuscoleAttivo() Then Return
            NativoLettore.keybd_event(CByte(NativoLettore.VK_CAPITAL), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(NativoLettore.VK_CAPITAL), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            Parla("Bloc maiuscole disattivato. Ora è un tasto dei comandi.", True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AllineaBlocMaiuscole: " & ex.Message)
        End Try
    End Sub

    Public Shared Function TestoComandi() As String
        Return "Il lettore di schermo è sperimentale. Legge le altre applicazioni, fra cui Chrome, Edge e Firefox, quando WinGabriel va in secondo piano. " &
            "I tasti dei comandi sono Insert e Bloc maiuscole. Si scelgono dalle impostazioni: solo Insert, solo Bloc maiuscole, oppure entrambi. Un doppio tocco invia il tasto all'applicazione. " &
            "Le frecce spostano. Con Shift si sente che cosa viene selezionato o deselezionato, nei campi normali e nei campi delle pagine web. Backspace e Canc annunciano la lettera, la parola o il testo cancellato. " &
            "Tasto dei comandi e freccia su: riga. Freccia giù: lettura fino in fondo. Shift e freccia su o giù, oppure S: legge la selezione. C: carattere. W: parola. Shift C: compita la parola. " &
            "Spazio entra o esce dalla navigazione dell'albero di accessibilità. Nella navigazione, H titoli, da 1 a 6 i livelli, K link, B pulsanti, E campi di scrittura, F campi, X caselle, R opzioni, C caselle combinate, L elenchi, I voci, T tabelle, G immagini, D regioni, N testo. Con Shift si torna indietro. Arrivati all'ultimo si ricomincia dal primo. Backspace e Canc, in navigazione, leggono il carattere accanto e non cancellano. " &
            "Control e freccia sinistra o destra: elemento accanto. Control Alt e frecce: celle della tabella, con intestazione, estensione, riga e colonna. Arrivando su una tabella si sentono le righe e le colonne. Y legge la riga con le intestazioni. J legge la colonna. Invio attiva l'elemento e, su un messaggio di posta, ne legge il testo: le frecce restano nel messaggio e, tornando indietro dall'inizio, si esce. Esc esce dalla scrittura e torna alla navigazione. " &
            "A, in navigazione, va all'articolo. M legge il contenuto aperto: il testo del riquadro di lettura accanto a un elenco, il paragrafo con il suo stile, la cella di un foglio di calcolo. " &
            "Tasto dei comandi con Control e le frecce percorre l'albero di accessibilità. D legge la descrizione. R aggiorna l'albero. T il titolo. Tab l'elemento con il fuoco. Fine la barra di stato. B il testo della finestra. F12 l'ora. F11 la data. Q sospende il lettore. F1 ripete questi comandi. Control C annuncia la copia. " &
            "Tasto dei comandi e 1: aiuto tasti, legge il gesto senza eseguirlo. F legge il formato del testo. Shift F passa alla revisione piatta. P mette in pausa o riprende la lettura continua. Freccia giù la avvia e, se è già in corso, la mette in pausa. O legge con il riconoscimento ottico. V attiva il Braille. Z legge l'elemento sotto il puntatore. 8 sposta il puntatore. 9 fa clic. 7 elenca gli elementi della pagina per tipo. Control F apre un campo di testo per cercare nella pagina. 0 cambia la prolissità. F10 scrive su file l'albero dell'elemento con il fuoco. " &
            "Control 0 salva prolissità ed echi attuali come profilo del programma in primo piano, Shift 0 lo toglie. " &
            "H ripete l'ultima frase detta, Shift H legge quella prima a ogni pressione, Control H la copia negli appunti. A legge il testo degli appunti. " &
            "Con il layout da tavolo il tastierino vale anche senza il tasto dei comandi: 4 e 6 l'elemento accanto, 8 il contenitore, 2 il contenuto, 5 il fuoco. I gesti si possono rimappare."
    End Function

    Public Sub Avvia()
        Try
            SyncLock _bloccoAvvio
                If Volatile.Read(_inEsecuzione) = 1 Then Return
                _ultimiPerChiave.Clear()
                _coda = New BlockingCollection(Of EventoLettore)()
                _avvioCompletato = New ManualResetEventSlim(False)
                Interlocked.Exchange(_inEsecuzione, 1)
                Dim generazione As Integer = Interlocked.Increment(_generazione)
                _generazioneLavoro = generazione
                _threadLavoro = New Thread(AddressOf CicloLavoro)
                _threadLavoro.IsBackground = True
                _threadLavoro.Name = "WGLettoreSchermo"
                _threadLavoro.SetApartmentState(ApartmentState.MTA)
                _threadLavoro.Start()
                _operazioni = New CodaOperazioni()
                _operazioni.BloccoEsecuzione = _bloccoEsecuzione
                _operazioni.Avvia()
                _operazioniCommutatore = New CodaOperazioni()
                _operazioniCommutatore.Avvia()
                _operazioniPeriodiche = New CodaOperazioni()
                _operazioniPeriodiche.BloccoEsecuzione = _bloccoEsecuzione
                _operazioniPeriodiche.Precedenza = _operazioni
                _operazioniPeriodiche.Avvia()
                AvviaRitardoTasti()
                _generazioneSfondo = generazione
                _threadSfondo = New Thread(AddressOf CicloSfondo)
                _threadSfondo.IsBackground = True
                _threadSfondo.Name = "WGLettoreSchermoSfondo"
                _threadSfondo.SetApartmentState(ApartmentState.MTA)
                _threadSfondo.Start()
                _agganci = New AgganciLettore(Me)
                _agganci.Avvia()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            SyncLock _bloccoAvvio
                If Volatile.Read(_inEsecuzione) = 0 Then Return
                Interlocked.Exchange(_inEsecuzione, 0)
                Dim agganci As AgganciLettore = _agganci
                Dim lavoro As Thread = _threadLavoro
                Dim operazioni As CodaOperazioni = _operazioni
                Dim sfondo As Thread = _threadSfondo
                If agganci IsNot Nothing Then agganci.Ferma()
                AiutoInProcesso.Istanza.Ferma()
                FermaRitardoTasti()
                _svegliaSfondo.Set()
                If _coda IsNot Nothing Then _coda.CompleteAdding()
                _agganci = Nothing
                _threadLavoro = Nothing
                _operazioni = Nothing
                Dim corsiaCommutatore As CodaOperazioni = _operazioniCommutatore
                Dim corsiaPeriodiche As CodaOperazioni = _operazioniPeriodiche
                _operazioniCommutatore = Nothing
                _operazioniPeriodiche = Nothing
                If corsiaCommutatore IsNot Nothing Then corsiaCommutatore.Ferma()
                If corsiaPeriodiche IsNot Nothing Then corsiaPeriodiche.Ferma()
                _threadSfondo = Nothing
                Dim arresto As New ArrestoThread(Me, agganci, lavoro, operazioni, sfondo)
                Dim attesa As New Thread(AddressOf arresto.Esegui)
                attesa.IsBackground = True
                attesa.Name = "WGLettoreSchermoArresto"
                attesa.SetApartmentState(ApartmentState.MTA)
                attesa.Start()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Sub AttendiArrestoThread(agganci As AgganciLettore, lavoro As Thread, operazioni As CodaOperazioni, sfondo As Thread)
        Try
            If agganci IsNot Nothing Then agganci.Attendi()
            If lavoro IsNot Nothing Then lavoro.Join(3000)
            If operazioni IsNot Nothing Then operazioni.Attendi()
            If sfondo IsNot Nothing Then sfondo.Join(1500)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttendiArrestoThread: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloLavoro()
        Dim miaGenerazione As Integer = _generazioneLavoro
        Try
            Dim pronto As Boolean = InizializzaAutomazione()
            _avvioCompletato.Set()
            If pronto Then
                While Volatile.Read(_inEsecuzione) = 1 AndAlso miaGenerazione = Volatile.Read(_generazione)
                    Dim evento As EventoLettore = Nothing
                    If _coda.TryTake(evento, 200) Then
                        ElaboraEvento(evento)
                    ElseIf Interlocked.Exchange(_riavvioRichiesto, 0) = 1 Then
                        RiavviaAutomazione("Explorer riavviato")
                    End If
                End While
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CicloLavoro: " & ex.Message)
            _avvioCompletato.Set()
        End Try
        If miaGenerazione = Volatile.Read(_generazione) Then RilasciaAutomazione()
    End Sub

    Private Sub CicloSfondo()
        Dim miaGenerazione As Integer = _generazioneSfondo
        Try
            While Volatile.Read(_inEsecuzione) = 1 AndAlso miaGenerazione = Volatile.Read(_generazione)
                _svegliaSfondo.WaitOne(IntervalloSfondo())
                If Volatile.Read(_inEsecuzione) = 0 Then Exit While
                If _automazione Is Nothing OrElse _descrittore Is Nothing Then Continue While
                ElaboraRegioniVivePendenti()
                ControllaTitolo()
                PuliziaOggettiCom()
                SorvegliaMenuStart()
                SorvegliaFuocoMenu()
                SorvegliaAltriLettori()
                If _watchdog IsNot Nothing AndAlso _watchdog.ExplorerRiavviato() Then Interlocked.Exchange(_riavvioRichiesto, 1)
                Dim operazioni As CodaOperazioni = _operazioniPeriodiche
                If operazioni IsNot Nothing Then
                    operazioni.Esegui("battito", AddressOf BattitoLettore, sostituisci:=True)
                    operazioni.Esegui("indice", AddressOf IndicizzaNavigazione, sostituisci:=True)
                    operazioni.Esegui("sincronizza", AddressOf SincronizzaPuntoPartenzaFuoco, sostituisci:=True)
                End If
            End While
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CicloSfondo: " & ex.Message)
        End Try
    End Sub

    Private Function InizializzaAutomazione() As Boolean
        Try
            _automazione = CreaAutomazione()
            If _automazione Is Nothing Then Throw New InvalidOperationException("UI Automation non disponibile")
            ImpostaTempiMassimi()
            InizializzaAutomazioneRapida()
            _descrittore = New DescrittoreElementi()
            _richiestaCache = _automazione.CreateCacheRequest()
            For Each idProprieta As Integer In DescrittoreElementi.ProprietaRichieste
                AggiungiProprieta(idProprieta)
            Next
            _richiestaCacheVive = _automazione.CreateCacheRequest()
            For Each idLeggera As Integer In New Integer() {DescrittoreElementi.ProprietaIdProcesso, DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ProprietaNome, DescrittoreElementi.ProprietaRuoloAria, DescrittoreElementi.ProprietaProprietaAria, DescrittoreElementi.ProprietaImpostazioneVita}
                Try
                    _richiestaCacheVive.AddProperty(idLeggera)
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.InizializzaAutomazione", exAtteso)
                End Try
            Next
            _richiestaCacheMenu = _automazione.CreateCacheRequest()
            For Each idMenu As Integer In New Integer() {DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ProprietaNome, DescrittoreElementi.ProprietaRuoloAria, DescrittoreElementi.ProprietaProprietaAria, DescrittoreElementi.ProprietaAbilitato, DescrittoreElementi.ProprietaFuoriSchermo, DescrittoreElementi.ProprietaIdProcesso, DescrittoreElementi.ProprietaSelezionato, DescrittoreElementi.ProprietaStatoEspansione, DescrittoreElementi.ProprietaRettangolo}
                Try
                    _richiestaCacheMenu.AddProperty(idMenu)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.InizializzaAutomazione", exAtteso2)
                End Try
            Next
            _richiestaCacheEventi = _automazione.CreateCacheRequest()
            For Each idEvento As Integer In New Integer() {DescrittoreElementi.ProprietaIdProcesso, DescrittoreElementi.ProprietaManiglia, DescrittoreElementi.ProprietaNome, DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ProprietaNomeClasse, DescrittoreElementi.ProprietaFramework, DescrittoreElementi.ProprietaRuoloAria}
                Try
                    _richiestaCacheEventi.AddProperty(idEvento)
                Catch ex As Exception
                    log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione (cache eventi " & idEvento.ToString() & "): " & ex.Message)
                End Try
            Next
            _richiestaCacheCommutatore = _automazione.CreateCacheRequest()
            For Each idCommutatore As Integer In New Integer() {DescrittoreElementi.ProprietaNome, DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ProprietaIdAutomazione, DescrittoreElementi.ProprietaIdProcesso, DescrittoreElementi.ProprietaSelezionato, DescrittoreElementi.ProprietaFuocoTastiera, DescrittoreElementi.ProprietaPosizione, DescrittoreElementi.ProprietaTotale, DescrittoreElementi.ProprietaFuoriSchermo}
                Try
                    _richiestaCacheCommutatore.AddProperty(idCommutatore)
                Catch exAtteso3 As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.InizializzaAutomazione", exAtteso3)
                End Try
            Next
            _gestoreFocus = New GestoreFocusUia(New RicevitoreFocus(AddressOf RiceviFocus))
            _automazione.AddFocusChangedEventHandler(_richiestaCacheEventi, _gestoreFocus)
            Try
                _gestoreSelezione = New GestoreSelezioneUia(New RicevitoreSelezione(AddressOf RiceviSelezione))
                _automazione.AddAutomationEventHandler(EventoSelezioneElemento, _automazione.GetRootElement(), TreeScope.TreeScope_Subtree, _richiestaCacheEventi, _gestoreSelezione)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione (selezione): " & ex.Message)
            End Try
            Try
                _gestoreAllerta = New GestoreSelezioneUia(New RicevitoreSelezione(AddressOf RiceviAvvisoAllerta))
                _automazione.AddAutomationEventHandler(EventoAllertaSistema, _automazione.GetRootElement(), TreeScope.TreeScope_Subtree, _richiestaCacheEventi, _gestoreAllerta)
                _gestoreFinestre = New GestoreSelezioneUia(New RicevitoreSelezione(AddressOf RiceviFinestraAperta))
                _automazione.AddAutomationEventHandler(EventoFinestraAperta, _automazione.GetRootElement(), TreeScope.TreeScope_Subtree, _richiestaCacheEventi, _gestoreFinestre)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione (avvisi): " & ex.Message)
            End Try
            Try
                _gestoreRegioniVive = New GestoreSelezioneUia(New RicevitoreSelezione(AddressOf RiceviRegioneViva))
                _automazione.AddAutomationEventHandler(EventoRegioneViva, _automazione.GetRootElement(), TreeScope.TreeScope_Subtree, _richiestaCacheVive, _gestoreRegioniVive)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione (regioni vive): " & ex.Message)
            End Try
            _campi = New LetturaCampi(_automazione, _descrittore)
            _navigazione = New NavigazioneWeb(_automazione, _descrittore, _richiestaCache)
            _walkerControllo = _automazione.ControlViewWalker
            Try
                _richiestaCacheManiglia = _automazione.CreateCacheRequest()
                _richiestaCacheManiglia.AddProperty(DescrittoreElementi.ProprietaManiglia)
                _walkerFinestre = _automazione.CreateTreeWalker(_automazione.CreateNotCondition(_automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaManiglia, 0)))
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione (walker finestre): " & ex.Message)
                _walkerFinestre = Nothing
                _richiestaCacheManiglia = Nothing
            End Try
            CollegaEstensioni()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InizializzaAutomazione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CreaAutomazione() As IUIAutomation
        Try
            Dim oggetto As Object = CreaOggettoCom(ClsidAutomazione8)
            If oggetto Is Nothing Then oggetto = CreaOggettoCom(ClsidAutomazione)
            Return TryCast(oggetto, IUIAutomation)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CreaAutomazione: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function CreaOggettoCom(clsid As String) As Object
        Try
            Dim tipo As Type = Type.GetTypeFromCLSID(New Guid(clsid))
            If tipo Is Nothing Then Return Nothing
            Return Activator.CreateInstance(tipo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CreaOggettoCom (" & clsid & "): " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub ImpostaTempiMassimi()
        Try
            Dim automazione2 As IUIAutomation2 = TryCast(_automazione, IUIAutomation2)
            If automazione2 IsNot Nothing Then
                automazione2.ConnectionTimeout = TempoMassimoUia
                automazione2.TransactionTimeout = TempoMassimoUia
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ImpostaTempiMassimi: " & ex.Message)
        End Try
        Try
            Dim sesta As IUIAutomation6 = TryCast(_automazione, IUIAutomation6)
            If sesta IsNot Nothing Then sesta.ConnectionRecoveryBehavior = ConnectionRecoveryBehaviorOptions.ConnectionRecoveryBehaviorOptions_Enabled
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ImpostaTempiMassimi (recupero connessione): " & ex.Message)
        End Try
        Try
            Dim mappatura As IUIAutomationProxyFactoryMapping = _automazione.ProxyFactoryMapping
            If mappatura IsNot Nothing Then
                Dim proprietaSeguite As Integer() = {DescrittoreElementi.ProprietaNome, DescrittoreElementi.ProprietaValore, DescrittoreElementi.ProprietaStatoAttivazione, DescrittoreElementi.ProprietaStatoEspansione, DescrittoreElementi.ProprietaAbilitato, DescrittoreElementi.ProprietaSelezionato, DescrittoreElementi.ProprietaValoreIntervallo}
                Dim totale As Integer = CInt(mappatura.count)
                Dim nonSupportato As Boolean = False
                For indice As Integer = 0 To totale - 1
                    If nonSupportato Then Exit For
                    Dim voce As IUIAutomationProxyFactoryEntry = Nothing
                    Try
                        voce = mappatura.GetEntry(CUInt(indice))
                    Catch exAtteso As Exception
                        RegistroErrori.Scrivi("WGLettoreSchermo.ImpostaTempiMassimi", exAtteso)
                        Continue For
                    End Try
                    If voce Is Nothing Then Continue For
                    Dim cambiata As Boolean = False
                    For Each idProprieta As Integer In proprietaSeguite
                        If nonSupportato Then Exit For
                        Try
                            Dim vecchi As Array = voce.GetWinEventsForAutomationEvent(EventoProprietaCambiata, idProprieta)
                            If vecchi IsNot Nothing AndAlso vecchi.Length > 0 Then
                                voce.SetWinEventsForAutomationEvent(EventoProprietaCambiata, idProprieta, New Integer() {})
                                cambiata = True
                            End If
                        Catch exTipo As ArrayTypeMismatchException
                            nonSupportato = True
                            log.ScriviLog("WGLettoreSchermo.ImpostaTempiMassimi: elenco WinEvent dei proxy non gestibile (" & exTipo.Message & "), lo salto.")
                        Catch exAtteso2 As Exception
                            RegistroErrori.Scrivi("WGLettoreSchermo.ImpostaTempiMassimi", exAtteso2)
                        End Try
                    Next
                    If cambiata Then
                        mappatura.RemoveEntry(CUInt(indice))
                        mappatura.InsertEntry(CUInt(indice), voce)
                    End If
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ImpostaTempiMassimi (winEvent non mappati): " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiProprieta(idProprieta As Integer)
        Try
            _richiestaCache.AddProperty(idProprieta)
            _descrittore.RegistraProprieta(idProprieta)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiungiProprieta (" & idProprieta.ToString() & "): " & ex.Message)
        End Try
    End Sub

    Private Sub RilasciaAutomazione()
        ScollegaEstensioni()
        Try
            If _automazione IsNot Nothing Then _automazione.RemoveAllEventHandlers()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RilasciaAutomazione: " & ex.Message)
        End Try
        Try
            _gestoreAppModule.Svuota()
            RilasciaAutomazioneRapida()
            _ultimoElemento = Nothing
            _ultimoFocusNav = Nothing
            _revisione = Nothing
            _istantanea = Nothing
            _campi = Nothing
            _navigazione = Nothing
            _navAttiva = False
            _navManuale = False
            _navDisattivataPer = ""
            ImpostaModalita(False, False)
            _gestoreFocus = Nothing
            _gestoreSelezione = Nothing
            _gestoreRegioniVive = Nothing
            _navForzataElemento = Nothing
            _modoFocusAnnunciato = False
            _regioniVive.Clear()
            SyncLock _bloccoRegioniVive
                _regioniVivePendenti.Clear()
            End SyncLock
            _sincronizzaFuocoElemento = Nothing
            _richiestaCacheVive = Nothing
            _richiestaCacheEventi = Nothing
            _richiestaCacheMenu = Nothing
            _richiestaCacheCommutatore = Nothing
            _nomiApplicazioneFinestra.Clear()
            _richiestaCache = Nothing
            _walkerFinestre = Nothing
            _richiestaCacheManiglia = Nothing
            _commutatoreRadice = Nothing
            _commutatoreRadiceFinestra = IntPtr.Zero
            _ultimaVoceCommutatoreChiave = ""
            _automazione = Nothing
            _ultimoNome = ""
            _ultimaParteVariabile = ""
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RilasciaAutomazione (pulizia): " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviFocus(elemento As IUIAutomationElement)
        Try
            If Volatile.Read(_inEsecuzione) = 0 Then Return
            If FinestraBloccata(elemento) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Focus
            evento.Elemento = elemento
            evento.Sequenza = Interlocked.Increment(_sequenzaFocus)
            Volatile.Write(_ultimoFocusArrivo, Environment.TickCount)
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RiceviFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviSelezione(elemento As IUIAutomationElement)
        Try
            If Volatile.Read(_inEsecuzione) = 0 OrElse elemento Is Nothing Then Return
            If FinestraBloccata(elemento) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.SelezioneElemento
            evento.Elemento = elemento
            evento.ChiaveSostituzione = "selezioneElemento"
            evento.Sequenza = Volatile.Read(_sequenzaFocus)
            evento.Epoca = Interlocked.Increment(_epocaSelezione)
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RiceviSelezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RiceviAvvisoAllerta(elemento As IUIAutomationElement)
        AccodaAvviso(elemento, False)
    End Sub

    Private Sub RiceviFinestraAperta(elemento As IUIAutomationElement)
        AccodaAvviso(elemento, True)
    End Sub

    Private Sub RiceviRegioneViva(elemento As IUIAutomationElement)
        Try
            If Volatile.Read(_inEsecuzione) = 0 OrElse Volatile.Read(_leggiRegioniVive) = 0 OrElse elemento Is Nothing Then Return
            If FinestraBloccata(elemento) Then Return
            Dim codaViva As BlockingCollection(Of EventoLettore) = _coda
            If codaViva IsNot Nothing AndAlso codaViva.Count >= 8 Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.RegioneViva
            evento.Elemento = elemento
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RiceviRegioneViva: " & ex.Message)
        End Try
    End Sub

    Private Sub AccodaAvviso(elemento As IUIAutomationElement, finestraAperta As Boolean)
        Try
            If Volatile.Read(_inEsecuzione) = 0 OrElse Volatile.Read(_leggiAvvisi) = 0 OrElse elemento Is Nothing Then Return
            If FinestraBloccata(elemento) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Avviso
            evento.Elemento = elemento
            evento.FinestraAperta = finestraAperta
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaAvviso: " & ex.Message)
        End Try
    End Sub

    Private Sub Accoda(evento As EventoLettore)
        Try
            Dim coda As BlockingCollection(Of EventoLettore) = _coda
            If coda Is Nothing OrElse coda.IsAddingCompleted Then Return
            If evento IsNot Nothing AndAlso evento.ChiaveSostituzione <> "" Then _ultimiPerChiave(evento.ChiaveSostituzione) = evento
            SegnaArrivoEvento(evento)
            coda.TryAdd(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Accoda: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaPrimoPiano(finestra As IntPtr)
        Try
            Volatile.Write(_sequenzaAlPrimoPiano, Volatile.Read(_sequenzaFocus))
            Volatile.Write(_primoPianoTick, Environment.TickCount)
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.PrimoPiano
            evento.Finestra = finestra
            evento.ChiaveSostituzione = "primoPiano"
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaTitolo(finestra As IntPtr)
        Try
            SyncLock _bloccoTitolo
                _titoloFinestra = finestra
                _titoloScadenza = Environment.TickCount + 700
                Interlocked.Exchange(_titoloInAttesa, 1)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaTitolo: " & ex.Message)
        End Try
    End Sub

    Friend Function FinestraTerminale(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Select Case ClasseFinestra(finestra)
                Case "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PuTTY", "mintty", "VirtualConsoleClass"
                    Return True
            End Select
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraTerminale: " & ex.Message)
        End Try
        Return False
    End Function

    Private Sub SvegliaBrowserInPrimoPiano(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero OrElse _segnali Is Nothing Then Return
            If Not FinestraChromium(finestra) AndAlso Not EBrowserGecko(finestra) Then Return
            ThreadPool.QueueUserWorkItem(AddressOf SvegliaBrowser, finestra)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SvegliaBrowserInPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Private Sub SvegliaBrowser(stato As Object)
        Try
            If Not TypeOf stato Is IntPtr Then Return
            Dim finestra As IntPtr = CType(stato, IntPtr)
            If _segnali Is Nothing Then Return
            Dim nuova As Boolean
            SyncLock _bloccoSvegliati
                If _browserSvegliati.Count > 200 Then _browserSvegliati.Clear()
                nuova = _browserSvegliati.Add(finestra)
            End SyncLock
            If nuova Then _segnali.SvegliaFinestra(finestra)
            Thread.Sleep(If(nuova, 700, 400))
            If NativoLettore.GetForegroundWindow() <> finestra Then Return
            EseguiSuOperazioni("ricontrollaBrowser", AddressOf RicontrollaBrowser, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SvegliaBrowser: " & ex.Message)
        End Try
    End Sub

    Private Sub RicontrollaBrowser()
        Try
            If Not NavigazioneWebAbilitata OrElse _navigazione Is Nothing Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If _navAttiva AndAlso Volatile.Read(_tastiVirtuali) = 1 AndAlso _navigazione.NumeroVoci > 0 AndAlso _navigazione.Finestra = primoPiano Then Return
            Dim elemento As IUIAutomationElement = ElementoConFocus(False)
            If elemento Is Nothing OrElse Not ElementoWeb(elemento) Then
                If _segnali IsNot Nothing AndAlso FinestraChromium(primoPiano) Then _segnali.SvegliaFinestra(primoPiano)
                Return
            End If
            Dim primaVuota As Boolean = _navigazione.NumeroVoci = 0
            Dim rientro As Boolean = RientroConCursore(elemento)
            If rientro Then _navForzataElemento = elemento
            AggiornaContestoWeb(elemento)
            If rientro AndAlso Not TastoRecente(700) Then
                AnnunciaRientro()
            ElseIf primaVuota AndAlso _navAttiva AndAlso _navigazione.NumeroVoci > 0 AndAlso Volatile.Read(_tastiVirtuali) = 1 AndAlso Not TastoRecente(700) Then
                Dim dove As String = _navigazione.AnnuncioCorrente()
                If dove <> "" Then Parla(dove, False)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RicontrollaBrowser: " & ex.Message)
        End Try
    End Sub

    Friend Sub SegnalaStatoCambiato(finestra As IntPtr, oggetto As Integer, figlio As Integer)
        Try
            If finestra = IntPtr.Zero Then Return
            If ClasseFinestra(finestra) <> "MozillaDropShadowWindowClass" Then Return
            SyncLock _bloccoProgresso
                _statoFinestra = finestra
                _statoOggetto = oggetto
                _statoFiglio = figlio
            End SyncLock
            If Interlocked.CompareExchange(_statoInCoda, 1, 0) <> 0 Then Return
            ThreadPool.QueueUserWorkItem(AddressOf ControllaStatoMenuGecko)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnalaStatoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaStatoMenuGecko(stato As Object)
        Try
            Thread.Sleep(30)
            Dim finestra As IntPtr
            Dim oggetto As Integer
            Dim figlio As Integer
            SyncLock _bloccoProgresso
                finestra = _statoFinestra
                oggetto = _statoOggetto
                figlio = _statoFiglio
            End SyncLock
            Interlocked.Exchange(_statoInCoda, 0)
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            If MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 300L Then Return
            If (AccessibilitaMsaa.StatoEvento(finestra, oggetto, figlio) And &H4L) = 0L Then Return
            Dim frase As String = AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio)
            If frase = "" OrElse String.Equals(frase, _ultimaFraseMsaa, StringComparison.CurrentCulture) Then Return
            _ultimaFraseMsaa = frase
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaStatoMenuGecko: " & ex.Message)
        End Try
    End Sub

    Friend Sub SegnalaValoreCambiato(finestra As IntPtr, oggetto As Integer, figlio As Integer)
        Try
            If Not BipAvanzamento OrElse finestra = IntPtr.Zero Then Return
            If FinestraChromium(finestra) Then Return
            SyncLock _bloccoProgresso
                _progressoFinestra = finestra
                _progressoOggetto = oggetto
                _progressoFiglio = figlio
            End SyncLock
            If Interlocked.CompareExchange(_progressoInCoda, 1, 0) <> 0 Then Return
            ThreadPool.QueueUserWorkItem(AddressOf ControllaProgresso)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnalaValoreCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub SuonaProgresso(percentuale As Integer)
        Try
            SyncLock _bloccoProgresso
                If percentuale = _bipUltimo AndAlso MillisecondiDa(_bipTick) < 1500L Then Return
                _bipUltimo = percentuale
                _bipTick = Environment.TickCount
            End SyncLock
            SuoniLettore.Progresso(percentuale)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SuonaProgresso: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaProgresso(stato As Object)
        Try
            Thread.Sleep(30)
            Dim finestra As IntPtr
            Dim oggetto As Integer
            Dim figlio As Integer
            SyncLock _bloccoProgresso
                finestra = _progressoFinestra
                oggetto = _progressoOggetto
                figlio = _progressoFiglio
            End SyncLock
            Interlocked.Exchange(_progressoInCoda, 0)
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim percentuale As Integer = -1
            If Not AccessibilitaMsaa.ValoreProgresso(finestra, oggetto, figlio, percentuale) Then Return
            SuonaProgresso(percentuale)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaProgresso: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaCambioValore()
        Try
            If Interlocked.CompareExchange(_cambioValoreInCoda, 1, 0) <> 0 Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.CambioValore
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaCambioValore: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaTasto(codice As Integer, ctrl As Boolean, maiuscolo As Boolean, Optional azione As AzioneTastoLettore = AzioneTastoLettore.Navigazione, Optional alt As Boolean = False, Optional altGr As Boolean = False)
        Try
            SegnaTasto()
            SegnaCodiceTasto(codice)
            If (codice >= &H21 AndAlso codice <= &H28) OrElse azione = AzioneTastoLettore.NavigazioneWeb Then Volatile.Write(_ultimoTastoFreccetta, Environment.TickCount)
            If azione = AzioneTastoLettore.Modifica Then Volatile.Write(_ultimoTastoScrittura, Environment.TickCount)
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Tasto
            evento.CodiceTasto = codice
            evento.Ctrl = ctrl
            evento.Maiuscolo = maiuscolo
            evento.Alt = alt
            evento.AltGr = altGr
            evento.Azione = azione
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaTasto: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaFocusMsaa(finestra As IntPtr, idOggetto As Integer, idFiglio As Integer)
        Try
            If finestra = IntPtr.Zero Then Return
            If idOggetto = NativoLettore.OBJID_CARET OrElse idOggetto = NativoLettore.OBJID_CURSOR Then Return
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI OrElse EProcessoWinGabriel(pid) Then Return
            If FinestraChromium(finestra) Then Return
            SyncLock _bloccoMsaa
                _msaaFinestra = finestra
                _msaaOggetto = idOggetto
                _msaaFiglio = idFiglio
            End SyncLock
            Volatile.Write(_msaaTick, Environment.TickCount)
            If Interlocked.CompareExchange(_focusMsaaInCoda, 1, 0) <> 0 Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.FocusMsaa
            evento.Finestra = finestra
            evento.IdOggetto = idOggetto
            evento.IdFiglio = idFiglio
            evento.Sequenza = Volatile.Read(_sequenzaFocus)
            evento.Epoca = Volatile.Read(_epocaSelezione)
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaFocusMsaa: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaFocusJava(frase As String)
        Try
            If String.IsNullOrWhiteSpace(frase) Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.FocusJava
            evento.Testo = frase
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaFocusJava: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaSelezione()
        Try
            If Interlocked.CompareExchange(_selezioneInCoda, 1, 0) <> 0 Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.SelezioneTesto
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaSelezione: " & ex.Message)
        End Try
    End Sub

    Friend Sub AccodaComando(comando As ComandoLettore)
        Try
            If comando = ComandoLettore.Commutatore Then
                Volatile.Write(_commutatoreUltimoTasto, Environment.TickCount)
                Interlocked.Increment(_commutatoreGenerazione)
                InviaLetturaCommutatore()
                Return
            End If
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Comando
            evento.Comando = comando
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaComando: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaLetturaCommutatore()
        Try
            SegnaTasto()
            If Sospeso Then Return
            ThreadPool.QueueUserWorkItem(AddressOf AttendiLetturaCommutatore, Volatile.Read(_commutatoreGenerazione))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InviaLetturaCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Sub AttendiLetturaCommutatore(stato As Object)
        Try
            Dim generazione As Integer = Volatile.Read(_commutatoreGenerazione)
            If TypeOf stato Is Integer Then generazione = CInt(stato)
            Thread.Sleep(200)
            If Volatile.Read(_commutatoreGenerazione) <> generazione Then Return
            If CommutatoreAnnunciatoDopoTasto() Then Return
            Dim corsia As CodaOperazioni = _operazioniCommutatore
            If corsia Is Nothing Then Return
            corsia.Esegui("commutatore", AddressOf LeggiCommutatoreDaTasto, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttendiLetturaCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiCommutatoreDaTasto()
        Try
            Volatile.Write(_origineUtente, 1)
            Dim generazione As Integer = Volatile.Read(_commutatoreGenerazione)
            If CommutatoreAnnunciatoDopoTasto() Then Return
            LeggiCommutatore(2, True, generazione)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiCommutatoreDaTasto: " & ex.Message)
        Finally
            Volatile.Write(_origineUtente, 0)
        End Try
    End Sub

    Private NotInheritable Class RichiestaVoceCommutatore
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _elemento As IUIAutomationElement

        Public Sub New(lettore As WGLettoreSchermo, elemento As IUIAutomationElement)
            _lettore = lettore
            _elemento = elemento
        End Sub

        Public Sub Esegui()
            Try
                If _lettore.VoceCommutatoreValida(_elemento) Then
                    _lettore.AnnunciaCommutatore(_elemento)
                Else
                    _lettore.LeggiCommutatore(2)
                End If
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaVoceCommutatore.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Function CommutatoreAnnunciatoDopoTasto() As Boolean
        Try
            Return MillisecondiDa(Volatile.Read(_commutatoreAttivitaTick)) < MillisecondiDa(Volatile.Read(_commutatoreUltimoTasto))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CommutatoreAnnunciatoDopoTasto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CommutatoreAppenaDetto() As Boolean
        Try
            If Volatile.Read(_commutatoreAnnunciato) = 0 Then Return False
            If AltPremuto() Then Return True
            Return MillisecondiDa(Volatile.Read(_commutatoreAttivitaTick)) < 1500L
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CommutatoreAppenaDetto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RegistraTitoliFinestra(ParamArray titoli() As String)
        Try
            Dim insieme As New HashSet(Of String)(StringComparer.CurrentCultureIgnoreCase)
            Dim precedenti As HashSet(Of String) = _titoliFinestra
            If precedenti IsNot Nothing AndAlso MillisecondiDa(Volatile.Read(_ultimoAnnuncioFinestraTick)) < 3000L Then
                For Each vecchio As String In precedenti
                    insieme.Add(vecchio)
                Next
            End If
            For Each titolo As String In titoli
                Dim pulito As String = If(titolo, "").Trim()
                If pulito.Length >= 2 Then insieme.Add(pulito)
            Next
            _titoliFinestra = insieme
            Volatile.Write(_ultimoAnnuncioFinestraTick, Environment.TickCount)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RegistraTitoliFinestra: " & ex.Message)
        End Try
    End Sub

    Private Function TitoloRipetuto(testo As String) As Boolean
        Try
            Dim pulito As String = If(testo, "").Trim()
            If pulito = "" Then Return False
            If MillisecondiDa(Volatile.Read(_ultimoAnnuncioFinestraTick)) > 3000L Then Return False
            Dim titoli As HashSet(Of String) = _titoliFinestra
            If titoli Is Nothing OrElse titoli.Count = 0 Then Return False
            If titoli.Contains(pulito) Then Return True
            For Each titolo As String In titoli
                If pulito.StartsWith(titolo, StringComparison.CurrentCultureIgnoreCase) Then
                    Dim resto As String = pulito.Substring(titolo.Length).Trim()
                    If resto = "" OrElse resto.StartsWith(",", StringComparison.Ordinal) Then Return True
                End If
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloRipetuto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PuoSpostareFuoco() As Boolean
        Try
            If _navigazione Is Nothing Then Return False
            If (CInt(NativoLettore.GetAsyncKeyState(&H12)) And &H8000) <> 0 Then Return False
            If (CInt(NativoLettore.GetAsyncKeyState(&H5B)) And &H8000) <> 0 OrElse (CInt(NativoLettore.GetAsyncKeyState(&H5C)) And &H8000) <> 0 Then Return False
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano = IntPtr.Zero OrElse primoPiano <> _navigazione.Finestra Then Return False
            If InCommutatore(primoPiano) Then Return False
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PuoSpostareFuoco: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EBrowserGecko(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Return ClasseFinestra(finestra).StartsWith("Mozilla", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EBrowserGecko: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function NomeDesktopVirtuale(nome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return False
            Dim pulito As String = nome.Trim()
            If Regex.IsMatch(pulito, "^(desktop|desktop virtuale|virtual desktop)\s+\d+$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant) Then Return True
            SyncLock _bloccoNomiDesktop
                If MillisecondiDa(_nomiDesktopTick) > 30000L Then
                    _nomiDesktopTick = Environment.TickCount
                    _nomiDesktop.Clear()
                    LeggiNomiDesktop()
                End If
                Return _nomiDesktop.Contains(pulito)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeDesktopVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LeggiNomiDesktop()
        Try
            Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops")
                If chiave Is Nothing Then Return
                For Each nomeSotto As String In chiave.GetSubKeyNames()
                    Try
                        Using sotto As Microsoft.Win32.RegistryKey = chiave.OpenSubKey(nomeSotto)
                            If sotto Is Nothing Then Continue For
                            Dim valore As String = TryCast(sotto.GetValue("Name"), String)
                            If Not String.IsNullOrWhiteSpace(valore) Then _nomiDesktop.Add(valore.Trim())
                        End Using
                    Catch ex As Exception
                        log.ScriviLog("WGLettoreSchermo.LeggiNomiDesktop (desktop): " & ex.Message)
                    End Try
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiNomiDesktop: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiPopupGecko()
        Try
            If Not _popupGecko Then Return
            _popupGecko = False
            Dim vivo As IUIAutomationElement = ElementoConFocus()
            If vivo IsNot Nothing AndAlso ElementoWeb(vivo) Then AggiornaContestoWeb(vivo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ChiudiPopupGecko: " & ex.Message)
        End Try
    End Sub

    Private Sub ModalitaMenu()
        Try
            Dim nelBrowser As Boolean = _navAttiva AndAlso EBrowser(NativoLettore.GetForegroundWindow())
            ImpostaModalita(_navAttiva, True, Not nelBrowser)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ModalitaMenu: " & ex.Message)
        End Try
    End Sub

    Private Function FuocoSpostatoDallaPagina(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _navigazione Is Nothing Then Return False
            If Not _navAttiva OrElse Volatile.Read(_tastiVirtuali) <> 1 Then Return False
            If _popupGecko Then Return False
            If Volatile.Read(_cursoreDaTasti) <> 1 Then Return False
            If MillisecondiDa(Volatile.Read(_ultimoFuocoChiesto)) < 2500L Then Return False
            ' Dopo aver attivato un link la pagina nuova può metterci parecchi secondi: il suo fuoco è quello giusto, non uno spostamento della pagina vecchia.
            If MillisecondiDa(Volatile.Read(_ultimaAttivazioneWeb)) < 60000L Then Return False
            If Not MovimentoVirtualeRecente(15000) Then Return False
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano <> _navigazione.Finestra Then Return False
            If Not FinestraChromium(primoPiano) OrElse EBrowserGecko(primoPiano) Then Return False
            If Not ElementoWeb(elemento) Then Return False
            Return Not _navigazione.FuocoDaSeguire(elemento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoSpostatoDallaPagina: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PaginaAppenaCaricata(elemento As IUIAutomationElement, chiave As String) As Boolean
        Try
            If elemento Is Nothing OrElse String.IsNullOrEmpty(chiave) Then Return False
            If _descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloDocumento Then Return False
            If MillisecondiDa(Volatile.Read(_primoPianoTick)) < 1500L Then
                _pagineLette.Add(chiave)
                Return False
            End If
            If DescrittoreElementi.PrimoPianoTeams() Then Return False
            If _pagineLette.Contains(chiave) Then Return False
            If _pagineLette.Count > 300 Then _pagineLette.Clear()
            _pagineLette.Add(chiave)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PaginaAppenaCaricata: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AvviaLetturaPagina()
        Try
            If Not InVirtuale() OrElse Volatile.Read(_continuaAttiva) = 1 Then Return
            If TastoRecente(400) Then Return
            AvviaLetturaContinua()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AvviaLetturaPagina: " & ex.Message)
        End Try
    End Sub

    Private Function AggiungiNonSelezionato(elemento As IUIAutomationElement, testo As String) As String
        Try
            If elemento Is Nothing OrElse String.IsNullOrEmpty(testo) OrElse _descrittore Is Nothing Then Return testo
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloElementoDati
                Case Else
                    Return testo
            End Select
            If ElementoWeb(elemento) Then Return testo
            If Not _descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSelezioneDisponibile) Then Return testo
            If Not _descrittore.FuocoDaTastiera(elemento) Then Return testo
            If _descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSelezionato) Then Return testo
            If testo.IndexOf("non selezionat", StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return testo
            Return testo & ", non selezionato"
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiungiNonSelezionato: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function ContenitoreVuoto(elemento As IUIAutomationElement, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            If elemento Is Nothing OrElse _automazione Is Nothing OrElse _descrittore Is Nothing Then Return False
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElenco, DescrittoreElementi.ControlloAlbero, DescrittoreElementi.ControlloGrigliaDati, DescrittoreElementi.ControlloTabella
                Case Else
                    Return False
            End Select
            If ElementoWeb(elemento) Then Return False
            Dim figli As IUIAutomationElementArray = elemento.FindAllBuildCache(TreeScope.TreeScope_Children, _automazione.CreateTrueCondition(), _richiestaCache)
            If figli Is Nothing Then Return True
            Dim testi As New List(Of String)()
            For indice As Integer = 0 To Math.Min(figli.Length, 30) - 1
                Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                Select Case _descrittore.TipoControllo(figlio)
                    Case DescrittoreElementi.ControlloIntestazione, DescrittoreElementi.ControlloElementoIntestazione, DescrittoreElementi.ControlloBarraScorrimento, DescrittoreElementi.ControlloCursore
                    Case DescrittoreElementi.ControlloTesto
                        Dim scritta As String = _descrittore.Nome(figlio)
                        If scritta <> "" Then testi.Add(scritta)
                    Case Else
                        Return False
                End Select
            Next
            If figli.Length > 30 Then Return False
            messaggio = String.Join(", ", testi)
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ContenitoreVuoto", exAtteso)
            Return False
        End Try
    End Function

    Private Function FuocoNelFiglio(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _automazione Is Nothing OrElse _descrittore Is Nothing Then Return Nothing
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloBarraStrumenti, DescrittoreElementi.ControlloElenco, DescrittoreElementi.ControlloAlbero,
                     DescrittoreElementi.ControlloSchede, DescrittoreElementi.ControlloBarraMenu, DescrittoreElementi.ControlloGrigliaDati,
                     DescrittoreElementi.ControlloTabella
                    Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(30008, True)
                    Dim figlio As IUIAutomationElement = elemento.FindFirstBuildCache(TreeScope.TreeScope_Children, condizione, _richiestaCache)
                    If figlio IsNot Nothing AndAlso Not StessoElemento(figlio, elemento) Then Return figlio
                    Return Nothing
            End Select
            Dim maniglia As IntPtr = New IntPtr(_descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaManiglia, 0))
            If maniglia = IntPtr.Zero OrElse ClasseFinestra(maniglia) <> "Shell DocObject View" Then Return Nothing
            Dim interna As IntPtr = CercaFinestraDiscendente(maniglia, "Internet Explorer_Server", 4)
            If interna = IntPtr.Zero Then Return Nothing
            Return _automazione.ElementFromHandleBuildCache(interna, _richiestaCache)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoNelFiglio: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub EcoFuoriDaiCampi(evento As EventoLettore)
        Try
            If evento Is Nothing OrElse Not EcoCaratteri OrElse evento.Ctrl OrElse evento.Alt Then Return
            If MenuVirtualeAttivo OrElse InVirtuale() Then Return
            If Volatile.Read(_ultimoFocusTick) - Volatile.Read(_ultimoTastoTick) >= 0 Then Return
            Dim carattere As String = CarattereEco(evento.CodiceTasto, evento.Maiuscolo)
            If carattere = "" Then Return
            Volatile.Write(_origineUtente, 1)
            Try
                Parla(_descrittore.NomeCarattere(carattere), True)
            Finally
                Volatile.Write(_origineUtente, 0)
            End Try
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EcoFuoriDaiCampi: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CarattereEco(codice As Integer, maiuscolo As Boolean) As String
        If codice >= &H41 AndAlso codice <= &H5A Then
            Dim lettera As String = ChrW(codice).ToString()
            Return If(maiuscolo, lettera, lettera.ToLowerInvariant())
        End If
        If codice >= &H30 AndAlso codice <= &H39 Then Return If(maiuscolo, "", ChrW(codice).ToString())
        If codice >= &H60 AndAlso codice <= &H69 Then Return ChrW(&H30 + codice - &H60).ToString()
        Select Case codice
            Case &H20
                Return " "
            Case &H6A
                Return "*"
            Case &H6B
                Return "+"
            Case &H6D
                Return "-"
            Case &H6E
                Return ","
            Case &H6F
                Return "/"
        End Select
        Return ""
    End Function

    Private Function ReindirizzaVistaWebWx(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _automazione Is Nothing Then Return False
            If Not String.Equals(_descrittore.Nome(elemento), "wxWebView", StringComparison.Ordinal) Then Return False
            If Not _descrittore.ApplicazioneWx(elemento) Then Return False
            Dim maniglia As IntPtr = ManigliaFinestra(elemento)
            If maniglia = IntPtr.Zero Then Return False
            Dim interna As IntPtr = CercaFinestraDiscendente(maniglia, "Chrome_RenderWidgetHostHWND", 6)
            If interna = IntPtr.Zero Then interna = CercaFinestraDiscendente(maniglia, "Internet Explorer_Server", 6)
            If interna = IntPtr.Zero Then Return False
            Dim interno As IUIAutomationElement = _automazione.ElementFromHandle(interna)
            If interno Is Nothing Then Return False
            interno.SetFocus()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ReindirizzaVistaWebWx: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CercaFinestraDiscendente(padre As IntPtr, classe As String, profondita As Integer) As IntPtr
        Try
            If padre = IntPtr.Zero OrElse profondita <= 0 Then Return IntPtr.Zero
            Dim diretta As IntPtr = NativoLettore.FindWindowEx(padre, IntPtr.Zero, classe, Nothing)
            If diretta <> IntPtr.Zero Then Return diretta
            Dim figlio As IntPtr = NativoLettore.FindWindowEx(padre, IntPtr.Zero, Nothing, Nothing)
            Dim giri As Integer = 0
            While figlio <> IntPtr.Zero AndAlso giri < 64
                Dim trovata As IntPtr = CercaFinestraDiscendente(figlio, classe, profondita - 1)
                If trovata <> IntPtr.Zero Then Return trovata
                figlio = NativoLettore.FindWindowEx(padre, figlio, Nothing, Nothing)
                giri += 1
            End While
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CercaFinestraDiscendente: " & ex.Message)
        End Try
        Return IntPtr.Zero
    End Function

    Private Function VoceMenuGiaDetta(nome As String) As Boolean
        Try
            If String.IsNullOrEmpty(nome) Then Return False
            If Not String.Equals(nome, _ultimoMenuMsaaNome, StringComparison.CurrentCultureIgnoreCase) Then Return False
            Return MillisecondiDa(Volatile.Read(_ultimoMenuMsaaTick)) < 700L
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VoceMenuGiaDetta: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function VoceNonScrivibile(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _menuVirtualeMessaggi Then Return True
            If DescrittoreElementi.PrimoPianoTeams() Then Return True
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloElementoAlbero
                    Return True
            End Select
            Return _descrittore.MessaggioTeams(elemento, _walkerControllo, _richiestaCache)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VoceNonScrivibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FuocoDalCursoreVirtuale(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If MovimentoVirtualeRecente(800) Then Return True
            Dim forzato As IUIAutomationElement = _navForzataElemento
            Return forzato IsNot Nothing AndAlso StessoElemento(elemento, forzato)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoDalCursoreVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FuocoDiAltraFinestra(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If evento Is Nothing OrElse elemento Is Nothing OrElse evento.Sequenza <= 0 Then Return False
            If evento.Sequenza > Volatile.Read(_sequenzaAlPrimoPiano) Then Return False
            If MillisecondiDa(Volatile.Read(_primoPianoTick)) > 3000L Then Return False
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If primo = IntPtr.Zero Then Return False
            Return Not ElementoInFinestra(elemento, primo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoDiAltraFinestra: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TitoloFinestraRipetuto(elemento As IUIAutomationElement, nome As String) As Boolean
        Try
            If elemento Is Nothing OrElse String.IsNullOrWhiteSpace(nome) Then Return False
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloFinestra, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloPersonalizzato,
                     DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloBarraTitolo, 0
                    Return TitoloRipetuto(nome)
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloFinestraRipetuto: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Function TabDalCursoreVirtuale() As Boolean
        Try
            If Not TastiVirtuali OrElse MenuVirtualeAttivo Then Return False
            If _navigazione Is Nothing OrElse Not _navAttiva Then Return False
            Return Volatile.Read(_cursoreDaTasti) = 1
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TabDalCursoreVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub SegnaCursoreDaTasti()
        Try
            Volatile.Write(_cursoreDaTasti, 1)
            Interlocked.Increment(_tabGenerazione)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnaCursoreDaTasti: " & ex.Message)
        End Try
    End Sub

    Private Sub CursoreAlFuoco()
        Try
            Volatile.Write(_cursoreDaTasti, 0)
            _tabRifiutato = Nothing
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CursoreAlFuoco: " & ex.Message)
        End Try
    End Sub

    Private Sub TabDalCursore(indietro As Boolean)
        Try
            Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
            Dim generazione As Integer = Interlocked.Increment(_tabGenerazione)
            If _navigazione Is Nothing OrElse _navigazione.NumeroVoci = 0 Then
                Volatile.Write(_cursoreDaTasti, 0)
                InviaTab(indietro)
                Return
            End If
            Dim fuoco As IUIAutomationElement = ElementoConFocus()
            Dim bersaglio As IUIAutomationElement = _navigazione.FocalizzabileDalCursore(Not indietro, _tabRifiutato)
            If bersaglio IsNot Nothing AndAlso _ultimoBersaglioTab IsNot Nothing AndAlso generazione = _ultimoBersaglioGenerazione + 1 AndAlso StessoElemento(bersaglio, _ultimoBersaglioTab) AndAlso (fuoco Is Nothing OrElse Not StessoElemento(fuoco, bersaglio)) Then
                _tabRifiutato = bersaglio
                bersaglio = _navigazione.FocalizzabileDalCursore(Not indietro, bersaglio)
            End If
            ' Su una pagina lunga il prossimo elemento può essere troppo lontano per la ricerca: si va al campo di scrittura.
            If bersaglio Is Nothing AndAlso Not indietro Then bersaglio = _navigazione.CampoDiScrittura(fuoco)
            _ultimoBersaglioTab = bersaglio
            _ultimoBersaglioGenerazione = generazione
            If bersaglio Is Nothing Then
                Volatile.Write(_cursoreDaTasti, 0)
                InviaTab(indietro)
                Return
            End If
            _fuocoDifferito = Nothing
            _sincronizzaFuocoElemento = Nothing
            _navForzataElemento = Nothing
            Dim frase As String = _navigazione.PortaCursoreSu(bersaglio)
            _revisione = bersaglio
            If fuoco IsNot Nothing AndAlso StessoElemento(fuoco, bersaglio) Then
                CursoreAlFuoco()
                _ultimoFocusNav = bersaglio
                If frase <> "" Then Parla(frase, True)
                AggiornaContestoWeb(bersaglio)
                Return
            End If
            _ultimoFocusNav = Nothing
            _ultimoElemento = Nothing
            If Not PuoSpostareFuoco() Then Return
            Dim fuochiPrima As Integer = Volatile.Read(_sequenzaFocus)
            Try
                bersaglio.SetFocus()
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.TabDalCursore (fuoco): " & ex.Message)
                _tabRifiutato = bersaglio
                If frase <> "" Then Parla(frase, True)
                Return
            End Try
            Dim verifica As New VerificaTab(Me, bersaglio, frase, fuochiPrima, generazione)
            ThreadPool.QueueUserWorkItem(AddressOf verifica.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TabDalCursore: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class VerificaTab
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _bersaglio As IUIAutomationElement
        Private ReadOnly _frase As String
        Private ReadOnly _fuochiPrima As Integer
        Private ReadOnly _generazione As Integer

        Public Sub New(lettore As WGLettoreSchermo, bersaglio As IUIAutomationElement, frase As String, fuochiPrima As Integer, generazione As Integer)
            _lettore = lettore
            _bersaglio = bersaglio
            _frase = If(frase, "")
            _fuochiPrima = fuochiPrima
            _generazione = generazione
        End Sub

        Public Sub Attendi(stato As Object)
            Try
                Thread.Sleep(650)
                If _lettore Is Nothing Then Return
                _lettore.EseguiSuOperazioni("verificaTab", AddressOf Controlla, sostituisci:=True)
            Catch ex As Exception
                If _lettore IsNot Nothing Then _lettore.log.ScriviLog("WGLettoreSchermo.VerificaTab.Attendi: " & ex.Message)
            End Try
        End Sub

        Public Sub Controlla()
            Try
                _lettore.ControllaFuocoTab(_bersaglio, _frase, _fuochiPrima, _generazione)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.VerificaTab.Controlla: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Sub ControllaFuocoTab(bersaglio As IUIAutomationElement, frase As String, fuochiPrima As Integer, generazione As Integer)
        Try
            If bersaglio Is Nothing Then Return
            If generazione <> Volatile.Read(_tabGenerazione) Then Return
            If Volatile.Read(_sequenzaFocus) <> fuochiPrima Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            If _navigazione Is Nothing OrElse Not InVirtuale() Then Return
            If NativoLettore.GetForegroundWindow() <> _navigazione.Finestra Then Return
            Dim attuale As IUIAutomationElement = ElementoConFocus(False)
            If attuale IsNot Nothing AndAlso StessoElemento(attuale, bersaglio) Then
                MemorizzaElemento(bersaglio, _descrittore.Nome(bersaglio), _descrittore.ParteVariabile(bersaglio))
                _ultimoFocusTick = Environment.TickCount
                CursoreAlFuoco()
                _ultimoFocusNav = bersaglio
                If frase <> "" Then Parla(frase, True)
                AggiornaContestoWeb(bersaglio)
                Return
            End If
            _tabRifiutato = bersaglio
            If frase <> "" Then Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaFuocoTab: " & ex.Message)
        End Try
    End Sub

    Private Function RientroConCursore(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _navigazione Is Nothing OrElse Not NavigazioneWebAbilitata Then Return False
            If Volatile.Read(_fuoriDalDocumento) = 0 OrElse Volatile.Read(_cursoreDaTasti) <> 1 Then Return False
            If _navDisattivataPer <> "" OrElse _popupGecko OrElse MenuVirtualeAttivo Then Return False
            If _navigazione.NumeroVoci = 0 OrElse _navigazione.Finestra <> NativoLettore.GetForegroundWindow() Then Return False
            If Not ElementoWeb(elemento) Then Return False
            If Not _navigazione.ERadice(elemento) AndAlso (_ultimoFocusNav Is Nothing OrElse Not StessoElemento(elemento, _ultimoFocusNav)) Then Return False
            Return _navigazione.CursoreValido()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RientroConCursore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RientraConCursore(elemento As IUIAutomationElement)
        Try
            If elemento Is Nothing Then Return
            MemorizzaElemento(elemento, _descrittore.Nome(elemento), _descrittore.ParteVariabile(elemento))
            _ultimoFocusTick = Environment.TickCount
            _uiaFerma = Nothing
            _navForzataElemento = elemento
            AggiornaContestoWeb(elemento)
            AggiornaEventiFuoco(elemento)
            AnnunciaRientro()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RientraConCursore: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaRientro()
        Try
            If Not InVirtuale() OrElse Volatile.Read(_cursoreDaTasti) <> 1 Then Return
            If MillisecondiDa(Volatile.Read(_rientroAnnunciatoTick)) < 1500L Then Return
            Volatile.Write(_rientroAnnunciatoTick, Environment.TickCount)
            Dim dove As String = _navigazione.AnnuncioCorrente()
            _revisione = _navigazione.ElementoCorrente()
            If dove <> "" Then Parla(dove, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaRientro: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaTab(indietro As Boolean)
        Dim premiMaiuscolo As Boolean = False
        Try
            premiMaiuscolo = indietro AndAlso (CInt(NativoLettore.GetAsyncKeyState(&H10)) And &H8000) = 0
            If premiMaiuscolo Then NativoLettore.keybd_event(CByte(&H10), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&H9), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&H9), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InviaTab: " & ex.Message)
        Finally
            Try
                If premiMaiuscolo Then NativoLettore.keybd_event(CByte(&H10), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InviaTab (maiuscolo): " & ex.Message)
            End Try
        End Try
    End Sub

    Private Sub ApplicaFuocoDifferito(subito As Boolean)
        Try
            Dim bersaglio As IUIAutomationElement = _fuocoDifferito
            If bersaglio Is Nothing Then Return
            If Not subito AndAlso TastoRecente(350) Then Return
            _fuocoDifferito = Nothing
            If _navigazione Is Nothing OrElse Not _navAttiva Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim attuale As IUIAutomationElement = ElementoConFocus()
            If attuale IsNot Nothing AndAlso StessoElemento(attuale, bersaglio) Then Return
            MemorizzaElemento(bersaglio, _descrittore.Nome(bersaglio), _descrittore.ParteVariabile(bersaglio))
            _ultimoFocusNav = bersaglio
            _navForzataElemento = bersaglio
            _modoFocusAnnunciato = False
            SilenziaValore(500)
            SilenziaSelezione(500)
            If Not PuoSpostareFuoco() Then Return
            Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
            bersaglio.SetFocus()
            _ultimoFocusTick = Environment.TickCount
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApplicaFuocoDifferito: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiVoceMenuWinForms(finestra As IntPtr, oggetto As Integer, figlio As Integer, ByRef frase As String) As Boolean
        frase = ""
        Try
            If finestra = IntPtr.Zero OrElse _automazione Is Nothing OrElse _descrittore Is Nothing Then Return False
            Dim classe As String = ClasseFinestra(finestra)
            Dim gecko As Boolean = classe.StartsWith("Mozilla", StringComparison.Ordinal)
            If Not gecko AndAlso Not classe.StartsWith("WindowsForms", StringComparison.Ordinal) Then Return False
            Dim ruolo As Integer = 0
            Dim nomeMsaa As String = ""
            If Not AccessibilitaMsaa.InfoEvento(finestra, oggetto, figlio, ruolo, nomeMsaa) Then Return False
            If ruolo = 12 AndAlso gecko AndAlso classe = "MozillaWindowClass" Then Return False
            Dim popupGecko As Boolean = False
            If ruolo <> 12 Then
                If Not gecko Then Return False
                Dim nomePopup As String = ""
                If Not AccessibilitaMsaa.VocePopupGecko(finestra, oggetto, figlio, nomePopup) Then
                    ChiudiPopupGecko()
                    Return False
                End If
                popupGecko = True
                nomeMsaa = nomePopup
            End If
            If gecko Then
                _popupGecko = True
                If nomeMsaa <> "" AndAlso String.Equals(nomeMsaa, _ultimoNome, StringComparison.CurrentCultureIgnoreCase) AndAlso MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 700L Then Return True
                frase = If(popupGecko, nomeMsaa, AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio))
                If frase <> "" AndAlso MillisecondiDa(Volatile.Read(_ultimoMenuMsaaTick)) > 1500L Then frase = "menu, " & frase
                _ultimoMenuMsaaNome = nomeMsaa
                Volatile.Write(_ultimoMenuMsaaTick, Environment.TickCount)
                _ultimoNome = nomeMsaa
                Return True
            End If
            Dim trovata As IUIAutomationElement = Nothing
            Dim posizione As Integer = 0
            Dim totale As Integer = 0
            Dim radice As IUIAutomationElement = _automazione.ElementFromHandleBuildCache(finestra, _richiestaCache)
            If radice IsNot Nothing AndAlso nomeMsaa <> "" Then
                Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloVoceMenu)
                Dim voci As IUIAutomationElementArray = radice.FindAllBuildCache(TreeScope.TreeScope_Descendants, condizione, _richiestaCache)
                If voci IsNot Nothing Then
                    For indice As Integer = 0 To Math.Min(voci.Length, 200) - 1
                        Dim voce As IUIAutomationElement = voci.GetElement(indice)
                        totale += 1
                        If trovata Is Nothing AndAlso String.Equals(_descrittore.Nome(voce).Trim(), nomeMsaa.Trim(), StringComparison.CurrentCultureIgnoreCase) Then
                            trovata = voce
                            posizione = totale
                        End If
                    Next
                End If
            End If
            If trovata Is Nothing Then
                frase = AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio)
                Return True
            End If
            Dim nome As String = _descrittore.Nome(trovata)
            Dim variabile As String = _descrittore.ParteVariabile(trovata)
            If StessaVoce(trovata, _ultimoElemento) AndAlso nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile AndAlso MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 700L Then Return True
            frase = _descrittore.Descrivi(trovata)
            If _descrittore.LeggiIntero(trovata, DescrittoreElementi.ProprietaPosizione, 0) <= 0 AndAlso posizione > 0 AndAlso totale > 0 Then
                frase = If(frase = "", "", frase & ", ") & posizione.ToString() & " di " & totale.ToString()
            End If
            MemorizzaElemento(trovata, nome, variabile)
            _revisione = trovata
            _uiaFerma = Nothing
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiVoceMenuWinForms: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub TastoSpostamentoFuoco()
        Try
            Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
            Volatile.Write(_ultimoTastoFreccetta, Environment.TickCount)
            SegnaTasto()
            RichiediInterruzione()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoSpostamentoFuoco: " & ex.Message)
        End Try
    End Sub

    Private Function FinestraChromium(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As String = ClasseFinestra(finestra)
            Return classe = "Chrome_RenderWidgetHostHWND" OrElse classe.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraChromium: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub RichiediInterruzione()
        Try
            If Sospeso Then Return
            Volatile.Write(_continuaAttiva, 0)
            Dim coda As CodaVoce = _codaVoce
            If coda IsNot Nothing Then coda.SvuotaAttesa()
            ThreadPool.QueueUserWorkItem(AddressOf EseguiInterruzione, Environment.TickCount)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RichiediInterruzione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiInterruzione(stato As Object)
        Try
            Dim tickRichiesta As Integer = Environment.TickCount
            If TypeOf stato Is Integer Then tickRichiesta = CInt(stato)
            FermaLetturaContinua(False)
            Dim coda As CodaVoce = _codaVoce
            If coda IsNot Nothing Then
                coda.FermaSeNonSuperata(tickRichiesta)
                Return
            End If
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura Is Nothing Then Return
            If lettura.StaParlando Then lettura.Ferma()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiInterruzione: " & ex.Message)
        End Try
    End Sub

    Friend Function PrimoPianoInWinGabriel() As Boolean
        Try
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            Return EProcessoWinGabriel(pid)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PrimoPianoInWinGabriel: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ElaboraEvento(evento As EventoLettore)
        Try
            If evento Is Nothing Then Return
            If evento.ChiaveSostituzione <> "" Then
                Dim ultimo As EventoLettore = Nothing
                If _ultimiPerChiave.TryGetValue(evento.ChiaveSostituzione, ultimo) Then
                    If Not ReferenceEquals(ultimo, evento) Then Return
                    _ultimiPerChiave.TryRemove(evento.ChiaveSostituzione, ultimo)
                End If
            End If
            If evento.Tipo = TipoEventoLettore.CambioValore Then Interlocked.Exchange(_cambioValoreInCoda, 0)
            If evento.Tipo = TipoEventoLettore.SelezioneTesto Then Interlocked.Exchange(_selezioneInCoda, 0)
            If evento.Tipo = TipoEventoLettore.FocusMsaa Then Interlocked.Exchange(_focusMsaaInCoda, 0)
            If evento.Tipo = TipoEventoLettore.Comando AndAlso evento.Comando = ComandoLettore.ComponenteEsterno Then
                Dim chiamataComponente As New Chiamata(Of String)(AddressOf EseguiComandoComponente, evento.Testo)
                EseguiSuOperazioni("", AddressOf chiamataComponente.Esegui)
                Return
            End If
            If evento.Tipo = TipoEventoLettore.Comando Then
                If evento.Comando = ComandoLettore.Commutatore Then Interlocked.Exchange(_commutatoreInCoda, 0)
                Dim comando As ComandoLettore = evento.Comando
                Dim chiamataComando As New Chiamata(Of ComandoLettore)(AddressOf EseguiComando, comando)
                EseguiSuOperazioni("", AddressOf chiamataComando.Esegui)
                Return
            End If
            If Sospeso Then Return
            If EventoDaScartare(evento) Then Return
            If AvvisoDaIgnorareInAlbero(evento) Then Return
            If (evento.Tipo = TipoEventoLettore.Avviso OrElse evento.Tipo = TipoEventoLettore.RegioneViva OrElse evento.Tipo = TipoEventoLettore.Notifica) AndAlso AvvisoDaAltraPagina(evento.Elemento, evento.Finestra) Then Return
            If evento.Tipo = TipoEventoLettore.Avviso Then
                Dim eventoAvviso As EventoLettore = evento
                Dim chiamataAvviso As New Chiamata(Of EventoLettore)(AddressOf ElaboraAvviso, eventoAvviso)
                EseguiSuOperazioni("", AddressOf chiamataAvviso.Esegui)
                Return
            End If
            If ElaboraEventoEsteso(evento) Then Return
            If PrimoPianoInWinGabriel() Then Return
            Select Case evento.Tipo
                Case TipoEventoLettore.Focus
                    If evento.Elemento IsNot Nothing AndAlso InCommutatore(NativoLettore.GetForegroundWindow()) Then
                        Dim corsiaCommutatore As CodaOperazioni = _operazioniCommutatore
                        If corsiaCommutatore IsNot Nothing Then
                            Dim richiestaCommutatore As New RichiestaVoceCommutatore(Me, evento.Elemento)
                            corsiaCommutatore.Esegui("commutatoreEvento", AddressOf richiestaCommutatore.Esegui, sostituisci:=True)
                            Exit Select
                        End If
                    End If
                    Dim eventoFocus As EventoLettore = evento
                    Dim chiamataFocus As New Chiamata(Of EventoLettore)(AddressOf ElaboraFocus, eventoFocus)
                    EseguiSuOperazioni("focus", AddressOf chiamataFocus.Esegui, sostituisci:=True)
                Case TipoEventoLettore.PrimoPiano
                    Dim finestra As IntPtr = evento.Finestra
                    If finestra <> IntPtr.Zero AndAlso finestra = NativoLettore.GetForegroundWindow() AndAlso Not InCommutatore(finestra) Then
                        Volatile.Write(_fuoriDalDocumento, 1)
                        AzzeraListaNativa()
                        FermaLetturaContinua(False)
                        Dim corsia As CodaOperazioni = _operazioni
                        If corsia IsNot Nothing Then corsia.Svuota()
                        _menuNativoAperto = False
                        ChiudiMenuVirtuale(False)
                        _popupGecko = False
                        ImpostaModalita(False, False)
                    End If
                    SvegliaBrowserInPrimoPiano(finestra)
                    AnnunciaTitoloPrimoPiano(finestra)
                    Dim chiamataPrimoPiano As New Chiamata(Of IntPtr)(AddressOf ElaboraPrimoPiano, finestra)
                    EseguiSuOperazioni("primoPiano", AddressOf chiamataPrimoPiano.Esegui, sostituisci:=True)
                Case TipoEventoLettore.CambioValore
                    EseguiSuOperazioni("cambioValore", AddressOf ElaboraCambioValore, sostituisci:=True)
                Case TipoEventoLettore.SelezioneTesto
                    ProgrammaOperazione("selezioneTesto", 15, AddressOf ElaboraSelezioneEsterna, True)
                Case TipoEventoLettore.Tasto
                    AccodaTasto(evento)
                Case TipoEventoLettore.RegioneViva
                    If _menuVirtualeMessaggi AndAlso TastoRecente(3000) Then Exit Select
                    Dim differita As Boolean = False
                    SyncLock _bloccoRegioniVive
                        differita = MillisecondiDa(Volatile.Read(_ultimaElaboraRegioneViva)) < 150L OrElse _regioniVivePendenti.Count > 0
                        If differita Then
                            _regioniVivePendenti.Enqueue(evento)
                            While _regioniVivePendenti.Count > 4
                                _regioniVivePendenti.Dequeue()
                            End While
                        End If
                    End SyncLock
                    If Not differita Then
                        Volatile.Write(_ultimaElaboraRegioneViva, Environment.TickCount)
                        Dim eventoRegione As EventoLettore = evento
                        Dim chiamataRegione As New Chiamata(Of EventoLettore)(AddressOf ElaboraRegioneViva, eventoRegione)
                        EseguiSuOperazioni("regioneViva", AddressOf chiamataRegione.Esegui, sostituisci:=True)
                    End If
                Case TipoEventoLettore.SelezioneElemento
                    If evento.Elemento Is Nothing Then Exit Select
                    If InCommutatore(NativoLettore.GetForegroundWindow()) Then
                        Dim elementoCommutatore As IUIAutomationElement = evento.Elemento
                        Dim chiamataCommutatore As New Chiamata(Of IUIAutomationElement)(AddressOf AnnunciaCommutatoreEvento, elementoCommutatore)
                        EseguiLetturaCommutatore(AddressOf chiamataCommutatore.Esegui)
                    Else
                        AccodaSelezioneElemento(evento)
                    End If
                Case TipoEventoLettore.FocusMsaa
                    Dim eventoMsaa As EventoLettore = evento
                    Dim chiamataMsaa As New Chiamata(Of EventoLettore)(AddressOf ElaboraFocusMsaa, eventoMsaa)
                    ProgrammaOperazione("focusMsaa", 15, AddressOf chiamataMsaa.Esegui, True)
                Case TipoEventoLettore.MenuTeamsTrovato, TipoEventoLettore.MenuBarraTrovato
                    Dim eventoMenu As EventoLettore = evento
                    Dim chiamataMenu As New Chiamata(Of EventoLettore)(AddressOf ElaboraMenuVirtualeTrovato, eventoMenu)
                    EseguiSuOperazioni("menuTrovato", AddressOf chiamataMenu.Esegui, sostituisci:=True)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraEvento: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraMenuVirtualeTrovato(evento As EventoLettore)
        Try
            If _menuVirtualeMessaggi Then ChiudiMenuVirtuale(False)
            If MenuVirtualeAttivo OrElse Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            If evento Is Nothing OrElse evento.Elemento Is Nothing Then Return
            If ApriMenuVirtualeDaContenitore(evento.Elemento) Then
                ModalitaMenu()
                AnnunciaVoceMenuVirtuale(True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraMenuVirtualeTrovato: " & ex.Message)
        End Try
    End Sub

    Private Function NomeEseguibile(pid As UInteger) As String
        Dim nome As String = Nothing
        If _eseguibili.TryGetValue(pid, nome) Then Return nome
        nome = ""
        Dim processo As IntPtr = IntPtr.Zero
        Try
            If pid <> 0UI Then
                processo = NativoLettore.OpenProcess(&H1000UI, False, pid)
                If processo <> IntPtr.Zero Then
                    Dim percorso As New StringBuilder(1024)
                    Dim dimensione As UInteger = CUInt(percorso.Capacity)
                    If NativoLettore.QueryFullProcessImageName(processo, 0UI, percorso, dimensione) Then
                        nome = System.IO.Path.GetFileNameWithoutExtension(percorso.ToString()).ToLowerInvariant()
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeEseguibile: " & ex.Message)
        Finally
            If processo <> IntPtr.Zero Then NativoLettore.CloseHandle(processo)
        End Try
        _eseguibili(pid) = nome
        Return nome
    End Function

    Private Function EFinestraNotifiche(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim classe As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse)
            If classe = "" Then
                Try
                    classe = elemento.CurrentClassName
                Catch exClasse As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.EFinestraNotifiche", exClasse)
                    If ElementoAssente(exClasse) Then Return False
                End Try
            End If
            If classe = "ToastChildWindow" OrElse classe = "NotificationToast" OrElse classe = "ToastContentHost" Then Return True
            Dim pid As Integer = _descrittore.IdProcesso(elemento)
            If pid <= 0 Then
                Try
                    pid = elemento.CurrentProcessId
                Catch exPid As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.EFinestraNotifiche", exPid)
                    If ElementoAssente(exPid) Then Return False
                End Try
            End If
            If pid <= 0 Then Return False
            Dim eseguibile As String = NomeEseguibile(CUInt(pid))
            Return eseguibile = "shellexperiencehost" OrElse eseguibile = "shellhost"
        Catch ex As Exception
            If Not ElementoAssente(ex) Then log.ScriviLog("WGLettoreSchermo.EFinestraNotifiche: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TestoAvviso(elemento As IUIAutomationElement) As String
        Try
            Dim parti As New List(Of String)()
            Dim nome As String = _descrittore.NomeCorrente(elemento)
            If nome <> "" AndAlso nome.IndexOf("notific", StringComparison.CurrentCultureIgnoreCase) < 0 Then parti.Add(nome)
            If _automazione IsNot Nothing Then
                Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloTesto)
                Dim testi As IUIAutomationElementArray = elemento.FindAllBuildCache(TreeScope.TreeScope_Descendants, condizione, _richiestaCache)
                If testi IsNot Nothing Then
                    For indice As Integer = 0 To Math.Min(testi.Length, 10) - 1
                        Dim testo As String = _descrittore.Nome(testi.GetElement(indice))
                        If testo <> "" AndAlso Not parti.Contains(testo) Then parti.Add(testo)
                    Next
                End If
            End If
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoAvviso: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ElaboraAvviso(evento As EventoLettore)
        Try
            If Volatile.Read(_leggiAvvisi) = 0 Then Return
            Dim elemento As IUIAutomationElement = evento.Elemento
            If elemento Is Nothing Then Return
            If evento.FinestraAperta Then
                If Not EFinestraNotifiche(elemento) Then Return
                Dim maniglia As IntPtr = IntPtr.Zero
                Try
                    maniglia = elemento.CurrentNativeWindowHandle
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("WGLettoreSchermo.ElaboraAvviso", exAtteso)
                End Try
                If maniglia <> IntPtr.Zero AndAlso maniglia = NativoLettore.GetForegroundWindow() Then Return
            End If
            elemento = CompletaCache(elemento)
            Dim richiesta As New RichiestaAvviso(Me, elemento)
            ProvaAvviso(richiesta)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraAvviso: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraRegioneViva(evento As EventoLettore)
        SyncLock _bloccoRegioniVive
            ElaboraRegioneVivaSenzaBlocco(evento)
        End SyncLock
    End Sub

    Private Sub ElaboraRegioneVivaSenzaBlocco(evento As EventoLettore)
        Try
            If Volatile.Read(_leggiRegioniVive) = 0 Then Return
            If OttieniModuloForegroundApp().AllAvviso(evento) Then Return
            Dim sorgente As IUIAutomationElement = evento.Elemento
            If sorgente Is Nothing OrElse _descrittore Is Nothing Then Return
            Dim pidSorgente As Integer = _descrittore.IdProcesso(sorgente)
            If EProcessoWinGabriel(CUInt(pidSorgente)) Then Return
            Dim pidPrimoPiano As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(NativoLettore.GetForegroundWindow(), pidPrimoPiano)
            If CInt(pidPrimoPiano) <> pidSorgente Then Return
            Dim regione As IUIAutomationElement = sorgente
            Dim impostazione As Integer = _descrittore.ImpostazioneVita(regione)
            If impostazione = 0 AndAlso _walkerControllo IsNot Nothing Then
                Dim antenato As IUIAutomationElement = sorgente
                For salto As Integer = 1 To 6
                    antenato = _walkerControllo.GetParentElementBuildCache(antenato, _richiestaCache)
                    If antenato Is Nothing Then Exit For
                    Dim valore As Integer = _descrittore.ImpostazioneVita(antenato)
                    If valore > 0 Then
                        regione = antenato
                        impostazione = valore
                        Exit For
                    End If
                Next
            End If
            If impostazione = 0 Then impostazione = 1
            If _descrittore.ValoreAria(regione, "busy") = "true" Then Return
            Dim ruolo As String = _descrittore.RuoloAria(regione)
            Dim atomico As String = _descrittore.ValoreAria(regione, "atomic")
            Dim atomica As Boolean = atomico = "true" OrElse ((ruolo = "alert" OrElse ruolo = "status") AndAlso atomico <> "false")
            Dim righe As List(Of String) = RigheRegioneViva(regione)
            If righe.Count = 0 Then Return
            Dim chiave As String = ChiaveElemento(regione)
            Dim precedenti As List(Of String) = Nothing
            If chiave <> "" Then _regioniVive.TryGetValue(chiave, precedenti)
            Dim daLeggere As New List(Of String)()
            If precedenti Is Nothing Then
                Dim inizio As Integer = If(atomica, 0, Math.Max(0, righe.Count - 3))
                For indice As Integer = inizio To righe.Count - 1
                    daLeggere.Add(righe(indice))
                Next
            ElseIf atomica Then
                If Not ListeUguali(righe, precedenti) Then daLeggere.AddRange(righe)
            Else
                For Each riga As String In righe
                    If Not precedenti.Contains(riga) Then daLeggere.Add(riga)
                Next
            End If
            If chiave <> "" Then
                If _regioniVive.Count > 300 Then _regioniVive.Clear()
                _regioniVive(chiave) = righe
            End If
            If daLeggere.Count = 0 Then Return
            Dim testo As String = String.Join(". ", daLeggere)
            If testo.Length > 600 Then testo = testo.Substring(0, 600)
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(testo, _ultimaRegioneViva, StringComparison.CurrentCulture) AndAlso adesso - _ultimaRegioneVivaTick < 1500 Then Return
            _ultimaRegioneViva = testo
            _ultimaRegioneVivaTick = adesso
            Parla(testo, impostazione >= 2)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraRegioneViva: " & ex.Message)
        End Try
    End Sub

    Private Function RigheRegioneViva(regione As IUIAutomationElement) As List(Of String)
        Dim righe As New List(Of String)()
        Try
            If _automazione IsNot Nothing Then
                Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloTesto)
                Dim testi As IUIAutomationElementArray = regione.FindAllBuildCache(TreeScope.TreeScope_Descendants, condizione, _richiestaCache)
                If testi IsNot Nothing Then
                    For indice As Integer = Math.Max(0, testi.Length - 60) To testi.Length - 1
                        Dim testo As String = _descrittore.Nome(testi.GetElement(indice))
                        If testo = "" Then Continue For
                        If testo.Length > 300 Then testo = testo.Substring(0, 300)
                        If righe.Count = 0 OrElse Not String.Equals(righe(righe.Count - 1), testo, StringComparison.CurrentCulture) Then righe.Add(testo)
                    Next
                End If
            End If
            If righe.Count = 0 Then
                Dim nome As String = _descrittore.NomeCorrente(regione)
                If nome <> "" Then righe.Add(If(nome.Length > 300, nome.Substring(0, 300), nome))
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RigheRegioneViva: " & ex.Message)
        End Try
        Return righe
    End Function

    Private Function ListeUguali(primo As List(Of String), secondo As List(Of String)) As Boolean
        If primo.Count <> secondo.Count Then Return False
        For indice As Integer = 0 To primo.Count - 1
            If Not String.Equals(primo(indice), secondo(indice), StringComparison.CurrentCulture) Then Return False
        Next
        Return True
    End Function

    Private Function AvvisoDaIgnorareInAlbero(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing Then Return False
            Select Case evento.Tipo
                Case TipoEventoLettore.RegioneViva, TipoEventoLettore.ProprietaCambiata, TipoEventoLettore.TestoModificato, TipoEventoLettore.Tooltip, TipoEventoLettore.Notifica
                Case TipoEventoLettore.Avviso
                    If evento.FinestraAperta Then Return False
                Case Else
                    Return False
            End Select
            Return OttieniModuloForegroundApp().AllAvviso(evento)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AvvisoDaIgnorareInAlbero", exAtteso)
            Return False
        End Try
    End Function

    Private Sub ElaboraRegioniVivePendenti()
        Try
            Dim evento As EventoLettore = Nothing
            SyncLock _bloccoRegioniVive
                If _regioniVivePendenti.Count = 0 Then Return
                If MillisecondiDa(Volatile.Read(_ultimaElaboraRegioneViva)) < 150L Then Return
                If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then
                    _regioniVivePendenti.Clear()
                    Return
                End If
                evento = _regioniVivePendenti.Dequeue()
                Volatile.Write(_ultimaElaboraRegioneViva, Environment.TickCount)
            End SyncLock
            If evento IsNot Nothing Then
                Dim chiamataRegione As New Chiamata(Of EventoLettore)(AddressOf ElaboraRegioneViva, evento)
                EseguiSuOperazioni("regioneViva", AddressOf chiamataRegione.Esegui, sostituisci:=True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraRegioniVivePendenti: " & ex.Message)
        End Try
    End Sub

    Private Sub SincronizzaPuntoPartenzaFuoco()
        Try
            Dim elemento As IUIAutomationElement = _sincronizzaFuocoElemento
            If elemento Is Nothing Then Return
            If TastoRecente(60) Then Return
            _sincronizzaFuocoElemento = Nothing
            If _navigazione Is Nothing OrElse Not _navAttiva Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            SilenziaValore(500)
            SilenziaSelezione(500)
            _navigazione.ImpostaPuntoPartenzaFuoco(elemento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SincronizzaPuntoPartenzaFuoco: " & ex.Message)
        End Try
    End Sub

    Private Function StessaVoce(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            If StessoElemento(primo, secondo) Then Return True
            Dim nomePrimo As String = _descrittore.Nome(primo)
            If nomePrimo = "" OrElse Not String.Equals(nomePrimo, _descrittore.Nome(secondo), StringComparison.Ordinal) Then Return False
            If _descrittore.TipoControllo(primo) <> _descrittore.TipoControllo(secondo) Then Return False
            If _descrittore.IdProcesso(primo) <> _descrittore.IdProcesso(secondo) Then Return False
            Dim primoRett As tagRECT = primo.CachedBoundingRectangle
            Dim secondoRett As tagRECT = secondo.CachedBoundingRectangle
            If primoRett.right - primoRett.left <= 0 OrElse primoRett.bottom - primoRett.top <= 0 Then Return False
            Return primoRett.left = secondoRett.left AndAlso primoRett.top = secondoRett.top AndAlso primoRett.right = secondoRett.right AndAlso primoRett.bottom = secondoRett.bottom
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.StessaVoce", exAtteso)
            Return False
        End Try
    End Function

    Private Function SelezioneSuperata(evento As EventoLettore) As Boolean
        If evento Is Nothing OrElse evento.Epoca = 0 Then Return False
        Return evento.Epoca <> Volatile.Read(_epocaSelezione) OrElse evento.Sequenza <> Volatile.Read(_sequenzaFocus)
    End Function

    Private Shared Function MillisecondiDa(tick As Integer) As Long
        Dim trascorso As Long = CLng(Environment.TickCount) - CLng(tick)
        If trascorso < 0L Then trascorso += 4294967296L
        Return trascorso
    End Function

    Private Function MovimentoVirtualeRecente(millisecondi As Integer) As Boolean
        Return MillisecondiDa(Volatile.Read(_ultimoMovimentoVirtuale)) < CLng(millisecondi)
    End Function

    Private Sub ElaboraFocus(evento As EventoLettore)
        Try
            If FocusSilenziato() Then Return
            If evento.Sequenza <> Volatile.Read(_sequenzaFocus) Then Return
            _messaggioGiaAnnunciato = False
            If ProvaRigaNativaVeloce(IntPtr.Zero, ListaDaElemento(evento.Elemento)) Then Return
            SegnaListaNativaConFuoco()
            Dim elemento As IUIAutomationElement = evento.Elemento
            If elemento Is Nothing Then Return
            If InCommutatore(NativoLettore.GetForegroundWindow()) Then
                Dim chiamataCommutatore As New Chiamata(Of IUIAutomationElement)(AddressOf LeggiVoceCommutatore, elemento)
                EseguiLetturaCommutatore(AddressOf chiamataCommutatore.Esegui)
                Return
            End If
            If FuocoDaRiquadroGecko() Then Return
            If EVoceMenuNativa(elemento) AndAlso AnnunciaVoceMenuNativa(evento, elemento) Then Return
            Dim vivo As IUIAutomationElement = Nothing
            If FuocoDaEvento(elemento) Then
                vivo = CompletaCache(elemento)
            Else
                vivo = ElementoConFocus(False)
            End If
            If Not EVoceMenuNativa(elemento) Then _menuNativoAperto = False
            Dim stessoVivo As Boolean = vivo IsNot Nothing AndAlso StessoElemento(vivo, elemento)
            Dim voceMenuEvento As Boolean = Not stessoVivo AndAlso EVoceMenuNativa(elemento)
            If vivo IsNot Nothing AndAlso Not voceMenuEvento AndAlso (stessoVivo OrElse Not EAntenato(vivo, elemento)) Then
                elemento = vivo
            Else
                elemento = CompletaCache(elemento)
            End If
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            If Not ElementoDelPrimoPiano(elemento) Then Return
            If ReindirizzaVistaWebWx(elemento) Then Return
            If FuocoSpostatoDallaPagina(elemento) Then Return
            If RientroConCursore(elemento) Then
                RientraConCursore(elemento)
                Return
            End If
            If _menuVirtualeMessaggi AndAlso (EVoceDiMenu(elemento) OrElse EContenitoreMenu(elemento)) Then ChiudiMenuVirtuale(False)
            If Not MenuVirtualeAttivo AndAlso ProvaAprireElencoMessaggiTeams(elemento) Then
                ModalitaMenu()
                AnnunciaVoceMenuVirtuale(_elencoMessaggiNuovo)
                AggiornaEventiFuoco(elemento)
                Return
            End If
            If Not MenuVirtualeAttivo AndAlso Not FuocoDalCursoreVirtuale(elemento) AndAlso EVirtualePerMenu(elemento) AndAlso (EVoceDiMenu(elemento) OrElse EContenitoreMenu(elemento)) Then
                If ProvaAprireMenuVirtuale(elemento) Then
                    ModalitaMenu()
                    AnnunciaVoceMenuVirtuale(True)
                    AggiornaEventiFuoco(elemento)
                    Return
                End If
            End If
            If Not MenuVirtualeAttivo AndAlso AltRecente(1500) AndAlso DescrittoreElementi.PrimoPianoTeams() AndAlso ElementoWeb(elemento) Then
                If ProvaAprireMenuVirtuale(elemento, True) Then
                    ModalitaMenu()
                    AnnunciaVoceMenuVirtuale(True)
                    AggiornaEventiFuoco(elemento)
                    Return
                End If
            End If
            Dim modulo As AppModuleLettore = OttieniModuloApp(elemento)
            If modulo.AllFocusSoppresso(elemento) Then Return
            Dim reindirizzato As IUIAutomationElement = modulo.ReindirizzaFuoco(elemento)
            If reindirizzato Is Nothing Then reindirizzato = FuocoNelFiglio(elemento)
            If reindirizzato IsNot Nothing Then elemento = reindirizzato
            If MenuVirtualeAttivo AndAlso SeguiFuocoMenuVirtuale(elemento) Then
                ModalitaMenu()
                Return
            End If
            If _navAttiva AndAlso _navigazione IsNot Nothing AndAlso MovimentoVirtualeRecente(400) AndAlso _navigazione.EDocumentoORadice(elemento) Then Return
            If _navAttiva AndAlso _navigazione IsNot Nothing AndAlso Volatile.Read(_tastiVirtuali) = 1 AndAlso _navigazione.NumeroVoci > 0 AndAlso _navigazione.ERadice(elemento) AndAlso _navigazione.Finestra = NativoLettore.GetForegroundWindow() Then
                RitornaSullaPagina(True)
                Return
            End If
            Dim originale As IUIAutomationElement = elemento
            Dim voce As IUIAutomationElement = LetturaAlberi.ElementoDaAnnunciare(elemento, _descrittore, _walkerControllo, _richiestaCache)
            If voce IsNot Nothing Then elemento = voce
            Dim nome As String = _descrittore.Nome(elemento)
            Dim variabile As String = _descrittore.ParteVariabile(elemento)
            If StessaVoce(elemento, _ultimoElemento) AndAlso nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile Then Return
            Dim testo As String = DescriviConColonne(elemento)
            testo = AggiungiNonSelezionato(elemento, testo)
            testo = AggiungiPosizioneVoceElencoWeb(elemento, testo)
            Dim voceApp As Boolean = modulo.EVoceApp(elemento)
            Dim interno As Boolean = Not voceApp AndAlso MovimentoInternoElenco(_ultimoElemento, elemento)
            If voceApp OrElse modulo.EElencoApp(elemento) Then
                testo = AggiungiPosizioneFinale(elemento, modulo.AllFocus(elemento, testo, _ultimoElemento))
            Else
                testo = modulo.AllFocus(elemento, testo, _ultimoElemento)
                If Not interno Then
                    Dim contesto As String = ContestoNuovo(elemento)
                    If contesto <> "" Then testo = contesto & ", " & testo
                    testo = CompletaAnnuncio(elemento, testo)
                End If
            End If
            If evento.Sequenza <> Volatile.Read(_sequenzaFocus) Then Return
            _messaggioGiaAnnunciato = testo.StartsWith("messaggio, ", StringComparison.CurrentCultureIgnoreCase)
            _ultimoAnnuncioAlbero = testo
            MemorizzaElemento(elemento, nome, variabile)
            _revisione = elemento
            _uiaFerma = Nothing
            _ultimoFocusTick = Environment.TickCount
            If FuocoDiAltraFinestra(evento, elemento) Then Return
            Dim messaggioVuoto As String = ""
            Dim senzaVoci As Boolean = ContenitoreVuoto(elemento, messaggioVuoto)
            If senzaVoci Then
                If messaggioVuoto <> "" Then
                    testo = If(testo = "", messaggioVuoto, testo & ", " & messaggioVuoto)
                ElseIf testo.IndexOf("vuot", StringComparison.CurrentCultureIgnoreCase) < 0 Then
                    testo = If(testo = "", "vuoto", testo & ", vuoto")
                End If
                Parla(testo, True)
            ElseIf Not TitoloGiaAnnunciato(elemento, nome) AndAlso Not NomeDesktop(nome) AndAlso Not NomeEseguibileElemento(elemento, nome) AndAlso Not TitoloFinestraRipetuto(elemento, nome) AndAlso Not VoceMenuGiaDetta(nome) Then
                Parla(testo, True)
            End If
            If _campi IsNot Nothing Then _istantanea = _campi.Cattura(elemento)
            AggiornaContestoWeb(originale)
            AggiornaEventiFuoco(originale)
            If Not interno Then modulo.AllFocusFine(originale)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaTitoloPrimoPiano(finestra As IntPtr)
        Try
            If FocusSilenziato() OrElse finestra = IntPtr.Zero Then Return
            If NativoLettore.GetForegroundWindow() <> finestra Then Return
            If InCommutatore(finestra) Then Return
            ApplicaProfilo(finestra)
            If FinestraDesktop(finestra) AndAlso ProvaRigaNativaVeloce(finestra) Then Return
            If OttieniModuloApp(finestra).PrimoPianoIgnorato(finestra) Then Return
            If SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim titoloGiaDetto As Boolean = CommutatoreAppenaDetto()
            Dim titolo As String = TitoloPrimoPiano(finestra)
            Dim parti As List(Of String) = PartiTitoloPrimoPiano(finestra, titolo)
            If titolo <> "" AndAlso EBrowser(finestra) Then
                Dim titoloPagina As String = OttieniModuloApp(finestra).TitoloPagina(titolo)
                If titoloPagina <> "" Then
                    _ultimoTitoloPagina = titoloPagina
                    _ultimoTitoloTick = Environment.TickCount
                End If
            End If
            _ultimoFocusTick = Environment.TickCount
            If parti.Count = 0 Then Return
            Interlocked.Exchange(_commutatoreAnnunciato, 0)
            If Not titoloGiaDetto Then ParlaPriorita(String.Join(", ", parti), 2)
            Dim titoloPaginaCorrente As String = ""
            If titolo <> "" AndAlso EBrowser(finestra) Then titoloPaginaCorrente = OttieniModuloApp(finestra).TitoloPagina(titolo)
            RegistraTitoliFinestra(titolo, titoloPaginaCorrente, String.Join(", ", parti), NomeApplicazione(finestra))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaTitoloPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Private Function TitoloPrimoPiano(finestra As IntPtr) As String
        Try
            Dim pidFinestra As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pidFinestra)
            Dim titolo As String = If(FinestraDesktop(finestra), "", TitoloFinestra(finestra))
            If TitoloEseguibile(pidFinestra, titolo) Then titolo = ""
            Return SenzaEstensioneEseguibile(titolo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloPrimoPiano: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function PartiTitoloPrimoPiano(finestra As IntPtr, titolo As String) As List(Of String)
        Dim parti As New List(Of String)()
        Try
            If titolo = "" Then
                If FinestraDesktop(finestra) OrElse FinestraDiSistema(finestra) OrElse InCommutatore(finestra) Then Return parti
                Dim applicazioneSola As String = NomeApplicazione(finestra)
                If applicazioneSola = "" OrElse applicazioneSola = "*" Then
                    Dim pidSolo As UInteger = 0UI
                    NativoLettore.GetWindowThreadProcessId(finestra, pidSolo)
                    applicazioneSola = NomeFileEseguibile(pidSolo)
                End If
                If applicazioneSola <> "" Then parti.Add(applicazioneSola)
                Return parti
            End If
            Dim pidFinestra As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pidFinestra)
            If NomeEseguibile(pidFinestra) = "explorer" Then
                parti.Add(titolo)
                Dim nomeFinestra As String = NomeApplicazione(finestra)
                If nomeFinestra <> "" AndAlso titolo.IndexOf(nomeFinestra, StringComparison.CurrentCultureIgnoreCase) < 0 Then parti.Add(nomeFinestra)
            Else
                If Not FinestraDiSistema(finestra) Then
                    Dim applicazione As String = NomeApplicazione(finestra)
                    If applicazione <> "" AndAlso titolo.IndexOf(applicazione, StringComparison.CurrentCultureIgnoreCase) < 0 Then parti.Add(applicazione)
                End If
                parti.Add(titolo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PartiTitoloPrimoPiano: " & ex.Message)
        End Try
        Return parti
    End Function

    ' Nei testi della finestra di dialogo ci sono anche le etichette, e il controllo che ha il fuoco ne prende il nome: quelle parti si dicono una volta sola,
    ' con il controllo.
    Private Shared Function TogliPartiGiaNellaVoce(testi As String, voce As String) As String
        Try
            If String.IsNullOrWhiteSpace(testi) OrElse String.IsNullOrWhiteSpace(voce) Then Return testi
            Dim tenute As New List(Of String)()
            For Each parte As String In testi.Split(New String() {", "}, StringSplitOptions.RemoveEmptyEntries)
                Dim pulita As String = parte.Trim()
                If pulita = "" Then Continue For
                If pulita.Length >= 3 AndAlso voce.IndexOf(pulita, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Continue For
                tenute.Add(pulita)
            Next
            Return String.Join(", ", tenute)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TogliPartiGiaNellaVoce", ex)
            Return testi
        End Try
    End Function

    Private Sub ElaboraPrimoPiano(finestra As IntPtr)
        Try
            If FocusSilenziato() Then Return
            If finestra = IntPtr.Zero Then Return
            _catena = Nothing
            If InCommutatore(finestra) Then
                EseguiLetturaCommutatore(AddressOf LeggiCommutatoreDue)
                Return
            End If
            If NativoLettore.GetForegroundWindow() <> finestra Then Return
            ' Sul desktop l'ingresso lo dice la lettura veloce dal controllo della lista: qui, con UI Automation, sarebbe lento e farebbe un secondo annuncio.
            If FinestraDesktop(finestra) AndAlso (MillisecondiDa(_listaNativaAnnuncioTick) < 3000L OrElse ProvaRigaNativaVeloce(finestra)) Then Return
            If NativoLettore.IsHungAppWindow(finestra) Then Return
            If OttieniModuloApp(finestra).PrimoPianoIgnorato(finestra) Then Return
            Try
                Dim orologioPrimoPiano As Stopwatch = Stopwatch.StartNew()
                Dim focusPrecedenteTick As Integer = Volatile.Read(_ultimoFocusTick)
                Dim pidFinestra As UInteger = 0UI
                NativoLettore.GetWindowThreadProcessId(finestra, pidFinestra)
                Dim titolo As String = TitoloPrimoPiano(finestra)
                Dim resto As New List(Of String)()
                Dim testiDialogo As String = ""
                If EFinestraDialogo(finestra) Then testiDialogo = TestiFinestra(finestra, titolo)
                Dim tardi As Boolean = orologioPrimoPiano.ElapsedMilliseconds > 400L
                Dim elemento As IUIAutomationElement = ElementoConFocus()
                If orologioPrimoPiano.ElapsedMilliseconds > 600L Then tardi = True
                Dim valido As Boolean = elemento IsNot Nothing AndAlso _descrittore.IdProcesso(elemento) = CInt(pidFinestra) AndAlso ElementoInFinestra(elemento, finestra)
                Dim rientro As Boolean = valido AndAlso RientroConCursore(elemento)
                Dim daRiannunciare As Boolean = False
                If valido Then
                    Dim voce As IUIAutomationElement = LetturaAlberi.ElementoDaAnnunciare(elemento, _descrittore, _walkerControllo, _richiestaCache)
                    If voce Is Nothing Then voce = elemento
                    Dim nomeVoce As String = _descrittore.Nome(voce)
                    Dim parteVoce As String = _descrittore.ParteVariabile(voce)
                    Dim giaDetta As Boolean = rientro OrElse (MillisecondiDa(focusPrecedenteTick) < 1200L AndAlso StessaVoce(voce, _ultimoElemento) AndAlso nomeVoce = _ultimoNome AndAlso parteVoce = _ultimaParteVariabile)
                    Dim moduloVoce As AppModuleLettore = OttieniModuloApp(voce)
                    Dim voceModulo As Boolean = moduloVoce.EVoceApp(voce) OrElse moduloVoce.EElencoApp(voce)
                    Dim contesto As String = If(voceModulo, "", ContestoNuovo(voce))
                    If Not giaDetta Then
                        If contesto <> "" Then resto.Add(contesto)
                        Dim descrizione As String = DescriviConColonne(voce)
                        If voceModulo Then descrizione = AggiungiPosizioneFinale(voce, moduloVoce.AllFocus(voce, descrizione, _ultimoElemento))
                        If descrizione <> "" AndAlso Not String.Equals(descrizione, titolo, StringComparison.CurrentCulture) Then resto.Add(descrizione)
                        testiDialogo = TogliPartiGiaNellaVoce(testiDialogo, descrizione)
                        _ultimoAnnuncioAlbero = descrizione
                    End If
                    If tardi AndAlso Not giaDetta AndAlso Not ElementoWeb(voce) Then
                        daRiannunciare = True
                    Else
                        MemorizzaElemento(voce, nomeVoce, parteVoce)
                        _revisione = voce
                        _uiaFerma = Nothing
                    End If
                End If
                If testiDialogo <> "" Then resto.Insert(0, testiDialogo)
                If resto.Count > 0 AndAlso Not tardi Then Parla(String.Join(", ", resto), False)
                If daRiannunciare Then EseguiSuOperazioni("fuocoDopoPrimoPiano", AddressOf AnnunciaFuocoSeNuovo, sostituisci:=True)
                If valido Then
                    If _campi IsNot Nothing Then _istantanea = _campi.Cattura(elemento)
                    If rientro Then _navForzataElemento = elemento
                    AggiornaContestoWeb(elemento)
                    AggiornaEventiFuoco(elemento)
                    If rientro AndAlso Not tardi Then AnnunciaRientro()
                End If
                VerificaFinestra(finestra)
            Catch exInterno As Exception
                If Not ElementoAssente(exInterno) AndAlso Not TimeoutOperazione(exInterno) Then log.ScriviLog("WGLettoreSchermo.ElaboraPrimoPiano (lettura): " & exInterno.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraCambioValore()
        Try
            If ValoreSilenziato() Then Return
            If _ultimoElemento Is Nothing Then Return
            If MenuNativoInCorso() Then Return
            If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) < 300L Then Return
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing Then Return
            If Not StessoElemento(elemento, _ultimoElemento) Then Return
            If OttieniModuloApp(elemento).EventoSoppresso(elemento) Then Return
            Dim nome As String = _descrittore.Nome(elemento)
            Dim variabile As String = _descrittore.ParteVariabile(elemento)
            If nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile Then Return
            If Environment.TickCount - Volatile.Read(_ultimoFocusTick) < 250 Then
                MemorizzaElemento(elemento, nome, variabile)
                Return
            End If
            If nome = _ultimoNome AndAlso ValoreCasellaGiaDetto(elemento, variabile) Then
                MemorizzaElemento(elemento, nome, variabile)
                Return
            End If
            Dim testo As String
            If nome <> _ultimoNome Then
                testo = _descrittore.Descrivi(elemento)
            Else
                testo = variabile
            End If
            MemorizzaElemento(elemento, nome, variabile)
            If _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloCasellaCombinata AndAlso nome = _ultimoNome Then
                _valoreCasellaDetto = _descrittore.SceltaCasella(elemento)
                If _valoreCasellaDetto = "" Then _valoreCasellaDetto = If(_descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore), "")
                Volatile.Write(_valoreCasellaTick, Environment.TickCount)
            End If
            Parla(testo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraCambioValore: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraTasto(evento As EventoLettore)
        Try
            If evento Is Nothing OrElse _campi Is Nothing Then Return
            If evento.Azione = AzioneTastoLettore.SoloModulo Then
                TastoAlModulo(evento)
                Return
            End If
            If evento.Azione = AzioneTastoLettore.Copia Then
                If InVirtuale() Then
                    CopiaVirtuale()
                Else
                    AnnunciaCopiaCampo()
                End If
                Return
            End If
            If evento.Azione = AzioneTastoLettore.NavigazioneWeb Then
                ElaboraNavigazioneVirtuale(evento)
                Return
            End If
            If evento.Azione = AzioneTastoLettore.SelezioneVoce Then
                AnnunciaSelezioneVoce()
                Return
            End If
            If _popupGecko AndAlso evento.Azione = AzioneTastoLettore.Navigazione Then Return
            If MenuNativoInCorso() Then
                If evento.Azione = AzioneTastoLettore.Navigazione Then
                    AttendiVoceMenu(evento)
                ElseIf evento.Azione = AzioneTastoLettore.Modifica Then
                    If Not evento.EcoDetto Then EcoFuoriDaiCampi(evento)
                End If
                Return
            End If
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing Then Return
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            Dim moduloTasto As AppModuleLettore = OttieniModuloApp(elemento)
            Dim sostituto As IUIAutomationElement = moduloTasto.ElementoTasto(elemento)
            If sostituto IsNot Nothing Then elemento = sostituto
            If moduloTasto.AllTasto(evento, elemento) Then Return
            Dim web As Boolean = ElementoWeb(elemento)
            If _menuNativoAperto AndAlso evento.Azione = AzioneTastoLettore.Navigazione AndAlso Not EVoceMenuNativa(elemento) AndAlso EVoceMenuNativa(_ultimoElemento) Then Return
            Dim haTesto As Boolean = _campi.HaTesto(elemento) OrElse _descrittore.EditabileConTesto(elemento) OrElse _descrittore.ECampoScrittura(elemento)
            If Not haTesto AndAlso _navigazione IsNot Nothing AndAlso _navigazione.RichiedeScrittura(elemento) Then haTesto = True
            If haTesto AndAlso Not _descrittore.ECampoScrittura(elemento) AndAlso Not _descrittore.Protetto(elemento) AndAlso NelControlloComposito(elemento) AndAlso Not (_navigazione IsNot Nothing AndAlso _navigazione.RichiedeScrittura(elemento)) Then haTesto = False
            If haTesto AndAlso Not _descrittore.ECampoScrittura(elemento) AndAlso Not (_navigazione IsNot Nothing AndAlso _navigazione.RichiedeScrittura(elemento)) AndAlso VoceNonScrivibile(elemento) Then haTesto = False
            If haTesto Then Volatile.Write(_ultimoAnnuncioCampo, Environment.TickCount) Else Volatile.Write(_ultimoAnnuncioCampo, 0)
            Dim cancella As Boolean = evento.CodiceTasto = &H8 OrElse evento.CodiceTasto = &H2E OrElse (evento.Ctrl AndAlso evento.CodiceTasto = &H58)
            Dim chiedeSelezione As Boolean = evento.Azione = AzioneTastoLettore.Selezione AndAlso Not cancella
            If Not haTesto Then
                If evento.Azione = AzioneTastoLettore.Modifica Then
                    If Not evento.EcoDetto Then EcoFuoriDaiCampi(evento)
                    Return
                End If
                If chiedeSelezione Then
                    AnnunciaSelezioneElemento(elemento, web)
                ElseIf evento.Azione = AzioneTastoLettore.Navigazione Then
                    If evento.CodiceTasto = &H28 AndAlso Not evento.Ctrl AndAlso Not evento.Alt AndAlso Not evento.Maiuscolo AndAlso EVoceBarraMenu(elemento) Then
                        ProgrammaAperturaMenuDaBarra(elemento)
                    End If
                    ControllaFuocoDopoTasto(elemento, evento.Ctrl)
                End If
                Return
            End If
            If Not StessoElemento(elemento, _ultimoElemento) Then
                AnnunciaTastoNuovoElemento(elemento, evento, chiedeSelezione, cancella)
                Return
            End If
            Dim precedente As IstantaneaCampo = _istantanea
            If precedente Is Nothing Then precedente = New IstantaneaCampo()
            Dim paziente As Boolean = chiedeSelezione OrElse cancella OrElse evento.Azione = AzioneTastoLettore.Modifica
            Dim attuale As IstantaneaCampo = AttendiCampo(elemento, precedente, web, paziente, evento.CodiceTasto = &H2E OrElse evento.CodiceTasto = &H8)
            Dim testo As String = ""
            Dim previsto As IstantaneaCampo = Nothing
            Dim fermo As Boolean = Not ModelloSuperato(precedente, attuale)
            If chiedeSelezione Then
                If fermo OrElse attuale.Selezione = "" OrElse attuale.Selezione = precedente.Selezione Then
                    Dim diretta As String = _campi.TestoSelezionato(elemento)
                    If diretta Is Nothing Then diretta = ""
                    If diretta <> "" AndAlso diretta <> precedente.Selezione AndAlso Not diretta.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then
                        attuale.Selezione = diretta
                        fermo = False
                        _uiaFerma = Nothing
                    ElseIf diretta.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) AndAlso diretta <> precedente.Selezione Then
                        testo = diretta
                        fermo = False
                        _uiaFerma = Nothing
                    End If
                End If
                If testo = "" Then testo = FraseSelezione(precedente, attuale)
                If testo = "" AndAlso fermo Then testo = _campi.PrevediSelezione(precedente, evento.CodiceTasto, evento.Ctrl, previsto)
                If testo = "" AndAlso fermo AndAlso (evento.CodiceTasto = &H26 OrElse evento.CodiceTasto = &H28 OrElse evento.CodiceTasto = &H21 OrElse evento.CodiceTasto = &H22) Then
                    Dim attesaSelezione As String = AttendiTestoSelezione(elemento, precedente.Selezione, web)
                    If attesaSelezione <> "" AndAlso attesaSelezione <> precedente.Selezione Then
                        If attesaSelezione.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then
                            testo = attesaSelezione
                        Else
                            Dim fraseSelezione As String = _campi.DescriviCambioSelezione(precedente.Selezione, attesaSelezione)
                            If fraseSelezione = "" Then fraseSelezione = _campi.MessaggioSelezione(attesaSelezione)
                            testo = fraseSelezione
                            attuale.Selezione = attesaSelezione
                            fermo = False
                            _uiaFerma = Nothing
                        End If
                    End If
                End If
            ElseIf evento.Azione = AzioneTastoLettore.Modifica OrElse cancella Then
                If Not fermo Then testo = _campi.DescriviModifica(precedente, attuale, evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo, EcoCaratteri, EcoParole, LeggiPassword)
                If testo = "" AndAlso cancella AndAlso fermo Then testo = _campi.PrevediCancellazione(precedente, evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo, LeggiPassword, previsto)
                If testo = "" AndAlso cancella AndAlso Not fermo AndAlso Not precedente.Uguale(attuale) Then testo = _campi.FraseCancellazioneDaValori(precedente.Valore, attuale.Valore, precedente.Protetto AndAlso Not LeggiPassword, evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo)
                Dim vuoleEco As Boolean = (EcoCaratteri OrElse EcoParole) AndAlso Not cancella AndAlso Not evento.Ctrl AndAlso Not evento.Alt
                If testo = "" AndAlso vuoleEco AndAlso fermo Then testo = _campi.PrevediInserimento(precedente, evento.CodiceTasto, evento.Maiuscolo, EcoCaratteri, EcoParole, LeggiPassword, previsto)
                If testo = "" AndAlso EcoCaratteri AndAlso Not cancella AndAlso Not evento.Ctrl AndAlso Not evento.Alt Then testo = _campi.LeggiCarattere(elemento)
                If evento.CodiceTasto = &HD AndAlso If(attuale.Valore, "").Length < If(precedente.Valore, "").Length Then testo = ""
            Else
                testo = _campi.DescriviNavigazione(elemento, evento.CodiceTasto, evento.Ctrl, evento.Alt, False, precedente.Selezione)
            End If
            If previsto IsNot Nothing Then
                If _uiaFerma Is Nothing Then _uiaFerma = attuale.Clona()
                attuale = previsto
            End If
            If chiedeSelezione AndAlso testo <> "" Then
                _ultimaSelezioneEsterna = attuale.Selezione
                SilenziaSelezione(450)
            End If
            _istantanea = attuale
            MemorizzaElemento(elemento, _descrittore.Nome(elemento), _descrittore.ParteVariabile(elemento))
            SilenziaValore(400)
            If String.Equals(testo, "vuoto", StringComparison.CurrentCultureIgnoreCase) AndAlso DescrittoreElementi.PrimoPianoTeams() Then testo = ""
            If evento.Azione = AzioneTastoLettore.Modifica OrElse cancella Then
                If evento.EcoDetto AndAlso Not cancella Then testo = ""
                Volatile.Write(_origineUtente, 1)
                Try
                    Parla(testo, True)
                Finally
                    Volatile.Write(_origineUtente, 0)
                End Try
            Else
                Volatile.Write(_origineUtente, 1)
                Try
                    Parla(testo, True)
                Finally
                    Volatile.Write(_origineUtente, 0)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraTasto: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaSelezioneVoce()
        Try
            Dim fuoco As IUIAutomationElement = ElementoConFocus()
            If fuoco Is Nothing OrElse EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(fuoco))) Then Return
            Dim voce As IUIAutomationElement = LetturaAlberi.ElementoDaAnnunciare(fuoco, _descrittore, _walkerControllo, _richiestaCache, True)
            If voce Is Nothing Then voce = fuoco
            Dim voceSel As IUIAutomationSelectionItemPattern = TryCast(voce.GetCurrentPattern(10010), IUIAutomationSelectionItemPattern)
            If voceSel Is Nothing Then Return
            Dim richiesta As New RichiestaSelezioneVoce(Me)
            richiesta.Voce = voceSel
            richiesta.Prima = voceSel.CurrentIsSelected <> 0
            richiesta.Tentativo = 0
            ProgrammaOperazione("selezioneVoce", 30, AddressOf richiesta.Esegui, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaSelezioneVoce: " & ex.Message)
        End Try
    End Sub

    Private Function NelControlloComposito(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return False
            If LetturaAlberi.NelAlbero(elemento, _descrittore, _walkerControllo, _richiestaCache) Then Return True
            If _descrittore.VoceDiComposito(elemento) OrElse _descrittore.ContenitoreComposito(elemento) Then Return True
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 5
                If _walkerControllo Is Nothing Then Exit For
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
                If corrente Is Nothing Then Exit For
                If LetturaAlberi.EContenitoreDiVoci(_descrittore, corrente) Then Return True
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.NelControlloComposito", exAtteso)
        End Try
        Return False
    End Function

    Private Sub AnnunciaVoceAlbero(elemento As IUIAutomationElement, Optional preferisciFuoco As Boolean = False, Optional evento As EventoLettore = Nothing)
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            If Not NelControlloComposito(elemento) Then Return
            Dim fuoco As IUIAutomationElement = ElementoConFocus(False)
            If Not TastoRecente(500) Then
                If fuoco Is Nothing OrElse Not NelControlloComposito(fuoco) Then Return
            End If
            Dim voce As IUIAutomationElement = LetturaAlberi.ElementoDaAnnunciare(elemento, _descrittore, _walkerControllo, _richiestaCache, preferisciFuoco)
            If voce Is Nothing Then voce = elemento
            If fuoco IsNot Nothing AndAlso _descrittore.TipoControllo(fuoco) = DescrittoreElementi.ControlloCasellaCombinata AndAlso Not StessoElemento(fuoco, voce) Then
                If DentroElemento(voce, fuoco, 12) OrElse VoceDellaCasella(voce, fuoco) Then AnnunciaSceltaCasella(fuoco, voce, evento)
                Return
            End If
            If fuoco IsNot Nothing AndAlso Not SelezioneLegataAlFuoco(voce, fuoco) Then Return
            Dim nome As String = _descrittore.Nome(voce)
            Dim variabile As String = _descrittore.ParteVariabile(voce)
            If StessaVoce(voce, _ultimoElemento) AndAlso nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile Then Return
            If _descrittore.TipoControllo(voce) = DescrittoreElementi.ControlloCasellaCombinata AndAlso StessoElemento(voce, _ultimoElemento) AndAlso nome = _ultimoNome Then
                If SelezioneSuperata(evento) Then Return
                MemorizzaElemento(voce, nome, variabile)
                If ValoreCasellaGiaDetto(voce, variabile) OrElse variabile = "" Then Return
                _valoreCasellaDetto = _descrittore.SceltaCasella(voce)
                If _valoreCasellaDetto = "" Then _valoreCasellaDetto = If(_descrittore.LeggiStringa(voce, DescrittoreElementi.ProprietaValore), "")
                Volatile.Write(_valoreCasellaTick, Environment.TickCount)
                Parla(variabile, True)
                Return
            End If
            Dim modulo As AppModuleLettore = OttieniModuloApp(voce)
            Dim testo As String = DescriviConColonne(voce)
            testo = AggiungiNonSelezionato(voce, testo)
            If modulo.EVoceApp(voce) Then
                testo = AggiungiPosizioneFinale(voce, modulo.AllFocus(voce, testo, _ultimoElemento))
            ElseIf MovimentoInternoElenco(_ultimoElemento, voce) Then
            Else
                Dim contesto As String = ContestoNuovo(voce)
                If contesto <> "" Then testo = contesto & ", " & testo
                testo = CompletaAnnuncio(voce, testo)
            End If
            If testo = "" Then Return
            If SelezioneSuperata(evento) Then Return
            _ultimoAnnuncioAlbero = testo
            MemorizzaElemento(voce, nome, variabile)
            _revisione = voce
            _ultimoFocusTick = Environment.TickCount
            Parla(testo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaVoceAlbero: " & ex.Message)
        End Try
    End Sub

    Private Function DentroElemento(elemento As IUIAutomationElement, contenitore As IUIAutomationElement, livelli As Integer) As Boolean
        Try
            If elemento Is Nothing OrElse contenitore Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To livelli
                If corrente Is Nothing Then Return False
                If StessoElemento(corrente, contenitore) Then Return True
                Dim tipo As Integer = _descrittore.TipoControllo(corrente)
                If salto > 0 AndAlso tipo = DescrittoreElementi.ControlloFinestra AndAlso _descrittore.TipoControllo(contenitore) <> DescrittoreElementi.ControlloFinestra Then Return False
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.DentroElemento: " & ex.Message)
        End Try
        Return False
    End Function

    Private Function ContenitoreVoci(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walkerControllo Is Nothing Then Return Nothing
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Return Nothing
                Dim tipo As Integer = _descrittore.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloFinestra OrElse tipo = DescrittoreElementi.ControlloDocumento Then Return Nothing
                If salto > 0 OrElse tipo <> DescrittoreElementi.ControlloCasellaCombinata Then
                    If LetturaAlberi.EContenitoreDiVoci(_descrittore, corrente) OrElse _descrittore.EAlbero(corrente) Then Return corrente
                    If tipo <> DescrittoreElementi.ControlloCasellaCombinata AndAlso _descrittore.ContenitoreComposito(corrente) Then Return corrente
                End If
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ContenitoreVoci: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function SelezioneLegataAlFuoco(voce As IUIAutomationElement, fuoco As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing OrElse fuoco Is Nothing Then Return True
            If StessoElemento(voce, fuoco) Then Return True
            If DentroElemento(voce, fuoco, 12) Then Return True
            Dim contenitore As IUIAutomationElement = ContenitoreVoci(voce)
            If contenitore Is Nothing Then Return False
            Return DentroElemento(fuoco, contenitore, 12)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SelezioneLegataAlFuoco: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaSceltaCasella(casella As IUIAutomationElement, voce As IUIAutomationElement, evento As EventoLettore)
        Try
            If casella Is Nothing OrElse voce Is Nothing Then Return
            If evento IsNot Nothing AndAlso SelezioneSuperata(evento) Then Return
            If OttieniModuloApp(casella).EventoSoppresso(casella) Then Return
            Dim casellaViva As IUIAutomationElement = CompletaCache(casella)
            If casellaViva Is Nothing Then casellaViva = casella
            Dim nomeCasella As String = _descrittore.Nome(casellaViva)
            Dim variabileCasella As String = _descrittore.ParteVariabile(casellaViva)
            Dim espansa As Boolean = _descrittore.LeggiIntero(casellaViva, DescrittoreElementi.ProprietaStatoEspansione, -1) = 1
            Dim testo As String
            If espansa Then
                testo = DescriviConColonne(voce)
                testo = AggiungiNonSelezionato(voce, testo)
            Else
                testo = _descrittore.NomeCorrente(voce)
            End If
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim giaDetto As Boolean = StessoElemento(casellaViva, _ultimoElemento) AndAlso String.Equals(variabileCasella, _ultimaParteVariabile, StringComparison.CurrentCulture) AndAlso variabileCasella.IndexOf(testo, StringComparison.CurrentCulture) >= 0
            If Not giaDetto AndAlso String.Equals(testo, _valoreCasellaDetto, StringComparison.CurrentCulture) AndAlso MillisecondiDa(Volatile.Read(_valoreCasellaTick)) < 600L Then giaDetto = True
            MemorizzaElemento(casellaViva, nomeCasella, variabileCasella)
            _revisione = casellaViva
            If giaDetto Then Return
            _valoreCasellaDetto = testo
            Volatile.Write(_valoreCasellaTick, Environment.TickCount)
            Parla(testo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaSceltaCasella: " & ex.Message)
        End Try
    End Sub

    Private Function VoceDellaCasella(voce As IUIAutomationElement, casella As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing OrElse casella Is Nothing Then Return False
            If _descrittore.IdProcesso(voce) <> _descrittore.IdProcesso(casella) Then Return False
            Dim nomeVoce As String = _descrittore.NomeCorrente(voce)
            If nomeVoce = "" Then Return False
            Dim valore As String = ""
            Try
                Dim schema As IUIAutomationValuePattern = TryCast(casella.GetCurrentPattern(10002), IUIAutomationValuePattern)
                If schema IsNot Nothing Then valore = If(schema.CurrentValue, "")
            Catch exValore As Exception
                log.ScriviLog("WGLettoreSchermo.VoceDellaCasella (valore): " & exValore.Message)
            End Try
            If valore = "" Then valore = _descrittore.SceltaCasella(casella)
            Return String.Equals(valore.Trim(), nomeVoce.Trim(), StringComparison.CurrentCulture)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VoceDellaCasella: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ValoreCasellaGiaDetto(elemento As IUIAutomationElement, variabile As String) As Boolean
        Try
            If elemento Is Nothing OrElse _descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloCasellaCombinata Then Return False
            Dim detto As String = _valoreCasellaDetto
            If String.IsNullOrEmpty(detto) Then Return False
            If MillisecondiDa(Volatile.Read(_valoreCasellaTick)) > 800L Then Return False
            Return If(variabile, "").IndexOf(detto, StringComparison.CurrentCulture) >= 0
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ValoreCasellaGiaDetto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaSelezioneElemento(elemento As IUIAutomationElement, web As Boolean)
        Try
            Dim precedente As String = ""
            If _istantanea IsNot Nothing Then precedente = _istantanea.Selezione
            Dim selezione As String = AttendiTestoSelezione(elemento, precedente, web)
            If selezione = "" Then Return
            Dim frase As String = selezione
            If Not selezione.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then
                frase = _campi.DescriviCambioSelezione(precedente, selezione)
                If frase = "" Then frase = _campi.MessaggioSelezione(selezione)
            End If
            If _istantanea Is Nothing Then _istantanea = New IstantaneaCampo()
            _istantanea.Selezione = selezione
            _ultimaSelezioneEsterna = selezione
            SilenziaSelezione(450)
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaSelezioneElemento: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaTastoNuovoElemento(elemento As IUIAutomationElement, evento As EventoLettore, chiedeSelezione As Boolean, cancella As Boolean)
        Try
            _uiaFerma = Nothing
            _istantanea = _campi.Cattura(elemento)
            MemorizzaElemento(elemento, _descrittore.Nome(elemento), _descrittore.ParteVariabile(elemento))
            Dim annuncio As String = ""
            If chiedeSelezione Then
                annuncio = AttendiTestoSelezione(elemento, "", ElementoWeb(elemento))
                If annuncio <> "" AndAlso Not annuncio.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then annuncio = _campi.MessaggioSelezione(annuncio)
                If annuncio <> "" Then
                    _ultimaSelezioneEsterna = _istantanea.Selezione
                    SilenziaSelezione(450)
                End If
            ElseIf cancella Then
                annuncio = ""
            ElseIf evento.Azione = AzioneTastoLettore.Modifica AndAlso Not evento.Ctrl AndAlso Not evento.Alt AndAlso (EcoCaratteri OrElse EcoParole) Then
                Dim scartato As IstantaneaCampo = Nothing
                Dim base As New IstantaneaCampo()
                base.HaCursore = True
                base.Protetto = _descrittore.Protetto(elemento)
                annuncio = _campi.PrevediInserimento(base, evento.CodiceTasto, evento.Maiuscolo, EcoCaratteri, EcoParole, LeggiPassword, scartato)
                If annuncio = "" AndAlso EcoCaratteri Then annuncio = _campi.LeggiCarattere(elemento)
            Else
                annuncio = _campi.DescriviNavigazione(elemento, evento.CodiceTasto, evento.Ctrl, evento.Alt, False, "")
            End If
            SilenziaValore(400)
            Parla(annuncio, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaTastoNuovoElemento: " & ex.Message)
        End Try
    End Sub

    Private Function AttendiCampo(elemento As IUIAutomationElement, precedente As IstantaneaCampo, web As Boolean, paziente As Boolean, Optional senzaAttesa As Boolean = False) As IstantaneaCampo
        Dim tentativi As Integer = If(paziente, If(web, 8, 4), If(web, 3, 2))
        If senzaAttesa Then tentativi = 1
        Dim pausa As Integer = If(web, 18, 8)
        Dim attuale As IstantaneaCampo = _campi.Cattura(elemento)
        If precedente Is Nothing Then Return attuale
        For indice As Integer = 1 To tentativi
            If ModelloSuperato(precedente, attuale) Then
                _uiaFerma = Nothing
                Return attuale
            End If
            If indice = tentativi Then Exit For
            Thread.Sleep(pausa)
            attuale = _campi.Cattura(elemento)
        Next
        If _uiaFerma IsNot Nothing Then Return precedente.Clona()
        Return attuale
    End Function

    Private Function ModelloSuperato(modello As IstantaneaCampo, attuale As IstantaneaCampo) As Boolean
        If modello Is Nothing Then Return True
        If attuale Is Nothing Then Return False
        If modello.Uguale(attuale) Then Return False
        If _uiaFerma IsNot Nothing AndAlso (_uiaFerma.Uguale(attuale) OrElse InRitardo(attuale, modello)) Then Return False
        If modello.HaCursore AndAlso modello.StessoTesto(attuale) AndAlso _uiaFerma IsNot Nothing Then Return False
        Return True
    End Function

    Private Function InRitardo(uia As IstantaneaCampo, modello As IstantaneaCampo) As Boolean
        If uia Is Nothing OrElse modello Is Nothing OrElse _uiaFerma Is Nothing Then Return False
        If uia.Uguale(_uiaFerma) OrElse uia.StessoTesto(modello) Then Return True
        If Not uia.HaCursore OrElse Not modello.HaCursore OrElse Not _uiaFerma.HaCursore Then Return False
        Dim base As IstantaneaCampo = _uiaFerma
        If uia.Selezione = base.Selezione AndAlso modello.Selezione <> base.Selezione AndAlso uia.TestoPrima = base.TestoPrima AndAlso uia.TestoDopo = base.TestoDopo Then Return True
        If uia.Selezione <> "" AndAlso uia.Selezione <> modello.Selezione AndAlso uia.Selezione.StartsWith(base.Selezione, StringComparison.Ordinal) AndAlso modello.Selezione.StartsWith(uia.Selezione, StringComparison.Ordinal) Then Return True
        If uia.Selezione <> "" AndAlso uia.Selezione <> modello.Selezione AndAlso uia.Selezione.EndsWith(base.Selezione, StringComparison.Ordinal) AndAlso modello.Selezione.EndsWith(uia.Selezione, StringComparison.Ordinal) Then Return True
        If uia.TestoDopo = modello.TestoDopo AndAlso uia.Selezione = modello.Selezione Then
            If modello.TestoPrima.StartsWith(uia.TestoPrima, StringComparison.Ordinal) AndAlso uia.TestoPrima.StartsWith(base.TestoPrima, StringComparison.Ordinal) Then Return True
            If base.TestoPrima.StartsWith(uia.TestoPrima, StringComparison.Ordinal) AndAlso uia.TestoPrima.StartsWith(modello.TestoPrima, StringComparison.Ordinal) Then Return True
            If uia.TestoPrima.EndsWith(modello.TestoPrima, StringComparison.Ordinal) AndAlso base.TestoPrima.EndsWith(modello.TestoPrima, StringComparison.Ordinal) Then Return True
        End If
        If uia.TestoPrima = modello.TestoPrima AndAlso uia.Selezione = modello.Selezione Then
            If base.TestoDopo.StartsWith(uia.TestoDopo, StringComparison.Ordinal) AndAlso uia.TestoDopo.StartsWith(modello.TestoDopo, StringComparison.Ordinal) Then Return True
            If modello.TestoDopo.StartsWith(uia.TestoDopo, StringComparison.Ordinal) AndAlso uia.TestoDopo.StartsWith(base.TestoDopo, StringComparison.Ordinal) Then Return True
            If uia.TestoDopo.StartsWith(modello.TestoDopo, StringComparison.Ordinal) AndAlso base.TestoDopo.StartsWith(modello.TestoDopo, StringComparison.Ordinal) Then Return True
        End If
        Return False
    End Function

    Private Function FraseSelezione(precedente As IstantaneaCampo, attuale As IstantaneaCampo) As String
        Try
            If precedente Is Nothing OrElse attuale Is Nothing Then Return ""
            Dim prima As String = If(precedente.Selezione, "")
            Dim dopo As String = If(attuale.Selezione, "")
            If prima = dopo Then Return ""
            If dopo.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then Return dopo
            Dim frase As String = _campi.DescriviCambioSelezione(prima, dopo)
            If frase = "" AndAlso dopo <> "" Then frase = _campi.MessaggioSelezione(dopo)
            Return frase
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FraseSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function AttendiTestoSelezione(elemento As IUIAutomationElement, precedente As String, web As Boolean) As String
        Dim tentativi As Integer = If(web, 6, 3)
        Dim pausa As Integer = If(web, 20, 12)
        Dim testo As String = ""
        For indice As Integer = 1 To tentativi
            testo = _campi.TestoSelezionato(elemento)
            If testo Is Nothing Then testo = ""
            If testo <> precedente Then Return testo
            If indice < tentativi Then Thread.Sleep(pausa)
        Next
        Return testo
    End Function

    Private Function ElementoWeb(elemento As IUIAutomationElement) As Boolean
        Try
            Dim framework As String = _descrittore.Framework(elemento)
            If DescrittoreElementi.FrameworkWeb(framework) Then Return True
            If Not String.IsNullOrEmpty(framework) Then Return False
            Return BufferInPrimoPiano() AndAlso _navigazione.EWeb
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElementoWeb: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function BufferInPrimoPiano() As Boolean
        Try
            If _navigazione Is Nothing OrElse _navigazione.NumeroVoci = 0 Then Return False
            Return _navigazione.Finestra = NativoLettore.GetForegroundWindow()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.BufferInPrimoPiano: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ElaboraSelezioneEsterna()
        Try
            If SelezioneSilenziata() Then Return
            If Volatile.Read(_selezioneInCoda) = 1 Then Return
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing OrElse _campi Is Nothing Then Return
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            Dim testo As String = _campi.TestoSelezionato(elemento)
            If testo Is Nothing OrElse testo = "" OrElse testo = _ultimaSelezioneEsterna Then Return
            _ultimaSelezioneEsterna = testo
            If _istantanea Is Nothing Then _istantanea = _campi.Cattura(elemento)
            If Not testo.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then _istantanea.Selezione = testo
            If testo.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then
                Parla(testo, True)
            Else
                Parla(_campi.MessaggioSelezione(testo), True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraSelezioneEsterna: " & ex.Message)
        End Try
    End Sub

    Private _prossimoIndiceTick As Integer

    Private Sub IndicizzaNavigazione()
        Try
            If Volatile.Read(_tastiVirtuali) <> 1 OrElse _navigazione Is Nothing Then Return
            If Environment.TickCount - _prossimoIndiceTick < 0 Then Return
            _prossimoIndiceTick = Environment.TickCount + 150
            If NativoLettore.GetForegroundWindow() <> _navigazione.Finestra Then Return
            If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) < 500L Then Return
            If _coda IsNot Nothing AndAlso _coda.Count > 0 Then Return
            Dim operazioni As CodaOperazioni = _operazioni
            If operazioni IsNot Nothing AndAlso operazioni.Occupata Then Return
            _navigazione.Indicizza(If(TastoRecente(300), 8, 30))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.IndicizzaNavigazione: " & ex.Message)
        End Try
    End Sub

    Private Function SelezioneSilenziata() As Boolean
        Try
            Return Environment.TickCount - Volatile.Read(_silenzioSelezioneFino) < 0
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SelezioneSilenziata: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SilenziaSelezione(millisecondi As Integer)
        Try
            Volatile.Write(_silenzioSelezioneFino, Environment.TickCount + millisecondi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SilenziaSelezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiComando(comando As ComandoLettore)
        Try
            Volatile.Write(_origineUtente, 1)
            If comando <> ComandoLettore.PausaLettura AndAlso comando <> ComandoLettore.LetturaContinua Then SegnaTasto()
            If comando = ComandoLettore.AttivaDisattiva Then
                Dim nuovoStato As Boolean = Not Sospeso
                If nuovoStato Then
                    Parla("Lettore di schermo sospeso", True)
                    Sospeso = True
                Else
                    Sospeso = False
                    Parla("Lettore di schermo attivo", True)
                End If
                Return
            End If
            If Sospeso Then Return
            If SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            If EseguiComandoEsteso(comando) Then Return
            Select Case comando
                Case ComandoLettore.Titolo
                    Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
                    Dim titolo As String = If(FinestraDesktop(primoPiano), "Desktop", TitoloFinestra(primoPiano))
                    Parla(If(titolo = "", "Nessun titolo", titolo), True)
                Case ComandoLettore.Focus
                    LeggiFocus()
                Case ComandoLettore.Ora
                    Parla(TestoOra(), True)
                Case ComandoLettore.Data
                    Parla(TestoData(), True)
                Case ComandoLettore.BarraStato
                    Parla(TestoBarraStato(), True)
                Case ComandoLettore.Riga
                    LeggiRigaCorrente()
                Case ComandoLettore.LetturaContinua
                    LeggiDalCursore()
                Case ComandoLettore.Finestra
                    LeggiFinestra()
                Case ComandoLettore.Selezione
                    LeggiSelezioneCorrente()
                Case ComandoLettore.Precedente, ComandoLettore.Successivo, ComandoLettore.Contenitore, ComandoLettore.Contenuto
                    Parla(SpostaRevisione(comando), True)
                Case ComandoLettore.NavigazioneWeb
                    AlternaNavigazione()
                Case ComandoLettore.AttivaElemento
                    AttivaCorrente()
                Case ComandoLettore.Aiuto
                    Parla(TestoComandi(), True)
                Case ComandoLettore.AggiornaNavigazione
                    AggiornaAlbero(True)
                Case ComandoLettore.Descrizione
                    Parla(TestoDescrizione(), True)
                Case ComandoLettore.Compita
                    Parla(Compita(ParolaCorrente()), True)
                Case ComandoLettore.Parola
                    Parla(ParolaCorrente(), True)
                Case ComandoLettore.Carattere
                    LeggiCarattereComando()
                Case ComandoLettore.EsciScrittura
                    EsciDaScrittura()
                Case ComandoLettore.Commutatore
                    Dim generazione As Integer = Volatile.Read(_commutatoreGenerazione)
                    Dim corsiaCommutatore As CodaOperazioni = _operazioniCommutatore
                    If corsiaCommutatore IsNot Nothing Then
                        Dim chiamataCommutatore As New Chiamata(Of Integer)(AddressOf LeggiCommutatoreUtente, generazione)
                        corsiaCommutatore.Esegui("commutatore", AddressOf chiamataCommutatore.Esegui, sostituisci:=True)
                    Else
                        LeggiCommutatore(3, True, generazione)
                    End If
                Case ComandoLettore.StatoMaiuscole
                    Parla(If(NativoLettore.BlocMaiuscoleAttivo(), "Maiuscole attivate", "Maiuscole disattivate"), True)
                Case ComandoLettore.Messaggio
                    LeggiMessaggio()
                Case ComandoLettore.RigaTabella
                    LeggiRigaTabella()
                Case ComandoLettore.ColonnaTabella
                    LeggiColonnaTabella()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiComando: " & ex.Message)
        Finally
            Volatile.Write(_origineUtente, 0)
        End Try
    End Sub

    Private Sub LeggiFocus()
        Try
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing Then
                Parla("Nessun elemento con il focus", True)
                Return
            End If
            Dim descrizione As String = DescriviConColonne(elemento, True)
            MemorizzaElemento(elemento, _descrittore.Nome(elemento), _descrittore.ParteVariabile(elemento))
            Parla(If(descrizione = "", "Elemento senza nome", descrizione), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiRigaCorrente()
        Try
            If InVirtuale() Then
                Dim annuncio As String = _navigazione.AnnuncioCorrente()
                Parla(If(annuncio = "", "vuoto", annuncio), True)
                Return
            End If
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing OrElse _campi Is Nothing Then Return
            Dim testo As String = _campi.LeggiRiga(elemento)
            If testo = "" Then
                LeggiFocus()
                Return
            End If
            Parla(testo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiRigaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiDalCursore()
        AvviaLetturaContinua()
    End Sub

    Private Sub LeggiFinestra()
        Try
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return
            Dim parti As New List(Of String)()
            Dim titolo As String = TitoloFinestra(finestra)
            If FinestraTerminale(finestra) Then
                Dim schermo As String = LeggiSchermoTerminale(titolo)
                If schermo <> "" Then
                    Parla(schermo, True)
                    Return
                End If
            End If
            If titolo <> "" Then parti.Add(titolo)
            Dim testi As String = TestiFinestra(finestra, titolo)
            If testi <> "" Then parti.Add(testi)
            Parla(If(parti.Count = 0, "Nessun testo nella finestra", String.Join(", ", parti)), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiFinestra: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiSelezioneCorrente()
        Try
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            Dim campo As Boolean = elemento IsNot Nothing AndAlso (_descrittore.ECampoScrittura(elemento) OrElse (_navigazione IsNot Nothing AndAlso _navigazione.RichiedeScrittura(elemento)))
            If campo AndAlso _campi IsNot Nothing Then
                Parla(_campi.LeggiSelezione(elemento), True)
                Return
            End If
            If InVirtuale() Then
                Dim testo As String = _navigazione.TestoSelezione()
                If testo <> "" Then
                    If testo.Length > 2000 Then
                        Parla("Selezionati " & testo.Length.ToString() & " caratteri. " & Accorcia(testo), True)
                    ElseIf testo.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then
                        Parla(testo, True)
                    Else
                        Parla(testo & " selezionato", True)
                    End If
                    Return
                End If
            End If
            If _campi Is Nothing Then Return
            Parla(_campi.LeggiSelezione(elemento), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiSelezioneCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiCarattereComando()
        Try
            Dim doppio As Boolean = MillisecondiDa(_ultimoComandoCarattereTick) < 600L
            _ultimoComandoCarattereTick = If(doppio, Integer.MinValue, Environment.TickCount)
            Dim nome As String = CarattereCorrente()
            If doppio AndAlso _descrittore IsNot Nothing Then
                Dim fonetico As String = _descrittore.NomeFonetico(_descrittore.UltimoCarattere)
                If fonetico <> "" Then
                    Parla(fonetico, True)
                    Return
                End If
            End If
            Parla(nome, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiCarattereComando: " & ex.Message)
        End Try
    End Sub

    Private Sub PassaAlProgramma(evento As EventoLettore)
        Try
            If evento Is Nothing Then Return
            Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
            If evento.CodiceTasto = &HD OrElse evento.CodiceTasto = &H20 Then Volatile.Write(_ultimaAttivazioneWeb, Environment.TickCount)
            If _navigazione IsNot Nothing Then
                Dim bersaglio As IUIAutomationElement = _navigazione.ElementoPerFuoco(_navigazione.ElementoCorrente())
                If bersaglio IsNot Nothing AndAlso PuoSpostareFuoco() Then
                    Dim attuale As IUIAutomationElement = ElementoConFocus(False)
                    If attuale Is Nothing OrElse Not StessoElemento(attuale, bersaglio) Then
                        _navForzataElemento = bersaglio
                        _ultimoFocusNav = bersaglio
                        MemorizzaElemento(bersaglio, _descrittore.Nome(bersaglio), _descrittore.ParteVariabile(bersaglio))
                        Try
                            bersaglio.SetFocus()
                        Catch ex As Exception
                            log.ScriviLog("WGLettoreSchermo.PassaAlProgramma (fuoco): " & ex.Message)
                        End Try
                        Thread.Sleep(80)
                    End If
                End If
            End If
            InviaTastoConModificatori(evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PassaAlProgramma: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaTastoConModificatori(codice As Integer, ctrl As Boolean, maiuscolo As Boolean)
        Dim premiCtrl As Boolean = False
        Dim premiMaiuscolo As Boolean = False
        Try
            premiCtrl = ctrl AndAlso (CInt(NativoLettore.GetAsyncKeyState(&H11)) And &H8000) = 0
            premiMaiuscolo = maiuscolo AndAlso (CInt(NativoLettore.GetAsyncKeyState(&H10)) And &H8000) = 0
            Dim esteso As Integer = If(codice = &H5D, NativoLettore.KEYEVENTF_EXTENDEDKEY, 0)
            If premiCtrl Then NativoLettore.keybd_event(CByte(&H11), 0, 0, IntPtr.Zero)
            If premiMaiuscolo Then NativoLettore.keybd_event(CByte(&H10), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(codice), 0, esteso, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(codice), 0, esteso Or NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InviaTastoConModificatori: " & ex.Message)
        Finally
            Try
                If premiMaiuscolo Then NativoLettore.keybd_event(CByte(&H10), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
                If premiCtrl Then NativoLettore.keybd_event(CByte(&H11), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            Catch ex As Exception
                log.ScriviLog("WGLettoreSchermo.InviaTastoConModificatori (rilascio): " & ex.Message)
            End Try
        End Try
    End Sub

    Private Function CarattereCorrente() As String
        Try
            If InVirtuale() Then Return _navigazione.CarattereCorrente()
            If _campi Is Nothing Then Return ""
            Dim testo As String = _campi.LeggiCarattere(ElementoConFocus())
            If testo = "" Then Return "vuoto"
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CarattereCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ParolaCorrente() As String
        Try
            If InVirtuale() Then Return _navigazione.ParolaCorrente()
            If _campi Is Nothing Then Return ""
            Dim testo As String = _campi.LeggiParola(ElementoConFocus())
            If testo = "" Then Return "vuoto"
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ParolaCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Compita(parola As String) As String
        Try
            If String.IsNullOrEmpty(parola) OrElse parola = "vuoto" OrElse parola = "protetto" OrElse parola = "stella" Then
                If String.IsNullOrEmpty(parola) Then Return "vuoto"
                Return parola
            End If
            Dim parti As New List(Of String)()
            Dim limite As Integer = Math.Min(parola.Length, 80)
            For indice As Integer = 0 To limite - 1
                parti.Add(_descrittore.NomeCarattere(parola.Substring(indice, 1)))
            Next
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Compita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AnnunciaCopiaCampo()
        Try
            ProgrammaOperazione("copiaCampo", AttesaCursore, AddressOf AnnunciaCopiaCampoDopo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaCopiaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub CopiaVirtuale()
        Try
            If _navigazione Is Nothing Then Return
            Dim testo As String = _navigazione.TestoSelezione()
            If testo = "" Then testo = _navigazione.TestoVoceCorrente()
            If testo = "" Then
                Dim elemento As IUIAutomationElement = _navigazione.ElementoCorrente()
                If elemento IsNot Nothing Then testo = _descrittore.Nome(elemento)
            End If
            If testo = "" Then
                Parla("Nessuna selezione", True)
                Return
            End If
            If NativoLettore.CopiaTesto(testo) Then
                Parla("Copiato, " & Accorcia(testo), True)
            Else
                Parla("Copia non riuscita", True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CopiaVirtuale: " & ex.Message)
        End Try
    End Sub

    ' Un tasto di navigazione ferma subito la lettura continua e aspetta che finisca quello che il battito stava facendo: tutti e due
    ' muovono lo stesso cursore, e muoverlo insieme lo faceva tornare dove la lettura era arrivata.
    Private Sub ElaboraNavigazioneVirtuale(evento As EventoLettore)
        Dim preso As Boolean = False
        Try
            If Volatile.Read(_continuaAttiva) = 1 OrElse Volatile.Read(_continuaVirtuale) = 1 Then FermaLetturaContinua(False)
            preso = Monitor.TryEnter(_bloccoEsecuzione, 1500)
            ElaboraNavigazioneVirtualeInterna(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraNavigazioneVirtuale: " & ex.Message)
        Finally
            If preso Then Monitor.Exit(_bloccoEsecuzione)
        End Try
    End Sub

    Private Sub ElaboraNavigazioneVirtualeInterna(evento As EventoLettore)
        Try
            If evento.CodiceTasto = &H9 Then
                TabDalCursore(evento.Maiuscolo)
                Return
            End If
            If evento.CodiceTasto = &H5D OrElse evento.CodiceTasto = &H79 OrElse (evento.CodiceTasto = &HD AndAlso (evento.Ctrl OrElse evento.Maiuscolo)) Then
                PassaAlProgramma(evento)
                Return
            End If
            If _navigazione Is Nothing Then
                Parla("Nessun contenuto", True)
                Return
            End If
            If _navigazione.NumeroVoci = 0 OrElse Not _navigazione.RadiceValida() Then
                If Not RecuperaDocumento() AndAlso _navigazione.NumeroVoci = 0 Then
                    Parla("Nessun contenuto", True)
                    Return
                End If
            Else
                _navigazione.RiancoraCursore()
            End If
            If evento.CodiceTasto = &HD OrElse evento.CodiceTasto = &H20 Then
                AttivaCorrente()
                Return
            End If
            If evento.Ctrl AndAlso evento.CodiceTasto = &H41 Then
                _navigazione.SelezionaTutto()
                Parla("Selezionato tutto", True)
                Return
            End If
            Volatile.Write(_ultimoMovimentoVirtuale, Environment.TickCount)
            Dim frase As String = _navigazione.Muovi(evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo, evento.Alt)
            _navigazione.RicordaCursore()
            SegnaCursoreDaTasti()
            If frase = "" Then frase = NomeTastoNonGestito(evento)
            _revisione = _navigazione.ElementoCorrente()
            SuonaConfine(frase)
            Volatile.Write(_origineUtente, 1)
            Try
                Parla(frase, True)
            Finally
                Volatile.Write(_origineUtente, 0)
            End Try
            If Not _navigazione.UltimoMovimentoARighe Then PortaFuocoSulCursore()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraNavigazioneVirtuale: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoAlModulo(evento As EventoLettore)
        Try
            Dim elemento As IUIAutomationElement = ElementoConFocus(False)
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            Dim modulo As AppModuleLettore = OttieniModuloApp(elemento)
            If modulo.EPredefinito Then Return
            Dim sostituto As IUIAutomationElement = modulo.ElementoTasto(elemento)
            If sostituto IsNot Nothing Then elemento = sostituto
            modulo.AllTasto(evento, elemento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoAlModulo: " & ex.Message)
        End Try
    End Sub

    Private Function RecuperaDocumento() As Boolean
        Try
            If _navigazione Is Nothing OrElse Not NavigazioneWebAbilitata Then Return False
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero OrElse Not EBrowser(finestra) Then Return False
            Dim elemento As IUIAutomationElement = ElementoConFocus(False)
            Dim contesto As ContestoNavigazione = Nothing
            Dim dentro As Boolean = False
            If elemento IsNot Nothing Then
                contesto = _navigazione.Contesto(elemento, finestra)
                dentro = contesto IsNot Nothing AndAlso contesto.Radice IsNot Nothing AndAlso contesto.DentroDocumento
            End If
            If Not dentro Then contesto = _navigazione.ContestoFinestra(finestra)
            If contesto Is Nothing OrElse contesto.Radice Is Nothing OrElse Not contesto.DentroDocumento Then Return False
            If String.Equals(contesto.Chiave, _navDisattivataPer, StringComparison.Ordinal) Then Return False
            Dim numero As Integer = _navigazione.Costruisci(contesto.Radice, finestra, True, contesto.ERiquadro)
            _ultimoCostruisci = Environment.TickCount
            If numero <= 0 Then Return False
            If dentro AndAlso Not _navigazione.EDocumentoORadice(elemento) Then
                _navigazione.Sincronizza(elemento)
                _ultimoFocusNav = elemento
            Else
                _navigazione.RitrovaPosizione()
            End If
            _navAttiva = True
            _navManuale = False
            Volatile.Write(_fuoriDalDocumento, 0)
            ImpostaModalita(True, False)
            _modoFocusAnnunciato = False
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RecuperaDocumento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FuocoPersoNelBrowser(elemento As IUIAutomationElement, finestra As IntPtr) As Boolean
        Try
            If elemento Is Nothing OrElse finestra = IntPtr.Zero OrElse Not EBrowser(finestra) Then Return False
            Dim tipo As Integer = _descrittore.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloFinestra AndAlso tipo <> DescrittoreElementi.ControlloRiquadro Then Return False
            Dim maniglia As IntPtr = IntPtr.Zero
            Dim classe As String = ""
            Try
                maniglia = elemento.CurrentNativeWindowHandle
                classe = If(elemento.CurrentClassName, "")
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.FuocoPersoNelBrowser", exAtteso)
                Return False
            End Try
            If tipo = DescrittoreElementi.ControlloFinestra Then Return maniglia = finestra
            Return classe = "Chrome_RenderWidgetHostHWND" OrElse classe = "MozillaWindowClass" OrElse (maniglia <> IntPtr.Zero AndAlso maniglia = finestra)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoPersoNelBrowser: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub PortaFuocoSulCursore()
        Try
            _sincronizzaFuocoElemento = Nothing
            If _navigazione Is Nothing Then Return
            _fuocoDifferito = _navigazione.ElementoPerFuoco(_navigazione.ElementoCorrente())
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PortaFuocoSulCursore: " & ex.Message)
        End Try
    End Sub

    Private Function NomeTastoNonGestito(evento As EventoLettore) As String
        Try
            If evento Is Nothing OrElse evento.Ctrl Then Return ""
            If evento.CodiceTasto >= &H41 AndAlso evento.CodiceTasto <= &H5A Then
                Dim lettera As String = ChrW(evento.CodiceTasto).ToString()
                If Not evento.Maiuscolo Then lettera = lettera.ToLowerInvariant()
                Return _descrittore.NomeCarattere(lettera)
            End If
            If evento.CodiceTasto >= &H30 AndAlso evento.CodiceTasto <= &H39 Then Return ChrW(evento.CodiceTasto).ToString()
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeTastoNonGestito: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AttivaCorrente()
        Try
            Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
            Volatile.Write(_ultimaAttivazioneWeb, Environment.TickCount)
            If _navigazione Is Nothing Then Return
            Dim elemento As IUIAutomationElement = Nothing
            If InVirtuale() Then elemento = _navigazione.ElementoCorrente()
            If elemento Is Nothing Then elemento = _revisione
            If elemento Is Nothing Then elemento = ElementoConFocus()
            If elemento Is Nothing Then
                Parla("Nessun elemento", True)
                Return
            End If
            If InVirtuale() Then _navigazione.RicordaCursore()
            Dim esito As String = _navigazione.Attiva(elemento)
            If esito = "scrittura" Then
                _navForzataElemento = Nothing
                ImpostaModalita(_navAttiva, True)
                Thread.Sleep(AttesaCursore)
                Dim campo As IUIAutomationElement = ElementoConFocus()
                If _campi IsNot Nothing Then _istantanea = _campi.Cattura(campo)
                Return
            End If
            Thread.Sleep(180)
            Dim dopo As IUIAutomationElement = ElementoConFocus()
            SilenziaValore(250)
            If InVirtuale() AndAlso RitornaSullaPagina(True) Then Return
            If dopo IsNot Nothing AndAlso _navigazione.RichiedeScrittura(dopo) Then
                _navForzataElemento = Nothing
                ImpostaModalita(_navAttiva, True)
                If _campi IsNot Nothing Then _istantanea = _campi.Cattura(dopo)
                If esito <> "" Then Parla(esito, True)
                Return
            End If
            If SembraAperturaMessaggio(elemento, esito) Then
                Dim messaggio As String = ""
                For tentativo As Integer = 1 To 3
                    messaggio = _navigazione.ApriContenuto()
                    If messaggio <> "" Then Exit For
                    Thread.Sleep(160 * tentativo)
                Next
                If messaggio <> "" Then
                    EntraNelMessaggio(dopo)
                    Parla(messaggio, True)
                    Return
                End If
            End If
            If Not InVirtuale() AndAlso ProvaLetturaRiquadro(elemento, 2500) Then Return
            If esito <> "" Then
                Parla(esito, True)
            ElseIf dopo Is Nothing OrElse StessoElemento(elemento, dopo) Then
                Parla("Attivato", True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttivaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Function SembraAperturaMessaggio(elemento As IUIAutomationElement, esito As String) As Boolean
        Try
            If Not BufferInPrimoPiano() OrElse Not _navigazione.EWeb Then Return False
            If esito <> "" AndAlso esito <> "selezionato" Then Return False
            If elemento Is Nothing Then Return False
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            If ruolo = "row" OrElse ruolo = "gridcell" OrElse ruolo = "link" OrElse ruolo = "listitem" OrElse ruolo = "option" OrElse ruolo = "treeitem" Then Return True
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloCollegamento
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SembraAperturaMessaggio: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub EntraNelMessaggio(fuoco As IUIAutomationElement)
        Try
            _navDisattivataPer = ""
            _navAttiva = True
            _navManuale = False
            ImpostaModalita(True, False)
            _modoFocusAnnunciato = False
            _navForzataElemento = fuoco
            If fuoco IsNot Nothing Then _ultimoFocusNav = fuoco
            Volatile.Write(_cursoreDaTasti, 1)
            Volatile.Write(_proteggiCursoreFino, Environment.TickCount + 1200)
            If fuoco IsNot Nothing Then MemorizzaElemento(fuoco, _descrittore.Nome(fuoco), _descrittore.ParteVariabile(fuoco))
            _ultimoFocusTick = Environment.TickCount
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EntraNelMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiMessaggio()
        Try
            If BufferInPrimoPiano() AndAlso _navigazione.EWeb Then
                Dim frase As String = ""
                For tentativo As Integer = 1 To 3
                    frase = _navigazione.ApriContenuto()
                    If frase <> "" Then Exit For
                    Thread.Sleep(140 * tentativo)
                Next
                If frase <> "" Then
                    EntraNelMessaggio(ElementoConFocus())
                    Parla(frase, True)
                    Return
                End If
            End If
            If ProvaLetturaRiquadro(Nothing, 8000) Then Return
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing Then
                Parla("nessun messaggio", True)
                Return
            End If
            Dim documento As String = LetturaStrutturaDocumento.LeggiDocumento(elemento, _descrittore)
            Parla(If(documento = "", "nessun messaggio", documento), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function ProvaLetturaRiquadro(elemento As IUIAutomationElement, massimo As Integer) As Boolean
        Try
            If elemento Is Nothing Then elemento = ElementoConFocus()
            If elemento Is Nothing Then elemento = _ultimoElemento
            If elemento Is Nothing OrElse _descrittore Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            Dim testo As String = LetturaRiquadroCollegato.TestoDelRiquadro(elemento, _automazione, _descrittore, _walkerControllo, _richiestaCache, massimo)
            If testo = "" Then Return False
            Dim doc As IUIAutomationElement = LetturaRiquadroCollegato.Riquadro(elemento, _automazione, _descrittore, _walkerControllo, _richiestaCache)
            If doc IsNot Nothing AndAlso _navigazione IsNot Nothing AndAlso NavigazioneWebAbilitata Then
                Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
                _navigazione.Costruisci(doc, finestra, True, True)
                If LetturaRiquadroCollegato.InRiquadro(elemento, _automazione, _descrittore, _walkerControllo, _richiestaCache) Then
                    _navigazione.Sincronizza(elemento)
                Else
                    _navigazione.Sincronizza(doc)
                End If
                _navDisattivataPer = ""
                _navAttiva = _navigazione.NumeroVoci > 0
                _navManuale = False
                ImpostaModalita(True, False)
                EntraNelMessaggio(elemento)
                _ultimoCostruisci = Environment.TickCount
            End If
            Parla("dettaglio, " & testo, True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProvaLetturaRiquadro: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LeggiRigaTabella()
        Try
            Dim frase As String = ""
            If InVirtuale() Then
                frase = _navigazione.LeggiRiga()
            Else
                Dim elemento As IUIAutomationElement = ElementoConFocus()
                frase = OttieniModuloApp(elemento).RigaApp(elemento)
                If frase = "" Then frase = LetturaTabelle.FraseRiga(elemento, _descrittore, _automazione, _richiestaCache)
            End If
            Parla(If(frase = "", "non sei in una tabella", frase), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiRigaTabella: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiColonnaTabella()
        Try
            Dim frase As String = ""
            If InVirtuale() Then
                frase = _navigazione.LeggiColonna()
            Else
                Dim elemento As IUIAutomationElement = ElementoConFocus()
                frase = OttieniModuloApp(elemento).ColonnaApp(elemento)
                If frase = "" Then frase = LetturaTabelle.FraseColonna(elemento, _descrittore, _automazione, _richiestaCache)
            End If
            Parla(If(frase = "", "non sei in una tabella", frase), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiColonnaTabella: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraFocusMsaa(Optional evento As EventoLettore = Nothing)
        Try
            Dim finestra As IntPtr
            Dim oggetto As Integer
            Dim figlio As Integer
            SyncLock _bloccoMsaa
                finestra = _msaaFinestra
                oggetto = _msaaOggetto
                figlio = _msaaFiglio
            End SyncLock
            Interlocked.Exchange(_focusMsaaInCoda, 0)
            Dim listaMsaa As IntPtr = If(finestra <> IntPtr.Zero AndAlso ControlliNativi.EListaNativa(ClasseFinestra(finestra)), finestra, IntPtr.Zero)
            If ProvaRigaNativaVeloce(IntPtr.Zero, listaMsaa) Then Return
            SegnaListaNativaConFuoco()
            If LeggiRiquadroGecko(finestra, oggetto, figlio) Then Return
            If ClasseFinestra(finestra) = "MozillaWindowClass" Then Return
            If FinestraXaml(finestra) Then
                ProgrammaOperazione("fuocoXaml", 120, AddressOf VerificaFuocoXaml, True)
                Return
            End If
            If OttieniModuloApp(finestra).FocusMsaaIgnorato(finestra) Then Return
            If evento IsNot Nothing AndAlso evento.Epoca <> Volatile.Read(_epocaSelezione) Then Return
            If LeggiMenuClassico(finestra, oggetto, figlio) Then Return
            If Environment.TickCount - Volatile.Read(_ultimoFocusTick) < 80 Then Return
            Dim fraseMenu As String = ""
            If LeggiVoceMenuWinForms(finestra, oggetto, figlio, fraseMenu) Then
                If _popupGecko Then ModalitaMenu()
                If fraseMenu = "" Then Return
                _ultimaFraseMsaa = fraseMenu
                _ultimoFocusTick = Environment.TickCount
                Parla(fraseMenu, True)
                Return
            End If
            Dim vivo As IUIAutomationElement = ElementoConFocus()
            If vivo IsNot Nothing AndAlso Not ElementoWeb(vivo) Then
                ' Le voci che un modulo sa descrivere (per Esplora risorse: nome della vista, tipo di visualizzazione, selezione e posizione)
                ' si annunciano dal percorso del modulo: la frase generica di MSAA, con il solo nome e «voce di elenco», non deve passargli davanti.
                Dim moduloVivo As AppModuleLettore = OttieniModuloApp(vivo)
                If moduloVivo.EVoceApp(vivo) OrElse moduloVivo.EElencoApp(vivo) Then
                    If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) > 200L AndAlso Not StessaVoce(vivo, _ultimoElemento) Then
                        Dim daModulo As New EventoLettore()
                        daModulo.Tipo = TipoEventoLettore.Focus
                        daModulo.Elemento = vivo
                        daModulo.Sequenza = Interlocked.Increment(_sequenzaFocus)
                        Volatile.Write(_ultimoFocusArrivo, Environment.TickCount)
                        ElaboraFocus(daModulo)
                    End If
                    Return
                End If
            End If
            If vivo IsNot Nothing AndAlso ElementoWeb(vivo) Then
                If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) > 200L AndAlso Not StessaVoce(vivo, _ultimoElemento) Then
                    Dim sintetico As New EventoLettore()
                    sintetico.Tipo = TipoEventoLettore.Focus
                    sintetico.Elemento = vivo
                    sintetico.Sequenza = Interlocked.Increment(_sequenzaFocus)
                    Volatile.Write(_ultimoFocusArrivo, Environment.TickCount)
                    ElaboraFocus(sintetico)
                End If
                Return
            End If
            If vivo Is Nothing AndAlso _navigazione IsNot Nothing AndAlso _navigazione.EFinestraWeb(NativoLettore.GetAncestor(finestra, NativoLettore.GA_ROOT)) Then Return
            Dim nomeUia As String = If(vivo Is Nothing, "", _descrittore.Nome(vivo))
            If NomeUiaUtile(nomeUia) AndAlso vivo IsNot Nothing AndAlso StessaVoce(vivo, _ultimoElemento) Then Return
            Dim frase As String = AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio)
            If frase = "" AndAlso vivo IsNot Nothing AndAlso Not StessaVoce(vivo, _ultimoElemento) Then frase = AccessibilitaMsaa.DescriviElemento(vivo)
            If frase = "" Then Return
            If String.Equals(frase, _ultimaFraseMsaa, StringComparison.CurrentCulture) Then Return
            If TitoloRipetuto(frase) Then Return
            _ultimaFraseMsaa = frase
            _ultimoFocusTick = Environment.TickCount
            If vivo IsNot Nothing Then
                MemorizzaElemento(vivo, _descrittore.Nome(vivo), _descrittore.ParteVariabile(vivo))
                _revisione = vivo
            End If
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraFocusMsaa: " & ex.Message)
        End Try
    End Sub

    Private Function CompletaCache(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _richiestaCache Is Nothing Then Return elemento
            Dim completo As IUIAutomationElement = elemento.BuildUpdatedCache(_richiestaCache)
            If completo Is Nothing Then Return elemento
            Return completo
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.CompletaCache: " & ex.Message)
            Return elemento
        End Try
    End Function

    Private NotInheritable Class RichiestaVivaIa2
        Public Finestra As IntPtr
        Public Oggetto As Integer
        Public Figlio As Integer
        Public Tipo As UInteger
    End Class

    Private NotInheritable Class RichiestaParlaVivaIa2
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _testo As String
        Private ReadOnly _assertiva As Boolean
        Private ReadOnly _avviso As Boolean

        Public Sub New(lettore As WGLettoreSchermo, testo As String, assertiva As Boolean, avviso As Boolean)
            _lettore = lettore
            _testo = testo
            _assertiva = assertiva
            _avviso = avviso
        End Sub

        Public Sub Esegui()
            Try
                _lettore.ParlaVivaIa2(_testo, _assertiva, _avviso)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaParlaVivaIa2.Esegui: " & ex.Message)
            Finally
                Interlocked.Decrement(_lettore._parlaVivaInCoda)
            End Try
        End Sub
    End Class

    Friend Sub SegnalaRegioneVivaIa2(finestra As IntPtr, oggetto As Integer, figlio As Integer, tipo As UInteger)
        Try
            If finestra = IntPtr.Zero Then Return
            If tipo = &H2UI Then
                If Volatile.Read(_leggiAvvisi) = 0 Then Return
            ElseIf Volatile.Read(_leggiRegioniVive) = 0 Then
                Return
            End If
            If Not EBrowserGecko(NativoLettore.GetForegroundWindow()) Then Return
            If tipo <> &H2UI AndAlso ClasseFinestra(finestra) = "MozillaDropShadowWindowClass" Then Return
            If (tipo = &H11EUI OrElse tipo = &H120UI) AndAlso TastoRecente(600) Then Return
            Dim adesso As Integer = Environment.TickCount
            If adesso - Volatile.Read(_vivaFinestraTick) > 1000 OrElse adesso - Volatile.Read(_vivaFinestraTick) < 0 Then
                Volatile.Write(_vivaFinestraTick, adesso)
                Volatile.Write(_vivaConteggio, 0)
            End If
            If Interlocked.Increment(_vivaConteggio) > 25 Then Return
            If _codaVivaIa2.Count >= 16 Then Return
            _codaVivaIa2.Enqueue(New RichiestaVivaIa2 With {.Finestra = finestra, .Oggetto = oggetto, .Figlio = figlio, .Tipo = tipo})
            If Interlocked.CompareExchange(_vivaIa2InCoda, 1, 0) <> 0 Then Return
            ThreadPool.QueueUserWorkItem(AddressOf ElaboraVivaIa2)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnalaRegioneVivaIa2: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraVivaIa2(stato As Object)
        Try
            Thread.Sleep(30)
            Dim richiesta As RichiestaVivaIa2 = Nothing
            While _codaVivaIa2.TryDequeue(richiesta)
                If richiesta Is Nothing Then Continue While
                If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Continue While
                If AvvisoDaAltraPagina(Nothing, richiesta.Finestra) Then Continue While
                Dim assertiva As Boolean = False
                Dim avviso As Boolean = richiesta.Tipo = &H2UI
                Dim testo As String
                If avviso Then
                    testo = AccessibilitaMsaa.TestoAvvisoEvento(richiesta.Finestra, richiesta.Oggetto, richiesta.Figlio)
                    assertiva = True
                Else
                    testo = AccessibilitaMsaa.TestoRegioneViva(richiesta.Finestra, richiesta.Oggetto, richiesta.Figlio, richiesta.Tipo, assertiva)
                End If
                If String.IsNullOrWhiteSpace(testo) Then Continue While
                If Volatile.Read(_parlaVivaInCoda) >= 3 Then Continue While
                Interlocked.Increment(_parlaVivaInCoda)
                Dim parla As New RichiestaParlaVivaIa2(Me, testo, assertiva, avviso)
                EseguiSuOperazioni("", AddressOf parla.Esegui)
            End While
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraVivaIa2: " & ex.Message)
        Finally
            Interlocked.Exchange(_vivaIa2InCoda, 0)
            Try
                If Not _codaVivaIa2.IsEmpty AndAlso Interlocked.CompareExchange(_vivaIa2InCoda, 1, 0) = 0 Then ThreadPool.QueueUserWorkItem(AddressOf ElaboraVivaIa2)
            Catch exRiavvio As Exception
                log.ScriviLog("WGLettoreSchermo.ElaboraVivaIa2 (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    Private Sub ParlaVivaIa2(testo As String, assertiva As Boolean, avviso As Boolean)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim eventoModulo As New EventoLettore()
            eventoModulo.Tipo = If(avviso, TipoEventoLettore.Avviso, TipoEventoLettore.RegioneViva)
            If Not avviso AndAlso OttieniModuloForegroundApp().AllAvviso(eventoModulo) Then Return
            Dim adesso As Integer = Environment.TickCount
            If avviso Then
                If String.Equals(testo, _ultimoAvviso, StringComparison.CurrentCulture) AndAlso adesso - _ultimoAvvisoTick < 4000 Then Return
                _ultimoAvviso = testo
                _ultimoAvvisoTick = adesso
                Parla("Avviso, " & testo, True)
                Return
            End If
            If String.Equals(testo, _ultimaRegioneViva, StringComparison.CurrentCulture) AndAlso adesso - _ultimaRegioneVivaTick < 1500 Then Return
            _ultimaRegioneViva = testo
            _ultimaRegioneVivaTick = adesso
            Parla(testo, assertiva)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ParlaVivaIa2: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiMenuClassico(finestra As IntPtr, oggetto As Integer, figlio As Integer) As Boolean
        Try
            If finestra = IntPtr.Zero OrElse ClasseFinestra(finestra) <> "#32768" Then Return False
            Dim frase As String = AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio)
            If frase = "" Then Return True
            Dim appenaAperto As Boolean = Not _menuClassico
            _menuClassico = True
            ModalitaMenu()
            If String.Equals(frase, _ultimaFraseMsaa, StringComparison.CurrentCulture) AndAlso MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 400L Then Return True
            _ultimaFraseMsaa = frase
            _ultimoFocusTick = Environment.TickCount
            Parla(If(appenaAperto, "menu, " & frase, frase), True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiMenuClassico: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub FineMenuClassico()
        Try
            If Not _menuClassico Then Return
            If Interlocked.CompareExchange(_fineMenuInCoda, 1, 0) <> 0 Then Return
            ThreadPool.QueueUserWorkItem(AddressOf ControllaFineMenuClassico)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FineMenuClassico: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaFineMenuClassico(stato As Object)
        Try
            Thread.Sleep(80)
            Interlocked.Exchange(_fineMenuInCoda, 0)
            If MenuClassicoVisibile() Then Return
            _menuClassico = False
            SyncLock _bloccoMsaa
                If ClasseFinestra(_msaaFinestra) = "#32768" Then _msaaFinestra = IntPtr.Zero
            End SyncLock
            EseguiSuOperazioni("fineMenuClassico", AddressOf RipristinaDopoMenuClassico, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaFineMenuClassico: " & ex.Message)
        End Try
    End Sub

    Private Function MenuClassicoVisibile() As Boolean
        Try
            Dim menu As IntPtr = NativoLettore.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "#32768", Nothing)
            For giro As Integer = 0 To 20
                If menu = IntPtr.Zero Then Return False
                If NativoLettore.IsWindowVisible(menu) Then Return True
                menu = NativoLettore.FindWindowEx(IntPtr.Zero, menu, "#32768", Nothing)
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MenuClassicoVisibile: " & ex.Message)
        End Try
        Return False
    End Function

    Private Sub RipristinaDopoMenuClassico()
        Try
            If _menuClassico Then Return
            Dim vivo As IUIAutomationElement = ElementoConFocus(False)
            If vivo Is Nothing OrElse _navigazione Is Nothing Then Return
            AggiornaContestoWeb(vivo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RipristinaDopoMenuClassico: " & ex.Message)
        End Try
    End Sub

    Private Function EVoceMenuNativa(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return False
            Dim tipo As Integer = _descrittore.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloVoceMenu AndAlso tipo <> DescrittoreElementi.ControlloMenu Then Return False
            If Not ElementoWeb(elemento) Then Return True
            Return RegoleRuoli.NormalizzaFramework(_descrittore.Framework(elemento)) = "mozilla"
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EVoceMenuNativa: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FinestraXaml(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Select Case ClasseFinestra(finestra)
                Case "Xaml_WindowedPopupClass", "Windows.UI.Composition.DesktopWindowContentBridge", "Windows.UI.Input.InputSite.WindowClass",
                     "Microsoft.UI.Content.DesktopChildSiteBridge", "Microsoft.UI.Content.PopupWindowSiteBridge", "InputSiteWindowClass"
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraXaml: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AnnunciaVoceMenuNativa(evento As EventoLettore, origine As IUIAutomationElement) As Boolean
        Try
            If origine Is Nothing OrElse _descrittore Is Nothing Then Return False
            Dim elemento As IUIAutomationElement = CompletaCache(origine)
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return True
            _menuNativoAperto = True
            If ElementoWeb(elemento) AndAlso Volatile.Read(_tastiVirtuali) = 1 Then ModalitaMenu()
            Dim nome As String = _descrittore.Nome(elemento)
            Dim variabile As String = _descrittore.ParteVariabile(elemento)
            If StessaVoce(elemento, _ultimoElemento) AndAlso nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile AndAlso MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 1500L Then Return True
            Dim testo As String = DescriviConColonne(elemento)
            If testo = "" Then Return False
            If evento IsNot Nothing AndAlso evento.Sequenza > 0 AndAlso evento.Sequenza <> Volatile.Read(_sequenzaFocus) Then Return True
            _messaggioGiaAnnunciato = False
            _ultimoAnnuncioAlbero = testo
            MemorizzaElemento(elemento, nome, variabile)
            _revisione = elemento
            _uiaFerma = Nothing
            _ultimoFocusTick = Environment.TickCount
            _ultimoMenuMsaaNome = nome
            Volatile.Write(_ultimoMenuMsaaTick, Environment.TickCount)
            _menuNativoAperto = True
            Parla(testo, True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaVoceMenuNativa: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function MenuNativoInCorso() As Boolean
        Try
            If Not _menuNativoAperto OrElse _menuClassico Then Return False
            Dim ultimo As IUIAutomationElement = _ultimoElemento
            Return ultimo IsNot Nothing AndAlso EVoceMenuNativa(ultimo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MenuNativoInCorso: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AttendiVoceMenu(evento As EventoLettore)
        Try
            Dim richiesta As New RichiestaVoceMenu(Me, _ultimoElemento, Volatile.Read(_sequenzaFocus), evento)
            ThreadPool.QueueUserWorkItem(AddressOf richiesta.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttendiVoceMenu: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaVoceMenu
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _prima As IUIAutomationElement
        Private ReadOnly _sequenza As Integer
        Private ReadOnly _evento As EventoLettore

        Public Sub New(lettore As WGLettoreSchermo, prima As IUIAutomationElement, sequenza As Integer, evento As EventoLettore)
            _lettore = lettore
            _prima = prima
            _sequenza = sequenza
            _evento = evento
        End Sub

        Public Sub Attendi(stato As Object)
            Try
                Thread.Sleep(150)
                If _lettore Is Nothing OrElse _lettore.FuocoCambiatoDa(_sequenza) Then Return
                _lettore.EseguiSuOperazioni("voceMenuNativa", AddressOf Esegui, sostituisci:=True)
            Catch ex As Exception
                If _lettore IsNot Nothing Then _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaVoceMenu.Attendi: " & ex.Message)
            End Try
        End Sub

        Public Sub Esegui()
            Try
                _lettore.CercaVoceMenuConFuoco(_prima, _sequenza, _evento)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaVoceMenu.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Function FuocoCambiatoDa(sequenza As Integer) As Boolean
        Try
            Return Volatile.Read(_sequenzaFocus) <> sequenza
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoCambiatoDa: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub CercaVoceMenuConFuoco(prima As IUIAutomationElement, sequenza As Integer, evento As EventoLettore)
        Try
            If prima Is Nothing OrElse _automazione Is Nothing OrElse _walkerControllo Is Nothing Then Return
            If FuocoCambiatoDa(sequenza) Then Return
            If Not MenuNativoInCorso() OrElse Not StessoElemento(prima, _ultimoElemento) Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim voce As IUIAutomationElement = VoceMenuConFuoco(prima)
            If voce IsNot Nothing Then
                If StessoElemento(voce, prima) Then Return
                Dim sintetico As New EventoLettore()
                sintetico.Tipo = TipoEventoLettore.Focus
                sintetico.Elemento = voce
                sintetico.Sequenza = Volatile.Read(_sequenzaFocus)
                AnnunciaVoceMenuNativa(sintetico, voce)
                Return
            End If
            If HaFuocoTastiera(prima) Then Return
            _menuNativoAperto = False
            If evento IsNot Nothing Then ElaboraTasto(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CercaVoceMenuConFuoco: " & ex.Message)
        End Try
    End Sub

    Private Function VoceMenuConFuoco(prima As IUIAutomationElement) As IUIAutomationElement
        Try
            If prima Is Nothing OrElse _automazione Is Nothing OrElse _walkerControllo Is Nothing Then Return Nothing
            Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(30008, True)
            Dim contenitore As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(prima, _richiestaCache)
            For livello As Integer = 0 To 2
                If contenitore Is Nothing Then Exit For
                Dim trovata As IUIAutomationElement = contenitore.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, _richiestaCache)
                If trovata IsNot Nothing AndAlso EVoceMenuNativa(trovata) Then Return trovata
                contenitore = _walkerControllo.GetParentElementBuildCache(contenitore, _richiestaCache)
            Next
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.VoceMenuConFuoco: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function HaFuocoTastiera(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Return elemento.CurrentHasKeyboardFocus <> 0
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.HaFuocoTastiera: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function VoceMenuVisibile(voce As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing Then Return False
            Return voce.CurrentIsOffscreen = 0
        Catch ex As Exception
            If TimeoutOperazione(ex) Then Return True
            If Not ElementoAssente(ex) Then log.ScriviLog("WGLettoreSchermo.VoceMenuVisibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ControllaMenuChiusoNativo()
        Try
            If Not _menuNativoAperto Then Return
            Dim ultimo As IUIAutomationElement = _ultimoElemento
            If ultimo IsNot Nothing AndAlso EVoceMenuNativa(ultimo) AndAlso VoceMenuVisibile(ultimo) AndAlso HaFuocoTastiera(ultimo) Then Return
            _menuNativoAperto = False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaMenuChiusoNativo: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaMenuAperto
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _elemento As IUIAutomationElement
        Private ReadOnly _tick As Integer

        Public Sub New(lettore As WGLettoreSchermo, elemento As IUIAutomationElement)
            _lettore = lettore
            _elemento = elemento
            _tick = Environment.TickCount
        End Sub

        Public Sub Attendi(stato As Object)
            Try
                Thread.Sleep(80)
                If _lettore.FuocoArrivatoDopo(_tick) Then Return
                _lettore.EseguiSuOperazioni("menuAperto", AddressOf Esegui, sostituisci:=True)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaMenuAperto.Attendi: " & ex.Message)
            End Try
        End Sub

        Public Sub Esegui()
            Try
                If _lettore.FuocoArrivatoDopo(_tick) Then Return
                _lettore.FuocoDaMenuAperto(_elemento)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaMenuAperto.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Function FuocoArrivatoDopo(tick As Integer) As Boolean
        Try
            Return MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) < MillisecondiDa(tick)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoArrivatoDopo: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub MenuApertoNativo(elemento As IUIAutomationElement)
        Try
            If elemento Is Nothing Then Return
            Dim richiesta As New RichiestaMenuAperto(Me, elemento)
            ThreadPool.QueueUserWorkItem(AddressOf richiesta.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MenuApertoNativo: " & ex.Message)
        End Try
    End Sub

    Private Sub FuocoDaMenuAperto(elemento As IUIAutomationElement)
        Try
            If elemento Is Nothing Then Return
            Dim sintetico As New EventoLettore()
            sintetico.Tipo = TipoEventoLettore.Focus
            sintetico.Elemento = CompletaCache(elemento)
            sintetico.Sequenza = Interlocked.Increment(_sequenzaFocus)
            Volatile.Write(_ultimoFocusArrivo, Environment.TickCount)
            ElaboraFocus(sintetico)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoDaMenuAperto: " & ex.Message)
        End Try
    End Sub

    Private NotInheritable Class RichiestaFuocoMenu
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _sequenza As Integer
        Private ReadOnly _tentativo As Integer

        Public Sub New(lettore As WGLettoreSchermo, sequenza As Integer, tentativo As Integer)
            _lettore = lettore
            _sequenza = sequenza
            _tentativo = tentativo
        End Sub

        Public Sub Attendi(stato As Object)
            Try
                Dim attesa As Integer = 60
                Select Case _tentativo
                    Case 2
                        attesa = 150
                    Case 3
                        attesa = 300
                    Case Is >= 4
                        attesa = 600
                End Select
                Thread.Sleep(attesa)
                If _lettore Is Nothing OrElse Not _lettore.InEsecuzione Then Return
                _lettore.EseguiSuOperazioni("fuocoMenu", AddressOf Esegui, sostituisci:=True)
            Catch ex As Exception
                If _lettore IsNot Nothing Then _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaFuocoMenu.Attendi: " & ex.Message)
            End Try
        End Sub

        Public Sub Esegui()
            Try
                _lettore.ControllaFuocoMenu(_sequenza, _tentativo)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaFuocoMenu.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Friend Sub InizioMenuSistema(finestra As IntPtr)
        Try
            If Not InEsecuzione OrElse Sospeso Then Return
            If finestra <> IntPtr.Zero Then
                Dim pid As UInteger = 0UI
                NativoLettore.GetWindowThreadProcessId(finestra, pid)
                If pid <> 0UI AndAlso EProcessoWinGabriel(pid) Then Return
            End If
            Volatile.Write(_menuSistemaTick, Environment.TickCount)
            Interlocked.Exchange(_menuFuoriConteggio, 0)
            Interlocked.Exchange(_menuSistemaAttivo, 1)
            _svegliaSfondo.Set()
            Dim richiesta As New RichiestaFuocoMenu(Me, Volatile.Read(_sequenzaFocus), 1)
            ThreadPool.QueueUserWorkItem(AddressOf richiesta.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InizioMenuSistema: " & ex.Message)
        End Try
    End Sub

    Friend Sub FineMenuSistema()
        Try
            If Volatile.Read(_menuSistemaAttivo) = 0 Then Return
            ProgrammaOperazione("fineMenuSistema", 150, AddressOf ControllaFineMenuSistema, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FineMenuSistema: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaFineMenuSistema()
        Try
            If Volatile.Read(_menuSistemaAttivo) = 0 Then Return
            Dim vivo As IUIAutomationElement = ElementoConFocus(False)
            If vivo IsNot Nothing AndAlso EVoceMenuNativa(vivo) Then Return
            Interlocked.Exchange(_menuSistemaAttivo, 0)
            ControllaMenuChiusoNativo()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaFineMenuSistema: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaFuocoMenu(sequenza As Integer, tentativo As Integer)
        Try
            If Volatile.Read(_menuSistemaAttivo) = 0 Then Return
            Dim inMenu As Boolean = False
            If AnnunciaVoceMenuSeNuova(inMenu) Then Return
            If inMenu AndAlso Volatile.Read(_sequenzaFocus) <> sequenza Then Return
            If tentativo >= 4 Then Return
            Dim richiesta As New RichiestaFuocoMenu(Me, sequenza, tentativo + 1)
            ThreadPool.QueueUserWorkItem(AddressOf richiesta.Attendi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaFuocoMenu: " & ex.Message)
        End Try
    End Sub

    Private Function AnnunciaVoceMenuSeNuova(ByRef inMenu As Boolean) As Boolean
        inMenu = False
        Try
            If _automazione Is Nothing OrElse _descrittore Is Nothing Then Return False
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() OrElse FocusSilenziato() Then Return False
            If MenuVirtualeAttivo OrElse _popupGecko OrElse _menuClassico Then Return False
            If InCommutatore(NativoLettore.GetForegroundWindow()) Then Return False
            Dim sequenza As Integer = Volatile.Read(_sequenzaFocus)
            Dim vivo As IUIAutomationElement = ElementoConFocus(False)
            If vivo Is Nothing OrElse Not EVoceMenuNativa(vivo) Then Return False
            If ElementoWeb(vivo) Then Return False
            inMenu = True
            If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) < 200L Then Return False
            If MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 300L AndAlso MillisecondiDa(Volatile.Read(_msaaTick)) < 400L Then Return False
            If ClasseFinestra(ManigliaFinestra(vivo)) = "#32768" Then Return False
            vivo = CompletaCache(vivo)
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(vivo))) Then Return False
            If StessaVoce(vivo, _ultimoElemento) AndAlso _descrittore.Nome(vivo) = _ultimoNome AndAlso _descrittore.ParteVariabile(vivo) = _ultimaParteVariabile Then Return False
            Dim sintetico As New EventoLettore()
            sintetico.Tipo = TipoEventoLettore.Focus
            sintetico.Elemento = vivo
            sintetico.Sequenza = sequenza
            Return AnnunciaVoceMenuNativa(sintetico, vivo)
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.AnnunciaVoceMenuSeNuova: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SorvegliaFuocoMenu()
        Try
            If Sospeso OrElse _automazione Is Nothing OrElse _descrittore Is Nothing Then Return
            Dim attivo As Boolean = Volatile.Read(_menuSistemaAttivo) = 1
            If attivo AndAlso MillisecondiDa(Volatile.Read(_menuSistemaTick)) > 30000L Then
                Interlocked.Exchange(_menuSistemaAttivo, 0)
                attivo = False
            End If
            If Not EventiRecenti(2000) Then Return
            If MillisecondiDa(_menuSorveglianzaTick) < 120L Then Return
            _menuSorveglianzaTick = Environment.TickCount
            If Not attivo AndAlso Not PrimoPianoConMenuShell() Then Return
            If SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim operazioni As CodaOperazioni = _operazioni
            If operazioni Is Nothing Then Return
            operazioni.Esegui("sorveglianzaMenu", AddressOf EseguiSorveglianzaMenu, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SorvegliaFuocoMenu: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSorveglianzaMenu()
        Try
            Dim inMenu As Boolean = False
            AnnunciaVoceMenuSeNuova(inMenu)
            If Volatile.Read(_menuSistemaAttivo) = 0 Then Return
            If inMenu Then
                Interlocked.Exchange(_menuFuoriConteggio, 0)
                Volatile.Write(_menuSistemaTick, Environment.TickCount)
                Return
            End If
            If Interlocked.Increment(_menuFuoriConteggio) >= 8 Then Interlocked.Exchange(_menuSistemaAttivo, 0)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiSorveglianzaMenu: " & ex.Message)
        End Try
    End Sub

    Private Function PrimoPianoConMenuShell() As Boolean
        Try
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If primo = IntPtr.Zero Then Return False
            If FinestraXaml(primo) Then Return True
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(primo, pid)
            If pid = 0UI OrElse EProcessoWinGabriel(pid) Then Return False
            Select Case NomeEseguibile(pid)
                Case "startmenuexperiencehost", "searchhost", "searchapp", "searchui", "shellexperiencehost", "shellhost"
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PrimoPianoConMenuShell: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub VerificaFuocoXaml()
        Try
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() OrElse FocusSilenziato() Then Return
            If MillisecondiDa(Volatile.Read(_ultimoFocusArrivo)) < 250L Then Return
            Dim inMenu As Boolean = False
            If AnnunciaVoceMenuSeNuova(inMenu) OrElse inMenu Then Return
            AnnunciaFuocoSeNuovo()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VerificaFuocoXaml: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaFuocoSeNuovo()
        Try
            If _descrittore Is Nothing OrElse _automazione Is Nothing Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() OrElse FocusSilenziato() Then Return
            If FinestraDesktop(NativoLettore.GetForegroundWindow()) AndAlso ProvaRigaNativaVeloce() Then Return
            Dim vivo As IUIAutomationElement = ElementoConFocus(False)
            If vivo Is Nothing Then Return
            vivo = CompletaCache(vivo)
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(vivo))) Then Return
            If StessaVoce(vivo, _ultimoElemento) AndAlso _descrittore.Nome(vivo) = _ultimoNome Then Return
            Dim voce As IUIAutomationElement = LetturaAlberi.ElementoDaAnnunciare(vivo, _descrittore, _walkerControllo, _richiestaCache)
            If voce IsNot Nothing AndAlso StessaVoce(voce, _ultimoElemento) AndAlso _descrittore.Nome(voce) = _ultimoNome AndAlso _descrittore.ParteVariabile(voce) = _ultimaParteVariabile Then Return
            Dim sintetico As New EventoLettore()
            sintetico.Tipo = TipoEventoLettore.Focus
            sintetico.Elemento = vivo
            sintetico.Sequenza = Interlocked.Increment(_sequenzaFocus)
            Volatile.Write(_ultimoFocusArrivo, Environment.TickCount)
            ElaboraFocus(sintetico)
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.AnnunciaFuocoSeNuovo: " & ex.Message)
        End Try
    End Sub

    Private Function FuocoDaRiquadroGecko() As Boolean
        Try
            If MillisecondiDa(Volatile.Read(_msaaTick)) > 1500L Then Return False
            Dim finestra As IntPtr
            SyncLock _bloccoMsaa
                finestra = _msaaFinestra
            End SyncLock
            Dim classe As String = ClasseFinestra(finestra)
            If classe = "#32768" Then Return _menuClassico
            If classe <> "MozillaDropShadowWindowClass" Then Return False
            Return EBrowserGecko(NativoLettore.GetForegroundWindow())
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FuocoDaRiquadroGecko: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LeggiRiquadroGecko(finestra As IntPtr, oggetto As Integer, figlio As Integer) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            If ClasseFinestra(finestra) <> "MozillaDropShadowWindowClass" Then Return False
            Dim nomePopup As String = ""
            Dim frase As String
            If AccessibilitaMsaa.VocePopupGecko(finestra, oggetto, figlio, nomePopup) AndAlso nomePopup <> "" Then
                frase = nomePopup
            Else
                frase = AccessibilitaMsaa.DescriviEvento(finestra, oggetto, figlio)
            End If
            If frase = "" Then Return True
            Dim appenaAperto As Boolean = Not _popupGecko OrElse MillisecondiDa(Volatile.Read(_ultimoMenuMsaaTick)) > 1500L
            _popupGecko = True
            Volatile.Write(_ultimoMenuMsaaTick, Environment.TickCount)
            ModalitaMenu()
            If String.Equals(frase, _ultimaFraseMsaa, StringComparison.CurrentCulture) AndAlso MillisecondiDa(Volatile.Read(_ultimoFocusTick)) < 400L Then Return True
            _ultimaFraseMsaa = frase
            _ultimoFocusTick = Environment.TickCount
            Parla(If(appenaAperto, "menu, " & frase, frase), True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiRiquadroGecko: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub EsciDaScrittura()
        Try
            If Not _navAttiva Then Return
            ImpostaModalita(True, False)
            _navForzataElemento = ElementoConFocus()
            _modoFocusAnnunciato = False
            SuonaModalita(False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EsciDaScrittura: " & ex.Message)
        End Try
    End Sub

    Private Sub AlternaNavigazione()
        Try
            If Not NavigazioneWebAbilitata Then
                Parla("La navigazione delle pagine è disattivata nelle impostazioni.", True)
                Return
            End If
            If _navigazione Is Nothing Then Return
            If _navAttiva AndAlso Volatile.Read(_tastiVirtuali) = 0 Then
                _navForzataElemento = ElementoConFocus()
                ImpostaModalita(True, False)
                _modoFocusAnnunciato = False
                SuonaModalita(False)
                Return
            End If
            If _navAttiva Then
                ApplicaFuocoDifferito(True)
                _navDisattivataPer = _navigazione.ChiaveRadice
                _navAttiva = False
                _navManuale = False
                ImpostaModalita(False, False)
                SuonaModalita(True)
                Return
            End If
            AggiornaAlbero(False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AlternaNavigazione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaAlbero(soloAggiorna As Boolean)
        Try
            If _navigazione Is Nothing OrElse _automazione Is Nothing Then Return
            If Not NavigazioneWebAbilitata Then
                Parla("La navigazione delle pagine è disattivata nelle impostazioni.", True)
                Return
            End If
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            Dim contesto As ContestoNavigazione = Nothing
            If elemento IsNot Nothing Then _navigazione.ImpostaFocus(elemento)
            If elemento IsNot Nothing Then contesto = _navigazione.Contesto(elemento, finestra)
            If contesto Is Nothing OrElse contesto.Radice Is Nothing Then contesto = _navigazione.ContestoFinestra(finestra)
            If contesto Is Nothing OrElse contesto.Radice Is Nothing Then
                Parla("Nessun elemento da leggere", True)
                Return
            End If
            _navDisattivataPer = ""
            _navManuale = Not contesto.EWeb
            Dim numero As Integer = _navigazione.Costruisci(contesto.Radice, finestra, contesto.EWeb, contesto.ERiquadro)
            _ultimoCostruisci = Environment.TickCount
            If elemento IsNot Nothing Then
                _navigazione.Sincronizza(elemento)
                _ultimoFocusNav = elemento
                CursoreAlFuoco()
                If _navigazione.EDocumentoORadice(elemento) Then Volatile.Write(_cursoreDaTasti, 1)
            End If
            Volatile.Write(_fuoriDalDocumento, 0)
            _navAttiva = numero > 0
            Dim scrittura As Boolean = elemento IsNot Nothing AndAlso _navigazione.RichiedeScrittura(elemento)
            If Not soloAggiorna AndAlso Not scrittura Then _navForzataElemento = elemento
            _modoFocusAnnunciato = scrittura AndAlso _navAttiva
            ImpostaModalita(_navAttiva, scrittura)
            Dim dove As String = _navigazione.AnnuncioCorrente()
            If numero = 0 Then
                Parla("Nessun elemento", True)
            ElseIf soloAggiorna Then
                Parla(If(dove = "", "Navigazione aggiornata", "Navigazione aggiornata, " & dove), True)
            Else
                SuonaModalita(False)
                If dove <> "" Then Parla(dove, True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiornaAlbero: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaModalita(focus As Boolean)
        Try
            If focus = _modoFocusAnnunciato Then Return
            _modoFocusAnnunciato = focus
            SuonaModalita(focus)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaModalita: " & ex.Message)
        End Try
    End Sub

    Private Function RitornaSullaPagina(annuncia As Boolean) As Boolean
        Try
            If _navigazione Is Nothing OrElse _navigazione.NumeroVoci = 0 Then Return False
            If _navigazione.CursoreValido() Then Return False
            If Not _navigazione.RitrovaPosizione() Then Return False
            _revisione = _navigazione.ElementoCorrente()
            Volatile.Write(_cursoreDaTasti, 1)
            If annuncia Then
                Dim frase As String = _navigazione.AnnuncioCorrente()
                If frase <> "" Then Parla(frase, True)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RitornaSullaPagina: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AvvisoDaAltraPagina(elemento As IUIAutomationElement, finestra As IntPtr) As Boolean
        Try
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano = IntPtr.Zero Then Return False
            If elemento Is Nothing Then
                If finestra = IntPtr.Zero Then Return False
                Dim radice As IntPtr = NativoLettore.GetAncestor(finestra, 2UI)
                If radice = IntPtr.Zero OrElse radice = primoPiano Then Return False
                Dim classe As String = ClasseFinestra(radice)
                Return classe = "MozillaWindowClass" OrElse classe = "Chrome_WidgetWin_1"
            End If
            If Not ElementoWeb(elemento) Then Return False
            Dim pidPrimoPiano As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(primoPiano, pidPrimoPiano)
            If pidPrimoPiano <> 0UI AndAlso _descrittore.IdProcesso(elemento) <> CInt(pidPrimoPiano) Then Return True
            Dim documentoEvento As IUIAutomationElement = DocumentoEsterno(elemento)
            If documentoEvento Is Nothing Then Return False
            Dim fuoco As IUIAutomationElement = ElementoConFocus(False)
            If fuoco Is Nothing OrElse Not ElementoWeb(fuoco) Then Return False
            Dim documentoFuoco As IUIAutomationElement = DocumentoEsterno(fuoco)
            If documentoFuoco Is Nothing Then Return False
            Return Not StessoElemento(documentoEvento, documentoFuoco)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AvvisoDaAltraPagina", exAtteso)
            Return False
        End Try
    End Function

    Private Function DocumentoEsterno(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walkerControllo Is Nothing Then Return Nothing
            Dim trovato As IUIAutomationElement = Nothing
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 40
                If corrente Is Nothing Then Exit For
                Dim tipo As Integer = _descrittore.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloDocumento Then trovato = corrente
                If tipo = DescrittoreElementi.ControlloFinestra Then Exit For
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
            Next
            Return trovato
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.DocumentoEsterno", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Sub AggiornaContestoWeb(elemento As IUIAutomationElement)
        Try
            If _navigazione Is Nothing OrElse elemento Is Nothing Then Return
            If _popupGecko Then
                ModalitaMenu()
                Return
            End If
            If DescrittoreElementi.PrimoPianoTeams() AndAlso _navForzataElemento Is Nothing Then
                ImpostaModalita(_navAttiva, True, True)
                Return
            End If
            If Not NavigazioneWebAbilitata Then
                _navAttiva = False
                _navManuale = False
                ImpostaModalita(False, False)
                _modoFocusAnnunciato = False
                Return
            End If
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            _navigazione.ImpostaFocus(elemento)
            Dim contesto As ContestoNavigazione = _navigazione.Contesto(elemento, finestra)
            If contesto Is Nothing Then Return
            If contesto.Incerto AndAlso _navAttiva AndAlso _navigazione.NumeroVoci > 0 AndAlso _navigazione.EWeb AndAlso _navigazione.Finestra = finestra AndAlso _navigazione.RadiceValida() Then
                Volatile.Write(_fuoriDalDocumento, 0)
                Return
            End If
            If Not contesto.EWeb OrElse Not contesto.DentroDocumento OrElse contesto.Radice Is Nothing Then
                If FuocoPersoNelBrowser(elemento, finestra) Then
                    If _navigazione.NumeroVoci > 0 AndAlso _navigazione.RadiceValida() AndAlso _navigazione.Finestra = finestra AndAlso _navigazione.EWeb Then
                        _navAttiva = True
                        Volatile.Write(_fuoriDalDocumento, 0)
                        ImpostaModalita(True, False)
                        _modoFocusAnnunciato = False
                        Return
                    End If
                    If RecuperaDocumento() Then Return
                End If
                If _navManuale AndAlso finestra = _navigazione.Finestra AndAlso _navAttiva Then
                    _navigazione.Sincronizza(elemento)
                    ImpostaModalita(True, _navigazione.RichiedeScrittura(elemento))
                    Return
                End If
                If _navAttiva Then
                    _navAttiva = False
                    _navManuale = False
                    _navDisattivataPer = ""
                End If
                Volatile.Write(_fuoriDalDocumento, 1)
                ImpostaModalita(False, False)
                _modoFocusAnnunciato = False
                Return
            End If
            If String.Equals(contesto.Chiave, _navDisattivataPer, StringComparison.Ordinal) Then
                _navAttiva = False
                ImpostaModalita(False, False)
                _modoFocusAnnunciato = False
                Return
            End If
            Dim paginaNuova As Boolean = _navigazione.NumeroVoci > 0 AndAlso contesto.Chiave = _navigazione.ChiaveRadice AndAlso _navigazione.PaginaCambiata(contesto.Radice)
            Dim chiaveDiversa As Boolean = contesto.Chiave <> _navigazione.ChiaveRadice OrElse _navigazione.NumeroVoci = 0 OrElse paginaNuova
            Dim proteggiCursore As Boolean = Environment.TickCount - Volatile.Read(_proteggiCursoreFino) < 0
            Dim cursoreSulFuoco As Boolean = False
            Dim cursoreAltrove As Boolean = False
            Dim rientroDaAnnunciare As Boolean = False
            If chiaveDiversa Then
                Dim tieniMessaggio As Boolean = proteggiCursore OrElse _navigazione.RestaNelMessaggio(elemento)
                If _navigazione.NumeroVoci > 0 AndAlso Not _navManuale Then _navigazione.RicordaPosizione(_ultimoFocusNav, Volatile.Read(_cursoreDaTasti) = 1)
                _navigazione.Costruisci(contesto.Radice, finestra, True, contesto.ERiquadro)
                _ultimoCostruisci = Environment.TickCount
                Dim daTasti As Boolean = False
                Dim ripristinato As Boolean = Not tieniMessaggio AndAlso _navigazione.RipristinaPosizione(elemento, daTasti)
                If Not tieniMessaggio AndAlso Not ripristinato AndAlso PaginaAppenaCaricata(elemento, contesto.Chiave) Then _letturaPaginaInAttesa = True
                If tieniMessaggio Then
                    _navigazione.ApriContenuto()
                    cursoreAltrove = True
                ElseIf ripristinato Then
                    _ultimoFocusNav = elemento
                    If daTasti Then
                        cursoreAltrove = True
                        rientroDaAnnunciare = True
                    Else
                        cursoreSulFuoco = True
                    End If
                ElseIf _navigazione.CorpoDentro(elemento) Then
                    Dim aperto As String = _navigazione.EntraNelCorpo(elemento)
                    If aperto = "" Then _navigazione.Sincronizza(elemento)
                    _ultimoFocusNav = elemento
                    If aperto = "" Then
                        cursoreSulFuoco = True
                    Else
                        cursoreAltrove = True
                    End If
                    If aperto <> "" AndAlso Not _messaggioGiaAnnunciato Then Parla(aperto, False)
                Else
                    Dim mail As String = _navigazione.EntraNellaMailSingola(elemento)
                    If mail <> "" Then
                        _ultimoFocusNav = elemento
                        cursoreAltrove = True
                        If Not _messaggioGiaAnnunciato Then Parla(mail, False)
                    Else
                        _navigazione.Sincronizza(elemento)
                        _ultimoFocusNav = elemento
                        cursoreSulFuoco = True
                    End If
                End If
            ElseIf Not proteggiCursore AndAlso (_ultimoFocusNav Is Nothing OrElse Not StessoElemento(elemento, _ultimoFocusNav)) Then
                If _navigazione.RestaNelMessaggio(elemento) OrElse _navigazione.RestaSullaMailSingola(elemento) Then
                    _ultimoFocusNav = elemento
                    cursoreAltrove = True
                ElseIf _navigazione.CorpoDentro(elemento) Then
                    Dim aperto As String = _navigazione.EntraNelCorpo(elemento)
                    If aperto = "" Then _navigazione.Sincronizza(elemento)
                    _ultimoFocusNav = elemento
                    If aperto = "" Then
                        cursoreSulFuoco = True
                    Else
                        cursoreAltrove = True
                    End If
                    If aperto <> "" AndAlso Not _messaggioGiaAnnunciato Then Parla(aperto, False)
                Else
                    Dim mail As String = _navigazione.EntraNellaMailSingola(elemento)
                    If mail <> "" Then
                        _ultimoFocusNav = elemento
                        cursoreAltrove = True
                        If Not _messaggioGiaAnnunciato Then Parla(mail, False)
                    ElseIf _navigazione.ERadice(elemento) OrElse _navigazione.CursoreDentro(elemento) Then
                        _ultimoFocusNav = elemento
                        If _navigazione.CursoreSuElemento(elemento) Then
                            cursoreSulFuoco = True
                        ElseIf Not _navigazione.ERadice(elemento) Then
                            cursoreAltrove = True
                        End If
                    Else
                        _navigazione.ChiudiMessaggio()
                        If _navigazione.Sincronizza(elemento) < 0 AndAlso Environment.TickCount - _ultimoCostruisci > 2000 Then
                            _navigazione.Costruisci(contesto.Radice, finestra, True, contesto.ERiquadro)
                            _ultimoCostruisci = Environment.TickCount
                            _navigazione.Sincronizza(elemento)
                        End If
                        _ultimoFocusNav = elemento
                        cursoreSulFuoco = True
                    End If
                End If
            End If
            If cursoreSulFuoco Then CursoreAlFuoco()
            If _navigazione.ERadice(elemento) AndAlso Not chiaveDiversa Then RitornaSullaPagina(True)
            If cursoreAltrove OrElse _navigazione.EDocumentoORadice(elemento) Then Volatile.Write(_cursoreDaTasti, 1)
            Volatile.Write(_fuoriDalDocumento, 0)
            _navAttiva = _navigazione.NumeroVoci > 0
            _navManuale = False
            Dim scrittura As Boolean = contesto.EScrittura OrElse _navigazione.RichiedeScrittura(elemento)
            Dim controllo As Boolean = Not scrittura AndAlso _navigazione.RichiedeModalitaFocus(elemento)
            If _navigazione.RestaNelMessaggio(elemento) OrElse _navigazione.CorpoDentro(elemento) OrElse _navigazione.RestaSullaMailSingola(elemento) Then controllo = False
            If _navForzataElemento IsNot Nothing Then
                If StessoElemento(elemento, _navForzataElemento) Then
                    scrittura = False
                    controllo = False
                Else
                    _navForzataElemento = Nothing
                End If
            End If
            AnnunciaModalita(_navAttiva AndAlso (scrittura OrElse controllo))
            ImpostaModalita(_navAttiva, scrittura OrElse controllo, controllo)
            If rientroDaAnnunciare AndAlso Not scrittura AndAlso Not controllo Then AnnunciaRientro()
            If _letturaPaginaInAttesa Then
                _letturaPaginaInAttesa = False
                If _navAttiva AndAlso Not scrittura AndAlso Not controllo AndAlso LeggiPaginaAlCaricamento Then EseguiSuOperazioni("letturaPagina", AddressOf AvviaLetturaPagina, sostituisci:=True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiornaContestoWeb: " & ex.Message)
        End Try
    End Sub

    Private Function SpostaRevisione(comando As ComandoLettore) As String
        Try
            If TerminaleInPrimoPiano() Then
                Dim daTerminale As String = SpostaRevisioneTerminale(comando)
                If daTerminale <> "" Then Return daTerminale
            End If
            If RevisionePiattaAttiva() Then
                Dim piatta As String = SpostaRevisionePiatta(comando)
                If piatta <> "" Then Return piatta
            End If
            If _automazione Is Nothing Then Return ""
            Dim origine As IUIAutomationElement = _revisione
            If origine Is Nothing Then origine = ElementoConFocus()
            If origine Is Nothing Then Return "Nessun elemento"
            Dim walker As IUIAutomationTreeWalker = _automazione.ControlViewWalker
            If walker Is Nothing Then Return ""
            Dim destinazione As IUIAutomationElement = Nothing
            Select Case comando
                Case ComandoLettore.Precedente
                    destinazione = walker.GetPreviousSiblingElementBuildCache(origine, _richiestaCache)
                Case ComandoLettore.Successivo
                    destinazione = walker.GetNextSiblingElementBuildCache(origine, _richiestaCache)
                Case ComandoLettore.Contenitore
                    destinazione = walker.GetParentElementBuildCache(origine, _richiestaCache)
                Case ComandoLettore.Contenuto
                    destinazione = walker.GetFirstChildElementBuildCache(origine, _richiestaCache)
            End Select
            If destinazione Is Nothing Then
                Dim daMsaa As String = AccessibilitaMsaa.Naviga(origine, comando = ComandoLettore.Contenitore, comando = ComandoLettore.Contenuto, comando = ComandoLettore.Successivo)
                If daMsaa <> "" Then Return daMsaa
                Dim limite As String
                Select Case comando
                    Case ComandoLettore.Contenitore
                        limite = "Non c'è un contenitore"
                    Case ComandoLettore.Contenuto
                        limite = "Non c'è contenuto"
                    Case Else
                        limite = "Non ci sono altri elementi"
                End Select
                SuonaConfine(limite)
                Return limite
            End If
            _revisione = destinazione
            Return _descrittore.Descrivi(destinazione)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SpostaRevisione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoDescrizione() As String
        Try
            Dim elemento As IUIAutomationElement = Nothing
            If InVirtuale() Then elemento = _navigazione.ElementoCorrente()
            If elemento Is Nothing Then elemento = _revisione
            If elemento Is Nothing Then elemento = ElementoConFocus()
            If elemento Is Nothing Then Return "Nessun elemento"
            Dim aiuto As String = _descrittore.TestoAiuto(elemento)
            Dim accesso As String = _descrittore.TastoAccesso(elemento)
            If aiuto = "" AndAlso accesso = "" Then Return "Nessuna descrizione"
            If aiuto <> "" AndAlso accesso <> "" Then Return aiuto & ", tasto " & accesso
            If accesso <> "" Then Return "Tasto " & accesso
            Return aiuto
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoDescrizione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeUiaUtile(nome As String) As Boolean
        Try
            If nome Is Nothing Then Return False
            Dim pulito As String = nome.Trim()
            If pulito.Length < 2 Then Return False
            Select Case pulito.ToLowerInvariant()
                Case "client", "pannello", "riquadro", "finestra", "documento", "table", "tabella", "gruppo", "custom"
                    Return False
            End Select
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.NomeUiaUtile", exAtteso)
            Return False
        End Try
    End Function

    Private Function InVirtuale() As Boolean
        Try
            Return _navAttiva AndAlso Volatile.Read(_tastiVirtuali) = 1 AndAlso _navigazione IsNot Nothing AndAlso _navigazione.NumeroVoci > 0
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ImpostaModalita(virtuale As Boolean, scrittura As Boolean, Optional lasciaEsc As Boolean = False)
        Try
            Interlocked.Exchange(_tastiVirtuali, If(virtuale AndAlso Not scrittura, 1, 0))
            Interlocked.Exchange(_escScrittura, If(scrittura AndAlso _navAttiva AndAlso Not lasciaEsc, 1, 0))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ImpostaModalita: " & ex.Message)
        End Try
    End Sub

    Private Function ValoreSilenziato() As Boolean
        Try
            Return Environment.TickCount - Volatile.Read(_silenzioValoreFino) < 0
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ValoreSilenziato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SilenziaValore(millisecondi As Integer)
        Try
            Volatile.Write(_silenzioValoreFino, Environment.TickCount + millisecondi)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SilenziaValore: " & ex.Message)
        End Try
    End Sub

    Private Function Accorcia(testo As String) As String
        Try
            If testo Is Nothing Then Return ""
            Dim pulito As String = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If pulito.Length > 180 Then Return pulito.Substring(0, 180)
            Return pulito
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Accorcia: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoOra() As String
        Try
            Dim adesso As DateTime = DateTime.Now
            If adesso.Minute = 0 Then Return "Ore " & adesso.Hour.ToString() & " in punto"
            Return "Ore " & adesso.Hour.ToString() & " e " & adesso.Minute.ToString()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoOra: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoData() As String
        Try
            Return DateTime.Now.ToString("dddd d MMMM yyyy", New CultureInfo("it-IT"))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoData: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoParteBarraStato(figlio As IUIAutomationElement, tutti As IUIAutomationCondition) As String
        Try
            If figlio Is Nothing Then Return ""
            Dim nome As String = _descrittore.Nome(figlio)
            If _descrittore.TipoControllo(figlio) = DescrittoreElementi.ControlloGruppo Then
                Dim nipoti As IUIAutomationElementArray = figlio.FindAllBuildCache(TreeScope.TreeScope_Children, tutti, _richiestaCache)
                If nipoti IsNot Nothing AndAlso nipoti.Length = 1 AndAlso _descrittore.TipoControllo(nipoti.GetElement(0)) = DescrittoreElementi.ControlloTesto Then
                    Dim unico As String = _descrittore.Nome(nipoti.GetElement(0))
                    If unico <> "" Then Return unico
                End If
                If nipoti IsNot Nothing AndAlso nipoti.Length > 1 Then
                    Dim tuttiOpzioni As Boolean = True
                    Dim scelta As String = ""
                    For indice As Integer = 0 To nipoti.Length - 1
                        Dim nipote As IUIAutomationElement = nipoti.GetElement(indice)
                        If _descrittore.TipoControllo(nipote) <> DescrittoreElementi.ControlloPulsanteOpzione Then
                            tuttiOpzioni = False
                            Exit For
                        End If
                        If scelta = "" AndAlso _descrittore.LeggiBooleano(nipote, DescrittoreElementi.ProprietaSelezionato) Then scelta = _descrittore.Nome(nipote)
                    Next
                    If tuttiOpzioni Then Return (nome & " " & scelta).Trim()
                End If
            End If
            Dim valore As String = _descrittore.LeggiStringa(figlio, DescrittoreElementi.ProprietaValore)
            If valore <> "" AndAlso Not String.Equals(valore, nome, StringComparison.CurrentCulture) Then Return (nome & " " & valore).Trim()
            Return nome
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoParteBarraStato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoBarraStato() As String
        Try
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return "Barra di stato non trovata"
            Dim daModulo As String = OttieniModuloApp(finestra).TestoBarraStatoApp(finestra)
            If daModulo <> "" Then Return daModulo
            If FinestraChromium(finestra) Then Return "Barra di stato non trovata"
            Dim radice As IUIAutomationElement = _automazione.ElementFromHandle(finestra)
            If radice Is Nothing Then Return "Barra di stato non trovata"
            Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloBarraStato)
            Dim barra As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, _richiestaCache)
            If barra Is Nothing Then Return "Barra di stato non trovata"
            Dim parti As New List(Of String)()
            Dim tutti As IUIAutomationCondition = _automazione.CreateTrueCondition()
            Dim figli As IUIAutomationElementArray = barra.FindAllBuildCache(TreeScope.TreeScope_Children, tutti, _richiestaCache)
            If figli IsNot Nothing Then
                For indice As Integer = 0 To figli.Length - 1
                    Dim parte As String = TestoParteBarraStato(figli.GetElement(indice), tutti)
                    If parte <> "" Then parti.Add(parte)
                Next
            End If
            If parti.Count = 0 Then
                Dim nomeBarra As String = _descrittore.Nome(barra)
                If nomeBarra <> "" Then parti.Add(nomeBarra)
            End If
            If parti.Count = 0 Then Return "Barra di stato vuota"
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoBarraStato: " & ex.Message)
            Return "Barra di stato non disponibile"
        End Try
    End Function

    Private Function TestiFinestra(finestra As IntPtr, titolo As String) As String
        Try
            If finestra = IntPtr.Zero OrElse _automazione Is Nothing OrElse _walkerControllo Is Nothing Then Return ""
            Dim radice As IUIAutomationElement = AutomazioneRapida().ElementFromHandleBuildCache(finestra, CacheRapida())
            If radice Is Nothing Then Return ""
            Dim parti As New List(Of String)()
            Dim visitati As Integer = 0
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            RaccogliTestiDialogo(radice, titolo, parti, 0, visitati, orologio)
            Return String.Join(", ", parti)
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.TestiFinestra: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RaccogliTestiDialogo(elemento As IUIAutomationElement, titolo As String, parti As List(Of String), profondita As Integer, ByRef visitati As Integer, orologio As Stopwatch)
        Try
            If elemento Is Nothing OrElse profondita > 6 Then Return
            Dim camminatore As IUIAutomationTreeWalker = CamminatoreRapido()
            Dim cache As IUIAutomationCacheRequest = CacheRapida()
            Dim figlio As IUIAutomationElement = camminatore.GetFirstChildElementBuildCache(elemento, cache)
            While figlio IsNot Nothing
                If visitati >= 300 OrElse parti.Count >= MassimoTestiFinestra OrElse orologio.ElapsedMilliseconds > 700L Then Return
                visitati += 1
                Select Case _descrittore.TipoControllo(figlio)
                    Case DescrittoreElementi.ControlloTesto
                        Dim nome As String = _descrittore.Nome(figlio)
                        If nome <> "" AndAlso Not String.Equals(nome, titolo, StringComparison.CurrentCulture) AndAlso Not parti.Contains(nome) Then parti.Add(nome)
                    Case DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloFinestra
                        If Not ElementoWeb(figlio) Then RaccogliTestiDialogo(figlio, titolo, parti, profondita + 1, visitati, orologio)
                End Select
                figlio = camminatore.GetNextSiblingElementBuildCache(figlio, cache)
            End While
        Catch ex As Exception
            If Not ElementoAssente(ex) AndAlso Not TimeoutOperazione(ex) Then log.ScriviLog("WGLettoreSchermo.RaccogliTestiDialogo: " & ex.Message)
        End Try
    End Sub

    Private Function ClasseCommutatore(classe As String) As Boolean
        Return classe = "MultitaskingViewFrame" OrElse classe = "XamlExplorerHostIslandWindow" OrElse classe = "TaskSwitcherWnd" OrElse classe = "TaskSwitcherOverlayWnd" OrElse classe = "#32771"
    End Function

    Friend Function CommutatoreAperto() As Boolean
        Try
            Return ECommutatore(NativoLettore.GetForegroundWindow())
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.CommutatoreAperto", exAtteso)
            Return False
        End Try
    End Function

    Friend Sub AlRilascioAlt()
        Volatile.Write(_commutatoreAttivitaTick, Environment.TickCount)
        _ultimoCommutatore = ""
        _ultimoCommutatoreDaTasto = False
        _ultimoCommutatoreNome = ""
        _ultimaVoceCommutatoreChiave = ""
        _commutatoreRadice = Nothing
        _commutatoreRadiceFinestra = IntPtr.Zero
    End Sub

    Private Function ECommutatore(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Return ClasseCommutatore(ClasseFinestra(finestra))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ECommutatore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FinestraShellSconosciuta(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As String = ClasseFinestra(finestra)
            Select Case classe
                Case "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "CabinetWClass", "ExploreWClass"
                    Return False
            End Select
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI OrElse EProcessoWinGabriel(pid) Then Return False
            Dim noto As Boolean = False
            If _processiShell.TryGetValue(pid, noto) Then Return noto
            Using processo As Process = Process.GetProcessById(CInt(pid))
                noto = String.Equals(processo.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
            End Using
            _processiShell(pid) = noto
            Return noto
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FinestraShellSconosciuta", ex)
            Return False
        End Try
    End Function

    Private Function FinestraDesktop(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As String = ClasseFinestra(finestra)
            Return classe = "Progman" OrElse classe = "WorkerW"
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraDesktop: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function NomeDesktop(nome As String) As Boolean
        Return String.Equals(nome, "Program Manager", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function EAntenato(antenato As IUIAutomationElement, elemento As IUIAutomationElement) As Boolean
        Try
            If antenato Is Nothing OrElse elemento Is Nothing OrElse _walkerControllo Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = _walkerControllo.GetParentElement(elemento)
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Return False
                If StessoElemento(corrente, antenato) Then Return True
                corrente = _walkerControllo.GetParentElement(corrente)
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EAntenato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TrovaFinestraCommutatore() As IntPtr
        Try
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If ECommutatore(primo) Then Return primo
            If Not AltPremuto() Then Return IntPtr.Zero
            If FinestraShellSconosciuta(primo) Then Return primo
            Dim ricerca As New RicercaFinestraCommutatore(Me)
            Dim procedura As New NativoLettore.ProceduraFinestre(AddressOf ricerca.Procedura)
            NativoLettore.EnumWindows(procedura, IntPtr.Zero)
            GC.KeepAlive(procedura)
            Return ricerca.Trovata
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TrovaFinestraCommutatore: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function VoceCommutatoreValida(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim nomeVoce As String = _descrittore.NomeCorrente(elemento)
            If nomeVoce = "" AndAlso (_descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloElementoElenco OrElse _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0) <= 0) Then Return False
            If NomeDesktopVirtuale(nomeVoce) Then Return False
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloFinestra, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloElenco,
                     DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloBarraTitolo, DescrittoreElementi.ControlloBarraStrumenti,
                     DescrittoreElementi.ControlloAlbero, DescrittoreElementi.ControlloTabella, DescrittoreElementi.ControlloGrigliaDati
                    Return False
            End Select
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.VoceCommutatoreValida", exAtteso)
            Return False
        End Try
    End Function

    Private Function VoceCommutatore(radice As IUIAutomationElement, finestra As IntPtr) As IUIAutomationElement
        Try
            Dim condizioneElenco As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloElementoElenco)
            Dim voci As IUIAutomationElementArray = radice.FindAllBuildCache(TreeScope.TreeScope_Descendants, condizioneElenco, CacheCommutatore())
            If voci IsNot Nothing Then
                Dim primoConFuoco As IUIAutomationElement = Nothing
                For indice As Integer = 0 To Math.Min(voci.Length, 60) - 1
                    Dim voce As IUIAutomationElement = voci.GetElement(indice)
                    Dim nomeVoce As String = _descrittore.NomeCorrente(voce)
                    If NomeDesktopVirtuale(nomeVoce) Then Continue For
                    If nomeVoce = "" AndAlso _descrittore.LeggiIntero(voce, DescrittoreElementi.ProprietaPosizione, 0) <= 0 Then Continue For
                    If _descrittore.LeggiBooleano(voce, DescrittoreElementi.ProprietaSelezionato) Then Return voce
                    If primoConFuoco Is Nothing AndAlso _descrittore.LeggiBooleano(voce, DescrittoreElementi.ProprietaFuocoTastiera) Then primoConFuoco = voce
                Next
                If primoConFuoco IsNot Nothing Then Return primoConFuoco
            End If
            Dim conFuoco As IUIAutomationElement = ElementoConFocus()
            If conFuoco IsNot Nothing AndAlso VoceCommutatoreValida(conFuoco) Then
                If StessoElemento(conFuoco, radice) = False AndAlso (EAntenato(radice, conFuoco) OrElse _descrittore.IdProcesso(conFuoco) = _descrittore.IdProcesso(radice) AndAlso finestra = NativoLettore.GetForegroundWindow()) Then
                    Return conFuoco
                End If
            End If
            Dim selezionata As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaSelezionato, True)
            Dim qualunque As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, selezionata, CacheCommutatore())
            If qualunque IsNot Nothing AndAlso VoceCommutatoreValida(qualunque) Then Return qualunque
            Dim fuoco As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaFuocoTastiera, True)
            qualunque = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, fuoco, CacheCommutatore())
            If qualunque IsNot Nothing AndAlso VoceCommutatoreValida(qualunque) Then Return qualunque
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VoceCommutatore: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Sub EseguiLetturaCommutatore(azione As Action)
        Try
            Dim corsia As CodaOperazioni = _operazioniCommutatore
            If corsia Is Nothing Then
                azione.Invoke()
                Return
            End If
            corsia.Esegui("commutatore", azione, sostituisci:=False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiLetturaCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSuOperazioni(chiave As String, azione As Action, Optional sostituisci As Boolean = False)
        Try
            Dim operazioni As CodaOperazioni = _operazioni
            If operazioni Is Nothing Then
                If Volatile.Read(_inEsecuzione) = 1 Then azione.Invoke()
                Return
            End If
            operazioni.Esegui(chiave, azione, sostituisci)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiSuOperazioni: " & ex.Message)
        End Try
    End Sub

    Private Function CacheCommutatore() As IUIAutomationCacheRequest
        Dim leggera As IUIAutomationCacheRequest = _richiestaCacheCommutatore
        If leggera Is Nothing Then Return _richiestaCache
        Return leggera
    End Function

    Private Function RadiceCommutatore(finestra As IntPtr) As IUIAutomationElement
        Try
            If finestra = _commutatoreRadiceFinestra AndAlso _commutatoreRadice IsNot Nothing Then Return _commutatoreRadice
            Dim radice As IUIAutomationElement = Nothing
            Try
                radice = _automazione.ElementFromHandleBuildCache(finestra, CacheCommutatore())
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RadiceCommutatore", exAtteso)
                radice = Nothing
            End Try
            _commutatoreRadiceFinestra = finestra
            _commutatoreRadice = radice
            Return radice
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RadiceCommutatore: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function LeggiCommutatore(tentativi As Integer, Optional attendiCambio As Boolean = False, Optional generazione As Integer = -1) As Boolean
        Try
            If _automazione Is Nothing Then Return False
            If generazione < 0 Then generazione = Volatile.Read(_commutatoreGenerazione)
            Dim attesaCambio As Integer = 0
            For indice As Integer = 1 To tentativi
                If Volatile.Read(_commutatoreGenerazione) <> generazione Then Return False
                Dim finestra As IntPtr = TrovaFinestraCommutatore()
                If finestra <> IntPtr.Zero Then
                    Dim radice As IUIAutomationElement = RadiceCommutatore(finestra)
                    If radice IsNot Nothing Then
                        Dim voce As IUIAutomationElement = VoceCommutatore(radice, finestra)
                        If voce IsNot Nothing Then
                            Dim chiaveVoce As String = ChiaveElemento(voce) & "|" & _descrittore.Nome(voce)
                            If attendiCambio AndAlso attesaCambio < 7 Then
                                If _ultimaVoceCommutatoreChiave <> "" AndAlso String.Equals(chiaveVoce, _ultimaVoceCommutatoreChiave, StringComparison.Ordinal) Then
                                    attesaCambio += 1
                                    Thread.Sleep(40)
                                    Continue For
                                End If
                            End If
                            If Volatile.Read(_commutatoreGenerazione) <> generazione Then Return False
                            _ultimaVoceCommutatoreChiave = chiaveVoce
                            AnnunciaCommutatore(voce, attendiCambio)
                            Return True
                        End If
                        Dim classe As String = ClasseFinestra(finestra)
                        If classe = "#32771" OrElse classe = "TaskSwitcherWnd" Then
                            Dim testi As String = TestiFinestra(finestra, "")
                            If testi = "" Then testi = TitoloFinestra(finestra)
                            If testi <> "" Then
                                AnnunciaTestoCommutatore(testi)
                                Return True
                            End If
                        End If
                    End If
                End If
                If finestra = IntPtr.Zero AndAlso Not AltPremuto() Then Return False
                If indice < tentativi Then Thread.Sleep(40)
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiCommutatore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SorvegliaMenuStart()
        Try
            If Sospeso OrElse _automazione Is Nothing OrElse _descrittore Is Nothing Then Return
            If SilenzioEsterno() Then Return
            If Not EventiRecenti(1500) Then Return
            If MillisecondiDa(_startUltimoControllo) < 150L Then Return
            _startUltimoControllo = Environment.TickCount
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If primo = IntPtr.Zero Then Return
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(primo, pid)
            If pid = 0UI OrElse EProcessoWinGabriel(pid) Then Return
            Select Case NomeEseguibile(pid)
                Case "searchhost", "searchapp", "searchui", "startmenuexperiencehost", "shellexperiencehost"
                Case Else
                    _ultimaSelezioneStart = ""
                    Return
            End Select
            Dim radice As IUIAutomationElement = _automazione.ElementFromHandleBuildCache(primo, _richiestaCache)
            If radice Is Nothing Then Return
            Dim selezionati As IUIAutomationElementArray = radice.FindAllBuildCache(TreeScope.TreeScope_Descendants, _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaSelezionato, True), _richiestaCache)
            If selezionati Is Nothing Then Return
            Dim voce As IUIAutomationElement = Nothing
            For indice As Integer = 0 To Math.Min(selezionati.Length, 40) - 1
                Dim candidato As IUIAutomationElement = selezionati.GetElement(indice)
                Dim tipo As Integer = _descrittore.TipoControllo(candidato)
                If (tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero) AndAlso _descrittore.NomeCorrente(candidato) <> "" Then
                    voce = candidato
                    Exit For
                End If
            Next
            If voce Is Nothing Then Return
            Dim chiave As String = ChiaveElemento(voce) & "|" & _descrittore.NomeCorrente(voce)
            If String.Equals(chiave, _ultimaSelezioneStart, StringComparison.Ordinal) Then Return
            _ultimaSelezioneStart = chiave
            Dim fuoco As IUIAutomationElement = ElementoConFocus()
            If fuoco IsNot Nothing AndAlso StessoElemento(fuoco, voce) Then Return
            Dim testo As String = _descrittore.Descrivi(voce)
            If testo = "" Then Return
            Parla(testo, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SorvegliaMenuStart: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaTestoCommutatore(testo As String)
        Try
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(testo, _ultimoCommutatore, StringComparison.CurrentCulture) AndAlso adesso - _ultimoCommutatoreTick < 250 Then Return
            _ultimoCommutatore = testo
            _ultimoCommutatoreTick = adesso
            _ultimoCommutatoreDaTasto = True
            _ultimoFocusTick = adesso
            Volatile.Write(_commutatoreAttivitaTick, adesso)
            Volatile.Write(_commutatoreAnnunciato, 1)
            ParlaCommutatore(testo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaTestoCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Function FinestraDiSistema(finestra As IntPtr) As Boolean
        Try
            Select Case ClasseFinestra(finestra)
                Case "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
                    Return True
            End Select
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FinestraDiSistema", exAtteso)
            Return False
        End Try
    End Function

    Private Function NomeFileEseguibile(pid As UInteger) As String
        Dim processo As IntPtr = IntPtr.Zero
        Try
            If pid = 0UI OrElse EProcessoWinGabriel(pid) Then Return ""
            processo = NativoLettore.OpenProcess(&H1000UI, False, pid)
            If processo = IntPtr.Zero Then Return ""
            Dim percorso As New StringBuilder(1024)
            Dim dimensione As UInteger = CUInt(percorso.Capacity)
            If Not NativoLettore.QueryFullProcessImageName(processo, 0UI, percorso, dimensione) Then Return ""
            Dim nome As String = System.IO.Path.GetFileNameWithoutExtension(percorso.ToString())
            If String.Equals(nome, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) Then Return ""
            Return If(nome, "").Trim()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeFileEseguibile: " & ex.Message)
            Return ""
        Finally
            If processo <> IntPtr.Zero Then NativoLettore.CloseHandle(processo)
        End Try
    End Function

    Private Shared Function SenzaEstensioneEseguibile(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim pulito As String = testo.Trim()
            If pulito.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
                Dim barra As Integer = pulito.LastIndexOfAny(New Char() {"\"c, "/"c})
                If barra >= 0 Then pulito = pulito.Substring(barra + 1)
                pulito = pulito.Substring(0, pulito.Length - 4).Trim()
            End If
            Return pulito
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SenzaEstensioneEseguibile", ex)
            Return ""
        End Try
    End Function

    Private Function TitoloEseguibile(pid As UInteger, titolo As String) As Boolean
        Try
            If pid = 0UI OrElse String.IsNullOrWhiteSpace(titolo) Then Return False
            Dim eseguibile As String = NomeEseguibile(pid)
            If eseguibile = "" Then Return False
            Dim confronto As String = titolo.Trim().ToLowerInvariant()
            Return confronto = eseguibile OrElse confronto = eseguibile & ".exe"
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TitoloEseguibile", exAtteso)
            Return False
        End Try
    End Function

    Private Function NomeProgramma(pid As UInteger) As String
        Dim nome As String = Nothing
        If _nomiProgramma.TryGetValue(pid, nome) Then Return nome
        nome = ""
        Dim processo As IntPtr = IntPtr.Zero
        Try
            If pid <> 0UI AndAlso Not EProcessoWinGabriel(pid) Then
                processo = NativoLettore.OpenProcess(&H1000UI, False, pid)
                If processo <> IntPtr.Zero Then
                    Dim percorso As New StringBuilder(1024)
                    Dim dimensione As UInteger = CUInt(percorso.Capacity)
                    If NativoLettore.QueryFullProcessImageName(processo, 0UI, percorso, dimensione) Then
                        Dim file As String = percorso.ToString()
                        If String.Equals(System.IO.Path.GetFileNameWithoutExtension(file), "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) Then
                            nome = "*"
                        ElseIf String.Equals(System.IO.Path.GetFileNameWithoutExtension(file), "explorer", StringComparison.OrdinalIgnoreCase) Then
                            nome = "Esplora file"
                        Else
                            Dim descrizione As String = FileVersionInfo.GetVersionInfo(file).FileDescription
                            If descrizione IsNot Nothing Then nome = SenzaEstensioneEseguibile(descrizione)
                            If nome <> "" Then
                                Dim nomeFile As String = System.IO.Path.GetFileName(file)
                                Dim senzaEstensione As String = System.IO.Path.GetFileNameWithoutExtension(file)
                                If String.Equals(nome, senzaEstensione, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(nome, nomeFile, StringComparison.OrdinalIgnoreCase) Then nome = ""
                            End If
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeProgramma: " & ex.Message)
        Finally
            If processo <> IntPtr.Zero Then NativoLettore.CloseHandle(processo)
        End Try
        _nomiProgramma(pid) = nome
        Return nome
    End Function

    Private Function NomeApplicazione(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            Dim nome As String = NomeProgramma(pid)
            If nome <> "*" Then Return nome
            Dim inCache As VoceNomeApplicazione = Nothing
            If _nomiApplicazioneFinestra.TryGetValue(finestra, inCache) Then
                Dim validita As Long = If(inCache.Nome <> "", 120000L, 4000L)
                If MillisecondiDa(inCache.Tick) < validita Then Return inCache.Nome
            End If
            If _nomiApplicazioneFinestra.Count > 300 Then _nomiApplicazioneFinestra.Clear()
            Dim trovato As String = ""
            Dim figlio As IntPtr = NativoLettore.FindWindowEx(finestra, IntPtr.Zero, "Windows.UI.Core.CoreWindow", Nothing)
            If figlio <> IntPtr.Zero Then
                Dim pidFiglio As UInteger = 0UI
                NativoLettore.GetWindowThreadProcessId(figlio, pidFiglio)
                If pidFiglio <> 0UI AndAlso pidFiglio <> pid Then
                    Dim reale As String = NomeProgramma(pidFiglio)
                    If reale <> "*" Then trovato = reale
                End If
                If trovato = "" Then trovato = TitoloFinestra(figlio)
            End If
            _nomiApplicazioneFinestra(finestra) = New VoceNomeApplicazione With {.Nome = trovato, .Tick = Environment.TickCount}
            Return trovato
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeApplicazione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeApplicazionePerTitolo(titolo As String) As String
        Try
            If titolo = "" Then Return ""
            Dim trovata As IntPtr = FinestraVisibilePerTitolo(titolo, True)
            If trovata = IntPtr.Zero Then Return ""
            Return NomeApplicazione(trovata)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeApplicazionePerTitolo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TitoloVoceEseguibile(titolo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return False
            Dim trovata As IntPtr = FinestraVisibilePerTitolo(titolo, False)
            If trovata = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(trovata, pid)
            Return TitoloEseguibile(pid, titolo)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TitoloVoceEseguibile", exAtteso)
            Return False
        End Try
    End Function

    Private Function FinestraVisibilePerTitolo(titolo As String, saltaCommutatore As Boolean) As IntPtr
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return IntPtr.Zero
            Dim chiave As String = If(saltaCommutatore, "n|", "s|") & titolo
            Dim inCache As VoceCacheFinestra
            If _cacheFinestrePerTitolo.TryGetValue(chiave, inCache) AndAlso MillisecondiDa(inCache.Tick) < 3000L Then Return inCache.Maniglia
            Dim ricerca As New RicercaFinestraTitolo(Me, titolo, saltaCommutatore)
            Dim procedura As New NativoLettore.ProceduraFinestre(AddressOf ricerca.Procedura)
            NativoLettore.EnumWindows(procedura, IntPtr.Zero)
            GC.KeepAlive(procedura)
            Dim trovata As IntPtr = ricerca.Trovata
            _cacheFinestrePerTitolo(chiave) = New VoceCacheFinestra With {.Maniglia = trovata, .Tick = Environment.TickCount}
            Return trovata
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraVisibilePerTitolo: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function AltPremuto() As Boolean
        Try
            Return (NativoLettore.GetAsyncKeyState(&H12) And &H8000S) <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AltPremuto", exAtteso)
            Return False
        End Try
    End Function

    Friend Sub AltPremutoTasto()
        Try
            Volatile.Write(_ultimoTastoAltTick, Environment.TickCount)
            If _operazioni Is Nothing OrElse _descrittore Is Nothing OrElse _ultimoElemento Is Nothing Then Return
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            EseguiSuOperazioni("tastoAlt", AddressOf TastoAltAlModulo, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AltPremutoTasto: " & ex.Message)
        End Try
    End Sub

    Friend Function AltRecente(millisecondi As Integer) As Boolean
        Return MillisecondiDa(Volatile.Read(_ultimoTastoAltTick)) < CLng(millisecondi)
    End Function

    Private Function InCommutatore(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            If ECommutatore(finestra) Then Return True
            Return AltPremuto() AndAlso FinestraShellSconosciuta(finestra)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.InCommutatore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FraseCommutatore(elemento As IUIAutomationElement) As String
        Try
            Dim nome As String = _descrittore.NomeCorrente(elemento)
            If nome = "" Then nome = NomeVoceSenzaNome(elemento)
            If nome = "" Then nome = _descrittore.Descrivi(elemento)
            If nome = "" Then Return ""
            If TitoloVoceEseguibile(nome) Then
                Dim alPosto As String = NomeApplicazionePerTitolo(nome)
                If alPosto = "" Then alPosto = SenzaEstensioneEseguibile(nome)
                If alPosto = "" Then Return ""
                nome = alPosto
            End If
            nome = SenzaEstensioneEseguibile(nome)
            If nome = "" Then Return ""
            Dim posizione As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            Dim totale As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
            If posizione > 0 AndAlso totale > 0 AndAlso posizione <= totale Then Return nome & ", " & posizione.ToString() & " di " & totale.ToString()
            Return nome
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FraseCommutatore: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeVoceSenzaNome(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim posizione As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            If posizione <= 0 Then Return ""
            Dim finestra As IntPtr = FinestraAltTab(posizione)
            If finestra = IntPtr.Zero Then Return ""
            Dim titolo As String = TitoloFinestra(finestra)
            If titolo <> "" Then Return titolo
            Dim applicazione As String = NomeApplicazione(finestra)
            If applicazione <> "" AndAlso applicazione <> "*" Then Return applicazione
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            Return NomeFileEseguibile(pid)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.NomeVoceSenzaNome: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function FinestraAltTab(posizione As Integer) As IntPtr
        Try
            If posizione <= 0 Then Return IntPtr.Zero
            Dim raccolta As New RaccoltaAltTab()
            Dim procedura As New NativoLettore.ProceduraFinestre(AddressOf raccolta.Raccogli)
            NativoLettore.EnumWindows(procedura, IntPtr.Zero)
            GC.KeepAlive(procedura)
            If posizione > raccolta.Finestre.Count Then Return IntPtr.Zero
            Return raccolta.Finestre(posizione - 1)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraAltTab: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private _famigliaChiave As String = ""
    Private _famigliaTick As Integer
    Private _famigliaFigli As List(Of IUIAutomationElement)
    Private _famigliaUltimo As Integer

    ' Posizione dell'elemento fra i fratelli dello stesso tipo e con un nome. L'elenco dei fratelli si tiene per un attimo e si parte
    ' dall'ultima posizione trovata: spostandosi con le frecce basta guardare i vicini invece di confrontare tutta la lista a ogni passo.
    Private Sub PosizioneTraFratelli(elemento As IUIAutomationElement, ByRef posizione As Integer, ByRef totale As Integer, Optional leggera As Boolean = False)
        posizione = 0
        totale = 0
        Try
            If _walkerControllo Is Nothing OrElse _automazione Is Nothing Then Return
            Dim richiestaFratelli As IUIAutomationCacheRequest = If(leggera, CacheCommutatore(), _richiestaCache)
            Dim genitore As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(elemento, richiestaFratelli)
            If genitore Is Nothing Then Return
            Dim tipo As Integer = _descrittore.TipoControllo(elemento)
            Dim chiave As String = ChiaveElemento(genitore) & "|" & tipo.ToString()
            Dim figli As List(Of IUIAutomationElement) = _famigliaFigli
            Dim memoValida As Boolean = figli IsNot Nothing AndAlso chiave.Length > 1 AndAlso String.Equals(chiave, _famigliaChiave, StringComparison.Ordinal) AndAlso Environment.TickCount - _famigliaTick >= 0 AndAlso Environment.TickCount - _famigliaTick < 1500
            If Not memoValida Then
                Dim tutti As IUIAutomationElementArray = genitore.FindAllBuildCache(TreeScope.TreeScope_Children, _automazione.CreateTrueCondition(), richiestaFratelli)
                If tutti Is Nothing Then Return
                figli = New List(Of IUIAutomationElement)(tutti.Length)
                For indice As Integer = 0 To tutti.Length - 1
                    Dim figlio As IUIAutomationElement = tutti.GetElement(indice)
                    If _descrittore.TipoControllo(figlio) <> tipo Then Continue For
                    If _descrittore.Nome(figlio) = "" Then Continue For
                    figli.Add(figlio)
                Next
                _famigliaFigli = figli
                _famigliaChiave = chiave
                _famigliaUltimo = 0
            End If
            _famigliaTick = Environment.TickCount
            Dim trovato As Integer = -1
            Dim vicini As Integer() = {0, 1, -1, 2, -2, 3, -3}
            For Each scarto As Integer In vicini
                Dim indice As Integer = _famigliaUltimo + scarto
                If indice < 0 OrElse indice >= figli.Count Then Continue For
                If StessoElemento(figli(indice), elemento) Then
                    trovato = indice
                    Exit For
                End If
            Next
            If trovato < 0 Then
                Dim orologio As Stopwatch = Stopwatch.StartNew()
                For indice As Integer = 0 To figli.Count - 1
                    If orologio.ElapsedMilliseconds > 400L Then Exit For
                    If StessoElemento(figli(indice), elemento) Then
                        trovato = indice
                        Exit For
                    End If
                Next
            End If
            If trovato >= 0 Then
                _famigliaUltimo = trovato
                posizione = trovato + 1
                totale = figli.Count
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PosizioneTraFratelli: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviConColonne(elemento As IUIAutomationElement, Optional completo As Boolean = False) As String
        LetturaTabelle.Completo = completo
        Dim testo As String = _descrittore.Descrivi(elemento)
        LetturaTabelle.Completo = False
        testo = VoceElencoSenzaNome(elemento, testo)
        testo = LetturaAlberi.Arricchisci(testo, elemento, _descrittore, _walkerControllo, _richiestaCache, completo)
        Try
            RicordaListaNativa(elemento)
            If testo <> "" AndAlso LeggiColonne Then testo = AggiungiColonneRiga(elemento, testo)
            testo = DescriviAlberoNativo(elemento, testo)
            Dim suffisso As String = LetturaStrutturaDocumento.Suffisso(elemento, _descrittore)
            If suffisso <> "" Then testo = If(testo = "", suffisso, testo & ", " & suffisso)
            If ServeMsaa(elemento) Then
                Dim msaa As String = AccessibilitaMsaa.DescriviElemento(elemento)
                If msaa <> "" AndAlso (testo = "" OrElse testo.IndexOf(msaa, StringComparison.CurrentCultureIgnoreCase) < 0) Then
                    testo = If(testo = "", msaa, testo & ", " & msaa)
                End If
            End If
            testo = AggiungiPosizioneFinale(elemento, testo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.DescriviConColonne: " & ex.Message)
        End Try
        Return testo
    End Function

    Private Function ServeMsaa(elemento As IUIAutomationElement) As Boolean
        Try
            If ElementoWeb(elemento) Then Return False
            If Not NomeUiaUtile(_descrittore.Nome(elemento)) Then Return True
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloFinestra
                    Return _descrittore.Nome(elemento).Length < 2
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ServeMsaa: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaCommutatore(elemento As IUIAutomationElement, Optional daTasto As Boolean = False)
        Try
            If elemento Is Nothing Then Return
            Dim testo As String = FraseCommutatore(elemento)
            If testo = "" Then Return
            Dim adesso As Integer = Environment.TickCount
            Dim finestraDoppio As Integer = If(daTasto AndAlso _ultimoCommutatoreDaTasto, 250, 2000)
            If String.Equals(testo, _ultimoCommutatore, StringComparison.CurrentCulture) AndAlso adesso - _ultimoCommutatoreTick < finestraDoppio Then Return
            Dim nomeVoce As String = _descrittore.NomeCorrente(elemento)
            If nomeVoce <> "" AndAlso String.Equals(nomeVoce, _ultimoCommutatoreNome, StringComparison.CurrentCultureIgnoreCase) AndAlso adesso - _ultimoCommutatoreTick < finestraDoppio Then Return
            _ultimoCommutatoreNome = nomeVoce
            RegistraTitoliFinestra(nomeVoce, testo)
            _ultimoCommutatore = testo
            _ultimoCommutatoreTick = adesso
            _ultimoCommutatoreDaTasto = daTasto
            _ultimoFocusTick = adesso
            Volatile.Write(_commutatoreAttivitaTick, adesso)
            Volatile.Write(_commutatoreAnnunciato, 1)
            ParlaCommutatore(testo)
            _revisione = elemento
            _uiaFerma = Nothing
            MemorizzaElemento(elemento, _descrittore.Nome(elemento), _descrittore.ParteVariabile(elemento))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Function ElementoConFocus(Optional seMancaUsaUltimo As Boolean = True) As IUIAutomationElement
        If _automazione Is Nothing Then Return Nothing
        Dim noto As IUIAutomationElement = FuocoNotoValido()
        If noto IsNot Nothing Then Return noto
        Dim cliente As IUIAutomation = AutomazioneRapida()
        Dim cache As IUIAutomationCacheRequest = CacheRapida()
        Try
            For tentativo As Integer = 1 To 2
                Try
                    Dim elemento As IUIAutomationElement = cliente.GetFocusedElementBuildCache(cache)
                    AzzeraErroriAutomazione()
                    Return elemento
                Catch ex As Exception
                    If tentativo < 2 AndAlso ElementoAssente(ex) Then Continue For
                    If ElementoAssente(ex) OrElse TimeoutOperazione(ex) Then Return FuocoSeManca(seMancaUsaUltimo)
                    SegnaErroreAutomazione(ex)
                    log.ScriviLog("WGLettoreSchermo.ElementoConFocus: " & ex.Message)
                    Return FuocoSeManca(seMancaUsaUltimo)
                End Try
            Next
            Return FuocoSeManca(seMancaUsaUltimo)
        Catch exEsterno As Exception
            log.ScriviLog("WGLettoreSchermo.ElementoConFocus (esterno): " & exEsterno.Message)
            Return FuocoSeManca(seMancaUsaUltimo)
        End Try
    End Function

    Private Function FuocoSeManca(seMancaUsaUltimo As Boolean) As IUIAutomationElement
        If seMancaUsaUltimo Then Return _ultimoElemento
        Return Nothing
    End Function

    Private Shared Function TimeoutOperazione(ex As Exception) As Boolean
        If ex Is Nothing Then Return False
        If TypeOf ex Is TimeoutException Then Return True
        Const CodiceTimeout As Integer = &H80131505
        If ex.HResult = CodiceTimeout Then Return True
        Dim com As COMException = TryCast(ex, COMException)
        If com IsNot Nothing AndAlso com.ErrorCode = CodiceTimeout Then Return True
        Dim messaggio As String = If(ex.Message, "")
        If messaggio.IndexOf("80131505", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return False
    End Function

    Private Shared Function ElementoAssente(ex As Exception) As Boolean
        Const CodiceElementoAssente As Integer = &H80040201
        Const CodiceOggettoScollegato As Integer = &H800401FD
        If ex Is Nothing Then Return False
        Dim com As COMException = TryCast(ex, COMException)
        If com IsNot Nothing AndAlso (com.ErrorCode = CodiceElementoAssente OrElse com.ErrorCode = CodiceOggettoScollegato) Then Return True
        Dim messaggio As String = If(ex.Message, "")
        If messaggio.IndexOf("80040201", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("sottoscrittor", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("800401fd", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("connesso a un server", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return False
    End Function

    Private Function ChiaveElemento(elemento As IUIAutomationElement) As String
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
            RegistroErrori.Scrivi("WGLettoreSchermo.ChiaveElemento", exAtteso)
            Return ""
        End Try
    End Function

    Private Function AggiungiConteggioElenco(lista As IUIAutomationElement, frase As String) As String
        Try
            If frase Is Nothing OrElse Not frase.EndsWith("elenco", StringComparison.CurrentCultureIgnoreCase) Then Return frase
            Dim daApp As String = OttieniModuloApp(lista).FraseElencoApp(lista, frase)
            If daApp <> "" Then Return daApp
            If _walkerControllo Is Nothing OrElse Not ElementoWeb(lista) Then Return frase
            Dim primo As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(lista, _richiestaCache)
            If primo Is Nothing Then Return frase
            Dim numero As Integer = _descrittore.LeggiIntero(primo, DescrittoreElementi.ProprietaTotale, 0)
            If numero <= 0 Then
                Dim inizioConteggio As Integer = Environment.TickCount
                Dim corrente As IUIAutomationElement = primo
                While corrente IsNot Nothing AndAlso numero < 200 AndAlso Environment.TickCount - inizioConteggio < 100
                    numero += 1
                    corrente = _walkerControllo.GetNextSiblingElementBuildCache(corrente, _richiestaCache)
                End While
                If corrente IsNot Nothing Then numero = 0
            End If
            If numero <= 0 Then Return frase
            Return frase & " con " & numero.ToString() & If(numero = 1, " elemento", " elementi")
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AggiungiConteggioElenco", exAtteso)
            Return frase
        End Try
    End Function

    Private Function AggiungiPosizioneVoceElencoWeb(elemento As IUIAutomationElement, testo As String) As String
        Try
            If _descrittore Is Nothing OrElse elemento Is Nothing Then Return testo
            If Not _descrittore.VoceElencoWeb(elemento) OrElse Not ElementoWeb(elemento) Then Return testo
            Dim posizione As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            Dim totale As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
            If posizione <= 0 OrElse totale <= 0 Then Return testo
            Dim frase As String = posizione.ToString() & " di " & totale.ToString()
            If testo.IndexOf(frase, StringComparison.Ordinal) >= 0 Then Return testo
            Return If(testo = "", frase, testo & ", " & frase)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AggiungiPosizioneVoceElencoWeb", exAtteso)
            Return testo
        End Try
    End Function

    Private Function ContestoNuovo(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse _walkerControllo Is Nothing Then Return ""
            Dim chiavi As New List(Of String)()
            Dim frasi As New List(Of String)()
            Dim corrente As IUIAutomationElement = _walkerControllo.GetParentElementBuildCache(elemento, _richiestaCache)
            Dim livelli As Integer = 0
            Dim dentroAlbero As Boolean = _descrittore.EVoceAlbero(elemento)
            While corrente IsNot Nothing AndAlso livelli < 12
                Dim tipo As Integer = _descrittore.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloDocumento Then Exit While
                If dentroAlbero Then
                    If _descrittore.EAlbero(corrente) Then
                        dentroAlbero = False
                    ElseIf tipo = DescrittoreElementi.ControlloGruppo OrElse _descrittore.EVoceAlbero(corrente) Then
                        corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
                        livelli += 1
                        Continue While
                    End If
                End If
                If tipo = DescrittoreElementi.ControlloFinestra AndAlso Not FinestraAnnidata(corrente) Then Exit While
                If OttieniModuloApp(corrente).AntenatoDaTacere(corrente) Then
                    corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
                    livelli += 1
                    Continue While
                End If
                Dim frase As String
                If _descrittore.EDialogo(corrente) Then
                    frase = DescriviDialogo(corrente)
                Else
                    frase = _descrittore.DescriviAntenato(corrente)
                End If
                If frase = "" AndAlso tipo = DescrittoreElementi.ControlloGruppo Then
                    Dim nomeGruppo As String = TitoloGruppo(corrente)
                    If nomeGruppo <> "" Then frase = nomeGruppo & ", gruppo"
                End If
                If frase <> "" Then
                    Dim chiave As String = ChiaveElemento(corrente)
                    If chiave = "" Then chiave = frase
                    If _catena Is Nothing OrElse Not _catena.Contains(chiave) Then frase = AggiungiConteggioElenco(corrente, frase)
                    chiavi.Insert(0, chiave)
                    frasi.Insert(0, frase)
                End If
                corrente = _walkerControllo.GetParentElementBuildCache(corrente, _richiestaCache)
                livelli += 1
            End While
            Dim nuove As New List(Of String)()
            For indice As Integer = 0 To chiavi.Count - 1
                If _catena Is Nothing OrElse Not _catena.Contains(chiavi(indice)) Then nuove.Add(frasi(indice))
            Next
            _catena = New HashSet(Of String)(chiavi)
            Dim viste As New HashSet(Of String)(StringComparer.CurrentCultureIgnoreCase)
            Dim distinte As New List(Of String)()
            For Each frase As String In nuove
                If viste.Add(frase) Then distinte.Add(frase)
            Next
            Return String.Join(", ", distinte)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ContestoNuovo", ex)
            Return ""
        End Try
    End Function
    Private Function EFinestraDialogo(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As String = ClasseFinestra(finestra)
            If classe = "#32770" Then Return True
            If FinestraDiSistema(finestra) OrElse ClasseCommutatore(classe) Then Return False
            Return NativoLettore.GetWindow(finestra, NativoLettore.GW_OWNER) <> IntPtr.Zero
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EFinestraDialogo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FinestraAnnidata(finestra As IUIAutomationElement) As Boolean
        Try
            If _walkerControllo Is Nothing OrElse _automazione Is Nothing Then Return False
            Dim padre As IUIAutomationElement = _walkerControllo.GetParentElement(finestra)
            If padre Is Nothing Then Return False
            Return Not StessoElemento(padre, _automazione.GetRootElement())
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraAnnidata: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function DescriviDialogo(dialogo As IUIAutomationElement) As String
        Try
            Dim parti As New List(Of String)()
            Dim nome As String = _descrittore.NomeCorrente(dialogo)
            If nome <> "" Then parti.Add(nome)
            parti.Add("finestra di dialogo")
            If _automazione IsNot Nothing Then
                Dim nomiPulsanti As New HashSet(Of String)(StringComparer.CurrentCultureIgnoreCase)
                Dim pulsanti As IUIAutomationElementArray = dialogo.FindAllBuildCache(TreeScope.TreeScope_Descendants, _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloPulsante), _richiestaCache)
                If pulsanti IsNot Nothing Then
                    For indice As Integer = 0 To Math.Min(pulsanti.Length, 20) - 1
                        Dim nomePulsante As String = _descrittore.Nome(pulsanti.GetElement(indice))
                        If nomePulsante <> "" Then nomiPulsanti.Add(nomePulsante)
                    Next
                End If
                Dim testi As IUIAutomationElementArray = dialogo.FindAllBuildCache(TreeScope.TreeScope_Descendants, _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloTesto), _richiestaCache)
                If testi IsNot Nothing Then
                    Dim contati As Integer = 0
                    For indice As Integer = 0 To Math.Min(testi.Length, 20) - 1
                        Dim testo As String = _descrittore.Nome(testi.GetElement(indice))
                        If testo = "" OrElse nomiPulsanti.Contains(testo) Then Continue For
                        If String.Equals(testo, nome, StringComparison.CurrentCultureIgnoreCase) OrElse parti.Contains(testo) Then Continue For
                        If testo.Length > 300 Then testo = testo.Substring(0, 300)
                        parti.Add(testo)
                        contati += 1
                        If contati >= 6 Then Exit For
                    Next
                End If
            End If
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.DescriviDialogo: " & ex.Message)
            Return "finestra di dialogo"
        End Try
    End Function

    Private Function TitoloGruppo(gruppo As IUIAutomationElement) As String
        Try
            Try
                Dim legacy As IUIAutomationLegacyIAccessiblePattern = TryCast(gruppo.GetCurrentPattern(10018), IUIAutomationLegacyIAccessiblePattern)
                If legacy IsNot Nothing Then
                    Dim nomeClassico As String = legacy.CurrentName
                    If Not String.IsNullOrWhiteSpace(nomeClassico) Then Return nomeClassico.Trim()
                End If
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.TitoloGruppo", exAtteso)
            End Try
            If _walkerControllo Is Nothing Then Return ""
            Dim figlio As IUIAutomationElement = _walkerControllo.GetFirstChildElementBuildCache(gruppo, _richiestaCache)
            If figlio Is Nothing Then Return ""
            Dim tipoFiglio As Integer = _descrittore.TipoControllo(figlio)
            If tipoFiglio = DescrittoreElementi.ControlloTesto OrElse tipoFiglio = DescrittoreElementi.ControlloIntestazione OrElse tipoFiglio = DescrittoreElementi.ControlloElementoIntestazione Then Return _descrittore.Nome(figlio)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloGruppo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ElementoInFinestra(elemento As IUIAutomationElement, finestra As IntPtr) As Boolean
        Try
            If elemento Is Nothing Then Return True
            Dim radiceFinestra As IntPtr = NativoLettore.GetAncestor(finestra, NativoLettore.GA_ROOT)
            If radiceFinestra = IntPtr.Zero Then radiceFinestra = finestra
            Dim maniglia As IntPtr = ManigliaFinestra(elemento)
            If maniglia = IntPtr.Zero Then Return True
            Dim radice As IntPtr = NativoLettore.GetAncestor(maniglia, NativoLettore.GA_ROOT)
            If radice = IntPtr.Zero Then radice = maniglia
            Return radice = radiceFinestra
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElementoInFinestra: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function ManigliaFinestra(elemento As IUIAutomationElement) As IntPtr
        Try
            If elemento Is Nothing Then Return IntPtr.Zero
            Dim maniglia As IntPtr = LeggiManiglia(elemento)
            If maniglia = IntPtr.Zero AndAlso _walkerFinestre IsNot Nothing AndAlso _richiestaCacheManiglia IsNot Nothing Then
                Dim normalizzato As IUIAutomationElement = _walkerFinestre.NormalizeElementBuildCache(elemento, _richiestaCacheManiglia)
                If normalizzato IsNot Nothing Then maniglia = LeggiManiglia(normalizzato)
            End If
            Return maniglia
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ManigliaFinestra", ex)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function LeggiManiglia(elemento As IUIAutomationElement) As IntPtr
        Try
            Dim valore As Object = elemento.GetCachedPropertyValue(DescrittoreElementi.ProprietaManiglia)
            If valore IsNot Nothing Then
                If TypeOf valore Is IntPtr Then Return CType(valore, IntPtr)
                If TypeOf valore Is Integer Then Return New IntPtr(CInt(valore))
                If TypeOf valore Is Long Then Return New IntPtr(CLng(valore))
            End If
            Return elemento.CurrentNativeWindowHandle
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.LeggiManiglia", exAtteso)
            Return IntPtr.Zero
        End Try
    End Function

    ' Un evento di fuoco di un programma che non è in primo piano (Claude che aggiorna la pagina mentre si è altrove) non si legge.
    Private Function ElementoDelPrimoPiano(elemento As IUIAutomationElement) As Boolean
        Try
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano = IntPtr.Zero OrElse elemento Is Nothing Then Return True
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
            If pid = 0UI Then Return True
            Dim pidElemento As Integer = _descrittore.IdProcesso(elemento)
            If pidElemento <= 0 OrElse pidElemento = CInt(pid) Then Return True
            ' Le app di Windows stanno in una finestra del processo che le ospita (ApplicationFrameHost e simili): lì i processi non coincidono.
            If ClasseFinestra(primoPiano) = "ApplicationFrameWindow" OrElse SuperficieDiSistema(primoPiano, pid) Then Return True
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ElementoDelPrimoPiano", ex)
            Return True
        End Try
    End Function

    Private Function FinestraBloccata(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim maniglia As IntPtr = elemento.CachedNativeWindowHandle
            Return maniglia <> IntPtr.Zero AndAlso NativoLettore.IsHungAppWindow(maniglia)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FinestraBloccata", exAtteso)
            Return False
        End Try
    End Function

    Private Function TitoloGiaAnnunciato(elemento As IUIAutomationElement, nome As String) As Boolean
        Try
            If nome = "" OrElse _descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloDocumento Then Return False
            Return String.Equals(nome, _ultimoTitoloPagina, StringComparison.CurrentCulture) AndAlso Environment.TickCount - _ultimoTitoloTick < 2500
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloGiaAnnunciato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EBrowser(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI Then Return False
            Return OttieniModuloApp(pid).EApplicazioneBrowser
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EBrowser", exAtteso)
            Return False
        End Try
    End Function

    Private ReadOnly _browserSvegliati As New HashSet(Of IntPtr)()
    Private ReadOnly _bloccoSvegliati As New Object()
    Private _prossimaPuliziaCom As Integer

    Private Sub PuliziaOggettiCom()
        Try
            Dim adesso As Integer = Environment.TickCount
            If adesso - _prossimaPuliziaCom < 0 Then Return
            _prossimaPuliziaCom = adesso + 5000
            RaccogliOggettiCom(NativoLettore.GetForegroundWindow())
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PuliziaOggettiCom: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaTitolo()
        Try
            If Volatile.Read(_titoloInAttesa) = 0 Then Return
            Dim finestra As IntPtr
            SyncLock _bloccoTitolo
                If Environment.TickCount - _titoloScadenza < 0 Then Return
                finestra = _titoloFinestra
                Interlocked.Exchange(_titoloInAttesa, 0)
            End SyncLock
            If Sospeso OrElse SilenzioEsterno() OrElse PrimoPianoInWinGabriel() Then Return
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If primo = IntPtr.Zero Then Return
            Dim radice As IntPtr = NativoLettore.GetAncestor(finestra, NativoLettore.GA_ROOT)
            If radice = IntPtr.Zero Then radice = finestra
            If radice <> primo Then Return
            If Not EBrowser(primo) Then Return
            If _navigazione Is Nothing OrElse Not _navigazione.EFinestraWeb(primo) Then Return
            Dim titolo As String = OttieniModuloApp(primo).TitoloPagina(TitoloFinestra(primo))
            If titolo = "" OrElse String.Equals(titolo, _ultimoTitoloPagina, StringComparison.CurrentCulture) Then Return
            _ultimoTitoloPagina = titolo
            _ultimoTitoloTick = Environment.TickCount
            Parla(titolo, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ControllaTitolo: " & ex.Message)
        End Try
    End Sub

    Private Function StessoElemento(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            Return _automazione.CompareElements(primo, secondo) <> 0
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.StessoElemento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub MemorizzaElemento(elemento As IUIAutomationElement, nome As String, variabile As String)
        Try
            _ultimoElemento = elemento
            _ultimoInAlbero = 0
            _ultimoNome = If(nome, "")
            _ultimaParteVariabile = If(variabile, "")
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MemorizzaElemento: " & ex.Message)
        End Try
    End Sub

    Private Function TitoloFinestra(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim testo As New StringBuilder(1024)
            Dim lunghezza As Integer = NativoLettore.InternalGetWindowText(finestra, testo, testo.Capacity)
            Dim titolo As String = If(lunghezza > 0, testo.ToString().Trim(), "")
            If titolo <> "" Then
                If _titoliRicordati.Count > 2000 Then _titoliRicordati.Clear()
                _titoliRicordati(finestra) = titolo
                Return titolo
            End If
            Dim ricordato As String = Nothing
            If _titoliRicordati.TryGetValue(finestra, ricordato) AndAlso Not String.IsNullOrEmpty(ricordato) Then
                If NativoLettore.IsWindow(finestra) Then Return ricordato
                _titoliRicordati.TryRemove(finestra, ricordato)
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TitoloFinestra: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ClasseFinestra(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim testo As New StringBuilder(256)
            NativoLettore.GetClassName(finestra, testo, testo.Capacity)
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ClasseFinestra: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function InterruzioneConsentita() As Boolean
        Try
            If MillisecondiDa(Volatile.Read(_ultimoTastoFreccetta)) < 700L Then Return True
            If MillisecondiDa(Volatile.Read(_ultimoTastoScrittura)) < 700L AndAlso MillisecondiDa(Volatile.Read(_ultimoAnnuncioCampo)) < 700L Then Return True
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.InterruzioneConsentita", exAtteso)
            Return False
        End Try
    End Function

    Friend Function AnnuncioDaSopprimere(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim pulito As String = NormalizzaTesto(testo).ToLowerInvariant()
            If pulito = "" OrElse pulito.Length > 120 Then Return False
            If pulito.Contains("programma chius") Then Return True
            If pulito.Contains("visualizzazione shell") Then Return True
            If pulito.Contains("shell per esplora file") Then Return True
            If pulito.Contains("shell view") Then Return True
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AnnuncioDaSopprimere", exAtteso)
            Return False
        End Try
    End Function

    Private Function NomeEseguibileElemento(elemento As IUIAutomationElement, nome As String) As Boolean
        Try
            If elemento Is Nothing OrElse String.IsNullOrWhiteSpace(nome) Then Return False
            If _descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloFinestra Then Return False
            Return TitoloEseguibile(CUInt(_descrittore.IdProcesso(elemento)), nome)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.NomeEseguibileElemento", exAtteso)
            Return False
        End Try
    End Function

    Private Sub ParlaCommutatore(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If AnnuncioDaSopprimere(testo) Then Return
            If SilenzioEsterno() Then Return
            If _codaVoce IsNot Nothing Then
                _codaVoce.Accoda(testo, PrioritaAnnuncio.Interruzione, True, True)
                If _braille IsNot Nothing AndAlso BrailleAttivo Then _braille.Mostra(testo, 0)
                Return
            End If
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura Is Nothing Then Return
            lettura.LeggiTesto(testo, priorita:="Alta")
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ParlaCommutatore: " & ex.Message)
        End Try
    End Sub

    Private Sub Parla(testo As String, interrompi As Boolean)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If AnnuncioDaSopprimere(testo) Then Return
            If SilenzioEsterno() Then Return
            If TieniPerAvvio(testo) Then Return
            If interrompi AndAlso Not InterruzioneConsentita() Then interrompi = False
            If _codaVoce IsNot Nothing Then
                _codaVoce.Accoda(testo, If(interrompi, PrioritaAnnuncio.Interruzione, PrioritaAnnuncio.Normale), interrompi, Volatile.Read(_origineUtente) = 1)
                If _braille IsNot Nothing AndAlso BrailleAttivo Then _braille.Mostra(testo, 0)
                Return
            End If
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura Is Nothing Then Return
            lettura.LeggiTesto(testo, priorita:=If(interrompi, "Alta", "Normale"))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.Parla: " & ex.Message)
        End Try
    End Sub

End Class