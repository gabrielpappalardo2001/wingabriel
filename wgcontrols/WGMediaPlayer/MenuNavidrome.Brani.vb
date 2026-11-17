Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports wgcontrols
Imports WGNavidrome

' Brani e playlist del server Navidrome.
Partial Friend Class MenuNavidrome

    ' ------------------------------------------------------------------------------------------
    ' Elenco di brani
    ' ------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Un elenco di brani. Invio su un brano riproduce quel brano e basta; con più brani segnati
    ''' riproduce quelli. Per ascoltare tutto l'elenco c'è Riproduci tutti nelle opzioni. Se idPlaylist
    ''' non è vuoto l'elenco è una playlist e si possono togliere brani. opzioniExtra permette a chi
    ''' apre l'elenco di aggiungere voci sue.
    ''' </summary>
    Private Sub MostraBrani(titolo As String, brani As List(Of NavidromeBrano), Optional idPlaylist As String = "", Optional opzioniExtra As Action(Of ElencoMedia) = Nothing)
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim elenco As New ElencoMedia(titolo & ", " & brani.Count.ToString() & If(brani.Count = 1, " brano", " brani"), True)
            ' L'identificativo di ogni voce è la posizione nell'elenco: lo stesso brano può comparire due volte.
            elenco.AggiungiMolti(brani.Select(Function(b, i) New KeyValuePair(Of String, String)(b.Descrizione, i.ToString())))

            AddHandler elenco.Scelto, Sub(id As String)
                                          ' Il brano si prende dagli elementi selezionati (il primo); se mancano, da quello dell'evento.
                                          Dim scelti As List(Of NavidromeBrano) = BraniScelti(brani, elenco.Selezionati())
                                          If scelti.Count = 0 Then scelti = BraniScelti(brani, New List(Of String) From {id})
                                          If scelti.Count > 0 Then RiproduciBrani(titolo, New List(Of NavidromeBrano) From {scelti(0)}, 0)
                                      End Sub
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String))
                                             RiproduciBrani(titolo, BraniScelti(brani, ids), 0)
                                         End Sub

            elenco.Opzione("Riproduci selezione", Sub() RiproduciBrani(titolo, BraniScelti(brani, elenco.Selezionati()), 0))
            elenco.Opzione("Riproduci tutti", Sub() RiproduciBrani(titolo, brani, 0))
            elenco.Opzione("Riproduci la selezione in ordine casuale", Sub() RiproduciBrani(titolo, Rimescola(BraniScelti(brani, elenco.Selezionati())), 0))
            elenco.Opzione("Riproduci tutti in ordine casuale", Sub() RiproduciBrani(titolo, Rimescola(brani), 0))
            elenco.Opzione("Scarica", Sub() ScaricaBrani(BraniScelti(brani, elenco.Selezionati())))
            elenco.Opzione("Scarica tutti", Sub() ScaricaBrani(brani))
            elenco.Opzione("Aggiungi ai preferiti", Sub() ImpostaPreferitiBrani(BraniScelti(brani, elenco.Selezionati()), True))
            elenco.Opzione("Togli dai preferiti", Sub() ImpostaPreferitiBrani(BraniScelti(brani, elenco.Selezionati()), False))
            elenco.Opzione("Aggiungi a una playlist", Sub() ScegliPlaylist(BraniScelti(brani, elenco.Selezionati()).Select(Function(b) b.Id).ToList()))
            elenco.Opzione("Dai un voto", Sub() ScegliVoto(BraniScelti(brani, elenco.Selezionati())))
            elenco.Opzione("Informazioni", Sub() MostraInformazioniBrano(BraniScelti(brani, elenco.Selezionati())))
            elenco.Opzione("Apri l'album", Sub() ApriAlbumDelBrano(BraniScelti(brani, elenco.Selezionati())))
            elenco.Opzione("Apri l'artista", Sub() ApriArtistaDelBrano(BraniScelti(brani, elenco.Selezionati())))
            If idPlaylist <> "" Then
                elenco.Opzione("Rimuovi dalla playlist", Sub() RimuoviDallaPlaylist(idPlaylist, titolo, elenco, elenco.Selezionati()))
            End If
            If opzioniExtra IsNot Nothing Then opzioniExtra(elenco)
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraBrani: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' L'intera libreria in un solo elenco. Il download gira in un thread a parte, con l'attesa a schermo,
    ''' e finisce quando sono arrivati tutti i brani.
    ''' </summary>
    Private Sub MostraTuttiIBrani()
        ApriTuttiIBrani(False)
    End Sub

    ''' <summary>
    ''' Apre "Tutti i brani" dalla copia salvata nel database locale; se non c'è, o se si chiede di
    ''' aggiornarla, scarica prima l'elenco dal server e lo salva.
    ''' </summary>
    Private Async Sub ApriTuttiIBrani(daServer As Boolean)
        Try
            Dim cache As New NavidromeCache(_client.IdServer)
            Dim brani As List(Of NavidromeBrano) = Nothing
            If Not daServer Then brani = cache.LeggiBrani()
            If brani Is Nothing OrElse brani.Count = 0 Then
                brani = Await ProvaAsync("Scarico tutti i brani", Function() Task.Run(Function() ScaricaEMemorizzaAsync(cache)))
            End If
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("La libreria del server non contiene brani.")
                Return
            End If
            Dim data As DateTime? = cache.DataAggiornamento()
            Dim titolo As String = "Tutti i brani" & If(data.HasValue, ", aggiornati il " & data.Value.ToString("dd/MM/yyyy"), "")
            MostraBrani(titolo, brani, "", Sub(elenco As ElencoMedia)
                                              elenco.Opzione("Aggiorna l'elenco dal server", Sub()
                                                                                                 elenco.Chiudi()
                                                                                                 ApriTuttiIBrani(True)
                                                                                             End Sub)
                                          End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraTuttiIBrani: " & ex.ToString())
        End Try
    End Sub

    Private Async Function ScaricaEMemorizzaAsync(cache As NavidromeCache) As Task(Of List(Of NavidromeBrano))
        Dim brani As List(Of NavidromeBrano) = Await _client.TuttiIBraniAsync()
        If brani.Count > 0 Then cache.SalvaBrani(brani)
        Return brani
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Scaricamento sul computer
    ' ------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Scarica i brani nella cartella Scaricati di Navidrome, nei dati di WinGabriel, e li registra nella
    ''' libreria Scaricati. Con l'attesa a schermo e la possibilità di annullare tra un brano e l'altro.
    ''' </summary>
    Private Async Sub ScaricaBrani(brani As List(Of NavidromeBrano))
        Dim processo As WGProcessoInCorso = Nothing
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim archivio As New NavidromeScaricati()
            Dim idServer As String = _client.IdServer
            Dim annullato As Boolean = False
            Dim scaricati As Integer = 0
            Dim giaPresenti As Integer = 0
            Dim falliti As Integer = 0
            Dim ultimoErrore As String = ""

            processo = New WGProcessoInCorso()
            AddHandler processo.cancella, Sub() annullato = True
            processo.Avvia("Scarico " & brani.Count.ToString() & If(brani.Count = 1, " brano", " brani"), True)

            For i As Integer = 0 To brani.Count - 1
                If annullato Then Exit For
                Dim b As NavidromeBrano = brani(i)
                If archivio.EGiaScaricato(idServer, b.Id) Then
                    giaPresenti += 1
                    Continue For
                End If
                processo.AggiornaStato((i + 1).ToString() & " di " & brani.Count.ToString() & ", " & b.Titolo)
                Dim percorso As String = NavidromeScaricati.PercorsoPer(b)
                Try
                    Await Task.Run(Function() _client.ScaricaAsync(b.Id, percorso))
                    archivio.Registra(idServer, b, percorso)
                    scaricati += 1
                Catch ex As NavidromeEccezione
                    falliti += 1
                    ultimoErrore = ex.Message
                Catch ex As Exception
                    MediaUtil.Log("MenuNavidrome.ScaricaBrani: " & ex.ToString())
                    falliti += 1
                    ultimoErrore = "Errore durante lo scaricamento."
                End Try
            Next

            processo.Fine()
            processo = Nothing

            Dim esito As String = "Scaricati " & scaricati.ToString() & If(scaricati = 1, " brano.", " brani.")
            If giaPresenti > 0 Then esito &= " Già presenti: " & giaPresenti.ToString() & "."
            If annullato Then esito &= " Scaricamento annullato."
            If falliti > 0 Then esito &= " Non riusciti: " & falliti.ToString() & ". " & ultimoErrore
            MediaUtil.Messaggio(esito)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScaricaBrani: " & ex.ToString())
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch
            End Try
            MediaUtil.Messaggio("Si è verificato un errore durante lo scaricamento.")
        End Try
    End Sub

    Private Shared ReadOnly _casuale As New Random()

    ''' <summary>Una copia della lista in ordine casuale.</summary>
    Private Shared Function Rimescola(brani As List(Of NavidromeBrano)) As List(Of NavidromeBrano)
        Dim copia As New List(Of NavidromeBrano)(brani)
        For i As Integer = copia.Count - 1 To 1 Step -1
            Dim j As Integer = _casuale.Next(i + 1)
            Dim t As NavidromeBrano = copia(i)
            copia(i) = copia(j)
            copia(j) = t
        Next
        Return copia
    End Function

    Private Shared Function BraniScelti(brani As List(Of NavidromeBrano), ids As List(Of String)) As List(Of NavidromeBrano)
        Dim risultato As New List(Of NavidromeBrano)
        If ids Is Nothing Then Return risultato
        For Each id As String In ids
            Dim posizione As Integer
            If Integer.TryParse(id, posizione) AndAlso posizione >= 0 AndAlso posizione < brani.Count Then
                risultato.Add(brani(posizione))
            End If
        Next
        Return risultato
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Riproduzione
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Riproduce i brani dall'inizio indicato in poi.</summary>
    Private Sub RiproduciBrani(titolo As String, brani As List(Of NavidromeBrano), inizio As Integer)
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim daSuonare As List(Of NavidromeBrano) = brani.Skip(Math.Max(inizio, 0)).ToList()
            If daSuonare.Count = 0 Then Return
            Dim indirizzi As List(Of String) = _client.IndirizziStream(daSuonare)
            Dim nomi As List(Of String) = daSuonare.Select(Function(b) NomeLeggibile(b)).ToList()
            RiproduzioneMedia.Riproduci(titolo, indirizzi, nomi)
            SegnaAscoltato(daSuonare(0))
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RiproduciBrani: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore durante l'avvio della riproduzione.")
        End Try
    End Sub

    Private Shared Function NomeLeggibile(b As NavidromeBrano) As String
        If String.IsNullOrWhiteSpace(b.Artista) Then Return b.Titolo
        Return b.Titolo & ", " & b.Artista
    End Function

    ''' <summary>
    ''' Dice al server che il brano è partito, così compare tra gli ascoltati di recente. Vale solo per il
    ''' primo brano: il lettore non segnala al Media player quando passa al successivo.
    ''' </summary>
    Private Async Sub SegnaAscoltato(brano As NavidromeBrano)
        Try
            Await _client.SegnaAscoltatoAsync(brano.Id)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.SegnaAscoltato: " & ex.Message)
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Preferiti, voti, informazioni
    ' ------------------------------------------------------------------------------------------

    Private Async Sub ImpostaPreferitiBrani(brani As List(Of NavidromeBrano), preferito As Boolean)
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim ids As List(Of String) = brani.Select(Function(b) b.Id).ToList()
            Dim ok As Boolean = Await ProvaAzioneAsync("Aggiorno i preferiti", Function() _client.ImpostaPreferitoBraniAsync(ids, preferito))
            If ok Then
                For Each b As NavidromeBrano In brani
                    b.Preferito = preferito
                Next
                MediaUtil.Messaggio(If(preferito, "Aggiunti ai preferiti.", "Tolti dai preferiti."))
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ImpostaPreferitiBrani: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliVoto(brani As List(Of NavidromeBrano))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim voci As New List(Of KeyValuePair(Of String, Action))
            For voto As Integer = 5 To 1 Step -1
                Dim v As Integer = voto
                voci.Add(MediaUtil.Voce(v.ToString() & If(v = 1, " stella", " stelle"), Sub() ImpostaVoto(brani, v)))
            Next
            voci.Add(MediaUtil.Voce("Togli il voto", Sub() ImpostaVoto(brani, 0)))
            MediaUtil.Scegli("Voto", voci)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScegliVoto: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ImpostaVoto(brani As List(Of NavidromeBrano), voto As Integer)
        Try
            Dim ok As Boolean = Await ProvaAzioneAsync("Salvo il voto", Function() ImpostaVotoAsync(brani, voto))
            If ok Then
                For Each b As NavidromeBrano In brani
                    b.Voto = voto
                Next
                MediaUtil.Messaggio(If(voto = 0, "Voto tolto.", "Voto salvato: " & voto.ToString() & If(voto = 1, " stella.", " stelle.")))
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ImpostaVoto: " & ex.ToString())
        End Try
    End Sub

    Private Async Function ImpostaVotoAsync(brani As List(Of NavidromeBrano), voto As Integer) As Task
        For Each b As NavidromeBrano In brani
            Await _client.ImpostaVotoAsync(b.Id, voto)
        Next
    End Function

    Private Sub MostraInformazioniBrano(brani As List(Of NavidromeBrano))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim b As NavidromeBrano = brani(0)
            Dim righe As New List(Of String)
            righe.Add("Titolo: " & b.Titolo)
            If b.Artista <> "" Then righe.Add("Artista: " & b.Artista)
            If b.Album <> "" Then righe.Add("Album: " & b.Album)
            If b.Anno > 0 Then righe.Add("Anno: " & b.Anno.ToString())
            If b.Genere <> "" Then righe.Add("Genere: " & b.Genere)
            If b.Traccia > 0 Then righe.Add("Traccia: " & b.Traccia.ToString() & If(b.Disco > 1, ", disco " & b.Disco.ToString(), ""))
            If b.DurataSecondi > 0 Then righe.Add("Durata: " & NavidromeDurata(b.DurataSecondi))
            If b.Formato <> "" Then righe.Add("Formato: " & b.Formato & If(b.BitRate > 0, ", " & b.BitRate.ToString() & " chilobit al secondo", ""))
            righe.Add("Preferito: " & If(b.Preferito, "sì", "no"))
            If b.Voto > 0 Then righe.Add("Voto: " & b.Voto.ToString() & If(b.Voto = 1, " stella", " stelle"))
            If b.NumeroAscolti > 0 Then righe.Add("Ascolti: " & b.NumeroAscolti.ToString())
            MediaUtil.Messaggio(String.Join(vbCrLf, righe))
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraInformazioniBrano: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function NavidromeDurata(secondi As Integer) As String
        Dim t As TimeSpan = TimeSpan.FromSeconds(secondi)
        If t.TotalHours >= 1 Then
            Return CInt(Math.Floor(t.TotalHours)).ToString() & " ore, " & t.Minutes.ToString() & " minuti e " & t.Seconds.ToString() & " secondi"
        End If
        Return t.Minutes.ToString() & " minuti e " & t.Seconds.ToString() & " secondi"
    End Function

    Private Async Sub ApriAlbumDelBrano(brani As List(Of NavidromeBrano))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim b As NavidromeBrano = brani(0)
            If b.AlbumId = "" Then
                MediaUtil.Messaggio("Per questo brano il server non indica l'album.")
                Return
            End If
            Await ApriAlbumPerIdAsync(b.AlbumId, If(b.Album <> "", b.Album, "Album"))
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriAlbumDelBrano: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriArtistaDelBrano(brani As List(Of NavidromeBrano))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim b As NavidromeBrano = brani(0)
            If b.ArtistaId = "" Then
                MediaUtil.Messaggio("Per questo brano il server non indica l'artista.")
                Return
            End If
            Dim perId As New Dictionary(Of String, NavidromeArtista)
            Dim artista As New NavidromeArtista With {.Id = b.ArtistaId, .Nome = b.Artista}
            perId.Add(artista.Id, artista)
            ApriArtista(perId, artista.Id)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriArtistaDelBrano: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Playlist
    ' ------------------------------------------------------------------------------------------

    Private Sub MostraMenuPlaylist()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Le playlist del server", AddressOf MostraPlaylist)
            menu.aggiungi("Nuova playlist", AddressOf NuovaPlaylistVuota)
            menu.avvia("Playlist")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraMenuPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraPlaylist()
        Try
            Dim playlist As List(Of NavidromePlaylist) = Await ProvaAsync("Carico le playlist", Function() _client.PlaylistAsync())
            If playlist Is Nothing Then Return
            If playlist.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono playlist sul server. Creane una da Nuova playlist.")
                Return
            End If

            Dim elenco As New ElencoMedia("Playlist, " & playlist.Count.ToString(), False)
            Dim perId As New Dictionary(Of String, NavidromePlaylist)
            For Each p As NavidromePlaylist In playlist
                If perId.ContainsKey(p.Id) Then Continue For
                perId.Add(p.Id, p)
                elenco.Aggiungi(p.Descrizione, p.Id)
            Next

            AddHandler elenco.Scelto, Sub(id As String)
                                          Dim scelta As NavidromePlaylist = Nothing
                                          If perId.TryGetValue(id, scelta) Then ApriPlaylist(scelta)
                                      End Sub
            elenco.Opzione("Riproduci la playlist", Sub() RiproduciPlaylist(PrimaPlaylist(perId, elenco.Selezionati())))
            elenco.Opzione("Rinomina", Sub() RinominaPlaylist(PrimaPlaylist(perId, elenco.Selezionati()), elenco))
            elenco.Opzione("Elimina", Sub() EliminaPlaylist(PrimaPlaylist(perId, elenco.Selezionati()), elenco))
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function PrimaPlaylist(perId As Dictionary(Of String, NavidromePlaylist), ids As List(Of String)) As NavidromePlaylist
        If ids Is Nothing OrElse ids.Count = 0 Then Return Nothing
        Dim p As NavidromePlaylist = Nothing
        If perId.TryGetValue(ids(0), p) Then Return p
        Return Nothing
    End Function

    Private Async Function CaricaBraniPlaylistAsync(playlist As NavidromePlaylist) As Task(Of List(Of NavidromeBrano))
        Return Await ProvaAsync("Carico " & playlist.Nome, Function() _client.BraniDiPlaylistAsync(playlist.Id))
    End Function

    Private Async Sub ApriPlaylist(playlist As NavidromePlaylist)
        Try
            Dim brani As List(Of NavidromeBrano) = Await CaricaBraniPlaylistAsync(playlist)
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("La playlist è vuota. Aggiungi brani da un album o da una ricerca, con l'opzione Aggiungi a una playlist.")
                Return
            End If
            MostraBrani(playlist.Nome, brani, playlist.Id)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub RiproduciPlaylist(playlist As NavidromePlaylist)
        Try
            If playlist Is Nothing Then Return
            Dim brani As List(Of NavidromeBrano) = Await CaricaBraniPlaylistAsync(playlist)
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("La playlist è vuota.")
                Return
            End If
            RiproduciBrani(playlist.Nome, brani, 0)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RiproduciPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Sub NuovaPlaylistVuota()
        MediaUtil.ChiediTesto("Nome della nuova playlist", "", Async Sub(nome)
                                                                  Try
                                                                      Dim ok As Boolean = Await ProvaAzioneAsync("Creo la playlist", Function() _client.CreaPlaylistAsync(nome, Nothing))
                                                                      If ok Then MediaUtil.Messaggio("Playlist " & nome & " creata.")
                                                                  Catch ex As Exception
                                                                      MediaUtil.Log("MenuNavidrome.NuovaPlaylistVuota: " & ex.ToString())
                                                                  End Try
                                                              End Sub)
    End Sub

    Private Sub RinominaPlaylist(playlist As NavidromePlaylist, elenco As ElencoMedia)
        Try
            If playlist Is Nothing Then Return
            MediaUtil.ChiediTesto("Nuovo nome della playlist", playlist.Nome, Async Sub(nome)
                                                                                  Try
                                                                                      Dim ok As Boolean = Await ProvaAzioneAsync("Rinomino la playlist", Function() _client.RinominaPlaylistAsync(playlist.Id, nome))
                                                                                      If ok Then
                                                                                          elenco.Chiudi()
                                                                                          MediaUtil.Messaggio("Playlist rinominata in " & nome & ".")
                                                                                      End If
                                                                                  Catch ex As Exception
                                                                                      MediaUtil.Log("MenuNavidrome.RinominaPlaylist: " & ex.ToString())
                                                                                  End Try
                                                                              End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RinominaPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Sub EliminaPlaylist(playlist As NavidromePlaylist, elenco As ElencoMedia)
        Try
            If playlist Is Nothing Then Return
            WGDomanda.Chiedi("Eliminare la playlist " & playlist.Nome & "?",
                             Async Sub()
                                 Try
                                     Dim ok As Boolean = Await ProvaAzioneAsync("Elimino la playlist", Function() _client.EliminaPlaylistAsync(playlist.Id))
                                     If ok Then
                                         elenco.Chiudi()
                                         MediaUtil.Messaggio("Playlist eliminata.")
                                     End If
                                 Catch ex As Exception
                                     MediaUtil.Log("MenuNavidrome.EliminaPlaylist: " & ex.ToString())
                                 End Try
                             End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.EliminaPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub RimuoviDallaPlaylist(idPlaylist As String, titolo As String, elenco As ElencoMedia, ids As List(Of String))
        Try
            Dim posizioni As New List(Of Integer)
            For Each id As String In ids
                Dim p As Integer
                If Integer.TryParse(id, p) Then posizioni.Add(p)
            Next
            If posizioni.Count = 0 Then Return
            Dim ok As Boolean = Await ProvaAzioneAsync("Tolgo i brani dalla playlist", Function() _client.RimuoviDaPlaylistAsync(idPlaylist, posizioni))
            If ok Then
                elenco.Chiudi()
                MediaUtil.Messaggio("Brani tolti dalla playlist. Riaprila per vedere l'elenco aggiornato.")
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RimuoviDallaPlaylist: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Fa scegliere una playlist (o ne crea una nuova) e ci aggiunge i brani indicati.</summary>
    Private Async Sub ScegliPlaylist(idBrani As List(Of String))
        Try
            If idBrani Is Nothing OrElse idBrani.Count = 0 Then Return
            Dim playlist As List(Of NavidromePlaylist) = Await ProvaAsync("Carico le playlist", Function() _client.PlaylistAsync())
            If playlist Is Nothing Then Return

            Dim voci As New List(Of KeyValuePair(Of String, Action))
            voci.Add(MediaUtil.Voce("Nuova playlist", Sub() CreaPlaylistConBrani(idBrani)))
            For Each p As NavidromePlaylist In playlist
                Dim scelta As NavidromePlaylist = p
                voci.Add(MediaUtil.Voce(scelta.Nome, Sub() AggiungiAPlaylist(scelta, idBrani)))
            Next
            MediaUtil.Scegli("Aggiungi a una playlist", voci)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScegliPlaylist: " & ex.ToString())
        End Try
    End Sub

    Private Sub CreaPlaylistConBrani(idBrani As List(Of String))
        MediaUtil.ChiediTesto("Nome della nuova playlist", "", Async Sub(nome)
                                                                  Try
                                                                      Dim ok As Boolean = Await ProvaAzioneAsync("Creo la playlist", Function() _client.CreaPlaylistAsync(nome, idBrani))
                                                                      If ok Then MediaUtil.Messaggio("Playlist " & nome & " creata con " & idBrani.Count.ToString() & If(idBrani.Count = 1, " brano.", " brani."))
                                                                  Catch ex As Exception
                                                                      MediaUtil.Log("MenuNavidrome.CreaPlaylistConBrani: " & ex.ToString())
                                                                  End Try
                                                              End Sub)
    End Sub

    Private Async Sub AggiungiAPlaylist(playlist As NavidromePlaylist, idBrani As List(Of String))
        Try
            Dim ok As Boolean = Await ProvaAzioneAsync("Aggiungo alla playlist", Function() _client.AggiungiAPlaylistAsync(playlist.Id, idBrani))
            If ok Then MediaUtil.Messaggio(idBrani.Count.ToString() & If(idBrani.Count = 1, " brano aggiunto", " brani aggiunti") & " alla playlist " & playlist.Nome & ".")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.AggiungiAPlaylist: " & ex.ToString())
        End Try
    End Sub

End Class
