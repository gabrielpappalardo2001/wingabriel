Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' I podcast: ricerca, classifiche, podcast seguiti e preferiti, episodi ascoltati di recente, nuovi episodi e scaricamento sul
''' computer. L'ascolto passa dal lettore multimediale di WinGabriel, come per gli altri brani.
''' </summary>
Public Class MenuPodcast

    Private Enum ModoElenco
        Ricerca
        Seguiti
        Preferiti
    End Enum

    Private Class VoceEpisodio
        Public Podcast As PodcastInfo
        Public Episodio As EpisodioPodcast
    End Class

    Private Const VOCE_SEGUITI As String = "Seguiti"
    Private Const VOCE_NUOVI As String = "Nuovi episodi"
    Private Const VOCE_RECENTI As String = "Recenti"
    Private Const VOCE_SCARICATI As String = "Scaricati"

    Private ReadOnly _archivio As New PodcastArchivio()
    Private _menu As WGMenu
    Private _nuoviVisibile As Boolean
    Private _scaricatiVisibile As Boolean

    ' ------------------------------------------------------------------------------------------
    ' Il menu principale
    ' ------------------------------------------------------------------------------------------

    Public Sub Avvia()
        Try
            _menu = New WGMenu()
            _menu.aggiungi("Cerca", AddressOf Cerca)
            _menu.aggiungi("Classifiche", AddressOf MostraClassifiche)
            _menu.aggiungi(VOCE_SEGUITI, AddressOf MostraSeguiti)
            _nuoviVisibile = _archivio.CiSonoSeguiti()
            If _nuoviVisibile Then _menu.aggiungi(VOCE_NUOVI, AddressOf MostraNuovi)
            _menu.aggiungi("Preferiti", AddressOf MostraPreferiti)
            _menu.aggiungi(VOCE_RECENTI, AddressOf MostraRecenti)
            _scaricatiVisibile = _archivio.CiSonoScaricati()
            If _scaricatiVisibile Then _menu.aggiungi(VOCE_SCARICATI, AddressOf MostraScaricati)
            _menu.aggiungi("Paese", AddressOf ScegliPaese)
            AddHandler _menu.MenuRiattivato, Sub() AggiornaVoci()
            _menu.avvia("Podcast")
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.Avvia: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Le scelte dei podcast, per Impostazioni, Applicazioni.</summary>
    Public Shared Sub MostraImpostazioni()
        Try
            Dim m As New MenuPodcast()
            m.ScegliPaese()
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraImpostazioni: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>"Nuovi episodi" c'è solo se si segue qualche podcast, "Scaricati" solo se c'è qualcosa di scaricato.</summary>
    Private Sub AggiornaVoci()
        Try
            If _menu Is Nothing Then Return
            Dim nuovi As Boolean = _archivio.CiSonoSeguiti()
            If nuovi <> _nuoviVisibile Then
                _nuoviVisibile = nuovi
                If nuovi Then
                    _menu.AggiungiDopo(VOCE_SEGUITI, VOCE_NUOVI, AddressOf MostraNuovi)
                Else
                    _menu.RimuoviVoce(VOCE_NUOVI)
                End If
            End If
            Dim scaricati As Boolean = _archivio.CiSonoScaricati()
            If scaricati <> _scaricatiVisibile Then
                _scaricatiVisibile = scaricati
                If scaricati Then
                    _menu.AggiungiDopo(VOCE_RECENTI, VOCE_SCARICATI, AddressOf MostraScaricati)
                Else
                    _menu.RimuoviVoce(VOCE_SCARICATI)
                End If
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.AggiornaVoci: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Aiutanti per la rete
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Esegue una chiamata con l'attesa a schermo. Se fallisce dice l'errore con un messaggio e restituisce Nothing.</summary>
    Private Async Function ProvaAsync(Of T)(attesa As String, lavoro As Func(Of Task(Of T))) As Task(Of T)
        Try
            Return Await WGProcessoInCorso.EseguiAsync(Of T)(attesa, lavoro)
        Catch ex As PodcastEccezione
            MediaUtil.Messaggio(ex.Message)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.ProvaAsync: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore nel leggere i podcast.")
        End Try
        Return Nothing
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Ricerca e classifiche
    ' ------------------------------------------------------------------------------------------

    Private Sub Cerca()
        MediaUtil.ChiediTesto("Cerca podcast", "", Sub(testo) EseguiRicerca(testo))
    End Sub

    Private Async Sub EseguiRicerca(testo As String)
        Try
            Dim paese As String = PodcastImpostazioni.Paese()
            Dim r As List(Of PodcastInfo) = Await ProvaAsync("Cerco " & testo, Function() PodcastRete.CercaAsync(testo, paese, CancellationToken.None))
            If r Is Nothing Then Return
            If r.Count = 0 Then
                MediaUtil.Messaggio("Nessun podcast trovato per " & testo & ".")
                Return
            End If
            MostraPodcast("Risultati per " & testo, r, ModoElenco.Ricerca)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraClassifiche()
        Try
            Dim paese As String = PodcastImpostazioni.Paese()
            Dim menu As New WGMenu()
            menu.aggiungi("Più ascoltati", Sub() ApriClassifica(0, "Più ascoltati"))
            For Each c As KeyValuePair(Of Integer, String) In PodcastRete.Categorie
                Dim id As Integer = c.Key
                Dim nome As String = c.Value
                menu.aggiungi(nome, Sub() ApriClassifica(id, nome))
            Next
            menu.avvia("Classifiche, " & PodcastRete.NomePaese(paese))
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraClassifiche: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ApriClassifica(genere As Integer, nome As String)
        Try
            Dim paese As String = PodcastImpostazioni.Paese()
            Dim r As List(Of PodcastInfo) = Await ProvaAsync("Carico la classifica", Function() PodcastRete.ClassificaAsync(paese, genere, CancellationToken.None))
            If r Is Nothing Then Return
            If r.Count = 0 Then
                MediaUtil.Messaggio("La classifica è vuota.")
                Return
            End If
            MostraPodcast(nome & ", " & PodcastRete.NomePaese(paese), r, ModoElenco.Ricerca)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.ApriClassifica: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Elenchi di podcast: risultati, classifiche, seguiti e preferiti
    ' ------------------------------------------------------------------------------------------

    Private Sub MostraSeguiti()
        Dim lista As List(Of PodcastInfo) = _archivio.Seguiti()
        If lista.Count = 0 Then
            MediaUtil.Messaggio("Non segui ancora nessun podcast. Cercalo o aprilo da una classifica, poi scegli Segui dalle opzioni.")
            Return
        End If
        MostraPodcast("Seguiti, " & lista.Count.ToString(), lista, ModoElenco.Seguiti)
    End Sub

    Private Sub MostraPreferiti()
        Dim lista As List(Of PodcastInfo) = _archivio.Preferiti()
        If lista.Count = 0 Then
            MediaUtil.Messaggio("Non hai ancora podcast preferiti. Cercalo o aprilo da una classifica, poi scegli Aggiungi ai preferiti dalle opzioni.")
            Return
        End If
        MostraPodcast("Preferiti, " & lista.Count.ToString(), lista, ModoElenco.Preferiti)
    End Sub

    Private Sub MostraPodcast(titolo As String, lista As List(Of PodcastInfo), modo As ModoElenco)
        Try
            Dim seguiti As HashSet(Of String) = _archivio.IndirizziSeguiti()
            Dim preferiti As HashSet(Of String) = _archivio.IndirizziPreferiti()
            Dim elenco As New ElencoMedia(titolo, True)
            Dim perId As New Dictionary(Of String, PodcastInfo)()
            For i As Integer = 0 To lista.Count - 1
                Dim id As String = i.ToString()
                perId.Add(id, lista(i))
                elenco.Aggiungi(lista(i).Etichetta(seguiti.Contains(lista(i).FeedUrl), preferiti.Contains(lista(i).FeedUrl)), id)
            Next
            AddHandler elenco.Scelto, Sub(id As String)
                                          Dim p As PodcastInfo = Nothing
                                          If perId.TryGetValue(id, p) Then ApriPodcast(p)
                                      End Sub
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String))
                                             Dim p As PodcastInfo = Nothing
                                             If ids.Count > 0 AndAlso perId.TryGetValue(ids(0), p) Then ApriPodcast(p)
                                         End Sub

            Select Case modo
                Case ModoElenco.Seguiti
                    elenco.Opzione("Smetti di seguire", Sub()
                                                            ImpostaSeguiti(Scelti(perId, elenco.Selezionati()), False)
                                                            Ricarica(elenco, modo)
                                                        End Sub)
                    elenco.Opzione("Aggiungi ai preferiti", Sub() ImpostaPreferiti(Scelti(perId, elenco.Selezionati()), True))
                Case ModoElenco.Preferiti
                    elenco.Opzione("Togli dai preferiti", Sub()
                                                              ImpostaPreferiti(Scelti(perId, elenco.Selezionati()), False)
                                                              Ricarica(elenco, modo)
                                                          End Sub)
                    elenco.Opzione("Segui", Sub() ImpostaSeguiti(Scelti(perId, elenco.Selezionati()), True))
                Case Else
                    elenco.Opzione("Segui", Sub() ImpostaSeguiti(Scelti(perId, elenco.Selezionati()), True))
                    elenco.Opzione("Aggiungi ai preferiti", Sub() ImpostaPreferiti(Scelti(perId, elenco.Selezionati()), True))
            End Select
            elenco.Opzione("Informazioni", Sub()
                                               Dim s As List(Of PodcastInfo) = Scelti(perId, elenco.Selezionati())
                                               If s.Count > 0 Then InformazioniPodcast(s(0))
                                           End Sub)
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraPodcast: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Dopo aver tolto un podcast dai seguiti o dai preferiti l'elenco si riapre aggiornato.</summary>
    Private Sub Ricarica(elenco As ElencoMedia, modo As ModoElenco)
        elenco.Chiudi()
        If modo = ModoElenco.Seguiti Then
            Dim l As List(Of PodcastInfo) = _archivio.Seguiti()
            If l.Count > 0 Then MostraPodcast("Seguiti, " & l.Count.ToString(), l, modo)
        Else
            Dim l As List(Of PodcastInfo) = _archivio.Preferiti()
            If l.Count > 0 Then MostraPodcast("Preferiti, " & l.Count.ToString(), l, modo)
        End If
    End Sub

    Private Shared Function Scelti(perId As Dictionary(Of String, PodcastInfo), ids As List(Of String)) As List(Of PodcastInfo)
        Dim r As New List(Of PodcastInfo)()
        If ids Is Nothing Then Return r
        For Each id As String In ids
            Dim p As PodcastInfo = Nothing
            If perId.TryGetValue(id, p) Then r.Add(p)
        Next
        Return r
    End Function

    Private Sub ImpostaSeguiti(podcast As List(Of PodcastInfo), seguito As Boolean)
        If podcast.Count = 0 Then Return
        For Each p As PodcastInfo In podcast
            _archivio.ImpostaSeguito(p, seguito)
        Next
        If podcast.Count = 1 Then
            MediaUtil.Messaggio(If(seguito, "Ora segui " & podcast(0).Titolo & ".", "Non segui più " & podcast(0).Titolo & "."))
        Else
            MediaUtil.Messaggio(If(seguito, "Ora segui " & podcast.Count.ToString() & " podcast.", "Non segui più " & podcast.Count.ToString() & " podcast."))
        End If
    End Sub

    Private Sub ImpostaPreferiti(podcast As List(Of PodcastInfo), preferito As Boolean)
        If podcast.Count = 0 Then Return
        For Each p As PodcastInfo In podcast
            _archivio.ImpostaPreferito(p, preferito)
        Next
        If podcast.Count = 1 Then
            MediaUtil.Messaggio(If(preferito, podcast(0).Titolo & " è tra i preferiti.", podcast(0).Titolo & " non è più tra i preferiti."))
        Else
            MediaUtil.Messaggio(If(preferito, podcast.Count.ToString() & " podcast aggiunti ai preferiti.", podcast.Count.ToString() & " podcast tolti dai preferiti."))
        End If
    End Sub

    Private Sub InformazioniPodcast(p As PodcastInfo)
        Dim righe As New List(Of String)()
        righe.Add(p.Titolo)
        If p.Autore <> "" Then righe.Add("Di " & p.Autore)
        If p.Categoria <> "" Then righe.Add("Categoria: " & p.Categoria)
        If p.NumeroEpisodi > 0 Then righe.Add(p.NumeroEpisodi.ToString() & " episodi")
        If p.Descrizione <> "" Then righe.Add(Taglia(p.Descrizione, 2000))
        If p.Sito <> "" Then righe.Add("Sito: " & p.Sito)
        MediaUtil.Messaggio(String.Join(vbCrLf, righe))
    End Sub

    Private Shared Function Taglia(testo As String, massimo As Integer) As String
        If testo Is Nothing OrElse testo.Length <= massimo Then Return testo
        Return testo.Substring(0, massimo).TrimEnd() & "…"
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Un podcast e i suoi episodi
    ' ------------------------------------------------------------------------------------------

    Private Async Sub ApriPodcast(info As PodcastInfo)
        Try
            Dim feed As FeedPodcast = Await ProvaAsync("Carico gli episodi di " & info.Titolo, Function() PodcastRete.LeggiFeedAsync(info.FeedUrl, CancellationToken.None))
            If feed Is Nothing Then Return
            ' il catalogo e il feed possono dire cose diverse: si tiene quello che manca
            feed.Info.FeedUrl = info.FeedUrl
            If feed.Info.Titolo = "" Then feed.Info.Titolo = info.Titolo
            If feed.Info.Autore = "" Then feed.Info.Autore = info.Autore
            If feed.Info.Categoria = "" Then feed.Info.Categoria = info.Categoria
            If feed.Info.Immagine = "" Then feed.Info.Immagine = info.Immagine
            Dim voci As List(Of VoceEpisodio) = feed.Episodi.Select(Function(e) New VoceEpisodio With {.Podcast = feed.Info, .Episodio = e}).ToList()
            MostraEpisodi(feed.Info.Titolo & ", " & voci.Count.ToString() & If(voci.Count = 1, " episodio", " episodi"), voci, feed.Info, False, False)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.ApriPodcast: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' L'elenco degli episodi. Con un podcast solo (podcast diverso da Nothing) si può seguirlo e aggiungerlo ai preferiti; negli elenchi
    ''' misti (nuovi episodi, recenti) ogni voce dice di quale podcast è.
    ''' </summary>
    Private Sub MostraEpisodi(titolo As String, voci As List(Of VoceEpisodio), podcast As PodcastInfo, dalPiuVecchio As Boolean, sonoRecenti As Boolean)
        Try
            Dim misto As Boolean = podcast Is Nothing
            Dim ordinati As List(Of VoceEpisodio) = If(dalPiuVecchio, Enumerable.Reverse(voci).ToList(), voci)

            Dim scaricati As New HashSet(Of String)()
            For Each s As EpisodioScaricato In _archivio.Scaricati()
                scaricati.Add(s.FeedUrl & "|" & s.Guid)
            Next
            Dim ascoltati As New Dictionary(Of String, HashSet(Of String))()
            Dim giaAscoltato As Func(Of VoceEpisodio, Boolean) =
                Function(v As VoceEpisodio) As Boolean
                    Dim insieme As HashSet(Of String) = Nothing
                    If Not ascoltati.TryGetValue(v.Podcast.FeedUrl, insieme) Then
                        insieme = _archivio.Ascoltati(v.Podcast.FeedUrl)
                        ascoltati.Add(v.Podcast.FeedUrl, insieme)
                    End If
                    Return insieme.Contains(v.Episodio.Guid)
                End Function

            ' tutti gli episodi, anche migliaia: si aggiungono in un colpo solo, senza ridisegnare l'elenco a ogni voce
            Dim elenco As New ElencoMedia(titolo, True)
            Dim haScaricati As Boolean = False
            Dim voceElenco As New List(Of KeyValuePair(Of String, String))(ordinati.Count)
            For i As Integer = 0 To ordinati.Count - 1
                Dim v As VoceEpisodio = ordinati(i)
                Dim stati As New List(Of String)()
                If scaricati.Contains(v.Podcast.FeedUrl & "|" & v.Episodio.Guid) Then
                    stati.Add("scaricato")
                    haScaricati = True
                End If
                If Not sonoRecenti AndAlso giaAscoltato(v) Then stati.Add("ascoltato")
                voceElenco.Add(New KeyValuePair(Of String, String)(v.Episodio.Etichetta(If(misto, v.Podcast.Titolo, ""), String.Join(", ", stati)), i.ToString()))
            Next
            elenco.AggiungiMolti(voceElenco)

            AddHandler elenco.Scelto, Sub(id As String) Riproduci(titolo, Presi(ordinati, New List(Of String) From {id}))
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String)) Riproduci(titolo, Presi(ordinati, ids))

            elenco.Opzione("Riproduci tutti", Sub() Riproduci(titolo, ordinati))
            elenco.Opzione("Scarica", Sub() ScaricaEpisodi(Presi(ordinati, elenco.Selezionati())))
            If haScaricati Then elenco.Opzione("Elimina il download", Sub() EliminaDownload(Presi(ordinati, elenco.Selezionati()), elenco, titolo, voci, podcast, dalPiuVecchio, sonoRecenti))
            elenco.Opzione("Informazioni sull'episodio", Sub()
                                                             Dim p As List(Of VoceEpisodio) = Presi(ordinati, elenco.Selezionati())
                                                             If p.Count > 0 Then InformazioniEpisodio(p(0))
                                                         End Sub)
            If misto Then
                elenco.Opzione("Apri il podcast", Sub()
                                                      Dim p As List(Of VoceEpisodio) = Presi(ordinati, elenco.Selezionati())
                                                      If p.Count > 0 Then ApriPodcast(p(0).Podcast)
                                                  End Sub)
                If sonoRecenti Then
                    elenco.Opzione("Togli dai recenti", Sub()
                                                            For Each v As VoceEpisodio In Presi(ordinati, elenco.Selezionati())
                                                                _archivio.TogliDaiRecenti(v.Podcast.FeedUrl, v.Episodio.Guid)
                                                            Next
                                                            elenco.Chiudi()
                                                            MostraRecenti()
                                                        End Sub)
                    elenco.Opzione("Svuota i recenti", Sub() SvuotaRecenti(elenco))
                End If
            Else
                elenco.Opzione("Informazioni sul podcast", Sub() InformazioniPodcast(podcast))
                If _archivio.IndirizziSeguiti().Contains(podcast.FeedUrl) Then
                    elenco.Opzione("Smetti di seguire", Sub()
                                                            ImpostaSeguiti(New List(Of PodcastInfo) From {podcast}, False)
                                                            elenco.Chiudi()
                                                            MostraEpisodi(titolo, voci, podcast, dalPiuVecchio, sonoRecenti)
                                                        End Sub)
                Else
                    elenco.Opzione("Segui", Sub()
                                                ImpostaSeguiti(New List(Of PodcastInfo) From {podcast}, True)
                                                elenco.Chiudi()
                                                MostraEpisodi(titolo, voci, podcast, dalPiuVecchio, sonoRecenti)
                                            End Sub)
                End If
                If _archivio.IndirizziPreferiti().Contains(podcast.FeedUrl) Then
                    elenco.Opzione("Togli dai preferiti", Sub()
                                                              ImpostaPreferiti(New List(Of PodcastInfo) From {podcast}, False)
                                                              elenco.Chiudi()
                                                              MostraEpisodi(titolo, voci, podcast, dalPiuVecchio, sonoRecenti)
                                                          End Sub)
                Else
                    elenco.Opzione("Aggiungi ai preferiti", Sub()
                                                                ImpostaPreferiti(New List(Of PodcastInfo) From {podcast}, True)
                                                                elenco.Chiudi()
                                                                MostraEpisodi(titolo, voci, podcast, dalPiuVecchio, sonoRecenti)
                                                            End Sub)
                End If
                elenco.Opzione(If(dalPiuVecchio, "Dal più recente", "Dal più vecchio"), Sub()
                                                                                          elenco.Chiudi()
                                                                                          MostraEpisodi(titolo, voci, podcast, Not dalPiuVecchio, sonoRecenti)
                                                                                      End Sub)
            End If
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraEpisodi: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function Presi(voci As List(Of VoceEpisodio), ids As List(Of String)) As List(Of VoceEpisodio)
        Dim r As New List(Of VoceEpisodio)()
        If ids Is Nothing Then Return r
        For Each id As String In ids
            Dim n As Integer
            If Integer.TryParse(id, n) AndAlso n >= 0 AndAlso n < voci.Count Then r.Add(voci(n))
        Next
        Return r
    End Function

    Private Sub InformazioniEpisodio(v As VoceEpisodio)
        Dim righe As New List(Of String)()
        righe.Add(v.Episodio.Titolo)
        righe.Add("Podcast: " & v.Podcast.Titolo)
        Dim d As String = v.Episodio.DataParlata()
        If d <> "" Then righe.Add("Pubblicato il " & d)
        Dim t As String = PodcastRete.DurataParlata(v.Episodio.DurataSecondi)
        If t <> "" Then righe.Add("Durata: " & t)
        If v.Episodio.Stagione > 0 Then righe.Add("Stagione " & v.Episodio.Stagione.ToString())
        If v.Episodio.Numero > 0 Then righe.Add("Episodio " & v.Episodio.Numero.ToString())
        If v.Episodio.Dimensione > 0 Then righe.Add("Dimensione: " & (v.Episodio.Dimensione / 1048576.0).ToString("0.0") & " megabyte")
        If v.Episodio.Descrizione <> "" Then righe.Add(Taglia(v.Episodio.Descrizione, 2500))
        MediaUtil.Messaggio(String.Join(vbCrLf, righe))
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Ascolto
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Ascolta gli episodi nell'ordine dato: se uno è stato scaricato si usa il file, altrimenti si ascolta da internet.</summary>
    Private Sub Riproduci(titolo As String, voci As List(Of VoceEpisodio))
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return
            Dim indirizzi As New List(Of String)()
            Dim nomi As New List(Of String)()
            ' i file scaricati si cercano una volta sola, anche con migliaia di episodi
            Dim locali As New Dictionary(Of String, String)()
            For Each s As EpisodioScaricato In _archivio.Scaricati()
                locali(s.FeedUrl & "|" & s.Guid) = s.Percorso
            Next
            For Each v As VoceEpisodio In voci
                Dim locale As String = Nothing
                locali.TryGetValue(v.Podcast.FeedUrl & "|" & v.Episodio.Guid, locale)
                indirizzi.Add(If(Not String.IsNullOrEmpty(locale), locale, v.Episodio.Url))
                nomi.Add(v.Episodio.Titolo & ", " & v.Podcast.Titolo)
            Next
            ' con un elenco lungo (Riproduci tutti) conta come ascoltato solo il primo, quello che parte
            For Each v As VoceEpisodio In voci.Take(If(voci.Count > 20, 1, voci.Count))
                _archivio.RegistraAscolto(v.Podcast, v.Episodio)
            Next
            Dim titoloLettore As String = If(voci.Count = 1, voci(0).Podcast.Titolo, titolo)
            RiproduzioneMedia.Riproduci(titoloLettore, indirizzi, nomi)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.Riproduci: " & ex.ToString())
            MediaUtil.Messaggio("Non è stato possibile avviare l'ascolto.")
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Scaricamento
    ' ------------------------------------------------------------------------------------------

    Private Async Sub ScaricaEpisodi(voci As List(Of VoceEpisodio))
        Dim processo As WGProcessoInCorso = Nothing
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return
            Dim annullamento As New CancellationTokenSource()
            Dim annullato As Boolean = False
            Dim scaricati As Integer = 0
            Dim giaPresenti As Integer = 0
            Dim falliti As Integer = 0
            Dim ultimoErrore As String = ""

            processo = New WGProcessoInCorso()
            AddHandler processo.cancella, Sub()
                                              annullato = True
                                              annullamento.Cancel()
                                          End Sub
            processo.Avvia("Scarico " & voci.Count.ToString() & If(voci.Count = 1, " episodio", " episodi"), True)

            For i As Integer = 0 To voci.Count - 1
                If annullato Then Exit For
                Dim v As VoceEpisodio = voci(i)
                If _archivio.PercorsoScaricato(v.Podcast.FeedUrl, v.Episodio.Guid) <> "" Then
                    giaPresenti += 1
                    Continue For
                End If
                processo.AggiornaStato((i + 1).ToString() & " di " & voci.Count.ToString() & ", " & v.Episodio.Titolo)
                processo.AggiornaPercentuale(CInt(i * 100 / voci.Count))
                Dim percorso As String = PodcastArchivio.PercorsoPer(v.Podcast, v.Episodio)
                Try
                    Dim dimensione As Long = Await Task.Run(Function() PodcastRete.ScaricaAsync(v.Episodio.Url, percorso, annullamento.Token))
                    _archivio.RegistraScaricato(v.Podcast, v.Episodio, percorso, dimensione)
                    scaricati += 1
                Catch ex As OperationCanceledException
                    annullato = True
                Catch ex As PodcastEccezione
                    falliti += 1
                    ultimoErrore = ex.Message
                Catch ex As Exception
                    MediaUtil.Log("MenuPodcast.ScaricaEpisodi: " & ex.ToString())
                    falliti += 1
                    ultimoErrore = "Errore durante lo scaricamento."
                End Try
            Next

            processo.Fine()
            processo = Nothing

            Dim esito As String = "Scaricati " & scaricati.ToString() & If(scaricati = 1, " episodio.", " episodi.")
            If giaPresenti > 0 Then esito &= " Già presenti: " & giaPresenti.ToString() & "."
            If annullato Then esito &= " Scaricamento annullato."
            If falliti > 0 Then esito &= " Non riusciti: " & falliti.ToString() & ". " & ultimoErrore
            MediaUtil.Messaggio(esito)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.ScaricaEpisodi: " & ex.ToString())
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch
            End Try
            MediaUtil.Messaggio("Si è verificato un errore durante lo scaricamento.")
        End Try
    End Sub

    Private Sub EliminaDownload(voci As List(Of VoceEpisodio), elenco As ElencoMedia, titolo As String, tutte As List(Of VoceEpisodio), podcast As PodcastInfo, dalPiuVecchio As Boolean, sonoRecenti As Boolean)
        Try
            Dim daTogliere As New List(Of VoceEpisodio)()
            For Each v As VoceEpisodio In voci
                If _archivio.PercorsoScaricato(v.Podcast.FeedUrl, v.Episodio.Guid) <> "" Then daTogliere.Add(v)
            Next
            If daTogliere.Count = 0 Then
                MediaUtil.Messaggio("Gli episodi scelti non sono scaricati.")
                Return
            End If
            WGDomanda.Chiedi(If(daTogliere.Count = 1, "Eliminare dal computer l'episodio " & daTogliere(0).Episodio.Titolo & "?", "Eliminare dal computer " & daTogliere.Count.ToString() & " episodi?"),
                             Sub()
                                 For Each v As VoceEpisodio In daTogliere
                                     _archivio.EliminaScaricato(v.Podcast.FeedUrl, v.Episodio.Guid, _archivio.PercorsoScaricato(v.Podcast.FeedUrl, v.Episodio.Guid))
                                 Next
                                 elenco.Chiudi()
                                 MostraEpisodi(titolo, tutte, podcast, dalPiuVecchio, sonoRecenti)
                                 MediaUtil.Messaggio(If(daTogliere.Count = 1, "Episodio eliminato.", "Episodi eliminati."))
                             End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.EliminaDownload: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Nuovi episodi, recenti e scaricati
    ' ------------------------------------------------------------------------------------------

    Private Async Sub MostraNuovi()
        Try
            Dim seguiti As List(Of PodcastInfo) = _archivio.Seguiti()
            If seguiti.Count = 0 Then
                MediaUtil.Messaggio("Non segui ancora nessun podcast.")
                Return
            End If
            Dim voci As List(Of VoceEpisodio) = Await ProvaAsync("Cerco i nuovi episodi", Function() RaccogliNuoviAsync(seguiti))
            If voci Is Nothing Then Return
            If voci.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono nuovi episodi nei podcast che segui.")
                Return
            End If
            MostraEpisodi("Nuovi episodi, " & voci.Count.ToString(), voci, Nothing, False, False)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraNuovi: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Gli ultimi episodi di ogni podcast seguito che non sono ancora stati ascoltati, i più recenti per primi.</summary>
    Private Async Function RaccogliNuoviAsync(seguiti As List(Of PodcastInfo)) As Task(Of List(Of VoceEpisodio))
        Dim limite As New SemaphoreSlim(4)
        Dim compiti As New List(Of Task(Of List(Of VoceEpisodio)))()
        For Each p As PodcastInfo In seguiti
            Dim podcast As PodcastInfo = p
            compiti.Add(Task.Run(Async Function() As Task(Of List(Of VoceEpisodio))
                                     Dim r As New List(Of VoceEpisodio)()
                                     Await limite.WaitAsync().ConfigureAwait(False)
                                     Try
                                         Using fine As New CancellationTokenSource(TimeSpan.FromSeconds(25))
                                             Dim feed As FeedPodcast = Await PodcastRete.LeggiFeedAsync(podcast.FeedUrl, fine.Token).ConfigureAwait(False)
                                             Dim ascoltati As HashSet(Of String) = _archivio.Ascoltati(podcast.FeedUrl)
                                             feed.Info.FeedUrl = podcast.FeedUrl
                                             If feed.Info.Titolo = "" Then feed.Info.Titolo = podcast.Titolo
                                             For Each e As EpisodioPodcast In feed.Episodi.Take(5)
                                                 If Not ascoltati.Contains(e.Guid) Then r.Add(New VoceEpisodio With {.Podcast = feed.Info, .Episodio = e})
                                             Next
                                         End Using
                                     Catch ex As Exception
                                         ' un podcast che non risponde si salta: gli altri si vedono lo stesso
                                         MediaUtil.Log("MenuPodcast.RaccogliNuovi (" & podcast.Titolo & "): " & ex.Message)
                                     Finally
                                         limite.Release()
                                     End Try
                                     Return r
                                 End Function))
        Next
        Dim tutti As New List(Of VoceEpisodio)()
        For Each c As Task(Of List(Of VoceEpisodio)) In compiti
            tutti.AddRange(Await c.ConfigureAwait(False))
        Next
        Return tutti.OrderByDescending(Function(v) If(v.Episodio.Data.HasValue, v.Episodio.Data.Value, DateTime.MinValue)).Take(60).ToList()
    End Function

    Private Sub MostraRecenti()
        Try
            Dim recenti As List(Of AscoltoPodcast) = _archivio.Recenti()
            If recenti.Count = 0 Then
                MediaUtil.Messaggio("Non hai ancora ascoltato episodi.")
                Return
            End If
            Dim voci As New List(Of VoceEpisodio)()
            For Each a As AscoltoPodcast In recenti
                Dim p As New PodcastInfo()
                p.FeedUrl = a.FeedUrl
                p.Titolo = a.Podcast
                Dim e As New EpisodioPodcast()
                e.Guid = a.Guid
                e.Titolo = a.Titolo
                e.Url = a.Url
                e.Data = a.DataEpisodio
                voci.Add(New VoceEpisodio With {.Podcast = p, .Episodio = e})
            Next
            MostraEpisodi("Recenti, " & voci.Count.ToString(), voci, Nothing, False, True)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub SvuotaRecenti(elenco As ElencoMedia)
        Try
            WGDomanda.Chiedi("Svuotare l'elenco degli episodi recenti?", Sub()
                                                                            _archivio.SvuotaRecenti()
                                                                            elenco.Chiudi()
                                                                            MediaUtil.Messaggio("Elenco svuotato.")
                                                                        End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.SvuotaRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraScaricati()
        Try
            Dim lista As List(Of EpisodioScaricato) = _archivio.Scaricati()
            If lista.Count = 0 Then
                MediaUtil.Messaggio("Non hai ancora scaricato episodi.")
                Return
            End If
            Dim elenco As New ElencoMedia("Scaricati, " & lista.Count.ToString(), True)
            elenco.AggiungiMolti(lista.Select(Function(e, i) New KeyValuePair(Of String, String)(e.Etichetta, i.ToString())))

            Dim scelti As Func(Of List(Of String), List(Of EpisodioScaricato)) =
                Function(ids As List(Of String)) As List(Of EpisodioScaricato)
                    Dim r As New List(Of EpisodioScaricato)()
                    For Each id As String In ids
                        Dim n As Integer
                        If Integer.TryParse(id, n) AndAlso n >= 0 AndAlso n < lista.Count Then r.Add(lista(n))
                    Next
                    Return r
                End Function

            Dim riproduci As Action(Of List(Of EpisodioScaricato)) =
                Sub(episodi As List(Of EpisodioScaricato))
                    If episodi.Count = 0 Then Return
                    For Each e As EpisodioScaricato In episodi
                        Dim p As New PodcastInfo()
                        p.FeedUrl = e.FeedUrl
                        p.Titolo = e.Podcast
                        Dim ep As New EpisodioPodcast()
                        ep.Guid = e.Guid
                        ep.Titolo = e.Titolo
                        ep.Data = e.DataEpisodio
                        _archivio.RegistraAscolto(p, ep)
                    Next
                    RiproduzioneMedia.Riproduci(If(episodi.Count = 1, episodi(0).Podcast, "Episodi scaricati"), episodi.Select(Function(e) e.Percorso).ToList(), episodi.Select(Function(e) e.Titolo & ", " & e.Podcast).ToList())
                End Sub

            AddHandler elenco.Scelto, Sub(id As String) riproduci(scelti(New List(Of String) From {id}))
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String)) riproduci(scelti(ids))
            elenco.Opzione("Riproduci tutti", Sub() riproduci(lista))
            elenco.Opzione("Informazioni", Sub()
                                               Dim s As List(Of EpisodioScaricato) = scelti(elenco.Selezionati())
                                               If s.Count = 0 Then Return
                                               Dim e As EpisodioScaricato = s(0)
                                               Dim righe As New List(Of String)()
                                               righe.Add(e.Titolo)
                                               righe.Add("Podcast: " & e.Podcast)
                                               If e.DataEpisodio.HasValue Then righe.Add("Pubblicato il " & e.DataEpisodio.Value.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("it-IT")))
                                               If e.Dimensione > 0 Then righe.Add("Dimensione: " & (e.Dimensione / 1048576.0).ToString("0.0") & " megabyte")
                                               righe.Add("Si trova in " & e.Percorso)
                                               MediaUtil.Messaggio(String.Join(vbCrLf, righe))
                                           End Sub)
            elenco.Opzione("Elimina dal computer", Sub()
                                                       Dim s As List(Of EpisodioScaricato) = scelti(elenco.Selezionati())
                                                       If s.Count = 0 Then Return
                                                       WGDomanda.Chiedi(If(s.Count = 1, "Eliminare dal computer l'episodio " & s(0).Titolo & "?", "Eliminare dal computer " & s.Count.ToString() & " episodi?"),
                                                                        Sub()
                                                                            For Each e As EpisodioScaricato In s
                                                                                _archivio.EliminaScaricato(e.FeedUrl, e.Guid, e.Percorso)
                                                                            Next
                                                                            elenco.Chiudi()
                                                                            MediaUtil.Messaggio(If(s.Count = 1, "Episodio eliminato.", "Episodi eliminati."))
                                                                        End Sub)
                                                   End Sub)
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.MostraScaricati: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire gli episodi scaricati.")
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Opzioni
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Il paese delle classifiche e della ricerca.</summary>
    Private Sub ScegliPaese()
        Try
            Dim attuale As String = PodcastImpostazioni.Paese()
            Dim menu As New WGMenu()
            Dim posizione As Integer = 0
            For i As Integer = 0 To PodcastRete.Paesi.Length - 1
                Dim codice As String = PodcastRete.Paesi(i).Key
                Dim nome As String = PodcastRete.Paesi(i).Value
                If String.Equals(codice, attuale, StringComparison.OrdinalIgnoreCase) Then posizione = i
                menu.aggiungi(nome, Sub()
                                        PodcastImpostazioni.ImpostaPaese(codice)
                                        menu.chiudi()
                                        MediaUtil.Messaggio("Paese dei podcast: " & nome & ".")
                                    End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Paese dei podcast", True)
        Catch ex As Exception
            MediaUtil.Log("MenuPodcast.ScegliPaese: " & ex.ToString())
        End Try
    End Sub

End Class
