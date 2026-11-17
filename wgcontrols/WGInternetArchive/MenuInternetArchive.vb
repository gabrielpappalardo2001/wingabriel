Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGPlayerMpv
Public Class MenuInternetArchive

    Private Const PAGE_SIZE As Integer = 120
    Private Const CHIAVE_CONFIG_MODALITA_ASCOLTO As String = "internetarchive_modalita_ascolto"

    Private Menu As WGMenu
    Private camporicerca As WGCampoTesto
    Private listaRisultati As WGMenuLista
    Private listaAutori As WGMenuLista
    Private listaGeneri As WGMenuLista
    Private listaSupporti As WGMenuLista
    Private _campoDocumento As WGCampo

    Private ReadOnly _archivio As New CLSInternetArchive
    Private ReadOnly _logger As New Logger()
    Private ReadOnly _config As New WGConfig()

    Private _risultatiCorrenti As List(Of ElementoArchivio)
    Private _queryCorrente As String
    Private _filtroSupportoRicerca As String = "entrambi"

    Private Const CHIAVE_CONFIG_RICERCHE_RECENTI As String = "internetarchive_ricerche_recenti"
    Private Const MAX_RICERCHE_RECENTI As Integer = 10
    Private listaRicercheRecenti As WGMenuLista

    Private listaCapitoli As WGMenuLista
    Private _capitoliPercorsiCorrenti As String()
    Private _capitoliLibroTitolo As String
    Private _percorsiAscoltoInSospeso As List(Of String)
    Private _titoloAscoltoInSospeso As String

    Private listaLibriScaricati As WGMenuLista
    Private ReadOnly _libriScaricatiPerId As New Dictionary(Of String, WGCentroDiControllo.LibroScaricato)
    Private _filtroLibriScaricatiCorrente As String = "Tutti"
    Private _domandaEliminaLibro As WGDomanda
    Private _idLibroDaEliminare As String = ""

    Private _filtroTipo As String = ""
    Private _filtroValore As String = ""
    Private _paginaCorrente As Integer = 0
    Private _caricamentoInCorso As Boolean = False
    Private _haAltrePagine As Boolean = True

    Private _cursoriTitolo As New List(Of String)
    Private _cursoriId As New List(Of Long)

    Public Sub avvia()
        Try
            If Menu Is Nothing Then
                Menu = New WGMenu()
            End If
            Menu.aggiungi("Cerca", AddressOf ricercaelemento)
            Menu.aggiungi("Ricerche recenti", AddressOf mostraRicercheRecenti)
            Menu.aggiungi("Elenco per Autore", AddressOf mostraelencoper_autore)
            Menu.aggiungi("Elenco per Genere", AddressOf mostraelencoper_genere)
            Menu.aggiungi("Elenco per Supporto", AddressOf mostraelencoper_supporto)
            Menu.aggiungi("Aggiorna catalogo", AddressOf aggiornacatalogo)
            Menu.aggiungi("Libri scaricati", AddressOf sceltaTipoLibriScaricati)
            Menu.avvia("Internet Archive")
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.avvia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ReimpostaCursori()
        Try
            _cursoriTitolo = New List(Of String) From {Nothing}
            _cursoriId = New List(Of Long) From {0L}
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ReimpostaCursori: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercaelemento()
        Try
            camporicerca = New WGCampoTesto()
            camporicerca.init("Scrivi il titolo da cercare su Internet Archive", "", campoobbligatorio:=True)
            AddHandler camporicerca.chiusura, AddressOf avvia_ricerca
            camporicerca.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ricercaelemento: " & ex.ToString())
        End Try
    End Sub

    Private Sub avvia_ricerca()
        Try
            Dim query As String = camporicerca.OttieniTesto()
            camporicerca.chiudi()
            If String.IsNullOrWhiteSpace(query) Then Return

            _queryCorrente = query.Trim()
            MostraSceltaTipoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.avvia_ricerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraSceltaTipoRicerca()
        Try
            Dim menuTipo As New WGMenu()
            menuTipo.aggiungi("Cerca solo testi", AddressOf AvviaRicercaTesto)
            menuTipo.aggiungi("Cerca solo audiolibri", AddressOf AvviaRicercaAudio)
            menuTipo.aggiungi("Cerca sia testi che audiolibri", AddressOf AvviaRicercaEntrambi)
            menuTipo.avvia("Scelta del tipo di supporto", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.MostraSceltaTipoRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaRicercaTesto()
        EseguiRicercaConTipo("testo")
    End Sub

    Private Sub AvviaRicercaAudio()
        EseguiRicercaConTipo("audio")
    End Sub

    Private Sub AvviaRicercaEntrambi()
        EseguiRicercaConTipo("entrambi")
    End Sub

    Private Sub EseguiRicercaConTipo(tipoSupporto As String)
        Try
            _filtroSupportoRicerca = tipoSupporto
            _filtroTipo = "ricerca"
            _filtroValore = _queryCorrente
            _paginaCorrente = 0
            _haAltrePagine = False
            ReimpostaCursori()

            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun elemento trovato con questi termini di ricerca.")
                Return
            End If

            SalvaRicercaRecente(_queryCorrente)
            MostraRisultati("Risultati ricerca")
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.EseguiRicercaConTipo: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniRicercheRecenti() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Dim valoreGrezzo As String = _config.LeggiValore(CHIAVE_CONFIG_RICERCHE_RECENTI)
            If String.IsNullOrWhiteSpace(valoreGrezzo) Then Return risultati
            Dim voci As String() = valoreGrezzo.Split(New String() {"||"}, StringSplitOptions.RemoveEmptyEntries)
            For Each voce As String In voci
                If Not String.IsNullOrWhiteSpace(voce) Then risultati.Add(voce.Trim())
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.OttieniRicercheRecenti: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Private Sub SalvaRicercaRecente(query As String)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return
            Dim elenco As List(Of String) = OttieniRicercheRecenti()

            Dim indiceEsistente As Integer = -1
            For indice As Integer = 0 To elenco.Count - 1
                If String.Equals(elenco(indice), query, StringComparison.OrdinalIgnoreCase) Then
                    indiceEsistente = indice
                    Exit For
                End If
            Next
            If indiceEsistente >= 0 Then elenco.RemoveAt(indiceEsistente)

            elenco.Insert(0, query)
            While elenco.Count > MAX_RICERCHE_RECENTI
                elenco.RemoveAt(elenco.Count - 1)
            End While

            _config.SalvaValore(CHIAVE_CONFIG_RICERCHE_RECENTI, String.Join("||", elenco))
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.SalvaRicercaRecente: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraRicercheRecenti()
        Try
            Dim elenco As List(Of String) = OttieniRicercheRecenti()
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggio("Non hai ancora effettuato ricerche.")
                Return
            End If

            ChiudiListaRicercheRecenti()

            listaRicercheRecenti = New WGMenuLista()
            listaRicercheRecenti.ciclono = True
            listaRicercheRecenti.selezioneMenu = True
            listaRicercheRecenti.multiselezioneConsentita = False

            listaRicercheRecenti.BeginUpdate()
            For Each query As String In elenco
                Try
                    listaRicercheRecenti.aggiungiElemento(query, query)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuInternetArchive.mostraRicercheRecenti elemento: " & exRiga.Message)
                End Try
            Next
            listaRicercheRecenti.EndUpdate()

            AddHandler listaRicercheRecenti.ElementoSelezionato, AddressOf ricercaRecenteSelezionata
            AddHandler listaRicercheRecenti.escPremuto, AddressOf ChiudiListaRicercheRecenti

            listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            listaRicercheRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.mostraRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRicercheRecenti()
        Try
            If listaRicercheRecenti IsNot Nothing Then
                RemoveHandler listaRicercheRecenti.ElementoSelezionato, AddressOf ricercaRecenteSelezionata
                RemoveHandler listaRicercheRecenti.escPremuto, AddressOf ChiudiListaRicercheRecenti
                listaRicercheRecenti.abbandona()
                listaRicercheRecenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercaRecenteSelezionata(query As String)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return
            ChiudiListaRicercheRecenti()
            _queryCorrente = query
            MostraSceltaTipoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ricercaRecenteSelezionata: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneRicercaPagina()
        Try
            _risultatiCorrenti = _archivio.Cerca(_filtroValore, PAGE_SIZE, _paginaCorrente, _filtroSupportoRicerca)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.OperazioneRicercaPagina: " & ex.ToString())
            _risultatiCorrenti = New List(Of ElementoArchivio)
        End Try
    End Sub

    Private Sub EseguiConAttesa(messaggio As String, operazione As Action)
        Dim processo As WGProcessoInCorso = Nothing
        Try
            If operazione Is Nothing Then Return

            processo = New WGProcessoInCorso()
            processo.Avvia(messaggio, False)

            Dim attivita As Task = Task.Run(operazione)

            While Not attivita.IsCompleted
                Try
                    Application.DoEvents()
                    Thread.Sleep(30)
                Catch exAttesa As Exception
                    _logger.ScriviLog("MenuInternetArchive.EseguiConAttesa ciclo: " & exAttesa.Message)
                End Try
            End While

            If attivita.IsFaulted AndAlso attivita.Exception IsNot Nothing Then
            End If

            Try
                processo.Fine()
            Catch
            End Try
            processo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.EseguiConAttesa: " & ex.ToString())
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch
            End Try
            processo = Nothing
        End Try
    End Sub

    Private Sub mostraelencoper_autore()
        Try
            Dim autori As List(Of String) = _archivio.OttieniAutoriCatalogati()
            If autori Is Nothing OrElse autori.Count = 0 Then
                MostraMessaggio("Il catalogo locale e' vuoto. Usa prima ""Aggiorna catalogo locale"" per scaricare l'elenco.")
                Return
            End If

            ChiudiListaAutori()

            listaAutori = New WGMenuLista()
            listaAutori.ciclono = True
            listaAutori.selezioneMenu = True
            listaAutori.multiselezioneConsentita = False

            Dim dict As New Dictionary(Of String, String)(autori.Count)
            For Each autore As String In autori
                If Not String.IsNullOrEmpty(autore) AndAlso Not dict.ContainsKey(autore) Then
                    dict.Add(autore, autore)
                End If
            Next
            listaAutori.CaricaElementiMassivi(dict)

            AddHandler listaAutori.ElementoSelezionato, AddressOf autore_selezionato
            AddHandler listaAutori.escPremuto, AddressOf ChiudiListaAutori

            listaAutori.impostaTitolo("Elenco Autori, " & autori.Count & " autori")
            listaAutori.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.mostraelencoper_autore: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaAutori()
        Try
            If listaAutori IsNot Nothing Then
                RemoveHandler listaAutori.ElementoSelezionato, AddressOf autore_selezionato
                RemoveHandler listaAutori.escPremuto, AddressOf ChiudiListaAutori
                listaAutori.abbandona()
                listaAutori = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaAutori: " & ex.ToString())
        End Try
    End Sub

    Private Sub autore_selezionato(autore As String)
        Try
            _filtroTipo = "autore"
            _filtroValore = autore
            _paginaCorrente = 0
            ReimpostaCursori()
            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun elemento trovato per questo autore.")
                Return
            End If
            MostraRisultati("Elementi di " & autore)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.autore_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_genere()
        Try
            Dim generi As List(Of String) = _archivio.OttieniGeneriCatalogati()
            If generi Is Nothing OrElse generi.Count = 0 Then
                MostraMessaggio("Il catalogo locale e' vuoto. Usa prima ""Aggiorna catalogo locale"" per scaricare l'elenco.")
                Return
            End If

            ChiudiListaGeneri()

            listaGeneri = New WGMenuLista()
            listaGeneri.ciclono = True
            listaGeneri.selezioneMenu = True
            listaGeneri.multiselezioneConsentita = False

            Dim dict As New Dictionary(Of String, String)(generi.Count)
            For Each genere As String In generi
                If Not String.IsNullOrEmpty(genere) AndAlso Not dict.ContainsKey(genere) Then
                    dict.Add(genere, genere)
                End If
            Next
            listaGeneri.CaricaElementiMassivi(dict)

            AddHandler listaGeneri.ElementoSelezionato, AddressOf genere_selezionato
            AddHandler listaGeneri.escPremuto, AddressOf ChiudiListaGeneri

            listaGeneri.impostaTitolo("Elenco Generi, " & generi.Count & " generi")
            listaGeneri.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.mostraelencoper_genere: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaGeneri()
        Try
            If listaGeneri IsNot Nothing Then
                RemoveHandler listaGeneri.ElementoSelezionato, AddressOf genere_selezionato
                RemoveHandler listaGeneri.escPremuto, AddressOf ChiudiListaGeneri
                listaGeneri.abbandona()
                listaGeneri = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaGeneri: " & ex.ToString())
        End Try
    End Sub

    Private Sub genere_selezionato(genere As String)
        Try
            _filtroTipo = "genere"
            _filtroValore = genere
            _paginaCorrente = 0
            ReimpostaCursori()
            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun elemento trovato per questo genere.")
                Return
            End If
            MostraRisultati("Elementi del genere " & genere)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.genere_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_supporto()
        Try
            Dim supporti As List(Of String) = _archivio.OttieniSupportiCatalogati()
            If supporti Is Nothing OrElse supporti.Count = 0 Then
                MostraMessaggio("Il catalogo locale e' vuoto. Usa prima ""Aggiorna catalogo locale"" per scaricare l'elenco.")
                Return
            End If

            ChiudiListaSupporti()

            listaSupporti = New WGMenuLista()
            listaSupporti.ciclono = True
            listaSupporti.selezioneMenu = True
            listaSupporti.multiselezioneConsentita = False

            Dim dict As New Dictionary(Of String, String)(supporti.Count)
            For Each supporto As String In supporti
                If Not String.IsNullOrEmpty(supporto) AndAlso Not dict.ContainsKey(supporto) Then
                    dict.Add(supporto, supporto)
                End If
            Next
            listaSupporti.CaricaElementiMassivi(dict)

            AddHandler listaSupporti.ElementoSelezionato, AddressOf supporto_selezionato
            AddHandler listaSupporti.escPremuto, AddressOf ChiudiListaSupporti

            listaSupporti.impostaTitolo("Elenco Supporti")
            listaSupporti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.mostraelencoper_supporto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaSupporti()
        Try
            If listaSupporti IsNot Nothing Then
                RemoveHandler listaSupporti.ElementoSelezionato, AddressOf supporto_selezionato
                RemoveHandler listaSupporti.escPremuto, AddressOf ChiudiListaSupporti
                listaSupporti.abbandona()
                listaSupporti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaSupporti: " & ex.ToString())
        End Try
    End Sub

    Private Sub supporto_selezionato(supporto As String)
        Try
            _filtroTipo = "supporto"
            _filtroValore = supporto
            _paginaCorrente = 0
            ReimpostaCursori()
            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun elemento trovato per questo supporto.")
                Return
            End If
            MostraRisultati("Elementi di tipo " & supporto)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.supporto_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Function CaricaPaginaCorrente() As Boolean
        If _caricamentoInCorso Then Return False
        _caricamentoInCorso = True
        Try
            If _paginaCorrente < 0 OrElse _paginaCorrente >= _cursoriTitolo.Count Then
                _risultatiCorrenti = New List(Of ElementoArchivio)
                Return False
            End If

            Dim titoloCursore As String = _cursoriTitolo(_paginaCorrente)
            Dim idCursore As Long = _cursoriId(_paginaCorrente)
            Dim titoloUltimo As String = Nothing
            Dim idUltimo As Long = 0

            Select Case _filtroTipo
                Case "supporto"
                    _risultatiCorrenti = _archivio.OttieniElementiPerSupporto(_filtroValore, PAGE_SIZE, titoloCursore, idCursore, titoloUltimo, idUltimo)
                Case "autore"
                    _risultatiCorrenti = _archivio.OttieniElementiPerAutore(_filtroValore, PAGE_SIZE, titoloCursore, idCursore, titoloUltimo, idUltimo)
                Case "genere"
                    _risultatiCorrenti = _archivio.OttieniElementiPerGenere(_filtroValore, PAGE_SIZE, titoloCursore, idCursore, titoloUltimo, idUltimo)
                Case "ricerca"
                    EseguiConAttesa("Ricerca in corso su Internet Archive, attendi", New Action(AddressOf OperazioneRicercaPagina))
                    titoloUltimo = Nothing
                    idUltimo = 0
                Case Else
                    _risultatiCorrenti = New List(Of ElementoArchivio)
            End Select

            If _risultatiCorrenti Is Nothing Then
                _risultatiCorrenti = New List(Of ElementoArchivio)
            End If

            If _risultatiCorrenti.Count > 0 AndAlso _paginaCorrente + 1 = _cursoriTitolo.Count Then
                _cursoriTitolo.Add(titoloUltimo)
                _cursoriId.Add(idUltimo)
            End If

            _haAltrePagine = (_risultatiCorrenti.Count >= PAGE_SIZE)
            Return _risultatiCorrenti.Count > 0
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.CaricaPaginaCorrente: " & ex.ToString())
            _risultatiCorrenti = New List(Of ElementoArchivio)
            _haAltrePagine = False
            Return False
        Finally
            _caricamentoInCorso = False
        End Try
    End Function

    Private Sub sceltaTipoLibriScaricati()
        Try
            Dim menuTipo As New WGMenu()
            menuTipo.aggiungi("Audio", AddressOf mostraLibriScaricatiAudio)
            menuTipo.aggiungi("Testo", AddressOf mostraLibriScaricatiTesto)
            menuTipo.aggiungi("Tutti", AddressOf mostraLibriScaricatiTutti)
            menuTipo.avvia("Scelta del tipo da vedere", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.sceltaTipoLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraLibriScaricatiAudio()
        mostraLibriScaricati("Audio")
    End Sub

    Private Sub mostraLibriScaricatiTesto()
        mostraLibriScaricati("Testo")
    End Sub

    Private Sub mostraLibriScaricatiTutti()
        mostraLibriScaricati("Tutti")
    End Sub

    Private Sub mostraLibriScaricati(filtroTipo As String)
        Try
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then Return

            _filtroLibriScaricatiCorrente = filtroTipo

            Dim libri As List(Of WGCentroDiControllo.LibroScaricato) = centro.OttieniLibriScaricatiArchivioInternet(filtroTipo)
            If libri Is Nothing OrElse libri.Count = 0 Then
                MostraMessaggio("Non hai ancora scaricato nessun elemento di questo tipo da Internet Archive.")
                Return
            End If

            ChiudiListaLibriScaricati()

            listaLibriScaricati = New WGMenuLista()
            listaLibriScaricati.ciclono = True
            listaLibriScaricati.selezioneMenu = True
            listaLibriScaricati.multiselezioneConsentita = False

            _libriScaricatiPerId.Clear()
            Dim dict As New Dictionary(Of String, String)(libri.Count)
            For Each libro As WGCentroDiControllo.LibroScaricato In libri
                _libriScaricatiPerId(libro.Id) = libro
                Dim etichetta As String = libro.Titolo & " - " & libro.Autore & " (" & libro.Tipo & ")"
                If Not dict.ContainsKey(etichetta) Then
                    dict.Add(etichetta, libro.Id)
                Else
                    dict.Add(etichetta & " [" & libro.Id & "]", libro.Id)
                End If
            Next
            listaLibriScaricati.CaricaElementiMassivi(dict)

            listaLibriScaricati.aggiungiOpzione("Elimina", AddressOf EliminaLibroScaricatoOpzione)

            AddHandler listaLibriScaricati.ElementoSelezionato, AddressOf libroScaricatoSelezionato
            AddHandler listaLibriScaricati.escPremuto, AddressOf ChiudiListaLibriScaricati

            listaLibriScaricati.impostaTitolo("Libri scaricati")
            listaLibriScaricati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.mostraLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaLibriScaricati()
        Try
            If listaLibriScaricati IsNot Nothing Then
                RemoveHandler listaLibriScaricati.ElementoSelezionato, AddressOf libroScaricatoSelezionato
                RemoveHandler listaLibriScaricati.escPremuto, AddressOf ChiudiListaLibriScaricati
                listaLibriScaricati.abbandona()
                listaLibriScaricati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub libroScaricatoSelezionato(id As String)
        Try
            If Not _libriScaricatiPerId.ContainsKey(id) Then Return
            Dim libro As WGCentroDiControllo.LibroScaricato = _libriScaricatiPerId(id)

            If String.Equals(libro.Tipo, "Testo", StringComparison.OrdinalIgnoreCase) Then
                MostraTestoDaFile(libro.PercorsoLocale, libro.Titolo)
                Return
            End If

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then Return

            Dim capitoli As List(Of WGCentroDiControllo.CapitoloScaricato) = centro.OttieniCapitoliLibroArchivioInternet(id)
            If capitoli Is Nothing OrElse capitoli.Count = 0 Then
                MostraMessaggio("Non ho trovato i file di questo audiolibro.")
                Return
            End If

            Dim percorsi As New List(Of String)
            For Each capitolo As WGCentroDiControllo.CapitoloScaricato In capitoli
                percorsi.Add(capitolo.PercorsoFile)
            Next

            MostraSelezioneCapitoliDaPercorsi(percorsi, libro.Titolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.libroScaricatoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraTestoDaFile(percorsoFile As String, titolo As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                MostraMessaggio("Non ho trovato il file di testo.")
                Return
            End If

            Dim testo As String = File.ReadAllText(percorsoFile)

            ChiudiCampoDocumento()

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init(titolo, testo, True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.MostraTestoDaFile: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il testo.")
        End Try
    End Sub

    Private Sub EliminaLibroScaricatoOpzione(sender As Object, e As EventArgs)
        Try
            If listaLibriScaricati Is Nothing Then Return

            Dim elementi As List(Of String) = listaLibriScaricati.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            _idLibroDaEliminare = elementi(0)

            ChiudiDomandaEliminaLibro()
            _domandaEliminaLibro = New WGDomanda()
            AddHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibro
            _domandaEliminaLibro.avvia("Vuoi eliminare definitivamente questo elemento scaricato? L'operazione non e' reversibile.")
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.EliminaLibroScaricatoOpzione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ConfermaEliminaLibro(risposta As DialogResult)
        Try
            ChiudiDomandaEliminaLibro()

            If risposta = DialogResult.Yes AndAlso Not String.IsNullOrWhiteSpace(_idLibroDaEliminare) Then
                Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
                Dim eliminato As Boolean = False
                If centro IsNot Nothing Then
                    eliminato = centro.EliminaLibroArchivioInternet(_idLibroDaEliminare)
                End If

                If eliminato Then
                    MostraMessaggio("Elemento eliminato.")
                    mostraLibriScaricati(_filtroLibriScaricatiCorrente)
                Else
                    MostraMessaggio("Non e' stato possibile eliminare l'elemento.")
                End If
            End If

            _idLibroDaEliminare = ""
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ConfermaEliminaLibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiDomandaEliminaLibro()
        Try
            If _domandaEliminaLibro IsNot Nothing Then
                RemoveHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibro
                _domandaEliminaLibro = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiDomandaEliminaLibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub aggiornacatalogo()
        Try
            WGCentroDiControllo.GetInstance().AggiornaCatalogoInternetArchive()
            MostraMessaggio("Aggiornamento catalogo avviato in background." & vbCrLf & "Puoi controllare lo stato dal Centro di Controllo.")
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.aggiornacatalogo: " & ex.ToString())
        End Try
    End Sub

    Private Function CostruisciDizionarioRisultati() As Dictionary(Of String, String)
        Dim dict As New Dictionary(Of String, String)
        Try
            If _risultatiCorrenti Is Nothing Then Return dict
            dict = New Dictionary(Of String, String)(_risultatiCorrenti.Count)
            For indice As Integer = 0 To _risultatiCorrenti.Count - 1
                Dim el As ElementoArchivio = _risultatiCorrenti(indice)
                Dim tipoIndicatore As String = If(el.EAudio, "audio", "testo")
                Dim etichetta As String = el.Titolo & " - " & el.Autore & " (" & tipoIndicatore & ")"
                Dim chiave As String = indice.ToString()
                If Not dict.ContainsKey(etichetta) Then
                    dict.Add(etichetta, chiave)
                Else
                    dict.Add(etichetta & " [" & chiave & "]", chiave)
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.CostruisciDizionarioRisultati: " & ex.ToString())
        End Try
        Return dict
    End Function

    Private Sub MostraRisultati(titolo As String)
        Try
            ChiudiListaRisultati()

            listaRisultati = New WGMenuLista()
            listaRisultati.ciclono = True
            listaRisultati.selezioneMenu = True
            listaRisultati.multiselezioneConsentita = False

            Dim dict As Dictionary(Of String, String) = CostruisciDizionarioRisultati()
            listaRisultati.CaricaElementiMassivi(dict)

            AddHandler listaRisultati.ElementoSelezionato, AddressOf elemento_selezionato
            AddHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
            AddHandler listaRisultati.finelista, AddressOf listaRisultati_finelista
            AddHandler listaRisultati.iniziolista, AddressOf listaRisultati_iniziolista

            listaRisultati.impostaTitolo(titolo)
            listaRisultati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.MostraRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub RiempiListaRisultati()
        If listaRisultati Is Nothing OrElse _risultatiCorrenti Is Nothing Then Return
        Try
            listaRisultati.BeginUpdate()
            listaRisultati.svuotaLista()
            Dim dict As Dictionary(Of String, String) = CostruisciDizionarioRisultati()
            listaRisultati.CaricaElementiMassivi(dict)
            listaRisultati.EndUpdate()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.RiempiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub listaRisultati_finelista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse Not _haAltrePagine Then Return

            Dim paginaPrecedente As Integer = _paginaCorrente
            _paginaCorrente += 1

            If CaricaPaginaCorrente() Then
                RiempiListaRisultati()
                listaRisultati.impostaTitolo(OttieniTitoloBase())
                listaRisultati.selezionaElemento(0)
            Else
                _paginaCorrente = paginaPrecedente
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.listaRisultati_finelista: " & ex.ToString())
        End Try
    End Sub

    Private Sub listaRisultati_iniziolista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse _paginaCorrente <= 0 Then Return

            _paginaCorrente -= 1

            If CaricaPaginaCorrente() Then
                RiempiListaRisultati()
                listaRisultati.impostaTitolo(OttieniTitoloBase())
                listaRisultati.SelezionaUltimoElemento()
            Else
                _paginaCorrente += 1
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.listaRisultati_iniziolista: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniTitoloBase() As String
        Select Case _filtroTipo
            Case "autore" : Return "Elementi di " & _filtroValore
            Case "genere" : Return "Elementi del genere " & _filtroValore
            Case "supporto" : Return "Elementi di tipo " & _filtroValore
            Case "ricerca" : Return "Risultati ricerca"
            Case Else : Return "Risultati"
        End Select
    End Function

    Private Sub ChiudiListaRisultati()
        Try
            If listaRisultati IsNot Nothing Then
                RemoveHandler listaRisultati.ElementoSelezionato, AddressOf elemento_selezionato
                RemoveHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
                RemoveHandler listaRisultati.finelista, AddressOf listaRisultati_finelista
                RemoveHandler listaRisultati.iniziolista, AddressOf listaRisultati_iniziolista
                listaRisultati.abbandona()
                listaRisultati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub elemento_selezionato(id As String)
        Try
            Dim indice As Integer
            If Not Integer.TryParse(id, indice) Then Return
            If _risultatiCorrenti Is Nothing OrElse indice < 0 OrElse indice >= _risultatiCorrenti.Count Then Return

            Dim elemento As ElementoArchivio = _risultatiCorrenti(indice)
            If elemento Is Nothing Then Return

            Dim voce As New WGCentroDiControllo.ElementoDaScaricareArchivioInternet
            voce.Identificatore = elemento.Identificatore
            voce.Titolo = elemento.Titolo
            voce.Autore = elemento.Autore
            voce.EAudio = elemento.EAudio

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then
                centro.ScaricaLibriArchivioInternet(New List(Of WGCentroDiControllo.ElementoDaScaricareArchivioInternet) From {voce}, CLSInternetArchive.PercorsoAudioLibri, CLSInternetArchive.PercorsoTesti)
                MostraMessaggio("Download avviato in background." & vbCrLf & "Troverai l'elemento completo in ""Libri scaricati"" quando sara' pronto." & vbCrLf & "Puoi controllare lo stato anche dal Centro di Controllo.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.elemento_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub CampoDocumento_Chiusura()
        Try
            _campoDocumento = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.CampoDocumento_Chiusura: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiCampoDocumento()
        Try
            If _campoDocumento IsNot Nothing Then
                RemoveHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
                _campoDocumento.chiudi()
                _campoDocumento = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiCampoDocumento: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraSelezioneCapitoliDaPercorsi(percorsiFile As List(Of String), titoloLibro As String)
        Try
            If percorsiFile Is Nothing OrElse percorsiFile.Count = 0 Then
                MostraMessaggio("Non ho trovato file audio in questo audiolibro.")
                Return
            End If

            If percorsiFile.Count = 1 Then
                AvviaAscoltoConScelta(percorsiFile, titoloLibro)
                Return
            End If

            ChiudiListaCapitoli()

            _capitoliPercorsiCorrenti = percorsiFile.ToArray()
            _capitoliLibroTitolo = titoloLibro

            listaCapitoli = New WGMenuLista()
            listaCapitoli.ciclono = True
            listaCapitoli.selezioneMenu = True
            listaCapitoli.multiselezioneConsentita = True

            Dim dict As New Dictionary(Of String, String)(_capitoliPercorsiCorrenti.Length)
            For indice As Integer = 0 To _capitoliPercorsiCorrenti.Length - 1
                Dim nomeCapitolo As String = Path.GetFileNameWithoutExtension(_capitoliPercorsiCorrenti(indice))
                Dim chiave As String = indice.ToString()
                If Not dict.ContainsKey(nomeCapitolo) Then
                    dict.Add(nomeCapitolo, chiave)
                Else
                    dict.Add(nomeCapitolo & " [" & chiave & "]", chiave)
                End If
            Next
            listaCapitoli.CaricaElementiMassivi(dict)

            AddHandler listaCapitoli.ElementiSelezionati, AddressOf CapitoliSelezionati
            AddHandler listaCapitoli.escPremuto, AddressOf ChiudiListaCapitoli

            listaCapitoli.impostaTitolo("Seleziona i capitoli di " & titoloLibro)
            listaCapitoli.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.MostraSelezioneCapitoliDaPercorsi: " & ex.ToString())
        End Try
    End Sub

    Private Sub CapitoliSelezionati(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return

            Dim percorsi As New List(Of String)
            For Each idTesto As String In ids
                Dim indice As Integer
                If Integer.TryParse(idTesto, indice) AndAlso _capitoliPercorsiCorrenti IsNot Nothing AndAlso indice >= 0 AndAlso indice < _capitoliPercorsiCorrenti.Length Then
                    percorsi.Add(_capitoliPercorsiCorrenti(indice))
                End If
            Next

            Dim titoloLibro As String = _capitoliLibroTitolo
            ChiudiListaCapitoli()

            If percorsi.Count = 0 Then Return
            AvviaAscoltoConScelta(percorsi, titoloLibro)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.CapitoliSelezionati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaCapitoli()
        Try
            If listaCapitoli IsNot Nothing Then
                RemoveHandler listaCapitoli.ElementiSelezionati, AddressOf CapitoliSelezionati
                RemoveHandler listaCapitoli.escPremuto, AddressOf ChiudiListaCapitoli
                listaCapitoli.abbandona()
                listaCapitoli = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ChiudiListaCapitoli: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaAscoltoConScelta(percorsi As List(Of String), titolo As String)
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return

            Dim modalita As String = ""
            Try
                modalita = _config.LeggiValore(CHIAVE_CONFIG_MODALITA_ASCOLTO)
            Catch exConfig As Exception
                _logger.ScriviLog("MenuInternetArchive.AvviaAscoltoConScelta (lettura config): " & exConfig.ToString())
            End Try

            If String.Equals(modalita, "player", StringComparison.OrdinalIgnoreCase) Then
                AvviaLettoreMultimediale(percorsi, titolo)
            ElseIf String.Equals(modalita, "sottofondo", StringComparison.OrdinalIgnoreCase) Then
                AvviaAscoltoBackground(percorsi)
            Else
                _percorsiAscoltoInSospeso = percorsi
                _titoloAscoltoInSospeso = titolo

                Dim menuScelta As New WGMenu()
                menuScelta.aggiungi("Apri lettore multimediale", AddressOf ApriLettoreMultimediale)
                menuScelta.aggiungi("Ascolta in background", AddressOf ApriAscoltoBackground)
                menuScelta.avvia("Scelta della modalita' di ascolto", True)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.AvviaAscoltoConScelta: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriLettoreMultimediale()
        Try
            AvviaLettoreMultimediale(_percorsiAscoltoInSospeso, _titoloAscoltoInSospeso)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ApriLettoreMultimediale: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriAscoltoBackground()
        Try
            AvviaAscoltoBackground(_percorsiAscoltoInSospeso)
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.ApriAscoltoBackground: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaLettoreMultimediale(percorsi As List(Of String), titolo As String)
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return
            Dim player As New WGPlayerFormInt()
            For Each percorso As String In percorsi
                player.AggiungiElemento(percorso, ElementType.File)
            Next
            player.avvia(If(String.IsNullOrWhiteSpace(titolo), "Audiolibro", titolo))
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.AvviaLettoreMultimediale: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaAscoltoBackground(percorsi As List(Of String))
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then
                centro.RiproduciFileAudio(percorsi)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.AvviaAscoltoBackground: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuInternetArchive.MostraMessaggio: " & ex.ToString())
        End Try
    End Sub

End Class