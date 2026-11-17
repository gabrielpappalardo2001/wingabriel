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
const MASSIMO_CASSETTE_TOTALI = 20000;

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
    } catch (Throwable $e) {
        registra_errore('pulizia', $e);
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
        // il conto delle cassette scorre la cartella: serve solo quando se ne sta per creare una nuova
        $nuova = false;
        foreach ($valide as $v) {
            if (!file_exists(file_cassetta($v['mb']))) {
                $nuova = true;
                break;
            }
        }
        if ($nuova && count(glob(cartella() . '/c_*.json') ?: []) >= MASSIMO_CASSETTE_TOTALI) {
            errore_pieno();
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
