# Sicurezza

Il codice di WinGabriel è pubblico: la sicurezza non dipende dal fatto che il codice sia segreto, ma dai segreti che nel repository non ci sono.

## Cosa non sta mai nel repository

- Password, chiavi, token e indirizzi personali dei server: stanno in file riservati sui server, fuori dal repository
  (per esempio `/etc/wingabriel-beta.password` per l'utente di prova della copia beta dei giochi).
- La chiave privata con cui si firmano rilasci e plugin: resta sul computer di chi pubblica. Nel codice c'è solo la chiave pubblica
  che serve a controllare la firma; un rilascio con la firma sbagliata non si installa, da qualunque parte arrivi (sito o GitHub).
- Il segreto incorporato nella libreria di cifratura (`wgcontrols/WGCifraturaNativa/WGSegreto.h`): chi compila da sé ne riceve
  uno nuovo, creato a caso alla prima compilazione.
- Le password degli utenti: i server tengono solo impronte, e la chat è cifrata da un capo all'altro.

## Come segnalare un problema

Se trovi una vulnerabilità, non aprire una segnalazione pubblica: usa «Report a vulnerability» nella scheda Security di questo
repository, oppure la funzione Segnala di WinGabriel.

## Se trovi un segreto per errore

Segnalalo allo stesso modo: lo si cambia subito sul server e lo si toglie dal codice.
