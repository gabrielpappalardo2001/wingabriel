#!/bin/bash
# Installa su wingabriel.net (Aruba, come root) la chat di WinGabriel (WGChat): il relè chat.php e la pagina web.
#   chat.php, index.php, chat.js, chat.css, IMPRONTE.txt, IMPRONTE.firmate.json, più pwa.js, sw.js, manifest e icone (PWA) in /var/www/html/wingabriel.net/chat
# Il relè non ha database né dischi: tiene i blocchi cifrati solo in memoria (/dev/shm/wingabriel-chat) e solo per pochi secondi.
# Protezione dei dati: le richieste a chat.php (e a giochi.php) non devono finire nei registri, che contengono l'indirizzo IP.
# Lo script le toglie dal registro di nginx, dalla copia veloce di nginx e dal registro di OGNI virtual host di Apache
# (quello su 8080 e quello su 8443, su cui arriva il traffico HTTPS). Si può rilanciare quando si vuole.
# Le attese lunghe (fino a 20 secondi) non occupano più un processo di php-fpm per ogni persona: le tiene il motore d'attesa
# (installa-attesa.sh, che questo script lancia). Il limite del pool di wingabriel.net resta a 40, per il lavoro vero e per quando il
# motore non c'è (a richiesta, i processi fermi non costano memoria).
set -euo pipefail

ORIGINE="$(cd "$(dirname "$0")" && pwd)"
SITO=/var/www/html/wingabriel.net
DESTINAZIONE="$SITO/chat"
BETA="${BETA:-}"
UTENTE=wingabriel
GRUPPO=www-data
NGINX=/etc/nginx/nginx.conf
APACHE_SITO=/etc/apache2/sites-available/wingabriel.net.conf
POOL=/etc/php/8.3/fpm/pool.d/wingabriel.net.conf

if [ "$(hostname)" != "wingabriel" ]; then
  echo "Questo script va lanciato su Aruba (wingabriel), non su $(hostname)"
  exit 1
fi

if [ "$BETA" = 1 ]; then
  DESTINAZIONE="$SITO/beta/chat"
  echo "== COPIA BETA: $DESTINAZIONE"
  python3 "$ORIGINE/configura-beta.py"   # prima di tutto: senza registro e senza copia veloce, o non si installa
  install -d -m 755 -o "$UTENTE" -g "$GRUPPO" "$SITO/beta"
fi

echo "== Controllo di chat.php"
php -l "$ORIGINE/chat.php" | tail -1
php -l "$ORIGINE/index.php" | tail -1

echo "== File"
install -d -m 755 -o "$UTENTE" -g "$GRUPPO" "$DESTINAZIONE"
for f in chat.php index.php chat.js chat.css IMPRONTE.txt IMPRONTE.firmate.json pwa.js sw.js manifest.webmanifest icona-192.png icona-512.png icona-maskable-512.png apple-touch-icon.png; do
  install -m 644 -o "$UTENTE" -g "$GRUPPO" "$ORIGINE/$f" "$DESTINAZIONE/$f"
done

# i suoni delle notifiche (un messaggio, una richiesta di contatto)
install -d -m 755 -o "$UTENTE" -g "$GRUPPO" "$DESTINAZIONE/suoni"
for f in "$ORIGINE"/suoni/*.wav; do
  install -m 644 -o "$UTENTE" -g "$GRUPPO" "$f" "$DESTINAZIONE/suoni/$(basename "$f")"
done

if [ "$BETA" = 1 ]; then
  # le attese lunghe passano dal motore d'attesa (se non va, la chat funziona lo stesso, con più processi php)
  bash "$ORIGINE/installa-attesa.sh" chat || echo "ATTENZIONE: il motore d'attesa non è stato installato: la chat usa le attese di PHP."
  echo
  echo "Fatto. La copia beta della chat è su https://wingabriel.net/beta/chat/"
  exit 0
fi

echo "== nginx, Apache e php-fpm (niente registro delle richieste a chat.php e giochi.php)"
python3 - "$NGINX" "$APACHE_SITO" "$POOL" <<'PYEOF'
import re, subprocess, sys
nginx, apache, pool = sys.argv[1], sys.argv[2], sys.argv[3]
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
    if 'chat/chat\\.php' not in t:
        t = t.replace('|giochi/giochi\\.php)" 1;', '|giochi/giochi\\.php|chat/chat\\.php|chat/index\\.php)" 1;', 1)
        t = t.replace('        "~^/giochi/giochi\\.php" 0;\n', '        "~^/giochi/giochi\\.php" 0;\n        "~^/chat/chat\\.php" 0;\n', 1)
    return t

RIGHE = ['    SetEnvIf Request_URI "^/giochi/giochi\\.php" nolog_conta',
         '    SetEnvIf Request_URI "^/chat/chat\\.php" nolog_conta']

def apache_conf(t):
    # dopo la riga di conta.php, in ogni virtual host, ci devono essere anche quelle di giochi.php e chat.php
    righe = t.split('\n')
    uscita = []
    for i, r in enumerate(righe):
        uscita.append(r)
        if r == '    SetEnvIf Request_URI "^/conta\\.php" nolog_conta':
            seguenti = righe[i + 1:i + 5]
            for nuova in RIGHE:
                if nuova not in seguenti:
                    uscita.append(nuova)
    return '\n'.join(uscita)

def pool_conf(t):
    m = re.search(r'^pm\.max_children\s*=\s*(\d+)', t, re.M)
    if m and int(m.group(1)) < 40:
        t = t[:m.start()] + 'pm.max_children = 40' + t[m.end():]
    return t

modifica(nginx, nginx_conf)
modifica(apache, apache_conf)
modifica(pool, pool_conf)

def prova():
    a = subprocess.run(['nginx', '-t'], capture_output=True, text=True)
    b = subprocess.run(['apache2ctl', 'configtest'], capture_output=True, text=True)
    c = subprocess.run(['php-fpm8.3', '-t'], capture_output=True, text=True)
    return a.returncode == 0 and b.returncode == 0 and c.returncode == 0, a.stderr + b.stderr + c.stderr

riuscita, testo = prova()
if not riuscita:
    print('La prova della configurazione non è riuscita, torno com\'era:\n' + testo)
    for p, t in originali.items():
        scrivi(p, t)
    sys.exit(1)
if originali:
    subprocess.run(['systemctl', 'reload', 'nginx'], check=True)
    subprocess.run(['systemctl', 'reload', 'apache2'], check=True)
    subprocess.run(['systemctl', 'reload', 'php8.3-fpm'], check=True)
    print('nginx, Apache e php-fpm ricaricati')
PYEOF

echo "== Motore d'attesa"
bash "$ORIGINE/installa-attesa.sh" chat || echo "ATTENZIONE: il motore d'attesa non è stato installato: la chat usa le attese di PHP."

echo
echo "Fatto. La chat è su https://wingabriel.net/chat/ e WinGabriel usa https://wingabriel.net/chat/chat.php"
