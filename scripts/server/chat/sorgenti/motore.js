// Motore della chat di WinGabriel (WGChat): contatti, richieste, coda di invio con riprova, conferme di ricezione.
// Non conosce né il browser né il disco: chi lo usa gli passa il trasporto (il relè) e la funzione che salva lo stato.
// Stesso comportamento della versione per il programma (MotoreChat.vb).
import * as c from './cripto.js';

const GIORNI = 86400000;
export const SCADENZA_CODA = 14 * GIORNI;
const MAX_CONTATTI = 200;
const MAX_VISTI = 300;
const RIPROVA_OK = 0;         // niente attesa: se manca la conferma si rimanda subito

function pulisciNome(n) {
  return String(n ?? '').replace(/[\u0000-\u001f\u007f]/g, '').trim().slice(0, 30);
}
function esadecimale(s, n) { return typeof s === 'string' && s.length === n && /^[0-9a-f]+$/i.test(s); }
function nuovoId() { return c.inHex(c.casuali(8)); }

export function statoVuoto(nome) {
  const id = c.creaIdentita();
  return { v: 1, ident: { priv: c.inHex(id.priv), pub: c.inHex(id.pub) }, nome: pulisciNome(nome) || 'Io', inviti: [], contatti: [], coda: [], bloccati: [] };
}

export class MotoreChat {
  /**
   * @param {object} o  stato: lo stato salvato (o null per cominciare); trasporto: { invia(mb, tk, b64) -> 'ok'|'no', ascolta(cassette, ack, attesa) -> [{id, mb, b}] };
   *                    salvaStato(stato): salva lo stato; salvaMessaggi(cid, elenco): salva i messaggi di una conversazione; messaggi: { cid: [...] }; ora: () => ms
   */
  constructor(o) {
    this.s = o.stato || statoVuoto(o.nome);
    if (!Array.isArray(this.s.bloccati)) this.s.bloccati = [];
    // il nome nella chat è il nome utente dei giochi: uno solo per tutto
    if (o.nome && o.stato && this.s.nome !== pulisciNome(o.nome)) this.s.nome = pulisciNome(o.nome);
    this.messaggi = o.messaggi || {};
    this.trasporto = o.trasporto;
    this._salvaStato = o.salvaStato || (async () => {});
    this._salvaMessaggi = o.salvaMessaggi || (async () => {});
    this.ora = o.ora || (() => Date.now());
    this.ascoltatori = {};
    this.ackPendenti = [];
    this.attivo = false;
    this.collegato = false;
  }

  // ---------------------------------------------------------------- eventi
  su(tipo, f) { (this.ascoltatori[tipo] ||= []).push(f); }
  emetti(tipo, dati) { for (const f of this.ascoltatori[tipo] || []) { try { f(dati); } catch (e) { /* un ascoltatore che sbaglia non ferma il motore */ } } }

  get identita() { return { priv: c.daHex(this.s.ident.priv), pub: c.daHex(this.s.ident.pub) }; }
  contatto(cid) { return this.s.contatti.find((k) => k.id === cid); }
  async salva() { await this._salvaStato(this.s); }

  // ---------------------------------------------------------------- inviti e contatti
  async cambiaNome(nome) { this.s.nome = pulisciNome(nome) || this.s.nome; await this.salva(); }

  async nuovoInvito() {
    const inv = c.creaInvito(this.identita);
    inv.creato = this.ora();
    this.s.inviti.push(inv);
    if (this.s.inviti.length > 5) this.s.inviti.shift();
    await this.salva();
    return inv;
  }

  async eliminaInvito(mb) {
    this.s.inviti = this.s.inviti.filter((i) => i.mb !== mb);
    await this.salva();
  }

  /** Aggiunge un contatto con il suo codice: parte la richiesta, che sarà consegnata appena l'altro è collegato. */
  async aggiungiContatto(codiceTesto, utente = '') {
    if (this.s.contatti.length >= MAX_CONTATTI) throw new Error('Hai già troppi contatti.');
    const codice = c.leggiCodice(codiceTesto);
    if (c.uguali(codice.ikPub, this.identita.pub)) throw new Error('Questo è il tuo codice: devi usare quello di un\'altra persona.');
    const ikHex = c.inHex(codice.ikPub);
    if (this.s.bloccati.some((b) => b.ik === ikHex)) throw new Error('Hai bloccato questa persona: prima sbloccala.');
    const esistente = this.s.contatti.find((k) => k.ikRemota === ikHex && k.stato !== 'rifiutato');
    if (esistente) throw new Error('Hai già questo contatto: ' + this.nomeVisibile(esistente) + '.');
    const mia = { mb: c.inHex(c.casuali(16)), rd: c.inHex(c.casuali(32)), tk: c.inHex(c.casuali(32)) };
    const r = c.iniziaStretta(this.identita, codice, { n: this.s.nome, mb: mia.mb, tk: mia.tk });
    const k = { id: nuovoId(), nome: pulisciNome(utente) || 'Nuovo contatto', utente: pulisciNome(utente), alias: '', stato: 'invitato', ikRemota: ikHex, mia, verso: { mb: codice.mb, tk: codice.tk },
      stretta: r.stato, rstato: null, sessione: null, verificato: false, visti: {}, nonLetti: 0, creato: this.ora() };
    this.s.contatti.push(k);
    this.accodaBlocco(k, 'stretta', r.msg1, null);
    await this.salva();
    this.emetti('contatto', k);
    return k;
  }

  async accetta(cid) {
    const k = this.contatto(cid);
    if (!k || k.stato !== 'richiesta') throw new Error('Nessuna richiesta da accettare.');
    k.mia = { mb: c.inHex(c.casuali(16)), rd: c.inHex(c.casuali(32)), tk: c.inHex(c.casuali(32)) };
    const r = c.rispondiStretta(k.rstato, { n: this.s.nome, mb: k.mia.mb, tk: k.mia.tk });
    k.rstato = r.stato;
    k.stato = 'accettato';
    this.accodaBlocco(k, 'stretta2', r.msg2, null);
    await this.salva();
    this.emetti('contatto', k);
  }

  async rifiuta(cid) { await this.elimina(cid); }

  /**
   * Blocca una persona: non riceve più niente da lei, le sue richieste vengono ignorate e non le arriva alcun segnale (i suoi messaggi
   * restano "in attesa", come se non fossi collegato). Restituisce il nome utente, se si conosce, per bloccarlo anche nei giochi.
   */
  async blocca(cid) {
    const k = this.contatto(cid);
    if (!k) return '';
    if (!this.s.bloccati.some((b) => b.ik === k.ikRemota)) this.s.bloccati.push({ ik: k.ikRemota, nome: this.nomeVisibile(k), utente: k.utente || '' });
    k.bloccato = true;
    this.s.coda = this.s.coda.filter((q) => q.cid !== cid);
    await this.salva();
    this.emetti('contatto', k);
    return k.utente || '';
  }

  /** Sblocca per chiave d'identità (resta valido anche se il contatto era stato eliminato). */
  async sblocca(ik) {
    const b = this.s.bloccati.find((x) => x.ik === ik);
    this.s.bloccati = this.s.bloccati.filter((x) => x.ik !== ik);
    const k = this.s.contatti.find((x) => x.ikRemota === ik);
    if (k) k.bloccato = false;
    await this.salva();
    this.emetti('contatto', k || null);
    return b ? (b.utente || '') : '';
  }

  elencoBloccati() { return this.s.bloccati; }

  async elimina(cid) {
    this.s.contatti = this.s.contatti.filter((k) => k.id !== cid);
    this.s.coda = this.s.coda.filter((q) => q.cid !== cid);
    delete this.messaggi[cid];
    await this.salva();
    await this._salvaMessaggi(cid, null);
    this.emetti('contatto', null);
  }

  async segnaVerificato(cid, valore = true) { const k = this.contatto(cid); if (k) { k.verificato = valore; await this.salva(); this.emetti('contatto', k); } }
  async rinomina(cid, alias) { const k = this.contatto(cid); if (k) { k.alias = pulisciNome(alias); await this.salva(); this.emetti('contatto', k); } }
  nomeVisibile(k) { return k.alias || k.nome; }
  numeroDiSicurezza(cid) { const k = this.contatto(cid); return k ? c.numeroDiSicurezza(this.identita.pub, c.daHex(k.ikRemota)) : ''; }
  elencoMessaggi(cid) { return this.messaggi[cid] || []; }

  async segnaLetti(cid) {
    const k = this.contatto(cid);
    if (k && k.nonLetti) { k.nonLetti = 0; await this.salva(); this.emetti('contatto', k); }
  }

  // ---------------------------------------------------------------- invio
  accodaBlocco(k, tipo, blob, mid) {
    this.s.coda.push({ cid: k.id, tipo, mid, b64: c.inB64(blob), verso: { ...k.verso }, tentativi: 0, prossimo: 0, creato: this.ora() });
  }

  accodaMessaggio(k, oggetto, tipo) {
    const blob = c.cifra(k.sessione, c.testoAByte(oggetto));
    this.accodaBlocco(k, tipo, blob, oggetto.id || null);
  }

  /** Scrive un messaggio: si cifra subito e resta in coda finché l'altro non lo riceve. */
  async scrivi(cid, testo) {
    const k = this.contatto(cid);
    if (k && k.bloccato) throw new Error('Hai bloccato questa persona: sbloccala per scriverle.');
    if (!k || !k.sessione || !k.sessione.cks) throw new Error('Questa conversazione non è ancora pronta: aspetta che l\'altro si colleghi.');
    testo = String(testo).replace(/\r\n/g, '\n').trim();
    if (!testo) throw new Error('Scrivi qualcosa.');
    if (testo.length > c.MASSIMO_TESTO) throw new Error('Il messaggio è troppo lungo: al massimo ' + c.MASSIMO_TESTO + ' caratteri.');
    const id = nuovoId();
    this.accodaMessaggio(k, { t: 'm', id, x: testo }, 'msg');
    const m = { id, dir: 'out', ts: this.ora(), x: testo, st: 'attesa' };
    (this.messaggi[cid] ||= []).push(m);
    await this.salva();
    await this._salvaMessaggi(cid, this.messaggi[cid]);
    this.emetti('messaggio', { cid, m });
    this.sollecita();
    return m;
  }

  /** Prova a consegnare quello che è in coda. Restituisce quanti blocchi sono stati accettati dal relè. */
  async inviaCoda() {
    if (this._invio) return 0;     // un solo invio alla volta
    this._invio = true;
    try { return await this._inviaCoda(); } finally { this._invio = false; }
  }

  /** Fa partire subito la consegna della coda (per esempio dopo aver scritto), senza aspettare il prossimo giro. */
  sollecita() { this.inviaCoda().catch(() => {}); }

  async _inviaCoda() {
    const adesso = this.ora();
    let accettati = 0, cambiato = false;
    for (const q of [...this.s.coda]) {
      const k = this.contatto(q.cid);
      if (!k || k.bloccato) { this.s.coda = this.s.coda.filter((x) => x !== q); cambiato = true; continue; }
      if (adesso - q.creato > SCADENZA_CODA) {
        this.s.coda = this.s.coda.filter((x) => x !== q);
        const m = (this.messaggi[q.cid] || []).find((x) => x.id === q.mid);
        if (m && m.st !== 'consegnato') { m.st = 'scaduto'; await this._salvaMessaggi(q.cid, this.messaggi[q.cid]); this.emetti('messaggio', { cid: q.cid, m }); }
        cambiato = true;
        continue;
      }
      if (q.prossimo > adesso) continue;
      let r;
      try { r = await this.trasporto.invia(q.verso.mb, q.verso.tk, q.b64); } catch (e) { r = 'errore'; }
      if (r === 'ok') {
        accettati++;
        if (q.tipo === 'ack') {
          this.s.coda = this.s.coda.filter((x) => x !== q);
        } else {
          q.prossimo = adesso + RIPROVA_OK;
          q.tentativi = 0;
          const m = (this.messaggi[q.cid] || []).find((x) => x.id === q.mid);
          if (m && m.st === 'attesa') { m.st = 'inviato'; await this._salvaMessaggi(q.cid, this.messaggi[q.cid]); this.emetti('messaggio', { cid: q.cid, m }); }
        }
      } else {
        q.tentativi++;
        q.prossimo = adesso;
      }
      cambiato = true;
    }
    if (cambiato) await this.salva();
    return accettati;
  }

  // ---------------------------------------------------------------- ricezione
  cassette() {
    const r = [];
    for (const i of this.s.inviti) r.push({ mb: i.mb, rd: i.rd, th: c.shaHex(c.daHex(i.tk)) });
    for (const k of this.s.contatti) if (k.mia && k.stato !== 'richiesta' && !k.bloccato) r.push({ mb: k.mia.mb, rd: k.mia.rd, th: c.shaHex(c.daHex(k.mia.tk)) });
    return r;
  }

  /** Una richiesta all'ascolto: chiede al relè se c'è qualcosa e lo elabora. Restituisce quanti blocchi sono arrivati. */
  async ascolta(attesa) {
    const cassette = this.cassette();
    if (!cassette.length) return 0;
    const ack = this.ackPendenti;
    this.ackPendenti = [];
    let blocchi;
    try {
      blocchi = await this.trasporto.ascolta(cassette, ack, attesa);
      this.collegato = true;
    } catch (e) {
      this.ackPendenti = ack.concat(this.ackPendenti);
      if (this.collegato) { this.collegato = false; this.emetti('collegamento', false); }
      throw e;
    }
    for (const b of blocchi) {
      try { await this.gestisciBlocco(b); } catch (e) { /* un blocco rovinato non ferma gli altri */ }
      this.ackPendenti.push(b.id);
    }
    if (blocchi.length) await this.salva();
    return blocchi.length;
  }

  async gestisciBlocco(b) {
    let blob;
    try { blob = c.daB64(b.b); } catch (e) { return; }
    const tipo = c.tipoBlocco(blob);
    const inv = this.s.inviti.find((i) => i.mb === b.mb);
    if (inv) { if (tipo === 'stretta1') await this.nuovaRichiesta(blob); return; }
    const k = this.s.contatti.find((x) => x.mia && x.mia.mb === b.mb);
    if (!k || k.bloccato) return;
    if (tipo === 'stretta2' && k.stato === 'invitato') return this.completaStretta(k, blob);
    if (tipo === 'messaggio' && (k.stato === 'attivo' || k.stato === 'accettato')) return this.nuovoMessaggio(k, blob);
  }

  async nuovaRichiesta(blob) {
    if (this.s.contatti.length >= MAX_CONTATTI) return;
    let r;
    try { r = c.riceviStretta1(this.identita, blob); } catch (e) { return; }
    const ikHex = c.inHex(r.ikI);
    if (ikHex === this.s.ident.pub) return;
    if (this.s.bloccati.some((x) => x.ik === ikHex)) return;     // una persona bloccata non riesce nemmeno a chiedere
    const d = r.dati;
    if (!esadecimale(d.mb, 32) || !esadecimale(d.tk, 64)) return;
    // la stessa richiesta rimandata: già nota
    if (this.s.contatti.some((k) => k.ikRemota === ikHex && k.stato !== 'rifiutato')) return;
    const k = { id: nuovoId(), nome: pulisciNome(d.n) || 'Sconosciuto', alias: '', stato: 'richiesta', ikRemota: ikHex, mia: null, verso: { mb: d.mb, tk: d.tk },
      stretta: null, rstato: r.stato, sessione: null, verificato: false, visti: {}, nonLetti: 0, creato: this.ora() };
    this.s.contatti.push(k);
    this.emetti('richiesta', k);
    this.emetti('contatto', k);
  }

  async completaStretta(k, blob) {
    let r;
    try { r = c.finisciStretta(this.identita, k.stretta, blob); } catch (e) { return; }
    const d = r.dati;
    if (!esadecimale(d.mb, 32) || !esadecimale(d.tk, 64)) return;
    k.sessione = r.sessione;
    k.verso = { mb: d.mb, tk: d.tk };
    k.nome = pulisciNome(d.n) || k.nome;
    k.stretta = null;
    k.stato = 'attivo';
    this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.tipo === 'stretta'));
    this.accodaMessaggio(k, { t: 'k', id: nuovoId() }, 'k');
    this.emetti('contatto', k);
  }

  async nuovoMessaggio(k, blob) {
    const h = c.shaHex(blob).slice(0, 32);
    let testo;
    try {
      if (!k.sessione) {
        const r = c.accettaPrimoMessaggio(k.rstato, blob);
        k.sessione = r.sessione;
        k.rstato = null;
        k.stato = 'attivo';
        this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.tipo === 'stretta2'));
        testo = r.testo;
        this.emetti('contatto', k);
      } else {
        testo = c.decifra(k.sessione, blob);
      }
    } catch (e) {
      // blocco già visto (rimandato perché la conferma non è arrivata): si conferma di nuovo
      const idNoto = k.visti[h];
      if (idNoto && k.sessione && k.sessione.cks) { this.accodaMessaggio(k, { t: 'a', id: idNoto }, 'ack'); }
      return;
    }
    let o;
    try { o = c.byteATesto(testo); } catch (e) { return; }
    k.visti[h] = typeof o.id === 'string' ? o.id : '';
    const chiavi = Object.keys(k.visti);
    if (chiavi.length > MAX_VISTI) for (const x of chiavi.slice(0, chiavi.length - MAX_VISTI)) delete k.visti[x];
    if (o.t === 'm' && typeof o.x === 'string' && esadecimale(o.id, 16)) {
      const lista = (this.messaggi[k.id] ||= []);
      if (!lista.some((m) => m.id === o.id)) {
        const m = { id: o.id, dir: 'in', ts: this.ora(), x: o.x.slice(0, c.MASSIMO_TESTO), st: 'consegnato' };
        lista.push(m);
        k.nonLetti = (k.nonLetti || 0) + 1;
        await this._salvaMessaggi(k.id, lista);
        this.emetti('messaggio', { cid: k.id, m });
      }
      this.accodaMessaggio(k, { t: 'a', id: o.id }, 'ack');
    } else if (o.t === 'k' && esadecimale(o.id, 16)) {
      this.accodaMessaggio(k, { t: 'a', id: o.id }, 'ack');
    } else if (o.t === 'a' && esadecimale(o.id, 16)) {
      const m = (this.messaggi[k.id] || []).find((x) => x.id === o.id);
      if (m && m.st !== 'consegnato') { m.st = 'consegnato'; await this._salvaMessaggi(k.id, this.messaggi[k.id]); this.emetti('messaggio', { cid: k.id, m }); }
      this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.mid === o.id));
    }
  }

  // ---------------------------------------------------------------- ciclo
  /** Resta collegato: ascolta e consegna finché non si chiama ferma(). */
  async avvia(attesa = 15) {
    this.attivo = true;
    let errori = 0;
    while (this.attivo) {
      try {
        await this.inviaCoda();
        const prossima = this.s.coda.reduce((m, q) => Math.min(m, q.prossimo), Infinity);
        const secondi = Math.max(1, Math.min(attesa, Math.ceil((prossima - this.ora()) / 1000)));
        const n = await this.ascolta(secondi);
        if (!this.collegatoNotificato) { this.collegatoNotificato = true; this.emetti('collegamento', true); }
        errori = 0;
        if (!n && !this.cassette().length) await new Promise((r) => setTimeout(r, 1000));
      } catch (e) {
        errori++;
        if (this.collegatoNotificato !== false) { this.collegatoNotificato = false; this.emetti('collegamento', false); }
        await new Promise((r) => setTimeout(r, Math.min(30000, 2000 * errori)));
      }
    }
  }

  ferma() { this.attivo = false; }
}
