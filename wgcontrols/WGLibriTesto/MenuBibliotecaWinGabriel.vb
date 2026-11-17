Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

Public Class MenuBibliotecaWinGabriel

    Private Menu As WGMenu
    Private _campiRicerca As Campi
    Private listaRisultati As WGMenuLista
    Private listaRicercheRecenti As WGMenuLista
    Private listaLibriScaricati As WGMenuLista
    Private _campoDocumento As WGCampo
    Private _processo As WGProcessoInCorso
    Private _domandaEliminaLibro As WGDomanda

    Private ReadOnly _biblioteca As New CLSBibliotecaWinGabriel
    Private ReadOnly _logger As New Logger()
    Private ReadOnly _config As New WGConfig()

    Private Const CHIAVE_CONFIG_RICERCHE_RECENTI As String = "bibliotecawingabriel_ricerche_recenti"
    Private Const MAX_RICERCHE_RECENTI As Integer = 10

    Private _risultatoCorrente As RisultatoRicercaWinGabriel
    Private _queryCorrente As String = ""
    Private _campoRicercaCorrente As String = "tutto"
    Private _paginaCorrente As Integer = 1
    Private _caricamentoInCorso As Boolean = False
    Private _libroCorrente As LibroWinGabriel
    Private _idLibroDaEliminare As Integer = 0
    Private ReadOnly _libriScaricatiPerId As New Dictionary(Of Integer, LibroWinGabriel)

    Public Sub avvia()
        Try
            If Menu Is Nothing Then
                Menu = New WGMenu()
            End If
            Menu.aggiungi("Cerca un libro", AddressOf ApriRicerca)
            Menu.aggiungi("Ricerche recenti", AddressOf MostraRicercheRecenti)
            Menu.aggiungi("Informazioni sulla biblioteca", AddressOf MostraInfoBiblioteca)
            Menu.aggiungi("Libri scaricati", AddressOf MostraLibriScaricati)
            Menu.avvia("Biblioteca di WinGabriel")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.avvia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriRicerca()
        Try
            LiberaCampiRicerca()
            _campiRicerca = New Campi()
            _campiRicerca.Aggiungi("Testo da cercare", "q", True, False, "")
            AddHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            AddHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ApriRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            Dim query As String = ""
            If _campiRicerca IsNot Nothing Then query = _campiRicerca.RecuperaValore("q")
            LiberaCampiRicerca()
            If String.IsNullOrWhiteSpace(query) Then Return
            _queryCorrente = query.Trim()
            MostraSceltaCampoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.RicercaConfermata: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaAnnullata(sender As Object, e As EventArgs)
        Try
            LiberaCampiRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.RicercaAnnullata: " & ex.ToString())
        End Try
    End Sub

    Private Sub LiberaCampiRicerca()
        Try
            If _campiRicerca Is Nothing Then Return
            RemoveHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            RemoveHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.LiberaCampiRicerca: " & ex.ToString())
            _campiRicerca = Nothing
        End Try
    End Sub

    Private Sub MostraSceltaCampoRicerca()
        Try
            Dim menuCampo As New WGMenu()
            menuCampo.aggiungi("Cerca in tutto", AddressOf CercaInTutto)
            menuCampo.aggiungi("Cerca nel titolo", AddressOf CercaNelTitolo)
            menuCampo.aggiungi("Cerca nell'autore", AddressOf CercaNellAutore)
            menuCampo.avvia("Dove cercare", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraSceltaCampoRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub CercaInTutto()
        AvviaRicercaOnline("tutto")
    End Sub

    Private Sub CercaNelTitolo()
        AvviaRicercaOnline("titolo")
    End Sub

    Private Sub CercaNellAutore()
        AvviaRicercaOnline("autore")
    End Sub

    Private Sub AvviaRicercaOnline(campo As String)
        Try
            If String.IsNullOrWhiteSpace(_queryCorrente) Then Return
            _campoRicercaCorrente = CLSBibliotecaWinGabriel.NormalizzaCampo(campo)
            _paginaCorrente = 1
            If Not CaricaPaginaCorrente() Then
                If _risultatoCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_risultatoCorrente.Errore) Then
                    MostraMessaggio(_risultatoCorrente.Errore)
                Else
                    MostraMessaggio("Nessun libro trovato con questi termini di ricerca.")
                End If
                Return
            End If
            SalvaRicercaRecente(_queryCorrente)
            MostraRisultati()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.AvviaRicercaOnline: " & ex.ToString())
        End Try
    End Sub

    Private Function CaricaPaginaCorrente() As Boolean
        If _caricamentoInCorso Then Return False
        _caricamentoInCorso = True
        Try
            EseguiConAttesa("Ricerca in corso nella biblioteca di WinGabriel, attendi", AddressOf OperazioneRicerca)
            If _risultatoCorrente Is Nothing Then Return False
            If Not String.IsNullOrWhiteSpace(_risultatoCorrente.Errore) Then Return False
            Return _risultatoCorrente.Libri IsNot Nothing AndAlso _risultatoCorrente.Libri.Count > 0
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.CaricaPaginaCorrente: " & ex.ToString())
            Return False
        Finally
            _caricamentoInCorso = False
        End Try
    End Function

    Private Sub OperazioneRicerca()
        Try
            _risultatoCorrente = _biblioteca.Cerca(_queryCorrente, _campoRicercaCorrente, _paginaCorrente)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.OperazioneRicerca: " & ex.ToString())
            _risultatoCorrente = New RisultatoRicercaWinGabriel()
            _risultatoCorrente.Errore = "La ricerca non e' andata a buon fine."
        End Try
    End Sub

    Private Sub MostraRisultati()
        Try
            ChiudiListaRisultati()
            If _risultatoCorrente Is Nothing OrElse _risultatoCorrente.Libri Is Nothing Then Return

            listaRisultati = New WGMenuLista()
            listaRisultati.ciclono = True
            listaRisultati.selezioneMenu = True
            listaRisultati.multiselezioneConsentita = False

            RiempiListaRisultati(True)

            AddHandler listaRisultati.ElementoSelezionato, AddressOf LibroSelezionato
            AddHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
            AddHandler listaRisultati.finelista, AddressOf ListaRisultati_FineLista
            AddHandler listaRisultati.iniziolista, AddressOf ListaRisultati_InizioLista

            listaRisultati.impostaTitolo(ComponiTitoloRisultati())
            listaRisultati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Function ComponiTitoloRisultati() As String
        Try
            If _risultatoCorrente Is Nothing Then Return "Risultati ricerca"
            Dim titolo As String = "Risultati ricerca, " & _risultatoCorrente.Libri.Count.ToString() & " libri"
            If _risultatoCorrente.Totale > 0 Then
                titolo = "Risultati ricerca, " & _risultatoCorrente.Totale.ToString() & " libri"
            End If
            If _risultatoCorrente.Pagine > 1 Then
                titolo &= ", pagina " & _risultatoCorrente.Pagina.ToString() & " di " & _risultatoCorrente.Pagine.ToString()
            End If
            Return titolo
        Catch
            Return "Risultati ricerca"
        End Try
    End Function

    Private Sub RiempiListaRisultati(popolamentoIniziale As Boolean)
        If listaRisultati Is Nothing OrElse _risultatoCorrente Is Nothing OrElse _risultatoCorrente.Libri Is Nothing Then Return
        Try
            If Not popolamentoIniziale Then
                listaRisultati.BeginUpdate()
                listaRisultati.svuotaLista()
            End If
            For Each libro As LibroWinGabriel In _risultatoCorrente.Libri
                Try
                    listaRisultati.aggiungiElemento(libro.EtichettaElenco(), libro.Id.ToString())
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaWinGabriel.RiempiListaRisultati elemento: " & exRiga.Message)
                End Try
            Next
            If Not popolamentoIniziale Then
                listaRisultati.EndUpdate()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.RiempiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRisultati()
        Try
            If listaRisultati IsNot Nothing Then
                RemoveHandler listaRisultati.ElementoSelezionato, AddressOf LibroSelezionato
                RemoveHandler listaRisultati.escPremuto, AddressOf ChiudiListaRisultati
                RemoveHandler listaRisultati.finelista, AddressOf ListaRisultati_FineLista
                RemoveHandler listaRisultati.iniziolista, AddressOf ListaRisultati_InizioLista
                listaRisultati.abbandona()
                listaRisultati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ChiudiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ListaRisultati_FineLista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse _risultatoCorrente Is Nothing Then Return
            If _paginaCorrente >= _risultatoCorrente.Pagine Then Return
            Dim paginaPrecedente As Integer = _paginaCorrente
            _paginaCorrente += 1
            If CaricaPaginaCorrente() Then
                RiempiListaRisultati(False)
                If listaRisultati IsNot Nothing Then listaRisultati.impostaTitolo(ComponiTitoloRisultati())
                If listaRisultati IsNot Nothing Then listaRisultati.selezionaElemento(0)
            Else
                _paginaCorrente = paginaPrecedente
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ListaRisultati_FineLista: " & ex.ToString())
        End Try
    End Sub

    Private Sub ListaRisultati_InizioLista(sender As Object, e As EventArgs)
        Try
            If _caricamentoInCorso OrElse _paginaCorrente <= 1 Then Return
            _paginaCorrente -= 1
            If CaricaPaginaCorrente() Then
                RiempiListaRisultati(False)
                If listaRisultati IsNot Nothing Then listaRisultati.impostaTitolo(ComponiTitoloRisultati())
                If listaRisultati IsNot Nothing Then listaRisultati.SelezionaUltimoElemento()
            Else
                _paginaCorrente += 1
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ListaRisultati_InizioLista: " & ex.ToString())
        End Try
    End Sub

    Private Sub LibroSelezionato(id As String)
        Try
            Dim idLibro As Integer
            If Not Integer.TryParse(id, idLibro) Then Return
            _libroCorrente = TrovaLibroNeiRisultati(idLibro)
            If _libroCorrente Is Nothing Then Return

            Dim menuLibro As New WGMenu()
            menuLibro.aggiungi("Leggi", AddressOf LeggiLibroCorrente)
            menuLibro.aggiungi("Scarica", AddressOf ScaricaLibroCorrente)
            menuLibro.aggiungi("Scheda del libro", AddressOf MostraSchedaLibroCorrente)
            If Not String.IsNullOrWhiteSpace(_libroCorrente.UrlScheda) Then
                menuLibro.aggiungi("Apri la scheda nel browser", AddressOf ApriSchedaNelBrowser)
            End If
            menuLibro.avvia(_libroCorrente.EtichettaElenco(), True)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.LibroSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Function TrovaLibroNeiRisultati(idLibro As Integer) As LibroWinGabriel
        Try
            If _risultatoCorrente Is Nothing OrElse _risultatoCorrente.Libri Is Nothing Then Return Nothing
            For Each libro As LibroWinGabriel In _risultatoCorrente.Libri
                If libro IsNot Nothing AndAlso libro.Id = idLibro Then Return libro
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.TrovaLibroNeiRisultati: " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Private Sub LeggiLibroCorrente()
        Try
            If _libroCorrente Is Nothing Then Return
            Dim percorsoPdf As String = ""
            Dim testo As String = ""
            EseguiConAttesa("Preparazione del libro in corso, attendi", Sub()
                                                                            testo = _biblioteca.OttieniTestoLibro(_libroCorrente, percorsoPdf)
                                                                        End Sub)
            If Not String.IsNullOrWhiteSpace(percorsoPdf) Then
                ApriPdfInCampo(percorsoPdf, _libroCorrente.EtichettaElenco())
                Return
            End If
            If String.IsNullOrWhiteSpace(testo) Then
                MostraMessaggio("Non riesco a leggere questo libro.")
                Return
            End If
            MostraTestoInCampo(_libroCorrente.EtichettaElenco(), testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.LeggiLibroCorrente: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il libro in lettura.")
        End Try
    End Sub

    Private Sub ScaricaLibroCorrente()
        Try
            If _libroCorrente Is Nothing Then Return
            Dim riuscito As Boolean = False
            EseguiConAttesa("Download del libro in corso, attendi", Sub()
                                                                        riuscito = _biblioteca.ScaricaESalva(_libroCorrente)
                                                                    End Sub)
            If riuscito Then
                MostraMessaggio("Libro scaricato. Lo trovi in ""Libri scaricati"".")
            Else
                MostraMessaggio("Il download del libro non e' andato a buon fine.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ScaricaLibroCorrente: " & ex.ToString())
            MostraMessaggio("Il download del libro non e' andato a buon fine.")
        End Try
    End Sub

    Private Sub MostraSchedaLibroCorrente()
        Try
            If _libroCorrente Is Nothing Then Return
            Dim dettagli As LibroWinGabriel = Nothing
            EseguiConAttesa("Caricamento della scheda in corso, attendi", Sub()
                                                                              dettagli = _biblioteca.OttieniLibro(_libroCorrente.Id)
                                                                          End Sub)
            If dettagli Is Nothing Then dettagli = _libroCorrente
            Dim scheda As String = _biblioteca.ComponiScheda(dettagli)
            If String.IsNullOrWhiteSpace(scheda) Then
                MostraMessaggio("La scheda di questo libro non e' disponibile.")
                Return
            End If
            MostraTestoInCampo("Scheda di " & dettagli.EtichettaElenco(), scheda)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraSchedaLibroCorrente: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire la scheda del libro.")
        End Try
    End Sub

    Private Sub ApriSchedaNelBrowser()
        Try
            If _libroCorrente Is Nothing OrElse String.IsNullOrWhiteSpace(_libroCorrente.UrlScheda) Then Return
            WGImpostazioniComuni.ApriLinkConBrowserPreferito(_libroCorrente.UrlScheda)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ApriSchedaNelBrowser: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire la scheda nel browser.")
        End Try
    End Sub

    Private Sub MostraInfoBiblioteca()
        Try
            Dim info As InfoBibliotecaWinGabriel = Nothing
            EseguiConAttesa("Lettura delle informazioni della biblioteca, attendi", Sub()
                                                                                         info = _biblioteca.OttieniInfo()
                                                                                     End Sub)
            If info Is Nothing Then
                MostraMessaggio("Non riesco a leggere le informazioni della biblioteca.")
                Return
            End If
            If Not String.IsNullOrWhiteSpace(info.Errore) Then
                MostraMessaggio(info.Errore)
                Return
            End If
            Dim testo As String = info.Nome
            If info.NumeroLibri > 0 Then
                testo &= Environment.NewLine & "Libri disponibili: " & info.NumeroLibri.ToString()
            End If
            testo &= Environment.NewLine & "Puoi cercare per titolo, autore o in tutto il catalogo, leggere i libri e scaricarli per consultarli anche dopo."
            MostraTestoInCampo(info.Nome, testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraInfoBiblioteca: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraRicercheRecenti()
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
                    _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraRicercheRecenti elemento: " & exRiga.Message)
                End Try
            Next
            listaRicercheRecenti.EndUpdate()

            AddHandler listaRicercheRecenti.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
            AddHandler listaRicercheRecenti.escPremuto, AddressOf ChiudiListaRicercheRecenti
            listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            listaRicercheRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRicercheRecenti()
        Try
            If listaRicercheRecenti IsNot Nothing Then
                RemoveHandler listaRicercheRecenti.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
                RemoveHandler listaRicercheRecenti.escPremuto, AddressOf ChiudiListaRicercheRecenti
                listaRicercheRecenti.abbandona()
                listaRicercheRecenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ChiudiListaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaRecenteSelezionata(query As String)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return
            ChiudiListaRicercheRecenti()
            _queryCorrente = query
            MostraSceltaCampoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.RicercaRecenteSelezionata: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaWinGabriel.OttieniRicercheRecenti: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaWinGabriel.SalvaRicercaRecente: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraLibriScaricati()
        Try
            Dim libri As List(Of LibroWinGabriel) = _biblioteca.OttieniLibriScaricati()
            If libri Is Nothing OrElse libri.Count = 0 Then
                MostraMessaggio("Non hai ancora scaricato nessun libro dalla biblioteca di WinGabriel.")
                Return
            End If

            ChiudiListaLibriScaricati()
            listaLibriScaricati = New WGMenuLista()
            listaLibriScaricati.ciclono = True
            listaLibriScaricati.selezioneMenu = True
            listaLibriScaricati.multiselezioneConsentita = False

            _libriScaricatiPerId.Clear()
            listaLibriScaricati.BeginUpdate()
            For Each libro As LibroWinGabriel In libri
                Try
                    _libriScaricatiPerId(libro.Id) = libro
                    listaLibriScaricati.aggiungiElemento(libro.EtichettaElenco(), libro.Id.ToString())
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraLibriScaricati elemento: " & exRiga.Message)
                End Try
            Next
            listaLibriScaricati.EndUpdate()

            listaLibriScaricati.aggiungiOpzione("Elimina", AddressOf EliminaLibroScaricatoOpzione)
            AddHandler listaLibriScaricati.ElementoSelezionato, AddressOf LibroScaricatoSelezionato
            AddHandler listaLibriScaricati.escPremuto, AddressOf ChiudiListaLibriScaricati
            listaLibriScaricati.impostaTitolo("Libri scaricati")
            listaLibriScaricati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaLibriScaricati()
        Try
            If listaLibriScaricati IsNot Nothing Then
                RemoveHandler listaLibriScaricati.ElementoSelezionato, AddressOf LibroScaricatoSelezionato
                RemoveHandler listaLibriScaricati.escPremuto, AddressOf ChiudiListaLibriScaricati
                listaLibriScaricati.abbandona()
                listaLibriScaricati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ChiudiListaLibriScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub LibroScaricatoSelezionato(id As String)
        Try
            Dim idLibro As Integer
            If Not Integer.TryParse(id, idLibro) Then Return
            If Not _libriScaricatiPerId.ContainsKey(idLibro) Then Return
            _libroCorrente = _libriScaricatiPerId(idLibro)
            LeggiLibroCorrente()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.LibroScaricatoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub EliminaLibroScaricatoOpzione(sender As Object, e As EventArgs)
        Try
            If listaLibriScaricati Is Nothing Then Return
            Dim elementi As List(Of String) = listaLibriScaricati.OttieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            If Not Integer.TryParse(elementi(0), _idLibroDaEliminare) Then Return

            ChiudiDomandaEliminaLibro()
            _domandaEliminaLibro = New WGDomanda()
            AddHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibroScaricato
            _domandaEliminaLibro.avvia("Vuoi eliminare definitivamente questo libro scaricato? L'operazione non e' reversibile.")
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.EliminaLibroScaricatoOpzione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ConfermaEliminaLibroScaricato(risposta As DialogResult)
        Try
            ChiudiDomandaEliminaLibro()
            If risposta = DialogResult.Yes AndAlso _idLibroDaEliminare > 0 Then
                If _biblioteca.EliminaLibroScaricato(_idLibroDaEliminare) Then
                    MostraMessaggio("Libro eliminato.")
                    MostraLibriScaricati()
                Else
                    MostraMessaggio("Non e' stato possibile eliminare il libro.")
                End If
            End If
            _idLibroDaEliminare = 0
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ConfermaEliminaLibroScaricato: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiDomandaEliminaLibro()
        Try
            If _domandaEliminaLibro IsNot Nothing Then
                RemoveHandler _domandaEliminaLibro.rispostadomanda, AddressOf ConfermaEliminaLibroScaricato
                _domandaEliminaLibro = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ChiudiDomandaEliminaLibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraTestoInCampo(titolo As String, testo As String)
        Try
            ChiudiCampoDocumento()
            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init(titolo, testo, True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraTestoInCampo: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il testo in lettura.")
        End Try
    End Sub

    Private Sub ApriPdfInCampo(percorso As String, titolo As String)
        Try
            ChiudiCampoDocumento()
            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.ApriPdf(percorso, titolo, True)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ApriPdfInCampo: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il PDF in lettura.")
        End Try
    End Sub

    Private Sub CampoDocumento_Chiusura()
        Try
            _campoDocumento = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.CampoDocumento_Chiusura: " & ex.ToString())
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
            _logger.ScriviLog("MenuBibliotecaWinGabriel.ChiudiCampoDocumento: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.MostraMessaggio: " & ex.ToString())
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
                    _logger.ScriviLog("MenuBibliotecaWinGabriel.EseguiConAttesa ciclo: " & exAttesa.Message)
                End Try
            End While
            Try
                _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuBibliotecaWinGabriel.EseguiConAttesa: " & ex.ToString())
            Try
                If _processo IsNot Nothing Then _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        End Try
    End Sub

End Class
