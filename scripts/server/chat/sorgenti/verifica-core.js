// Cuore del controllo della pagina della chat: usato dall'estensione del browser e dalle prove di rilascio.
// Il manifest (IMPRONTE.firmate.json) lo firma chi rilascia con una chiave che non sta sul server (Ed25519): un server compromesso non
// può costruirne uno valido per un programma modificato; può solo servire uno vecchio, per questo il manifest ha una scadenza.
import { ed25519 } from '@noble/curves/ed25519';
import { sha256 } from '@noble/hashes/sha2';

const te = new TextEncoder();

export function inHex(b) { return Array.from(b, (x) => x.toString(16).padStart(2, '0')).join(''); }
export function daHex(s) { const r = new Uint8Array(s.length / 2); for (let i = 0; i < r.length; i++) r[i] = parseInt(s.substr(i * 2, 2), 16); return r; }
export function daB64(s) { const bin = atob(s); const r = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) r[i] = bin.charCodeAt(i); return r; }
export function inB64(b) { let s = ''; for (const x of b) s += String.fromCharCode(x); return btoa(s); }
export function shaHex(bytes) { return inHex(sha256(bytes)); }

/** Firma un manifest (usato solo da chi rilascia). `chiavePrivata`: 32 byte. */
export function firmaManifest(oggetto, chiavePrivata) {
  const testo = JSON.stringify(oggetto);
  return { manifest: testo, firma: inB64(ed25519.sign(te.encode(testo), chiavePrivata)) };
}

/**
 * Controlla un documento firmato. Restituisce { ok, motivo, manifest }.
 * chiavePubblicaHex: la chiave ufficiale (sta dentro l'estensione); adesso: millisecondi.
 */
export function controllaManifest(doc, chiavePubblicaHex, adesso) {
  try {
    if (!doc || typeof doc.manifest !== 'string' || typeof doc.firma !== 'string') return { ok: false, motivo: 'Il documento delle impronte non è valido.' };
    const valida = ed25519.verify(daB64(doc.firma), te.encode(doc.manifest), daHex(chiavePubblicaHex));
    if (!valida) return { ok: false, motivo: 'La firma del documento delle impronte non è quella ufficiale.' };
    const m = JSON.parse(doc.manifest);
    if (m.formato !== 1 || typeof m.file !== 'object') return { ok: false, motivo: 'Formato del documento delle impronte sconosciuto.' };
    if (Date.parse(m.scade) < adesso) return { ok: false, motivo: 'Il documento delle impronte è scaduto: il sito serve una versione troppo vecchia.' };
    return { ok: true, manifest: m };
  } catch (e) {
    return { ok: false, motivo: 'Non riesco a controllare il documento delle impronte: ' + e.message };
  }
}

/** Confronta il contenuto di un file con l'impronta del manifest. */
export function fileConforme(manifest, nome, bytes) {
  return typeof manifest.file[nome] === 'string' && manifest.file[nome] === shaHex(bytes);
}

/**
 * Controlla com'è fatta la pagina: un solo script (chat.js, con la sua impronta dichiarata), un solo foglio di stile, niente script nel
 * testo della pagina, niente cornici né oggetti incorporati. `doc` è il documento della pagina (o uno che gli somiglia nelle prove).
 */
export function paginaConforme(doc, manifest) {
  const problemi = [];
  const scripts = Array.from(doc.querySelectorAll('script'));
  if (scripts.length !== 1) problemi.push('la pagina ha ' + scripts.length + ' script invece di uno');
  for (const s of scripts) {
    if (!s.getAttribute('src') || !/(^|\/)chat\.js$/.test(s.getAttribute('src'))) problemi.push('uno script non è chat.js');
    if (!s.getAttribute('integrity')) problemi.push('lo script non dichiara la sua impronta');
    if (s.textContent && s.textContent.trim() !== '') problemi.push('uno script è scritto dentro la pagina');
  }
  for (const l of Array.from(doc.querySelectorAll('link[rel~="stylesheet"]'))) {
    if (!/(^|\/)chat\.css$/.test(l.getAttribute('href') || '')) problemi.push('un foglio di stile non è chat.css');
  }
  if (doc.querySelector('iframe, object, embed, base, meta[http-equiv="refresh"]')) problemi.push('la pagina contiene elementi incorporati o reindirizzamenti');
  for (const e of Array.from(doc.querySelectorAll('*'))) {
    for (const a of Array.from(e.attributes)) {
      if (/^on/i.test(a.name)) { problemi.push('un elemento ha un gestore di eventi nel testo della pagina'); break; }
    }
  }
  return { ok: problemi.length === 0, problemi };
}
