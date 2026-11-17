<?php
// Gira come root ogni minuto (wingabriel-segnalazioni.timer): manda per mail a Gabriel le segnalazioni che segnalazioni.php ha messo in coda
// e cancella ogni file appena la mail è partita. Usa msmtp con la configurazione di root (/etc/msmtprc), che PHP non può leggere.
// Nel registro (/var/lib/wingabriel-segnalazioni/invio.log) finiscono solo gli esiti tecnici, mai i testi.

declare(strict_types=1);

// L'indirizzo che riceve le segnalazioni sta solo sul server, in /etc/wingabriel-segnalazioni.destinatario.
const FILE_DESTINATARIO = '/etc/wingabriel-segnalazioni.destinatario';
const MITTENTE = 'postmaster@gabrielpappalardo.name';
const DIR = '/var/lib/wingabriel-segnalazioni';
const TIPI = ['problema' => 'Problema', 'suggerimento' => 'Suggerimento', 'altro' => 'Altro'];

function registro(string $riga): void
{
    $file = DIR . '/invio.log';
    if (@filesize($file) > 262144) {
        @file_put_contents($file, '', LOCK_EX);
    }
    @file_put_contents($file, date('Y-m-d H:i:s') . ' ' . $riga . "\n", FILE_APPEND | LOCK_EX);
}

/** L'indirizzo che riceve le segnalazioni (solo sul server); vuoto se manca o non è un indirizzo di posta valido. */
function destinatario(): string
{
    $a = trim((string) @file_get_contents(FILE_DESTINATARIO));
    return filter_var($a, FILTER_VALIDATE_EMAIL) !== false ? $a : '';
}

function invia(string $oggetto, string $testo): bool
{
    try {
        if (destinatario() === '') {
            registro('manca ' . FILE_DESTINATARIO . ': le segnalazioni restano in coda');
            return false;
        }
        $intestazioni = 'From: Segnalazioni WinGabriel <' . MITTENTE . ">\r\n"
            . 'To: ' . destinatario() . "\r\n"
            . 'Subject: =?UTF-8?B?' . base64_encode($oggetto) . "?=\r\n"
            . 'Date: ' . date('r') . "\r\n"
            . "MIME-Version: 1.0\r\n"
            . "Content-Type: text/plain; charset=UTF-8\r\n"
            . "Content-Transfer-Encoding: base64\r\n\r\n";
        $messaggio = $intestazioni . chunk_split(base64_encode($testo), 76, "\r\n");
        $p = proc_open(['/usr/bin/msmtp', '-C', '/etc/msmtprc', '-a', 'aruba', destinatario()], [0 => ['pipe', 'r'], 1 => ['pipe', 'w'], 2 => ['pipe', 'w']], $pipe);
        if (!is_resource($p)) {
            return false;
        }
        fwrite($pipe[0], $messaggio);
        fclose($pipe[0]);
        stream_get_contents($pipe[1]);
        stream_get_contents($pipe[2]);
        fclose($pipe[1]);
        fclose($pipe[2]);
        return proc_close($p) === 0;
    } catch (Throwable $e) {
        registro('invia: ' . get_class($e) . ' (riga ' . $e->getLine() . ')');
        return false;
    }
}

try {
    $lock = fopen(DIR . '/invio.lock', 'c');
    if ($lock === false || !flock($lock, LOCK_EX | LOCK_NB)) {
        exit(0);
    }
    $file = glob(DIR . '/coda/s_*.json') ?: [];
    sort($file);
    foreach (array_slice($file, 0, 20) as $f) {
        if (is_link($f) || !is_file($f) || filesize($f) > 20000) {
            @unlink($f);
            continue;
        }
        $d = json_decode((string) file_get_contents($f), true);
        $tipo = (is_array($d) && is_string($d['tipo'] ?? null) && isset(TIPI[$d['tipo']])) ? $d['tipo'] : '';
        if ($tipo === '' || !is_string($d['testo'] ?? null)) {
            registro('file non valido, tolto');
            @unlink($f);
            continue;
        }
        $versione = (is_string($d['versione'] ?? null) && preg_match('/^[\p{L}\p{N}._ -]{1,40}$/u', $d['versione'])) ? $d['versione'] : 'non indicata';
        $quando = is_string($d['data'] ?? null) ? $d['data'] : '';
        $corpo = "Segnalazione anonima di un utente di WinGabriel\n"
            . 'Tipo: ' . TIPI[$tipo] . "\n"
            . 'Versione di WinGabriel: ' . $versione . "\n"
            . 'Data: ' . $quando . "\n\n"
            . $d['testo'] . "\n";
        if (invia('Segnalazione WinGabriel: ' . strtolower(TIPI[$tipo]), $corpo)) {
            @unlink($f);
            registro('inviata una segnalazione');
        } else {
            registro('invio non riuscito, riprovo al prossimo giro');
            break;
        }
    }
} catch (Throwable $e) {
    registro('errore ' . get_class($e) . ' (riga ' . $e->getLine() . ')');
    exit(1);
}
