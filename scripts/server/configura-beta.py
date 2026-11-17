#!/usr/bin/env python3
# Prepara nginx e Apache per la copia beta di giochi e chat (wingabriel.net/beta/giochi e wingabriel.net/beta/chat), come per quelle vere:
# niente registro delle richieste (così non restano gli indirizzi IP di chi gioca o scrive) e niente copia veloce di nginx.
# Ripetibile. Se non riesce a mettere a posto le righe esce con errore, e l'installazione della beta si ferma prima di copiare i file.
# Lo lanciano installa-giochi.sh e installa-chat.sh quando si installa la beta (BETA=1).
import subprocess, sys

NGINX = '/etc/nginx/nginx.conf'
APACHE = '/etc/apache2/sites-available/wingabriel.net.conf'
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


RIGA_NGINX = '        "~^/beta/(giochi/giochi|chat/chat)\\.php" 0;\n'
COPIA_VELOCE = '|chat/chat\\.php|chat/index\\.php)" 1;'
COPIA_VELOCE_BETA = '|chat/chat\\.php|chat/index\\.php|beta/giochi/giochi\\.php|beta/chat/chat\\.php|beta/chat/index\\.php)" 1;'
RIGA_APACHE = '    SetEnvIf Request_URI "^/beta/(giochi/giochi|chat/chat)\\.php" nolog_conta'
ANCORA_APACHE = '    SetEnvIf Request_URI "^/giochi/giochi\\.php" nolog_conta'


def nginx_conf(t):
    if RIGA_NGINX not in t:
        ancora = '        "~^/chat/chat\\.php" 0;\n'
        if ancora not in t:
            print("ERRORE: in nginx.conf non trovo la riga di chat.php (installa prima la chat vera).")
            sys.exit(1)
        t = t.replace(ancora, ancora + RIGA_NGINX, 1)
    if 'beta/giochi/giochi\\.php|' not in t:
        if COPIA_VELOCE not in t:
            print("ERRORE: in nginx.conf non trovo l'elenco della copia veloce (installa prima giochi e chat veri).")
            sys.exit(1)
        t = t.replace(COPIA_VELOCE, COPIA_VELOCE_BETA, 1)
    return t


def apache_conf(t):
    righe = t.split('\n')
    uscita = []
    trovate = 0
    for i, r in enumerate(righe):
        uscita.append(r)
        if r == ANCORA_APACHE:
            trovate += 1
            if RIGA_APACHE not in righe[i + 1:i + 3]:
                uscita.append(RIGA_APACHE)
    if trovate == 0:
        print("ERRORE: in Apache non trovo la riga di giochi.php (installa prima i giochi veri).")
        sys.exit(1)
    return '\n'.join(uscita)


modifica(NGINX, nginx_conf)
modifica(APACHE, apache_conf)

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
