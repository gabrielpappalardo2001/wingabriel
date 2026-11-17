Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGGiochi
Imports wgcontrols

''' <summary>
''' La chat di WinGabriel, con il menu e i campi normali del programma.
''' Il menu ha quattro voci: Scrivi a, Utenti in linea, Utenti bloccati, Esci dalla chat.
''' Una conversazione è un elenco di messaggi, come nell'intelligenza artificiale: Invio legge il messaggio per intero,
''' in fondo c'è "Scrivi un messaggio" e si scrive con i campi.
''' Resta in ascolto finché WinGabriel è aperto. Se la chat è chiusa e arriva un messaggio, si pubblica una notifica
''' e si chiede se lo si vuole leggere.
''' Si entra con lo stesso nome utente e la stessa password dei giochi.
''' </summary>
Public Class MenuChat

    Private Const IdScrivi As String = "SCRIVI"
    Private Const IdNuovoNome As String = "NUOVO_NOME"
    Private Const IdCodice As String = "CODICE"

    Private ReadOnly _logger As New Logger()
    Private _archivio As ArchivioChatDisco
    Private _motore As MotoreChat
    Private _cicloAttivo As Boolean
    Private _uiAperta As Boolean
    Private _cidAperta As String = ""
    Private _domandaInCorso As Boolean
    Private _avvioMotore As Task(Of Boolean)
    Private _menuChat As WGMenu
    Private _menuContatti As WGMenuLista
    Private _menuInLinea As WGMenuLista
    Private _menuBloccati As WGMenuLista
    Private _menuConversazione As WGMenuLista
    Private _campoMessaggio As WGCampo
    Private _campiScrivi As Campi
    Private ReadOnly _domandeFatte As New HashSet(Of String)()
    Private ReadOnly _codaDomande As New Queue(Of Func(Of Task))()
    ''' <summary>Il programma principale la imposta: pubblica nel centro notifiche. Il testo e l'azione da eseguire se si apre la notifica.</summary>
    Public Shared Property PubblicaNotifica As Action(Of String, Action)
    Private Shared _servizio As MenuChat

    Private ReadOnly Property Cliente As GiochiClient
        Get
            Return GiochiClient.Condiviso
        End Get
    End Property

    ''' <summary>Dall'interno di un gioco: apre la conversazione con un'altra persona. Se la chat non era collegata, si collega.</summary>
    Public Shared Sub ScriviA(nomeUtente As String)
        Try
            AssicuraServizio().ApriPerNome(nomeUtente)
        Catch ex As Exception
            Dim l As New Logger()
            l.ScriviLog("MenuChat.ScriviA: " & ex.Message)
        End Try
    End Sub

    ''' <summary>All'avvio di WinGabriel: se c'è già un nome utente con la password salvata, la chat resta in ascolto anche da chiusa.</summary>
    Public Shared Sub AvviaInSottofondo()
        Try
            Dim ignora As Task = AssicuraServizio().AssicuraMotoreAsync(True)
        Catch ex As Exception
            Dim l As New Logger()
            l.ScriviLog("MenuChat.AvviaInSottofondo: " & ex.Message)
        End Try
    End Sub

    Private Shared Function AssicuraServizio() As MenuChat
        Try
            If _servizio Is Nothing Then _servizio = New MenuChat()
            Return _servizio
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AssicuraServizio", ex)
            Throw
        End Try
    End Function

    Public Sub Avvia()
        Try
            If _servizio IsNot Nothing AndAlso _servizio IsNot Me Then
                _servizio.ApriInterfaccia()
                Return
            End If
            _servizio = Me
            ApriInterfaccia()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.Avvia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriInterfaccia()
        Try
            If _uiAperta Then Return
            _uiAperta = True
            Dim ignora As Task = ApriMenuAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ApriInterfaccia", ex)
            Throw
        End Try
    End Sub

    Private Sub ApriPerNome(nome As String)
        Try
            Dim ignora As Task = ApriPerNomeAsync(nome)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ApriPerNome", ex)
            Throw
        End Try
    End Sub

    Private Async Function ApriMenuAsync() As Task
        Try
            If Not Await AssicuraMotoreAsync(False) Then
                _uiAperta = False
                Return
            End If
            MostraMenu()
        Catch ex As Exception
            _uiAperta = False
            _logger.ScriviLog("MenuChat.ApriMenu: " & ex.ToString())
            Messaggio("Non è stato possibile usare la chat.")
        End Try
    End Function

    Private Async Function ApriPerNomeAsync(nome As String) As Task
        Try
            If Not Await AssicuraMotoreAsync(False) Then Return
            Await ContattaPerNomeAsync(nome)
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.ApriPerNome: " & ex.ToString())
        End Try
    End Function

    ' ------------------------------------------------------------------ collegamento, anche a chat chiusa
    Private Async Function AssicuraMotoreAsync(silenzioso As Boolean) As Task(Of Boolean)
        Try
            If _motore IsNot Nothing Then Return True
            If _avvioMotore IsNot Nothing AndAlso Not _avvioMotore.IsCompleted Then
                Dim riuscito As Boolean = Await _avvioMotore
                If riuscito OrElse silenzioso Then Return riuscito
            End If
            If _motore IsNot Nothing Then Return True
            _avvioMotore = AvviaMotoreAsync(silenzioso)
            Return Await _avvioMotore
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AssicuraMotoreAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function AvviaMotoreAsync(silenzioso As Boolean) As Task(Of Boolean)
        Try
            Cliente.Carica()
            If silenzioso Then
                If Not Await EntraSilenziosoAsync() Then Return False
            Else
                If Cliente.Nome.Length = 0 OrElse Not ArchivioChatDisco.Esiste(Cliente.Nome) Then AccessoAccount.AvvisaChat()
                If Not Await AccessoAccount.AccediAsync(Cliente, True) Then Return False
            End If
            If Not ApriArchivio(silenzioso) Then Return False
            _motore = New MotoreChat(_archivio.Stato, New ReleHttp(), _archivio)
            _motore.ImpostaNomeUtente(Cliente.Nome)
            AddHandler _motore.Evento, AddressOf SuEvento
            _cicloAttivo = True
            Dim ciclo As Task = _motore.AvviaAsync(15)
            Dim presenza As Task = PresenzaAsync()
            Await AggiornaPubblicazioneAsync()
            Return True
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.AvviaMotore: " & ex.ToString())
            If Not silenzioso Then Messaggio("Non è stato possibile usare la chat.")
            Return False
        End Try
    End Function

    ''' <summary>Entra senza chiedere niente, solo se su questo computer ci sono già nome utente e password. Altrimenti falso.</summary>
    Private Async Function EntraSilenziosoAsync() As Task(Of Boolean)
        Try
            If Cliente.Nome.Length = 0 OrElse Cliente.PasswordSalvata().Length = 0 Then Return False
            Dim nome As String = Cliente.Nome
            Dim password As String = Cliente.PasswordSalvata()
            Try
                If Cliente.Master Is Nothing Then
                    Cliente.Master = Await Task.Run(Function() As Byte()
                                                        Dim master As Byte() = Nothing
                                                        GiochiClient.Deriva(nome, password, master)
                                                        Return master
                                                    End Function)
                End If
                If Cliente.Token.Length > 0 AndAlso Await Cliente.SessioneValidaAsync() Then Return True
            Catch ex As GiochiErrore
                If ex.Codice <> 401 Then Return False
            Catch exLog As Exception
                RegistroErrori.Registra("MenuChat.EntraSilenziosoAsync", exLog)
                Return False
            End Try
            Try
                Await Task.Run(Function() Cliente.EntraAsync(nome, password))
                Return Cliente.Master IsNot Nothing
            Catch exLog As Exception
                RegistroErrori.Registra("MenuChat.EntraSilenziosoAsync", exLog)
                Return False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.EntraSilenziosoAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Tiene aggiornata la presenza, così chi ha WinGabriel aperto compare tra gli utenti in linea.</summary>
    Private Async Function PresenzaAsync() As Task
        Try
            While _cicloAttivo AndAlso _motore IsNot Nothing
                Try
                    Await Cliente.MiaAsync()
                Catch exLog As Exception
                    RegistroErrori.Registra("MenuChat.PresenzaAsync", exLog)
                End Try
                Try
                    Await Task.Delay(60000)
                Catch exLog As Exception
                    RegistroErrori.Registra("MenuChat.PresenzaAsync", exLog)
                    Return
                End Try
            End While
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.PresenzaAsync", ex)
            Throw
        End Try
    End Function

    Private Function ApriArchivio(silenzioso As Boolean) As Boolean
        Try
            Dim nome As String = Cliente.Nome
            Try
                If ArchivioChatDisco.Esiste(nome) Then
                    _archivio = ArchivioChatDisco.Apri(nome, Cliente.Master)
                    If _archivio Is Nothing Then
                        If Not silenzioso Then Messaggio("La chat di questo computer non si apre con questa password.")
                        Return False
                    End If
                Else
                    _archivio = ArchivioChatDisco.Crea(nome, Cliente.Master)
                End If
                Return True
            Catch ex As Exception
                _logger.ScriviLog("MenuChat.ApriArchivio: " & ex.Message)
                If Not silenzioso Then Messaggio("Non è stato possibile aprire l'archivio della chat.")
                Return False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ApriArchivio", ex)
            Throw
        End Try
    End Function

    Private Shared Function ScegliAsync(titolo As String, voci As IList(Of String)) As Task(Of Integer)
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
                                               RegistroErrori.Registra("MenuChat.ScegliAsync", exLog)
                                           End Try
                                       End Sub)
            Next
            AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult(-1)
            menu.avvia(titolo, True)
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ScegliAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Function ChiediTestoAsync(titolo As String, predefinito As String) As Task(Of String)
        Try
            Dim esito As New TaskCompletionSource(Of String)()
            Dim campi As New Campi()
            campi.Aggiungi(titolo, "chat.testo", True, False, If(predefinito, ""))
            AddHandler campi.Fine, Sub()
                                       Try
                                           esito.TrySetResult(campi.RecuperaValore("chat.testo"))
                                       Catch exLog As Exception
                                           RegistroErrori.Registra("MenuChat.ChiediTestoAsync", exLog)
                                           esito.TrySetResult(Nothing)
                                       End Try
                                   End Sub
            AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
            campi.Avvia()
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiediTestoAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New Action(Of String)(AddressOf Messaggio), testo)
                Return
            End If
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.Messaggio", ex)
            Throw
        End Try
    End Sub

    Private Shared Sub Leggi(testo As String)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New Action(Of String)(AddressOf Leggi), testo)
                Return
            End If
            Try
                If Not String.IsNullOrEmpty(testo) Then sintesivocale.GetInstance().LeggiTesto(testo)
            Catch exLog As Exception
                RegistroErrori.Registra("MenuChat.Leggi", exLog)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.Leggi", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Tiene il proprio codice trovabile per nome utente, così gli altri possono cercarti.</summary>
    Private Async Function AggiornaPubblicazioneAsync() As Task
        Try
            Dim trovabile As Boolean = _motore.Stato("trovabile") Is Nothing OrElse _motore.Stato("trovabile").Type <> JTokenType.Boolean OrElse CBool(_motore.Stato("trovabile"))
            If Not trovabile Then
                Await Cliente.RitiraChatAsync()
                Return
            End If
            If _motore.Inviti.Count = 0 Then _motore.NuovoInvito()
            Await Cliente.PubblicaChatAsync(CStr(_motore.Inviti.Last("codice")))
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.AggiornaPubblicazione: " & ex.Message)
        End Try
    End Function

    ' ------------------------------------------------------------------ eventi del motore
    Private Sub SuEvento(tipo As String, dati As Object)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New Action(Of String, Object)(AddressOf SuEvento), tipo, dati)
                Return
            End If
            Select Case tipo
                Case "messaggio"
                    Dim t As Tuple(Of String, JObject) = CType(dati, Tuple(Of String, JObject))
                    GestisciMessaggio(t.Item1, t.Item2)
                Case "richiesta"
                    GestisciRichiesta(CType(dati, ContattoChat))
                Case "contatto"
                    If _menuContatti IsNot Nothing Then AggiornaVociContatti()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.SuEvento: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciMessaggio(cid As String, m As JObject)
        Try
            If m Is Nothing OrElse _motore Is Nothing Then Return
            Dim entrata As Boolean = CStr(m("dir")) = "in"
            If Not entrata Then
                If cid = _cidAperta Then AggiornaVociConversazione()
                Return
            End If
            Dim k As ContattoChat = _motore.Contatto(cid)
            If k Is Nothing Then Return
            Dim testo As String = CStr(m("x"))
            If ConversazioneInVista(cid) Then
                _motore.SegnaLetti(cid)
                Leggi(k.Nome & ": " & testo)
                AggiornaVociConversazione()
                Return
            End If
            If InterfacciaAperta() Then
                Leggi("Nuovo messaggio da " & k.Nome)
                If _menuContatti IsNot Nothing Then AggiornaVociContatti()
                Return
            End If
            Dim idMsg As String = If(m("id") Is Nothing, "", CStr(m("id")))
            If idMsg.Length = 0 Then idMsg = cid & ":" & CStr(m("ts"))
            If Not _domandeFatte.Add(idMsg) Then Return
            Dim nome As String = k.Nome
            WGGiochi.SuoniGiochi.SuonaNome("notifica")
            If PubblicaNotifica IsNot Nothing Then PubblicaNotifica.Invoke("Nuovo messaggio da " & nome & ": " & Anteprima(testo), Sub() ApriConversazioneDiretta(cid))
            AccodaDomanda(Async Function()
                              If Await WGDomanda.ChiediAsync("Nuovo messaggio da " & nome & ". Vuoi leggerlo?") Then
                                  _cidAperta = cid
                                  Try
                                      _motore.SegnaLetti(cid)
                                  Catch exLog As Exception
                                      RegistroErrori.Registra("MenuChat.GestisciMessaggio", exLog)
                                  End Try
                                  MostraTesto("Messaggio di " & nome, testo)
                              End If
                          End Function)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.GestisciMessaggio", ex)
            Throw
        End Try
    End Sub

    Private Sub GestisciRichiesta(k As ContattoChat)
        Try
            If k Is Nothing Then Return
            If InterfacciaAperta() Then
                Leggi(k.Nome & " vuole scriverti.")
                If _menuContatti IsNot Nothing Then AggiornaVociContatti()
                Return
            End If
            If Not _domandeFatte.Add("r:" & k.Id) Then Return
            Dim nome As String = k.Nome
            Dim cid As String = k.Id
            WGGiochi.SuoniGiochi.SuonaNome("richiesta")
            If PubblicaNotifica IsNot Nothing Then PubblicaNotifica.Invoke(nome & " vuole scriverti.", Sub() ApriRichiesta(cid))
            AccodaDomanda(Async Function()
                              If Await WGDomanda.ChiediAsync(nome & " vuole scriverti. Vuoi rispondere?") Then Await ApriContattoAsync(cid)
                          End Function)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.GestisciRichiesta", ex)
            Throw
        End Try
    End Sub

    Private Sub AccodaDomanda(lavoro As Func(Of Task))
        Try
            _codaDomande.Enqueue(lavoro)
            If _domandaInCorso Then Return
            Dim ignora As Task = ScorriDomandeAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AccodaDomanda", ex)
            Throw
        End Try
    End Sub

    Private Async Function ScorriDomandeAsync() As Task
        Try
            _domandaInCorso = True
            Try
                While _codaDomande.Count > 0
                    Dim lavoro As Func(Of Task) = _codaDomande.Dequeue()
                    Try
                        Await lavoro()
                    Catch ex As Exception
                        _logger.ScriviLog("MenuChat.Domanda: " & ex.Message)
                    End Try
                End While
            Finally
                _domandaInCorso = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ScorriDomandeAsync", ex)
            Throw
        End Try
    End Function

    Private Function InterfacciaAperta() As Boolean
        Try
            Return _uiAperta OrElse _menuContatti IsNot Nothing OrElse _menuInLinea IsNot Nothing OrElse _menuBloccati IsNot Nothing OrElse _menuConversazione IsNot Nothing OrElse _campoMessaggio IsNot Nothing OrElse _campiScrivi IsNot Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.InterfacciaAperta", ex)
            Throw
        End Try
    End Function

    Private Function ConversazioneInVista(cid As String) As Boolean
        Try
            Return _cidAperta = cid AndAlso (_menuConversazione IsNot Nothing OrElse _campoMessaggio IsNot Nothing OrElse _campiScrivi IsNot Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ConversazioneInVista", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ menu della chat
    Private Sub MostraMenu()
        Try
            _menuChat = New WGMenu()
            _menuChat.aggiungi("Scrivi a", AddressOf VoceScriviA)
            _menuChat.aggiungi("Utenti in linea", AddressOf VoceInLinea)
            _menuChat.aggiungi("Utenti bloccati", AddressOf VoceBloccati)
            _menuChat.aggiungi("Esci dalla chat", AddressOf EsciDallaChat)
            AddHandler _menuChat.abbandonato, AddressOf MenuChatAbbandonato
            _menuChat.avvia("Chat")
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.MostraMenu", ex)
            Throw
        End Try
    End Sub

    Private Sub MenuChatAbbandonato(sender As Object, e As EventArgs)
        Try
            _uiAperta = False
            _menuChat = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.MenuChatAbbandonato", ex)
            Throw
        End Try
    End Sub

    Private Sub EsciDallaChat(sender As Object, e As EventArgs)
        Try
            If _menuChat IsNot Nothing Then _menuChat.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.Esci: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceScriviA(sender As Object, e As EventArgs)
        Try
            MostraContatti()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.VoceScriviA", ex)
            Throw
        End Try
    End Sub

    Private Sub MostraContatti()
        Try
            _menuContatti = New WGMenuLista()
            _menuContatti.multiselezioneConsentita = False
            _menuContatti.impostaTitolo("Scrivi a")
            RiempiContatti()
            AddHandler _menuContatti.escPremuto, AddressOf ChiudiContatti
            AddHandler _menuContatti.ElementoSelezionato, AddressOf ContattoSelezionato
            _menuContatti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.Contatti: " & ex.Message)
        End Try
    End Sub

    Private Sub RiempiContatti()
        Try
            For Each k As ContattoChat In _motore.Contatti().OrderBy(Function(x) If(x.Stato = "richiesta", 0, If(x.NonLetti > 0, 1, 2))).ThenBy(Function(x) x.Nome).ToList()
                _menuContatti.aggiungiElemento(DescrizioneContatto(k), k.Id)
            Next
            _menuContatti.aggiungiElemento("A un altro nome utente", IdNuovoNome)
            _menuContatti.aggiungiElemento("Con un codice", IdCodice)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.RiempiContatti", ex)
            Throw
        End Try
    End Sub

    Private Sub AggiornaVociContatti()
        Try
            If _menuContatti Is Nothing OrElse _menuContatti.IsDisposed OrElse _menuContatti.OpzioniAperte Then Return
            Dim corrente As String = _menuContatti.ottieniElemento()
            _menuContatti.BeginUpdate()
            _menuContatti.svuotaLista()
            RiempiContatti()
            _menuContatti.EndUpdate()
            If corrente.Length > 0 Then _menuContatti.posiziona(corrente)
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.AggiornaContatti: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiContatti(sender As Object, e As EventArgs)
        Try
            ChiudiLista(_menuContatti)
            _menuContatti = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiudiContatti", ex)
            Throw
        End Try
    End Sub

    Private Sub ContattoSelezionato(id As String)
        Try
            ChiudiLista(_menuContatti)
            _menuContatti = Nothing
            If id = IdNuovoNome Then
                Dim ignoraNome As Task = AggiungiPerNomeAsync()
            ElseIf id = IdCodice Then
                Dim ignoraCodice As Task = AggiungiContattoAsync()
            Else
                Dim ignora As Task = ApriContattoAsync(id)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ContattoSelezionato", ex)
            Throw
        End Try
    End Sub

    Private Sub VoceInLinea(sender As Object, e As EventArgs)
        Try
            Dim ignora As Task = VoceInLineaAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.VoceInLinea", ex)
            Throw
        End Try
    End Sub

    Private Async Function VoceInLineaAsync() As Task
        Try
            Dim r As JObject = Await Cliente.InLineaAsync()
            Dim nomi As New List(Of String)()
            Dim elenco As JArray = TryCast(r("utenti"), JArray)
            If elenco IsNot Nothing Then
                For Each voce As JToken In elenco
                    Dim nome As String = CStr(voce).Trim()
                    If nome.Length > 0 Then nomi.Add(nome)
                Next
            End If
            If nomi.Count = 0 Then
                Messaggio("Nessuno è in linea adesso.")
                Return
            End If
            _menuInLinea = New WGMenuLista()
            _menuInLinea.multiselezioneConsentita = False
            _menuInLinea.impostaTitolo("Utenti in linea")
            For Each nome As String In nomi
                _menuInLinea.aggiungiElemento(nome, nome)
            Next
            AddHandler _menuInLinea.escPremuto, AddressOf ChiudiInLinea
            AddHandler _menuInLinea.ElementoSelezionato, AddressOf InLineaSelezionato
            _menuInLinea.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.InLinea: " & ex.Message)
            Messaggio("Non riesco a sapere chi è in linea.")
        End Try
    End Function

    Private Sub ChiudiInLinea(sender As Object, e As EventArgs)
        Try
            ChiudiLista(_menuInLinea)
            _menuInLinea = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiudiInLinea", ex)
            Throw
        End Try
    End Sub

    Private Sub InLineaSelezionato(nome As String)
        Try
            ChiudiLista(_menuInLinea)
            _menuInLinea = Nothing
            Dim ignora As Task = ContattaPerNomeAsync(nome)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.InLineaSelezionato", ex)
            Throw
        End Try
    End Sub

    Private Sub VoceBloccati(sender As Object, e As EventArgs)
        Try
            Dim bloccati As List(Of JObject) = _motore.ElencoBloccati()
            If bloccati.Count = 0 Then
                Messaggio("Non hai bloccato nessuno. Chi blocchi non può scriverti né entrare nelle tue partite, e non lo sa.")
                Return
            End If
            _menuBloccati = New WGMenuLista()
            _menuBloccati.multiselezioneConsentita = False
            _menuBloccati.impostaTitolo("Utenti bloccati. Scegli chi sbloccare")
            For Each b As JObject In bloccati
                _menuBloccati.aggiungiElemento("Sblocca " & CStr(b("nome")), CStr(b("ik")))
            Next
            AddHandler _menuBloccati.escPremuto, AddressOf ChiudiBloccati
            AddHandler _menuBloccati.ElementoSelezionato, AddressOf BloccatoSelezionato
            _menuBloccati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.Bloccati: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiBloccati(sender As Object, e As EventArgs)
        Try
            ChiudiLista(_menuBloccati)
            _menuBloccati = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiudiBloccati", ex)
            Throw
        End Try
    End Sub

    Private Sub BloccatoSelezionato(ik As String)
        Try
            Dim b As JObject = _motore.ElencoBloccati().FirstOrDefault(Function(x) CStr(x("ik")) = ik)
            ChiudiLista(_menuBloccati)
            _menuBloccati = Nothing
            If b Is Nothing Then Return
            Dim ignora As Task = SbloccaPersonaAsync(ik, CStr(b("nome")))
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.BloccatoSelezionato", ex)
            Throw
        End Try
    End Sub

    Private Shared Sub ChiudiLista(menu As WGMenuLista)
        Try
            If menu Is Nothing Then Return
            Try
                menu.abbandona()
            Catch exLog As Exception
                RegistroErrori.Registra("MenuChat.ChiudiLista", exLog)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiudiLista", ex)
            Throw
        End Try
    End Sub

    Private Sub ApriConversazioneDiretta(cid As String)
        Try
            If _campiScrivi IsNot Nothing OrElse _domandaInCorso Then Return
            If _cidAperta = cid AndAlso (_menuConversazione IsNot Nothing OrElse _campoMessaggio IsNot Nothing) Then Return
            ChiudiSuperficiConversazione()
            MostraConversazione(cid)
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.ApriDiretta: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriRichiesta(cid As String)
        Try
            Dim ignora As Task = ApriContattoAsync(cid)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ApriRichiesta", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Anteprima(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim t As String = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If t.Length > 80 Then t = t.Substring(0, 80) & "..."
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.Anteprima", ex)
            Throw
        End Try
    End Function

    Private Function DescrizioneContatto(k As ContattoChat) As String
        Try
            Select Case k.Stato
                Case "richiesta" : Return k.Nome & ", vuole scriverti"
                Case "invitato" : Return k.Nome & ", in attesa che accetti"
                Case "accettato" : Return k.Nome & ", in attesa del primo messaggio"
            End Select
            Dim t As String = k.Nome
            If k.Bloccato Then t &= ", bloccato"
            If k.NonLetti > 0 Then t &= ", " & k.NonLetti.ToString(CultureInfo.InvariantCulture) & If(k.NonLetti = 1, " messaggio nuovo", " messaggi nuovi")
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.DescrizioneContatto", ex)
            Throw
        End Try
    End Function


    Private Async Function SbloccaPersonaAsync(ik As String, nome As String) As Task
        Try
            Dim utente As String = _motore.Sblocca(ik)
            If utente.Length > 0 Then
                Try
                    Await Cliente.SbloccaAsync(utente)
                Catch ex As Exception
                    _logger.ScriviLog("MenuChat.Sblocca: " & ex.Message)
                End Try
            End If
            Leggi(nome & " è stato sbloccato.")
            Dim k As ContattoChat = If(_cidAperta.Length = 0, Nothing, _motore.Contatto(_cidAperta))
            If k IsNot Nothing AndAlso _menuConversazione IsNot Nothing AndAlso Not k.Bloccato Then
                _menuConversazione.sostituisciOpzione("Sblocca", "Blocca", AddressOf OpzioneBlocca)
                _menuConversazione.impostaTitolo(TitoloConversazione(k))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.SbloccaPersonaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function BloccaOSbloccaAsync() As Task
        Try
            Dim k As ContattoChat = _motore.Contatto(_cidAperta)
            If k Is Nothing Then Return
            If k.Bloccato Then
                Await SbloccaPersonaAsync(CStr(k.Dati("ikRemota")), k.Nome)
                Return
            End If
            If Not Await WGDomanda.ChiediAsync("Vuoi bloccare " & k.Nome & "? Non riceverai più niente da questa persona, non potrà chiederti di scrivere né entrare nelle tue partite, e non saprà di essere stata bloccata.") Then Return
            Dim utente As String = _motore.Blocca(_cidAperta)
            If utente.Length > 0 Then
                Try
                    Await Cliente.BloccaAsync(utente)
                Catch ex As Exception
                    _logger.ScriviLog("MenuChat.Blocca: " & ex.Message)
                End Try
            End If
            Leggi(k.Nome & " è stato bloccato.")
            ChiudiConversazione(Nothing, EventArgs.Empty)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.BloccaOSbloccaAsync", ex)
            Throw
        End Try
    End Function
    ''' <summary>Scrive a una persona dal suo nome utente (lo stesso dei giochi): la cerca, chiede di poterle scrivere e apre la conversazione.</summary>
    Private Async Function AggiungiPerNomeAsync() As Task
        Try
            Dim nome As String = Await ChiediTestoAsync("Il nome utente della persona a cui vuoi scrivere", "")
            If String.IsNullOrWhiteSpace(nome) Then Return
            Await ContattaPerNomeAsync(nome.Trim())
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AggiungiPerNomeAsync", ex)
            Throw
        End Try
    End Function

    Public Async Function ContattaPerNomeAsync(nome As String) As Task
        Try
            Dim esistente As ContattoChat = _motore.Contatti().FirstOrDefault(Function(x) String.Equals(x.Utente, nome, StringComparison.OrdinalIgnoreCase))
            If esistente IsNot Nothing Then
                Await ApriContattoAsync(esistente.Id)
                Return
            End If
            Dim r As JObject = Await Cliente.CercaAsync(nome)
            Dim k As ContattoChat = _motore.AggiungiContatto(CStr(r("codice")), CStr(r("nome")))
            Messaggio("Ho preparato la richiesta per " & k.Nome & ". Appena è collegato e accetta, potrete scrivervi. Se adesso non c'è, riprovo da solo anche con la chat chiusa.")
        Catch ex As GiochiErrore
            If ex.Codice = 404 Then
                Messaggio("Non trovo questa persona, oppure non può essere contattata. Se ha un codice, scegli Con un codice.")
            Else
                Messaggio(ex.Message)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ContattaPerNomeAsync", ex)
            Messaggio(ex.Message)
        End Try
    End Function

    Private Async Function AggiungiContattoAsync() As Task
        Try
            Dim codice As String = Await ChiediTestoAsync("Scrivi o incolla il codice della persona: comincia con WGC1", "")
            If String.IsNullOrWhiteSpace(codice) Then Return
            Try
                Dim k As ContattoChat = _motore.AggiungiContatto(codice)
                Messaggio("Ho preparato la richiesta per " & k.Nome & ". Appena è collegata e accetta, potrete scrivervi. Se adesso non c'è, riprovo da solo anche con la chat chiusa.")
            Catch ex As Exception
                RegistroErrori.Registra("MenuChat.AggiungiContattoAsync", ex)
                Messaggio(ex.Message)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AggiungiContattoAsync", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ un contatto
    Private Async Function ApriContattoAsync(cid As String) As Task
        Try
            Dim k As ContattoChat = _motore.Contatto(cid)
            If k Is Nothing Then Return
            Select Case k.Stato
                Case "richiesta"
                    Dim r As Integer = Await ScegliAsync(k.Nome & " vuole scriverti. Accetti?", New String() {"Sì, accetta", "No, rifiuta"})
                    If r = 0 Then
                        _motore.Accetta(cid)
                        Messaggio("Hai accettato. Appena " & k.Nome & " è collegato potrete scrivervi.")
                    ElseIf r = 1 Then
                        _motore.Rifiuta(cid)
                    End If
                Case "invitato"
                    Messaggio(k.Nome & " non ha ancora accettato. La richiesta arriva quando è collegato, anche se la chat è chiusa.")
                Case "accettato"
                    Messaggio("Hai accettato la richiesta. Aspetto che " & k.Nome & " si colleghi e scriva il primo messaggio.")
                Case Else
                    MostraConversazione(cid)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ApriContattoAsync", ex)
            Throw
        End Try
    End Function

    Private Function TitoloConversazione(k As ContattoChat) As String
        Try
            Dim t As String = "Conversazione con " & k.Nome
            If k.Bloccato Then t &= ", bloccato"
            If Not _motore.Collegato Then t &= ", non collegato"
            Return t & ". Premi Invio su un messaggio per leggerlo per intero"
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.TitoloConversazione", ex)
            Throw
        End Try
    End Function

    Private Function TitoloMessaggio(m As JObject, nome As String) As String
        Try
            If CStr(m("dir")) = "in" Then Return "Messaggio di " & nome
            Select Case CStr(m("st"))
                Case "consegnato" : Return "Tuo messaggio, consegnato"
                Case "inviato" : Return "Tuo messaggio, inviato"
                Case "scaduto" : Return "Tuo messaggio, non consegnato"
                Case Else : Return "Tuo messaggio, in attesa che " & nome & " sia collegato"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.TitoloMessaggio", ex)
            Throw
        End Try
    End Function

    Private Function EtichettaMessaggio(m As JObject, nome As String) As String
        Try
            Dim ora As String = DateTimeOffset.FromUnixTimeMilliseconds(CLng(m("ts"))).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            Dim testo As String = CStr(m("x")).Replace(vbCr, " ").Replace(vbLf, " ")
            If testo.Length > 40 Then testo = testo.Substring(0, 40) & "..."
            If CStr(m("dir")) = "in" Then Return ora & " " & nome & ": " & testo
            Dim stato As String
            Select Case CStr(m("st"))
                Case "consegnato" : stato = "consegnato"
                Case "inviato" : stato = "inviato"
                Case "scaduto" : stato = "non consegnato"
                Case Else : stato = "in attesa"
            End Select
            Return ora & " Tu, " & stato & ": " & testo
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.EtichettaMessaggio", ex)
            Throw
        End Try
    End Function

    Private Sub MostraConversazione(cid As String)
        Try
            If _motore Is Nothing Then Return
            Dim k As ContattoChat = _motore.Contatto(cid)
            If k Is Nothing Then Return
            _cidAperta = cid
            Try
                _motore.SegnaLetti(cid)
            Catch ex As Exception
                _logger.ScriviLog("MenuChat.SegnaLetti: " & ex.Message)
            End Try
            If NumeroMessaggi(cid) = 0 AndAlso Not k.Bloccato Then
                AvviaComposizione()
                Return
            End If
            CreaMenuConversazione()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.MostraConversazione", ex)
            Throw
        End Try
    End Sub

    Private Sub CreaMenuConversazione()
        Try
            Dim k As ContattoChat = If(_motore Is Nothing OrElse _cidAperta.Length = 0, Nothing, _motore.Contatto(_cidAperta))
            If k Is Nothing Then
                _cidAperta = ""
                Return
            End If
            If _menuConversazione IsNot Nothing AndAlso Not _menuConversazione.IsDisposed Then
                _menuConversazione.impostaTitolo(TitoloConversazione(k))
                AggiornaVociConversazione()
                Return
            End If
            _menuConversazione = New WGMenuLista()
            _menuConversazione.multiselezioneConsentita = False
            _menuConversazione.impostaTitolo(TitoloConversazione(k))
            RiempiMessaggi(k)
            _menuConversazione.aggiungiOpzione("Cambia il nome", AddressOf OpzioneRinomina)
            _menuConversazione.aggiungiOpzione(If(k.Bloccato, "Sblocca", "Blocca"), AddressOf OpzioneBlocca)
            _menuConversazione.aggiungiOpzione("Elimina la conversazione", AddressOf OpzioneElimina)
            AddHandler _menuConversazione.escPremuto, AddressOf ChiudiConversazione
            AddHandler _menuConversazione.ElementoSelezionato, AddressOf MessaggioSelezionato
            _menuConversazione.avvia()
            Dim quante As Integer = NumeroMessaggi(_cidAperta)
            If quante > 0 Then _menuConversazione.selezionaElemento(quante - 1)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.CreaMenuConversazione", ex)
            Throw
        End Try
    End Sub

    Private Sub RiempiMessaggi(k As ContattoChat)
        Try
            For Each m As JObject In _motore.ElencoMessaggi(_cidAperta).OfType(Of JObject)()
                _menuConversazione.aggiungiElemento(EtichettaMessaggio(m, k.Nome), CStr(m("id")))
            Next
            _menuConversazione.aggiungiElemento("Scrivi un messaggio", IdScrivi)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.RiempiMessaggi", ex)
            Throw
        End Try
    End Sub

    Private Sub AggiornaVociConversazione()
        Try
            If _menuConversazione Is Nothing OrElse _menuConversazione.IsDisposed Then Return
            If _campoMessaggio IsNot Nothing OrElse _campiScrivi IsNot Nothing OrElse _menuConversazione.OpzioniAperte Then Return
            Dim k As ContattoChat = _motore.Contatto(_cidAperta)
            If k Is Nothing Then Return
            Dim corrente As String = _menuConversazione.ottieniElemento()
            _menuConversazione.BeginUpdate()
            _menuConversazione.svuotaLista()
            RiempiMessaggi(k)
            _menuConversazione.EndUpdate()
            If corrente.Length > 0 AndAlso _menuConversazione.VoceEsiste(corrente) Then _menuConversazione.posiziona(corrente)
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.AggiornaConversazione: " & ex.Message)
        End Try
    End Sub

    Private Function NumeroMessaggi(cid As String) As Integer
        Try
            Return _motore.ElencoMessaggi(cid).OfType(Of JObject)().Count()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.NumeroMessaggi", ex)
            Throw
        End Try
    End Function

    Private Function TrovaMessaggio(id As String) As JObject
        Try
            Return _motore.ElencoMessaggi(_cidAperta).OfType(Of JObject)().FirstOrDefault(Function(m) CStr(m("id")) = id)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.TrovaMessaggio", ex)
            Throw
        End Try
    End Function

    Private Sub MessaggioSelezionato(id As String)
        Try
            Dim menu As WGMenuLista = _menuConversazione
            _menuConversazione = Nothing
            ChiudiLista(menu)
            If id = IdScrivi Then
                AvviaComposizione()
                Return
            End If
            Dim k As ContattoChat = _motore.Contatto(_cidAperta)
            Dim m As JObject = TrovaMessaggio(id)
            If k Is Nothing OrElse m Is Nothing Then
                CreaMenuConversazione()
                Return
            End If
            MostraTesto(TitoloMessaggio(m, k.Nome), CStr(m("x")))
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.MessaggioSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiConversazione(sender As Object, e As EventArgs)
        Try
            Dim menu As WGMenuLista = _menuConversazione
            _menuConversazione = Nothing
            ChiudiLista(menu)
            _cidAperta = ""
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.ChiudiConversazione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiSuperficiConversazione()
        Try
            If _campoMessaggio IsNot Nothing Then
                Dim campo As WGCampo = _campoMessaggio
                RemoveHandler campo.chiusura, AddressOf TestoChiuso
                RemoveHandler campo.InvioPremuto, AddressOf InvioSuMessaggio
                _campoMessaggio = Nothing
                Try
                    campo.chiudi()
                Catch exLog As Exception
                    RegistroErrori.Registra("MenuChat.ChiudiSuperficiConversazione", exLog)
                End Try
            End If
            Dim menu As WGMenuLista = _menuConversazione
            _menuConversazione = Nothing
            ChiudiLista(menu)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.ChiudiSuperficiConversazione", ex)
            Throw
        End Try
    End Sub

    Private Sub MostraTesto(titolo As String, testo As String)
        Try
            _campoMessaggio = New WGCampo()
            _campoMessaggio.init(titolo, If(testo, ""), True, True, False, False)
            _campoMessaggio.InvioSollevaEvento = True
            AddHandler _campoMessaggio.chiusura, AddressOf TestoChiuso
            AddHandler _campoMessaggio.InvioPremuto, AddressOf InvioSuMessaggio
            _campoMessaggio.avvia()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.MostraTesto", ex)
            Throw
        End Try
    End Sub

    Private Sub InvioSuMessaggio(sender As Object, e As EventArgs)
        Try
            Dim campo As WGCampo = _campoMessaggio
            If campo Is Nothing Then Return
            RemoveHandler campo.chiusura, AddressOf TestoChiuso
            RemoveHandler campo.InvioPremuto, AddressOf InvioSuMessaggio
            _campoMessaggio = Nothing
            campo.chiudi()
            AvviaComposizione()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.InvioMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub TestoChiuso(sender As Object, e As EventArgs)
        Try
            If _campoMessaggio IsNot Nothing Then
                RemoveHandler _campoMessaggio.chiusura, AddressOf TestoChiuso
                RemoveHandler _campoMessaggio.InvioPremuto, AddressOf InvioSuMessaggio
                _campoMessaggio = Nothing
            End If
            CreaMenuConversazione()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.TestoChiuso: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaComposizione()
        Try
            Dim k As ContattoChat = If(_motore Is Nothing OrElse _cidAperta.Length = 0, Nothing, _motore.Contatto(_cidAperta))
            If k Is Nothing Then
                _cidAperta = ""
                Return
            End If
            If k.Bloccato Then
                Messaggio(k.Nome & " è bloccato: non puoi scrivergli. Sbloccalo dalle opzioni della conversazione.")
                CreaMenuConversazione()
                Return
            End If
            _campiScrivi = New Campi()
            AddHandler _campiScrivi.Chiusura, AddressOf ComposizioneChiusa
            AddHandler _campiScrivi.Fine, AddressOf ComposizioneConfermata
            _campiScrivi.AggiungiMultiriga("Scrivi il messaggio per " & k.Nome & " (premi Invio tre volte per inviare)", "messaggio")
            _campiScrivi.Avvia()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.AvviaComposizione", ex)
            Throw
        End Try
    End Sub

    Private Sub PuliziaComposizione()
        Try
            If _campiScrivi Is Nothing Then Return
            RemoveHandler _campiScrivi.Chiusura, AddressOf ComposizioneChiusa
            RemoveHandler _campiScrivi.Fine, AddressOf ComposizioneConfermata
            _campiScrivi = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.PuliziaComposizione", ex)
            Throw
        End Try
    End Sub

    Private Sub ComposizioneChiusa(sender As Object, e As EventArgs)
        Try
            PuliziaComposizione()
            If _cidAperta.Length > 0 AndAlso NumeroMessaggi(_cidAperta) = 0 Then
                _cidAperta = ""
                Return
            End If
            CreaMenuConversazione()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.ComposizioneChiusa: " & ex.Message)
        End Try
    End Sub

    Private Sub ComposizioneConfermata(sender As Object, e As EventArgs)
        Try
            Dim testo As String = If(_campiScrivi Is Nothing, "", _campiScrivi.RecuperaValore("messaggio"))
            PuliziaComposizione()
            If String.IsNullOrWhiteSpace(testo) Then
                If _cidAperta.Length > 0 AndAlso NumeroMessaggi(_cidAperta) = 0 Then
                    _cidAperta = ""
                    Return
                End If
            Else
                Try
                    _motore.Scrivi(_cidAperta, testo)
                    Leggi("Messaggio scritto.")
                Catch ex As Exception
                    RegistroErrori.Registra("MenuChat.ComposizioneConfermata", ex)
                    Messaggio(ex.Message)
                End Try
            End If
            CreaMenuConversazione()
        Catch ex As Exception
            _logger.ScriviLog("MenuChat.ComposizioneConfermata: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRinomina(sender As Object, e As EventArgs)
        Try
            Dim ignora As Task = RinominaAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.OpzioneRinomina", ex)
            Throw
        End Try
    End Sub

    Private Sub OpzioneBlocca(sender As Object, e As EventArgs)
        Try
            Dim ignora As Task = BloccaOSbloccaAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.OpzioneBlocca", ex)
            Throw
        End Try
    End Sub

    Private Sub OpzioneElimina(sender As Object, e As EventArgs)
        Try
            Dim ignora As Task = EliminaAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.OpzioneElimina", ex)
            Throw
        End Try
    End Sub

    Private Async Function RinominaAsync() As Task
        Try
            Dim k As ContattoChat = _motore.Contatto(_cidAperta)
            If k Is Nothing Then Return
            Dim nome As String = Await ChiediTestoAsync("Nome da mostrare per questo contatto, solo per te", k.Nome)
            If String.IsNullOrWhiteSpace(nome) Then Return
            _motore.Rinomina(_cidAperta, nome.Trim())
            k = _motore.Contatto(_cidAperta)
            If k IsNot Nothing AndAlso _menuConversazione IsNot Nothing Then
                _menuConversazione.impostaTitolo(TitoloConversazione(k))
                AggiornaVociConversazione()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.RinominaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function EliminaAsync() As Task
        Try
            Dim k As ContattoChat = _motore.Contatto(_cidAperta)
            If k Is Nothing Then Return
            If Not Await WGDomanda.ChiediAsync("Vuoi eliminare la conversazione con " & k.Nome & " e il contatto? I messaggi vengono cancellati da questo computer.") Then Return
            _motore.Elimina(_cidAperta)
            Leggi("Conversazione eliminata.")
            ChiudiConversazione(Nothing, EventArgs.Empty)
        Catch ex As Exception
            RegistroErrori.Registra("MenuChat.EliminaAsync", ex)
            Throw
        End Try
    End Function

End Class

Friend Module EstensioniChat
    ''' <summary>Gli ultimi n elementi (TakeLast non c'è in .NET Framework).</summary>
    <System.Runtime.CompilerServices.Extension>
    Public Function TakeLast2(Of T)(origine As IEnumerable(Of T), n As Integer) As IEnumerable(Of T)
        Try
            Dim l As List(Of T) = origine.ToList()
            Return l.Skip(Math.Max(0, l.Count - n))
        Catch ex As Exception
            RegistroErrori.Registra("EstensioniChat.TakeLast2", ex)
            Throw
        End Try
    End Function
End Module
