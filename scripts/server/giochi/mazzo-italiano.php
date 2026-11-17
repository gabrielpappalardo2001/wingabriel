<?php
// Il mazzo italiano da 40 carte, in comune tra briscola e scopa.
//
// Carta = numero da 0 a 39: il seme è id / 10 e la posizione è id % 10 (0 asso, 1 due, ... 6 sette, 7 fante, 8 cavallo, 9 re).
// Semi: 0 coppe, 1 denari, 2 bastoni, 3 spade. Le immagini corrispondono alle carte regionali italiane disegnate
// da scripts\genera-carte.cs e alle relative varianti speciali (Settebello, Re Bello, Asso Pigliatutto).

declare(strict_types=1);

final class MazzoItaliano
{
    public const SEMI = ['coppe', 'denari', 'bastoni', 'spade'];
    private const NOMI = ['Asso', 'Due', 'Tre', 'Quattro', 'Cinque', 'Sei', 'Sette', 'Fante', 'Cavallo', 'Re'];
    private const SIGLE = ['A', '2', '3', '4', '5', '6', '7', 'F', 'C', 'R'];
    private const RANGO_IMMAGINE = [1, 2, 3, 4, 5, 6, 7, 11, 12, 13];

    public static function seme(int $id): int
    {
        try {
            return intdiv($id, 10);
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::seme', $e);
            throw $e;
        }
    }

    public static function posizione(int $id): int
    {
        try {
            return $id % 10;
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::posizione', $e);
            throw $e;
        }
    }

    /** Valore numerico della carta nella scopa: 1 l'asso ... 7 il sette, 8 il fante, 9 il cavallo, 10 il re. */
    public static function numero(int $id): int
    {
        try {
            return ($id % 10) + 1;
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::numero', $e);
            throw $e;
        }
    }

    public static function nome(int $id): string
    {
        try {
            return self::NOMI[$id % 10] . ' di ' . self::SEMI[intdiv($id, 10)];
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::nome', $e);
            throw $e;
        }
    }

    public static function breve(int $id): string
    {
        try {
            return self::SIGLE[$id % 10] . ' ' . substr(self::SEMI[intdiv($id, 10)], 0, 3);
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::breve', $e);
            throw $e;
        }
    }

    public static function sigla(int $id): string
    {
        try {
            return self::SIGLE[$id % 10];
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::sigla', $e);
            throw $e;
        }
    }

    public static function semeNome(int $id): string
    {
        try {
            return self::SEMI[intdiv($id, 10)];
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::semeNome', $e);
            throw $e;
        }
    }

    public static function immagine(int $id, ?string $varianteSpeciale = null): string
    {
        try {
            $sigla = self::sigla($id);
            $seme = self::semeNome($id);
            if ($varianteSpeciale !== null && $varianteSpeciale !== '') {
                return $sigla . '_' . $seme . '_' . $varianteSpeciale;
            }
            return $sigla . '_' . $seme;
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::immagine', $e);
            throw $e;
        }
    }

    public static function carta(int $id, ?string $varianteSpeciale = null): array
    {
        try {
            return [
                'id' => $id,
                'nome' => self::nome($id),
                'seme' => self::seme($id),
                'seme_nome' => self::semeNome($id),
                'rango' => self::RANGO_IMMAGINE[$id % 10],
                'sigla' => self::sigla($id),
                'immagine' => self::immagine($id, $varianteSpeciale),
                'jolly' => false,
                'breve' => self::breve($id),
            ];
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::carta', $e);
            throw $e;
        }
    }

    /** Le carte in ordine di seme e posizione. */
    public static function ordina(array $carte): array
    {
        try {
            sort($carte);
            return array_values($carte);
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::ordina', $e);
            throw $e;
        }
    }

    public static function elenca(array $ids): string
    {
        try {
            return implode(', ', array_map(fn($id) => self::nome($id), self::ordina($ids)));
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::elenca', $e);
            throw $e;
        }
    }

    public static function mescolato(): array
    {
        try {
            $mazzo = range(0, 39);
            shuffle($mazzo);
            return $mazzo;
        } catch (Throwable $e) {
            registra_errore('MazzoItaliano::mescolato', $e);
            throw $e;
        }
    }
}
