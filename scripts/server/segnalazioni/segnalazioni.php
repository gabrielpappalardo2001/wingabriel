<?php
// Segnalazioni anonime degli utenti di WinGabriel: problemi, suggerimenti e altro.
//
// Protezione dei dati: la segnalazione è anonima. Non c'è nessun account e il sito non scrive gli indirizzi IP (le richieste a questo file
// non vanno nei registri di nginx e di Apache). Di ogni segnalazione si conservano solo il tipo, il testo, la versione di WinGabriel e la data,
// in un piccolo file nella coda: lo script di root invia-segnalazioni.php lo manda per mail a Gabriel e lo cancella subito.
// Il testo non può contenere nomi: il programma controlla quelli che conosce (nome dell'utente, del computer, dell'account) e qui si
// rifiutano indirizzi di posta, indirizzi internet, numeri di telefono, indirizzi IP e frasi con cui ci si presenta.
// Nel file errori.log finiscono solo gli errori tecnici (funzione, tipo e riga), mai i testi.
//
//   POST ?a=segnalazione {tipo, testo, versione}  -> {ok:true}   tipo: problema | suggerimento | altro
//   Errori: 400 (segnalazione non valida, con il motivo in "errore"), 429 (troppe segnalazioni), 503 (coda piena)

declare(strict_types=1);

const TIPI = ['problema', 'suggerimento', 'altro'];
const MIN_CARATTERI = 10;
const MAX_CARATTERI = 3000;
const MAX_ORA = 30;
const MAX_GIORNO = 150;
const MAX_CODA = 300;

function cartella(): string
{
    $c = getenv('WG_SEGNALAZIONI_DIR');
    return ($c !== false && $c !== '') ? $c : '/var/lib/wingabriel-segnalazioni';
}

function registra_errore(string $dove, Throwable $e): void
{
    try {
        $riga = date('Y-m-d H:i:s') . ' ' . $dove . ': ' . get_class($e) . ' (' . basename($e->getFile()) . ':' . $e->getLine() . ')' . "\n";
        $file = cartella() . '/errori.log';
        if (@filesize($file) > 262144) {
            @file_put_contents($file, '', LOCK_EX);
        }
        @file_put_contents($file, $riga, FILE_APPEND | LOCK_EX);
        @chmod($file, 0600);
    } catch (Throwable $x) {
        // il registro non deve mai far fallire quello che sta registrando
    }
}

function rispondi(int $codice, array $dati): void
{
    http_response_code($codice);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    echo json_encode($dati, JSON_UNESCAPED_UNICODE);
    exit;
}

/** Il motivo per cui il testo non va bene, oppure '' se va bene. */
function controlla_testo(string $testo): string
{
    try {
        if (preg_match('/[^\s@]+@[^\s@]+/u', $testo) || preg_match('/\b[\w.-]+\s*(?:chiocciola|\[at\]|\(at\))\s*[\w.-]+/iu', $testo)) {
            return 'La segnalazione contiene un indirizzo di posta elettronica. Toglilo e riprova.';
        }
        if (preg_match('~(?:https?://|ftp://|www\.)~iu', $testo) || preg_match('/\b[a-z0-9-]+\.(?:it|com|net|org|eu|info|io|me|app|dev)\b/iu', $testo)) {
            return 'La segnalazione contiene un indirizzo internet. Toglilo e riprova.';
        }
        $senzaDate = preg_replace('/\b\d{1,2}[\/.]\d{1,2}[\/.]\d{2,4}\b/', ' ', $testo) ?? $testo;
        if (preg_match('/(?:\+|00)?\d(?:[\s.\-\/]?\d){7,}/', $senzaDate)) {
            return 'La segnalazione contiene un numero di telefono o un altro numero lungo. Toglilo e riprova.';
        }
        if (preg_match('/\b\d{1,3}(?:\.\d{1,3}){3}\b/', $testo) || preg_match('/\b(?:[0-9a-f]{1,4}:){3,}[0-9a-f]{1,4}\b/i', $testo)) {
            return 'La segnalazione contiene un indirizzo di rete. Toglilo e riprova.';
        }
        if (preg_match('/\b[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z]\b/i', $testo)) {
            return 'La segnalazione contiene un codice fiscale. Toglilo e riprova.';
        }
        if (preg_match('/\b(?:mi\s+chiamo|mi\s+chiamano|il\s+mio\s+(?:nome|cognome)|mi\s+presento|mi\s+firmo|cordiali\s+saluti|distinti\s+saluti)\b/iu', $testo)) {
            return 'La segnalazione non può contenere il nome di chi la scrive. Toglilo e riprova.';
        }
        return '';
    } catch (Throwable $e) {
        registra_errore('controlla_testo', $e);
        throw $e;
    }
}

/** Conta una segnalazione in più nell'ora e nel giorno; false se si è oltre il limite. Il conteggio è un elenco di orari, senza altro. */
function conta_segnalazione(string $dir): bool
{
    try {
        $file = $dir . '/orari.json';
        $f = fopen($file, 'c+');
        if ($f === false) {
            throw new RuntimeException('orari');
        }
        try {
            flock($f, LOCK_EX);
            $elenco = json_decode((string) stream_get_contents($f), true);
            $elenco = is_array($elenco) ? $elenco : [];
            $adesso = time();
            $elenco = array_values(array_filter($elenco, static fn($t) => is_int($t) && $t > $adesso - 86400));
            $ultimaOra = count(array_filter($elenco, static fn($t) => $t > $adesso - 3600));
            if ($ultimaOra >= MAX_ORA || count($elenco) >= MAX_GIORNO) {
                return false;
            }
            $elenco[] = $adesso;
            ftruncate($f, 0);
            rewind($f);
            fwrite($f, json_encode($elenco));
            return true;
        } finally {
            flock($f, LOCK_UN);
            fclose($f);
        }
    } catch (Throwable $e) {
        registra_errore('conta_segnalazione', $e);
        throw $e;
    }
}

function gestisci(): void
{
    try {
        if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST' || ($_GET['a'] ?? '') !== 'segnalazione') {
            rispondi(404, ['errore' => 'Non trovato']);
        }
        $corpo = (string) file_get_contents('php://input', false, null, 0, 16384);
        $dati = json_decode($corpo, true);
        if (!is_array($dati)) {
            rispondi(400, ['errore' => 'Segnalazione non valida.']);
        }
        $tipo = is_string($dati['tipo'] ?? null) ? $dati['tipo'] : '';
        $testo = is_string($dati['testo'] ?? null) ? trim($dati['testo']) : '';
        $versione = is_string($dati['versione'] ?? null) ? $dati['versione'] : '';
        if (!in_array($tipo, TIPI, true)) {
            rispondi(400, ['errore' => 'Tipo di segnalazione non valido.']);
        }
        if (!preg_match('/^[\p{L}\p{N}._ -]{1,40}$/u', $versione)) {
            $versione = '';
        }
        $testo = preg_replace('/[^\P{C}\n\t]/u', '', $testo) ?? '';
        $lunghezza = mb_strlen($testo, 'UTF-8');
        if ($lunghezza < MIN_CARATTERI) {
            rispondi(400, ['errore' => 'La segnalazione è troppo breve.']);
        }
        if ($lunghezza > MAX_CARATTERI) {
            rispondi(400, ['errore' => 'La segnalazione è troppo lunga.']);
        }
        $motivo = controlla_testo($testo);
        if ($motivo !== '') {
            rispondi(400, ['errore' => $motivo]);
        }
        $dir = cartella();
        $coda = $dir . '/coda';
        if (!is_dir($coda) && !@mkdir($coda, 0770, true) && !is_dir($coda)) {
            throw new RuntimeException('coda');
        }
        if (count(glob($coda . '/s_*.json') ?: []) >= MAX_CODA) {
            rispondi(503, ['errore' => 'Al momento non si possono ricevere segnalazioni: riprova più tardi.']);
        }
        if (!conta_segnalazione($dir)) {
            rispondi(429, ['errore' => 'Sono arrivate troppe segnalazioni: riprova più tardi.']);
        }
        $nome = $coda . '/s_' . date('YmdHis') . '_' . bin2hex(random_bytes(6)) . '.json';
        $scritto = file_put_contents($nome . '.tmp', json_encode(
            ['tipo' => $tipo, 'testo' => $testo, 'versione' => $versione, 'data' => date('c')],
            JSON_UNESCAPED_UNICODE
        ), LOCK_EX);
        if ($scritto === false || !rename($nome . '.tmp', $nome)) {
            throw new RuntimeException('scrittura');
        }
        @chmod($nome, 0660);
        rispondi(200, ['ok' => true]);
    } catch (Throwable $e) {
        registra_errore('gestisci', $e);
        rispondi(500, ['errore' => 'Non è stato possibile ricevere la segnalazione.']);
    }
}

if (PHP_SAPI !== 'cli') {
    gestisci();
}
