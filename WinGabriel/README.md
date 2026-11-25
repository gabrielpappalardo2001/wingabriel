# WinGabriel

Software multifunzione per Windows, con un menu principale unificato che dà accesso ad agenda, calendario, audiolibri (LibriVox), note, quotidiani, Wikipedia e una sezione riservata. Usa la libreria `wgcontrols.dll` (vedi repo `wgcontrols`) per tutta la parte UI accessibile, TTS e gestione media.

> Documentazione interna per chi mantiene il codice.

---

## Sommario

- [Stack tecnico](#stack-tecnico)
- [Struttura del progetto](#struttura-del-progetto)
- [Architettura ad alto livello](#architettura-ad-alto-livello)
- [Avvio e ciclo di vita](#avvio-e-ciclo-di-vita)
- [Moduli funzionali](#moduli-funzionali)
- [Integrazioni esterne](#integrazioni-esterne)
- [Punti d'attenzione](#punti-dattenzione)
- [Build](#build)

---

## Stack tecnico

| Voce | Valore |
|---|---|
| Linguaggio | VB.NET |
| Target framework | .NET Framework 4.8.1 |
| Piattaforma | Windows (AnyCPU) |
| Tipo output | Eseguibile WinForms (`WinGabriel.exe`) |
| IDE | Visual Studio 2022 |
| Dipendenze chiave | `wgcontrols.dll` (libreria locale), `WGFileManager.dll`, `WGYoutube.dll`, `WGDati.dll`, Newtonsoft.Json, HtmlAgilityPack, System.Data.SQLite, NAudio (via wgcontrols) |
| Runtime asset | `WGUpdater.exe`, `WGService.exe`, `nvdaControllerClient.dll`, `NAudio.dll` (scaricati da `wingabriel.net` se mancanti) |
| Distribuzione | Sito ufficiale `https://www.wingabriel.net/wingabriel/` |

`App.config` fissa il binding redirect su `System.Net.Http 4.1.1.3`.

---

## Struttura del progetto

```
WinGabriel/
├── WinGabriel.sln                  Solution single-project
└── WinGabriel/
    ├── WinGabriel.vbproj           Eseguibile WinForms (.NET 4.8.1)
    ├── App.config                  Binding redirect System.Net.Http
    ├── iniziale.vb                 Module entry-point (Sub main, ~2200 righe)
    ├── Connessione.vb              connessione.VerificaConnessione (Ping a google.com)
    ├── PreavvisoManager.vb         Notifiche appuntamenti dell'agenda
    │
    ├── CLSAlmanacco.vb             Almanacco (data, fase lunare, zodiaco, info giornaliere)
    ├── CLSLibriVox.vb              Catalogo + DB SQLite per audiolibri LibriVox
    ├── ClsWikipedia.vb             Client API Wikipedia italiana (HtmlAgilityPack)
    ├── quotidiani.vb               Aggregatore quotidiani online (RSS/JSON)
    │
    ├── MenuAgenda.vb               UI agenda (3340 righe — il modulo più grande)
    ├── MenuCalendario.vb           UI calendario
    ├── MenuLibriVox.vb             UI sfoglia/scarica/leggi audiolibri
    ├── MenuNote.vb                 UI gestione note (1227 righe)
    ├── MenuQuotidiani.vb           UI elenco testate giornalistiche
    ├── MenuQuotidiano.vb           UI singolo quotidiano
    ├── MenuRiservato.vb            Sezione riservata/admin (884 righe)
    ├── MenuWikipedia.vb            UI ricerca Wikipedia
    │
    ├── My Project/                 Application/AssemblyInfo/Resources/Settings designer
    ├── bin/, obj/                  Output build
    └── packages.config (implicito) Restore NuGet
```

**Statistiche:** ~12.000 righe VB nel solo eseguibile, distribuite su 15 file. `iniziale.vb` (entry-point) e `MenuAgenda.vb` (UI agenda) da soli fanno più del 45% del codice.

---

## Architettura ad alto livello

```
                   ┌──────────────────────────┐
                   │   iniziale.Main()        │
                   │   ─ Mutex single-instance│
                   │   ─ download wgcontrols  │
                   │   ─ aggiornamenti        │
                   │   ─ ping WGService       │
                   └────────────┬─────────────┘
                                │
                                ▼
                   ┌──────────────────────────┐
                   │      Menu principale     │
                   │      (WGMenu da          │
                   │       wgcontrols)        │
                   └────────────┬─────────────┘
                                │ entry per voce
       ┌────────────┬───────────┼───────────┬────────────┬───────────┐
       ▼            ▼           ▼           ▼            ▼           ▼
   MenuAgenda  MenuCalendario MenuLibriVox MenuNote MenuQuotidiani MenuWikipedia
       │                          │                       │             │
       ▼                          ▼                       ▼             ▼
  CLSAgenda                CLSLibriVox             Quotidiani       WikipediaAPI
  (WGDati)                 (DB SQLite)             quotidiani.vb    ClsWikipedia
                                                                      │
                                                                      ▼
                                                              api.php (it.wiki)

                                + PreavvisoManager (timer 20 s) ──► WGMenu di alert
                                + MenuRiservato (sezione speciale)
```

WinGabriel è un **frontend**: la logica accessibile, TTS e file system passano da `wgcontrols`. Qui dentro sta la logica di dominio specifica (almanacco, audiolibri, quotidiani, wiki) e l'orchestrazione dei menu.

---

## Avvio e ciclo di vita

`Module iniziale.Sub main()` (in `iniziale.vb`) fa, in ordine:

1. **Single-instance lock** — `New Mutex(...)` con nome univoco per evitare doppia istanza. Se già acquisito, fa `FindWindow` + `SetForegroundWindow` + `ShowWindow(SW_RESTORE)` sull'istanza esistente.
2. **Stili WinForms** — `Application.EnableVisualStyles`, `SetCompatibleTextRenderingDefault(False)`.
3. **Auto-bootstrap** — `DownloadWgControlsSeNecessario()` scarica `wgcontrols.dll` da `https://www.wingabriel.net/wingabriel/` se mancante.
4. **Verifica connettività** — `New connessione().VerificaConnessione()` (Ping a `www.google.com`).
5. **Auto-update silente** — se online, `AggiornaComponentiSilenti()` confronta `wgcontrols.dll` e `WGUpdater.exe` con la versione remota tramite richieste **HEAD** (timeout 5 s) e in caso aggiorna; le notifiche all'utente passano da `WGDomanda` (componente `wgcontrols`).
6. **Servizio di sistema** — `WGServiceInterop.InviaComando(...)` apre `NamedPipeClientStream` su `WGServicePipe` per dialogare con `WGService.exe` (timeout connect 2 s; risposte normali oppure prefisso `ERROR|`).
7. **UI principale** — costruisce e mostra il menu top-level.

Tutta l'app gira su un solo thread UI (WinForms classico) con `Async Function`/`Task.Run` quando serve I/O di rete.

---

## Moduli funzionali

### `Connessione.vb` — `Public Class connessione`
Una sola funzione: `VerificaConnessione() As Boolean`. Usa `System.Net.NetworkInformation.Ping` su `www.google.com`. Tutte le eccezioni → `False`.

### `CLSAlmanacco.vb` — `Public Class CLSAlmanacco`
Genera il "testo dell'almanacco" del giorno (data, fase lunare, segno zodiacale, settimana, giorno dell'anno, info varie). `OttieniTestoAlmanacco(lat, lon)` è `Async`, costruisce con `StringBuilder` e `CultureInfo("it-IT")`. Usa un `Shared HttpClient` con header `User-Agent: AlmanaccoApp/1.0` (best practice .NET 4.x).

Helper privati: `CalcolaFaseLunare`, `CalcolaSegnoZodiacale`.

### `CLSLibriVox.vb` — `Public Class CLSLibriVox`
Catalogo audiolibri **LibriVox** salvato in SQLite (`Dati\Libri\db.sqlite`). Tipi pubblici: `DatiElenco`, `DettagliLibro`, enum `OrdinamentoLibri (AlfabeticoAZ, PiuRecente, MenoRecente)`. `GetSqlOrder` produce ORDER BY consistente con o senza prefisso di tabella.

API REST sorgente: `https://librivox.org/api/feed/audiobooks?format=json` (definita anche in `WGCentroDiControllo` — l'orchestrazione del download libri è lì).

### `ClsWikipedia.vb` — `Friend Class WikipediaAPI` + `Public Class Wikipedia`
Ricerca su `https://it.wikipedia.org/w/api.php` (action `query` + `list=search`). Eventi: `RicercaTerminata(List(Of Wikipedia))`, `nontrovato`. Parser HTML via **HtmlAgilityPack**. Sezioni rappresentate da `WikipediaSection`.

### `quotidiani.vb` — `Public Class Quotidiani`
Aggregatore feed di quotidiani. Costruttore richiede `urlsito`. Auto-scarica `NAudio.dll` (via `WebClient`) all'init perché usa `WGSuoni` per il "suono di attesa". Stato di paginazione per categoria in `currentCategoryPage`.

### `MenuQuotidiano.vb` (205 righe) e `MenuQuotidiani.vb` (554 righe)
UI rispettivamente per il singolo quotidiano e per l'elenco categorie/testate. Tipi di supporto: `ElencoSiti`, `Sito`, `CategoriaSiti`, `Articolo`, `Categoria` (in `quotidiani.vb`).

### `MenuAgenda.vb` (3340 righe)
Il blocco più corposo. UI completa per appuntamenti dell'agenda — creazione, modifica, ricerca, visualizzazione per data. Dipende da `WGDati.CLSAgenda` (singleton `GetInstance`).

### `MenuCalendario.vb` (287 righe)
Calendario navigabile, integrato con `MenuAgenda`.

### `MenuLibriVox.vb` (549 righe)
Sfoglia catalogo, scarica e riproduce audiolibri. Si appoggia a `WGCentroDiControllo` (in `wgcontrols`) per la coda di download e a `WGMpv` per la riproduzione.

### `MenuNote.vb` (1227 righe)
CRUD note libere. Persistenza in `WGDati.CLSNote`.

### `MenuRiservato.vb` (884 righe)
Sezione "amministrativa" / impostazioni avanzate.

### `MenuWikipedia.vb` (115 righe)
Front-end leggero su `WikipediaAPI`.

### `PreavvisoManager.vb` — `Public Class PreavvisoManager`
Sistema di **promemoria appuntamenti**. Costruisce un `System.Timers.Timer` ogni **20 s**, controlla `CLSAgenda.GetInstance()` e mostra un `WGMenu` di avviso se è imminente (flag separati per 20/15/10 minuti prima). 

Nota importante: `_classiBloccanti = {"WGCampo", "WGCampoTesto", "Campi", "WGProcessoInCorso"}` — se l'utente è dentro uno di questi controlli (input attivo), il preavviso viene **soppresso** per non interrompere. È un esempio di "context-aware notification" basato sul tipo del controllo focused.

Stato deduplicato in `idsNotificati As HashSet(Of String)`.

---

## Integrazioni esterne

| Servizio | Endpoint | Modulo |
|---|---|---|
| Server proprio | `https://www.wingabriel.net/wingabriel/` | bootstrap (DLL/EXE), updater |
| Connettività | Ping `www.google.com` | `Connessione.vb` |
| Wikipedia IT | `https://it.wikipedia.org/w/api.php` | `ClsWikipedia.vb` |
| LibriVox | `https://librivox.org/api/feed/audiobooks` | `CLSLibriVox.vb` |
| Quotidiani | URL passato al costruttore di `Quotidiani` | `quotidiani.vb` |
| Servizio locale | Named pipe `WGServicePipe` (timeout 2 s) | `iniziale.vb → WGServiceInterop` |
| Screen reader | `nvdaControllerClient.dll` + JAWS via `wgcontrols` | `wgcontrols/ScreenReader.vb` |

---

## Punti d'attenzione

**Hard-dependency su `wgcontrols.dll`.** Se manca, l'app prova a scaricarla da `wingabriel.net` (vedi `DownloadWgControlsSeNecessario` in `iniziale.vb`). Per build offline o ambienti firewallati, mettere la DLL accanto all'eseguibile **prima** del primo avvio.

**Accoppiamento di rete con `wingabriel.net`.** Self-update silente all'avvio. È intenzionale (delivery model) ma significa che modifiche locali a `wgcontrols.dll` vengono **sovrascritte** se la versione remota è più recente. Per debugging mirato, disabilitare temporaneamente l'update o lavorare offline.

**`MenuAgenda.vb` è gigante.** 3340 righe in un unico file. Quando ci si tocca dentro, conviene cercare per nome di Sub/Function piuttosto che leggere top-down. Candidato a refactoring se il modulo cresce ancora.

**Eccezioni silenziate.** Pattern ricorrente `Catch ex As Exception` con log via `wgcontrols.Logger`. La UI difficilmente vede l'errore: per debug, leggere `dati\logs\log.txt` accanto all'eseguibile.

**`PreavvisoManager` interroga ogni 20 s.** Il timer è leggero ma valuta `_classiBloccanti` con un controllo a stringa sul tipo del controllo focused. Cambiando i nomi delle classi in `wgcontrols`, **aggiornare anche quest'array** o i preavvisi inonderanno l'utente durante l'input.

**Stato globale del menu.** La navigazione tra menu si appoggia a `MenuStateManager` di `wgcontrols`. Aprire/chiudere menu fuori dal flusso normale può lasciare lo stack in stati inconsistenti.

**Threading WinForms classico.** `Task.Run`/`Async` sono usati solo per HTTP (Almanacco, Wikipedia). Ricordarsi del marshalling sull'UI thread (via `Invoke` o `SynchronizationContext` ereditato da `WGCentroDiControllo`) quando si aggiorna un controllo da una continuation.

**Ping a `google.com`.** `Connessione.VerificaConnessione` è sincrono. In ambienti di rete con DNS lento blocca il main thread per qualche secondo all'avvio. Se diventa un problema, sostituire con check su un endpoint controllato (es. il proprio `wingabriel.net`).

---

## Build

1. Aprire `WinGabriel.sln` in **Visual Studio 2022**.
2. Restore NuGet packages.
3. Mettere `wgcontrols.dll` (e dipendenze: `WGFileManager.dll`, `WGYoutube.dll`, `WGDati.dll`, `WGPlayerMpv.dll`, `NAudio.dll`, `nvdaControllerClient.dll`, `svox-pico.dll`, ecc.) accanto a `WinGabriel.exe`. Per debug locale: configurare l'output di `wgcontrols.sln` perché copi i binari nella cartella di WinGabriel, **oppure** lasciare che il bootstrap di `iniziale.vb` li scarichi al primo run.
4. Configurazione: `Debug|Any CPU` o `Release|Any CPU`. Niente target x86/x64 dedicati nel `.sln` di WinGabriel.
5. Output: `WinGabriel\bin\Debug\WinGabriel.exe`.

Target framework: **.NET Framework 4.8.1**.

---

## File chiave a colpo d'occhio

| File | Cosa contiene | Righe |
|---|---|---|
| `iniziale.vb` | Entry-point, bootstrap, auto-update, IPC con WGService | 2176 |
| `MenuAgenda.vb` | UI completa agenda | 3340 |
| `MenuNote.vb` | UI note | 1227 |
| `MenuRiservato.vb` | Sezione admin | 884 |
| `quotidiani.vb` | Aggregatore feed quotidiani | 750 |
| `CLSLibriVox.vb` | Catalogo SQLite audiolibri | 658 |
| `MenuQuotidiani.vb` | UI elenco testate | 554 |
| `MenuLibriVox.vb` | UI audiolibri | 549 |
| `ClsWikipedia.vb` | API Wikipedia | 372 |
| `PreavvisoManager.vb` | Promemoria agenda (timer 20 s) | 361 |
| `MenuCalendario.vb` | Calendario | 287 |
| `CLSAlmanacco.vb` | Almanacco quotidiano | 227 |
| `MenuQuotidiano.vb` | UI singolo quotidiano | 205 |
| `MenuWikipedia.vb` | UI ricerca wiki | 115 |
| `Connessione.vb` | Ping check | 15 |

## Configurazione guidata e installazione

- **Configurazione guidata** (`ConfigurazioneGuidata.vb`). Parte da sola con un profilo nuovo, dopo il benvenuto, e si ripete da Impostazioni, Sistema, Configurazione guidata. Passi: installare o portable; cartella dati con prova di lettura e scrittura (`WGFileDialog`); sintesi vocale (stesso menu delle impostazioni, più «Ho finito, continua»); sincronizzazione con altri computer (account Filen: accedere o creare, poi controllo dei dati da sincronizzare con `WGSincronizzazioneCloud.Sincronizza`). Tutto con i menu e i campi di WinGabriel; chiudendo un qualsiasi menu o campo la procedura si ferma del tutto, viene segnata come fatta (non riparte da sola) e si può rifare dal menu.
- **Installazione** (`IntegrazioneWindows.vb`). Con la copia: chiede se copiare la configurazione attuale (e i dati). Se sì, la cartella dei dati va scelta obbligatoriamente (in Programmi non si scrive) e `PreparaProfilo` copia configurazione e dati lì, segnando la configurazione guidata come fatta; se no, la copia parte con una configurazione vuota in `%LocalAppData%\WinGabriel` e al primo avvio fa la configurazione completa. In entrambi i casi il puntatore nel registro (`PercorsoConfigurazione`) indica quel `config.xml`. Con `/integrazione:installa /copia` (elevato) il programma, dati esclusi, viene copiato in `Programmi\WinGabriel`, poi WinGabriel esce e riparte da lì tramite l'attività. Crea l'attività pianificata `WinGabriel` (privilegi massimi, nessun trigger) e le icone sul desktop e nel menu Start, che eseguono `schtasks /Run /TN WinGabriel`. Così le icone, l'avvio automatico e i riavvii di WGService partono come amministratore senza alcuna richiesta: `AvvioAmministratore.RilanciaSeServe` usa l'attività se punta a questa copia. Per crearla serve la conferma di amministratore, una volta: WinGabriel si rilancia con `/integrazione:installa` o `/integrazione:rimuovi`.
