Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports wgcontrols

' Gestione dell'interfaccia e dei comandi del browser integrato di WinGabriel.
Public Class WGGestoreBrowser
    Private ReadOnly _log As New Logger()
    Private ReadOnly _lettura As sintesivocale = sintesivocale.GetInstance()
    Private ReadOnly _apri As Action(Of String, Boolean)
    Private ReadOnly _controlloDownload As IWGControlloDownload
    Private Shared ReadOnly _cultura As New CultureInfo("it-IT")
    Private _dialogoImportazione As WGFileDialog
    Private _dialogoCartellaDownload As WGFileDialog
    Private _sceltaEsportazione As WGSceltaSalvataggio

    Public Shared ReadOnly CampiDatiPersonali As String()() = {
        New String() {"nome", "Nome"},
        New String() {"cognome", "Cognome"},
        New String() {"email", "Email"},
        New String() {"telefono", "Telefono"},
        New String() {"indirizzo", "Indirizzo, via e numero civico"},
        New String() {"citta", "Città"},
        New String() {"cap", "CAP"},
        New String() {"provincia", "Provincia"},
        New String() {"paese", "Paese"},
        New String() {"azienda", "Azienda"},
        New String() {"codicefiscale", "Codice fiscale"},
        New String() {"datanascita", "Data di nascita"}
    }

    Public Sub New(apri As Action(Of String, Boolean), Optional controlloDownload As IWGControlloDownload = Nothing)
        _apri = apri
        _controlloDownload = controlloDownload
    End Sub

    Private ReadOnly Property Archivio As WGArchivioBrowser
        Get
            Return WGArchivioBrowser.Istanza
        End Get
    End Property

    ' Gli esiti e le conferme (anche dopo aver cambiato un'impostazione) si danno solo con WGMessaggi: la voce da sola viene interrotta
    ' dal menu che si apre subito dopo e non si sente.
    Private Sub Annuncia(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.Annuncia: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.Show()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre un indirizzo nel browser di WinGabriel, verificando prima il runtime WebView2.</summary>
    Public Sub Apri(indirizzo As String, nuovaScheda As Boolean)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return
            If _apri Is Nothing Then
                Annuncia("Nessun browser da cui aprire l'indirizzo")
                Return
            End If
            WGRuntimeWebView2.AssicuraRuntime(Sub() _apri(indirizzo, nuovaScheda))
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.Apri: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiediTesto(titolo As String, iniziale As String, azione As Action(Of String))
        Try
            Dim campo As New WGCampoTesto()
            Dim confermato As Boolean = False
            campo.init(titolo, If(iniziale, ""))
            AddHandler campo.chiusura, Sub(s As Object, e As EventArgs)
                                           Try
                                               If confermato Then Return
                                               confermato = True
                                               Dim testo As String = campo.OttieniTesto()
                                               campo.chiudi()
                                               azione(If(testo, "").Trim())
                                           Catch exChiusura As Exception
                                               _log.ScriviLog("WGGestoreBrowser.ChiediTesto chiusura: " & exChiusura.ToString())
                                           End Try
                                       End Sub
            campo.avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ChiediTesto: " & ex.ToString())
        End Try
    End Sub

    Private Sub Conferma(domanda As String, azione As Action)
        Try
            Dim risposto As Boolean = False
            Dim d As New WGDomanda()
            AddHandler d.rispostadomanda, Sub(risposta As DialogResult)
                                              Try
                                                  If risposto Then Return
                                                  risposto = True
                                                  If risposta = DialogResult.Yes Then azione()
                                              Catch exRisposta As Exception
                                                  _log.ScriviLog("WGGestoreBrowser.Conferma risposta: " & exRisposta.ToString())
                                              End Try
                                          End Sub
            d.avvia(domanda)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.Conferma: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function IdCorrente(lista As WGMenuLista) As String
        Try
            If lista Is Nothing Then Return ""
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrEmpty(elementi(0)) Then Return elementi(0)
            Return If(lista.ValoreSelezionato, "")
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGGestoreBrowser.IdCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ValoreId(id As String, prefisso As String) As String
        If String.IsNullOrEmpty(id) OrElse Not id.StartsWith(prefisso & "|", StringComparison.Ordinal) Then Return ""
        Return id.Substring(prefisso.Length + 1)
    End Function

    Public Shared Function DescriviIndirizzo(indirizzo As String) As String
        Try
            Dim host As String = WGArchivioBrowser.HostDi(indirizzo)
            If host.StartsWith("www.") Then host = host.Substring(4)
            If host = "" Then Return If(indirizzo, "")
            Return host
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGGestoreBrowser.DescriviIndirizzo: " & ex.Message)
            Return If(indirizzo, "")
        End Try
    End Function

    Public Shared Function DescriviData(data As DateTime) As String
        Try
            If data = DateTime.MinValue Then Return ""
            Dim oggi As DateTime = DateTime.Today
            If data.Date = oggi Then Return "oggi alle " & data.ToString("HH:mm", _cultura)
            If data.Date = oggi.AddDays(-1) Then Return "ieri alle " & data.ToString("HH:mm", _cultura)
            If data.Year = oggi.Year Then Return data.ToString("d MMMM", _cultura) & " alle " & data.ToString("HH:mm", _cultura)
            Return data.ToString("d MMMM yyyy", _cultura)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGGestoreBrowser.DescriviData: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function DescriviDimensione(byteTotali As Long) As String
        Try
            If byteTotali < 1024 Then Return byteTotali.ToString(_cultura) & " byte"
            If byteTotali < 1024L * 1024L Then Return Math.Round(byteTotali / 1024.0R).ToString(_cultura) & " kilobyte"
            If byteTotali < 1024L * 1024L * 1024L Then Return Math.Round(byteTotali / (1024.0R * 1024.0R), 1).ToString(_cultura) & " megabyte"
            Return Math.Round(byteTotali / (1024.0R * 1024.0R * 1024.0R), 2).ToString(_cultura) & " gigabyte"
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGGestoreBrowser.DescriviDimensione: " & ex.Message)
            Return byteTotali & " byte"
        End Try
    End Function

    ' Finché l'utente non ha scelto il motore di ricerca, il browser glielo chiede all'apertura: nessun motore è
    ' già scelto in partenza e l'ordine dell'elenco è casuale, perché nessuno sia favorito dalla posizione.
    Private Sub ChiediMotoreRicerca(dopo As Action)
        Try
            Dim avviso As New WGMessaggi()
            avviso.Messaggio("Scegli il motore di ricerca che il browser userà. Potrai cambiarlo quando vuoi dalla configurazione del browser.")
            avviso.ShowDialog()
            Dim motori As New List(Of String)(WGImpostazioniBrowser.MotoriDisponibili)
            Dim caso As New Random()
            For i As Integer = motori.Count - 1 To 1 Step -1
                Dim j As Integer = caso.Next(i + 1)
                Dim scambio As String = motori(i)
                motori(i) = motori(j)
                motori(j) = scambio
            Next
            Dim menu As New WGMenu()
            For Each motore As String In motori
                Dim scelto As String = motore
                menu.aggiungi(WGImpostazioniBrowser.NomeMotore(motore), Sub(s, e)
                                                                            Try
                                                                                WGImpostazioniBrowser.MotoreRicerca = scelto
                                                                                Annuncia("Motore di ricerca: " & WGImpostazioniBrowser.NomeMotore(scelto))
                                                                                If dopo IsNot Nothing Then dopo()
                                                                            Catch exScelta As Exception
                                                                                _log.ScriviLog("WGGestoreBrowser.ChiediMotoreRicerca scelta: " & exScelta.ToString())
                                                                            End Try
                                                                        End Sub)
            Next
            menu.avvia("Motore di ricerca", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ChiediMotoreRicerca: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraMenuApertura()
        Try
            If Not WGImpostazioniBrowser.MotoreRicercaScelto Then
                ChiediMotoreRicerca(Sub() MostraMenuApertura())
                Return
            End If
            Dim menu As New WGMenu()
            If Not WGRuntimeWebView2.RuntimePresente() Then
                menu.aggiungi("Installa Microsoft WebView2 Runtime", Sub(s, e) WGRuntimeWebView2.InstallaConAvanzamento())
            End If
            menu.aggiungi("Scrivi indirizzo", Sub(s, e) ChiediIndirizzoDaAprire())
            menu.aggiungi("Preferiti", Sub(s, e) MostraPreferiti())
            menu.aggiungi("Cronologia", Sub(s, e) MostraCronologia())
            menu.aggiungi("Configurazione", Sub(s, e) MostraConfigurazione())
            menu.avvia("Browser", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraMenuApertura: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiediIndirizzoDaAprire()
        Try
            WGRuntimeWebView2.AssicuraRuntime(Sub() ChiediIndirizzo())
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ChiediIndirizzoDaAprire: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiediIndirizzo()
        Try
            ChiediTesto("Indirizzo del sito da visitare o testo da cercare, Invio senza testo per la pagina iniziale", "", Sub(valore As String)
                                                                                                                              Try
                                                                                                                                  If _apri Is Nothing Then
                                                                                                                                      Annuncia("Browser non disponibile")
                                                                                                                                      Return
                                                                                                                                  End If
                                                                                                                                  _apri(If(valore, "").Trim(), False)
                                                                                                                              Catch exApri As Exception
                                                                                                                                  _log.ScriviLog("WGGestoreBrowser.ChiediIndirizzoDaAprire apertura: " & exApri.ToString())
                                                                                                                              End Try
                                                                                                                          End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ChiediIndirizzo: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraPasswordEDati()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Password salvate", Sub(s, e) MostraPassword())
            menu.aggiungi("Dati per la compilazione dei moduli", Sub(s, e) MostraDatiPersonali())
            menu.aggiungi("Permessi dei siti", Sub(s, e) MostraPermessi())
            menu.aggiungi("Impostazioni dei singoli siti", Sub(s, e) MostraImpostazioniSiti())
            menu.avvia("Password, dati e permessi", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraPasswordEDati: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function NomeBrowserLink(valore As String) As String
        Select Case If(valore, "")
            Case WGImpostazioniComuni.BrowserWinGabriel : Return "browser di WinGabriel"
            Case "Chrome" : Return "Chrome"
            Case "Firefox" : Return "Firefox"
            Case "Edge" : Return "Edge"
        End Select
        Return "browser predefinito di Windows"
    End Function

    Public Sub MostraConfigurazione(Optional indice As Integer = 0)
        Try
            Dim impostazioniComuni As New WGImpostazioniComuni()
            Dim menu As New WGMenu()
            menu.aggiungi("Blocco della pubblicità: " & If(WGImpostazioniBrowser.BloccoPubblicita, "attivo", "disattivato"), Sub(s, e) ScegliBloccoPubblicita())
            Dim esclusi As Integer = WGBloccoPubblicita.SitiEsclusi().Count
            menu.aggiungi("Siti esclusi dal blocco della pubblicità: " & If(esclusi = 0, "nessuno", esclusi.ToString()), Sub(s, e) MostraSitiEsclusi())
            menu.aggiungi("Cronologia: " & If(WGImpostazioniBrowser.SalvaCronologia, "attiva", "disattivata"), Sub(s, e) ScegliCronologia())
            menu.aggiungi("Lingua delle pagine: " & WGImpostazioniBrowser.NomeLinguaPagine(), Sub(s, e) ScegliLinguaPagine())
            menu.aggiungi("Apri i link di WinGabriel, come quelli delle e-mail e dei campi, con: " & NomeBrowserLink(impostazioniComuni.BrowserPreferito), Sub(s, e) ScegliBrowserLink())
            menu.aggiungi("Pagine in un'altra lingua: " & WGImpostazioniBrowser.NomeModoTraduzione(), Sub(s, e) ScegliModoTraduzione())
            Dim nonTradotti As Integer = WGImpostazioniBrowser.SitiNonTradotti().Count
            menu.aggiungi("Siti da non tradurre: " & If(nonTradotti = 0, "nessuno", nonTradotti.ToString()), Sub(s, e) MostraSitiNonTradotti())
            menu.aggiungi("Descrizione automatica delle immagini senza descrizione: " & If(WGImpostazioniBrowser.DescrizioneAutomaticaImmagini, "attiva", "disattivata, consigliato per non consumare le richieste dell'intelligenza artificiale"), Sub(s, e) ScegliDescrizioneAutomaticaImmagini())
            menu.aggiungi("Impostazioni del traduttore: " & WGTraduttore.NomeLingua(WGTraduttore.LinguaDestinazione).ToLowerInvariant() & ", " & If(WGTraduttore.Motore = WGTraduttore.MotoreOffline, "offline", "Google"), Sub(s, e) WGTraduttore.MostraImpostazioni())
            menu.aggiungi("Altre impostazioni del browser", Sub(s, e) MostraImpostazioni())
            menu.posiziona(indice)
            menu.avvia("Configurazione del browser", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraConfigurazione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliBloccoPubblicita()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attivo", Sub(s, e)
                                         WGImpostazioniBrowser.BloccoPubblicita = True
                                         WGBloccoPubblicita.AggiornaStato()
                                         Annuncia("Blocco della pubblicità attivato")
                                         MostraConfigurazione(0)
                                     End Sub)
            menu.aggiungi("Disattivato", Sub(s, e)
                                              WGImpostazioniBrowser.BloccoPubblicita = False
                                              WGBloccoPubblicita.AggiornaStato()
                                              Annuncia("Blocco della pubblicità disattivato")
                                              MostraConfigurazione(0)
                                          End Sub)
            menu.posiziona(If(WGImpostazioniBrowser.BloccoPubblicita, 0, 1))
            menu.avvia("Blocco della pubblicità", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliBloccoPubblicita: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliCronologia()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attiva", Sub(s, e)
                                         WGImpostazioniBrowser.SalvaCronologia = True
                                         Annuncia("Cronologia attivata: le pagine visitate verranno ricordate")
                                         MostraConfigurazione(2)
                                     End Sub)
            menu.aggiungi("Disattivata", Sub(s, e)
                                              WGImpostazioniBrowser.SalvaCronologia = False
                                              Annuncia("Cronologia disattivata: le pagine visitate non verranno ricordate")
                                              MostraConfigurazione(2)
                                          End Sub)
            menu.posiziona(If(WGImpostazioniBrowser.SalvaCronologia, 0, 1))
            menu.avvia("Cronologia", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliCronologia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliDescrizioneAutomaticaImmagini()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attiva", Sub(s, e)
                                         WGImpostazioniBrowser.DescrizioneAutomaticaImmagini = True
                                         Annuncia("Descrizione automatica attivata: le immagini senza descrizione verranno descritte quando il cursore ci arriva, al massimo 20 per pagina. Ogni immagine usa una richiesta dell'intelligenza artificiale")
                                         MostraConfigurazione(7)
                                     End Sub)
            menu.aggiungi("Disattivata, consigliato per non consumare le richieste dell'intelligenza artificiale", Sub(s, e)
                                              WGImpostazioniBrowser.DescrizioneAutomaticaImmagini = False
                                              Annuncia("Descrizione automatica disattivata: le immagini si descrivono dal menu delle opzioni o con il tasto Applicazioni")
                                              MostraConfigurazione(7)
                                          End Sub)
            menu.posiziona(If(WGImpostazioniBrowser.DescrizioneAutomaticaImmagini, 0, 1))
            menu.avvia("Descrizione automatica delle immagini senza descrizione", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliDescrizioneAutomaticaImmagini: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliLinguaPagine()
        Try
            Dim menu As New WGMenu()
            Dim attuale As String = WGImpostazioniBrowser.LinguaPagine
            Dim posizione As Integer = 0
            For i As Integer = 0 To WGImpostazioniBrowser.LinguePagine.Length - 1
                Dim voce As String() = WGImpostazioniBrowser.LinguePagine(i)
                Dim codice As String = voce(0)
                Dim nome As String = voce(1)
                If codice = attuale Then posizione = i
                menu.aggiungi(nome, Sub(s, e)
                                        Try
                                            WGImpostazioniBrowser.LinguaPagine = codice
                                            Annuncia("Le pagine verranno chieste in " & nome.ToLowerInvariant() & ", quando il sito le offre")
                                            MostraConfigurazione(3)
                                        Catch exLingua As Exception
                                            _log.ScriviLog("WGGestoreBrowser.ScegliLinguaPagine scelta: " & exLingua.ToString())
                                        End Try
                                    End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Lingua delle pagine", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliLinguaPagine: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliModoTraduzione()
        Try
            Dim modi As String() = {WGImpostazioniBrowser.TraduzioneChiedi, WGImpostazioniBrowser.TraduzioneAutomatica, WGImpostazioniBrowser.TraduzioneAComando}
            Dim descrizioni As String() = {
                "Chiedere per ogni sito se tradurre le sue pagine",
                "Tradurre senza chiedere le pagine in un'altra lingua",
                "Non tradurre da solo: la traduzione si attiva con Control Shift U o dal menu delle opzioni"
            }
            Dim attuale As String = WGImpostazioniBrowser.ModoTraduzione
            Dim menu As New WGMenu()
            Dim posizione As Integer = 0
            For i As Integer = 0 To modi.Length - 1
                Dim modo As String = modi(i)
                If modo = attuale Then posizione = i
                menu.aggiungi(descrizioni(i), Sub(s, e)
                                                  Try
                                                      WGImpostazioniBrowser.ModoTraduzione = modo
                                                      Annuncia("Pagine in un'altra lingua: " & WGImpostazioniBrowser.NomeModoTraduzione(modo))
                                                      MostraConfigurazione(5)
                                                  Catch exModo As Exception
                                                      _log.ScriviLog("WGGestoreBrowser.ScegliModoTraduzione scelta: " & exModo.ToString())
                                                  End Try
                                              End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Pagine in un'altra lingua", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliModoTraduzione: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Eccezioni della traduzione: siti che non vengono mai tradotti.</summary>
    Public Sub MostraSitiNonTradotti(Optional indice As Integer = 0)
        Try
            Dim siti As List(Of String) = WGImpostazioniBrowser.SitiNonTradotti()
            If siti.Count = 0 Then
                Dim menuVuoto As New WGMenu()
                menuVuoto.aggiungi("Aggiungi un sito", Sub(s, e) AggiungiSitoNonTradotto())
                menuVuoto.avvia("Nessun sito escluso dalla traduzione", True)
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Siti da non tradurre: " & siti.Count)
            For Each sito As String In siti
                lista.aggiungiElemento(sito, "s|" & sito)
            Next
            Dim togli As Action = Sub()
                                      Try
                                          Dim sito As String = ValoreId(IdCorrente(lista), "s")
                                          If sito = "" Then
                                              Annuncia("Scegliere un sito")
                                              Return
                                          End If
                                          Dim posizione As Integer = Math.Max(0, lista.ottieniIndice() - 1)
                                          Conferma("Togliere " & sito & " dai siti da non tradurre? Le sue pagine potranno di nuovo essere tradotte.", Sub()
                                                                                                                                                  WGImpostazioniBrowser.ImpostaSitoNonTradotto(sito, False)
                                                                                                                                                  lista.abbandona()
                                                                                                                                                  Annuncia(sito & " tolto dai siti da non tradurre")
                                                                                                                                                  MostraSitiNonTradotti(posizione)
                                                                                                                                              End Sub)
                                      Catch exTogli As Exception
                                          _log.ScriviLog("WGGestoreBrowser.MostraSitiNonTradotti togli: " & exTogli.ToString())
                                      End Try
                                  End Sub
            lista.aggiungiOpzione("Aggiungi un sito", Sub(s, e)
                                                          lista.abbandona()
                                                          AggiungiSitoNonTradotto()
                                                      End Sub)
            lista.aggiungiOpzione("Togli il sito dall'elenco", Sub(s, e) togli())
            AddHandler lista.ElementoSelezionato, Sub(id As String) togli()
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraSitiNonTradotti esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(Math.Min(indice, siti.Count - 1))
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraSitiNonTradotti: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiSitoNonTradotto()
        Try
            ChiediTesto("Sito da non tradurre, per esempio wikipedia.org", "", Sub(valore As String)
                                                                                 Try
                                                                                     If valore = "" Then Return
                                                                                     Dim sito As String = WGBloccoPubblicita.NormalizzaSito(valore)
                                                                                     If sito = "" Then
                                                                                         MostraMessaggio("Indirizzo del sito non valido. Scrivere per esempio wikipedia.org")
                                                                                         Return
                                                                                     End If
                                                                                     WGImpostazioniBrowser.ImpostaSitoNonTradotto(sito, True)
                                                                                     Annuncia("Le pagine di " & sito & " non verranno tradotte")
                                                                                     MostraSitiNonTradotti()
                                                                                 Catch exSito As Exception
                                                                                     _log.ScriviLog("WGGestoreBrowser.AggiungiSitoNonTradotto conferma: " & exSito.ToString())
                                                                                 End Try
                                                                             End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.AggiungiSitoNonTradotto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliBrowserLink()
        Try
            Dim impostazioniComuni As New WGImpostazioniComuni()
            Dim scelte As String() = {WGImpostazioniComuni.BrowserWinGabriel, "Predefinito", "Chrome", "Firefox", "Edge"}
            Dim attuale As String = impostazioniComuni.BrowserPreferito
            Dim menu As New WGMenu()
            Dim posizione As Integer = 0
            For i As Integer = 0 To scelte.Length - 1
                Dim valore As String = scelte(i)
                If String.Equals(valore, attuale, StringComparison.OrdinalIgnoreCase) Then posizione = i
                menu.aggiungi(If(i = 0, "Browser di WinGabriel", If(i = 1, "Browser predefinito di Windows", valore)), Sub(s, e)
                                                                                                                         Try
                                                                                                                             Dim nuove As New WGImpostazioniComuni()
                                                                                                                             nuove.BrowserPreferito = valore
                                                                                                                             Annuncia("I link di WinGabriel verranno aperti con " & NomeBrowserLink(valore))
                                                                                                                             MostraConfigurazione(4)
                                                                                                                         Catch exScelta As Exception
                                                                                                                             _log.ScriviLog("WGGestoreBrowser.ScegliBrowserLink scelta: " & exScelta.ToString())
                                                                                                                         End Try
                                                                                                                     End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Apertura dei link di WinGabriel", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliBrowserLink: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraSitiEsclusi(Optional indice As Integer = 0)
        Try
            Dim siti As List(Of String) = WGBloccoPubblicita.SitiEsclusi()
            If siti.Count = 0 Then
                Dim menuVuoto As New WGMenu()
                menuVuoto.aggiungi("Aggiungi un sito", Sub(s, e) AggiungiSitoEscluso())
                menuVuoto.avvia("Nessun sito escluso dal blocco della pubblicità", True)
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Siti esclusi dal blocco della pubblicità: " & siti.Count)
            For Each sito As String In siti
                lista.aggiungiElemento(sito, "s|" & sito)
            Next
            Dim togli As Action = Sub()
                                      Try
                                          Dim sito As String = ValoreId(IdCorrente(lista), "s")
                                          If sito = "" Then
                                              Annuncia("Scegliere un sito")
                                              Return
                                          End If
                                          Dim posizione As Integer = Math.Max(0, lista.ottieniIndice() - 1)
                                          Conferma("Togliere " & sito & " dai siti esclusi? Su questo sito la pubblicità tornerà bloccata.", Sub()
                                                                                                                                             WGBloccoPubblicita.ImpostaSitoEscluso(sito, False)
                                                                                                                                             lista.abbandona()
                                                                                                                                             Annuncia(sito & " tolto dai siti esclusi")
                                                                                                                                             MostraSitiEsclusi(posizione)
                                                                                                                                         End Sub)
                                      Catch exTogli As Exception
                                          _log.ScriviLog("WGGestoreBrowser.MostraSitiEsclusi togli: " & exTogli.ToString())
                                      End Try
                                  End Sub
            lista.aggiungiOpzione("Aggiungi un sito", Sub(s, e)
                                                          lista.abbandona()
                                                          AggiungiSitoEscluso()
                                                      End Sub)
            lista.aggiungiOpzione("Togli il sito dall'elenco", Sub(s, e) togli())
            AddHandler lista.ElementoSelezionato, Sub(id As String) togli()
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraSitiEsclusi esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(Math.Min(indice, siti.Count - 1))
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraSitiEsclusi: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiSitoEscluso()
        Try
            ChiediTesto("Sito da escludere dal blocco della pubblicità, per esempio repubblica.it", "", Sub(valore As String)
                                                                                                        Try
                                                                                                            If valore = "" Then Return
                                                                                                            Dim sito As String = WGBloccoPubblicita.NormalizzaSito(valore)
                                                                                                            If sito = "" Then
                                                                                                                MostraMessaggio("Indirizzo del sito non valido. Scrivere per esempio repubblica.it")
                                                                                                                Return
                                                                                                            End If
                                                                                                            WGBloccoPubblicita.ImpostaSitoEscluso(sito, True)
                                                                                                            Annuncia("Pubblicità consentita su " & sito & ". Ricaricare le pagine già aperte per applicare")
                                                                                                            MostraSitiEsclusi()
                                                                                                        Catch exSito As Exception
                                                                                                            _log.ScriviLog("WGGestoreBrowser.AggiungiSitoEscluso conferma: " & exSito.ToString())
                                                                                                        End Try
                                                                                                    End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.AggiungiSitoEscluso: " & ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiPreferito(titolo As String, indirizzo As String)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) OrElse Not WGArchivioBrowser.IndirizzoRegistrabile(indirizzo) Then
                Annuncia("Questa pagina non si può aggiungere ai preferiti")
                Return
            End If
            Dim esistente As PreferitoBrowser = Archivio.PreferitoPerIndirizzo(indirizzo)
            If esistente IsNot Nothing Then
                Dim menu As New WGMenu()
                menu.aggiungi("Rinomina il preferito", Sub(s, e) ChiediTesto("Nome del preferito", esistente.Titolo, Sub(nome)
                                                                                                                   If nome <> "" AndAlso Archivio.RinominaPreferito(esistente.Chiave, nome) Then Annuncia("Preferito rinominato")
                                                                                                               End Sub))
                menu.aggiungi("Sposta il preferito in una cartella", Sub(s, e) ScegliCartellaPreferito(esistente.Chiave))
                menu.aggiungi("Togli dai preferiti", Sub(s, e)
                                                          If Archivio.EliminaPreferito(esistente.Chiave) Then Annuncia("Tolto dai preferiti")
                                                      End Sub)
                menu.avvia("La pagina è già tra i preferiti", True)
                Return
            End If
            Dim chiave As String = Archivio.AggiungiPreferito(titolo, indirizzo)
            If chiave = "" Then
                Annuncia("Impossibile aggiungere il preferito")
            Else
                Annuncia("Aggiunto ai preferiti: " & If(String.IsNullOrWhiteSpace(titolo), DescriviIndirizzo(indirizzo), titolo))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.AggiungiPreferito: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraPreferiti(Optional cartella As String = Nothing, Optional indice As Integer = 0)
        Try
            Dim cartelle As List(Of String) = If(cartella Is Nothing, Archivio.CartellePreferiti(), New List(Of String)())
            Dim preferiti As List(Of PreferitoBrowser) = Archivio.ElencoPreferiti(If(cartella, ""))
            If cartelle.Count = 0 AndAlso preferiti.Count = 0 Then
                Dim menuVuoto As New WGMenu()
                menuVuoto.aggiungi("Aggiungi un preferito", Sub(s, e) NuovoPreferito(cartella))
                menuVuoto.aggiungi("Importa i preferiti da un file di Chrome, Edge o Firefox", Sub(s, e) ImportaPreferiti())
                menuVuoto.avvia(If(cartella Is Nothing, "Nessun preferito", "Cartella " & cartella & " vuota"), True)
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(If(cartella Is Nothing, "Preferiti del browser", "Cartella " & cartella))
            For Each nome As String In cartelle
                lista.aggiungiElemento("Cartella " & nome & ", " & Archivio.ElencoPreferiti(nome).Count & " preferiti", "c|" & nome)
            Next
            For Each p As PreferitoBrowser In preferiti
                lista.aggiungiElemento(p.Titolo & ", " & DescriviIndirizzo(p.Indirizzo), "p|" & p.Chiave)
            Next
            lista.aggiungiOpzione("Apri in una nuova scheda", Sub(s, e)
                                                                     Dim p As PreferitoBrowser = Archivio.OttieniPreferito(ValoreId(IdCorrente(lista), "p"))
                                                                     If p Is Nothing Then Return
                                                                     lista.abbandona()
                                                                     Apri(p.Indirizzo, True)
                                                                 End Sub)
            lista.aggiungiOpzione("Rinomina", Sub(s, e)
                                                  Dim chiave As String = ValoreId(IdCorrente(lista), "p")
                                                  Dim p As PreferitoBrowser = Archivio.OttieniPreferito(chiave)
                                                  If p Is Nothing Then
                                                      Annuncia("Scegliere un preferito")
                                                      Return
                                                  End If
                                                  Dim posizione As Integer = lista.ottieniIndice()
                                                  ChiediTesto("Nuovo nome del preferito", p.Titolo, Sub(nome)
                                                                                                        If nome = "" Then Return
                                                                                                        Archivio.RinominaPreferito(chiave, nome)
                                                                                                        lista.abbandona()
                                                                                                        MostraPreferiti(cartella, posizione)
                                                                                                    End Sub)
                                              End Sub)
            lista.aggiungiOpzione("Modifica l'indirizzo", Sub(s, e)
                                                              Dim chiave As String = ValoreId(IdCorrente(lista), "p")
                                                              Dim p As PreferitoBrowser = Archivio.OttieniPreferito(chiave)
                                                              If p Is Nothing Then Return
                                                              Dim posizione As Integer = lista.ottieniIndice()
                                                              ChiediTesto("Indirizzo del preferito", p.Indirizzo, Sub(nuovo)
                                                                                                                      If nuovo = "" Then Return
                                                                                                                      Archivio.ModificaIndirizzoPreferito(chiave, nuovo)
                                                                                                                      lista.abbandona()
                                                                                                                      MostraPreferiti(cartella, posizione)
                                                                                                                  End Sub)
                                                          End Sub)
            lista.aggiungiOpzione("Sposta in una cartella", Sub(s, e)
                                                                Dim chiave As String = ValoreId(IdCorrente(lista), "p")
                                                                If chiave = "" Then Return
                                                                lista.abbandona()
                                                                ScegliCartellaPreferito(chiave)
                                                            End Sub)
            lista.aggiungiOpzione("Elimina", Sub(s, e)
                                                 Dim id As String = IdCorrente(lista)
                                                 Dim posizione As Integer = lista.ottieniIndice()
                                                 Dim chiave As String = ValoreId(id, "p")
                                                 Dim nomeCartella As String = ValoreId(id, "c")
                                                 If chiave <> "" Then
                                                     Archivio.EliminaPreferito(chiave)
                                                     lista.abbandona()
                                                     Annuncia("Preferito eliminato")
                                                     MostraPreferiti(cartella, Math.Max(0, posizione - 1))
                                                 ElseIf nomeCartella <> "" Then
                                                     Conferma("Eliminare la cartella " & nomeCartella & " con tutti i suoi preferiti?", Sub()
                                                                                                                                         For Each p As PreferitoBrowser In Archivio.ElencoPreferiti(nomeCartella)
                                                                                                                                             Archivio.EliminaPreferito(p.Chiave)
                                                                                                                                         Next
                                                                                                                                         lista.abbandona()
                                                                                                                                         Annuncia("Cartella eliminata")
                                                                                                                                         MostraPreferiti(cartella, 0)
                                                                                                                                     End Sub)
                                                 End If
                                             End Sub)
            lista.aggiungiOpzione("Aggiungi un preferito", Sub(s, e)
                                                               lista.abbandona()
                                                               NuovoPreferito(cartella)
                                                           End Sub)
            lista.aggiungiOpzione("Importa i preferiti da un file di Chrome, Edge o Firefox", Sub(s, e)
                                                                                                 lista.abbandona()
                                                                                                 ImportaPreferiti()
                                                                                             End Sub)
            lista.aggiungiOpzione("Esporta i preferiti in un file", Sub(s, e)
                                                                        lista.abbandona()
                                                                        EsportaPreferiti()
                                                                    End Sub)
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim nomeCartella As String = ValoreId(id, "c")
                                                          If nomeCartella <> "" Then
                                                              lista.abbandona()
                                                              MostraPreferiti(nomeCartella)
                                                              Return
                                                          End If
                                                          Dim p As PreferitoBrowser = Archivio.OttieniPreferito(ValoreId(id, "p"))
                                                          If p Is Nothing Then Return
                                                          lista.abbandona()
                                                          Apri(p.Indirizzo, False)
                                                      Catch exScelta As Exception
                                                          _log.ScriviLog("WGGestoreBrowser.MostraPreferiti scelta: " & exScelta.ToString())
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                                 If cartella IsNot Nothing Then MostraPreferiti()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraPreferiti esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(indice)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub NuovoPreferito(cartella As String)
        Try
            ChiediTesto("Indirizzo del nuovo preferito", "https://", Sub(indirizzo)
                                                                        If indirizzo = "" OrElse indirizzo = "https://" Then Return
                                                                        Dim completo As String = indirizzo
                                                                        If Not completo.Contains("://") Then completo = "https://" & completo
                                                                        ChiediTesto("Nome del nuovo preferito", DescriviIndirizzo(completo), Sub(nome)
                                                                                                                                                  If Archivio.AggiungiPreferito(nome, completo, If(cartella, "")) <> "" Then Annuncia("Preferito aggiunto")
                                                                                                                                              End Sub)
                                                                    End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.NuovoPreferito: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliCartellaPreferito(chiave As String)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Nessuna cartella", Sub(s, e)
                                                  Archivio.SpostaPreferito(chiave, "")
                                                  Annuncia("Preferito spostato fuori dalle cartelle")
                                              End Sub)
            For Each nome As String In Archivio.CartellePreferiti()
                Dim destinazione As String = nome
                menu.aggiungi("Cartella " & nome, Sub(s, e)
                                                      Archivio.SpostaPreferito(chiave, destinazione)
                                                      Annuncia("Spostato nella cartella " & destinazione)
                                                  End Sub)
            Next
            menu.aggiungi("Nuova cartella", Sub(s, e) ChiediTesto("Nome della nuova cartella", "", Sub(nome)
                                                                                                       If nome = "" Then Return
                                                                                                       Archivio.SpostaPreferito(chiave, nome)
                                                                                                       Annuncia("Spostato nella nuova cartella " & nome)
                                                                                                   End Sub))
            menu.avvia("Sposta il preferito", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliCartellaPreferito: " & ex.ToString())
        End Try
    End Sub

    Public Sub ImportaPreferiti()
        Try
            StaccaDialogoImportazione()
            _dialogoImportazione = New WGFileDialog()
            _dialogoImportazione.MultiselezioneConsentita = False
            _dialogoImportazione.AggiungiEstensione(".html")
            _dialogoImportazione.AggiungiEstensione(".htm")
            AddHandler _dialogoImportazione.selezioneterminata, AddressOf FilePreferitiScelto
            AddHandler _dialogoImportazione.annullata, AddressOf StaccaDialogoImportazione
            _dialogoImportazione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ImportaPreferiti: " & ex.ToString())
            StaccaDialogoImportazione()
        End Try
    End Sub

    Private Sub StaccaDialogoImportazione()
        Try
            If _dialogoImportazione IsNot Nothing Then
                RemoveHandler _dialogoImportazione.selezioneterminata, AddressOf FilePreferitiScelto
                RemoveHandler _dialogoImportazione.annullata, AddressOf StaccaDialogoImportazione
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.StaccaDialogoImportazione: " & ex.Message)
        End Try
        _dialogoImportazione = Nothing
    End Sub

    Private Sub FilePreferitiScelto(elementi As List(Of String))
        Try
            StaccaDialogoImportazione()
            Dim percorso As String = ""
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then percorso = If(elementi(0), "")
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                MostraMessaggio("Scegliere un file di preferiti, di tipo html.")
                Return
            End If
            Dim importati As Integer = ImportaDaFile(percorso)
            If importati < 0 Then
                MostraMessaggio("Il file non contiene preferiti leggibili.")
            Else
                MostraMessaggio("Preferiti importati: " & importati & ".")
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.FilePreferitiScelto: " & ex.ToString())
            MostraMessaggio("Impossibile importare i preferiti.")
        End Try
    End Sub

    Private Function ImportaDaFile(percorso As String) As Integer
        Try
            Dim html As String = File.ReadAllText(percorso, Encoding.UTF8)
            Dim modello As New Regex("<h3[^>]*>(?<cartella>.*?)</h3>|<a\s[^>]*href\s*=\s*""(?<indirizzo>[^""]*)""[^>]*>(?<titolo>.*?)</a>|<dl[^>]*>|</dl>", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            Dim pila As New List(Of String)()
            Dim cartellaInAttesa As String = Nothing
            Dim importati As Integer = 0
            Dim trovati As Integer = 0
            Dim esistenti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each p As PreferitoBrowser In Archivio.ElencoPreferiti()
                esistenti.Add(p.Indirizzo)
            Next
            Dim livelloDl As Integer = 0
            Dim livelliCartelle As New List(Of Integer)()
            For Each corrispondenza As Match In modello.Matches(html)
                Dim valore As String = corrispondenza.Value.ToLowerInvariant()
                If corrispondenza.Groups("cartella").Success Then
                    cartellaInAttesa = WebUtility.HtmlDecode(Regex.Replace(corrispondenza.Groups("cartella").Value, "<[^>]+>", "")).Trim()
                ElseIf corrispondenza.Groups("indirizzo").Success Then
                    trovati += 1
                    Dim indirizzo As String = WebUtility.HtmlDecode(corrispondenza.Groups("indirizzo").Value).Trim()
                    If Not WGArchivioBrowser.IndirizzoRegistrabile(indirizzo) OrElse esistenti.Contains(indirizzo) Then Continue For
                    Dim titolo As String = WebUtility.HtmlDecode(Regex.Replace(corrispondenza.Groups("titolo").Value, "<[^>]+>", "")).Trim()
                    Dim cartella As String = If(pila.Count > 0, pila(pila.Count - 1), "")
                    If Archivio.AggiungiPreferito(titolo, indirizzo, cartella) <> "" Then
                        importati += 1
                        esistenti.Add(indirizzo)
                    End If
                ElseIf valore.StartsWith("<dl") Then
                    livelloDl += 1
                    If cartellaInAttesa IsNot Nothing Then
                        Dim nome As String = cartellaInAttesa
                        If pila.Count > 0 AndAlso nome <> "" Then nome = pila(pila.Count - 1) & " - " & nome
                        pila.Add(nome)
                        livelliCartelle.Add(livelloDl)
                        cartellaInAttesa = Nothing
                    End If
                ElseIf valore.StartsWith("</dl") Then
                    If livelliCartelle.Count > 0 AndAlso livelliCartelle(livelliCartelle.Count - 1) = livelloDl Then
                        livelliCartelle.RemoveAt(livelliCartelle.Count - 1)
                        pila.RemoveAt(pila.Count - 1)
                    End If
                    livelloDl = Math.Max(0, livelloDl - 1)
                End If
            Next
            If trovati = 0 Then Return -1
            Return importati
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ImportaDaFile: " & ex.ToString())
            Return -1
        End Try
    End Function

    Public Sub EsportaPreferiti()
        Try
            StaccaSceltaEsportazione()
            _sceltaEsportazione = New WGSceltaSalvataggio("Nome del file in cui esportare i preferiti", "preferiti WinGabriel.html", ".html")
            AddHandler _sceltaEsportazione.Confermato, AddressOf ScriviPreferitiNelFile
            AddHandler _sceltaEsportazione.Annullato, AddressOf EsportazioneAnnullata
            _sceltaEsportazione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.EsportaPreferiti: " & ex.ToString())
            StaccaSceltaEsportazione()
        End Try
    End Sub

    Private Sub StaccaSceltaEsportazione()
        Try
            If _sceltaEsportazione IsNot Nothing Then
                RemoveHandler _sceltaEsportazione.Confermato, AddressOf ScriviPreferitiNelFile
                RemoveHandler _sceltaEsportazione.Annullato, AddressOf EsportazioneAnnullata
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.StaccaSceltaEsportazione: " & ex.Message)
        End Try
        _sceltaEsportazione = Nothing
    End Sub

    Private Sub EsportazioneAnnullata(sender As Object, e As EventArgs)
        Try
            StaccaSceltaEsportazione()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.EsportazioneAnnullata: " & ex.Message)
        End Try
    End Sub

    Private Sub ScriviPreferitiNelFile(percorso As String)
        Try
            StaccaSceltaEsportazione()
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim contenuto As New StringBuilder()
            contenuto.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>")
            contenuto.AppendLine("<META HTTP-EQUIV=""Content-Type"" CONTENT=""text/html; charset=UTF-8"">")
            contenuto.AppendLine("<TITLE>Bookmarks</TITLE>")
            contenuto.AppendLine("<H1>Bookmarks</H1>")
            contenuto.AppendLine("<DL><p>")
            For Each nome As String In Archivio.CartellePreferiti()
                contenuto.AppendLine("    <DT><H3>" & WebUtility.HtmlEncode(nome) & "</H3>")
                contenuto.AppendLine("    <DL><p>")
                For Each p As PreferitoBrowser In Archivio.ElencoPreferiti(nome)
                    contenuto.AppendLine("        <DT><A HREF=""" & WebUtility.HtmlEncode(p.Indirizzo) & """>" & WebUtility.HtmlEncode(p.Titolo) & "</A>")
                Next
                contenuto.AppendLine("    </DL><p>")
            Next
            For Each p As PreferitoBrowser In Archivio.ElencoPreferiti("")
                contenuto.AppendLine("    <DT><A HREF=""" & WebUtility.HtmlEncode(p.Indirizzo) & """>" & WebUtility.HtmlEncode(p.Titolo) & "</A>")
            Next
            contenuto.AppendLine("</DL><p>")
            File.WriteAllText(percorso, contenuto.ToString(), New UTF8Encoding(False))
            MostraMessaggio("Preferiti esportati nel file " & Path.GetFileName(percorso) & ".")
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScriviPreferitiNelFile: " & ex.ToString())
            MostraMessaggio("Impossibile esportare i preferiti.")
        End Try
    End Sub

    Public Sub MostraCronologia(Optional filtro As String = "", Optional indice As Integer = 0)
        Try
            Dim visite As List(Of VisitaBrowser) = Archivio.ElencoCronologia(filtro, 500)
            If visite.Count = 0 Then
                Annuncia(If(String.IsNullOrWhiteSpace(filtro), "La cronologia è vuota", "Nessuna pagina trovata per " & filtro))
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(If(String.IsNullOrWhiteSpace(filtro), "Cronologia del browser", "Cronologia: " & filtro))
            For Each v As VisitaBrowser In visite
                Dim titolo As String = If(String.IsNullOrWhiteSpace(v.Titolo), v.Indirizzo, v.Titolo)
                lista.aggiungiElemento(titolo & ", " & DescriviIndirizzo(v.Indirizzo) & ", " & DescriviData(v.UltimaVisita), "v|" & v.Chiave)
            Next
            lista.aggiungiOpzione("Cerca nella cronologia", Sub(s, e) ChiediTesto("Testo da cercare nella cronologia", filtro, Sub(testo)
                                                                                                                                  lista.abbandona()
                                                                                                                                  MostraCronologia(testo)
                                                                                                                              End Sub))
            lista.aggiungiOpzione("Apri in una nuova scheda", Sub(s, e)
                                                                  Dim v As VisitaBrowser = Archivio.OttieniVisita(ValoreId(IdCorrente(lista), "v"))
                                                                  If v Is Nothing Then Return
                                                                  lista.abbandona()
                                                                  Apri(v.Indirizzo, True)
                                                              End Sub)
            lista.aggiungiOpzione("Aggiungi ai preferiti", Sub(s, e)
                                                               Dim v As VisitaBrowser = Archivio.OttieniVisita(ValoreId(IdCorrente(lista), "v"))
                                                               If v Is Nothing Then Return
                                                               AggiungiPreferito(v.Titolo, v.Indirizzo)
                                                           End Sub)
            lista.aggiungiOpzione("Elimina questa pagina dalla cronologia", Sub(s, e)
                                                                                Dim chiave As String = ValoreId(IdCorrente(lista), "v")
                                                                                If chiave = "" Then Return
                                                                                Dim posizione As Integer = lista.ottieniIndice()
                                                                                Archivio.EliminaVisita(chiave)
                                                                                lista.abbandona()
                                                                                Annuncia("Pagina eliminata dalla cronologia")
                                                                                MostraCronologia(filtro, Math.Max(0, posizione - 1))
                                                                            End Sub)
            lista.aggiungiOpzione("Cancella la cronologia dell'ultima ora", Sub(s, e) Conferma("Cancellare la cronologia dell'ultima ora?", Sub()
                                                                                                                                              Dim n As Integer = Archivio.SvuotaCronologia(DateTime.Now.AddHours(-1))
                                                                                                                                              lista.abbandona()
                                                                                                                                              Annuncia("Pagine cancellate: " & n)
                                                                                                                                          End Sub))
            lista.aggiungiOpzione("Cancella la cronologia di oggi", Sub(s, e) Conferma("Cancellare la cronologia di oggi?", Sub()
                                                                                                                              Dim n As Integer = Archivio.SvuotaCronologia(DateTime.Today)
                                                                                                                              lista.abbandona()
                                                                                                                              Annuncia("Pagine cancellate: " & n)
                                                                                                                          End Sub))
            lista.aggiungiOpzione("Cancella tutta la cronologia", Sub(s, e) Conferma("Cancellare tutta la cronologia del browser?", Sub()
                                                                                                                                      Archivio.SvuotaCronologia()
                                                                                                                                      lista.abbandona()
                                                                                                                                      Annuncia("Cronologia cancellata")
                                                                                                                                  End Sub))
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim v As VisitaBrowser = Archivio.OttieniVisita(ValoreId(id, "v"))
                                                          If v Is Nothing Then Return
                                                          lista.abbandona()
                                                          Apri(v.Indirizzo, False)
                                                      Catch exScelta As Exception
                                                          _log.ScriviLog("WGGestoreBrowser.MostraCronologia scelta: " & exScelta.ToString())
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraCronologia esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(indice)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraCronologia: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviStatoDownload(stato As String) As String
        Select Case If(stato, "")
            Case "completato" : Return "completato"
            Case "annullato" : Return "annullato"
            Case "interrotto" : Return "non riuscito"
            Case "in corso" : Return "interrotto alla chiusura"
        End Select
        Return stato
    End Function

    Public Sub MostraDownload(Optional indice As Integer = 0)
        Try
            Dim attivi As New List(Of DownloadAttivoBrowser)()
            Try
                If _controlloDownload IsNot Nothing Then attivi = _controlloDownload.DownloadInCorso()
            Catch exAttivi As Exception
                _log.ScriviLog("WGGestoreBrowser.MostraDownload attivi: " & exAttivi.Message)
            End Try
            Dim registrati As List(Of DownloadRegistrato) = Archivio.ElencoDownload(200)
            Dim percorsiAttivi As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each a As DownloadAttivoBrowser In attivi
                If Not String.IsNullOrEmpty(a.Percorso) Then percorsiAttivi.Add(a.Percorso)
            Next
            If attivi.Count = 0 AndAlso registrati.Count = 0 Then
                Annuncia("Nessun download")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Download del browser")
            For Each a As DownloadAttivoBrowser In attivi
                Dim testo As String = a.Nome & ", " & If(a.InPausa, "in pausa", "in corso")
                If a.Percentuale >= 0 Then testo &= ", " & a.Percentuale & " percento"
                If a.Totali > 0 Then
                    testo &= ", " & DescriviDimensione(a.Ricevuti) & " su " & DescriviDimensione(a.Totali)
                ElseIf a.Ricevuti > 0 Then
                    testo &= ", " & DescriviDimensione(a.Ricevuti)
                End If
                lista.aggiungiElemento(testo, "a|" & a.Id.ToString(CultureInfo.InvariantCulture))
            Next
            For Each d As DownloadRegistrato In registrati
                If d.Stato = "in corso" AndAlso percorsiAttivi.Contains(d.Percorso) Then Continue For
                Dim testo As String = d.Nome & ", " & DescriviStatoDownload(d.Stato)
                If d.Dimensione > 0 Then testo &= ", " & DescriviDimensione(d.Dimensione)
                testo &= ", " & DescriviData(d.Data)
                lista.aggiungiElemento(testo, "s|" & d.Chiave)
            Next
            lista.aggiungiOpzione("Apri la cartella del file", Sub(s, e)
                                                                   Dim percorso As String = PercorsoDaId(IdCorrente(lista), attivi)
                                                                   ApriCartellaDelFile(percorso)
                                                               End Sub)
            lista.aggiungiOpzione("Pausa", Sub(s, e) AzioneDownloadAttivo(lista, 1))
            lista.aggiungiOpzione("Riprendi", Sub(s, e) AzioneDownloadAttivo(lista, 2))
            lista.aggiungiOpzione("Annulla il download", Sub(s, e) AzioneDownloadAttivo(lista, 3))
            lista.aggiungiOpzione("Elimina dall'elenco", Sub(s, e)
                                                             Dim chiave As String = ValoreId(IdCorrente(lista), "s")
                                                             If chiave = "" Then
                                                                 Annuncia("Si possono eliminare dall'elenco solo i download terminati")
                                                                 Return
                                                             End If
                                                             Dim posizione As Integer = lista.ottieniIndice()
                                                             Archivio.EliminaDownload(chiave)
                                                             lista.abbandona()
                                                             Annuncia("Eliminato dall'elenco")
                                                             MostraDownload(Math.Max(0, posizione - 1))
                                                         End Sub)
            lista.aggiungiOpzione("Elimina il file dal disco", Sub(s, e)
                                                                  Dim chiave As String = ValoreId(IdCorrente(lista), "s")
                                                                  Dim d As DownloadRegistrato = Archivio.OttieniDownload(chiave)
                                                                  If d Is Nothing OrElse String.IsNullOrWhiteSpace(d.Percorso) OrElse Not File.Exists(d.Percorso) Then
                                                                      Annuncia("File non trovato")
                                                                      Return
                                                                  End If
                                                                  Conferma("Eliminare dal disco il file " & Path.GetFileName(d.Percorso) & "?", Sub()
                                                                                                                                                  Try
                                                                                                                                                      File.Delete(d.Percorso)
                                                                                                                                                      Archivio.EliminaDownload(chiave)
                                                                                                                                                      lista.abbandona()
                                                                                                                                                      Annuncia("File eliminato")
                                                                                                                                                  Catch exElimina As Exception
                                                                                                                                                      _log.ScriviLog("WGGestoreBrowser.MostraDownload elimina file: " & exElimina.Message)
                                                                                                                                                      Annuncia("Impossibile eliminare il file")
                                                                                                                                                  End Try
                                                                                                                                              End Sub)
                                                              End Sub)
            lista.aggiungiOpzione("Svuota l'elenco dei download", Sub(s, e) Conferma("Svuotare l'elenco dei download? I file restano sul disco.", Sub()
                                                                                                                                                  Archivio.SvuotaDownload()
                                                                                                                                                  lista.abbandona()
                                                                                                                                                  Annuncia("Elenco svuotato")
                                                                                                                                              End Sub))
            lista.aggiungiOpzione("Cambia la cartella dei download", Sub(s, e)
                                                                         lista.abbandona()
                                                                         ScegliCartellaDownload()
                                                                     End Sub)
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim idAttivo As String = ValoreId(id, "a")
                                                          If idAttivo <> "" Then
                                                              Annuncia("Download ancora in corso. Alt per metterlo in pausa o annullarlo")
                                                              Return
                                                          End If
                                                          Dim d As DownloadRegistrato = Archivio.OttieniDownload(ValoreId(id, "s"))
                                                          If d Is Nothing Then Return
                                                          If String.IsNullOrWhiteSpace(d.Percorso) OrElse Not File.Exists(d.Percorso) Then
                                                              Annuncia("Il file non esiste più")
                                                              Return
                                                          End If
                                                          lista.abbandona()
                                                          ApriFileConWinGabriel(d.Percorso)
                                                      Catch exScelta As Exception
                                                          _log.ScriviLog("WGGestoreBrowser.MostraDownload scelta: " & exScelta.ToString())
                                                          Annuncia("Impossibile aprire il file")
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraDownload esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(indice)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraDownload: " & ex.ToString())
        End Try
    End Sub

    Private Function PercorsoDaId(id As String, attivi As List(Of DownloadAttivoBrowser)) As String
        Try
            Dim idAttivo As String = ValoreId(id, "a")
            If idAttivo <> "" Then
                Dim numero As Integer
                If Integer.TryParse(idAttivo, numero) Then
                    Dim a As DownloadAttivoBrowser = attivi.FirstOrDefault(Function(x) x.Id = numero)
                    If a IsNot Nothing Then Return a.Percorso
                End If
                Return ""
            End If
            Dim d As DownloadRegistrato = Archivio.OttieniDownload(ValoreId(id, "s"))
            If d Is Nothing Then Return ""
            Return d.Percorso
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.PercorsoDaId: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ApriFileConWinGabriel(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                Annuncia("Il file non esiste più")
                Return
            End If
            Dim gestioneFile As New WGFileManager.MenuFile()
            gestioneFile.Apri(percorso)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ApriFileConWinGabriel: " & ex.ToString())
            Annuncia("Impossibile aprire il file")
        End Try
    End Sub

    Private Sub ApriCartellaDelFile(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then
                Annuncia("Percorso del file non disponibile")
                Return
            End If
            Dim cartella As String = Path.GetDirectoryName(percorso)
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then
                Annuncia("Cartella non trovata")
                Return
            End If
            Dim gestioneFile As New WGFileManager.MenuFile()
            gestioneFile.Avvia(cartella)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ApriCartellaDelFile: " & ex.ToString())
            Annuncia("Impossibile aprire la cartella")
        End Try
    End Sub

    Private Sub AzioneDownloadAttivo(lista As WGMenuLista, azione As Integer)
        Try
            Dim idAttivo As String = ValoreId(IdCorrente(lista), "a")
            Dim numero As Integer
            If idAttivo = "" OrElse Not Integer.TryParse(idAttivo, numero) OrElse _controlloDownload Is Nothing Then
                Annuncia("Scegliere un download in corso")
                Return
            End If
            Select Case azione
                Case 1
                    _controlloDownload.PausaDownload(numero)
                    Annuncia("Download in pausa")
                Case 2
                    _controlloDownload.RiprendiDownload(numero)
                    Annuncia("Download ripreso")
                Case 3
                    _controlloDownload.AnnullaDownload(numero)
                    Annuncia("Download annullato")
            End Select
            lista.abbandona()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.AzioneDownloadAttivo: " & ex.Message)
        End Try
    End Sub

    Public Sub ScegliCartellaDownload()
        Try
            StaccaDialogoCartellaDownload()
            _dialogoCartellaDownload = New WGFileDialog()
            _dialogoCartellaDownload.MultiselezioneConsentita = False
            AddHandler _dialogoCartellaDownload.singolaselezione, AddressOf CartellaDownloadScelta
            AddHandler _dialogoCartellaDownload.selezioneterminata, AddressOf VociCartellaDownloadScelte
            AddHandler _dialogoCartellaDownload.annullata, AddressOf StaccaDialogoCartellaDownload
            _dialogoCartellaDownload.Avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliCartellaDownload: " & ex.ToString())
            StaccaDialogoCartellaDownload()
        End Try
    End Sub

    Private Sub StaccaDialogoCartellaDownload()
        Try
            If _dialogoCartellaDownload IsNot Nothing Then
                RemoveHandler _dialogoCartellaDownload.singolaselezione, AddressOf CartellaDownloadScelta
                RemoveHandler _dialogoCartellaDownload.selezioneterminata, AddressOf VociCartellaDownloadScelte
                RemoveHandler _dialogoCartellaDownload.annullata, AddressOf StaccaDialogoCartellaDownload
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.StaccaDialogoCartellaDownload: " & ex.Message)
        End Try
        _dialogoCartellaDownload = Nothing
    End Sub

    Private Sub VociCartellaDownloadScelte(elementi As List(Of String))
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                StaccaDialogoCartellaDownload()
                Return
            End If
            CartellaDownloadScelta(elementi(0))
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.VociCartellaDownloadScelte: " & ex.Message)
        End Try
    End Sub

    Private Sub CartellaDownloadScelta(elemento As String)
        Try
            StaccaDialogoCartellaDownload()
            Dim cartella As String = If(elemento, "").Trim()
            If cartella <> "" AndAlso Not Directory.Exists(cartella) AndAlso File.Exists(cartella) Then cartella = Path.GetDirectoryName(cartella)
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then
                MostraMessaggio("La cartella scelta non è disponibile.")
                Return
            End If
            WGImpostazioniBrowser.CartellaDownload = cartella
            Annuncia("I download andranno nella cartella " & WGSceltaSalvataggio.NomeCartella(cartella))
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.CartellaDownloadScelta: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraPassword()
        Try
            Dim credenziali As List(Of CredenzialeBrowser) = Archivio.ElencoCredenziali()
            If credenziali.Count = 0 Then
                Annuncia("Nessuna password salvata")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Password salvate")
            For Each c As CredenzialeBrowser In credenziali
                lista.aggiungiElemento(DescriviIndirizzo(c.Origine) & ", " & If(String.IsNullOrWhiteSpace(c.Utente), "senza nome utente", c.Utente), "c|" & c.Chiave)
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim chiave As String = ValoreId(id, "c")
                                                          If chiave = "" Then Return
                                                          MostraAzioniCredenziale(chiave, lista)
                                                      Catch exScelta As Exception
                                                          _log.ScriviLog("WGGestoreBrowser.MostraPassword scelta: " & exScelta.ToString())
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraPassword esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraPassword: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraAzioniCredenziale(chiave As String, lista As WGMenuLista)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Leggi la password", Sub(s, e)
                                                   Dim c As CredenzialeBrowser = Archivio.OttieniCredenziale(chiave)
                                                   If c IsNot Nothing Then Annuncia(c.Password)
                                               End Sub)
            menu.aggiungi("Copia la password negli appunti", Sub(s, e)
                                                                 Dim c As CredenzialeBrowser = Archivio.OttieniCredenziale(chiave)
                                                                 If c Is Nothing OrElse c.Password = "" Then Return
                                                                 Clipboard.SetText(c.Password)
                                                                 Annuncia("Password copiata negli appunti")
                                                             End Sub)
            menu.aggiungi("Copia il nome utente negli appunti", Sub(s, e)
                                                                    Dim c As CredenzialeBrowser = Archivio.OttieniCredenziale(chiave)
                                                                    If c Is Nothing OrElse c.Utente = "" Then Return
                                                                    Clipboard.SetText(c.Utente)
                                                                    Annuncia("Nome utente copiato negli appunti")
                                                                End Sub)
            menu.aggiungi("Modifica la password", Sub(s, e) ChiediTesto("Nuova password", "", Sub(nuova)
                                                                                                  If nuova = "" Then Return
                                                                                                  If Archivio.ModificaPassword(chiave, nuova) Then Annuncia("Password modificata")
                                                                                              End Sub))
            menu.aggiungi("Elimina", Sub(s, e) Conferma("Eliminare la password salvata?", Sub()
                                                                                              Archivio.EliminaCredenziale(chiave)
                                                                                              If lista IsNot Nothing Then lista.abbandona()
                                                                                              Annuncia("Password eliminata")
                                                                                          End Sub))
            menu.avvia("Password salvata", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraAzioniCredenziale: " & ex.ToString())
        End Try
    End Sub

    Public Sub MostraDatiPersonali(Optional indice As Integer = 0)
        Try
            Dim dati As Dictionary(Of String, String) = Archivio.DatiPersonali()
            Dim menu As New WGMenu()
            For i As Integer = 0 To CampiDatiPersonali.Length - 1
                Dim chiave As String = CampiDatiPersonali(i)(0)
                Dim etichetta As String = CampiDatiPersonali(i)(1)
                Dim posizione As Integer = i
                Dim valore As String = ""
                dati.TryGetValue(chiave, valore)
                menu.aggiungi(etichetta & ": " & If(String.IsNullOrWhiteSpace(valore), "vuoto", valore), Sub(s, e) ChiediTesto(etichetta, valore, Sub(nuovo)
                                                                                                                                                   Archivio.ImpostaDatoPersonale(chiave, nuovo)
                                                                                                                                                   MostraDatiPersonali(posizione)
                                                                                                                                               End Sub))
            Next
            menu.posiziona(indice)
            menu.avvia("Dati per la compilazione dei moduli", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraDatiPersonali: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function NomePermesso(tipo As String) As String
        Select Case If(tipo, "")
            Case "microfono" : Return "microfono"
            Case "fotocamera" : Return "fotocamera"
            Case "posizione" : Return "posizione"
            Case "notifiche" : Return "notifiche"
            Case "appunti" : Return "lettura degli appunti"
            Case "download" : Return "download multipli"
            Case "schermo" : Return "condivisione dello schermo"
            Case "midi" : Return "dispositivi MIDI"
            Case "sensori" : Return "sensori"
            Case "archiviazione" : Return "spazio di archiviazione"
            Case "finestre" : Return "gestione delle finestre"
        End Select
        Return If(tipo, "")
    End Function

    Public Sub MostraPermessi()
        Try
            Dim permessi As List(Of PermessoSitoBrowser) = Archivio.ElencoPermessi()
            If permessi.Count = 0 Then
                Annuncia("Nessun permesso memorizzato. I siti chiederanno ogni volta")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Permessi dei siti")
            For Each p As PermessoSitoBrowser In permessi
                lista.aggiungiElemento(DescriviIndirizzo(p.Origine) & ", " & NomePermesso(p.Tipo) & ", " & If(p.Valore = "consenti", "consentito", "bloccato"), "p|" & p.Origine & "|" & p.Tipo)
            Next
            Dim cambia As Action(Of String) = Sub(valore As String)
                                                  Dim id As String = ValoreId(IdCorrente(lista), "p")
                                                  Dim separatore As Integer = id.LastIndexOf("|"c)
                                                  If separatore <= 0 Then Return
                                                  Archivio.ImpostaPermessoSito(id.Substring(0, separatore), id.Substring(separatore + 1), valore)
                                                  lista.abbandona()
                                                  Annuncia(If(valore = "", "Il sito chiederà di nuovo", If(valore = "consenti", "Permesso consentito", "Permesso bloccato")))
                                                  MostraPermessi()
                                              End Sub
            lista.aggiungiOpzione("Consenti", Sub(s, e) cambia("consenti"))
            lista.aggiungiOpzione("Blocca", Sub(s, e) cambia("blocca"))
            lista.aggiungiOpzione("Chiedi ogni volta", Sub(s, e) cambia(""))
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Annuncia("Alt per consentire, bloccare o far chiedere di nuovo")
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraPermessi esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraPermessi: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviImpostazioneSito(chiave As String, valore As String) As String
        Select Case chiave
            Case "identita" : Return "si presenta come " & WGImpostazioniBrowser.NomeIdentita(valore)
            Case "pubblicita" : Return "pubblicità consentita"
            Case "zoom" : Return "zoom " & valore & " percento"
        End Select
        Return chiave & " " & valore
    End Function

    Public Sub MostraImpostazioniSiti()
        Try
            Dim voci As List(Of Tuple(Of String, String, String)) = Archivio.ImpostazioniDeiSiti()
            Dim siti As HashSet(Of String) = WGImpostazioniBrowser.SitiInControlloCompleto()
            If voci.Count = 0 AndAlso siti.Count = 0 Then
                Annuncia("Nessun sito con impostazioni particolari")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Impostazioni dei singoli siti")
            For Each host As String In siti
                lista.aggiungiElemento(host & ", controllo completo alla pagina", "x|" & host)
            Next
            For Each voce As Tuple(Of String, String, String) In voci
                lista.aggiungiElemento(voce.Item1 & ", " & DescriviImpostazioneSito(voce.Item2, voce.Item3), "i|" & voce.Item1 & "|" & voce.Item2)
            Next
            Dim elimina As Action = Sub()
                                        Dim id As String = IdCorrente(lista)
                                        Dim host As String = ValoreId(id, "x")
                                        If host <> "" Then
                                            WGImpostazioniBrowser.ImpostaControlloCompleto(host, False)
                                        Else
                                            Dim valore As String = ValoreId(id, "i")
                                            Dim separatore As Integer = valore.LastIndexOf("|"c)
                                            If separatore <= 0 Then Return
                                            Archivio.ImpostaValoreSito(valore.Substring(0, separatore), valore.Substring(separatore + 1), "")
                                        End If
                                        lista.abbandona()
                                        Annuncia("Impostazione eliminata")
                                        WGBloccoPubblicita.AggiornaStato()
                                        MostraImpostazioniSiti()
                                    End Sub
            lista.aggiungiOpzione("Elimina questa impostazione", Sub(s, e) elimina())
            AddHandler lista.ElementoSelezionato, Sub(id As String) Annuncia("Alt per eliminare l'impostazione")
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 _log.ScriviLog("WGGestoreBrowser.MostraImpostazioniSiti esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraImpostazioniSiti: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function SiNo(valore As Boolean) As String
        Return If(valore, "sì", "no")
    End Function

    Public Sub MostraImpostazioni(Optional indice As Integer = 0)
        Try
            Dim menu As New WGMenu()
            Dim contatore As Integer = 0
            Dim aggiungi As Action(Of String, Action(Of Integer)) = Sub(testo As String, azione As Action(Of Integer))
                                                                        Dim posizione As Integer = contatore
                                                                        menu.aggiungi(testo, Sub(s, e)
                                                                                                 Try
                                                                                                     azione(posizione)
                                                                                                 Catch exAzione As Exception
                                                                                                     _log.ScriviLog("WGGestoreBrowser.MostraImpostazioni azione: " & exAzione.ToString())
                                                                                                 End Try
                                                                                             End Sub)
                                                                        contatore += 1
                                                                    End Sub
            Dim interruttore As Action(Of String, Func(Of Boolean), Action(Of Boolean)) = Sub(testo As String, leggi As Func(Of Boolean), scrivi As Action(Of Boolean))
                                                                                              aggiungi(testo & ": " & SiNo(leggi()), Sub(posizione As Integer)
                                                                                                                                        scrivi(Not leggi())
                                                                                                                                        MostraImpostazioni(posizione)
                                                                                                                                    End Sub)
                                                                                          End Sub
            aggiungi("Pagina iniziale: " & WGImpostazioniBrowser.PaginaIniziale, Sub(posizione As Integer)
                                                                                     ChiediTesto("Indirizzo della pagina iniziale", WGImpostazioniBrowser.PaginaIniziale, Sub(valore As String)
                                                                                                                                                                         If valore <> "" Then WGImpostazioniBrowser.PaginaIniziale = If(valore.Contains("://"), valore, "https://" & valore)
                                                                                                                                                                         MostraImpostazioni(posizione)
                                                                                                                                                                     End Sub)
                                                                                 End Sub)
            aggiungi("Motore di ricerca: " & WGImpostazioniBrowser.NomeMotore(), Sub(posizione As Integer) ScegliMotoreRicerca(posizione))
            aggiungi("Cartella dei download: " & WGImpostazioniBrowser.CartellaDownload, Sub(posizione As Integer) ScegliCartellaDownload())
            interruttore("Chiedi dove salvare ogni download", Function() WGImpostazioniBrowser.ChiediDoveSalvare, Sub(v As Boolean) WGImpostazioniBrowser.ChiediDoveSalvare = v)
            interruttore("Annuncia l'avanzamento dei download", Function() WGImpostazioniBrowser.AnnunciaAvanzamentoDownload, Sub(v As Boolean) WGImpostazioniBrowser.AnnunciaAvanzamentoDownload = v)
            aggiungi("Aggiorna l'elenco dei siti pubblicitari, " & WGBloccoPubblicita.NumeroDomini & " siti", Sub(posizione As Integer) AggiornaElencoPubblicita())
            interruttore("Proponi di salvare le password", Function() WGImpostazioniBrowser.ProponiSalvataggioPassword, Sub(v As Boolean) WGImpostazioniBrowser.ProponiSalvataggioPassword = v)
            interruttore("Annuncia i nuovi contenuti in controllo completo", Function() WGImpostazioniBrowser.AnnunciaNuoviContenuti, Sub(v As Boolean) WGImpostazioniBrowser.AnnunciaNuoviContenuti = v)
            interruttore("Annuncia i nuovi contenuti anche in navigazione", Function() WGImpostazioniBrowser.AnnunciaNuoviContenutiInNavigazione, Sub(v As Boolean) WGImpostazioniBrowser.AnnunciaNuoviContenutiInNavigazione = v)
            interruttore("Annuncia gli elementi selezionati in controllo completo", Function() WGImpostazioniBrowser.AnnunciaSelezione, Sub(v As Boolean) WGImpostazioniBrowser.AnnunciaSelezione = v)
            interruttore("Leggi le immagini senza descrizione", Function() WGImpostazioniBrowser.LeggiImmaginiSenzaDescrizione, Sub(v As Boolean) WGImpostazioniBrowser.LeggiImmaginiSenzaDescrizione = v)
            interruttore("Annuncia la lingua dei testi", Function() WGImpostazioniBrowser.AnnunciaLingua, Sub(v As Boolean) WGImpostazioniBrowser.AnnunciaLingua = v)
            interruttore("Regioni e articoli letti a parte come in JAWS, la lettera H legge solo l'intestazione", Function() WGImpostazioniBrowser.RegioniAParte, Sub(v As Boolean) WGImpostazioniBrowser.RegioniAParte = v)
            interruttore("In controllo completo le frecce leggono la pagina quando il fuoco non è su un elemento che le usa", Function() WGImpostazioniBrowser.FrecceLeggonoInControlloCompleto, Sub(v As Boolean) WGImpostazioniBrowser.FrecceLeggonoInControlloCompleto = v)
            interruttore("Accelerazione grafica per WebGL e video, vale dal prossimo avvio di WinGabriel", Function() WGImpostazioniBrowser.AccelerazioneGrafica, Sub(v As Boolean) WGImpostazioniBrowser.AccelerazioneGrafica = v)
            aggiungi("Riproduzione video: " & WGImpostazioniBrowser.NomeRiproduzioneVideo(), Sub(posizione As Integer) ScegliRiproduzioneVideo(posizione))
            interruttore("Riproduci con il lettore di WinGabriel i video che il browser non supporta", Function() WGImpostazioniBrowser.NonSupportatiNelLettore, Sub(v As Boolean) WGImpostazioniBrowser.NonSupportatiNelLettore = v)
            aggiungi("Siti fatti con WordPress: " & WGImpostazioniBrowser.NomeSitiWordPress(), Sub(posizione As Integer) ScegliSitiWordPress(posizione))
            interruttore("Riconosci gli elementi cliccabili senza ruolo", Function() WGImpostazioniBrowser.RiconosciCliccabili, Sub(v As Boolean) WGImpostazioniBrowser.RiconosciCliccabili = v)
            interruttore("Apri i PDF nel lettore di WinGabriel", Function() WGImpostazioniBrowser.ApriPdfNelLettore, Sub(v As Boolean) WGImpostazioniBrowser.ApriPdfNelLettore = v)
            interruttore("Riapri le schede della volta precedente", Function() WGImpostazioniBrowser.RiapriSchede, Sub(v As Boolean) WGImpostazioniBrowser.RiapriSchede = v)
            aggiungi("Identità predefinita verso i siti: " & WGImpostazioniBrowser.NomeIdentita(WGImpostazioniBrowser.IdentitaPredefinita), Sub(posizione As Integer) ScegliIdentitaPredefinita(posizione))
            menu.posiziona(indice)
            menu.avvia("Altre impostazioni del browser", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.MostraImpostazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliMotoreRicerca(posizioneImpostazioni As Integer)
        Try
            Dim menu As New WGMenu()
            For Each motore As String In WGImpostazioniBrowser.MotoriDisponibili
                Dim scelto As String = motore
                menu.aggiungi(WGImpostazioniBrowser.NomeMotore(motore), Sub(s, e)
                                                                            WGImpostazioniBrowser.MotoreRicerca = scelto
                                                                            Annuncia("Motore di ricerca: " & WGImpostazioniBrowser.NomeMotore(scelto))
                                                                            MostraImpostazioni(posizioneImpostazioni)
                                                                        End Sub)
            Next
            menu.avvia("Motore di ricerca", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliMotoreRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliSitiWordPress(posizioneImpostazioni As Integer)
        Try
            Dim menu As New WGMenu()
            Dim scegli As Action(Of String) = Sub(modo As String)
                                                  Try
                                                      WGImpostazioniBrowser.SitiWordPress = modo
                                                      Annuncia("Siti fatti con WordPress: " & WGImpostazioniBrowser.NomeSitiWordPress(modo))
                                                      MostraImpostazioni(posizioneImpostazioni)
                                                  Catch exScelta As Exception
                                                      _log.ScriviLog("WGGestoreBrowser.ScegliSitiWordPress scelta: " & exScelta.ToString())
                                                  End Try
                                              End Sub
            menu.aggiungi("Chiedi ogni volta", Sub(s, e) scegli(WGImpostazioniBrowser.WordPressChiedi))
            menu.aggiungi("Apri con la lettura semplificata", Sub(s, e) scegli(WGImpostazioniBrowser.WordPressSemplificata))
            menu.aggiungi("Apri con la lettura normale", Sub(s, e) scegli(WGImpostazioniBrowser.WordPressNormale))
            Select Case WGImpostazioniBrowser.SitiWordPress
                Case WGImpostazioniBrowser.WordPressSemplificata : menu.posiziona(1)
                Case WGImpostazioniBrowser.WordPressNormale : menu.posiziona(2)
                Case Else : menu.posiziona(0)
            End Select
            menu.avvia("Siti fatti con WordPress", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliSitiWordPress: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliRiproduzioneVideo(posizioneImpostazioni As Integer)
        Try
            Dim menu As New WGMenu()
            Dim scegli As Action(Of String) = Sub(modo As String)
                                                  Try
                                                      WGImpostazioniBrowser.RiproduzioneVideo = modo
                                                      Annuncia("Riproduzione video: " & WGImpostazioniBrowser.NomeRiproduzioneVideo(modo))
                                                      MostraImpostazioni(posizioneImpostazioni)
                                                  Catch exScelta As Exception
                                                      _log.ScriviLog("WGGestoreBrowser.ScegliRiproduzioneVideo scelta: " & exScelta.ToString())
                                                  End Try
                                              End Sub
            menu.aggiungi("Apri il lettore di WinGabriel: il video si apre a schermo con tutti i comandi, chiudendolo torni alla pagina", Sub(s, e) scegli(WGImpostazioniBrowser.RiproduzioneLettore))
            menu.aggiungi("Riproduci in sottofondo: il suono parte senza aprire il lettore e la pagina resta a schermo", Sub(s, e) scegli(WGImpostazioniBrowser.RiproduzioneSottofondo))
            menu.aggiungi("Riproduci dalla pagina: i video di YouTube restano nel lettore del sito, con la sua pubblicità", Sub(s, e) scegli(WGImpostazioniBrowser.RiproduzionePagina))
            Select Case WGImpostazioniBrowser.RiproduzioneVideo
                Case WGImpostazioniBrowser.RiproduzioneSottofondo : menu.posiziona(1)
                Case WGImpostazioniBrowser.RiproduzionePagina : menu.posiziona(2)
                Case Else : menu.posiziona(0)
            End Select
            menu.avvia("Riproduzione video", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliRiproduzioneVideo: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliIdentitaPredefinita(posizioneImpostazioni As Integer)
        Try
            Dim menu As New WGMenu()
            Dim scegli As Action(Of String) = Sub(identita As String)
                                                  Try
                                                      WGImpostazioniBrowser.IdentitaPredefinita = identita
                                                      Annuncia("Identità predefinita: " & WGImpostazioniBrowser.NomeIdentita(identita) & ", vale dalla prossima pagina aperta")
                                                      MostraImpostazioni(posizioneImpostazioni)
                                                  Catch exScelta As Exception
                                                      _log.ScriviLog("WGGestoreBrowser.ScegliIdentitaPredefinita scelta: " & exScelta.ToString())
                                                  End Try
                                              End Sub
            menu.aggiungi("Automatica, consigliata: Chrome come il motore del browser, così l'identità coincide con l'impronta della connessione; Edge su Netflix e sugli altri servizi di video e musica protetti, per usare PlayReady; se Google rifiuta l'accesso prova da sola Firefox", Sub(s, e) scegli(WGImpostazioniBrowser.IdentitaAutomatica))
            menu.aggiungi("Presentati sempre come Chrome, tranne sui servizi di video protetti dove resta Edge", Sub(s, e) scegli(WGImpostazioniBrowser.IdentitaChrome))
            menu.aggiungi("Presentati sempre come Firefox, tranne sui servizi di video protetti dove resta Edge", Sub(s, e) scegli(WGImpostazioniBrowser.IdentitaFirefox))
            menu.aggiungi("Presentati sempre come Edge", Sub(s, e) scegli(WGImpostazioniBrowser.IdentitaEdge))
            Select Case WGImpostazioniBrowser.IdentitaPredefinita
                Case WGImpostazioniBrowser.IdentitaChrome : menu.posiziona(1)
                Case WGImpostazioniBrowser.IdentitaFirefox : menu.posiziona(2)
                Case WGImpostazioniBrowser.IdentitaEdge : menu.posiziona(3)
                Case Else : menu.posiziona(0)
            End Select
            menu.avvia("Identità predefinita verso i siti", True)
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.ScegliIdentitaPredefinita: " & ex.ToString())
        End Try
    End Sub

    Public Async Sub AggiornaElencoPubblicita()
        Try
            Dim numero As Integer = 0
            Await WGProcessoInCorso.EseguiAsync("Aggiornamento dell'elenco dei siti pubblicitari in corso",
                Async Function() As Task
                    numero = Await WGBloccoPubblicita.AggiornaElencoAsync()
                End Function)
            If numero < 0 Then
                MostraMessaggio("Impossibile scaricare l'elenco dei siti pubblicitari. Riprovare più tardi.")
            Else
                MostraMessaggio("Elenco aggiornato: " & numero & " siti pubblicitari bloccati.")
            End If
        Catch ex As Exception
            _log.ScriviLog("WGGestoreBrowser.AggiornaElencoPubblicita: " & ex.ToString())
        End Try
    End Sub
End Class