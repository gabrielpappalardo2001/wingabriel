Imports System.Text.RegularExpressions
Imports System.Threading
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' I giochi senza internet: dalle impostazioni (Giochi) si sceglie se giocare online, con il server, oppure offline, con il motore che sta
''' dentro WinGabriel. La scelta è la chiave di configurazione GiochiOffline (true = offline).
''' </summary>
Friend NotInheritable Class GiochiLocali
    Public Const CHIAVE_OFFLINE As String = "GiochiOffline"

    Private Sub New()
    End Sub

    ''' <summary>Dalle impostazioni: si gioca senza internet, con il motore sul computer.</summary>
    Public Shared ReadOnly Property Offline As Boolean
        Get
            Try
                Dim c As New WGConfig()
                If Not c.EsisteId(CHIAVE_OFFLINE) Then Return False
                Return String.Equals(If(c.LeggiValore(CHIAVE_OFFLINE), "").Trim(), "true", StringComparison.OrdinalIgnoreCase)
            Catch exLog As Exception
                RegistroErrori.Registra("GiochiLocali.New", exLog)
                Return False
            End Try
        End Get
    End Property
End Class

''' <summary>
''' Il "server" dei giochi dentro WinGabriel, per giocare senza internet. Risponde alle stesse richieste di giochi.php (giochi, crea,
''' stato, mossa, lascia, chat...) con gli stessi dati, ma solo per una persona contro il computer, tutto in memoria: lo schermo di gioco
''' non sa se parla con il server o con questo motore. Le regole dei giochi sono nei motori (MotoreBurraco, MotoreMonopoli,
''' MotoreBriscola, MotoreScopa), riscritti da quelli del server.
''' Le partite durano finché WinGabriel resta aperto; il nome utente locale è quello di Windows e non esce dal computer.
''' </summary>
Friend NotInheritable Class MotoreLocale

    Private Class PostoStanza
        Public Nome As String
        Public Bot As Boolean
    End Class

    Private Class Stanza
        Public Id As String
        Public Gioco As String
        Public Variante As String = "classica"
        Public Stato As String = "attesa"
        Public Posti As New List(Of PostoStanza)()
        Public Versione As Integer = 0
        Public UltimoEvento As Integer = 0
        Public Eventi As New List(Of JObject)()
        Public Motore As IMotoreGioco = Nothing
        Public Punti As Integer = 2005
        ''' <summary>Il livello del computer: facile, normale o difficile.</summary>
        Public Livello As String = "difficile"
    End Class

    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _stanze As New Dictionary(Of String, Stanza)()
    Private Shared ReadOnly _sessioni As New Dictionary(Of String, String)()   ' token -> nome utente
    Private Shared ReadOnly _nomiNoti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly NOMI_COMPUTER As String() = {"Aldo", "Bruna", "Carlo", "Dora", "Enzo", "Fulvia", "Gino", "Livia"}
    Private Shared ReadOnly _logger As New Logger()
    ''' <summary>L'ultimo errore interno di un motore (per le prove).</summary>
    Public Shared UltimoErrore As String = ""

    Private Sub New()
    End Sub

    ' ------------------------------------------------------------------ catalogo
    Private Class VarianteInfo
        Public Id As String
        Public Nome As String
        Public Descrizione As String
        Public Posti As Integer()
    End Class

    Private Class GiocoInfo
        Public Id As String
        Public Nome As String
        Public Posti As Integer()
        Public Descrizione As String
        Public Varianti As New List(Of VarianteInfo)()
    End Class

    Private Shared Function V(id As String, nome As String, descrizione As String, ParamArray posti As Integer()) As VarianteInfo
        Try
            Return New VarianteInfo With {.Id = id, .Nome = nome, .Descrizione = descrizione, .Posti = If(posti.Length = 0, Nothing, posti)}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.V", ex)
            Throw
        End Try
    End Function

    Private Shared ReadOnly CATALOGO As GiocoInfo() = {
        New GiocoInfo With {.Id = "burraco", .Nome = "Burraco", .Posti = {2, 4},
            .Descrizione = "Burraco italiano, uno contro uno o a coppie in quattro, a punti (di solito 2005), con minimo per aprire e pozzo congelato.",
            .Varianti = New List(Of VarianteInfo) From {V("classica", "Classica", "Partita tradizionale a punti a 2 o 4 giocatori.")}},
        New GiocoInfo With {.Id = "monopoli", .Nome = "Monopoli", .Posti = {2, 3, 4},
            .Descrizione = "Monopoli da due a quattro giocatori, con aste, ipoteche, costruzioni e scambi. Vince l'ultimo rimasto o chi ha più patrimonio.",
            .Varianti = New List(Of VarianteInfo) From {
                V("classica", "Classica", "Regole standard complete con aste, ipoteche e 4 case per albergo."),
                V("parcheggio_ricco", "Parcheggio Ricco", "Tutte le tasse e le uscite di prigione vanno nel piatto del Parcheggio Gratuito."),
                V("doppio_via", "Doppio al Via", "Fermarsi con tiro esatto sulla casella del Via raddoppia il premio a 400 euro."),
                V("completo", "Regole complete", "Combina Parcheggio Ricco e Doppio al Via."),
                V("veloce", "Partita Veloce", "2 proprietà iniziali a testa, 3 case per albergo, fine al primo fallimento.")}},
        New GiocoInfo With {.Id = "briscola", .Nome = "Briscola", .Posti = {2, 3, 4, 5},
            .Descrizione = "Briscola con mazzo da 40 carte, classica, al meglio di 3, a chiamata a 5, Marianna con accusi, scoperta o a 3.",
            .Varianti = New List(Of VarianteInfo) From {
                V("classica", "Classica", "Quaranta carte, tre carte a testa, vince chi supera 60 punti su 120.", 2, 4),
                V("al_meglio", "Al meglio delle tre", "Vince chi per primo si aggiudica due smazzate da 61 punti.", 2, 4),
                V("chiamata", "A chiamata", "In cinque, otto carte a testa. Chi apre chiama una carta e il suo seme diventa briscola: chi la possiede e il compagno segreto.", 5),
                V("marianna", "Marianna con accusi", "Re e cavallo dello stesso seme dopo una presa valgono 20 punti, 40 se e briscola. Si gioca a 250 punti.", 2, 4),
                V("scoperta", "Scoperta", "Le carte di tutti sono scoperte sul tavolo.", 2, 4),
                V("briscolino", "Briscolino", "In tre. Si toglie un due, tredici carte a testa, prese individuali.", 3)}},
        New GiocoInfo With {.Id = "scopa", .Nome = "Scopa", .Posti = {2, 4},
            .Descrizione = "Scopa con mazzo da 40 carte, classica, a 21 punti, scopone scientifico, asso pigliatutto, napola, re bello o a 15.",
            .Varianti = New List(Of VarianteInfo) From {
                V("classica", "Classica", "Si gioca fino a 11 punti, con carte, denari, settebello, primiera e scope.", 2, 4),
                V("punti_21", "A 21 punti", "La partita tradizionale lunga.", 2, 4),
                V("scopone", "Scopone scientifico", "In quattro a coppie. Nove carte a testa e quattro sul tavolo, distribuite tutte all inizio.", 4),
                V("asso_piglia_tutto", "Asso pigliatutto", "Giocando l asso si prende tutto il tavolo, se non c e gia un asso singolo da prendere.", 2, 4),
                V("napola", "Napola", "Una sequenza continua di denari dall asso vale punti, fino a 10 per il re.", 2, 4),
                V("rebello", "Re bello", "Chi prende il re di denari ha un punto in piu, oltre al settebello.", 2, 4),
                V("scopa_quindici", "Scopa a 15", "Si prende quando la carta giocata, sommata a quelle sul tavolo, fa 15.", 2, 4),
                V("completa", "Completa", "Napola, asso pigliatutto e re bello insieme.", 2, 4)}},
        New GiocoInfo With {.Id = "battaglia", .Nome = "Battaglia navale", .Posti = {2},
            .Descrizione = "Battaglia navale per due: ognuno posiziona la sua flotta sul casellario, poi si spara a turno dicendo una casella (colonna e riga) finché una flotta è affondata. Anche contro il computer.",
            .Varianti = New List(Of VarianteInfo) From {
                V("classica", "Classica", "Dieci navi (una da quattro caselle, due da tre, tre da due e quattro da una) che non si possono toccare. Si spara a turno."),
                V("cinque_navi", "Cinque navi", "Cinque navi da cinque, quattro, tre, tre e due caselle, che possono toccarsi. Si spara a turno."),
                V("a_raffica", "A raffica", "Come la classica, ma chi colpisce una nave spara ancora.")}}}

    Private Shared Function Info(gioco As String) As GiocoInfo
        Try
            Return CATALOGO.FirstOrDefault(Function(g) g.Id = gioco)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.Info", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ ingresso
    ''' <summary>Risponde a una richiesta come farebbe giochi.php. Solleva GiochiErrore con il codice HTTP come il server.</summary>
    Public Shared Function Chiama(metodo As String, azione As String, corpo As JObject, query As String, token As String) As JObject
        Try
            Return ChiamaInterno(metodo, azione, corpo, query, token)
        Catch ex As GiochiErrore
            Throw
        Catch ex As Exception
            ' un errore inatteso dentro un motore non deve fermare WinGabriel: si scrive nel registro e si dice a chi gioca cosa è successo
            UltimoErrore = ex.ToString()
            _logger.ScriviLog("MotoreLocale.Chiama (" & azione & "): " & ex.ToString())
            Throw New GiochiErrore("Il motore dei giochi ha avuto un problema e non ha completato l'operazione. Riprova.", 500)
        End Try
    End Function

    Private Shared Function ChiamaInterno(metodo As String, azione As String, corpo As JObject, query As String, token As String) As JObject
        Try
            SyncLock _blocco
                Dim q As Dictionary(Of String, String) = LeggiQuery(query)
                Select Case azione
                    Case "giochi" : Return AzioneGiochi()
                    Case "registra", "entra" : Return AzioneAccesso(azione, corpo)
                    Case "elimina_account" : Return New JObject()
                    Case "tavoli" : Return Attrezzi.J("tavoli", New JArray())
                    Case "stanze" : Return Attrezzi.J("stanze", New JArray())
                    Case "inviti" : Return Attrezzi.J("inviti", New JArray())
                    Case "bloccati" : Return Attrezzi.J("bloccati", New JArray())
                    Case "in_linea" : Return Attrezzi.J("nomi", New JArray())
                End Select
                Dim nome As String = NomeDaToken(token)
                Select Case azione
                    Case "mia" : Return AzioneMia(nome)
                    Case "crea" : Return AzioneCrea(nome, corpo)
                    Case "stato" : Return AzioneStato(q)
                    Case "mossa" : Return AzioneMossa(corpo)
                    Case "lascia" : Return AzioneLascia(corpo)
                    Case "chat" : Return AzioneChat(nome, corpo)
                    Case "entra_stanza", "avvia", "invita", "rifiuta_invito"
                        Throw New GiochiErrore("Senza internet si gioca solo contro il computer.", 409)
                    Case Else
                        Throw New GiochiErrore("Richiesta sconosciuta.", 404)
                End Select
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.ChiamaInterno", ex)
            Throw
        End Try
    End Function

    Private Shared Function LeggiQuery(query As String) As Dictionary(Of String, String)
        Try
            Dim r As New Dictionary(Of String, String)()
            If String.IsNullOrEmpty(query) Then Return r
            For Each parte As String In query.Split("&"c)
                Dim k As Integer = parte.IndexOf("="c)
                If k > 0 Then r(parte.Substring(0, k)) = Uri.UnescapeDataString(parte.Substring(k + 1))
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.LeggiQuery", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeDaToken(token As String) As String
        Try
            Dim nome As String = Nothing
            If String.IsNullOrEmpty(token) OrElse Not _sessioni.TryGetValue(token, nome) Then
                Throw New GiochiErrore("Per giocare devi prima entrare con il tuo nome utente.", 401)
            End If
            Return nome
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.NomeDaToken", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneGiochi() As JObject
        Try
            Dim r As New JArray()
            For Each g As GiocoInfo In CATALOGO
                Dim varianti As New JArray()
                For Each x As VarianteInfo In g.Varianti
                    varianti.Add(Attrezzi.J("id", x.Id, "nome", x.Nome, "descrizione", x.Descrizione, "posti", If(x.Posti IsNot Nothing, CType(Attrezzi.Arr(x.Posti), JToken), JValue.CreateNull())))
                Next
                r.Add(Attrezzi.J("id", g.Id, "nome", g.Nome, "posti", Attrezzi.Arr(g.Posti), "descrizione", g.Descrizione, "varianti", varianti))
            Next
            Return Attrezzi.J("giochi", r, "chat", "../chat/", "ha_password", True)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneGiochi", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneAccesso(azione As String, corpo As JObject) As JObject
        Try
            Dim nome As String = Convert.ToString(If(corpo IsNot Nothing, corpo("nome"), Nothing)).Trim()
            If nome.Length < 2 OrElse nome.Length > 20 Then Throw New GiochiErrore("Il nome utente deve avere da 2 a 20 caratteri: lettere, numeri, spazi, punto, trattino.", 400)
            If azione = "entra" AndAlso Not _nomiNoti.Contains(nome) Then Throw New GiochiErrore("Nome utente o password non corretti.", 401)
            If azione = "registra" AndAlso _nomiNoti.Contains(nome) Then
                ' al riavvio del programma le sessioni sono perse: lo stesso utente locale rientra
            End If
            _nomiNoti.Add(nome)
            Dim token As String = Guid.NewGuid().ToString("N")
            _sessioni(token) = nome
            Return Attrezzi.J("token", token, "nome", nome)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneAccesso", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneMia(nome As String) As JObject
        Try
            For Each s As Stanza In _stanze.Values
                If s.Stato <> "finita" AndAlso PostoDi(s, nome) >= 0 Then Return Attrezzi.J("id", s.Id, "gioco", s.Gioco, "stato", s.Stato)
            Next
            Return Attrezzi.J("id", Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneMia", ex)
            Throw
        End Try
    End Function

    Private Shared Function PostoDi(s As Stanza, nome As String) As Integer
        Try
            For i As Integer = 0 To s.Posti.Count - 1
                If s.Posti(i) IsNot Nothing AndAlso Not s.Posti(i).Bot AndAlso s.Posti(i).Nome = nome Then Return i
            Next
            Return -1
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.PostoDi", ex)
            Throw
        End Try
    End Function

    Private Shared Function Testo(corpo As JObject, chiave As String) As String
        Try
            If corpo Is Nothing OrElse corpo(chiave) Is Nothing OrElse corpo(chiave).Type = JTokenType.Null Then Return ""
            Return Convert.ToString(corpo(chiave))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.Testo", ex)
            Throw
        End Try
    End Function

    Private Shared Function Intero(corpo As JObject, chiave As String) As Integer
        Try
            Dim n As Integer = 0
            Integer.TryParse(Testo(corpo, chiave), n)
            Return n
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.Intero", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneCrea(nome As String, corpo As JObject) As JObject
        Try
            Dim gioco As String = Testo(corpo, "gioco")
            Dim gi As GiocoInfo = Info(gioco)
            If gi Is Nothing Then Throw New GiochiErrore("Gioco sconosciuto.", 400)
            Dim variante As String = Testo(corpo, "variante").Trim()
            Dim vi As VarianteInfo = gi.Varianti.FirstOrDefault(Function(x) x.Id = variante)
            If vi Is Nothing Then
                variante = "classica"
                vi = gi.Varianti.First(Function(x) x.Id = "classica")
            End If
            Dim postiAmmessi As Integer() = If(vi.Posti, gi.Posti)
            Dim posti As Integer = Intero(corpo, "posti")
            If Not postiAmmessi.Contains(posti) Then
                If postiAmmessi.Length = 1 Then
                    posti = postiAmmessi(0)
                Else
                    Throw New GiochiErrore("Numero di giocatori non valido per questa variante.", 400)
                End If
            End If
            If Not (corpo IsNot Nothing AndAlso corpo("computer") IsNot Nothing AndAlso corpo("computer").Type = JTokenType.Boolean AndAlso CBool(corpo("computer"))) Then
                Throw New GiochiErrore("Senza internet si gioca solo contro il computer.", 409)
            End If
            For Each s As Stanza In _stanze.Values
                If s.Stato <> "finita" AndAlso PostoDi(s, nome) >= 0 Then Throw New GiochiErrore("Hai già una partita in corso: prima lasciala.", 409)
            Next
            Dim stanza As New Stanza With {.Id = NuovoId(), .Gioco = gioco, .Variante = variante}
            Dim livello As String = Testo(corpo, "livello").Trim().ToLowerInvariant()
            stanza.Livello = If(livello = "facile" OrElse livello = "normale", livello, "difficile")
            Dim punti As Integer = Intero(corpo, "punti")
            stanza.Punti = If(gioco = "burraco", Math.Max(500, Math.Min(5005, If(punti > 0, punti, 2005))), 0)
            For i As Integer = 0 To posti - 1
                stanza.Posti.Add(Nothing)
            Next
            stanza.Posti(0) = New PostoStanza With {.Nome = nome, .Bot = False}
            _stanze(stanza.Id) = stanza
            Try
                AggiungiEventi(stanza, New List(Of EventoMotore) From {New EventoMotore(Nothing, nome & " ha creato il tavolo.")}, "sistema")
                AvviaPartita(stanza)
            Catch ex As Exception
                _stanze.Remove(stanza.Id)
                _logger.ScriviLog("MotoreLocale.AzioneCrea: " & ex.ToString())
                UltimoErrore = ex.ToString()
                Throw New GiochiErrore("Non riesco a preparare la partita.", 500)
            End Try
            stanza.Versione += 1
            Return Attrezzi.J("id", stanza.Id, "link", "")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneCrea", ex)
            Throw
        End Try
    End Function

    Private Shared Function NuovoId() As String
        Try
            Const lettere As String = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
            Dim id As String
            Do
                id = ""
                For i As Integer = 0 To 4
                    id &= lettere(Attrezzi.Casuale(0, lettere.Length))
                Next
            Loop While _stanze.ContainsKey(id)
            Return id
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.NuovoId", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ partita
    Private Shared Function NomeComputer(s As Stanza) As String
        Try
            Dim usati As List(Of String) = s.Posti.Select(Function(p) If(p Is Nothing, "", p.Nome)).ToList()
            For Each n As String In NOMI_COMPUTER
                If Not usati.Contains(n & " (computer)") Then Return n & " (computer)"
            Next
            Return "Computer " & usati.Count.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.NomeComputer", ex)
            Throw
        End Try
    End Function

    Private Shared Sub SegnaComputer(s As Stanza)
        Try
            If s.Motore IsNot Nothing Then s.Motore.ImpostaComputer(s.Posti.Select(Function(p) p IsNot Nothing AndAlso p.Bot).ToArray())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.SegnaComputer", ex)
            Throw
        End Try
    End Sub

    Private Shared Sub AvviaPartita(s As Stanza)
        Try
            For i As Integer = 0 To s.Posti.Count - 1
                If s.Posti(i) Is Nothing Then s.Posti(i) = New PostoStanza With {.Nome = NomeComputer(s), .Bot = True}
            Next
            Dim nomi As List(Of String) = s.Posti.Select(Function(p) p.Nome).ToList()
            Select Case s.Gioco
                Case "burraco" : s.Motore = New MotoreBurraco(nomi, s.Punti)
                Case "monopoli" : s.Motore = New MotoreMonopoli(nomi, s.Variante)
                Case "briscola" : s.Motore = New MotoreBriscola(nomi, s.Variante, 0)
                Case "battaglia" : s.Motore = New MotoreBattaglia(nomi, s.Variante)
                Case Else : s.Motore = New MotoreScopa(nomi, s.Variante, 0)
            End Select
            SegnaComputer(s)
            s.Stato = "in corso"
            AggiungiEventi(s, New List(Of EventoMotore) From {New EventoMotore(Nothing, "La partita comincia.")}, "sistema")
            AggiungiEventi(s, s.Motore.Inizio(), "gioco")
            FaiGiocareComputer(s)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AvviaPartita", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' La mossa del computer secondo il livello della partita (come mossa_computer di giochi.php): a difficile sempre la sua mossa migliore; a normale
    ''' una volta su cinque, a facile una volta su due, una mossa a caso tra quelle possibili (la battaglia navale spara su una casella a caso).
    ''' Dalle mosse a caso si tolgono quelle che non fanno andare avanti il gioco (annullare, ipotecare, vendere, costruire, proporre scambi).
    ''' </summary>
    Private Shared Function MossaComputer(s As Stanza, turno As Integer) As String
        Try
            ' alla battaglia navale un colpo a caso pesa meno che una carta a caso: il computer spara a caso più spesso (come PROBABILITA_CASO di battaglia.php)
            Dim battagliaNavale As Boolean = TypeOf s.Motore Is MotoreBattaglia
            Dim probabilita As Double = If(s.Livello = "facile", If(battagliaNavale, 0.85, 0.5), If(s.Livello = "normale", If(battagliaNavale, 0.45, 0.2), 0.0))
            If probabilita > 0 AndAlso Attrezzi.Casuale(0, 1000) < probabilita * 1000 Then
                Dim battaglia As MotoreBattaglia = TryCast(s.Motore, MotoreBattaglia)
                If battaglia IsNot Nothing Then
                    Dim m As String = battaglia.MossaCasuale(turno)
                    If m IsNot Nothing Then Return m
                Else
                    Dim escluse As String() = {"annulla", "ipoteca", "vendi", "costruisci", "riscatta", "scambio"}
                    Dim mosse As List(Of MossaMotore) = s.Motore.Mosse(turno).Where(Function(x) x.Parametro Is Nothing AndAlso Not escluse.Contains(x.Id.Split("|"c)(0))).ToList()
                    If mosse.Count > 0 Then Return mosse(Attrezzi.Casuale(0, mosse.Count)).Id
                End If
            End If
            Return s.Motore.Bot(turno)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.MossaComputer", ex)
            Throw
        End Try
    End Function

    Private Shared Sub FaiGiocareComputer(s As Stanza)
        Try
            SegnaComputer(s)
            Dim guardia As Integer = 0
            While s.Stato = "in corso" AndAlso Not s.Motore.Finita AndAlso guardia < 3000
                guardia += 1
                Dim turno As Integer = s.Motore.Attore
                If Not s.Posti(turno).Bot Then Exit While
                Try
                    Dim id As String = MossaComputer(s, turno)
                    AggiungiEventi(s, s.Motore.Applica(turno, id, False), "gioco")
                Catch ex As Exception
                    _logger.ScriviLog("MotoreLocale.FaiGiocareComputer: " & ex.ToString())
                    UltimoErrore = ex.ToString()
                    ' il computer non ha trovato una mossa valida: si prova la prima possibile; se non ce n'è la partita finisce, non resta bloccata
                    If Not ProvaPrimaMossa(s, turno) Then
                        AggiungiEventi(s, New List(Of EventoMotore) From {New EventoMotore(Nothing, "Il computer non riesce a continuare: la partita termina qui.")}, "sistema")
                        s.Stato = "finita"
                        Exit While
                    End If
                End Try
            End While
            ControllaFine(s)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.FaiGiocareComputer", ex)
            Throw
        End Try
    End Sub

    Private Shared Function ProvaPrimaMossa(s As Stanza, turno As Integer) As Boolean
        Try
            Dim mosse As List(Of MossaMotore) = s.Motore.Mosse(turno)
            If mosse.Count = 0 Then Return False
            AggiungiEventi(s, s.Motore.Applica(turno, mosse(0).Id, True), "gioco")
            Return True
        Catch ex As Exception
            _logger.ScriviLog("MotoreLocale.ProvaPrimaMossa: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub ControllaFine(s As Stanza)
        Try
            If s.Stato = "in corso" AndAlso s.Motore.Finita Then s.Stato = "finita"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.ControllaFine", ex)
            Throw
        End Try
    End Sub

    Private Shared Sub AggiungiEventi(s As Stanza, eventi As IEnumerable(Of EventoMotore), tipo As String)
        Try
            For Each e As EventoMotore In eventi
                s.UltimoEvento += 1
                Dim voce As JObject = Attrezzi.J("n", s.UltimoEvento, "tipo", tipo, "per", If(e.Per.HasValue, CObj(e.Per.Value), Nothing), "testo", e.Testo)
                Dim suono As String = If(e.Suono, SuonoEvento(s.Gioco, e.Testo, tipo, e.Per))
                If suono IsNot Nothing Then voce("suono") = suono
                s.Eventi.Add(voce)
            Next
            If s.Eventi.Count > 400 Then s.Eventi.RemoveRange(0, s.Eventi.Count - 400)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AggiungiEventi", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Ha(testo As String, schema As String) As Boolean
        Try
            Return Regex.IsMatch(testo, schema)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.Ha", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il suono tipico di un evento: gli stessi del server (suono_evento in giochi.php), così offline e online si sente lo stesso.</summary>
    Private Shared Function SuonoEvento(gioco As String, testo As String, tipo As String, per As Integer?) As String
        Try
            If tipo <> "gioco" OrElse gioco = "battaglia" Then Return Nothing   ' la battaglia navale dà i suoi suoni negli eventi
            If per.HasValue Then
                If Ha(testo, "^Le tue carte") Then Return "mescola"
                If Ha(testo, "^(Hai pescato|Nel pozzetto|Con gli scarti)") Then Return "pesca"
                Return Nothing
            End If
            If Ha(testo, "(La partita è finita|Vince la partita|La partita finisce in parità)") Then Return "fine"
            If gioco = "monopoli" Then
                If Ha(testo, "è eliminato") Then Return "eliminato"
                If Ha(testo, "tira i dadi") Then Return "dadi"
                If Ha(testo, "(va in prigione|resta in prigione)") Then Return "prigione"
                If Ha(testo, "(passa dal Via|si ferma sul Via)") Then Return "via"
                If Ha(testo, "(pesca un imprevisto|pesca una probabilità)") Then Return "imprevisto"
                If Ha(testo, "(Asta per|offre .* per|si ritira dall|vince l'asta)") Then Return "asta"
                If Ha(testo, " compra ") Then Return "compra"
                If Ha(testo, "scambio") Then Return "scambio"
                If Ha(testo, "(ipoteca|riscatta)") Then Return "ipoteca"
                If Ha(testo, "(costruisce|vende un edificio)") Then Return "casa"
                If Ha(testo, "(incassa|ritira)") Then Return "cassa"
                If Ha(testo, "(paga|di tassa|di affitto)") Then Return "soldi"
                If Ha(testo, "(arriva a|esce di prigione)") Then Return "passo"
                Return Nothing
            End If
            If Ha(testo, "^(Comincia|Si gioca|Si danno altre)") Then Return "mescola"
            If Ha(testo, "(Asso pigliatutto|Scopa!|chiude la mano)") Then Return "scopa"
            If Ha(testo, "(accusa Marianna|chiama |compagno|apre con)") Then Return "accusa"
            If Ha(testo, "(pesca dal mazzo)") Then Return "pesca"
            If Ha(testo, "(prende con|prende gli scarti|prende il pozzetto| e prende )") Then Return "presa"
            If Ha(testo, "( cala | attacca |annulla le combinazioni)") Then Return "calata"
            If Ha(testo, "( gioca | scarta )") Then Return "carta"
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.SuonoEvento", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeVariante(s As Stanza) As String
        Try
            Dim gi As GiocoInfo = Info(s.Gioco)
            Dim vi As VarianteInfo = gi.Varianti.FirstOrDefault(Function(x) x.Id = s.Variante)
            Return If(vi IsNot Nothing, vi.Nome, "Classica")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.NomeVariante", ex)
            Throw
        End Try
    End Function

    Private Shared Function VistaStato(s As Stanza, posto As Integer, conSituazione As Boolean, da As Integer) As JObject
        Try
            Dim eventi As New JArray()
            For Each e As JObject In s.Eventi
                Dim n As Integer = CInt(e("n"))
                Dim per As JToken = e("per")
                If n > da AndAlso (per.Type = JTokenType.Null OrElse CInt(per) = posto) Then eventi.Add(e)
            Next
            Dim giocatori As New JArray()
            For Each p As PostoStanza In s.Posti
                giocatori.Add(If(p Is Nothing, CType(JValue.CreateNull(), JToken), Attrezzi.J("nome", p.Nome, "computer", p.Bot)))
            Next
            Dim r As JObject = Attrezzi.J("id", s.Id, "gioco", s.Gioco, "nome_gioco", Info(s.Gioco).Nome, "variante", s.Variante, "variante_nome", NomeVariante(s),
                                          "versione", s.Versione, "stato", s.Stato, "posto", posto, "turno", Nothing, "giocatori", giocatori, "eventi", eventi,
                                          "mosse", New JArray(), "opzioni", New JArray(), "vista", Nothing, "ultimo_evento", s.UltimoEvento, "link", "")
            If s.Motore IsNot Nothing Then
                Dim m As IMotoreGioco = s.Motore
                r("turno") = m.Turno
                r("attore") = m.Attore
                r("vista") = m.Vista(posto)
                If s.Stato = "in corso" AndAlso posto >= 0 Then
                    Dim mosse As New JArray()
                    For Each x As MossaMotore In m.Mosse(posto)
                        Dim o As JObject = Attrezzi.J("id", x.Id, "testo", x.Testo)
                        If x.Parametro IsNot Nothing Then o("parametro") = x.Parametro
                        mosse.Add(o)
                    Next
                    r("mosse") = mosse
                    r("opzioni") = m.Opzioni(posto)
                End If
                If conSituazione AndAlso posto >= 0 Then r("situazione") = m.Situazione(posto)
            End If
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.VistaStato", ex)
            Throw
        End Try
    End Function

    Private Shared Function TrovaStanza(id As String) As Stanza
        Try
            Dim s As Stanza = Nothing
            If String.IsNullOrEmpty(id) OrElse Not _stanze.TryGetValue(id, s) Then Throw New GiochiErrore("Partita non trovata.", 404)
            Return s
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.TrovaStanza", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneStato(q As Dictionary(Of String, String)) As JObject
        Try
            Dim id As String = If(q.ContainsKey("id"), q("id"), "")
            Dim da As Integer = 0
            If q.ContainsKey("da") Then Integer.TryParse(q("da"), da)
            Dim s As Stanza = TrovaStanza(id)
            Return VistaStato(s, 0, q.ContainsKey("situazione") AndAlso q("situazione") <> "" AndAlso q("situazione") <> "0", da)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneStato", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneMossa(corpo As JObject) As JObject
        Try
            Dim s As Stanza = TrovaStanza(Testo(corpo, "id"))
            Dim mossa As String = Testo(corpo, "mossa")
            If s.Stato <> "in corso" Then Throw New GiochiErrore("La partita non è in corso.", 409)
            SegnaComputer(s)
            Dim eventi As List(Of EventoMotore)
            Try
                eventi = s.Motore.Applica(0, mossa, True)
            Catch ex As Exception When TypeOf ex Is MossaNonConsentita OrElse TypeOf ex Is FormatException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is IndexOutOfRangeException OrElse TypeOf ex Is ArgumentOutOfRangeException OrElse TypeOf ex Is OverflowException
                If Not (TypeOf ex Is MossaNonConsentita) Then _logger.ScriviLog("MotoreLocale.AzioneMossa (mossa non valida " & mossa & "): " & ex.ToString())
                Dim haMosse As Boolean = s.Motore.Mosse(0).Count > 0 OrElse s.Motore.Opzioni(0).Count > 0
                Throw New GiochiErrore(If(haMosse, "Questa mossa non è consentita.", "Non tocca a te."), 409)
            End Try
            AggiungiEventi(s, eventi, "gioco")
            ControllaFine(s)
            FaiGiocareComputer(s)
            s.Versione += 1
            Dim da As Integer = s.UltimoEvento
            If corpo("da") IsNot Nothing AndAlso corpo("da").Type <> JTokenType.Null Then da = CInt(corpo("da"))
            Return VistaStato(s, 0, False, da)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneMossa", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneLascia(corpo As JObject) As JObject
        Try
            Dim id As String = Testo(corpo, "id")
            If _stanze.ContainsKey(id) Then _stanze.Remove(id)
            Return New JObject()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneLascia", ex)
            Throw
        End Try
    End Function

    Private Shared Function AzioneChat(nome As String, corpo As JObject) As JObject
        Try
            Dim s As Stanza = TrovaStanza(Testo(corpo, "id"))
            Dim t As String = Regex.Replace(Testo(corpo, "testo"), "[\x00-\x1F\x7F]+", " ").Trim()
            If t = "" Then Throw New GiochiErrore("Il messaggio è vuoto.", 400)
            If t.Length > 200 Then t = t.Substring(0, 200)
            s.UltimoEvento += 1
            s.Eventi.Add(Attrezzi.J("n", s.UltimoEvento, "tipo", "chat", "per", Nothing, "testo", t, "da", nome))
            s.Versione += 1
            Return New JObject()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreLocale.AzioneChat", ex)
            Throw
        End Try
    End Function

End Class
