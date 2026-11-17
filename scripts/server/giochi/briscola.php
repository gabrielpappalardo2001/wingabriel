<?php
// Briscola per 2, 3, 4 o 5 giocatori (uno contro uno, a coppie, a 3, o a chiamata in 5).
//
// Regole e varianti:
//  - Classica (2 o 4 giocatori): 40 carte, 3 carte a testa, carta scoperta in fondo al mazzo briscola. Vince chi supera 60 punti su 120.
//  - Al meglio delle 3 smazzate: vince chi per primo si aggiudica 2 smazzate da 61 punti.
//  - Briscola a chiamata (5 giocatori): 40 carte distribuite tutte (8 a testa). Chi apre chiama una carta e il seme diventa briscola;
//    chi possiede la carta chiamata è il compagno segreto. Chiamante e compagno vincono se fanno almeno 61 punti insieme, altrimenti vincono i 3 difensori.
//  - Marianna (con accuso): dichiarando Re e Cavallo dello stesso seme dopo una presa si ottengono 20 punti (40 se briscola). Si gioca a 250 punti.
//  - Briscola scoperta: le carte di tutti i giocatori sono scoperte sul tavolo per una partita a viso aperto.
//  - Briscolino (3 giocatori): si toglie un due (39 carte), 13 a testa con prese individuali.

declare(strict_types=1);

require_once __DIR__ . '/mazzo-italiano.php';

final class Briscola
{
    /** Quanto vale ogni posizione della carta (0 asso ... 9 re). */
    private const PUNTI = [11, 0, 10, 0, 0, 0, 0, 2, 3, 4];
    /** Forza di ogni posizione per decidere chi prende: più alto è più forte. */
    private const FORZA = [10, 1, 9, 2, 3, 4, 5, 6, 7, 8];
    public const VARIANTI = [
        'classica' => ['nome' => 'Classica', 'descrizione' => 'Quaranta carte, tre carte a testa, vince chi supera 60 punti su 120.', 'posti' => [2, 4]],
        'al_meglio' => ['nome' => 'Al meglio delle tre', 'descrizione' => 'Vince chi per primo si aggiudica due smazzate da 61 punti.', 'posti' => [2, 4]],
        'chiamata' => ['nome' => 'A chiamata', 'descrizione' => 'In cinque, otto carte a testa. Chi apre chiama una carta e il suo seme diventa briscola: chi la possiede e il compagno segreto.', 'posti' => [5]],
        'marianna' => ['nome' => 'Marianna con accusi', 'descrizione' => 'Re e cavallo dello stesso seme dopo una presa valgono 20 punti, 40 se e briscola. Si gioca a 250 punti.', 'posti' => [2, 4]],
        'scoperta' => ['nome' => 'Scoperta', 'descrizione' => 'Le carte di tutti sono scoperte sul tavolo.', 'posti' => [2, 4]],
        'briscolino' => ['nome' => 'Briscolino', 'descrizione' => 'In tre. Si toglie un due, tredici carte a testa, prese individuali.', 'posti' => [3]],
    ];

    public static function punti(int $id): int
    {
        try {
            return self::PUNTI[$id % 10];
        } catch (Throwable $e) {
            registra_errore('Briscola::punti', $e);
            throw $e;
        }
    }

    private static function forza(int $id): int
    {
        try {
            return self::FORZA[$id % 10];
        } catch (Throwable $e) {
            registra_errore('Briscola::forza', $e);
            throw $e;
        }
    }

    public static function squadraDi(array $st, int $posto): int
    {
        try {
            $v = $st['variante'] ?? 'classica';
            if ($v === 'chiamata') {
                $chiamante = $st['chiamante'] ?? 0;
                $socio = $st['socio'] ?? -1;
                return ($posto === $chiamante || $posto === $socio) ? 0 : 1;
            }
            if ($st['n'] === 4) {
                return $posto % 2;
            }
            return $posto;
        } catch (Throwable $e) {
            registra_errore('Briscola::squadraDi', $e);
            throw $e;
        }
    }

    public static function nuova(array $nomi, string $variante = 'classica', int $puntiObiettivo = 0): array
    {
        try {
            $n = count($nomi);
            $variantiValide = ['classica', 'al_meglio', 'chiamata', 'marianna', 'scoperta', 'briscolino'];
            $variante = in_array($variante, $variantiValide, true) ? $variante : ($n === 5 ? 'chiamata' : ($n === 3 ? 'briscolino' : 'classica'));
            if ($variante === 'chiamata' && $n !== 5) {
                $n = 5;
            } elseif ($variante === 'briscolino' && $n !== 3) {
                $n = 3;
            } elseif (($variante === 'classica' || $variante === 'al_meglio' || $variante === 'marianna' || $variante === 'scoperta') && $n !== 2 && $n !== 4) {
                $n = $n >= 4 ? 4 : 2;
            }

            $numSquadre = $variante === 'briscolino' ? 3 : 2;
            $st = [
                'tipo' => 'briscola',
                'variante' => $variante,
                'n' => $n,
                'nomi' => array_values($nomi),
                'smazzata' => 1,
                'totali_smazzate' => array_fill(0, $numSquadre, 0),
                'totali_punti' => array_fill(0, $numSquadre, 0),
                'obiettivo_marianna' => $puntiObiettivo > 0 ? $puntiObiettivo : 250,
                'finita' => false,
                'vincitori' => [],
                'punteggi' => null,
                'motivo' => '',
            ];
            self::preparaMano($st, 0);
            return $st;
        } catch (Throwable $e) {
            registra_errore('Briscola::nuova', $e);
            throw $e;
        }
    }

    private static function preparaMano(array &$st, int $primo): void
    {
        try {
            $n = $st['n'];
            $variante = $st['variante'];
            $st['turno'] = $primo;
            $st['tavolo'] = [];
            $st['ultima_presa'] = null;
            $numSquadre = $variante === 'briscolino' ? 3 : 2;
            $st['prese'] = array_fill(0, $numSquadre, []);
            $st['accusi'] = array_fill(0, $numSquadre, 0);
            $st['accusi_dichiarati'] = [];
            $st['chi_ha_preso_ultimo'] = null;

            if ($variante === 'chiamata') {
                $mazzo = MazzoItaliano::mescolato();
                $mani = [];
                for ($i = 0; $i < $n; $i++) {
                    $mani[] = [];
                }
                for ($giro = 0; $giro < 8; $giro++) {
                    for ($i = 0; $i < $n; $i++) {
                        $mani[$i][] = array_shift($mazzo);
                    }
                }
                foreach ($mani as $i => $m) {
                    $mani[$i] = MazzoItaliano::ordina($m);
                }
                $st['mazzo'] = [];
                $st['mani'] = $mani;
                $st['chiamante'] = $primo;
                $st['fase_chiamata'] = true;
                $st['carta_chiamata'] = null;
                $st['socio'] = null;
                $st['socio_scoperto'] = false;
                $st['briscola'] = null;
            } elseif ($variante === 'briscolino') {
                // Rimuove il due di coppe (id 1) per avere 39 carte (13 a testa)
                $mazzoCompleto = MazzoItaliano::mescolato();
                $mazzo = array_values(array_filter($mazzoCompleto, fn($c) => $c !== 1));
                $mani = array_fill(0, $n, []);
                for ($giro = 0; $giro < 3; $giro++) {
                    for ($i = 0; $i < $n; $i++) {
                        $mani[$i][] = array_shift($mazzo);
                    }
                }
                foreach ($mani as $i => $m) {
                    $mani[$i] = MazzoItaliano::ordina($m);
                }
                $st['mazzo'] = $mazzo;
                $st['mani'] = $mani;
                $st['briscola'] = $mazzo[count($mazzo) - 1];
                $st['fase_chiamata'] = false;
            } else {
                $mazzo = MazzoItaliano::mescolato();
                $mani = array_fill(0, $n, []);
                for ($giro = 0; $giro < 3; $giro++) {
                    for ($i = 0; $i < $n; $i++) {
                        $mani[$i][] = array_shift($mazzo);
                    }
                }
                foreach ($mani as $i => $m) {
                    $mani[$i] = MazzoItaliano::ordina($m);
                }
                $st['mazzo'] = $mazzo;
                $st['mani'] = $mani;
                $st['briscola'] = $mazzo[count($mazzo) - 1];
                $st['fase_chiamata'] = false;
            }
        } catch (Throwable $e) {
            registra_errore('Briscola::preparaMano', $e);
            throw $e;
        }
    }

    public static function inizio(array $st): array
    {
        try {
            $v = $st['variante'];
            if ($v === 'chiamata') {
                $r = [['per' => null, 'testo' => 'Si gioca a Briscola a chiamata (in 5 giocatori). 8 carte a testa, tutto il mazzo distribuito. ' . $st['nomi'][$st['turno']] . ' deve chiamare la carta!']];
                foreach ($st['mani'] as $i => $m) {
                    $r[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
                }
                return $r;
            }

            $b = $st['briscola'];
            $nomeB = MazzoItaliano::nome($b);
            $semeB = MazzoItaliano::SEMI[MazzoItaliano::seme($b)];
            $testoInizio = 'Si gioca a briscola';
            if ($v === 'al_meglio') {
                $testoInizio .= ' al meglio delle 3 smazzate';
            } elseif ($v === 'marianna') {
                $testoInizio .= ' (Marianna con accuso fino a ' . $st['obiettivo_marianna'] . ' punti)';
            } elseif ($v === 'scoperta') {
                $testoInizio .= ' scoperta (carte visibili)';
            } elseif ($v === 'briscolino') {
                $testoInizio .= ' a tre giocatori (Briscolino)';
            }
            $testoInizio .= '. La briscola è ' . $semeB . ': la carta in fondo al mazzo è ' . $nomeB . '. Inizia ' . $st['nomi'][$st['turno']] . '.';

            $r = [['per' => null, 'testo' => $testoInizio]];
            foreach ($st['mani'] as $i => $m) {
                $r[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Briscola::inizio', $e);
            throw $e;
        }
    }

    public static function mosse(array $st, int $posto): array
    {
        try {
            if ($st['finita'] || $posto !== $st['turno']) {
                return [];
            }
            $r = [];

            // Fase chiamata (variante a chiamata)
            if (!empty($st['fase_chiamata'])) {
                $mano = $st['mani'][$posto] ?? [];
                // Carte chiamabili: Assi, Tre, Re non posseduti in mano
                $candidati = [];
                foreach ([0, 1, 2, 3] as $seme) {
                    foreach ([0, 2, 9] as $pos) { // Asso (0), Tre (2), Re (9)
                        $cid = $seme * 10 + $pos;
                        if (!in_array($cid, $mano, true)) {
                            $candidati[] = $cid;
                        }
                    }
                }
                foreach ($candidati as $cid) {
                    $r[] = ['id' => 'chiama|' . $cid, 'testo' => 'Chiama: ' . MazzoItaliano::nome($cid)];
                }
                return $r;
            }

            // Accuso per Marianna
            if ($st['variante'] === 'marianna' && count($st['tavolo']) === 0 && ($st['chi_ha_preso_ultimo'] === $posto || $st['chi_ha_preso_ultimo'] === null)) {
                $mano = $st['mani'][$posto];
                $semeBriscola = MazzoItaliano::seme($st['briscola']);
                for ($s = 0; $s < 4; $s++) {
                    if (in_array($s, $st['accusi_dichiarati'], true)) {
                        continue;
                    }
                    $haRe = in_array($s * 10 + 9, $mano, true);
                    $haCavallo = in_array($s * 10 + 8, $mano, true);
                    if ($haRe && $haCavallo) {
                        $bonus = ($s === $semeBriscola) ? 40 : 20;
                        $r[] = ['id' => 'accuso|' . $s, 'testo' => 'Accusa Marianna di ' . MazzoItaliano::SEMI[$s] . ' (+' . $bonus . ' punti)'];
                    }
                }
            }

            $semeB = $st['briscola'] !== null ? MazzoItaliano::seme($st['briscola']) : -1;
            foreach ($st['mani'][$posto] as $id) {
                $punti = self::punti($id);
                $testo = MazzoItaliano::nome($id) . ', ' . $punti . ($punti === 1 ? ' punto' : ' punti');
                if ($semeB >= 0 && MazzoItaliano::seme($id) === $semeB) {
                    $testo .= ', briscola';
                }
                $r[] = ['id' => 'gioca|' . $id, 'testo' => $testo];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Briscola::mosse', $e);
            throw $e;
        }
    }

    /** Chi sta vincendo la mano fra le carte sul tavolo: restituisce la posizione nell'elenco del tavolo. */
    private static function vincenteTavolo(array $st): int
    {
        try {
            $semeBriscola = MazzoItaliano::seme($st['briscola']);
            $miglior = 0;
            for ($i = 1; $i < count($st['tavolo']); $i++) {
                if (self::batte($st['tavolo'][$i][1], $st['tavolo'][$miglior][1], $semeBriscola, MazzoItaliano::seme($st['tavolo'][0][1]))) {
                    $miglior = $i;
                }
            }
            return $miglior;
        } catch (Throwable $e) {
            registra_errore('Briscola::vincenteTavolo', $e);
            throw $e;
        }
    }

    /** La carta $a batte la carta $b? ($semeUscita è il seme giocato per primo.) */
    private static function batte(int $a, int $b, int $semeBriscola, int $semeUscita): bool
    {
        try {
            $sa = MazzoItaliano::seme($a);
            $sb = MazzoItaliano::seme($b);
            if ($sa === $sb) {
                return self::forza($a) > self::forza($b);
            }
            if ($sa === $semeBriscola) {
                return true;
            }
            if ($sb === $semeBriscola) {
                return false;
            }
            return false; // semi diversi senza briscola: vale il primo (la carta che c'era prima)
        } catch (Throwable $e) {
            registra_errore('Briscola::batte', $e);
            throw $e;
        }
    }

    public static function applica(array &$st, int $posto, string $idMossa, bool $verifica = true): array
    {
        try {
            if ($verifica) {
                $ok = false;
                foreach (self::mosse($st, $posto) as $m) {
                    if ($m['id'] === $idMossa) {
                        $ok = true;
                        break;
                    }
                }
                if (!$ok) {
                    throw new InvalidArgumentException('Mossa non consentita.');
                }
            }
            $parti = explode('|', $idMossa);

            // Chiamata della carta
            if ($parti[0] === 'chiama' && isset($parti[1])) {
                $idCarta = (int) $parti[1];
                $st['carta_chiamata'] = $idCarta;
                $st['briscola'] = $idCarta;
                $st['fase_chiamata'] = false;
                // Trova chi ha la carta
                $socio = null;
                foreach ($st['mani'] as $p => $m) {
                    if (in_array($idCarta, $m, true)) {
                        $socio = $p;
                        break;
                    }
                }
                $st['socio'] = $socio;
                $st['socio_scoperto'] = false;
                $semeNome = MazzoItaliano::SEMI[MazzoItaliano::seme($idCarta)];
                $nomeCarta = MazzoItaliano::nome($idCarta);
                $eventi = [
                    ['per' => null, 'testo' => $st['nomi'][$posto] . ' chiama ' . $nomeCarta . '! Il seme di briscola è ' . $semeNome . '. Chi ha la carta è il compagno segreto.'],
                ];
                $st['turno'] = $posto; // Il chiamante gioca la prima carta
                return $eventi;
            }

            // Accuso per Marianna
            if ($parti[0] === 'accuso' && isset($parti[1])) {
                $seme = (int) $parti[1];
                $st['accusi_dichiarati'][] = $seme;
                $bonus = ($seme === MazzoItaliano::seme($st['briscola'])) ? 40 : 20;
                $sq = self::squadraDi($st, $posto);
                $st['accusi'][$sq] += $bonus;
                $eventi = [
                    ['per' => null, 'testo' => $st['nomi'][$posto] . ' accusa Marianna di ' . MazzoItaliano::SEMI[$seme] . ': +' . $bonus . ' punti per la squadra!'],
                ];
                return $eventi;
            }

            if ($parti[0] !== 'gioca' || !isset($parti[1])) {
                throw new InvalidArgumentException('Mossa non consentita.');
            }
            $id = (int) $parti[1];
            $chiave = array_search($id, $st['mani'][$posto], true);
            if ($chiave === false) {
                throw new InvalidArgumentException('Mossa non consentita.');
            }
            array_splice($st['mani'][$posto], (int) $chiave, 1);
            $st['tavolo'][] = [$posto, $id];
            $nome = $st['nomi'][$posto];
            $eventi = [['per' => null, 'testo' => $nome . ' gioca ' . MazzoItaliano::nome($id) . '.']];

            // Rivelazione del compagno segreto nella briscola a chiamata
            if ($st['variante'] === 'chiamata' && $id === $st['carta_chiamata'] && !$st['socio_scoperto']) {
                $st['socio_scoperto'] = true;
                $eventi[] = ['per' => null, 'testo' => $nome . ' ha giocato la carta chiamata ed è il compagno di ' . $st['nomi'][$st['chiamante']] . '!'];
            }

            if (count($st['tavolo']) < $st['n']) {
                $st['turno'] = ($posto + 1) % $st['n'];
                return $eventi;
            }

            // la mano è completa: prende chi vince
            $v = self::vincenteTavolo($st);
            $vincitore = $st['tavolo'][$v][0];
            $punti = 0;
            $carte = [];
            foreach ($st['tavolo'] as [$p, $c]) {
                $punti += self::punti($c);
                $carte[] = $c;
            }
            $sq = self::squadraDi($st, $vincitore);
            foreach ($carte as $c) {
                $st['prese'][$sq][] = $c;
            }
            $st['ultima_presa'] = ['vincitore' => $vincitore, 'carte' => $carte, 'punti' => $punti];
            $st['chi_ha_preso_ultimo'] = $vincitore;
            $st['tavolo'] = [];
            $eventi[] = ['per' => null, 'testo' => $st['nomi'][$vincitore] . ' prende con ' . MazzoItaliano::nome($st['ultima_presa']['carte'][$v]) . ': ' . $punti . ($punti === 1 ? ' punto' : ' punti') . '.'];

            // si pesca, cominciando da chi ha preso
            if (count($st['mazzo']) > 0) {
                for ($i = 0; $i < $st['n'] && count($st['mazzo']) > 0; $i++) {
                    $chi = ($vincitore + $i) % $st['n'];
                    $c = array_shift($st['mazzo']);
                    $st['mani'][$chi] = MazzoItaliano::ordina(array_merge($st['mani'][$chi], [$c]));
                    $eventi[] = ['per' => $chi, 'testo' => 'Hai pescato: ' . MazzoItaliano::nome($c) . '.'];
                }
                if (count($st['mazzo']) === 0) {
                    $eventi[] = ['per' => null, 'testo' => 'Il mazzo è finito: non si pesca più.'];
                }
            }
            $st['turno'] = $vincitore;

            $rimaste = 0;
            foreach ($st['mani'] as $m) {
                $rimaste += count($m);
            }
            if ($rimaste === 0) {
                self::concludi($st, $eventi);
            }
            return $eventi;
        } catch (Throwable $e) {
            registra_errore('Briscola::applica', $e);
            throw $e;
        }
    }

    private static function puntiSquadra(array $st, int $sq): int
    {
        try {
            $p = $st['accusi'][$sq] ?? 0;
            if (isset($st['prese'][$sq])) {
                foreach ($st['prese'][$sq] as $c) {
                    $p += self::punti($c);
                }
            }
            return $p;
        } catch (Throwable $e) {
            registra_errore('Briscola::puntiSquadra', $e);
            throw $e;
        }
    }

    private static function nomiSquadra(array $st, int $sq): string
    {
        try {
            $v = $st['variante'] ?? 'classica';
            if ($v === 'chiamata') {
                if ($sq === 0) {
                    $c = $st['nomi'][$st['chiamante']];
                    $s = $st['socio'] !== null ? $st['nomi'][$st['socio']] : 'compagno';
                    return $st['socio_scoperto'] || $st['finita'] ? "$c e $s" : "$c e compagno";
                }
                $difensori = [];
                for ($i = 0; $i < $st['n']; $i++) {
                    if ($i !== $st['chiamante'] && $i !== $st['socio']) {
                        $difensori[] = $st['nomi'][$i];
                    }
                }
                return implode(', ', $difensori);
            }
            if ($v === 'briscolino') {
                return $st['nomi'][$sq];
            }
            if ($st['n'] === 2) {
                return $st['nomi'][$sq];
            }
            return $st['nomi'][$sq] . ' e ' . $st['nomi'][$sq + 2];
        } catch (Throwable $e) {
            registra_errore('Briscola::nomiSquadra', $e);
            throw $e;
        }
    }

    private static function concludi(array &$st, array &$eventi): void
    {
        try {
            $v = $st['variante'];
            if ($v === 'briscolino') {
                $p = [self::puntiSquadra($st, 0), self::puntiSquadra($st, 1), self::puntiSquadra($st, 2)];
                $st['finita'] = true;
                $st['punteggi'] = [
                    ['punti' => $p[0], 'carte' => count($st['prese'][0])],
                    ['punti' => $p[1], 'carte' => count($st['prese'][1])],
                    ['punti' => $p[2], 'carte' => count($st['prese'][2])],
                ];
                $maxP = max($p);
                $vincenti = [];
                for ($i = 0; $i < 3; $i++) {
                    if ($p[$i] === $maxP) $vincenti[] = $i;
                }
                if (count($vincenti) > 1) {
                    $st['vincitori'] = [];
                    $st['motivo'] = 'Pareggio a ' . $maxP . ' punti.';
                } else {
                    $st['vincitori'] = $vincenti;
                    $st['motivo'] = 'Vince ' . $st['nomi'][$vincenti[0]] . ' con ' . $maxP . ' punti.';
                }
                $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
                return;
            }

            $p = [self::puntiSquadra($st, 0), self::puntiSquadra($st, 1)];

            if ($v === 'chiamata') {
                $st['finita'] = true;
                $st['socio_scoperto'] = true;
                $st['punteggi'] = [
                    ['punti' => $p[0], 'carte' => count($st['prese'][0])],
                    ['punti' => $p[1], 'carte' => count($st['prese'][1])],
                ];
                $socio = $st['socio'] !== null ? $st['socio'] : $st['chiamante'];
                if ($p[0] >= 61) {
                    $st['vincitori'] = array_values(array_unique([$st['chiamante'], $socio]));
                    $st['motivo'] = 'Vincono ' . $st['nomi'][$st['chiamante']] . ' e ' . $st['nomi'][$socio] . ' con ' . $p[0] . ' punti contro ' . $p[1] . '.';
                } else {
                    $difensori = [];
                    for ($i = 0; $i < 5; $i++) {
                        if ($i !== $st['chiamante'] && $i !== $socio) $difensori[] = $i;
                    }
                    $st['vincitori'] = $difensori;
                    $st['motivo'] = 'Vincono i difensori (' . self::nomiSquadra($st, 1) . ') con ' . $p[1] . ' punti contro ' . $p[0] . '.';
                }
                $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
                return;
            }

            if ($v === 'al_meglio') {
                $vincitoreMano = $p[0] > $p[1] ? 0 : ($p[1] > $p[0] ? 1 : -1);
                if ($vincitoreMano >= 0) {
                    $st['totali_smazzate'][$vincitoreMano]++;
                }
                $s0 = $st['totali_smazzate'][0];
                $s1 = $st['totali_smazzate'][1];
                $eventi[] = ['per' => null, 'testo' => 'Smazzata ' . $st['smazzata'] . ' conclusa: ' . self::nomiSquadra($st, 0) . ' ' . $p[0] . ' punti, ' . self::nomiSquadra($st, 1) . ' ' . $p[1] . ' punti. Smazzate vinte: ' . $s0 . ' a ' . $s1 . '.'];

                if ($s0 >= 2 || $s1 >= 2) {
                    $st['finita'] = true;
                    $vinc = $s0 >= 2 ? 0 : 1;
                    $st['vincitori'] = $st['n'] === 2 ? [$vinc] : [$vinc, $vinc + 2];
                    $st['motivo'] = 'Vince la partita ' . self::nomiSquadra($st, $vinc) . ' per ' . $st['totali_smazzate'][$vinc] . ' smazzate a ' . $st['totali_smazzate'][1 - $vinc] . '.';
                    $st['punteggi'] = [
                        ['punti' => $s0, 'carte' => count($st['prese'][0])],
                        ['punti' => $s1, 'carte' => count($st['prese'][1])],
                    ];
                    $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
                    return;
                }
                $st['smazzata']++;
                $primo = ($st['smazzata'] - 1) % $st['n'];
                self::preparaMano($st, $primo);
                $eventi[] = ['per' => null, 'testo' => 'Comincia la smazzata ' . $st['smazzata'] . '. Inizia ' . $st['nomi'][$primo] . '.'];
                foreach ($st['mani'] as $i => $m) {
                    $eventi[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
                }
                return;
            }

            if ($v === 'marianna') {
                $st['totali_punti'][0] += $p[0];
                $st['totali_punti'][1] += $p[1];
                $t0 = $st['totali_punti'][0];
                $t1 = $st['totali_punti'][1];
                $eventi[] = ['per' => null, 'testo' => 'Mano ' . $st['smazzata'] . ' finita: ' . self::nomiSquadra($st, 0) . ' +' . $p[0] . ' (totale ' . $t0 . '), ' . self::nomiSquadra($st, 1) . ' +' . $p[1] . ' (totale ' . $t1 . '). Obiettivo: ' . $st['obiettivo_marianna'] . ' punti.'];

                if (($t0 >= $st['obiettivo_marianna'] || $t1 >= $st['obiettivo_marianna']) && $t0 !== $t1) {
                    $st['finita'] = true;
                    $vinc = $t0 > $t1 ? 0 : 1;
                    $st['vincitori'] = $st['n'] === 2 ? [$vinc] : [$vinc, $vinc + 2];
                    $st['motivo'] = 'Vince ' . self::nomiSquadra($st, $vinc) . ' con ' . $st['totali_punti'][$vinc] . ' punti contro ' . $st['totali_punti'][1 - $vinc] . '.';
                    $st['punteggi'] = [
                        ['punti' => $t0, 'carte' => count($st['prese'][0])],
                        ['punti' => $t1, 'carte' => count($st['prese'][1])],
                    ];
                    $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
                    return;
                }
                $st['smazzata']++;
                $primo = ($st['smazzata'] - 1) % $st['n'];
                self::preparaMano($st, $primo);
                $eventi[] = ['per' => null, 'testo' => 'Comincia la mano ' . $st['smazzata'] . '. Inizia ' . $st['nomi'][$primo] . '.'];
                foreach ($st['mani'] as $i => $m) {
                    $eventi[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
                }
                return;
            }

            // Classica o Scoperta
            $st['finita'] = true;
            $st['punteggi'] = [
                ['punti' => $p[0], 'carte' => count($st['prese'][0])],
                ['punti' => $p[1], 'carte' => count($st['prese'][1])],
            ];
            if ($p[0] === $p[1]) {
                $st['vincitori'] = [];
                $st['motivo'] = 'Pareggio: ' . $p[0] . ' punti a testa.';
            } else {
                $vinc = $p[0] > $p[1] ? 0 : 1;
                $st['vincitori'] = $st['n'] === 2 ? [$vinc] : [$vinc, $vinc + 2];
                $st['motivo'] = 'Vince ' . self::nomiSquadra($st, $vinc) . ' con ' . $p[$vinc] . ' punti contro ' . $p[1 - $vinc] . '.';
            }
            $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
        } catch (Throwable $e) {
            registra_errore('Briscola::concludi', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ vista
    public static function vista(array $st, int $posto): array
    {
        try {
            $v = $st['variante'];
            $mioSq = $posto >= 0 ? self::squadraDi($st, $posto) : -1;
            $tavolo = [];
            foreach ($st['tavolo'] as [$p, $c]) {
                $x = MazzoItaliano::carta($c);
                $x['da'] = $st['nomi'][$p];
                $tavolo[] = $x;
            }
            $briscola = $st['briscola'] !== null ? MazzoItaliano::carta($st['briscola']) : null;
            $semeB = $st['briscola'] !== null ? MazzoItaliano::SEMI[MazzoItaliano::seme($st['briscola'])] : '';
            $numSquadre = $v === 'briscolino' ? 3 : 2;
            $squadre = [];
            for ($sq = 0; $sq < $numSquadre; $sq++) {
                $membri = [];
                for ($p = 0; $p < $st['n']; $p++) {
                    if (self::squadraDi($st, $p) === $sq) {
                        $membri[] = $p;
                    }
                }
                // In chiamata prima della scoperta i punti del socio restano riservati a fine smazzata
                $vedi = $st['finita'] || ($v === 'chiamata' ? $st['socio_scoperto'] : ($sq === $mioSq));
                $squadre[] = [
                    'nome' => self::nomiSquadra($st, $sq),
                    'membri' => $membri,
                    'carte_prese' => count($st['prese'][$sq]),
                    'punti' => $vedi ? self::puntiSquadra($st, $sq) : null,
                    'accusi' => $st['accusi'][$sq] ?? 0,
                ];
            }
            $riepilogo = [];
            if ($semeB !== '') {
                $riepilogo[] = 'Briscola: ' . $semeB . (count($st['mazzo']) > 0 ? ', carta in fondo al mazzo: ' . MazzoItaliano::nome($st['briscola']) : ($v === 'chiamata' ? ' (chiamata)' : ', mazzo finito'));
            } else {
                $riepilogo[] = 'In attesa della chiamata della carta di briscola';
            }
            if ($v !== 'chiamata') {
                $riepilogo[] = 'Mazzo: ' . count($st['mazzo']) . ' carte';
            }
            if ($mioSq >= 0 && isset($st['prese'][$mioSq])) {
                $riepilogo[] = 'Prese squadra: ' . count($st['prese'][$mioSq]) . ' carte per ' . self::puntiSquadra($st, $mioSq) . ' punti';
            }

            $maniScoperte = null;
            if ($v === 'scoperta' || $st['finita']) {
                $maniScoperte = [];
                foreach ($st['mani'] as $m) {
                    $maniScoperte[] = array_map(fn($id) => MazzoItaliano::carta($id), $m);
                }
            }

            return [
                'tipo' => 'briscola',
                'variante' => $v,
                'n' => $st['n'],
                'nomi' => $st['nomi'],
                'turno' => $st['turno'],
                'fase_chiamata' => !empty($st['fase_chiamata']),
                'carta_chiamata' => ($st['carta_chiamata'] ?? null) !== null ? MazzoItaliano::carta($st['carta_chiamata']) : null,
                'socio_scoperto' => !empty($st['socio_scoperto']),
                'mazzo' => count($st['mazzo']),
                'briscola' => $briscola,
                'seme_briscola' => $semeB,
                'tavolo_carte' => $tavolo,
                'ultima_presa' => $st['ultima_presa'] === null ? null : [
                    'vincitore' => $st['ultima_presa']['vincitore'],
                    'punti' => $st['ultima_presa']['punti'],
                    'carte' => array_map(fn($c) => MazzoItaliano::carta($c), $st['ultima_presa']['carte']),
                ],
                'mano' => array_map(fn($id) => MazzoItaliano::carta($id), $posto >= 0 && isset($st['mani'][$posto]) ? $st['mani'][$posto] : []),
                'mani_scoperte' => $maniScoperte,
                'carte_in_mano' => array_map('count', $st['mani']),
                'squadre' => $squadre,
                'mia_squadra' => $mioSq,
                'smazzata' => $st['smazzata'] ?? 1,
                'totali_smazzate' => $st['totali_smazzate'] ?? null,
                'totali_punti' => $st['totali_punti'] ?? null,
                'finita' => $st['finita'],
                'punteggi' => $st['punteggi'],
                'vincitori' => $st['vincitori'],
                'motivo' => $st['motivo'],
                'riepilogo' => $riepilogo,
                'testo_tavolo' => self::testoTavolo($st),
                'testo_punti' => self::testoPunti($st, $mioSq),
            ];
        } catch (Throwable $e) {
            registra_errore('Briscola::vista', $e);
            throw $e;
        }
    }

    private static function testoTavolo(array $st): string
    {
        try {
            $v = $st['variante'];
            $t = '';
            if ($st['briscola'] !== null) {
                $t .= 'Briscola: ' . MazzoItaliano::nome($st['briscola']) . (count($st['mazzo']) > 0 ? ', carta in fondo al mazzo. ' : '. ');
            } elseif (!empty($st['fase_chiamata'])) {
                $t .= 'In attesa della chiamata. ';
            }
            if ($v !== 'chiamata') {
                $t .= 'Mazzo: ' . count($st['mazzo']) . ' carte. ';
            }
            if (!$st['tavolo']) {
                $t .= 'Sul tavolo non ci sono carte.';
            } else {
                $parti = [];
                foreach ($st['tavolo'] as [$p, $c]) {
                    $parti[] = $st['nomi'][$p] . ' ha giocato ' . MazzoItaliano::nome($c);
                }
                $t .= 'Sul tavolo: ' . implode('; ', $parti) . '.';
            }
            $mani = [];
            foreach ($st['mani'] as $i => $m) {
                $n = count($m);
                $mani[] = $st['nomi'][$i] . ' ' . $n . ($n === 1 ? ' carta' : ' carte');
            }
            $t .= ' Carte in mano: ' . implode(', ', $mani) . '.';
            if ($st['ultima_presa'] !== null) {
                $u = $st['ultima_presa'];
                $t .= ' Ultima presa: ' . $st['nomi'][$u['vincitore']] . ' con ' . $u['punti'] . ($u['punti'] === 1 ? ' punto' : ' punti') . '.';
            }
            return $t;
        } catch (Throwable $e) {
            registra_errore('Briscola::testoTavolo', $e);
            throw $e;
        }
    }

    private static function testoPunti(array $st, int $mioSq): string
    {
        try {
            if ($st['finita']) {
                return $st['motivo'];
            }
            $v = $st['variante'];
            if ($v === 'al_meglio') {
                $s0 = $st['totali_smazzate'][0] ?? 0;
                $s1 = $st['totali_smazzate'][1] ?? 0;
                return "Al meglio delle 3 smazzate (smazzata {$st['smazzata']}). Smazzate vinte: " . self::nomiSquadra($st, 0) . " $s0, " . self::nomiSquadra($st, 1) . " $s1. Punti mano: " . self::puntiSquadra($st, $mioSq) . '.';
            }
            if ($v === 'marianna') {
                $t0 = $st['totali_punti'][0] ?? 0;
                $t1 = $st['totali_punti'][1] ?? 0;
                return "Marianna fino a {$st['obiettivo_marianna']} punti (mano {$st['smazzata']}). Totali: " . self::nomiSquadra($st, 0) . " $t0, " . self::nomiSquadra($st, 1) . " $t1. Punti in questa mano: " . self::puntiSquadra($st, $mioSq) . '.';
            }
            if ($v === 'chiamata') {
                $chiamante = $st['chiamante'] ?? 0;
                return 'Briscola a chiamata: ' . $st['nomi'][$chiamante] . ' e compagno contro i difensori. Punti squadra: ' . self::puntiSquadra($st, $mioSq) . '.';
            }
            if ($mioSq < 0) {
                return 'I punti si contano alla fine.';
            }
            return 'Hai preso ' . count($st['prese'][$mioSq]) . ' carte per ' . self::puntiSquadra($st, $mioSq) . ' punti. Gli altri punti si vedono alla fine.';
        } catch (Throwable $e) {
            registra_errore('Briscola::testoPunti', $e);
            throw $e;
        }
    }

    public static function situazione(array $st, int $posto): string
    {
        try {
            $t = self::testoTavolo($st) . ' ';
            $t .= 'Hai in mano ' . count($st['mani'][$posto]) . ' carte: ' . MazzoItaliano::elenca($st['mani'][$posto]) . '. ';
            $t .= self::testoPunti($st, self::squadraDi($st, $posto));
            return trim($t);
        } catch (Throwable $e) {
            registra_errore('Briscola::situazione', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ computer
    /**
     * Quanti punti in più (o in meno) fa chi gioca (indice $io) da qui alla fine, se entrambi giocano al meglio: a mazzo finito le carte
     * dell'avversario si ricavano da quelle già viste, quindi si può provare ogni sequenza di mosse. Solo per due giocatori.
     * $mani: le mani dei due posti; $tavolo: le carte già giocate nella presa [[posto, carta], ...]; $turno: chi gioca adesso.
     */
    private static function esitoFinale(array $mani, array $tavolo, int $turno, int $io, int $semeBriscola): int
    {
        try {
            if (count($tavolo) === 2) {
                $semeUscita = MazzoItaliano::seme($tavolo[0][1]);
                $v = self::batte($tavolo[1][1], $tavolo[0][1], $semeBriscola, $semeUscita) ? 1 : 0;
                $vincitore = $tavolo[$v][0];
                $punti = self::punti($tavolo[0][1]) + self::punti($tavolo[1][1]);
                $valore = $vincitore === $io ? $punti : -$punti;
                if (!$mani[0] && !$mani[1]) {
                    return $valore;
                }
                return $valore + self::esitoFinale($mani, [], $vincitore, $io, $semeBriscola);
            }
            $migliore = $turno === $io ? -1000 : 1000;
            foreach ($mani[$turno] as $k => $c) {
                $dopo = $mani;
                unset($dopo[$turno][$k]);
                $dopo[$turno] = array_values($dopo[$turno]);
                $r = self::esitoFinale($dopo, array_merge($tavolo, [[$turno, $c]]), 1 - $turno, $io, $semeBriscola);
                $migliore = $turno === $io ? max($migliore, $r) : min($migliore, $r);
            }
            return $migliore;
        } catch (Throwable $e) {
            registra_errore('Briscola::esitoFinale', $e);
            throw $e;
        }
    }

    /** A mazzo finito, in due, sceglie la carta che dà più punti alla fine. Nothing (null) se le carte dell'avversario non si possono ricavare. */
    private static function botFinale(array $st, int $posto): ?string
    {
        try {
            $altro = 1 - $posto;
            $mia = $st['mani'][$posto];
            $viste = array_merge($mia, array_map(fn($t) => $t[1], $st['tavolo']), ...$st['prese']);
            $sua = array_values(array_diff(range(0, 39), $viste));
            if (count($sua) !== count($st['mani'][$altro]) || !$mia) {
                return null;
            }
            $semeBriscola = MazzoItaliano::seme($st['briscola']);
            $migliore = null;
            $valoreMigliore = -1000;
            foreach ($mia as $k => $c) {
                $mani = [$posto => array_values(array_diff($mia, [$c])), $altro => $sua];
                $v = self::esitoFinale($mani, array_merge($st['tavolo'], [[$posto, $c]]), $altro, $posto, $semeBriscola);
                // a parità di punti si tiene la carta più forte per dopo (si gioca la più debole)
                if ($v > $valoreMigliore || ($v === $valoreMigliore && self::forza($c) < self::forza((int) explode('|', (string) $migliore)[1]))) {
                    $valoreMigliore = $v;
                    $migliore = 'gioca|' . $c;
                }
            }
            return $migliore;
        } catch (Throwable $e) {
            registra_errore('Briscola::botFinale', $e);
            throw $e;
        }
    }

    /** La mossa semplice di chi gioca nelle simulazioni (le stesse idee del computer di sempre): una carta da $mano, con sul tavolo al massimo la carta dell'altro. */
    private static function mossaSemplice(array $mano, array $tavolo, int $semeBriscola): int
    {
        try {
            $scarto = fn(int $c) => [MazzoItaliano::seme($c) === $semeBriscola ? 1 : 0, self::punti($c), self::forza($c)];
            $piuBassa = function (array $carte) use ($scarto): int {
                usort($carte, fn($a, $b) => $scarto($a) <=> $scarto($b));
                return $carte[0];
            };
            if (!$tavolo) {
                return $piuBassa($mano);
            }
            $avversaria = $tavolo[0][1];
            $semeUscita = MazzoItaliano::seme($avversaria);
            $vincenti = array_values(array_filter($mano, fn($c) => self::batte($c, $avversaria, $semeBriscola, $semeUscita)));
            if ($vincenti) {
                usort($vincenti, fn($a, $b) => $scarto($a) <=> $scarto($b));
                $meglio = $vincenti[0];
                $conBriscola = MazzoItaliano::seme($meglio) === $semeBriscola && MazzoItaliano::seme($avversaria) !== $semeBriscola;
                $puntiTavolo = self::punti($avversaria);
                if (!$conBriscola || $puntiTavolo + self::punti($meglio) >= 10 || $puntiTavolo >= 4) {
                    return $meglio;
                }
            }
            return $piuBassa($mano);
        } catch (Throwable $e) {
            registra_errore('Briscola::mossaSemplice', $e);
            throw $e;
        }
    }

    /** Gioca fino alla fine con la mossa semplice e dice quanti punti in più ha fatto $io. Il mazzo si pesca come nel gioco (vince chi ha preso). */
    private static function giocaFinoAllaFine(array $mani, array $mazzo, array $tavolo, int $turno, int $io, int $semeBriscola): int
    {
        try {
            $valore = 0;
            while (true) {
                while (count($tavolo) < 2) {
                    $c = self::mossaSemplice($mani[$turno], $tavolo, $semeBriscola);
                    $k = array_search($c, $mani[$turno], true);
                    unset($mani[$turno][$k]);
                    $mani[$turno] = array_values($mani[$turno]);
                    $tavolo[] = [$turno, $c];
                    $turno = 1 - $turno;
                }
                $v = self::batte($tavolo[1][1], $tavolo[0][1], $semeBriscola, MazzoItaliano::seme($tavolo[0][1])) ? 1 : 0;
                $vincitore = $tavolo[$v][0];
                $punti = self::punti($tavolo[0][1]) + self::punti($tavolo[1][1]);
                $valore += $vincitore === $io ? $punti : -$punti;
                $tavolo = [];
                if ($mazzo) {
                    $mani[$vincitore][] = array_shift($mazzo);
                    if ($mazzo) {
                        $mani[1 - $vincitore][] = array_shift($mazzo);
                    }
                }
                $turno = $vincitore;
                if (!$mani[0] && !$mani[1]) {
                    return $valore;
                }
            }
        } catch (Throwable $e) {
            registra_errore('Briscola::giocaFinoAllaFine', $e);
            throw $e;
        }
    }

    /**
     * Due giocatori: del mazzo e dell'avversario si sa quello che è uscito (prese, tavolo, la propria mano, la briscola scoperta). Il resto si mescola
     * più volte a caso (ogni volta un'ipotesi su cosa ha in mano l'avversario e in che ordine escono le carte), si gioca ogni mossa fino alla fine
     * con la mossa semplice, e si sceglie la carta che in media fa più punti. A mazzo finito l'avversario non ha segreti e si prova ogni sequenza.
     */
    private static function botSimulato(array $st, int $posto): ?string
    {
        try {
            $altro = 1 - $posto;
            $mia = $st['mani'][$posto];
            if (!$mia) {
                return null;
            }
            $semeBriscola = MazzoItaliano::seme($st['briscola']);
            $nelMazzo = count($st['mazzo']);
            $viste = array_merge($mia, array_map(fn($t) => $t[1], $st['tavolo']), ...$st['prese']);
            if ($nelMazzo > 0) {
                $viste[] = $st['briscola'];   // la briscola scoperta sta in fondo al mazzo
            }
            $ignote = array_values(array_diff(range(0, 39), array_unique($viste)));
            $h = count($st['mani'][$altro]);
            if (count($ignote) !== $h + max(0, $nelMazzo - 1)) {
                return null;
            }
            if ($nelMazzo === 0) {
                return self::botFinale($st, $posto);
            }
            $mondi = [];
            for ($r = 0; $r < 80; $r++) {
                $pool = $ignote;
                shuffle($pool);
                $mano = array_slice($pool, 0, $h);
                $mazzo = array_slice($pool, $h);
                $mazzo[] = $st['briscola'];
                $mondi[] = [$mano, $mazzo];
            }
            $migliore = null;
            $mediaMigliore = -INF;
            foreach (array_unique($mia) as $c) {
                $somma = 0;
                foreach ($mondi as [$manoAvv, $mazzo]) {
                    $mani = [$posto => array_values(array_diff($mia, [$c])), $altro => $manoAvv];
                    $tavolo = array_merge($st['tavolo'], [[$posto, $c]]);
                    $somma += self::giocaFinoAllaFine($mani, $mazzo, $tavolo, $altro, $posto, $semeBriscola);
                }
                $media = $somma / count($mondi);
                // a parità conviene giocare la carta meno preziosa
                if ($media > $mediaMigliore + 1e-9 || (abs($media - $mediaMigliore) <= 1e-9 && $migliore !== null && [MazzoItaliano::seme($c) === $semeBriscola ? 1 : 0, self::punti($c), self::forza($c)] < [MazzoItaliano::seme($migliore) === $semeBriscola ? 1 : 0, self::punti($migliore), self::forza($migliore)])) {
                    $mediaMigliore = $media;
                    $migliore = $c;
                }
            }
            return $migliore === null ? null : 'gioca|' . $migliore;
        } catch (Throwable $e) {
            registra_errore('Briscola::botSimulato', $e);
            throw $e;
        }
    }

    public static function bot(array $st, int $posto): string
    {
        try {
            // Chiamata
            if (!empty($st['fase_chiamata']) && $posto === $st['turno']) {
                $mosse = self::mosse($st, $posto);
                // Sceglie l'Asso o la carta più alta disponibile
                return $mosse[0]['id'];
            }

            // Accuso per Marianna
            if ($st['variante'] === 'marianna') {
                $mosse = self::mosse($st, $posto);
                foreach ($mosse as $m) {
                    if (str_starts_with($m['id'], 'accuso|')) {
                        return $m['id'];
                    }
                }
            }

            // in due: si prova a indovinare le carte dell'avversario (a mazzo finito si ricavano con certezza) e si sceglie la carta che in media rende di più
            if ($st['n'] === 2 && ($st['variante'] ?? 'classica') !== 'chiamata' && $st['briscola'] !== null) {
                $scelta = self::botSimulato($st, $posto);
                if ($scelta !== null) {
                    return $scelta;
                }
            }

            $mano = $st['mani'][$posto];
            $semeBriscola = $st['briscola'] !== null ? MazzoItaliano::seme($st['briscola']) : 0;
            $scarto = fn(int $c) => [MazzoItaliano::seme($c) === $semeBriscola ? 1 : 0, self::punti($c), self::forza($c)];
            $piuBassa = function (array $carte) use ($scarto): int {
                usort($carte, fn($a, $b) => $scarto($a) <=> $scarto($b));
                return $carte[0];
            };

            if (!$st['tavolo']) {
                return 'gioca|' . $piuBassa($mano);
            }

            $semeUscita = MazzoItaliano::seme($st['tavolo'][0][1]);
            $v = self::vincenteTavolo($st);
            $cartaVincente = $st['tavolo'][$v][1];
            $postoVincente = $st['tavolo'][$v][0];
            $puntiTavolo = 0;
            foreach ($st['tavolo'] as [$p, $c]) {
                $puntiTavolo += self::punti($c);
            }
            $ultimo = count($st['tavolo']) === $st['n'] - 1;
            $compagnoVince = ($st['n'] === 4 || $st['variante'] === 'chiamata') && self::squadraDi($st, $postoVincente) === self::squadraDi($st, $posto);

            // il compagno sta già prendendo: se sono l'ultimo carico, altrimenti gioco basso
            if ($compagnoVince) {
                if ($ultimo) {
                    $carichi = array_values(array_filter($mano, fn($c) => MazzoItaliano::seme($c) !== $semeBriscola));
                    if ($carichi) {
                        usort($carichi, fn($a, $b) => self::punti($b) <=> self::punti($a));
                        return 'gioca|' . $carichi[0];
                    }
                }
                return 'gioca|' . $piuBassa($mano);
            }

            $vincenti = array_values(array_filter($mano, fn($c) => self::batte($c, $cartaVincente, $semeBriscola, $semeUscita)));
            if ($vincenti) {
                usort($vincenti, fn($a, $b) => $scarto($a) <=> $scarto($b));
                $meglio = $vincenti[0];
                $conBriscola = MazzoItaliano::seme($meglio) === $semeBriscola && MazzoItaliano::seme($cartaVincente) !== $semeBriscola;
                // la briscola per prendere si spende solo se ne vale la pena
                if (!$conBriscola || $puntiTavolo + self::punti($meglio) >= 10 || ($puntiTavolo >= 4 && $ultimo)) {
                    return 'gioca|' . $meglio;
                }
            }
            return 'gioca|' . $piuBassa($mano);
        } catch (Throwable $e) {
            registra_errore('Briscola::bot', $e);
            throw $e;
        }
    }
}
