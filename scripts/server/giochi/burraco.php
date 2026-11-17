<?php
// Burraco italiano per 2 giocatori (uno contro uno) o 4 (a coppie: 0 e 2 contro 1 e 3), giocato a punti.
//
// Regole usate:
//  - 108 carte: due mazzi da 52 e quattro jolly. Jolly e 2 sono jolly (la "pinella"): ogni combinazione ne può avere al massimo uno.
//    Un 2 al suo posto naturale in una scala (A-2-3) conta come carta normale.
//  - Ognuno ha 11 carte. Due pozzetti da 11 carte. Una carta scoperta negli scarti.
//  - Si pesca dal mazzo oppure si prendono tutti gli scarti, ma solo se la carta in cima si cala subito (in una combinazione nuova
//    o attaccandola a una della propria squadra). Poi si calano combinazioni e si scarta una carta.
//  - Pozzo congelato: se negli scarti c'è un jolly o un 2 si possono prendere solo con una combinazione nuova fatta con la carta in cima
//    e almeno due carte naturali della propria mano (niente jolly, niente attaccare). Se la carta in cima è un jolly o un 2 non si prendono.
//  - Combinazioni: tris (stesso valore, almeno 3 carte) o scala (stesso seme in sequenza, almeno 3 carte, l'asso vale 1 o 14).
//    Con 7 o più carte è un burraco: pulito (senza jolly) 200 punti, sporco 100 punti.
//  - Prima calata (apertura): la squadra deve calare almeno 15 punti se ha un totale negativo, 50 punti fino a 1495, 90 punti da 1500
//    (contano solo le carte, non i burraco). Le combinazioni sono provvisorie finché non si raggiunge il minimo e si possono annullare;
//    finché la squadra non ha aperto non si può scartare dopo aver calato. Prendendo gli scarti il minimo va raggiunto subito
//    con la combinazione che usa la carta in cima.
//  - Chi resta senza carte prende il pozzetto della propria squadra (se non l'ha già preso) e continua.
//  - Per chiudere la mano (restare senza carte) la squadra deve aver già preso il pozzetto e avere almeno un burraco;
//    l'ultima carta scartata per chiudere non può essere un jolly o un 2.
//  - Punti di una mano: carte in tavolo (asso 15, re-donna-fante-10-9-8 valgono 10, 7-3 valgono 5, 2 vale 20, jolly 30), burraco,
//    chiusura +100, pozzetto non preso -100, meno le carte rimaste in mano.
//  - Si gioca a mani successive fino a un obiettivo (di solito 2005 punti): vince la squadra che lo raggiunge per prima, se è davanti.

declare(strict_types=1);

final class Burraco
{
    public const SEMI = ['cuori', 'quadri', 'fiori', 'picche'];
    public const SIMBOLI = ['♥', '♦', '♣', '♠'];

    // ------------------------------------------------------------------ carte
    public static function seme(int $id): int
    {
        try {
            $k = $id % 54;
            return $k >= 52 ? -1 : intdiv($k, 13);
        } catch (Throwable $e) {
            registra_errore('Burraco::seme', $e);
            throw $e;
        }
    }

    public static function rango(int $id): int
    {
        try {
            $k = $id % 54;
            return $k >= 52 ? 0 : ($k % 13) + 1;
        } catch (Throwable $e) {
            registra_errore('Burraco::rango', $e);
            throw $e;
        }
    }

    public static function eJolly(int $id): bool
    {
        try {
            return $id % 54 >= 52;
        } catch (Throwable $e) {
            registra_errore('Burraco::eJolly', $e);
            throw $e;
        }
    }

    /** Il jolly o il 2: carte che possono fare da jolly. */
    public static function puoEssereJolly(int $id): bool
    {
        try {
            return self::eJolly($id) || self::rango($id) === 2;
        } catch (Throwable $e) {
            registra_errore('Burraco::puoEssereJolly', $e);
            throw $e;
        }
    }

    public static function nomeRango(int $r): string
    {
        try {
            return [1 => 'Asso', 2 => '2', 3 => '3', 4 => '4', 5 => '5', 6 => '6', 7 => '7', 8 => '8', 9 => '9', 10 => '10', 11 => 'Fante', 12 => 'Donna', 13 => 'Re', 14 => 'Asso'][$r] ?? '?';
        } catch (Throwable $e) {
            registra_errore('Burraco::nomeRango', $e);
            throw $e;
        }
    }

    public static function nome(int $id): string
    {
        try {
            if (self::eJolly($id)) {
                return 'Jolly';
            }
            return self::nomeRango(self::rango($id)) . ' di ' . self::SEMI[self::seme($id)];
        } catch (Throwable $e) {
            registra_errore('Burraco::nome', $e);
            throw $e;
        }
    }

    public static function valore(int $id): int
    {
        try {
            if (self::eJolly($id)) {
                return 30;
            }
            $r = self::rango($id);
            if ($r === 1) {
                return 15;
            }
            if ($r === 2) {
                return 20;
            }
            return $r >= 8 ? 10 : 5;
        } catch (Throwable $e) {
            registra_errore('Burraco::valore', $e);
            throw $e;
        }
    }

    public static function carta(int $id): array
    {
        try {
            return ['id' => $id, 'nome' => self::nome($id), 'seme' => self::seme($id), 'rango' => self::rango($id), 'jolly' => self::eJolly($id)];
        } catch (Throwable $e) {
            registra_errore('Burraco::carta', $e);
            throw $e;
        }
    }

    /** Ordine per mostrare la mano: per seme e valore, i jolly in fondo. */
    private static function chiaveOrdine(int $id): int
    {
        try {
            if (self::eJolly($id)) {
                return 1000 + $id;
            }
            return self::seme($id) * 100 + self::rango($id) * 2 + intdiv($id, 54);
        } catch (Throwable $e) {
            registra_errore('Burraco::chiaveOrdine', $e);
            throw $e;
        }
    }

    public static function ordina(array $carte): array
    {
        try {
            usort($carte, fn($a, $b) => self::chiaveOrdine($a) <=> self::chiaveOrdine($b));
            return array_values($carte);
        } catch (Throwable $e) {
            registra_errore('Burraco::ordina', $e);
            throw $e;
        }
    }

    public static function elencaCarte(array $ids): string
    {
        try {
            return implode(', ', array_map(fn($id) => self::nome($id), self::ordina($ids)));
        } catch (Throwable $e) {
            registra_errore('Burraco::elencaCarte', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ combinazioni
    /** Se le carte formano una combinazione valida la restituisce, altrimenti null. */
    public static function combinazione(array $carte): ?array
    {
        try {
            $n = count($carte);
            if ($n < 3 || $n > 14) {
                return null;
            }
            return self::comeTris($carte) ?? self::comeScala($carte);
        } catch (Throwable $e) {
            registra_errore('Burraco::combinazione', $e);
            throw $e;
        }
    }

    private static function comeTris(array $carte): ?array
    {
        try {
            $n = count($carte);
            for ($r = 1; $r <= 13; $r++) {
                $nat = [];
                $jol = [];
                $ok = true;
                foreach ($carte as $id) {
                    if (!self::eJolly($id) && self::rango($id) === $r) {
                        $nat[] = $id;
                    } elseif (self::puoEssereJolly($id)) {
                        $jol[] = $id;
                    } else {
                        $ok = false;
                        break;
                    }
                }
                if ($ok && count($nat) >= 2 && count($jol) <= 1 && count($nat) + count($jol) === $n) {
                    return ['tipo' => 'tris', 'rango' => $r, 'carte' => self::ordina($carte), 'jolly' => $jol[0] ?? null];
                }
            }
            return null;
        } catch (Throwable $e) {
            registra_errore('Burraco::comeTris', $e);
            throw $e;
        }
    }

    private static function comeScala(array $carte): ?array
    {
        try {
            $n = count($carte);
            $carte = array_values($carte);
            for ($s = 0; $s < 4; $s++) {
                for ($inizio = 1; $inizio + $n - 1 <= 14; $inizio++) {
                    $usate = [];
                    $slot = [];
                    $mancanti = [];
                    for ($p = $inizio; $p < $inizio + $n; $p++) {
                        $rk = $p > 13 ? 1 : $p;
                        $trovata = null;
                        foreach ($carte as $i => $id) {
                            if (!isset($usate[$i]) && !self::eJolly($id) && self::seme($id) === $s && self::rango($id) === $rk) {
                                $trovata = $i;
                                break;
                            }
                        }
                        if ($trovata !== null) {
                            $usate[$trovata] = true;
                            $slot[] = ['id' => $carte[$trovata], 'rango' => $p, 'jolly' => false];
                        } else {
                            $slot[] = null;
                            $mancanti[] = count($slot) - 1;
                        }
                    }
                    $avanzi = [];
                    foreach ($carte as $i => $id) {
                        if (!isset($usate[$i])) {
                            $avanzi[] = $id;
                        }
                    }
                    if (count($mancanti) > 1 || count($avanzi) !== count($mancanti)) {
                        continue;
                    }
                    if (count($mancanti) === 1) {
                        if (!self::puoEssereJolly($avanzi[0])) {
                            continue;
                        }
                        $slot[$mancanti[0]] = ['id' => $avanzi[0], 'rango' => $inizio + $mancanti[0], 'jolly' => true];
                    }
                    return ['tipo' => 'scala', 'seme' => $s, 'slot' => $slot, 'carte' => array_map(fn($x) => $x['id'], $slot)];
                }
            }
            return null;
        } catch (Throwable $e) {
            registra_errore('Burraco::comeScala', $e);
            throw $e;
        }
    }

    public static function eBurraco(array $comb): bool
    {
        try {
            return count($comb['carte']) >= 7;
        } catch (Throwable $e) {
            registra_errore('Burraco::eBurraco', $e);
            throw $e;
        }
    }

    public static function eBurracoPulito(array $comb): bool
    {
        try {
            if (!self::eBurraco($comb)) {
                return false;
            }
            if ($comb['tipo'] === 'tris') {
                return $comb['jolly'] === null;
            }
            foreach ($comb['slot'] as $x) {
                if ($x['jolly']) {
                    return false;
                }
            }
            return true;
        } catch (Throwable $e) {
            registra_errore('Burraco::eBurracoPulito', $e);
            throw $e;
        }
    }

    public static function descrivi(array $comb): string
    {
        try {
            $n = count($comb['carte']);
            $extra = '';
            if (self::eBurraco($comb)) {
                $extra = self::eBurracoPulito($comb) ? ', burraco pulito' : ', burraco sporco';
            }
            if ($comb['tipo'] === 'tris') {
                $t = 'tris di ' . ($comb['rango'] === 1 ? 'assi' : ($comb['rango'] === 11 ? 'fanti' : ($comb['rango'] === 12 ? 'donne' : ($comb['rango'] === 13 ? 're' : (string) $comb['rango']))));
                return $t . ' (' . $n . ' carte' . $extra . ')';
            }
            $primo = $comb['slot'][0]['rango'];
            $ultimo = $comb['slot'][$n - 1]['rango'];
            $t = 'scala di ' . self::SEMI[$comb['seme']] . ' dal ' . self::nomeRango($primo) . ' al ' . self::nomeRango($ultimo);
            foreach ($comb['slot'] as $x) {
                if ($x['jolly']) {
                    $t .= ', con ' . self::nome($x['id']) . ' al posto del ' . self::nomeRango($x['rango']);
                    break;
                }
            }
            return $t . ' (' . $n . ' carte' . $extra . ')';
        } catch (Throwable $e) {
            registra_errore('Burraco::descrivi', $e);
            throw $e;
        }
    }

    public static function puntiCombinazione(array $comb): int
    {
        try {
            $p = 0;
            foreach ($comb['carte'] as $id) {
                $p += self::valore($id);
            }
            if (self::eBurraco($comb)) {
                $p += self::eBurracoPulito($comb) ? 200 : 100;
            }
            return $p;
        } catch (Throwable $e) {
            registra_errore('Burraco::puntiCombinazione', $e);
            throw $e;
        }
    }

    /**
     * Le combinazioni nuove che si possono calare con queste carte (una carta per volta, nessun riuso):
     * tris e scale, tenendo solo quelle che non sono contenute in un'altra dello stesso tipo.
     * Restituisce elenchi di id.
     */
    public static function nuoveCombinazioni(array $mano): array
    {
        try {
            $risultato = [];
            $mano = array_values($mano);
            $jollyInMano = array_values(array_filter($mano, fn($id) => self::puoEssereJolly($id)));

            // tris
            for ($r = 1; $r <= 13; $r++) {
                $nat = array_values(array_filter($mano, fn($id) => !self::eJolly($id) && self::rango($id) === $r));
                $nat = self::ordina($nat);
                if (count($nat) >= 3) {
                    $risultato[] = $nat;
                } elseif (count($nat) === 2) {
                    // con un jolly (un 2 che non sia dello stesso valore, oppure un jolly vero)
                    foreach ($jollyInMano as $j) {
                        if (self::rango($j) === $r) {
                            continue;
                        }
                        $risultato[] = array_merge($nat, [$j]);
                        break;
                    }
                }
            }

            // scale: per ogni seme le carte vere che si hanno, poi si provano tutte le finestre di rango con al massimo un buco da riempire con un jolly
            $vere = [];
            foreach ($mano as $id) {
                if (!self::eJolly($id)) {
                    $vere[self::seme($id)][self::rango($id)][] = $id;
                }
            }
            foreach ($vere as $s => $perRango) {
                if (count($perRango) < 2) {
                    continue;
                }
                for ($n = 14; $n >= 3; $n--) {
                    for ($inizio = 1; $inizio + $n - 1 <= 14; $inizio++) {
                        $carte = [];
                        $uso = [];
                        $mancanti = 0;
                        for ($p = $inizio; $p < $inizio + $n; $p++) {
                            $rk = $p > 13 ? 1 : $p;
                            $k = $uso[$rk] ?? 0;
                            if (isset($perRango[$rk][$k])) {
                                $carte[] = $perRango[$rk][$k];
                                $uso[$rk] = $k + 1;
                            } else {
                                $mancanti++;
                                if ($mancanti > 1) {
                                    break;
                                }
                            }
                        }
                        if ($mancanti > 1 || count($carte) < 2) {
                            continue;
                        }
                        if ($mancanti === 1) {
                            $jolly = null;
                            foreach ($jollyInMano as $j) {
                                if (!in_array($j, $carte, true)) {
                                    $jolly = $j;
                                    break;
                                }
                            }
                            if ($jolly === null) {
                                continue;
                            }
                            $carte[] = $jolly;
                        }
                        $risultato[] = self::ordina($carte);
                    }
                }
            }

            // niente doppioni e niente combinazioni contenute in altre
            $unici = [];
            foreach ($risultato as $ids) {
                $chiave = implode(',', $ids);
                $unici[$chiave] = $ids;
            }
            $elenco = array_values($unici);
            $tenute = [];
            foreach ($elenco as $i => $a) {
                $contenuta = false;
                foreach ($elenco as $j => $b) {
                    if ($i !== $j && count($b) > count($a) && count(array_diff($a, $b)) === 0) {
                        $contenuta = true;
                        break;
                    }
                }
                if (!$contenuta) {
                    $tenute[] = $a;
                }
            }
            return $tenute;
        } catch (Throwable $e) {
            registra_errore('Burraco::nuoveCombinazioni', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ partita
    public static function squadraDi(array $st, int $posto): int
    {
        try {
            return $st['n'] === 4 ? $posto % 2 : $posto;
        } catch (Throwable $e) {
            registra_errore('Burraco::squadraDi', $e);
            throw $e;
        }
    }

    public static function nuova(array $nomi, int $obiettivo = 2005): array
    {
        try {
            $n = count($nomi);
            if ($n !== 2 && $n !== 4) {
                throw new InvalidArgumentException('Il burraco si gioca in 2 o in 4.');
            }
            $obiettivo = max(500, min(5005, $obiettivo));
            $st = [
                'tipo' => 'burraco',
                'n' => $n,
                'nomi' => array_values($nomi),
                'partita' => ['obiettivo' => $obiettivo, 'totali' => [0, 0], 'mano' => 1, 'storico' => []],
                'finita' => false,
                'vincitori' => [],
                'punteggi' => null,
                'motivo' => '',
            ];
            self::daiCarte($st, 0);
            return $st;
        } catch (Throwable $e) {
            registra_errore('Burraco::nuova', $e);
            throw $e;
        }
    }

    /** Mescola e distribuisce una mano nuova; comincia $primo. */
    private static function daiCarte(array &$st, int $primo): void
    {
        try {
            $n = $st['n'];
            $mazzo = range(0, 107);
            shuffle($mazzo);
            $mani = [];
            for ($i = 0; $i < $n; $i++) {
                $mani[] = self::ordina(array_splice($mazzo, 0, 11));
            }
            $st['mani'] = $mani;
            $st['pozzetti'] = [array_splice($mazzo, 0, 11), array_splice($mazzo, 0, 11)];
            $st['scarti'] = array_splice($mazzo, 0, 1);
            $st['mazzo'] = $mazzo;
            $st['squadre'] = [
                ['combinazioni' => [], 'pozzetto' => false, 'aperta' => false],
                ['combinazioni' => [], 'pozzetto' => false, 'aperta' => false],
            ];
            $st['turno'] = $primo;
            $st['fase'] = 'pesca';
            $st['chiusa_da'] = null;
            $st['provv'] = null;
            $st['rinuncia'] = false;
        } catch (Throwable $e) {
            registra_errore('Burraco::daiCarte', $e);
            throw $e;
        }
    }

    public static function inizio(array $st): array
    {
        try {
            return [['per' => null, 'testo' => 'Si gioca a burraco fino a ' . $st['partita']['obiettivo'] . ' punti. Ognuno ha 11 carte. Negli scarti c\'è ' . self::nome($st['scarti'][0]) . '. Inizia ' . $st['nomi'][$st['turno']] . '.']];
        } catch (Throwable $e) {
            registra_errore('Burraco::inizio', $e);
            throw $e;
        }
    }

    /** Punti da calare per aprire: dipendono dal totale della squadra. */
    public static function minimoApertura(array $st, int $sq): int
    {
        try {
            $t = $st['partita']['totali'][$sq];
            return $t < 0 ? 15 : ($t < 1500 ? 50 : 90);
        } catch (Throwable $e) {
            registra_errore('Burraco::minimoApertura', $e);
            throw $e;
        }
    }

    /** Valore delle carte calate (senza i premi dei burraco). */
    private static function puntiCarte(array $combinazioni): int
    {
        try {
            $p = 0;
            foreach ($combinazioni as $c) {
                foreach ($c['carte'] as $id) {
                    $p += self::valore($id);
                }
            }
            return $p;
        } catch (Throwable $e) {
            registra_errore('Burraco::puntiCarte', $e);
            throw $e;
        }
    }

    private static function eNaturale(array $comb): bool
    {
        try {
            if ($comb['tipo'] === 'tris') {
                return $comb['jolly'] === null;
            }
            foreach ($comb['slot'] as $x) {
                if ($x['jolly']) {
                    return false;
                }
            }
            return true;
        } catch (Throwable $e) {
            registra_errore('Burraco::eNaturale', $e);
            throw $e;
        }
    }

    /** Il pozzo è congelato se contiene un jolly o un 2. */
    public static function congelato(array $st): bool
    {
        try {
            foreach ($st['scarti'] as $id) {
                if (self::puoEssereJolly($id)) {
                    return true;
                }
            }
            return false;
        } catch (Throwable $e) {
            registra_errore('Burraco::congelato', $e);
            throw $e;
        }
    }

    private static function haBurraco(array $st, int $squadra): bool
    {
        try {
            foreach ($st['squadre'][$squadra]['combinazioni'] as $c) {
                if (self::eBurraco($c)) {
                    return true;
                }
            }
            return false;
        } catch (Throwable $e) {
            registra_errore('Burraco::haBurraco', $e);
            throw $e;
        }
    }

    /** Dopo una mossa che lascia $rimaste carte in mano: lo stato è accettabile? (si deve poter ancora scartare) */
    private static function statoAccettabile(array $st, int $posto, int $rimaste, array $squadraDopo): bool
    {
        try {
            $sq = self::squadraDi($st, $posto);
            $apertaDopo = $st['squadre'][$sq]['aperta'] || self::puntiCarte($squadraDopo['combinazioni']) >= self::minimoApertura($st, $sq);
            if (!$apertaDopo) {
                return $rimaste >= 1; // calate provvisorie: serve almeno una carta, e si può sempre annullare
            }
            if ($rimaste >= 2) {
                return true;
            }
            $pozzettoPreso = $st['squadre'][$sq]['pozzetto'];
            $haBurraco = false;
            foreach ($squadraDopo['combinazioni'] as $c) {
                if (self::eBurraco($c)) {
                    $haBurraco = true;
                    break;
                }
            }
            return !$pozzettoPreso || $haBurraco; // con una o zero carte: si potrà scartare per chiudere o per prendere il pozzetto
        } catch (Throwable $e) {
            registra_errore('Burraco::statoAccettabile', $e);
            throw $e;
        }
    }

    private static function distinte(array $mano): array
    {
        try {
            $viste = [];
            $r = [];
            foreach (self::ordina($mano) as $id) {
                $chiave = self::eJolly($id) ? 'J' : (self::seme($id) . '-' . self::rango($id));
                if (!isset($viste[$chiave])) {
                    $viste[$chiave] = true;
                    $r[] = $id;
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Burraco::distinte', $e);
            throw $e;
        }
    }

    /** Le mosse possibili per il giocatore di turno: elenco di ['id'=>..., 'testo'=>...]. */
    public static function mosse(array $st, int $posto): array
    {
        try {
            if ($st['finita'] || $posto !== $st['turno']) {
                return [];
            }
            $sq = self::squadraDi($st, $posto);
            $mano = $st['mani'][$posto];
            $mosse = [];
            $squadraStato = $st['squadre'][$sq];
            $aperta = $squadraStato['aperta'];
            $minimo = self::minimoApertura($st, $sq);

            if ($st['fase'] === 'pesca') {
                if (count($st['mazzo']) > 0) {
                    $mosse[] = ['id' => 'pesca', 'testo' => 'Pesca dal mazzo (' . count($st['mazzo']) . ' carte rimaste)'];
                }
                if (count($st['scarti']) > 0) {
                    $cima = $st['scarti'][count($st['scarti']) - 1];
                    $n = count($st['scarti']);
                    if (!self::puoEssereJolly($cima)) { // con un jolly o un 2 in cima gli scarti non si prendono
                        $congelato = self::congelato($st);
                        if (!$congelato && $aperta) {
                            foreach ($squadraStato['combinazioni'] as $i => $c) {
                                $nuova = self::combinazione(array_merge($c['carte'], [$cima]));
                                if ($nuova !== null) {
                                    $dopo = $squadraStato;
                                    $dopo['combinazioni'][$i] = $nuova;
                                    if (self::statoAccettabile($st, $posto, count($mano) + $n - 1, $dopo)) {
                                        $mosse[] = ['id' => 'scarti|att|' . $i, 'testo' => 'Prendi gli scarti (' . $n . ' carte) e attacca ' . self::nome($cima) . ' alla ' . self::descrivi($c)];
                                    }
                                }
                            }
                        }
                        $pool = array_merge($mano, [$cima]);
                        if ($congelato) {
                            $pool = array_values(array_filter($pool, fn($id) => !self::puoEssereJolly($id)));
                        }
                        foreach (self::nuoveCombinazioni($pool) as $ids) {
                            if (!in_array($cima, $ids, true)) {
                                continue;
                            }
                            $comb = self::combinazione($ids);
                            if ($comb === null || ($congelato && !self::eNaturale($comb))) {
                                continue;
                            }
                            if (!$aperta && self::puntiCarte([$comb]) < $minimo) {
                                continue; // prendendo gli scarti il minimo per aprire va raggiunto subito
                            }
                            $dopo = $squadraStato;
                            $dopo['combinazioni'][] = $comb;
                            if (self::statoAccettabile($st, $posto, count($mano) + $n - count($ids), $dopo)) {
                                $mosse[] = ['id' => 'scarti|cala|' . implode(',', $ids), 'testo' => 'Prendi gli scarti (' . $n . ' carte) e cala ' . self::descrivi($comb) . ': ' . self::elencaCarte($ids)];
                            }
                        }
                    }
                }
                return $mosse;
            }

            // fase di gioco: calare, attaccare, scartare
            $provvisoria = !$aperta && count($squadraStato['combinazioni']) > 0;
            foreach (self::nuoveCombinazioni($mano) as $ids) {
                $dopo = $squadraStato;
                $dopo['combinazioni'][] = self::combinazione($ids);
                if (self::statoAccettabile($st, $posto, count($mano) - count($ids), $dopo)) {
                    $mosse[] = ['id' => 'cala|' . implode(',', $ids), 'testo' => 'Cala ' . self::descrivi(self::combinazione($ids)) . ': ' . self::elencaCarte($ids)];
                }
            }
            $attacchi = [];
            foreach (self::distinte($mano) as $id) {
                foreach ($squadraStato['combinazioni'] as $i => $c) {
                    $nuova = self::combinazione(array_merge($c['carte'], [$id]));
                    if ($nuova !== null) {
                        $dopo = $squadraStato;
                        $dopo['combinazioni'][$i] = $nuova;
                        if (self::statoAccettabile($st, $posto, count($mano) - 1, $dopo)) {
                            $mosse[] = ['id' => 'att|' . $id . '|' . $i, 'testo' => 'Attacca ' . self::nome($id) . ', ' . self::valore($id) . ' punti, alla ' . self::descrivi($c)];
                            $attacchi[$id] = true;
                        }
                    }
                }
            }
            if ($provvisoria) {
                // finché non si è raggiunto il minimo non si scarta: o si cala altro o si annulla
                $mosse[] = ['id' => 'annulla', 'testo' => 'Annulla le combinazioni provvisorie (' . self::puntiCarte($squadraStato['combinazioni']) . ' punti su ' . $minimo . ') e riprendi le carte'];
                return $mosse;
            }
            foreach (self::distinte($mano) as $id) {
                if (count($mano) === 1 && $squadraStato['pozzetto']) {
                    if (!self::haBurraco($st, $sq)) {
                        continue; // non si può chiudere senza burraco
                    }
                    if (self::puoEssereJolly($id) && !isset($attacchi[$id])) {
                        // l'ultima carta per chiudere non può essere un jolly: se non si può attaccare si scarta lo stesso, per non bloccare la partita
                    }
                    if (self::puoEssereJolly($id) && isset($attacchi[$id])) {
                        continue;
                    }
                }
                $mosse[] = ['id' => 'scarta|' . $id, 'testo' => 'Scarta ' . self::nome($id) . ', ' . self::valore($id) . ' punti'];
            }
            return $mosse;
        } catch (Throwable $e) {
            registra_errore('Burraco::mosse', $e);
            throw $e;
        }
    }

    private static function togli(array &$mano, array $ids): void
    {
        try {
            foreach ($ids as $id) {
                $i = array_search($id, $mano, true);
                if ($i === false) {
                    throw new InvalidArgumentException('Carta non in mano.');
                }
                array_splice($mano, $i, 1);
            }
            $mano = array_values($mano);
        } catch (Throwable $e) {
            registra_errore('Burraco::togli', $e);
            throw $e;
        }
    }

    private static function prendiPozzetto(array &$st, int $posto, array &$eventi): void
    {
        try {
            $sq = self::squadraDi($st, $posto);
            $nome = $st['nomi'][$posto];
            $carte = array_shift($st['pozzetti']);
            $st['squadre'][$sq]['pozzetto'] = true;
            $st['mani'][$posto] = self::ordina(array_merge($st['mani'][$posto], $carte));
            $eventi[] = ['per' => null, 'testo' => $nome . ' prende il pozzetto.'];
            $eventi[] = ['per' => $posto, 'testo' => 'Nel pozzetto hai trovato: ' . self::elencaCarte($carte) . '.'];
        } catch (Throwable $e) {
            registra_errore('Burraco::prendiPozzetto', $e);
            throw $e;
        }
    }

    /** Se le calate provvisorie raggiungono il minimo la squadra ha aperto. */
    private static function controllaApertura(array &$st, int $posto, array &$eventi): void
    {
        try {
            $sq = self::squadraDi($st, $posto);
            if ($st['squadre'][$sq]['aperta']) {
                return;
            }
            $punti = self::puntiCarte($st['squadre'][$sq]['combinazioni']);
            if ($punti >= self::minimoApertura($st, $sq)) {
                $st['squadre'][$sq]['aperta'] = true;
                $st['provv'] = null;
                $eventi[] = ['per' => null, 'testo' => $st['nomi'][$posto] . ' apre con ' . $punti . ' punti.'];
            }
        } catch (Throwable $e) {
            registra_errore('Burraco::controllaApertura', $e);
            throw $e;
        }
    }

    /** Prima di una calata di chi non ha ancora aperto: si ricorda com'era, per poter annullare. */
    private static function ricordaProvvisorio(array &$st, int $posto): void
    {
        try {
            $sq = self::squadraDi($st, $posto);
            if (!$st['squadre'][$sq]['aperta'] && $st['provv'] === null) {
                $st['provv'] = ['posto' => $posto, 'mano' => $st['mani'][$posto]];
            }
        } catch (Throwable $e) {
            registra_errore('Burraco::ricordaProvvisorio', $e);
            throw $e;
        }
    }

    /** Applica una mossa (già verificata contro mosse()). Restituisce gli eventi. */
    public static function applica(array &$st, int $posto, string $idMossa, bool $verifica = true): array
    {
        try {
            $valida = !$verifica;
            if ($verifica) {
                foreach (self::mosse($st, $posto) as $m) {
                    if ($m['id'] === $idMossa) {
                        $valida = true;
                        break;
                    }
                }
            }
            if (!$valida) {
                throw new InvalidArgumentException('Mossa non consentita.');
            }
            $eventi = [];
            $nome = $st['nomi'][$posto];
            $sq = self::squadraDi($st, $posto);
            $parti = explode('|', $idMossa);

            if ($parti[0] === 'pesca') {
                $carta = array_shift($st['mazzo']);
                $st['mani'][$posto] = self::ordina(array_merge($st['mani'][$posto], [$carta]));
                $st['fase'] = 'azione';
                $eventi[] = ['per' => null, 'testo' => $nome . ' pesca dal mazzo.'];
                $eventi[] = ['per' => $posto, 'testo' => 'Hai pescato: ' . self::nome($carta) . '.'];
                return $eventi;
            }

            if ($parti[0] === 'scarti') {
                $cima = array_pop($st['scarti']);
                $pila = $st['scarti'];
                $st['scarti'] = [];
                $nPila = count($pila) + 1;
                $st['mani'][$posto] = self::ordina(array_merge($st['mani'][$posto], $pila));
                $eventi[] = ['per' => null, 'testo' => $nome . ' prende gli scarti: ' . $nPila . ' carte.'];
                if (count($pila) > 0) {
                    $eventi[] = ['per' => $posto, 'testo' => 'Con gli scarti hai preso: ' . self::elencaCarte($pila) . '.'];
                }
                if ($parti[1] === 'att') {
                    $i = (int) $parti[2];
                    $nuova = self::combinazione(array_merge($st['squadre'][$sq]['combinazioni'][$i]['carte'], [$cima]));
                    $st['squadre'][$sq]['combinazioni'][$i] = $nuova;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' attacca ' . self::nome($cima) . ' alla ' . self::descrivi($nuova) . '.'];
                } else {
                    $ids = array_map('intval', explode(',', $parti[2]));
                    $daMano = array_values(array_diff($ids, [$cima]));
                    self::togli($st['mani'][$posto], $daMano);
                    $comb = self::combinazione($ids);
                    $st['squadre'][$sq]['combinazioni'][] = $comb;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' cala ' . self::descrivi($comb) . ': ' . self::elencaCarte($ids) . '.'];
                }
                $st['fase'] = 'azione';
                self::controllaApertura($st, $posto, $eventi);
                self::dopoCalata($st, $posto, $eventi);
                return $eventi;
            }

            if ($parti[0] === 'cala') {
                self::ricordaProvvisorio($st, $posto);
                $ids = array_map('intval', explode(',', $parti[1]));
                self::togli($st['mani'][$posto], $ids);
                $comb = self::combinazione($ids);
                $st['squadre'][$sq]['combinazioni'][] = $comb;
                $eventi[] = ['per' => null, 'testo' => $nome . ' cala ' . self::descrivi($comb) . ': ' . self::elencaCarte($ids) . '.'];
                self::controllaApertura($st, $posto, $eventi);
                self::dopoCalata($st, $posto, $eventi);
                return $eventi;
            }

            if ($parti[0] === 'att') {
                self::ricordaProvvisorio($st, $posto);
                $id = (int) $parti[1];
                $i = (int) $parti[2];
                self::togli($st['mani'][$posto], [$id]);
                $nuova = self::combinazione(array_merge($st['squadre'][$sq]['combinazioni'][$i]['carte'], [$id]));
                $st['squadre'][$sq]['combinazioni'][$i] = $nuova;
                $eventi[] = ['per' => null, 'testo' => $nome . ' attacca ' . self::nome($id) . ' alla ' . self::descrivi($nuova) . '.'];
                self::controllaApertura($st, $posto, $eventi);
                self::dopoCalata($st, $posto, $eventi);
                return $eventi;
            }

            if ($parti[0] === 'annulla') {
                $st['mani'][$posto] = self::ordina($st['provv']['mano']);
                $st['squadre'][$sq]['combinazioni'] = [];
                $st['provv'] = null;
                $st['rinuncia'] = true;
                $eventi[] = ['per' => null, 'testo' => $nome . ' annulla le combinazioni provvisorie.'];
                return $eventi;
            }

            if ($parti[0] === 'scarta') {
                $id = (int) $parti[1];
                self::togli($st['mani'][$posto], [$id]);
                $st['scarti'][] = $id;
                $eventi[] = ['per' => null, 'testo' => $nome . ' scarta ' . self::nome($id) . '.'];
                $st['provv'] = null;
                $st['rinuncia'] = false;
                if (count($st['mani'][$posto]) === 0) {
                    if (!$st['squadre'][$sq]['pozzetto']) {
                        self::prendiPozzetto($st, $posto, $eventi);
                    } else {
                        // chiusura (mosse() garantisce il burraco)
                        $st['chiusa_da'] = $posto;
                        $eventi[] = ['per' => null, 'testo' => $nome . ' chiude la mano!'];
                        self::conta($st, $eventi);
                        return $eventi;
                    }
                }
                $st['turno'] = ($posto + 1) % $st['n'];
                $st['fase'] = 'pesca';
                self::controllaMazzo($st, $eventi);
                return $eventi;
            }
            throw new InvalidArgumentException('Mossa sconosciuta.');
        } catch (Throwable $e) {
            registra_errore('Burraco::applica', $e);
            throw $e;
        }
    }

    /** Dopo aver calato carte: se la mano è vuota si prende il pozzetto o si chiude. */
    private static function dopoCalata(array &$st, int $posto, array &$eventi): void
    {
        try {
            if (count($st['mani'][$posto]) > 0) {
                return;
            }
            $sq = self::squadraDi($st, $posto);
            if (!$st['squadre'][$sq]['pozzetto']) {
                self::prendiPozzetto($st, $posto, $eventi);
                return;
            }
            $st['chiusa_da'] = $posto;
            $eventi[] = ['per' => null, 'testo' => $st['nomi'][$posto] . ' chiude la mano senza scartare!'];
            self::conta($st, $eventi);
        } catch (Throwable $e) {
            registra_errore('Burraco::dopoCalata', $e);
            throw $e;
        }
    }

    /** Se chi deve giocare non può pescare né prendere gli scarti, la mano finisce. */
    private static function controllaMazzo(array &$st, array &$eventi): void
    {
        try {
            if ($st['finita'] || $st['fase'] !== 'pesca') {
                return;
            }
            if (count(self::mosse($st, $st['turno'])) === 0) {
                $eventi[] = ['per' => null, 'testo' => 'Il mazzo è finito: la mano termina.'];
                self::conta($st, $eventi);
            }
        } catch (Throwable $e) {
            registra_errore('Burraco::controllaMazzo', $e);
            throw $e;
        }
    }

    private static function nomiSquadre(array $st): array
    {
        try {
            $nomiSq = [];
            for ($sq = 0; $sq < 2; $sq++) {
                $membri = [];
                for ($p = 0; $p < $st['n']; $p++) {
                    if (self::squadraDi($st, $p) === $sq) {
                        $membri[] = $st['nomi'][$p];
                    }
                }
                $nomiSq[$sq] = implode(' e ', $membri);
            }
            return $nomiSq;
        } catch (Throwable $e) {
            registra_errore('Burraco::nomiSquadre', $e);
            throw $e;
        }
    }

    /** Fine di una mano: si contano i punti, si sommano ai totali e si ricomincia, oppure la partita è finita. */
    private static function conta(array &$st, array &$eventi): void
    {
        try {
            $punti = [0, 0];
            $dettaglio = [];
            for ($sq = 0; $sq < 2; $sq++) {
                $tavolo = 0;
                foreach ($st['squadre'][$sq]['combinazioni'] as $c) {
                    $tavolo += self::puntiCombinazione($c);
                }
                $mano = 0;
                for ($p = 0; $p < $st['n']; $p++) {
                    if (self::squadraDi($st, $p) === $sq) {
                        foreach ($st['mani'][$p] as $id) {
                            $mano += self::valore($id);
                        }
                    }
                }
                $chiusura = ($st['chiusa_da'] !== null && self::squadraDi($st, (int) $st['chiusa_da']) === $sq) ? 100 : 0;
                $pozzetto = $st['squadre'][$sq]['pozzetto'] ? 0 : -100;
                $punti[$sq] = $tavolo + $chiusura + $pozzetto - $mano;
                $dettaglio[$sq] = ['tavolo' => $tavolo, 'chiusura' => $chiusura, 'pozzetto' => $pozzetto, 'in_mano' => -$mano, 'totale' => $punti[$sq]];
            }
            $st['punteggi'] = $dettaglio;
            $nomiSq = self::nomiSquadre($st);
            for ($sq = 0; $sq < 2; $sq++) {
                $st['partita']['totali'][$sq] += $punti[$sq];
            }
            $tot = $st['partita']['totali'];
            $st['partita']['storico'][] = ['mano' => $st['partita']['mano'], 'punti' => $punti];
            $eventi[] = ['per' => null, 'testo' => 'Fine della mano ' . $st['partita']['mano'] . '. Punti della mano: ' . $nomiSq[0] . ' ' . $punti[0] . ', ' . $nomiSq[1] . ' ' . $punti[1]
                . '. Totali: ' . $nomiSq[0] . ' ' . $tot[0] . ', ' . $nomiSq[1] . ' ' . $tot[1] . '.'];

            $obiettivo = $st['partita']['obiettivo'];
            $fine = false;
            if (max($tot) >= $obiettivo && $tot[0] !== $tot[1]) {
                $fine = true;
                $st['motivo'] = 'Raggiunti ' . $obiettivo . ' punti.';
            } elseif ($st['partita']['mano'] >= 15) {
                $fine = true;
                $st['motivo'] = 'Sono state giocate ' . $st['partita']['mano'] . ' mani.';
            }
            if ($fine) {
                $st['finita'] = true;
                $st['vincitori'] = $tot[0] === $tot[1] ? [] : ($tot[0] > $tot[1] ? [0] : [1]);
                $eventi[] = ['per' => null, 'testo' => $tot[0] === $tot[1] ? 'La partita finisce in parità.' : 'Vince la partita ' . $nomiSq[$tot[0] > $tot[1] ? 0 : 1] . '.'];
                return;
            }
            $st['partita']['mano']++;
            $primo = ($st['partita']['mano'] - 1) % $st['n'];
            self::daiCarte($st, $primo);
            $eventi[] = ['per' => null, 'testo' => 'Comincia la mano ' . $st['partita']['mano'] . '. Negli scarti c\'è ' . self::nome($st['scarti'][0]) . '. Inizia ' . $st['nomi'][$primo] . '.'];
        } catch (Throwable $e) {
            registra_errore('Burraco::conta', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ vista per un giocatore
    public static function vista(array $st, int $posto): array
    {
        try {
            $mioSq = $posto >= 0 ? self::squadraDi($st, $posto) : -1;
            $v = [
                'tipo' => 'burraco',
                'n' => $st['n'],
                'nomi' => $st['nomi'],
                'turno' => $st['turno'],
                'fase' => $st['fase'],
                'mazzo' => count($st['mazzo']),
                'pozzetti_rimasti' => count($st['pozzetti']),
                'scarti' => ['n' => count($st['scarti']), 'cima' => count($st['scarti']) ? self::carta($st['scarti'][count($st['scarti']) - 1]) : null,
                    'carte' => array_map(fn($id) => self::carta($id), $st['scarti']), 'congelato' => self::congelato($st)],
                'mano' => array_map(fn($id) => self::carta($id), $posto >= 0 && isset($st['mani'][$posto]) ? $st['mani'][$posto] : []),
                'carte_in_mano' => array_map('count', $st['mani']),
                'squadre' => [],
                'finita' => $st['finita'],
                'punteggi' => $st['punteggi'],
                'partita' => ['obiettivo' => $st['partita']['obiettivo'], 'totali' => $st['partita']['totali'], 'mano' => $st['partita']['mano']],
                'mia_squadra' => $mioSq,
                'miei_compagni' => [],
            ];
            for ($sq = 0; $sq < 2; $sq++) {
                $comb = [];
                foreach ($st['squadre'][$sq]['combinazioni'] as $i => $c) {
                    $comb[] = ['indice' => $i, 'descrizione' => self::descrivi($c), 'tipo' => $c['tipo'], 'burraco' => self::eBurraco($c) ? (self::eBurracoPulito($c) ? 'pulito' : 'sporco') : null,
                        'carte' => array_map(fn($id) => self::carta($id), $c['carte'])];
                }
                $membri = [];
                for ($p = 0; $p < $st['n']; $p++) {
                    if (self::squadraDi($st, $p) === $sq) {
                        $membri[] = $p;
                    }
                }
                $v['squadre'][] = ['membri' => $membri, 'pozzetto_preso' => $st['squadre'][$sq]['pozzetto'], 'aperta' => $st['squadre'][$sq]['aperta'],
                    'minimo' => self::minimoApertura($st, $sq), 'punti_calati' => self::puntiCarte($st['squadre'][$sq]['combinazioni']), 'combinazioni' => $comb];
            }
            return $v;
        } catch (Throwable $e) {
            registra_errore('Burraco::vista', $e);
            throw $e;
        }
    }

    /** Frase da leggere per descrivere la situazione al giocatore (mano, tavolo). */
    public static function situazione(array $st, int $posto): string
    {
        try {
            $t = 'Mano ' . $st['partita']['mano'] . ', si gioca fino a ' . $st['partita']['obiettivo'] . ' punti. ';
            $nomiSq = self::nomiSquadre($st);
            $t .= 'Totali: ' . $nomiSq[0] . ' ' . $st['partita']['totali'][0] . ', ' . $nomiSq[1] . ' ' . $st['partita']['totali'][1] . '. ';
            $t .= 'Hai in mano ' . count($st['mani'][$posto]) . ' carte: ' . self::elencaCarte($st['mani'][$posto]) . '. ';
            $t .= 'Mazzo: ' . count($st['mazzo']) . ' carte. ';
            $t .= count($st['scarti']) ? 'In cima agli scarti c\'è ' . self::nome($st['scarti'][count($st['scarti']) - 1]) . ', in tutto ' . count($st['scarti']) . ' carte' . (self::congelato($st) ? ', pozzo congelato' : '') . '. ' : 'Gli scarti sono vuoti. ';
            for ($sq = 0; $sq < 2; $sq++) {
                $mia = $sq === self::squadraDi($st, $posto);
                $c = $st['squadre'][$sq]['combinazioni'];
                $t .= ($mia ? 'La tua squadra' : 'La squadra avversaria') . ($st['squadre'][$sq]['pozzetto'] ? ' ha preso il pozzetto' : ' non ha ancora preso il pozzetto')
                    . ($st['squadre'][$sq]['aperta'] ? '' : ', non ha ancora aperto: servono ' . self::minimoApertura($st, $sq) . ' punti') . ': ';
                if (!$c) {
                    $t .= 'nessuna combinazione. ';
                } else {
                    $parti = [];
                    foreach ($c as $i => $x) {
                        $parti[] = ($i + 1) . ', ' . self::descrivi($x) . ': ' . self::elencaCarte($x['carte']);
                    }
                    $t .= implode('; ', $parti) . '. ';
                }
            }
            return trim($t);
        } catch (Throwable $e) {
            registra_errore('Burraco::situazione', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ computer
    /** Combinazioni disgiunte, le più ricche prima, da calare per aprire: elenco di elenchi di id con i punti totali. */
    private static function pianoApertura(array $mano): array
    {
        try {
            $melds = self::nuoveCombinazioni($mano);
            usort($melds, function ($a, $b) {
                return array_sum(array_map(fn($i) => self::valore($i), $b)) <=> array_sum(array_map(fn($i) => self::valore($i), $a));
            });
            $usate = [];
            $scelte = [];
            $punti = 0;
            foreach ($melds as $ids) {
                if (count(array_intersect($ids, $usate)) === 0) {
                    $scelte[] = $ids;
                    $usate = array_merge($usate, $ids);
                    $punti += array_sum(array_map(fn($i) => self::valore($i), $ids));
                }
            }
            return ['melds' => $scelte, 'punti' => $punti];
        } catch (Throwable $e) {
            registra_errore('Burraco::pianoApertura', $e);
            throw $e;
        }
    }

    /** Sceglie una mossa per il computer. */
    public static function bot(array $st, int $posto): string
    {
        try {
            $mosse = self::mosse($st, $posto);
            $sq = self::squadraDi($st, $posto);
            $per = [];
            foreach ($mosse as $m) {
                $per[explode('|', $m['id'])[0] . (str_starts_with($m['id'], 'scarti|') ? '|' . explode('|', $m['id'])[1] : '')][] = $m;
            }

            if ($st['fase'] === 'pesca') {
                // gli scarti solo se aiutano davvero: attaccare, oppure un tris/scala di sole carte vere con pochi scarti
                if (isset($per['scarti|att'])) {
                    return $per['scarti|att'][0]['id'];
                }
                if (isset($per['scarti|cala']) && count($st['scarti']) <= 6) {
                    foreach ($per['scarti|cala'] as $m) {
                        $ids = array_map('intval', explode(',', explode('|', $m['id'])[2]));
                        $ok = true;
                        foreach ($ids as $id) {
                            if (self::puoEssereJolly($id)) {
                                $ok = false;
                            }
                        }
                        if ($ok) {
                            return $m['id'];
                        }
                    }
                }
                return isset($per['pesca']) ? 'pesca' : $mosse[0]['id'];
            }

            $mano = $st['mani'][$posto];
            $squadraStato = $st['squadre'][$sq];
            // prima apertura: si cala solo se con le carte in mano si raggiunge il minimo, altrimenti si scarta
            if (!$squadraStato['aperta']) {
                $provvisoria = count($squadraStato['combinazioni']) > 0;
                if (!$st['rinuncia']) {
                    $minimo = self::minimoApertura($st, $sq);
                    $gia = self::puntiCarte($squadraStato['combinazioni']);
                    $piano = self::pianoApertura($mano);
                    if ($gia + $piano['punti'] >= $minimo) {
                        foreach ($piano['melds'] as $ids) {
                            $id = 'cala|' . implode(',', $ids);
                            foreach ($per['cala'] ?? [] as $m) {
                                if ($m['id'] === $id) {
                                    return $id;
                                }
                            }
                        }
                    }
                }
                if ($provvisoria) {
                    return 'annulla';
                }
                foreach ($per['scarta'] ?? [] as $m) {
                    $id = (int) explode('|', $m['id'])[1];
                    $punti = self::utilita($mano, $id);
                    if (!isset($migliore0) || $punti < $migliorePunteggio0) {
                        $migliorePunteggio0 = $punti;
                        $migliore0 = $m['id'];
                    }
                }
                return $migliore0 ?? $mosse[0]['id'];
            }

            // prima si attacca quanto si può, poi si cala, ma senza restare con meno di due carte se non si può chiudere
            if (isset($per['att'])) {
                // prima i jolly non servono: si attacca solo carte vere, a meno che serva a chiudere
                foreach ($per['att'] as $m) {
                    $id = (int) explode('|', $m['id'])[1];
                    if (!self::puoEssereJolly($id)) {
                        return $m['id'];
                    }
                }
            }
            if (isset($per['cala'])) {
                usort($per['cala'], function ($a, $b) {
                    return count(explode(',', explode('|', $b['id'])[1])) <=> count(explode(',', explode('|', $a['id'])[1]));
                });
                foreach ($per['cala'] as $m) {
                    $ids = array_map('intval', explode(',', explode('|', $m['id'])[1]));
                    if (count($mano) - count($ids) >= 1) {
                        return $m['id'];
                    }
                }
            }
            // si scarta la carta meno utile: isolata e di poco valore, mai i jolly se si può
            $migliore = null;
            $migliorePunteggio = PHP_INT_MAX;
            foreach ($per['scarta'] ?? [] as $m) {
                $id = (int) explode('|', $m['id'])[1];
                $punti = self::utilita($mano, $id);
                if ($punti < $migliorePunteggio) {
                    $migliorePunteggio = $punti;
                    $migliore = $m['id'];
                }
            }
            if ($migliore !== null) {
                return $migliore;
            }
            return $mosse[0]['id'];
        } catch (Throwable $e) {
            registra_errore('Burraco::bot', $e);
            throw $e;
        }
    }

    /** Quanto una carta serve in mano: più è alto, meno conviene scartarla. */
    private static function utilita(array $mano, int $id): int
    {
        try {
            if (self::puoEssereJolly($id)) {
                return 500;
            }
            $punti = 0;
            $r = self::rango($id);
            $s = self::seme($id);
            foreach ($mano as $altro) {
                if ($altro === $id || self::eJolly($altro)) {
                    continue;
                }
                if (self::rango($altro) === $r) {
                    $punti += 30;
                }
                if (self::seme($altro) === $s && abs(self::rango($altro) - $r) <= 2) {
                    $punti += (abs(self::rango($altro) - $r) === 1) ? 25 : 12;
                }
            }
            return $punti + intdiv(self::valore($id), 5);
        } catch (Throwable $e) {
            registra_errore('Burraco::utilita', $e);
            throw $e;
        }
    }
}
