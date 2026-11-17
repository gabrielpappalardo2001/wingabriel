// Estensione "Verifica la chat di WinGabriel" (stile Code Verify). Gira dentro la pagina della chat, prima che il suo programma lavori:
//   1. nasconde la chat finché non ha finito di controllare;
//   2. scarica IMPRONTE.firmate.json e ne controlla la firma con la chiave ufficiale che sta in questa estensione (non sul sito);
//   3. controlla com'è fatta la pagina (un solo script con la sua impronta, nessuno script nel testo, niente cornici);
//   4. confronta chat.js e chat.css con le impronte firmate.
// Se tutto torna mostra "verificata"; se no blocca la chat e dice di non usarla. Non può proteggere dal caso in cui il sito
// riconosca l'estensione e le serva file diversi da quelli che serve a te: per questo i file vengono letti dalla copia che il
// browser ha già scaricato per la pagina (cache), non da una richiesta nuova.
import { controllaManifest, fileConforme, paginaConforme } from '../sorgenti/verifica-core.js';
import chiave from './chiave-pubblica.json';

const ID_STILE = 'wgv-nascondi';

function nascondi() {
  const st = document.createElement('style');
  st.id = ID_STILE;
  st.textContent = 'main, .piede { visibility: hidden !important; }';
  (document.head || document.documentElement).append(st);
}

function mostra(ok, testo, dettagli) {
  const st = document.getElementById(ID_STILE);
  const banner = document.createElement('div');
  banner.setAttribute('role', ok ? 'status' : 'alert');
  banner.setAttribute('aria-live', ok ? 'polite' : 'assertive');
  banner.style.cssText = 'position:sticky;top:0;z-index:2147483647;padding:.6rem 1rem;font:600 16px/1.4 Segoe UI,system-ui,sans-serif;color:#fff;background:' + (ok ? '#1b5e3a' : '#b71c1c') + ';';
  banner.textContent = testo;
  if (!ok && dettagli && dettagli.length) {
    const ul = document.createElement('ul');
    for (const d of dettagli) { const li = document.createElement('li'); li.textContent = d; ul.append(li); }
    banner.append(ul);
  }
  document.body.prepend(banner);
  if (ok) { if (st) st.remove(); return; }
  // problema: la chat resta nascosta e non si può usare
  const main = document.querySelector('main');
  if (main) { main.setAttribute('inert', ''); main.hidden = true; }
  if (st) st.remove();
}

async function leggi(url, cache) {
  const r = await fetch(url, { cache, credentials: 'omit', referrerPolicy: 'no-referrer' });
  if (!r.ok) throw new Error('non riesco a leggere ' + url.split('/').pop());
  return new Uint8Array(await r.arrayBuffer());
}

async function controlla() {
  const base = location.origin + location.pathname.replace(/[^/]*$/, '');
  const problemi = [];
  let manifest = null;
  try {
    const doc = JSON.parse(new TextDecoder().decode(await leggi(base + 'IMPRONTE.firmate.json', 'no-store')));
    const r = controllaManifest(doc, chiave.chiavePubblica, Date.now());
    if (!r.ok) problemi.push(r.motivo); else manifest = r.manifest;
  } catch (e) { problemi.push('Il documento delle impronte firmate non c\'è o non si legge: ' + e.message + '.'); }
  if (manifest) {
    const p = paginaConforme(document, manifest);
    for (const x of p.problemi) problemi.push('La pagina non è quella ufficiale: ' + x + '.');
    for (const f of ['chat.js', 'chat.css']) {
      try {
        // la copia che il browser ha già scaricato per la pagina; se non c'è si rilegge (meno sicuro)
        let bytes;
        try { bytes = await leggi(base + f, 'force-cache'); } catch (e) { bytes = await leggi(base + f, 'reload'); }
        if (!fileConforme(manifest, f, bytes)) problemi.push(f + ' non corrisponde all\'impronta firmata.');
      } catch (e) { problemi.push('Non riesco a controllare ' + f + ': ' + e.message + '.'); }
    }
  }
  if (problemi.length) mostra(false, 'ATTENZIONE: non usare questa chat. Il programma che il sito ti ha servito non corrisponde a quello ufficiale di WinGabriel.', problemi);
  else mostra(true, 'Verificata: la pagina e il programma della chat sono quelli ufficiali di WinGabriel (impronte firmate il ' + manifest.creato.slice(0, 10) + ').');
}

nascondi();
const parti = () => { controlla().catch((e) => mostra(false, 'ATTENZIONE: non sono riuscito a controllare la chat: ' + e.message + '. Non usarla.')); };
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', parti, { once: true }); else parti();
