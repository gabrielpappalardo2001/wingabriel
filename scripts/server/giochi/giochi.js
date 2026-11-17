'use strict';
// Versione web dei giochi di WinGabriel (burraco, monopoli, briscola e scopa). Parla con giochi.php.
// Il codice della sessione resta in sessionStorage. La password, se si sceglie di salvarla, resta solo cifrata
// (AES-256-GCM, chiave non esportabile di questo browser): alla prossima apertura si entra con quell'utente. In chiaro non si scrive.
// Un link ?p=ID apre quella partita dopo l'accesso: chi vede e chi non vede giocano insieme, dal web o da WinGabriel.

const API = 'giochi.php';
const SIMBOLI = ['♥', '♦', '♣', '♠'];
const COLORI_GRUPPO = { marrone: '#8b4513', azzurro: '#87ceeb', rosa: '#d93a96', arancione: '#f7941d', rosso: '#e31b23', giallo: '#fed100', verde: '#1fb25a', blu: '#0072bb' };
const COLORI_GIOCATORI = ['#d02020', '#2070d0', '#30a020', '#e0c020']; // gli stessi colori delle pedine (monopoli/pedina-N.png) e di WinGabriel

let token = sessionStorage.getItem('wg_token') || '';
let nome = sessionStorage.getItem('wg_nome') || '';
let haPassword = sessionStorage.getItem('wg_ha_password') === '1';
let partitaAttiva = null;   // id della partita che si sta seguendo
let aggiornaSala = null;    // timer della sala
let cartaScelta = null;     // carta del burraco su cui si filtrano le mosse
let ultimoStato = null;
let urlChat = '../chat/';   // cambia se la chat ha un dominio dedicato (lo dice il server)
let eraMioTurno = false;
let attesaCorrente = 20;   // secondi di attesa del server: poca se tocca a te (la partita non cambia da sola)
const ATTESA_FORZATA = new URLSearchParams(location.search).get('attesa'); // per i server con un solo processo, nelle prove

function idDaLinkOCodice(testo) {
  const t = String(testo || '').trim();
  if (!t) return '';
  const marca = t.search(/p=/i);
  if (marca >= 0) {
    let resto = t.slice(marca + 2);
    const taglio = resto.search(/[&?#\s\/]/);
    if (taglio >= 0) resto = resto.slice(0, taglio);
    return resto.trim().toUpperCase();
  }
  return t.toUpperCase();
}

function linkPartita(id) {
  const u = new URL(location.href);
  u.search = '';
  u.hash = '';
  u.searchParams.set('p', id);
  return u.toString();
}

function impostaUrlPartita(id) {
  const u = new URL(location.href);
  if (id) u.searchParams.set('p', id);
  else u.searchParams.delete('p');
  history.replaceState({}, '', u);
}

const $ = (id) => document.getElementById(id);

// l'errore si scrive nella schermata in cui si è (sala o pagina di un gioco)
function erroreSala(testo) {
  $('errore-sala').textContent = testo;
  $('errore-gioco').textContent = testo;
}

function el(tag, attrs, ...figli) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (k === 'class') e.className = v;
    else if (k === 'testo') e.textContent = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (v === true) e.setAttribute(k, '');
    else if (v !== false && v != null) e.setAttribute(k, String(v));
  }
  for (const f of figli.flat()) {
    if (f == null || f === false) continue;
    e.append(f.nodeType ? f : document.createTextNode(String(f)));
  }
  return e;
}

// La password non parte mai: al server va un derivato (PBKDF2-SHA256, 600000 giri, sale "WGAccount-v1|nome in minuscolo", 64 byte, i primi 32 in
// esadecimale); gli altri 32 byte restano nel dispositivo e proteggono la chat. Stessa regola del programma di WinGabriel.
async function derivaAuth(utente, password) {
  const te = new TextEncoder();
  const base = await crypto.subtle.importKey('raw', te.encode(password), 'PBKDF2', false, ['deriveBits']);
  const bit = new Uint8Array(await crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt: te.encode('WGAccount-v1|' + utente.toLowerCase()), iterations: 600000 }, base, 512));
  return Array.from(bit.slice(0, 32), (x) => x.toString(16).padStart(2, '0')).join('');
}

// ------------------------------------------------------------------ prova di calcolo per creare un account
// Quando in un'ora si creano moltissimi account il server risponde 428 con una sfida: si cerca un numero (esadecimale minuscolo) tale che
// SHA-256 di "sfida|nome in minuscolo|numero" inizi con "bit" bit a zero. Servono pochi secondi per un account, troppi per migliaia.
// SHA-256 scritto qui: quello del browser è asincrono e per milioni di tentativi sarebbe troppo lento.
const K256 = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);
const W256 = new Uint32Array(64);
const M256 = new Uint8Array(256);   // il messaggio da calcolare, con il riempimento (basta fino a 246 byte: la prova ne usa circa 160)
// SHA-256 di bytes[0..lunghezza): scrive l'impronta in `fuori` (8 numeri a 32 bit)
function sha256Blocchi(bytes, lunghezza, fuori) {
  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  const totale = ((lunghezza + 9 + 63) >> 6) << 6;
  if (totale > M256.length) throw new Error('Messaggio troppo lungo per la prova.');
  const m = M256;
  m.fill(0, 0, totale);
  m.set(bytes.subarray(0, lunghezza));
  m[lunghezza] = 0x80;
  const bit = lunghezza * 8;
  m[totale - 4] = (bit >>> 24) & 255; m[totale - 3] = (bit >>> 16) & 255; m[totale - 2] = (bit >>> 8) & 255; m[totale - 1] = bit & 255;
  for (let o = 0; o < totale; o += 64) {
    for (let i = 0; i < 16; i++) W256[i] = (m[o + 4 * i] << 24) | (m[o + 4 * i + 1] << 16) | (m[o + 4 * i + 2] << 8) | m[o + 4 * i + 3];
    for (let i = 16; i < 64; i++) {
      const a = W256[i - 15], b = W256[i - 2];
      const s0 = ((a >>> 7) | (a << 25)) ^ ((a >>> 18) | (a << 14)) ^ (a >>> 3);
      const s1 = ((b >>> 17) | (b << 15)) ^ ((b >>> 19) | (b << 13)) ^ (b >>> 10);
      W256[i] = (W256[i - 16] + s0 + W256[i - 7] + s1) | 0;
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let i = 0; i < 64; i++) {
      const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
      const t1 = (h + S1 + ((e & f) ^ (~e & g)) + K256[i] + W256[i]) | 0;
      const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
      const t2 = (S0 + ((a & b) ^ (a & c) ^ (b & c))) | 0;
      h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
    }
    h0 = (h0 + a) | 0; h1 = (h1 + b) | 0; h2 = (h2 + c) | 0; h3 = (h3 + d) | 0; h4 = (h4 + e) | 0; h5 = (h5 + f) | 0; h6 = (h6 + g) | 0; h7 = (h7 + h) | 0;
  }
  fuori[0] = h0; fuori[1] = h1; fuori[2] = h2; fuori[3] = h3; fuori[4] = h4; fuori[5] = h5; fuori[6] = h6; fuori[7] = h7;
}
function zeriInizialiNumero(n) { return n === 0 ? 32 : Math.clz32(n); }
// Cerca il numero della prova. Lavora a pezzi di circa 80 ms per non bloccare la pagina e dice come procede con `avanza`.
async function risolviProva(sfida, utente, bit, avanza) {
  const te = new TextEncoder();
  const prefisso = te.encode(sfida + '|' + utente.toLowerCase() + '|');
  const buffer = new Uint8Array(prefisso.length + 16);
  buffer.set(prefisso);
  const fuori = new Int32Array(8);
  const attesi = Math.pow(2, bit);
  let contatore = 0;
  for (;;) {
    const fine = performance.now() + 80;
    while (performance.now() < fine) {
      for (let k = 0; k < 500; k++) {
        const esa = contatore.toString(16);
        let n = prefisso.length;
        for (let i = 0; i < esa.length; i++) buffer[n++] = esa.charCodeAt(i);
        sha256Blocchi(buffer, n, fuori);
        const z = fuori[0] === 0 ? 32 + zeriInizialiNumero(fuori[1]) : zeriInizialiNumero(fuori[0]);
        if (z >= bit) return esa;
        contatore++;
      }
    }
    if (avanza) avanza(Math.min(99, Math.round((contatore / attesi) * 100)));
    await new Promise((ok) => setTimeout(ok, 0));
  }
}
// Crea l'account; se il server chiede la prova di calcolo la fa (dicendolo a voce nell'avviso della pagina) e rifà la richiesta.
async function registraConProva(utente, auth) {
  try {
    return await api('POST', 'registra', { nome: utente, auth });
  } catch (err) {
    if (err.codice !== 428 || !err.prova || !err.prova.sfida || !(err.prova.bit >= 1 && err.prova.bit <= 26)) throw err;
    $('errore-accesso').textContent = 'Un momento: preparo una piccola verifica per creare il nome utente.';
    const nonce = await risolviProva(err.prova.sfida, utente, err.prova.bit);
    $('errore-accesso').textContent = '';
    return await api('POST', 'registra', { nome: utente, auth, prova: { sfida: err.prova.sfida, nonce } });
  }
}

// La password dell'utente, solo cifrata. La chiave AES resta in IndexedDB e non si può esportare.
const DB_ACCESSO = 'wg-giochi-accesso';
function dbAccesso() {
  return new Promise((ok, no) => {
    const r = indexedDB.open(DB_ACCESSO, 1);
    r.onupgradeneeded = () => { if (!r.result.objectStoreNames.contains('d')) r.result.createObjectStore('d'); };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
function richiestaIdb(r) { return new Promise((ok, no) => { r.onsuccess = () => ok(r.result); r.onerror = () => no(r.error); }); }
async function chiaveAccesso(d) {
  let k = await richiestaIdb(d.transaction('d', 'readonly').objectStore('d').get('chiave'));
  if (k) return k;
  k = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  const t = d.transaction('d', 'readwrite');
  t.objectStore('d').put(k, 'chiave');
  await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); });
  return k;
}
async function salvaPassword(utente, password) {
  const d = await dbAccesso();
  try {
    const chiave = await chiaveAccesso(d);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const c = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, chiave, new TextEncoder().encode(password)));
    const t = d.transaction('d', 'readwrite');
    t.objectStore('d').put({ utente, iv, c }, 'accesso');
    await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); });
  } finally { d.close(); }
}
async function leggiPassword() {
  try {
    const d = await dbAccesso();
    try {
      const salvato = await richiestaIdb(d.transaction('d', 'readonly').objectStore('d').get('accesso'));
      if (!salvato || !salvato.utente || !salvato.c) return null;
      const chiave = await richiestaIdb(d.transaction('d', 'readonly').objectStore('d').get('chiave'));
      if (!chiave) return null;
      const chiaro = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: salvato.iv }, chiave, salvato.c);
      return { utente: salvato.utente, password: new TextDecoder().decode(chiaro) };
    } finally { d.close(); }
  } catch (e) { return null; }
}
async function dimenticaPassword() {
  try {
    const d = await dbAccesso();
    try {
      const t = d.transaction('d', 'readwrite');
      t.objectStore('d').delete('accesso');
      await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); });
    } finally { d.close(); }
  } catch (e) { /* niente di salvato */ }
}

async function api(metodo, azione, corpo, query) {
  const url = API + '?a=' + azione + (query ? '&' + new URLSearchParams(query).toString() : '');
  let risposta;
  try {
    risposta = await fetch(url, {
      method: metodo,
      headers: Object.assign({ 'Content-Type': 'application/json' }, token ? { 'X-Token': token } : {}),
      body: corpo ? JSON.stringify(corpo) : undefined,
    });
  } catch (e) {
    // niente rete o server spento: il messaggio del browser (in inglese) non serve a chi gioca
    const errore = new Error('Non riesco a collegarmi al server dei giochi. Controlla la connessione a internet.');
    errore.codice = 0;
    throw errore;
  }
  let dati = {};
  try { dati = await risposta.json(); } catch (e) { /* risposta vuota */ }
  if (!risposta.ok) {
    const errore = new Error(dati.errore || 'Errore del server.');
    errore.codice = risposta.status;
    if (dati.prova) errore.prova = dati.prova;   // la prova di calcolo che il server chiede per creare un account (428)
    throw errore;
  }
  return dati;
}

let schermataCorrente = '';
const TITOLI_PAGINA = { 'schermata-accesso': 'Accedi', 'schermata-sala': 'Scegli un gioco', 'schermata-gioco': 'Gioco', 'schermata-partita': 'Partita' };
function mostra(nomeSchermata) {
  for (const id of ['schermata-accesso', 'schermata-sala', 'schermata-gioco', 'schermata-partita']) $(id).hidden = id !== nomeSchermata;
  // il titolo della scheda dice dove sei; passando alla sala il lettore di schermo riparte dal titolo
  document.title = TITOLI_PAGINA[nomeSchermata] + ' – Giochi';
  if (schermataCorrente && schermataCorrente !== nomeSchermata && nomeSchermata === 'schermata-sala') $('titolo-sala').focus({ preventScroll: true });
  if (schermataCorrente && schermataCorrente !== nomeSchermata && nomeSchermata === 'schermata-gioco') $('titolo-gioco').focus({ preventScroll: true });
  schermataCorrente = nomeSchermata;
  $('utente').hidden = !token;
  $('nome-utente').textContent = nome;
  if (aggiornaSala) { clearInterval(aggiornaSala); aggiornaSala = null; }
}

// ------------------------------------------------------------------ accesso
function ricordaAccesso(r) {
  token = r.token; nome = r.nome;
  haPassword = !!r.ha_password;
  sessionStorage.setItem('wg_token', token); sessionStorage.setItem('wg_nome', nome);
  sessionStorage.setItem('wg_ha_password', haPassword ? '1' : '0');
}

function schermataAccesso() {
  partitaAttiva = null;
  token = ''; nome = ''; haPassword = false;
  sessionStorage.removeItem('wg_token'); sessionStorage.removeItem('wg_nome'); sessionStorage.removeItem('wg_ha_password');
  mostra('schermata-accesso');
  impostaModo(false);
  $('campo-nome').focus();
}

// Due modi di entrare: con un account che c'è già, oppure creandone uno nuovo.
let modoCrea = false;
function impostaModo(crea) {
  modoCrea = crea;
  $('scheda-entra').setAttribute('aria-pressed', crea ? 'false' : 'true');
  $('scheda-crea').setAttribute('aria-pressed', crea ? 'true' : 'false');
  $('riga-ripeti').hidden = !crea;
  $('campo-ripeti').required = crea;
  $('campo-password').autocomplete = crea ? 'new-password' : 'current-password';
  $('invio-accesso').textContent = crea ? 'Crea l\'account ed entra' : 'Entra';
  $('errore-accesso').textContent = '';
}
$('scheda-entra').addEventListener('click', () => impostaModo(false));
$('scheda-crea').addEventListener('click', () => impostaModo(true));

$('modulo-accesso').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('errore-accesso').textContent = '';
  const password = $('campo-password').value;
  if (password.length < 8) {
    $('errore-accesso').textContent = 'La password è troppo corta: servono almeno 8 caratteri.';
    return;
  }
  if (modoCrea && password !== $('campo-ripeti').value) {
    $('errore-accesso').textContent = 'Le due password sono diverse.';
    return;
  }
  try {
    const utente = $('campo-nome').value.trim();
    const r = await entraCon(utente, password, modoCrea);
    ricordaAccesso(r);
    if ($('ricorda').checked) {
      await salvaPassword(r.nome, password);
    } else {
      await dimenticaPassword();
    }
    $('campo-password').value = ''; $('campo-ripeti').value = '';
    dopoAccesso();
  } catch (err) {
    $('errore-accesso').textContent = err.message;
  }
});

// Senza password non si entra. Al server va il derivato; la password in chiaro parte solo una volta, per i nomi utente vecchi.
async function entraCon(utente, password, crea) {
  const auth = await derivaAuth(utente, password);
  try {
    if (crea) return await registraConProva(utente, auth);
    return await api('POST', 'entra', { nome: utente, auth });
  } catch (err) {
    if (!crea && err.codice === 401) return await api('POST', 'entra', { nome: utente, password });
    throw err;
  }
}

$('modulo-elimina').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('esito-elimina').textContent = '';
  if (!confirm('Vuoi eliminare il tuo account (' + nome + ')? Non si può annullare.')) return;
  try {
    await api('POST', 'elimina_account', { auth: await derivaAuth(nome, $('password-elimina').value) });
    $('password-elimina').value = '';
    await dimenticaPassword();
    schermataAccesso();
    $('errore-accesso').textContent = 'Account eliminato.';
  } catch (err) {
    $('esito-elimina').textContent = err.message;
  }
});

$('esci').addEventListener('click', async () => {
  if (partitaAttiva) { try { await api('POST', 'lascia', { id: partitaAttiva }); } catch (e) { /* già finita */ } }
  await dimenticaPassword();
  schermataAccesso();
});

// ------------------------------------------------------------------ persone bloccate
async function caricaBloccati() {
  try {
    const r = await api('GET', 'bloccati');
    const ul = $('elenco-bloccati');
    ul.replaceChildren();
    $('nessun-bloccato').hidden = r.bloccati.length > 0;
    for (const n of r.bloccati) {
      ul.append(el('li', { class: 'riga' }, el('span', { testo: n }), el('button', { type: 'button', class: 'secondario', testo: 'Sblocca', 'aria-label': 'Sblocca ' + n, onclick: async () => { await api('POST', 'sblocca', { nome: n }); caricaBloccati(); } })));
    }
  } catch (err) { /* l'elenco si rilegge al prossimo giro */ }
}

async function blocca(n) {
  if (!confirm('Vuoi bloccare ' + n + '? Non potrà entrare nelle tue partite, trovarti né scriverti in chat, e tu non vedrai le sue partite né i suoi messaggi. Non saprà di essere stato bloccato.')) return false;
  try { await api('POST', 'blocca', { nome: n }); aggiungiMessaggio({ tipo: 'sistema', testo: n + ' è stato bloccato.' }); return true; } catch (err) { aggiungiMessaggio({ tipo: 'sistema', testo: err.message }); return false; }
}

$('modulo-blocca').addEventListener('submit', async (e) => {
  e.preventDefault();
  const n = $('nome-blocca').value.trim();
  if (n && await blocca(n)) { $('nome-blocca').value = ''; caricaBloccati(); }
});

// ------------------------------------------------------------------ sala
async function dopoAccesso() {
  const idLink = idDaLinkOCodice(new URLSearchParams(location.search).get('p') || '');
  if (idLink) {
    await entraDaLink(idLink);
    return;
  }
  sala();
}

async function entraDaLink(id) {
  try {
    const mia = await api('GET', 'mia');
    if (mia.id && mia.id === id) { seguiPartita(id); return; }
    if (mia.id) {
      sala();
      erroreSala('Hai già una partita in corso: prima lasciala.');
      return;
    }
    if (!(await assicuraPassword())) { sala(); return; }
    await api('POST', 'entra_stanza', { id });
    seguiPartita(id);
  } catch (err) {
    sala();
    erroreSala(err.message);
  }
}

let catalogo = null;          // i giochi, come li dà il server
let ultimaRotta = '';         // l'ultima pagina mostrata (home o id del gioco), per non ridisegnare quando non serve

// Ogni gioco ha la sua pagina e il suo link: giochi/#/burraco, giochi/#/monopoli... La home (giochi/#/) elenca i giochi.
function idGiocoDaUrl() {
  const m = /^#\/([a-z]+)\/?$/.exec(location.hash);
  return m ? m[1] : '';
}

function avviaAggiornamentoSala() {
  if (aggiornaSala) clearInterval(aggiornaSala);
  aggiornaSala = setInterval(caricaStanze, 5000);
}

async function sala() {
  partitaAttiva = null;
  impostaUrlPartita('');
  ultimaRotta = '';
  await caricaSala();
  if (!partitaAttiva && token) avviaAggiornamentoSala();
}

async function caricaSala() {
  erroreSala('');
  try {
    // se c'è una partita in corso si riprende da dove era
    const mia = await api('GET', 'mia');
    if (mia.id) { seguiPartita(mia.id); return; }
    const g = await api('GET', 'giochi');
    if (g.chat) urlChat = g.chat;
    $('vai-chat').href = urlChat;
    if (typeof g.ha_password === 'boolean') {
      haPassword = g.ha_password;
      sessionStorage.setItem('wg_ha_password', haPassword ? '1' : '0');
    }
    catalogo = g.giochi;
    mostraRotta();
    await caricaStanze();
    caricaBloccati();
  } catch (err) {
    if (err.codice === 401) return schermataAccesso();
    erroreSala(err.message);
  }
}

function mostraRotta() {
  if (!catalogo) return;
  const id = idGiocoDaUrl();
  const gioco = catalogo.find((x) => x.id === id);
  ultimaRotta = gioco ? gioco.id : '';
  if (gioco) paginaGioco(gioco); else paginaHome();
}

window.addEventListener('hashchange', () => {
  if (!token || partitaAttiva || !catalogo) return;
  if ((idGiocoDaUrl() || '') === ultimaRotta) return;
  erroreSala('');
  mostraRotta();
  caricaStanze();
  avviaAggiornamentoSala();
});

function paginaHome() {
  mostra('schermata-sala');
  document.title = 'Scegli un gioco – Giochi';
  const elenco = $('elenco-giochi');
  elenco.replaceChildren(...catalogo.map((gioco) => el('li', {},
    el('a', { class: 'gioco-link', href: '#/' + gioco.id },
      el('strong', { testo: gioco.nome }),
      el('span', { class: 'descrizione', testo: gioco.descrizione }),
      el('span', { class: 'conta', 'data-conta': gioco.id, testo: '' })))));
}

function paginaGioco(gioco) {
  mostra('schermata-gioco');
  document.title = gioco.nome + ' – Giochi';
  $('titolo-gioco').textContent = gioco.nome;
  $('descrizione-gioco').textContent = gioco.descrizione;
  const varianti = gioco.varianti && gioco.varianti.length > 0
    ? gioco.varianti
    : [{ id: 'classica', nome: 'Classica', descrizione: '' }];
  const selectVariante = varianti.length > 1
    ? el('select', { id: 'variante-' + gioco.id },
        varianti.map((v) => el('option', { value: v.id, testo: v.nome + (v.descrizione ? ' — ' + v.descrizione : '') })))
    : null;
  const scelta = el('select', { id: 'posti-' + gioco.id },
    gioco.posti.map((n) => el('option', { value: n, testo: n + ' giocatori' })));
  const aggiornaPostiPerVariante = () => {
    if (!selectVariante) return;
    const vInfo = varianti.find((v) => v.id === selectVariante.value);
    const ammessi = vInfo && vInfo.posti ? vInfo.posti : gioco.posti;
    const valoreCorrente = Number(scelta.value);
    scelta.replaceChildren(...ammessi.map((n) => el('option', { value: n, testo: n + ' giocatori' })));
    scelta.value = ammessi.includes(valoreCorrente) ? valoreCorrente : ammessi[0];
  };
  if (selectVariante) {
    selectVariante.addEventListener('change', aggiornaPostiPerVariante);
    aggiornaPostiPerVariante();
  }
  const punti = gioco.id === 'burraco'
    ? el('select', { id: 'punti-burraco' },
      [[2005, '2005 punti, la partita completa'], [1005, '1005 punti, più breve'], [505, '505 punti, veloce']].map(([n, t]) => el('option', { value: n, testo: t })))
    : null;
  // il livello del computer: la scelta resta per le prossime partite (solo in questo browser)
  const livello = el('select', { id: 'livello-' + gioco.id },
    [['facile', 'Facile'], ['normale', 'Normale'], ['difficile', 'Difficile']].map(([v, t]) => el('option', { value: v, testo: t })));
  try { livello.value = localStorage.getItem('wg_livello') || 'normale'; } catch (e) { livello.value = 'normale'; }
  livello.addEventListener('change', () => { try { localStorage.setItem('wg_livello', livello.value); } catch (e) { /* senza memoria vale solo adesso */ } });
  livelloScelto = () => livello.value;
  const p = () => (punti ? Number(punti.value) : 0);
  const vScelta = () => (selectVariante ? selectVariante.value : 'classica');
  const pulsante = (testo, classe, azione) => el('button', { type: 'button', class: classe, onclick: azione, testo, 'aria-label': testo + ', ' + gioco.nome });
  $('pagina-gioco').replaceChildren(el('div', { 'data-gioco': gioco.id },
    el('div', { class: 'pannello tavoli-gioco' },
      el('h3', { id: 'titolo-tavoli', testo: 'Tavoli aperti' }),
      el('p', { class: 'vuoto', testo: 'Nessun tavolo aperto.' }),
      el('ul', { class: 'stanze lista-tavoli', 'aria-labelledby': 'titolo-tavoli' })),
    el('div', { class: 'pannello' },
      el('h3', { testo: 'Apri un nuovo tavolo' }),
      el('div', { class: 'campi' },
        selectVariante ? el('div', { class: 'campo' }, el('label', { for: 'variante-' + gioco.id, testo: 'Variante' }), selectVariante) : null,
        el('div', { class: 'campo' }, el('label', { for: 'posti-' + gioco.id, testo: 'Quanti giocatori' }), scelta),
        punti ? el('div', { class: 'campo' }, el('label', { for: 'punti-burraco', testo: 'Fino a quanti punti' }), punti) : null,
        el('div', { class: 'campo' }, el('label', { for: 'livello-' + gioco.id, testo: 'Livello del computer' }), livello)),
      el('div', { class: 'riga' },
        pulsante('Apri un tavolo', '', () => creaPartita(gioco.id, Number(scelta.value), false, p(), vScelta())),
        pulsante('Invita una persona', 'secondario', () => invitaNuovoTavolo(gioco.id, Number(scelta.value), p(), vScelta())),
        pulsante('Gioca contro il computer', 'secondario', () => creaPartita(gioco.id, Number(scelta.value), true, p(), vScelta()))))));
}

function voceTavolo(s, conGioco) {
  const variante = s.variante_nome && s.variante !== 'classica' ? ' (' + s.variante_nome + ')' : '';
  const testo = (conGioco ? s.nome_gioco : '') + variante + (conGioco ? ', ' : '') + (s.invitato ? 'invito di ' : 'tavolo di ') + s.creatore + ', ' + s.occupati + ' giocatori su ' + s.posti;
  const comandi = [el('button', { type: 'button', onclick: () => entraInPartita(s.id), testo: 'Entra', 'aria-label': 'Entra nel tavolo di ' + s.creatore + (conGioco ? ', ' + s.nome_gioco : '') + variante })];
  if (s.invitato) {
    comandi.push(el('button', { type: 'button', class: 'secondario', onclick: () => rifiutaInvito(s.id), testo: 'Rifiuta', 'aria-label': 'Rifiuta l\'invito di ' + s.creatore }));
  }
  return el('li', {}, el('span', { testo }), ...comandi);
}

async function caricaStanze() {
  try {
    const [tavoli, inviti] = await Promise.all([api('GET', 'tavoli'), api('GET', 'inviti')]);
    const perGioco = {};
    // un tavolo già al completo (per esempio con due giocatori su due) non si mostra: non ci si può più sedere
    for (const s of tavoli.tavoli.filter((x) => x.occupati < x.posti)) {
      (perGioco[s.gioco] || (perGioco[s.gioco] = [])).push(s);
    }
    for (const scheda of document.querySelectorAll('[data-gioco]')) {
      const id = scheda.getAttribute('data-gioco');
      const aperti = perGioco[id] || [];
      const vuoto = scheda.querySelector('.vuoto');
      const lista = scheda.querySelector('.lista-tavoli');
      if (!lista) continue;
      vuoto.hidden = aperti.length > 0;
      lista.replaceChildren(...aperti.map((s) => voceTavolo(s, false)));
    }
    for (const c of document.querySelectorAll('[data-conta]')) {
      const n = (perGioco[c.getAttribute('data-conta')] || []).length;
      c.textContent = n === 0 ? 'Nessun tavolo aperto' : (n === 1 ? '1 tavolo aperto' : n + ' tavoli aperti');
    }
    const listaInviti = $('elenco-inviti');
    listaInviti.replaceChildren(...inviti.inviti.map((s) => voceTavolo(s, true)));
    $('nessun-invito').hidden = inviti.inviti.length > 0;
  } catch (err) {
    if (err.codice === 401) schermataAccesso();
  }
}

function chiediNomeInvito() {
  $('errore-invito-nuovo').textContent = '';
  $('nome-invito-nuovo').value = '';
  $('chiedi-invito').hidden = false;
  $('nome-invito-nuovo').focus();
  return new Promise((resolve) => {
    const fine = (nomeScelto) => {
      $('chiedi-invito').hidden = true;
      $('modulo-invito-nuovo').onsubmit = null;
      $('annulla-invito').onclick = null;
      resolve(nomeScelto);
    };
    $('annulla-invito').onclick = () => fine('');
    $('modulo-invito-nuovo').onsubmit = (e) => {
      e.preventDefault();
      const scritto = $('nome-invito-nuovo').value.trim();
      if (scritto.length < 2) {
        $('errore-invito-nuovo').textContent = 'Scrivi il nome utente, da 2 a 20 caratteri.';
        return;
      }
      fine(scritto);
    };
  });
}

async function invitaNuovoTavolo(gioco, posti, punti, variante) {
  erroreSala('');
  if (!(await assicuraPassword())) return;
  const nomeInvito = await chiediNomeInvito();
  if (!nomeInvito) return;
  try {
    const r = await api('POST', 'crea', { gioco, posti, computer: false, punti: punti || 0, variante: variante || 'classica', livello: livelloScelto() });
    try {
      await api('POST', 'invita', { id: r.id, nome: nomeInvito });
    } catch (err) {
      erroreSala(err.message);
    }
    seguiPartita(r.id);
  } catch (err) {
    erroreSala(err.message);
  }
}

async function rifiutaInvito(id) {
  erroreSala('');
  try {
    await api('POST', 'rifiuta_invito', { id });
    caricaStanze();
  } catch (err) {
    erroreSala(err.message);
  }
}

// Nomi utente vecchi, entrati prima di questa regola: per giocare con altri la password va scritta, e da lì resta assegnata.
function assicuraPassword() {
  if (haPassword) return Promise.resolve(true);
  $('errore-password').textContent = '';
  $('password-assegna').value = '';
  $('chiedi-password').hidden = false;
  $('password-assegna').focus();
  return new Promise((resolve) => {
    const fine = (ok) => {
      $('chiedi-password').hidden = true;
      $('modulo-assegna').onsubmit = null;
      $('annulla-password').onclick = null;
      resolve(ok);
    };
    $('annulla-password').onclick = () => fine(false);
    $('modulo-assegna').onsubmit = async (e) => {
      e.preventDefault();
      const scritta = $('password-assegna').value;
      if (scritta.length < 8) {
        $('errore-password').textContent = 'La password è troppo corta: servono almeno 8 caratteri.';
        return;
      }
      try {
        const auth = await derivaAuth(nome, scritta);
        let r;
        try { r = await api('POST', 'entra', { nome, auth }); }
        catch (err) { if (err.codice === 401) r = await api('POST', 'entra', { nome, password: scritta }); else throw err; }
        ricordaAccesso(r);
        fine(!!r.ha_password);
      } catch (err) {
        $('errore-password').textContent = err.message;
      }
    };
  });
}

let livelloScelto = () => 'normale';
async function creaPartita(gioco, posti, computer, punti, variante) {
  erroreSala('');
  if (!computer && !(await assicuraPassword())) return;
  try {
    const r = await api('POST', 'crea', { gioco, posti, computer, punti: punti || 0, variante: variante || 'classica', livello: livelloScelto() });
    seguiPartita(r.id);
  } catch (err) {
    erroreSala(err.message);
  }
}

async function entraInPartita(id) {
  erroreSala('');
  if (!(await assicuraPassword())) return;
  try {
    await api('POST', 'entra_stanza', { id });
    seguiPartita(id);
  } catch (err) {
    erroreSala(err.message);
    caricaStanze();
  }
}

$('modulo-codice').addEventListener('submit', async (e) => {
  e.preventDefault();
  const id = idDaLinkOCodice($('codice-partita').value);
  if (!id) {
    erroreSala('Scrivi il codice del tavolo o incolla il link.');
    return;
  }
  await entraInPartita(id);
});

$('modulo-invita').addEventListener('submit', async (e) => {
  e.preventDefault();
  const scritto = $('nome-invita').value.trim();
  $('esito-invita').textContent = '';
  if (!partitaAttiva) return;
  if (scritto.length < 2) {
    $('esito-invita').textContent = 'Scrivi il nome utente, da 2 a 20 caratteri.';
    return;
  }
  try {
    const r = await api('POST', 'invita', { id: partitaAttiva, nome: scritto });
    $('nome-invita').value = '';
    $('esito-invita').textContent = 'Hai invitato ' + (r.nome || scritto) + '.';
  } catch (err) {
    $('esito-invita').textContent = err.message;
  }
});

// ------------------------------------------------------------------ battaglia navale
// <casellario>
// Il casellario della battaglia navale: una griglia con le colonne A, B, C... e le righe 1, 2, 3... Ci si muove con le frecce e ogni casella si legge
// come in un foglio di calcolo con NVDA o JAWS: prima quello che c'è, poi le coordinate ("acqua, B 3"). Serve per mettere le navi all'inizio
// (Invio posiziona la nave scelta, R la gira) e poi per sparare (Invio sul mare nemico). La logica (cursore, cosa dire, posizionamento) sta in
// nuovoCasellario e non tocca la pagina; creaCasellario la disegna come una tabella ARIA (role="grid") con una casella alla volta raggiungibile.
const STATI_CASELLA = ['vuota', 'nave', 'acqua', 'colpita', 'affondata'];

function letteraColonna(c) { return String.fromCharCode(65 + c); }
function coordinateCasella(r, c) { return letteraColonna(c) + ' ' + (r + 1); }

function nuovoCasellario(righe, colonne, titolo) {
  const celle = Array.from({ length: righe }, () => Array(colonne).fill('vuota'));
  const k = {
    righe, colonne, titolo: titolo || '', celle, riga: 0, colonna: 0, lunghezzaNave: 0, orizzontale: true, naviDistanziate: false,
    imposta(r, c, stato) { if (r >= 0 && r < righe && c >= 0 && c < colonne && STATI_CASELLA.includes(stato)) celle[r][c] = stato; },
    /** Sposta il cursore. Falso se si è al bordo: il cursore resta dov'è. */
    muovi(dr, dc) {
      const r = k.riga + dr; const c = k.colonna + dc;
      if (r < 0 || r >= righe || c < 0 || c >= colonne) return false;
      k.riga = r; k.colonna = c; return true;
    },
    vaiA(r, c) { k.riga = Math.max(0, Math.min(righe - 1, r)); k.colonna = Math.max(0, Math.min(colonne - 1, c)); },
    /** Cosa si dice su una casella: prima quello che c'è, poi le coordinate. */
    frase(r = k.riga, c = k.colonna) {
      let t = celle[r][c] + ', ' + coordinateCasella(r, c);
      if (k.lunghezzaNave > 0) t += k.puoPosizionare(r, c) ? '. Qui ci sta la nave da ' + k.lunghezzaNave : '. Qui la nave da ' + k.lunghezzaNave + ' non ci sta';
      return t;
    },
    /** Le caselle [riga, colonna] che occuperebbe la nave se partisse da qui (anche fuori dal casellario, se non ci sta). */
    caselleNave(r, c) {
      const x = [];
      for (let i = 0; i < k.lunghezzaNave; i++) x.push([r + (k.orizzontale ? 0 : i), c + (k.orizzontale ? i : 0)]);
      return x;
    },
    puoPosizionare(r, c) {
      if (k.lunghezzaNave <= 0) return false;
      for (const [rr, cc] of k.caselleNave(r, c)) {
        if (rr < 0 || rr >= righe || cc < 0 || cc >= colonne || celle[rr][cc] !== 'vuota') return false;
        if (k.naviDistanziate) {
          for (let dr = -1; dr <= 1; dr++) for (let dc = -1; dc <= 1; dc++) {
            const ar = rr + dr; const ac = cc + dc;
            if (ar >= 0 && ar < righe && ac >= 0 && ac < colonne && celle[ar][ac] === 'nave') return false;
          }
        }
      }
      return true;
    },
    posiziona(r, c) {
      if (!k.puoPosizionare(r, c)) return false;
      for (const [rr, cc] of k.caselleNave(r, c)) celle[rr][cc] = 'nave';
      return true;
    },
    ruota() { k.orizzontale = !k.orizzontale; },
    frasePosizionata(r, c) {
      const fine = k.caselleNave(r, c)[k.lunghezzaNave - 1];
      return 'Nave da ' + k.lunghezzaNave + ' posizionata, ' + (k.orizzontale ? 'orizzontale' : 'verticale') + ', da ' + coordinateCasella(r, c) + ' a ' + coordinateCasella(fine[0], fine[1]) + '.';
    },
    fraseNaveScelta() { return 'Nave da ' + k.lunghezzaNave + ', ' + (k.orizzontale ? 'orizzontale' : 'verticale') + '.'; },
  };
  return k;
}

/**
 * Disegna il casellario in "contenitore" come una tabella role="grid". Una sola casella è raggiungibile con Tab (quella del cursore), le frecce
 * spostano il cursore e il lettore di schermo legge la casella (l'etichetta è la frase: "acqua, B 3"; le intestazioni visibili non si leggono due volte).
 * opzioni: alPremere(riga, colonna) con Invio o Spazio; alRuotare() con R; alCambiareMare() con M; dopoSpostamento(frase) dopo ogni spostamento.
 * Restituisce { aggiorna(), fuoco(), elemento }.
 */
function creaCasellario(k, contenitore, opzioni) {
  const o = opzioni || {};
  const tabella = document.createElement('table');
  tabella.className = 'casellario';
  tabella.setAttribute('role', 'grid');
  tabella.setAttribute('aria-label', k.titolo);
  const intestazione = tabella.createTHead().insertRow();
  const angolo = document.createElement('th'); angolo.setAttribute('aria-hidden', 'true'); intestazione.append(angolo);
  for (let c = 0; c < k.colonne; c++) {
    const th = document.createElement('th'); th.scope = 'col'; th.textContent = letteraColonna(c); th.setAttribute('aria-hidden', 'true'); intestazione.append(th);
  }
  const corpo = tabella.createTBody();
  const celle = [];
  for (let r = 0; r < k.righe; r++) {
    const riga = corpo.insertRow();
    riga.setAttribute('role', 'row');
    const th = document.createElement('th'); th.scope = 'row'; th.textContent = String(r + 1); th.setAttribute('aria-hidden', 'true'); riga.append(th);
    celle.push([]);
    for (let c = 0; c < k.colonne; c++) {
      const td = riga.insertCell();
      td.setAttribute('role', 'gridcell');
      td.dataset.riga = String(r); td.dataset.colonna = String(c);
      celle[r].push(td);
    }
  }
  function aggiorna() {
    const anteprima = new Set();
    let buona = false;
    if (k.lunghezzaNave > 0) {
      buona = k.puoPosizionare(k.riga, k.colonna);
      for (const [rr, cc] of k.caselleNave(k.riga, k.colonna)) if (rr >= 0 && rr < k.righe && cc >= 0 && cc < k.colonne) anteprima.add(rr * 100 + cc);
    }
    for (let r = 0; r < k.righe; r++) for (let c = 0; c < k.colonne; c++) {
      const td = celle[r][c];
      const stato = k.celle[r][c];
      td.className = 'stato-' + stato + (anteprima.has(r * 100 + c) && stato === 'vuota' ? (buona ? ' anteprima-buona' : ' anteprima-cattiva') : '') + (r === k.riga && c === k.colonna ? ' cursore' : '');
      td.textContent = stato === 'acqua' ? '•' : (stato === 'colpita' || stato === 'affondata') ? 'X' : '';
      td.setAttribute('aria-label', k.frase(r, c));
      td.tabIndex = r === k.riga && c === k.colonna ? 0 : -1;
    }
  }
  function fuoco() { celle[k.riga][k.colonna].focus({ preventScroll: false }); }
  tabella.addEventListener('keydown', (e) => {
    if (e.ctrlKey || e.altKey || e.metaKey) {
      if (e.ctrlKey && !e.altKey && !e.metaKey && (e.key === 'Home' || e.key === 'End')) {
        e.preventDefault();
        if (e.key === 'Home') k.vaiA(0, 0); else k.vaiA(k.righe - 1, k.colonne - 1);
        aggiorna(); fuoco(); if (o.dopoSpostamento) o.dopoSpostamento(k.frase());
      }
      return;
    }
    const mosse = { ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1] };
    if (mosse[e.key]) {
      e.preventDefault();
      if (k.muovi(mosse[e.key][0], mosse[e.key][1])) { aggiorna(); fuoco(); if (o.dopoSpostamento) o.dopoSpostamento(k.frase()); }
      return;   // al bordo il cursore resta fermo e non si dice niente
    }
    if (e.key === 'Home') { e.preventDefault(); k.vaiA(k.riga, 0); aggiorna(); fuoco(); if (o.dopoSpostamento) o.dopoSpostamento(k.frase()); return; }
    if (e.key === 'End') { e.preventDefault(); k.vaiA(k.riga, k.colonne - 1); aggiorna(); fuoco(); if (o.dopoSpostamento) o.dopoSpostamento(k.frase()); return; }
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (o.alPremere) o.alPremere(k.riga, k.colonna); return; }
    if (e.key === 'r' || e.key === 'R') { e.preventDefault(); if (o.alRuotare) o.alRuotare(); aggiorna(); return; }
    if (e.key === 'm' || e.key === 'M') { e.preventDefault(); if (o.alCambiareMare) o.alCambiareMare(); }
  });
  tabella.addEventListener('click', (e) => {
    const td = e.target.closest('td'); if (!td) return;
    k.vaiA(Number(td.dataset.riga), Number(td.dataset.colonna)); aggiorna(); fuoco();
    if (o.alPremere) o.alPremere(k.riga, k.colonna);   // col mouse il clic fa subito quello che fa Invio: mette la nave o spara
  });
  // col mouse l'anteprima della nave segue il puntatore; il tasto destro gira la nave
  tabella.addEventListener('mouseover', (e) => {
    const td = e.target.closest('td'); if (!td || k.lunghezzaNave <= 0) return;
    k.vaiA(Number(td.dataset.riga), Number(td.dataset.colonna)); aggiorna();
  });
  tabella.addEventListener('contextmenu', (e) => {
    if (k.lunghezzaNave <= 0 || !o.alRuotare) return;
    e.preventDefault();
    const td = e.target.closest('td'); if (td) k.vaiA(Number(td.dataset.riga), Number(td.dataset.colonna));
    o.alRuotare(); aggiorna();
  });
  contenitore.replaceChildren(tabella);
  aggiorna();
  return { aggiorna, fuoco, elemento: tabella };
}
// </casellario>

// <battaglia-web>
// La battaglia navale sulla pagina: il casellario sta in #tavolo. In posizionamento si vede la propria flotta (Invio mette la nave, R la gira,
// C mette a caso le navi che mancano, X toglie l'ultima, P dice che la flotta è pronta); in battaglia, quando tocca a te, si vede il mare
// nemico e Invio spara; M passa dal mare nemico alla propria flotta e viceversa; T dice la situazione, Q le navi nemiche affondate.
// Cursore e casellari restano gli stessi da una mossa all'altra.
let battaglia = null;
// il cannone con la nave all'orizzonte (disegno vettoriale, decorativo)
const IMMAGINE_CANNONE = '<svg viewBox="0 0 120 70" xmlns="http://www.w3.org/2000/svg" focusable="false">'
  + '<rect width="120" height="70" rx="8" fill="#78b4e6"/><path d="M0 52q10-6 20 0t20 0 20 0 20 0 20 0 20 0V70H0z" fill="#3f7fbf"/>'
  + '<path d="M68 38h38l-5 8H72z" fill="#35404f"/><rect x="82" y="26" width="3" height="12" fill="#35404f"/><path d="M85 26l14 6H85z" fill="#e8e0c8"/>'
  + '<g transform="rotate(-18 34 46)"><rect x="12" y="38" width="46" height="13" rx="6" fill="#3a3a3a" stroke="#111" stroke-width="2"/><rect x="52" y="35" width="10" height="19" rx="3" fill="#555" stroke="#111" stroke-width="2"/><circle cx="14" cy="44.5" r="5" fill="#222"/></g>'
  + '<circle cx="32" cy="55" r="10" fill="#8a5a2b" stroke="#4a2e12" stroke-width="3"/><circle cx="32" cy="55" r="3" fill="#4a2e12"/>'
  + '<path d="M58 28l6-6 4 7 8-3-3 8 7 4-9 2" fill="#ffd23c" stroke="#f0641e" stroke-width="2" stroke-linejoin="round"/></svg>';

function carattereACasella(ch) { return { N: 'nave', o: 'acqua', X: 'colpita', '#': 'affondata' }[ch] || 'vuota'; }

function riempiCasellario(k, righe) {
  if (!Array.isArray(righe)) return;
  for (let r = 0; r < k.righe; r++) for (let c = 0; c < k.colonne; c++) k.imposta(r, c, carattereACasella((righe[r] || '')[c]));
}

function haMossa(s, id) { return s.mosse.some((m) => m.id === id); }

function dichiaraBattaglia(testo) {
  const v = $('battaglia-voce');
  if (!v) return;
  v.textContent = '';
  setTimeout(() => { v.textContent = testo; }, 30);   // il lettore di schermo rilegge anche se la frase è uguale alla precedente
}

function mossaBattaglia(id) { if (ultimoStato && haMossa(ultimoStato, id)) inviaMossa(id); else dichiaraBattaglia('Adesso non si può.'); }

function istruzioneBattaglia(s, b) {
  const v = s.vista;
  if (v.fase === 'posizionamento') {
    // sulla pagina si dice solo come stanno le cose, senza spiegare i tasti
    if (haMossa(s, 'nave')) return 'Posiziona la nave da ' + v.prossima + ' caselle, ' + (b.mia.orizzontale ? 'orizzontale' : 'verticale') + '.';
    if (haMossa(s, 'pronto')) return 'Hai messo tutte le navi.';
    return 'La tua flotta è pronta. Aspetto l\'altro giocatore.';
  }
  if (v.fase === 'finita') return 'La partita è finita.';
  if (haMossa(s, 'spara')) return 'Tocca a te, ' + (b.mare === 'nemica' ? 'sei sul mare nemico.' : 'sei sulla tua flotta.');
  return 'Non tocca a te.';
}

function disegnaBattaglia(s) {
  const v = s.vista;
  const tavolo = $('tavolo');
  if (v.mia === undefined) {   // chi guarda soltanto non ha una flotta
    tavolo.replaceChildren(el('p', { testo: 'Stai guardando la partita: ' + (v.fase === 'finita' ? 'è finita.' : 'le flotte sono nascoste.') }));
    return;
  }
  if (!battaglia || battaglia.id !== s.id || battaglia.mia.righe !== v.righe || battaglia.mia.colonne !== v.colonne) {
    battaglia = { id: s.id, mia: nuovoCasellario(v.righe, v.colonne, 'La tua flotta'), nemica: nuovoCasellario(v.righe, v.colonne, 'Mare nemico'), mare: 'mia', novita: -1 };
  }
  const b = battaglia;
  const aveva = tavolo.contains(document.activeElement);
  riempiCasellario(b.mia, v.mia);
  riempiCasellario(b.nemica, v.nemica);
  if (v.fase === 'posizionamento') b.mare = 'mia';
  else if (b.novita !== s.ultimo_evento) b.mare = haMossa(s, 'spara') || v.fase === 'battaglia' ? 'nemica' : b.mare;
  b.novita = s.ultimo_evento;
  b.mia.lunghezzaNave = v.fase === 'posizionamento' && haMossa(s, 'nave') ? v.prossima : 0;
  b.mia.naviDistanziate = !!v.distanza;
  b.nemica.lunghezzaNave = 0;

  const k = () => (b.mare === 'mia' ? b.mia : b.nemica);
  let griglia = null;
  const zona = el('div', { class: 'zona-casellario' });
  const info = el('p', { class: 'istruzione-battaglia', id: 'battaglia-istruzione' });
  const voce = el('div', { id: 'battaglia-voce', class: 'solo-voce', role: 'status', 'aria-live': 'polite' });
  function ridisegna(focalizza) {
    info.textContent = istruzioneBattaglia(s, b);
    griglia = creaCasellario(k(), zona, {
      alPremere(r, c) {
        const cas = k();
        if (v.fase === 'posizionamento') {
          if (!haMossa(s, 'nave')) { dichiaraBattaglia(istruzioneBattaglia(s, b)); return; }
          if (!cas.puoPosizionare(r, c)) { dichiaraBattaglia('Qui la nave da ' + cas.lunghezzaNave + ' non ci sta. ' + cas.frase()); return; }
          inviaMossa('nave|' + r + '|' + c + '|' + (cas.orizzontale ? 'o' : 'v'));
        } else if (v.fase === 'battaglia') {
          if (b.mare !== 'nemica') dichiaraBattaglia(cas.frase());
          else if (!haMossa(s, 'spara')) dichiaraBattaglia('Non tocca a te.');
          else if (cas.celle[r][c] !== 'vuota') dichiaraBattaglia('Hai già sparato qui. ' + cas.frase());
          else inviaMossa('spara|' + r + '|' + c);
        } else dichiaraBattaglia(cas.frase());
      },
      alRuotare() {
        const cas = b.mia;
        if (b.mare !== 'mia' || cas.lunghezzaNave <= 0) { dichiaraBattaglia('Adesso non si può.'); return; }
        cas.ruota();
        dichiaraBattaglia(cas.fraseNaveScelta() + (cas.puoPosizionare(cas.riga, cas.colonna) ? ' Qui ci sta.' : ' Qui non ci sta.'));
        info.textContent = istruzioneBattaglia(s, b);
      },
      alCambiareMare() {
        if (v.fase === 'posizionamento') { dichiaraBattaglia('Adesso non si può.'); return; }
        b.mare = b.mare === 'mia' ? 'nemica' : 'mia';
        ridisegna(true);
        dichiaraBattaglia(k().titolo + '. ' + k().frase());
      },
    });
    griglia.elemento.addEventListener('keydown', (e) => {
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      const t = e.key.toLowerCase();
      if (t === 'c') { e.preventDefault(); mossaBattaglia('casuale'); }
      else if (t === 'x') { e.preventDefault(); mossaBattaglia('annulla'); }
      else if (t === 'p') { e.preventDefault(); mossaBattaglia('pronto'); }
      else if (t === 't') {
        e.preventDefault();
        dichiaraBattaglia(v.fase === 'posizionamento' ? 'Hai messo ' + (v.navi_nemiche_numero - v.da_posizionare.length) + ' navi su ' + v.navi_nemiche_numero + '.' : 'Hai ancora ' + v.mie_navi_rimaste + ' navi, il nemico ne ha ' + v.nemiche_rimaste + '.');
      } else if (t === 'q') {
        e.preventDefault();
        dichiaraBattaglia(v.navi_nemiche_perse.length ? 'Hai affondato: ' + v.navi_nemiche_perse.map((l) => 'nave da ' + l).join(', ') + '.' : 'Non hai ancora affondato nessuna nave.');
      }
    });
    if (focalizza) griglia.fuoco();
  }
  const immagine = el('span', { 'aria-hidden': 'true' });
  immagine.innerHTML = IMMAGINE_CANNONE;   // solo decorazione: la partita si capisce da testo e voce
  tavolo.replaceChildren(el('div', { class: 'banda-battaglia' }, immagine, info), zona, voce);
  ridisegna(aveva);
  const stato = v.fase === 'posizionamento'
    ? 'Navi messe: ' + (v.navi_nemiche_numero - v.da_posizionare.length) + ' su ' + v.navi_nemiche_numero + '.'
    : 'Le tue navi in gioco: ' + v.mie_navi_rimaste + '. Navi nemiche in gioco: ' + v.nemiche_rimaste + '.';
  tavolo.append(el('p', { testo: stato }));
}
// </battaglia-web>

// ------------------------------------------------------------------ partita
// <flusso-giochi>
// Il flusso di eventi della partita: invece di una richiesta lunga che si rinnova ogni 20 secondi, una connessione sempre aperta su cui il
// server scrive appena c'è qualcosa (e dà segno di vita ogni 15 secondi). Si tiene l'ultimo stato e gli eventi arrivati; chi aspetta riceve
// lo stato appena ci sono eventi più nuovi di quelli che conosce. Se la rete cade si riapre da solo. Se il server non ha i flussi
// (risponde con un errore) si ripiega sulle richieste lunghe di prima.
let flussoPartita = null;
let flussiSospesiFino = 0;

function nuovoFlussoPartita(id, da) {
  const f = {
    id, ctrl: null, stato: null, eventi: new Map(), maxVisibile: da, daServer: da,
    errore: null, fermato: false, ultimoUso: Date.now(), risvegli: [],
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
    if (nome !== 'stato' || dati.length === 0) return;
    const s = JSON.parse(dati.join('\n'));
    f.stato = s;
    for (const e of s.eventi || []) { f.eventi.set(e.n, e); if (e.n > f.maxVisibile) f.maxVisibile = e.n; }
    if (typeof s.ultimo_evento === 'number' && s.ultimo_evento > f.daServer) f.daServer = s.ultimo_evento;
    while (f.eventi.size > 400) f.eventi.delete(f.eventi.keys().next().value);
    f.sveglia();
  };
  (async () => {
    let tentativo = 0; let inizioCaduta = 0;
    try {
      while (!f.fermato) {
        if (Date.now() - f.ultimoUso > 90000) break;   // nessuno lo usa più
        const ctrl = new AbortController(); f.ctrl = ctrl;
        let orologio = null;
        try {
          const risposta = await fetch(API + '?a=flusso&id=' + encodeURIComponent(id) + '&da=' + f.daServer, {
            headers: token ? { 'X-Token': token } : {}, signal: ctrl.signal, cache: 'no-store',
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
            if (Date.now() - f.ultimoUso > 90000) { ctrl.abort(); break; }
          }
        } catch (err) {
          if (f.fermato) return;
          if (err && err.codice) {
            // il server non ha i flussi o non vanno: per un po' si usano le richieste lunghe (un 401 o 403 invece è un fatto di questa partita)
            f.errore = err;
            if (err.codice === 400 || err.codice === 404 || err.codice >= 500) flussiSospesiFino = Date.now() + 300000;
            return;
          }
          if (Date.now() - f.ultimoUso > 90000) return;
          tentativo++; if (tentativo === 1) inizioCaduta = Date.now();
          if (Date.now() - inizioCaduta > 180000) { const e = new Error('Non riesco a collegarmi al server dei giochi. Controlla la connessione a internet.'); e.codice = 0; f.errore = e; return; }
          await new Promise((r) => setTimeout(r, Math.min(15000, 1000 * (2 ** Math.min(tentativo - 1, 4))) + Math.random() * 500));
        } finally { if (orologio) clearInterval(orologio); }
      }
    } finally { f.fermato = true; f.sveglia(); }
  })();
  return f;
}

function statoDalFlusso(f, da) {
  const r = JSON.parse(JSON.stringify(f.stato));
  r.eventi = [...f.eventi.entries()].filter(([n]) => n > da).sort((a, b) => a[0] - b[0]).map(([, e]) => e);
  return r;
}

// Aspetta fino a "attesa" secondi che ci siano eventi più nuovi di "da" e restituisce lo stato (con solo gli eventi dopo "da").
async function aspettaStato(id, da, attesa) {
  if (Date.now() >= flussiSospesiFino && typeof ReadableStream === 'function' && typeof AbortController === 'function') {
    if (!flussoPartita || flussoPartita.id !== id || flussoPartita.fermato || flussoPartita.errore) {
      if (flussoPartita) flussoPartita.ferma();
      flussoPartita = nuovoFlussoPartita(id, da);
    }
    const f = flussoPartita;
    f.ultimoUso = Date.now();
    const fine = Date.now() + attesa * 1000;
    for (;;) {
      if (f.errore) { if (f.errore.codice === 0) throw f.errore; break; }
      if (f.stato && f.maxVisibile > da) return statoDalFlusso(f, da);
      if (f.fermato) break;
      const resto = fine - Date.now();
      if (resto <= 0) return api('GET', 'stato', null, { id, da, attesa: 0 });   // niente di nuovo: si rilegge lo stato com'è
      await Promise.race([new Promise((ok) => f.risvegli.push(ok)), new Promise((ok) => setTimeout(ok, resto))]);
      f.ultimoUso = Date.now();
    }
  }
  return api('GET', 'stato', null, { id, da, attesa: ATTESA_FORZATA !== null ? ATTESA_FORZATA : attesa });
}
// </flusso-giochi>

async function seguiPartita(id) {
  partitaAttiva = id;
  cartaScelta = null;
  ultimoStato = null;
  eraMioTurno = false;
  firmaOpzioni = '';
  impostaUrlPartita(id);
  mostra('schermata-partita');
  $('registro').replaceChildren();
  $('tavolo').replaceChildren();
  $('mosse').replaceChildren();
  $('situazione-pannello').hidden = true;
  let da = 0;
  try {
    while (partitaAttiva === id) {
      try {
        // quello che la pagina ha già mostrato (per esempio la risposta a una mossa) non si ripete
        const base = Math.max(da, ultimoStato && ultimoStato.id === id ? ultimoStato.ultimo_evento : 0);
        const s = da === 0 ? await api('GET', 'stato', null, { id, da, attesa: 0 }) : await aspettaStato(id, base, ATTESA_FORZATA !== null ? Number(ATTESA_FORZATA) : attesaCorrente);
        if (partitaAttiva !== id) return;
        da = s.ultimo_evento;
        // si ridisegna solo se la partita è cambiata
        if (!ultimoStato || s.versione !== ultimoStato.versione || s.eventi.length) aggiorna(s);
      } catch (err) {
        if (partitaAttiva !== id) return;
        if (err.codice === 401) return schermataAccesso();
        if (err.codice === 404 || err.codice === 403) {
          partitaAttiva = null;
          await sala();
          erroreSala('La partita non c\'è più.');
          return;
        }
        await new Promise((r) => setTimeout(r, 3000));
      }
    }
  } finally {
    if (flussoPartita && flussoPartita.id === id) { flussoPartita.ferma(); flussoPartita = null; }
  }
}

function aggiungiMessaggio(e) {
  const registro = $('registro');
  let p;
  if (e.tipo === 'chat') p = el('p', { class: 'chat-msg', testo: e.da + ': ' + e.testo });
  else if (e.tipo === 'sistema') p = el('p', { class: 'sistema', testo: e.testo });
  else p = el('p', { class: e.per !== null ? 'privato' : '', testo: e.testo });
  registro.append(p);
  registro.scrollTop = registro.scrollHeight;
}

// I suoni dei giochi (carte, dadi, soldi...): li sceglie il server (campo "suono" degli eventi), sono i file di suoni/.
function suoniAttivi() {
  try { return localStorage.getItem('wg_suoni') !== '0'; } catch (e) { return true; }
}
function aggiornaPulsanteSuoni() {
  const b = $('suoni');
  const attivi = suoniAttivi();
  b.textContent = 'Suoni: ' + (attivi ? 'attivi' : 'spenti');
  b.setAttribute('aria-pressed', attivi ? 'true' : 'false');
}
$('suoni').addEventListener('click', () => {
  try { localStorage.setItem('wg_suoni', suoniAttivi() ? '0' : '1'); } catch (e) { /* senza memoria del browser la scelta vale solo adesso */ }
  aggiornaPulsanteSuoni();
});
aggiornaPulsanteSuoni();
const bufferSuoni = {};
// i suoni che si ripetono molto hanno più varianti (nome, nome_2, nome_3...): se ne sceglie una a caso, come fa il programma
const VARIANTI_SUONI = { carta: 4, pesca: 3, presa: 2, calata: 2, dadi: 3, passo: 3, soldi: 3, casa: 2, compra: 2, colpito: 2, posiziona: 2 };
function variante(nomeSuono) {
  const n = VARIANTI_SUONI[nomeSuono] || 1;
  const k = 1 + Math.floor(Math.random() * n);
  return k === 1 ? nomeSuono : nomeSuono + '_' + k;
}
let codaSuoni = [];
let suonoInCorso = false;
function suonaProssimo() {
  const scelto = codaSuoni.shift();
  if (!scelto) { suonoInCorso = false; return; }
  const nomeSuono = variante(scelto);
  suonoInCorso = true;
  try {
    if (!bufferSuoni[nomeSuono]) bufferSuoni[nomeSuono] = new Audio('suoni/' + nomeSuono + '.wav');
    const a = bufferSuoni[nomeSuono];
    a.currentTime = 0;
    const p = a.play();
    if (p && p.catch) p.catch(() => {});
  } catch (e) { /* senza audio si gioca in silenzio */ }
  // il prossimo parte quando questo è finito davvero (non a tempo fisso: i suoni durano poco ma non tutti uguale)
  let passato = false;
  const avanti = () => { if (passato) return; passato = true; setTimeout(suonaProssimo, 150); };
  try {
    const a = bufferSuoni[nomeSuono];
    a.addEventListener('ended', avanti, { once: true });
    setTimeout(avanti, Math.max(800, (isFinite(a.duration) && a.duration > 0 ? a.duration : 2) * 1000 + 400));
  } catch (e) { setTimeout(avanti, 1500); }
}

// Alla battaglia navale il computer risponde subito, ma i suoi colpi si mostrano uno alla volta, dopo che sono finiti i suoni del colpo prima.
function dopoSuoni(f) {
  const prova = () => { if (suonoInCorso) setTimeout(prova, 120); else setTimeout(f, 450); };
  setTimeout(prova, 250);
}
function indiceSecondoColpo(evs) { return evs.findIndex((e, k) => k > 0 && e.suono === 'cannone'); }

// I colpi del computer ancora da mostrare: { id, stato (lo stato da disegnare alla fine), coda (gli eventi), silenzioso }.
let rinvio = null;
function mostraTesta(evs, silenzioso) {
  const i = indiceSecondoColpo(evs);
  const testa = i > 0 ? evs.slice(0, i) : evs;
  if (!silenzioso) suonaEventi(testa);
  for (const e of testa) aggiungiMessaggio(e);
  return i > 0 ? evs.slice(i) : [];
}
function proseguiRinvio() {
  const r = rinvio;
  if (!r) return;
  if (!ultimoStato || ultimoStato.id !== r.id) { rinvio = null; return; }
  r.coda = mostraTesta(r.coda, r.silenzioso);
  if (r.coda.length) { dopoSuoni(() => { if (rinvio === r) proseguiRinvio(); }); return; }
  rinvio = null;
  disegnaStato(r.stato);
}
// Mostra subito quello che restava (arriva una novità che non si può far aspettare).
function chiudiRinvio() {
  const r = rinvio;
  rinvio = null;
  if (!r) return;
  if (!r.silenzioso) suonaEventi(r.coda);
  for (const e of r.coda) aggiungiMessaggio(e);
}
function mostraEventi(evs, silenzioso) {
  if (!silenzioso) suonaEventi(evs);
  for (const e of evs) aggiungiMessaggio(e);
}
function suonaEventi(eventi) {
  if (!suoniAttivi()) return;
  const nomi = eventi.filter((e) => e.suono).map((e) => e.suono);
  if (!nomi.length) return;
  codaSuoni = codaSuoni.concat(nomi).slice(-4);   // se ne arrivano molti insieme (il computer che gioca) suonano solo gli ultimi
  if (!suonoInCorso) suonaProssimo();
}

let ultimoEventoMostrato = { id: null, n: 0 };
function aggiorna(s) {
  const primo = ultimoStato === null || ultimoStato.id !== s.id;   // aprendo una partita non si rifà il suono di tutta la storia
  const miaPrima = !primo && ultimoStato.vista ? ultimoStato.vista.mia : null;
  ultimoStato = s;
  // lo stesso evento può arrivare due volte (dal flusso e dalla risposta alla mia mossa): si mostra una volta sola
  if (ultimoEventoMostrato.id !== s.id) ultimoEventoMostrato = { id: s.id, n: 0 };
  const nuoviEventi = s.eventi.filter((e) => typeof e.n !== 'number' || e.n > ultimoEventoMostrato.n);
  for (const e of s.eventi) if (typeof e.n === 'number' && e.n > ultimoEventoMostrato.n) ultimoEventoMostrato.n = e.n;
  if (rinvio && rinvio.id === s.id) {
    // si stanno ancora mostrando i colpi del computer: lo stesso stato ripetuto (dal flusso) aspetta con loro; una novità vera li mostra subito
    if (!nuoviEventi.length) { rinvio.stato = s; return; }
    chiudiRinvio();
  } else {
    rinvio = null;
  }
  const battagliaNavale = s.gioco === 'battaglia' && s.stato === 'in corso';
  if (battagliaNavale && miaPrima && s.vista && indiceSecondoColpo(nuoviEventi) > 0) {
    // battaglia navale: il computer ha già risposto, ma il suo colpo si vede solo dopo il tuo. Intanto la tua flotta resta com'era e non tocca a te
    const visibile = Object.assign({}, s, { mosse: [], turno: 1 - s.posto, vista: Object.assign({}, s.vista, { mia: miaPrima }) });
    const r = { id: s.id, stato: s, coda: mostraTesta(nuoviEventi, primo), silenzioso: primo };
    rinvio = r;
    disegnaStato(visibile);
    dopoSuoni(() => { if (rinvio === r) proseguiRinvio(); });
    return;
  }
  mostraEventi(nuoviEventi, primo);
  disegnaStato(s);
}

// Titolo, turno, tavolo e mosse della partita (gli eventi li mostra aggiorna).
function disegnaStato(s) {
  $('titolo-partita').textContent = s.nome_gioco + ' (tavolo ' + s.id + ')';
  const turno = $('stato-turno');
  const mio = s.stato === 'in corso' && s.mosse.length > 0;
  const diventatoMio = mio && !eraMioTurno;
  eraMioTurno = mio;
  attesaCorrente = mio ? 3 : 20;
  turno.classList.toggle('mio', mio);
  document.title = s.nome_gioco + (mio ? ', tocca a te' : '') + ' – Giochi';
  const occupati = s.giocatori.filter((g) => g).length;
  if (s.stato === 'attesa') turno.textContent = 'In attesa di altri giocatori: ' + occupati + ' su ' + s.giocatori.length + '. Codice del tavolo: ' + s.id + '.';
  else if (s.stato === 'finita') turno.textContent = 'La partita è finita.';
  else if (mio && s.vista && s.vista.asta) turno.textContent = 'Asta per ' + s.vista.asta.nome + ': tocca a te.';
  else if (mio && s.turno !== s.posto) turno.textContent = 'Ti propongono uno scambio.';
  else if (mio) turno.textContent = 'Tocca a te.';
  else if (s.vista && s.vista.asta) turno.textContent = 'Asta per ' + s.vista.asta.nome + ': tocca a ' + (s.giocatori[s.vista.asta.attore] ? s.giocatori[s.vista.asta.attore].nome : '...') + '.';
  else if (s.vista && s.vista.scambio) turno.textContent = s.vista.scambio.da === s.posto ? 'Aspetti la risposta allo scambio che hai proposto.' : 'È in corso uno scambio tra ' + s.giocatori[s.vista.scambio.da].nome + ' e ' + s.giocatori[s.vista.scambio.a].nome + '.';
  else turno.textContent = 'Tocca a ' + (s.giocatori[s.turno] ? s.giocatori[s.turno].nome : '...') + '.';
  $('avvia-ora').hidden = !(s.stato === 'attesa' && s.posto === 0);
  $('copia-link').hidden = s.stato !== 'attesa';
  $('modulo-invita').hidden = s.stato !== 'attesa';
  disegnaGiocatori(s);
  if (s.vista) {
    if (s.vista.tipo === 'burraco') disegnaBurraco(s);
    else if (s.vista.tipo === 'monopoli') disegnaMonopoli(s);
    else if (s.vista.tipo === 'briscola' || s.vista.tipo === 'scopa') disegnaCarteItaliane(s);
    else if (s.vista.tipo === 'battaglia') disegnaBattaglia(s);
  } else {
    $('tavolo').replaceChildren(el('p', { testo: 'La partita non è ancora cominciata.' }));
  }
  disegnaMosse(s, diventatoMio);
  disegnaOpzioni(s);
}

// ---- giocatori: scrivere in privato (chat cifrata) o bloccare
let firmaGiocatori = '';
function disegnaGiocatori(s) {
  const altri = (s.giocatori || []).map((g, i) => ({ g, i })).filter((x) => x.g && !x.g.computer && x.i !== s.posto);
  const firma = altri.map((x) => x.g.nome).join('|');
  if (firma === firmaGiocatori) return;
  firmaGiocatori = firma;
  const ul = $('giocatori-partita');
  ul.replaceChildren();
  $('giocatori-pannello').hidden = altri.length === 0;
  for (const x of altri) {
    ul.append(el('li', { class: 'riga' }, el('span', { testo: x.g.nome }),
      el('a', { class: 'pulsante secondario', href: urlChat + '#con=' + encodeURIComponent(x.g.nome), testo: 'Scrivi in privato', 'aria-label': 'Scrivi in privato a ' + x.g.nome + ' con la chat cifrata' }),
      el('button', { type: 'button', class: 'secondario', testo: 'Blocca', 'aria-label': 'Blocca ' + x.g.nome, onclick: () => blocca(x.g.nome) })));
  }
}

// ---- mosse
function cartePerMossa(id) {
  const p = id.split('|');
  if (p[0] === 'scarta' || p[0] === 'att' || p[0] === 'gioca') return [Number(p[1])];
  if (p[0] === 'cala') return p[1].split(',').map(Number);
  if (p[0] === 'scarti' && p[1] === 'cala') return p[2].split(',').map(Number);
  return [];
}

function gruppoMossa(s, m) {
  const p = m.id.split('|')[0];
  if (s.gioco === 'burraco') {
    return { pesca: 'Pesca', scarti: 'Prendi gli scarti', cala: 'Cala una combinazione', att: 'Attacca una carta', scarta: 'Scarta', annulla: 'Annulla le calate provvisorie' }[p] || 'Altro';
  }
  if (s.gioco === 'briscola' || s.gioco === 'scopa') return 'Scegli la carta';
  if (['accetta_scambio', 'rifiuta_scambio'].includes(p)) return 'Scambio proposto';
  if (['offri', 'ritirati_asta'].includes(p)) return 'Asta';
  if (p === 'compra' || p === 'passa') return 'Vuoi comprare questa proprietà?';
  return 'Il turno';
}

// Una riga su come stanno le cose adesso (non come si gioca).
function guidaTurno(s) {
  const ids = s.mosse.map((m) => m.id);
  if (s.gioco === 'burraco') {
    const q = s.vista && s.vista.squadre ? s.vista.squadre.find((x) => x.membri.includes(s.posto)) : null;
    if (ids.includes('annulla') && q) return 'Hai calato ' + q.punti_calati + ' punti su ' + q.minimo + ' necessari per aprire.';
    return '';
  }
  if (ids.includes('accetta_scambio')) return 'Ti propongono uno scambio.';
  if (ids.includes('compra')) return 'Sei su una proprietà libera. Se non la compri va all\'asta.';
  return '';
}

function disegnaMosse(s, daFocalizzare) {
  const contenitore = $('mosse');
  contenitore.replaceChildren();
  const filtro = $('filtro-carta');
  filtro.hidden = true;
  if (!s.mosse.length) {
    $('mosse-titolo').textContent = s.stato === 'finita' ? 'Partita finita' : 'Le tue mosse';
    $('guida-turno').textContent = '';
    if (s.stato === 'finita') contenitore.append(el('button', { type: 'button', onclick: sala, testo: 'Torna ai giochi' }));
    else contenitore.append(el('p', { testo: s.stato === 'attesa' ? 'La partita non è ancora cominciata.' : 'Non è il tuo turno.' }));
    return;
  }
  $('mosse-titolo').textContent = 'Tocca a te';
  $('guida-turno').textContent = guidaTurno(s);
  let mosse = s.mosse;
  if ((s.gioco === 'burraco' || s.gioco === 'scopa') && cartaScelta !== null) {
    const filtrate = mosse.filter((m) => cartePerMossa(m.id).includes(cartaScelta));
    if (filtrate.length) {
      mosse = filtrate;
      filtro.hidden = false;
      filtro.replaceChildren('Mosse con la carta scelta. ', el('button', { type: 'button', class: 'secondario', onclick: () => { cartaScelta = null; aggiorna(ultimoStato); }, testo: 'Tutte le mosse' }));
    } else {
      cartaScelta = null;
    }
  }
  let gruppoCorrente = '';
  for (const m of mosse) {
    const g = gruppoMossa(s, m);
    if (g !== gruppoCorrente) {
      gruppoCorrente = g;
      contenitore.append(el('h4', { class: 'gruppo', testo: g }));
    }
    if (m.parametro && m.parametro.tipo === 'numero') {
      const campo = el('input', { type: 'number', min: m.parametro.min, max: m.parametro.max, value: m.parametro.min, 'aria-label': m.parametro.domanda, step: 10 });
      const modulo = el('form', { class: 'scambio', onsubmit: (e) => { e.preventDefault(); inviaMossa(m.id + '|' + campo.value); } },
        el('label', { testo: m.parametro.domanda }), campo, el('button', { type: 'submit', testo: m.testo }));
      contenitore.append(modulo);
      continue;
    }
    if (m.parametro && m.parametro.tipo === 'casella') {
      continue;   // si sceglie sul casellario, non serve un pulsante né una spiegazione
    }
    contenitore.append(el('button', { type: 'button', onclick: () => inviaMossa(m.id), testo: m.testo }));
  }
  if (s.gioco === 'monopoli' && s.vista && mosse.some((m) => m.id === 'compra')) {
    const c = s.vista.caselle[s.vista.giocatori[s.posto].posizione];
    contenitore.insertBefore(infoProprieta(c, s.vista), contenitore.firstChild);
  }
  const primo = contenitore.querySelector('button');
  if (primo && daFocalizzare) primo.focus({ preventScroll: true });
}

// Colore, prezzo, affitti, edifici, ipoteca e gruppo di una proprietà.
function infoProprieta(c, v) {
  const righe = [];
  const banda = c.colore ? el('span', { class: 'banda-info', style: 'background:' + COLORI_GRUPPO[c.colore] }) : null;
  righe.push(el('h4', {}, banda, ' ' + c.nome + (c.colore ? ', colore ' + c.colore : '') + ', prezzo ' + c.prezzo + ' euro'));
  if (c.tipo === 'terreno') {
    const a = c.affitti;
    righe.push(el('p', { testo: 'Affitto: ' + a[0] + ' euro senza case, ' + a[1] + ' con una casa, ' + a[2] + ' con due, ' + a[3] + ' con tre, ' + a[4] + ' con quattro, ' + a[5] + " con l'albergo. Ogni casa o albergo costa " + c.costo_edificio + ' euro.' }));
    righe.push(el('p', { testo: 'Fa parte del gruppo: ' + c.gruppo.join(', ') + '.' }));
  } else if (c.tipo === 'stazione') {
    righe.push(el('p', { testo: 'Affitto: 25 euro con una stazione, 50 con due, 100 con tre, 200 con quattro.' }));
  } else if (c.tipo === 'societa') {
    righe.push(el('p', { testo: 'Affitto: 4 volte il tiro dei dadi con una società, 10 volte con due.' }));
  }
  righe.push(el('p', { testo: 'Valore di ipoteca: ' + c.valore_ipoteca + ' euro.' }));
  return el('div', { class: 'info-proprieta' }, righe);
}

async function inviaMossa(mossa) {
  if (!partitaAttiva) return;
  cartaScelta = null;
  try {
    const s = await api('POST', 'mossa', { id: partitaAttiva, mossa, da: ultimoStato ? ultimoStato.ultimo_evento : 0 });
    aggiorna(s);
  } catch (err) {
    aggiungiMessaggio({ tipo: 'sistema', testo: err.message });
  }
}

// ---- opzioni e scambi
let firmaOpzioni = '';

function disegnaOpzioni(s) {
  const pannello = $('opzioni-pannello');
  const contenitore = $('opzioni');
  const gruppi = s.opzioni || [];
  // se le opzioni non sono cambiate non si ridisegnano: chi compila uno scambio non perde quello che ha scritto
  const firma = JSON.stringify(gruppi);
  if (firma === firmaOpzioni) return;
  firmaOpzioni = firma;
  // chi sta compilando uno scambio non lo perde se arriva un aggiornamento che non lo riguarda
  const aperti = new Set(Array.from(contenitore.querySelectorAll('details[open]')).map((d) => d.dataset.gruppo));
  contenitore.replaceChildren();
  pannello.hidden = gruppi.length === 0;
  for (const g of gruppi) {
    const dettagli = el('details', { 'data-gruppo': g.id });
    if (aperti.has(g.id)) dettagli.open = true;
    dettagli.append(el('summary', { testo: g.testo }));
    if (g.id === 'scambio') {
      dettagli.append(formScambio(g.scambio));
    } else {
      // più proprietà insieme: si spuntano e un solo pulsante le esegue tutte (ipoteca|1,5,9)
      const caselle = g.voci.map((v) => el('label', { class: 'casella-scelta' }, el('input', { type: 'checkbox', value: v.id.split('|')[1] }), ' ' + v.testo));
      const modulo = el('form', { class: 'scambio', onsubmit: (e) => {
        e.preventDefault();
        const scelte = Array.from(modulo.querySelectorAll('input:checked')).map((i) => i.value);
        if (!scelte.length) { aggiungiMessaggio({ tipo: 'sistema', testo: 'Nessuna proprietà scelta.' }); return; }
        inviaMossa(g.id + '|' + scelte.join(','));
      } }, caselle, el('button', { type: 'submit', testo: 'Conferma le proprietà scelte' }));
      dettagli.append(modulo);
    }
    contenitore.append(dettagli);
  }
}

// Il nome di una proprietà con il colore (terreni) oppure "stazione" e "società".
function nomeConColore(p) {
  const c = ultimoStato && ultimoStato.vista && ultimoStato.vista.caselle ? ultimoStato.vista.caselle[p.id] : null;
  if (!c) return p.nome;
  if (c.colore) return p.nome + ', ' + c.colore;
  if (c.tipo === 'stazione') return p.nome + ', stazione';
  if (c.tipo === 'societa') return p.nome + ', società';
  return p.nome;
}

function formScambio(dati) {
  const con = el('select', { id: 'scambio-con' }, dati.giocatori.map((g) => el('option', { value: g.indice, testo: g.nome + ', ' + g.soldi + ' euro' })));
  const mieProp = el('div', {}, dati.mie.length ? dati.mie.map((p) => el('label', { class: 'casella-scelta' }, el('input', { type: 'checkbox', value: p.id }), nomeConColore(p) + ', ' + p.prezzo + ' euro')) : el('p', { testo: 'Non hai proprietà da scambiare.' }));
  const mieSoldi = el('input', { id: 'scambio-dai-soldi', type: 'number', min: 0, max: dati.miei_soldi, value: 0 });
  const sueProp = el('div');
  const sueSoldi = el('input', { id: 'scambio-chiedi-soldi', type: 'number', min: 0, value: 0 });
  const esito = el('p', { class: 'esito', role: 'status' });

  function altro() { return dati.giocatori.find((g) => String(g.indice) === con.value); }
  function aggiornaAltro() {
    const g = altro();
    sueProp.replaceChildren(...(g.proprieta.length ? g.proprieta.map((p) => el('label', { class: 'casella-scelta' }, el('input', { type: 'checkbox', value: p.id }), nomeConColore(p) + ', ' + p.prezzo + ' euro')) : [el('p', { testo: g.nome + ' non ha proprietà da scambiare.' })]));
    sueSoldi.max = g.soldi;
    sueSoldi.value = Math.min(Number(sueSoldi.value), g.soldi);
  }
  con.addEventListener('change', aggiornaAltro);
  aggiornaAltro();

  const scelte = (contenitore) => Array.from(contenitore.querySelectorAll('input[type=checkbox]:checked')).map((c) => c.value);
  const invia = el('button', {
    type: 'button',
    testo: 'Proponi lo scambio',
    onclick: () => {
      const dai = scelte(mieProp);
      const chiedi = scelte(sueProp);
      const daiSoldi = Math.max(0, Math.min(dati.miei_soldi, Math.floor(Number(mieSoldi.value) || 0)));
      const chiediSoldi = Math.max(0, Math.floor(Number(sueSoldi.value) || 0));
      if (!dai.length && !chiedi.length && !daiSoldi && !chiediSoldi) { esito.textContent = 'Lo scambio è vuoto: non dai e non chiedi niente.'; return; }
      esito.textContent = '';
      inviaMossa(['scambio', con.value, dai.length ? dai.join(',') : '-', daiSoldi, chiedi.length ? chiedi.join(',') : '-', chiediSoldi].join('|'));
    },
  });
  return el('div', { class: 'scambio' },
    el('div', {}, el('label', { for: 'scambio-con', testo: 'Con chi' }), con),
    el('fieldset', {}, el('legend', { testo: 'Cosa dai' }), mieProp, el('label', { for: 'scambio-dai-soldi', testo: 'Soldi che dai' }), mieSoldi),
    el('fieldset', {}, el('legend', { testo: 'Cosa chiedi' }), sueProp, el('label', { for: 'scambio-chiedi-soldi', testo: 'Soldi che chiedi' }), sueSoldi),
    invia, esito);
}

// ---- burraco
// Le carte sono immagini (carte/7H.png...): una per ogni carta, disegnate da scripts\genera-carte.ps1.
const RANGHI_IMMAGINE = { 1: 'A', 11: 'J', 12: 'Q', 13: 'K' };
const SEMI_IMMAGINE = ['H', 'D', 'C', 'S'];
function immagineCarta(c) {
  if (c.immagine) return c.immagine;
  if (c.jolly) return 'JK';
  return (RANGHI_IMMAGINE[c.rango] || String(c.rango)) + SEMI_IMMAGINE[c.seme];
}

function elementoCarta(c, opzioni) {
  opzioni = opzioni || {};
  const classi = ['carta'];
  if (opzioni.piccola) classi.push('piccola');
  if (opzioni.scelta) classi.push('scelta');
  const attributi = { class: classi.join(' '), 'aria-label': c.nome };
  const img = el('img', { src: 'carte/' + immagineCarta(c) + '.png', alt: '', width: 200, height: 280, draggable: 'false' });
  let carta;
  if (opzioni.clic) {
    // il pulsante ha già il nome della carta: l'immagine non lo ripete
    carta = el('button', Object.assign(attributi, { type: 'button', 'aria-pressed': opzioni.scelta ? 'true' : 'false', onclick: opzioni.clic }), img);
  } else {
    carta = el('div', Object.assign(attributi, { role: 'img' }), img);
  }
  // nei giochi con il mazzo italiano i semi delle immagini sono quelli francesi: il nome sotto la carta dice il seme vero
  if (opzioni.didascalia) return el('div', { class: 'con-didascalia' }, carta, el('span', { class: 'didascalia', 'aria-hidden': 'true', testo: opzioni.didascalia }));
  return carta;
}

function dorso(piccola) {
  return el('div', { class: 'carta dorso' + (piccola ? ' piccola' : ''), 'aria-hidden': 'true' }, el('img', { src: 'carte/dorso.png', alt: '', width: 200, height: 280, draggable: 'false' }));
}

function disegnaBurraco(s) {
  const v = s.vista;
  const tavolo = $('tavolo');
  const radice = el('div', { class: 'felpato' });

  const avversari = el('div', { class: 'avversari' });
  v.nomi.forEach((n, i) => {
    if (i === s.posto) return;
    const mani = el('span', { class: 'carte' });
    for (let k = 0; k < Math.min(v.carte_in_mano[i], 14); k++) mani.append(dorso(true));
    avversari.append(el('div', { class: 'avversario' }, el('span', { testo: n + ' (' + v.carte_in_mano[i] + ' carte)' }), mani));
  });
  radice.append(avversari);

  const centro = el('div', { class: 'centro' });
  centro.append(el('div', { class: 'mazzo' }, dorso(false), el('div', { testo: 'Mazzo: ' + v.mazzo })));
  centro.append(el('div', { class: 'mazzo' },
    v.scarti.cima ? elementoCarta(v.scarti.cima, {}) : el('div', { class: 'carta piccola', 'aria-hidden': 'true' }),
    el('div', { testo: 'Scarti: ' + v.scarti.n })));
  centro.append(el('div', { testo: 'Pozzetti non ancora presi: ' + v.pozzetti_rimasti }));
  if (v.scarti.congelato) centro.append(el('div', { class: 'distintivo', testo: 'pozzo congelato' }));
  radice.append(centro);
  if (v.partita) {
    const tot = v.squadre.map((q, i) => q.membri.map((m) => v.nomi[m]).join(' e ') + ' ' + v.partita.totali[i]).join(' · ');
    radice.insertBefore(el('p', { class: 'distintivo-partita', testo: 'Mano ' + v.partita.mano + ', si gioca fino a ' + v.partita.obiettivo + ' punti. Totali: ' + tot }), radice.firstChild);
  }

  const miaSquadra = v.squadre.findIndex((q) => q.membri.includes(s.posto));
  v.squadre.forEach((q, i) => {
    const nomiSquadra = q.membri.map((m) => v.nomi[m]).join(' e ');
    const blocco = el('div', { class: 'squadra' },
      el('h4', { testo: (i === miaSquadra ? 'La tua squadra' : 'La squadra avversaria') + ': ' + nomiSquadra + (q.pozzetto_preso ? ', pozzetto preso' : ', pozzetto da prendere') + (q.aperta ? ', ha aperto' : ', non ha aperto: servono ' + q.minimo + ' punti (calati ' + q.punti_calati + ')') }));
    if (!q.combinazioni.length) blocco.append(el('p', { testo: 'Nessuna combinazione calata.' }));
    for (const c of q.combinazioni) {
      blocco.append(el('div', { class: 'combinazione' },
        el('span', { class: 'etichetta', testo: (c.indice + 1) + '. ' + c.descrizione }),
        c.burraco ? el('span', { class: 'distintivo', testo: 'burraco ' + c.burraco }) : null,
        el('span', { class: 'carte' }, c.carte.map((k) => elementoCarta(k, { piccola: true })))));
    }
    radice.append(blocco);
  });

  if (s.posto >= 0) {
    radice.append(el('h3', { testo: 'La tua mano (' + v.mano.length + ' carte)' }));
    const mano = el('div', { class: 'carte' });
    for (const c of v.mano) {
      mano.append(elementoCarta(c, { clic: () => { cartaScelta = cartaScelta === c.id ? null : c.id; aggiorna(ultimoStato); }, scelta: cartaScelta === c.id }));
    }
    radice.append(mano);
  }

  if (v.punteggi && (v.finita || v.partita.mano > 1)) {
    const tab = el('table', {}, el('thead', {}, el('tr', {}, ['Squadra', 'Tavolo', 'Chiusura', 'Pozzetto', 'In mano', 'Totale'].map((t) => el('th', { testo: t })))));
    const corpo = el('tbody');
    v.punteggi.forEach((p, i) => {
      corpo.append(el('tr', {}, [v.squadre[i].membri.map((m) => v.nomi[m]).join(' e '), p.tavolo, p.chiusura, p.pozzetto, p.in_mano, p.totale].map((t, k) => el(k === 0 ? 'th' : 'td', { testo: String(t) }))));
    });
    tab.append(corpo);
    radice.append(el('div', { class: 'punteggi' }, el('h3', { testo: v.finita ? "Punti dell'ultima mano" : 'Punti della mano precedente' }), tab));
  }
  tavolo.replaceChildren(radice);
}

// ---- briscola e scopa (mazzo italiano da 40 carte)
function disegnaCarteItaliane(s) {
  const v = s.vista;
  const radice = el('div', { class: 'felpato' });
  const nomiSquadra = (q) => q.membri.map((m) => v.nomi[m]).join(' e ');

  const distintivo = (s.variante_nome && s.variante !== 'classica' ? [s.variante_nome] : []).concat(v.riepilogo);
  radice.append(el('p', { class: 'distintivo-partita', testo: distintivo.join(' · ') }));

  const avversari = el('div', { class: 'avversari' });
  v.nomi.forEach((n, i) => {
    if (i === s.posto) return;
    const mani = el('span', { class: 'carte' });
    if (v.mani_scoperte && v.mani_scoperte[i]) {
      for (const c of v.mani_scoperte[i]) mani.append(elementoCarta(c, { piccola: true, didascalia: c.breve }));
    } else {
      for (let k = 0; k < v.carte_in_mano[i]; k++) mani.append(dorso(true));
    }
    avversari.append(el('div', { class: 'avversario' }, el('span', { testo: n + ' (' + v.carte_in_mano[i] + ' carte)' }), mani));
  });
  radice.append(avversari);

  const centro = el('div', { class: 'centro' });
  centro.append(el('div', { class: 'mazzo' }, dorso(false), el('div', { testo: 'Mazzo: ' + v.mazzo })));
  if (v.tipo === 'briscola') {
    if (v.briscola) {
      centro.append(el('div', { class: 'mazzo' }, elementoCarta(v.briscola, { didascalia: v.briscola.breve }), el('div', { testo: 'Briscola: ' + v.seme_briscola })));
    } else if (v.seme_briscola) {
      centro.append(el('div', { class: 'mazzo' }, el('div', { testo: 'Briscola: ' + v.seme_briscola })));
    }
    if (v.chiamata) {
      centro.append(el('div', { class: 'info-chiamata', testo: 'Chiamata: ' + v.chiamata.carta + (v.chiamata.socio !== null ? ' (Socio: ' + v.nomi[v.chiamata.socio] + ')' : '') }));
    }
    if (v.accusi && v.accusi.length) {
      centro.append(el('div', { class: 'info-accusi', testo: 'Accusi: ' + v.accusi.map((a) => v.nomi[a.giocatore] + ' (+' + a.punti + ' pt)').join(', ') }));
    }
  }
  radice.append(centro);

  radice.append(el('h3', { testo: v.tipo === 'scopa' ? 'Il tavolo (' + v.tavolo_carte.length + ' carte)' : 'Sul tavolo' }));
  const tavolo = el('div', { class: 'carte' });
  if (!v.tavolo_carte.length) tavolo.append(el('p', { testo: v.tipo === 'scopa' ? 'Il tavolo è vuoto.' : 'Nessuna carta giocata in questa mano.' }));
  for (const c of v.tavolo_carte) tavolo.append(elementoCarta(c, { didascalia: v.tipo === 'briscola' ? c.da + ': ' + c.breve : c.breve }));
  radice.append(tavolo);
  if (v.tipo === 'scopa' && v.ultima) radice.append(el('p', { testo: 'Ultima giocata: ' + v.ultima }));
  if (v.tipo === 'briscola' && v.ultima_presa) {
    radice.append(el('p', { testo: 'Ultima presa: ' + v.nomi[v.ultima_presa.vincitore] + ' con ' + v.ultima_presa.punti + ' punti.' }));
  }

  if (s.posto >= 0) {
    radice.append(el('h3', { testo: 'La tua mano (' + v.mano.length + ' carte)' }));
    const mano = el('div', { class: 'carte' });
    const mioTurno = s.mosse.length > 0;
    for (const c of v.mano) {
      const opzioni = { didascalia: c.breve };
      if (mioTurno) {
        opzioni.scelta = cartaScelta === c.id;
        opzioni.clic = () => {
          const sue = s.mosse.filter((m) => cartePerMossa(m.id).includes(c.id));
          if (sue.length === 1) { inviaMossa(sue[0].id); return; }
          cartaScelta = cartaScelta === c.id ? null : c.id;
          aggiorna(ultimoStato);
        };
      }
      mano.append(elementoCarta(c, opzioni));
    }
    radice.append(mano);
  }

  if (v.punteggi && (v.finita || v.tipo === 'scopa')) {
    let intestazioni;
    let righe;
    if (v.tipo === 'scopa') {
      const haNapola = v.punteggi.some((p) => (p.napola ?? 0) > 0);
      const haRebello = v.punteggi.some((p) => (p.rebello ?? 0) > 0);
      intestazioni = ['Squadra', 'Carte', 'Denari', 'Sette bello', 'Primiera', 'Scope'];
      if (haNapola) intestazioni.push('Napola');
      if (haRebello) intestazioni.push('Re bello');
      intestazioni.push('Totale');
      righe = v.punteggi.map((p, i) => {
        const r = [nomiSquadra(v.squadre[i]), p.carte, p.denari, p.sette_bello, p.primiera_punto, p.scope];
        if (haNapola) r.push(p.napola ?? 0);
        if (haRebello) r.push(p.rebello ?? 0);
        r.push(p.totale);
        return r;
      });
    } else {
      intestazioni = ['Squadra', 'Carte prese', 'Punti'];
      righe = v.punteggi.map((p, i) => [nomiSquadra(v.squadre[i]), p.carte, p.punti]);
    }
    const tab = el('table', {}, el('thead', {}, el('tr', {}, intestazioni.map((t) => el('th', { testo: t })))));
    const corpo = el('tbody');
    for (const r of righe) corpo.append(el('tr', {}, r.map((t, k) => el(k === 0 ? 'th' : 'td', { testo: String(t) }))));
    tab.append(corpo);
    const titolo = v.finita ? 'Punti ' + (v.tipo === 'scopa' ? "dell'ultima mano" : 'della partita') : 'Punti della mano precedente';
    radice.append(el('div', { class: 'punteggi' }, el('h3', { testo: titolo }), tab, v.finita ? el('p', { testo: v.motivo }) : null));
  } else if (v.finita && v.motivo) {
    radice.append(el('p', { testo: v.motivo }));
  }
  $('tavolo').replaceChildren(radice);
}

// ---- monopoli
// Dadi, case, albergo, pedine e icone delle caselle sono immagini (monopoli/*.png), disegnate da scripts/genera-monopoli.py.
function immagineMonopoli(nomeImmagine, attributi) {
  return el('img', Object.assign({ src: 'monopoli/' + nomeImmagine + '.png', alt: '', draggable: 'false' }, attributi || {}));
}

function iconaCasella(c) {
  if (c.tipo === 'societa') return /elettric/i.test(c.nome) ? 'societa-elettrica' : 'societa-acqua';
  return ['via', 'prigione', 'parcheggio', 'vai_prigione', 'stazione', 'probabilita', 'imprevisti', 'tassa'].includes(c.tipo) ? c.tipo : '';
}

function posizioneCasella(i) {
  if (i <= 10) return [11, 11 - i];
  if (i <= 20) return [11 - (i - 10), 1];
  if (i <= 30) return [1, 1 + (i - 20)];
  return [1 + (i - 30), 11];
}

function disegnaMonopoli(s) {
  const v = s.vista;
  const tab = el('div', { class: 'tabellone', role: 'group', 'aria-label': 'Tabellone del monopoli' });
  for (const c of v.caselle) {
    const [riga, colonna] = posizioneCasella(c.indice);
    const angolo = ['via', 'prigione', 'parcheggio', 'vai_prigione'].includes(c.tipo);
    const casella = el('div', { class: 'casella' + (angolo ? ' angolo' : '') + (c.ipoteca ? ' ipotecata' : ''), style: 'grid-row:' + riga + ';grid-column:' + colonna, title: c.nome + (c.prezzo ? ', ' + c.prezzo + ' euro' : '') });
    if (c.colore) casella.append(el('div', { class: 'banda', style: 'background:' + COLORI_GRUPPO[c.colore] }));
    const icona = iconaCasella(c);
    if (icona) casella.append(immagineMonopoli('icona-' + icona, { class: 'icona', width: 96, height: 96 }));
    casella.append(el('div', { class: 'nome', testo: c.nome }));
    if (c.prezzo) casella.append(el('div', { class: 'prezzo', testo: String(c.prezzo) }));
    if (c.proprietario !== undefined && c.proprietario !== null) {
      casella.append(el('span', { class: 'proprietario', style: 'background:' + COLORI_GIOCATORI[c.proprietario], title: 'Di ' + v.giocatori[c.proprietario].nome }));
    }
    if (c.case > 0) {
      const case_ = el('span', { class: 'case' });
      if (c.case === 5) case_.append(immagineMonopoli('albergo', { class: 'albergo', width: 64, height: 64 }));
      else for (let k = 0; k < c.case; k++) case_.append(immagineMonopoli('casa', { class: 'casa', width: 64, height: 64 }));
      casella.append(case_);
    }
    const qui = v.giocatori.map((g, i) => ({ g, i })).filter((x) => !x.g.fuori && x.g.posizione === c.indice);
    if (qui.length) {
      const pedine = el('div', { class: 'pedine' });
      for (const x of qui) pedine.append(el('span', { class: 'pedina', title: x.g.nome, role: 'img', 'aria-label': x.g.nome }, immagineMonopoli('pedina-' + (x.i + 1), { width: 96, height: 96 })));
      casella.append(pedine);
    }
    tab.append(casella);
  }

  const centro = el('div', { class: 'centro-tabellone' });
  const dado = (n) => (n >= 1 && n <= 6 ? el('div', { class: 'dado con-immagine' }, immagineMonopoli('dado-' + n, { width: 128, height: 128 })) : el('div', { class: 'dado', testo: '·' }));
  centro.append(el('div', { class: 'dadi', role: 'img', 'aria-label': 'Dadi: ' + v.dadi[0] + ' e ' + v.dadi[1] }, dado(v.dadi[0]), dado(v.dadi[1])));
  const elenco = el('div', { class: 'giocatori-mon' });
  v.giocatori.forEach((g, i) => {
    elenco.append(el('div', { class: 'giocatore-mon' + (i === (v.attore !== undefined ? v.attore : v.turno) && !v.finita ? ' attuale' : '') + (g.fuori ? ' eliminato' : '') },
      el('span', { class: 'gettone', 'aria-hidden': 'true' }, immagineMonopoli('pedina-' + (i + 1), { width: 96, height: 96 })),
      el('span', { testo: g.nome + (i === s.posto ? ' (tu)' : '') + ': ' + g.soldi + ' euro' + (g.prigione ? ', in prigione' : '') + (g.carte_esci ? ', carta uscita' : '') })));
  });
  centro.append(elenco);
  const infoGiro = ['Giro ' + v.giri + ' di ' + v.giri_massimi];
  if (v.variante && v.variante !== 'classica') infoGiro.push('Variante: ' + (v.variante_nome || v.variante));
  if (v.parcheggio_piatto > 0) infoGiro.push('Piatto Parcheggio: ' + v.parcheggio_piatto + ' euro');
  centro.append(el('div', { testo: infoGiro.join(' · ') }));
  if (v.asta) {
    centro.append(el('div', { class: 'asta', role: 'status', testo: 'Asta per ' + v.asta.nome + ': ' + (v.asta.offerta ? 'offerta più alta ' + v.asta.offerta + ' euro di ' + v.giocatori[v.asta.leader].nome : 'nessuna offerta') + '. Tocca a ' + v.giocatori[v.asta.attore].nome + '.' }));
  }
  tab.append(centro);

  const radice = el('div', {}, tab);
  if (v.finita && v.punteggi) {
    radice.append(el('div', { class: 'pannello' }, el('h3', { testo: 'Patrimonio finale' }),
      el('ul', {}, v.giocatori.map((g, i) => el('li', { testo: g.nome + ': ' + v.punteggi[i] + ' euro' + (v.vincitore === i ? ' (vincitore)' : '') })))));
  }
  $('tavolo').replaceChildren(radice);
}

// ------------------------------------------------------------------ comandi della partita
$('rileggi').addEventListener('click', async () => {
  if (!partitaAttiva) return;
  try {
    const s = await api('GET', 'stato', null, { id: partitaAttiva, da: ultimoStato ? ultimoStato.ultimo_evento : 0, situazione: 1 });
    $('situazione-pannello').hidden = false;
    $('situazione').textContent = s.situazione || 'La partita non è ancora cominciata.';
  } catch (err) {
    $('situazione-pannello').hidden = false;
    $('situazione').textContent = err.message;
  }
});

$('avvia-ora').addEventListener('click', async () => {
  try { await api('POST', 'avvia', { id: partitaAttiva }); } catch (err) { aggiungiMessaggio({ tipo: 'sistema', testo: err.message }); }
});

$('copia-link').addEventListener('click', async () => {
  const id = partitaAttiva;
  if (!id) return;
  const link = (ultimoStato && ultimoStato.link) || linkPartita(id);
  try {
    await navigator.clipboard.writeText(link);
    aggiungiMessaggio({ tipo: 'sistema', testo: 'Link copiato: ' + link });
  } catch (e) {
    aggiungiMessaggio({ tipo: 'sistema', testo: 'Copia questo link: ' + link });
  }
});

$('abbandona').addEventListener('click', async () => {
  const id = partitaAttiva;
  if (!id) return;
  if (!confirm('Vuoi interrompere la partita?')) return;
  partitaAttiva = null;
  try { await api('POST', 'lascia', { id }); } catch (e) { /* già sparita */ }
  sala();
});

$('modulo-chat').addEventListener('submit', async (e) => {
  e.preventDefault();
  const campo = $('testo-chat');
  const testo = campo.value.trim();
  if (!testo || !partitaAttiva) return;
  try {
    await api('POST', 'chat', { id: partitaAttiva, testo });
    campo.value = '';
  } catch (err) {
    aggiungiMessaggio({ tipo: 'sistema', testo: err.message });
  }
});

// ------------------------------------------------------------------ avvio
// Chiudendo o ricaricando la pagina la partita non si lascia: si riprende da dove era (la sala la ritrova).
// Se chi deve giocare resta assente per 4 minuti, il server fa giocare il computer al suo posto.

async function avvio() {
  if (token) { dopoAccesso(); return; }
  const salvato = await leggiPassword();
  if (salvato && salvato.password) {
    try {
      const r = await entraCon(salvato.utente, salvato.password, false);
      ricordaAccesso(r);
      await salvaPassword(r.nome, salvato.password);
      dopoAccesso();
      return;
    } catch (err) {
      if (err.codice === 401) await dimenticaPassword();
      schermataAccesso();
      $('campo-nome').value = salvato.utente;
      $('errore-accesso').textContent = err.message;
      return;
    }
  }
  schermataAccesso();
}
avvio();

// PWA: il sito si può installare e i file statici restano disponibili anche con la rete debole. Le partite passano sempre dal server.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => { navigator.serviceWorker.register('sw.js', { scope: './' }).catch(() => { /* senza service worker il sito funziona lo stesso */ }); });
}
