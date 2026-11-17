<?php
// Server dei giochi di WinGabriel (WGGiochi): burraco, monopoli, briscola e scopa, contro il computer o contro altri giocatori.
//
// Protezione dei dati: ogni nome utente ha una password. Non si entra senza. Niente email, niente altri dati personali.
// Di ogni nome utente si conserva soltanto il nome, l'impronta del derivato della password (la password non si salva mai in chiaro:
// sul server resta solo l'impronta, e all'accesso si riconosce quell'utente), la data dell'ultimo accesso, l'elenco di chi ha bloccato
// e, se la persona lo vuole, il codice pubblico per scriverle in chat. Un nome utente vecchio, creato senza password, non entra finché
// non ne scrive una: in quel momento gli viene assegnata. Un nome utente non usato per un anno si cancella da solo, e chi vuole può
// cancellarlo subito (elimina_account). Le partite e i messaggi della chat stanno soltanto in piccoli file temporanei e spariscono da
// soli: una partita e la sua chat 15 minuti dopo la fine o 3 ore dopo l'ultima mossa. Qui non si scrivono indirizzi IP: nel file errori.log finiscono solo gli errori tecnici (funzione, tipo di errore e riga del codice), mai dati dei giocatori.
//
// Tutte le risposte sono JSON. Dopo l'ingresso ogni richiesta porta l'intestazione X-Token. Se la richiesta ha un corpo è JSON.
//   POST ?a=registra       {nome, auth[, prova]}           -> {token, nome, ha_password}   crea un nome utente (nome 2-20 caratteri, password obbligatoria)
//       Se in un'ora se ne creano troppi (20 o più) risponde 428 con {errore, prova:{sfida, bit}}: il client trova un numero esadecimale "nonce"
//       tale che SHA-256("sfida|nome in minuscolo|nonce") inizi con "bit" bit a zero e rifà la richiesta con prova:{sfida, nonce}.
//       Oltre 600 al giorno risponde 503 (si riprova domani). Un nome utente nuovo (meno di un giorno) ha meno ricerche e meno inviti.
//   POST ?a=entra          {nome, auth}                    -> {token, nome, ha_password}
//       Senza auth non si entra e non si crea niente. Se il nome non ha ancora una password e si scrive auth (o la vecchia password
//       in chiaro), gli viene assegnata e si entra con quell'utente. Se ce l'ha già, va scritta.
//       "auth" non è la password: è il suo derivato (PBKDF2-SHA256, 600000 giri, sale "WGAccount-v1|nome in minuscolo", 64 byte; i primi 32
//       in esadecimale sono l'auth, gli altri 32 restano sul dispositivo e proteggono la chat). Il server non vede mai la password.
//       Per i nomi utente creati prima di questa regola si accetta una volta la vecchia {nome, password}, e si convertono.
//   POST ?a=elimina_account {auth}                         -> {}     cancella il nome utente e lascia le partite (richiede X-Token)
//   POST ?a=chat_pubblica  {codice}                        -> {}     rende il proprio codice della chat trovabile per nome utente
//   POST ?a=chat_ritira                                    -> {}     toglie il codice: non ci si trova più per nome
//   POST ?a=cerca          {nome}                          -> {nome, codice}  il codice della chat di una persona (o 404 "Non disponibile" per ogni motivo)
//   POST ?a=blocca {nome} / ?a=sblocca {nome} / GET ?a=bloccati  -> {bloccati:[...]}  chi blocchi non può entrare nelle tue partite, trovarti né scriverti
//   GET  ?a=in_linea                                       -> {utenti:[...]}  nomi visti negli ultimi due minuti, senza se stessi e senza i bloccati
//   GET  ?a=giochi                                         -> {giochi:[{id, nome, posti:[...], descrizione}]}
//   GET  ?a=stanze                                         -> {stanze:[{id, gioco, nome_gioco, creatore, posti, occupati, stato}]}
//   GET  ?a=tavoli[&gioco=ID]                              -> {tavoli:[...]}  i tavoli in attesa di quel gioco (o di tutti). invitato=true se ti hanno chiamato
//   GET  ?a=inviti                                         -> {inviti:[...]}  i tavoli a cui sei stato invitato e che sono ancora aperti, con un posto libero
//   GET  ?a=mia                                            -> {id}   la partita (non finita) a cui partecipi, oppure id null: serve per riprendere dopo una disconnessione
//   POST ?a=crea           {gioco, posti, computer, punti} -> {id, link}   crea un tavolo. computer=true: gli altri posti li occupa il computer e si parte subito; punti: obiettivo del burraco (500-5005, di solito 2005)
//   POST ?a=invita         {id, nome}                      -> {nome}  invita un giocatore al tavolo (sei seduto, il tavolo è in attesa e c'è un posto)
//   POST ?a=rifiuta_invito {id}                            -> {}     toglie il tuo invito a quel tavolo
//       "link" è l'indirizzo web da condividere (https://wingabriel.net/giochi/?p=ID): chi lo apre entra nella stessa partita dal browser.
//   POST ?a=entra_stanza   {id}                            -> {id}
//   POST ?a=avvia          {id}                            -> {}     (chi ha creato la partita: i posti liberi li prende il computer)
//   POST ?a=lascia         {id}                            -> {}
//   GET  ?a=stato&id=ID&da=N[&attesa=S][&situazione=1]     -> lo stato per chi chiede (vedi sotto); con attesa aspetta fino a S secondi (max 20)
//   GET  ?a=flusso&id=ID&da=N      (non è di PHP: lo gestisce il motore d'attesa, attesa.py, che risponde con eventi "stato", uno per ogni novità,
//       e un segno di vita ogni 15 secondi; senza il motore PHP risponde 404 e i client ripiegano su a=stato con attesa)
//   POST ?a=presenza       {id}                            -> {}     lo manda il motore a nome di un giocatore che ha perso il collegamento
//   POST ?a=mossa          {id, mossa[, da, mid]}          -> come stato   mid: numero proprio della mossa (8-40 caratteri): se si rimanda, la mossa non si rifà
//   POST ?a=chat           {id, testo}                     -> {}
// Lo stato contiene: versione, stato ('attesa'|'in corso'|'finita'), posto (il tuo), turno, giocatori, eventi nuovi (dopo da) che puoi
// vedere (messaggi di gioco, chat, avvisi), mosse (solo se tocca a te: id e testo da mostrare), vista (le carte, il tavolo...), link
// (l'indirizzo web da condividere) e, se richiesto, situazione (una descrizione parlata di mano e tavolo).
// La pagina web accetta ?p=ID: dopo l'accesso entra in quella partita, così chi vede e chi non vede giocano insieme.

declare(strict_types=1);

require __DIR__ . '/burraco.php';
require __DIR__ . '/monopoli.php';
require __DIR__ . '/briscola.php';
require __DIR__ . '/scopa.php';
require __DIR__ . '/battaglia.php';

const GIOCHI = [
    'burraco' => [
        'nome' => 'Burraco', 'classe' => 'Burraco', 'posti' => [2, 4],
        'descrizione' => 'Burraco italiano, uno contro uno o a coppie in quattro, a punti (di solito 2005), con minimo per aprire e pozzo congelato.',
        'varianti' => [
            'classica' => ['nome' => 'Classica', 'descrizione' => 'Partita tradizionale a punti a 2 o 4 giocatori.'],
        ],
    ],
    'monopoli' => [
        'nome' => 'Monopoli', 'classe' => 'Monopoli', 'posti' => [2, 3, 4],
        'descrizione' => 'Monopoli da due a quattro giocatori, con aste, ipoteche, costruzioni e scambi. Vince l\'ultimo rimasto o chi ha più patrimonio.',
        'varianti' => Monopoli::VARIANTI,
    ],
    'briscola' => [
        'nome' => 'Briscola', 'classe' => 'Briscola', 'posti' => [2, 3, 4, 5],
        'descrizione' => 'Briscola con mazzo da 40 carte, classica, al meglio di 3, a chiamata a 5, Marianna con accusi, scoperta o a 3.',
        'varianti' => Briscola::VARIANTI,
    ],
    'battaglia' => [
        'nome' => 'Battaglia navale', 'classe' => 'BattagliaNavale', 'posti' => [2],
        'descrizione' => 'Battaglia navale per due: ognuno posiziona la sua flotta sul casellario, poi si spara a turno dicendo una casella (colonna e riga) finché una flotta è affondata. Anche contro il computer.',
        'varianti' => BattagliaNavale::VARIANTI,
    ],
    'scopa' => [
        'nome' => 'Scopa', 'classe' => 'Scopa', 'posti' => [2, 4],
        'descrizione' => 'Scopa con mazzo da 40 carte, classica, a 21 punti, scopone scientifico, asso pigliatutto, napola, re bello o a 15.',
        'varianti' => Scopa::VARIANTI,
    ],
];

const NOMI_COMPUTER = ['Aldo', 'Bruna', 'Carlo', 'Dora', 'Enzo', 'Fulvia', 'Gino', 'Livia'];
const SECONDI_NOME = 2592000;      // la sessione dura 30 giorni senza attività
const SECONDI_ACCOUNT_INATTIVO = 31536000; // un nome utente non usato per un anno si cancella
const PROVE_PASSWORD = 5;           // dopo 5 password sbagliate il nome utente si blocca per un po'
const SECONDI_BLOCCO = 900;
const MASSIMO_ACCOUNT = 20000;
// Difese contro chi crea nomi utente a raffica (nessun indirizzo IP si scrive qui: lo limita il server web, in memoria):
const MASSIMO_REGISTRAZIONI_GIORNO = 600;   // nuovi nomi utente al giorno in tutto il server: oltre, si riprova domani
const REGISTRAZIONI_SENZA_PROVA_ORA = 20;   // finché in un'ora se ne creano meno di così non si chiede niente; oltre serve una piccola prova di calcolo
const SECONDI_SFIDA = 600;                  // la sfida della prova vale 10 minuti
const SECONDI_ACCOUNT_MAI_USATO = 5184000;  // un nome utente creato e mai più usato (nessun accesso, blocco, codice o sessione) si cancella dopo 60 giorni
const SECONDI_ACCOUNT_NUOVO = 86400;        // per un giorno un nome utente è "nuovo": ricerche e inviti hanno limiti più stretti
const RICERCHE_MASSIME_NUOVO = 5;           // ricerche per nome ogni 10 minuti, per un nome utente nuovo
const INVITI_ORA_NUOVO = 10;                // inviti ai tavoli in un'ora, per un nome utente nuovo
const MASSIMO_BLOCCATI = 200;
const RICERCHE_MASSIME = 30;       // ricerche per nome ogni 10 minuti, per non poter elencare gli utenti
const SECONDI_STANZA_FERMA = 10800; // 3 ore
const SECONDI_STANZA_FINITA = 900;  // 15 minuti
const SECONDI_ASSENTE = 240;       // dopo 4 minuti senza richieste, tocca al computer
const SECONDI_SEGNALA_ASSENZA = 15; // chi ha dato segno di vita da meno di così non si segnala come scollegato
// La versione di WinGabriel consigliata (numero come 3.0.12.0): se il programma di chi gioca è più vecchio gli dice di aggiornarsi. Vuota: nessun avviso.
const CLIENT_CONSIGLIATO = '';
const MASSIMO_STANZE = 300;
const MASSIMO_GIOCATORI = 1000;
const MASSIMO_INVITI = 30;

/** True se questo giochi.php sta nella copia beta del sito (wingabriel.net/beta/giochi). */
function copia_beta(): bool
{
    try {
        return str_contains(str_replace(chr(92), '/', __DIR__), '/beta/');
    } catch (Throwable $e) {
        registra_errore('copia_beta', $e);
        throw $e;
    }
}

/**
 * Registra un errore tecnico nel file errori.log della cartella dei dati dei giochi (una riga per errore, una sola volta per eccezione).
 * Nella riga ci sono soltanto data e ora, la funzione, il tipo di errore, il suo messaggio e il punto del codice: mai indirizzi IP, nomi,
 * token, mosse o altri dati dei giocatori. Non si usa error_log di PHP perché il server web ci aggiungerebbe l'indirizzo di chi chiede.
 * Le eccezioni di validazione (InvalidArgumentException: una mossa non ammessa, un dato sbagliato) non sono errori del server.
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
        $riga = date('Y-m-d H:i:s') . ' ' . $dove . ': ' . get_class($e) . ': ' . substr(str_replace(["\r", "\n"], ' ', $e->getMessage()), 0, 200) . ' (' . basename($e->getFile()) . ':' . $e->getLine() . ')' . "\n";
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
            // la copia beta (wingabriel.net/beta/giochi) ha le sue partite e i suoi utenti, separati da quelli veri
            $c = getenv('GIOCHI_DIR') ?: (copia_beta() ? '/var/lib/wingabriel-giochi-beta' : '/var/lib/wingabriel-giochi');
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
        // una risposta vuota è un oggetto, non un elenco
        echo $dati === [] ? '{}' : json_encode($dati, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    } catch (Throwable $e) {
        registra_errore('rispondi', $e);
        throw $e;
    }
}

function errore(string $testo, int $codice = 400): never
{
    try {
        rispondi(['errore' => $testo], $codice);
    } catch (Throwable $e) {
        registra_errore('errore', $e);
        throw $e;
    }
}

function corpo(): array
{
    try {
        static $c = null;
        if ($c === null) {
            // Limite esplicito (come in chat.php e segnalazioni.php): senza non si dipende solo da post_max_size
            // di php.ini, e un corpo enorme non viene letto in memoria prima di essere scartato.
            $testo = file_get_contents('php://input', false, null, 0, 262144);
            $letto = $testo === false || $testo === '' ? [] : json_decode($testo, true);
            $c = is_array($letto) ? $letto : [];
        }
        return $c;
    } catch (Throwable $e) {
        registra_errore('corpo', $e);
        throw $e;
    }
}

function leggi_file(string $percorso): ?array
{
    try {
        $testo = @file_get_contents($percorso);
        if ($testo === false) {
            return null;
        }
        $d = json_decode($testo, true);
        return is_array($d) ? $d : null;
    } catch (Throwable $e) {
        registra_errore('leggi_file', $e);
        throw $e;
    }
}

function scrivi_file(string $percorso, array $dati): void
{
    try {
        $GLOBALS['wg_memo_account'] = [];   // quello che si era letto in questa richiesta potrebbe non valere più
        $tmp = $percorso . '.' . bin2hex(random_bytes(4)) . '.tmp';
        file_put_contents($tmp, json_encode($dati, JSON_UNESCAPED_UNICODE));
        if (!@rename($tmp, $percorso)) {
            @unlink($percorso);
            rename($tmp, $percorso);
        }
    } catch (Throwable $e) {
        registra_errore('scrivi_file', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ giocatori
function file_giocatore(string $token): string
{
    try {
        return cartella() . '/p_' . $token . '.json';
    } catch (Throwable $e) {
        registra_errore('file_giocatore', $e);
        throw $e;
    }
}

/** Il file che resta al posto della sessione quando la stessa persona entra da un altro apparecchio: così chi viene escluso lo sa. */
function file_sostituita(string $token): string
{
    try {
        return cartella() . '/x_' . $token . '.json';
    } catch (Throwable $e) {
        registra_errore('file_sostituita', $e);
        throw $e;
    }
}

/**
 * La sessione di chi chiede. $aggiorna falso: si riconosce chi è senza segnare che è presente adesso (serve a chi segnala l'assenza di un altro).
 * L'account si controlla ogni 2 minuti, non a ogni richiesta (quando si cancella un account le sue sessioni si cancellano con lui).
 */
function giocatore_corrente(bool $obbligatorio = true, bool $aggiorna = true): ?array
{
    try {
        $token = (string) ($_SERVER['HTTP_X_TOKEN'] ?? '');
        if (!preg_match('/^[0-9a-f]{32}$/', $token)) {
            if ($obbligatorio) {
                errore('Prima devi entrare con il tuo nome utente e la password.', 401);
            }
            return null;
        }
        $g = leggi_file(file_giocatore($token));
        if ($g === null) {
            if ($obbligatorio) {
                if (file_exists(file_sostituita($token))) {
                    rispondi(['errore' => 'Sei entrato da un altro apparecchio con questo nome utente.', 'sostituita' => true], 401);
                }
                errore('La sessione è scaduta: entra di nuovo con il tuo nome utente e la password.', 401);
            }
            return null;
        }
        $adesso = time();
        $daSalvare = false;
        if ($adesso - (int) ($g['acc'] ?? 0) >= 120) {
            $account = carica_account((string) ($g['nome'] ?? ''));
            if ($account === null || !ha_password($account)) {
                if ($obbligatorio) {
                    errore('Prima devi entrare con il tuo nome utente e la password.', 401);
                }
                return null;
            }
            $g['acc'] = $adesso;
            $daSalvare = true;
        }
        if ($aggiorna && $adesso - (int) $g['visto'] >= 5) {
            $g['visto'] = $adesso;
            $daSalvare = true;
        }
        if ($daSalvare && $aggiorna) {
            scrivi_file(file_giocatore($token), $g);
        }
        $g['token'] = $token;
        return $g;
    } catch (Throwable $e) {
        registra_errore('giocatore_corrente', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ la partita di ogni sessione
/** Il numero della partita in cui la sessione ha un posto: un piccolo file a parte, così non si scorrono tutte le partite. null = non si sa. */
function file_indice(string $token): string
{
    try {
        return cartella() . '/i_' . $token . '.txt';
    } catch (Throwable $e) {
        registra_errore('file_indice', $e);
        throw $e;
    }
}

function indice_leggi(string $token): ?string
{
    try {
        $t = @file_get_contents(file_indice($token));
        return $t === false ? null : trim($t);
    } catch (Throwable $e) {
        registra_errore('indice_leggi', $e);
        throw $e;
    }
}

function indice_scrivi(string $token, string $idStanza): void
{
    try {
        @file_put_contents(file_indice($token), $idStanza);
    } catch (Throwable $e) {
        registra_errore('indice_scrivi', $e);
        throw $e;
    }
}

/**
 * La partita non finita in cui questa sessione ha un posto (o lo riavrà, se il computer l'ha sostituita), oppure null.
 * Si guarda il numero scritto per la sessione e si controlla nella partita; se non c'è scritto niente (sessioni nate prima di questa
 * novità) si scorrono le partite una volta, e il risultato si scrive.
 */
function stanza_della_sessione(string $token): ?array
{
    try {
        $id = indice_leggi($token);
        if ($id !== null) {
            if ($id === '') {
                return null;
            }
            $s = id_valido($id) ? leggi_file(file_stanza($id)) : null;
            if ($s !== null && $s['stato'] !== 'finita' && (posto_di($s, $token) >= 0 || posto_in_rientro($s, $token) >= 0)) {
                return $s;
            }
            // la partita è finita, sparita o non è più la sua: non ne ha
            indice_scrivi($token, '');
            return null;
        }
        foreach (elenco_stanze() as $s) {
            if ($s['stato'] !== 'finita' && (posto_di($s, $token) >= 0 || posto_in_rientro($s, $token) >= 0)) {
                indice_scrivi($token, (string) $s['id']);
                return $s;
            }
        }
        indice_scrivi($token, '');
        return null;
    } catch (Throwable $e) {
        registra_errore('stanza_della_sessione', $e);
        throw $e;
    }
}

function elenco_giocatori(): array
{
    try {
        $r = [];
        foreach (glob(cartella() . '/p_*.json') ?: [] as $f) {
            $g = leggi_file($f);
            if ($g !== null) {
                $r[] = $g;
            }
        }
        return $r;
    } catch (Throwable $e) {
        registra_errore('elenco_giocatori', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ nomi utente
function file_account(string $nome): string
{
    try {
        return cartella() . '/u_' . hash('sha256', mb_strtolower($nome)) . '.json';
    } catch (Throwable $e) {
        registra_errore('file_account', $e);
        throw $e;
    }
}

function nome_dal_corpo(): string
{
    try {
        $nome = trim((string) (corpo()['nome'] ?? ''));
        $nome = preg_replace('/\s+/u', ' ', $nome) ?? '';
        $lunghezza = mb_strlen($nome);
        if ($lunghezza < 2 || $lunghezza > 20 || !preg_match('/^[\p{L}\p{N} ._-]+$/u', $nome)) {
            errore('Il nome utente deve avere da 2 a 20 caratteri: lettere, numeri, spazi, punto, trattino.');
        }
        return $nome;
    } catch (Throwable $e) {
        registra_errore('nome_dal_corpo', $e);
        throw $e;
    }
}

/** Vero se al nome utente è già stata assegnata una password. */
function ha_password(array $a): bool
{
    try {
        return isset($a['impronta']) && is_string($a['impronta']) && $a['impronta'] !== '';
    } catch (Throwable $e) {
        registra_errore('ha_password', $e);
        throw $e;
    }
}

/** Il derivato della password (64 cifre esadecimali), calcolato dal programma o dal browser. */
function auth_dal_corpo(): ?string
{
    try {
        $a = corpo()['auth'] ?? null;
        return is_string($a) && strlen($a) === 64 && ctype_xdigit($a) ? strtolower($a) : null;
    } catch (Throwable $e) {
        registra_errore('auth_dal_corpo', $e);
        throw $e;
    }
}

/** Per i nomi utente vecchi: la stessa derivazione dei client, fatta qui una volta sola sulla vecchia password. */
function deriva_auth(string $password, string $nome): string
{
    try {
        $r = hash_pbkdf2('sha256', $password, 'WGAccount-v1|' . mb_strtolower($nome), 600000, 64, true);
        return bin2hex(substr($r, 0, 32));
    } catch (Throwable $e) {
        registra_errore('deriva_auth', $e);
        throw $e;
    }
}

/**
 * Controlla nome utente e derivato della password. Dopo troppi errori il nome utente si blocca per qualche minuto. Restituisce l'account.
 * $vecchia: la vecchia password in chiaro, accettata una sola volta per i nomi utente creati prima del derivato (poi si convertono).
 */
function verifica_credenziali(string $nome, ?string $auth, ?string $vecchia = null): array
{
    try {
        $percorso = file_account($nome);
        $blocco = fopen(cartella() . '/u_accessi.lock', 'c');
        if ($blocco === false) {
            errore('Server occupato.', 503);
        }
        flock($blocco, LOCK_EX);
        try {
            $a = leggi_file($percorso);
            if ($a === null) {
                // stesso tempo di una verifica vera, per non far capire se il nome utente esiste
                password_verify((string) $auth, '$2y$10$5xG7R43ve8.v30wAqNbhn.BHg8KmwNi/S3kIGpELzNajY5EY0qAca');
                errore('Nome utente o password non corretti.', 401);
            }
            if ((int) ($a['blocco_fino'] ?? 0) > time()) {
                errore('Troppi tentativi con la password sbagliata: riprova tra qualche minuto.', 429);
            }
            $buono = false;
            if (!ha_password($a)) {
                // Nome utente vecchio, senza password: scrivendola gli viene assegnata. Senza non si entra.
                if ($auth !== null) {
                    $a['impronta'] = password_hash($auth, PASSWORD_DEFAULT);
                    $a['v'] = 2;
                    $buono = true;
                } elseif ($vecchia !== null && $vecchia !== '') {
                    if (strlen($vecchia) < 8 || strlen($vecchia) > 300) {
                        errore('La password è troppo corta: servono almeno 8 caratteri.');
                    }
                    $a['impronta'] = password_hash(deriva_auth($vecchia, (string) $a['nome']), PASSWORD_DEFAULT);
                    $a['v'] = 2;
                    $buono = true;
                } else {
                    errore('Scrivi la password.', 401);
                }
            } else {
                $nuovo = (int) ($a['v'] ?? 1) >= 2;
                if ($nuovo) {
                    $buono = $auth !== null && password_verify($auth, (string) $a['impronta']);
                } elseif ($vecchia !== null && $vecchia !== '' && strlen($vecchia) <= 300 && password_verify($vecchia, (string) $a['impronta'])) {
                    $buono = true;
                    $a['impronta'] = password_hash(deriva_auth($vecchia, (string) $a['nome']), PASSWORD_DEFAULT);
                    $a['v'] = 2;
                }
                if (!$buono && $auth === null && ($vecchia === null || $vecchia === '')) {
                    errore('Questo nome utente ha una password: scrivila.', 401);
                }
            }
            if (!$buono) {
                $a['falliti'] = (int) ($a['falliti'] ?? 0) + 1;
                if ($a['falliti'] >= PROVE_PASSWORD) {
                    $a['falliti'] = 0;
                    $a['blocco_fino'] = time() + SECONDI_BLOCCO;
                }
                scrivi_file($percorso, $a);
                errore('Nome utente o password non corretti.', 401);
            }
            $a['falliti'] = 0;
            $a['blocco_fino'] = 0;
            $a['ultimo'] = time();
            scrivi_file($percorso, $a);
            return $a;
        } finally {
            flock($blocco, LOCK_UN);
            fclose($blocco);
        }
    } catch (Throwable $e) {
        registra_errore('verifica_credenziali', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ account: dati, blocchi, codice della chat
function carica_account(string $nome): ?array
{
    try {
        // dentro la stessa richiesta lo stesso account si legge una volta sola (scrivi_file azzera quello che si era letto)
        $p = file_account($nome);
        if (!isset($GLOBALS['wg_memo_account']) || !array_key_exists($p, $GLOBALS['wg_memo_account'])) {
            $GLOBALS['wg_memo_account'][$p] = leggi_file($p);
        }
        return $GLOBALS['wg_memo_account'][$p];
    } catch (Throwable $e) {
        registra_errore('carica_account', $e);
        throw $e;
    }
}

/** Legge, modifica e salva un account con un blocco, così due richieste insieme non si pestano i piedi. */
function modifica_account(string $nome, callable $fn): mixed
{
    try {
        $blocco = fopen(cartella() . '/u_accessi.lock', 'c');
        if ($blocco === false) {
            errore('Server occupato.', 503);
        }
        flock($blocco, LOCK_EX);
        try {
            $a = leggi_file(file_account($nome));
            if ($a === null) {
                errore('Nome utente non trovato.', 404);
            }
            $r = $fn($a);
            scrivi_file(file_account($nome), $a);
            return $r;
        } finally {
            flock($blocco, LOCK_UN);
            fclose($blocco);
        }
    } catch (Throwable $e) {
        registra_errore('modifica_account', $e);
        throw $e;
    }
}

function bloccati_di(string $nome): array
{
    try {
        $a = carica_account($nome);
        return is_array($a['bloccati'] ?? null) ? $a['bloccati'] : [];
    } catch (Throwable $e) {
        registra_errore('bloccati_di', $e);
        throw $e;
    }
}

/** Vero se uno dei due ha bloccato l'altro. */
function bloccato_tra(string $a, string $b): bool
{
    try {
        if (mb_strtolower($a) === mb_strtolower($b)) {
            return false;
        }
        return in_array(mb_strtolower($b), bloccati_di($a), true) || in_array(mb_strtolower($a), bloccati_di($b), true);
    } catch (Throwable $e) {
        registra_errore('bloccato_tra', $e);
        throw $e;
    }
}

function azione_chat_pubblica(): never
{
    try {
        $g = giocatore_corrente();
        $codice = strtoupper(trim((string) (corpo()['codice'] ?? '')));
        if (!preg_match('/^WGC1-[A-Z2-7]{1,6}(-[A-Z2-7]{1,6}){10,30}$/', $codice) || strlen($codice) > 220) {
            errore('Il codice non è valido.');
        }
        modifica_account((string) $g['nome'], function (array &$a) use ($codice) {
            $a['chat'] = ['codice' => $codice, 'quando' => time()];
        });
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_chat_pubblica', $e);
        throw $e;
    }
}

function azione_chat_ritira(): never
{
    try {
        $g = giocatore_corrente();
        modifica_account((string) $g['nome'], function (array &$a) {
            unset($a['chat']);
        });
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_chat_ritira', $e);
        throw $e;
    }
}

function azione_cerca(): never
{
    try {
        $g = giocatore_corrente();
        $nome = nome_dal_corpo();
        // ricerche limitate: non si può scorrere l'elenco degli utenti
        $troppe = modifica_account((string) $g['nome'], function (array &$a) {
            $adesso = time();
            $ricerche = array_values(array_filter(is_array($a['ricerche'] ?? null) ? $a['ricerche'] : [], fn($t) => $adesso - (int) $t < 600));
            // un nome utente creato da meno di un giorno ha meno ricerche: chi ne crea a raffica non può passare in rassegna gli altri
            $massimo = $adesso - (int) ($a['creato'] ?? 0) < SECONDI_ACCOUNT_NUOVO ? RICERCHE_MASSIME_NUOVO : RICERCHE_MASSIME;
            if (count($ricerche) >= $massimo) {
                $a['ricerche'] = $ricerche;
                return true;
            }
            $ricerche[] = $adesso;
            $a['ricerche'] = $ricerche;
            return false;
        });
        if ($troppe) {
            errore('Troppe ricerche: riprova tra qualche minuto.', 429);
        }
        $altro = carica_account($nome);
        if ($altro === null || !is_array($altro['chat'] ?? null) || bloccato_tra((string) $g['nome'], (string) $altro['nome']) || mb_strtolower((string) $altro['nome']) === mb_strtolower((string) $g['nome'])) {
            rispondi(['errore' => 'Non disponibile.'], 404);
        }
        rispondi(['nome' => $altro['nome'], 'codice' => $altro['chat']['codice']]);
    } catch (Throwable $e) {
        registra_errore('azione_cerca', $e);
        throw $e;
    }
}

function azione_blocca(): never
{
    try {
        $g = giocatore_corrente();
        $nome = mb_strtolower(nome_dal_corpo());
        if ($nome === mb_strtolower((string) $g['nome'])) {
            errore('Non puoi bloccare te stesso.');
        }
        $elenco = modifica_account((string) $g['nome'], function (array &$a) use ($nome) {
            $b = is_array($a['bloccati'] ?? null) ? $a['bloccati'] : [];
            if (!in_array($nome, $b, true)) {
                if (count($b) >= MASSIMO_BLOCCATI) {
                    errore('Hai già bloccato troppe persone.');
                }
                $b[] = $nome;
            }
            $a['bloccati'] = $b;
            return $b;
        });
        rispondi(['bloccati' => $elenco]);
    } catch (Throwable $e) {
        registra_errore('azione_blocca', $e);
        throw $e;
    }
}

function azione_sblocca(): never
{
    try {
        $g = giocatore_corrente();
        $nome = mb_strtolower(nome_dal_corpo());
        $elenco = modifica_account((string) $g['nome'], function (array &$a) use ($nome) {
            $b = array_values(array_filter(is_array($a['bloccati'] ?? null) ? $a['bloccati'] : [], fn($x) => $x !== $nome));
            $a['bloccati'] = $b;
            return $b;
        });
        rispondi(['bloccati' => $elenco]);
    } catch (Throwable $e) {
        registra_errore('azione_sblocca', $e);
        throw $e;
    }
}

function azione_bloccati(): never
{
    try {
        $g = giocatore_corrente();
        rispondi(['bloccati' => bloccati_di((string) $g['nome'])]);
    } catch (Throwable $e) {
        registra_errore('azione_bloccati', $e);
        throw $e;
    }
}

/** Chi sta usando WinGabriel adesso: sessioni viste negli ultimi due minuti, senza se stessi e senza chi è bloccato. */
function azione_in_linea(): never
{
    try {
        $g = giocatore_corrente();
        $io = mb_strtolower((string) $g['nome']);
        $miei = bloccati_di((string) $g['nome']);
        $nomi = [];
        foreach (istantanea_in_linea() as $chiave => $x) {
            if ($chiave === $io || in_array($chiave, $miei, true) || in_array($io, $x['b'], true)) {
                continue;
            }
            $nomi[$chiave] = $x['n'];
        }
        rispondi(['utenti' => array_values($nomi)]);
    } catch (Throwable $e) {
        registra_errore('azione_in_linea', $e);
        throw $e;
    }
}

/**
 * Chi è in linea (visto negli ultimi due minuti) con l'elenco di chi ha bloccato, rifatta al massimo ogni 10 secondi e tenuta in un file:
 * scorrere tutti i giocatori e i loro account a ogni richiesta costava sempre di più al crescere degli utenti.
 */
function istantanea_in_linea(): array
{
    try {
        $f = cartella() . '/in_linea.json';
        $c = leggi_file($f);
        if ($c !== null && time() - (int) ($c['t'] ?? 0) < 10 && is_array($c['g'] ?? null)) {
            return $c['g'];
        }
        $r = [];
        foreach (elenco_giocatori() as $altro) {
            if (time() - (int) ($altro['visto'] ?? 0) > 120) {
                continue;
            }
            $nome = trim((string) ($altro['nome'] ?? ''));
            if ($nome === '') {
                continue;
            }
            $r[mb_strtolower($nome)] = ['n' => $nome, 'b' => bloccati_di($nome)];
        }
        scrivi_file($f, ['t' => time(), 'g' => $r]);
        return $r;
    } catch (Throwable $e) {
        registra_errore('istantanea_in_linea', $e);
        throw $e;
    }
}

/** Le partite di una sessione vecchia dello stesso nome utente passano alla nuova (si può entrare da un altro apparecchio). */
function trasferisci_sessione(string $vecchio, string $nuovo): void
{
    try {
        foreach (glob(cartella() . '/s_*.json') ?: [] as $f) {
            $s = leggi_file($f);
            if ($s === null) {
                continue;
            }
            $coinvolta = false;
            foreach ($s['posti'] as $p) {
                if ($p !== null && (($p['token'] ?? null) === $vecchio && !$p['bot'] || ($p['rientro'] ?? null) === $vecchio)) {
                    $coinvolta = true;
                }
            }
            if (!$coinvolta) {
                continue;
            }
            $id = (string) $s['id'];
            try {
                con_stanza($id, function (array &$st) use ($vecchio, $nuovo) {
                    foreach ($st['posti'] as $i => $p) {
                        if ($p !== null && !$p['bot'] && $p['token'] === $vecchio) {
                            $st['posti'][$i]['token'] = $nuovo;
                        } elseif ($p !== null && $p['bot'] && ($p['rientro'] ?? null) === $vecchio) {
                            $st['posti'][$i]['rientro'] = $nuovo;
                        }
                    }
                });
            } catch (Throwable $e) {
                // la partita è sparita nel frattempo
                registra_errore('trasferisci_sessione (partita)', $e);
            }
        }
    } catch (Throwable $e) {
        registra_errore('trasferisci_sessione', $e);
        throw $e;
    }
}

function apri_sessione(string $nome): never
{
    try {
        // si contano solo le sessioni viste nell'ultimo giorno: quelle ferme (per esempio di nomi utente creati a raffica) non bloccano gli altri
        $esistenti = array_filter(elenco_giocatori(), fn($x) => time() - (int) ($x['visto'] ?? 0) < 86400);
        if (count($esistenti) >= MASSIMO_GIOCATORI) {
            errore('Il server è pieno, riprova più tardi.', 503);
        }
        $token = bin2hex(random_bytes(16));
        foreach (glob(cartella() . '/p_*.json') ?: [] as $f) {
            $g = leggi_file($f);
            if ($g !== null && mb_strtolower((string) $g['nome']) === mb_strtolower($nome)) {
                $vecchio = substr(basename($f), 2, 32);
                trasferisci_sessione($vecchio, $token);
                // la partita della vecchia sessione è anche quella della nuova
                $indice = indice_leggi($vecchio);
                if ($indice !== null) {
                    indice_scrivi($token, $indice);
                    @unlink(file_indice($vecchio));
                }
                // chi usava la vecchia sessione, al prossimo colpo, saprà che è stato sostituito (e non riprenderà il posto da solo)
                @file_put_contents(file_sostituita($vecchio), (string) time());
                @unlink($f);
            }
        }
        scrivi_file(file_giocatore($token), ['nome' => $nome, 'visto' => time(), 'ultima_chat' => 0, 'acc' => time()]);
        $account = carica_account($nome);
        rispondi(['token' => $token, 'nome' => $nome, 'ha_password' => $account !== null && ha_password($account)]);
    } catch (Throwable $e) {
        registra_errore('apri_sessione', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ difese contro le registrazioni a raffica
/** Quanti nomi utente ci sono: un numero tenuto da pulizia() (e aumentato a ogni creazione), così non si scorre la cartella a ogni registrazione. */
function numero_account(): int
{
    try {
        $v = trim((string) @file_get_contents(cartella() . '/n_account.txt'));
        if ($v !== '' && ctype_digit($v)) {
            return (int) $v;
        }
        return count(glob(cartella() . '/u_*.json') ?: []);
    } catch (Throwable $e) {
        registra_errore('numero_account', $e);
        throw $e;
    }
}

/** Gli orari (solo orari: né nomi né indirizzi) delle registrazioni delle ultime 24 ore. */
function registrazioni_recenti(): array
{
    try {
        $d = leggi_file(cartella() . '/registrazioni.json');
        $t = is_array($d['t'] ?? null) ? $d['t'] : [];
        $limite = time() - 86400;
        return array_values(array_filter($t, fn($x) => (int) $x >= $limite));
    } catch (Throwable $e) {
        registra_errore('registrazioni_recenti', $e);
        throw $e;
    }
}

function conta_registrazioni(int $secondi): int
{
    try {
        $limite = time() - $secondi;
        return count(array_filter(registrazioni_recenti(), fn($x) => (int) $x >= $limite));
    } catch (Throwable $e) {
        registra_errore('conta_registrazioni', $e);
        throw $e;
    }
}

function segna_registrazione(): void
{
    try {
        $blocco = fopen(cartella() . '/registrazioni.lock', 'c');
        if ($blocco === false) {
            return;
        }
        flock($blocco, LOCK_EX);
        try {
            $t = registrazioni_recenti();
            $t[] = time();
            if (count($t) > 5000) {
                $t = array_slice($t, -5000);
            }
            scrivi_file(cartella() . '/registrazioni.json', ['t' => $t]);
        } finally {
            flock($blocco, LOCK_UN);
            fclose($blocco);
        }
    } catch (Throwable $e) {
        registra_errore('segna_registrazione', $e);
        throw $e;
    }
}

/** La chiave segreta con cui il server firma le sfide (32 byte casuali, nella cartella dei dati). */
function chiave_prova(): string
{
    try {
        static $k = null;
        if ($k !== null) {
            return $k;
        }
        $f = cartella() . '/chiave_prova.bin';
        for ($giro = 0; $giro < 3; $giro++) {
            $letta = (string) @file_get_contents($f);
            if (strlen($letta) === 32) {
                return $k = $letta;
            }
            $h = @fopen($f, 'x');
            if ($h !== false) {
                $nuova = random_bytes(32);
                fwrite($h, $nuova);
                fclose($h);
                @chmod($f, 0600);
                return $k = $nuova;
            }
            usleep(50000);   // un'altra richiesta la sta scrivendo proprio adesso
        }
        throw new RuntimeException('chiave della prova non disponibile');
    } catch (Throwable $e) {
        registra_errore('chiave_prova', $e);
        throw $e;
    }
}

/** Una sfida: orario, numero casuale e firma. Non si conserva niente sul server: basta rifare la firma per riconoscerla. */
function nuova_sfida(): string
{
    try {
        $t = (string) time();
        $r = bin2hex(random_bytes(8));
        return $t . '.' . $r . '.' . substr(hash_hmac('sha256', $t . '.' . $r, chiave_prova()), 0, 32);
    } catch (Throwable $e) {
        registra_errore('nuova_sfida', $e);
        throw $e;
    }
}

function sfida_valida(string $s): bool
{
    try {
        $p = explode('.', $s);
        if (count($p) !== 3 || !ctype_digit($p[0]) || strlen($p[0]) > 12 || strlen($p[1]) !== 16 || !ctype_xdigit($p[1]) || strlen($p[2]) !== 32) {
            return false;
        }
        $atteso = substr(hash_hmac('sha256', $p[0] . '.' . $p[1], chiave_prova()), 0, 32);
        if (!hash_equals($atteso, $p[2])) {
            return false;
        }
        $eta = time() - (int) $p[0];
        return $eta >= -5 && $eta <= SECONDI_SFIDA;
    } catch (Throwable $e) {
        registra_errore('sfida_valida', $e);
        throw $e;
    }
}

/** Quanti zeri iniziali (in bit) deve avere la prova: nessuno finché le registrazioni sono poche, sempre di più se aumentano. */
function bit_richiesti(int $registrazioniOra): int
{
    if ($registrazioniOra < REGISTRAZIONI_SENZA_PROVA_ORA) {
        return 0;
    }
    if ($registrazioniOra < 60) {
        return 18;
    }
    if ($registrazioniOra < 150) {
        return 20;
    }
    return $registrazioniOra < 400 ? 22 : 24;
}

function zeri_iniziali(string $binario): int
{
    try {
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
    } catch (Throwable $e) {
        registra_errore('zeri_iniziali', $e);
        throw $e;
    }
}

/**
 * La prova: il client trova un numero (nonce, in esadecimale minuscolo) tale che SHA-256 di "sfida|nome in minuscolo|nonce" inizi con
 * almeno $bit bit a zero. Chi crea un solo nome utente ci mette pochi secondi, chi ne crea migliaia no. Legata al nome: non si riusa per altri.
 */
function prova_valida(string $nome, mixed $prova, int $bit): bool
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
        return zeri_iniziali(hash('sha256', $sfida . '|' . mb_strtolower($nome) . '|' . $nonce, true)) >= $bit;
    } catch (Throwable $e) {
        registra_errore('prova_valida', $e);
        throw $e;
    }
}

/** Prima di creare un nome utente: il tetto di oggi e, se se ne creano troppi, la prova di calcolo (428 con la sfida). */
function controlla_registrazione(string $nome): void
{
    try {
        if (conta_registrazioni(86400) >= MASSIMO_REGISTRAZIONI_GIORNO) {
            errore('Oggi sono stati creati troppi nuovi nomi utente: riprova domani.', 503);
        }
        $bit = bit_richiesti(conta_registrazioni(3600));
        if ($bit > 0 && !prova_valida($nome, corpo()['prova'] ?? null, $bit)) {
            rispondi([
                'errore' => 'In questo momento il server chiede una piccola verifica per creare un nome utente nuovo. Aggiorna WinGabriel, oppure riprova tra un po\'.',
                'prova' => ['sfida' => nuova_sfida(), 'bit' => $bit],
            ], 428);
        }
    } catch (Throwable $e) {
        registra_errore('controlla_registrazione', $e);
        throw $e;
    }
}

/** Crea il nome utente. L'impronta è l'unica forma in cui la password viene salvata. */
function crea_account(string $nome, string $auth): bool
{
    try {
        if (numero_account() >= MASSIMO_ACCOUNT) {
            errore('Il server è pieno, riprova più tardi.', 503);
        }
        $f = @fopen(file_account($nome), 'x');
        if ($f === false) {
            return false;
        }
        fwrite($f, json_encode([
            'v' => 2,
            'nome' => $nome,
            'impronta' => password_hash($auth, PASSWORD_DEFAULT),
            'creato' => time(),
            'ultimo' => time(),
            'falliti' => 0,
            'blocco_fino' => 0,
            'bloccati' => [],
        ], JSON_UNESCAPED_UNICODE));
        fclose($f);
        segna_registrazione();
        @file_put_contents(cartella() . '/n_account.txt', (string) (numero_account() + 1), LOCK_EX);   // pulizia() lo riallinea ogni minuto
        return true;
    } catch (Throwable $e) {
        registra_errore('crea_account', $e);
        throw $e;
    }
}

function azione_registra(): never
{
    try {
        $nome = nome_dal_corpo();
        $auth = auth_dal_corpo();
        if ($auth === null) {
            errore('Scrivi una password, almeno 8 caratteri.');
        }
        if (is_file(file_account($nome))) {
            $a = leggi_file(file_account($nome));
            if ($a !== null && !ha_password($a)) {
                $account = verifica_credenziali($nome, $auth, null);
                apri_sessione((string) $account['nome']);
            }
            errore('Questo nome utente esiste già: scegline un altro, oppure entra con la sua password.', 409);
        }
        controlla_registrazione($nome);
        if (!crea_account($nome, $auth)) {
            errore('Questo nome utente esiste già: scegline un altro, oppure entra con la sua password.', 409);
        }
        apri_sessione($nome);
    } catch (Throwable $e) {
        registra_errore('azione_registra', $e);
        throw $e;
    }
}

function azione_entra(): never
{
    try {
        $nome = nome_dal_corpo();
        $auth = auth_dal_corpo();
        $vecchia = is_string(corpo()['password'] ?? null) ? (string) corpo()['password'] : null;
        if ($auth === null && ($vecchia === null || $vecchia === '')) {
            errore('Scrivi la password.', 401);
        }
        if (!is_file(file_account($nome))) {
            errore('Nome utente o password non corretti.', 401);
        }
        $account = verifica_credenziali($nome, $auth, $vecchia);
        apri_sessione((string) $account['nome']);
    } catch (Throwable $e) {
        registra_errore('azione_entra', $e);
        throw $e;
    }
}

function azione_elimina_account(): never
{
    try {
        $g = giocatore_corrente();
        $presente = carica_account((string) $g['nome']);
        if ($presente === null) {
            errore('Nome utente non trovato.', 404);
        }
        $vecchia = is_string(corpo()['password'] ?? null) ? (string) corpo()['password'] : null;
        $account = verifica_credenziali((string) $g['nome'], auth_dal_corpo(), $vecchia);
        foreach (elenco_stanze() as $s) {
            foreach ($s['posti'] as $p) {
                if ($p !== null && !$p['bot'] && $p['token'] === $g['token']) {
                    lascia_stanza((string) $s['id'], $g);
                    break;
                }
            }
        }
        foreach (glob(cartella() . '/p_*.json') ?: [] as $f) {
            $x = leggi_file($f);
            if ($x !== null && mb_strtolower((string) $x['nome']) === mb_strtolower((string) $account['nome'])) {
                @unlink(file_indice(substr(basename($f), 2, 32)));
                @unlink($f);
            }
        }
        @unlink(file_account((string) $account['nome']));
        $GLOBALS['wg_memo_account'] = [];
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_elimina_account', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ stanze
function file_stanza(string $id): string
{
    try {
        return cartella() . '/s_' . $id . '.json';
    } catch (Throwable $e) {
        registra_errore('file_stanza', $e);
        throw $e;
    }
}

function id_valido(string $id): bool
{
    try {
        return (bool) preg_match('/^[A-Z0-9]{5}$/', $id);
    } catch (Throwable $e) {
        registra_errore('id_valido', $e);
        throw $e;
    }
}

/** Partite iniziate prima di una novità del gioco: si aggiungono i campi nuovi con il loro valore di partenza. */
function normalizza_stanza(array &$stanza): void
{
    try {
        if (isset($stanza['gioco_stato']) && is_array($stanza['gioco_stato']) && ($stanza['gioco_stato']['tipo'] ?? '') === 'monopoli' && !array_key_exists('scambio', $stanza['gioco_stato'])) {
            $stanza['gioco_stato']['scambio'] = null;
        }
        if (!isset($stanza['invitati']) || !is_array($stanza['invitati'])) {
            $stanza['invitati'] = [];
        }
        if (!isset($stanza['variante'])) {
            $stanza['variante'] = 'classica';
        }
    } catch (Throwable $e) {
        registra_errore('normalizza_stanza', $e);
        throw $e;
    }
}

/** Monopoli: vero se il turno è finito ma è rimasto aperto (partite cominciate quando serviva ancora un comando per terminarlo). */
function monopoli_turno_aperto(array $stanza): bool
{
    try {
        $gs = $stanza['gioco_stato'] ?? null;
        return ($stanza['stato'] ?? '') === 'in corso' && ($stanza['gioco'] ?? '') === 'monopoli' && is_array($gs)
            && ($gs['fase'] ?? '') === 'fine' && empty($gs['finita']) && ($gs['asta'] ?? null) === null && ($gs['scambio'] ?? null) === null;
    } catch (Throwable $e) {
        registra_errore('monopoli_turno_aperto', $e);
        throw $e;
    }
}

/** Esegue $fn con la stanza bloccata e la salva. $fn riceve la stanza per riferimento. */
function con_stanza(string $id, callable $fn): mixed
{
    try {
        if (!id_valido($id) || !file_exists(file_stanza($id))) {
            errore('Partita non trovata.', 404);
        }
        $blocco = fopen(cartella() . '/s_' . $id . '.lock', 'c');
        if ($blocco === false) {
            errore('Server occupato.', 503);
        }
        flock($blocco, LOCK_EX);
        try {
            $stanza = leggi_file(file_stanza($id));
            if ($stanza === null) {
                errore('Partita non trovata.', 404);
            }
            normalizza_stanza($stanza);
            if (monopoli_turno_aperto($stanza)) {
                $eventiTurno = [];
                Monopoli::chiudiTurnoSeFinito($stanza['gioco_stato'], $eventiTurno);
                aggiungi_eventi($stanza, $eventiTurno);
                fai_giocare_computer($stanza);
            }
            $risultato = $fn($stanza);
            $stanza['aggiornata'] = time();
            $stanza['versione'] = (int) $stanza['versione'] + 1;
            scrivi_file(file_stanza($id), $stanza);
            return $risultato;
        } finally {
            flock($blocco, LOCK_UN);
            fclose($blocco);
        }
    } catch (Throwable $e) {
        registra_errore('con_stanza', $e);
        throw $e;
    }
}

/**
 * Il suono tipico di un evento (il nome di un file in suoni/, senza .wav), oppure null se l'evento non ne ha.
 * Le carte (briscola, scopa, burraco) fanno i rumori delle carte; il monopoli ha i suoi (dadi, soldi, case, prigione...).
 * Lo decide il server, così il programma e la versione web suonano lo stesso suono per lo stesso fatto.
 */
function suono_evento(string $gioco, string $testo, string $tipo, $per): ?string
{
    try {
        if ($tipo !== 'gioco' || $gioco === 'battaglia') {
            return null;   // la battaglia navale dà i suoi suoni negli eventi (campo "suono")
        }
        $ha = static fn (string $schema): bool => preg_match($schema . 'u', $testo) === 1;
        if ($per !== null) {
            if ($ha('/^Le tue carte/')) {
                return 'mescola';
            }
            if ($ha('/^(Hai pescato|Nel pozzetto|Con gli scarti)/')) {
                return 'pesca';
            }
            return null;
        }
        if ($ha('/(La partita è finita|Vince la partita|La partita finisce in parità)/')) {
            return 'fine';
        }
        if ($gioco === 'monopoli') {
            return match (true) {
                $ha('/è eliminato/') => 'eliminato',
                $ha('/tira i dadi/') => 'dadi',
                $ha('/(va in prigione|resta in prigione)/') => 'prigione',
                $ha('/(passa dal Via|si ferma sul Via)/') => 'via',
                $ha('/(pesca un imprevisto|pesca una probabilità)/') => 'imprevisto',
                $ha('/(Asta per|offre .* per|si ritira dall|vince l\'asta)/') => 'asta',
                $ha('/ compra /') => 'compra',
                $ha('/scambio/') => 'scambio',
                $ha('/(ipoteca|riscatta)/') => 'ipoteca',
                $ha('/(costruisce|vende un edificio)/') => 'casa',
                $ha('/(incassa|ritira)/') => 'cassa',
                $ha('/(paga|di tassa|di affitto)/') => 'soldi',
                $ha('/(arriva a|esce di prigione)/') => 'passo',
                default => null,
            };
        }
        return match (true) {
            $ha('/^(Comincia|Si gioca|Si danno altre)/') => 'mescola',
            $ha('/(Asso pigliatutto|Scopa!|chiude la mano)/') => 'scopa',
            $ha('/(accusa Marianna|chiama |compagno|apre con)/') => 'accusa',
            $ha('/(pesca dal mazzo)/') => 'pesca',
            $ha('/(prende con|prende gli scarti|prende il pozzetto| e prende )/') => 'presa',
            $ha('/( cala | attacca |annulla le combinazioni)/') => 'calata',
            $ha('/( gioca | scarta )/') => 'carta',
            default => null,
        };
    } catch (Throwable $e) {
        registra_errore('suono_evento', $e);
        throw $e;
    }
}

function aggiungi_eventi(array &$stanza, array $eventi, string $tipo = 'gioco'): void
{
    try {
        foreach ($eventi as $e) {
            $stanza['ultimo_evento'] = (int) $stanza['ultimo_evento'] + 1;
            $voce = ['n' => $stanza['ultimo_evento'], 'tipo' => $tipo, 'per' => $e['per'] ?? null, 'testo' => (string) $e['testo']];
            $suono = $e['suono'] ?? suono_evento((string) ($stanza['gioco'] ?? ''), $voce['testo'], $tipo, $voce['per']);
            if ($suono !== null) {
                $voce['suono'] = $suono;
            }
            if (isset($e['da'])) {
                $voce['da'] = $e['da'];
            }
            $stanza['eventi'][] = $voce;
        }
        if (count($stanza['eventi']) > 400) {
            $stanza['eventi'] = array_slice($stanza['eventi'], -400);
        }
    } catch (Throwable $e) {
        registra_errore('aggiungi_eventi', $e);
        throw $e;
    }
}

function classe_gioco(array $stanza): string
{
    try {
        return GIOCHI[$stanza['gioco']]['classe'];
    } catch (Throwable $e) {
        registra_errore('classe_gioco', $e);
        throw $e;
    }
}

function posto_di(array $stanza, string $token): int
{
    try {
        foreach ($stanza['posti'] as $i => $p) {
            if ($p !== null && !$p['bot'] && $p['token'] === $token) {
                return $i;
            }
        }
        return -1;
    } catch (Throwable $e) {
        registra_errore('posto_di', $e);
        throw $e;
    }
}

/** Il posto che il computer tiene per questa sessione (chi era assente da troppo tempo), oppure -1. Solo a partita in corso. */
function posto_in_rientro(array $stanza, string $token): int
{
    try {
        if (($stanza['stato'] ?? '') !== 'in corso') {
            return -1;
        }
        foreach ($stanza['posti'] as $i => $p) {
            if ($p !== null && $p['bot'] && isset($p['rientro']) && $p['rientro'] === $token) {
                return $i;
            }
        }
        return -1;
    } catch (Throwable $e) {
        registra_errore('posto_in_rientro', $e);
        throw $e;
    }
}

/** Chi era stato sostituito dal computer perché assente riprende il suo posto. Va chiamata dentro con_stanza. */
function riprendi_posto(array &$stanza, array $g): void
{
    try {
        $i = posto_in_rientro($stanza, (string) $g['token']);
        if ($i < 0) {
            return;
        }
        $stanza['posti'][$i] = ['nome' => $stanza['posti'][$i]['nome'], 'token' => (string) $g['token'], 'bot' => false];
        indice_scrivi((string) $g['token'], (string) $stanza['id']);
        segna_computer($stanza);
        aggiungi_eventi($stanza, [['per' => null, 'testo' => $stanza['posti'][$i]['nome'] . ' è di nuovo in partita.']], 'sistema');
    } catch (Throwable $e) {
        registra_errore('riprendi_posto', $e);
        throw $e;
    }
}

/** Segna che chi occupa questo posto ha perso il collegamento (lo dice il motore d'attesa): lo si dice agli altri, una volta sola. Dentro con_stanza. */
function segna_assente(array &$stanza, array $g): void
{
    try {
        $i = posto_di($stanza, (string) $g['token']);
        if ($i < 0 || $stanza['stato'] !== 'in corso' || !empty($stanza['posti'][$i]['assente_dal'])) {
            return;
        }
        $stanza['posti'][$i]['assente_dal'] = time();
        aggiungi_eventi($stanza, [['per' => null, 'testo' => $stanza['posti'][$i]['nome'] . ' ha perso il collegamento.']], 'sistema');
    } catch (Throwable $e) {
        registra_errore('segna_assente', $e);
        throw $e;
    }
}

/** Chi era segnalato come scollegato è tornato. Dentro con_stanza. */
function segna_presente(array &$stanza, array $g): void
{
    try {
        $i = posto_di($stanza, (string) $g['token']);
        if ($i < 0 || empty($stanza['posti'][$i]['assente_dal'])) {
            return;
        }
        unset($stanza['posti'][$i]['assente_dal']);
        aggiungi_eventi($stanza, [['per' => null, 'testo' => $stanza['posti'][$i]['nome'] . ' è di nuovo collegato.']], 'sistema');
    } catch (Throwable $e) {
        registra_errore('segna_presente', $e);
        throw $e;
    }
}

function nomi_posti(array $stanza): array
{
    try {
        return array_map(fn($p) => $p['nome'], $stanza['posti']);
    } catch (Throwable $e) {
        registra_errore('nomi_posti', $e);
        throw $e;
    }
}

function nome_computer(array $stanza): string
{
    try {
        $usati = array_map(fn($p) => $p === null ? '' : $p['nome'], $stanza['posti']);
        foreach (NOMI_COMPUTER as $n) {
            if (!in_array($n . ' (computer)', $usati, true)) {
                return $n . ' (computer)';
            }
        }
        return 'Computer ' . count($usati);
    } catch (Throwable $e) {
        registra_errore('nome_computer', $e);
        throw $e;
    }
}

/** Segna nello stato del gioco quali posti sono del computer. */
function segna_computer(array &$stanza): void
{
    try {
        if ($stanza['gioco_stato'] !== null) {
            $stanza['gioco_stato']['computer'] = array_map(fn($p) => $p !== null && $p['bot'], $stanza['posti']);
        }
    } catch (Throwable $e) {
        registra_errore('segna_computer', $e);
        throw $e;
    }
}

function riempi_con_computer(array &$stanza): void
{
    try {
        foreach ($stanza['posti'] as $i => $p) {
            if ($p === null) {
                $stanza['posti'][$i] = ['nome' => nome_computer($stanza), 'token' => null, 'bot' => true];
            }
        }
    } catch (Throwable $e) {
        registra_errore('riempi_con_computer', $e);
        throw $e;
    }
}

function avvia_partita(array &$stanza): void
{
    try {
        $classe = classe_gioco($stanza);
        $stanza['invitati'] = [];
        riempi_con_computer($stanza);
        $variante = (string) ($stanza['variante'] ?? 'classica');
        if ($stanza['gioco'] === 'burraco') {
            $stanza['gioco_stato'] = $classe::nuova(nomi_posti($stanza), (int) ($stanza['punti'] ?? 2005));
        } else {
            $stanza['gioco_stato'] = $classe::nuova(nomi_posti($stanza), $variante);
        }
        segna_computer($stanza);
        $stanza['stato'] = 'in corso';
        aggiungi_eventi($stanza, [['per' => null, 'testo' => 'La partita comincia.']], 'sistema');
        aggiungi_eventi($stanza, $classe::inizio($stanza['gioco_stato']));
        fai_giocare_computer($stanza);
    } catch (Throwable $e) {
        registra_errore('avvia_partita', $e);
        throw $e;
    }
}

/** Chi deve agire adesso: di solito chi ha il turno, ma nel monopoli anche chi risponde a uno scambio o fa un'offerta all'asta. */
function attore_di(array $stanza): int
{
    try {
        $classe = classe_gioco($stanza);
        if (method_exists($classe, 'attore')) {
            return (int) $classe::attore($stanza['gioco_stato']);
        }
        return (int) $stanza['gioco_stato']['turno'];
    } catch (Throwable $e) {
        registra_errore('attore_di', $e);
        throw $e;
    }
}

function fai_giocare_computer(array &$stanza): void
{
    try {
        $classe = classe_gioco($stanza);
        segna_computer($stanza);
        $guardia = 0;
        while ($stanza['stato'] === 'in corso' && !$stanza['gioco_stato']['finita'] && $guardia++ < 3000) {
            $turno = attore_di($stanza);
            if (!$stanza['posti'][$turno]['bot']) {
                break;
            }
            try {
                $id = mossa_computer($stanza, $classe, $turno);
                aggiungi_eventi($stanza, $classe::applica($stanza['gioco_stato'], $turno, $id, false));
            } catch (Throwable $e) {
                registra_errore('fai_giocare_computer (bot)', $e);
                // il computer non ha trovato una mossa valida: si prova la prima possibile, altrimenti la partita non resta bloccata ma finisce
                $mosse = $classe::mosse($stanza['gioco_stato'], $turno);
                try {
                    if ($mosse) {
                        aggiungi_eventi($stanza, $classe::applica($stanza['gioco_stato'], $turno, (string) $mosse[0]['id'], true));
                        continue;
                    }
                } catch (Throwable $e2) {
                    registra_errore('fai_giocare_computer (prima mossa possibile)', $e2);
                }
                aggiungi_eventi($stanza, [['per' => null, 'testo' => 'Il computer non riesce a continuare: la partita termina qui.']], 'sistema');
                $stanza['gioco_stato']['finita'] = true;
                break;
            }
        }
        controlla_fine($stanza);
    } catch (Throwable $e) {
        registra_errore('fai_giocare_computer', $e);
        throw $e;
    }
}

const LIVELLI_COMPUTER = ['facile', 'normale', 'difficile'];

/**
 * La mossa del computer secondo il livello della partita: a "difficile" gioca sempre la sua mossa migliore; a "normale" una volta su cinque, e a
 * "facile" una volta su due, fa invece una mossa a caso tra quelle possibili (la battaglia navale spara su una casella a caso). Dalle mosse a caso
 * si tolgono quelle che non fanno andare avanti il gioco (annullare, ipotecare, vendere, costruire, proporre scambi).
 */
function mossa_computer(array $stanza, string $classe, int $turno): string
{
    try {
        // un gioco può avere le sue probabilità (alla battaglia navale un colpo a caso pesa meno che una carta a caso)
        $tabella = defined($classe . '::PROBABILITA_CASO') ? constant($classe . '::PROBABILITA_CASO') : ['facile' => 0.5, 'normale' => 0.2, 'difficile' => 0.0];
        $probabilita = $tabella[$stanza['livello'] ?? 'difficile'] ?? 0.0;
        if ($probabilita > 0 && mt_rand() / mt_getrandmax() < $probabilita) {
            if (method_exists($classe, 'mossaCasuale')) {
                $m = $classe::mossaCasuale($stanza['gioco_stato'], $turno);
                if ($m !== null) {
                    return $m;
                }
            } else {
                $escluse = ['annulla', 'ipoteca', 'vendi', 'costruisci', 'riscatta', 'scambio'];
                $mosse = array_values(array_filter($classe::mosse($stanza['gioco_stato'], $turno), fn($m) => empty($m['parametro']) && !in_array(explode('|', (string) $m['id'])[0], $escluse, true)));
                if ($mosse) {
                    return (string) $mosse[array_rand($mosse)]['id'];
                }
            }
        }
        return $classe::bot($stanza['gioco_stato'], $turno);
    } catch (Throwable $e) {
        registra_errore('mossa_computer', $e);
        throw $e;
    }
}

function controlla_fine(array &$stanza): void
{
    try {
        if ($stanza['stato'] === 'in corso' && $stanza['gioco_stato']['finita']) {
            $stanza['stato'] = 'finita';
            $stanza['finita_il'] = time();
        }
    } catch (Throwable $e) {
        registra_errore('controlla_fine', $e);
        throw $e;
    }
}

/** Chi è assente da troppo tempo e deve giocare (o rispondere a uno scambio) lascia il posto al computer. */
function sostituisci_assenti(array &$stanza): void
{
    try {
        if ($stanza['stato'] !== 'in corso') {
            return;
        }
        $gs = $stanza['gioco_stato'];
        // chi deve agire adesso: il giocatore di turno, oppure chi ha ricevuto una proposta di scambio
        $attore = attore_di($stanza);
        $p = $stanza['posti'][$attore];
        if ($p['bot']) {
            return;
        }
        $g = leggi_file(file_giocatore((string) $p['token']));
        if ($g === null || time() - (int) $g['visto'] > SECONDI_ASSENTE) {
            // il posto si ricorda: se la persona torna (anche dopo una caduta della rete) lo riprende
            $stanza['posti'][$attore] = ['nome' => $p['nome'], 'token' => null, 'bot' => true, 'rientro' => (string) $p['token']];
            aggiungi_eventi($stanza, [['per' => null, 'testo' => $p['nome'] . ' non risponde: continua il computer.']], 'sistema');
            // una proposta di scambio senza risposta si considera rifiutata
            if (isset($gs['scambio']) && $gs['scambio'] !== null && (int) $gs['scambio']['a'] === $attore) {
                $classe = classe_gioco($stanza);
                segna_computer($stanza);
                aggiungi_eventi($stanza, $classe::applica($stanza['gioco_stato'], $attore, 'rifiuta_scambio', false));
            }
            fai_giocare_computer($stanza);
        }
    } catch (Throwable $e) {
        registra_errore('sostituisci_assenti', $e);
        throw $e;
    }
}

function nuovo_id_stanza(): string
{
    try {
        $lettere = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        do {
            $id = '';
            for ($i = 0; $i < 5; $i++) {
                $id .= $lettere[random_int(0, strlen($lettere) - 1)];
            }
        } while (file_exists(file_stanza($id)));
        return $id;
    } catch (Throwable $e) {
        registra_errore('nuovo_id_stanza', $e);
        throw $e;
    }
}

/** L'indirizzo web della partita: chi lo apre entra nella stessa stanza dal browser. */
function link_partita(string $id): string
{
    try {
        return 'https://wingabriel.net/' . (copia_beta() ? 'beta/' : '') . 'giochi/?p=' . rawurlencode($id);
    } catch (Throwable $e) {
        registra_errore('link_partita', $e);
        throw $e;
    }
}

function elenco_stanze(): array
{
    try {
        $r = [];
        foreach (glob(cartella() . '/s_*.json') ?: [] as $f) {
            $s = leggi_file($f);
            if ($s !== null) {
                $r[] = $s;
            }
        }
        return $r;
    } catch (Throwable $e) {
        registra_errore('elenco_stanze', $e);
        throw $e;
    }
}

function azione_giochi(): never
{
    try {
        $r = [];
        foreach (GIOCHI as $id => $g) {
            $varianti = [];
            if (isset($g['varianti']) && is_array($g['varianti'])) {
                foreach ($g['varianti'] as $vid => $vinfo) {
                    $varianti[] = [
                        'id' => $vid,
                        'nome' => $vinfo['nome'] ?? $vid,
                        'descrizione' => $vinfo['descrizione'] ?? '',
                        'posti' => $vinfo['posti'] ?? null,
                    ];
                }
            }
            $r[] = [
                'id' => $id,
                'nome' => $g['nome'],
                'posti' => $g['posti'],
                'descrizione' => $g['descrizione'],
                'varianti' => $varianti,
            ];
        }
        // dove si apre la chat: con il dominio dedicato (scritto da installa-dominio-chat.sh) è un altro indirizzo
        $dominio = @trim((string) @file_get_contents(dirname(__DIR__) . '/chat/DOMINIO.txt'));
        $chat = preg_match('/^[a-z0-9.-]+$/', $dominio) ? 'https://' . $dominio . '/' : '../chat/';
        $g = giocatore_corrente(false);
        $ha = false;
        if ($g !== null) {
            $a = carica_account((string) $g['nome']);
            $ha = $a !== null && ha_password($a);
        }
        rispondi(['giochi' => $r, 'chat' => $chat, 'ha_password' => $ha, 'consigliata' => CLIENT_CONSIGLIATO]);
    } catch (Throwable $e) {
        registra_errore('azione_giochi', $e);
        throw $e;
    }
}

function azione_stanze(): never
{
    try {
        $g = giocatore_corrente();
        $r = [];
        foreach (elenco_stanze() as $s) {
            if ($s['stato'] !== 'attesa') {
                continue;
            }
            // chi ti ha bloccato (o tu hai bloccato) non lo vedi, e non vedi le sue partite
            $bloccata = false;
            foreach ($s['posti'] as $p) {
                if ($p !== null && !$p['bot'] && bloccato_tra((string) $g['nome'], (string) $p['nome'])) {
                    $bloccata = true;
                }
            }
            if ($bloccata) {
                continue;
            }
            $occupati = count(array_filter($s['posti'], fn($p) => $p !== null));
            $v = (string) ($s['variante'] ?? 'classica');
            $vNome = GIOCHI[$s['gioco']]['varianti'][$v]['nome'] ?? ($v !== 'classica' ? ucfirst(str_replace('_', ' ', $v)) : 'Classica');
            $r[] = ['id' => $s['id'], 'gioco' => $s['gioco'], 'nome_gioco' => GIOCHI[$s['gioco']]['nome'], 'creatore' => $s['posti'][0]['nome'] ?? '',
                'variante' => $v, 'variante_nome' => $vNome,
                'posti' => count($s['posti']), 'occupati' => $occupati, 'stato' => $s['stato']];
        }
        rispondi(['stanze' => $r]);
    } catch (Throwable $e) {
        registra_errore('azione_stanze', $e);
        throw $e;
    }
}

/** Un tavolo in attesa, come lo vede chi è in sala. */
function riepilogo_tavolo(array $s, string $io): array
{
    try {
        $occupati = count(array_filter($s['posti'], fn($p) => $p !== null));
        $invitati = is_array($s['invitati'] ?? null) ? $s['invitati'] : [];
        $v = (string) ($s['variante'] ?? 'classica');
        $vNome = GIOCHI[$s['gioco']]['varianti'][$v]['nome'] ?? ($v !== 'classica' ? ucfirst(str_replace('_', ' ', $v)) : 'Classica');
        return [
            'id' => $s['id'],
            'gioco' => $s['gioco'],
            'nome_gioco' => GIOCHI[$s['gioco']]['nome'],
            'variante' => $v,
            'variante_nome' => $vNome,
            'creatore' => $s['posti'][0]['nome'] ?? '',
            'posti' => count($s['posti']),
            'occupati' => $occupati,
            'stato' => $s['stato'],
            'invitato' => in_array($io, $invitati, true),
        ];
    } catch (Throwable $e) {
        registra_errore('riepilogo_tavolo', $e);
        throw $e;
    }
}

/** Vero se in quel tavolo c'è qualcuno che ti ha bloccato, o che hai bloccato tu. */
function tavolo_nascosto(array $s, array $g): bool
{
    try {
        foreach ($s['posti'] as $p) {
            if ($p !== null && !$p['bot'] && bloccato_tra((string) $g['nome'], (string) $p['nome'])) {
                return true;
            }
        }
        return false;
    } catch (Throwable $e) {
        registra_errore('tavolo_nascosto', $e);
        throw $e;
    }
}

/** I tavoli in attesa. Con gioco= solo quelli di quel gioco. */
function azione_tavoli(): never
{
    try {
        $g = giocatore_corrente();
        $gioco = (string) ($_GET['gioco'] ?? '');
        if ($gioco !== '' && !isset(GIOCHI[$gioco])) {
            errore('Gioco sconosciuto.');
        }
        $io = mb_strtolower((string) $g['nome']);
        $r = [];
        foreach (elenco_stanze() as $s) {
            if ($s['stato'] !== 'attesa') {
                continue;
            }
            if ($gioco !== '' && $s['gioco'] !== $gioco) {
                continue;
            }
            if (tavolo_nascosto($s, $g)) {
                continue;
            }
            $r[] = riepilogo_tavolo($s, $io);
        }
        rispondi(['tavoli' => $r]);
    } catch (Throwable $e) {
        registra_errore('azione_tavoli', $e);
        throw $e;
    }
}

/** Gli inviti ancora validi: tavolo in attesa, un posto libero, e il tuo nome tra gli invitati. */
function azione_inviti(): never
{
    try {
        $g = giocatore_corrente();
        $io = mb_strtolower((string) $g['nome']);
        $r = [];
        foreach (elenco_stanze() as $s) {
            if ($s['stato'] !== 'attesa' || posto_di($s, $g['token']) >= 0 || tavolo_nascosto($s, $g)) {
                continue;
            }
            $invitati = is_array($s['invitati'] ?? null) ? $s['invitati'] : [];
            if (!in_array($io, $invitati, true)) {
                continue;
            }
            if (count(array_filter($s['posti'], fn($p) => $p === null)) === 0) {
                continue;
            }
            $r[] = riepilogo_tavolo($s, $io);
        }
        rispondi(['inviti' => $r]);
    } catch (Throwable $e) {
        registra_errore('azione_inviti', $e);
        throw $e;
    }
}

/** Invita un nome utente al tavolo in cui sei seduto, finché la partita non è cominciata. */
function azione_invita(): never
{
    try {
        $g = giocatore_corrente();
        $id = (string) (corpo()['id'] ?? '');
        $nomeRichiesto = nome_dal_corpo();
        if (mb_strtolower($nomeRichiesto) === mb_strtolower((string) $g['nome'])) {
            errore('Non puoi invitare te stesso.');
        }
        $account = carica_account($nomeRichiesto);
        if ($account === null || bloccato_tra((string) $g['nome'], (string) $account['nome'])) {
            errore('Non trovo questo giocatore, oppure non è disponibile.', 404);
        }
        $nomeInvito = (string) $account['nome'];
        $chiave = mb_strtolower($nomeInvito);
        // un nome utente nuovo (meno di un giorno) può fare pochi inviti all'ora
        $troppi = modifica_account((string) $g['nome'], function (array &$a) {
            $adesso = time();
            if ($adesso - (int) ($a['creato'] ?? 0) >= SECONDI_ACCOUNT_NUOVO) {
                return false;
            }
            $fatti = array_values(array_filter(is_array($a['inviti_fatti'] ?? null) ? $a['inviti_fatti'] : [], fn($t) => $adesso - (int) $t < 3600));
            if (count($fatti) >= INVITI_ORA_NUOVO) {
                $a['inviti_fatti'] = $fatti;
                return true;
            }
            $fatti[] = $adesso;
            $a['inviti_fatti'] = $fatti;
            return false;
        });
        if ($troppi) {
            errore('Hai fatto molti inviti in poco tempo: riprova più tardi.', 429);
        }
        con_stanza($id, function (array &$s) use ($g, $nomeInvito, $chiave) {
            if ($s['stato'] !== 'attesa') {
                errore('La partita è già cominciata.', 409);
            }
            if (posto_di($s, $g['token']) < 0) {
                errore('Non fai parte di questa partita.', 403);
            }
            foreach ($s['posti'] as $p) {
                if ($p !== null && !$p['bot'] && mb_strtolower((string) $p['nome']) === $chiave) {
                    errore('Questo giocatore è già al tavolo.', 409);
                }
            }
            if (count(array_filter($s['posti'], fn($p) => $p === null)) === 0) {
                errore('Il tavolo è al completo.', 409);
            }
            if (!in_array($chiave, $s['invitati'], true)) {
                if (count($s['invitati']) >= MASSIMO_INVITI) {
                    errore('Hai già invitato troppe persone a questo tavolo.', 409);
                }
                $s['invitati'][] = $chiave;
                aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' ha invitato ' . $nomeInvito . '.']], 'sistema');
            }
        });
        rispondi(['nome' => $nomeInvito]);
    } catch (Throwable $e) {
        registra_errore('azione_invita', $e);
        throw $e;
    }
}

/** Toglie il tuo nome dagli invitati di un tavolo. */
function azione_rifiuta_invito(): never
{
    try {
        $g = giocatore_corrente();
        $id = (string) (corpo()['id'] ?? '');
        $io = mb_strtolower((string) $g['nome']);
        con_stanza($id, function (array &$s) use ($io) {
            $s['invitati'] = array_values(array_filter($s['invitati'], fn($n) => (string) $n !== $io));
        });
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_rifiuta_invito', $e);
        throw $e;
    }
}

function azione_mia(): never
{
    try {
        $g = giocatore_corrente();
        // anche la partita in cui il computer ha preso il posto di chi era assente: tornando lo si riprende
        $s = stanza_della_sessione((string) $g['token']);
        if ($s !== null) {
            rispondi(['id' => $s['id'], 'gioco' => $s['gioco'], 'stato' => $s['stato']]);
        }
        rispondi(['id' => null]);
    } catch (Throwable $e) {
        registra_errore('azione_mia', $e);
        throw $e;
    }
}

function azione_crea(): never
{
    try {
        $g = giocatore_corrente();
        $c = corpo();
        $gioco = (string) ($c['gioco'] ?? '');
        if (!isset(GIOCHI[$gioco])) {
            errore('Gioco sconosciuto.');
        }
        $variante = trim((string) ($c['variante'] ?? 'classica'));
        $variantiGioco = GIOCHI[$gioco]['varianti'] ?? [];
        if (!isset($variantiGioco[$variante])) {
            $variante = 'classica';
        }
        $postiAmmessi = $variantiGioco[$variante]['posti'] ?? GIOCHI[$gioco]['posti'];
        $posti = (int) ($c['posti'] ?? 0);
        if (!in_array($posti, $postiAmmessi, true)) {
            if (count($postiAmmessi) === 1) {
                $posti = (int) $postiAmmessi[0];
            } else {
                errore('Numero di giocatori non valido per questa variante.');
            }
        }
        if (count(glob(cartella() . '/s_*.json') ?: []) >= MASSIMO_STANZE) {
            errore('Ci sono troppe partite aperte, riprova più tardi.', 503);
        }
        // chi ha già una partita aperta o in corso non ne crea un'altra
        $aperta = stanza_della_sessione((string) $g['token']);
        if ($aperta !== null && posto_di($aperta, (string) $g['token']) >= 0) {
            errore('Hai già una partita in corso: prima lasciala.', 409);
        }
        $id = nuovo_id_stanza();
        // il livello del computer: chi non lo dice (i client vecchi) gioca contro il più forte, come prima
        $livello = in_array($c['livello'] ?? '', LIVELLI_COMPUTER, true) ? (string) $c['livello'] : 'difficile';
        $stanza = [
            'id' => $id,
            'gioco' => $gioco,
            'variante' => $variante,
            'livello' => $livello,
            'stato' => 'attesa',
            'posti' => array_fill(0, $posti, null),
            'versione' => 0,
            'ultimo_evento' => 0,
            'eventi' => [],
            'gioco_stato' => null,
            'punti' => $gioco === 'burraco' ? max(500, min(5005, ((int) ($c['punti'] ?? 0)) > 0 ? (int) $c['punti'] : 2005)) : null,
            'creata' => time(),
            'aggiornata' => time(),
            'invitati' => [],
        ];
        $stanza['posti'][0] = ['nome' => $g['nome'], 'token' => $g['token'], 'bot' => false];
        scrivi_file(file_stanza($id), $stanza);
        con_stanza($id, function (array &$s) use ($g, $c) {
            aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' ha creato il tavolo.']], 'sistema');
            if (!empty($c['computer'])) {
                avvia_partita($s);
            }
        });
        indice_scrivi((string) $g['token'], $id);
        rispondi(['id' => $id, 'link' => link_partita($id)]);
    } catch (Throwable $e) {
        registra_errore('azione_crea', $e);
        throw $e;
    }
}

function azione_entra_stanza(): never
{
    try {
        $g = giocatore_corrente();
        $id = (string) (corpo()['id'] ?? '');
        $aperta = stanza_della_sessione((string) $g['token']);
        if ($aperta !== null && posto_di($aperta, (string) $g['token']) >= 0 && $aperta['id'] !== $id) {
            errore('Hai già una partita in corso: prima lasciala.', 409);
        }
        con_stanza($id, function (array &$s) use ($g) {
            if ($s['stato'] !== 'attesa') {
                errore('La partita è già cominciata.', 409);
            }
            if (posto_di($s, $g['token']) >= 0) {
                return;
            }
            foreach ($s['posti'] as $p) {
                if ($p !== null && !$p['bot'] && bloccato_tra((string) $g['nome'], (string) $p['nome'])) {
                    errore('Non puoi entrare in questa partita.', 403);
                }
            }
            $libero = null;
            foreach ($s['posti'] as $i => $p) {
                if ($p === null) {
                    $libero = $i;
                    break;
                }
            }
            if ($libero === null) {
                errore('La partita è al completo.', 409);
            }
            $s['posti'][$libero] = ['nome' => $g['nome'], 'token' => $g['token'], 'bot' => false];
            $chiave = mb_strtolower((string) $g['nome']);
            $s['invitati'] = array_values(array_filter($s['invitati'], fn($n) => (string) $n !== $chiave));
            aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' è entrato nella partita.']], 'sistema');
            if (count(array_filter($s['posti'], fn($p) => $p === null)) === 0) {
                avvia_partita($s);
            }
        });
        indice_scrivi((string) $g['token'], $id);
        rispondi(['id' => $id, 'link' => link_partita($id)]);
    } catch (Throwable $e) {
        registra_errore('azione_entra_stanza', $e);
        throw $e;
    }
}

function azione_avvia(): never
{
    try {
        $g = giocatore_corrente();
        $id = (string) (corpo()['id'] ?? '');
        con_stanza($id, function (array &$s) use ($g) {
            if (posto_di($s, $g['token']) !== 0) {
                errore('Solo chi ha creato la partita può avviarla.', 403);
            }
            if ($s['stato'] !== 'attesa') {
                errore('La partita è già cominciata.', 409);
            }
            avvia_partita($s);
        });
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_avvia', $e);
        throw $e;
    }
}

function azione_lascia(): never
{
    try {
        $g = giocatore_corrente();
        lascia_stanza((string) (corpo()['id'] ?? ''), $g);
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_lascia', $e);
        throw $e;
    }
}

function lascia_stanza(string $id, array $g): void
{
    try {
        $elimina = false;
        con_stanza($id, function (array &$s) use ($g, &$elimina) {
            $i = posto_di($s, $g['token']);
            if ($i < 0) {
                return;
            }
            if ($s['stato'] === 'attesa') {
                $s['posti'][$i] = null;
                aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' ha lasciato la partita.']], 'sistema');
                // se se n'è andato chi l'aveva creata, il primo giocatore rimasto prende il suo posto
                if ($i === 0) {
                    foreach ($s['posti'] as $k => $p) {
                        if ($p !== null) {
                            $s['posti'][0] = $p;
                            $s['posti'][$k] = null;
                            break;
                        }
                    }
                }
                if ($s['posti'][0] === null) {
                    $elimina = true;
                }
                return;
            }
            $s['posti'][$i] = ['nome' => $g['nome'], 'token' => null, 'bot' => true];
            if ($s['stato'] === 'in corso') {
                // chi esce interrompe la partita, in ogni gioco: non continua il computer al suo posto
                aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' ha lasciato la partita: la partita è finita.']], 'sistema');
                $s['gioco_stato']['finita'] = true;
                if ($s['gioco'] === 'battaglia') {
                    $s['gioco_stato']['vincitore'] = 1 - $i;
                }
                $s['gioco_stato']['motivo'] = $g['nome'] . ' ha lasciato la partita.';
                controlla_fine($s);
            } else {
                aggiungi_eventi($s, [['per' => null, 'testo' => $g['nome'] . ' ha lasciato la partita.']], 'sistema');
            }
            $umani = array_filter($s['posti'], fn($p) => $p !== null && !$p['bot']);
            if (count($umani) === 0) {
                $elimina = true;
            }
        });
        if ($elimina) {
            @unlink(file_stanza($id));
            @unlink(cartella() . '/s_' . $id . '.lock');
        }
    } catch (Throwable $e) {
        registra_errore('lascia_stanza', $e);
        throw $e;
    }
}

function vista_stato(array $stanza, int $posto, bool $conSituazione, int $da): array
{
    try {
        $classe = $stanza['gioco'] ? classe_gioco($stanza) : '';
        $v = (string) ($stanza['variante'] ?? 'classica');
        $vNome = GIOCHI[$stanza['gioco']]['varianti'][$v]['nome'] ?? ($v !== 'classica' ? ucfirst(str_replace('_', ' ', $v)) : 'Classica');
        $r = [
            'id' => $stanza['id'],
            'gioco' => $stanza['gioco'],
            'nome_gioco' => GIOCHI[$stanza['gioco']]['nome'],
            'variante' => $v,
            'variante_nome' => $vNome,
            'versione' => $stanza['versione'],
            'stato' => $stanza['stato'],
            'posto' => $posto,
            'turno' => null,
            'giocatori' => array_map(fn($p) => $p === null ? null : ['nome' => $p['nome'], 'computer' => $p['bot'], 'assente' => !empty($p['assente_dal'])], $stanza['posti']),
            'eventi' => [],
            'mosse' => [],
            'opzioni' => [],
            'vista' => null,
            'ultimo_evento' => $stanza['ultimo_evento'],
            'link' => link_partita($stanza['id']),
        ];
        // i messaggi della chat della partita di chi hai bloccato (o di chi ti ha bloccato) non si vedono
        $nomeMio = $posto >= 0 && isset($stanza['posti'][$posto]) ? (string) $stanza['posti'][$posto]['nome'] : '';
        $nascosti = [];
        foreach ($stanza['eventi'] as $e) {
            if ($e['n'] > $da && ($e['per'] === null || $e['per'] === $posto)) {
                if (($e['tipo'] ?? '') === 'chat' && $nomeMio !== '' && isset($e['da'])) {
                    $autore = (string) $e['da'];
                    if (!isset($nascosti[$autore])) {
                        $nascosti[$autore] = bloccato_tra($nomeMio, $autore);
                    }
                    if ($nascosti[$autore]) {
                        continue;
                    }
                }
                $r['eventi'][] = $e;
            }
        }
        if ($stanza['gioco_stato'] !== null) {
            $gs = $stanza['gioco_stato'];
            $r['turno'] = $gs['turno'];
            $r['attore'] = attore_di($stanza);
            $r['vista'] = $classe::vista($gs, $posto);
            if ($stanza['stato'] === 'in corso' && $posto >= 0) {
                $r['mosse'] = $classe::mosse($gs, $posto);
                if (method_exists($classe, 'opzioni')) {
                    $r['opzioni'] = $classe::opzioni($gs, $posto);
                }
            }
            if ($conSituazione && $posto >= 0) {
                $r['situazione'] = $classe::situazione($gs, $posto);
            }
        }
        return $r;
    } catch (Throwable $e) {
        registra_errore('vista_stato', $e);
        throw $e;
    }
}

function azione_stato(): never
{
    try {
        $g = giocatore_corrente();
        $id = (string) ($_GET['id'] ?? '');
        $da = (int) ($_GET['da'] ?? 0);
        $attesa = max(0, min(20, (int) ($_GET['attesa'] ?? 0)));
        $conSituazione = !empty($_GET['situazione']);
        if (!id_valido($id) || !file_exists(file_stanza($id))) {
            errore('Partita non trovata.', 404);
        }
        set_time_limit($attesa + 15);
        $fine = microtime(true) + $attesa;
        $versioneNota = isset($_GET['versione']) ? (int) $_GET['versione'] : -1;
        // Di solito l'attesa non passa da qui: la tiene il motore d'attesa (attesa.py) e a PHP arriva sempre attesa=0. Se il motore non c'è,
        // l'attesa resta qui, ma si rifà il lavoro pesante solo quando il file della partita cambia (o ogni 5 secondi, per le assenze).
        $ultimoTesto = null;
        $ultimaRisposta = [];
        $ripresa = microtime(true);
        $tentativiRientro = 0;
        while (true) {
            $testo = @file_get_contents(file_stanza($id));
            if ($testo === false) {
                errore('Partita non trovata.', 404);
            }
            if ($testo === $ultimoTesto && microtime(true) < $ripresa + 5) {
                if (microtime(true) >= $fine) {
                    rispondi($ultimaRisposta);
                }
                usleep(400000);
                continue;
            }
            $ultimoTesto = $testo;
            $ripresa = microtime(true);
            $stanza = json_decode($testo, true);
            if (!is_array($stanza)) {
                errore('Partita non trovata.', 404);
            }
            normalizza_stanza($stanza);
            $posto = posto_di($stanza, $g['token']);
            if ($posto < 0) {
                // chi era stato sostituito dal computer perché assente da troppo tempo, tornando riprende il suo posto
                $rientro = $tentativiRientro++ < 2 ? posto_in_rientro($stanza, $g['token']) : -1;
                if ($rientro >= 0) {
                    con_stanza($id, function (array &$s) use ($g) {
                        riprendi_posto($s, $g);
                    });
                    $ultimoTesto = null;
                    continue;
                }
                errore('Non fai parte di questa partita.', 403);
            }
            // chi era stato segnalato come scollegato e adesso chiede lo stato è tornato: lo si dice agli altri
            if (!empty($stanza['posti'][$posto]['assente_dal'])) {
                con_stanza($id, function (array &$s) use ($g) {
                    segna_presente($s, $g);
                });
                $stanza = leggi_file(file_stanza($id)) ?? $stanza;
            }
            // un turno di Monopoli rimasto aperto si chiude con una scrittura (che lo fa da sola), poi si rilegge
            if (monopoli_turno_aperto($stanza)) {
                con_stanza($id, function (array &$s) {
                });
                $stanza = leggi_file(file_stanza($id)) ?? $stanza;
            }
            // chi è assente da troppo tempo cede il posto al computer (si guarda l'ora dell'ultimo segno di vita, senza rileggere il file)
            if ($stanza['stato'] === 'in corso') {
                $turno = attore_di($stanza);
                $p = $stanza['posti'][$turno];
                if (!$p['bot'] && $turno !== $posto) {
                    $ultimoSegno = @filemtime(file_giocatore((string) $p['token']));
                    if ($ultimoSegno === false || time() - $ultimoSegno > SECONDI_ASSENTE) {
                        con_stanza($id, function (array &$s) {
                            sostituisci_assenti($s);
                        });
                        $stanza = leggi_file(file_stanza($id)) ?? $stanza;
                    }
                }
            }
            $r = vista_stato($stanza, $posto, $conSituazione, $da);
            if ($r['eventi'] || $attesa === 0 || microtime(true) >= $fine || ($versioneNota >= 0 && $stanza['versione'] !== $versioneNota)) {
                rispondi($r);
            }
            $ultimaRisposta = $r;
            usleep(400000);
        }
    } catch (Throwable $e) {
        registra_errore('azione_stato', $e);
        throw $e;
    }
}

/**
 * POST ?a=presenza {id, stato:'assente'}: lo manda il motore d'attesa a nome di un giocatore la cui connessione è sparita da qualche secondo.
 * Non basta dirlo: se questa sessione ha dato segno di vita da meno di 15 secondi (o la partita non è in corso) non succede niente.
 * Non segna il giocatore come presente (così la richiesta non nasconde la sua vera assenza).
 */
function azione_presenza(): never
{
    try {
        $g = giocatore_corrente(true, false);
        $id = (string) (corpo()['id'] ?? '');
        if (!id_valido($id) || !file_exists(file_stanza($id))) {
            rispondi([]);
        }
        $ultimoSegno = @filemtime(file_giocatore((string) $g['token']));
        if ($ultimoSegno !== false && time() - $ultimoSegno < SECONDI_SEGNALA_ASSENZA) {
            rispondi([]);
        }
        con_stanza($id, function (array &$s) use ($g) {
            segna_assente($s, $g);
        });
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_presenza', $e);
        throw $e;
    }
}

function azione_mossa(): never
{
    try {
        $g = giocatore_corrente();
        $c = corpo();
        $id = (string) ($c['id'] ?? '');
        $mossa = (string) ($c['mossa'] ?? '');
        // "mid": il numero che il programma dà a ogni mossa. Se la risposta si perde e il programma la rimanda, la mossa non si rifà:
        // si risponde con lo stato com'è adesso.
        $mid = (string) ($c['mid'] ?? '');
        $mid = preg_match('/^[A-Za-z0-9-]{8,40}$/', $mid) ? $mid : '';
        $stanza = con_stanza($id, function (array &$s) use ($g, $mossa, $mid) {
            $posto = posto_di($s, $g['token']);
            if ($posto >= 0) {
                segna_presente($s, $g);
            }
            if ($posto < 0) {
                // chi era stato sostituito perché assente e rimanda la mossa riprende il posto
                riprendi_posto($s, $g);
                $posto = posto_di($s, $g['token']);
                if ($posto < 0) {
                    errore('Non fai parte di questa partita.', 403);
                }
            }
            if ($s['stato'] !== 'in corso') {
                errore('La partita non è in corso.', 409);
            }
            if ($mid !== '') {
                $chiave = $posto . ':' . $mid;
                if (in_array($chiave, $s['mids'] ?? [], true)) {
                    return $s;   // già fatta
                }
                $s['mids'] = array_slice(array_merge($s['mids'] ?? [], [$chiave]), -24);
            }
            $classe = classe_gioco($s);
            segna_computer($s);
            try {
                $eventi = $classe::applica($s['gioco_stato'], $posto, $mossa, true);
            } catch (InvalidArgumentException | RuntimeException | TypeError | ValueError | DivisionByZeroError | OutOfRangeException $e) {
                errore($classe::mosse($s['gioco_stato'], $posto) || (method_exists($classe, 'opzioni') && $classe::opzioni($s['gioco_stato'], $posto)) ? 'Questa mossa non è consentita.' : 'Non tocca a te.', 409);
            }
            aggiungi_eventi($s, $eventi);
            controlla_fine($s);
            fai_giocare_computer($s);
            return $s;
        });
        $posto = posto_di($stanza, $g['token']);
        rispondi(vista_stato($stanza, $posto, false, (int) ($c['da'] ?? $stanza['ultimo_evento'])));
    } catch (Throwable $e) {
        registra_errore('azione_mossa', $e);
        throw $e;
    }
}

function azione_chat(): never
{
    try {
        $g = giocatore_corrente();
        $c = corpo();
        $id = (string) ($c['id'] ?? '');
        $testo = trim((string) ($c['testo'] ?? ''));
        $testo = preg_replace('/[\x00-\x1F\x7F]+/u', ' ', $testo) ?? '';
        $testo = trim(preg_replace('/ {2,}/u', ' ', $testo) ?? '');
        if ($testo === '') {
            errore('Il messaggio è vuoto.');
        }
        if (mb_strlen($testo) > 200) {
            $testo = mb_substr($testo, 0, 200);
        }
        if (microtime(true) - (float) ($g['ultima_chat'] ?? 0) < 1.0) {
            errore('Scrivi più piano: un messaggio al secondo.', 429);
        }
        con_stanza($id, function (array &$s) use ($g, $testo) {
            if (posto_di($s, $g['token']) < 0) {
                errore('Non fai parte di questa partita.', 403);
            }
            aggiungi_eventi($s, [['per' => null, 'da' => $g['nome'], 'testo' => $testo]], 'chat');
        });
        $dati = leggi_file(file_giocatore($g['token'])) ?? [];
        $dati['ultima_chat'] = microtime(true);
        scrivi_file(file_giocatore($g['token']), $dati);
        rispondi([]);
    } catch (Throwable $e) {
        registra_errore('azione_chat', $e);
        throw $e;
    }
}

/**
 * Cancella i nomi utente non usati da un anno e quelli creati e mai più usati (nessun accesso dopo la creazione, nessun blocco, nessun codice
 * della chat, nessuna sessione) da 60 giorni: sono quasi sempre prove o nomi creati a raffica. Si leggono solo i file non modificati da più di 60 giorni.
 */
function pulizia_account(): void
{
    try {
        $sessioni = [];
        foreach (glob(cartella() . '/p_*.json') ?: [] as $f) {
            $g = leggi_file($f);
            if ($g !== null) {
                $sessioni[mb_strtolower((string) ($g['nome'] ?? ''))] = true;
            }
        }
        $adesso = time();
        $soglia = min(SECONDI_ACCOUNT_MAI_USATO, SECONDI_ACCOUNT_INATTIVO);
        foreach (glob(cartella() . '/u_*.json') ?: [] as $f) {
            if ($adesso - (int) @filemtime($f) < $soglia) {
                continue;
            }
            $a = leggi_file($f);
            if ($a === null) {
                continue;
            }
            $creato = (int) ($a['creato'] ?? 0);
            $inattivo = $adesso - (int) ($a['ultimo'] ?? 0) > SECONDI_ACCOUNT_INATTIVO;
            $mai = $creato > 0 && (int) ($a['ultimo'] ?? 0) === $creato && $adesso - $creato > SECONDI_ACCOUNT_MAI_USATO
                && empty($a['bloccati']) && !isset($a['chat']) && !isset($sessioni[mb_strtolower((string) ($a['nome'] ?? ''))]);
            if ($inattivo || $mai) {
                @unlink($f);
            }
        }
    } catch (Throwable $e) {
        registra_errore('pulizia_account', $e);
        throw $e;
    }
}

/** Cancella quello che è scaduto: nomi, partite ferme o finite, file di blocco senza partita. */
function pulizia(): void
{
    try {
        // quanti nomi utente ci sono (solo i nomi dei file, senza leggerli): lo usa crea_account
        @file_put_contents(cartella() . '/n_account.txt', (string) count(glob(cartella() . '/u_*.json') ?: []), LOCK_EX);
        // i nomi utente si leggono uno per uno: una volta all'ora basta
        $marcaAccount = cartella() . '/pulizia_account.marca';
        if (time() - (int) @filemtime($marcaAccount) >= 3600 && @touch($marcaAccount)) {
            pulizia_account();
        }
        foreach (glob(cartella() . '/p_*.json') ?: [] as $f) {
            $g = leggi_file($f);
            if ($g === null || time() - (int) $g['visto'] > SECONDI_NOME) {
                @unlink($f);
            }
        }
        // i file che accompagnano una sessione (la partita, l'avviso di sostituzione) spariscono con lei
        foreach (glob(cartella() . '/i_*.txt') ?: [] as $f) {
            if (!file_exists(cartella() . '/p_' . substr(basename($f), 2, 32) . '.json')) {
                @unlink($f);
            }
        }
        foreach (glob(cartella() . '/x_*.json') ?: [] as $f) {
            if (time() - (int) @filemtime($f) > SECONDI_NOME) {
                @unlink($f);
            }
        }
        foreach (glob(cartella() . '/s_*.json') ?: [] as $f) {
            $s = leggi_file($f);
            $scaduta = $s === null
                || time() - (int) $s['aggiornata'] > SECONDI_STANZA_FERMA
                || ($s['stato'] === 'finita' && time() - (int) ($s['finita_il'] ?? 0) > SECONDI_STANZA_FINITA);
            if ($scaduta) {
                @unlink($f);
                @unlink(preg_replace('/\.json$/', '.lock', $f));
            }
        }
        foreach (glob(cartella() . '/s_*.lock') ?: [] as $f) {
            if (!file_exists(preg_replace('/\.lock$/', '.json', $f))) {
                @unlink($f);
            }
        }
        foreach (glob(cartella() . '/*.tmp') ?: [] as $f) {
            if (time() - (int) @filemtime($f) > 300) {
                @unlink($f);
            }
        }
    } catch (Throwable $e) {
        registra_errore('pulizia', $e);
        throw $e;
    }
}

// ------------------------------------------------------------------ ingresso
// Il client vero (giochi/index.html) sta sullo stesso sito, quindi non avrebbe nemmeno bisogno di CORS: "*" permetteva
// a qualsiasi sito di rigiocare un X-Token finito fuori per un'altra via (log di un proxy, screenshot condiviso).
header('Access-Control-Allow-Origin: https://wingabriel.net');
header('Access-Control-Allow-Headers: X-Token, Content-Type');
if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') {
    http_response_code(204);
    exit;
}

// la pulizia scorre tutti i file: al massimo una volta al minuto (e una sola richiesta per volta), non a caso ogni tanto
$marcaPulizia = cartella() . '/pulizia.marca';
if (time() - (int) @filemtime($marcaPulizia) >= 60 && @touch($marcaPulizia)) {
    pulizia();
}

try {
    switch ((string) ($_GET['a'] ?? '')) {
        case 'registra':
            azione_registra();
        case 'entra':
            azione_entra();
        case 'elimina_account':
            azione_elimina_account();
        case 'chat_pubblica':
            azione_chat_pubblica();
        case 'chat_ritira':
            azione_chat_ritira();
        case 'cerca':
            azione_cerca();
        case 'blocca':
            azione_blocca();
        case 'sblocca':
            azione_sblocca();
        case 'bloccati':
            azione_bloccati();
        case 'in_linea':
            azione_in_linea();
        case 'giochi':
            azione_giochi();
        case 'stanze':
            azione_stanze();
        case 'tavoli':
            azione_tavoli();
        case 'inviti':
            azione_inviti();
        case 'mia':
            azione_mia();
        case 'crea':
            azione_crea();
        case 'entra_stanza':
            azione_entra_stanza();
        case 'invita':
            azione_invita();
        case 'rifiuta_invito':
            azione_rifiuta_invito();
        case 'avvia':
            azione_avvia();
        case 'lascia':
            azione_lascia();
        case 'stato':
            azione_stato();
        case 'mossa':
            azione_mossa();
        case 'presenza':
            azione_presenza();
        case 'chat':
            azione_chat();
        default:
            errore('Richiesta sconosciuta.', 404);
    }
} catch (Throwable $e) {
    // niente dettagli all'esterno; nel registro solo l'errore tecnico (vedi registra_errore)
    registra_errore('principale', $e);
    errore('Errore del server.', 500);
}
