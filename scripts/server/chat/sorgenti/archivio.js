// Archivio della chat nel browser: tutto sta in IndexedDB e sempre cifrato con AES-256-GCM. La chiave non si sceglie: viene dalla
// stessa password del nome utente dei giochi (la metà "locale" del derivato, che il server non vede mai). Ogni nome utente ha il
// suo archivio. Niente esce dal browser, tranne i blocchi cifrati end to end che vanno al relè. La chiave resta in memoria solo
// finché la chat è aperta e non si può esportare.
const te = new TextEncoder();
const td = new TextDecoder();

function nomeDb(nome) { return 'wgchat-' + nome.toLowerCase().replace(/[^a-z0-9]/g, '_').slice(0, 24) + '-' + nome.length; }

function db(nome) {
  return new Promise((ok, no) => {
    const r = indexedDB.open(nomeDb(nome), 1);
    r.onupgradeneeded = () => { r.result.createObjectStore('dati'); };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
function richiesta(r) { return new Promise((ok, no) => { r.onsuccess = () => ok(r.result); r.onerror = () => no(r.error); }); }
async function conStore(nome, modo, f) {
  const d = await db(nome);
  try {
    const t = d.transaction('dati', modo);
    const risultato = await f(t.objectStore('dati'));
    await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); t.onabort = () => no(t.error); });
    return risultato;
  } finally { d.close(); }
}

/** Il derivato della password: auth (va al server) e master (resta qui). Stessa regola del programma di WinGabriel. */
export async function deriva(nome, password) {
  const base = await crypto.subtle.importKey('raw', te.encode(password), 'PBKDF2', false, ['deriveBits']);
  const bit = new Uint8Array(await crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt: te.encode('WGAccount-v1|' + nome.toLowerCase()), iterations: 600000 }, base, 512));
  const auth = Array.from(bit.slice(0, 32), (x) => x.toString(16).padStart(2, '0')).join('');
  return { auth, master: bit.slice(32) };
}

async function cifra(chiave, nome, testo) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const c = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: te.encode(nome) }, chiave, te.encode(testo)));
  const r = new Uint8Array(12 + c.length);
  r.set(iv); r.set(c, 12);
  return r;
}
async function decifra(chiave, nome, dati) {
  const d = new Uint8Array(dati);
  const p = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: d.slice(0, 12), additionalData: te.encode(nome) }, chiave, d.slice(12));
  return td.decode(p);
}

export async function esiste(utente) {
  try { return !!(await conStore(utente, 'readonly', (s) => richiesta(s.get('stato')))); } catch (e) { return false; }
}

class Archivio {
  constructor(utente, chiave, stato, messaggi) { this.utente = utente; this.chiave = chiave; this.stato = stato; this.messaggi = messaggi; }
  async salvaStato(stato) {
    const c = await cifra(this.chiave, 'stato', JSON.stringify(stato));
    await conStore(this.utente, 'readwrite', (s) => richiesta(s.put(c, 'stato')));
  }
  async salvaMessaggi(cid, elenco) {
    if (elenco === null) { await conStore(this.utente, 'readwrite', (s) => richiesta(s.delete('m:' + cid))); return; }
    const c = await cifra(this.chiave, 'm:' + cid, JSON.stringify(elenco));
    await conStore(this.utente, 'readwrite', (s) => richiesta(s.put(c, 'm:' + cid)));
  }
}

// extractable = false: la chiave si può usare ma nessuno script, nemmeno uno malevolo, può leggerne i byte e portarli via.
function chiaveDa(master) { return crypto.subtle.importKey('raw', master, 'AES-GCM', false, ['encrypt', 'decrypt']); }
export function chiaveDaMaster(master) { return chiaveDa(master); }

/** Crea l'archivio di un nome utente. */
export async function crea(utente, master, stato) {
  const a = new Archivio(utente, master instanceof CryptoKey ? master : await chiaveDa(master), stato, {});
  await a.salvaStato(stato);
  return a;
}

/** Apre l'archivio. Restituisce null se la chiave non è quella giusta (per esempio password cambiata). */
export async function apri(utente, master) {
  const chiave = master instanceof CryptoKey ? master : await chiaveDa(master);
  let stato;
  try {
    const c = await conStore(utente, 'readonly', (s) => richiesta(s.get('stato')));
    stato = JSON.parse(await decifra(chiave, 'stato', c));
  } catch (e) { return null; }
  const messaggi = {};
  const chiavi = await conStore(utente, 'readonly', (s) => richiesta(s.getAllKeys()));
  for (const k of chiavi) {
    if (typeof k !== 'string' || !k.startsWith('m:')) continue;
    try {
      const c = await conStore(utente, 'readonly', (s) => richiesta(s.get(k)));
      messaggi[k.slice(2)] = JSON.parse(await decifra(chiave, k, c));
    } catch (e) { /* conversazione rovinata: si salta */ }
  }
  return new Archivio(utente, chiave, stato, messaggi);
}

/** Cancella tutto l'archivio di questo nome utente in questo browser (non quello degli altri dispositivi). */
export async function cancellaTutto(utente) {
  await new Promise((ok, no) => { const r = indexedDB.deleteDatabase(nomeDb(utente)); r.onsuccess = ok; r.onerror = () => no(r.error); r.onblocked = ok; });
}

// ------------------------------------------------------------------ password dell'utente, solo cifrata
// Si salva la password con AES-256-GCM. La chiave non si può esportare e resta in questo browser.
// All'accesso si decifra e si entra con quell'utente: non si entra mai senza password.
const DB_RICORDO = 'wgchat-ricordo';
function dbRicordo() {
  return new Promise((ok, no) => {
    const r = indexedDB.open(DB_RICORDO, 2);
    r.onupgradeneeded = () => { if (!r.result.objectStoreNames.contains('r')) r.result.createObjectStore('r'); };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
async function chiaveRicordo(d) {
  let k = await richiesta(d.transaction('r', 'readonly').objectStore('r').get('chiave'));
  if (k) return k;
  k = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  const t = d.transaction('r', 'readwrite');
  t.objectStore('r').put(k, 'chiave');
  await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); });
  return k;
}
export async function ricorda(utente, password) {
  const d = await dbRicordo();
  try {
    const chiave = await chiaveRicordo(d);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const c = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, chiave, te.encode(password)));
    const t = d.transaction('r', 'readwrite');
    t.objectStore('r').put({ utente, iv, c }, 'ricordo');
    await new Promise((ok, no) => { t.oncomplete = ok; t.onerror = () => no(t.error); });
  } finally { d.close(); }
}
/** L'utente e la password, decifrati. Null se non c'è niente di cifrato (i ricordi vecchi, senza password, non si usano). */
export async function ricordo() {
  try {
    const d = await dbRicordo();
    try {
      const salvato = await richiesta(d.transaction('r', 'readonly').objectStore('r').get('ricordo'));
      if (!salvato || !salvato.utente || !salvato.c) return null;
      const chiave = await richiesta(d.transaction('r', 'readonly').objectStore('r').get('chiave'));
      if (!chiave) return null;
      const chiaro = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: salvato.iv }, chiave, salvato.c);
      return { utente: salvato.utente, password: td.decode(chiaro) };
    } finally { d.close(); }
  } catch (e) { return null; }
}
export async function dimentica() {
  await new Promise((ok) => { const r = indexedDB.deleteDatabase(DB_RICORDO); r.onsuccess = ok; r.onerror = ok; r.onblocked = ok; });
}
