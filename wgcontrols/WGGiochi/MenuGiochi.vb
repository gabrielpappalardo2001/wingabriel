Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' L'ambiente dei giochi di WinGabriel (WGGiochi): burraco, monopoli, briscola e scopa, con le loro varianti, contro il computer o contro altri giocatori online.
''' Si entra scegliendo solo un nome; il gioco gira sul server (giochi.php), questo programma ne mostra lo stato e invia le mosse.
'''  - A briscola e a scopa, quando tocca a te, il menu delle carte si apre da solo dopo i messaggi e ogni voce dice il punteggio.
'''  - Quando si deve scegliere un'altra mossa compare un menu con le scelte possibili.
'''  - Quando si aspetta si usa l'attesa del contenitore di WinGabriel (WGContenitore), con gli ultimi messaggi sullo schermo; con Esc si
'''    chiude la partita e si chiede se si vuole interrompere. Non si salva niente.
'''  - Nel menu ci sono anche le varianti che sono giochi a sé (briscola a chiamata, briscolino, scopone scientifico, asso pigliatutto, scopa a 15):
'''    partono direttamente. Gli altri giochi, avviandoli, chiedono quale variante usare (per esempio la marianna alla briscola, la napola alla scopa).
'''  - Da ogni gioco si può creare un tavolo, vedere i tavoli aperti e invitare un giocatore per nome utente.
'''  - Si può copiare un link da condividere: chi lo apre entra nella stessa partita dal web.
'''  - Ogni messaggio della partita (mosse degli altri, messaggi di chat, avvisi) si legge con la sintesi vocale e si vede sullo schermo.
''' </summary>
Partial Public Class MenuGiochi

    Private ReadOnly _logger As New Logger()
    Private _client As GiochiClient = GiochiClient.Condiviso
    Private _inCorso As Boolean = False
    ' Lo schermo di base dell'applicazione: sta nel contenitore per tutta la durata dei giochi, sotto menu, domande e partite, così
    ' l'applicazione non resta mai senza finestre (e il contenitore non torna al menu iniziale) fra una scelta e l'altra.
    Private _base As SchermoGioco

    ''' <summary>Il programma principale la imposta: apre la chat cifrata con una persona (anche dall'interno di una partita).</summary>
    Public Shared Property ScriviInPrivato As Action(Of String)

    Private Const SECONDI_ATTESA_LUNGA As Integer = 15
    Private Const SECONDI_ATTESA_BREVE As Integer = 2

    Public Async Sub Avvia()
        Try
            If _inCorso Then Return
            _inCorso = True
            Try
                ' senza internet (impostazione Giochi) si usa il motore locale, con un suo utente
                _client = If(GiochiLocali.Offline, GiochiClient.Locale, GiochiClient.Condiviso)
                _client.Carica()
                ApriBase()
                Do
                    Dim nomiMenu As New List(Of String)()
                    For Each voce As VoceGioco In VociGiochi
                        nomiMenu.Add(voce.Nome)
                    Next
                    If Not _client.ELocale Then nomiMenu.Add("Configurazione")
                    Dim scelta As Integer = Await ScegliAsync("Giochi", nomiMenu)
                    If scelta < 0 Then Exit Do
                    If scelta >= VociGiochi.Length AndAlso Not _client.ELocale Then
                        Await ConfigurazioneAsync()
                    Else
                        Await ScegliModoAsync(VociGiochi(scelta))
                    End If
                Loop
            Catch ex As Exception
                _logger.ScriviLog("MenuGiochi.Avvia: " & ex.ToString())
                Messaggio("Non riesco ad aprire i giochi.")
            Finally
                ChiudiBase()
                _inCorso = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Avvia", ex)
            Throw
        End Try
    End Sub

    Private Sub ApriBase()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione("Giochi")
            _base = New SchermoGioco("Giochi")
            _base.Istruzione = "Burraco, monopoli, briscola e scopa."
            _base.TestoAnnuncio = Function() ""
            _base.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ApriBase: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiBase()
        Try
            Try
                If _base IsNot Nothing Then _base.Chiudi()
            Catch ex As Exception
                _logger.ScriviLog("MenuGiochi.ChiudiBase: " & ex.Message)
            End Try
            _base = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiudiBase", ex)
            Throw
        End Try
    End Sub

#Region "Interfaccia"

    ''' <summary>Un menu di scelte. Restituisce la posizione scelta, oppure -1 con Esc.</summary>
    Friend Shared Function ScegliAsync(titolo As String, voci As IList(Of String), Optional posizioneIniziale As Integer = 0) As Task(Of Integer)
        Try
            Dim esito As New TaskCompletionSource(Of Integer)()
            Dim menu As New WGMenu()
            For i As Integer = 0 To voci.Count - 1
                Dim indice As Integer = i
                menu.aggiungi(voci(i), Sub()
                                           esito.TrySetResult(indice)
                                           Try
                                               menu.abbandona()
                                           Catch exLog As Exception
                                               RegistroErrori.Registra("MenuGiochi.ScegliAsync", exLog)
                                           End Try
                                       End Sub)
            Next
            AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult(-1)
            If posizioneIniziale > 0 AndAlso posizioneIniziale < voci.Count Then menu.posiziona(posizioneIniziale)
            menu.avvia(titolo, True)
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Chiede un testo. Restituisce Nothing se si annulla.</summary>
    Friend Shared Function ChiediTestoAsync(titolo As String, predefinito As String) As Task(Of String)
        Try
            Dim esito As New TaskCompletionSource(Of String)()
            Dim campi As New Campi()
            campi.Aggiungi(titolo, "giochi.testo", True, False, If(predefinito, ""))
            AddHandler campi.Fine, Sub()
                                       Try
                                           esito.TrySetResult(campi.RecuperaValore("giochi.testo"))
                                       Catch exLog As Exception
                                           RegistroErrori.Registra("MenuGiochi.ChiediTestoAsync", exLog)
                                           esito.TrySetResult(Nothing)
                                       End Try
                                   End Sub
            ' La chiusura può arrivare insieme alla conferma: le si dà il tempo di lasciar passare prima il testo.
            AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
            campi.Avvia()
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiediTestoAsync", ex)
            Throw
        End Try
    End Function

    Friend Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Messaggio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Aumenta a ogni nuova lettura: una lettura a messaggi (vedi LeggiASegmenti) si ferma se ne parte un'altra.</summary>
    Friend Shared GenLettura As Integer = 0

    ''' <summary>
    ''' Vero solo la prima volta in assoluto che serve un suggerimento (la scelta si ricorda nella configurazione): da lì non si dice più.
    ''' </summary>
    Friend Shared Function PrimoSuggerimento(chiave As String) As Boolean
        Try
            Dim c As New WGConfig()
            If c.EsisteId(chiave) Then Return False
            c.SalvaValore(chiave, "true")
            Return True
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.PrimoSuggerimento", exLog)
            Return False
        End Try
    End Function

    ''' <summary>Quando è stata chiesta l'ultima lettura (in ticks, UTC): chi aspetta che la voce finisca sa se deve ancora aspettare che cominci.</summary>
    Friend Shared UltimaLetturaTicks As Long = 0

    Friend Shared Sub SegnaLettura()
        Try
            System.Threading.Interlocked.Exchange(UltimaLetturaTicks, DateTime.UtcNow.Ticks)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.SegnaLettura", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se una lettura è stata chiesta da meno di "millisecondi": può darsi che la voce non abbia ancora cominciato.</summary>
    Friend Shared Function LetturaRecente(millisecondi As Integer) As Boolean
        Try
            Dim t As Long = System.Threading.Interlocked.Read(UltimaLetturaTicks)
            Return t <> 0 AndAlso (DateTime.UtcNow.Ticks - t) < CLng(millisecondi) * TimeSpan.TicksPerMillisecond
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.LetturaRecente", ex)
            Throw
        End Try
    End Function

    Friend Shared Sub Leggi(testo As String)
        Try
            System.Threading.Interlocked.Increment(GenLettura)
            SegnaLettura()
            sintesivocale.GetInstance().LeggiTesto(testo)
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.Leggi", exLog)
        End Try
    End Sub

#End Region

#Region "Configurazione e accesso"

    ''' <summary>Chiede una password (il testo non si vede). Restituisce Nothing se si annulla.</summary>
    Friend Shared Function ChiediPasswordAsync(titolo As String) As Task(Of String)
        Try
            Dim esito As New TaskCompletionSource(Of String)()
            Dim campi As New Campi()
            campi.AggiungiPassword(titolo, "giochi.password", True, False, "")
            AddHandler campi.Fine, Sub()
                                       Try
                                           esito.TrySetResult(campi.RecuperaValore("giochi.password"))
                                       Catch exLog As Exception
                                           RegistroErrori.Registra("MenuGiochi.ChiediPasswordAsync", exLog)
                                           esito.TrySetResult(Nothing)
                                       End Try
                                   End Sub
            AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
            campi.Avvia()
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiediPasswordAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function ConfigurazioneAsync() As Task
        Try
            Do
                Dim voci As New List(Of String)()
                Dim azioni As New List(Of Integer)()
                If _client.Nome.Length > 0 Then
                    voci.Add("Cambia account")
                    azioni.Add(0)
                    voci.Add("Elimina account")
                    azioni.Add(1)
                Else
                    voci.Add("Accedi all'account o crea un account")
                    azioni.Add(2)
                End If
                voci.Add("Persone bloccate")
                azioni.Add(3)
                Dim scelta As Integer = Await ScegliAsync("Configurazione", voci)
                If scelta < 0 Then Return
                Select Case azioni(scelta)
                    Case 0
                        If Await WGDomanda.ChiediAsync("Vuoi cambiare account?") Then
                            _client.ImpostaNome("")
                            Await AccediAsync()
                        End If
                    Case 1
                        Await EliminaAccountUtenteAsync()
                    Case 2
                        Await AccediAsync()
                    Case 3
                        Await BloccatiAsync()
                End Select
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ConfigurazioneAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le persone bloccate: chi blocchi non entra nelle tue partite, non ti trova né ti scrive in chat, e non lo sa.</summary>
    Private Async Function BloccatiAsync() As Task
        Try
            If Not Await AccediAsync() Then Return
            Do
                Dim elenco As JObject
                Try
                    elenco = Await _client.BloccatiAsync()
                Catch ex As GiochiErrore
                    Messaggio(ex.Message)
                    Return
                End Try
                Dim nomi As List(Of String) = CType(elenco("bloccati"), JArray).Select(Function(x) Convert.ToString(x)).ToList()
                Dim voci As New List(Of String)()
                voci.Add("Blocca un nome utente")
                For Each n As String In nomi
                    voci.Add("Sblocca " & n)
                Next
                Dim scelta As Integer = Await ScegliAsync("Persone bloccate", voci)
                If scelta < 0 Then Return
                Try
                    If scelta = 0 Then
                        Dim nome As String = Await ChiediTestoAsync("Il nome utente da bloccare", "")
                        If Not String.IsNullOrWhiteSpace(nome) Then Await _client.BloccaAsync(nome.Trim())
                    Else
                        Await _client.SbloccaAsync(nomi(scelta - 1))
                    End If
                Catch ex As GiochiErrore
                    Messaggio(ex.Message)
                End Try
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.BloccatiAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function EliminaAccountUtenteAsync() As Task
        Try
            If _client.Nome.Length = 0 Then Return
            If Not Await WGDomanda.ChiediAsync("Vuoi eliminare l'account?") Then Return
            If Not Await AccediAsync() Then Return
            Dim password As String = Await ChiediPasswordAsync("Per eliminare l'account scrivi la sua password")
            If String.IsNullOrEmpty(password) Then Return
            Try
                Dim operazione As Task = _client.EliminaAccountAsync(password)
                Await AttesaGiochi.EseguiAsync("Elimino l'account", Function() operazione)
                Messaggio("L'account è stato eliminato.")
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.EliminaAccountUtenteAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Si assicura di essere entrati nel server. Senza password non si entra; se è salvata cifrata si usa quell'utente.</summary>
    Private Function AccediAsync() As Task(Of Boolean)
        Try
            Return AccessoAccount.AccediAsync(_client, False)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.AccediAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Per giocare con altri: l'avviso, poi nome utente e password. Se è già salvata cifrata si entra con quell'utente.</summary>
    Private Async Function CredenzialiPerGliAltriAsync() As Task(Of Boolean)
        Try
            Dim pronta As Boolean = _client.Master IsNot Nothing
            If pronta Then
                Try
                    pronta = Await _client.SessioneValidaAsync()
                Catch ex As GiochiErrore
                    pronta = False
                End Try
            End If
            If pronta Then Return True
            AccessoAccount.AvvisaChat()
            Return Await AccessoAccount.AccediAsync(_client, True)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CredenzialiPerGliAltriAsync", ex)
            Throw
        End Try
    End Function

#End Region

#Region "Catalogo dei giochi e delle varianti"

    ''' <summary>Una variante di un gioco, come la descrive il server (o, se il server non risponde, come la conosce questo programma).</summary>
    Private Class VarianteGioco
        Public Id As String = "classica"
        Public Nome As String = "Classica"
        Public Descrizione As String = ""
        ''' <summary>I numeri di giocatori ammessi. Vuoto: vale l'elenco del gioco.</summary>
        Public Posti As Integer() = New Integer() {}
    End Class

    ''' <summary>
    ''' Una voce del menu dei giochi. Con la variante vuota è un gioco con le sue varianti interne: avviandolo si sceglie quale usare.
    ''' Con la variante piena è una variante che è un gioco a sé (scopone scientifico, asso pigliatutto...): sta nel menu e parte direttamente.
    ''' </summary>
    Private Class VoceGioco
        Public ReadOnly Nome As String
        Public ReadOnly Gioco As String
        Public ReadOnly Variante As String

        Public Sub New(nome As String, gioco As String, Optional variante As String = "")
            Try
                Me.Nome = nome
                Me.Gioco = gioco
                Me.Variante = variante
            Catch ex As Exception
                RegistroErrori.Registra("MenuGiochi.VoceGioco.New", ex)
                Throw
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' I giochi del menu, nell'ordine in cui compaiono. Le varianti che sono giochi a sé hanno qui la loro riga, con l'id che usa il server;
    ''' tutte le altre varianti di un gioco si scelgono quando lo si avvia. Per spostare una variante dal gioco al menu (o viceversa)
    ''' basta aggiungere o togliere la sua riga.
    ''' </summary>
    Private Shared ReadOnly VociGiochi As VoceGioco() = New VoceGioco() {
        New VoceGioco("Burraco", "burraco"),
        New VoceGioco("Monopoli", "monopoli"),
        New VoceGioco("Briscola", "briscola"),
        New VoceGioco("Briscola a chiamata", "briscola", "chiamata"),
        New VoceGioco("Briscolino", "briscola", "briscolino"),
        New VoceGioco("Scopa", "scopa"),
        New VoceGioco("Scopone scientifico", "scopa", "scopone"),
        New VoceGioco("Asso pigliatutto", "scopa", "asso_piglia_tutto"),
        New VoceGioco("Scopa a 15", "scopa", "scopa_quindici"),
        New VoceGioco("Battaglia navale", "battaglia")
    }

    Private Shared Function NuovaVariante(id As String, nome As String, descrizione As String, ParamArray posti As Integer()) As VarianteGioco
        Try
            Return New VarianteGioco With {.Id = id, .Nome = nome, .Descrizione = descrizione, .Posti = posti}
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NuovaVariante", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Le varianti come le conosce questo programma. Servono se il server non le elenca e, per i numeri di giocatori, sempre:
    ''' sono quelli che il motore di ogni variante accetta (per esempio la briscola a chiamata si gioca solo in cinque).
    ''' </summary>
    Private Shared Function VariantiPredefinite(gioco As String) As List(Of VarianteGioco)
        Try
            Dim r As New List(Of VarianteGioco)()
            Select Case gioco
                Case "burraco"
                    r.Add(NuovaVariante("classica", "Classica", "Partita tradizionale a punti, a due giocatori o a coppie in quattro.", 2, 4))
                Case "monopoli"
                    r.Add(NuovaVariante("classica", "Classica", "Regole standard complete, con aste, ipoteche e quattro case per albergo.", 2, 3, 4))
                    r.Add(NuovaVariante("parcheggio_ricco", "Parcheggio ricco", "Tutte le tasse e le uscite di prigione vanno nel piatto del Parcheggio gratuito.", 2, 3, 4))
                    r.Add(NuovaVariante("doppio_via", "Doppio al Via", "Fermarsi con tiro esatto sulla casella del Via raddoppia il premio a 400 euro.", 2, 3, 4))
                    r.Add(NuovaVariante("completo", "Regole complete", "Combina Parcheggio ricco e Doppio al Via.", 2, 3, 4))
                    r.Add(NuovaVariante("veloce", "Partita veloce", "Due proprietà iniziali a testa, tre case per albergo, fine al primo fallimento.", 2, 3, 4))
                Case "briscola"
                    r.Add(NuovaVariante("classica", "Classica", "Quaranta carte, tre carte a testa, vince chi supera 60 punti su 120. Due giocatori, oppure quattro a coppie.", 2, 4))
                    r.Add(NuovaVariante("al_meglio", "Al meglio delle tre", "Vince chi per primo si aggiudica due smazzate da 61 punti.", 2, 4))
                    r.Add(NuovaVariante("chiamata", "A chiamata", "In cinque, otto carte a testa. Chi apre chiama una carta e il suo seme diventa briscola: chi la possiede è il compagno segreto. Chiamante e compagno vincono se fanno almeno 61 punti insieme.", 5))
                    r.Add(NuovaVariante("marianna", "Marianna con accusi", "Dichiarando re e cavallo dello stesso seme dopo una presa si ottengono 20 punti, 40 se è briscola. Si gioca a 250 punti.", 2, 4))
                    r.Add(NuovaVariante("scoperta", "Scoperta", "Le carte di tutti sono scoperte sul tavolo, per una partita a viso aperto.", 2, 4))
                    r.Add(NuovaVariante("briscolino", "Briscolino", "In tre. Si toglie un due, tredici carte a testa, prese individuali.", 3))
                Case "battaglia"
                    r.Add(NuovaVariante("classica", "Classica", "Dieci navi (una da quattro caselle, due da tre, tre da due e quattro da una) che non si possono toccare. Si spara a turno.", 2))
                    r.Add(NuovaVariante("cinque_navi", "Cinque navi", "Cinque navi da cinque, quattro, tre, tre e due caselle, che possono toccarsi. Si spara a turno.", 2))
                    r.Add(NuovaVariante("a_raffica", "A raffica", "Come la classica, ma chi colpisce una nave spara ancora.", 2))
                Case "scopa"
                    r.Add(NuovaVariante("classica", "Classica", "Si gioca fino a 11 punti, con carte, denari, settebello, primiera e scope.", 2, 4))
                    r.Add(NuovaVariante("punti_21", "A 21 punti", "La partita tradizionale lunga.", 2, 4))
                    r.Add(NuovaVariante("scopone", "Scopone scientifico", "In quattro a coppie. Nove carte a testa e quattro sul tavolo, distribuite tutte all'inizio.", 4))
                    r.Add(NuovaVariante("asso_piglia_tutto", "Asso pigliatutto", "Giocando l'asso si prende tutto il tavolo, se non c'è già un asso singolo da prendere.", 2, 4))
                    r.Add(NuovaVariante("napola", "Napola", "Una sequenza continua di denari dall'asso vale punti: asso, due e tre fanno 3 punti, con il quattro 4, fino a 10 punti per il re.", 2, 4))
                    r.Add(NuovaVariante("rebello", "Re bello", "Chi prende il re di denari ha un punto in più, oltre al settebello.", 2, 4))
                    r.Add(NuovaVariante("scopa_quindici", "Scopa a 15", "Si prende quando la carta giocata, sommata a quelle sul tavolo, fa 15.", 2, 4))
                    r.Add(NuovaVariante("completa", "Completa", "Napola, asso pigliatutto e re bello insieme.", 2, 4))
            End Select
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.VariantiPredefinite", ex)
            Throw
        End Try
    End Function

    ' Le varianti di ogni gioco lette dal server, tenute dopo la prima volta che servono.
    Private _variantiServer As Dictionary(Of String, List(Of VarianteGioco))

    ''' <summary>Legge dal server i giochi e le loro varianti. Se non riesce si usano quelle predefinite, senza tenerle: la prossima volta si riprova.</summary>
    Private Shared _versioneAvvisata As Boolean

    ''' <summary>Se il server consiglia una versione di WinGabriel più nuova di questa, lo si dice una volta sola.</summary>
    Private Sub AvvisaVersioneVecchia(consigliata As String)
        Try
            If _versioneAvvisata OrElse String.IsNullOrWhiteSpace(consigliata) Then Return
            Dim minima As System.Version = Nothing
            If Not System.Version.TryParse(consigliata.Trim(), minima) Then Return
            If GiochiClient.VersioneProgramma >= minima Then Return
            _versioneAvvisata = True
            Messaggio("Questa versione di WinGabriel è più vecchia di quella consigliata per i giochi: aggiornala per avere tutte le novità.")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.AvvisaVersioneVecchia", ex)
            Throw
        End Try
    End Sub

    Private Async Function CaricaVariantiAsync() As Task
        Try
            If _variantiServer IsNot Nothing Then Return
            Try
                Dim lettura As Task(Of JObject) = _client.GiochiAsync()
                Dim r As JObject = Await AttesaGiochi.EseguiAsync("Leggo i giochi", Function() lettura)
                AvvisaVersioneVecchia(Txt(r("consigliata")))
                Dim trovate As New Dictionary(Of String, List(Of VarianteGioco))()
                For Each g As JObject In CType(r("giochi"), JArray)
                    Dim elenco As New List(Of VarianteGioco)()
                    Dim varianti As JArray = TryCast(g("varianti"), JArray)
                    If varianti IsNot Nothing Then
                        For Each x As JObject In varianti
                            Dim v As New VarianteGioco()
                            v.Id = Txt(x("id"))
                            v.Nome = If(Txt(x("nome")).Length > 0, Txt(x("nome")), v.Id)
                            v.Descrizione = Txt(x("descrizione"))
                            Dim posti As JArray = TryCast(x("posti"), JArray)
                            If posti IsNot Nothing Then v.Posti = posti.Select(Function(p) Convert.ToInt32(p)).ToArray()
                            If v.Id.Length > 0 Then elenco.Add(v)
                        Next
                    End If
                    trovate(Txt(g("id"))) = elenco
                Next
                _variantiServer = trovate
            Catch ex As Exception
                _logger.ScriviLog("MenuGiochi.CaricaVarianti: " & ex.Message)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CaricaVariantiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le varianti di un gioco: quelle del server, con i numeri di giocatori che il programma conosce; se il server non ne elenca, quelle predefinite.</summary>
    Private Function VariantiDi(gioco As String) As List(Of VarianteGioco)
        Try
            Dim predefinite As List(Of VarianteGioco) = VariantiPredefinite(gioco)
            Dim daServer As List(Of VarianteGioco) = Nothing
            If _variantiServer Is Nothing OrElse Not _variantiServer.TryGetValue(gioco, daServer) OrElse daServer.Count = 0 Then Return predefinite
            For Each v As VarianteGioco In daServer
                Dim id As String = v.Id
                Dim nota As VarianteGioco = predefinite.FirstOrDefault(Function(p) p.Id = id)
                If nota Is Nothing Then Continue For
                If nota.Posti.Length > 0 Then v.Posti = nota.Posti
                If v.Descrizione.Length = 0 Then v.Descrizione = nota.Descrizione
            Next
            Return daServer
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.VariantiDi", ex)
            Throw
        End Try
    End Function

    ''' <summary>La variante è una di quelle che hanno la loro voce nel menu dei giochi?</summary>
    Private Shared Function EVoceDiMenu(gioco As String, variante As String) As Boolean
        Try
            For Each voce As VoceGioco In VociGiochi
                If voce.Gioco = gioco AndAlso voce.Variante.Length > 0 AndAlso voce.Variante = variante Then Return True
            Next
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.EVoceDiMenu", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Il tavolo (o l'invito) è di questa voce del menu? Una voce con la variante propria mostra solo i suoi tavoli;
    ''' il gioco di base mostra quelli di tutte le altre varianti.
    ''' </summary>
    Private Shared Function TavoloDellaVoce(voce As VoceGioco, tavolo As JObject) As Boolean
        Try
            If Txt(tavolo("gioco")) <> voce.Gioco Then Return False
            Dim v As String = Txt(tavolo("variante"))
            If v.Length = 0 Then v = "classica"
            If voce.Variante.Length > 0 Then Return v = voce.Variante
            Return Not EVoceDiMenu(voce.Gioco, v)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TavoloDellaVoce", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' La variante da usare. Per una voce con la variante propria è quella; per gli altri giochi, se ne hanno più d'una, si sceglie
    ''' (senza quelle che hanno la loro voce nel menu). Restituisce Nothing se si annulla.
    ''' </summary>
    Private Async Function ScegliVarianteAsync(voce As VoceGioco) As Task(Of VarianteGioco)
        Try
            Await CaricaVariantiAsync()
            Dim tutte As List(Of VarianteGioco) = VariantiDi(voce.Gioco)
            If voce.Variante.Length > 0 Then
                Dim idVoce As String = voce.Variante
                Dim propria As VarianteGioco = tutte.FirstOrDefault(Function(x) x.Id = idVoce)
                If propria Is Nothing AndAlso _variantiServer IsNot Nothing Then
                    Messaggio(voce.Nome & " non è ancora disponibile.")
                    Return Nothing
                End If
                If propria Is Nothing Then propria = New VarianteGioco With {.Id = voce.Variante, .Nome = voce.Nome}
                Return propria
            End If
            Dim gioco As String = voce.Gioco
            Dim interne As List(Of VarianteGioco) = tutte.Where(Function(x) Not EVoceDiMenu(gioco, x.Id)).ToList()
            If interne.Count = 0 Then Return New VarianteGioco()
            If interne.Count = 1 Then Return interne(0)
            Dim testi As New List(Of String)()
            For Each cand As VarianteGioco In interne
                testi.Add(If(cand.Descrizione.Length > 0, cand.Nome & ". " & cand.Descrizione, cand.Nome))
            Next
            Dim scelta As Integer = Await ScegliAsync(voce.Nome & ", quale variante", testi)
            If scelta < 0 Then Return Nothing
            Return interne(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliVarianteAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quanti giocatori ammette la variante.</summary>
    Private Function PostiAmmessi(gioco As String, variante As VarianteGioco) As Integer()
        Try
            If variante.Posti.Length > 0 Then Return variante.Posti
            Dim idVariante As String = variante.Id
            Dim predefinita As VarianteGioco = VariantiPredefinite(gioco).FirstOrDefault(Function(p) p.Id = idVariante)
            If predefinita IsNot Nothing AndAlso predefinita.Posti.Length > 0 Then Return predefinita.Posti
            Return If(gioco = "monopoli", New Integer() {2, 3, 4}, New Integer() {2, 4})
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.PostiAmmessi", ex)
            Throw
        End Try
    End Function

    Private Shared Function NumeroInParole(n As Integer) As String
        Try
            Select Case n
                Case 2 : Return "due"
                Case 3 : Return "tre"
                Case 4 : Return "quattro"
                Case 5 : Return "cinque"
                Case Else : Return n.ToString(CultureInfo.InvariantCulture)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NumeroInParole", ex)
            Throw
        End Try
    End Function

#End Region

#Region "Scelta della partita"

    Private Async Function ScegliModoAsync(voce As VoceGioco) As Task
        Try
            If Not Await AccediAsync() Then Return

            ' una partita rimasta aperta (per esempio dopo aver chiuso WinGabriel) si riprende
            Try
                Dim mia As JObject = Await _client.MiaAsync()
                Dim idAperta As String = Convert.ToString(mia("id"))
                If Not String.IsNullOrEmpty(idAperta) AndAlso mia("id").Type <> JTokenType.Null Then
                    Leggi("Riprendo la tua partita.")
                    Await GiocaAsync(idAperta)
                    Return
                End If
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
                Return
            End Try

            If _client.ELocale Then
                ' senza internet si gioca solo contro il computer
                Await CreaPartitaAsync(voce, True)
                Return
            End If

            Await AvvisaInvitiAsync(voce)

            Do
                Dim scelta As Integer = Await ScegliAsync(voce.Nome, New String() {"Gioca contro il computer", "Crea un tavolo", "Tavoli disponibili", "Invita un giocatore"})
                If scelta < 0 Then Return
                If scelta > 0 AndAlso Not Await CredenzialiPerGliAltriAsync() Then Continue Do
                Dim finita As Boolean = False
                Select Case scelta
                    Case 0
                        finita = Await CreaPartitaAsync(voce, True)
                    Case 1
                        finita = Await CreaPartitaAsync(voce, False)
                    Case 2
                        finita = Await EntraInPartitaAsync(voce)
                    Case 3
                        finita = Await InvitaGiocatoreAsync(voce)
                End Select
                If finita Then Return
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliModoAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Se qualcuno ti ha invitato a un tavolo di questo gioco (e di questa variante), lo dice prima del menu.</summary>
    Private Async Function AvvisaInvitiAsync(voce As VoceGioco) As Task
        Try
            Dim inviti As JObject = Await _client.InvitiAsync()
            Dim n As Integer = 0
            For Each s As JObject In CType(inviti("inviti"), JArray)
                If TavoloDellaVoce(voce, s) Then n += 1
            Next
            If n = 1 Then
                Leggi("Hai un invito a un tavolo, lo trovi tra i tavoli disponibili.")
            ElseIf n > 1 Then
                Leggi("Hai " & n.ToString(CultureInfo.InvariantCulture) & " inviti a un tavolo, li trovi tra i tavoli disponibili.")
            End If
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.AvvisaInvitiAsync", exLog)
        End Try
    End Function

    ''' <summary>Quanti giocatori. Se la variante ne ammette uno solo (scopone, briscolino, briscola a chiamata) non si chiede. Restituisce 0 se si annulla.</summary>
    Private Async Function ScegliPostiAsync(voce As VoceGioco, variante As VarianteGioco) As Task(Of Integer)
        Try
            Dim posti As Integer() = PostiAmmessi(voce.Gioco, variante)
            If posti.Length = 1 Then Return posti(0)
            Dim acoppie As Boolean = voce.Gioco = "burraco" OrElse voce.Gioco = "briscola" OrElse voce.Gioco = "scopa"
            Dim voci As New List(Of String)()
            For Each n As Integer In posti
                If acoppie AndAlso n = 2 Then
                    voci.Add("Due giocatori, uno contro uno")
                ElseIf acoppie AndAlso n = 4 Then
                    voci.Add("Quattro giocatori, a coppie")
                Else
                    voci.Add(n.ToString(CultureInfo.InvariantCulture) & " giocatori")
                End If
            Next
            Dim scelta As Integer = Await ScegliAsync(voce.Nome & ", quanti giocatori", voci)
            If scelta < 0 Then Return 0
            Return posti(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliPostiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Al burraco: fino a quanti punti si gioca. Per gli altri giochi 0. Restituisce -1 se si annulla.</summary>
    Private Async Function ScegliPuntiAsync(gioco As String) As Task(Of Integer)
        Try
            If gioco <> "burraco" Then Return 0
            Dim obiettivi As Integer() = {2005, 1005, 505}
            Dim scelta As Integer = Await ScegliAsync("Burraco, fino a quanti punti si gioca", New String() {"2005 punti, la partita completa", "1005 punti, una partita più breve", "505 punti, una partita veloce"})
            If scelta < 0 Then Return -1
            Return obiettivi(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliPuntiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>La chiave dell'impostazione del livello del computer: "chiedi" (si sceglie a ogni partita), "facile", "normale" o "difficile".</summary>
    Friend Const CHIAVE_LIVELLO As String = "GiochiLivello"

    ''' <summary>
    ''' Il livello del computer per la partita che si crea. Se l'impostazione dice di chiederlo, contro il computer si sceglie da un elenco;
    ''' altrimenti si usa quello impostato. Per un tavolo con altre persone, dove il computer entra solo se si avvia prima, non si chiede:
    ''' vale il livello impostato, o normale. Restituisce Nothing se si annulla.
    ''' </summary>
    Private Async Function ScegliLivelloAsync(voce As VoceGioco, controComputer As Boolean) As Task(Of String)
        Try
            Dim impostato As String = "chiedi"
            Try
                Dim c As New WGConfig()
                If c.EsisteId(CHIAVE_LIVELLO) Then impostato = If(c.LeggiValore(CHIAVE_LIVELLO), "chiedi").Trim().ToLowerInvariant()
            Catch exConfig As Exception
                RegistroErrori.Registra("MenuGiochi.ScegliLivelloAsync (impostazione)", exConfig)
            End Try
            If impostato = "facile" OrElse impostato = "normale" OrElse impostato = "difficile" Then Return impostato
            If Not controComputer Then Return "normale"
            Dim livelli As String() = {"facile", "normale", "difficile"}
            Dim scelta As Integer = Await ScegliAsync(voce.Nome & ", livello del computer", New String() {"Facile", "Normale", "Difficile"}, 1)
            If scelta < 0 Then Return Nothing
            Return livelli(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliLivelloAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Crea la partita e la gioca. Restituisce True se si è usciti dai giochi, False per tornare alla scelta.</summary>
    Private Async Function CreaPartitaAsync(voce As VoceGioco, controComputer As Boolean) As Task(Of Boolean)
        Try
            Dim variante As VarianteGioco = Await ScegliVarianteAsync(voce)
            If variante Is Nothing Then Return False
            Dim posti As Integer = Await ScegliPostiAsync(voce, variante)
            If posti = 0 Then Return False
            Dim punti As Integer = Await ScegliPuntiAsync(voce.Gioco)
            If punti < 0 Then Return False
            Dim livello As String = Await ScegliLivelloAsync(voce, controComputer)
            If livello Is Nothing Then Return False
            Dim id As String = ""
            Try
                Dim creazione As Task(Of JObject) = _client.CreaAsync(voce.Gioco, posti, controComputer, punti, variante.Id, livello)
                Dim testoAttesa As String = If(controComputer, "Preparo la partita", "Preparo il tavolo") & " per " & NumeroInParole(posti) & " giocatori"
                Dim r As JObject = Await AttesaGiochi.EseguiAsync(testoAttesa, Function() creazione)
                id = Convert.ToString(r("id"))
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
                Return False
            End Try
            Await GiocaAsync(id)
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CreaPartitaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function EntraInPartitaAsync(voce As VoceGioco) As Task(Of Boolean)
        Try
            Dim tavoli As JObject
            Try
                Dim lettura As Task(Of JObject) = _client.TavoliAsync(voce.Gioco)
                tavoli = Await AttesaGiochi.EseguiAsync("Cerco i tavoli aperti", Function() lettura)
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
                Return False
            End Try
            Dim elenco As New List(Of JObject)()
            For Each s As JObject In CType(tavoli("tavoli"), JArray)
                If TavoloDellaVoce(voce, s) Then elenco.Add(s)
            Next
            Dim voci As New List(Of String)()
            voci.Add("Scrivi il codice o il link del tavolo")
            For Each s As JObject In elenco
                Dim invitato As Boolean = s("invitato") IsNot Nothing AndAlso s("invitato").Type <> JTokenType.Null AndAlso Convert.ToBoolean(s("invitato"))
                ' al gioco di base si dice anche la variante del tavolo (Napola, Marianna con accusi...); le voci con la variante propria la sanno già
                Dim idVariante As String = Txt(s("variante"))
                Dim nomeVariante As String = Txt(s("variante_nome"))
                Dim conVariante As String = If(voce.Variante.Length = 0 AndAlso idVariante.Length > 0 AndAlso idVariante <> "classica" AndAlso nomeVariante.Length > 0, ", " & nomeVariante, "")
                Dim frase As String = If(invitato, "Invito di ", "Tavolo di ") & Convert.ToString(s("creatore")) & conVariante & ", " & Convert.ToString(s("occupati")) & " giocatori su " & Convert.ToString(s("posti"))
                voci.Add(frase)
            Next
            Dim titolo As String = If(elenco.Count = 0, "Nessun tavolo aperto", "Tavoli disponibili")
            Dim scelta As Integer = Await ScegliAsync(titolo, voci)
            If scelta < 0 Then Return False
            Dim id As String = ""
            If scelta = 0 Then
                Dim scritto As String = Await ChiediTestoAsync("Codice del tavolo o link da aprire", "")
                id = IdDaLinkOCodice(scritto)
                If id.Length = 0 Then Return False
            Else
                Dim scelto As JObject = elenco(scelta - 1)
                id = Convert.ToString(scelto("id"))
                Dim invitato As Boolean = scelto("invitato") IsNot Nothing AndAlso scelto("invitato").Type <> JTokenType.Null AndAlso Convert.ToBoolean(scelto("invitato"))
                If invitato Then
                    Dim cosa As Integer = Await ScegliAsync("Invito di " & Convert.ToString(scelto("creatore")), New String() {"Entra al tavolo", "Rifiuta l'invito"})
                    If cosa < 0 Then Return False
                    If cosa = 1 Then
                        Try
                            Await _client.RifiutaInvitoAsync(id)
                            Leggi("Invito rifiutato.")
                        Catch ex As GiochiErrore
                            Messaggio(ex.Message)
                        End Try
                        Return False
                    End If
                End If
            End If
            Try
                Await _client.EntraStanzaAsync(id)
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
                Return False
            End Try
            Await GiocaAsync(id)
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.EntraInPartitaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Crea un tavolo e invita un giocatore, poi resta in attesa con gli altri. True se si è usciti dai giochi.</summary>
    Private Async Function InvitaGiocatoreAsync(voce As VoceGioco) As Task(Of Boolean)
        Try
            Dim variante As VarianteGioco = Await ScegliVarianteAsync(voce)
            If variante Is Nothing Then Return False
            Dim posti As Integer = Await ScegliPostiAsync(voce, variante)
            If posti = 0 Then Return False
            Dim punti As Integer = Await ScegliPuntiAsync(voce.Gioco)
            If punti < 0 Then Return False
            Dim nome As String = Await ChiediTestoAsync("Nome utente del giocatore da invitare", "")
            If String.IsNullOrWhiteSpace(nome) Then Return False
            Dim id As String = ""
            Try
                Dim creazione As Task(Of JObject) = _client.CreaAsync(voce.Gioco, posti, False, punti, variante.Id)
                Dim r As JObject = Await AttesaGiochi.EseguiAsync("Preparo il tavolo per " & NumeroInParole(posti) & " giocatori", Function() creazione)
                id = Convert.ToString(r("id"))
                Dim invito As Task(Of JObject) = _client.InvitaAsync(id, nome.Trim())
                Dim esito As JObject = Await AttesaGiochi.EseguiAsync("Invito " & nome.Trim() & "", Function() invito)
                Dim invitato As String = Convert.ToString(esito("nome"))
                Leggi("Hai invitato " & If(invitato.Length > 0, invitato, nome.Trim()) & ".")
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
                If id.Length = 0 Then Return False
            End Try
            Await GiocaAsync(id)
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.InvitaGiocatoreAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dal tavolo in attesa, invita un altro giocatore per nome utente.</summary>
    Private Async Function InvitaAlTavoloAsync(id As String) As Task
        Try
            Dim nome As String = Await ChiediTestoAsync("Nome utente del giocatore da invitare", "")
            If String.IsNullOrWhiteSpace(nome) Then Return
            Try
                Dim invito As Task(Of JObject) = _client.InvitaAsync(id, nome.Trim())
                Dim esito As JObject = Await AttesaGiochi.EseguiAsync("Invito " & nome.Trim() & "", Function() invito)
                Dim invitato As String = Convert.ToString(esito("nome"))
                Leggi("Hai invitato " & If(invitato.Length > 0, invitato, nome.Trim()) & ".")
            Catch ex As GiochiErrore
                Messaggio(ex.Message)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.InvitaAlTavoloAsync", ex)
            Throw
        End Try
    End Function

#End Region

#Region "La partita"

    Private Class DatiPartita
        Public Property Id As String = ""
        Public Property Da As Integer = 0
        Public Property UltimiMessaggi As New List(Of String)()
        Public Property Attesa As AttesaGiochi
        Public Property SuggerimentoDato As Boolean = False
        ''' <summary>Il suggerimento su Alt (ipotecare, costruire, scambi) si dice una volta sola per partita.</summary>
        Public Property SuggerimentoAltDato As Boolean = False
        ''' <summary>I messaggi nuovi di questo giro, uno per uno (Shift salta quello che si sta leggendo).</summary>
        Public Property Segmenti As New List(Of String)()
        Public Property InAttesaDiGiocatori As Boolean = False
        ''' <summary>Battaglia navale: ci sono colpi del computer ancora da mostrare; si aspetta che finiscano suoni e voce del tuo colpo.</summary>
        Public Property PausaTurno As Boolean = False
        Public Property EraMioTurno As Boolean = False
        Public Property Esci As Boolean = False
        ''' <summary>Battaglia navale: i due casellari (la propria flotta e il mare nemico) restano gli stessi da una mossa all'altra, con il cursore dov'era.</summary>
        Public Property MiaFlotta As CasellarioNavale
        Public Property MareNemico As CasellarioNavale
        ''' <summary>"mia" o "nemica": quale dei due casellari si vede.</summary>
        Public Property MareAttivo As String = ""
        Public Property UltimoEventoComposto As Integer = -1
        ''' <summary>Interrompe la lettura in corso quando si preme Esc durante l'attesa.</summary>
        Public Property Interruzione As CancellationTokenSource
    End Class

    Private Shared Sub FermaAttesa(d As DatiPartita)
        Try
            If d.Attesa IsNot Nothing Then
                d.Attesa.Dispose()
                d.Attesa = Nothing
            End If
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.FermaAttesa", exLog)
        End Try
    End Sub

    ''' <summary>I testi dei messaggi nuovi dello stato (mosse di tutti, chat, avvisi); i messaggi privati sono già solo per chi spettano.</summary>
    Private Function TestiNuovi(stato As JObject) As List(Of String)
        Try
            Dim testi As New List(Of String)()
            For Each e As JObject In CType(stato("eventi"), JArray)
                Dim tipo As String = Convert.ToString(e("tipo"))
                Dim testo As String = Convert.ToString(e("testo"))
                If tipo = "chat" Then
                    Dim chi As String = Convert.ToString(e("da"))
                    If String.Equals(chi, _client.Nome, StringComparison.Ordinal) Then Continue For
                    testo = chi & " scrive: " & testo
                End If
                If testo.Length > 0 Then testi.Add(testo)
            Next
            Return testi
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestiNuovi", ex)
            Throw
        End Try
    End Function

    Private Shared Function UnisciTesti(testi As IEnumerable(Of String)) As String
        Try
            Dim sb As New StringBuilder()
            For Each t As String In testi
                If sb.Length > 0 Then sb.Append(" ")
                Dim pulito As String = t.Trim()
                sb.Append(pulito)
                If Not (pulito.EndsWith(".") OrElse pulito.EndsWith("!") OrElse pulito.EndsWith("?")) Then sb.Append(".")
            Next
            Return sb.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.UnisciTesti", ex)
            Throw
        End Try
    End Function

    Private Async Function GiocaAsync(id As String) As Task
        Try
            Dim d As New DatiPartita()
            d.Id = id
            Dim stato As JObject = Nothing
            Dim nomeGioco As String = ""
            Dim primoGiro As Boolean = True
            ' se la rete cade il client riprova da solo e dura a lungo: si dice una volta quando cade e una quando torna, e non si esce dalla partita
            _client.AvvisoConnessione = Sub(connesso As Boolean) Leggi(If(connesso, "Connessione ripresa.", "Connessione assente. Riprovo."))
            Try
                Do
                    If stato Is Nothing Then stato = Await _client.StatoAsync(d.Id, d.Da, 0, False, CancellationToken.None)
                    d.Da = Convert.ToInt32(stato("ultimo_evento"))
                    d.PausaTurno = Txt(stato("gioco")) = "battaglia" AndAlso SpezzaTurniBattaglia(stato, d)
                    nomeGioco = Convert.ToString(stato("nome_gioco"))
                    Dim nomeVariante As String = Txt(stato("variante_nome"))
                    If nomeVariante.Length > 0 AndAlso Txt(stato("variante")) <> "classica" Then nomeGioco &= ", " & nomeVariante
                    If Not primoGiro Then SuoniGiochi.Suona(CType(stato("eventi"), JArray))   ' aprendo la partita non si rifanno i suoni di tutta la storia
                    primoGiro = False
                    Dim testi As List(Of String) = TestiNuovi(stato)
                    For Each t As String In testi
                        d.UltimiMessaggi.Add(t)
                    Next
                    While d.UltimiMessaggi.Count > 30
                        d.UltimiMessaggi.RemoveAt(0)
                    End While
                    Dim messaggi As String = UnisciTesti(testi)
                    d.Segmenti = New List(Of String)(testi)
                    Dim statoPartita As String = Convert.ToString(stato("stato"))
                    d.InAttesaDiGiocatori = statoPartita = "attesa"
                    Dim posto As Integer = Convert.ToInt32(stato("posto"))
                    Dim mosse As JArray = CType(stato("mosse"), JArray)

                    ' ---- partita finita: il messaggio finale (mosse e punti) si legge e si vede
                    If statoPartita = "finita" Then
                        FermaAttesa(d)
                        Messaggio(If(messaggi.Length > 0, messaggi, "La partita è finita."))
                        Try
                            Await _client.LasciaAsync(d.Id)
                        Catch exLog As Exception
                            RegistroErrori.Registra("MenuGiochi.GiocaAsync", exLog)
                        End Try
                        Return
                    End If

                    ' ---- si aspetta che altri giocatori entrino
                    If statoPartita = "attesa" Then
                        If posto = 0 Then
                            FermaAttesa(d)
                            Dim giocatori As JArray = CType(stato("giocatori"), JArray)
                            Dim occupati As Integer = giocatori.Where(Function(g) g.Type <> JTokenType.Null).Count()
                            Dim titolo As String = UnisciTesti(New String() {messaggi, nomeGioco & ", codice del tavolo " & d.Id & ". Ci sono " & occupati.ToString() & " giocatori su " & giocatori.Count.ToString()})
                            Dim scelta As Integer = Await ScegliAsync(titolo, New String() {"Aspetta altri giocatori", "Invita un giocatore", "Copia il link", "Avvia adesso, il computer prende i posti liberi", "Annulla la partita"})
                            If scelta = 1 Then
                                Await InvitaAlTavoloAsync(d.Id)
                                stato = Nothing
                                Continue Do
                            ElseIf scelta = 2 Then
                                CopiaLinkPartita(d.Id)
                                stato = Nothing
                                Continue Do
                            ElseIf scelta = 3 Then
                                Try
                                    Await _client.AvviaAsync(d.Id)
                                Catch ex As GiochiErrore
                                    Messaggio(ex.Message)
                                End Try
                                stato = Nothing
                                Continue Do
                            ElseIf scelta = 4 Then
                                Try
                                    Await _client.LasciaAsync(d.Id)
                                Catch exLog As Exception
                                    RegistroErrori.Registra("MenuGiochi.GiocaAsync", exLog)
                                End Try
                                Return
                            ElseIf scelta < 0 Then
                                If Await ChiediUscitaMenuAsync(d) Then Return
                                stato = Nothing
                                Continue Do
                            End If
                            messaggi = ""
                        End If
                        stato = Await AttendiAsync(d, If(posto = 0, "Aspetto gli altri giocatori. Codice del tavolo " & d.Id & ".", "Aspetto che la partita cominci."), messaggi, stato)
                        If stato Is Nothing Then Return
                        Continue Do
                    End If

                    ' ---- tocca a te (o devi rispondere a una proposta): la lista delle cose da fare
                    If mosse.Count > 0 Then
                        FermaAttesa(d)
                        Dim primaVolta As Boolean = Not d.EraMioTurno
                        d.EraMioTurno = True
                        Dim scelta As RisultatoScelta = Await ScegliMossaAsync(d, messaggi, primaVolta, stato)
                        Select Case scelta.Tipo
                            Case "aggiorna"
                                stato = scelta.Stato
                            Case "esci"
                                If Await ChiediUscitaMenuAsync(d) Then Return
                                stato = Nothing
                            Case Else
                                Try
                                    Dim invio As Task(Of JObject) = _client.MossaAsync(d.Id, scelta.IdMossa, d.Da)
                                    stato = Await AttesaGiochi.EseguiAsync("Invio la mossa", Function() invio)
                                Catch ex As GiochiErrore
                                    Messaggio(ex.Message)
                                    stato = Nothing
                                End Try
                        End Select
                        Continue Do
                    End If

                    ' ---- tocca a un altro: si aspetta
                    d.EraMioTurno = False
                    Dim chi As String = ""
                    Dim turno As JToken = If(stato("attore") IsNot Nothing AndAlso stato("attore").Type <> JTokenType.Null, stato("attore"), stato("turno"))
                    If turno IsNot Nothing AndAlso turno.Type <> JTokenType.Null Then
                        Dim giocatori As JArray = CType(stato("giocatori"), JArray)
                        Dim indice As Integer = Convert.ToInt32(turno)
                        If indice >= 0 AndAlso indice < giocatori.Count AndAlso giocatori(indice).Type <> JTokenType.Null Then chi = Convert.ToString(giocatori(indice)("nome"))
                    End If
                    Dim testoAttesa As String = If(chi.Length > 0, "Aspetto la mossa di " & chi & ".", "Aspetto.")
                    If Txt(stato("gioco")) = "battaglia" AndAlso chi.Length > 0 Then
                        testoAttesa = If(Txt(TryCast(stato("vista"), JObject)("fase")) = "posizionamento", "Aspetto che " & chi & " finisca di posizionare le navi.", "Aspetto il colpo di " & chi & ".")
                    End If
                    Dim vista As JObject = TryCast(stato("vista"), JObject)
                    If vista IsNot Nothing AndAlso vista("asta") IsNot Nothing AndAlso vista("asta").Type <> JTokenType.Null Then
                        testoAttesa = If(chi.Length > 0, "Aspetto l'offerta di " & chi & " all'asta per " & Convert.ToString(vista("asta")("nome")) & ".", "Aspetto l'asta.")
                    End If
                    If vista IsNot Nothing AndAlso vista("scambio") IsNot Nothing AndAlso vista("scambio").Type <> JTokenType.Null Then
                        Dim giocatoriScambio As JArray = CType(stato("giocatori"), JArray)
                        Dim da As Integer = Convert.ToInt32(vista("scambio")("da"))
                        Dim a As Integer = Convert.ToInt32(vista("scambio")("a"))
                        If da = posto Then
                            testoAttesa = "Aspetto la risposta di " & Convert.ToString(giocatoriScambio(a)("nome")) & " alla tua proposta di scambio."
                        Else
                            testoAttesa = Convert.ToString(giocatoriScambio(da)("nome")) & " e " & Convert.ToString(giocatoriScambio(a)("nome")) & " stanno trattando uno scambio."
                        End If
                    End If
                    stato = Await AttendiAsync(d, testoAttesa, messaggi, stato)
                    If stato Is Nothing Then Return
                Loop
            Catch ex As GiochiErrore
                FermaAttesa(d)
                If ex.Codice = 401 Then _client.DimenticaSessione()
                If ex.Codice = 404 Then
                    Messaggio("La partita non c'è più.")
                Else
                    Messaggio(ex.Message)
                End If
            Catch ex As OperationCanceledException
                FermaAttesa(d)
            Catch ex As Exception
                FermaAttesa(d)
                _logger.ScriviLog("MenuGiochi.GiocaAsync: " & ex.ToString())
                Messaggio("Non riesco a continuare la partita.")
            Finally
                _client.AvvisoConnessione = Nothing
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.GiocaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Aspetta con l'attesa dei giochi (che usa quella del contenitore) finché c'è qualcosa di nuovo: un messaggio, la fine della partita, una mossa da fare.
    ''' I messaggi nuovi restano sullo schermo, nell'attesa, e si leggono con la sintesi vocale. Con Esc si chiede se interrompere. Non si salva niente.
    ''' Restituisce il nuovo stato, oppure Nothing se si è lasciata la partita.
    ''' </summary>
    Private Async Function AttendiAsync(d As DatiPartita, testoAttesa As String, messaggiNuovi As String, statoPrecedente As JObject) As Task(Of JObject)
        Try
            ' Sullo schermo: gli ultimi messaggi, quello che si aspetta e come interrompere. A voce: i messaggi nuovi; la prima volta anche come interrompere.
            Dim righe As New List(Of String)()
            For Each m As String In d.UltimiMessaggi.Skip(Math.Max(0, d.UltimiMessaggi.Count - 6))
                righe.Add(m)
            Next
            righe.Add(testoAttesa)
            If IstruzioniAttive() Then righe.Add("Esc interrompe il gioco")
            Dim schermo As String = String.Join(vbCrLf, righe)
            If d.Attesa Is Nothing Then
                Dim a As New AttesaGiochi()
                AddHandler a.InterruzioneRichiesta, Sub(s As Object, e As EventArgs)
                                                        d.Esci = True
                                                        Try
                                                            If d.Interruzione IsNot Nothing Then d.Interruzione.Cancel()
                                                        Catch exLog As Exception
                                                            RegistroErrori.Registra("MenuGiochi.AttendiAsync", exLog)
                                                        End Try
                                                    End Sub
                d.Attesa = a
                Dim parlato As String = UnisciTesti(New String() {messaggiNuovi, testoAttesa})
                If Not d.SuggerimentoDato Then
                    d.SuggerimentoDato = True
                    If IstruzioniAttive() Then parlato &= " Esc interrompe il gioco."
                End If
                a.Avvia(schermo, parlato)
            ElseIf messaggiNuovi.Length > 0 Then
                d.Attesa.Aggiorna(schermo, messaggiNuovi)
            End If
            ' battaglia navale: il computer spara solo dopo che sono finiti il suono e la voce del tuo colpo
            If d.PausaTurno Then
                d.PausaTurno = False
                Await AttendiFineAudioAsync()
            End If

            Dim precedente As String = Convert.ToString(statoPrecedente("stato"))
            Dim turnoPrecedente As String = Convert.ToString(statoPrecedente("turno"))
            Do
                d.Interruzione = New CancellationTokenSource()
                Dim stato As JObject = Nothing
                Try
                    stato = Await _client.StatoAsync(d.Id, d.Da, SECONDI_ATTESA_LUNGA, False, d.Interruzione.Token)
                Catch ex As OperationCanceledException When d.Esci
                    ' Esc durante l'attesa: la lettura è stata interrotta
                End Try
                If d.Esci Then
                    ' si chiede se lasciare la partita; se no, si va avanti rileggendo lo stato
                    If Await ChiediUscitaAsync(d) Then Return Nothing
                    If stato Is Nothing Then stato = Await _client.StatoAsync(d.Id, d.Da, 0, False, CancellationToken.None)
                    Return stato
                End If
                Dim novita As Boolean = CType(stato("eventi"), JArray).Count > 0 _
                    OrElse CType(stato("mosse"), JArray).Count > 0 _
                    OrElse Convert.ToString(stato("stato")) <> precedente _
                    OrElse Convert.ToString(stato("turno")) <> turnoPrecedente
                If novita Then Return stato
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.AttendiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Chiede se lasciare la partita (da un'attesa interrotta con Esc). True se si è lasciata.</summary>
    Private Async Function ChiediUscitaAsync(d As DatiPartita) As Task(Of Boolean)
        Try
            FermaAttesa(d)
            d.Esci = False
            If Await ChiediUscitaMenuAsync(d) Then Return True
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiediUscitaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Domanda sì o no. Sì interrompe e non salva niente. Escape vale no e si resta in partita.</summary>
    Private Async Function ChiediUscitaMenuAsync(d As DatiPartita) As Task(Of Boolean)
        Try
            If Not Await WGDomanda.ChiediAsync("Vuoi interrompere la partita?") Then Return False
            Try
                Await _client.LasciaAsync(d.Id)
            Catch exLog As Exception
                RegistroErrori.Registra("MenuGiochi.ChiediUscitaMenuAsync", exLog)
            End Try
            d.Esci = True
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiediUscitaMenuAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Copia negli appunti il link web della partita e lo legge.</summary>
    Friend Shared Sub CopiaLinkPartita(id As String)
        Try
            Dim link As String = GiochiClient.LinkPartita(id)
            Try
                Clipboard.SetText(link)
                Leggi("Link copiato. " & link)
            Catch ex As Exception
                RegistroErrori.Registra("MenuGiochi.CopiaLinkPartita", ex)
                Leggi("Non riesco a copiare il link. Eccolo: " & link)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CopiaLinkPartita", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Dal testo scritto (codice o link) ricava l'id della partita.</summary>
    Friend Shared Function IdDaLinkOCodice(testo As String) As String
        Try
            Dim t As String = If(testo, "").Trim()
            If t.Length = 0 Then Return ""
            Dim marca As Integer = t.IndexOf("p=", StringComparison.OrdinalIgnoreCase)
            If marca >= 0 Then
                Dim resto As String = t.Substring(marca + 2)
                Dim taglio As Integer = resto.IndexOfAny(New Char() {"&"c, "#"c, " "c, "/"c, "?"c})
                If taglio >= 0 Then resto = resto.Substring(0, taglio)
                t = resto.Trim()
            End If
            Return t.ToUpperInvariant()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IdDaLinkOCodice", ex)
            Throw
        End Try
    End Function

#End Region

End Class
