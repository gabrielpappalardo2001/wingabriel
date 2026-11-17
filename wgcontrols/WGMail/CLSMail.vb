Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Net.Pop3
Imports MailKit.Net.Smtp
Imports MailKit.Search
Imports MailKit.Security
Imports MimeKit
Imports MimeKit.Text
Imports MimeKit.Utils
Imports WGContatti
Imports WGDb

Imports wgcontrols ' Ical.Net (NuGet, MIT, attivamente mantenuta, compatibile .NET Framework): sostituisce la
' generazione/lettura manuale del VEVENT per l'agenda CalDAV. Vedi commento sopra
' CostruisciIcs/AnalizzaEventoDav per il perche' del cambio.
Imports Ical.Net
Imports Ical.Net.CalendarComponents
Imports Ical.Net.DataTypes
Imports Ical.Net.Serialization
' ============================================================================================
' CLSMail - client di posta multi-account di WinGabriel (MailKit / MimeKit)
' ============================================================================================
'
' A cosa serve
'   Gestisce PIU' account di posta contemporaneamente: ogni account viene salvato nel database
'   (tabella account_mail) e richiamato con la sua ChiaveWG. Invio, ricezione, elenco cartelle e
'   sincronizzazione agenda/rubrica accettano tutti la chiave dell'account su cui operare;
'   ScaricaTuttiGliAccount permette invece di avviare lo scaricamento su tutti gli account attivi
'   con una sola chiamata. Messaggi, cartelle ed eventi restano nello stesso database SQLite ma
'   sono sempre marcati con la ChiaveAccount di appartenenza, cosi' le caselle non si mescolano.
'
' Come funziona la ricezione
'   1) OttieniElencoRapidoEAvviaScaricamento(chiaveAccount, ...) si collega, chiede solo
'      Uid + oggetto (envelope) degli ultimi messaggi e restituisce SUBITO l'elenco (Id come
'      stringa, Oggetto, Mittente).
'   2) In parallelo (Task in sottofondo) ogni messaggio viene scaricato per intero (corpo TESTO
'      e HTML, allegati salvati su disco) e registrato nel database SQLite "mail".
'   3) ApriMessaggio recupera un messaggio (scaricandolo se non e' ancora completo) e lo segna
'      come letto sia sul database locale sia sul server IMAP (flag \Seen).
'   ScaricaTuttiGliAccount richiama il punto 1) per ogni account attivo, cosi' lo scaricamento in
'   sottofondo parte per tutte le caselle configurate con una sola chiamata.
'
'   OttieniPaginaMessaggi (usata dal menu posta per mostrare l'elenco della cartella) non e'
'   piu' paginata: restituisce sempre tutti i messaggi della cartella (o tutti i non letti, se
'   richiesto) con un solo giro di rete (una FETCH sull'intero intervallo, oppure una SEARCH
'   seguita da un'unica FETCH per i soli non letti). Il chiamante riceve l'elenco completo solo
'   quando il recupero e' terminato.
'
' Codifica dei messaggi
'   In invio il corpo viene sempre marcato esplicitamente come UTF-8 (anche quando MailKit
'   sceglierebbe un'altra codifica) cosi' i messaggi restano leggibili correttamente da client
'   di posta moderni, webmail compresa. In ricezione la decodifica del charset e degli header
'   codificati (subject, nomi mittente/destinatario) e' gestita automaticamente da MimeKit.
'
' Sincronizzazione agenda/rubrica
'   E' un'implementazione essenziale di CalDAV/CardDAV (REPORT via HTTP con autenticazione
'   Basic) che copre i campi piu' comuni di vCard (FN, EMAIL, TEL, ORG) e iCalendar
'   (SUMMARY, DTSTART, DTEND, LOCATION, DESCRIPTION). Non e' un parser completo degli RFC
'   5545/6350: copre cio' che serve per popolare rubrica e agenda; per provider particolari
'   (autenticazione OAuth, URL di scoperta non standard) puo' richiedere adattamenti.
'   La rubrica funziona anche nel verso opposto: i contatti aggiunti/modificati a mano in
'   CLSContatti (con l'account di sincronizzazione indicato) vengono inviati al server con
'   SincronizzaContattiManualiVersoServer / ...TuttiGliAccount (richiesta HTTP PUT, vCard 3.0
'   costruito da CostruisciVCard). E' la funzione apposita richiesta per chiudere il giro:
'   aggiunta manuale in CLSContatti -> marcatura DaSincronizzare -> invio al CardDAV qui.
'
' Password degli account
'   Questa classe salva la password cosi' come viene passata in ImpostazioniAccountMail: non la
'   cifra ne' la decifra. Chi la usa dall'interfaccia deve occuparsi di cifrarla prima di
'   chiamare AggiungiAccount/AggiornaAccount e di decifrarla dopo averla letta con
'   OttieniAccount/OttieniElencoAccount (coerente con l'idea gia' discussa per WinGabriel di
'   spostare la cifratura delle password su una libreria nativa condivisa).
'
' Dove finiscono i dati
'   dati\mail\wg-mail.sqlite               account, messaggi, allegati (riferimento), cartelle, eventi
'   dati\mail\allegati\{chiave messaggio}  file degli allegati scaricati
'
' Errori
'   Come nelle altre classi di WinGabriel, gli errori intercettati nei Try/Catch vengono scritti
'   in Application.StartupPath & "\dati\logs\log.txt" e il metodo restituisce un valore neutro
'   (stringa vuota, False, lista vuota, Nothing) invece di sollevare l'eccezione al chiamante.
' ============================================================================================

Public Class ImpostazioniAccountMail
    Public Property Chiave As String = ""
    Public Property NomeVisualizzato As String = ""
    Public Property IndirizzoEmail As String = ""
    Public Property NomeUtente As String = ""
    Public Property Password As String = ""

    ' IMAP oppure POP3 (posta in arrivo). SMTP resta separato.
    Public Property ProtocolloRicezione As String = "IMAP"

    Public Property ServerImap As String = ""
    Public Property PortaImap As Integer = 993
    Public Property SicurezzaImap As SecureSocketOptions = SecureSocketOptions.SslOnConnect

    Public Property ServerPop3 As String = ""
    Public Property PortaPop3 As Integer = 995
    Public Property SicurezzaPop3 As SecureSocketOptions = SecureSocketOptions.SslOnConnect

    Public Property ServerSmtp As String = ""
    Public Property PortaSmtp As Integer = 587
    Public Property SicurezzaSmtp As SecureSocketOptions = SecureSocketOptions.StartTls

    Public Property UrlRubricaCardDav As String = ""
    Public Property UrlAgendaCalDav As String = ""

    ' --- Autenticazione OAuth 2.0 per Google (obbligatoria dal 2013: Google NON accetta piu'
    ' Basic Auth, ne' con la password normale ne' con una password per le app, su CardDAV/CalDAV).
    ' Se questi campi sono valorizzati l'account e' considerato "Google autenticato" e la
    ' rubrica/agenda vengono lette e scritte tramite le API REST di Google (People API e
    ' Calendar API) con token Bearer, invece che tramite il canale CardDAV/CalDAV generico
    ' usato dagli altri provider. Vedi CLSMail.AccountGoogleAutenticato/AutenticaGoogleOAuth.
    Public Property GoogleClientId As String = ""
    Public Property GoogleClientSecret As String = ""
    Public Property GoogleRefreshToken As String = ""
    Public Property GoogleAccessToken As String = ""
    Public Property GoogleAccessTokenScadenza As DateTime = DateTime.MinValue

    Public Property Attivo As Boolean = True

    ' Impostazioni per-account richieste dal menu posta
    Public Property ControlloAutomaticoRicezione As Boolean = False
    Public Property IntervalloControlloMinuti As Integer = 15
    Public Property CancellaMessaggiDalServer As Boolean = False
    Public Property SincronizzaRubrica As Boolean = False

    ' Url (con "/" finale) del calendario CalDAV scelto dall'utente per salvare i NUOVI eventi
    ' (uno di quelli restituiti da CLSMail.OttieniElencoCalendari). Se lasciato vuoto, il
    ' comportamento resta quello di sempre: il nuovo evento viene scritto su TUTTI i calendari
    ' scrivibili trovati sull'account (vedi CLSMail.InviaEventoAlServer).
    Public Property CalendarioScrittura As String = ""
End Class

' Un calendario CalDAV trovato sull'account (nome per l'utente + url), restituito da
' OttieniElencoCalendari/OttieniElencoCalendariTuttiGliAccount: serve per far scegliere
' all'utente su quale calendario salvare i nuovi eventi (funziona anche su iCloud, che non
' usa nomi nell'url delle collezioni ma solo identificativi opachi).
Public Class CalendarioDav
    Public Property ChiaveAccount As String = ""
    Public Property Nome As String = ""
    Public Property Url As String = ""
    Public Property Scrivibile As Boolean = True
End Class

' Risultato del test di connessione (IMAP e/o SMTP) prima del salvataggio account.
Public Class EsitoProvaConnessione
    Public Property RicezioneOk As Boolean = False
    Public Property SmtpOk As Boolean = False
    Public Property MessaggioRicezione As String = ""
    Public Property MessaggioSmtp As String = ""
    ' Alias compatibilità
    Public Property ImapOk As Boolean
        Get
            Return RicezioneOk
        End Get
        Set(value As Boolean)
            RicezioneOk = value
        End Set
    End Property
    Public Property MessaggioImap As String
        Get
            Return MessaggioRicezione
        End Get
        Set(value As String)
            MessaggioRicezione = value
        End Set
    End Property

    Public ReadOnly Property TuttoOk As Boolean
        Get
            Return RicezioneOk AndAlso SmtpOk
        End Get
    End Property
End Class

Public Class CartellaImap
    Public Property ChiaveAccount As String = ""
    Public Property Nome As String = ""
    Public Property PercorsoCompleto As String = ""
    Public Property Speciale As String = ""
    Public Property NumeroMessaggi As Integer = 0
    Public Property NumeroNonLetti As Integer = 0
End Class

' Elenco completo di messaggi restituito da OttieniPaginaMessaggi, con il totale della cartella.
' Il nome "Pagina" e' rimasto per compatibilita' con il resto del codice, ma non c'e' piu'
' paginazione: Messaggi contiene sempre tutti i messaggi della cartella (o tutti i non letti).
Public Class PaginaMessaggi
    Public Property Messaggi As New List(Of RiepilogoMessaggio)
    Public Property Totale As Integer = 0
End Class

' Riepilogo restituito subito dall'elenco (senza attendere il download completo).
Public Class RiepilogoMessaggio
    Public Property ChiaveAccount As String = ""
    Public Property Id As String = ""
    Public Property Oggetto As String = ""
    Public Property Mittente As String = ""
    Public Property Data As DateTimeOffset = DateTimeOffset.MinValue
    Public Property Letto As Boolean = False
End Class

Public Class AllegatoMessaggio
    Public Property Nome As String = ""
    Public Property Percorso As String = ""
    Public Property TipoMime As String = ""
    Public Property Dimensione As Long = 0
End Class

Public Class MessaggioCompleto
    Public Property Chiave As String = ""
    Public Property ChiaveAccount As String = ""
    Public Property Id As String = ""
    Public Property Cartella As String = ""
    Public Property Mittente As String = ""
    Public Property MittenteEmail As String = ""
    Public Property Destinatari As String = ""
    Public Property Cc As String = ""
    Public Property Ccn As String = ""
    Public Property Oggetto As String = ""
    Public Property CorpoTesto As String = ""
    Public Property CorpoHtml As String = ""
    Public Property Data As DateTimeOffset = DateTimeOffset.MinValue
    Public Property Letto As Boolean = False
    Public Property Allegati As New List(Of AllegatoMessaggio)
    Public Property RispondiA As String = ""
    Public Property Identificativo As String = ""
    Public Property Riferimenti As String = ""
    Public Property IndirizzoLista As String = ""
    Public Property NomeLista As String = ""

    Public ReadOnly Property ArrivaDaUnaLista As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(IndirizzoLista) OrElse Not String.IsNullOrWhiteSpace(NomeLista)
        End Get
    End Property
End Class

Public Class EventoDav
    Public Property Uid As String = ""
    Public Property Href As String = ""
    Public Property Etag As String = ""
    Public Property Titolo As String = ""
    Public Property Descrizione As String = ""
    Public Property Luogo As String = ""
    Public Property Inizio As DateTime = DateTime.MinValue
    Public Property Fine As DateTime = DateTime.MinValue
    Public Property TuttoIlGiorno As Boolean = False

    ' Url (con "/" finale) del calendario CalDAV da cui e' stato scaricato questo evento. Serve a
    ' chi importa l'evento in agenda per decidere in quale Categoria farlo fluire, secondo le
    ' impostazioni per-calendario scelte dall'utente (vedi CLSAgenda.RisolviCategoriaSincronizzazione).
    Public Property UrlCalendario As String = ""

    ' Regola di ricorrenza (RFC 5545, RRULE), presa cosi' com'e' dal VEVENT (es.
    ' "FREQ=WEEKLY;BYDAY=MO,WE,FR"), senza il prefisso "RRULE:". Vuota se l'evento non e' ricorrente.
    Public Property RegolaRicorrenza As String = ""

    ' Date/orari esclusi dalla ricorrenza (RFC 5545, EXDATE): occorrenze della serie che non
    ' devono comparire (es. l'utente ha cancellato una singola ricorrenza).
    Public Property DateEsclusioni As New List(Of DateTime)
End Class

Partial Public Class CLSMail
    Implements IDisposable

    Private Const NomeTabellaAccount As String = "account_mail"
    Private Const NomeTabellaMessaggi As String = "messaggi"
    Private Const NomeTabellaAllegati As String = "allegati"
    Private Const NomeTabellaCartelle As String = "cartelle_imap"
    Private Const NomeTabellaEventiAgenda As String = "eventi_agenda"
    Private Const NomeTabellaDestinatariRecenti As String = "destinatari_recenti"

    Private ReadOnly _db As WGDatabase
    Private ReadOnly _cartellaAllegati As String
    Private _smaltito As Boolean = False
    Private _ultimoErrore As String = ""

    ' Cache in memoria (per la durata dell'istanza) degli URL "reali" delle collection
    ' CardDAV/CalDAV scoperti con la discovery a 3 passi (principal -> home-set -> collection).
    ' Chiave: url configurato & "|" & utente & "|carddav" oppure "|caldav". Evita di rifare la
    ' discovery ad ogni sincronizzazione (utile soprattutto per iCloud, che la richiede sempre).
    Private ReadOnly _cacheCollezioniDav As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Private Shared _reteDavConfigurata As Boolean = False

    ' Cache di connessioni IMAP gia' autenticate, per evitare di rifare l'handshake TLS e il
    ' login ad ogni singola pagina di messaggi o apertura di un messaggio: era la causa
    ' principale della lentezza percepita nel consultare la posta (ogni chiamata riapriva e
    ' richiudeva una connessione completa). La cache contiene solo connessioni "libere" (non in
    ' uso): OttieniClientImapDaCache la toglie dalla cache, RestituisciClientImapAllaCache la
    ' rimette a disposizione solo se e' ancora valida. Cosi' due chiamate concorrenti sullo
    ' stesso account non si contendono mai la stessa istanza di ImapClient (che non e'
    ' thread-safe): la seconda, non trovando nulla libero in cache, ne apre semplicemente una
    ' nuova, esattamente come succedeva prima di questa modifica.
    Private ReadOnly _connessioniImapLibere As New Dictionary(Of String, ImapClient)
    Private ReadOnly _ultimoUsoConnessioneImap As New Dictionary(Of String, DateTime)
    Private ReadOnly _lockConnessioniImap As New Object()
    Private Const DurataMassimaConnessioneImapInCacheMs As Double = 600000

    Private _tokenAnnullamento As CancellationToken = CancellationToken.None
    Private ReadOnly _lockAnnullamento As New Object()

    Public ReadOnly Property UltimoErrore As String
        Get
            Return If(_ultimoErrore, "")
        End Get
    End Property

    Public Sub ImpostaAnnullamento(token As CancellationToken)
        Try
            SyncLock _lockAnnullamento
                _tokenAnnullamento = token
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.ImpostaAnnullamento: " & ex.ToString())
        End Try
    End Sub

    Public Sub AzzeraAnnullamento()
        Try
            SyncLock _lockAnnullamento
                _tokenAnnullamento = CancellationToken.None
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.AzzeraAnnullamento: " & ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property AnnullamentoRichiesto As Boolean
        Get
            Try
                SyncLock _lockAnnullamento
                    Return _tokenAnnullamento.IsCancellationRequested
                End SyncLock
            Catch ex As Exception
                ScriviLog("CLSMail.AnnullamentoRichiesto: " & ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Private Function Segnale() As CancellationToken
        Try
            SyncLock _lockAnnullamento
                Return _tokenAnnullamento
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.Segnale: " & ex.ToString())
            Return CancellationToken.None
        End Try
    End Function

    Private Function EAnnullamento(ex As Exception) As Boolean
        Try
            If ex Is Nothing Then Return False
            Dim corrente As Exception = ex
            Dim livelli As Integer = 0
            While corrente IsNot Nothing AndAlso livelli < 8
                If TypeOf corrente Is OperationCanceledException Then Return True
                If TypeOf corrente Is ObjectDisposedException Then Return AnnullamentoRichiesto
                corrente = corrente.InnerException
                livelli += 1
            End While
            If TypeOf ex Is AggregateException Then
                For Each interna As Exception In DirectCast(ex, AggregateException).InnerExceptions
                    If interna IsNot Nothing AndAlso TypeOf interna Is OperationCanceledException Then Return True
                Next
            End If
            Return False
        Catch errore As Exception
            ScriviLog("CLSMail.EAnnullamento: " & errore.ToString())
            Return False
        End Try
    End Function

    ' Creazione tabelle, aggiornamento dello schema e cifratura delle password sono lavori da
    ' fare una volta per avvio del programma, non ogni volta che si apre la posta: erano fra le
    ' cose che facevano aspettare all'ingresso.
    Private Shared _preparazioneDatiFatta As Boolean = False
    Private Shared ReadOnly _lockPreparazioneDati As New Object()

    Public Sub New()
        Try
            _db = New WGDatabase("mail", "wg-mail.sqlite")
            _cartellaAllegati = WGDatabase.CartellaDati(Path.Combine("mail", "allegati"))
            SyncLock _lockPreparazioneDati
                If Not _preparazioneDatiFatta Then
                    CreaTabelle()
                    AggiornaSchemaAccountSeNecessario()
                    CifraPasswordEsistenti()
                    _preparazioneDatiFatta = True
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.New: " & ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property Smaltita As Boolean
        Get
            Return _smaltito
        End Get
    End Property

    Private Sub CreaTabelle()
        Try
            _db.CreaTabellaSeNonEsiste(NomeTabellaAccount,
                "NomeVisualizzato TEXT, IndirizzoEmail TEXT, NomeUtente TEXT, Password TEXT, " &
                "ProtocolloRicezione TEXT DEFAULT 'IMAP', " &
                "ServerImap TEXT, PortaImap INTEGER, SicurezzaImap TEXT, " &
                "ServerPop3 TEXT, PortaPop3 INTEGER, SicurezzaPop3 TEXT, " &
                "ServerSmtp TEXT, PortaSmtp INTEGER, SicurezzaSmtp TEXT, " &
                "UrlRubricaCardDav TEXT, UrlAgendaCalDav TEXT, Attivo INTEGER DEFAULT 1, " &
                "ControlloAutomaticoRicezione INTEGER DEFAULT 0, CancellaMessaggiDalServer INTEGER DEFAULT 0, SincronizzaRubrica INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaMessaggi,
                "ChiaveAccount TEXT, Cartella TEXT, UidServer TEXT, MessageId TEXT, Mittente TEXT, MittenteEmail TEXT, " &
                "Destinatari TEXT, DestinatariCc TEXT, DestinatariCcn TEXT, Oggetto TEXT, " &
                "CorpoTesto TEXT, CorpoHtml TEXT, DataMessaggio TEXT, Letto INTEGER DEFAULT 0, " &
                "HaAllegati INTEGER DEFAULT 0, Dimensione INTEGER DEFAULT 0, Completo INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaAllegati,
                "ChiaveMessaggio TEXT, NomeFile TEXT, PercorsoFile TEXT, TipoMime TEXT, Dimensione INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaCartelle,
                "ChiaveAccount TEXT, Nome TEXT, PercorsoCompleto TEXT, Speciale TEXT, NumeroMessaggi INTEGER DEFAULT 0, NumeroNonLetti INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaEventiAgenda,
                "ChiaveAccount TEXT, UidServer TEXT, Titolo TEXT, Inizio TEXT, Fine TEXT, TuttoIlGiorno INTEGER DEFAULT 0, Luogo TEXT, Note TEXT")
            ' Indirizzi a cui e' stata inviata almeno una mail (To/Cc/Ccn), per l'autocompletamento
            ' in fase di composizione. Non e' legata a un account specifico: un indirizzo usato di
            ' recente resta suggerito indipendentemente da quale casella lo ha usato.
            _db.CreaTabellaSeNonEsiste(NomeTabellaDestinatariRecenti,
                "Email TEXT, NomeVisualizzato TEXT, UltimoUtilizzo TEXT, NumeroUtilizzi INTEGER DEFAULT 1")
        Catch ex As Exception
            ScriviLog("CLSMail.CreaTabelle: " & ex.ToString())
        End Try
    End Sub

    ' Aggiunge colonne mancanti senza richiamare ALTER se la colonna c'è già
    ' (EseguiNonQuery di WGDatabase logga ogni errore SQL, anche i "duplicate column").
    Private Sub AggiornaSchemaAccountSeNecessario()
        Try
            AggiungiColonnaSeManca(NomeTabellaAccount, "ControlloAutomaticoRicezione", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaAccount, "IntervalloControlloMinuti", "INTEGER DEFAULT 15")
            AggiungiColonnaSeManca(NomeTabellaAccount, "CancellaMessaggiDalServer", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaAccount, "SincronizzaRubrica", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaAccount, "ProtocolloRicezione", "TEXT DEFAULT 'IMAP'")
            AggiungiColonnaSeManca(NomeTabellaAccount, "ServerPop3", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "PortaPop3", "INTEGER DEFAULT 995")
            AggiungiColonnaSeManca(NomeTabellaAccount, "SicurezzaPop3", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "Firma", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "TestoPredefinito", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "InReplyTo", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "RispondiA", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "Riferimenti", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "ListaIndirizzo", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "ListaNome", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "ThreadId", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaMessaggi, "Segnalibro", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(NomeTabellaAccount, "GoogleClientId", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "GoogleClientSecret", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "GoogleRefreshToken", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "GoogleAccessToken", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaAccount, "GoogleAccessTokenScadenza", "TEXT")
        Catch ex As Exception
            ScriviLog("CLSMail.AggiornaSchemaAccountSeNecessario: " & ex.ToString())
        End Try
    End Sub

    Private Function ColonnaPresente(nomeTabella As String, nomeColonna As String) As Boolean
        Try
            If _db Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            Dim info As DataTable = _db.EseguiQuery("PRAGMA table_info(" & nomeTabella & ");", Nothing)
            If info Is Nothing Then Return False
            For Each riga As DataRow In info.Rows
                If String.Equals(riga("name").ToString(), nomeColonna, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
        Catch
        End Try
        Return False
    End Function

    Private Sub AggiungiColonnaSeManca(nomeTabella As String, nomeColonna As String, definizioneSql As String)
        Try
            If ColonnaPresente(nomeTabella, nomeColonna) Then Return
            _db.EseguiNonQuery("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " " & definizioneSql & ";", Nothing)
        Catch
        End Try
    End Sub

    ' ============================================================================================
    ' LOG ERRORI - dati\logs\log.txt
    ' ============================================================================================
    Private Shared ReadOnly _lockLog As New Object()

    Public Shared Sub ScriviLog(messaggio As String)
        Try
            SyncLock _lockLog
                Dim cartella As String = WGDb.WGPercorsiApplicazione.CartellaLog()
                If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                Dim percorso As String = Path.Combine(cartella, "log.txt")
                Dim riga As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine
                File.AppendAllText(percorso, riga, New UTF8Encoding(False))
            End SyncLock
        Catch
            ' non deve mai interrompere il chiamante
        End Try
    End Sub

    Private Sub ImpostaErrore(contesto As String, ex As Exception)
        Try
            If EAnnullamento(ex) Then
                _ultimoErrore = ""
                Return
            End If
            _ultimoErrore = DescriviErroreMailKit(ex)
        Catch
            _ultimoErrore = If(ex Is Nothing, "Errore sconosciuto.", ex.Message)
        End Try
    End Sub

    ' Traduce le eccezioni MailKit / MimeKit in un messaggio comprensibile, senza farle risalire.
    Private Function DescriviErroreMailKit(ex As Exception) As String
        Try
            If ex Is Nothing Then Return "Errore sconosciuto."

            If TypeOf ex Is AuthenticationException Then
                Return "Autenticazione fallita. Controlla nome utente e password (per Gmail e Outlook serve spesso una password per le app)."
            End If
            If TypeOf ex Is SslHandshakeException Then
                Return "Errore SSL o TLS. Controlla porta e tipo di protezione (SSL diretto o STARTTLS)."
            End If
            If TypeOf ex Is SmtpCommandException Then
                Dim smtpEx As SmtpCommandException = DirectCast(ex, SmtpCommandException)
                Return "Il server SMTP ha rifiutato il comando (codice " & CInt(smtpEx.StatusCode).ToString() & "): " & smtpEx.Message
            End If
            If TypeOf ex Is SmtpProtocolException Then
                Return "Errore di protocollo SMTP: " & ex.Message
            End If
            If TypeOf ex Is ImapCommandException Then
                Return "Il server IMAP ha rifiutato il comando: " & ex.Message
            End If
            If TypeOf ex Is ImapProtocolException Then
                Return "Errore di protocollo IMAP: " & ex.Message
            End If
            If TypeOf ex Is ServiceNotConnectedException Then
                Return "Non connesso al server di posta."
            End If
            If TypeOf ex Is ServiceNotAuthenticatedException Then
                Return "Non autenticato sul server di posta."
            End If
            If TypeOf ex Is FolderNotFoundException Then
                Return "Cartella IMAP non trovata."
            End If
            If TypeOf ex Is FolderNotOpenException Then
                Return "Cartella IMAP non aperta."
            End If
            If TypeOf ex Is MessageNotFoundException Then
                Return "Messaggio non trovato sul server."
            End If
            If TypeOf ex Is ParseException Then
                Return "Indirizzo o contenuto MIME non valido: " & ex.Message
            End If
            If TypeOf ex Is SocketException Then
                Return "Errore di rete (socket): " & ex.Message
            End If
            If TypeOf ex Is IOException Then
                Return "Errore di comunicazione o di file: " & ex.Message
            End If
            If TypeOf ex Is TimeoutException Then
                Return "Tempo scaduto nella comunicazione con il server."
            End If
            If TypeOf ex Is OperationCanceledException Then
                Return "Operazione annullata."
            End If
            If TypeOf ex Is ObjectDisposedException Then
                Return "Connessione di posta già chiusa."
            End If
            If TypeOf ex Is FormatException Then
                Return "Formato non valido: " & ex.Message
            End If
            If ex.InnerException IsNot Nothing AndAlso Not Object.ReferenceEquals(ex.InnerException, ex) Then
                Dim interno As String = DescriviErroreMailKit(ex.InnerException)
                If Not String.IsNullOrWhiteSpace(interno) AndAlso interno <> ex.InnerException.Message Then Return interno
            End If
            Return ex.Message
        Catch
            Return If(ex Is Nothing, "Errore sconosciuto.", ex.Message)
        End Try
    End Function

    ' ============================================================================================
    ' ACCOUNT - CRUD (piu' account contemporaneamente, ognuno con la propria ChiaveWG)
    ' ============================================================================================

    Public Function AggiungiAccount(impostazioni As ImpostazioniAccountMail) As String
        Try
            If impostazioni Is Nothing Then Return ""
            Return _db.InserisciRecord(NomeTabellaAccount, ImpostazioniAccountVersoValori(impostazioni))
        Catch ex As Exception
            ScriviLog("CLSMail.AggiungiAccount: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function AggiornaAccount(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.Chiave) Then Return False
            Return _db.AggiornaRecord(NomeTabellaAccount, impostazioni.Chiave, ImpostazioniAccountVersoValori(impostazioni))
        Catch ex As Exception
            ScriviLog("CLSMail.AggiornaAccount (" & impostazioni?.Chiave & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function EliminaAccount(chiaveAccount As String, Optional eliminaAncheIMessaggiSalvati As Boolean = True) As Boolean
        Try
            If eliminaAncheIMessaggiSalvati Then
                Dim parametri As New Dictionary(Of String, Object) From {{"@account", chiaveAccount}}
                Dim chiaviMessaggi As DataTable = _db.EseguiQuery(
                    "SELECT ChiaveWG FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@account;", parametri)
                For Each riga As DataRow In chiaviMessaggi.Rows
                    Dim chiaveMessaggio As String = riga("ChiaveWG").ToString()
                    _db.EseguiNonQuery("DELETE FROM " & NomeTabellaAllegati & " WHERE ChiaveMessaggio=@c;",
                        New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
                    Try
                        Dim cartellaAllegatiMessaggio As String = Path.Combine(_cartellaAllegati, chiaveMessaggio)
                        If Directory.Exists(cartellaAllegatiMessaggio) Then Directory.Delete(cartellaAllegatiMessaggio, True)
                    Catch exCartella As Exception
                        ScriviLog("CLSMail.EliminaAccount pulizia allegati (" & chiaveMessaggio & "): " & exCartella.ToString())
                    End Try
                Next
                _db.EseguiNonQuery("DELETE FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@account;", parametri)
                _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCartelle & " WHERE ChiaveAccount=@account;", parametri)
                _db.EseguiNonQuery("DELETE FROM " & NomeTabellaEventiAgenda & " WHERE ChiaveAccount=@account;", parametri)
            End If
            Return _db.EliminaRecord(NomeTabellaAccount, chiaveAccount)
        Catch ex As Exception
            ScriviLog("CLSMail.EliminaAccount (" & chiaveAccount & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniAccount(chiaveAccount As String) As ImpostazioniAccountMail
        Try
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaAccount, chiaveAccount)
            If riga Is Nothing Then Return Nothing
            Return RigaVersoImpostazioniAccount(riga)
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniAccount (" & chiaveAccount & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Minimo intervallo (minuti) tra gli account attivi con controllo automatico. 15 se nessuno.
    Public Function OttieniIntervalloControlloMinuti() As Integer
        Try
            Dim minimo As Integer = 0
            For Each a As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If Not a.ControlloAutomaticoRicezione Then Continue For
                If a.IntervalloControlloMinuti <= 0 Then Continue For
                If minimo = 0 OrElse a.IntervalloControlloMinuti < minimo Then minimo = a.IntervalloControlloMinuti
            Next
            If minimo <= 0 Then Return 15
            Return minimo
        Catch
            Return 15
        End Try
    End Function

    Public Function OttieniElencoAccount(Optional soloAttivi As Boolean = False) As List(Of ImpostazioniAccountMail)
        Dim risultato As New List(Of ImpostazioniAccountMail)
        Try
            Dim tabella As DataTable = _db.OttieniTuttiIRecord(NomeTabellaAccount, "NomeVisualizzato ASC")
            For Each riga As DataRow In tabella.Rows
                Dim impostazioni As ImpostazioniAccountMail = RigaVersoImpostazioniAccount(riga)
                If Not soloAttivi OrElse impostazioni.Attivo Then risultato.Add(impostazioni)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniElencoAccount: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ImpostazioniAccountVersoValori(impostazioni As ImpostazioniAccountMail) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object) From {
            {"NomeVisualizzato", impostazioni.NomeVisualizzato},
            {"IndirizzoEmail", impostazioni.IndirizzoEmail},
            {"NomeUtente", impostazioni.NomeUtente},
            {"Password", CifraPassword(impostazioni.Password)},
            {"ProtocolloRicezione", If(String.IsNullOrWhiteSpace(impostazioni.ProtocolloRicezione), "IMAP", impostazioni.ProtocolloRicezione.ToUpperInvariant())},
            {"ServerImap", impostazioni.ServerImap},
            {"PortaImap", impostazioni.PortaImap},
            {"SicurezzaImap", impostazioni.SicurezzaImap.ToString()},
            {"ServerPop3", impostazioni.ServerPop3},
            {"PortaPop3", impostazioni.PortaPop3},
            {"SicurezzaPop3", impostazioni.SicurezzaPop3.ToString()},
            {"ServerSmtp", impostazioni.ServerSmtp},
            {"PortaSmtp", impostazioni.PortaSmtp},
            {"SicurezzaSmtp", impostazioni.SicurezzaSmtp.ToString()},
            {"UrlRubricaCardDav", impostazioni.UrlRubricaCardDav},
            {"UrlAgendaCalDav", impostazioni.UrlAgendaCalDav},
            {"GoogleClientId", impostazioni.GoogleClientId},
            {"GoogleClientSecret", CifraPassword(impostazioni.GoogleClientSecret)},
            {"GoogleRefreshToken", CifraPassword(impostazioni.GoogleRefreshToken)},
            {"GoogleAccessToken", CifraPassword(impostazioni.GoogleAccessToken)},
            {"GoogleAccessTokenScadenza", impostazioni.GoogleAccessTokenScadenza.ToString("o")},
            {"Attivo", If(impostazioni.Attivo, 1, 0)},
            {"ControlloAutomaticoRicezione", If(impostazioni.ControlloAutomaticoRicezione, 1, 0)},
            {"IntervalloControlloMinuti", If(impostazioni.IntervalloControlloMinuti > 0, impostazioni.IntervalloControlloMinuti, 15)},
            {"CancellaMessaggiDalServer", If(impostazioni.CancellaMessaggiDalServer, 1, 0)},
            {"SincronizzaRubrica", If(impostazioni.SincronizzaRubrica, 1, 0)}
        }
    End Function

    Private Function RigaVersoImpostazioniAccount(riga As DataRow) As ImpostazioniAccountMail
        Dim impostazioni As New ImpostazioniAccountMail() With {
            .Chiave = riga("ChiaveWG").ToString(),
            .NomeVisualizzato = riga("NomeVisualizzato").ToString(),
            .IndirizzoEmail = riga("IndirizzoEmail").ToString(),
            .NomeUtente = riga("NomeUtente").ToString(),
            .Password = DecifraPassword(riga("Password").ToString()),
            .ServerImap = riga("ServerImap").ToString(),
            .PortaImap = Convert.ToInt32(riga("PortaImap")),
            .ServerSmtp = riga("ServerSmtp").ToString(),
            .PortaSmtp = Convert.ToInt32(riga("PortaSmtp")),
            .UrlRubricaCardDav = riga("UrlRubricaCardDav").ToString(),
            .UrlAgendaCalDav = riga("UrlAgendaCalDav").ToString(),
            .Attivo = Convert.ToInt32(riga("Attivo")) = 1
        }
        Try
            If riga.Table.Columns.Contains("GoogleClientId") AndAlso Not IsDBNull(riga("GoogleClientId")) Then impostazioni.GoogleClientId = riga("GoogleClientId").ToString()
            If riga.Table.Columns.Contains("GoogleClientSecret") AndAlso Not IsDBNull(riga("GoogleClientSecret")) Then impostazioni.GoogleClientSecret = DecifraPassword(riga("GoogleClientSecret").ToString())
            If riga.Table.Columns.Contains("GoogleRefreshToken") AndAlso Not IsDBNull(riga("GoogleRefreshToken")) Then impostazioni.GoogleRefreshToken = DecifraPassword(riga("GoogleRefreshToken").ToString())
            If riga.Table.Columns.Contains("GoogleAccessToken") AndAlso Not IsDBNull(riga("GoogleAccessToken")) Then impostazioni.GoogleAccessToken = DecifraPassword(riga("GoogleAccessToken").ToString())
            If riga.Table.Columns.Contains("GoogleAccessTokenScadenza") AndAlso Not IsDBNull(riga("GoogleAccessTokenScadenza")) Then
                Dim testoScadenza As String = riga("GoogleAccessTokenScadenza").ToString()
                Dim scadenza As DateTime
                If Not String.IsNullOrWhiteSpace(testoScadenza) AndAlso DateTime.TryParse(testoScadenza, Globalization.CultureInfo.InvariantCulture,
                        Globalization.DateTimeStyles.RoundtripKind, scadenza) Then
                    impostazioni.GoogleAccessTokenScadenza = scadenza
                End If
            End If
        Catch
        End Try
        Try
        Catch
        End Try
        Try
            impostazioni.SicurezzaImap = DirectCast([Enum].Parse(GetType(SecureSocketOptions), riga("SicurezzaImap").ToString()), SecureSocketOptions)
        Catch
            impostazioni.SicurezzaImap = SecureSocketOptions.SslOnConnect
        End Try
        Try
            impostazioni.SicurezzaSmtp = DirectCast([Enum].Parse(GetType(SecureSocketOptions), riga("SicurezzaSmtp").ToString()), SecureSocketOptions)
        Catch
            impostazioni.SicurezzaSmtp = SecureSocketOptions.StartTls
        End Try
        Try
            If riga.Table.Columns.Contains("ControlloAutomaticoRicezione") AndAlso Not IsDBNull(riga("ControlloAutomaticoRicezione")) Then
                impostazioni.ControlloAutomaticoRicezione = Convert.ToInt32(riga("ControlloAutomaticoRicezione")) = 1
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("IntervalloControlloMinuti") AndAlso Not IsDBNull(riga("IntervalloControlloMinuti")) Then
                Dim minuti As Integer = Convert.ToInt32(riga("IntervalloControlloMinuti"))
                If minuti > 0 Then impostazioni.IntervalloControlloMinuti = minuti
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("CancellaMessaggiDalServer") AndAlso Not IsDBNull(riga("CancellaMessaggiDalServer")) Then
                impostazioni.CancellaMessaggiDalServer = Convert.ToInt32(riga("CancellaMessaggiDalServer")) = 1
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("SincronizzaRubrica") AndAlso Not IsDBNull(riga("SincronizzaRubrica")) Then
                impostazioni.SincronizzaRubrica = Convert.ToInt32(riga("SincronizzaRubrica")) = 1
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("ProtocolloRicezione") AndAlso Not IsDBNull(riga("ProtocolloRicezione")) Then
                Dim p As String = riga("ProtocolloRicezione").ToString().Trim().ToUpperInvariant()
                impostazioni.ProtocolloRicezione = If(p = "POP3", "POP3", "IMAP")
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("ServerPop3") AndAlso Not IsDBNull(riga("ServerPop3")) Then
                impostazioni.ServerPop3 = riga("ServerPop3").ToString()
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("PortaPop3") AndAlso Not IsDBNull(riga("PortaPop3")) Then
                impostazioni.PortaPop3 = Convert.ToInt32(riga("PortaPop3"))
            End If
        Catch
        End Try
        Try
            If riga.Table.Columns.Contains("SicurezzaPop3") AndAlso Not IsDBNull(riga("SicurezzaPop3")) AndAlso Not String.IsNullOrWhiteSpace(riga("SicurezzaPop3").ToString()) Then
                impostazioni.SicurezzaPop3 = DirectCast([Enum].Parse(GetType(SecureSocketOptions), riga("SicurezzaPop3").ToString()), SecureSocketOptions)
            End If
        Catch
            impostazioni.SicurezzaPop3 = SecureSocketOptions.SslOnConnect
        End Try
        Return impostazioni
    End Function

    ' ============================================================================================
    ' CONNESSIONE
    ' ============================================================================================

    Private Function CreaClientImap(impostazioni As ImpostazioniAccountMail) As ImapClient
        Dim client As New ImapClient()
        Try
            client.Timeout = 60000
            client.CheckCertificateRevocation = False
            client.Connect(impostazioni.ServerImap, impostazioni.PortaImap, impostazioni.SicurezzaImap)
            client.Authenticate(impostazioni.NomeUtente, impostazioni.Password)
            Return client
        Catch ex As Exception
            Try
                If client IsNot Nothing Then
                    If client.IsConnected Then client.Disconnect(True)
                    client.Dispose()
                End If
            Catch
            End Try
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Toglie dalla cache (se presente e ancora valida) la connessione IMAP libera per l'account
    ''' indicato; altrimenti ne apre una nuova con CreaClientImap. Chi chiama DEVE restituire la
    ''' connessione con RestituisciClientImapAllaCache (in un Finally), altrimenti la connessione
    ''' resta aperta finche' non la raccoglie il garbage collector.
    ''' </summary>
    Private Function OttieniClientImapDaCache(chiaveAccount As String, impostazioni As ImpostazioniAccountMail) As ImapClient
        Try
            SyncLock _lockConnessioniImap
                Dim client As ImapClient = Nothing
                If _connessioniImapLibere.TryGetValue(chiaveAccount, client) Then
                    _connessioniImapLibere.Remove(chiaveAccount)
                    Dim ultimoUso As DateTime = DateTime.MinValue
                    _ultimoUsoConnessioneImap.TryGetValue(chiaveAccount, ultimoUso)
                    _ultimoUsoConnessioneImap.Remove(chiaveAccount)
                    Dim scaduta As Boolean = (DateTime.UtcNow - ultimoUso).TotalMilliseconds > DurataMassimaConnessioneImapInCacheMs
                    If client IsNot Nothing Then
                        If Not scaduta AndAlso client.IsConnected AndAlso client.IsAuthenticated Then
                            Try
                                client.NoOp()
                                Return client
                            Catch
                            End Try
                        End If
                        Try
                            If client.IsConnected Then client.Disconnect(True)
                            client.Dispose()
                        Catch
                        End Try
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniClientImapDaCache (" & If(chiaveAccount, "") & "): " & ex.ToString())
        End Try
        Return CreaClientImap(impostazioni)
    End Function

    ''' <summary>
    ''' Rimette a disposizione della cache la connessione, cosi' la prossima chiamata sullo
    ''' stesso account non deve rifare handshake TLS e login. Se non e' piu' utilizzabile
    ''' (server che ha chiuso la connessione, errore nel frattempo) viene chiusa e scartata.
    ''' </summary>
    Private Sub RestituisciClientImapAllaCache(chiaveAccount As String, client As ImapClient)
        Try
            If client Is Nothing Then Return
            If Not client.IsConnected OrElse Not client.IsAuthenticated Then
                Try
                    client.Dispose()
                Catch
                End Try
                Return
            End If
            SyncLock _lockConnessioniImap
                Dim precedente As ImapClient = Nothing
                If _connessioniImapLibere.TryGetValue(chiaveAccount, precedente) Then
                    Try
                        If precedente IsNot Nothing Then
                            If precedente.IsConnected Then precedente.Disconnect(True)
                            precedente.Dispose()
                        End If
                    Catch
                    End Try
                End If
                _connessioniImapLibere(chiaveAccount) = client
                _ultimoUsoConnessioneImap(chiaveAccount) = DateTime.UtcNow
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.RestituisciClientImapAllaCache (" & If(chiaveAccount, "") & "): " & ex.ToString())
            Try
                If client IsNot Nothing Then client.Dispose()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' Chiude e svuota tutte le connessioni IMAP eventualmente in cache. Chiamato da Dispose.
    ''' </summary>
    Private Sub ChiudiConnessioniImapInCache()
        Try
            SyncLock _lockConnessioniImap
                For Each coppia As KeyValuePair(Of String, ImapClient) In _connessioniImapLibere
                    Try
                        If coppia.Value IsNot Nothing Then
                            If coppia.Value.IsConnected Then coppia.Value.Disconnect(True)
                            coppia.Value.Dispose()
                        End If
                    Catch
                    End Try
                Next
                _connessioniImapLibere.Clear()
                _ultimoUsoConnessioneImap.Clear()
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.ChiudiConnessioniImapInCache: " & ex.ToString())
        End Try
    End Sub

    Private Function UsaPop3(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            Return String.Equals(If(impostazioni.ProtocolloRicezione, ""), "POP3", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ' Connessione POP3 sicura (SSL/TLS via SecureSocketOptions).
    Private Function CreaClientPop3(impostazioni As ImpostazioniAccountMail) As Pop3Client
        Dim client As New Pop3Client()
        Try
            client.Timeout = 30000
            client.CheckCertificateRevocation = False
            Dim server As String = If(String.IsNullOrWhiteSpace(impostazioni.ServerPop3), impostazioni.ServerImap, impostazioni.ServerPop3)
            Dim porta As Integer = If(impostazioni.PortaPop3 > 0, impostazioni.PortaPop3, 995)
            Dim sicurezza As SecureSocketOptions = impostazioni.SicurezzaPop3
            If sicurezza = SecureSocketOptions.None AndAlso porta = 995 Then sicurezza = SecureSocketOptions.SslOnConnect
            client.Connect(server, porta, sicurezza)
            client.Authenticate(impostazioni.NomeUtente, impostazioni.Password)
            Return client
        Catch ex As Exception
            Try
                If client IsNot Nothing Then
                    If client.IsConnected Then client.Disconnect(True)
                    client.Dispose()
                End If
            Catch
            End Try
            Throw
        End Try
    End Function

    Private Function CreaClientSmtp(impostazioni As ImpostazioniAccountMail) As SmtpClient
        Dim client As New SmtpClient()
        Try
            client.Timeout = 30000
            client.CheckCertificateRevocation = False
            client.Connect(impostazioni.ServerSmtp, impostazioni.PortaSmtp, impostazioni.SicurezzaSmtp)
            client.Authenticate(impostazioni.NomeUtente, impostazioni.Password)
            Return client
        Catch ex As Exception
            Try
                If client IsNot Nothing Then
                    If client.IsConnected Then client.Disconnect(True)
                    client.Dispose()
                End If
            Catch
            End Try
            Throw
        End Try
    End Function

    Private Function AggiungiIndirizzoSicuro(elenco As InternetAddressList, testo As String, contesto As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim pulito As String = testo.Trim()
            Try
                elenco.Add(MailboxAddress.Parse(pulito))
                Return True
            Catch exParse As ParseException
                Try
                    If pulito.Contains("<") AndAlso pulito.Contains(">") Then
                        elenco.Add(MailboxAddress.Parse(pulito))
                        Return True
                    End If
                    elenco.Add(New MailboxAddress("", pulito))
                    Return True
                Catch exDue As Exception
                    ImpostaErrore(contesto & " indirizzo (" & pulito & ")", exDue)
                    Return False
                End Try
            End Try
        Catch ex As Exception
            ImpostaErrore(contesto & " indirizzo", ex)
            Return False
        End Try
    End Function

    ' Costruisce un allegato MimeKit: file normale come MimePart (Base64), .eml come MessagePart rfc822.
    Private Function CostruisciAllegatoMime(percorsoFile As String) As MimeEntity
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then Return Nothing
            Dim nomeFile As String = Path.GetFileName(percorsoFile)
            If String.IsNullOrWhiteSpace(nomeFile) Then nomeFile = "allegato"

            If nomeFile.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) Then
                Try
                    Dim nidificato As MimeMessage = MimeMessage.Load(percorsoFile)
                    Dim parteMessaggio As New MessagePart("rfc822")
                    parteMessaggio.Message = nidificato
                    parteMessaggio.ContentDisposition = New ContentDisposition(ContentDisposition.Attachment)
                    parteMessaggio.ContentDisposition.FileName = nomeFile
                    parteMessaggio.ContentType.Name = nomeFile
                    Return parteMessaggio
                Catch exEml As Exception
                    ScriviLog("CLSMail.CostruisciAllegatoMime eml (" & percorsoFile & "): " & DescriviErroreMailKit(exEml))
                End Try
            End If

            Dim tipoMime As String = "application/octet-stream"
            Try
                tipoMime = MimeTypes.GetMimeType(nomeFile)
                If String.IsNullOrWhiteSpace(tipoMime) Then tipoMime = "application/octet-stream"
            Catch
                tipoMime = "application/octet-stream"
            End Try

            Dim tipo As ContentType = ContentType.Parse(tipoMime)
            Dim parte As New MimePart(tipo)
            parte.Content = New MimeContent(File.OpenRead(percorsoFile), ContentEncoding.Unspecified)
            parte.ContentDisposition = New ContentDisposition(ContentDisposition.Attachment)
            parte.ContentTransferEncoding = ContentEncoding.Base64
            parte.FileName = nomeFile
            Return parte
        Catch ex As Exception
            ImpostaErrore("CLSMail.CostruisciAllegatoMime (" & percorsoFile & ")", ex)
            Return Nothing
        End Try
    End Function

    ' ============================================================================================
    ' INVIO - con e senza allegati, dall'account indicato
    ' ============================================================================================

    Public Function InviaMessaggio(chiaveAccount As String, destinatari As List(Of String), oggetto As String, corpo As String, corpoHtml As Boolean,
                                    Optional cc As List(Of String) = Nothing,
                                    Optional ccn As List(Of String) = Nothing,
                                    Optional percorsiAllegati As List(Of String) = Nothing) As Boolean
        _ultimoErrore = ""
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If destinatari Is Nothing OrElse destinatari.Count = 0 Then
                _ultimoErrore = "Nessun destinatario indicato."
                Return False
            End If

            Dim messaggio As New MimeMessage()
            Try
                messaggio.From.Add(New MailboxAddress(
                    If(String.IsNullOrWhiteSpace(impostazioni.NomeVisualizzato), impostazioni.IndirizzoEmail, impostazioni.NomeVisualizzato),
                    impostazioni.IndirizzoEmail))
            Catch exFrom As Exception
                ImpostaErrore("CLSMail.InviaMessaggio mittente", exFrom)
                Return False
            End Try

            Dim destinatariValidi As Integer = 0
            For Each destinatario As String In destinatari
                If AggiungiIndirizzoSicuro(messaggio.To, destinatario, "CLSMail.InviaMessaggio To") Then destinatariValidi += 1
            Next
            If destinatariValidi = 0 Then
                _ultimoErrore = "Nessun destinatario valido."
                Return False
            End If
            If cc IsNot Nothing Then
                For Each indirizzo As String In cc
                    AggiungiIndirizzoSicuro(messaggio.Cc, indirizzo, "CLSMail.InviaMessaggio Cc")
                Next
            End If
            If ccn IsNot Nothing Then
                For Each indirizzo As String In ccn
                    AggiungiIndirizzoSicuro(messaggio.Bcc, indirizzo, "CLSMail.InviaMessaggio Ccn")
                Next
            End If

            messaggio.Subject = If(oggetto, "")
            messaggio.Date = DateTimeOffset.Now
            Try
                messaggio.MessageId = MimeUtils.GenerateMessageId(OttieniDominioDaEmail(impostazioni.IndirizzoEmail))
            Catch
            End Try

            Dim haAllegati As Boolean = percorsiAllegati IsNot Nothing AndAlso percorsiAllegati.Count > 0
            If Not haAllegati Then
                Dim corpoParte As New TextPart(If(corpoHtml, TextFormat.Html, TextFormat.Plain))
                corpoParte.SetText(Encoding.UTF8, If(corpo, ""))
                corpoParte.ContentTransferEncoding = ContentEncoding.QuotedPrintable
                messaggio.Body = corpoParte
            Else
                Dim generatore As New BodyBuilder()
                If corpoHtml Then
                    generatore.HtmlBody = If(corpo, "")
                Else
                    generatore.TextBody = If(corpo, "")
                End If
                For Each percorsoFile As String In percorsiAllegati
                    Try
                        Dim allegato As MimeEntity = CostruisciAllegatoMime(percorsoFile)
                        If allegato IsNot Nothing Then generatore.Attachments.Add(allegato)
                    Catch exAllegato As Exception
                        ImpostaErrore("CLSMail.InviaMessaggio allegato (" & percorsoFile & ")", exAllegato)
                    End Try
                Next
                messaggio.Body = generatore.ToMessageBody()
                ForzaCharsetUtf8(messaggio.Body)
            End If

            Using client As SmtpClient = CreaClientSmtp(impostazioni)
                Try
                    client.Send(messaggio)
                Catch exSend As SmtpCommandException
                    ImpostaErrore("CLSMail.InviaMessaggio SMTP comando", exSend)
                    Try
                        client.Disconnect(True)
                    Catch
                    End Try
                    Return False
                Catch exProto As SmtpProtocolException
                    ImpostaErrore("CLSMail.InviaMessaggio SMTP protocollo", exProto)
                    Try
                        client.Disconnect(True)
                    Catch
                    End Try
                    Return False
                End Try
                Try
                    client.Disconnect(True)
                Catch exDisc As Exception
                    ScriviLog("CLSMail.InviaMessaggio disconnect: " & DescriviErroreMailKit(exDisc))
                End Try
            End Using

            If Not UsaPop3(impostazioni) Then
                Try
                    SalvaMessaggioInviatoSulServer(impostazioni, messaggio)
                Catch exSent As Exception
                    ScriviLog("CLSMail.InviaMessaggio salvataggio inviata: " & DescriviErroreMailKit(exSent))
                End Try
            End If

            Try
                RegistraDestinatariRecenti(messaggio.To.Mailboxes.
                    Concat(messaggio.Cc.Mailboxes).
                    Concat(messaggio.Bcc.Mailboxes))
            Catch exRecenti As Exception
                ScriviLog("CLSMail.InviaMessaggio destinatari recenti: " & exRecenti.ToString())
            End Try

            _ultimoErrore = ""
            Return True
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.InviaMessaggio autenticazione (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.InviaMessaggio SSL (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As SmtpCommandException
            ImpostaErrore("CLSMail.InviaMessaggio SMTP (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As SmtpProtocolException
            ImpostaErrore("CLSMail.InviaMessaggio SMTP (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As ParseException
            ImpostaErrore("CLSMail.InviaMessaggio MIME (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As SocketException
            ImpostaErrore("CLSMail.InviaMessaggio rete (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As IOException
            ImpostaErrore("CLSMail.InviaMessaggio IO (account " & chiaveAccount & ")", ex)
            Return False
        Catch ex As Exception
            ImpostaErrore("CLSMail.InviaMessaggio (account " & chiaveAccount & ")", ex)
            Return False
        End Try
    End Function

    ' ============================================================================================
    ' DESTINATARI RECENTI - per l'autocompletamento in fase di composizione
    ' ============================================================================================

    ' Registra (o aggiorna) un elenco di indirizzi appena usati come destinatari di una mail
    ' inviata (tipicamente To/Cc/Ccn del messaggio spedito). Duplicati nello stesso elenco
    ' (stessa email, maiuscole/minuscole a parte) vengono contati una volta sola.
    Private Sub RegistraDestinatariRecenti(indirizzi As IEnumerable(Of MailboxAddress))
        Try
            If indirizzi Is Nothing Then Return
            Dim gia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each indirizzo As MailboxAddress In indirizzi
                Try
                    If indirizzo Is Nothing OrElse String.IsNullOrWhiteSpace(indirizzo.Address) Then Continue For
                    If Not gia.Add(indirizzo.Address.Trim()) Then Continue For
                    RegistraDestinatarioRecente(indirizzo.Address, indirizzo.Name)
                Catch exIndirizzo As Exception
                    ScriviLog("CLSMail.RegistraDestinatariRecenti indirizzo: " & exIndirizzo.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.RegistraDestinatariRecenti: " & ex.ToString())
        End Try
    End Sub

    ' Registra (o aggiorna) un singolo indirizzo nella tabella dei destinatari recenti: se
    ' l'indirizzo e' gia' presente (confronto case-insensitive) aggiorna solo data dell'ultimo
    ' utilizzo, contatore e nome visualizzato (se questa volta ne abbiamo uno e prima non c'era);
    ' altrimenti inserisce una riga nuova. Restituisce True se l'operazione e' andata a buon fine.
    Public Function RegistraDestinatarioRecente(email As String, Optional nomeVisualizzato As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(email) Then Return False
            Dim emailPulita As String = email.Trim()
            Dim nomePulito As String = If(nomeVisualizzato, "").Trim()
            Dim adesso As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

            Dim esistente As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG, NomeVisualizzato, NumeroUtilizzi FROM " & NomeTabellaDestinatariRecenti & " WHERE Email=@e COLLATE NOCASE LIMIT 1;",
                New Dictionary(Of String, Object) From {{"@e", emailPulita}})

            If esistente IsNot Nothing AndAlso esistente.Rows.Count > 0 Then
                Dim riga As DataRow = esistente.Rows(0)
                Dim chiave As String = riga("ChiaveWG").ToString()
                Dim nomeAttuale As String = riga("NomeVisualizzato").ToString()
                Dim numeroAttuale As Integer = 0
                Integer.TryParse(riga("NumeroUtilizzi").ToString(), numeroAttuale)

                Dim valori As New Dictionary(Of String, Object) From {
                    {"UltimoUtilizzo", adesso},
                    {"NumeroUtilizzi", numeroAttuale + 1}
                }
                If String.IsNullOrWhiteSpace(nomeAttuale) AndAlso Not String.IsNullOrWhiteSpace(nomePulito) Then
                    valori("NomeVisualizzato") = nomePulito
                End If
                _db.AggiornaRecord(NomeTabellaDestinatariRecenti, chiave, valori)
            Else
                Dim valori As New Dictionary(Of String, Object) From {
                    {"Email", emailPulita},
                    {"NomeVisualizzato", nomePulito},
                    {"UltimoUtilizzo", adesso},
                    {"NumeroUtilizzi", 1}
                }
                _db.InserisciRecord(NomeTabellaDestinatariRecenti, valori)
            End If
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.RegistraDestinatarioRecente (" & If(email, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Restituisce i destinatari recenti (piu' di recente usati per primi), utile per
    ' l'autocompletamento nella finestra di composizione. filtro, se indicato, limita ai risultati
    ' la cui email o nome visualizzato lo contengono (case-insensitive).
    Public Function OttieniDestinatariRecenti(Optional filtro As String = "", Optional numeroMassimo As Integer = 20) As DataTable
        Try
            If numeroMassimo <= 0 Then numeroMassimo = 20
            If String.IsNullOrWhiteSpace(filtro) Then
                Return _db.EseguiQuery(
                    "SELECT Email, NomeVisualizzato, UltimoUtilizzo, NumeroUtilizzi FROM " & NomeTabellaDestinatariRecenti &
                    " ORDER BY UltimoUtilizzo DESC LIMIT " & numeroMassimo.ToString() & ";", Nothing)
            End If

            Dim filtroSql As String = "%" & filtro.Trim() & "%"
            Return _db.EseguiQuery(
                "SELECT Email, NomeVisualizzato, UltimoUtilizzo, NumeroUtilizzi FROM " & NomeTabellaDestinatariRecenti &
                " WHERE Email LIKE @f COLLATE NOCASE OR NomeVisualizzato LIKE @f COLLATE NOCASE" &
                " ORDER BY UltimoUtilizzo DESC LIMIT " & numeroMassimo.ToString() & ";",
                New Dictionary(Of String, Object) From {{"@f", filtroSql}})
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniDestinatariRecenti: " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    ' Rimuove un destinatario dall'elenco recenti (es. l'utente lo elimina a mano dai
    ' suggerimenti). Restituisce True se e' stata rimossa una riga.
    Public Function EliminaDestinatarioRecente(email As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(email) Then Return False
            _db.EseguiNonQuery(
                "DELETE FROM " & NomeTabellaDestinatariRecenti & " WHERE Email=@e COLLATE NOCASE;",
                New Dictionary(Of String, Object) From {{"@e", email.Trim()}})
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.EliminaDestinatarioRecente (" & If(email, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Svuota completamente l'elenco dei destinatari recenti (voce "Svuota recenti" nel menu).
    ' Restituisce True se l'operazione e' andata a buon fine.
    Public Function SvuotaDestinatariRecenti() As Boolean
        Try
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaDestinatariRecenti & ";", Nothing)
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.SvuotaDestinatariRecenti: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Dopo l'invio via SMTP, mette una copia del messaggio anche nella cartella "Posta inviata"
    ''' del server IMAP (APPEND, con flag Seen) e - per farla comparire subito nell'app senza dover
    ''' aspettare il prossimo giro di sincronizzazione - la salva anche nel database locale, allegati
    ''' compresi, nella stessa cartella (percorso) usata per l'APPEND. Se il server supporta UIDPLUS
    ''' l'APPEND restituisce l'Uid reale assegnato al messaggio, che viene salvato subito insieme alla
    ''' copia locale: cosi' un successivo OttieniPaginaMessaggi/OttieniElencoRapidoEAvviaScaricamento
    ''' su questa cartella riconosce che il messaggio e' gia' presente (stesso UidServer) e non lo
    ''' duplica. Se il server non supporta UIDPLUS (Uid non disponibile) la copia locale viene comunque
    ''' salvata: SalvaMessaggioCompleto la riconciliera' con quella "vera" del server, quando arrivera',
    ''' cercandola per MessageId invece che per Uid (vedi TrovaChiaveMessaggioPerMessageId).
    ''' </summary>
    Private Sub SalvaMessaggioInviatoSulServer(impostazioni As ImpostazioniAccountMail, messaggio As MimeMessage)
        Try
            If impostazioni Is Nothing OrElse messaggio Is Nothing Then Return
            Dim percorso As String = RisolviPercorsoCartellaSpeciale(impostazioni.Chiave, "sent")
            If String.IsNullOrWhiteSpace(percorso) Then percorso = "Sent"

            Dim uidAssegnato As UniqueId? = Nothing
            Dim client As ImapClient = OttieniClientImapDaCache(impostazioni.Chiave, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorso)
                cartella.Open(FolderAccess.ReadWrite, Segnale())
                uidAssegnato = cartella.Append(messaggio, MessageFlags.Seen)
                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(impostazioni.Chiave, client)
            End Try

            Try
                Dim uidTesto As String = If(uidAssegnato.HasValue, uidAssegnato.Value.Id.ToString(), "")
                Dim chiaveEsistente As String = ""
                If String.IsNullOrWhiteSpace(uidTesto) Then
                    ' Nessun Uid dal server: evitiamo un doppione cercando se questo stesso
                    ' messaggio (per MessageId) e' gia' presente in questa cartella - puo'
                    ' succedere se un giro di sincronizzazione della posta inviata e' partito
                    ' nel frattempo, tra l'APPEND appena fatto e questo salvataggio locale.
                    chiaveEsistente = TrovaChiaveMessaggioPerMessageId(impostazioni.Chiave, percorso, messaggio.MessageId)
                End If
                Dim uidPerSalvataggio As UniqueId = If(uidAssegnato.HasValue, uidAssegnato.Value, New UniqueId(0))
                SalvaMessaggioCompleto(impostazioni.Chiave, percorso, uidPerSalvataggio, messaggio, chiaveEsistente, True)
            Catch exLocale As Exception
                ScriviLog("CLSMail.SalvaMessaggioInviatoSulServer salvataggio locale: " & exLocale.ToString())
            End Try
        Catch ex As Exception
            ScriviLog("CLSMail.SalvaMessaggioInviatoSulServer: " & DescriviErroreMailKit(ex))
        End Try
    End Sub

    ''' <summary>
    ''' Cerca nel database locale, per account e cartella, un messaggio gia' salvato con lo stesso
    ''' MessageId. Serve come chiave di riconciliazione di riserva per la posta inviata quando l'Uid
    ''' del server non e' disponibile subito dopo l'APPEND (server senza estensione UIDPLUS): evita
    ''' che la copia salvata al momento dell'invio e quella scaricata da un successivo giro di
    ''' sincronizzazione finiscano per essere due righe distinte nello stesso database.
    ''' </summary>
    Private Function TrovaChiaveMessaggioPerMessageId(chiaveAccount As String, nomeCartella As String, messageId As String) As String
        Try
            If String.IsNullOrWhiteSpace(messageId) Then Return ""
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c AND MessageId=@m LIMIT 1;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}, {"@m", messageId}})
            If tabella IsNot Nothing AndAlso tabella.Rows.Count > 0 Then Return tabella.Rows(0)("ChiaveWG").ToString()
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.TrovaChiaveMessaggioPerMessageId: " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Recupera dal server la "Posta inviata" di UN account: risolve il percorso reale della
    ''' cartella (che varia da provider a provider) e riusa la stessa logica generica delle altre
    ''' cartelle (elenco rapido subito + scaricamento completo in sottofondo, salvato nel database
    ''' locale). Chiamarla periodicamente (o dopo l'invio) tiene la vista "Posta inviata" allineata
    ''' anche con i messaggi spediti da altri client/webmail sullo stesso account.
    ''' </summary>
    Public Function SincronizzaPostaInviata(chiaveAccount As String,
                                             Optional numeroMassimo As Integer = 0,
                                             Optional alTerminaSingoloMessaggio As Action(Of MessaggioCompleto) = Nothing,
                                             Optional alTerminaTutto As Action(Of String, Integer) = Nothing) As List(Of RiepilogoMessaggio)
        Try
            Dim percorso As String = RisolviPercorsoCartellaSpeciale(chiaveAccount, "sent")
            If String.IsNullOrWhiteSpace(percorso) Then percorso = "Sent"
            Return OttieniElencoRapidoEAvviaScaricamento(chiaveAccount, percorso, numeroMassimo, alTerminaSingoloMessaggio, alTerminaTutto)
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaPostaInviata (account " & chiaveAccount & "): " & ex.ToString())
            Return New List(Of RiepilogoMessaggio)
        End Try
    End Function

    ''' <summary>
    ''' Come SincronizzaPostaInviata, ma per tutti gli account attivi (o per quelli indicati) in un
    ''' solo giro: utile per un pulsante "Aggiorna" generale o per un controllo periodico, esattamente
    ''' come ScaricaTuttiGliAccount fa per l'INBOX - con la differenza che qui il percorso della
    ''' cartella "Posta inviata" viene risolto separatamente per ciascun account, dato che il nome
    ''' reale della cartella cambia da provider a provider (es. "Sent", "Sent Items", "[Gmail]/Posta inviata").
    ''' </summary>
    Public Function SincronizzaPostaInviataTuttiGliAccount(Optional numeroMassimoPerAccount As Integer = 0,
                                                            Optional chiaviAccount As List(Of String) = Nothing,
                                                            Optional alTerminaSingoloMessaggio As Action(Of MessaggioCompleto) = Nothing,
                                                            Optional alTerminaAccount As Action(Of String, Integer) = Nothing) As Dictionary(Of String, List(Of RiepilogoMessaggio))
        Dim risultato As New Dictionary(Of String, List(Of RiepilogoMessaggio))
        Try
            Dim account As List(Of ImpostazioniAccountMail)
            If chiaviAccount IsNot Nothing AndAlso chiaviAccount.Count > 0 Then
                account = chiaviAccount.Select(Function(c) OttieniAccount(c)).Where(Function(a) a IsNot Nothing).ToList()
            Else
                account = OttieniElencoAccount(True)
            End If

            For Each impostazioni As ImpostazioniAccountMail In account
                Try
                    If UsaPop3(impostazioni) Then
                        ' Il POP3 non ha cartelle: la "posta inviata" di questi account resta solo
                        ' quella salvata localmente al momento dell'invio.
                        risultato(impostazioni.Chiave) = New List(Of RiepilogoMessaggio)
                        Continue For
                    End If
                    risultato(impostazioni.Chiave) = SincronizzaPostaInviata(
                        impostazioni.Chiave, numeroMassimoPerAccount, alTerminaSingoloMessaggio, alTerminaAccount)
                Catch exAccount As Exception
                    ScriviLog("CLSMail.SincronizzaPostaInviataTuttiGliAccount account (" & impostazioni.Chiave & "): " & exAccount.ToString())
                    risultato(impostazioni.Chiave) = New List(Of RiepilogoMessaggio)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaPostaInviataTuttiGliAccount: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function CercaMessaggi(chiaveAccount As String, testo As String, Optional nomeCartella As String = "INBOX") As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return elenco
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return elenco
            If UsaPop3(impostazioni) Then
                Dim salvati As DataTable = OttieniMessaggiSalvati(chiaveAccount, nomeCartella)
                Dim q As String = testo.Trim().ToLowerInvariant()
                For Each riga As DataRow In salvati.Rows
                    Dim oggetto As String = riga("Oggetto").ToString()
                    Dim mittente As String = riga("Mittente").ToString()
                    Dim corpo As String = riga("CorpoTesto").ToString()
                    If oggetto.ToLowerInvariant().Contains(q) OrElse mittente.ToLowerInvariant().Contains(q) OrElse corpo.ToLowerInvariant().Contains(q) Then
                        elenco.Add(New RiepilogoMessaggio() With {
                            .ChiaveAccount = chiaveAccount,
                            .Id = riga("UidServer").ToString(),
                            .Oggetto = oggetto,
                            .Mittente = mittente,
                            .Letto = Convert.ToInt32(riga("Letto")) = 1
                        })
                    End If
                Next
                Return elenco
            End If
            Dim testoPulito As String = testo.Trim()
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = RisolviCartellaPerPercorso(client, nomeCartella)
                If cartella Is Nothing Then
                    _ultimoErrore = "Non ho trovato sul server la cartella in cui cercare (" & If(nomeCartella, "") & ")."
                    ScriviLog("CLSMail.CercaMessaggi: cartella non risolta (" & If(nomeCartella, "") & ")")
                    Return elenco
                End If

                If Not cartella.IsOpen Then cartella.Open(FolderAccess.ReadOnly, Segnale())

                ' Non tutti i server accettano la ricerca dentro il corpo del messaggio: se la
                ' rifiutano si ripiega su oggetto e mittente invece di restituire il nulla.
                Dim uid As IList(Of UniqueId) = Nothing
                Try
                    Dim ricercaCompleta As SearchQuery = SearchQuery.SubjectContains(testoPulito).Or(SearchQuery.FromContains(testoPulito)).Or(SearchQuery.ToContains(testoPulito)).Or(SearchQuery.BodyContains(testoPulito))
                    uid = cartella.Search(ricercaCompleta, Segnale())
                Catch exCompleta As Exception
                    ScriviLog("CLSMail.CercaMessaggi ricerca completa non riuscita: " & DescriviErroreMailKit(exCompleta))
                    Try
                        Dim ricercaRidotta As SearchQuery = SearchQuery.SubjectContains(testoPulito).Or(SearchQuery.FromContains(testoPulito))
                        uid = cartella.Search(ricercaRidotta, Segnale())
                    Catch exRidotta As Exception
                        ImpostaErrore("CLSMail.CercaMessaggi ricerca (account " & If(chiaveAccount, "") & ")", exRidotta)
                        Return elenco
                    End Try
                End Try

                If uid IsNot Nothing AndAlso uid.Count > 0 Then
                    Dim riepiloghi As IList(Of IMessageSummary) = cartella.Fetch(uid,
                        MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags, Segnale())
                    For Each voce As IMessageSummary In riepiloghi
                        Try
                            Dim flagLetto As Boolean = voce.Flags.GetValueOrDefault(MessageFlags.None).HasFlag(MessageFlags.Seen)
                            elenco.Add(New RiepilogoMessaggio() With {
                                .ChiaveAccount = chiaveAccount,
                                .Id = voce.UniqueId.Id.ToString(),
                                .Oggetto = If(voce.Envelope IsNot Nothing, voce.Envelope.Subject, ""),
                                .Mittente = If(voce.Envelope IsNot Nothing AndAlso voce.Envelope.From.Count > 0, voce.Envelope.From(0).ToString(), ""),
                                .Data = If(voce.Envelope IsNot Nothing, voce.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue),
                                .Letto = flagLetto
                            })
                        Catch exVoce As Exception
                            ScriviLog("CLSMail.CercaMessaggi voce: " & exVoce.Message)
                        End Try
                    Next
                End If
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As Exception
            ImpostaErrore("CLSMail.CercaMessaggi (account " & If(chiaveAccount, "") & ", " & If(nomeCartella, "") & ")", ex)
            ScriviLog("CLSMail.CercaMessaggi (account " & If(chiaveAccount, "") & ", " & If(nomeCartella, "") & "): " & ex.ToString())
        End Try
        Return elenco
    End Function

    ' Cerca la cartella per percorso senza fidarsi ciecamente di GetFolder, che solleva
    ' un'eccezione se il percorso non corrisponde esattamente a quello del server (separatore
    ' diverso, maiuscole diverse, prefisso di spazio dei nomi).
    Private Function RisolviCartellaPerPercorso(client As ImapClient, percorso As String) As IMailFolder
        Try
            If client Is Nothing Then Return Nothing
            Dim cercato As String = If(percorso, "").Trim()
            If String.IsNullOrWhiteSpace(cercato) Then Return client.Inbox
            If String.Equals(cercato, "INBOX", StringComparison.OrdinalIgnoreCase) Then Return client.Inbox

            Try
                Dim diretta As IMailFolder = client.GetFolder(cercato)
                If diretta IsNot Nothing Then Return diretta
            Catch exDiretta As Exception
                ScriviLog("CLSMail.RisolviCartellaPerPercorso diretta (" & cercato & "): " & exDiretta.Message)
            End Try

            For Each candidata As IMailFolder In RaccogliTutteLeCartelle(client)
                Try
                    If String.Equals(If(candidata.FullName, ""), cercato, StringComparison.OrdinalIgnoreCase) Then Return candidata
                Catch exPercorso As Exception
                    ScriviLog("CLSMail.RisolviCartellaPerPercorso confronto percorso: " & exPercorso.Message)
                End Try
            Next

            For Each candidata As IMailFolder In RaccogliTutteLeCartelle(client)
                Try
                    If String.Equals(If(candidata.Name, ""), cercato, StringComparison.OrdinalIgnoreCase) Then Return candidata
                Catch exNome As Exception
                    ScriviLog("CLSMail.RisolviCartellaPerPercorso confronto nome: " & exNome.Message)
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviCartellaPerPercorso (" & If(percorso, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Impone UTF-8 su tutte le parti testuali del corpo (BodyBuilder a volte sceglie una
    ' codifica "migliore" in base al contenuto: la forziamo per la massima compatibilita').
    Private Sub ForzaCharsetUtf8(entita As MimeEntity)
        Try
            If entita Is Nothing Then Return
            If TypeOf entita Is TextPart Then
                Dim parteTesto As TextPart = DirectCast(entita, TextPart)
                parteTesto.SetText(Encoding.UTF8, parteTesto.Text)
            ElseIf TypeOf entita Is Multipart Then
                For Each figlia As MimeEntity In DirectCast(entita, Multipart)
                    ForzaCharsetUtf8(figlia)
                Next
            End If
        Catch ex As Exception
            ScriviLog("CLSMail.ForzaCharsetUtf8: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniDominioDaEmail(email As String) As String
        Try
            If String.IsNullOrWhiteSpace(email) OrElse Not email.Contains("@") Then Return "wingabriel.local"
            Return email.Substring(email.IndexOf("@"c) + 1)
        Catch
            Return "wingabriel.local"
        End Try
    End Function

    ' ============================================================================================
    ' CARTELLE IMAP (per un singolo account)
    ' ============================================================================================

    Public Function OttieniElencoCartelleImap(chiaveAccount As String) As List(Of CartellaImap)
        Dim risultato As New List(Of CartellaImap)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return risultato

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim sottocartelle As List(Of IMailFolder) = RaccogliTutteLeCartelle(client)
                Dim specialiPerPercorso As Dictionary(Of String, String) = MappaCartelleSpecialiDelServer(client)

                For Each cartella As IMailFolder In sottocartelle
                    Try
                        Dim numeroMessaggi As Integer = cartella.Count
                        Dim numeroNonLetti As Integer = cartella.Unread
                        ' Se il server non ha restituito lo STATUS con GetSubfolders, come ultima
                        ' risorsa si apre la singola cartella (comportamento precedente).
                        ' Contenitori come [Gmail] hanno l'attributo NonSelectable: aprirli
                        ' fa rispondere NO al comando EXAMINE. Non sono cartelle di messaggi,
                        ' vanno solo elencate.
                        Dim apribile As Boolean = True
                        Try
                            apribile = Not cartella.Attributes.HasFlag(FolderAttributes.NonExistent) AndAlso
                                       Not cartella.Attributes.HasFlag(FolderAttributes.NoSelect)
                        Catch exAttributi As Exception
                            ScriviLog("CLSMail.OttieniElencoCartelleImap attributi (" & cartella.FullName & "): " & exAttributi.Message)
                            apribile = True
                        End Try

                        If apribile AndAlso numeroMessaggi = 0 AndAlso numeroNonLetti = 0 AndAlso Not cartella.IsOpen Then
                            cartella.Open(FolderAccess.ReadOnly, Segnale())
                            numeroMessaggi = cartella.Count
                            numeroNonLetti = cartella.Unread
                            cartella.Close()
                        End If
                        Dim etichettaSpeciale As String = OttieniAttributoSpeciale(cartella)
                        If String.IsNullOrWhiteSpace(etichettaSpeciale) Then
                            Try
                                Dim percorsoChiave As String = If(cartella.FullName, "")
                                If specialiPerPercorso.ContainsKey(percorsoChiave) Then etichettaSpeciale = specialiPerPercorso(percorsoChiave)
                            Catch exMappa As Exception
                                ScriviLog("CLSMail.OttieniElencoCartelleImap mappa speciali: " & exMappa.Message)
                            End Try
                        End If
                        If String.IsNullOrWhiteSpace(etichettaSpeciale) Then etichettaSpeciale = SpecialeDaNomeCartella(cartella)

                        risultato.Add(New CartellaImap() With {
                            .ChiaveAccount = chiaveAccount,
                            .Nome = cartella.Name,
                            .PercorsoCompleto = cartella.FullName,
                            .Speciale = etichettaSpeciale,
                            .NumeroMessaggi = numeroMessaggi,
                            .NumeroNonLetti = numeroNonLetti
                        })
                    Catch exCartella As Exception
                        ScriviLog("CLSMail.OttieniElencoCartelleImap cartella (" & cartella.FullName & "): " & exCartella.ToString())
                    End Try
                Next
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            SalvaCartelleNelDb(chiaveAccount, risultato)
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniElencoCartelleImap (account " & chiaveAccount & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function RaccogliTutteLeCartelle(client As ImapClient) As List(Of IMailFolder)
        Dim tutte As New List(Of IMailFolder)
        Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not ClientImapUtilizzabile(client) Then Return tutte
            Dim radici As New List(Of IMailFolder)
            Try
                If client.PersonalNamespaces IsNot Nothing Then
                    For Each spazio As FolderNamespace In client.PersonalNamespaces
                        Try
                            Dim radice As IMailFolder = client.GetFolder(spazio)
                            If radice IsNot Nothing Then radici.Add(radice)
                        Catch exSpazio As Exception
                            ScriviLog("CLSMail.RaccogliTutteLeCartelle spazio: " & exSpazio.Message)
                        End Try
                    Next
                End If
            Catch exSpazi As Exception
                ScriviLog("CLSMail.RaccogliTutteLeCartelle spazi: " & exSpazi.Message)
            End Try

            If radici.Count = 0 Then
                Try
                    radici.Add(client.GetFolder(client.PersonalNamespaces(0)))
                Catch exUnica As Exception
                    ScriviLog("CLSMail.RaccogliTutteLeCartelle radice unica: " & exUnica.Message)
                End Try
            End If

            For Each radice As IMailFolder In radici
                ScendiNelleCartelle(radice, tutte, visti, 0)
            Next

            Try
                Dim inbox As IMailFolder = client.Inbox
                If inbox IsNot Nothing AndAlso Not visti.Contains(If(inbox.FullName, "")) Then
                    visti.Add(If(inbox.FullName, ""))
                    tutte.Insert(0, inbox)
                End If
            Catch exInbox As Exception
                If Not ErroreConnessioneImapPersa(exInbox) Then
                    ScriviLog("CLSMail.RaccogliTutteLeCartelle inbox: " & exInbox.Message)
                End If
            End Try
        Catch ex As Exception
            If Not ErroreConnessioneImapPersa(ex) Then
                ScriviLog("CLSMail.RaccogliTutteLeCartelle: " & ex.ToString())
            End If
        End Try
        Return tutte
    End Function

    Private Sub ScendiNelleCartelle(radice As IMailFolder, destinazione As List(Of IMailFolder), visti As HashSet(Of String), profondita As Integer)
        Try
            If radice Is Nothing Then Return
            If destinazione Is Nothing Then Return
            If profondita > 8 Then Return
            If destinazione.Count > 500 Then Return

            Dim figlie As IList(Of IMailFolder) = Nothing
            Try
                figlie = radice.GetSubfolders(StatusItems.Count Or StatusItems.Unread, False, Segnale())
            Catch exStatus As Exception
                If ErroreConnessioneImapPersa(exStatus) Then Return
                Try
                    figlie = radice.GetSubfolders(False, Segnale())
                Catch exSemplice As Exception
                    If Not ErroreConnessioneImapPersa(exSemplice) Then
                        ScriviLog("CLSMail.ScendiNelleCartelle (" & If(radice.FullName, "") & "): " & exSemplice.Message)
                    End If
                    Return
                End Try
            End Try
            If figlie Is Nothing Then Return

            For Each figlia As IMailFolder In figlie
                Try
                    Dim percorso As String = If(figlia.FullName, "")
                    Dim inesistente As Boolean = False
                    Try
                        inesistente = figlia.Attributes.HasFlag(FolderAttributes.NonExistent)
                    Catch exAttributo As Exception
                        ScriviLog("CLSMail.ScendiNelleCartelle attributi (" & percorso & "): " & exAttributo.Message)
                    End Try

                    If Not inesistente AndAlso Not visti.Contains(percorso) Then
                        visti.Add(percorso)
                        destinazione.Add(figlia)
                    End If

                    Dim puoAvereFiglie As Boolean = True
                    Try
                        If figlia.Attributes.HasFlag(FolderAttributes.HasNoChildren) Then puoAvereFiglie = False
                    Catch exFiglie As Exception
                        ScriviLog("CLSMail.ScendiNelleCartelle figlie (" & percorso & "): " & exFiglie.Message)
                    End Try

                    If puoAvereFiglie Then ScendiNelleCartelle(figlia, destinazione, visti, profondita + 1)
                Catch exSingola As Exception
                    ScriviLog("CLSMail.ScendiNelleCartelle singola: " & exSingola.Message)
                End Try
            Next
        Catch ex As Exception
            If Not ErroreConnessioneImapPersa(ex) Then
                ScriviLog("CLSMail.ScendiNelleCartelle: " & ex.ToString())
            End If
        End Try
    End Sub

    Private Function CartellaSpecialeDalServer(client As ImapClient, tipo As String) As IMailFolder
        Try
            If Not ClientImapUtilizzabile(client) Then Return Nothing
            Select Case If(tipo, "").Trim().ToLowerInvariant()
                Case "inbox"
                    Return client.Inbox
                Case "sent"
                    If Not ServerSupportaCartelleSpeciali(client) Then Return Nothing
                    Return client.GetFolder(SpecialFolder.Sent)
                Case "drafts"
                    If Not ServerSupportaCartelleSpeciali(client) Then Return Nothing
                    Return client.GetFolder(SpecialFolder.Drafts)
                Case "trash"
                    If Not ServerSupportaCartelleSpeciali(client) Then Return Nothing
                    Return client.GetFolder(SpecialFolder.Trash)
                Case "junk"
                    If Not ServerSupportaCartelleSpeciali(client) Then Return Nothing
                    Return client.GetFolder(SpecialFolder.Junk)
                Case Else
                    Return Nothing
            End Select
        Catch exNonSupportato As NotSupportedException
            Return Nothing
        Catch ex As Exception
            If ErroreConnessioneImapPersa(ex) Then Return Nothing
            ScriviLog("CLSMail.CartellaSpecialeDalServer (" & If(tipo, "") & "): " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ServerSupportaCartelleSpeciali(client As ImapClient) As Boolean
        Try
            If client Is Nothing Then Return False
            Return client.Capabilities.Contains(ImapCapability.SpecialUse) OrElse client.Capabilities.Contains(ImapCapability.XList)
        Catch ex As Exception
            ScriviLog("CLSMail.ServerSupportaCartelleSpeciali: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ClientImapUtilizzabile(client As ImapClient) As Boolean
        Try
            Return client IsNot Nothing AndAlso client.IsConnected AndAlso client.IsAuthenticated
        Catch
            Return False
        End Try
    End Function

    Private Function ErroreConnessioneImapPersa(ex As Exception) As Boolean
        Try
            If ex Is Nothing Then Return False
            If TypeOf ex Is SocketException Then Return True
            If TypeOf ex Is IOException Then Return True
            If TypeOf ex Is ObjectDisposedException Then Return True
            If TypeOf ex Is TimeoutException Then Return True
            Dim messaggio As String = If(ex.Message, "")
            If messaggio.IndexOf("not connected", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("has been disconnected", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("SPECIAL-USE", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("XLIST", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("Impossibile stabilire la connessione", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("intervallo di tempo", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If ex.InnerException IsNot Nothing Then Return ErroreConnessioneImapPersa(ex.InnerException)
            Return False
        Catch
            Return False
        End Try
    End Function

    Private Function MappaCartelleSpecialiDelServer(client As ImapClient) As Dictionary(Of String, String)
        Dim mappa As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Try
            Dim tipi As String() = New String() {"inbox", "sent", "drafts", "trash", "junk"}
            For Each tipo As String In tipi
                Try
                    Dim cartella As IMailFolder = CartellaSpecialeDalServer(client, tipo)
                    If cartella Is Nothing Then Continue For
                    Dim percorso As String = If(cartella.FullName, "")
                    If String.IsNullOrWhiteSpace(percorso) Then Continue For
                    If mappa.ContainsKey(percorso) Then Continue For
                    mappa.Add(percorso, EtichettaSpeciale(tipo))
                Catch exTipo As Exception
                    ScriviLog("CLSMail.MappaCartelleSpecialiDelServer (" & tipo & "): " & exTipo.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.MappaCartelleSpecialiDelServer: " & ex.ToString())
        End Try
        Return mappa
    End Function

    Private Function EtichettaSpeciale(tipo As String) As String
        Try
            Select Case If(tipo, "").Trim().ToLowerInvariant()
                Case "inbox" : Return "Posta in arrivo"
                Case "sent" : Return "Posta inviata"
                Case "drafts" : Return "Bozze"
                Case "trash" : Return "Cestino"
                Case "junk" : Return "Posta indesiderata"
                Case Else : Return If(tipo, "")
            End Select
        Catch ex As Exception
            ScriviLog("CLSMail.EtichettaSpeciale: " & ex.Message)
            Return If(tipo, "")
        End Try
    End Function

    Private Function SpecialeDaNomeCartella(cartella As IMailFolder) As String
        Try
            If cartella Is Nothing Then Return ""
            Dim nome As String = If(cartella.Name, "").Trim()
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim tipi As String() = New String() {"sent", "drafts", "trash", "junk", "inbox"}
            For Each tipo As String In tipi
                For Each candidato As String In NomiComuniCartellaSpeciale(tipo)
                    If String.Equals(nome, candidato, StringComparison.OrdinalIgnoreCase) Then Return EtichettaSpeciale(tipo)
                Next
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.SpecialeDaNomeCartella: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function OttieniAttributoSpeciale(cartella As IMailFolder) As String
        Try
            If cartella.Attributes.HasFlag(FolderAttributes.Inbox) Then Return "Posta in arrivo"
            If cartella.Attributes.HasFlag(FolderAttributes.Sent) Then Return "Posta inviata"
            If cartella.Attributes.HasFlag(FolderAttributes.Drafts) Then Return "Bozze"
            If cartella.Attributes.HasFlag(FolderAttributes.Trash) Then Return "Cestino"
            If cartella.Attributes.HasFlag(FolderAttributes.Junk) Then Return "Posta indesiderata"
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniAttributoSpeciale: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub SalvaCartelleNelDb(chiaveAccount As String, cartelle As List(Of CartellaImap))
        Try
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaCartelle & " WHERE ChiaveAccount=@account;",
                New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
            For Each cartella As CartellaImap In cartelle
                Dim valori As New Dictionary(Of String, Object) From {
                    {"ChiaveAccount", chiaveAccount},
                    {"Nome", cartella.Nome},
                    {"PercorsoCompleto", cartella.PercorsoCompleto},
                    {"Speciale", cartella.Speciale},
                    {"NumeroMessaggi", cartella.NumeroMessaggi},
                    {"NumeroNonLetti", cartella.NumeroNonLetti}
                }
                _db.InserisciRecord(NomeTabellaCartelle, valori)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SalvaCartelleNelDb (account " & chiaveAccount & "): " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniCartelleSalvate(Optional chiaveAccount As String = "") As DataTable
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return _db.OttieniTuttiIRecord(NomeTabellaCartelle, "ChiaveAccount ASC, Nome ASC")
            Return _db.EseguiQuery("SELECT * FROM " & NomeTabellaCartelle & " WHERE ChiaveAccount=@account ORDER BY Nome ASC;",
                New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniCartelleSalvate: " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    ' ============================================================================================
    ' RICEZIONE (per un singolo account) - elenco immediato e scaricamento completo in sottofondo
    ' ============================================================================================

    ' Restituisce subito l'elenco (Id come stringa, Oggetto, Mittente, Data, Letto) degli ultimi
    ' "numeroMassimo" messaggi della cartella indicata per l'account indicato, poi avvia in
    ' sottofondo lo scaricamento completo (corpo + allegati) e il salvataggio nel database SQLite.
    ' alTerminaSingoloMessaggio viene richiamato (da un thread di sottofondo) ogni volta che un
    ' messaggio e' stato salvato; alTerminaTutto viene richiamato alla fine con il numero totale
    ' di messaggi salvati. Entrambi sono opzionali.
    Public Function OttieniElencoRapidoEAvviaScaricamento(chiaveAccount As String, nomeCartella As String,
                                                           Optional numeroMassimo As Integer = 0,
                                                           Optional alTerminaSingoloMessaggio As Action(Of MessaggioCompleto) = Nothing,
                                                           Optional alTerminaTutto As Action(Of String, Integer) = Nothing) As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Dim elencoUid As New List(Of UniqueId)

        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return elenco
            End If

            If UsaPop3(impostazioni) Then
                Return OttieniElencoRapidoPop3(chiaveAccount, impostazioni, numeroMassimo, alTerminaSingoloMessaggio, alTerminaTutto)
            End If

            ' Connessione presa dalla cache invece di aprirne sempre una nuova (vedi
            ' OttieniClientImapDaCache): evita handshake TLS + login ad ogni chiamata.
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadOnly, Segnale())

                ' Se non serve alcun filtro, il totale e' gia' noto da cartella.Count dopo l'Open:
                ' evita il giro IMAP SEARCH ALL, che su cartelle grandi puo' pesare.
                Dim daPrendere As Integer
                Dim uidDaLeggere As List(Of UniqueId)
                If cartella.Count = 0 Then
                    daPrendere = 0
                    uidDaLeggere = New List(Of UniqueId)
                ElseIf numeroMassimo > 0 AndAlso numeroMassimo < cartella.Count Then
                    Dim riepiloghiParziali As IList(Of IMessageSummary) = cartella.Fetch(
                        cartella.Count - numeroMassimo, -1, MessageSummaryItems.UniqueId)
                    uidDaLeggere = riepiloghiParziali.Select(Function(v) v.UniqueId).ToList()
                    daPrendere = uidDaLeggere.Count
                Else
                    Dim tuttiGliUid As IList(Of UniqueId) = cartella.Search(SearchQuery.All)
                    daPrendere = tuttiGliUid.Count
                    uidDaLeggere = tuttiGliUid.ToList()
                End If

                If daPrendere > 0 Then
                    Dim riepiloghi As IList(Of IMessageSummary) = cartella.Fetch(uidDaLeggere,
                        MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags)

                    Dim riepiloghiOrdinati As New List(Of IMessageSummary)(riepiloghi)
                    riepiloghiOrdinati.Sort(AddressOf ConfrontaRiepiloghiPerDataDiscendente)

                    For Each voce As IMessageSummary In riepiloghiOrdinati
                        Dim flagLetto As Boolean = voce.Flags.GetValueOrDefault(MessageFlags.None).HasFlag(MessageFlags.Seen)
                        elenco.Add(New RiepilogoMessaggio() With {
                            .ChiaveAccount = chiaveAccount,
                            .Id = voce.UniqueId.Id.ToString(),
                            .Oggetto = If(voce.Envelope IsNot Nothing, voce.Envelope.Subject, ""),
                            .Mittente = If(voce.Envelope IsNot Nothing AndAlso voce.Envelope.From.Count > 0, voce.Envelope.From(0).ToString(), ""),
                            .Data = If(voce.Envelope IsNot Nothing, voce.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue),
                            .Letto = flagLetto
                        })
                        elencoUid.Add(voce.UniqueId)
                    Next
                End If

                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento autenticazione (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento SSL (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As FolderNotFoundException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento cartella (account " & chiaveAccount & ", " & nomeCartella & ")", ex)
            Return elenco
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento IMAP (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As ImapProtocolException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento IMAP (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As SocketException
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento rete (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As Exception
            ImpostaErrore("CLSMail.OttieniElencoRapidoEAvviaScaricamento elenco (account " & chiaveAccount & ", " & nomeCartella & ")", ex)
            Return elenco
        End Try

        ' l'elenco (id + oggetto) e' gia' pronto per essere mostrato: lo scaricamento completo
        ' (corpo, allegati) prosegue da qui in poi in un thread separato.
        Task.Run(Sub() ScaricaMessaggiInSottofondo(chiaveAccount, nomeCartella, elencoUid, alTerminaSingoloMessaggio, alTerminaTutto))

        Return elenco
    End Function

    ''' <summary>
    ''' Legge dal server TUTTI i messaggi della cartella (o, se richiesto, tutti i non letti),
    ''' partendo dal piu' recente. Non c'e' piu' paginazione: il recupero e' un solo giro di
    ''' rete verso il server (una FETCH sull'intero intervallo, oppure una SEARCH seguita da
    ''' un'unica FETCH per i soli non letti), cosi' il tempo di attesa e' il minimo possibile e
    ''' non dipende da quante volte si interroga la cartella. Il corpo di un messaggio viene
    ''' scaricato da ApriMessaggio solo quando lo si apre davvero.
    ''' </summary>
    Public Function OttieniPaginaMessaggi(chiaveAccount As String, nomeCartella As String,
                                          Optional soloNonLetti As Boolean = False) As PaginaMessaggi
        Dim pagina As New PaginaMessaggi()
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return pagina
            End If

            If UsaPop3(impostazioni) Then
                Return OttieniPaginaMessaggiPop3(chiaveAccount, impostazioni, soloNonLetti)
            End If

            ' Connessione presa dalla cache (se una libera e ancora valida e' disponibile per
            ' questo account) invece di aprirne sempre una nuova: evita di rifare handshake TLS
            ' e login ad ogni richiesta, il che rende il recupero piu' veloce.
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = RisolviCartellaPerPercorso(client, nomeCartella)
                If cartella Is Nothing Then
                    _ultimoErrore = "Non ho trovato sul server la cartella " & If(nomeCartella, "") & "."
                    ScriviLog("CLSMail.OttieniPaginaMessaggi: cartella non risolta (" & If(nomeCartella, "") & ")")
                    Return pagina
                End If

                Dim apertaAdesso As Boolean = False
                If Not cartella.IsOpen Then
                    cartella.Open(FolderAccess.ReadOnly, Segnale())
                    apertaAdesso = True
                End If
                If apertaAdesso Then VerificaValiditaCacheCartella(chiaveAccount, nomeCartella, cartella)

                Dim riepiloghi As IList(Of IMessageSummary)

                If soloNonLetti Then
                    ' Una SEARCH per sapere quali UID sono non letti, poi un'unica FETCH per
                    ' tutti insieme: un solo giro di rete indipendentemente da quanti sono.
                    Dim uidNonLetti As IList(Of UniqueId) = cartella.Search(SearchQuery.NotSeen, Segnale())
                    pagina.Totale = If(uidNonLetti Is Nothing, 0, uidNonLetti.Count)

                    If pagina.Totale > 0 Then
                        riepiloghi = cartella.Fetch(uidNonLetti,
                            MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags, Segnale())
                    Else
                        riepiloghi = New List(Of IMessageSummary)
                    End If
                Else
                    ' Nessun filtro: tutta la cartella in un solo FETCH sull'intero intervallo di
                    ' numero di sequenza (0 = primo, -1 = ultimo), invece di leggerla a blocchi.
                    pagina.Totale = cartella.Count

                    If pagina.Totale > 0 Then
                        riepiloghi = cartella.Fetch(0, -1,
                            MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags, Segnale())
                    Else
                        riepiloghi = New List(Of IMessageSummary)
                    End If
                End If

                If riepiloghi IsNot Nothing AndAlso riepiloghi.Count > 0 Then
                    Dim ordinati As New List(Of IMessageSummary)(riepiloghi)
                    ordinati.Sort(AddressOf ConfrontaRiepiloghiPerDataDiscendente)

                    For Each voce As IMessageSummary In ordinati
                        Dim flagLetto As Boolean = voce.Flags.GetValueOrDefault(MessageFlags.None).HasFlag(MessageFlags.Seen)
                        pagina.Messaggi.Add(New RiepilogoMessaggio() With {
                            .ChiaveAccount = chiaveAccount,
                            .Id = voce.UniqueId.Id.ToString(),
                            .Oggetto = If(voce.Envelope IsNot Nothing, voce.Envelope.Subject, ""),
                            .Mittente = If(voce.Envelope IsNot Nothing AndAlso voce.Envelope.From.Count > 0, voce.Envelope.From(0).ToString(), ""),
                            .Data = If(voce.Envelope IsNot Nothing, voce.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue),
                            .Letto = flagLetto
                        })
                    Next
                End If

                If Not soloNonLetti Then
                    Try
                        ScriviStatoCacheCartella(chiaveAccount, nomeCartella, Convert.ToInt64(cartella.UidValidity), pagina.Totale)
                        SalvaRiepiloghiInCache(chiaveAccount, nomeCartella, pagina.Messaggi)
                    Catch exCache As Exception
                        ScriviLog("CLSMail.OttieniPaginaMessaggi cache: " & exCache.Message)
                    End Try
                End If
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi autenticazione (account " & chiaveAccount & ")", ex)
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi SSL (account " & chiaveAccount & ")", ex)
        Catch ex As FolderNotFoundException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi cartella (account " & chiaveAccount & ", " & If(nomeCartella, "") & ")", ex)
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi IMAP (account " & chiaveAccount & ")", ex)
        Catch ex As ImapProtocolException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi IMAP (account " & chiaveAccount & ")", ex)
        Catch ex As SocketException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi rete (account " & chiaveAccount & ")", ex)
        Catch ex As Exception
            ImpostaErrore("CLSMail.OttieniPaginaMessaggi (account " & chiaveAccount & ", " & If(nomeCartella, "") & ")", ex)
        End Try
        Return pagina
    End Function

    ' Piu' recente per primo, per data effettiva del messaggio (Envelope.Date) e non per Uid.
    ' L'Uid cresce quasi sempre nello stesso ordine della data di arrivo, ma non e' garantito:
    ' un messaggio copiato/spostato in questa cartella, o importato da un altro account, puo'
    ' ricevere un Uid piu' alto pur avendo una data piu' vecchia. A parita' di data (o quando la
    ' data manca) l'Uid resta un criterio di riserva, solo per rendere stabile l'ordinamento.
    Private Function ConfrontaRiepiloghiPerDataDiscendente(a As IMessageSummary, b As IMessageSummary) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Dim dataA As DateTimeOffset = If(a.Envelope IsNot Nothing, a.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue)
            Dim dataB As DateTimeOffset = If(b.Envelope IsNot Nothing, b.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue)
            Dim esito As Integer = dataB.CompareTo(dataA)
            If esito <> 0 Then Return esito
            Return b.UniqueId.Id.CompareTo(a.UniqueId.Id)
        Catch ex As Exception
            ScriviLog("CLSMail.ConfrontaRiepiloghiPerDataDiscendente: " & ex.ToString())
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Come OttieniPaginaMessaggi, ma per POP3: legge le intestazioni di TUTTI i messaggi della
    ''' casella. Il protocollo POP3 non offre un comando che restituisca piu' intestazioni in un
    ''' colpo solo (a differenza della FETCH di IMAP): un comando UIDL recupera tutti gli UID
    ''' insieme, ma le intestazioni vanno comunque chieste un messaggio alla volta sulla stessa
    ''' connessione. E' il massimo di velocita' che il protocollo consente.
    ''' </summary>
    Private Function OttieniPaginaMessaggiPop3(chiaveAccount As String, impostazioni As ImpostazioniAccountMail,
                                               soloNonLetti As Boolean) As PaginaMessaggi
        Dim pagina As New PaginaMessaggi()
        Dim indici As New List(Of Integer)
        Try
            Using client As Pop3Client = CreaClientPop3(impostazioni)
                Dim totale As Integer = client.Count
                pagina.Totale = totale
                If totale > 0 Then
                    ' Un solo comando UIDL per tutti gli UID, invece di uno per messaggio.
                    Dim tuttiGliUid As IList(Of String) = Nothing
                    Try
                        tuttiGliUid = client.GetMessageUids()
                    Catch exUid As Exception
                        ScriviLog("CLSMail.OttieniPaginaMessaggiPop3 UIDL: " & DescriviErroreMailKit(exUid))
                    End Try

                    ' Stato "letto" locale caricato in blocco per l'intera cartella, invece di
                    ' una query per messaggio.
                    Dim statoLetto As Dictionary(Of String, Boolean) = OttieniStatoLettoMassivo(chiaveAccount, "INBOX")

                    For i As Integer = totale - 1 To 0 Step -1
                        Try
                            Dim headers As HeaderList = client.GetMessageHeaders(i)
                            Dim oggetto As String = ""
                            Dim mittente As String = ""
                            Dim dataMsg As DateTimeOffset = DateTimeOffset.MinValue
                            Try
                                oggetto = If(headers(HeaderId.Subject), "")
                            Catch
                            End Try
                            Try
                                mittente = If(headers(HeaderId.From), "")
                            Catch
                            End Try
                            Try
                                Dim dataTesto As String = headers(HeaderId.Date)
                                Dim dt As DateTimeOffset
                                If DateTimeOffset.TryParse(dataTesto, dt) Then dataMsg = dt
                            Catch
                            End Try
                            Dim idPop As String = "pop3-" & i.ToString()
                            If tuttiGliUid IsNot Nothing AndAlso i < tuttiGliUid.Count AndAlso Not String.IsNullOrWhiteSpace(tuttiGliUid(i)) Then
                                idPop = "pop3-" & tuttiGliUid(i)
                            End If
                            Dim giaLetto As Boolean = False
                            statoLetto.TryGetValue(idPop, giaLetto)
                            If soloNonLetti AndAlso giaLetto Then Continue For
                            pagina.Messaggi.Add(New RiepilogoMessaggio() With {
                                .ChiaveAccount = chiaveAccount,
                                .Id = idPop,
                                .Oggetto = oggetto,
                                .Mittente = mittente,
                                .Data = dataMsg,
                                .Letto = giaLetto
                            })
                            indici.Add(i)
                        Catch exMsg As Exception
                            ScriviLog("CLSMail.OttieniPaginaMessaggiPop3 messaggio " & i.ToString() & ": " & DescriviErroreMailKit(exMsg))
                        End Try
                    Next
                End If
                client.Disconnect(True)
            End Using
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggiPop3 autenticazione (account " & chiaveAccount & ")", ex)
            Return pagina
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggiPop3 SSL (account " & chiaveAccount & ")", ex)
            Return pagina
        Catch ex As Pop3CommandException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggiPop3 POP3 (account " & chiaveAccount & ")", ex)
            Return pagina
        Catch ex As Pop3ProtocolException
            ImpostaErrore("CLSMail.OttieniPaginaMessaggiPop3 POP3 (account " & chiaveAccount & ")", ex)
            Return pagina
        Catch ex As Exception
            ImpostaErrore("CLSMail.OttieniPaginaMessaggiPop3 (account " & chiaveAccount & ")", ex)
            Return pagina
        End Try

        ' Su POP3 il corpo non e' recuperabile a richiesta: si scarica in sottofondo tutta la lista appena letta.
        If indici.Count > 0 Then
            Task.Run(Sub() ScaricaMessaggiPop3InSottofondo(chiaveAccount, indici, Nothing, Nothing))
        End If
        Return pagina
    End Function

    Private Function MessaggioLocaleLetto(chiaveAccount As String, nomeCartella As String, idMessaggio As String) As Boolean
        Try
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT Letto FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c AND UidServer=@u LIMIT 1;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}, {"@u", idMessaggio}})
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return False
            Return Convert.ToInt32(tabella.Rows(0)("Letto")) = 1
        Catch ex As Exception
            ScriviLog("CLSMail.MessaggioLocaleLetto (" & If(idMessaggio, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Come MessaggioLocaleLetto, ma per l'intera cartella in un'unica query: da preferire quando
    ' si deve conoscere lo stato "letto" di molti messaggi nella stessa chiamata (evita l'N+1).
    Private Function OttieniStatoLettoMassivo(chiaveAccount As String, nomeCartella As String) As Dictionary(Of String, Boolean)
        Dim risultato As New Dictionary(Of String, Boolean)
        Try
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT UidServer, Letto FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}})
            If tabella IsNot Nothing Then
                For Each riga As DataRow In tabella.Rows
                    risultato(riga("UidServer").ToString()) = Convert.ToInt32(riga("Letto")) = 1
                Next
            End If
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniStatoLettoMassivo (" & chiaveAccount & ", " & nomeCartella & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' --- POP3: elenco rapido + scaricamento completo (posta in arrivo locale "INBOX") ---
    Private Function OttieniElencoRapidoPop3(chiaveAccount As String, impostazioni As ImpostazioniAccountMail,
                                              numeroMassimo As Integer,
                                              alTerminaSingoloMessaggio As Action(Of MessaggioCompleto),
                                              alTerminaTutto As Action(Of String, Integer)) As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Dim indici As New List(Of Integer)
        Try
            Using client As Pop3Client = CreaClientPop3(impostazioni)
                Dim totale As Integer = client.Count
                Dim daPrendere As Integer = totale
                If numeroMassimo > 0 Then daPrendere = Math.Min(numeroMassimo, totale)
                Dim inizio As Integer = Math.Max(0, totale - daPrendere)

                ' Un solo comando UIDL per tutti gli UID, invece di uno per messaggio.
                Dim tuttiGliUid As IList(Of String) = Nothing
                Try
                    tuttiGliUid = client.GetMessageUids()
                Catch exUid As Exception
                    ScriviLog("CLSMail.OttieniElencoRapidoPop3 UIDL: " & DescriviErroreMailKit(exUid))
                End Try

                For i As Integer = totale - 1 To inizio Step -1
                    Try
                        Dim headers As HeaderList = client.GetMessageHeaders(i)
                        Dim oggetto As String = ""
                        Dim mittente As String = ""
                        Dim dataMsg As DateTimeOffset = DateTimeOffset.MinValue
                        Try
                            oggetto = If(headers(HeaderId.Subject), "")
                        Catch
                        End Try
                        Try
                            mittente = If(headers(HeaderId.From), "")
                        Catch
                        End Try
                        Try
                            Dim dataTesto As String = headers(HeaderId.Date)
                            Dim dt As DateTimeOffset
                            If DateTimeOffset.TryParse(dataTesto, dt) Then dataMsg = dt
                        Catch
                        End Try
                        Dim idPop As String = "pop3-" & i.ToString()
                        If tuttiGliUid IsNot Nothing AndAlso i < tuttiGliUid.Count AndAlso Not String.IsNullOrWhiteSpace(tuttiGliUid(i)) Then
                            idPop = "pop3-" & tuttiGliUid(i)
                        End If
                        elenco.Add(New RiepilogoMessaggio() With {
                            .ChiaveAccount = chiaveAccount,
                            .Id = idPop,
                            .Oggetto = oggetto,
                            .Mittente = mittente,
                            .Data = dataMsg,
                            .Letto = False
                        })
                        indici.Add(i)
                    Catch exMsg As Exception
                        ScriviLog("CLSMail.OttieniElencoRapidoPop3 messaggio " & i.ToString() & ": " & DescriviErroreMailKit(exMsg))
                    End Try
                Next
                client.Disconnect(True)
            End Using
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.OttieniElencoRapidoPop3 autenticazione (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.OttieniElencoRapidoPop3 SSL (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As Pop3CommandException
            ImpostaErrore("CLSMail.OttieniElencoRapidoPop3 POP3 (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As Pop3ProtocolException
            ImpostaErrore("CLSMail.OttieniElencoRapidoPop3 POP3 (account " & chiaveAccount & ")", ex)
            Return elenco
        Catch ex As Exception
            ImpostaErrore("CLSMail.OttieniElencoRapidoPop3 (account " & chiaveAccount & ")", ex)
            Return elenco
        End Try

        Task.Run(Sub() ScaricaMessaggiPop3InSottofondo(chiaveAccount, indici, alTerminaSingoloMessaggio, alTerminaTutto))
        Return elenco
    End Function

    Private Sub ScaricaMessaggiPop3InSottofondo(chiaveAccount As String, indici As List(Of Integer),
                                                 alTerminaSingoloMessaggio As Action(Of MessaggioCompleto),
                                                 alTerminaTutto As Action(Of String, Integer))
        Dim contatoreSalvati As Integer = 0
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse indici Is Nothing OrElse indici.Count = 0 Then
                If alTerminaTutto IsNot Nothing Then
                    Try
                        alTerminaTutto(chiaveAccount, 0)
                    Catch
                    End Try
                End If
                Return
            End If

            Using client As Pop3Client = CreaClientPop3(impostazioni)
                ' Un solo comando UIDL per tutti gli indici richiesti, invece di uno per messaggio.
                Dim tuttiGliUid As IList(Of String) = Nothing
                Try
                    tuttiGliUid = client.GetMessageUids()
                Catch exUid As Exception
                    ScriviLog("CLSMail.ScaricaMessaggiPop3InSottofondo UIDL: " & DescriviErroreMailKit(exUid))
                End Try

                ' Calcola prima tutti gli idPop del blocco, poi fa un'unica query "IN" per sapere
                ' quali sono gia' presenti in DB, invece di una query per messaggio.
                Dim idPopPerIndice As New Dictionary(Of Integer, String)
                For Each i As Integer In indici
                    Dim idPop As String = "pop3-" & i.ToString()
                    If tuttiGliUid IsNot Nothing AndAlso i < tuttiGliUid.Count AndAlso Not String.IsNullOrWhiteSpace(tuttiGliUid(i)) Then
                        idPop = "pop3-" & tuttiGliUid(i)
                    End If
                    idPopPerIndice(i) = idPop
                Next
                Dim chiaviEsistenti As Dictionary(Of String, String) = OttieniChiaviEsistentiPop3(chiaveAccount, idPopPerIndice.Values.ToList())

                For Each i As Integer In indici
                    Try
                        If i < 0 OrElse i >= client.Count Then Continue For
                        Dim mimeMsg As MimeMessage = client.GetMessage(i)
                        Dim idPop As String = idPopPerIndice(i)

                        Dim chiaveEsistente As String = ""
                        chiaviEsistenti.TryGetValue(idPop, chiaveEsistente)

                        Dim chiave As String = SalvaMessaggioCompletoPop3(chiaveAccount, idPop, mimeMsg, chiaveEsistente)
                        If Not String.IsNullOrWhiteSpace(chiave) Then
                            contatoreSalvati += 1
                            If impostazioni.CancellaMessaggiDalServer Then
                                Try
                                    client.DeleteMessage(i)
                                Catch exDel As Exception
                                    ScriviLog("CLSMail.ScaricaMessaggiPop3 delete: " & DescriviErroreMailKit(exDel))
                                End Try
                            End If
                            If alTerminaSingoloMessaggio IsNot Nothing Then
                                Try
                                    Dim completo As MessaggioCompleto = CaricaMessaggioCompletoDaDb(chiave)
                                    If completo IsNot Nothing Then alTerminaSingoloMessaggio(completo)
                                Catch
                                End Try
                            End If
                        End If
                    Catch exMsg As Exception
                        ScriviLog("CLSMail.ScaricaMessaggiPop3InSottofondo msg: " & DescriviErroreMailKit(exMsg))
                    End Try
                Next
                client.Disconnect(True)
            End Using
        Catch ex As Exception
            ImpostaErrore("CLSMail.ScaricaMessaggiPop3InSottofondo (account " & chiaveAccount & ")", ex)
        Finally
            If alTerminaTutto IsNot Nothing Then
                Try
                    alTerminaTutto(chiaveAccount, contatoreSalvati)
                Catch
                End Try
            End If
        End Try
    End Sub

    ' Come la vecchia query "SELECT ChiaveWG ... WHERE UidServer=@u", ma per tutti gli idPop
    ' indicati in un'unica interrogazione con clausola IN, invece di una query per messaggio.
    Private Function OttieniChiaviEsistentiPop3(chiaveAccount As String, idPopList As List(Of String)) As Dictionary(Of String, String)
        Dim risultato As New Dictionary(Of String, String)
        Try
            If idPopList Is Nothing OrElse idPopList.Count = 0 Then Return risultato

            Const dimensioneBlocco As Integer = 400
            For inizioBlocco As Integer = 0 To idPopList.Count - 1 Step dimensioneBlocco
                Dim blocco As List(Of String) = idPopList.Skip(inizioBlocco).Take(dimensioneBlocco).ToList()
                Dim parametri As New Dictionary(Of String, Object)
                Dim segnaposto As New List(Of String)
                For i As Integer = 0 To blocco.Count - 1
                    Dim nomeParametro As String = "@u" & i.ToString()
                    segnaposto.Add(nomeParametro)
                    parametri(nomeParametro) = blocco(i)
                Next
                parametri("@a") = chiaveAccount

                Dim sql As String = "SELECT ChiaveWG, UidServer FROM " & NomeTabellaMessaggi &
                    " WHERE ChiaveAccount=@a AND UidServer IN (" & String.Join(",", segnaposto) & ");"

                Dim tabella As DataTable = _db.EseguiQuery(sql, parametri)
                If tabella IsNot Nothing Then
                    For Each riga As DataRow In tabella.Rows
                        risultato(riga("UidServer").ToString()) = riga("ChiaveWG").ToString()
                    Next
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniChiaviEsistentiPop3 (" & chiaveAccount & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function SalvaMessaggioCompletoPop3(chiaveAccount As String, idPop As String, mimeMsg As MimeMessage, chiaveEsistente As String) As String
        Try
            Dim uidFinto As New UniqueId(1)
            ' riusa la logica di salvataggio passando cartella INBOX e uid sintetico non usato come id primario
            Dim mittente As String = If(mimeMsg.From.Mailboxes.Any(), mimeMsg.From.Mailboxes.First().Name, "")
            Dim mittenteEmail As String = If(mimeMsg.From.Mailboxes.Any(), mimeMsg.From.Mailboxes.First().Address, "")
            Dim listaAllegati As List(Of MimeEntity) = RaccogliAllegatiMime(mimeMsg)
            Dim valori As New Dictionary(Of String, Object) From {
                {"ChiaveAccount", chiaveAccount},
                {"Cartella", "INBOX"},
                {"UidServer", idPop},
                {"MessageId", If(mimeMsg.MessageId, "")},
                {"RispondiA", IndirizziRispondiA(mimeMsg)},
                {"Riferimenti", RiferimentiDelMessaggio(mimeMsg)},
                {"ListaIndirizzo", IndirizzoDiPubblicazioneLista(mimeMsg)},
                {"ListaNome", NomeDellaLista(mimeMsg)},
                {"Mittente", mittente},
                {"MittenteEmail", mittenteEmail},
                {"Destinatari", String.Join(", ", mimeMsg.To.Mailboxes.Select(Function(m) m.Address))},
                {"DestinatariCc", String.Join(", ", mimeMsg.Cc.Mailboxes.Select(Function(m) m.Address))},
                {"DestinatariCcn", String.Join(", ", mimeMsg.Bcc.Mailboxes.Select(Function(m) m.Address))},
                {"Oggetto", If(mimeMsg.Subject, "")},
                {"CorpoTesto", EstraiCorpoTestoDaMime(mimeMsg)},
                {"CorpoHtml", EstraiHtmlConCharsetCorretto(mimeMsg)},
                {"DataMessaggio", mimeMsg.Date.ToString("yyyy-MM-dd HH:mm:ss")},
                {"Letto", 0},
                {"HaAllegati", If(listaAllegati.Count > 0, 1, 0)},
                {"Completo", 1}
            }
            Dim chiave As String
            If Not String.IsNullOrWhiteSpace(chiaveEsistente) Then
                _db.AggiornaRecord(NomeTabellaMessaggi, chiaveEsistente, valori)
                chiave = chiaveEsistente
            Else
                chiave = _db.InserisciRecord(NomeTabellaMessaggi, valori)
            End If
            If Not String.IsNullOrWhiteSpace(chiave) AndAlso listaAllegati.Count > 0 Then
                SalvaAllegatiSuDisco(chiave, listaAllegati)
            End If
            Return chiave
        Catch ex As Exception
            ImpostaErrore("CLSMail.SalvaMessaggioCompletoPop3", ex)
            Return ""
        End Try
    End Function

    ' Avvia lo scaricamento in sottofondo su TUTTI gli account attivi (o su quelli indicati) per
    ' la stessa cartella (es. "INBOX" su ogni account): restituisce subito, per ogni account, il
    ' relativo elenco rapido; il download completo prosegue in background per ciascun account.
    Public Function ScaricaTuttiGliAccount(Optional nomeCartella As String = "INBOX",
                                            Optional numeroMassimoPerAccount As Integer = 0,
                                            Optional chiaviAccount As List(Of String) = Nothing,
                                            Optional alTerminaSingoloMessaggio As Action(Of MessaggioCompleto) = Nothing,
                                            Optional alTerminaAccount As Action(Of String, Integer) = Nothing) As Dictionary(Of String, List(Of RiepilogoMessaggio))
        Dim risultato As New Dictionary(Of String, List(Of RiepilogoMessaggio))
        Try
            Dim account As List(Of ImpostazioniAccountMail)
            If chiaviAccount IsNot Nothing AndAlso chiaviAccount.Count > 0 Then
                account = chiaviAccount.Select(Function(c) OttieniAccount(c)).Where(Function(a) a IsNot Nothing).ToList()
            Else
                account = OttieniElencoAccount(True)
            End If

            For Each impostazioni As ImpostazioniAccountMail In account
                Try
                    risultato(impostazioni.Chiave) = OttieniElencoRapidoEAvviaScaricamento(
                        impostazioni.Chiave, nomeCartella, numeroMassimoPerAccount,
                        alTerminaSingoloMessaggio, alTerminaAccount)
                Catch exAccount As Exception
                    ScriviLog("CLSMail.ScaricaTuttiGliAccount account (" & impostazioni.Chiave & "): " & exAccount.ToString())
                    risultato(impostazioni.Chiave) = New List(Of RiepilogoMessaggio)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaTuttiGliAccount: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Sub ScaricaMessaggiInSottofondo(chiaveAccount As String, nomeCartella As String, elencoUid As List(Of UniqueId),
                                             alTerminaSingoloMessaggio As Action(Of MessaggioCompleto),
                                             alTerminaTutto As Action(Of String, Integer))
        Dim contatoreSalvati As Integer = 0
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return

            Using client As ImapClient = CreaClientImap(impostazioni)
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadOnly, Segnale())

                ' Una sola query per sapere quali UID sono gia' completi/gia' noti, invece di una
                ' query per messaggio (evita centinaia di round-trip verso il DB).
                Dim statoEsistenti As Dictionary(Of String, Tuple(Of String, Boolean)) =
                    OttieniStatoMessaggiEsistenti(chiaveAccount, nomeCartella, elencoUid)

                ' Un solo FETCH per tutti i flag, invece di uno per messaggio.
                Dim flagPerUid As New Dictionary(Of UInteger, Boolean)
                Try
                    Dim riepiloghiFlag As IList(Of IMessageSummary) = cartella.Fetch(elencoUid, MessageSummaryItems.UniqueId Or MessageSummaryItems.Flags)
                    If riepiloghiFlag IsNot Nothing Then
                        For Each voce As IMessageSummary In riepiloghiFlag
                            flagPerUid(voce.UniqueId.Id) = voce.Flags.GetValueOrDefault(MessageFlags.None).HasFlag(MessageFlags.Seen)
                        Next
                    End If
                Catch exFlag As Exception
                    ScriviLog("CLSMail.ScaricaMessaggiInSottofondo flag: " & DescriviErroreMailKit(exFlag))
                End Try

                For Each uid As UniqueId In elencoUid
                    Try
                        ' se e' gia' stato scaricato per intero in precedenza, non lo riscarichiamo
                        Dim chiaveEStato As Tuple(Of String, Boolean) = Nothing
                        statoEsistenti.TryGetValue(uid.Id.ToString(), chiaveEStato)
                        If chiaveEStato IsNot Nothing AndAlso chiaveEStato.Item2 Then Continue For

                        Dim chiaveEsistente As String = If(chiaveEStato IsNot Nothing, chiaveEStato.Item1, "")
                        Dim flagLetto As Boolean = False
                        flagPerUid.TryGetValue(uid.Id, flagLetto)

                        Dim mimeMsg As MimeMessage = cartella.GetMessage(uid)

                        Dim chiave As String = SalvaMessaggioCompleto(chiaveAccount, nomeCartella, uid, mimeMsg, chiaveEsistente, flagLetto)
                        contatoreSalvati += 1

                        If alTerminaSingoloMessaggio IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(chiave) Then
                            Try
                                alTerminaSingoloMessaggio(CaricaMessaggioCompletoDaDb(chiave))
                            Catch exCallback As Exception
                                ScriviLog("CLSMail.ScaricaMessaggiInSottofondo callback singolo: " & exCallback.ToString())
                            End Try
                        End If
                    Catch exMessaggio As Exception
                        ScriviLog("CLSMail.ScaricaMessaggiInSottofondo messaggio (account " & chiaveAccount & ", uid " & uid.Id.ToString() & "): " & exMessaggio.ToString())
                    End Try
                Next

                cartella.Close()
                client.Disconnect(True)
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaMessaggiInSottofondo (account " & chiaveAccount & ", " & nomeCartella & "): " & ex.ToString())
        End Try

        If alTerminaTutto IsNot Nothing Then
            Try
                alTerminaTutto(chiaveAccount, contatoreSalvati)
            Catch exCallback As Exception
                ScriviLog("CLSMail.ScaricaMessaggiInSottofondo callback finale: " & exCallback.ToString())
            End Try
        End If
    End Sub

    ' Restituisce, per gli UID indicati, ChiaveWG e stato "Completo" gia' presenti nel DB, in
    ' un'unica query con clausola IN invece di una query per ogni singolo UID.
    Private Function OttieniStatoMessaggiEsistenti(chiaveAccount As String, nomeCartella As String,
                                                    elencoUid As List(Of UniqueId)) As Dictionary(Of String, Tuple(Of String, Boolean))
        Dim risultato As New Dictionary(Of String, Tuple(Of String, Boolean))
        Try
            If elencoUid Is Nothing OrElse elencoUid.Count = 0 Then Return risultato

            Const dimensioneBlocco As Integer = 400 ' margine di sicurezza sotto il limite tipico di parametri SQLite
            For inizioBlocco As Integer = 0 To elencoUid.Count - 1 Step dimensioneBlocco
                Dim blocco As List(Of UniqueId) = elencoUid.Skip(inizioBlocco).Take(dimensioneBlocco).ToList()
                Dim parametri As New Dictionary(Of String, Object)
                Dim segnaposto As New List(Of String)
                For i As Integer = 0 To blocco.Count - 1
                    Dim nomeParametro As String = "@u" & i.ToString()
                    segnaposto.Add(nomeParametro)
                    parametri(nomeParametro) = blocco(i).Id.ToString()
                Next
                parametri("@a") = chiaveAccount
                parametri("@c") = nomeCartella

                Dim sql As String = "SELECT ChiaveWG, Completo, UidServer FROM " & NomeTabellaMessaggi &
                    " WHERE ChiaveAccount=@a AND Cartella=@c AND UidServer IN (" & String.Join(",", segnaposto) & ");"

                Dim tabella As DataTable = _db.EseguiQuery(sql, parametri)
                If tabella IsNot Nothing Then
                    For Each riga As DataRow In tabella.Rows
                        Dim uidTesto As String = riga("UidServer").ToString()
                        Dim completo As Boolean = Convert.ToInt32(riga("Completo")) = 1
                        risultato(uidTesto) = New Tuple(Of String, Boolean)(riga("ChiaveWG").ToString(), completo)
                    Next
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniStatoMessaggiEsistenti (account " & chiaveAccount & ", " & nomeCartella & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' Salva (inserimento o aggiornamento) un messaggio completo nel database e i suoi allegati su
    ' disco. Restituisce la ChiaveWG del record.
    Private Function SalvaMessaggioCompleto(chiaveAccount As String, nomeCartella As String, uid As UniqueId, mimeMsg As MimeMessage, chiaveEsistente As String, letto As Boolean) As String
        Try
            ' Se non ci hanno gia' detto quale riga aggiornare, proviamo a riconoscerla dal
            ' MessageId prima di inserirne una nuova: e' il caso della posta inviata, dove la
            ' copia locale salvata subito dopo l'invio puo' non avere ancora l'Uid reale del
            ' server (server senza UIDPLUS) e va quindi riconciliata cosi', invece che per Uid,
            ' quando arriva la copia "vera" scaricata da un giro di sincronizzazione successivo.
            If String.IsNullOrWhiteSpace(chiaveEsistente) AndAlso Not String.IsNullOrWhiteSpace(mimeMsg.MessageId) Then
                chiaveEsistente = TrovaChiaveMessaggioPerMessageId(chiaveAccount, nomeCartella, mimeMsg.MessageId)
            End If

            Dim mittente As String = If(mimeMsg.From.Mailboxes.Any(), mimeMsg.From.Mailboxes.First().Name, "")
            Dim mittenteEmail As String = If(mimeMsg.From.Mailboxes.Any(), mimeMsg.From.Mailboxes.First().Address, "")
            Dim listaAllegati As List(Of MimeEntity) = RaccogliAllegatiMime(mimeMsg)

            Dim valori As New Dictionary(Of String, Object) From {
                {"ChiaveAccount", chiaveAccount},
                {"Cartella", nomeCartella},
                {"UidServer", uid.Id.ToString()},
                {"MessageId", If(mimeMsg.MessageId, "")},
                {"RispondiA", IndirizziRispondiA(mimeMsg)},
                {"Riferimenti", RiferimentiDelMessaggio(mimeMsg)},
                {"ListaIndirizzo", IndirizzoDiPubblicazioneLista(mimeMsg)},
                {"ListaNome", NomeDellaLista(mimeMsg)},
                {"Mittente", mittente},
                {"MittenteEmail", mittenteEmail},
                {"Destinatari", String.Join(", ", mimeMsg.To.Mailboxes.Select(Function(m) m.Address))},
                {"DestinatariCc", String.Join(", ", mimeMsg.Cc.Mailboxes.Select(Function(m) m.Address))},
                {"DestinatariCcn", String.Join(", ", mimeMsg.Bcc.Mailboxes.Select(Function(m) m.Address))},
                {"Oggetto", If(mimeMsg.Subject, "")},
                {"CorpoTesto", EstraiCorpoTestoDaMime(mimeMsg)},
                {"CorpoHtml", EstraiHtmlConCharsetCorretto(mimeMsg)},
                {"DataMessaggio", mimeMsg.Date.ToString("yyyy-MM-dd HH:mm:ss")},
                {"Letto", If(letto, 1, 0)},
                {"HaAllegati", If(listaAllegati.Count > 0, 1, 0)},
                {"Completo", 1}
            }

            Dim chiave As String
            If Not String.IsNullOrWhiteSpace(chiaveEsistente) Then
                _db.AggiornaRecord(NomeTabellaMessaggi, chiaveEsistente, valori)
                chiave = chiaveEsistente
            Else
                chiave = _db.InserisciRecord(NomeTabellaMessaggi, valori)
            End If

            If Not String.IsNullOrWhiteSpace(chiave) AndAlso listaAllegati.Count > 0 Then
                SalvaAllegatiSuDisco(chiave, listaAllegati)
            End If

            If Not String.IsNullOrWhiteSpace(chiave) Then
                Try
                    Avanzata().ApplicaRegoleAlMessaggioLocale(chiave, chiaveAccount, mittente, If(mimeMsg.Subject, ""), EstraiCorpoTestoDaMime(mimeMsg), String.Join(", ", mimeMsg.To.Mailboxes.Select(Function(m) m.Address)))
                Catch exRegole As Exception
                    ScriviLog("CLSMail.SalvaMessaggioCompleto regole (" & chiave & "): " & exRegole.ToString())
                End Try
            End If

            Return chiave
        Catch ex As ParseException
            ImpostaErrore("CLSMail.SalvaMessaggioCompleto MIME (account " & chiaveAccount & ", uid " & uid.Id.ToString() & ")", ex)
            Return ""
        Catch ex As Exception
            ImpostaErrore("CLSMail.SalvaMessaggioCompleto (account " & chiaveAccount & ", " & nomeCartella & ", uid " & uid.Id.ToString() & ")", ex)
            Return ""
        End Try
    End Function

    ' Raccoglie gli allegati MimeKit: Content-Disposition:attachment, MessagePart rfc822,
    ' MimePart con FileName, e parti binarie che non sono il corpo testo/html.
    Private Function RaccogliAllegatiMime(messaggio As MimeMessage) As List(Of MimeEntity)
        Dim risultato As New List(Of MimeEntity)
        Try
            If messaggio Is Nothing Then Return risultato

            Try
                For Each allegato As MimeEntity In messaggio.Attachments
                    If allegato IsNot Nothing AndAlso Not risultato.Contains(allegato) Then risultato.Add(allegato)
                Next
            Catch exAll As Exception
                ScriviLog("CLSMail.RaccogliAllegatiMime Attachments: " & DescriviErroreMailKit(exAll))
            End Try

            Try
                For Each parte As MimeEntity In messaggio.BodyParts
                    Try
                        If parte Is Nothing Then Continue For
                        If risultato.Contains(parte) Then Continue For

                        If TypeOf parte Is MessagePart Then
                            risultato.Add(parte)
                            Continue For
                        End If

                        Dim mimeParte As MimePart = TryCast(parte, MimePart)
                        If mimeParte Is Nothing Then Continue For
                        If TypeOf mimeParte Is TextPart AndAlso Not mimeParte.IsAttachment Then Continue For
                        If mimeParte.IsAttachment OrElse Not String.IsNullOrWhiteSpace(mimeParte.FileName) Then
                            risultato.Add(mimeParte)
                        End If
                    Catch exParte As Exception
                        ScriviLog("CLSMail.RaccogliAllegatiMime parte: " & DescriviErroreMailKit(exParte))
                    End Try
                Next
            Catch exBody As Exception
                ScriviLog("CLSMail.RaccogliAllegatiMime BodyParts: " & DescriviErroreMailKit(exBody))
            End Try
        Catch ex As Exception
            ImpostaErrore("CLSMail.RaccogliAllegatiMime", ex)
        End Try
        Return risultato
    End Function

    Private Sub SalvaAllegatiSuDisco(chiaveMessaggio As String, allegati As List(Of MimeEntity))
        Try
            If allegati Is Nothing OrElse allegati.Count = 0 Then Return

            Dim giaPresenti As DataTable = _db.EseguiQuery(
                "SELECT COUNT(*) AS Totale FROM " & NomeTabellaAllegati & " WHERE ChiaveMessaggio=@c;",
                New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
            If giaPresenti.Rows.Count > 0 AndAlso Convert.ToInt32(giaPresenti.Rows(0)("Totale")) > 0 Then Return

            Dim cartellaMessaggio As String = Path.Combine(_cartellaAllegati, chiaveMessaggio)
            If Not Directory.Exists(cartellaMessaggio) Then Directory.CreateDirectory(cartellaMessaggio)

            Dim usati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each entita As MimeEntity In allegati
                Try
                    If entita Is Nothing Then Continue For
                    Dim nomeFile As String = OttieniNomeFileMime(entita)
                    nomeFile = PulisciNomeFile(nomeFile)
                    If String.IsNullOrWhiteSpace(nomeFile) Then
                        Dim estensione As String = EstensioneDaMime(If(entita.ContentType Is Nothing, "", entita.ContentType.MimeType))
                        nomeFile = "allegato_" & Guid.NewGuid().ToString("N").Substring(0, 8) & estensione
                    End If
                    nomeFile = NomeFileUnivoco(cartellaMessaggio, nomeFile, usati)
                    Dim percorsoFile As String = Path.Combine(cartellaMessaggio, nomeFile)

                    Using flusso As New FileStream(percorsoFile, FileMode.Create, FileAccess.Write, FileShare.None)
                        If TypeOf entita Is MessagePart Then
                            Dim rfc822 As MessagePart = DirectCast(entita, MessagePart)
                            If rfc822.Message IsNot Nothing Then
                                rfc822.Message.WriteTo(flusso)
                            End If
                        ElseIf TypeOf entita Is MimePart Then
                            Dim parte As MimePart = DirectCast(entita, MimePart)
                            If parte.Content IsNot Nothing Then
                                parte.Content.DecodeTo(flusso)
                            End If
                        End If
                    End Using

                    Dim dimensione As Long = 0
                    Try
                        dimensione = New FileInfo(percorsoFile).Length
                    Catch
                    End Try

                    Dim tipoMime As String = "application/octet-stream"
                    Try
                        If entita.ContentType IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entita.ContentType.MimeType) Then
                            tipoMime = entita.ContentType.MimeType
                        End If
                    Catch
                    End Try

                    Dim valori As New Dictionary(Of String, Object) From {
                        {"ChiaveMessaggio", chiaveMessaggio},
                        {"NomeFile", nomeFile},
                        {"PercorsoFile", percorsoFile},
                        {"TipoMime", tipoMime},
                        {"Dimensione", dimensione}
                    }
                    _db.InserisciRecord(NomeTabellaAllegati, valori)
                Catch exAllegato As Exception
                    ImpostaErrore("CLSMail.SalvaAllegatiSuDisco allegato (" & chiaveMessaggio & ")", exAllegato)
                End Try
            Next
        Catch ex As Exception
            ImpostaErrore("CLSMail.SalvaAllegatiSuDisco (" & chiaveMessaggio & ")", ex)
        End Try
    End Sub

    Private Function OttieniNomeFileMime(entita As MimeEntity) As String
        Try
            If entita Is Nothing Then Return ""
            Dim mimeParte As MimePart = TryCast(entita, MimePart)
            If mimeParte IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(mimeParte.FileName) Then Return mimeParte.FileName
            If entita.ContentDisposition IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entita.ContentDisposition.FileName) Then
                Return entita.ContentDisposition.FileName
            End If
            If entita.ContentType IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entita.ContentType.Name) Then
                Return entita.ContentType.Name
            End If
            If TypeOf entita Is MessagePart Then Return "messaggio.eml"
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniNomeFileMime: " & DescriviErroreMailKit(ex))
        End Try
        Return ""
    End Function

    Private Function EstensioneDaMime(tipoMime As String) As String
        Try
            If String.IsNullOrWhiteSpace(tipoMime) Then Return ""
            Select Case tipoMime.Trim().ToLowerInvariant()
                Case "message/rfc822" : Return ".eml"
                Case "application/pdf" : Return ".pdf"
                Case "image/jpeg" : Return ".jpg"
                Case "image/png" : Return ".png"
                Case "image/gif" : Return ".gif"
                Case "audio/mpeg" : Return ".mp3"
                Case "video/mp4" : Return ".mp4"
                Case "application/zip" : Return ".zip"
                Case "application/vnd.ms-outlook" : Return ".msg"
                Case Else : Return ""
            End Select
        Catch
            Return ""
        End Try
    End Function

    Private Function NomeFileUnivoco(cartella As String, nomeFile As String, usati As HashSet(Of String)) As String
        Try
            Dim candidato As String = nomeFile
            Dim nome As String = Path.GetFileNameWithoutExtension(nomeFile)
            Dim estensione As String = Path.GetExtension(nomeFile)
            Dim indice As Integer = 2
            While usati.Contains(candidato) OrElse File.Exists(Path.Combine(cartella, candidato))
                candidato = nome & "_" & indice.ToString() & estensione
                indice += 1
            End While
            usati.Add(candidato)
            Return candidato
        Catch
            Return nomeFile
        End Try
    End Function

    Private Function PulisciNomeFile(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim nomePulito As String = nome
            For Each carattereNonValido As Char In Path.GetInvalidFileNameChars()
                nomePulito = nomePulito.Replace(carattereNonValido, "_"c)
            Next
            Return nomePulito
        Catch ex As Exception
            ScriviLog("CLSMail.PulisciNomeFile: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' ============================================================================================
    ' LETTURA DI UN MESSAGGIO - scarica se serve e segna come letto sul server dell'account giusto
    ' ============================================================================================

    Public Function ApriMessaggio(chiaveAccount As String, nomeCartella As String, idMessaggio As String) As MessaggioCompleto
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return Nothing
            End If

            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c AND UidServer=@u LIMIT 1;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}, {"@u", idMessaggio}})

            Dim chiave As String = ""
            Dim giaCompleto As Boolean = False
            Dim giaLetto As Boolean = False

            If tabella.Rows.Count > 0 Then
                Dim riga As DataRow = tabella.Rows(0)
                chiave = riga("ChiaveWG").ToString()
                giaCompleto = Convert.ToInt32(riga("Completo")) = 1
                giaLetto = Convert.ToInt32(riga("Letto")) = 1
            End If

            ' POP3: il messaggio non ha un UID IMAP, si legge da quanto gia' scaricato in locale
            If UsaPop3(impostazioni) OrElse IdMessaggioPop3(idMessaggio) Then
                If String.IsNullOrWhiteSpace(chiave) Then
                    _ultimoErrore = "Messaggio non ancora scaricato. Riprova tra qualche istante."
                    Return Nothing
                End If
                If Not giaLetto Then
                    _db.AggiornaRecord(NomeTabellaMessaggi, chiave, New Dictionary(Of String, Object) From {{"Letto", 1}})
                End If
                Return CaricaMessaggioCompletoDaDb(chiave)
            End If

            ' Se il messaggio e' gia' stato scaricato per intero ED e' gia' segnato come letto,
            ' tutto quello che serve e' gia' nel database locale: nessuna connessione al server e'
            ' necessaria. Prima di questa modifica si riapriva sempre una connessione IMAP
            ' completa (handshake TLS + login + apertura cartella) anche solo per rileggere un
            ' messaggio gia' consultato, ed era la causa principale della lentezza percepita.
            If Not String.IsNullOrWhiteSpace(chiave) AndAlso giaCompleto AndAlso giaLetto Then
                Return CaricaMessaggioCompletoDaDb(chiave)
            End If

            ' Connessione presa dalla cache invece di aprirne sempre una nuova (vedi
            ' OttieniClientImapDaCache): stesso beneficio di velocita' di OttieniPaginaMessaggi.
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadWrite, Segnale())
                Dim uid As New UniqueId(UInteger.Parse(idMessaggio))

                If Not giaCompleto Then
                    Dim mimeMsg As MimeMessage = cartella.GetMessage(uid)
                    chiave = SalvaMessaggioCompleto(chiaveAccount, nomeCartella, uid, mimeMsg, chiave, giaLetto)
                End If

                If Not giaLetto Then
                    cartella.Store(uid, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) With {.Silent = True})
                    If Not String.IsNullOrWhiteSpace(chiave) Then
                        _db.AggiornaRecord(NomeTabellaMessaggi, chiave, New Dictionary(Of String, Object) From {{"Letto", 1}})
                    End If
                End If

                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            Return CaricaMessaggioCompletoDaDb(chiave)
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.ApriMessaggio autenticazione (account " & chiaveAccount & ")", ex)
            Return Nothing
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.ApriMessaggio SSL (account " & chiaveAccount & ")", ex)
            Return Nothing
        Catch ex As MessageNotFoundException
            ImpostaErrore("CLSMail.ApriMessaggio (account " & chiaveAccount & ", " & idMessaggio & ")", ex)
            Return Nothing
        Catch ex As FolderNotFoundException
            ImpostaErrore("CLSMail.ApriMessaggio cartella (account " & chiaveAccount & ", " & nomeCartella & ")", ex)
            Return Nothing
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.ApriMessaggio IMAP (account " & chiaveAccount & ")", ex)
            Return Nothing
        Catch ex As ImapProtocolException
            ImpostaErrore("CLSMail.ApriMessaggio IMAP (account " & chiaveAccount & ")", ex)
            Return Nothing
        Catch ex As ParseException
            ImpostaErrore("CLSMail.ApriMessaggio MIME (account " & chiaveAccount & ")", ex)
            Return Nothing
        Catch ex As Exception
            ImpostaErrore("CLSMail.ApriMessaggio (account " & chiaveAccount & ", " & nomeCartella & ", " & idMessaggio & ")", ex)
            Return Nothing
        End Try
    End Function

    ' Aggancia la risposta alla conversazione a cui appartiene. Senza queste due intestazioni
    ' le liste di discussione e gli archivi trattano la risposta come un messaggio nuovo, e
    ' chi legge non capisce a che cosa si riferisca.
    Private Sub AgganciaAllaConversazione(messaggio As MimeMessage, inRispostaA As String, riferimenti As String)
        Try
            If messaggio Is Nothing Then Return

            Dim identificativo As String = RipulisciIdentificativo(inRispostaA)
            If Not String.IsNullOrWhiteSpace(identificativo) Then
                Try
                    messaggio.InReplyTo = identificativo
                Catch exRisposta As Exception
                    ScriviLog("CLSMail.AgganciaAllaConversazione In-Reply-To: " & exRisposta.Message)
                End Try
            End If

            Dim catena As New List(Of String)
            If Not String.IsNullOrWhiteSpace(riferimenti) Then
                For Each pezzo As String In riferimenti.Split(New Char() {" "c, ","c, ";"c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                    Dim ripulito As String = RipulisciIdentificativo(pezzo)
                    If Not String.IsNullOrWhiteSpace(ripulito) AndAlso Not catena.Contains(ripulito) Then catena.Add(ripulito)
                Next
            End If
            If Not String.IsNullOrWhiteSpace(identificativo) AndAlso Not catena.Contains(identificativo) Then catena.Add(identificativo)

            For Each riferimento As String In catena
                Try
                    messaggio.References.Add(riferimento)
                Catch exRiferimento As Exception
                    ScriviLog("CLSMail.AgganciaAllaConversazione riferimento: " & exRiferimento.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.AgganciaAllaConversazione: " & ex.ToString())
        End Try
    End Sub

    Private Function RipulisciIdentificativo(valore As String) As String
        Try
            Dim testo As String = If(valore, "").Trim()
            If testo.StartsWith("<") Then testo = testo.Substring(1)
            If testo.EndsWith(">") Then testo = testo.Substring(0, testo.Length - 1)
            testo = testo.Trim()
            If Not testo.Contains("@") Then Return ""
            Return testo
        Catch ex As Exception
            ScriviLog("CLSMail.RipulisciIdentificativo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IndirizziRispondiA(mimeMsg As MimeMessage) As String
        Try
            If mimeMsg Is Nothing OrElse mimeMsg.ReplyTo Is Nothing Then Return ""
            Return String.Join(", ", mimeMsg.ReplyTo.Mailboxes.Select(Function(m) m.Address))
        Catch ex As Exception
            ScriviLog("CLSMail.IndirizziRispondiA: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RiferimentiDelMessaggio(mimeMsg As MimeMessage) As String
        Try
            If mimeMsg Is Nothing Then Return ""
            Dim elenco As New List(Of String)
            If mimeMsg.References IsNot Nothing Then
                For Each riferimento As String In mimeMsg.References
                    If Not String.IsNullOrWhiteSpace(riferimento) Then elenco.Add(riferimento)
                Next
            End If
            Return String.Join(" ", elenco.ToArray())
        Catch ex As Exception
            ScriviLog("CLSMail.RiferimentiDelMessaggio: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IntestazioneGrezza(mimeMsg As MimeMessage, nome As String) As String
        Try
            If mimeMsg Is Nothing OrElse mimeMsg.Headers Is Nothing Then Return ""
            Dim valore As String = mimeMsg.Headers(nome)
            Return If(valore, "")
        Catch ex As Exception
            ScriviLog("CLSMail.IntestazioneGrezza (" & If(nome, "") & "): " & ex.Message)
            Return ""
        End Try
    End Function

    ' Indirizzo a cui si scrive per pubblicare sulla lista. Lo standard e' l'intestazione
    ' List-Post, che contiene un mailto fra parentesi angolari. Non tutti i gestori di liste
    ' la mettono, quindi si guardano anche le intestazioni piu' vecchie usate da ezmlm,
    ' Mailman e dalle prime versioni di Google Groups.
    Private Function IndirizzoDiPubblicazioneLista(mimeMsg As MimeMessage) As String
        Try
            Dim valore As String = IntestazioneGrezza(mimeMsg, "List-Post")
            If valore.Trim().Equals("NO", StringComparison.OrdinalIgnoreCase) Then Return ""
            Dim indirizzo As String = PrimoMailtoNelTesto(valore)
            If Not String.IsNullOrWhiteSpace(indirizzo) Then Return indirizzo

            For Each nome As String In New String() {"Mailing-List", "X-Mailing-List", "X-Loop", "X-BeenThere"}
                Dim testo As String = IntestazioneGrezza(mimeMsg, nome)
                If String.IsNullOrWhiteSpace(testo) Then Continue For
                indirizzo = PrimoMailtoNelTesto(testo)
                If Not String.IsNullOrWhiteSpace(indirizzo) Then Return indirizzo
                indirizzo = PrimoIndirizzoNelTesto(testo)
                If Not String.IsNullOrWhiteSpace(indirizzo) Then Return indirizzo
            Next

            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.IndirizzoDiPubblicazioneLista: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function PrimoMailtoNelTesto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim corrispondenza As Match = Regex.Match(testo, "mailto:\s*([^>\s,;""'()]+@[^>\s,;""'()]+)", RegexOptions.IgnoreCase)
            If corrispondenza.Success Then Return corrispondenza.Groups(1).Value.Trim()
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.PrimoMailtoNelTesto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function PrimoIndirizzoNelTesto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim corrispondenza As Match = Regex.Match(testo, "[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")
            If corrispondenza.Success Then Return corrispondenza.Value.Trim()
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.PrimoIndirizzoNelTesto: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Nome leggibile della lista. List-Id ha la forma: descrizione fra virgolette seguita
    ' dall'identificativo fra parentesi angolari. Se la descrizione manca si usa la prima
    ' parte dell'identificativo.
    Private Function NomeDellaLista(mimeMsg As MimeMessage) As String
        Try
            Dim listId As String = IntestazioneGrezza(mimeMsg, "List-Id").Trim()
            If Not String.IsNullOrWhiteSpace(listId) Then
                Dim identificativo As String = ""
                Dim corrispondenza As Match = Regex.Match(listId, "<([^>]+)>")
                If corrispondenza.Success Then identificativo = corrispondenza.Groups(1).Value.Trim()

                Dim descrizione As String = listId
                If corrispondenza.Success Then descrizione = listId.Substring(0, corrispondenza.Index)
                descrizione = descrizione.Replace(Chr(34), " ").Trim()

                If Not String.IsNullOrWhiteSpace(descrizione) Then Return descrizione
                If Not String.IsNullOrWhiteSpace(identificativo) Then
                    Dim punto As Integer = identificativo.IndexOf("."c)
                    If punto > 0 Then Return identificativo.Substring(0, punto)
                    Return identificativo
                End If
            End If

            Dim indirizzo As String = IndirizzoDiPubblicazioneLista(mimeMsg)
            If Not String.IsNullOrWhiteSpace(indirizzo) AndAlso indirizzo.Contains("@") Then
                Return indirizzo.Substring(0, indirizzo.IndexOf("@"c))
            End If

            If Not String.IsNullOrWhiteSpace(IntestazioneGrezza(mimeMsg, "List-Unsubscribe")) Then Return "lista di discussione"
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.NomeDellaLista: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function CaricaMessaggioCompletoDaDb(chiave As String) As MessaggioCompleto
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaMessaggi, chiave)
            If riga Is Nothing Then Return Nothing

            Dim messaggio As New MessaggioCompleto() With {
                .Chiave = chiave,
                .ChiaveAccount = riga("ChiaveAccount").ToString(),
                .Id = riga("UidServer").ToString(),
                .Cartella = riga("Cartella").ToString(),
                .Mittente = riga("Mittente").ToString(),
                .MittenteEmail = riga("MittenteEmail").ToString(),
                .Destinatari = riga("Destinatari").ToString(),
                .Cc = riga("DestinatariCc").ToString(),
                .Ccn = riga("DestinatariCcn").ToString(),
                .Oggetto = riga("Oggetto").ToString(),
                .CorpoTesto = riga("CorpoTesto").ToString(),
                .CorpoHtml = riga("CorpoHtml").ToString(),
                .Letto = Convert.ToInt32(riga("Letto")) = 1,
                .Identificativo = ValoreColonna(riga, "MessageId"),
                .RispondiA = ValoreColonna(riga, "RispondiA"),
                .Riferimenti = ValoreColonna(riga, "Riferimenti"),
                .IndirizzoLista = ValoreColonna(riga, "ListaIndirizzo"),
                .NomeLista = ValoreColonna(riga, "ListaNome")
            }
            If String.IsNullOrWhiteSpace(messaggio.CorpoTesto) AndAlso Not String.IsNullOrWhiteSpace(messaggio.CorpoHtml) Then
                messaggio.CorpoTesto = HtmlVersoTesto(messaggio.CorpoHtml)
            End If

            Dim dataTesto As String = riga("DataMessaggio").ToString()
            Dim dataConvertita As DateTimeOffset
            If DateTimeOffset.TryParse(dataTesto, dataConvertita) Then messaggio.Data = dataConvertita

            Dim allegati As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaAllegati & " WHERE ChiaveMessaggio=@c;",
                New Dictionary(Of String, Object) From {{"@c", chiave}})
            For Each rigaAllegato As DataRow In allegati.Rows
                messaggio.Allegati.Add(New AllegatoMessaggio() With {
                    .Nome = rigaAllegato("NomeFile").ToString(),
                    .Percorso = rigaAllegato("PercorsoFile").ToString(),
                    .TipoMime = rigaAllegato("TipoMime").ToString(),
                    .Dimensione = Convert.ToInt64(rigaAllegato("Dimensione"))
                })
            Next

            Return messaggio
        Catch ex As Exception
            ScriviLog("CLSMail.CaricaMessaggioCompletoDaDb (" & chiave & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Preferisce il testo puro; se manca, converte l'HTML in testo leggibile per WGCampo.
    Private Function EstraiCorpoTestoDaMime(mimeMsg As MimeMessage) As String
        Try
            If mimeMsg Is Nothing Then Return ""
            Dim testo As String = If(mimeMsg.TextBody, "")
            If Not String.IsNullOrWhiteSpace(testo) Then Return testo
            Dim html As String = EstraiHtmlConCharsetCorretto(mimeMsg)
            If Not String.IsNullOrWhiteSpace(html) Then Return HtmlVersoTesto(html)
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiCorpoTestoDaMime: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' ============================================================================================
    ' CHARSET DEL CORPO HTML: correzione per i messaggi in cui l'header MIME (Content-Type:
    ' charset=...) mente rispetto alla codifica reale del contenuto.
    '
    ' Caso concreto che ha fatto scattare questa funzione: mail transazionali generate con MJML
    ' (tra cui quelle di Anthropic) che arrivano con "Content-Type: text/html; charset=iso-8859-1"
    ' nell'header, ma il cui <meta charset> interno dichiara (correttamente) UTF-8: il corpo e'
    ' realmente UTF-8, l'header e' semplicemente sbagliato. mimeMsg.HtmlBody di MimeKit si fida
    ' del charset dichiarato nell'header, quindi in questi casi restituisce testo "mojibake"
    ' (es. "Ã¨" al posto di "è") per ogni carattere accentato o comunque non-ASCII.
    '
    ' La correzione: si legge il corpo HTML gia' decodificato dal quoted-printable/base64 (MA
    ' ancora come sequenza di byte, non ancora convertito in stringa), si cerca un <meta charset>
    ' o <meta http-equiv="Content-Type" content="...charset=..."> nei primi byte (sempre in ASCII
    ' puro, quindi leggibili con qualunque codifica a 1 byte senza rischi) e, se e' diverso da
    ' quello dichiarato nell'header, si usa quello del meta tag. Come ulteriore rete di sicurezza,
    ' se anche cosi' il risultato assomiglia a UTF-8 letto con una codifica a 1 byte, si ritenta.
    ' ============================================================================================

    Private Function EstraiHtmlConCharsetCorretto(mimeMsg As MimeMessage) As String
        Try
            If mimeMsg Is Nothing Then Return ""

            Dim parteHtml As TextPart = TryCast(
                mimeMsg.BodyParts.FirstOrDefault(Function(p) TypeOf p Is TextPart AndAlso DirectCast(p, TextPart).IsHtml),
                TextPart)
            If parteHtml Is Nothing Then Return If(mimeMsg.HtmlBody, "")

            Using ms As New MemoryStream()
                ' Toglie quoted-printable/base64 (Content-Transfer-Encoding), NON tocca il charset:
                ' qui dentro abbiamo i byte "veri" del documento HTML, cosi' come li ha scritti chi
                ' ha generato la mail.
                parteHtml.Content.DecodeTo(ms)
                Dim byteGrezzi As Byte() = ms.ToArray()
                If byteGrezzi.Length = 0 Then Return If(mimeMsg.HtmlBody, "")

                Dim charsetHeader As String = parteHtml.ContentType.Charset
                Dim charsetMeta As String = EstraiCharsetDaMetaTag(byteGrezzi)

                Dim charsetDaUsare As String = charsetHeader
                If Not String.IsNullOrWhiteSpace(charsetMeta) AndAlso
                   Not String.Equals(charsetMeta, charsetHeader, StringComparison.OrdinalIgnoreCase) Then
                    charsetDaUsare = charsetMeta
                    ScriviLog("CLSMail.EstraiHtmlConCharsetCorretto: header dichiara '" & If(charsetHeader, "") &
                              "' ma il meta tag dichiara '" & charsetMeta & "'; uso quest'ultimo.")
                End If

                Dim codifica As Encoding
                Try
                    codifica = Encoding.GetEncoding(If(String.IsNullOrWhiteSpace(charsetDaUsare), "utf-8", charsetDaUsare))
                Catch
                    codifica = Encoding.UTF8
                End Try

                Dim testo As String = codifica.GetString(byteGrezzi)

                ' Rete di sicurezza: nessun meta tag trovato (o charset dichiarato ovunque coerente,
                ' ma comunque sbagliato) e il risultato mostra i segni tipici di UTF-8 letto con una
                ' codifica a 1 byte (Latin-1/CP1252/ISO-8859-*): ritenta come UTF-8.
                If SembraMojibakeUtf8(testo) Then
                    Try
                        Dim byteRiletti As Byte() = codifica.GetBytes(testo)
                        Dim testoCorretto As String = Encoding.UTF8.GetString(byteRiletti)
                        If Not SembraMojibakeUtf8(testoCorretto) Then testo = testoCorretto
                    Catch
                        ' Se la ri-decodifica fallisce, si tiene il risultato precedente.
                    End Try
                End If

                Return testo
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiHtmlConCharsetCorretto: " & ex.ToString())
            Return If(mimeMsg?.HtmlBody, "")
        End Try
    End Function

    ' Cerca "charset=..." dentro un <meta> del documento HTML, leggendo i primi byte come
    ' ISO-8859-1: i meta tag sono sempre puro ASCII, quindi questa lettura e' sicura qualunque sia
    ' la codifica reale del resto del documento.
    Private Function EstraiCharsetDaMetaTag(byteGrezzi As Byte()) As String
        Try
            If byteGrezzi Is Nothing OrElse byteGrezzi.Length = 0 Then Return Nothing
            Dim inizio As Byte() = byteGrezzi.Take(Math.Min(byteGrezzi.Length, 4096)).ToArray()
            Dim testoAscii As String = Encoding.GetEncoding("ISO-8859-1").GetString(inizio)
            Dim m As Match = Regex.Match(testoAscii, "charset\s*=\s*[""']?([\w\-]+)", RegexOptions.IgnoreCase)
            Return If(m.Success, m.Groups(1).Value, Nothing)
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiCharsetDaMetaTag: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Riconosce i segni tipici di un testo UTF-8 decodificato per errore con una codifica a 1
    ' byte (Latin-1/CP1252/ISO-8859-*): lettere accentate italiane trasformate in "Ã¨", "Ã²" ecc.,
    ' virgolette/apostrofi tipografici trasformati in "â€™", "â€œ" ecc.
    Private Function SembraMojibakeUtf8(testo As String) As Boolean
        If String.IsNullOrEmpty(testo) Then
            Return False
        End If

        Dim sequenze() As String = {
        "Ã ", "Ã¨", "Ã©", "Ã¬", "Ã²", "Ã¹", "Ã¡", "Ã«", "Ã¯",
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H2122),
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H201C),
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H201D),
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H9D),
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H2013),
        ChrW(&HE2) & ChrW(&H20AC) & ChrW(&H2014),
        "Â "
    }

        For Each sequenza As String In sequenze
            If testo.Contains(sequenza) Then
                Return True
            End If
        Next

        Return False
    End Function
    Public Shared Function CorpoLeggibile(testo As String, html As String) As String
        If Not String.IsNullOrWhiteSpace(testo) Then Return testo
        Return HtmlVersoTesto(html)
    End Function

    ' ============================================================================================
    ' ESPORTAZIONE IN .EML
    '
    ' Due livelli, in ordine di preferenza:
    ' 1) Bytes grezzi originali presi direttamente dal server IMAP (IMailFolder.GetStream): e'
    '    l'unico modo per ottenere un .eml identico byte per byte all'originale, con lo stesso
    '    charset dichiarato (giusto o sbagliato che sia) e la stessa formattazione MIME. Non
    '    disponibile per gli account POP3 (il messaggio, una volta scaricato, non e' piu'
    '    rifetchabile "grezzo" dal server con la stessa affidabilita').
    ' 2) Se il punto 1 non e' disponibile (POP3, niente connessione, messaggio non piu' sul
    '    server...): ricostruzione di un .eml valido a partire da quanto gia' salvato in locale
    '    (mittente, destinatari, oggetto, corpo, allegati). Non e' identico bit a bit
    '    all'originale, ma si apre correttamente in qualunque client di posta.
    ' ============================================================================================

    ''' <summary>
    ''' Esporta un messaggio gia' caricato (MessaggioCompleto) come file .eml nel percorso indicato.
    ''' Ritorna True se il file e' stato scritto; in caso di errore, il dettaglio e' disponibile
    ''' tramite la proprieta' UltimoErrore.
    ''' </summary>
    Public Function EsportaMessaggioComeEml(messaggio As MessaggioCompleto, percorsoFileDestinazione As String) As Boolean
        _ultimoErrore = ""
        Try
            If messaggio Is Nothing Then
                _ultimoErrore = "Nessun messaggio da esportare."
                Return False
            End If
            If String.IsNullOrWhiteSpace(percorsoFileDestinazione) Then
                _ultimoErrore = "Percorso di destinazione non valido."
                Return False
            End If

            Dim byteGrezzi As Byte() = OttieniByteMessaggioGrezzoDalServer(messaggio)
            If byteGrezzi IsNot Nothing AndAlso byteGrezzi.Length > 0 Then
                File.WriteAllBytes(percorsoFileDestinazione, byteGrezzi)
                Return True
            End If

            Dim mimeRicostruito As MimeMessage = RicostruisciMimeDaMessaggioCompleto(messaggio)
            If mimeRicostruito Is Nothing Then
                If String.IsNullOrWhiteSpace(_ultimoErrore) Then _ultimoErrore = "Impossibile ricostruire il messaggio per l'esportazione."
            Return False
            End If

            Using flusso As FileStream = File.Create(percorsoFileDestinazione)
                mimeRicostruito.WriteTo(flusso)
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.EsportaMessaggioComeEml: " & ex.ToString())
            _ultimoErrore = "Errore durante l'esportazione: " & ex.Message
            Return False
        End Try
    End Function

    ' Tenta di riottenere dal server IMAP i byte grezzi originali del messaggio (BODY[] via
    ' IMailFolder.GetStream). Ritorna Nothing se l'account e' POP3, se manca la connessione, o se
    ' qualunque cosa va storta: in quel caso EsportaMessaggioComeEml passa alla ricostruzione.
    Private Function OttieniByteMessaggioGrezzoDalServer(messaggio As MessaggioCompleto) As Byte()
        Try
            If messaggio Is Nothing OrElse String.IsNullOrWhiteSpace(messaggio.ChiaveAccount) OrElse
               String.IsNullOrWhiteSpace(messaggio.Id) Then Return Nothing

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(messaggio.ChiaveAccount)
            If impostazioni Is Nothing Then Return Nothing
            If UsaPop3(impostazioni) Then Return Nothing

            Dim numero As UInteger
            If Not UInteger.TryParse(messaggio.Id, numero) Then Return Nothing
            Dim uid As New UniqueId(numero)

            Dim client As ImapClient = OttieniClientImapDaCache(messaggio.ChiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = RisolviCartellaPerPercorso(client, messaggio.Cartella)
                If cartella Is Nothing Then Return Nothing

                cartella.Open(FolderAccess.ReadOnly, Segnale())
                Try
                    Using origine As Stream = cartella.GetStream(uid, Segnale())
                        If origine Is Nothing Then Return Nothing
                        Using destinazione As New MemoryStream()
                            origine.CopyTo(destinazione)
                            Return destinazione.ToArray()
                        End Using
                    End Using
                Finally
                    cartella.Close()
                End Try
            Finally
                RestituisciClientImapAllaCache(messaggio.ChiaveAccount, client)
            End Try
        Catch ex As Exception
            ' Non e' un errore da mostrare all'utente: e' normale che il ri-fetch grezzo possa
            ' fallire (offline, messaggio spostato/cancellato nel frattempo, ecc.); si passa
            ' semplicemente alla ricostruzione in locale.
            ScriviLog("CLSMail.OttieniByteMessaggioGrezzoDalServer (" & If(messaggio?.Id, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Ricostruisce un MimeMessage valido a partire da un MessaggioCompleto gia' salvato in locale
    ' (usato quando non e' possibile riottenere i byte grezzi dal server: vedi
    ' OttieniByteMessaggioGrezzoDalServer).
    Private Function RicostruisciMimeDaMessaggioCompleto(messaggio As MessaggioCompleto) As MimeMessage
        Try
            Dim mimeMsg As New MimeMessage()

            If Not String.IsNullOrWhiteSpace(messaggio.MittenteEmail) Then
                Try
                    mimeMsg.From.Add(New MailboxAddress(If(messaggio.Mittente, ""), messaggio.MittenteEmail))
                Catch
                End Try
            End If

            AggiungiIndirizziAllaLista(mimeMsg.To, messaggio.Destinatari)
            AggiungiIndirizziAllaLista(mimeMsg.Cc, messaggio.Cc)
            AggiungiIndirizziAllaLista(mimeMsg.Bcc, messaggio.Ccn)

            mimeMsg.Subject = If(messaggio.Oggetto, "")
            If messaggio.Data <> DateTimeOffset.MinValue Then mimeMsg.Date = messaggio.Data

            If Not String.IsNullOrWhiteSpace(messaggio.Identificativo) Then
                Try
                    mimeMsg.MessageId = messaggio.Identificativo.Trim("<"c, ">"c)
                Catch
                    ' Message-Id non in formato valido: lo lasciamo generare da MimeKit.
                End Try
            End If

            Dim builder As New BodyBuilder()
            If Not String.IsNullOrWhiteSpace(messaggio.CorpoHtml) Then builder.HtmlBody = messaggio.CorpoHtml
            If Not String.IsNullOrWhiteSpace(messaggio.CorpoTesto) Then builder.TextBody = messaggio.CorpoTesto
            If String.IsNullOrWhiteSpace(builder.HtmlBody) AndAlso String.IsNullOrWhiteSpace(builder.TextBody) Then
                builder.TextBody = ""
            End If

            If messaggio.Allegati IsNot Nothing Then
                For Each allegato As AllegatoMessaggio In messaggio.Allegati
                    Try
                        If String.IsNullOrWhiteSpace(allegato.Percorso) OrElse Not File.Exists(allegato.Percorso) Then Continue For
                        Dim tipo As ContentType
                        Try
                            tipo = ContentType.Parse(allegato.TipoMime)
                        Catch
                            tipo = New ContentType("application", "octet-stream")
                        End Try
                        builder.Attachments.Add(allegato.Nome, File.ReadAllBytes(allegato.Percorso), tipo)
                    Catch exAllegato As Exception
                        ScriviLog("CLSMail.RicostruisciMimeDaMessaggioCompleto allegato (" & If(allegato.Nome, "") & "): " & exAllegato.ToString())
                    End Try
                Next
            End If

            mimeMsg.Body = builder.ToMessageBody()

            ' Il corpo ricostruito e' sempre marcato UTF-8 (stessa logica di ForzaCharsetUtf8 usata
            ' in fase di invio): cosi' il .eml ricostruito non eredita eventuali problemi di
            ' charset del messaggio originale.
            ForzaCharsetUtf8(mimeMsg.Body)

            Return mimeMsg
        Catch ex As Exception
            ScriviLog("CLSMail.RicostruisciMimeDaMessaggioCompleto: " & ex.ToString())
            _ultimoErrore = "Errore nella ricostruzione del messaggio: " & ex.Message
            Return Nothing
        End Try
    End Function

    ' Aggiunge a una InternetAddressList gli indirizzi contenuti in una stringa CSV (come quelle
    ' salvate in Destinatari/Cc/Ccn: semplici indirizzi separati da virgola, senza nome
    ' visualizzato). Indirizzi non validi vengono scartati singolarmente senza interrompere gli
    ' altri.
    Private Sub AggiungiIndirizziAllaLista(destinazione As InternetAddressList, valoreCsv As String)
        Try
            If destinazione Is Nothing OrElse String.IsNullOrWhiteSpace(valoreCsv) Then Return
            For Each pezzo As String In valoreCsv.Split(","c, ";"c)
                Dim indirizzo As String = pezzo.Trim()
                If String.IsNullOrWhiteSpace(indirizzo) Then Continue For
                Try
                    destinazione.Add(New MailboxAddress("", indirizzo))
                Catch exIndirizzo As Exception
                    ScriviLog("CLSMail.AggiungiIndirizziAllaLista (" & indirizzo & "): " & exIndirizzo.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.AggiungiIndirizziAllaLista: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function HtmlVersoTesto(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""
            Dim t As String = html
            t = Regex.Replace(t, "(?is)<(script|style)[^>]*>.*?</\1>", " ")
            t = Regex.Replace(t, "(?i)<br\s*/?>", Environment.NewLine)
            t = Regex.Replace(t, "(?i)</\s*p\s*>", Environment.NewLine & Environment.NewLine)
            t = Regex.Replace(t, "(?i)</\s*div\s*>", Environment.NewLine)
            t = Regex.Replace(t, "(?i)</\s*tr\s*>", Environment.NewLine)
            t = Regex.Replace(t, "(?i)</\s*li\s*>", Environment.NewLine)
            t = Regex.Replace(t, "(?i)<hr\s*/?>", Environment.NewLine & "-----" & Environment.NewLine)
            t = Regex.Replace(t, "(?is)<[^>]+>", " ")
            t = WebUtility.HtmlDecode(t)
            t = t.Replace(ChrW(160), " "c)
            t = Regex.Replace(t, "[ \t\f\v]+", " ")
            t = Regex.Replace(t, " *(\r?\n) *", "$1")
            t = Regex.Replace(t, "(\r?\n){3,}", Environment.NewLine & Environment.NewLine)
            Return t.Trim()
        Catch
            Return If(html, "")
        End Try
    End Function

    ' Elenco dei messaggi gia' presenti localmente per una cartella di un account (utile per
    ' popolare una lista senza tornare sul server ogni volta).
    Public Function OttieniMessaggiSalvati(chiaveAccount As String, nomeCartella As String) As DataTable
        Try
            Return _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c ORDER BY DataMessaggio DESC;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}})
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniMessaggiSalvati (account " & chiaveAccount & ", " & nomeCartella & "): " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    ' Come sopra ma su tutti gli account insieme, per una vista aggregata tipo "posta in arrivo
    ' unificata" (filtrando ad esempio su Cartella='INBOX').
    Public Function OttieniMessaggiSalvatiTuttiGliAccount(Optional nomeCartella As String = "INBOX") As DataTable
        Try
            Return _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaMessaggi & " WHERE Cartella=@c ORDER BY DataMessaggio DESC;",
                New Dictionary(Of String, Object) From {{"@c", nomeCartella}})
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniMessaggiSalvatiTuttiGliAccount (" & nomeCartella & "): " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    ' ============================================================================================
    ' SINCRONIZZAZIONE AGENDA (CalDAV) E RUBRICA (CardDAV) - per un singolo account
    ' ============================================================================================

    ' Sincronizza la rubrica dell'account indicato: legge i vCard dal server CardDAV e li
    ' importa/aggiorna nella rubrica gestita da CLSContatti, marcandoli con l'account di origine.
    ' Restituisce il numero di contatti importati/aggiornati.
    Public Function SincronizzaRubrica(chiaveAccount As String, contatti As CLSContatti) As Integer
        Dim contatore As Integer = 0
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) OrElse contatti Is Nothing Then Return 0

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim urlRubrica As String = RisolviCollezioneDav(impostazioni.UrlRubricaCardDav, utente, password,
                                                              "urn:ietf:params:xml:ns:carddav", "addressbook-home-set", "addressbook")
            If String.IsNullOrWhiteSpace(urlRubrica) Then Return 0

            Dim listaVCard As List(Of KeyValuePair(Of String, String)) = ScaricaVCardConHref(urlRubrica, utente, password)
            If listaVCard.Count = 0 Then
                _ultimoErrore = "Nessun contatto restituito dal server (" & urlRubrica & ")."
            End If

            For Each coppia As KeyValuePair(Of String, String) In listaVCard
                Try
                    Dim campi As Dictionary(Of String, String) = AnalizzaVCard(coppia.Value)
                    If campi Is Nothing OrElse Not campi.ContainsKey("UID") Then Continue For

                    Dim urlContatto As String = ""
                    If Not String.IsNullOrWhiteSpace(coppia.Key) Then urlContatto = RisolviUrlRelativo(urlRubrica, coppia.Key)

                    Dim listaEmail As New List(Of String)
                    For Each proprieta As KeyValuePair(Of String, String) In EstraiProprietaMultipleVCard(coppia.Value, "EMAIL")
                        Dim indirizzo As String = PrimoValoreVCard(proprieta.Value)
                        If Not String.IsNullOrWhiteSpace(indirizzo) AndAlso Not listaEmail.Contains(indirizzo, StringComparer.OrdinalIgnoreCase) Then listaEmail.Add(indirizzo)
                    Next

                    Dim listaTelefono As New List(Of String)
                    Dim listaCellulare As New List(Of String)
                    For Each proprieta As KeyValuePair(Of String, String) In EstraiProprietaMultipleVCard(coppia.Value, "TEL")
                        Dim numero As String = PrimoValoreVCard(proprieta.Value)
                        If String.IsNullOrWhiteSpace(numero) Then Continue For
                        If proprieta.Key.Contains("CELL") OrElse proprieta.Key.Contains("MOBILE") Then
                            If Not listaCellulare.Contains(numero, StringComparer.OrdinalIgnoreCase) Then listaCellulare.Add(numero)
                        Else
                            If Not listaTelefono.Contains(numero, StringComparer.OrdinalIgnoreCase) Then listaTelefono.Add(numero)
                        End If
                    Next

                    Dim listaIndirizzo As New List(Of String)
                    For Each proprieta As KeyValuePair(Of String, String) In EstraiProprietaMultipleVCard(coppia.Value, "ADR")
                        Dim testoIndirizzo As String = ComponiIndirizzoDaCampoVCard(proprieta.Value)
                        If Not String.IsNullOrWhiteSpace(testoIndirizzo) AndAlso Not listaIndirizzo.Contains(testoIndirizzo, StringComparer.OrdinalIgnoreCase) Then listaIndirizzo.Add(testoIndirizzo)
                    Next

                    Dim listaSito As New List(Of String)
                    For Each proprieta As KeyValuePair(Of String, String) In EstraiProprietaMultipleVCard(coppia.Value, "URL")
                        Dim indirizzoSito As String = PrimoValoreVCard(proprieta.Value)
                        If Not String.IsNullOrWhiteSpace(indirizzoSito) AndAlso Not listaSito.Contains(indirizzoSito, StringComparer.OrdinalIgnoreCase) Then listaSito.Add(indirizzoSito)
                    Next

                    contatti.ImportaDaSincronizzazione(
                        chiaveAccount,
                        campi("UID"),
                        OttieniValore(campi, "FN"),
                        listaEmail,
                        listaTelefono,
                        listaCellulare,
                        listaIndirizzo,
                        listaSito,
                        OttieniValore(campi, "ORG"),
                        urlContatto,
                        CLSContatti.NormalizzaCompleanno(OttieniValore(campi, "BDAY")))
                    contatore += 1
                Catch exVCard As Exception
                    ScriviLog("CLSMail.SincronizzaRubrica vcard (account " & chiaveAccount & "): " & exVCard.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaRubrica (account " & chiaveAccount & "): " & ex.ToString())
        End Try
        Return contatore
    End Function

    ' Sincronizza la rubrica su tutti gli account attivi che supportano davvero la rubrica
    ' (CardDAV configurato, Google escluso: per Google non esiste una sincronizzazione dei
    ' contatti possibile in questo programma). Gli account che non la supportano (piu')
    ' vengono ripuliti da eventuali rubriche residue create in precedenza.
    Public Function SincronizzaRubricaTuttiGliAccount(contatti As CLSContatti) As Integer
        Dim totale As Integer = 0
        Try
            For Each impostazioni As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If AccountSupportaRubrica(impostazioni) Then
                    totale += SincronizzaRubrica(impostazioni.Chiave, contatti)
                Else
                    contatti.RimuoviRubricaAccount(impostazioni.Chiave)
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaRubricaTuttiGliAccount: " & ex.ToString())
        End Try
        Return totale
    End Function

    ' Funzione apposita per la sincronizzazione "in uscita": prende dalla rubrica (CLSContatti)
    ' i contatti aggiunti/modificati manualmente e marcati per l'account indicato
    ' (CLSContatti.OttieniContattiDaSincronizzare) e li invia al server CardDAV di
    ' quell'account con una richiesta PUT (crea il vCard se e' nuovo, lo sovrascrive se aveva
    ' gia' un UidServer). Ad invio riuscito il contatto viene segnato come sincronizzato
    ' (CLSContatti.SegnaContattoSincronizzato), salvando l'UidServer usato. Restituisce il
    ' numero di contatti inviati con successo.
    Public Function SincronizzaContattiManualiVersoServer(chiaveAccount As String, contatti As CLSContatti) As Integer
        Dim contatore As Integer = 0
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) OrElse contatti Is Nothing Then Return 0

            Dim daInviare As DataTable = contatti.OttieniContattiDaSincronizzare(chiaveAccount)
            If daInviare Is Nothing OrElse daInviare.Rows.Count = 0 Then Return 0

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim urlBase As String = RisolviCollezioneDav(impostazioni.UrlRubricaCardDav, utente, password,
                                                           "urn:ietf:params:xml:ns:carddav", "addressbook-home-set", "addressbook", True)
            If String.IsNullOrWhiteSpace(urlBase) Then
                Return 0
            End If
            If Not urlBase.EndsWith("/") Then urlBase &= "/"

            For Each riga As DataRow In daInviare.Rows
                Try
                    Dim vCard As KeyValuePair(Of String, String) = CostruisciVCard(riga, contatti)
                    If String.IsNullOrWhiteSpace(vCard.Key) Then Continue For

                    Dim urlContatto As String = ValoreColonna(riga, "UrlServer")
                    If String.IsNullOrWhiteSpace(urlContatto) Then
                        urlContatto = urlBase & Uri.EscapeDataString(vCard.Key) & ".vcf"
                    End If

                    Dim inviato As Boolean = EseguiPutWebDav(urlContatto, utente, password, vCard.Value, "text/vcard")

                    If inviato Then
                        contatti.SegnaContattoSincronizzato(riga("ChiaveWG").ToString(), vCard.Key, urlContatto)
                        contatore += 1
                    End If
                Catch exContatto As Exception
                    ScriviLog("CLSMail.SincronizzaContattiManualiVersoServer contatto (account " & chiaveAccount & "): " & exContatto.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaContattiManualiVersoServer (account " & chiaveAccount & "): " & ex.ToString())
        End Try
        Return contatore
    End Function

    Public Function SincronizzaEliminazioniContatti(chiaveAccount As String, contatti As CLSContatti) As Integer
        Dim contatore As Integer = 0
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) OrElse contatti Is Nothing Then Return 0

            Dim daEliminare As DataTable = contatti.OttieniEliminazioniDaSincronizzare(chiaveAccount)
            If daEliminare Is Nothing OrElse daEliminare.Rows.Count = 0 Then Return 0

            For Each riga As DataRow In daEliminare.Rows
                Try
                    Dim uid As String = ValoreColonna(riga, "UidServer")
                    Dim url As String = ValoreColonna(riga, "UrlServer")
                    If String.IsNullOrWhiteSpace(uid) AndAlso String.IsNullOrWhiteSpace(url) Then
                        contatti.RimuoviEliminazioneSincronizzata(riga("ChiaveWG").ToString())
                        Continue For
                    End If

                    If EliminaContattoDalServer(chiaveAccount, uid, url) Then
                        contatti.RimuoviEliminazioneSincronizzata(riga("ChiaveWG").ToString())
                        contatore += 1
                    End If
                Catch exRiga As Exception
                    ScriviLog("CLSMail.SincronizzaEliminazioniContatti riga (account " & chiaveAccount & "): " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaEliminazioniContatti (account " & chiaveAccount & "): " & ex.ToString())
        End Try
        Return contatore
    End Function

    Public Function SincronizzaEliminazioniContattiTuttiGliAccount(contatti As CLSContatti) As Integer
        Dim totale As Integer = 0
        Try
            For Each impostazioni As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If AccountSupportaRubrica(impostazioni) Then
                    totale += SincronizzaEliminazioniContatti(impostazioni.Chiave, contatti)
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaEliminazioniContattiTuttiGliAccount: " & ex.ToString())
        End Try
        Return totale
    End Function

    Public Function EliminaContattoDalServer(chiaveAccount As String, uidServer As String, Optional urlServer As String = "") As Boolean
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) Then Return False

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim url As String = If(urlServer, "").Trim()
            If String.IsNullOrWhiteSpace(url) Then
                If String.IsNullOrWhiteSpace(uidServer) Then Return False

                Dim urlCollezione As String = RisolviCollezioneDav(impostazioni.UrlRubricaCardDav, utente, password,
                                                                     "urn:ietf:params:xml:ns:carddav", "addressbook-home-set", "addressbook", True)
                If String.IsNullOrWhiteSpace(urlCollezione) Then Return False
                If Not urlCollezione.EndsWith("/") Then urlCollezione &= "/"

                url = TrovaHrefPerUidVCard(urlCollezione, utente, password, uidServer)
                If String.IsNullOrWhiteSpace(url) Then url = urlCollezione & Uri.EscapeDataString(uidServer) & ".vcf"
            End If

            Return EseguiDeleteWebDav(url, utente, password)
        Catch ex As Exception
            ScriviLog("CLSMail.EliminaContattoDalServer (" & If(uidServer, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function TrovaHrefPerUidVCard(urlCollezione As String, utente As String, password As String, uidServer As String) As String
        Try
            If String.IsNullOrWhiteSpace(uidServer) Then Return ""
            For Each coppia As KeyValuePair(Of String, String) In ScaricaVCardConHref(urlCollezione, utente, password)
                Try
                    Dim campi As Dictionary(Of String, String) = AnalizzaVCard(coppia.Value)
                    If campi Is Nothing OrElse Not campi.ContainsKey("UID") Then Continue For
                    If String.Equals(campi("UID"), uidServer, StringComparison.OrdinalIgnoreCase) Then
                        Return RisolviUrlRelativo(urlCollezione, coppia.Key)
                    End If
                Catch exCoppia As Exception
                    ScriviLog("CLSMail.TrovaHrefPerUidVCard coppia: " & exCoppia.ToString())
                End Try
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.TrovaHrefPerUidVCard (" & If(uidServer, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Come sopra ma su tutti gli account attivi con URL CardDAV configurato: comodo per
    ' inviare in un colpo solo i contatti manuali in sospeso su tutte le caselle.
    Public Function SincronizzaContattiManualiVersoServerTuttiGliAccount(contatti As CLSContatti) As Integer
        Dim totale As Integer = 0
        Try
            For Each impostazioni As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If AccountSupportaRubrica(impostazioni) Then
                    totale += SincronizzaContattiManualiVersoServer(impostazioni.Chiave, contatti)
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SincronizzaContattiManualiVersoServerTuttiGliAccount: " & ex.ToString())
        End Try
        Return totale
    End Function

    ' Sincronizza l'agenda dell'account indicato: legge gli eventi (VEVENT) dal server CalDAV e
    ' li salva nella tabella locale eventi_agenda (aggiornando quelli gia' importati in base a
    ' UidServer, per quello stesso account).
    ' Orchestrazione completa: CLSMail chiama CLSContatti (non il contrario).
    Public Function SincronizzaRubricaCompleta(contatti As CLSContatti, Optional chiaveAccount As String = "") As Integer
        Dim totale As Integer = 0
        Try
            If contatti Is Nothing Then
                _ultimoErrore = "Istanza rubrica assente."
                Return 0
            End If
            If String.IsNullOrWhiteSpace(chiaveAccount) Then
                totale += SincronizzaEliminazioniContattiTuttiGliAccount(contatti)
                totale += SincronizzaContattiManualiVersoServerTuttiGliAccount(contatti)
                totale += SincronizzaRubricaTuttiGliAccount(contatti)
            Else
                If Not AccountSupportaRubrica(chiaveAccount) Then
                    contatti.RimuoviRubricaAccount(chiaveAccount)
                    Return 0
                End If
                totale += SincronizzaEliminazioniContatti(chiaveAccount, contatti)
                totale += SincronizzaContattiManualiVersoServer(chiaveAccount, contatti)
                totale += SincronizzaRubrica(chiaveAccount, contatti)
            End If
        Catch ex As Exception
            ImpostaErrore("CLSMail.SincronizzaRubricaCompleta", ex)
        End Try
        Return totale
    End Function


    ' Risposta "grezza" di una richiesta WebDAV: stato HTTP finale, corpo testuale e URL
    ' effettivo su cui e' stata eseguita la richiesta (dopo eventuali redirect).
    Private Class RispostaDav
        Public Property CodiceStato As Integer = 0
        Public Property Successo As Boolean = False
        Public Property Corpo As String = ""
        Public Property UrlFinale As String = ""
    End Class

    ' Esegue una richiesta WebDAV (PROPFIND/REPORT/PUT/...) con autenticazione Basic seguendo
    ' MANUALMENTE eventuali redirect (301/302/303/307/308), invece di lasciare che sia
    ' HttpClientHandler a farlo in automatico.
    ' Motivo: quando .NET segue un redirect verso un host diverso (caso tipico di iCloud, che
    ' rimanda da "contacts.icloud.com" a un host specifico tipo "pXX-contacts.icloud.com") toglie
    ' d'ufficio l'header Authorization per motivi di sicurezza, e la richiesta successiva arriva
    ' al server senza credenziali: il risultato e' un 401 anche con password corretta. Seguendo i
    ' redirect a mano possiamo decidere noi di reinviare le credenziali.
    Private Sub ConfiguraReteDav()
        Try
            If _reteDavConfigurata Then Return
            _reteDavConfigurata = True
            Try
                ServicePointManager.Expect100Continue = False
            Catch exAspetta As Exception
                ScriviLog("CLSMail.ConfiguraReteDav expect100: " & exAspetta.Message)
            End Try
            Try
                ServicePointManager.DefaultConnectionLimit = 10
            Catch exLimite As Exception
                ScriviLog("CLSMail.ConfiguraReteDav limite: " & exLimite.Message)
            End Try
            Try
                ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType) Or CType(12288, SecurityProtocolType)
            Catch exTls As Exception
                Try
                    ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
                Catch exTlsBase As Exception
                    ScriviLog("CLSMail.ConfiguraReteDav tls: " & exTlsBase.Message)
                End Try
            End Try
        Catch ex As Exception
            ScriviLog("CLSMail.ConfiguraReteDav: " & ex.ToString())
        End Try
    End Sub

    Private Function AccountIcloud(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            Dim testo As String = (If(impostazioni.IndirizzoEmail, "") & " " & If(impostazioni.NomeUtente, "") & " " &
                                   If(impostazioni.ServerImap, "") & " " & If(impostazioni.ServerSmtp, "") & " " &
                                   If(impostazioni.UrlRubricaCardDav, "") & " " & If(impostazioni.UrlAgendaCalDav, "")).ToLowerInvariant()
            If testo.IndexOf("icloud", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If testo.IndexOf("@me.com", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If testo.IndexOf("@mac.com", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.AccountIcloud: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function UtenteDav(impostazioni As ImpostazioniAccountMail) As String
        Try
            If impostazioni Is Nothing Then Return ""
            Dim utente As String = If(impostazioni.NomeUtente, "").Trim()
            Dim email As String = If(impostazioni.IndirizzoEmail, "").Trim()
            If utente.IndexOf("@"c) > 0 Then Return utente
            If email.IndexOf("@"c) > 0 Then Return email
            If Not String.IsNullOrWhiteSpace(utente) Then Return utente
            Return email
        Catch ex As Exception
            ScriviLog("CLSMail.UtenteDav: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function PasswordDav(impostazioni As ImpostazioniAccountMail) As String
        Try
            If impostazioni Is Nothing Then Return ""
            Dim password As String = If(impostazioni.Password, "").Trim()
            If AccountIcloud(impostazioni) Then
                Dim pulita As New StringBuilder()
                For Each carattere As Char In password
                    If carattere <> " "c AndAlso carattere <> ChrW(160) AndAlso carattere <> ChrW(9) Then pulita.Append(carattere)
                Next
                password = pulita.ToString()
            End If
            Return password
        Catch ex As Exception
            ScriviLog("CLSMail.PasswordDav: " & ex.ToString())
            If impostazioni Is Nothing Then Return ""
            Return If(impostazioni.Password, "")
        End Try
    End Function

    Private Function EscapeXml(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Return valore.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;")
        Catch ex As Exception
            ScriviLog("CLSMail.EscapeXml: " & ex.ToString())
            Return valore
        End Try
    End Function

    Private Function RadiceUrl(url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return ""
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return ""
            Return uri.Scheme & "://" & uri.Authority & "/"
        Catch ex As Exception
            ScriviLog("CLSMail.RadiceUrl (" & If(url, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function PuntiPartenzaDav(urlConfigurato As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(urlConfigurato) Then Return risultato

            Dim url As String = urlConfigurato.Trim()
            Dim pulito As String = url
            Dim posizione As Integer = pulito.IndexOf("/.well-known/", StringComparison.OrdinalIgnoreCase)
            If posizione >= 0 Then pulito = pulito.Substring(0, posizione + 1)

            Dim radice As String = RadiceUrl(pulito)
            If String.IsNullOrWhiteSpace(radice) Then radice = RadiceUrl(url)

            If Not String.IsNullOrWhiteSpace(pulito) AndAlso Not risultato.Contains(pulito) Then risultato.Add(pulito)
            If Not String.IsNullOrWhiteSpace(radice) AndAlso Not risultato.Contains(radice) Then risultato.Add(radice)
            If Not risultato.Contains(url) Then risultato.Add(url)
        Catch ex As Exception
            ScriviLog("CLSMail.PuntiPartenzaDav (" & If(urlConfigurato, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ElencaRisorseDav(urlCollezione As String, utente As String, password As String, estensione As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Dim corpo As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:""><D:prop><D:getetag/><D:resourcetype/><D:getcontenttype/></D:prop></D:propfind>"

            Dim risposta As RispostaDav = EseguiRichiestaDav("PROPFIND", urlCollezione, utente, password, corpo, "text/xml", "1")
            If risposta Is Nothing OrElse (risposta.CodiceStato <> 207 AndAlso risposta.CodiceStato <> 200) Then
                Dim codice As Integer = 0
                If risposta IsNot Nothing Then codice = risposta.CodiceStato
                Return risultato
            End If

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(risposta.Corpo)

            For Each nodoRisposta As XmlNode In documento.SelectNodes("//*[local-name()='response']")
                Dim href As String = ""
                For Each figlio As XmlNode In nodoRisposta.SelectNodes(".//*")
                    If String.Equals(figlio.LocalName, "href", StringComparison.OrdinalIgnoreCase) Then
                        href = figlio.InnerText.Trim()
                        Exit For
                    End If
                Next
                If String.IsNullOrWhiteSpace(href) Then Continue For

                Dim confronto As String = href
                Dim posizioneParametri As Integer = confronto.IndexOf("?"c)
                If posizioneParametri > 0 Then confronto = confronto.Substring(0, posizioneParametri)
                If Not confronto.EndsWith(estensione, StringComparison.OrdinalIgnoreCase) Then Continue For

                If Not risultato.Contains(href) Then risultato.Add(href)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.ElencaRisorseDav (" & If(urlCollezione, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function EseguiRichiestaDav(metodo As String, url As String, utente As String, password As String,
                                         Optional corpo As String = Nothing, Optional tipoContenuto As String = "text/xml",
                                         Optional profondita As String = Nothing, Optional massimiRedirect As Integer = 5) As RispostaDav
        Dim risultato As New RispostaDav()
        Dim urlCorrente As String = url
        Try
            ConfiguraReteDav()

            Using gestore As New HttpClientHandler()
                gestore.AllowAutoRedirect = False
                Try
                    gestore.UseCookies = True
                Catch exCookie As Exception
                    ScriviLog("CLSMail.EseguiRichiestaDav cookie: " & exCookie.Message)
                End Try
                Try
                    gestore.PreAuthenticate = True
                Catch exPre As Exception
                    ScriviLog("CLSMail.EseguiRichiestaDav preauth: " & exPre.Message)
                End Try

                Using client As New HttpClient(gestore)
                    client.Timeout = TimeSpan.FromSeconds(90)
                    Try
                        client.DefaultRequestHeaders.ExpectContinue = False
                        client.DefaultRequestHeaders.ConnectionClose = False
                    Catch exIntestazioni As Exception
                        ScriviLog("CLSMail.EseguiRichiestaDav intestazioni: " & exIntestazioni.Message)
                    End Try

                    Dim credenziali As String = Convert.ToBase64String(Encoding.UTF8.GetBytes(If(utente, "") & ":" & If(password, "")))

                    For tentativo As Integer = 0 To massimiRedirect
                        Dim tentativiTimeout As Integer = 0
                        Dim reinvia As Boolean = True
                        While reinvia
                            reinvia = False
                            Using richiesta As New HttpRequestMessage(New HttpMethod(metodo), urlCorrente)
                                richiesta.Headers.TryAddWithoutValidation("Authorization", "Basic " & credenziali)
                                richiesta.Headers.TryAddWithoutValidation("User-Agent", "WinGabriel/1.0 (macOS; CardDAV)")
                                richiesta.Headers.TryAddWithoutValidation("Accept", "*/*")
                                richiesta.Headers.TryAddWithoutValidation("Accept-Encoding", "identity")
                                If Not String.IsNullOrWhiteSpace(profondita) Then richiesta.Headers.TryAddWithoutValidation("Depth", profondita)
                                If corpo IsNot Nothing Then richiesta.Content = New StringContent(corpo, New UTF8Encoding(False), tipoContenuto)

                                Dim risposta As HttpResponseMessage = Nothing
                                Try
                                    risposta = client.SendAsync(richiesta).GetAwaiter().GetResult()
                                Catch exAnnullata As TaskCanceledException
                                    tentativiTimeout += 1
                                    If tentativiTimeout <= 2 Then
                                        reinvia = True
                                        Thread.Sleep(1500 * tentativiTimeout)
                                        Continue While
                                    End If
                                    ScriviLog("CLSMail.EseguiRichiestaDav (" & metodo & " " & urlCorrente & "): tempo scaduto")
                                    Return risultato
                                End Try

                                Using risposta
                                    Dim codice As Integer = CInt(risposta.StatusCode)

                                    If (codice = 301 OrElse codice = 302 OrElse codice = 303 OrElse codice = 307 OrElse codice = 308) AndAlso risposta.Headers.Location IsNot Nothing Then
                                        Dim nuovaUri As Uri
                                        If risposta.Headers.Location.IsAbsoluteUri Then
                                            nuovaUri = risposta.Headers.Location
                                        Else
                                            nuovaUri = New Uri(New Uri(urlCorrente), risposta.Headers.Location)
                                        End If
                                        urlCorrente = nuovaUri.ToString()
                                        Continue For
                                    End If

                                    risultato.CodiceStato = codice
                                    risultato.UrlFinale = urlCorrente
                                    risultato.Successo = risposta.IsSuccessStatusCode
                                    Try
                                        risultato.Corpo = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                                    Catch exCorpo As TaskCanceledException
                                        tentativiTimeout += 1
                                        If tentativiTimeout <= 2 Then
                                            reinvia = True
                                            Thread.Sleep(1500 * tentativiTimeout)
                                            Continue While
                                        End If
                                        ScriviLog("CLSMail.EseguiRichiestaDav (" & metodo & " " & urlCorrente & "): tempo scaduto in lettura")
                                        Return risultato
                                    End Try

                                    If codice = 401 OrElse codice = 403 Then
                                        Dim sfida As String = ""
                                        Try
                                            For Each valore As String In risposta.Headers.GetValues("WWW-Authenticate")
                                                sfida &= valore & " "
                                            Next
                                        Catch exSfida As Exception
                                            sfida = ""
                                        End Try
                                        Dim frammento As String = If(risultato.Corpo, "")
                                        If frammento.Length > 300 Then frammento = frammento.Substring(0, 300)
                                    End If

                                    Return risultato
                                End Using
                            End Using
                        End While
                    Next

                    Return risultato
                End Using
            End Using
        Catch exAnnullataEsterna As TaskCanceledException
            ScriviLog("CLSMail.EseguiRichiestaDav (" & metodo & " " & url & "): tempo scaduto")
            Return risultato
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiRichiestaDav (" & metodo & " " & url & "): " & ex.Message)
            Return risultato
        End Try
    End Function


    ' Esegue una richiesta HTTP PUT (CardDAV) con autenticazione Basic per creare/sovrascrivere
    ' una risorsa (nel nostro caso un vCard) sul server. Restituisce True se il server ha
    ' risposto con un codice di successo (2xx).
    Private Function EseguiPutWebDav(url As String, utente As String, password As String, corpo As String, tipoContenuto As String) As Boolean
        Try
            Dim risposta As RispostaDav = EseguiRichiestaDav("PUT", url, utente, password, corpo, tipoContenuto)
            If risposta Is Nothing Then
                Return False
            End If
            If Not risposta.Successo Then
                ' Prima il fallimento veniva scartato in silenzio: senza codice di stato e corpo
                ' della risposta non c'era modo di distinguere "credenziali sbagliate" da "server
                ' che rifiuta l'ICS" da "collezione sbagliata". Lo logghiamo sempre.
                Dim frammentoCorpo As String = If(risposta.Corpo, "")
                If frammentoCorpo.Length > 500 Then frammentoCorpo = frammentoCorpo.Substring(0, 500)
                Return False
            End If
            ' Logghiamo anche il successo (non solo il fallimento): e' l'unico modo per verificare
            ' con certezza, guardando il log, che la scrittura e' arrivata davvero fino a iCloud e
            ' su QUALE url esatto e' finita (utile per capire se e' finita sul calendario sbagliato
            ' quando ce n'e' piu' di uno sull'account).
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiPutWebDav (" & url & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' ============================================================================================
    ' DISCOVERY CardDAV/CalDAV (RFC 6352 / RFC 4791): principal -> home-set -> collection reale.
    ' ============================================================================================
    '
    ' Molti provider (Yahoo, Fastmail, GMX, mailbox.org, Aruba, ...) espongono direttamente l'URL
    ' della collection di contatti/eventi: in quel caso la discovery non serve e viene saltata
    ' (si usa l'URL cosi' come configurato). iCloud invece NON espone un URL statico: bisogna
    ' interrogare il server per scoprire, con le credenziali dell'utente, dove si trova davvero
    ' la sua rubrica/agenda. Questa funzione fa da "ponte" davanti a REPORT e PUT: prova prima la
    ' discovery, e se non produce nulla di utile ripiega sull'URL originale (comportamento
    ' invariato per i provider che non ne hanno bisogno).
    '
    ' spazioNomi/elementoHomeSet: "urn:ietf:params:xml:ns:carddav" + "addressbook-home-set" per la
    ' rubrica, "urn:ietf:params:xml:ns:caldav" + "calendar-home-set" per l'agenda.
    ' tipoRisorsa: "addressbook" oppure "calendar" (per riconoscere le collection nello home-set).
    ' richiedeScrittura: quando True (percorsi di scrittura: PUT/DELETE) la funzione non ripiega MAI
    ' sulla cartella home-set grezza se non trova una collezione specifica di tipo corretto, perche'
    ' su iCloud e altri provider l'home-set e' solo un contenitore e non un calendario/rubrica vero:
    ' scriverci dentro puo' restituire un HTTP di successo senza che il contatto/evento diventi mai
    ' visibile lato client (es. su iPhone). In quel caso la funzione restituisce stringa vuota,
    ' cosi' il chiamante puo' evitare la scrittura invece di mandarla verso un posto sbagliato.
    Private Function RisolviCollezioneDav(urlConfigurato As String, utente As String, password As String,
                                           spazioNomi As String, elementoHomeSet As String, tipoRisorsa As String,
                                           Optional richiedeScrittura As Boolean = False) As String
        Try
            If String.IsNullOrWhiteSpace(urlConfigurato) Then Return If(richiedeScrittura, "", urlConfigurato)

            Dim chiaveCache As String = urlConfigurato & "|" & If(utente, "") & "|" & tipoRisorsa & If(richiedeScrittura, "|W", "")
            Dim urlDaCache As String = Nothing
            If _cacheCollezioniDav.TryGetValue(chiaveCache, urlDaCache) Then Return urlDaCache

            Dim corpoPropfind As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:"" xmlns:C=""" & spazioNomi & """>" &
                "<D:prop><D:current-user-principal/><D:principal-URL/><C:" & elementoHomeSet & "/></D:prop></D:propfind>"

            Dim urlHost As String = ""
            Dim homeSet As String = ""
            Dim principal As String = ""

            For Each partenza As String In PuntiPartenzaDav(urlConfigurato)
                Dim risposta As RispostaDav = EseguiRichiestaDav("PROPFIND", partenza, utente, password, corpoPropfind, "text/xml", "0")
                If risposta Is Nothing Then Continue For
                If risposta.CodiceStato <> 207 AndAlso risposta.CodiceStato <> 200 Then Continue For

                urlHost = risposta.UrlFinale
                homeSet = EstraiPrimoHref(risposta.Corpo, elementoHomeSet)
                principal = EstraiPrimoHref(risposta.Corpo, "current-user-principal")
                If String.IsNullOrWhiteSpace(principal) Then principal = EstraiPrimoHref(risposta.Corpo, "principal-URL")
                If Not String.IsNullOrWhiteSpace(homeSet) OrElse Not String.IsNullOrWhiteSpace(principal) Then Exit For
            Next

            If String.IsNullOrWhiteSpace(urlHost) Then
                Return If(richiedeScrittura, "", urlConfigurato)
            End If

            If String.IsNullOrWhiteSpace(homeSet) AndAlso Not String.IsNullOrWhiteSpace(principal) Then
                Dim urlPrincipal As String = RisolviUrlRelativo(urlHost, principal)
                Dim rispostaPrincipal As RispostaDav = EseguiRichiestaDav("PROPFIND", urlPrincipal, utente, password, corpoPropfind, "text/xml", "0")
                If rispostaPrincipal IsNot Nothing AndAlso (rispostaPrincipal.CodiceStato = 207 OrElse rispostaPrincipal.CodiceStato = 200) Then
                    urlHost = rispostaPrincipal.UrlFinale
                    homeSet = EstraiPrimoHref(rispostaPrincipal.Corpo, elementoHomeSet)
                End If
            End If

            If String.IsNullOrWhiteSpace(homeSet) Then
                Return If(richiedeScrittura, "", urlConfigurato)
            End If

            Dim urlHomeSet As String = RisolviUrlRelativo(urlHost, homeSet)
            If Not urlHomeSet.EndsWith("/") Then urlHomeSet &= "/"

            ' Per l'agenda chiediamo anche CALDAV:supported-calendar-component-set (RFC 4791
            ' §5.2.3): serve a distinguere un calendario "vero" (VEVENT) da una lista Promemoria/
            ' Reminders, che su iCloud vive nello stesso home-set e ha anch'essa resourcetype
            ' calendar, ma supporta solo VTODO. Senza questo controllo l'euristica sui nomi puo'
            ' scegliere per prima proprio la lista Promemoria: il PUT dell'evento va li' e non
            ' comparira' mai nel Calendario.
            '
            ' Quando si scrive (richiedeScrittura) chiediamo anche DAV:current-user-privilege-set
            ' (RFC 3744 §5.5): sia su iCloud che su altri provider l'home-set puo' contenere, oltre
            ' alla collezione principale, rubriche/calendari condivisi da altri a cui l'utente ha
            ' accesso in sola lettura (es. una rubrica di famiglia). Se una di queste viene scelta
            ' per errore il PUT puo' fallire (403) o, peggio, non fallire affatto pur non rendendo
            ' mai visibile il contatto/evento all'utente. Scartiamo quindi le collezioni per cui il
            ' server dichiara esplicitamente di non concedere il permesso di scrittura.
            Dim corpoElenco As String
            Dim chiediPrivilegi As String = If(richiedeScrittura, "<D:current-user-privilege-set/>", "")
            If String.Equals(tipoRisorsa, "calendar", StringComparison.OrdinalIgnoreCase) Then
                corpoElenco =
                    "<?xml version=""1.0"" encoding=""utf-8""?>" &
                    "<D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                    "<D:prop><D:resourcetype/><D:displayname/><C:supported-calendar-component-set/>" & chiediPrivilegi & "</D:prop></D:propfind>"
            Else
                corpoElenco =
                    "<?xml version=""1.0"" encoding=""utf-8""?>" &
                    "<D:propfind xmlns:D=""DAV:""><D:prop><D:resourcetype/><D:displayname/>" & chiediPrivilegi & "</D:prop></D:propfind>"
            End If

            Dim rispostaElenco As RispostaDav = EseguiRichiestaDav("PROPFIND", urlHomeSet, utente, password, corpoElenco, "text/xml", "1")
            If rispostaElenco Is Nothing OrElse (rispostaElenco.CodiceStato <> 207 AndAlso rispostaElenco.CodiceStato <> 200) Then
                If richiedeScrittura Then
                    Return ""
                End If
                _cacheCollezioniDav(chiaveCache) = urlHomeSet
                Return urlHomeSet
            End If

            Dim collectionTrovata As String = EstraiPrimaCollectionDiTipo(rispostaElenco.Corpo, rispostaElenco.UrlFinale, tipoRisorsa, richiedeScrittura)

            If String.IsNullOrWhiteSpace(collectionTrovata) Then
                If richiedeScrittura Then
                    Return ""
                End If
                _cacheCollezioniDav(chiaveCache) = urlHomeSet
                Return urlHomeSet
            End If

            Dim urlFinale As String = collectionTrovata
            If Not urlFinale.EndsWith("/") Then urlFinale &= "/"

            _cacheCollezioniDav(chiaveCache) = urlFinale
            Return urlFinale
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviCollezioneDav (" & urlConfigurato & "): " & ex.ToString())
            Return If(richiedeScrittura, "", urlConfigurato)
        End Try
    End Function

    ' Estrae il primo <D:href> figlio del primo elemento con il nome locale indicato (es.
    ' "current-user-principal" o "addressbook-home-set"), indipendentemente dal prefisso usato.
    Private Function EstraiPrimoHref(testoXml As String, nomeElemento As String) As String
        Try
            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(testoXml)

            For Each nodo As XmlNode In documento.SelectNodes("//*")
                If String.Equals(nodo.LocalName, nomeElemento, StringComparison.OrdinalIgnoreCase) Then
                    For Each figlio As XmlNode In nodo.ChildNodes
                        If String.Equals(figlio.LocalName, "href", StringComparison.OrdinalIgnoreCase) Then
                            If Not String.IsNullOrWhiteSpace(figlio.InnerText) Then Return figlio.InnerText.Trim()
                        End If
                    Next
                End If
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiPrimoHref (" & nomeElemento & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Raccoglie TUTTE le collezioni valide di un dato tipo (calendar/addressbook) da una risposta
    ' multistatus, applicando gli stessi controlli di EstraiPrimaCollectionDiTipo (resourcetype,
    ' supporto VEVENT quando richiesto, permessi di scrittura quando richiesti, esclusione delle
    ' collezioni da scartare). Restituisce la lista grezza, senza scegliere quale preferire: la
    ' scelta (se serve un solo risultato) resta a carico del chiamante.
    Private Function TrovaCollezioniValide(testoXml As String, urlBase As String, tipoRisorsa As String,
                                            Optional richiedeScrittura As Boolean = False) As List(Of String)
        Dim candidati As New List(Of String)
        Try
            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(testoXml)

            Dim controllaComponentiVEvent As Boolean = String.Equals(tipoRisorsa, "calendar", StringComparison.OrdinalIgnoreCase)

            For Each risposta As XmlNode In documento.SelectNodes("//*[local-name()='response']")
                Dim tipoTrovato As Boolean = False
                Dim hrefRisposta As String = ""
                Dim proprietaComponentiPresente As Boolean = False
                Dim supportaVEvent As Boolean = False
                Dim proprietaPrivilegiPresente As Boolean = False
                Dim consenteScrittura As Boolean = False

                ' L'href sta direttamente sotto <response>, non dentro un <propstat> specifico:
                ' lo leggiamo a parte, prima di valutare le proprieta' propstat per propstat.
                For Each nodoHref As XmlNode In risposta.ChildNodes
                    If String.Equals(nodoHref.LocalName, "href", StringComparison.OrdinalIgnoreCase) Then
                        hrefRisposta = nodoHref.InnerText.Trim()
                        Exit For
                    End If
                Next

                ' IMPORTANTE: una <D:response> WebDAV puo' contenere PIU' <D:propstat>, uno per le
                ' proprieta' che il server e' riuscito a restituire (status 200) e uno per quelle
                ' che non e' riuscito a fornire per quella risorsa (status 404), che compaiono
                ' comunque come elemento XML VUOTO dentro il propstat fallito. iCloud fa proprio
                ' questo con current-user-privilege-set sull'home-set del calendario: se leggessimo
                ' quell'elemento vuoto come "proprieta' presente ma senza permesso di scrittura",
                ' scarteremmo per errore il calendario vero (che in realta' e' scrivibile) solo
                ' perche' il server non ha risposto a QUELLA proprieta' in QUEL propstat. Per questo
                ' consideriamo resourcetype/supported-calendar-component-set/current-user-privilege-set
                ' solo quando arrivano in un propstat con stato di successo (2xx).
                For Each propstat As XmlNode In risposta.SelectNodes(".//*[local-name()='propstat']")
                    Dim statoTesto As String = ""
                    For Each nodoStato As XmlNode In propstat.ChildNodes
                        If String.Equals(nodoStato.LocalName, "status", StringComparison.OrdinalIgnoreCase) Then
                            statoTesto = nodoStato.InnerText
                            Exit For
                        End If
                    Next
                    Dim successoPropstat As Boolean = statoTesto.IndexOf(" 200 ", StringComparison.Ordinal) >= 0 OrElse statoTesto.TrimEnd().EndsWith(" 200")
                    If Not successoPropstat Then Continue For

                    For Each nodo As XmlNode In propstat.SelectNodes(".//*")
                        If String.Equals(nodo.LocalName, tipoRisorsa, StringComparison.OrdinalIgnoreCase) Then
                            tipoTrovato = True
                        End If
                        If controllaComponentiVEvent AndAlso String.Equals(nodo.LocalName, "supported-calendar-component-set", StringComparison.OrdinalIgnoreCase) Then
                            proprietaComponentiPresente = True
                            For Each figlio As XmlNode In nodo.ChildNodes
                                If String.Equals(figlio.LocalName, "comp", StringComparison.OrdinalIgnoreCase) Then
                                    Dim attributoNome As XmlAttribute = figlio.Attributes("name")
                                    If attributoNome IsNot Nothing AndAlso String.Equals(attributoNome.Value, "VEVENT", StringComparison.OrdinalIgnoreCase) Then
                                        supportaVEvent = True
                                    End If
                                End If
                            Next
                        End If
                        If richiedeScrittura AndAlso String.Equals(nodo.LocalName, "current-user-privilege-set", StringComparison.OrdinalIgnoreCase) Then
                            proprietaPrivilegiPresente = True
                            ' RFC 3744 §5.5: ogni <D:privilege> elenca un permesso concesso; cerchiamo
                            ' "write", "write-content" o "all" fra i suoi figli diretti.
                            For Each privilegio As XmlNode In nodo.SelectNodes(".//*[local-name()='privilege']")
                                For Each concessione As XmlNode In privilegio.ChildNodes
                                    If String.Equals(concessione.LocalName, "write", StringComparison.OrdinalIgnoreCase) OrElse
                                       String.Equals(concessione.LocalName, "write-content", StringComparison.OrdinalIgnoreCase) OrElse
                                       String.Equals(concessione.LocalName, "all", StringComparison.OrdinalIgnoreCase) Then
                                        consenteScrittura = True
                                    End If
                                Next
                            Next
                        End If
                    Next
                Next

                If Not tipoTrovato OrElse String.IsNullOrWhiteSpace(hrefRisposta) Then Continue For
                ' RFC 4791 §5.2.3: se il server dichiara esplicitamente quali componenti supporta
                ' e VEVENT non c'e' (tipico delle liste Promemoria/Reminders di iCloud), questa
                ' non e' una collezione utilizzabile per gli eventi: la scartiamo. Se il server
                ' non espone affatto la proprieta' non blocchiamo nulla (comportamento invariato
                ' per i provider che non la implementano).
                If controllaComponentiVEvent AndAlso proprietaComponentiPresente AndAlso Not supportaVEvent Then Continue For
                ' RFC 3744 §5.5: se il server dichiara esplicitamente i permessi e la scrittura non
                ' e' concessa (es. una rubrica/calendario condiviso in sola lettura), scartiamo
                ' anche questa: scriverci potrebbe fallire o, peggio, non rendere mai visibile il
                ' contatto/evento al proprietario della risorsa. Se il server non espone la
                ' proprieta' non blocchiamo nulla (stesso comportamento permissivo di sempre).
                If richiedeScrittura AndAlso proprietaPrivilegiPresente AndAlso Not consenteScrittura Then Continue For
                If CollezioneDaScartare(hrefRisposta) Then Continue For

                Dim assoluto As String = RisolviUrlRelativo(urlBase, hrefRisposta)
                If Not candidati.Contains(assoluto) Then candidati.Add(assoluto)
            Next

            Return candidati
        Catch ex As Exception
            ScriviLog("CLSMail.TrovaCollezioniValide (" & tipoRisorsa & "): " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    ' Scandisce una risposta multistatus (PROPFIND Depth 1) alla ricerca della prima <D:response>
    ' il cui <D:resourcetype> contiene un elemento del tipo richiesto (addressbook/calendar) e ne
    ' restituisce l'href assoluto.
    Private Function EstraiPrimaCollectionDiTipo(testoXml As String, urlBase As String, tipoRisorsa As String,
                                                   Optional richiedeScrittura As Boolean = False) As String
        Try
            Dim candidati As List(Of String) = TrovaCollezioniValide(testoXml, urlBase, tipoRisorsa, richiedeScrittura)
            If candidati.Count = 0 Then Return ""

            ' NOTA: se un account ha piu' calendari validi (es. "Casa" e "Lavoro"), fino a qui
            ' possono sopravvivere piu' candidati contemporaneamente. Su iCloud gli href delle
            ' collection sono identificativi opachi tipo /12345678-ABCD-.../ e NON contengono mai
            ' parole come "home"/"default"/"personal", quindi l'euristica sotto quasi certamente
            ' non trovera' corrispondenze e si finira' su candidati(0): il PRIMO calendario
            ' restituito dal server nell'ordine in cui compare nella risposta, che puo' benissimo
            ' NON essere quello che l'utente si aspetta (es. scrive su "Lavoro" mentre l'utente
            ' guarda "Casa" sull'iPhone: nessun errore, evento creato, ma sul calendario sbagliato).
            For Each candidato As String In candidati
                Dim minuscolo As String = candidato.ToLowerInvariant()
                If minuscolo.EndsWith("/card/") OrElse minuscolo.EndsWith("/card") Then Return candidato
            Next

            For Each candidato As String In candidati
                Dim minuscolo As String = candidato.ToLowerInvariant()
                If minuscolo.Contains("default") OrElse minuscolo.Contains("home") OrElse minuscolo.Contains("personal") Then Return candidato
            Next

            Return candidati(0)
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiPrimaCollectionDiTipo (" & tipoRisorsa & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function CollezioneDaScartare(href As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(href) Then Return True
            Dim minuscolo As String = href.ToLowerInvariant()
            If minuscolo.Contains("/inbox") Then Return True
            If minuscolo.Contains("/outbox") Then Return True
            If minuscolo.Contains("/dropbox") Then Return True
            If minuscolo.Contains("/notification") Then Return True
            If minuscolo.Contains("/freebusy") Then Return True
            If minuscolo.Contains("/directory") Then Return True
            If minuscolo.Contains("gateway") Then Return True
            If minuscolo.Contains("/tasks") Then Return True
            If minuscolo.Contains("reminder") Then Return True
            If minuscolo.Contains("promemoria") Then Return True
            If minuscolo.Contains("/todo") Then Return True
            If minuscolo.Contains("birthday") Then Return True
            If minuscolo.Contains("compleann") Then Return True
            If minuscolo.Contains("holiday") Then Return True
            If minuscolo.Contains("festivit") Then Return True
            If minuscolo.Contains("readonly") Then Return True
            If minuscolo.Contains("read-only") Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.CollezioneDaScartare (" & If(href, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Risolve un href (assoluto o relativo, come spesso capita nelle risposte DAV) rispetto
    ' all'host/URL su cui e' stata fatta la richiesta che l'ha restituito.
    Private Function RisolviUrlRelativo(urlBase As String, href As String) As String
        Try
            If String.IsNullOrWhiteSpace(href) Then Return urlBase
            Dim uriHref As Uri = Nothing
            If Uri.TryCreate(href, UriKind.Absolute, uriHref) Then Return uriHref.ToString()
            Return New Uri(New Uri(urlBase), href).ToString()
        Catch
            Return href
        End Try
    End Function

    ' Costruisce il testo vCard (3.0) di un contatto a partire dalla riga letta da CLSContatti,
    ' riusando l'UidServer se il contatto ne aveva gia' uno (aggiornamento) o generandone uno
    ' nuovo (creazione). Restituisce la coppia Uid/testo vCard; Uid vuoto se qualcosa e' andato
    ' storto (riga non valida).
    Private Function CostruisciVCard(riga As DataRow, contatti As CLSContatti) As KeyValuePair(Of String, String)
        Try
            Dim uid As String = riga("UidServer").ToString()
            If String.IsNullOrWhiteSpace(uid) Then uid = Guid.NewGuid().ToString().ToUpperInvariant()

            Dim nome As String = riga("Nome").ToString()
            Dim cognome As String = riga("Cognome").ToString()
            Dim nomeCompleto As String = (nome & " " & cognome).Trim()

            Dim chiaveContatto As String = riga("ChiaveWG").ToString()
            Dim listaEmail As New List(Of String)
            Dim listaTelefono As New List(Of String)
            Dim listaCellulare As New List(Of String)
            Dim listaIndirizzo As New List(Of String)
            Dim listaSito As New List(Of String)
            If contatti IsNot Nothing Then
                listaEmail = contatti.OttieniRecapiti(chiaveContatto, CLSContatti.CategoriaRecapitoEmail)
                listaTelefono = contatti.OttieniRecapiti(chiaveContatto, CLSContatti.CategoriaRecapitoTelefono)
                listaCellulare = contatti.OttieniRecapiti(chiaveContatto, CLSContatti.CategoriaRecapitoCellulare)
                listaIndirizzo = contatti.OttieniRecapiti(chiaveContatto, CLSContatti.CategoriaRecapitoIndirizzo)
                listaSito = contatti.OttieniRecapiti(chiaveContatto, CLSContatti.CategoriaRecapitoSito)
            End If
            If listaEmail.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(riga("Email").ToString()) Then listaEmail.Add(riga("Email").ToString())
            If listaTelefono.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(riga("Telefono").ToString()) Then listaTelefono.Add(riga("Telefono").ToString())
            If listaCellulare.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(riga("Cellulare").ToString()) Then listaCellulare.Add(riga("Cellulare").ToString())

            If String.IsNullOrWhiteSpace(nomeCompleto) Then
                If listaEmail.Count > 0 Then
                    nomeCompleto = listaEmail(0)
                Else
                    nomeCompleto = riga("Email").ToString()
                End If
            End If

            Dim testo As New StringBuilder()
            testo.Append("BEGIN:VCARD").Append(vbCrLf)
            testo.Append("VERSION:3.0").Append(vbCrLf)
            testo.Append("PRODID:-//WinGabriel//Rubrica//IT").Append(vbCrLf)
            testo.Append("UID:").Append(EscapeValoreVCard(uid)).Append(vbCrLf)
            testo.Append("N:").Append(EscapeValoreVCard(cognome)).Append(";").Append(EscapeValoreVCard(nome)).Append(";;;").Append(vbCrLf)
            testo.Append("FN:").Append(EscapeValoreVCard(nomeCompleto)).Append(vbCrLf)

            For indice As Integer = 0 To listaEmail.Count - 1
                Dim tipo As String = If(indice = 0, "TYPE=INTERNET;TYPE=HOME;TYPE=PREF", "TYPE=INTERNET;TYPE=OTHER")
                testo.Append("EMAIL;").Append(tipo).Append(":").Append(EscapeValoreVCard(listaEmail(indice))).Append(vbCrLf)
            Next

            For indice As Integer = 0 To listaCellulare.Count - 1
                Dim tipo As String = If(indice = 0, "TYPE=CELL;TYPE=VOICE;TYPE=PREF", "TYPE=CELL;TYPE=VOICE")
                testo.Append("TEL;").Append(tipo).Append(":").Append(EscapeValoreVCard(listaCellulare(indice))).Append(vbCrLf)
            Next

            For indice As Integer = 0 To listaTelefono.Count - 1
                Dim tipo As String = If(listaCellulare.Count = 0 AndAlso indice = 0, "TYPE=VOICE;TYPE=PREF", "TYPE=VOICE")
                testo.Append("TEL;").Append(tipo).Append(":").Append(EscapeValoreVCard(listaTelefono(indice))).Append(vbCrLf)
            Next

            For Each indirizzo As String In listaIndirizzo
                testo.Append("ADR;TYPE=HOME:;;").Append(EscapeValoreVCard(indirizzo)).Append(";;;;").Append(vbCrLf)
            Next

            For Each sito As String In listaSito
                testo.Append("URL:").Append(EscapeValoreVCard(sito)).Append(vbCrLf)
            Next

            If riga.Table.Columns.Contains("Compleanno") Then
                Dim rigaCompleanno As String = CLSContatti.RigaVCardCompleanno(riga("Compleanno").ToString())
                If rigaCompleanno.Length > 0 Then testo.Append(rigaCompleanno).Append(vbCrLf)
            End If
            If Not String.IsNullOrWhiteSpace(riga("Azienda").ToString()) Then
                testo.Append("ORG:").Append(EscapeValoreVCard(riga("Azienda").ToString())).Append(";").Append(vbCrLf)
            End If
            testo.Append("REV:").Append(DateTime.UtcNow.ToString("yyyyMMdd\THHmmss\Z")).Append(vbCrLf)
            testo.Append("END:VCARD").Append(vbCrLf)

            Return New KeyValuePair(Of String, String)(uid, testo.ToString())
        Catch ex As Exception
            ScriviLog("CLSMail.CostruisciVCard: " & ex.ToString())
            Return New KeyValuePair(Of String, String)("", "")
        End Try
    End Function

    Private Function EstraiProprietaMultipleVCard(testo As String, nomeProprieta As String) As List(Of KeyValuePair(Of String, String))
        Dim risultato As New List(Of KeyValuePair(Of String, String))
        Try
            Dim righe As List(Of String) = SciogliRigheContinuazione(testo)
            For Each riga As String In righe
                Dim posizioneDuePunti As Integer = PosizioneSeparatoreVCard(riga)
                If posizioneDuePunti <= 0 Then Continue For

                Dim chiaveGrezza As String = riga.Substring(0, posizioneDuePunti)
                Dim valoreGrezzo As String = riga.Substring(posizioneDuePunti + 1).Trim()

                Dim chiave As String = chiaveGrezza.Split(";"c)(0).Trim()
                Dim posizionePunto As Integer = chiave.IndexOf("."c)
                If posizionePunto >= 0 Then chiave = chiave.Substring(posizionePunto + 1)
                chiave = chiave.Trim().ToUpperInvariant()
                If chiave <> nomeProprieta Then Continue For

                Dim valore As String = DecodificaValoreVCard(valoreGrezzo, chiaveGrezza)
                If String.IsNullOrWhiteSpace(valore) Then Continue For

                risultato.Add(New KeyValuePair(Of String, String)(chiaveGrezza.ToUpperInvariant(), valore))
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiProprietaMultipleVCard (" & If(nomeProprieta, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ComponiIndirizzoDaCampoVCard(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim parti() As String = valore.Split(";"c)
            Dim componenti As New List(Of String)
            For Each parte As String In parti
                Dim testo As String = parte.Trim()
                If testo.Length > 0 Then componenti.Add(testo)
            Next
            Return String.Join(", ", componenti)
        Catch ex As Exception
            ScriviLog("CLSMail.ComponiIndirizzoDaCampoVCard: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Escape minimo dei valori vCard (RFC 6350): backslash, punto e virgola, virgola e ritorni a
    ' capo vanno "protetti" perche' sono caratteri speciali nel formato.
    Private Function EscapeValoreVCard(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Return valore.Replace("\", "\\").Replace(";", "\;").Replace(",", "\,").Replace(vbCrLf, "\n").Replace(vbLf, "\n")
        Catch
            Return valore
        End Try
    End Function

    ' Analisi essenziale di un vCard: scioglie le righe spezzate (continuazione con spazio/tab
    ' iniziale) e legge le proprieta' piu' comuni (UID, FN, EMAIL, TEL, ORG). I parametri dopo
    ' il ";" nel nome della proprieta' (es. TEL;TYPE=cell) vengono ignorati ai fini della chiave.
    Private Function AnalizzaVCard(testo As String) As Dictionary(Of String, String)
        Try
            Dim campi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim righe As List(Of String) = SciogliRigheContinuazione(testo)

            Dim emailPreferita As String = ""
            Dim telefonoPreferito As String = ""
            Dim nomeStrutturato As String = ""

            For Each riga As String In righe
                Dim posizioneDuePunti As Integer = PosizioneSeparatoreVCard(riga)
                If posizioneDuePunti <= 0 Then Continue For

                Dim chiaveGrezza As String = riga.Substring(0, posizioneDuePunti)
                Dim valore As String = DecodificaValoreVCard(riga.Substring(posizioneDuePunti + 1).Trim(), chiaveGrezza)
                If String.IsNullOrWhiteSpace(valore) Then Continue For

                Dim parametri As String = chiaveGrezza.ToUpperInvariant()
                Dim chiave As String = chiaveGrezza.Split(";"c)(0).Trim()
                Dim posizionePunto As Integer = chiave.IndexOf("."c)
                If posizionePunto >= 0 Then chiave = chiave.Substring(posizionePunto + 1)
                chiave = chiave.Trim().ToUpperInvariant()

                Select Case chiave
                    Case "UID"
                        If Not campi.ContainsKey("UID") Then campi("UID") = valore
                    Case "FN"
                        If Not campi.ContainsKey("FN") Then campi("FN") = valore
                    Case "BDAY"
                        If Not campi.ContainsKey("BDAY") Then campi("BDAY") = valore
                    Case "N"
                        If String.IsNullOrWhiteSpace(nomeStrutturato) Then nomeStrutturato = NomeDaCampoVCard(valore)
                    Case "EMAIL"
                        Dim indirizzo As String = PrimoValoreVCard(valore)
                        If Not String.IsNullOrWhiteSpace(indirizzo) Then
                            If String.IsNullOrWhiteSpace(emailPreferita) OrElse parametri.Contains("PREF") Then emailPreferita = indirizzo
                        End If
                    Case "TEL"
                        Dim numero As String = PrimoValoreVCard(valore)
                        If Not String.IsNullOrWhiteSpace(numero) Then
                            If String.IsNullOrWhiteSpace(telefonoPreferito) OrElse parametri.Contains("CELL") OrElse parametri.Contains("PREF") Then telefonoPreferito = numero
                        End If
                    Case "ORG"
                        If Not campi.ContainsKey("ORG") Then campi("ORG") = valore.Split(";"c)(0).Trim()
                End Select
            Next

            If Not String.IsNullOrWhiteSpace(emailPreferita) Then campi("EMAIL") = emailPreferita
            If Not String.IsNullOrWhiteSpace(telefonoPreferito) Then campi("TEL") = telefonoPreferito

            If Not campi.ContainsKey("FN") OrElse String.IsNullOrWhiteSpace(campi("FN")) Then
                If Not String.IsNullOrWhiteSpace(nomeStrutturato) Then
                    campi("FN") = nomeStrutturato
                ElseIf campi.ContainsKey("EMAIL") Then
                    campi("FN") = campi("EMAIL")
                End If
            End If

            If Not campi.ContainsKey("UID") OrElse String.IsNullOrWhiteSpace(campi("UID")) Then
                Dim baseUid As String = OttieniValore(campi, "EMAIL") & "|" & OttieniValore(campi, "FN") & "|" & OttieniValore(campi, "TEL")
                If Not String.IsNullOrWhiteSpace(baseUid.Replace("|", "").Trim()) Then
                    campi("UID") = "wg-" & (baseUid.GetHashCode() And &H7FFFFFFF).ToString()
                End If
            End If

            Return campi
        Catch ex As Exception
            ScriviLog("CLSMail.AnalizzaVCard: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function PosizioneSeparatoreVCard(riga As String) As Integer
        Try
            If String.IsNullOrEmpty(riga) Then Return -1
            Dim dentroApici As Boolean = False
            For posizione As Integer = 0 To riga.Length - 1
                Dim carattere As Char = riga(posizione)
                If carattere = """"c Then
                    dentroApici = Not dentroApici
                ElseIf carattere = ":"c AndAlso Not dentroApici Then
                    Return posizione
                End If
            Next
            Return -1
        Catch ex As Exception
            ScriviLog("CLSMail.PosizioneSeparatoreVCard: " & ex.ToString())
            Return riga.IndexOf(":"c)
        End Try
    End Function

    Private Function DecodificaValoreVCard(valore As String, parametri As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Dim risultato As String = valore

            If Not String.IsNullOrEmpty(parametri) AndAlso parametri.ToUpperInvariant().Contains("QUOTED-PRINTABLE") Then
                risultato = DecodificaQuotedPrintable(risultato)
            End If

            Dim testo As New StringBuilder()
            Dim posizione As Integer = 0
            While posizione < risultato.Length
                Dim carattere As Char = risultato(posizione)
                If carattere = "\"c AndAlso posizione < risultato.Length - 1 Then
                    Dim successivo As Char = risultato(posizione + 1)
                    Select Case successivo
                        Case "n"c, "N"c
                            testo.Append(vbCrLf)
                        Case "\"c
                            testo.Append("\"c)
                        Case ","c
                            testo.Append(","c)
                        Case ";"c
                            testo.Append(";"c)
                        Case Else
                            testo.Append(successivo)
                    End Select
                    posizione += 2
                Else
                    testo.Append(carattere)
                    posizione += 1
                End If
            End While

            Return testo.ToString().Trim()
        Catch ex As Exception
            ScriviLog("CLSMail.DecodificaValoreVCard: " & ex.ToString())
            Return valore
        End Try
    End Function

    Private Function DecodificaQuotedPrintable(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Dim byteDecodificati As New List(Of Byte)
            Dim posizione As Integer = 0
            While posizione < valore.Length
                Dim carattere As Char = valore(posizione)
                If carattere = "="c AndAlso posizione + 2 < valore.Length Then
                    Dim esadecimale As String = valore.Substring(posizione + 1, 2)
                    Dim valoreByte As Integer = 0
                    If Integer.TryParse(esadecimale, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, valoreByte) Then
                        byteDecodificati.Add(CByte(valoreByte))
                        posizione += 3
                        Continue While
                    End If
                End If
                byteDecodificati.AddRange(Encoding.UTF8.GetBytes(carattere.ToString()))
                posizione += 1
            End While
            Return Encoding.UTF8.GetString(byteDecodificati.ToArray())
        Catch ex As Exception
            ScriviLog("CLSMail.DecodificaQuotedPrintable: " & ex.ToString())
            Return valore
        End Try
    End Function

    Private Function PrimoValoreVCard(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim testo As String = valore.Trim()
            Dim posizione As Integer = testo.IndexOf(","c)
            If posizione > 0 Then testo = testo.Substring(0, posizione)
            Return testo.Trim()
        Catch ex As Exception
            ScriviLog("CLSMail.PrimoValoreVCard: " & ex.ToString())
            Return valore
        End Try
    End Function

    Private Function NomeDaCampoVCard(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim parti() As String = valore.Split(";"c)
            Dim cognome As String = ""
            Dim nome As String = ""
            If parti.Length > 0 Then cognome = parti(0).Trim()
            If parti.Length > 1 Then nome = parti(1).Trim()
            Return (nome & " " & cognome).Trim()
        Catch ex As Exception
            ScriviLog("CLSMail.NomeDaCampoVCard: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Analisi essenziale di un evento iCalendar (VEVENT): UID, SUMMARY, DTSTART, DTEND,
    ' LOCATION, DESCRIPTION. Non gestisce ricorrenze (RRULE) ne' fusi orari complessi: la data
    ' viene salvata cosi' come arriva dal server (formato AAAAMMSSTHHMMSS o AAAAMMGG).
    Private Function AnalizzaEventoIcs(testo As String) As Dictionary(Of String, String)
        Try
            Dim campi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim righe As List(Of String) = SciogliRigheContinuazione(testo)
            Dim dentroEvento As Boolean = False

            For Each riga As String In righe
                If riga.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) Then
                    dentroEvento = True
                    Continue For
                End If
                If riga.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase) Then
                    Exit For
                End If
                If Not dentroEvento Then Continue For

                Dim posizioneDuePunti As Integer = riga.IndexOf(":"c)
                If posizioneDuePunti <= 0 Then Continue For

                Dim chiaveGrezza As String = riga.Substring(0, posizioneDuePunti)
                Dim valore As String = riga.Substring(posizioneDuePunti + 1).Trim()
                Dim chiave As String = chiaveGrezza.Split(";"c)(0).Trim().ToUpperInvariant()

                If chiave = "UID" OrElse chiave = "SUMMARY" OrElse chiave = "DTSTART" OrElse chiave = "DTEND" OrElse chiave = "LOCATION" OrElse chiave = "DESCRIPTION" Then
                    If Not campi.ContainsKey(chiave) Then campi(chiave) = valore
                End If
            Next

            Return campi
        Catch ex As Exception
            ScriviLog("CLSMail.AnalizzaEventoIcs: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Le righe di vCard/iCalendar possono continuare sulla riga successiva se questa comincia
    ' con uno spazio o un tab (RFC 5545/6350 "line folding"): qui le ricomponiamo su un'unica riga.
    Private Function SciogliRigheContinuazione(testo As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Dim righeGrezze() As String = testo.Replace(vbCrLf, vbLf).Split(vbLf)
            For Each riga As String In righeGrezze
                If riga.Length > 0 AndAlso (riga(0) = " "c OrElse riga(0) = vbTab(0)) AndAlso risultato.Count > 0 Then
                    risultato(risultato.Count - 1) &= riga.Substring(1)
                Else
                    If Not String.IsNullOrWhiteSpace(riga) Then risultato.Add(riga.TrimEnd(vbCr(0)))
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.SciogliRigheContinuazione: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function OttieniValore(campi As Dictionary(Of String, String), chiave As String) As String
        Try
            Dim valore As String = ""
            If campi IsNot Nothing AndAlso campi.TryGetValue(chiave, valore) Then Return valore
            Return ""
        Catch
            Return ""
        End Try
    End Function

    ' ============================================================================================
    ' DISCOVERY CONFIGURAZIONE PROVIDER
    ' ============================================================================================
    ' Stesso ordine di priorita' di Thunderbird (FindConfig / FetchConfig): prima la
    ' configurazione pubblicata dal dominio, poi l'ISPDB Mozilla, poi i record MX (il dominio
    ' personalizzato ospitato da Aruba, Google Workspace o Microsoft 365 si riconosce dall'host
    ' MX e si riusa la configurazione di quell'hoster), poi Exchange Autodiscover, i record SRV
    ' e infine gli host per convenzione provati con una connessione vera.
    ' I provider italiani che non pubblicano nulla (Aruba, Libero, Virgilio, Alice/TIM, Tiscali,
    ' Fastweb, Register.it) restano nella tabella scritta a mano, applicata anche al dominio
    ' base dell'MX: cosi' posta@miodominio.it ospitata su Aruba prende imaps.aruba.it.
    ' Ogni livello aggiorna SOLO i campi ancora vuoti. Una risposta HTTP che non e' un XML
    ' clientConfig non blocca i tentativi successivi. Compila ServerImap/Porta/Sicurezza e
    ' ServerSmtp/Porta/Sicurezza; non tocca password né nome, salvo i segnaposto autoconfig
    ' (%EMAILADDRESS%, %EMAILLOCALPART%, %EMAILDOMAIN%).
    Public Function ScopriConfigurazioneProvider(indirizzoEmail As String) As ImpostazioniAccountMail
        Dim risultato As New ImpostazioniAccountMail()
        Try
            If String.IsNullOrWhiteSpace(indirizzoEmail) OrElse Not indirizzoEmail.Contains("@") Then Return risultato

            Dim dominio As String = indirizzoEmail.Substring(indirizzoEmail.IndexOf("@"c) + 1).Trim().ToLowerInvariant()
            If String.IsNullOrWhiteSpace(dominio) Then Return risultato

            Dim emailPulita As String = indirizzoEmail.Trim()
            risultato.IndirizzoEmail = emailPulita
            risultato.NomeUtente = emailPulita

            ApplicaFontiAutoconfig(risultato, dominio, emailPulita)

            Dim dominioBase As String = DominioOrganizzativo(dominio)
            If ServerRicezioneOSmtpMancanti(risultato) AndAlso
               Not String.Equals(dominioBase, dominio, StringComparison.OrdinalIgnoreCase) Then
                ApplicaFontiAutoconfig(risultato, dominioBase, emailPulita)
            End If

            Dim dominioMx As String = ""
            If ServerRicezioneOSmtpMancanti(risultato) Then
                dominioMx = ApplicaConfigurazioneDaMx(risultato, dominio, emailPulita)
            End If

            If ServerRicezioneOSmtpMancanti(risultato) Then
                ApplicaProviderCanonico(risultato, dominio, dominioMx)
            End If

            If ServerRicezioneOSmtpMancanti(risultato) Then
                ApplicaProviderNotoDaTabella(risultato, dominio)
            End If
            If ServerRicezioneOSmtpMancanti(risultato) AndAlso
               Not String.Equals(dominioBase, dominio, StringComparison.OrdinalIgnoreCase) Then
                ApplicaProviderNotoDaTabella(risultato, dominioBase)
            End If

            If ServerRicezioneOSmtpMancanti(risultato) Then
                ApplicaAutodiscoverExchange(risultato, dominio, emailPulita)
            End If
            If ServerRicezioneOSmtpMancanti(risultato) Then
                ApplicaConfigurazioneDaSrv(risultato, dominio)
            End If
            If ServerRicezioneOSmtpMancanti(risultato) Then
                ApplicaConfigurazioneIndovinata(risultato, dominio, dominioMx)
            End If

            ' CardDAV: standard aperto gratuito, senza registrazioni app
            risultato.UrlRubricaCardDav = ScopriUrlCardDav(indirizzoEmail.Trim())
            If String.IsNullOrWhiteSpace(risultato.UrlRubricaCardDav) AndAlso ServerSembraAruba(risultato.ServerImap) Then
                risultato.UrlRubricaCardDav = "https://syncdav.aruba.it"
            End If
            If String.IsNullOrWhiteSpace(risultato.UrlRubricaCardDav) AndAlso ServerSembraAruba(risultato.ServerPop3) Then
                risultato.UrlRubricaCardDav = "https://syncdav.aruba.it"
            End If
            If String.IsNullOrWhiteSpace(risultato.UrlRubricaCardDav) AndAlso ServerSembraAruba(risultato.ServerSmtp) Then
                risultato.UrlRubricaCardDav = "https://syncdav.aruba.it"
            End If

            ' CalDAV: stessa logica della rubrica, per l'agenda
            risultato.UrlAgendaCalDav = ScopriUrlCalDav(indirizzoEmail.Trim())
            If String.IsNullOrWhiteSpace(risultato.UrlAgendaCalDav) AndAlso ServerSembraAruba(risultato.ServerImap) Then
                risultato.UrlAgendaCalDav = "https://syncdav.aruba.it"
            End If
            If String.IsNullOrWhiteSpace(risultato.UrlAgendaCalDav) AndAlso ServerSembraAruba(risultato.ServerSmtp) Then
                risultato.UrlAgendaCalDav = "https://syncdav.aruba.it"
            End If

            risultato.SincronizzaRubrica = Not String.IsNullOrWhiteSpace(risultato.UrlRubricaCardDav)
        Catch ex As Exception
            ScriviLog("CLSMail.ScopriConfigurazioneProvider (" & indirizzoEmail & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Riconosce gli host dell'infrastruttura di posta Aruba, che espone CardDAV e CalDAV su
    ''' syncdav.aruba.it anche per i domini personalizzati privi di record well-known propri.
    ''' </summary>
    Private Function ServerSembraAruba(host As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(host) Then Return False
            Dim h As String = host.Trim().ToLowerInvariant()
            If h.IndexOf("aruba", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If h.IndexOf("arubabusiness", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.ServerSembraAruba (" & If(host, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Scopre l'URL CalDAV dell'agenda a partire dall'indirizzo email, con la stessa strategia
    ''' usata per la rubrica: prima i modelli dei provider noti, poi /.well-known/caldav.
    ''' </summary>
    Public Function ScopriUrlCalDav(indirizzoEmail As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzoEmail) OrElse Not indirizzoEmail.Contains("@") Then Return ""
            Dim email As String = indirizzoEmail.Trim().ToLowerInvariant()
            Dim dominio As String = email.Substring(email.IndexOf("@"c) + 1).Trim()
            If String.IsNullOrWhiteSpace(dominio) Then Return ""

            Select Case dominio
                Case "outlook.com", "hotmail.com", "live.com", "msn.com", "office365.com", "outlook.it", "hotmail.it"
                    Return ""
                Case "gmail.com", "googlemail.com"
                    Return ""
            End Select
            If dominio.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase) Then Return ""

            Dim noto As String = UrlCalDavProviderNoto(email, dominio)
            If Not String.IsNullOrWhiteSpace(noto) Then Return noto

            Dim well As String = SeguiWellKnownDav("https://" & dominio & "/.well-known/caldav", ".well-known/caldav")
            If Not String.IsNullOrWhiteSpace(well) Then Return well
            well = SeguiWellKnownDav("http://" & dominio & "/.well-known/caldav", ".well-known/caldav")
            If Not String.IsNullOrWhiteSpace(well) Then Return well

            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.ScopriUrlCalDav (" & indirizzoEmail & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function UrlCalDavProviderNoto(email As String, dominio As String) As String
        Try
            Select Case dominio
                ' Google non e' incluso: dal 2013 non accetta piu' CalDAV con utente/password
                ' (serve OAuth 2.0), quindi per questi account la sincronizzazione dell'agenda
                ' non e' semplicemente disponibile in questa app.
                Case "yahoo.com", "ymail.com", "yahoo.it"
                    Return "https://caldav.calendar.yahoo.com/dav/" & email & "/Calendar/"
                Case "fastmail.com", "fastmail.fm"
                    Return "https://caldav.fastmail.com/dav/calendars/user/" & email & "/Default"
                Case "mailbox.org"
                    Return "https://dav.mailbox.org/caldav"
                Case "posteo.de", "posteo.net"
                    Return "https://posteo.de:8443/calendars/" & email.Split("@"c)(0)
                Case "gmx.com", "gmx.net", "gmx.de", "gmx.it"
                    Return "https://caldav.gmx.net/"
                Case "icloud.com", "me.com", "mac.com"
                    Return "https://caldav.icloud.com/"
                Case "aruba.it", "arubapec.it", "pec.it"
                    Return "https://syncdav.aruba.it"
            End Select
            If dominio.EndsWith(".aruba.it", StringComparison.OrdinalIgnoreCase) Then
                Return "https://syncdav.aruba.it"
            End If
            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.UrlCalDavProviderNoto (" & If(dominio, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' True se l'account ha un endpoint CalDAV utilizzabile per l'agenda.
    ''' </summary>
    Public Function AccountSupportaAgenda(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            If AccountEGoogle(impostazioni) Then Return False
            Return Not String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav)
        Catch ex As Exception
            ScriviLog("CLSMail.AccountSupportaAgenda: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Verifica con le credenziali dell'account che l'endpoint indicato risponda davvero al
    ''' protocollo WebDAV (PROPFIND autenticato). Serve a non attivare una sincronizzazione
    ''' automatica verso un indirizzo scoperto ma inutilizzabile.
    ''' </summary>
    Public Function VerificaEndpointDav(url As String, utente As String, password As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False
            Return EseguiPropfindWebDav(url, utente, password)
        Catch ex As Exception
            ScriviLog("CLSMail.VerificaEndpointDav (" & If(url, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function EseguiPropfindWebDav(url As String, utente As String, password As String) As Boolean
        Try
            Dim corpo As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:""><D:prop><D:current-user-principal/><D:resourcetype/></D:prop></D:propfind>"

            Dim ultimoCodice As Integer = 0
            For Each partenza As String In PuntiPartenzaDav(url)
                Dim risposta As RispostaDav = EseguiRichiestaDav("PROPFIND", partenza, utente, password, corpo, "text/xml", "0")
                If risposta Is Nothing Then Continue For
                ultimoCodice = risposta.CodiceStato
                If risposta.CodiceStato = 207 OrElse risposta.CodiceStato = 200 Then Return True
            Next

            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiPropfindWebDav (" & If(url, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AzzeraCacheDav()
        Try
            _cacheCollezioniDav.Clear()
        Catch ex As Exception
            ScriviLog("CLSMail.AzzeraCacheDav: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Completa in automatico la parte di sincronizzazione delle impostazioni: se gli URL
    ''' CardDAV e CalDAV non sono gia' presenti li cerca, poi li prova con le credenziali
    ''' dell'account e scarta quelli che non rispondono. Attiva SincronizzaRubrica solo se la
    ''' rubrica e' realmente raggiungibile. Restituisce True se la rubrica risulta utilizzabile.
    ''' </summary>
    Public Function ConfiguraSincronizzazioneAutomatica(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False

            If String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) Then
                impostazioni.UrlRubricaCardDav = ScopriUrlCardDav(impostazioni.IndirizzoEmail)
                If String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) AndAlso ServerSembraAruba(impostazioni.ServerImap) Then
                    impostazioni.UrlRubricaCardDav = "https://syncdav.aruba.it"
                End If
                If String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) AndAlso ServerSembraAruba(impostazioni.ServerSmtp) Then
                    impostazioni.UrlRubricaCardDav = "https://syncdav.aruba.it"
                End If
            End If

            If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then
                impostazioni.UrlAgendaCalDav = ScopriUrlCalDav(impostazioni.IndirizzoEmail)
                If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) AndAlso ServerSembraAruba(impostazioni.ServerImap) Then
                    impostazioni.UrlAgendaCalDav = "https://syncdav.aruba.it"
                End If
                If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) AndAlso ServerSembraAruba(impostazioni.ServerSmtp) Then
                    impostazioni.UrlAgendaCalDav = "https://syncdav.aruba.it"
                End If
            End If

            AzzeraCacheDav()

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            If Not String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) Then
                If Not VerificaEndpointDav(impostazioni.UrlRubricaCardDav, utente, password) Then
                    impostazioni.UrlRubricaCardDav = ""
                End If
            End If

            If Not String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then
                If Not VerificaEndpointDav(impostazioni.UrlAgendaCalDav, utente, password) Then
                    impostazioni.UrlAgendaCalDav = ""
                End If
            End If

            impostazioni.SincronizzaRubrica = Not String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav)
            Return impostazioni.SincronizzaRubrica
        Catch ex As Exception
            ScriviLog("CLSMail.ConfiguraSincronizzazioneAutomatica: " & ex.ToString())
            Return False
        End Try
    End Function

    ' True se l'account ha un endpoint CardDAV utilizzabile (URL non vuoto).
    ' Microsoft Graph non e' incluso: richiede registrazione app Azure.
    ' Google non e' incluso: dal 2013 non accetta piu' CardDAV con utente/password, quindi per
    ' questi account la sincronizzazione della rubrica non e' semplicemente disponibile.
    Public Function AccountSupportaRubrica(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            If AccountEGoogle(impostazioni) Then Return False
            If Not String.IsNullOrWhiteSpace(impostazioni.UrlRubricaCardDav) Then Return True
            Return False
        Catch
            Return False
        End Try
    End Function

    Public Function AccountSupportaRubrica(chiaveAccount As String) As Boolean
        Try
            Return AccountSupportaRubrica(OttieniAccount(chiaveAccount))
        Catch
            Return False
        End Try
    End Function

    ' Almeno un account attivo con CardDAV configurato.
    Public Function EsisteAccountConRubrica() As Boolean
        Try
            For Each a As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If AccountSupportaRubrica(a) Then Return True
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EsisteAccountConRubrica: " & ex.ToString())
        End Try
        Return False
    End Function

    Public Function OttieniAccountConRubrica(Optional soloAttivi As Boolean = True) As List(Of ImpostazioniAccountMail)
        Dim risultato As New List(Of ImpostazioniAccountMail)
        Try
            For Each a As ImpostazioniAccountMail In OttieniElencoAccount(soloAttivi)
                If AccountSupportaRubrica(a) Then risultato.Add(a)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniAccountConRubrica: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' Scopre l'URL CardDAV senza registrazioni a pagamento e senza app Azure.
    ' 1) modelli noti (Yahoo, Fastmail, mailbox.org, ...)
    ' 2) /.well-known/carddav sul dominio
    ' Per Google e per outlook.com / hotmail / live / office365 restituisce sempre vuoto: nessuno
    ' dei due accetta CardDAV con semplice utente/password (serve OAuth2/Graph + registrazione app).
    Public Function ScopriUrlCardDav(indirizzoEmail As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzoEmail) OrElse Not indirizzoEmail.Contains("@") Then Return ""
            Dim email As String = indirizzoEmail.Trim().ToLowerInvariant()
            Dim dominio As String = email.Substring(email.IndexOf("@"c) + 1).Trim()
            If String.IsNullOrWhiteSpace(dominio) Then Return ""

            ' Provider Google e Microsoft: niente CardDAV pubblico gratuito senza OAuth/Graph
            Select Case dominio
                Case "outlook.com", "hotmail.com", "live.com", "msn.com", "office365.com", "outlook.it", "hotmail.it"
                    Return ""
                Case "gmail.com", "googlemail.com"
                    Return ""
            End Select
            If dominio.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase) Then Return ""

            Dim noto As String = UrlCardDavProviderNoto(email, dominio)
            If Not String.IsNullOrWhiteSpace(noto) Then Return noto

            Dim well As String = SeguiWellKnownCardDav("https://" & dominio & "/.well-known/carddav")
            If Not String.IsNullOrWhiteSpace(well) Then Return well
            well = SeguiWellKnownCardDav("http://" & dominio & "/.well-known/carddav")
            If Not String.IsNullOrWhiteSpace(well) Then Return well

            Return ""
        Catch ex As Exception
            ScriviLog("CLSMail.ScopriUrlCardDav (" & indirizzoEmail & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function UrlCardDavProviderNoto(email As String, dominio As String) As String
        Try
            Select Case dominio
                ' Google non e' incluso: dal 2013 non accetta piu' CardDAV con utente/password
                ' (serve OAuth 2.0), quindi per questi account la sincronizzazione della rubrica
                ' non e' semplicemente disponibile in questa app.
                Case "yahoo.com", "ymail.com", "yahoo.it"
                    Return "https://carddav.address.yahoo.com/dav/" & email & "/Contacts/"
                Case "fastmail.com", "fastmail.fm"
                    Return "https://carddav.fastmail.com/dav/addressbooks/user/" & email & "/Default"
                Case "mailbox.org"
                    Return "https://dav.mailbox.org/carddav"
                Case "posteo.de", "posteo.net"
                    Return "https://posteo.de:8443/addressbooks/" & email.Split("@"c)(0)
                Case "gmx.com", "gmx.net", "gmx.de", "gmx.it"
                    Return "https://carddav.gmx.net/"
                Case "icloud.com", "me.com", "mac.com"
                    Return "https://contacts.icloud.com/"
                Case "aruba.it", "arubapec.it", "pec.it"
                    ' Aruba documenta ufficialmente CardDAV su syncdav.aruba.it (guide.aruba.it)
                    Return "https://syncdav.aruba.it"
            End Select
            ' Domini personalizzati ospitati su Aruba: spesso non hanno well-known proprio
            If dominio.EndsWith(".aruba.it", StringComparison.OrdinalIgnoreCase) Then
                Return "https://syncdav.aruba.it"
            End If
            Return ""
        Catch
            Return ""
        End Try
    End Function

    Private Function SeguiWellKnownCardDav(url As String) As String
        Try
            Return SeguiWellKnownDav(url, ".well-known/carddav")
        Catch ex As Exception
            ScriviLog("CLSMail.SeguiWellKnownCardDav (" & If(url, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Segue i redirect di un indirizzo /.well-known e restituisce l'URL finale se il server
    ''' risponde in modo compatibile con WebDAV. Il marcatore indica quale percorso well-known
    ''' e' stato richiesto, per scartare le risposte rimaste sul percorso di partenza.
    ''' </summary>
    Private Function SeguiWellKnownDav(url As String, marcatore As String) As String
        Try
            ConfiguraReteDav()
            Using gestore As New HttpClientHandler()
                gestore.AllowAutoRedirect = True
                Using client As New HttpClient(gestore)
                    client.Timeout = TimeSpan.FromSeconds(10)
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                        richiesta.Headers.TryAddWithoutValidation("User-Agent", "WinGabriel/1.0 (macOS; CardDAV)")
                        Using risposta As HttpResponseMessage = client.SendAsync(richiesta).GetAwaiter().GetResult()
                            Dim finale As String = ""
                            If risposta.RequestMessage IsNot Nothing AndAlso risposta.RequestMessage.RequestUri IsNot Nothing Then
                                finale = risposta.RequestMessage.RequestUri.ToString()
                            End If
                            If String.IsNullOrWhiteSpace(finale) Then finale = url

                            Dim codice As Integer = CInt(risposta.StatusCode)
                            If codice <> 200 AndAlso codice <> 401 AndAlso codice <> 403 AndAlso codice <> 405 AndAlso codice <> 207 Then Return ""

                            If finale.IndexOf(marcatore, StringComparison.OrdinalIgnoreCase) >= 0 Then
                                Dim radice As String = RadiceUrl(finale)
                                If String.IsNullOrWhiteSpace(radice) Then radice = RadiceUrl(url)
                                Return radice
                            End If

                            Return finale.TrimEnd("/"c)
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.SeguiWellKnownDav (" & If(url, "") & "): " & ex.Message)
        End Try
        Return ""
    End Function

    Private Function ScaricaAutoconfig(url As String) As String
        Try
            Using gestore As New HttpClientHandler()
                gestore.AllowAutoRedirect = True
                Using client As New HttpClient(gestore)
                    client.Timeout = TimeSpan.FromSeconds(8)
                    Dim risposta As HttpResponseMessage = client.GetAsync(url).GetAwaiter().GetResult()
                    If Not risposta.IsSuccessStatusCode Then Return ""
                    Return risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaAutoconfig (" & url & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ApplicaAutoconfigXml(impostazioni As ImpostazioniAccountMail, xml As String, email As String)
        Try
            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(xml)

            Dim trovatoImap As Boolean = False
            Dim trovatoPop3 As Boolean = False
            For Each nodo As XmlNode In documento.SelectNodes("//*[local-name()='incomingServer']")
                Dim tipo As String = If(nodo.Attributes("type") IsNot Nothing, nodo.Attributes("type").Value, "")
                If String.Equals(tipo, "imap", StringComparison.OrdinalIgnoreCase) AndAlso Not trovatoImap AndAlso
                   String.IsNullOrWhiteSpace(impostazioni.ServerImap) Then
                    impostazioni.ServerImap = TestoNodoLocale(nodo, "hostname")
                    Dim portaTesto As String = TestoNodoLocale(nodo, "port")
                    Dim porta As Integer = 993
                    Integer.TryParse(portaTesto, porta)
                    If porta > 0 Then impostazioni.PortaImap = porta
                    impostazioni.SicurezzaImap = InterpretaSocketType(TestoNodoLocale(nodo, "socketType"))
                    Dim username As String = TestoNodoLocale(nodo, "username")
                    If Not String.IsNullOrWhiteSpace(username) Then
                        impostazioni.NomeUtente = EspandiSegnapostoAutoconfig(username, email)
                    End If
                    impostazioni.ProtocolloRicezione = "IMAP"
                    trovatoImap = True
                ElseIf String.Equals(tipo, "pop3", StringComparison.OrdinalIgnoreCase) AndAlso Not trovatoPop3 AndAlso
                       String.IsNullOrWhiteSpace(impostazioni.ServerPop3) Then
                    impostazioni.ServerPop3 = TestoNodoLocale(nodo, "hostname")
                    Dim portaTestoP As String = TestoNodoLocale(nodo, "port")
                    Dim portaP As Integer = 995
                    Integer.TryParse(portaTestoP, portaP)
                    If portaP > 0 Then impostazioni.PortaPop3 = portaP
                    impostazioni.SicurezzaPop3 = InterpretaSocketType(TestoNodoLocale(nodo, "socketType"))
                    If String.IsNullOrWhiteSpace(impostazioni.NomeUtente) OrElse
                       String.Equals(impostazioni.NomeUtente, email, StringComparison.OrdinalIgnoreCase) Then
                        Dim usernameP As String = TestoNodoLocale(nodo, "username")
                        If Not String.IsNullOrWhiteSpace(usernameP) Then
                            impostazioni.NomeUtente = EspandiSegnapostoAutoconfig(usernameP, email)
                        End If
                    End If
                    trovatoPop3 = True
                End If
            Next
            ' Se c'è solo POP3 (niente IMAP) usa POP3 come protocollo di ricezione
            If Not trovatoImap AndAlso trovatoPop3 Then
                impostazioni.ProtocolloRicezione = "POP3"
                If String.IsNullOrWhiteSpace(impostazioni.ServerImap) Then impostazioni.ServerImap = impostazioni.ServerPop3
            End If

            For Each nodo As XmlNode In documento.SelectNodes("//*[local-name()='outgoingServer']")
                Dim tipo As String = If(nodo.Attributes("type") IsNot Nothing, nodo.Attributes("type").Value, "")
                If Not String.Equals(tipo, "smtp", StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) Then Exit For

                impostazioni.ServerSmtp = TestoNodoLocale(nodo, "hostname")
                Dim portaTesto As String = TestoNodoLocale(nodo, "port")
                Dim porta As Integer = 587
                Integer.TryParse(portaTesto, porta)
                If porta > 0 Then impostazioni.PortaSmtp = porta
                impostazioni.SicurezzaSmtp = InterpretaSocketType(TestoNodoLocale(nodo, "socketType"))
                Exit For
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaAutoconfigXml: " & ex.ToString())
        End Try
    End Sub

    Private Function TestoNodoLocale(padre As XmlNode, nomeLocale As String) As String
        Try
            For Each figlio As XmlNode In padre.ChildNodes
                If String.Equals(figlio.LocalName, nomeLocale, StringComparison.OrdinalIgnoreCase) Then
                    Return If(figlio.InnerText, "").Trim()
                End If
            Next
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' True se manca ancora almeno un server di ricezione (IMAP o POP3) oppure il server SMTP.
    ''' Usata tra un livello e l'altro della discovery per capire se vale la pena provare il
    ''' livello successivo, e per non sovrascrivere mai un campo gia' trovato.
    ''' </summary>
    Private Function ServerRicezioneOSmtpMancanti(impostazioni As ImpostazioniAccountMail) As Boolean
        Return String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) OrElse
              (String.IsNullOrWhiteSpace(impostazioni.ServerImap) AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerPop3))
    End Function

    ' Suffissi pubblici a due etichette piu' comuni: senza di questi "mail.bbc.co.uk"
    ' diventerebbe "co.uk" e la ricerca autoconfig andrebbe sul suffisso, non sul dominio.
    Private Shared ReadOnly _suffissiPubbliciDoppi As String() = {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "ltd.uk", "plc.uk", "me.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "net.nz", "co.jp", "ne.jp", "or.jp", "ac.jp",
        "com.br", "com.mx", "com.ar", "com.tr", "com.sg", "com.hk", "com.tw",
        "co.in", "net.in", "org.in", "co.za", "co.il", "co.kr", "com.cn", "com.ua", "com.pl"
    }

    ''' <summary>
    ''' Dominio organizzativo (eTLD+1) di un host: "mx1.mail.example.co.uk" diventa "example.co.uk",
    ''' "aspmx.l.google.com" diventa "google.com". E' l'analogo di Services.eTLD.getBaseDomainFromHost
    ''' usato da Thunderbird prima di interrogare l'ISPDB sull'host dell'MX.
    ''' </summary>
    Private Function DominioOrganizzativo(host As String) As String
        Try
            If String.IsNullOrWhiteSpace(host) Then Return ""
            Dim h As String = host.Trim().Trim("."c).ToLowerInvariant()
            Dim parti() As String = h.Split("."c)
            If parti.Length <= 2 Then Return h
            Dim ultimiDue As String = parti(parti.Length - 2) & "." & parti(parti.Length - 1)
            Dim profondita As Integer = 2
            For Each suffisso As String In _suffissiPubbliciDoppi
                If String.Equals(ultimiDue, suffisso, StringComparison.OrdinalIgnoreCase) Then
                    profondita = 3
                    Exit For
                End If
            Next
            If parti.Length <= profondita Then Return h
            Return String.Join(".", parti.Skip(parti.Length - profondita))
        Catch ex As Exception
            ScriviLog("CLSMail.DominioOrganizzativo (" & If(host, "") & "): " & ex.Message)
            Return If(host, "").Trim().Trim("."c).ToLowerInvariant()
        End Try
    End Function

    ' Un'etichetta in meno dell'host MX: "contoso.mail.protection.outlook.com" diventa
    ' "mail.protection.outlook.com", che nell'ISPDB e' la voce di Microsoft 365 e non quella
    ' di outlook.com (stessa distinzione di getNextSubDomain in Thunderbird).
    Private Function DominioSottoMx(host As String) As String
        Try
            If String.IsNullOrWhiteSpace(host) Then Return ""
            Dim h As String = host.Trim().Trim("."c).ToLowerInvariant()
            Dim punto As Integer = h.IndexOf("."c)
            If punto <= 0 OrElse punto >= h.Length - 1 Then Return ""
            Return h.Substring(punto + 1)
        Catch
            Return ""
        End Try
    End Function

    Private Function EspandiSegnapostoAutoconfig(modello As String, email As String) As String
        Try
            If String.IsNullOrWhiteSpace(modello) Then Return ""
            Dim indirizzo As String = If(email, "").Trim()
            Dim dominio As String = ""
            If indirizzo.Contains("@") Then dominio = indirizzo.Substring(indirizzo.IndexOf("@"c) + 1)
            Return modello.Replace("%EMAILADDRESS%", indirizzo).
                Replace("%EMAILLOCALPART%", ParteLocaleEmail(indirizzo)).
                Replace("%EMAILDOMAIN%", dominio)
        Catch
            Return If(modello, "")
        End Try
    End Function

    Private Function SembraAutoconfig(xml As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(xml) Then Return False
            Return xml.IndexOf("incomingServer", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   xml.IndexOf("clientConfig", StringComparison.OrdinalIgnoreCase) >= 0
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Sorgenti autoconfig di Thunderbird per un dominio, in ordine di priorita':
    ''' autoconfig.&lt;dominio&gt; https, /.well-known https, le stesse in http, poi l'ISPDB Mozilla.
    ''' L'indirizzo viene passato in query (emailaddress=) come fa Thunderbird: alcuni hoster
    ''' rispondono in modo diverso per le caselle ospitate. Un XML che non e' un clientConfig
    ''' viene ignorato e si prova l'URL successivo.
    ''' </summary>
    Private Sub ApplicaFontiAutoconfig(impostazioni As ImpostazioniAccountMail, dominio As String, email As String)
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) Then Return
            If Not ServerRicezioneOSmtpMancanti(impostazioni) Then Return

            Dim indirizzo As String = If(email, "").Trim()
            Dim query As String = ""
            If Not String.IsNullOrWhiteSpace(indirizzo) Then query = "?emailaddress=" & Uri.EscapeDataString(indirizzo)

            Dim url() As String = {
                "https://autoconfig." & dominio & "/mail/config-v1.1.xml" & query,
                "https://" & dominio & "/.well-known/autoconfig/mail/config-v1.1.xml" & query,
                "http://autoconfig." & dominio & "/mail/config-v1.1.xml" & query,
                "http://" & dominio & "/.well-known/autoconfig/mail/config-v1.1.xml" & query,
                "https://autoconfig.thunderbird.net/v1.1/" & dominio
            }

            For Each candidato As String In url
                If Not ServerRicezioneOSmtpMancanti(impostazioni) Then Exit For
                Dim xmlConfig As String = ScaricaAutoconfig(candidato)
                If Not SembraAutoconfig(xmlConfig) Then Continue For
                ApplicaAutoconfigXml(impostazioni, xmlConfig, indirizzo)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaFontiAutoconfig (" & If(dominio, "") & "): " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Thunderbird fetchConfigForMX: legge l'MX del dominio, ricava il dominio dell'hoster
    ''' e cerca la configurazione di quello (ISPDB, autoconfig e tabella dei provider noti).
    ''' Ritorna il dominio base dell'MX, utile all'ultimo tentativo per convenzione.
    ''' </summary>
    Private Function ApplicaConfigurazioneDaMx(impostazioni As ImpostazioniAccountMail, dominio As String, email As String) As String
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) Then Return ""
            Dim json As String = InterrogaDnsOverHttps(dominio, "MX")
            Dim hostMx As String = MigliorHostMx(json)
            If String.IsNullOrWhiteSpace(hostMx) Then Return ""

            Dim baseMx As String = DominioOrganizzativo(hostMx)
            Dim sottoMx As String = DominioSottoMx(hostMx)
            Dim giaProvati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            giaProvati.Add(dominio.Trim().ToLowerInvariant())
            Dim baseDominio As String = DominioOrganizzativo(dominio)
            If Not String.IsNullOrWhiteSpace(baseDominio) Then giaProvati.Add(baseDominio)

            Dim candidati As New List(Of String)
            If Not String.IsNullOrWhiteSpace(sottoMx) AndAlso Not String.Equals(sottoMx, baseMx, StringComparison.OrdinalIgnoreCase) Then
                candidati.Add(sottoMx)
            End If
            If Not String.IsNullOrWhiteSpace(baseMx) Then candidati.Add(baseMx)

            For Each candidato As String In candidati
                If giaProvati.Contains(candidato) Then Continue For
                giaProvati.Add(candidato)
                If Not ServerRicezioneOSmtpMancanti(impostazioni) Then Exit For
                ApplicaFontiAutoconfig(impostazioni, candidato, email)
                If ServerRicezioneOSmtpMancanti(impostazioni) Then ApplicaProviderNotoDaTabella(impostazioni, candidato)
            Next

            Return If(baseMx, "")
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaConfigurazioneDaMx (" & If(dominio, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function MigliorHostMx(json As String) As String
        Try
            If String.IsNullOrWhiteSpace(json) Then Return ""
            Dim migliore As String = ""
            Dim migliorePriorita As Integer = Integer.MaxValue
            For Each trovato As Match In Regex.Matches(json, """data""\s*:\s*""(\d+)\s+([^""\s]+)")
                Dim priorita As Integer
                If Not Integer.TryParse(trovato.Groups(1).Value, priorita) Then Continue For
                Dim host As String = trovato.Groups(2).Value.Trim().Trim("."c)
                If String.IsNullOrWhiteSpace(host) Then Continue For
                If migliore.Length = 0 OrElse priorita < migliorePriorita Then
                    migliore = host
                    migliorePriorita = priorita
                End If
            Next
            Return migliore
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Google Workspace e Microsoft 365, anche su dominio personalizzato: se l'MX (o il dominio
    ''' stesso) e' di uno dei due, si compilano i server che Thunderbird mette nell'ISPDB.
    ''' </summary>
    Private Sub ApplicaProviderCanonico(impostazioni As ImpostazioniAccountMail, dominio As String, dominioMx As String)
        Try
            If impostazioni Is Nothing OrElse Not ServerRicezioneOSmtpMancanti(impostazioni) Then Return

            Dim tipo As TipoProviderMail = RilevaProviderDaHost(dominio)
            If tipo = TipoProviderMail.Sconosciuto Then tipo = RilevaProviderDaHost(dominioMx)
            If tipo = TipoProviderMail.Sconosciuto AndAlso String.IsNullOrWhiteSpace(dominioMx) AndAlso Not String.IsNullOrWhiteSpace(dominio) Then
                Dim sonda As New ImpostazioniAccountMail()
                sonda.IndirizzoEmail = "utente@" & dominio
                tipo = RilevaProvider(sonda)
            End If
            If tipo = TipoProviderMail.Sconosciuto Then Return

            Dim c As ConfigurazioneProviderNota = Nothing
            If tipo = TipoProviderMail.Google Then
                c = New ConfigurazioneProviderNota(
                    "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect,
                    "pop.gmail.com", 995, SecureSocketOptions.SslOnConnect,
                    "smtp.gmail.com", 587, SecureSocketOptions.StartTls)
            ElseIf tipo = TipoProviderMail.Microsoft Then
                c = New ConfigurazioneProviderNota(
                    "outlook.office365.com", 993, SecureSocketOptions.SslOnConnect,
                    "outlook.office365.com", 995, SecureSocketOptions.SslOnConnect,
                    "smtp.office365.com", 587, SecureSocketOptions.StartTls)
            End If
            If c Is Nothing Then Return
            CompilaDaConfigurazioneNota(impostazioni, c)
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaProviderCanonico (" & If(dominio, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Function CompilaDaConfigurazioneNota(impostazioni As ImpostazioniAccountMail, c As ConfigurazioneProviderNota) As Boolean
        Try
            If impostazioni Is Nothing OrElse c Is Nothing Then Return False
            Dim applicato As Boolean = False
            If String.IsNullOrWhiteSpace(impostazioni.ServerImap) AndAlso Not String.IsNullOrWhiteSpace(c.ServerImap) Then
                impostazioni.ServerImap = c.ServerImap
                impostazioni.PortaImap = c.PortaImap
                impostazioni.SicurezzaImap = c.SicurezzaImap
                impostazioni.ProtocolloRicezione = "IMAP"
                applicato = True
            End If
            If String.IsNullOrWhiteSpace(impostazioni.ServerPop3) AndAlso Not String.IsNullOrWhiteSpace(c.ServerPop3) Then
                impostazioni.ServerPop3 = c.ServerPop3
                impostazioni.PortaPop3 = c.PortaPop3
                impostazioni.SicurezzaPop3 = c.SicurezzaPop3
                applicato = True
            End If
            If String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) AndAlso Not String.IsNullOrWhiteSpace(c.ServerSmtp) Then
                impostazioni.ServerSmtp = c.ServerSmtp
                impostazioni.PortaSmtp = c.PortaSmtp
                impostazioni.SicurezzaSmtp = c.SicurezzaSmtp
                applicato = True
            End If
            Return applicato
        Catch ex As Exception
            ScriviLog("CLSMail.CompilaDaConfigurazioneNota: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Microsoft Exchange Autodiscover (gli stessi URL di Thunderbird), senza password:
    ''' molti server rispondono comunque con i protocolli IMAP/POP3/SMTP. Se la risposta e'
    ''' solo Exchange/Office 365 si usano i server standard di Microsoft 365.
    ''' </summary>
    Private Function ApplicaAutodiscoverExchange(impostazioni As ImpostazioniAccountMail, dominio As String, email As String) As Boolean
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) OrElse String.IsNullOrWhiteSpace(email) Then Return False
            If Not ServerRicezioneOSmtpMancanti(impostazioni) Then Return False

            Dim corpo As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<Autodiscover xmlns=""http://schemas.microsoft.com/exchange/autodiscover/outlook/requestschema/2006"">" &
                "<Request><EMailAddress>" & System.Security.SecurityElement.Escape(email) & "</EMailAddress>" &
                "<AcceptableResponseSchema>http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a</AcceptableResponseSchema>" &
                "</Request></Autodiscover>"

            Dim url() As String = {
                "https://autodiscover." & dominio & "/autodiscover/autodiscover.xml",
                "https://" & dominio & "/autodiscover/autodiscover.xml"
            }

            For Each candidato As String In url
                Dim xml As String = ScaricaAutodiscover(candidato, corpo)
                If String.IsNullOrWhiteSpace(xml) OrElse xml.IndexOf("Protocol", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                If ApplicaXmlAutodiscover(impostazioni, xml, email) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaAutodiscoverExchange (" & If(dominio, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function ScaricaAutodiscover(url As String, corpo As String) As String
        Try
            Using gestore As New HttpClientHandler()
                gestore.AllowAutoRedirect = True
                Using client As New HttpClient(gestore)
                    client.Timeout = TimeSpan.FromSeconds(8)
                    Using contenuto As New StringContent(corpo, Encoding.UTF8, "text/xml")
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Content = contenuto
                            richiesta.Headers.TryAddWithoutValidation("User-Agent", "WinGabriel/1.0")
                            Using risposta As HttpResponseMessage = client.SendAsync(richiesta).GetAwaiter().GetResult()
                                If Not risposta.IsSuccessStatusCode Then Return ""
                                Return risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaAutodiscover (" & url & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ApplicaXmlAutodiscover(impostazioni As ImpostazioniAccountMail, xml As String, email As String) As Boolean
        Try
            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(xml)

            Dim applicato As Boolean = False
            Dim sembraMicrosoft As Boolean = False
            For Each nodo As XmlNode In documento.SelectNodes("//*[local-name()='Protocol']")
                Dim tipo As String = TestoNodoLocale(nodo, "Type").ToUpperInvariant()
                Dim server As String = TestoNodoLocale(nodo, "Server")
                If String.IsNullOrWhiteSpace(server) Then Continue For
                If server.IndexOf("office365", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   server.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    sembraMicrosoft = True
                End If

                Dim porta As Integer = 0
                Integer.TryParse(TestoNodoLocale(nodo, "Port"), porta)
                Dim sslAttivo As Boolean = String.Equals(TestoNodoLocale(nodo, "SSL"), "on", StringComparison.OrdinalIgnoreCase)
                Dim cifratura As String = TestoNodoLocale(nodo, "Encryption").ToUpperInvariant()
                Dim login As String = TestoNodoLocale(nodo, "LoginName")

                If tipo = "IMAP" AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerImap) Then
                    If porta <= 0 Then porta = If(sslAttivo, 993, 143)
                    impostazioni.ServerImap = server
                    impostazioni.PortaImap = porta
                    impostazioni.SicurezzaImap = SicurezzaDaAutodiscover(sslAttivo, cifratura, porta)
                    impostazioni.ProtocolloRicezione = "IMAP"
                    If Not String.IsNullOrWhiteSpace(login) Then impostazioni.NomeUtente = EspandiSegnapostoAutoconfig(login, email)
                    applicato = True
                ElseIf tipo = "POP3" AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerPop3) Then
                    If porta <= 0 Then porta = If(sslAttivo, 995, 110)
                    impostazioni.ServerPop3 = server
                    impostazioni.PortaPop3 = porta
                    impostazioni.SicurezzaPop3 = SicurezzaDaAutodiscover(sslAttivo, cifratura, porta)
                    applicato = True
                ElseIf tipo = "SMTP" AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) Then
                    If porta <= 0 Then porta = 587
                    impostazioni.ServerSmtp = server
                    impostazioni.PortaSmtp = porta
                    impostazioni.SicurezzaSmtp = SicurezzaDaAutodiscover(sslAttivo, cifratura, porta)
                    applicato = True
                End If
            Next

            If sembraMicrosoft AndAlso ServerRicezioneOSmtpMancanti(impostazioni) Then
                ApplicaProviderCanonico(impostazioni, "outlook.com", "outlook.office365.com")
                applicato = True
            End If
            Return applicato
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaXmlAutodiscover: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function SicurezzaDaAutodiscover(sslAttivo As Boolean, cifratura As String, porta As Integer) As SecureSocketOptions
        Try
            If porta = 993 OrElse porta = 995 OrElse porta = 465 Then Return SecureSocketOptions.SslOnConnect
            If sslAttivo AndAlso (porta = 587 OrElse porta = 143 OrElse porta = 110) Then Return SecureSocketOptions.StartTls
            If cifratura.Contains("TLS") OrElse cifratura.Contains("SSL") Then
                If porta = 587 OrElse porta = 143 OrElse porta = 110 Then Return SecureSocketOptions.StartTls
                Return SecureSocketOptions.SslOnConnect
            End If
            If sslAttivo Then Return SecureSocketOptions.SslOnConnect
            Return SecureSocketOptions.StartTlsWhenAvailable
        Catch
            Return SecureSocketOptions.Auto
        End Try
    End Function

    ' --------------------------------------------------------------------------------------------
    ' Livello 2: tabella scritta a mano dei provider "orfani" (niente ISPDB, niente autoconfig)
    ' --------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Parametri IMAP/POP3/SMTP di un provider che non pubblica ne' un profilo ISPDB ne' un
    ''' autoconfig/well-known proprio. Immutabile: si crea una nuova istanza per ogni provider
    ''' elencato in ApplicaProviderNotoDaTabella.
    ''' </summary>
    Private NotInheritable Class ConfigurazioneProviderNota
        Public ReadOnly ServerImap As String
        Public ReadOnly PortaImap As Integer
        Public ReadOnly SicurezzaImap As SecureSocketOptions
        Public ReadOnly ServerPop3 As String
        Public ReadOnly PortaPop3 As Integer
        Public ReadOnly SicurezzaPop3 As SecureSocketOptions
        Public ReadOnly ServerSmtp As String
        Public ReadOnly PortaSmtp As Integer
        Public ReadOnly SicurezzaSmtp As SecureSocketOptions

        Public Sub New(serverImap As String, portaImap As Integer, sicurezzaImap As SecureSocketOptions,
                       serverPop3 As String, portaPop3 As Integer, sicurezzaPop3 As SecureSocketOptions,
                       serverSmtp As String, portaSmtp As Integer, sicurezzaSmtp As SecureSocketOptions)
            Me.ServerImap = serverImap
            Me.PortaImap = portaImap
            Me.SicurezzaImap = sicurezzaImap
            Me.ServerPop3 = serverPop3
            Me.PortaPop3 = portaPop3
            Me.SicurezzaPop3 = sicurezzaPop3
            Me.ServerSmtp = serverSmtp
            Me.PortaSmtp = portaSmtp
            Me.SicurezzaSmtp = sicurezzaSmtp
        End Sub
    End Class

    ''' <summary>
    ''' Elenco scritto a mano dei provider (soprattutto italiani) che non pubblicano ne' un profilo
    ''' nella Mozilla ISPDB ne' un autoconfig/well-known sul proprio dominio: Aruba e' il caso che
    ''' ha fatto notare il problema, ma vale anche per Libero, Virgilio, Alice/TIM, Tiscali,
    ''' Fastweb e Register.it. Aggiorna SOLO i campi ancora vuoti in "impostazioni" (un risultato
    ''' parziale di ISPDB/autoconfig non viene mai perso). Ritorna True se ha applicato qualcosa.
    ''' Quando si scopre un nuovo provider "orfano" va aggiunto qui un nuovo Case: e' l'unico punto
    ''' della classe da toccare per estendere il riconoscimento a un provider non standard.
    ''' </summary>
    Private Function ApplicaProviderNotoDaTabella(impostazioni As ImpostazioniAccountMail, dominio As String) As Boolean
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) Then Return False
            Dim d As String = dominio.Trim().ToLowerInvariant()

            Dim c As ConfigurazioneProviderNota = Nothing
            Select Case d
                Case "aruba.it", "arubapec.it", "pec.it", "arubapec.com"
                    c = New ConfigurazioneProviderNota(
                        "imaps.aruba.it", 993, SecureSocketOptions.SslOnConnect,
                        "pop3s.aruba.it", 995, SecureSocketOptions.SslOnConnect,
                        "smtps.aruba.it", 465, SecureSocketOptions.SslOnConnect)
                Case "libero.it"
                    c = New ConfigurazioneProviderNota(
                        "imapmail.libero.it", 993, SecureSocketOptions.SslOnConnect,
                        "popmail.libero.it", 995, SecureSocketOptions.SslOnConnect,
                        "smtp.libero.it", 465, SecureSocketOptions.SslOnConnect)
                Case "virgilio.it"
                    c = New ConfigurazioneProviderNota(
                        "in.virgilio.it", 993, SecureSocketOptions.SslOnConnect,
                        "in.virgilio.it", 995, SecureSocketOptions.SslOnConnect,
                        "out.virgilio.it", 465, SecureSocketOptions.SslOnConnect)
                Case "alice.it", "tim.it"
                    c = New ConfigurazioneProviderNota(
                        "in.alice.it", 993, SecureSocketOptions.SslOnConnect,
                        "in.alice.it", 995, SecureSocketOptions.SslOnConnect,
                        "out.alice.it", 465, SecureSocketOptions.SslOnConnect)
                Case "tiscali.it"
                    c = New ConfigurazioneProviderNota(
                        "imap.tiscali.it", 993, SecureSocketOptions.SslOnConnect,
                        "pop.tiscali.it", 995, SecureSocketOptions.SslOnConnect,
                        "smtp.tiscali.it", 465, SecureSocketOptions.SslOnConnect)
                Case "fastwebnet.it", "fastweb.it"
                    c = New ConfigurazioneProviderNota(
                        "imap.fastwebnet.it", 993, SecureSocketOptions.SslOnConnect,
                        "pop.fastwebnet.it", 995, SecureSocketOptions.SslOnConnect,
                        "smtp.fastwebnet.it", 465, SecureSocketOptions.SslOnConnect)
                Case "register.it"
                    c = New ConfigurazioneProviderNota(
                        "imap.register.it", 993, SecureSocketOptions.SslOnConnect,
                        "pop.register.it", 995, SecureSocketOptions.SslOnConnect,
                        "smtp.register.it", 465, SecureSocketOptions.SslOnConnect)
            End Select

            ' Domini personalizzati ospitati su Aruba (PEC comprese) usano la stessa infrastruttura.
            If c Is Nothing AndAlso d.EndsWith(".aruba.it", StringComparison.OrdinalIgnoreCase) Then
                c = New ConfigurazioneProviderNota(
                    "imaps.aruba.it", 993, SecureSocketOptions.SslOnConnect,
                    "pop3s.aruba.it", 995, SecureSocketOptions.SslOnConnect,
                    "smtps.aruba.it", 465, SecureSocketOptions.SslOnConnect)
            End If

            If c Is Nothing Then Return False
            Return CompilaDaConfigurazioneNota(impostazioni, c)
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaProviderNotoDaTabella (" & If(dominio, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' --------------------------------------------------------------------------------------------
    ' Livello 3: record DNS SRV standard (RFC 6186)
    ' --------------------------------------------------------------------------------------------

    ''' <summary>Host e porta risolti da un record SRV.</summary>
    Private NotInheritable Class RecordSrv
        Public ReadOnly Host As String
        Public ReadOnly Porta As Integer
        Public Sub New(host As String, porta As Integer)
            Me.Host = host
            Me.Porta = porta
        End Sub
    End Class

    ''' <summary>
    ''' RFC 6186: alcuni provider (soprattutto hosting piu' recenti) pubblicano direttamente record
    ''' SRV per indicare host e porta di IMAP/POP3/SMTP, senza bisogno di autoconfig ne' di una
    ''' voce scritta a mano nella tabella dei provider noti. Aggiorna SOLO i campi ancora vuoti.
    ''' </summary>
    Private Function ApplicaConfigurazioneDaSrv(impostazioni As ImpostazioniAccountMail, dominio As String) As Boolean
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) Then Return False
            Dim applicato As Boolean = False

            If String.IsNullOrWhiteSpace(impostazioni.ServerImap) Then
                Dim srv As RecordSrv = InterrogaSrv("_imaps._tcp." & dominio)
                Dim sicurezza As SecureSocketOptions = SecureSocketOptions.SslOnConnect
                If srv Is Nothing Then
                    srv = InterrogaSrv("_imap._tcp." & dominio)
                    sicurezza = SecureSocketOptions.StartTls
                End If
                If srv IsNot Nothing Then
                    impostazioni.ServerImap = srv.Host
                    impostazioni.PortaImap = srv.Porta
                    impostazioni.SicurezzaImap = If(srv.Porta = 993, SecureSocketOptions.SslOnConnect, sicurezza)
                    impostazioni.ProtocolloRicezione = "IMAP"
                    applicato = True
                End If
            End If

            If String.IsNullOrWhiteSpace(impostazioni.ServerImap) AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerPop3) Then
                Dim srv As RecordSrv = InterrogaSrv("_pop3s._tcp." & dominio)
                Dim sicurezzaPop As SecureSocketOptions = SecureSocketOptions.SslOnConnect
                If srv Is Nothing Then
                    srv = InterrogaSrv("_pop3._tcp." & dominio)
                    sicurezzaPop = SecureSocketOptions.StartTls
                End If
                If srv IsNot Nothing Then
                    impostazioni.ServerPop3 = srv.Host
                    impostazioni.PortaPop3 = srv.Porta
                    impostazioni.SicurezzaPop3 = If(srv.Porta = 995, SecureSocketOptions.SslOnConnect, sicurezzaPop)
                    applicato = True
                End If
            End If

            If String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) Then
                Dim srv As RecordSrv = InterrogaSrv("_submission._tcp." & dominio)
                Dim sicurezzaSmtp As SecureSocketOptions = SecureSocketOptions.StartTls
                If srv Is Nothing Then
                    srv = InterrogaSrv("_submissions._tcp." & dominio)
                    sicurezzaSmtp = SecureSocketOptions.SslOnConnect
                End If
                If srv IsNot Nothing Then
                    impostazioni.ServerSmtp = srv.Host
                    impostazioni.PortaSmtp = srv.Porta
                    impostazioni.SicurezzaSmtp = If(srv.Porta = 465, SecureSocketOptions.SslOnConnect, sicurezzaSmtp)
                    applicato = True
                End If
            End If

            Return applicato
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaConfigurazioneDaSrv (" & If(dominio, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Interroga un singolo record SRV (es. "_imaps._tcp.dominio.it") tramite il resolver
    ''' DNS-over-HTTPS gia' usato per il riconoscimento del provider via MX (InterrogaDnsOverHttps)
    ''' e restituisce l'host/porta con priorita' migliore (numero piu' basso), o Nothing se il
    ''' record non esiste - il caso normale per la stragrande maggioranza dei domini, che non
    ''' pubblica questo standard.
    ''' </summary>
    Private Function InterrogaSrv(nomeRecord As String) As RecordSrv
        Try
            Dim json As String = InterrogaDnsOverHttps(nomeRecord, "SRV")
            If String.IsNullOrWhiteSpace(json) Then Return Nothing
            Return MiglioreRecordSrv(json)
        Catch ex As Exception
            ScriviLog("CLSMail.InterrogaSrv (" & If(nomeRecord, "") & "): " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Estrae dal JSON di dns.google/resolve (campo "Answer", voci con "data" nel formato testuale
    ''' standard "priorita' peso porta host.") il record con priorita' migliore. Una regex basta:
    ''' sono poche righe testuali con un formato fisso, non serve un parser DNS/JSON completo
    ''' (stesso approccio gia' usato per i record MX in ProviderDaRispostaMx).
    ''' </summary>
    Private Function MiglioreRecordSrv(json As String) As RecordSrv
        Try
            If String.IsNullOrWhiteSpace(json) Then Return Nothing
            Dim migliore As RecordSrv = Nothing
            Dim miglioreOrdine As Integer = Integer.MaxValue
            For Each m As Match In Regex.Matches(json, """data""\s*:\s*""(\d+)\s+(\d+)\s+(\d+)\s+([^""\s]+?)\.?""")
                Dim priorita As Integer
                Dim porta As Integer
                If Integer.TryParse(m.Groups(1).Value, priorita) AndAlso Integer.TryParse(m.Groups(3).Value, porta) Then
                    Dim host As String = m.Groups(4).Value.Trim()
                    If host = "." OrElse String.IsNullOrWhiteSpace(host) Then Continue For ' "servizio assente", RFC 2782
                    If migliore Is Nothing OrElse priorita < miglioreOrdine Then
                        migliore = New RecordSrv(host, porta)
                        miglioreOrdine = priorita
                    End If
                End If
            Next
            Return migliore
        Catch
            Return Nothing
        End Try
    End Function

    ' --------------------------------------------------------------------------------------------
    ' Livello 4: ultima risorsa - host per convenzione, accettati solo se rispondono davvero
    ' --------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Quando ISPDB, autoconfig, tabella dei provider noti e record SRV non hanno dato nulla,
    ''' prova gli host che quasi tutti gli altri provider (soprattutto hosting piccoli o PEC
    ''' generiche) usano per convenzione (imap./mail.<dominio> per la ricezione,
    ''' smtp./mail.<dominio> per l'invio) e accetta SOLO quelli per cui una connessione TLS vera
    ''' riesce (senza autenticarsi: a questo punto della procedura non c'e' ancora una password).
    ''' Cosi' non si propone mai un server indovinato che non esiste davvero.
    ''' </summary>
    Private Function ApplicaConfigurazioneIndovinata(impostazioni As ImpostazioniAccountMail, dominio As String, Optional dominioMx As String = "") As Boolean
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(dominio) Then Return False
            Dim applicato As Boolean = False
            Dim domini As New List(Of String) From {dominio}
            If Not String.IsNullOrWhiteSpace(dominioMx) AndAlso Not String.Equals(dominioMx, dominio, StringComparison.OrdinalIgnoreCase) Then
                domini.Add(dominioMx)
            End If

            If String.IsNullOrWhiteSpace(impostazioni.ServerImap) AndAlso String.IsNullOrWhiteSpace(impostazioni.ServerPop3) Then
                For Each d As String In domini
                    If ProvaHostRicezione(impostazioni, d) Then
                        applicato = True
                        Exit For
                    End If
                Next
            ElseIf String.IsNullOrWhiteSpace(impostazioni.ServerImap) Then
                For Each d As String In domini
                    If ProvaHostImap(impostazioni, d) Then
                        applicato = True
                        Exit For
                    End If
                Next
            End If

            If String.IsNullOrWhiteSpace(impostazioni.ServerSmtp) Then
                For Each d As String In domini
                    If ProvaHostSmtp(impostazioni, d) Then
                        applicato = True
                        Exit For
                    End If
                Next
            End If

            Return applicato
        Catch ex As Exception
            ScriviLog("CLSMail.ApplicaConfigurazioneIndovinata (" & If(dominio, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' imap./mail. su 993 e, se non rispondono, su 143 con STARTTLS. Poi pop./pop3. su 995.
    Private Function ProvaHostRicezione(impostazioni As ImpostazioniAccountMail, dominio As String) As Boolean
        Try
            If ProvaHostImap(impostazioni, dominio) Then Return True
            For Each host As String In New String() {"pop." & dominio, "pop3." & dominio}
                If HostRispondePop3(host, 995) Then
                    impostazioni.ServerPop3 = host
                    impostazioni.PortaPop3 = 995
                    impostazioni.SicurezzaPop3 = SecureSocketOptions.SslOnConnect
                    impostazioni.ProtocolloRicezione = "POP3"
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.ProvaHostRicezione (" & If(dominio, "") & "): " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ProvaHostImap(impostazioni As ImpostazioniAccountMail, dominio As String) As Boolean
        Try
            For Each host As String In New String() {"imap." & dominio, "mail." & dominio}
                If HostRispondeImaps(host, 993) Then
                    impostazioni.ServerImap = host
                    impostazioni.PortaImap = 993
                    impostazioni.SicurezzaImap = SecureSocketOptions.SslOnConnect
                    impostazioni.ProtocolloRicezione = "IMAP"
                    Return True
                End If
            Next
            For Each host As String In New String() {"imap." & dominio, "mail." & dominio}
                If HostRispondeImap(host, 143, SecureSocketOptions.StartTls) Then
                    impostazioni.ServerImap = host
                    impostazioni.PortaImap = 143
                    impostazioni.SicurezzaImap = SecureSocketOptions.StartTls
                    impostazioni.ProtocolloRicezione = "IMAP"
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.ProvaHostImap (" & If(dominio, "") & "): " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ProvaHostSmtp(impostazioni As ImpostazioniAccountMail, dominio As String) As Boolean
        Try
            For Each host As String In New String() {"smtp." & dominio, "mail." & dominio}
                If HostRispondeSmtp(host, 587, SecureSocketOptions.StartTls) Then
                    impostazioni.ServerSmtp = host
                    impostazioni.PortaSmtp = 587
                    impostazioni.SicurezzaSmtp = SecureSocketOptions.StartTls
                    Return True
                End If
                If HostRispondeSmtp(host, 465, SecureSocketOptions.SslOnConnect) Then
                    impostazioni.ServerSmtp = host
                    impostazioni.PortaSmtp = 465
                    impostazioni.SicurezzaSmtp = SecureSocketOptions.SslOnConnect
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSMail.ProvaHostSmtp (" & If(dominio, "") & "): " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>True se su host:porta risponde davvero un server IMAP con TLS immediato.</summary>
    Private Function HostRispondeImaps(host As String, porta As Integer) As Boolean
        Return HostRispondeImap(host, porta, SecureSocketOptions.SslOnConnect)
    End Function

    Private Function HostRispondeImap(host As String, porta As Integer, sicurezza As SecureSocketOptions) As Boolean
        Try
            Using client As New ImapClient()
                client.Timeout = 4000
                client.CheckCertificateRevocation = False
                client.Connect(host, porta, sicurezza)
                client.Disconnect(True)
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Function HostRispondePop3(host As String, porta As Integer) As Boolean
        Try
            Using client As New Pop3Client()
                client.Timeout = 4000
                client.CheckCertificateRevocation = False
                client.Connect(host, porta, SecureSocketOptions.SslOnConnect)
                client.Disconnect(True)
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>True se su host:porta risponde davvero un server SMTP con la sicurezza indicata.</summary>
    Private Function HostRispondeSmtp(host As String, porta As Integer, sicurezza As SecureSocketOptions) As Boolean
        Try
            Using client As New SmtpClient()
                client.Timeout = 5000
                client.CheckCertificateRevocation = False
                client.Connect(host, porta, sicurezza)
                client.Disconnect(True)
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Function InterpretaSocketType(valore As String) As SecureSocketOptions
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return SecureSocketOptions.Auto
            Select Case valore.Trim().ToUpperInvariant()
                Case "SSL", "SSL/TLS"
                    Return SecureSocketOptions.SslOnConnect
                Case "STARTTLS"
                    Return SecureSocketOptions.StartTls
                Case "PLAIN", "NONE"
                    Return SecureSocketOptions.None
                Case Else
                    Return SecureSocketOptions.Auto
            End Select
        Catch
            Return SecureSocketOptions.Auto
        End Try
    End Function

    Private Function ParteLocaleEmail(email As String) As String
        Try
            If String.IsNullOrWhiteSpace(email) OrElse Not email.Contains("@") Then Return email
            Return email.Substring(0, email.IndexOf("@"c))
        Catch
            Return email
        End Try
    End Function

    ' ============================================================================================
    ' PROVA CONNESSIONE (prima di salvare un account nuovo o modificato)
    ' ============================================================================================

    Public Function ProvaConnessioneAccount(impostazioni As ImpostazioniAccountMail) As EsitoProvaConnessione
        Dim esito As New EsitoProvaConnessione()
        Try
            If impostazioni Is Nothing Then
                esito.MessaggioImap = "Impostazioni assenti."
                esito.MessaggioSmtp = "Impostazioni assenti."
                Return esito
            End If

            Try
                If UsaPop3(impostazioni) Then
                    Using client As Pop3Client = CreaClientPop3(impostazioni)
                        client.Disconnect(True)
                    End Using
                    esito.RicezioneOk = True
                    esito.MessaggioRicezione = "Connessione POP3 sicura riuscita."
                Else
                    Using client As ImapClient = CreaClientImap(impostazioni)
                        client.Disconnect(True)
                    End Using
                    esito.RicezioneOk = True
                    esito.MessaggioRicezione = "Connessione IMAP sicura riuscita."
                End If
            Catch exRic As Exception
                esito.RicezioneOk = False
                esito.MessaggioRicezione = If(UsaPop3(impostazioni), "POP3: ", "IMAP: ") & DescriviErroreMailKit(exRic)
                ImpostaErrore("CLSMail.ProvaConnessioneAccount ricezione", exRic)
            End Try

            Try
                Using client As SmtpClient = CreaClientSmtp(impostazioni)
                    client.Disconnect(True)
                End Using
                esito.SmtpOk = True
                esito.MessaggioSmtp = "Connessione SMTP riuscita."
            Catch exSmtp As Exception
                esito.SmtpOk = False
                esito.MessaggioSmtp = "SMTP: " & DescriviErroreMailKit(exSmtp)
                ImpostaErrore("CLSMail.ProvaConnessioneAccount SMTP", exSmtp)
            End Try
        Catch ex As Exception
            ScriviLog("CLSMail.ProvaConnessioneAccount: " & ex.ToString())
            esito.MessaggioImap = "Errore: " & ex.Message
        End Try
        Return esito
    End Function

    ' ============================================================================================
    ' CONTROLLO NUOVI MESSAGGI (senza GUI, senza avvisi) - True se ci sono non letti sul server
    ' ============================================================================================

    ' Controlla se ci sono messaggi non letti. Se chiaveAccount è vuota controlla tutti gli
    ' account attivi con ControlloAutomaticoRicezione oppure, se nessuno ha il flag, tutti gli attivi.
    ' Non mostra messaggi e non avvia scaricamento completo: solo ricerca IMAP Unseen.
    Public Function CiSonoNuoviMessaggi(Optional chiaveAccount As String = "") As Boolean
        Try
            Dim elenco As List(Of ImpostazioniAccountMail)
            If Not String.IsNullOrWhiteSpace(chiaveAccount) Then
                Dim uno As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
                elenco = New List(Of ImpostazioniAccountMail)
                If uno IsNot Nothing Then elenco.Add(uno)
            Else
                Dim attivi As List(Of ImpostazioniAccountMail) = OttieniElencoAccount(True)
                elenco = attivi.Where(Function(a) a.ControlloAutomaticoRicezione).ToList()
                If elenco.Count = 0 Then elenco = attivi
            End If

            For Each impostazioni As ImpostazioniAccountMail In elenco
                Try
                    If UsaPop3(impostazioni) Then
                        Using client As Pop3Client = CreaClientPop3(impostazioni)
                            If client.Count > 0 Then
                                client.Disconnect(True)
                                Return True
                            End If
                            client.Disconnect(True)
                        End Using
                    Else
                        Dim client As ImapClient = OttieniClientImapDaCache(impostazioni.Chiave, impostazioni)
                        Dim quanti As Integer = 0
                        Try
                            Dim cartella As IMailFolder = client.GetFolder("INBOX")
                            cartella.Open(FolderAccess.ReadOnly, Segnale())
                            Dim nonLetti As IList(Of UniqueId) = cartella.Search(SearchQuery.NotSeen)
                            quanti = If(nonLetti Is Nothing, 0, nonLetti.Count)
                            cartella.Close()
                        Finally
                            RestituisciClientImapAllaCache(impostazioni.Chiave, client)
                        End Try
                        If quanti > 0 Then Return True
                    End If
                Catch exAccount As Exception
                    ScriviLog("CLSMail.CiSonoNuoviMessaggi account (" & impostazioni.Chiave & "): " & exAccount.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.CiSonoNuoviMessaggi: " & ex.ToString())
        End Try
        Return False
    End Function

    ' Restituisce solo i riepiloghi non letti (senza avviare scaricamento completo in sottofondo
    ' se già noti localmente). Utile per liste "solo da leggere".
    Public Function OttieniElencoSoloNonLetti(chiaveAccount As String, Optional nomeCartella As String = "INBOX",
                                              Optional numeroMassimo As Integer = 50) As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return elenco

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadOnly, Segnale())
                Dim uidNonLetti As IList(Of UniqueId) = cartella.Search(SearchQuery.NotSeen)
                If uidNonLetti Is Nothing OrElse uidNonLetti.Count = 0 Then
                    cartella.Close()
                    Return elenco
                End If

                Dim daPrendere = uidNonLetti.Skip(Math.Max(0, uidNonLetti.Count - numeroMassimo)).ToList()
                Dim riepiloghi As IList(Of IMessageSummary) = cartella.Fetch(daPrendere,
                    MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags)

                Dim riepiloghiOrdinati As New List(Of IMessageSummary)(riepiloghi)
                riepiloghiOrdinati.Sort(AddressOf ConfrontaRiepiloghiPerDataDiscendente)

                For Each voce As IMessageSummary In riepiloghiOrdinati
                    elenco.Add(New RiepilogoMessaggio() With {
                        .ChiaveAccount = chiaveAccount,
                        .Id = voce.UniqueId.Id.ToString(),
                        .Oggetto = If(voce.Envelope IsNot Nothing, voce.Envelope.Subject, ""),
                        .Mittente = If(voce.Envelope IsNot Nothing AndAlso voce.Envelope.From.Count > 0, voce.Envelope.From(0).ToString(), ""),
                        .Data = If(voce.Envelope IsNot Nothing, voce.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue),
                        .Letto = False
                    })
                Next

                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniElencoSoloNonLetti (account " & chiaveAccount & "): " & ex.ToString())
        End Try
        Return elenco
    End Function

    ' Risolve il percorso IMAP di una cartella speciale (Inbox, Sent, Drafts, Trash, Junk)
    ' usando l'elenco già salvato o interrogando il server.
    ' I nomi con cui le cartelle di sistema compaiono sui server piu' diffusi, in italiano,
    ' inglese e spagnolo. Serve solo quando il server non espone l'attributo speciale.
    Private Function NomiComuniCartellaSpeciale(tipo As String) As String()
        Try
            Select Case If(tipo, "").Trim().ToLowerInvariant()
                Case "trash"
                    Return New String() {"Cestino", "Trash", "Deleted Items", "Deleted Messages", "Posta eliminata", "Bin", "Papelera", "Elementi eliminati", "Corbeille", "Papierkorb", "Geloschte Elemente", "Lixeira", "Prullenbak", "Elementos eliminados"}
                Case "junk"
                    Return New String() {"Posta indesiderata", "Spam", "Junk", "Junk E-mail", "Junk Email", "Bulk Mail", "Indesiderata", "Courrier indesirable", "Pourriel", "Correo no deseado", "Werbung", "Ongewenst"}
                Case "sent"
                    Return New String() {"Posta inviata", "Sent", "Sent Items", "Sent Mail", "Sent Messages", "Inviata", "Inviati", "Elementi inviati", "Elements envoyes", "Messages envoyes", "Envoyes", "Correo enviado", "Enviados", "Gesendet", "Gesendete Elemente", "Verzonden items"}
                Case "drafts"
                    Return New String() {"Bozze", "Drafts", "Draft", "Borradores", "Brouillons", "Entwurfe", "Concepten", "Rascunhos"}
                Case "inbox"
                    Return New String() {"INBOX", "Posta in arrivo", "Inbox", "Boite de reception", "Bandeja de entrada", "Posteingang"}
                Case Else
                    Return New String() {}
            End Select
        Catch ex As Exception
            ScriviLog("CLSMail.NomiComuniCartellaSpeciale: " & ex.ToString())
            Return New String() {}
        End Try
    End Function

    Private ReadOnly _cacheCartelleSpeciali As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockCartelleSpeciali As New Object()

    Public Sub AzzeraCacheCartelleSpeciali()
        Try
            SyncLock _lockCartelleSpeciali
                _cacheCartelleSpeciali.Clear()
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.AzzeraCacheCartelleSpeciali: " & ex.Message)
        End Try
    End Sub

    ' Chiede al server qual e' la cartella speciale, sfruttando SPECIAL-USE / XLIST. E' la via
    ' piu' affidabile: Gmail tiene posta inviata, bozze e cestino dentro il contenitore [Gmail],
    ' con nomi tradotti che cambiano da casella a casella.
    Private Function ChiediCartellaSpecialeAlServer(chiaveAccount As String, tipo As String) As String
        Try
            Dim chiaveCache As String = If(chiaveAccount, "") & "|" & If(tipo, "")
            SyncLock _lockCartelleSpeciali
                If _cacheCartelleSpeciali.ContainsKey(chiaveCache) Then Return _cacheCartelleSpeciali(chiaveCache)
            End SyncLock

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return ""
            If Not String.Equals(If(impostazioni.ProtocolloRicezione, "IMAP"), "IMAP", StringComparison.OrdinalIgnoreCase) Then Return ""

            Dim percorso As String = ""
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = CartellaSpecialeDalServer(client, tipo)
                If cartella IsNot Nothing Then percorso = If(cartella.FullName, "")

                If String.IsNullOrWhiteSpace(percorso) Then
                    Dim nomiPossibili As String() = NomiComuniCartellaSpeciale(tipo)
                    If nomiPossibili IsNot Nothing AndAlso nomiPossibili.Length > 0 Then
                        For Each candidata As IMailFolder In RaccogliTutteLeCartelle(client)
                            Try
                                Dim nome As String = If(candidata.Name, "").Trim()
                                For Each candidato As String In nomiPossibili
                                    If String.Equals(nome, candidato, StringComparison.OrdinalIgnoreCase) Then
                                        percorso = If(candidata.FullName, "")
                                        Exit For
                                    End If
                                Next
                            Catch exCandidata As Exception
                                ScriviLog("CLSMail.ChiediCartellaSpecialeAlServer candidata: " & exCandidata.Message)
                            End Try
                            If Not String.IsNullOrWhiteSpace(percorso) Then Exit For
                        Next
                    End If
                End If
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            If Not String.IsNullOrWhiteSpace(percorso) Then
                SyncLock _lockCartelleSpeciali
                    _cacheCartelleSpeciali(chiaveCache) = percorso
                End SyncLock
            End If
            Return percorso
        Catch ex As Exception
            ScriviLog("CLSMail.ChiediCartellaSpecialeAlServer (" & If(chiaveAccount, "") & ", " & If(tipo, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function RisolviPercorsoCartellaSpeciale(chiaveAccount As String, tipoSpeciale As String) As String
        Try
            Dim cercato As String = If(tipoSpeciale, "").Trim().ToLowerInvariant()
            Dim mappa As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                {"inbox", "Posta in arrivo"},
                {"sent", "Posta inviata"},
                {"drafts", "Bozze"},
                {"trash", "Cestino"},
                {"junk", "Posta indesiderata"}
            }
            Dim etichetta As String = If(mappa.ContainsKey(cercato), mappa(cercato), tipoSpeciale)

            Dim salvate As DataTable = OttieniCartelleSalvate(chiaveAccount)
            For Each riga As DataRow In salvate.Rows
                Dim speciale As String = riga("Speciale").ToString()
                If String.Equals(speciale, etichetta, StringComparison.OrdinalIgnoreCase) Then
                    Return riga("PercorsoCompleto").ToString()
                End If
            Next

            Dim daServer As String = ChiediCartellaSpecialeAlServer(chiaveAccount, cercato)
            If Not String.IsNullOrWhiteSpace(daServer) Then Return daServer

            Dim elenco As List(Of CartellaImap) = OttieniElencoCartelleImap(chiaveAccount)
            For Each cartella As CartellaImap In elenco
                If String.Equals(cartella.Speciale, etichetta, StringComparison.OrdinalIgnoreCase) Then
                    Return cartella.PercorsoCompleto
                End If
            Next

            ' Se il server non ha marcato la cartella con l'attributo speciale, la si cerca
            ' per nome fra quelle che esistono davvero. Restituire un nome inventato tipo
            ' "Trash" su una casella che chiama il cestino in un altro modo faceva fallire
            ' l'apertura con un errore del server invece di dire cosa mancava.
            Dim nomiPossibili As String() = NomiComuniCartellaSpeciale(cercato)
            If nomiPossibili IsNot Nothing AndAlso nomiPossibili.Length > 0 Then
                For Each cartella As CartellaImap In elenco
                    Try
                        Dim nomeSemplice As String = If(cartella.Nome, "").Trim()
                        Dim percorsoCompleto As String = If(cartella.PercorsoCompleto, "").Trim()
                        For Each candidato As String In nomiPossibili
                            If String.Equals(nomeSemplice, candidato, StringComparison.OrdinalIgnoreCase) Then Return cartella.PercorsoCompleto
                            If percorsoCompleto.EndsWith("/" & candidato, StringComparison.OrdinalIgnoreCase) Then Return cartella.PercorsoCompleto
                            If percorsoCompleto.EndsWith("." & candidato, StringComparison.OrdinalIgnoreCase) Then Return cartella.PercorsoCompleto
                        Next
                    Catch exCartella As Exception
                        ScriviLog("CLSMail.RisolviPercorsoCartellaSpeciale confronto nome: " & exCartella.ToString())
                    End Try
                Next
            End If

            ' fallback comuni
            Select Case cercato
                Case "inbox" : Return "INBOX"
                Case "sent" : Return "Sent"
                Case "drafts" : Return "Drafts"
                Case "trash" : Return "Trash"
                Case "junk" : Return "Junk"
            End Select
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviPercorsoCartellaSpeciale (" & chiaveAccount & ", " & tipoSpeciale & "): " & ex.ToString())
        End Try
        If String.Equals(tipoSpeciale, "inbox", StringComparison.OrdinalIgnoreCase) Then Return "INBOX"
        Return tipoSpeciale
    End Function

    ' ============================================================================================
    ' ELIMINAZIONE MESSAGGI (uno o piu')
    ' ============================================================================================

    ''' <summary>
    ''' True se l'identificativo indicato appartiene a un messaggio scaricato via POP3.
    ''' </summary>
    Private Function IdMessaggioPop3(idMessaggio As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(idMessaggio) Then Return False
            Return idMessaggio.StartsWith("pop3-", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLog("CLSMail.IdMessaggioPop3: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Elimina un singolo messaggio. Su IMAP lo sposta nel cestino, oppure lo elimina in modo
    ''' definitivo se e' gia' nel cestino o se il cestino non esiste. Su POP3 lo toglie dal
    ''' server solo se l'account e' impostato per cancellare dal server. In ogni caso rimuove la
    ''' copia locale e i relativi allegati.
    ''' </summary>
    Public Function EliminaMessaggio(chiaveAccount As String, nomeCartella As String, idMessaggio As String,
                                     Optional definitivo As Boolean = False) As Boolean
        Try
            If String.IsNullOrWhiteSpace(idMessaggio) Then Return False
            Dim elenco As New List(Of String)
            elenco.Add(idMessaggio)
            Return EliminaMessaggi(chiaveAccount, nomeCartella, elenco, definitivo) > 0
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaMessaggio (account " & chiaveAccount & ", " & idMessaggio & ")", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Elimina piu' messaggi in una sola connessione e restituisce quanti ne sono stati
    ''' effettivamente eliminati.
    ''' </summary>
    Public Function EliminaMessaggi(chiaveAccount As String, nomeCartella As String, idMessaggi As List(Of String),
                                    Optional definitivo As Boolean = False) As Integer
        Try
            If idMessaggi Is Nothing OrElse idMessaggi.Count = 0 Then Return 0
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return 0
            End If

            If UsaPop3(impostazioni) Then
                Return EliminaMessaggiPop3(chiaveAccount, nomeCartella, impostazioni, idMessaggi)
            End If
            Return EliminaMessaggiImap(chiaveAccount, nomeCartella, impostazioni, idMessaggi, definitivo)
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaMessaggi (account " & chiaveAccount & ", " & If(nomeCartella, "") & ")", ex)
            Return 0
        End Try
    End Function

    Private Function EliminaMessaggiImap(chiaveAccount As String, nomeCartella As String,
                                         impostazioni As ImpostazioniAccountMail, idMessaggi As List(Of String),
                                         definitivo As Boolean) As Integer
        Dim eliminati As Integer = 0
        Try
            Dim uid As New List(Of UniqueId)
            For Each id As String In idMessaggi
                Try
                    If String.IsNullOrWhiteSpace(id) Then Continue For
                    Dim numero As UInteger = 0
                    If Not UInteger.TryParse(id.Trim(), numero) Then Continue For
                    uid.Add(New UniqueId(numero))
                Catch exId As Exception
                    ScriviLog("CLSMail.EliminaMessaggiImap identificativo (" & If(id, "") & "): " & exId.ToString())
                End Try
            Next
            If uid.Count = 0 Then Return 0

            Dim percorsoCestino As String = ""
            If Not definitivo Then
                Try
                    percorsoCestino = RisolviPercorsoCartellaSpeciale(chiaveAccount, "trash")
                Catch exCestino As Exception
                    ScriviLog("CLSMail.EliminaMessaggiImap cestino: " & exCestino.ToString())
                End Try
                If String.Equals(percorsoCestino, nomeCartella, StringComparison.OrdinalIgnoreCase) Then percorsoCestino = ""
            End If

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadWrite, Segnale())

                Dim spostato As Boolean = False
                If Not String.IsNullOrWhiteSpace(percorsoCestino) Then
                    Try
                        Dim cestino As IMailFolder = client.GetFolder(percorsoCestino)
                        If cestino IsNot Nothing Then
                            cartella.MoveTo(uid, cestino)
                            spostato = True
                        End If
                    Catch exSposta As Exception
                        ScriviLog("CLSMail.EliminaMessaggiImap spostamento nel cestino (" & percorsoCestino & "): " & DescriviErroreMailKit(exSposta))
                    End Try
                End If

                If Not spostato Then
                    cartella.Store(uid, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) With {.Silent = True})
                    cartella.Expunge(uid)
                End If

                eliminati = uid.Count
                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            For Each id As String In idMessaggi
                EliminaMessaggioDalDb(chiaveAccount, nomeCartella, id)
            Next
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.EliminaMessaggiImap autenticazione (account " & chiaveAccount & ")", ex)
            Return 0
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.EliminaMessaggiImap SSL (account " & chiaveAccount & ")", ex)
            Return 0
        Catch ex As FolderNotFoundException
            ImpostaErrore("CLSMail.EliminaMessaggiImap cartella (account " & chiaveAccount & ", " & If(nomeCartella, "") & ")", ex)
            Return 0
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.EliminaMessaggiImap IMAP (account " & chiaveAccount & ")", ex)
            Return 0
        Catch ex As ImapProtocolException
            ImpostaErrore("CLSMail.EliminaMessaggiImap IMAP (account " & chiaveAccount & ")", ex)
            Return 0
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaMessaggiImap (account " & chiaveAccount & ")", ex)
            Return 0
        End Try
        Return eliminati
    End Function

    Private Function EliminaMessaggiPop3(chiaveAccount As String, nomeCartella As String,
                                         impostazioni As ImpostazioniAccountMail, idMessaggi As List(Of String)) As Integer
        Dim eliminati As Integer = 0
        Try
            If impostazioni.CancellaMessaggiDalServer Then
                Try
                    Using client As Pop3Client = CreaClientPop3(impostazioni)
                        Dim uidServer As IList(Of String) = Nothing
                        Try
                            uidServer = client.GetMessageUids()
                        Catch exUid As Exception
                            ScriviLog("CLSMail.EliminaMessaggiPop3 elenco UID: " & DescriviErroreMailKit(exUid))
                        End Try

                        For Each id As String In idMessaggi
                            Try
                                If String.IsNullOrWhiteSpace(id) Then Continue For
                                Dim parte As String = id
                                If parte.StartsWith("pop3-", StringComparison.OrdinalIgnoreCase) Then parte = parte.Substring(5)
                                Dim indice As Integer = -1
                                If uidServer IsNot Nothing Then
                                    For i As Integer = 0 To uidServer.Count - 1
                                        If String.Equals(uidServer(i), parte, StringComparison.Ordinal) Then
                                            indice = i
                                            Exit For
                                        End If
                                    Next
                                End If
                                If indice < 0 Then
                                    Dim numero As Integer = 0
                                    If Integer.TryParse(parte, numero) Then indice = numero
                                End If
                                If indice < 0 OrElse indice >= client.Count Then Continue For
                                client.DeleteMessage(indice)
                            Catch exSingolo As Exception
                                ScriviLog("CLSMail.EliminaMessaggiPop3 messaggio (" & If(id, "") & "): " & DescriviErroreMailKit(exSingolo))
                            End Try
                        Next
                        client.Disconnect(True)
                    End Using
                Catch exServer As Exception
                    ImpostaErrore("CLSMail.EliminaMessaggiPop3 server (account " & chiaveAccount & ")", exServer)
                End Try
            End If

            For Each id As String In idMessaggi
                If String.IsNullOrWhiteSpace(id) Then Continue For
                EliminaMessaggioDalDb(chiaveAccount, nomeCartella, id)
                eliminati += 1
            Next
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaMessaggiPop3 (account " & chiaveAccount & ")", ex)
            Return eliminati
        End Try
        Return eliminati
    End Function

    ''' <summary>
    ''' Elimina tutti i messaggi presenti nella cartella indicata, non solo quelli caricati
    ''' nell'elenco a video. Restituisce quanti ne sono stati eliminati.
    ''' </summary>
    Public Function EliminaTuttiIMessaggi(chiaveAccount As String, nomeCartella As String,
                                          Optional definitivo As Boolean = False) As Integer
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return 0
            End If

            Dim identificativi As New List(Of String)

            If UsaPop3(impostazioni) Then
                Using client2 As Pop3Client = CreaClientPop3(impostazioni)
                    Dim totale As Integer = client2.Count
                    Dim tuttiGliUid As IList(Of String) = Nothing
                    Try
                        tuttiGliUid = client2.GetMessageUids()
                    Catch exUid As Exception
                        ScriviLog("CLSMail.EliminaTuttiIMessaggi UIDL: " & DescriviErroreMailKit(exUid))
                    End Try
                    For i As Integer = 0 To totale - 1
                        Try
                            Dim idPop As String = "pop3-" & i.ToString()
                            If tuttiGliUid IsNot Nothing AndAlso i < tuttiGliUid.Count AndAlso Not String.IsNullOrWhiteSpace(tuttiGliUid(i)) Then
                                idPop = "pop3-" & tuttiGliUid(i)
                            End If
                            identificativi.Add(idPop)
                        Catch exId As Exception
                            ScriviLog("CLSMail.EliminaTuttiIMessaggi POP3 " & i.ToString() & ": " & DescriviErroreMailKit(exId))
                        End Try
                    Next
                    client2.Disconnect(True)
                End Using
                If identificativi.Count = 0 Then Return 0
                Return EliminaMessaggiPop3(chiaveAccount, nomeCartella, impostazioni, identificativi)
            End If

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(nomeCartella)
                cartella.Open(FolderAccess.ReadOnly, Segnale())
                Dim tuttiGliUid As IList(Of UniqueId) = cartella.Search(SearchQuery.All)
                If tuttiGliUid IsNot Nothing Then
                    For Each uid As UniqueId In tuttiGliUid
                        identificativi.Add(uid.Id.ToString())
                    Next
                End If
                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            If identificativi.Count = 0 Then Return 0
            Return EliminaMessaggiImap(chiaveAccount, nomeCartella, impostazioni, identificativi, definitivo)
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaTuttiIMessaggi (account " & chiaveAccount & ", " & If(nomeCartella, "") & ")", ex)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Toglie dal database locale il messaggio indicato, i suoi allegati registrati e la
    ''' cartella dei file scaricati.
    ''' </summary>
    Private Sub EliminaMessaggioDalDb(chiaveAccount As String, nomeCartella As String, idMessaggio As String)
        Try
            If String.IsNullOrWhiteSpace(idMessaggio) Then Return
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG FROM " & NomeTabellaMessaggi & " WHERE ChiaveAccount=@a AND Cartella=@c AND UidServer=@u;",
                New Dictionary(Of String, Object) From {{"@a", chiaveAccount}, {"@c", nomeCartella}, {"@u", idMessaggio}})
            If tabella Is Nothing Then Return

            For Each riga As DataRow In tabella.Rows
                Dim chiaveMessaggio As String = riga("ChiaveWG").ToString()
                If String.IsNullOrWhiteSpace(chiaveMessaggio) Then Continue For
                Try
                    _db.EseguiNonQuery("DELETE FROM " & NomeTabellaAllegati & " WHERE ChiaveMessaggio=@c;",
                        New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
                Catch exAllegati As Exception
                    ScriviLog("CLSMail.EliminaMessaggioDalDb allegati (" & chiaveMessaggio & "): " & exAllegati.ToString())
                End Try
                Try
                    Dim cartellaAllegatiMessaggio As String = Path.Combine(_cartellaAllegati, chiaveMessaggio)
                    If Directory.Exists(cartellaAllegatiMessaggio) Then Directory.Delete(cartellaAllegatiMessaggio, True)
                Catch exCartella As Exception
                    ScriviLog("CLSMail.EliminaMessaggioDalDb cartella allegati (" & chiaveMessaggio & "): " & exCartella.ToString())
                End Try
                Try
                    _db.EliminaRecord(NomeTabellaMessaggi, chiaveMessaggio)
                Catch exRecord As Exception
                    ScriviLog("CLSMail.EliminaMessaggioDalDb record (" & chiaveMessaggio & "): " & exRecord.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EliminaMessaggioDalDb (account " & chiaveAccount & ", " & If(idMessaggio, "") & "): " & ex.ToString())
        End Try
    End Sub

    ' ============================================================================================
    Private Function ValoreColonna(riga As DataRow, nomeColonna As String) As String
        Try
            If riga Is Nothing Then Return ""
            If Not riga.Table.Columns.Contains(nomeColonna) Then Return ""
            If riga.IsNull(nomeColonna) Then Return ""
            Return riga(nomeColonna).ToString()
        Catch ex As Exception
            ScriviLog("CLSMail.ValoreColonna (" & If(nomeColonna, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EstraiCoppieMultistatus(testoXml As String, nomeElementoDati As String) As List(Of KeyValuePair(Of String, String))
        Dim risultato As New List(Of KeyValuePair(Of String, String))
        Try
            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(testoXml)

            For Each nodoRisposta As XmlNode In documento.SelectNodes("//*[local-name()='response']")
                Dim href As String = ""
                Dim dati As String = ""

                For Each figlio As XmlNode In nodoRisposta.SelectNodes(".//*")
                    If String.IsNullOrWhiteSpace(href) AndAlso String.Equals(figlio.LocalName, "href", StringComparison.OrdinalIgnoreCase) Then
                        href = figlio.InnerText.Trim()
                    End If
                    If String.IsNullOrWhiteSpace(dati) AndAlso String.Equals(figlio.LocalName, nomeElementoDati, StringComparison.OrdinalIgnoreCase) Then
                        dati = figlio.InnerText
                    End If
                Next

                If Not String.IsNullOrWhiteSpace(dati) Then
                    risultato.Add(New KeyValuePair(Of String, String)(href, dati))
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiCoppieMultistatus (" & If(nomeElementoDati, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function EseguiReportCoppieWebDav(url As String, utente As String, password As String, corpoRichiesta As String,
                                              nomeElementoDati As String, profondita As String) As List(Of KeyValuePair(Of String, String))
        Dim risultato As New List(Of KeyValuePair(Of String, String))
        Try
            Dim risposta As RispostaDav = EseguiRichiestaDav("REPORT", url, utente, password, corpoRichiesta, "text/xml", profondita)
            If risposta Is Nothing Then Return risultato
            If risposta.CodiceStato <> 207 AndAlso risposta.CodiceStato <> 200 Then
                Return risultato
            End If
            Return EstraiCoppieMultistatus(risposta.Corpo, nomeElementoDati)
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiReportCoppieWebDav (" & If(url, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function EseguiDeleteWebDav(url As String, utente As String, password As String) As Boolean
        Try
            Dim risposta As RispostaDav = EseguiRichiestaDav("DELETE", url, utente, password)
            If risposta Is Nothing Then Return False
            If risposta.CodiceStato = 404 OrElse risposta.CodiceStato = 410 Then Return True
            If Not risposta.Successo Then
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiDeleteWebDav (" & If(url, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function ScaricaVCardConHref(urlCollezione As String, utente As String, password As String) As List(Of KeyValuePair(Of String, String))
        Dim risultato As New List(Of KeyValuePair(Of String, String))
        Try
            Dim elencoHref As List(Of String) = ElencaRisorseDav(urlCollezione, utente, password, ".vcf")

            If elencoHref.Count > 0 Then
                Dim dimensioneBlocco As Integer = 50
                Dim indice As Integer = 0
                While indice < elencoHref.Count
                    Dim fine As Integer = Math.Min(indice + dimensioneBlocco - 1, elencoHref.Count - 1)
                    Dim corpo As New StringBuilder()
                    corpo.Append("<?xml version=""1.0"" encoding=""utf-8""?>")
                    corpo.Append("<C:addressbook-multiget xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:carddav"">")
                    corpo.Append("<D:prop><D:getetag/><C:address-data/></D:prop>")
                    For posizione As Integer = indice To fine
                        corpo.Append("<D:href>").Append(EscapeXml(elencoHref(posizione))).Append("</D:href>")
                    Next
                    corpo.Append("</C:addressbook-multiget>")

                    Dim parziale As List(Of KeyValuePair(Of String, String)) = EseguiReportCoppieWebDav(urlCollezione, utente, password, corpo.ToString(), "address-data", "0")
                    If parziale IsNot Nothing AndAlso parziale.Count > 0 Then risultato.AddRange(parziale)
                    indice = fine + 1
                End While
            End If

            If risultato.Count = 0 Then
                Dim corpoQuery As String =
                    "<?xml version=""1.0"" encoding=""utf-8""?>" &
                    "<C:addressbook-query xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:carddav"">" &
                    "<D:prop><D:getetag/><C:address-data/></D:prop>" &
                    "<C:filter/></C:addressbook-query>"
                Dim tutti As List(Of KeyValuePair(Of String, String)) = EseguiReportCoppieWebDav(urlCollezione, utente, password, corpoQuery, "address-data", "1")
                If tutti IsNot Nothing AndAlso tutti.Count > 0 Then risultato.AddRange(tutti)
            End If

            If risultato.Count = 0 AndAlso elencoHref.Count > 0 Then
                For Each href As String In elencoHref
                    Try
                        Dim urlRisorsa As String = RisolviUrlRelativo(urlCollezione, href)
                        Dim rispostaFile As RispostaDav = EseguiRichiestaDav("GET", urlRisorsa, utente, password, Nothing, "text/vcard")
                        If rispostaFile IsNot Nothing AndAlso rispostaFile.Successo AndAlso Not String.IsNullOrWhiteSpace(rispostaFile.Corpo) Then
                            risultato.Add(New KeyValuePair(Of String, String)(href, rispostaFile.Corpo))
                        End If
                    Catch exFile As Exception
                        ScriviLog("CLSMail.ScaricaVCardConHref singolo (" & If(href, "") & "): " & exFile.ToString())
                    End Try
                Next
            End If
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaVCardConHref (" & If(urlCollezione, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ScaricaEventiConHref(urlCollezione As String, utente As String, password As String) As List(Of KeyValuePair(Of String, String))
        Dim risultato As New List(Of KeyValuePair(Of String, String))
        Try
            Dim inizioIntervallo As String = DateTime.UtcNow.AddYears(-2).ToString("yyyyMMdd\THHmmss\Z")
            Dim fineIntervallo As String = DateTime.UtcNow.AddYears(5).ToString("yyyyMMdd\THHmmss\Z")

            Dim corpoQuery As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<C:calendar-query xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                "<D:prop><D:getetag/><C:calendar-data/></D:prop>" &
                "<C:filter><C:comp-filter name=""VCALENDAR""><C:comp-filter name=""VEVENT"">" &
                "<C:time-range start=""" & inizioIntervallo & """ end=""" & fineIntervallo & """/>" &
                "</C:comp-filter></C:comp-filter></C:filter>" &
                "</C:calendar-query>"

            Dim tutti As List(Of KeyValuePair(Of String, String)) = EseguiReportCoppieWebDav(urlCollezione, utente, password, corpoQuery, "calendar-data", "1")
            If tutti IsNot Nothing AndAlso tutti.Count > 0 Then risultato.AddRange(tutti)

            If risultato.Count = 0 Then
                Dim corpoSenzaIntervallo As String =
                    "<?xml version=""1.0"" encoding=""utf-8""?>" &
                    "<C:calendar-query xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                    "<D:prop><D:getetag/><C:calendar-data/></D:prop>" &
                    "<C:filter><C:comp-filter name=""VCALENDAR""><C:comp-filter name=""VEVENT""/></C:comp-filter></C:filter>" &
                    "</C:calendar-query>"
                Dim senzaIntervallo As List(Of KeyValuePair(Of String, String)) = EseguiReportCoppieWebDav(urlCollezione, utente, password, corpoSenzaIntervallo, "calendar-data", "1")
                If senzaIntervallo IsNot Nothing AndAlso senzaIntervallo.Count > 0 Then risultato.AddRange(senzaIntervallo)
            End If

            If risultato.Count = 0 Then
                Dim elencoHref As List(Of String) = ElencaRisorseDav(urlCollezione, utente, password, ".ics")
                If elencoHref.Count > 0 Then
                    Dim dimensioneBlocco As Integer = 50
                    Dim indice As Integer = 0
                    While indice < elencoHref.Count
                        Dim fine As Integer = Math.Min(indice + dimensioneBlocco - 1, elencoHref.Count - 1)
                        Dim corpo As New StringBuilder()
                        corpo.Append("<?xml version=""1.0"" encoding=""utf-8""?>")
                        corpo.Append("<C:calendar-multiget xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">")
                        corpo.Append("<D:prop><D:getetag/><C:calendar-data/></D:prop>")
                        For posizione As Integer = indice To fine
                            corpo.Append("<D:href>").Append(EscapeXml(elencoHref(posizione))).Append("</D:href>")
                        Next
                        corpo.Append("</C:calendar-multiget>")

                        Dim parziale As List(Of KeyValuePair(Of String, String)) = EseguiReportCoppieWebDav(urlCollezione, utente, password, corpo.ToString(), "calendar-data", "0")
                        If parziale IsNot Nothing AndAlso parziale.Count > 0 Then risultato.AddRange(parziale)
                        indice = fine + 1
                    End While
                End If
            End If
        Catch ex As Exception
            ScriviLog("CLSMail.ScaricaEventiConHref (" & If(urlCollezione, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function RisolviCollezioneAgenda(impostazioni As ImpostazioniAccountMail, utente As String, password As String,
                                              Optional richiedeScrittura As Boolean = False) As String
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then Return ""
            Dim url As String = RisolviCollezioneDav(impostazioni.UrlAgendaCalDav, utente, password,
                                                       "urn:ietf:params:xml:ns:caldav", "calendar-home-set", "calendar", richiedeScrittura)
            If String.IsNullOrWhiteSpace(url) Then Return ""
            If Not url.EndsWith("/") Then url &= "/"
            Return url
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviCollezioneAgenda: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Come RisolviCollezioneAgenda, ma restituisce TUTTI i calendari scrivibili trovati
    ' sull'account, non solo uno. Serve per la creazione di un evento nuovo: quando un account ha
    ' piu' calendari (es. "Casa" e "Lavoro") non possiamo sapere quale l'utente si aspetta di
    ' vedere sull'iPhone, quindi scriviamo l'evento su tutti quelli validi invece di indovinarne
    ' uno solo. Ripete la stessa discovery (principal -> home-set) di RisolviCollezioneDav perche'
    ' quella funzione e' vincolata a restituire un singolo url.
    ' Discovery CalDAV comune (current-user-principal -> calendar-home-set): parte dai punti di
    ' partenza ricavati dall'UrlAgendaCalDav configurato e restituisce l'url assoluto (con "/"
    ' finale) dell'home-set dei calendari dell'account, oppure "" se non trovato. Condivisa da
    ' RisolviTutteLeCollezioniAgenda e OttieniElencoCalendari per non duplicare la discovery.
    Private Function RisolviHomeSetAgenda(urlConfigurato As String, utente As String, password As String) As String
        Try
            If String.IsNullOrWhiteSpace(urlConfigurato) Then Return ""

            Dim corpoPropfind As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                "<D:prop><D:current-user-principal/><D:principal-URL/><C:calendar-home-set/></D:prop></D:propfind>"

            Dim urlHost As String = ""
            Dim homeSet As String = ""
            Dim principal As String = ""

            For Each partenza As String In PuntiPartenzaDav(urlConfigurato)
                Dim risposta As RispostaDav = EseguiRichiestaDav("PROPFIND", partenza, utente, password, corpoPropfind, "text/xml", "0")
                If risposta Is Nothing Then Continue For
                If risposta.CodiceStato <> 207 AndAlso risposta.CodiceStato <> 200 Then Continue For

                urlHost = risposta.UrlFinale
                homeSet = EstraiPrimoHref(risposta.Corpo, "calendar-home-set")
                principal = EstraiPrimoHref(risposta.Corpo, "current-user-principal")
                If String.IsNullOrWhiteSpace(principal) Then principal = EstraiPrimoHref(risposta.Corpo, "principal-URL")
                If Not String.IsNullOrWhiteSpace(homeSet) OrElse Not String.IsNullOrWhiteSpace(principal) Then Exit For
            Next

            If String.IsNullOrWhiteSpace(urlHost) Then Return ""

            If String.IsNullOrWhiteSpace(homeSet) AndAlso Not String.IsNullOrWhiteSpace(principal) Then
                Dim urlPrincipal As String = RisolviUrlRelativo(urlHost, principal)
                Dim rispostaPrincipal As RispostaDav = EseguiRichiestaDav("PROPFIND", urlPrincipal, utente, password, corpoPropfind, "text/xml", "0")
                If rispostaPrincipal IsNot Nothing AndAlso (rispostaPrincipal.CodiceStato = 207 OrElse rispostaPrincipal.CodiceStato = 200) Then
                    urlHost = rispostaPrincipal.UrlFinale
                    homeSet = EstraiPrimoHref(rispostaPrincipal.Corpo, "calendar-home-set")
                End If
            End If

            If String.IsNullOrWhiteSpace(homeSet) Then Return ""

            Dim urlHomeSet As String = RisolviUrlRelativo(urlHost, homeSet)
            If Not urlHomeSet.EndsWith("/") Then urlHomeSet &= "/"
            Return urlHomeSet
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviHomeSetAgenda (" & If(urlConfigurato, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Come RisolviCollezioneAgenda, ma restituisce TUTTI i calendari scrivibili trovati
    ' sull'account, non solo uno. Serve per la creazione di un evento nuovo: quando un account ha
    ' piu' calendari (es. "Casa" e "Lavoro") non possiamo sapere quale l'utente si aspetta di
    ' vedere sull'iPhone, quindi scriviamo l'evento su tutti quelli validi invece di indovinarne
    ' uno solo, A MENO CHE l'utente non abbia scelto esplicitamente un calendario con
    ' ImpostazioniAccountMail.CalendarioScrittura (vedi InviaEventoAlServer).
    Private Function RisolviTutteLeCollezioniAgenda(impostazioni As ImpostazioniAccountMail, utente As String, password As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then Return risultato
            Dim urlConfigurato As String = impostazioni.UrlAgendaCalDav

            Dim urlHomeSet As String = RisolviHomeSetAgenda(urlConfigurato, utente, password)
            If String.IsNullOrWhiteSpace(urlHomeSet) Then Return risultato

            Dim corpoElenco As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                "<D:prop><D:resourcetype/><D:displayname/><C:supported-calendar-component-set/><D:current-user-privilege-set/></D:prop></D:propfind>"

            Dim rispostaElenco As RispostaDav = EseguiRichiestaDav("PROPFIND", urlHomeSet, utente, password, corpoElenco, "text/xml", "1")
            If rispostaElenco Is Nothing OrElse (rispostaElenco.CodiceStato <> 207 AndAlso rispostaElenco.CodiceStato <> 200) Then
                Return risultato
            End If

            Dim candidati As List(Of String) = TrovaCollezioniValide(rispostaElenco.Corpo, rispostaElenco.UrlFinale, "calendar", True)
            For Each candidato As String In candidati
                Dim finale As String = candidato
                If Not finale.EndsWith("/") Then finale &= "/"
                If Not risultato.Contains(finale) Then risultato.Add(finale)
            Next

            Return risultato
        Catch ex As Exception
            ScriviLog("CLSMail.RisolviTutteLeCollezioniAgenda: " & ex.ToString())
            Return risultato
        End Try
    End Function

    ' Restituisce l'elenco dei calendari CalDAV trovati sull'account (nome per l'utente + url),
    ' incluso iCloud: usa la stessa discovery (principal -> calendar-home-set -> collezioni) di
    ' RisolviTutteLeCollezioniAgenda ma tiene anche il <D:displayname> di ciascuna collezione,
    ' cosi' l'interfaccia puo' proporre all'utente un elenco leggibile ("Casa", "Lavoro", ecc.)
    ' invece dei soli url. Serve per popolare ImpostazioniAccountMail.CalendarioScrittura.
    Public Function OttieniElencoCalendari(chiaveAccount As String) As List(Of CalendarioDav)
        Dim risultato As New List(Of CalendarioDav)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then Return risultato

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim urlHomeSet As String = RisolviHomeSetAgenda(impostazioni.UrlAgendaCalDav, utente, password)
            If String.IsNullOrWhiteSpace(urlHomeSet) Then Return risultato

            Dim corpoElenco As String =
                "<?xml version=""1.0"" encoding=""utf-8""?>" &
                "<D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">" &
                "<D:prop><D:resourcetype/><D:displayname/><C:supported-calendar-component-set/><D:current-user-privilege-set/></D:prop></D:propfind>"

            Dim rispostaElenco As RispostaDav = EseguiRichiestaDav("PROPFIND", urlHomeSet, utente, password, corpoElenco, "text/xml", "1")
            If rispostaElenco Is Nothing OrElse (rispostaElenco.CodiceStato <> 207 AndAlso rispostaElenco.CodiceStato <> 200) Then
                Return risultato
            End If

            risultato = TrovaCalendariConNome(rispostaElenco.Corpo, rispostaElenco.UrlFinale)
            For Each voce As CalendarioDav In risultato
                voce.ChiaveAccount = chiaveAccount
            Next
            Return risultato
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniElencoCalendari (" & If(chiaveAccount, "") & "): " & ex.ToString())
            Return risultato
        End Try
    End Function

    ' Come OttieniElencoCalendari, ma su tutti gli account (attivi di default) che hanno
    ' un'agenda configurata: comodo per popolare in un colpo solo il selettore "dove salvo i
    ' nuovi eventi" quando l'utente ha piu' caselle con CalDAV/iCloud collegate.
    Public Function OttieniElencoCalendariTuttiGliAccount(Optional soloAttivi As Boolean = True) As List(Of CalendarioDav)
        Dim risultato As New List(Of CalendarioDav)
        Try
            For Each a As ImpostazioniAccountMail In OttieniAccountConAgenda(soloAttivi)
                risultato.AddRange(OttieniElencoCalendari(a.Chiave))
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniElencoCalendariTuttiGliAccount: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' Come TrovaCollezioniValide(..., "calendar", True) ma tiene anche il <D:displayname> di
    ' ogni collezione valida invece di scartarlo, e non richiede permessi di scrittura: elenca
    ' TUTTI i calendari usabili per eventi (VEVENT), segnalando in Scrivibile quelli su cui si
    ' puo' anche scrivere. Duplica in parte il parsing multistatus di TrovaCollezioniValide
    ' perche' quella funzione restituisce solo url e qui serve anche il nome.
    Private Function TrovaCalendariConNome(testoXml As String, urlBase As String) As List(Of CalendarioDav)
        Dim risultato As New List(Of CalendarioDav)
        Try
            If String.IsNullOrWhiteSpace(testoXml) Then Return risultato
            Dim documento As New XmlDocument()
            documento.LoadXml(testoXml)

            For Each risposta As XmlNode In documento.SelectNodes("//*[local-name()='response']")
                Dim tipoTrovato As Boolean = False
                Dim hrefRisposta As String = ""
                Dim nome As String = ""
                Dim proprietaComponentiPresente As Boolean = False
                Dim supportaVEvent As Boolean = False
                Dim proprietaPrivilegiPresente As Boolean = False
                Dim consenteScrittura As Boolean = False

                For Each nodoHref As XmlNode In risposta.ChildNodes
                    If String.Equals(nodoHref.LocalName, "href", StringComparison.OrdinalIgnoreCase) Then
                        hrefRisposta = nodoHref.InnerText.Trim()
                        Exit For
                    End If
                Next

                For Each propstat As XmlNode In risposta.SelectNodes(".//*[local-name()='propstat']")
                    Dim statoTesto As String = ""
                    For Each nodoStato As XmlNode In propstat.ChildNodes
                        If String.Equals(nodoStato.LocalName, "status", StringComparison.OrdinalIgnoreCase) Then
                            statoTesto = nodoStato.InnerText
                            Exit For
                        End If
                    Next
                    Dim successoPropstat As Boolean = statoTesto.IndexOf(" 200 ", StringComparison.Ordinal) >= 0 OrElse statoTesto.TrimEnd().EndsWith(" 200")
                    If Not successoPropstat Then Continue For

                    For Each nodo As XmlNode In propstat.SelectNodes(".//*")
                        If String.Equals(nodo.LocalName, "calendar", StringComparison.OrdinalIgnoreCase) Then
                            tipoTrovato = True
                        End If
                        If String.Equals(nodo.LocalName, "displayname", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrWhiteSpace(nome) Then
                            nome = nodo.InnerText.Trim()
                        End If
                        If String.Equals(nodo.LocalName, "supported-calendar-component-set", StringComparison.OrdinalIgnoreCase) Then
                            proprietaComponentiPresente = True
                            For Each figlio As XmlNode In nodo.ChildNodes
                                If String.Equals(figlio.LocalName, "comp", StringComparison.OrdinalIgnoreCase) Then
                                    Dim attributoNome As XmlAttribute = figlio.Attributes("name")
                                    If attributoNome IsNot Nothing AndAlso String.Equals(attributoNome.Value, "VEVENT", StringComparison.OrdinalIgnoreCase) Then
                                        supportaVEvent = True
                                    End If
                                End If
                            Next
                        End If
                        If String.Equals(nodo.LocalName, "current-user-privilege-set", StringComparison.OrdinalIgnoreCase) Then
                            proprietaPrivilegiPresente = True
                            For Each privilegio As XmlNode In nodo.SelectNodes(".//*[local-name()='privilege']")
                                For Each concessione As XmlNode In privilegio.ChildNodes
                                    If String.Equals(concessione.LocalName, "write", StringComparison.OrdinalIgnoreCase) OrElse
                                       String.Equals(concessione.LocalName, "write-content", StringComparison.OrdinalIgnoreCase) OrElse
                                       String.Equals(concessione.LocalName, "all", StringComparison.OrdinalIgnoreCase) Then
                                        consenteScrittura = True
                                    End If
                                Next
                            Next
                        End If
                    Next
                Next

                If Not tipoTrovato OrElse String.IsNullOrWhiteSpace(hrefRisposta) Then Continue For
                If proprietaComponentiPresente AndAlso Not supportaVEvent Then Continue For
                If CollezioneDaScartare(hrefRisposta) Then Continue For

                Dim assoluto As String = RisolviUrlRelativo(urlBase, hrefRisposta)
                If Not assoluto.EndsWith("/") Then assoluto &= "/"
                If risultato.Any(Function(c) String.Equals(c.Url, assoluto, StringComparison.OrdinalIgnoreCase)) Then Continue For

                Dim voce As New CalendarioDav()
                voce.Url = assoluto
                voce.Nome = If(String.IsNullOrWhiteSpace(nome), NomeCalendarioDaUrl(assoluto), nome)
                voce.Scrivibile = Not (proprietaPrivilegiPresente AndAlso Not consenteScrittura)
                risultato.Add(voce)
            Next

            Return risultato
        Catch ex As Exception
            ScriviLog("CLSMail.TrovaCalendariConNome: " & ex.ToString())
            Return New List(Of CalendarioDav)
        End Try
    End Function

    ' Nome "di ripiego" per un calendario privo di <D:displayname> (capita su alcuni server):
    ' ricava qualcosa di leggibile dall'ultimo segmento dell'url invece di lasciarlo vuoto.
    Private Function NomeCalendarioDaUrl(url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return ""
            Dim pulito As String = url.TrimEnd("/"c)
            Dim ultimoSlash As Integer = pulito.LastIndexOf("/"c)
            Dim segmento As String = If(ultimoSlash >= 0, pulito.Substring(ultimoSlash + 1), pulito)
            segmento = Uri.UnescapeDataString(segmento)
            Return If(String.IsNullOrWhiteSpace(segmento), url, segmento)
        Catch ex As Exception
            ScriviLog("CLSMail.NomeCalendarioDaUrl: " & ex.ToString())
            Return If(url, "")
        End Try
    End Function

    ' Se urlCalendariSelezionati e' Nothing o vuoto, mantiene il comportamento di sempre: scarica
    ' gli eventi da un solo calendario, quello predefinito risolto da RisolviCollezioneAgenda (o
    ' quello scelto in ImpostazioniAccountMail.CalendarioScrittura). Se invece l'utente ha scelto
    ' esplicitamente uno o piu' calendari da sincronizzare (vedi CLSAgenda.OttieniCalendariSelezionati),
    ' scarica gli eventi da tutti quelli indicati e marca ciascun evento con il calendario di
    ' provenienza (EventoDav.UrlCalendario), cosi' il chiamante puo' farne fluire gli appuntamenti
    ' nella categoria scelta per quel calendario.
    Public Function OttieniEventiDalServer(chiaveAccount As String, Optional urlCalendariSelezionati As List(Of String) = Nothing) As List(Of EventoDav)
        Dim risultato As New List(Of EventoDav)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return risultato
            End If
            If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then
                Return risultato
            End If

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim calendariDaLeggere As New List(Of String)
            If urlCalendariSelezionati IsNot Nothing Then
                For Each u As String In urlCalendariSelezionati
                    If Not String.IsNullOrWhiteSpace(u) AndAlso Not calendariDaLeggere.Contains(u) Then calendariDaLeggere.Add(u)
                Next
            End If

            If calendariDaLeggere.Count = 0 Then
                Dim urlPredefinito As String = RisolviCollezioneAgenda(impostazioni, utente, password)
                If String.IsNullOrWhiteSpace(urlPredefinito) Then Return risultato
                calendariDaLeggere.Add(urlPredefinito)
            End If

            For Each urlAgenda As String In calendariDaLeggere
                Try
                    For Each coppia As KeyValuePair(Of String, String) In ScaricaEventiConHref(urlAgenda, utente, password)
                        Try
                            Dim evento As EventoDav = AnalizzaEventoDav(coppia.Value)
                            If evento Is Nothing OrElse String.IsNullOrWhiteSpace(evento.Uid) Then Continue For
                            If Not String.IsNullOrWhiteSpace(coppia.Key) Then evento.Href = RisolviUrlRelativo(urlAgenda, coppia.Key)
                            evento.UrlCalendario = urlAgenda
                            risultato.Add(evento)
                        Catch exEvento As Exception
                            ScriviLog("CLSMail.OttieniEventiDalServer evento: " & exEvento.ToString())
                        End Try
                    Next
                Catch exCalendario As Exception
                    ScriviLog("CLSMail.OttieniEventiDalServer calendario (" & If(urlAgenda, "") & "): " & exCalendario.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniEventiDalServer (" & If(chiaveAccount, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function InviaEventoAlServer(chiaveAccount As String, evento As EventoDav) As String
        Try
            If evento Is Nothing Then
                Return ""
            End If
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return ""
            End If
            If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then
                Return ""
            End If

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            If String.IsNullOrWhiteSpace(evento.Uid) Then evento.Uid = Guid.NewGuid().ToString().ToUpperInvariant()

            Dim corpo As String = CostruisciIcs(evento)
            If String.IsNullOrWhiteSpace(corpo) Then Return ""

            Dim urlEsistente As String = If(evento.Href, "").Trim()
            If Not String.IsNullOrWhiteSpace(urlEsistente) Then
                ' Evento gia' sincronizzato in precedenza: sappiamo gia' su quale calendario vive,
                ' aggiorniamo solo quello (nessun motivo per scriverlo altrove).
                If Not EseguiPutWebDav(urlEsistente, utente, password, corpo, "text/calendar") Then Return ""
                Return urlEsistente
            End If

            ' Evento NUOVO. Se l'utente ha scelto esplicitamente un calendario (ImpostazioniAccountMail.
            ' CalendarioScrittura, valorizzabile a partire dall'elenco di OttieniElencoCalendari),
            ' scriviamo SOLO li'. Altrimenti non sappiamo quale calendario dell'account l'utente
            ' considera quello "giusto" sul telefono, quindi lo scriviamo su TUTTI i calendari
            ' scrivibili trovati invece di sceglierne uno a caso: su iCloud i calendari hanno url
            ' opachi tipo /12345678-ABCD-.../ invece di nomi leggibili come "home" o "personal",
            ' quindi nessuna euristica sui nomi funzionerebbe comunque.
            Dim collezioni As New List(Of String)
            If Not String.IsNullOrWhiteSpace(impostazioni.CalendarioScrittura) Then
                Dim urlScelto As String = impostazioni.CalendarioScrittura.Trim()
                If Not urlScelto.EndsWith("/") Then urlScelto &= "/"
                collezioni.Add(urlScelto)
            Else
                collezioni = RisolviTutteLeCollezioniAgenda(impostazioni, utente, password)
            End If
            If collezioni Is Nothing OrElse collezioni.Count = 0 Then
                Return ""
            End If

            Dim nomeFile As String = Uri.EscapeDataString(evento.Uid) & ".ics"
            Dim primoUrlRiuscito As String = ""
            For Each collezione As String In collezioni
                Dim urlTentativo As String = collezione & nomeFile
                If EseguiPutWebDav(urlTentativo, utente, password, corpo, "text/calendar") Then
                    If String.IsNullOrWhiteSpace(primoUrlRiuscito) Then primoUrlRiuscito = urlTentativo
                End If
            Next

            If String.IsNullOrWhiteSpace(primoUrlRiuscito) Then
                Return ""
            End If

            ' Teniamo traccia del primo url riuscito come "canonico" (usato per i futuri
            ' aggiornamenti/eliminazioni di QUESTO evento): se e' stato scritto su piu' calendari,
            ' gli aggiornamenti successivi seguiranno quello, gli altri restano come creati.
            Return primoUrlRiuscito
        Catch ex As Exception
            ScriviLog("CLSMail.InviaEventoAlServer (" & If(chiaveAccount, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function EliminaEventoDalServer(chiaveAccount As String, uidServer As String, Optional urlServer As String = "") As Boolean
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                Return False
            End If
            If String.IsNullOrWhiteSpace(impostazioni.UrlAgendaCalDav) Then
                Return False
            End If

            Dim utente As String = UtenteDav(impostazioni)
            Dim password As String = PasswordDav(impostazioni)

            Dim url As String = If(urlServer, "").Trim()
            If String.IsNullOrWhiteSpace(url) Then
                If String.IsNullOrWhiteSpace(uidServer) Then Return False
                Dim urlAgenda As String = RisolviCollezioneAgenda(impostazioni, utente, password, True)
                If String.IsNullOrWhiteSpace(urlAgenda) Then Return False
                url = urlAgenda & Uri.EscapeDataString(uidServer) & ".ics"
            End If

            Return EseguiDeleteWebDav(url, utente, password)
        Catch ex As Exception
            ScriviLog("CLSMail.EliminaEventoDalServer (" & If(uidServer, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Genera il testo ICS (VCALENDAR/VEVENT) con Ical.Net invece che con concatenazione manuale
    ' di stringhe: Ical.Net si occupa correttamente di folding delle righe lunghe, escaping dei
    ' valori ed emissione conforme a RFC 5545, cosa che il generatore manuale precedente non
    ' garantiva sempre (motivo sospetto dei mancati aggiornamenti visti su alcuni server CalDAV).
    Private Function CostruisciIcs(evento As EventoDav) As String
        Try
            If evento Is Nothing Then Return ""

            Dim inizio As DateTime = evento.Inizio
            Dim fine As DateTime = evento.Fine
            If inizio = DateTime.MinValue Then inizio = DateTime.Now
            If fine = DateTime.MinValue OrElse fine < inizio Then fine = inizio.AddHours(1)

            Dim calendario As New Calendar()
            calendario.ProductId = "-//WinGabriel//Agenda//IT"

            Dim vevento As New CalendarEvent()
            vevento.Uid = evento.Uid
            vevento.DtStamp = New CalDateTime(DateTime.UtcNow, "UTC")
            vevento.Summary = If(evento.Titolo, "")
            If Not String.IsNullOrWhiteSpace(evento.Luogo) Then vevento.Location = evento.Luogo
            If Not String.IsNullOrWhiteSpace(evento.Descrizione) Then vevento.Description = evento.Descrizione

            If evento.TuttoIlGiorno Then
                ' HasTime e' di sola lettura in questa versione di Ical.Net: non si imposta dopo
                ' la costruzione, va passato direttamente al costruttore (DateTime, hasTime).
                vevento.Start = New CalDateTime(inizio.Date, False)
                ' RFC 5545: per un evento "tutto il giorno" DTEND e' esclusivo (il giorno dopo).
                vevento.End = New CalDateTime(fine.Date.AddDays(1), False)
            Else
                ' UTC esplicito (non "floating"): coerente con quanto faceva il generatore
                ' precedente e piu' sicuro su server come iCloud in assenza di un VTIMEZONE.
                vevento.Start = New CalDateTime(inizio.ToUniversalTime(), "UTC")
                vevento.End = New CalDateTime(fine.ToUniversalTime(), "UTC")
            End If

            If Not String.IsNullOrWhiteSpace(evento.RegolaRicorrenza) Then
                Try
                    vevento.RecurrenceRule = New RecurrencePattern(evento.RegolaRicorrenza.Trim())
                Catch exRrule As Exception
                    ScriviLog("CLSMail.CostruisciIcs RRULE (" & evento.RegolaRicorrenza & "): " & exRrule.ToString())
                End Try
            End If
            ' Le date di esclusione (EXDATE) le aggiungiamo piu' sotto direttamente sul testo ICS
            ' gia' serializzato: la proprieta' tipizzata ExceptionDates ha una firma che e' cambiata
            ' piu' volte tra le versioni di Ical.Net (da qui l'errore di compilazione "non e' un
            ' tipo di raccolta"), mentre inserire righe "EXDATE:" nel testo finale e' stabile e non
            ' dipende dalla versione esatta della libreria installata.

            calendario.Events.Add(vevento)

            Dim serializzatore As New CalendarSerializer()
            Dim testoGenerato As String = serializzatore.SerializeToString(calendario)

            If evento.DateEsclusioni IsNot Nothing AndAlso evento.DateEsclusioni.Count > 0 Then
                Dim righeExdate As New StringBuilder()
                For Each dataEsclusa As DateTime In evento.DateEsclusioni
                    If evento.TuttoIlGiorno Then
                        righeExdate.Append("EXDATE;VALUE=DATE:").Append(dataEsclusa.ToString("yyyyMMdd")).Append(vbCrLf)
                    Else
                        righeExdate.Append("EXDATE:").Append(dataEsclusa.ToUniversalTime().ToString("yyyyMMdd\THHmmss\Z")).Append(vbCrLf)
                    End If
                Next
                Dim posizioneFineEvento As Integer = testoGenerato.IndexOf("END:VEVENT")
                If posizioneFineEvento >= 0 Then
                    testoGenerato = testoGenerato.Insert(posizioneFineEvento, righeExdate.ToString())
                End If
            End If

            Return testoGenerato
        Catch ex As Exception
            ScriviLog("CLSMail.CostruisciIcs: " & ex.ToString())
            Return ""
        End Try
    End Function

    ' Legge un VEVENT usando Ical.Net (Calendar.Load) invece del parsing manuale riga per riga:
    ' gestisce da solo folding, escaping, fusi orari e le varie forme di DTSTART/DTEND/EXDATE
    ' previste da RFC 5545, che il parser scritto a mano copriva solo in parte.
    Private Function AnalizzaEventoDav(testoIcs As String) As EventoDav
        Try
            If String.IsNullOrWhiteSpace(testoIcs) Then Return Nothing

            Dim calendario As Calendar = Calendar.Load(testoIcs)
            If calendario Is Nothing OrElse calendario.Events Is Nothing OrElse calendario.Events.Count = 0 Then Return Nothing

            Dim vevento As CalendarEvent = calendario.Events(0)
            Dim evento As New EventoDav()

            evento.Uid = If(vevento.Uid, "")
            evento.Titolo = If(vevento.Summary, "")
            evento.Luogo = If(vevento.Location, "")
            evento.Descrizione = If(vevento.Description, "")
            evento.TuttoIlGiorno = vevento.IsAllDay

            If vevento.Start IsNot Nothing Then
                evento.Inizio = If(vevento.IsAllDay, vevento.Start.Value.Date, vevento.Start.AsUtc.ToLocalTime())
            End If
            If vevento.End IsNot Nothing Then
                Dim fine As DateTime = If(vevento.IsAllDay, vevento.End.Value.Date, vevento.End.AsUtc.ToLocalTime())
                ' RFC 5545: DTEND di un evento "tutto il giorno" e' esclusivo (il giorno dopo).
                If vevento.IsAllDay Then fine = fine.AddDays(-1)
                evento.Fine = fine
            End If
            If evento.Fine = DateTime.MinValue Then evento.Fine = evento.Inizio
            If evento.Fine < evento.Inizio Then evento.Fine = evento.Inizio

            If vevento.RecurrenceRule IsNot Nothing Then
                ' Teniamo la regola cosi' com'e' (stringa RFC 5545) per non perdere componenti
                ' che non gestiamo esplicitamente, coerentemente col comportamento precedente.
                evento.RegolaRicorrenza = vevento.RecurrenceRule.ToString()
            End If

            ' EXDATE la leggiamo a mano dal testo ICS originale (gia' disponibile qui) invece che
            ' dalla proprieta' tipizzata ExceptionDates: la sua firma cambia da una versione
            ' all'altra di Ical.Net (vedi commento in CostruisciIcs), mentre il testo grezzo no.
            EstraiEsclusioniDaTestoIcs(testoIcs, evento)

            Return evento
        Catch ex As Exception
            ScriviLog("CLSMail.AnalizzaEventoDav: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Legge le righe EXDATE (RFC 5545 §3.8.5.1) direttamente dal testo ICS grezzo. Puo' comparire
    ' piu' volte e ogni occorrenza puo' contenere piu' date separate da virgola.
    Private Sub EstraiEsclusioniDaTestoIcs(testoIcs As String, evento As EventoDav)
        Try
            If evento Is Nothing Then Return
            Dim righe As List(Of String) = SciogliRigheContinuazione(testoIcs)
            Dim dentroEvento As Boolean = False

            For Each riga As String In righe
                If riga.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) Then
                    dentroEvento = True
                    Continue For
                End If
                If riga.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase) Then Exit For
                If Not dentroEvento Then Continue For

                Dim posizione As Integer = PosizioneSeparatoreVCard(riga)
                If posizione <= 0 Then Continue For

                Dim chiaveGrezza As String = riga.Substring(0, posizione)
                Dim chiave As String = chiaveGrezza.Split(";"c)(0).Trim().ToUpperInvariant()
                If chiave <> "EXDATE" Then Continue For

                Dim valore As String = riga.Substring(posizione + 1).Trim()
                For Each singolaData As String In valore.Split(","c)
                    Dim dataEsclusa As DateTime = DataIcsSingolaVersoDateTime(singolaData)
                    If dataEsclusa <> DateTime.MinValue Then evento.DateEsclusioni.Add(dataEsclusa)
                Next
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiEsclusioniDaTestoIcs: " & ex.ToString())
        End Try
    End Sub

    ' Converte una singola data/ora ICS (forma "yyyyMMdd\THHmmss[Z]" oppure solo "yyyyMMdd") in
    ' DateTime locale. Usata solo per EXDATE (vedi EstraiEsclusioniDaTestoIcs).
    Private Function DataIcsSingolaVersoDateTime(testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim valore As String = testo.Trim()

            Dim utc As Boolean = valore.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            If utc Then valore = valore.Substring(0, valore.Length - 1)

            Dim risultato As DateTime = DateTime.MinValue
            If valore.Length >= 15 Then
                If DateTime.TryParseExact(valore.Substring(0, 15), "yyyyMMdd\THHmmss", Globalization.CultureInfo.InvariantCulture,
                                          Globalization.DateTimeStyles.None, risultato) Then
                    If utc Then risultato = DateTime.SpecifyKind(risultato, DateTimeKind.Utc).ToLocalTime()
                    Return risultato
                End If
            End If
            If valore.Length >= 8 Then
                If DateTime.TryParseExact(valore.Substring(0, 8), "yyyyMMdd", Globalization.CultureInfo.InvariantCulture,
                                          Globalization.DateTimeStyles.None, risultato) Then
                    Return risultato
                End If
            End If
            If DateTime.TryParse(valore, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            ScriviLog("CLSMail.DataIcsSingolaVersoDateTime (" & If(testo, "") & "): " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Public Function AccountSupportaAgenda(chiaveAccount As String) As Boolean
        Try
            Return AccountSupportaAgenda(OttieniAccount(chiaveAccount))
        Catch ex As Exception
            ScriviLog("CLSMail.AccountSupportaAgenda (" & If(chiaveAccount, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function EsisteAccountConAgenda() As Boolean
        Try
            For Each a As ImpostazioniAccountMail In OttieniElencoAccount(True)
                If AccountSupportaAgenda(a) Then Return True
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EsisteAccountConAgenda: " & ex.ToString())
        End Try
        Return False
    End Function

    Public Function OttieniAccountConAgenda(Optional soloAttivi As Boolean = True) As List(Of ImpostazioniAccountMail)
        Dim risultato As New List(Of ImpostazioniAccountMail)
        Try
            For Each a As ImpostazioniAccountMail In OttieniElencoAccount(soloAttivi)
                If AccountSupportaAgenda(a) Then risultato.Add(a)
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.OttieniAccountConAgenda: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ' ============================================================================================
    ' RILEVAMENTO PROVIDER DI POSTA (Google, Microsoft, ...)
    ' ============================================================================================
    ' Riconoscere il provider di un account guardando solo il dominio dell'indirizzo (gmail.com,
    ' outlook.com, ...) funziona per gli indirizzi "diretti" ma non per un dominio personalizzato
    ' che si appoggia comunque a Google Workspace o Microsoft 365 (es. "posta@miaazienda.it"): in
    ' quel caso il dominio non dice nulla, ma l'account e' a tutti gli effetti Google o Microsoft.
    ' Per riconoscerlo in modo affidabile, oltre ai domini diretti e al nome dei server IMAP/SMTP
    ' gia' configurati, interroghiamo anche i record MX del dominio: chi ospita la posta su Google
    ' Workspace ha sempre MX che puntano a host "...google.com"/"...googlemail.com", chi la ospita
    ' su Microsoft 365 ha sempre MX che puntano a host "...protection.outlook.com". Questo terzo
    ' controllo funziona indipendentemente dall'indirizzo scelto dall'utente e dai server che ha
    ' (o non ha ancora) configurato nell'app.
    '
    ' L'interrogazione MX usa un resolver DNS-over-HTTPS pubblico (dns.google/resolve) via
    ' HttpClient, con lo stesso approccio gia' usato in questo file per l'autoconfig e i
    ' well-known CardDAV/CalDAV: nessuna libreria DNS aggiuntiva, nessun permesso particolare.
    ' Il risultato viene messo in cache per dominio (in memoria, per la durata del processo)
    ' perche' la stessa domanda viene fatta piu' volte nell'arco di una sessione (menu opzioni
    ' account, autoconfigurazione, ecc.) e non ha senso interrogare il DNS ogni volta.

    Public Enum TipoProviderMail
        Sconosciuto = 0
        Google = 1
        Microsoft = 2
    End Enum

    Private Shared ReadOnly _cacheProviderDominio As New Dictionary(Of String, TipoProviderMail)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _lockCacheProviderDominio As New Object()

    ' Domini "diretti" dei due provider: chi si registra con questi indirizzi e' senza dubbio
    ' Google o Microsoft, senza bisogno di nessuna interrogazione di rete.
    Private Shared ReadOnly _dominiGoogleDiretti As String() = {"gmail.com", "googlemail.com"}
    Private Shared ReadOnly _dominiMicrosoftDiretti As String() = {
        "outlook.com", "hotmail.com", "hotmail.it", "live.com", "live.it", "msn.com", "office365.com"
    }

    ''' <summary>
    ''' Vero se l'account e' con ogni probabilita' un account Google (Gmail o Google Workspace,
    ''' anche su dominio personalizzato). Vedi RilevaProvider per come viene stabilito.
    ''' </summary>
    Public Function AccountEGoogle(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            Return RilevaProvider(impostazioni) = TipoProviderMail.Google
        Catch ex As Exception
            ScriviLog("CLSMail.AccountEGoogle: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se l'account e' con ogni probabilita' un account Microsoft (Outlook/Hotmail o
    ''' Microsoft 365, anche su dominio personalizzato). Vedi RilevaProvider per come viene stabilito.
    ''' </summary>
    Public Function AccountEMicrosoft(impostazioni As ImpostazioniAccountMail) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            Return RilevaProvider(impostazioni) = TipoProviderMail.Microsoft
        Catch ex As Exception
            ScriviLog("CLSMail.AccountEMicrosoft: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Riconosce il provider di posta dell'account (Google, Microsoft, o nessuno dei due) in modo
    ''' affidabile anche su un dominio personalizzato, verificando in ordine tre segnali e
    ''' fermandosi al primo che da' una risposta certa:
    ''' 1) dominio "diretto" del provider (gmail.com, outlook.com, ...);
    ''' 2) nome del server IMAP/SMTP/POP3 gia' configurato (es. "smtp.office365.com");
    ''' 3) record MX del dominio dell'indirizzo, interrogati via DNS: e' il segnale che riconosce
    '''    anche un dominio personalizzato ospitato su Google Workspace o Microsoft 365.
    ''' </summary>
    Public Function RilevaProvider(impostazioni As ImpostazioniAccountMail) As TipoProviderMail
        Try
            If impostazioni Is Nothing Then Return TipoProviderMail.Sconosciuto

            Dim email As String = If(impostazioni.IndirizzoEmail, "").Trim().ToLowerInvariant()
            Dim dominio As String = ""
            If email.Contains("@") Then dominio = email.Substring(email.IndexOf("@"c) + 1).Trim()

            ' 1) domini diretti: nessuna ambiguita' possibile
            If Not String.IsNullOrWhiteSpace(dominio) Then
                If _dominiGoogleDiretti.Contains(dominio) Then Return TipoProviderMail.Google
                If _dominiMicrosoftDiretti.Contains(dominio) Then Return TipoProviderMail.Microsoft
                If dominio.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase) Then Return TipoProviderMail.Microsoft
            End If

            ' 2) nome host dei server configurati, se gia' presenti
            Dim daHost As TipoProviderMail = RilevaProviderDaHost(impostazioni.ServerImap)
            If daHost = TipoProviderMail.Sconosciuto Then daHost = RilevaProviderDaHost(impostazioni.ServerSmtp)
            If daHost = TipoProviderMail.Sconosciuto Then daHost = RilevaProviderDaHost(impostazioni.ServerPop3)
            If daHost <> TipoProviderMail.Sconosciuto Then Return daHost

            ' 3) record MX del dominio: l'unico segnale affidabile su un dominio personalizzato
            ' che non ha (ancora) server IMAP/SMTP propri configurati nell'app.
            If Not String.IsNullOrWhiteSpace(dominio) Then Return RilevaProviderDaMx(dominio)

            Return TipoProviderMail.Sconosciuto
        Catch ex As Exception
            ScriviLog("CLSMail.RilevaProvider: " & ex.ToString())
            Return TipoProviderMail.Sconosciuto
        End Try
    End Function

    Private Function RilevaProviderDaHost(host As String) As TipoProviderMail
        Try
            If String.IsNullOrWhiteSpace(host) Then Return TipoProviderMail.Sconosciuto
            Dim h As String = host.Trim().ToLowerInvariant()
            If h.Contains("gmail") OrElse h.Contains("googlemail") OrElse h.Contains("google.com") Then Return TipoProviderMail.Google
            If h.Contains("office365.com") OrElse h.Contains("outlook.office.com") OrElse
               h.Contains("protection.outlook.com") OrElse h.Contains("outlook.com") OrElse
               h.Contains("hotmail.com") Then Return TipoProviderMail.Microsoft
            Return TipoProviderMail.Sconosciuto
        Catch
            Return TipoProviderMail.Sconosciuto
        End Try
    End Function

    ''' <summary>
    ''' Interroga il resolver DNS-over-HTTPS pubblico di Google (dns.google/resolve) per un nome e
    ''' un tipo di record (es. "MX", "SRV") e restituisce il JSON grezzo della risposta, o stringa
    ''' vuota in caso di errore di rete/risposta non riuscita. Punto unico usato sia dal
    ''' riconoscimento del provider via MX (RilevaProviderDaMx) sia dalla ricerca dei record SRV
    ''' RFC 6186 nella discovery della configurazione (InterrogaSrv), per non duplicare la stessa
    ''' chiamata HttpClient in due punti della classe.
    ''' </summary>
    Private Function InterrogaDnsOverHttps(nome As String, tipo As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(tipo) Then Return ""
            ConfiguraReteDav()
            Using gestore As New HttpClientHandler()
                Using client As New HttpClient(gestore)
                    client.Timeout = TimeSpan.FromSeconds(5)
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get,
                        "https://dns.google/resolve?name=" & Uri.EscapeDataString(nome) & "&type=" & tipo)
                        richiesta.Headers.TryAddWithoutValidation("Accept", "application/dns-json")
                        Using risposta As HttpResponseMessage = client.SendAsync(richiesta).GetAwaiter().GetResult()
                            If Not risposta.IsSuccessStatusCode Then Return ""
                            Return risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSMail.InterrogaDnsOverHttps (" & If(nome, "") & ", " & If(tipo, "") & "): " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Interroga i record MX del dominio tramite DNS-over-HTTPS (risolutore pubblico di Google,
    ''' https://dns.google/resolve) e riconosce il provider dall'host di destinazione: e' il modo
    ''' piu' affidabile per capire se un dominio personalizzato e' ospitato su Google Workspace o
    ''' Microsoft 365. Il risultato resta in cache per il resto del processo, per non interrogare
    ''' il DNS ripetutamente per lo stesso dominio. In caso di errore di rete (o assenza totale di
    ''' connessione) restituisce "Sconosciuto" senza sollevare eccezioni: il chiamante ricade
    ''' allora sugli altri segnali gia' verificati (o su un comportamento prudente di default).
    ''' </summary>
    Private Function RilevaProviderDaMx(dominio As String) As TipoProviderMail
        Try
            If String.IsNullOrWhiteSpace(dominio) Then Return TipoProviderMail.Sconosciuto
            Dim chiave As String = dominio.Trim().ToLowerInvariant()

            SyncLock _lockCacheProviderDominio
                Dim inCache As TipoProviderMail
                If _cacheProviderDominio.TryGetValue(chiave, inCache) Then Return inCache
            End SyncLock

            Dim risultato As TipoProviderMail = TipoProviderMail.Sconosciuto
            Try
                Dim json As String = InterrogaDnsOverHttps(chiave, "MX")
                If Not String.IsNullOrWhiteSpace(json) Then risultato = ProviderDaRispostaMx(json)
            Catch exRete As Exception
                ScriviLog("CLSMail.RilevaProviderDaMx (" & chiave & "): " & exRete.Message)
            End Try

            SyncLock _lockCacheProviderDominio
                _cacheProviderDominio(chiave) = risultato
            End SyncLock
            Return risultato
        Catch ex As Exception
            ScriviLog("CLSMail.RilevaProviderDaMx (" & If(dominio, "") & "): " & ex.ToString())
            Return TipoProviderMail.Sconosciuto
        End Try
    End Function

    ''' <summary>
    ''' Estrae dal JSON di risposta di dns.google/resolve (formato DoH standard: campo "Answer",
    ''' voci con "data" contenente l'host di destinazione del record MX) il provider corrispondente,
    ''' cercando direttamente le sotto-stringhe caratteristiche dei due provider: i record MX sono
    ''' poche righe testuali, non serve un parser JSON completo per riconoscerle.
    ''' </summary>
    Private Function ProviderDaRispostaMx(json As String) As TipoProviderMail
        Try
            If String.IsNullOrWhiteSpace(json) Then Return TipoProviderMail.Sconosciuto
            Dim testo As String = json.ToLowerInvariant()
            If testo.Contains("google.com") OrElse testo.Contains("googlemail.com") Then
                Return TipoProviderMail.Google
            End If
            If testo.Contains("protection.outlook.com") Then
                Return TipoProviderMail.Microsoft
            End If
            Return TipoProviderMail.Sconosciuto
        Catch
            Return TipoProviderMail.Sconosciuto
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _smaltito Then Return
            _smaltito = True
            FermaSorveglianzaPosta()
            ChiudiConnessioniImapInCache()
            If _db IsNot Nothing Then _db.Dispose()
        Catch ex As Exception
            ScriviLog("CLSMail.Dispose: " & ex.ToString())
        End Try
    End Sub

End Class