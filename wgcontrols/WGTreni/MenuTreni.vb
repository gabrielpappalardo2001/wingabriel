Imports System.Text
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' Il menu Orario treni e pullman: cerca viaggio (treni, pullman e FlixBus con prezzi e dettaglio da Trainline; se non risponde, gli
''' orari programmati di Trenitalia e Trenord con i cambi), partenze e arrivi con i binari (Trenitalia e Italo in tempo reale),
''' ricerca di un treno per numero.
''' </summary>
Public Class MenuTreni

    Private Class ContestoRicerca
        Public Orari As OrariTreni
        Public Ricerca As RicercaViaggi
        Public Origine As StazioneOrario
        Public Destinazione As StazioneOrario
        Public Partenza As DateTime
        Public Tipo As TipoTreni
        Public Cambi As Integer
    End Class

    Private Class SceltaTreno
        Public Testo As String
        Public Trenitalia As TrenoTrovato
        Public Italo As DettaglioTreno
    End Class

    Private Const IdConfigTipo As String = "TreniTipo"
    Private Const IdConfigCambi As String = "TreniCambi"
    Private Const IdConfigMezzi As String = "TreniMezzi"
    Private Const IdConfigPasseggeri As String = "TreniPasseggeri"

    Private Shared ReadOnly _log As New Logger()
    Private Shared _avvisiMostrati As Boolean
    Private Shared _ricerca As RicercaViaggi
    Private Shared _ricercaDi As OrariTreni

    Private _menu As WGMenu
    Private _tipo As TipoTreni = TipoTreni.Tutti
    Private _cambi As Integer = 3
    ''' <summary>Cosa cercare su Trainline: 0 treni e pullman, 1 solo treni, 2 solo pullman.</summary>
    Private _mezzi As Integer = 0
    Private _passeggeri As Integer = 1

    Public Sub New()
        Try
            Dim config As New WGConfig()
            Dim v As Integer
            If Integer.TryParse(config.LeggiValore(IdConfigTipo), v) AndAlso v >= 0 AndAlso v <= 3 Then _tipo = CType(v, TipoTreni)
            If Integer.TryParse(config.LeggiValore(IdConfigCambi), v) AndAlso v >= 0 AndAlso v <= 3 Then _cambi = v
            If Integer.TryParse(config.LeggiValore(IdConfigMezzi), v) AndAlso v >= 0 AndAlso v <= 2 Then _mezzi = v
            If Integer.TryParse(config.LeggiValore(IdConfigPasseggeri), v) AndAlso v >= 1 AndAlso v <= 9 Then _passeggeri = v
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.New: " & ex.Message)
        End Try
    End Sub

#Region "Menu principale"

    Public Sub Avvia()
        Try
            _menu = New WGMenu()
            _menu.aggiungi("Cerca viaggio", AddressOf CercaViaggio)
            _menu.aggiungi("Partenze e arrivi", AddressOf ChiediStazioneTabellone)
            _menu.aggiungi("Cerca treno per numero", AddressOf ChiediNumeroTreno)
            _menu.aggiungi("Ricerche recenti", AddressOf RicercheRecenti)
            _menu.aggiungi("Corse preferite", Sub(s, e) ElencoCorse(True))
            _menu.aggiungi("Corse recenti", Sub(s, e) ElencoCorse(False))
            _menu.aggiungi("Configurazione", AddressOf Configurazione)
            _menu.avvia("Orario treni e pullman")
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function NomeTipo(t As TipoTreni) As String
        Select Case t
            Case TipoTreni.AltaVelocita : Return "Solo alta velocità"
            Case TipoTreni.Veloci : Return "Senza regionali"
            Case TipoTreni.Regionali : Return "Solo regionali"
        End Select
        Return "Tutti i treni"
    End Function

    Private Shared Function NomeCambi(c As Integer) As String
        Select Case c
            Case 0 : Return "Nessun cambio"
            Case 1 : Return "Un cambio"
            Case 2 : Return "Fino a due cambi"
        End Select
        Return "Fino a tre cambi"
    End Function

    ''' <summary>
    ''' La configurazione: due campi menu (si sceglie da un elenco che si apre appena ci si arriva); Ctrl freccia su e giù passano da
    ''' un campo all'altro. Si salva alla fine dell'ultimo campo.
    ''' </summary>
    Private Sub Configurazione()
        Try
            Dim campi As New Campi()
            Dim tipi As New List(Of KeyValuePair(Of String, String))()
            For Each t As TipoTreni In New TipoTreni() {TipoTreni.Tutti, TipoTreni.AltaVelocita, TipoTreni.Veloci, TipoTreni.Regionali}
                tipi.Add(New KeyValuePair(Of String, String)(CInt(t).ToString(), NomeTipo(t)))
            Next
            Dim cambi As New List(Of KeyValuePair(Of String, String))()
            For c As Integer = 0 To 3
                cambi.Add(New KeyValuePair(Of String, String)(c.ToString(), NomeCambi(c)))
            Next
            campi.AggiungiScelta("Tipo di treni", "t", tipi, True, CInt(_tipo).ToString())
            campi.AggiungiScelta("Cambi", "c", cambi, True, _cambi.ToString())
            AddHandler campi.Fine, Sub() FineConfigurazione(campi)
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.Configurazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineConfigurazione(campi As Campi)
        Try
            Dim v As Integer
            If Integer.TryParse(campi.RecuperaIdScelta("t"), v) AndAlso v >= 0 AndAlso v <= 3 Then
                _tipo = CType(v, TipoTreni)
                Salva(IdConfigTipo, v.ToString())
            End If
            If Integer.TryParse(campi.RecuperaIdScelta("c"), v) AndAlso v >= 0 AndAlso v <= 3 Then
                _cambi = v
                Salva(IdConfigCambi, v.ToString())
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.FineConfigurazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub Salva(id As String, valore As String)
        Try
            Dim config As New WGConfig()
            config.AggiungiValore(id, valore)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.Salva: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Aiuti"

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.Messaggio: " & ex.Message)
        End Try
    End Sub

    Private Shared Function NuovaLista(titolo As String) As WGMenuLista
        Dim l As New WGMenuLista()
        l.invioNonConsentito = False
        l.multiselezioneConsentita = False
        l.impostaTitolo(titolo)
        AddHandler l.escPremuto, Sub() l.abbandona()
        Return l
    End Function

    Private Shared Function Ora(secondi As Integer) As String
        Dim s As Integer = ((secondi Mod 86400) + 86400) Mod 86400
        Return (s \ 3600).ToString("00") & ":" & ((s Mod 3600) \ 60).ToString("00")
    End Function

    Private Shared Function GiornoDi(secondi As Integer) As Integer
        Return CInt(Math.Floor(secondi / 86400.0))
    End Function

    Private Shared Function Durata(secondi As Integer) As String
        Dim minuti As Integer = secondi \ 60
        Dim h As Integer = minuti \ 60
        Dim m As Integer = minuti Mod 60
        If h = 0 Then Return m & If(m = 1, " minuto", " minuti")
        Dim ore As String = h & If(h = 1, " ora", " ore")
        If m = 0 Then Return ore
        Return ore & " e " & m & If(m = 1, " minuto", " minuti")
    End Function

    Private Shared Function NomeMessaggio(ex As Exception) As String
        Dim s As ServizioNonDisponibileException = TryCast(ex, ServizioNonDisponibileException)
        If s IsNot Nothing Then Return "Il servizio di " & s.Fonte & " non risponde. Riprova tra poco."
        Return "Non riesco a leggere i dati dei treni. Controlla la connessione a Internet e riprova."
    End Function

    Private Shared Function LeggiData(testo As String) As DateTime?
        Try
            Dim p As New WGUtilsDataOra()
            Dim r As DateTime? = Nothing
            If p.AnalizzaData(testo, r) AndAlso r.HasValue Then Return r.Value.Date
            Dim d As DateTime
            If DateTime.TryParse(testo, Globalization.CultureInfo.GetCultureInfo("it-IT"), Globalization.DateTimeStyles.None, d) Then Return d.Date
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.LeggiData: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Shared Function LeggiOrario(testo As String) As TimeSpan?
        Try
            Dim p As New WGUtilsDataOra()
            Dim r As DateTime? = Nothing
            If p.AnalizzaOrario(testo, r) AndAlso r.HasValue Then Return r.Value.TimeOfDay
            Dim t As TimeSpan
            If TimeSpan.TryParse(testo, t) Then Return t
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.LeggiOrario: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Async Function PreparaOrariAsync() As Task(Of OrariTreni)
        Try
            Dim orari As OrariTreni = Await WGProcessoInCorso.EseguiAsync(Of OrariTreni)("Ricerca corse in corso", Function() OrariTreni.OttieniAsync())
            Return orari
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.PreparaOrariAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio("Non riesco a preparare gli orari dei treni. Controlla la connessione a Internet e riprova.")
            Return Nothing
        End Try
    End Function

#End Region

#Region "Cerca viaggio"

    ''' <summary>Un luogo scelto: il nome, il codice di Trainline (se si è potuto chiedere) e la stazione degli orari programmati.</summary>
    Private Class LuogoScelto
        Public Nome As String = ""
        Public Testo As String = ""
        Public CodiceTrainline As String = ""
        Public IdOrario As Integer = -1
    End Class

    ''' <summary>Quello che l'utente ha scelto dagli elenchi mentre compila i campi.</summary>
    Private Class ScelteViaggio
        Public Da As LuogoScelto
        Public A As LuogoScelto
    End Class

    ' Le ricerche delle stazioni si fanno una volta sola per ogni nome scritto.
    Private Shared ReadOnly _cacheLuoghi As New Dictionary(Of String, List(Of LuogoScelto))()

    ' Il campo menu "Orari" (orario specifico o tutta la giornata) e, se serve, il campo "Orario". Con "Tutta la giornata" il campo dell'orario
    ' sparisce e si parte dalla mezzanotte; con un orario specifico si parte da quell'orario. In ogni caso i risultati arrivano fino a fine giornata.
    Private Sub AggiungiCampiOrario(campi As Campi)
        Dim scelte As New List(Of KeyValuePair(Of String, String))()
        scelte.Add(New KeyValuePair(Of String, String)("0", "Orario specifico"))
        scelte.Add(New KeyValuePair(Of String, String)("1", "Tutta la giornata"))
        campi.AggiungiScelta("Orari", "q", scelte, True, "0")
        campi.AggiungiOrario("Orario", "o", True, False, DateTime.Now.ToString("HH:mm"))
        Dim orarioPresente As Boolean = True
        AddHandler campi.CampoConfermato,
            Sub(idCampo As String)
                Try
                    If idCampo <> "q" Then Return
                    Dim tuttaGiornata As Boolean = (campi.RecuperaIdScelta("q") = "1")
                    If tuttaGiornata AndAlso orarioPresente Then
                        campi.RimuoviCampo("o")
                        orarioPresente = False
                    ElseIf Not tuttaGiornata AndAlso Not orarioPresente Then
                        campi.AggiungiOrario("Orario", "o", True, False, DateTime.Now.ToString("HH:mm"))
                        orarioPresente = True
                    End If
                Catch ex As Exception
                    _log.ScriviLog("MenuTreni.AggiungiCampiOrario: " & ex.Message)
                End Try
            End Sub
    End Sub

    ''' <summary>L'orario di partenza scelto: mezzanotte per "Tutta la giornata", altrimenti quello scritto.</summary>
    Private Shared Function LeggiOrarioScelto(campi As Campi) As TimeSpan?
        If campi.RecuperaIdScelta("q") = "1" Then Return TimeSpan.Zero
        Return LeggiOrario(campi.RecuperaValore("o"))
    End Function

    ''' <summary>
    ''' Tutte le corse del giorno da quell'ora in poi: Trainline ne dà un gruppo alla volta, quindi si chiede di nuovo a partire dall'ultima
    ''' partenza finché si arriva a fine giornata (al massimo dodici richieste). Se per quel giorno non c'è niente, restano le prime corse trovate.
    ''' </summary>
    Private Async Function CercaGiornataTrainlineAsync(da As LuogoScelto, a As LuogoScelto, quando As DateTime) As Task(Of List(Of OffertaTrainline))
        Dim tutte As New List(Of OffertaTrainline)()
        Dim prima As List(Of OffertaTrainline) = Nothing
        Dim giorno As Date = quando.Date
        Dim da1 As DateTime = quando
        For giro As Integer = 1 To 12
            Dim gruppo As List(Of OffertaTrainline) = Await RicercaTrainline.CercaAsync(da.CodiceTrainline, a.CodiceTrainline, da1, da.Nome, a.Nome, _passeggeri)
            If gruppo Is Nothing OrElse gruppo.Count = 0 Then Exit For
            If prima Is Nothing Then prima = gruppo
            Dim nuove As Integer = 0
            Dim ultima As DateTime = DateTime.MinValue
            For Each o As OffertaTrainline In gruppo
                If o.Partenza > ultima Then ultima = o.Partenza
                If o.Partenza.Date <> giorno Then Continue For
                Dim presente As Boolean = False
                For Each x As OffertaTrainline In tutte
                    If x.Partenza = o.Partenza AndAlso x.Arrivo = o.Arrivo AndAlso x.Operatore = o.Operatore AndAlso x.Prezzo = o.Prezzo AndAlso x.Tratti.Count = o.Tratti.Count Then
                        presente = True
                        Exit For
                    End If
                Next
                If Not presente Then
                    tutte.Add(o)
                    nuove += 1
                End If
            Next
            If ultima.Date <> giorno OrElse nuove = 0 Then Exit For
            da1 = ultima.AddMinutes(1)
        Next
        If tutte.Count = 0 Then Return prima
        tutte.Sort(Function(x, y) x.Partenza.CompareTo(y.Partenza))
        Return tutte
    End Function

    Private Sub CercaViaggio()
        Try
            Dim scelte As New ScelteViaggio()
            Dim campi As New Campi()
            campi.Aggiungi("Stazione di partenza", "p", True)
            campi.Aggiungi("Stazione di arrivo", "a", True)
            campi.AggiungiData("Data", "d", True, False, DateTime.Now.ToShortDateString())
            AggiungiCampiOrario(campi)
            Dim mezzi As New List(Of KeyValuePair(Of String, String))()
            mezzi.Add(New KeyValuePair(Of String, String)("0", "Treni e pullman"))
            mezzi.Add(New KeyValuePair(Of String, String)("1", "Solo treni"))
            mezzi.Add(New KeyValuePair(Of String, String)("2", "Solo pullman"))
            campi.AggiungiScelta("Mezzi", "m", mezzi, True, _mezzi.ToString())
            Dim passeggeri As New List(Of KeyValuePair(Of String, String))()
            For n As Integer = 1 To 9
                passeggeri.Add(New KeyValuePair(Of String, String)(n.ToString(), n & If(n = 1, " passeggero", " passeggeri")))
            Next
            campi.AggiungiScelta("Passeggeri", "n", passeggeri, True, _passeggeri.ToString())
            ' Il tipo di treni si chiede solo se si cercano anche treni: con "Solo pullman" non compare. Sta per ultimo, così si toglie
            ' e si rimette senza cambiare l'ordine degli altri campi.
            Dim tipi As New List(Of KeyValuePair(Of String, String))()
            For Each tipo As TipoTreni In New TipoTreni() {TipoTreni.Tutti, TipoTreni.AltaVelocita, TipoTreni.Veloci, TipoTreni.Regionali}
                tipi.Add(New KeyValuePair(Of String, String)(CInt(tipo).ToString(), NomeTipo(tipo)))
            Next
            Dim tipoPresente As Boolean = (_mezzi <> 2)
            If tipoPresente Then campi.AggiungiScelta("Tipo di treni", "k", tipi, True, CInt(_tipo).ToString())
            AddHandler campi.CampoConfermato,
                Sub(idCampo As String)
                    Try
                        If idCampo <> "m" Then Return
                        Dim soloPullman As Boolean = (campi.RecuperaIdScelta("m") = "2")
                        If soloPullman AndAlso tipoPresente Then
                            campi.RimuoviCampo("k")
                            tipoPresente = False
                        ElseIf Not soloPullman AndAlso Not tipoPresente Then
                            campi.AggiungiScelta("Tipo di treni", "k", tipi, True, CInt(_tipo).ToString())
                            tipoPresente = True
                        End If
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.CercaViaggio (tipo di treni): " & ex.Message)
                    End Try
                End Sub
            ' Invio su un campo di stazione apre l'elenco delle stazioni; su Data, l'elenco delle date (lo fa il campo stesso).
            campi.ImpostaElenco("p", Function(testo As String, conferma As Action(Of String)) CostruisciElencoLuoghiAsync(testo, "Stazione di partenza", True, scelte, conferma))
            campi.ImpostaElenco("a", Function(testo As String, conferma As Action(Of String)) CostruisciElencoLuoghiAsync(testo, "Stazione di arrivo", False, scelte, conferma))
            AddHandler campi.Fine, Sub() FineCercaViaggio(campi, scelte)
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CercaViaggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' I luoghi che corrispondono al testo: prima quelli di Trainline (stazioni, fermate di pullman, città, anche all'estero); se
    ''' Trainline non risponde, le stazioni degli orari programmati che sono già sul computer. Senza messaggi.
    ''' </summary>
    Private Async Function CercaLuoghiAsync(testo As String) As Task(Of List(Of LuogoScelto))
        Dim chiave As String = OrariTreni.Normalizza(testo)
        Dim presente As List(Of LuogoScelto) = Nothing
        If _cacheLuoghi.TryGetValue(chiave, presente) Then Return presente
        Dim risultato As New List(Of LuogoScelto)()
        ' Con il campo vuoto si mostrano le stazioni più frequentate degli orari sul computer.
        If chiave = "" Then
            Try
                Dim orariTutti As OrariTreni = Await OrariSilenziosiAsync()
                If orariTutti IsNot Nothing Then
                    For Each s As StazioneOrario In orariTutti.PiuFrequentate(300)
                        Dim l As New LuogoScelto()
                        l.Nome = s.Nome
                        l.Testo = s.Nome
                        l.IdOrario = s.Id
                        risultato.Add(l)
                    Next
                End If
            Catch ex As Exception
                _log.ScriviLog("MenuTreni.CercaLuoghiAsync frequenti: " & ex.Message)
            End Try
            If risultato.Count > 0 Then _cacheLuoghi(chiave) = risultato
            Return risultato
        End If
        Try
            Dim daTrainline As List(Of TempoRealeTreni.LuogoTrainline) =
                Await WGProcessoInCorso.EseguiAsync(Of List(Of TempoRealeTreni.LuogoTrainline))("Ricerca stazioni in corso", Function() TempoRealeTreni.CercaLuoghiTrainlineAsync(testo))
            For Each x As TempoRealeTreni.LuogoTrainline In daTrainline
                Dim l As New LuogoScelto()
                l.Nome = x.Nome
                l.Testo = x.Descrizione()
                l.CodiceTrainline = x.Codice
                risultato.Add(l)
            Next
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CercaLuoghiAsync Trainline: " & ex.Message)
        End Try
        If risultato.Count = 0 Then
            Try
                Dim orari As OrariTreni = Await OrariSilenziosiAsync()
                If orari IsNot Nothing Then
                    For Each s As StazioneOrario In orari.CercaStazioni(testo, 60)
                        Dim l As New LuogoScelto()
                        l.Nome = s.Nome
                        l.Testo = s.Nome
                        l.IdOrario = s.Id
                        risultato.Add(l)
                    Next
                End If
            Catch ex As Exception
                _log.ScriviLog("MenuTreni.CercaLuoghiAsync orari: " & ex.Message)
            End Try
        End If
        If risultato.Count > 0 Then _cacheLuoghi(chiave) = risultato
        Return risultato
    End Function

    ''' <summary>Gli orari programmati, senza messaggi: se non si possono avere, Nothing.</summary>
    Private Async Function OrariSilenziosiAsync() As Task(Of OrariTreni)
        Try
            Return Await WGProcessoInCorso.EseguiAsync(Of OrariTreni)("Ricerca corse in corso", Function() OrariTreni.OttieniAsync())
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.OrariSilenziosiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' L'elenco delle stazioni che si apre con Invio su un campo di stazione. Con del testo scritto: i luoghi di Trainline (o, se non
    ''' risponde, le stazioni degli orari sul computer) che corrispondono; con il campo vuoto: le stazioni più frequentate.
    ''' Scegliendo una voce l'elenco si chiude, il nome va nel campo e si passa al campo successivo.
    ''' </summary>
    Private Async Function CostruisciElencoLuoghiAsync(testo As String, titolo As String, partenza As Boolean, scelte As ScelteViaggio, conferma As Action(Of String)) As Task(Of WGMenuLista)
        Try
            Dim luoghi As List(Of LuogoScelto) = Await CercaLuoghiAsync(testo)
            If luoghi.Count = 0 Then
                Messaggio("Non trovo nessuna stazione che si chiami " & testo.Trim() & ".")
                Return Nothing
            End If
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(titolo)
            For i As Integer = 0 To luoghi.Count - 1
                lista.aggiungiElemento(luoghi(i).Testo, i.ToString())
            Next
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim scelto As LuogoScelto = luoghi(CInt(id))
                        If partenza Then scelte.Da = scelto Else scelte.A = scelto
                        lista.abbandona()
                        conferma(scelto.Nome)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.CostruisciElencoLuoghiAsync selezione: " & ex.Message)
                    End Try
                End Sub
            Return lista
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CostruisciElencoLuoghiAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Async Sub FineCercaViaggio(campi As Campi, scelte As ScelteViaggio)
        Try
            If Not campi.TuttiObbligatoriRiempiti() Then Return
            Dim testoP As String = campi.RecuperaValore("p").Trim()
            Dim testoA As String = campi.RecuperaValore("a").Trim()
            Dim data As DateTime? = LeggiData(campi.RecuperaValore("d"))
            Dim orario As TimeSpan? = LeggiOrarioScelto(campi)
            If Not data.HasValue Then
                Messaggio("Non ho capito la data.")
                Return
            End If
            If Not orario.HasValue Then
                Messaggio("Non ho capito l'orario.")
                Return
            End If
            Dim quando As DateTime = data.Value.Add(orario.Value)
            Dim valore As Integer
            If Integer.TryParse(campi.RecuperaIdScelta("m"), valore) AndAlso valore >= 0 AndAlso valore <= 2 Then _mezzi = valore
            If Integer.TryParse(campi.RecuperaIdScelta("n"), valore) AndAlso valore >= 1 AndAlso valore <= 9 Then _passeggeri = valore
            If Integer.TryParse(campi.RecuperaIdScelta("k"), valore) AndAlso valore >= 0 AndAlso valore <= 3 Then _tipo = CType(valore, TipoTreni)
            Salva(IdConfigTipo, CInt(_tipo).ToString())
            Salva(IdConfigMezzi, _mezzi.ToString())
            Salva(IdConfigPasseggeri, _passeggeri.ToString())

            ' Se un elenco è stato chiuso senza scegliere, vale il primo luogo che corrisponde al testo scritto.
            Dim da As LuogoScelto = scelte.Da
            Dim a As LuogoScelto = scelte.A
            If da Is Nothing OrElse Not String.Equals(da.Nome, testoP, StringComparison.OrdinalIgnoreCase) Then da = Await PrimoLuogoAsync(testoP)
            If a Is Nothing OrElse Not String.Equals(a.Nome, testoA, StringComparison.OrdinalIgnoreCase) Then a = Await PrimoLuogoAsync(testoA)
            If da Is Nothing OrElse a Is Nothing Then Return
            If String.Equals(da.Nome, a.Nome, StringComparison.OrdinalIgnoreCase) Then
                Messaggio("La stazione di partenza e quella di arrivo sono la stessa.")
                Return
            End If

            SalvaRicercaRecente(da, a)
            EseguiRicercaCompleta(da, a, quando, Nothing)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.FineCercaViaggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' La ricerca vera e propria, con le scelte attuali (mezzi, tipo di treni, passeggeri). "evidenzia" è l'ora di una corsa su cui
    ''' posizionare l'elenco, per le corse preferite e recenti.
    ''' </summary>
    Private Async Sub EseguiRicercaCompleta(da As LuogoScelto, a As LuogoScelto, quando As DateTime, evidenzia As TimeSpan?)
        Try
            ' Trainline dà insieme treni, pullman e FlixBus con prezzi e dettaglio; per i pullman si cerca anche direttamente su FlixBus (tutte le
            ' corse del giorno, con le fermate di partenza e di arrivo). Se Trainline non risponde, per i treni valgono gli orari programmati di
            ' Trenitalia e Trenord che sono sul computer, senza nessun messaggio.
            Dim offerte As List(Of OffertaTrainline) = Nothing
            Dim flix As List(Of OffertaTrainline) = Nothing
            Dim codiciPresenti As Boolean = (da.CodiceTrainline <> "" AndAlso a.CodiceTrainline <> "")
            Await WGProcessoInCorso.EseguiAsync("Ricerca corse in corso",
                Async Function() As Task
                    If codiciPresenti Then offerte = Await CercaGiornataTrainlineAsync(da, a, quando)
                    If _mezzi <> 1 Then
                        Dim idDa As String = Await RicercaFlixBus.IdCittaAsync(da.Nome)
                        Dim idA As String = Await RicercaFlixBus.IdCittaAsync(a.Nome)
                        If idDa IsNot Nothing AndAlso idA IsNot Nothing AndAlso idDa <> idA Then
                            flix = Await RicercaFlixBus.CercaAsync(idDa, idA, quando, _passeggeri)
                        End If
                    End If
                End Function)
            Dim filtrate As List(Of OffertaTrainline) = If(offerte IsNot Nothing, FiltraPerMezzo(offerte), New List(Of OffertaTrainline)())
            If flix IsNot Nothing AndAlso flix.Count > 0 Then
                Dim flixFiltrate As List(Of OffertaTrainline) = FiltraPerMezzo(flix)
                If flixFiltrate.Count > 0 Then
                    ' Le corse di FlixBus che Trainline ha già si tolgono da Trainline: restano quelle dirette, più complete.
                    filtrate.RemoveAll(Function(o) o.Operatore.IndexOf("Flix", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso o.Mezzi = "Pullman")
                    filtrate.AddRange(flixFiltrate)
                    filtrate.Sort(Function(x, y) x.Partenza.CompareTo(y.Partenza))
                End If
            End If
            ' Con "Treni e pullman" e senza risposta di Trainline si usano gli orari dei treni sul computer: i soli pullman non bastano.
            If filtrate.Count > 0 AndAlso (_mezzi = 2 OrElse (offerte IsNot Nothing AndAlso offerte.Count > 0)) Then
                MostraOfferte(da.Nome, a.Nome, If(codiciPresenti, da.CodiceTrainline, ""), If(codiciPresenti, a.CodiceTrainline, ""), filtrate, evidenzia)
                Return
            End If
            If _mezzi = 2 Then
                Messaggio("Non ci sono pullman per questo viaggio.")
                Return
            End If
            CercaConOrari(da, a, quando)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.EseguiRicercaCompleta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Le corse che corrispondono alle scelte: treni e pullman, solo treni o solo pullman, e il tipo di treni (alta velocità, senza regionali, regionali).</summary>
    Private Function FiltraPerMezzo(offerte As List(Of OffertaTrainline)) As List(Of OffertaTrainline)
        Dim risultato As New List(Of OffertaTrainline)()
        For Each o As OffertaTrainline In offerte
            Dim mezzoValido As Boolean = (_mezzi = 0) OrElse (_mezzi = 1 AndAlso o.Mezzi = "Treno") OrElse (_mezzi = 2 AndAlso o.Mezzi = "Pullman")
            If mezzoValido AndAlso o.CorrispondeTipo(_tipo) Then risultato.Add(o)
        Next
        Return risultato
    End Function

    Private Async Function PrimoLuogoAsync(testo As String) As Task(Of LuogoScelto)
        Dim luoghi As List(Of LuogoScelto) = Await CercaLuoghiAsync(testo)
        If luoghi.Count = 0 Then
            Messaggio("Non trovo nessuna stazione che si chiami " & testo & ".")
            Return Nothing
        End If
        Return luoghi(0)
    End Function

    ''' <summary>La stazione degli orari programmati che corrisponde al luogo scelto, o Nothing.</summary>
    Private Shared Function StazioneDegliOrari(orari As OrariTreni, luogo As LuogoScelto) As StazioneOrario
        If luogo.IdOrario >= 0 AndAlso luogo.IdOrario < orari.Stazioni.Count Then Return orari.Stazioni(luogo.IdOrario)
        Dim trovate As List(Of StazioneOrario) = orari.CercaStazioni(luogo.Nome, 5)
        If trovate.Count = 0 Then Return Nothing
        Dim chiave As String = OrariTreni.Normalizza(luogo.Nome)
        For Each s As StazioneOrario In trovate
            If s.Chiave = chiave Then Return s
        Next
        Return trovate(0)
    End Function

    ''' <summary>La ricerca con gli orari programmati di Trenitalia e Trenord, che funziona anche senza Trainline.</summary>
    Private Async Sub CercaConOrari(da As LuogoScelto, a As LuogoScelto, quando As DateTime)
        Try
            Dim orari As OrariTreni = Await PreparaOrariAsync()
            If orari Is Nothing Then Return

            Dim giorno As Integer = OrariTreni.GiornoDa(quando)
            If giorno < orari.PrimoGiorno OrElse giorno > orari.UltimoGiorno Then
                Messaggio("Per quel giorno non ho gli orari. Sono disponibili dal " & OrariTreni.DataDa(orari.PrimoGiorno).ToString("dd/MM/yyyy") & " al " & OrariTreni.DataDa(orari.UltimoGiorno).ToString("dd/MM/yyyy") & ".")
                Return
            End If

            Dim partenza As StazioneOrario = StazioneDegliOrari(orari, da)
            Dim arrivo As StazioneOrario = StazioneDegliOrari(orari, a)
            If partenza Is Nothing OrElse arrivo Is Nothing Then
                Messaggio("Non trovo " & If(partenza Is Nothing, da.Nome, a.Nome) & " negli orari.")
                Return
            End If
            If partenza.Id = arrivo.Id Then
                Messaggio("La stazione di partenza e quella di arrivo sono la stessa.")
                Return
            End If

            Dim ctx As New ContestoRicerca()
            ctx.Orari = orari
            If _ricerca Is Nothing OrElse Not Object.ReferenceEquals(_ricercaDi, orari) Then
                _ricerca = New RicercaViaggi(orari)
                _ricercaDi = orari
            End If
            ctx.Ricerca = _ricerca
            ctx.Partenza = quando
            ctx.Tipo = _tipo
            ctx.Cambi = _cambi
            ctx.Origine = partenza
            ctx.Destinazione = arrivo
            EseguiRicerca(ctx)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CercaConOrari: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub EseguiRicerca(ctx As ContestoRicerca)
        Try
            Dim soluzioni As List(Of SoluzioneViaggio) =
                Await WGProcessoInCorso.EseguiAsync(Of List(Of SoluzioneViaggio))("Ricerca corse in corso",
                    Function() Task.Run(Function() ctx.Ricerca.Cerca(ctx.Origine.Id, ctx.Destinazione.Id, ctx.Partenza, ctx.Tipo, ctx.Cambi, 60)))
            ' Si mostrano tutte quelle del giorno di partenza (se per quel giorno non ce ne sono restano le prime trovate).
            Dim delGiorno As New List(Of SoluzioneViaggio)()
            For Each s As SoluzioneViaggio In soluzioni
                If GiornoDi(s.Partenza) = 0 Then delGiorno.Add(s)
            Next
            If delGiorno.Count > 0 Then soluzioni = delGiorno
            If soluzioni.Count = 0 Then
                Messaggio("Non ho trovato viaggi da " & ctx.Origine.Nome & " a " & ctx.Destinazione.Nome & " per questa richiesta.")
                Return
            End If
            MostraRisultati(ctx, soluzioni)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.EseguiRicerca: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio("La ricerca del viaggio non è riuscita.")
        End Try
    End Sub

    Private Function DescriviSoluzione(ctx As ContestoRicerca, s As SoluzioneViaggio) As String
        Dim giornoRicerca As DateTime = ctx.Partenza.Date
        Dim sb As New StringBuilder()
        Dim dataPartenza As DateTime = giornoRicerca.AddDays(GiornoDi(s.Partenza))
        sb.Append("Parte alle " & Ora(s.Partenza))
        If dataPartenza.Date <> giornoRicerca Then sb.Append(" del " & dataPartenza.ToString("dd/MM"))
        sb.Append(", arriva alle " & Ora(s.Arrivo))
        If GiornoDi(s.Arrivo) > GiornoDi(s.Partenza) Then sb.Append(" del giorno dopo")
        sb.Append(", durata " & Durata(s.Arrivo - s.Partenza))
        If s.Cambi = 0 Then
            sb.Append(", senza cambi")
        ElseIf s.Cambi = 1 Then
            sb.Append(", un cambio")
        Else
            sb.Append(", " & s.Cambi & " cambi")
        End If
        Dim treni As New List(Of String)()
        For Each t As TrattoViaggio In s.Tratti
            treni.Add(t.Corsa.Etichetta())
        Next
        sb.Append(": " & String.Join(", ", treni))
        Return sb.ToString()
    End Function

    Private Sub MostraRisultati(ctx As ContestoRicerca, soluzioni As List(Of SoluzioneViaggio))
        Try
            Dim lista As WGMenuLista = NuovaLista("Da " & ctx.Origine.Nome & " a " & ctx.Destinazione.Nome)
            For i As Integer = 0 To soluzioni.Count - 1
                lista.aggiungiElemento(DescriviSoluzione(ctx, soluzioni(i)), i.ToString())
            Next
            lista.aggiungiOpzione("Viaggi successivi",
                Sub(s, e)
                    Try
                        Dim nuovo As New ContestoRicerca()
                        nuovo.Orari = ctx.Orari
                        nuovo.Ricerca = ctx.Ricerca
                        nuovo.Origine = ctx.Origine
                        nuovo.Destinazione = ctx.Destinazione
                        nuovo.Tipo = ctx.Tipo
                        nuovo.Cambi = ctx.Cambi
                        nuovo.Partenza = ctx.Partenza.Date.AddSeconds(soluzioni(soluzioni.Count - 1).Partenza + 60)
                        EseguiRicerca(nuovo)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ViaggiSuccessivi: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        ApriViaggio(ctx, soluzioni(CInt(id)))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.MostraRisultati selezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.MostraRisultati: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriViaggio(ctx As ContestoRicerca, sol As SoluzioneViaggio)
        Try
            Dim lista As WGMenuLista = NuovaLista("Dettaglio viaggio")
            For i As Integer = 0 To sol.Tratti.Count - 1
                Dim t As TrattoViaggio = sol.Tratti(i)
                Dim sb As New StringBuilder()
                If i > 0 Then
                    Dim precedente As TrattoViaggio = sol.Tratti(i - 1)
                    Dim nomeCambio As String = ctx.Orari.Stazioni(t.Corsa.Stazioni(t.Salita)).Nome
                    sb.Append("Cambio a " & nomeCambio & " in " & Durata(t.Partenza - precedente.Arrivo) & ". ")
                End If
                sb.Append(t.Corsa.Etichetta() & " da " & ctx.Orari.Stazioni(t.Corsa.Stazioni(t.Salita)).Nome & " alle " & Ora(t.Partenza) &
                          " a " & ctx.Orari.Stazioni(t.Corsa.Stazioni(t.Discesa)).Nome & " alle " & Ora(t.Arrivo))
                lista.aggiungiElemento(sb.ToString(), i.ToString())
            Next
            lista.aggiungiOpzione("Binari e ritardi", Sub(s, e) TempoRealeTratto(ctx, sol.Tratti(CInt(lista.ottieniElemento()))))
            lista.aggiungiOpzione("Acquista il biglietto", Sub(s, e) LinkTratto(ctx, sol.Tratti(CInt(lista.ottieniElemento()))))
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        ApriTratto(ctx, sol.Tratti(CInt(id)))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ApriViaggio selezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriViaggio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriTratto(ctx As ContestoRicerca, t As TrattoViaggio)
        Try
            Dim lista As WGMenuLista = NuovaLista(t.Corsa.Etichetta())
            For i As Integer = t.Salita To t.Discesa
                Dim nome As String = ctx.Orari.Stazioni(t.Corsa.Stazioni(i)).Nome
                Dim testo As String
                If i = t.Salita Then
                    testo = nome & ", partenza " & Ora(t.Base + t.Corsa.Partenze(i))
                ElseIf i = t.Discesa Then
                    testo = nome & ", arrivo " & Ora(t.Base + t.Corsa.Arrivi(i))
                Else
                    testo = nome & ", arrivo " & Ora(t.Base + t.Corsa.Arrivi(i)) & ", partenza " & Ora(t.Base + t.Corsa.Partenze(i))
                End If
                lista.aggiungiElemento(testo, i.ToString())
            Next
            lista.aggiungiOpzione("Binari e ritardi", Sub(s, e) TempoRealeTratto(ctx, t))
            lista.aggiungiOpzione("Acquista il biglietto", Sub(s, e) LinkTratto(ctx, t))
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriTratto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Apre i risultati di Trainline già impostati (stazioni, giorno e ora), nel browser scelto nelle impostazioni: quello di
    ''' WinGabriel o quello predefinito. Trainline vende Trenitalia, Italo e Trenord. I siti delle compagnie non accettano un viaggio
    ''' già impostato nell'indirizzo, Trainline sì. Il codice delle stazioni si chiede a Trainline; se non risponde si apre la sua
    ''' pagina di ricerca.
    ''' </summary>
    Private Async Sub ApriBiglietto(da As String, a As String, quando As DateTime)
        Try
            Dim origine As String = ""
            Dim destinazione As String = ""
            Dim offerte As List(Of OffertaTrainline) = Nothing
            Await WGProcessoInCorso.EseguiAsync("Ricerca corse in corso",
                Async Function() As Task
                    origine = Await TempoRealeTreni.CodiceTrainlineAsync(da)
                    destinazione = Await TempoRealeTreni.CodiceTrainlineAsync(a)
                    If origine <> "" AndAlso destinazione <> "" Then offerte = Await RicercaTrainline.CercaAsync(origine, destinazione, quando, da, a, _passeggeri)
                End Function)
            ' Con le corse lette dal JSON si sceglie da un elenco di WinGabriel; altrimenti si apre la pagina di Trainline.
            If offerte IsNot Nothing AndAlso offerte.Count > 0 Then
                Dim filtrate As List(Of OffertaTrainline) = FiltraPerMezzo(offerte)
                If filtrate.Count > 0 Then
                    MostraOfferte(da, a, origine, destinazione, filtrate)
                    Return
                End If
            End If
            Dim url As String = "https://www.thetrainline.com/it"
            If origine <> "" AndAlso destinazione <> "" Then url = RicercaTrainline.UrlRisultati(origine, destinazione, quando.AddMinutes(-1), _passeggeri)
            WGImpostazioniComuni.ApriLinkConBrowserPreferito(url)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriBiglietto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio("Non riesco ad aprire la biglietteria. Controlla la connessione a Internet e riprova.")
        End Try
    End Sub

    ''' <summary>L'elenco delle corse con operatore e prezzo; scegliendone una si apre Trainline su quella partenza.</summary>
    Private Sub MostraOfferte(da As String, a As String, origine As String, destinazione As String, offerte As List(Of OffertaTrainline), Optional evidenzia As TimeSpan? = Nothing)
        Try
            Dim lista As WGMenuLista = NuovaLista("Biglietti da " & da & " a " & a)
            For i As Integer = 0 To offerte.Count - 1
                lista.aggiungiElemento(offerte(i).Descrizione(), i.ToString())
            Next
            If evidenzia.HasValue Then
                For i As Integer = 0 To offerte.Count - 1
                    If offerte(i).Partenza.TimeOfDay = evidenzia.Value Then
                        lista.posiziona(i.ToString())
                        Exit For
                    End If
                Next
            End If
            lista.aggiungiOpzione("Aggiungi ai preferiti",
                Sub(s, e)
                    Try
                        Dim indice As Integer
                        If Integer.TryParse(lista.ottieniElemento(), indice) AndAlso indice >= 0 AndAlso indice < offerte.Count Then
                            Dim corsa As CorsaSalvata = CorsaDaOfferta(da, a, origine, destinazione, offerte(indice))
                            ArchivioTreni.SalvaCorsa(True, corsa)
                            Messaggio("Corsa aggiunta ai preferiti.")
                        End If
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.AggiungiAiPreferiti: " & ex.Message)
                    End Try
                End Sub)
            If origine <> "" AndAlso destinazione <> "" Then
                lista.aggiungiOpzione("Viaggi successivi",
                    Sub(s, e)
                        Try
                            CercaSuccessiviTrainline(da, a, origine, destinazione, offerte(offerte.Count - 1).Partenza.AddMinutes(1))
                        Catch ex As Exception
                            _log.ScriviLog("MenuTreni.ViaggiSuccessiviTrainline: " & ex.Message)
                        End Try
                    End Sub)
            End If
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        ApriDettaglioOfferta(da, a, origine, destinazione, offerte(CInt(id)))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.MostraOfferte selezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.MostraOfferte: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub CercaSuccessiviTrainline(da As String, a As String, origine As String, destinazione As String, quando As DateTime)
        Try
            Dim offerte As List(Of OffertaTrainline) = Nothing
            Await WGProcessoInCorso.EseguiAsync("Ricerca corse in corso",
                Async Function() As Task
                    offerte = Await RicercaTrainline.CercaAsync(origine, destinazione, quando, da, a, _passeggeri)
                End Function)
            If offerte IsNot Nothing Then offerte = FiltraPerMezzo(offerte)
            If offerte Is Nothing OrElse offerte.Count = 0 Then
                Messaggio("Non ci sono altri viaggi da mostrare.")
                Return
            End If
            MostraOfferte(da, a, origine, destinazione, offerte)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CercaSuccessiviTrainline: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio("La ricerca del viaggio non è riuscita.")
        End Try
    End Sub

    ''' <summary>
    ''' Il dettaglio di una corsa: i tratti (treno o pullman, stazioni e orari) e, in fondo, la voce che apre Trainline su quella
    ''' partenza nel browser scelto nelle impostazioni.
    ''' </summary>
    Private Sub ApriDettaglioOfferta(da As String, a As String, origine As String, destinazione As String, offerta As OffertaTrainline)
        Try
            ArchivioTreni.SalvaCorsa(False, CorsaDaOfferta(da, a, origine, destinazione, offerta))
            Dim apri As Action = Sub() WGImpostazioniComuni.ApriLinkConBrowserPreferito(If(offerta.LinkAcquisto <> "", offerta.LinkAcquisto, RicercaTrainline.UrlRisultati(origine, destinazione, offerta.Partenza.AddMinutes(-1), offerta.Passeggeri)))
            If offerta.Tratti.Count = 0 Then
                If offerta.Acquistabile Then apri() Else Messaggio("Questo viaggio non si può acquistare.")
                Return
            End If
            Dim lista As WGMenuLista = NuovaLista("Dettaglio viaggio")
            For i As Integer = 0 To offerta.Tratti.Count - 1
                Dim t As TrattoTrainline = offerta.Tratti(i)
                Dim testo As String = t.Descrizione()
                If testo = "" Then testo = "Tratto " & (i + 1)
                If i > 0 AndAlso offerta.Tratti(i - 1).Arrivo <> DateTime.MinValue AndAlso t.Partenza <> DateTime.MinValue Then
                    Dim attesa As TimeSpan = t.Partenza - offerta.Tratti(i - 1).Arrivo
                    If attesa > TimeSpan.Zero Then testo = "Cambio di " & Durata(CInt(attesa.TotalSeconds)) & ". " & testo
                End If
                lista.aggiungiElemento(testo, i.ToString())
            Next
            If offerta.Acquistabile Then lista.aggiungiElemento(If(offerta.LinkAcquisto <> "", "Acquista su FlixBus", "Acquista su Trainline"), "acquista")
            lista.aggiungiOpzione("Fermate del treno",
                Sub(s, e)
                    Try
                        Dim n As Integer
                        If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < offerta.Tratti.Count Then ApriFermateTrattoTrainline(offerta.Tratti(n))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.FermateTrattoTrainline: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        If id = "acquista" Then
                            apri()
                        Else
                            Dim n As Integer
                            If Integer.TryParse(id, n) AndAlso n >= 0 AndAlso n < offerta.Tratti.Count Then ApriFermateTrattoTrainline(offerta.Tratti(n))
                        End If
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ApriDettaglioOfferta selezione: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriDettaglioOfferta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Le fermate di un tratto in treno di una corsa di Trainline: si cerca il treno per numero (Italo o Trenitalia) e si mostra tutto il percorso con
    ''' gli orari e gli eventuali ritardi. Per i pullman e per i treni che non si trovano c'è un messaggio.
    ''' </summary>
    Private Async Sub ApriFermateTrattoTrainline(t As TrattoTrainline)
        Try
            If t Is Nothing Then Return
            If t.Mezzo <> "Treno" Then
                Messaggio("Per i pullman non ci sono le fermate.")
                Return
            End If
            Dim m As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(If(t.Nome, ""), "(\d+)\s*$")
            If Not m.Success Then
                Messaggio("Non conosco il numero di questo treno.")
                Return
            End If
            Dim numero As String = m.Groups(1).Value
            If t.Nome.IndexOf("Italo", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Dim d As DettaglioTreno =
                    Await WGProcessoInCorso.EseguiAsync(Of DettaglioTreno)("Ricerca fermate in corso", Function() TempoRealeTreni.TrenoItaloAsync(numero))
                If d Is Nothing OrElse d.Fermate.Count = 0 Then
                    Messaggio("Non ci sono informazioni sulle fermate del treno " & numero & ".")
                    Return
                End If
                MostraDettaglio(d)
                Return
            End If
            Dim trovati As List(Of TrenoTrovato) =
                Await WGProcessoInCorso.EseguiAsync(Of List(Of TrenoTrovato))("Ricerca fermate in corso", Function() TempoRealeTreni.CercaTrenoTrenitaliaAsync(numero))
            If trovati Is Nothing OrElse trovati.Count = 0 Then
                Messaggio("Non ci sono informazioni sulle fermate del treno " & numero & ".")
                Return
            End If
            Dim scelto As TrenoTrovato = trovati(0)
            For Each x As TrenoTrovato In trovati
                If TempoRealeTreni.DaMillisecondi(x.Millisecondi).Date = t.Partenza.Date Then
                    scelto = x
                    Exit For
                End If
            Next
            Await MostraAndamentoTrenitaliaAsync(scelto)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriFermateTrattoTrainline: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

    Private Sub LinkTratto(ctx As ContestoRicerca, t As TrattoViaggio)
        Try
            Dim da As String = ctx.Orari.Stazioni(t.Corsa.Stazioni(t.Salita)).Nome
            Dim a As String = ctx.Orari.Stazioni(t.Corsa.Stazioni(t.Discesa)).Nome
            ApriBiglietto(da, a, ctx.Partenza.Date.AddSeconds(t.Partenza))
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.LinkTratto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub TempoRealeTratto(ctx As ContestoRicerca, t As TrattoViaggio)
        Try
            Dim giornoTreno As DateTime = ctx.Partenza.Date.AddDays(GiornoDi(t.Base))
            Dim trovati As List(Of TrenoTrovato) =
                Await WGProcessoInCorso.EseguiAsync(Of List(Of TrenoTrovato))("Ricerca corse in corso", Function() TempoRealeTreni.CercaTrenoTrenitaliaAsync(t.Corsa.Numero))
            If trovati.Count = 0 Then
                Messaggio("Non ci sono informazioni in tempo reale per il treno " & t.Corsa.Numero & ".")
                Return
            End If
            Dim scelto As TrenoTrovato = trovati(0)
            For Each x As TrenoTrovato In trovati
                If TempoRealeTreni.DaMillisecondi(x.Millisecondi).Date = giornoTreno.Date Then
                    scelto = x
                    Exit For
                End If
            Next
            Await MostraAndamentoTrenitaliaAsync(scelto)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.TempoRealeTratto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

#End Region

#Region "Partenze e arrivi"

    ''' <summary>Quello che si sceglie dall'elenco delle stazioni del tabellone.</summary>
    Private Class SceltaTabellone
        Public Stazione As StazioneScelta
    End Class

    ''' <summary>
    ''' Due campi: la stazione (Invio apre l'elenco delle stazioni che corrispondono) e un campo menu con Partenze e Arrivi.
    ''' </summary>
    Private Sub ChiediStazioneTabellone()
        Try
            Dim scelta As New SceltaTabellone()
            Dim campi As New Campi()
            campi.Aggiungi("Stazione", "s", True)
            campi.ImpostaElenco("s", Function(testo As String, conferma As Action(Of String)) CostruisciElencoStazioniAsync(testo, scelta, conferma))
            Dim modi As New List(Of KeyValuePair(Of String, String))()
            modi.Add(New KeyValuePair(Of String, String)("p", "Partenze"))
            modi.Add(New KeyValuePair(Of String, String)("a", "Arrivi"))
            campi.AggiungiScelta("Tabellone", "t", modi, True, "p")
            AddHandler campi.Fine, Sub() FineStazioneTabellone(campi, scelta)
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ChiediStazioneTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Function CercaStazioniTabelloneAsync(testo As String) As Task(Of List(Of StazioneScelta))
        Return Await WGProcessoInCorso.EseguiAsync(Of List(Of StazioneScelta))("Ricerca stazioni in corso", Function() TempoRealeTreni.CercaStazioniAsync(testo))
    End Function

    Private Async Function CostruisciElencoStazioniAsync(testo As String, scelta As SceltaTabellone, conferma As Action(Of String)) As Task(Of WGMenuLista)
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                Messaggio("Scrivi il nome di una stazione.")
                Return Nothing
            End If
            Dim trovate As List(Of StazioneScelta) = Await CercaStazioniTabelloneAsync(testo.Trim())
            If trovate.Count = 0 Then
                Messaggio("Non trovo nessuna stazione che si chiami " & testo.Trim() & ".")
                Return Nothing
            End If
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Stazione")
            For i As Integer = 0 To trovate.Count - 1
                lista.aggiungiElemento(trovate(i).Nome, i.ToString())
            Next
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim s As StazioneScelta = trovate(CInt(id))
                        scelta.Stazione = s
                        lista.abbandona()
                        conferma(s.Nome)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.CostruisciElencoStazioniAsync selezione: " & ex.Message)
                    End Try
                End Sub
            Return lista
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.CostruisciElencoStazioniAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
            Return Nothing
        End Try
    End Function

    Private Async Sub FineStazioneTabellone(campi As Campi, scelta As SceltaTabellone)
        Try
            If Not campi.TuttiObbligatoriRiempiti() Then Return
            Dim testo As String = campi.RecuperaValore("s").Trim()
            Dim stazione As StazioneScelta = scelta.Stazione
            ' Se l'elenco non è stato usato, vale la prima stazione che corrisponde al testo scritto.
            If stazione Is Nothing OrElse Not String.Equals(stazione.Nome, testo, StringComparison.OrdinalIgnoreCase) Then
                Dim trovate As List(Of StazioneScelta) = Await CercaStazioniTabelloneAsync(testo)
                If trovate.Count = 0 Then
                    Messaggio("Non trovo nessuna stazione che si chiami " & testo & ".")
                    Return
                End If
                stazione = trovate(0)
            End If
            MostraTabellone(stazione, campi.RecuperaIdScelta("t") <> "a")
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.FineStazioneTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

    Private Async Sub MostraTabellone(stazione As StazioneScelta, partenze As Boolean)
        Try
            Dim tab As Tabellone =
                Await WGProcessoInCorso.EseguiAsync(Of Tabellone)("Ricerca corse in corso", Function() TempoRealeTreni.OttieniTabelloneAsync(stazione, partenze))
            If tab.Avvisi.Count > 0 Then Messaggio(String.Join(" ", tab.Avvisi))
            If tab.Voci.Count = 0 Then
                Messaggio("Non ci sono " & If(partenze, "partenze", "arrivi") & " da mostrare per " & stazione.Nome & ".")
                Return
            End If
            ApriTabellone(stazione, partenze, tab.Voci)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.MostraTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

    Private Sub ApriTabellone(stazione As StazioneScelta, partenze As Boolean, vociIniziali As List(Of VoceTabellone))
        Try
            Dim voci As List(Of VoceTabellone) = vociIniziali
            Dim filtro As String = ""
            Dim lista As WGMenuLista = NuovaLista(If(partenze, "Partenze da ", "Arrivi a ") & stazione.Nome)
            Dim visibili As New List(Of VoceTabellone)()

            Dim Riempi As Func(Of Boolean) =
                Function() As Boolean
                    Dim nuove As New List(Of VoceTabellone)()
                    For Each v As VoceTabellone In voci
                        If filtro = "" OrElse String.Equals(v.Binario, filtro, StringComparison.OrdinalIgnoreCase) Then nuove.Add(v)
                    Next
                    If nuove.Count = 0 Then Return False
                    visibili.Clear()
                    visibili.AddRange(nuove)
                    lista.BeginUpdate()
                    lista.svuotaLista()
                    For i As Integer = 0 To visibili.Count - 1
                        lista.aggiungiElemento(visibili(i).Descrizione(partenze), i.ToString())
                    Next
                    lista.EndUpdate()
                    Return True
                End Function

            Riempi()

            lista.aggiungiOpzione("Cerca per binario",
                Sub(s, e)
                    Try
                        Dim campi As New Campi()
                        campi.Aggiungi("Binario", "b", True)
                        AddHandler campi.Fine,
                            Sub()
                                Try
                                    If Not campi.TuttiObbligatoriRiempiti() Then Return
                                    Dim precedente As String = filtro
                                    filtro = campi.RecuperaValore("b").Trim()
                                    If Not Riempi() Then
                                        Dim richiesto As String = filtro
                                        filtro = precedente
                                        Messaggio("Nessun treno sul binario " & richiesto & ".")
                                    End If
                                Catch ex As Exception
                                    _log.ScriviLog("MenuTreni.CercaPerBinario fine: " & ex.Message)
                                End Try
                            End Sub
                        campi.Avvia()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.CercaPerBinario: " & ex.Message)
                    End Try
                End Sub)
            lista.aggiungiOpzione("Tutti i binari",
                Sub(s, e)
                    Try
                        filtro = ""
                        Riempi()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.TuttiIBinari: " & ex.Message)
                    End Try
                End Sub)
            lista.aggiungiOpzione("Aggiorna", Sub(s, e) AggiornaTabellone(stazione, partenze, Sub(nuove) voci = nuove, Sub() Riempi()))
            lista.aggiungiOpzione("Acquista il biglietto",
                Sub(s, e)
                    Try
                        Dim v As VoceTabellone = visibili(CInt(lista.ottieniElemento()))
                        ApriBiglietto(If(partenze, stazione.Nome, v.Localita), If(partenze, v.Localita, stazione.Nome), v.Orario)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.LinkTabellone: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        ApriDettaglioDaTabellone(stazione, partenze, visibili(CInt(id)))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ApriTabellone selezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub AggiornaTabellone(stazione As StazioneScelta, partenze As Boolean, imposta As Action(Of List(Of VoceTabellone)), ridisegna As Action)
        Try
            Dim tab As Tabellone =
                Await WGProcessoInCorso.EseguiAsync(Of Tabellone)("Ricerca corse in corso", Function() TempoRealeTreni.OttieniTabelloneAsync(stazione, partenze))
            If tab.Voci.Count = 0 Then
                Messaggio("Non ci sono " & If(partenze, "partenze", "arrivi") & " da mostrare per " & stazione.Nome & ".")
                Return
            End If
            imposta(tab.Voci)
            ridisegna()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.AggiornaTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

    Private Async Sub ApriDettaglioDaTabellone(stazione As StazioneScelta, partenze As Boolean, v As VoceTabellone)
        Try
            If v.Fonte = "Italo" Then
                Dim d As DettaglioTreno =
                    Await WGProcessoInCorso.EseguiAsync(Of DettaglioTreno)("Ricerca corse in corso", Function() TempoRealeTreni.TrenoItaloAsync(v.Numero))
                If d Is Nothing Then d = TempoRealeTreni.DettaglioItaloDaTabellone(v, stazione.Nome, partenze)
                If d Is Nothing Then
                    Messaggio("Non ci sono informazioni sul percorso di questo treno.")
                    Return
                End If
                MostraDettaglio(d)
                Return
            End If
            Dim trovati As List(Of TrenoTrovato) =
                Await WGProcessoInCorso.EseguiAsync(Of List(Of TrenoTrovato))("Ricerca corse in corso", Function() TempoRealeTreni.CercaTrenoTrenitaliaAsync(v.Numero))
            If trovati.Count = 0 Then
                Messaggio("Non ci sono informazioni sul percorso del treno " & v.Numero & ".")
                Return
            End If
            Dim scelto As TrenoTrovato = trovati(0)
            For Each x As TrenoTrovato In trovati
                If TempoRealeTreni.DaMillisecondi(x.Millisecondi).Date = v.Orario.Date Then
                    scelto = x
                    Exit For
                End If
            Next
            Await MostraAndamentoTrenitaliaAsync(scelto)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriDettaglioDaTabellone: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

#End Region

#Region "Ricerche e corse salvate"

    Private Sub SalvaRicercaRecente(da As LuogoScelto, a As LuogoScelto)
        Try
            Dim r As New RicercaSalvata()
            r.Da = da.Nome
            r.CodiceDa = da.CodiceTrainline
            r.A = a.Nome
            r.CodiceA = a.CodiceTrainline
            r.Mezzi = _mezzi
            r.Tipo = CInt(_tipo)
            r.Passeggeri = _passeggeri
            ArchivioTreni.SalvaRicerca(r)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.SalvaRicercaRecente: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CorsaDaOfferta(da As String, a As String, origine As String, destinazione As String, offerta As OffertaTrainline) As CorsaSalvata
        Dim c As New CorsaSalvata()
        c.Da = da
        c.CodiceDa = origine
        c.A = a
        c.CodiceA = destinazione
        c.Ora = offerta.Partenza.ToString("HH:mm")
        Dim nomi As New List(Of String)()
        For Each t As TrattoTrainline In offerta.Tratti
            If t.Nome <> "" Then nomi.Add(t.Nome)
        Next
        c.Nome = String.Join(" e ", nomi)
        c.Operatore = offerta.Operatore
        Return c
    End Function

    ''' <summary>Le ultime ricerche: sceglierne una chiede giorno e ora e la ripete con le stesse scelte.</summary>
    Private Sub RicercheRecenti()
        Try
            Dim elenco As List(Of RicercaSalvata) = ArchivioTreni.LeggiRicerche()
            If elenco.Count = 0 Then
                Messaggio("Non ci sono ricerche recenti.")
                Return
            End If
            Dim lista As WGMenuLista = NuovaLista("Ricerche recenti")
            For Each r As RicercaSalvata In elenco
                lista.aggiungiElemento(r.Descrizione(), r.Id)
            Next
            lista.aggiungiOpzione("Elimina",
                Sub(s, e)
                    Try
                        Dim id As String = lista.ottieniElemento()
                        If String.IsNullOrEmpty(id) Then Return
                        ArchivioTreni.EliminaRicerca(id)
                        lista.RimuoviElemento(id)
                        elenco.RemoveAll(Function(x) x.Id = id)
                        If elenco.Count = 0 Then lista.abbandona()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.RicercheRecenti elimina: " & ex.Message)
                    End Try
                End Sub)
            lista.aggiungiOpzione("Svuota l'elenco",
                Sub(s, e)
                    Try
                        ArchivioTreni.SvuotaRicerche()
                        elenco.Clear()
                        lista.abbandona()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.RicercheRecenti svuota: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim r As RicercaSalvata = elenco.Find(Function(x) x.Id = id)
                        If r IsNot Nothing Then RipetiRicerca(r)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.RicercheRecenti selezione: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.RicercheRecenti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RipetiRicerca(r As RicercaSalvata)
        Try
            Dim campi As New Campi()
            campi.AggiungiData("Data", "d", True, False, DateTime.Now.ToShortDateString())
            AggiungiCampiOrario(campi)
            AddHandler campi.Fine,
                Sub()
                    Try
                        If Not campi.TuttiObbligatoriRiempiti() Then Return
                        Dim data As DateTime? = LeggiData(campi.RecuperaValore("d"))
                        Dim orario As TimeSpan? = LeggiOrarioScelto(campi)
                        If Not data.HasValue OrElse Not orario.HasValue Then
                            Messaggio("Non ho capito la data o l'orario.")
                            Return
                        End If
                        _mezzi = r.Mezzi
                        _tipo = CType(r.Tipo, TipoTreni)
                        _passeggeri = r.Passeggeri
                        Dim da As New LuogoScelto() With {.Nome = r.Da, .Testo = r.Da, .CodiceTrainline = r.CodiceDa}
                        Dim a As New LuogoScelto() With {.Nome = r.A, .Testo = r.A, .CodiceTrainline = r.CodiceA}
                        SalvaRicercaRecente(da, a)
                        EseguiRicercaCompleta(da, a, data.Value.Add(orario.Value), Nothing)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.RipetiRicerca fine: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.RipetiRicerca: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Le corse preferite o le ultime aperte. Sceglierne una chiede il giorno e cerca di nuovo la stessa tratta a quell'ora,
    ''' con l'elenco già sulla corsa.
    ''' </summary>
    Private Sub ElencoCorse(preferite As Boolean)
        Try
            Dim elenco As List(Of CorsaSalvata) = ArchivioTreni.LeggiCorse(preferite)
            If elenco.Count = 0 Then
                Messaggio(If(preferite, "Non ci sono corse preferite.", "Non ci sono corse recenti."))
                Return
            End If
            Dim lista As WGMenuLista = NuovaLista(If(preferite, "Corse preferite", "Corse recenti"))
            For Each c As CorsaSalvata In elenco
                lista.aggiungiElemento(c.Descrizione(), c.Id)
            Next
            If Not preferite Then
                lista.aggiungiOpzione("Aggiungi ai preferiti",
                    Sub(s, e)
                        Try
                            Dim c As CorsaSalvata = elenco.Find(Function(x) x.Id = lista.ottieniElemento())
                            If c IsNot Nothing Then
                                ArchivioTreni.SalvaCorsa(True, c)
                                Messaggio("Corsa aggiunta ai preferiti.")
                            End If
                        Catch ex As Exception
                            _log.ScriviLog("MenuTreni.ElencoCorse preferita: " & ex.Message)
                        End Try
                    End Sub)
            End If
            lista.aggiungiOpzione(If(preferite, "Elimina dai preferiti", "Elimina"),
                Sub(s, e)
                    Try
                        Dim id As String = lista.ottieniElemento()
                        If String.IsNullOrEmpty(id) Then Return
                        ArchivioTreni.EliminaCorsa(preferite, id)
                        lista.RimuoviElemento(id)
                        elenco.RemoveAll(Function(x) x.Id = id)
                        If elenco.Count = 0 Then lista.abbandona()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ElencoCorse elimina: " & ex.Message)
                    End Try
                End Sub)
            lista.aggiungiOpzione(If(preferite, "Svuota i preferiti", "Svuota l'elenco"),
                Sub(s, e)
                    Try
                        ArchivioTreni.SvuotaCorse(preferite)
                        elenco.Clear()
                        lista.abbandona()
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ElencoCorse svuota: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim c As CorsaSalvata = elenco.Find(Function(x) x.Id = id)
                        If c IsNot Nothing Then RipetiCorsa(c)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.ElencoCorse selezione: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ElencoCorse: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RipetiCorsa(c As CorsaSalvata)
        Try
            Dim campi As New Campi()
            campi.AggiungiData("Data", "d", True, False, DateTime.Now.ToShortDateString())
            AddHandler campi.Fine,
                Sub()
                    Try
                        If Not campi.TuttiObbligatoriRiempiti() Then Return
                        Dim data As DateTime? = LeggiData(campi.RecuperaValore("d"))
                        Dim ora As TimeSpan
                        If Not data.HasValue OrElse Not TimeSpan.TryParse(c.Ora, ora) Then
                            Messaggio("Non ho capito la data.")
                            Return
                        End If
                        Dim da As New LuogoScelto() With {.Nome = c.Da, .Testo = c.Da, .CodiceTrainline = c.CodiceDa}
                        Dim a As New LuogoScelto() With {.Nome = c.A, .Testo = c.A, .CodiceTrainline = c.CodiceA}
                        EseguiRicercaCompleta(da, a, data.Value.Add(ora), ora)
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.RipetiCorsa fine: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.RipetiCorsa: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

#End Region

#Region "Treno per numero"

    Private Sub ChiediNumeroTreno()
        Try
            Dim campi As New Campi()
            campi.AggiungiNumerico("Numero del treno", "n", True)
            AddHandler campi.Fine, Sub() FineNumeroTreno(campi)
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ChiediNumeroTreno: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub FineNumeroTreno(campi As Campi)
        Try
            If Not campi.TuttiObbligatoriRiempiti() Then Return
            Dim numero As String = campi.RecuperaValore("n").Trim()
            If numero = "" Then Return

            Dim scelte As New List(Of SceltaTreno)()
            Dim errori As New List(Of Exception)()
            Await WGProcessoInCorso.EseguiAsync("Ricerca corse in corso",
                Async Function() As Task
                    Try
                        For Each t As TrenoTrovato In Await TempoRealeTreni.CercaTrenoTrenitaliaAsync(numero)
                            Dim s As New SceltaTreno()
                            s.Testo = t.Descrizione
                            s.Trenitalia = t
                            scelte.Add(s)
                        Next
                    Catch ex As Exception
                        errori.Add(ex)
                    End Try
                    Try
                        Dim d As DettaglioTreno = Await TempoRealeTreni.TrenoItaloAsync(numero)
                        If d IsNot Nothing Then
                            Dim s As New SceltaTreno()
                            s.Testo = "Italo " & numero & " da " & d.Origine & " a " & d.Destinazione
                            s.Italo = d
                            scelte.Add(s)
                        End If
                    Catch ex As Exception
                        errori.Add(ex)
                    End Try
                End Function)

            If scelte.Count = 0 Then
                If errori.Count >= 2 Then
                    Messaggio(NomeMessaggio(errori(0)))
                Else
                    Messaggio("Non trovo nessun treno con il numero " & numero & ".")
                End If
                Return
            End If
            If scelte.Count = 1 Then
                ApriSceltaTreno(scelte(0))
                Return
            End If
            Dim lista As WGMenuLista = NuovaLista("Treno " & numero)
            For i As Integer = 0 To scelte.Count - 1
                lista.aggiungiElemento(scelte(i).Testo, i.ToString())
            Next
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        ApriSceltaTreno(scelte(CInt(id)))
                    Catch ex As Exception
                        _log.ScriviLog("MenuTreni.FineNumeroTreno selezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.FineNumeroTreno: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

    Private Async Sub ApriSceltaTreno(s As SceltaTreno)
        Try
            If s.Italo IsNot Nothing Then
                MostraDettaglio(s.Italo)
            ElseIf s.Trenitalia IsNot Nothing Then
                Await MostraAndamentoTrenitaliaAsync(s.Trenitalia)
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.ApriSceltaTreno: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Sub

#End Region

#Region "Dettaglio di un treno"

    Private Async Function MostraAndamentoTrenitaliaAsync(t As TrenoTrovato) As Task
        Try
            Dim d As DettaglioTreno =
                Await WGProcessoInCorso.EseguiAsync(Of DettaglioTreno)("Ricerca corse in corso", Function() TempoRealeTreni.AndamentoTrenitaliaAsync(t))
            If d Is Nothing OrElse d.Fermate.Count = 0 Then
                Messaggio("Non ci sono informazioni sul percorso del treno " & t.Numero & ".")
                Return
            End If
            MostraDettaglio(d)
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.MostraAndamentoTrenitaliaAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Messaggio(NomeMessaggio(ex))
        End Try
    End Function

    Private Sub MostraDettaglio(d As DettaglioTreno)
        Try
            If d.Fermate.Count = 0 Then
                Messaggio("Non ci sono informazioni sul percorso di questo treno.")
                Return
            End If
            Dim lista As WGMenuLista = NuovaLista(d.Etichetta)
            Dim riepilogo As New StringBuilder()
            If d.Origine <> "" AndAlso d.Destinazione <> "" Then riepilogo.Append("Da " & d.Origine & " a " & d.Destinazione)
            If d.Stato <> "" Then riepilogo.Append(If(riepilogo.Length > 0, ", ", "") & d.Stato)
            If riepilogo.Length > 0 Then lista.aggiungiElemento(riepilogo.ToString(), "riepilogo")
            For i As Integer = 0 To d.Fermate.Count - 1
                lista.aggiungiElemento(d.Fermate(i).Descrizione(), i.ToString())
            Next
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuTreni.MostraDettaglio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

#End Region

End Class
