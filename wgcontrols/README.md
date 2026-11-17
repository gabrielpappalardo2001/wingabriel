# wgcontrols

Libreria di componenti e servizi che alimenta **WinGabriel**, un ambiente Windows pensato per utenti non vedenti che si appoggia agli screen reader **JAWS** e **NVDA**. La solution `wgcontrols.sln` raggruppa la libreria UI principale, i moduli dati, i servizi multimediali e i binari nativi C++ che fanno da ponte verso lo screen reader, la sintesi SAPI e i servizi di sistema.

> Documentazione interna per chi mantiene il codice. Tono operativo: scopo del modulo, dipendenze, punti d'attenzione.

---

## Sommario

- [Stack tecnico](#stack-tecnico)
- [Struttura della solution](#struttura-della-solution)
- [Architettura ad alto livello](#architettura-ad-alto-livello)
- [Componenti VB.NET](#componenti-vbnet)
- [Componenti C++ nativi](#componenti-c-nativi)
- [Flussi principali](#flussi-principali)
- [Punti d'attenzione](#punti-dattenzione)
- [Build](#build)

---

## Stack tecnico

| Voce | Valore |
|---|---|
| Linguaggi | VB.NET (libreria), C++ (binari nativi), C++/Win32 |
| Target framework | .NET Framework 4.8.1 |
| Piattaforma | Windows (x86 / x64 / AnyCPU) |
| IDE | Visual Studio 2022 |
| Output principali | `wgcontrols.dll` + DLL/exe satelliti caricati a runtime da WinGabriel |
| Dipendenze NuGet ricorrenti | NAudio (audio/MIDI), Newtonsoft.Json, System.Data.SQLite, MediaDevices, FSAPILib (JAWS) |
| Dipendenze native | `nvdaControllerClient.dll`, `svox-pico.dll`, `WGJaws32/64.dll`, `WGSapi.exe`, `WGService.exe` |

Molte dipendenze native sono **scaricate a runtime** da `https://www.wingabriel.net/wingabriel/` se mancano nella cartella dell'applicazione (vedi `ScreenReader.vb`, `WGSuoni.WGMidi`, `iniziale.vb`).

---

## Struttura della solution

```
wgcontrols/
├── wgcontrols.sln                  Solution che include 6 progetti VB
├── wgcontrols/                     Libreria UI/funzionale principale (~50 file .vb)
├── WGDati/                         Modello dati (agenda, brani, comuni, note, librivox)
├── WGFileManager/                  Gestione file, preferiti/recenti, tag MP3, pulizia
├── WGYoutube/                      Client yt-dlp, ricerche e cronologia YouTube
├── WGPlayerMpv/                    UI Form per il player MPV
├── WGMediaPlayer/                  Media player: libreria multimediale, menu Navidrome
├── WGMusica/                       Musica: strumenti musicali, spartiti, sintesi, SoundFont, riproduzione MIDI, configurazione musica
├── WGRegistratore/                 Registratore vocale (progetti, effetti sulla voce, microfono) e Fotocamera (foto, video, anteprima a tutto schermo)
├── WGVideoEditor/                  Editor video: montaggio con linea del tempo, effetti, esportazione con ffmpeg (GPU dove possibile)
├── WGNavidrome/                    Client del server Navidrome (API Subsonic)
├── vb-library/                     server.vb — wrapper VB su svox-pico per TTS Pico
├── c++-lib/                        3 sorgenti C++ + binari prodotti
└── packages/                       NuGet packages restore
```

`WinGabriel\WinGabriel.vbproj` è linkato dalla solution (`..\WinGabriel\WinGabriel\WinGabriel.vbproj`) ma vive nel proprio repo: vedi il README di WinGabriel.

---

## Architettura ad alto livello

```
┌─────────────────────────────────────────────────────────────────┐
│                       WinGabriel.exe (UI)                       │
│       Menu: Agenda · Calendario · LibriVox · Note ·             │
│             Quotidiani · Wikipedia · Riservato                  │
└──────────────┬──────────────────────────────┬───────────────────┘
               │ usa                          │ usa
               ▼                              ▼
       ┌──────────────────┐           ┌────────────────────┐
       │   wgcontrols.dll │           │ WGFileManager.dll  │
       │   (UI + servizi) │           │   WGYoutube.dll    │
       └──┬───┬───┬───┬───┘           │     WGDati.dll     │
          │   │   │   │               └────────────────────┘
          │   │   │   │
   parla  │   │   │   │ legge schermo
          ▼   │   │   ▼
   ┌──────────┐│   │┌──────────────────┐
   │ WGSapi   ││   ││  ScreenReader    │──► JAWS (FSAPILib + WGJaws*.dll)
   │  .exe    ││   ││  (Output)        │──► NVDA (nvdaControllerClient.dll)
   │ (C++ TTS)││   │└──────────────────┘
   └──────────┘│   │
               │   │
        suoni  │   │ utility di sistema
               ▼   ▼
        ┌──────────────┐  ┌────────────────┐
        │  WGSuoni     │  │ WGService.exe  │
        │  (NAudio)    │  │   (C++ Win32)  │
        └──────────────┘  └────────────────┘
                              │
                              ▼ pipe "WGServicePipe"
                          chiamato da iniziale.vb
                          (WGServiceInterop.InviaComando)
```

I tre binari C++ comunicano con la parte VB tramite **socket TCP locali** (JawsTracker, WGSapi) o **named pipe** (WGService): i confini sono process-level, non solo modulari.

---

## Componenti VB.NET

### 1. Progetto `wgcontrols` — libreria UI principale

Tutto il codice condiviso che WinGabriel istanzia. Pubblica:

**Controlli e UI custom**
- `WGCampo`, `WGCampoTesto`, `WGCampoMovimento`, `WGCampoTestoMovimento`, `Campi`, `WGCampiMovimento` — sistema di "campi" navigabili (la primitiva di interazione vocale).
- `WGMenu`, `WGMenuLista`, `WGMenuOpzioni`, `WGList`, `WGListaView` — menu e liste accessibili.
- `WGFileDialog` — dialog file accessibile.
- `WGProcessoInCorso`, `WGMessaggi`, `WGDomanda` — feedback utente (dialog di stato/conferma).
- `WGTastiera` — gestione tastiera/scorciatoie.

**Sintesi vocale e screen reader**
- `WGSpeechManager` (singleton, `OttieniIstanza()`) — wrapper su `System.Speech.Synthesis.SpeechSynthesizer`, espone `OttieniVociInstallate`, `ParlaAsync`, `FermaLettura`. Tono: voci SAPI di sistema.
- `sintesivocale` — orchestratore TTS più sofisticato. Implementa `IDisposable`, listener TCP, parla via `Output` (verso JAWS/NVDA), integra **NAudio** per riprodurre buffer wav, gestisce **Pico** in-process e dialoga con `WGSapi.exe` su socket. Espone eventi `OnTestoRicevuto`, `OnFineLettura`, `OnParolaLetta`.
- `ScreenReader.vb` (Friend `Output`, singleton) — punto unico di output verso screen reader. Usa `FSAPILib` (Freedom Scientific API per JAWS) e `nvdaControllerClient.dll` (auto-scaricata se mancante).

**Audio e media**
- `WGSuoni` (con `WGMidi` interno) — riproduce suoni e MIDI tramite NAudio. Auto-scarica `NAudio.dll` se manca.
- `WGMpv` — wrapper su processo `mpv.exe` via **named pipe**, espone eventi `MediaCaricato`, `FineFile`, `ErroreMedia`, `CambioProprieta`. Singleton thread-safe.
- `WGYoutubePlayer` — player YouTube (656 righe) integrato col flow di lettura.

**Word processor interno**
- Famiglia `WGWord*`: `WGWord`, `WGWordData`, `WGWordRender`, `WGWordInternal`, `WGWordUtils`, `WGWordSqlite`, `WGWordGestoreMovimento`, più tipi di supporto (`UndoState`, `DocParagraph`, `DocTable*`, `DocRun`). Gestisce un modello di documento con tabelle e undo, persistito su SQLite.

**Centro di controllo operazioni**
- `WGCentroDiControllo` (singleton) — coordina operazioni lunghe (file copia/sposta/elimina anche su dispositivi MTP/WPD via `MediaDevices`, video, archivi, audio, Bluetooth, download dipendenze, LibriVox). Usa enum `StatoOperazioneCorrente`, `TipoOperazioneFile`, `TipoOperazioneLibriVox`. Lavora in coda con lock dedicato (`_lockCoda`) e ha un `SynchronizationContext` per il marshalling sull'UI thread.

**Utility**
- `Logger` (`Logging.vb`) — log su `dati\logs\log.txt` via P/Invoke `kernel32!CreateFile/WriteFile` con sharing, formattazione timestamp.
- `WGConfig`, `WGDizionario`, `WGEmogi`, `WGUtils` (Module).

**Gestione menu**
- `MenuStateManager`, `MenuState` — stack/history per navigazione menu nidificati.

### 2. Progetto `WGDati` — modello dati

Persistenza e logica di dominio:
- `CLSAgenda`, `GuidaAgenda` — appuntamenti.
- `CLSBrani` con `Sessione`, `BranoPreferito`, `CategoriaBrano`, `MetadatiBrano`, `LegacySessione`, `LegacyBranoPreferito` — gestione brani audio (preferiti + sessione corrente, con migrazione da formato legacy).
- `CLSComuni` con `Comune`, `ComuneElenco`, `ComuneComparer` — anagrafica comuni italiani.
- `CLSDati` con `Valore`, `Dato`, `elencodato` — store dati generico.
- `CLSNote` con `Nota`, enum `OrdinamentoNote`.

### 3. Progetto `WGFileManager` — file system

- `CLSFile`, `FileUtils`, `PathDetails` — primitive file/path.
- `CLSTagMp3` — lettura/scrittura tag ID3.
- `editor`, `EditorSystem` — editor testuale interno.
- `Preferiti`, `FilePreferiti`, `Recenti`, `GestoreBraniPreferitiERecenti` — preferiti/recenti.
- `MenuFile`, `brani.vb` — UI selezione file/brani.
- `WGPuliziaPC` — utility di pulizia.

### 4. Progetto `WGYoutube` — YouTube/yt-dlp

- `YtdlpClient` — client del processo `yt-dlp` (search, download).
- `XmlManager` — persistenza preferiti/cronologia in XML.
- Modello: `YoutubeSearchResult`, `YoutubeComment`, `YoutubeItemDetails`, `ElementoSalvato`, `ElementoCronologia`, `ContenitorePreferiti`, `ContenitoreRecenti`, enum `YoutubeItemType`.
- `MenuYoutube` — UI menu.

### 5. Progetto `WGPlayerMpv` — UI player

- `WGPlayerGui`, `WGPlayerInt` (`WGPlayerFormInt`) — Form Windows che pilota `WGMpv` via i suoi eventi.

### 6. Progetto `WGMediaPlayer` — libreria multimediale

Voce **Media player** del menu principale; sostituisce "Ascolto file multimediali" di Gestione file.

- `MenuMediaPlayer` — menu: Brani preferiti e Brani recenti (delegano a `GestoreBraniPreferitiERecenti` di `WGFileManager`, quindi stesso database `library.db`), Navidrome (se non c'è nessun server chiede «Non sono presenti server Navidrome. Aggiungerne uno?»: sì avvia il collegamento, no chiude), Opzioni.
- `MenuMediaPlayer.Opzioni` — modalità di apertura, ordine di preferiti e recenti, **Opzioni avanzate → Server Navidrome** (collega, verifica, modifica, scollega).
- `MenuNavidrome` / `MenuNavidrome.Brani` — ricerca, artisti, album, generi, playlist, preferiti, brani casuali, radio del server, gestione libreria (scansione).
- `MediaPlayerImpostazioni` — usa le stesse chiavi di configurazione della vecchia schermata (`modalitaAperturaMedia`, `ordinamento_brani_preferiti`, `ordinamento_brani_recenti`): le preferenze dell'utente restano valide, nessuna importazione da fare.
- `ElencoMedia` — involucro di `WGMenuLista` (Esc chiude, gli elenchi si impilano); `RiproduzioneMedia` — avvio in sottofondo o nel lettore.

### 7. Progetto `WGNavidrome` — client Navidrome

Nessuna interfaccia, solo accesso al server tramite API Subsonic 1.16.1 (`/rest/*.view`).

- `NavidromeClient` — chiamate asincrone; accesso con token (MD5 di password e sale casuale), errori tradotti in italiano in `NavidromeEccezione`. `IndirizzoStream` costruisce l'URL di ascolto per mpv.
- `NavidromeConfigurazione` — indirizzo, utente e password in `WGConfig` (`navidrome_*`); la password è cifrata con DPAPI dell'utente corrente, quindi non si decifra su un altro computer o utente.
- `NavidromeModelli` — brani, album, artisti, playlist, generi, radio.

### 9. Lettore di schermo

Il lettore di schermo non è più in questa soluzione: vive in `..\WGLettore` (soluzione `WGLettore.sln`, vedi il suo README). WinGabriel lo avvia e lo comanda con `LettoreEsterno.vb` e lo installa con `InstallatoreLettore.vb`; i punti di contatto qui in wgcontrols sono `WGConfig` e `WGDatabase` (puntatore di registro alla configurazione condivisa) e `sintesivocale` (chiude solo i WGSapi orfani).

### 10. `vb-library/server.vb` — TTS Pico embedded

Module standalone che incapsula **svox-pico** (sintesi TTS open source) tramite P/Invoke su `svox-pico.dll`. Usa una `Logger` semplificata che scrive su `dati\logs\log.txt`. Funzioni esportate dalla DLL Pico: `pico_initialize`, `pico_terminate`, `pico_loadResource`, eccetera. Costanti chiave: `SVOX_MEMORY_SIZE = 3 MB`, buffer 4 KB. Compilato come `WGPico.exe` (vedi cartella).

---

## Componenti C++ nativi (`c++-lib/`)

### `JawsTracker.cpp` → `WGJaws32.dll`, `WGJaws64.dll`

Hook leggero da iniettare/caricare in JAWS per inoltrare il testo letto a un server TCP locale.

API esportata (C linkage, `__declspec(dllexport)`):
- `Inizializza()` — apre socket TCP verso `127.0.0.1:12345`.
- `Invia(prefisso, testo)` — concatena `prefisso + ":" + testo + "\n"`, converte in UTF-8 e invia.
- `Chiudi()` — chiude socket e `WSACleanup`.

Stato globale: una `SOCKET sock`. Ascoltatore lato VB: `sintesivocale` (la porta 12345 è hard-coded da entrambe le parti).

### `WGSapi.cpp` → `WGSapi.exe`

Server TTS che usa **SAPI 5** (`sapi.h`/`sphelper.h`) e **SAPI OneCore** (registry `HKLM\SOFTWARE\Microsoft\Speech_OneCore\Voices`). Architettura:

- **Multi-transport**: enum `TransportType { None, TCP, Pipe, SHM }`, `std::atomic<TransportType> activeTransport`. Sceglie a runtime fra TCP, named pipe e shared memory.
- **TCP**: bind su `127.0.0.1`, costanti `KEEPALIVE_TIME=5000`, `KEEPALIVE_INTERVAL=1000`, `HANDSHAKE_TIMEOUT_MS=5000`, `MAX_INVALID_PACKETS=5`.
- **Audio**: usa **WASAPI** (`mmdeviceapi.h`, `audioclient.h`) con flag `AUTOCONVERTPCM` + `SRC_DEFAULT_QUALITY` per resampling automatico.
- **Crypto**: `bcrypt` + `wincrypt` per handshake/auth.
- **Logging**: rotazione a `MAX_LOG_FILE_SIZE = 5 MB`.
- Usa `std::thread`, `std::atomic`, `std::mutex`, `std::queue` per coda di comandi.

Lato VB: client gestiti da `sintesivocale`.

### `WGService.cpp` → `WGService.exe`

Servizio utility eseguito a parte. Funzionalità:
- `LogToFile` thread-safe (`std::mutex logMutex`) su `<baseDir>\dati\logs\logservice.txt`.
- `RunCommandHidden` — `CreateProcessA` con `CREATE_NO_WINDOW`, timeout 15 s.
- `IsProcessRunning` — `CreateToolhelp32Snapshot` su processi Unicode.
- API WLAN (`wlanapi.h`) e Bluetooth (`BluetoothAPIs.h`) per query rete.

Comunicazione con WinGabriel: **named pipe** `WGServicePipe`. Vedi `iniziale.vb → WGServiceInterop.InviaComando` (timeout 2 s, formato `comando` request → `risposta` line; errori prefissati `ERROR|`).

---

## Flussi principali

### Avvio di WinGabriel
1. `iniziale.main()` crea un `Mutex` per impedire doppia istanza.
2. `DownloadWgControlsSeNecessario()` scarica `wgcontrols.dll` da `wingabriel.net` se mancante.
3. `connessione.VerificaConnessione()` → se online, `AggiornaComponentiSilenti()` aggiorna `wgcontrols.dll` e `WGUpdater.exe` con HEAD request.
4. Ping a `WGService.exe` via `WGServiceInterop.InviaComando` (pipe `WGServicePipe`).
5. Avvia UI con menu principale.

### Sintesi vocale
- Voci SAPI: `WGSpeechManager.OttieniIstanza().ParlaAsync(testo, voce, rate, volume)`.
- Voci OneCore o pipeline custom (Pico, WGSapi.exe): `sintesivocale` — instrada via TCP/pipe/SHM in base a `modalitaCorrente` e produce evento `OnFineLettura`.

### Lettura schermo (output verso screen reader)
- `Output.GetInstance()` (singleton in `ScreenReader.vb`) tiene `JawsOutput` (FSAPILib) e `nvda` (P/Invoke su `nvdaControllerClient.dll`).
- Il chiamante invia testo a entrambi: chi è attivo lo legge.

### Riproduzione media
- Audio/MIDI brevi → `WGSuoni`.
- Audio/video lunghi → `WGMpv` lancia `mpv.exe` con pipe IPC. Eventi → `WGPlayerGui`/`WGPlayerInt` o `WGYoutubePlayer`.

### Operazioni file lunghe
- `WGCentroDiControllo` (singleton) accoda `TipoOperazioneFile` e gestisce stato `StatoOperazioneCorrente`. Le copie su MTP usano `MediaDevices` (vedi `WPD_PREFIX = "WPD|"`).

---

## Punti d'attenzione

**Singleton ovunque.** `WGSpeechManager`, `sintesivocale`, `Output`, `WGMidi`, `WGMpv`, `WGCentroDiControllo` sono tutti singleton lazy con double-checked locking. Stato globale, attenzione a test/teardown.

**Download a runtime.** `nvdaControllerClient.dll`, `NAudio.dll`, `wgcontrols.dll`, `WGUpdater.exe` vengono scaricati da `https://www.wingabriel.net/wingabriel/` se mancanti. Punto di rete da considerare per build offline o ambienti firewallati.

**P/Invoke pesante.** `Logging.vb` apre log via `kernel32!CreateFile` invece che `StreamWriter` — scelto per il file sharing flags. `WGCentroDiControllo` ha `CopyFile`/`MoveFile`/`DeleteFile` da `kernel32` per controllo fine sull'overwrite.

**Eccezioni silenziate.** Diversi `Catch ex As Exception` finiscono solo nel logger (es. `Logger.ScriviLog` interno ha pure un `Catch ex As Exception` vuoto). Verificare prima di assumere che un errore sia stato visto.

**Constanti di rete hard-coded.** Porta `12345` per JawsTracker, `127.0.0.1` ovunque, pipe name `WGServicePipe`. Cambiandone una sola ne va aggiornata anche la controparte.

**Nomi misti italiano/inglese.** Convenzione informale: dominio (Comune, Brano, Agenda) e UI in italiano, framework/Win32 in inglese. Quando si introduce nuovo codice, restare nello stile del file circostante.

**SQLite condiviso.** Più moduli aprono DB SQLite (`WGWordSqlite`, `WGCentroDiControllo` con `NOME_FILE_DB = "db.sqlite"`). Fare attenzione a connessioni e file lock concorrenti.

---

## Build

1. Aprire `wgcontrols.sln` in Visual Studio 2022.
2. Restore NuGet packages (`packages/`).
3. Per i binari C++: compilare separatamente da Developer Command Prompt o con un progetto C++ esterno (al momento non esiste un `.vcxproj` nella solution — i `.exe`/`.dll` sono già nel repo).
4. Configurazione di default `Debug|AnyCPU`. Configurazioni `x86` e `x64` esistono per `wgcontrols`; gli altri progetti compilano `AnyCPU`.
5. Output: `wgcontrols\bin\Debug\wgcontrols.dll` (e dipendenze copiate). WinGabriel lo carica dalla propria cartella.

Target framework: **.NET Framework 4.8.1** (`v4.8.1`). Niente .NET 6+/8 al momento.

---

## File chiave a colpo d'occhio

| File | Cosa contiene |
|---|---|
| `wgcontrols/sintesivocale.vb` | TTS orchestrator (1941 righe) |
| `wgcontrols/WGCentroDiControllo.vb` | Coda operazioni di sistema |
| `wgcontrols/ScreenReader.vb` | Output verso JAWS/NVDA |
| `wgcontrols/WGSpeechManager.vb` | Wrapper SAPI semplice |
| `wgcontrols/WGMpv.vb` | Controllo processo mpv |
| `wgcontrols/Logging.vb` | Logger via P/Invoke |
| `vb-library/server.vb` | Wrapper svox-pico |
| `c++-lib/JawsTracker.cpp` | Hook JAWS → socket TCP |
| `c++-lib/WGSapi.cpp` | Server TTS SAPI multi-transport |
| `c++-lib/WGService.cpp` | Servizio utility con pipe |
