// Firma dei file della chat al rilascio.
//   node firma.mjs genera    crea la coppia di chiavi (se non c'è) e scrive la chiave pubblica nell'estensione
//   node firma.mjs firma     calcola le impronte di chat.js, chat.css e chat.php e scrive IMPRONTE.firmate.json firmato
//   node firma.mjs verifica  controlla che il documento firmato sia valido con la chiave pubblica dell'estensione
// La chiave PRIVATA sta fuori dal repository e dal server: %LOCALAPPDATA%\WinGabriel\chiavi\chat-firma.key (o la cartella in
// WG_CHIAVI). Va conservata con cura: chi la ha può firmare programmi "ufficiali". Se si perde, si genera una nuova e si ripubblica
// l'estensione.
import { createHash, generateKeyPairSync, createPrivateKey, createPublicKey } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { firmaManifest, controllaManifest } from './verifica-core.js';

const qui = dirname(fileURLToPath(import.meta.url));
const radice = join(qui, '..');
const cartellaChiavi = process.env.WG_CHIAVI || join(process.env.LOCALAPPDATA || join(process.env.HOME || '.', '.local', 'share'), 'WinGabriel', 'chiavi');
const fileChiave = join(cartellaChiavi, 'chat-firma.key');
const filePubblica = join(radice, 'estensione', 'chiave-pubblica.json');
const fileDocumento = join(radice, 'IMPRONTE.firmate.json');
const FILE_DA_FIRMARE = ['chat.js', 'chat.css', 'chat.php'];
const VALIDITA_GIORNI = 90;

function sha(f) { return createHash('sha256').update(readFileSync(join(radice, f))).digest('hex'); }

function privataGrezza() {
  const k = createPrivateKey(readFileSync(fileChiave, 'utf8'));
  const der = k.export({ format: 'der', type: 'pkcs8' });
  return new Uint8Array(der.subarray(der.length - 32));
}

function genera() {
  if (!existsSync(fileChiave)) {
    mkdirSync(cartellaChiavi, { recursive: true });
    const { privateKey } = generateKeyPairSync('ed25519');
    writeFileSync(fileChiave, privateKey.export({ format: 'pem', type: 'pkcs8' }), { mode: 0o600 });
    console.log('Creata la chiave di firma: ' + fileChiave + '\nCONSERVALA e fanne una copia al sicuro: non sta nel repository né sul server.');
  }
  const pub = createPublicKey(createPrivateKey(readFileSync(fileChiave, 'utf8'))).export({ format: 'der', type: 'spki' });
  const pubHex = Buffer.from(pub.subarray(pub.length - 32)).toString('hex');
  mkdirSync(dirname(filePubblica), { recursive: true });
  const nuovo = JSON.stringify({ chiavePubblica: pubHex }, null, 2) + '\n';
  if (!existsSync(filePubblica) || readFileSync(filePubblica, 'utf8') !== nuovo) {
    writeFileSync(filePubblica, nuovo);
    console.log('Chiave pubblica scritta in estensione/chiave-pubblica.json (se è cambiata, l\'estensione va ricostruita e ripubblicata).');
  }
  return pubHex;
}

function firma() {
  genera();
  const adesso = new Date();
  const manifest = { formato: 1, prodotto: 'WinGabriel chat', creato: adesso.toISOString(), scade: new Date(adesso.getTime() + VALIDITA_GIORNI * 86400000).toISOString(), file: {} };
  for (const f of FILE_DA_FIRMARE) manifest.file[f] = sha(f);
  const doc = firmaManifest(manifest, privataGrezza());
  writeFileSync(fileDocumento, JSON.stringify(doc) + '\n');
  console.log('Firmato ' + fileDocumento + ' (valido fino al ' + manifest.scade.slice(0, 10) + ')');
  for (const f of FILE_DA_FIRMARE) console.log('  ' + manifest.file[f] + '  ' + f);
}

function verifica() {
  const pub = JSON.parse(readFileSync(filePubblica, 'utf8')).chiavePubblica;
  const doc = JSON.parse(readFileSync(fileDocumento, 'utf8'));
  const r = controllaManifest(doc, pub, Date.now());
  if (!r.ok) { console.error('NON VALIDO: ' + r.motivo); process.exit(1); }
  for (const f of FILE_DA_FIRMARE) if (r.manifest.file[f] !== sha(f)) { console.error('NON VALIDO: ' + f + ' è cambiato dopo la firma'); process.exit(1); }
  console.log('Valido: le impronte firmate corrispondono ai file.');
}

const comando = process.argv[2];
if (comando === 'genera') genera();
else if (comando === 'firma') firma();
else if (comando === 'verifica') verifica();
else { console.error('Uso: node firma.mjs genera | firma | verifica'); process.exit(2); }
