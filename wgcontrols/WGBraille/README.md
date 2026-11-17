# Braille

Supporto alle barre braille in WinGabriel. Il codice sta in `wgcontrols\wgcontrols\WGBraille*.vb`; qui ci sono solo liblouis e le tabelle.

## Cosa c'è

| File | Scopo |
|---|---|
| `WGBraille.vb` | Gestore unico: trova la barra, mostra il testo, tasti, instradamento, impostazioni |
| `WGBrailleHims.vb` | Driver HIMS (Braille Sense 6 e 6 mini, Classic, Polaris, Edge, Smart Beetle): seriale (Bluetooth SPP, cavo con COM virtuale) e WinUSB |
| `WGBrailleHid.vb` | Barre HID braille standard (pagina d'uso 0x41): nessun driver |
| `WGBrailleRilevamento.vb` | Elenco dei posti dove cercare (HID, WinUSB, porte COM con nome del dispositivo) |
| `WGBrailleLouis.vb` | Ponte verso liblouis (celle, posizioni, punti battuti in lettere) con tabella interna di riserva |
| `WGCampoMovimento.Braille.vb`, `WGWord.Braille.vb` | Editor di testo che si fanno leggere e scrivere dalla barra |
| `WGCentrale\Menu\MenuBraille.vb` | Menu Impostazioni, Braille |

Il testo arriva da `sintesivocale.LeggiTesto` (nessun punto di ingresso nuovo). All'avvio `sintesivocale.GetInstance()` fa partire `WGBraille.Avvia()`, che cerca la barra ogni pochi secondi finché non la trova e di nuovo se si scollega.

## Protocollo HIMS

Ricavato dal driver `hims` di NVDA (vedi i commenti in `WGBrailleHims.vb`). La barra manda pacchetti di 10 byte (`FA`, modo, ?, dato, 4 byte di tasti, somma, `FD`); il computer scrive le celle con `FC FC 01 F0 <lunghezza> <celle> F1 F2 00 00 F3 00 00 00 00 <somma> FD FD`.

## Collegamento

- **Bluetooth**: la barra deve essere già associata a Windows (porta COM "Standard Serial over Bluetooth"). Si provano da sole le porte il cui dispositivo si chiama Braille..., Hims, Smart Beetle...
- **Cavo**: porta COM virtuale (FTDI) oppure WinUSB; le barre HID standard non vogliono driver.
- Le Braille Sense 6 via USB usano i driver HIMS: senza di loro (o senza WinUSB) Windows non le mostra e resta il Bluetooth.

## Dati nativi

`liblouis64.dll` (a 64 bit) e `liblouis32.dll` (a 32 bit) sono la libreria ufficiale di liblouis (LGPL 2.1 o successiva, https://liblouis.io; la stessa usata da NVDA), una per ogni bitness del processo; `tabelle\` contiene le tabelle usate e quelle che includono, per italiano, inglese (Stati Uniti e Regno Unito), francese, tedesco, spagnolo, portoghese, olandese, polacco, russo, greco, turco, svedese, danese, norvegese, finlandese, ceco e rumeno. `WinGabriel.vbproj` le copia in `braille\` accanto al programma. Senza la dll giusta per quel processo si usa una tabella interna in braille informatico a 6 punti.
