Imports wgcontrols
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports WGPlayerMpv
Imports System.Text.RegularExpressions
Imports System.IO
Imports System.Text.StringBuilder
Imports System.Runtime.InteropServices

' Interfaccia principale per la gestione di YouTube. Permette la ricerca, la riproduzione di video e la gestione dei preferiti.
Public Class YtdlpCapitoliProvider
    Implements ICapitoliProvider

    Private ReadOnly _client As YtdlpClient
    Private ReadOnly _logger As New Logger()

    Public Sub New(client As YtdlpClient)
        _client = client
    End Sub

    Public Function GetCapitoli(url As String, includiSponsorBlock As Boolean) As (Capitoli As List(Of CapitoloInfo), Sponsor As List(Of SponsorSegmentoInfo)) Implements ICapitoliProvider.GetCapitoli
        Dim capitoli As New List(Of CapitoloInfo)
        Dim sponsor As New List(Of SponsorSegmentoInfo)
        Try
            If _client Is Nothing Then Return (capitoli, sponsor)

            Dim risultato = _client.GetCapitoli(url, includiSponsorBlock)

            If risultato.Capitoli IsNot Nothing Then
                For Each c In risultato.Capitoli
                    Try
                        Dim capitolo As New CapitoloInfo()
                        capitolo.Title = c.Title
                        capitolo.StartSeconds = c.StartSeconds
                        capitolo.EndSeconds = c.EndSeconds
                        capitoli.Add(capitolo)
                    Catch exCapitolo As Exception
                        _logger.ScriviLog($"ERRORE [YtdlpCapitoliProvider.GetCapitoli.LoopCapitoli]: {exCapitolo.Message}")
                    End Try
                Next
            End If

            If risultato.Sponsor IsNot Nothing Then
                For Each s In risultato.Sponsor
                    Try
                        Dim segmento As New SponsorSegmentoInfo()
                        segmento.Category = s.Category
                        segmento.Title = s.Title
                        segmento.StartSeconds = s.StartSeconds
                        segmento.EndSeconds = s.EndSeconds
                        sponsor.Add(segmento)
                    Catch exSponsor As Exception
                        _logger.ScriviLog($"ERRORE [YtdlpCapitoliProvider.GetCapitoli.LoopSponsor]: {exSponsor.Message}")
                    End Try
                Next
            End If

            Return (capitoli, sponsor)
        Catch ex As Exception
            _logger.ScriviLog($"ERRORE [YtdlpCapitoliProvider.GetCapitoli]: {ex.ToString()}")
            Return (capitoli, sponsor)
        End Try
    End Function
End Class

''' <summary>
''' Commenti, mi piace e non mi piace per il player: i commenti si leggono con yt-dlp (senza account); mi piace e non mi piace
''' si mettono con l'account di YouTube del browser di WinGabriel, lo stesso delle iscrizioni online.
''' </summary>
Public Class YoutubeInterazioniProvider
    Implements IInterazioniVideoProvider

    Private ReadOnly _client As YtdlpClient
    Private ReadOnly _logger As New Logger()
    Private _ultimoErrore As String = ""

    Public Sub New(client As YtdlpClient)
        _client = client
    End Sub

    Public ReadOnly Property UltimoErrore As String Implements IInterazioniVideoProvider.UltimoErrore
        Get
            Return _ultimoErrore
        End Get
    End Property

    Private Shared Function IdVideo(url As String) As String
        If String.IsNullOrWhiteSpace(url) Then Return ""
        Dim m As Match = Regex.Match(url, "(?:[?&]v=|youtu\.be/|/shorts/|/live/|/embed/)([A-Za-z0-9_-]{6,20})")
        If m.Success Then Return m.Groups(1).Value
        If Regex.IsMatch(url.Trim(), "^[A-Za-z0-9_-]{11}$") Then Return url.Trim()
        Return ""
    End Function

    Private Shared Function Converti(c As YoutubeComment) As CommentoInfo
        Dim r As New CommentoInfo() With {.Autore = c.Author, .Testo = c.Text, .Data = c.Timestamp, .MiPiace = c.LikeCount,
                                          .InEvidenza = c.IsPinned, .DelCanale = c.AuthorIsUploader}
        If c.Replies IsNot Nothing Then
            For Each risposta As YoutubeComment In c.Replies
                r.Risposte.Add(Converti(risposta))
            Next
        End If
        Return r
    End Function

    Public Function GetCommenti(url As String) As List(Of CommentoInfo) Implements IInterazioniVideoProvider.GetCommenti
        Try
            _ultimoErrore = ""
            Dim id As String = IdVideo(url)
            If id = "" OrElse _client Is Nothing Then
                _ultimoErrore = "Di questo video non ho l'identificativo che serve a YouTube."
                Return Nothing
            End If
            Dim commenti As List(Of YoutubeComment) = _client.GetVideoComments(id)
            If commenti Is Nothing Then
                _ultimoErrore = "Non è stato possibile leggere i commenti."
                Return Nothing
            End If
            Dim risultato As New List(Of CommentoInfo)()
            For Each c As YoutubeComment In commenti
                If c IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(c.Text) Then risultato.Add(Converti(c))
            Next
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog($"ERRORE [YoutubeInterazioniProvider.GetCommenti]: {ex.ToString()}")
            _ultimoErrore = "Non è stato possibile leggere i commenti."
            Return Nothing
        End Try
    End Function

    Public Function Reagisci(url As String, reazione As String) As Boolean Implements IInterazioniVideoProvider.Reagisci
        Try
            _ultimoErrore = ""
            Dim id As String = IdVideo(url)
            If id = "" Then
                _ultimoErrore = "Di questo video non ho l'identificativo che serve a YouTube."
                Return False
            End If
            If Not WGPaginaNascosta.Disponibile Then
                _ultimoErrore = "Per usare il tuo account serve il browser di WinGabriel, che in questo momento non è disponibile."
                Return False
            End If
            If Not YoutubeOnline.HaAccessoAsync().GetAwaiter().GetResult() Then
                _ultimoErrore = "Nel browser di WinGabriel non hai ancora fatto l'accesso a YouTube. Accedi dal browser e poi riprova."
                Return False
            End If
            Dim riuscito As Boolean
            Select Case reazione
                Case "mipiace"
                    riuscito = YoutubeOnline.MiPiaceAsync(id, True).GetAwaiter().GetResult()
                Case "nonmipiace"
                    riuscito = YoutubeOnline.NonMiPiaceAsync(id).GetAwaiter().GetResult()
                Case Else
                    riuscito = YoutubeOnline.MiPiaceAsync(id, False).GetAwaiter().GetResult()
            End Select
            If Not riuscito Then _ultimoErrore = If(String.IsNullOrWhiteSpace(YoutubeOnline.UltimoErrore), "YouTube non ha accettato la richiesta.", YoutubeOnline.UltimoErrore)
            Return riuscito
        Catch ex As Exception
            _logger.ScriviLog($"ERRORE [YoutubeInterazioniProvider.Reagisci]: {ex.ToString()}")
            _ultimoErrore = "Non è stato possibile parlare con YouTube."
            Return False
        End Try
    End Function
End Class

Partial Public Class MenuYoutube
    Private ricercacampo As WGCampoTesto
    Private campoUrl As WGCampoTesto
    Private WithEvents elencorisultati As WGMenuLista
    Private WithEvents elencoContenuti As WGMenuLista
    Private WithEvents elencoPreferiti As WGMenuLista
    Private WithEvents elencoCategorie As WGMenuLista
    Private WithEvents elencoPlaylistCanale As WGMenuLista
    Private WithEvents elencoSceltaCategoria As WGMenuLista
    Private WithEvents elencoCronologia As WGMenuLista
    Private WithEvents elencoCronologiaRicerche As WGMenuLista
    Private WithEvents elencoIscrizioni As WGMenuLista
    Private WithEvents elencoCanaliNuovi As WGMenuLista
    Private WithEvents elencoVideoNuovi As WGMenuLista
    Private searcher As YtdlpClient
    Private _gestoreXml As XmlManager
    Private menuPrincipale As WGMenu
    Private menuRicerca As WGMenu
    Private menuRiproduzionePlaylist As WGMenu
    Private menuCanale As WGMenu
    Private menuPreferitiMain As WGMenu
    Private menuOrdinamento As WGMenu
    Private menuOpzioniPlaylist As WGMenu
    Private WithEvents listaInformazioni As WGMenuLista
    Private WithEvents listaCommenti As WGMenuLista
    Private campoTestoLungo As WGCampo
    Private campoRiassuntoVideo As WGCampo
    Private _idRiassuntoVideo As Integer = 0
    Private campoNuovaCategoria As WGCampoTesto
    Private risultatiRicercaCorrente As List(Of YoutubeSearchResult)
    Private risultatiContenutiCorrenti As List(Of YoutubeSearchResult)
    Private risultatiPlaylistCanale As List(Of YoutubeSearchResult)
    Private risultatiCommentiCorrenti As List(Of YoutubeComment)
    Private risultatiPreferitiCorrenti As List(Of ElementoSalvato)
    Private elementoSelezionatoCorrente As YoutubeSearchResult
    Private elementoContenutoSelezionato As YoutubeSearchResult
    Private _elementoDaAggiungereTemp As YoutubeSearchResult
    Private _dettagliElementoCorrente As YoutubeItemDetails
    Private processoincorso As WGProcessoInCorso
    Private logger As New Logger()
    Private idRicerca As String
    Private queryRicerca As String
    Private _tipoRicercaCorrente As YoutubeItemType
    Private _risultatiRicercheCorrenti As List(Of ElementoCronologia)
    Private _ricercaDaCronologia As Boolean = False
    Private _longTextStorage As New Dictionary(Of String, String)()
    Private Const LONG_TEXT_PREFIX As String = "LT_"
    Private Const DIMENSIONE_PAGINA_RICERCA As Integer = 50
    Private Const SOGLIA_TESTO_LUNGO As Integer = 200
    Private Const SESSION_RICERCA As String = "ricerca"
    Private Const SESSION_CONTENUTI As String = "contenuti"
    Private Const SESSION_PLAYLIST_CANALE As String = "playlistCanale"
    Private Const SESSION_COMMENTI As String = "commenti"
    Private Const DIMENSIONE_PAGINA_NUOVI As Integer = 50
    Private Const LIMITE_CARICAMENTO_NUOVI As Integer = 1000
    Private Const VIDEO_CONTROLLO_PREDEFINITO As Integer = 15
    Private Const INTERVALLO_CONTROLLO_PREDEFINITO As Integer = 60
    Private _playerForm As WGPlayerFormInt
    Private _tempUrlAnalisi As String
    Private _tempTipoContext As YoutubeItemType
    Private _tempFiltroCategoria As String
    Private _contextListaOrdinamento As Object
    Private _contextDatiOrdinamento As Object
    Private _contextCallbackOrdinamento As Action(Of Object, Object)
    Private _contextCriterioOrdinamento As String
    Private _tempIsShuffle As Boolean
    Private _tempCategoriaDestinazione As String

    Private _sortByRicerca As YoutubeSortBy = YoutubeSortBy.None
    Private _minDurataRicerca As Long? = Nothing
    Private _maxDurataRicerca As Long? = Nothing
    Private _sortByContenuti As YoutubeSortBy = YoutubeSortBy.None
    Private _minDurataContenuti As Long? = Nothing
    Private _maxDurataContenuti As Long? = Nothing
    Private _sortByPlaylistCanale As YoutubeSortBy = YoutubeSortBy.None
    Private _contextSortDaApplicare As YoutubeSortBy = YoutubeSortBy.None
    Private _contextMinDurataDaApplicare As Long? = Nothing
    Private _contextMaxDurataDaApplicare As Long? = Nothing
    Private _contextTipoListaOrdinamento As String = ""

    Private Const DIMENSIONE_PAGINA_DB As Integer = 50
    Private _offsetPreferiti As Long = 0
    Private _ordinePreferiti As OrdinamentoElementi = OrdinamentoElementi.DataDiscendente
    Private _haAltrePaginePreferiti As Boolean = False
    Private _preferitiSelezionaTuttePagine As Boolean = False
    Private _offsetCronologia As Long = 0
    Private _ordineCronologia As OrdinamentoElementi = OrdinamentoElementi.DataDiscendente
    Private _haAltrePagineCronologia As Boolean = False
    Private _offsetRicerche As Long = 0
    Private _haAltrePagineRicerche As Boolean = False
    Private _posizioneDopoCambioPagina As String = ""

    Private campoRicercaInterna As WGCampoTesto
    Private campoRicercaIscrizioni As WGCampoTesto
    Private _iscrizioniCorrenti As List(Of IscrizioneCanale)
    Private _canaliConNuoviVideo As List(Of CanaleConNuoviVideo)
    Private _videoNuoviTutti As List(Of VideoNuovo)
    Private _videoNuoviCache As List(Of VideoNuovo)
    Private _videoNuoviCorrenti As List(Of VideoNuovo)
    Private _offsetVideoNuovi As Integer = 0
    Private _titoloElencoVideoNuovi As String = ""
    Private _idCanaleContestoIscrizione As String
    Private _queryRicercaInterna As String
    Private _contestoRicercaInterna As String = ""
    Private _nuoviVideoTrovatiControllo As Integer = 0
    Private Shared ReadOnly loggerStatico As New Logger()

    Private Delegate Function ThreadProc(lpParameter As IntPtr) As Integer

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CreateThread(lpThreadAttributes As IntPtr, dwStackSize As IntPtr, lpStartAddress As ThreadProc, lpParameter As IntPtr, dwCreationFlags As UInteger, ByRef lpThreadId As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    Private Shared Function NativeThreadRunner(lpParameter As IntPtr) As Integer
        Dim handle = GCHandle.FromIntPtr(lpParameter)
        Dim action As Action = CType(handle.Target, Action)
        Try
            action()
        Finally
            handle.Free()
        End Try
        Return 0
    End Function

    Private Sub AvviaThread(azione As Action)
        Dim handle = GCHandle.Alloc(azione)
        Dim ptr = GCHandle.ToIntPtr(handle)
        Dim threadId As UInteger
        Dim hThread = CreateThread(IntPtr.Zero, IntPtr.Zero, AddressOf NativeThreadRunner, ptr, 0, threadId)
        CloseHandle(hThread)
    End Sub

    Public Sub avvia()
        Try
            AssicuraInizializzazione()
            MostraMenuPrincipale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.avvia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AssicuraInizializzazione()
        Try
            If searcher Is Nothing Then searcher = New YtdlpClient()
            If _gestoreXml Is Nothing Then _gestoreXml = New XmlManager()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AssicuraInizializzazione]: {ex.ToString()}")
        End Try
    End Sub

    Public Sub AvviaVideoDaVedere()
        Try
            AssicuraInizializzazione()
            MostraElencoVideoNuovi(Nothing, "Nuovi video da vedere")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaVideoDaVedere]: {ex.ToString()}")
        End Try
    End Sub

    Public Sub AvviaCanaliConNuoviVideo()
        Try
            AssicuraInizializzazione()
            MostraCanaliConNuoviVideo()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaCanaliConNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiSuThreadUI(azione As Action)
        Try
            If Application.OpenForms.Count > 0 Then
                Dim mainForm = Application.OpenForms(0)
                If mainForm.InvokeRequired Then
                    mainForm.Invoke(azione)
                Else
                    azione.Invoke()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiSuThreadUI]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuPrincipale()
        EseguiSuThreadUI(AddressOf UI_MostraMenuPrincipale)
    End Sub

    Private Sub UI_MostraMenuPrincipale()
        Try
            menuPrincipale = New WGMenu()
            menuPrincipale.aggiungi("Ricerca", AddressOf AvviaCampoRicerca)
            menuPrincipale.aggiungi("Scrivi URL", AddressOf AvviaCampoUrl)
            menuPrincipale.aggiungi("Preferiti", AddressOf MostraMenuPreferitiMain)
            menuPrincipale.aggiungi("Iscrizioni", AddressOf MostraMenuIscrizioni)
            menuPrincipale.aggiungi("Cronologia", AddressOf MostraMenuCronologia)
            menuPrincipale.aggiungi("Il tuo account", AddressOf MostraMenuAccount)
            menuPrincipale.aggiungi("Aggiorna", AddressOf AggiornaYtDlp)
            menuPrincipale.aggiungi("Impostazioni", AddressOf UI_MostraImpostazioni)
            menuPrincipale.avvia("Menu YouTube")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuPrincipale]: {ex.ToString()}")
        End Try
    End Sub

    Public Sub MostraImpostazioni()
        EseguiSuThreadUI(AddressOf UI_MostraImpostazioni)
    End Sub

    Private Sub UI_MostraImpostazioni()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Player e riproduzione", AddressOf UI_MenuGruppoPlayer)
            m.aggiungi("Ricerca", AddressOf UI_MenuGruppoRicerca)
            m.aggiungi("Ordinamento e filtri", AddressOf UI_MenuGruppoOrdinamento)
            m.aggiungi("Iscrizioni e nuovi video", AddressOf UI_MenuGruppoIscrizioni)
            m.avvia("Impostazioni YouTube")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraImpostazioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuGruppoPlayer()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Player per i video di YouTube", AddressOf UI_MenuPlayerYoutube)
            m.aggiungi("SponsorBlock", AddressOf UI_MenuSponsorBlockAbilitato)
            m.aggiungi("Annuncio quando SponsorBlock salta uno sponsor", AddressOf UI_MenuSponsorBlockAnnuncio)
            m.aggiungi("Lettura automatica dei titoli dei capitoli", AddressOf UI_MenuLetturaCapitoliAutomatica)
            m.avvia("Player e riproduzione")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuGruppoPlayer]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuGruppoRicerca()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tipo di ricerca predefinito", AddressOf UI_MenuTipoRicercaPredefinito)
            m.avvia("Ricerca")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuGruppoRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuGruppoOrdinamento()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Ordinamento predefinito video", AddressOf UI_MenuOrdinamentoPreferitoVideo)
            m.aggiungi("Ordinamento predefinito canali", AddressOf UI_MenuOrdinamentoPreferitoCanali)
            m.aggiungi("Ordinamento predefinito playlist", AddressOf UI_MenuOrdinamentoPreferitoPlaylist)
            m.aggiungi("Filtro durata video predefinito", AddressOf UI_MenuFiltroDurataPreferito)
            m.aggiungi("Filtri predefiniti della ricerca", AddressOf UI_MenuFiltriPredefiniti)
            m.avvia("Ordinamento e filtri")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuGruppoOrdinamento]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuTipoRicercaPredefinito()
        Try
            Dim config As New WGConfig()
            Dim attuale = LeggiConfigValoreYoutube(config, "tipo_ricerca_predefinito", "Chiedi")
            Dim m As New WGMenu()
            m.aggiungi(If(attuale = "Chiedi", "Chiedi ogni volta [attuale]", "Chiedi ogni volta"),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, "tipo_ricerca_predefinito", "Chiedi", "Ad ogni ricerca verrà chiesto il tipo (Video, Canali, Playlist, Misti)."))
            m.aggiungi(If(attuale = "Unknown", "Contenuti misti [attuale]", "Contenuti misti"),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, "tipo_ricerca_predefinito", "Unknown", "Le ricerche partiranno sempre con risultati misti."))
            m.aggiungi(If(attuale = "Video", "Solo video [attuale]", "Solo video"),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, "tipo_ricerca_predefinito", "Video", "Le ricerche partiranno sempre sui video."))
            m.aggiungi(If(attuale = "Channel", "Solo canali [attuale]", "Solo canali"),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, "tipo_ricerca_predefinito", "Channel", "Le ricerche partiranno sempre sui canali."))
            m.aggiungi(If(attuale = "Playlist", "Solo playlist [attuale]", "Solo playlist"),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, "tipo_ricerca_predefinito", "Playlist", "Le ricerche partiranno sempre sulle playlist."))
            m.avvia("Tipo di ricerca predefinito", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuTipoRicercaPredefinito]: {ex.ToString()}")
        End Try
    End Sub

    Private Function LeggiTipoRicercaPredefinito() As String
        Try
            Dim config As New WGConfig()
            Return LeggiConfigValoreYoutube(config, "tipo_ricerca_predefinito", "Chiedi")
        Catch
            Return "Chiedi"
        End Try
    End Function

    Private Function TipoRicercaDaConfigString(valore As String) As YoutubeItemType
        Select Case If(valore, "").Trim()
            Case "Video" : Return YoutubeItemType.Video
            Case "Channel" : Return YoutubeItemType.Channel
            Case "Playlist" : Return YoutubeItemType.Playlist
            Case "Unknown" : Return YoutubeItemType.Unknown
            Case Else : Return YoutubeItemType.Unknown
        End Select
    End Function

    Private Function LeggiConfigValoreYoutube(configManager As WGConfig, chiave As String, defaultVal As String) As String
        Try
            Dim valore As String = configManager.LeggiValore(chiave)
            If String.IsNullOrEmpty(valore) Then Return defaultVal
            Return valore
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LeggiConfigValoreYoutube]: {ex.ToString()}")
            Return defaultVal
        End Try
    End Function

    Private Sub SalvaConfigurazioneYoutube(configManager As WGConfig, id As String, valore As String)
        Try
            If configManager.EsisteId(id) Then
                configManager.ModificaValore(id, valore)
            Else
                configManager.AggiungiValore(id, valore)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SalvaConfigurazioneYoutube]: {ex.ToString()}")
        End Try
    End Sub

    Private Function IndiceBinarioYoutube(configManager As WGConfig, chiave As String, valorePrimaVoce As String, defaultVal As String) As Integer
        Try
            If String.Equals(LeggiConfigValoreYoutube(configManager, chiave, defaultVal), valorePrimaVoce, StringComparison.OrdinalIgnoreCase) Then Return 0
            Return 1
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.IndiceBinarioYoutube]: {ex.ToString()}")
            Return 0
        End Try
    End Function

    Private Sub UI_SalvaImpostazioneYoutubeConMessaggio(configManager As WGConfig, id As String, valore As String, messaggioFeedback As String)
        Try
            SalvaConfigurazioneYoutube(configManager, id, valore)
            If Not String.IsNullOrEmpty(messaggioFeedback) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio(messaggioFeedback)
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_SalvaImpostazioneYoutubeConMessaggio]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuPlayerYoutube()
        Try
            Dim configManager As New WGConfig()
            Dim m As New WGMenu()
            m.aggiungi("Lettore interno MPV", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "playeryoutube", "mpv", "I video di YouTube verranno riprodotti con il lettore interno MPV."))
            m.aggiungi("Lettore web WebView", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "playeryoutube", "browser", "I video di YouTube verranno riprodotti con il lettore web WebView. Capitoli e SponsorBlock non sono disponibili in questa modalità."))
            m.posiziona(IndiceBinarioYoutube(configManager, "playeryoutube", "mpv", "mpv"))
            m.avvia("Player per i video di YouTube", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuPlayerYoutube]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuSponsorBlockAbilitato()
        Try
            Dim configManager As New WGConfig()
            Dim m As New WGMenu()
            m.aggiungi("Attiva SponsorBlock", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "sponsorblock_abilitato", "true", "SponsorBlock attivato: gli sponsor verranno saltati automaticamente durante la riproduzione con il lettore interno MPV."))
            m.aggiungi("Disattiva SponsorBlock", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "sponsorblock_abilitato", "false", "SponsorBlock disattivato."))
            m.posiziona(IndiceBinarioYoutube(configManager, "sponsorblock_abilitato", "true", "false"))
            m.avvia("SponsorBlock", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuSponsorBlockAbilitato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuSponsorBlockAnnuncio()
        Try
            Dim configManager As New WGConfig()
            Dim m As New WGMenu()
            m.aggiungi("Annuncia quando viene saltato uno sponsor", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "sponsorblock_annuncio", "true", "Verrà annunciato ogni volta che SponsorBlock salta uno sponsor."))
            m.aggiungi("Non annunciare", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "sponsorblock_annuncio", "false", "Lo sponsor verrà saltato senza annuncio."))
            m.posiziona(IndiceBinarioYoutube(configManager, "sponsorblock_annuncio", "true", "false"))
            m.avvia("Annuncio SponsorBlock", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuSponsorBlockAnnuncio]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuLetturaCapitoliAutomatica()
        Try
            Dim configManager As New WGConfig()
            Dim m As New WGMenu()
            m.aggiungi("Leggi automaticamente il titolo del capitolo", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "lettura_capitoli_automatica", "true", "Il titolo del capitolo verrà letto automaticamente quando la riproduzione lo raggiunge."))
            m.aggiungi("Non leggere automaticamente", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "lettura_capitoli_automatica", "false", "I titoli dei capitoli non verranno più letti automaticamente."))
            m.posiziona(IndiceBinarioYoutube(configManager, "lettura_capitoli_automatica", "true", "false"))
            m.avvia("Lettura automatica dei capitoli", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuLetturaCapitoliAutomatica]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AvviaCampoRicerca()
        EseguiSuThreadUI(AddressOf UI_AvviaCampoRicerca)
    End Sub

    Private Sub UI_AvviaCampoRicerca()
        Try
            ricercacampo = New WGCampoTesto()
            ricercacampo.init("Immetti termini da cercare su YouTube")
            AddHandler ricercacampo.chiusura, AddressOf EseguiRicercaPrincipale
            ricercacampo.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaCampoRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AvviaCampoUrl()
        EseguiSuThreadUI(AddressOf UI_AvviaCampoUrl)
    End Sub

    Private Sub UI_AvviaCampoUrl()
        Try
            campoUrl = New WGCampoTesto()
            campoUrl.init("Inserisci URL Video, Playlist o Canale")
            AddHandler campoUrl.chiusura, AddressOf EseguiAnalisiUrl
            campoUrl.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaCampoUrl]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiAnalisiUrl()
        Try
            Dim url As String = campoUrl.OttieniTesto()
            campoUrl.chiudi()
            If String.IsNullOrWhiteSpace(url) Then
                Return
            End If

            _tempUrlAnalisi = url
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Analisi URL in corso...")

            AvviaThread(AddressOf ThreadLavoroAnalisiUrl)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiAnalisiUrl]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroAnalisiUrl()
        LavoroAnalisiUrl(_tempUrlAnalisi)
    End Sub

    Private Sub LavoroAnalisiUrl(url As String)
        Try
            Dim item As YoutubeSearchResult = searcher.RecuperaMetadatiUrl(url)
            _elementoDaAggiungereTemp = item
            EseguiSuThreadUI(AddressOf UI_PostAnalisiUrl)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroAnalisiUrl]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostAnalisiUrl()
        ChiudiProcessoInCorso()
        Dim item As YoutubeSearchResult = _elementoDaAggiungereTemp
        If item IsNot Nothing Then
            elementoSelezionatoCorrente = item
            _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)

            If item.ItemType = YoutubeItemType.Video Then
                AvviaPlayer(item.Title, New List(Of String) From {item.Url})
            ElseIf item.ItemType = YoutubeItemType.Playlist Then
                MostraMenuContestualePlaylist()
            ElseIf item.ItemType = YoutubeItemType.Channel Then
                MostraMenuCanale()
            End If
        Else
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile recuperare informazioni dall'URL.")
            msg.ShowDialog()
        End If
    End Sub

    Private Sub AggiornaYtDlp()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Aggiornamento libreria YtDlp In Corso, attendere...")
            AvviaThread(AddressOf ThreadAggiornaYtDlp)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaYtDlp.Start]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadAggiornaYtDlp()
        Try
            Dim startInfo As New ProcessStartInfo()
            startInfo.FileName = "yt-dlp.exe"
            startInfo.Arguments = "-U"
            startInfo.UseShellExecute = False
            startInfo.CreateNoWindow = True
            Dim proc = Process.Start(startInfo)
            proc.WaitForExit()

            EseguiSuThreadUI(AddressOf UI_MessaggioAggiornamentoOK)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaYtDlp]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_MessaggioAggiornamentoErrore)
        Finally
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_MessaggioAggiornamentoOK()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Aggiornamento completato.")
        msg.ShowDialog()
    End Sub

    Private Sub UI_MessaggioAggiornamentoErrore()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Errore aggiornamento.")
        msg.ShowDialog()
    End Sub

    Private Sub MostraMenuPreferitiMain()
        EseguiSuThreadUI(AddressOf UI_MostraMenuPreferitiMain)
    End Sub

    Private Sub UI_MostraMenuPreferitiMain()
        Try
            menuPreferitiMain = New WGMenu()
            menuPreferitiMain.aggiungi("Video Preferiti", AddressOf ClickPreferitiVideo)
            menuPreferitiMain.aggiungi("Canali Preferiti", AddressOf ClickPreferitiCanali)
            menuPreferitiMain.aggiungi("Playlist Preferite", AddressOf ClickPreferitiPlaylist)
            menuPreferitiMain.avvia("Gestione Preferiti", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuPreferitiMain]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickPreferitiVideo()
        GestisciSezionePreferiti(YoutubeItemType.Video)
    End Sub

    Private Sub ClickPreferitiCanali()
        GestisciSezionePreferiti(YoutubeItemType.Channel)
    End Sub

    Private Sub ClickPreferitiPlaylist()
        GestisciSezionePreferiti(YoutubeItemType.Playlist)
    End Sub

    Private Sub GestisciSezionePreferiti(tipo As YoutubeItemType)
        Try
            _tempTipoContext = tipo
            Dim menuSezione = New WGMenu()
            menuSezione.aggiungi("Consulta Elementi", AddressOf ClickConsultaElementiPreferiti)
            menuSezione.aggiungi("Consulta Categorie", AddressOf ClickConsultaCategoriePreferiti)
            menuSezione.avvia($"Preferiti: {GetTipoStringa(tipo)}", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.GestisciSezionePreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickConsultaElementiPreferiti()
        MostraElencoPreferiti(_tempTipoContext)
    End Sub

    Private Sub ClickConsultaCategoriePreferiti()
        MostraElencoCategorie(_tempTipoContext)
    End Sub

    Private Sub MostraElencoPreferiti(tipo As YoutubeItemType, Optional filtroCategoria As String = Nothing)
        Try
            _tempFiltroCategoria = filtroCategoria
            _tempTipoContext = tipo
            _offsetPreferiti = 0
            _preferitiSelezionaTuttePagine = False
            _ordinePreferiti = OrdinamentoElementi.DataDiscendente
            CaricaPaginaPreferiti(posizionaInizio:=True, primaApertura:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CaricaPaginaPreferiti(Optional posizionaInizio As Boolean = True, Optional primaApertura As Boolean = False)
        Try
            Dim items = _gestoreXml.OttieniPreferitiPagina(_tempTipoContext, _tempFiltroCategoria, _ordinePreferiti, _offsetPreferiti, DIMENSIONE_PAGINA_DB)
            Dim totale = _gestoreXml.ContaPreferiti(_tempTipoContext, _tempFiltroCategoria)
            _haAltrePaginePreferiti = (_offsetPreferiti + items.Count) < totale

            If items.Count = 0 AndAlso _offsetPreferiti = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun elemento presente nei preferiti.")
                msg.ShowDialog()
                Return
            End If

            If items.Count = 0 AndAlso _offsetPreferiti > 0 Then
                _offsetPreferiti = Math.Max(0, _offsetPreferiti - DIMENSIONE_PAGINA_DB)
                items = _gestoreXml.OttieniPreferitiPagina(_tempTipoContext, _tempFiltroCategoria, _ordinePreferiti, _offsetPreferiti, DIMENSIONE_PAGINA_DB)
                _haAltrePaginePreferiti = False
                posizionaInizio = True
            End If

            risultatiPreferitiCorrenti = items

            If primaApertura OrElse elencoPreferiti Is Nothing OrElse elencoPreferiti.IsDisposed Then
                elencoPreferiti = New WGMenuLista()
                elencoPreferiti.ciclono = True
                elencoPreferiti.multiselezioneConsentita = True
                elencoPreferiti.impostaTitolo(If(_tempFiltroCategoria Is Nothing, "Tutti gli elementi", $"Categoria: {_tempFiltroCategoria}"))

                elencoPreferiti.aggiungiOpzione("Ordina", AddressOf ClickOrdinaPreferiti)
                elencoPreferiti.aggiungiOpzione("Elimina dai preferiti", AddressOf ClickEliminaPreferiti)
                elencoPreferiti.aggiungiOpzione("Svuota tutti i preferiti", AddressOf ClickSvuotaPreferiti)
                elencoPreferiti.aggiungiOpzione("Sposta in categoria", AddressOf ClickSpostaPreferiti)
                elencoPreferiti.aggiungiOpzione("Seleziona tutti (tutte le pagine)", AddressOf ClickSelezionaTuttiPreferiti)

                If _tempTipoContext = YoutubeItemType.Video Then
                    elencoPreferiti.aggiungiOpzione("Riproduci", AddressOf ClickRiproduciPreferiti)
                    If RiassuntoVideoDisponibile() Then elencoPreferiti.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoPreferiti)
                    AddHandler elencoPreferiti.ElementoSelezionato, AddressOf ClickRiproduciPreferiti
                End If

                If _tempTipoContext = YoutubeItemType.Playlist Then
                    AddHandler elencoPreferiti.ElementoSelezionato, AddressOf HandlerElementoPreferitoPlaylist
                End If

                If _tempTipoContext = YoutubeItemType.Channel Then
                    elencoPreferiti.aggiungiOpzione("Apri Canale", AddressOf ClickApriCanalePreferito)
                End If

                AddHandler elencoPreferiti.iniziolista, AddressOf HandlerInizioListaPreferiti
                AddHandler elencoPreferiti.finelista, AddressOf HandlerFineListaPreferiti

                PopolaElementiPreferiti()
                AggiornaOpzioniPaginazionePreferiti()
                elencoPreferiti.avvia()
            Else
                elencoPreferiti.pulisci()
                PopolaElementiPreferiti()
                AggiornaOpzioniPaginazionePreferiti()
                If posizionaInizio Then
                    If risultatiPreferitiCorrenti.Count > 0 Then elencoPreferiti.selezionaElemento(0)
                Else
                    elencoPreferiti.SelezionaUltimoElemento()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CaricaPaginaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaElementiPreferiti()
        Try
            For Each item In risultatiPreferitiCorrenti
                Try
                    elencoPreferiti.aggiungiElemento(item.Titolo, item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiPreferiti.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzioniPaginazionePreferiti()
        Try
            If elencoPreferiti Is Nothing OrElse elencoPreferiti.IsDisposed Then Return
            elencoPreferiti.rimuoviOpzione("Pagina precedente")
            elencoPreferiti.rimuoviOpzione("Pagina successiva")
            If _offsetPreferiti > 0 Then
                elencoPreferiti.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedentePreferiti)
            End If
            If _haAltrePaginePreferiti Then
                elencoPreferiti.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaPreferiti)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzioniPaginazionePreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaPreferiti()
        Try
            _offsetPreferiti += DIMENSIONE_PAGINA_DB
            CaricaPaginaPreferiti(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedentePreferiti()
        Try
            _offsetPreferiti = Math.Max(0, _offsetPreferiti - DIMENSIONE_PAGINA_DB)
            CaricaPaginaPreferiti(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedentePreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaPreferiti(sender As Object, e As EventArgs)
        Try
            If _offsetPreferiti <= 0 Then Return
            _offsetPreferiti = Math.Max(0, _offsetPreferiti - DIMENSIONE_PAGINA_DB)
            CaricaPaginaPreferiti(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaPreferiti(sender As Object, e As EventArgs)
        Try
            If Not _haAltrePaginePreferiti Then Return
            _offsetPreferiti += DIMENSIONE_PAGINA_DB
            CaricaPaginaPreferiti(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickSelezionaTuttiPreferiti()
        Try
            _preferitiSelezionaTuttePagine = True
            elencoPreferiti.selezionaTuttiGliElementi()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Selezione di tutte le pagine attivata. Usa Riproduci per riprodurre tutti gli elementi.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickSelezionaTuttiPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaPreferiti()
        Try
            Dim menuOrd As New WGMenu()
            menuOrd.aggiungi("Data (più recenti)", AddressOf SubOrdinaPreferitiDataDesc)
            menuOrd.aggiungi("Data (più vecchi)", AddressOf SubOrdinaPreferitiDataAsc)
            menuOrd.aggiungi("Titolo A-Z", AddressOf SubOrdinaPreferitiTitoloAsc)
            menuOrd.aggiungi("Titolo Z-A", AddressOf SubOrdinaPreferitiTitoloDesc)
            menuOrd.avvia("Ordina preferiti", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickOrdinaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SubOrdinaPreferitiDataDesc()
        _ordinePreferiti = OrdinamentoElementi.DataDiscendente
        _offsetPreferiti = 0
        CaricaPaginaPreferiti(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaPreferitiDataAsc()
        _ordinePreferiti = OrdinamentoElementi.DataAscendente
        _offsetPreferiti = 0
        CaricaPaginaPreferiti(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaPreferitiTitoloAsc()
        _ordinePreferiti = OrdinamentoElementi.TitoloAscendente
        _offsetPreferiti = 0
        CaricaPaginaPreferiti(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaPreferitiTitoloDesc()
        _ordinePreferiti = OrdinamentoElementi.TitoloDiscendente
        _offsetPreferiti = 0
        CaricaPaginaPreferiti(posizionaInizio:=True)
    End Sub

    Private Sub ClickEliminaPreferiti()
        AzioneEliminaDaPreferiti(_tempTipoContext)
    End Sub

    Private Sub ClickSvuotaPreferiti()
        Try
            _gestoreXml.SvuotaPreferiti(_tempTipoContext)
            risultatiPreferitiCorrenti = New List(Of ElementoSalvato)

            If elencoPreferiti IsNot Nothing Then
                elencoPreferiti.abbandona()
                elencoPreferiti = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickSvuotaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickSpostaPreferiti()
        AzioneSpostaInCategoria(_tempTipoContext)
    End Sub

    Private Sub ClickRiproduciPreferiti()
        AzioneRiproduciPreferito(_tempTipoContext)
    End Sub

    Private Sub HandlerElementoPreferitoPlaylist()
        Try
            Dim ids = elencoPreferiti.ottieniElementi()
            If ids.Count > 0 Then
                Dim itemSav = TrovaPreferitoPerId(ids(0))
                If itemSav IsNot Nothing Then
                    elementoSelezionatoCorrente = ConvertiSalvatoInSearchResult(itemSav)
                    MostraMenuContestualePlaylist()
                Else
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoPreferiti.HandlerPlaylist]: {ex.ToString()}")
        End Try
    End Sub

    Private Function TrovaPreferitoPerId(id As String) As ElementoSalvato
        For Each item In risultatiPreferitiCorrenti
            Try
                If item.Id = id Then Return item
            Catch ex As Exception
                logger.ScriviLog($"ERRORE [MenuYoutube.TrovaPreferitoPerId.Loop]: {ex.ToString()}")
            End Try
        Next
        Return Nothing
    End Function

    Private Sub ClickApriCanalePreferito()
        AzioneApriCanalePreferito()
    End Sub

    Private Sub AggiornaListaPreferiti(lista As Object, dati As Object)
        Try
            Dim l = CType(lista, WGMenuLista)
            Dim d = CType(dati, List(Of ElementoSalvato))
            l.pulisci()
            For Each item In d
                Try
                    l.aggiungiElemento(item.Titolo, item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaListaPreferiti.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaListaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneRiproduciPreferito(tipo As YoutubeItemType)
        Try
            Dim itemsToPlay As New List(Of String)
            Dim firstTitle As String = ""
            Dim foundFirst As Boolean = False

            If _preferitiSelezionaTuttePagine Then
                Dim tutti = _gestoreXml.OttieniPreferitiPagina(tipo, _tempFiltroCategoria, _ordinePreferiti, 0, Integer.MaxValue)
                For Each item In tutti
                    Try
                        If item IsNot Nothing AndAlso Not String.IsNullOrEmpty(item.Url) Then
                            itemsToPlay.Add(item.Url)
                            _gestoreXml.AggiungiRecente(item.Id, item.Titolo, item.Url, item.Tipo)
                            If Not foundFirst Then
                                firstTitle = item.Titolo
                                foundFirst = True
                            End If
                        End If
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.AzioneRiproduciPreferito.Tutti.Loop]: {ex.ToString()}")
                    End Try
                Next
                _preferitiSelezionaTuttePagine = False
            Else
                Dim ids = elencoPreferiti.ottieniElementi()
                If ids Is Nothing OrElse ids.Count = 0 Then
                    Return
                End If

                For Each id In ids
                    Try
                        Dim item = TrovaPreferitoPerId(id)
                        If item IsNot Nothing Then
                            itemsToPlay.Add(item.Url)
                            _gestoreXml.AggiungiRecente(item.Id, item.Titolo, item.Url, item.Tipo)
                            If Not foundFirst Then
                                firstTitle = item.Titolo
                                foundFirst = True
                            End If
                        End If
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.AzioneRiproduciPreferito.Loop]: {ex.ToString()}")
                    End Try
                Next
            End If

            If itemsToPlay.Count > 0 Then
                AvviaPlayer(If(itemsToPlay.Count = 1, firstTitle, "Riproduzione Preferiti"), itemsToPlay)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneRiproduciPreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneApriCanalePreferito()
        Try
            Dim ids = elencoPreferiti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If
            Dim item = TrovaPreferitoPerId(ids(0))
            If item IsNot Nothing Then
                elementoSelezionatoCorrente = ConvertiSalvatoInSearchResult(item)
                _gestoreXml.AggiungiRecente(item.Id, item.Titolo, item.Url, item.Tipo)
                MostraMenuCanale()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneApriCanalePreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneEliminaDaPreferiti(tipo As YoutubeItemType)
        Try
            Dim ids = elencoPreferiti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If

            For Each id In ids
                Try
                    _gestoreXml.RimuoviPreferito(id, tipo)
                    Dim itemToRemove = TrovaPreferitoPerId(id)
                    If itemToRemove IsNot Nothing Then risultatiPreferitiCorrenti.Remove(itemToRemove)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaPreferiti.Loop]: {ex.ToString()}")
                End Try
            Next

            elencoPreferiti.pulisci()
            For Each item In risultatiPreferitiCorrenti
                Try
                    elencoPreferiti.aggiungiElemento(item.Titolo, item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaPreferiti.RebuildLoop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneSpostaInCategoria(tipo As YoutubeItemType)
        Try
            Dim ids = elencoPreferiti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then Return

            _tempTipoContext = tipo
            Dim menuCat As New WGMenu()
            For Each cat In _gestoreXml.OttieniCategorie()
                Try
                    _tempCategoriaDestinazione = cat
                    menuCat.aggiungi(cat, AddressOf ClickEseguiSpostamentoCategoria)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AzioneSpostaInCategoria.Loop]: {ex.ToString()}")
                End Try
            Next
            menuCat.avvia("Seleziona Categoria Destinazione", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneSpostaInCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickEseguiSpostamentoCategoria()
        Dim ids = elencoPreferiti.ottieniElementi()
        EseguiSpostamentoCategoria(ids, _tempTipoContext, _tempCategoriaDestinazione)
    End Sub

    Private Sub EseguiSpostamentoCategoria(ids As List(Of String), tipo As YoutubeItemType, nuovaCat As String)
        Try
            For Each id In ids
                Try
                    _gestoreXml.ImpostaCategoriaElemento(id, tipo, nuovaCat)
                    Dim item = TrovaPreferitoPerId(id)
                    If item IsNot Nothing Then item.Categoria = nuovaCat
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.EseguiSpostamentoCategoria.Loop]: {ex.ToString()}")
                End Try
            Next
            Dim msg As New WGMessaggi()
            msg.Messaggio("Elementi spostati.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiSpostamentoCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraElencoCategorie(tipo As YoutubeItemType)
        Try
            elencoCategorie = New WGMenuLista()
            elencoCategorie.ciclono = True
            elencoCategorie.impostaTitolo("Gestione Categorie")
            elencoCategorie.aggiungiOpzione("Crea nuova categoria", AddressOf OpzioneNuovaCategoria)
            elencoCategorie.aggiungiOpzione("Elimina Categoria", AddressOf OpzioneEliminaCategoria)

            _tempTipoContext = tipo
            AddHandler elencoCategorie.ElementiSelezionati, AddressOf HandlerNavigaCategorie

            For Each cat In _gestoreXml.OttieniCategorie()
                Try
                    elencoCategorie.aggiungiElemento(cat, cat)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoCategorie.Loop]: {ex.ToString()}")
                End Try
            Next
            elencoCategorie.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoCategorie]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerNavigaCategorie()
        NavigaCategorieSelezionate(_tempTipoContext)
    End Sub

    Private Sub OpzioneNuovaCategoria()
        EseguiSuThreadUI(AddressOf UI_OpzioneNuovaCategoria)
    End Sub

    Private Sub UI_OpzioneNuovaCategoria()
        Try
            campoNuovaCategoria = New WGCampoTesto()
            campoNuovaCategoria.init("Nome Nuova Categoria")
            AddHandler campoNuovaCategoria.chiusura, AddressOf ConfermaNuovaCategoria
            campoNuovaCategoria.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneNuovaCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ConfermaNuovaCategoria()
        Try
            Dim nome = campoNuovaCategoria.OttieniTesto()
            campoNuovaCategoria.chiudi()
            If Not String.IsNullOrWhiteSpace(nome) Then
                _gestoreXml.AggiungiCategoria(nome)
                RicaricaElencoCategorie()
                Dim msg As New WGMessaggi()
                msg.Messaggio("Categoria creata.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ConfermaNuovaCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneEliminaCategoria()
        Try
            Dim cats = elencoCategorie.ottieniElementi()
            If cats Is Nothing OrElse cats.Count = 0 Then
                Return
            End If

            For Each cat In cats
                Try
                    _gestoreXml.EliminaCategoria(cat)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneEliminaCategoria.Loop]: {ex.ToString()}")
                End Try
            Next
            RicaricaElencoCategorie()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneEliminaCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RicaricaElencoCategorie()
        Try
            elencoCategorie.pulisci()
            For Each cat In _gestoreXml.OttieniCategorie()
                Try
                    elencoCategorie.aggiungiElemento(cat, cat)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.RicaricaElencoCategorie.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RicaricaElencoCategorie]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub NavigaCategorieSelezionate(tipo As YoutubeItemType)
        Try
            Dim cats = elencoCategorie.ottieniElementi()
            If cats Is Nothing OrElse cats.Count = 0 Then
                Return
            End If

            Dim combinedItems = _gestoreXml.OttieniPreferitiPerCategorie(tipo, cats, OrdinamentoElementi.DataDiscendente, Integer.MaxValue)
            risultatiPreferitiCorrenti = combinedItems
            _tempTipoContext = tipo
            _tempFiltroCategoria = Nothing

            If risultatiPreferitiCorrenti.Count > 0 Then
                elencoPreferiti = New WGMenuLista()
                elencoPreferiti.ciclono = True
                elencoPreferiti.multiselezioneConsentita = True
                elencoPreferiti.impostaTitolo("Elementi Categorie Selezionate")
                elencoPreferiti.aggiungiOpzione("Ordina", AddressOf ClickOrdinaPreferiti)
                elencoPreferiti.aggiungiOpzione("Seleziona tutti (tutte le pagine)", AddressOf ClickSelezionaTuttiPreferiti)

                If tipo = YoutubeItemType.Video Then
                    elencoPreferiti.aggiungiOpzione("Riproduci", AddressOf ClickRiproduciPreferiti)
                    If RiassuntoVideoDisponibile() Then elencoPreferiti.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoPreferiti)
                    AddHandler elencoPreferiti.ElementoSelezionato, AddressOf ClickRiproduciPreferiti
                End If

                For Each item In risultatiPreferitiCorrenti
                    Try
                        elencoPreferiti.aggiungiElemento(item.Titolo, item.Id)
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.NavigaCategorieSelezionate.Populate]: {ex.ToString()}")
                    End Try
                Next

                AddHandler elencoPreferiti.ElementoSelezionato, AddressOf HandlerElementoSelezionatoDaCategorie
                elencoPreferiti.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun elemento nelle categorie selezionate.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.NavigaCategorieSelezionate]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerElementoSelezionatoDaCategorie()
        Try
            Dim ids = elencoPreferiti.ottieniElementi()
            If ids.Count > 0 Then
                Dim itemSalvato = TrovaPreferitoPerId(ids(0))
                If itemSalvato IsNot Nothing Then
                    Dim item = ConvertiSalvatoInSearchResult(itemSalvato)
                    SelezionaElementoDaPreferiti(item)
                Else
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.NavigaCategorieSelezionate.Handler]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SelezionaElementoDaPreferiti(item As YoutubeSearchResult)
        Try
            elementoSelezionatoCorrente = item
            _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)

            If item.ItemType = YoutubeItemType.Video Then
                AvviaPlayer(item.Title, New List(Of String) From {item.Url})
            ElseIf item.ItemType = YoutubeItemType.Playlist Then
                MostraMenuContestualePlaylist()
            ElseIf item.ItemType = YoutubeItemType.Channel Then
                MostraMenuCanale()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SelezionaElementoDaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuContestualePlaylist()
        EseguiSuThreadUI(AddressOf UI_MostraMenuContestualePlaylist)
    End Sub

    Private Sub UI_MostraMenuContestualePlaylist()
        Try
            menuOpzioniPlaylist = New WGMenu()

            menuOpzioniPlaylist.aggiungi("Visualizza playlist", AddressOf LavoroRicercaPlaylist)
            menuOpzioniPlaylist.aggiungi("Cerca nella playlist", AddressOf OpzioneCercaNellaPlaylist)
            menuOpzioniPlaylist.aggiungi("Riproduci intera playlist", AddressOf ClickRiproduciInteraPlaylist)
            menuOpzioniPlaylist.aggiungi("Scarica l'intera playlist", Sub() PreparaPrelievo(elementoSelezionatoCorrente))
            menuOpzioniPlaylist.aggiungi("Informazioni", AddressOf ClickInfoPlaylist)

            If _gestoreXml.EPreferito(elementoSelezionatoCorrente.Id, YoutubeItemType.Playlist) Then
                menuOpzioniPlaylist.aggiungi("Rimuovi dai preferiti", AddressOf RimuoviPlaylistCorrenteDaPreferiti)
            Else
                menuOpzioniPlaylist.aggiungi("Aggiungi ai preferiti", AddressOf AggiungiPlaylistCorrenteAPreferiti)
            End If

            menuOpzioniPlaylist.avvia($"Menu Playlist: {elementoSelezionatoCorrente.Title}", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuContestualePlaylist]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickRiproduciInteraPlaylist()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Preparazione riproduzione playlist...")
            _tempIsShuffle = False
            AvviaThread(AddressOf ThreadCaricaPlaylist)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuContestualePlaylist.Riproduci]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickInfoPlaylist()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {elementoSelezionatoCorrente.Title}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuContestualePlaylist.Info]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiungiPlaylistCorrenteAPreferiti()
        Try
            If _gestoreXml.HaPiuCategorie() Then
                _elementoDaAggiungereTemp = elementoSelezionatoCorrente
                MostraSceltaCategoriaPerAggiunta()
            Else
                _gestoreXml.AggiungiPreferito(elementoSelezionatoCorrente.Id, elementoSelezionatoCorrente.Title, elementoSelezionatoCorrente.Url, YoutubeItemType.Playlist)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Playlist aggiunta ai preferiti.")
                msg.ShowDialog()
                menuOpzioniPlaylist.chiudi()
                MostraMenuContestualePlaylist()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiungiPlaylistCorrenteAPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RimuoviPlaylistCorrenteDaPreferiti()
        Try
            _gestoreXml.RimuoviPreferito(elementoSelezionatoCorrente.Id, YoutubeItemType.Playlist)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Playlist rimossa dai preferiti.")
            msg.ShowDialog()
            menuOpzioniPlaylist.chiudi()
            MostraMenuContestualePlaylist()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RimuoviPlaylistCorrenteDaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuCronologia()
        ' Con l'accesso a YouTube nel browser si chiede se usare la cronologia online (MenuYoutube.Online.vb).
        ApriCronologiaScegliendoFonte()
    End Sub

    Private Sub UI_MostraMenuCronologia()
        Try
            Dim menuCron = New WGMenu()
            menuCron.aggiungi("Video Recenti", AddressOf ClickCronologiaVideo)
            menuCron.aggiungi("Canali Recenti", AddressOf ClickCronologiaCanali)
            menuCron.aggiungi("Playlist Recenti", AddressOf ClickCronologiaPlaylist)
            menuCron.aggiungi("Ricerche Recenti", AddressOf MostraCronologiaRicerche)
            menuCron.avvia("Cronologia", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickCronologiaVideo()
        MostraElencoCronologia(YoutubeItemType.Video)
    End Sub

    Private Sub ClickCronologiaCanali()
        MostraElencoCronologia(YoutubeItemType.Channel)
    End Sub

    Private Sub ClickCronologiaPlaylist()
        MostraElencoCronologia(YoutubeItemType.Playlist)
    End Sub

    Private Sub MostraElencoCronologia(tipo As YoutubeItemType)
        Try
            _tempTipoContext = tipo
            _offsetCronologia = 0
            _ordineCronologia = OrdinamentoElementi.DataDiscendente
            CaricaPaginaCronologia(posizionaInizio:=True, primaApertura:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CaricaPaginaCronologia(Optional posizionaInizio As Boolean = True, Optional primaApertura As Boolean = False)
        Try
            Dim items = _gestoreXml.OttieniRecentiPagina(_tempTipoContext, _ordineCronologia, _offsetCronologia, DIMENSIONE_PAGINA_DB)
            Dim totale = _gestoreXml.ContaRecenti(_tempTipoContext)
            _haAltrePagineCronologia = (_offsetCronologia + items.Count) < totale

            If items.Count = 0 AndAlso _offsetCronologia = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Cronologia vuota.")
                msg.ShowDialog()
                Return
            End If

            If items.Count = 0 AndAlso _offsetCronologia > 0 Then
                _offsetCronologia = Math.Max(0, _offsetCronologia - DIMENSIONE_PAGINA_DB)
                items = _gestoreXml.OttieniRecentiPagina(_tempTipoContext, _ordineCronologia, _offsetCronologia, DIMENSIONE_PAGINA_DB)
                _haAltrePagineCronologia = False
                posizionaInizio = True
            End If

            risultatiPreferitiCorrenti = items

            If primaApertura OrElse elencoCronologia Is Nothing OrElse elencoCronologia.IsDisposed Then
                elencoCronologia = New WGMenuLista()
                elencoCronologia.ciclono = True
                elencoCronologia.impostaTitolo($"Cronologia {GetTipoStringa(_tempTipoContext)}")
                _contextListaOrdinamento = elencoCronologia

                elencoCronologia.aggiungiOpzione("Ordina", AddressOf ClickOrdinaCronologia)
                elencoCronologia.aggiungiOpzione("Elimina dalla cronologia", AddressOf ClickEliminaCronologia)
                elencoCronologia.aggiungiOpzione("Svuota cronologia", AddressOf ClickSvuotaCronologia)
                If _tempTipoContext = YoutubeItemType.Video Then
                    If RiassuntoVideoDisponibile() Then elencoCronologia.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoCronologia)
                End If

                AddHandler elencoCronologia.ElementoSelezionato, AddressOf HandlerElementoCronologia
                AddHandler elencoCronologia.iniziolista, AddressOf HandlerInizioListaCronologia
                AddHandler elencoCronologia.finelista, AddressOf HandlerFineListaCronologia

                For Each item In items
                    Try
                        elencoCronologia.aggiungiElemento(item.Titolo, item.Id)
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.CaricaPaginaCronologia.Loop]: {ex.ToString()}")
                    End Try
                Next
                AggiornaOpzioniPaginazioneCronologia()
                elencoCronologia.avvia()
            Else
                elencoCronologia.pulisci()
                For Each item In items
                    Try
                        elencoCronologia.aggiungiElemento(item.Titolo, item.Id)
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.CaricaPaginaCronologia.Reload.Loop]: {ex.ToString()}")
                    End Try
                Next
                AggiornaOpzioniPaginazioneCronologia()
                If posizionaInizio Then
                    If items.Count > 0 Then elencoCronologia.selezionaElemento(0)
                Else
                    elencoCronologia.SelezionaUltimoElemento()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CaricaPaginaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzioniPaginazioneCronologia()
        Try
            If elencoCronologia Is Nothing OrElse elencoCronologia.IsDisposed Then Return
            elencoCronologia.rimuoviOpzione("Pagina precedente")
            elencoCronologia.rimuoviOpzione("Pagina successiva")
            If _offsetCronologia > 0 Then
                elencoCronologia.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteCronologia)
            End If
            If _haAltrePagineCronologia Then
                elencoCronologia.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaCronologia)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzioniPaginazioneCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaCronologia()
        Try
            _offsetCronologia += DIMENSIONE_PAGINA_DB
            CaricaPaginaCronologia(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteCronologia()
        Try
            _offsetCronologia = Math.Max(0, _offsetCronologia - DIMENSIONE_PAGINA_DB)
            CaricaPaginaCronologia(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaCronologia(sender As Object, e As EventArgs)
        Try
            If _offsetCronologia <= 0 Then Return
            _offsetCronologia = Math.Max(0, _offsetCronologia - DIMENSIONE_PAGINA_DB)
            CaricaPaginaCronologia(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaCronologia(sender As Object, e As EventArgs)
        Try
            If Not _haAltrePagineCronologia Then Return
            _offsetCronologia += DIMENSIONE_PAGINA_DB
            CaricaPaginaCronologia(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaCronologia()
        Try
            Dim menuOrd As New WGMenu()
            menuOrd.aggiungi("Data (più recenti)", AddressOf SubOrdinaCronologiaDataDesc)
            menuOrd.aggiungi("Data (più vecchi)", AddressOf SubOrdinaCronologiaDataAsc)
            menuOrd.aggiungi("Titolo A-Z", AddressOf SubOrdinaCronologiaTitoloAsc)
            menuOrd.aggiungi("Titolo Z-A", AddressOf SubOrdinaCronologiaTitoloDesc)
            menuOrd.avvia("Ordina cronologia", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickOrdinaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SubOrdinaCronologiaDataDesc()
        _ordineCronologia = OrdinamentoElementi.DataDiscendente
        _offsetCronologia = 0
        CaricaPaginaCronologia(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaCronologiaDataAsc()
        _ordineCronologia = OrdinamentoElementi.DataAscendente
        _offsetCronologia = 0
        CaricaPaginaCronologia(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaCronologiaTitoloAsc()
        _ordineCronologia = OrdinamentoElementi.TitoloAscendente
        _offsetCronologia = 0
        CaricaPaginaCronologia(posizionaInizio:=True)
    End Sub

    Private Sub SubOrdinaCronologiaTitoloDesc()
        _ordineCronologia = OrdinamentoElementi.TitoloDiscendente
        _offsetCronologia = 0
        CaricaPaginaCronologia(posizionaInizio:=True)
    End Sub

    Private Sub ClickEliminaCronologia()
        AzioneEliminaDaCronologia(_tempTipoContext)
    End Sub

    Private Sub ClickSvuotaCronologia()
        Try
            _gestoreXml.SvuotaRecenti(_tempTipoContext)
            risultatiPreferitiCorrenti = New List(Of ElementoSalvato)

            If elencoCronologia IsNot Nothing Then
                elencoCronologia.abbandona()
                elencoCronologia = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickSvuotaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneEliminaDaCronologia(tipo As YoutubeItemType)
        Try
            Dim elementi As List(Of String) = elencoCronologia.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                Return
            End If

            For Each elemento In elementi
                Try
                    _gestoreXml.RimuoviRecente(elemento, tipo)
                    Dim itemToRemove = TrovaPreferitoPerId(elemento)
                    If itemToRemove IsNot Nothing Then risultatiPreferitiCorrenti.Remove(itemToRemove)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaCronologia.Loop]: {ex.ToString()}")
                End Try
            Next

            elencoCronologia.pulisci()
            For Each item In risultatiPreferitiCorrenti
                Try
                    elencoCronologia.aggiungiElemento(item.Titolo, item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaCronologia.RebuildLoop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneEliminaDaCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaCronologiaLegacy()
        OpzioneOrdinaGenerica(_contextListaOrdinamento, risultatiPreferitiCorrenti, AddressOf AggiornaListaPreferiti)
    End Sub

    Private Sub HandlerElementoCronologia()
        Try
            Dim ids = CType(_contextListaOrdinamento, WGMenuLista).ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then Return

            Dim idCercato = ids(0)
            Dim item As ElementoSalvato = Nothing

            For Each i In risultatiPreferitiCorrenti
                Try
                    If i.Id = idCercato Then
                        item = i
                        Exit For
                    End If
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementoCronologia.Loop]: {ex.ToString()}")
                End Try
            Next

            If item IsNot Nothing Then
                If _tempTipoContext = YoutubeItemType.Video Then
                    AvviaPlayer(item.Titolo, New List(Of String) From {item.Url})
                ElseIf _tempTipoContext = YoutubeItemType.Playlist Then
                    elementoSelezionatoCorrente = ConvertiSalvatoInSearchResult(item)
                    MostraMenuContestualePlaylist()
                ElseIf _tempTipoContext = YoutubeItemType.Channel Then
                    elementoSelezionatoCorrente = ConvertiSalvatoInSearchResult(item)
                    MostraMenuCanale()
                End If
            Else
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ElementoSelezionatoCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraCronologiaRicerche()
        Try
            _offsetRicerche = 0
            CaricaPaginaRicerche(posizionaInizio:=True, primaApertura:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraCronologiaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CaricaPaginaRicerche(Optional posizionaInizio As Boolean = True, Optional primaApertura As Boolean = False)
        Try
            Dim items = _gestoreXml.OttieniRicerchePagina(_offsetRicerche, DIMENSIONE_PAGINA_DB)
            Dim totale = _gestoreXml.ContaRicerche()
            _haAltrePagineRicerche = (_offsetRicerche + items.Count) < totale
            _risultatiRicercheCorrenti = items

            If items.Count = 0 AndAlso _offsetRicerche = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Cronologia ricerche vuota.")
                msg.ShowDialog()
                Return
            End If

            If items.Count = 0 AndAlso _offsetRicerche > 0 Then
                _offsetRicerche = Math.Max(0, _offsetRicerche - DIMENSIONE_PAGINA_DB)
                items = _gestoreXml.OttieniRicerchePagina(_offsetRicerche, DIMENSIONE_PAGINA_DB)
                _risultatiRicercheCorrenti = items
                _haAltrePagineRicerche = False
                posizionaInizio = True
            End If

            If primaApertura OrElse elencoCronologiaRicerche Is Nothing OrElse elencoCronologiaRicerche.IsDisposed Then
                elencoCronologiaRicerche = New WGMenuLista()
                elencoCronologiaRicerche.ciclono = True
                elencoCronologiaRicerche.impostaTitolo("Ricerche Recenti")
                _contextListaOrdinamento = elencoCronologiaRicerche

                elencoCronologiaRicerche.aggiungiOpzione("Riesegui ricerca", AddressOf HandlerElementoCronologiaRicerche)
                elencoCronologiaRicerche.aggiungiOpzione("Cambia tipo e riesegui", AddressOf OpzioneCambiaTipoRicercaRecente)
                elencoCronologiaRicerche.aggiungiOpzione("Copia testo", AddressOf OpzioneCopiaTestoRicercaRecente)
                elencoCronologiaRicerche.aggiungiOpzione("Elimina ricerca", AddressOf OpzioneEliminaRicercaRecente)
                elencoCronologiaRicerche.aggiungiOpzione("Svuota tutte le ricerche", AddressOf OpzioneSvuotaRicercheRecenti)

                AddHandler elencoCronologiaRicerche.ElementoSelezionato, AddressOf HandlerElementoCronologiaRicerche
                AddHandler elencoCronologiaRicerche.iniziolista, AddressOf HandlerInizioListaRicerche
                AddHandler elencoCronologiaRicerche.finelista, AddressOf HandlerFineListaRicerche

                PopolaElementiRicerche(items)
                AggiornaOpzioniPaginazioneRicerche()
                elencoCronologiaRicerche.avvia()
            Else
                elencoCronologiaRicerche.pulisci()
                PopolaElementiRicerche(items)
                AggiornaOpzioniPaginazioneRicerche()
                If posizionaInizio Then
                    If items.Count > 0 Then elencoCronologiaRicerche.selezionaElemento(0)
                Else
                    elencoCronologiaRicerche.SelezionaUltimoElemento()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CaricaPaginaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaElementiRicerche(items As List(Of ElementoCronologia))
        Try
            For Each item In items
                Try
                    elencoCronologiaRicerche.aggiungiElemento(item.Testo, item.Testo)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiRicerche.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Function GetTipoRicercaBreve(tipo As YoutubeItemType) As String
        Select Case tipo
            Case YoutubeItemType.Video : Return "Video"
            Case YoutubeItemType.Channel : Return "Canali"
            Case YoutubeItemType.Playlist : Return "Playlist"
            Case Else : Return "Misti"
        End Select
    End Function

    Private Function TrovaRicercaCorrentePerTesto(testo As String) As ElementoCronologia
        If _risultatiRicercheCorrenti Is Nothing OrElse String.IsNullOrEmpty(testo) Then Return Nothing
        For Each item In _risultatiRicercheCorrenti
            If item IsNot Nothing AndAlso String.Equals(item.Testo, testo, StringComparison.Ordinal) Then
                Return item
            End If
        Next
        Return Nothing
    End Function

    Private Sub AggiornaOpzioniPaginazioneRicerche()
        Try
            If elencoCronologiaRicerche Is Nothing OrElse elencoCronologiaRicerche.IsDisposed Then Return
            elencoCronologiaRicerche.rimuoviOpzione("Pagina precedente")
            elencoCronologiaRicerche.rimuoviOpzione("Pagina successiva")
            If _offsetRicerche > 0 Then
                elencoCronologiaRicerche.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteRicerche)
            End If
            If _haAltrePagineRicerche Then
                elencoCronologiaRicerche.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaRicerche)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzioniPaginazioneRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaRicerche()
        Try
            _offsetRicerche += DIMENSIONE_PAGINA_DB
            CaricaPaginaRicerche(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteRicerche()
        Try
            _offsetRicerche = Math.Max(0, _offsetRicerche - DIMENSIONE_PAGINA_DB)
            CaricaPaginaRicerche(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaRicerche(sender As Object, e As EventArgs)
        Try
            If _offsetRicerche <= 0 Then Return
            _offsetRicerche = Math.Max(0, _offsetRicerche - DIMENSIONE_PAGINA_DB)
            CaricaPaginaRicerche(posizionaInizio:=False)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaRicerche(sender As Object, e As EventArgs)
        Try
            If Not _haAltrePagineRicerche Then Return
            _offsetRicerche += DIMENSIONE_PAGINA_DB
            CaricaPaginaRicerche(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerElementoCronologiaRicerche()
        Try
            Dim elementi As List(Of String) = elencoCronologiaRicerche.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim testo As String = elementi(0)
            queryRicerca = testo

            Dim voce = TrovaRicercaCorrentePerTesto(testo)
            Dim tipo As YoutubeItemType = If(voce IsNot Nothing, voce.Tipo, YoutubeItemType.Unknown)

            _ricercaDaCronologia = True
            Dim msg As String
            Select Case tipo
                Case YoutubeItemType.Video : msg = "Ricerca Video in corso, attendere..."
                Case YoutubeItemType.Channel : msg = "Ricerca Canali in corso, attendere..."
                Case YoutubeItemType.Playlist : msg = "Ricerca Playlists in corso, attendere..."
                Case Else : msg = "Ricerca YouTube in corso, attendere..."
            End Select
            PrepThreadRicerca(tipo, msg)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementoCronologiaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCambiaTipoRicercaRecente()
        Try
            Dim elementi As List(Of String) = elencoCronologiaRicerche.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            queryRicerca = elementi(0)
            _ricercaDaCronologia = True
            MostraMenuOpzioniRicerca()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCambiaTipoRicercaRecente]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCopiaTestoRicercaRecente()
        Try
            Dim elementi As List(Of String) = elencoCronologiaRicerche.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Clipboard.SetText(elementi(0))
            Dim msg As New WGMessaggi()
            msg.Messaggio("Testo della ricerca copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCopiaTestoRicercaRecente]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneEliminaRicercaRecente()
        Try
            If elencoCronologiaRicerche Is Nothing OrElse elencoCronologiaRicerche.IsDisposed Then Return
            Dim elementi As List(Of String) = elencoCronologiaRicerche.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            For Each testo In elementi
                Try
                    If String.IsNullOrWhiteSpace(testo) Then Continue For
                    _gestoreXml.RimuoviRicercaRecente(testo)
                Catch exLoop As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneEliminaRicercaRecente.Loop]: {exLoop.ToString()}")
                End Try
            Next

            Dim rimanenti = _gestoreXml.ContaRicerche()
            If rimanenti = 0 Then
                If elencoCronologiaRicerche IsNot Nothing AndAlso Not elencoCronologiaRicerche.IsDisposed Then
                    elencoCronologiaRicerche.abbandona()
                    elencoCronologiaRicerche = Nothing
                End If
                _risultatiRicercheCorrenti = Nothing
                _offsetRicerche = 0
                Dim msg As New WGMessaggi()
                msg.Messaggio("Cronologia ricerche vuota.")
                msg.ShowDialog()
                Return
            End If

            If _offsetRicerche >= rimanenti Then
                _offsetRicerche = Math.Max(0, rimanenti - DIMENSIONE_PAGINA_DB)
            End If
            CaricaPaginaRicerche(posizionaInizio:=True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneEliminaRicercaRecente]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneSvuotaRicercheRecenti()
        Try
            _gestoreXml.SvuotaRicerche()
            If elencoCronologiaRicerche IsNot Nothing AndAlso Not elencoCronologiaRicerche.IsDisposed Then
                elencoCronologiaRicerche.abbandona()
                elencoCronologiaRicerche = Nothing
            End If
            _risultatiRicercheCorrenti = Nothing
            _offsetRicerche = 0
            Dim msg As New WGMessaggi()
            msg.Messaggio("Cronologia ricerche svuotata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneSvuotaRicercheRecenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiRicercaPrincipale()
        Try
            queryRicerca = ricercacampo.OttieniTesto()
            ricercacampo.chiudi()
            ricercacampo = Nothing
            If String.IsNullOrWhiteSpace(queryRicerca) Then
                Return
            End If

            _ricercaDaCronologia = False

            Dim tipoPref = LeggiTipoRicercaPredefinito()
            If String.Equals(tipoPref, "Chiedi", StringComparison.OrdinalIgnoreCase) OrElse String.IsNullOrWhiteSpace(tipoPref) Then
                MostraMenuOpzioniRicerca()
            Else
                Dim tipo = TipoRicercaDaConfigString(tipoPref)
                Dim msg As String
                Select Case tipo
                    Case YoutubeItemType.Video : msg = "Ricerca Video in corso, attendere..."
                    Case YoutubeItemType.Channel : msg = "Ricerca Canali in corso, attendere..."
                    Case YoutubeItemType.Playlist : msg = "Ricerca Playlists in corso, attendere..."
                    Case Else : msg = "Ricerca YouTube in corso, attendere..."
                End Select
                PrepThreadRicerca(tipo, msg)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiRicercaPrincipale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuOpzioniRicerca()
        EseguiSuThreadUI(AddressOf UI_MostraMenuOpzioniRicerca)
    End Sub

    Private Sub UI_MostraMenuOpzioniRicerca()
        Try
            menuRicerca = New WGMenu()
            menuRicerca.aggiungi("Contenuti Misti", AddressOf AvviaRicercaMista)
            menuRicerca.aggiungi("Video", AddressOf AvviaRicercaVideo)
            menuRicerca.aggiungi("Playlist", AddressOf AvviaRicercaPlaylist)
            menuRicerca.aggiungi("Canali", AddressOf AvviaRicercaCanale)
            menuRicerca.avvia($"Tipo di ricerca per: {queryRicerca}", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuOpzioniRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AvviaRicercaMista()
        PrepThreadRicerca(YoutubeItemType.Unknown, "Ricerca YouTube in corso, attendere...")
    End Sub

    Private Sub AvviaRicercaVideo()
        PrepThreadRicerca(YoutubeItemType.Video, "Ricerca Video in corso, attendere...")
    End Sub

    Private Sub AvviaRicercaPlaylist()
        PrepThreadRicerca(YoutubeItemType.Playlist, "Ricerca Playlists in corso, attendere...")
    End Sub

    Private Sub AvviaRicercaCanale()
        PrepThreadRicerca(YoutubeItemType.Channel, "Ricerca Canali in corso, attendere...")
    End Sub

    Private Sub PrepThreadRicerca(tipo As YoutubeItemType, msg As String)
        Try
            If Not String.IsNullOrWhiteSpace(queryRicerca) Then
                _gestoreXml.AggiungiRicercaRecente(queryRicerca, tipo)
            End If
            _tipoRicercaCorrente = tipo
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia(msg)
            _tempTipoContext = tipo
            AvviaThread(AddressOf ThreadLavoroRicercaGenerica)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PrepThreadRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroRicercaGenerica()
        LavoroRicercaGenerica(_tempTipoContext)
    End Sub

    Private Sub LavoroRicercaGenerica(tipoRicerca As YoutubeItemType)
        Dim risultati As List(Of YoutubeSearchResult) = Nothing
        Try
            _sortByRicerca = LeggiOrdinamentoPreferito(tipoRicerca)
            If tipoRicerca = YoutubeItemType.Video OrElse tipoRicerca = YoutubeItemType.Unknown Then
                Dim filtro = LeggiFiltroDurataPreferito()
                _minDurataRicerca = filtro.Min
                _maxDurataRicerca = filtro.Max
            Else
                _minDurataRicerca = Nothing
                _maxDurataRicerca = Nothing
            End If

            ' Data, funzioni e Short: quelli predefiniti (Impostazioni), cambiabili dalle opzioni dell'elenco.
            _filtriRicerca = If(tipoRicerca = YoutubeItemType.Video OrElse tipoRicerca = YoutubeItemType.Unknown, LeggiFiltriPredefiniti(), New FiltriRicerca())
            risultati = searcher.IniziaRicercaPaginata(SESSION_RICERCA, queryRicerca, DIMENSIONE_PAGINA_RICERCA, tipoRicerca, _sortByRicerca, _minDurataRicerca, _maxDurataRicerca, _filtriRicerca)
            risultatiRicercaCorrente = risultati
            _tipoRicercaCorrente = tipoRicerca
            EseguiSuThreadUI(AddressOf UI_PostRicercaGenerica)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaGenerica]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_MessaggioErroreRicerca)
        End Try
    End Sub

    Private Sub UI_MessaggioErroreRicerca()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Si è verificato un errore durante la ricerca.")
        msg.ShowDialog()
    End Sub

    Private Sub UI_PostRicercaGenerica()
        Try

            ChiudiProcessoInCorso()
            Dim titoloLista As String
            Select Case _tipoRicercaCorrente
                Case YoutubeItemType.Channel
                    titoloLista = $"Risultati Canali per: {queryRicerca}"
                Case YoutubeItemType.Playlist
                    titoloLista = $"Risultati Playlists per: {queryRicerca}"
                Case YoutubeItemType.Video
                    titoloLista = $"Risultati Video per: {queryRicerca}"
                Case Else
                    titoloLista = $"Risultati Misti per: {queryRicerca}"
            End Select

            If risultatiRicercaCorrente IsNot Nothing AndAlso risultatiRicercaCorrente.Count > 0 Then
                If elencorisultati Is Nothing OrElse elencorisultati.IsDisposed Then
                    elencorisultati = New WGMenuLista()
                End If
                PopolaListaRisultati(elencorisultati, risultatiRicercaCorrente, titoloLista)
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun risultato trovato")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaGenerica.UI]: {ex.ToString()}")

        End Try
    End Sub

    Private Sub PopolaListaRisultati(lista As WGMenuLista, risultati As List(Of YoutubeSearchResult), titolo As String)
        Try
            If risultati Is Nothing OrElse risultati.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun risultato trovato")
                messaggio.ShowDialog()
                Return
            End If

            lista.ciclono = True
            lista.multiselezioneConsentita = True
            lista.impostaTitolo(titolo)

            lista.aggiungiOpzione("Riproduci", AddressOf OpzioneRiproduciRisultato)
            lista.aggiungiOpzione("Scarica", AddressOf OpzioneScaricaRisultato)
            lista.aggiungiOpzione("Informazioni", AddressOf OpzioneInformazioniRisultato)
            lista.aggiungiOpzione("Ordinamento", AddressOf OpzioneOrdinamentoRicerca)
            If _tipoRicercaCorrente = YoutubeItemType.Video OrElse _tipoRicercaCorrente = YoutubeItemType.Unknown Then
                lista.aggiungiOpzione("Filtro durata", AddressOf OpzioneFiltroDurataRicerca)
                lista.aggiungiOpzione("Filtri", AddressOf OpzioneFiltriRicerca)
            End If
            If _tipoRicercaCorrente <> YoutubeItemType.Unknown Then
                lista.aggiungiOpzione("Cambia tipo di ricerca", AddressOf OpzioneCambiaTipoRicerca)
            End If
            lista.aggiungiOpzione("Copia URL", AddressOf OpzioneCopiaUrlRisultato)
            If RiassuntoVideoDisponibile() Then lista.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoRisultati)
            lista.aggiungiOpzione("Visualizza Commenti", AddressOf OpzioneVisualizzaCommentiRisultato)
            lista.aggiungiOpzione("Vai al Canale", AddressOf OpzioneVaiAlCanaleRisultato)
            lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoRisultati)
            lista.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaRisultati)
            AggiornaOpzionePaginaPrecedente()

            For Each item In risultati
                Try
                    Dim tipo As String = GetTipoStringa(item.ItemType)
                    lista.aggiungiElemento($"[{tipo}] {item.Title}", item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaListaRisultati.Loop]: {ex.ToString()}")
                End Try
            Next

            RemoveHandler lista.ElementoSelezionato, AddressOf HandlerDinamicoPreferitiRisultati
            AddHandler lista.ElementoSelezionato, AddressOf HandlerDinamicoPreferitiRisultati
            RemoveHandler lista.ElementiSelezionati, AddressOf HandlerElementiSelezionatiRisultati
            AddHandler lista.ElementiSelezionati, AddressOf HandlerElementiSelezionatiRisultati
            RemoveHandler lista.iniziolista, AddressOf HandlerInizioListaRisultati
            AddHandler lista.iniziolista, AddressOf HandlerInizioListaRisultati
            RemoveHandler lista.finelista, AddressOf HandlerFineListaRisultati
            AddHandler lista.finelista, AddressOf HandlerFineListaRisultati
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaListaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaRisultati(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            If searcher.PaginaAttuale(SESSION_RICERCA) <= 1 Then Return
            _posizioneDopoCambioPagina = "fine"
            OpzionePaginaPrecedenteRisultati()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaRisultati(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            _posizioneDopoCambioPagina = "inizio"
            OpzionePaginaSuccessivaRisultati()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaRisultati()
        OpzioneOrdinaGenerica(CType(elencorisultati, Object), risultatiRicercaCorrente, AddressOf AggiornaListaRisultati)
    End Sub

    ''' <summary>HandlerDinamicoPreferitiRisultati con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub HandlerDinamicoPreferitiRisultati(idEvento As String)
        HandlerDinamicoPreferitiRisultati()
    End Sub

    Private Sub HandlerDinamicoPreferitiRisultati()
        GestioneDinamicaPreferiti(elencorisultati, risultatiRicercaCorrente)
        OpzioneRiproduciRisultato()
    End Sub

    Private Sub HandlerElementiSelezionatiRisultati(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return

            Dim urls As New List(Of String)
            For Each id As String In ids
                Try
                    Dim item As YoutubeSearchResult = TrovaRisultatoPerId(id, risultatiRicercaCorrente)
                    If item IsNot Nothing AndAlso (item.ItemType = YoutubeItemType.Video) Then
                        urls.Add(item.Url)
                        _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)
                    End If
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementiSelezionatiRisultati.Loop]: {ex.ToString()}")
                End Try
            Next

            If urls.Count = 0 Then Return

            AvviaPlayer("Selezione da ricerca", urls)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementiSelezionatiRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub GestioneDinamicaPreferiti(lista As WGMenuLista, sourceList As List(Of YoutubeSearchResult))
        Try
            Dim ids = lista.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If
            Dim item = TrovaRisultatoPerId(ids(0), sourceList)
            If item Is Nothing Then
                Return
            End If

            If _gestoreXml.EPreferito(item.Id, item.ItemType) Then
                lista.rimuoviOpzione("Aggiungi ai preferiti")
                lista.aggiungiOpzione("Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferitoRisultati)
            Else
                lista.rimuoviOpzione("Rimuovi dai preferiti")
                lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoRisultati)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.GestioneDinamicaPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneAggiungiPreferitoRisultati()
        Try
            AzioneAggiungiPreferitoGenerica(elencorisultati, risultatiRicercaCorrente)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAggiungiPreferitoRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRimuoviPreferitoRisultati()
        Try
            AzioneRimuoviPreferitoGenerica(elencorisultati, risultatiRicercaCorrente)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRimuoviPreferitoRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneAggiungiPreferitoGenerica(lista As WGMenuLista, sourceList As List(Of YoutubeSearchResult))
        Try
            Dim ids = lista.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If
            Dim item = TrovaRisultatoPerId(ids(0), sourceList)
            If item Is Nothing Then
                Return
            End If

            If _gestoreXml.HaPiuCategorie() Then
                _elementoDaAggiungereTemp = item
                MostraSceltaCategoriaPerAggiunta()
            Else
                _gestoreXml.AggiungiPreferito(item.Id, item.Title, item.Url, item.ItemType)
                lista.rimuoviOpzione("Aggiungi ai preferiti")
                lista.aggiungiOpzione("Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferitoRisultati)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneAggiungiPreferitoGenerica]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraSceltaCategoriaPerAggiunta()
        Try
            elencoSceltaCategoria = New WGMenuLista()
            elencoSceltaCategoria.ciclono = True
            elencoSceltaCategoria.impostaTitolo("Seleziona Categoria")
            For Each cat In _gestoreXml.OttieniCategorie()
                Try
                    elencoSceltaCategoria.aggiungiElemento(cat, cat)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.MostraSceltaCategoriaPerAggiunta.Loop]: {ex.ToString()}")
                End Try
            Next
            AddHandler elencoSceltaCategoria.ElementoSelezionato, AddressOf ConfermaAggiuntaConCategoria
            elencoSceltaCategoria.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraSceltaCategoriaPerAggiunta]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ConfermaAggiuntaConCategoria()
        Try
            Dim cats = elencoSceltaCategoria.ottieniElementi()
            If cats Is Nothing OrElse cats.Count = 0 Then
                Return
            End If
            Dim categoriaSelezionata As String = cats(0)

            If _elementoDaAggiungereTemp IsNot Nothing Then
                _gestoreXml.AggiungiPreferito(_elementoDaAggiungereTemp.Id, _elementoDaAggiungereTemp.Title, _elementoDaAggiungereTemp.Url, _elementoDaAggiungereTemp.ItemType, categoriaSelezionata)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Elemento aggiunto ai preferiti nella categoria: " & categoriaSelezionata)
                msg.ShowDialog()
                _elementoDaAggiungereTemp = Nothing
                elencoSceltaCategoria.abbandona()
                elencoSceltaCategoria = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ConfermaAggiuntaConCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AzioneRimuoviPreferitoGenerica(lista As WGMenuLista, sourceList As List(Of YoutubeSearchResult))
        Try
            Dim ids = lista.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If
            Dim item = TrovaRisultatoPerId(ids(0), sourceList)
            If item Is Nothing Then
                Return
            End If

            _gestoreXml.RimuoviPreferito(item.Id, item.ItemType)
            lista.rimuoviOpzione("Rimuovi dai preferiti")
            lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoRisultati)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneRimuoviPreferitoGenerica]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaListaRisultati(lista As Object, dati As Object)
        Try
            Dim l = CType(lista, WGMenuLista)
            Dim d = CType(dati, List(Of YoutubeSearchResult))
            l.pulisci()
            For Each item In d
                Try
                    Dim tipo As String = GetTipoStringa(item.ItemType)
                    l.aggiungiElemento($"[{tipo}] {item.Title}", item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaListaRisultati.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaListaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaRisultati()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina successiva...")
            AvviaThread(AddressOf ThreadLavoroPaginaSuccessivaRisultati)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaSuccessivaRisultati()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaSuccessiva(SESSION_RICERCA)
            If risultati Is Nothing OrElse risultati.Count = 0 Then
                searcher.PaginaPrecedente(SESSION_RICERCA)
                EseguiSuThreadUI(AddressOf UI_NessunAltroRisultatoRisultati)
                Return
            End If
            risultatiRicercaCorrente = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaRisultati)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaSuccessivaRisultati]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteRisultati()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina precedente...")
            AvviaThread(AddressOf ThreadLavoroPaginaPrecedenteRisultati)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaPrecedenteRisultati()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaPrecedente(SESSION_RICERCA)
            risultatiRicercaCorrente = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaRisultati)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaPrecedenteRisultati]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_NessunAltroRisultatoRisultati()
        Try
            ChiudiProcessoInCorso()
            If Not String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                _posizioneDopoCambioPagina = ""
                If elencorisultati IsNot Nothing AndAlso Not elencorisultati.IsDisposed AndAlso risultatiRicercaCorrente IsNot Nothing AndAlso risultatiRicercaCorrente.Count > 0 Then
                    elencorisultati.selezionaElemento(0)
                End If
                Return
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun altro risultato disponibile.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_NessunAltroRisultatoRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_PostCambioPaginaRisultati()
        Try
            ChiudiProcessoInCorso()
            If elencorisultati Is Nothing OrElse elencorisultati.IsDisposed Then Return

            If risultatiRicercaCorrente Is Nothing OrElse risultatiRicercaCorrente.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun risultato trovato per questa pagina.")
                messaggio.ShowDialog()
                _posizioneDopoCambioPagina = ""
                Return
            End If

            AggiornaListaRisultati(elencorisultati, risultatiRicercaCorrente)
            AggiornaOpzionePaginaPrecedente()

            If _posizioneDopoCambioPagina = "fine" Then
                elencorisultati.SelezionaUltimoElemento()
            ElseIf _posizioneDopoCambioPagina = "inizio" OrElse String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                If risultatiRicercaCorrente.Count > 0 Then elencorisultati.selezionaElemento(0)
            End If
            _posizioneDopoCambioPagina = ""
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostCambioPaginaRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzionePaginaPrecedente()
        Try
            If elencorisultati Is Nothing OrElse elencorisultati.IsDisposed Then Return
            If searcher Is Nothing Then Return

            elencorisultati.rimuoviOpzione("Pagina precedente")
            If searcher.PaginaAttuale(SESSION_RICERCA) > 1 Then
                elencorisultati.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteRisultati)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzionePaginaPrecedente]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneOrdinaGenerica(lista As Object, dati As Object, callbackAggiornamento As Action(Of Object, Object))
        Try
            _contextListaOrdinamento = lista
            _contextDatiOrdinamento = dati
            _contextCallbackOrdinamento = callbackAggiornamento

            menuOrdinamento = New WGMenu()
            menuOrdinamento.aggiungi("Titolo (A-Z)", AddressOf ClickOrdinaAZ)
            menuOrdinamento.aggiungi("Titolo (Z-A)", AddressOf ClickOrdinaZA)
            menuOrdinamento.aggiungi("Tipo Contenuto", AddressOf ClickOrdinaTipo)
            menuOrdinamento.aggiungi("Data Aggiunta (Crescente)", AddressOf ClickOrdinaDataCresc)
            menuOrdinamento.aggiungi("Data Aggiunta (Decrescente)", AddressOf ClickOrdinaDataDecr)
            menuOrdinamento.avvia("Scegli Criterio Ordinamento", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneOrdinaGenerica]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaAZ()
        EseguiOrdinamentoGlobale("TITLE_ASC")
    End Sub

    Private Sub ClickOrdinaZA()
        EseguiOrdinamentoGlobale("TITLE_DESC")
    End Sub

    Private Sub ClickOrdinaTipo()
        EseguiOrdinamentoGlobale("TYPE")
    End Sub

    Private Sub ClickOrdinaDataCresc()
        EseguiOrdinamentoGlobale("DATE_ASC")
    End Sub

    Private Sub ClickOrdinaDataDecr()
        EseguiOrdinamentoGlobale("DATE_DESC")
    End Sub

    Private Sub EseguiOrdinamentoGlobale(criterio As String)
        processoincorso = New WGProcessoInCorso()
        processoincorso.Avvia("Ordinamento in corso...")
        _contextCriterioOrdinamento = criterio
        AvviaThread(AddressOf ThreadOrdinamento)
    End Sub

    Private Sub ThreadOrdinamento()
        Try
            Dim resultList As Object = Nothing
            Dim criterio = _contextCriterioOrdinamento
            Dim dati = _contextDatiOrdinamento

            If TypeOf dati Is List(Of YoutubeSearchResult) Then
                Dim list = CType(dati, List(Of YoutubeSearchResult))
                Select Case criterio
                    Case "TITLE_ASC"
                        Dim q = From x In list Order By x.Title Select x
                        list = q.ToList()
                    Case "TITLE_DESC"
                        Dim q = From x In list Order By x.Title Descending Select x
                        list = q.ToList()
                    Case "TYPE"
                        Dim q = From x In list Order By x.ItemType, x.Title Select x
                        list = q.ToList()
                End Select
                resultList = list
            ElseIf TypeOf dati Is List(Of ElementoSalvato) Then
                Dim list = CType(dati, List(Of ElementoSalvato))
                Select Case criterio
                    Case "TITLE_ASC"
                        Dim q = From x In list Order By x.Titolo Select x
                        list = q.ToList()
                    Case "TITLE_DESC"
                        Dim q = From x In list Order By x.Titolo Descending Select x
                        list = q.ToList()
                    Case "TYPE"
                        Dim q = From x In list Order By x.Tipo, x.Titolo Select x
                        list = q.ToList()
                    Case "DATE_ASC"
                        Dim q = From x In list Order By x.DataAggiunta Select x
                        list = q.ToList()
                    Case "DATE_DESC"
                        Dim q = From x In list Order By x.DataAggiunta Descending Select x
                        list = q.ToList()
                End Select
                resultList = list
            End If

            _contextDatiOrdinamento = resultList
            EseguiSuThreadUI(AddressOf UI_PostOrdinamento)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiOrdinamentoGlobale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostOrdinamento()
        _contextCallbackOrdinamento.Invoke(_contextListaOrdinamento, _contextDatiOrdinamento)
        ChiudiProcessoInCorso()
    End Sub

    Private Sub UI_ChiudiProcesso()
        ChiudiProcessoInCorso()
    End Sub

    Private Function ConvertiSalvatoInSearchResult(item As ElementoSalvato) As YoutubeSearchResult
        Try
            Return New YoutubeSearchResult With {
                .Id = item.Id,
                .Title = item.Titolo,
                .Url = item.Url,
                .ItemType = item.Tipo
            }
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ConvertiSalvatoInSearchResult]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Function TrovaRisultatoPerId(idToFind As String, sourceList As List(Of YoutubeSearchResult)) As YoutubeSearchResult
        Try
            For Each r In sourceList
                Try
                    If r.Id = idToFind Then Return r
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.TrovaRisultatoPerId.Loop]: {ex.ToString()}")
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TrovaRisultatoPerId]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneRiproduciRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente)
            If item Is Nothing Then
                Return
            End If

            _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)

            If item.ItemType = YoutubeItemType.Video Then
                AvviaPlayer(item.Title, New List(Of String) From {item.Url})
            ElseIf item.ItemType = YoutubeItemType.Playlist Then
                elementoSelezionatoCorrente = item
                MostraMenuContestualePlaylist()
            ElseIf item.ItemType = YoutubeItemType.Channel Then
                elementoSelezionatoCorrente = item
                MostraMenuCanale()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiproduciRisultato]: {ex.ToString()}")
        End Try
    End Sub

    ' ---- prelievo da YouTube (video, playlist intere, canali interi) ---------------------------------------------
    ' Si sceglie se prelevare il video o solo l'audio, poi la cartella (con il selettore di WinGabriel, che parte dalle unità del
    ' computer); il lavoro lo fa il centro di controllo, dove si vedono avanzamento e si può annullare.

    Private _selettoreCartellaPrelievo As SelettoreCartellaMusica

    Private Sub OpzioneScaricaRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then Return
            PreparaPrelievo(TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente))
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneScaricaRisultato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PreparaPrelievo(item As YoutubeSearchResult)
        Try
            If item Is Nothing Then Return
            Dim indirizzo As String = item.Url
            If String.IsNullOrWhiteSpace(indirizzo) Then
                If item.ItemType = YoutubeItemType.Playlist Then
                    indirizzo = "https://www.youtube.com/playlist?list=" & item.Id
                ElseIf item.ItemType = YoutubeItemType.Channel Then
                    indirizzo = "https://www.youtube.com/channel/" & item.Id
                Else
                    indirizzo = "https://www.youtube.com/watch?v=" & item.Id
                End If
            End If
            Dim intero As Boolean = item.ItemType = YoutubeItemType.Playlist OrElse item.ItemType = YoutubeItemType.Channel
            If item.ItemType = YoutubeItemType.Channel AndAlso Not indirizzo.Contains("/videos") Then indirizzo = indirizzo.TrimEnd("/"c) & "/videos"
            Dim titolo As String = item.Title
            Dim menu As New WGMenu()
            menu.aggiungi("Video, in mp4", Sub()
                                               menu.chiudi()
                                               ScegliCartellaPrelievo(indirizzo, titolo, False, intero)
                                           End Sub)
            menu.aggiungi("Solo audio, in mp3", Sub()
                                                    menu.chiudi()
                                                    ScegliCartellaPrelievo(indirizzo, titolo, True, intero)
                                                End Sub)
            menu.avvia("Scarica " & If(item.ItemType = YoutubeItemType.Channel, "tutti i video del canale ", If(item.ItemType = YoutubeItemType.Playlist, "la playlist ", "il video ")) & titolo, True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PreparaPrelievo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ScegliCartellaPrelievo(indirizzo As String, titolo As String, soloAudio As Boolean, intero As Boolean)
        Try
            _selettoreCartellaPrelievo = New SelettoreCartellaMusica()
            _selettoreCartellaPrelievo.Scegli(Sub(cartella As String)
                                                  EseguiSuThreadUI(Sub()
                                                                       Try
                                                                           WGCentroDiControllo.GetInstance().AggiungiDownloadYoutube(indirizzo, titolo, cartella, soloAudio, intero)
                                                                           Dim msg As New WGMessaggi()
                                                                           msg.Messaggio("Prelievo avviato: " & titolo & ". Lo trovi nel centro di controllo, dove puoi vedere l'avanzamento e annullarlo. I file andranno in " & cartella & ".")
                                                                           msg.ShowDialog()
                                                                       Catch ex2 As Exception
                                                                           logger.ScriviLog($"ERRORE [MenuYoutube.ScegliCartellaPrelievo (avvio)]: {ex2.ToString()}")
                                                                       End Try
                                                                   End Sub)
                                              End Sub)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ScegliCartellaPrelievo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneInformazioniRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente)
            If item Is Nothing Then
                Return
            End If

            elementoSelezionatoCorrente = item
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {item.Title}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneInformazioniRisultato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCopiaUrlRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente)
            If item Is Nothing Then
                Return
            End If
            Clipboard.SetText(item.Url)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCopiaUrlRisultato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVisualizzaCommentiRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente)
            If item Is Nothing Then
                Return
            End If

            If item.ItemType = YoutubeItemType.Video Then
                elementoSelezionatoCorrente = item
                processoincorso = New WGProcessoInCorso()
                processoincorso.Avvia("Recupero commenti in corso...")
                AvviaThread(AddressOf LavoroVisualizzaCommenti)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneVisualizzaCommentiRisultato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVaiAlCanaleRisultato()
        Try
            Dim idSelezionato As List(Of String) = elencorisultati.ottieniElementi()
            If idSelezionato Is Nothing OrElse idSelezionato.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionato(0), risultatiRicercaCorrente)
            If item Is Nothing Then
                Return
            End If

            If Not String.IsNullOrEmpty(item.ChannelId) Then
                Dim channelItem As New YoutubeSearchResult()
                channelItem.Id = item.ChannelId
                channelItem.Title = item.ChannelName
                channelItem.ItemType = YoutubeItemType.Channel
                channelItem.Url = $"https://www.youtube.com/channel/{item.ChannelId}"

                elementoSelezionatoCorrente = channelItem
                _gestoreXml.AggiungiRecente(channelItem.Id, channelItem.Title, channelItem.Url, channelItem.ItemType)
                MostraMenuCanale()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneVaiAlCanaleRisultato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuRiproduzionePlaylist(item As YoutubeSearchResult)
        EseguiSuThreadUI(AddressOf UI_MostraMenuRiproduzionePlaylist)
    End Sub

    Private Sub UI_MostraMenuRiproduzionePlaylist()
        Try
            menuRiproduzionePlaylist = New WGMenu()
            menuRiproduzionePlaylist.aggiungi("Riproduci in ordine", AddressOf RiproduciPlaylistInOrdine)
            menuRiproduzionePlaylist.aggiungi("Riproduci in modo casuale", AddressOf RiproduciPlaylistCasuale)
            menuRiproduzionePlaylist.avvia($"Riproduzione Playlist: {elementoSelezionatoCorrente.Title}", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuRiproduzionePlaylist]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RiproduciPlaylistInOrdine()
        Try
            menuRiproduzionePlaylist = Nothing
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento video dalla playlist (in ordine)...")
            _tempIsShuffle = False
            AvviaThread(AddressOf ThreadCaricaPlaylist)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RiproduciPlaylistInOrdine]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RiproduciPlaylistCasuale()
        Try
            menuRiproduzionePlaylist = Nothing
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento video dalla playlist (casuale)...")
            _tempIsShuffle = True
            AvviaThread(AddressOf ThreadCaricaPlaylist)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RiproduciPlaylistCasuale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuCanale()
        EseguiSuThreadUI(AddressOf UI_MostraMenuCanale)
    End Sub

    Private Sub UI_MostraMenuCanale()
        Try
            menuCanale = New WGMenu()
            menuCanale.aggiungi("Video", AddressOf OpzioneVideoCanale)
            menuCanale.aggiungi("Cerca nel canale", AddressOf OpzioneCercaNelCanale)
            menuCanale.aggiungi("Playlists", AddressOf OpzionePlaylistCanale)
            menuCanale.aggiungi("Scarica tutti i video del canale", Sub() PreparaPrelievo(elementoSelezionatoCorrente))
            menuCanale.aggiungi("Informazioni sul canale", AddressOf OpzioneInfoCanale)

            If _gestoreXml.EIscritto(elementoSelezionatoCorrente.Id) Then
                menuCanale.aggiungi("Annulla iscrizione al canale", AddressOf OpzioneAnnullaIscrizioneCanale)
                menuCanale.aggiungi("Nuovi video di questo canale", AddressOf OpzioneNuoviVideoDelCanale)
            Else
                menuCanale.aggiungi("Iscriviti al canale", AddressOf OpzioneIscrivitiAlCanale)
            End If

            If _gestoreXml.EPreferito(elementoSelezionatoCorrente.Id, YoutubeItemType.Channel) Then
                menuCanale.aggiungi("Rimuovi dai preferiti", AddressOf OpzioneRimuoviCanalePreferito)
            Else
                menuCanale.aggiungi("Aggiungi ai preferiti", AddressOf OpzioneAggiungiCanalePreferito)
            End If

            menuCanale.avvia($"Canale: {elementoSelezionatoCorrente.Title}", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneAggiungiCanalePreferito()
        Try
            _gestoreXml.AggiungiPreferito(elementoSelezionatoCorrente.Id, elementoSelezionatoCorrente.Title, elementoSelezionatoCorrente.Url, YoutubeItemType.Channel)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Canale aggiunto ai preferiti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAggiungiCanalePreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRimuoviCanalePreferito()
        Try
            _gestoreXml.RimuoviPreferito(elementoSelezionatoCorrente.Id, YoutubeItemType.Channel)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Canale rimosso dai preferiti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRimuoviCanalePreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVideoCanale()
        Try
            menuCanale = Nothing
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero video dal canale {elementoSelezionatoCorrente.Title}...")
            AvviaThread(AddressOf LavoroRicercaCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneVideoCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePlaylistCanale()
        Try
            menuCanale = Nothing
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero playlist dal canale {elementoSelezionatoCorrente.Title}...")
            AvviaThread(AddressOf LavoroRicercaPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneInfoCanale()
        Try
            menuCanale = Nothing
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {elementoSelezionatoCorrente.Title}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneInfoCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub LavoroRicercaPlaylistCanale()
        Dim playlists As List(Of YoutubeSearchResult) = Nothing
        Try
            _sortByPlaylistCanale = LeggiOrdinamentoPreferito(YoutubeItemType.Playlist)
            playlists = searcher.IniziaPlaylistCanalePaginato(SESSION_PLAYLIST_CANALE, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByPlaylistCanale)
            risultatiPlaylistCanale = playlists
            EseguiSuThreadUI(AddressOf UI_PostRicercaPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaPlaylistCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_MessaggioNessunRisultato)
        End Try
    End Sub

    Private Sub UI_PostRicercaPlaylistCanale()
        ChiudiProcessoInCorso()
        If risultatiPlaylistCanale IsNot Nothing AndAlso risultatiPlaylistCanale.Count > 0 Then
            elencoPlaylistCanale = New WGMenuLista()
            elencoPlaylistCanale.ciclono = True
            elencoPlaylistCanale.impostaTitolo($"Playlists di: {elementoSelezionatoCorrente.Title}")

            elencoPlaylistCanale.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoPlaylistCanale)
            elencoPlaylistCanale.aggiungiOpzione("Ordinamento", AddressOf OpzioneOrdinamentoPlaylistCanale)
            elencoPlaylistCanale.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaPlaylistCanale)
            AggiornaOpzionePaginaPrecedentePlaylistCanale()
            AddHandler elencoPlaylistCanale.ElementoSelezionato, AddressOf HandlerDinamicoPreferitiPlaylistCanale
            AddHandler elencoPlaylistCanale.iniziolista, AddressOf HandlerInizioListaPlaylistCanale
            AddHandler elencoPlaylistCanale.finelista, AddressOf HandlerFineListaPlaylistCanale

            For Each item In risultatiPlaylistCanale
                Try
                    elencoPlaylistCanale.aggiungiElemento($"[Playlist] {item.Title}", item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostRicercaPlaylistCanale.Loop]: {ex.ToString()}")
                End Try
            Next
            elencoPlaylistCanale.avvia()
        Else
            UI_MessaggioNessunRisultato()
        End If
    End Sub

    Private Sub HandlerInizioListaPlaylistCanale(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            If searcher.PaginaAttuale(SESSION_PLAYLIST_CANALE) <= 1 Then Return
            _posizioneDopoCambioPagina = "fine"
            OpzionePaginaPrecedentePlaylistCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaPlaylistCanale(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            _posizioneDopoCambioPagina = "inizio"
            OpzionePaginaSuccessivaPlaylistCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MessaggioNessunRisultato()
        Dim messaggio As New WGMessaggi()
        messaggio.Messaggio("Nessun risultato trovato")
        messaggio.ShowDialog()
    End Sub

    Private Sub HandlerDinamicoPreferitiPlaylistCanale()
        GestioneDinamicaPreferiti(elencoPlaylistCanale, risultatiPlaylistCanale)
    End Sub

    Private Sub OpzioneAggiungiPreferitoPlaylistCanale()
        Try
            AzioneAggiungiPreferitoGenerica(elencoPlaylistCanale, risultatiPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAggiungiPreferitoPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaPlaylistCanale()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina successiva...")
            AvviaThread(AddressOf ThreadLavoroPaginaSuccessivaPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaSuccessivaPlaylistCanale()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaSuccessiva(SESSION_PLAYLIST_CANALE)
            If risultati Is Nothing OrElse risultati.Count = 0 Then
                searcher.PaginaPrecedente(SESSION_PLAYLIST_CANALE)
                EseguiSuThreadUI(AddressOf UI_NessunAltroRisultatoPlaylistCanale)
                Return
            End If
            risultatiPlaylistCanale = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaSuccessivaPlaylistCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedentePlaylistCanale()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina precedente...")
            AvviaThread(AddressOf ThreadLavoroPaginaPrecedentePlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedentePlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaPrecedentePlaylistCanale()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaPrecedente(SESSION_PLAYLIST_CANALE)
            risultatiPlaylistCanale = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaPlaylistCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaPrecedentePlaylistCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_NessunAltroRisultatoPlaylistCanale()
        Try
            ChiudiProcessoInCorso()
            If Not String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                _posizioneDopoCambioPagina = ""
                If elencoPlaylistCanale IsNot Nothing AndAlso Not elencoPlaylistCanale.IsDisposed AndAlso risultatiPlaylistCanale IsNot Nothing AndAlso risultatiPlaylistCanale.Count > 0 Then
                    elencoPlaylistCanale.selezionaElemento(0)
                End If
                Return
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun altro risultato disponibile.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_NessunAltroRisultatoPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_PostCambioPaginaPlaylistCanale()
        Try
            ChiudiProcessoInCorso()
            If elencoPlaylistCanale Is Nothing OrElse elencoPlaylistCanale.IsDisposed Then Return

            If risultatiPlaylistCanale Is Nothing OrElse risultatiPlaylistCanale.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun risultato trovato per questa pagina.")
                messaggio.ShowDialog()
                _posizioneDopoCambioPagina = ""
                Return
            End If

            AggiornaListaRisultati(elencoPlaylistCanale, risultatiPlaylistCanale)
            AggiornaOpzionePaginaPrecedentePlaylistCanale()

            If _posizioneDopoCambioPagina = "fine" Then
                elencoPlaylistCanale.SelezionaUltimoElemento()
            ElseIf _posizioneDopoCambioPagina = "inizio" OrElse String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                If risultatiPlaylistCanale.Count > 0 Then elencoPlaylistCanale.selezionaElemento(0)
            End If
            _posizioneDopoCambioPagina = ""
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostCambioPaginaPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzionePaginaPrecedentePlaylistCanale()
        Try
            If elencoPlaylistCanale Is Nothing OrElse elencoPlaylistCanale.IsDisposed Then Return
            If searcher Is Nothing Then Return

            elencoPlaylistCanale.rimuoviOpzione("Pagina precedente")
            If searcher.PaginaAttuale(SESSION_PLAYLIST_CANALE) > 1 Then
                elencoPlaylistCanale.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedentePlaylistCanale)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzionePaginaPrecedentePlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ElementoSelezionatoPlaylistCanale() Handles elencoPlaylistCanale.ElementoSelezionato
        Try
            Dim idSelezionati As List(Of String) = elencoPlaylistCanale.ottieniElementi()
            If idSelezionati Is Nothing OrElse idSelezionati.Count = 0 Then
                Return
            End If
            Dim item As YoutubeSearchResult = TrovaRisultatoPerId(idSelezionati(0), risultatiPlaylistCanale)
            If item Is Nothing Then
                Return
            End If

            elementoSelezionatoCorrente = item
            _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)

            MostraMenuContestualePlaylist()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ElementoSelezionatoPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub LavoroRecuperaInformazioni()
        Dim itemDetails As YoutubeItemDetails = Nothing
        Try
            itemDetails = searcher.GetItemDetails(elementoSelezionatoCorrente.Id, elementoSelezionatoCorrente.ItemType)
            If itemDetails IsNot Nothing Then
                _dettagliElementoCorrente = itemDetails
                EseguiSuThreadUI(AddressOf UI_VisualizzaListaInformazioni)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRecuperaInformazioni]: {ex.ToString()}")
        Finally
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_VisualizzaListaInformazioni()
        VisualizzaListaInformazioni(_dettagliElementoCorrente)
    End Sub

    Private Sub VisualizzaListaInformazioni(details As YoutubeItemDetails)
        Try
            If details Is Nothing Then Return

            listaInformazioni = New WGMenuLista()
            listaInformazioni.ciclono = True
            listaInformazioni.impostaTitolo("Dettagli " & GetTipoStringa(details.ItemType))

            Select Case details.ItemType
                Case YoutubeItemType.Video
                    PopolaInformazioniVideo(listaInformazioni, details)
                Case YoutubeItemType.Playlist
                    PopolaInformazioniPlaylist(listaInformazioni, details)
                Case YoutubeItemType.Channel
                    PopolaInformazioniCanale(listaInformazioni, details)
                Case Else
                    listaInformazioni.aggiungiElemento("Titolo: " & details.Title, "TITOLO")
                    listaInformazioni.aggiungiElemento("URL: " & details.Url, "URL")
            End Select

            AggiungiVoceDescrizione(listaInformazioni, details)

            listaInformazioni.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.VisualizzaListaInformazioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaInformazioniVideo(lista As WGMenuLista, details As YoutubeItemDetails)
        Try
            lista.aggiungiElemento("Titolo: " & details.Title, "TITOLO")
            lista.aggiungiElemento("Canale: " & SeVuoto(details.ChannelName), "CANALE")
            If Not String.IsNullOrWhiteSpace(details.ChannelUrl) Then
                lista.aggiungiElemento("URL canale: " & details.ChannelUrl, "URLCANALE")
            End If
            lista.aggiungiElemento("Data pubblicazione: " & FormattaData(details.UploadDate), "DATA")
            lista.aggiungiElemento("Durata: " & SeVuoto(details.DurationText), "DURATA")
            lista.aggiungiElemento("Visualizzazioni: " & FormattaNumero(details.ViewCount), "VIEW")
            lista.aggiungiElemento("Mi piace: " & FormattaNumero(details.LikeCount), "LIKE")
            lista.aggiungiElemento("Numero commenti: " & FormattaNumero(details.CommentCount), "COMMENTI")
            If Not String.IsNullOrWhiteSpace(details.Categories) Then
                lista.aggiungiElemento("Categoria: " & details.Categories, "CATEGORIA")
            End If
            If Not String.IsNullOrWhiteSpace(details.Tags) Then
                lista.aggiungiElemento("Tag: " & details.Tags, "TAG")
            End If
            If details.IsLive.HasValue Then
                lista.aggiungiElemento("In diretta: " & FormattaBooleano(details.IsLive, "Sì", "No"), "LIVE")
            End If
            If details.WasLive.HasValue AndAlso details.WasLive.Value Then
                lista.aggiungiElemento("Era una diretta: Sì", "WASLIVE")
            End If
            If details.AgeLimit.HasValue AndAlso details.AgeLimit.Value > 0 Then
                lista.aggiungiElemento("Limite di eta: " & details.AgeLimit.Value.ToString() & " anni", "ETA")
            End If
            If Not String.IsNullOrWhiteSpace(details.License) Then
                lista.aggiungiElemento("Licenza: " & details.License, "LICENZA")
            End If
            lista.aggiungiElemento("URL: " & details.Url, "URL")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaInformazioniVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaInformazioniPlaylist(lista As WGMenuLista, details As YoutubeItemDetails)
        Try
            lista.aggiungiElemento("Titolo: " & details.Title, "TITOLO")
            lista.aggiungiElemento("Canale: " & SeVuoto(details.ChannelName), "CANALE")
            If Not String.IsNullOrWhiteSpace(details.ChannelUrl) Then
                lista.aggiungiElemento("URL canale: " & details.ChannelUrl, "URLCANALE")
            End If
            lista.aggiungiElemento("Numero di video: " & FormattaNumeroInt(details.VideoCount), "VIDEOCOUNT")
            lista.aggiungiElemento("Durata stimata: " & SeVuoto(details.EstimatedDurationText), "DURATA")
            If Not String.IsNullOrWhiteSpace(details.Tags) Then
                lista.aggiungiElemento("Tag: " & details.Tags, "TAG")
            End If
            lista.aggiungiElemento("URL: " & details.Url, "URL")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaInformazioniPlaylist]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaInformazioniCanale(lista As WGMenuLista, details As YoutubeItemDetails)
        Try
            lista.aggiungiElemento("Nome canale: " & details.Title)
            lista.aggiungiElemento("Iscritti: " & FormattaNumero(details.SubscriberCount), "ISCRITTI")
            lista.aggiungiElemento("Numero di video: " & FormattaNumero(details.TotalVideoCount), "CONTENUTI")
            lista.aggiungiElemento("Visualizzazioni totali: " & FormattaNumero(details.TotalViewCount), "VISUALIZZAZIONI")
            If Not String.IsNullOrWhiteSpace(details.Tags) Then
                lista.aggiungiElemento("Tag: " & details.Tags, "TAG")
            End If
            If details.ChannelVerificato.HasValue Then
                lista.aggiungiElemento("Canale verificato: " & FormattaBooleano(details.ChannelVerificato, "Sì", "No"), "VERIFICATO")
            End If
            lista.aggiungiElemento("URL: " & details.Url, "URL")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaInformazioniCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiungiVoceDescrizione(lista As WGMenuLista, details As YoutubeItemDetails)
        Try
            If String.IsNullOrWhiteSpace(details.Description) Then Return

            If details.Description.Length > SOGLIA_TESTO_LUNGO OrElse DescrizioneVideo.Analizza(details.Description).HaStruttura Then
                lista.aggiungiElemento("Descrizione: seleziona per leggerla a paragrafi, con link e punti del video", "DESCRIZIONE")
            Else
                lista.aggiungiElemento("Descrizione: " & details.Description.Replace(Environment.NewLine, " "), "DESCRIZIONE")
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiungiVoceDescrizione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ElementoSelezionatoListaInfo() Handles listaInformazioni.ElementoSelezionato
        Try
            Dim idSelezionati As List(Of String) = listaInformazioni.ottieniElementi()
            If idSelezionati Is Nothing OrElse idSelezionati.Count = 0 Then Return
            If idSelezionati(0) = "DESCRIZIONE" AndAlso _dettagliElementoCorrente IsNot Nothing Then
                MostraDescrizioneStrutturata(_dettagliElementoCorrente)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ElementoSelezionatoListaInfo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraDescrizioneCompleta(details As YoutubeItemDetails)
        Try
            If details Is Nothing OrElse String.IsNullOrWhiteSpace(details.Description) Then Return

            campoTestoLungo = New WGCampo()
            AddHandler campoTestoLungo.chiusura, AddressOf ChiusuraCampoTestoLungo
            campoTestoLungo.init($"Descrizione: {details.Title}", details.Description, True, True)
            campoTestoLungo.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraDescrizioneCompleta]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraCommentoCompleto(commento As YoutubeComment)
        Try
            If commento Is Nothing OrElse String.IsNullOrWhiteSpace(commento.Text) Then Return

            Dim testataCommento As String = commento.Author & Environment.NewLine & commento.Timestamp & " - Mi piace: " & commento.LikeCount.ToString() & Environment.NewLine & Environment.NewLine
            Dim testoCompleto As String = testataCommento & commento.Text

            campoTestoLungo = New WGCampo()
            AddHandler campoTestoLungo.chiusura, AddressOf ChiusuraCampoTestoLungo
            campoTestoLungo.init("Commento di " & commento.Author, testoCompleto, True, True)
            campoTestoLungo.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraCommentoCompleto]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ChiusuraCampoTestoLungo(sender As Object, e As EventArgs)
        Try
            If campoTestoLungo IsNot Nothing Then
                campoTestoLungo.chiudi()
                campoTestoLungo = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ChiusuraCampoTestoLungo]: {ex.ToString()}")
        End Try
    End Sub

    Private Function SeVuoto(testo As String) As String
        Try
            Return If(String.IsNullOrWhiteSpace(testo), "Non disponibile", testo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SeVuoto]: {ex.ToString()}")
            Return "Non disponibile"
        End Try
    End Function

    Private Function FormattaData(data As Date?) As String
        Try
            If Not data.HasValue Then Return "Non disponibile"
            Return data.Value.ToString("dd/MM/yyyy")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.FormattaData]: {ex.ToString()}")
            Return "Non disponibile"
        End Try
    End Function

    Private Function FormattaNumero(numero As Long?) As String
        Try
            If Not numero.HasValue Then Return "Non disponibile"
            Return numero.Value.ToString("N0")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.FormattaNumero]: {ex.ToString()}")
            Return "Non disponibile"
        End Try
    End Function

    Private Function FormattaNumeroInt(numero As Integer?) As String
        Try
            If Not numero.HasValue Then Return "Non disponibile"
            Return numero.Value.ToString("N0")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.FormattaNumeroInt]: {ex.ToString()}")
            Return "Non disponibile"
        End Try
    End Function

    Private Function FormattaBooleano(valore As Boolean?, sePositivo As String, seNegativo As String) As String
        Try
            If Not valore.HasValue Then Return "Non disponibile"
            Return If(valore.Value, sePositivo, seNegativo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.FormattaBooleano]: {ex.ToString()}")
            Return "Non disponibile"
        End Try
    End Function

    Private Sub LavoroVisualizzaCommenti()
        Dim commenti As List(Of YoutubeComment) = Nothing
        Try
            commenti = searcher.IniziaCommentiPaginati(SESSION_COMMENTI, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _ordineCommenti)
            If commenti IsNot Nothing AndAlso commenti.Count > 0 Then
                risultatiCommentiCorrenti = commenti
                EseguiSuThreadUI(AddressOf UI_VisualizzaListaCommenti)
            Else
                EseguiSuThreadUI(AddressOf UI_MessaggioNoCommenti)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroVisualizzaCommenti]: {ex.ToString()}")
        Finally
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_MessaggioNoCommenti()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Nessun commento trovato.")
        msg.ShowDialog()
    End Sub

    Private Sub UI_VisualizzaListaCommenti()
        VisualizzaListaCommenti(risultatiCommentiCorrenti)
    End Sub

    Private Sub VisualizzaListaCommenti(commenti As List(Of YoutubeComment))
        Try
            listaCommenti = New WGMenuLista()
            listaCommenti.ciclono = True
            listaCommenti.impostaTitolo($"Commenti per: {elementoSelezionatoCorrente.Title}")
            listaCommenti.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaCommenti)
            AggiornaOpzionePaginaPrecedenteCommenti()
            listaCommenti.aggiungiOpzione("Leggi il commento per intero", AddressOf OpzioneLeggiCommentoPerIntero)
            listaCommenti.aggiungiOpzione("Copia il testo del commento", AddressOf OpzioneCopiaCommento)
            listaCommenti.aggiungiOpzione(If(_ordineCommenti = "new", "Mostra prima i più rilevanti", "Mostra prima i più recenti"), AddressOf OpzioneCambiaOrdineCommenti)
            AddHandler listaCommenti.iniziolista, AddressOf HandlerInizioListaCommenti
            AddHandler listaCommenti.finelista, AddressOf HandlerFineListaCommenti
            PopolaElementiCommenti(listaCommenti, commenti)
            listaCommenti.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.VisualizzaListaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaCommenti(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            If searcher.CommentiPaginaAttuale(SESSION_COMMENTI) <= 1 Then Return
            _posizioneDopoCambioPagina = "fine"
            OpzionePaginaPrecedenteCommenti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaCommenti(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            _posizioneDopoCambioPagina = "inizio"
            OpzionePaginaSuccessivaCommenti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaElementiCommenti(lista As WGMenuLista, commenti As List(Of YoutubeComment))
        Try
            For indice As Integer = 0 To commenti.Count - 1
                Try
                    Dim comment = commenti(indice)
                    lista.aggiungiElemento(EtichettaCommento(comment, ""), "C" & indice.ToString())
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiCommenti.Loop]: {ex.ToString()}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElementiCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ElementoSelezionatoListaCommenti() Handles listaCommenti.ElementoSelezionato
        Try
            Dim idSelezionati As List(Of String) = listaCommenti.ottieniElementi()
            If idSelezionati Is Nothing OrElse idSelezionati.Count = 0 Then Return

            Dim tag As String = idSelezionati(0)
            If String.IsNullOrEmpty(tag) OrElse Not tag.StartsWith("C") Then Return

            Dim indice As Integer
            If Not Integer.TryParse(tag.Substring(1), indice) Then Return
            If risultatiCommentiCorrenti Is Nothing OrElse indice < 0 OrElse indice >= risultatiCommentiCorrenti.Count Then Return

            Dim commento As YoutubeComment = risultatiCommentiCorrenti(indice)
            If commento Is Nothing OrElse String.IsNullOrWhiteSpace(commento.Text) Then Return

            If commento.Replies.Count > 0 Then
                MostraConversazione(commento)
            ElseIf (commento.Author & ": " & commento.Text).Length > SOGLIA_TESTO_LUNGO Then
                MostraCommentoCompleto(commento)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ElementoSelezionatoListaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaCommenti()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina successiva...")
            AvviaThread(AddressOf ThreadLavoroPaginaSuccessivaCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaSuccessivaCommenti()
        Try
            Dim commenti As List(Of YoutubeComment) = searcher.CommentiPaginaSuccessiva(SESSION_COMMENTI)
            If commenti Is Nothing OrElse commenti.Count = 0 Then
                searcher.CommentiPaginaPrecedente(SESSION_COMMENTI)
                EseguiSuThreadUI(AddressOf UI_NessunAltroRisultatoCommenti)
                Return
            End If
            risultatiCommentiCorrenti = commenti
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaSuccessivaCommenti]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteCommenti()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina precedente...")
            AvviaThread(AddressOf ThreadLavoroPaginaPrecedenteCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaPrecedenteCommenti()
        Try
            Dim commenti As List(Of YoutubeComment) = searcher.CommentiPaginaPrecedente(SESSION_COMMENTI)
            risultatiCommentiCorrenti = commenti
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaPrecedenteCommenti]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_NessunAltroRisultatoCommenti()
        Try
            ChiudiProcessoInCorso()
            If Not String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                _posizioneDopoCambioPagina = ""
                If listaCommenti IsNot Nothing AndAlso Not listaCommenti.IsDisposed AndAlso risultatiCommentiCorrenti IsNot Nothing AndAlso risultatiCommentiCorrenti.Count > 0 Then
                    listaCommenti.selezionaElemento(0)
                End If
                Return
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun altro commento disponibile.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_NessunAltroRisultatoCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_PostCambioPaginaCommenti()
        Try
            ChiudiProcessoInCorso()
            If listaCommenti Is Nothing OrElse listaCommenti.IsDisposed Then Return

            If risultatiCommentiCorrenti Is Nothing OrElse risultatiCommentiCorrenti.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun commento trovato per questa pagina.")
                messaggio.ShowDialog()
                _posizioneDopoCambioPagina = ""
                Return
            End If

            listaCommenti.pulisci()
            PopolaElementiCommenti(listaCommenti, risultatiCommentiCorrenti)
            AggiornaOpzionePaginaPrecedenteCommenti()

            If _posizioneDopoCambioPagina = "fine" Then
                listaCommenti.SelezionaUltimoElemento()
            ElseIf _posizioneDopoCambioPagina = "inizio" OrElse String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                If risultatiCommentiCorrenti.Count > 0 Then listaCommenti.selezionaElemento(0)
            End If
            _posizioneDopoCambioPagina = ""
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostCambioPaginaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzionePaginaPrecedenteCommenti()
        Try
            If listaCommenti Is Nothing OrElse listaCommenti.IsDisposed Then Return
            If searcher Is Nothing Then Return

            listaCommenti.rimuoviOpzione("Pagina precedente")
            If searcher.CommentiPaginaAttuale(SESSION_COMMENTI) > 1 Then
                listaCommenti.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteCommenti)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzionePaginaPrecedenteCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub LavoroRicercaCanale()
        Dim videoCanale As List(Of YoutubeSearchResult) = Nothing
        Try
            _inRicercaInterna = False
            _sortByContenuti = LeggiOrdinamentoPreferito(YoutubeItemType.Video)
            Dim filtroCanale = LeggiFiltroDurataPreferito()
            _minDurataContenuti = filtroCanale.Min
            _maxDurataContenuti = filtroCanale.Max
            videoCanale = searcher.IniziaVideoCanalePaginato(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
            risultatiContenutiCorrenti = videoCanale
            EseguiSuThreadUI(AddressOf UI_PostRicercaCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostRicercaCanale()
        ChiudiProcessoInCorso()
        If risultatiContenutiCorrenti IsNot Nothing AndAlso risultatiContenutiCorrenti.Count > 0 Then
            elencoContenuti = New WGMenuLista()
            elencoContenuti.impostaTitolo($"Video del canale: {elementoSelezionatoCorrente.Title}")
            PopolaListaContenuti(elencoContenuti, risultatiContenutiCorrenti)
        End If
    End Sub

    Private Sub LavoroRicercaPlaylist()
        Dim videoPlaylist As List(Of YoutubeSearchResult) = Nothing
        Try
            _inRicercaInterna = False
            _sortByContenuti = LeggiOrdinamentoPreferito(YoutubeItemType.Video)
            Dim filtroPlaylist = LeggiFiltroDurataPreferito()
            _minDurataContenuti = filtroPlaylist.Min
            _maxDurataContenuti = filtroPlaylist.Max
            videoPlaylist = searcher.IniziaVideoPlaylistPaginato(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
            risultatiContenutiCorrenti = videoPlaylist
            EseguiSuThreadUI(AddressOf UI_PostRicercaPlaylist)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaPlaylist]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostRicercaPlaylist()
        ChiudiProcessoInCorso()
        If risultatiContenutiCorrenti IsNot Nothing AndAlso risultatiContenutiCorrenti.Count > 0 Then
            elencoContenuti = New WGMenuLista()
            elencoContenuti.impostaTitolo($"Video in: {elementoSelezionatoCorrente.Title}")
            PopolaListaContenuti(elencoContenuti, risultatiContenutiCorrenti)
        End If
    End Sub

    Private Sub PopolaListaContenuti(lista As WGMenuLista, risultati As List(Of YoutubeSearchResult))
        Try
            lista.ciclono = True
            lista.multiselezioneConsentita = True
            lista.aggiungiOpzione("Riproduci Video", AddressOf OpzioneRiproduciVideoContenuti)
            lista.aggiungiOpzione("Informazioni", AddressOf OpzioneInformazioniContenuti)
            lista.aggiungiOpzione("Visualizza Commenti", AddressOf OpzioneVisualizzaCommentiContenuti)
            lista.aggiungiOpzione("Vai al Canale", AddressOf OpzioneVaiAlCanaleContenuti)
            lista.aggiungiOpzione("Copia URL", AddressOf OpzioneCopiaUrlContenuti)
            If RiassuntoVideoDisponibile() Then lista.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoContenuti)
            lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoContenuti)
            lista.aggiungiOpzione("Ordinamento", AddressOf OpzioneOrdinamentoContenuti)
            lista.aggiungiOpzione("Filtro durata", AddressOf OpzioneFiltroDurataContenuti)
            lista.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaContenuti)
            AggiornaOpzionePaginaPrecedenteContenuti()

            RemoveHandler lista.ElementoSelezionato, AddressOf HandlerDinamicoPreferitiContenuti
            AddHandler lista.ElementoSelezionato, AddressOf HandlerDinamicoPreferitiContenuti
            RemoveHandler lista.iniziolista, AddressOf HandlerInizioListaContenuti
            AddHandler lista.iniziolista, AddressOf HandlerInizioListaContenuti
            RemoveHandler lista.finelista, AddressOf HandlerFineListaContenuti
            AddHandler lista.finelista, AddressOf HandlerFineListaContenuti

            For Each item In risultati
                Try
                    lista.aggiungiElemento($"[Video] {item.Title}", item.Id)
                Catch ex As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaListaContenuti.Loop]: {ex.ToString()}")
                End Try
            Next
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaListaContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaContenuti(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            If searcher.PaginaAttuale(SESSION_CONTENUTI) <= 1 Then Return
            _posizioneDopoCambioPagina = "fine"
            OpzionePaginaPrecedenteContenuti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaContenuti(sender As Object, e As EventArgs)
        Try
            If searcher Is Nothing Then Return
            _posizioneDopoCambioPagina = "inizio"
            OpzionePaginaSuccessivaContenuti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaContenuti()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina successiva...")
            AvviaThread(AddressOf ThreadLavoroPaginaSuccessivaContenuti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaSuccessivaContenuti()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaSuccessiva(SESSION_CONTENUTI)
            If risultati Is Nothing OrElse risultati.Count = 0 Then
                searcher.PaginaPrecedente(SESSION_CONTENUTI)
                EseguiSuThreadUI(AddressOf UI_NessunAltroRisultatoContenuti)
                Return
            End If
            risultatiContenutiCorrenti = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaContenuti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaSuccessivaContenuti]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteContenuti()
        Try
            If searcher Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Caricamento pagina precedente...")
            AvviaThread(AddressOf ThreadLavoroPaginaPrecedenteContenuti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroPaginaPrecedenteContenuti()
        Try
            Dim risultati As List(Of YoutubeSearchResult) = searcher.PaginaPrecedente(SESSION_CONTENUTI)
            risultatiContenutiCorrenti = risultati
            EseguiSuThreadUI(AddressOf UI_PostCambioPaginaContenuti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroPaginaPrecedenteContenuti]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_NessunAltroRisultatoContenuti()
        Try
            ChiudiProcessoInCorso()
            If Not String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                _posizioneDopoCambioPagina = ""
                If elencoContenuti IsNot Nothing AndAlso Not elencoContenuti.IsDisposed AndAlso risultatiContenutiCorrenti IsNot Nothing AndAlso risultatiContenutiCorrenti.Count > 0 Then
                    elencoContenuti.selezionaElemento(0)
                End If
                Return
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun altro risultato disponibile.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_NessunAltroRisultatoContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_PostCambioPaginaContenuti()
        Try
            ChiudiProcessoInCorso()
            If elencoContenuti Is Nothing OrElse elencoContenuti.IsDisposed Then Return

            If risultatiContenutiCorrenti Is Nothing OrElse risultatiContenutiCorrenti.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessun risultato trovato per questa pagina.")
                messaggio.ShowDialog()
                _posizioneDopoCambioPagina = ""
                Return
            End If

            AggiornaListaRisultati(elencoContenuti, risultatiContenutiCorrenti)
            AggiornaOpzionePaginaPrecedenteContenuti()

            If _posizioneDopoCambioPagina = "fine" Then
                elencoContenuti.SelezionaUltimoElemento()
            ElseIf _posizioneDopoCambioPagina = "inizio" OrElse String.IsNullOrEmpty(_posizioneDopoCambioPagina) Then
                If risultatiContenutiCorrenti.Count > 0 Then elencoContenuti.selezionaElemento(0)
            End If
            _posizioneDopoCambioPagina = ""
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostCambioPaginaContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzionePaginaPrecedenteContenuti()
        Try
            If elencoContenuti Is Nothing OrElse elencoContenuti.IsDisposed Then Return
            If searcher Is Nothing Then Return

            elencoContenuti.rimuoviOpzione("Pagina precedente")
            If searcher.PaginaAttuale(SESSION_CONTENUTI) > 1 Then
                elencoContenuti.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteContenuti)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzionePaginaPrecedenteContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickOrdinaContenuti()
        OpzioneOrdinaGenerica(CType(elencoContenuti, Object), risultatiContenutiCorrenti, AddressOf AggiornaListaRisultati)
    End Sub

    ''' <summary>HandlerDinamicoPreferitiContenuti con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub HandlerDinamicoPreferitiContenuti(idEvento As String)
        HandlerDinamicoPreferitiContenuti()
    End Sub

    Private Sub HandlerDinamicoPreferitiContenuti()
        GestioneDinamicaPreferiti(elencoContenuti, risultatiContenutiCorrenti)
    End Sub

    Private Sub OpzioneAggiungiPreferitoContenuti()
        Try
            AzioneAggiungiPreferitoGenerica(elencoContenuti, risultatiContenutiCorrenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAggiungiPreferitoContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiproduciVideoContenuti()
        Try
            Dim ids = elencoContenuti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return
            End If
            Dim item = TrovaRisultatoPerId(ids(0), risultatiContenutiCorrenti)
            If item IsNot Nothing Then
                AvviaPlayer(item.Title, New List(Of String) From {item.Url})
                _gestoreXml.AggiungiRecente(item.Id, item.Title, item.Url, item.ItemType)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiproduciVideoContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ElementoSelezionatoContenuti() Handles elencoContenuti.ElementoSelezionato
        Try
            OpzioneRiproduciVideoContenuti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ElementoSelezionatoContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadCaricaPlaylist()
        LavoroCaricaPlaylistPerRiproduzione(_tempIsShuffle)
    End Sub

    Private Sub LavoroCaricaPlaylistPerRiproduzione(isShuffled As Boolean)
        Try
            Dim videoPlaylist = searcher.GetPlaylistVideos(elementoSelezionatoCorrente.Id)
            If videoPlaylist IsNot Nothing AndAlso videoPlaylist.Count > 0 Then
                Dim urls As New List(Of String)
                For Each v In videoPlaylist
                    Try
                        urls.Add(v.Url)
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.LavoroCaricaPlaylistPerRiproduzione.Loop]: {ex.ToString()}")
                    End Try
                Next

                If isShuffled Then
                    Dim rnd As New Random()
                    Dim randomized = From x In urls Order By rnd.Next() Select x
                    urls = randomized.ToList()
                End If

                _contextDatiOrdinamento = urls
                EseguiSuThreadUI(AddressOf UI_AvviaPlayerPlaylist)
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Playlist vuota.")
                EseguiSuThreadUI(AddressOf UI_MessaggioPlaylistVuota)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE: {ex.ToString()}")
        Finally
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_MessaggioPlaylistVuota()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Playlist vuota.")
        msg.ShowDialog()
    End Sub

    Private Sub UI_AvviaPlayerPlaylist()
        If _contextDatiOrdinamento IsNot Nothing Then
            AvviaPlayer(elementoSelezionatoCorrente.Title, CType(_contextDatiOrdinamento, List(Of String)))
        End If
    End Sub

    Private Sub AvviaPlayer(titolo As String, urls As List(Of String))
        _tempFiltroCategoria = titolo
        _contextDatiOrdinamento = urls
        EseguiSuThreadUI(AddressOf UI_LaunchPlayer)
    End Sub

    ' Dove si ascolta lo decide la scelta delle impostazioni (sottofondo, lettore o richiesta ogni volta): il lettore a schermo, con i capitoli
    ' e il salto iniziale, è ApriLettoreYoutube.
    Private Sub UI_LaunchPlayer()
        Try
            Dim indirizzi As List(Of String) = TryCast(_contextDatiOrdinamento, List(Of String))
            If indirizzi Is Nothing OrElse indirizzi.Count = 0 OrElse WGModalitaAscolto.Corrente() = WGModalitaAscolto.Lettore Then
                ApriLettoreYoutube()
                Return
            End If

            ChiudiProcessoInCorso()
            Dim titolo As String = _tempFiltroCategoria
            Dim copia As New List(Of String)(indirizzi)
            WGModalitaAscolto.Avvia(titolo,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciFileAudio(copia),
                                    Sub() ApriLettoreYoutube())
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_LaunchPlayer]: {ex.ToString()}")
            ChiudiProcessoInCorso()
        End Try
    End Sub

    Private Sub ApriLettoreYoutube()
        Try
            If _playerForm IsNot Nothing Then
                _playerForm.Dispose()
                _playerForm = Nothing
            End If
            _playerForm = New WGPlayerFormInt()
            _playerForm.ImpostaProviderCapitoli(New YtdlpCapitoliProvider(searcher))
            _playerForm.ImpostaProviderInterazioni(New YoutubeInterazioniProvider(searcher))

            If _contextDatiOrdinamento IsNot Nothing Then
                Dim urls = CType(_contextDatiOrdinamento, List(Of String))
                For Each url In urls
                    Try
                        _playerForm.AggiungiElemento(url)
                    Catch ex As Exception
                        logger.ScriviLog($"ERRORE [UI_LaunchPlayer.Loop]: {ex.ToString()}")
                    End Try
                Next

                _playerForm.avvia(_tempFiltroCategoria)
                PianificaSaltoIniziale()
            Else
            End If
            ChiudiProcessoInCorso()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaPlayer]: {ex.ToString()}")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore avvio player: " & ex.Message)
            msg.ShowDialog()
        End Try
    End Sub

    Private Function RiassuntoVideoDisponibile() As Boolean
        Try
            Return WGAi.ChiaveApiConfigurata(WGAi.ProviderAi.Gemini)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RiassuntoVideoDisponibile]: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub AggiungiVideoDaRiassumere(destinazione As List(Of YoutubeSearchResult), elemento As YoutubeSearchResult)
        Try
            If elemento Is Nothing Then Return
            If elemento.ItemType <> YoutubeItemType.Video Then Return
            If String.IsNullOrWhiteSpace(elemento.Url) Then Return
            For Each presente As YoutubeSearchResult In destinazione
                If String.Equals(presente.Url, elemento.Url, StringComparison.OrdinalIgnoreCase) Then Return
            Next
            destinazione.Add(elemento)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiungiVideoDaRiassumere]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiassumiVideoRisultati()
        Try
            Dim video As New List(Of YoutubeSearchResult)
            If elencorisultati IsNot Nothing AndAlso Not elencorisultati.IsDisposed AndAlso risultatiRicercaCorrente IsNot Nothing Then
                Dim elementi As List(Of String) = elencorisultati.ottieniElementi()
                If elementi IsNot Nothing Then
                    For Each identificativo As String In elementi
                        AggiungiVideoDaRiassumere(video, TrovaRisultatoPerId(identificativo, risultatiRicercaCorrente))
                    Next
                End If
            End If
            AvviaRiassuntoVideo(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiassumiVideoRisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiassumiVideoContenuti()
        Try
            Dim video As New List(Of YoutubeSearchResult)
            If elencoContenuti IsNot Nothing AndAlso Not elencoContenuti.IsDisposed AndAlso risultatiContenutiCorrenti IsNot Nothing Then
                Dim elementi As List(Of String) = elencoContenuti.ottieniElementi()
                If elementi IsNot Nothing Then
                    For Each identificativo As String In elementi
                        AggiungiVideoDaRiassumere(video, TrovaRisultatoPerId(identificativo, risultatiContenutiCorrenti))
                    Next
                End If
            End If
            AvviaRiassuntoVideo(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiassumiVideoContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiassumiVideoPreferiti()
        Try
            Dim video As New List(Of YoutubeSearchResult)
            If elencoPreferiti IsNot Nothing AndAlso Not elencoPreferiti.IsDisposed Then
                Dim elementi As List(Of String) = elencoPreferiti.ottieniElementi()
                If elementi IsNot Nothing Then
                    For Each identificativo As String In elementi
                        Dim salvato As ElementoSalvato = TrovaPreferitoPerId(identificativo)
                        If salvato IsNot Nothing Then AggiungiVideoDaRiassumere(video, ConvertiSalvatoInSearchResult(salvato))
                    Next
                End If
            End If
            AvviaRiassuntoVideo(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiassumiVideoPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiassumiVideoCronologia()
        Try
            Dim video As New List(Of YoutubeSearchResult)
            If elencoCronologia IsNot Nothing AndAlso Not elencoCronologia.IsDisposed Then
                Dim elementi As List(Of String) = elencoCronologia.ottieniElementi()
                If elementi IsNot Nothing Then
                    For Each identificativo As String In elementi
                        Dim salvato As ElementoSalvato = TrovaPreferitoPerId(identificativo)
                        If salvato IsNot Nothing Then AggiungiVideoDaRiassumere(video, ConvertiSalvatoInSearchResult(salvato))
                    Next
                End If
            End If
            AvviaRiassuntoVideo(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiassumiVideoCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiassumiVideoNuovi()
        Try
            Dim video As New List(Of YoutubeSearchResult)
            If elencoVideoNuovi IsNot Nothing AndAlso Not elencoVideoNuovi.IsDisposed Then
                Dim elementi As List(Of String) = elencoVideoNuovi.ottieniElementi()
                If elementi IsNot Nothing Then
                    For Each identificativo As String In elementi
                        Dim nuovo As VideoNuovo = TrovaVideoNuovoPerId(identificativo)
                        If nuovo IsNot Nothing Then AggiungiVideoDaRiassumere(video, ConvertiVideoNuovoInSearchResult(nuovo))
                    Next
                End If
            End If
            AvviaRiassuntoVideo(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiassumiVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Async Sub AvviaRiassuntoVideo(video As List(Of YoutubeSearchResult))
        Dim idOperazione As Integer = 0
        Try
            If video Is Nothing OrElse video.Count = 0 Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Seleziona almeno un video.")
                avviso.ShowDialog()
                Return
            End If
            If Not RiassuntoVideoDisponibile() Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Il riassunto dei video richiede la chiave API di Gemini.")
                avviso.ShowDialog()
                Return
            End If

            _idRiassuntoVideo += 1
            idOperazione = _idRiassuntoVideo
            ChiudiProcessoInCorso()
            processoincorso = New WGProcessoInCorso()
            If video.Count = 1 Then
                processoincorso.Avvia($"Riassunto del video in corso: {video(0).Title}")
            Else
                processoincorso.Avvia($"Riassunto di {video.Count} video in corso")
            End If

            Dim testo As New System.Text.StringBuilder()
            Dim riusciti As Integer = 0
            Dim ultimoErrore As String = ""
            For Each elemento As YoutubeSearchResult In video
                Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiVideoAsync(elemento.Url, elemento.Title)
                If idOperazione <> _idRiassuntoVideo Then Return
                If video.Count > 1 Then
                    If testo.Length > 0 Then testo.AppendLine().AppendLine()
                    testo.AppendLine(elemento.Title)
                End If
                If risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item1) Then
                    testo.Append(risultato.Item1.Trim())
                    riusciti += 1
                Else
                    ultimoErrore = If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), risultato.Item2, "Riassunto non riuscito")
                    testo.Append("Riassunto non riuscito: ").Append(ultimoErrore)
                End If
            Next

            ChiudiProcessoInCorso()

            If riusciti = 0 AndAlso video.Count = 1 Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio(ultimoErrore)
                avviso.ShowDialog()
                Return
            End If

            Dim titolo As String = If(video.Count = 1, "Riassunto: " & video(0).Title, "Riassunto di " & video.Count.ToString() & " video")
            MostraRiassuntoVideo(titolo, testo.ToString())
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaRiassuntoVideo]: {ex.ToString()}")
            If idOperazione = _idRiassuntoVideo Then ChiudiProcessoInCorso()
        End Try
    End Sub

    Private Sub MostraRiassuntoVideo(titolo As String, testo As String)
        Try
            ChiudiRiassuntoVideo()
            campoRiassuntoVideo = New WGCampo()
            AddHandler campoRiassuntoVideo.chiusura, AddressOf ChiusuraCampoRiassuntoVideo
            campoRiassuntoVideo.init(titolo, If(testo, ""), True, True)
            campoRiassuntoVideo.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraRiassuntoVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ChiusuraCampoRiassuntoVideo(sender As Object, e As EventArgs)
        Try
            ChiudiRiassuntoVideo()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ChiusuraCampoRiassuntoVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ChiudiRiassuntoVideo()
        Try
            If campoRiassuntoVideo Is Nothing Then Return
            RemoveHandler campoRiassuntoVideo.chiusura, AddressOf ChiusuraCampoRiassuntoVideo
            campoRiassuntoVideo.chiudi()
            campoRiassuntoVideo = Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ChiudiRiassuntoVideo]: {ex.ToString()}")
            campoRiassuntoVideo = Nothing
        End Try
    End Sub

    Private Sub ChiudiProcessoInCorso()
        Try
            If processoincorso IsNot Nothing Then
                processoincorso.Fine()
                processoincorso = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ChiudiProcessoInCorso]: {ex.ToString()}")
        End Try
    End Sub

    Private Function GetTipoStringa(tipo As YoutubeItemType) As String
        Try
            Select Case tipo
                Case YoutubeItemType.Video : Return "Video"
                Case YoutubeItemType.Playlist : Return "Playlist"
                Case YoutubeItemType.Channel : Return "Canale"
                Case Else : Return "Sconosciuto"
            End Select
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.GetTipoStringa]: {ex.ToString()}")
            Return "Errore"
        End Try
    End Function

    Private Function TruncateText(text As String, Optional maxLength As Integer = 40) As String
        Try
            If String.IsNullOrWhiteSpace(text) Then
                Return ""
            End If
            Dim clean = text.Replace(Environment.NewLine, " ").Trim()
            If clean.Length <= maxLength Then
                Return clean
            End If
            Return clean.Substring(0, maxLength) & "..."
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TruncateText]: {ex.ToString()}")
            Return text
        End Try
    End Function

    Private Sub chiudirisultati() Handles elencorisultati.escPremuto
        Try
            If elencorisultati IsNot Nothing Then
                elencorisultati.abbandona()
                elencorisultati = Nothing
            End If
            If searcher IsNot Nothing Then
                searcher.TerminaRicercaPaginata(SESSION_RICERCA)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudirisultati]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiContenuti() Handles elencoContenuti.escPremuto
        Try
            If elencoContenuti IsNot Nothing Then
                elencoContenuti.abbandona()
                elencoContenuti = Nothing
            End If
            If searcher IsNot Nothing Then
                searcher.TerminaRicercaPaginata(SESSION_CONTENUTI)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoPlaylistCanale() Handles elencoPlaylistCanale.escPremuto
        Try
            If elencoPlaylistCanale IsNot Nothing Then
                elencoPlaylistCanale.abbandona()
                elencoPlaylistCanale = Nothing
            End If
            If searcher IsNot Nothing Then
                searcher.TerminaRicercaPaginata(SESSION_PLAYLIST_CANALE)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoPlaylistCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoPreferiti() Handles elencoPreferiti.escPremuto
        Try
            If elencoPreferiti IsNot Nothing Then
                elencoPreferiti.abbandona()
                elencoPreferiti = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoPreferiti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoCategorie() Handles elencoCategorie.escPremuto
        Try
            If elencoCategorie IsNot Nothing Then
                elencoCategorie.abbandona()
                elencoCategorie = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoCategorie]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiListaInfo() Handles listaInformazioni.escPremuto
        Try
            If listaInformazioni IsNot Nothing Then
                listaInformazioni.abbandona()
                listaInformazioni = Nothing
            End If
            _dettagliElementoCorrente = Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiListaInfo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiListaCommenti() Handles listaCommenti.escPremuto
        Try
            If listaCommenti IsNot Nothing Then
                listaCommenti.abbandona()
                listaCommenti = Nothing
            End If
            If searcher IsNot Nothing Then
                searcher.TerminaCommentiPaginati(SESSION_COMMENTI)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiListaCommenti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoCronologia() Handles elencoCronologia.escPremuto

        Try
            If elencoCronologia IsNot Nothing Then
                elencoCronologia.abbandona()
                elencoCronologia = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoCronologia]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoCronologiaRicerche() Handles elencoCronologiaRicerche.escPremuto
        Try
            If elencoCronologiaRicerche IsNot Nothing Then
                elencoCronologiaRicerche.abbandona()
                elencoCronologiaRicerche = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoCronologiaRicerche]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoSceltaCategoria() Handles elencoSceltaCategoria.escPremuto
        Try
            If elencoSceltaCategoria IsNot Nothing Then
                elencoSceltaCategoria.abbandona()
                elencoSceltaCategoria = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoSceltaCategoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Function SortByToString(s As YoutubeSortBy) As String
        Return s.ToString()
    End Function

    Private Function StringToSortBy(valore As String) As YoutubeSortBy
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return YoutubeSortBy.None
            Dim parsed As YoutubeSortBy
            If [Enum].TryParse(Of YoutubeSortBy)(valore, True, parsed) Then Return parsed
        Catch
        End Try
        Return YoutubeSortBy.None
    End Function

    Private Function GetSortLabel(s As YoutubeSortBy) As String
        Select Case s
            Case YoutubeSortBy.None : Return "Nessun ordinamento (predefinito YouTube)"
            Case YoutubeSortBy.DateAscending : Return "Data (meno recente prima)"
            Case YoutubeSortBy.DateDescending : Return "Data (più recente prima)"
            Case YoutubeSortBy.ViewsAscending : Return "Visualizzazioni (crescenti)"
            Case YoutubeSortBy.ViewsDescending : Return "Visualizzazioni (decrescenti)"
            Case YoutubeSortBy.LikesAscending : Return "Mi piace (crescenti)"
            Case YoutubeSortBy.LikesDescending : Return "Mi piace (decrescenti)"
            Case YoutubeSortBy.SubscribersAscending : Return "Iscritti (crescenti)"
            Case YoutubeSortBy.SubscribersDescending : Return "Iscritti (decrescenti)"
            Case YoutubeSortBy.TitleAscending : Return "Titolo A-Z"
            Case YoutubeSortBy.TitleDescending : Return "Titolo Z-A"
            Case YoutubeSortBy.DurationAscending : Return "Durata (crescente)"
            Case YoutubeSortBy.DurationDescending : Return "Durata (decrescente)"
            Case YoutubeSortBy.VideoCountAscending : Return "Numero video (crescente)"
            Case YoutubeSortBy.VideoCountDescending : Return "Numero video (decrescente)"
            Case Else : Return s.ToString()
        End Select
    End Function

    Private Function LeggiOrdinamentoPreferito(tipo As YoutubeItemType) As YoutubeSortBy
        Try
            Dim config As New WGConfig()
            Dim chiave As String
            Select Case tipo
                Case YoutubeItemType.Video
                    chiave = "ordinamento_youtube_video"
                Case YoutubeItemType.Channel
                    chiave = "ordinamento_youtube_canale"
                Case YoutubeItemType.Playlist
                    chiave = "ordinamento_youtube_playlist"
                Case Else
                    chiave = "ordinamento_youtube_video"
            End Select
            Return StringToSortBy(LeggiConfigValoreYoutube(config, chiave, "None"))
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [LeggiOrdinamentoPreferito]: {ex.ToString()}")
            Return YoutubeSortBy.None
        End Try
    End Function

    Private Sub SalvaOrdinamentoPreferito(tipo As YoutubeItemType, sortBy As YoutubeSortBy)
        Try
            Dim config As New WGConfig()
            Dim chiave As String
            Select Case tipo
                Case YoutubeItemType.Video
                    chiave = "ordinamento_youtube_video"
                Case YoutubeItemType.Channel
                    chiave = "ordinamento_youtube_canale"
                Case YoutubeItemType.Playlist
                    chiave = "ordinamento_youtube_playlist"
                Case Else
                    chiave = "ordinamento_youtube_video"
            End Select
            SalvaConfigurazioneYoutube(config, chiave, SortByToString(sortBy))
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [SalvaOrdinamentoPreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Function LeggiFiltroDurataPreferito() As (Min As Long?, Max As Long?)
        Try
            Dim config As New WGConfig()
            Dim minStr = LeggiConfigValoreYoutube(config, "filtro_durata_min", "")
            Dim maxStr = LeggiConfigValoreYoutube(config, "filtro_durata_max", "")
            Dim minV As Long? = Nothing
            Dim maxV As Long? = Nothing
            Dim tmp As Long
            If Long.TryParse(minStr, tmp) Then minV = tmp
            If Long.TryParse(maxStr, tmp) Then maxV = tmp
            Return (minV, maxV)
        Catch
            Return (Nothing, Nothing)
        End Try
    End Function

    Private Sub SalvaFiltroDurataPreferito(minSec As Long?, maxSec As Long?)
        Try
            Dim config As New WGConfig()
            SalvaConfigurazioneYoutube(config, "filtro_durata_min", If(minSec.HasValue, minSec.Value.ToString(), ""))
            SalvaConfigurazioneYoutube(config, "filtro_durata_max", If(maxSec.HasValue, maxSec.Value.ToString(), ""))
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [SalvaFiltroDurataPreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuOrdinamentoPreferitoVideo()
        MostraMenuSceltaOrdinamentoPreferito(YoutubeItemType.Video)
    End Sub
    Private Sub UI_MenuOrdinamentoPreferitoCanali()
        MostraMenuSceltaOrdinamentoPreferito(YoutubeItemType.Channel)
    End Sub
    Private Sub UI_MenuOrdinamentoPreferitoPlaylist()
        MostraMenuSceltaOrdinamentoPreferito(YoutubeItemType.Playlist)
    End Sub

    Private Sub MostraMenuSceltaOrdinamentoPreferito(tipo As YoutubeItemType)
        Try
            Dim attuale = LeggiOrdinamentoPreferito(tipo)
            Dim m As New WGMenu()
            Dim opzioni As YoutubeSortBy() = {
                YoutubeSortBy.None,
                YoutubeSortBy.DateDescending, YoutubeSortBy.DateAscending,
                YoutubeSortBy.ViewsDescending, YoutubeSortBy.ViewsAscending,
                YoutubeSortBy.LikesDescending, YoutubeSortBy.LikesAscending,
                YoutubeSortBy.SubscribersDescending, YoutubeSortBy.SubscribersAscending,
                YoutubeSortBy.TitleAscending, YoutubeSortBy.TitleDescending,
                YoutubeSortBy.DurationDescending, YoutubeSortBy.DurationAscending,
                YoutubeSortBy.VideoCountDescending, YoutubeSortBy.VideoCountAscending
            }
            For Each s In opzioni
                Dim sortCopia = s
                Dim label = GetSortLabel(s)
                If s = attuale Then label = label & " [attuale]"
                m.aggiungi(label, Sub()
                                      SalvaOrdinamentoPreferito(tipo, sortCopia)
                                      Dim msg As New WGMessaggi()
                                      msg.Messaggio("Ordinamento predefinito salvato: " & GetSortLabel(sortCopia))
                                      msg.ShowDialog()
                                  End Sub)
            Next
            m.avvia("Ordinamento predefinito " & GetTipoStringa(tipo), True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MostraMenuSceltaOrdinamentoPreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuFiltroDurataPreferito()
        Try
            Dim attuale = LeggiFiltroDurataPreferito()
            Dim m As New WGMenu()
            m.aggiungi("Nessun filtro (tutti i video)", Sub()
                                                           SalvaFiltroDurataPreferito(Nothing, Nothing)
                                                           Dim msg As New WGMessaggi()
                                                           msg.Messaggio("Filtro durata disattivato.")
                                                           msg.ShowDialog()
                                                       End Sub)
            m.aggiungi("Solo video < 2 minuti", Sub() SalvaEConfermaFiltro(Nothing, 120))
            m.aggiungi("Solo video da 2 a 10 minuti", Sub() SalvaEConfermaFiltro(120, 600))
            m.aggiungi("Solo video da 10 a 30 minuti", Sub() SalvaEConfermaFiltro(600, 1800))
            m.aggiungi("Solo video > 10 minuti", Sub() SalvaEConfermaFiltro(600, Nothing))
            m.aggiungi("Solo video > 30 minuti", Sub() SalvaEConfermaFiltro(1800, Nothing))
            m.aggiungi("Solo video > 1 ora", Sub() SalvaEConfermaFiltro(3600, Nothing))
            m.avvia("Filtro durata video predefinito", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [UI_MenuFiltroDurataPreferito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SalvaEConfermaFiltro(minS As Long?, maxS As Long?)
        SalvaFiltroDurataPreferito(minS, maxS)
        Dim msg As New WGMessaggi()
        msg.Messaggio("Filtro durata predefinito salvato.")
        msg.ShowDialog()
    End Sub

    Private Sub OpzioneOrdinamentoRicerca()
        MostraMenuOrdinamentoDinamico(risultatiRicercaCorrente, "ricerca")
    End Sub
    Private Sub OpzioneOrdinamentoContenuti()
        MostraMenuOrdinamentoDinamico(risultatiContenutiCorrenti, "contenuti")
    End Sub
    Private Sub OpzioneOrdinamentoPlaylistCanale()
        MostraMenuOrdinamentoDinamico(risultatiPlaylistCanale, "playlistCanale")
    End Sub

    Private Sub MostraMenuOrdinamentoDinamico(risultati As List(Of YoutubeSearchResult), tipoLista As String)
        Try
            If searcher Is Nothing OrElse risultati Is Nothing Then Return
            Dim disponibili = searcher.OttieniOrdinamentiDisponibili(risultati)
            If disponibili Is Nothing OrElse disponibili.Count = 0 Then
                disponibili = New List(Of YoutubeSortBy) From {YoutubeSortBy.None, YoutubeSortBy.TitleAscending, YoutubeSortBy.TitleDescending}
            End If

            Dim m As New WGMenu()
            For Each s In disponibili
                Dim sortCopia = s
                Dim tipoCopia = tipoLista
                m.aggiungi(GetSortLabel(s), Sub()
                                                _contextSortDaApplicare = sortCopia
                                                _contextTipoListaOrdinamento = tipoCopia
                                                ApplicaNuovoOrdinamento()
                                            End Sub)
            Next
            m.avvia("Ordinamento", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MostraMenuOrdinamentoDinamico]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ApplicaNuovoOrdinamento()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Applicazione ordinamento...")
            AvviaThread(AddressOf ThreadApplicaOrdinamento)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [ApplicaNuovoOrdinamento]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadApplicaOrdinamento()
        Try
            Dim sort = _contextSortDaApplicare
            Select Case _contextTipoListaOrdinamento
                Case "ricerca"
                    _sortByRicerca = sort
                    Dim risultati = searcher.IniziaRicercaPaginata(SESSION_RICERCA, queryRicerca, DIMENSIONE_PAGINA_RICERCA, _tipoRicercaCorrente, _sortByRicerca, _minDurataRicerca, _maxDurataRicerca, _filtriRicerca)
                    risultatiRicercaCorrente = risultati
                    EseguiSuThreadUI(AddressOf UI_RicaricaListaRicercaDopoOrdinamento)
                Case "contenuti"
                    _sortByContenuti = sort
                    Dim risultati As List(Of YoutubeSearchResult) = CaricaContenutiCorrenti()
                    risultatiContenutiCorrenti = risultati
                    EseguiSuThreadUI(AddressOf UI_RicaricaListaContenutiDopoOrdinamento)
                Case "playlistCanale"
                    _sortByPlaylistCanale = sort
                    Dim risultati = searcher.IniziaPlaylistCanalePaginato(SESSION_PLAYLIST_CANALE, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByPlaylistCanale)
                    risultatiPlaylistCanale = risultati
                    EseguiSuThreadUI(AddressOf UI_RicaricaListaPlaylistCanaleDopoOrdinamento)
            End Select
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [ThreadApplicaOrdinamento]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_RicaricaListaRicercaDopoOrdinamento()
        Try
            ChiudiProcessoInCorso()
            If elencorisultati Is Nothing OrElse elencorisultati.IsDisposed Then Return
            If risultatiRicercaCorrente Is Nothing OrElse risultatiRicercaCorrente.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun risultato con questo ordinamento.")
                msg.ShowDialog()
                Return
            End If
            AggiornaListaRisultati(elencorisultati, risultatiRicercaCorrente)
            AggiornaOpzionePaginaPrecedente()
            If risultatiRicercaCorrente.Count > 0 Then elencorisultati.selezionaElemento(0)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [UI_RicaricaListaRicercaDopoOrdinamento]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_RicaricaListaContenutiDopoOrdinamento()
        Try
            ChiudiProcessoInCorso()
            If elencoContenuti Is Nothing OrElse elencoContenuti.IsDisposed Then Return
            If risultatiContenutiCorrenti Is Nothing OrElse risultatiContenutiCorrenti.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun risultato con questo ordinamento/filtro.")
                msg.ShowDialog()
                Return
            End If
            AggiornaListaRisultati(elencoContenuti, risultatiContenutiCorrenti)
            AggiornaOpzionePaginaPrecedenteContenuti()
            If risultatiContenutiCorrenti.Count > 0 Then elencoContenuti.selezionaElemento(0)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [UI_RicaricaListaContenutiDopoOrdinamento]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_RicaricaListaPlaylistCanaleDopoOrdinamento()
        Try
            ChiudiProcessoInCorso()
            If elencoPlaylistCanale Is Nothing OrElse elencoPlaylistCanale.IsDisposed Then Return
            If risultatiPlaylistCanale Is Nothing OrElse risultatiPlaylistCanale.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna playlist con questo ordinamento.")
                msg.ShowDialog()
                Return
            End If
            AggiornaListaRisultati(elencoPlaylistCanale, risultatiPlaylistCanale)
            AggiornaOpzionePaginaPrecedentePlaylistCanale()
            If risultatiPlaylistCanale.Count > 0 Then elencoPlaylistCanale.selezionaElemento(0)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [UI_RicaricaListaPlaylistCanaleDopoOrdinamento]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneFiltroDurataRicerca()
        MostraMenuFiltroDurata("ricerca")
    End Sub
    Private Sub OpzioneFiltroDurataContenuti()
        MostraMenuFiltroDurata("contenuti")
    End Sub

    Private Sub MostraMenuFiltroDurata(tipoLista As String)
        Try
            Dim m As New WGMenu()
            Dim tipoCopia = tipoLista
            m.aggiungi("Nessun filtro (tutti)", Sub() ApplicaFiltroDurataLista(tipoCopia, Nothing, Nothing))
            m.aggiungi("Video < 2 minuti", Sub() ApplicaFiltroDurataLista(tipoCopia, Nothing, 120))
            m.aggiungi("Video da 2 a 10 minuti", Sub() ApplicaFiltroDurataLista(tipoCopia, 120, 600))
            m.aggiungi("Video da 10 a 30 minuti", Sub() ApplicaFiltroDurataLista(tipoCopia, 600, 1800))
            m.aggiungi("Video > 10 minuti", Sub() ApplicaFiltroDurataLista(tipoCopia, 600, Nothing))
            m.aggiungi("Video > 30 minuti", Sub() ApplicaFiltroDurataLista(tipoCopia, 1800, Nothing))
            m.aggiungi("Video > 1 ora", Sub() ApplicaFiltroDurataLista(tipoCopia, 3600, Nothing))
            m.avvia("Filtro durata", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MostraMenuFiltroDurata]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ApplicaFiltroDurataLista(tipoLista As String, minS As Long?, maxS As Long?)
        Try
            _contextTipoListaOrdinamento = tipoLista
            _contextMinDurataDaApplicare = minS
            _contextMaxDurataDaApplicare = maxS
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Applicazione filtro durata...")
            AvviaThread(AddressOf ThreadApplicaFiltroDurata)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [ApplicaFiltroDurataLista]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadApplicaFiltroDurata()
        Try
            Dim minS = _contextMinDurataDaApplicare
            Dim maxS = _contextMaxDurataDaApplicare
            Select Case _contextTipoListaOrdinamento
                Case "ricerca"
                    _minDurataRicerca = minS
                    _maxDurataRicerca = maxS
                    Dim risultati = searcher.IniziaRicercaPaginata(SESSION_RICERCA, queryRicerca, DIMENSIONE_PAGINA_RICERCA, _tipoRicercaCorrente, _sortByRicerca, _minDurataRicerca, _maxDurataRicerca, _filtriRicerca)
                    risultatiRicercaCorrente = risultati
                    EseguiSuThreadUI(AddressOf UI_RicaricaListaRicercaDopoOrdinamento)
                Case "contenuti"
                    _minDurataContenuti = minS
                    _maxDurataContenuti = maxS
                    Dim risultati As List(Of YoutubeSearchResult) = CaricaContenutiCorrenti()
                    risultatiContenutiCorrenti = risultati
                    EseguiSuThreadUI(AddressOf UI_RicaricaListaContenutiDopoOrdinamento)
            End Select
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [ThreadApplicaFiltroDurata]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub OpzioneCambiaTipoRicerca()
        Try
            MostraMenuOpzioniRicerca()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [OpzioneCambiaTipoRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Function OttieniItemSelezionatoContenuti() As YoutubeSearchResult
        Try
            If elencoContenuti Is Nothing OrElse elencoContenuti.IsDisposed Then Return Nothing
            Dim ids = elencoContenuti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 Then Return Nothing
            Return TrovaRisultatoPerId(ids(0), risultatiContenutiCorrenti)
        Catch
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneInformazioniContenuti()
        Try
            Dim item = OttieniItemSelezionatoContenuti()
            If item Is Nothing Then Return
            elementoSelezionatoCorrente = item
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {item.Title}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [OpzioneInformazioniContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVisualizzaCommentiContenuti()
        Try
            Dim item = OttieniItemSelezionatoContenuti()
            If item Is Nothing Then Return
            If item.ItemType <> YoutubeItemType.Video Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("I commenti sono disponibili solo per i video.")
                msg.ShowDialog()
                Return
            End If
            elementoSelezionatoCorrente = item
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero commenti: {item.Title}")
            AvviaThread(AddressOf LavoroVisualizzaCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [OpzioneVisualizzaCommentiContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVaiAlCanaleContenuti()
        Try
            Dim item = OttieniItemSelezionatoContenuti()
            If item Is Nothing Then Return
            If String.IsNullOrWhiteSpace(item.ChannelId) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Canale non disponibile per questo elemento.")
                msg.ShowDialog()
                Return
            End If
            Dim canale As New YoutubeSearchResult()
            canale.Id = item.ChannelId
            canale.Title = If(String.IsNullOrWhiteSpace(item.ChannelName), "Canale", item.ChannelName)
            canale.Url = $"https://www.youtube.com/channel/{item.ChannelId}"
            canale.ItemType = YoutubeItemType.Channel
            canale.ChannelId = item.ChannelId
            canale.ChannelName = item.ChannelName
            elementoSelezionatoCorrente = canale
            _gestoreXml.AggiungiRecente(canale.Id, canale.Title, canale.Url, canale.ItemType)
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [OpzioneVaiAlCanaleContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCopiaUrlContenuti()
        Try
            Dim item = OttieniItemSelezionatoContenuti()
            If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Url) Then Return
            Clipboard.SetText(item.Url)
            Dim msg As New WGMessaggi()
            msg.Messaggio("URL copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [OpzioneCopiaUrlContenuti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCercaNelCanale()
        Try
            menuCanale = Nothing
            _contestoRicercaInterna = "canale"
            EseguiSuThreadUI(AddressOf UI_AvviaCampoRicercaInterna)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCercaNelCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCercaNellaPlaylist()
        Try
            menuOpzioniPlaylist = Nothing
            _contestoRicercaInterna = "playlist"
            EseguiSuThreadUI(AddressOf UI_AvviaCampoRicercaInterna)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCercaNellaPlaylist]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_AvviaCampoRicercaInterna()
        Try
            Dim titolo As String = ""
            If elementoSelezionatoCorrente IsNot Nothing Then titolo = elementoSelezionatoCorrente.Title

            campoRicercaInterna = New WGCampoTesto()
            If _contestoRicercaInterna = "playlist" Then
                campoRicercaInterna.init($"Cerca nella playlist {titolo}")
            Else
                campoRicercaInterna.init($"Cerca nel canale {titolo}")
            End If
            AddHandler campoRicercaInterna.chiusura, AddressOf EseguiRicercaInterna
            campoRicercaInterna.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_AvviaCampoRicercaInterna]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub EseguiRicercaInterna()
        Try
            If campoRicercaInterna Is Nothing Then Return

            _queryRicercaInterna = campoRicercaInterna.OttieniTesto()
            campoRicercaInterna.chiudi()
            campoRicercaInterna = Nothing

            If String.IsNullOrWhiteSpace(_queryRicercaInterna) Then Return
            If elementoSelezionatoCorrente Is Nothing Then Return

            _gestoreXml.AggiungiRicercaRecente(_queryRicercaInterna, YoutubeItemType.Video)

            processoincorso = New WGProcessoInCorso()
            If _contestoRicercaInterna = "playlist" Then
                processoincorso.Avvia("Ricerca nella playlist in corso, attendere...")
            Else
                processoincorso.Avvia("Ricerca nel canale in corso, attendere...")
            End If

            AvviaThread(AddressOf ThreadLavoroRicercaInterna)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EseguiRicercaInterna]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroRicercaInterna()
        Try
            _inRicercaInterna = True
            _sortByContenuti = LeggiOrdinamentoPreferito(YoutubeItemType.Video)
            Dim filtro = LeggiFiltroDurataPreferito()
            _minDurataContenuti = filtro.Min
            _maxDurataContenuti = filtro.Max

            Dim risultati As List(Of YoutubeSearchResult)
            If _contestoRicercaInterna = "playlist" Then
                risultati = searcher.IniziaRicercaPlaylistPaginata(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, _queryRicercaInterna, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
            Else
                risultati = searcher.IniziaRicercaCanalePaginata(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, _queryRicercaInterna, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
            End If

            risultatiContenutiCorrenti = risultati
            EseguiSuThreadUI(AddressOf UI_PostRicercaInterna)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroRicercaInterna]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_MessaggioNessunRisultato)
        End Try
    End Sub

    Private Sub UI_PostRicercaInterna()
        Try
            ChiudiProcessoInCorso()

            If risultatiContenutiCorrenti Is Nothing OrElse risultatiContenutiCorrenti.Count = 0 Then
                UI_MessaggioNessunRisultato()
                Return
            End If

            Dim titolo As String = ""
            If elementoSelezionatoCorrente IsNot Nothing Then titolo = elementoSelezionatoCorrente.Title

            elencoContenuti = New WGMenuLista()
            If _contestoRicercaInterna = "playlist" Then
                elencoContenuti.impostaTitolo($"In {titolo}, risultati per: {_queryRicercaInterna}")
            Else
                elencoContenuti.impostaTitolo($"Nel canale {titolo}, risultati per: {_queryRicercaInterna}")
            End If
            PopolaListaContenuti(elencoContenuti, risultatiContenutiCorrenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostRicercaInterna]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraMenuIscrizioni()
        ' Con l'accesso a YouTube nel browser si chiede se usare le iscrizioni online (MenuYoutube.Online.vb).
        ApriIscrizioniScegliendoFonte()
    End Sub

    Private Sub UI_MostraMenuIscrizioni()
        Try
            AssicuraInizializzazione()

            Dim nonVisti As Long = _gestoreXml.ContaVideoNuovi(Nothing, True)
            Dim quanteIscrizioni As Long = _gestoreXml.ContaIscrizioni(False)

            Dim m As New WGMenu()
            m.aggiungi($"Canali con nuovi video ({_gestoreXml.OttieniCanaliConNuoviVideo().Count})", AddressOf ClickCanaliConNuoviVideo)
            m.aggiungi($"Tutti i nuovi video ({nonVisti})", AddressOf ClickTuttiINuoviVideo)
            m.aggiungi($"Canali a cui sei iscritto ({quanteIscrizioni})", AddressOf ClickElencoIscrizioni)
            m.aggiungi("Controlla ora se ci sono nuovi video", AddressOf ClickControllaNuoviVideoAdesso)
            m.aggiungi("Segna tutti i video come gia' visti", AddressOf ClickSegnaTuttiVistiDaMenu)
            m.avvia("Iscrizioni", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraMenuIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ClickCanaliConNuoviVideo()
        MostraCanaliConNuoviVideo()
    End Sub

    Private Sub ClickTuttiINuoviVideo()
        MostraElencoVideoNuovi(Nothing, "Nuovi video da vedere")
    End Sub

    Private Sub ClickElencoIscrizioni()
        MostraElencoIscrizioni()
    End Sub

    Private Sub ClickSegnaTuttiVistiDaMenu()
        Try
            _gestoreXml.SegnaTuttiVideoVisti()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tutti i nuovi video sono stati segnati come gia' visti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickSegnaTuttiVistiDaMenu]: {ex.ToString()}")
        End Try
    End Sub

    ' Segue il canale su questo computer (nel menu del canale c'è OpzioneIscrivitiAlCanale, in MenuYoutube.Online.vb,
    ' che prima chiede a YouTube se serve).
    Private Sub IscrivitiSoloSulComputer()
        Try
            If elementoSelezionatoCorrente Is Nothing Then Return
            menuCanale = Nothing
            _idCanaleContestoIscrizione = elementoSelezionatoCorrente.Id
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Iscrizione al canale in corso...")
            AvviaThread(AddressOf ThreadLavoroIscrizioneCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneIscrivitiAlCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroIscrizioneCanale()
        Try
            If elementoSelezionatoCorrente Is Nothing Then Return

            Dim url As String = elementoSelezionatoCorrente.Url
            If String.IsNullOrWhiteSpace(url) Then url = "https://www.youtube.com/channel/" & elementoSelezionatoCorrente.Id

            _gestoreXml.AggiungiIscrizione(elementoSelezionatoCorrente.Id, elementoSelezionatoCorrente.Title, url)

            Dim video As List(Of YoutubeSearchResult) = searcher.OttieniUltimiVideoCanale(elementoSelezionatoCorrente.Id, LeggiVideoDaControllarePerCanale())
            If video IsNot Nothing AndAlso video.Count > 0 Then
                Dim identificativi As New List(Of String)
                For Each elemento As YoutubeSearchResult In video
                    Try
                        If elemento Is Nothing OrElse String.IsNullOrWhiteSpace(elemento.Id) Then Continue For
                        identificativi.Add(elemento.Id)
                    Catch exVoce As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroIscrizioneCanale.Loop]: {exVoce.Message}")
                    End Try
                Next
                _gestoreXml.RegistraVideoNotiIniziali(elementoSelezionatoCorrente.Id, identificativi)
            End If

            _gestoreXml.AggiornaUltimoControlloIscrizione(elementoSelezionatoCorrente.Id)
            EseguiSuThreadUI(AddressOf UI_PostIscrizioneCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroIscrizioneCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostIscrizioneCanale()
        Try
            ChiudiProcessoInCorso()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Iscrizione effettuata. Verranno segnalati solo i video pubblicati da adesso in poi.")
            msg.ShowDialog()
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostIscrizioneCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AnnullaIscrizioneSoloSulComputer()
        Try
            If elementoSelezionatoCorrente Is Nothing Then Return
            _gestoreXml.RimuoviIscrizione(elementoSelezionatoCorrente.Id)
            menuCanale = Nothing
            Dim msg As New WGMessaggi()
            msg.Messaggio("Iscrizione annullata.")
            msg.ShowDialog()
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAnnullaIscrizioneCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneNuoviVideoDelCanale()
        Try
            If elementoSelezionatoCorrente Is Nothing Then Return
            menuCanale = Nothing
            MostraElencoVideoNuovi(elementoSelezionatoCorrente.Id, $"Nuovi video di: {elementoSelezionatoCorrente.Title}")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneNuoviVideoDelCanale]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraElencoIscrizioni()
        EseguiSuThreadUI(AddressOf UI_MostraElencoIscrizioni)
    End Sub

    Private Sub UI_MostraElencoIscrizioni()
        Try
            AssicuraInizializzazione()
            _iscrizioniCorrenti = _gestoreXml.OttieniIscrizioni(False)

            If _iscrizioniCorrenti Is Nothing OrElse _iscrizioniCorrenti.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sei iscritto a nessun canale. Apri un canale e scegli Iscriviti al canale.")
                msg.ShowDialog()
                Return
            End If

            elencoIscrizioni = New WGMenuLista()
            elencoIscrizioni.ciclono = True
            elencoIscrizioni.impostaTitolo("Canali a cui sei iscritto")
            elencoIscrizioni.aggiungiOpzione("Apri canale", AddressOf OpzioneApriCanaleIscrizione)
            elencoIscrizioni.aggiungiOpzione("Nuovi video del canale", AddressOf OpzioneNuoviVideoIscrizione)
            elencoIscrizioni.aggiungiOpzione("Controlla ora questo canale", AddressOf OpzioneControllaCanaleIscrizione)
            elencoIscrizioni.aggiungiOpzione("Annulla iscrizione", AddressOf OpzioneRimuoviIscrizioneDaElenco)
            elencoIscrizioni.aggiungiOpzione("Sospendi o riattiva controllo", AddressOf OpzioneCommutaIscrizione)

            PopolaElencoIscrizioni()

            RemoveHandler elencoIscrizioni.ElementoSelezionato, AddressOf OpzioneApriCanaleIscrizione
            AddHandler elencoIscrizioni.ElementoSelezionato, AddressOf OpzioneApriCanaleIscrizione
            elencoIscrizioni.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraElencoIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaElencoIscrizioni()
        Try
            If elencoIscrizioni Is Nothing OrElse elencoIscrizioni.IsDisposed Then Return
            If _iscrizioniCorrenti Is Nothing Then Return

            For Each iscrizione As IscrizioneCanale In _iscrizioniCorrenti
                Try
                    Dim descrizione As String = iscrizione.Titolo
                    If iscrizione.NuoviNonVisti > 0 Then
                        descrizione &= $" ({iscrizione.NuoviNonVisti} nuovi)"
                    End If
                    If Not iscrizione.Attiva Then
                        descrizione &= " [controllo sospeso]"
                    End If
                    elencoIscrizioni.aggiungiElemento(descrizione, iscrizione.IdCanale)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElencoIscrizioni.Loop]: {exVoce.Message}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaElencoIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Function TrovaIscrizioneSelezionata() As IscrizioneCanale
        Try
            If elencoIscrizioni Is Nothing OrElse elencoIscrizioni.IsDisposed Then Return Nothing
            If _iscrizioniCorrenti Is Nothing Then Return Nothing

            Dim identificativi = elencoIscrizioni.ottieniElementi()
            If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return Nothing

            For Each iscrizione As IscrizioneCanale In _iscrizioniCorrenti
                If String.Equals(iscrizione.IdCanale, identificativi(0), StringComparison.Ordinal) Then Return iscrizione
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TrovaIscrizioneSelezionata]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    ''' <summary>OpzioneApriCanaleIscrizione con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub OpzioneApriCanaleIscrizione(idEvento As String)
        OpzioneApriCanaleIscrizione()
    End Sub

    Private Sub OpzioneApriCanaleIscrizione()
        Try
            Dim iscrizione = TrovaIscrizioneSelezionata()
            If iscrizione Is Nothing Then Return

            Dim canale As New YoutubeSearchResult()
            canale.Id = iscrizione.IdCanale
            canale.Title = iscrizione.Titolo
            canale.Url = iscrizione.Url
            canale.ItemType = YoutubeItemType.Channel
            canale.ChannelId = iscrizione.IdCanale
            canale.ChannelName = iscrizione.Titolo

            elementoSelezionatoCorrente = canale
            _gestoreXml.AggiungiRecente(canale.Id, canale.Title, canale.Url, canale.ItemType)
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneApriCanaleIscrizione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneNuoviVideoIscrizione()
        Try
            Dim iscrizione = TrovaIscrizioneSelezionata()
            If iscrizione Is Nothing Then Return
            MostraElencoVideoNuovi(iscrizione.IdCanale, $"Nuovi video di: {iscrizione.Titolo}")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneNuoviVideoIscrizione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRimuoviIscrizioneDaElenco()
        Try
            Dim iscrizione = TrovaIscrizioneSelezionata()
            If iscrizione Is Nothing Then Return

            _gestoreXml.RimuoviIscrizione(iscrizione.IdCanale)
            _iscrizioniCorrenti = _gestoreXml.OttieniIscrizioni(False)
            elencoIscrizioni.pulisci()
            PopolaElencoIscrizioni()

            Dim msg As New WGMessaggi()
            msg.Messaggio("Iscrizione annullata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRimuoviIscrizioneDaElenco]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCommutaIscrizione()
        Try
            Dim iscrizione = TrovaIscrizioneSelezionata()
            If iscrizione Is Nothing Then Return

            _gestoreXml.ImpostaIscrizioneAttiva(iscrizione.IdCanale, Not iscrizione.Attiva)
            _iscrizioniCorrenti = _gestoreXml.OttieniIscrizioni(False)
            elencoIscrizioni.pulisci()
            PopolaElencoIscrizioni()

            Dim msg As New WGMessaggi()
            If iscrizione.Attiva Then
                msg.Messaggio("Controllo sospeso per questo canale.")
            Else
                msg.Messaggio("Controllo riattivato per questo canale.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCommutaIscrizione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneControllaCanaleIscrizione()
        Try
            Dim iscrizione = TrovaIscrizioneSelezionata()
            If iscrizione Is Nothing Then Return

            _idCanaleContestoIscrizione = iscrizione.IdCanale
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Controllo nuovi video di {iscrizione.Titolo}...")
            AvviaThread(AddressOf ThreadLavoroControlloSingoloCanale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneControllaCanaleIscrizione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroControlloSingoloCanale()
        Try
            _nuoviVideoTrovatiControllo = ControllaCanale(_gestoreXml, searcher, _idCanaleContestoIscrizione, LeggiVideoDaControllarePerCanale())
            EseguiSuThreadUI(AddressOf UI_PostControlloNuoviVideo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroControlloSingoloCanale]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub ClickControllaNuoviVideoAdesso()
        Try
            AssicuraInizializzazione()

            ' Online le iscrizioni si leggono dall'account, quindi l'archivio può essere ancora vuoto.
            If Not FonteIscrizioniOnline() AndAlso _gestoreXml.ContaIscrizioni(True) = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sei iscritto a nessun canale attivo.")
                msg.ShowDialog()
                Return
            End If

            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Controllo nuovi video nei canali a cui sei iscritto...")
            AvviaThread(AddressOf ThreadLavoroControlloNuoviVideo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ClickControllaNuoviVideoAdesso]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadLavoroControlloNuoviVideo()
        Try
            _nuoviVideoTrovatiControllo = ControllaIscrizioniSecondoFonte(_gestoreXml, searcher, LeggiVideoDaControllarePerCanale())
            EseguiSuThreadUI(AddressOf UI_PostControlloNuoviVideo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadLavoroControlloNuoviVideo]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_PostControlloNuoviVideo()
        Try
            ChiudiProcessoInCorso()

            If _nuoviVideoTrovatiControllo <= 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun nuovo video trovato.")
                msg.ShowDialog()
                Return
            End If

            Dim avviso As New WGMessaggi()
            If _nuoviVideoTrovatiControllo = 1 Then
                avviso.Messaggio("E' stato trovato un nuovo video.")
            Else
                avviso.Messaggio($"Sono stati trovati {_nuoviVideoTrovatiControllo} nuovi video.")
            End If
            avviso.ShowDialog()

            MostraElencoVideoNuovi(Nothing, "Nuovi video da vedere")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostControlloNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

    Public Shared Function ControllaTutteLeIscrizioni(archivio As YoutubeDb, client As YtdlpClient, quantiPerCanale As Integer) As Integer
        Dim totale As Integer = 0
        Try
            If archivio Is Nothing OrElse client Is Nothing Then Return 0
            If quantiPerCanale <= 0 Then quantiPerCanale = VIDEO_CONTROLLO_PREDEFINITO

            Dim iscrizioni As List(Of IscrizioneCanale) = archivio.OttieniIscrizioni(True)
            If iscrizioni Is Nothing OrElse iscrizioni.Count = 0 Then Return 0

            ' I feed RSS di tutti i canali si leggono insieme, otto alla volta: è la parte lenta, il resto è l'archivio.
            Dim precaricati As Dictionary(Of String, List(Of YoutubeSearchResult)) = YoutubeRss.LeggiPiuCanali(iscrizioni.Select(Function(i) i.IdCanale))

            For Each iscrizione As IscrizioneCanale In iscrizioni
                Try
                    totale += ControllaCanale(archivio, client, iscrizione.IdCanale, quantiPerCanale, precaricati)
                Catch exCanale As Exception
                    loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaTutteLeIscrizioni.Canale]: {exCanale.Message}")
                End Try
            Next

            Try
                archivio.EliminaVideoNuoviVistiVecchi(30)
            Catch exPulizia As Exception
                loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaTutteLeIscrizioni.Pulizia]: {exPulizia.Message}")
            End Try
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaTutteLeIscrizioni]: {ex.ToString()}")
        End Try
        Return totale
    End Function

    ' Gli ultimi video del canale: dal feed RSS (veloce, con la data esatta), e se non risponde da yt-dlp.
    ' "precaricati" ha i feed già letti per tanti canali insieme; senza, il feed di questo canale si legge adesso.
    Private Shared Function UltimiVideoDelCanale(client As YtdlpClient, idCanale As String, quanti As Integer, precaricati As Dictionary(Of String, List(Of YoutubeSearchResult))) As List(Of YoutubeSearchResult)
        Try
            If YoutubeRss.IdValido(idCanale) Then
                Dim dalFeed As List(Of YoutubeSearchResult) = Nothing
                If precaricati Is Nothing OrElse Not precaricati.TryGetValue(idCanale, dalFeed) Then dalFeed = YoutubeRss.Leggi(idCanale)
                If dalFeed IsNot Nothing Then Return dalFeed
            End If
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.UltimiVideoDelCanale RSS idCanale={idCanale}]: {ex.Message}")
        End Try
        Return client.OttieniUltimiVideoCanale(idCanale, quanti)
    End Function

    Public Shared Function ControllaCanale(archivio As YoutubeDb, client As YtdlpClient, idCanale As String, quantiPerCanale As Integer, Optional precaricati As Dictionary(Of String, List(Of YoutubeSearchResult)) = Nothing) As Integer
        Dim trovati As Integer = 0
        Try
            If archivio Is Nothing OrElse client Is Nothing Then Return 0
            If String.IsNullOrWhiteSpace(idCanale) Then Return 0
            If quantiPerCanale <= 0 Then quantiPerCanale = VIDEO_CONTROLLO_PREDEFINITO

            Dim iscrizione As IscrizioneCanale = archivio.OttieniIscrizione(idCanale)
            If iscrizione Is Nothing Then Return 0

            Dim video As List(Of YoutubeSearchResult) = UltimiVideoDelCanale(client, idCanale, quantiPerCanale, precaricati)
            If video Is Nothing OrElse video.Count = 0 Then
                archivio.AggiornaUltimoControlloIscrizione(idCanale)
                Return 0
            End If

            For Each elemento As YoutubeSearchResult In video
                Try
                    If elemento Is Nothing OrElse String.IsNullOrWhiteSpace(elemento.Id) Then Continue For
                    If Not client.TitoloAmmesso(elemento.Title) Then Continue For
                    If elemento.IsShort.GetValueOrDefault() AndAlso IgnoraShortNeiNuoviVideo() Then Continue For
                    If archivio.EVideoNoto(idCanale, elemento.Id) Then Continue For

                    Dim dataPubblicazione As Date = Date.MinValue
                    If elemento.UploadDate.HasValue Then dataPubblicazione = elemento.UploadDate.Value

                    If dataPubblicazione <> Date.MinValue AndAlso iscrizione.DataIscrizione <> Date.MinValue Then
                        If dataPubblicazione < iscrizione.DataIscrizione Then
                            Dim precedenti As New List(Of String)
                            precedenti.Add(elemento.Id)
                            archivio.RegistraVideoNotiIniziali(idCanale, precedenti)
                            Continue For
                        End If
                    End If

                    Dim nomeCanale As String = elemento.ChannelName
                    If String.IsNullOrWhiteSpace(nomeCanale) Then nomeCanale = iscrizione.Titolo

                    If archivio.RegistraVideoRilevato(idCanale, nomeCanale, elemento.Id, elemento.Title, elemento.Url, elemento.DurationText, dataPubblicazione) Then
                        trovati += 1
                    End If
                Catch exVoce As Exception
                    loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaCanale.Voce]: {exVoce.Message}")
                End Try
            Next

            archivio.AggiornaUltimoControlloIscrizione(idCanale)
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaCanale idCanale={idCanale}]: {ex.ToString()}")
        End Try
        Return trovati
    End Function

    Public Shared Function EseguiControlloAutomatico() As Integer
        Dim trovati As Integer = 0
        Dim archivio As YoutubeDb = Nothing
        Try
            archivio = New YoutubeDb()
            Dim client As New YtdlpClient()
            trovati = ControllaIscrizioniSecondoFonte(archivio, client, LeggiVideoDaControllarePerCanale())
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.EseguiControlloAutomatico]: {ex.ToString()}")
        Finally
            Try
                If archivio IsNot Nothing Then archivio.Dispose()
            Catch exChiusura As Exception
                loggerStatico.ScriviLog($"ERRORE [MenuYoutube.EseguiControlloAutomatico.Chiusura]: {exChiusura.Message}")
            End Try
        End Try
        Return trovati
    End Function

    Public Shared Function ContaNuoviVideoNonVisti() As Long
        Dim quanti As Long = 0
        Dim archivio As YoutubeDb = Nothing
        Try
            archivio = New YoutubeDb()
            quanti = archivio.ContaVideoNuovi(Nothing, True)
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ContaNuoviVideoNonVisti]: {ex.ToString()}")
        Finally
            Try
                If archivio IsNot Nothing Then archivio.Dispose()
            Catch exChiusura As Exception
                loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ContaNuoviVideoNonVisti.Chiusura]: {exChiusura.Message}")
            End Try
        End Try
        Return quanti
    End Function

    Private Sub MostraCanaliConNuoviVideo()
        EseguiSuThreadUI(AddressOf UI_MostraCanaliConNuoviVideo)
    End Sub

    Private Sub UI_MostraCanaliConNuoviVideo()
        Try
            AssicuraInizializzazione()

            _canaliConNuoviVideo = _gestoreXml.OttieniCanaliConNuoviVideo()
            _videoNuoviCache = _gestoreXml.OttieniVideoNuovi(Nothing, True, 0, LIMITE_CARICAMENTO_NUOVI)

            If _canaliConNuoviVideo Is Nothing OrElse _canaliConNuoviVideo.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun canale ha nuovi video da vedere.")
                msg.ShowDialog()
                Return
            End If

            elencoCanaliNuovi = New WGMenuLista()
            elencoCanaliNuovi.ciclono = True
            elencoCanaliNuovi.impostaTitolo("Canali con nuovi video")
            elencoCanaliNuovi.aggiungiOpzione("Vedi i nuovi video del canale", AddressOf OpzioneVediNuoviVideoCanaleSelezionato)
            elencoCanaliNuovi.aggiungiOpzione("Apri canale", AddressOf OpzioneApriCanaleDaNuovi)

            For Each canale As CanaleConNuoviVideo In _canaliConNuoviVideo
                Try
                    elencoCanaliNuovi.aggiungiElemento($"{canale.Titolo} ({canale.Quantita} nuovi)", canale.IdCanale)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraCanaliConNuoviVideo.Loop]: {exVoce.Message}")
                End Try
            Next

            RemoveHandler elencoCanaliNuovi.ElementoSelezionato, AddressOf OpzioneVediNuoviVideoCanaleSelezionato
            AddHandler elencoCanaliNuovi.ElementoSelezionato, AddressOf OpzioneVediNuoviVideoCanaleSelezionato
            elencoCanaliNuovi.avvia()

            _gestoreXml.SegnaTuttiVideoVisti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraCanaliConNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Function TrovaCanaleNuoviSelezionato() As CanaleConNuoviVideo
        Try
            If elencoCanaliNuovi Is Nothing OrElse elencoCanaliNuovi.IsDisposed Then Return Nothing
            If _canaliConNuoviVideo Is Nothing Then Return Nothing

            Dim identificativi = elencoCanaliNuovi.ottieniElementi()
            If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return Nothing

            For Each canale As CanaleConNuoviVideo In _canaliConNuoviVideo
                If String.Equals(canale.IdCanale, identificativi(0), StringComparison.Ordinal) Then Return canale
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TrovaCanaleNuoviSelezionato]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    ''' <summary>OpzioneVediNuoviVideoCanaleSelezionato con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub OpzioneVediNuoviVideoCanaleSelezionato(idEvento As String)
        OpzioneVediNuoviVideoCanaleSelezionato()
    End Sub

    Private Sub OpzioneVediNuoviVideoCanaleSelezionato()
        Try
            Dim canale = TrovaCanaleNuoviSelezionato()
            If canale Is Nothing Then Return
            MostraElencoVideoNuoviDaMemoria(canale.IdCanale, $"Nuovi video di: {canale.Titolo}")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneVediNuoviVideoCanaleSelezionato]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneApriCanaleDaNuovi()
        Try
            Dim canale = TrovaCanaleNuoviSelezionato()
            If canale Is Nothing Then Return

            Dim elemento As New YoutubeSearchResult()
            elemento.Id = canale.IdCanale
            elemento.Title = canale.Titolo
            elemento.Url = "https://www.youtube.com/channel/" & canale.IdCanale
            elemento.ItemType = YoutubeItemType.Channel
            elemento.ChannelId = canale.IdCanale
            elemento.ChannelName = canale.Titolo

            elementoSelezionatoCorrente = elemento
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneApriCanaleDaNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraElencoVideoNuovi(idCanale As String, titolo As String)
        Try
            AssicuraInizializzazione()
            _idCanaleContestoIscrizione = idCanale
            _titoloElencoVideoNuovi = titolo
            _videoNuoviTutti = _gestoreXml.OttieniVideoNuovi(idCanale, True, 0, LIMITE_CARICAMENTO_NUOVI)
            _offsetVideoNuovi = 0
            EseguiSuThreadUI(AddressOf UI_MostraElencoVideoNuovi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraElencoVideoNuoviDaMemoria(idCanale As String, titolo As String)
        Try
            AssicuraInizializzazione()
            _idCanaleContestoIscrizione = idCanale
            _titoloElencoVideoNuovi = titolo

            Dim filtrati As New List(Of VideoNuovo)
            If _videoNuoviCache IsNot Nothing Then
                For Each video As VideoNuovo In _videoNuoviCache
                    Try
                        If video Is Nothing Then Continue For
                        If String.IsNullOrWhiteSpace(idCanale) OrElse String.Equals(video.IdCanale, idCanale, StringComparison.Ordinal) Then
                            filtrati.Add(video)
                        End If
                    Catch exVoce As Exception
                        logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoVideoNuoviDaMemoria.Loop]: {exVoce.Message}")
                    End Try
                Next
            End If

            If filtrati.Count = 0 Then
                filtrati = _gestoreXml.OttieniVideoNuovi(idCanale, False, 0, LIMITE_CARICAMENTO_NUOVI)
            End If

            _videoNuoviTutti = filtrati
            _offsetVideoNuovi = 0
            EseguiSuThreadUI(AddressOf UI_MostraElencoVideoNuovi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraElencoVideoNuoviDaMemoria]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MostraElencoVideoNuovi()
        Try
            If _videoNuoviTutti Is Nothing OrElse _videoNuoviTutti.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ci sono nuovi video da vedere.")
                msg.ShowDialog()
                Return
            End If

            elencoVideoNuovi = New WGMenuLista()
            elencoVideoNuovi.ciclono = True
            elencoVideoNuovi.multiselezioneConsentita = True
            elencoVideoNuovi.impostaTitolo(_titoloElencoVideoNuovi)

            elencoVideoNuovi.aggiungiOpzione("Riproduci", AddressOf OpzioneRiproduciVideoNuovo)
            elencoVideoNuovi.aggiungiOpzione("Riproduci tutti", AddressOf OpzioneRiproduciTuttiVideoNuovi)
            elencoVideoNuovi.aggiungiOpzione("Informazioni", AddressOf OpzioneInformazioniVideoNuovo)
            elencoVideoNuovi.aggiungiOpzione("Vai al canale", AddressOf OpzioneVaiAlCanaleVideoNuovo)
            elencoVideoNuovi.aggiungiOpzione("Copia URL", AddressOf OpzioneCopiaUrlVideoNuovo)
            If RiassuntoVideoDisponibile() Then elencoVideoNuovi.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiVideoNuovi)
            elencoVideoNuovi.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoVideoNuovo)

            PopolaPaginaVideoNuovi()
            AggiornaOpzioniPaginazioneVideoNuovi()

            RemoveHandler elencoVideoNuovi.ElementoSelezionato, AddressOf OpzioneRiproduciVideoNuovo
            AddHandler elencoVideoNuovi.ElementoSelezionato, AddressOf OpzioneRiproduciVideoNuovo
            RemoveHandler elencoVideoNuovi.ElementiSelezionati, AddressOf HandlerElementiSelezionatiVideoNuovi
            AddHandler elencoVideoNuovi.ElementiSelezionati, AddressOf HandlerElementiSelezionatiVideoNuovi
            RemoveHandler elencoVideoNuovi.iniziolista, AddressOf HandlerInizioListaVideoNuovi
            AddHandler elencoVideoNuovi.iniziolista, AddressOf HandlerInizioListaVideoNuovi
            RemoveHandler elencoVideoNuovi.finelista, AddressOf HandlerFineListaVideoNuovi
            AddHandler elencoVideoNuovi.finelista, AddressOf HandlerFineListaVideoNuovi

            elencoVideoNuovi.avvia()

            SegnaVideoElencoComeVisti()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MostraElencoVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SegnaVideoElencoComeVisti()
        Try
            If _videoNuoviTutti Is Nothing OrElse _videoNuoviTutti.Count = 0 Then Return

            Dim identificativi As New List(Of String)
            For Each video As VideoNuovo In _videoNuoviTutti
                Try
                    If video Is Nothing OrElse String.IsNullOrWhiteSpace(video.IdVideo) Then Continue For
                    identificativi.Add(video.IdVideo)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.SegnaVideoElencoComeVisti.Loop]: {exVoce.Message}")
                End Try
            Next

            _gestoreXml.SegnaVideoVisti(identificativi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SegnaVideoElencoComeVisti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PopolaPaginaVideoNuovi()
        Try
            If elencoVideoNuovi Is Nothing OrElse elencoVideoNuovi.IsDisposed Then Return
            If _videoNuoviTutti Is Nothing Then Return

            Dim inizio As Integer = _offsetVideoNuovi
            If inizio < 0 Then inizio = 0
            If inizio >= _videoNuoviTutti.Count Then inizio = 0

            Dim quanti As Integer = Math.Min(DIMENSIONE_PAGINA_NUOVI, _videoNuoviTutti.Count - inizio)
            _videoNuoviCorrenti = _videoNuoviTutti.GetRange(inizio, quanti)

            For Each video As VideoNuovo In _videoNuoviCorrenti
                Try
                    If searcher IsNot Nothing AndAlso Not searcher.TitoloAmmesso(video.Titolo) Then Continue For

                    Dim descrizione As String = video.Titolo
                    If Not String.IsNullOrWhiteSpace(video.NomeCanale) Then
                        descrizione = $"{video.Titolo} - {video.NomeCanale}"
                    End If
                    If video.DataPubblicazione <> Date.MinValue Then
                        descrizione &= $" ({video.DataPubblicazione.ToString("dd/MM/yyyy")})"
                    End If
                    elencoVideoNuovi.aggiungiElemento(descrizione, video.IdVideo)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.PopolaPaginaVideoNuovi.Loop]: {exVoce.Message}")
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PopolaPaginaVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AggiornaOpzioniPaginazioneVideoNuovi()
        Try
            If elencoVideoNuovi Is Nothing OrElse elencoVideoNuovi.IsDisposed Then Return
            If _videoNuoviTutti Is Nothing Then Return

            elencoVideoNuovi.rimuoviOpzione("Pagina precedente")
            elencoVideoNuovi.rimuoviOpzione("Pagina successiva")

            If _offsetVideoNuovi > 0 Then
                elencoVideoNuovi.aggiungiOpzione("Pagina precedente", AddressOf OpzionePaginaPrecedenteVideoNuovi)
            End If
            If (_offsetVideoNuovi + DIMENSIONE_PAGINA_NUOVI) < _videoNuoviTutti.Count Then
                elencoVideoNuovi.aggiungiOpzione("Pagina successiva", AddressOf OpzionePaginaSuccessivaVideoNuovi)
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AggiornaOpzioniPaginazioneVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaSuccessivaVideoNuovi()
        Try
            If _videoNuoviTutti Is Nothing Then Return
            If (_offsetVideoNuovi + DIMENSIONE_PAGINA_NUOVI) >= _videoNuoviTutti.Count Then Return

            _offsetVideoNuovi += DIMENSIONE_PAGINA_NUOVI
            elencoVideoNuovi.pulisci()
            PopolaPaginaVideoNuovi()
            AggiornaOpzioniPaginazioneVideoNuovi()
            If _videoNuoviCorrenti IsNot Nothing AndAlso _videoNuoviCorrenti.Count > 0 Then elencoVideoNuovi.selezionaElemento(0)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaSuccessivaVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzionePaginaPrecedenteVideoNuovi()
        Try
            If _offsetVideoNuovi <= 0 Then Return

            _offsetVideoNuovi = Math.Max(0, _offsetVideoNuovi - DIMENSIONE_PAGINA_NUOVI)
            elencoVideoNuovi.pulisci()
            PopolaPaginaVideoNuovi()
            AggiornaOpzioniPaginazioneVideoNuovi()
            elencoVideoNuovi.SelezionaUltimoElemento()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzionePaginaPrecedenteVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerInizioListaVideoNuovi(sender As Object, e As EventArgs)
        Try
            OpzionePaginaPrecedenteVideoNuovi()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerInizioListaVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerFineListaVideoNuovi(sender As Object, e As EventArgs)
        Try
            OpzionePaginaSuccessivaVideoNuovi()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerFineListaVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Function TrovaVideoNuovoPerId(identificativo As String) As VideoNuovo
        Try
            If String.IsNullOrWhiteSpace(identificativo) Then Return Nothing
            If _videoNuoviTutti Is Nothing Then Return Nothing

            For Each video As VideoNuovo In _videoNuoviTutti
                If video IsNot Nothing AndAlso String.Equals(video.IdVideo, identificativo, StringComparison.Ordinal) Then Return video
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TrovaVideoNuovoPerId]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Function TrovaVideoNuovoSelezionato() As VideoNuovo
        Try
            If elencoVideoNuovi Is Nothing OrElse elencoVideoNuovi.IsDisposed Then Return Nothing
            Dim identificativi = elencoVideoNuovi.ottieniElementi()
            If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return Nothing
            Return TrovaVideoNuovoPerId(identificativi(0))
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TrovaVideoNuovoSelezionato]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Function ConvertiVideoNuovoInSearchResult(video As VideoNuovo) As YoutubeSearchResult
        Try
            If video Is Nothing Then Return Nothing

            Dim elemento As New YoutubeSearchResult()
            elemento.Id = video.IdVideo
            elemento.Title = video.Titolo
            elemento.Url = video.Url
            elemento.ItemType = YoutubeItemType.Video
            elemento.ChannelId = video.IdCanale
            elemento.ChannelName = video.NomeCanale
            elemento.DurationText = video.Durata
            If video.DataPubblicazione <> Date.MinValue Then elemento.UploadDate = video.DataPubblicazione
            Return elemento
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ConvertiVideoNuovoInSearchResult]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    ''' <summary>OpzioneRiproduciVideoNuovo con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub OpzioneRiproduciVideoNuovo(idEvento As String)
        OpzioneRiproduciVideoNuovo()
    End Sub

    Private Sub OpzioneRiproduciVideoNuovo()
        Try
            Dim video = TrovaVideoNuovoSelezionato()
            If video Is Nothing Then Return

            _gestoreXml.AggiungiRecente(video.IdVideo, video.Titolo, video.Url, YoutubeItemType.Video)

            Dim indirizzi As New List(Of String)
            indirizzi.Add(video.Url)
            AvviaPlayer(video.Titolo, indirizzi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiproduciVideoNuovo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneRiproduciTuttiVideoNuovi()
        Try
            If _videoNuoviTutti Is Nothing OrElse _videoNuoviTutti.Count = 0 Then Return

            Dim indirizzi As New List(Of String)
            For Each video As VideoNuovo In _videoNuoviTutti
                Try
                    If video Is Nothing OrElse String.IsNullOrWhiteSpace(video.Url) Then Continue For
                    indirizzi.Add(video.Url)
                    _gestoreXml.AggiungiRecente(video.IdVideo, video.Titolo, video.Url, YoutubeItemType.Video)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiproduciTuttiVideoNuovi.Loop]: {exVoce.Message}")
                End Try
            Next

            If indirizzi.Count = 0 Then Return
            AvviaPlayer(_titoloElencoVideoNuovi, indirizzi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneRiproduciTuttiVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub HandlerElementiSelezionatiVideoNuovi(identificativi As List(Of String))
        Try
            If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return

            Dim indirizzi As New List(Of String)
            For Each identificativo As String In identificativi
                Try
                    Dim video = TrovaVideoNuovoPerId(identificativo)
                    If video Is Nothing OrElse String.IsNullOrWhiteSpace(video.Url) Then Continue For
                    indirizzi.Add(video.Url)
                    _gestoreXml.AggiungiRecente(video.IdVideo, video.Titolo, video.Url, YoutubeItemType.Video)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementiSelezionatiVideoNuovi.Loop]: {exVoce.Message}")
                End Try
            Next

            If indirizzi.Count = 0 Then Return
            AvviaPlayer("Selezione dai nuovi video", indirizzi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.HandlerElementiSelezionatiVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneInformazioniVideoNuovo()
        Try
            Dim video = TrovaVideoNuovoSelezionato()
            If video Is Nothing Then Return

            elementoSelezionatoCorrente = ConvertiVideoNuovoInSearchResult(video)
            If elementoSelezionatoCorrente Is Nothing Then Return

            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {video.Titolo}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneInformazioniVideoNuovo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneVaiAlCanaleVideoNuovo()
        Try
            Dim video = TrovaVideoNuovoSelezionato()
            If video Is Nothing OrElse String.IsNullOrWhiteSpace(video.IdCanale) Then Return

            Dim canale As New YoutubeSearchResult()
            canale.Id = video.IdCanale
            canale.Title = If(String.IsNullOrWhiteSpace(video.NomeCanale), "Canale", video.NomeCanale)
            canale.Url = "https://www.youtube.com/channel/" & video.IdCanale
            canale.ItemType = YoutubeItemType.Channel
            canale.ChannelId = video.IdCanale
            canale.ChannelName = video.NomeCanale

            elementoSelezionatoCorrente = canale
            _gestoreXml.AggiungiRecente(canale.Id, canale.Title, canale.Url, canale.ItemType)
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneVaiAlCanaleVideoNuovo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneCopiaUrlVideoNuovo()
        Try
            Dim video = TrovaVideoNuovoSelezionato()
            If video Is Nothing OrElse String.IsNullOrWhiteSpace(video.Url) Then Return

            Clipboard.SetText(video.Url)
            Dim msg As New WGMessaggi()
            msg.Messaggio("URL copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCopiaUrlVideoNuovo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneAggiungiPreferitoVideoNuovo()
        Try
            Dim video = TrovaVideoNuovoSelezionato()
            If video Is Nothing Then Return

            Dim elemento = ConvertiVideoNuovoInSearchResult(video)
            If elemento Is Nothing Then Return

            If _gestoreXml.HaPiuCategorie() Then
                _elementoDaAggiungereTemp = elemento
                MostraSceltaCategoriaPerAggiunta()
            Else
                _gestoreXml.AggiungiPreferito(elemento.Id, elemento.Title, elemento.Url, YoutubeItemType.Video)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Video aggiunto ai preferiti.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAggiungiPreferitoVideoNuovo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoIscrizioni() Handles elencoIscrizioni.escPremuto
        Try
            If elencoIscrizioni IsNot Nothing Then
                elencoIscrizioni.abbandona()
                elencoIscrizioni = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoCanaliNuovi() Handles elencoCanaliNuovi.escPremuto
        Try
            If elencoCanaliNuovi IsNot Nothing Then
                elencoCanaliNuovi.abbandona()
                elencoCanaliNuovi = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoCanaliNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiElencoVideoNuovi() Handles elencoVideoNuovi.escPremuto
        Try
            If elencoVideoNuovi IsNot Nothing Then
                elencoVideoNuovi.abbandona()
                elencoVideoNuovi = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.chiudiElencoVideoNuovi]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuGruppoIscrizioni()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Iscrizioni online o offline", AddressOf UI_MenuFonteIscrizioni)
            m.aggiungi("Controllo automatico dei nuovi video", AddressOf UI_MenuControlloAutomaticoNuoviVideo)
            m.aggiungi("Ogni quanto controllare", AddressOf UI_MenuIntervalloControlloNuoviVideo)
            m.aggiungi("Quanti video controllare per canale", AddressOf UI_MenuVideoDaControllarePerCanale)
            m.aggiungi("Short nei nuovi video", AddressOf UI_MenuShortNuoviVideo)
            m.avvia("Iscrizioni e nuovi video")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuGruppoIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuControlloAutomaticoNuoviVideo()
        Try
            Dim configManager As New WGConfig()
            Dim m As New WGMenu()
            m.aggiungi("Attiva il controllo automatico", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "youtube_controllo_nuovi_video", "true", "Il controllo automatico dei nuovi video e' attivo."))
            m.aggiungi("Disattiva il controllo automatico", Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "youtube_controllo_nuovi_video", "false", "Il controllo automatico dei nuovi video e' disattivato. Potrai comunque controllare manualmente dal menu Iscrizioni."))
            m.posiziona(IndiceBinarioYoutube(configManager, "youtube_controllo_nuovi_video", "true", "true"))
            m.avvia("Controllo automatico dei nuovi video", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuControlloAutomaticoNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuIntervalloControlloNuoviVideo()
        Try
            Dim configManager As New WGConfig()
            Dim attuale As Integer = LeggiIntervalloControlloMinuti()
            Dim valori() As Integer = {15, 30, 60, 120, 240, 480, 720, 1440}
            Dim etichette() As String = {"Ogni 15 minuti", "Ogni 30 minuti", "Ogni ora", "Ogni 2 ore", "Ogni 4 ore", "Ogni 8 ore", "Ogni 12 ore", "Una volta al giorno"}

            Dim m As New WGMenu()
            Dim posizione As Integer = 2

            For i As Integer = 0 To valori.Length - 1
                Try
                    Dim valore As Integer = valori(i)
                    Dim etichetta As String = etichette(i)
                    If valore = attuale Then
                        etichetta &= " [attuale]"
                        posizione = i
                    End If
                    m.aggiungi(etichetta, Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "youtube_intervallo_controllo_minuti", valore.ToString(), "Intervallo di controllo aggiornato. La modifica sara' attiva dal prossimo riavvio del programma."))
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuIntervalloControlloNuoviVideo.Loop]: {exVoce.Message}")
                End Try
            Next

            m.posiziona(posizione)
            m.avvia("Ogni quanto controllare i nuovi video", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuIntervalloControlloNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuVideoDaControllarePerCanale()
        Try
            Dim configManager As New WGConfig()
            Dim attuale As Integer = LeggiVideoDaControllarePerCanale()
            Dim valori() As Integer = {5, 10, 15, 25, 50}

            Dim m As New WGMenu()
            Dim posizione As Integer = 2

            For i As Integer = 0 To valori.Length - 1
                Try
                    Dim valore As Integer = valori(i)
                    Dim etichetta As String = $"Ultimi {valore} video"
                    If valore = attuale Then
                        etichetta &= " [attuale]"
                        posizione = i
                    End If
                    m.aggiungi(etichetta, Sub() UI_SalvaImpostazioneYoutubeConMessaggio(configManager, "youtube_video_controllo_per_canale", valore.ToString(), $"Verranno controllati gli ultimi {valore} video di ogni canale."))
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuVideoDaControllarePerCanale.Loop]: {exVoce.Message}")
                End Try
            Next

            m.posiziona(posizione)
            m.avvia("Quanti video controllare per canale", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuVideoDaControllarePerCanale]: {ex.ToString()}")
        End Try
    End Sub

    Public Shared Function ControlloNuoviVideoAttivo() As Boolean
        Try
            Dim configManager As New WGConfig()
            Dim valore As String = configManager.LeggiValore("youtube_controllo_nuovi_video")
            If String.IsNullOrWhiteSpace(valore) Then Return True
            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControlloNuoviVideoAttivo]: {ex.ToString()}")
            Return False
        End Try
    End Function

    Public Shared Function LeggiIntervalloControlloMinuti() As Integer
        Try
            Dim configManager As New WGConfig()
            Dim valore As String = configManager.LeggiValore("youtube_intervallo_controllo_minuti")
            If String.IsNullOrWhiteSpace(valore) Then Return INTERVALLO_CONTROLLO_PREDEFINITO

            Dim minuti As Integer
            If Not Integer.TryParse(valore.Trim(), minuti) Then Return INTERVALLO_CONTROLLO_PREDEFINITO
            If minuti < 5 Then minuti = 5
            If minuti > 10080 Then minuti = 10080
            Return minuti
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.LeggiIntervalloControlloMinuti]: {ex.ToString()}")
            Return INTERVALLO_CONTROLLO_PREDEFINITO
        End Try
    End Function

    Public Shared Function LeggiVideoDaControllarePerCanale() As Integer
        Try
            Dim configManager As New WGConfig()
            Dim valore As String = configManager.LeggiValore("youtube_video_controllo_per_canale")
            If String.IsNullOrWhiteSpace(valore) Then Return VIDEO_CONTROLLO_PREDEFINITO

            Dim quanti As Integer
            If Not Integer.TryParse(valore.Trim(), quanti) Then Return VIDEO_CONTROLLO_PREDEFINITO
            If quanti < 3 Then quanti = 3
            If quanti > 100 Then quanti = 100
            Return quanti
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.LeggiVideoDaControllarePerCanale]: {ex.ToString()}")
            Return VIDEO_CONTROLLO_PREDEFINITO
        End Try
    End Function

#Region "Comandi vocali"

    Private Const SESSION_COMANDO_VOCALE As String = "comandoVocale"
    Private Const DIMENSIONE_RICERCA_VOCALE As Integer = 20
    Private _titoloComandoVocale As String = ""
    Private _videiComandoVocale As List(Of YoutubeSearchResult)
    Private _erroreComandoVocale As Boolean = False

    Public Sub RiproduciVideoPerTitolo(titolo As String)
        Try
            AssicuraInizializzazione()

            If String.IsNullOrWhiteSpace(titolo) Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Indica il titolo del video da riprodurre.")
                avviso.ShowDialog()
                Return
            End If

            _titoloComandoVocale = titolo.Trim()
            _videiComandoVocale = Nothing
            _erroreComandoVocale = False

            ChiudiProcessoInCorso()
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Ricerca del video " & _titoloComandoVocale & " in corso")
            AvviaThread(AddressOf LavoroRicercaVideoVocale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.RiproduciVideoPerTitolo]: {ex.ToString()}")
            ChiudiProcessoInCorso()
        End Try
    End Sub

    Private Sub LavoroRicercaVideoVocale()
        Try
            _videiComandoVocale = CercaVideiPerTitolo(_titoloComandoVocale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaVideoVocale]: {ex.ToString()}")
            _videiComandoVocale = Nothing
            _erroreComandoVocale = True
        End Try

        Try
            EseguiSuThreadUI(AddressOf UI_PostRicercaVideoVocale)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LavoroRicercaVideoVocale.UI]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_PostRicercaVideoVocale()
        Try
            ChiudiProcessoInCorso()

            Dim videi As List(Of YoutubeSearchResult) = _videiComandoVocale
            If videi Is Nothing OrElse videi.Count = 0 Then
                Dim avviso As New WGMessaggi()
                If _erroreComandoVocale Then
                    avviso.Messaggio("Si è verificato un errore durante la ricerca del video.")
                Else
                    avviso.Messaggio("Nessun video trovato per " & _titoloComandoVocale & ".")
                End If
                avviso.ShowDialog()
                Return
            End If

            Dim primo As YoutubeSearchResult = videi(0)
            Try
                _gestoreXml.AggiungiRecente(primo.Id, primo.Title, primo.Url, primo.ItemType)
            Catch exRecente As Exception
                logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostRicercaVideoVocale.Recente]: {exRecente.ToString()}")
            End Try

            Dim indirizzi As New List(Of String)
            For Each elemento As YoutubeSearchResult In videi
                indirizzi.Add(elemento.Url)
            Next
            AvviaPlayer(primo.Title, indirizzi)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_PostRicercaVideoVocale]: {ex.ToString()}")
        End Try
    End Sub

    Public Function CercaVideiPerTitolo(titolo As String) As List(Of YoutubeSearchResult)
        Dim videi As New List(Of YoutubeSearchResult)
        Try
            AssicuraInizializzazione()
            If String.IsNullOrWhiteSpace(titolo) Then Return videi

            Dim risultati As List(Of YoutubeSearchResult) = searcher.IniziaRicercaPaginata(SESSION_COMANDO_VOCALE, titolo.Trim(), DIMENSIONE_RICERCA_VOCALE, YoutubeItemType.Video, YoutubeSortBy.None, Nothing, Nothing)
            If risultati Is Nothing Then Return videi

            For Each elemento As YoutubeSearchResult In risultati
                If elemento Is Nothing Then Continue For
                If elemento.ItemType <> YoutubeItemType.Video Then Continue For
                If String.IsNullOrWhiteSpace(elemento.Url) Then Continue For
                videi.Add(elemento)
            Next
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CercaVideiPerTitolo]: {ex.ToString()}")
        End Try
        Return videi
    End Function

    Public Sub CercaVideoPerVoce(testo As String)
        Try
            AssicuraInizializzazione()

            If String.IsNullOrWhiteSpace(testo) Then
                avvia()
                Return
            End If

            ChiudiProcessoInCorso()
            queryRicerca = testo.Trim()
            _ricercaDaCronologia = False
            PrepThreadRicerca(YoutubeItemType.Video, "Ricerca Video in corso, attendere...")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CercaVideoPerVoce]: {ex.ToString()}")
            ChiudiProcessoInCorso()
        End Try
    End Sub

    Public Function CercaVideoPerTitolo(titolo As String) As YoutubeSearchResult
        Try
            AssicuraInizializzazione()
            If String.IsNullOrWhiteSpace(titolo) Then Return Nothing

            Dim risultati As List(Of YoutubeSearchResult) = searcher.IniziaRicercaPaginata(SESSION_COMANDO_VOCALE, titolo.Trim(), DIMENSIONE_RICERCA_VOCALE, YoutubeItemType.Video, YoutubeSortBy.None, Nothing, Nothing)
            If risultati Is Nothing OrElse risultati.Count = 0 Then Return Nothing

            Dim richiesta As String = NormalizzaTitoloVocale(titolo)
            Dim paroleRichiesta As String() = richiesta.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)

            Dim migliore As YoutubeSearchResult = Nothing
            Dim punteggioMigliore As Integer = -1

            For Each elemento As YoutubeSearchResult In risultati
                If elemento Is Nothing Then Continue For
                If elemento.ItemType <> YoutubeItemType.Video Then Continue For
                If String.IsNullOrWhiteSpace(elemento.Url) Then Continue For

                Dim punteggio As Integer = PunteggioTitoloVocale(richiesta, paroleRichiesta, elemento.Title)
                If punteggio > punteggioMigliore Then
                    punteggioMigliore = punteggio
                    migliore = elemento
                End If
            Next

            Return migliore
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CercaVideoPerTitolo]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Function PunteggioTitoloVocale(richiesta As String, paroleRichiesta As String(), titoloVideo As String) As Integer
        Try
            Dim titolo As String = NormalizzaTitoloVocale(titoloVideo)
            If String.IsNullOrWhiteSpace(titolo) Then Return 0
            If String.Equals(titolo, richiesta, StringComparison.Ordinal) Then Return 1000

            Dim punteggio As Integer = 0
            If titolo.Contains(richiesta) Then punteggio += 500
            If titolo.StartsWith(richiesta, StringComparison.Ordinal) Then punteggio += 100

            Dim paroleTitolo As New HashSet(Of String)(titolo.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
            For Each parola As String In paroleRichiesta
                If paroleTitolo.Contains(parola) Then punteggio += 10
            Next
            Return punteggio
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PunteggioTitoloVocale]: {ex.ToString()}")
            Return 0
        End Try
    End Function

    Private Function NormalizzaTitoloVocale(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim scomposto As String = testo.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            Dim risultato As New System.Text.StringBuilder(scomposto.Length)
            Dim ultimoSpazio As Boolean = True
            For Each carattere As Char In scomposto
                Dim categoria As System.Globalization.UnicodeCategory = Char.GetUnicodeCategory(carattere)
                If categoria = System.Globalization.UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(carattere) Then
                    risultato.Append(carattere)
                    ultimoSpazio = False
                ElseIf Not ultimoSpazio Then
                    risultato.Append(" "c)
                    ultimoSpazio = True
                End If
            Next
            Return risultato.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.NormalizzaTitoloVocale]: {ex.ToString()}")
            Return If(testo, "").ToLowerInvariant().Trim()
        End Try
    End Function

#End Region

End Class