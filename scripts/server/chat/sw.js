'use strict';
// Service worker della chat (PWA). Non salva niente: né messaggi, né chat.php, né la pagina. Serve a far installare la chat come app,
// a mostrare le notifiche (su Android e nelle app installate passano da qui) e, se si apre senza rete, a dire che manca la connessione. I messaggi passano sempre direttamente da chat.php.
self.addEventListener('install', () => { self.skipWaiting(); });
self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((nomi) => Promise.all(nomi.map((n) => caches.delete(n)))).then(() => self.clients.claim()));
});
const FUORI_RETE = '<!DOCTYPE html><html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Chat di WinGabriel</title></head><body style="font-family:system-ui,sans-serif;font-size:18px;margin:2rem"><h1>Chat</h1><p>Sei senza connessione. La chat si apre appena torna la rete: riprova tra poco.</p></body></html>';
self.addEventListener('fetch', (e) => {
  if (e.request.mode !== 'navigate') return;
  e.respondWith(fetch(e.request).catch(() => new Response(FUORI_RETE, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } })));
});

// Un tocco sulla notifica riporta la chat in primo piano e dice alla pagina quale conversazione aprire (pwa.js lo passa a chat.js).
self.addEventListener('notificationclick', (e) => {
  e.notification.close();
  const tag = e.notification.data && e.notification.data.tag;
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((lista) => {
    for (const c of lista) {
      if ('focus' in c) { c.postMessage({ tipo: 'wg-clic', tag }); return c.focus(); }
    }
    if (self.clients.openWindow) return self.clients.openWindow('./');
  }));
});
