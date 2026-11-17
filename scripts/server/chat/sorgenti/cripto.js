// WGChat v1 - cifratura end to end per la chat di WinGabriel (browser e Node).
//
// In breve:
//   - un invito (codice breve) contiene la chiave pubblica d'identità del destinatario e la sua "cassetta" sul relè;
//   - stretta di mano: Noise IK (X25519) con in più ML-KEM-768 (ibrido post-quantistico), poi Double Ratchet con
//     cifratura delle intestazioni (come nella specifica di Signal), ChaCha20-Poly1305 e SHA-256;
//   - il server inoltra soltanto blocchi che sembrano casuali: non vede mittente, contenuto, lunghezza esatta né
//     numero dei messaggi.
// Le primitive vengono dalle librerie "noble" (sottoposte ad audit): qui non c'è nessun algoritmo scritto a mano.
import { x25519 } from '@noble/curves/ed25519';
import { chacha20poly1305 } from '@noble/ciphers/chacha';
import { sha256 } from '@noble/hashes/sha2';
import { hkdf } from '@noble/hashes/hkdf';
import { hmac } from '@noble/hashes/hmac';
import { ml_kem768 } from '@noble/post-quantum/ml-kem';

const te = new TextEncoder();
const td = new TextDecoder();
export const MAX_SKIP = 1000;
export const MAX_CHIAVI_SALTATE = 2000;
export const MASSIMO_TESTO = 4000;           // caratteri
const BLOCCHI = [256, 512, 1024, 2048, 4096, 8192];

// ------------------------------------------------------------------ utilità
export function casuali(n) {
  const b = new Uint8Array(n);
  globalThis.crypto.getRandomValues(b);
  return b;
}
export function concat(...parti) {
  let tot = 0;
  for (const p of parti) tot += p.length;
  const r = new Uint8Array(tot);
  let o = 0;
  for (const p of parti) { r.set(p, o); o += p.length; }
  return r;
}
export function inHex(b) { return Array.from(b, (x) => x.toString(16).padStart(2, '0')).join(''); }
export function daHex(s) {
  if (typeof s !== 'string' || s.length % 2 || /[^0-9a-f]/i.test(s)) throw new Error('esadecimale non valido');
  const r = new Uint8Array(s.length / 2);
  for (let i = 0; i < r.length; i++) r[i] = parseInt(s.substr(i * 2, 2), 16);
  return r;
}
export function inB64(b) {
  let s = '';
  for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode.apply(null, b.subarray(i, i + 0x8000));
  return btoa(s);
}
export function daB64(s) {
  const bin = atob(s);
  const r = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) r[i] = bin.charCodeAt(i);
  return r;
}
export function uguali(a, b) {
  if (a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a[i] ^ b[i];
  return d === 0;
}
function u32(n) { const b = new Uint8Array(4); new DataView(b.buffer).setUint32(0, n >>> 0, true); return b; }
function leggiU32(b, o) { return new DataView(b.buffer, b.byteOffset + o, 4).getUint32(0, true); }
function kdf(salt, ikm, info, len) { return hkdf(sha256, ikm, salt, typeof info === 'string' ? te.encode(info) : info, len); }
const ZERI32 = new Uint8Array(32);

// ------------------------------------------------------------------ primitive con controlli
function dh(priv, pub) {
  const r = x25519.getSharedSecret(priv, pub);
  let tutto0 = 0;
  for (const x of r) tutto0 |= x;
  if (tutto0 === 0) throw new Error('chiave pubblica non valida');
  return r;
}
function nuovaCoppia() {
  const priv = x25519.utils.randomPrivateKey();
  return { priv, pub: x25519.getPublicKey(priv) };
}
function aeadCifra(chiave, nonce, ad, testo) { return chacha20poly1305(chiave, nonce, ad).encrypt(testo); }
function aeadDecifra(chiave, nonce, ad, cifrato) { return chacha20poly1305(chiave, nonce, ad).decrypt(cifrato); }

// ------------------------------------------------------------------ identità e inviti
/** Identità di un dispositivo: la chiave d'identità non lascia mai il dispositivo. */
export function creaIdentita() {
  return nuovaCoppia();
}

const ALFABETO = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
function inBase32(b) {
  let bits = 0, val = 0, out = '';
  for (const x of b) {
    val = (val << 8) | x; bits += 8;
    while (bits >= 5) { out += ALFABETO[(val >>> (bits - 5)) & 31]; bits -= 5; }
  }
  if (bits > 0) out += ALFABETO[(val << (5 - bits)) & 31];
  return out;
}
function daBase32(s) {
  let bits = 0, val = 0;
  const out = [];
  for (const c of s) {
    const i = ALFABETO.indexOf(c);
    if (i < 0) throw new Error('carattere non valido nel codice');
    val = (val << 5) | i; bits += 5;
    if (bits >= 8) { out.push((val >>> (bits - 8)) & 255); bits -= 8; }
  }
  return Uint8Array.from(out);
}

/** Un invito: il codice da dare a chi ti deve scrivere, e i segreti della cassetta (da tenere). */
export function creaInvito(identita) {
  const mb = casuali(16), rd = casuali(32), tk = casuali(32);
  const corpo = concat(identita.pub, mb, tk);
  const controllo = sha256(concat(te.encode('WGC1'), corpo)).slice(0, 4);
  const testo = inBase32(concat(corpo, controllo));
  const gruppi = testo.match(/.{1,6}/g).join('-');
  return { codice: 'WGC1-' + gruppi, mb: inHex(mb), rd: inHex(rd), tk: inHex(tk) };
}

/** Legge un codice scritto da una persona: ignora maiuscole, trattini e spazi. */
export function leggiCodice(testo) {
  let s = String(testo).toUpperCase().replace(/[\s-]/g, '');
  if (!s.startsWith('WGC1')) throw new Error('Il codice non è valido: deve cominciare con WGC1.');
  s = s.slice(4);
  let b;
  try { b = daBase32(s); } catch (e) { throw new Error('Il codice contiene caratteri non validi.'); }
  if (b.length < 84) throw new Error('Il codice è incompleto.');
  b = b.slice(0, 84);
  const corpo = b.slice(0, 80);
  const controllo = sha256(concat(te.encode('WGC1'), corpo)).slice(0, 4);
  if (!uguali(controllo, b.slice(80, 84))) throw new Error('Il codice non è corretto: controlla di averlo scritto giusto.');
  return { ikPub: corpo.slice(0, 32), mb: inHex(corpo.slice(32, 48)), tk: inHex(corpo.slice(48, 80)) };
}

/** Numero di sicurezza (60 cifre) da confrontare a voce o di persona: uguale per i due interlocutori. */
export function numeroDiSicurezza(ikA, ikB) {
  const [a, b] = inHex(ikA) < inHex(ikB) ? [ikA, ikB] : [ikB, ikA];
  const d = kdf(ZERI32, concat(a, b), 'WGChat-sicurezza', 48);
  let cifre = '';
  for (let i = 0; i < 12; i++) {
    const n = (d[i * 4] << 24 | d[i * 4 + 1] << 16 | d[i * 4 + 2] << 8 | d[i * 4 + 3]) >>> 0;
    cifre += String(n % 100000).padStart(5, '0');
  }
  return cifre.match(/.{5}/g).join(' ');
}

// ------------------------------------------------------------------ stato simmetrico (stile Noise)
class Simm {
  constructor() {
    const nome = te.encode('WGChat/1 Noise_IK_25519+MLKEM768_ChaChaPoly_SHA256');
    this.ck = sha256(nome);
    this.h = sha256(concat(this.ck, te.encode('WGChat/1')));
    this.k = null;
    this.n = 0;
  }
  static daJson(o) { const s = new Simm(); s.ck = daHex(o.ck); s.h = daHex(o.h); s.k = o.k ? daHex(o.k) : null; s.n = o.n; return s; }
  inJson() { return { ck: inHex(this.ck), h: inHex(this.h), k: this.k ? inHex(this.k) : null, n: this.n }; }
  mixHash(d) { this.h = sha256(concat(this.h, d)); }
  mixKey(dhOut) {
    const o = kdf(this.ck, dhOut, 'mk', 64);
    this.ck = o.slice(0, 32);
    this.k = o.slice(32);
    this.n = 0;
  }
  nonce() { const n = new Uint8Array(12); new DataView(n.buffer).setUint32(4, this.n, true); return n; }
  cifra(testo) {
    if (!this.k) throw new Error('nessuna chiave');
    const c = aeadCifra(this.k, this.nonce(), this.h, testo);
    this.n++;
    this.mixHash(c);
    return c;
  }
  decifra(c) {
    if (!this.k) throw new Error('nessuna chiave');
    const t = aeadDecifra(this.k, this.nonce(), this.h, c);
    this.n++;
    this.mixHash(c);
    return t;
  }
}

const T_STRETTA1 = 1, T_STRETTA2 = 2, T_MSG = 3, T_MSG_INIZIALE = 4;

function jsonBytes(o) { return te.encode(JSON.stringify(o)); }
function bytesJson(b) { return JSON.parse(td.decode(b)); }

// ------------------------------------------------------------------ stretta di mano
/**
 * Chi inizia (I): ha il codice di R. dati = { n: nome mostrato, mb, tk: la propria cassetta per le risposte }.
 * Restituisce il blocco da inviare a R e lo stato da conservare finché R non risponde.
 */
export function iniziaStretta(identitaI, codiceR, dati) {
  const s = new Simm();
  s.mixHash(codiceR.ikPub);                       // premessa: <- s
  const e = nuovaCoppia();
  s.mixHash(e.pub);                               // e
  s.mixKey(dh(e.priv, codiceR.ikPub));            // es
  const c1 = s.cifra(identitaI.pub);              // s
  s.mixKey(dh(identitaI.priv, codiceR.ikPub));    // ss
  const c2 = s.cifra(imbottisci(jsonBytes(dati)));
  const msg1 = concat(Uint8Array.of(T_STRETTA1), e.pub, c1, c2);
  return { msg1, stato: { simm: s.inJson(), ePriv: inHex(e.priv), ePub: inHex(e.pub), ikR: inHex(codiceR.ikPub) } };
}

/** R riceve la richiesta: scopre chi è I (chiave e nome dichiarato). Non risponde ancora: decide l'utente. */
export function riceviStretta1(identitaR, msg1) {
  if (msg1[0] !== T_STRETTA1 || msg1.length < 1 + 32 + 48 + 16) throw new Error('richiesta non valida');
  const s = new Simm();
  s.mixHash(identitaR.pub);
  const eI = msg1.slice(1, 33);
  s.mixHash(eI);
  s.mixKey(dh(identitaR.priv, eI));               // es
  const ikI = s.decifra(msg1.slice(33, 81));      // s
  s.mixKey(dh(identitaR.priv, ikI));              // ss
  const dati = bytesJson(spoglia(s.decifra(msg1.slice(81))));
  return { ikI, dati, stato: { simm: s.inJson(), eIPub: inHex(eI), ikI: inHex(ikI) } };
}

/** R accetta: risponde con la sua chiave effimera, la chiave ML-KEM e la propria cassetta. */
export function rispondiStretta(statoR, dati) {
  const s = Simm.daJson(statoR.simm);
  const eI = daHex(statoR.eIPub), ikI = daHex(statoR.ikI);
  const e = nuovaCoppia();
  const kem = ml_kem768.keygen();
  s.mixHash(e.pub);
  s.mixKey(dh(e.priv, eI));                       // ee
  s.mixKey(dh(e.priv, ikI));                      // se
  const c3 = s.cifra(imbottisci(jsonBytes({ ...dati, ek: inB64(kem.publicKey) })));
  const msg2 = concat(Uint8Array.of(T_STRETTA2), e.pub, c3);
  return { msg2, stato: { simm: s.inJson(), ePriv: inHex(e.priv), ePub: inHex(e.pub), kemSk: inHex(kem.secretKey), ikI: statoR.ikI } };
}

/** I riceve la risposta di R e crea la sessione. Restituisce anche i dati di R (nome e cassetta). */
export function finisciStretta(identitaI, statoI, msg2) {
  if (msg2[0] !== T_STRETTA2 || msg2.length < 1 + 32 + 16) throw new Error('risposta non valida');
  const s = Simm.daJson(statoI.simm);
  const eR = msg2.slice(1, 33);
  s.mixHash(eR);
  s.mixKey(dh(daHex(statoI.ePriv), eR));          // ee
  s.mixKey(dh(identitaI.priv, eR));               // se
  const dati = bytesJson(spoglia(s.decifra(msg2.slice(33))));
  const ek = daB64(dati.ek);
  if (ek.length !== 1184) throw new Error('chiave non valida');
  delete dati.ek;
  const enc = ml_kem768.encapsulate(ek);
  const sk = kdf(s.ck, enc.sharedSecret, concat(te.encode('WGChat-sk'), s.h), 32);
  const sess = iniziaRatchetAlice(sk, s.h, eR);
  sess.prefisso = inHex(enc.cipherText);          // allegato ai messaggi finché R non risponde
  sess.ikRemota = statoI.ikR;
  return { sessione: sess, dati };
}

/** R riceve il primo messaggio di I (che porta il testo cifrato ML-KEM) e crea la sessione. */
export function accettaPrimoMessaggio(statoR, blob) {
  if (blob[0] !== T_MSG_INIZIALE || blob.length < 1 + 1088 + 68 + 16) throw new Error('messaggio non valido');
  const s = Simm.daJson(statoR.simm);
  const ss = ml_kem768.decapsulate(blob.slice(1, 1089), daHex(statoR.kemSk));
  const sk = kdf(s.ck, ss, concat(te.encode('WGChat-sk'), s.h), 32);
  const sess = iniziaRatchetBob(sk, s.h, { priv: daHex(statoR.ePriv), pub: daHex(statoR.ePub) });
  sess.ikRemota = statoR.ikI;
  const testo = decifra(sess, blob);
  return { sessione: sess, testo };
}

// ------------------------------------------------------------------ Double Ratchet con cifratura delle intestazioni
function kdfRk(rk, dhOut) {
  const o = kdf(rk, dhOut, 'WGChat-rk', 96);
  return [o.slice(0, 32), o.slice(32, 64), o.slice(64, 96)];
}
function kdfCk(ck) {
  return [hmac(sha256, ck, Uint8Array.of(2)), hmac(sha256, ck, Uint8Array.of(1))];
}
function hCifra(hk, intestazione) {
  const nonce = casuali(12);
  return concat(nonce, aeadCifra(hk, nonce, new Uint8Array(0), intestazione));
}
function hDecifra(hk, eh) {
  if (!hk) return null;
  try { return aeadDecifra(hk, eh.slice(0, 12), new Uint8Array(0), eh.slice(12)); } catch (e) { return null; }
}
function chiaveMessaggio(mk) {
  const o = kdf(ZERI32, mk, 'WGChat-mk', 44);
  return [o.slice(0, 32), o.slice(32, 44)];
}
const LUNG_EH = 12 + 40 + 16;

function iniziaRatchetAlice(sk, ad, dhrPub) {
  const hka = kdf(ZERI32, sk, 'WGChat-hka', 32), nhkb = kdf(ZERI32, sk, 'WGChat-nhkb', 32);
  const dhs = nuovaCoppia();
  const [rk, cks, nhks] = kdfRk(sk, dh(dhs.priv, dhrPub));
  return { ad: inHex(ad), dhsPriv: inHex(dhs.priv), dhsPub: inHex(dhs.pub), dhr: inHex(dhrPub), rk: inHex(rk), cks: inHex(cks), ckr: null,
    ns: 0, nr: 0, pn: 0, hks: inHex(hka), hkr: null, nhks: inHex(nhks), nhkr: inHex(nhkb), saltate: {}, prefisso: null, ikRemota: null };
}
function iniziaRatchetBob(sk, ad, coppia) {
  const hka = kdf(ZERI32, sk, 'WGChat-hka', 32), nhkb = kdf(ZERI32, sk, 'WGChat-nhkb', 32);
  return { ad: inHex(ad), dhsPriv: inHex(coppia.priv), dhsPub: inHex(coppia.pub), dhr: null, rk: inHex(sk), cks: null, ckr: null,
    ns: 0, nr: 0, pn: 0, hks: null, hkr: null, nhks: inHex(nhkb), nhkr: inHex(hka), saltate: {}, prefisso: null, ikRemota: null };
}

function imbottisci(testo) {
  const tot = 2 + testo.length;
  const dim = BLOCCHI.find((b) => b >= tot);
  if (!dim) throw new Error('Il messaggio è troppo lungo.');
  const r = new Uint8Array(dim);
  r[0] = testo.length >> 8; r[1] = testo.length & 255;
  r.set(testo, 2);
  return r;
}
function spoglia(b) {
  const n = (b[0] << 8) | b[1];
  if (2 + n > b.length) throw new Error('riempimento non valido');
  return b.slice(2, 2 + n);
}

/** Cifra un messaggio per l'altro: restituisce il blocco da inviare. */
export function cifra(sess, testoBytes) {
  const ad = daHex(sess.ad);
  const [ck2, mk] = kdfCk(daHex(sess.cks));
  sess.cks = inHex(ck2);
  const intestazione = concat(daHex(sess.dhsPub), u32(sess.pn), u32(sess.ns));
  const eh = hCifra(daHex(sess.hks), intestazione);
  sess.ns += 1;
  const [k, nonce] = chiaveMessaggio(mk);
  const corpo = aeadCifra(k, nonce, concat(ad, eh), imbottisci(testoBytes));
  if (sess.prefisso) return concat(Uint8Array.of(T_MSG_INIZIALE), daHex(sess.prefisso), eh, corpo);
  return concat(Uint8Array.of(T_MSG), eh, corpo);
}

function copia(o) { return JSON.parse(JSON.stringify(o)); }

/** Decifra un blocco ricevuto. Se non va bene lancia un errore e la sessione resta com'era. */
export function decifra(sess, blob) {
  if (blob.length < 1 + LUNG_EH + 16) throw new Error('messaggio troppo corto');
  let inizio = 1;
  if (blob[0] === T_MSG_INIZIALE) inizio = 1 + 1088;
  else if (blob[0] !== T_MSG) throw new Error('tipo di messaggio sconosciuto');
  const eh = blob.slice(inizio, inizio + LUNG_EH);
  const corpo = blob.slice(inizio + LUNG_EH);
  const st = copia(sess);
  const testo = decifraHE(st, eh, corpo);
  // se l'altro risponde, ha già la sessione: il prefisso non serve più
  st.prefisso = null;
  Object.assign(sess, st);
  return testo;
}

function decifraHE(st, eh, corpo) {
  const ad = daHex(st.ad);
  // 1. chiavi saltate
  for (const chiave of Object.keys(st.saltate)) {
    const [hkHex, nStr] = chiave.split(':');
    const intest = hDecifra(daHex(hkHex), eh);
    if (intest && leggiU32(intest, 36) === Number(nStr)) {
      const mk = daHex(st.saltate[chiave]);
      const [k, nonce] = chiaveMessaggio(mk);
      const t = spoglia(aeadDecifra(k, nonce, concat(ad, eh), corpo));
      delete st.saltate[chiave];
      return t;
    }
  }
  // 2. intestazione
  let intestazione = hDecifra(st.hkr ? daHex(st.hkr) : null, eh), nuovoDh = false;
  if (!intestazione) {
    intestazione = hDecifra(st.nhkr ? daHex(st.nhkr) : null, eh);
    nuovoDh = true;
  }
  if (!intestazione) throw new Error('messaggio non decifrabile');
  const dhPub = intestazione.slice(0, 32), pn = leggiU32(intestazione, 32), n = leggiU32(intestazione, 36);
  if (nuovoDh) {
    salta(st, pn);
    st.pn = st.ns; st.ns = 0; st.nr = 0;
    st.hks = st.nhks; st.hkr = st.nhkr;
    st.dhr = inHex(dhPub);
    let o = kdfRk(daHex(st.rk), dh(daHex(st.dhsPriv), dhPub));
    st.rk = inHex(o[0]); st.ckr = inHex(o[1]); st.nhkr = inHex(o[2]);
    const nuova = nuovaCoppia();
    st.dhsPriv = inHex(nuova.priv); st.dhsPub = inHex(nuova.pub);
    o = kdfRk(daHex(st.rk), dh(nuova.priv, dhPub));
    st.rk = inHex(o[0]); st.cks = inHex(o[1]); st.nhks = inHex(o[2]);
  }
  salta(st, n);
  const [ck2, mk] = kdfCk(daHex(st.ckr));
  st.ckr = inHex(ck2);
  st.nr += 1;
  const [k, nonce] = chiaveMessaggio(mk);
  return spoglia(aeadDecifra(k, nonce, concat(ad, eh), corpo));
}

function salta(st, fino) {
  if (st.nr + MAX_SKIP < fino) throw new Error('troppi messaggi saltati');
  if (!st.ckr) return;
  while (st.nr < fino) {
    const [ck2, mk] = kdfCk(daHex(st.ckr));
    st.ckr = inHex(ck2);
    st.saltate[st.hkr + ':' + st.nr] = inHex(mk);
    st.nr += 1;
  }
  const chiavi = Object.keys(st.saltate);
  if (chiavi.length > MAX_CHIAVI_SALTATE) for (const c of chiavi.slice(0, chiavi.length - MAX_CHIAVI_SALTATE)) delete st.saltate[c];
}

// ------------------------------------------------------------------ messaggi dell'applicazione
export function testoAByte(oggetto) { return jsonBytes(oggetto); }
export function byteATesto(b) { return bytesJson(b); }

export function nuovoIdMessaggio() { return inHex(casuali(8)); }

/** Impronta SHA-256 (esadecimale) di dei byte: serve per riconoscere i blocchi già visti e per l'impronta del gettone di invio. */
export function shaHex(b) { return inHex(sha256(b)); }

/** Tipo di un blocco ricevuto: 'stretta1', 'stretta2' o 'messaggio'. */
export function tipoBlocco(blob) {
  switch (blob[0]) {
    case T_STRETTA1: return 'stretta1';
    case T_STRETTA2: return 'stretta2';
    case T_MSG: case T_MSG_INIZIALE: return 'messaggio';
    default: return 'sconosciuto';
  }
}
