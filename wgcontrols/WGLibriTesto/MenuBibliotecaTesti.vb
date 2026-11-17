Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

Public Class MenuBibliotecaTesti

    Private Const PAGE_SIZE As Integer = 120

    Private Menu As WGMenu
    Private camporicerca As WGCampoTesto
    Private listaRisultati As WGMenuLista
    Private listaAutori As WGMenuLista
    Private listaGeneri As WGMenuLista
    Private _campoDocumento As WGCampo
    Private _processo As WGProcessoInCorso

    Private ReadOnly _biblioteca As New CLSBibliotecaTesti
    Private ReadOnly _logger As New Logger()
    Private ReadOnly _config As New WGConfig()

    Private Const CHIAVE_CONFIG_RICERCHE_RECENTI As String = "bibliotecatesti_ricerche_recenti"
    Private Const MAX_RICERCHE_RECENTI As Integer = 10
    Private listaRicercheRecenti As WGMenuLista
    Private listaCategorie As WGMenuLista

    Private _risultatiCorrenti As List(Of LibroTesto)
    Private _queryCorrente As String

    Private _filtroTipo As String = ""
    Private _filtroValore As String = ""
    Private _paginaCorrente As Integer = 0
    Private _caricamentoInCorso As Boolean = False
    Private _haAltrePagine As Boolean = False
    Private _cursoriTitolo As New List(Of String)
    Private _cursoriId As New List(Of Long)

    Private listaLibriScaricati As WGMenuLista
    Private listaCapitoliScaricati As WGMenuLista
    Private ReadOnly _libriScaricatiPerId As New Dictionary(Of String, WGCentroDiControllo.LibroTestoScaricato)
    Private _capitoliScaricatiCorrenti As List(Of WGCentroDiControllo.CapitoloTestoScaricato)
    Private _libroScaricatoInLettura As WGCentroDiControllo.LibroTestoScaricato
    Private _domandaEliminaLibro As WGDomanda
    Private _idLibroDaEliminare As String = ""

    Public Sub avvia()
        Try
            If Menu Is Nothing Then
                Menu = New WGMenu()
            End If
            Menu.aggiungi("Cerca un libro", AddressOf ricercalibro)
            Menu.aggiungi("Ricerche recenti", AddressOf mostraRicercheRecenti)
            Menu.aggiungi("Elenco per Autore", AddressOf mostraelencoper_autore)
            Menu.aggiungi("Elenco per Genere", AddressOf mostraelencoper_genere)
            Menu.aggiungi("Aggiorna catalogo", AddressOf aggiornacatalogo)
            Menu.aggiungi("Scegli categorie da aggiornare", AddressOf sceltaCategorie)
            Menu.aggiungi("Libri scaricati", AddressOf mostraLibriScaricati)
            Menu.avvia("Wikisource")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.avvia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercalibro()
        Try
            camporicerca = New WGCampoTesto()
            camporicerca.init("Scrivi il titolo o l'autore del libro da cercare", "", campoobbligatorio:=True)
            AddHandler camporicerca.chiusura, AddressOf avvia_ricerca
            camporicerca.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ricercalibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub ReimpostaCursori()
        Try
            _cursoriTitolo = New List(Of String) From {Nothing}
            _cursoriId = New List(Of Long) From {0L}
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ReimpostaCursori: " & ex.ToString())
        End Try
    End Sub

    Private Sub avvia_ricerca()
        Try
            Dim query As String = camporicerca.OttieniTesto()
            camporicerca.chiudi()
            If String.IsNullOrWhiteSpace(query) Then Return

            _queryCorrente = query
            _filtroTipo = "ricerca"
            _filtroValore = ""
            _paginaCorrente = 0
            _haAltrePagine = False

            EseguiConAttesa("Ricerca in corso su Wikisource, attendi", New Action(AddressOf OperazioneRicerca))

            If _risultatiCorrenti Is Nothing OrElse _risultatiCorrenti.Count = 0 Then
                MostraMessaggio("Nessun libro trovato con questi termini di ricerca.")
                Return
            End If

            SalvaRicercaRecente(_queryCorrente)
            MostraRisultati("Risultati ricerca, " & _risultatiCorrenti.Count & " libri trovati")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.avvia_ricerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneRicerca()
        Try
            _risultatiCorrenti = _biblioteca.CercaEntrambe(_queryCorrente)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.OperazioneRicerca: " & ex.ToString())
            _risultatiCorrenti = New List(Of LibroTesto)
        End Try
    End Sub

    Private Sub mostraelencoper_autore()
        Try
            Dim autori As List(Of String) = _biblioteca.OttieniAutoriCatalogati()
            If autori Is Nothing OrElse autori.Count = 0 Then
                MostraMessaggio("Il catalogo locale e' vuoto. Usa prima ""Aggiorna catalogo locale"" per scaricare l'elenco dei libri.")
                Return
            End If

            ChiudiListaAutori()

            listaAutori = New WGMenuLista()
            listaAutori.ciclono = True
            listaAutori.selezioneMenu = True
            listaAutori.multiselezioneConsentita = False

            listaAutori.BeginUpdate()
            For Each autore As String In autori
                Try
                    listaAutori.aggiungiElemento(autore, autore)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.mostraelencoper_autore elemento: " & exRiga.Message)
                End Try
            Next
            listaAutori.EndUpdate()

            AddHandler listaAutori.ElementoSelezionato, AddressOf autore_selezionato
            AddHandler listaAutori.escPremuto, AddressOf ChiudiListaAutori

            listaAutori.impostaTitolo("Elenco Autori, " & autori.Count & " autori")
            listaAutori.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.mostraelencoper_autore: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaAutori: " & ex.ToString())
        End Try
    End Sub

    Private Sub autore_selezionato(autore As String)
        Try
            _filtroTipo = "autore"
            _filtroValore = autore
            _paginaCorrente = 0
            ReimpostaCursori()
            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun libro trovato per questo autore.")
                Return
            End If
            MostraRisultati("Libri di " & autore)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.autore_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_genere()
        Try
            Dim generi As List(Of String) = _biblioteca.OttieniGeneriCatalogati()
            If generi Is Nothing OrElse generi.Count = 0 Then
                MostraMessaggio("Il catalogo locale e' vuoto. Usa prima ""Aggiorna catalogo locale"" per scaricare l'elenco dei libri.")
                Return
            End If

            ChiudiListaGeneri()

            listaGeneri = New WGMenuLista()
            listaGeneri.ciclono = True
            listaGeneri.selezioneMenu = True
            listaGeneri.multiselezioneConsentita = False

            listaGeneri.BeginUpdate()
            For Each genere As String In generi
                Try
                    listaGeneri.aggiungiElemento(genere, genere)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.mostraelencoper_genere elemento: " & exRiga.Message)
                End Try
            Next
            listaGeneri.EndUpdate()

            AddHandler listaGeneri.ElementoSelezionato, AddressOf genere_selezionato
            AddHandler listaGeneri.escPremuto, AddressOf ChiudiListaGeneri

            listaGeneri.impostaTitolo("Elenco Generi, " & generi.Count & " generi")
            listaGeneri.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.mostraelencoper_genere: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaGeneri: " & ex.ToString())
        End Try
    End Sub

    Private Sub genere_selezionato(genere As String)
        Try
            _filtroTipo = "genere"
            _filtroValore = genere
            _paginaCorrente = 0
            ReimpostaCursori()
            If Not CaricaPaginaCorrente() Then
                MostraMessaggio("Nessun libro trovato per questo genere.")
                Return
            End If
            MostraRisultati("Libri del genere " & genere)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.genere_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Function CaricaPaginaCorrente() As Boolean
        If _caricamentoInCorso Then Return False
        _caricamentoInCorso = True
        Try
            If _filtroTipo = "ricerca" Then
                _haAltrePagine = False
                Return _risultatiCorrenti IsNot Nothing AndAlso _risultatiCorrenti.Count > 0
            End If

            If _paginaCorrente < 0 OrElse _paginaCorrente >= _cursoriTitolo.Count Then
                _risultatiCorrenti = New List(Of LibroTesto)
                Return False
            End If

            Dim titoloCursore As String = _cursoriTitolo(_paginaCorrente)
            Dim idCursore As Long = _cursoriId(_paginaCorrente)
            Dim titoloUltimo As String = Nothing
            Dim idUltimo As Long = 0

            Select Case _filtroTipo
                Case "autore"
                    _risultatiCorrenti = _biblioteca.OttieniLibriPerAutore(_filtroValore, PAGE_SIZE, titoloCursore, idCursore, titoloUltimo, idUltimo)
                Case "genere"
                    _risultatiCorrenti = _biblioteca.OttieniLibriPerGenere(_filtroValore, PAGE_SIZE, titoloCursore, idCursore, titoloUltimo, idUltimo)
                Case Else
                    _risultatiCorrenti = New List(Of LibroTesto)
            End Select

            If _risultatiCorrenti Is Nothing Then
                _risultatiCorrenti = New List(Of LibroTesto)
            End If

            If _risultatiCorrenti.Count > 0 AndAlso _paginaCorrente + 1 = _cursoriTitolo.Count Then
                _cursoriTitolo.Add(titoloUltimo)
                _cursoriId.Add(idUltimo)
            End If

            _haAltrePagine = (_risultatiCorrenti.Count >= PAGE_SIZE)
            Return _risultatiCorrenti.Count > 0
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.CaricaPaginaCorrente: " & ex.ToString())
            _risultatiCorrenti = New List(Of LibroTesto)
            _haAltrePagine = False
            Return False
        Finally
            _caricamentoInCorso = False
        End Try
    End Function

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
            _logger.ScriviLog("MenuBibliotecaTesti.OttieniRicercheRecenti: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.SalvaRicercaRecente: " & ex.ToString())
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
                    _logger.ScriviLog("MenuBibliotecaTesti.mostraRicercheRecenti elemento: " & exRiga.Message)
                End Try
            Next
            listaRicercheRecenti.EndUpdate()

            AddHandler listaRicercheRecenti.ElementoSelezionato, AddressOf ricercaRecenteSelezionata
            AddHandler listaRicercheRecenti.escPremuto, AddressOf ChiudiListaRicercheRecenti

            listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            listaRicercheRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.mostraRicercheRecenti: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercaRecenteSelezionata(query As String)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return
            ChiudiListaRicercheRecenti()

            _queryCorrente = query
            _filtroTipo = "ricerca"
            _filtroValore = ""
            _paginaCorrente = 0
            _haAltrePagine = False

            EseguiConAttesa("Ricerca in corso su Wikisource, attendi", New Action(AddressOf OperazioneRicerca))

            If _risultatiCorrenti Is Nothing OrElse _risultatiCorrenti.Count = 0 Then
                MostraMessaggio("Nessun libro trovato con questi termini di ricerca.")
                Return
            End If

            SalvaRicercaRecente(_queryCorrente)
            MostraRisultati("Risultati ricerca, " & _risultatiCorrenti.Count & " libri trovati")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ricercaRecenteSelezionata: " & ex.ToString())
        End Try
    End Sub

    Private Sub sceltaCategorie()
        Try
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then Return

            Dim categorie As List(Of String) = centro.OttieniCategorieWikisourceDisponibili()
            If categorie Is Nothing OrElse categorie.Count = 0 Then
                MostraMessaggio("Non ho trovato nessuna categoria disponibile.")
                Return
            End If

            ChiudiListaCategorie()

            listaCategorie = New WGMenuLista()
            listaCategorie.ciclono = True
            listaCategorie.selezioneMenu = True
            listaCategorie.multiselezioneConsentita = True

            listaCategorie.BeginUpdate()
            For Each categoria As String In categorie
                Try
                    listaCategorie.aggiungiElemento(categoria, categoria)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.sceltaCategorie elemento: " & exRiga.Message)
                End Try
            Next
            listaCategorie.EndUpdate()

            AddHandler listaCategorie.ElementiSelezionati, AddressOf categorieSelezionate
            AddHandler listaCategorie.escPremuto, AddressOf ChiudiListaCategorie

            listaCategorie.impostaTitolo("Scegli le categorie da aggiornare")
            listaCategorie.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.sceltaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaCategorie()
        Try
            If listaCategorie IsNot Nothing Then
                RemoveHandler listaCategorie.ElementiSelezionati, AddressOf categorieSelezionate
                RemoveHandler listaCategorie.escPremuto, AddressOf ChiudiListaCategorie
                listaCategorie.abbandona()
                listaCategorie = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub categorieSelezionate(categorie As List(Of String))
        Try
            ChiudiListaCategorie()

            If categorie Is Nothing OrElse categorie.Count = 0 Then
                MostraMessaggio("Non hai selezionato nessuna categoria.")
                Return
            End If

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then
                centro.AggiornaCatalogoTesti(categorie)
                MostraMessaggio("Aggiornamento delle categorie scelte avviato in background." & vbCrLf & "Puoi controllare lo stato dal Centro di Controllo.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.categorieSelezionate: " & ex.ToString())
        End Try
    End Sub

    Private Sub aggiornacatalogo()
        Try
            WGCentroDiControllo.GetInstance().AggiornaCatalogoTesti()
            MostraMessaggio("Aggiornamento catalogo avviato in background." & vbCrLf & "Puoi controllare lo stato dal Centro di Controllo.")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.aggiornacatalogo: " & ex.ToString())
        End Try
    End Sub

    Private Sub RiempiListaRisultati(popolamentoIniziale As Boolean)
        If listaRisultati Is Nothing OrElse _risultatiCorrenti Is Nothing Then Return
        Try
            If Not popolamentoIniziale Then
                listaRisultati.BeginUpdate()
                listaRisultati.svuotaLista()
            End If

            For indice As Integer = 0 To _risultatiCorrenti.Count - 1
                Try
                    Dim libro As LibroTesto = _risultatiCorrenti(indice)
                    Dim nomeFonte As String = "Wikisource"
                    Dim etichetta As String = libro.Titolo & " - " & libro.Autore & " (" & nomeFonte & ")"
                    listaRisultati.aggiungiElemento(etichetta, indice.ToString())
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.RiempiListaRisultati elemento: " & exRiga.Message)
                End Try
            Next

            If Not popolamentoIniziale Then
                listaRisultati.EndUpdate()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.RiempiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraRisultati(titolo As String)
        Try
            ChiudiListaRisultati()

            listaRisultati = New WGMenuLista()
            listaRisultati.ciclono = True
            listaRisultati.selezioneMenu = True
            listaRisultati.multiselezioneConsentita = False

            RiempiListaRisultati(True)

            AddHandler listaRisultati.ElementoSelezionato, AddressOf libro_selezionato
            AddHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
            AddHandler listaRisultati.finelista, AddressOf listaRisultati_finelista
            AddHandler listaRisultati.iniziolista, AddressOf listaRisultati_iniziolista

            listaRisultati.impostaTitolo(titolo)
            listaRisultati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.MostraRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRisultati()
        Try
            If listaRisultati IsNot Nothing Then
                RemoveHandler listaRisultati.ElementoSelezionato, AddressOf libro_selezionato
                RemoveHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
                RemoveHandler listaRisultati.finelista, AddressOf listaRisultati_finelista
                RemoveHandler listaRisultati.iniziolista, AddressOf listaRisultati_iniziolista
                listaRisultati.abbandona()
                listaRisultati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub listaRisultati_finelista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse Not _haAltrePagine Then Return

            Dim paginaPrecedente As Integer = _paginaCorrente
            _paginaCorrente += 1

            If CaricaPaginaCorrente() Then
                RiempiListaRisultati(False)
                listaRisultati.selezionaElemento(0)
            Else
                _paginaCorrente = paginaPrecedente
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.listaRisultati_finelista: " & ex.ToString())
        End Try
    End Sub

    Private Sub listaRisultati_iniziolista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse _paginaCorrente <= 0 Then Return

            _paginaCorrente -= 1

            If CaricaPaginaCorrente() Then
                RiempiListaRisultati(False)
                listaRisultati.SelezionaUltimoElemento()
            Else
                _paginaCorrente += 1
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.listaRisultati_iniziolista: " & ex.ToString())
        End Try
    End Sub

    Private Sub libro_selezionato(id As String)
        Try
            Dim indice As Integer
            If Not Integer.TryParse(id, indice) Then Return
            If _risultatiCorrenti Is Nothing OrElse indice < 0 OrElse indice >= _risultatiCorrenti.Count Then Return

            Dim libro As LibroTesto = _risultatiCorrenti(indice)
            If libro Is Nothing Then Return

            Dim voce As New WGCentroDiControllo.ElementoDaScaricareTesto
            voce.TitoloPaginaWikisource = libro.TitoloPaginaWikisource
            voce.Titolo = libro.Titolo
            voce.Autore = libro.Autore
            voce.Genere = libro.Genere

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then
                centro.ScaricaLibriTesto(New List(Of WGCentroDiControllo.ElementoDaScaricareTesto) From {voce})
                MostraMessaggio("Download avviato in background." & vbCrLf & "Troverai il libro completo in ""Libri scaricati"" quando sara' pronto." & vbCrLf & "Puoi controllare lo stato anche dal Centro di Controllo.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.libro_selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub CampoDocumento_Chiusura()
        Try
            _campoDocumento = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.CampoDocumento_Chiusura: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiCampoDocumento: " & ex.ToString())
        End Try
    End Sub

    Private Sub mostraLibriScaricati()
        Try
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then Return

            Dim libri As List(Of WGCentroDiControllo.LibroTestoScaricato) = centro.OttieniLibriTestoScaricati()
            If libri Is Nothing OrElse libri.Count = 0 Then
                MostraMessaggio("Non hai ancora scaricato nessun libro da Wikisource.")
                Return
            End If

            ChiudiListaLibriScaricati()

            listaLibriScaricati = New WGMenuLista()
            listaLibriScaricati.ciclono = True
            listaLibriScaricati.selezioneMenu = True
            listaLibriScaricati.multiselezioneConsentita = False

            _libriScaricatiPerId.Clear()
            listaLibriScaricati.BeginUpdate()
            For Each libro As WGCentroDiControllo.LibroTestoScaricato In libri
                Try
                    _libriScaricatiPerId(libro.Id) = libro
                    Dim etichetta As String = libro.Titolo & " - " & libro.Autore
                    listaLibriScaricati.aggiungiElemento(etichetta, libro.Id)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.mostraLibriScaricati elemento: " & exRiga.Message)
                End Try
            Next
            listaLibriScaricati.EndUpdate()

            listaLibriScaricati.aggiungiOpzione("Elimina", AddressOf EliminaLibroScaricatoOpzione)

            AddHandler listaLibriScaricati.ElementoSelezionato, AddressOf libroScaricatoSelezionato
            AddHandler listaLibriScaricati.escPremuto, AddressOf ChiudiListaLibriScaricati

            listaLibriScaricati.impostaTitolo("Libri scaricati")
            listaLibriScaricati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.mostraLibriScaricati: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub libroScaricatoSelezionato(id As String)
        Try
            If Not _libriScaricatiPerId.ContainsKey(id) Then Return
            _libroScaricatoInLettura = _libriScaricatiPerId(id)

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then Return

            _capitoliScaricatiCorrenti = centro.OttieniCapitoliTestoScaricati(_libroScaricatoInLettura.Id)
            If _capitoliScaricatiCorrenti Is Nothing OrElse _capitoliScaricatiCorrenti.Count = 0 Then
                MostraMessaggio("Non ho trovato il testo di questo libro.")
                Return
            End If

            If _capitoliScaricatiCorrenti.Count = 1 Then
                MostraTestoCompletoScaricato()
                Return
            End If

            Dim menuModalita As New WGMenu()
            menuModalita.aggiungi("Leggi tutto", AddressOf MostraTestoCompletoScaricato)
            menuModalita.aggiungi("Leggi per capitoli", AddressOf MostraIndiceCapitoliScaricati)
            menuModalita.avvia("Scegli come leggere " & _libroScaricatoInLettura.Titolo, True)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.libroScaricatoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraTestoCompletoScaricato()
        Try
            If _capitoliScaricatiCorrenti Is Nothing OrElse _capitoliScaricatiCorrenti.Count = 0 Then Return

            Dim testoCompleto As New StringBuilder()
            For Each capitolo As WGCentroDiControllo.CapitoloTestoScaricato In _capitoliScaricatiCorrenti
                If testoCompleto.Length > 0 Then testoCompleto.Append(Environment.NewLine & Environment.NewLine)
                testoCompleto.Append(capitolo.Testo)
            Next

            ChiudiCampoDocumento()

            Dim titolo As String = "Libro"
            If _libroScaricatoInLettura IsNot Nothing Then
                titolo = _libroScaricatoInLettura.Titolo
                If Not String.IsNullOrWhiteSpace(_libroScaricatoInLettura.Autore) Then
                    titolo &= " di " & _libroScaricatoInLettura.Autore
                End If
            End If

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init(titolo, testoCompleto.ToString(), True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.MostraTestoCompletoScaricato: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il libro in lettura.")
        End Try
    End Sub

    Private Sub MostraIndiceCapitoliScaricati()
        Try
            If _capitoliScaricatiCorrenti Is Nothing OrElse _capitoliScaricatiCorrenti.Count = 0 Then Return

            ChiudiListaCapitoliScaricati()

            listaCapitoliScaricati = New WGMenuLista()
            listaCapitoliScaricati.ciclono = True
            listaCapitoliScaricati.selezioneMenu = True
            listaCapitoliScaricati.multiselezioneConsentita = False

            listaCapitoliScaricati.BeginUpdate()
            For indice As Integer = 0 To _capitoliScaricatiCorrenti.Count - 1
                Try
                    listaCapitoliScaricati.aggiungiElemento(_capitoliScaricatiCorrenti(indice).TitoloCapitolo, indice.ToString())
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.MostraIndiceCapitoliScaricati elemento: " & exRiga.Message)
                End Try
            Next
            listaCapitoliScaricati.EndUpdate()

            AddHandler listaCapitoliScaricati.ElementoSelezionato, AddressOf capitoloScaricatoSelezionato
            AddHandler listaCapitoliScaricati.escPremuto, AddressOf ChiudiListaCapitoliScaricati

            Dim titoloLibro As String = ""
            If _libroScaricatoInLettura IsNot Nothing Then titoloLibro = _libroScaricatoInLettura.Titolo
            listaCapitoliScaricati.impostaTitolo("Indice di " & titoloLibro)
            listaCapitoliScaricati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.MostraIndiceCapitoliScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaCapitoliScaricati()
        Try
            If listaCapitoliScaricati IsNot Nothing Then
                RemoveHandler listaCapitoliScaricati.ElementoSelezionato, AddressOf capitoloScaricatoSelezionato
                RemoveHandler listaCapitoliScaricati.escPremuto, AddressOf ChiudiListaCapitoliScaricati
                listaCapitoliScaricati.abbandona()
                listaCapitoliScaricati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiListaCapitoliScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub capitoloScaricatoSelezionato(id As String)
        Try
            Dim indice As Integer
            If Not Integer.TryParse(id, indice) Then Return
            If _capitoliScaricatiCorrenti Is Nothing OrElse indice < 0 OrElse indice >= _capitoliScaricatiCorrenti.Count Then Return

            Dim capitolo As WGCentroDiControllo.CapitoloTestoScaricato = _capitoliScaricatiCorrenti(indice)

            ChiudiCampoDocumento()

            Dim titolo As String = capitolo.TitoloCapitolo
            If _libroScaricatoInLettura IsNot Nothing Then
                titolo = _libroScaricatoInLettura.Titolo & " - " & titolo
            End If

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init(titolo, capitolo.Testo, True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.capitoloScaricatoSelezionato: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il capitolo in lettura.")
        End Try
    End Sub

    Private Sub EliminaLibroScaricatoOpzione(sender As Object, e As EventArgs)
        Try
            If listaLibriScaricati Is Nothing Then Return

            Dim elementi As List(Of String) = listaLibriScaricati.OttieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            _idLibroDaEliminare = elementi(0)

            ChiudiDomandaEliminaLibro()
            _domandaEliminaLibro = New WGDomanda()
            AddHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibroScaricato
            _domandaEliminaLibro.avvia("Vuoi eliminare definitivamente questo libro scaricato? L'operazione non e' reversibile.")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.EliminaLibroScaricatoOpzione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ConfermaEliminaLibroScaricato(risposta As DialogResult)
        Try
            ChiudiDomandaEliminaLibro()

            If risposta = DialogResult.Yes AndAlso Not String.IsNullOrWhiteSpace(_idLibroDaEliminare) Then
                Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
                Dim eliminato As Boolean = False
                If centro IsNot Nothing Then
                    eliminato = centro.EliminaLibroTestoScaricato(_idLibroDaEliminare)
                End If

                If eliminato Then
                    MostraMessaggio("Libro eliminato.")
                    mostraLibriScaricati()
                Else
                    MostraMessaggio("Non e' stato possibile eliminare il libro.")
                End If
            End If

            _idLibroDaEliminare = ""
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ConfermaEliminaLibroScaricato: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiDomandaEliminaLibro()
        Try
            If _domandaEliminaLibro IsNot Nothing Then
                RemoveHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibroScaricato
                _domandaEliminaLibro = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.ChiudiDomandaEliminaLibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.MostraMessaggio: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiConAttesa(messaggio As String, operazione As Action)
        Try
            If operazione Is Nothing Then Return

            _processo = New WGProcessoInCorso()
            _processo.Avvia(messaggio, False)

            Dim attivita As Task = Task.Run(operazione)

            While Not attivita.IsCompleted
                Try
                    Application.DoEvents()
                    Thread.Sleep(30)
                Catch exAttesa As Exception
                    _logger.ScriviLog("MenuBibliotecaTesti.EseguiConAttesa ciclo: " & exAttesa.Message)
                End Try
            End While

            If attivita.IsFaulted AndAlso attivita.Exception IsNot Nothing Then
            End If

            Try
                _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaTesti.EseguiConAttesa: " & ex.ToString())
            Try
                If _processo IsNot Nothing Then _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        End Try
    End Sub

End Class