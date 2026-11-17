<?php
// Battaglia navale per due giocatori (due persone, oppure una persona contro il computer).
//
// Il casellario è di 10 colonne (A-J) per 10 righe (1-10). Ognuno posiziona la sua flotta senza che l'altro la veda, poi si spara a turno
// dicendo una casella: acqua, colpito, colpito e affondato. Vince chi per primo affonda tutte le navi dell'altro.
//
// Varianti:
//  - Classica: 10 navi (una da 4, due da 3, tre da 2, quattro da 1) che non si possono toccare, nemmeno in diagonale. Si spara a turno.
//  - Cinque navi: 5, 4, 3, 3 e 2 caselle; le navi possono toccarsi. Si spara a turno.
//  - A raffica: come la classica, ma chi colpisce una nave spara ancora.
//
// Mosse (id):
//   nave|riga|colonna|o   posiziona la prossima nave, con la prua nella casella (righe e colonne da 0); o = "o" orizzontale (verso destra) oppure "v" verticale (verso il basso)
//   casuale               mette a caso le navi che mancano
//   annulla               toglie l'ultima nave messa
//   pronto                la flotta è a posto: quando lo sono entrambi comincia la battaglia
//   spara|riga|colonna    spara su una casella (righe e colonne da 0)
// La vista di ognuno ha il suo mare (la sua flotta con i colpi presi) e il mare nemico (solo i suoi colpi): le navi dell'altro non si vedono
// finché la partita non finisce. Caselle: "." niente o non ancora colpita, "N" nave (solo la propria), "o" acqua, "X" colpita, "#" affondata.

declare(strict_types=1);

final class BattagliaNavale
{
    private const INSULTI_ACQUA = ['Solo solletico alle alghe.', 'I pesci rossi ringraziano per l\'annaffiata.', 'Nemmeno i pesci se ne sono accorti.', 'La mira è andata in ferie.', 'Splash, ma senza gloria.', 'I pesci protestano per il disturbo.', 'Un tiro da marinaio ubriaco.', 'Il bersaglio era dall\'altra parte, genio.', 'Il cannone spara solo bolle d\'aria.', 'Colpita una medusa innocente.', 'Il fondo del mare ringrazia.', 'Munizioni sprecate, che peccato.', 'Un gabbiano distratto è salvo per un soffio.', 'Il capitano nemico ride sotto i baffi.', 'Che mira da campione di nuoto.'];
    private const COMPLIMENTI_COLPITO = ['Bel colpo, la nave trema tutta.', 'Centro perfetto, complimenti.', 'Mira chirurgica!', 'Un colpo da maestro.', 'Questo ha fatto male sul serio.', 'Gran bella cannonata!', 'Boom, che botta!', 'Il nemico cerca già i giubbotti.', 'Colpo secco e preciso, bravissimo.', 'Hanno sentito il colpo fino a terra.', 'Questa sì che è mira.', 'Il capitano nemico non dorme più tranquillo.'];
    private const COMPLIMENTI_AFFONDATA = ['Giù nel blu, senza scampo.', 'Addio nave, è stato un piacere.', 'Pesci, fate spazio a un relitto nuovo.', 'È rimasta solo la schiuma a galla.', 'Un affondamento da manuale.', 'Non galleggia più nemmeno un pezzo.', 'Spettacolare, è andata a fondo dritta.', 'Questa sì che è una fine gloriosa.'];
    public const RIGHE = 10;
    public const COLONNE = 10;

    public const VARIANTI = [
        'classica' => ['nome' => 'Classica', 'descrizione' => 'Dieci navi (una da quattro caselle, due da tre, tre da due e quattro da una) che non si possono toccare. Si spara a turno.'],
        'cinque_navi' => ['nome' => 'Cinque navi', 'descrizione' => 'Cinque navi da cinque, quattro, tre, tre e due caselle, che possono toccarsi. Si spara a turno.'],
        'a_raffica' => ['nome' => 'A raffica', 'descrizione' => 'Come la classica, ma chi colpisce una nave spara ancora.'],
    ];

    private static function flotta(string $variante): array
    {
        try {
            return $variante === 'cinque_navi' ? [5, 4, 3, 3, 2] : [4, 3, 3, 2, 2, 2, 1, 1, 1, 1];
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::flotta', $e);
            throw $e;
        }
    }

    private static function distanza(string $variante): bool
    {
        try {
            return $variante !== 'cinque_navi';
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::distanza', $e);
            throw $e;
        }
    }

    private static function raffica(string $variante): bool
    {
        try {
            return $variante === 'a_raffica';
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::raffica', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ partita
    public static function nuova(array $nomi, string $variante = 'classica'): array
    {
        try {
            if (count($nomi) !== 2) {
                throw new InvalidArgumentException('La battaglia navale si gioca in due.');
            }
            $variante = isset(self::VARIANTI[$variante]) ? $variante : 'classica';
            $flotta = self::flotta($variante);
            return [
                'tipo' => 'battaglia',
                'variante' => $variante,
                'n' => 2,
                'nomi' => array_values($nomi),
                'fase' => 'posizionamento',
                'navi' => [[], []],
                'da_posizionare' => [$flotta, $flotta],
                'colpi' => [[], []],
                'pronto' => [false, false],
                'turno' => 0,
                'ultimo_colpo' => null,
                'finita' => false,
                'vincitore' => null,
                'punteggi' => null,
                'motivo' => '',
            ];
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::nuova', $e);
            throw $e;
        }
    }

    public static function inizio(array $st): array
    {
        try {
            $v = $st['variante'];
            $desc = 'Si gioca a battaglia navale, ' . mb_strtolower(self::VARIANTI[$v]['nome']) . ': ' . count(self::flotta($v)) . ' navi' . (self::distanza($v) ? ' che non si possono toccare' : '') . '.';
            $r = [['per' => null, 'testo' => $desc . ' Ognuno posiziona la sua flotta senza farla vedere, poi si spara a turno.']];
            return $r;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::inizio', $e);
            throw $e;
        }
    }

    private static function eComputer(array $st, int $p): bool
    {
        try {
            return !empty($st['computer'][$p]);
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::eComputer', $e);
            throw $e;
        }
    }

    /** Chi deve agire adesso: nel posizionamento chi non è ancora pronto (prima le persone, poi il computer), poi chi ha il turno. */
    public static function attore(array $st): int
    {
        try {
            if ($st['fase'] === 'posizionamento') {
                foreach ([0, 1] as $i) {
                    if (!$st['pronto'][$i] && !self::eComputer($st, $i)) {
                        return $i;
                    }
                }
                foreach ([0, 1] as $i) {
                    if (!$st['pronto'][$i]) {
                        return $i;
                    }
                }
            }
            return (int) $st['turno'];
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::attore', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ il casellario
    public static function coordinate(int $r, int $c): string
    {
        try {
            return chr(65 + $c) . ' ' . ($r + 1);
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::coordinate', $e);
            throw $e;
        }
    }

    private static function dentro(int $r, int $c): bool
    {
        try {
            return $r >= 0 && $r < self::RIGHE && $c >= 0 && $c < self::COLONNE;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::dentro', $e);
            throw $e;
        }
    }

    /** Le caselle occupate da una flotta, come ["r,c" => indice della nave]. */
    private static function occupate(array $navi): array
    {
        try {
            $o = [];
            foreach ($navi as $k => $n) {
                foreach ($n['celle'] as [$r, $c]) {
                    $o[$r . ',' . $c] = $k;
                }
            }
            return $o;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::occupate', $e);
            throw $e;
        }
    }

    private static function cellePerNave(int $lunghezza, int $r, int $c, bool $orizzontale): array
    {
        try {
            $x = [];
            for ($i = 0; $i < $lunghezza; $i++) {
                $x[] = [$r + ($orizzontale ? 0 : $i), $c + ($orizzontale ? $i : 0)];
            }
            return $x;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::cellePerNave', $e);
            throw $e;
        }
    }

    private static function puoPosizionare(array $navi, bool $distanza, int $lunghezza, int $r, int $c, bool $orizzontale): bool
    {
        try {
            $occ = self::occupate($navi);
            foreach (self::cellePerNave($lunghezza, $r, $c, $orizzontale) as [$rr, $cc]) {
                if (!self::dentro($rr, $cc) || isset($occ[$rr . ',' . $cc])) {
                    return false;
                }
                if ($distanza) {
                    for ($dr = -1; $dr <= 1; $dr++) {
                        for ($dc = -1; $dc <= 1; $dc++) {
                            if (isset($occ[($rr + $dr) . ',' . ($cc + $dc)])) {
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::puoPosizionare', $e);
            throw $e;
        }
    }

    /** Mette a caso le navi che mancano (se non ci riesce toglie quelle messe e ricomincia). */
    private static function posizionaACaso(array &$st, int $p): void
    {
        try {
            $distanza = self::distanza($st['variante']);
            for ($giro = 0; $giro < 60; $giro++) {
                $navi = $st['navi'][$p];
                $da = $st['da_posizionare'][$p];
                if ($giro >= 30) {
                    $navi = [];
                    $da = self::flotta($st['variante']);
                }
                $ok = true;
                foreach ($da as $l) {
                    $messa = false;
                    for ($t = 0; $t < 300 && !$messa; $t++) {
                        $o = mt_rand(0, 1) === 1;
                        $r = mt_rand(0, self::RIGHE - 1);
                        $c = mt_rand(0, self::COLONNE - 1);
                        if (self::puoPosizionare($navi, $distanza, $l, $r, $c, $o)) {
                            $navi[] = ['l' => $l, 'celle' => self::cellePerNave($l, $r, $c, $o), 'colpite' => 0];
                            $messa = true;
                        }
                    }
                    if (!$messa) {
                        $ok = false;
                        break;
                    }
                }
                if ($ok) {
                    $st['navi'][$p] = $navi;
                    $st['da_posizionare'][$p] = [];
                    return;
                }
            }
            throw new RuntimeException('Non riesco a posizionare le navi.');
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::posizionaACaso', $e);
            throw $e;
        }
    }

    private static function naveAffondata(array $nave): bool
    {
        try {
            return $nave['colpite'] >= $nave['l'];
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::naveAffondata', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ mosse
    public static function mosse(array $st, int $posto): array
    {
        try {
            if ($st['finita'] || $posto < 0 || $posto > 1) {
                return [];
            }
            $m = [];
            if ($st['fase'] === 'posizionamento') {
                if ($st['pronto'][$posto]) {
                    return [];
                }
                if ($st['da_posizionare'][$posto]) {
                    $l = $st['da_posizionare'][$posto][0];
                    $m[] = ['id' => 'nave', 'testo' => 'Posiziona la nave da ' . $l . ' caselle', 'parametro' => ['tipo' => 'casella']];
                    $m[] = ['id' => 'casuale', 'testo' => 'Metti a caso le navi che mancano'];
                }
                if ($st['navi'][$posto]) {
                    $m[] = ['id' => 'annulla', 'testo' => 'Togli l\'ultima nave messa'];
                }
                if (!$st['da_posizionare'][$posto]) {
                    $m[] = ['id' => 'pronto', 'testo' => 'La flotta è pronta, si comincia'];
                }
                return $m;
            }
            if ((int) $st['turno'] === $posto) {
                $m[] = ['id' => 'spara', 'testo' => 'Spara su una casella', 'parametro' => ['tipo' => 'casella']];
            }
            return $m;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::mosse', $e);
            throw $e;
        }
    }

    public static function opzioni(array $st, int $posto): array
    {
        try {
            return [];
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::opzioni', $e);
            throw $e;
        }
    }

    /** La flotta di questo posto è pronta; se lo sono tutti e due comincia la battaglia. */
    private static function segnaPronto(array &$st, int $posto, array &$ev): void
    {
        try {
            $st['pronto'][$posto] = true;
            $ev[] = ['per' => null, 'testo' => $st['nomi'][$posto] . ' è pronto.'];
            if ($st['pronto'][0] && $st['pronto'][1]) {
                $st['fase'] = 'battaglia';
                $st['turno'] = mt_rand(0, 1);
                $ev[] = ['per' => null, 'testo' => 'La battaglia comincia. Tocca a ' . $st['nomi'][$st['turno']] . '.'];
            }
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::segnaPronto', $e);
            throw $e;
        }
    }

    /** Controlla che la mossa sia ammessa (id compreso). Solleva InvalidArgumentException altrimenti. */
    private static function controlla(array $st, int $p, string $id): void
    {
        try {
            if ($st['finita']) {
                throw new InvalidArgumentException('La partita è finita.');
            }
            $parti = explode('|', $id);
            if ($st['fase'] === 'posizionamento') {
                if ($st['pronto'][$p]) {
                    throw new InvalidArgumentException('Sei già pronto.');
                }
                switch ($parti[0]) {
                    case 'nave':
                        if (count($parti) !== 4 || !$st['da_posizionare'][$p] || !in_array($parti[3], ['o', 'v'], true)) {
                            throw new InvalidArgumentException('Nave non valida.');
                        }
                        if (!self::puoPosizionare($st['navi'][$p], self::distanza($st['variante']), $st['da_posizionare'][$p][0], (int) $parti[1], (int) $parti[2], $parti[3] === 'o')) {
                            throw new InvalidArgumentException('Qui la nave non ci sta.');
                        }
                        return;
                    case 'casuale':
                        if (!$st['da_posizionare'][$p]) {
                            throw new InvalidArgumentException('Le navi sono già tutte a posto.');
                        }
                        return;
                    case 'annulla':
                        if (!$st['navi'][$p]) {
                            throw new InvalidArgumentException('Non c\'è niente da togliere.');
                        }
                        return;
                    case 'pronto':
                        if ($st['da_posizionare'][$p]) {
                            throw new InvalidArgumentException('Mancano delle navi.');
                        }
                        return;
                }
                throw new InvalidArgumentException('Mossa sconosciuta.');
            }
            if ($parti[0] !== 'spara' || count($parti) !== 3 || (int) $st['turno'] !== $p) {
                throw new InvalidArgumentException('Mossa non valida.');
            }
            $r = (int) $parti[1];
            $c = (int) $parti[2];
            if (!self::dentro($r, $c)) {
                throw new InvalidArgumentException('Casella fuori dal casellario.');
            }
            if (isset($st['colpi'][$p][$r . ',' . $c])) {
                throw new InvalidArgumentException('Hai già sparato lì.');
            }
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::controlla', $e);
            throw $e;
        }
    }

    public static function applica(array &$st, int $posto, string $idMossa, bool $verifica = true): array
    {
        try {
            if ($verifica) {
                self::controlla($st, $posto, $idMossa);
            }
            $parti = explode('|', $idMossa);
            $nome = $st['nomi'][$posto];
            $ev = [];
            if ($st['fase'] === 'posizionamento') {
                switch ($parti[0]) {
                    case 'nave':
                        $l = $st['da_posizionare'][$posto][0];
                        $o = $parti[3] === 'o';
                        $celle = self::cellePerNave($l, (int) $parti[1], (int) $parti[2], $o);
                        $st['navi'][$posto][] = ['l' => $l, 'celle' => $celle, 'colpite' => 0];
                        array_shift($st['da_posizionare'][$posto]);
                        $fine = $celle[$l - 1];
                        $ev[] = ['per' => $posto, 'testo' => 'Nave da ' . $l . ' posizionata, ' . ($o ? 'orizzontale' : 'verticale') . ', da ' . self::coordinate($celle[0][0], $celle[0][1]) . ' a ' . self::coordinate($fine[0], $fine[1]) . '.', 'suono' => 'posiziona'];
                        break;
                    case 'casuale':
                        self::posizionaACaso($st, $posto);
                        $ev[] = ['per' => $posto, 'testo' => 'Le navi sono state messe a caso: la flotta è completa.', 'suono' => 'posiziona'];
                        break;
                    case 'annulla':
                        $nave = array_pop($st['navi'][$posto]);
                        array_unshift($st['da_posizionare'][$posto], $nave['l']);
                        $ev[] = ['per' => $posto, 'testo' => 'Tolta la nave da ' . $nave['l'] . '.'];
                        break;
                    case 'pronto':
                        self::segnaPronto($st, $posto, $ev);
                        break;
                    default:
                        throw new InvalidArgumentException('Mossa sconosciuta.');
                }
                // messa l'ultima nave la flotta è pronta da sola: non c'è altro da fare e non serve premere niente
                if (in_array($parti[0], ['nave', 'casuale'], true) && !$st['da_posizionare'][$posto] && !$st['pronto'][$posto]) {
                    self::segnaPronto($st, $posto, $ev);
                }
                return $ev;
            }

            if ($parti[0] !== 'spara') {
                throw new InvalidArgumentException('Mossa sconosciuta.');
            }
            $r = (int) $parti[1];
            $c = (int) $parti[2];
            $altro = 1 - $posto;
            $chiave = $r . ',' . $c;
            $dove = self::coordinate($r, $c);
            $ev[] = ['per' => null, 'testo' => $nome . ' spara su ' . $dove . '.', 'suono' => 'cannone'];
            $occ = self::occupate($st['navi'][$altro]);
            $st['ultimo_colpo'] = ['posto' => $posto, 'riga' => $r, 'colonna' => $c, 'esito' => 'acqua'];
            if (!isset($occ[$chiave])) {
                $st['colpi'][$posto][$chiave] = 'o';
                $ev[] = ['per' => null, 'testo' => 'Acqua! ' . self::INSULTI_ACQUA[array_rand(self::INSULTI_ACQUA)], 'suono' => 'acqua'];
                $st['turno'] = $altro;
                return $ev;
            }
            $k = $occ[$chiave];
            $st['colpi'][$posto][$chiave] = 'X';
            $st['navi'][$altro][$k]['colpite']++;
            $nave = $st['navi'][$altro][$k];
            if (!self::naveAffondata($nave)) {
                $st['ultimo_colpo']['esito'] = 'colpita';
                $ev[] = ['per' => null, 'testo' => 'Colpito! ' . self::COMPLIMENTI_COLPITO[array_rand(self::COMPLIMENTI_COLPITO)], 'suono' => 'colpito'];
            } else {
                $st['ultimo_colpo']['esito'] = 'affondata';
                $ev[] = ['per' => null, 'testo' => 'Colpito e affondato! ' . self::COMPLIMENTI_AFFONDATA[array_rand(self::COMPLIMENTI_AFFONDATA)] . ' ' . $st['nomi'][$altro] . ' perde la nave da ' . $nave['l'] . '.', 'suono' => 'affonda'];
                $rimaste = 0;
                foreach ($st['navi'][$altro] as $n) {
                    if (!self::naveAffondata($n)) {
                        $rimaste++;
                    }
                }
                if ($rimaste === 0) {
                    $st['finita'] = true;
                    $st['vincitore'] = $posto;
                    $st['motivo'] = 'Tutte le navi di ' . $st['nomi'][$altro] . ' sono affondate.';
                    $st['punteggi'] = [self::naviRimaste($st, 0), self::naviRimaste($st, 1)];
                    $ev[] = ['per' => null, 'testo' => 'La partita è finita: tutte le navi di ' . $st['nomi'][$altro] . ' sono affondate. Vince ' . $nome . '.', 'suono' => 'fine'];
                    return $ev;
                }
            }
            if (!self::raffica($st['variante'])) {
                $st['turno'] = $altro;
            }
            return $ev;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::applica', $e);
            throw $e;
        }
    }

    private static function naviRimaste(array $st, int $p): int
    {
        try {
            $n = 0;
            foreach ($st['navi'][$p] as $nave) {
                if (!self::naveAffondata($nave)) {
                    $n++;
                }
            }
            return $n;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::naviRimaste', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ viste
    /** Le righe del mare di $p: la sua flotta con i colpi che ha preso. */
    private static function mareMio(array $st, int $p): array
    {
        try {
            $g = array_fill(0, self::RIGHE, str_repeat('.', self::COLONNE));
            $set = function (int $r, int $c, string $x) use (&$g) {
                $g[$r][$c] = $x;
            };
            foreach ($st['navi'][$p] as $nave) {
                foreach ($nave['celle'] as [$r, $c]) {
                    $set($r, $c, 'N');
                }
            }
            $occ = self::occupate($st['navi'][$p]);
            foreach ($st['colpi'][1 - $p] as $chiave => $x) {
                [$r, $c] = array_map('intval', explode(',', $chiave));
                if ($x === 'o') {
                    $set($r, $c, 'o');
                } else {
                    $set($r, $c, self::naveAffondata($st['navi'][$p][$occ[$chiave]]) ? '#' : 'X');
                }
            }
            return $g;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::mareMio', $e);
            throw $e;
        }
    }

    /** Le righe del mare dell'avversario come le vede $p: solo i suoi colpi (con le navi, se la partita è finita). */
    private static function mareNemico(array $st, int $p, bool $svelato): array
    {
        try {
            $g = array_fill(0, self::RIGHE, str_repeat('.', self::COLONNE));
            $altro = 1 - $p;
            if ($svelato) {
                foreach ($st['navi'][$altro] as $nave) {
                    foreach ($nave['celle'] as [$r, $c]) {
                        $g[$r][$c] = 'N';
                    }
                }
            }
            $occ = self::occupate($st['navi'][$altro]);
            foreach ($st['colpi'][$p] as $chiave => $x) {
                [$r, $c] = array_map('intval', explode(',', $chiave));
                $g[$r][$c] = $x === 'o' ? 'o' : (self::naveAffondata($st['navi'][$altro][$occ[$chiave]]) ? '#' : 'X');
            }
            return $g;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::mareNemico', $e);
            throw $e;
        }
    }

    public static function vista(array $st, int $posto): array
    {
        try {
            $v = [
                'tipo' => 'battaglia',
                'variante' => $st['variante'],
                'fase' => $st['finita'] ? 'finita' : $st['fase'],
                'righe' => self::RIGHE,
                'colonne' => self::COLONNE,
                'distanza' => self::distanza($st['variante']),
                'raffica' => self::raffica($st['variante']),
                'turno' => (int) $st['turno'],
                'pronto' => $st['pronto'],
                'finita' => $st['finita'],
                'vincitore' => $st['vincitore'],
                'ultimo_colpo' => $st['ultimo_colpo'],
            ];
            if ($posto < 0 || $posto > 1) {
                return $v;
            }
            $altro = 1 - $posto;
            $v['mia'] = self::mareMio($st, $posto);
            $v['nemica'] = self::mareNemico($st, $posto, $st['finita']);
            $v['da_posizionare'] = $st['da_posizionare'][$posto];
            $v['prossima'] = $st['da_posizionare'][$posto] ? $st['da_posizionare'][$posto][0] : 0;
            $v['mie_navi'] = array_map(fn($n) => ['l' => $n['l'], 'colpite' => $n['colpite'], 'affondata' => self::naveAffondata($n)], $st['navi'][$posto]);
            // dell'avversario si sa solo quali navi ha già perso
            $perse = [];
            $vive = [];
            foreach ($st['navi'][$altro] as $n) {
                if (self::naveAffondata($n)) {
                    $perse[] = $n['l'];
                } else {
                    $vive[] = $n['l'];
                }
            }
            $v['navi_nemiche_perse'] = $perse;
            $v['navi_nemiche_numero'] = count($st['navi'][$altro]) ?: count(self::flotta($st['variante']));
            $v['navi_nemiche_rimaste'] = $st['finita'] ? $vive : null;
            $v['mie_navi_rimaste'] = self::naviRimaste($st, $posto);
            $v['nemiche_rimaste'] = count(self::flotta($st['variante'])) - count($perse);
            return $v;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::vista', $e);
            throw $e;
        }
    }

    private static function elencoNavi(array $lunghezze): string
    {
        try {
            $c = array_count_values($lunghezze);
            krsort($c);
            $t = [];
            foreach ($c as $l => $n) {
                $t[] = $n . ' da ' . $l;
            }
            return $t ? implode(', ', $t) : 'nessuna';
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::elencoNavi', $e);
            throw $e;
        }
    }

    public static function situazione(array $st, int $posto): string
    {
        try {
            if ($posto < 0 || $posto > 1) {
                return '';
            }
            $altro = 1 - $posto;
            if ($st['fase'] === 'posizionamento') {
                $t = 'Posizionamento. Hai messo ' . count($st['navi'][$posto]) . ' navi su ' . count(self::flotta($st['variante'])) . '.';
                if ($st['da_posizionare'][$posto]) {
                    $t .= ' Mancano: ' . self::elencoNavi($st['da_posizionare'][$posto]) . '. Ora la nave da ' . $st['da_posizionare'][$posto][0] . '.';
                } elseif (!$st['pronto'][$posto]) {
                    $t .= ' La flotta è completa: di\' che sei pronto.';
                } else {
                    $t .= ' Sei pronto: aspetti ' . $st['nomi'][$altro] . '.';
                }
                return $t;
            }
            $perse = [];
            foreach ($st['navi'][$altro] as $n) {
                if (self::naveAffondata($n)) {
                    $perse[] = $n['l'];
                }
            }
            $sparati = count($st['colpi'][$posto]);
            $colpite = count(array_filter($st['colpi'][$posto], fn($x) => $x === 'X'));
            $t = 'Hai ancora ' . self::naviRimaste($st, $posto) . ' navi, ' . $st['nomi'][$altro] . ' ne ha ' . (count(self::flotta($st['variante'])) - count($perse)) . '. Hai sparato ' . $sparati . ' colpi: ' . $colpite . ' sulle navi e ' . ($sparati - $colpite) . ' in acqua.';
            if ($perse) {
                $t .= ' Hai affondato: ' . self::elencoNavi($perse) . '.';
            }
            $t .= $st['finita'] ? ' La partita è finita.' : ' Tocca a ' . $st['nomi'][$st['turno']] . '.';
            return $t;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::situazione', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ computer
    /**
     * Il computer spara dove è più probabile che stia una nave: per ogni nave che manca si contano tutti i modi di metterla nel mare nemico senza
     * andare contro quello che si sa (acqua, navi affondate e, se le navi non si toccano, le caselle intorno alle navi affondate); le caselle
     * che stanno in più modi valgono di più, e molto di più quelle che prolungano una nave già colpita e non ancora affondata.
     */
    private static function sceltaColpo(array $st, int $p): string
    {
        try {
            $altro = 1 - $p;
            $acqua = [];
            $colpite = [];
            $occ = self::occupate($st['navi'][$altro]);
            foreach ($st['colpi'][$p] as $chiave => $x) {
                if ($x === 'o') {
                    $acqua[$chiave] = true;
                } elseif (self::naveAffondata($st['navi'][$altro][$occ[$chiave]])) {
                    $acqua[$chiave] = true;   // una nave affondata non si ricolpisce e non serve ad altre navi
                } else {
                    $colpite[$chiave] = true;
                }
            }
            $distanza = self::distanza($st['variante']);
            $mancano = self::flotta($st['variante']);
            foreach ($st['navi'][$altro] as $k => $n) {
                if (self::naveAffondata($n)) {
                    $i = array_search($n['l'], $mancano, true);
                    if ($i !== false) {
                        unset($mancano[$i]);
                    }
                    if ($distanza) {
                        foreach ($n['celle'] as [$r, $c]) {
                            for ($dr = -1; $dr <= 1; $dr++) {
                                for ($dc = -1; $dc <= 1; $dc++) {
                                    $q = ($r + $dr) . ',' . ($c + $dc);
                                    if (!isset($st['colpi'][$p][$q]) || $st['colpi'][$p][$q] === 'o') {
                                        $acqua[$q] = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            $peso = [];
            foreach ($mancano as $l) {
                foreach ([true, false] as $o) {
                    for ($r = 0; $r < self::RIGHE; $r++) {
                        for ($c = 0; $c < self::COLONNE; $c++) {
                            $celle = self::cellePerNave($l, $r, $c, $o);
                            $ok = true;
                            $toccate = 0;
                            foreach ($celle as [$rr, $cc]) {
                                $q = $rr . ',' . $cc;
                                if (!self::dentro($rr, $cc) || isset($acqua[$q])) {
                                    $ok = false;
                                    break;
                                }
                                if (isset($colpite[$q])) {
                                    $toccate++;
                                }
                            }
                            if (!$ok) {
                                continue;
                            }
                            $valore = $toccate > 0 ? 40 * $toccate : 1;
                            foreach ($celle as [$rr, $cc]) {
                                $q = $rr . ',' . $cc;
                                if (!isset($st['colpi'][$p][$q])) {
                                    $peso[$q] = ($peso[$q] ?? 0) + $valore;
                                }
                            }
                        }
                    }
                }
            }
            $migliori = [];
            $max = 0;
            foreach ($peso as $q => $w) {
                if ($w > $max) {
                    $max = $w;
                    $migliori = [$q];
                } elseif ($w === $max) {
                    $migliori[] = $q;
                }
            }
            if (!$migliori) {
                for ($r = 0; $r < self::RIGHE; $r++) {
                    for ($c = 0; $c < self::COLONNE; $c++) {
                        if (!isset($st['colpi'][$p][$r . ',' . $c])) {
                            $migliori[] = $r . ',' . $c;
                        }
                    }
                }
            }
            $q = $migliori[mt_rand(0, count($migliori) - 1)];
            return 'spara|' . str_replace(',', '|', $q);
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::sceltaColpo', $e);
            throw $e;
        }
    }

    /** Quante volte il computer spara a caso invece di cercare le navi, secondo il livello. */
    public const PROBABILITA_CASO = ['facile' => 0.85, 'normale' => 0.45, 'difficile' => 0.0];

    /** Una mossa a caso per il computer ai livelli facili: in posizionamento mette le navi a caso, in battaglia spara su una casella non ancora colpita. */
    public static function mossaCasuale(array $st, int $posto): ?string
    {
        try {
            if ($st['fase'] === 'posizionamento') {
                return $st['da_posizionare'][$posto] ? 'casuale' : null;
            }
            $libere = [];
            for ($r = 0; $r < self::RIGHE; $r++) {
                for ($c = 0; $c < self::COLONNE; $c++) {
                    if (!isset($st['colpi'][$posto][$r . ',' . $c])) {
                        $libere[] = 'spara|' . $r . '|' . $c;
                    }
                }
            }
            return $libere ? $libere[array_rand($libere)] : null;
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::mossaCasuale', $e);
            throw $e;
        }
    }

    public static function bot(array $st, int $posto): string
    {
        try {
            if ($st['fase'] === 'posizionamento') {
                return $st['da_posizionare'][$posto] ? 'casuale' : 'pronto';
            }
            return self::sceltaColpo($st, $posto);
        } catch (Throwable $e) {
            registra_errore('BattagliaNavale::bot', $e);
            throw $e;
        }
    }
}
