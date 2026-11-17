// Unisce l'interfaccia, il motore e la cifratura in un solo file (chat.js), senza script esterni, e scrive le impronte.
//   npm install && node build.mjs
import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const qui = dirname(fileURLToPath(import.meta.url));
const uscita = join(qui, '..', 'chat.js');
await build({
  entryPoints: [join(qui, 'app.js')],
  bundle: true,
  format: 'esm',
  target: 'es2020',
  minify: false,
  legalComments: 'inline',
  charset: 'utf8',
  outfile: uscita,
  logLevel: 'info',
});
const sha = (f) => createHash('sha256').update(readFileSync(f)).digest('hex');
const righe = ['chat.js', 'chat.css', 'index.php', 'chat.php'].map((f) => sha(join(qui, '..', f)) + '  ' + f);
writeFileSync(join(qui, '..', 'IMPRONTE.txt'), righe.join('\n') + '\n');
console.log(righe.join('\n'));
