<?php
// Relè della chat di WinGabriel (WGChat): inoltra blocchi cifrati da un utente all'altro e non ricorda niente.
//
// Cosa sa questo server: nulla di chi scrive e di cosa si scrive. Non ci sono account, nomi, password né elenchi di contatti.
// Vede soltanto, per il tempo di un inoltro, una "cassetta" (un numero casuale) e un blocco di byte che sembrano casuali: tutto il
// resto (chi è il mittente, il testo, la lunghezza esatta, l'ordine) è dentro la cifratura end to end, fatta dai dispositivi.
// Nessun messaggio viene salvato: un blocco sta in memoria (/dev/shm, che non finisce mai su disco) finché il destinatario collegato
// non lo ritira, e comunque non oltre 2 minuti. Se il destinatario non è collegato il blocco non si accetta nemmeno ("non
// disponibile"): chi scrive riprova più tardi, dal suo dispositivo. Qui non si scrivono indirizzi IP né dati di chi scrive: nel file errori.log finiscono solo gli errori tecnici (funzione, tipo di errore e riga del codice).
//
//   POST ?a=ascolta {cassette:[{mb, rd, th}], ack:[id...], attesa:S}  -> {blocchi:[{id, mb, b}]}
//       Il destinatario dice su quali cassette è collegato (mb: numero della cassetta, 32 cifre esadecimali; rd: segreto di lettura,
//       64 cifre; th: impronta SHA-256 del gettone di invio, 64 cifre), conferma i blocchi già salvati (ack) e aspetta fino a S secondi
//       (massimo 20) che ne arrivi qualcuno.
//       Le cassette nuove si aprono solo se la rete di chi chiede ne ha meno di 300 vive (altrimenti 429) e, quando in un'ora ne
//       nascono moltissime, con una prova di calcolo: il relè risponde 428 con {errore, prova:{sfida, bit}} e il client rifà la
//       richiesta con prova:{sfida, nonce}, dove SHA-256("sfida|impronta|nonce") inizia con "bit" bit a zero e impronta è lo SHA-256
//       (esadecimale) dei numeri delle cassette della richiesta in ordine, separati da virgole. Della rete si conserva solo
//       un'impronta HMAC con una chiave che cambia ogni giorno, in memoria: nessun indirizzo IP.
//   POST ?a=invia   {mb, tk, b}                                        -> {ok:true}  oppure errore 404 "non disponibile"
//       Chi scrive porta il numero della cassetta, il gettone di invio (64 cifre) e il blocco in base64. La risposta è sempre la stessa
//       se la cassetta non c'è, il destinatario non è collegato, il gettone è sbagliato o la coda è piena.

declare(strict_types=1);

const SECONDI_CODA = 120;          // un blocco non ritirato sparisce
const SECONDI_PRESENZA = 45;       // collegato = ha chiesto qualcosa negli ultimi 45 secondi (le attese passano dal motore d'attesa: la presenza si aggiorna all'inizio e alla fine di ognuna)
const SECONDI_RIPROVA = 20;        // un blocco consegnato e non confermato si riconsegna dopo 20 secondi
const SECONDI_CASSETTA = 600;      // una cassetta non usata per 10 minuti sparisce
const MASSIMO_CODA = 40;
const MASSIMO_BLOCCO = 16384;      // byte, dopo la decodifica
const MASSIMO_CASSETTE = 64;
const MASSIMO_CASSETTE_TOTALI = 100000;   // in memoria una cassetta pesa poche centinaia di byte
// Difese contro chi apre cassette a raffica per riempire il relè (nessun indirizzo si scrive: la rete diventa un'impronta HMAC con una
// chiave che cambia ogni giorno e sta solo in memoria, come tutto il resto del relè):
const MASSIMO_CASSETTE_RETE = 300;       // cassette vive aperte dalla stessa rete (una famiglia o un ufficio ne usano poche decine)
const NUOVE_SENZA_PROVA_ORA = 1500;      // finché in un'ora nascono meno cassette di così non si chiede niente; oltre serve una prova di calcolo
const SECONDI_SFIDA = 600;               // la sfida della prova vale 10 minuti

/**
 * Registra un errore tecnico nel file errori.log della cartella del relè (in memoria, /dev/shm: sparisce al riavvio), una riga per errore.
 * Nella riga ci sono soltanto data e ora, la funzione, il tipo di errore e il punto del codice. Mai il messaggio dell'errore e mai indirizzi IP,
 * cassette, gettoni o blocchi: la chat non deve lasciare tracce di chi scrive. Non si usa error_log di PHP perché il server web ci aggiungerebbe
 * l'indirizzo di chi chiede.
 */
function registra_errore(string $dove, Throwable $e): void
{
    static $visti = [];
    static $dentro = false;
    $id = spl_object_id($e);
    if ($dentro || isset($visti[$id]) || $e instanceof InvalidArgumentException) {
        return;
    }
    $visti[$id] = true;
    $dentro = true;
    try {
        $riga = date('Y-m-d H:i:s') . ' ' . $dove . ': ' . get_class($e) . ' (' . basename($e->getFile()) . ':' . $e->getLine() . ')' . "\n";
        $file = cartella() . '/errori.log';
        // il registro non cresce oltre un mega: si tiene l'ultima metà
        if (@filesize($file) > 1048576) {
            $vecchio = (string) @file_get_contents($file, false, null, -524288);
            @file_put_contents($file, substr($vecchio, (int) strpos($vecchio, "\n") + 1), LOCK_EX);
        }
        @file_put_contents($file, $riga, FILE_APPEND | LOCK_EX);
        @chmod($file, 0600);
    } catch (Throwable $x) {
        // il registro non deve mai far fallire quello che sta registrando
    } finally {
        $dentro = false;
    }
}

function cartella(): string
{
    try {
        static $c = null;
        if ($c === null) {
            // la copia beta (wingabriel.net/beta/chat) ha il suo relè, separato da quello vero
            $beta = str_contains(str_replace(chr(92), '/', __DIR__), '/beta/');
            $c = getenv('CHAT_DIR') ?: ($beta ? '/dev/shm/wingabriel-chat-beta' : '/dev/shm/wingabriel-chat');
            if (!is_dir($c)) {
                @mkdir($c, 0700, true);
            }
        }
        return $c;
    } catch (Throwable $e) {
        registra_errore('cartella', $e);
        throw $e;
    }
}

function rispondi(array $dati, int $codice = 200): never
{
    try {
        http_response_code($codice);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        header('X-Content-Type-Options: nosniff');
        echo $dati === [] ? '{}' : json_encode($dati, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    } catch (Throwable $e) {
        registra_errore('rispondi', $e);
        throw $e;
    }
}

function non_disponibile(): never
{
    try {
        rispondi(['errore' => 'Non disponibile.'], 404);
    } catch (Throwable $e) {
        registra_errore('non_disponibile', $e);
        throw $e;
    }
}

function corpo(): array
{
    try {
        $testo = file_get_contents('php://input', false, null, 0, 600000);
        $d = $testo === false || $testo === '' ? null : json_decode($testo, true);
        return is_array($d) ? $d : [];
    } catch (Throwable $e) {
        registra_errore('corpo', $e);
        throw $e;
    }
}

function esadecimale(mixed $v, int $lunghezza): bool
{
    try {
        return is_string($v) && strlen($v) === $lunghezza && ctype_xdigit($v);
    } catch (Throwable $e) {
        registra_errore('esadecimale', $e);
        throw $e;
    }
}

function file_cassetta(string $mb): string
{
    try {
        return cartella() . '/c_' . strtolower($mb) . '.json';
    } catch (Throwable $e) {
        registra_errore('file_cassetta', $e);
        throw $e;
    }
}

/** Apre la cassetta (creandola se serve), la blocca, e passa i dati a $fn; $fn restituisce i nuovi dati o null per non cambiare. */
function con_cassetta(string $mb, bool $crea, callable $fn): mixed
{
    try {
        $percorso = file_cassetta($mb);
        if (!$crea && !file_exists($percorso)) {
            return $fn(null);
        }
        $f = @fopen($percorso, 'c+');
        if ($f === false) {
            return $fn(null);
        }
        flock($f, LOCK_EX);
        try {
            $testo = stream_get_contents($f);
            $dati = $testo === '' || $testo === false ? null : json_decode($testo, true);
            $esito = $fn(is_array($dati) ? $dati : null, $nuovi);
            if (isset($nuovi) && is_array($nuovi)) {
                ftruncate($f, 0);
                rewind($f);
                fwrite($f, json_encode($nuovi));
            }
            return $esito;
        } finally {
            flock($f, LOCK_UN);
            fclose($f);
        }
    } catch (Throwable $e) {
        registra_errore('con_cassetta', $e);
        throw $e;
    }
}

/** Il file che dice al motore d'attesa (attesa.py) che nella cassetta è arrivato qualcosa: cambia a ogni blocco consegnato. */
function file_segnale(string $mb): string
{
    try {
        return cartella() . '/c_' . strtolower($mb) . '.sig';
    } catch (Throwable $e) {
        registra_errore('file_segnale', $e);
        throw $e;
    }
}

function pulizia(): void
{
    try {
        foreach (glob(cartella() . '/c_*.json') ?: [] as $f) {
            $t = @file_get_contents($f);
            $d = $t === false ? null : json_decode($t, true);
            if (!is_array($d) || time() - (int) ($d['vista'] ?? 0) > SECONDI_CASSETTA) {
                @unlink($f);
            }
        }
        foreach (glob(cartella() . '/c_*.sig') ?: [] as $f) {
            if (!file_exists(substr($f, 0, -4) . '.json')) {
                @unlink($f);
            }
        }
        // gli elenchi delle reti perdono le cassette sparite; quelli rimasti vuoti si cancellano
        foreach (glob(cartella() . '/r_*.json') ?: [] as $f) {
            $d = json_decode((string) @file_get_contents($f), true);
            // le chiavi sono "m" + numero della cassetta (con la lettera davanti PHP non le trasforma in numeri)
            $vive = is_array($d) ? array_filter($d, fn($t, $k) => is_string($k) && esadecimale(substr($k, 1), 32) && file_exists(file_cassetta(substr($k, 1))), ARRAY_FILTER_USE_BOTH) : [];
            if ($vive === []) {
                @unlink($f);
            } elseif (count($vive) !== count($d)) {
                @file_put_contents($f, json_encode($vive), LOCK_EX);
            }
        }
    } catch (Throwable $e) {
        registra_errore('pulizia', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ difese contro le cassette aperte a raffica

/** 32 byte casuali conservati nella cartella del relè (in memoria): $nome è il file. Se manca o è da rifare ($rifai), si crea. */
function chiave_segreta(string $nome, bool $rifai = false): string
{
    try {
        $f = cartella() . '/' . $nome;
        $letta = $rifai ? '' : (string) @file_get_contents($f);
        if (strlen($letta) === 32) {
            return $letta;
        }
        $nuova = random_bytes(32);
        $tmp = $f . '.' . bin2hex(random_bytes(4));
        file_put_contents($tmp, $nuova, LOCK_EX);
        @chmod($tmp, 0600);
        rename($tmp, $f);
        return (string) @file_get_contents($f);
    } catch (Throwable $e) {
        registra_errore('chiave_segreta', $e);
        throw $e;
    }
}

/**
 * L'impronta della rete di chi chiede (IPv4: l'indirizzo; IPv6: le prime 64 cifre binarie), oppure '' se non la si conosce.
 * Non si scrive mai l'indirizzo: si scrive HMAC-SHA256(chiave del giorno, rete), e la chiave cambia ogni giorno (gli elenchi vecchi
 * diventano illeggibili e si cancellano). Dietro nginx l'indirizzo vero arriva con mod_remoteip; da questo computer non si conta.
 */
function impronta_rete(): string
{
    try {
        $ip = (string) ($_SERVER['REMOTE_ADDR'] ?? '');
        $bin = @inet_pton($ip);
        if ($bin === false || $ip === '127.0.0.1' || $ip === '::1') {
            return '';
        }
        $rete = strlen($bin) === 16 ? substr($bin, 0, 8) : $bin;
        $giorno = gmdate('Ymd');
        $marca = cartella() . '/rete.giorno';
        $rifai = trim((string) @file_get_contents($marca)) !== $giorno;
        if ($rifai) {
            foreach (glob(cartella() . '/r_*.json') ?: [] as $f) {
                @unlink($f);
            }
            @file_put_contents($marca, $giorno, LOCK_EX);
        }
        return substr(hash_hmac('sha256', $rete, chiave_segreta('rete.chiave', $rifai)), 0, 32);
    } catch (Throwable $e) {
        registra_errore('impronta_rete', $e);
        throw $e;
    }
}

/**
 * Segna le cassette nuove nell'elenco della rete. Falso se con queste la rete supererebbe MASSIMO_CASSETTE_RETE cassette vive:
 * chi apre cassette a raffica riempie solo la sua quota, non il relè di tutti.
 */
function quota_rete(string $rete, array $nuove): bool
{
    try {
        if ($rete === '' || $nuove === []) {
            return true;
        }
        $f = @fopen(cartella() . '/r_' . $rete . '.json', 'c+');
        if ($f === false) {
            return true;
        }
        flock($f, LOCK_EX);
        try {
            $d = json_decode((string) stream_get_contents($f), true);
            $d = is_array($d) ? $d : [];
            $vive = array_filter($d, fn($t, $k) => is_string($k) && esadecimale(substr($k, 1), 32) && file_exists(file_cassetta(substr($k, 1))), ARRAY_FILTER_USE_BOTH);
            if (count($vive) + count($nuove) > MASSIMO_CASSETTE_RETE) {
                return false;
            }
            foreach ($nuove as $mb) {
                $vive['m' . $mb] = time();
            }
            ftruncate($f, 0);
            rewind($f);
            fwrite($f, json_encode($vive));
            return true;
        } finally {
            flock($f, LOCK_UN);
            fclose($f);
        }
    } catch (Throwable $e) {
        registra_errore('quota_rete', $e);
        throw $e;
    }
}

/** Gli orari (solo orari) in cui sono nate cassette nell'ultima ora, uno per cassetta: aggiunge $n nascite e restituisce quante sono. */
function nascite_ora(int $n = 0): int
{
    try {
        $f = @fopen(cartella() . '/nascite.json', 'c+');
        if ($f === false) {
            return 0;
        }
        flock($f, LOCK_EX);
        try {
            $t = json_decode((string) stream_get_contents($f), true);
            $limite = time() - 3600;
            $t = array_values(array_filter(is_array($t) ? $t : [], fn($x) => is_int($x) && $x >= $limite));
            if ($n > 0) {
                for ($i = 0; $i < $n; $i++) {
                    $t[] = time();
                }
                $t = array_slice($t, -50000);
                ftruncate($f, 0);
                rewind($f);
                fwrite($f, json_encode($t));
            }
            return count($t);
        } finally {
            flock($f, LOCK_UN);
            fclose($f);
        }
    } catch (Throwable $e) {
        registra_errore('nascite_ora', $e);
        throw $e;
    }
}

/** Quanti zeri iniziali (in bit) deve avere la prova per aprire $nuove cassette: nessuno finché ne nascono poche, sempre di più se aumentano. */
function bit_richiesti(int $nateOra, int $nuove): int
{
    if ($nateOra < NUOVE_SENZA_PROVA_ORA) {
        return 0;
    }
    $base = $nateOra < 3000 ? 16 : ($nateOra < 6000 ? 18 : ($nateOra < 15000 ? 20 : 22));
    // più cassette insieme costano di più: una prova vale per tutte quelle della richiesta
    return min(26, $base + (int) ceil(log(max(1, $nuove), 2)));
}

/** Una sfida firmata (orario, numero casuale, HMAC): non si conserva niente, per riconoscerla basta rifare la firma. */
function nuova_sfida(): string
{
    try {
        $t = (string) time();
        $r = bin2hex(random_bytes(8));
        return $t . '.' . $r . '.' . substr(hash_hmac('sha256', $t . '.' . $r, chiave_segreta('prova.chiave')), 0, 32);
    } catch (Throwable $e) {
        registra_errore('nuova_sfida', $e);
        throw $e;
    }
}

function sfida_valida(string $s): bool
{
    try {
        $p = explode('.', $s);
        if (count($p) !== 3 || !ctype_digit($p[0]) || strlen($p[0]) > 12 || !esadecimale($p[1], 16) || strlen($p[2]) !== 32) {
            return false;
        }
        if (!hash_equals(substr(hash_hmac('sha256', $p[0] . '.' . $p[1], chiave_segreta('prova.chiave')), 0, 32), $p[2])) {
            return false;
        }
        $eta = time() - (int) $p[0];
        return $eta >= -5 && $eta <= SECONDI_SFIDA;
    } catch (Throwable $e) {
        registra_errore('sfida_valida', $e);
        throw $e;
    }
}

function zeri_iniziali(string $binario): int
{
    $n = 0;
    for ($i = 0, $l = strlen($binario); $i < $l; $i++) {
        $b = ord($binario[$i]);
        if ($b === 0) {
            $n += 8;
            continue;
        }
        while (($b & 0x80) === 0) {
            $n++;
            $b <<= 1;
        }
        break;
    }
    return $n;
}

/**
 * La prova (la stessa dei nomi utente dei giochi): un numero esadecimale "nonce" tale che SHA-256 di "sfida|impronta|nonce" abbia almeno
 * $bit bit a zero. L'impronta è lo SHA-256 (esadecimale) dei numeri delle cassette della richiesta, in ordine e separati da virgole:
 * la prova vale solo per quelle.
 */
function prova_valida(string $impronta, mixed $prova, int $bit): bool
{
    try {
        if (!is_array($prova)) {
            return false;
        }
        $sfida = (string) ($prova['sfida'] ?? '');
        $nonce = (string) ($prova['nonce'] ?? '');
        if (!preg_match('/^[0-9a-f]{1,32}$/', $nonce) || !sfida_valida($sfida)) {
            return false;
        }
        return zeri_iniziali(hash('sha256', $sfida . '|' . $impronta . '|' . $nonce, true)) >= $bit;
    } catch (Throwable $e) {
        registra_errore('prova_valida', $e);
        throw $e;
    }
}

/** La pulizia scorre tutti i file: si fa al massimo una volta ogni 30 secondi, non a caso a ogni tanto di richieste. */
function pulizia_se_serve(): void
{
    try {
        $marca = cartella() . '/pulizia.marca';
        if (time() - (int) @filemtime($marca) >= 30 && @touch($marca)) {
            pulizia();
        }
    } catch (Throwable $e) {
        registra_errore('pulizia_se_serve', $e);
        throw $e;
    }
}

function azione_invia(): never
{
    try {
        $c = corpo();
        $mb = $c['mb'] ?? null;
        $tk = $c['tk'] ?? null;
        $b = $c['b'] ?? null;
        if (!esadecimale($mb, 32) || !esadecimale($tk, 64) || !is_string($b) || strlen($b) > 24000) {
            non_disponibile();
        }
        $blocco = base64_decode($b, true);
        if ($blocco === false || strlen($blocco) < 32 || strlen($blocco) > MASSIMO_BLOCCO) {
            non_disponibile();
        }
        $impronta = hash('sha256', (string) hex2bin($tk));
        $id = bin2hex(random_bytes(8));
        $accettato = con_cassetta($mb, false, function (?array $d, &$nuovi = null) use ($impronta, $b, $id) {
            if ($d === null || time() - (int) $d['vista'] > SECONDI_PRESENZA || !hash_equals((string) $d['th'], $impronta) || count($d['coda']) >= MASSIMO_CODA) {
                return false;
            }
            $d['coda'][] = ['id' => $id, 't' => time(), 's' => 0, 'b' => $b];
            $nuovi = $d;
            return true;
        });
        if (!$accettato) {
            non_disponibile();
        }
        // sveglia il motore d'attesa del destinatario (il contenuto cambia sempre, così cambia sempre anche l'ora del file)
        @file_put_contents(file_segnale($mb), bin2hex(random_bytes(4)));
        rispondi(['ok' => true]);
    } catch (Throwable $e) {
        registra_errore('azione_invia', $e);
        throw $e;
    }
}

function azione_ascolta(): never
{
    try {
        $c = corpo();
        $cassette = is_array($c['cassette'] ?? null) ? array_slice($c['cassette'], 0, MASSIMO_CASSETTE) : [];
        $ack = array_flip(array_filter(is_array($c['ack'] ?? null) ? $c['ack'] : [], fn($x) => is_string($x) && ctype_xdigit($x) && strlen($x) === 16));
        $attesa = max(0, min(20, (int) ($c['attesa'] ?? 0)));
        $valide = [];
        foreach ($cassette as $x) {
            if (is_array($x) && esadecimale($x['mb'] ?? null, 32) && esadecimale($x['rd'] ?? null, 64) && esadecimale($x['th'] ?? null, 64)) {
                $valide[] = ['mb' => strtolower($x['mb']), 'rdh' => hash('sha256', (string) hex2bin($x['rd'])), 'th' => strtolower($x['th'])];
            }
        }
        // Le cassette nuove (quelle che il relè non ha ancora) passano dai controlli: tetto di tutto il relè, prova di calcolo quando ne
        // nascono troppe, quota della rete. Il conto delle cassette scorre la cartella: serve solo quando se ne sta per creare una nuova.
        $nuove = [];
        foreach ($valide as $v) {
            if (!file_exists(file_cassetta($v['mb']))) {
                $nuove[$v['mb']] = $v['mb'];
            }
        }
        $nuove = array_values($nuove);
        if ($nuove !== []) {
            if (count(glob(cartella() . '/c_*.json') ?: []) + count($nuove) > MASSIMO_CASSETTE_TOTALI) {
                errore_pieno();
            }
            $bit = bit_richiesti(nascite_ora(), count($nuove));
            if ($bit > 0) {
                $tutte = array_values(array_unique(array_map(fn($v) => $v['mb'], $valide)));
                sort($tutte, SORT_STRING);
                if (!prova_valida(hash('sha256', implode(',', $tutte)), $c['prova'] ?? null, $bit)) {
                    rispondi([
                        'errore' => 'In questo momento il server della chat chiede una piccola verifica: aggiorna WinGabriel, oppure riprova tra un po\'.',
                        'prova' => ['sfida' => nuova_sfida(), 'bit' => $bit],
                    ], 428);
                }
            }
            if (!quota_rete(impronta_rete(), $nuove)) {
                rispondi(['errore' => 'Troppe conversazioni aperte da questa rete: riprova più tardi.'], 429);
            }
            nascite_ora(count($nuove));
        }
        $fine = microtime(true) + $attesa;
        do {
            $blocchi = [];
            foreach ($valide as $v) {
                $r = con_cassetta($v['mb'], true, function (?array $d, &$nuovi = null) use ($v, $ack) {
                    $ora = time();
                    $cambiato = false;
                    if ($d === null) {
                        $d = ['rdh' => $v['rdh'], 'th' => $v['th'], 'vista' => $ora, 'coda' => []];
                        $cambiato = true;
                    } elseif (!hash_equals((string) $d['rdh'], $v['rdh'])) {
                        return []; // un'altra persona ha già questa cassetta: non è la tua
                    }
                    if ($d['th'] !== $v['th']) {
                        $d['th'] = $v['th'];
                        $cambiato = true;
                    }
                    // la presenza si riscrive ogni 5 secondi, non a ogni giro dell'attesa
                    if ($ora - (int) ($d['vista'] ?? 0) >= 5) {
                        $d['vista'] = $ora;
                        $cambiato = true;
                    }
                    $coda = [];
                    $pronti = [];
                    foreach ($d['coda'] as $m) {
                        if (isset($ack[$m['id']]) || $ora - (int) $m['t'] > SECONDI_CODA) {
                            $cambiato = true;
                            continue;
                        }
                        if ((int) $m['s'] === 0 || $ora - (int) $m['s'] > SECONDI_RIPROVA) {
                            $m['s'] = $ora;
                            $pronti[] = ['id' => $m['id'], 'mb' => $v['mb'], 'b' => $m['b']];
                            $cambiato = true;
                        }
                        $coda[] = $m;
                    }
                    if ($cambiato) {
                        $d['coda'] = $coda;
                        $nuovi = $d;
                    }
                    return $pronti;
                });
                foreach ($r as $m) {
                    $blocchi[] = $m;
                }
            }
            $ack = []; // le conferme valgono una volta sola
            if ($blocchi || microtime(true) >= $fine) {
                break;
            }
            usleep(50000);
        } while (true);
        pulizia_se_serve();
        rispondi(['blocchi' => $blocchi]);
    } catch (Throwable $e) {
        registra_errore('azione_ascolta', $e);
        throw $e;
    }
}

function errore_pieno(): never
{
    try {
        rispondi(['errore' => 'Il server è pieno, riprova più tardi.'], 503);
    } catch (Throwable $e) {
        registra_errore('errore_pieno', $e);
        throw $e;
    }
}

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    rispondi(['errore' => 'Richiesta sconosciuta.'], 405);
}
try {
    switch ((string) ($_GET['a'] ?? '')) {
        case 'invia':
            azione_invia();
        case 'ascolta':
            azione_ascolta();
        default:
            rispondi(['errore' => 'Richiesta sconosciuta.'], 404);
    }
} catch (Throwable $e) {
    registra_errore('principale', $e);
    rispondi(['errore' => 'Errore del server.'], 500);
}
