Imports wgcontrols
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports System.Threading
Imports System.Windows.Forms
Imports System.Globalization
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports WGDati

' Gestione della vista calendario mensile. Include il calcolo delle feste locali, la consultazione di santi e onomastici e le
' informazioni astronomiche giornaliere.
Friend Class MenuCalendario

    Private log As New Logger()
    Private ReadOnly _config As New WGConfig()
    Private ReadOnly _httpClient As New HttpClient()

    Private calendario As WGMenuLista
    Private dataCorrente As Date
    Private meseavanti As Boolean
    Private dataset As String
    Private campo As WGCampo
    Private fest As New Dictionary(Of String, String)()
    Private campi As Campi
    Private _idFestaLocale As String = ""

    Private processoInCorso As WGProcessoInCorso
    Private listaSanti As WGMenuLista

    Private _titoloAlmanaccoRisultato As String = ""
    Private _testoAlmanaccoRisultato As String = ""
    Private _testoLunaRisultato As String = ""
    Private _testoSoleRisultato As String = ""
    Private _santiRisultato As List(Of Santo) = New List(Of Santo)()

    Private listaOnomastici As WGMenuLista
    Private _onomasticiRisultato As List(Of Onomastico) = New List(Of Onomastico)()
    Private _onomasticoSelezionato As String = ""
    Private _dataOnomasticoCorrente As String = ""
    Private _infoOnomasticoRisultato As String = ""

    Private Shared ReadOnly _festePatro As New Dictionary(Of String, Tuple(Of Integer, Integer))(StringComparer.OrdinalIgnoreCase) From {
        {"Verona", New Tuple(Of Integer, Integer)(5, 21)},
        {"Padova", New Tuple(Of Integer, Integer)(6, 13)},
        {"Trieste", New Tuple(Of Integer, Integer)(11, 3)},
        {"Brescia", New Tuple(Of Integer, Integer)(2, 15)},
        {"Taranto", New Tuple(Of Integer, Integer)(5, 10)},
        {"Prato", New Tuple(Of Integer, Integer)(12, 26)},
        {"Modena", New Tuple(Of Integer, Integer)(1, 31)},
        {"Reggio Emilia", New Tuple(Of Integer, Integer)(11, 24)},
        {"Perugia", New Tuple(Of Integer, Integer)(1, 29)},
        {"Ravenna", New Tuple(Of Integer, Integer)(7, 23)},
        {"Livorno", New Tuple(Of Integer, Integer)(5, 22)},
        {"Foggia", New Tuple(Of Integer, Integer)(3, 22)},
        {"Rimini", New Tuple(Of Integer, Integer)(10, 1)},
        {"Salerno", New Tuple(Of Integer, Integer)(9, 21)},
        {"Ferrara", New Tuple(Of Integer, Integer)(4, 23)},
        {"Sassari", New Tuple(Of Integer, Integer)(8, 15)},
        {"Latina", New Tuple(Of Integer, Integer)(4, 6)},
        {"Monza", New Tuple(Of Integer, Integer)(6, 22)},
        {"Bergamo", New Tuple(Of Integer, Integer)(8, 26)},
        {"Trento", New Tuple(Of Integer, Integer)(6, 26)},
        {"Vicenza", New Tuple(Of Integer, Integer)(4, 25)},
        {"Bolzano", New Tuple(Of Integer, Integer)(8, 15)},
        {"Novara", New Tuple(Of Integer, Integer)(1, 22)},
        {"Piacenza", New Tuple(Of Integer, Integer)(7, 4)},
        {"Ancona", New Tuple(Of Integer, Integer)(5, 4)},
        {"Andria", New Tuple(Of Integer, Integer)(9, 14)},
        {"Arezzo", New Tuple(Of Integer, Integer)(8, 7)},
        {"Udine", New Tuple(Of Integer, Integer)(7, 12)},
        {"Cesena", New Tuple(Of Integer, Integer)(6, 27)},
        {"Pescara", New Tuple(Of Integer, Integer)(10, 10)},
        {"Lecce", New Tuple(Of Integer, Integer)(8, 26)},
        {"Brindisi", New Tuple(Of Integer, Integer)(9, 3)},
        {"Pisa", New Tuple(Of Integer, Integer)(6, 17)},
        {"Siena", New Tuple(Of Integer, Integer)(12, 1)},
        {"Lucca", New Tuple(Of Integer, Integer)(9, 13)},
        {"Pistoia", New Tuple(Of Integer, Integer)(1, 25)},
        {"Grosseto", New Tuple(Of Integer, Integer)(8, 10)},
        {"Poggibonsi", New Tuple(Of Integer, Integer)(11, 8)},
        {"Colle di Val d'Elsa", New Tuple(Of Integer, Integer)(6, 1)},
        {"San Gimignano", New Tuple(Of Integer, Integer)(1, 31)},
        {"Aosta", New Tuple(Of Integer, Integer)(9, 7)},
        {"Alessandria", New Tuple(Of Integer, Integer)(11, 26)},
        {"Asti", New Tuple(Of Integer, Integer)(1, 26)},
        {"Cuneo", New Tuple(Of Integer, Integer)(9, 29)},
        {"Vercelli", New Tuple(Of Integer, Integer)(8, 1)},
        {"Verbania", New Tuple(Of Integer, Integer)(10, 24)},
        {"Varese", New Tuple(Of Integer, Integer)(9, 8)},
        {"Como", New Tuple(Of Integer, Integer)(8, 31)},
        {"Sondrio", New Tuple(Of Integer, Integer)(6, 19)},
        {"Lecco", New Tuple(Of Integer, Integer)(8, 22)},
        {"Mantova", New Tuple(Of Integer, Integer)(3, 18)},
        {"Cremona", New Tuple(Of Integer, Integer)(11, 13)},
        {"Lodi", New Tuple(Of Integer, Integer)(1, 19)},
        {"Pavia", New Tuple(Of Integer, Integer)(8, 7)},
        {"Treviso", New Tuple(Of Integer, Integer)(4, 27)},
        {"Rovigo", New Tuple(Of Integer, Integer)(11, 26)},
        {"Belluno", New Tuple(Of Integer, Integer)(11, 9)},
        {"Pordenone", New Tuple(Of Integer, Integer)(4, 28)},
        {"Gorizia", New Tuple(Of Integer, Integer)(3, 16)},
        {"Imperia", New Tuple(Of Integer, Integer)(5, 26)},
        {"Savona", New Tuple(Of Integer, Integer)(3, 18)},
        {"La Spezia", New Tuple(Of Integer, Integer)(6, 19)},
        {"Parma", New Tuple(Of Integer, Integer)(1, 13)},
        {"Forlì", New Tuple(Of Integer, Integer)(2, 4)},
        {"Macerata", New Tuple(Of Integer, Integer)(8, 31)},
        {"Fermo", New Tuple(Of Integer, Integer)(8, 16)},
        {"Ascoli Piceno", New Tuple(Of Integer, Integer)(8, 5)},
        {"Pesaro", New Tuple(Of Integer, Integer)(9, 27)},
        {"Terni", New Tuple(Of Integer, Integer)(2, 11)},
        {"Viterbo", New Tuple(Of Integer, Integer)(9, 4)},
        {"Rieti", New Tuple(Of Integer, Integer)(12, 3)},
        {"Frosinone", New Tuple(Of Integer, Integer)(12, 20)},
        {"Caserta", New Tuple(Of Integer, Integer)(1, 20)},
        {"Avellino", New Tuple(Of Integer, Integer)(2, 14)},
        {"Benevento", New Tuple(Of Integer, Integer)(8, 24)},
        {"Potenza", New Tuple(Of Integer, Integer)(5, 30)},
        {"Matera", New Tuple(Of Integer, Integer)(7, 2)},
        {"Cosenza", New Tuple(Of Integer, Integer)(2, 14)},
        {"Catanzaro", New Tuple(Of Integer, Integer)(7, 16)},
        {"Crotone", New Tuple(Of Integer, Integer)(10, 23)},
        {"Vibo Valentia", New Tuple(Of Integer, Integer)(3, 1)},
        {"Agrigento", New Tuple(Of Integer, Integer)(11, 25)},
        {"Caltanissetta", New Tuple(Of Integer, Integer)(9, 29)},
        {"Enna", New Tuple(Of Integer, Integer)(7, 2)},
        {"Ragusa", New Tuple(Of Integer, Integer)(8, 29)},
        {"Siracusa", New Tuple(Of Integer, Integer)(12, 13)},
        {"Trapani", New Tuple(Of Integer, Integer)(8, 2)},
        {"Nuoro", New Tuple(Of Integer, Integer)(8, 24)},
        {"Oristano", New Tuple(Of Integer, Integer)(2, 13)},
        {"Olbia", New Tuple(Of Integer, Integer)(1, 17)},
        {"Campobasso", New Tuple(Of Integer, Integer)(4, 23)}
    }

    Public Sub New()
        Try
            _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("WinGabriel-Calendario/1.0")
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json")
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            AvviaCalendario()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.Avvia - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaFestaLocale()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.ImpostaFestaLocale (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try
            Dim idEl As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                idEl = elementi(0).Trim()
            End If

            _idFestaLocale = idEl
            Dim valoreAttuale As String = LeggiFestaLocale(_idFestaLocale)

            campi = New Campi()
            campi.Aggiungi("Nome della festa locale del " & _idFestaLocale, "c.festa", True, , valoreAttuale)
            AddHandler campi.Fine, AddressOf FineImpostaFestaLocale
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ImpostaFestaLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineImpostaFestaLocale()
        Try
            If campi Is Nothing Then Exit Sub
            If Not campi.TuttiObbligatoriRiempiti() Then
                campi = Nothing
                Exit Sub
            End If

            Dim nome As String = campi.RecuperaValore("c.festa").Trim()
            campi = Nothing

            If String.IsNullOrWhiteSpace(nome) Then Exit Sub

            SalvaFestaLocale(_idFestaLocale, nome)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Festa locale impostata: " & nome)
            msg.ShowDialog()

            RicaricaEPosiziona(_idFestaLocale)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.FineImpostaFestaLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub RicaricaEPosiziona(idEl As String)
        Try
            Await CaricaMeseAsync(False)
            If calendario IsNot Nothing Then
                calendario.posiziona(idEl)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.RicaricaEPosiziona - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ChiaveFestaLocale(idEl As String) As String
        Try
            Dim dt As Date
            If Date.TryParseExact(idEl, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                Return "festalocale_" & dt.ToString("ddMMyyyy")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ChiaveFestaLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Private Function LeggiFestaLocale(idEl As String) As String
        Try
            Dim chiave As String = ChiaveFestaLocale(idEl)
            If Not String.IsNullOrEmpty(chiave) AndAlso _config.EsisteId(chiave) Then
                Return If(_config.LeggiValore(chiave), "")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.LeggiFestaLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Private Sub SalvaFestaLocale(idEl As String, nome As String)
        Try
            Dim chiave As String = ChiaveFestaLocale(idEl)
            If String.IsNullOrEmpty(chiave) Then Exit Sub
            If _config.EsisteId(chiave) Then
                _config.ModificaValore(chiave, nome)
            Else
                _config.AggiungiValore(chiave, nome)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.SalvaFestaLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function LeggiCitta() As String
        Try
            Dim valori As List(Of String) = _config.LeggiValoriMultipli("calendario_citta")
            If valori IsNot Nothing AndAlso valori.Count >= 2 Then
                Dim nome As String = If(valori(1), "").Trim()
                Dim regione As String = ""
                Dim paese As String = ""
                If valori.Count >= 3 Then regione = If(valori(2), "").Trim()
                If valori.Count >= 4 Then paese = If(valori(3), "").Trim()

                Dim descrizione As String = nome
                If Not String.IsNullOrWhiteSpace(regione) Then descrizione &= ", " & regione
                If Not String.IsNullOrWhiteSpace(paese) Then descrizione &= " (" & paese & ")"
                If Not String.IsNullOrWhiteSpace(descrizione) Then Return descrizione
            End If

            If _config.EsisteId("calendario_citta") Then
                Dim valoreSingolo As String = If(_config.LeggiValore("calendario_citta"), "")
                If Not String.IsNullOrWhiteSpace(valoreSingolo) Then
                    Return valoreSingolo.Trim()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.LeggiCitta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Private Function OttieniFestaPatrono(citta As String, anno As Integer) As Tuple(Of String, Date)
        If String.IsNullOrWhiteSpace(citta) Then Return Nothing
        Dim key As String = citta.Trim()
        If _festePatro.ContainsKey(key) Then
            Dim md As Tuple(Of Integer, Integer) = _festePatro(key)
            Try
                Dim dt As New Date(anno, md.Item1, md.Item2)
                Return New Tuple(Of String, Date)("Festa del Patrono (" & citta & ")", dt)
            Catch ex As Exception
                log.ScriviLog("MenuCalendario.OttieniFestaPatrono - " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
        End If
        Return Nothing
    End Function

    Private Sub AvviaCalendario()
        Try
            calendario = New WGMenuLista()
            calendario.multiselezioneConsentita = False
            calendario.impostaTitolo("Menù calendario")
            calendario.ciclono = True

            AddHandler calendario.escPremuto, AddressOf calendario_abbandona
            AddHandler calendario.ElementoSelezionato, AddressOf dataselezionata
            AddHandler calendario.iniziolista, AddressOf MesePrecedente
            AddHandler calendario.finelista, AddressOf MeseSuccessivo

            calendario.aggiungiOpzione("Mese precedente", AddressOf MesePrecedente)
            calendario.aggiungiOpzione("Mese Successivo", AddressOf MeseSuccessivo)
            calendario.aggiungiOpzione("Imposta festa locale", AddressOf ImpostaFestaLocale)
            calendario.aggiungiOpzione("Imposta città del calendario", AddressOf ChiediCittaCalendario)
            calendario.aggiungiOpzione("Crea nuovo appuntamento nell'agenda", AddressOf agendanuovo)
            calendario.aggiungiOpzione("Consulta agenda del giorno", AddressOf agendagiorno)
            calendario.aggiungiOpzione("Mostra santo del giorno", AddressOf MostraSanto)
            calendario.aggiungiOpzione("Mostra fasi lunari", AddressOf MostraLuna)
            calendario.aggiungiOpzione("Mostra orari del sole", AddressOf MostraSole)
            calendario.aggiungiOpzione("Ricerca onomastico", AddressOf RicercaOnomastico)

            dataCorrente = Date.Today
            AvvioAsync()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.AvviaCalendario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub AvvioAsync()
        Try
            Await CaricaMeseAsync(True)
            calendario.avvia()
            calendario.posiziona(Date.Today.ToString("dd/MM/yyyy"))
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.AvvioAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub dataselezionata()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.dataselezionata (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            dataset = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dataset = elementi(0).Trim()
            End If

            Dim menu As New WGMenu()
            menu.aggiungi("Crea un nuovo appuntamento nell'agenda", AddressOf agendanuovo)
            menu.aggiungi("Consulta agenda del giorno", AddressOf agendagiorno)
            menu.aggiungi("Crea nuova nota con la data di oggi", AddressOf nuovanota)
            menu.aggiungi("Consulta note del giorno", AddressOf notegiorno)
            menu.aggiungi("Consulta Almanacco del giorno", AddressOf MostraAlmanaccoDelGiorno)
            menu.aggiungi("Onomastici del giorno", AddressOf MostraOnomasticiDelGiorno)
            menu.avvia("Menù cosa fare data " & dataset)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.dataselezionata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub agendanuovo()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.agendanuovo (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim idEl As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                idEl = elementi(0).Trim()
            End If

            Dim agenda As New MenuAgenda()
            agenda.nuovoappuntamento(idEl)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.agendanuovo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub agendagiorno()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.agendagiorno (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim idEl As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                idEl = elementi(0).Trim()
            End If

            Dim agenda As New MenuAgenda()
            agenda.visualizzaprogramma(idEl)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.agendagiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub nuovanota()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.nuovanota (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim idEl As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                idEl = elementi(0).Trim()
            End If

            Dim menunota As New MenuNote()
            menunota.NotaExt(idEl)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.nuovanota - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub notegiorno()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.notegiorno (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim idEl As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                idEl = elementi(0).Trim()
            End If

            Dim note As New MenuNote()
            note.Consulta(idEl)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.notegiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub


    Private Sub MostraAlmanaccoDelGiorno()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.MostraAlmanaccoDelGiorno (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim dataStr As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dataStr = elementi(0).Trim()
            End If

            MostraAlmanacco(dataStr)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraAlmanaccoDelGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Async Sub MostraAlmanacco(Optional dataStr As String = "")
        Try
            Dim dataAlmanacco As String = dataStr
            If String.IsNullOrWhiteSpace(dataAlmanacco) Then
                dataAlmanacco = Date.Today.ToString("dd/MM/yyyy")
            End If

            Dim citta As String = LeggiCitta()

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Caricamento almanacco in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim almanacco As New CLSAlmanacco()
                                        Dim coord As CoordinateCitta = almanacco.RisolviCoordinate(0, 0, citta).GetAwaiter().GetResult()
                                        _testoAlmanaccoRisultato = almanacco.OttieniTestoAlmanacco(coord.Lat, coord.Lon, citta).GetAwaiter().GetResult()
                                        _titoloAlmanaccoRisultato = "Almanacco del " & dataAlmanacco
                                        If Not String.IsNullOrWhiteSpace(coord.NomeRisolto) Then
                                            _titoloAlmanaccoRisultato &= " - " & coord.NomeRisolto
                                        End If
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraAlmanacco (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _titoloAlmanaccoRisultato = "Almanacco del " & dataAlmanacco
                                        _testoAlmanaccoRisultato = "Errore durante il caricamento dell'almanacco."
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            ApreCampoTesto(_titoloAlmanaccoRisultato, _testoAlmanaccoRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraAlmanacco - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
        End Try
    End Sub

    Private Async Sub MostraLuna()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.MostraLuna (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim dtStr As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dtStr = elementi(0).Trim()
            End If

            Dim dt As Date
            If Not Date.TryParseExact(dtStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Exit Sub

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Calcolo della fase lunare in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim an As Double = dt.Year
                                        Dim ms As Double = dt.Month
                                        Dim gg As Double = dt.Day

                                        If ms < 3 Then
                                            an -= 1
                                            ms += 12
                                        End If
                                        ms += 1
                                        Dim c As Double = 365.25 * an
                                        Dim e As Double = 30.6 * ms
                                        Dim jd As Double = c + e + gg - 694039.09
                                        jd = jd / 29.5305882
                                        Dim b As Integer = Fix(jd)
                                        jd -= b
                                        Dim fs As Double = Math.Round(jd * 8)
                                        If fs >= 8 Then fs = 0

                                        Select Case CInt(fs)
                                            Case 0 : _testoLunaRisultato = "Luna Nuova"
                                            Case 1 : _testoLunaRisultato = "Luna Crescente"
                                            Case 2 : _testoLunaRisultato = "Primo Quarto"
                                            Case 3 : _testoLunaRisultato = "Gibbosa Crescente"
                                            Case 4 : _testoLunaRisultato = "Luna Piena"
                                            Case 5 : _testoLunaRisultato = "Gibbosa Calante"
                                            Case 6 : _testoLunaRisultato = "Ultimo Quarto"
                                            Case 7 : _testoLunaRisultato = "Luna Calante"
                                            Case Else : _testoLunaRisultato = "Non calcolabile"
                                        End Select
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraLuna (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _testoLunaRisultato = "Errore calcolo luna."
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            ApreCampoTesto("Fase Lunare", _testoLunaRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraLuna - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore calcolo luna.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Async Sub MostraSole()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.MostraSole (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim dtStr As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dtStr = elementi(0).Trim()
            End If

            Dim dt As Date
            If Not Date.TryParseExact(dtStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Exit Sub

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Recupero degli orari del sole in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim citta As String = LeggiCitta()
                                        Dim almanacco As New CLSAlmanacco()
                                        Dim coord As CoordinateCitta = almanacco.RisolviCoordinate(0, 0, citta).GetAwaiter().GetResult()

                                        Dim lat As Double = If(coord.Lat <> 0, coord.Lat, 43.47)
                                        Dim lon As Double = If(coord.Lon <> 0, coord.Lon, 11.15)

                                        Dim url As String = "https://api.sunrisesunset.io/json?lat=" & lat.ToString(CultureInfo.InvariantCulture) & "&lng=" & lon.ToString(CultureInfo.InvariantCulture) & "&date=" & dt.ToString("yyyy-MM-dd") & "&time_format=24"
                                        Dim json As String = _httpClient.GetStringAsync(url).GetAwaiter().GetResult()
                                        Dim obj As JObject = JObject.Parse(json)

                                        Dim res As String = "Dati non disponibili."
                                        If obj("status")?.ToString() = "OK" Then
                                            Dim r As JToken = obj("results")
                                            Dim alba As String = If(r("sunrise")?.ToString(), "N/D")
                                            Dim tram As String = If(r("sunset")?.ToString(), "N/D")
                                            Dim noon As String = If(r("solar_noon")?.ToString(), "N/D")
                                            Dim durGiorno As String = If(r("day_length")?.ToString(), "")
                                            Dim goldH As String = If(r("golden_hour")?.ToString(), "N/D")

                                            Dim sb As New StringBuilder()
                                            Dim localitaDesc As String = If(Not String.IsNullOrWhiteSpace(coord.NomeRisolto), coord.NomeRisolto, "coordinate predefinite")
                                            sb.AppendLine("Orari sole per " & localitaDesc & " - " & dtStr)
                                            sb.AppendLine("Alba: " & alba)
                                            sb.AppendLine("Tramonto: " & tram)
                                            sb.AppendLine("Mezzogiorno solare: " & noon)
                                            sb.AppendLine("Ora d'oro: " & goldH)
                                            If Not String.IsNullOrWhiteSpace(durGiorno) Then
                                                Dim ts As TimeSpan
                                                If TimeSpan.TryParse(durGiorno, ts) Then
                                                    sb.AppendLine("Durata del giorno: " & ts.Hours.ToString() & " ore e " & ts.Minutes.ToString() & " minuti")
                                                End If
                                            End If
                                            res = sb.ToString()
                                        End If

                                        _testoSoleRisultato = res
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraSole (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _testoSoleRisultato = "Errore recupero sole."
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            ApreCampoTesto("Orari del sole", _testoSoleRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraSole - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore recupero sole.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Async Sub MostraSanto()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.MostraSanto (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim dtStr As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dtStr = elementi(0).Trim()
            End If

            Dim dt As Date
            If Not Date.TryParseExact(dtStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Exit Sub

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Recupero dei santi del giorno in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim almanacco As New CLSAlmanacco()
                                        _santiRisultato = almanacco.OttieniSantiDelGiorno(dt).GetAwaiter().GetResult()
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraSanto (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _santiRisultato = New List(Of Santo)()
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            MostraListaSanti("Santi del giorno - " & dt.ToString("dd/MM/yyyy"), _santiRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraSanto - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore recupero santo.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Async Sub MostraOnomasticiDelGiorno()
        Try
            Dim elementi As List(Of String) = Nothing
            Try
                elementi = calendario.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.MostraOnomasticiDelGiorno (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            Dim dtStr As String = Date.Today.ToString("dd/MM/yyyy")
            If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                dtStr = elementi(0).Trim()
            End If

            Dim dt As Date
            If Not Date.TryParseExact(dtStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Exit Sub

            _dataOnomasticoCorrente = dtStr

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Recupero degli onomastici del giorno in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim almanacco As New CLSAlmanacco()
                                        _onomasticiRisultato = almanacco.CercaOnomastici(dt).GetAwaiter().GetResult()
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraOnomasticiDelGiorno (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _onomasticiRisultato = New List(Of Onomastico)()
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            MostraListaOnomastici("Onomastici del " & dt.ToString("dd/MM/yyyy"), _onomasticiRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraOnomasticiDelGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
        End Try
    End Sub

    Private Sub RicercaOnomastico()
        Try
            _dataOnomasticoCorrente = ""
            campi = New Campi()
            campi.Aggiungi("Nome onomastico da cercare", "c.onomastico", True)
            AddHandler campi.Fine, AddressOf FineRicercaOnomastico
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.RicercaOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineRicercaOnomastico()
        Try
            If campi Is Nothing Then Exit Sub

            If Not campi.TuttiObbligatoriRiempiti() Then
                campi = Nothing
                Exit Sub
            End If

            Dim nomeCercato As String = campi.RecuperaValore("c.onomastico").Trim()
            campi = Nothing

            If String.IsNullOrWhiteSpace(nomeCercato) Then Exit Sub

            Dim risultati As New List(Of Onomastico)()
            Dim voce As New Onomastico With {
                .Id = 1,
                .Nome = NormalizzaPrimaLettera(nomeCercato)
            }
            risultati.Add(voce)

            MostraListaOnomastici("Onomastico cercato", risultati)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.FineRicercaOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function NormalizzaPrimaLettera(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim t As String = testo.Trim()
            If t.Length = 1 Then Return t.ToUpperInvariant()
            Return Char.ToUpperInvariant(t(0)) & t.Substring(1)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.NormalizzaPrimaLettera - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return testo
        End Try
    End Function

    Private Sub MostraListaOnomastici(titolo As String, onomastici As List(Of Onomastico))
        Try
            AbbandonaListaOnomastici()

            If onomastici Is Nothing OrElse onomastici.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun onomastico trovato.")
                msg.ShowDialog()
                Exit Sub
            End If

            listaOnomastici = New WGMenuLista()
            listaOnomastici.multiselezioneConsentita = False
            listaOnomastici.BeginUpdate()

            For Each o As Onomastico In onomastici
                If o Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(o.Nome) Then Continue For
                listaOnomastici.aggiungiElemento(o.Nome.Trim(), o.Nome.Trim())
            Next

            listaOnomastici.EndUpdate()
            listaOnomastici.impostaTitolo(titolo)
            AddHandler listaOnomastici.ElementoSelezionato, AddressOf OnomasticoSelezionato
            AddHandler listaOnomastici.escPremuto, AddressOf ChiudiListaOnomastici
            listaOnomastici.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraListaOnomastici - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnomasticoSelezionato()
        Try
            Dim sel As List(Of String) = Nothing
            Try
                sel = listaOnomastici.ottieniElementi()
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.OnomasticoSelezionato (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            If sel Is Nothing OrElse sel.Count = 0 OrElse String.IsNullOrWhiteSpace(sel(0)) Then Exit Sub
            _onomasticoSelezionato = sel(0).Trim()

            Dim menuOnomastico As New WGMenu()
            menuOnomastico.aggiungi("Mostra informazioni", AddressOf MostraInfoOnomasticoSelezionato)
            menuOnomastico.aggiungi("Aggiungi come ricorrenza all'agenda", AddressOf AggiungiOnomasticoComeRicorrenza)
            menuOnomastico.avvia("Onomastico: " & _onomasticoSelezionato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.OnomasticoSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub MostraInfoOnomasticoSelezionato()
        Try
            Dim nomeOno As String = _onomasticoSelezionato
            If String.IsNullOrWhiteSpace(nomeOno) Then Exit Sub

            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Recupero delle informazioni sull'onomastico in corso, attendere")

            Try
                Await Task.Run(Sub()
                                    Try
                                        Dim almanacco As New CLSAlmanacco()
                                        _infoOnomasticoRisultato = almanacco.OttieniInfoOnomastico(nomeOno).GetAwaiter().GetResult()
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.MostraInfoOnomasticoSelezionato (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                        _infoOnomasticoRisultato = "Errore durante il recupero delle informazioni sull'onomastico."
                                    End Try
                                End Sub)
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try

            ApreCampoTesto("Onomastico - " & nomeOno, _infoOnomasticoRisultato)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraInfoOnomasticoSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
        End Try
    End Sub

    Private Sub AggiungiOnomasticoComeRicorrenza()
        Try
            Dim nomeOno As String = _onomasticoSelezionato
            If String.IsNullOrWhiteSpace(nomeOno) Then Exit Sub

            Dim agenda As New MenuAgenda()
            agenda.NuovaRicorrenzaOnomastico(nomeOno, _dataOnomasticoCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.AggiungiOnomasticoComeRicorrenza - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AbbandonaListaOnomastici()
        Try
            If listaOnomastici IsNot Nothing Then
                listaOnomastici.abbandona()
                listaOnomastici = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.AbbandonaListaOnomastici - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaOnomastici()
        Try
            AbbandonaListaOnomastici()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ChiudiListaOnomastici - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraListaSanti(titolo As String, santi As List(Of Santo))
        Try
            AbbandonaListaSanti()

            If santi Is Nothing OrElse santi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun santo trovato per la data selezionata.")
                msg.ShowDialog()
                Exit Sub
            End If

            listaSanti = New WGMenuLista()
            listaSanti.invioNonConsentito = True
            listaSanti.multiselezioneConsentita = False
            listaSanti.BeginUpdate()

            For Each s As Santo In santi
                If s Is Nothing Then Continue For
                Dim voce As String = ""
                If Not String.IsNullOrWhiteSpace(s.Nome) Then voce = s.Nome.Trim()
                If Not String.IsNullOrWhiteSpace(s.Categoria) Then
                    If voce.Length > 0 Then voce &= " - "
                    voce &= s.Categoria.Trim()
                End If
                If Not String.IsNullOrWhiteSpace(s.Descrizione) Then
                    If voce.Length > 0 Then voce &= " - "
                    voce &= s.Descrizione.Trim()
                End If
                If String.IsNullOrWhiteSpace(voce) Then Continue For
                listaSanti.aggiungiElemento(voce, If(String.IsNullOrWhiteSpace(s.Nome), voce, s.Nome))
            Next

            listaSanti.EndUpdate()
            listaSanti.impostaTitolo(titolo)
            AddHandler listaSanti.escPremuto, AddressOf ChiudiListaSanti
            listaSanti.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MostraListaSanti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApreCampoTesto(titolo As String, testo As String)
        Try
            If campo IsNot Nothing Then
                Try
                    RemoveHandler campo.chiusura, AddressOf chiusuracampo
                    campo.chiudi()
                Catch
                End Try
                campo = Nothing
            End If
            campo = New WGCampo()
            AddHandler campo.chiusura, AddressOf chiusuracampo
            campo.init(titolo, testo, True, True, False, False)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ApreCampoTesto - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiProcessoInCorso()
        Try
            If processoInCorso IsNot Nothing Then
                processoInCorso.fine()
                processoInCorso = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ChiudiProcessoInCorso - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AbbandonaListaSanti()
        Try
            If listaSanti IsNot Nothing Then
                listaSanti.abbandona()
                listaSanti = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.AbbandonaListaSanti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaSanti()
        Try
            AbbandonaListaSanti()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ChiudiListaSanti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>chiusuracampo con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub chiusuracampo(senderEvento As Object, eEvento As EventArgs)
        chiusuracampo()
    End Sub

    Private Sub chiusuracampo()
        Try
            If campo IsNot Nothing Then
                RemoveHandler campo.chiusura, AddressOf chiusuracampo
                campo.chiudi()
                campo = Nothing
            End If
            AbbandonaListaSanti()
            ChiudiProcessoInCorso()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.chiusuracampo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Function ScaricaFest(anno As Integer) As Task
        Try
            Dim keyAnno As String = $"__anno_{anno}"
            If fest.ContainsKey(keyAnno) Then Exit Function

            Dim url As String = $"https://date.nager.at/api/v3/PublicHolidays/{anno}/IT"
            Dim json As String = Await _httpClient.GetStringAsync(url)
            Dim arr As JArray = JArray.Parse(json)

            Dim citta As String = LeggiCitta()
            Dim festaPatrono As Tuple(Of String, Date) = OttieniFestaPatrono(citta, anno)

            For Each item As JToken In arr
                Try
                    Dim dateStr As String = If(item("date")?.ToString(), "")
                    Dim localName As String = If(item("localName")?.ToString(), If(item("name")?.ToString(), "Festività"))
                    Dim isGlobal As Boolean = If(item("global")?.ToObject(Of Boolean)(), True)
                    Dim counties As JArray = TryCast(item("counties"), JArray)

                    Dim dtParsed As Date
                    If Not Date.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, dtParsed) Then
                        Continue For
                    End If

                    If isGlobal OrElse counties Is Nothing OrElse counties.Count = 0 Then
                        Dim idEl As String = dtParsed.ToString("dd/MM/yyyy")
                        If Not fest.ContainsKey(idEl) Then
                            fest(idEl) = localName
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog("MenuCalendario.ScaricaFest (item) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            Next

            If festaPatrono IsNot Nothing Then
                Dim idPatrono As String = festaPatrono.Item2.ToString("dd/MM/yyyy")
                If Not fest.ContainsKey(idPatrono) Then
                    fest(idPatrono) = festaPatrono.Item1
                Else
                    fest(idPatrono) &= " / " & festaPatrono.Item1
                End If
            End If

            fest(keyAnno) = "ok"

        Catch ex As HttpRequestException
            log.ScriviLog("MenuCalendario.ScaricaFest (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ScaricaFest - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Function

    Private Function EFesta(idEl As String) As Boolean
        Return fest.ContainsKey(idEl)
    End Function

    Private Function EWeekend(dt As Date) As Boolean
        Return dt.DayOfWeek = DayOfWeek.Saturday OrElse dt.DayOfWeek = DayOfWeek.Sunday
    End Function

    Private Async Function CaricaMeseAsync(oggi As Boolean) As Task
        Try
            processoInCorso = New WGProcessoInCorso()
            processoInCorso.avvia("Caricamento del mese in corso, attendere")

            Try
                Await ScaricaFest(dataCorrente.Year)

                Dim citta As String = LeggiCitta()
                Dim annoCorr As Integer = dataCorrente.Year
                Dim meseCorr As Integer = dataCorrente.Month
                Dim ggMs As Integer = DateTime.DaysInMonth(annoCorr, meseCorr)
                Dim dOgg As Date = Date.Today

                Dim righe As New List(Of Tuple(Of String, String))()
                Dim idOgg As Integer = -1

                Await Task.Run(Sub()
                                    Try
                                        Dim ag As CLSAgenda = CLSAgenda.GetInstance()
                                        Dim idx As Integer = 0
                                        For g As Integer = 1 To ggMs
                                            Dim dt As New Date(annoCorr, meseCorr, g)
                                            Dim idEl As String = dt.ToString("dd/MM/yyyy")

                                            Dim vEl As New StringBuilder()
                                            vEl.Append(dt.ToString("dddd", New CultureInfo("it-IT")) & " " & g.ToString() & " " & dt.ToString("MMMM", New CultureInfo("it-IT")) & " " & annoCorr.ToString())

                                            If EFesta(idEl) Then
                                                vEl.Append(" - FESTA: " & fest(idEl))
                                            ElseIf EWeekend(dt) Then
                                                vEl.Append(" - Fine settimana")
                                            End If

                                            Dim festaLoc As String = LeggiFestaLocale(idEl)
                                            If Not String.IsNullOrWhiteSpace(festaLoc) Then
                                                vEl.Append(" - FESTA LOCALE: " & festaLoc)
                                            End If

                                            If dt = dOgg Then
                                                idOgg = idx
                                                vEl.Append(" - Oggi")
                                            ElseIf dt = dOgg.AddDays(-1) Then
                                                vEl.Append(" - Ieri")
                                            ElseIf dt = dOgg.AddDays(1) Then
                                                vEl.Append(" - Domani")
                                            End If

                                            Try
                                                Dim apt = ag.OttieniProgrammaGiornalieroPaginato(idEl, 0, 100, CType(0, CLSAgenda.TipoOrdinamento))
                                                If apt IsNot Nothing AndAlso apt.Count > 0 Then
                                                    vEl.Append(" (" & apt.Count.ToString() & " appuntamenti)")
                                                End If
                                            Catch ex As Exception
                                                log.ScriviLog("MenuCalendario.CaricaMeseAsync (agenda) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                            End Try

                                            righe.Add(New Tuple(Of String, String)(vEl.ToString(), idEl))
                                            idx += 1
                                        Next
                                    Catch ex As Exception
                                        log.ScriviLog("MenuCalendario.CaricaMeseAsync (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                    End Try
                                End Sub)

                calendario.pulisci()
                calendario.BeginUpdate()

                Dim titoloMese As String = dataCorrente.ToString("MMMM yyyy", New CultureInfo("it-IT")).ToUpper()
                If Not String.IsNullOrWhiteSpace(citta) Then
                    titoloMese &= " - " & citta
                End If
                calendario.impostaTitolo(titoloMese)

                For Each riga In righe
                    calendario.aggiungiElemento(riga.Item1, riga.Item2)
                Next

                calendario.EndUpdate()

                If oggi AndAlso idOgg <> -1 Then
                    calendario.selezionaElementoIndice(idOgg)
                ElseIf meseavanti Then
                    calendario.selezionaElementoIndice(0)
                Else
                    calendario.selezionaElementoIndice(ggMs - 1)
                End If
            Finally
                If processoInCorso IsNot Nothing Then
                    processoInCorso.fine()
                    processoInCorso = Nothing
                End If
            End Try
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.CaricaMeseAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If processoInCorso IsNot Nothing Then
                Try
                    processoInCorso.fine()
                Catch
                End Try
                processoInCorso = Nothing
            End If
        End Try
    End Function

    Private Async Sub MesePrecedente()
        Try
            meseavanti = False
            dataCorrente = dataCorrente.AddMonths(-1)
            Await CaricaMeseAsync(False)
            Dim ultimo As Integer = DateTime.DaysInMonth(dataCorrente.Year, dataCorrente.Month)
            Dim idUltimo As String = New Date(dataCorrente.Year, dataCorrente.Month, ultimo).ToString("dd/MM/yyyy")
            calendario.posiziona(idUltimo)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MesePrecedente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub MeseSuccessivo()
        Try
            meseavanti = True
            dataCorrente = dataCorrente.AddMonths(1)
            Await CaricaMeseAsync(False)
            Dim idPrimo As String = New Date(dataCorrente.Year, dataCorrente.Month, 1).ToString("dd/MM/yyyy")
            calendario.posiziona(idPrimo)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.MeseSuccessivo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub calendario_abbandona()
        Try
            calendario.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.calendario_abbandona - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediCittaCalendario()
        Try
            campi = New Campi()
            campi.Aggiungi("Nome della città", "c.citta", True)
            AddHandler campi.Fine, AddressOf FineCittaCalendario
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.ChiediCittaCalendario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCittaCalendario()
        Try
            If campi Is Nothing Then Exit Sub

            If Not campi.TuttiObbligatoriRiempiti() Then
                campi = Nothing
                Exit Sub
            End If

            Dim nome As String = campi.RecuperaValore("c.citta").Trim()
            campi = Nothing

            If String.IsNullOrWhiteSpace(nome) Then Exit Sub

            Try
                If _config.EsisteId("calendario_citta") Then
                    _config.EliminaValore("calendario_citta")
                End If
            Catch exElimina As Exception
                log.ScriviLog("MenuCalendario.FineCittaCalendario (elimina) - " & exElimina.Message & Environment.NewLine & exElimina.StackTrace)
            End Try

            _config.AggiungiValore("calendario_citta", nome)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Città del calendario impostata: " & nome)
            msg.ShowDialog()

            Dim dataSel As String = Date.Today.ToString("dd/MM/yyyy")
            Try
                Dim elementi As List(Of String) = calendario.ottieniElementi()
                If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(elementi(0)) Then
                    dataSel = elementi(0).Trim()
                End If
            Catch exSel As Exception
                log.ScriviLog("MenuCalendario.FineCittaCalendario (OttieniElementi) - " & exSel.Message & Environment.NewLine & exSel.StackTrace)
            End Try

            RicaricaEPosiziona(dataSel)
        Catch ex As Exception
            log.ScriviLog("MenuCalendario.FineCittaCalendario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

End Class