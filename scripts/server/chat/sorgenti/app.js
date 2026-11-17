// Interfaccia web della chat di WinGabriel. Si entra con lo stesso nome utente e la stessa password dei giochi. Nessun testo dei
// messaggi tocca mai il server in chiaro: parla con il relè (chat.php) solo con blocchi cifrati end to end, e tiene i dati nel
// browser, cifrati con una chiave che viene dalla password e non lascia mai questo dispositivo.
import { MotoreChat, statoVuoto } from './motore.js';
import * as archivio from './archivio.js';
import { risolviProva } from './prova.js';

// l'account sta sul sito principale: con il dominio dedicato della chat (chat.wingabriel.net) è un altro indirizzo
const API_ACCOUNT = location.hostname.indexOf('chat.') === 0 ? 'https://' + location.hostname.slice(5) + '/giochi/giochi.php' : '../giochi/giochi.php';
const URL_GIOCHI = location.hostname.indexOf('chat.') === 0 ? 'https://' + location.hostname.slice(5) + '/giochi/' : '../giochi/';
const $ = (id) => document.getElementById(id);
function el(tag, attrs, ...figli) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (k === 'testo') e.textContent = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else e.setAttribute(k, v);
  }
  for (const f of figli.flat()) if (f !== null && f !== undefined) e.append(f);
  return e;
}

let motore = null;
let token = '';
let nomeUtente = '';
let corrente = '';          // conversazione aperta
const titoloBase = document.title;
// secondi di attesa del relè (per i server di prova con un solo processo si può abbassare con ?attesa=1)
const ATTESA = Math.max(1, Math.min(20, Number(new URLSearchParams(location.search).get('attesa')) || 15));

// ------------------------------------------------------------------ server dei giochi (account) e relè
async function account(azione, corpo, metodo = 'POST') {
  const r = await fetch(API_ACCOUNT + '?a=' + azione, {
    method: metodo, headers: Object.assign({ 'Content-Type': 'application/json' }, token ? { 'X-Token': token } : {}),
    body: metodo === 'POST' ? JSON.stringify(corpo || {}) : undefined, cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer',
  });
  const dati = await r.json().catch(() => ({}));
  if (!r.ok) { const e = new Error(dati.errore || 'Errore del server.'); e.codice = r.status; if (dati.prova) e.prova = dati.prova; throw e; }
  return dati;
}
async function post(azione, corpo) {
  const r = await fetch('chat.php?a=' + azione, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(corpo), cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer' });
  return { codice: r.status, dati: await r.json().catch(() => ({})) };
}
// <flusso-chat>
// Il flusso di eventi della chat: una connessione sempre aperta su cui il relè scrive i blocchi appena arrivano (e dà segno di vita ogni
// 15 secondi). Le conferme dei blocchi passano subito con una richiesta normale; poi l'attesa torna sul flusso. Se la rete cade si riapre
// da solo; se il relè non ha i flussi (risponde con un errore) si ripiega sulle richieste lunghe di prima.
let flussoChat = null;
let flussiChatSospesiFino = 0;

function nuovoFlussoChat(chiave, cassette) {
  const f = {
    chiave, ctrl: null, blocchi: new Map(), errore: null, fermato: false, ultimoUso: Date.now(), risvegli: [],
    sveglia() { const r = this.risvegli; this.risvegli = []; r.forEach((x) => x()); },
    ferma() { this.fermato = true; try { if (this.ctrl) this.ctrl.abort(); } catch (e) { /* già chiuso */ } this.sveglia(); },
  };
  const trattaBlocco = (blocco) => {
    let nome = 'message'; const dati = [];
    for (const riga of blocco.split('\n')) {
      if (riga.startsWith(':')) continue;
      if (riga.startsWith('event:')) nome = riga.slice(6).trim();
      else if (riga.startsWith('data:')) dati.push(riga.slice(5).replace(/^ /, ''));
    }
    if (nome === 'errore') {
      let codice = 500;
      try { codice = JSON.parse(dati.join('\n')).stato || 500; } catch (e) { /* codice generico */ }
      const e = new Error('Il server ha chiuso il collegamento.'); e.codice = codice; throw e;
    }
    if (nome !== 'blocchi' || dati.length === 0) return;
    const d = JSON.parse(dati.join('\n'));
    let nuovi = false;
    for (const b of d.blocchi || []) { f.blocchi.set(b.id, b); nuovi = true; }
    if (nuovi) f.sveglia();
  };
  (async () => {
    let tentativo = 0; let inizioCaduta = 0;
    try {
      while (!f.fermato) {
        if (Date.now() - f.ultimoUso > 60000) break;   // nessuno lo usa più
        const ctrl = new AbortController(); f.ctrl = ctrl;
        let orologio = null;
        try {
          const risposta = await fetch('chat.php?a=flusso', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ cassette }),
            cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer', signal: ctrl.signal,
          });
          if (!risposta.ok || !(risposta.headers.get('content-type') || '').includes('text/event-stream')) {
            const e = new Error('Flusso non disponibile.'); e.codice = risposta.ok ? 404 : risposta.status; throw e;
          }
          tentativo = 0;
          let ultimoByte = Date.now();
          orologio = setInterval(() => { if (Date.now() - ultimoByte > 45000) ctrl.abort(); }, 5000);   // né dati né segno di vita: è morto
          const lettore = risposta.body.getReader(); const dec = new TextDecoder(); let buf = '';
          for (;;) {
            const { done, value } = await lettore.read();
            if (done) break;
            ultimoByte = Date.now();
            buf += dec.decode(value, { stream: true }).replace(/\r/g, '');
            let i;
            while ((i = buf.indexOf('\n\n')) >= 0) { const blocco = buf.slice(0, i); buf = buf.slice(i + 2); trattaBlocco(blocco); }
            if (Date.now() - f.ultimoUso > 60000) { ctrl.abort(); break; }
          }
        } catch (err) {
          if (f.fermato) return;
          if (err && err.codice) {
            f.errore = err;
            if (err.codice === 400 || err.codice === 404 || err.codice >= 500) flussiChatSospesiFino = Date.now() + 300000;
            return;
          }
          if (Date.now() - f.ultimoUso > 60000) return;
          tentativo++; if (tentativo === 1) inizioCaduta = Date.now();
          if (Date.now() - inizioCaduta > 120000) { const e = new Error('Non riesco a collegarmi al server della chat.'); e.codice = 0; f.errore = e; return; }
          await new Promise((r) => setTimeout(r, Math.min(15000, 1000 * (2 ** Math.min(tentativo - 1, 4))) + Math.random() * 500));
        } finally { if (orologio) clearInterval(orologio); }
      }
    } finally { f.fermato = true; f.sveglia(); }
  })();
  return f;
}

async function ascoltaConFlusso(cassette, ack, attesa) {
  if (attesa > 0 && cassette.length > 0 && Date.now() >= flussiChatSospesiFino && typeof ReadableStream === 'function' && typeof AbortController === 'function') {
    const chiave = cassette.map((c) => c.mb).sort().join(',');
    if (ack.length > 0) {
      if (flussoChat) for (const id of ack) flussoChat.blocchi.delete(id);
      attesa = 0;   // le conferme passano subito; l'attesa poi sta sul flusso
    } else {
      if (!flussoChat || flussoChat.chiave !== chiave || flussoChat.fermato || flussoChat.errore) {
        if (flussoChat) flussoChat.ferma();
        flussoChat = nuovoFlussoChat(chiave, cassette);
      }
      const f = flussoChat;
      f.ultimoUso = Date.now();
      const fine = Date.now() + attesa * 1000;
      for (;;) {
        if (f.blocchi.size > 0) { const r = [...f.blocchi.values()]; f.blocchi.clear(); return r; }
        if (f.errore) { if (f.errore.codice === 0) throw f.errore; break; }
        if (f.fermato) break;
        const resto = fine - Date.now();
        if (resto <= 0) return [];
        await Promise.race([new Promise((ok) => f.risvegli.push(ok)), new Promise((ok) => setTimeout(ok, resto))]);
        f.ultimoUso = Date.now();
      }
    }
  }
  const r = await post('ascolta', { cassette, ack, attesa });
  if (r.codice !== 200) throw new Error('errore ' + r.codice);
  return r.dati.blocchi;
}
// </flusso-chat>
const trasporto = {
  async invia(mb, tk, b) { const r = await post('invia', { mb, tk, b }); return r.codice === 200 ? 'ok' : 'no'; },
  async ascolta(cassette, ack, attesa) { return ascoltaConFlusso(cassette, ack, attesa); },
};

function annuncia(testo) {
  const z = $('annunci');
  z.textContent = '';
  setTimeout(() => { z.textContent = testo; }, 50);
}
function mostraToast(testo, cid) {
  const box = $('toast');
  if (!box || !testo) return;
  const t = el('button', { type: 'button', class: 'toast-voce', testo });
  t.addEventListener('click', () => { if (cid) apri(cid); t.remove(); });
  box.append(t);
  setTimeout(() => t.remove(), 6000);
}
// I suoni delle notifiche (un messaggio, una richiesta di contatto): si accendono e spengono qui, e la scelta vale anche per i giochi
// (stessa chiave nel browser). Sono i file di suoni/.
function suoniAttivi() {
  try { return localStorage.getItem('wg_suoni') !== '0'; } catch (e) { return true; }
}
const audioNotifiche = {};
function suona(nomeSuono) {
  if (!suoniAttivi()) return;
  try {
    const a = audioNotifiche[nomeSuono] || (audioNotifiche[nomeSuono] = new Audio('suoni/' + nomeSuono + '.wav'));
    a.currentTime = 0;
    const p = a.play();
    if (p && p.catch) p.catch(() => { /* il browser non lascia suonare finché non si è toccato la pagina */ });
  } catch (e) { /* senza audio si va avanti in silenzio */ }
}
function aggiornaPulsanteSuoni() {
  const b = $('suoni-chat');
  if (!b) return;
  const attivi = suoniAttivi();
  b.textContent = 'Suoni: ' + (attivi ? 'attivi' : 'spenti');
  b.setAttribute('aria-pressed', attivi ? 'true' : 'false');
}
function notifica(titolo, testo, cid) {
  const corpo = testo ? titolo + ': ' + testo : titolo;
  if (document.hidden || (cid && cid !== corrente)) mostraToast(corpo, cid);
  if (typeof Notification === 'undefined' || Notification.permission !== 'granted') return;
  if (!document.hidden && (!cid || cid === corrente)) return;
  try {
    const n = new Notification(titolo, { body: testo || '', tag: cid || 'wg-chat' });
    n.onclick = () => { window.focus(); if (cid) apri(cid); n.close(); };
  } catch (e) { /* il browser non mostra la notifica */ }
}
function aggiornaPulsanteNotifiche() {
  const b = $('attiva-notifiche');
  if (!b) return;
  b.hidden = typeof Notification === 'undefined' || Notification.permission === 'granted';
}
function aggiornaVista() {
  const aperta = !!corrente && !$('pannello-conversazione').hidden;
  document.body.classList.toggle('conversazione-aperta', aperta);
  const torna = $('torna-contatti');
  if (torna) torna.hidden = !aperta || !window.matchMedia('(max-width: 860px)').matches;
}
function mostra(id) { for (const s of ['schermata-sblocco', 'schermata-chat']) $(s).hidden = s !== id; aggiornaVista(); }

// ------------------------------------------------------------------ accesso (uno solo per giochi e chat)
let modoCrea = false;
function impostaModo(crea) {
  modoCrea = crea;
  $('scheda-entra').setAttribute('aria-pressed', crea ? 'false' : 'true');
  $('scheda-crea').setAttribute('aria-pressed', crea ? 'true' : 'false');
  $('riga-ripeti').hidden = !crea;
  $('campo-ripeti').required = crea;
  $('campo-password').autocomplete = crea ? 'new-password' : 'current-password';
  $('invio-accesso').textContent = crea ? 'Registrati' : 'Accedi';
  $('errore-sblocco').textContent = '';
}
$('scheda-entra').addEventListener('click', () => impostaModo(false));
$('scheda-crea').addEventListener('click', () => impostaModo(true));

async function iniziale() {
  $('vai-giochi').setAttribute('href', URL_GIOCHI);
  mostra('schermata-sblocco');
  impostaModo(false);
  // La password salvata è solo cifrata: si decifra e si entra con quell'utente. Senza password non si entra.
  const salvato = await archivio.ricordo();
  if (salvato && salvato.utente && salvato.password) {
    try {
      await entraEApri(salvato.utente, salvato.password, false);
      return;
    } catch (e) {
      if (e.codice === 401) await archivio.dimentica();
      else if (e.archivio) {
        $('campo-nome').value = salvato.utente;
        $('errore-sblocco').textContent = e.message;
        $('campo-password').focus();
        return;
      }
    }
  }
  const noto = (salvato && salvato.utente) || sessionStorage.getItem('wg_nome') || '';
  $('campo-nome').value = noto;
  (noto ? $('campo-password') : $('campo-nome')).focus();
}

async function entraEApri(nome, password, crea, chiediSalvataggio) {
  const { auth, master } = await archivio.deriva(nome, password);
  let r;
  try {
    r = await account(crea ? 'registra' : 'entra', { nome, auth });
  } catch (err) {
    if (!crea && err.codice === 401) r = await account('entra', { nome, password });
    else if (crea && err.codice === 428 && err.prova && err.prova.sfida && err.prova.bit >= 1 && err.prova.bit <= 26) {
      // il server chiede una piccola prova di calcolo (succede solo quando in un'ora si creano moltissimi account)
      $('errore-sblocco').textContent = 'Un momento: preparo una piccola verifica per creare il nome utente.';
      const nonce = await risolviProva(err.prova.sfida, nome, err.prova.bit);
      $('errore-sblocco').textContent = '';
      r = await account('registra', { nome, auth, prova: { sfida: err.prova.sfida, nonce } });
    } else throw err;
  }
  token = r.token; nomeUtente = r.nome;
  sessionStorage.setItem('wg_token', token); sessionStorage.setItem('wg_nome', nomeUtente);
  const chiave = await archivio.chiaveDaMaster(master);
  let a;
  if (await archivio.esiste(nomeUtente)) {
    a = await archivio.apri(nomeUtente, chiave);
    if (!a) {
      const e = new Error('La chat non si apre con questa password. Puoi cancellarla e ricominciare.');
      e.archivio = true;
      throw e;
    }
  } else {
    a = await archivio.crea(nomeUtente, chiave, statoVuoto(nomeUtente));
  }
  if (chiediSalvataggio) {
    if (confirm('Vuoi ricordare la password su questo telefono o computer?')) {
      await archivio.ricorda(nomeUtente, password);
    } else {
      await archivio.dimentica();
    }
  }
  $('campo-password').value = ''; $('campo-ripeti').value = '';
  await avviaChat(a);
}

$('modulo-accesso').addEventListener('submit', async (e) => {
  e.preventDefault();
  const errore = $('errore-sblocco');
  errore.textContent = '';
  const nome = $('campo-nome').value.trim(), password = $('campo-password').value;
  if (password.length < 8) { errore.textContent = 'La password è troppo corta: servono almeno 8 caratteri.'; return; }
  if (modoCrea && password !== $('campo-ripeti').value) { errore.textContent = 'Le due password sono diverse.'; return; }
  $('invio-accesso').disabled = true;
  try {
    await entraEApri(nome, password, modoCrea, true);
  } catch (err) {
    errore.textContent = err.message;
  } finally { $('invio-accesso').disabled = false; }
});

$('cancella-tutto').addEventListener('click', async () => {
  const nome = $('campo-nome').value.trim();
  if (!nome) { $('errore-sblocco').textContent = 'Scrivi il nome utente.'; return; }
  if (!confirm('Cancellare la chat di ' + nome + ' su questo telefono o computer?')) return;
  await archivio.cancellaTutto(nome);
  $('errore-sblocco').textContent = 'Chat cancellata.';
});

// ------------------------------------------------------------------ chat
async function avviaChat(a) {
  motore = new MotoreChat({
    stato: a.stato, messaggi: a.messaggi, trasporto, nome: nomeUtente,
    salvaStato: (s) => a.salvaStato(s),
    salvaMessaggi: (cid, elenco) => a.salvaMessaggi(cid, elenco),
  });
  motore.su('messaggio', ({ cid, m }) => {
    const k = motore.contatto(cid);
    if (m.dir === 'in') {
      const chi = motore.nomeVisibile(k);
      if (cid === corrente && !document.hidden) { motore.segnaLetti(cid); annuncia(chi + ': ' + m.x); }
      else { annuncia('Nuovo messaggio da ' + chi); suona('notifica'); notifica(chi, m.x, cid); }
    }
    disegnaElenco();
    if (cid === corrente) disegnaConversazione();
    aggiornaTitolo();
  });
  motore.su('richiesta', (k) => { annuncia(k.nome + ' vuole scriverti.'); suona('richiesta'); notifica(k.nome, 'Vuole scriverti', k.id); disegnaElenco(); aggiornaTitolo(); });
  motore.su('contatto', () => { disegnaElenco(); disegnaBloccati(); if (corrente) disegnaConversazione(); });
  motore.su('collegamento', (v) => { $('stato-collegamento').textContent = v ? 'Collegato' : 'Non collegato: riprovo'; });
  mostra('schermata-chat');
  aggiornaPulsanteNotifiche();
  $('mio-nome').textContent = nomeUtente;
  $('trovabile').checked = motore.s.trovabile !== false;
  disegnaElenco();
  disegnaBloccati();
  await aggiornaPubblicazione();
  motore.avvia(ATTESA);
  const dest = decodeURIComponent((location.hash.match(/con=([^&]+)/) || [])[1] || '');
  if (dest) { history.replaceState(null, '', location.pathname); await contattaPerNome(dest); }
  $('nuovo-nome-casella').focus();
}

function aggiornaTitolo() {
  const n = motore.s.contatti.reduce((t, k) => t + (k.nonLetti || 0) + (k.stato === 'richiesta' ? 1 : 0), 0);
  document.title = (n ? '(' + n + ') ' : '') + titoloBase;
}

/** Tiene il proprio codice trovabile per nome utente (o lo toglie), secondo la scelta. */
async function aggiornaPubblicazione() {
  try {
    if (motore.s.trovabile === false) { await account('chat_ritira'); $('mio-codice-riga').hidden = true; return; }
    if (!motore.s.inviti.length) await motore.nuovoInvito();
    const inv = motore.s.inviti[motore.s.inviti.length - 1];
    $('mio-codice').value = inv.codice;
    $('mio-codice-riga').hidden = false;
    await account('chat_pubblica', { codice: inv.codice });
  } catch (e) { annuncia('Non riesco a farmi trovare: ' + e.message); }
}

$('trovabile').addEventListener('change', async () => { motore.s.trovabile = $('trovabile').checked; await motore.salva(); await aggiornaPubblicazione(); annuncia($('trovabile').checked ? 'Chi cerca il tuo nome può scriverti.' : 'Nessuno ti trova più per nome.'); });
$('crea-codice').addEventListener('click', async () => { await motore.nuovoInvito(); await aggiornaPubblicazione(); annuncia('Codice nuovo. Il precedente non vale più.'); });
$('copia-codice').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText($('mio-codice').value); annuncia('Codice copiato.'); } catch (e) { $('mio-codice').select(); annuncia('Seleziona il codice e copialo.'); }
});
$('suoni-chat').addEventListener('click', () => {
  try { localStorage.setItem('wg_suoni', suoniAttivi() ? '0' : '1'); } catch (e) { /* senza memoria del browser la scelta vale solo adesso */ }
  aggiornaPulsanteSuoni();
  annuncia(suoniAttivi() ? 'Suoni attivi.' : 'Suoni spenti.');
});
aggiornaPulsanteSuoni();

$('attiva-notifiche').addEventListener('click', async () => {
  if (typeof Notification === 'undefined') return;
  const permesso = await Notification.requestPermission();
  aggiornaPulsanteNotifiche();
  annuncia(permesso === 'granted' ? 'Notifiche attive.' : 'Notifiche non attive.');
});
$('torna-contatti').addEventListener('click', () => {
  corrente = '';
  $('pannello-conversazione').hidden = true;
  $('pannello-vuoto').hidden = false;
  aggiornaVista();
  disegnaElenco();
  $('nuovo-nome-casella').focus();
});
window.addEventListener('resize', aggiornaVista);

function descrizioneContatto(k) {
  const n = motore.nomeVisibile(k) + (k.bloccato ? ' (bloccato)' : '');
  switch (k.stato) {
    case 'richiesta': return n + ', vuole scriverti';
    case 'invitato': return n + ', in attesa';
    case 'accettato': return n + ', quasi pronto';
    default: return n + (k.nonLetti ? ', ' + k.nonLetti + (k.nonLetti === 1 ? ' messaggio nuovo' : ' messaggi nuovi') : '');
  }
}

function disegnaElenco() {
  const ul = $('elenco-contatti');
  ul.replaceChildren();
  $('nessun-contatto').hidden = motore.s.contatti.length > 0;
  const peso = (k) => (k.stato === 'richiesta' ? 0 : k.nonLetti ? 1 : 2);
  const ordinati = [...motore.s.contatti].sort((a, b) => peso(a) - peso(b) || motore.nomeVisibile(a).localeCompare(motore.nomeVisibile(b)));
  for (const k of ordinati) {
    const b = el('button', { type: 'button', class: 'contatto' + (k.id === corrente ? ' aperto' : '') + (k.nonLetti || k.stato === 'richiesta' ? ' nuovo' : ''), testo: descrizioneContatto(k), 'aria-current': k.id === corrente ? 'true' : 'false', onclick: () => apri(k.id) });
    ul.append(el('li', {}, b));
  }
}

function disegnaBloccati() {
  const ul = $('elenco-bloccati');
  ul.replaceChildren();
  const b = motore.elencoBloccati();
  $('nessun-bloccato').hidden = b.length > 0;
  for (const x of b) {
    ul.append(el('li', { class: 'riga' }, el('span', { testo: x.nome + (x.utente ? '' : ' (senza nome utente)') }), el('button', { type: 'button', class: 'secondario', testo: 'Sblocca', 'aria-label': 'Sblocca ' + x.nome, onclick: () => sblocca(x) })));
  }
}

async function sblocca(x) {
  const utente = await motore.sblocca(x.ik);
  if (utente) { try { await account('sblocca', { nome: utente }); } catch (e) { /* si sblocca almeno qui */ } }
  annuncia(x.nome + ' è stato sbloccato.');
}

async function bloccaUtenteNome(utente) {
  if (!utente) return;
  try { await account('blocca', { nome: utente }); } catch (e) { annuncia('Bloccato qui. Nei giochi non ci sono riuscito.'); }
}

// ------------------------------------------------------------------ aggiungere contatti
async function contattaPerNome(nome) {
  $('errore-aggiungi').textContent = '';
  try {
    const gia = motore.s.contatti.find((k) => (k.utente || '').toLowerCase() === nome.toLowerCase() && k.stato !== 'rifiutato');
    if (gia) { apri(gia.id); return; }
    const r = await account('cerca', { nome });
    const k = await motore.aggiungiContatto(r.codice, r.nome);
    annuncia('Richiesta inviata a ' + r.nome + '.');
    apri(k.id);
  } catch (err) {
    $('errore-aggiungi').textContent = err.codice === 404 ? 'Non trovo questa persona. Se hai un codice, usalo qui sotto.' : err.message;
  }
}
$('modulo-aggiungi-nome').addEventListener('submit', async (e) => {
  e.preventDefault();
  const n = $('nuovo-nome-casella').value.trim();
  if (n) { await contattaPerNome(n); $('nuovo-nome-casella').value = ''; }
});
$('modulo-aggiungi').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('errore-aggiungi').textContent = '';
  try {
    await motore.aggiungiContatto($('nuovo-codice-casella').value);
    $('nuovo-codice-casella').value = '';
    annuncia('Richiesta inviata.');
  } catch (err) { $('errore-aggiungi').textContent = err.message; }
});

$('esci-chat').addEventListener('click', () => { motore.ferma(); sessionStorage.removeItem('wg_token'); location.reload(); });
// esce e cancella la password cifrata: la prossima volta va scritta di nuovo
$('esci-dimentica').addEventListener('click', async () => { motore.ferma(); sessionStorage.removeItem('wg_token'); await archivio.dimentica(); location.reload(); });

// ------------------------------------------------------------------ conversazione
function apri(cid) {
  const k = motore.contatto(cid);
  if (!k) return;
  corrente = cid;
  motore.segnaLetti(cid);
  $('pannello-vuoto').hidden = true;
  $('pannello-conversazione').hidden = false;
  disegnaConversazione();
  disegnaElenco();
  aggiornaTitolo();
  aggiornaVista();
  if (k.stato === 'attivo' && !k.bloccato) $('testo-messaggio').focus(); else $('titolo-conversazione').focus();
}

function etichettaStato(m, nome) {
  if (m.dir === 'in') return '';
  switch (m.st) {
    case 'consegnato': return 'consegnato';
    case 'inviato': return 'inviato';
    case 'scaduto': return 'non consegnato';
    default: return 'in attesa';
  }
}

function disegnaConversazione() {
  const k = motore.contatto(corrente);
  if (!k) { corrente = ''; $('pannello-conversazione').hidden = true; $('pannello-vuoto').hidden = false; return; }
  const nome = motore.nomeVisibile(k);
  $('titolo-conversazione').textContent = nome + (k.bloccato ? ' (bloccato)' : '');
  $('stato-verifica').textContent = k.verificato ? 'Controllato.' : 'Non ancora controllato.';
  $('numero-sicurezza').textContent = motore.numeroDiSicurezza(corrente);
  $('segna-verificato').hidden = k.verificato;
  $('riquadro-richiesta').hidden = k.stato !== 'richiesta';
  $('modulo-scrivi').hidden = k.stato !== 'attivo' || !!k.bloccato;
  $('attesa-contatto').hidden = !(k.stato === 'invitato' || k.stato === 'accettato') || !!k.bloccato;
  $('attesa-contatto').textContent = k.stato === 'invitato' ? nome + ' non ha ancora accettato.' : 'In attesa di ' + nome + '.';
  $('testo-richiesta').textContent = nome + ' vuole scriverti.';
  $('blocca-contatto').hidden = !!k.bloccato;
  $('sblocca-contatto').hidden = !k.bloccato;
  const log = $('messaggi');
  const prima = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
  log.replaceChildren();
  for (const m of motore.elencoMessaggi(corrente)) {
    const ora = new Date(m.ts).toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' });
    const stato = etichettaStato(m, nome);
    log.append(el('p', { class: 'msg ' + m.dir },
      el('span', { class: 'chi', testo: '[' + ora + '] ' + (m.dir === 'in' ? nome : 'Tu') + ': ' }),
      el('span', { class: 'testo', testo: m.x }),
      stato ? el('span', { class: 'stato', testo: ' (' + stato + ')' }) : null));
  }
  if (prima) log.scrollTop = log.scrollHeight;
  aggiornaVista();
}

$('modulo-scrivi').addEventListener('submit', async (e) => {
  e.preventDefault();
  const campo = $('testo-messaggio');
  if (!campo.value.trim()) return;
  try {
    await motore.scrivi(corrente, campo.value);
    campo.value = '';
    $('errore-scrivi').textContent = '';
  } catch (err) { $('errore-scrivi').textContent = err.message; }
});
$('testo-messaggio').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); $('modulo-scrivi').requestSubmit(); } });
$('accetta-richiesta').addEventListener('click', async () => { try { await motore.accetta(corrente); annuncia('Hai accettato.'); } catch (e) { annuncia(e.message); } });
$('rifiuta-richiesta').addEventListener('click', async () => { await motore.rifiuta(corrente); corrente = ''; disegnaElenco(); disegnaConversazione(); });
$('segna-verificato').addEventListener('click', async () => { await motore.segnaVerificato(corrente); annuncia('Segnato come verificato.'); });
$('blocca-contatto').addEventListener('click', async () => {
  const k = motore.contatto(corrente);
  if (!confirm('Bloccare ' + motore.nomeVisibile(k) + '? Non vi scriverete più.')) return;
  const utente = await motore.blocca(corrente);
  await bloccaUtenteNome(utente);
  annuncia(motore.nomeVisibile(k) + ' è stato bloccato.');
});
$('sblocca-contatto').addEventListener('click', async () => { const k = motore.contatto(corrente); await sblocca({ ik: k.ikRemota, nome: motore.nomeVisibile(k) }); });
$('rinomina').addEventListener('click', async () => {
  const k = motore.contatto(corrente);
  const n = prompt('Nome da mostrare', motore.nomeVisibile(k));
  if (n !== null) await motore.rinomina(corrente, n);
});
$('elimina').addEventListener('click', async () => {
  const k = motore.contatto(corrente);
  if (!confirm('Eliminare la chat con ' + motore.nomeVisibile(k) + '?')) return;
  await motore.elimina(corrente);
  corrente = ''; disegnaElenco(); disegnaConversazione();
});
$('rileggi').addEventListener('click', () => {
  const k = motore.contatto(corrente);
  const ultimi = motore.elencoMessaggi(corrente).slice(-5);
  annuncia(ultimi.length ? ultimi.map((m) => (m.dir === 'in' ? motore.nomeVisibile(k) : 'Tu') + ': ' + m.x).join('. ') : 'Non ci sono ancora messaggi.');
});
document.addEventListener('visibilitychange', () => { if (!document.hidden && corrente && motore) { motore.segnaLetti(corrente); aggiornaTitolo(); } });

iniziale();
