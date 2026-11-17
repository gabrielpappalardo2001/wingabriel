#!/bin/bash
# Installa su wingabriel.net (Aruba, come root) il ricevitore delle segnalazioni anonime di WinGabriel:
#   segnalazioni.php in /var/www/html/wingabriel.net/segnalazioni (WinGabriel usa https://wingabriel.net/segnalazioni/segnalazioni.php)
#   la coda in /var/lib/wingabriel-segnalazioni/coda, dove segnalazioni.php (utente wingabriel) lascia un file per ogni segnalazione
#   invia-segnalazioni.php in /usr/local/bin e il timer systemd wingabriel-segnalazioni (ogni minuto, come root): manda ogni segnalazione per mail
#   all'indirizzo scritto in /etc/wingabriel-segnalazioni.destinatario con msmtp (/etc/msmtprc, leggibile solo da root) e cancella il file.
# Protezione dei dati: nessun indirizzo IP nei registri (come per i giochi). Si può rilanciare quando si vuole.
set -euo pipefail

ORIGINE="$(cd "$(dirname "$0")" && pwd)"
SITO=/var/www/html/wingabriel.net
DESTINAZIONE="$SITO/segnalazioni"
DATI=/var/lib/wingabriel-segnalazioni
UTENTE=wingabriel
GRUPPO=www-data
NGINX=/etc/nginx/nginx.conf
APACHE_SITO=/etc/apache2/sites-available/wingabriel.net.conf

if [ "$(hostname)" != "wingabriel" ]; then
  echo "Questo script va lanciato su Aruba (wingabriel), non su $(hostname)"
  exit 1
fi

echo "== Controllo dei file PHP"
php -l "$ORIGINE/segnalazioni.php" | tail -1
php -l "$ORIGINE/invia-segnalazioni.php" | tail -1

echo "== Cartelle e file"
install -d -m 755 -o "$UTENTE" -g "$GRUPPO" "$DESTINAZIONE"
install -d -m 770 -o "$UTENTE" -g "$GRUPPO" "$DATI" "$DATI/coda"
install -m 644 -o "$UTENTE" -g "$GRUPPO" "$ORIGINE/segnalazioni.php" "$DESTINAZIONE/segnalazioni.php"
install -m 755 -o root -g root "$ORIGINE/invia-segnalazioni.php" /usr/local/bin/invia-segnalazioni
sed -i 's/\r$//' /usr/local/bin/invia-segnalazioni

echo "== Timer"
cat > /etc/systemd/system/wingabriel-segnalazioni.service <<'UNIT'
[Unit]
Description=Invio per mail delle segnalazioni di WinGabriel

[Service]
Type=oneshot
ExecStart=/usr/bin/php /usr/local/bin/invia-segnalazioni
Nice=10
PrivateTmp=true
NoNewPrivileges=true
UNIT
cat > /etc/systemd/system/wingabriel-segnalazioni.timer <<'UNIT'
[Unit]
Description=Invio per mail delle segnalazioni di WinGabriel, ogni minuto

[Timer]
OnBootSec=1min
OnUnitActiveSec=1min
AccuracySec=10s

[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now wingabriel-segnalazioni.timer

echo "== nginx e Apache (niente registro né copia veloce per segnalazioni.php)"
python3 - "$NGINX" "$APACHE_SITO" <<'PYEOF'
import subprocess, sys
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
    if 'segnalazioni' in t:
        return t
    t = t.replace('|giochi/giochi\\.php|', '|giochi/giochi\\.php|segnalazioni/segnalazioni\\.php|', 1)
    t = t.replace('        "~^/giochi/giochi\\.php" 0;\n', '        "~^/giochi/giochi\\.php" 0;\n        "~^/segnalazioni/segnalazioni\\.php" 0;\n', 1)
    return t

def apache_conf(t):
    riga_giochi = '    SetEnvIf Request_URI "^/giochi/giochi\\.php" nolog_conta'
    riga_nuova = '    SetEnvIf Request_URI "^/segnalazioni/segnalazioni\\.php" nolog_conta'
    righe = t.split(chr(10))
    uscita = []
    for i, r in enumerate(righe):
        uscita.append(r)
        if r == riga_giochi and riga_nuova not in righe[i + 1:i + 4]:
            uscita.append(riga_nuova)
    return chr(10).join(uscita)

modifica(nginx, nginx_conf)
modifica(apache, apache_conf)

if 'segnalazioni' not in leggi(nginx) or leggi(apache).count('segnalazioni/segnalazioni') < leggi(apache).count('giochi/giochi\\.php" nolog_conta'):
    print('ATTENZIONE: non ho trovato le righe di giochi.php da cui partire; il registro di segnalazioni.php va escluso a mano.')
    for p, t in originali.items():
        scrivi(p, t)
    sys.exit(1)

a = subprocess.run(['nginx', '-t'], capture_output=True, text=True)
b = subprocess.run(['apache2ctl', 'configtest'], capture_output=True, text=True)
if a.returncode != 0 or b.returncode != 0:
    print("La prova della configurazione non è riuscita, torno com'era:\n" + a.stderr + b.stderr)
    for p, t in originali.items():
        scrivi(p, t)
    sys.exit(1)
if originali:
    subprocess.run(['systemctl', 'reload', 'nginx'], check=True)
    subprocess.run(['systemctl', 'reload', 'apache2'], check=True)
    print('nginx e Apache ricaricati')
PYEOF

echo
echo "Fatto. Le segnalazioni arrivano su https://wingabriel.net/segnalazioni/segnalazioni.php e partono per mail ogni minuto."
