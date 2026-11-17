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
''' L'ambiente di gioco, disegnato a mano (SchermoGioco): non un menu, ma uno schermo con il tavolo, gli ultimi messaggi e in basso
''' cosa fare adesso ("Ora è il tuo turno, premi Invio per lanciare il dado"). A briscola e a scopa, quando tocca a te,
''' il menu delle carte si apre da solo dopo i messaggi: ogni voce dice la carta e il suo punteggio. Il tavolo è fatto
''' delle carte, quindi Escape non lo chiude: chiude la partita e chiede se si vuole interrompere. Non si salva niente. Negli altri casi una piccola lista
''' compare quando c'è da scegliere (una carta da scartare, una combinazione da calare).
'''  - I comandi rapidi sono lettere semplici e eseguono subito la mossa, senza portare sulla voce.
'''  - Alt apre le opzioni (controllo della cassa, ipoteche, costruzioni, scambi, messaggi, il link da condividere...): un pannello con un elenco, e per le
'''    proprietà una scelta multipla (Spazio sceglie, Invio conferma e il pannello si chiude).
'''  - Esc chiede se si vuole interrompere. Non si salva niente. Non c'è un elenco dei comandi: lo schermo dice sempre cosa fare adesso.
'''  - Nel Monopoli il turno finisce da solo dopo il tiro (lo chiude il server): per ipotecare, costruire o fare scambi si usa Alt prima di tirare.
'''  - WGMenu si usa solo per le domande: "Vuoi acquistare questo?" con Sì, No e Più informazioni (che legge tutto senza chiudere il menu).
''' Mentre lo schermo è aperto una sorveglianza guarda se arrivano messaggi o cambia la situazione e in quel caso lo schermo si ricompone.
''' </summary>
Partial Public Class MenuGiochi

    Private Class RisultatoScelta
        ''' <summary>"mossa", "aggiorna" (c'è uno stato nuovo) oppure "esci".</summary>
        Public Property Tipo As String = ""
        Public Property IdMossa As String = ""
        Public Property Stato As JObject
    End Class

    Private Class MossaGioco
        Public Id As String = ""
        Public Testo As String = ""
        Public Parametro As JObject
    End Class

    Private Class SessioneSchermo
        Public D As DatiPartita
        Public Stato As JObject
        Public Gioco As String = ""
        Public Schermo As SchermoGioco
        Public Esito As New TaskCompletionSource(Of RisultatoScelta)()
        Public Mosse As New List(Of MossaGioco)()
        ''' <summary>Gli id delle mosse della piccola lista di scelte in basso, quando è aperta.</summary>
        Public ScelteMosse As List(Of String)
        ''' <summary>"carte" o "prese" nella scopa, quando dalla presa si può tornare alla carta.</summary>
        Public LivelloScelta As String = ""
        Public TornaAlleCarte As Boolean
        Public Dialoghi As Integer
        ''' <summary>"acquisto" o "scambio" se c'è una domanda a cui si è risposto Esc e a cui si può tornare con Invio.</summary>
        Public DomandaInSospeso As String = ""
        Public Istruzione As String = ""
        ''' <summary>Quante volte si è premuto Shift (per saltare il messaggio che si legge).</summary>
        Public Salti As Integer = 0
        ''' <summary>Vero finché una lettura a messaggi è in corso.</summary>
        Public Sequenza As Boolean = False
        ''' <summary>L'ultima volta che Shift ha saltato una frase: due Shift ravvicinati contano come uno.</summary>
        Public UltimoSalto As DateTime = DateTime.MinValue
    End Class

    ' ------------------------------------------------------------------ piccoli aiuti sui dati del server
    Private Shared Function Txt(t As JToken) As String
        Try
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return ""
            Return Convert.ToString(t, CultureInfo.InvariantCulture)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Txt", ex)
            Throw
        End Try
    End Function

    Private Shared Function Num(t As JToken) As Integer
        Try
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return 0
            Try
                Return Convert.ToInt32(t, CultureInfo.InvariantCulture)
            Catch exLog As Exception
                RegistroErrori.Registra("MenuGiochi.Num", exLog)
                Return 0
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Num", ex)
            Throw
        End Try
    End Function

    Private Shared Function Vero(t As JToken) As Boolean
        Try
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return False
            Try
                Return Convert.ToBoolean(t, CultureInfo.InvariantCulture)
            Catch exLog As Exception
                RegistroErrori.Registra("MenuGiochi.Vero", exLog)
                Return False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Vero", ex)
            Throw
        End Try
    End Function

    Private Shared Function Vista(stato As JObject) As JObject
        Try
            Return If(TryCast(stato("vista"), JObject), New JObject())
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Vista", ex)
            Throw
        End Try
    End Function

    Private Shared Function Giocatori(stato As JObject) As JArray
        Try
            Return If(TryCast(stato("giocatori"), JArray), New JArray())
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Giocatori", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeGiocatore(stato As JObject, indice As Integer) As String
        Try
            Dim g As JArray = Giocatori(stato)
            If indice >= 0 AndAlso indice < g.Count AndAlso g(indice).Type <> JTokenType.Null Then Return Txt(g(indice)("nome"))
            Return ""
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NomeGiocatore", ex)
            Throw
        End Try
    End Function

    Private Shared Function Euro(n As Integer) As String
        Try
            Return n.ToString(CultureInfo.InvariantCulture) & " euro"
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Euro", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ sorveglianza
    Private Shared Function FirmaStato(stato As JObject) As String
        Try
            Dim sb As New StringBuilder()
            sb.Append(Txt(stato("stato"))).Append("|").Append(Txt(stato("turno"))).Append("|").Append(Txt(stato("attore"))).Append("|")
            Dim mosse As JArray = TryCast(stato("mosse"), JArray)
            If mosse IsNot Nothing Then
                For Each m As JObject In mosse
                    sb.Append(Txt(m("id"))).Append(";")
                Next
            End If
            sb.Append("|")
            Dim opzioni As JArray = TryCast(stato("opzioni"), JArray)
            If opzioni IsNot Nothing Then
                For Each g As JObject In opzioni
                    sb.Append(Txt(g("id"))).Append(":")
                    Dim voci As JArray = TryCast(g("voci"), JArray)
                    If voci IsNot Nothing Then
                        For Each v As JObject In voci
                            sb.Append(Txt(v("id"))).Append(",")
                        Next
                    End If
                    sb.Append(";")
                Next
            End If
            Dim vs As JObject = Vista(stato)
            If vs("scambio") IsNot Nothing AndAlso vs("scambio").Type <> JTokenType.Null Then sb.Append("|scambio")
            If vs("asta") IsNot Nothing AndAlso vs("asta").Type <> JTokenType.Null Then sb.Append("|asta")
            Return sb.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.FirmaStato", ex)
            Throw
        End Try
    End Function

    ''' <summary>Guarda in sottofondo se c'è qualcosa di nuovo (messaggi, un cambiamento delle mosse). Nothing se si ferma o non riesce.</summary>
    Private Async Function SorvegliaAsync(d As DatiPartita, firma As String, ct As CancellationToken) As Task(Of JObject)
        Try
            Do
                Dim s As JObject = Await _client.StatoAsync(d.Id, d.Da, 3, False, ct)
                If CType(s("eventi"), JArray).Count > 0 OrElse FirmaStato(s) <> firma Then Return s
            Loop
        Catch ex As OperationCanceledException
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.SorvegliaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' ------------------------------------------------------------------ lo schermo di gioco
    Private Async Function ScegliMossaAsync(d As DatiPartita, messaggi As String, primaVolta As Boolean, stato As JObject) As Task(Of RisultatoScelta)
        Try
            Dim s As New SessioneSchermo()
            s.D = d
            s.Stato = stato
            s.Gioco = Txt(stato("gioco"))
            For Each m As JObject In CType(stato("mosse"), JArray)
                s.Mosse.Add(New MossaGioco With {.Id = Txt(m("id")), .Testo = Txt(m("testo")), .Parametro = TryCast(m("parametro"), JObject)})
            Next

            Dim schermo As New SchermoGioco(TitoloSchermo(stato))
            s.Schermo = schermo
            schermo.TestoAnnuncio = Function() s.Istruzione
            Componi(s)

            AddHandler schermo.Tasto, Sub(mittente As Object, e As TastoGiocoEventArgs) GestisciTasto(s, e)
            AddHandler schermo.SceltaFatta, Sub(indice As Integer) ConfermaScelta(s, indice)
            AddHandler schermo.OpzioniRichieste, Sub() ApriOpzioni(s)
            AddHandler schermo.CasellaPremuta, Sub(riga As Integer, colonna As Integer) CasellaBattaglia(s, riga, colonna)
            AddHandler schermo.RuotaRichiesta, Sub() RuotaBattaglia(s)
            AddHandler schermo.OrientaRichiesta, Sub(orizzontale As Boolean) RuotaBattaglia(s, orizzontale)
            AddHandler schermo.CambiaMareRichiesto, Sub() CambiaMareBattaglia(s)
            AddHandler schermo.SaltaRichiesto, Sub()
                                                   ' Shift: si salta la frase che si sta leggendo (e solo durante la lettura dei messaggi). Ogni pressione
                                                   ' conta, anche se velocissima: la lettura tiene il conto delle frasi da saltare (vedi LeggiASegmenti)
                                                   If Not s.Sequenza Then Return
                                                   s.UltimoSalto = DateTime.UtcNow
                                                   System.Threading.Interlocked.Increment(s.Salti)
                                                   Try
                                                       sintesivocale.GetInstance().FermaVoce()
                                                   Catch exLog As Exception
                                                       RegistroErrori.Registra("MenuGiochi.ScegliMossaAsync", exLog)
                                                   End Try
                                                   ' la voce è stata fermata di proposito: chi aspetta la fine della lettura (per esempio la domanda
                                                   ' sull'acquisto) non deve più aspettare che ricominci
                                                   System.Threading.Interlocked.Exchange(UltimaLetturaTicks, 0)
                                               End Sub
            AddHandler schermo.EscPremuto, Sub()
                                               ' A briscola e a scopa il tavolo è il menu delle carte: Escape non lo chiude,
                                               ' chiude la partita. Dalla scelta della presa si torna alle carte.
                                               If s.ScelteMosse IsNot Nothing AndAlso s.LivelloScelta = "prese" AndAlso s.TornaAlleCarte Then
                                                   ApriCarteScopa(s)
                                               ElseIf s.ScelteMosse IsNot Nothing AndAlso Not EItaliano(s.Gioco) Then
                                                   s.ScelteMosse = Nothing
                                                   s.LivelloScelta = ""
                                                   s.TornaAlleCarte = False
                                                   schermo.ImpostaScelte(Nothing)
                                                   schermo.Istruzione = s.Istruzione
                                                   Leggi("Indietro. " & s.Istruzione)
                                               Else
                                                   s.Esito.TrySetResult(New RisultatoScelta With {.Tipo = "esci"})
                                               End If
                                           End Sub

            Dim cts As New CancellationTokenSource()
            Dim sorveglianza As Task(Of JObject) = SorvegliaAsync(d, FirmaStato(stato), cts.Token)
            Try
                schermo.Avvia()
                ' A voce: i messaggi nuovi (le mosse di chi ha giocato prima), poi cosa fare.
                ' A briscola e a scopa il menu delle carte si apre quando i messaggi sono finiti: prima si sente
                ' cosa è successo, poi si sceglie la carta con il punteggio.
                Dim sceglieCarta As Boolean = EItaliano(s.Gioco) AndAlso MosseCarte(s)
                ' Al Monopoli, se subito si apre una domanda (acquisto, scambio) o parte l'asta obbligata, l'istruzione la ripeterebbe:
                ' la dice la domanda stessa, dopo i messaggi.
                Dim conDomanda As Boolean = s.Gioco = "monopoli" AndAlso (Ha(s, "compra") OrElse Ha(s, "passa"))
                Dim parlato As New List(Of String)()
                If messaggi.Length > 0 Then parlato.Add(messaggi)
                If Not sceglieCarta AndAlso Not conDomanda Then parlato.Add(s.Istruzione)
                If parlato.Count > 0 Then
                    ' i messaggi nuovi uno alla volta (Shift salta quello che si sta leggendo), poi cosa fare
                    Dim segmenti As New List(Of String)()
                    If messaggi.Length > 0 Then
                        If d.Segmenti.Count > 0 Then
                            For Each t As String In d.Segmenti
                                segmenti.AddRange(DividiInFrasi(UnisciTesti(New String() {t})))
                            Next
                        Else
                            segmenti.AddRange(DividiInFrasi(UnisciTesti(New String() {messaggi})))
                        End If
                    End If
                    If Not sceglieCarta AndAlso Not conDomanda Then segmenti.AddRange(DividiInFrasi(UnisciTesti(New String() {s.Istruzione})))
                    LeggiASegmenti(s, segmenti)
                End If
                AvviaDomandaSeServe(s, messaggi.Length > 0)
                If sceglieCarta Then
                    If messaggi.Length > 0 Then Await AttendiFineLetturaAsync(s)
                    If Not s.Esito.Task.IsCompleted AndAlso Not sorveglianza.IsCompleted Then ApriCarte(s)
                End If

                Dim primo As Task = Await Task.WhenAny(s.Esito.Task, sorveglianza)
                If primo Is s.Esito.Task Then Return Await s.Esito.Task
                Dim nuovo As JObject = sorveglianza.Result
                If nuovo Is Nothing Then Return Await s.Esito.Task
                ' Se si sta rispondendo a una domanda o scegliendo da un pannello si aspetta di aver finito.
                Do While Not s.Esito.Task.IsCompleted AndAlso (s.Dialoghi > 0 OrElse schermo.PannelloAperto)
                    Await Task.WhenAny(s.Esito.Task, Task.Delay(300))
                Loop
                If s.Esito.Task.IsCompleted Then Return Await s.Esito.Task
                Return New RisultatoScelta With {.Tipo = "aggiorna", .Stato = nuovo}
            Finally
                cts.Cancel()
                Try
                    schermo.Chiudi()
                Catch exLog As Exception
                    RegistroErrori.Registra("MenuGiochi.ScegliMossaAsync", exLog)
                End Try
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliMossaAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Esegui(s As SessioneSchermo, idMossa As String)
        Try
            s.Esito.TrySetResult(New RisultatoScelta With {.Tipo = "mossa", .IdMossa = idMossa})
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Esegui", ex)
            Throw
        End Try
    End Sub

    Private Function Ha(s As SessioneSchermo, id As String) As Boolean
        Try
            Return s.Mosse.Any(Function(m) m.Id = id)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Ha", ex)
            Throw
        End Try
    End Function

    Private Function ConPrefisso(s As SessioneSchermo, prefisso As String) As List(Of MossaGioco)
        Try
            Return s.Mosse.Where(Function(m) m.Id.StartsWith(prefisso, StringComparison.Ordinal)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ConPrefisso", ex)
            Throw
        End Try
    End Function

    ''' <summary>A briscola e a scopa: c'è una carta da giocare, una carta da chiamare (briscola a chiamata) o una marianna da accusare.</summary>
    Private Function MosseCarte(s As SessioneSchermo) As Boolean
        Try
            Return ConPrefisso(s, "gioca|").Count > 0 OrElse ConPrefisso(s, "chiama|").Count > 0 OrElse ConPrefisso(s, "accuso|").Count > 0
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.MosseCarte", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una finestra di dialogo (domanda, campo) è aperta: lo schermo non si ricompone finché non finisce.</summary>
    Private Async Function InDialogoAsync(Of T)(s As SessioneSchermo, azione As Func(Of Task(Of T))) As Task(Of T)
        Try
            s.Dialoghi += 1
            Try
                Return Await azione()
            Finally
                s.Dialoghi -= 1
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.InDialogoAsync", ex)
            Throw
        End Try
    End Function

    Private Sub Informa(s As SessioneSchermo, testo As String)
        Try
            s.Schermo.Messaggi.Add(testo)
            s.Schermo.Aggiorna()
            Leggi(testo)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Informa", ex)
            Throw
        End Try
    End Sub

    ''' <summary>A briscola e a scopa, dopo un messaggio letto dalle opzioni, riapre le carte senza un altro Invio.</summary>
    Private Sub ProgrammaRitornoAlleCarte(s As SessioneSchermo)
        Try
            If Not EItaliano(s.Gioco) OrElse Not MosseCarte(s) Then Return
            Dim ritorno As Task = RitornoAlleCarteAsync(s)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ProgrammaRitornoAlleCarte", ex)
            Throw
        End Try
    End Sub

    Private Async Function RitornoAlleCarteAsync(s As SessioneSchermo) As Task
        Try
            Await AttendiFineLetturaAsync(s)
            TornaAlleCarte(s)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.RitornoAlleCarte: " & ex.Message)
        End Try
    End Function

    Private Sub TornaAlleCarte(s As SessioneSchermo)
        Try
            If Not EItaliano(s.Gioco) OrElse Not MosseCarte(s) Then Return
            If s.Esito.Task.IsCompleted OrElse s.Dialoghi > 0 OrElse s.Schermo Is Nothing OrElse s.Schermo.IsDisposed Then Return
            If s.Schermo.PannelloAperto OrElse s.ScelteMosse IsNot Nothing Then Return
            ApriCarte(s)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.TornaAlleCarte: " & ex.Message)
        End Try
    End Sub

    Private Shared Function TitoloSchermo(stato As JObject) As String
        Try
            Dim v As JObject = Vista(stato)
            ' la variante (Napola, Marianna con accusi, Scopone scientifico...) si dice nel titolo, tranne la classica
            Dim idVariante As String = Txt(stato("variante"))
            Dim suffisso As String = If(idVariante.Length > 0 AndAlso idVariante <> "classica" AndAlso Txt(stato("variante_nome")).Length > 0, " · " & Txt(stato("variante_nome")), "")
            If Txt(stato("gioco")) = "burraco" Then
                Dim p As JObject = TryCast(v("partita"), JObject)
                If p IsNot Nothing Then Return "Burraco" & suffisso & " · mano " & Txt(p("mano")) & " · si gioca fino a " & Txt(p("obiettivo")) & " punti"
                Return "Burraco" & suffisso
            End If
            If Txt(stato("gioco")) = "battaglia" Then Return "Battaglia navale" & suffisso
            If Txt(stato("gioco")) = "briscola" Then Return "Briscola" & suffisso
            If Txt(stato("gioco")) = "scopa" Then
                Dim p As JObject = TryCast(v("partita"), JObject)
                If p IsNot Nothing Then Return "Scopa" & suffisso & " · mano " & Txt(p("mano")) & " · si gioca fino a " & Txt(p("obiettivo")) & " punti"
                Return "Scopa" & suffisso
            End If
            Return "Monopoli" & suffisso & If(Num(v("giri")) > 0, " · giro " & Txt(v("giri")) & " di " & Txt(v("giri_massimi")), "")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TitoloSchermo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Briscola e scopa: le carte del mazzo italiano, giocate una per volta.</summary>
    Private Shared Function EItaliano(gioco As String) As Boolean
        Try
            Return gioco = "briscola" OrElse gioco = "scopa"
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.EItaliano", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ comporre lo schermo
    Private Sub Componi(s As SessioneSchermo)
        Try
            Dim schermo As SchermoGioco = s.Schermo
            schermo.Sezioni.Clear()
            schermo.Messaggi.Clear()
            If s.Gioco <> "monopoli" Then schermo.ImpostaTabellone(Nothing)
            For Each m As String In s.D.UltimiMessaggi.Skip(Math.Max(0, s.D.UltimiMessaggi.Count - 40))
                schermo.Messaggi.Add(m)
            Next
            If s.Gioco <> "battaglia" Then schermo.ImpostaCasellario(Nothing)
            If s.Gioco = "burraco" Then
                ComponiBurraco(s)
            ElseIf s.Gioco = "battaglia" Then
                ComponiBattaglia(s)
            ElseIf EItaliano(s.Gioco) Then
                ComponiItaliano(s)
            Else
                ComponiMonopoli(s)
            End If
            s.Istruzione = If(s.Gioco = "burraco", IstruzioneBurraco(s), If(s.Gioco = "battaglia", IstruzioneBattaglia(s), If(EItaliano(s.Gioco), IstruzioneItaliano(s), IstruzioneMonopoli(s))))
            schermo.Istruzione = s.Istruzione
            schermo.ImpostaScelte(Nothing)
            schermo.Aggiorna()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Componi", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Rango(rg As Integer) As String
        Try
            Select Case rg
                Case 1, 14 : Return "A"
                Case 11 : Return "F"
                Case 12 : Return "D"
                Case 13 : Return "R"
                Case Else : Return rg.ToString(CultureInfo.InvariantCulture)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Rango", ex)
            Throw
        End Try
    End Function

    Private Shared Function Carta(c As JToken) As CartaGioco
        Try
            Dim x As New CartaGioco()
            If Vero(c("jolly")) Then
                x.Testo = ChrW(&H2605)
                x.Jolly = True
                x.Immagine = "JK"
                Return x
            End If
            Dim seme As Integer = Num(c("seme"))
            Dim rg As Integer = Num(c("rango"))
            x.Immagine = If(rg = 1 OrElse rg = 14, "A", If(rg = 11, "J", If(rg = 12, "Q", If(rg = 13, "K", rg.ToString(CultureInfo.InvariantCulture))))) & "HDCS"(Math.Max(0, Math.Min(3, seme)))
            Dim simboli As String() = {ChrW(&H2665), ChrW(&H2666), ChrW(&H2663), ChrW(&H2660)}
            x.Testo = Rango(Num(c("rango"))) & simboli(Math.Max(0, Math.Min(3, seme)))
            ' mazzo italiano: il testo corto lo dà il server (per esempio "7 den"), perché i semi delle immagini sono quelli francesi
            If c("breve") IsNot Nothing AndAlso c("breve").Type = JTokenType.String Then x.Testo = Txt(c("breve"))
            x.Rosso = seme = 0 OrElse seme = 1
            ' mazzo italiano: il server dà il nome dell'immagine (7_denari, F_bastoni, e per le carte speciali 7_denari_settebello,
            ' R_denari_rebello, A_coppe_pigliatutto); senza, restano i nomi del mazzo francese del burraco (7H, KS...)
            Dim immagineServer As String = Txt(c("immagine"))
            If immagineServer.Length > 0 Then x.Immagine = immagineServer
            Return x
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Carta", ex)
            Throw
        End Try
    End Function

    ' ---- burraco
    Private Sub ComponiBurraco(s As SessioneSchermo)
        Try
            Dim stato As JObject = s.Stato
            Dim v As JObject = Vista(stato)
            Dim posto As Integer = Num(stato("posto"))
            Dim schermo As SchermoGioco = s.Schermo
            Dim partita As JObject = TryCast(v("partita"), JObject)

            Dim sPartita As New SezioneGioco With {.Titolo = "Partita"}
            If partita IsNot Nothing Then
                Dim totali As JArray = CType(partita("totali"), JArray)
                Dim squadre As JArray = CType(v("squadre"), JArray)
                Dim righe As New List(Of String)()
                For sq As Integer = 0 To squadre.Count - 1
                    righe.Add(NomiSquadra(stato, CType(squadre(sq)("membri"), JArray)) & " " & Txt(totali(sq)))
                Next
                sPartita.Aggiungi("Totali: " & String.Join(" · ", righe), True)
            End If
            sPartita.Aggiungi("Tocca a " & NomeGiocatore(stato, Num(v("turno"))) & If(Num(v("turno")) = posto, " (tu)", ""))
            schermo.Sezioni.Add(sPartita)

            Dim sMazzo As New SezioneGioco With {.Titolo = "Mazzo e scarti"}
            Dim rigaMazzo As RigaGioco = sMazzo.Aggiungi("Mazzo: " & Txt(v("mazzo")) & " carte · pozzetti ancora da prendere: " & Txt(v("pozzetti_rimasti")))
            rigaMazzo.Carte.Add(New CartaGioco With {.Dorso = True, .Immagine = "dorso"})
            Dim scarti As JObject = TryCast(v("scarti"), JObject)
            If scarti IsNot Nothing Then
                Dim rigaScarti As RigaGioco = sMazzo.Aggiungi("Scarti: " & Txt(scarti("n")) & " carte" & If(Vero(scarti("congelato")), ", pozzo congelato", ""))
                If scarti("cima") IsNot Nothing AndAlso scarti("cima").Type <> JTokenType.Null Then rigaScarti.Carte.Add(Carta(scarti("cima")))
            End If
            schermo.Sezioni.Add(sMazzo)

            Dim mano As JArray = If(TryCast(v("mano"), JArray), New JArray())
            Dim sMano As New SezioneGioco With {.Titolo = "La tua mano, " & mano.Count.ToString() & " carte"}
            Dim rigaMano As New RigaGioco()
            For Each c As JToken In mano
                rigaMano.Carte.Add(Carta(c))
            Next
            sMano.Righe.Add(rigaMano)
            schermo.Sezioni.Add(sMano)

            Dim mia As Integer = Num(v("mia_squadra"))
            Dim sq2 As JArray = If(TryCast(v("squadre"), JArray), New JArray())
            For i As Integer = 0 To sq2.Count - 1
                Dim q As JObject = CType(sq2(i), JObject)
                Dim sez As New SezioneGioco With {.Titolo = If(i = mia, "Tavolo: la tua squadra", "Tavolo: avversari") & ", " & NomiSquadra(stato, CType(q("membri"), JArray))}
                sez.Aggiungi(If(Vero(q("pozzetto_preso")), "Pozzetto preso", "Pozzetto non ancora preso") & " · " &
                             If(Vero(q("aperta")), "ha aperto", "non ha ancora aperto, servono " & Txt(q("minimo")) & " punti"))
                For Each c As JObject In CType(q("combinazioni"), JArray)
                    Dim riga As RigaGioco = sez.Aggiungi(Txt(c("descrizione")))
                    riga.Piccola = True
                    For Each k As JToken In CType(c("carte"), JArray)
                        riga.Carte.Add(Carta(k))
                    Next
                Next
                If CType(q("combinazioni"), JArray).Count = 0 Then sez.Aggiungi("Nessuna combinazione")
                schermo.Sezioni.Add(sez)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ComponiBurraco", ex)
            Throw
        End Try
    End Sub

    ' ---- briscola e scopa
    Private Sub ComponiItaliano(s As SessioneSchermo)
        Try
            Dim stato As JObject = s.Stato
            Dim v As JObject = Vista(stato)
            Dim posto As Integer = Num(stato("posto"))
            Dim schermo As SchermoGioco = s.Schermo

            Dim sPartita As New SezioneGioco With {.Titolo = "Partita"}
            For Each r As JToken In If(TryCast(v("riepilogo"), JArray), New JArray())
                sPartita.Aggiungi(Txt(r))
            Next
            Dim variante As String = Txt(v("variante"))
            If Txt(v("tipo")) = "briscola" AndAlso (variante = "al_meglio" OrElse variante = "marianna") Then
                Dim squadrePartita As JArray = TryCast(v("squadre"), JArray)
                Dim totali As JArray = TryCast(v(If(variante = "al_meglio", "totali_smazzate", "totali_punti")), JArray)
                If squadrePartita IsNot Nothing AndAlso totali IsNot Nothing Then
                    Dim parti As New List(Of String)()
                    For k As Integer = 0 To Math.Min(squadrePartita.Count, totali.Count) - 1
                        parti.Add(Txt(squadrePartita(k)("nome")) & " " & Txt(totali(k)))
                    Next
                    sPartita.Aggiungi("Smazzata " & Txt(v("smazzata")) & If(variante = "al_meglio", ", smazzate vinte: ", ", punti totali: ") & String.Join(", ", parti))
                End If
            End If
            If Not Vero(v("finita")) Then sPartita.Aggiungi("Tocca a " & NomeGiocatore(stato, Num(v("turno"))) & If(Num(v("turno")) = posto, " (tu)", ""), True)
            If Vero(v("finita")) AndAlso Txt(v("motivo")).Length > 0 Then sPartita.Aggiungi(Txt(v("motivo")), True)
            schermo.Sezioni.Add(sPartita)

            Dim nomi As JArray = If(TryCast(v("nomi"), JArray), New JArray())
            Dim inMano As JArray = If(TryCast(v("carte_in_mano"), JArray), New JArray())
            Dim maniScoperte As JArray = TryCast(v("mani_scoperte"), JArray)
            If nomi.Count > 1 Then
                Dim sAltri As New SezioneGioco With {.Titolo = "Gli altri giocatori"}
                For i As Integer = 0 To nomi.Count - 1
                    If i = posto Then Continue For
                    Dim nCarte As Integer = If(i < inMano.Count, Num(inMano(i)), 0)
                    ' briscola scoperta: le carte degli altri sono sul tavolo, a faccia in su
                    Dim manoAperta As JArray = Nothing
                    If maniScoperte IsNot Nothing AndAlso i < maniScoperte.Count Then manoAperta = TryCast(maniScoperte(i), JArray)
                    If manoAperta IsNot Nothing AndAlso manoAperta.Count > 0 Then
                        Dim rigaAperta As RigaGioco = sAltri.Aggiungi(Txt(nomi(i)) & ", carte scoperte: " & NomiCarte(manoAperta))
                        rigaAperta.Piccola = True
                        For Each cAperta As JToken In manoAperta
                            rigaAperta.Carte.Add(Carta(cAperta))
                        Next
                        Continue For
                    End If
                    Dim rigaAltro As RigaGioco = sAltri.Aggiungi(Txt(nomi(i)) & ": " & nCarte.ToString() & If(nCarte = 1, " carta", " carte"))
                    rigaAltro.Piccola = True
                    For k As Integer = 1 To Math.Min(nCarte, 3)
                        rigaAltro.Carte.Add(New CartaGioco With {.Dorso = True, .Immagine = "dorso"})
                    Next
                Next
                schermo.Sezioni.Add(sAltri)
            End If

            Dim sTavolo As New SezioneGioco With {.Titolo = "Tavolo"}
            ' alla briscola a chiamata e allo scopone le carte sono tutte distribuite: il mazzo non c'è
            If variante <> "chiamata" AndAlso variante <> "scopone" Then
                Dim rigaMazzo As RigaGioco = sTavolo.Aggiungi("Mazzo: " & Txt(v("mazzo")) & " carte")
                rigaMazzo.Carte.Add(New CartaGioco With {.Dorso = True, .Immagine = "dorso"})
            End If
            If Txt(v("tipo")) = "briscola" AndAlso v("briscola") IsNot Nothing AndAlso v("briscola").Type <> JTokenType.Null Then
                Dim testoBriscola As String
                If variante = "chiamata" Then
                    testoBriscola = "Carta chiamata: " & Txt(v("briscola")("nome")) & ", il seme " & Txt(v("seme_briscola")) & " è briscola. " & If(Vero(v("socio_scoperto")), "Il compagno si è scoperto.", "Il compagno è ancora segreto.")
                Else
                    Dim dove As String = If(Num(v("mazzo")) = 0, "già pescata", "scoperta in fondo al mazzo")
                    testoBriscola = "Briscola: " & Txt(v("briscola")("nome")) & ", seme " & Txt(v("seme_briscola")) & ", " & dove
                End If
                Dim rigaBriscola As RigaGioco = sTavolo.Aggiungi(testoBriscola)
                rigaBriscola.Carte.Add(Carta(v("briscola")))
            ElseIf Txt(v("tipo")) = "briscola" AndAlso Vero(v("fase_chiamata")) Then
                sTavolo.Aggiungi("Si sta chiamando la carta: il suo seme sarà la briscola.")
            End If
            Dim sulTavolo As JArray = If(TryCast(v("tavolo_carte"), JArray), New JArray())
            Dim descrizione As String
            If sulTavolo.Count = 0 Then
                descrizione = If(Txt(v("tipo")) = "scopa", "Il tavolo è vuoto.", "Sul tavolo non ci sono carte.")
            Else
                Dim pezzi As New List(Of String)()
                For Each c As JToken In sulTavolo
                    Dim chi As String = Txt(c("da"))
                    If chi.Length > 0 Then
                        pezzi.Add(chi & " ha giocato " & Txt(c("nome")))
                    Else
                        pezzi.Add(Txt(c("nome")))
                    End If
                Next
                descrizione = "Sul tavolo, " & sulTavolo.Count.ToString() & If(sulTavolo.Count = 1, " carta: ", " carte: ") & String.Join(", ", pezzi)
            End If
            Dim rigaTavolo As RigaGioco = sTavolo.Aggiungi(descrizione)
            For Each c As JToken In sulTavolo
                rigaTavolo.Carte.Add(Carta(c))
            Next
            If Txt(v("tipo")) = "scopa" AndAlso Txt(v("ultima")).Length > 0 Then
                sTavolo.Aggiungi("Ultima giocata: " & Txt(v("ultima")))
            ElseIf Txt(v("tipo")) = "briscola" AndAlso v("ultima_presa") IsNot Nothing AndAlso v("ultima_presa").Type = JTokenType.Object Then
                Dim u As JObject = CType(v("ultima_presa"), JObject)
                Dim puntiPresa As Integer = Num(u("punti"))
                sTavolo.Aggiungi("Ultima presa: " & NomeGiocatore(stato, Num(u("vincitore"))) & " con " & puntiPresa.ToString() & If(puntiPresa = 1, " punto", " punti"))
                Dim cartePresa As JArray = TryCast(u("carte"), JArray)
                If cartePresa IsNot Nothing AndAlso cartePresa.Count > 0 Then
                    Dim rigaPresa As RigaGioco = sTavolo.Aggiungi(NomiCarte(cartePresa))
                    rigaPresa.Piccola = True
                    For Each cPresa As JToken In cartePresa
                        rigaPresa.Carte.Add(Carta(cPresa))
                    Next
                End If
            End If
            schermo.Sezioni.Add(sTavolo)

            Dim mano As JArray = If(TryCast(v("mano"), JArray), New JArray())
            Dim sMano As New SezioneGioco With {.Titolo = "La tua mano, " & mano.Count.ToString() & " carte"}
            Dim rigaMano As RigaGioco = sMano.Aggiungi(If(mano.Count = 0, "Non hai carte.", NomiCarte(mano)))
            For Each c As JToken In mano
                rigaMano.Carte.Add(Carta(c))
            Next
            schermo.Sezioni.Add(sMano)

            Dim punteggi As JArray = TryCast(v("punteggi"), JArray)
            Dim squadreVista As JArray = TryCast(v("squadre"), JArray)
            If punteggi IsNot Nothing AndAlso squadreVista IsNot Nothing AndAlso punteggi.Count > 0 Then
                Dim sPunti As New SezioneGioco With {.Titolo = If(Vero(v("finita")), "Punti", "Punti della mano precedente")}
                For i As Integer = 0 To Math.Min(punteggi.Count, squadreVista.Count) - 1
                    Dim p As JObject = TryCast(punteggi(i), JObject)
                    Dim q As JObject = TryCast(squadreVista(i), JObject)
                    If p Is Nothing OrElse q Is Nothing Then Continue For
                    Dim nomeSq As String = NomiSquadra(stato, If(TryCast(q("membri"), JArray), New JArray()))
                    If Txt(v("tipo")) = "scopa" Then
                        ' re bello e napola ci sono solo nelle varianti che li usano
                        Dim extra As String = ""
                        If Num(p("re_bello")) > 0 Then extra &= ", re bello " & Txt(p("re_bello"))
                        If Num(p("napola_punti")) > 0 Then extra &= ", napola " & Txt(p("napola_punti"))
                        sPunti.Aggiungi(nomeSq & ": carte " & Txt(p("carte")) & ", denari " & Txt(p("denari")) & ", sette bello " & Txt(p("sette_bello")) & extra & ", primiera " & Txt(p("primiera_punto")) & ", scope " & Txt(p("scope")) & ", totale " & Txt(p("totale")))
                    Else
                        sPunti.Aggiungi(nomeSq & ": " & Txt(p("punti")) & " punti, " & Txt(p("carte")) & " carte prese")
                    End If
                Next
                schermo.Sezioni.Add(sPunti)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ComponiItaliano", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Le istruzioni di funzionamento (quali tasti usare) si dicono solo se si sono accese dalle impostazioni (Giochi e chat, Istruzioni dei giochi).
    ''' Spente, lo schermo dice soltanto come stanno le cose.
    ''' </summary>
    Friend Shared Function IstruzioniAttive() As Boolean
        Try
            Dim c As New WGConfig()
            If Not c.EsisteId("GiochiIstruzioni") Then Return False
            Return String.Equals(If(c.LeggiValore("GiochiIstruzioni"), "").Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.IstruzioniAttive", exLog)
            Return False
        End Try
    End Function

    Private Shared Function Istr(breve As String, lunga As String) As String
        Try
            Return If(IstruzioniAttive(), lunga, breve)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.Istr", ex)
            Throw
        End Try
    End Function

    Private Function IstruzioneItaliano(s As SessioneSchermo) As String
        Try
            If ConPrefisso(s, "chiama|").Count > 0 Then Return Istr("Tocca a te: scegli la carta da chiamare.", "Tocca a te: scegli la carta da chiamare. Il suo seme diventa la briscola e chi la possiede è il tuo compagno segreto. Esc chiude la partita, G riapre le carte.")
            If Not MosseCarte(s) Then Return Istr("Non è il tuo turno.", "Non è il tuo turno. M legge la tua mano, T il tavolo, Q i punti.")
            If s.Gioco = "scopa" Then Return Istr("Tocca a te.", "Tocca a te. Ogni carta dice quanto vale; se puoi prendere in più modi scegli anche la presa. Esc chiude la partita, G riapre le carte.")
            If ConPrefisso(s, "accuso|").Count > 0 Then Return Istr("Tocca a te: puoi accusare la marianna.", "Tocca a te: puoi accusare la marianna oppure giocare una carta. Ogni carta dice i punti. Esc chiude la partita, G riapre le carte.")
            Return Istr("Tocca a te.", "Tocca a te. Ogni carta dice i punti. Esc chiude la partita, G riapre le carte.")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IstruzioneItaliano", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomiSquadra(stato As JObject, membri As JArray) As String
        Try
            Dim nomi As New List(Of String)()
            For Each m As JToken In membri
                nomi.Add(NomeGiocatore(stato, Num(m)))
            Next
            Return String.Join(" e ", nomi)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NomiSquadra", ex)
            Throw
        End Try
    End Function

    Private Function IstruzioneBurraco(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            If Ha(s, "pesca") OrElse ConPrefisso(s, "scarti|").Count > 0 Then
                Dim lunga As String = "Tocca a te. P o Invio pesca dal mazzo"
                If ConPrefisso(s, "scarti|").Count > 0 Then lunga &= ", S prende gli scarti"
                Return Istr("Tocca a te: pesca.", lunga & ".")
            End If
            Dim parti As New List(Of String)()
            If ConPrefisso(s, "cala|").Count > 0 Then parti.Add("C cala")
            If ConPrefisso(s, "att|").Count > 0 Then parti.Add("A attacca una carta")
            If Ha(s, "annulla") Then parti.Add("X annulla le calate provvisorie")
            If ConPrefisso(s, "scarta|").Count > 0 Then parti.Add("D scarta una carta")
            Dim info As String = ""
            If Ha(s, "annulla") Then
                Dim mia As Integer = Num(v("mia_squadra"))
                Dim q As JObject = CType(CType(v("squadre"), JArray)(mia), JObject)
                info = "Per aprire servono " & Txt(q("minimo")) & " punti e ne hai calati " & Txt(q("punti_calati")) & ". "
            End If
            Return Istr(If(info.Length > 0, info.Trim(), "Tocca a te."), info & String.Join(", ", parti) & ".")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IstruzioneBurraco", ex)
            Throw
        End Try
    End Function

    ' ---- monopoli
    Private Shared ReadOnly ColoriPedine As Integer() = {&H2020D0, &HD07020, &H20A030, &H20C0E0, &HC040C0, &H808080}

    Private Shared Function ColoreCasella(c As JObject) As Integer
        Try
            Select Case Txt(c("colore"))
                Case "marrone" : Return &H365495
                Case "azzurro" : Return &HFAE0AA
                Case "rosa" : Return &H963AD9
                Case "arancione" : Return &H1D94F7
                Case "rosso" : Return &H241BED
                Case "giallo" : Return &HFEF2
                Case "verde" : Return &H5AB21F
                Case "blu" : Return &HBB7200
            End Select
            Select Case Txt(c("tipo"))
                Case "stazione" : Return &H303030
                Case "societa" : Return &H909090
            End Select
            Return -1
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ColoreCasella", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il nome dell'icona (monopoli.icona-NOME.png) delle caselle che non hanno una fascia di colore; vuoto per le altre.</summary>
    Private Shared Function IconaCasella(c As JObject) As String
        Try
            Select Case Txt(c("tipo"))
                Case "societa" : Return If(Txt(c("nome")).IndexOf("elettric", StringComparison.OrdinalIgnoreCase) >= 0, "societa-elettrica", "societa-acqua")
                Case "via", "prigione", "parcheggio", "vai_prigione", "stazione", "probabilita", "imprevisti", "tassa" : Return Txt(c("tipo"))
            End Select
            Return ""
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IconaCasella", ex)
            Throw
        End Try
    End Function

    Private Shared Function CasellaDi(stato As JObject, posizione As Integer) As JObject
        Try
            Dim caselle As JArray = If(TryCast(Vista(stato)("caselle"), JArray), New JArray())
            If posizione >= 0 AndAlso posizione < caselle.Count Then Return CType(caselle(posizione), JObject)
            Return New JObject()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CasellaDi", ex)
            Throw
        End Try
    End Function

    Private Shared Function GiocatoreVista(v As JObject, posto As Integer) As JObject
        Try
            Dim g As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
            If posto >= 0 AndAlso posto < g.Count Then Return CType(g(posto), JObject)
            Return New JObject()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.GiocatoreVista", ex)
            Throw
        End Try
    End Function

    ''' <summary>I dati per disegnare il tabellone: caselle, pedine (nella posizione in cui sono ora) e ultimo tiro dei dadi.</summary>
    Private Shared Function CostruisciTabellone(stato As JObject) As TabelloneGioco
        Try
            Dim v As JObject = Vista(stato)
            Dim caselle As JArray = If(TryCast(v("caselle"), JArray), New JArray())
            If caselle.Count <> 40 Then Return Nothing
            Dim tab As New TabelloneGioco()
            For Each token As JToken In caselle
                Dim c As JObject = TryCast(token, JObject)
                Dim x As New CasellaTabellone()
                If c IsNot Nothing Then
                    x.Nome = Txt(c("nome"))
                    x.Icona = IconaCasella(c)
                    If Txt(c("tipo")) = "terreno" Then x.Colore = ColoreCasella(c)
                    If c("proprietario") IsNot Nothing AndAlso c("proprietario").Type <> JTokenType.Null Then x.Proprietario = Num(c("proprietario"))
                    x.Edifici = Num(c("case"))
                    x.Ipotecata = Vero(c("ipoteca"))
                End If
                tab.Caselle.Add(x)
            Next
            Dim giocatori As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
            Dim attore As Integer = Num(v("attore"))
            For i As Integer = 0 To giocatori.Count - 1
                Dim g As JObject = CType(giocatori(i), JObject)
                tab.Pedine.Add(New PedinaTabellone With {
                    .Posizione = Math.Max(0, Math.Min(39, Num(g("posizione")))),
                    .Colore = ColoriPedine(i Mod ColoriPedine.Length),
                    .Fuori = Vero(g("fuori")),
                    .Attiva = (i = attore)})
            Next
            Dim dadi As JToken = v("dadi")
            If dadi IsNot Nothing AndAlso dadi.Type = JTokenType.Array AndAlso CType(dadi, JArray).Count = 2 Then
                tab.Dado1 = Math.Max(0, Math.Min(6, Num(dadi(0))))
                tab.Dado2 = Math.Max(0, Math.Min(6, Num(dadi(1))))
            End If
            Return tab
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.CostruisciTabellone", ex)
            Throw
        End Try
    End Function

    Private Sub ComponiMonopoli(s As SessioneSchermo)
        Try
            Dim stato As JObject = s.Stato
            Dim v As JObject = Vista(stato)
            Dim posto As Integer = Num(stato("posto"))
            Dim schermo As SchermoGioco = s.Schermo
            Dim giocatori As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
            Dim attore As Integer = Num(v("attore"))
            schermo.ImpostaTabellone(CostruisciTabellone(stato))

            If posto >= 0 AndAlso posto < giocatori.Count Then
                Dim c As JObject = CasellaDi(stato, Num(giocatori(posto)("posizione")))
                Dim sDove As New SezioneGioco With {.Titolo = "Sei su"}
                sDove.Aggiungi(DescriviCasella(stato, c), True, ColoreCasella(c))
                Dim dadi As JToken = v("dadi")
                If dadi IsNot Nothing AndAlso dadi.Type = JTokenType.Array AndAlso CType(dadi, JArray).Count = 2 Then
                    sDove.Aggiungi("Ultimo tiro: " & Txt(dadi(0)) & " e " & Txt(dadi(1)))
                End If
                schermo.Sezioni.Add(sDove)
            End If

            Dim idVarianteMonopoli As String = Txt(v("variante"))
            Dim piatto As Integer = Num(v("parcheggio_piatto"))
            If (idVarianteMonopoli.Length > 0 AndAlso idVarianteMonopoli <> "classica") OrElse piatto > 0 Then
                Dim sInfo As New SezioneGioco With {.Titolo = "Partita"}
                If idVarianteMonopoli.Length > 0 AndAlso idVarianteMonopoli <> "classica" Then sInfo.Aggiungi("Variante: " & If(Txt(v("variante_nome")).Length > 0, Txt(v("variante_nome")), idVarianteMonopoli))
                If piatto > 0 Then sInfo.Aggiungi("Nel Parcheggio gratuito ci sono " & Euro(piatto) & ".")
                schermo.Sezioni.Add(sInfo)
            End If

            If v("asta") IsNot Nothing AndAlso v("asta").Type <> JTokenType.Null Then
                Dim a As JObject = CType(v("asta"), JObject)
                Dim sAsta As New SezioneGioco With {.Titolo = "Asta"}
                sAsta.Aggiungi(Txt(a("nome")) & ": " & If(Num(a("offerta")) > 0, "offerta più alta " & Euro(Num(a("offerta"))) & " di " & NomeGiocatore(stato, Num(a("leader"))), "nessuna offerta") &
                               ". Tocca a " & NomeGiocatore(stato, Num(a("attore"))), True, ColoreCasella(CasellaDi(stato, Num(a("casella")))))
                schermo.Sezioni.Add(sAsta)
            End If
            If v("scambio") IsNot Nothing AndAlso v("scambio").Type <> JTokenType.Null Then
                Dim sScambio As New SezioneGioco With {.Titolo = "Scambio proposto"}
                sScambio.Aggiungi(Txt(v("scambio")("testo")), True)
                schermo.Sezioni.Add(sScambio)
            End If

            Dim sGiocatori As New SezioneGioco With {.Titolo = "Giocatori"}
            For i As Integer = 0 To giocatori.Count - 1
                Dim g As JObject = CType(giocatori(i), JObject)
                Dim t As String = NomeGiocatore(stato, i) & If(i = posto, " (tu)", "") & ": " & Euro(Num(g("soldi")))
                If Vero(g("fuori")) Then
                    t &= ", fuori dal gioco"
                Else
                    t &= ", su " & Txt(CasellaDi(stato, Num(g("posizione")))("nome"))
                    If Vero(g("prigione")) Then t &= ", in prigione"
                    If Num(g("carte_esci")) > 0 Then t &= ", ha una carta per uscire di prigione"
                End If
                sGiocatori.Aggiungi(t, i = attore, ColoriPedine(i Mod ColoriPedine.Length))
            Next
            schermo.Sezioni.Add(sGiocatori)

            Dim sProp As New SezioneGioco With {.Titolo = "Le tue proprietà"}
            Dim gruppi As New List(Of String)()
            Dim perGruppo As New Dictionary(Of String, List(Of String))()
            Dim coloreGruppo As New Dictionary(Of String, Integer)()
            For Each c As JObject In If(TryCast(v("caselle"), JArray), New JArray())
                If c("proprietario") Is Nothing OrElse c("proprietario").Type = JTokenType.Null OrElse Num(c("proprietario")) <> posto Then Continue For
                Dim chiave As String = If(Txt(c("colore")).Length > 0, Txt(c("colore")), If(Txt(c("tipo")) = "stazione", "stazioni", "società"))
                If Not perGruppo.ContainsKey(chiave) Then
                    perGruppo(chiave) = New List(Of String)()
                    gruppi.Add(chiave)
                    coloreGruppo(chiave) = ColoreCasella(c)
                End If
                Dim nome As String = Txt(c("nome"))
                If Vero(c("ipoteca")) Then
                    nome &= " (ipotecata)"
                ElseIf Num(c("case")) > 0 Then
                    nome &= If(Num(c("case")) = 5, " (albergo)", " (" & Txt(c("case")) & If(Num(c("case")) = 1, " casa)", " case)"))
                End If
                perGruppo(chiave).Add(nome)
            Next
            For Each chiave As String In gruppi
                sProp.Aggiungi(chiave & ": " & String.Join(", ", perGruppo(chiave)), False, coloreGruppo(chiave))
            Next
            If gruppi.Count = 0 Then sProp.Aggiungi("Nessuna proprietà")
            schermo.Sezioni.Add(sProp)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ComponiMonopoli", ex)
            Throw
        End Try
    End Sub

    Private Shared Function DescriviCasella(stato As JObject, c As JObject) As String
        Try
            Dim t As New StringBuilder(Txt(c("nome")))
            Select Case Txt(c("tipo"))
                Case "terreno" : t.Append(", terreno ").Append(Txt(c("colore")))
                Case "stazione" : t.Append(", stazione")
                Case "societa" : t.Append(", società")
            End Select
            If c("prezzo") IsNot Nothing AndAlso c("prezzo").Type <> JTokenType.Null Then t.Append(", prezzo ").Append(Euro(Num(c("prezzo"))))
            If c("proprietario") IsNot Nothing AndAlso c("proprietario").Type <> JTokenType.Null Then
                t.Append(", di ").Append(NomeGiocatore(stato, Num(c("proprietario"))))
                If Vero(c("ipoteca")) Then t.Append(", ipotecata")
            ElseIf Txt(c("tipo")) = "terreno" OrElse Txt(c("tipo")) = "stazione" OrElse Txt(c("tipo")) = "societa" Then
                t.Append(", libera")
            End If
            Return t.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.DescriviCasella", ex)
            Throw
        End Try
    End Function

    ''' <summary>Tutto quello che si sa di una proprietà: colore, prezzo, affitti, edifici, ipoteca, gruppo, proprietario.</summary>
    Private Shared Function InfoCasella(stato As JObject, c As JObject) As String
        Try
            Dim t As New StringBuilder()
            t.Append(Txt(c("nome")))
            Select Case Txt(c("tipo"))
                Case "terreno"
                    t.Append(", terreno di colore ").Append(Txt(c("colore"))).Append(". Prezzo ").Append(Euro(Num(c("prezzo")))).Append(". ")
                    Dim affitti As JArray = If(TryCast(c("affitti"), JArray), New JArray())
                    If affitti.Count = 6 Then
                        t.Append("Affitto: ").Append(Euro(Num(affitti(0)))).Append(" senza case, ")
                        t.Append(Euro(Num(affitti(1)))).Append(" con una casa, ")
                        t.Append(Euro(Num(affitti(2)))).Append(" con due case, ")
                        t.Append(Euro(Num(affitti(3)))).Append(" con tre case, ")
                        t.Append(Euro(Num(affitti(4)))).Append(" con quattro case, ")
                        t.Append(Euro(Num(affitti(5)))).Append(" con l'albergo. ")
                    End If
                    t.Append("Ogni casa o albergo costa ").Append(Euro(Num(c("costo_edificio")))).Append(". ")
                    t.Append("Valore di ipoteca ").Append(Euro(Num(c("valore_ipoteca")))).Append(". ")
                    Dim gruppo As JArray = If(TryCast(c("gruppo"), JArray), New JArray())
                    If gruppo.Count > 0 Then t.Append("Fa parte del gruppo: ").Append(String.Join(", ", gruppo.Select(Function(x) Txt(x)))).Append(". ")
                Case "stazione"
                    t.Append(", stazione. Prezzo ").Append(Euro(Num(c("prezzo")))).Append(". Affitto 25 euro con una stazione, 50 con due, 100 con tre, 200 con quattro. ")
                    t.Append("Valore di ipoteca ").Append(Euro(Num(c("valore_ipoteca")))).Append(". ")
                Case "societa"
                    t.Append(", società. Prezzo ").Append(Euro(Num(c("prezzo")))).Append(". Affitto 4 volte il tiro dei dadi con una società, 10 volte con due. ")
                    t.Append("Valore di ipoteca ").Append(Euro(Num(c("valore_ipoteca")))).Append(". ")
                Case Else
                    t.Append(". ")
            End Select
            If c("proprietario") IsNot Nothing AndAlso c("proprietario").Type <> JTokenType.Null Then
                t.Append("Proprietario: ").Append(NomeGiocatore(stato, Num(c("proprietario")))).Append(". ")
                If Vero(c("ipoteca")) Then t.Append("È ipotecata. ")
                If Num(c("case")) > 0 Then t.Append(If(Num(c("case")) = 5, "Ha l'albergo. ", "Ha " & Txt(c("case")) & If(Num(c("case")) = 1, " casa. ", " case. ")))
            ElseIf Txt(c("tipo")) <> "tassa" Then
                t.Append("Non appartiene a nessuno. ")
            End If
            Return t.ToString().Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.InfoCasella", ex)
            Throw
        End Try
    End Function

    Private Function IstruzioneMonopoli(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim posto As Integer = Num(s.Stato("posto"))
            If Ha(s, "offri") OrElse Ha(s, "ritirati_asta") Then
                Dim a As JObject = TryCast(v("asta"), JObject)
                Dim t As String = "Asta"
                If a IsNot Nothing Then
                    t &= " per " & Txt(a("nome")) & ". " & If(Num(a("offerta")) > 0, "Offerta più alta: " & Euro(Num(a("offerta"))) & " di " & NomeGiocatore(s.Stato, Num(a("leader"))) & ". ", "Nessuna offerta finora. ")
                End If
                Dim comandi As New List(Of String)()
                If Ha(s, "offri") Then comandi.Add("P offre di più")
                comandi.Add("R ti ritira")
                Return t & Istr("Tocca a te.", "Tocca a te: " & String.Join(", ", comandi) & ".")
            End If
            If Ha(s, "compra") Then
                Dim c As JObject = CasellaDi(s.Stato, Num(GiocatoreVista(v, posto)("posizione")))
                Return "Vuoi comprare " & Txt(c("nome")) & " per " & Euro(Num(c("prezzo"))) & "?" & Istr("", " Invio per rispondere.")
            End If
            If Ha(s, "passa") Then
                Dim cPassa As JObject = CasellaDi(s.Stato, Num(GiocatoreVista(v, posto)("posizione")))
                Return "Non hai abbastanza soldi per comprare " & Txt(cPassa("nome")) & ": va all'asta." & Istr("", " Invio per continuare.")
            End If
            If Ha(s, "accetta_scambio") Then
                Return "Ti propongono uno scambio: " & Txt(v("scambio")("testo")) & ". Accetti?" & Istr("", " A accetta, R rifiuta, G mostra i giocatori e le loro proprietà.")
            End If
            If Ha(s, "tira") Then
                Dim extra As New List(Of String)()
                If Ha(s, "paga_prigione") Then extra.Add("B paga la cauzione")
                If Ha(s, "usa_carta") Then extra.Add("U usa la carta per uscire di prigione")
                Dim lunga As String = "Tocca a te. Invio tira i dadi"
                If extra.Count > 0 Then lunga &= ", " & String.Join(", ", extra)
                Return Istr("Tocca a te.", lunga & ". Alt apre ipoteche, costruzioni e scambi.")
            End If
            If s.Mosse.Any(Function(m) Not MossaNotaMonopoli(m.Id)) Then Return Istr("Tocca a te.", "Tocca a te. Invio mostra cosa puoi fare.")
            Return "Non è il tuo turno."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IstruzioneMonopoli", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ domande (WGMenu solo per queste)
    ''' <summary>Un menu di tre voci: Sì, No e Più informazioni. Più informazioni legge tutto e il menu resta aperto. 0 = sì, 1 = no, -1 = Esc.</summary>
    Private Shared Function DomandaConInformazioniAsync(titolo As String, informazioni As Func(Of String)) As Task(Of Integer)
        Try
            Dim esito As New TaskCompletionSource(Of Integer)()
            Dim menu As New WGMenu()
            menu.aggiungi("Sì", Sub()
                                    esito.TrySetResult(0)
                                    Try
                                        menu.abbandona()
                                    Catch exLog As Exception
                                        RegistroErrori.Registra("MenuGiochi.DomandaConInformazioniAsync", exLog)
                                    End Try
                                End Sub)
            menu.aggiungi("No", Sub()
                                    esito.TrySetResult(1)
                                    Try
                                        menu.abbandona()
                                    Catch exLog As Exception
                                        RegistroErrori.Registra("MenuGiochi.DomandaConInformazioniAsync", exLog)
                                    End Try
                                End Sub)
            menu.aggiungi("Più informazioni", Sub() Leggi(informazioni()))
            AddHandler menu.abbandonato, Sub(mittente As Object, e As EventArgs) esito.TrySetResult(-1)
            ' Senza True: dopo "Più informazioni" il menu non si chiude.
            menu.avvia(titolo)
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.DomandaConInformazioniAsync", ex)
            Throw
        End Try
    End Function

    Private Async Sub AvviaDomandaSeServe(s As SessioneSchermo, Optional dopoLettura As Boolean = False)
        Try
            If s.Gioco <> "monopoli" Then Return
            If Ha(s, "compra") OrElse Ha(s, "passa") Then
                ' prima si sente quello che è successo (le mosse degli altri): una domanda che si apre subito coprirebbe i messaggi
                If dopoLettura Then Await AttendiFineLetturaAsync(s)
                If s.Esito.Task.IsCompleted OrElse s.Schermo Is Nothing OrElse s.Schermo.IsDisposed Then Return
            End If
            If Ha(s, "compra") Then
                Await RispondiAcquistoAsync(s)
            ElseIf Ha(s, "passa") Then
                Await AstaObbligataAsync(s)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.AvviaDomandaSeServe: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Non ci sono abbastanza soldi per comprare la casella: l'unica mossa del server è "passa", cioè l'asta. Prima non c'era niente che la facesse
    ''' partire e lo schermo diceva solo "Aspetta": la partita restava ferma in attesa di una mossa che nessuno poteva dare. Adesso si dice e parte da sola.
    ''' </summary>
    Private Async Function AstaObbligataAsync(s As SessioneSchermo) As Task
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim c As JObject = CasellaDi(s.Stato, Num(GiocatoreVista(v, Num(s.Stato("posto")))("posizione")))
            Informa(s, "Non hai abbastanza soldi per comprare " & Txt(c("nome")) & ", che costa " & Euro(Num(c("prezzo"))) & ": la casella va all'asta.")
            Await AttendiFineLetturaAsync(s)
            If s.Esito.Task.IsCompleted Then Return
            Esegui(s, "passa")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.AstaObbligataAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le mosse del Monopoli che questo programma sa gestire. Le altre, se il server ne offre, si scelgono da una lista (vedi ApriMosseSconosciute).</summary>
    Private Shared Function MossaNotaMonopoli(id As String) As Boolean
        Try
            Select Case id
                Case "tira", "paga_prigione", "usa_carta", "compra", "passa", "offri", "ritirati_asta", "accetta_scambio", "rifiuta_scambio"
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.MossaNotaMonopoli", ex)
            Throw
        End Try
    End Function

    ''' <summary>Rete di sicurezza: se il server offre mosse che il programma non conosce, le mette in una lista da scegliere, così non si resta mai bloccati.</summary>
    Private Sub ApriMosseSconosciute(s As SessioneSchermo)
        Try
            Dim sconosciute As List(Of MossaGioco) = s.Mosse.Where(Function(m) Not MossaNotaMonopoli(m.Id)).ToList()
            If sconosciute.Count = 0 Then
                NonSiPuo(s)
                Return
            End If
            MostraScelte(s, sconosciute.Select(Function(m) m.Id).ToList(), sconosciute.Select(Function(m) m.Testo).ToList(), "Cosa vuoi fare?", "mosse", False)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ApriMosseSconosciute", ex)
            Throw
        End Try
    End Sub

    Private Async Function RispondiAcquistoAsync(s As SessioneSchermo) As Task
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim c As JObject = CasellaDi(s.Stato, Num(GiocatoreVista(v, Num(s.Stato("posto")))("posizione")))
            Dim titolo As String = "Vuoi comprare " & Txt(c("nome")) & "? " & If(Txt(c("colore")).Length > 0, "Colore " & Txt(c("colore")) & ". ", "") & "Prezzo " & Euro(Num(c("prezzo"))) & "."
            Dim r As Integer = Await InDialogoAsync(Of Integer)(s, Function() DomandaConInformazioniAsync(titolo, Function() InfoCasella(s.Stato, c)))
            If r = 0 Then
                Esegui(s, "compra")
            ElseIf r = 1 Then
                Esegui(s, "passa")
            Else
                s.DomandaInSospeso = "acquisto"
                s.Schermo.Aggiorna()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.RispondiAcquistoAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function RispondiScambioAsync(s As SessioneSchermo) As Task
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim testo As String = Txt(v("scambio")("testo"))
            Dim titolo As String = "Ti propongono uno scambio: " & testo & ". Accetti?"
            Dim soldi As Integer = Num(GiocatoreVista(v, Num(s.Stato("posto")))("soldi"))
            Dim r As Integer = Await InDialogoAsync(Of Integer)(s, Function() DomandaConInformazioniAsync(titolo, Function() testo & " Hai " & Euro(soldi) & "."))
            If r = 0 Then
                Esegui(s, "accetta_scambio")
            ElseIf r = 1 Then
                Esegui(s, "rifiuta_scambio")
            Else
                s.DomandaInSospeso = "scambio"
                s.Schermo.Aggiorna()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.RispondiScambioAsync", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ comandi rapidi
    Private Sub GestisciTasto(s As SessioneSchermo, e As TastoGiocoEventArgs)
        Try
            If e.Ctrl OrElse s.Dialoghi > 0 Then Return
            Dim k As Keys = e.Codice
            If s.ScelteMosse IsNot Nothing Then Return
            Select Case k
                Case Keys.L
                    e.Gestito = True
                    RileggiMessaggi(s)
                    Return
                Case Keys.W
                    e.Gestito = True
                    ScriviMessaggio(s)
                    Return
                Case Keys.Insert
                    e.Gestito = True
                    Leggi(s.Istruzione)
                    Return
            End Select
            If s.Gioco = "burraco" Then
                e.Gestito = TastoBurraco(s, k)
            ElseIf s.Gioco = "battaglia" Then
                e.Gestito = TastoBattaglia(s, k)
            ElseIf EItaliano(s.Gioco) Then
                e.Gestito = TastoItaliano(s, k)
            Else
                e.Gestito = TastoMonopoli(s, k)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.GestisciTasto: " & ex.ToString())
        End Try
    End Sub

    Private Sub NonSiPuo(s As SessioneSchermo)
        Try
            Leggi("Non si può.")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NonSiPuo", ex)
            Throw
        End Try
    End Sub

    ' ---- burraco
    Private Function TastoBurraco(s As SessioneSchermo, k As Keys) As Boolean
        Try
            Select Case k
                Case Keys.Enter, Keys.P
                    If Ha(s, "pesca") Then
                        Esegui(s, "pesca")
                    ElseIf k = Keys.Enter Then
                        Leggi(s.Istruzione)
                    Else
                        NonSiPuo(s)
                    End If
                Case Keys.S
                    ApriScelta(s, "scarti|", "Quali scarti vuoi prendere?")
                Case Keys.C
                    ApriScelta(s, "cala|", "Quale combinazione vuoi calare?")
                Case Keys.A
                    ApriScelta(s, "att|", "Quale carta vuoi attaccare, e dove?")
                Case Keys.D
                    ApriScelta(s, "scarta|", "Quale carta vuoi scartare?")
                Case Keys.X
                    If Ha(s, "annulla") Then Esegui(s, "annulla") Else NonSiPuo(s)
                Case Keys.M
                    Informa(s, TestoManoBurraco(s))
                Case Keys.T
                    Informa(s, TestoTavoloBurraco(s))
                Case Keys.E
                    Informa(s, TestoScartiBurraco(s))
                Case Keys.Q
                    Informa(s, TestoPuntiBurraco(s))
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TastoBurraco", ex)
            Throw
        End Try
    End Function

    ' ---- briscola e scopa
    Private Function TastoItaliano(s As SessioneSchermo, k As Keys) As Boolean
        Try
            Select Case k
                Case Keys.Enter, Keys.G
                    If MosseCarte(s) Then
                        ApriCarte(s)
                    ElseIf k = Keys.Enter Then
                        Leggi(s.Istruzione)
                    Else
                        NonSiPuo(s)
                    End If
                Case Keys.M
                    Informa(s, TestoManoBurraco(s))
                Case Keys.T
                    Informa(s, Txt(Vista(s.Stato)("testo_tavolo")))
                Case Keys.Q
                    Informa(s, Txt(Vista(s.Stato)("testo_punti")))
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TastoItaliano", ex)
            Throw
        End Try
    End Function

    ''' <summary>A briscola e a scopa apre subito il menu delle carte, anche se ce n'è una sola, così si legge il punteggio prima di confermare.</summary>
    Private Sub ApriCarte(s As SessioneSchermo)
        Try
            If s.Gioco = "scopa" Then
                ApriCarteScopa(s)
            Else
                ApriCarteBriscola(s)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ApriCarte", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Briscola: la carta da giocare. Alla marianna, quando si può accusare, nel menu ci sono anche gli accusi; alla briscola a chiamata,
    ''' a chi apre il menu offre le carte da chiamare. Il menu si apre anche per una sola voce, così si legge il punteggio prima di confermare.
    ''' </summary>
    Private Sub ApriCarteBriscola(s As SessioneSchermo)
        Try
            Dim voci As New List(Of MossaGioco)()
            Dim domanda As String = "Quale carta?"
            Dim chiamate As List(Of MossaGioco) = ConPrefisso(s, "chiama|")
            If chiamate.Count > 0 Then
                voci.AddRange(chiamate)
                domanda = "Quale carta chiami?"
            Else
                Dim accusi As List(Of MossaGioco) = ConPrefisso(s, "accuso|")
                voci.AddRange(accusi)
                voci.AddRange(ConPrefisso(s, "gioca|"))
                If accusi.Count > 0 Then domanda = "Puoi accusare la marianna o giocare una carta."
            End If
            If voci.Count = 0 Then
                NonSiPuo(s)
                Return
            End If
            MostraScelte(s, voci.Select(Function(m) m.Id).ToList(), voci.Select(Function(m) TestoVoceMossa(s, m)).ToList(), domanda, "mosse", False)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ApriCarteBriscola", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Se c'è una sola mossa di quel tipo la esegue; se sono più d'una apre la piccola lista di scelte. Con sempreMenu la lista si apre anche per una sola voce.</summary>
    Private Sub ApriScelta(s As SessioneSchermo, prefisso As String, domanda As String, Optional sempreMenu As Boolean = False)
        Try
            Dim candidate As List(Of MossaGioco) = ConPrefisso(s, prefisso)
            If candidate.Count = 0 Then
                NonSiPuo(s)
                Return
            End If
            If candidate.Count = 1 AndAlso Not sempreMenu Then
                Esegui(s, candidate(0).Id)
                Return
            End If
            MostraScelte(s, candidate.Select(Function(m) m.Id).ToList(), candidate.Select(Function(m) TestoVoceMossa(s, m)).ToList(), domanda, "mosse", False)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ApriScelta", ex)
            Throw
        End Try
    End Sub

    Private Sub MostraScelte(s As SessioneSchermo, ids As IList(Of String), testi As IList(Of String), domanda As String, livello As String, tornaAlleCarte As Boolean)
        Try
            s.ScelteMosse = ids.ToList()
            s.LivelloScelta = livello
            s.TornaAlleCarte = tornaAlleCarte
            s.Schermo.ImpostaScelte(testi)
            Dim tastoEsc As String = If(tornaAlleCarte, "Esc torna alle carte", If(EItaliano(s.Gioco), "Esc chiude la partita", "Esc torna indietro"))
            s.Schermo.Istruzione = domanda & If(IstruzioniAttive(), " Su e giù scorrono, Invio sceglie, " & tastoEsc & ".", "")
            Leggi(domanda & " " & testi(0) & ", 1 di " & testi.Count.ToString() & ".")
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.MostraScelte", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Aspetta che la sintesi vocale finisca di leggere, così il menu delle carte si apre dopo i messaggi.</summary>
    ''' <summary>
    ''' Legge i messaggi uno dopo l'altro. Shift taglia quello che si sta leggendo e passa al successivo; se parte un'altra lettura
    ''' (un comando, una domanda) la sequenza si ferma.
    ''' </summary>
    ''' <summary>
    ''' Divide un testo in frasi, una per volta da leggere: Shift salta una frase sola. I pezzi troppo corti (un "Scopa!", un nome, un numero) restano
    ''' attaccati alla frase prima, così non si perdono da soli.
    ''' </summary>
    Private Shared Function DividiInFrasi(testo As String) As List(Of String)
        Try
            Dim frasi As New List(Of String)()
            For Each p As String In System.Text.RegularExpressions.Regex.Split(testo.Trim(), "(?<=[.!?])\s+")
                Dim t As String = p.Trim()
                If t.Length = 0 Then Continue For
                If frasi.Count > 0 AndAlso t.Length < 30 Then
                    frasi(frasi.Count - 1) &= " " & t
                Else
                    frasi.Add(t)
                End If
            Next
            Return frasi
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.DividiInFrasi", ex)
            Throw
        End Try
    End Function

    Private Sub LeggiASegmenti(s As SessioneSchermo, segmenti As List(Of String))
        Try
            If segmenti.Count = 0 Then Return
            ' anche con una frase sola si usa la lettura a messaggi: così Shift la salta e chi aspetta la fine non resta indietro
            Dim gen As Integer = System.Threading.Interlocked.Increment(GenLettura)
            s.Sequenza = True
            Task.Run(Async Function() As Task
                         Try
                             Dim voce As sintesivocale = sintesivocale.GetInstance()
                             Dim gestiti As Integer = s.Salti   ' le pressioni di Shift già arrivate prima di questa lettura non contano
                             For Each seg As String In segmenti
                                 If s.Esito.Task.IsCompleted OrElse GenLettura <> gen Then Exit For
                                 ' Shift premuto prima che questa frase cominciasse (la precedente era appena finita o saltata): si salta senza dirla
                                 If s.Salti > gestiti Then
                                     gestiti += 1
                                     Continue For
                                 End If
                                 Dim salti As Integer = gestiti
                                 Try
                                     SegnaLettura()
                                     voce.LeggiTesto(seg)
                                 Catch exLog As Exception
                                     RegistroErrori.Registra("MenuGiochi.LeggiASegmenti", exLog)
                                 End Try
                                 Dim atteso As Integer = 0
                                 While Not voce.StaParlando AndAlso s.Salti = salti AndAlso GenLettura = gen AndAlso atteso < 1500
                                     Await Task.Delay(50)
                                     atteso += 50
                                 End While
                                 atteso = 0
                                 While voce.StaParlando AndAlso s.Salti = salti AndAlso GenLettura = gen AndAlso Not s.Esito.Task.IsCompleted AndAlso atteso < 120000
                                     Await Task.Delay(50)
                                     atteso += 50
                                 End While
                                 If s.Salti > gestiti Then gestiti += 1   ' saltata mentre si leggeva: Shift ha già fermato la voce
                             Next
                         Catch ex As Exception
                             _logger.ScriviLog("MenuGiochi.LeggiASegmenti: " & ex.Message)
                         Finally
                             s.Sequenza = False
                         End Try
                     End Function)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.LeggiASegmenti", ex)
            Throw
        End Try
    End Sub

    Private Async Function AttendiFineLetturaAsync(s As SessioneSchermo) As Task
        Try
            ' prima finisce la lettura a messaggi, se c'è
            Dim a0 As Integer = 0
            While s.Sequenza AndAlso Not s.Esito.Task.IsCompleted AndAlso a0 < 120000
                Await Task.Delay(50)
                a0 += 50
            End While
            Dim voce As sintesivocale = sintesivocale.GetInstance()
            ' si aspetta che la voce cominci solo se una lettura è stata chiesta da pochissimo: se la voce è ferma (per esempio perché si è premuto
            ' Shift per saltare i messaggi) non c'è niente da aspettare e la domanda parte subito
            Dim atteso As Integer = 0
            While Not voce.StaParlando AndAlso Not s.Esito.Task.IsCompleted AndAlso LetturaRecente(1500) AndAlso atteso < 1500
                Await Task.Delay(50)
                atteso += 50
            End While
            atteso = 0
            While voce.StaParlando AndAlso Not s.Esito.Task.IsCompleted AndAlso atteso < 120000
                Await Task.Delay(50)
                atteso += 50
            End While
        Catch exLog As Exception
            RegistroErrori.Registra("MenuGiochi.AttendiFineLetturaAsync", exLog)
        End Try
    End Function

    Private Sub ConfermaScelta(s As SessioneSchermo, indice As Integer)
        Try
            If s.ScelteMosse Is Nothing OrElse indice < 0 OrElse indice >= s.ScelteMosse.Count Then Return
            Dim id As String = s.ScelteMosse(indice)
            If id.StartsWith("carta|", StringComparison.Ordinal) Then
                Dim carta As String = id.Substring(6)
                Dim delGruppo As List(Of MossaGioco) = s.Mosse.Where(Function(m) m.Id.StartsWith("gioca|" & carta & "|", StringComparison.Ordinal)).ToList()
                If delGruppo.Count = 0 Then
                    NonSiPuo(s)
                    Return
                End If
                If delGruppo.Count = 1 Then
                    Esegui(s, delGruppo(0).Id)
                    Return
                End If
                MostraScelte(s, delGruppo.Select(Function(m) m.Id).ToList(), delGruppo.Select(Function(m) TestoVoceMossa(s, m)).ToList(), "Con questa carta puoi prendere in più modi.", "prese", True)
                Return
            End If
            Esegui(s, id)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ConfermaScelta", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Scopa: prima si sceglie la carta. Se con quella carta si può prendere in più modi, si sceglie la presa. Il menu si apre anche se c'è una sola carta.</summary>
    Private Sub ApriCarteScopa(s As SessioneSchermo)
        Try
            Dim mosse As List(Of MossaGioco) = ConPrefisso(s, "gioca|")
            If mosse.Count = 0 Then
                NonSiPuo(s)
                Return
            End If
            Dim perCarta As New Dictionary(Of String, List(Of MossaGioco))()
            Dim ordine As New List(Of String)()
            For Each m As MossaGioco In mosse
                Dim parti As String() = m.Id.Split("|"c)
                Dim carta As String = If(parti.Length >= 2, parti(1), "")
                If Not perCarta.ContainsKey(carta) Then
                    perCarta(carta) = New List(Of MossaGioco)()
                    ordine.Add(carta)
                End If
                perCarta(carta).Add(m)
            Next
            If ordine.Count = 1 Then
                Dim solo As List(Of MossaGioco) = perCarta(ordine(0))
                MostraScelte(s, solo.Select(Function(m) m.Id).ToList(), solo.Select(Function(m) TestoVoceMossa(s, m)).ToList(), If(solo.Count = 1, "Quale carta?", "Con questa carta puoi prendere in più modi."), "prese", False)
                Return
            End If
            Dim ids As New List(Of String)()
            Dim testi As New List(Of String)()
            For Each carta As String In ordine
                Dim gruppo As List(Of MossaGioco) = perCarta(carta)
                If gruppo.Count = 1 Then
                    ids.Add(gruppo(0).Id)
                    testi.Add(TestoVoceMossa(s, gruppo(0)))
                Else
                    ids.Add("carta|" & carta)
                    testi.Add(TestoCartaScopa(s, carta) & ", puoi prendere in " & gruppo.Count.ToString() & " modi")
                End If
            Next
            MostraScelte(s, ids, testi, "Quale carta?", "carte", False)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ApriCarteScopa", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Testo di una voce del menu: per le carte dice il punteggio, non "gioca".</summary>
    Private Function TestoVoceMossa(s As SessioneSchermo, m As MossaGioco) As String
        Try
            If m.Id.StartsWith("gioca|", StringComparison.Ordinal) Then Return TestoGioca(s, m)
            If s.Gioco = "burraco" AndAlso (m.Id.StartsWith("scarta|", StringComparison.Ordinal) OrElse m.Id.StartsWith("att|", StringComparison.Ordinal)) Then
                If m.Testo.IndexOf("punt", StringComparison.OrdinalIgnoreCase) >= 0 Then Return m.Testo
                Dim idCarta As Integer = IdCartaMossa(m.Id)
                Dim punti As Integer = PuntiBurraco(s, idCarta)
                If punti >= 0 Then Return m.Testo & ", " & punti.ToString(CultureInfo.InvariantCulture) & " punti"
            End If
            Return m.Testo
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoVoceMossa", ex)
            Throw
        End Try
    End Function

    Private Function TestoGioca(s As SessioneSchermo, m As MossaGioco) As String
        Try
            Dim parti As String() = m.Id.Split("|"c)
            Dim idCarta As Integer = 0
            If parti.Length < 2 OrElse Not Integer.TryParse(parti(1), NumberStyles.Integer, CultureInfo.InvariantCulture, idCarta) Then Return m.Testo
            If s.Gioco = "scopa" Then
                Dim t As String = TestoCartaScopa(s, parti(1))
                If parti.Length < 3 OrElse parti(2) = "-" OrElse parti(2).Length = 0 Then
                    t &= ", la lasci sul tavolo"
                Else
                    Dim prese As New List(Of String)()
                    For Each pezzo As String In parti(2).Split(","c)
                        Dim idPresa As Integer
                        If Integer.TryParse(pezzo, NumberStyles.Integer, CultureInfo.InvariantCulture, idPresa) Then prese.Add(NomeCartaDaId(s, idPresa))
                    Next
                    If prese.Count > 0 Then t &= ", prendi " & String.Join(" e ", prese)
                    If m.Testo.IndexOf("scopa", StringComparison.OrdinalIgnoreCase) >= 0 Then t &= " (scopa)"
                End If
                Return t
            End If
            Dim punti As Integer = PuntiBriscola(idCarta)
            Dim voce As String = NomeCartaDaId(s, idCarta) & ", " & punti.ToString(CultureInfo.InvariantCulture) & If(punti = 1, " punto", " punti")
            If EBriscola(s, idCarta) Then voce &= ", briscola"
            Return voce
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoGioca", ex)
            Throw
        End Try
    End Function

    Private Function TestoCartaScopa(s As SessioneSchermo, idTesto As String) As String
        Try
            Dim idCarta As Integer = 0
            Integer.TryParse(idTesto, NumberStyles.Integer, CultureInfo.InvariantCulture, idCarta)
            Dim vale As Integer = (idCarta Mod 10) + 1
            Dim t As String = NomeCartaDaId(s, idCarta) & ", vale " & vale.ToString(CultureInfo.InvariantCulture)
            If idCarta \ 10 = 1 AndAlso idCarta Mod 10 = 6 Then t &= ", sette bello"
            Dim variante As String = Txt(s.Stato("variante"))
            If idCarta = 19 AndAlso (variante = "rebello" OrElse variante = "completa") Then t &= ", re bello"
            If idCarta Mod 10 = 0 AndAlso (variante = "asso_piglia_tutto" OrElse variante = "completa") Then t &= ", asso pigliatutto"
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoCartaScopa", ex)
            Throw
        End Try
    End Function

    Private Shared Function PuntiBriscola(id As Integer) As Integer
        Try
            Select Case id Mod 10
                Case 0 : Return 11
                Case 2 : Return 10
                Case 7 : Return 2
                Case 8 : Return 3
                Case 9 : Return 4
                Case Else : Return 0
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.PuntiBriscola", ex)
            Throw
        End Try
    End Function

    Private Function PuntiBurraco(s As SessioneSchermo, id As Integer) As Integer
        Try
            Dim c As JObject = TrovaCarta(s, id)
            If c Is Nothing Then Return -1
            If Vero(c("jolly")) Then Return 30
            Dim r As Integer = Num(c("rango"))
            If r = 1 OrElse r = 14 Then Return 15
            If r = 2 Then Return 20
            If r >= 8 Then Return 10
            If r > 0 Then Return 5
            Return -1
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.PuntiBurraco", ex)
            Throw
        End Try
    End Function

    Private Function EBriscola(s As SessioneSchermo, idCarta As Integer) As Boolean
        Try
            Dim b As JToken = Vista(s.Stato)("briscola")
            If b Is Nothing OrElse b.Type = JTokenType.Null Then Return False
            Return Num(b("id")) \ 10 = idCarta \ 10
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.EBriscola", ex)
            Throw
        End Try
    End Function

    Private Shared Function IdCartaMossa(idMossa As String) As Integer
        Try
            Dim parti As String() = idMossa.Split("|"c)
            Dim idCarta As Integer = 0
            If parti.Length >= 2 Then Integer.TryParse(parti(1), NumberStyles.Integer, CultureInfo.InvariantCulture, idCarta)
            Return idCarta
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.IdCartaMossa", ex)
            Throw
        End Try
    End Function

    Private Function NomeCartaDaId(s As SessioneSchermo, id As Integer) As String
        Try
            Dim trovata As JObject = TrovaCarta(s, id)
            If trovata IsNot Nothing AndAlso Txt(trovata("nome")).Length > 0 Then Return Txt(trovata("nome"))
            If id >= 0 AndAlso id <= 39 Then
                Dim ranghi As String() = {"Asso", "Due", "Tre", "Quattro", "Cinque", "Sei", "Sette", "Fante", "Cavallo", "Re"}
                Dim semi As String() = {"coppe", "denari", "bastoni", "spade"}
                Return ranghi(id Mod 10) & " di " & semi(id \ 10)
            End If
            Return "questa carta"
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NomeCartaDaId", ex)
            Throw
        End Try
    End Function

    Private Function TrovaCarta(s As SessioneSchermo, id As Integer) As JObject
        Try
            Dim v As JObject = Vista(s.Stato)
            For Each nome As String In New String() {"mano", "tavolo_carte"}
                Dim elenco As JArray = TryCast(v(nome), JArray)
                If elenco Is Nothing Then Continue For
                For Each c As JToken In elenco
                    If Num(c("id")) = id Then Return TryCast(c, JObject)
                Next
            Next
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TrovaCarta", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomiCarte(carte As JArray) As String
        Try
            Return String.Join(", ", carte.Select(Function(c) Txt(c("nome"))))
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NomiCarte", ex)
            Throw
        End Try
    End Function

    Private Function TestoManoBurraco(s As SessioneSchermo) As String
        Try
            Dim mano As JArray = If(TryCast(Vista(s.Stato)("mano"), JArray), New JArray())
            Return "Hai in mano " & mano.Count.ToString() & " carte: " & NomiCarte(mano) & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoManoBurraco", ex)
            Throw
        End Try
    End Function

    Private Function TestoTavoloBurraco(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim mia As Integer = Num(v("mia_squadra"))
            Dim t As New StringBuilder()
            Dim squadre As JArray = If(TryCast(v("squadre"), JArray), New JArray())
            For i As Integer = 0 To squadre.Count - 1
                Dim q As JObject = CType(squadre(i), JObject)
                t.Append(If(i = mia, "La tua squadra", "Gli avversari")).Append(", ").Append(NomiSquadra(s.Stato, CType(q("membri"), JArray))).Append(": ")
                t.Append(If(Vero(q("pozzetto_preso")), "pozzetto preso", "pozzetto non preso")).Append(", ")
                t.Append(If(Vero(q("aperta")), "ha aperto. ", "non ha aperto, servono " & Txt(q("minimo")) & " punti. "))
                Dim comb As JArray = CType(q("combinazioni"), JArray)
                If comb.Count = 0 Then
                    t.Append("Nessuna combinazione. ")
                Else
                    Dim k As Integer = 1
                    For Each c As JObject In comb
                        t.Append(k.ToString()).Append(", ").Append(Txt(c("descrizione"))).Append(": ").Append(NomiCarte(CType(c("carte"), JArray))).Append(". ")
                        k += 1
                    Next
                End If
            Next
            Return t.ToString().Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoTavoloBurraco", ex)
            Throw
        End Try
    End Function

    Private Function TestoScartiBurraco(s As SessioneSchermo) As String
        Try
            Dim sc As JObject = TryCast(Vista(s.Stato)("scarti"), JObject)
            If sc Is Nothing OrElse Num(sc("n")) = 0 Then Return "Gli scarti sono vuoti."
            Dim t As String = "Negli scarti ci sono " & Txt(sc("n")) & " carte, in cima " & Txt(sc("cima")("nome")) & "."
            If Vero(sc("congelato")) Then t &= " Il pozzo è congelato: si prende solo con una combinazione nuova di carte naturali."
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoScartiBurraco", ex)
            Throw
        End Try
    End Function

    Private Function TestoPuntiBurraco(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim p As JObject = TryCast(v("partita"), JObject)
            If p Is Nothing Then Return "Non ci sono punti."
            Dim totali As JArray = CType(p("totali"), JArray)
            Dim squadre As JArray = CType(v("squadre"), JArray)
            Dim t As New StringBuilder("Mano " & Txt(p("mano")) & ", si gioca fino a " & Txt(p("obiettivo")) & " punti. Totali: ")
            For i As Integer = 0 To squadre.Count - 1
                t.Append(NomiSquadra(s.Stato, CType(squadre(i)("membri"), JArray))).Append(" ").Append(Txt(totali(i)))
                t.Append(", ")
            Next
            t.Append("carte calate in questa mano: ")
            For i As Integer = 0 To squadre.Count - 1
                t.Append(Txt(squadre(i)("punti_calati"))).Append(" punti")
                If i < squadre.Count - 1 Then t.Append(", ")
            Next
            Return t.ToString() & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoPuntiBurraco", ex)
            Throw
        End Try
    End Function

    ' ---- monopoli
    Private Function TastoMonopoli(s As SessioneSchermo, k As Keys) As Boolean
        Try
            Select Case k
                Case Keys.Enter
                    If Ha(s, "tira") Then
                        Esegui(s, "tira")
                    ElseIf s.DomandaInSospeso = "acquisto" Then
                        s.DomandaInSospeso = ""
                        Dim domandaAcquisto As Task = RispondiAcquistoAsync(s)
                    ElseIf s.DomandaInSospeso = "scambio" Then
                        s.DomandaInSospeso = ""
                        Dim domandaScambio As Task = RispondiScambioAsync(s)
                    ElseIf Ha(s, "passa") AndAlso Not Ha(s, "compra") Then
                        Esegui(s, "passa")
                    ElseIf s.Mosse.Any(Function(m) Not MossaNotaMonopoli(m.Id)) Then
                        ApriMosseSconosciute(s)
                    Else
                        Leggi(s.Istruzione)
                    End If
                Case Keys.T
                    If Ha(s, "tira") Then Esegui(s, "tira") Else NonSiPuo(s)
                Case Keys.U
                    If Ha(s, "usa_carta") Then Esegui(s, "usa_carta") Else NonSiPuo(s)
                Case Keys.B
                    If Ha(s, "paga_prigione") Then Esegui(s, "paga_prigione") Else NonSiPuo(s)
                Case Keys.P
                    If Ha(s, "offri") Then OffriAsync(s) Else NonSiPuo(s)
                Case Keys.A
                    If Ha(s, "accetta_scambio") Then Esegui(s, "accetta_scambio") Else NonSiPuo(s)
                Case Keys.R
                    If Ha(s, "ritirati_asta") Then
                        Esegui(s, "ritirati_asta")
                    ElseIf Ha(s, "rifiuta_scambio") Then
                        Esegui(s, "rifiuta_scambio")
                    Else
                        NonSiPuo(s)
                    End If
                Case Keys.C
                    Informa(s, TestoCassa(s))
                Case Keys.V
                    ApriElencoProprieta(s)
                Case Keys.G
                    ApriElencoGiocatori(s)
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TastoMonopoli", ex)
            Throw
        End Try
    End Function

    Private Async Sub OffriAsync(s As SessioneSchermo)
        Try
            Dim m As MossaGioco = s.Mosse.First(Function(x) x.Id = "offri")
            Dim minimo As Integer = If(m.Parametro IsNot Nothing, Num(m.Parametro("min")), 10)
            Dim massimo As Integer = If(m.Parametro IsNot Nothing, Num(m.Parametro("max")), 0)
            Dim domanda As String = If(m.Parametro IsNot Nothing, Txt(m.Parametro("domanda")), "Quanto offri?")
            Dim importo As Integer = Await InDialogoAsync(Of Integer)(s, Function() ChiediNumeroAsync(domanda, minimo, massimo, minimo))
            If importo >= 0 Then Esegui(s, "offri|" & importo.ToString(CultureInfo.InvariantCulture))
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.OffriAsync: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Chiede un numero con un campo (WGCampi) finché è valido tra minimo e massimo. -1 se si annulla.</summary>
    Private Async Function ChiediNumeroAsync(domanda As String, minimo As Integer, massimo As Integer, predefinito As Integer) As Task(Of Integer)
        Try
            Dim valore As String = predefinito.ToString(CultureInfo.InvariantCulture)
            Do
                Dim testo As String = Await ChiediTestoAsync(domanda, valore)
                If testo Is Nothing Then Return -1
                Dim n As Integer
                If Integer.TryParse(testo.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, n) AndAlso n >= minimo AndAlso n <= massimo Then Return n
                Messaggio("Scrivi un numero da " & minimo.ToString(CultureInfo.InvariantCulture) & " a " & massimo.ToString(CultureInfo.InvariantCulture) & ".")
                valore = testo.Trim()
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ChiediNumeroAsync", ex)
            Throw
        End Try
    End Function

    Private Function TestoCassa(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim posto As Integer = Num(s.Stato("posto"))
            Dim g As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
            If posto < 0 OrElse posto >= g.Count Then Return "Non hai una cassa."
            Dim t As New StringBuilder("Hai " & Euro(Num(g(posto)("soldi"))) & ", con le proprietà e gli edifici il tuo patrimonio è " & Euro(Num(g(posto)("patrimonio"))) & ". ")
            For i As Integer = 0 To g.Count - 1
                If i = posto Then Continue For
                t.Append(NomeGiocatore(s.Stato, i)).Append(": ").Append(If(Vero(g(i)("fuori")), "fuori dal gioco", Euro(Num(g(i)("soldi"))))).Append(". ")
            Next
            If Num(v("parcheggio_piatto")) > 0 Then t.Append("Nel Parcheggio gratuito ci sono ").Append(Euro(Num(v("parcheggio_piatto")))).Append(". ")
            Return t.ToString().Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoCassa", ex)
            Throw
        End Try
    End Function

    Private Function TestoProprieta(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim posto As Integer = Num(s.Stato("posto"))
            Dim voci As New List(Of String)()
            For Each c As JObject In If(TryCast(v("caselle"), JArray), New JArray())
                If c("proprietario") IsNot Nothing AndAlso c("proprietario").Type <> JTokenType.Null AndAlso Num(c("proprietario")) = posto Then
                    Dim t As String = Txt(c("nome"))
                    If Txt(c("colore")).Length > 0 Then t &= ", " & Txt(c("colore"))
                    If Vero(c("ipoteca")) Then
                        t &= ", ipotecata"
                    ElseIf Num(c("case")) > 0 Then
                        t &= If(Num(c("case")) = 5, ", albergo", ", " & Txt(c("case")) & If(Num(c("case")) = 1, " casa", " case"))
                    End If
                    voci.Add(t)
                End If
            Next
            If voci.Count = 0 Then Return "Non hai nessuna proprietà."
            Return "Hai " & voci.Count.ToString() & " proprietà: " & String.Join("; ", voci) & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoProprieta", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ elenchi scorrevoli di giocatori e proprietà
    Private Async Sub ApriElencoGiocatori(s As SessioneSchermo)
        Try
            Await InDialogoAsync(Of Integer)(s, Function() ElencoGiocatoriAsync(s))
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ApriElencoGiocatori: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ApriElencoProprieta(s As SessioneSchermo)
        Try
            Await InDialogoAsync(Of Integer)(s, Function() ElencoProprietaAsync(s, Num(s.Stato("posto"))))
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ApriElencoProprieta: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Le caselle (proprietà) di un giocatore.</summary>
    Private Shared Function ProprietaDi(stato As JObject, giocatore As Integer) As List(Of JObject)
        Try
            Dim r As New List(Of JObject)()
            For Each c As JObject In If(TryCast(Vista(stato)("caselle"), JArray), New JArray())
                If c("proprietario") IsNot Nothing AndAlso c("proprietario").Type <> JTokenType.Null AndAlso Num(c("proprietario")) = giocatore Then r.Add(c)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ProprietaDi", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' L'elenco dei giocatori, da scorrere con Su e Giù: Invio apre l'elenco delle proprietà di quel giocatore (per decidere cosa scambiare),
    ''' Esc torna indietro dall'elenco delle proprietà e chiude l'elenco dei giocatori.
    ''' </summary>
    Private Async Function ElencoGiocatoriAsync(s As SessioneSchermo) As Task(Of Integer)
        Try
            Dim indice As Integer = 0
            Do
                If s.Esito.Task.IsCompleted Then Return 0
                Dim v As JObject = Vista(s.Stato)
                Dim g As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
                Dim posto As Integer = Num(s.Stato("posto"))
                Dim voci As New List(Of VocePannelloGioco)()
                For i As Integer = 0 To g.Count - 1
                    Dim t As String = NomeGiocatore(s.Stato, i) & If(i = posto, " (tu)", "")
                    If Vero(g(i)("fuori")) Then
                        t &= ", fuori dal gioco"
                    Else
                        t &= ", " & Euro(Num(g(i)("soldi"))) & ", su " & Txt(CasellaDi(s.Stato, Num(g(i)("posizione")))("nome"))
                        If Vero(g(i)("prigione")) Then t &= ", in prigione"
                        Dim n As Integer = ProprietaDi(s.Stato, i).Count
                        t &= ", " & If(n = 0, "nessuna proprietà", n.ToString() & If(n = 1, " proprietà", " proprietà"))
                    End If
                    voci.Add(New VocePannelloGioco With {.Testo = t})
                Next
                Dim scelta As List(Of Integer) = Await PannelloAsync(s, "Giocatori", voci, False, False, indice)
                If scelta Is Nothing OrElse scelta.Count = 0 Then Return 0
                indice = scelta(0)
                Await ElencoProprietaAsync(s, indice)
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ElencoGiocatoriAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le proprietà di un giocatore, da scorrere: Invio legge tutti i dettagli della proprietà (prezzo, affitti, case, gruppo), Esc torna indietro.</summary>
    Private Async Function ElencoProprietaAsync(s As SessioneSchermo, giocatore As Integer) As Task(Of Integer)
        Try
            Dim indice As Integer = 0
            Dim nome As String = NomeGiocatore(s.Stato, giocatore) & If(giocatore = Num(s.Stato("posto")), " (tu)", "")
            Do
                If s.Esito.Task.IsCompleted Then Return 0
                Dim proprieta As List(Of JObject) = ProprietaDi(s.Stato, giocatore)
                If proprieta.Count = 0 Then
                    Leggi(nome & " non ha nessuna proprietà.")
                    Await AttendiFineLetturaAsync(s)
                    Return 0
                End If
                Dim voci As New List(Of VocePannelloGioco)()
                For Each c As JObject In proprieta
                    Dim t As String = Txt(c("nome"))
                    If Txt(c("colore")).Length > 0 Then t &= ", " & Txt(c("colore"))
                    t &= ", " & Euro(Num(c("prezzo")))
                    If Vero(c("ipoteca")) Then
                        t &= ", ipotecata"
                    ElseIf Num(c("case")) > 0 Then
                        t &= If(Num(c("case")) = 5, ", albergo", ", " & Txt(c("case")) & If(Num(c("case")) = 1, " casa", " case"))
                    End If
                    voci.Add(New VocePannelloGioco With {.Testo = t})
                Next
                Dim scelta As List(Of Integer) = Await PannelloAsync(s, "Proprietà di " & nome & ", " & proprieta.Count.ToString(), voci, False, False, indice)
                If scelta Is Nothing OrElse scelta.Count = 0 Then Return 0
                indice = scelta(0)
                Leggi(InfoCasella(s.Stato, proprieta(indice)))
                Await AttendiFineLetturaAsync(s)
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ElencoProprietaAsync", ex)
            Throw
        End Try
    End Function

    Private Function TestoGiocatori(s As SessioneSchermo) As String
        Try
            Dim v As JObject = Vista(s.Stato)
            Dim g As JArray = If(TryCast(v("giocatori"), JArray), New JArray())
            Dim parti As New List(Of String)()
            For i As Integer = 0 To g.Count - 1
                Dim t As String = NomeGiocatore(s.Stato, i) & If(i = Num(s.Stato("posto")), " (tu)", "")
                If Vero(g(i)("fuori")) Then
                    t &= ", fuori dal gioco"
                Else
                    t &= ", " & Euro(Num(g(i)("soldi"))) & ", su " & Txt(CasellaDi(s.Stato, Num(g(i)("posizione")))("nome"))
                    If Vero(g(i)("prigione")) Then t &= ", in prigione"
                End If
                parti.Add(t)
            Next
            Return String.Join(". ", parti) & "."
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.TestoGiocatori", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ opzioni (Alt)
    Private Sub ApriOpzioni(s As SessioneSchermo)
        Try
            If s.Dialoghi > 0 Then Return
            If s.ScelteMosse IsNot Nothing Then
                s.ScelteMosse = Nothing
                s.LivelloScelta = ""
                s.TornaAlleCarte = False
                s.Schermo.ImpostaScelte(Nothing)
                s.Schermo.Istruzione = s.Istruzione
            End If
            Dim voci As New List(Of VocePannelloGioco)()
            Dim azioni As New List(Of Action)()
            Dim aggiungi As Action(Of String, Action) = Sub(testo As String, azione As Action)
                                                            voci.Add(New VocePannelloGioco With {.Testo = testo})
                                                            azioni.Add(azione)
                                                        End Sub
            Dim chiudi As Action = Sub() s.Schermo.ChiudiPannelli()

            Dim mostraTesto As Action(Of String) = Sub(testo As String)
                                                     chiudi()
                                                     Informa(s, testo)
                                                     ProgrammaRitornoAlleCarte(s)
                                                 End Sub
            If s.Gioco = "monopoli" Then
                aggiungi("Cassa", Sub() mostraTesto(TestoCassa(s)))
                For Each g As JObject In If(TryCast(s.Stato("opzioni"), JArray), New JArray())
                    Dim gruppo As JObject = g
                    If Txt(g("id")) = "scambio" Then
                        aggiungi(Txt(g("testo")), Sub()
                                                      chiudi()
                                                      ProponiScambioAsync(s, gruppo)
                                                  End Sub)
                    Else
                        aggiungi(Txt(g("testo")), Sub() SceltaMultipla(s, gruppo))
                    End If
                Next
                aggiungi("Le mie proprietà", Sub()
                                                         chiudi()
                                                         ApriElencoProprieta(s)
                                                     End Sub)
                aggiungi("Giocatori", Sub()
                                                                chiudi()
                                                                ApriElencoGiocatori(s)
                                                            End Sub)
            ElseIf EItaliano(s.Gioco) Then
                aggiungi("La mia mano", Sub() mostraTesto(TestoManoBurraco(s)))
                aggiungi("Il tavolo", Sub() mostraTesto(Txt(Vista(s.Stato)("testo_tavolo"))))
                aggiungi("I punti", Sub() mostraTesto(Txt(Vista(s.Stato)("testo_punti"))))
            ElseIf s.Gioco = "battaglia" Then
                aggiungi("La situazione", Sub() mostraTesto(TestoSituazioneBattaglia(s)))
                aggiungi("Le navi nemiche affondate", Sub() mostraTesto(TestoAffondateBattaglia(s)))
            Else
                aggiungi("La mia mano", Sub() mostraTesto(TestoManoBurraco(s)))
                aggiungi("Il tavolo", Sub() mostraTesto(TestoTavoloBurraco(s)))
                aggiungi("Gli scarti", Sub() mostraTesto(TestoScartiBurraco(s)))
                aggiungi("I punti", Sub() mostraTesto(TestoPuntiBurraco(s)))
            End If
            ' gli altri giocatori veri: scrivere in privato con la chat cifrata, oppure bloccarli
            For Each g As JToken In Giocatori(s.Stato)
                If g.Type = JTokenType.Null OrElse Vero(g("computer")) Then Continue For
                Dim altro As String = Txt(g("nome"))
                If String.Equals(altro, GiochiClient.Condiviso.Nome, StringComparison.OrdinalIgnoreCase) Then Continue For
                aggiungi("Scrivi in privato a " & altro, Sub()
                                                                 chiudi()
                                                                 If MenuGiochi.ScriviInPrivato IsNot Nothing Then
                                                                     MenuGiochi.ScriviInPrivato.Invoke(altro)
                                                                 Else
                                                                     Leggi("La chat non è disponibile.")
                                                                     ProgrammaRitornoAlleCarte(s)
                                                                 End If
                                                             End Sub)
                aggiungi("Blocca " & altro, Sub()
                                                chiudi()
                                                Dim ignora As Task = BloccaGiocatoreAsync(s, altro)
                                            End Sub)
            Next
            aggiungi("Scrivi ai giocatori", Sub()
                                                                      chiudi()
                                                                      ScriviMessaggio(s)
                                                                  End Sub)
            aggiungi("Ultimi messaggi", Sub()
                                                                 chiudi()
                                                                 RileggiMessaggi(s)
                                                                 ProgrammaRitornoAlleCarte(s)
                                                             End Sub)
            aggiungi("Copia il link", Sub()
                                          chiudi()
                                          CopiaLinkPartita(s.D.Id)
                                          ProgrammaRitornoAlleCarte(s)
                                      End Sub)
            aggiungi("Interrompi il gioco", Sub()
                                                        chiudi()
                                                        s.Esito.TrySetResult(New RisultatoScelta With {.Tipo = "esci"})
                                                    End Sub)
            s.Schermo.ApriPannello("Opzioni", voci, False,
                                   Sub(scelte As List(Of Integer)) azioni(scelte(0)).Invoke(),
                                   Sub()
                                       If EItaliano(s.Gioco) AndAlso ConPrefisso(s, "gioca|").Count > 0 Then ApriCarte(s)
                                   End Sub)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ApriOpzioni: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Un gruppo di opzioni del server (ipoteca, riscatto, costruzione, vendita): si scelgono una o più proprietà con Spazio, Invio conferma e il pannello si chiude.</summary>
    Private Sub SceltaMultipla(s As SessioneSchermo, gruppo As JObject)
        Try
            Dim voci As New List(Of VocePannelloGioco)()
            Dim ids As New List(Of String)()
            For Each v As JObject In CType(gruppo("voci"), JArray)
                voci.Add(New VocePannelloGioco With {.Testo = Txt(v("testo"))})
                ids.Add(Txt(v("id")))
            Next
            Dim prefisso As String = Txt(gruppo("id"))
            s.Schermo.ApriPannello(Txt(gruppo("testo")), voci, True,
                                   Sub(scelte As List(Of Integer))
                                       s.Schermo.ChiudiPannelli()
                                       Dim numeri As List(Of String) = scelte.Select(Function(i) ids(i).Split("|"c)(1)).ToList()
                                       Esegui(s, prefisso & "|" & String.Join(",", numeri))
                                   End Sub)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.SceltaMultipla", ex)
            Throw
        End Try
    End Sub

    Private Async Function BloccaGiocatoreAsync(s As SessioneSchermo, nome As String) As Task
        Try
            Dim conferma As Boolean = Await InDialogoAsync(Of Boolean)(s, Function() WGDomanda.ChiediAsync("Vuoi bloccare " & nome & "?"))
            If Not conferma Then
                TornaAlleCarte(s)
                Return
            End If
            Await _client.BloccaAsync(nome)
            Informa(s, nome & " è bloccato.")
            ProgrammaRitornoAlleCarte(s)
        Catch ex As GiochiErrore
            Messaggio(ex.Message)
            TornaAlleCarte(s)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.BloccaGiocatoreAsync: " & ex.Message)
            TornaAlleCarte(s)
        End Try
    End Function

    Private Sub RileggiMessaggi(s As SessioneSchermo)
        Try
            If s.D.UltimiMessaggi.Count = 0 Then
                Leggi("Non ci sono ancora messaggi.")
            Else
                Leggi(String.Join(" ", s.D.UltimiMessaggi.Skip(Math.Max(0, s.D.UltimiMessaggi.Count - 8))))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.RileggiMessaggi", ex)
            Throw
        End Try
    End Sub

    Private Async Sub ScriviMessaggio(s As SessioneSchermo)
        Try
            Dim testo As String = Await InDialogoAsync(Of String)(s, Function() ChiediTestoAsync("Messaggio per i giocatori, fino a 200 caratteri", ""))
            If String.IsNullOrWhiteSpace(testo) Then
                TornaAlleCarte(s)
                Return
            End If
            Await _client.ChatAsync(s.D.Id, testo.Trim())
            TornaAlleCarte(s)
        Catch ex As GiochiErrore
            Messaggio(ex.Message)
            TornaAlleCarte(s)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ScriviMessaggio: " & ex.Message)
            TornaAlleCarte(s)
        End Try
    End Sub

    ' ------------------------------------------------------------------ scambi
    Private Function PannelloAsync(s As SessioneSchermo, titolo As String, voci As List(Of VocePannelloGioco), multipla As Boolean, consentiVuoto As Boolean, Optional indice As Integer = 0) As Task(Of List(Of Integer))
        Try
            Dim esito As New TaskCompletionSource(Of List(Of Integer))()
            s.Schermo.ApriPannello(titolo, voci, multipla,
                                   Sub(scelte As List(Of Integer))
                                       s.Schermo.ChiudiPannello()
                                       esito.TrySetResult(scelte)
                                   End Sub,
                                   Sub() esito.TrySetResult(Nothing), indice, consentiVuoto)
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.PannelloAsync", ex)
            Throw
        End Try
    End Function

    Private Async Sub ProponiScambioAsync(s As SessioneSchermo, gruppo As JObject)
        Try
            Dim id As String = Await InDialogoAsync(Of String)(s, Function() ComponiScambioAsync(s, CType(gruppo("scambio"), JObject)))
            If Not String.IsNullOrEmpty(id) Then Esegui(s, id)
        Catch ex As Exception
            _logger.ScriviLog("MenuGiochi.ProponiScambioAsync: " & ex.ToString())
            Messaggio("Non riesco a preparare lo scambio.")
        End Try
    End Sub

    ''' <summary>Compone uno scambio: si sceglie con chi, poi un menu con quattro voci (proprietà da richiedere, proprietà da inviare, denaro da richiedere,
    ''' denaro da inviare) e "Proponi scambio". Restituisce la mossa nello stesso formato di sempre (il server non cambia), o Nothing se si annulla.</summary>
    Private Async Function ComponiScambioAsync(s As SessioneSchermo, dati As JObject) As Task(Of String)
        Try
            Dim giocatori As JArray = CType(dati("giocatori"), JArray)
            Dim vociGiocatori As New List(Of VocePannelloGioco)()
            For Each g As JObject In giocatori
                vociGiocatori.Add(New VocePannelloGioco With {.Testo = Txt(g("nome")) & ", " & Euro(Num(g("soldi"))) & ", " & CType(g("proprieta"), JArray).Count.ToString() & If(CType(g("proprieta"), JArray).Count = 1, " proprietà scambiabile", " proprietà scambiabili")})
            Next
            Dim quale As List(Of Integer) = Await PannelloAsync(s, "Scambio, con chi", vociGiocatori, False, False)
            If quale Is Nothing Then Return Nothing
            Dim altro As JObject = CType(giocatori(quale(0)), JObject)
            Dim nomeAltro As String = Txt(altro("nome"))
            Dim mie As JArray = CType(dati("mie"), JArray)
            Dim sue As JArray = CType(altro("proprieta"), JArray)
            Dim mieiSoldi As Integer = Num(dati("miei_soldi"))
            Dim suoiSoldi As Integer = Num(altro("soldi"))

            Dim chiedi As New List(Of JObject)()
            Dim dai As New List(Of JObject)()
            Dim chiediSoldi As Integer = 0
            Dim daiSoldi As Integer = 0
            Dim posizione As Integer = 0

            Do
                Dim voci As New List(Of VocePannelloGioco)()
                voci.Add(New VocePannelloGioco With {.Testo = "Proprietà da chiedere"})
                voci.Add(New VocePannelloGioco With {.Testo = "Proprietà da dare"})
                voci.Add(New VocePannelloGioco With {.Testo = "Soldi da chiedere"})
                voci.Add(New VocePannelloGioco With {.Testo = "Soldi da dare"})
                voci.Add(New VocePannelloGioco With {.Testo = "Proponi scambio"})
                Dim scelta As List(Of Integer) = Await PannelloAsync(s, "Scambio con " & nomeAltro, voci, False, False, posizione)
                If scelta Is Nothing Then Return Nothing
                posizione = scelta(0)
                Select Case posizione
                    Case 0
                        Dim r As List(Of JObject) = Await ScegliProprietaAsync(s, "Proprietà da chiedere a " & nomeAltro, sue, chiedi)
                        If r IsNot Nothing Then chiedi = r
                    Case 1
                        Dim r As List(Of JObject) = Await ScegliProprietaAsync(s, "Proprietà da dare a " & nomeAltro, mie, dai)
                        If r IsNot Nothing Then dai = r
                    Case 2
                        Dim n As Integer = Await ChiediNumeroAsync("Quanti euro chiedi a " & nomeAltro & "? Da 0 a " & suoiSoldi.ToString(CultureInfo.InvariantCulture), 0, suoiSoldi, chiediSoldi)
                        If n >= 0 Then chiediSoldi = n
                    Case 3
                        Dim n As Integer = Await ChiediNumeroAsync("Quanti euro dai a " & nomeAltro & "? Da 0 a " & mieiSoldi.ToString(CultureInfo.InvariantCulture), 0, mieiSoldi, daiSoldi)
                        If n >= 0 Then daiSoldi = n
                    Case Else
                        If dai.Count = 0 AndAlso chiedi.Count = 0 AndAlso daiSoldi = 0 AndAlso chiediSoldi = 0 Then
                            Messaggio("Lo scambio è vuoto: non dai e non chiedi niente.")
                        Else
                            Dim riepilogo As String = "Proponi a " & nomeAltro & ": dai " & DescriviCose(s.Stato, dai, daiSoldi) & " e chiedi " & DescriviCose(s.Stato, chiedi, chiediSoldi) & ". Confermi?"
                            If Await WGDomanda.ChiediAsync(riepilogo) Then
                                Return "scambio|" & Txt(altro("indice")) & "|" & ElencoId(dai) & "|" & daiSoldi.ToString(CultureInfo.InvariantCulture) & "|" & ElencoId(chiedi) & "|" & chiediSoldi.ToString(CultureInfo.InvariantCulture)
                            End If
                        End If
                End Select
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ComponiScambioAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Function DescriviProprieta(proprieta As List(Of JObject)) As String
        Try
            If proprieta.Count = 0 Then Return "nessuna"
            Return String.Join(", ", proprieta.Select(Function(p) Txt(p("nome"))))
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.DescriviProprieta", ex)
            Throw
        End Try
    End Function

    Private Shared Function ElencoId(proprieta As List(Of JObject)) As String
        Try
            If proprieta.Count = 0 Then Return "-"
            Return String.Join(",", proprieta.Select(Function(p) Txt(p("id"))))
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ElencoId", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il nome di una proprietà con il colore (terreni) oppure "stazione" e "società".</summary>
    Private Shared Function NomeConColore(stato As JObject, p As JObject) As String
        Try
            Dim c As JObject = CasellaDi(stato, Num(p("id")))
            Dim t As String = Txt(p("nome"))
            If c Is Nothing Then Return t
            If Txt(c("colore")).Length > 0 Then Return t & ", " & Txt(c("colore"))
            If Txt(c("tipo")) = "stazione" Then Return t & ", stazione"
            If Txt(c("tipo")) = "societa" Then Return t & ", società"
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.NomeConColore", ex)
            Throw
        End Try
    End Function

    Private Shared Function DescriviCose(stato As JObject, proprieta As List(Of JObject), soldi As Integer) As String
        Try
            Dim parti As New List(Of String)()
            If proprieta.Count > 0 Then parti.Add(String.Join("; ", proprieta.Select(Function(p) NomeConColore(stato, p))))
            If soldi > 0 Then parti.Add(Euro(soldi))
            If parti.Count = 0 Then Return "niente"
            Return String.Join(" e ", parti)
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.DescriviCose", ex)
            Throw
        End Try
    End Function

    ''' <summary>Pannello a scelta multipla di proprietà (anche nessuna), con quelle già scelte spuntate. Invio ed Esc confermano.</summary>
    Private Async Function ScegliProprietaAsync(s As SessioneSchermo, titolo As String, disponibili As JArray, gia As List(Of JObject)) As Task(Of List(Of JObject))
        Try
            Dim scelte As New List(Of JObject)()
            If disponibili.Count = 0 Then
                Messaggio("Non ci sono proprietà da scambiare.")
                Return scelte
            End If
            Dim idGia As New HashSet(Of String)(gia.Select(Function(p) Txt(p("id"))))
            Dim voci As New List(Of VocePannelloGioco)()
            For Each p As JObject In disponibili
                voci.Add(New VocePannelloGioco With {.Testo = NomeConColore(s.Stato, p) & ", " & Euro(Num(p("prezzo"))), .Selezionata = idGia.Contains(Txt(p("id")))})
            Next
            ' Invio ed Esc fanno la stessa cosa: confermano le proprietà spuntate (anche nessuna) e si torna al menu dello scambio.
            Dim esito As New TaskCompletionSource(Of List(Of Integer))()
            Dim spuntate As Func(Of List(Of Integer)) = Function()
                                                            Dim l As New List(Of Integer)()
                                                            For i As Integer = 0 To voci.Count - 1
                                                                If voci(i).Selezionata Then l.Add(i)
                                                            Next
                                                            Return l
                                                        End Function
            s.Schermo.ApriPannello(titolo, voci, True,
                                   Sub(scelte2 As List(Of Integer))
                                       s.Schermo.ChiudiPannello()
                                       esito.TrySetResult(spuntate())
                                   End Sub,
                                   Sub() esito.TrySetResult(spuntate()), 0, True)
            Dim risposta As List(Of Integer) = Await esito.Task
            For Each i As Integer In risposta
                scelte.Add(CType(disponibili(i), JObject))
            Next
            Return scelte
        Catch ex As Exception
            RegistroErrori.Registra("MenuGiochi.ScegliProprietaAsync", ex)
            Throw
        End Try
    End Function

End Class
