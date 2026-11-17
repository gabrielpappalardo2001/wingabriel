Imports System
Imports System.Collections.Generic
Imports System.Threading.Tasks

' ============================================================================================
' WGPlugin: il contratto tra WinGabriel e i plugin.
'
' Un plugin è una libreria .NET (Framework 4.8.1) che fa riferimento solo a WGPlugin.dll (e a un elenco ristretto di tipi del
' framework: niente file, niente rete, niente riflessione, niente codice nativo; WinGabriel lo controlla prima di caricarlo) e ha una classe pubblica,
' con costruttore senza parametri, che eredita da WGPluginBase. WinGabriel la trova, la crea e chiama Avvia: da lì
' il plugin usa i servizi di IWGServiziPlugin per aggiungere voci di menu, parlare, fare domande, salvare dati.
'
' La versione dell'interfaccia è sempre 1.0 e non cambia mai (WinGabriel carica solo i plugin compilati con la 1.0 di WGPlugin.dll).
' Per questo vale una sola regola: dopo la
' pubblicazione, a questo file si aggiunge e basta. Mai togliere un membro, mai cambiare un nome, un tipo o l'ordine dei
' parametri: un plugin compilato oggi deve funzionare con tutte le versioni future di WinGabriel.
' ============================================================================================

''' <summary>La versione dell'interfaccia dei plugin. Non cambia mai: vedi la regola in testa al file.</summary>
Public NotInheritable Class WGPluginApi

    Public Const Versione As String = "1.0"

    Private Sub New()
    End Sub

End Class

''' <summary>
''' I permessi che un plugin può chiedere all'utente per leggere i dati di WinGabriel. Il plugin li chiede con RichiediAccessoAsync;
''' l'utente decide e può revocarli in ogni momento dalle impostazioni, alla voce Plugin.
''' </summary>
Public NotInheritable Class WGPermessi

    ''' <summary>Leggere le note (non quelle protette da password).</summary>
    Public Const Note As String = "note"

    ''' <summary>Leggere la rubrica (non le rubriche protette da password).</summary>
    Public Const Rubrica As String = "rubrica"

    ''' <summary>Leggere gli appuntamenti dell'agenda (non le agende protette da password).</summary>
    Public Const Agenda As String = "agenda"

    ''' <summary>Aggiungere note (non protette da password).</summary>
    Public Const NoteScrittura As String = "note_scrittura"

    ''' <summary>Aggiungere contatti alla rubrica predefinita.</summary>
    Public Const RubricaScrittura As String = "rubrica_scrittura"

    ''' <summary>Aggiungere appuntamenti all'agenda.</summary>
    Public Const AgendaScrittura As String = "agenda_scrittura"

    ''' <summary>
    ''' Leggere le note protette da password. Il plugin non conosce mai la password: a ogni lettura WinGabriel la chiede all'utente,
    ''' che la scrive in un campo di WinGabriel.
    ''' </summary>
    Public Const NoteProtette As String = "note_protette"

    ''' <summary>Leggere le rubriche protette da password (la password la scrive l'utente a ogni lettura).</summary>
    Public Const RubricaProtette As String = "rubrica_protette"

    ''' <summary>Leggere le agende protette da password (la password la scrive l'utente a ogni lettura).</summary>
    Public Const AgendaProtette As String = "agenda_protette"

    ''' <summary>Spegnere il computer dopo un tempo scelto dall'utente (ProgrammaSpegnimentoAsync).</summary>
    Public Const Spegnimento As String = "spegnimento"

    Private Sub New()
    End Sub

End Class

''' <summary>Un contatto della rubrica, come lo vede un plugin che ha il permesso di leggerla.</summary>
Public NotInheritable Class WGContattoPlugin

    Public Property Nome As String = ""
    Public Property Telefono As String = ""
    Public Property Email As String = ""

End Class

''' <summary>Una voce di un elenco con identificatore: l'identificatore è quello che WinGabriel restituisce quando la voce viene scelta.</summary>
Public NotInheritable Class WGVoceElenco

    Private ReadOnly _id As String
    Private ReadOnly _testo As String

    Public Sub New(id As String, testo As String)
        _id = If(id, "")
        _testo = If(testo, "")
    End Sub

    ''' <summary>L'identificatore della voce, a scelta del plugin. Resta lo stesso anche se l'ordine delle voci cambia.</summary>
    Public ReadOnly Property Id As String
        Get
            Return _id
        End Get
    End Property

    ''' <summary>Il testo che l'utente vede e che la voce legge.</summary>
    Public ReadOnly Property Testo As String
        Get
            Return _testo
        End Get
    End Property

End Class

''' <summary>
''' Un menu di WinGabriel (WGMenu): voci con un'azione, più le opzioni (si aprono con Alt). Si costruisce, si mostra con Mostra e si può
''' cambiare anche dopo che è aperto. Con Esc l'utente torna a quello che c'era prima.
''' </summary>
Public Interface IWGMenuPlugin

    Sub Aggiungi(testo As String, azione As Action)
    Sub AggiungiDopo(vocePrecedente As String, testo As String, azione As Action)
    Sub AggiungiOpzione(testo As String, azione As Action)
    Sub RimuoviVoce(testo As String)
    Sub RinominaVoce(testoAttuale As String, nuovoTesto As String)

    ''' <summary>Porta la selezione sulla voce nella posizione data (da zero).</summary>
    Sub Posiziona(indice As Integer)

    Sub Mostra()
    Sub Chiudi()

    ''' <summary>Toglie un'opzione (quelle che si aprono con Alt) aggiunta con AggiungiOpzione.</summary>
    Sub RimuoviOpzione(testo As String)

End Interface

''' <summary>
''' Un elenco di WinGabriel (WGMenuLista): voci con identificatore, selezione di una voce o di più voci, opzioni (Alt). Si può cambiare
''' anche dopo che è aperto: aggiungere, togliere e aggiornare voci.
''' </summary>
Public Interface IWGElencoPlugin

    ''' <summary>Vero per poter selezionare più voci insieme. Va impostato prima di Mostra.</summary>
    Property Multiplo As Boolean

    ''' <summary>Chiamata con l'identificatore della voce scelta con Invio (elenco a scelta singola).</summary>
    Property AlScelto As Action(Of String)

    ''' <summary>Chiamata con gli identificatori delle voci selezionate quando si conferma (elenco a scelta multipla).</summary>
    Property AlleScelte As Action(Of IList(Of String))

    ''' <summary>L'identificatore della voce su cui si trova la selezione, o una stringa vuota.</summary>
    ReadOnly Property IdCorrente As String

    Sub Aggiungi(id As String, testo As String)
    Sub AggiungiDopo(idPrecedente As String, id As String, testo As String)
    Sub Rimuovi(id As String)
    Sub AggiornaTesto(id As String, nuovoTesto As String)
    Sub Svuota()
    Sub Posiziona(id As String)
    Sub AggiungiOpzione(testo As String, azione As Action)
    Sub RimuoviOpzione(testo As String)

    ''' <summary>
    ''' Aggiunge un'opzione (si apre con Alt) che riguarda la voce su cui si trova la selezione: l'azione riceve l'identificatore di quella
    ''' voce. È il modo di dare a ogni voce le sue azioni (segna come preso, sospendi, elimina...) senza un altro menu dopo la scelta.
    ''' Se non c'è nessuna voce selezionata l'opzione non fa niente.
    ''' </summary>
    Sub AggiungiOpzioneSuVoce(testo As String, azione As Action(Of String))

    Sub Mostra()
    Sub Chiudi()

End Interface

''' <summary>
''' Il campo di testo grande di WinGabriel (WGCampo): per leggere un testo lungo o per scriverne uno, con le opzioni (Alt). Per un testo
''' da scrivere si legge Testo quando AllaChiusura viene chiamata.
''' </summary>
Public Interface IWGCampoPlugin

    ''' <summary>Il testo del campo. Dopo la chiusura è quello che c'era nel campo al momento di chiuderlo.</summary>
    ReadOnly Property Testo As String

    ''' <summary>Chiamata quando il campo viene chiuso.</summary>
    Property AllaChiusura As Action

    Sub AggiungiOpzione(testo As String, azione As Action)
    Sub Mostra()
    Sub Chiudi()

End Interface

''' <summary>
''' Un modulo con più campi (WGCampi): testo, numeri, password, date, orari, testi su più righe, anche campi che si ripetono. Ogni campo ha un
''' identificatore, con cui si legge il valore. Si conferma con la fine dell'ultimo campo; con Esc si esce.
''' </summary>
Public Interface IWGCampiPlugin

    Sub AggiungiTesto(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")
    Sub AggiungiNumero(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")
    Sub AggiungiTestoLungo(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")
    Sub AggiungiPassword(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")
    Sub AggiungiData(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")
    Sub AggiungiOrario(titolo As String, id As String, Optional obbligatorio As Boolean = False, Optional soloLettura As Boolean = False, Optional predefinito As String = "")

    ''' <summary>Un campo che l'utente può ripetere più volte (per esempio più numeri di telefono). limite zero vuol dire nessun limite.</summary>
    Sub AggiungiMultiplo(titolo As String, id As String, Optional obbligatorio As Boolean = True, Optional limite As Integer = 0)

    ''' <summary>Il valore di un campo. Si legge dentro AlConferma.</summary>
    Function Valore(id As String) As String

    ''' <summary>I valori di un campo multiplo.</summary>
    Function Valori(id As String) As IList(Of String)

    ''' <summary>Vero se tutti i campi obbligatori sono stati compilati.</summary>
    Function ObbligatoriCompilati() As Boolean

    Sub AggiornaTesto(id As String, nuovoTesto As String)
    Sub SelezionaCampo(id As String)
    Sub AggiungiOpzione(testo As String, azione As Action)
    Sub RimuoviOpzione(testo As String)

    ''' <summary>Chiamata quando l'utente conferma il modulo (fine dell'ultimo campo). I valori si leggono qui.</summary>
    Property AlConferma As Action

    ''' <summary>Chiamata quando l'utente esce dal modulo.</summary>
    Property AllaChiusura As Action

    ''' <summary>Chiamata con l'identificatore del campo quando l'utente ne conferma uno.</summary>
    Property AlCampoConfermato As Action(Of String)

    Sub Mostra()
    Sub Chiudi()

End Interface

''' <summary>Gli elementi disposti su righe e colonne, come le icone della home (WGGriglia).</summary>
Public Interface IWGGrigliaPlugin

    ''' <summary>Chiamata con la posizione dell'elemento scelto con Invio (da zero).</summary>
    Property AlScelto As Action(Of Integer)

    ''' <summary>La posizione dell'elemento su cui si trova la selezione.</summary>
    ReadOnly Property IndiceCorrente As Integer

    Sub AggiornaVoce(indice As Integer, nuovoTesto As String)
    Sub Mostra()
    Sub Chiudi()

End Interface

''' <summary>
''' Quello che WinGabriel mette a disposizione di un plugin. Ogni plugin riceve un'istanza sua, in Avvia.
''' Le interfacce visive (elenchi, domande, messaggi, campi di testo) sono quelle di WinGabriel, già accessibili: un plugin non deve
''' aprire finestre proprie, perché non sarebbero lette dalla voce.
''' </summary>
Public Interface IWGServiziPlugin

    ''' <summary>Il nome del file del plugin, senza estensione: lo identifica nelle impostazioni.</summary>
    ReadOnly Property Id As String

    ''' <summary>La versione dell'interfaccia in uso (sempre 1.0).</summary>
    ReadOnly Property VersioneApi As String

    ''' <summary>La cartella dove il plugin tiene i suoi dati. Esiste già e nessun altro plugin la usa.</summary>
    ReadOnly Property CartellaDati As String

    ''' <summary>Vero se il plugin è firmato da WinGabriel.</summary>
    ReadOnly Property Firmato As Boolean

    ''' <summary>Dice un testo con la voce di WinGabriel. Con interrompi, taglia quello che sta dicendo.</summary>
    Sub Parla(testo As String, Optional interrompi As Boolean = False)

    ''' <summary>Mostra un messaggio, che la voce legge, e aspetta che sia chiuso. Va usato anche per gli errori.</summary>
    Sub Messaggio(testo As String)

    ''' <summary>Fa una domanda a cui si risponde sì o no. Vero se la risposta è sì; Esc vale no.</summary>
    Function ChiediAsync(domanda As String) As Task(Of Boolean)

    ''' <summary>Chiede un testo con il campo di WinGabriel. alTermine riceve il testo; con Esc non viene chiamato.</summary>
    Sub ChiediTesto(titolo As String, predefinito As String, alTermine As Action(Of String))

    ''' <summary>
    ''' Esegue un lavoro lungo mostrando l'attesa di WinGabriel con il testo dato. Ogni attesa dell'utente deve passare da qui.
    ''' </summary>
    Function EseguiConAttesaAsync(testoAttesa As String, lavoro As Func(Of Task)) As Task

    ''' <summary>
    ''' Apre un elenco di voci. Con Invio sulla voce, alScelto riceve la sua posizione (da zero). Un elenco senza voci non si apre:
    ''' per dire che non c'è niente si usa Messaggio.
    ''' </summary>
    Sub MostraElenco(titolo As String, voci As IList(Of String), alScelto As Action(Of Integer))

    ''' <summary>Aggiunge una voce al menu principale (Menù iniziale, o Menu applicazioni). Si chiama durante Avvia.</summary>
    Sub AggiungiVoceMenuPrincipale(testo As String, azione As Action)

    ''' <summary>Aggiunge una voce al menu Altre funzioni. Si chiama durante Avvia.</summary>
    Sub AggiungiVoceAltreFunzioni(testo As String, azione As Action)

    ''' <summary>Rende disponibile sulla scrivania una funzione con il nome dato.</summary>
    Sub RegistraFunzioneScrivania(nome As String, azione As Action)

    ''' <summary>Pubblica una notifica nel centro notifiche. Con azione, aprendola si esegue l'azione.</summary>
    Sub Notifica(testo As String, Optional azione As Action = Nothing)

    ''' <summary>Legge un'impostazione del plugin. Se non c'è, restituisce una stringa vuota.</summary>
    Function Leggi(chiave As String) As String

    ''' <summary>Salva un'impostazione del plugin, con la configurazione di WinGabriel.</summary>
    Sub Scrivi(chiave As String, valore As String)

    ''' <summary>Scrive un errore nel registro di WinGabriel. Va chiamata nei Try/Catch del plugin.</summary>
    Sub RegistraErrore(origine As String, ex As Exception)

    ''' <summary>
    ''' Chiede un testo lungo, su più righe, con il campo di testo di WinGabriel (Invio va a capo). Si conferma uscendo con Esc e rispondendo
    ''' sì alla domanda. alTermine riceve il testo, con le righe separate da un a capo.
    ''' </summary>
    Sub ChiediTestoLungo(titolo As String, predefinito As String, alTermine As Action(Of String))

    ''' <summary>
    ''' Apre un elenco con gli identificatori, come gli elenchi di WinGabriel (WGMenuLista). Scegliendo una voce con Invio, alScelto
    ''' riceve il suo identificatore. Un elenco senza voci non si apre: per dire che non c'è niente si usa Messaggio.
    ''' </summary>
    Sub MostraElencoConId(titolo As String, voci As IList(Of WGVoceElenco), alScelto As Action(Of String))

    ''' <summary>
    ''' Come MostraElencoConId, ma con più voci selezionabili insieme: alTermine riceve gli identificatori delle voci selezionate quando
    ''' l'utente conferma con Invio.
    ''' </summary>
    Sub MostraElencoMultiplo(titolo As String, voci As IList(Of WGVoceElenco), alTermine As Action(Of IList(Of String)))

    ''' <summary>Crea un menu di WinGabriel (WGMenu). Non si vede finché non si chiama Mostra.</summary>
    Function NuovoMenu(titolo As String) As IWGMenuPlugin

    ''' <summary>Crea un elenco di WinGabriel con gli identificatori (WGMenuLista). Non si vede finché non si chiama Mostra.</summary>
    Function NuovoElenco(titolo As String) As IWGElencoPlugin

    ''' <summary>Crea il campo di testo grande di WinGabriel (WGCampo), per scrivere o leggere un testo lungo. Non si vede finché non si chiama Mostra.</summary>
    Function NuovoCampo(titolo As String, testo As String, soloLettura As Boolean) As IWGCampoPlugin

    ''' <summary>Mostra un testo lungo da leggere (campo di sola lettura).</summary>
    Sub MostraTesto(titolo As String, testo As String)

    ''' <summary>Mostra un testo in HTML da leggere.</summary>
    Sub MostraHtml(titolo As String, html As String)

    ''' <summary>Crea un modulo con più campi (WGCampi). Non si vede finché non si chiama Mostra.</summary>
    Function NuovoModulo() As IWGCampiPlugin

    ''' <summary>Crea una griglia di elementi (WGGriglia). Non si vede finché non si chiama Mostra.</summary>
    Function NuovaGriglia(titolo As String, voci As IList(Of String)) As IWGGrigliaPlugin

    ''' <summary>
    ''' Aggiunge una voce a un qualsiasi menu di WinGabriel, riconosciuto dal titolo (per esempio "Multimedia", "Menu Impostazioni",
    ''' "Altre funzioni"; "Menu principale" è il menu iniziale). Vale per tutti i menu, anche quelli aggiunti a WinGabriel in futuro:
    ''' ogni menu che si apre è subito un punto d'innesto. La voce compare ogni volta che quel menu si apre. Con dopoLaVoce, se esiste
    ''' nel menu, la voce va subito dopo quella. Si chiama durante Avvia.
    ''' </summary>
    Sub AggiungiVoceAMenu(titoloMenu As String, testo As String, azione As Action, Optional dopoLaVoce As String = "")

    ''' <summary>Come AggiungiVoceAMenu, ma aggiunge un'opzione (si apre con Alt).</summary>
    Sub AggiungiOpzioneAMenu(titoloMenu As String, testo As String, azione As Action)

    ''' <summary>
    ''' Chiama azione ogni volta che il menu con quel titolo sta per aprirsi, dandole il menu: con quello il plugin può aggiungere, togliere
    ''' e rinominare le voci, cambiando il menu come vuole (per esempio solo se una condizione è vera). Non si chiama Mostra su quel menu.
    ''' </summary>
    Sub AlAperturaDiMenu(titoloMenu As String, azione As Action(Of IWGMenuPlugin))

    ''' <summary>I titoli dei menu che si sono aperti finora in questa sessione, ciascuno con il quale si può usare AggiungiVoceAMenu.</summary>
    Function MenuAperti() As IList(Of String)

    ' ---- File propri del plugin ---------------------------------------------------------------------------------------------
    ' Un plugin non può usare le funzioni di .NET per i file (System.IO.File, Directory, FileStream e simili): WinGabriel lo
    ' controlla prima di caricarlo e, se le usa, non lo carica. Per i suoi dati usa queste funzioni: lavorano solo nella cartella dei
    ' dati del plugin, con nomi semplici (lettere, numeri, punto, trattino), file fino a 1 MB e 5 MB in tutto.

    ''' <summary>Legge un file dalla cartella dei dati del plugin. Restituisce Nothing se non esiste.</summary>
    Function LeggiFileDati(nome As String) As Byte()

    ''' <summary>Scrive (o sostituisce) un file nella cartella dei dati del plugin.</summary>
    Sub ScriviFileDati(nome As String, dati As Byte())

    ''' <summary>I nomi dei file nella cartella dei dati del plugin.</summary>
    Function ElencaFileDati() As IList(Of String)

    ''' <summary>Cancella un file dalla cartella dei dati del plugin.</summary>
    Sub EliminaFileDati(nome As String)

    ' ---- Internet -----------------------------------------------------------------------------------------------------------
    ' Un plugin non può aprire connessioni di rete da solo (System.Net non è consentito). Per leggere una pagina usa questa funzione:
    ' solo indirizzi https verso siti pubblici (mai il tuo computer o la rete di casa), risposta fino a 2 MB, 15 secondi di attesa,
    ' al massimo 10 richieste al minuto.

    ''' <summary>Scarica il testo di una pagina https. Restituisce Nothing se la richiesta non è consentita o non riesce.</summary>
    Function ScaricaTestoAsync(url As String) As Task(Of String)

    ' ---- Dati di WinGabriel, solo con il permesso dell'utente ---------------------------------------------------------------

    ''' <summary>
    ''' Vero se l'utente ha già concesso il permesso (uno dei valori di WGPermessi). Non chiede niente.
    ''' </summary>
    Function AccessoConcesso(permesso As String) As Boolean

    ''' <summary>
    ''' Chiede all'utente il permesso (uno dei valori di WGPermessi), spiegando con il motivo a cosa serve. Se è già concesso restituisce
    ''' vero senza chiedere. Se l'utente rifiuta, in questa sessione non glielo si richiede più. L'utente può revocarlo dalle impostazioni.
    ''' </summary>
    Function RichiediAccessoAsync(permesso As String, motivo As String) As Task(Of Boolean)

    ''' <summary>Le note (identificatore e titolo), dalla più recente, fino a 200. Serve il permesso WGPermessi.Note; senza, l'elenco è vuoto.</summary>
    Function ElencaNote() As IList(Of WGVoceElenco)

    ''' <summary>Il testo di una nota. Serve il permesso WGPermessi.Note. Restituisce Nothing se non c'è o è protetta da password.</summary>
    Function LeggiNota(id As String) As String

    ''' <summary>I contatti che corrispondono al testo (o tutti, se vuoto), fino a 100. Serve il permesso WGPermessi.Rubrica.</summary>
    Function CercaContatti(testo As String) As IList(Of WGContattoPlugin)

    ''' <summary>Gli appuntamenti fra due date (testo: data e oggetto), fino a 200. Serve il permesso WGPermessi.Agenda.</summary>
    Function LeggiAppuntamenti(da As DateTime, a As DateTime) As IList(Of WGVoceElenco)

    ' ---- Scrittura (permessi NoteScrittura, RubricaScrittura, AgendaScrittura) --------------------------------------------------
    ' Aggiungono soltanto: un plugin non può modificare né cancellare quello che c'è. Senza permesso restituiscono False.

    ''' <summary>Aggiunge una nota (non protetta da password). Serve il permesso WGPermessi.NoteScrittura.</summary>
    Function AggiungiNota(titolo As String, testo As String) As Boolean

    ''' <summary>
    ''' Aggiunge un contatto alla rubrica predefinita (Nome può contenere nome e cognome). Restituisce False se la rubrica predefinita è
    ''' protetta da password. Serve il permesso WGPermessi.RubricaScrittura.
    ''' </summary>
    Function AggiungiContatto(contatto As WGContattoPlugin) As Boolean

    ''' <summary>
    ''' Aggiunge un appuntamento all'agenda predefinita. Le ore sono nel formato "HH:mm" (o vuote). Serve il permesso
    ''' WGPermessi.AgendaScrittura.
    ''' </summary>
    Function AggiungiAppuntamento(oggetto As String, data As DateTime, oraInizio As String, oraFine As String, note As String) As Boolean

    ' ---- Dati protetti da password (permessi NoteProtette, RubricaProtette, AgendaProtette) -------------------------------------
    ' Anche con il permesso, il plugin non ha le password: a ogni chiamata WinGabriel apre un campo in cui l'utente scrive la
    ' password (annullando non si legge niente). Il plugin riceve solo i dati, mai la password. Un'agenda o una rubrica che era
    ' chiusa torna chiusa subito dopo la lettura.

    ''' <summary>I titoli delle note protette da password (non il contenuto). Serve il permesso WGPermessi.NoteProtette.</summary>
    Function ElencaNoteProtette() As IList(Of WGVoceElenco)

    ''' <summary>Il testo di una nota protetta: WinGabriel chiede la password all'utente. Nothing se annulla o sbaglia.</summary>
    Function LeggiNotaProtettaAsync(id As String) As Task(Of String)

    ''' <summary>I contatti di una rubrica protetta: la password (scritta dall'utente) dice quale. Serve WGPermessi.RubricaProtette.</summary>
    Function CercaContattiProtettiAsync(testo As String) As Task(Of IList(Of WGContattoPlugin))

    ''' <summary>Gli appuntamenti di un'agenda protetta: la password (scritta dall'utente) dice quale. Serve WGPermessi.AgendaProtette.</summary>
    Function LeggiAppuntamentiProtettiAsync(da As DateTime, a As DateTime) As Task(Of IList(Of WGVoceElenco))

    ' ---- Spegnimento del computer (permesso Spegnimento) ----------------------------------------------------------------------

    ''' <summary>
    ''' Programma lo spegnimento del computer fra la durata data (fino a 24 ore). Serve il permesso WGPermessi.Spegnimento: se manca
    ''' viene chiesto all'utente. Vero se lo spegnimento è stato programmato. Resta programmato anche se il plugin esce dai suoi menu,
    ''' finché WinGabriel resta aperto; allo scadere l'utente ha un minuto per annullare.
    ''' </summary>
    Function ProgrammaSpegnimentoAsync(durata As TimeSpan) As Task(Of Boolean)

    ''' <summary>Annulla lo spegnimento programmato, se c'è.</summary>
    Sub AnnullaSpegnimento()

    ''' <summary>Quanto manca allo spegnimento programmato; zero se non ce n'è nessuno.</summary>
    Function SpegnimentoRimanente() As TimeSpan

End Interface

''' <summary>La classe da cui ogni plugin deriva.</summary>
Public MustInherit Class WGPluginBase

    ''' <summary>Il nome che l'utente vede nelle impostazioni.</summary>
    Public MustOverride ReadOnly Property Nome As String

    ''' <summary>Una frase che dice a cosa serve.</summary>
    Public Overridable ReadOnly Property Descrizione As String
        Get
            Return ""
        End Get
    End Property

    ''' <summary>Chi l'ha scritto.</summary>
    Public Overridable ReadOnly Property Autore As String
        Get
            Return ""
        End Get
    End Property

    ''' <summary>La versione del plugin, a scelta dell'autore.</summary>
    Public Overridable ReadOnly Property Versione As String
        Get
            Return "1.0"
        End Get
    End Property

    ''' <summary>Chiamato una volta, all'avvio di WinGabriel, quando il plugin è stato creato. Qui si aggiungono voci di menu e si registrano le funzioni.</summary>
    Public MustOverride Sub Avvia(servizi As IWGServiziPlugin)

    ''' <summary>Chiamato quando WinGabriel si chiude. Non fa niente, se non serve.</summary>
    Public Overridable Sub Arresta()
    End Sub

End Class
