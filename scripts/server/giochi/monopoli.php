<?php
// Monopoli per 2-4 giocatori (anche contro il computer).
//
// Regole complete e varianti:
//  - Classica: regole ufficiali complete. 1500 euro iniziali, aste obbligatorie per proprietà rifiutate,
//    limite di 32 case e 12 alberghi della banca, riscatto ipoteche al 10% di interesse, prigione, scambi e 60 giri massimi.
//  - Parcheggio Ricco (Jackpot): tutte le tasse, multe di imprevisti e probabilità e i 50 euro per uscire di prigione
//    vengono versati nel piatto del Parcheggio Gratuito; chi vi atterra incassa l'intero montepremi.
//  - Doppio sul Via: chi atterra esattamente sulla casella Via incassa 400 euro anziché 200.
//  - Completo: combina Parcheggio Ricco e Doppio sul Via.
//  - Partita Veloce: 2 proprietà distribuite a caso all'inizio a testa, 3 case per albergo, termine al primo fallimento.

declare(strict_types=1);

final class Monopoli
{
    public const SOLDI_INIZIALI = 1500;
    public const GIRI_MASSIMI = 60; // giri di tavolo prima del conteggio finale
    public const MAX_CASE_BANCA = 32;
    public const MAX_ALBERGHI_BANCA = 12;

    public const VARIANTI = [
        'classica' => ['nome' => 'Classica', 'descrizione' => 'Regole standard complete con aste, ipoteche e 4 case per albergo.'],
        'parcheggio_ricco' => ['nome' => 'Parcheggio Ricco', 'descrizione' => 'Tutte le tasse e le uscite di prigione vanno nel piatto del Parcheggio Gratuito.'],
        'doppio_via' => ['nome' => 'Doppio al Via', 'descrizione' => 'Fermarsi con tiro esatto sulla casella del Via raddoppia il premio a 400 euro.'],
        'completo' => ['nome' => 'Regole complete', 'descrizione' => 'Combina Parcheggio Ricco e Doppio al Via.'],
        'veloce' => ['nome' => 'Partita Veloce', 'descrizione' => '2 proprietà iniziali a testa, 3 case per albergo, fine al primo fallimento.'],
    ];
    public const VARIANTE_INFO = self::VARIANTI;

    /** tipo: via, terreno, stazione, societa, probabilita, imprevisti, tassa, prigione, parcheggio, vai_prigione */
    public const CASELLE = [
        ['Via', 'via'],
        ['Vicolo Corto', 'terreno', 60, 'marrone', [2, 10, 30, 90, 160, 250], 50],
        ['Probabilità', 'probabilita'],
        ['Vicolo Stretto', 'terreno', 60, 'marrone', [4, 20, 60, 180, 320, 450], 50],
        ['Tassa patrimoniale', 'tassa', 200],
        ['Stazione Sud', 'stazione', 200],
        ['Bastioni Gran Sasso', 'terreno', 100, 'azzurro', [6, 30, 90, 270, 400, 550], 50],
        ['Imprevisti', 'imprevisti'],
        ['Viale Monterosa', 'terreno', 100, 'azzurro', [6, 30, 90, 270, 400, 550], 50],
        ['Viale Vesuvio', 'terreno', 120, 'azzurro', [8, 40, 100, 300, 450, 600], 50],
        ['Prigione, solo visita', 'prigione'],
        ['Via Accademia', 'terreno', 140, 'rosa', [10, 50, 150, 450, 625, 750], 100],
        ['Società Elettrica', 'societa', 150],
        ['Corso Ateneo', 'terreno', 140, 'rosa', [10, 50, 150, 450, 625, 750], 100],
        ['Piazza Università', 'terreno', 160, 'rosa', [12, 60, 180, 500, 700, 900], 100],
        ['Stazione Ovest', 'stazione', 200],
        ['Via Verdi', 'terreno', 180, 'arancione', [14, 70, 200, 550, 750, 950], 100],
        ['Probabilità', 'probabilita'],
        ['Corso Raffaello', 'terreno', 180, 'arancione', [14, 70, 200, 550, 750, 950], 100],
        ['Piazza Dante', 'terreno', 200, 'arancione', [16, 80, 220, 600, 800, 1000], 100],
        ['Parcheggio gratuito', 'parcheggio'],
        ['Via Marco Polo', 'terreno', 220, 'rosso', [18, 90, 250, 700, 875, 1050], 150],
        ['Imprevisti', 'imprevisti'],
        ['Corso Magellano', 'terreno', 220, 'rosso', [18, 90, 250, 700, 875, 1050], 150],
        ['Largo Colombo', 'terreno', 240, 'rosso', [20, 100, 300, 750, 925, 1100], 150],
        ['Stazione Nord', 'stazione', 200],
        ['Viale Costantino', 'terreno', 260, 'giallo', [22, 110, 330, 800, 975, 1150], 150],
        ['Viale Traiano', 'terreno', 260, 'giallo', [22, 110, 330, 800, 975, 1150], 150],
        ['Società Acqua Potabile', 'societa', 150],
        ['Piazza Giulio Cesare', 'terreno', 280, 'giallo', [24, 120, 360, 850, 1025, 1200], 150],
        ['Vai in prigione', 'vai_prigione'],
        ['Via Roma', 'terreno', 300, 'verde', [26, 130, 390, 900, 1100, 1275], 200],
        ['Corso Impero', 'terreno', 300, 'verde', [26, 130, 390, 900, 1100, 1275], 200],
        ['Probabilità', 'probabilita'],
        ['Largo Augusto', 'terreno', 320, 'verde', [28, 150, 450, 1000, 1200, 1400], 200],
        ['Stazione Est', 'stazione', 200],
        ['Imprevisti', 'imprevisti'],
        ['Viale dei Giardini', 'terreno', 350, 'blu', [35, 175, 500, 1100, 1300, 1500], 200],
        ['Tassa di lusso', 'tassa', 100],
        ['Parco della Vittoria', 'terreno', 400, 'blu', [50, 200, 600, 1400, 1700, 2000], 200],
    ];

    private const IMPREVISTI = [
        ['Avanza fino al Via e ritira 200 euro.', 'vai', 0],
        ['Vai in prigione direttamente, senza passare dal Via.', 'prigione'],
        ['Avanza fino a Largo Colombo. Se passi dal Via ritira 200 euro.', 'vai', 24],
        ['Avanza fino a Via Roma. Se passi dal Via ritira 200 euro.', 'vai', 31],
        ['Torna indietro di tre caselle.', 'indietro'],
        ['La banca ti paga un dividendo di 50 euro.', 'ricevi', 50],
        ['Esci gratis di prigione. Puoi tenere questa carta.', 'esci'],
        ['Paga la multa per eccesso di velocità: 15 euro.', 'paga', 15],
        ['Fai le riparazioni ai tuoi edifici: paga 25 euro per ogni casa e 100 euro per ogni albergo.', 'riparazioni', 25, 100],
        ['Sei stato eletto presidente del consiglio: paga 50 euro a ogni giocatore.', 'paga_tutti', 50],
        ['Il tuo prestito edilizio è maturato: ritira 150 euro.', 'ricevi', 150],
        ['Avanza fino alla Stazione Nord. Se passi dal Via ritira 200 euro.', 'vai', 25],
        ['Hai vinto un concorso di cruciverba: ritira 100 euro.', 'ricevi', 100],
        ['Paga la tassa scolastica: 150 euro.', 'paga', 150],
        ['Avanza fino a Bastioni Gran Sasso. Se passi dal Via ritira 200 euro.', 'vai', 6],
        ['Avanza fino al Parco della Vittoria.', 'vai', 39],
    ];

    private const PROBABILITA = [
        ['Errore della banca a tuo favore: ritira 200 euro.', 'ricevi', 200],
        ['Paga la visita dal dottore: 50 euro.', 'paga', 50],
        ['Dalla vendita di un\'azione ricavi 50 euro.', 'ricevi', 50],
        ['Esci gratis di prigione. Puoi tenere questa carta.', 'esci'],
        ['Vai in prigione direttamente, senza passare dal Via.', 'prigione'],
        ['È il tuo compleanno: ogni giocatore ti dà 10 euro.', 'compleanno', 10],
        ['Rimborso delle tasse: ritira 20 euro.', 'ricevi', 20],
        ['Ricevi il pagamento dell\'assicurazione sulla vita: ritira 100 euro.', 'ricevi', 100],
        ['Paga le spese d\'ospedale: 100 euro.', 'paga', 100],
        ['Paga le rette scolastiche: 50 euro.', 'paga', 50],
        ['Ricevi un compenso di consulenza: 25 euro.', 'ricevi', 25],
        ['Riparazioni stradali: paga 40 euro per ogni casa e 115 euro per ogni albergo.', 'riparazioni', 40, 115],
        ['Hai vinto il secondo premio in un concorso di bellezza: ritira 10 euro.', 'ricevi', 10],
        ['Hai ricevuto un\'eredità da un parente lontano: ritira 100 euro.', 'ricevi', 100],
        ['Avanza fino al Via e ritira 200 euro.', 'vai', 0],
        ['Il fondo di previdenza ti rende 100 euro: ritirali.', 'ricevi', 100],
    ];

    // ------------------------------------------------------------------ utilità
    private static function nomeCasella(int $i): string
    {
        try {
            return self::CASELLE[$i][0];
        } catch (Throwable $e) {
            registra_errore('Monopoli::nomeCasella', $e);
            throw $e;
        }
    }

    private static function eProprieta(int $i): bool
    {
        try {
            return in_array(self::CASELLE[$i][1], ['terreno', 'stazione', 'societa'], true);
        } catch (Throwable $e) {
            registra_errore('Monopoli::eProprieta', $e);
            throw $e;
        }
    }

    private static function prezzo(int $i): int
    {
        try {
            return (int) self::CASELLE[$i][2];
        } catch (Throwable $e) {
            registra_errore('Monopoli::prezzo', $e);
            throw $e;
        }
    }

    private static function gruppo(int $i): array
    {
        try {
            $g = self::CASELLE[$i][3];
            $r = [];
            foreach (self::CASELLE as $k => $c) {
                if ($c[1] === 'terreno' && $c[3] === $g) {
                    $r[] = $k;
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::gruppo', $e);
            throw $e;
        }
    }

    private static function euro(int $n): string
    {
        try {
            return $n . ' euro';
        } catch (Throwable $e) {
            registra_errore('Monopoli::euro', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ partita
    public static function nuova(array $nomi, string $variante = 'classica'): array
    {
        try {
            $n = count($nomi);
            if ($n < 2 || $n > 4) {
                throw new InvalidArgumentException('Il monopoli si gioca da 2 a 4.');
            }
            $variantiValide = ['classica', 'parcheggio_ricco', 'doppio_via', 'completo', 'veloce'];
            $variante = in_array($variante, $variantiValide, true) ? $variante : 'classica';

            $imp = range(0, count(self::IMPREVISTI) - 1);
            $prob = range(0, count(self::PROBABILITA) - 1);
            shuffle($imp);
            shuffle($prob);
            $proprieta = [];
            $indiciProp = [];
            foreach (self::CASELLE as $i => $c) {
                if (self::eProprieta($i)) {
                    $proprieta[$i] = ['di' => null, 'case' => 0, 'ipoteca' => false];
                    $indiciProp[] = $i;
                }
            }

            // Se partita veloce, distribuisce a sorte 2 proprietà gratuite a ciascun giocatore
            if ($variante === 'veloce') {
                shuffle($indiciProp);
                for ($p = 0; $p < $n; $p++) {
                    for ($k = 0; $k < 2; $k++) {
                        if ($indiciProp) {
                            $idx = array_shift($indiciProp);
                            $proprieta[$idx]['di'] = $p;
                        }
                    }
                }
            }

            return [
                'tipo' => 'monopoli',
                'variante' => $variante,
                'n' => $n,
                'nomi' => array_values($nomi),
                'soldi' => array_fill(0, $n, self::SOLDI_INIZIALI),
                'pos' => array_fill(0, $n, 0),
                'prigione' => array_fill(0, $n, -1), // -1 libero, altrimenti i tentativi già fatti
                'esci' => array_fill(0, $n, 0),
                'fuori' => array_fill(0, $n, false),
                'proprieta' => $proprieta,
                'parcheggio_piatto' => 0,
                'imprevisti' => $imp,
                'probabilita' => $prob,
                'turno' => 0,
                'fase' => 'tiro',
                'doppi' => 0,
                'dadi' => [0, 0],
                'giri' => 0,
                'scambio' => null,
                'asta' => null,
                'finita' => false,
                'vincitore' => null,
                'motivo' => '',
                'punteggi' => null,
            ];
        } catch (Throwable $e) {
            registra_errore('Monopoli::nuova', $e);
            throw $e;
        }
    }

    public static function inizio(array $st): array
    {
        try {
            $v = $st['variante'] ?? 'classica';
            $desc = 'Si gioca a monopoli';
            if ($v === 'parcheggio_ricco') {
                $desc .= ' con Parcheggio Ricco (multe e tasse nel piatto)';
            } elseif ($v === 'doppio_via') {
                $desc .= ' con Doppio sul Via (400 euro fermandosi sul Via)';
            } elseif ($v === 'completo') {
                $desc .= ' completo (Parcheggio Ricco e Doppio sul Via)';
            } elseif ($v === 'veloce') {
                $desc .= ' veloce (2 proprietà iniziali a testa, 3 case per albergo)';
            }
            $r = [['per' => null, 'testo' => $desc . '. Ognuno ha ' . self::euro(self::SOLDI_INIZIALI) . '. Inizia ' . $st['nomi'][0] . '.']];
            if ($v === 'veloce') {
                for ($p = 0; $p < $st['n']; $p++) {
                    $mie = [];
                    foreach ($st['proprieta'] as $i => $x) {
                        if ($x['di'] === $p) {
                            $mie[] = self::nomeCasella($i);
                        }
                    }
                    if ($mie) {
                        $r[] = ['per' => $p, 'testo' => 'Proprietà assegnate all\'inizio: ' . implode(' e ', $mie) . '.'];
                    }
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::inizio', $e);
            throw $e;
        }
    }

    private static function attivi(array $st): array
    {
        try {
            $r = [];
            for ($i = 0; $i < $st['n']; $i++) {
                if (!$st['fuori'][$i]) {
                    $r[] = $i;
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::attivi', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ patrimonio
    public static function patrimonio(array $st, int $p): int
    {
        try {
            $t = $st['soldi'][$p];
            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] === $p) {
                    $t += $x['ipoteca'] ? intdiv(self::prezzo($i), 2) : self::prezzo($i);
                    if (self::CASELLE[$i][1] === 'terreno') {
                        $t += $x['case'] * intdiv((int) self::CASELLE[$i][5], 2);
                    }
                }
            }
            return $t;
        } catch (Throwable $e) {
            registra_errore('Monopoli::patrimonio', $e);
            throw $e;
        }
    }

    private static function possiedeGruppo(array $st, int $p, int $i): bool
    {
        try {
            foreach (self::gruppo($i) as $k) {
                if ($st['proprieta'][$k]['di'] !== $p) {
                    return false;
                }
            }
            return true;
        } catch (Throwable $e) {
            registra_errore('Monopoli::possiedeGruppo', $e);
            throw $e;
        }
    }

    private static function gruppoSenzaIpoteche(array $st, int $i): bool
    {
        try {
            foreach (self::gruppo($i) as $k) {
                if ($st['proprieta'][$k]['ipoteca']) {
                    return false;
                }
            }
            return true;
        } catch (Throwable $e) {
            registra_errore('Monopoli::gruppoSenzaIpoteche', $e);
            throw $e;
        }
    }

    private static function caseNelGruppo(array $st, int $i): int
    {
        try {
            $t = 0;
            foreach (self::gruppo($i) as $k) {
                $t += $st['proprieta'][$k]['case'];
            }
            return $t;
        } catch (Throwable $e) {
            registra_errore('Monopoli::caseNelGruppo', $e);
            throw $e;
        }
    }

    private static function affitto(array $st, int $i, int $dadi): int
    {
        try {
            $x = $st['proprieta'][$i];
            if ($x['ipoteca'] || $x['di'] === null) {
                return 0;
            }
            $tipo = self::CASELLE[$i][1];
            if ($tipo === 'stazione') {
                $q = 0;
                foreach ($st['proprieta'] as $k => $y) {
                    if (self::CASELLE[$k][1] === 'stazione' && $y['di'] === $x['di']) {
                        $q++;
                    }
                }
                return 25 * (2 ** ($q - 1));
            }
            if ($tipo === 'societa') {
                $q = 0;
                foreach ($st['proprieta'] as $k => $y) {
                    if (self::CASELLE[$k][1] === 'societa' && $y['di'] === $x['di']) {
                        $q++;
                    }
                }
                return $dadi * ($q >= 2 ? 10 : 4);
            }
            $base = self::CASELLE[$i][4][$x['case']];
            if ($x['case'] === 0 && self::possiedeGruppo($st, (int) $x['di'], $i)) {
                return $base * 2;
            }
            return $base;
        } catch (Throwable $e) {
            registra_errore('Monopoli::affitto', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ pagamenti
    /** Vende edifici e ipoteca finché si arriva alla somma. Restituisce la somma di cui si può disporre in tutto. */
    private static function liquidabile(array $st, int $p): int
    {
        try {
            $t = $st['soldi'][$p];
            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] === $p) {
                    if (self::CASELLE[$i][1] === 'terreno') {
                        $t += $x['case'] * intdiv((int) self::CASELLE[$i][5], 2);
                    }
                    if (!$x['ipoteca']) {
                        $t += intdiv(self::prezzo($i), 2);
                    }
                }
            }
            return $t;
        } catch (Throwable $e) {
            registra_errore('Monopoli::liquidabile', $e);
            throw $e;
        }
    }

    private static function reperisci(array &$st, int $p, int $somma, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            // prima gli edifici, dalle proprietà con più case
            while ($st['soldi'][$p] < $somma) {
                $scelta = null;
                $max = 0;
                foreach ($st['proprieta'] as $i => $x) {
                    if ($x['di'] === $p && $x['case'] > $max && self::CASELLE[$i][1] === 'terreno') {
                        // si vende dalla proprietà del gruppo con più case, per restare uniformi
                        $maxGruppo = 0;
                        foreach (self::gruppo($i) as $k) {
                            $maxGruppo = max($maxGruppo, $st['proprieta'][$k]['case']);
                        }
                        if ($x['case'] === $maxGruppo) {
                            $max = $x['case'];
                            $scelta = $i;
                        }
                    }
                }
                if ($scelta === null) {
                    break;
                }
                $st['proprieta'][$scelta]['case']--;
                $st['soldi'][$p] += intdiv((int) self::CASELLE[$scelta][5], 2);
                $eventi[] = ['per' => null, 'testo' => $nome . ' vende un edificio a ' . self::nomeCasella($scelta) . ' per ' . self::euro(intdiv((int) self::CASELLE[$scelta][5], 2)) . '.'];
            }
            // poi le ipoteche
            foreach ($st['proprieta'] as $i => $x) {
                if ($st['soldi'][$p] >= $somma) {
                    break;
                }
                if ($x['di'] === $p && !$x['ipoteca'] && $x['case'] === 0) {
                    $st['proprieta'][$i]['ipoteca'] = true;
                    $st['soldi'][$p] += intdiv(self::prezzo($i), 2);
                    $eventi[] = ['per' => null, 'testo' => $nome . ' ipoteca ' . self::nomeCasella($i) . ' e incassa ' . self::euro(intdiv(self::prezzo($i), 2)) . '.'];
                }
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::reperisci', $e);
            throw $e;
        }
    }

    /** Paga $somma da $da a $a (null = banca). Restituisce false se $da è fallito. */
    private static function paga(array &$st, int $da, ?int $a, int $somma, array &$eventi): bool
    {
        try {
            if ($somma <= 0) {
                return true;
            }
            if ($st['soldi'][$da] < $somma && self::liquidabile($st, $da) >= $somma) {
                self::reperisci($st, $da, $somma, $eventi);
            }
            if ($st['soldi'][$da] >= $somma) {
                $st['soldi'][$da] -= $somma;
                if ($a !== null) {
                    $st['soldi'][$a] += $somma;
                } else {
                    $v = $st['variante'] ?? 'classica';
                    if ($v === 'parcheggio_ricco' || $v === 'completo') {
                        $st['parcheggio_piatto'] = ($st['parcheggio_piatto'] ?? 0) + $somma;
                    }
                }
                return true;
            }
            self::fallimento($st, $da, $a, $eventi);
            return false;
        } catch (Throwable $e) {
            registra_errore('Monopoli::paga', $e);
            throw $e;
        }
    }

    private static function fallimento(array &$st, int $p, ?int $creditore, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            $eventi[] = ['per' => null, 'testo' => $nome . ' non riesce a pagare ed è eliminato' . ($creditore !== null ? ': tutto quello che possiede passa a ' . $st['nomi'][$creditore] : ': le sue proprietà tornano alla banca') . '.'];
            if ($creditore !== null) {
                $st['soldi'][$creditore] += max(0, $st['soldi'][$p]);
                $st['esci'][$creditore] += $st['esci'][$p];
            }
            $st['soldi'][$p] = 0;
            $st['esci'][$p] = 0;
            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] === $p) {
                    if ($creditore !== null) {
                        $st['proprieta'][$i]['di'] = $creditore;
                        $st['proprieta'][$i]['case'] = 0;
                    } else {
                        $st['proprieta'][$i] = ['di' => null, 'case' => 0, 'ipoteca' => false];
                    }
                }
            }
            $st['fuori'][$p] = true;
            $st['prigione'][$p] = -1;
            $st['scambio'] = null;
            $st['asta'] = null;

            // Nella partita veloce, la partita termina al primo fallimento
            if (($st['variante'] ?? '') === 'veloce') {
                $attivi = self::attivi($st);
                if ($attivi) {
                    usort($attivi, fn($a, $b) => self::patrimonio($st, $b) <=> self::patrimonio($st, $a));
                    $vincitore = $attivi[0];
                    self::terminaConVincitore($st, $vincitore, $eventi, 'Partita veloce conclusa con il fallimento di ' . $nome . '. Vince ' . $st['nomi'][$vincitore] . ' con un patrimonio di ' . self::euro(self::patrimonio($st, $vincitore)) . '.');
                    return;
                }
            }

            $vivi = self::attivi($st);
            if (count($vivi) === 1) {
                self::terminaConVincitore($st, $vivi[0], $eventi, $nome . ' era l\'ultimo avversario.');
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::fallimento', $e);
            throw $e;
        }
    }

    private static function terminaConVincitore(array &$st, int $v, array &$eventi, string $motivo): void
    {
        try {
            $st['finita'] = true;
            $st['vincitore'] = $v;
            $st['motivo'] = $motivo;
            $punti = [];
            for ($i = 0; $i < $st['n']; $i++) {
                $punti[$i] = $st['fuori'][$i] ? 0 : self::patrimonio($st, $i);
            }
            $st['punteggi'] = $punti;
            $eventi[] = ['per' => null, 'testo' => 'La partita è finita: vince ' . $st['nomi'][$v] . ' con un patrimonio di ' . self::euro($punti[$v]) . '. ' . $motivo];
        } catch (Throwable $e) {
            registra_errore('Monopoli::terminaConVincitore', $e);
            throw $e;
        }
    }

    /** Chi deve agire adesso: chi risponde a uno scambio, chi deve fare un'offerta all'asta, altrimenti chi ha il turno. */
    public static function attore(array $st): int
    {
        try {
            if (($st['scambio'] ?? null) !== null) {
                return (int) $st['scambio']['a'];
            }
            if (($st['asta'] ?? null) !== null) {
                return (int) $st['asta']['partecipanti'][$st['asta']['indice']];
            }
            return (int) $st['turno'];
        } catch (Throwable $e) {
            registra_errore('Monopoli::attore', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ mosse
    /** Le mosse del turno e, per chi ha ricevuto una proposta, la risposta. Ipoteche, case e scambi stanno nelle opzioni. */
    public static function mosse(array $st, int $p): array
    {
        try {
            if ($st['finita'] || $st['fuori'][$p]) {
                return [];
            }
            if ($st['scambio'] !== null) {
                if ($st['scambio']['a'] === $p) {
                    $t = $st['scambio'];
                    return [
                        ['id' => 'accetta_scambio', 'testo' => 'Accetta lo scambio: ' . self::descriviScambio($st, $t)],
                        ['id' => 'rifiuta_scambio', 'testo' => 'Rifiuta lo scambio proposto da ' . $st['nomi'][$t['da']]],
                    ];
                }
                return [];
            }
            if (($st['asta'] ?? null) !== null) {
                if (self::attore($st) !== $p) {
                    return [];
                }
                $a = $st['asta'];
                $min = $a['offerta'] > 0 ? $a['offerta'] + 10 : 10;
                $m = [];
                if ($st['soldi'][$p] >= $min) {
                    $m[] = ['id' => 'offri', 'testo' => 'Offri un prezzo più alto' . ($a['offerta'] > 0 ? ' di ' . self::euro($a['offerta']) : '') . ', almeno ' . self::euro($min),
                        'parametro' => ['tipo' => 'numero', 'min' => $min, 'max' => $st['soldi'][$p], 'domanda' => 'Quanto offri per ' . self::nomeCasella($a['casella']) . '? Da ' . $min . ' a ' . $st['soldi'][$p] . ' euro']];
                }
                $m[] = ['id' => 'ritirati_asta', 'testo' => 'Ritirati dall\'asta'];
                return $m;
            }
            if ($p !== $st['turno']) {
                return [];
            }
            $m = [];
            $fase = $st['fase'];
            if ($fase === 'acquisto') {
                $i = $st['pos'][$p];
                if ($st['soldi'][$p] >= self::prezzo($i)) {
                    $m[] = ['id' => 'compra', 'testo' => 'Compra ' . self::nomeCasella($i) . ' per ' . self::euro(self::prezzo($i))];
                }
                $m[] = ['id' => 'passa', 'testo' => 'Non comprare ' . self::nomeCasella($i) . ' e fai l\'asta'];
                return $m;
            }
            if ($fase === 'tiro') {
                if ($st['prigione'][$p] >= 0) {
                    if ($st['soldi'][$p] >= 50) {
                        $m[] = ['id' => 'paga_prigione', 'testo' => 'Paga 50 euro per uscire di prigione'];
                    }
                    if ($st['esci'][$p] > 0) {
                        $m[] = ['id' => 'usa_carta', 'testo' => 'Usa la carta "Esci gratis di prigione"'];
                    }
                    $m[] = ['id' => 'tira', 'testo' => 'Tira i dadi per fare un doppio (tentativo ' . ($st['prigione'][$p] + 1) . ' di 3)'];
                } else {
                    $m[] = ['id' => 'tira', 'testo' => $st['doppi'] > 0 ? 'Tira ancora i dadi' : 'Tira i dadi'];
                }
            }
            // Nella fase "fine" non c'è più niente da offrire: il turno passa da solo (vedi chiudiTurnoSeFinito).
            return $m;
        } catch (Throwable $e) {
            registra_errore('Monopoli::mosse', $e);
            throw $e;
        }
    }

    /**
     * Le opzioni del turno, in gruppi: [['id', 'testo', 'voci' => [['id','testo'],...]], ...] più, se si può, il gruppo 'scambio'
     * con i dati per comporre la proposta (giocatori con le loro proprietà, le proprie proprietà, i soldi).
     */
    public static function opzioni(array $st, int $p): array
    {
        try {
            if ($st['finita'] || $st['fuori'][$p] || $st['scambio'] !== null || ($st['asta'] ?? null) !== null || $p !== $st['turno'] || !in_array($st['fase'], ['tiro', 'fine'], true)) {
                return [];
            }
            $gruppi = [
                'ipoteca' => ['Metti in ipoteca una proprietà', []],
                'riscatta' => ['Riscatta un\'ipoteca', []],
                'costruisci' => ['Costruisci case o alberghi', []],
                'vendi' => ['Vendi case o alberghi', []],
            ];
            foreach (self::mosseGestione($st, $p) as $x) {
                $tipo = explode('|', $x['id'])[0];
                if (isset($gruppi[$tipo])) {
                    $gruppi[$tipo][1][] = $x;
                }
            }
            $r = [];
            foreach ($gruppi as $id => [$testo, $voci]) {
                if ($voci) {
                    $r[] = ['id' => $id, 'testo' => $testo, 'voci' => $voci];
                }
            }
            $scambio = self::datiScambio($st, $p);
            if ($scambio !== null) {
                $r[] = ['id' => 'scambio', 'testo' => 'Proponi uno scambio a un altro giocatore', 'voci' => [], 'scambio' => $scambio];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::opzioni', $e);
            throw $e;
        }
    }

    /** Le proprietà che si possono scambiare: di quel giocatore, senza ipoteca e senza edifici nel gruppo. */
    private static function scambiabili(array $st, int $q): array
    {
        try {
            $r = [];
            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] !== $q || $x['ipoteca']) {
                    continue;
                }
                if (self::CASELLE[$i][1] === 'terreno' && self::caseNelGruppo($st, $i) > 0) {
                    continue;
                }
                $r[] = ['id' => $i, 'nome' => self::nomeCasella($i), 'prezzo' => self::prezzo($i)];
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::scambiabili', $e);
            throw $e;
        }
    }

    private static function datiScambio(array $st, int $p): ?array
    {
        try {
            $altri = [];
            foreach (self::attivi($st) as $q) {
                if ($q !== $p) {
                    $altri[] = ['indice' => $q, 'nome' => $st['nomi'][$q], 'soldi' => $st['soldi'][$q], 'proprieta' => self::scambiabili($st, $q)];
                }
            }
            if (!$altri) {
                return null;
            }
            return ['giocatori' => $altri, 'mie' => self::scambiabili($st, $p), 'miei_soldi' => $st['soldi'][$p]];
        } catch (Throwable $e) {
            registra_errore('Monopoli::datiScambio', $e);
            throw $e;
        }
    }

    /** Tutte le mosse valide, comprese quelle delle opzioni (per controllare quello che arriva dal client). */
    private static function mossePossibili(array $st, int $p): array
    {
        try {
            $r = self::mosse($st, $p);
            foreach (self::opzioni($st, $p) as $g) {
                foreach ($g['voci'] as $v) {
                    $r[] = $v;
                }
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::mossePossibili', $e);
            throw $e;
        }
    }

    private static function elencoProprieta(array $ids): string
    {
        try {
            return $ids ? implode(', ', array_map(fn($i) => self::nomeCasella($i) . (self::CASELLE[$i][1] === 'terreno' ? ' (' . self::CASELLE[$i][3] . ')' : ''), $ids)) : 'nessuna proprietà';
        } catch (Throwable $e) {
            registra_errore('Monopoli::elencoProprieta', $e);
            throw $e;
        }
    }

    private static function descriviScambio(array $st, array $t): string
    {
        try {
            $dai = [];
            if ($t['dai_prop']) {
                $dai[] = self::elencoProprieta($t['dai_prop']);
            }
            if ($t['dai_soldi'] > 0) {
                $dai[] = self::euro($t['dai_soldi']);
            }
            $chiedi = [];
            if ($t['chiedi_prop']) {
                $chiedi[] = self::elencoProprieta($t['chiedi_prop']);
            }
            if ($t['chiedi_soldi'] > 0) {
                $chiedi[] = self::euro($t['chiedi_soldi']);
            }
            return $st['nomi'][$t['da']] . ' ti dà ' . ($dai ? implode(' e ', $dai) : 'niente') . ' e chiede ' . ($chiedi ? implode(' e ', $chiedi) : 'niente');
        } catch (Throwable $e) {
            registra_errore('Monopoli::descriviScambio', $e);
            throw $e;
        }
    }

    /** Legge e controlla "scambio|verso|prop,prop|soldi|prop,prop|soldi". Restituisce lo scambio oppure null. */
    private static function leggiScambio(array $st, int $p, string $id): ?array
    {
        try {
            $parti = explode('|', $id);
            if (count($parti) !== 6 || $parti[0] !== 'scambio') {
                return null;
            }
            $a = (int) $parti[1];
            if ($a === $p || $a < 0 || $a >= $st['n'] || $st['fuori'][$a]) {
                return null;
            }
            $lista = function (string $t): array {
                if ($t === '-' || $t === '') {
                    return [];
                }
                return array_values(array_unique(array_map('intval', explode(',', $t))));
            };
            $dai = $lista($parti[2]);
            $chiedi = $lista($parti[4]);
            $daiSoldi = (int) $parti[3];
            $chiediSoldi = (int) $parti[5];
            if ($daiSoldi < 0 || $chiediSoldi < 0 || $daiSoldi > $st['soldi'][$p] || $chiediSoldi > $st['soldi'][$a]) {
                return null;
            }
            if (!$dai && !$chiedi && $daiSoldi === 0 && $chiediSoldi === 0) {
                return null;
            }
            $mie = array_column(self::scambiabili($st, $p), 'id');
            $sue = array_column(self::scambiabili($st, $a), 'id');
            foreach ($dai as $i) {
                if (!in_array($i, $mie, true)) {
                    return null;
                }
            }
            foreach ($chiedi as $i) {
                if (!in_array($i, $sue, true)) {
                    return null;
                }
            }
            return ['da' => $p, 'a' => $a, 'dai_prop' => $dai, 'dai_soldi' => $daiSoldi, 'chiedi_prop' => $chiedi, 'chiedi_soldi' => $chiediSoldi];
        } catch (Throwable $e) {
            registra_errore('Monopoli::leggiScambio', $e);
            throw $e;
        }
    }

    /** Quanto vale per $q avere (o perdere) queste proprietà: il prezzo, più un premio se completa un colore e meno se ne rompe uno. */
    private static function valoreScambio(array $st, int $q, array $prendi, array $dai): int
    {
        try {
            $v = 0;
            foreach ($prendi as $i) {
                $v += self::prezzo($i);
                if (self::CASELLE[$i][1] === 'terreno') {
                    $mancanti = 0;
                    foreach (self::gruppo($i) as $k) {
                        if ($st['proprieta'][$k]['di'] !== $q && !in_array($k, $prendi, true)) {
                            $mancanti++;
                        }
                    }
                    if ($mancanti === 0) {
                        $v += 200;
                    }
                }
            }
            foreach ($dai as $i) {
                $v -= self::prezzo($i);
                if (self::CASELLE[$i][1] === 'terreno' && self::possiedeGruppo($st, $q, $i)) {
                    $v -= 200;
                }
            }
            return $v;
        } catch (Throwable $e) {
            registra_errore('Monopoli::valoreScambio', $e);
            throw $e;
        }
    }

    /** Il computer accetta se ci guadagna almeno un po' e non permette a chi propone di completare un colore (salvo un grosso guadagno). */
    private static function valutaScambioComputer(array $st, array $t): bool
    {
        try {
            $b = $t['a'];
            $guadagno = self::valoreScambio($st, $b, $t['dai_prop'], $t['chiedi_prop']) + $t['dai_soldi'] - $t['chiedi_soldi'];
            $ceduto = $t['chiedi_soldi'];
            foreach ($t['chiedi_prop'] as $i) {
                $ceduto += self::prezzo($i);
            }
            $minimo = max(20, (int) ($ceduto * 0.1));
            // chi propone completerebbe un colore con le proprietà del computer?
            $completa = false;
            foreach ($t['chiedi_prop'] as $i) {
                if (self::CASELLE[$i][1] !== 'terreno') {
                    continue;
                }
                $tutte = true;
                foreach (self::gruppo($i) as $k) {
                    $suo = ($st['proprieta'][$k]['di'] === $t['da'] && !in_array($k, $t['dai_prop'], true)) || in_array($k, $t['chiedi_prop'], true);
                    if (!$suo) {
                        $tutte = false;
                        break;
                    }
                }
                if ($tutte) {
                    $completa = true;
                }
            }
            if ($completa && $guadagno < 300) {
                return false;
            }
            return $guadagno >= $minimo;
        } catch (Throwable $e) {
            registra_errore('Monopoli::valutaScambioComputer', $e);
            throw $e;
        }
    }

    private static function concludiScambio(array &$st, array $t, array &$eventi): void
    {
        try {
            foreach ($t['dai_prop'] as $i) {
                $st['proprieta'][$i]['di'] = $t['a'];
            }
            foreach ($t['chiedi_prop'] as $i) {
                $st['proprieta'][$i]['di'] = $t['da'];
            }
            $st['soldi'][$t['da']] += $t['chiedi_soldi'] - $t['dai_soldi'];
            $st['soldi'][$t['a']] += $t['dai_soldi'] - $t['chiedi_soldi'];
            $eventi[] = ['per' => null, 'testo' => $st['nomi'][$t['a']] . ' accetta lo scambio con ' . $st['nomi'][$t['da']] . ': le proprietà e i soldi cambiano di mano.'];
            $st['scambio'] = null;
        } catch (Throwable $e) {
            registra_errore('Monopoli::concludiScambio', $e);
            throw $e;
        }
    }

    private static function mosseGestione(array $st, int $p): array
    {
        try {
            $m = [];
            $v = $st['variante'] ?? 'classica';
            $sogliaAlbergo = $v === 'veloce' ? 3 : 4;
            $maxEdifici = $v === 'veloce' ? 4 : 5;

            // Limite edifici della banca (32 case, 12 alberghi)
            $caseTotali = 0;
            $alberghiTotali = 0;
            foreach ($st['proprieta'] as $pr) {
                if ($pr['case'] === 5 || ($v === 'veloce' && $pr['case'] === 4)) {
                    $alberghiTotali++;
                } elseif ($pr['case'] > 0) {
                    $caseTotali += $pr['case'];
                }
            }

            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] !== $p) {
                    continue;
                }
                $nome = self::nomeCasella($i);
                if (self::CASELLE[$i][1] === 'terreno') {
                    $costo = (int) self::CASELLE[$i][5];
                    $gr = self::gruppo($i);
                    $minCase = min(array_map(fn($k) => $st['proprieta'][$k]['case'], $gr));
                    $maxCase = max(array_map(fn($k) => $st['proprieta'][$k]['case'], $gr));
                    if (self::possiedeGruppo($st, $p, $i) && self::gruppoSenzaIpoteche($st, $i) && $x['case'] < $maxEdifici && $x['case'] === $minCase && $st['soldi'][$p] >= $costo) {
                        $puoCostruire = ($x['case'] === $sogliaAlbergo) ? ($alberghiTotali < self::MAX_ALBERGHI_BANCA) : ($caseTotali < self::MAX_CASE_BANCA);
                        if ($puoCostruire) {
                            $m[] = ['id' => 'costruisci|' . $i, 'testo' => ($x['case'] === $sogliaAlbergo ? 'Costruisci un albergo a ' : 'Costruisci una casa a ') . $nome . ' per ' . self::euro($costo)];
                        }
                    }
                    if ($x['case'] > 0 && $x['case'] === $maxCase) {
                        $m[] = ['id' => 'vendi|' . $i, 'testo' => 'Vendi ' . ($x['case'] === $maxEdifici ? 'l\'albergo di ' : 'una casa di ') . $nome . ' per ' . self::euro(intdiv($costo, 2))];
                    }
                }
                if (!$x['ipoteca'] && $x['case'] === 0 && (self::CASELLE[$i][1] !== 'terreno' || self::caseNelGruppo($st, $i) === 0)) {
                    $m[] = ['id' => 'ipoteca|' . $i, 'testo' => 'Ipoteca ' . $nome . ' e incassa ' . self::euro(intdiv(self::prezzo($i), 2))];
                }
                if ($x['ipoteca']) {
                    $costo = (int) ceil(self::prezzo($i) / 2 * 1.1);
                    if ($st['soldi'][$p] >= $costo) {
                        $m[] = ['id' => 'riscatta|' . $i, 'testo' => 'Riscatta l\'ipoteca di ' . $nome . ' per ' . self::euro($costo)];
                    }
                }
            }
            return $m;
        } catch (Throwable $e) {
            registra_errore('Monopoli::mosseGestione', $e);
            throw $e;
        }
    }

    /** Case e alberghi in gioco (nella partita veloce l'albergo è il quarto edificio). */
    private static function edificiInGioco(array $st): array
    {
        try {
            $v = $st['variante'] ?? 'classica';
            $case = 0;
            $alberghi = 0;
            foreach ($st['proprieta'] as $pr) {
                if ($pr['case'] === 5 || ($v === 'veloce' && $pr['case'] === 4)) {
                    $alberghi++;
                } elseif ($pr['case'] > 0) {
                    $case += $pr['case'];
                }
            }
            return [$case, $alberghi];
        } catch (Throwable $e) {
            registra_errore('Monopoli::edificiInGioco', $e);
            throw $e;
        }
    }

    private static function caseDisponibili(array $st): int
    {
        try {
            return max(0, self::MAX_CASE_BANCA - self::edificiInGioco($st)[0]);
        } catch (Throwable $e) {
            registra_errore('Monopoli::caseDisponibili', $e);
            throw $e;
        }
    }

    private static function alberghiDisponibili(array $st): int
    {
        try {
            return max(0, self::MAX_ALBERGHI_BANCA - self::edificiInGioco($st)[1]);
        } catch (Throwable $e) {
            registra_errore('Monopoli::alberghiDisponibili', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ applicazione
    public static function applica(array &$st, int $p, string $idMossa, bool $verifica = true): array
    {
        try {
            if ($verifica) {
                $ok = false;
                foreach (self::mossePossibili($st, $p) as $x) {
                    if ($x['id'] === $idMossa) {
                        $ok = true;
                        break;
                    }
                }
                // lo scambio e l'offerta hanno parametri liberi: si controllano a parte
                if (!$ok && str_starts_with($idMossa, 'scambio|') && self::opzioniScambioPossibile($st, $p) && self::leggiScambio($st, $p, $idMossa) !== null) {
                    $ok = true;
                }
                if (!$ok && str_starts_with($idMossa, 'offri|') && self::leggiOfferta($st, $p, $idMossa) !== null) {
                    $ok = true;
                }
                // più proprietà insieme (ipoteca|1,5,9): si controllano una alla volta nella mossa stessa
                if (!$ok && preg_match('/^(ipoteca|riscatta|costruisci|vendi)\|\d+(,\d+)+$/', $idMossa) && self::opzioni($st, $p)) {
                    $ok = true;
                }
                if (!$ok) {
                    throw new InvalidArgumentException('Mossa non consentita.');
                }
            }
            $eventi = [];
            $nome = $st['nomi'][$p];
            $parti = explode('|', $idMossa);
            // più proprietà insieme: si applicano in ordine, su una copia, e solo se vanno bene tutte
            if (preg_match('/^(ipoteca|riscatta|costruisci|vendi)\|\d+(,\d+)+$/', $idMossa)) {
                $copia = $st;
                foreach (explode(',', $parti[1]) as $i) {
                    $singola = $parti[0] . '|' . (int) $i;
                    $valida = false;
                    foreach (self::mosseGestione($copia, $p) as $x) {
                        if ($x['id'] === $singola) {
                            $valida = true;
                            break;
                        }
                    }
                    if (!$valida) {
                        throw new InvalidArgumentException('Una delle proprietà scelte non va bene.');
                    }
                    foreach (self::applica($copia, $p, $singola, false) as $e) {
                        $eventi[] = $e;
                    }
                }
                $st = $copia;
                return $eventi;
            }
            switch ($parti[0]) {
                case 'tira':
                    self::tira($st, $p, $eventi);
                    break;
                case 'paga_prigione':
                    self::paga($st, $p, null, 50, $eventi);
                    $st['prigione'][$p] = -1;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' paga 50 euro ed esce di prigione.'];
                    break;
                case 'usa_carta':
                    $st['esci'][$p]--;
                    $st['prigione'][$p] = -1;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' usa la carta "Esci gratis di prigione" ed esce di prigione.'];
                    break;
                case 'compra':
                    $i = $st['pos'][$p];
                    $st['soldi'][$p] -= self::prezzo($i);
                    $st['proprieta'][$i]['di'] = $p;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' compra ' . self::nomeCasella($i) . ' per ' . self::euro(self::prezzo($i)) . '.'];
                    self::dopoAzione($st, $p, $eventi);
                    break;
                case 'passa':
                    $eventi[] = ['per' => null, 'testo' => $nome . ' non compra ' . self::nomeCasella($st['pos'][$p]) . '.'];
                    self::avviaAsta($st, $p, $st['pos'][$p], $eventi);
                    break;
                case 'offri':
                    $importo = self::leggiOfferta($st, $p, $idMossa);
                    if ($importo === null) {
                        throw new InvalidArgumentException('Offerta non valida.');
                    }
                    $st['asta']['offerta'] = $importo;
                    $st['asta']['leader'] = $p;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' offre ' . self::euro($importo) . ' per ' . self::nomeCasella($st['asta']['casella']) . '.'];
                    $st['asta']['indice'] = ($st['asta']['indice'] + 1) % count($st['asta']['partecipanti']);
                    self::controllaAsta($st, $eventi);
                    break;
                case 'ritirati_asta':
                    $eventi[] = ['per' => null, 'testo' => $nome . ' si ritira dall\'asta.'];
                    array_splice($st['asta']['partecipanti'], $st['asta']['indice'], 1);
                    if (count($st['asta']['partecipanti']) > 0) {
                        $st['asta']['indice'] = $st['asta']['indice'] % count($st['asta']['partecipanti']);
                    }
                    self::controllaAsta($st, $eventi);
                    break;
                case 'costruisci':
                    $i = (int) $parti[1];
                    $costo = (int) self::CASELLE[$i][5];
                    $st['soldi'][$p] -= $costo;
                    $st['proprieta'][$i]['case']++;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' costruisce ' . ($st['proprieta'][$i]['case'] === 5 ? 'un albergo' : 'una casa') . ' a ' . self::nomeCasella($i) . ' per ' . self::euro($costo) . '.'];
                    break;
                case 'vendi':
                    $i = (int) $parti[1];
                    $st['proprieta'][$i]['case']--;
                    $st['soldi'][$p] += intdiv((int) self::CASELLE[$i][5], 2);
                    $eventi[] = ['per' => null, 'testo' => $nome . ' vende un edificio a ' . self::nomeCasella($i) . '.'];
                    break;
                case 'ipoteca':
                    $i = (int) $parti[1];
                    $st['proprieta'][$i]['ipoteca'] = true;
                    $st['soldi'][$p] += intdiv(self::prezzo($i), 2);
                    $eventi[] = ['per' => null, 'testo' => $nome . ' ipoteca ' . self::nomeCasella($i) . '.'];
                    break;
                case 'riscatta':
                    $i = (int) $parti[1];
                    $st['proprieta'][$i]['ipoteca'] = false;
                    $st['soldi'][$p] -= (int) ceil(self::prezzo($i) / 2 * 1.1);
                    $eventi[] = ['per' => null, 'testo' => $nome . ' riscatta l\'ipoteca di ' . self::nomeCasella($i) . '.'];
                    break;
                case 'scambio':
                    $t = self::leggiScambio($st, $p, $idMossa);
                    if ($t === null) {
                        throw new InvalidArgumentException('Scambio non valido.');
                    }
                    $st['scambio'] = $t;
                    $st['ultimo_scambio_bot'][$p] = (int) ($st['giri'] ?? 0);   // il computer non ripropone subito (vedi scambioBot)
                    $eventi[] = ['per' => null, 'testo' => $nome . ' propone a ' . $st['nomi'][$t['a']] . ' uno scambio: dà ' . ($t['dai_prop'] ? self::elencoProprieta($t['dai_prop']) : 'nessuna proprietà') . ' e ' . self::euro($t['dai_soldi']) . ', chiede ' . ($t['chiedi_prop'] ? self::elencoProprieta($t['chiedi_prop']) : 'nessuna proprietà') . ' e ' . self::euro($t['chiedi_soldi']) . '.'];
                    // il computer risponde subito
                    if (self::eComputer($st, $t['a'])) {
                        if (self::valutaScambioComputer($st, $t)) {
                            self::concludiScambio($st, $t, $eventi);
                        } else {
                            $eventi[] = ['per' => null, 'testo' => $st['nomi'][$t['a']] . ' rifiuta lo scambio.'];
                            $st['scambio'] = null;
                        }
                    }
                    break;
                case 'accetta_scambio':
                    $t = $st['scambio'];
                    self::concludiScambio($st, $t, $eventi);
                    break;
                case 'rifiuta_scambio':
                    $t = $st['scambio'];
                    $eventi[] = ['per' => null, 'testo' => $nome . ' rifiuta lo scambio proposto da ' . $st['nomi'][$t['da']] . '.'];
                    $st['scambio'] = null;
                    break;
                default:
                    throw new InvalidArgumentException('Mossa sconosciuta.');
            }
            // Il turno finisce da solo: dopo il tiro e quello che ne segue (acquisto, asta, pagamento) non serve un comando per chiuderlo.
            self::chiudiTurnoSeFinito($st, $eventi);
            return $eventi;
        } catch (Throwable $e) {
            registra_errore('Monopoli::applica', $e);
            throw $e;
        }
    }

    /** Se la mossa del turno è finita (fase "fine") e non c'è un'asta o uno scambio in corso, passa il turno al giocatore dopo. */
    public static function chiudiTurnoSeFinito(array &$st, array &$eventi): void
    {
        try {
            if (!$st['finita'] && $st['fase'] === 'fine' && ($st['asta'] ?? null) === null && ($st['scambio'] ?? null) === null) {
                self::prossimo($st, $eventi);
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::chiudiTurnoSeFinito', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ asta
    private static function leggiOfferta(array $st, int $p, string $id): ?int
    {
        try {
            $parti = explode('|', $id);
            if (count($parti) !== 2 || ($st['asta'] ?? null) === null || self::attore($st) !== $p || !ctype_digit($parti[1])) {
                return null;
            }
            $n = (int) $parti[1];
            $min = $st['asta']['offerta'] > 0 ? $st['asta']['offerta'] + 10 : 10;
            return ($n >= $min && $n <= $st['soldi'][$p]) ? $n : null;
        } catch (Throwable $e) {
            registra_errore('Monopoli::leggiOfferta', $e);
            throw $e;
        }
    }

    /** Chi non ha comprato: asta tra tutti i giocatori in gioco, a cominciare da quello dopo chi ha rinunciato. */
    private static function avviaAsta(array &$st, int $p, int $casella, array &$eventi): void
    {
        try {
            $ordine = [];
            $n = $st['n'];
            for ($k = 1; $k <= $n; $k++) {
                $q = ($p + $k) % $n;
                if (!$st['fuori'][$q] && $st['soldi'][$q] >= 10) {
                    $ordine[] = $q;
                }
            }
            if (count($ordine) === 0) {
                $eventi[] = ['per' => null, 'testo' => 'Nessuno può partecipare all\'asta: ' . self::nomeCasella($casella) . ' resta libera.'];
                self::dopoAzione($st, $p, $eventi);
                return;
            }
            $st['asta'] = ['casella' => $casella, 'partecipanti' => $ordine, 'indice' => 0, 'offerta' => 0, 'leader' => null, 'da' => $p];
            $st['fase'] = 'asta';
            $eventi[] = ['per' => null, 'testo' => 'Asta per ' . self::nomeCasella($casella) . ', prezzo di listino ' . self::euro(self::prezzo($casella)) . '. Si parte da ' . $st['nomi'][$ordine[0]] . '.'];
        } catch (Throwable $e) {
            registra_errore('Monopoli::avviaAsta', $e);
            throw $e;
        }
    }

    private static function controllaAsta(array &$st, array &$eventi): void
    {
        try {
            $a = $st['asta'];
            $n = count($a['partecipanti']);
            $fine = false;
            if ($n === 0) {
                $eventi[] = ['per' => null, 'testo' => 'Nessuno offre: ' . self::nomeCasella($a['casella']) . ' resta libera.'];
                $fine = true;
            } elseif ($a['leader'] !== null && $n === 1) {
                $vincitore = (int) $a['leader'];
                $st['soldi'][$vincitore] -= $a['offerta'];
                $st['proprieta'][$a['casella']]['di'] = $vincitore;
                $eventi[] = ['per' => null, 'testo' => $st['nomi'][$vincitore] . ' vince l\'asta e compra ' . self::nomeCasella($a['casella']) . ' per ' . self::euro($a['offerta']) . '.'];
                $fine = true;
            }
            if ($fine) {
                $chi = (int) $a['da'];
                $st['asta'] = null;
                self::dopoAzione($st, $chi, $eventi);
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::controllaAsta', $e);
            throw $e;
        }
    }

    /** Quanto vale per il computer una proprietà all'asta: il prezzo, di più se completa un colore o ne ferma uno agli avversari. */
    private static function valoreAsta(array $st, int $q, int $i): int
    {
        try {
            $v = self::prezzo($i);
            if (self::CASELLE[$i][1] === 'terreno') {
                $miei = 0;
                $altri = [];
                foreach (self::gruppo($i) as $k) {
                    if ($k === $i) {
                        continue;
                    }
                    $di = $st['proprieta'][$k]['di'];
                    if ($di === $q) {
                        $miei++;
                    } elseif ($di !== null) {
                        $altri[$di] = ($altri[$di] ?? 0) + 1;
                    }
                }
                $totale = count(self::gruppo($i)) - 1;
                if ($miei === $totale) {
                    $v += 300;
                } elseif ($miei > 0) {
                    $v += 80;
                }
                foreach ($altri as $n) {
                    if ($n === $totale) {
                        $v += 150; // si toglie il colore a un avversario
                    }
                }
            } elseif (self::CASELLE[$i][1] === 'stazione') {
                $mie = 0;
                foreach ($st['proprieta'] as $k => $y) {
                    if (self::CASELLE[$k][1] === 'stazione' && $y['di'] === $q) {
                        $mie++;
                    }
                }
                $v += 30 + 40 * $mie;
            }
            return $v;
        } catch (Throwable $e) {
            registra_errore('Monopoli::valoreAsta', $e);
            throw $e;
        }
    }

    private static function opzioniScambioPossibile(array $st, int $p): bool
    {
        try {
            foreach (self::opzioni($st, $p) as $g) {
                if ($g['id'] === 'scambio') {
                    return true;
                }
            }
            return false;
        } catch (Throwable $e) {
            registra_errore('Monopoli::opzioniScambioPossibile', $e);
            throw $e;
        }
    }

    /** Il server segna nello stato quali posti sono del computer ('computer' => [bool...]). */
    private static function eComputer(array $st, int $q): bool
    {
        try {
            return !empty($st['computer'][$q]);
        } catch (Throwable $e) {
            registra_errore('Monopoli::eComputer', $e);
            throw $e;
        }
    }

    private static function prossimo(array &$st, array &$eventi): void
    {
        try {
            if ($st['finita']) {
                return;
            }
            $p = $st['turno'];
            for ($k = 1; $k <= $st['n']; $k++) {
                $q = ($p + $k) % $st['n'];
                if (!$st['fuori'][$q]) {
                    if ($q <= $p) {
                        $st['giri']++;
                        if ($st['giri'] >= self::GIRI_MASSIMI) {
                            $migliore = $q;
                            $max = -1;
                            foreach (self::attivi($st) as $a) {
                                $pat = self::patrimonio($st, $a);
                                if ($pat > $max) {
                                    $max = $pat;
                                    $migliore = $a;
                                }
                            }
                            self::terminaConVincitore($st, $migliore, $eventi, 'Sono passati ' . self::GIRI_MASSIMI . ' giri: vince chi ha più patrimonio.');
                            return;
                        }
                    }
                    $st['turno'] = $q;
                    $st['fase'] = 'tiro';
                    $st['doppi'] = 0;
                    $eventi[] = ['per' => null, 'testo' => 'Tocca a ' . $st['nomi'][$q] . '.'];
                    return;
                }
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::prossimo', $e);
            throw $e;
        }
    }

    /** Dopo aver risolto la casella (acquisto o altro): doppio = si tira ancora, altrimenti fine turno. */
    private static function dopoAzione(array &$st, int $p, array &$eventi): void
    {
        try {
            if ($st['finita']) {
                return;
            }
            if ($st['fuori'][$p]) {
                self::prossimo($st, $eventi);
                return;
            }
            if ($st['doppi'] > 0 && $st['prigione'][$p] < 0) {
                $st['fase'] = 'tiro';
                $eventi[] = ['per' => null, 'testo' => $st['nomi'][$p] . ' ha fatto un doppio e tira ancora.'];
            } else {
                $st['fase'] = 'fine';
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::dopoAzione', $e);
            throw $e;
        }
    }

    private static function tira(array &$st, int $p, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            $a = random_int(1, 6);
            $b = random_int(1, 6);
            $st['dadi'] = [$a, $b];
            $doppio = $a === $b;
            $eventi[] = ['per' => null, 'testo' => $nome . ' tira i dadi: ' . $a . ' e ' . $b . ', totale ' . ($a + $b) . ($doppio ? ', doppio' : '') . '.'];

            if ($st['prigione'][$p] >= 0) {
                if ($doppio) {
                    $st['prigione'][$p] = -1;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' esce di prigione con un doppio.'];
                    $st['doppi'] = 0; // uscendo con un doppio non si tira ancora
                    self::muovi($st, $p, $a + $b, true, $eventi);
                    if (!$st['finita'] && $st['fase'] === 'tiro') {
                        $st['fase'] = 'fine';
                    }
                    return;
                }
                $st['prigione'][$p]++;
                if ($st['prigione'][$p] >= 3) {
                    $eventi[] = ['per' => null, 'testo' => $nome . ' non ha fatto doppi per tre volte e paga 50 euro per uscire.'];
                    if (!self::paga($st, $p, null, 50, $eventi)) {
                        self::prossimo($st, $eventi);
                        return;
                    }
                    $st['prigione'][$p] = -1;
                    $st['doppi'] = 0;
                    self::muovi($st, $p, $a + $b, true, $eventi);
                    if (!$st['finita'] && $st['fase'] === 'tiro') {
                        $st['fase'] = 'fine';
                    }
                    return;
                }
                $eventi[] = ['per' => null, 'testo' => $nome . ' resta in prigione.'];
                $st['fase'] = 'fine';
                return;
            }

            if ($doppio) {
                $st['doppi']++;
                if ($st['doppi'] >= 3) {
                    $eventi[] = ['per' => null, 'testo' => $nome . ' ha fatto tre doppi di fila e va in prigione.'];
                    self::vaiInPrigione($st, $p, $eventi);
                    $st['doppi'] = 0;
                    $st['fase'] = 'fine';
                    return;
                }
            } else {
                $st['doppi'] = 0;
            }
            self::muovi($st, $p, $a + $b, true, $eventi);
        } catch (Throwable $e) {
            registra_errore('Monopoli::tira', $e);
            throw $e;
        }
    }

    private static function vaiInPrigione(array &$st, int $p, array &$eventi): void
    {
        try {
            $st['pos'][$p] = 10;
            $st['prigione'][$p] = 0;
            $eventi[] = ['per' => null, 'testo' => $st['nomi'][$p] . ' va in prigione.'];
        } catch (Throwable $e) {
            registra_errore('Monopoli::vaiInPrigione', $e);
            throw $e;
        }
    }

    /** Muove di $passi (o, con $assoluto false, fino alla casella $passi) e risolve la casella. */
    private static function muovi(array &$st, int $p, int $passi, bool $relativo, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            $da = $st['pos'][$p];
            $a = $relativo ? ($da + $passi) % 40 : $passi;
            if ($relativo && $da + $passi >= 40 || (!$relativo && $a < $da)) {
                $st['soldi'][$p] += 200;
                $eventi[] = ['per' => null, 'testo' => $nome . ' passa dal Via e ritira 200 euro.'];
            }
            $st['pos'][$p] = $a;
            $c = self::CASELLE[$a];
            $eventi[] = ['per' => null, 'testo' => $nome . ' arriva a ' . $c[0] . '.'];
            $v = $st['variante'] ?? 'classica';
            if ($a === 0 && ($v === 'doppio_via' || $v === 'completo')) {
                $st['soldi'][$p] += 200;
                $eventi[] = ['per' => null, 'testo' => $nome . ' si ferma sul Via e ritira altri 200 euro (totale 400 euro).'];
            }
            self::risolvi($st, $p, $eventi);
        } catch (Throwable $e) {
            registra_errore('Monopoli::muovi', $e);
            throw $e;
        }
    }

    private static function risolvi(array &$st, int $p, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            $i = $st['pos'][$p];
            $c = self::CASELLE[$i];
            switch ($c[1]) {
                case 'parcheggio':
                    $v = $st['variante'] ?? 'classica';
                    if (($v === 'parcheggio_ricco' || $v === 'completo') && ($st['parcheggio_piatto'] ?? 0) > 0) {
                        $piatto = $st['parcheggio_piatto'];
                        $st['soldi'][$p] += $piatto;
                        $st['parcheggio_piatto'] = 0;
                        $eventi[] = ['per' => null, 'testo' => $nome . ' incassa il piatto del Parcheggio Gratuito pari a ' . self::euro($piatto) . '!'];
                    }
                    break;
                case 'terreno':
                case 'stazione':
                case 'societa':
                    $x = $st['proprieta'][$i];
                    if ($x['di'] === null) {
                        $eventi[] = ['per' => null, 'testo' => $c[0] . ' è libera e costa ' . self::euro(self::prezzo($i)) . '.'];
                        if ($st['soldi'][$p] >= self::prezzo($i)) {
                            $st['fase'] = 'acquisto';
                            return;
                        }
                        $eventi[] = ['per' => null, 'testo' => $nome . ' non ha abbastanza soldi per comprarla.'];
                    } elseif ($x['di'] !== $p) {
                        $dovuto = self::affitto($st, $i, array_sum($st['dadi']));
                        if ($x['ipoteca']) {
                            $eventi[] = ['per' => null, 'testo' => $c[0] . ' è ipotecata: non si paga l\'affitto.'];
                        } else {
                            $eventi[] = ['per' => null, 'testo' => $nome . ' paga ' . self::euro($dovuto) . ' di affitto a ' . $st['nomi'][$x['di']] . '.'];
                            self::paga($st, $p, (int) $x['di'], $dovuto, $eventi);
                        }
                    }
                    break;
                case 'tassa':
                    $eventi[] = ['per' => null, 'testo' => $nome . ' paga ' . self::euro((int) $c[2]) . ' di tassa.'];
                    self::paga($st, $p, null, (int) $c[2], $eventi);
                    break;
                case 'vai_prigione':
                    self::vaiInPrigione($st, $p, $eventi);
                    $st['doppi'] = 0;
                    $st['fase'] = 'fine';
                    return;
                case 'probabilita':
                case 'imprevisti':
                    self::pesca($st, $p, $c[1] === 'imprevisti', $eventi);
                    if ($st['fase'] === 'acquisto') {
                        return;
                    }
                    break;
                default:
                    break;
            }
            if ($st['finita']) {
                return;
            }
            if ($st['fuori'][$p]) {
                self::prossimo($st, $eventi);
                return;
            }
            if ($st['prigione'][$p] >= 0) {
                $st['doppi'] = 0;
                $st['fase'] = 'fine';
                return;
            }
            self::dopoAzione($st, $p, $eventi);
        } catch (Throwable $e) {
            registra_errore('Monopoli::risolvi', $e);
            throw $e;
        }
    }

    private static function pesca(array &$st, int $p, bool $imprevisti, array &$eventi): void
    {
        try {
            $nome = $st['nomi'][$p];
            $chiave = $imprevisti ? 'imprevisti' : 'probabilita';
            $mazzo = $imprevisti ? self::IMPREVISTI : self::PROBABILITA;
            $indice = array_shift($st[$chiave]);
            $carta = $mazzo[$indice];
            $eventi[] = ['per' => null, 'testo' => $nome . ' pesca ' . ($imprevisti ? 'un imprevisto' : 'una probabilità') . ': ' . $carta[0]];
            $tieni = false;
            switch ($carta[1]) {
                case 'ricevi':
                    $st['soldi'][$p] += $carta[2];
                    break;
                case 'paga':
                    self::paga($st, $p, null, $carta[2], $eventi);
                    break;
                case 'esci':
                    $st['esci'][$p]++;
                    $tieni = true;
                    break;
                case 'prigione':
                    self::vaiInPrigione($st, $p, $eventi);
                    break;
                case 'vai':
                    $dest = $carta[2];
                    $da = $st['pos'][$p];
                    if ($dest < $da || $dest === 0) {
                        $st['soldi'][$p] += 200;
                        $eventi[] = ['per' => null, 'testo' => $nome . ' passa dal Via e ritira 200 euro.'];
                    }
                    $st['pos'][$p] = $dest;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' arriva a ' . self::nomeCasella($dest) . '.'];
                    $v = $st['variante'] ?? 'classica';
                    if ($dest === 0 && ($v === 'doppio_via' || $v === 'completo')) {
                        $st['soldi'][$p] += 200;
                        $eventi[] = ['per' => null, 'testo' => $nome . ' si ferma sul Via e ritira altri 200 euro (totale 400 euro).'];
                    }
                    // le caselle di arrivo delle carte sono proprietà o il Via: si risolve (senza pescare di nuovo)
                    if ($dest !== 0) {
                        self::risolvi($st, $p, $eventi);
                    }
                    break;
                case 'indietro':
                    $st['pos'][$p] = ($st['pos'][$p] + 37) % 40;
                    $eventi[] = ['per' => null, 'testo' => $nome . ' arriva a ' . self::nomeCasella($st['pos'][$p]) . '.'];
                    self::risolvi($st, $p, $eventi);
                    break;
                case 'compleanno':
                    foreach (self::attivi($st) as $q) {
                        if ($q !== $p) {
                            self::paga($st, $q, $p, $carta[2], $eventi);
                        }
                    }
                    break;
                case 'paga_tutti':
                    foreach (self::attivi($st) as $q) {
                        if ($q !== $p && !$st['fuori'][$p]) {
                            self::paga($st, $p, $q, $carta[2], $eventi);
                        }
                    }
                    break;
                case 'riparazioni':
                    $dovuto = 0;
                    foreach ($st['proprieta'] as $x) {
                        if ($x['di'] === $p) {
                            $dovuto += $x['case'] === 5 ? $carta[3] : $x['case'] * $carta[2];
                        }
                    }
                    if ($dovuto > 0) {
                        self::paga($st, $p, null, $dovuto, $eventi);
                    } else {
                        $eventi[] = ['per' => null, 'testo' => $nome . ' non ha edifici e non paga nulla.'];
                    }
                    break;
            }
            if (!$tieni) {
                $st[$chiave][] = $indice;
            }
        } catch (Throwable $e) {
            registra_errore('Monopoli::pesca', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ vista
    public static function vista(array $st, int $posto): array
    {
        try {
            $caselle = [];
            foreach (self::CASELLE as $i => $c) {
                $r = ['indice' => $i, 'nome' => $c[0], 'tipo' => $c[1]];
                if (self::eProprieta($i)) {
                    $r['prezzo'] = (int) $c[2];
                    $r['colore'] = $c[1] === 'terreno' ? $c[3] : null;
                    $r['valore_ipoteca'] = intdiv((int) $c[2], 2);
                    if ($c[1] === 'terreno') {
                        $r['affitti'] = $c[4];
                        $r['costo_edificio'] = (int) $c[5];
                        $r['gruppo'] = array_map(fn($k) => self::CASELLE[$k][0], self::gruppo($i));
                    } elseif ($c[1] === 'stazione') {
                        $r['affitti'] = [25, 50, 100, 200];
                    } else {
                        $r['affitti'] = [4, 10];
                    }
                    $x = $st['proprieta'][$i];
                    $r['proprietario'] = $x['di'];
                    $r['case'] = $x['case'];
                    $r['ipoteca'] = $x['ipoteca'];
                } elseif ($c[1] === 'tassa') {
                    $r['prezzo'] = (int) $c[2];
                }
                $caselle[] = $r;
            }
            $giocatori = [];
            for ($i = 0; $i < $st['n']; $i++) {
                $giocatori[] = ['nome' => $st['nomi'][$i], 'soldi' => $st['soldi'][$i], 'posizione' => $st['pos'][$i], 'prigione' => $st['prigione'][$i] >= 0,
                    'fuori' => $st['fuori'][$i], 'carte_esci' => $st['esci'][$i], 'patrimonio' => $st['fuori'][$i] ? 0 : self::patrimonio($st, $i)];
            }
            return ['tipo' => 'monopoli', 'turno' => $st['turno'], 'fase' => $st['fase'], 'dadi' => $st['dadi'], 'caselle' => $caselle, 'giocatori' => $giocatori,
                'finita' => $st['finita'], 'vincitore' => $st['vincitore'], 'giri' => $st['giri'], 'giri_massimi' => self::GIRI_MASSIMI, 'punteggi' => $st['punteggi'],
                'variante' => $st['variante'] ?? 'classica',
                'variante_nome' => self::VARIANTE_INFO[$st['variante'] ?? 'classica']['nome'] ?? ($st['variante'] ?? 'classica'),
                'parcheggio_piatto' => $st['parcheggio_piatto'] ?? 0,
                'case_banca' => self::caseDisponibili($st),
                'alberghi_banca' => self::alberghiDisponibili($st),
                'scambio' => $st['scambio'] === null ? null : ['da' => $st['scambio']['da'], 'a' => $st['scambio']['a'], 'testo' => self::descriviScambio($st, $st['scambio'])],
                'asta' => ($st['asta'] ?? null) === null ? null : ['casella' => $st['asta']['casella'], 'nome' => self::nomeCasella($st['asta']['casella']), 'offerta' => $st['asta']['offerta'], 'leader' => $st['asta']['leader'], 'attore' => self::attore($st)],
                'attore' => self::attore($st)];
        } catch (Throwable $e) {
            registra_errore('Monopoli::vista', $e);
            throw $e;
        }
    }

    public static function situazione(array $st, int $posto): string
    {
        try {
            $t = '';
            $v = $st['variante'] ?? 'classica';
            if ($v !== 'classica') {
                $t .= '[Variante: ' . (self::VARIANTE_INFO[$v]['nome'] ?? $v) . '] ';
            }
            if (($v === 'parcheggio_ricco' || $v === 'completo') && ($st['parcheggio_piatto'] ?? 0) > 0) {
                $t .= '[Piatto Parcheggio: ' . self::euro($st['parcheggio_piatto']) . '] ';
            }
            for ($i = 0; $i < $st['n']; $i++) {
                $t .= $st['nomi'][$i] . ($i === $posto ? ' (tu)' : '') . ': ';
                if ($st['fuori'][$i]) {
                    $t .= 'eliminato. ';
                    continue;
                }
                $t .= self::euro($st['soldi'][$i]) . ', sulla casella ' . self::nomeCasella($st['pos'][$i]) . ($st['prigione'][$i] >= 0 ? ' (in prigione)' : '');
                $mie = [];
                foreach ($st['proprieta'] as $k => $x) {
                    if ($x['di'] === $i) {
                        $mie[] = self::nomeCasella($k) . ($x['ipoteca'] ? ' (ipotecata)' : ($x['case'] === 5 ? ' (albergo)' : ($x['case'] > 0 ? ' (' . $x['case'] . ' case)' : '')));
                    }
                }
                $t .= $mie ? '. Proprietà: ' . implode(', ', $mie) . '. ' : '. Nessuna proprietà. ';
            }
            return trim($t);
        } catch (Throwable $e) {
            registra_errore('Monopoli::situazione', $e);
            throw $e;
        }
    }

    // ------------------------------------------------------------------ computer
    /** Il peggior affitto che oggi gli avversari possono chiedere a $p (con un tiro medio): serve a decidere quanti soldi tenere da parte. */
    private static function esposizione(array $st, int $p): int
    {
        try {
            $max = 0;
            foreach ($st['proprieta'] as $i => $x) {
                if ($x['di'] !== null && $x['di'] !== $p && !$x['ipoteca'] && !$st['fuori'][$x['di']]) {
                    $max = max($max, self::affitto($st, $i, 7));
                }
            }
            return $max;
        } catch (Throwable $e) {
            registra_errore('Monopoli::esposizione', $e);
            throw $e;
        }
    }

    /** Quanti soldi il computer tiene da parte: pochi all'inizio, quando conviene comprare tutto, di più quando gli avversari hanno costruito. */
    private static function riservaBot(array $st, int $p, int $minima): int
    {
        try {
            $r = max($minima, min(350, (int) round(self::esposizione($st, $p) * 0.6)));
            if (($st['giri'] ?? 0) < 12) {
                $r = min($r, max($minima, 100));
            }
            return $r;
        } catch (Throwable $e) {
            registra_errore('Monopoli::riservaBot', $e);
            throw $e;
        }
    }

    private static function proprietaLibere(array $st): int
    {
        try {
            $n = 0;
            foreach ($st['proprieta'] as $x) {
                if ($x['di'] === null) {
                    $n++;
                }
            }
            return $n;
        } catch (Throwable $e) {
            registra_errore('Monopoli::proprietaLibere', $e);
            throw $e;
        }
    }

    /**
     * Se a un colore manca una sola proprietà e ce l'ha un altro giocatore, il computer prova a comprargliela offrendo il prezzo più 320 euro
     * (un computer non aiuta nessuno a completare un colore per meno): senza colori completi non si costruisce e la partita non si decide.
     * Al massimo una proposta ogni 4 giri. Restituisce la mossa "scambio|..." oppure null.
     */
    private static function scambioBot(array $st, int $p): ?string
    {
        try {
            if (($st['ultimo_scambio_bot'][$p] ?? -99) > ($st['giri'] ?? 0) - 4) {
                return null;
            }
            $soldi = $st['soldi'][$p];
            $migliore = null;
            $valoreMigliore = 0;
            foreach (self::CASELLE as $i => $c) {
                if ($c[1] !== 'terreno') {
                    continue;
                }
                $x = $st['proprieta'][$i];
                $q = $x['di'];
                if ($q === null || $q === $p || $x['ipoteca'] || $st['fuori'][$q] || self::caseNelGruppo($st, $i) > 0) {
                    continue;
                }
                $gr = self::gruppo($i);
                $miei = 0;
                foreach ($gr as $k) {
                    if ($st['proprieta'][$k]['di'] === $p && !$st['proprieta'][$k]['ipoteca']) {
                        $miei++;
                    }
                }
                if ($miei !== count($gr) - 1) {
                    continue;
                }
                $offerta = (int) (ceil((self::prezzo($i) + 320) / 10) * 10);
                if ($soldi - $offerta < 400) {
                    continue;
                }
                $valore = (int) $c[5] * count($gr);   // i colori più cari rendono di più
                if ($valore > $valoreMigliore) {
                    $valoreMigliore = $valore;
                    $migliore = 'scambio|' . $q . '|-|' . $offerta . '|' . $i . '|0';
                }
            }
            return $migliore;
        } catch (Throwable $e) {
            registra_errore('Monopoli::scambioBot', $e);
            throw $e;
        }
    }

    public static function bot(array $st, int $p): string
    {
        try {
            $mosse = self::mosse($st, $p);
            $ids = array_column($mosse, 'id');
            $ha = fn(string $x) => in_array($x, $ids, true);
            $soldi = $st['soldi'][$p];

            // risposta a una proposta di scambio (di solito il motore risponde per il computer subito: vedi valutaScambioComputer)
            if (($st['scambio'] ?? null) !== null && (int) $st['scambio']['a'] === $p) {
                return self::valutaScambioComputer($st, $st['scambio']) ? 'accetta_scambio' : 'rifiuta_scambio';
            }

            // asta: si offre fino a quello che la proprietà vale davvero per questo giocatore (di più se completa un colore o lo toglie a un avversario)
            if (($st['asta'] ?? null) !== null) {
                $a = $st['asta'];
                $massimo = min((int) round(self::valoreAsta($st, $p, $a['casella']) * 1.2), $soldi - 30);
                $prossima = $a['offerta'] > 0 ? $a['offerta'] + 10 : max(10, (int) (round(self::prezzo($a['casella']) * 0.3 / 10) * 10));
                if ($ha('offri') && $prossima <= $massimo && $prossima <= $soldi) {
                    return 'offri|' . $prossima;
                }
                return 'ritirati_asta';
            }

            if ($st['fase'] === 'acquisto') {
                $i = $st['pos'][$p];
                $costo = self::prezzo($i);
                $tipo = self::CASELLE[$i][1];
                $completa = false;
                $blocca = false;
                $miei = 0;
                if ($tipo === 'terreno') {
                    $altri = count(self::gruppo($i)) - 1;
                    $degli = [];
                    foreach (self::gruppo($i) as $k) {
                        if ($k === $i) {
                            continue;
                        }
                        $di = $st['proprieta'][$k]['di'];
                        if ($di === $p) {
                            $miei++;
                        } elseif ($di !== null) {
                            $degli[$di] = ($degli[$di] ?? 0) + 1;
                        }
                    }
                    $completa = $miei === $altri;
                    foreach ($degli as $n) {
                        if ($n === $altri) {
                            $blocca = true;   // un avversario ha tutto il resto del colore: comprarla glielo impedisce
                        }
                    }
                }
                $riserva = self::riservaBot($st, $p, 60);
                $resto = $soldi - $costo;
                $compra = $ha('compra') && (
                    $resto >= $riserva
                    || $completa
                    || ($blocca && $resto >= 30)
                    || ($miei > 0 && $resto >= 40)
                    || ($tipo === 'stazione' && $resto >= 50)
                    || ($tipo === 'societa' && $resto >= 80)
                );
                return $compra ? 'compra' : 'passa';
            }

            if ($st['fase'] === 'tiro' && $st['prigione'][$p] >= 0) {
                if ($ha('usa_carta')) {
                    return 'usa_carta';
                }
                // all'inizio, con proprietà ancora da comprare, conviene uscire; più avanti, con gli avversari che hanno costruito, in prigione si è al sicuro
                if ($ha('paga_prigione') && self::proprietaLibere($st) >= 6 && $soldi >= 250) {
                    return 'paga_prigione';
                }
                if ($ha('paga_prigione') && $soldi > 900 && self::esposizione($st, $p) < 250) {
                    return 'paga_prigione';
                }
                return 'tira';
            }

            $opzioni = self::opzioni($st, $p);
            if ($st['fase'] === 'tiro' && $st['prigione'][$p] < 0 && in_array('scambio', array_column($opzioni, 'id'), true)) {
                $proposta = self::scambioBot($st, $p);
                if ($proposta !== null) {
                    return $proposta;
                }
            }
            $gestione = [];
            foreach ($opzioni as $g) {
                foreach ($g['voci'] as $v) {
                    $gestione[] = $v;
                }
            }
            $esposizione = self::esposizione($st, $p);
            $riservaCostruzione = max(100, min(300, (int) round($esposizione * 0.5)));

            // costruire: prima dove ogni euro speso fa crescere di più l'affitto
            $migliore = null;
            $punteggioMigliore = -1.0;
            $serveSoldi = null;   // una costruzione buona ma senza abbastanza soldi: si può ipotecare qualcosa per farla
            foreach ($gestione as $x) {
                if (!str_starts_with($x['id'], 'costruisci|')) {
                    continue;
                }
                $i = (int) explode('|', $x['id'])[1];
                $case = $st['proprieta'][$i]['case'];
                $costo = (int) self::CASELLE[$i][5];
                $aff = self::CASELLE[$i][4];
                $punteggio = ($aff[min(5, $case + 1)] - $aff[$case]) / $costo;
                if ($soldi - $costo >= $riservaCostruzione) {
                    if ($punteggio > $punteggioMigliore) {
                        $punteggioMigliore = $punteggio;
                        $migliore = $x['id'];
                    }
                } elseif ($serveSoldi === null || $costo < $serveSoldi) {
                    $serveSoldi = $costo;
                }
            }
            if ($migliore !== null) {
                return $migliore;
            }
            if ($serveSoldi !== null) {
                // si ipoteca una proprietà che non fa parte di un colore in costruzione né di un colore a cui ne manca una sola
                foreach ($gestione as $x) {
                    if (!str_starts_with($x['id'], 'ipoteca|')) {
                        continue;
                    }
                    $k = (int) explode('|', $x['id'])[1];
                    if (self::CASELLE[$k][1] === 'terreno') {
                        $nelColore = 0;
                        foreach (self::gruppo($k) as $g) {
                            if ($st['proprieta'][$g]['di'] === $p) {
                                $nelColore++;
                            }
                        }
                        if ($nelColore > 1) {
                            continue;
                        }
                    }
                    if ($soldi + intdiv(self::prezzo($k), 2) - $serveSoldi >= $riservaCostruzione) {
                        return $x['id'];
                    }
                }
            }

            // riscattare le ipoteche quando i soldi sono in abbondanza
            $riservaRiscatto = max(250, self::riservaBot($st, $p, 60) + 150);
            foreach ($gestione as $x) {
                if (str_starts_with($x['id'], 'riscatta|')) {
                    $k = (int) explode('|', $x['id'])[1];
                    if ($soldi - (int) ceil(self::prezzo($k) / 2 * 1.1) >= $riservaRiscatto) {
                        return $x['id'];
                    }
                }
            }
            return 'tira';
        } catch (Throwable $e) {
            registra_errore('Monopoli::bot', $e);
            throw $e;
        }
    }
}
