Imports System.ComponentModel
Imports System.Timers
Imports WGDati
Imports System.Windows.Forms
Imports wgcontrols
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Reflection
Imports WGYoutube
Imports WGMail

' Gestione centralizzata degli avvisi automatici in sottofondo. Coordina notifiche, promemoria di agenda, sincronizzazioni
' periodiche e backup dei dati.
Public Class PreavvisoManager
    Public Shared Property IstanzaAttiva As PreavvisoManager
    ''' <summary>
    ''' Vero finché all'avvio il menu iniziale fa le sue domande (benvenuto, notizie): gli altri avvisi
    ''' (quotidiani, posta, video...) aspettano, così prima arrivano le domande e poi tutto il resto.
    ''' </summary>
    Public Shared Property AvvioInCorso As Boolean

    Private agenda As CLSAgenda
    Private timer As System.Timers.Timer
    Private timerNotizie As System.Timers.Timer
    Private timerQuotidiani As System.Timers.Timer
    Private timerVideoYoutube As System.Timers.Timer
    Private timerSincronizzazioneRubrica As System.Timers.Timer
    Private timerSincronizzazioneAgenda As System.Timers.Timer
    Private timerBackup As System.Timers.Timer
    Private timerAvvioSorveglianzaPosta As System.Timers.Timer
    Private _postaSorvegliata As CLSMail = Nothing
    Private _avvisoPostaInCorso As Boolean = False
    Private _postaSilenziataFino As DateTime = DateTime.MinValue
    Private _backupInCorso As Integer = 0
    Private idsNotificati As New HashSet(Of String)
    Private _logger As Logger
    Private _config As New WGConfig()
    Private _notificato20 As Boolean = False
    Private _notificato15 As Boolean = False
    Private _notificato10 As Boolean = False
    Private _primoControlloVideoYoutube As Boolean = True
    Private _menuVideoYoutube As MenuYoutube
    Private mnuAvvisoNotifica As WGMenu
    Private mnuPosponiNotifica As WGMenu
    Private _testoAvvisoNotifica As String = ""
    Private _testoPosponiNotifica As String = ""
    Private _azioneConsultaNotifica As Action
    Private _azionePosponiNotifica As Action(Of Double)
    Private _azioneAbbandonoPosponiNotifica As Action
    Private _ripresentaAvvisoNotifica As Boolean = False
    Private ReadOnly _ripresentazioniAttive As New List(Of RipresentazioneAvviso)

    ' Serializza tutte le richieste di avviso (posta, youtube, notizie, quotidiani, appuntamenti
    ' esterni, ripresentazioni posticipate, promemoria agenda) in modo che non si sovrascrivano
    ' a vicenda: se un avviso e' gia' visualizzato, i successivi vengono accodati e mostrati uno
    ' alla volta, invece di rubare silenziosamente i campi condivisi dell'avviso in corso.
    Private _notificaCorrenteInCorso As Boolean = False
    Private ReadOnly _lockNotifiche As New Object()
    Private ReadOnly _codaNotifiche As New Queue(Of Action)

    Private lstAvv As List(Of CLSAgenda.AppuntamentoLista)
    Private WithEvents mnuAvv As WGMenu


    Private ReadOnly _classiBloccanti As String() = {"WGCampo", "WGCampoTesto", "Campi", "WGProcessoInCorso"}

    Public Sub New()
        _logger = New Logger()
        Try
            Me.agenda = CLSAgenda.GetInstance()
            IstanzaAttiva = Me
            RegistraGestoriNotifiche()
            timer = New System.Timers.Timer(20000)
            timer.AutoReset = True
            AddHandler timer.Elapsed, AddressOf Timer_Elapsed
            timer.Start()

            ' Le notizie all'avvio le chiede il menu iniziale, prima di aprire la scrivania (NotizieDaProporreAllAvvio).

            timerNotizie = New System.Timers.Timer(ServiziAvvio.IntervalloControlloNotizieMinuti() * 60000)
            timerNotizie.AutoReset = False
            AddHandler timerNotizie.Elapsed, AddressOf TimerNotizie_Elapsed
            timerNotizie.Start()

            timerQuotidiani = New System.Timers.Timer(15000)
            timerQuotidiani.AutoReset = False
            AddHandler timerQuotidiani.Elapsed, AddressOf TimerQuotidiani_Elapsed
            timerQuotidiani.Start()

            timerVideoYoutube = New System.Timers.Timer(60000)
            timerVideoYoutube.AutoReset = False
            AddHandler timerVideoYoutube.Elapsed, AddressOf TimerVideoYoutube_Elapsed
            timerVideoYoutube.Start()

            timerSincronizzazioneRubrica = New System.Timers.Timer(90000)
            timerSincronizzazioneRubrica.AutoReset = False
            AddHandler timerSincronizzazioneRubrica.Elapsed, AddressOf TimerSincronizzazioneRubrica_Elapsed
            timerSincronizzazioneRubrica.Start()

            timerBackup = New System.Timers.Timer(INTERVALLO_PRIMO_CONTROLLO_BACKUP)
            timerBackup.AutoReset = False
            AddHandler timerBackup.Elapsed, AddressOf TimerBackup_Elapsed
            timerBackup.Start()

            timerSincronizzazioneAgenda = New System.Timers.Timer(120000)
            timerSincronizzazioneAgenda.AutoReset = False
            AddHandler timerSincronizzazioneAgenda.Elapsed, AddressOf TimerSincronizzazioneAgenda_Elapsed
            timerSincronizzazioneAgenda.Start()

            timerAvvioSorveglianzaPosta = New System.Timers.Timer(45000)
            timerAvvioSorveglianzaPosta.AutoReset = False
            AddHandler timerAvvioSorveglianzaPosta.Elapsed, AddressOf TimerAvvioSorveglianzaPosta_Elapsed
            timerAvvioSorveglianzaPosta.Start()
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try
    End Sub

    Private Sub ControllaNotizieAvvioImmediato()
        Try
            ControllaNuoveNotizieWinGabriel()
        Catch ex As Exception
            _logger.ScriviLog("ControllaNotizieAvvioImmediato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TimerNotizie_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            ControllaNuoveNotizieWinGabriel()
        Catch ex As Exception
            _logger.ScriviLog("TimerNotizie_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerNotizie IsNot Nothing Then
                    timerNotizie.Interval = ServiziAvvio.IntervalloControlloNotizieMinuti() * 60000
                    timerNotizie.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerNotizie_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    ''' <summary>Vero se oggi c'è da controllare se sono arrivate notizie (avviso attivo e non ancora fatto oggi).</summary>
    Public Function NotizieDaControllareOggi() As Boolean
        Try
            If Not ServiziAvvio.AvvisoUltimeNotizieAttivo() Then Return False
            Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
            Return Not String.Equals(GestoreArchivioNotizieWinGabriel.OttieniDataUltimoAvviso(), oggi, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _logger.ScriviLog("NotizieDaControllareOggi eccezione: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Scarica le notizie nuove, le mette fra quelle da leggere e dice quante sono da leggere (0 se
    ''' l'avviso è spento o oggi è già stato fatto). Non chiede niente: all'avvio la domanda la fa il
    ''' menu iniziale, prima di tutto il resto.
    ''' </summary>
    Public Function NotizieDaProporreAllAvvio() As Integer
        Try
            If Not NotizieDaControllareOggi() Then Return 0
            Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
            Dim riuscito As Boolean = False
            Dim elenco As List(Of ArticoloNotiziaWinGabriel) = ServiziAvvio.RecuperaArticoliWinGabriel(riuscito)
            ' Se il sito non ha risposto il controllo non è fatto: si riprova al prossimo giro, non domani.
            If Not riuscito Then Return GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere().Count
            If elenco IsNot Nothing AndAlso elenco.Count > 0 Then
                Dim nuoveNotizie As List(Of ArticoloNotiziaWinGabriel) = GestoreArchivioNotizieWinGabriel.SelezionaNuove(elenco)
                If nuoveNotizie.Count > 0 Then GestoreArchivioNotizieWinGabriel.SalvaNotizieDaLeggere(nuoveNotizie)
            End If
            GestoreArchivioNotizieWinGabriel.SalvaDataUltimoAvviso(oggi)
            Return GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere().Count
        Catch ex As Exception
            _logger.ScriviLog("NotizieDaProporreAllAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Sub ControllaNuoveNotizieWinGabriel()
        Try
            If Not ServiziAvvio.AvvisoUltimeNotizieAttivo() Then Return

            Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
            Dim ultimoAvviso As String = GestoreArchivioNotizieWinGabriel.OttieniDataUltimoAvviso()

            If String.Equals(ultimoAvviso, oggi, StringComparison.OrdinalIgnoreCase) Then Return

            Dim riuscito As Boolean = False
            Dim elenco As List(Of ArticoloNotiziaWinGabriel) = ServiziAvvio.RecuperaArticoliWinGabriel(riuscito)
            ' Se il sito non ha risposto il controllo non è fatto: si riprova al prossimo giro, non domani.
            If Not riuscito Then Return

            If elenco IsNot Nothing AndAlso elenco.Count > 0 Then
                Dim nuoveNotizie As List(Of ArticoloNotiziaWinGabriel) = GestoreArchivioNotizieWinGabriel.SelezionaNuove(elenco)

                If nuoveNotizie.Count > 0 Then
                    GestoreArchivioNotizieWinGabriel.SalvaNotizieDaLeggere(nuoveNotizie)
                End If
            End If

            GestoreArchivioNotizieWinGabriel.SalvaDataUltimoAvviso(oggi)

            Dim totaleNonLette As Integer = GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere().Count
            If totaleNonLette = 0 Then Return

            Dim testoDomanda As String
            If totaleNonLette = 1 Then
                testoDomanda = "C'è una nuova notizia da WinGabriel non ancora letta. Vuoi consultarla adesso?"
            Else
                testoDomanda = "Ci sono " & totaleNonLette.ToString() & " nuove notizie da WinGabriel non ancora lette. Vuoi consultarle adesso?"
            End If

            ChiediConsultazioneNotizie(testoDomanda)
        Catch ex As Exception
            _logger.ScriviLog("ControllaNuoveNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediConsultazioneNotizie(testoDomanda As String)
        Try
            MostraAvvisoConScelte(testoDomanda, "Scegli fra quanto tempo ripresentare l'avviso delle nuove notizie", AddressOf ApriNotizieWinGabriel, AddressOf PosponiNotizieWinGabriel)
        Catch ex As Exception
            _logger.ScriviLog("ChiediConsultazioneNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriNotizieWinGabriel()
        Try
            Dim elenco As New MenuNotizieDaLeggereWinGabriel()
        Catch ex As Exception
            _logger.ScriviLog("ApriNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PosponiNotizieWinGabriel(ms As Double)
        Try
            If timerNotizie IsNot Nothing Then
                timerNotizie.Stop()
                timerNotizie.Interval = ms + 60000
                timerNotizie.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("PosponiNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TimerVideoYoutube_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            ControllaNuoviVideoYoutube()
        Catch ex As Exception
            _logger.ScriviLog("TimerVideoYoutube_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerVideoYoutube IsNot Nothing Then
                    timerVideoYoutube.Interval = IntervalloControlloVideoYoutubeMillisecondi()
                    timerVideoYoutube.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerVideoYoutube_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    Private Function IntervalloControlloVideoYoutubeMillisecondi() As Double
        Try
            Dim minuti As Integer = MenuYoutube.LeggiIntervalloControlloMinuti()
            If minuti < 5 Then minuti = 5
            Return CDbl(minuti) * 60000.0
        Catch ex As Exception
            _logger.ScriviLog("IntervalloControlloVideoYoutubeMillisecondi eccezione: " & ex.Message)
            Return 3600000.0
        End Try
    End Function

    Private Sub ControllaNuoviVideoYoutube()
        Try
            If Not MenuYoutube.ControlloNuoviVideoAttivo() Then
                _primoControlloVideoYoutube = False
                Return
            End If

            Dim trovati As Integer = MenuYoutube.EseguiControlloAutomatico()
            Dim nonVisti As Long = MenuYoutube.ContaNuoviVideoNonVisti()
            Dim eraPrimoControllo As Boolean = _primoControlloVideoYoutube
            _primoControlloVideoYoutube = False

            If nonVisti <= 0 Then Return
            If trovati <= 0 AndAlso Not eraPrimoControllo Then Return

            Dim testoDomanda As String
            If nonVisti = 1 Then
                testoDomanda = "C'e' un nuovo video nei canali a cui sei iscritto. Vuoi vederlo adesso?"
            Else
                testoDomanda = "Ci sono " & nonVisti.ToString() & " nuovi video nei canali a cui sei iscritto. Vuoi vedere l'elenco adesso?"
            End If

            InviaAvvisoNuoviVideoYoutube(testoDomanda)
        Catch ex As Exception
            _logger.ScriviLog("ControllaNuoviVideoYoutube eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InviaAvvisoNuoviVideoYoutube(testoDomanda As String)
        Try
            MostraAvvisoConScelte(testoDomanda, "Scegli fra quanto tempo ripresentare l'avviso dei nuovi video", AddressOf ApriVideoYoutube, AddressOf PosponiVideoYoutube)
        Catch ex As Exception
            _logger.ScriviLog("InviaAvvisoNuoviVideoYoutube eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriVideoYoutube()
        Try
            _menuVideoYoutube = New MenuYoutube()
            _menuVideoYoutube.AvviaVideoDaVedere()
        Catch ex As Exception
            _logger.ScriviLog("ApriVideoYoutube eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PosponiVideoYoutube(ms As Double)
        Try
            If timerVideoYoutube IsNot Nothing Then
                timerVideoYoutube.Stop()
                timerVideoYoutube.Interval = ms + 60000
                timerVideoYoutube.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("PosponiVideoYoutube eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TimerSincronizzazioneRubrica_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            EseguiSincronizzazioneRubricaAutomatica()
        Catch ex As Exception
            _logger.ScriviLog("TimerSincronizzazioneRubrica_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerSincronizzazioneRubrica IsNot Nothing Then
                    timerSincronizzazioneRubrica.Interval = IntervalloSincronizzazioneRubricaMillisecondi()
                    timerSincronizzazioneRubrica.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerSincronizzazioneRubrica_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    Private Const INTERVALLO_PRIMO_CONTROLLO_BACKUP As Double = 180000.0
    Private Const INTERVALLO_CONTROLLO_BACKUP As Double = 1800000.0

    Public ReadOnly Property BackupInCorso As Boolean
        Get
            Try
                Return System.Threading.Interlocked.CompareExchange(_backupInCorso, 0, 0) <> 0
            Catch ex As Exception
                _logger.ScriviLog("BackupInCorso eccezione: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Sub TimerBackup_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            EseguiBackupAutomatico()
        Catch ex As Exception
            _logger.ScriviLog("TimerBackup_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerBackup IsNot Nothing Then
                    timerBackup.Interval = INTERVALLO_CONTROLLO_BACKUP
                    timerBackup.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerBackup_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    ' Il backup gira sul thread del timer, quindi il resto del programma continua a rispondere
    ' mentre l'archivio viene creato: nessuna finestra, nessun blocco della tastiera.
    Private Sub EseguiBackupAutomatico()
        Try
            If System.Threading.Interlocked.CompareExchange(_backupInCorso, 1, 0) <> 0 Then Return

            Try
                If Not WGDb.WGBackup.BackupDovuto() Then Return

                ' Priorita' bassa: comprimere e copiare i database non deve mai rubare
                ' tempo al thread che serve la tastiera e la sintesi vocale.
                Try
                    System.Threading.Thread.CurrentThread.Priority = Threading.ThreadPriority.BelowNormal
                Catch exPriorita As Exception
                    _logger.ScriviLog("EseguiBackupAutomatico priorita eccezione: " & exPriorita.Message)
                End Try

                Dim messaggio As String = ""
                WGDb.WGBackup.EseguiBackupAutomaticoSeDovuto(messaggio)
            Finally
                System.Threading.Interlocked.Exchange(_backupInCorso, 0)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("EseguiBackupAutomatico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                System.Threading.Interlocked.Exchange(_backupInCorso, 0)
            Catch exRilascio As Exception
                _logger.ScriviLog("EseguiBackupAutomatico rilascio eccezione: " & exRilascio.Message)
            End Try
        End Try
    End Sub

    Public Sub RiallineaTimerBackup()
        Try
            If timerBackup Is Nothing Then Return
            timerBackup.Stop()
            timerBackup.Interval = INTERVALLO_PRIMO_CONTROLLO_BACKUP
            timerBackup.Start()
        Catch ex As Exception
            _logger.ScriviLog("RiallineaTimerBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub TimerSincronizzazioneAgenda_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            EseguiSincronizzazioneAgendaAutomatica()
        Catch ex As Exception
            _logger.ScriviLog("TimerSincronizzazioneAgenda_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerSincronizzazioneAgenda IsNot Nothing Then
                    timerSincronizzazioneAgenda.Interval = IntervalloSincronizzazioneAgendaMillisecondi()
                    timerSincronizzazioneAgenda.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerSincronizzazioneAgenda_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    Private Function IntervalloSincronizzazioneRubricaMillisecondi() As Double
        Try
            Dim minuti As Integer = SincronizzazioneWG.IntervalloRubricaMinuti()
            If minuti < 5 Then minuti = 5
            Return CDbl(minuti) * 60000.0
        Catch ex As Exception
            _logger.ScriviLog("IntervalloSincronizzazioneRubricaMillisecondi eccezione: " & ex.Message)
            Return 1800000.0
        End Try
    End Function

    Private Function IntervalloSincronizzazioneAgendaMillisecondi() As Double
        Try
            Dim minuti As Integer = SincronizzazioneWG.IntervalloAgendaMinuti()
            If minuti < 5 Then minuti = 5
            Return CDbl(minuti) * 60000.0
        Catch ex As Exception
            _logger.ScriviLog("IntervalloSincronizzazioneAgendaMillisecondi eccezione: " & ex.Message)
            Return 1800000.0
        End Try
    End Function

    Private Sub EseguiSincronizzazioneRubricaAutomatica()
        Try
            If Not SincronizzazioneWG.RubricaAutomaticaAttiva() Then Return
            If SincronizzazioneWG.RubricaInCorso Then Return
            If Not SincronizzazioneWG.EsisteRubricaSincronizzabile() Then Return

            Dim eseguite As Integer = SincronizzazioneWG.SincronizzaRubrica()
            If eseguite > 0 Then
            End If
        Catch ex As Exception
            _logger.ScriviLog("EseguiSincronizzazioneRubricaAutomatica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiSincronizzazioneAgendaAutomatica()
        Try
            If Not SincronizzazioneWG.AgendaAutomaticaAttiva() Then Return
            If SincronizzazioneWG.AgendaInCorso Then Return
            If Not SincronizzazioneWG.EsisteAgendaSincronizzabile() Then Return

            Dim eseguite As Integer = SincronizzazioneWG.SincronizzaAgenda()
            If eseguite > 0 Then
                idsNotificati.Clear()
            End If
        Catch ex As Exception
            _logger.ScriviLog("EseguiSincronizzazioneAgendaAutomatica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub RiallineaTimerSincronizzazione()
        Try
            If timerSincronizzazioneRubrica IsNot Nothing Then
                timerSincronizzazioneRubrica.Stop()
                timerSincronizzazioneRubrica.Interval = IntervalloSincronizzazioneRubricaMillisecondi()
                timerSincronizzazioneRubrica.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("RiallineaTimerSincronizzazione (rubrica): " & ex.Message)
        End Try

        Try
            If timerSincronizzazioneAgenda IsNot Nothing Then
                timerSincronizzazioneAgenda.Stop()
                timerSincronizzazioneAgenda.Interval = IntervalloSincronizzazioneAgendaMillisecondi()
                timerSincronizzazioneAgenda.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("RiallineaTimerSincronizzazione (agenda): " & ex.Message)
        End Try
    End Sub

    Public Sub EseguiSincronizzazioneImmediata()
        Try
            SincronizzazioneWG.SincronizzaTuttoInSottofondo()
        Catch ex As Exception
            _logger.ScriviLog("EseguiSincronizzazioneImmediata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TimerQuotidiani_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            ControllaNuoviArticoliQuotidiani()
        Catch ex As Exception
            _logger.ScriviLog("TimerQuotidiani_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If timerQuotidiani IsNot Nothing Then
                    timerQuotidiani.Interval = ServiziAvvio.IntervalloControlloQuotidianiMinuti() * 60000
                    timerQuotidiani.Start()
                End If
            Catch exRiavvio As Exception
                _logger.ScriviLog("TimerQuotidiani_Elapsed (riavvio): " & exRiavvio.Message)
            End Try
        End Try
    End Sub

    Private Sub ControllaNuoviArticoliQuotidiani()
        Try
            If Not ServiziAvvio.AvvisoNuoviArticoliQuotidianiAttivo() Then Return

            Dim elencoQuotidiani As New ElencoQuotidiani()
            Dim preferiti As List(Of Quotidiano) = elencoQuotidiani.OttieniPreferiti()
            If preferiti Is Nothing OrElse preferiti.Count = 0 Then Return

            Dim massimoArticoli As Integer = ServiziAvvio.MassimoArticoliSegnalatiQuotidiani()
            Dim nuoviArticoliTotali As New List(Of Articolo)

            For Each quotidiano In preferiti
                Try
                    If nuoviArticoliTotali.Count >= massimoArticoli Then Exit For

                    Dim gestoreArticoli As IGestoreArticoli = FabbricaGestoreArticoli.Crea(quotidiano)
                    Dim ultimiArticoli As List(Of Articolo) = gestoreArticoli.OttieniUltimiArticoli(20)
                    If ultimiArticoli Is Nothing OrElse ultimiArticoli.Count = 0 Then Continue For

                    Dim idConosciuti As HashSet(Of String) = GestoreArchivioNotizieQuotidiani.OttieniIdArticoliConosciuti(quotidiano.Url)

                    If idConosciuti.Count = 0 Then
                        Dim tuttiGliId As New List(Of String)
                        For Each articolo In ultimiArticoli
                            If Not String.IsNullOrEmpty(articolo.Id) Then tuttiGliId.Add(articolo.Id)
                        Next
                        GestoreArchivioNotizieQuotidiani.SalvaArticoliConosciuti(quotidiano.Url, tuttiGliId)
                        Continue For
                    End If

                    Dim nuoviDiQuestoQuotidiano As New List(Of Articolo)
                    Dim idDaSalvare As New List(Of String)
                    For Each articolo In ultimiArticoli
                        If String.IsNullOrEmpty(articolo.Id) Then Continue For
                        idDaSalvare.Add(articolo.Id)
                        If Not idConosciuti.Contains(articolo.Id) Then
                            nuoviDiQuestoQuotidiano.Add(articolo)
                        End If
                    Next

                    GestoreArchivioNotizieQuotidiani.SalvaArticoliConosciuti(quotidiano.Url, idDaSalvare)

                    If nuoviDiQuestoQuotidiano.Count = 0 Then Continue For

                    Dim daSegnalare As New List(Of Articolo)
                    For Each articolo In nuoviDiQuestoQuotidiano
                        If nuoviArticoliTotali.Count + daSegnalare.Count >= massimoArticoli Then Exit For
                        daSegnalare.Add(articolo)
                    Next

                    GestoreArchivioNotizieQuotidiani.SalvaArticoliNonLetti(quotidiano.Url, quotidiano.Titolo, daSegnalare)
                    nuoviArticoliTotali.AddRange(daSegnalare)
                Catch exQuotidiano As Exception
                    _logger.ScriviLog("ControllaNuoviArticoliQuotidiani (quotidiano " & quotidiano.Titolo & "): " & exQuotidiano.Message & Environment.NewLine & exQuotidiano.StackTrace)
                End Try
            Next

            If nuoviArticoliTotali.Count > 0 Then
                InviaAvvisoNuoviArticoliQuotidiani()
            End If
        Catch ex As Exception
            _logger.ScriviLog("ControllaNuoviArticoliQuotidiani eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InviaAvvisoNuoviArticoliQuotidiani()
        Try
            MostraAvvisoConScelte("Ci sono nuovi articoli non letti dai tuoi quotidiani preferiti. Vuoi aprirne l'elenco?", "Scegli fra quanto tempo ripresentare l'avviso dei nuovi articoli", AddressOf ApriArticoliQuotidiani, AddressOf PosponiQuotidiani)
        Catch ex As Exception
            _logger.ScriviLog("InviaAvvisoNuoviArticoliQuotidiani eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriArticoliQuotidiani()
        Try
            Dim elenco As New MenuArticoliQuotidianiNonLetti()
        Catch ex As Exception
            _logger.ScriviLog("ApriArticoliQuotidiani eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PosponiQuotidiani(ms As Double)
        Try
            If timerQuotidiani IsNot Nothing Then
                timerQuotidiani.Stop()
                timerQuotidiani.Interval = ms + 60000
                timerQuotidiani.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("PosponiQuotidiani eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Timer_Elapsed(sender As Object, e As ElapsedEventArgs)
        ControllaPreavvisi()
        ControllaBatteria()
    End Sub

    Public Sub RiallineaCartellaDatiAgenda()
        Try
            CLSAgenda.ReimpostaIstanza()
            Me.agenda = CLSAgenda.GetInstance()
            idsNotificati.Clear()
        Catch ex As Exception
            _logger.ScriviLog("RiallineaCartellaDatiAgenda eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiConControlloStack(azione As Action)
        Try
            Dim frmBlc As Form = Nothing
            Dim frmApt = Application.OpenForms

            For Each frm As Form In frmApt
                If frm.Visible AndAlso Not frm.IsDisposed Then
                    Dim tipo As String = frm.GetType().Name
                    If _classiBloccanti.Contains(tipo) Then
                        frmBlc = frm
                        Exit For
                    End If
                End If
            Next

            If frmBlc IsNot Nothing Then
                Dim hdr As FormClosedEventHandler = Nothing
                hdr = Sub(sender, ev)
                          RemoveHandler frmBlc.FormClosed, hdr
                          EseguiConControlloStack(azione)
                      End Sub
                AddHandler frmBlc.FormClosed, hdr
            Else
                azione.Invoke()
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
            azione.Invoke()
        End Try
    End Sub

    Private Sub ControllaBatteria()
        Try
            Dim pwrSts As PowerStatus = SystemInformation.PowerStatus
            Dim pct As Integer = CInt(pwrSts.BatteryLifePercent * 100)
            If pwrSts.PowerLineStatus = PowerLineStatus.Online Then
                _notificato20 = False
                _notificato15 = False
                _notificato10 = False
            Else
                ' Le soglie si segnano PRIMA di mostrare il messaggio: la schermata blocca finché non si chiude e nel frattempo il timer
                ' scatta ancora, quindi altrimenti ne uscirebbe una nuova ogni pochi secondi.
                Dim soglia As Integer = 0
                If pct < 10 Then
                    If Not _notificato10 Then soglia = 10
                ElseIf pct < 15 Then
                    If Not _notificato15 Then soglia = 15
                ElseIf pct < 20 Then
                    If Not _notificato20 Then soglia = 20
                End If
                If soglia > 0 Then
                    _notificato20 = True
                    If soglia <= 15 Then _notificato15 = True
                    If soglia <= 10 Then _notificato10 = True
                    InviaNotificaBatteria(pct)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try
    End Sub

    Private Sub InviaNotificaBatteria(pct As Integer)
        Try
            RegistraNelCentroNotifiche("Sistema", $"Batteria al {pct}%", Nothing)
            Dim frmMn As Form = GetMainFormForInvoke()
            If frmMn IsNot Nothing Then
                frmMn.Invoke(Sub()
                                 EseguiConControlloStack(Sub()
                                                             Try
                                                                 Dim msg As New wgcontrols.WGMessaggi()
                                                                 msg.Messaggio($"Attenzione: La batteria del computer è scesa sotto il {pct + 1}%. Livello attuale: {pct}%")
                                                                 msg.ShowDialog()
                                                             Catch exUI As Exception
                                                             End Try
                                                         End Sub)
                             End Sub)
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try
    End Sub

    Private Sub ControllaPreavvisi()
        Try
            Dim modNot As String = _config.LeggiValore("NotificheAttive")
            Dim mstNot As Boolean = False
            If modNot = "Sempre" Then
                mstNot = True
            ElseIf modNot = "Mattina e Pomeriggio" Then
                Dim hOra As Integer = DateTime.Now.Hour
                If hOra >= 6 And hOra <= 12 Then mstNot = True
                If hOra >= 14 And hOra <= 18 Then mstNot = True
            ElseIf modNot <> "Mai" AndAlso modNot <> "False" Then
                mstNot = True
            End If

            If Not mstNot Then Exit Sub

            Dim inLavStr As String = _config.LeggiValore("InizioLavoro")
            Dim finLavStr As String = _config.LeggiValore("FineLavoro")
            If String.IsNullOrEmpty(inLavStr) Then inLavStr = "09:00"
            If String.IsNullOrEmpty(finLavStr) Then finLavStr = "18:00"

            Dim dtIn As DateTime
            Dim dtFin As DateTime
            DateTime.TryParse(inLavStr, dtIn)
            DateTime.TryParse(finLavStr, dtFin)

            Dim tmAtt As TimeSpan = DateTime.Now.TimeOfDay
            Dim isLav As Boolean = (tmAtt >= dtIn.TimeOfDay AndAlso tmAtt <= dtFin.TimeOfDay)

            Dim aptSca = agenda.OttieniAppuntamentiInScadenza()
            Dim lstNot As New List(Of CLSAgenda.AppuntamentoLista)

            For Each apt In aptSca
                If Not idsNotificati.Contains(apt.Id) Then
                    Dim dts = agenda.OttieniDettagliAppuntamento(apt.Id)
                    If dts.HasValue Then
                        If dts.Value.Categoria.ToLower() = "lavoro" AndAlso Not isLav Then
                            Continue For
                        End If
                    End If
                    lstNot.Add(apt)
                End If
            Next

            If lstNot.Count > 0 Then
                MostraMenuAvvisi(lstNot)
            End If

            If idsNotificati.Count > 1000 Then
                idsNotificati.Clear()
            End If

        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try
    End Sub

    Private Sub MostraMenuAvvisi(lstApt As List(Of CLSAgenda.AppuntamentoLista))
        Try
            RegistraNelCentroNotifiche("Agenda", If(lstApt.Count = 1, "Un appuntamento in scadenza", lstApt.Count & " appuntamenti in scadenza"), AddressOf ConsApp, "avviso:agenda")
            timer.Stop()
            lstAvv = lstApt
            Dim frmMn As Form = GetMainFormForInvoke()
            If frmMn IsNot Nothing Then
                frmMn.Invoke(Sub()
                                 Dim aznNot As Action = Sub()
                                                            Try
                                                                mnuAvv = New WGMenu()
                                                                mnuAvv.aggiungi("Consultazione appuntamenti di oggi", Sub()
                                                                                                                          NotificaCorrenteConclusa()
                                                                                                                          ConsApp()
                                                                                                                      End Sub)
                                                                mnuAvv.aggiungi("Posponi avviso", Sub()
                                                                                                      NotificaCorrenteConclusa()
                                                                                                      SotPosp()
                                                                                                  End Sub)
                                                                mnuAvv.aggiungi("Sposta tutto a domani", Sub()
                                                                                                             NotificaCorrenteConclusa()
                                                                                                             SpsDom()
                                                                                                         End Sub)
                                                                mnuAvv.aggiungi("Segna tutti completati", Sub()
                                                                                                              NotificaCorrenteConclusa()
                                                                                                              SgnCmp()
                                                                                                          End Sub)
                                                                mnuAvv.aggiungi("Ignora per ora", Sub()
                                                                                                      NotificaCorrenteConclusa()
                                                                                                      Ignora()
                                                                                                  End Sub)
                                                                AddHandler mnuAvv.abbandonato, Sub()
                                                                                                    NotificaCorrenteConclusa()
                                                                                                    EscPr()
                                                                                                End Sub
                                                                mnuAvv.avvia("Ci sono appuntamenti in scadenza per oggi, cosa vuoi fare?", true)
                                                            Catch exUI As Exception
                                                                _logger.ScriviLog($"Errore UI: {exUI.Message}")
                                                                If timer IsNot Nothing Then timer.Start()
                                                                NotificaCorrenteConclusa()
                                                            End Try
                                                        End Sub
                                 EseguiConControlloStack(Sub() EseguiInSerieNotifiche(aznNot))
                             End Sub)
            Else
                If timer IsNot Nothing Then timer.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
            Try
                If timer IsNot Nothing Then timer.Start()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ConsApp()
        For Each a In lstAvv
            idsNotificati.Add(a.Id)
        Next
        timer.Interval = 20000
        timer.Start()
        Try
            Dim mnu As New MenuAgenda()
            mnu.consultascadenza()
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SotPosp()
        Try
            EseguiInSerieNotifiche(Sub()
                                       Try
                                           _testoPosponiNotifica = "Scegli fra quanto tempo ripresentare l'avviso degli appuntamenti"
                                           _azionePosponiNotifica = AddressOf PosponiAvvisiAgenda
                                           _azioneAbbandonoPosponiNotifica = AddressOf Ignora
                                           _ripresentaAvvisoNotifica = False
                                           ApriMenuPosponiNotifica()
                                       Catch exAvvio As Exception
                                           _logger.ScriviLog("SotPosp (avvio) eccezione: " & exAvvio.Message & Environment.NewLine & exAvvio.StackTrace)
                                           NotificaCorrenteConclusa()
                                           Try
                                               If timer IsNot Nothing Then
                                                   timer.Interval = 20000
                                                   timer.Start()
                                               End If
                                           Catch exTimer As Exception
                                               _logger.ScriviLog("SotPosp (ripristino timer): " & exTimer.Message)
                                           End Try
                                       End Try
                                   End Sub)
        Catch ex As Exception
            _logger.ScriviLog("SotPosp eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If timer IsNot Nothing Then
                    timer.Interval = 20000
                    timer.Start()
                End If
            Catch exTimer As Exception
                _logger.ScriviLog("SotPosp (ripristino timer): " & exTimer.Message)
            End Try
        End Try
    End Sub

    Private Sub PosponiAvvisiAgenda(ms As Double)
        Try
            If timer IsNot Nothing Then
                timer.Stop()
                timer.Interval = ms
                timer.Start()
            End If
        Catch ex As Exception
            _logger.ScriviLog("PosponiAvvisiAgenda eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SpsDom()
        Try
            For Each a In lstAvv
                Dim dt = agenda.OttieniDettagliAppuntamento(a.Id)
                If dt.HasValue Then
                    Dim v = dt.Value
                    Dim dtDmn As DateTime
                    If DateTime.TryParse(v.Data, dtDmn) Then
                        Dim dtDmnStr = dtDmn.AddDays(1).ToString("dd/MM/yyyy")
                        Dim dtFnStr = dtDmnStr
                        If Not String.IsNullOrEmpty(v.DataFine) Then
                            Dim dtFn As DateTime
                            If DateTime.TryParse(v.DataFine, dtFn) Then
                                dtFnStr = dtFn.AddDays(1).ToString("dd/MM/yyyy")
                            End If
                        End If
                        agenda.ModificaAppuntamento(a.Id, v.Oggetto, v.Nome, v.OraInizio, v.OraFine, v.Note, v.Preavviso, v.Frequenza, dtDmnStr, v.Categoria, dtFnStr)
                    End If
                End If
                idsNotificati.Add(a.Id)
            Next
            Dim msg As New wgcontrols.WGMessaggi()
            msg.Messaggio("Attività spostate a domani.")
            msg.ShowDialog()
            timer.Interval = 20000
            timer.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
            timer.Interval = 20000
            timer.Start()
        End Try
    End Sub

    Private Sub SgnCmp()
        Dim ids As New List(Of String)
        For Each a In lstAvv
            ids.Add(a.Id)
            idsNotificati.Add(a.Id)
        Next
        agenda.SegnaPassatiMassivi(ids)
        timer.Interval = 20000
        timer.Start()
        Dim msg As New wgcontrols.WGMessaggi()
        msg.Messaggio("Appuntamenti completati.")
        msg.ShowDialog()
    End Sub

    Private Sub Ignora()
        For Each a In lstAvv
            idsNotificati.Add(a.Id)
        Next
        timer.Interval = 20000
        timer.Start()
    End Sub

    Private Sub EscPr()
        Try
            mnuAvv.chiudi()
        Catch
        End Try
        Ignora()
    End Sub


    ' ============================================================================================
    ' AVVISO DI POSTA NUOVA
    ' ============================================================================================

    ' La posta non viene interrogata a intervalli: il server avvisa da solo appena arriva un
    ' messaggio, con il comando IDLE dell'IMAP. Qui si aspetta quell'avviso e si domanda se
    ' leggere subito. La sorveglianza parte dopo l'avvio del programma, per non aggiungere
    ' connessioni proprio nel momento in cui il computer ha gia' da fare.
    Private Sub TimerAvvioSorveglianzaPosta_Elapsed(sender As Object, e As ElapsedEventArgs)
        Try
            AvviaSorveglianzaPosta()
        Catch ex As Exception
            _logger.ScriviLog("TimerAvvioSorveglianzaPosta_Elapsed eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaSorveglianzaPosta()
        Try
            If _postaSorvegliata IsNot Nothing Then Return
            _postaSorvegliata = MenuPosta.PostaCondivisa()
            If _postaSorvegliata Is Nothing Then Return
            AddHandler _postaSorvegliata.PostaNuova, AddressOf PostaNuovaRicevuta
            _postaSorvegliata.AvviaSorveglianzaPosta()
        Catch ex As Exception
            _logger.ScriviLog("AvviaSorveglianzaPosta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _postaSorvegliata = Nothing
        End Try
    End Sub

    Private Sub PostaNuovaRicevuta(sender As Object, chiaveAccount As String)
        Try
            If _avvisoPostaInCorso Then Return
            If DateTime.Now < _postaSilenziataFino Then Return
            _avvisoPostaInCorso = True
            MostraAvvisoConScelte("È arrivata posta nuova. Vuoi leggerla adesso?",
                                  "Scegli fra quanto tempo ripresentare l'avviso della posta",
                                  AddressOf ApriPostaDaLeggere, AddressOf PosponiPosta)
        Catch ex As Exception
            _logger.ScriviLog("PostaNuovaRicevuta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _avvisoPostaInCorso = False
        End Try
    End Sub

    Private Sub ApriPostaDaLeggere()
        Try
            _avvisoPostaInCorso = False
            MenuPosta.ApriElencoDaLeggere()
        Catch ex As Exception
            _logger.ScriviLog("ApriPostaDaLeggere eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PosponiPosta(ms As Double)
        Try
            _avvisoPostaInCorso = False
            _postaSilenziataFino = DateTime.Now.AddMilliseconds(ms)
        Catch ex As Exception
            _logger.ScriviLog("PosponiPosta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub FermaSorveglianzaPosta()
        Try
            If _postaSorvegliata Is Nothing Then Return
            RemoveHandler _postaSorvegliata.PostaNuova, AddressOf PostaNuovaRicevuta
            _postaSorvegliata.FermaSorveglianzaPosta()
            _postaSorvegliata = Nothing
        Catch ex As Exception
            _logger.ScriviLog("FermaSorveglianzaPosta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _postaSorvegliata = Nothing
        End Try
    End Sub

    ' Punto d'ingresso unico per mostrare un avviso Sì/No/Posticipa. Se un altro avviso e' gia'
    ' visualizzato, questa richiesta viene accodata ed eseguita automaticamente non appena
    ' l'avviso corrente si conclude (scelta effettuata, o menu abbandonato).
    Private Sub EseguiInSerieNotifiche(azioneAvvio As Action)
        Try
            If azioneAvvio Is Nothing Then Return

            Dim eseguiOra As Boolean = False
            SyncLock _lockNotifiche
                If _notificaCorrenteInCorso Then
                    _codaNotifiche.Enqueue(azioneAvvio)
                Else
                    _notificaCorrenteInCorso = True
                    eseguiOra = True
                End If
            End SyncLock

            If eseguiOra Then azioneAvvio.Invoke()
        Catch ex As Exception
            _logger.ScriviLog("EseguiInSerieNotifiche eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Da chiamare ogni volta che l'avviso attualmente mostrato si conclude in modo definitivo
    ' (scelta Sì/No, posticipo applicato, o menu abbandonato senza scegliere). Fa partire
    ' l'eventuale prossimo avviso in coda.
    Private Sub NotificaCorrenteConclusa()
        Try
            Dim prossima As Action = Nothing
            SyncLock _lockNotifiche
                If _codaNotifiche.Count > 0 Then
                    prossima = _codaNotifiche.Dequeue()
                Else
                    _notificaCorrenteInCorso = False
                End If
            End SyncLock

            If prossima IsNot Nothing Then prossima.Invoke()
        Catch ex As Exception
            _logger.ScriviLog("NotificaCorrenteConclusa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Ogni avviso finisce anche nel centro notifiche: resta consultabile dopo aver risposto "No" o "Posticipa".
    ' Le notifiche si salvano senza la loro azione: con queste, anche dopo un riavvio, Invio nel centro notifiche le apre.
    Private Sub RegistraGestoriNotifiche()
        Try
            WGNotifiche.WGCentroNotifiche.RegistraGestore("avviso:ApriNotizieWinGabriel", AddressOf ApriNotizieWinGabriel)
            WGNotifiche.WGCentroNotifiche.RegistraGestore("avviso:ApriVideoYoutube", AddressOf ApriVideoYoutube)
            WGNotifiche.WGCentroNotifiche.RegistraGestore("avviso:ApriArticoliQuotidiani", AddressOf ApriArticoliQuotidiani)
            WGNotifiche.WGCentroNotifiche.RegistraGestore("avviso:ApriPostaDaLeggere", AddressOf ApriPostaDaLeggere)
            WGNotifiche.WGCentroNotifiche.RegistraGestore("avviso:agenda", Sub()
                                                                               Dim mnu As New MenuAgenda()
                                                                               mnu.consultascadenza()
                                                                           End Sub)
        Catch ex As Exception
            _logger.ScriviLog("RegistraGestoriNotifiche eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraNelCentroNotifiche(applicazione As String, testo As String, azione As Action, Optional chiaveAzione As String = "")
        Try
            WGNotifiche.WGCentroNotifiche.Pubblica(applicazione, testo, azione, chiaveAzione:=chiaveAzione)
        Catch ex As Exception
            _logger.ScriviLog("RegistraNelCentroNotifiche eccezione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ApplicazioneDellAvviso(testoPosponi As String) As String
        Dim t As String = If(testoPosponi, "").ToLowerInvariant()
        If t.Contains("notizie") Then Return "Notizie"
        If t.Contains("video") Then Return "YouTube"
        If t.Contains("articoli") Then Return "Quotidiani"
        If t.Contains("posta") Then Return "Mail"
        Return "Agenda"
    End Function

    Private Sub MostraAvvisoConScelte(testoDomanda As String, testoPosponi As String, azioneConsulta As Action, azionePosponi As Action(Of Double))
        Try
            ' Durante le domande dell'avvio gli altri avvisi aspettano il loro turno.
            If AvvioInCorso Then
                Task.Delay(3000).ContinueWith(Sub(t) MostraAvvisoConScelte(testoDomanda, testoPosponi, azioneConsulta, azionePosponi))
                Return
            End If
            RegistraNelCentroNotifiche(ApplicazioneDellAvviso(testoPosponi), testoDomanda, Sub() ConsultaDalCentroNotifiche(testoDomanda, azioneConsulta),
                                       If(azioneConsulta IsNot Nothing, "avviso:" & azioneConsulta.Method.Name, ""))
            EseguiInSerieNotifiche(Sub()
                                       Try
                                           Dim frmMn As Form = GetMainFormForInvoke()
                                           If frmMn Is Nothing Then
                                               NotificaCorrenteConclusa()
                                               Return
                                           End If

                                           _testoAvvisoNotifica = testoDomanda
                                           _testoPosponiNotifica = testoPosponi
                                           _azioneConsultaNotifica = azioneConsulta
                                           _azionePosponiNotifica = azionePosponi
                                           _azioneAbbandonoPosponiNotifica = Nothing
                                           _ripresentaAvvisoNotifica = True

                                           frmMn.Invoke(New Action(AddressOf AvviaMenuAvvisoNotifica))
                                       Catch exAvvio As Exception
                                           _logger.ScriviLog("MostraAvvisoConScelte (avvio) eccezione: " & exAvvio.Message & Environment.NewLine & exAvvio.StackTrace)
                                           NotificaCorrenteConclusa()
                                       End Try
                                   End Sub)
        Catch ex As Exception
            _logger.ScriviLog("MostraAvvisoConScelte eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Aprendo l'avviso dal centro notifiche la domanda Sì, No, Posticipa non serve più: se restasse aperta sotto, chiudendo
    ' quello che si è aperto si tornerebbe a lei invece che a dove si era.
    Private Sub ConsultaDalCentroNotifiche(testoDomanda As String, azione As Action)
        Try
            Dim aperta As Boolean
            SyncLock _lockNotifiche
                aperta = _notificaCorrenteInCorso AndAlso mnuAvvisoNotifica IsNot Nothing AndAlso _testoAvvisoNotifica = testoDomanda
            End SyncLock
            If aperta Then
                _testoAvvisoNotifica = ""
                ChiudiMenuAvvisoNotifica()
                AzzeraAvvisoPosta()
                NotificaCorrenteConclusa()
            End If
        Catch ex As Exception
            _logger.ScriviLog("ConsultaDalCentroNotifiche eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        If azione IsNot Nothing Then azione.Invoke()
    End Sub

    Private Sub AvviaMenuAvvisoNotifica()
        Try
            EseguiConControlloStack(New Action(AddressOf CreaMenuAvvisoNotifica))
        Catch ex As Exception
            _logger.ScriviLog("AvviaMenuAvvisoNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CreaMenuAvvisoNotifica()
        Try
            _logger.ScriviLog("Avvisi: presento la domanda '" & _testoAvvisoNotifica & "'")
            mnuAvvisoNotifica = New WGMenu()
            mnuAvvisoNotifica.aggiungi("Sì", AddressOf ScegliConsultaNotifica)
            mnuAvvisoNotifica.aggiungi("No", AddressOf ScegliIgnoraNotifica)
            mnuAvvisoNotifica.aggiungi("Posticipa", AddressOf ScegliPosticipaNotifica)
            AddHandler mnuAvvisoNotifica.abbandonato, AddressOf AbbandonaMenuAvvisoNotifica
            mnuAvvisoNotifica.avvia(_testoAvvisoNotifica)
        Catch ex As Exception
            _logger.ScriviLog("CreaMenuAvvisoNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuAvvisoNotifica()
        Try
            If mnuAvvisoNotifica IsNot Nothing Then mnuAvvisoNotifica.chiudi()
        Catch ex As Exception
            _logger.ScriviLog("ChiudiMenuAvvisoNotifica eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaMenuAvvisoNotifica()
        Try
            ChiudiMenuAvvisoNotifica()
            AzzeraAvvisoPosta()
        Catch ex As Exception
            _logger.ScriviLog("AbbandonaMenuAvvisoNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            NotificaCorrenteConclusa()
        End Try
    End Sub

    Private Sub ScegliConsultaNotifica()
        Try
            ChiudiMenuAvvisoNotifica()
            Dim azione As Action = _azioneConsultaNotifica
            If azione IsNot Nothing Then azione.Invoke()
        Catch ex As Exception
            _logger.ScriviLog("ScegliConsultaNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            NotificaCorrenteConclusa()
        End Try
    End Sub

    Private Sub AzzeraAvvisoPosta()
        Try
            _avvisoPostaInCorso = False
        Catch ex As Exception
            _logger.ScriviLog("AzzeraAvvisoPosta eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliIgnoraNotifica()
        Try
            ChiudiMenuAvvisoNotifica()
            AzzeraAvvisoPosta()
        Catch ex As Exception
            _logger.ScriviLog("ScegliIgnoraNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            NotificaCorrenteConclusa()
        End Try
    End Sub

    Private Sub ScegliPosticipaNotifica()
        Try
            ChiudiMenuAvvisoNotifica()
            ApriMenuPosponiNotifica()
        Catch ex As Exception
            _logger.ScriviLog("ScegliPosticipaNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriMenuPosponiNotifica()
        Try
            Dim testo As String = _testoPosponiNotifica
            If String.IsNullOrWhiteSpace(testo) Then testo = "Scegli fra quanto tempo ripresentare l'avviso"

            mnuPosponiNotifica = New WGMenu()
            mnuPosponiNotifica.aggiungi("Tra 15 minuti", AddressOf Posticipa15Minuti)
            mnuPosponiNotifica.aggiungi("Tra 30 minuti", AddressOf Posticipa30Minuti)
            mnuPosponiNotifica.aggiungi("Tra 60 minuti", AddressOf Posticipa60Minuti)
            mnuPosponiNotifica.aggiungi("Tra 4 ore", AddressOf Posticipa4Ore)
            mnuPosponiNotifica.aggiungi("Tra 8 ore", AddressOf Posticipa8Ore)
            mnuPosponiNotifica.aggiungi("Al giorno dopo alle 9", AddressOf PosticipaGiornoDopo)
            AddHandler mnuPosponiNotifica.abbandonato, AddressOf AbbandonaMenuPosponiNotifica
            mnuPosponiNotifica.avvia(testo)
        Catch ex As Exception
            _logger.ScriviLog("ApriMenuPosponiNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuPosponiNotifica()
        Try
            If mnuPosponiNotifica IsNot Nothing Then mnuPosponiNotifica.chiudi()
        Catch ex As Exception
            _logger.ScriviLog("ChiudiMenuPosponiNotifica eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaMenuPosponiNotifica()
        Try
            ChiudiMenuPosponiNotifica()
            Dim azione As Action = _azioneAbbandonoPosponiNotifica
            _azioneAbbandonoPosponiNotifica = Nothing
            If azione IsNot Nothing Then azione.Invoke()
        Catch ex As Exception
            _logger.ScriviLog("AbbandonaMenuPosponiNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            NotificaCorrenteConclusa()
        End Try
    End Sub

    Private Sub Posticipa15Minuti()
        Try
            ApplicaPosticipoNotifica(15.0 * 60000.0)
        Catch ex As Exception
            _logger.ScriviLog("Posticipa15Minuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Posticipa30Minuti()
        Try
            ApplicaPosticipoNotifica(30.0 * 60000.0)
        Catch ex As Exception
            _logger.ScriviLog("Posticipa30Minuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Posticipa60Minuti()
        Try
            ApplicaPosticipoNotifica(60.0 * 60000.0)
        Catch ex As Exception
            _logger.ScriviLog("Posticipa60Minuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Posticipa4Ore()
        Try
            ApplicaPosticipoNotifica(4.0 * 60.0 * 60000.0)
        Catch ex As Exception
            _logger.ScriviLog("Posticipa4Ore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Posticipa8Ore()
        Try
            ApplicaPosticipoNotifica(8.0 * 60.0 * 60000.0)
        Catch ex As Exception
            _logger.ScriviLog("Posticipa8Ore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PosticipaGiornoDopo()
        Try
            Dim domaniMattina As DateTime = DateTime.Today.AddDays(1).AddHours(9)
            Dim ms As Double = (domaniMattina - DateTime.Now).TotalMilliseconds
            If ms <= 0 Then ms = 60000.0
            ApplicaPosticipoNotifica(ms)
        Catch ex As Exception
            _logger.ScriviLog("PosticipaGiornoDopo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApplicaPosticipoNotifica(ms As Double)
        Try
            _azioneAbbandonoPosponiNotifica = Nothing
            ChiudiMenuPosponiNotifica()

            Dim azione As Action(Of Double) = _azionePosponiNotifica
            If azione IsNot Nothing Then azione.Invoke(ms)

            If _ripresentaAvvisoNotifica Then
                ProgrammaRipresentazioneAvviso(_testoAvvisoNotifica, _testoPosponiNotifica, _azioneConsultaNotifica, _azionePosponiNotifica, ms)
            End If

            Dim msg As New wgcontrols.WGMessaggi()
            msg.Messaggio("Avviso posticipato di " & DescrizionePosticipo(ms) & ".")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("ApplicaPosticipoNotifica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            NotificaCorrenteConclusa()
        End Try
    End Sub

    Private Function DescrizionePosticipo(ms As Double) As String
        Try
            Dim minuti As Double = ms / 60000.0
            If minuti < 60.0 Then Return CInt(Math.Round(minuti)).ToString() & " minuti"
            Dim ore As Double = minuti / 60.0
            If ore < 24.0 Then
                Dim oreArrotondate As Integer = CInt(Math.Round(ore))
                If oreArrotondate <= 1 Then Return "un'ora"
                Return oreArrotondate.ToString() & " ore"
            End If
            Return "un giorno"
        Catch ex As Exception
            _logger.ScriviLog("DescrizionePosticipo eccezione: " & ex.Message)
            Return "un po'"
        End Try
    End Function

    Private Sub ProgrammaRipresentazioneAvviso(testoDomanda As String, testoPosponi As String, azioneConsulta As Action, azionePosponi As Action(Of Double), ms As Double)
        Try
            Dim ripresentazione As New RipresentazioneAvviso(Me, testoDomanda, testoPosponi, azioneConsulta, azionePosponi, ms)
            SyncLock _ripresentazioniAttive
                _ripresentazioniAttive.Add(ripresentazione)
            End SyncLock
            ripresentazione.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("ProgrammaRipresentazioneAvviso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RimuoviRipresentazione(ripresentazione As RipresentazioneAvviso)
        Try
            SyncLock _ripresentazioniAttive
                If _ripresentazioniAttive.Contains(ripresentazione) Then _ripresentazioniAttive.Remove(ripresentazione)
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("RimuoviRipresentazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Class RipresentazioneAvviso
        Private ReadOnly _padre As PreavvisoManager
        Private ReadOnly _testoDomanda As String
        Private ReadOnly _testoPosponi As String
        Private ReadOnly _azioneConsulta As Action
        Private ReadOnly _azionePosponi As Action(Of Double)
        Private ReadOnly _timerRipresentazione As System.Timers.Timer

        Public Sub New(padre As PreavvisoManager, testoDomanda As String, testoPosponi As String, azioneConsulta As Action, azionePosponi As Action(Of Double), ms As Double)
            _padre = padre
            _testoDomanda = testoDomanda
            _testoPosponi = testoPosponi
            _azioneConsulta = azioneConsulta
            _azionePosponi = azionePosponi
            Dim intervallo As Double = ms
            If intervallo <= 0 Then intervallo = 60000.0
            _timerRipresentazione = New System.Timers.Timer(intervallo)
            _timerRipresentazione.AutoReset = False
        End Sub

        Public Sub Avvia()
            Try
                AddHandler _timerRipresentazione.Elapsed, AddressOf TimerRipresentazione_Elapsed
                _timerRipresentazione.Start()
            Catch ex As Exception
                _padre.RegistraErroreRipresentazione("RipresentazioneAvviso.Avvia", ex)
            End Try
        End Sub

        Private Sub TimerRipresentazione_Elapsed(sender As Object, e As ElapsedEventArgs)
            Try
                RemoveHandler _timerRipresentazione.Elapsed, AddressOf TimerRipresentazione_Elapsed
                _timerRipresentazione.Stop()
                _timerRipresentazione.Dispose()
            Catch ex As Exception
                _padre.RegistraErroreRipresentazione("RipresentazioneAvviso.TimerRipresentazione_Elapsed (chiusura timer)", ex)
            End Try

            Try
                _padre.RimuoviRipresentazione(Me)
                _padre.MostraAvvisoConScelte(_testoDomanda, _testoPosponi, _azioneConsulta, _azionePosponi)
            Catch ex As Exception
                _padre.RegistraErroreRipresentazione("RipresentazioneAvviso.TimerRipresentazione_Elapsed", ex)
            End Try
        End Sub
    End Class

    Private Sub RegistraErroreRipresentazione(contesto As String, ex As Exception)
        Try
            If ex Is Nothing Then
                Return
            End If
        Catch exLog As Exception
        End Try
    End Sub

    Private Function GetMainFormForInvoke() As Form
        Try
            Return Application.OpenForms.Cast(Of Form)().LastOrDefault(Function(f) f IsNot Nothing AndAlso Not f.IsDisposed AndAlso f.Visible)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    Public Sub StopManager()
        Try
            If timer IsNot Nothing Then
                timer.Stop()
                timer.Dispose()
                timer = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If timerNotizie IsNot Nothing Then
                timerNotizie.Stop()
                timerNotizie.Dispose()
                timerNotizie = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If timerQuotidiani IsNot Nothing Then
                timerQuotidiani.Stop()
                timerQuotidiani.Dispose()
                timerQuotidiani = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If timerVideoYoutube IsNot Nothing Then
                timerVideoYoutube.Stop()
                timerVideoYoutube.Dispose()
                timerVideoYoutube = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If timerSincronizzazioneRubrica IsNot Nothing Then
                timerSincronizzazioneRubrica.Stop()
                timerSincronizzazioneRubrica.Dispose()
                timerSincronizzazioneRubrica = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If timerSincronizzazioneAgenda IsNot Nothing Then
                timerSincronizzazioneAgenda.Stop()
                timerSincronizzazioneAgenda.Dispose()
                timerSincronizzazioneAgenda = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try

        Try
            If IstanzaAttiva Is Me Then
                IstanzaAttiva = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog($"Errore: {ex.Message}")
        End Try
    End Sub
End Class