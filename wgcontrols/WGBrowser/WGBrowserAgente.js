(function () {
    'use strict';

    var VERSIONE = 13;
    try {
        if (window.__wgAgente && window.__wgAgente.versione === VERSIONE) {
            window.__wgAgente.collegaDocumento(document);
            return;
        }
    } catch (e) { }

    var A = { versione: VERSIONE };

    function trovaPonte() {
        try {
            var c = window.chrome;
            if (c && c.webview && typeof c.webview.postMessage === 'function') return c.webview;
        } catch (e) { }
        return null;
    }

    var ponteIniziale = trovaPonte();
    A.ponteCatturato = !!ponteIniziale;
    try {
        Object.defineProperty(window, '__wgAgente', { value: A, configurable: true, enumerable: false, writable: true });
    } catch (e) {
        window.__wgAgente = A;
    }

    var MASSIMO_RIGHE = 25000;
    var LUNGHEZZA_MASSIMA_RIGA = 600;
    var SVG_NS = 'http://www.w3.org/2000/svg';

    var idPerElemento = new WeakMap();
    var mappa = new Map();
    var contatore = 0;
    var sporco = false;
    var documentiCollegati = new WeakSet();

    var legameCatturato = null;

    function trovaLegame() {
        try {
            if (legameCatturato) return legameCatturato;
            var f = window.__wgLegame;
            if (typeof f === 'function') {
                legameCatturato = f;
                try { delete window.__wgLegame; } catch (e) { }
                return legameCatturato;
            }
        } catch (e) { }
        return null;
    }
    trovaLegame();

    function inviaSuPonte(testo) {
        try {
            if (!ponteIniziale) {
                ponteIniziale = trovaPonte();
                if (ponteIniziale) A.ponteCatturato = true;
            }
            if (ponteIniziale) {
                ponteIniziale.postMessage(testo);
                return true;
            }
        } catch (e) { }
        return false;
    }

    function inviaSuLegame(testo) {
        try {
            var l = trovaLegame();
            if (l) {
                l(testo);
                return true;
            }
        } catch (e) { }
        return false;
    }

    function invia(oggetto) {
        var testo = JSON.stringify(oggetto);
        if (inviaSuPonte(testo)) return;
        inviaSuLegame(testo);
    }

    function inviaSuEntrambi(oggetto) {
        var testo = JSON.stringify(oggetto);
        inviaSuPonte(testo);
        inviaSuLegame(testo);
    }

    function idDi(el) {
        var id = idPerElemento.get(el);
        if (!id) {
            contatore += 1;
            id = contatore;
            idPerElemento.set(el, id);
        }
        mappa.set(id, el);
        return id;
    }

    function elementoDaId(id) {
        var el = mappa.get(id);
        if (el && el.isConnected) return el;
        return null;
    }

    function normalizza(s) {
        if (!s) return '';
        return String(s).replace(/[\s ​‎‏﻿]+/g, ' ').trim();
    }

    function stile(el) {
        try {
            var vista = el.ownerDocument.defaultView || window;
            return vista.getComputedStyle(el);
        } catch (e) {
            return null;
        }
    }

    function attributo(el, nome) {
        try {
            return el.getAttribute(nome);
        } catch (e) {
            return null;
        }
    }

    function figliDi(el) {
        if (el.shadowRoot) return el.shadowRoot.childNodes;
        if (el.localName === 'slot') {
            try {
                var assegnati = el.assignedNodes({ flatten: true });
                if (assegnati && assegnati.length) return assegnati;
            } catch (e) { }
        }
        return el.childNodes;
    }

    function genitoreDi(el) {
        return el.parentElement || (el.parentNode && el.parentNode.host) || null;
    }

    function radiceDi(el) {
        try {
            var r = el.getRootNode();
            if (r && typeof r.getElementById === 'function') return r;
        } catch (e) { }
        return el.ownerDocument || document;
    }

    function elementoPerIdHtml(el, idHtml) {
        if (!idHtml) return null;
        var r = radiceDi(el);
        var trovato = null;
        try { trovato = r.getElementById(idHtml); } catch (e) { }
        if (!trovato) {
            try { trovato = (el.ownerDocument || document).getElementById(idHtml); } catch (e) { }
        }
        return trovato;
    }

    function spezza(testo, massimo) {
        if (testo.length <= massimo) return [testo];
        var pezzi = [];
        var resto = testo;
        while (resto.length > massimo) {
            var taglio = -1;
            var finestra = resto.substring(0, massimo);
            var re = /[.!?;:](\s)/g;
            var m;
            while ((m = re.exec(finestra)) !== null) {
                if (m.index > massimo / 3) taglio = m.index + 1;
            }
            if (taglio < 0) taglio = finestra.lastIndexOf(' ');
            if (taglio < massimo / 3) taglio = massimo;
            pezzi.push(resto.substring(0, taglio).trim());
            resto = resto.substring(taglio).trim();
        }
        if (resto) pezzi.push(resto);
        return pezzi;
    }

    function sorgenteSicura(indirizzo) {
        if (!indirizzo) return '';
        var testo = String(indirizzo);
        if (/^data:/i.test(testo) || testo.length > 2000) return '';
        return testo;
    }

    function linguaDi(el) {
        try {
            var e = el;
            var giri = 0;
            while (e && giri < 80) {
                giri += 1;
                if (e.nodeType === 1) {
                    var l = attributo(e, 'lang') || attributo(e, 'xml:lang');
                    if (l) return String(l).trim().toLowerCase().replace('_', '-');
                }
                e = genitoreDi(e);
            }
        } catch (err) { }
        return '';
    }

    function nomeDaSorgente(indirizzo) {
        try {
            if (!indirizzo) return '';
            if (/^(data|blob|javascript):/i.test(indirizzo)) return '';
            var u = new URL(indirizzo, document.baseURI);
            var pezzi = u.pathname.split('/').filter(function (p) { return p; });
            if (!pezzi.length) return '';
            var nome = decodeURIComponent(pezzi[pezzi.length - 1]).replace(/\.[a-z0-9]{2,5}$/i, '').replace(/[-_+.]+/g, ' ').trim();
            if (nome.length > 60) return '';
            if ((nome.match(/[a-zà-ÿ]/gi) || []).length < 3) return '';
            if (/^[0-9a-f ]{12,}$/i.test(nome)) return '';
            return nome;
        } catch (e) {
            return '';
        }
    }

    var RUOLI_VALIDI = {
        alert: 1, alertdialog: 1, application: 1, article: 1, banner: 1, blockquote: 1, button: 1, caption: 1,
        cell: 1, checkbox: 1, code: 1, columnheader: 1, combobox: 1, complementary: 1, contentinfo: 1,
        definition: 1, deletion: 1, dialog: 1, directory: 1, document: 1, emphasis: 1, feed: 1, figure: 1,
        form: 1, generic: 1, grid: 1, gridcell: 1, group: 1, heading: 1, img: 1, image: 1, insertion: 1,
        link: 1, list: 1, listbox: 1, listitem: 1, log: 1, main: 1, marquee: 1, math: 1, menu: 1, menubar: 1,
        menuitem: 1, menuitemcheckbox: 1, menuitemradio: 1, meter: 1, navigation: 1, none: 1, note: 1,
        option: 1, paragraph: 1, presentation: 1, progressbar: 1, radio: 1, radiogroup: 1, region: 1, row: 1,
        rowgroup: 1, rowheader: 1, scrollbar: 1, search: 1, searchbox: 1, separator: 1, slider: 1,
        spinbutton: 1, status: 1, strong: 1, subscript: 1, superscript: 1, switch: 1, tab: 1, table: 1,
        tablist: 1, tabpanel: 1, term: 1, textbox: 1, time: 1, timer: 1, toolbar: 1, tooltip: 1, tree: 1,
        treegrid: 1, treeitem: 1
    };

    var RUOLI_WIDGET = {
        link: 1, button: 1, checkbox: 1, radio: 1, switch: 1, textbox: 1, searchbox: 1, combobox: 1,
        slider: 1, spinbutton: 1, option: 1, menuitem: 1, menuitemcheckbox: 1, menuitemradio: 1, tab: 1,
        treeitem: 1, progressbar: 1, meter: 1, listbox: 0
    };

    var RUOLI_STRUTTURALI = {
        list: 1, table: 1, grid: 1, treegrid: 1, row: 1, banner: 1, navigation: 1, main: 1, complementary: 1,
        contentinfo: 1, search: 1, form: 1, region: 1, dialog: 1, alertdialog: 1, tablist: 1, menu: 1, menubar: 1,
        tree: 1, toolbar: 1, radiogroup: 1, heading: 1, frame: 1, listbox: 1
    };

    var NOME_DA_CONTENUTO = {
        button: 1, cell: 1, checkbox: 1, columnheader: 1, gridcell: 1, heading: 1, link: 1, menuitem: 1,
        menuitemcheckbox: 1, menuitemradio: 1, option: 1, radio: 1, row: 1, rowheader: 1, switch: 1, tab: 1,
        tooltip: 1, treeitem: 1, caption: 1, term: 1
    };

    var RUOLI_REGIONE = {
        banner: 'intestazione della pagina', navigation: 'navigazione', main: 'contenuto principale',
        complementary: 'contenuto complementare', contentinfo: 'informazioni sul contenuto',
        search: 'ricerca', form: 'modulo', region: 'regione'
    };

    var TIPI_TESTO = {
        text: 1, search: 1, email: 1, url: 1, tel: 1, password: 1, number: 1, date: 1, time: 1,
        'datetime-local': 1, month: 1, week: 1
    };

    var ELEMENTI_DA_SALTARE = {
        script: 1, style: 1, noscript: 1, template: 1, head: 1, meta: 1, link: 1, title: 1, base: 1,
        datalist: 1, param: 1, source: 1, track: 1
    };

    var TAG_NON_CLICCABILI = {
        html: 1, body: 1, label: 1, main: 1, form: 1, nav: 1, header: 1, footer: 1, option: 1, select: 1, input: 1,
        textarea: 1, button: 1, a: 1, summary: 1, iframe: 1, frame: 1, svg: 1, path: 1, g: 1, img: 1, video: 1,
        audio: 1, canvas: 1, table: 1, tbody: 1, thead: 1, tr: 1, td: 1, th: 1, ul: 1, ol: 1, dl: 1
    };

    function ruoloEsplicito(el) {
        var r = attributo(el, 'role');
        if (!r) return '';
        var parti = r.trim().toLowerCase().split(/\s+/);
        for (var i = 0; i < parti.length; i++) {
            if (RUOLI_VALIDI[parti[i]]) return parti[i] === 'image' ? 'img' : parti[i];
        }
        return '';
    }

    function focalizzabile(el) {
        try {
            if (el.isContentEditable) return true;
            if (el.tabIndex >= 0) return true;
        } catch (e) { }
        return false;
    }

    function dentroSezione(el) {
        var p = el.parentElement;
        while (p) {
            var t = p.localName;
            if (t === 'article' || t === 'aside' || t === 'main' || t === 'nav' || t === 'section') return true;
            var r = ruoloEsplicito(p);
            if (r === 'article' || r === 'complementary' || r === 'main' || r === 'navigation' || r === 'region') return true;
            p = p.parentElement;
        }
        return false;
    }

    function haNomeEsplicito(el) {
        return !!(normalizza(attributo(el, 'aria-label')) || attributo(el, 'aria-labelledby') || normalizza(attributo(el, 'title')));
    }

    function ruoloInput(el) {
        var t = (el.type || 'text').toLowerCase();
        switch (t) {
            case 'hidden': return 'hidden';
            case 'button': case 'submit': case 'reset': case 'image': case 'file': case 'color': return 'button';
            case 'checkbox': return 'checkbox';
            case 'radio': return 'radio';
            case 'range': return 'slider';
            case 'number': return 'spinbutton';
            case 'search': return el.hasAttribute('list') ? 'combobox' : 'searchbox';
            default: return el.hasAttribute('list') ? 'combobox' : 'textbox';
        }
    }

    function ruoloDi(el) {
        var r = ruoloEsplicito(el);
        if (r === 'none' || r === 'presentation') {
            if (!focalizzabile(el)) return 'generic';
            r = '';
        }
        if (r) return r;
        var tag = el.localName;
        switch (tag) {
            case 'a': case 'area': return el.hasAttribute('href') ? 'link' : 'generic';
            case 'button': return 'button';
            case 'summary': return (el.parentElement && el.parentElement.localName === 'details') ? 'button' : 'generic';
            case 'input': return ruoloInput(el);
            case 'textarea': return 'textbox';
            case 'select': return (el.multiple || el.size > 1) ? 'listbox' : 'combobox';
            case 'option': return 'option';
            case 'h1': case 'h2': case 'h3': case 'h4': case 'h5': case 'h6': return 'heading';
            case 'img':
                if (attributo(el, 'alt') === '' && !haNomeEsplicito(el)) return 'presentation';
                return 'img';
            case 'ul': case 'ol': case 'menu': case 'dl': return 'list';
            case 'li': return 'listitem';
            case 'dt': return 'term';
            case 'dd': return 'definition';
            case 'table': return 'table';
            case 'tr': return 'row';
            case 'td': return 'cell';
            case 'th': return attributo(el, 'scope') === 'row' ? 'rowheader' : 'columnheader';
            case 'caption': return 'caption';
            case 'nav': return 'navigation';
            case 'main': return 'main';
            case 'aside': return 'complementary';
            case 'header': return dentroSezione(el) ? 'generic' : 'banner';
            case 'footer': return dentroSezione(el) ? 'generic' : 'contentinfo';
            case 'form': return haNomeEsplicito(el) ? 'form' : 'generic';
            case 'section': return haNomeEsplicito(el) ? 'region' : 'generic';
            case 'search': return 'search';
            case 'article': return 'article';
            case 'dialog': return 'dialog';
            case 'fieldset': return 'group';
            case 'figure': return 'figure';
            case 'blockquote': return 'blockquote';
            case 'p': return 'paragraph';
            case 'hr': return 'separator';
            case 'progress': return 'progressbar';
            case 'meter': return 'meter';
            case 'iframe': case 'frame': return 'frame';
            case 'output': return 'status';
            case 'canvas': return 'canvas';
            case 'video': return 'video';
            case 'audio': return 'audio';
        }
        try {
            if (el.isContentEditable && (!el.parentElement || !el.parentElement.isContentEditable)) return 'textbox';
        } catch (e) { }
        return 'generic';
    }

    function livelloDi(el) {
        var a = parseInt(attributo(el, 'aria-level'), 10);
        if (a > 0) return a;
        var m = /^h([1-6])$/.exec(el.localName);
        return m ? parseInt(m[1], 10) : 2;
    }

    function infoModifica(el) {
        var tag = el.localName;
        var ruolo = ruoloEsplicito(el);
        if (tag === 'input') {
            var tipo = (el.type || 'text').toLowerCase();
            if (!TIPI_TESTO[tipo]) return null;
            return { ml: false, pw: tipo === 'password', ro: !!(el.readOnly || el.disabled) };
        }
        if (tag === 'textarea') {
            var multiriga = true;
            if (ruolo === 'combobox' || ruolo === 'searchbox') multiriga = false;
            else if (attributo(el, 'aria-multiline') === 'false') multiriga = false;
            else if (attributo(el, 'rows') === '1') multiriga = false;
            else if (attributo(el, 'aria-multiline') !== 'true') {
                var s = stile(el);
                if (s) {
                    var altezzaRiga = parseFloat(s.lineHeight);
                    if (!(altezzaRiga > 0)) altezzaRiga = (parseFloat(s.fontSize) || 16) * 1.25;
                    var altezza = el.clientHeight - (parseFloat(s.paddingTop) || 0) - (parseFloat(s.paddingBottom) || 0);
                    if (altezza > 0 && altezza < altezzaRiga * 1.7) multiriga = false;
                }
            }
            return { ml: multiriga, pw: false, ro: !!(el.readOnly || el.disabled) };
        }
        try {
            if (el.isContentEditable) {
                return {
                    ml: !(ruolo === 'combobox' || ruolo === 'searchbox' || attributo(el, 'aria-multiline') === 'false'),
                    pw: false,
                    ro: false
                };
            }
        } catch (e) { }
        if ((ruolo === 'textbox' || ruolo === 'searchbox') && attributo(el, 'aria-readonly') !== 'true') {
            return { ml: attributo(el, 'aria-multiline') === 'true', pw: false, ro: false };
        }
        return null;
    }

    function valoreDi(el, ruolo) {
        var tag = el.localName;
        try {
            if (tag === 'input') {
                if (el.type === 'password') return '';
                if (TIPI_TESTO[el.type] || el.type === 'range' || el.type === 'color') return el.value || '';
                return '';
            }
            if (tag === 'textarea') return el.value || '';
            if (tag === 'select') {
                if (el.multiple) {
                    var scelte = [];
                    for (var i = 0; i < el.options.length; i++) if (el.options[i].selected) scelte.push(el.options[i].text);
                    return normalizza(scelte.join(', '));
                }
                var o = el.options[el.selectedIndex];
                return o ? normalizza(o.text) : '';
            }
            if (ruolo === 'slider' || ruolo === 'spinbutton' || ruolo === 'progressbar' || ruolo === 'meter' || ruolo === 'scrollbar') {
                var vt = attributo(el, 'aria-valuetext');
                if (vt) return normalizza(vt);
                var vn = attributo(el, 'aria-valuenow');
                if (vn !== null && vn !== '') {
                    if (ruolo === 'progressbar') {
                        var max = parseFloat(attributo(el, 'aria-valuemax') || '100');
                        var min = parseFloat(attributo(el, 'aria-valuemin') || '0');
                        var val = parseFloat(vn);
                        if (max > min) return Math.round((val - min) * 100 / (max - min)) + ' percento';
                    }
                    return vn;
                }
                if (tag === 'progress' || tag === 'meter') {
                    if (tag === 'progress' && el.max > 0 && el.hasAttribute('value')) return Math.round(el.value * 100 / el.max) + ' percento';
                    return String(el.value);
                }
                return '';
            }
            if (el.isContentEditable) return el.innerText || '';
            if (ruolo === 'combobox') {
                var vtc = attributo(el, 'aria-valuetext');
                if (vtc) return normalizza(vtc);
                return testoSottoalbero(el, new Set(), false, null);
            }
        } catch (e) { }
        return '';
    }

    function statiDi(el, ruolo) {
        var st = [];
        var a = function (n) { return attributo(el, n); };
        try {
            if (ruolo === 'checkbox' || ruolo === 'switch' || ruolo === 'menuitemcheckbox' || ruolo === 'radio' || ruolo === 'menuitemradio') {
                var c;
                if (el.localName === 'input') c = el.indeterminate ? 'mixed' : String(!!el.checked);
                else c = a('aria-checked') || 'false';
                if (ruolo === 'switch') st.push(c === 'true' ? 'attivato' : 'disattivato');
                else st.push(c === 'true' ? 'selezionato' : (c === 'mixed' ? 'parzialmente selezionato' : 'non selezionato'));
            }
            var premuto = a('aria-pressed');
            if (premuto === 'true') st.push('premuto');
            else if (premuto === 'false') st.push('non premuto');
            else if (premuto === 'mixed') st.push('parzialmente premuto');
            var espanso = a('aria-expanded');
            if (espanso === 'true') st.push('espanso');
            else if (espanso === 'false') st.push('compresso');
            if (el.localName === 'summary' && el.parentElement && el.parentElement.localName === 'details') {
                st.push(el.parentElement.open ? 'espanso' : 'compresso');
            }
            if ((ruolo === 'tab' || ruolo === 'option' || ruolo === 'treeitem' || ruolo === 'gridcell' || ruolo === 'row') && a('aria-selected') === 'true') st.push('selezionato');
            if (el.localName === 'option' && el.selected) st.push('selezionato');
            var corrente = a('aria-current');
            if (corrente && corrente !== 'false') st.push(corrente === 'page' ? 'pagina corrente' : 'corrente');
            if (el.disabled || a('aria-disabled') === 'true') st.push('non disponibile');
            if (el.required || a('aria-required') === 'true') st.push('obbligatorio');
            if (a('aria-invalid') === 'true' || a('aria-invalid') === 'spelling' || a('aria-invalid') === 'grammar') st.push('non valido');
            if ((ruolo === 'textbox' || ruolo === 'searchbox' || ruolo === 'combobox' || ruolo === 'spinbutton') && (el.readOnly || a('aria-readonly') === 'true')) st.push('sola lettura');
            var popup = a('aria-haspopup');
            if (popup && popup !== 'false' && ruolo !== 'combobox') st.push(popup === 'dialog' ? 'apre una finestra' : 'apre un menu');
        } catch (e) { }
        return st.join(', ');
    }

    function nascostoPerNome(el) {
        if (el.hidden) return true;
        if (attributo(el, 'aria-hidden') === 'true') return true;
        var s = stile(el);
        if (s && (s.display === 'none' || s.visibility === 'hidden')) return true;
        return false;
    }

    function visibile(el) {
        try {
            if (nascostoPerNome(el)) return false;
            var r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
        } catch (e) {
            return false;
        }
    }

    function eBloccoPerTesto(el) {
        var s = stile(el);
        if (!s) return false;
        var d = s.display;
        return !(d === 'inline' || d === 'contents' || d === 'inline-block' || d === 'none');
    }

    function testoSottoalbero(radice, visitati, includiNascosti, escludi) {
        var parti = [];
        var contatoreNodi = 0;
        function visita(n, primo) {
            contatoreNodi += 1;
            if (contatoreNodi > 4000) return;
            if (n.nodeType === 3) {
                parti.push(n.nodeValue);
                return;
            }
            if (n.nodeType !== 1) return;
            var e = n;
            if (e === escludi) return;
            var tag = e.localName;
            if (ELEMENTI_DA_SALTARE[tag]) return;
            if (!primo) {
                if (!includiNascosti && nascostoPerNome(e)) return;
                if (tag === 'input') {
                    var t = (e.type || '').toLowerCase();
                    if (TIPI_TESTO[t] && t !== 'password') parti.push(' ' + (e.value || '') + ' ');
                    else if (t === 'button' || t === 'submit' || t === 'reset') parti.push(' ' + (e.value || '') + ' ');
                    return;
                }
                if (tag === 'select') {
                    var o = e.options[e.selectedIndex];
                    if (o) parti.push(' ' + o.text + ' ');
                    return;
                }
                if (tag === 'textarea') {
                    parti.push(' ' + (e.value || '') + ' ');
                    return;
                }
                var al = attributo(e, 'aria-label');
                if (al && al.trim()) {
                    parti.push(' ' + al + ' ');
                    return;
                }
                if (tag === 'img' || tag === 'area') {
                    var alt = attributo(e, 'alt');
                    if (alt) parti.push(' ' + alt + ' ');
                    return;
                }
                if (tag === 'svg' || e.namespaceURI === SVG_NS) {
                    if (tag === 'svg') {
                        var titolo = e.querySelector('title');
                        if (titolo && titolo.textContent.trim()) parti.push(' ' + titolo.textContent + ' ');
                    }
                    return;
                }
                if (tag === 'br') {
                    parti.push(' ');
                    return;
                }
            }
            var blocco = !primo && eBloccoPerTesto(e);
            if (blocco) parti.push(' ');
            var figli = figliDi(e);
            for (var i = 0; i < figli.length; i++) visita(figli[i], false);
            if (blocco) parti.push(' ');
        }
        visita(radice, true);
        return normalizza(parti.join(''));
    }

    function nomeDa(el, visitati, daRiferimento) {
        if (!visitati) visitati = new Set();
        if (visitati.has(el)) return '';
        visitati.add(el);
        if (!daRiferimento) {
            var lb = attributo(el, 'aria-labelledby');
            if (lb) {
                var parti = [];
                lb.split(/\s+/).forEach(function (idHtml) {
                    var rif = elementoPerIdHtml(el, idHtml);
                    if (rif) {
                        var n = nomeDa(rif, visitati, true);
                        if (n) parti.push(n);
                    }
                });
                var unito = normalizza(parti.join(' '));
                if (unito) return unito;
            }
        }
        var al = attributo(el, 'aria-label');
        if (al && al.trim()) return normalizza(al);
        var tag = el.localName;
        if (tag === 'input' || tag === 'textarea' || tag === 'select' || tag === 'meter' || tag === 'progress' || tag === 'output') {
            var t = (el.type || '').toLowerCase();
            if (tag === 'input' && (t === 'button' || t === 'submit' || t === 'reset')) {
                return normalizza(el.value || (t === 'submit' ? 'Invia' : (t === 'reset' ? 'Reimposta' : '')));
            }
            if (tag === 'input' && t === 'image') return normalizza(el.alt || el.value || 'Invia');
            var etichette = [];
            try {
                if (el.labels) {
                    for (var i = 0; i < el.labels.length; i++) etichette.push(testoSottoalbero(el.labels[i], visitati, true, el));
                }
            } catch (e) { }
            var testoEtichette = normalizza(etichette.join(' '));
            if (testoEtichette) return testoEtichette;
            if (normalizza(el.title)) return normalizza(el.title);
            var segnaposto = attributo(el, 'placeholder') || attributo(el, 'aria-placeholder');
            if (normalizza(segnaposto)) return normalizza(segnaposto);
            if (tag === 'input' && t === 'file') return 'Scegli file';
            if (daRiferimento) return testoSottoalbero(el, visitati, true, null);
            return '';
        }
        if (tag === 'img' || tag === 'area') {
            var alt = attributo(el, 'alt');
            if (alt && alt.trim()) return normalizza(alt);
        }
        if (tag === 'svg') {
            var titolo = el.querySelector('title');
            if (titolo && titolo.textContent.trim()) return normalizza(titolo.textContent);
        }
        if (tag === 'fieldset') {
            var legenda = el.querySelector('legend');
            if (legenda) return testoSottoalbero(legenda, visitati, true, null);
        }
        if (tag === 'table') {
            var didascalia = el.querySelector('caption');
            if (didascalia) return testoSottoalbero(didascalia, visitati, true, null);
        }
        if (tag === 'figure') {
            var fc = el.querySelector('figcaption');
            if (fc) return testoSottoalbero(fc, visitati, true, null);
        }
        if (tag === 'iframe' || tag === 'frame') {
            if (normalizza(el.title)) return normalizza(el.title);
        }
        var ruolo = ruoloDi(el);
        if (daRiferimento || NOME_DA_CONTENUTO[ruolo]) {
            var c = testoSottoalbero(el, visitati, !!daRiferimento, null);
            if (c) return c;
        }
        if (normalizza(el.title)) return normalizza(el.title);
        if (ruolo === 'link') return nomeDaIndirizzo(el);
        return '';
    }

    function nomeDaIndirizzo(el) {
        try {
            var href = el.href || attributo(el, 'href') || '';
            if (!href) return '';
            var u = new URL(href, document.baseURI);
            var pezzi = u.pathname.split('/').filter(function (p) { return p; });
            var ultimo = pezzi.length ? decodeURIComponent(pezzi[pezzi.length - 1]) : u.hostname;
            return normalizza(ultimo.replace(/\.[a-z0-9]{2,5}$/i, '').replace(/[-_+]+/g, ' '));
        } catch (e) {
            return '';
        }
    }

    function descrivi(el) {
        var ruolo = ruoloDi(el);
        var riga = { i: idDi(el), r: ruolo, t: '' };
        riga.t = nomeDa(el, new Set(), false);
        if (ruolo === 'heading') riga.l = livelloDi(el);
        var st = statiDi(el, ruolo);
        if (st) riga.s = st;
        var ed = infoModifica(el);
        if (ed) {
            riga.ed = 1;
            if (ed.ml) riga.ml = 1;
            if (ed.pw) {
                riga.pw = 1;
                try { riga.pl = (el.value || '').length; } catch (e) { }
            }
            if (ed.ro) riga.ro = 1;
        }
        var v = valoreDi(el, ruolo);
        if (v && normalizza(v) !== riga.t) riga.v = v.length > 2000 ? v.substring(0, 2000) : v;
        if (!riga.t && (ruolo === 'generic' || ruolo === 'paragraph' || ruolo === 'group' || ruolo === 'listitem' || ruolo === 'cell' || ruolo === 'article' || ruolo === 'dialog' || ruolo === 'region')) {
            riga.t = testoSottoalbero(el, new Set(), false, null).substring(0, 300);
        }
        if (ruolo === 'link') {
            try { riga.u = el.href || attributo(el, 'href') || ''; } catch (e) { }
        }
        if (ruolo === 'dialog' || ruolo === 'alertdialog') {
            var descr = attributo(el, 'aria-describedby');
            if (descr) {
                var d = elementoPerIdHtml(el, descr.split(/\s+/)[0]);
                if (d) riga.dd = testoSottoalbero(d, new Set(), true, null).substring(0, 500);
            }
        }
        var lingua = linguaDi(el);
        if (lingua) riga.lg = lingua;
        return riga;
    }

    function widgetUnicoIn(el) {
        var trovati = [];
        try {
            var candidati = el.querySelectorAll('a[href],button,[role="button"],[role="link"],summary,input:not([type="hidden"])');
            for (var i = 0; i < candidati.length && trovati.length < 2; i++) {
                if (!nascostoPerNome(candidati[i])) trovati.push(candidati[i]);
            }
        } catch (e) { }
        return trovati.length === 1 ? trovati[0] : null;
    }

    function analizzaSottoalbero(el) {
        var risultato = { widget: 0, intestazioni: 0, strutture: 0, nodi: 0, testo: [], immagine: '' };
        var pila = [];
        var figli = figliDi(el);
        for (var i = figli.length - 1; i >= 0; i--) pila.push(figli[i]);
        while (pila.length && risultato.nodi < 2000) {
            risultato.nodi += 1;
            var n = pila.pop();
            if (n.nodeType === 3) {
                risultato.testo.push(n.nodeValue);
                continue;
            }
            if (n.nodeType !== 1) continue;
            var tag = n.localName;
            if (ELEMENTI_DA_SALTARE[tag]) continue;
            if (nascostoPerNome(n)) continue;
            var r = ruoloDi(n);
            if (tag === 'select' || RUOLI_WIDGET[r] === 1) {
                risultato.widget += 1;
                continue;
            }
            if (r === 'heading') risultato.intestazioni += 1;
            if (RUOLI_STRUTTURALI[r]) risultato.strutture += 1;
            if (tag === 'img' || tag === 'svg') {
                var nomeImmagine = nomeDa(n, new Set(), false);
                if (nomeImmagine) risultato.testo.push(' ' + nomeImmagine + ' ');
                else if (!risultato.immagine && tag === 'img') risultato.immagine = nomeDaSorgente(n.currentSrc || n.src || '');
                continue;
            }
            var fs = figliDi(n);
            for (var k = fs.length - 1; k >= 0; k--) pila.push(fs[k]);
        }
        risultato.testo = normalizza(risultato.testo.join(' '));
        return risultato;
    }

    function candidatoCliccabile(el, s, ruolo) {
        try {
            if (TAG_NON_CLICCABILI[el.localName]) return false;
            if (RUOLI_WIDGET[ruolo] === 1 || RUOLI_STRUTTURALI[ruolo]) return false;
            if (ruolo === 'img' || ruolo === 'canvas' || ruolo === 'video' || ruolo === 'audio') return false;
            var r = el.getBoundingClientRect();
            if (r.width < 2 || r.height < 2) return false;
            var larghezzaPagina = document.documentElement.clientWidth || window.innerWidth;
            if (r.width >= larghezzaPagina * 0.9 && r.height >= 500) return false;
            if (el.hasAttribute('onclick')) return true;
            var azioni = attributo(el, 'jsaction');
            if (azioni && (azioni.indexOf('click') >= 0 || (azioni.trim() && azioni.indexOf(':') < 0))) return true;
            if (s && s.cursor === 'pointer') {
                var padre = genitoreDi(el);
                var sp = padre && padre.nodeType === 1 ? stile(padre) : null;
                if (!sp || sp.cursor !== 'pointer') return true;
            }
            var tabindex = attributo(el, 'tabindex');
            if (tabindex !== null && parseInt(tabindex, 10) >= 0 && (ruolo === 'generic' || ruolo === 'paragraph' || ruolo === 'group' || ruolo === 'listitem')) return true;
        } catch (e) { }
        return false;
    }

    function Costruttore() {
        this.righe = [];
        this.linea = null;
        this.link = 0;
        this.linkRuolo = '';
        this.linkIndirizzo = '';
        this.dialogo = 0;
        this.visNascosta = false;
        this.inAttesa = {};
        this.pieno = false;
        this.canvas = false;
        this.cliccabile = 0;
    }

    Costruttore.prototype.emetti = function (riga) {
        if (this.righe.length >= MASSIMO_RIGHE) {
            this.pieno = true;
            return;
        }
        var p = this.inAttesa;
        if (p.rg) riga.rg = p.rg;
        if (p.ls) riga.ls = p.ls;
        if (p.li) riga.li = 1;
        if (p.tb) riga.tb = p.tb;
        if (p.ar) riga.ar = 1;
        if (p.gr) riga.gr = p.gr;
        if (p.bq) riga.bq = 1;
        if (p.pp) riga.pp = 1;
        this.inAttesa = {};
        if (this.dialogo) riga.dg = this.dialogo;
        if (this.link && !riga.lk && riga.i !== this.link) {
            riga.lk = this.link;
            riga.lr = this.linkRuolo;
            if (!riga.u && this.linkIndirizzo) riga.u = this.linkIndirizzo;
        }
        if (!riga.lg && riga.i) {
            var el = mappa.get(riga.i);
            if (el) {
                var lingua = linguaDi(el);
                if (lingua) riga.lg = lingua;
            }
        }
        this.righe.push(riga);
    };

    Costruttore.prototype.chiudiLinea = function () {
        if (!this.linea) return;
        var testo = normalizza(this.linea.parti.join(''));
        var id = this.linea.id;
        this.linea = null;
        if (!testo) return;
        var pezzi = spezza(testo, LUNGHEZZA_MASSIMA_RIGA);
        for (var i = 0; i < pezzi.length; i++) {
            if (pezzi[i]) this.emetti({ i: id, r: 'text', t: pezzi[i] });
        }
    };

    Costruttore.prototype.testo = function (nodo) {
        if (this.visNascosta) return;
        var v = nodo.nodeValue;
        if (!v) return;
        if (!this.linea) {
            if (!/\S/.test(v)) return;
            var genitore = nodo.parentElement || (nodo.parentNode && nodo.parentNode.host) || document.body;
            this.linea = { parti: [], id: idDi(genitore) };
        }
        this.linea.parti.push(v);
    };

    Costruttore.prototype.figli = function (el) {
        var figli = figliDi(el);
        for (var i = 0; i < figli.length; i++) {
            if (this.pieno) return;
            this.visita(figli[i]);
        }
    };

    Costruttore.prototype.visita = function (n) {
        if (n.nodeType === 3) {
            this.testo(n);
            return;
        }
        if (n.nodeType === 11) {
            this.figli(n);
            return;
        }
        if (n.nodeType !== 1) return;
        var el = n;
        var tag = el.localName;
        if (ELEMENTI_DA_SALTARE[tag]) return;
        if (attributo(el, 'aria-hidden') === 'true') return;
        if (tag === 'input' && (el.type || '').toLowerCase() === 'hidden') return;
        var s = stile(el);
        if (!s || s.display === 'none') return;
        if (tag === 'option' && el.parentElement && (el.parentElement.localName === 'select' || el.parentElement.localName === 'optgroup')) return;

        var vecchiaVis = this.visNascosta;
        this.visNascosta = (s.visibility === 'hidden' || s.visibility === 'collapse');
        try {
            this.elemento(el, tag, s);
        } finally {
            this.visNascosta = vecchiaVis;
        }
    };

    Costruttore.prototype.immagineSenzaNome = function (el) {
        try {
            if (this.link || this.visNascosta) return;
            var r = el.getBoundingClientRect();
            if (r.width < 30 || r.height < 30) return;
            var sorgente = el.currentSrc || el.src || attributo(el, 'data-src') || '';
            this.chiudiLinea();
            this.emetti({ i: idDi(el), r: 'img', t: nomeDaSorgente(sorgente), sn: 1, src: sorgenteSicura(sorgente) });
        } catch (e) { }
    };

    Costruttore.prototype.elemento = function (el, tag, s) {
        var ruolo = ruoloDi(el);
        var blocco = !(s.display === 'inline' || s.display === 'contents' || s.display === 'inline-block' || s.display === 'ruby' || s.display === 'ruby-text');
        if (s.position === 'absolute' || s.position === 'fixed') blocco = true;
        if (s.cssFloat && s.cssFloat !== 'none') blocco = true;

        if (tag === 'br') {
            this.chiudiLinea();
            return;
        }
        if (tag === 'iframe' || tag === 'frame') {
            this.frame(el);
            return;
        }
        if (tag === 'details' && !el.open) {
            this.chiudiLinea();
            var sommario = null;
            for (var k = 0; k < el.children.length; k++) {
                if (el.children[k].localName === 'summary') { sommario = el.children[k]; break; }
            }
            if (sommario) this.visita(sommario);
            this.chiudiLinea();
            return;
        }
        if (tag === 'svg' || el.namespaceURI === SVG_NS) {
            if (this.visNascosta) return;
            var nomeSvg = (ruolo === 'img' || haNomeEsplicito(el)) ? nomeDa(el, new Set(), false) : '';
            if (nomeSvg && !this.link) {
                this.chiudiLinea();
                this.emetti({ i: idDi(el), r: 'img', t: nomeSvg });
            }
            return;
        }
        if (tag === 'img' || tag === 'area') {
            if (this.visNascosta || ruolo !== 'img') return;
            var nomeImg = nomeDa(el, new Set(), false);
            if (nomeImg) {
                if (this.link) {
                    if (!this.linea) this.linea = { parti: [], id: idDi(el) };
                    this.linea.parti.push(' ' + nomeImg + ' ');
                    return;
                }
                this.chiudiLinea();
                this.emetti({ i: idDi(el), r: 'img', t: nomeImg, src: sorgenteSicura(el.currentSrc || el.src || '') });
            } else if (tag === 'img') {
                this.immagineSenzaNome(el);
            }
            return;
        }
        if (tag === 'canvas') {
            this.chiudiLinea();
            var prima = this.righe.length;
            this.figli(el);
            this.chiudiLinea();
            if (this.righe.length > prima || this.visNascosta) return;
            var rc = el.getBoundingClientRect();
            if (rc.width < 150 || rc.height < 100) return;
            this.canvas = true;
            this.emetti({ i: idDi(el), r: 'canvas', t: nomeDa(el, new Set(), false) || 'contenuto grafico senza testo' });
            return;
        }
        if (tag === 'video' || tag === 'audio') {
            this.chiudiLinea();
            if (this.visNascosta) return;
            var sorgente = el.currentSrc || el.src || '';
            this.emetti({ i: idDi(el), r: tag, t: nomeDa(el, new Set(), false) || nomeDaSorgente(sorgente), src: sorgenteSicura(sorgente) });
            return;
        }
        if (ruolo === 'separator') {
            this.chiudiLinea();
            return;
        }

        if (tag === 'select' || RUOLI_WIDGET[ruolo]) {
            this.chiudiLinea();
            if (this.visNascosta) {
                this.figli(el);
                return;
            }
            this.widget(el, ruolo);
            return;
        }

        if (ruolo === 'heading') {
            this.chiudiLinea();
            if (this.visNascosta) {
                this.figli(el);
                this.chiudiLinea();
                return;
            }
            this.intestazione(el);
            return;
        }

        if (!this.visNascosta && !this.link && !this.cliccabile && candidatoCliccabile(el, s, ruolo)) {
            var analisi = analizzaSottoalbero(el);
            if (analisi.widget === 0 && analisi.nodi < 2000 && analisi.testo.length <= 3000) {
                var idCliccabile = idDi(el);
                if (analisi.intestazioni === 0 && analisi.strutture === 0 && analisi.testo.length <= 400) {
                    this.chiudiLinea();
                    var nomeCliccabile = nomeDa(el, new Set(), false) || analisi.testo || analisi.immagine || 'senza nome';
                    if (ruolo === 'listitem') this.inAttesa.li = 1;
                    if (ruolo === 'paragraph') this.inAttesa.pp = 1;
                    var rigaCliccabile = { i: idCliccabile, r: 'cliccabile', t: nomeCliccabile };
                    var statiCliccabile = statiDi(el, 'cliccabile');
                    if (statiCliccabile) rigaCliccabile.s = statiCliccabile;
                    this.emetti(rigaCliccabile);
                    return;
                }
                if (ruolo !== 'listitem' && ruolo !== 'article') {
                    this.chiudiLinea();
                    var vecchioLinkC = this.link, vecchioRuoloC = this.linkRuolo, vecchioIndirizzoC = this.linkIndirizzo;
                    this.link = idCliccabile;
                    this.linkRuolo = 'cliccabile';
                    this.linkIndirizzo = '';
                    this.cliccabile += 1;
                    this.figli(el);
                    this.chiudiLinea();
                    this.cliccabile -= 1;
                    this.link = vecchioLinkC;
                    this.linkRuolo = vecchioRuoloC;
                    this.linkIndirizzo = vecchioIndirizzoC;
                    return;
                }
            }
        }

        if (blocco) this.chiudiLinea();

        var impostati = [];
        var nome;
        if (RUOLI_REGIONE[ruolo]) {
            nome = (ruolo === 'region' || ruolo === 'form' || ruolo === 'navigation' || ruolo === 'complementary' || ruolo === 'search') ? nomeDa(el, new Set(), false) : '';
            this.inAttesa.rg = RUOLI_REGIONE[ruolo] + (nome ? ' ' + nome : '');
            impostati.push('rg');
        } else if (ruolo === 'dialog' || ruolo === 'alertdialog') {
            nome = nomeDa(el, new Set(), false);
            this.inAttesa.rg = 'finestra di dialogo' + (nome ? ' ' + nome : '');
            impostati.push('rg');
        } else if (ruolo === 'list') {
            var elementi = 0;
            for (var i = 0; i < el.children.length; i++) {
                var figlio = el.children[i];
                var rf = ruoloDi(figlio);
                if (rf === 'listitem' || rf === 'term') elementi += 1;
            }
            if (elementi > 0) {
                this.inAttesa.ls = elementi;
                impostati.push('ls');
            }
        } else if (ruolo === 'listitem' || ruolo === 'term') {
            this.inAttesa.li = 1;
            impostati.push('li');
        } else if (ruolo === 'table' || ruolo === 'grid' || ruolo === 'treegrid') {
            var righeTabella = 0, colonne = 0;
            try {
                if (tag === 'table') {
                    righeTabella = el.rows.length;
                    for (var r = 0; r < Math.min(el.rows.length, 5); r++) {
                        var celle = 0;
                        for (var c = 0; c < el.rows[r].cells.length; c++) celle += el.rows[r].cells[c].colSpan || 1;
                        if (celle > colonne) colonne = celle;
                    }
                } else {
                    var righeRuolo = el.querySelectorAll('[role="row"]');
                    righeTabella = righeRuolo.length;
                    if (righeRuolo.length) colonne = righeRuolo[0].querySelectorAll('[role="cell"],[role="gridcell"],[role="columnheader"],[role="rowheader"]').length;
                }
            } catch (e) { }
            var nomeTabella = nomeDa(el, new Set(), false);
            this.inAttesa.tb = (nomeTabella ? nomeTabella + ', ' : '') + righeTabella + (righeTabella === 1 ? ' riga, ' : ' righe, ') + colonne + (colonne === 1 ? ' colonna' : ' colonne');
            impostati.push('tb');
        } else if (ruolo === 'article') {
            this.inAttesa.ar = 1;
            impostati.push('ar');
        } else if (ruolo === 'paragraph') {
            this.inAttesa.pp = 1;
            impostati.push('pp');
        } else if (ruolo === 'group' || ruolo === 'radiogroup' || ruolo === 'toolbar' || ruolo === 'tablist' || ruolo === 'menu' || ruolo === 'menubar' || ruolo === 'listbox' || ruolo === 'tree') {
            nome = nomeDa(el, new Set(), false);
            if (nome || ruolo !== 'group') {
                var descrizioneGruppo = { group: 'gruppo', radiogroup: 'gruppo di opzioni', toolbar: 'barra degli strumenti', tablist: 'elenco schede', menu: 'menu', menubar: 'barra dei menu', listbox: 'elenco', tree: 'struttura ad albero' }[ruolo];
                this.inAttesa.gr = descrizioneGruppo + (nome ? ' ' + nome : '');
                impostati.push('gr');
            }
        } else if (ruolo === 'blockquote') {
            this.inAttesa.bq = 1;
            impostati.push('bq');
        }

        var righePrima = this.righe.length;
        var dialogoPrima = this.dialogo;
        if (ruolo === 'dialog' || ruolo === 'alertdialog') this.dialogo = idDi(el);
        this.figli(el);
        if (blocco) this.chiudiLinea();
        this.dialogo = dialogoPrima;
        if (this.righe.length === righePrima) {
            for (var j = 0; j < impostati.length; j++) delete this.inAttesa[impostati[j]];
        }
    };

    Costruttore.prototype.widget = function (el, ruolo) {
        var id = idDi(el);
        if (ruolo === 'link' || ruolo === 'button') {
            var intestazioneInterna = null;
            try { intestazioneInterna = el.querySelector('h1,h2,h3,h4,h5,h6,[role="heading"]'); } catch (e) { }
            if (intestazioneInterna) {
                var vecchioLink = this.link, vecchioRuolo = this.linkRuolo, vecchioIndirizzo = this.linkIndirizzo;
                this.link = id;
                this.linkRuolo = ruolo;
                this.linkIndirizzo = ruolo === 'link' ? (el.href || '') : '';
                this.figli(el);
                this.chiudiLinea();
                this.link = vecchioLink;
                this.linkRuolo = vecchioRuolo;
                this.linkIndirizzo = vecchioIndirizzo;
                return;
            }
        }
        if (ruolo === 'treeitem') {
            this.elementoAlbero(el, id);
            return;
        }
        var riga = descrivi(el);
        if (!riga.t && (ruolo === 'link' || ruolo === 'button' || ruolo === 'menuitem' || ruolo === 'tab' || ruolo === 'option')) {
            var analisi = analizzaSottoalbero(el);
            riga.t = analisi.testo || analisi.immagine || normalizza(el.title) || (ruolo === 'link' ? nomeDaIndirizzo(el) : '');
            if (!riga.t && (ruolo === 'link' || ruolo === 'button')) riga.t = 'senza nome';
        }
        if (!riga.t && !riga.v && !riga.ed && ruolo !== 'checkbox' && ruolo !== 'radio' && ruolo !== 'switch' && ruolo !== 'combobox' && ruolo !== 'slider' && ruolo !== 'listbox') {
            return;
        }
        this.emetti(riga);
    };

    Costruttore.prototype.elementoAlbero = function (el, id) {
        var nome = normalizza(attributo(el, 'aria-label'));
        var gruppi = [];
        if (!nome) {
            var parti = [];
            var figli = figliDi(el);
            for (var i = 0; i < figli.length; i++) {
                var f = figli[i];
                if (f.nodeType === 1) {
                    var rf = ruoloEsplicito(f);
                    if (rf === 'group' || rf === 'tree' || f.localName === 'ul' || f.localName === 'ol') {
                        gruppi.push(f);
                        continue;
                    }
                    if (!nascostoPerNome(f)) parti.push(testoSottoalbero(f, new Set(), false, null));
                } else if (f.nodeType === 3) {
                    parti.push(f.nodeValue);
                }
            }
            nome = normalizza(parti.join(' '));
        } else {
            var tutti = figliDi(el);
            for (var j = 0; j < tutti.length; j++) {
                var g = tutti[j];
                if (g.nodeType === 1 && (ruoloEsplicito(g) === 'group' || g.localName === 'ul' || g.localName === 'ol')) gruppi.push(g);
            }
        }
        var riga = { i: id, r: 'treeitem', t: nome };
        var st = statiDi(el, 'treeitem');
        if (st) riga.s = st;
        var livello = parseInt(attributo(el, 'aria-level'), 10);
        if (livello > 0) riga.l = livello;
        if (nome) this.emetti(riga);
        for (var k = 0; k < gruppi.length; k++) this.visita(gruppi[k]);
    };

    Costruttore.prototype.intestazione = function (el) {
        var nome = nomeDa(el, new Set(), false);
        if (!nome) return;
        var riga = { i: idDi(el), r: 'heading', l: livelloDi(el), t: nome };
        if (!this.link) {
            var interno = widgetUnicoIn(el);
            if (interno) {
                riga.lk = idDi(interno);
                riga.lr = ruoloDi(interno);
                if (riga.lr === 'link') riga.u = interno.href || '';
                var st = statiDi(interno, riga.lr);
                if (st) riga.s = st;
            }
        }
        this.emetti(riga);
    };

    Costruttore.prototype.frame = function (el) {
        this.chiudiLinea();
        if (this.visNascosta) return;
        var doc = null;
        try { doc = el.contentDocument; } catch (e) { doc = null; }
        var nome = nomeDa(el, new Set(), false) || normalizza(attributo(el, 'name'));
        if (doc && doc.body) {
            collegaDocumento(doc);
            this.inAttesa.rg = 'frame' + (nome ? ' ' + nome : '');
            var prima = this.righe.length;
            this.visita(doc.body);
            this.chiudiLinea();
            if (this.righe.length === prima) delete this.inAttesa.rg;
            return;
        }
        var r = el.getBoundingClientRect();
        if (r.width < 2 || r.height < 2) return;
        if (nome) this.emetti({ i: idDi(el), r: 'frame', t: nome });
    };

    function radiceModale() {
        try {
            var candidati = document.querySelectorAll('dialog[open],[aria-modal="true"]');
            for (var i = candidati.length - 1; i >= 0; i--) {
                var d = candidati[i];
                if (d.localName === 'dialog') {
                    var modale = false;
                    try { modale = d.matches(':modal'); } catch (e) { modale = false; }
                    if (!modale) continue;
                }
                var s = stile(d);
                if (!s || s.display === 'none' || s.visibility === 'hidden') continue;
                if (d.getClientRects().length === 0) continue;
                var r = d.getBoundingClientRect();
                if (r.width < 5 || r.height < 5) continue;
                return d;
            }
        } catch (e) { }
        return null;
    }

    A.costruisci = function () {
        var inizio = Date.now();
        mappa.clear();
        collegaDocumento(document);
        var c = new Costruttore();
        var modale = radiceModale();
        var radice = modale || document.body || document.documentElement;
        if (radice) {
            if (modale) {
                var nomeDialogo = nomeDa(modale, new Set(), false);
                c.inAttesa.rg = 'finestra di dialogo' + (nomeDialogo ? ' ' + nomeDialogo : '');
            }
            c.visita(radice);
            c.chiudiLinea();
        }
        sporco = false;
        var attivo = elementoAttivo();
        var linguaPagina = '';
        try { linguaPagina = (document.documentElement.lang || '').toLowerCase(); } catch (e) { }
        return JSON.stringify({
            righe: c.righe,
            titolo: document.title || '',
            url: location.href,
            modale: modale ? idDi(modale) : 0,
            focus: attivo ? idDi(attivo) : 0,
            pieno: c.pieno ? 1 : 0,
            lingua: linguaPagina,
            canvas: c.canvas ? 1 : 0,
            ms: Date.now() - inizio
        });
    };

    function elementoAttivo() {
        var el = document.activeElement;
        var giri = 0;
        while (el && giri < 20) {
            giri += 1;
            if (el.shadowRoot && el.shadowRoot.activeElement) {
                el = el.shadowRoot.activeElement;
                continue;
            }
            if (el.localName === 'iframe' || el.localName === 'frame') {
                try {
                    var d = el.contentDocument;
                    if (d && d.activeElement && d.activeElement !== d.body) {
                        el = d.activeElement;
                        continue;
                    }
                } catch (e) { }
            }
            break;
        }
        if (!el || el === document.body || el === document.documentElement) return null;
        if (el.ownerDocument && (el === el.ownerDocument.body || el === el.ownerDocument.documentElement)) return null;
        return el;
    }

    function cursoreEditabile(el) {
        var doc = el.ownerDocument || document;
        var tutto = doc.createRange();
        tutto.selectNodeContents(el);
        var testo = tutto.toString();
        var inizio = testo.length, fine = testo.length;
        var rigaCorrente = '';
        try {
            var sel = doc.getSelection();
            if (sel && sel.rangeCount) {
                var r = sel.getRangeAt(0);
                if (el.contains(r.startContainer)) {
                    var pre = doc.createRange();
                    pre.selectNodeContents(el);
                    pre.setEnd(r.startContainer, r.startOffset);
                    inizio = pre.toString().length;
                    pre.setEnd(r.endContainer, r.endOffset);
                    fine = pre.toString().length;
                    var nodo = sel.focusNode;
                    if (nodo && nodo.nodeType === 3) nodo = nodo.parentElement;
                    while (nodo && nodo !== el && !eBloccoPerTesto(nodo)) nodo = nodo.parentElement;
                    if (nodo) rigaCorrente = normalizza(nodo.innerText || nodo.textContent || '');
                }
            }
        } catch (e) { }
        return { testo: testo, a: inizio, b: fine, riga: rigaCorrente };
    }

    function listaControllata(el) {
        var id = attributo(el, 'aria-controls') || attributo(el, 'aria-owns');
        if (!id) return null;
        var parti = id.split(/\s+/);
        for (var i = 0; i < parti.length; i++) {
            var l = elementoPerIdHtml(el, parti[i]);
            if (l) return l;
        }
        return null;
    }

    A.statoCampo = function () {
        var el = elementoAttivo();
        if (!el) return JSON.stringify({ i: 0 });
        var ruolo = ruoloDi(el);
        var r = { i: idDi(el), r: ruolo, t: nomeDa(el, new Set(), false) };
        var ed = infoModifica(el);
        if (ed) {
            r.ed = 1;
            r.ml = ed.ml ? 1 : 0;
            r.pw = ed.pw ? 1 : 0;
            r.ro = ed.ro ? 1 : 0;
        }
        var tag = el.localName;
        try {
            if (tag === 'input' || tag === 'textarea') {
                var valore = el.value || '';
                if (r.pw) r.n = valore.length;
                else r.v = valore;
                var a = null, b = null;
                try { a = el.selectionStart; b = el.selectionEnd; } catch (e) { a = null; }
                if (a === null || a === undefined) { a = valore.length; b = valore.length; }
                r.a = a;
                r.b = b;
            } else if (el.isContentEditable) {
                var info = cursoreEditabile(el);
                r.v = info.testo;
                r.a = info.a;
                r.b = info.b;
                r.rc = info.riga;
            } else if (tag === 'select') {
                var o = el.options[el.selectedIndex];
                r.v = o ? normalizza(o.text) : '';
                r.n = el.options.length;
                r.x = el.selectedIndex + 1;
            } else {
                r.v = valoreDi(el, ruolo);
            }
        } catch (e) { }
        var st = statiDi(el, ruolo);
        if (st) r.s = st;
        r.e = attributo(el, 'aria-expanded') === 'true' ? 1 : 0;
        var attivoDiscendente = attributo(el, 'aria-activedescendant');
        if (attivoDiscendente) {
            var opzione = elementoPerIdHtml(el, attivoDiscendente);
            if (opzione) {
                r.d = nomeDa(opzione, new Set(), false) || testoSottoalbero(opzione, new Set(), false, null);
                var so = statiDi(opzione, ruoloDi(opzione));
                if (so) r.ds = so;
            }
        }
        if (!r.d && (ruolo === 'combobox' || attributo(el, 'aria-autocomplete'))) {
            var lista = listaControllata(el);
            if (lista && !nascostoPerNome(lista)) {
                var scelta = null;
                try { scelta = lista.querySelector('[aria-selected="true"]'); } catch (e) { }
                if (scelta) r.d = nomeDa(scelta, new Set(), false) || testoSottoalbero(scelta, new Set(), false, null);
                try { r.no = lista.querySelectorAll('[role="option"]').length; } catch (e) { }
            }
        }
        var lingua = linguaDi(el);
        if (lingua) r.lg = lingua;
        return JSON.stringify(r);
    };

    A.descriviAttivo = function () {
        var el = elementoAttivo();
        if (!el) return JSON.stringify({ i: 0 });
        return JSON.stringify(descrivi(el));
    };

    A.elemento = function (id) {
        return elementoDaId(id);
    };

    A.descrivi = function (id) {
        var el = elementoDaId(id);
        if (!el) return JSON.stringify({ i: 0 });
        return JSON.stringify(descrivi(el));
    };

    A.descriviElemento = function (el) {
        if (!el || el.nodeType !== 1) return JSON.stringify({ i: 0 });
        return JSON.stringify(descrivi(el));
    };

    function coordinateNelFrame(el) {
        var r = el.getBoundingClientRect();
        var x = r.left;
        var y = r.top;
        var vista = el.ownerDocument.defaultView;
        while (vista && vista !== window && vista.frameElement) {
            var rf = vista.frameElement.getBoundingClientRect();
            x += rf.left + (vista.frameElement.clientLeft || 0);
            y += rf.top + (vista.frameElement.clientTop || 0);
            vista = vista.parent;
        }
        return { x: x + r.width / 2, y: y + r.height / 2, l: x, t: y, w: r.width, h: r.height };
    }

    function scorriInVista(el) {
        try {
            el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'instant' });
        } catch (e) {
            try { el.scrollIntoView(); } catch (e2) { }
        }
    }

    A.mostra = function (id) {
        var el = elementoDaId(id);
        if (!el) return false;
        try {
            var r = el.getBoundingClientRect();
            var vista = el.ownerDocument.defaultView || window;
            if (r.bottom < 0 || r.top > vista.innerHeight || r.right < 0 || r.left > vista.innerWidth) scorriInVista(el);
        } catch (e) {
            scorriInVista(el);
        }
        return true;
    };

    A.punto = function (id) {
        var el = elementoDaId(id);
        if (!el) return JSON.stringify({ ok: 0, manca: 1 });
        scorriInVista(el);
        var c = coordinateNelFrame(el);
        if (c.w < 1 || c.h < 1) return JSON.stringify({ ok: 0 });
        if (c.x < 0 || c.y < 0 || c.x >= window.innerWidth || c.y >= window.innerHeight) return JSON.stringify({ ok: 0 });
        var colpito = null;
        try { colpito = document.elementFromPoint(c.x, c.y); } catch (e) { }
        var dentro = false;
        var n = colpito;
        var giri = 0;
        while (n && giri < 50) {
            giri += 1;
            if (n === el) { dentro = true; break; }
            if (n.localName === 'iframe' && el.ownerDocument !== document) { dentro = true; break; }
            n = n.parentElement || (n.parentNode && n.parentNode.host) || null;
        }
        if (!dentro && colpito && el.contains && el.contains(colpito)) dentro = true;
        if (!dentro && colpito && colpito.localName === 'label' && colpito.control === el) dentro = true;
        return JSON.stringify({ ok: dentro ? 1 : 0, x: Math.round(c.x), y: Math.round(c.y) });
    };

    A.rettangolo = function (id, scorri) {
        var el = elementoDaId(id);
        if (!el) return JSON.stringify({ ok: 0 });
        if (scorri) scorriInVista(el);
        var c = coordinateNelFrame(el);
        if (c.w < 1 || c.h < 1) return JSON.stringify({ ok: 0 });
        return JSON.stringify({ ok: 1, x: c.l, y: c.t, w: c.w, h: c.h });
    };

    A.indirizzo = function (id) {
        var e = elementoDaId(id);
        var giri = 0;
        while (e && giri < 8) {
            giri += 1;
            if (e.href && typeof e.href === 'string') return e.href;
            if (e.currentSrc) return e.currentSrc;
            if (e.src && typeof e.src === 'string') return e.src;
            e = e.parentElement;
        }
        return '';
    };

    function mediaDi(el) {
        if (!el) return null;
        if (el.localName === 'video' || el.localName === 'audio') return el;
        try { return el.querySelector('video,audio'); } catch (e) { return null; }
    }

    A.sorgenteMedia = function (id) {
        var m = mediaDi(elementoDaId(id));
        if (!m) return '';
        return m.currentSrc || m.src || '';
    };

    A.comandoMedia = function (id, c) {
        var m = mediaDi(elementoDaId(id));
        if (!m) return 'nessun';
        try {
            if (c === 'play') {
                var p = m.play();
                if (p && p.catch) p.catch(function () { });
                return 'avviato';
            }
            if (c === 'pause') { m.pause(); return 'pausa'; }
            if (c === 'muto') { m.muted = !m.muted; return m.muted ? 'muto' : 'audio'; }
            if (c === 'avanti') { m.currentTime = Math.min(m.duration || 0, m.currentTime + 10); return 'avanti'; }
            if (c === 'indietro') { m.currentTime = Math.max(0, m.currentTime - 10); return 'indietro'; }
            if (c === 'stato') {
                var d = isFinite(m.duration) ? Math.round(m.duration) : 0;
                return (m.paused ? 'in pausa' : 'in riproduzione') + '|' + Math.round(m.currentTime) + '|' + d + '|' + (m.error ? m.error.code : 0);
            }
        } catch (e) {
            return 'errore';
        }
        return '';
    };

    A.clic = function (id) {
        var el = elementoDaId(id);
        if (!el) return false;
        try { el.focus({ preventScroll: true }); } catch (e) { }
        try {
            if (typeof el.click === 'function') {
                el.click();
            } else {
                el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
            }
        } catch (e) {
            return false;
        }
        return true;
    };

    A.focalizza = function (id) {
        var el = elementoDaId(id);
        if (!el) return JSON.stringify({ i: 0 });
        scorriInVista(el);
        try { el.focus(); } catch (e) { }
        return A.statoCampo();
    };

    A.sfoca = function () {
        var el = elementoAttivo();
        if (el) {
            try { el.blur(); } catch (e) { }
        }
        return true;
    };

    A.preparaTab = function (id) {
        return A.preparaTabElemento(elementoDaId(id));
    };

    A.preparaTabElemento = function (el) {
        if (!el) return false;
        try {
            var attivo = elementoAttivo();
            if (attivo && attivo !== el && !el.contains(attivo) && !attivo.contains(el)) attivo.blur();
            if (focalizzabile(el) && attivo !== el) {
                el.focus({ preventScroll: true });
                return true;
            }
            var doc = el.ownerDocument || document;
            var sel = doc.getSelection();
            if (sel) {
                var r = doc.createRange();
                r.setStart(el, 0);
                r.collapse(true);
                sel.removeAllRanges();
                sel.addRange(r);
            }
        } catch (e) { }
        return true;
    };

    A.sporco = function () {
        return sporco;
    };

    A.azzeraSporco = function () {
        sporco = false;
        return true;
    };

    function impostaValore(el, valore) {
        try {
            try { el.focus({ preventScroll: true }); } catch (e) { }
            if (el.localName === 'select') {
                var cercato = String(valore).toLowerCase();
                for (var i = 0; i < el.options.length; i++) {
                    var o = el.options[i];
                    if (o.value.toLowerCase() === cercato || normalizza(o.text).toLowerCase() === cercato) {
                        el.selectedIndex = i;
                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        el.dispatchEvent(new Event('change', { bubbles: true }));
                        return true;
                    }
                }
                return false;
            }
            var prototipo = el.localName === 'textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            var descrittore = Object.getOwnPropertyDescriptor(prototipo, 'value');
            if (descrittore && descrittore.set) descrittore.set.call(el, valore); else el.value = valore;
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch (e) {
            return false;
        }
    }

    function campoUtente(el) {
        if (!el || el.localName !== 'input') return false;
        var t = (el.type || 'text').toLowerCase();
        if (t !== 'text' && t !== 'email' && t !== 'tel') return false;
        var descrizione = ((attributo(el, 'autocomplete') || '') + ' ' + (el.name || '') + ' ' + (el.id || '') + ' ' + (attributo(el, 'placeholder') || '') + ' ' + (attributo(el, 'aria-label') || '')).toLowerCase();
        if (t === 'email') return true;
        return /(user|utente|login|email|e-mail|mail|account|identifier|username|nome utente|codice cliente|telefono|phone)/.test(descrizione);
    }

    function campiLogin(documento) {
        var password = null;
        try {
            var elenco = documento.querySelectorAll('input[type="password"]');
            for (var i = 0; i < elenco.length; i++) {
                if (visibile(elenco[i])) { password = elenco[i]; break; }
            }
        } catch (e) { }
        var utente = null;
        var generico = null;
        try {
            var radice = (password && password.form) ? password.form : documento;
            var candidati = radice.querySelectorAll('input:not([type]),input[type="text"],input[type="email"],input[type="tel"]');
            for (var k = 0; k < candidati.length; k++) {
                var c = candidati[k];
                if (!visibile(c)) continue;
                if (password && !(c.compareDocumentPosition(password) & 4)) continue;
                if (campoUtente(c)) utente = c;
                else if (password) generico = c;
            }
        } catch (e) { }
        return { password: password, utente: utente || generico };
    }

    var ultimaCattura = '';
    var istanteUltimaCattura = 0;

    function catturaCredenziali() {
        try {
            var campi = campiLogin(document);
            if (!campi.password || !campi.password.value) return;
            var utente = campi.utente && campi.utente.value ? campi.utente.value : '';
            if (!utente) {
                try { utente = sessionStorage.getItem('__wgUtente') || ''; } catch (e) { utente = ''; }
            }
            var firma = utente + '\u0001' + campi.password.value.length;
            var ora = Date.now();
            if (firma === ultimaCattura && ora - istanteUltimaCattura < 4000) return;
            ultimaCattura = firma;
            istanteUltimaCattura = ora;
            invia({ tipo: 'credenziali', origine: location.origin, utente: utente, password: campi.password.value });
        } catch (e) { }
    }

    function ricordaUtente(el) {
        try {
            if (!campoUtente(el) || !el.value) return;
            sessionStorage.setItem('__wgUtente', el.value);
        } catch (e) { }
    }

    A.compilaCredenziali = function (utente, password) {
        var compilati = 0;
        try {
            var campi = campiLogin(document);
            if (campi.utente && utente && (!campi.utente.value || campi.utente === elementoAttivo())) {
                if (impostaValore(campi.utente, utente)) compilati += 1;
            }
            if (campi.password && password) {
                if (impostaValore(campi.password, password)) compilati += 1;
            }
            if (!campi.password && !campi.utente && utente) {
                var attivo = elementoAttivo();
                if (attivo && campoUtente(attivo) && impostaValore(attivo, utente)) compilati += 1;
            }
        } catch (e) { }
        return compilati;
    };

    A.haCampoLogin = function () {
        try {
            var campi = campiLogin(document);
            return !!(campi.password || campi.utente);
        } catch (e) {
            return false;
        }
    };

    var REGOLE_MODULO = [
        { k: 'email', ac: ['email'], re: /(e-?mail|posta elettronica)/i, tipo: 'email' },
        { k: 'telefono', ac: ['tel', 'tel-national', 'tel-local', 'mobile'], re: /(telefono|phone|cellulare|mobile|cell|tel$|^tel)/i, tipo: 'tel' },
        { k: 'cognome', ac: ['family-name', 'lname'], re: /(cognome|last.?name|surname|family.?name|lname|nachname)/i },
        { k: 'nome', ac: ['given-name', 'fname'], re: /(^|[^a-z])(nome|first.?name|given.?name|fname|firstname|vorname|pr[eé]nom)/i, no: /(cognome|last|user|utente|azienda|company|societ)/i },
        { k: 'codicefiscale', ac: [], re: /(codice.?fiscale|fiscal.?code|tax.?code|^cf$|codfisc)/i },
        { k: 'datanascita', ac: ['bday'], re: /(nascita|birth|bday|dob)/i },
        { k: 'cap', ac: ['postal-code'], re: /(^cap$|cap[^a-z]|postal|zip|postcode|codice.?postale|plz)/i },
        { k: 'citta', ac: ['address-level2'], re: /(citt|city|comune|localit|town)/i },
        { k: 'provincia', ac: ['address-level1'], re: /(provincia|province|state|region|^prov)/i },
        { k: 'paese', ac: ['country', 'country-name'], re: /(paese|country|nazione)/i },
        { k: 'azienda', ac: ['organization'], re: /(azienda|company|organi[sz]ation|ragione.?sociale|ditta)/i },
        { k: 'indirizzo', ac: ['street-address', 'address-line1'], re: /(indirizzo|address|via|street|strasse)/i, no: /(e-?mail|ip|mail)/i }
    ];

    function descrizioneCampo(el) {
        var parti = [attributo(el, 'autocomplete') || '', el.name || '', el.id || '', attributo(el, 'placeholder') || '', attributo(el, 'aria-label') || ''];
        try {
            if (el.labels) for (var i = 0; i < el.labels.length; i++) parti.push(el.labels[i].textContent || '');
        } catch (e) { }
        return normalizza(parti.join(' '));
    }

    A.compilaModulo = function (dati) {
        var compilati = 0;
        try {
            if (typeof dati === 'string') dati = JSON.parse(dati);
            if (!dati) return 0;
            var attivo = elementoAttivo();
            var radice = (attivo && attivo.form) ? attivo.form : document;
            var campi = radice.querySelectorAll('input,select,textarea');
            for (var i = 0; i < campi.length; i++) {
                var el = campi[i];
                if (el.disabled || el.readOnly || !visibile(el)) continue;
                var tipo = (el.type || '').toLowerCase();
                if (el.localName === 'input' && ['hidden', 'password', 'checkbox', 'radio', 'submit', 'button', 'file', 'image', 'reset', 'range', 'color'].indexOf(tipo) >= 0) continue;
                if (el.localName !== 'select' && el.value) continue;
                var ac = (attributo(el, 'autocomplete') || '').toLowerCase();
                var descrizione = descrizioneCampo(el);
                var valore = '';
                if (ac === 'name' && dati.nome) valore = (dati.nome + ' ' + (dati.cognome || '')).trim();
                if (!valore) {
                    for (var r = 0; r < REGOLE_MODULO.length; r++) {
                        var regola = REGOLE_MODULO[r];
                        if (!dati[regola.k]) continue;
                        var corrisponde = false;
                        for (var a = 0; a < regola.ac.length; a++) if (ac.indexOf(regola.ac[a]) >= 0) corrisponde = true;
                        if (!corrisponde && regola.tipo && tipo === regola.tipo) corrisponde = true;
                        if (!corrisponde && regola.re.test(descrizione) && !(regola.no && regola.no.test(descrizione))) corrisponde = true;
                        if (corrisponde) { valore = dati[regola.k]; break; }
                    }
                }
                if (valore && impostaValore(el, valore)) compilati += 1;
            }
        } catch (e) { }
        return compilati;
    };

    var statoNotifiche = (function () {
        try {
            var mappa = window.__wgNotifiche;
            if (mappa && typeof mappa === 'object') {
                var valore = mappa[location.origin];
                if (valore === 'granted' || valore === 'denied') return valore;
            }
        } catch (e) { }
        return 'default';
    })();
    var attesePermesso = {};
    var progressivoPermesso = 0;

    function inviaNotifica(titolo, opzioni) {
        try {
            opzioni = opzioni || {};
            invia({ tipo: 'notifica', titolo: normalizza(String(titolo || '')), testo: normalizza(String(opzioni.body || '')), origine: location.origin });
        } catch (e) { }
    }

    function NotificaWG(titolo, opzioni) {
        if (!(this instanceof NotificaWG)) throw new TypeError("Failed to construct 'Notification': Please use the 'new' operator.");
        opzioni = opzioni || {};
        this.title = String(titolo);
        this.body = String(opzioni.body || '');
        this.tag = String(opzioni.tag || '');
        this.icon = String(opzioni.icon || '');
        this.data = opzioni.data === undefined ? null : opzioni.data;
        this.onclick = null;
        this.onclose = null;
        this.onshow = null;
        this.onerror = null;
        this._ascoltatori = {};
        var self = this;
        if (statoNotifiche === 'granted') {
            inviaNotifica(this.title, opzioni);
            setTimeout(function () { self._emetti('show'); }, 0);
        } else {
            setTimeout(function () { self._emetti('error'); }, 0);
        }
    }

    NotificaWG.prototype._emetti = function (tipo) {
        try {
            var evento = new Event(tipo);
            var gestore = this['on' + tipo];
            if (typeof gestore === 'function') gestore.call(this, evento);
            var elenco = this._ascoltatori[tipo] || [];
            for (var i = 0; i < elenco.length; i++) {
                try { elenco[i].call(this, evento); } catch (e) { }
            }
        } catch (e) { }
    };
    NotificaWG.prototype.close = function () { this._emetti('close'); };
    NotificaWG.prototype.addEventListener = function (tipo, f) {
        if (typeof f !== 'function') return;
        (this._ascoltatori[tipo] = this._ascoltatori[tipo] || []).push(f);
    };
    NotificaWG.prototype.removeEventListener = function (tipo, f) {
        var elenco = this._ascoltatori[tipo] || [];
        var i = elenco.indexOf(f);
        if (i >= 0) elenco.splice(i, 1);
    };
    NotificaWG.prototype.dispatchEvent = function () { return true; };
    try {
        Object.defineProperty(NotificaWG, 'permission', { get: function () { return statoNotifiche; }, configurable: true });
    } catch (e) { }
    NotificaWG.maxActions = 2;
    NotificaWG.requestPermission = function (richiamo) {
        return new Promise(function (risolvi) {
            if (statoNotifiche !== 'default') {
                if (typeof richiamo === 'function') { try { richiamo(statoNotifiche); } catch (e) { } }
                risolvi(statoNotifiche);
                return;
            }
            progressivoPermesso += 1;
            var id = progressivoPermesso;
            attesePermesso[id] = function (valore) {
                statoNotifiche = valore;
                if (typeof richiamo === 'function') { try { richiamo(valore); } catch (e) { } }
                risolvi(valore);
            };
            invia({ tipo: 'permessoNotifiche', id: id, origine: location.origin });
        });
    };

    try {
        Object.defineProperty(window, 'Notification', { value: NotificaWG, configurable: true, writable: true });
    } catch (e) {
        try { window.Notification = NotificaWG; } catch (e2) { }
    }
    try {
        if (window.ServiceWorkerRegistration && window.ServiceWorkerRegistration.prototype) {
            window.ServiceWorkerRegistration.prototype.showNotification = function (titolo, opzioni) {
                if (statoNotifiche === 'granted') inviaNotifica(titolo, opzioni);
                return Promise.resolve();
            };
            window.ServiceWorkerRegistration.prototype.getNotifications = function () { return Promise.resolve([]); };
        }
    } catch (e) { }
    try {
        if (navigator.permissions && typeof navigator.permissions.query === 'function') {
            var interrogaOriginale = navigator.permissions.query.bind(navigator.permissions);
            navigator.permissions.query = function (descrittore) {
                if (descrittore && (descrittore.name === 'notifications' || descrittore.name === 'push')) {
                    return Promise.resolve({ name: descrittore.name, state: statoNotifiche === 'default' ? 'prompt' : statoNotifiche, onchange: null, addEventListener: function () { }, removeEventListener: function () { }, dispatchEvent: function () { return true; } });
                }
                return interrogaOriginale(descrittore);
            };
        }
    } catch (e) { }
    try {
        if (navigator.requestMediaKeySystemAccess) {
            var richiestaOriginale = navigator.requestMediaKeySystemAccess.bind(navigator);
            navigator.requestMediaKeySystemAccess = function (sistema, configurazioni) {
                invia({ tipo: 'mediaProtetto', sistema: String(sistema || '') });
                return richiestaOriginale(sistema, configurazioni);
            };
        }
    } catch (e) { }

    var attesaCondivisione = {};
    var tracceCondivise = [];

    function codiceCasuale() {
        return Math.random().toString(36).slice(2) + Date.now().toString(36);
    }

    function registraTracciaCondivisa(traccia) {
        try {
            if (!traccia || tracceCondivise.indexOf(traccia) >= 0) return;
            tracceCondivise.push(traccia);
            var cloneOriginale = traccia.clone;
            if (typeof cloneOriginale === 'function') {
                traccia.clone = function () {
                    var copia = cloneOriginale.apply(traccia, arguments);
                    registraTracciaCondivisa(copia);
                    return copia;
                };
            }
        } catch (e) { }
    }

    function sorvegliaCondivisione(flusso) {
        try {
            var tracce = flusso.getTracks();
            for (var i = 0; i < tracce.length; i++) registraTracciaCondivisa(tracce[i]);
            var cloneFlusso = flusso.clone;
            if (typeof cloneFlusso === 'function') {
                flusso.clone = function () {
                    var copia = cloneFlusso.apply(flusso, arguments);
                    try {
                        var nuove = copia.getTracks();
                        for (var j = 0; j < nuove.length; j++) registraTracciaCondivisa(nuove[j]);
                    } catch (e) { }
                    return copia;
                };
            }
            var video = flusso.getVideoTracks();
            if (!video.length) return;
            var traccia = video[0];
            var finita = false;
            var segnala = function () {
                if (finita) return;
                finita = true;
                invia({ tipo: 'condivisioneFinita' });
            };
            traccia.addEventListener('ended', segnala);
            var fermaOriginale = traccia.stop;
            traccia.stop = function () {
                segnala();
                return fermaOriginale.apply(traccia, arguments);
            };
        } catch (e) { }
    }

    function opzioniCondivisione(opzioni, superficie) {
        var copia = {};
        try {
            if (opzioni && typeof opzioni === 'object') {
                for (var chiave in opzioni) copia[chiave] = opzioni[chiave];
            }
        } catch (e) { }
        var finestra = superficie === 'finestra';
        try {
            var video = copia.video;
            if (video === undefined || video === null || video === true) video = {};
            if (video && typeof video === 'object') {
                var nuovoVideo = {};
                for (var k in video) nuovoVideo[k] = video[k];
                nuovoVideo.displaySurface = finestra ? 'window' : 'monitor';
                copia.video = nuovoVideo;
            }
        } catch (e) { }
        copia.preferCurrentTab = false;
        copia.selfBrowserSurface = 'exclude';
        copia.monitorTypeSurfaces = finestra ? 'exclude' : 'include';
        return copia;
    }

    try {
        var protoDispositivi = window.MediaDevices && window.MediaDevices.prototype;
        if (protoDispositivi && typeof protoDispositivi.getDisplayMedia === 'function') {
            var mostraOriginale = protoDispositivi.getDisplayMedia;
            var sostitutoCondivisione = function getDisplayMedia(opzioni) {
                var dispositivi = this;
                return new Promise(function (risolvi, rifiuta) {
                    var id = codiceCasuale();
                    var audio = !!(opzioni && opzioni.audio);
                    attesaCondivisione[id] = { risolvi: risolvi, rifiuta: rifiuta, opzioni: opzioni, dispositivi: dispositivi, mostra: mostraOriginale };
                    inviaSuEntrambi({ tipo: 'condivisione', id: id, audio: audio ? 1 : 0, origine: location.origin });
                });
            };
            Object.defineProperty(protoDispositivi, 'getDisplayMedia', { value: sostitutoCondivisione, configurable: true, writable: true, enumerable: true });
        }
    } catch (e) { }

    try {
        if ('documentPictureInPicture' in window) {
            try { delete window.documentPictureInPicture; } catch (e) { }
            if ('documentPictureInPicture' in window) {
                Object.defineProperty(window, 'documentPictureInPicture', { value: undefined, configurable: true, writable: true });
            }
        }
    } catch (e) { }

    function nonDisponibile(funzione) {
        invia({ tipo: 'funzioneNonDisponibile', funzione: funzione });
    }

    function erroreNonConsentito() {
        try {
            return new DOMException('The request is not allowed by the user agent or the platform in the current context.', 'NotAllowedError');
        } catch (e) {
            return new Error('NotAllowedError');
        }
    }

    function togli(oggetto, nome) {
        try {
            if (!oggetto || !(nome in oggetto)) return;
            try { delete oggetto[nome]; } catch (e) { }
            if (nome in oggetto) Object.defineProperty(oggetto, nome, { value: undefined, configurable: true, writable: true });
        } catch (e) { }
    }

    try {
        var stampaWG = function print() {
            invia({ tipo: 'stampa', origine: location.origin });
        };
        Object.defineProperty(window, 'print', { value: stampaWG, configurable: true, writable: true, enumerable: true });
    } catch (e) {
        try { window.print = function () { invia({ tipo: 'stampa', origine: location.origin }); }; } catch (e2) { }
    }

    try {
        togli(window.Navigator && Navigator.prototype, 'share');
        togli(window.Navigator && Navigator.prototype, 'canShare');
        if (typeof navigator.share === 'function') {
            navigator.share = function share() {
                nonDisponibile('condivisione');
                return Promise.reject(erroreNonConsentito());
            };
        }
    } catch (e) { }

    try {
        togli(window, 'showOpenFilePicker');
        togli(window, 'showSaveFilePicker');
        togli(window, 'showDirectoryPicker');
        togli(window, 'EyeDropper');
        togli(window, 'PaymentRequest');
    } catch (e) { }

    try {
        if (window.HTMLVideoElement && HTMLVideoElement.prototype && typeof HTMLVideoElement.prototype.requestPictureInPicture === 'function') {
            Object.defineProperty(HTMLVideoElement.prototype, 'requestPictureInPicture', {
                value: function requestPictureInPicture() {
                    nonDisponibile('immagineNellImmagine');
                    return Promise.reject(erroreNonConsentito());
                },
                configurable: true,
                writable: true
            });
        }
        if (window.Document && Document.prototype && 'pictureInPictureEnabled' in Document.prototype) {
            Object.defineProperty(Document.prototype, 'pictureInPictureEnabled', { get: function () { return false; }, configurable: true });
        }
    } catch (e) { }

    function senzaImmagineNellImmagine(ev) {
        try {
            var el = ev.target;
            if (el && el.localName === 'video' && !el.disablePictureInPicture) el.disablePictureInPicture = true;
        } catch (e) { }
    }

    var attesaColore = {};

    function eCampoColore(el) {
        try {
            return !!(el && el.localName === 'input' && String(el.type || '').toLowerCase() === 'color');
        } catch (e) {
            return false;
        }
    }

    function chiediColore(el) {
        try {
            if (!eCampoColore(el) || el.disabled) return false;
            var id = codiceCasuale();
            attesaColore[id] = el;
            var etichetta = '';
            try { etichetta = nomeDa(el, new Set(), false) || ''; } catch (e) { }
            invia({ tipo: 'colore', id: id, valore: String(el.value || '#000000'), etichetta: etichetta, origine: location.origin });
            return true;
        } catch (e) {
            return false;
        }
    }

    function suClicColore(ev) {
        try {
            if (ev.defaultPrevented || !eCampoColore(ev.target)) return;
            ev.preventDefault();
            chiediColore(ev.target);
        } catch (e) { }
    }

    try {
        var protoCampo = window.HTMLInputElement && HTMLInputElement.prototype;
        if (protoCampo && typeof protoCampo.showPicker === 'function') {
            var sceltaOriginale = protoCampo.showPicker;
            Object.defineProperty(protoCampo, 'showPicker', {
                value: function showPicker() {
                    if (eCampoColore(this)) {
                        chiediColore(this);
                        return undefined;
                    }
                    return sceltaOriginale.apply(this, arguments);
                },
                configurable: true,
                writable: true
            });
        }
    } catch (e) { }

    A.impostaColore = function (id, valore) {
        var el = attesaColore[id];
        if (!el) return false;
        delete attesaColore[id];
        var colore = String(valore || '').trim().toLowerCase();
        if (!/^#[0-9a-f]{6}$/.test(colore)) return true;
        try {
            var descrittore = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
            if (descrittore && descrittore.set) descrittore.set.call(el, colore); else el.value = colore;
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
        } catch (e) { }
        return true;
    };

    A.rispostaCondivisione = function (id, superficie) {
        var attesa = attesaCondivisione[id];
        if (!attesa) return false;
        delete attesaCondivisione[id];
        if (superficie !== 'schermo' && superficie !== 'finestra') {
            attesa.rifiuta(erroreNonConsentito());
            return true;
        }
        var attesa_superficie = superficie === 'finestra' ? 'window' : 'monitor';
        var tentativo;
        try {
            tentativo = attesa.mostra.call(attesa.dispositivi, opzioniCondivisione(attesa.opzioni, superficie));
        } catch (e) {
            tentativo = Promise.reject(e);
        }
        tentativo.then(function (flusso) {
            var ottenuta = '';
            try {
                var tracce = flusso.getVideoTracks();
                if (tracce.length && typeof tracce[0].getSettings === 'function') ottenuta = String(tracce[0].getSettings().displaySurface || '');
            } catch (e) { }
            if (ottenuta && ottenuta !== attesa_superficie) {
                try {
                    var tutte = flusso.getTracks();
                    for (var i = 0; i < tutte.length; i++) tutte[i].stop();
                } catch (e) { }
                invia({ tipo: 'condivisioneNonRiuscita', motivo: 'superficie' });
                attesa.rifiuta(erroreNonConsentito());
                return;
            }
            sorvegliaCondivisione(flusso);
            invia({ tipo: 'condivisioneAvviata', superficie: ottenuta || attesa_superficie });
            attesa.risolvi(flusso);
        }, function (errore) {
            var motivo = '';
            try { motivo = errore && errore.name ? String(errore.name) : ''; } catch (e) { }
            invia({ tipo: 'condivisioneNonRiuscita', motivo: motivo });
            attesa.rifiuta(errore);
        });
        return true;
    };

    A.fermaCondivisione = function () {
        var fermate = 0;
        var elenco = tracceCondivise.slice();
        tracceCondivise.length = 0;
        for (var i = 0; i < elenco.length; i++) {
            var traccia = elenco[i];
            try {
                if (traccia.readyState === 'ended') continue;
                traccia.stop();
                fermate++;
                try { traccia.dispatchEvent(new Event('ended')); } catch (e) { }
            } catch (e) { }
        }
        return fermate;
    };

    A.permessoNotifiche = function (valore) {
        if (valore === 'granted' || valore === 'denied' || valore === 'default') statoNotifiche = valore;
        return statoNotifiche;
    };

    A.rispostaPermesso = function (id, valore, origine) {
        if (origine && origine !== location.origin) return false;
        var f = attesePermesso[id];
        delete attesePermesso[id];
        if (valore === 'granted' || valore === 'denied' || valore === 'default') statoNotifiche = valore;
        if (f) f(statoNotifiche);
        return true;
    };

    A.abilitaFlutter = function () {
        try {
            var segnaposto = document.querySelector('flt-semantics-placeholder');
            if (!segnaposto) {
                var viste = document.querySelectorAll('flutter-view,flt-glass-pane');
                for (var i = 0; i < viste.length && !segnaposto; i++) {
                    if (viste[i].shadowRoot) segnaposto = viste[i].shadowRoot.querySelector('flt-semantics-placeholder');
                }
            }
            if (!segnaposto) return false;
            ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function (tipo) {
                try {
                    var Costr = (tipo.indexOf('pointer') === 0 && window.PointerEvent) ? window.PointerEvent : MouseEvent;
                    segnaposto.dispatchEvent(new Costr(tipo, { bubbles: true, cancelable: true, view: window }));
                } catch (e) { }
            });
            try { segnaposto.click(); } catch (e) { }
            return true;
        } catch (e) {
            return false;
        }
    };

    A.testoSelezionato = function () {
        try {
            var a = elementoAttivo();
            if (a && (a.localName === 'input' || a.localName === 'textarea') && typeof a.selectionStart === 'number') return a.value.substring(a.selectionStart, a.selectionEnd);
            return String(window.getSelection() || '');
        } catch (e) {
            return '';
        }
    };

    var inAttesaLive = [];
    var timerLive = 0;
    var ultimoLive = '';
    var istanteUltimoLive = 0;

    function regioneLive(nodo) {
        var el = nodo && nodo.nodeType === 1 ? nodo : (nodo ? nodo.parentElement : null);
        var giri = 0;
        while (el && giri < 60) {
            giri += 1;
            var live = attributo(el, 'aria-live');
            if (live === 'off') return null;
            if (live === 'polite' || live === 'assertive') {
                return { el: el, assertivo: live === 'assertive', atomico: attributo(el, 'aria-atomic') === 'true' };
            }
            var r = ruoloEsplicito(el);
            if (r === 'alert') return { el: el, assertivo: true, atomico: true };
            if (r === 'status' || r === 'log') return { el: el, assertivo: false, atomico: r === 'status' };
            if (el.localName === 'output') return { el: el, assertivo: false, atomico: true };
            el = el.parentElement || (el.parentNode && el.parentNode.host) || null;
        }
        return null;
    }

    function accodaLive(testo, assertivo) {
        testo = normalizza(testo);
        if (!testo) return;
        if (testo.length > 500) testo = testo.substring(0, 500);
        for (var i = 0; i < inAttesaLive.length; i++) if (inAttesaLive[i].t === testo) return;
        inAttesaLive.push({ t: testo, a: assertivo });
        if (!timerLive) timerLive = setTimeout(scaricaLive, 150);
    }

    function scaricaLive() {
        timerLive = 0;
        var elenco = inAttesaLive;
        inAttesaLive = [];
        if (!elenco.length) return;
        var assertivo = elenco.some(function (x) { return x.a; });
        var testo = elenco.map(function (x) { return x.t; }).join('. ');
        var ora = Date.now();
        if (testo === ultimoLive && ora - istanteUltimoLive < 1500) return;
        ultimoLive = testo;
        istanteUltimoLive = ora;
        invia({ tipo: 'live', testo: testo, assertivo: assertivo ? 1 : 0 });
    }

    var istanteUltimoSporco = 0;
    var timerSporco = 0;

    function inviaSporco() {
        istanteUltimoSporco = Date.now();
        invia({ tipo: 'sporco' });
    }

    function segnaSporco() {
        sporco = true;
        var ora = Date.now();
        if (ora - istanteUltimoSporco >= 300) {
            inviaSporco();
        } else if (!timerSporco) {
            timerSporco = setTimeout(function () {
                timerSporco = 0;
                inviaSporco();
            }, Math.max(20, 300 - (ora - istanteUltimoSporco)));
        }
    }

    var ATTRIBUTI_RILEVANTI = {
        hidden: 1, 'aria-hidden': 1, 'aria-expanded': 1, open: 1, 'aria-modal': 1, 'aria-checked': 1,
        'aria-selected': 1, 'aria-pressed': 1, 'aria-label': 1, 'aria-current': 1, disabled: 1, value: 1,
        'aria-valuenow': 1, 'aria-valuetext': 1
    };

    var ultimoAttivo = null;
    function annunciaDiscendenteAttivo(el) {
        try {
            var doc = el.ownerDocument || document;
            if (doc.activeElement !== el) return;
            var idHtml = attributo(el, 'aria-activedescendant');
            if (!idHtml) return;
            var d = elementoPerIdHtml(el, idHtml);
            if (!d || d === ultimoAttivo) return;
            ultimoAttivo = d;
            invia({ tipo: 'attivo', riga: descrivi(d) });
        } catch (e) { }
    }

    var ultimoStatoInviato = '';
    var ultimoInputUtente = 0;
    function suInputUtente() {
        ultimoInputUtente = Date.now();
    }

    function statoCambiato(el, attributoCambiato) {
        try {
            var attivo = elementoAttivo();
            if (!attivo) return;
            if (el === attivo) {
                var ruoloStato = '';
                try { ruoloStato = ruoloDi(el); } catch (e) { }
                if (ruoloStato === 'slider' || ruoloStato === 'progressbar' || ruoloStato === 'meter' || ruoloStato === 'scrollbar') {
                    // Lo slider di un lettore avanza da solo ogni secondo: si annuncia solo quello che segue
                    // un'azione dell'utente (e ai tasti sugli slider pensa WinGabriel, che legge il valore a cose ferme).
                    if (Date.now() - ultimoInputUtente > 1500) return;
                }
                var riga = descrivi(el);
                var firma = riga.i + '|' + (riga.s || '') + '|' + (riga.v || '');
                if (firma === ultimoStatoInviato) return;
                ultimoStatoInviato = firma;
                invia({ tipo: 'stato', riga: riga });
                return;
            }
            if (attributoCambiato !== 'aria-selected' && attributoCambiato !== 'aria-checked') return;
            if (attributo(el, attributoCambiato) !== 'true') return;
            if (attributo(attivo, 'aria-activedescendant')) return;
            if (!attivo.contains(el) && !(el.contains && el.contains(attivo))) {
                var contenitore = attivo.closest ? attivo.closest('[role="listbox"],[role="grid"],[role="tree"],[role="tablist"],[role="menu"],[role="radiogroup"],[role="treegrid"]') : null;
                if (!contenitore || !contenitore.contains(el)) return;
            }
            if (el === ultimoAttivo) return;
            ultimoAttivo = el;
            invia({ tipo: 'selezione', riga: descrivi(el) });
        } catch (e) { }
    }

    var flutterTentato = 0;
    function forseFlutter(nodo) {
        try {
            if (!nodo || nodo.nodeType !== 1) return;
            var nome = nodo.localName;
            if (nome !== 'flt-semantics-placeholder' && nome !== 'flutter-view' && nome !== 'flt-glass-pane') return;
            if (flutterTentato > 5) return;
            flutterTentato += 1;
            setTimeout(function () {
                if (A.abilitaFlutter()) invia({ tipo: 'flutter' });
            }, 400);
        } catch (e) { }
    }

    // Nello stesso gruppo di mutazioni molti nodi condividono il genitore (commenti, liste che si allungano):
    // la regione live si cerca una volta per genitore invece di risalire l'albero per ogni nodo.
    function regioneLiveInGruppo(nodo, memoria) {
        var chiave = nodo && nodo.nodeType === 1 ? nodo : (nodo ? nodo.parentElement : null);
        if (!chiave || !memoria) return regioneLive(nodo);
        if (memoria.has(chiave)) return memoria.get(chiave);
        var r = regioneLive(chiave);
        memoria.set(chiave, r);
        return r;
    }

    function suMutazioni(lista) {
        var memoriaLive = typeof Map === 'function' ? new Map() : null;
        for (var i = 0; i < lista.length; i++) {
            var m = lista[i];
            if (m.type === 'attributes' && m.attributeName === 'aria-activedescendant') {
                annunciaDiscendenteAttivo(m.target);
                continue;
            }
            if (m.type === 'attributes') {
                if (ATTRIBUTI_RILEVANTI[m.attributeName]) segnaSporco();
                if (m.attributeName === 'open' && m.target.localName === 'dialog' && m.target.open) annunciaDialogo(m.target);
                if (m.attributeName === 'aria-modal' || m.attributeName === 'hidden' || m.attributeName === 'aria-hidden') {
                    var t = m.target;
                    var rt = ruoloEsplicito(t);
                    if ((rt === 'dialog' || rt === 'alertdialog') && !nascostoPerNome(t)) annunciaDialogo(t);
                }
                if (m.attributeName === 'aria-expanded' || m.attributeName === 'aria-pressed' || m.attributeName === 'aria-checked' || m.attributeName === 'aria-selected' || m.attributeName === 'aria-valuenow' || m.attributeName === 'aria-valuetext') {
                    statoCambiato(m.target, m.attributeName);
                }
                continue;
            }
            var bersaglio = m.target;
            var genitore = bersaglio.nodeType === 1 ? bersaglio : bersaglio.parentElement;
            if (genitore && (genitore.localName === 'script' || genitore.localName === 'style')) continue;
            segnaSporco();
            var lr = regioneLiveInGruppo(bersaglio, memoriaLive);
            if (m.type === 'characterData') {
                if (lr) accodaLive(lr.atomico ? lr.el.textContent : bersaglio.nodeValue, lr.assertivo);
                continue;
            }
            for (var j = 0; j < m.addedNodes.length; j++) {
                var aggiunto = m.addedNodes[j];
                if (aggiunto.nodeType === 1) {
                    forseFlutter(aggiunto);
                    var ra = ruoloEsplicito(aggiunto);
                    if (ra === 'alert') {
                        accodaLive(aggiunto.textContent, true);
                        continue;
                    }
                    if ((ra === 'dialog' || ra === 'alertdialog') && !nascostoPerNome(aggiunto)) {
                        annunciaDialogo(aggiunto);
                        continue;
                    }
                    if (aggiunto.localName === 'script' || aggiunto.localName === 'style') continue;
                    if (lr && !nascostoPerNome(aggiunto)) accodaLive(lr.atomico ? lr.el.textContent : aggiunto.textContent, lr.assertivo);
                } else if (aggiunto.nodeType === 3 && lr) {
                    accodaLive(lr.atomico ? lr.el.textContent : aggiunto.nodeValue, lr.assertivo);
                }
            }
        }
    }

    var ultimoDialogo = null;
    function annunciaDialogo(d) {
        if (ultimoDialogo === d) return;
        ultimoDialogo = d;
        setTimeout(function () {
            if (!d.isConnected || nascostoPerNome(d)) return;
            var riga = descrivi(d);
            invia({ tipo: 'dialogo', riga: riga });
        }, 60);
    }

    function suFocus(ev) {
        var el = null;
        try { el = ev.composedPath ? ev.composedPath()[0] : ev.target; } catch (e) { el = ev.target; }
        if (!el || el.nodeType !== 1) return;
        if (el.localName === 'iframe' || el.localName === 'frame') return;
        if (el === el.ownerDocument.body || el === el.ownerDocument.documentElement) return;
        ultimoAttivo = null;
        ultimoStatoInviato = '';
        try {
            invia({ tipo: 'focus', riga: descrivi(el) });
            if (el.localName === 'input' && ((el.type || '').toLowerCase() === 'password' || campoUtente(el))) {
                invia({ tipo: 'campoLogin', origine: location.origin });
            }
        } catch (e) { }
    }

    function suCambio(ev) {
        var el = ev.target;
        if (!el || el.nodeType !== 1) return;
        var ruolo = ruoloDi(el);
        if (ruolo === 'checkbox' || ruolo === 'radio' || ruolo === 'switch' || el.localName === 'select') {
            segnaSporco();
        }
        ricordaUtente(el);
    }

    function suInvioModulo() {
        catturaCredenziali();
    }

    function suClic(ev) {
        try {
            var el = ev.target;
            if (!el || el.nodeType !== 1) return;
            var pulsante = el.closest ? el.closest('button,input[type="submit"],input[type="button"],input[type="image"],[role="button"],a') : null;
            if (!pulsante) return;
            if (!document.querySelector('input[type="password"]')) return;
            catturaCredenziali();
        } catch (e) { }
    }

    function suTasto(ev) {
        try {
            suInputUtente();
            if (ev.key !== 'Enter') return;
            var el = ev.target;
            if (!el || el.localName !== 'input') return;
            if ((el.type || '').toLowerCase() === 'password') catturaCredenziali();
            else ricordaUtente(el);
        } catch (e) { }
    }

    var erroriMedia = {};
    function suErroreMedia(ev) {
        try {
            var t = ev.target;
            if (!t || t.nodeType !== 1) return;
            var m = t.localName === 'source' ? t.parentElement : t;
            if (!m || (m.localName !== 'video' && m.localName !== 'audio')) return;
            var codice = m.error ? m.error.code : 0;
            if (codice !== 4 && t.localName !== 'source') return;
            var sorgente = m.currentSrc || m.src || t.src || '';
            if (erroriMedia[sorgente]) return;
            erroriMedia[sorgente] = 1;
            invia({ tipo: 'mediaErrore', src: sorgente, codice: codice, video: m.localName === 'video' ? 1 : 0 });
        } catch (e) { }
    }

    function collegaDocumento(doc) {
        if (!doc || documentiCollegati.has(doc)) return;
        try {
            if (doc !== document && doc.defaultView && doc.defaultView.__wgAgente) return;
        } catch (e) { }
        documentiCollegati.add(doc);
        try {
            doc.addEventListener('focusin', suFocus, true);
            doc.addEventListener('change', suCambio, true);
            doc.addEventListener('submit', suInvioModulo, true);
            doc.addEventListener('click', suClic, true);
            doc.addEventListener('keydown', suTasto, true);
            doc.addEventListener('pointerdown', suInputUtente, true);
            doc.addEventListener('wheel', suInputUtente, true);
            doc.addEventListener('error', suErroreMedia, true);
            doc.addEventListener('loadedmetadata', senzaImmagineNellImmagine, true);
            doc.addEventListener('play', senzaImmagineNellImmagine, true);
            try {
                var vista = doc.defaultView || window;
                vista.addEventListener('click', suClicColore, true);
            } catch (e) {
                doc.addEventListener('click', suClicColore, true);
            }
            var osservatore = new MutationObserver(suMutazioni);
            osservatore.observe(doc.documentElement || doc, {
                childList: true,
                subtree: true,
                characterData: true,
                attributes: true,
                attributeFilter: Object.keys(ATTRIBUTI_RILEVANTI).concat(['aria-activedescendant'])
            });
        } catch (e) { }
    }

    A.collegaDocumento = collegaDocumento;

    function osservaCookie() {
        try {
            var deposito = window.cookieStore;
            if (!deposito || typeof deposito.addEventListener !== 'function') return;
            deposito.addEventListener('change', function () {
                invia({ tipo: 'cookie' });
            });
        } catch (e) { }
    }
    osservaCookie();

    function pronto() {
        invia({ tipo: 'pronto', titolo: document.title || '', url: location.href });
        try {
            if (document.querySelector('flt-semantics-placeholder,flutter-view,flt-glass-pane')) {
                setTimeout(function () {
                    if (A.abilitaFlutter()) invia({ tipo: 'flutter' });
                }, 600);
            }
        } catch (e) { }
    }

    if (document.documentElement) {
        collegaDocumento(document);
    } else {
        var attesaRadice = new MutationObserver(function () {
            if (document.documentElement) {
                attesaRadice.disconnect();
                collegaDocumento(document);
            }
        });
        try { attesaRadice.observe(document, { childList: true }); } catch (e) { }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', pronto, { once: true });
    } else {
        pronto();
    }
})();