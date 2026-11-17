# Verifica la chat di WinGabriel (estensione per Chrome, Edge e Firefox)

La pagina web della chat scarica il suo programma dal server a ogni apertura. Chi controllasse il server potrebbe servirne una versione
modificata che ruba i messaggi. Questa estensione lo scopre: a ogni apertura controlla che la pagina e il programma siano quelli
ufficiali, **firmati da chi rilascia WinGabriel con una chiave che non sta sul server**.

## Come funziona

- Al rilascio, `WGStrumenti` (rilascia-server) calcola le impronte (SHA-256) di `chat.js` e `chat.css` e le firma (Ed25519) in
  `IMPRONTE.firmate.json`. La chiave privata sta solo sul computer di chi rilascia, fuori dal repository.
- L'estensione contiene la chiave pubblica. Nasconde la chat, scarica `IMPRONTE.firmate.json`, ne controlla la firma e la scadenza,
  controlla com'è fatta la pagina e confronta i file con le impronte. Poi mostra «Verificata» oppure blocca la chat con un avviso.
- Il documento scade dopo 90 giorni (e ogni rilascio lo rifirma): un server compromesso non può far passare per buona una versione
  vecchia e difettosa per sempre.

## Limiti (da dire agli utenti)

- Protegge dal sito che serve file sbagliati. Non protegge se il browser o l'estensione stessi sono compromessi.
- Un server molto astuto potrebbe riconoscere la richiesta dell'estensione e servirle file buoni mentre alla pagina serve file
  cattivi: per questo i file si leggono dalla copia che il browser ha già scaricato (cache) e non da una richiesta nuova.
- Va installata una volta e aggiornata se cambia la chiave di firma.
- Non è stata provata dentro un browser vero in questa versione: il collegamento con la pagina va provato installandola.

## Costruire e installare

1. `WGStrumenti rilascia-server -SoloCostruisci` (oppure `node sorgenti\build-estensione.mjs`) crea `verifica.js` qui dentro.
2. Chrome o Edge: `chrome://extensions`, attiva «Modalità sviluppatore», «Carica estensione non pacchettizzata» e scegli questa cartella.
   Firefox: `about:debugging`, «Questo Firefox», «Carica componente aggiuntivo temporaneo» e scegli `manifest.json`.
3. Per distribuirla agli utenti si pubblica come estensione normale (Chrome Web Store, Edge Add-ons, Firefox Add-ons) con un archivio zip
   che contiene `manifest.json`, `verifica.js` e `chiave-pubblica.json`.
