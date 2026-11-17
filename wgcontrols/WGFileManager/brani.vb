Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports MediaDevices
Imports wgcontrols
Imports WGDati

Public Class GestoreBraniPreferitiERecenti
    Private Enum OperazioneCampi
        Nessuna
        Crea
        Modifica
        Cerca
    End Enum

    Private log As New wgcontrols.Logger
    Private menuFile As MenuFile
    Private logic As CLSFile
    Private dbBrani As New CLSBrani()
    Private config As New WGConfig()

    Private WithEvents listaBrani As WGMenuLista
    Private WithEvents listaCategorie As WGMenuLista
    Private WithEvents listaInfo As WGMenuLista
    Private WithEvents campiInput As Campi

    Private _paginaCorrente As Integer = 1
    Private _elementiPerPagina As Integer = 100
    Private _filtroCategoriaId As Long = -1
    Private _searchQuery As String = ""
    Private _caricamentoIndietro As Boolean = False

    Private _idCategoriaModifica As Long = 0
    Private _idCategoriaDaEliminare As Long = 0
    Private _operazioneCorrente As OperazioneCampi = OperazioneCampi.Nessuna
    Private _braniDaSpostare As List(Of String)
    Private _braniDaEliminare As List(Of String)
    Private _pathBranoCorrente As String = ""

    Private ordineDefaultPreferiti As OrdinamentoPreferiti
    Private ordineCorrentePreferiti As OrdinamentoPreferiti
    Private ordineDefaultRecenti As OrdinamentoRecenti
    Private ordineCorrenteRecenti As OrdinamentoRecenti

    Public Sub New()
        Try
            menuFile = New MenuFile()
            logic = New CLSFile()

            Dim ordinePrefDefaultStr As String = "DataAggiunta"
            If config.EsisteId("ordinamento_brani_preferiti") Then
                Dim val = config.LeggiValore("ordinamento_brani_preferiti")
                If val = "Nome" Then
                    ordinePrefDefaultStr = "Alfabetico"
                ElseIf val = "DataAggiunta" Then
                    ordinePrefDefaultStr = "DataAggiunta"
                ElseIf val = "Durata" Then
                    ordinePrefDefaultStr = "Durata"
                End If
            End If

            If Not [Enum].TryParse(ordinePrefDefaultStr, True, ordineDefaultPreferiti) Then
                ordineDefaultPreferiti = OrdinamentoPreferiti.DataAggiunta
            End If
            ordineCorrentePreferiti = ordineDefaultPreferiti

            Dim ordineRecentiDefaultStr As String = "DataRecente"
            If config.EsisteId("ordinamento_brani_recenti") Then
                Dim val = config.LeggiValore("ordinamento_brani_recenti")
                If val = "Nome" Then
                    ordineRecentiDefaultStr = "Alfabetico"
                ElseIf val = "Data" Then
                    ordineRecentiDefaultStr = "DataRecente"
                End If
            End If

            If Not [Enum].TryParse(ordineRecentiDefaultStr, True, ordineDefaultRecenti) Then
                ordineDefaultRecenti = OrdinamentoRecenti.DataRecente
            End If
            ordineCorrenteRecenti = ordineDefaultRecenti
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function VerificaEsistenza(path As String, Optional mostraErrori As Boolean = True) As Boolean
        Try
            If logic.IsWpdPath(path) Then
                Dim parts = path.Split("|"c)
                If parts.Length < 3 Then Return False
                Dim deviceId As String = parts(1)
                Dim wpdPath As String = parts(2)

                Dim devices = MediaDevice.GetDevices()
                Dim device As MediaDevice = Nothing

                For Each d In devices
                    If d.DeviceId = deviceId OrElse d.FriendlyName = deviceId Then
                        device = d
                        Exit For
                    End If
                Next

                If device Is Nothing Then
                    If mostraErrori Then MostraMessaggio("Dispositivo portatile non connesso o non disponibile.")
                    Return False
                End If

                Try
                    device.Connect()
                    Dim exists As Boolean = device.FileExists(wpdPath)
                    device.Disconnect()
                    If Not exists AndAlso mostraErrori Then MostraMessaggio("Il file non esiste sul dispositivo portatile.")
                    Return exists
                Catch ex As Exception
                    If mostraErrori Then MostraMessaggio("Errore durante l'accesso al dispositivo: " & ex.Message)
                    Return False
                End Try
            Else
                If Not WGRete.EsisteFile(path, 8000) Then
                    If mostraErrori Then MostraMessaggio("Il file non esiste nel percorso specificato:" & vbCrLf & path)
                    Return False
                End If
                Return True
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function VerificaEsistenzaBatch(percorsi As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim percorsiLocali As List(Of String) = percorsi.Where(Function(p) Not logic.IsWpdPath(p)).ToList()
            Dim percorsiWpd As List(Of String) = percorsi.Where(Function(p) logic.IsWpdPath(p)).ToList()

            For Each p As String In percorsiLocali
                Try
                    If WGRete.EsisteFile(p, 8000) Then risultato.Add(p)
                Catch ex As Exception
                    log.ScriviLog(ex.ToString())
                End Try
            Next

            If percorsiWpd.Count > 0 Then
                Dim gruppi = percorsiWpd.GroupBy(Function(p) EstraiDeviceIdDaPercorso(p))
                For Each gruppo In gruppi
                    Try
                        Dim deviceId As String = gruppo.Key
                        If String.IsNullOrEmpty(deviceId) Then Continue For

                        Dim devices = MediaDevice.GetDevices()
                        Dim device As MediaDevice = devices.FirstOrDefault(Function(d) d.DeviceId = deviceId OrElse d.FriendlyName = deviceId)
                        If device Is Nothing Then Continue For

                        Try
                            device.Connect()
                            For Each p As String In gruppo
                                Try
                                    Dim parts As String() = p.Split("|"c)
                                    If parts.Length < 3 Then Continue For
                                    If device.FileExists(parts(2)) Then risultato.Add(p)
                                Catch ex As Exception
                                    log.ScriviLog(ex.ToString())
                                End Try
                            Next
                        Finally
                            Try
                                If device.IsConnected Then device.Disconnect()
                            Catch ex As Exception
                                log.ScriviLog(ex.ToString())
                            End Try
                        End Try
                    Catch ex As Exception
                        log.ScriviLog(ex.ToString())
                    End Try
                Next
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function EstraiDeviceIdDaPercorso(percorso As String) As String
        Try
            Dim parts As String() = percorso.Split("|"c)
            If parts.Length < 2 Then Return ""
            Return parts(1)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Public Sub VisualizzaBraniPreferiti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Consulta brani", AddressOf AvviaConsultaTutti)
            menu.aggiungi("Consulta per categoria", AddressOf AvviaConsultaPerCategoria)
            menu.aggiungi("Cerca...", AddressOf AvviaRicerca)
            menu.aggiungi("Gestisci categorie", AddressOf AvviaGestioneCategorie)
            menu.avvia("Menu Preferiti")
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaGestioneCategorie()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Crea nuova categoria", AddressOf CreaNuovaCategoria)
            menu.aggiungi("Elenco categorie", AddressOf MostraElencoCategorieGestione)
            menu.avvia("Gestione Categorie")
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraElencoCategorieGestione()
        Try
            Dim categorie As List(Of CategoriaBrano) = Await dbBrani.GetCategorieAsync()

            If categorie.Count = 0 Then
                MostraMessaggio("Nessuna categoria presente.")
                Return
            End If

            If listaCategorie IsNot Nothing Then listaCategorie.abbandona()
            listaCategorie = New WGMenuLista()
            listaCategorie.impostaTitolo("Elenco Categorie")

            listaCategorie.aggiungiOpzione("Modifica Selezionata", AddressOf ModificaCategoriaSelezionata)
            listaCategorie.aggiungiOpzione("Elimina Selezionata", AddressOf EliminaCategoriaSelezionata)

            For Each cat As CategoriaBrano In categorie
                listaCategorie.aggiungiElemento(cat.Nome, cat.Id.ToString())
            Next

            AddHandler listaCategorie.escPremuto, AddressOf ChiudiListaCategorie
            listaCategorie.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare l'elenco delle categorie.")
        End Try
    End Sub

    Private Sub ChiudiListaCategorie()
        Try
            listaCategorie.abbandona()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CreaNuovaCategoria()
        _operazioneCorrente = OperazioneCampi.Crea
        AvviaCampi("Nome nuova categoria", "")
    End Sub

    Private Sub ModificaCategoriaSelezionata()
        Try
            Dim elementi As List(Of String) = listaCategorie.ottieniElementi()
            If elementi.Count > 0 Then
                _idCategoriaModifica = Long.Parse(elementi.Last())
                _operazioneCorrente = OperazioneCampi.Modifica
                AvviaCampi("Modifica nome categoria", "")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EliminaCategoriaSelezionata()
        Try
            Dim elementi As List(Of String) = listaCategorie.ottieniElementi()
            If elementi.Count > 0 Then
                _idCategoriaDaEliminare = Long.Parse(elementi.Last())
                Dim domanda As New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf RispostaEliminaCategoria
                domanda.avvia("Eliminare la categoria?")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub RispostaEliminaCategoria(risposta As DialogResult)
        Try
            If risposta <> DialogResult.Yes Then Return

            Await dbBrani.GestisciCategoriaAsync("elimina", "", _idCategoriaDaEliminare)
            listaCategorie.abbandona()

            Dim contaCat As Long = Await dbBrani.GetContaCategorieAsync()
            If contaCat > 0 Then
                MostraElencoCategorieGestione()
            Else
                MostraMessaggio("Nessuna categoria rimasta.")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile eliminare la categoria.")
        End Try
    End Sub

    Private Sub AvviaCampi(titolo As String, valoreDefault As String)
        Try
            campiInput = New Campi()
            campiInput.Aggiungi(titolo, "c.valore", True)
            AddHandler campiInput.Fine, AddressOf FineCampi
            campiInput.Avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub FineCampi()
        Try
            Dim valore As String = campiInput.RecuperaValore("c.valore")

            campiInput = Nothing
            Select Case _operazioneCorrente
                Case OperazioneCampi.Crea
                    GestisciCategoria("aggiungi", valore, 0)
                Case OperazioneCampi.Modifica
                    GestisciCategoria("modifica", valore, _idCategoriaModifica)
                Case OperazioneCampi.Cerca
                    _searchQuery = valore
                    _filtroCategoriaId = -1
                    _paginaCorrente = 1
                    _caricamentoIndietro = False
                    MostraListaBrani()
            End Select
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub GestisciCategoria(azione As String, valore As String, id As Long)
        Try
            Await dbBrani.GestisciCategoriaAsync(azione, valore, id)
            If listaCategorie IsNot Nothing Then
                listaCategorie.abbandona()
                MostraElencoCategorieGestione()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile salvare la categoria.")
        End Try
    End Sub

    Private Sub AvviaConsultaTutti()
        _filtroCategoriaId = -1
        _searchQuery = ""
        _paginaCorrente = 1
        _caricamentoIndietro = False
        MostraListaBrani()
    End Sub

    Private Sub AvviaRicerca()
        _operazioneCorrente = OperazioneCampi.Cerca
        AvviaCampi("Cerca brani", "")
    End Sub

    Private Async Sub AvviaConsultaPerCategoria()
        Try
            Dim categorie As List(Of CategoriaBrano) = Await dbBrani.GetCategorieAsync()

            If categorie.Count = 0 Then
                MostraMessaggio("Nessuna categoria presente per filtrare.")
                Return
            End If

            If listaCategorie IsNot Nothing Then listaCategorie.abbandona()
            listaCategorie = New WGMenuLista()
            listaCategorie.impostaTitolo("Seleziona Categoria")

            listaCategorie.aggiungiElemento("Tutti i brani / Senza categoria", "0")

            For Each cat As CategoriaBrano In categorie
                listaCategorie.aggiungiElemento(cat.Nome, cat.Id.ToString())
            Next

            AddHandler listaCategorie.ElementoSelezionato, AddressOf FiltraPerCategoriaSelezionata
            AddHandler listaCategorie.escPremuto, AddressOf ChiudiListaCategorie
            listaCategorie.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare le categorie.")
        End Try
    End Sub

    Private Sub FiltraPerCategoriaSelezionata(idString As String)
        Try
            _filtroCategoriaId = Long.Parse(idString)
            _searchQuery = ""
            _paginaCorrente = 1
            _caricamentoIndietro = False
            listaCategorie.abbandona()
            MostraListaBrani()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub MostraListaBrani()
        Try
            Dim countBrani As Long = Await dbBrani.GetContaBraniPreferitiAsync(_filtroCategoriaId, _searchQuery)

            If countBrani = 0 Then
                If _searchQuery <> "" Then
                    MostraMessaggio("Nessun brano trovato per la ricerca: " & _searchQuery)
                Else
                    MostraMessaggio("Nessun brano presente in questa selezione.")
                End If
                Return
            End If

            If listaBrani IsNot Nothing Then listaBrani.abbandona()
            listaBrani = New WGMenuLista()

            Dim titolo As String = "Elenco Brani"
            If _searchQuery <> "" Then titolo &= " (Cerca: " & _searchQuery & ")"
            listaBrani.impostaTitolo(titolo)

            listaBrani.multiselezioneConsentita = True
            listaBrani.ciclono = True

            listaBrani.aggiungiOpzione("Informazioni", AddressOf AzioneInformazioni)
            listaBrani.aggiungiOpzione("Riproduci selezione", AddressOf AzioneRiproduciSelezione)
            listaBrani.aggiungiOpzione("Riproduci tutti", AddressOf AzioneRiproduciTutti)
            listaBrani.aggiungiOpzione("Sposta in categoria", AddressOf AzioneSpostaInCategoria)
            listaBrani.aggiungiOpzione("Elimina", AddressOf AzioneElimina)

            AddHandler listaBrani.fine, AddressOf PaginaSuccessiva
            AddHandler listaBrani.inizio, AddressOf PaginaPrecedente
            AddHandler listaBrani.ElementoSelezionato, AddressOf ApriBranoDiretto
            AddHandler listaBrani.escPremuto, AddressOf ChiudiListaBrani

            Await CaricaDatiPaginaBrani()
            listaBrani.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare l'elenco dei brani.")
        End Try
    End Sub

    Private Sub ChiudiListaBrani()
        Try
            listaBrani.abbandona()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Function CaricaDatiPaginaBrani() As Task
        Try
            Dim brani As List(Of WGDati.Brano) = Await dbBrani.GetPreferitiPaginatiAsync(_paginaCorrente, _elementiPerPagina, ordineCorrentePreferiti, _filtroCategoriaId, _searchQuery)

            listaBrani.pulisci()

            If brani.Count = 0 AndAlso _paginaCorrente > 1 Then
                _paginaCorrente -= 1
                Await CaricaDatiPaginaBrani()
                Return
            End If

            For Each b As WGDati.Brano In brani
                listaBrani.aggiungiElemento(b.Nome, b.Path)
            Next

            If _caricamentoIndietro Then
                listaBrani.SelezionaUltimoElemento()
                _caricamentoIndietro = False
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare la pagina dei brani.")
        End Try
    End Function

    Private Async Sub PaginaSuccessiva()
        Try
            Dim conta As Long = Await dbBrani.GetContaBraniPreferitiAsync(_filtroCategoriaId, _searchQuery)
            If (_paginaCorrente * _elementiPerPagina) < conta Then
                _paginaCorrente += 1
                Await CaricaDatiPaginaBrani()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub PaginaPrecedente()
        Try
            If _paginaCorrente > 1 Then
                _paginaCorrente -= 1
                _caricamentoIndietro = True
                Await CaricaDatiPaginaBrani()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub AzioneInformazioni()
        Try
            Dim elementi As List(Of String) = listaBrani.ottieniElementi()
            If elementi.Count = 0 Then Return

            _pathBranoCorrente = elementi.Last()
            Dim info As MetadatiBrano = Await dbBrani.InfoBranoAsync(_pathBranoCorrente)

            If listaInfo IsNot Nothing Then listaInfo.abbandona()

            If info Is Nothing Then
                MostraMessaggio("Nessuna informazione disponibile.")
                Return
            End If

            listaInfo = New WGMenuLista()
            listaInfo.impostaTitolo("Informazioni Brano")

            listaInfo.aggiungiOpzione("Copia informazioni", AddressOf CopiaInfoBrano)
            listaInfo.aggiungiOpzione("Riproduci", AddressOf RiproduciBranoInfo)
            listaInfo.aggiungiOpzione("Sposta in categoria", AddressOf SpostaBranoInfo)

            listaInfo.aggiungiElemento("Titolo: " & info.Titolo)
            listaInfo.aggiungiElemento("Artista: " & info.Artista)
            listaInfo.aggiungiElemento("Album: " & info.Album)
            listaInfo.aggiungiElemento("Anno: " & info.Anno)
            listaInfo.aggiungiElemento("Genere: " & info.Genere)

            If info.TrackNumber > 0 Then
                Dim tracciaStr As String = "Traccia: " & info.TrackNumber.ToString()
                If info.TrackCount > 0 Then tracciaStr &= " di " & info.TrackCount.ToString()
                listaInfo.aggiungiElemento(tracciaStr)
            End If

            If info.DiscNumber > 0 Then
                Dim discoStr As String = "Disco: " & info.DiscNumber.ToString()
                If info.DiscCount > 0 Then discoStr &= " di " & info.DiscCount.ToString()
                listaInfo.aggiungiElemento(discoStr)
            End If

            If Not String.IsNullOrEmpty(info.Country) Then
                listaInfo.aggiungiElemento("Paese: " & info.Country)
            End If

            Dim esplicitoStr As String = "No"
            If Not String.IsNullOrEmpty(info.Explicit) AndAlso (info.Explicit.ToLower().Contains("explicit") OrElse info.Explicit.ToLower() = "yes") Then
                esplicitoStr = "Sì"
            End If
            listaInfo.aggiungiElemento("Contenuto Esplicito: " & esplicitoStr)

            AddHandler listaInfo.escPremuto, AddressOf ChiudiListaInfo
            listaInfo.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare le informazioni del brano.")
        End Try
    End Sub

    Private Sub ChiudiListaInfo()
        Try
            listaInfo.abbandona()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CopiaInfoBrano()
        Try
            Dim sb As New StringBuilder()
            Dim elementi As List(Of String) = listaInfo.ottieniElementi()
            For Each riga As String In elementi
                sb.AppendLine(riga)
            Next
            Clipboard.SetText(sb.ToString())
            MostraMessaggio("Informazioni copiate negli appunti.")
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciBranoInfo()
        Try
            If Not String.IsNullOrEmpty(_pathBranoCorrente) Then
                If VerificaEsistenza(_pathBranoCorrente) Then
                    menuFile.Apri(_pathBranoCorrente)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SpostaBranoInfo()
        Try
            If Not String.IsNullOrEmpty(_pathBranoCorrente) Then
                _braniDaSpostare = New List(Of String) From {_pathBranoCorrente}
                PrepareSpostaMenu()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub PrepareSpostaMenu()
        Try
            Dim categorie As List(Of CategoriaBrano) = Await dbBrani.GetCategorieAsync()

            If listaCategorie IsNot Nothing Then listaCategorie.abbandona()
            listaCategorie = New WGMenuLista()
            listaCategorie.impostaTitolo("Sposta in...")
            listaCategorie.aggiungiElemento("Senza categoria", "0")

            For Each cat As CategoriaBrano In categorie
                listaCategorie.aggiungiElemento(cat.Nome, cat.Id.ToString())
            Next

            AddHandler listaCategorie.ElementoSelezionato, AddressOf EseguiSpostamento
            AddHandler listaCategorie.escPremuto, AddressOf ChiudiListaCategorie
            listaCategorie.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare le categorie.")
        End Try
    End Sub

    Private Sub AzioneElimina()
        Try
            Dim elementi As List(Of String) = listaBrani.ottieniElementi()
            If elementi.Count > 0 Then
                _braniDaEliminare = elementi
                Dim domanda As New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf RispostaEliminaBrani
                domanda.avvia("Eliminare i brani selezionati?")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub RispostaEliminaBrani(risposta As DialogResult)
        Try
            If risposta <> DialogResult.Yes Then Return

            For Each path As String In _braniDaEliminare
                Await dbBrani.RimuoviBranoPreferitoAsync(path)
            Next

            Dim contaRimanenti As Long = Await dbBrani.GetContaBraniPreferitiAsync(_filtroCategoriaId, _searchQuery)
            If contaRimanenti > 0 Then
                Await CaricaDatiPaginaBrani()
            Else
                listaBrani.abbandona()
                MostraMessaggio("Nessun brano rimasto.")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile eliminare i brani selezionati.")
        End Try
    End Sub

    Private Sub AzioneSpostaInCategoria()
        Try
            _braniDaSpostare = listaBrani.ottieniElementi()
            If _braniDaSpostare.Count > 0 Then
                PrepareSpostaMenu()
            Else
                MostraMessaggio("Seleziona almeno un brano.")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub EseguiSpostamento(idString As String)
        Try
            Dim targetId As Long = Long.Parse(idString)
            Await dbBrani.SpostaBraniInCategoriaAsync(_braniDaSpostare, targetId)

            listaCategorie.abbandona()

            If listaInfo IsNot Nothing AndAlso Not listaInfo.IsDisposed Then
                listaInfo.abbandona()
            End If

            If listaBrani IsNot Nothing AndAlso Not listaBrani.IsDisposed Then
                Await CaricaDatiPaginaBrani()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile spostare i brani nella categoria selezionata.")
        End Try
    End Sub

    Private Sub AzioneRiproduciSelezione()
        RiproduciBrani(listaBrani.ottieniElementi(), "Nessun brano selezionato è accessibile.")
    End Sub

    Private Sub AzioneRiproduciTutti()
        RiproduciBrani(listaBrani.ottieniElementi(), "Nessun brano nella lista è accessibile.")
    End Sub

    Private Sub RiproduciBrani(percorsi As List(Of String), messaggioErrore As String)
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return

            Dim percorsiValidi As List(Of String) = VerificaEsistenzaBatch(percorsi)

            If percorsiValidi.Count > 0 Then
                menuFile.ApriBrani(percorsiValidi)
            Else
                MostraMessaggio(messaggioErrore)
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Si è verificato un errore durante l'avvio della riproduzione.")
        End Try
    End Sub

    Private Sub ApriBranoDiretto(percorso As String)
        Try
            If VerificaEsistenza(percorso) Then
                menuFile.Apri(percorso)
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Async Sub VisualizzaBraniRecenti()
        Try
            Dim contaRecenti As Long = Await dbBrani.GetContaBraniRecentiAsync()

            If contaRecenti = 0 Then
                MostraMessaggio("Nessun brano recente presente.")
                Return
            End If

            If listaBrani IsNot Nothing Then listaBrani.abbandona()
            listaBrani = New WGMenuLista()
            listaBrani.impostaTitolo("Brani Recenti")
            listaBrani.multiselezioneConsentita = True

            listaBrani.aggiungiOpzione("Riproduci selezione", AddressOf AzioneRiproduciSelezione)
            listaBrani.aggiungiOpzione("Elimina selezione", AddressOf EliminaRecentiSelezionati)
            listaBrani.aggiungiOpzione("Svuota recenti", AddressOf SvuotaRecenti)

            AddHandler listaBrani.ElementoSelezionato, AddressOf ApriBranoDiretto
            AddHandler listaBrani.escPremuto, AddressOf ChiudiListaBrani

            Dim brani As List(Of WGDati.Brano) = Await dbBrani.GetListaRecentiAsync(ordineCorrenteRecenti)
            For Each b As WGDati.Brano In brani
                listaBrani.aggiungiElemento(b.Nome, b.Path)
            Next
            listaBrani.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile caricare i brani recenti.")
        End Try
    End Sub

    Private Async Sub EliminaRecentiSelezionati()
        Try
            Dim elementi As List(Of String) = listaBrani.ottieniElementi()
            If elementi.Count = 0 Then Return

            For Each path As String In elementi
                Await dbBrani.EliminaBranoRecenteAsync(path)
            Next

            Dim conta As Long = Await dbBrani.GetContaBraniRecentiAsync()
            If conta > 0 Then
                VisualizzaBraniRecenti()
            Else
                listaBrani.abbandona()
                MostraMessaggio("Nessun brano recente rimasto.")
            End If
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile eliminare i brani recenti selezionati.")
        End Try
    End Sub

    Private Sub SvuotaRecenti()
        Try
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaSvuotaRecenti
            domanda.avvia("Svuotare l'intera cronologia dei brani recenti?")
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Async Sub RispostaSvuotaRecenti(risposta As DialogResult)
        Try
            If risposta <> DialogResult.Yes Then Return
            Await dbBrani.EliminaTuttiIBraniRecentiAsync()
            listaBrani.abbandona()
            MostraMessaggio("Cronologia svuotata.")
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            MostraMessaggio("Impossibile svuotare la cronologia.")
        End Try
    End Sub

    Public Async Sub AggiungiBranoPreferito(percorso As String)
        Try
            Await dbBrani.AggiungiBranoPreferitoAsync(percorso)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Async Sub RimuoviBranoPreferito(percorso As String)
        Try
            Await dbBrani.RimuoviBranoPreferitoAsync(percorso)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Async Sub AggiungiBranoRecente(percorso As String)
        Try
            Await dbBrani.AggiungiBranoRecenteAsync(percorso)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Async Function IsPreferito(percorso As String) As Task(Of Boolean)
        Try
            Return Await dbBrani.IsPreferitoAsync(percorso)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Async Function IsRecente(percorso As String) As Task(Of Boolean)
        Try
            Return Await dbBrani.IsRecenteAsync(percorso)
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function
End Class