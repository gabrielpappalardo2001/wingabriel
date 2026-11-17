'use strict';
// Service worker dei giochi (PWA). Serve solo i file statici della cartella /giochi/.
// Le chiamate a giochi.php (partite, attese lunghe, accesso) NON passano mai di qui: vanno sempre dirette al server.
// Quando cambi i file del sito alza VERSIONE: la vecchia copia si cancella da sola.
const VERSIONE = 'giochi-v8';
const GUSCIO = ['./', 'index.html', 'giochi.js', 'giochi.css', 'manifest.webmanifest', 'icona-192.png', 'icona-512.png', 'apple-touch-icon.png'];

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(VERSIONE).then((c) => c.addAll(GUSCIO)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys()
      .then((nomi) => Promise.all(nomi.filter((n) => n.startsWith('giochi-') && n !== VERSIONE).map((n) => caches.delete(n))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (e) => {
  const r = e.request;
  if (r.method !== 'GET') return;
  const url = new URL(r.url);
  if (url.origin !== self.location.origin) return;
  if (!url.pathname.startsWith(new URL('./', self.location).pathname)) return;
  if (url.pathname.endsWith('.php')) return;           // il server dei giochi: mai dalla copia
  const carta = url.pathname.includes('/carte/') || url.pathname.includes('/monopoli/') || url.pathname.includes('/suoni/');
  if (carta) {
    // le immagini delle carte e del monopoli e i suoni non cambiano (se le rigeneri alza VERSIONE): prima la copia, poi la rete
    e.respondWith(caches.match(r).then((c) => c || fetch(r).then((n) => {
      if (n.ok) { const copia = n.clone(); caches.open(VERSIONE).then((ca) => ca.put(r, copia)); }
      return n;
    })));
    return;
  }
  // pagina, script e stili: prima la rete (così gli aggiornamenti arrivano subito e il sito resta uguale al server), la copia solo se si è offline
  e.respondWith(fetch(r).then((n) => {
    if (n.ok) { const copia = n.clone(); caches.open(VERSIONE).then((ca) => ca.put(r, copia)); }
    return n;
  }).catch(() => caches.match(r).then((c) => c || (r.mode === 'navigate' ? caches.match('index.html') : Response.error()))));
});
