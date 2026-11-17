Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports System

Public Class MenuQuotidiano
    Private WithEvents listaarticoli As WGMenuLista
    Private gestoreArticoli As IGestoreArticoli
    Private WithEvents listacategorie As WGMenuLista
    Private WithEvents menu As WGMenu
    Private titolo As String
    Private quotidianoCorrente As Quotidiano
    Private campoRicerca As WGCampoTesto
    Private campo As WGCampo
    Private ReadOnly logger As New Logger()
    Private ReadOnly elencoQuotidiani As New ElencoQuotidiani()
    Private ultimiArticoliMostrati As New List(Of Articolo)
    Private articoloAperto As Articolo
    Private ReadOnly riassunti As New RiassuntoArticoliQuotidiani()

    Public Sub New(ByVal quotidiano As Quotidiano)
        Try
            quotidianoCorrente = quotidiano
            titolo = quotidiano.Titolo
            gestoreArticoli = FabbricaGestoreArticoli.Crea(quotidiano)
            elencoQuotidiani.AggiornaUltimoAccesso(quotidiano.Url)

            If quotidiano.Tipo = TipoQuotidiano.WordPress OrElse quotidiano.Tipo = TipoQuotidiano.Rss Then
                AvviaCaricamentoQuotidiano()
            Else
                articoli()
            End If
        Catch ex As ArgumentNullException
            logger.ScriviLog($"Argomento nullo nel costruttore di MenuQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione nel costruttore di MenuQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private articoliPrecaricati As List(Of Articolo) = Nothing

    Private Sub AvviaCaricamentoQuotidiano()
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim articoliTrovati As List(Of Articolo) = gestoreArticoli.OttieniUltimiArticoli()
                         Dim categorieTrovate As List(Of Categoria) = gestoreArticoli.OttieniCategorie()
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              articoliPrecaricati = articoliTrovati
                                              MostraMenuOArticoli(categorieTrovate)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in AvviaCaricamentoQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              articoli()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Caricamento quotidiano in corso, attendere...")
    End Sub

    Private Sub MostraMenuOArticoli(ByVal categorieTrovate As List(Of Categoria))
        Try
            If categorieTrovate IsNot Nothing AndAlso categorieTrovate.Count > 0 Then
                If quotidianoCorrente.Tipo = TipoQuotidiano.WordPress Then
                    MostraMenuWordPress()
                Else
                    MostraMenuRssConCategorie()
                End If
            Else
                articoli()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraMenuOArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraMenuWordPress()
        Try
            menu = New WGMenu()
            menu.aggiungi("Tutti gli articoli " & titolo, AddressOf elencoarticoli)
            menu.aggiungi("Categorie " & titolo, AddressOf categorie)
            menu.aggiungi("Cerca articoli in " & titolo, AddressOf AvviaRicerca)
            menu.avvia("Menu quotidiano " & titolo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraMenuWordPress: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraMenuRssConCategorie()
        Try
            menu = New WGMenu()
            menu.aggiungi("Tutti gli articoli " & titolo, AddressOf elencoarticoli)
            menu.aggiungi("Categorie " & titolo, AddressOf categorie)
            menu.avvia("Menu quotidiano " & titolo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraMenuRssConCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub New(urlquotidiano As String, titoloquotidiano As String)
        Me.New(CreaQuotidianoAlVolo(urlquotidiano, titoloquotidiano))
    End Sub

    Private Shared Function CreaQuotidianoAlVolo(urlquotidiano As String, titoloquotidiano As String) As Quotidiano
        Dim rilevazione As RisultatoRilevazione = RilevatoreQuotidiano.Rileva(urlquotidiano)
        If rilevazione IsNot Nothing AndAlso rilevazione.Tipo <> TipoQuotidiano.Sconosciuto Then
            Return New Quotidiano With {.Url = rilevazione.UrlBase, .Titolo = titoloquotidiano, .Tipo = rilevazione.Tipo, .FeedUrl = rilevazione.FeedUrl}
        End If
        Return New Quotidiano With {.Url = urlquotidiano, .Titolo = titoloquotidiano, .Tipo = TipoQuotidiano.WordPress}
    End Function

    Private Sub elencoarticoli()
        Try
            articoli()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in elencoarticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaRicerca()
        Try
            campoRicerca = New WGCampoTesto()
            campoRicerca.init("Scrivi il testo da cercare", "")
            AddHandler campoRicerca.chiusura, AddressOf EseguiRicerca
            campoRicerca.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AvviaRicerca: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub EseguiRicerca()
        Try
            Dim termine As String = campoRicerca.ottienitesto()
            campoRicerca.chiudi()
            If Not String.IsNullOrWhiteSpace(termine) Then
                articoli(termineRicerca:=termine)
            End If
        Catch ex As NullReferenceException
            logger.ScriviLog($"Riferimento nullo in EseguiRicerca, l'oggetto 'campoRicerca' non era inizializzato: {ex.Message} - StackTrace: {ex.StackTrace}")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EseguiRicerca: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub categorie()
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim lista As List(Of Categoria) = gestoreArticoli.OttieniCategorie()
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              MostraCategorie(lista)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in categorie: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Recupero delle categorie in corso, attendere...")
    End Sub

    Private Sub MostraCategorie(lista As List(Of Categoria))
        Try
            If lista IsNot Nothing AndAlso lista.Count > 0 Then
                listacategorie = New WGMenuLista()
                listacategorie.multiselezioneConsentita = False
                listacategorie.impostaTitolo("Elenco categorie " & titolo)
                AddHandler listacategorie.ElementoSelezionato, AddressOf selezionacategoria
                AddHandler listacategorie.escPremuto, AddressOf chiudicategorie

                For Each elemento In lista
                    listacategorie.aggiungiElemento(elemento.Nome, elemento.Id)
                Next

                listacategorie.avvia()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono categorie da mostrare")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub selezionacategoria()
        Try
            Dim lista As List(Of String) = listacategorie.ottieniElementi()
            Dim idSelezionato As String
            For Each elemento In lista
                idSelezionato = elemento
            Next
            If Not String.IsNullOrEmpty(idSelezionato) Then
                articoli(idSelezionato)
            Else
                Dim messaggionothing As New WGMessaggi()
                messaggionothing.Messaggio("Non è stato possibile recuperare la categoria selezionata.")
                messaggionothing.ShowDialog()
            End If
        Catch ex As NullReferenceException
            logger.ScriviLog($"Riferimento nullo in selezionacategoria, 'listacategorie' non era inizializzata: {ex.Message} - StackTrace: {ex.StackTrace}")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in selezionacategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub chiudicategorie()
        Try
            If listacategorie IsNot Nothing Then
                listacategorie.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in chiudicategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub articoli(Optional idcategoria As String = "", Optional termineRicerca As String = "")
        If String.IsNullOrEmpty(idcategoria) AndAlso String.IsNullOrEmpty(termineRicerca) AndAlso articoliPrecaricati IsNot Nothing Then
            Dim precaricati As List(Of Articolo) = articoliPrecaricati
            articoliPrecaricati = Nothing
            MostraArticoli(precaricati, "Non sono presenti articoli da mostrare.", "")
            Return
        End If

        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim articoliResult As New List(Of Articolo)
                         Dim messaggioNessunArticolo As String = ""

                         If Not String.IsNullOrEmpty(termineRicerca) Then
                             articoliResult.AddRange(gestoreArticoli.RicercaArticoli(termineRicerca))
                             messaggioNessunArticolo = "Nessun articolo trovato per i termini di ricerca."
                         ElseIf String.IsNullOrEmpty(idcategoria) Then
                             articoliResult.AddRange(gestoreArticoli.OttieniUltimiArticoli())
                             messaggioNessunArticolo = "Non sono presenti articoli da mostrare."
                         Else
                             articoliResult.AddRange(gestoreArticoli.OttieniArticoliPerCategoria(idcategoria))
                             messaggioNessunArticolo = "Non sono presenti articoli per la categoria selezionata."
                         End If

                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              MostraArticoli(articoliResult, messaggioNessunArticolo, termineRicerca)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in articoli: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Recupero degli articoli in corso, attendere...")
    End Sub

    Private Sub MostraArticoli(articoliResult As List(Of Articolo), messaggioNessunArticolo As String, termineRicerca As String)
        Try
            If articoliResult IsNot Nothing AndAlso articoliResult.Count > 0 Then
                ultimiArticoliMostrati = articoliResult

                listaarticoli = New WGMenuLista()
                AddHandler listaarticoli.ElementoSelezionato, AddressOf seleziona
                AddHandler listaarticoli.escPremuto, AddressOf chiudilista

                If Not String.IsNullOrEmpty(termineRicerca) Then
                    listaarticoli.impostaTitolo("Risultati ricerca per: " & termineRicerca)
                Else
                    listaarticoli.impostaTitolo("Elenco articoli " & titolo)
                End If

                listaarticoli.multiselezioneConsentita = False
                For Each elemento As Articolo In articoliResult
                    listaarticoli.aggiungiElemento(elemento.Titolo, elemento.Id)
                Next

                listaarticoli.aggiungiOpzione("Apri nel browser", AddressOf ApriArticoloNelBrowser)
                listaarticoli.aggiungiOpzione("Copia indirizzo articolo", AddressOf CopiaIndirizzoArticoloSelezionato)
                If RiassuntoArticoliQuotidiani.Disponibile() Then
                    listaarticoli.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf RiassumiArticoliSelezionati)
                End If

                listaarticoli.avvia()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(messaggioNessunArticolo)
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    ' L'articolo così com'è sul sito, nel browser di WinGabriel.
    Private Sub ApriArticoloNelBrowser()
        Try
            Dim elementi As List(Of String) = listaarticoli.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idArticolo As String = elementi(0)
            Dim articolo As Articolo = ultimiArticoliMostrati.FirstOrDefault(Function(a) a.Id = idArticolo)
            If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.Url) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Indirizzo non disponibile per questo articolo.")
                messaggio.ShowDialog()
                Return
            End If
            Global.WGBrowser.WGBrowser.ApriBrowser(articolo.Url)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ApriArticoloNelBrowser: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub CopiaIndirizzoArticoloSelezionato()
        Try
            Dim elementi As List(Of String) = listaarticoli.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim idArticolo As String = elementi(0)
            Dim articolo As Articolo = ultimiArticoliMostrati.FirstOrDefault(Function(a) a.Id = idArticolo)

            If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.Url) Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Indirizzo non disponibile per questo articolo.")
                messaggio.ShowDialog()
                Return
            End If

            Clipboard.SetText(articolo.Url)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Indirizzo copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in CopiaIndirizzoArticoloSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub seleziona()
        Try
            Dim elemento As String = Nothing
            Dim lista As List(Of String) = listaarticoli.ottieniElementi()
            For Each art In lista
                elemento = art
            Next

            If String.IsNullOrEmpty(elemento) Then Return

            AvviaRecuperoArticolo(elemento)
        Catch ex As NullReferenceException
            logger.ScriviLog($"Riferimento nullo in seleziona, 'listaarticoli' o 'gestoreArticoli' non erano inizializzati: {ex.Message} - StackTrace: {ex.StackTrace}")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in seleziona: {ex.Message} - StackTrace: {ex.StackTrace}")
            Dim messaggiotesto As New WGMessaggi()
            messaggiotesto.Messaggio("Si è verificato un errore imprevisto nel visualizzare l'articolo.")
            messaggiotesto.ShowDialog()
        End Try
    End Sub

    Private Sub AvviaRecuperoArticolo(ByVal idArticolo As String)
        Dim processoincorso As New WGProcessoInCorso()
        Dim syncContext As SynchronizationContext = SynchronizationContext.Current

        Task.Run(Sub()
                     Try
                         Dim articolo As Articolo = gestoreArticoli.OttieniArticoloPerId(idArticolo)
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              MostraArticoloSelezionato(articolo)
                                          End Sub, Nothing)
                     Catch ex As Exception
                         logger.ScriviLog($"Eccezione in AvviaRecuperoArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
                         syncContext.Post(Sub(state)
                                              processoincorso.Fine()
                                              Dim messaggiotesto As New WGMessaggi()
                                              messaggiotesto.Messaggio("Si è verificato un errore imprevisto nel recuperare l'articolo.")
                                              messaggiotesto.ShowDialog()
                                          End Sub, Nothing)
                     End Try
                 End Sub)

        processoincorso.Avvia("Recupero dell'articolo in corso, attendere...")
    End Sub

    Private Sub MostraArticoloSelezionato(ByVal articolo As Articolo)
        Try
            If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.Testo) Then
                Dim messaggiotesto As New WGMessaggi()
                messaggiotesto.Messaggio("Non è stato possibile recuperare il testo dell'articolo")
                messaggiotesto.ShowDialog()
            Else
                articoloAperto = articolo
                campo = New WGCampo()
                campo.CaricaHtml(articolo.Titolo, articolo.Testo)
                If RiassuntoArticoliQuotidiani.Disponibile() Then
                    campo.aggiungiopzione("Riassumi con l'intelligenza artificiale", AddressOf RiassumiArticoloAperto)
                End If
                ' Per le testate WordPress: informazioni dell'articolo e commenti, dalle API del sito.
                If quotidianoCorrente IsNot Nothing AndAlso quotidianoCorrente.Tipo = TipoQuotidiano.WordPress AndAlso Not String.IsNullOrEmpty(articolo.Id) Then
                    Dim idArticolo As String = articolo.Id
                    Dim titoloArticolo As String = articolo.Titolo
                    campo.aggiungiopzione("Informazioni sull'articolo", Sub(s, e) InformazioniArticoloWordPress(idArticolo, titoloArticolo))
                    campo.aggiungiopzione("Commenti", Sub(s, e) CommentiArticoloWordPress(idArticolo, titoloArticolo))
                End If
                campo.avvia()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraArticoloSelezionato: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub InformazioniArticoloWordPress(idArticolo As String, titoloArticolo As String)
        Try
            Dim radice As String = WGWordPress.RadiceDaSito(quotidianoCorrente.Url)
            Dim testo As String = Nothing
            Await WGProcessoInCorso.EseguiAsync("Recupero in corso",
                Async Function() As Task
                    testo = Await WGWordPress.InformazioniAsync(radice, "wp/v2/posts/" & idArticolo)
                End Function)
            If testo Is Nothing Then
                MostraMessaggioQuotidiano("Non è stato possibile leggere le informazioni dell'articolo.")
                Return
            End If
            Dim informazioni As New WGCampo()
            informazioni.init("Informazioni: " & titoloArticolo, testo, True, True)
            informazioni.avvia()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InformazioniArticoloWordPress: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub CommentiArticoloWordPress(idArticolo As String, titoloArticolo As String)
        Try
            Dim radice As String = WGWordPress.RadiceDaSito(quotidianoCorrente.Url)
            Dim commenti As List(Of WGWordPress.CommentoWp) = Nothing
            Await WGProcessoInCorso.EseguiAsync("Recupero dei commenti in corso",
                Async Function() As Task
                    commenti = Await WGWordPress.CommentiAsync(radice, idArticolo)
                End Function)
            If commenti Is Nothing Then
                MostraMessaggioQuotidiano("I commenti non sono disponibili.")
                Return
            End If
            If commenti.Count = 0 Then
                MostraMessaggioQuotidiano("Non ci sono commenti.")
                Return
            End If
            WGWordPress.MostraCommenti(titoloArticolo, commenti)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in CommentiArticoloWordPress: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraMessaggioQuotidiano(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MostraMessaggioQuotidiano: {ex.Message}")
        End Try
    End Sub

    Private Sub chiudilista()
        Try
            If listaarticoli IsNot Nothing Then
                listaarticoli.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in chiudilista: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RiassumiArticoloAperto(sender As Object, e As EventArgs)
        Try
            If articoloAperto Is Nothing Then Return
            Dim voci As New List(Of ArticoloDaRiassumere)
            voci.Add(New ArticoloDaRiassumere With {.Quotidiano = quotidianoCorrente, .TitoloTestata = titolo, .Titolo = articoloAperto.Titolo, .Articolo = articoloAperto})
            riassunti.RiassumiArticoli(voci)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassumiArticoloAperto: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RiassumiArticoliSelezionati()
        Try
            If listaarticoli Is Nothing Then Return
            Dim elementi As List(Of String) = listaarticoli.ottieniElementi()
            Dim voci As New List(Of ArticoloDaRiassumere)
            If elementi IsNot Nothing Then
                For Each idArticolo As String In elementi
                    If String.IsNullOrEmpty(idArticolo) Then Continue For
                    Dim titoloArticolo As String = ""
                    For Each elemento As Articolo In ultimiArticoliMostrati
                        If elemento.Id = idArticolo Then
                            titoloArticolo = elemento.Titolo
                            Exit For
                        End If
                    Next
                    voci.Add(New ArticoloDaRiassumere With {.Quotidiano = quotidianoCorrente, .TitoloTestata = titolo, .IdArticolo = idArticolo, .Titolo = titoloArticolo})
                Next
            End If
            riassunti.RiassumiArticoli(voci)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RiassumiArticoliSelezionati: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

End Class