#!/bin/bash
# Installa su wingabriel.net (Aruba, come root) il motore d'attesa (attesa.py) e gli manda le attese lunghe di giochi o chat.
#   bash installa-attesa.sh giochi|chat       (con BETA=1: solo la copia di prova, wingabriel.net/beta/...)
# Lo lanciano installa-giochi.sh e installa-chat.sh: non si lancia a mano. È ripetibile.
#
# Cosa fa:
#   - copia attesa.py in /usr/local/lib/wingabriel e crea il servizio wingabriel-attesa (utente wingabriel, solo 127.0.0.1:9701, sola lettura);
#   - ricorda in /etc/wingabriel-attesa/componenti quali percorsi gli sono affidati (giochi, giochi-beta, chat, chat-beta);
#   - per quei percorsi, e solo per le richieste che aspettano (stato con attesa, ascolta), nginx le manda al motore;
#     tutto il resto continua ad andare ad Apache e a PHP. Se il motore non risponde nginx le rimanda ad Apache come prima.
#     Sugli stessi percorsi nginx comprime le risposte JSON (gzip) e permette al massimo 60 richieste insieme da uno stesso indirizzo.
#   - prova la configurazione di nginx e la ricarica; se la prova non riesce torna com'era.
# La configurazione di nginx per wingabriel.net sta in /etc/nginx/gabriel/siti.conf: lo script ci aggiunge una sola riga (include) dentro
# il server 443. Se quel file viene rifatto da un altro script basta rilanciare questo, o un rilascio dei giochi o della chat.
set -euo pipefail

COMPONENTE="${1:-}"
BETA="${BETA:-}"
ORIGINE="$(cd "$(dirname "$0")" && pwd)"
CARTELLA_PROGRAMMA=/usr/local/lib/wingabriel
UNITA=/etc/systemd/system/wingabriel-attesa.service
STATO=/etc/wingabriel-attesa/componenti
MAPPE=/etc/nginx/gabriel/attesa-mappe.conf
SNIPPET=/etc/nginx/snippets/wingabriel-attesa.conf
SITI=/etc/nginx/gabriel/siti.conf

if [ "$(hostname)" != "wingabriel" ]; then
  echo "Questo script va lanciato su Aruba (wingabriel), non su $(hostname)"
  exit 1
fi
case "$COMPONENTE" in
  giochi|chat) ;;
  *) echo "Uso: installa-attesa.sh giochi|chat"; exit 1;;
esac
VOCE="$COMPONENTE"
if [ "$BETA" = 1 ]; then VOCE="$COMPONENTE-beta"; fi

echo "== Motore d'attesa ($VOCE)"
python3 -c "import ast,sys; ast.parse(open(sys.argv[1], encoding='utf-8').read())" "$ORIGINE/attesa.py"
install -d -m 755 -o root -g root "$CARTELLA_PROGRAMMA"
CAMBIATO=0
if ! cmp -s "$ORIGINE/attesa.py" "$CARTELLA_PROGRAMMA/attesa.py"; then
  install -m 644 -o root -g root "$ORIGINE/attesa.py" "$CARTELLA_PROGRAMMA/attesa.py"
  CAMBIATO=1
fi

cat > "$UNITA.nuovo" <<'UNITAEOF'
[Unit]
Description=Motore d'attesa di WinGabriel (giochi e chat)
After=network.target apache2.service

[Service]
Type=simple
User=wingabriel
Group=www-data
ExecStart=/usr/bin/python3 -I /usr/local/lib/wingabriel/attesa.py
Restart=always
RestartSec=2
LimitNOFILE=65535
MemoryMax=300M
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
ProtectKernelTunables=yes
ProtectControlGroups=yes
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
CapabilityBoundingSet=
SystemCallArchitectures=native

[Install]
WantedBy=multi-user.target
UNITAEOF
if ! cmp -s "$UNITA.nuovo" "$UNITA"; then
  mv "$UNITA.nuovo" "$UNITA"
  systemctl daemon-reload
  CAMBIATO=1
else
  rm -f "$UNITA.nuovo"
fi
systemctl enable wingabriel-attesa >/dev/null 2>&1
if systemctl is-active --quiet wingabriel-attesa; then
  if [ "$CAMBIATO" = 1 ]; then systemctl restart wingabriel-attesa; fi
else
  systemctl start wingabriel-attesa
fi
sleep 1
if ! curl -fs -m 5 http://127.0.0.1:9701/__stato >/dev/null; then
  echo "ATTENZIONE: il motore d'attesa non risponde su 127.0.0.1:9701 (journalctl -u wingabriel-attesa). Non cambio nginx."
  exit 1
fi
echo "il servizio wingabriel-attesa funziona"

echo "== nginx: le attese lunghe di $VOCE passano dal motore"
install -d -m 755 /etc/wingabriel-attesa
touch "$STATO"
grep -qx "$VOCE" "$STATO" || echo "$VOCE" >> "$STATO"

python3 - "$STATO" "$MAPPE" "$SNIPPET" "$SITI" <<'PYEOF'
import subprocess, sys

stato, mappe, snippet, siti = sys.argv[1:5]
RIGA_INCLUDE = '    include /etc/nginx/snippets/wingabriel-attesa.conf;'
PERCORSI = {
    'giochi': (r'^/giochi/giochi\.php$', 'giochi'),
    'giochi-beta': (r'^/beta/giochi/giochi\.php$', 'giochi'),
    'chat': (r'^/chat/chat\.php$', 'chat'),
    'chat-beta': (r'^/beta/chat/chat\.php$', 'chat'),
}
originali = {}   # file -> testo di prima (None se non c'era): serve solo per tornare indietro se nginx rifiuta, niente copie su disco


def leggi(p):
    try:
        with open(p, encoding='utf-8', newline='') as f:
            return f.read()
    except FileNotFoundError:
        return None


def scrivi(p, t):
    if p not in originali:
        originali[p] = leggi(p)
    with open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(t)


voci = [r.strip() for r in (leggi(stato) or '').splitlines() if r.strip() in PERCORSI]
usate = sorted(set(voci))

# ---- le mappe (livello http: nginx.conf include /etc/nginx/gabriel/*.conf)
nuove_mappe = '''# generato da installa-attesa.sh: non modificare a mano
upstream wg_attesa {
    server 127.0.0.1:9701;
    keepalive 64;
}
# solo le richieste che aspettano (o aprono un flusso di eventi) vanno al motore (attesa.py); le altre restano ad Apache e a PHP
map "$arg_a:$arg_attesa" $wg_attesa_giochi {
    default 0;
    "~^stato:([1-9]|1[0-9]|20)$" 1;
    "~^flusso:" 1;
}
map $arg_a $wg_attesa_chat {
    default 0;
    ascolta 1;
    flusso 1;
}
# al massimo 60 richieste insieme da uno stesso indirizzo (una famiglia, un ufficio): oltre, 429. Nessun utente normale ci arriva.
limit_conn_zone $binary_remote_addr zone=wg_conn:10m;
# Registrazioni di nuovi nomi utente: per ogni rete 5 subito e poi 1 al minuto (gli indirizzi IPv6 si raggruppano per rete /64).
# Il conteggio sta solo nella memoria di nginx: non si scrive su disco e non va in nessun registro.
map $remote_addr $wg_rete {
    "~^(?<v6>[0-9a-fA-F]{1,4}:[0-9a-fA-F]{1,4}:[0-9a-fA-F]{1,4}:[0-9a-fA-F]{1,4}):" $v6;
    default $remote_addr;
}
map "$uri:$arg_a" $wg_reg_chiave {
    default "";
    "~^/(beta/)?giochi/giochi[.]php:registra$" $wg_rete;
}
limit_req_zone $wg_reg_chiave zone=wg_reg:2m rate=1r/m;
# Chat: per ogni rete al massimo 120 richieste al minuto, con 120 di scorta (un utente normale ne fa poche decine). Solo in memoria di nginx.
map $uri $wg_chat_chiave {
    default "";
    "~^/(beta/)?chat/chat[.]php$" $wg_rete;
}
limit_req_zone $wg_chat_chiave zone=wg_chat:5m rate=120r/m;
'''
if leggi(mappe) != nuove_mappe:
    scrivi(mappe, nuove_mappe)

# ---- i percorsi
pezzi = ['# generato da installa-attesa.sh: non modificare a mano']
for v in usate:
    regola, nome = PERCORSI[v]
    pezzi.append('''location ~ %s {
    error_page 418 = @wg_apache;
    error_page 502 503 504 = @wg_apache;
    if ($wg_attesa_%s = 0) { return 418; }
    proxy_pass http://wg_attesa;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;   # il motore lo passa ad Apache (mod_remoteip): il relè della chat conta per rete
    proxy_connect_timeout 2s;
    proxy_send_timeout 30s;
    proxy_read_timeout 65s;   # i flussi di eventi danno segno di vita ogni 15 secondi
    proxy_buffering off;
    limit_conn wg_conn 60;
    limit_conn_status 429;
    limit_req zone=wg_chat burst=120 nodelay;
    limit_req_status 429;
    error_page 429 = @wg_troppe;
    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_min_length 400;
    gzip_types application/json;
}''' % (regola, nome))
pezzi.append('''location @wg_troppe {
    default_type application/json;
    return 429 '{"errore":"Troppe richieste da questa rete: riprova tra qualche minuto."}';
}''')
pezzi.append('''location @wg_apache {
    error_page 429 = @wg_troppe;
    limit_req zone=wg_reg burst=5 nodelay;
    limit_req zone=wg_chat burst=120 nodelay;
    limit_req_status 429;
    proxy_pass https://apache_wingabriel_net;
    include /etc/nginx/gabriel-proxy.inc;
    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_min_length 400;
    gzip_types application/json;
}''')
nuovo_snippet = '\n'.join(pezzi) + '\n'
if leggi(snippet) != nuovo_snippet:
    scrivi(snippet, nuovo_snippet)

# ---- una riga dentro il server 443 di wingabriel.net
testo = leggi(siti)
if testo is None:
    print('ATTENZIONE: manca ' + siti + ': nginx non è cambiato.')
    sys.exit(1)
righe = testo.split('\n')
inizio = None
for i, r in enumerate(righe):
    if r.strip().startswith('listen 443 ssl') and any('server_name wingabriel.net' in x for x in righe[i:i + 6]):
        inizio = i
        break
if inizio is None:
    print('ATTENZIONE: non trovo il server 443 di wingabriel.net in ' + siti + ': nginx non è cambiato.')
    sys.exit(1)
fine = next((j for j in range(inizio, len(righe)) if righe[j].strip() == '}' and not righe[j].startswith(' ')), len(righe))
blocco = righe[inizio:fine]
if RIGA_INCLUDE not in blocco:
    dopo = next((k for k in range(inizio, fine) if 'snippets/biblioteca-libri.conf' in righe[k]), None)
    if dopo is None:
        print('ATTENZIONE: nel server 443 di wingabriel.net non trovo la riga di riferimento: nginx non è cambiato.')
        sys.exit(1)
    righe.insert(dopo + 1, RIGA_INCLUDE)
    scrivi(siti, '\n'.join(righe))

# ---- prova e ricarica
a = subprocess.run(['nginx', '-t'], capture_output=True, text=True)
if a.returncode != 0:
    print("La prova di nginx non è riuscita, torno com'era:\n" + a.stderr)
    for p, t in originali.items():
        if t is None:
            import os
            os.remove(p)
        else:
            with open(p, 'w', encoding='utf-8', newline='') as f:
                f.write(t)
    sys.exit(1)
if originali:
    subprocess.run(['systemctl', 'reload', 'nginx'], check=True)
    print('nginx ricaricato; percorsi affidati al motore: ' + ', '.join(usate))
else:
    print('nginx già a posto; percorsi affidati al motore: ' + ', '.join(usate))
PYEOF

echo "Fatto: il motore d'attesa tiene le attese di: $(tr '\n' ' ' < "$STATO")"
