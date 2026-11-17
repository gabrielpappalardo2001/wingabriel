# WGLettore

Il lettore di schermo. È un programma a sé, con la sua soluzione (`WGLettore.sln`), che funziona anche da solo e, se WinGabriel c'è, si integra con lui.

## Cartelle

```
WGLettore/
├── WGLettore.sln                 soluzione: i due progetti VB
├── WGLettoreSchermo/             il motore (libreria): UI Automation, MSAA, navigazione web, tabelle, Braille, OCR, comandi
├── WGEspeak/                     libreria di eSpeak NG: scaricamento, installazione e motore vocale (copia di quella di WinGabriel, senza wgcontrols)
├── WGLettore/                    il programma (WinForms): icona nell'area di notifica, impostazioni, controllo, installazione
├── Nativo/                       programmi in C++: WGLettoreVoce.cpp (voce), WGLettoreServizio.cpp (servizio), WGAiutoLettore.cpp e WGAiutoLettoreCarica.cpp (libreria nei browser) e i loro compila_*.bat
├── crea-wglettore-zip.ps1        crea dist\wglettore.zip
├── Directory.Build.props, salva.cmd   commit automatico a ogni compilazione, come nelle altre soluzioni
```

Compilare: `WGLettore.sln` con MSBuild (la prima volta con `-restore`, per NAudio). I programmi nativi si compilano a parte con `Nativo\compila_WGLettoreVoce.bat`, `Nativo\compila_WGLettoreServizio.bat` e `Nativo\compila_WGAiutoLettore.bat` (quest'ultimo produce `WGAiutoLettore32.dll`, `WGAiutoLettore64.dll`, `WGAiutoLettoreCarica32.exe` e `WGAiutoLettoreCarica64.exe`); i file finiscono in `Nativo` e il progetto WGLettore li copia accanto al suo.

## Come funziona

- **Indipendente.** Non ha bisogno di wgcontrols né di altre librerie di WinGabriel. Le impostazioni e la voce le gestisce da solo (`ConfigLettore`, `WGLettoreVoce.exe`).
- **Voce propria a bassa latenza.** `WGLettoreVoce.exe` ha la struttura di WGSapi: SAPI sintetizza in uno stream in memoria, un thread WASAPI a priorità massima lo preleva ogni 2 ms, volume sui campioni, stop sfumato, segue il dispositivo predefinito.
- **Voce di WinGabriel quando c'è.** Ogni secondo il lettore cerca un `WGSapi.exe` attivo nella stessa sessione. Se lo trova parla attraverso quel processo, caricando `wgcontrols.dll` dalla cartella in cui gira `WGSapi.exe`, e chiude il proprio motore. Se `WGSapi.exe` sparisce o smette di rispondere torna a `WGLettoreVoce.exe` e riprende WGSapi appena ne ricompare uno. Sul desktop sicuro usa sempre il motore proprio.
- **Libreria nei browser.** `WGAiutoLettore32.dll` e `WGAiutoLettore64.dll` si agganciano al thread dell'interfaccia del browser e costruiscono l'indice della pagina dall'interno del processo, come fa NVDA; il lettore la riceve su una pipe `WGAiutoLettore_v1_<pid>`. Se il processo ha un'architettura diversa la inietta `WGAiutoLettoreCarica32.exe` o `WGAiutoLettoreCarica64.exe`. Le librerie si usano dove stanno, senza copie di appoggio: finché un browser le tiene caricate l'aggiornatore non può sostituirle, quindi vanno cambiate a browser chiusi. Se manca, se il browser la rifiuta o se qualcosa va storto, l'indice si costruisce come prima con UI Automation.
- **Configurazione condivisa.** WinGabriel e il lettore si segnano nel registro dell'utente (`HKCU\Software\WinGabriel\PercorsoConfigurazione`) il `config.xml` che condividono; chi ha già il suo lo tiene, chi arriva dopo e non ne ha uno usa quello. Da solo il lettore scrive accanto a sé (`dati\config`) se la cartella è scrivibile, altrimenti in `AppData\WGLettore`. WinGabriel indica il suo con `/config:percorso` quando lo avvia.
- **Silenzio con WinGabriel.** Il lettore riconosce WinGabriel dal nome del processo (`wingabriel`), in qualunque cartella: con WinGabriel in primo piano tace, appena passa in secondo piano parla.
- **Controllo.** Pipe `WGLettoreControllo_<sessione>_<desktop>`: STATO, IMPOSTAZIONI, RICARICA, CONFIG percorso, PAUSA, RIPRENDI, ESCI. Riga di comando: `/impostazioni`, `/esci`, `/avvio`, `/nonelevare`, `/sicuro`, `/installa`, `/installa:portatile`, `/installa:sistema`, `/disinstalla`, `/avvioauto:on|off`, `/config:percorso`.
- **Privilegi.** All'avvio manuale chiede i privilegi di amministratore (per leggere anche le applicazioni avviate come amministratore); se si rifiuta parte lo stesso e lo dice quando comincia a parlare. All'accesso a Windows (`/avvio`) non chiede niente.
- **Nessun log.** Il `Logger` del motore è inerte. Resta solo la scrittura dell'albero (`DiagnosticaAlbero`, comando F10), nei dati accanto alla configurazione (cartella `lettore`).

## Impostazioni

Finestra WinForms in schede (Generale, Voce, Scrittura, Pagine web, Altro, Profili, Sistema). Dalla scheda Voce si sceglie la sintesi vocale da un elenco delle voci SAPI installate (`System.Speech`); la scheda Sistema ha avvio automatico, installazione per accesso e blocco, sospensione e chiusura. Il menu dell'icona nell'area di notifica ha una sola voce, Impostazioni. Il silenzio quando WinGabriel è in primo piano non è un'impostazione: è sempre attivo.

## Smart App Control

Su Windows 11 con Smart App Control attivo (modalità di applicazione) i programmi non firmati con una reputazione sconosciuta possono essere bloccati: succede anche ai file appena compilati, in `%TEMP%` e a quelli scaricati. Gli eventi stanno in Visualizza eventi, Microsoft-Windows-CodeIntegrity/Operational (3077). La soluzione stabile è firmare `WGLettore.exe`, `WGLettoreVoce.exe`, `WGLettoreServizio.exe`, `WGLettoreSchermo.dll` e le quattro parti di `WGAiutoLettore`. Per questo i binari nativi non vanno ricompilati senza necessità.

## Guida

Il testo della guida sta in `WGLettore\Guida\lettore-schermo.txt` ed è incorporato nel programma. Si apre dalla voce Guida dell'icona accanto all'orologio e dal pulsante «Apri la guida del lettore» nelle impostazioni, scheda Sistema. Non è più un capitolo del manuale di WinGabriel.

## Installazione

`WGLettore.exe /installa` apre la procedura guidata e chiede come usare il lettore.

- **Uso portatile** (`/installa:portatile`): i file restano nella cartella da cui si avvia il programma. Parte all'accesso dell'utente. Non va sulla schermata di accesso né sul controllo account utente. Se i file arrivano dallo scaricamento di WinGabriel, vengono copiati nella cartella dell'utente prima di cancellare il lavoro temporaneo.
- **Installazione nel sistema** (`/installa:sistema`, chiede la conferma di amministratore):

1. Copia i file del lettore in `Programmi\WGLettore` (solo i suoi: nessuna libreria di WinGabriel).
2. Registra `WGLettoreServizio` (SYSTEM, avvio automatico). Il servizio, quando compaiono `consent.exe` (UAC) o `LogonUI.exe` (accesso e blocco), lancia `WGLettore.exe /sicuro` sul desktop Winlogon con il token di winlogon.exe; l'installazione rifiuta qualunque cartella diversa da Programmi.
3. Crea l'attività pianificata `WGLettore`: parte all'accesso dell'utente con i privilegi massimi, senza conferma. Se esiste, l'avvio automatico si cambia con `/avvioauto:on|off` (chiede la conferma di amministratore).
4. Dà agli utenti la modifica di `ProgramData\WGLettore`, dove il lettore dell'utente copia le sole impostazioni `lettore_schermo_*` per l'istanza della schermata di accesso.
5. Se NVDA è registrato sul controllo account utente, lo toglie da lì: leva il suo nome (`nvda_nvda_v1` e, nelle copie vecchie, `nvda`) dalla voce `Configuration` degli strumenti di accessibilità di sistema e segna `SecureDesktopAccommodation` come `none`. Non disinstalla NVDA e non chiude il processo.

`/disinstalla` toglie servizio e attività. L'installazione non è mai stata eseguita nei test fatti finora.

## Pacchetto

`crea-wglettore-zip.ps1` crea `dist\wglettore.zip` con `WGLettore.exe`, `WGLettoreSchermo.dll`, `WGLettoreVoce.exe`, `WGLettoreServizio.exe`, le parti di `WGAiutoLettore` se compilate, NAudio e poco altro. WinGabriel lo scarica da `https://www.wingabriel.net/download/wglettore.zip` (menu del lettore, Installazione del lettore). Il caricamento sul sito è a parte.


## Avvio, aggiornamenti e file mancanti

- **Altri lettori**: all'avvio (non sul desktop sicuro) il lettore chiude NVDA, JAWS, l'Assistente vocale e simili (`AvvioLettore.ChiudiAltriLettori`).
- **Icona sul desktop**: la prima volta chiede se la si vuole e ricorda la risposta (`lettore_schermo_icona_desktop`); si cambia da Impostazioni, Sistema. Se esiste l'attività pianificata `WGLettore`, l'icona la avvia senza conferma di Windows.
- **Aggiornamenti e recupero**: come WinGabriel, ma da `https://www.wingabriel.net/wglettore/`. Il sito deve avere `DownloadFile.aspx` (POST `chiave` e `app=wglettore`, risposta XML con `<Voce percorso dimensione hash>` per ogni file) e i file nella stessa cartella. All'avvio, dopo qualche secondo, i file dell'elenco che mancano si riscaricano da soli; se ce n'è di diversi si chiede se aggiornare, e `WGLettoreAggiornatore.exe` (copia di `WGUpdater` con indirizzo e nomi del lettore) chiude il lettore, sostituisce i file e lo riapre. Da Impostazioni, Sistema, «Controlla gli aggiornamenti e i file mancanti».
- La voce è più scattante: velocità predefinita 3 e attesa più corta fra una frase e l'altra.

- **Menu del lettore**: l'icona nell'area di notifica ha Impostazioni e Uscita; Insert più L apre lo stesso menu (comando `MenuLettore`, evento `MenuRichiesto`).

## Profili per programma

Ogni programma può avere la sua quantità di dettagli e i suoi echi di caratteri e parole. Si salvano dal programma con il tasto dei comandi e Control 0 (i valori in uso in quel momento) e si tolgono con Shift 0; oppure dalla scheda Profili delle impostazioni, dove si scrive il nome del programma senza .exe. Stanno nella configurazione come `lettore_schermo_profilo_<programma>` (`p` prolissità, `c` eco dei caratteri, `w` eco delle parole). Quando il programma va in primo piano i valori si applicano in memoria, senza toccare le scelte generali; quando se ne esce tornano quelle.

## Moduli esterni

Un modulo esterno è una libreria .NET Framework 4.8 (Visual Basic o C#) che aggiunge al lettore un programma senza ricompilarlo. Si crea un progetto «Libreria di classi», si aggiunge il riferimento a `WGLettoreSchermo.dll` e si scrive una classe che eredita `ModuloEsternoLettore`. La dll compilata va nella cartella `moduli`, accanto a `WGLettoreSchermo.dll`; il lettore la carica all'avvio. Si carica solo da quella cartella.

```vb
Imports WGLettoreSchermo

Public Class ModuloBloccoNoteInPiu
    Inherits ModuloEsternoLettore

    Public Overrides ReadOnly Property Programmi As String()
        Get
            Return New String() {"notepad++"}
        End Get
    End Property

    Public Overrides Function AlFuoco(frase As String, nomeElemento As String) As String
        Return "editor, " & frase
    End Function

    Public Overrides Function AlTasto(codiceTasto As Integer, ctrl As Boolean, maiuscolo As Boolean, alt As Boolean) As String
        Return ""
    End Function

    Public Overrides Function Formato() As String
        Return ""
    End Function

    Public Overrides Function BarraStato() As String
        Return ""
    End Function
End Class
```

`AlFuoco` riceve la frase che il lettore direbbe e restituisce quella da dire; `AlTasto` può restituire un testo da dire dopo un tasto di spostamento o di modifica; `Formato` e `BarraStato` rispondono ai comandi di formato e di barra di stato. Solo `Programmi` è obbligatorio.

## Primo avvio e voce

- **Frase di avvio.** All'avvio il lettore dice «Lettore di schermo caricato»; finché non l'ha detta, tutto il resto aspetta e viene detto dopo, in ordine. Non la dice sul desktop sicuro.
- **Primo avvio.** Se non c'è nessuna configurazione del lettore (nessuna voce `lettore_schermo_*`), il lettore chiede se installarlo o usarlo portatile; se si sceglie l'installazione, chiede se farlo partire con Windows e sulla schermata di accesso (servizio e attività pianificata) oppure copiarlo soltanto in Programmi (`/installa:sistema-senzaavvio`); poi si scelgono sintesi vocale, voce e velocità, con il pulsante Prova. Chi ha già una configurazione non vede niente e resta con le voci di Windows (`lettore_schermo_motore` = `windows`). La domanda si segna con `lettore_schermo_primo_avvio`.
- **eSpeak.** La libreria `WGEspeak` scarica il pacchetto ufficiale di eSpeak NG (circa dodici megabyte, con la verifica dell'impronta SHA-256) e lo estrae nella cartella `espeak` accanto al programma, o in `AppData\Local\WGLettore\espeak` se lì non si può scrivere. È la voce predefinita per chi comincia adesso (`lettore_schermo_motore` = `espeak`, voce in `lettore_schermo_voce_espeak`). La velocità da meno 10 a 10 è la stessa delle voci di Windows. Se i file di eSpeak mancano, il lettore parla con la voce di Windows.
- **Installazione dal menu.** L'installazione nel sistema e il suo aggiornamento non sono più nelle impostazioni: stanno nel menu del lettore, voce Installazione.

## Uscita, riavvio e componenti aggiuntivi

- **Uscita.** La voce Uscita del menu e il pulsante «Chiudi il lettore» aprono una schermata con tre scelte: Uscita, Riavvio, Riavvio senza componenti aggiuntivi. Il riavvio lancia una nuova copia con `/attendi:<pid>` (aspetta che questa sia uscita) e, nel terzo caso, con `/senzacomponenti` (per quella volta non si carica nessun componente).
- **Componenti aggiuntivi.** Se nella cartella `moduli` c'è almeno una libreria, il menu del lettore ha la voce «Componenti aggiuntivi»: elenco con nome (la proprietà `Nome` della classe, se ridefinita), stato e programmi, e i pulsanti Disattiva o Attiva e Disinstalla. Disattivare, riattivare e disinstallare hanno effetto subito. Le librerie si leggono in memoria, così il file non resta bloccato e si può cancellare con il lettore in funzione. I componenti disattivati sono elencati in `lettore_schermo_componenti_disattivati`.

## Interfaccia dei componenti aggiuntivi, oltre i programmi

Oltre a `Programmi`, `Nome`, `AlFuoco`, `AlTasto`, `Formato` e `BarraStato`, la classe `ModuloEsternoLettore` ha altri membri, tutti facoltativi:

- `Avvia(servizi As ServiziComponente)`: chiamato una volta, quando il componente è caricato. `ServiziComponente` offre `Annuncia` (in coda), `AnnunciaSubito` (interrompe), `ProgrammaInPrimoPiano`, `ElementoConFuoco`, e `Leggi` e `Scrivi` per le impostazioni del componente (nella configurazione del lettore, con il nome del file come prefisso).
- `AlFuocoElemento(elemento As ElementoUi, frase As String)`: come `AlFuoco`, ma con l'elemento. `ElementoUi` ha `Nome`, `Tipo` ("Button", "Edit", "List", "ListItem", "Text", "Window"...), `IdAutomazione`, `NomeClasse`, `Valore`, `Modificabile`, `Genitore()`, `Figli()`, `Discendenti()`, `TestiDiscendenti()` e `DaiFuoco()`.
- `AlEvento(evento As EventoComponente)`: notifiche, regioni vive e avvisi del programma. Nothing lascia fare al lettore, una stringa vuota non fa dire niente, un testo si dice al posto dell'originale.
- `Comandi()` e `EseguiComando(id)`: comandi con un tasto premuto con il tasto dei comandi del lettore (`ComandoComponente`: `Id`, `Descrizione`, `Tasto` come codice VK, `Ctrl`, `Maiuscolo`, `Globale`). Valgono solo quando il programma del componente è in primo piano, a meno che `Globale` sia vero. I tasti che il lettore usa già non si possono prendere.
- `VociMenu()`: voci (`VoceMenuComponente`: `Testo` e `Azione`) che il lettore aggiunge al proprio menu.

### Componente per Telegram

`Componenti\Telegram` (assembly `Telegram.dll`, copiato in `moduli` a ogni compilazione) vale per `telegram` e `unigram`, cioè l'app del Microsoft Store e Telegram Desktop. Pulisce le voci della lista (via le ripetizioni e le parole dei controlli generici; se resta solo il nome, la frase si compone con i testi contenuti), segna come campo di modifica la casella di scrittura, non ripete lo stesso avviso entro quattro secondi. Comandi, con il tasto dei comandi: Control U va all'ultimo messaggio dell'elenco, Control E alla casella di scrittura, Control I rilegge per intero la voce con il fuoco.
