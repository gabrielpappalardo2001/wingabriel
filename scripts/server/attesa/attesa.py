#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Motore d'attesa di WinGabriel: tiene le attese lunghe di giochi e chat senza occupare un processo di php-fpm per ogni persona.

Prima ogni giocatore (o chat) che aspettava una novità teneva aperta una richiesta di fino a 20 secondi dentro PHP: un processo
php-fpm bloccato per ognuno, e con 40 processi finivano i posti. Ora quelle attese stanno qui, in un solo processo che regge
migliaia di connessioni quasi gratis. PHP resta l'unica cosa che decide: questo programma non conosce né regole né segreti.

Come funziona (nginx gli manda solo le richieste che aspettano; tutto il resto va ad Apache e a PHP come prima):
  giochi   GET  /giochi/giochi.php?a=stato&id=..&attesa=S
  chat     POST /chat/chat.php?a=ascolta  {cassette, ack, attesa}
  1. la richiesta si gira ad Apache con attesa=0: PHP controlla chi è (sessione o cassetta), aggiorna la presenza e risponde subito;
  2. se la risposta ha già qualcosa di nuovo, si restituisce subito;
  3. altrimenti si aspetta, senza PHP, che il file della partita (o il segnale della cassetta, scritto da chat.php quando arriva un
     blocco) cambi, oppure che passi il tempo dell'attesa, oppure che chi aspetta se ne vada;
  4. a quel punto si gira ad Apache una seconda volta e si restituisce quella risposta.
Se questo programma non c'è, nginx rimanda le richieste ad Apache come prima e tutto funziona uguale (solo con più processi php).

Non scrive nulla su disco, non registra indirizzi, sessioni, nomi né testi: sul registro di sistema finiscono solo gli errori.
Si ferma con SIGTERM (risponde subito a chi aspetta, che riprova). Configurazione con variabili d'ambiente (vedi sotto).
"""
import asyncio
import functools
import json
import os
import re
import signal
import sys
import time
import traceback
from http import HTTPStatus
from urllib.parse import parse_qsl, urlencode, urlsplit

def registra_errori(f):
    """Ogni funzione registra gli errori imprevisti (una sola volta per eccezione): funzione, tipo e riga del codice, mai il messaggio né dati di chi gioca o scrive.
    Le eccezioni che qui sono la norma (rete che cade, attese annullate o scadute, dati non validi) non sono errori del programma e passano senza traccia."""
    previste = (asyncio.CancelledError, asyncio.TimeoutError, TimeoutError, ConnectionError, asyncio.IncompleteReadError, asyncio.LimitOverrunError, ValueError, KeyboardInterrupt, SystemExit, GeneratorExit)

    def segna(e):
        if isinstance(e, previste) or getattr(e, '_registrato', False):
            return
        try:
            e._registrato = True
            ultimo = traceback.extract_tb(e.__traceback__)[-1]
            _log('[attesa] %s: %s (%s:%d)' % (f.__qualname__, type(e).__name__, os.path.basename(ultimo.filename), ultimo.lineno))
        except Exception:
            pass

    if asyncio.iscoroutinefunction(f):
        @functools.wraps(f)
        async def avvolta(*a, **k):
            try:
                return await f(*a, **k)
            except BaseException as e:
                segna(e)
                raise
    else:
        @functools.wraps(f)
        def avvolta(*a, **k):
            try:
                return f(*a, **k)
            except BaseException as e:
                segna(e)
                raise
    return avvolta


@registra_errori
def _env(nome, predefinito):
    return os.environ.get(nome, predefinito)


INDIRIZZO = _env('WG_ATTESA_INDIRIZZO', '127.0.0.1')
PORTA = int(_env('WG_ATTESA_PORTA', '9701'))
UPSTREAM_HOST = _env('WG_UPSTREAM_HOST', '127.0.0.1')   # Apache, in chiaro e solo su questo computer
UPSTREAM_PORTA = int(_env('WG_UPSTREAM_PORTA', '8080'))
HOST_SITO = _env('WG_HOST_SITO', 'wingabriel.net')
DATI = {
    ('giochi', False): _env('WG_DATI_GIOCHI', '/var/lib/wingabriel-giochi'),
    ('giochi', True): _env('WG_DATI_GIOCHI_BETA', '/var/lib/wingabriel-giochi-beta'),
    ('chat', False): _env('WG_DATI_CHAT', '/dev/shm/wingabriel-chat'),
    ('chat', True): _env('WG_DATI_CHAT_BETA', '/dev/shm/wingabriel-chat-beta'),
}
MASSIMO_ATTESE = int(_env('WG_MASSIMO_ATTESE', '4000'))
PERIODO = float(_env('WG_PERIODO', '0.12'))           # ogni quanto si guardano i file
ATTESA_MASSIMA = 20                                    # come in PHP
TEMPO_RICHIESTA_ORIGINE = 30                           # una chiamata ad Apache non dura di più
TEMPO_INATTIVITA = 120                                 # una connessione ferma si chiude
MASSIMO_CORPO = 700000

RE_ID = re.compile(r'^[A-Z0-9]{5}$')
RE_CASSETTA = re.compile(r'^[0-9a-fA-F]{32}$')
RE_GIOCHI = re.compile(r'^(/beta)?/giochi/giochi\.php$')
RE_CHAT = re.compile(r'^(/beta)?/chat/chat\.php$')
RE_RIGA = re.compile(r'^([A-Z]+) (\S+) HTTP/1\.[01]$')

# intestazioni che non si ripetono verso chi ha chiesto (le ricalcola questo programma)
SALTA_RISPOSTA = {'connection', 'keep-alive', 'transfer-encoding', 'content-length', 'date', 'server', 'upgrade', 'proxy-connection'}

GRAZIA_ASSENZA = float(_env('WG_GRAZIA_ASSENZA', '25'))   # dopo quanti secondi senza una nuova attesa si avvisa che un giocatore ha perso il collegamento
RE_TOKEN = re.compile(r'^[0-9a-f]{32}$')
DURATA_FLUSSO = float(_env('WG_DURATA_FLUSSO', '300'))   # un flusso di eventi si richiude dopo questo tempo (il client lo riapre da solo)
PING = float(_env('WG_PING', '15'))                      # ogni quanti secondi si controlla e si dà segno di vita a chi ascolta un flusso

attese = set()
attivi = {}     # (sessione, partita, beta) -> quante attese ha in corso
assenze = {}    # (sessione, partita, beta) -> quando avvisare gli altri, se nel frattempo non è tornato
fermo = False
ultimo_avviso_pieno = 0.0


def _log(testo):
    # solo errori del programma, mai dati di chi gioca o scrive
    print(testo, file=sys.stderr, flush=True)


@registra_errori
def mtime(percorso):
    try:
        return os.stat(percorso).st_mtime_ns
    except OSError:
        return -1


class Attesa:
    """Una richiesta che sta aspettando: i file da guardare e com'erano quando è cominciata."""
    __slots__ = ('percorsi', 'firma', 'scade', 'evento', 'lettore', 'scrittore')

    @registra_errori
    def __init__(self, percorsi, firma, scade, lettore, scrittore):
        self.percorsi = percorsi
        self.firma = firma
        self.scade = scade
        self.evento = asyncio.Event()
        self.lettore = lettore
        self.scrittore = scrittore


@registra_errori
def _se_ne_e_andato(lettore, scrittore):
    return lettore.at_eof() or scrittore.transport.is_closing()


@registra_errori
async def segnala_assenza(sessione, id_partita, beta):
    """Dice a PHP che questo giocatore ha perso il collegamento: PHP lo scrive nella partita solo se è davvero sparito."""
    try:
        percorso = '/beta/giochi/giochi.php' if beta else '/giochi/giochi.php'
        corpo = json.dumps({'id': id_partita}).encode('utf-8')
        await inoltra('POST', percorso + '?a=presenza', {'x-token': sessione, 'content-type': 'application/json'}, corpo)
    except Exception as e:
        _log('attesa: segnalazione di assenza non riuscita (%s)' % type(e).__name__)


@registra_errori
async def sorveglia():
    """Un solo giro guarda tutti: scade il tempo, se ne va chi aspetta, cambia un file. Poi avvisa delle assenze."""
    global ultimo_avviso_pieno
    while True:
        await asyncio.sleep(PERIODO)
        adesso = time.monotonic()
        if assenze and not fermo:
            for chiave, quando in list(assenze.items()):
                if adesso >= quando:
                    del assenze[chiave]
                    asyncio.ensure_future(segnala_assenza(*chiave))
        if len(attese) >= MASSIMO_ATTESE * 0.8 and adesso - ultimo_avviso_pieno > 60:
            ultimo_avviso_pieno = adesso
            _log('attesa: ATTENZIONE, %d attese su un massimo di %d' % (len(attese), MASSIMO_ATTESE))
        if not attese:
            continue
        letti = {}
        for a in list(attese):
            if a.evento.is_set():
                continue
            if adesso >= a.scade or _se_ne_e_andato(a.lettore, a.scrittore):
                a.evento.set()
                continue
            for percorso, prima in zip(a.percorsi, a.firma):
                ora = letti.get(percorso)
                if ora is None:
                    ora = letti[percorso] = mtime(percorso)
                if ora != prima:
                    a.evento.set()
                    break


@registra_errori
async def aspetta(percorsi, firma, secondi, lettore, scrittore):
    """Aspetta un cambiamento dei file, il tempo o la chiusura. Vero se chi aspettava se n'è andato."""
    a = Attesa(percorsi, firma, time.monotonic() + secondi, lettore, scrittore)
    if fermo:
        return False
    attese.add(a)
    try:
        await a.evento.wait()
    finally:
        attese.discard(a)
    return _se_ne_e_andato(lettore, scrittore)


# ---------------------------------------------------------------------- verso Apache
@registra_errori
def _decodifica_a_pezzi(dati):
    uscita = bytearray()
    while dati:
        riga, _, resto = dati.partition(b'\r\n')
        try:
            lunghezza = int(riga.split(b';')[0].strip() or b'0', 16)
        except ValueError:
            break
        if lunghezza == 0:
            break
        uscita += resto[:lunghezza]
        dati = resto[lunghezza + 2:]
    return bytes(uscita)


@registra_errori
async def inoltra(metodo, target, intest, corpo):
    """Chiede ad Apache (e quindi a PHP). Restituisce (stato, [(nome, valore)], corpo)."""
    lettore, scrittore = await asyncio.wait_for(asyncio.open_connection(UPSTREAM_HOST, UPSTREAM_PORTA), 5)
    try:
        testa = [
            '%s %s HTTP/1.1' % (metodo, target),
            'Host: ' + HOST_SITO,
            'Accept: application/json',
            'Accept-Encoding: identity',
            'X-Forwarded-Proto: https',
            'Connection: close',
        ]
        for nome in ('x-token', 'content-type', 'origin'):
            if nome in intest:
                testa.append('%s: %s' % (nome, intest[nome]))
        if metodo != 'GET' or corpo:
            testa.append('Content-Length: %d' % len(corpo))
        scrittore.write(('\r\n'.join(testa) + '\r\n\r\n').encode('latin-1') + corpo)
        await scrittore.drain()
        dati = await asyncio.wait_for(lettore.read(-1), TEMPO_RICHIESTA_ORIGINE)
    finally:
        scrittore.close()
    testa_b, separatore, resto = dati.partition(b'\r\n\r\n')
    if not separatore:
        raise ConnectionError('risposta di Apache non valida')
    righe = testa_b.split(b'\r\n')
    parti = righe[0].split(b' ', 2)
    if len(parti) < 2 or not parti[1].isdigit():
        raise ConnectionError('risposta di Apache non valida')
    stato = int(parti[1])
    intestazioni = []
    a_pezzi = False
    for r in righe[1:]:
        nome, _, valore = r.partition(b':')
        nome = nome.strip().decode('latin-1')
        valore = valore.strip().decode('latin-1')
        if nome.lower() == 'transfer-encoding' and 'chunked' in valore.lower():
            a_pezzi = True
        intestazioni.append((nome, valore))
    return stato, intestazioni, (_decodifica_a_pezzi(resto) if a_pezzi else resto)


@registra_errori
def _json_di(risposta):
    try:
        d = json.loads(risposta[2])
        return d if isinstance(d, dict) else None
    except (ValueError, UnicodeDecodeError):
        return None


# ---------------------------------------------------------------------- le due attese
@registra_errori
async def attesa_stato(target, intest, corpo, beta, lettore, scrittore):
    parti = urlsplit(target)
    coppie = parse_qsl(parti.query, keep_blank_values=True)
    q = dict(coppie)
    try:
        attesa = max(0, min(ATTESA_MASSIMA, int(q.get('attesa', '0'))))
    except ValueError:
        attesa = 0
    id_partita = q.get('id', '')
    if attesa == 0 or not RE_ID.match(id_partita) or fermo:
        return await inoltra('GET', target, intest, b'')
    return await _con_presenza(intest, id_partita, beta, lambda: _attesa_stato_interna(parti, coppie, q, attesa, id_partita, intest, beta, lettore, scrittore))


@registra_errori
async def _con_presenza(intest, id_partita, beta, lavoro):
    """
    Chi sta aspettando in questa partita è collegato. Se smette di chiedere (la rete è caduta, il programma si è chiuso) e non ricompare
    entro la grazia, PHP avvisa gli altri al tavolo; quando torna, lo dice PHP stesso.
    """
    sessione = intest.get('x-token', '')
    chiave = (sessione, id_partita, beta) if RE_TOKEN.match(sessione) else None
    valida = False
    if chiave is not None:
        assenze.pop(chiave, None)
        attivi[chiave] = attivi.get(chiave, 0) + 1
    try:
        risposta = await lavoro()
        valida = risposta is None or risposta[0] == 200
        return risposta
    finally:
        if chiave is not None:
            attivi[chiave] -= 1
            if attivi[chiave] <= 0:
                del attivi[chiave]
                if valida and not fermo:
                    assenze[chiave] = time.monotonic() + GRAZIA_ASSENZA


@registra_errori
async def _attesa_stato_interna(parti, coppie, q, attesa, id_partita, intest, beta, lettore, scrittore):
    percorso = os.path.join(DATI[('giochi', beta)], 's_%s.json' % id_partita)
    inizio = time.monotonic()
    firma = (mtime(percorso),)   # PRIMA della prima richiesta: un cambiamento durante la richiesta sveglia subito
    senza_attesa = parti.path + '?' + urlencode([(k, v) for k, v in coppie if k != 'attesa'] + [('attesa', '0')])
    risposta = await inoltra('GET', senza_attesa, intest, b'')
    if risposta[0] != 200:
        return risposta
    stato = _json_di(risposta)
    if stato is None:
        return risposta
    # le stesse ragioni per cui PHP rispondeva subito
    versione_nota = q.get('versione')
    if stato.get('eventi'):
        return risposta
    if versione_nota not in (None, ''):
        try:
            if int(stato.get('versione', -1)) != int(versione_nota):
                return risposta
        except (TypeError, ValueError):
            return risposta
    if len(attese) >= MASSIMO_ATTESE:
        await asyncio.sleep(1)   # troppo pieno: si risponde senza tenere la connessione, chi chiede riprova
        return risposta
    resto = attesa - (time.monotonic() - inizio)
    if resto > 0 and await aspetta([percorso], firma, resto, lettore, scrittore):
        return None
    return await inoltra('GET', senza_attesa, intest, b'')


@registra_errori
async def attesa_chat(target, intest, corpo, beta, lettore, scrittore):
    try:
        c = json.loads(corpo)
    except (ValueError, UnicodeDecodeError):
        return await inoltra('POST', target, intest, corpo)
    if not isinstance(c, dict):
        return await inoltra('POST', target, intest, corpo)
    try:
        attesa = max(0, min(ATTESA_MASSIMA, int(c.get('attesa', 0))))
    except (TypeError, ValueError):
        attesa = 0
    cassette = c.get('cassette') if isinstance(c.get('cassette'), list) else []
    mb = []
    for x in cassette[:64]:
        if isinstance(x, dict) and isinstance(x.get('mb'), str) and RE_CASSETTA.match(x['mb']):
            mb.append(x['mb'].lower())
    if attesa == 0 or not mb or fermo:
        return await inoltra('POST', target, intest, corpo)
    cartella = DATI[('chat', beta)]
    percorsi = [os.path.join(cartella, 'c_%s.sig' % m) for m in mb]
    inizio = time.monotonic()
    firma = tuple(mtime(p) for p in percorsi)
    c0 = dict(c)
    c0['attesa'] = 0
    risposta = await inoltra('POST', target, intest, json.dumps(c0, separators=(',', ':')).encode('utf-8'))
    if risposta[0] != 200:
        return risposta
    d = _json_di(risposta)
    if d is None or d.get('blocchi'):
        return risposta
    if len(attese) >= MASSIMO_ATTESE:
        await asyncio.sleep(1)
        return risposta
    resto = attesa - (time.monotonic() - inizio)
    if resto > 0 and await aspetta(percorsi, firma, resto, lettore, scrittore):
        return None
    c1 = dict(c0)
    c1['ack'] = []   # le conferme sono già arrivate con la prima richiesta
    return await inoltra('POST', target, intest, json.dumps(c1, separators=(',', ':')).encode('utf-8'))


# ---------------------------------------------------------------------- flussi di eventi (server-sent events)
# Al posto di una richiesta che si rinnova ogni 15-20 secondi, il client apre un flusso e il motore gli scrive quando c'è qualcosa:
#   giochi   GET  /giochi/giochi.php?a=flusso&id=..&da=N                  eventi "stato" (lo stesso JSON di a=stato) e righe di segno di vita
#   chat     POST /chat/chat.php?a=flusso {cassette}                       eventi "blocchi" (lo stesso JSON di a=ascolta)
# Le conferme dei blocchi e le mosse viaggiano con le richieste normali. Dietro c'è sempre PHP, che decide e controlla chi è.
# Chi non ha i flussi (il motore non c'è, PHP risponde 404) continua con le attese di prima.
@registra_errori
async def inizia_flusso(scrittore):
    scrittore.write(b'HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\n'
                    b'X-Accel-Buffering: no\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n')
    await scrittore.drain()


@registra_errori
async def invia_evento(scrittore, nome, dati=b''):
    """nome None: una riga di segno di vita. dati: JSON su una riga sola (PHP scrive gli a capo dentro le stringhe come sequenza di escape)."""
    if nome is None:
        blocco = b': ping\n\n'
    else:
        riga = dati if isinstance(dati, bytes) else str(dati).encode('utf-8')
        riga = riga.replace(b'\r', b'').replace(b'\n', b'')
        blocco = b'event: ' + nome.encode('ascii') + b'\ndata: ' + riga + b'\n\n'
    scrittore.write(b'%x\r\n' % len(blocco) + blocco + b'\r\n')
    await scrittore.drain()


@registra_errori
async def chiudi_flusso(scrittore):
    scrittore.write(b'0\r\n\r\n')
    await scrittore.drain()


@registra_errori
def _risposta_json(stato, testo):
    return stato, [('Content-Type', 'application/json; charset=utf-8'), ('Cache-Control', 'no-store')], json.dumps({'errore': testo}).encode('utf-8')


@registra_errori
async def flusso_stato(target, intest, corpo, beta, lettore, scrittore):
    parti = urlsplit(target)
    q = dict(parse_qsl(parti.query, keep_blank_values=True))
    id_partita = q.get('id', '')
    if not RE_ID.match(id_partita):
        return _risposta_json(400, 'Partita non valida.')
    try:
        da = int(q.get('da', '0'))
    except ValueError:
        da = 0
    if fermo or len(attese) >= MASSIMO_ATTESE:
        return _risposta_json(503, 'Il server è pieno: riprova.')
    return await _con_presenza(intest, id_partita, beta, lambda: _flusso_stato_interno(parti, id_partita, da, intest, beta, lettore, scrittore))


@registra_errori
async def _flusso_stato_interno(parti, id_partita, da, intest, beta, lettore, scrittore):
    percorso = os.path.join(DATI[('giochi', beta)], 's_%s.json' % id_partita)

    @registra_errori
    def richiesta(d):
        return parti.path + '?' + urlencode([('a', 'stato'), ('id', id_partita), ('da', str(d)), ('attesa', '0')])

    firma = (mtime(percorso),)   # prima di ogni richiesta a PHP, come nelle attese
    risposta = await inoltra('GET', richiesta(da), intest, b'')
    if risposta[0] != 200:
        return risposta
    stato = _json_di(risposta)
    if stato is None:
        return risposta
    try:
        await inizia_flusso(scrittore)
        await invia_evento(scrittore, 'stato', risposta[2])
        ultimo = int(stato.get('ultimo_evento', da))
        versione = stato.get('versione')
        fine = time.monotonic() + DURATA_FLUSSO
        while not fermo and time.monotonic() < fine:
            if await aspetta([percorso], firma, PING, lettore, scrittore):
                return None
            firma = (mtime(percorso),)
            risposta = await inoltra('GET', richiesta(ultimo), intest, b'')
            if risposta[0] != 200:
                await invia_evento(scrittore, 'errore', json.dumps({'stato': risposta[0]}).encode('utf-8'))
                break
            stato = _json_di(risposta)
            if stato is None:
                continue
            if stato.get('eventi') or stato.get('versione') != versione:
                versione = stato.get('versione')
                ultimo = int(stato.get('ultimo_evento', ultimo))
                await invia_evento(scrittore, 'stato', risposta[2])
            else:
                await invia_evento(scrittore, None)   # PHP è stato interpellato: la presenza è aggiornata
        await chiudi_flusso(scrittore)
    except (ConnectionError, OSError):
        pass
    return None


@registra_errori
async def flusso_chat(target, intest, corpo, beta, lettore, scrittore):
    try:
        c = json.loads(corpo)
    except (ValueError, UnicodeDecodeError):
        return _risposta_json(400, 'Richiesta non valida.')
    cassette = c.get('cassette') if isinstance(c, dict) and isinstance(c.get('cassette'), list) else []
    mb = [x['mb'].lower() for x in cassette[:64] if isinstance(x, dict) and isinstance(x.get('mb'), str) and RE_CASSETTA.match(x['mb'])]
    if not mb:
        return _risposta_json(400, 'Nessuna cassetta.')
    if fermo or len(attese) >= MASSIMO_ATTESE:
        return _risposta_json(503, 'Il server è pieno: riprova.')
    ascolta = urlsplit(target).path + '?a=ascolta'
    percorsi = [os.path.join(DATI[('chat', beta)], 'c_%s.sig' % m) for m in mb]
    corpo0 = json.dumps({'cassette': cassette[:64], 'ack': [], 'attesa': 0}, separators=(',', ':')).encode('utf-8')
    firma = tuple(mtime(p) for p in percorsi)
    risposta = await inoltra('POST', ascolta, intest, corpo0)
    if risposta[0] != 200:
        return risposta
    if _json_di(risposta) is None:
        return risposta
    try:
        await inizia_flusso(scrittore)
        await invia_evento(scrittore, 'blocchi', risposta[2])   # anche vuoto: dice al client che il flusso è vivo
        fine = time.monotonic() + DURATA_FLUSSO
        while not fermo and time.monotonic() < fine:
            if await aspetta(percorsi, firma, PING, lettore, scrittore):
                return None
            firma = tuple(mtime(p) for p in percorsi)
            risposta = await inoltra('POST', ascolta, intest, corpo0)
            if risposta[0] != 200:
                await invia_evento(scrittore, 'errore', json.dumps({'stato': risposta[0]}).encode('utf-8'))
                break
            d = _json_di(risposta)
            if d is not None and d.get('blocchi'):
                await invia_evento(scrittore, 'blocchi', risposta[2])
            else:
                await invia_evento(scrittore, None)
        await chiudi_flusso(scrittore)
    except (ConnectionError, OSError):
        pass
    return None


@registra_errori
async def smista(metodo, target, intest, corpo, lettore, scrittore):
    percorso = urlsplit(target).path
    if percorso == '/__stato' and metodo == 'GET':
        # solo da questo computer (nginx non lo manda qui): quante attese ci sono adesso
        dati = json.dumps({'attese': len(attese), 'massimo': MASSIMO_ATTESE, 'in_partita': len(attivi), 'assenze_in_corso': len(assenze)}).encode('utf-8')
        return 200, [('Content-Type', 'application/json')], dati
    azione = dict(parse_qsl(urlsplit(target).query)).get('a')
    m = RE_GIOCHI.match(percorso)
    if m and metodo == 'GET' and azione == 'stato':
        return await attesa_stato(target, intest, corpo, bool(m.group(1)), lettore, scrittore)
    if m and metodo == 'GET' and azione == 'flusso':
        return await flusso_stato(target, intest, corpo, bool(m.group(1)), lettore, scrittore)
    m = RE_CHAT.match(percorso)
    if m and metodo == 'POST' and azione == 'ascolta':
        return await attesa_chat(target, intest, corpo, bool(m.group(1)), lettore, scrittore)
    if m and metodo == 'POST' and azione == 'flusso':
        return await flusso_chat(target, intest, corpo, bool(m.group(1)), lettore, scrittore)
    return await inoltra(metodo, target, intest, corpo)


# ---------------------------------------------------------------------- il piccolo server HTTP
@registra_errori
async def leggi_richiesta(lettore):
    try:
        testa = await lettore.readuntil(b'\r\n\r\n')
    except (asyncio.IncompleteReadError, asyncio.LimitOverrunError, ConnectionError):
        return None
    righe = testa[:-4].split(b'\r\n')
    m = RE_RIGA.match(righe[0].decode('latin-1'))
    if not m:
        return None
    intest = {}
    for r in righe[1:]:
        nome, _, valore = r.partition(b':')
        intest[nome.strip().lower().decode('latin-1')] = valore.strip().decode('latin-1')
    try:
        lunghezza = int(intest.get('content-length', '0') or 0)
    except ValueError:
        return None
    if lunghezza < 0 or lunghezza > MASSIMO_CORPO:
        return None
    try:
        corpo = await lettore.readexactly(lunghezza) if lunghezza else b''
    except (asyncio.IncompleteReadError, ConnectionError):
        return None
    return m.group(1), m.group(2), intest, corpo


@registra_errori
async def scrivi_risposta(scrittore, stato, intestazioni, corpo, chiudi):
    try:
        frase = HTTPStatus(stato).phrase
    except ValueError:
        frase = 'OK'
    righe = ['HTTP/1.1 %d %s' % (stato, frase)]
    for nome, valore in intestazioni:
        if nome.lower() not in SALTA_RISPOSTA:
            righe.append('%s: %s' % (nome, valore))
    righe.append('Content-Length: %d' % len(corpo))
    righe.append('Connection: ' + ('close' if chiudi else 'keep-alive'))
    scrittore.write(('\r\n'.join(righe) + '\r\n\r\n').encode('latin-1') + corpo)
    await scrittore.drain()


ERRORE_ORIGINE = (502, [('Content-Type', 'application/json; charset=utf-8'), ('Cache-Control', 'no-store')],
                  '{"errore":"Il server non risponde: riprova."}'.encode('utf-8'))


@registra_errori
async def connessione(lettore, scrittore):
    try:
        while not fermo:
            try:
                richiesta = await asyncio.wait_for(leggi_richiesta(lettore), TEMPO_INATTIVITA)
            except asyncio.TimeoutError:
                break
            if richiesta is None:
                break
            metodo, target, intest, corpo = richiesta
            chiudi = intest.get('connection', '').lower() == 'close'
            try:
                esito = await smista(metodo, target, intest, corpo, lettore, scrittore)
            except asyncio.CancelledError:
                raise
            except Exception as e:   # Apache non risponde, risposta strana...: chi chiede riprova
                _log('attesa: %s' % type(e).__name__)
                esito = ERRORE_ORIGINE
            if esito is None:   # chi aspettava se n'è andato
                break
            await scrivi_risposta(scrittore, esito[0], esito[1], esito[2], chiudi)
            if chiudi:
                break
    except (ConnectionError, asyncio.CancelledError):
        pass
    finally:
        try:
            scrittore.close()
        except Exception:
            pass


@registra_errori
async def principale():
    global fermo
    server = await asyncio.start_server(connessione, INDIRIZZO, PORTA, backlog=1024)
    compito = asyncio.ensure_future(sorveglia())
    ferma = asyncio.Event()

    @registra_errori
    def chiedi_stop():
        global fermo
        fermo = True
        for a in list(attese):
            a.evento.set()   # chi aspetta riceve subito la sua risposta e riprova
        ferma.set()

    try:
        ciclo = asyncio.get_running_loop()
        for sig in (signal.SIGTERM, signal.SIGINT):
            ciclo.add_signal_handler(sig, chiedi_stop)
    except (NotImplementedError, AttributeError):
        pass   # Windows (prove): si ferma con Ctrl+C
    _log('attesa: in ascolto su %s:%d' % (INDIRIZZO, PORTA))
    try:
        await ferma.wait()
    finally:
        fermo = True
        server.close()
        compito.cancel()
        await asyncio.sleep(1)   # il tempo di mandare le ultime risposte


if __name__ == '__main__':
    try:
        asyncio.run(principale())
    except KeyboardInterrupt:
        pass
