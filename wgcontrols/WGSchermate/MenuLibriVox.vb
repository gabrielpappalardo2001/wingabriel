Imports System.Runtime.InteropServices
Imports wgcontrols
Imports WGPlayerMpv
Imports System.Threading
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

' Catalogo e riproduttore degli audiolibri di LibriVox. Permette la ricerca, il download e l'ascolto dei libri completi o dei
' singoli capitoli.
Public Class MenuLibriVox
    Private camporicerca As WGCampoTesto
    Private Menu As WGMenu
    Private menuLingua As WGMenu

    Private menuOrdinamento As WGMenu
    Private menuModalitaAscolto As WGMenu

    Private listaAttuale As WGMenuLista
    Private listaCapitoli As WGMenuLista
    Private listaInfo As WGMenuLista

    Private librivox As New CLSLibriVox
    Private idLibroSelezionato As String
    Private _logger As New Logger()
    Private attesa As WGProcessoInCorso

    Private _uiContext As SynchronizationContext

    Private _tempDatiElenco As List(Of DatiElenco)
    Private _filtroCorrente As String = ""
    Private _statoCorrente As StatoLista
    Private _ordinamentoCorrente As OrdinamentoLibri = OrdinamentoLibri.AlfabeticoAZ
    Private _linguaSelezionata As String = ""
    Private _tipoOpzioniLista As TipoOpzioni = TipoOpzioni.Standard
    Private _percorsiLibroCompleto As List(Of String)
    Private _percorsiInAttesa As List(Of String)
    Private _titoloInAttesa As String = ""

    Private Enum TipoOpzioni
        Standard
        RicercheRecenti
        Libreria
    End Enum

    Private Enum StatoLista
        Nessuno
        Ricerca
        RicercheRecenti
        UltimeAggiunte
        Titoli
        Autori
        LibriDiAutore
        Lingue
        LibriPerLingua
        AutoriPerLingua
        UltimePerLingua
        LibreriaLocale
    End Enum

    Public Sub avvia()
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            If Menu Is Nothing Then
                Menu = New WGMenu()
            End If
            Menu.aggiungi("Ricerca audiolibro", AddressOf ricercalibro)
            Menu.aggiungi("Ricerche recenti", AddressOf mostraricercherecenti)
            Menu.aggiungi("Elenco per Titolo", AddressOf mostraelencoper_titolo)
            Menu.aggiungi("Elenco per Autore", AddressOf mostraelencoper_autore)
            Menu.aggiungi("Elenco per Lingua", AddressOf mostraelencoper_lingua)
            Menu.aggiungi("Ultime Aggiunte", AddressOf mostraultimeaggiunte)
            Menu.aggiungi("Libreria (Libri Scaricati)", AddressOf libriscaricati)
            Menu.aggiungi("Aggiorna catalogo online", AddressOf aggiornaelencolibri)
            Menu.avvia("Menu LibriVox")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub aggiornaelencolibri()
        Try
            librivox.AvviaAggiornamentoCatalogo()
            Messaggio("Aggiornamento avviato in background." & vbCrLf & "Puoi controllare lo stato dal Centro di Controllo.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiSuUI(azione As Action)
        Try
            If azione Is Nothing Then Return
            If _uiContext IsNot Nothing Then
                _uiContext.Post(New SendOrPostCallback(AddressOf CallbackEsecuzioneUI), azione)
            Else
                azione.Invoke()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CallbackEsecuzioneUI(stato As Object)
        Try
            Dim azione As Action = TryCast(stato, Action)
            If azione IsNot Nothing Then
                azione.Invoke()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaCaricamento(nuovoStato As StatoLista, Optional filtro As String = "")
        Try
            If _statoCorrente <> nuovoStato Then
                _ordinamentoCorrente = OrdinamentoLibri.AlfabeticoAZ
                If nuovoStato = StatoLista.UltimeAggiunte OrElse nuovoStato = StatoLista.UltimePerLingua OrElse nuovoStato = StatoLista.RicercheRecenti Then
                    _ordinamentoCorrente = OrdinamentoLibri.PiuRecente
                End If
            End If

            Dim tipoRichiesto As TipoOpzioni = OttieniTipoOpzioni(nuovoStato)
            If listaAttuale IsNot Nothing AndAlso _tipoOpzioniLista <> tipoRichiesto Then
                listaAttuale.abbandona()
                listaAttuale = Nothing
            End If

            _statoCorrente = nuovoStato
            _filtroCorrente = filtro

            MostraAttesa("Caricamento in corso...")
            Dim t As New Thread(AddressOf ThreadCaricaDati)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            NascondiAttesa()
        End Try
    End Sub

    Private Sub ApriMenuOrdinamento()
        Try
            menuOrdinamento = New WGMenu()
            menuOrdinamento.aggiungi("Alfabetico A-Z", AddressOf ImpostaOrdinamentoAlfabetico)
            menuOrdinamento.aggiungi("Più Recente", AddressOf ImpostaOrdinamentoPiuRecente)
            menuOrdinamento.aggiungi("Meno Recente", AddressOf ImpostaOrdinamentoMenoRecente)
            menuOrdinamento.avvia("Ordina per...", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaOrdinamentoAlfabetico()
        Try
            ImpostaOrdinamento(OrdinamentoLibri.AlfabeticoAZ)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaOrdinamentoPiuRecente()
        Try
            ImpostaOrdinamento(OrdinamentoLibri.PiuRecente)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaOrdinamentoMenoRecente()
        Try
            ImpostaOrdinamento(OrdinamentoLibri.MenoRecente)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaOrdinamento(nuovoOrd As OrdinamentoLibri)
        Try
            _ordinamentoCorrente = nuovoOrd
            AvviaCaricamento(_statoCorrente, _filtroCorrente)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraInfoLibro()
        Try
            If listaAttuale Is Nothing Then Return
            If _statoCorrente = StatoLista.Autori OrElse _statoCorrente = StatoLista.AutoriPerLingua OrElse _statoCorrente = StatoLista.Lingue OrElse _statoCorrente = StatoLista.RicercheRecenti Then
                Messaggio("Le informazioni sono disponibili solo negli elenchi di libri.")
                Return
            End If
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                Dim info As DettagliLibro = librivox.GetDettagliLibro(elemento)

                listaInfo = New WGMenuLista()
                listaInfo.impostaTitolo("Dettagli: " & info.Titolo)
                listaInfo.multiselezioneConsentita = False
                listaInfo.ciclono = True

                listaInfo.aggiungiElemento("Titolo: " & info.Titolo, "")
                listaInfo.aggiungiElemento("Autore: " & info.Autore, "")
                listaInfo.aggiungiElemento("Data Pubblicazione: " & info.DataPubblicazione, "")
                listaInfo.aggiungiElemento("Numero Capitoli Scaricati: " & info.NumeroCapitoli, "")
                listaInfo.aggiungiElemento("Stato: " & If(info.Scaricato, "Scaricato", "Non Scaricato"), "")

                AddHandler listaInfo.escPremuto, AddressOf ChiudiInfoLibro

                listaInfo.avvia()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiInfoLibro()
        Try
            If listaInfo IsNot Nothing Then
                listaInfo.abbandona()
                listaInfo = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ThreadCaricaDati()
        Try
            Dim totaleElementi As Long = 0

            Select Case _statoCorrente
                Case StatoLista.Ricerca
                    totaleElementi = librivox.CountRicercaNelCatalogo(_filtroCorrente)
                Case StatoLista.RicercheRecenti
                    totaleElementi = librivox.CountRicercheRecenti()
                Case StatoLista.UltimeAggiunte
                    totaleElementi = librivox.CountUltimeAggiunte()
                Case StatoLista.Titoli
                    totaleElementi = librivox.CountCatalogoCompleto()
                Case StatoLista.Autori
                    totaleElementi = librivox.CountElencoAutori()
                Case StatoLista.LibriDiAutore
                    totaleElementi = librivox.CountRicercaPerAutore(_filtroCorrente)
                Case StatoLista.Lingue
                    totaleElementi = librivox.CountElencoLingue()
                Case StatoLista.LibriPerLingua
                    totaleElementi = librivox.CountLibriPerLingua(_filtroCorrente)
                Case StatoLista.AutoriPerLingua
                    totaleElementi = librivox.CountAutoriPerLingua(_filtroCorrente)
                Case StatoLista.UltimePerLingua
                    totaleElementi = librivox.CountUltimeAggiuntePerLingua(_filtroCorrente)
                Case StatoLista.LibreriaLocale
                    totaleElementi = librivox.CountLibriScaricati()
            End Select

            Dim limit As Integer
            If totaleElementi > Integer.MaxValue Then
                limit = Integer.MaxValue
            ElseIf totaleElementi <= 0 Then
                limit = 1
            Else
                limit = CInt(totaleElementi)
            End If

            Select Case _statoCorrente
                Case StatoLista.Ricerca
                    _tempDatiElenco = librivox.RicercaNelCatalogo(_filtroCorrente, _ordinamentoCorrente, 1, limit)
                Case StatoLista.RicercheRecenti
                    _tempDatiElenco = librivox.GetRicercheRecenti(_ordinamentoCorrente, limit)
                Case StatoLista.UltimeAggiunte
                    _tempDatiElenco = librivox.GetUltimeAggiunte(_ordinamentoCorrente, 1, limit)
                Case StatoLista.Titoli
                    _tempDatiElenco = librivox.GetCatalogoCompleto(_ordinamentoCorrente, 1, limit)
                Case StatoLista.Autori
                    _tempDatiElenco = librivox.GetElencoAutori(_ordinamentoCorrente, 1, limit)
                Case StatoLista.LibriDiAutore
                    _tempDatiElenco = librivox.RicercaPerAutore(_filtroCorrente, _ordinamentoCorrente, 1, limit)
                Case StatoLista.Lingue
                    _tempDatiElenco = librivox.GetElencoLingue(1, limit)
                Case StatoLista.LibriPerLingua
                    _tempDatiElenco = librivox.GetLibriPerLingua(_filtroCorrente, _ordinamentoCorrente, 1, limit)
                Case StatoLista.AutoriPerLingua
                    _tempDatiElenco = librivox.GetAutoriPerLingua(_filtroCorrente, _ordinamentoCorrente, 1, limit)
                Case StatoLista.UltimePerLingua
                    _tempDatiElenco = librivox.GetUltimeAggiuntePerLingua(_filtroCorrente, _ordinamentoCorrente, 1, limit)
                Case StatoLista.LibreriaLocale
                    _tempDatiElenco = librivox.GetElencoLibriScaricati(_ordinamentoCorrente, 1, limit)
            End Select

            EseguiSuUI(AddressOf UI_MostraListaGenerica)

        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            EseguiSuUI(AddressOf NascondiAttesa)
        End Try
    End Sub

    Private Sub UI_MostraListaGenerica()
        Try
            NascondiAttesa()
            If _tempDatiElenco IsNot Nothing AndAlso _tempDatiElenco.Count > 0 Then

                Dim isNew As Boolean = False
                If listaAttuale Is Nothing Then
                    listaAttuale = New WGMenuLista()
                    listaAttuale.ciclono = True

                    AddHandler listaAttuale.escPremuto, AddressOf ChiudiListaPrincipale
                    AddHandler listaAttuale.ElementoSelezionato, AddressOf GestisciSelezioneSingola

                    _tipoOpzioniLista = OttieniTipoOpzioni(_statoCorrente)

                    Select Case _tipoOpzioniLista
                        Case TipoOpzioni.RicercheRecenti
                            listaAttuale.aggiungiOpzione("Elimina la ricerca selezionata", AddressOf elimina_ricerca_recente)
                            listaAttuale.aggiungiOpzione("Cancella tutte le ricerche recenti", AddressOf svuota_ricerche_recenti)
                        Case TipoOpzioni.Libreria
                            listaAttuale.aggiungiOpzione("Riproduci tutto il libro", AddressOf riproduci_libro_completo)
                            listaAttuale.aggiungiOpzione("Elimina il libro selezionato", AddressOf elimina_libro_selezionato)
                            listaAttuale.aggiungiOpzione("Informazioni", AddressOf MostraInfoLibro)
                        Case Else
                            listaAttuale.aggiungiOpzione("Informazioni", AddressOf MostraInfoLibro)
                    End Select

                    listaAttuale.aggiungiOpzione("Ordina", AddressOf ApriMenuOrdinamento)

                    isNew = True
                End If

                Select Case _statoCorrente
                    Case StatoLista.Ricerca
                        listaAttuale.impostaTitolo($"Risultati: {_filtroCorrente}")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.RicercheRecenti
                        listaAttuale.impostaTitolo("Ricerche Recenti")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.UltimeAggiunte
                        listaAttuale.impostaTitolo("Ultime Aggiunte")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.Titoli
                        listaAttuale.impostaTitolo("Elenco per Titolo")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.Autori
                        listaAttuale.impostaTitolo("Elenco Autori")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.LibriDiAutore
                        listaAttuale.impostaTitolo($"Libri di: {_filtroCorrente}")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.Lingue
                        listaAttuale.impostaTitolo("Elenco Lingue")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.LibriPerLingua
                        listaAttuale.impostaTitolo($"Lingua: {_filtroCorrente}")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.AutoriPerLingua
                        listaAttuale.impostaTitolo($"Autori in {_filtroCorrente}")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.UltimePerLingua
                        listaAttuale.impostaTitolo($"Ultime in {_filtroCorrente}")
                        listaAttuale.multiselezioneConsentita = False

                    Case StatoLista.LibreriaLocale
                        listaAttuale.impostaTitolo("Libreria")
                        listaAttuale.multiselezioneConsentita = False
                End Select

                Dim listaDinamica As New List(Of ElementoLista)
                For Each elemento As DatiElenco In _tempDatiElenco
                    listaDinamica.Add(New ElementoLista(elemento.Titolo, elemento.Id))
                Next

                listaAttuale.CaricaListaDinamica(listaDinamica)

                If isNew Then
                    listaAttuale.avvia()
                End If
            Else
                If listaAttuale IsNot Nothing Then
                    listaAttuale.abbandona()
                    listaAttuale = Nothing
                End If
                _tipoOpzioniLista = TipoOpzioni.Standard
                _statoCorrente = StatoLista.Nessuno
                Messaggio("Nessun elemento trovato.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciSelezioneSingola()
        Try
            Select Case _statoCorrente
                Case StatoLista.Autori
                    autore_selezionato()
                Case StatoLista.Lingue
                    lingua_selezionata()
                Case StatoLista.AutoriPerLingua
                    autore_lingua_selezionato()
                Case StatoLista.RicercheRecenti
                    ricerca_recente_selezionata()
                Case StatoLista.LibreriaLocale
                    apri_libro_selezionato()
                Case Else
                    scarica_da_lista()
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function OttieniTipoOpzioni(stato As StatoLista) As TipoOpzioni
        Try
            Select Case stato
                Case StatoLista.RicercheRecenti
                    Return TipoOpzioni.RicercheRecenti
                Case StatoLista.LibreriaLocale
                    Return TipoOpzioni.Libreria
                Case Else
                    Return TipoOpzioni.Standard
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return TipoOpzioni.Standard
        End Try
    End Function

    Private Sub ChiudiListaPrincipale()
        Try
            If listaAttuale IsNot Nothing Then
                listaAttuale.abbandona()
                listaAttuale = Nothing
            End If
            _tipoOpzioniLista = TipoOpzioni.Standard
            _statoCorrente = StatoLista.Nessuno
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ricercalibro()
        Try
            camporicerca = New WGCampoTesto()
            camporicerca.init("Scrivi il titolo del libro da cercare", "", campoobbligatorio:=True)
            AddHandler camporicerca.chiusura, AddressOf avvia_ricerca
            camporicerca.avvia()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub avvia_ricerca()
        Try
            Dim query As String = camporicerca.ottienitesto()
            camporicerca.chiudi()
            If String.IsNullOrWhiteSpace(query) Then Return
            librivox.AggiungiRicercaRecente(query)
            AvviaCaricamento(StatoLista.Ricerca, query)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub mostraricercherecenti()
        Try
            If librivox.CountRicercheRecenti() = 0 Then
                Messaggio("Non ci sono ricerche recenti.")
                Return
            End If
            AvviaCaricamento(StatoLista.RicercheRecenti)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ricerca_recente_selezionata()
        Try
            If listaAttuale Is Nothing Then Return
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            If String.IsNullOrWhiteSpace(elemento) Then Return

            listaAttuale.abbandona()
            listaAttuale = Nothing

            librivox.AggiungiRicercaRecente(elemento)
            AvviaCaricamento(StatoLista.Ricerca, elemento)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub elimina_ricerca_recente()
        Try
            If _statoCorrente <> StatoLista.RicercheRecenti Then Return
            If listaAttuale Is Nothing Then Return

            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            If String.IsNullOrWhiteSpace(elemento) Then Return

            If librivox.EliminaRicercaRecente(elemento) Then
                Messaggio("Ricerca eliminata.")
            Else
                Messaggio("Impossibile eliminare la ricerca selezionata.")
            End If

            AvviaCaricamento(StatoLista.RicercheRecenti)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub svuota_ricerche_recenti()
        Try
            If _statoCorrente <> StatoLista.RicercheRecenti Then Return

            If librivox.SvuotaRicercheRecenti() Then
                Messaggio("Tutte le ricerche recenti sono state cancellate.")
            Else
                Messaggio("Impossibile cancellare le ricerche recenti.")
            End If

            If listaAttuale IsNot Nothing Then
                listaAttuale.abbandona()
                listaAttuale = Nothing
            End If
            _tipoOpzioniLista = TipoOpzioni.Standard
            _statoCorrente = StatoLista.Nessuno
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub mostraultimeaggiunte()
        Try
            AvviaCaricamento(StatoLista.UltimeAggiunte)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_titolo()
        Try
            AvviaCaricamento(StatoLista.Titoli)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_autore()
        Try
            AvviaCaricamento(StatoLista.Autori)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub mostraelencoper_lingua()
        Try
            AvviaCaricamento(StatoLista.Lingue)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub libriscaricati()
        Try
            AvviaCaricamento(StatoLista.LibreriaLocale)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub scarica_da_lista()
        Try
            If listaAttuale Is Nothing Then Return
            ScaricaGenerico(listaAttuale)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub autore_selezionato()
        Try
            If listaAttuale Is Nothing Then Return
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)

            listaAttuale.abbandona()
            listaAttuale = Nothing

            AvviaCaricamento(StatoLista.LibriDiAutore, elemento)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub lingua_selezionata()
        Try
            If listaAttuale Is Nothing Then Return
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            _linguaSelezionata = elemento

            menuLingua = New WGMenu()
            menuLingua.aggiungi("Elenco Titoli", AddressOf ApriTitoliPerLingua)
            menuLingua.aggiungi("Elenco Autori", AddressOf ApriAutoriPerLingua)
            menuLingua.aggiungi("Ultime Aggiunte", AddressOf ApriUltimePerLingua)
            menuLingua.avvia("Libri in " & _linguaSelezionata, True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriTitoliPerLingua()
        Try
            AvviaCaricamentoPerLingua(StatoLista.LibriPerLingua, _linguaSelezionata)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriAutoriPerLingua()
        Try
            AvviaCaricamentoPerLingua(StatoLista.AutoriPerLingua, _linguaSelezionata)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriUltimePerLingua()
        Try
            AvviaCaricamentoPerLingua(StatoLista.UltimePerLingua, _linguaSelezionata)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaCaricamentoPerLingua(nuovoStato As StatoLista, lingua As String)
        Try
            If listaAttuale IsNot Nothing Then
                listaAttuale.abbandona()
                listaAttuale = Nothing
            End If
            AvviaCaricamento(nuovoStato, lingua)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub autore_lingua_selezionato()
        Try
            If listaAttuale Is Nothing Then Return
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)

            listaAttuale.abbandona()
            listaAttuale = Nothing

            AvviaCaricamento(StatoLista.LibriDiAutore, elemento)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ScaricaGenerico(listaDaCuiScaricare As WGMenuLista)
        Try
            If listaDaCuiScaricare Is Nothing Then Return
            Dim elementi As List(Of String) = listaDaCuiScaricare.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            idLibroSelezionato = elemento

            Dim cartellaDati As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati", "Libri")
            If Not Directory.Exists(cartellaDati) Then
                Directory.CreateDirectory(cartellaDati)
            End If

            Dim ids As New List(Of String)()
            ids.Add(idLibroSelezionato)
            librivox.AvviaDownloadLibri(ids, cartellaDati)

            Messaggio("Download avviato in background." & vbCrLf & "Puoi controllare lo stato dal Centro di Controllo.")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function OttieniIdLibroDaLista() As String
        Try
            If listaAttuale Is Nothing Then Return ""
            Dim elementi As List(Of String) = listaAttuale.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Dim elemento As String = elementi(0)
            Return elemento
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub apri_libro_selezionato()
        Try
            Dim elemento As String = OttieniIdLibroDaLista()
            If String.IsNullOrWhiteSpace(elemento) Then Return

            idLibroSelezionato = elemento
            MostraAttesa("Caricamento capitoli...")
            Dim t As New Thread(AddressOf ThreadCaricaCapitoli)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub riproduci_libro_completo()
        Try
            If _statoCorrente <> StatoLista.LibreriaLocale Then Return

            Dim elemento As String = OttieniIdLibroDaLista()
            If String.IsNullOrWhiteSpace(elemento) Then Return

            idLibroSelezionato = elemento
            MostraAttesa("Caricamento del libro...")
            Dim t As New Thread(AddressOf ThreadCaricaLibroCompleto)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            NascondiAttesa()
        End Try
    End Sub

    Private Sub ThreadCaricaLibroCompleto()
        Try
            _percorsiLibroCompleto = New List(Of String)()
            Dim capitoli As List(Of DatiElenco) = librivox.GetCapitoliLibroScaricato(idLibroSelezionato)
            If capitoli IsNot Nothing AndAlso capitoli.Count > 0 Then
                Dim ids As New List(Of Integer)()
                For Each cap As DatiElenco In capitoli
                    Dim idInt As Integer
                    If Integer.TryParse(cap.Id, idInt) Then
                        ids.Add(idInt)
                    End If
                Next
                If ids.Count > 0 Then
                    _percorsiLibroCompleto = librivox.GetPercorsiFileCapitoli(ids)
                End If
            End If
            EseguiSuUI(AddressOf UI_RiproduciLibroCompleto)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            EseguiSuUI(AddressOf NascondiAttesa)
        End Try
    End Sub

    Private Sub UI_RiproduciLibroCompleto()
        Try
            NascondiAttesa()
            If _percorsiLibroCompleto Is Nothing OrElse _percorsiLibroCompleto.Count = 0 Then
                Messaggio("Nessun capitolo disponibile per questo libro.")
                Return
            End If
            AvviaPlayerLibro(_percorsiLibroCompleto, "Riproduzione Libro")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ThreadCaricaCapitoli()
        Try
            _tempDatiElenco = librivox.GetCapitoliLibroScaricato(idLibroSelezionato)
            EseguiSuUI(AddressOf UI_MostraCapitoli)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            EseguiSuUI(AddressOf NascondiAttesa)
        End Try
    End Sub

    Private Sub UI_MostraCapitoli()
        Try
            NascondiAttesa()
            If _tempDatiElenco IsNot Nothing AndAlso _tempDatiElenco.Count > 0 Then
                listaCapitoli = New WGMenuLista()
                listaCapitoli.ciclono = True
                listaCapitoli.multiselezioneConsentita = True
                listaCapitoli.impostaTitolo("Elenco Capitoli")
                For Each cap As DatiElenco In _tempDatiElenco
                    listaCapitoli.aggiungiElemento(cap.Titolo, cap.Id)
                Next
                AddHandler listaCapitoli.ElementiSelezionati, AddressOf riproduci_capitoli
                AddHandler listaCapitoli.ElementoSelezionato, AddressOf riproduci_capitolo_singolo
                AddHandler listaCapitoli.escPremuto, AddressOf chiudi_lista_capitoli
                listaCapitoli.avvia()
            Else
                Messaggio("Nessun capitolo trovato per questo libro.")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub chiudi_lista_capitoli()
        Try
            If listaCapitoli IsNot Nothing Then listaCapitoli.abbandona()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub elimina_libro_selezionato()
        Try
            If _statoCorrente <> StatoLista.LibreriaLocale Then Return

            Dim elemento As String = OttieniIdLibroDaLista()
            If String.IsNullOrWhiteSpace(elemento) Then Return

            idLibroSelezionato = elemento
            Dim ids As New List(Of String)()
            ids.Add(elemento)

            librivox.AvviaEliminazioneLibri(ids)
            Messaggio("Eliminazione avviata in background.")
            AvviaCaricamento(StatoLista.LibreriaLocale, "")
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub riproduci_capitoli()
        Try
            Dim idsString As List(Of String) = listaCapitoli.ottieniElementi()
            If idsString.Count > 0 Then
                Dim ids As New List(Of Integer)()
                For Each idStr As String In idsString
                    Dim idInt As Integer
                    If Integer.TryParse(idStr, idInt) Then
                        ids.Add(idInt)
                    End If
                Next

                Dim paths As List(Of String) = librivox.GetPercorsiFileCapitoli(ids)
                AvviaPlayerLibro(paths, "Riproduzione Libro")
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub riproduci_capitolo_singolo()
        Try
            If listaCapitoli Is Nothing Then Return
            Dim elementi As List(Of String) = listaCapitoli.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            Dim idInt As Integer
            If Integer.TryParse(elemento, idInt) Then
                Dim path As String = librivox.GetPercorsoFileCapitolo(idInt)
                If Not String.IsNullOrEmpty(path) Then
                    AvviaPlayerLibro(New List(Of String) From {path}, "Riproduzione Capitolo")
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaPlayerLibro(paths As List(Of String), titolo As String)
        Try
            If paths Is Nothing OrElse paths.Count = 0 Then Return

            Select Case LeggiModalitaAperturaMedia()
                Case "Sottofondo"
                    WGCentroDiControllo.GetInstance().RiproduciFileAudio(paths)
                Case "Player"
                    AvviaPlayerGui(paths, titolo)
                Case Else
                    ChiediModalitaApertura(paths, titolo)
            End Select
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LeggiModalitaAperturaMedia() As String
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore("modalitaAperturaMedia")
            If String.IsNullOrEmpty(valore) Then Return ""
            Return valore
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub ChiediModalitaApertura(paths As List(Of String), titolo As String)
        Try
            _percorsiInAttesa = paths
            _titoloInAttesa = titolo

            menuModalitaAscolto = New WGMenu()
            menuModalitaAscolto.aggiungi("Ascolto in background", AddressOf AscoltaInBackground)
            menuModalitaAscolto.aggiungi("Apri lettore multimediale", AddressOf ApriLettoreMultimediale)
            menuModalitaAscolto.avvia("Modalità di ascolto", True)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AscoltaInBackground()
        Try
            If _percorsiInAttesa Is Nothing OrElse _percorsiInAttesa.Count = 0 Then Return
            WGCentroDiControllo.GetInstance().RiproduciFileAudio(_percorsiInAttesa)
            ChiudiMenuModalitaAscolto()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriLettoreMultimediale()
        Try
            If _percorsiInAttesa Is Nothing OrElse _percorsiInAttesa.Count = 0 Then Return
            AvviaPlayerGui(_percorsiInAttesa, _titoloInAttesa)
            ChiudiMenuModalitaAscolto()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiMenuModalitaAscolto()
        Try
            If menuModalitaAscolto IsNot Nothing Then
                menuModalitaAscolto.abbandona()
                menuModalitaAscolto = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaPlayerGui(paths As List(Of String), titolo As String)
        Try
            If paths Is Nothing OrElse paths.Count = 0 Then Return
            Dim player As New WGPlayerFormInt
            For Each p As String In paths
                player.AggiungiElemento(p, wgcontrols.ElementType.File)
            Next
            player.avvia(titolo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraAttesa(testo As String)
        Try
            If attesa Is Nothing Then
                attesa = New WGProcessoInCorso()
            End If
            attesa.Avvia(testo)
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub NascondiAttesa()
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub Messaggio(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog(ex.ToString())
        End Try
    End Sub

End Class