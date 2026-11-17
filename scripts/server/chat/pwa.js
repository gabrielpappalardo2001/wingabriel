'use strict';
// PWA e notifiche della chat. Sta in un file a parte perché chat.js è firmato e non si tocca: questo file viene caricato prima e
// "raddrizza" l'oggetto Notification che chat.js usa, senza cambiare niente di chat.js.
//
//  1. registra il service worker (sw.js);
//  2. su Android e nelle app installate "new Notification()" non funziona: la notifica passa dal service worker;
//  3. quando la chat sta in una cornice di wingabriel.it (il plugin WordPress) il browser non lascia chiedere il permesso né mostrare
//     notifiche da lì: la richiesta e la notifica le fa la pagina che contiene la cornice, che qui si avvisa con postMessage.
// Le notifiche arrivano quando la chat è aperta (anche in secondo piano). A chat chiusa no: il relè non conserva i messaggi.
(function () {
  var PADRI = ['https://wingabriel.it', 'https://www.wingabriel.it'];
  var incorniciata = window.parent !== window;
  var Nativa = window.Notification;
  var registro = {};          // tag -> notifica "finta" a cui chat.js attacca onclick
  var permessoPadre = 'default';
  var inAttesa = [];

  // ---- service worker
  if ('serviceWorker' in navigator) {
    var indirizzo = 'sw.js';
    try {
      if (window.trustedTypes && window.trustedTypes.createPolicy) {
        // la pagina ha le Trusted Types: serve una regola con nome che accetta solo sw.js di questa cartella
        var regola = window.trustedTypes.createPolicy('wgpwa', {
          createScriptURL: function (s) {
            if (new URL(s, location.href).href !== new URL('sw.js', location.href).href) throw new TypeError('Indirizzo non permesso');
            return s;
          }
        });
        indirizzo = regola.createScriptURL('sw.js');
      }
    } catch (e) { /* senza la regola il browser può rifiutare la registrazione: la chat funziona lo stesso */ }
    window.addEventListener('load', function () {
      navigator.serviceWorker.register(indirizzo, { scope: './' }).catch(function () { /* la chat funziona anche senza */ });
    });
    navigator.serviceWorker.addEventListener('message', function (e) { clic(e.data); });
  }

  function clic(d) {
    if (!d || d.tipo !== 'wg-clic') return;
    var f = registro[String(d.tag || 'wg-chat')];
    if (f && typeof f.onclick === 'function') { try { f.onclick({}); } catch (e) { /* niente */ } }
  }

  // ---- messaggi con la pagina che contiene la cornice
  function alPadre(m) {
    for (var i = 0; i < PADRI.length; i++) { try { window.parent.postMessage(m, PADRI[i]); } catch (e) { /* niente */ } }
  }
  if (incorniciata) {
    window.addEventListener('message', function (e) {
      if (PADRI.indexOf(e.origin) < 0 || e.source !== window.parent) return;
      var d = e.data;
      if (!d || typeof d !== 'object') return;
      if (d.tipo === 'wg-permesso') {
        if (d.valore === 'granted' || d.valore === 'denied' || d.valore === 'default') permessoPadre = d.valore;
        var attese = inAttesa; inAttesa = [];
        for (var i = 0; i < attese.length; i++) attese[i](permessoPadre);
      } else if (d.tipo === 'wg-clic') {
        clic(d);
      }
    });
    alPadre({ tipo: 'wg-pronto' });
  }

  // ---- la notifica
  function finta(tag) {
    var f = {
      onclick: null,
      close: function () {
        delete registro[tag];
        if (incorniciata) { alPadre({ tipo: 'wg-chiudi', tag: tag }); return; }
        if (!('serviceWorker' in navigator)) return;
        navigator.serviceWorker.getRegistration().then(function (r) {
          if (!r || !r.getNotifications) return;
          return r.getNotifications({ tag: tag }).then(function (l) { for (var i = 0; i < l.length; i++) l[i].close(); });
        }).catch(function () { /* niente */ });
      }
    };
    registro[tag] = f;
    return f;
  }

  function viaServiceWorker(titolo, opzioni, tag) {
    var f = finta(tag);
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.getRegistration().then(function (r) {
        if (!r) return;
        return r.showNotification(titolo, { body: String(opzioni.body || ''), tag: tag, data: { tag: tag }, icon: 'icona-192.png', badge: 'icona-192.png' });
      }).catch(function () { /* niente */ });
    }
    return f;
  }

  function viaPadre(titolo, opzioni, tag) {
    var f = finta(tag);
    alPadre({ tipo: 'wg-notifica', titolo: titolo.slice(0, 100), testo: String(opzioni.body || '').slice(0, 300), tag: tag.slice(0, 100) });
    return f;
  }

  function Notifica(titolo, opzioni) {
    opzioni = opzioni || {};
    titolo = String(titolo);
    var tag = String(opzioni.tag || 'wg-chat');
    if (incorniciata) return viaPadre(titolo, opzioni, tag);
    try { return new Nativa(titolo, opzioni); } catch (e) { return viaServiceWorker(titolo, opzioni, tag); }
  }
  Object.defineProperty(Notifica, 'permission', {
    get: function () { return incorniciata ? permessoPadre : (Nativa ? Nativa.permission : 'denied'); }
  });
  Notifica.requestPermission = function (cb) {
    if (incorniciata) {
      return new Promise(function (ok) {
        inAttesa.push(ok);
        alPadre({ tipo: 'wg-chiedi-permesso' });
        setTimeout(function () { var i = inAttesa.indexOf(ok); if (i >= 0) { inAttesa.splice(i, 1); ok(permessoPadre); } }, 4000);
      }).then(function (v) { if (typeof cb === 'function') cb(v); return v; });
    }
    return Nativa.requestPermission.apply(Nativa, arguments);
  };

  // Dentro la cornice si sostituisce sempre (la pagina che la contiene decide); fuori, solo se il browser ha le notifiche.
  if (incorniciata || typeof Nativa === 'function') {
    try { window.Notification = Notifica; } catch (e) { /* niente */ }
  }
})();
