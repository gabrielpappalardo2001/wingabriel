<?php
// Scopa per 2 giocatori (uno contro uno) o 4 (a coppie: 0 e 2 contro 1 e 3), a mani successive.
//
// Regole e varianti complete:
//  - Classica: fino a 11 punti con carte, denari, settebello, primiera e scope.
//  - Partita a 21 punti: partita tradizionale lunga.
//  - Scopone scientifico (4 giocatori a coppie): 9 carte a testa e 4 in tavola distribuite tutte all'inizio.
//  - Asso pigliatutto (Scopa d'Assi): giocando l'Asso si prende tutto il tavolo se non c'è già un asso singolo da prendere.
//  - Napola (Piccola): sequenza continua di denari dall'Asso (Asso, 2, 3 = 3 punti; con il 4 = 4 punti, fino a 10 punti per il Re).
//  - Re Bello: assegna 1 punto aggiuntivo a chi cattura il Re di denari (oltre al Settebello).
//  - Scopa a 15: la presa avviene quando il valore della carta giocata sommato a quello delle carte sul tavolo fa 15.
//  - Completa: combina Napola, Asso pigliatutto e Re Bello in una sola partita.

declare(strict_types=1);

require_once __DIR__ . '/mazzo-italiano.php';

final class Scopa
{
    private const OBIETTIVO = 11;
    private const SETTEBELLO = 16; // sette di denari: seme 1, posizione 6
    private const REBELLO = 19;    // re di denari: seme 1, posizione 9
    private const PRIMIERA = [21 => 6, 18 => 5, 16 => 0, 15 => 4, 14 => 3, 13 => 2, 12 => 1];
    public const VARIANTI = [
        'classica' => ['nome' => 'Classica', 'descrizione' => 'Si gioca fino a 11 punti, con carte, denari, settebello, primiera e scope.', 'posti' => [2, 4]],
        'punti_21' => ['nome' => 'A 21 punti', 'descrizione' => 'La partita tradizionale lunga.', 'posti' => [2, 4]],
        'scopone' => ['nome' => 'Scopone scientifico', 'descrizione' => 'In quattro a coppie. Nove carte a testa e quattro sul tavolo, distribuite tutte all inizio.', 'posti' => [4]],
        'asso_piglia_tutto' => ['nome' => 'Asso pigliatutto', 'descrizione' => 'Giocando l asso si prende tutto il tavolo, se non c e gia un asso singolo da prendere.', 'posti' => [2, 4]],
        'napola' => ['nome' => 'Napola', 'descrizione' => 'Una sequenza continua di denari dall asso vale punti, fino a 10 per il re.', 'posti' => [2, 4]],
        'rebello' => ['nome' => 'Re bello', 'descrizione' => 'Chi prende il re di denari ha un punto in piu, oltre al settebello.', 'posti' => [2, 4]],
        'scopa_quindici' => ['nome' => 'Scopa a 15', 'descrizione' => 'Si prende quando la carta giocata, sommata a quelle sul tavolo, fa 15.', 'posti' => [2, 4]],
        'completa' => ['nome' => 'Completa', 'descrizione' => 'Napola, asso pigliatutto e re bello insieme.', 'posti' => [2, 4]],
    ];

    public static function squadraDi(array $st, int $posto): int
    {
        try {
            return $st['n'] === 4 ? $posto % 2 : $posto;
        } catch (Throwable $e) {
            registra_errore('Scopa::squadraDi', $e);
            throw $e;
        }
    }

    private static function valorePrimiera(int $id): int
    {
        try {
            return [16, 12, 13, 14, 15, 18, 21, 10, 10, 10][$id % 10];
        } catch (Throwable $e) {
            registra_errore('Scopa::valorePrimiera', $e);
            throw $e;
        }
    }

    public static function nuova(array $nomi, string $variante = 'classica', int $puntiObiettivo = 0): array
    {
        try {
            $n = count($nomi);
            $variantiValide = ['classica', 'punti_21', 'scopone', 'asso_piglia_tutto', 'napola', 'rebello', 'scopa_quindici', 'completa'];
            $variante = in_array($variante, $variantiValide, true) ? $variante : 'classica';
            if ($variante === 'scopone' && $n !== 4) {
                $n = 4;
            } elseif ($n !== 2 && $n !== 4) {
                $n = $n >= 4 ? 4 : 2;
            }

            $obiettivo = 11;
            if ($variante === 'punti_21') {
                $obiettivo = 21;
            } elseif ($puntiObiettivo > 0) {
                $obiettivo = $puntiObiettivo;
            }

            $st = [
                'tipo' => 'scopa',
                'variante' => $variante,
                'n' => $n,
                'nomi' => array_values($nomi),
                'partita' => ['obiettivo' => $obiettivo, 'totali' => [0, 0], 'mano' => 1],
                'finita' => false,
                'vincitori' => [],
                'punteggi' => null,
                'punteggi_mano' => null,
                'motivo' => '',
                'ultima' => null,
            ];
            self::daiCarte($st, 0);
            return $st;
        } catch (Throwable $e) {
            registra_errore('Scopa::nuova', $e);
            throw $e;
        }
    }

    private static function daiCarte(array &$st, int $primo): void
    {
        try {
            $n = $st['n'];
            $variante = $st['variante'];
            do {
                $mazzo = MazzoItaliano::mescolato();
                $tavolo = array_splice($mazzo, 0, 4);
                $re = count(array_filter($tavolo, fn($c) => $c % 10 === 9));
            } while ($re >= 3);
            $st['tavolo'] = MazzoItaliano::ordina($tavolo);
            $st['mazzo'] = $mazzo;
            $st['mani'] = array_fill(0, $n, []);

            if ($variante === 'scopone') {
                // Scopone scientifico: 9 carte a testa e 4 in tavola
                for ($giro = 0; $giro < 9; $giro++) {
                    for ($i = 0; $i < $n; $i++) {
                        $st['mani'][$i][] = array_shift($st['mazzo']);
                    }
                }
                foreach ($st['mani'] as $i => $m) {
                    $st['mani'][$i] = MazzoItaliano::ordina($m);
                }
            } else {
                self::daiTre($st);
            }

            $st['prese'] = [[], []];
            $st['scope'] = [0, 0];
            $st['ultima_presa'] = null;
            $st['turno'] = $primo;
        } catch (Throwable $e) {
            registra_errore('Scopa::daiCarte', $e);
            throw $e;
        }
    }

    private static function daiTre(array &$st): void
    {
        try {
            for ($giro = 0; $giro < 3; $giro++) {
                for ($i = 0; $i < $st['n']; $i++) {
                    $st['mani'][$i][] = array_shift($st['mazzo']);
                }
            }
            foreach ($st['mani'] as $i => $m) {
                $st['mani'][$i] = MazzoItaliano::ordina($m);
            }
        } catch (Throwable $e) {
            registra_errore('Scopa::daiTre', $e);
            throw $e;
        }
    }

    public static function inizio(array $st): array
    {
        try {
            $v = $st['variante'];
            $nomeVariante = 'a scopa';
            if ($v === 'scopone') {
                $nomeVariante = 'a Scopone scientifico (9 carte a testa)';
            } elseif ($v === 'asso_piglia_tutto') {
                $nomeVariante = 'a Scopa d\'Assi (Asso pigliatutto)';
            } elseif ($v === 'napola') {
                $nomeVariante = 'a Scopa con Napola (sequenze di denari)';
            } elseif ($v === 'rebello') {
                $nomeVariante = 'a Scopa con Re Bello';
            } elseif ($v === 'scopa_quindici') {
                $nomeVariante = 'a Scopa a 15 (prese con somma a 15)';
            } elseif ($v === 'completa') {
                $nomeVariante = 'a Scopa completa (Napola, Asso pigliatutto e Re Bello)';
            }
            $r = [['per' => null, 'testo' => 'Si gioca ' . $nomeVariante . ' fino a ' . $st['partita']['obiettivo'] . ' punti. Sul tavolo ci sono: ' . MazzoItaliano::elenca($st['tavolo']) . '. Inizia ' . $st['nomi'][$st['turno']] . '.']];
            foreach ($st['mani'] as $i => $m) {
                $r[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Scopa::inizio', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ mosse
    /** Le prese possibili con una carta: elenco di elenchi di id del tavolo (un elenco vuoto: la carta resta sul tavolo). */
    private static function prese(array $tavolo, int $carta, string $variante = 'classica'): array
    {
        try {
            $v = MazzoItaliano::numero($carta);

            // Variante Scopa a 15: si prende quando carta giocata + somma carte sul tavolo fa 15
            if ($variante === 'scopa_quindici') {
                $manca = 15 - $v;
                if ($manca <= 0) {
                    return [[]];
                }
                $trovate = self::sommeSulTavolo($tavolo, $manca);
                return $trovate ?: [[]];
            }

            // Variante Asso pigliatutto / Completa: giocata dell'asso
            if (($variante === 'asso_piglia_tutto' || $variante === 'completa') && $v === 1) {
                $ugualiAssi = [];
                foreach ($tavolo as $c) {
                    if (MazzoItaliano::numero($c) === 1) {
                        $ugualiAssi[] = [$c];
                    }
                }
                // Se c'è già un asso sul tavolo, si prende quello
                if ($ugualiAssi) {
                    return $ugualiAssi;
                }
                // Se non c'è asso e il tavolo ha carte, l'asso piglia TUTTO il tavolo
                if ($tavolo) {
                    return [$tavolo];
                }
                return [[]];
            }

            // Regole classiche: carta uguale obbligatoria
            $uguali = [];
            foreach ($tavolo as $c) {
                if (MazzoItaliano::numero($c) === $v) {
                    $uguali[] = [$c];
                }
            }
            if ($uguali) {
                return $uguali;
            }

            $n = count($tavolo);
            if ($n < 2) {
                return [[]];
            }
            $trovate = self::sommeSulTavolo($tavolo, $v);
            return $trovate ?: [[]];
        } catch (Throwable $e) {
            registra_errore('Scopa::prese', $e);
            throw $e;
        }
    }

    private static function sommeSulTavolo(array $tavolo, int $sommaObiettivo): array
    {
        try {
            $n = count($tavolo);
            if ($n === 0) {
                return [];
            }
            $meta = intdiv($n, 2);
            $sinistra = self::sottoinsiemi(array_slice($tavolo, 0, $meta), $sommaObiettivo);
            $destra = self::sottoinsiemi(array_slice($tavolo, $meta), $sommaObiettivo);
            $risultati = [];
            foreach ($sinistra as $sa => $liste) {
                $manca = $sommaObiettivo - $sa;
                if (!isset($destra[$manca])) {
                    continue;
                }
                foreach ($liste as $la) {
                    foreach ($destra[$manca] as $lb) {
                        if (count($la) + count($lb) >= 1) {
                            $risultati[] = array_merge($la, $lb);
                        }
                    }
                }
            }
            return $risultati;
        } catch (Throwable $e) {
            registra_errore('Scopa::sommeSulTavolo', $e);
            throw $e;
        }
    }

    /** Per ogni somma fino al massimo specificato, i sottoinsiemi di carte che la fanno (l'insieme vuoto fa 0). */
    private static function sottoinsiemi(array $carte, int $sommaMax = 15): array
    {
        try {
            $out = [0 => [[]]];
            $m = count($carte);
            $limite = 1 << $m;
            for ($maschera = 1; $maschera < $limite; $maschera++) {
                $somma = 0;
                $scelte = [];
                for ($i = 0; $i < $m; $i++) {
                    if (($maschera & (1 << $i)) === 0) {
                        continue;
                    }
                    $somma += MazzoItaliano::numero($carte[$i]);
                    if ($somma > $sommaMax) {
                        $scelte = null;
                        break;
                    }
                    $scelte[] = $carte[$i];
                }
                if ($scelte !== null) {
                    if (!isset($out[$somma])) {
                        $out[$somma] = [];
                    }
                    $out[$somma][] = $scelte;
                }
            }
            return $out;
        } catch (Throwable $e) {
            registra_errore('Scopa::sottoinsiemi', $e);
            throw $e;
        }
    }

    private static function eUltimaGiocata(array $st, int $carta): bool
    {
        try {
            if (count($st['mazzo']) > 0) {
                return false;
            }
            $rimaste = 0;
            foreach ($st['mani'] as $m) {
                $rimaste += count($m);
            }
            return $rimaste === 1;
        } catch (Throwable $e) {
            registra_errore('Scopa::eUltimaGiocata', $e);
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
            $v = $st['variante'] ?? 'classica';
            foreach ($st['mani'][$posto] as $carta) {
                foreach (self::prese($st['tavolo'], $carta, $v) as $presa) {
                    $id = 'gioca|' . $carta . '|' . ($presa ? implode(',', $presa) : '-');
                    $nome = MazzoItaliano::nome($carta);
                    $vale = MazzoItaliano::numero($carta);
                    $testo = $nome . ', vale ' . $vale;
                    if ($carta === self::SETTEBELLO) {
                        $testo .= ', sette bello';
                    }
                    if ($carta === self::REBELLO && ($v === 'rebello' || $v === 'completa')) {
                        $testo .= ', re bello';
                    }
                    if (!$presa) {
                        $testo .= ', la lasci sul tavolo';
                    } else {
                        $testo .= ', prendi ' . implode(' e ', array_map(fn($c) => MazzoItaliano::nome($c), $presa));
                        $eraPigliatutto = ($v === 'asso_piglia_tutto' || $v === 'completa') && $vale === 1 && count($presa) > 1 && count($presa) === count($st['tavolo']);
                        if ($eraPigliatutto) {
                            $testo .= ' (asso pigliatutto)';
                        } elseif (count($presa) === count($st['tavolo']) && !self::eUltimaGiocata($st, $carta)) {
                            $testo .= ' (scopa)';
                        }
                    }
                    $r[] = ['id' => $id, 'testo' => $testo];
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Scopa::mosse', $e);
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
            if ($parti[0] !== 'gioca' || count($parti) !== 3) {
                throw new InvalidArgumentException('Mossa non consentita.');
            }
            $carta = (int) $parti[1];
            $presa = $parti[2] === '-' ? [] : array_map('intval', explode(',', $parti[2]));
            $chiave = array_search($carta, $st['mani'][$posto], true);
            if ($chiave === false) {
                throw new InvalidArgumentException('Mossa non consentita.');
            }
            array_splice($st['mani'][$posto], (int) $chiave, 1);
            $nome = $st['nomi'][$posto];
            $sq = self::squadraDi($st, $posto);
            $eventi = [];
            $v = $st['variante'] ?? 'classica';
            // la carta è già uscita dalla mano: se non resta altro è l'ultima giocata della mano
            $nessunaAltra = count($st['mazzo']) === 0 && array_sum(array_map('count', $st['mani'])) === 0;

            if ($presa) {
                foreach ($presa as $c) {
                    $k = array_search($c, $st['tavolo'], true);
                    if ($k === false) {
                        throw new InvalidArgumentException('Mossa non consentita.');
                    }
                    array_splice($st['tavolo'], (int) $k, 1);
                    $st['prese'][$sq][] = $c;
                }
                $st['prese'][$sq][] = $carta;
                $st['ultima_presa'] = $sq;
                $testo = $nome . ' gioca ' . MazzoItaliano::nome($carta) . ' e prende ' . MazzoItaliano::elenca($presa) . '.';
                $eraPigliatutto = ($v === 'asso_piglia_tutto' || $v === 'completa') && ($carta % 10 === 0) && count($presa) > 1;
                if (!$st['tavolo'] && !$nessunaAltra) {
                    if ($eraPigliatutto) {
                        $testo .= ' Asso pigliatutto! Tavolo ripulito.';
                    } else {
                        $st['scope'][$sq]++;
                        $testo .= ' Scopa!';
                    }
                } elseif ($eraPigliatutto) {
                    $testo .= ' Asso pigliatutto! Tavolo ripulito.';
                }
                $eventi[] = ['per' => null, 'testo' => $testo];
                $st['ultima'] = $testo;
            } else {
                $st['tavolo'] = MazzoItaliano::ordina(array_merge($st['tavolo'], [$carta]));
                $testo = $nome . ' gioca ' . MazzoItaliano::nome($carta) . ' e la lascia sul tavolo.';
                $eventi[] = ['per' => null, 'testo' => $testo];
                $st['ultima'] = $testo;
            }
            $st['turno'] = ($posto + 1) % $st['n'];

            $inMano = array_sum(array_map('count', $st['mani']));
            if ($inMano === 0) {
                if (count($st['mazzo']) > 0) {
                    self::daiTre($st);
                    $eventi[] = ['per' => null, 'testo' => 'Si danno altre tre carte a testa. Mazzo: ' . count($st['mazzo']) . ' carte.'];
                    foreach ($st['mani'] as $i => $m) {
                        $eventi[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
                    }
                } else {
                    self::finisciMano($st, $eventi);
                }
            }
            return $eventi;
        } catch (Throwable $e) {
            registra_errore('Scopa::applica', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ punti
    private static function nomiSquadra(array $st, int $sq): string
    {
        try {
            if ($st['n'] === 2) {
                return $st['nomi'][$sq];
            }
            return $st['nomi'][$sq] . ' e ' . $st['nomi'][$sq + 2];
        } catch (Throwable $e) {
            registra_errore('Scopa::nomiSquadra', $e);
            throw $e;
        }
    }

    private static function primiera(array $carte): ?int
    {
        try {
            $migliore = [];
            foreach ($carte as $c) {
                $s = MazzoItaliano::seme($c);
                $migliore[$s] = max($migliore[$s] ?? 0, self::valorePrimiera($c));
            }
            return count($migliore) === 4 ? array_sum($migliore) : null;
        } catch (Throwable $e) {
            registra_errore('Scopa::primiera', $e);
            throw $e;
        }
    }

    private static function calcolaNapola(array $carte): int
    {
        try {
            $seq = 0;
            // Asso = 10, 2 = 11, 3 = 12 ... Re = 19
            for ($pos = 0; $pos < 10; $pos++) {
                if (in_array(10 + $pos, $carte, true)) {
                    $seq++;
                } else {
                    break;
                }
            }
            return $seq >= 3 ? $seq : 0;
        } catch (Throwable $e) {
            registra_errore('Scopa::calcolaNapola', $e);
            throw $e;
        }
    }

    /** I punti di una mano, per ciascuna squadra. */
    private static function calcolaPunti(array $st): array
    {
        try {
            $v = $st['variante'] ?? 'classica';
            $usaNapola = $v === 'napola' || $v === 'completa';
            $usaRebello = $v === 'rebello' || $v === 'completa';

            $r = [];
            for ($sq = 0; $sq < 2; $sq++) {
                $c = $st['prese'][$sq];
                $napola = $usaNapola ? self::calcolaNapola($c) : 0;
                $rebello = $usaRebello && in_array(self::REBELLO, $c, true);
                $r[$sq] = [
                    'carte_prese' => count($c),
                    'denari_presi' => count(array_filter($c, fn($x) => MazzoItaliano::seme($x) === 1)),
                    'settebello' => in_array(self::SETTEBELLO, $c, true),
                    'rebello' => $rebello,
                    'napola' => $napola,
                    'primiera' => self::primiera($c),
                    'scope' => $st['scope'][$sq],
                    'carte' => 0,
                    'denari' => 0,
                    'sette_bello' => 0,
                    're_bello' => 0,
                    'primiera_punto' => 0,
                    'napola_punti' => $napola,
                    'totale' => 0,
                ];
            }
            for ($sq = 0; $sq < 2; $sq++) {
                $a = $r[$sq];
                $b = $r[1 - $sq];
                $r[$sq]['carte'] = $a['carte_prese'] > $b['carte_prese'] ? 1 : 0;
                $r[$sq]['denari'] = $a['denari_presi'] > $b['denari_presi'] ? 1 : 0;
                $r[$sq]['sette_bello'] = $a['settebello'] ? 1 : 0;
                $r[$sq]['re_bello'] = $a['rebello'] ? 1 : 0;
                $pa = $a['primiera'];
                $pb = $b['primiera'];
                $r[$sq]['primiera_punto'] = ($pa !== null && ($pb === null || $pa > $pb)) ? 1 : 0;
                $r[$sq]['totale'] = $r[$sq]['carte'] + $r[$sq]['denari'] + $r[$sq]['sette_bello'] + $r[$sq]['re_bello'] + $r[$sq]['primiera_punto'] + $a['napola_punti'] + $a['scope'];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Scopa::calcolaPunti', $e);
            throw $e;
        }
    }

    private static function finisciMano(array &$st, array &$eventi): void
    {
        try {
            if ($st['tavolo']) {
                $sq = $st['ultima_presa'] ?? 0;
                foreach ($st['tavolo'] as $c) {
                    $st['prese'][$sq][] = $c;
                }
                $eventi[] = ['per' => null, 'testo' => 'La mano è finita: le carte rimaste sul tavolo vanno a ' . self::nomiSquadra($st, $sq) . '.'];
                $st['tavolo'] = [];
            } else {
                $eventi[] = ['per' => null, 'testo' => 'La mano è finita.'];
            }
            $p = self::calcolaPunti($st);
            $st['punteggi_mano'] = $p;
            $st['punteggi'] = $p;
            for ($sq = 0; $sq < 2; $sq++) {
                $st['partita']['totali'][$sq] += $p[$sq]['totale'];
                $parti = [];
                if ($p[$sq]['carte']) {
                    $parti[] = 'carte';
                }
                if ($p[$sq]['denari']) {
                    $parti[] = 'denari';
                }
                if ($p[$sq]['sette_bello']) {
                    $parti[] = 'sette bello';
                }
                if ($p[$sq]['re_bello']) {
                    $parti[] = 're bello';
                }
                if ($p[$sq]['primiera_punto']) {
                    $parti[] = 'primiera';
                }
                if ($p[$sq]['napola_punti']) {
                    $parti[] = 'napola (' . $p[$sq]['napola_punti'] . ' punti)';
                }
                if ($p[$sq]['scope']) {
                    $parti[] = $p[$sq]['scope'] . ($p[$sq]['scope'] === 1 ? ' scopa' : ' scope');
                }
                $eventi[] = ['per' => null, 'testo' => self::nomiSquadra($st, $sq) . ': ' . ($parti ? implode(', ', $parti) : 'nessun punto') . '. Punti della mano: ' . $p[$sq]['totale'] . '. Totale: ' . $st['partita']['totali'][$sq] . '.'];
            }
            $t = $st['partita']['totali'];
            if (max($t) >= $st['partita']['obiettivo'] && $t[0] !== $t[1]) {
                $v = $t[0] > $t[1] ? 0 : 1;
                $st['finita'] = true;
                $st['vincitori'] = $st['n'] === 2 ? [$v] : [$v, $v + 2];
                $st['motivo'] = 'Vince ' . self::nomiSquadra($st, $v) . ' per ' . $t[$v] . ' a ' . $t[1 - $v] . '.';
                $eventi[] = ['per' => null, 'testo' => 'La partita è finita. ' . $st['motivo']];
                return;
            }
            $st['partita']['mano']++;
            $primo = ($st['partita']['mano'] - 1) % $st['n'];
            self::daiCarte($st, $primo);
            $eventi[] = ['per' => null, 'testo' => 'Comincia la mano ' . $st['partita']['mano'] . '. Sul tavolo ci sono: ' . MazzoItaliano::elenca($st['tavolo']) . '. Inizia ' . $st['nomi'][$primo] . '.'];
            foreach ($st['mani'] as $i => $m) {
                $eventi[] = ['per' => $i, 'testo' => 'Le tue carte: ' . MazzoItaliano::elenca($m) . '.'];
            }
        } catch (Throwable $e) {
            registra_errore('Scopa::finisciMano', $e);
            throw $e;
        }
    }

    private static function cartaDettaglio(int $id, string $variante): array
    {
        try {
            $speciale = null;
            if ($id === self::SETTEBELLO) {
                $speciale = 'settebello';
            } elseif ($id === self::REBELLO && ($variante === 'rebello' || $variante === 'completa')) {
                $speciale = 'rebello';
            } elseif (($id % 10 === 0) && ($variante === 'asso_piglia_tutto' || $variante === 'completa')) {
                $speciale = 'pigliatutto';
            }
            return MazzoItaliano::carta($id, $speciale);
        } catch (Throwable $e) {
            registra_errore('Scopa::cartaDettaglio', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ vista
    public static function vista(array $st, int $posto): array
    {
        try {
            $v = $st['variante'] ?? 'classica';
            $mioSq = $posto >= 0 ? self::squadraDi($st, $posto) : -1;
            $squadre = [];
            for ($sq = 0; $sq < 2; $sq++) {
                $membri = [];
                for ($p = 0; $p < $st['n']; $p++) {
                    if (self::squadraDi($st, $p) === $sq) {
                        $membri[] = $p;
                    }
                }
                $c = $st['prese'][$sq];
                $squadre[] = [
                    'membri' => $membri,
                    'carte_prese' => count($c),
                    'denari_presi' => count(array_filter($c, fn($x) => MazzoItaliano::seme($x) === 1)),
                    'settebello' => in_array(self::SETTEBELLO, $c, true),
                    'rebello' => in_array(self::REBELLO, $c, true),
                    'napola' => self::calcolaNapola($c),
                    'scope' => $st['scope'][$sq],
                ];
            }
            $riepilogo = [
                'Totali: ' . self::nomiSquadra($st, 0) . ' ' . $st['partita']['totali'][0] . ', ' . self::nomiSquadra($st, 1) . ' ' . $st['partita']['totali'][1],
                'Mazzo: ' . count($st['mazzo']) . ' carte',
            ];
            for ($sq = 0; $sq < 2; $sq++) {
                $infoSq = ($sq === $mioSq ? 'La tua squadra' : self::nomiSquadra($st, $sq)) . ': ' . count($st['prese'][$sq]) . ' carte prese, ' . $squadre[$sq]['scope'] . ' scope' . ($squadre[$sq]['settebello'] ? ', ha il sette bello' : '');
                if ($squadre[$sq]['rebello'] && ($v === 'rebello' || $v === 'completa')) {
                    $infoSq .= ', ha il re bello';
                }
                if ($squadre[$sq]['napola'] > 0 && ($v === 'napola' || $v === 'completa')) {
                    $infoSq .= ', napola da ' . $squadre[$sq]['napola'] . ' punti';
                }
                $riepilogo[] = $infoSq;
            }
            return [
                'tipo' => 'scopa',
                'variante' => $v,
                'n' => $st['n'],
                'nomi' => $st['nomi'],
                'turno' => $st['turno'],
                'mazzo' => count($st['mazzo']),
                'tavolo_carte' => array_map(fn($id) => self::cartaDettaglio($id, $v), $st['tavolo']),
                'ultima' => $st['ultima'],
                'mano' => array_map(fn($id) => self::cartaDettaglio($id, $v), $posto >= 0 && isset($st['mani'][$posto]) ? $st['mani'][$posto] : []),
                'carte_in_mano' => array_map('count', $st['mani']),
                'squadre' => $squadre,
                'mia_squadra' => $mioSq,
                'finita' => $st['finita'],
                'partita' => $st['partita'],
                'punteggi' => $st['punteggi_mano'],
                'vincitori' => $st['vincitori'],
                'motivo' => $st['motivo'],
                'riepilogo' => $riepilogo,
                'testo_tavolo' => self::testoTavolo($st),
                'testo_punti' => self::testoPunti($st, $mioSq),
            ];
        } catch (Throwable $e) {
            registra_errore('Scopa::vista', $e);
            throw $e;
        }
    }

    private static function testoTavolo(array $st): string
    {
        try {
            $t = $st['tavolo'] ? 'Sul tavolo ci sono ' . count($st['tavolo']) . ' carte: ' . MazzoItaliano::elenca($st['tavolo']) . '. Mazzo: ' . count($st['mazzo']) . ' carte.' : 'Il tavolo è vuoto. Mazzo: ' . count($st['mazzo']) . ' carte.';
            $mani = [];
            foreach ($st['mani'] as $i => $m) {
                $n = count($m);
                $mani[] = $st['nomi'][$i] . ' ' . $n . ($n === 1 ? ' carta' : ' carte');
            }
            $t .= ' Carte in mano: ' . implode(', ', $mani) . '.';
            if (!empty($st['ultima'])) {
                $t .= ' Ultima giocata: ' . $st['ultima'];
            }
            return $t;
        } catch (Throwable $e) {
            registra_errore('Scopa::testoTavolo', $e);
            throw $e;
        }
    }

    private static function testoPunti(array $st, int $mioSq): string
    {
        try {
            $v = $st['variante'] ?? 'classica';
            $t = 'Mano ' . $st['partita']['mano'] . ', si gioca fino a ' . $st['partita']['obiettivo'] . ' punti. Totali: ' . self::nomiSquadra($st, 0) . ' ' . $st['partita']['totali'][0] . ', ' . self::nomiSquadra($st, 1) . ' ' . $st['partita']['totali'][1] . '. ';
            for ($sq = 0; $sq < 2; $sq++) {
                $denari = count(array_filter($st['prese'][$sq], fn($x) => MazzoItaliano::seme($x) === 1));
                $t .= ($sq === $mioSq ? 'La tua squadra' : self::nomiSquadra($st, $sq)) . ' in questa mano: ' . count($st['prese'][$sq]) . ' carte, ' . $denari . ' denari, ' . $st['scope'][$sq] . ' scope'
                    . (in_array(self::SETTEBELLO, $st['prese'][$sq], true) ? ', il sette bello' : '');
                if (($v === 'rebello' || $v === 'completa') && in_array(self::REBELLO, $st['prese'][$sq], true)) {
                    $t .= ', il re bello';
                }
                $nap = ($v === 'napola' || $v === 'completa') ? self::calcolaNapola($st['prese'][$sq]) : 0;
                if ($nap > 0) {
                    $t .= ', napola da ' . $nap . ' punti';
                }
                $t .= '. ';
            }
            return trim($t);
        } catch (Throwable $e) {
            registra_errore('Scopa::testoPunti', $e);
            throw $e;
        }
    }

    public static function situazione(array $st, int $posto): string
    {
        try {
            return trim(self::testoTavolo($st) . ' Hai in mano ' . count($st['mani'][$posto]) . ' carte: ' . MazzoItaliano::elenca($st['mani'][$posto]) . '. ' . self::testoPunti($st, self::squadraDi($st, $posto)));
        } catch (Throwable $e) {
            registra_errore('Scopa::situazione', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ computer
    /**
     * Probabilità che un avversario faccia scopa se sul tavolo restano queste carte: ci riesce solo con una carta del valore uguale alla somma
     * (se la somma è fino a 10). Si contano le carte già viste (prese, tavolo, la propria mano) e si stima quante ne restano nelle mani degli altri.
     */
    private static function rischioScopa(array $st, int $posto, array $resta): float
    {
        try {
            if (!$resta) {
                return 0.0;
            }
            $somma = array_sum(array_map(fn($c) => MazzoItaliano::numero($c), $resta));
            if ($somma > 10) {
                return 0.0;
            }
            $viste = array_merge($st['tavolo'], $st['mani'][$posto], ...$st['prese']);
            $ignote = 40 - count(array_unique($viste));
            $uguali = 0;
            foreach (array_unique($viste) as $c) {
                if (MazzoItaliano::numero($c) === $somma) {
                    $uguali++;
                }
            }
            $k = 4 - $uguali;                 // carte di quel valore che possono ancora essere in mano agli altri
            $h = 0;                            // carte in mano agli avversari (nel gioco a coppie il compagno non conta)
            $coppie = count($st['mani']) === 4;
            foreach ($st['mani'] as $i => $m) {
                if ($i !== $posto && !($coppie && $i % 2 === $posto % 2)) {
                    $h += count($m);
                }
            }
            if ($k <= 0 || $ignote <= 0 || $h <= 0) {
                return 0.0;
            }
            $h = min($h, $ignote);
            $senza = 1.0;
            for ($j = 0; $j < $h; $j++) {
                $senza *= max(0, $ignote - $k - $j) / ($ignote - $j);
            }
            return 1.0 - $senza;
        } catch (Throwable $e) {
            registra_errore('Scopa::rischioScopa', $e);
            throw $e;
        }
    }

    public static function bot(array $st, int $posto): string
    {
        try {
            $migliore = null;
            $punteggioMigliore = -INF;
            $v = $st['variante'] ?? 'classica';
            foreach (self::mosse($st, $posto) as $m) {
                $parti = explode('|', $m['id']);
                $carta = (int) $parti[1];
                $presa = $parti[2] === '-' ? [] : array_map('intval', explode(',', $parti[2]));
                $p = 0.0;
                if ($presa) {
                    $tutte = array_merge($presa, [$carta]);
                    $p += count($tutte) * 2;
                    foreach ($tutte as $c) {
                        if ($c === self::SETTEBELLO) {
                            $p += 40;
                        }
                        if ($c === self::REBELLO && ($v === 'rebello' || $v === 'completa')) {
                            $p += 30;
                        }
                        if (($v === 'napola' || $v === 'completa') && MazzoItaliano::seme($c) === 1 && ($c % 10 <= 2)) {
                            $p += 15; // carte chiave napola (asso, 2, 3 di denari)
                        }
                        if (MazzoItaliano::seme($c) === 1) {
                            $p += 4;
                        }
                        $p += self::valorePrimiera($c) / 5;
                    }
                    $resta = array_values(array_diff($st['tavolo'], $presa));
                    if (!$resta && !self::eUltimaGiocata($st, $carta)) {
                        $p += 100;
                    } elseif ($resta) {
                        $p -= 60 * self::rischioScopa($st, $posto, $resta);   // non si lascia al tavolo una scopa facile
                    }
                } else {
                    $p -= self::valorePrimiera($carta) / 4;
                    if ($carta === self::SETTEBELLO) {
                        $p -= 60;
                    }
                    if ($carta === self::REBELLO && ($v === 'rebello' || $v === 'completa')) {
                        $p -= 45;
                    }
                    if (MazzoItaliano::seme($carta) === 1) {
                        $p -= 6;
                    }
                    $p -= 60 * self::rischioScopa($st, $posto, array_merge($st['tavolo'], [$carta]));   // lascia una scopa: dipende da quante carte giuste restano
                }
                $p += mt_rand(0, 99) / 1000;
                if ($p > $punteggioMigliore) {
                    $punteggioMigliore = $p;
                    $migliore = $m['id'];
                }
            }
            return (string) $migliore;
        } catch (Throwable $e) {
            registra_errore('Scopa::bot', $e);
            throw $e;
        }
    }
}
