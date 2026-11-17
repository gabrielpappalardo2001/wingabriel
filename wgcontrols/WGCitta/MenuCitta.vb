Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

' ============================================================================================
' MenuCitta - l'ingresso di Porto Franco, il gioco a città aperta di WinGabriel (tutto offline).
' Dal menu iniziale si sceglie una delle tre partite salvate (o se ne comincia una nuova), si fa la scuola guida (facoltativa) o si
' cambiano le impostazioni. Qui ci sono anche i menu che si aprono durante il gioco: pausa, telefono con le missioni, la storia fin qui,
' le scelte della trama, la meta del navigatore e i luoghi in cui si entra (casa e rifugi, officina, concessionario, attività da
' comprare, la base di un personaggio). Mentre un menu è aperto il mondo si ferma.
' ============================================================================================

Public NotInheritable Class MenuCitta

    Private Shared _inCorso As Boolean
    Private _mondo As Mondo
    Private _schermo As SchermoCitta
    Private _audio As MotoreAudio
    Private _imp As ImpostazioniCitta
    Private _fine As TaskCompletionSource(Of Boolean)
    Private _scuola As Boolean

    Private Shared ReadOnly MIGLIORIE As (Id As String, Nome As String, Prezzo As Integer)() = {
        ("motore", "Motore elaborato: più veloce", 2500),
        ("freni", "Freni nuovi: si ferma prima", 1500),
        ("blindatura", "Carrozzeria rinforzata: regge più urti", 3000),
        ("turbo", "Turbo: scatta meglio", 3500),
        ("gomme", "Gomme da corsa: tengono le curve", 1800)}

    Public Async Sub Avvia()
        Try
            If _inCorso Then Return
            _inCorso = True
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione("Porto Franco")
                Dim dati As DatiCitta
                Try
                    dati = DatiCitta.Carica()
                Catch ex As Exception
                    RegistroErrori.Registra("MenuCitta.Avvia (dati)", ex)
                    Messaggio("Non riesco a caricare la città di Porto Franco.")
                    Return
                End Try
                _imp = ImpostazioniCitta.Carica()
                Salvataggio.ConvertiVecchia()
                Do
                    Dim voci As New List(Of String)()
                    For p As Integer = 1 To Salvataggio.POSTI
                        Dim r As String = Salvataggio.Riassunto(p, dati.Missioni.Count)
                        voci.Add("Partita " & p.ToString() & ": " & If(r = "", "vuota, comincia una nuova partita", r))
                    Next
                    voci.Add("Scuola guida")
                    voci.Add("Impostazioni")
                    Dim scelta As Integer = Await ScegliAsync(dati.Nome, voci)
                    If scelta < 0 Then Return
                    If scelta < Salvataggio.POSTI Then
                        Dim posto As Integer = scelta + 1
                        Dim partita As Salvataggio
                        Dim nuova As Boolean = Not Salvataggio.Esiste(posto)
                        If Not nuova Then
                            Dim s2 As Integer = Await ScegliAsync("Partita " & posto.ToString(), New String() {"Continua", "Ricomincia da capo"})
                            If s2 < 0 Then Continue Do
                            If s2 = 1 Then
                                Salvataggio.Cancella(posto)
                                nuova = True
                            End If
                        End If
                        partita = If(nuova, New Salvataggio With {.Posto = posto}, Salvataggio.Carica(posto))
                        Await GiocaAsync(dati, partita, nuova, False)
                        Return
                    ElseIf scelta = Salvataggio.POSTI Then
                        Await GiocaAsync(dati, New Salvataggio(), True, True)
                    Else
                        Await ImpostazioniAsync(False)
                    End If
                Loop
            Catch ex As Exception
                RegistroErrori.Registra("MenuCitta.Avvia", ex)
                Messaggio("C'è stato un intoppo e il gioco si è chiuso.")
            Finally
                _inCorso = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Avvia", ex)
            Throw
        End Try
    End Sub

    Private Async Function GiocaAsync(dati As DatiCitta, partita As Salvataggio, nuova As Boolean, scuola As Boolean) As Task
        Try
            _scuola = scuola
            _audio = New MotoreAudio()
            Try
                _audio.Avvia()
                ApplicaAudio()
            Catch ex As Exception
                ' senza scheda audio si gioca lo stesso, con la sola voce
                RegistroErrori.Registra("MenuCitta.GiocaAsync (audio)", ex)
                _audio = Nothing
            End Try
            _mondo = New Mondo(dati, _audio, partita, _imp, scuola)
            AddHandler _mondo.Missioni.DaSalvare, AddressOf Salva
            AddHandler _mondo.Missioni.SceltaRichiesta, AddressOf FaiScelta
            AddHandler _mondo.Missioni.SceltaInMissione, AddressOf FaiSceltaInMissione
            AddHandler _mondo.Eventi.DomandaPosta, AddressOf FaiDomandaEvento
            ' le registrazioni si decodificano in sottofondo, così il gioco non si ferma la prima volta che servono
            Dim precarica As Task = Task.Run(Sub() Registrazioni.Precarica())
            If _mondo.Guida IsNot Nothing Then AddHandler _mondo.Guida.Conclusa, AddressOf FineScuola
            _schermo = New SchermoCitta(_mondo)
            AddHandler _schermo.PausaRichiesta, AddressOf Pausa
            AddHandler _schermo.TelefonoRichiesto, AddressOf Telefono
            AddHandler _schermo.MetaRichiesta, AddressOf ScegliMeta
            AddHandler _schermo.LuogoRichiesto, AddressOf EntraNelLuogo
            AddHandler _schermo.TastoCatturato, AddressOf TastoCatturato
            AddHandler _schermo.OpzioniRichieste, AddressOf OpzioniAlt
            _fine = New TaskCompletionSource(Of Boolean)()
            _schermo.Avvia()
            If Not scuola Then
                Dim inizio As String
                If nuova Then
                    inizio = dati.Nome & ". " & dati.Motto & " " & dati.Descrizione & " Sei davanti a casa, " & _mondo.Pos.Tratto.Strada.Nome & ". La tua utilitaria è parcheggiata qui."
                    Dim c As Capitolo = dati.Capitoli.FirstOrDefault()
                    If c IsNot Nothing Then inizio &= " Capitolo 1: " & c.Titolo & ". " & c.Racconto
                ElseIf StatoPartita.Ripristina(_mondo, partita.Stato) Then
                    inizio = dati.Nome & ". Riprendi la partita. " & _mondo.DoveSei() & " " & _mondo.OraETempo() & " Hai " & partita.Soldi.ToString() & " euro."
                Else
                    inizio = dati.Nome & ". Sei davanti a casa, " & _mondo.Pos.Tratto.Strada.Nome & ". " & _mondo.OraETempo() & " Hai " & partita.Soldi.ToString() & " euro."
                End If
                If IstruzioniAttive() Then inizio &= " F1 per i comandi."
                _schermo.Leggi(inizio)
                If Not nuova Then StatoPartita.Riprendi(_mondo)
            End If
            Await _fine.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.GiocaAsync", ex)
            Throw
        End Try
    End Function

    Private Sub ApplicaAudio()
        Try
            If _audio Is Nothing Then Return
            _audio.VolumeEffetti = _imp.Volume / 100.0F
            _audio.VolumeRadio = _imp.VolumeRadio / 100.0F
            _audio.Modo3D = _imp.Usa3D()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ApplicaAudio", ex)
            Throw
        End Try
    End Sub

    Private Sub Esci()
        Try
            If Not _scuola Then
                Try
                    Salva()
                Catch ex As Exception
                    RegistroErrori.Registra("MenuCitta.Esci (salvataggio)", ex)
                End Try
            End If
            If _mondo IsNot Nothing Then
                _mondo.Radio.Spegni()
                _mondo.Silenzio()
            End If
            If _audio IsNot Nothing Then _audio.Ferma()
            If _schermo IsNot Nothing Then _schermo.Chiudi()
            _schermo = Nothing
            If _fine IsNot Nothing Then _fine.TrySetResult(True)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Esci", ex)
            Throw
        End Try
    End Sub

    Private Sub Salva()
        Try
            If _scuola OrElse _mondo Is Nothing Then Return
            _mondo.PreparaSalvataggio()
            _mondo.Partita.Salva()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Salva", ex)
            Throw
        End Try
    End Sub

    Private Async Sub FineScuola()
        Try
            Await Task.Delay(6000)
            _schermo.Leggi("La scuola guida è finita. Torni al menu di Porto Franco.")
            Await Task.Delay(2500)
            Esci()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.FineScuola", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------ menu durante il gioco
    ''' <summary>Apre un menu fermando il mondo; quando si chiude il mondo riparte.</summary>
    Private Async Function MenuInPausaAsync(titolo As String, voci As IList(Of String), Optional posizione As Integer = 0) As Task(Of Integer)
        Try
            If _schermo IsNot Nothing Then _schermo.InPausa = True
            Try
                Return Await ScegliAsync(titolo, voci, posizione)
            Finally
                If _schermo IsNot Nothing Then _schermo.InPausa = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.MenuInPausaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Sub Pausa()
        Try
            Dim voci As New List(Of String) From {"Riprendi"}
            If Not _scuola Then
                voci.AddRange({"Telefono, i lavori", "Le tue cose", "Trofei", "Statistiche", "Le canzoni della radio", "Salva la partita"})
                If _mondo.Missioni.Attiva IsNot Nothing Then voci.Add("Rinuncia alla missione")
                If _mondo.Sfide.InCorso Then voci.Add("Rinuncia alla sfida")
            End If
            voci.Add("Impostazioni")
            voci.Add(If(_scuola, "Esci dalla scuola guida", "Esci dal gioco"))
            Dim scelta As Integer = Await MenuInPausaAsync("Pausa", voci)
            If scelta < 0 Then Return
            Select Case voci(scelta)
                Case "Telefono, i lavori" : Telefono()
                Case "Salva la partita"
                    Salva()
                    _schermo.Leggi("Partita salvata.")
                Case "Rinuncia alla missione" : _mondo.Missioni.Abbandona()
                Case "Rinuncia alla sfida" : _mondo.Sfide.Annulla("Lasci perdere.")
                Case "Le tue cose" : Await LeTueCoseAsync()
                Case "Trofei"
                    Dim trofei As List(Of String) = _mondo.Progressi.ElencoTrofei()
                    Await MenuInPausaAsync("Trofei: " & _mondo.Partita.Trofei.Count.ToString() & " su " & _mondo.Dati.Trofei.Count.ToString(), trofei)
                Case "Statistiche"
                    Await MenuInPausaAsync("Statistiche", _mondo.Progressi.Statistiche())
                Case "Le canzoni della radio"
                    Dim brani As List(Of String) = Canzoni.Elenco().Select(Function(b) b.Titolo & ", di " & b.Autore & ", " & b.Stile).ToList()
                    brani.Insert(0, "Brani di Kevin MacLeod, incompetech punto com, licenza Creative Commons Attribuzione 4.0")
                    Await MenuInPausaAsync("Le canzoni della radio", brani)
                Case "Impostazioni" : Await ImpostazioniAsync(True)
                Case Else : Esci()
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Pausa", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Alt: il menu di quello che si può fare adesso. Le voci cambiano con la situazione (a piedi, in macchina, vicino a un luogo, con una
    ''' meta, con la radio accesa...): niente di quello che non serve.
    ''' </summary>
    Private Async Sub OpzioniAlt()
        Try
            If _mondo Is Nothing OrElse _schermo Is Nothing OrElse _schermo.InPausa Then Return
            Dim m As Mondo = _mondo
            Dim voci As New List(Of String)()
            Dim azioni As New List(Of Action)()
            Dim agg As Action(Of String, Action) = Sub(testo As String, azione As Action)
                                                       voci.Add(testo)
                                                       azioni.Add(azione)
                                                   End Sub
            If m.Barca IsNot Nothing Then
                agg("Situazione", Sub() _schermo.Leggi(m.Barca.Descrizione()))
                agg("Pausa", Sub() Pausa())
            Else
                Dim fermo As Boolean = m.APiedi OrElse Math.Abs(m.Velocita) < 1.5
                Dim l As Luogo = m.LuogoVicino()
                agg("Dove sono", Sub() _schermo.Esegui("dove"))
                If Not _scuola AndAlso m.Eventi.DomandaAperta Then agg(m.Eventi.EtichettaDomanda, Sub() m.Eventi.RipetiDomanda())
                If Not _scuola Then
                    If m.APiedi Then
                        If m.Veicoli.Any(Function(v) v.Ruolo <> RuoloVeicolo.Polizia AndAlso v.Ruolo <> RuoloVeicolo.Relitto AndAlso m.DistanzaDa(v.X, v.Y) < 9) Then agg("Sali", Sub() _schermo.Esegui("sali"))
                        If m.VeicoliVicini(70).Count > 0 Then agg("Veicoli vicini", Sub() VeicoliViciniMenu())
                        If m.Stelle = 0 Then agg("Borseggia", Sub() m.Furti.Borseggia())
                    ElseIf fermo Then
                        agg("Scendi", Sub() _schermo.Esegui("sali"))
                        If Not m.Veicolo.Tuo AndAlso m.Stelle = 0 Then agg("Manda a casa", Sub() m.MandaACasa())
                    End If
                    If l IsNot Nothing AndAlso fermo Then
                        agg("Entra", Sub() EntraNelLuogo(l))
                        If l.Tipo = "casa" OrElse l.Tipo = "officina" OrElse (l.Tipo = "rifugio" AndAlso m.Partita.Proprieta.Contains(l.Id)) Then agg("Officina", Sub() MieMacchineAvvia())
                    End If
                End If
                If Not m.APiedi Then
                    agg("Veicolo", Sub() _schermo.Esegui("veicolo"))
                    agg("Inversione", Sub() _schermo.Esegui("frenoamano"))
                    If Not _scuola AndAlso fermo AndAlso l IsNot Nothing AndAlso (l.Tipo = "casa" OrElse (l.Tipo = "rifugio" AndAlso m.Partita.Proprieta.Contains(l.Id))) Then agg("Garage", Sub() _schermo.Esegui("garage"))
                    agg("Radio", Sub() _schermo.Esegui("radio"))
                    If m.Radio.Descrizione() <> "" Then agg("Radio spenta", Sub() m.Radio.Spegni())
                    agg("Radar", Sub() _schermo.Esegui("radar"))
                End If
                Dim destinazione As Luogo = m.MetaAttiva()
                If destinazione IsNot Nothing AndAlso m.DistanzaDa(destinazione.X, destinazione.Y) < 60 Then agg("Sono arrivato", Sub() m.ConfermaArrivo())
                agg("Luoghi vicini", Sub() _schermo.Esegui("luoghi"))
                agg("Chi c'è intorno", Sub() _schermo.Esegui("intorno"))
                agg("Meta", Sub() _schermo.Esegui("meta"))
                If m.Meta IsNot Nothing Then
                    agg("Indicazioni", Sub() _schermo.Esegui("navigatore"))
                    agg("Meta annullata", Sub() _schermo.Esegui("spegni"))
                End If
                If Not _scuola Then
                    If m.Missioni.Attiva IsNot Nothing Then
                        agg("Obiettivo", Sub() _schermo.Esegui("missione"))
                    Else
                        agg("Telefono", Sub() Telefono())
                    End If
                    If m.Lavori.Attivo Then
                        agg("Lavoro finito", Sub() _schermo.Esegui("lavoro"))
                    ElseIf Not m.APiedi Then
                        agg(If(m.Partita.LavoroSospeso IsNot Nothing, "Lavoro sospeso", "Lavoro"), Sub() _schermo.Esegui("lavoro"))
                    End If
                    If m.Vita.DescriviBanda() <> "" Then agg("Banda", Sub() _schermo.Esegui("banda"))
                    agg("Stato", Sub() _schermo.Esegui("stato"))
                    agg("Le tue cose", Sub() LeTueCoseAvvia())
                End If
                agg("Ora", Sub() _schermo.Esegui("ora"))
                agg("Messaggi", Sub() _schermo.Esegui("rileggi"))
                agg("Pausa", Sub() Pausa())
            End If
            Dim scelta As Integer = Await MenuInPausaAsync("Opzioni", voci)
            If scelta >= 0 Then azioni(scelta).Invoke()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.OpzioniAlt", ex)
        End Try
    End Sub

    Private Async Sub LeTueCoseAvvia()
        Try
            Await LeTueCoseAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.LeTueCoseAvvia", ex)
        End Try
    End Sub

    Private Async Sub MieMacchineAvvia()
        Try
            Await MieMacchineAsync()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.MieMacchineAvvia", ex)
        End Try
    End Sub

    ''' <summary>I veicoli fermi qui intorno: si sceglie e ci si va a sedere sopra (il furto semplice).</summary>
    Private Async Sub VeicoliViciniMenu()
        Try
            Dim m As Mondo = _mondo
            Dim lista As List(Of Veicolo) = m.VeicoliVicini(70)
            If lista.Count = 0 Then
                _schermo.Leggi("Qui intorno non c'è nessun veicolo fermo.")
                Return
            End If
            Dim voci As List(Of String) = lista.Select(Function(v) Char.ToUpper(v.Tipo.Nome(0)) & v.Tipo.Nome.Substring(1) & If(v.Tuo, ", tuo", "") & ", " & Mappa.Metri(m.DistanzaDa(v.X, v.Y)) & " " & m.LatoA(v.X, v.Y)).ToList()
            Dim scelta As Integer = Await MenuInPausaAsync("Veicoli vicini", voci)
            If scelta >= 0 Then m.Ruba(lista(scelta))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.VeicoliViciniMenu", ex)
        End Try
    End Sub

    Private Async Sub Telefono()
        Try
            If _scuola Then Return
            If _mondo.Missioni.Attiva IsNot Nothing Then
                _schermo.Leggi("Sei già in missione: " & _mondo.Missioni.Obiettivo())
                Return
            End If
            ' prima i finali di capitolo, poi la trama, poi le storie di quartiere
            Dim missioni As List(Of Missione) = _mondo.Missioni.Disponibili().OrderBy(Function(m) If(m.Finale, 0, If(m.Storia, 2, 1))).ThenBy(Function(m) m.Capitolo).ThenBy(Function(m) m.Ordine).ToList()
            Dim voci As List(Of String) = missioni.Select(Function(m) If(m.Finale, "Finale del capitolo " & m.Capitolo.ToString() & ": ", If(m.Storia, "Storia di quartiere: ", "Capitolo " & m.Capitolo.ToString() & ": ")) &
                                                              m.Titolo & ", da " & m.Personaggio.Nome & ", " & m.Personaggio.Quartiere.Nome & ", " & m.Soldi.ToString() & " euro" &
                                                              If(_mondo.Partita.Ripresa.ContainsKey(m.Id), ", si riprende da dove eri", "")).ToList()
            voci.Add("Sfide a tempo")
            Dim scelta As Integer = Await MenuInPausaAsync("Telefono: " & missioni.Count.ToString() & " lavori, capitolo " & _mondo.Missioni.CapitoloAttuale().ToString(), voci)
            If scelta < 0 Then Return
            If scelta = missioni.Count Then
                Dim s2 As Integer = Await MenuInPausaAsync("Sfide a tempo", _mondo.Sfide.Elenco())
                If s2 < 0 Then Return
                If _mondo.Lavori.Attivo Then _mondo.Lavori.CominciaOFinisci()
                If _mondo.Economia.LavoroAttivo Then _mondo.Economia.Sospendi("Smetti di lavorare.")
                _mondo.Missioni.Scelta = Nothing
                _mondo.Sfide.Scegli(_mondo.Dati.Sfide(s2))
                Return
            End If
            If _mondo.Lavori.Attivo Then _mondo.Lavori.CominciaOFinisci()
            If _mondo.Economia.LavoroAttivo Then _mondo.Economia.Sospendi("Smetti di lavorare.")
            _mondo.Missioni.Scegli(missioni(scelta))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Telefono", ex)
        End Try
    End Sub

    ''' <summary>Una scelta della trama: si risponde da un menu (si ripropone finché non si sceglie).</summary>
    Private Async Sub FaiScelta(s As Scelta)
        Try
            Await Task.Delay(1500)
            Dim voci As List(Of String) = s.Opzioni.Select(Function(o) o.Testo).ToList()
            Dim titolo As String = If(s.Personaggio IsNot Nothing, s.Personaggio.Nome & ": ", "") & s.Domanda
            Dim scelta As Integer = -1
            While scelta < 0 AndAlso _schermo IsNot Nothing
                scelta = Await MenuInPausaAsync(titolo, voci)
            End While
            If scelta >= 0 Then _mondo.Missioni.Rispondi(s, s.Opzioni(scelta))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.FaiScelta", ex)
        End Try
    End Sub

    ''' <summary>Una scelta a metà missione: si risponde da un menu (si ripropone finché non si sceglie).</summary>
    Private Async Sub FaiSceltaInMissione(p As PassoMissione)
        Try
            Await Task.Delay(1200)
            Dim voci As List(Of String) = p.Opzioni.Select(Function(o) o.Testo).ToList()
            Dim scelta As Integer = -1
            While scelta < 0 AndAlso _schermo IsNot Nothing
                scelta = Await MenuInPausaAsync(p.Domanda, voci)
            End While
            If scelta >= 0 Then _mondo.Missioni.RispondiInMissione(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.FaiSceltaInMissione", ex)
        End Try
    End Sub

    ''' <summary>Un evento che chiede cosa fare (passante che vuole un passaggio, posto di blocco): si risponde da un menu.</summary>
    Private Async Sub FaiDomandaEvento(titolo As String, opzioni As List(Of String))
        Try
            Await Task.Delay(400)
            If _schermo Is Nothing OrElse _schermo.InPausa Then Return
            Dim scelta As Integer = Await MenuInPausaAsync(titolo, opzioni)
            If scelta >= 0 Then _mondo.Eventi.Rispondi(scelta)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.FaiDomandaEvento", ex)
        End Try
    End Sub

    ''' <summary>Le cose comprate e non ancora usate: si usano da qui.</summary>
    Private Async Function LeTueCoseAsync() As Task
        Try
            Dim p As Salvataggio = _mondo.Partita
            Dim voci As New List(Of String)()
            Dim azioni As New List(Of String)()
            For Each id As String In {"tanica", "attrezzi", "cappello"}
                Dim n As Integer = p.QuantiOggetti(id)
                If n <= 0 Then Continue For
                Dim nome As String = If(id = "tanica", "una tanica di benzina", NomeMerce(id))
                voci.Add("Usa " & nome & If(n > 1, ", ne hai " & n.ToString(), ""))
                azioni.Add(id)
            Next
            If Not _mondo.APiedi Then
                voci.Add("La merce a bordo")
                azioni.Add("carico")
            End If
            If p.SantinoFino > p.Minuti Then
                voci.Add("Il santino ti protegge ancora per " & CInt((p.SantinoFino - p.Minuti) / 60).ToString() & " ore")
                azioni.Add("")
            End If
            If voci.Count = 0 Then
                _schermo.Leggi("Non hai niente con te. Le cose si comprano nei bar, nei negozi e ai distributori.")
                Return
            End If
            Dim scelta As Integer = Await MenuInPausaAsync("Le tue cose", voci)
            If scelta < 0 Then Return
            Select Case azioni(scelta)
                Case "tanica" : _schermo.Leggi(_mondo.Economia.UsaTanica())
                Case "carico" : _schermo.Leggi(_mondo.Economia.DescriviCarico(_mondo.Veicolo))
                Case "attrezzi"
                    If _mondo.APiedi Then
                        _schermo.Leggi("Gli attrezzi servono per il veicolo su cui sei.")
                    Else
                        p.CambiaOggetti("attrezzi", -1)
                        Dim v As Veicolo = _mondo.Veicolo
                        v.Salute = Math.Min(_mondo.Robustezza(v), v.Salute + _mondo.Robustezza(v) * 0.4)
                        _schermo.Leggi("Con gli attrezzi rimetti a posto quello che puoi. " & _mondo.SaluteVeicolo())
                    End If
                Case "cappello"
                    If _mondo.Stelle = 0 Then
                        _schermo.Leggi("Nessuno ti cerca: tienilo per quando serve.")
                    ElseIf _mondo.Stelle > 3 Then
                        _schermo.Leggi("Con tutta questa polizia addosso un cappello non basta.")
                    ElseIf _mondo.Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso _mondo.DistanzaDa(x.X, x.Y) < 90) Then
                        _schermo.Leggi("La polizia è troppo vicina: ti vedrebbero cambiarti.")
                    Else
                        p.CambiaOggetti("cappello", -1)
                        _mondo.Stelle -= 1
                        _schermo.Leggi("Ti cambi cappello e occhiali: adesso ti cercano di meno. Ricercato: " & _mondo.TestoStelle() & ".")
                    End If
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.LeTueCoseAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il nome di una cosa che si compra (scritto da Gemini), dal suo effetto.</summary>
    Private Function NomeMerce(effetto As String) As String
        Try
            Dim m As Merce = _mondo.Dati.Merci.FirstOrDefault(Function(x) x.Effetto = effetto)
            Return If(m Is Nothing, effetto, m.Nome)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.NomeMerce", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeTipo(tipo As String) As String
        Try
            Select Case tipo
                Case "casa" : Return "Casa"
                Case "rifugio" : Return "Rifugi"
                Case "officina" : Return "Officine"
                Case "ospedale" : Return "Ospedali"
                Case "commissariato" : Return "Commissariati"
                Case "banca" : Return "Banche"
                Case "bar" : Return "Bar"
                Case "ristorante" : Return "Ristoranti"
                Case "pizzeria" : Return "Pizzerie"
                Case "negozio" : Return "Negozi"
                Case "garage" : Return "Garage"
                Case "porto" : Return "Porto"
                Case "stazione" : Return "Stazioni"
                Case "distributore" : Return "Distributori"
                Case "concessionario" : Return "Concessionari"
                Case "magazzino" : Return "Magazzini"
                Case "piazza" : Return "Piazze"
                Case "chiesa" : Return "Chiese"
                Case "mercato" : Return "Mercati"
                Case "albergo" : Return "Alberghi"
                Case "club" : Return "Locali"
                Case "parco" : Return "Parchi"
                Case "autoscuola" : Return "Autoscuola"
                Case "radio" : Return "Radio"
                Case Else : Return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(tipo)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.NomeTipo", ex)
            Throw
        End Try
    End Function

    Private Async Sub ScegliMeta()
        Try
            Dim tipi As List(Of String) = _mondo.Dati.Luoghi.Values.Select(Function(l) l.Tipo).Distinct().OrderBy(Function(t) If(t = "casa", "", NomeTipo(t))).ToList()
            Dim voci As New List(Of String)()
            Dim speciali As New List(Of Luogo)()
            Dim daMissione As Luogo = If(_mondo.Lavori.MetaAttuale, If(_mondo.Economia.MetaAttuale, _mondo.Missioni.LuogoDaRaggiungere))
            If daMissione IsNot Nothing Then
                voci.Add("La meta del lavoro in corso")
                speciali.Add(daMissione)
            End If
            For Each id As String In _mondo.Partita.Proprieta
                Dim l As Luogo = Nothing
                If _mondo.Dati.Luoghi.TryGetValue(id, l) Then
                    voci.Add("Tuo: " & l.Nome)
                    speciali.Add(l)
                End If
            Next
            ' l'auto parcheggiata più vicina (dopo un arresto, o a piedi lontano da casa)
            Dim auto As Veicolo = _mondo.Veicoli.Where(Function(v) v.Ruolo = RuoloVeicolo.Parcheggiato AndAlso Not v.Blocco).OrderBy(Function(v) _mondo.DistanzaDa(v.X, v.Y)).FirstOrDefault()
            If auto IsNot Nothing AndAlso _mondo.APiedi Then
                Dim l As New Luogo With {.Id = "auto", .Tipo = "auto", .Nome = auto.Tipo.ConArticolo() & If(auto.Tipo.Maschile, " parcheggiato", " parcheggiata"), .Tratto = auto.Pos.Tratto,
                                         .Distanza = auto.Pos.Distanza, .Quartiere = _mondo.Dati.QuartiereDi(auto.X, auto.Y), .Lato = "destra", .X = auto.X, .Y = auto.Y}
                voci.Add("L'auto parcheggiata più vicina, a " & Mappa.Metri(_mondo.DistanzaDa(auto.X, auto.Y)))
                speciali.Add(l)
            End If
            Dim primo As Integer = voci.Count
            voci.AddRange(tipi.Select(Function(t) NomeTipo(t)))
            Dim scelta As Integer = Await MenuInPausaAsync("Navigatore", voci)
            If scelta < 0 Then Return
            If scelta < primo Then
                _mondo.ImpostaMeta(speciali(scelta))
                Return
            End If
            Dim tipo As String = tipi(scelta - primo)
            Dim luoghi As List(Of Luogo) = _mondo.Dati.Luoghi.Values.Where(Function(l) l.Tipo = tipo).OrderBy(Function(l) _mondo.DistanzaDa(l.X, l.Y)).ToList()
            Dim nomi As List(Of String) = luoghi.Select(Function(l) l.Nome & ", " & l.Quartiere.Nome & ", " & Mappa.Metri(_mondo.DistanzaDa(l.X, l.Y))).ToList()
            Dim s2 As Integer = Await MenuInPausaAsync(NomeTipo(tipo), nomi)
            If s2 < 0 Then Return
            _mondo.ImpostaMeta(luoghi(s2))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ScegliMeta", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------ i luoghi
    Private Async Sub EntraNelLuogo(l As Luogo)
        Try
            If _scuola Then
                _schermo.Leggi(l.Nome & ". " & l.Descrizione)
                Return
            End If
            If _mondo.Barca IsNot Nothing Then Return
            ' se questo è il posto dove si doveva arrivare (missione, lavoro, consegna), si può dire di essere arrivati
            If _mondo.MetaAttiva() Is l Then
                Dim sceltaArrivo As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Sono arrivato", "Entra"})
                If sceltaArrivo < 0 Then Return
                If sceltaArrivo = 0 Then
                    _mondo.ConfermaArrivo()
                    Return
                End If
            End If
            Dim personaggio As Personaggio = _mondo.Dati.Personaggi.Values.FirstOrDefault(Function(p) p.Luogo Is l)
            If personaggio IsNot Nothing Then
                ' dal personaggio si può anche solo entrare nel suo locale
                Dim s0 As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Parla con " & personaggio.Nome, "Entra"})
                If s0 < 0 Then Return
                If s0 = 0 Then
                    Await DaPersonaggioAsync(personaggio)
                    Return
                End If
            End If
            Select Case l.Tipo
                Case "casa" : Await CasaAsync(l)
                Case "rifugio" : Await RifugioAsync(l)
                Case "officina" : Await OfficinaAsync(l)
                Case "concessionario" : Await ConcessionarioAsync(l)
                Case Else : Await ServiziAsync(l)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.EntraNelLuogo", ex)
        End Try
    End Sub

    ''' <summary>Cosa si compra in un tipo di luogo (gli effetti delle merci scritte da Gemini).</summary>
    Private Shared Function EffettiIn(tipo As String) As String()
        Try
            Select Case tipo
                Case "bar", "club", "albergo" : Return {"caffe", "panino", "gratta"}
                Case "ristorante", "pizzeria" : Return {"panino", "caffe"}
                Case "negozio", "mercato", "stazione" : Return {"giornale", "gratta", "attrezzi", "mappa", "cappello", "panino"}
                Case "chiesa" : Return {"santino"}
                Case "distributore" : Return {"caffe", "giornale", "attrezzi"}
                Case Else : Return New String() {}
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.EffettiIn", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Dentro un luogo qualsiasi: si sente il suo rumore e una frase di quello che succede lì, poi il menu di quello che ci si può fare
    ''' (comprare qualcosa, la banca, la benzina, il commercio, vendere un veicolo rubato, comprare l'attività).
    ''' </summary>
    Private Async Function ServiziAsync(l As Luogo) As Task
        Try
            Dim p As Salvataggio = _mondo.Partita
            Dim scene As List(Of String) = Nothing
            Dim frase As String = ""
            If _mondo.Dati.Scene.TryGetValue(l.Tipo, scene) AndAlso scene.Count > 0 Then frase = " " & scene(_mondo.Caso().Next(scene.Count))
            _mondo.Vita.Scena(l.Tipo)
            Try
                _schermo.Leggi(l.Nome & ". " & l.Descrizione & frase)
                Do
                    Dim voci As New List(Of String)()
                    Dim azioni As New List(Of Func(Of Task(Of String)))()
                    ' le cose da comprare
                    For Each effetto As String In EffettiIn(l.Tipo)
                        Dim m As Merce = _mondo.Dati.Merci.FirstOrDefault(Function(x) x.Effetto = effetto)
                        If m Is Nothing Then Continue For
                        voci.Add(Char.ToUpper(m.Nome(0)) & m.Nome.Substring(1) & ", " & m.Prezzo.ToString() & " euro")
                        azioni.Add(Function() Task.FromResult(CompraCosa(m)))
                    Next
                    Select Case l.Tipo
                        Case "distributore"
                            If Not _mondo.APiedi Then
                                Dim costo As Integer = _mondo.Economia.CostoPieno(_mondo.Veicolo)
                                voci.Insert(0, If(costo <= 0, "Il serbatoio è pieno", "Fai il pieno, " & costo.ToString() & " euro, benzina a " & CInt(_mondo.Veicolo.Benzina).ToString() & " su cento"))
                                azioni.Insert(0, Function() Task.FromResult(_mondo.Economia.Pieno(_mondo.Veicolo)))
                            End If
                            voci.Add("Una tanica di benzina di scorta, 45 euro")
                            azioni.Add(Function()
                                           If p.Soldi < 45 Then Return Task.FromResult("Non hai abbastanza soldi.")
                                           p.Soldi -= 45
                                           p.CambiaOggetti("tanica", 1)
                                           _mondo.Suona(MotoreAudio.Soldi(), 0.5F)
                                           Return Task.FromResult("Presa: la trovi tra le tue cose, nel menu di pausa.")
                                       End Function)
                        Case "banca"
                            voci.Add("Versa, in tasca hai " & p.Soldi.ToString() & " euro")
                            azioni.Add(Function() BancaAsync(True))
                            voci.Add("Preleva, sul conto hai " & p.Banca.ToString() & " euro")
                            azioni.Add(Function() BancaAsync(False))
                            voci.Add("Colpo in banca")
                            azioni.Add(Function() Task.FromResult(_mondo.Furti.ColpoInBanca(l)))
                        Case "garage"
                            voci.Add("Ordini del giorno")
                            azioni.Add(Function() OrdiniAsync())
                            If Not _mondo.APiedi AndAlso _mondo.Economia.ValoreRubato(_mondo.Veicolo) > 0 Then
                                voci.Add("Vendi " & _mondo.Veicolo.Tipo.ConArticolo() & " senza fare domande, " & _mondo.Economia.ValoreRubato(_mondo.Veicolo).ToString() & " euro")
                                azioni.Add(Function() Task.FromResult(_mondo.Economia.VendiRubato()))
                            End If
                    End Select
                    If FurtiCitta.HaUnaCassa(l.Tipo) AndAlso Not p.Proprieta.Contains(l.Id) Then
                        voci.Add("Svuota la cassa")
                        azioni.Add(Function() Task.FromResult(_mondo.Furti.SvuotaCassa(l)))
                    End If
                    If EconomiaCitta.SiCommercia(l) Then
                        voci.Add("Compra merce")
                        azioni.Add(Function() CommercioAsync(l, True))
                        voci.Add("Vendi merce")
                        azioni.Add(Function() CommercioAsync(l, False))
                        voci.Add("Chiedi dove conviene vendere")
                        azioni.Add(Function() Task.FromResult(String.Join(" ", _mondo.Economia.InVendita(l).Select(Function(x) _mondo.Economia.DoveConviene(x)))))
                    End If
                    If l.Prezzo > 0 Then
                        If p.Proprieta.Contains(l.Id) Then
                            voci.Add("È tuo: rende " & l.Rendita.ToString() & " euro al giorno")
                            azioni.Add(Function() Task.FromResult("Gli affari vanno bene."))
                        Else
                            voci.Add("Compra l'attività per " & l.Prezzo.ToString() & " euro: rende " & l.Rendita.ToString() & " euro al giorno")
                            azioni.Add(Function()
                                           Compra(l)
                                           Return Task.FromResult("")
                                       End Function)
                        End If
                    End If
                    If voci.Count = 0 Then Exit Do
                    Dim scelta As Integer = Await MenuInPausaAsync(l.Nome & ", hai " & p.Soldi.ToString() & " euro", voci)
                    If scelta < 0 Then Exit Do
                    Dim esito As String = Await azioni(scelta)()
                    If esito <> "" Then _schermo.Leggi(esito)
                    ' venduto il veicolo si è a piedi, fuori
                    If _mondo.APiedi AndAlso l.Tipo = "garage" Then Exit Do
                    Await Task.Delay(400)
                Loop
            Finally
                _mondo.Vita.Scena(Nothing)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ServiziAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Compra una cosa e ne applica l'effetto. Restituisce cosa dire.</summary>
    Private Function CompraCosa(m As Merce) As String
        Try
            Dim p As Salvataggio = _mondo.Partita
            If p.Soldi < m.Prezzo Then Return "Non hai abbastanza soldi."
            p.Soldi -= m.Prezzo
            _mondo.Suona(MotoreAudio.Registrato("cassa", MotoreAudio.Soldi()), 0.6F)
            Select Case m.Effetto
                Case "giornale"
                    Dim merci As List(Of MerceCommercio) = _mondo.Economia.Merci
                    Dim consiglio As String = If(merci.Count > 0, " Nella pagina degli affari: " & _mondo.Economia.DoveConviene(merci(_mondo.Caso().Next(merci.Count))), "")
                    Dim c As Capitolo = _mondo.Dati.Capitoli.FirstOrDefault(Function(x) x.Numero = _mondo.Missioni.CapitoloAttuale())
                    Return "Sfogli " & m.Nome & "." & If(c IsNot Nothing, " In cronaca: " & c.Racconto, "") & consiglio
                Case "gratta"
                    Dim dado As Double = _mondo.Caso().NextDouble()
                    Dim vincita As Integer = If(dado < 0.005, 2000, If(dado < 0.04, 100, If(dado < 0.3, 10, 0)))
                    If vincita = 0 Then Return "Gratti: niente, come al solito."
                    p.Guadagna(vincita)
                    _mondo.Suona(MotoreAudio.Successo(), 0.6F)
                    Return "Gratti e vinci " & vincita.ToString() & " euro!"
                Case "attrezzi", "cappello"
                    p.CambiaOggetti(m.Effetto, 1)
                    Return m.Descrizione & " Lo trovi tra le tue cose, nel menu di pausa."
                Case "santino"
                    p.SantinoFino = p.Minuti + 1440
                    Return m.Descrizione
                Case "mappa"
                    Dim n As Nascosto = _mondo.NascostoPiuVicino()
                    If n Is Nothing Then Return "La mappa non ti serve più: hai già trovato tutto."
                    _mondo.ImpostaMeta(New Luogo With {.Id = "nascosto", .Tipo = "nascosto", .Nome = "il posto segnato sulla mappa", .Tratto = n.Tratto, .Distanza = n.Distanza, .Quartiere = _mondo.Dati.QuartiereDi(n.X, n.Y), .Lato = "destra", .X = n.X, .Y = n.Y}, True)
                    Return "Sulla mappa c'è una croce a " & n.Tratto.Strada.Nome & ", " & _mondo.Dati.QuartiereDi(n.X, n.Y).Nome & ": il navigatore ti porta lì."
                Case Else
                    Return m.Descrizione
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.CompraCosa", ex)
            Throw
        End Try
    End Function

    ''' <summary>Versare o prelevare: si sceglie quanto.</summary>
    Private Async Function BancaAsync(versa As Boolean) As Task(Of String)
        Try
            Dim p As Salvataggio = _mondo.Partita
            Dim disponibile As Integer = If(versa, p.Soldi, p.Banca)
            If disponibile <= 0 Then Return If(versa, "Non hai niente da versare.", "Sul conto non c'è niente.")
            Dim tagli As List(Of Integer) = New Integer() {100, 500, 1000, 5000, 20000}.Where(Function(x) x < disponibile).ToList()
            tagli.Add(disponibile)
            Dim voci As List(Of String) = tagli.Select(Function(x) If(x = disponibile, "Tutto: " & x.ToString() & " euro", x.ToString() & " euro")).ToList()
            Dim scelta As Integer = Await MenuInPausaAsync(If(versa, "Quanto versi", "Quanto prelevi"), voci, voci.Count - 1)
            If scelta < 0 Then Return ""
            Dim quanto As Integer = tagli(scelta)
            If versa Then
                p.Soldi -= quanto
                p.Banca += quanto
            Else
                p.Banca -= quanto
                p.Soldi += quanto
            End If
            _mondo.Suona(MotoreAudio.Soldi(), 0.5F)
            Return _mondo.Dati.TestoACaso("economia", "banca", _mondo.Caso()) & " Adesso hai " & p.Soldi.ToString() & " euro in tasca e " & p.Banca.ToString() & " sul conto."
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.BancaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Comprare o vendere merce in un mercato, in un magazzino o al porto.</summary>
    Private Async Function CommercioAsync(l As Luogo, compra As Boolean) As Task(Of String)
        Try
            If _mondo.APiedi Then Return "La merce viaggia sul veicolo: torna qui in macchina, meglio se con un furgone."
            Dim v As Veicolo = _mondo.Veicolo
            Dim e As EconomiaCitta = _mondo.Economia
            If compra Then
                Dim merci As List(Of MerceCommercio) = e.InVendita(l)
                If merci.Count = 0 Then Return "Oggi non c'è niente."
                Dim voci As List(Of String) = merci.Select(Function(x) Char.ToUpper(x.Nome(0)) & x.Nome.Substring(1) & ", " & e.Prezzo(x, l.Quartiere).ToString() & " euro l'una" & If(x.Contrabbando, ", di contrabbando", "") & ". " & x.Descrizione).ToList()
                Dim s As Integer = Await MenuInPausaAsync("Compra merce: a bordo c'è posto per " & (EconomiaCitta.Capienza(v.Tipo) - EconomiaCitta.Caricate(v)).ToString(), voci)
                If s < 0 Then Return ""
                Dim merce As MerceCommercio = merci(s)
                Dim massimo As Integer = Math.Min(EconomiaCitta.Capienza(v.Tipo) - EconomiaCitta.Caricate(v), _mondo.Partita.Soldi \ Math.Max(1, e.Prezzo(merce, l.Quartiere)))
                If massimo <= 0 Then Return e.Compra(merce, l, 1)
                Dim quantita As List(Of Integer) = New Integer() {1, 2, 5, 10, 20}.Where(Function(x) x < massimo).ToList()
                quantita.Add(massimo)
                Dim s2 As Integer = Await MenuInPausaAsync("Quante " & merce.Nome, quantita.Select(Function(x) x.ToString() & ", " & (x * e.Prezzo(merce, l.Quartiere)).ToString() & " euro").ToList(), quantita.Count - 1)
                If s2 < 0 Then Return ""
                Return e.Compra(merce, l, quantita(s2))
            Else
                Dim mie As List(Of MerceCommercio) = e.Merci.Where(Function(x) v.Carico.ContainsKey(x.Id)).ToList()
                If mie.Count = 0 Then Return "Non hai merce a bordo."
                Dim voci As List(Of String) = mie.Select(Function(x) v.Carico(x.Id).ToString() & " " & x.Nome & ": qui le pagano " & e.PrezzoVendita(x, l).ToString() & " euro l'una, in tutto " & (v.Carico(x.Id) * e.PrezzoVendita(x, l)).ToString()).ToList()
                Dim s As Integer = Await MenuInPausaAsync("Vendi merce", voci)
                If s < 0 Then Return ""
                Return e.Vendi(mie(s), l)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.CommercioAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>A casa o in un rifugio tuo: ci si nasconde finché la polizia non smette di cercare (passano due ore).</summary>
    Private Function Nasconditi() As String
        Try
            If _mondo.Stelle = 0 Then Return "Nessuno ti cerca."
            If _mondo.Stelle > 3 Then Return "Ti cercano dappertutto: qui ti troverebbero. Prima semina un po' di volanti."
            If _mondo.Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso _mondo.DistanzaDa(x.X, x.Y) < 120) Then Return "La polizia è qui fuori: ti hanno visto entrare."
            _mondo.Ripulisci()
            _mondo.Partita.Minuti += 120
            Return "Resti chiuso dentro due ore, con le serrande abbassate. Quando esci non ti cerca più nessuno."
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Nasconditi", ex)
            Throw
        End Try
    End Function

    Private Async Function DaPersonaggioAsync(p As Personaggio) As Task
        Try
            If _mondo.Partita.Bloccati.Contains(p.Id) Then
                _schermo.Leggi(p.Nome & " non ti vuole più vedere.")
                Return
            End If
            Dim sue As List(Of Missione) = _mondo.Missioni.Disponibili().Where(Function(m) m.Personaggio Is p).ToList()
            If _mondo.Missioni.Attiva IsNot Nothing OrElse sue.Count = 0 Then
                _schermo.Leggi(p.Nome & ": " & p.Saluto & If(_mondo.Missioni.Attiva IsNot Nothing, " Prima finisci quello che stai facendo.", " Adesso non ho niente per te: torna più avanti."))
                Return
            End If
            _schermo.Leggi(p.Nome & ": " & p.Saluto)
            Dim voci As List(Of String) = sue.Select(Function(m) If(m.Finale, "Finale del capitolo: ", If(m.Storia, "Storia di quartiere: ", "")) & m.Titolo & ", " & m.Soldi.ToString() & " euro").ToList()
            Dim stima As Integer = _mondo.Partita.StimaDi(p.Id)
            Dim scelta As Integer = Await MenuInPausaAsync(p.Nome & If(stima <> 0, ", " & ProgressiCitta.DescriviStima(stima), ""), voci)
            If scelta < 0 Then Return
            If _mondo.Lavori.Attivo Then _mondo.Lavori.CominciaOFinisci()
            _mondo.Missioni.Scegli(sue(scelta))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.DaPersonaggioAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function CasaAsync(l As Luogo) As Task
        Try
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Salva la partita", "Officina", "Le tue proprietà", "Nasconditi dalla polizia"})
            If scelta = 3 Then _schermo.Leggi(Nasconditi())
            If scelta = 0 Then
                Salva()
                _schermo.Leggi("Partita salvata.")
            ElseIf scelta = 1 Then
                Await MieMacchineAsync()
            ElseIf scelta = 2 Then
                Dim mie As List(Of Luogo) = _mondo.Partita.Proprieta.Where(Function(id) _mondo.Dati.Luoghi.ContainsKey(id)).Select(Function(id) _mondo.Dati.Luoghi(id)).ToList()
                If mie.Count = 0 Then
                    _schermo.Leggi("Non hai ancora comprato niente.")
                Else
                    _schermo.Leggi("Hai: " & String.Join(", ", mie.Select(Function(x) x.Nome & If(x.Rendita > 0, ", che rende " & x.Rendita.ToString() & " euro al giorno", ""))) & ".")
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.CasaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function RifugioAsync(l As Luogo) As Task
        Try
            If _mondo.Partita.Proprieta.Contains(l.Id) Then
                Dim s As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Salva la partita", "Officina", "Nasconditi dalla polizia"})
                If s = 0 Then
                    Salva()
                    _schermo.Leggi("Partita salvata.")
                ElseIf s = 1 Then
                    Await MieMacchineAsync()
                ElseIf s = 2 Then
                    _schermo.Leggi(Nasconditi())
                End If
                Return
            End If
            If l.Prezzo <= 0 Then
                _schermo.Leggi(l.Nome & ". " & l.Descrizione)
                Return
            End If
            _schermo.Leggi(l.Nome & ". " & l.Descrizione)
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome & ", in vendita", New String() {"Compralo per " & l.Prezzo.ToString() & " euro: potrai salvare qui"})
            If scelta = 0 Then Compra(l)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.RifugioAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function AttivitaAsync(l As Luogo) As Task
        Try
            _schermo.Leggi(l.Nome & ". " & l.Descrizione)
            If _mondo.Partita.Proprieta.Contains(l.Id) Then
                _schermo.Leggi("È tuo: rende " & l.Rendita.ToString() & " euro al giorno.")
                Return
            End If
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome & ", in vendita", New String() {"Compralo per " & l.Prezzo.ToString() & " euro: rende " & l.Rendita.ToString() & " euro al giorno"})
            If scelta = 0 Then Compra(l)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.AttivitaAsync", ex)
            Throw
        End Try
    End Function

    Private Sub Compra(l As Luogo)
        Try
            If _mondo.Partita.Soldi < l.Prezzo Then
                _schermo.Leggi("Non hai abbastanza soldi: ne hai " & _mondo.Partita.Soldi.ToString() & ".")
                Return
            End If
            _mondo.Partita.Soldi -= l.Prezzo
            _mondo.Partita.Proprieta.Add(l.Id)
            _mondo.Suona(MotoreAudio.Soldi(), 0.7F)
            Salva()
            _schermo.Leggi("Fatto: " & l.Nome & " è tuo.")
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Compra", ex)
            Throw
        End Try
    End Sub

    Private Async Function OfficinaAsync(l As Luogo) As Task
        Try
            If _mondo.APiedi Then
                Await MieMacchineAsync()
                Return
            End If
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Questa macchina", "Le mie macchine"})
            If scelta = 0 Then
                Await ServizioMacchinaAsync(_mondo.Veicolo, l.Nome)
            ElseIf scelta = 1 Then
                Await MieMacchineAsync()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.OfficinaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>L'officina delle macchine tue: si sceglie quale (ovunque sia parcheggiata) e si fa quello che serve.</summary>
    Private Async Function MieMacchineAsync() As Task
        Try
            Do
                Dim mie As List(Of Veicolo) = _mondo.Veicoli.Where(Function(v) v.Tuo AndAlso v.Ruolo <> RuoloVeicolo.Relitto).ToList()
                If mie.Count = 0 Then
                    _schermo.Leggi("Non hai ancora nessuna macchina tua.")
                    Return
                End If
                Dim voci As List(Of String) = mie.Select(Function(v) Char.ToUpper(v.Tipo.Nome(0)) & v.Tipo.Nome.Substring(1) & ", " & CInt(Math.Max(0, v.Salute) / _mondo.Robustezza(v) * 100).ToString() & " per cento, benzina " & CInt(v.Benzina).ToString() & If(v Is _mondo.Veicolo, ", ci sei sopra", "")).ToList()
                Dim scelta As Integer = Await MenuInPausaAsync("Officina, hai " & _mondo.Partita.Soldi.ToString() & " euro", voci)
                If scelta < 0 Then Return
                Await ServizioMacchinaAsync(mie(scelta), "Officina")
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.MieMacchineAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>I lavori su una macchina: riparare, fare il pieno, riverniciare e le migliorie (queste solo sulle tue).</summary>
    Private Async Function ServizioMacchinaAsync(v As Veicolo, titolo As String) As Task
        Try
            Do
                Dim danno As Double = Math.Max(0, _mondo.Robustezza(v) - v.Salute)
                Dim costoRiparazione As Integer = CInt(Math.Ceiling(danno * 6 / 10)) * 10
                Dim costoPieno As Integer = _mondo.Economia.CostoPieno(v)
                Dim voci As New List(Of String)()
                Dim azioni As New List(Of String)()
                If costoRiparazione > 0 Then
                    voci.Add("Ripara, " & costoRiparazione.ToString() & " euro")
                    azioni.Add("ripara")
                End If
                If costoPieno > 0 Then
                    voci.Add("Pieno, " & costoPieno.ToString() & " euro")
                    azioni.Add("pieno")
                End If
                voci.Add("Vernice, 500 euro")
                azioni.Add("vernice")
                If v.Tuo Then
                    For Each mg In MIGLIORIE
                        If Not _mondo.Partita.HaMiglioria(v.Tipo.Id, mg.Id) Then
                            voci.Add(mg.Nome & ", " & mg.Prezzo.ToString() & " euro")
                            azioni.Add(mg.Id)
                        End If
                    Next
                End If
                Dim scelta As Integer = Await MenuInPausaAsync(Char.ToUpper(v.Tipo.Nome(0)) & v.Tipo.Nome.Substring(1) & ", hai " & _mondo.Partita.Soldi.ToString() & " euro", voci)
                If scelta < 0 Then Return
                Dim azione As String = azioni(scelta)
                Dim prezzo As Integer = If(azione = "ripara", costoRiparazione, If(azione = "pieno", costoPieno, If(azione = "vernice", 500, MIGLIORIE.First(Function(x) x.Id = azione).Prezzo)))
                If _mondo.Partita.Soldi < prezzo Then
                    _schermo.Leggi("Non hai abbastanza soldi.")
                    Continue Do
                End If
                _mondo.Partita.Soldi -= prezzo
                _mondo.Suona(MotoreAudio.Soldi(), 0.6F)
                Select Case azione
                    Case "ripara"
                        v.Salute = _mondo.Robustezza(v)
                        _schermo.Leggi("Fatto: come nuova.")
                    Case "pieno"
                        v.Benzina = 100
                        _schermo.Leggi("Serbatoio pieno.")
                    Case "vernice"
                        _mondo.Ripulisci()
                        _schermo.Leggi("Una mano di vernice e via: la polizia non ti cerca più.")
                    Case Else
                        _mondo.Partita.AggiungiMiglioria(v.Tipo.Id, azione)
                        If azione = "blindatura" Then v.Salute = _mondo.Robustezza(v)
                        Salva()
                        _schermo.Leggi("Fatto: " & MIGLIORIE.First(Function(x) x.Id = azione).Nome.ToLowerInvariant() & ".")
                End Select
                Await Task.Delay(400)
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ServizioMacchinaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Gli ordini di oggi: se si è sull'auto giusta (rubata) la si consegna e si incassa.</summary>
    Private Async Function OrdiniAsync() As Task(Of String)
        Try
            Dim ordini As List(Of OrdineAuto) = _mondo.Furti.Ordini()
            Dim voci As List(Of String) = ordini.Select(Function(o) _mondo.Furti.DescriviOrdine(o)).ToList()
            Dim scelta As Integer = Await MenuInPausaAsync("Ordini del giorno", voci)
            If scelta < 0 Then Return ""
            Return _mondo.Furti.ConsegnaOrdine(ordini(scelta))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.OrdiniAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function ConcessionarioAsync(l As Luogo) As Task
        Try
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome, New String() {"Compra", "Vendi una tua macchina"})
            If scelta = 0 Then
                Await ConcessionarioCompraAsync(l)
            ElseIf scelta = 1 Then
                Await VendiTuaAsync()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ConcessionarioAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function VendiTuaAsync() As Task
        Try
            Dim lista As List(Of Veicolo) = _mondo.Furti.MacchineVendibili()
            If lista.Count = 0 Then
                _schermo.Leggi("Non hai macchine da vendere.")
                Return
            End If
            Dim voci As List(Of String) = lista.Select(Function(v) Char.ToUpper(v.Tipo.Nome(0)) & v.Tipo.Nome.Substring(1) & ", " & _mondo.Furti.ValoreTua(v).ToString() & " euro" & If(v Is _mondo.Veicolo, ", ci sei sopra", "")).ToList()
            Dim scelta As Integer = Await MenuInPausaAsync("Vendi, hai " & _mondo.Partita.Soldi.ToString() & " euro", voci)
            If scelta < 0 Then Return
            Dim esito As String = _mondo.Furti.VendiTua(lista(scelta))
            Salva()
            _schermo.Leggi(esito)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.VendiTuaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function ConcessionarioCompraAsync(l As Luogo) As Task
        Try
            Dim tipi As List(Of String) = _mondo.Dati.PrezziVeicoli.Keys.Where(Function(t) _mondo.Dati.Veicoli.ContainsKey(t)).ToList()
            Dim voci As List(Of String) = tipi.Select(Function(t) _mondo.Dati.Veicoli(t).Nome & ", " & _mondo.Dati.PrezziVeicoli(t).ToString() & " euro" & If(_mondo.Partita.Posseduti.Contains(t), ", ce l'hai già", "")).ToList()
            Dim scelta As Integer = Await MenuInPausaAsync(l.Nome & ", hai " & _mondo.Partita.Soldi.ToString() & " euro", voci)
            If scelta < 0 Then Return
            Dim tipo As String = tipi(scelta)
            Dim prezzo As Integer = _mondo.Dati.PrezziVeicoli(tipo)
            If _mondo.Partita.Soldi < prezzo Then
                _schermo.Leggi("Non hai abbastanza soldi.")
                Return
            End If
            _mondo.Partita.Soldi -= prezzo
            If Not _mondo.Partita.Posseduti.Contains(tipo) Then _mondo.Partita.Posseduti.Add(tipo)
            Dim nuova As Veicolo = _mondo.CreaVeicolo(tipo, l, RuoloVeicolo.Parcheggiato)
            nuova.Tuo = True
            Salva()
            _mondo.Suona(MotoreAudio.Soldi(), 0.7F)
            Dim n As String = nuova.Tipo.ConArticolo()
            _schermo.Leggi("Fatto! " & Char.ToUpper(n(0)) & n.Substring(1) & " è qui davanti, " & If(nuova.Tipo.Maschile, "tuo", "tua") & ".")
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ConcessionarioCompraAsync", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ impostazioni
    Private Async Function ImpostazioniAsync(inGioco As Boolean) As Task
        Try
            Do
                Dim cuffie As String = If(_imp.Audio3D = "automatico", If(ImpostazioniCitta.CuffieCollegate(), ", cuffie trovate", ", cuffie non trovate"), "")
                Dim voci As New List(Of String) From {
                    "Volume dei suoni: " & _imp.Volume.ToString(),
                    "Volume della radio: " & _imp.VolumeRadio.ToString(),
                    "Audio 3D in cuffia: " & If(_imp.Audio3D = "si", "sempre", If(_imp.Audio3D = "no", "mai", "automatico" & cuffie)),
                    "Svolte: " & If(_imp.GuidaAssistita, "il navigatore svolta da solo", "le scegli tu"),
                    "Annunci: " & If(_imp.AnnunciEssenziali, "solo gli essenziali", "tutti"),
                    "Traffico: " & _imp.Traffico,
                    "Polizia: " & _imp.Polizia,
                    "Orologio del gioco: " & _imp.Orologio}
                If inGioco Then voci.Add("Tasti")
                Dim scelta As Integer = Await MenuInPausaAsync("Impostazioni", voci)
                If scelta < 0 Then Exit Do
                Select Case scelta
                    Case 0, 1
                        Dim valori As List(Of String) = Enumerable.Range(0, 11).Select(Function(i) (i * 10).ToString()).ToList()
                        Dim attuale As Integer = If(scelta = 0, _imp.Volume, _imp.VolumeRadio) \ 10
                        Dim s2 As Integer = Await MenuInPausaAsync(If(scelta = 0, "Volume dei suoni", "Volume della radio"), valori, attuale)
                        If s2 >= 0 Then
                            If scelta = 0 Then _imp.Volume = s2 * 10 Else _imp.VolumeRadio = s2 * 10
                        End If
                    Case 2
                        Dim s2 As Integer = Await MenuInPausaAsync("Audio 3D in cuffia", New String() {"Automatico: solo se trovo le cuffie", "Sempre", "Mai"}, If(_imp.Audio3D = "si", 1, If(_imp.Audio3D = "no", 2, 0)))
                        If s2 >= 0 Then _imp.Audio3D = {"automatico", "si", "no"}(s2)
                    Case 3
                        Dim s2 As Integer = Await MenuInPausaAsync("Svolte", New String() {"Il navigatore svolta da solo", "Le svolte le scegli tu"}, If(_imp.GuidaAssistita, 0, 1))
                        If s2 >= 0 Then _imp.GuidaAssistita = s2 = 0
                    Case 4
                        Dim s2 As Integer = Await MenuInPausaAsync("Annunci", New String() {"Tutti", "Solo gli essenziali"}, If(_imp.AnnunciEssenziali, 1, 0))
                        If s2 >= 0 Then _imp.AnnunciEssenziali = s2 = 1
                    Case 5
                        Dim s2 As Integer = Await MenuInPausaAsync("Traffico (vale dalla prossima partita)", New String() {"Poco", "Normale", "Tanto"}, If(_imp.Traffico = "poco", 0, If(_imp.Traffico = "tanto", 2, 1)))
                        If s2 >= 0 Then _imp.Traffico = {"poco", "normale", "tanto"}(s2)
                    Case 6
                        Dim s2 As Integer = Await MenuInPausaAsync("Polizia", New String() {"Tranquilla: poche volanti, si semina presto", "Normale", "Severa: tante volanti, non molla"}, If(_imp.Polizia = "tranquilla", 0, If(_imp.Polizia = "severa", 2, 1)))
                        If s2 >= 0 Then _imp.Polizia = {"tranquilla", "normale", "severa"}(s2)
                    Case 7
                        Dim s2 As Integer = Await MenuInPausaAsync("Orologio del gioco", New String() {"Normale: un giorno dura 24 minuti", "Lento: un giorno dura 48 minuti", "Lentissimo: un giorno dura un'ora e mezza"}, If(_imp.Orologio = "lento", 1, If(_imp.Orologio = "lentissimo", 2, 0)))
                        If s2 >= 0 Then _imp.Orologio = {"normale", "lento", "lentissimo"}(s2)
                    Case 8
                        Dim nomi As List(Of String) = ImpostazioniCitta.Azioni.Select(Function(a) a.Nome & ": " & ImpostazioniCitta.NomeTasto(_imp.Tasti(a.Id))).ToList()
                        nomi.Add("Rimetti i tasti di partenza")
                        Dim s2 As Integer = Await MenuInPausaAsync("Tasti", nomi)
                        If s2 = nomi.Count - 1 Then
                            For Each a In ImpostazioniCitta.Azioni
                                _imp.Tasti(a.Id) = a.Tasto
                            Next
                        ElseIf s2 >= 0 Then
                            Dim a = ImpostazioniCitta.Azioni(s2)
                            _schermo.CatturaPer = a.Id
                            _schermo.Leggi("Premi il nuovo tasto per: " & a.Nome.ToLowerInvariant() & ".")
                            _imp.Salva()
                            Return
                        End If
                End Select
                _imp.Salva()
                ApplicaAudio()
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ImpostazioniAsync", ex)
            Throw
        End Try
    End Function

    Private Sub TastoCatturato(azione As String, k As Keys)
        Try
            Dim altra As String = _imp.AzioneDi(k)
            If altra <> "" AndAlso altra <> azione Then
                ' i due tasti si scambiano
                _imp.Tasti(altra) = _imp.Tasti(azione)
            End If
            _imp.Tasti(azione) = k
            _imp.Salva()
            Dim nome As String = ImpostazioniCitta.Azioni.First(Function(a) a.Id = azione).Nome.ToLowerInvariant()
            _schermo.Leggi("Fatto: " & ImpostazioniCitta.NomeTasto(k) & " per " & nome & "." & If(altra <> "" AndAlso altra <> azione, " Il tasto di prima passa all'altra azione.", ""))
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.TastoCatturato", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------ attrezzi
    Private Shared Function IstruzioniAttive() As Boolean
        Try
            Dim c As New WGConfig()
            Return c.EsisteId("GiochiIstruzioni") AndAlso String.Equals(If(c.LeggiValore("GiochiIstruzioni"), "").Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.IstruzioniAttive", ex)
            Return False
        End Try
    End Function

    ''' <summary>Un menu di scelte. Restituisce la posizione scelta, oppure -1 con Esc.</summary>
    Private Shared Function ScegliAsync(titolo As String, voci As IList(Of String), Optional posizioneIniziale As Integer = 0) As Task(Of Integer)
        Try
            Dim esito As New TaskCompletionSource(Of Integer)()
            Dim menu As New WGMenu()
            For i As Integer = 0 To voci.Count - 1
                Dim indice As Integer = i
                menu.aggiungi(voci(i), Sub()
                                           esito.TrySetResult(indice)
                                           Try
                                               menu.abbandona()
                                           Catch ex As Exception
                                               RegistroErrori.Registra("MenuCitta.ScegliAsync (chiusura)", ex)
                                           End Try
                                       End Sub)
            Next
            AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult(-1)
            If posizioneIniziale > 0 AndAlso posizioneIniziale < voci.Count Then menu.posiziona(posizioneIniziale)
            menu.avvia(titolo, True)
            Return esito.Task
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.ScegliAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            RegistroErrori.Registra("MenuCitta.Messaggio", ex)
            Throw
        End Try
    End Sub
End Class
