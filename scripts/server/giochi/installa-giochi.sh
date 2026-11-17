#!/bin/bash
# Installa su wingabriel.net (Aruba, come root) i giochi di WinGabriel (WGGiochi): burraco, monopoli, briscola e scopa, anche contro altri giocatori.
#   giochi.php, burraco.php, monopoli.php, mazzo-italiano.php, briscola.php, scopa.php e la versione web (index.html, giochi.js, giochi.css, più sw.js, manifest e icone per la PWA, le cartelle carte/ e monopoli/ con le immagini) in /var/www/html/wingabriel.net/giochi
#   la cartella /var/lib/wingabriel-giochi per le partite: file temporanei che spariscono da soli (nomi 6 ore, partite 3 ore o 15 minuti dopo la fine)
# Con BETA=1 (lo fa rilascia-server.cmd -Beta) installa invece la copia di prova in wingabriel.net/beta/giochi, con le sue partite e i suoi utenti
# (/var/lib/wingabriel-giochi-beta) e l'utente di prova wingabrielbeta; i giochi veri non si toccano.
# Non c'è database. Si può rilanciare quando si vuole: non tocca le partite in corso.
# Protezione dei dati: il sito non deve scrivere gli indirizzi IP di chi gioca. Per questo lo script toglie giochi/giochi.php dal registro di
# nginx e di Apache e dalla copia veloce di nginx (come già fa installa-sito.sh per conta.php). Il registro dei file statici (pagine e script)
# resta quello normale.
# Le attese lunghe (fino a 20 secondi) non occupano più un processo di php-fpm per ogni giocatore: le tiene il motore d'attesa
# (installa-attesa.sh, che questo script lancia). Senza il motore ogni giocatore che aspetta occupa un processo php-fpm.
set -euo pipefail

ORIGINE="$(cd "$(dirname "$0")" && pwd)"
SITO=/var/www/html/wingabriel.net
DESTINAZIONE="$SITO/giochi"
DATI=/var/lib/wingabriel-giochi
UTENTE=wingabriel   # PHP gira con questo utente: possiede solo i dati, il codice è di root
# alla fine (anche se si esce prima) si aggiornano le impronte del controllo d'integrità del sito, se c'è: i file nuovi sono quelli giusti
trap '[ -x /usr/local/bin/wingabriel-impronte ] && /usr/local/bin/wingabriel-impronte aggiorna >/dev/null 2>&1 || true' EXIT
GRUPPO=www-data
NGINX=/etc/nginx/nginx.conf
APACHE_SITO=/etc/apache2/sites-available/wingabriel.net.conf
BETA="${BETA:-}"
UTENTE_BETA=wingabrielbeta
# La password dell'account beta non sta nel repository: sta sul server in /etc/wingabriel-beta.password (chmod 600)
# oppure si passa in ambiente (PASSWORD_BETA=... BETA=1 ./installa-giochi.sh).
PASSWORD_BETA="${PASSWORD_BETA:-$(cat /etc/wingabriel-beta.password 2>/dev/null || true)}"

if [ "$(hostname)" != "wingabriel" ]; then
  echo "Questo script va lanciato su Aruba (wingabriel), non su $(hostname)"
  exit 1
fi

if [ "$BETA" = 1 ]; then
  DESTINAZIONE="$SITO/beta/giochi"
  DATI=/var/lib/wingabriel-giochi-beta
  echo "== COPIA BETA: $DESTINAZIONE"
  python3 "$ORIGINE/configura-beta.py"   # prima di tutto: senza registro e senza copia veloce, o non si installa
fi

echo "== Controllo dei file PHP"
for f in giochi.php burraco.php monopoli.php mazzo-italiano.php briscola.php scopa.php battaglia.php; do
  php -l "$ORIGINE/$f" | tail -1
done
for f in attesa.py installa-attesa.sh; do
  [ -f "$ORIGINE/$f" ] || { echo "Manca $f: lo carica rilascia-server."; exit 1; }
done

echo "== Cartelle"
if [ "$BETA" = 1 ]; then install -d -m 755 -o root -g "$GRUPPO" "$SITO/beta"; fi
install -d -m 755 -o root -g "$GRUPPO" "$DESTINAZIONE"
install -d -m 770 -o "$UTENTE" -g "$GRUPPO" "$DATI"

echo "== File"
for f in giochi.php burraco.php monopoli.php mazzo-italiano.php briscola.php scopa.php battaglia.php index.html giochi.js giochi.css sw.js manifest.webmanifest icona-192.png icona-512.png icona-maskable-512.png apple-touch-icon.png; do
  install -m 644 -o root -g "$GRUPPO" "$ORIGINE/$f" "$DESTINAZIONE/$f"
done
# le immagini delle carte (una per carta, più il dorso)
install -d -m 755 -o root -g "$GRUPPO" "$DESTINAZIONE/carte"
for f in "$ORIGINE"/carte/*.png; do
  install -m 644 -o root -g "$GRUPPO" "$f" "$DESTINAZIONE/carte/$(basename "$f")"
done
# i suoni dei giochi (carte, dadi, soldi...: li fa scripts/genera-suoni.py)
install -d -m 755 -o root -g "$GRUPPO" "$DESTINAZIONE/suoni"
for f in "$ORIGINE"/suoni/*.wav; do
  install -m 644 -o root -g "$GRUPPO" "$f" "$DESTINAZIONE/suoni/$(basename "$f")"
done
# le immagini del monopoli (dadi, case, albergo, pedine, icone delle caselle: le fa scripts/genera-monopoli.py)
install -d -m 755 -o root -g "$GRUPPO" "$DESTINAZIONE/monopoli"
for f in "$ORIGINE"/monopoli/*.png; do
  install -m 644 -o root -g "$GRUPPO" "$f" "$DESTINAZIONE/monopoli/$(basename "$f")"
done

if [ "$BETA" = 1 ]; then
  echo "== Utente di prova $UTENTE_BETA"
  if [ -z "$PASSWORD_BETA" ]; then
    echo "ATTENZIONE: manca /etc/wingabriel-beta.password: l'utente di prova non si crea"
  else
  AUTH=$(php -r 'echo bin2hex(substr(hash_pbkdf2("sha256", $argv[2], "WGAccount-v1|" . strtolower($argv[1]), 600000, 64, true), 0, 32));' "$UTENTE_BETA" "$PASSWORD_BETA")
  RISPOSTA=$(curl -s -m 60 -X POST -H 'Content-Type: application/json' -d "{\"nome\":\"$UTENTE_BETA\",\"auth\":\"$AUTH\"}" "https://wingabriel.net/beta/giochi/giochi.php?a=registra" || true)
  case "$RISPOSTA" in
    *'"token"'*) echo "utente $UTENTE_BETA creato";;
    *"esiste già"*) echo "utente $UTENTE_BETA già presente";;
    *) echo "ATTENZIONE: l'utente $UTENTE_BETA non risulta creato: $RISPOSTA";;
  esac
  fi
  # le attese lunghe passano dal motore d'attesa (se non va, i giochi funzionano lo stesso, con più processi php)
  bash "$ORIGINE/installa-attesa.sh" giochi || echo "ATTENZIONE: il motore d'attesa non è stato installato: i giochi usano le attese di PHP."
  echo
  echo "Fatto. La copia beta dei giochi è su https://wingabriel.net/beta/giochi/ (utente $UTENTE_BETA)"
  exit 0
fi

echo "== nginx e Apache (niente registro né copia veloce per giochi.php)"
python3 - "$NGINX" "$APACHE_SITO" <<'PYEOF'
import re, subprocess, sys
nginx, apache = sys.argv[1], sys.argv[2]
originali = {}

def leggi(p):
    with open(p, encoding='utf-8', newline='') as f:
        return f.read()

def scrivi(p, t):
    with open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(t)

def modifica(p, funzione):
    t = leggi(p)
    n = funzione(t)
    if n != t:
        originali[p] = t
        scrivi(p, n)
        print('modificato:', p)
    else:
        print('già a posto:', p)

def nginx_conf(t):
    # la copia veloce non deve mai servire le risposte dei giochi
    if 'giochi' not in t:
        t = t.replace('|quotidiano|statistiche|conta\\.php)" 1;', '|quotidiano|statistiche|conta\\.php|giochi/giochi\\.php)" 1;', 1)
        t = t.replace('        "~^/conta\\.php" 0;\n', '        "~^/conta\\.php" 0;\n        "~^/giochi/giochi\\.php" 0;\n', 1)
    return t

def apache_conf(t):
    # dopo la riga di conta.php, in OGNI virtual host (8080 e 8443: da qui passa il traffico HTTPS), ci dev'essere anche quella di giochi.php
    riga_giochi = '    SetEnvIf Request_URI "^/giochi/giochi\.php" nolog_conta'
    righe = t.split(chr(10))
    uscita = []
    for i, r in enumerate(righe):
        uscita.append(r)
        if r == '    SetEnvIf Request_URI "^/conta\.php" nolog_conta' and riga_giochi not in righe[i + 1:i + 4]:
            uscita.append(riga_giochi)
    return chr(10).join(uscita)

modifica(nginx, nginx_conf)
modifica(apache, apache_conf)

if 'giochi' not in leggi(nginx) or leggi(apache).count('giochi/giochi') < leggi(apache).count('conta\.php" nolog_conta'):
    print('ATTENZIONE: non ho trovato le righe di conta.php da cui partire; il registro di giochi.php va escluso a mano.')

def prova():
    a = subprocess.run(['nginx', '-t'], capture_output=True, text=True)
    b = subprocess.run(['apache2ctl', 'configtest'], capture_output=True, text=True)
    return a.returncode == 0 and b.returncode == 0, a.stderr + b.stderr

riuscita, testo = prova()
if not riuscita:
    print('La prova della configurazione non è riuscita, torno com\'era:\n' + testo)
    for p, t in originali.items():
        scrivi(p, t)
    sys.exit(1)
if originali:
    subprocess.run(['systemctl', 'reload', 'nginx'], check=True)
    subprocess.run(['systemctl', 'reload', 'apache2'], check=True)
    print('nginx e Apache ricaricati')
PYEOF

echo "== Motore d'attesa"
bash "$ORIGINE/installa-attesa.sh" giochi || echo "ATTENZIONE: il motore d'attesa non è stato installato: i giochi usano le attese di PHP."

echo
echo "Fatto. Il sito dei giochi è su https://wingabriel.net/giochi/ e WinGabriel usa https://wingabriel.net/giochi/giochi.php"
