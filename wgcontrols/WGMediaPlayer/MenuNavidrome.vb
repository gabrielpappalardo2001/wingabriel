Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports wgcontrols
Imports WGNavidrome

''' <summary>
''' La libreria del server Navidrome: ricerca, artisti, album, generi, playlist, preferiti, radio e
''' gestione della libreria. Le schermate dei brani e delle playlist stanno in MenuNavidrome.Brani.vb.
''' </summary>
Partial Friend Class MenuNavidrome

    Private Const ALBUM_PER_PAGINA As Integer = 200
    Private Const BRANI_CASUALI As Integer = 50

    Private ReadOnly _client As NavidromeClient

    Public Sub New(configurazione As NavidromeConfigurazione)
        _client = configurazione.CreaClient()
    End Sub

    Public Sub Avvia()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Cerca", AddressOf Cerca)
            menu.aggiungi("Artisti", AddressOf MostraArtisti)
            menu.aggiungi("Album", AddressOf MostraTipiAlbum)
            menu.aggiungi("Tutti i brani", AddressOf MostraTuttiIBrani)
            menu.aggiungi("Generi", AddressOf MostraGeneri)
            menu.aggiungi("Playlist", AddressOf MostraMenuPlaylist)
            menu.aggiungi("Preferiti", AddressOf MostraPreferiti)
            menu.aggiungi("Brani casuali", AddressOf MostraBraniCasuali)
            menu.aggiungi("In ascolto ora", AddressOf MostraInAscolto)
            menu.aggiungi("Radio del server", AddressOf MostraRadio)
            menu.aggiungi("Gestione libreria", AddressOf MostraGestioneLibreria)
            menu.avvia("Navidrome")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.Avvia: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Aiutanti per le chiamate al server
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Esegue una chiamata con l'attesa a schermo. Se fallisce legge l'errore e restituisce Nothing.</summary>
    Private Async Function ProvaAsync(Of T)(attesa As String, lavoro As Func(Of Task(Of T))) As Task(Of T)
        Try
            Return Await WGProcessoInCorso.EseguiAsync(Of T)(attesa, lavoro)
        Catch ex As NavidromeEccezione
            MediaUtil.Messaggio(ex.Message)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ProvaAsync: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore parlando con il server Navidrome.")
        End Try
        Return Nothing
    End Function

    ''' <summary>Come ProvaAsync, per le operazioni senza risultato. Restituisce vero se è andata a buon fine.</summary>
    Private Async Function ProvaAzioneAsync(attesa As String, lavoro As Func(Of Task)) As Task(Of Boolean)
        Try
            Await WGProcessoInCorso.EseguiAsync(attesa, lavoro)
            Return True
        Catch ex As NavidromeEccezione
            MediaUtil.Messaggio(ex.Message)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ProvaAzioneAsync: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore parlando con il server Navidrome.")
        End Try
        Return False
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Ricerca
    ' ------------------------------------------------------------------------------------------

    Private Sub Cerca()
        MediaUtil.ChiediTesto("Cerca brani, album e artisti", "", Sub(testo) EseguiRicerca(testo))
    End Sub

    Private Async Sub EseguiRicerca(testo As String)
        Try
            Dim r As NavidromeRisultatiRicerca = Await ProvaAsync("Cerco " & testo, Function() _client.CercaAsync(testo))
            If r Is Nothing Then Return
            If r.Vuoto Then
                MediaUtil.Messaggio("Nessun risultato per " & testo & ".")
                Return
            End If

            Dim voci As New List(Of KeyValuePair(Of String, Action))
            If r.Brani.Count > 0 Then
                voci.Add(MediaUtil.Voce("Brani, " & r.Brani.Count.ToString(), Sub() MostraBrani("Brani trovati per " & testo, r.Brani)))
            End If
            If r.Album.Count > 0 Then
                voci.Add(MediaUtil.Voce("Album, " & r.Album.Count.ToString(), Sub() MostraAlbum("Album trovati per " & testo, r.Album, Nothing)))
            End If
            If r.Artisti.Count > 0 Then
                voci.Add(MediaUtil.Voce("Artisti, " & r.Artisti.Count.ToString(), Sub() MostraElencoArtisti("Artisti trovati per " & testo, r.Artisti)))
            End If
            ' Un solo tipo di risultati: si apre direttamente, senza menu intermedio.
            If voci.Count = 1 Then
                voci(0).Value.Invoke()
            Else
                MediaUtil.Scegli("Risultati per " & testo, voci)
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Artisti
    ' ------------------------------------------------------------------------------------------

    Private Async Sub MostraArtisti()
        Try
            Dim artisti As List(Of NavidromeArtista) = Await ProvaAsync("Carico gli artisti", Function() _client.ArtistiAsync())
            If artisti Is Nothing Then Return
            If artisti.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono artisti nella libreria del server.")
                Return
            End If
            MostraElencoArtisti("Artisti, " & artisti.Count.ToString(), artisti)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraArtisti: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraElencoArtisti(titolo As String, artisti As List(Of NavidromeArtista))
        Try
            Dim elenco As New ElencoMedia(titolo, True)
            Dim perId As New Dictionary(Of String, NavidromeArtista)
            For Each a As NavidromeArtista In artisti
                If perId.ContainsKey(a.Id) Then Continue For
                perId.Add(a.Id, a)
                elenco.Aggiungi(a.Descrizione, a.Id)
            Next
            AddHandler elenco.Scelto, Sub(id As String) ApriArtista(perId, id)
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String))
                                             If ids.Count > 0 Then ApriArtista(perId, ids(0))
                                         End Sub
            elenco.Opzione("Riproduci i brani degli artisti selezionati", Sub() RiproduciArtisti(titolo, elenco.Selezionati(), False))
            elenco.Opzione("Riproduci in ordine casuale", Sub() RiproduciArtisti(titolo, elenco.Selezionati(), True))
            elenco.Opzione("Aggiungi ai preferiti", Sub() ImpostaPreferitiArtisti(elenco.Selezionati(), True))
            elenco.Opzione("Togli dai preferiti", Sub() ImpostaPreferitiArtisti(elenco.Selezionati(), False))
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraElencoArtisti: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ApriArtista(perId As Dictionary(Of String, NavidromeArtista), id As String)
        Try
            Dim artista As NavidromeArtista = Nothing
            If Not perId.TryGetValue(id, artista) Then Return
            Dim album As List(Of NavidromeAlbum) = Await ProvaAsync("Carico gli album di " & artista.Nome, Function() _client.AlbumDiArtistaAsync(artista.Id))
            If album Is Nothing Then Return
            If album.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono album di " & artista.Nome & ".")
                Return
            End If
            MostraAlbum("Album di " & artista.Nome, album, Nothing)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriArtista: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Tutti i brani degli artisti indicati: prima gli album di ciascuno, poi i brani degli album.</summary>
    Private Async Function BraniDegliArtistiAsync(ids As List(Of String)) As Task(Of List(Of NavidromeBrano))
        Dim risultato As New List(Of NavidromeBrano)
        For Each id As String In ids
            For Each album As NavidromeAlbum In Await _client.AlbumDiArtistaAsync(id)
                risultato.AddRange(Await _client.BraniDiAlbumAsync(album.Id))
            Next
        Next
        Return risultato
    End Function

    Private Async Sub RiproduciArtisti(titolo As String, ids As List(Of String), casuale As Boolean)
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Preparo l'ascolto", Function() Task.Run(Function() BraniDegliArtistiAsync(ids)))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Gli artisti scelti non hanno brani.")
                Return
            End If
            RiproduciBrani(titolo, If(casuale, Rimescola(brani), brani), 0)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RiproduciArtisti: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraInAscolto()
        Try
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Controllo cosa si ascolta", Function() _client.InAscoltoAsync())
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("In questo momento nessuno sta ascoltando musica sul server.")
                Return
            End If
            MostraBrani("In ascolto ora", brani)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraInAscolto: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ImpostaPreferitiArtisti(ids As List(Of String), preferito As Boolean)
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim ok As Boolean = Await ProvaAzioneAsync("Aggiorno i preferiti", Function() ImpostaPreferitiArtistiAsync(ids, preferito))
            If ok Then MediaUtil.Messaggio(If(preferito, "Aggiunti ai preferiti.", "Tolti dai preferiti."))
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ImpostaPreferitiArtisti: " & ex.ToString())
        End Try
    End Sub

    Private Async Function ImpostaPreferitiArtistiAsync(ids As List(Of String), preferito As Boolean) As Task
        For Each id As String In ids
            Await _client.ImpostaPreferitoArtistaAsync(id, preferito)
        Next
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Album
    ' ------------------------------------------------------------------------------------------

    Private Sub MostraTipiAlbum()
        Try
            Dim voci As New List(Of KeyValuePair(Of String, Action)) From {
                MediaUtil.Voce("Aggiunti di recente", Sub() ApriElencoAlbum("Album aggiunti di recente", NavidromeElencoAlbum.PiuRecenti, True)),
                MediaUtil.Voce("Ascoltati di recente", Sub() ApriElencoAlbum("Album ascoltati di recente", NavidromeElencoAlbum.AscoltatiDiRecente, True)),
                MediaUtil.Voce("I più ascoltati", Sub() ApriElencoAlbum("Album più ascoltati", NavidromeElencoAlbum.PiuAscoltati, True)),
                MediaUtil.Voce("Casuali", Sub() ApriElencoAlbum("Album casuali", NavidromeElencoAlbum.Casuali, False)),
                MediaUtil.Voce("In ordine alfabetico", Sub() ApriElencoAlbum("Album in ordine alfabetico", NavidromeElencoAlbum.OrdineAlfabetico, True)),
                MediaUtil.Voce("In ordine di artista", Sub() ApriElencoAlbum("Album in ordine di artista", NavidromeElencoAlbum.OrdinePerArtista, True)),
                MediaUtil.Voce("Preferiti", Sub() ApriElencoAlbum("Album preferiti", NavidromeElencoAlbum.Preferiti, True)),
                MediaUtil.Voce("I più votati", Sub() ApriElencoAlbum("Album più votati", NavidromeElencoAlbum.PiuVotati, True))
            }
            ' Le voci si aprono sopra al menu, che resta sotto: Esc riporta qui.
            Dim menu As New WGMenu()
            For Each voce As KeyValuePair(Of String, Action) In voci
                Dim azione As Action = voce.Value
                menu.aggiungi(voce.Key, Sub() azione())
            Next
            menu.avvia("Album")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraTipiAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ApriElencoAlbum(titolo As String, tipo As NavidromeElencoAlbum, paginabile As Boolean, Optional genere As String = "")
        Try
            Dim primi As List(Of NavidromeAlbum) = Await ProvaAsync("Carico gli album", Function() _client.ElencoAlbumAsync(tipo, ALBUM_PER_PAGINA, 0, genere))
            If primi Is Nothing Then Return
            If primi.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono album in questo elenco.")
                Return
            End If
            Dim caricaAltri As Func(Of Integer, Task(Of List(Of NavidromeAlbum))) = Nothing
            If paginabile AndAlso primi.Count >= ALBUM_PER_PAGINA Then
                caricaAltri = Function(inizio As Integer) _client.ElencoAlbumAsync(tipo, ALBUM_PER_PAGINA, inizio, genere)
            End If
            MostraAlbum(titolo, primi, caricaAltri)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriElencoAlbum: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Un elenco di album. Se caricaAltri non è Nothing l'elenco è a pagine: arrivati in fondo (o con
    ''' l'opzione Carica altri album) si chiedono al server gli album successivi.
    ''' </summary>
    Private Sub MostraAlbum(titolo As String, album As List(Of NavidromeAlbum), caricaAltri As Func(Of Integer, Task(Of List(Of NavidromeAlbum))))
        Try
            Dim elenco As New ElencoMedia(titolo, True)
            Dim perId As New Dictionary(Of String, NavidromeAlbum)
            Dim totale As Integer = 0
            Dim finito As Boolean = (caricaAltri Is Nothing)
            Dim inCorso As Boolean = False

            Dim aggiungi As Action(Of List(Of NavidromeAlbum)) =
                Sub(nuovi As List(Of NavidromeAlbum))
                    For Each a As NavidromeAlbum In nuovi
                        totale += 1
                        If perId.ContainsKey(a.Id) Then Continue For
                        perId.Add(a.Id, a)
                        elenco.Aggiungi(a.Descrizione, a.Id)
                    Next
                End Sub
            aggiungi(album)

            Dim altri As Action =
                Async Sub()
                    If finito OrElse inCorso Then Return
                    inCorso = True
                    Try
                        Dim inizio As Integer = totale
                        Dim nuovi As List(Of NavidromeAlbum) = Await ProvaAsync("Carico altri album", Function() caricaAltri(inizio))
                        If nuovi Is Nothing OrElse nuovi.Count = 0 Then
                            finito = True
                        Else
                            aggiungi(nuovi)
                            If nuovi.Count < ALBUM_PER_PAGINA Then finito = True
                        End If
                    Finally
                        inCorso = False
                    End Try
                End Sub

            AddHandler elenco.Scelto, Sub(id As String) ApriAlbum(perId, id)
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String))
                                             If ids.Count > 0 Then ApriAlbum(perId, ids(0))
                                         End Sub
            If caricaAltri IsNot Nothing Then
                AddHandler elenco.FineElenco, Sub() altri()
                elenco.Opzione("Carica altri album", Sub() altri())
            End If
            elenco.Opzione("Riproduci gli album selezionati", Sub() RiproduciAlbum(titolo, elenco.Selezionati()))
            elenco.Opzione("Aggiungi ai preferiti", Sub() ImpostaPreferitiAlbum(elenco.Selezionati(), True))
            elenco.Opzione("Togli dai preferiti", Sub() ImpostaPreferitiAlbum(elenco.Selezionati(), False))
            elenco.Opzione("Aggiungi a una playlist", Sub() AggiungiAlbumAPlaylist(elenco.Selezionati()))
            elenco.Opzione("Scarica gli album selezionati", Sub() ScaricaAlbum(elenco.Selezionati()))
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ApriAlbum(perId As Dictionary(Of String, NavidromeAlbum), id As String)
        Try
            Dim album As NavidromeAlbum = Nothing
            If Not perId.TryGetValue(id, album) Then Return
            Await ApriAlbumPerIdAsync(album.Id, album.Nome)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ApriAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Function ApriAlbumPerIdAsync(idAlbum As String, nome As String) As Task
        Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Carico " & nome, Function() _client.BraniDiAlbumAsync(idAlbum))
        If brani Is Nothing Then Return
        If brani.Count = 0 Then
            MediaUtil.Messaggio("L'album non contiene brani.")
            Return
        End If
        MostraBrani(nome, brani)
    End Function

    ''' <summary>I brani di tutti gli album indicati, nell'ordine in cui sono stati scelti.</summary>
    Private Async Function BraniDegliAlbumAsync(ids As List(Of String)) As Task(Of List(Of NavidromeBrano))
        Dim risultato As New List(Of NavidromeBrano)
        For Each id As String In ids
            risultato.AddRange(Await _client.BraniDiAlbumAsync(id))
        Next
        Return risultato
    End Function

    Private Async Sub RiproduciAlbum(titolo As String, ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Preparo l'ascolto", Function() BraniDegliAlbumAsync(ids))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Gli album scelti non contengono brani.")
                Return
            End If
            RiproduciBrani(titolo, brani, 0)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.RiproduciAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ScaricaAlbum(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Leggo i brani degli album", Function() BraniDegliAlbumAsync(ids))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Gli album scelti non contengono brani.")
                Return
            End If
            ScaricaBrani(brani)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScaricaAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ImpostaPreferitiAlbum(ids As List(Of String), preferito As Boolean)
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim ok As Boolean = Await ProvaAzioneAsync("Aggiorno i preferiti", Function() ImpostaPreferitiAlbumAsync(ids, preferito))
            If ok Then MediaUtil.Messaggio(If(preferito, "Aggiunti ai preferiti.", "Tolti dai preferiti."))
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ImpostaPreferitiAlbum: " & ex.ToString())
        End Try
    End Sub

    Private Async Function ImpostaPreferitiAlbumAsync(ids As List(Of String), preferito As Boolean) As Task
        For Each id As String In ids
            Await _client.ImpostaPreferitoAlbumAsync(id, preferito)
        Next
    End Function

    Private Async Sub AggiungiAlbumAPlaylist(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Leggo i brani degli album", Function() BraniDegliAlbumAsync(ids))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Gli album scelti non contengono brani.")
                Return
            End If
            ScegliPlaylist(brani.Select(Function(b) b.Id).ToList())
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.AggiungiAlbumAPlaylist: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Generi
    ' ------------------------------------------------------------------------------------------

    Private Async Sub MostraGeneri()
        Try
            Dim generi As List(Of NavidromeGenere) = Await ProvaAsync("Carico i generi", Function() _client.GeneriAsync())
            If generi Is Nothing Then Return
            If generi.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono generi nella libreria del server.")
                Return
            End If
            Dim elenco As New ElencoMedia("Generi, " & generi.Count.ToString(), False)
            Dim perId As New Dictionary(Of String, NavidromeGenere)
            For Each g As NavidromeGenere In generi.OrderBy(Function(x) x.Nome, StringComparer.CurrentCultureIgnoreCase)
                If perId.ContainsKey(g.Nome) Then Continue For
                perId.Add(g.Nome, g)
                elenco.Aggiungi(g.Descrizione, g.Nome)
            Next
            AddHandler elenco.Scelto, Sub(nome As String) ApriElencoAlbum("Album del genere " & nome, NavidromeElencoAlbum.PerGenere, True, nome)
            elenco.Opzione("Brani casuali del genere", Sub() MostraBraniCasualiDelGenere(elenco.Selezionati()))
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraGeneri: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraBraniCasualiDelGenere(selezionati As List(Of String))
        Try
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then Return
            Dim genere As String = selezionati(0)
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Carico brani del genere", Function() _client.BraniCasualiAsync(BRANI_CASUALI, genere))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Non ci sono brani di questo genere.")
                Return
            End If
            MostraBrani("Brani casuali, " & genere, brani)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraBraniCasualiDelGenere: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Preferiti
    ' ------------------------------------------------------------------------------------------

    Private Async Sub MostraPreferiti()
        Try
            Dim r As NavidromeRisultatiRicerca = Await ProvaAsync("Carico i preferiti", Function() _client.PreferitiAsync())
            If r Is Nothing Then Return
            If r.Vuoto Then
                MediaUtil.Messaggio("Non hai ancora preferiti sul server.")
                Return
            End If
            Dim voci As New List(Of KeyValuePair(Of String, Action))
            If r.Brani.Count > 0 Then
                voci.Add(MediaUtil.Voce("Brani preferiti, " & r.Brani.Count.ToString(), Sub() MostraBrani("Brani preferiti", r.Brani)))
            End If
            If r.Album.Count > 0 Then
                voci.Add(MediaUtil.Voce("Album preferiti, " & r.Album.Count.ToString(), Sub() MostraAlbum("Album preferiti", r.Album, Nothing)))
            End If
            If r.Artisti.Count > 0 Then
                voci.Add(MediaUtil.Voce("Artisti preferiti, " & r.Artisti.Count.ToString(), Sub() MostraElencoArtisti("Artisti preferiti", r.Artisti)))
            End If
            If voci.Count = 1 Then
                voci(0).Value.Invoke()
            Else
                MediaUtil.Scegli("Preferiti", voci)
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraPreferiti: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Brani casuali e radio
    ' ------------------------------------------------------------------------------------------

    Private Async Sub MostraBraniCasuali()
        Try
            Dim brani As List(Of NavidromeBrano) = Await ProvaAsync("Scelgo brani a caso", Function() _client.BraniCasualiAsync(BRANI_CASUALI))
            If brani Is Nothing Then Return
            If brani.Count = 0 Then
                MediaUtil.Messaggio("La libreria del server non contiene brani.")
                Return
            End If
            MostraBrani("Brani casuali", brani)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraBraniCasuali: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraRadio()
        Try
            Dim radio As List(Of NavidromeRadio) = Await ProvaAsync("Carico le radio", Function() _client.RadioAsync())
            If radio Is Nothing Then Return
            If radio.Count = 0 Then
                MediaUtil.Messaggio("Sul server non ci sono radio internet configurate.")
                Return
            End If
            Dim elenco As New ElencoMedia("Radio del server, " & radio.Count.ToString(), False)
            Dim perId As New Dictionary(Of String, NavidromeRadio)
            For Each r As NavidromeRadio In radio
                If perId.ContainsKey(r.Id) Then Continue For
                perId.Add(r.Id, r)
                elenco.Aggiungi(r.Nome, r.Id)
            Next
            AddHandler elenco.Scelto, Sub(id As String)
                                          Dim scelta As NavidromeRadio = Nothing
                                          If perId.TryGetValue(id, scelta) AndAlso scelta.IndirizzoStream <> "" Then
                                              RiproduzioneMedia.RiproduciRadio(scelta.IndirizzoStream)
                                          End If
                                      End Sub
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraRadio: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Gestione della libreria sul server
    ' ------------------------------------------------------------------------------------------

    Private Sub MostraGestioneLibreria()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Cerca nuovi brani sul server", AddressOf ScansioneVeloce)
            menu.aggiungi("Rileggi tutta la libreria", AddressOf ScansioneCompleta)
            menu.aggiungi("Stato della scansione", AddressOf StatoScansione)
            menu.avvia("Gestione libreria")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.MostraGestioneLibreria: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ScansioneVeloce()
        Try
            Dim stato As NavidromeStatoScansione = Await ProvaAsync("Avvio la scansione", Function() _client.AvviaScansioneAsync(False))
            If stato Is Nothing Then Return
            MediaUtil.Messaggio("Scansione avviata. Il server cerca i brani nuovi o cambiati. Puoi controllare l'avanzamento da Stato della scansione.")
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScansioneVeloce: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScansioneCompleta()
        Try
            WGDomanda.Chiedi("Rileggere tutta la libreria? Può richiedere molto tempo.",
                             Async Sub()
                                 Try
                                     Dim stato As NavidromeStatoScansione = Await ProvaAsync("Avvio la scansione completa", Function() _client.AvviaScansioneAsync(True))
                                     If stato Is Nothing Then Return
                                     MediaUtil.Messaggio("Scansione completa avviata. Puoi controllare l'avanzamento da Stato della scansione.")
                                 Catch ex As Exception
                                     MediaUtil.Log("MenuNavidrome.ScansioneCompleta: " & ex.ToString())
                                 End Try
                             End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.ScansioneCompleta: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub StatoScansione()
        Try
            Dim stato As NavidromeStatoScansione = Await ProvaAsync("Controllo la scansione", Function() _client.StatoScansioneAsync())
            If stato Is Nothing Then Return
            If stato.InCorso Then
                MediaUtil.Messaggio("Scansione in corso. Elementi letti finora: " & stato.Conteggio.ToString() & ".")
            Else
                MediaUtil.Messaggio("Nessuna scansione in corso. La libreria contiene " & stato.Conteggio.ToString() & " elementi.")
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuNavidrome.StatoScansione: " & ex.ToString())
        End Try
    End Sub

End Class
