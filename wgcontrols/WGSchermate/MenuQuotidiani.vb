Imports WGDati
Imports System.Windows.Forms
Imports System.Globalization
Imports System.IO
Imports System.Security
Imports wgcontrols
Imports System.Net
Imports System.Threading
Imports System.Threading.Tasks

Public Class MenuQuotidiani
    Private menu As New WGMenu
    Private WithEvents elencoQuotidianiPersonaliLista As WGMenuLista
    Private WithEvents elencoCategorieLista As WGMenuLista
    Private WithEvents listaSelezioneCategoria As WGMenuLista
    Private WithEvents campiInput As wgcontrols.Campi
    Private WithEvents campoSingoloInput As WGCampoTesto
    Private WithEvents domandaQuotidianoEsistente As WGDomanda
    Private quotidianoDaModificareUrl As String
    Private categoriaDaModificareId As String
    Private ReadOnly logger As New Logger()
    Private ReadOnly elencoQuotidiani As New ElencoQuotidiani()
    Private nomeQuotidianoDaAggiungere As String
    Private urlBaseQuotidianoDaAggiungere As String
    Private tipoQuotidianoDaAggiungere As TipoQuotidiano
    Private feedUrlQuotidianoDaAggiungere As String
    Private resocontoQuotidiano As RiassuntoArticoliQuotidiani

    Public Sub New()
        Try
            Dim verifica As New connessione()
            Dim risultato As Boolean = verifica.VerificaConnessione()
            If risultato Then
                menu.aggiungi("Elenco quotidiani", AddressOf MostraTuttiIQuotidiani)
                menu.aggiungi("Quotidiani preferiti", AddressOf MostraPreferiti)
                menu.aggiungi("Quotidiani recenti", AddressOf MostraRecenti)
                If RiassuntoArticoliQuotidiani.Disponibile() Then
                    menu.aggiungi("Resoconto quotidiano", AddressOf AvviaResocontoQuotidiano)
                End If
                menu.aggiungi("Quotidiani consigliati", AddressOf MostraQuotidianiConsigliati)
                menu.aggiungi("Categorie", AddressOf MostraElencoCategorie)
                menu.aggiungi("Scrivi indirizzo quotidiano", AddressOf AvviaAggiuntaNuovoQuotidiano)
                menu.avvia("Menù quotidiani")
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessuna connessione ad internet, impossibile avviare la consultazione dei quotidiani")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione nel costruttore di MenuQuotidiani: {ex.Message} - StackTrace: {ex.StackTrace}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore critico all'avvio del menu.")
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub MostraQuotidianiConsigliati()
        Try
            Dim menuConsigliati As New WGMenu()
            menuConsigliati.aggiungi("universalaccess.it", AddressOf universalaccess)
            menuConsigliati.avvia("Quotidiani consigliati")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraQuotidianiConsigliati: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub universalaccess()
        Try
            Dim quotidiano As New MenuQuotidiano("https://universalaccess.it/", "UniversalAccess")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in universalaccess: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaAggiuntaNuovoQuotidiano()
        Try
            campiInput = New wgcontrols.Campi()
            campiInput.Aggiungi("Nome Quotidiano", "nome", True)
            campiInput.Aggiungi("URL Quotidiano", "url", True)
            AddHandler campiInput.Fine, AddressOf FineCampiAggiungiQuotidiano
            campiInput.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AvviaAggiuntaNuovoQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiediSeAggiungereAltroQuotidiano(ByVal messaggioDuplicato As String)
        Try
            domandaQuotidianoEsistente = New WGDomanda()
            domandaQuotidianoEsistente.avvia(messaggioDuplicato & " Vuoi aggiungere un altro quotidiano?")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ChiediSeAggiungereAltroQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RispostaDomandaQuotidianoEsistente(risposta As DialogResult) Handles domandaQuotidianoEsistente.rispostadomanda
        Try
            If risposta = DialogResult.Yes Then
                AvviaAggiuntaNuovoQuotidiano()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Operazione annullata.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RispostaDomandaQuotidianoEsistente: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub FineCampiAggiungiQuotidiano()
        Try
            If campiInput IsNot Nothing Then
                nomeQuotidianoDaAggiungere = campiInput.RecuperaValore("nome")
                Dim urlRaw As String = campiInput.RecuperaValore("url")

                AvviaVerificaQuotidiano(urlRaw)
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Inserimento annullato: non tutti i campi obbligatori sono stati riempiti.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in FineCampiAggiungiQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        Finally
            If campiInput IsNot Nothing Then
                campiInput = Nothing
            End If
        End Try
    End Sub

    Private Sub AvviaVerificaQuotidiano(ByVal urlRaw As String)
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim rilevazione As RisultatoRilevazione = RilevatoreQuotidiano.Rileva(urlRaw)
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              GestisciRilevazioneQuotidiano(urlRaw, rilevazione)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in AvviaVerificaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              Dim msg As New WGMessaggi()
                                              msg.Messaggio("Si è verificato un errore imprevisto durante la verifica del quotidiano.")
                                              msg.ShowDialog()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Verifica del quotidiano in corso, attendere...")
    End Sub

    Private Sub GestisciRilevazioneQuotidiano(ByVal urlRaw As String, ByVal rilevazione As RisultatoRilevazione)
        Try
            If rilevazione Is Nothing OrElse String.IsNullOrEmpty(rilevazione.UrlBase) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio($"L'URL '{urlRaw}' non sembra essere valido o raggiungibile. Impossibile aggiungere il quotidiano.")
                msg.ShowDialog()
                Return
            End If

            If rilevazione.Tipo = TipoQuotidiano.Sconosciuto Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il sito è raggiungibile ma non è stato possibile trovare articoli: non è WordPress, non ha un feed RSS, e non ha una sitemap di notizie. Impossibile aggiungere il quotidiano.")
                msg.ShowDialog()
                Return
            End If

            If elencoQuotidiani.QuotidianoEsistente(rilevazione.UrlBase) Then
                ChiediSeAggiungereAltroQuotidiano("Questo quotidiano è già presente nell'elenco.")
                Return
            End If

            urlBaseQuotidianoDaAggiungere = rilevazione.UrlBase
            tipoQuotidianoDaAggiungere = rilevazione.Tipo
            feedUrlQuotidianoDaAggiungere = rilevazione.FeedUrl
            SelezionaCategoriaPerNuovoQuotidiano()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestisciRilevazioneQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SelezionaCategoriaPerNuovoQuotidiano()
        Try
            Dim categorie As List(Of CategoriaQuotidiani) = elencoQuotidiani.OttieniCategorie()
            If categorie IsNot Nothing AndAlso categorie.Count > 0 Then
                listaSelezioneCategoria = New WGMenuLista()
                listaSelezioneCategoria.impostaTitolo("Seleziona una categoria per il nuovo quotidiano")
                listaSelezioneCategoria.multiselezioneConsentita = False
                For Each cat As CategoriaQuotidiani In categorie
                    listaSelezioneCategoria.aggiungiElemento(cat.Nome, cat.Id)
                Next
                AddHandler listaSelezioneCategoria.ElementoSelezionato, AddressOf SalvaQuotidianoConCategoria
                AddHandler listaSelezioneCategoria.escPremuto, AddressOf AbbandonaListaSelezioneCategoria
                listaSelezioneCategoria.avvia()
            Else
                SalvaQuotidianoConCategoria(True)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionaCategoriaPerNuovoQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AbbandonaListaSelezioneCategoria()
        listaSelezioneCategoria.abbandona()
    End Sub

    Private Sub SalvaQuotidianoConCategoria(Optional senzaCategoria As Boolean = False)
        Try
            Dim categoriaId As String = ""
            If Not senzaCategoria Then
                Dim elementiSelezionati As List(Of String) = listaSelezioneCategoria.ottieniElementi()
                listaSelezioneCategoria.abbandona()
                If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Aggiunta annullata.")
                    msg.ShowDialog()
                    Return
                End If
                categoriaId = elementiSelezionati(0)
            End If

            If elencoQuotidiani.AggiungiQuotidiano(nomeQuotidianoDaAggiungere, urlBaseQuotidianoDaAggiungere, tipoQuotidianoDaAggiungere, feedUrlQuotidianoDaAggiungere, categoriaId) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Quotidiano aggiunto.")
                msg.ShowDialog()
            Else
                ChiediSeAggiungereAltroQuotidiano("Impossibile aggiungere il quotidiano: un quotidiano con lo stesso nome o indirizzo esiste già.")
            End If

        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SalvaQuotidianoConCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
            If listaSelezioneCategoria IsNot Nothing Then listaSelezioneCategoria.abbandona()
        End Try
    End Sub

    Private Sub AvviaCreaNuovaCategoria()
        Try
            campoSingoloInput = New WGCampoTesto()
            campoSingoloInput.init("Inserisci il nome della nuova categoria", "", campoobbligatorio:=True)
            AddHandler campoSingoloInput.chiusura, AddressOf SalvaNuovaCategoria
            campoSingoloInput.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AvviaCreaNuovaCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SalvaNuovaCategoria()
        Try
            Dim nomeCategoria As String = campoSingoloInput.ottienitesto()
            campoSingoloInput.chiudi()
            If Not String.IsNullOrWhiteSpace(nomeCategoria) Then
                Dim risultato As String = elencoQuotidiani.AggiungiCategoria(nomeCategoria)
                If String.IsNullOrEmpty(risultato) Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Categoria creata.")
                    messaggio.ShowDialog()
                    If elencoCategorieLista IsNot Nothing Then
                        elencoCategorieLista.abbandona()
                        MostraElencoCategorie()
                    End If
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio(risultato)
                    messaggio.ShowDialog()
                End If
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Creazione categoria annullata.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SalvaNuovaCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        Finally
            If campoSingoloInput IsNot Nothing Then
                campoSingoloInput.chiudi()
                campoSingoloInput = Nothing
            End If
        End Try
    End Sub

    Private Sub AvviaResocontoQuotidiano()
        Try
            Dim testate As List(Of Quotidiano) = elencoQuotidiani.OttieniPreferiti()
            If testate Is Nothing OrElse testate.Count = 0 Then testate = elencoQuotidiani.OttieniRecenti()
            If testate Is Nothing OrElse testate.Count = 0 Then testate = elencoQuotidiani.OttieniTuttiIQuotidiani()
            If resocontoQuotidiano Is Nothing Then resocontoQuotidiano = New RiassuntoArticoliQuotidiani()
            resocontoQuotidiano.CreaResoconto(testate)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AvviaResocontoQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraTuttiIQuotidiani()
        MostraElencoQuotidianiFiltrato(elencoQuotidiani.OttieniTuttiIQuotidiani(), "Elenco quotidiani personali")
    End Sub

    Private Sub MostraPreferiti()
        Dim preferiti As List(Of Quotidiano) = elencoQuotidiani.OttieniPreferiti()
        If preferiti.Count = 0 Then
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Non hai ancora nessun quotidiano preferito.")
            messaggio.ShowDialog()
            Return
        End If
        MostraElencoQuotidianiFiltrato(preferiti, "Quotidiani preferiti", "preferiti")
    End Sub

    Private Sub MostraRecenti()
        Dim recenti As List(Of Quotidiano) = elencoQuotidiani.OttieniRecenti()
        If recenti.Count = 0 Then
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Non hai ancora consultato nessun quotidiano.")
            messaggio.ShowDialog()
            Return
        End If
        MostraElencoQuotidianiFiltrato(recenti, "Quotidiani recenti", "recenti")
    End Sub

    Private Sub MostraElencoCategorie()
        Try
            Dim categorie As List(Of CategoriaQuotidiani) = elencoQuotidiani.OttieniCategorie()
            elencoCategorieLista = New WGMenuLista()
            elencoCategorieLista.impostaTitolo("Elenco Categorie")
            elencoCategorieLista.multiselezioneConsentita = False
            If categorie IsNot Nothing Then
                For Each cat As CategoriaQuotidiani In categorie
                    elencoCategorieLista.aggiungiElemento(cat.Nome, cat.Id)
                Next
            End If
            AddHandler elencoCategorieLista.ElementoSelezionato, AddressOf MostraQuotidianiDiCategoriaSelezionata
            AddHandler elencoCategorieLista.escPremuto, AddressOf AbbandonaElencoCategorie
            elencoCategorieLista.aggiungiOpzione("Crea nuova categoria", AddressOf AvviaCreaNuovaCategoria)
            elencoCategorieLista.aggiungiOpzione("Modifica nome categoria", AddressOf ModificaCategoriaSelezionata)
            elencoCategorieLista.aggiungiOpzione("Elimina categoria", AddressOf EliminaCategoriaSelezionata)
            elencoCategorieLista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraElencoCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AbbandonaElencoCategorie()
        If elencoCategorieLista IsNot Nothing Then
            elencoCategorieLista.abbandona()
        End If
    End Sub

    Private Sub MostraQuotidianiDiCategoriaSelezionata()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoCategorieLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim categoriaId As String = elementiSelezionati(0)
                Dim nomeCategoria As String = elencoCategorieLista.TestoSelezionato
                MostraElencoQuotidianiFiltrato(elencoQuotidiani.OttieniQuotidianiPerCategoria(categoriaId), "Quotidiani: " & nomeCategoria)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraQuotidianiDiCategoriaSelezionata: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraElencoQuotidianiFiltrato(sitiPersonali As List(Of Quotidiano), titoloLista As String, Optional ByVal tipoVista As String = "")
        Try
            If sitiPersonali IsNot Nothing AndAlso sitiPersonali.Count > 0 Then
                elencoQuotidianiPersonaliLista = New WGMenuLista()
                elencoQuotidianiPersonaliLista.impostaTitolo(titoloLista)
                elencoQuotidianiPersonaliLista.multiselezioneConsentita = False
                For Each quotidiano As Quotidiano In sitiPersonali
                    elencoQuotidianiPersonaliLista.aggiungiElemento(quotidiano.Titolo, quotidiano.Url)
                Next
                AddHandler elencoQuotidianiPersonaliLista.ElementoSelezionato, AddressOf AvviaMenuQuotidianoPersonale
                AddHandler elencoQuotidianiPersonaliLista.escPremuto, AddressOf AbbandonaElencoQuotidianiPersonali
                AddHandler elencoQuotidianiPersonaliLista.vocecambiata, AddressOf GestisciOpzioniContestualiQuotidiano
                AggiungiOpzioniGestioneQuotidiano(elencoQuotidianiPersonaliLista)

                If tipoVista = "preferiti" Then
                    elencoQuotidianiPersonaliLista.aggiungiOpzione("Svuota preferiti", AddressOf SvuotaPreferitiSelezionato)
                ElseIf tipoVista = "recenti" Then
                    elencoQuotidianiPersonaliLista.aggiungiOpzione("Svuota recenti", AddressOf SvuotaRecentiSelezionato)
                End If

                GestisciOpzioniContestualiQuotidiano()
                elencoQuotidianiPersonaliLista.avvia()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non sono presenti quotidiani in questa vista.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraElencoQuotidianiFiltrato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SvuotaPreferitiSelezionato()
        Try
            elencoQuotidiani.SvuotaPreferiti()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Elenco preferiti svuotato.")
            msg.ShowDialog()
            If elencoQuotidianiPersonaliLista IsNot Nothing Then elencoQuotidianiPersonaliLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SvuotaPreferitiSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SvuotaRecentiSelezionato()
        Try
            elencoQuotidiani.SvuotaRecenti()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Elenco recenti svuotato.")
            msg.ShowDialog()
            If elencoQuotidianiPersonaliLista IsNot Nothing Then elencoQuotidianiPersonaliLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SvuotaRecentiSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AggiungiOpzioniGestioneQuotidiano(ByRef lista As WGMenuLista)
        lista.aggiungiOpzione("Modifica Quotidiano", AddressOf ModificaQuotidianoSelezionato)
        lista.aggiungiOpzione("Elimina Quotidiano", AddressOf EliminaQuotidianoSelezionato)
        lista.aggiungiOpzione("Sposta in un'altra categoria", AddressOf SpostaQuotidianoSelezionato)
        lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf ToggleQuotidianoPreferito)
        lista.aggiungiOpzione("Copia indirizzo quotidiano", AddressOf CopiaIndirizzoQuotidianoSelezionato)
    End Sub

    Private Sub CopiaIndirizzoQuotidianoSelezionato()
        Try
            Dim urlSelezionato As String = elencoQuotidianiPersonaliLista.ValoreSelezionato
            If String.IsNullOrEmpty(urlSelezionato) Then Return

            Clipboard.SetText(urlSelezionato)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Indirizzo del quotidiano copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in CopiaIndirizzoQuotidianoSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private quotidianoInModifica As Quotidiano = Nothing

    Private Sub ModificaQuotidianoSelezionato()
        Try
            Dim urlSelezionato As String = elencoQuotidianiPersonaliLista.ValoreSelezionato
            If String.IsNullOrEmpty(urlSelezionato) Then Return

            Dim quotidiano As Quotidiano = elencoQuotidiani.OttieniTuttiIQuotidiani().FirstOrDefault(Function(q) q.Url = urlSelezionato)
            If quotidiano Is Nothing Then Return

            quotidianoInModifica = quotidiano

            campiInput = New wgcontrols.Campi()
            campiInput.Aggiungi("Nome Quotidiano", "nome", True, False, quotidiano.Titolo)
            campiInput.Aggiungi("URL Quotidiano", "url", True, False, quotidiano.Url)
            AddHandler campiInput.Fine, AddressOf FineCampiModificaQuotidiano
            campiInput.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ModificaQuotidianoSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub FineCampiModificaQuotidiano()
        Dim quotidianoOriginale As Quotidiano = quotidianoInModifica
        Try
            If campiInput Is Nothing OrElse quotidianoOriginale Is Nothing Then Return

            Dim nuovoNome As String = campiInput.RecuperaValore("nome")
            Dim nuovoUrlGrezzo As String = campiInput.RecuperaValore("url")

            If String.IsNullOrWhiteSpace(nuovoNome) OrElse String.IsNullOrWhiteSpace(nuovoUrlGrezzo) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Modifica annullata: non tutti i campi obbligatori sono stati riempiti.")
                messaggio.ShowDialog()
                Return
            End If

            nuovoUrlGrezzo = nuovoUrlGrezzo.Trim()

            If String.Equals(nuovoUrlGrezzo, quotidianoOriginale.Url, StringComparison.OrdinalIgnoreCase) Then
                SalvaModificaQuotidiano(quotidianoOriginale, nuovoNome, quotidianoOriginale.Url, quotidianoOriginale.Tipo, quotidianoOriginale.FeedUrl)
            Else
                AvviaVerificaModificaQuotidiano(quotidianoOriginale, nuovoNome, nuovoUrlGrezzo)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in FineCampiModificaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        Finally
            If campiInput IsNot Nothing Then
                campiInput = Nothing
            End If
            quotidianoInModifica = Nothing
        End Try
    End Sub

    Private Sub AvviaVerificaModificaQuotidiano(ByVal quotidianoOriginale As Quotidiano, ByVal nuovoNome As String, ByVal nuovoUrlGrezzo As String)
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim rilevazione As RisultatoRilevazione = RilevatoreQuotidiano.Rileva(nuovoUrlGrezzo)
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              GestisciRilevazioneModificaQuotidiano(quotidianoOriginale, nuovoNome, nuovoUrlGrezzo, rilevazione)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in AvviaVerificaModificaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              Dim msg As New WGMessaggi()
                                              msg.Messaggio("Si è verificato un errore imprevisto durante la verifica del nuovo indirizzo.")
                                              msg.ShowDialog()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Verifica del nuovo indirizzo in corso, attendere...")
    End Sub

    Private Sub GestisciRilevazioneModificaQuotidiano(ByVal quotidianoOriginale As Quotidiano, ByVal nuovoNome As String, ByVal nuovoUrlGrezzo As String, ByVal rilevazione As RisultatoRilevazione)
        Try
            If rilevazione Is Nothing OrElse String.IsNullOrEmpty(rilevazione.UrlBase) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio($"L'URL '{nuovoUrlGrezzo}' non sembra essere valido o raggiungibile. Modifica annullata.")
                msg.ShowDialog()
                Return
            End If

            If rilevazione.Tipo = TipoQuotidiano.Sconosciuto Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il nuovo indirizzo è raggiungibile ma non è stato possibile trovare articoli: non è WordPress, non ha un feed RSS, e non ha una sitemap di notizie. Modifica annullata.")
                msg.ShowDialog()
                Return
            End If

            SalvaModificaQuotidiano(quotidianoOriginale, nuovoNome, rilevazione.UrlBase, rilevazione.Tipo, rilevazione.FeedUrl)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestisciRilevazioneModificaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SalvaModificaQuotidiano(ByVal quotidianoOriginale As Quotidiano, ByVal nuovoNome As String, ByVal nuovoUrl As String, ByVal nuovoTipo As TipoQuotidiano, ByVal nuovoFeedUrl As String)
        Try
            If elencoQuotidiani.AggiornaQuotidiano(quotidianoOriginale.Url, nuovoNome, nuovoUrl, nuovoTipo, nuovoFeedUrl) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Quotidiano modificato.")
                msg.ShowDialog()
                If elencoQuotidianiPersonaliLista IsNot Nothing Then elencoQuotidianiPersonaliLista.abbandona()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Esiste già un quotidiano con questo nome o con questo indirizzo.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SalvaModificaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub GestisciOpzioniContestualiQuotidiano()
        Try
            If elencoQuotidianiPersonaliLista Is Nothing Then Return
            Dim urlSelezionato As String = elencoQuotidianiPersonaliLista.ValoreSelezionato
            If String.IsNullOrEmpty(urlSelezionato) Then Return

            Dim quotidiano As Quotidiano = elencoQuotidiani.OttieniTuttiIQuotidiani().FirstOrDefault(Function(q) q.Url = urlSelezionato)
            If quotidiano Is Nothing Then Return

            If String.IsNullOrEmpty(quotidiano.CategoriaId) Then
                elencoQuotidianiPersonaliLista.sostituisciopzione("Sposta in un'altra categoria", "Aggiungi a una categoria", AddressOf SpostaQuotidianoSelezionato)
                elencoQuotidianiPersonaliLista.rimuoviOpzione("Rimuovi dalla categoria")
            Else
                elencoQuotidianiPersonaliLista.sostituisciopzione("Aggiungi a una categoria", "Sposta in un'altra categoria", AddressOf SpostaQuotidianoSelezionato)
                elencoQuotidianiPersonaliLista.aggiungiOpzioneDopo("Sposta in un'altra categoria", "Rimuovi dalla categoria", AddressOf RimuoviQuotidianoDaCategoria)
            End If

            If quotidiano.Preferito Then
                elencoQuotidianiPersonaliLista.sostituisciopzione("Aggiungi ai preferiti", "Rimuovi dai preferiti", AddressOf ToggleQuotidianoPreferito)
            Else
                elencoQuotidianiPersonaliLista.sostituisciopzione("Rimuovi dai preferiti", "Aggiungi ai preferiti", AddressOf ToggleQuotidianoPreferito)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestisciOpzioniContestualiQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ToggleQuotidianoPreferito()
        Try
            Dim urlSelezionato As String = elencoQuotidianiPersonaliLista.ValoreSelezionato
            If String.IsNullOrEmpty(urlSelezionato) Then Return
            Dim quotidiano As Quotidiano = elencoQuotidiani.OttieniTuttiIQuotidiani().FirstOrDefault(Function(q) q.Url = urlSelezionato)
            If quotidiano Is Nothing Then Return
            elencoQuotidiani.ImpostaPreferito(urlSelezionato, Not quotidiano.Preferito)
            GestisciOpzioniContestualiQuotidiano()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ToggleQuotidianoPreferito: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AbbandonaElencoQuotidianiPersonali()
        If elencoQuotidianiPersonaliLista IsNot Nothing Then
            elencoQuotidianiPersonaliLista.abbandona()
        End If
    End Sub

    Private Sub AvviaMenuQuotidianoPersonale()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoQuotidianiPersonaliLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim urlSelezionato As String = elementiSelezionati(0)
                Dim sitoSelezionato As Quotidiano = elencoQuotidiani.OttieniTuttiIQuotidiani().FirstOrDefault(Function(s) s.Url = urlSelezionato)
                If sitoSelezionato IsNot Nothing Then
                    Dim quotidiano As New MenuQuotidiano(sitoSelezionato)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AvviaMenuQuotidianoPersonale: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub EliminaQuotidianoSelezionato()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoQuotidianiPersonaliLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim urlDaEliminare As String = elementiSelezionati(0)
                elencoQuotidiani.RimuoviQuotidiano(urlDaEliminare)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Quotidiano eliminato.")
                messaggio.ShowDialog()
                elencoQuotidianiPersonaliLista.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EliminaQuotidianoSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SpostaQuotidianoSelezionato()
        Try
            Dim categorie As List(Of CategoriaQuotidiani) = elencoQuotidiani.OttieniCategorie()
            If categorie IsNot Nothing AndAlso categorie.Count > 0 Then
                Dim elementiSelezionati As New List(Of String)
                elementiSelezionati.AddRange(elencoQuotidianiPersonaliLista.ottieniElementi())
                If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                    quotidianoDaModificareUrl = elementiSelezionati(0)
                    listaSelezioneCategoria = New WGMenuLista()
                    listaSelezioneCategoria.impostaTitolo("Scegli la nuova categoria")
                    listaSelezioneCategoria.multiselezioneConsentita = False
                    For Each cat As CategoriaQuotidiani In categorie
                        listaSelezioneCategoria.aggiungiElemento(cat.Nome, cat.Id)
                    Next
                    AddHandler listaSelezioneCategoria.ElementoSelezionato, AddressOf EseguiSpostamentoQuotidiano
                    AddHandler listaSelezioneCategoria.escPremuto, AddressOf AbbandonaListaSelezioneCategoria
                    listaSelezioneCategoria.avvia()
                End If
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono categorie in cui spostare il quotidiano.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SpostaQuotidianoSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub EseguiSpostamentoQuotidiano()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(listaSelezioneCategoria.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim nuovaCategoriaId As String = elementiSelezionati(0)
                elencoQuotidiani.SpostaQuotidianoCategoria(quotidianoDaModificareUrl, nuovaCategoriaId)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Quotidiano spostato.")
                messaggio.ShowDialog()
                listaSelezioneCategoria.abbandona()
                elencoQuotidianiPersonaliLista.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EseguiSpostamentoQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RimuoviQuotidianoDaCategoria()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoQuotidianiPersonaliLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim urlSito As String = elementiSelezionati(0)
                elencoQuotidiani.SpostaQuotidianoCategoria(urlSito, "")
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Quotidiano rimosso dalla categoria.")
                messaggio.ShowDialog()
                elencoQuotidianiPersonaliLista.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RimuoviQuotidianoDaCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ModificaCategoriaSelezionata()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoCategorieLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                categoriaDaModificareId = elementiSelezionati(0)
                Dim nomeCorrente = elencoCategorieLista.TestoSelezionato
                campoSingoloInput = New WGCampoTesto()
                campoSingoloInput.init("Modifica il nome della categoria", nomeCorrente, campoobbligatorio:=True)
                AddHandler campoSingoloInput.chiusura, AddressOf SalvaNuovoNomeCategoria
                campoSingoloInput.avvia()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ModificaCategoriaSelezionata: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub SalvaNuovoNomeCategoria()
        Try
            Dim nuovoNome As String = campoSingoloInput.ottienitesto()
            If Not String.IsNullOrWhiteSpace(nuovoNome) Then
                elencoQuotidiani.ModificaCategoria(categoriaDaModificareId, nuovoNome)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Categoria modificata.")
                messaggio.ShowDialog()
                elencoCategorieLista.abbandona()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Modifica annullata.")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SalvaNuovoNomeCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        Finally
            If campoSingoloInput IsNot Nothing Then
                campoSingoloInput.chiudi()
                campoSingoloInput = Nothing
            End If
        End Try
    End Sub

    Private Sub EliminaCategoriaSelezionata()
        Try
            Dim elementiSelezionati As New List(Of String)
            elementiSelezionati.AddRange(elencoCategorieLista.ottieniElementi())
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Count > 0 Then
                Dim categoriaId As String = elementiSelezionati(0)
                elencoQuotidiani.RimuoviCategoria(categoriaId)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Categoria eliminata. I quotidiani al suo interno sono ora 'Senza Categoria'.")
                messaggio.ShowDialog()
                elencoCategorieLista.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EliminaCategoriaSelezionata: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub
End Class

Public Class MenuArticoliQuotidianiNonLetti
    Private Const SeparatoreChiave As Char = ControlChars.Tab
    Private WithEvents elencoNonLettiLista As WGMenuLista
    Private ReadOnly logger As New Logger()
    Private ReadOnly elencoQuotidiani As New ElencoQuotidiani()
    Private ultimiNonLettiMostrati As New List(Of ArticoloQuotidianoNonLetto)
    Private articoloAperto As Articolo
    Private campoArticolo As WGCampo
    Private ReadOnly riassunti As New RiassuntoArticoliQuotidiani()

    Public Sub New()
        MostraElenco()
    End Sub

    Private Sub MostraElenco()
        Try
            Dim nonLetti As List(Of ArticoloQuotidianoNonLetto) = GestoreArchivioNotizieQuotidiani.OttieniArticoliNonLettiOggi()
            If nonLetti Is Nothing OrElse nonLetti.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono più articoli non letti dai tuoi quotidiani preferiti.")
                messaggio.ShowDialog()
                Return
            End If

            ultimiNonLettiMostrati = nonLetti

            elencoNonLettiLista = New WGMenuLista()
            elencoNonLettiLista.impostaTitolo("Articoli non letti")
            elencoNonLettiLista.multiselezioneConsentita = False
            For Each articolo As ArticoloQuotidianoNonLetto In nonLetti
                elencoNonLettiLista.aggiungiElemento(articolo.TitoloArticolo & " - " & articolo.TitoloQuotidiano, articolo.IdArticolo & SeparatoreChiave & articolo.UrlQuotidiano)
            Next
            AddHandler elencoNonLettiLista.ElementoSelezionato, AddressOf ApriArticoloSelezionato
            AddHandler elencoNonLettiLista.escPremuto, AddressOf AbbandonaElenco
            elencoNonLettiLista.aggiungiOpzione("Apri nel browser", AddressOf ApriNonLettoNelBrowser)
            elencoNonLettiLista.aggiungiOpzione("Copia indirizzo articolo", AddressOf CopiaIndirizzoNonLettoSelezionato)
            If RiassuntoArticoliQuotidiani.Disponibile() Then
                elencoNonLettiLista.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf RiassumiArticoliSelezionati)
            End If
            elencoNonLettiLista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraElenco: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AbbandonaElenco()
        Try
            If elencoNonLettiLista IsNot Nothing Then elencoNonLettiLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AbbandonaElenco: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Function TrovaSelezionato() As ArticoloQuotidianoNonLetto
        Dim elementi As List(Of String) = elencoNonLettiLista.ottieniElementi()
        If elementi Is Nothing OrElse elementi.Count = 0 Then Return Nothing

        Dim chiave As String = elementi(0)
        Dim parti() As String = chiave.Split(SeparatoreChiave)
        If parti.Length <> 2 Then Return Nothing

        Dim idArticolo As String = parti(0)
        Dim urlQuotidiano As String = parti(1)

        Return ultimiNonLettiMostrati.FirstOrDefault(Function(a) a.IdArticolo = idArticolo AndAlso a.UrlQuotidiano = urlQuotidiano)
    End Function

    ' L'articolo così com'è sul sito, nel browser di WinGabriel: conta come letto, come aprirlo con Invio.
    Private Sub ApriNonLettoNelBrowser()
        Try
            Dim selezionato As ArticoloQuotidianoNonLetto = TrovaSelezionato()
            If selezionato Is Nothing OrElse String.IsNullOrEmpty(selezionato.UrlArticolo) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Indirizzo non disponibile per questo articolo.")
                messaggio.ShowDialog()
                Return
            End If
            GestoreArchivioNotizieQuotidiani.SegnaArticoloComeLetto(selezionato.IdArticolo, selezionato.UrlQuotidiano)
            Global.WGBrowser.WGBrowser.ApriBrowser(selezionato.UrlArticolo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ApriNonLettoNelBrowser: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub CopiaIndirizzoNonLettoSelezionato()
        Try
            Dim selezionato As ArticoloQuotidianoNonLetto = TrovaSelezionato()

            If selezionato Is Nothing OrElse String.IsNullOrEmpty(selezionato.UrlArticolo) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Indirizzo non disponibile per questo articolo.")
                messaggio.ShowDialog()
                Return
            End If

            Clipboard.SetText(selezionato.UrlArticolo)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Indirizzo copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in CopiaIndirizzoNonLettoSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApriArticoloSelezionato()
        Try
            Dim selezionato As ArticoloQuotidianoNonLetto = TrovaSelezionato()
            If selezionato Is Nothing Then Return

            Dim idArticolo As String = selezionato.IdArticolo
            Dim urlQuotidiano As String = selezionato.UrlQuotidiano

            GestoreArchivioNotizieQuotidiani.SegnaArticoloComeLetto(idArticolo, urlQuotidiano)

            Dim quotidiano As Quotidiano = elencoQuotidiani.OttieniTuttiIQuotidiani().FirstOrDefault(Function(q) q.Url = urlQuotidiano)
            If quotidiano Is Nothing Then
                elencoNonLettiLista.abbandona()
                MostraElenco()
                Return
            End If

            AvviaRecuperoArticolo(quotidiano, idArticolo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ApriArticoloSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaRecuperoArticolo(ByVal quotidiano As Quotidiano, ByVal idArticolo As String)
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim gestoreArticoli As IGestoreArticoli = FabbricaGestoreArticoli.Crea(quotidiano)
                         Dim articolo As Articolo = gestoreArticoli.OttieniArticoloPerId(idArticolo)
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              MostraArticoloRecuperato(articolo)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in AvviaRecuperoArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              Dim messaggio As New WGMessaggi()
                                              messaggio.Messaggio("Si è verificato un errore imprevisto nel recuperare l'articolo.")
                                              messaggio.ShowDialog()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Recupero dell'articolo in corso, attendere...")
    End Sub

    Private Sub MostraArticoloRecuperato(ByVal articolo As Articolo)
        Try
            If articolo IsNot Nothing AndAlso Not String.IsNullOrEmpty(articolo.Testo) Then
                articoloAperto = articolo
                campoArticolo = New WGCampo()
                campoArticolo.CaricaHtml(articolo.Titolo, articolo.Testo)
                If RiassuntoArticoliQuotidiani.Disponibile() Then
                    campoArticolo.aggiungiopzione("Riassumi con l'intelligenza artificiale", AddressOf RiassumiArticoloAperto)
                End If
                campoArticolo.avvia()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non è stato possibile recuperare il testo dell'articolo.")
                messaggio.ShowDialog()
            End If

            If elencoNonLettiLista IsNot Nothing Then elencoNonLettiLista.abbandona()
            MostraElenco()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraArticoloRecuperato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RiassumiArticoloAperto(sender As Object, e As EventArgs)
        Try
            If articoloAperto Is Nothing Then Return
            Dim voci As New List(Of ArticoloDaRiassumere)
            voci.Add(New ArticoloDaRiassumere With {.Titolo = articoloAperto.Titolo, .Articolo = articoloAperto})
            riassunti.RiassumiArticoli(voci)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassumiArticoloAperto: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RiassumiArticoliSelezionati()
        Try
            If elencoNonLettiLista Is Nothing Then Return
            Dim elementi As List(Of String) = elencoNonLettiLista.ottieniElementi()
            Dim voci As New List(Of ArticoloDaRiassumere)
            If elementi IsNot Nothing Then
                For Each chiave As String In elementi
                    Dim nonLetto As ArticoloQuotidianoNonLetto = TrovaNonLettoPerChiave(chiave)
                    If nonLetto Is Nothing Then Continue For
                    voci.Add(New ArticoloDaRiassumere With {
                        .Quotidiano = TrovaQuotidianoPerUrl(nonLetto.UrlQuotidiano),
                        .TitoloTestata = nonLetto.TitoloQuotidiano,
                        .IdArticolo = nonLetto.IdArticolo,
                        .Titolo = nonLetto.TitoloArticolo
                    })
                Next
            End If
            riassunti.RiassumiArticoli(voci)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassumiArticoliSelezionati: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Function TrovaNonLettoPerChiave(chiave As String) As ArticoloQuotidianoNonLetto
        Try
            If String.IsNullOrEmpty(chiave) Then Return Nothing
            Dim parti() As String = chiave.Split(SeparatoreChiave)
            If parti.Length <> 2 Then Return Nothing
            For Each articolo As ArticoloQuotidianoNonLetto In ultimiNonLettiMostrati
                If articolo.IdArticolo = parti(0) AndAlso articolo.UrlQuotidiano = parti(1) Then Return articolo
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in TrovaNonLettoPerChiave: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function TrovaQuotidianoPerUrl(urlQuotidiano As String) As Quotidiano
        Try
            For Each quotidiano As Quotidiano In elencoQuotidiani.OttieniTuttiIQuotidiani()
                If quotidiano.Url = urlQuotidiano Then Return quotidiano
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in TrovaQuotidianoPerUrl: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

End Class

Public Class ArticoloDaRiassumere
    Public Property Quotidiano As Quotidiano
    Public Property TitoloTestata As String
    Public Property IdArticolo As String
    Public Property Titolo As String
    Public Property Articolo As Articolo
End Class

Public Class RiassuntoArticoliQuotidiani
    Private Const ArticoliPerTestata As Integer = 3
    Private Const TestateMassimeResoconto As Integer = 8
    Private Const TestateMassimeResocontoGroq As Integer = 4

    Private ReadOnly logger As New Logger()
    Private processo As WGProcessoInCorso
    Private campoRisultato As WGCampo
    Private idOperazione As Integer = 0

    Public Shared Function Disponibile() As Boolean
        Try
            Return WGAi.AlmenoUnProviderConfigurato()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.Disponibile: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Async Sub RiassumiArticoli(voci As List(Of ArticoloDaRiassumere))
        Dim operazione As Integer = 0
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then
                MostraAvviso("Seleziona almeno un articolo.")
                Return
            End If

            idOperazione += 1
            operazione = idOperazione
            AvviaProcesso(If(voci.Count = 1, "Riassunto dell'articolo in corso, attendere...", $"Riassunto di {voci.Count} articoli in corso, attendere..."))

            Dim daRecuperare As New List(Of ArticoloDaRiassumere)
            For Each voce As ArticoloDaRiassumere In voci
                If voce.Articolo Is Nothing Then daRecuperare.Add(voce)
            Next
            If daRecuperare.Count > 0 Then
                Await Task.Factory.StartNew(AddressOf RecuperaArticoliDaStato, daRecuperare)
                If operazione <> idOperazione Then Return
            End If

            Dim testo As New System.Text.StringBuilder()
            Dim riusciti As Integer = 0
            Dim ultimoErrore As String = ""
            For Each voce As ArticoloDaRiassumere In voci
                Dim titolo As String = TitoloVoce(voce)
                If voci.Count > 1 Then
                    If testo.Length > 0 Then testo.AppendLine().AppendLine()
                    testo.AppendLine(titolo)
                End If
                If voce.Articolo Is Nothing OrElse String.IsNullOrWhiteSpace(voce.Articolo.Testo) Then
                    ultimoErrore = "Non è stato possibile recuperare il testo dell'articolo"
                    testo.Append("Riassunto non riuscito: ").Append(ultimoErrore)
                    Continue For
                End If
                Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiArticoloAsync(titolo, voce.Articolo.Testo)
                If operazione <> idOperazione Then Return
                If risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item1) Then
                    testo.Append(risultato.Item1.Trim())
                    riusciti += 1
                Else
                    ultimoErrore = If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), risultato.Item2, "Riassunto non riuscito")
                    testo.Append("Riassunto non riuscito: ").Append(ultimoErrore)
                End If
            Next

            ChiudiProcesso()
            If riusciti = 0 AndAlso voci.Count = 1 Then
                MostraAvviso(ultimoErrore)
                Return
            End If
            MostraTesto(If(voci.Count = 1, "Riassunto: " & TitoloVoce(voci(0)), $"Riassunto di {voci.Count} articoli"), testo.ToString())
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RiassumiArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
            If operazione = idOperazione Then ChiudiProcesso()
        End Try
    End Sub

    Public Async Sub CreaResoconto(testate As List(Of Quotidiano))
        Dim operazione As Integer = 0
        Try
            If testate Is Nothing OrElse testate.Count = 0 Then
                MostraAvviso("Aggiungi almeno un quotidiano per ottenere il resoconto.")
                Return
            End If

            idOperazione += 1
            operazione = idOperazione
            AvviaProcesso("Preparazione del resoconto in corso, attendere...")

            Dim testateMassime As Integer = If(WGAi.ResocontoSoloConGroq(), TestateMassimeResocontoGroq, TestateMassimeResoconto)
            Dim testateScelte As New List(Of Quotidiano)
            For Each testata As Quotidiano In testate
                If testateScelte.Count >= testateMassime Then Exit For
                If testata IsNot Nothing Then testateScelte.Add(testata)
            Next

            Dim raccolti As List(Of ArticoloDaRiassumere) = Await Task.Factory.StartNew(Of List(Of ArticoloDaRiassumere))(AddressOf RaccogliArticoliResocontoDaStato, testateScelte)
            If operazione <> idOperazione Then Return

            If raccolti Is Nothing OrElse raccolti.Count = 0 Then
                ChiudiProcesso()
                MostraAvviso("Non è stato possibile scaricare gli articoli dei quotidiani.")
                Return
            End If

            Dim materiale As New List(Of Tuple(Of String, String, String))
            For Each voce As ArticoloDaRiassumere In raccolti
                materiale.Add(New Tuple(Of String, String, String)(If(voce.TitoloTestata, ""), TitoloVoce(voce), voce.Articolo.Testo))
            Next

            Dim risultato As Tuple(Of String, String) = Await WGAi.CreaResocontoNotizieAsync(materiale)
            If operazione <> idOperazione Then Return
            ChiudiProcesso()

            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                MostraAvviso(If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), risultato.Item2, "Non è stato possibile preparare il resoconto."))
                Return
            End If
            MostraTesto("Resoconto quotidiano del " & DateTime.Now.ToString("d MMMM yyyy", New System.Globalization.CultureInfo("it-IT")), risultato.Item1)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.CreaResoconto: {ex.Message} - StackTrace: {ex.StackTrace}")
            If operazione = idOperazione Then ChiudiProcesso()
        End Try
    End Sub

    Private Shared Sub RecuperaArticoliDaStato(stato As Object)
        Try
            Dim voci As List(Of ArticoloDaRiassumere) = TryCast(stato, List(Of ArticoloDaRiassumere))
            If voci Is Nothing Then Return
            For Each voce As ArticoloDaRiassumere In voci
                voce.Articolo = RecuperaArticolo(voce.Quotidiano, voce.IdArticolo)
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RecuperaArticoliDaStato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RecuperaArticolo(quotidiano As Quotidiano, idArticolo As String) As Articolo
        Try
            If quotidiano Is Nothing OrElse String.IsNullOrEmpty(idArticolo) Then Return Nothing
            Dim gestoreArticoli As IGestoreArticoli = FabbricaGestoreArticoli.Crea(quotidiano)
            Return gestoreArticoli.OttieniArticoloPerId(idArticolo)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RecuperaArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function RaccogliArticoliResocontoDaStato(stato As Object) As List(Of ArticoloDaRiassumere)
        Dim raccolti As New List(Of ArticoloDaRiassumere)
        Try
            Dim testate As List(Of Quotidiano) = TryCast(stato, List(Of Quotidiano))
            If testate Is Nothing Then Return raccolti
            For Each testata As Quotidiano In testate
                RaccogliArticoliTestata(testata, raccolti)
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RaccogliArticoliResocontoDaStato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return raccolti
    End Function

    Private Shared Sub RaccogliArticoliTestata(testata As Quotidiano, raccolti As List(Of ArticoloDaRiassumere))
        Try
            Dim gestoreArticoli As IGestoreArticoli = FabbricaGestoreArticoli.Crea(testata)
            Dim ultimi As List(Of Articolo) = gestoreArticoli.OttieniUltimiArticoli()
            If ultimi Is Nothing Then Return
            Dim presi As Integer = 0
            For Each elemento As Articolo In ultimi
                If presi >= ArticoliPerTestata Then Exit For
                If elemento Is Nothing OrElse String.IsNullOrEmpty(elemento.Id) Then Continue For
                Dim completo As Articolo = Nothing
                Try
                    completo = gestoreArticoli.OttieniArticoloPerId(elemento.Id)
                Catch exArticolo As Exception
                    Dim registroArticolo As New Logger()
                    registroArticolo.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RaccogliArticoliTestata (articolo): {exArticolo.Message} - StackTrace: {exArticolo.StackTrace}")
                End Try
                If completo Is Nothing OrElse String.IsNullOrWhiteSpace(completo.Testo) Then Continue For
                raccolti.Add(New ArticoloDaRiassumere With {
                    .Quotidiano = testata,
                    .TitoloTestata = testata.Titolo,
                    .IdArticolo = elemento.Id,
                    .Titolo = If(String.IsNullOrWhiteSpace(completo.Titolo), elemento.Titolo, completo.Titolo),
                    .Articolo = completo
                })
                presi += 1
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.RaccogliArticoliTestata: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Function TitoloVoce(voce As ArticoloDaRiassumere) As String
        Try
            If voce Is Nothing Then Return ""
            If Not String.IsNullOrWhiteSpace(voce.Titolo) Then Return voce.Titolo
            If voce.Articolo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(voce.Articolo.Titolo) Then Return voce.Articolo.Titolo
            Return "Articolo senza titolo"
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.TitoloVoce: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Sub AvviaProcesso(testo As String)
        Try
            ChiudiProcesso()
            processo = New WGProcessoInCorso()
            processo.Avvia(testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.AvviaProcesso: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiProcesso()
        Try
            If processo IsNot Nothing Then
                processo.Fine()
                processo = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.ChiudiProcesso: {ex.Message} - StackTrace: {ex.StackTrace}")
            processo = Nothing
        End Try
    End Sub

    Private Sub MostraAvviso(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.MostraAvviso: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraTesto(titolo As String, testo As String)
        Try
            ChiudiRisultato()
            campoRisultato = New WGCampo()
            AddHandler campoRisultato.chiusura, AddressOf ChiusuraCampoRisultato
            campoRisultato.init(titolo, If(testo, ""), True, True)
            campoRisultato.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.MostraTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiusuraCampoRisultato(sender As Object, e As EventArgs)
        Try
            ChiudiRisultato()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.ChiusuraCampoRisultato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiRisultato()
        Try
            If campoRisultato Is Nothing Then Return
            RemoveHandler campoRisultato.chiusura, AddressOf ChiusuraCampoRisultato
            campoRisultato.chiudi()
            campoRisultato = Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassuntoArticoliQuotidiani.ChiudiRisultato: {ex.Message} - StackTrace: {ex.StackTrace}")
            campoRisultato = Nothing
        End Try
    End Sub
End Class