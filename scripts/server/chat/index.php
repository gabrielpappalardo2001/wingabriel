<?php
// Pagina della chat di WinGabriel (wingabriel.net/chat, oppure chat.wingabriel.net se c'è il dominio dedicato). Serve soltanto la pagina, con
// intestazioni di sicurezza molto rigide: niente script né stili che non siano quelli di questa cartella, niente collegamenti verso altri
// siti, niente cornici, niente altre pagine nella stessa origine quando la chat ha il suo dominio.
$host = strtolower($_SERVER['HTTP_HOST'] ?? '');
$host = preg_match('/^[a-z0-9.-]+$/', $host) ? $host : 'wingabriel.net';
// con il dominio dedicato (chat.wingabriel.net) l'account sta sul sito principale: si permette solo quello
$account = strpos($host, 'chat.') === 0 ? 'https://' . substr($host, 5) : '';
$cartella = __DIR__;
$integrita = function (string $file) use ($cartella): string {
    $p = $cartella . '/' . $file;
    return is_file($p) ? 'sha384-' . base64_encode(hash_file('sha384', $p, true)) : '';
};
header('Content-Type: text/html; charset=utf-8');
header('Cache-Control: no-store');
// Può stare in una cornice solo nelle pagine di wingabriel.it (il plugin WordPress dei giochi e della chat); in nessun altro sito.
$cornice = 'https://wingabriel.it https://www.wingabriel.it';
header("Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'" . ($account !== '' ? ' ' . $account : '') . "; img-src 'self'; media-src 'self'; manifest-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors " . $cornice . "; object-src 'none'; require-trusted-types-for 'script'; trusted-types wgpwa; upgrade-insecure-requests");
header('Referrer-Policy: no-referrer');
header('X-Content-Type-Options: nosniff');
// niente X-Frame-Options: non può nominare un sito, e frame-ancestors qui sopra è più preciso
header('X-Permitted-Cross-Domain-Policies: none');
header('Cross-Origin-Opener-Policy: same-origin');
header('Cross-Origin-Resource-Policy: same-origin');
header('Cross-Origin-Embedder-Policy: require-corp');
header('Origin-Agent-Cluster: ?1');
header('Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), clipboard-read=(), interest-cohort=()');
header('Strict-Transport-Security: max-age=31536000; includeSubDomains');
?>
<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="robots" content="noindex">
<title>Chat di WinGabriel</title>
<meta name="theme-color" content="#1b5e3a">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-title" content="Chat">
<link rel="manifest" href="manifest.webmanifest">
<link rel="icon" type="image/png" sizes="192x192" href="icona-192.png">
<link rel="apple-touch-icon" href="apple-touch-icon.png">
<link rel="stylesheet" href="chat.css" integrity="<?= htmlspecialchars($integrita('chat.css')) ?>" crossorigin="anonymous">
</head>
<body>
<header class="testata">
  <h1>Chat</h1>
  <button type="button" id="torna-contatti" class="testata-pulsante" hidden>Contatti</button>
</header>

<main>
  <noscript><p class="pannello errore">Per usare la chat attiva JavaScript.</p></noscript>
  <div id="annunci" class="solo-voce" role="status" aria-live="polite" aria-atomic="true"></div>
  <div id="toast" class="toast" role="status" aria-live="polite"></div>

  <section id="schermata-sblocco" class="pannello" hidden>
    <h2>Accedi alla chat</h2>
    <div class="schede" role="group" aria-label="Scegli cosa fare">
      <button type="button" id="scheda-entra" class="scheda" aria-pressed="true">Ho già un account</button>
      <button type="button" id="scheda-crea" class="scheda" aria-pressed="false">Voglio crearne uno</button>
    </div>
    <form id="modulo-accesso" class="modulo">
      <label for="campo-nome">Nome utente</label>
      <input id="campo-nome" type="text" minlength="2" maxlength="20" autocomplete="username" required>
      <label for="campo-password">Password</label>
      <input id="campo-password" type="password" minlength="8" autocomplete="current-password" required>
      <div id="riga-ripeti" hidden>
        <label for="campo-ripeti">Ripeti la password</label>
        <input id="campo-ripeti" type="password" minlength="8" autocomplete="new-password">
      </div>
      <p class="nota">Nome da 2 a 20 caratteri. Password di almeno 8.</p>
      <label class="spunta"><input id="ricorda" type="checkbox"> Ricordami su questo dispositivo (la password resta cifrata e vale anche per i giochi)</label>
      <button type="submit" id="invio-accesso">Entra</button>
    </form>
    <p id="errore-sblocco" class="errore" role="alert"></p>
    <p><button type="button" id="cancella-tutto" class="secondario">Cancella la chat di questo nome</button></p>
  </section>

  <section id="schermata-chat" hidden>
    <div class="barra">
      <p><strong id="mio-nome"></strong> · <span id="stato-collegamento" role="status">Collegamento in corso</span></p>
      <div class="azioni">
        <button type="button" id="attiva-notifiche" class="secondario" hidden>Notifiche</button>
        <button type="button" id="suoni-chat" class="secondario" aria-pressed="true">Suoni: attivi</button>
        <a class="pulsante secondario" id="vai-giochi" href="../giochi/">Giochi</a>
        <button type="button" id="esci-chat" class="secondario">Esci</button>
      </div>
    </div>
    <div class="colonne">
      <aside class="laterale">
        <div class="pannello">
          <h2>Nuova chat</h2>
          <form id="modulo-aggiungi-nome" class="modulo">
            <label for="nuovo-nome-casella">Nome utente</label>
            <input id="nuovo-nome-casella" type="text" maxlength="20" autocomplete="off" required>
            <button type="submit">Scrivi</button>
          </form>
          <details class="nota">
            <summary>Ho un codice</summary>
            <form id="modulo-aggiungi" class="modulo">
              <label for="nuovo-codice-casella">Codice</label>
              <input id="nuovo-codice-casella" type="text" autocomplete="off" spellcheck="false">
              <button type="submit">Scrivi</button>
            </form>
          </details>
          <p id="errore-aggiungi" class="errore" role="alert"></p>
        </div>
        <nav class="pannello" aria-label="Contatti">
          <h2>Contatti</h2>
          <p id="nessun-contatto">Nessun contatto.</p>
          <ul id="elenco-contatti" class="elenco"></ul>
        </nav>
        <section class="pannello" aria-labelledby="titolo-impostazioni">
          <h2 id="titolo-impostazioni">Impostazioni dell'account</h2>
          <label class="spunta"><input type="checkbox" id="trovabile" checked> Chi cerca il mio nome può scrivermi</label>
          <details class="nota">
            <summary>Il mio codice</summary>
            <div id="mio-codice-riga" class="riga" hidden>
              <label for="mio-codice" class="solo-voce">Il mio codice</label>
              <input id="mio-codice" type="text" readonly>
              <button type="button" id="copia-codice">Copia</button>
            </div>
            <button type="button" id="crea-codice" class="secondario">Codice nuovo</button>
          </details>
          <details class="nota">
            <summary>Persone bloccate</summary>
            <p id="nessun-bloccato">Nessuno.</p>
            <ul id="elenco-bloccati" class="elenco"></ul>
          </details>
          <details class="nota">
            <summary>Elimina il mio account</summary>
            <form id="modulo-elimina" class="modulo">
              <p>L'account vale per la chat e per i giochi. Si cancellano il nome, le partite e la chat su questo dispositivo. Non si può annullare.</p>
              <label for="password-elimina">Password</label>
              <input id="password-elimina" type="password" autocomplete="current-password" required>
              <button type="submit">Elimina il mio account</button>
            </form>
            <p id="esito-elimina" class="errore" role="alert"></p>
          </details>
        </section>
      </aside>
      <section class="conversazione">
        <div id="pannello-vuoto" class="pannello"><p>Scegli un contatto.</p></div>
        <div id="pannello-conversazione" class="pannello" hidden>
          <h2 id="titolo-conversazione" tabindex="-1"></h2>
          <details class="nota">
            <summary id="stato-verifica">Controllo</summary>
            <p>Confronta questo numero con l'altra persona.</p>
            <p class="numero" id="numero-sicurezza"></p>
            <button type="button" id="segna-verificato">Segna come controllato</button>
          </details>
          <div id="riquadro-richiesta" class="richiesta" hidden>
            <p id="testo-richiesta"></p>
            <button type="button" id="accetta-richiesta">Accetta</button>
            <button type="button" id="rifiuta-richiesta" class="secondario">Rifiuta</button>
          </div>
          <p id="attesa-contatto" class="nota" hidden></p>
          <div id="messaggi" class="messaggi" role="log" aria-live="off" aria-label="Messaggi"></div>
          <form id="modulo-scrivi" class="modulo" hidden>
            <label for="testo-messaggio" class="solo-voce">Messaggio</label>
            <textarea id="testo-messaggio" rows="2" maxlength="4000" placeholder="Messaggio"></textarea>
            <button type="submit">Invia</button>
          </form>
          <p id="errore-scrivi" class="errore" role="alert"></p>
          <details class="comandi-extra">
            <summary>Altro</summary>
            <div class="azioni">
              <button type="button" id="rileggi" class="secondario">Rileggi</button>
              <button type="button" id="rinomina" class="secondario">Rinomina</button>
              <button type="button" id="blocca-contatto" class="secondario">Blocca</button>
              <button type="button" id="sblocca-contatto" class="secondario" hidden>Sblocca</button>
              <button type="button" id="elimina" class="secondario">Elimina</button>
            </div>
          </details>
        </div>
      </section>
    </div>
  </section>
</main>
<footer class="piede">
  <p><a href="https://wingabriel.it/privacy-policy" rel="noopener noreferrer">Privacy</a></p>
</footer>
<script src="pwa.js" integrity="<?= htmlspecialchars($integrita('pwa.js')) ?>" crossorigin="anonymous"></script>
<script type="module" src="chat.js" integrity="<?= htmlspecialchars($integrita('chat.js')) ?>" crossorigin="anonymous"></script>
</body>
</html>
