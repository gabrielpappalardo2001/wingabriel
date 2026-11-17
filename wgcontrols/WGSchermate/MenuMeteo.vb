Imports System
Imports System.Threading
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports wgcontrols

' Previsioni meteorologiche basate sulla localizzazione geografica. Offre bollettini attuali, giornalieri, orari, settimanali e la
' gestione dei comuni preferiti.
Friend Class MenuMeteo

    Private ReadOnly _almanacco As CLSAlmanacco
    Private ReadOnly _meteo As CLSMeteo
    Private ReadOnly _logger As New Logger()

    Private WithEvents _campi As Campi
    Private WithEvents _attesa As WGProcessoInCorso
    Private WithEvents _menuPrincipale As WGMenu
    Private _menuScelte As WGMenu
    Private _listaMeteo As WGMenuLista

    Private _threadCoord As CLSMeteo.CoordinateCitta
    Private _cittaValida As Boolean = False

    Private _meteoAttuale As CLSMeteo.InfoMeteo
    Private _meteoGiorno As CLSMeteo.InfoMeteoGiorno
    Private _meteoGiorni As List(Of CLSMeteo.InfoMeteoGiorno)
    Private _meteoOre As List(Of CLSMeteo.InfoMeteoOra)
    Private _puntoMarinoCache As CLSMeteo.PuntoMarino
    Private _puntoMarinoRisolto As Boolean = False
    Private _meteoMarinoCorrente As CLSMeteo.InfoMeteoMarino
    Private _meteoMarinoOre As List(Of CLSMeteo.InfoMeteoMarinoOra)
    Private _oraSelezionataCorrente As CLSMeteo.InfoMeteoOra
    Private _giornoSelezionatoCorrente As CLSMeteo.InfoMeteoGiorno
    Private _gestoreSelezioneCorrente As Action
    Private _vistaOrarioCorrente As Action

    Private _nomeCittaCorrente As String = ""
    Private _uiContext As SynchronizationContext
    Private _vistaCorrente As Action

    Private _tipoListaComuni As String = ""
    Private _comuneSelezionato As CLSMeteo.ComuneInfo
    Private _comuniCorrenti As List(Of CLSMeteo.ComuneInfo)

    Private Const OPZ_AGG_PREF As String = "Aggiungi ai preferiti"
    Private Const OPZ_RIM_PREF As String = "Rimuovi dai preferiti"

    Public Sub New()
        Try
            _almanacco = New CLSAlmanacco()
            _meteo = New CLSMeteo()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Meteo per città", AddressOf ChiediCitta)
            _menuPrincipale.aggiungi("Meteo posizione attuale", AddressOf MeteoPosizioneAttuale)
            _menuPrincipale.aggiungi("Comuni preferiti", AddressOf VisualizzaPreferiti)
            _menuPrincipale.aggiungi("Comuni recenti", AddressOf VisualizzaRecenti)
            _menuPrincipale.avvia("Menu - Meteo")
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Avvia - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediCitta()
        Try
            _campi = New Campi()
            _campi.Aggiungi("Nome della città", "c.citta", True)
            AddHandler _campi.Fine, AddressOf FineCampiCitta
            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ChiediCitta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCampiCitta()
        Try
            If _campi Is Nothing Then Exit Sub

            If Not _campi.TuttiObbligatoriRiempiti() Then
                _campi = Nothing
                Exit Sub
            End If

            Dim nomeCitta As String = _campi.RecuperaValore("c.citta").Trim()
            _campi = Nothing

            If String.IsNullOrWhiteSpace(nomeCitta) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il nome della città non può essere vuoto.")
                messaggio.ShowDialog()
                Exit Sub
            End If

            _nomeCittaCorrente = nomeCitta
            RisolviEScegli()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FineCampiCitta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MeteoPosizioneAttuale()
        Try
            _nomeCittaCorrente = ""
            RisolviEScegli()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MeteoPosizioneAttuale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RisolviEScegli()
        Try
            _attesa = New WGProcessoInCorso()
            _attesa.Avvia("Ricerca località in corso...", False)

            Dim t As New Thread(AddressOf ThreadRisolviCoord)
            t.IsBackground = True
            t.Start()
            AttendiThread(t)

            If _uiContext IsNot Nothing Then
                _uiContext.Post(Sub() DopoRisoluzione(), Nothing)
            Else
                DopoRisoluzione()
            End If
        Catch ex As Exception
            ChiudiAttesa()
            _logger.ScriviLog("MenuMeteo.RisolviEScegli - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ThreadRisolviCoord()
        Try
            _cittaValida = False

            If String.IsNullOrWhiteSpace(_nomeCittaCorrente) Then
                Try
                    Using wc As New System.Net.WebClient()
                        wc.Headers.Add("User-Agent", "WinGabriel/2.0 (accessibilita; postmaster@wingabriel.net)")
                        Dim json As String = wc.DownloadString("http://ip-api.com/json/")
                        If json.Contains("""city"":""") Then
                            Dim startIdx As Integer = json.IndexOf("""city"":""") + 8
                            Dim endIdx As Integer = json.IndexOf("""", startIdx)
                            Dim cittaRilevata As String = json.Substring(startIdx, endIdx - startIdx)
                            If Not String.IsNullOrWhiteSpace(cittaRilevata) Then
                                _nomeCittaCorrente = cittaRilevata
                            End If
                        End If
                    End Using
                Catch
                End Try
            End If

            Dim coordTask = _meteo.RisolviCoordinate(0, 0, _nomeCittaCorrente)
            coordTask.Wait()
            _threadCoord = coordTask.Result

            If Not String.IsNullOrWhiteSpace(_nomeCittaCorrente) AndAlso String.IsNullOrWhiteSpace(_threadCoord.NomeRisolto) Then
                _cittaValida = False
            ElseIf _threadCoord.Lat = 0 AndAlso _threadCoord.Lon = 0 Then
                _cittaValida = False
            Else
                _cittaValida = True
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ThreadRisolviCoord - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _cittaValida = False
        End Try
    End Sub

    Private Sub DopoRisoluzione()
        Try
            ChiudiAttesa()

            If Not _cittaValida Then
                Dim messaggio As New WGMessaggi()
                If Not String.IsNullOrWhiteSpace(_nomeCittaCorrente) Then
                    messaggio.Messaggio($"Località '{_nomeCittaCorrente}' non trovata. Verificare il nome e riprovare.")
                Else
                    messaggio.Messaggio("Posizione non disponibile. Provare a indicare una città.")
                End If
                messaggio.ShowDialog()
                Exit Sub
            End If

            _puntoMarinoCache = Nothing
            _puntoMarinoRisolto = False

            Try
                Dim comuneDaSalvare As New CLSMeteo.ComuneInfo With {
                    .IdComune = _threadCoord.IdComune,
                    .NomeComune = If(String.IsNullOrWhiteSpace(_threadCoord.NomeRisolto), _nomeCittaCorrente, _threadCoord.NomeRisolto),
                    .Lat = _threadCoord.Lat,
                    .Lon = _threadCoord.Lon,
                    .Paese = _threadCoord.Paese,
                    .Regione = _threadCoord.Regione,
                    .UltimoUso = DateTime.UtcNow
                }
                If Not String.IsNullOrWhiteSpace(comuneDaSalvare.IdComune) OrElse Not String.IsNullOrWhiteSpace(comuneDaSalvare.NomeComune) Then
                    _meteo.AggiungiComuneRecente(comuneDaSalvare)
                End If
            Catch exSalvataggio As Exception
                _logger.ScriviLog("MenuMeteo.DopoRisoluzione (recente) - " & exSalvataggio.Message & Environment.NewLine & exSalvataggio.StackTrace)
            End Try

            MostraMenuScelte()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DopoRisoluzione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuScelte()
        Try
            _menuScelte = New WGMenu()
            _menuScelte.aggiungi("Meteo attuale", AddressOf MostraAttuale)
            _menuScelte.aggiungi("Meteo generale di oggi", AddressOf MostraGiornaliero)
            _menuScelte.aggiungi("Previsioni prossimi 7 giorni", AddressOf MostraSettimana)
            _menuScelte.aggiungi("Meteo per fasce orarie di oggi", AddressOf MostraOrario)
            _menuScelte.aggiungi("Informazioni sul mare più vicino", AddressOf MostraMarinoVicino)
            _menuScelte.avvia("Meteo - " & DescrizioneLocalita())
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraMenuScelte - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraAttuale()
        EseguiConAttesa(AddressOf FetchAttuale, AddressOf ListaAttuale)
    End Sub

    Private Sub MostraGiornaliero()
        EseguiConAttesa(AddressOf FetchGiornaliero, AddressOf ListaGiornaliero)
    End Sub

    Private Sub MostraSettimana()
        EseguiConAttesa(AddressOf FetchSettimana, AddressOf ListaSettimana)
    End Sub

    Private Sub MostraOrario()
        Try
            _vistaOrarioCorrente = AddressOf MostraOrario
            EseguiConAttesa(AddressOf FetchOrario, AddressOf ListaOrario)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraOrario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraOrarioGiorno()
        Try
            If _giornoSelezionatoCorrente Is Nothing Then Exit Sub
            _vistaOrarioCorrente = AddressOf MostraOrarioGiorno
            EseguiConAttesa(AddressOf FetchOrarioGiorno, AddressOf ListaOrarioGiorno)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraOrarioGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMarinoVicino()
        EseguiConAttesa(AddressOf FetchMarinoVicino, AddressOf ListaMarinoVicino)
    End Sub

    Private Sub AssicuraPuntoMarino()
        Try
            If _puntoMarinoRisolto Then Return
            _puntoMarinoCache = _meteo.TrovaPuntoMarinoVicino(_threadCoord.Lat, _threadCoord.Lon).Result
            _puntoMarinoRisolto = True
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AssicuraPuntoMarino - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _puntoMarinoCache = New CLSMeteo.PuntoMarino With {.Valido = False}
            _puntoMarinoRisolto = True
        End Try
    End Sub

    Private Sub FetchMarinoVicino()
        Try
            AssicuraPuntoMarino()
            _meteoMarinoCorrente = _meteo.OttieniMeteoMarinoCorrente(_puntoMarinoCache).Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchMarinoVicino - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoMarinoCorrente = Nothing
        End Try
    End Sub

    Private Sub ListaMarinoVicino()
        Try
            If _meteoMarinoCorrente Is Nothing OrElse Not _meteoMarinoCorrente.Valido Then
                MostraErroreDati(If(_meteoMarinoCorrente IsNot Nothing, _meteoMarinoCorrente.Messaggio, ""))
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            Dim idx As Integer = 0

            Dim distanza As String = ""
            If _meteoMarinoCorrente.DistanzaKm > 0.5 Then
                distanza = _meteoMarinoCorrente.DistanzaKm.ToString("0.#", CultureInfo.InvariantCulture) & " km"
                If Not String.IsNullOrWhiteSpace(_meteoMarinoCorrente.Direzione) Then
                    distanza &= " a " & _meteoMarinoCorrente.Direzione
                End If
            Else
                distanza = "questa città si affaccia direttamente sul mare"
            End If

            AggiungiVoce(elementi, idx, "Distanza dal mare", distanza)
            AggiungiVoce(elementi, idx, "Temperatura del mare", _meteoMarinoCorrente.TemperaturaMare)
            AggiungiVoce(elementi, idx, "Altezza onde", _meteoMarinoCorrente.AltezzaOnde)
            AggiungiVoce(elementi, idx, "Direzione onde", _meteoMarinoCorrente.DirezioneOnde)
            AggiungiVoce(elementi, idx, "Periodo onde", _meteoMarinoCorrente.PeriodoOnde)
            AggiungiVoce(elementi, idx, "Altezza mareggiata", _meteoMarinoCorrente.AltezzaMareggiata)
            AggiungiVoce(elementi, idx, "Aggiornamento", _meteoMarinoCorrente.Aggiornamento)

            MostraLista("Mare più vicino - " & DescrizioneLocalita(), elementi, AddressOf MostraMarinoVicino)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaMarinoVicino - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FetchAttuale()
        Try
            _meteoAttuale = _meteo.OttieniMeteo(_threadCoord.Lat, _threadCoord.Lon, "").Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchAttuale - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoAttuale = Nothing
        End Try
    End Sub

    Private Sub FetchGiornaliero()
        Try
            _meteoGiorno = _meteo.OttieniMeteoGiornaliero(_threadCoord.Lat, _threadCoord.Lon, "").Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchGiornaliero - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoGiorno = Nothing
        End Try
    End Sub

    Private Sub FetchSettimana()
        Try
            _meteoGiorni = _meteo.OttieniPrevisione(_threadCoord.Lat, _threadCoord.Lon, "", 7).Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchSettimana - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoGiorni = Nothing
        End Try
    End Sub

    Private Sub FetchOrario()
        Try
            _meteoOre = _meteo.OttieniMeteoOrario(_threadCoord.Lat, _threadCoord.Lon, "").Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchOrario - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoOre = Nothing
        End Try
    End Sub

    Private Sub FetchOrarioGiorno()
        Try
            If _giornoSelezionatoCorrente Is Nothing Then
                _meteoOre = Nothing
                Exit Sub
            End If
            _meteoOre = _meteo.OttieniMeteoOrarioData(_threadCoord.Lat, _threadCoord.Lon, _giornoSelezionatoCorrente.DataIso, "").Result
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchOrarioGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoOre = Nothing
        End Try
    End Sub

    Private Sub ListaAttuale()
        Try
            If _meteoAttuale Is Nothing OrElse Not _meteoAttuale.Valido Then
                MostraErroreDati(If(_meteoAttuale IsNot Nothing, _meteoAttuale.Messaggio, ""))
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            Dim idx As Integer = 0
            Dim vento As String = _meteoAttuale.Vento
            If Not String.IsNullOrWhiteSpace(vento) AndAlso Not String.IsNullOrWhiteSpace(_meteoAttuale.DirezioneVento) Then
                vento &= " da " & _meteoAttuale.DirezioneVento
            End If
            AggiungiVoce(elementi, idx, "Condizioni", _meteoAttuale.Condizioni)
            AggiungiVoce(elementi, idx, "Temperatura", _meteoAttuale.Temperatura)
            AggiungiVoce(elementi, idx, "Umidità", _meteoAttuale.Umidita)
            AggiungiVoce(elementi, idx, "Nuvolosità", _meteoAttuale.Nuvolosita)
            AggiungiVoce(elementi, idx, "Pressione", _meteoAttuale.Pressione)
            AggiungiVoce(elementi, idx, "Vento", vento)
            AggiungiVoce(elementi, idx, "Precipitazioni", _meteoAttuale.Precipitazioni)
            AggiungiVoce(elementi, idx, "Aggiornamento", _meteoAttuale.Aggiornamento)

            MostraLista("Meteo attuale - " & DescrizioneLocalita(), elementi, AddressOf MostraAttuale)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaAttuale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ListaGiornaliero()
        Try
            If _meteoGiorno Is Nothing Then
                MostraErroreDati("")
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            Dim idx As Integer = 0
            Dim vento As String = _meteoGiorno.Vento
            If Not String.IsNullOrWhiteSpace(vento) AndAlso Not String.IsNullOrWhiteSpace(_meteoGiorno.DirezioneVento) Then
                vento &= " da " & _meteoGiorno.DirezioneVento
            End If
            AggiungiVoce(elementi, idx, "Giorno", _meteoGiorno.DataCompleta)
            AggiungiVoce(elementi, idx, "Condizioni", _meteoGiorno.Condizioni)
            AggiungiVoce(elementi, idx, "Temperatura minima", _meteoGiorno.TempMin)
            AggiungiVoce(elementi, idx, "Temperatura massima", _meteoGiorno.TempMax)
            AggiungiVoce(elementi, idx, "Probabilità di pioggia", _meteoGiorno.ProbabilitaPioggia)
            AggiungiVoce(elementi, idx, "Precipitazioni", _meteoGiorno.Precipitazioni)
            AggiungiVoce(elementi, idx, "Vento massimo", vento)

            MostraLista("Meteo di oggi - " & DescrizioneLocalita(), elementi, AddressOf MostraGiornaliero)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaGiornaliero - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ListaSettimana()
        Try
            If _meteoGiorni Is Nothing OrElse _meteoGiorni.Count = 0 Then
                MostraErroreDati("")
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            Dim idx As Integer = 0
            For Each g As CLSMeteo.InfoMeteoGiorno In _meteoGiorni
                Dim riga As New System.Text.StringBuilder()
                riga.Append(g.Giorno)
                riga.Append(": ")
                riga.Append(g.Condizioni)
                If Not String.IsNullOrWhiteSpace(g.TempMin) AndAlso Not String.IsNullOrWhiteSpace(g.TempMax) Then
                    riga.Append(", min ")
                    riga.Append(g.TempMin)
                    riga.Append(" max ")
                    riga.Append(g.TempMax)
                End If
                If Not String.IsNullOrWhiteSpace(g.ProbabilitaPioggia) Then
                    riga.Append(", pioggia ")
                    riga.Append(g.ProbabilitaPioggia)
                End If
                Dim idVoce As String = If(String.IsNullOrWhiteSpace(g.DataIso), idx.ToString(), g.DataIso)
                elementi.Add(New ElementoLista(riga.ToString(), idVoce))
                idx += 1
            Next

            MostraLista("Previsioni 7 giorni - " & DescrizioneLocalita(), elementi, AddressOf MostraSettimana, AddressOf GiornoSelezionato)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaSettimana - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ListaOrario()
        Try
            If _meteoOre Is Nothing OrElse _meteoOre.Count = 0 Then
                MostraErroreDati("")
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            For Each o As CLSMeteo.InfoMeteoOra In _meteoOre
                Dim riga As New System.Text.StringBuilder()
                riga.Append(o.Ora)
                riga.Append(": ")
                riga.Append(o.Condizioni)
                If Not String.IsNullOrWhiteSpace(o.Temperatura) Then
                    riga.Append(", ")
                    riga.Append(o.Temperatura)
                End If
                If Not String.IsNullOrWhiteSpace(o.ProbabilitaPioggia) Then
                    riga.Append(", pioggia ")
                    riga.Append(o.ProbabilitaPioggia)
                End If
                elementi.Add(New ElementoLista(riga.ToString(), o.Id))
            Next

            MostraLista("Fasce orarie di oggi - " & DescrizioneLocalita(), elementi, AddressOf MostraOrario, AddressOf OraSelezionata)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaOrario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ListaOrarioGiorno()
        Try
            If _meteoOre Is Nothing OrElse _meteoOre.Count = 0 Then
                MostraErroreDati("")
                Exit Sub
            End If

            Dim elementi As New List(Of ElementoLista)()
            For Each o As CLSMeteo.InfoMeteoOra In _meteoOre
                Dim riga As New System.Text.StringBuilder()
                riga.Append(o.Ora)
                riga.Append(": ")
                riga.Append(o.Condizioni)
                If Not String.IsNullOrWhiteSpace(o.Temperatura) Then
                    riga.Append(", ")
                    riga.Append(o.Temperatura)
                End If
                If Not String.IsNullOrWhiteSpace(o.ProbabilitaPioggia) Then
                    riga.Append(", pioggia ")
                    riga.Append(o.ProbabilitaPioggia)
                End If
                elementi.Add(New ElementoLista(riga.ToString(), o.Id))
            Next

            Dim titoloGiorno As String = "Fasce orarie"
            If _giornoSelezionatoCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_giornoSelezionatoCorrente.DataCompleta) Then
                titoloGiorno = "Fasce orarie di " & _giornoSelezionatoCorrente.DataCompleta
            End If

            MostraLista(titoloGiorno & " - " & DescrizioneLocalita(), elementi, AddressOf MostraOrarioGiorno, AddressOf OraSelezionata)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaOrarioGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub GiornoSelezionato()
        Try
            Dim id As String = ""
            If _listaMeteo IsNot Nothing Then
                id = _listaMeteo.ottieniElemento()
            End If
            If String.IsNullOrWhiteSpace(id) Then Exit Sub
            If _meteoGiorni Is Nothing Then Exit Sub

            _giornoSelezionatoCorrente = Nothing
            For Each g As CLSMeteo.InfoMeteoGiorno In _meteoGiorni
                If g.DataIso = id Then
                    _giornoSelezionatoCorrente = g
                    Exit For
                End If
            Next
            If _giornoSelezionatoCorrente Is Nothing Then Exit Sub

            AbbandonaLista()
            MostraOrarioGiorno()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.GiornoSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OraSelezionata()
        Try
            Dim id As String = ""
            If _listaMeteo IsNot Nothing Then
                id = _listaMeteo.ottieniElemento()
            End If
            If String.IsNullOrWhiteSpace(id) Then Exit Sub
            If _meteoOre Is Nothing Then Exit Sub

            _oraSelezionataCorrente = Nothing
            For Each o As CLSMeteo.InfoMeteoOra In _meteoOre
                If o.Id = id Then
                    _oraSelezionataCorrente = o
                    Exit For
                End If
            Next
            If _oraSelezionataCorrente Is Nothing Then Exit Sub

            EseguiConAttesa(AddressOf FetchDettaglioOra, AddressOf ListaDettaglioOra)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.OraSelezionata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FetchDettaglioOra()
        Try
            AssicuraPuntoMarino()
            If _puntoMarinoCache IsNot Nothing AndAlso _puntoMarinoCache.Valido Then
                _meteoMarinoOre = _meteo.OttieniMeteoMarinoOrario(_puntoMarinoCache).Result
            Else
                _meteoMarinoOre = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FetchDettaglioOra - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _meteoMarinoOre = Nothing
        End Try
    End Sub

    Private Sub ListaDettaglioOra()
        Try
            If _oraSelezionataCorrente Is Nothing Then Exit Sub
            Dim o As CLSMeteo.InfoMeteoOra = _oraSelezionataCorrente
            Dim elementi As New List(Of ElementoLista)()
            Dim idx As Integer = 0

            Dim vento As String = o.Vento
            If Not String.IsNullOrWhiteSpace(vento) AndAlso Not String.IsNullOrWhiteSpace(o.DirezioneVento) Then
                vento &= " da " & o.DirezioneVento
            End If

            AggiungiVoce(elementi, idx, "Condizioni", o.Condizioni)
            AggiungiVoce(elementi, idx, "Temperatura", o.Temperatura)
            AggiungiVoce(elementi, idx, "Percepita", o.TemperaturaPercepita)
            AggiungiVoce(elementi, idx, "Umidità", o.Umidita)
            AggiungiVoce(elementi, idx, "Punto di rugiada", o.PuntoDiRugiada)
            AggiungiVoce(elementi, idx, "Probabilità di pioggia", o.ProbabilitaPioggia)
            AggiungiVoce(elementi, idx, "Precipitazioni previste", o.Precipitazioni)
            AggiungiVoce(elementi, idx, "Vento", vento)
            AggiungiVoce(elementi, idx, "Raffiche", o.RaffficheVento)
            AggiungiVoce(elementi, idx, "Pressione", o.Pressione)
            AggiungiVoce(elementi, idx, "Nuvolosità", o.Nuvolosita)
            AggiungiVoce(elementi, idx, "Visibilità", o.Visibilita)
            AggiungiVoce(elementi, idx, "Indice UV", o.IndiceUV)

            If _puntoMarinoCache IsNot Nothing AndAlso _puntoMarinoCache.Valido Then
                Dim marinoOra As CLSMeteo.InfoMeteoMarinoOra = Nothing
                If _meteoMarinoOre IsNot Nothing Then
                    For Each m As CLSMeteo.InfoMeteoMarinoOra In _meteoMarinoOre
                        If m.Id = o.Id Then
                            marinoOra = m
                            Exit For
                        End If
                    Next
                End If

                If marinoOra IsNot Nothing Then
                    Dim distanzaMare As String = ""
                    If _puntoMarinoCache.DistanzaKm > 0.5 Then
                        distanzaMare = _puntoMarinoCache.DistanzaKm.ToString("0.#", CultureInfo.InvariantCulture) & " km"
                        If Not String.IsNullOrWhiteSpace(_puntoMarinoCache.Direzione) Then
                            distanzaMare &= " a " & _puntoMarinoCache.Direzione
                        End If
                    End If
                    If Not String.IsNullOrWhiteSpace(distanzaMare) Then
                        AggiungiVoce(elementi, idx, "Distanza dal mare", distanzaMare)
                    End If
                    AggiungiVoce(elementi, idx, "Temperatura del mare", marinoOra.TemperaturaMare)
                    AggiungiVoce(elementi, idx, "Altezza onde", marinoOra.AltezzaOnde)
                    AggiungiVoce(elementi, idx, "Direzione onde", marinoOra.DirezioneOnde)
                    AggiungiVoce(elementi, idx, "Periodo onde", marinoOra.PeriodoOnde)
                End If
            End If

            Dim vistaRitorno As Action = If(_vistaOrarioCorrente, AddressOf MostraOrario)
            MostraLista("Dettaglio ore " & o.Ora & " - " & DescrizioneLocalita(), elementi, vistaRitorno)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ListaDettaglioOra - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraLista(titolo As String, elementi As List(Of ElementoLista), callbackAggiorna As Action, Optional gestoreSelezione As Action = Nothing)
        Try
            AbbandonaLista()
            _vistaCorrente = callbackAggiorna
            _gestoreSelezioneCorrente = gestoreSelezione

            If elementi Is Nothing OrElse elementi.Count = 0 Then
                MostraErroreDati("Dati meteo non disponibili al momento.")
                Exit Sub
            End If

            _listaMeteo = New WGMenuLista()
            _listaMeteo.invioNonConsentito = (gestoreSelezione Is Nothing)
            _listaMeteo.multiselezioneConsentita = False
            _listaMeteo.BeginUpdate()
            _listaMeteo.CaricaListaDinamica(elementi)
            _listaMeteo.aggiungiOpzione("Aggiorna", AddressOf AggiornaListaMeteo)
            _listaMeteo.aggiungiOpzione("Cambia città", AddressOf CambiaCitta)
            _listaMeteo.aggiungiOpzione("Aggiungi ai preferiti", AddressOf AggiungiComunePreferitoCorrente)
            _listaMeteo.EndUpdate()
            _listaMeteo.impostaTitolo(titolo)
            AddHandler _listaMeteo.escPremuto, AddressOf ChiudiListaMeteo
            If gestoreSelezione IsNot Nothing Then
                AddHandler _listaMeteo.ElementoSelezionato, AddressOf ElementoListaSelezionato
            End If
            _listaMeteo.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraLista - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElementoListaSelezionato()
        Try
            If _gestoreSelezioneCorrente IsNot Nothing Then
                _gestoreSelezioneCorrente()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ElementoListaSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiornaListaMeteo()
        Try
            If _vistaCorrente IsNot Nothing Then
                AbbandonaLista()
                _vistaCorrente()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AggiornaListaMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub


    Private Sub AggiungiComunePreferitoCorrente()
        Try
            Dim coord As CLSMeteo.CoordinateCitta = _threadCoord

            If String.IsNullOrWhiteSpace(_nomeCittaCorrente) AndAlso
               String.IsNullOrWhiteSpace(coord.NomeRisolto) Then
                Exit Sub
            End If

            Dim nomeComune As String = _nomeCittaCorrente.Trim()
            Dim idComune As String = coord.IdComune

            If Not String.IsNullOrWhiteSpace(coord.NomeRisolto) Then
                nomeComune = coord.NomeRisolto.Trim()
            End If

            If String.IsNullOrWhiteSpace(nomeComune) Then
                Exit Sub
            End If

            If String.IsNullOrWhiteSpace(idComune) Then
                idComune = _meteo.CreaComuneInfo(coord, nomeComune).IdComune
            End If

            Dim comune As New CLSMeteo.ComuneInfo With {
                .IdComune = idComune,
                .NomeComune = nomeComune,
                .Lat = coord.Lat,
                .Lon = coord.Lon,
                .Paese = coord.Paese,
                .Regione = coord.Regione,
                .UltimoUso = DateTime.UtcNow
            }

            If _meteo.AggiungiComunePreferito(comune) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Comune aggiunto ai preferiti.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AggiungiComunePreferitoCorrente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub
    Private Sub VisualizzaPreferiti()
        Try
            MostraListaComuni("Comuni preferiti", _meteo.OttieniComuniPreferiti(), "preferiti")
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.VisualizzaPreferiti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VisualizzaRecenti()
        Try
            MostraListaComuni("Comuni recenti", _meteo.OttieniComuniRecenti(), "recenti")
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.VisualizzaRecenti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraListaComuni(titolo As String, comuni As List(Of CLSMeteo.ComuneInfo), tipo As String)
        Try
            AbbandonaLista()
            _tipoListaComuni = tipo
            _comuneSelezionato = Nothing

            If comuni Is Nothing OrElse comuni.Count = 0 Then
                Dim messaggio As New WGMessaggi()

                If tipo = "recenti" Then
                    messaggio.Messaggio("Non sono disponibili comuni recenti")
                    messaggio.ShowDialog()
                ElseIf tipo = "preferiti" Then
                    messaggio.Messaggio("Non è stato preparato il tuo elenco di comuni preferiti")
                    messaggio.ShowDialog()
                End If

                Exit Sub
            End If

            _comuniCorrenti = comuni

            _listaMeteo = New WGMenuLista()
            _listaMeteo.invioNonConsentito = False
            _listaMeteo.multiselezioneConsentita = False
            _listaMeteo.BeginUpdate()

            Dim primoComune As CLSMeteo.ComuneInfo = Nothing
            For Each comune As CLSMeteo.ComuneInfo In comuni
                If comune Is Nothing Then Continue For
                If primoComune Is Nothing AndAlso comune.IdComune <> "0" Then
                    primoComune = comune
                End If

                Dim nomeVoce As String = comune.NomeComune
                If Not String.IsNullOrWhiteSpace(comune.Regione) Then
                    nomeVoce &= " - " & comune.Regione
                End If
                If Not String.IsNullOrWhiteSpace(comune.Paese) Then
                    nomeVoce &= " (" & comune.Paese & ")"
                End If
                _listaMeteo.aggiungiElemento(nomeVoce, comune.IdComune)
            Next

            If primoComune IsNot Nothing Then
                _comuneSelezionato = primoComune
                If tipo = "preferiti" Then
                    _listaMeteo.aggiungiOpzione("Modifica nome", AddressOf ModificaComunePreferito)
                    _listaMeteo.aggiungiOpzione("Elimina dai preferiti", AddressOf EliminaComunePreferito)
                ElseIf tipo = "recenti" Then
                    Dim etichettaPref As String = If(EComunePreferito(primoComune.IdComune), OPZ_RIM_PREF, OPZ_AGG_PREF)
                    _listaMeteo.aggiungiOpzione(etichettaPref, AddressOf CommutaPreferitoRecente)
                    _listaMeteo.aggiungiOpzione("Elimina dai recenti", AddressOf EliminaComuneRecente)
                End If
            End If

            _listaMeteo.EndUpdate()
            _listaMeteo.impostaTitolo(titolo)

            AddHandler _listaMeteo.ElementoSelezionato, AddressOf ComuneSelezionato
            AddHandler _listaMeteo.VoceCambiata, AddressOf ComuneVoceCambiata
            AddHandler _listaMeteo.escPremuto, AddressOf ChiudiListaMeteo
            _listaMeteo.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraListaComuni - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ComuneSelezionato()
        Try
            Dim id As String = ""
            If _listaMeteo IsNot Nothing Then
                id = _listaMeteo.ottieniElemento()
            End If

            If String.IsNullOrWhiteSpace(id) OrElse id = "0" Then Exit Sub

            AbbandonaLista()

            Dim comune As CLSMeteo.ComuneInfo = RisolviComuneCorrente(id)
            If comune Is Nothing Then Exit Sub

            _comuneSelezionato = comune
            ConsultaComuneSelezionato()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ComuneSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ComuneVoceCambiata(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Exit Sub
            Dim id As String = e.IdVoce
            If String.IsNullOrWhiteSpace(id) OrElse id = "0" Then Exit Sub

            Dim comune As CLSMeteo.ComuneInfo = RisolviComuneCorrente(id)
            If comune IsNot Nothing Then
                _comuneSelezionato = comune
            End If

            If _tipoListaComuni = "recenti" Then
                AggiornaOpzionePreferiti(id)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ComuneVoceCambiata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function RisolviComuneCorrente(id As String) As CLSMeteo.ComuneInfo
        Try
            If String.IsNullOrWhiteSpace(id) Then Return Nothing

            If _comuniCorrenti IsNot Nothing Then
                For Each c As CLSMeteo.ComuneInfo In _comuniCorrenti
                    If c IsNot Nothing AndAlso c.IdComune = id Then
                        Return c
                    End If
                Next
            End If

            If _tipoListaComuni = "preferiti" Then
                Return _meteo.OttieniComunePreferito(id)
            Else
                Return _meteo.OttieniComuneRecente(id)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.RisolviComuneCorrente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return Nothing
    End Function

    Private Function EComunePreferito(id As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) OrElse id = "0" Then Return False
            Return _meteo.OttieniComunePreferito(id) IsNot Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.EComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub AggiornaOpzionePreferiti(id As String)
        Try
            If _listaMeteo Is Nothing Then Exit Sub

            Dim ePref As Boolean = EComunePreferito(id)
            Dim desiderato As String = If(ePref, OPZ_RIM_PREF, OPZ_AGG_PREF)
            Dim altro As String = If(ePref, OPZ_AGG_PREF, OPZ_RIM_PREF)

            If _listaMeteo.opzioneEsiste(altro) Then
                _listaMeteo.sostituisciOpzione(altro, desiderato, AddressOf CommutaPreferitoRecente)
            ElseIf Not _listaMeteo.opzioneEsiste(desiderato) Then
                _listaMeteo.aggiungiOpzione(desiderato, AddressOf CommutaPreferitoRecente)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AggiornaOpzionePreferiti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CommutaPreferitoRecente()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub

            Dim id As String = _comuneSelezionato.IdComune

            If EComunePreferito(id) Then
                If _meteo.EliminaComunePreferito(id) Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Comune rimosso dai preferiti.")
                    messaggio.ShowDialog()
                End If
            Else
                Dim comune As New CLSMeteo.ComuneInfo With {
                    .IdComune = _comuneSelezionato.IdComune,
                    .NomeComune = _comuneSelezionato.NomeComune,
                    .Lat = _comuneSelezionato.Lat,
                    .Lon = _comuneSelezionato.Lon,
                    .Paese = _comuneSelezionato.Paese,
                    .Regione = _comuneSelezionato.Regione,
                    .UltimoUso = DateTime.UtcNow
                }
                If _meteo.AggiungiComunePreferito(comune) Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Comune aggiunto ai preferiti.")
                    messaggio.ShowDialog()
                End If
            End If

            AggiornaOpzionePreferiti(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.CommutaPreferitoRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConsultaComuneSelezionato()
        Try
            ImpostaComuneCorrenteSelezionato()
            RegistraRecenteCorrente()
            MostraMenuScelte()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ConsultaComuneSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RegistraRecenteCorrente()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub

            Dim comune As New CLSMeteo.ComuneInfo With {
                .IdComune = _comuneSelezionato.IdComune,
                .NomeComune = _comuneSelezionato.NomeComune,
                .Lat = _comuneSelezionato.Lat,
                .Lon = _comuneSelezionato.Lon,
                .Paese = _comuneSelezionato.Paese,
                .Regione = _comuneSelezionato.Regione,
                .UltimoUso = DateTime.UtcNow
            }

            If Not String.IsNullOrWhiteSpace(comune.IdComune) OrElse Not String.IsNullOrWhiteSpace(comune.NomeComune) Then
                _meteo.AggiungiComuneRecente(comune)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.RegistraRecenteCorrente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaComuneCorrenteSelezionato()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub

            _nomeCittaCorrente = _comuneSelezionato.NomeComune
            _threadCoord = New CLSMeteo.CoordinateCitta With {
                .IdComune = _comuneSelezionato.IdComune,
                .Lat = _comuneSelezionato.Lat,
                .Lon = _comuneSelezionato.Lon,
                .NomeRisolto = _comuneSelezionato.NomeComune,
                .Paese = _comuneSelezionato.Paese,
                .Regione = _comuneSelezionato.Regione
            }
            _cittaValida = True
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ImpostaComuneCorrenteSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModificaComunePreferito()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub

            _campi = New Campi()
            _campi.Aggiungi("Nuovo nome del comune", "c.nome", True, , _comuneSelezionato.NomeComune)
            AddHandler _campi.Fine, AddressOf FineModificaComunePreferito
            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ModificaComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineModificaComunePreferito()
        Try
            If _campi Is Nothing Then Exit Sub
            If Not _campi.TuttiObbligatoriRiempiti() Then
                _campi = Nothing
                Exit Sub
            End If

            Dim nuovoNome As String = _campi.RecuperaValore("c.nome").Trim()
            _campi = Nothing

            If String.IsNullOrWhiteSpace(nuovoNome) Then Exit Sub

            If _meteo.ModificaComunePreferito(_comuneSelezionato.IdComune, nuovoNome) Then
                _comuneSelezionato.NomeComune = nuovoNome
                VisualizzaPreferiti()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.FineModificaComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaComunePreferito()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub
            If _meteo.EliminaComunePreferito(_comuneSelezionato.IdComune) Then
                VisualizzaPreferiti()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.EliminaComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaComuneRecente()
        Try
            If _comuneSelezionato Is Nothing Then Exit Sub
            If _meteo.EliminaComuneRecente(_comuneSelezionato.IdComune) Then
                VisualizzaRecenti()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.EliminaComuneRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraErroreDati(messaggio As String)
        Try
            Dim msg As New WGMessaggi()
            If Not String.IsNullOrWhiteSpace(messaggio) Then
                msg.Messaggio(messaggio)
            Else
                msg.Messaggio("Dati meteo non disponibili al momento.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MostraErroreDati - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiungiVoce(elementi As List(Of ElementoLista), ByRef indice As Integer, etichetta As String, valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Exit Sub
            elementi.Add(New ElementoLista(etichetta & ": " & valore.Trim(), indice.ToString()))
            indice += 1
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AggiungiVoce - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function DescrizioneLocalita() As String
        Try
            Dim nome As String = _threadCoord.NomeRisolto
            If String.IsNullOrWhiteSpace(nome) Then nome = _nomeCittaCorrente
            If String.IsNullOrWhiteSpace(nome) Then nome = "posizione attuale"
            If Not String.IsNullOrWhiteSpace(_threadCoord.Paese) Then
                nome &= " (" & _threadCoord.Paese & ")"
            End If
            Return nome
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescrizioneLocalita - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "località"
        End Try
    End Function

    
    Private Sub CambiaCitta()
        Try
            AbbandonaLista()
            ChiediCitta()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.CambiaCitta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaMeteo()
        Try
            AbbandonaLista()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ChiudiListaMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AbbandonaLista()
        Try
            If _listaMeteo IsNot Nothing Then
                _listaMeteo.abbandona()
                _listaMeteo = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AbbandonaLista - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiConAttesa(fetch As Action, mostra As Action)
        Try
            _attesa = New WGProcessoInCorso()
            _attesa.Avvia("Recupero dati meteo in corso...", False)

            Dim t As New Thread(Sub()
                                    Try
                                        fetch()
                                    Catch ex As Exception
                                        _logger.ScriviLog("MenuMeteo.EseguiConAttesa (thread) - " & ex.Message & Environment.NewLine & ex.StackTrace)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
            AttendiThread(t)

            If _uiContext IsNot Nothing Then
                _uiContext.Post(Sub() mostra(), Nothing)
            Else
                mostra()
            End If
        Catch ex As Exception
            ChiudiAttesa()
            _logger.ScriviLog("MenuMeteo.EseguiConAttesa - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttendiThread(t As Thread)
        While t.IsAlive
            Application.DoEvents()
            Thread.Sleep(10)
        End While
        ChiudiAttesa()
    End Sub

    Private Sub ChiudiAttesa()
        Try
            If _attesa IsNot Nothing Then
                _attesa.Fine()
                _attesa = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ChiudiAttesa - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

#Region "Comandi vocali"

    Private Class RichiestaMeteoVocale
        Public Property Citta As String = ""
        Public Property GiorniDaOggi As Integer = 0
        Public Property Risultato As String = ""
    End Class

    Private Class FasciaMeteoVocale
        Public Property Nome As String = ""
        Public Property OraInizio As Integer = 0
        Public Property OraFine As Integer = 0
        Public Property Condizioni As New List(Of String)()
        Public Property TemperaturaMin As Double = Double.NaN
        Public Property TemperaturaMax As Double = Double.NaN
        Public Property PioggiaMax As Integer = -1
    End Class

    Private Shared ReadOnly _culturaItaliana As CultureInfo = CultureInfo.GetCultureInfo("it-IT")

    Public Sub ComandoMeteoOggi(Optional citta As String = "")
        Try
            AvviaComandoMeteo(citta, 0)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ComandoMeteoOggi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub ComandoMeteoGiorno(citta As String, giorniDaOggi As Integer)
        Try
            AvviaComandoMeteo(citta, giorniDaOggi)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.ComandoMeteoGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaComandoMeteo(citta As String, giorniDaOggi As Integer)
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            ChiudiAttesa()
            _attesa = New WGProcessoInCorso()
            _attesa.Avvia("Recupero dati meteo in corso...", False)

            Dim richiesta As New RichiestaMeteoVocale()
            richiesta.Citta = If(citta, "").Trim()
            richiesta.GiorniDaOggi = giorniDaOggi

            Dim lavoro As New Thread(AddressOf LavoroComandoMeteo)
            lavoro.IsBackground = True
            lavoro.Start(richiesta)
        Catch ex As Exception
            ChiudiAttesa()
            _logger.ScriviLog("MenuMeteo.AvviaComandoMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
            MostraErroreDati("Non è stato possibile recuperare il meteo.")
        End Try
    End Sub

    Private Sub LavoroComandoMeteo(stato As Object)
        Dim richiesta As RichiestaMeteoVocale = Nothing
        Try
            richiesta = TryCast(stato, RichiestaMeteoVocale)
            If richiesta IsNot Nothing Then
                If richiesta.GiorniDaOggi <= 0 Then
                    richiesta.Risultato = TestoMeteoOggi(richiesta.Citta)
                Else
                    richiesta.Risultato = TestoMeteoGiorno(richiesta.Citta, richiesta.GiorniDaOggi)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.LavoroComandoMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            PubblicaSuInterfaccia(AddressOf CompletaComandoMeteo, richiesta)
        End Try
    End Sub

    Private Sub PubblicaSuInterfaccia(callback As SendOrPostCallback, stato As Object)
        Try
            If _uiContext IsNot Nothing Then
                _uiContext.Post(callback, stato)
                Return
            End If

            If System.Windows.Forms.Application.OpenForms.Count > 0 Then
                Dim finestra As System.Windows.Forms.Form = System.Windows.Forms.Application.OpenForms(0)
                If finestra IsNot Nothing AndAlso finestra.IsHandleCreated AndAlso Not finestra.IsDisposed Then
                    finestra.BeginInvoke(callback, New Object() {stato})
                    Return
                End If
            End If

            callback(stato)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.PubblicaSuInterfaccia - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletaComandoMeteo(stato As Object)
        Try
            ChiudiAttesa()

            Dim testo As String = ""
            Dim richiesta As RichiestaMeteoVocale = TryCast(stato, RichiestaMeteoVocale)
            If richiesta IsNot Nothing Then testo = richiesta.Risultato

            If String.IsNullOrWhiteSpace(testo) Then
                MostraErroreDati("Dati meteo non disponibili al momento.")
                Exit Sub
            End If

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.CompletaComandoMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function TestoMeteoOggi(Optional citta As String = "") As String
        Try
            Dim coord As CLSMeteo.CoordinateCitta = Nothing
            Dim nomeLocalita As String = ""
            If Not RisolviLocalitaComando(citta, coord, nomeLocalita) Then
                Return MessaggioLocalitaNonTrovata(citta)
            End If

            Dim attuale As CLSMeteo.InfoMeteo = Nothing
            Dim giorno As CLSMeteo.InfoMeteoGiorno = Nothing
            Dim ore As List(Of CLSMeteo.InfoMeteoOra) = Nothing

            Try
                attuale = _meteo.OttieniMeteo(coord.Lat, coord.Lon, "").Result
            Catch exAttuale As Exception
                _logger.ScriviLog("MenuMeteo.TestoMeteoOggi (attuale) - " & exAttuale.Message & Environment.NewLine & exAttuale.StackTrace)
            End Try

            Try
                giorno = _meteo.OttieniMeteoGiornaliero(coord.Lat, coord.Lon, "").Result
            Catch exGiorno As Exception
                _logger.ScriviLog("MenuMeteo.TestoMeteoOggi (giorno) - " & exGiorno.Message & Environment.NewLine & exGiorno.StackTrace)
            End Try

            Try
                ore = _meteo.OttieniMeteoOrario(coord.Lat, coord.Lon, "").Result
            Catch exOre As Exception
                _logger.ScriviLog("MenuMeteo.TestoMeteoOggi (ore) - " & exOre.Message & Environment.NewLine & exOre.StackTrace)
            End Try

            Dim frasi As New List(Of String)()

            If attuale IsNot Nothing AndAlso attuale.Valido Then
                Dim fraseAttuale As New StringBuilder()
                fraseAttuale.Append("Adesso a ").Append(nomeLocalita).Append(" ")
                fraseAttuale.Append(DescriviCondizioni(attuale.Condizioni))
                Dim gradiAttuali As String = Gradi(attuale.Temperatura)
                If Not String.IsNullOrWhiteSpace(gradiAttuali) Then
                    fraseAttuale.Append(", con ").Append(gradiAttuali)
                End If
                fraseAttuale.Append(".")
                frasi.Add(fraseAttuale.ToString())

                Dim umidita As Integer = Percentuale(attuale.Umidita)
                If umidita >= 80 Then
                    frasi.Add("L'umidità è alta, al " & umidita.ToString(_culturaItaliana) & " per cento.")
                End If

                Dim fraseVento As String = DescriviVento(attuale.Vento, attuale.DirezioneVento, False)
                If Not String.IsNullOrWhiteSpace(fraseVento) Then frasi.Add(fraseVento)
            End If

            If giorno IsNot Nothing Then
                If Not String.IsNullOrWhiteSpace(giorno.Condizioni) Then
                    frasi.Add("Previsione per la giornata: " & Minuscola(giorno.Condizioni.Trim()) & ".")
                End If

                Dim fraseTemperature As String = DescriviMinimaMassima("Oggi", giorno.TempMin, giorno.TempMax)
                If Not String.IsNullOrWhiteSpace(fraseTemperature) Then frasi.Add(fraseTemperature)

                Dim frasePioggia As String = DescriviPioggia(giorno.ProbabilitaPioggia)
                If Not String.IsNullOrWhiteSpace(frasePioggia) Then frasi.Add(frasePioggia)
            End If

            If ore IsNot Nothing AndAlso ore.Count > 0 Then
                frasi.AddRange(DescriviFasceOrarie(ore, DateTime.Today, DateTime.Now.Hour))
                Dim frasePrimaPioggia As String = DescriviInizioPioggia(ore, DateTime.Today, DateTime.Now.Hour)
                If Not String.IsNullOrWhiteSpace(frasePrimaPioggia) Then frasi.Add(frasePrimaPioggia)
            End If

            If frasi.Count = 0 Then
                If attuale IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attuale.Messaggio) Then Return attuale.Messaggio
                Return "Dati meteo non disponibili al momento per " & nomeLocalita & "."
            End If

            Return String.Join(Environment.NewLine, frasi)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.TestoMeteoOggi - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile recuperare il meteo."
        End Try
    End Function

    Public Function TestoMeteoGiorno(citta As String, giorniDaOggi As Integer) As String
        Try
            If giorniDaOggi <= 0 Then Return TestoMeteoOggi(citta)
            If giorniDaOggi > 6 Then
                Return "Le previsioni arrivano al massimo a sei giorni da oggi."
            End If

            Dim coord As CLSMeteo.CoordinateCitta = Nothing
            Dim nomeLocalita As String = ""
            If Not RisolviLocalitaComando(citta, coord, nomeLocalita) Then
                Return MessaggioLocalitaNonTrovata(citta)
            End If

            Dim giorni As List(Of CLSMeteo.InfoMeteoGiorno) = Nothing
            Try
                giorni = _meteo.OttieniPrevisione(coord.Lat, coord.Lon, "", 7).Result
            Catch exPrevisione As Exception
                _logger.ScriviLog("MenuMeteo.TestoMeteoGiorno (previsione) - " & exPrevisione.Message & Environment.NewLine & exPrevisione.StackTrace)
            End Try

            If giorni Is Nothing OrElse giorni.Count = 0 Then
                Return "Previsioni non disponibili al momento per " & nomeLocalita & "."
            End If

            Dim dataRichiesta As DateTime = DateTime.Today.AddDays(giorniDaOggi)
            Dim chiaveData As String = dataRichiesta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim giorno As CLSMeteo.InfoMeteoGiorno = Nothing
            For Each g As CLSMeteo.InfoMeteoGiorno In giorni
                If g Is Nothing Then Continue For
                If Not String.IsNullOrWhiteSpace(g.DataIso) AndAlso g.DataIso.StartsWith(chiaveData, StringComparison.Ordinal) Then
                    giorno = g
                    Exit For
                End If
            Next
            If giorno Is Nothing AndAlso giorni.Count > giorniDaOggi Then giorno = giorni(giorniDaOggi)
            If giorno Is Nothing Then
                Return "Previsioni non disponibili per il giorno richiesto."
            End If

            Dim nomeGiorno As String = EtichettaGiorno(giorniDaOggi, dataRichiesta)
            Dim frasi As New List(Of String)()

            Dim fraseApertura As New StringBuilder()
            fraseApertura.Append(nomeGiorno).Append(" a ").Append(nomeLocalita)
            If Not String.IsNullOrWhiteSpace(giorno.Condizioni) Then
                fraseApertura.Append(" è previsto: ").Append(Minuscola(giorno.Condizioni.Trim()))
            Else
                fraseApertura.Append(" non sono disponibili le condizioni generali")
            End If
            fraseApertura.Append(".")
            frasi.Add(fraseApertura.ToString())

            Dim fraseTemperature As String = DescriviMinimaMassima("La giornata", giorno.TempMin, giorno.TempMax)
            If Not String.IsNullOrWhiteSpace(fraseTemperature) Then frasi.Add(fraseTemperature)

            Dim frasePioggia As String = DescriviPioggia(giorno.ProbabilitaPioggia)
            If Not String.IsNullOrWhiteSpace(frasePioggia) Then frasi.Add(frasePioggia)

            Dim fraseVento As String = DescriviVento(giorno.Vento, giorno.DirezioneVento, True)
            If Not String.IsNullOrWhiteSpace(fraseVento) Then frasi.Add(fraseVento)

            If Not String.IsNullOrWhiteSpace(giorno.DataIso) Then
                Dim ore As List(Of CLSMeteo.InfoMeteoOra) = Nothing
                Try
                    ore = _meteo.OttieniMeteoOrarioData(coord.Lat, coord.Lon, giorno.DataIso, "").Result
                Catch exOre As Exception
                    _logger.ScriviLog("MenuMeteo.TestoMeteoGiorno (ore) - " & exOre.Message & Environment.NewLine & exOre.StackTrace)
                End Try
                If ore IsNot Nothing AndAlso ore.Count > 0 Then
                    frasi.AddRange(DescriviFasceOrarie(ore, dataRichiesta, 0))
                End If
            End If

            Return String.Join(Environment.NewLine, frasi)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.TestoMeteoGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile recuperare le previsioni."
        End Try
    End Function

    Private Function RisolviLocalitaComando(citta As String, ByRef coord As CLSMeteo.CoordinateCitta, ByRef nomeLocalita As String) As Boolean
        Try
            Dim nomeRicerca As String = If(citta, "").Trim()
            nomeLocalita = ""

            If String.IsNullOrWhiteSpace(nomeRicerca) Then
                Dim preferiti As List(Of CLSMeteo.ComuneInfo) = Nothing
                Try
                    preferiti = _meteo.OttieniComuniPreferiti()
                Catch exPreferiti As Exception
                    _logger.ScriviLog("MenuMeteo.RisolviLocalitaComando (preferiti) - " & exPreferiti.Message & Environment.NewLine & exPreferiti.StackTrace)
                End Try

                If preferiti IsNot Nothing Then
                    For Each comune As CLSMeteo.ComuneInfo In preferiti
                        If comune Is Nothing OrElse comune.IdComune = "0" Then Continue For
                        If comune.Lat = 0 AndAlso comune.Lon = 0 Then Continue For
                        coord = New CLSMeteo.CoordinateCitta With {
                            .IdComune = comune.IdComune,
                            .Lat = comune.Lat,
                            .Lon = comune.Lon,
                            .NomeRisolto = comune.NomeComune,
                            .Paese = comune.Paese,
                            .Regione = comune.Regione
                        }
                        nomeLocalita = comune.NomeComune
                        Return True
                    Next
                End If

                nomeRicerca = CittaDaIndirizzoIp()
            End If

            Dim risolte As CLSMeteo.CoordinateCitta = _meteo.RisolviCoordinate(0, 0, nomeRicerca).Result
            If Not String.IsNullOrWhiteSpace(nomeRicerca) AndAlso String.IsNullOrWhiteSpace(risolte.NomeRisolto) Then Return False
            If risolte.Lat = 0 AndAlso risolte.Lon = 0 Then Return False

            coord = risolte
            nomeLocalita = If(String.IsNullOrWhiteSpace(risolte.NomeRisolto), nomeRicerca, risolte.NomeRisolto)
            If String.IsNullOrWhiteSpace(nomeLocalita) Then nomeLocalita = "posizione attuale"

            Try
                Dim comuneRecente As New CLSMeteo.ComuneInfo With {
                    .IdComune = risolte.IdComune,
                    .NomeComune = nomeLocalita,
                    .Lat = risolte.Lat,
                    .Lon = risolte.Lon,
                    .Paese = risolte.Paese,
                    .Regione = risolte.Regione,
                    .UltimoUso = DateTime.UtcNow
                }
                If Not String.IsNullOrWhiteSpace(comuneRecente.IdComune) OrElse Not String.IsNullOrWhiteSpace(comuneRecente.NomeComune) Then
                    _meteo.AggiungiComuneRecente(comuneRecente)
                End If
            Catch exRecente As Exception
                _logger.ScriviLog("MenuMeteo.RisolviLocalitaComando (recente) - " & exRecente.Message & Environment.NewLine & exRecente.StackTrace)
            End Try

            Return True
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.RisolviLocalitaComando - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CittaDaIndirizzoIp() As String
        Try
            Using client As New System.Net.WebClient()
                client.Headers.Add("User-Agent", "WinGabriel/2.0 (accessibilita; postmaster@wingabriel.net)")
                client.Encoding = Encoding.UTF8
                Dim json As String = client.DownloadString("http://ip-api.com/json/")
                Dim corrispondenza As Match = Regex.Match(json, """city""\s*:\s*""([^""]*)""")
                If corrispondenza.Success Then Return corrispondenza.Groups(1).Value.Trim()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.CittaDaIndirizzoIp - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Private Function MessaggioLocalitaNonTrovata(citta As String) As String
        Try
            If String.IsNullOrWhiteSpace(citta) Then
                Return "Posizione non disponibile. Indica il nome di una città oppure aggiungi un comune ai preferiti del meteo."
            End If
            Return "Località " & citta.Trim() & " non trovata."
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.MessaggioLocalitaNonTrovata - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Località non trovata."
        End Try
    End Function

    Private Function EtichettaGiorno(giorniDaOggi As Integer, data As DateTime) As String
        Try
            If giorniDaOggi = 1 Then Return "Domani"
            If giorniDaOggi = 2 Then Return "Dopodomani"
            Dim testo As String = data.ToString("dddd d MMMM", _culturaItaliana)
            Return Maiuscola(testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.EtichettaGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Quel giorno"
        End Try
    End Function

    Private Function DescriviCondizioni(condizioni As String) As String
        Try
            Dim testo As String = Minuscola(If(condizioni, "").Trim())
            If String.IsNullOrWhiteSpace(testo) Then Return "le condizioni non sono disponibili"

            If testo.Contains("sereno") OrElse testo.Contains("nuvol") OrElse testo.Contains("coperto") OrElse testo.Contains("velato") Then
                Return "il cielo è " & RimuoviPrefissoCielo(testo)
            End If

            Dim primaParola As String = testo.Split(New Char() {" "c, ","c}, StringSplitOptions.RemoveEmptyEntries)(0)

            If primaParola = "temporale" Then Return "c'è un " & testo

            Dim plurali As String() = {"piogge", "nevicate", "precipitazioni", "raffiche"}
            If primaParola.EndsWith("i", StringComparison.Ordinal) OrElse Array.IndexOf(plurali, primaParola) >= 0 Then
                Return "ci sono " & testo
            End If

            Dim singolari As String() = {"pioggia", "pioviggine", "neve", "nebbia", "foschia", "grandine", "nevischio", "rovescio", "acquazzone", "gelicidio", "brina"}
            If Array.IndexOf(singolari, primaParola) >= 0 Then Return "c'è " & testo

            Return "il tempo è " & testo
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviCondizioni - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "il tempo è " & If(condizioni, "")
        End Try
    End Function

    Private Function RimuoviPrefissoCielo(testo As String) As String
        Try
            If testo.StartsWith("cielo ", StringComparison.Ordinal) Then Return testo.Substring(6).Trim()
            Return testo
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.RimuoviPrefissoCielo - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return testo
        End Try
    End Function

    Private Function DescriviMinimaMassima(soggetto As String, testoMinima As String, testoMassima As String) As String
        Try
            Dim minima As Double = Numero(testoMinima)
            Dim massima As Double = Numero(testoMassima)
            If Double.IsNaN(minima) AndAlso Double.IsNaN(massima) Then Return ""

            If Double.IsNaN(minima) Then Return soggetto & " arriva a una massima di " & GradiDaNumero(massima) & "."
            If Double.IsNaN(massima) Then Return soggetto & " parte da una minima di " & GradiDaNumero(minima) & "."

            If String.Equals(soggetto, "Oggi", StringComparison.Ordinal) Then
                Return "Oggi la minima è di " & GradiDaNumero(minima) & " e la massima di " & GradiDaNumero(massima) & "."
            End If
            Return "La minima sarà di " & GradiDaNumero(minima) & " e la massima di " & GradiDaNumero(massima) & "."
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviMinimaMassima - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function DescriviPioggia(testoProbabilita As String) As String
        Try
            Dim probabilita As Integer = Percentuale(testoProbabilita)
            If probabilita < 0 Then Return ""
            If probabilita < 20 Then Return "La pioggia è poco probabile."
            If probabilita < 50 Then Return "C'è una possibilità di pioggia del " & probabilita.ToString(_culturaItaliana) & " per cento."
            If probabilita < 80 Then Return "La pioggia è probabile, al " & probabilita.ToString(_culturaItaliana) & " per cento."
            Return "La pioggia è molto probabile, al " & probabilita.ToString(_culturaItaliana) & " per cento."
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviPioggia - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function DescriviVento(testoVento As String, direzione As String, previsione As Boolean) As String
        Try
            Dim velocita As Double = Numero(testoVento)
            If Double.IsNaN(velocita) OrElse velocita < 20 Then Return ""

            Dim intensita As String = "sostenuto"
            If velocita >= 50 Then
                intensita = "molto forte"
            ElseIf velocita >= 35 Then
                intensita = "forte"
            End If

            Dim frase As New StringBuilder()
            frase.Append(If(previsione, "Il vento sarà ", "Il vento è ")).Append(intensita)
            If Not String.IsNullOrWhiteSpace(direzione) Then
                frase.Append(", da ").Append(Minuscola(direzione.Trim()))
            End If
            frase.Append(", con ").Append(Math.Round(velocita).ToString("0", _culturaItaliana)).Append(" chilometri orari.")
            Return frase.ToString()
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviVento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function DescriviFasceOrarie(ore As List(Of CLSMeteo.InfoMeteoOra), giorno As DateTime, oraMinima As Integer) As List(Of String)
        Dim frasi As New List(Of String)()
        Try
            Dim fasce As New List(Of FasciaMeteoVocale)()
            fasce.Add(New FasciaMeteoVocale With {.Nome = "In mattinata", .OraInizio = 6, .OraFine = 12})
            fasce.Add(New FasciaMeteoVocale With {.Nome = "Nel pomeriggio", .OraInizio = 12, .OraFine = 18})
            fasce.Add(New FasciaMeteoVocale With {.Nome = "In serata", .OraInizio = 18, .OraFine = 24})

            Dim orePerGiorno As List(Of KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra)) = AssegnaDateAlleOre(ore, giorno)

            For Each voce As KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra) In orePerGiorno
                If voce.Key.Date <> giorno.Date Then Continue For
                If voce.Key.Hour < oraMinima Then Continue For
                Dim o As CLSMeteo.InfoMeteoOra = voce.Value
                If o Is Nothing Then Continue For

                For Each fascia As FasciaMeteoVocale In fasce
                    If voce.Key.Hour < fascia.OraInizio OrElse voce.Key.Hour >= fascia.OraFine Then Continue For

                    If Not String.IsNullOrWhiteSpace(o.Condizioni) Then fascia.Condizioni.Add(Minuscola(o.Condizioni.Trim()))

                    Dim temperatura As Double = Numero(o.Temperatura)
                    If Not Double.IsNaN(temperatura) Then
                        If Double.IsNaN(fascia.TemperaturaMin) OrElse temperatura < fascia.TemperaturaMin Then fascia.TemperaturaMin = temperatura
                        If Double.IsNaN(fascia.TemperaturaMax) OrElse temperatura > fascia.TemperaturaMax Then fascia.TemperaturaMax = temperatura
                    End If

                    Dim pioggia As Integer = Percentuale(o.ProbabilitaPioggia)
                    If pioggia > fascia.PioggiaMax Then fascia.PioggiaMax = pioggia
                Next
            Next

            For Each fascia As FasciaMeteoVocale In fasce
                If fascia.Condizioni.Count = 0 AndAlso Double.IsNaN(fascia.TemperaturaMin) Then Continue For

                Dim frase As New StringBuilder()
                frase.Append(fascia.Nome).Append(": ")
                Dim condizione As String = CondizionePrevalente(fascia.Condizioni)
                If Not String.IsNullOrWhiteSpace(condizione) Then
                    frase.Append(condizione)
                Else
                    frase.Append("condizioni non disponibili")
                End If

                If Not Double.IsNaN(fascia.TemperaturaMin) Then
                    Dim minimo As Integer = CInt(Math.Round(fascia.TemperaturaMin))
                    Dim massimo As Integer = CInt(Math.Round(fascia.TemperaturaMax))
                    If minimo = massimo Then
                        frase.Append(", ").Append(GradiDaNumero(minimo))
                    Else
                        frase.Append(", tra ").Append(minimo.ToString(_culturaItaliana)).Append(" e ").Append(GradiDaNumero(massimo))
                    End If
                End If

                If fascia.PioggiaMax >= 20 Then
                    frase.Append(", pioggia al ").Append(fascia.PioggiaMax.ToString(_culturaItaliana)).Append(" per cento")
                End If

                frase.Append(".")
                frasi.Add(frase.ToString())
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviFasceOrarie - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return frasi
    End Function

    Private Function DescriviInizioPioggia(ore As List(Of CLSMeteo.InfoMeteoOra), giorno As DateTime, oraMinima As Integer) As String
        Try
            Dim orePerGiorno As List(Of KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra)) = AssegnaDateAlleOre(ore, giorno)
            Dim primaOraPiovosa As Integer = -1
            Dim piovosaGiaOra As Boolean = False

            For Each voce As KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra) In orePerGiorno
                If voce.Key.Date <> giorno.Date Then Continue For
                If voce.Key.Hour < oraMinima Then Continue For
                If voce.Value Is Nothing Then Continue For

                Dim pioggia As Integer = Percentuale(voce.Value.ProbabilitaPioggia)
                If pioggia >= 50 Then
                    If voce.Key.Hour = oraMinima Then piovosaGiaOra = True
                    primaOraPiovosa = voce.Key.Hour
                    Exit For
                End If
            Next

            If primaOraPiovosa < 0 OrElse piovosaGiaOra Then Return ""
            Return "La pioggia diventa probabile verso le " & primaOraPiovosa.ToString(_culturaItaliana) & "."
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.DescriviInizioPioggia - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function AssegnaDateAlleOre(ore As List(Of CLSMeteo.InfoMeteoOra), giorno As DateTime) As List(Of KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra))
        Dim risultato As New List(Of KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra))()
        Try
            If ore Is Nothing Then Return risultato

            Dim giorniAggiunti As Integer = 0
            Dim oraPrecedente As Integer = -1

            For Each o As CLSMeteo.InfoMeteoOra In ore
                If o Is Nothing Then Continue For

                Dim momento As DateTime
                If Not String.IsNullOrWhiteSpace(o.Id) AndAlso DateTime.TryParse(o.Id, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, momento) AndAlso momento.Year > 2000 Then
                    risultato.Add(New KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra)(momento, o))
                    Continue For
                End If

                Dim ora As Integer = OraDaTesto(o.Ora)
                If ora < 0 Then Continue For
                If oraPrecedente >= 0 AndAlso ora < oraPrecedente Then giorniAggiunti += 1
                oraPrecedente = ora
                momento = giorno.Date.AddDays(giorniAggiunti).AddHours(ora)
                risultato.Add(New KeyValuePair(Of DateTime, CLSMeteo.InfoMeteoOra)(momento, o))
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.AssegnaDateAlleOre - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function OraDaTesto(testo As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return -1
            Dim corrispondenza As Match = Regex.Match(testo, "(\d{1,2})")
            If Not corrispondenza.Success Then Return -1
            Dim ora As Integer = Integer.Parse(corrispondenza.Groups(1).Value, CultureInfo.InvariantCulture)
            If ora < 0 OrElse ora > 23 Then Return -1
            Return ora
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.OraDaTesto - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Private Function CondizionePrevalente(condizioni As List(Of String)) As String
        Try
            If condizioni Is Nothing OrElse condizioni.Count = 0 Then Return ""
            Dim conteggi As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim ordine As New List(Of String)()
            For Each c As String In condizioni
                If String.IsNullOrWhiteSpace(c) Then Continue For
                If conteggi.ContainsKey(c) Then
                    conteggi(c) += 1
                Else
                    conteggi(c) = 1
                    ordine.Add(c)
                End If
            Next

            Dim migliore As String = ""
            Dim massimo As Integer = 0
            For Each c As String In ordine
                If conteggi(c) > massimo Then
                    massimo = conteggi(c)
                    migliore = c
                End If
            Next
            Return migliore
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.CondizionePrevalente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function Gradi(testo As String) As String
        Try
            Dim valore As Double = Numero(testo)
            If Double.IsNaN(valore) Then Return ""
            Return GradiDaNumero(valore)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Gradi - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function GradiDaNumero(valore As Double) As String
        Try
            Dim arrotondato As Integer = CInt(Math.Round(valore))
            If arrotondato = 1 OrElse arrotondato = -1 Then Return arrotondato.ToString(_culturaItaliana) & " grado"
            Return arrotondato.ToString(_culturaItaliana) & " gradi"
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.GradiDaNumero - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function Numero(testo As String) As Double
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return Double.NaN
            Dim corrispondenza As Match = Regex.Match(testo, "-?\d+(?:[.,]\d+)?")
            If Not corrispondenza.Success Then Return Double.NaN
            Dim valore As Double
            If Double.TryParse(corrispondenza.Value.Replace(","c, "."c), NumberStyles.Float, CultureInfo.InvariantCulture, valore) Then Return valore
            Return Double.NaN
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Numero - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Double.NaN
        End Try
    End Function

    Private Function Percentuale(testo As String) As Integer
        Try
            Dim valore As Double = Numero(testo)
            If Double.IsNaN(valore) Then Return -1
            Dim arrotondato As Integer = CInt(Math.Round(valore))
            If arrotondato < 0 Then Return -1
            If arrotondato > 100 Then Return 100
            Return arrotondato
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Percentuale - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Private Function Minuscola(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            If testo.Length > 1 AndAlso Char.IsUpper(testo(1)) Then Return testo
            Return testo.Substring(0, 1).ToLower(_culturaItaliana) & testo.Substring(1)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Minuscola - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "")
        End Try
    End Function

    Private Function Maiuscola(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return testo.Substring(0, 1).ToUpper(_culturaItaliana) & testo.Substring(1)
        Catch ex As Exception
            _logger.ScriviLog("MenuMeteo.Maiuscola - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return If(testo, "")
        End Try
    End Function

#End Region

End Class