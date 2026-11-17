Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports Microsoft.Web.WebView2.Core
Imports WGBrowserManager
Imports wgcontrols

' La lettura semplificata dei siti fatti con WordPress. Se la pagina dichiara le API di WordPress (il collegamento api.w.org) e le API
' rispondono, il browser chiede se aprire il sito con la lettura semplificata: un menu con articoli, categorie, pagine e ricerca; ogni voce
' apre l'elenco (le categorie aprono l'elenco degli articoli di quella categoria) e da lì si legge il testo di un articolo o di una pagina.
Partial Public Class WGSchedaBrowser

#Region "WordPress"

    ''' <summary>
    ''' Prima di aprire un indirizzo: se il sito è WordPress con le API funzionanti la pagina non si carica. Con la lettura semplificata (scelta o
    ''' accettata) si apre il menu; altrimenti si apre la pagina come sempre. Vero se si è occupato lui dell'apertura (poi si va avanti da solo).
    ''' </summary>
    Friend Function ControllaWordPressPrimaDiAprire(destinazione As String) As Boolean
        Try
            If WGImpostazioniBrowser.SitiWordPress = WGImpostazioniBrowser.WordPressNormale Then Return False
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(destinazione, UriKind.Absolute, uri) OrElse (uri.Scheme <> "https" AndAlso uri.Scheme <> "http") Then Return False
            If uri.IsLoopback OrElse uri.HostNameType <> UriHostNameType.Dns Then Return False
            If Not PrimaVolta("wordpress|" & WGArchivioBrowser.HostDi(destinazione)) Then Return False
            ControllaWordPressPoiApri(destinazione)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaWordPressPrimaDiAprire: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Sub ControllaWordPressPoiApri(destinazione As String)
        Dim proseguire As Boolean = True
        Try
            Dim uri As New Uri(destinazione)
            Dim radice As String = uri.Scheme & "://" & uri.Authority & "/wp-json/"
            Dim funziona As Boolean = False
            Await WGProcessoInCorso.EseguiAsync("Controllo del sito in corso",
                Async Function() As Task
                    funziona = Await WGWordPress.ApiFunzionaAsync(radice)
                End Function)
            If _chiusa Then Return
            If funziona Then
                proseguire = False
                Dim host As String = WGArchivioBrowser.HostDi(destinazione)
                ' Se la scheda è vuota, chiudendo la lettura semplificata si chiude anche lei: la pagina non deve restare sotto.
                Dim eraVuota As Boolean = String.IsNullOrEmpty(_indirizzo) OrElse _indirizzo.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                If WGImpostazioniBrowser.SitiWordPress = WGImpostazioniBrowser.WordPressSemplificata Then
                    MenuWordPress(radice, host, eraVuota)
                Else
                    WGDomanda.Chiedi("Questo sito è fatto con WordPress. Vuoi aprirlo con la lettura semplificata?",
                                     Sub() MenuWordPress(radice, host, eraVuota),
                                     Sub() ApriIndirizzoDopoControllo(destinazione))
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaWordPressPoiApri: " & ex.Message)
            proseguire = True
        End Try
        If proseguire Then ApriIndirizzoDopoControllo(destinazione)
    End Sub

    Private Sub ApriIndirizzoDopoControllo(destinazione As String)
        Try
            If _chiusa Then Return
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then
                _indirizzoIniziale = destinazione
                Return
            End If
            PrimaDellaNavigazione(destinazione)
            caricamentoAnnunciato = True
            NavigaVerso(core, destinazione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ApriIndirizzoDopoControllo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>La voce del menu del browser: apre subito la lettura semplificata se il sito la permette.</summary>
    Friend Async Sub LetturaSemplificataWordPress()
        Try
            Dim host As String = WGArchivioBrowser.HostDi(_indirizzo)
            Dim radice As String = ""
            If host <> "" Then radice = Await RadiceApiWordPressAsync()
            If radice = "" Then
                MessaggioWordPress("Questo sito non è fatto con WordPress oppure non permette la lettura semplificata.")
                Return
            End If
            MenuWordPress(radice, host)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LetturaSemplificataWordPress: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>L'indirizzo base delle API di WordPress del sito della pagina aperta, se la pagina le dichiara e rispondono; altrimenti una stringa vuota.</summary>
    Private Async Function RadiceApiWordPressAsync() As Task(Of String)
        Try
            If _cdp Is Nothing Then Return ""
            Dim o As JObject = Await _cdp.ValutaOggettoAsync("(function(){var l=document.querySelector('link[rel=""https://api.w.org/""]');return {radice: l ? l.href : ''};})()", 5000)
            If o Is Nothing Then Return ""
            Dim radice As String = Convert.ToString(o("radice")).Trim()
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(radice, UriKind.Absolute, uri) OrElse (uri.Scheme <> "https" AndAlso uri.Scheme <> "http") Then Return ""
            ' Con i collegamenti "semplici" l'indirizzo è del tipo ?rest_route=/ ; si tiene comunque com'è.
            Dim prova As Tuple(Of JToken, Integer) = Await WGWordPress.ScaricaJsonAsync(WGWordPress.UrlApi(radice, "wp/v2/posts", "per_page=1&_fields=id"))
            If prova Is Nothing OrElse Not TypeOf prova.Item1 Is JArray Then Return ""
            Return radice
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RadiceApiWordPressAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub MessaggioWordPress(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MessaggioWordPress: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuWordPress(radice As String, host As String, Optional chiudiSchedaAllaFine As Boolean = False)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Articoli", Sub() ElencoWordPress(radice, "Articoli", "wp/v2/posts", "", TipoVoceWp.Articolo))
            menu.aggiungi("Categorie", Sub() ElencoWordPress(radice, "Categorie", "wp/v2/categories", "", TipoVoceWp.Categoria))
            menu.aggiungi("Pagine", Sub() ElencoWordPress(radice, "Pagine", "wp/v2/pages", "", TipoVoceWp.Pagina))
            menu.aggiungi("Ricerca", Sub() RicercaWordPress(radice))
            If chiudiSchedaAllaFine Then
                AddHandler menu.abbandonato, Sub(s, e) SuUI(Sub() _contenitore.ChiudiScheda(Me, True))
            End If
            menu.avvia(host)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MenuWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Enum TipoVoceWp
        Articolo
        Categoria
        Pagina
    End Enum

    Private Const VociPerPaginaWp As Integer = 100

    ' Gli ordini possibili: chiave, nome da dire e parametri delle API.
    Private Shared Function OrdiniWordPress(tipo As TipoVoceWp) As String()()
        If tipo = TipoVoceWp.Categoria Then
            Return New String()() {
                New String() {"nome-asc", "dalla A alla Z", "orderby=name&order=asc"},
                New String() {"nome-desc", "dalla Z alla A", "orderby=name&order=desc"},
                New String() {"numero-desc", "dalla categoria con più articoli", "orderby=count&order=desc"},
                New String() {"numero-asc", "dalla categoria con meno articoli", "orderby=count&order=asc"}}
        End If
        If tipo = TipoVoceWp.Pagina Then
            Return New String()() {
                New String() {"titolo-asc", "per titolo dalla A alla Z", "orderby=title&order=asc"},
                New String() {"titolo-desc", "per titolo dalla Z alla A", "orderby=title&order=desc"},
                New String() {"data-desc", "dalla più recente", "orderby=date&order=desc"},
                New String() {"data-asc", "dalla meno recente", "orderby=date&order=asc"}}
        End If
        Return New String()() {
            New String() {"data-desc", "dal più recente", "orderby=date&order=desc"},
            New String() {"data-asc", "dal più vecchio", "orderby=date&order=asc"},
            New String() {"titolo-asc", "per titolo dalla A alla Z", "orderby=title&order=asc"},
            New String() {"titolo-desc", "per titolo dalla Z alla A", "orderby=title&order=desc"}}
    End Function

    ''' <summary>
    ''' Un elenco (articoli, categorie, pagine o gli articoli di una categoria): tutte le voci, cento per pagina. Nelle opzioni ci sono Pagina successiva,
    ''' Pagina precedente e Vai alla pagina; e si sceglie l'ordine (per esempio dal più vecchio). Invio su una voce apre il testo (articolo o pagina) o, per una
    ''' categoria, l'elenco dei suoi articoli. L'elenco resta aperto: Esc dal testo torna all'elenco. Con daSostituire l'elenco nuovo prende il posto di quello.
    ''' </summary>
    Private Async Sub ElencoWordPress(radice As String, titolo As String, rotta As String, queryBase As String, tipo As TipoVoceWp,
                                      Optional ordine As String = "", Optional pagina As Integer = 1, Optional daSostituire As WGMenuLista = Nothing)
        Try
            Dim ordini As String()() = OrdiniWordPress(tipo)
            Dim scelto As String() = ordini(0)
            For Each o As String() In ordini
                If o(0) = ordine Then scelto = o
            Next
            Dim campi As String = If(tipo = TipoVoceWp.Categoria, "id,name,count", "id,title")
            Dim totalePagine As Integer = 1
            Dim totaleVoci As Integer = 0
            Dim voci As JArray = Nothing

            Await WGProcessoInCorso.EseguiAsync("Recupero in corso",
                Async Function() As Task
                    Dim r As Tuple(Of JToken, Integer) = Await WGWordPress.ScaricaJsonAsync(WGWordPress.UrlApi(radice, rotta, "per_page=" & VociPerPaginaWp & "&page=" & pagina & "&_fields=" & campi & "&" & scelto(2) & If(queryBase <> "", "&" & queryBase, "")))
                    If r IsNot Nothing AndAlso TypeOf r.Item1 Is JArray Then
                        voci = CType(r.Item1, JArray)
                        totalePagine = r.Item2
                    End If
                End Function)
            If _chiusa Then Return
            If voci Is Nothing Then
                MessaggioWordPress(If(pagina > 1, "Non ci sono altre voci.", "Non è stato possibile leggere il sito."))
                Return
            End If
            ' Se il sito non dice quante pagine ha ma questa è piena, ce n'è probabilmente un'altra.
            If voci.Count >= VociPerPaginaWp AndAlso totalePagine <= pagina Then totalePagine = pagina + 1
            If voci.Count = 0 Then
                MessaggioWordPress(If(tipo = TipoVoceWp.Categoria, "Non ci sono categorie.", If(tipo = TipoVoceWp.Pagina, "Non ci sono pagine.", "Non ci sono articoli.")))
                Return
            End If

            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(titolo & If(totalePagine > 1, ", pagina " & pagina & " di " & totalePagine, ""))
            Dim nomi As New Dictionary(Of String, String)()
            AggiungiVociWordPress(lista, nomi, voci, tipo)

            AddHandler lista.escPremuto, Sub(s, e) lista.abbandona()
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim numero As Integer = 0
                        If Not Integer.TryParse(id, numero) Then Return
                        Dim nome As String = ""
                        nomi.TryGetValue(id, nome)
                        If tipo = TipoVoceWp.Categoria Then
                            ElencoWordPress(radice, nome, "wp/v2/posts", "categories=" & numero, TipoVoceWp.Articolo)
                        Else
                            MostraContenutoWordPress(radice, If(tipo = TipoVoceWp.Pagina, "wp/v2/pages/", "wp/v2/posts/") & numero, nome)
                        End If
                    Catch ex As Exception
                        log.ScriviLog("WGSchedaBrowser.ElencoWordPress (voce): " & ex.Message)
                    End Try
                End Sub
            lista.aggiungiOpzione("Ordine: " & scelto(1),
                Sub(s, e)
                    Try
                        Dim menuOrdine As New WGMenu()
                        For Each o As String() In ordini
                            Dim chiave As String = o(0)
                            menuOrdine.aggiungi(o(1).Substring(0, 1).ToUpperInvariant() & o(1).Substring(1), Sub(s2, e2) ElencoWordPress(radice, titolo, rotta, queryBase, tipo, chiave, 1, lista))
                        Next
                        menuOrdine.avvia("Ordine", True)
                    Catch ex As Exception
                        log.ScriviLog("WGSchedaBrowser.ElencoWordPress (ordine): " & ex.Message)
                    End Try
                End Sub)
            ' Il cambio di pagina sta solo nelle opzioni (Alt).
            If pagina < totalePagine Then
                lista.aggiungiOpzione("Pagina successiva", Sub(s, e) ElencoWordPress(radice, titolo, rotta, queryBase, tipo, scelto(0), pagina + 1, lista))
            End If
            If pagina > 1 Then
                lista.aggiungiOpzione("Pagina precedente", Sub(s, e) ElencoWordPress(radice, titolo, rotta, queryBase, tipo, scelto(0), pagina - 1, lista))
            End If
            If totalePagine > 1 Then
                lista.aggiungiOpzione("Vai alla pagina",
                    Async Sub(s, e)
                        Try
                            Dim testo As String = Await ChiediTestoWordPressAsync("Numero della pagina, da 1 a " & totalePagine)
                            If String.IsNullOrWhiteSpace(testo) Then Return
                            Dim n As Integer
                            If Not Integer.TryParse(testo.Trim(), n) OrElse n < 1 OrElse n > totalePagine Then
                                MessaggioWordPress("La pagina deve essere un numero da 1 a " & totalePagine & ".")
                                Return
                            End If
                            ElencoWordPress(radice, titolo, rotta, queryBase, tipo, scelto(0), n, lista)
                        Catch ex As Exception
                            log.ScriviLog("WGSchedaBrowser.ElencoWordPress (vai alla pagina): " & ex.Message)
                        End Try
                    End Sub)
            End If
            If daSostituire IsNot Nothing Then
                Try
                    daSostituire.abbandona()
                Catch exVecchia As Exception
                    log.ScriviLog("WGSchedaBrowser.ElencoWordPress (elenco precedente): " & exVecchia.Message)
                End Try
            End If
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ElencoWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub AggiungiVociWordPress(lista As WGMenuLista, voci As Dictionary(Of String, String), elementi As JArray, tipo As TipoVoceWp)
        For Each el As JToken In elementi
            Dim id As String = Convert.ToString(el("id"))
            If String.IsNullOrEmpty(id) OrElse voci.ContainsKey(id) Then Continue For
            Dim nome As String
            If tipo = TipoVoceWp.Categoria Then
                nome = WGWordPress.TestoSemplice(Convert.ToString(el("name")))
                Dim quanti As Integer = 0
                If Integer.TryParse(Convert.ToString(el("count")), quanti) Then nome &= ", " & If(quanti = 0, "nessun articolo", If(quanti = 1, "un articolo", quanti & " articoli"))
            Else
                nome = WGWordPress.TestoSemplice(Convert.ToString(el("title")("rendered")))
            End If
            If nome = "" Then nome = "Senza titolo"
            voci(id) = If(tipo = TipoVoceWp.Categoria, WGWordPress.TestoSemplice(Convert.ToString(el("name"))), nome)
            lista.aggiungiElemento(nome, id)
        Next
    End Sub

    ''' <summary>Scarica un articolo o una pagina e ne mostra il testo.</summary>
    Private Async Sub MostraContenutoWordPress(radice As String, rotta As String, titoloPredefinito As String)
        Try
            Dim token As JToken = Nothing
            Await WGProcessoInCorso.EseguiAsync("Recupero in corso",
                Async Function() As Task
                    Dim r As Tuple(Of JToken, Integer) = Await WGWordPress.ScaricaJsonAsync(WGWordPress.UrlApi(radice, rotta, "_fields=id,title,content,link,date"))
                    If r IsNot Nothing Then token = r.Item1
                End Function)
            If _chiusa Then Return
            If token Is Nothing OrElse token("content") Is Nothing Then
                MessaggioWordPress("Non è stato possibile leggere il testo.")
                Return
            End If
            Dim titolo As String = WGWordPress.TestoSemplice(Convert.ToString(token("title")("rendered")))
            If titolo = "" Then titolo = titoloPredefinito
            Dim html As String = Convert.ToString(token("content")("rendered"))
            If String.IsNullOrWhiteSpace(html) Then
                MessaggioWordPress("Questa pagina non ha testo.")
                Return
            End If
            Dim idPost As String = Convert.ToString(token("id"))
            Dim campo As New WGCampo()
            campo.CaricaHtml(If(titolo = "", "Testo", titolo), html)
            campo.aggiungiopzione("Informazioni sulla pagina", Sub(s, e) InformazioniWordPress(radice, rotta, titolo))
            campo.aggiungiopzione("Commenti", Sub(s, e) CommentiWordPress(radice, idPost, titolo))
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraContenutoWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub InformazioniWordPress(radice As String, rotta As String, titolo As String)
        Try
            Dim testo As String = Nothing
            Await WGProcessoInCorso.EseguiAsync("Recupero in corso",
                Async Function() As Task
                    testo = Await WGWordPress.InformazioniAsync(radice, rotta)
                End Function)
            If _chiusa Then Return
            If testo Is Nothing Then
                MessaggioWordPress("Non è stato possibile leggere le informazioni.")
                Return
            End If
            Dim campo As New WGCampo()
            campo.init("Informazioni: " & titolo, testo, True, True)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InformazioniWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub CommentiWordPress(radice As String, idPost As String, titolo As String)
        Try
            Dim commenti As List(Of WGWordPress.CommentoWp) = Nothing
            Await WGProcessoInCorso.EseguiAsync("Recupero dei commenti in corso",
                Async Function() As Task
                    commenti = Await WGWordPress.CommentiAsync(radice, idPost)
                End Function)
            If _chiusa Then Return
            If commenti Is Nothing Then
                MessaggioWordPress("I commenti non sono disponibili.")
                Return
            End If
            If commenti.Count = 0 Then
                MessaggioWordPress("Non ci sono commenti.")
                Return
            End If
            WGWordPress.MostraCommenti(titolo, commenti)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CommentiWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Function ChiediTestoWordPressAsync(titolo As String) As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Try
            Dim campi As New Campi()
            campi.Aggiungi(titolo, "wp.testo", True, False, "")
            AddHandler campi.Fine, Sub()
                                       Try
                                           esito.TrySetResult(campi.RecuperaValore("wp.testo"))
                                       Catch exLog As Exception
                                           esito.TrySetResult(Nothing)
                                       End Try
                                   End Sub
            AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediTestoWordPressAsync: " & ex.Message)
            esito.TrySetResult(Nothing)
        End Try
        Return esito.Task
    End Function

    ''' <summary>La ricerca nel sito: articoli e pagine che contengono il testo; Invio su un risultato apre il testo.</summary>
    Private Async Sub RicercaWordPress(radice As String)
        Try
            Dim testo As String = Await ChiediTestoWordPressAsync("Ricerca")
            If String.IsNullOrWhiteSpace(testo) OrElse _chiusa Then Return
            testo = testo.Trim()
            Dim trovati As JArray = Nothing
            Dim esito As Boolean = False
            Await WGProcessoInCorso.EseguiAsync("Ricerca in corso",
                Async Function() As Task
                    Dim r As Tuple(Of JToken, Integer) = Await WGWordPress.ScaricaJsonAsync(WGWordPress.UrlApi(radice, "wp/v2/search", "per_page=40&type=post&search=" & Uri.EscapeDataString(testo) & "&_fields=id,title,subtype"))
                    If r IsNot Nothing AndAlso TypeOf r.Item1 Is JArray Then
                        trovati = CType(r.Item1, JArray)
                        esito = True
                    End If
                End Function)
            If _chiusa Then Return
            If Not esito Then
                MessaggioWordPress("Non è stato possibile cercare nel sito.")
                Return
            End If
            If trovati.Count = 0 Then
                MessaggioWordPress("Non è stato trovato niente.")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Ricerca")
            Dim voci As New Dictionary(Of String, String)()
            For Each el As JToken In trovati
                Dim id As String = Convert.ToString(el("id"))
                Dim sotto As String = Convert.ToString(el("subtype"))
                If String.IsNullOrEmpty(id) OrElse voci.ContainsKey(sotto & "|" & id) Then Continue For
                Dim nome As String = WGWordPress.TestoSemplice(Convert.ToString(el("title")))
                If nome = "" Then nome = "Senza titolo"
                voci(sotto & "|" & id) = nome
                lista.aggiungiElemento(nome & If(sotto = "page", ", pagina", ""), sotto & "|" & id)
            Next
            AddHandler lista.escPremuto, Sub(s, e) lista.abbandona()
            AddHandler lista.ElementoSelezionato,
                Sub(chiave As String)
                    Try
                        Dim parti As String() = chiave.Split("|"c)
                        If parti.Length <> 2 Then Return
                        Dim nome As String = ""
                        voci.TryGetValue(chiave, nome)
                        MostraContenutoWordPress(radice, If(parti(0) = "page", "wp/v2/pages/", "wp/v2/posts/") & parti(1), nome)
                    Catch ex As Exception
                        log.ScriviLog("WGSchedaBrowser.RicercaWordPress (voce): " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RicercaWordPress: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class
