' ============================================================================================
' YoutubeOnline - iscrizioni e cronologia dell'account Google, lette dal browser di WinGabriel
' ============================================================================================
'
' Se nel browser di WinGabriel c'è già l'accesso a YouTube, iscrizioni e cronologia del vero account si
' leggono aprendo la pagina di YouTube in una scheda nascosta del browser (WGPaginaNascosta): la pagina
' contiene già l'elenco, e per le pagine successive è la pagina stessa a chiederle a YouTube, con la
' sessione di sempre. yt-dlp non vede mai cookie né password: dopo, ai video pensa lui, senza account.
' Se YouTube cambia la struttura della pagina la lettura può fallire: allora chi chiama offre l'elenco
' salvato sul computer (UltimoErrore dice cosa è andato storto).
'
'   If Await YoutubeOnline.HaAccessoAsync() Then ...
'   Dim canali As List(Of YoutubeOnline.CanaleOnline) = Await YoutubeOnline.IscrizioniAsync()
'   Dim video As List(Of YoutubeOnline.VideoOnline) = Await YoutubeOnline.CronologiaAsync(300)
'   Dim riuscito As Boolean = Await YoutubeOnline.IscriviAsync(idCanale)    ' e DisiscriviAsync
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Friend NotInheritable Class YoutubeOnline

    Public Class CanaleOnline
        Public Id As String
        Public Titolo As String
        Public ReadOnly Property Url As String
            Get
                Return "https://www.youtube.com/channel/" & Id
            End Get
        End Property
    End Class

    Public Class PlaylistOnline
        Public Id As String
        Public Titolo As String
        ''' <summary>Quanti video, come lo scrive YouTube ("12 video"); vuoto se non si sa.</summary>
        Public Quanti As String
        Public ReadOnly Property Url As String
            Get
                Return "https://www.youtube.com/playlist?list=" & Id
            End Get
        End Property
    End Class

    Public Class VideoOnline
        Public Id As String
        Public Titolo As String
        Public Canale As String
        Public IdCanale As String
        Public Durata As String
        ''' <summary>L'identificativo del video dentro la playlist (serve per toglierlo); vuoto se non è in una playlist.</summary>
        Public SetId As String
        ''' <summary>Quando è stato pubblicato, come lo scrive YouTube ("3 ore fa"); vuoto se non si sa.</summary>
        Public Pubblicato As String
        ''' <summary>La data esatta di pubblicazione, quando si è riusciti a saperla dal feed del canale; YouTube dà solo "3 giorni fa".</summary>
        Public PubblicatoIl As DateTime?
        Public ReadOnly Property Url As String
            Get
                Return "https://www.youtube.com/watch?v=" & Id
            End Get
        End Property
    End Class

    Private Shared ReadOnly log As New Logger()
    Private Const Sito As String = "https://www.youtube.com"
    Private Shared ReadOnly NomiCookie As String() = {"SAPISID", "__Secure-3PAPISID"}
    Private Shared _accessoNoto As Boolean
    Private Shared _istanteAccesso As DateTime = DateTime.MinValue

    ''' <summary>Cosa è andato storto nell'ultima lettura, in una frase pronta da dire.</summary>
    Public Shared Property UltimoErrore As String = ""

    Private Shared ReadOnly Script As String = String.Join(vbLf, New String() {
            "(async function(modo, massimo, idNavigazione){",
            "  try {",
            "    var inizio = Date.now();",
            "    var pausa = function(ms) { return new Promise(function(r) { setTimeout(r, ms); }); };",
            "    if (location.hostname.indexOf('consent.') === 0 || location.pathname.indexOf('/consent') === 0) { return JSON.stringify({errore: 'consenso'}); }",
            "    var d = null, cfg = null;",
            "    for (var attesa = 0; attesa < 48; attesa++) {",
            "      if (!cfg && window.ytcfg && window.ytcfg.get && window.ytcfg.get('INNERTUBE_CONTEXT')) { cfg = window.ytcfg; }",
            "      if (!d && window.ytInitialData) { d = window.ytInitialData; }",
            "      if (cfg && d) { break; }",
            "      if (cfg && attesa >= 12) { break; }",
            "      await pausa(250);",
            "    }",
            "    var html = '';",
            "    var daHtml = function(nome) {",
            "      var m = html.match(new RegExp('""' + nome + '"":""([^""]*)""'));",
            "      return m ? m[1] : '';",
            "    };",
            "    if (!cfg) {",
            "      try {",
            "        var rp = await fetch(location.href, {credentials: 'include'});",
            "        html = await rp.text();",
            "      } catch (e0) { html = ''; }",
            "      var versioneHtml = daHtml('INNERTUBE_CONTEXT_CLIENT_VERSION');",
            "      if (!versioneHtml) { return JSON.stringify({errore: 'pagina', dettaglio: 'senza configurazione, ' + location.href}); }",
            "      var visitatore = daHtml('VISITOR_DATA');",
            "      var valori = {",
            "        LOGGED_IN: html.indexOf('""LOGGED_IN"":true') >= 0,",
            "        INNERTUBE_API_KEY: daHtml('INNERTUBE_API_KEY'),",
            "        INNERTUBE_CONTEXT_CLIENT_VERSION: versioneHtml,",
            "        VISITOR_DATA: visitatore,",
            "        INNERTUBE_CONTEXT: {client: {hl: 'it', gl: 'IT', clientName: 'WEB', clientVersion: versioneHtml, visitorData: visitatore}}",
            "      };",
            "      cfg = {get: function(k) { return valori[k]; }};",
            "    }",
            "    if (!cfg.get('LOGGED_IN')) { return JSON.stringify({errore: 'accesso'}); }",
            "    if (!d && html) {",
            "      var md = html.match(/ytInitialData\s*=\s*(\{[\s\S]*?\});\s*<\/script>/);",
            "      if (md) { try { d = JSON.parse(md[1]); } catch (e1) { d = null; } }",
            "    }",
            "    var testo = function(x) {",
            "      if (!x) { return ''; }",
            "      if (typeof x === 'string') { return x; }",
            "      if (x.simpleText) { return x.simpleText; }",
            "      if (x.content) { return x.content; }",
            "      if (x.runs) { return x.runs.map(function(r) { return r.text; }).join(''); }",
            "      return '';",
            "    };",
            "    var elementi = [];",
            "    var visti = {};",
            "    var token = '';",
            "    var aggiungiCanale = function(id, titolo) {",
            "      if (!id || !titolo || visti[id]) { return; }",
            "      visti[id] = 1;",
            "      elementi.push({id: id, t: titolo});",
            "    };",
            "    var aggiungiVideo = function(id, titolo, canale, idCanale, durata, quando, setId) {",
            "      if (!id || !titolo || visti[id]) { return; }",
            "      visti[id] = 1;",
            "      elementi.push({id: id, t: titolo, c: canale || '', cid: idCanale || '', d: durata || '', p: quando || '', s: setId || ''});",
            "    };",
            "    var aggiungiPlaylist = function(id, titolo, quanti) {",
            "      if (!id || !titolo || visti[id]) { return; }",
            "      visti[id] = 1;",
            "      elementi.push({id: id, t: titolo, n: quanti || ''});",
            "    };",
            "    var cercaBadge = function(n, prof) {",
            "      if (!n || typeof n !== 'object' || prof > 25) { return ''; }",
            "      if (n.thumbnailBadgeViewModel && n.thumbnailBadgeViewModel.text) { return testo(n.thumbnailBadgeViewModel.text); }",
            "      var k, r;",
            "      for (k in n) {",
            "        if (Object.prototype.hasOwnProperty.call(n, k)) {",
            "          r = cercaBadge(n[k], prof + 1);",
            "          if (r) { return r; }",
            "        }",
            "      }",
            "      return '';",
            "    };",
            "    var idCanaleDi = function(x) {",
            "      try { return x.runs[0].navigationEndpoint.browseEndpoint.browseId || ''; } catch (e) { return ''; }",
            "    };",
            "    var trovaCanale = function(n, prof) {",
            "      if (!n || typeof n !== 'object' || prof > 25) { return ''; }",
            "      if (n.browseEndpoint && typeof n.browseEndpoint.browseId === 'string' && n.browseEndpoint.browseId.indexOf('UC') === 0) { return n.browseEndpoint.browseId; }",
            "      var k, r;",
            "      for (k in n) {",
            "        if (Object.prototype.hasOwnProperty.call(n, k)) {",
            "          r = trovaCanale(n[k], prof + 1);",
            "          if (r) { return r; }",
            "        }",
            "      }",
            "      return '';",
            "    };",
            "    var cammina = function(n, prof) {",
            "      if (!n || typeof n !== 'object' || prof > 60) { return; }",
            "      var i, k;",
            "      if (Array.isArray(n)) {",
            "        for (i = 0; i < n.length; i++) { cammina(n[i], prof + 1); }",
            "        return;",
            "      }",
            "      try {",
            "        if (modo === 'iscrizioni') {",
            "          var ch = n.channelRenderer || n.gridChannelRenderer;",
            "          if (ch) { aggiungiCanale(ch.channelId, testo(ch.title)); }",
            "          if (n.lockupViewModel && String(n.lockupViewModel.contentType).indexOf('CHANNEL') >= 0) {",
            "            var mc = n.lockupViewModel.metadata && n.lockupViewModel.metadata.lockupMetadataViewModel;",
            "            aggiungiCanale(n.lockupViewModel.contentId, mc ? testo(mc.title) : '');",
            "          }",
            "        } else if (modo === 'playlist') {",
            "          var pl = n.playlistRenderer || n.gridPlaylistRenderer;",
            "          if (pl) { aggiungiPlaylist(pl.playlistId, testo(pl.title), testo(pl.videoCountText) || testo(pl.videoCountShortText) || String(pl.videoCount || '')); }",
            "          if (n.lockupViewModel && String(n.lockupViewModel.contentType).indexOf('PLAYLIST') >= 0) {",
            "            var lp = n.lockupViewModel;",
            "            var mp = lp.metadata && lp.metadata.lockupMetadataViewModel;",
            "            aggiungiPlaylist(lp.contentId, mp ? testo(mp.title) : '', cercaBadge(lp, 0));",
            "          }",
            "        } else {",
            "          if (n.playlistVideoRenderer) {",
            "            var pv = n.playlistVideoRenderer;",
            "            aggiungiVideo(pv.videoId, testo(pv.title), testo(pv.shortBylineText), idCanaleDi(pv.shortBylineText) || trovaCanale(pv, 0), testo(pv.lengthText), '', pv.setVideoId);",
            "          }",
            "          var vr = n.videoRenderer || n.gridVideoRenderer || n.compactVideoRenderer || n.videoWithContextRenderer;",
            "          if (vr) {",
            "            var chi = vr.ownerText || vr.longBylineText || vr.shortBylineText;",
            "            aggiungiVideo(vr.videoId, testo(vr.title) || testo(vr.headline), testo(chi), idCanaleDi(chi) || trovaCanale(vr, 0), testo(vr.lengthText), testo(vr.publishedTimeText));",
            "          }",
            "          if (n.lockupViewModel && n.lockupViewModel.contentType === 'LOCKUP_CONTENT_TYPE_VIDEO') {",
            "            var l = n.lockupViewModel;",
            "            var m = l.metadata && l.metadata.lockupMetadataViewModel;",
            "            var canale = '', quando = '';",
            "            try {",
            "              var righe = m.metadata.contentMetadataViewModel.metadataRows;",
            "              canale = testo(righe[0].metadataParts[0].text);",
            "              var ultima = righe[righe.length - 1].metadataParts;",
            "              if (righe.length > 1) { quando = testo(ultima[ultima.length - 1].text); }",
            "            } catch (e1) { }",
            "            aggiungiVideo(l.contentId, m ? testo(m.title) : '', canale, trovaCanale(l, 0), cercaBadge(l, 0), quando);",
            "          }",
            "          if (n.shortsLockupViewModel) {",
            "            var sh = n.shortsLockupViewModel;",
            "            var idShort = '', titoloShort = '';",
            "            try { idShort = sh.onTap.innertubeCommand.reelWatchEndpoint.videoId; } catch (e3) { }",
            "            try { titoloShort = testo(sh.overlayMetadata.primaryText); } catch (e4) { }",
            "            aggiungiVideo(idShort, titoloShort, '', '', '');",
            "          }",
            "        }",
            "        if (n.continuationCommand && n.continuationCommand.token) { token = n.continuationCommand.token; }",
            "      } catch (e2) { }",
            "      for (k in n) {",
            "        if (Object.prototype.hasOwnProperty.call(n, k)) { cammina(n[k], prof + 1); }",
            "      }",
            "    };",
            "    var sha1 = async function(s) {",
            "      var b = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(s));",
            "      return Array.prototype.map.call(new Uint8Array(b), function(x) { return ('0' + x.toString(16)).slice(-2); }).join('');",
            "    };",
            "    var cookie = function(nome) {",
            "      var r = document.cookie.match(new RegExp('(?:^|; )' + nome + '=([^;]+)'));",
            "      return r ? r[1] : '';",
            "    };",
            "    var chiave = cfg.get('INNERTUBE_API_KEY');",
            "    var contesto = cfg.get('INNERTUBE_CONTEXT');",
            "    var versione = cfg.get('INNERTUBE_CONTEXT_CLIENT_VERSION');",
            "    var intestazioni = async function() {",
            "      var h = {'Content-Type': 'application/json', 'X-Origin': location.origin, 'X-Goog-AuthUser': '0',",
            "               'X-Youtube-Client-Name': '1', 'X-Youtube-Client-Version': versione};",
            "      var vd = cfg.get('VISITOR_DATA');",
            "      if (vd) { h['X-Goog-Visitor-Id'] = vd; }",
            "      var sid = cookie('SAPISID') || cookie('__Secure-3PAPISID');",
            "      if (sid) {",
            "        var ts = Math.floor(Date.now() / 1000);",
            "        h['Authorization'] = 'SAPISIDHASH ' + ts + '_' + await sha1(ts + ' ' + sid + ' ' + location.origin);",
            "      }",
            "      return h;",
            "    };",
            "    var chiedi = async function(corpo) {",
            "      for (var tentativo = 0; tentativo < 2; tentativo++) {",
            "        var risposta = await fetch('/youtubei/v1/browse?prettyPrint=false' + (chiave ? '&key=' + chiave : ''), {",
            "          method: 'POST', credentials: 'include', headers: await intestazioni(), body: JSON.stringify(corpo)",
            "        });",
            "        if (risposta.ok) { return await risposta.json(); }",
            "        if (risposta.status < 500 && risposta.status !== 429) { return null; }",
            "        await pausa(1000);",
            "      }",
            "      return null;",
            "    };",
            "    if (d) { cammina(d, 0); }",
            "    // Se i dati della pagina mancano o non si riconoscono (YouTube cambia spesso la pagina), si chiede l'elenco direttamente a YouTube.",
            "    if (elementi.length === 0 && idNavigazione) {",
            "      var iniziale = await chiedi({context: contesto, browseId: idNavigazione});",
            "      if (iniziale) { cammina(iniziale, 0); }",
            "      else if (!d) { return JSON.stringify({errore: 'pagina', dettaglio: 'senza dati, ' + location.href}); }",
            "    }",
            "    var pagine = 0;",
            "    while (token && elementi.length < massimo && pagine < 80 && Date.now() - inizio < 80000) {",
            "      var t = token;",
            "      token = '';",
            "      var seguente = await chiedi({context: contesto, continuation: t});",
            "      if (!seguente) { break; }",
            "      cammina(seguente, 0);",
            "      pagine++;",
            "    }",
            "    return JSON.stringify({ok: true, elementi: elementi.slice(0, massimo), altri: token !== '' || elementi.length > massimo});",
            "  } catch (e) {",
            "    return JSON.stringify({errore: 'script', dettaglio: String(e)});",
            "  }",
            "})('@MODO@', @MASSIMO@, '@NAVIGAZIONE@')"})

    ' Iscriversi e annullare l'iscrizione: la pagina di un canale chiede a YouTube di farlo, con la sessione
    ' del browser (lo stesso che fa il pulsante Iscriviti). Poi si rilegge l'elenco dei canali per verificare.
    Private Shared ReadOnly ScriptAzione As String = String.Join(vbLf, New String() {
            "(async function(azione, id){",
            "  try {",
            "    var pausa = function(ms) { return new Promise(function(r) { setTimeout(r, ms); }); };",
            "    var cfg = null;",
            "    for (var attesa = 0; attesa < 40; attesa++) {",
            "      if (window.ytcfg && window.ytcfg.get && window.ytcfg.get('INNERTUBE_CONTEXT')) { cfg = window.ytcfg; break; }",
            "      await pausa(250);",
            "    }",
            "    if (location.hostname.indexOf('consent.') === 0) { return JSON.stringify({errore: 'consenso'}); }",
            "    if (!cfg) { return JSON.stringify({errore: 'pagina', dettaglio: location.href}); }",
            "    if (!cfg.get('LOGGED_IN')) { return JSON.stringify({errore: 'accesso'}); }",
            "    var sha1 = async function(s) {",
            "      var b = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(s));",
            "      return Array.prototype.map.call(new Uint8Array(b), function(x) { return ('0' + x.toString(16)).slice(-2); }).join('');",
            "    };",
            "    var cookie = function(nome) {",
            "      var r = document.cookie.match(new RegExp('(?:^|; )' + nome + '=([^;]+)'));",
            "      return r ? r[1] : '';",
            "    };",
            "    var versione = cfg.get('INNERTUBE_CONTEXT_CLIENT_VERSION');",
            "    var h = {'Content-Type': 'application/json', 'X-Origin': location.origin, 'X-Goog-AuthUser': '0',",
            "             'X-Youtube-Client-Name': '1', 'X-Youtube-Client-Version': versione};",
            "    var vd = cfg.get('VISITOR_DATA');",
            "    if (vd) { h['X-Goog-Visitor-Id'] = vd; }",
            "    var sid = cookie('SAPISID') || cookie('__Secure-3PAPISID');",
            "    if (sid) {",
            "      var ts = Math.floor(Date.now() / 1000);",
            "      h['Authorization'] = 'SAPISIDHASH ' + ts + '_' + await sha1(ts + ' ' + sid + ' ' + location.origin);",
            "    }",
            "    var chiave = cfg.get('INNERTUBE_API_KEY');",
            "    var indirizzoApi = function(percorso) { return '/youtubei/v1/' + percorso + '?prettyPrint=false' + (chiave ? '&key=' + chiave : ''); };",
            "    var iscriviti = (azione === 'iscriviti');",
            "    var corpo = {context: cfg.get('INNERTUBE_CONTEXT'), channelIds: [id]};",
            "    if (iscriviti) { corpo.params = 'EgIIAhgA'; }",
            "    var risposta = await fetch(indirizzoApi('subscription/' + (iscriviti ? 'subscribe' : 'unsubscribe')), {",
            "      method: 'POST', credentials: 'include', headers: h, body: JSON.stringify(corpo)",
            "    });",
            "    return JSON.stringify({ok: risposta.ok, stato: risposta.status});",
            "  } catch (e) {",
            "    return JSON.stringify({errore: 'script', dettaglio: String(e)});",
            "  }",
            "})('@AZIONE@', '@ID@')"})

    ' Una chiamata qualsiasi a YouTube (mi piace, playlist...) con la sessione del browser: percorso dopo
    ' /youtubei/v1/ e corpo in JSON senza il contesto, che aggiunge la pagina.
    Private Shared ReadOnly ScriptApi As String = String.Join(vbLf, New String() {
            "(async function(percorso, corpo){",
            "  try {",
            "    var pausa = function(ms) { return new Promise(function(r) { setTimeout(r, ms); }); };",
            "    var cfg = null;",
            "    for (var attesa = 0; attesa < 40; attesa++) {",
            "      if (window.ytcfg && window.ytcfg.get && window.ytcfg.get('INNERTUBE_CONTEXT')) { cfg = window.ytcfg; break; }",
            "      await pausa(250);",
            "    }",
            "    if (location.hostname.indexOf('consent.') === 0) { return JSON.stringify({errore: 'consenso'}); }",
            "    if (!cfg) { return JSON.stringify({errore: 'pagina', dettaglio: location.href}); }",
            "    if (!cfg.get('LOGGED_IN')) { return JSON.stringify({errore: 'accesso'}); }",
            "    var sha1 = async function(s) {",
            "      var b = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(s));",
            "      return Array.prototype.map.call(new Uint8Array(b), function(x) { return ('0' + x.toString(16)).slice(-2); }).join('');",
            "    };",
            "    var cookie = function(nome) {",
            "      var r = document.cookie.match(new RegExp('(?:^|; )' + nome + '=([^;]+)'));",
            "      return r ? r[1] : '';",
            "    };",
            "    var versione = cfg.get('INNERTUBE_CONTEXT_CLIENT_VERSION');",
            "    var h = {'Content-Type': 'application/json', 'X-Origin': location.origin, 'X-Goog-AuthUser': '0',",
            "             'X-Youtube-Client-Name': '1', 'X-Youtube-Client-Version': versione};",
            "    var vd = cfg.get('VISITOR_DATA');",
            "    if (vd) { h['X-Goog-Visitor-Id'] = vd; }",
            "    var sid = cookie('SAPISID') || cookie('__Secure-3PAPISID');",
            "    if (sid) {",
            "      var ts = Math.floor(Date.now() / 1000);",
            "      h['Authorization'] = 'SAPISIDHASH ' + ts + '_' + await sha1(ts + ' ' + sid + ' ' + location.origin);",
            "    }",
            "    var chiave = cfg.get('INNERTUBE_API_KEY');",
            "    var indirizzoApi = function(percorso) { return '/youtubei/v1/' + percorso + '?prettyPrint=false' + (chiave ? '&key=' + chiave : ''); };",
            "    var dati = Object.assign({context: cfg.get('INNERTUBE_CONTEXT')}, JSON.parse(corpo));",
            "    var risposta = await fetch(indirizzoApi(percorso), {",
            "      method: 'POST', credentials: 'include', headers: h, body: JSON.stringify(dati)",
            "    });",
            "    var esito = '';",
            "    try { var j = await risposta.json(); esito = (j && j.status) ? String(j.status) : ''; } catch (e1) { esito = ''; }",
            "    return JSON.stringify({ok: risposta.ok, stato: risposta.status, esito: esito});",
            "  } catch (e) {",
            "    return JSON.stringify({errore: 'script', dettaglio: String(e)});",
            "  }",
            "})('@PERCORSO@', @CORPO@)"})

    Private Sub New()
    End Sub

    ''' <summary>Dimentica il risultato di HaAccessoAsync: dopo aver fatto l'accesso nel browser si ricontrolla subito.</summary>
    Public Shared Sub DimenticaAccesso()
        _istanteAccesso = DateTime.MinValue
    End Sub

    ''' <summary>Vero se il browser ha l'accesso a YouTube (il cookie c'è); il risultato vale dieci minuti.</summary>
    Public Shared Async Function HaAccessoAsync() As Task(Of Boolean)
        Try
            If (DateTime.Now - _istanteAccesso).TotalMinutes < 10 Then Return _accessoNoto
            _accessoNoto = Await WGPaginaNascosta.HaCookieAsync(Sito, NomiCookie)
            _istanteAccesso = DateTime.Now
            Return _accessoNoto
        Catch ex As Exception
            log.ScriviLog("YoutubeOnline.HaAccessoAsync: " & ex.Message)
            Return False
        End Try
    End Function

    ' Traduce un errore dello script in una frase pronta da dire (e ricorda se l'accesso è venuto a mancare).
    Private Shared Function FraseErrore(errore As String) As String
        If errore = "accesso" Then
            _accessoNoto = False
            Return "Nel browser non risulta più l'accesso a YouTube"
        End If
        If errore = "consenso" Then
            Return "YouTube chiede di accettare i cookie: apri YouTube nel browser di WinGabriel, accetta e riprova"
        End If
        Return "La pagina di YouTube non è fatta come mi aspettavo"
    End Function

    ' Apre la pagina, esegue lo script e restituisce la risposta; Nothing (con UltimoErrore) se non riesce.
    ' Se la pagina non si legge si riprova una volta (YouTube a volte risponde con una pagina a metà); idNavigazione è
    ' l'identificativo con cui l'elenco si chiede direttamente a YouTube quando la pagina non si riconosce (FEhistory...).
    Private Shared Async Function LeggiAsync(indirizzo As String, modo As String, massimo As Integer, Optional idNavigazione As String = "") As Task(Of JObject)
        Try
            UltimoErrore = ""
            Dim testoScript As String = Script.Replace("@MODO@", modo).Replace("@MASSIMO@", massimo.ToString()).Replace("@NAVIGAZIONE@", If(idNavigazione, ""))
            For tentativo As Integer = 1 To 2
                Dim risposta As String = Await WGPaginaNascosta.EseguiAsync(indirizzo, testoScript)
                Dim o As JObject = Nothing
                If Not String.IsNullOrEmpty(risposta) Then
                    Try
                        o = JObject.Parse(risposta)
                    Catch exJson As Exception
                        log.ScriviLog("YoutubeOnline.LeggiAsync: risposta non valida " & exJson.Message)
                    End Try
                End If
                If o Is Nothing Then
                    UltimoErrore = "Non sono riuscito a leggere la pagina di YouTube"
                    If tentativo = 1 Then Await Task.Delay(1500)
                    Continue For
                End If
                Dim errore As String = CStr(o("errore"))
                If errore IsNot Nothing Then
                    log.ScriviLog("YoutubeOnline.LeggiAsync (" & modo & "): " & errore & " " & CStr(o("dettaglio")))
                    UltimoErrore = FraseErrore(errore)
                    ' L'accesso mancante e il consenso non cambiano riprovando.
                    If errore = "accesso" OrElse errore = "consenso" OrElse tentativo = 2 Then Return Nothing
                    Await Task.Delay(1500)
                    Continue For
                End If
                If TryCast(o("elementi"), JArray) Is Nothing Then
                    log.ScriviLog("YoutubeOnline.LeggiAsync (" & modo & "): risposta senza elenco")
                    UltimoErrore = "La pagina di YouTube non è fatta come mi aspettavo"
                    Continue For
                End If
                UltimoErrore = ""
                Return o
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("YoutubeOnline.LeggiAsync: " & ex.Message)
            UltimoErrore = "Non sono riuscito a leggere la pagina di YouTube"
            Return Nothing
        End Try
    End Function

    ' Gli elementi della risposta come testi, saltando quelli senza identificativo.
    Private Shared Function Elementi(risposta As JObject) As List(Of JToken)
        Dim risultato As New List(Of JToken)()
        Dim elenco As JArray = TryCast(risposta("elementi"), JArray)
        If elenco Is Nothing Then Return risultato
        For Each e As JToken In elenco
            If e IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(CStr(e("id"))) Then risultato.Add(e)
        Next
        Return risultato
    End Function

    Private Shared Function Testo(valore As JToken) As String
        If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return ""
        Return CStr(valore)
    End Function

    Private Shared Function VideoDa(e As JToken) As VideoOnline
        Return New VideoOnline With {.Id = Testo(e("id")), .Titolo = Testo(e("t")), .Canale = Testo(e("c")), .IdCanale = Testo(e("cid")),
                                     .Durata = Testo(e("d")), .Pubblicato = Testo(e("p")), .SetId = Testo(e("s"))}
    End Function

    ''' <summary>I canali a cui è iscritto l'account; Nothing se la lettura non è riuscita.</summary>
    Public Shared Async Function IscrizioniAsync() As Task(Of List(Of CanaleOnline))
        Dim risposta As JObject = Await LeggiAsync(Sito & "/feed/channels", "iscrizioni", 5000, "FEchannels")
        If risposta Is Nothing Then Return Nothing
        Dim canali As New List(Of CanaleOnline)()
        For Each e As JToken In Elementi(risposta)
            canali.Add(New CanaleOnline With {.Id = Testo(e("id")), .Titolo = Testo(e("t"))})
        Next
        Return canali
    End Function

    ''' <summary>Gli ultimi video visti dall'account (al massimo quanti indicati); Nothing se la lettura non è riuscita.</summary>
    Public Shared Async Function CronologiaAsync(massimo As Integer) As Task(Of List(Of VideoOnline))
        Dim risposta As JObject = Await LeggiAsync(Sito & "/feed/history", "cronologia", massimo, "FEhistory")
        If risposta Is Nothing Then Return Nothing
        Dim video As New List(Of VideoOnline)()
        For Each e As JToken In Elementi(risposta)
            video.Add(VideoDa(e))
        Next
        Return video
    End Function

    ''' <summary>I video più recenti dei canali a cui è iscritto l'account (la pagina "Iscrizioni" di YouTube); Nothing se la lettura non è riuscita.</summary>
    Public Shared Async Function NovitaAsync(massimo As Integer) As Task(Of List(Of VideoOnline))
        Dim risposta As JObject = Await LeggiAsync(Sito & "/feed/subscriptions", "novita", massimo, "FEsubscriptions")
        If risposta Is Nothing Then Return Nothing
        Dim video As New List(Of VideoOnline)()
        For Each e As JToken In Elementi(risposta)
            video.Add(VideoDa(e))
        Next
        Return video
    End Function

    ''' <summary>
    ''' Le playlist dell'account, con in testa "Video che ti piacciono" e "Guarda più tardi" (che YouTube tratta come
    ''' playlist ma non sempre elenca); Nothing se la lettura non è riuscita.
    ''' </summary>
    Public Shared Async Function PlaylistAsync() As Task(Of List(Of PlaylistOnline))
        Dim risposta As JObject = Await LeggiAsync(Sito & "/feed/playlists", "playlist", 500, "FEplaylist_aggregation")
        If risposta Is Nothing Then Return Nothing
        Dim playlist As New List(Of PlaylistOnline)()
        playlist.Add(New PlaylistOnline With {.Id = "LL", .Titolo = "Video che ti piacciono", .Quanti = ""})
        playlist.Add(New PlaylistOnline With {.Id = "WL", .Titolo = "Guarda più tardi", .Quanti = ""})
        For Each e As JToken In Elementi(risposta)
            Dim id As String = Testo(e("id"))
            If id = "LL" OrElse id = "WL" Then Continue For
            playlist.Add(New PlaylistOnline With {.Id = id, .Titolo = Testo(e("t")), .Quanti = Testo(e("n"))})
        Next
        Return playlist
    End Function

    ''' <summary>I video di una playlist dell'account (anche LL e WL), al massimo "massimo"; Nothing se la lettura non è riuscita.</summary>
    Public Shared Async Function VideoDiPlaylistAsync(idPlaylist As String, massimo As Integer) As Task(Of List(Of VideoOnline))
        If Not Regex.IsMatch(If(idPlaylist, ""), "^[A-Za-z0-9_-]{2,64}$") Then
            UltimoErrore = "Di questa playlist non ho l'identificativo che serve a YouTube"
            Return Nothing
        End If
        Dim risposta As JObject = Await LeggiAsync(Sito & "/playlist?list=" & idPlaylist, "elencoplaylist", massimo, "VL" & idPlaylist)
        If risposta Is Nothing Then Return Nothing
        Dim video As New List(Of VideoOnline)()
        For Each e As JToken In Elementi(risposta)
            video.Add(VideoDa(e))
        Next
        Return video
    End Function

    ''' <summary>Mette il mi piace al video (o lo toglie); falso con UltimoErrore se YouTube non l'ha accettato.</summary>
    Public Shared Function MiPiaceAsync(idVideo As String, mettere As Boolean) As Task(Of Boolean)
        If Not IdVideoValido(idVideo) Then Return ErroreId("Di questo video non ho l'identificativo che serve a YouTube")
        Dim corpo As String = New JObject(New JProperty("target", New JObject(New JProperty("videoId", idVideo)))).ToString(Newtonsoft.Json.Formatting.None)
        Return ChiamataApiAsync(Sito & "/watch?v=" & idVideo, If(mettere, "like/like", "like/removelike"), corpo)
    End Function

    ''' <summary>Mette non mi piace al video; falso con UltimoErrore se YouTube non l'ha accettato.</summary>
    Public Shared Function NonMiPiaceAsync(idVideo As String) As Task(Of Boolean)
        If Not IdVideoValido(idVideo) Then Return ErroreId("Di questo video non ho l'identificativo che serve a YouTube")
        Dim corpo As String = New JObject(New JProperty("target", New JObject(New JProperty("videoId", idVideo)))).ToString(Newtonsoft.Json.Formatting.None)
        Return ChiamataApiAsync(Sito & "/watch?v=" & idVideo, "like/dislike", corpo)
    End Function

    ''' <summary>Aggiunge il video a una playlist dell'account (anche "WL", Guarda più tardi).</summary>
    Public Shared Function AggiungiAPlaylistAsync(idVideo As String, idPlaylist As String) As Task(Of Boolean)
        If Not IdVideoValido(idVideo) Then Return ErroreId("Di questo video non ho l'identificativo che serve a YouTube")
        If Not Regex.IsMatch(If(idPlaylist, ""), "^[A-Za-z0-9_-]{2,64}$") Then Return ErroreId("Di questa playlist non ho l'identificativo che serve a YouTube")
        Dim azione As New JObject(New JProperty("action", "ACTION_ADD_VIDEO"), New JProperty("addedVideoId", idVideo))
        Dim corpo As String = New JObject(New JProperty("playlistId", idPlaylist), New JProperty("actions", New JArray(azione))).ToString(Newtonsoft.Json.Formatting.None)
        Return ChiamataApiAsync(Sito & "/playlist?list=" & idPlaylist, "browse/edit_playlist", corpo)
    End Function

    ''' <summary>Toglie un video da una playlist dell'account, con l'identificativo che ha dentro la playlist (SetId).</summary>
    Public Shared Function TogliDaPlaylistAsync(idPlaylist As String, setId As String) As Task(Of Boolean)
        If Not Regex.IsMatch(If(idPlaylist, ""), "^[A-Za-z0-9_-]{2,64}$") OrElse String.IsNullOrEmpty(setId) Then Return ErroreId("Per questo video manca l'identificativo che serve a toglierlo dalla playlist")
        Dim azione As New JObject(New JProperty("action", "ACTION_REMOVE_VIDEO"), New JProperty("setVideoId", setId))
        Dim corpo As String = New JObject(New JProperty("playlistId", idPlaylist), New JProperty("actions", New JArray(azione))).ToString(Newtonsoft.Json.Formatting.None)
        Return ChiamataApiAsync(Sito & "/playlist?list=" & idPlaylist, "browse/edit_playlist", corpo)
    End Function

    Private Shared Function IdVideoValido(idVideo As String) As Boolean
        Return Not String.IsNullOrEmpty(idVideo) AndAlso Regex.IsMatch(idVideo, "^[A-Za-z0-9_-]{6,20}$")
    End Function

    Private Shared Function ErroreId(testo As String) As Task(Of Boolean)
        UltimoErrore = testo
        Return Task.FromResult(False)
    End Function

    ' Una chiamata a YouTube dalla pagina indicata; vero se YouTube risponde bene (e, dove lo dice, "STATUS_SUCCEEDED").
    Private Shared Async Function ChiamataApiAsync(paginaDaAprire As String, percorso As String, corpoJson As String) As Task(Of Boolean)
        Try
            UltimoErrore = ""
            Dim script As String = ScriptApi.Replace("@PERCORSO@", percorso).Replace("@CORPO@", Newtonsoft.Json.JsonConvert.ToString(corpoJson))
            Dim risposta As String = Await WGPaginaNascosta.EseguiAsync(paginaDaAprire, script)
            If String.IsNullOrEmpty(risposta) Then
                UltimoErrore = "Non sono riuscito a parlare con YouTube"
                Return False
            End If
            Dim o As JObject = JObject.Parse(risposta)
            Dim errore As String = CStr(o("errore"))
            If errore IsNot Nothing Then
                log.ScriviLog("YoutubeOnline.ChiamataApiAsync " & percorso & ": " & errore & " " & CStr(o("dettaglio")))
                UltimoErrore = FraseErrore(errore)
                Return False
            End If
            Dim esito As String = CStr(o("esito"))
            If Not CBool(o("ok")) OrElse (Not String.IsNullOrEmpty(esito) AndAlso esito <> "STATUS_SUCCEEDED") Then
                log.ScriviLog("YoutubeOnline.ChiamataApiAsync " & percorso & ": YouTube ha risposto " & CStr(o("stato")) & " " & esito)
                UltimoErrore = "YouTube ha rifiutato la richiesta"
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("YoutubeOnline.ChiamataApiAsync: " & ex.Message)
            UltimoErrore = "Non sono riuscito a parlare con YouTube"
            Return False
        End Try
    End Function

    ''' <summary>Vero se l'identificativo è quello di un canale (UC...), l'unico che YouTube accetta per iscriversi.</summary>
    Public Shared Function IdCanaleValido(idCanale As String) As Boolean
        Return Not String.IsNullOrEmpty(idCanale) AndAlso Regex.IsMatch(idCanale, "^UC[A-Za-z0-9_-]{10,}$")
    End Function

    ''' <summary>Iscrive l'account al canale; falso (con UltimoErrore) se non è riuscito o YouTube non lo conferma.</summary>
    Public Shared Function IscriviAsync(idCanale As String) As Task(Of Boolean)
        Return CambiaIscrizioneAsync("iscriviti", idCanale)
    End Function

    ''' <summary>Annulla l'iscrizione dell'account al canale; falso (con UltimoErrore) se non è riuscito o YouTube non lo conferma.</summary>
    Public Shared Function DisiscriviAsync(idCanale As String) As Task(Of Boolean)
        Return CambiaIscrizioneAsync("disiscriviti", idCanale)
    End Function

    Private Shared Async Function CambiaIscrizioneAsync(azione As String, idCanale As String) As Task(Of Boolean)
        Try
            UltimoErrore = ""
            If Not IdCanaleValido(idCanale) Then
                UltimoErrore = "Di questo canale non ho l'identificativo che serve a YouTube"
                Return False
            End If
            Dim risposta As String = Await WGPaginaNascosta.EseguiAsync(Sito & "/channel/" & idCanale, ScriptAzione.Replace("@AZIONE@", azione).Replace("@ID@", idCanale))
            If String.IsNullOrEmpty(risposta) Then
                UltimoErrore = "Non sono riuscito a parlare con YouTube"
                Return False
            End If
            Dim o As JObject = JObject.Parse(risposta)
            Dim errore As String = CStr(o("errore"))
            If errore IsNot Nothing Then
                log.ScriviLog("YoutubeOnline.CambiaIscrizioneAsync: " & errore & " " & CStr(o("dettaglio")))
                UltimoErrore = FraseErrore(errore)
                Return False
            End If
            If Not CBool(o("ok")) Then
                log.ScriviLog("YoutubeOnline.CambiaIscrizioneAsync: YouTube ha risposto " & CStr(o("stato")))
                UltimoErrore = "YouTube ha rifiutato la richiesta"
                Return False
            End If
            ' Verifica: il canale deve comparire (o sparire) dall'elenco delle iscrizioni dell'account.
            Dim canali As List(Of CanaleOnline) = Await IscrizioniAsync()
            UltimoErrore = ""
            If canali Is Nothing Then Return True
            If canali.Any(Function(c) c.Id = idCanale) = (azione = "iscriviti") Then Return True
            log.ScriviLog("YoutubeOnline.CambiaIscrizioneAsync: YouTube ha risposto bene ma l'elenco non è cambiato")
            UltimoErrore = "YouTube ha risposto, ma l'elenco delle iscrizioni non è cambiato"
            Return False
        Catch ex As Exception
            log.ScriviLog("YoutubeOnline.CambiaIscrizioneAsync: " & ex.Message)
            UltimoErrore = "Non sono riuscito a parlare con YouTube"
            Return False
        End Try
    End Function

End Class
