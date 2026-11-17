// Costruisce l'estensione di verifica (verifica.js) dai sorgenti: node build-estensione.mjs
import { build } from 'esbuild';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';

const qui = dirname(fileURLToPath(import.meta.url));
const estensione = join(qui, '..', 'estensione');
if (!existsSync(join(estensione, 'chiave-pubblica.json'))) {
  console.error('Manca estensione/chiave-pubblica.json: lancia prima "node firma.mjs genera".');
  process.exit(1);
}
await build({
  entryPoints: [join(estensione, 'verifica.src.js')],
  bundle: true,
  format: 'iife',
  target: 'es2020',
  minify: false,
  legalComments: 'inline',
  charset: 'utf8',
  outfile: join(estensione, 'verifica.js'),
  logLevel: 'info',
});
