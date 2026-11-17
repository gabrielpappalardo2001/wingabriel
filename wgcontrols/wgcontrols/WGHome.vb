' ============================================================================================
' WGHome - la home di WinGabriel in modalità tablet: applicazioni su pagine da dieci e un dock
' ============================================================================================
'
' Come la schermata iniziale di un tablet: solo applicazioni (niente file, quelli stanno sulla
' scrivania del computer), dieci per pagina, con in alto la barra con l'ora, il Wi-Fi a tacche e la
' batteria (anche se è in carica), e in fondo il dock, uguale su tutte le pagine.
' Disposizione iniziale:
'   dock: Agenda e Rubrica;
'   pagina 1: Note, Mail, Web, Editor, Gestione file, Traduttore, Salute, Intelligenza artificiale,
'             Altre funzioni, Impostazioni;
'   pagina 2: Dati riservati, Aggiornamento, Informazioni sul programma.
' Il centro di controllo non è un'applicazione della home: si apre con tre dita in su dalla barra in alto.
' Le pagine si sfogliano con tre dita a sinistra e a destra, o con Pagina giù e Pagina su; al dock
' si arriva andando avanti dall'ultima applicazione della pagina. Doppio tocco o Invio apre; un
' colpo in su o Alt le opzioni (aggiungere, spostare, mettere nel dock, rinominare, togliere).
' Le applicazioni stanno in dati\gui\gui.sqlite; ognuna ricorda le voci da scegliere nei menu per
' aprirla ("Organizer > Agenda"). Tre dita in giù: il centro notifiche, in su il centro di
' controllo; tre tocchi con due dita il multitasking.
' In modalità tablet WGScrivania.Vai porta qui, così avvio, due dita in su e Control Alt D portano
' alla home; tornando al computer la home si chiude e torna la scrivania.
'
'   WGHome.Vai()
'   WGHome.AggiungiVoceDiMenu(voci)
' ============================================================================================
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Windows.Forms
Imports WGDb

Public NotInheritable Class WGHome

    Public Const NomeApplicazione As String = "Home"
    Public Const VociPerPagina As Integer = 10
    Public Const MassimoDock As Integer = 4
    Private Const Tabella As String = "HomeApplicazioni"
    Private Const IdDisposizione As String = "home_disposizione"
    Private Const VersioneDisposizione As String = "5"

    ' Nome mostrato e voci da scegliere dal menu applicazioni.
    Private Shared ReadOnly DisposizioneDock As String()() = {
        New String() {"Agenda", "Organizer > Agenda"},
        New String() {"Rubrica", "Organizer > Rubrica"}}
    Private Shared ReadOnly DisposizionePagine As String()() = {
        New String() {"Note", "Organizer > note"},
        New String() {"Mail", "Mail"},
        New String() {"Web", "Web"},
        New String() {"Editor", "Editor"},
        New String() {"Gestione file", "Gestione file"},
        New String() {"Media player", "Media player"},
        New String() {"Traduttore", "Traduttore"},
        New String() {"Salute", "Salute"},
        New String() {"Intelligenza artificiale", "Intelligenza artificiale"},
        New String() {"Altre funzioni", "Altre funzioni"},
        New String() {"Impostazioni", "Impostazioni"},
        New String() {"Dati riservati", "Organizer > Dati riservati"},
        New String() {"Aggiornamento", "Aggiornamento"},
        New String() {"Informazioni sul programma", "Aiuto > Informazioni"},
        New String() {"Manuale", "Aiuto > Manuale"}}

    Private Class App
        Public Id As String = ""
        Public Nome As String = ""
        Public Percorso As String = ""
        Public Ordine As Integer
        Public Dock As Boolean
    End Class

    Private Shared ReadOnly log As New Logger()
    Private Shared _db As WGDatabase
    Private Shared _griglia As WGGriglia

    Private Sub New()
    End Sub

#Region "Archivio"

    Private Shared Function Db() As WGDatabase
        If _db IsNot Nothing Then Return _db
        Try
            _db = WGGui.CreaTabella(Tabella, "Nome TEXT, Percorso TEXT, Ordine INTEGER, Dock INTEGER", "home", "home.db", "home")
        Catch ex As Exception
            log.ScriviLog("WGHome.Db: " & ex.Message)
        End Try
        Return _db
    End Function

    ' Tutte le applicazioni: prima quelle delle pagine, poi il dock, ognuna nel suo ordine.
    Private Shared Function Tutte() As List(Of App)
        Dim risultato As New List(Of App)()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return risultato
            For Each riga As DataRow In d.EseguiQuery("SELECT * FROM " & Tabella & " ORDER BY IFNULL(Dock,0), Ordine").Rows
                risultato.Add(New App With {.Id = Convert.ToString(riga(WGDatabase.NomeColonnaChiavePredefinita)),
                                            .Nome = Convert.ToString(riga("Nome")),
                                            .Percorso = If(IsDBNull(riga("Percorso")), "", Convert.ToString(riga("Percorso"))),
                                            .Ordine = If(IsDBNull(riga("Ordine")), 0, Convert.ToInt32(riga("Ordine"))),
                                            .Dock = Not IsDBNull(riga("Dock")) AndAlso Convert.ToInt32(riga("Dock")) <> 0})
            Next
        Catch ex As Exception
            log.ScriviLog("WGHome.Tutte: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function Inserisci(nome As String, percorso As String, dock As Boolean) As String
        Dim ordine As Integer = Tutte().Where(Function(a) a.Dock = dock).Select(Function(a) a.Ordine).DefaultIfEmpty(0).Max() + 1
        Return Db().InserisciRecord(Tabella, New Dictionary(Of String, Object) From {{"Nome", nome}, {"Percorso", percorso}, {"Ordine", ordine}, {"Dock", If(dock, 1, 0)}})
    End Function

    Private Shared Sub Aggiorna(a As App)
        Db().AggiornaRecord(Tabella, a.Id, New Dictionary(Of String, Object) From {{"Nome", a.Nome}, {"Percorso", a.Percorso}, {"Ordine", a.Ordine}, {"Dock", If(a.Dock, 1, 0)}})
    End Sub

    ' La prima volta (e con "Rimetti la disposizione iniziale") la home prende la disposizione iniziale.
    Private Shared Sub PreparaDisposizione(Optional forza As Boolean = False)
        Try
            Dim config As New WGConfig()
            Dim versione As String = config.LeggiValore(IdDisposizione)
            If Not forza AndAlso versione = VersioneDisposizione Then Return
            ' Dalla versione 3 cambia solo il manuale, dalla 5 il Media player, in fondo alle pagine: la disposizione scelta resta.
            If Not forza AndAlso (versione = "3" OrElse versione = "4") Then
                If versione = "3" AndAlso Not Tutte().Any(Function(a) String.Equals(a.Percorso, "Manuale", StringComparison.OrdinalIgnoreCase)) Then Inserisci("Manuale", "Manuale", False)
                If Not Tutte().Any(Function(a) String.Equals(a.Percorso, "Media player", StringComparison.OrdinalIgnoreCase)) Then Inserisci("Media player", "Media player", False)
                config.AggiungiValore(IdDisposizione, VersioneDisposizione)
                Return
            End If
            Db().EseguiNonQuery("DELETE FROM " & Tabella)
            For Each voce As String() In DisposizioneDock
                Inserisci(voce(0), voce(1), True)
            Next
            For Each voce As String() In DisposizionePagine
                Inserisci(voce(0), voce(1), False)
            Next
            config.AggiungiValore(IdDisposizione, VersioneDisposizione)
        Catch ex As Exception
            log.ScriviLog("WGHome.PreparaDisposizione: " & ex.Message)
        End Try
    End Sub

    ' Le applicazioni della disposizione iniziale e le altre voci del menu applicazioni (tranne Esci e
    ' Organizer, le cui voci ci sono già una per una), per rimettere quelle tolte.
    Private Shared Function VociDisponibili() As List(Of Tuple(Of String, String))
        Dim risultato As New List(Of Tuple(Of String, String))()
        Try
            For Each voce As String() In DisposizioneDock.Concat(DisposizionePagine)
                risultato.Add(Tuple.Create(voce(0), voce(1)))
            Next
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then
                For Each voce As String In contenitore.VociMenuApplicazioni()
                    If String.Equals(voce, "Esci", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(voce, "Organizer", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(voce, "Centro di controllo", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not risultato.Any(Function(r) String.Equals(r.Item2, voce, StringComparison.OrdinalIgnoreCase)) Then risultato.Add(Tuple.Create(voce, voce))
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGHome.VociDisponibili: " & ex.Message)
        End Try
        Return risultato
    End Function

#End Region

#Region "Apertura"

    ''' <summary>Torna alla home se è aperta, altrimenti la apre come applicazione (solo modalità smart).</summary>
    Public Shared Function Vai() As Boolean
        Try
            If Not WGModalita.Smart Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return False
            If contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New MethodInvoker(Sub() Vai()))
                Return True
            End If
            If contenitore.VaiAllApplicazioneConNome(NomeApplicazione) Then Return True
            _griglia = Nothing
            contenitore.ApriComeNuovaApplicazione(NomeApplicazione)
            Mostra(0)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGHome.Vai: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Chiude la home (tornando al computer, dove c'è la scrivania).</summary>
    Public Shared Sub Chiudi()
        Try
            Dim griglia As WGGriglia = _griglia
            _griglia = Nothing
            If griglia IsNot Nothing AndAlso Not griglia.IsDisposed Then griglia.Chiudi()
        Catch ex As Exception
            log.ScriviLog("WGHome.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property Davanti As Boolean
        Get
            Return _griglia IsNot Nothing AndAlso Not _griglia.IsDisposed AndAlso _griglia.Visible
        End Get
    End Property

    ' La nuova griglia entra nella stessa applicazione prima di chiudere la vecchia. Gli indici della
    ' griglia: prima le applicazioni delle pagine, poi quelle del dock.
    Private Shared Sub Mostra(indice As Integer)
        Try
            PreparaDisposizione()
            Dim elenco As List(Of App) = Tutte()
            Dim pagine As List(Of App) = elenco.Where(Function(a) Not a.Dock).ToList()
            Dim dock As List(Of App) = elenco.Where(Function(a) a.Dock).ToList()
            Dim precedente As WGGriglia = _griglia
            If precedente IsNot Nothing AndAlso precedente.IsDisposed Then precedente = Nothing
            ' Niente voci di solo testo: con le pagine vuote c'è la voce per aggiungere.
            Dim voci As List(Of String) = If(pagine.Count = 0, New List(Of String) From {"Aggiungi un'applicazione"}, pagine.Select(Function(a) a.Nome).ToList())
            Dim griglia As New WGGriglia("Home", voci, Math.Min(indice, voci.Count - 1)) With {
                .VociPerPagina = VociPerPagina, .BarraStatoInAlto = True, .VociDock = dock.Select(Function(a) a.Nome).ToList()}
            If indice >= voci.Count AndAlso dock.Count > 0 Then griglia.SelezionaDock(indice - voci.Count)
            _griglia = griglia
            Dim scelta As Func(Of App) = Function()
                                             Dim i As Integer = griglia.IndiceCorrente
                                             If i < voci.Count Then Return If(pagine.Count = 0, Nothing, pagine(i))
                                             Return If(i - voci.Count < dock.Count, dock(i - voci.Count), Nothing)
                                         End Function
            AddHandler griglia.ElementoScelto, Sub(i As Integer)
                                                   Dim a As App = scelta()
                                                   If a Is Nothing Then AggiungiApplicazione() Else Apri(a)
                                               End Sub
            AddHandler griglia.EscPremuto, Sub()
                                               Dim c As WGContenitore = WGContenitore.Istanza
                                               If c IsNot Nothing Then c.RichiediUscita()
                                           End Sub
            AddHandler griglia.OpzioniRichieste, Sub() MostraOpzioni(scelta())
            AddHandler griglia.VisibleChanged, Sub(s As Object, ev As EventArgs)
                                                   If Not griglia.Visible OrElse griglia.IsDisposed OrElse Not _aggiornaAlRitorno OrElse griglia.MenuAperto Then Return
                                                   _aggiornaAlRitorno = False
                                                   Dim idRitorno As String = _idAlRitorno
                                                   _idAlRitorno = ""
                                                   griglia.BeginInvoke(New MethodInvoker(Sub() If _griglia Is griglia Then Ridisegna(idRitorno)))
                                               End Sub
            AddHandler griglia.Comando, Sub(s As Object, richiesta As ComandoGrigliaEventArgs)
                                            If richiesta.Ctrl Then Return
                                            Select Case richiesta.Codice
                                                Case Keys.F1
                                                    MostraGuida()
                                                    richiesta.Gestito = True
                                                Case Keys.F2
                                                    Dim a As App = scelta()
                                                    If a IsNot Nothing Then Rinomina(a)
                                                    richiesta.Gestito = True
                                                Case Keys.Delete
                                                    Dim a As App = scelta()
                                                    If a IsNot Nothing Then Togli(a)
                                                    richiesta.Gestito = True
                                            End Select
                                        End Sub
            griglia.ImpostaBarraStato(VociBarraStato())
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If precedente IsNot Nothing AndAlso contenitore IsNot Nothing Then contenitore.MettiNellaStessaApplicazione(griglia, precedente)
            griglia.Avvia()
            If precedente IsNot Nothing Then precedente.Chiudi()
        Catch ex As Exception
            log.ScriviLog("WGHome.Mostra: " & ex.Message)
        End Try
    End Sub

    ' La home va ridisegnata quando torna davanti (qualcosa è cambiato mentre era dietro).
    Private Shared _aggiornaAlRitorno As Boolean
    Private Shared _idAlRitorno As String = ""

    Private Shared Sub Ridisegna(Optional idDaSelezionare As String = "")
        If _griglia Is Nothing OrElse _griglia.IsDisposed Then Return
        ' Cambiata mentre è dietro (Aggiungi alla home da un menu): ridisegnarla adesso la porterebbe davanti.
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If _griglia.MenuAperto OrElse (contenitore IsNot Nothing AndAlso WGContenitore.ApplicazioniSeparate AndAlso Not contenitore.EApplicazioneCorrente(_griglia)) Then
            _aggiornaAlRitorno = True
            _idAlRitorno = If(idDaSelezionare, "")
            Return
        End If
        Dim elenco As List(Of App) = Tutte()
        Dim ordinato As List(Of App) = elenco.Where(Function(a) Not a.Dock).Concat(elenco.Where(Function(a) a.Dock)).ToList()
        Dim posizione As Integer = ordinato.FindIndex(Function(a) a.Id = idDaSelezionare)
        ' Con le pagine vuote la voce "Aggiungi" occupa il primo posto prima del dock.
        If posizione >= 0 AndAlso Not elenco.Any(Function(a) Not a.Dock) Then posizione += 1
        Mostra(If(posizione < 0, Math.Max(0, _griglia.IndiceCorrente), posizione))
    End Sub

    ' In alto, come sui telefoni: ora, Wi-Fi a tacche e batteria.
    Private Shared Function VociBarraStato() As List(Of VoceStatoGriglia)
        Dim italiano As New Globalization.CultureInfo("it-IT")
        ' Lo stato di Wi-Fi e Bluetooth, per aprire subito il loro menu.
        WGPannelloControllo.PreparaStatoRadio()
        Dim voci As New List(Of VoceStatoGriglia) From {
            New VoceStatoGriglia() With {.Testo = Function() DateTime.Now.ToString("H:mm", italiano) & ", " & DateTime.Now.ToString("dddd d MMMM", italiano),
                                         .TestoBreve = Function() DateTime.Now.ToString("H:mm", italiano)},
            New VoceStatoGriglia() With {.Testo = Function() WGStatoDispositivo.Wifi().Descrizione,
                                         .TestoBreve = Function() If(WGStatoDispositivo.Wifi().Connesso, WGStatoDispositivo.Wifi().Rete, "Wi-Fi"),
                                         .Icona = Function() New IconaStato With {.Tipo = "wifi", .Livello = WGStatoDispositivo.Wifi().Tacche},
                                         .ApreFinestra = WGScrivania.MenuWifiBarra IsNot Nothing OrElse WGScrivania.ApriConnessione IsNot Nothing,
                                         .Invio = Sub()
                                                      If WGScrivania.MenuWifiBarra IsNot Nothing Then
                                                          WGScrivania.MenuWifiBarra.Invoke(WGGriglia.PerMenu)
                                                      ElseIf WGScrivania.ApriConnessione IsNot Nothing Then
                                                          WGPannelloControllo.MostraMenuRadio(False)
                                                      End If
                                                  End Sub},
            New VoceStatoGriglia() With {.Testo = Function() WGStatoDispositivo.Batteria().Descrizione,
                                         .TestoBreve = Function()
                                                           Dim b As StatoBatteria = WGStatoDispositivo.Batteria()
                                                           Return If(b.Presente, b.Percentuale & "%" & If(b.InCarica, " in carica", ""), "Corrente")
                                                       End Function,
                                         .Icona = Function()
                                                      Dim b As StatoBatteria = WGStatoDispositivo.Batteria()
                                                      Return If(b.Presente, New IconaStato With {.Tipo = "batteria", .Livello = b.Percentuale, .InCarica = b.InCarica}, Nothing)
                                                  End Function,
                                         .ApreFinestra = True, .Invio = Sub() WGStatoDispositivo.MostraBatteria()}
        }
        ' Senza batteria la voce della batteria, che è l'ultima, non c'è.
        If Not WGStatoDispositivo.Batteria().Presente Then voci.RemoveAt(voci.Count - 1)
        Return voci
    End Function

    ' L'applicazione si apre come applicazione a sé, ripetendo le scelte nei menu; se è già aperta ci si torna.
    Private Shared Async Sub Apri(a As App)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            If contenitore.VaiAllApplicazioneConNome(a.Nome) Then Return
            Dim voci As List(Of String) = a.Percorso.Split(New String() {WGScrivania.SeparatoreMenu}, StringSplitOptions.None).ToList()
            contenitore.ApriComeNuovaApplicazione(a.Nome, "menu:" & a.Percorso)
            If Not Await contenitore.EseguiPercorsoMenuAsync(voci) Then
                WGDomanda.Chiedi(a.Nome & " non si apre più: le voci del menu sono cambiate. Vuoi toglierla dalla home?", Sub() Togli(a, False))
            End If
        Catch ex As Exception
            log.ScriviLog("WGHome.Apri: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Modifiche"

    ''' <summary>Da qualsiasi menu ("Aggiungi alla home"): la voce, con le scelte per arrivarci dal menu applicazioni.</summary>
    Public Shared Sub AggiungiVoceDiMenu(voci As List(Of String))
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return
            Aggiungi(voci(voci.Count - 1), String.Join(WGScrivania.SeparatoreMenu, voci))
        Catch ex As Exception
            log.ScriviLog("WGHome.AggiungiVoceDiMenu: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Aggiungi(nome As String, percorso As String)
        If Tutte().Any(Function(a) String.Equals(a.Percorso, percorso, StringComparison.OrdinalIgnoreCase)) Then
            sintesivocale.GetInstance().LeggiTesto(nome & " è già sulla home")
            Return
        End If
        Dim nomeMostrato As String = If(nome.Length > 0, Char.ToUpper(nome(0)) & nome.Substring(1), nome)
        Dim id As String = Inserisci(nomeMostrato, percorso, False)
        Dim quante As Integer = Tutte().Where(Function(a) Not a.Dock).Count()
        sintesivocale.GetInstance().LeggiTesto(nomeMostrato & " aggiunta alla home, pagina " & ((quante - 1) \ VociPerPagina + 1))
        Ridisegna(id)
    End Sub

    Private Shared Sub AggiungiApplicazione()
        Try
            Dim presenti As HashSet(Of String) = New HashSet(Of String)(Tutte().Select(Function(a) a.Percorso), StringComparer.OrdinalIgnoreCase)
            Dim mancanti As List(Of Tuple(Of String, String)) = VociDisponibili().Where(Function(v) Not presenti.Contains(v.Item2)).ToList()
            If mancanti.Count = 0 Then
                Dim m As New WGMessaggi()
                m.Messaggio("Tutte le applicazioni sono già sulla home. Le voci degli altri menu si aggiungono dal loro menu opzioni, con Aggiungi alla home.")
                m.ShowDialog()
                Return
            End If
            Dim menu As New WGMenu()
            For Each voce As Tuple(Of String, String) In mancanti
                Dim scelta As Tuple(Of String, String) = voce
                menu.aggiungi(scelta.Item1, Sub(s, e) Aggiungi(scelta.Item1, scelta.Item2))
            Next
            menu.avvia("Aggiungi", True)
        Catch ex As Exception
            log.ScriviLog("WGHome.AggiungiApplicazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Rinomina(a As App)
        Dim campi As New Campi()
        campi.Aggiungi("Nuovo nome di " & a.Nome, "nome", True, False, a.Nome)
        AddHandler campi.Fine, Sub(s, e)
                                   Dim nome As String = If(campi.RecuperaValore("nome"), "").Trim()
                                   If nome = "" Then Return
                                   a.Nome = nome
                                   Aggiorna(a)
                                   sintesivocale.GetInstance().LeggiTesto("Rinominata " & nome)
                                   Ridisegna(a.Id)
                               End Sub
        campi.Avvia()
    End Sub

    Private Shared Sub Togli(a As App, Optional chiedi As Boolean = True)
        Dim togli As Action = Sub()
                                  Db().EliminaRecord(Tabella, a.Id)
                                  sintesivocale.GetInstance().LeggiTesto(a.Nome & " tolta dalla home")
                                  Ridisegna()
                              End Sub
        If chiedi Then
            WGDomanda.Chiedi("Togliere " & a.Nome & " dalla home? Resta nel menu applicazioni", togli)
        Else
            togli()
        End If
    End Sub

    ' Rinumera l'ordine di un gruppo (pagine o dock) come nell'elenco dato.
    Private Shared Sub Rinumera(gruppo As List(Of App))
        For i As Integer = 0 To gruppo.Count - 1
            If gruppo(i).Ordine <> i + 1 Then
                gruppo(i).Ordine = i + 1
                Aggiorna(gruppo(i))
            End If
        Next
    End Sub

    ' Sposta di un posto (direzione -1 o 1) o all'inizio della pagina successiva o precedente (±10), nel suo gruppo.
    Private Shared Sub Sposta(a As App, direzione As Integer)
        Try
            Dim gruppo As List(Of App) = Tutte().Where(Function(x) x.Dock = a.Dock).ToList()
            Dim posizione As Integer = gruppo.FindIndex(Function(x) x.Id = a.Id)
            Dim nuova As Integer
            If Math.Abs(direzione) = VociPerPagina Then
                Dim pagina As Integer = posizione \ VociPerPagina + Math.Sign(direzione)
                nuova = Math.Min(gruppo.Count - 1, pagina * VociPerPagina)
            Else
                nuova = posizione + direzione
            End If
            If posizione < 0 OrElse nuova < 0 OrElse nuova >= gruppo.Count OrElse nuova = posizione Then
                sintesivocale.GetInstance().LeggiTesto(If(direzione < 0, "Già al primo posto", "Già all'ultimo posto"))
                Return
            End If
            Dim spostata As App = gruppo(posizione)
            gruppo.RemoveAt(posizione)
            gruppo.Insert(nuova, spostata)
            Rinumera(gruppo)
            sintesivocale.GetInstance().LeggiTesto(a.Nome & If(a.Dock, ", nel dock, posto " & (nuova + 1), ", pagina " & (nuova \ VociPerPagina + 1) & ", posto " & (nuova Mod VociPerPagina + 1)))
            Ridisegna(a.Id)
        Catch ex As Exception
            log.ScriviLog("WGHome.Sposta: " & ex.Message)
        End Try
    End Sub

    ' Dal dock alle pagine (in fondo) e viceversa; nel dock ci stanno al massimo quattro applicazioni.
    Private Shared Sub CambiaDock(a As App)
        Try
            If Not a.Dock AndAlso Tutte().Where(Function(x) x.Dock).Count() >= MassimoDock Then
                Dim m As New WGMessaggi()
                m.Messaggio("Nel dock ci stanno al massimo " & MassimoDock & " applicazioni: prima togline una dal dock.")
                m.ShowDialog()
                Return
            End If
            a.Dock = Not a.Dock
            a.Ordine = Tutte().Where(Function(x) x.Dock = a.Dock AndAlso x.Id <> a.Id).Select(Function(x) x.Ordine).DefaultIfEmpty(0).Max() + 1
            Aggiorna(a)
            sintesivocale.GetInstance().LeggiTesto(a.Nome & If(a.Dock, " messa nel dock", " spostata nelle pagine"))
            Ridisegna(a.Id)
        Catch ex As Exception
            log.ScriviLog("WGHome.CambiaDock: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraOpzioni(a As App)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Aggiungi un'applicazione", Sub(s, e) AggiungiApplicazione())
            If a IsNot Nothing Then
                menu.aggiungi("Rinomina " & a.Nome & ", F2", Sub(s, e) Rinomina(a))
                menu.aggiungi("Sposta prima", Sub(s, e) Sposta(a, -1))
                menu.aggiungi("Sposta dopo", Sub(s, e) Sposta(a, 1))
                If Not a.Dock Then
                    menu.aggiungi("Sposta alla pagina successiva", Sub(s, e) Sposta(a, VociPerPagina))
                    menu.aggiungi("Sposta alla pagina precedente", Sub(s, e) Sposta(a, -VociPerPagina))
                End If
                menu.aggiungi(If(a.Dock, "Togli dal dock e metti nelle pagine", "Metti nel dock"), Sub(s, e) CambiaDock(a))
                menu.aggiungi("Togli " & a.Nome & " dalla home, Canc", Sub(s, e) Togli(a))
            End If
            menu.aggiungi("Rimetti la disposizione iniziale", Sub(s, e)
                                                                  WGDomanda.Chiedi("Rimettere la disposizione iniziale della home? Le modifiche fatte si perdono",
                                                                                   Sub()
                                                                                       PreparaDisposizione(True)
                                                                                       sintesivocale.GetInstance().LeggiTesto("Disposizione iniziale rimessa")
                                                                                       Ridisegna()
                                                                                   End Sub)
                                                              End Sub)
            menu.aggiungi("Centro di controllo, tre dita verso l'alto", Sub(s, e) WGPannelloControllo.Mostra())
            menu.aggiungi("Centro notifiche, Windows N", Sub(s, e)
                                                             Dim c As WGContenitore = WGContenitore.Istanza
                                                             If c IsNot Nothing Then c.MostraCentroNotifiche()
                                                         End Sub)
            menu.aggiungi("Multitasking, F10", Sub(s, e)
                                                   Dim c As WGContenitore = WGContenitore.Istanza
                                                   If c IsNot Nothing Then c.MostraElencoApplicazioni()
                                               End Sub)
            menu.aggiungi("Menu applicazioni", Sub(s, e)
                                                   Dim c As WGContenitore = WGContenitore.Istanza
                                                   If c IsNot Nothing Then c.VaiAlMenuIniziale()
                                               End Sub)
            menu.aggiungi("Guida della home, F1", Sub(s, e) MostraGuida())
            If WGContenitore.ManualeDisponibile Then menu.aggiungi("Manuale, Control F1", Sub(s, e) WGContenitore.ApriManuale())
            menu.aggiungi("Esci da WinGabriel", Sub(s, e)
                                                    Dim c As WGContenitore = WGContenitore.Istanza
                                                    If c IsNot Nothing Then c.RichiediUscita()
                                                End Sub)
            menu.avvia("Opzioni", True)
        Catch ex As Exception
            log.ScriviLog("WGHome.MostraOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Const TestoGuida As String =
        "Guida della home." & vbCrLf & vbCrLf &
        "In modalità tablet, con la tastiera staccata o ripiegata, la prima cosa che si vede è la home, come su un tablet: solo applicazioni, dieci per pagina. File e cartelle si usano con la gestione file, come al computer; la scrivania torna quando si riattacca la tastiera." & vbCrLf &
        "In alto c'è la barra con l'ora, il Wi-Fi con le tacche del segnale e la batteria, che dice anche se è in carica. In fondo c'è il dock, uguale su tutte le pagine, con agenda e rubrica." & vbCrLf &
        "Nella prima pagina ci sono note, posta, web, editor, gestione file, traduttore, salute, intelligenza artificiale, altre funzioni e impostazioni; nella seconda i dati riservati, l'aggiornamento, le informazioni sul programma e il manuale." & vbCrLf & vbCrLf &
        "Gesti:" & vbCrLf &
        "- un colpo a destra o a sinistra: applicazione successiva o precedente; dopo l'ultima della pagina si arriva al dock; toccare e scorrere col dito: si sente cosa c'è sotto;" & vbCrLf &
        "- doppio tocco: apre l'applicazione;" & vbCrLf &
        "- un colpo in su: le opzioni, per aggiungere, spostare, mettere nel dock, rinominare e togliere applicazioni;" & vbCrLf &
        "- tre dita a sinistra e a destra: pagina successiva e precedente;" & vbCrLf &
        "- tre dita in giù: centro notifiche; tre dita in su: centro di controllo, con Wi-Fi, Bluetooth, luminosità, volume e i comandi dei brani. Nei campi di testo e nel browser tre dita in su e in giù sono il rotore: lì i due centri si aprono partendo dalla barra in alto;" & vbCrLf &
        "- tre tocchi con due dita: multitasking, le applicazioni aperte; su un'applicazione un colpo in su fa dire Chiudi e il doppio tocco la chiude;" & vbCrLf &
        "- due dita in su: torna alla home da qualsiasi applicazione; due dita a destra e a sinistra: applicazione successiva e precedente." & vbCrLf & vbCrLf &
        "Tasti: frecce per spostarsi (in giù dall'ultima riga si va al dock), Pagina giù e Pagina su per le pagine, Invio per aprire, Alt per le opzioni, F2 per rinominare, Canc per togliere, Tab per la barra in alto, F6 o Windows A il centro di controllo a menu, Windows N il centro notifiche, F10 il multitasking." & vbCrLf & vbCrLf &
        "Aggiungere: dalle opzioni della home si rimettono le applicazioni tolte, e da qualsiasi menu, nel suo menu opzioni, c'è Aggiungi alla home."

    Private Shared Sub MostraGuida()
        Try
            If WGContenitore.ApriCapitolo("home") Then Return
            Dim campo As New WGCampo()
            campo.init("Guida della home", TestoGuida, True, True)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGHome.MostraGuida: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
