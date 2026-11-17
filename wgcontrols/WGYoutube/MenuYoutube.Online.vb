' ============================================================================================
' MenuYoutube - iscrizioni e cronologia online, dall'account Google del browser di WinGabriel
' ============================================================================================
'
' Se il browser ha l'accesso a YouTube, entrando in "Iscrizioni" o in "Cronologia" si chiede se usare quelle
' online (dell'account) o quelle salvate sul computer. Se la lettura online non riesce si chiede se
' usare quelle del computer. Senza accesso nel browser non si chiede niente: sono quelle del computer.
'
' Nelle impostazioni (Iscrizioni online o offline) si sceglie la fonte una volta per tutte: "online" vale
' per l'elenco delle iscrizioni e per il controllo dei nuovi video, anche quello automatico che alimenta
' il centro notifiche (PreavvisoManager); "offline" sono le iscrizioni salvate sul computer; "chiedi ogni
' volta" (il predefinito) chiede quando si apre l'elenco e lascia il controllo automatico sulle salvate.
' Online i nuovi video si leggono dalla pagina "Iscrizioni" di YouTube e finiscono nello stesso archivio
' dei controlli offline, quindi "Nuovi video", "Canali con nuovi video" e gli avvisi funzionano uguale.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

Partial Public Class MenuYoutube

    Private Async Function FonteOnlineAsync(usoOnline As String, usoOffline As String) As Task(Of Boolean)
        Try
            If Not WGPaginaNascosta.Disponibile Then Return False
            Dim acceso As Boolean = Await WGProcessoInCorso.EseguiAsync("Controllo dell'accesso a YouTube", Function() YoutubeOnline.HaAccessoAsync())
            If Not acceso Then Return False
            Return Await WGDomanda.ChiediAsync("Nel browser di WinGabriel hai già fatto l'accesso a YouTube. Vuoi usare " & usoOnline & "? Se rispondi no, usi " & usoOffline & ".")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.FonteOnlineAsync]: {ex.ToString()}")
            Return False
        End Try
    End Function

    ' Il motivo dell'ultimo errore di YouTube, senza il punto finale e mai vuoto.
    Private Shared Function MotivoOnline() As String
        Dim motivo As String = If(YoutubeOnline.UltimoErrore, "").Trim().TrimEnd("."c)
        Return If(motivo.Length = 0, "Non sono riuscito a leggere i dati di YouTube", motivo)
    End Function

    Private Sub MessaggioOnline(testo As String)
        Dim msg As New WGMessaggi()
        msg.Messaggio(testo)
        msg.ShowDialog()
    End Sub

    Private Function CanaleDaId(id As String, titolo As String) As YoutubeSearchResult
        Return New YoutubeSearchResult() With {.Id = id, .Title = If(String.IsNullOrWhiteSpace(titolo), "Canale", titolo),
            .Url = "https://www.youtube.com/channel/" & id, .ItemType = YoutubeItemType.Channel, .ChannelId = id, .ChannelName = titolo}
    End Function

    Private Sub ApriCanaleDaId(id As String, titolo As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then
                MessaggioOnline("Canale non disponibile per questo elemento.")
                Return
            End If
            AssicuraInizializzazione()
            Dim canale As YoutubeSearchResult = CanaleDaId(id, titolo)
            elementoSelezionatoCorrente = canale
            _gestoreXml.AggiungiRecente(canale.Id, canale.Title, canale.Url, canale.ItemType)
            MostraMenuCanale()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ApriCanaleDaId]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CopiaIndirizzoOnline(indirizzo As String)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return
            Clipboard.SetText(indirizzo)
            MessaggioOnline("Indirizzo copiato negli appunti.")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CopiaIndirizzoOnline]: {ex.ToString()}")
        End Try
    End Sub

#Region "Impostazione della fonte"

    Private Const ChiaveFonteIscrizioni As String = "youtube_fonte_iscrizioni"
    Private Const ChiaveOnlineIniziato As String = "youtube_online_iniziato"

    ''' <summary>"online", "offline" oppure "" (chiedi ogni volta, il predefinito).</summary>
    Public Shared Function FonteIscrizioni() As String
        Try
            Dim valore As String = If(New WGConfig().LeggiValore(ChiaveFonteIscrizioni), "").Trim().ToLowerInvariant()
            Return If(valore = "online" OrElse valore = "offline", valore, "")
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.FonteIscrizioni]: {ex.ToString()}")
            Return ""
        End Try
    End Function

    Public Shared Function FonteIscrizioniOnline() As Boolean
        Return FonteIscrizioni() = "online"
    End Function

    Private Shared Sub ScriviConfigurazione(chiave As String, valore As String)
        Try
            Dim config As New WGConfig()
            If config.EsisteId(chiave) Then config.ModificaValore(chiave, valore) Else config.AggiungiValore(chiave, valore)
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ScriviConfigurazione]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuFonteIscrizioni()
        Try
            Dim attuale As String = FonteIscrizioni()
            Dim m As New WGMenu()
            m.aggiungi("Chiedi ogni volta" & If(attuale = "", " [attuale]", ""), Sub() ImpostaFonteIscrizioni(""))
            m.aggiungi("Online, con l'account di YouTube del browser" & If(attuale = "online", " [attuale]", ""), Sub() ImpostaFonteIscrizioni("online"))
            m.aggiungi("Offline, quelle salvate su questo computer" & If(attuale = "offline", " [attuale]", ""), Sub() ImpostaFonteIscrizioni("offline"))
            m.posiziona(If(attuale = "online", 1, If(attuale = "offline", 2, 0)))
            m.avvia("Iscrizioni online o offline", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuFonteIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

    Private Async Sub ImpostaFonteIscrizioni(valore As String)
        Try
            If valore = "online" Then
                YoutubeOnline.DimenticaAccesso()
                Dim acceso As Boolean = WGPaginaNascosta.Disponibile AndAlso Await WGProcessoInCorso.EseguiAsync("Controllo dell'accesso a YouTube", Function() YoutubeOnline.HaAccessoAsync())
                If Not acceso Then
                    MessaggioOnline("Nel browser di WinGabriel non hai ancora fatto l'accesso a YouTube. Accedi dal browser e poi scegli di nuovo questa voce.")
                    Return
                End If
            End If
            ScriviConfigurazione(ChiaveFonteIscrizioni, valore)
            Select Case valore
                Case "online"
                    MessaggioOnline("Le iscrizioni e i nuovi video si controlleranno online, con l'account di YouTube del browser. Il primo controllo non segnala niente come nuovo: da lì in poi ti avverte dei video che escono.")
                Case "offline"
                    MessaggioOnline("Le iscrizioni e i nuovi video si controlleranno su quelle salvate su questo computer.")
                Case Else
                    MessaggioOnline("Quando apri le iscrizioni, WinGabriel ti chiederà se usare quelle online. Il controllo automatico dei nuovi video usa quelle salvate su questo computer.")
            End Select
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ImpostaFonteIscrizioni]: {ex.ToString()}")
        End Try
    End Sub

#End Region

#Region "Controllo dei nuovi video"

    ''' <summary>
    ''' Controlla i nuovi video dalla fonte scelta nelle impostazioni: online dall'account (se la lettura non
    ''' riesce, per non perdere gli avvisi, ripiega sulle iscrizioni salvate), altrimenti quelle salvate.
    ''' Restituisce quanti video nuovi ha trovato. Si può chiamare da qualsiasi thread.
    ''' </summary>
    Public Shared Function ControllaIscrizioniSecondoFonte(archivio As YoutubeDb, client As YtdlpClient, quantiPerCanale As Integer) As Integer
        Try
            If FonteIscrizioniOnline() Then
                Dim trovati As Integer = ControllaOnline(archivio, client)
                If trovati >= 0 Then Return trovati
                loggerStatico.ScriviLog("MenuYoutube.ControllaIscrizioniSecondoFonte: lettura online non riuscita, uso le iscrizioni salvate")
            End If
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaIscrizioniSecondoFonte]: {ex.ToString()}")
        End Try
        Return ControllaTutteLeIscrizioni(archivio, client, quantiPerCanale)
    End Function

    ' Allinea le iscrizioni salvate a quelle dell'account e registra i video nuovi dalla pagina "Iscrizioni"
    ' di YouTube. Il primo controllo e i canali appena arrivati segnano i video come già noti, così non si
    ' riceve un diluvio di "nuovi". -1 se la lettura non è riuscita.
    Private Shared Function ControllaOnline(archivio As YoutubeDb, client As YtdlpClient) As Integer
        Try
            If archivio Is Nothing OrElse Not WGPaginaNascosta.Disponibile Then Return -1
            If Not YoutubeOnline.HaAccessoAsync().GetAwaiter().GetResult() Then Return -1
            Dim canali As List(Of YoutubeOnline.CanaleOnline) = YoutubeOnline.IscrizioniAsync().GetAwaiter().GetResult()
            If canali Is Nothing Then Return -1
            Dim video As List(Of YoutubeOnline.VideoOnline) = YoutubeOnline.NovitaAsync(60).GetAwaiter().GetResult()
            If video Is Nothing Then Return -1

            Dim primoControllo As Boolean = String.IsNullOrEmpty(New WGConfig().LeggiValore(ChiaveOnlineIniziato))
            Dim appenaArrivati As New HashSet(Of String)()
            For Each canale As YoutubeOnline.CanaleOnline In canali
                If archivio.AggiungiIscrizione(canale.Id, canale.Titolo, canale.Url) Then appenaArrivati.Add(canale.Id)
            Next
            Dim sospesi As New HashSet(Of String)()
            For Each iscrizione As IscrizioneCanale In archivio.OttieniIscrizioni(False)
                If Not iscrizione.Attiva Then sospesi.Add(iscrizione.IdCanale)
            Next

            Dim trovati As Integer = 0
            Dim giaNoti As New Dictionary(Of String, List(Of String))()
            For Each v As YoutubeOnline.VideoOnline In video
                Try
                    If String.IsNullOrWhiteSpace(v.IdCanale) OrElse String.IsNullOrWhiteSpace(v.Id) Then Continue For
                    If sospesi.Contains(v.IdCanale) Then Continue For
                    If primoControllo OrElse appenaArrivati.Contains(v.IdCanale) Then
                        If Not giaNoti.ContainsKey(v.IdCanale) Then giaNoti(v.IdCanale) = New List(Of String)()
                        giaNoti(v.IdCanale).Add(v.Id)
                        Continue For
                    End If
                    If client IsNot Nothing AndAlso Not client.TitoloAmmesso(v.Titolo) Then Continue For
                    If archivio.EVideoNoto(v.IdCanale, v.Id) Then Continue For
                    If archivio.RegistraVideoRilevato(v.IdCanale, v.Canale, v.Id, v.Titolo, v.Url, v.Durata, Date.MinValue) Then trovati += 1
                Catch exVoce As Exception
                    loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaOnline.Voce]: {exVoce.Message}")
                End Try
            Next
            For Each coppia As KeyValuePair(Of String, List(Of String)) In giaNoti
                archivio.RegistraVideoNotiIniziali(coppia.Key, coppia.Value)
            Next
            For Each canale As YoutubeOnline.CanaleOnline In canali
                archivio.AggiornaUltimoControlloIscrizione(canale.Id)
            Next
            If primoControllo Then ScriviConfigurazione(ChiaveOnlineIniziato, "1")
            Try
                archivio.EliminaVideoNuoviVistiVecchi(30)
            Catch exPulizia As Exception
                loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaOnline.Pulizia]: {exPulizia.Message}")
            End Try
            Return trovati
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.ControllaOnline]: {ex.ToString()}")
            Return -1
        End Try
    End Function

#End Region

#Region "Iscrizioni"

    Private Const VideoIscrizioniOnline As Integer = 100

    Private Async Sub ApriIscrizioniScegliendoFonte()
        Try
            AssicuraInizializzazione()
            Dim fonte As String = FonteIscrizioni()
            If fonte = "offline" Then
                EseguiSuThreadUI(AddressOf UI_MostraMenuIscrizioni)
                Return
            ElseIf fonte = "online" Then
                ' Già scelto nelle impostazioni: non si chiede, si controlla solo che l'accesso ci sia ancora.
                Dim acceso As Boolean = WGPaginaNascosta.Disponibile AndAlso Await WGProcessoInCorso.EseguiAsync("Controllo dell'accesso a YouTube", Function() YoutubeOnline.HaAccessoAsync())
                If Not acceso Then
                    MessaggioOnline("Nel browser di WinGabriel non risulta più l'accesso a YouTube")
                    Return
                End If
            ElseIf Not Await FonteOnlineAsync("le iscrizioni online, cioè quelle del tuo account", "quelle salvate su questo computer") Then
                EseguiSuThreadUI(AddressOf UI_MostraMenuIscrizioni)
                Return
            End If
            ' Si aprono i video dei canali a cui sei iscritto; l'elenco dei canali è nelle opzioni.
            Dim video As List(Of YoutubeOnline.VideoOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura dei video dei canali a cui sei iscritto", Function() YoutubeOnline.NovitaAsync(VideoIscrizioniOnline))
            If video Is Nothing Then
                MessaggioOnline(MotivoOnline() & ".")
                Return
            End If
            If video.Count = 0 Then
                ' Nessun video recente: si parte dall'elenco dei canali.
                Await MostraCanaliOnlineAsync()
                Return
            End If
            ' l'ordine per data è quello di partenza: YouTube dà solo "3 giorni fa", e con tanti video dello stesso giorno l'ordine
            ' resterebbe quello della pagina; le date esatte si prendono dal feed di ogni canale
            If OrdinePerData(OrdineSalvatoOnline(ContestoElenco.Iscrizioni)) Then
                Await WGProcessoInCorso.EseguiAsync("Metto i video in ordine", Function() CompletaDateAsync(video))
            End If
            MostraElencoVideoIscrizioniOnline(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ApriIscrizioniScegliendoFonte]: {ex.ToString()}")
        End Try
    End Sub

    ' I video più recenti dei canali a cui sei iscritto: Invio riproduce; nelle opzioni l'elenco dei canali.
    Private Sub MostraElencoVideoIscrizioniOnline(video As List(Of YoutubeOnline.VideoOnline))
        MostraElencoVideoOnline(True, video)
    End Sub

    ' L'elenco dei canali a cui è iscritto l'account.
    Private Async Function MostraCanaliOnlineAsync() As Task
        Try
            Dim canali As List(Of YoutubeOnline.CanaleOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura delle iscrizioni dal tuo account YouTube", Function() YoutubeOnline.IscrizioniAsync())
            If canali Is Nothing Then
                MessaggioOnline(YoutubeOnline.UltimoErrore)
                Return
            End If
            If canali.Count = 0 Then
                MessaggioOnline("Nel tuo account YouTube non ci sono iscrizioni.")
                Return
            End If
            MostraElencoIscrizioniOnline(canali)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraCanaliOnlineAsync]: {ex.ToString()}")
        End Try
    End Function

    Private Sub MostraElencoIscrizioniOnline(canali As List(Of YoutubeOnline.CanaleOnline))
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = False
        Dim riempi As Action = Sub()
                                   lista.impostaTitolo($"Canali a cui sei iscritto, {canali.Count}")
                                   For i As Integer = 0 To canali.Count - 1
                                       lista.aggiungiElemento(canali(i).Titolo, i.ToString())
                                   Next
                               End Sub
        riempi()
        Dim scelto As Func(Of YoutubeOnline.CanaleOnline) = Function()
                                                                Dim n As Integer
                                                                If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < canali.Count Then Return canali(n)
                                                                Return Nothing
                                                            End Function
        AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                  Dim c As YoutubeOnline.CanaleOnline = scelto()
                                                  If c IsNot Nothing Then ApriCanaleDaId(c.Id, c.Titolo)
                                              End Sub
        lista.aggiungiOpzione("Apri il canale", Sub(s, e)
                                                    Dim c As YoutubeOnline.CanaleOnline = scelto()
                                                    If c IsNot Nothing Then ApriCanaleDaId(c.Id, c.Titolo)
                                                End Sub)
        lista.aggiungiOpzione("Annulla l'iscrizione", Async Sub(s, e)
                                                          Dim c As YoutubeOnline.CanaleOnline = scelto()
                                                          If c Is Nothing Then Return
                                                          If Await AnnullaIscrizioneOnlineAsync(c.Id, c.Titolo) Then
                                                              canali.Remove(c)
                                                              lista.pulisci()
                                                              If canali.Count = 0 Then
                                                                  lista.abbandona()
                                                              Else
                                                                  riempi()
                                                              End If
                                                          End If
                                                      End Sub)
        lista.aggiungiOpzione("Segui i nuovi video su questo computer", Sub(s, e) SeguiCanaleOnline(scelto()))
        lista.aggiungiOpzione("Copia l'indirizzo del canale", Sub(s, e)
                                                                  Dim c As YoutubeOnline.CanaleOnline = scelto()
                                                                  If c IsNot Nothing Then CopiaIndirizzoOnline(c.Url)
                                                              End Sub)
        AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
        lista.avvia()
    End Sub

    ' Il canale entra tra le iscrizioni salvate su questo computer, quelle per cui WinGabriel controlla i nuovi video.
    Private Sub SeguiCanaleOnline(c As YoutubeOnline.CanaleOnline)
        Try
            If c Is Nothing Then Return
            AssicuraInizializzazione()
            If _gestoreXml.EIscritto(c.Id) Then
                MessaggioOnline("Su questo computer segui già i nuovi video di questo canale.")
                Return
            End If
            elementoSelezionatoCorrente = CanaleDaId(c.Id, c.Titolo)
            IscrivitiSoloSulComputer()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SeguiCanaleOnline]: {ex.ToString()}")
        End Try
    End Sub

#End Region

#Region "Elenco dei video dell'account"

    Private Enum ContestoElenco
        Iscrizioni
        Cronologia
        Playlist
    End Enum

    Private Const VideoPlaylistOnline As Integer = 500

    Private Sub MostraElencoVideoOnline(iscrizioni As Boolean, video As List(Of YoutubeOnline.VideoOnline))
        MostraElencoVideoOnline(If(iscrizioni, ContestoElenco.Iscrizioni, ContestoElenco.Cronologia), If(iscrizioni, "Iscrizioni online", "Cronologia online"), video, "")
    End Sub

    ' Elenco di video dell'account, per le iscrizioni, la cronologia e le playlist: Invio riproduce; nelle opzioni
    ' informazioni sul video, ordinamento, vai al canale, copia indirizzo, mi piace, aggiunta a una playlist, e
    ' secondo l'elenco: i canali e l'annullamento dell'iscrizione (iscrizioni), togliere il video (playlist).
    Private Sub MostraElencoVideoOnline(contesto As ContestoElenco, titoloBase As String, video As List(Of YoutubeOnline.VideoOnline), idPlaylist As String)
        Dim massimo As Integer = If(contesto = ContestoElenco.Iscrizioni, VideoIscrizioniOnline, If(contesto = ContestoElenco.Cronologia, VideoCronologiaOnline, VideoPlaylistOnline))
        ' l'ordine scelto l'ultima volta in questo tipo di elenco si ritrova: per le iscrizioni si parte dai più recenti
        Dim ordine As String = OrdineSalvatoOnline(contesto)
        Dim dateComplete As Boolean = video.Any(Function(v) v.PubblicatoIl.HasValue)
        Dim visibili As List(Of YoutubeOnline.VideoOnline) = OrdinaVideoOnline(video, ordine)
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = False
        Dim riempi As Action = Sub()
                                   lista.impostaTitolo(If(visibili.Count >= massimo, $"{titoloBase}, gli ultimi {visibili.Count} video", $"{titoloBase}, {visibili.Count} video"))
                                   For i As Integer = 0 To visibili.Count - 1
                                       lista.aggiungiElemento(DescrizioneVideoOnline(visibili(i)), i.ToString())
                                   Next
                               End Sub
        riempi()
        Dim scelto As Func(Of YoutubeOnline.VideoOnline) = Function()
                                                               Dim n As Integer
                                                               If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < visibili.Count Then Return visibili(n)
                                                               Return Nothing
                                                           End Function
        Dim riproduci As Action = Sub()
                                      Dim v As YoutubeOnline.VideoOnline = scelto()
                                      If v Is Nothing Then Return
                                      AvviaPlayer(v.Titolo, New List(Of String) From {v.Url})
                                      AssicuraInizializzazione()
                                      _gestoreXml.AggiungiRecente(v.Id, v.Titolo, v.Url, YoutubeItemType.Video)
                                  End Sub
        AddHandler lista.ElementoSelezionato, Sub(id As String) riproduci()
        If contesto = ContestoElenco.Iscrizioni Then lista.aggiungiOpzione("Elenco dei canali a cui sei iscritto", Async Sub(s, e) Await MostraCanaliOnlineAsync())
        lista.aggiungiOpzione("Riproduci il video", Sub(s, e) riproduci())
        lista.aggiungiOpzione("Informazioni sul video", Sub(s, e) InformazioniVideoOnline(scelto()))
        lista.aggiungiOpzione("Ordinamento", Sub(s, e)
                                                 SceltaOrdinamentoOnline(ordine, Async Sub(nuovo As String)
                                                                                     Try
                                                                                         If OrdinePerData(nuovo) AndAlso Not dateComplete Then
                                                                                             Await WGProcessoInCorso.EseguiAsync("Metto i video in ordine", Function() CompletaDateAsync(video))
                                                                                             dateComplete = True
                                                                                         End If
                                                                                         ordine = nuovo
                                                                                         SalvaOrdineOnline(contesto, nuovo)
                                                                                         visibili = OrdinaVideoOnline(video, nuovo)
                                                                                         lista.pulisci()
                                                                                         riempi()
                                                                                         lista.selezionaElemento(0)
                                                                                     Catch ex As Exception
                                                                                         logger.ScriviLog($"ERRORE [MenuYoutube.Ordinamento]: {ex.ToString()}")
                                                                                     End Try
                                                                                 End Sub)
                                             End Sub)
        lista.aggiungiOpzione("Metti mi piace al video", Async Sub(s, e)
                                                             Dim v As YoutubeOnline.VideoOnline = scelto()
                                                             If v IsNot Nothing Then Await MiPiaceVideoOnlineAsync(v, True)
                                                         End Sub)
        lista.aggiungiOpzione("Togli il mi piace al video", Async Sub(s, e)
                                                                Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                If v IsNot Nothing Then Await MiPiaceVideoOnlineAsync(v, False)
                                                            End Sub)
        lista.aggiungiOpzione("Aggiungi a una tua playlist", Async Sub(s, e)
                                                                 Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                 If v IsNot Nothing Then Await ScegliPlaylistPerVideoOnlineAsync(v)
                                                             End Sub)
        lista.aggiungiOpzione("Aggiungi a Guarda più tardi", Async Sub(s, e)
                                                                 Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                 If v IsNot Nothing Then Await AggiungiVideoAPlaylistOnlineAsync(v, "WL", "Guarda più tardi")
                                                             End Sub)
        If contesto = ContestoElenco.Playlist Then
            lista.aggiungiOpzione("Togli il video da questa playlist", Async Sub(s, e)
                                                                           Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                           If v Is Nothing Then Return
                                                                           If Await TogliVideoDaPlaylistOnlineAsync(v, idPlaylist) Then
                                                                               video.Remove(v)
                                                                               visibili.Remove(v)
                                                                               lista.pulisci()
                                                                               If visibili.Count = 0 Then
                                                                                   lista.abbandona()
                                                                               Else
                                                                                   riempi()
                                                                                   lista.selezionaElemento(0)
                                                                               End If
                                                                           End If
                                                                       End Sub)
        End If
        lista.aggiungiOpzione("Vai al canale", Sub(s, e)
                                                   Dim v As YoutubeOnline.VideoOnline = scelto()
                                                   If v IsNot Nothing Then ApriCanaleDaId(v.IdCanale, v.Canale)
                                               End Sub)
        If contesto = ContestoElenco.Iscrizioni Then
            lista.aggiungiOpzione("Annulla l'iscrizione a questo canale", Async Sub(s, e)
                                                                              Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                              If v IsNot Nothing Then Await AnnullaIscrizioneOnlineAsync(v.IdCanale, v.Canale)
                                                                          End Sub)
        End If
        lista.aggiungiOpzione("Copia l'indirizzo del video", Sub(s, e)
                                                                 Dim v As YoutubeOnline.VideoOnline = scelto()
                                                                 If v IsNot Nothing Then CopiaIndirizzoOnline(v.Url)
                                                             End Sub)
        AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
        lista.avvia()
    End Sub

    ' Un'azione sull'account con l'attesa; se YouTube non risponde chiede se riprovare. Vero se è riuscita.
    Private Async Function AzioneAccountAsync(attesa As String, azione As Func(Of Task(Of Boolean)), riuscita As String) As Task(Of Boolean)
        Try
            Do
                Dim esito As Boolean = Await WGProcessoInCorso.EseguiAsync(attesa, azione)
                If esito Then
                    If riuscita <> "" Then MessaggioOnline(riuscita)
                    Return True
                End If
                MessaggioOnline(YoutubeOnline.UltimoErrore)
                Return False
            Loop
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AzioneAccountAsync]: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Async Function MiPiaceVideoOnlineAsync(v As YoutubeOnline.VideoOnline, mettere As Boolean) As Task
        Await AzioneAccountAsync(If(mettere, "Mi piace al video sul tuo account YouTube", "Mi piace tolto dal tuo account YouTube"),
                                 Function() YoutubeOnline.MiPiaceAsync(v.Id, mettere),
                                 If(mettere, "Mi piace aggiunto.", "Mi piace tolto."))
    End Function

    Private Async Function AggiungiVideoAPlaylistOnlineAsync(v As YoutubeOnline.VideoOnline, idPlaylist As String, nomePlaylist As String) As Task
        Await AzioneAccountAsync("Aggiunta del video a " & nomePlaylist,
                                 Function() YoutubeOnline.AggiungiAPlaylistAsync(v.Id, idPlaylist),
                                 "Video aggiunto a " & nomePlaylist & ".")
    End Function

    ' Toglie il video dalla playlist, dopo una conferma; vero se è stato tolto.
    Private Async Function TogliVideoDaPlaylistOnlineAsync(v As YoutubeOnline.VideoOnline, idPlaylist As String) As Task(Of Boolean)
        Try
            If Not Await WGDomanda.ChiediAsync("Togliere il video " & v.Titolo & " da questa playlist?") Then Return False
            Return Await AzioneAccountAsync("Rimozione del video dalla playlist", Function() YoutubeOnline.TogliDaPlaylistAsync(idPlaylist, v.SetId), "Video tolto dalla playlist.")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.TogliVideoDaPlaylistOnlineAsync]: {ex.ToString()}")
            Return False
        End Try
    End Function

    ' Le tue playlist, per scegliere a quale aggiungere il video.
    Private Async Function ScegliPlaylistPerVideoOnlineAsync(v As YoutubeOnline.VideoOnline) As Task
        Try
            Dim playlist As List(Of YoutubeOnline.PlaylistOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura delle tue playlist", Function() YoutubeOnline.PlaylistAsync())
            If playlist Is Nothing Then
                MessaggioOnline(YoutubeOnline.UltimoErrore)
                Return
            End If
            ' "Video che ti piacciono" non si riempie così: ci si arriva col mi piace.
            Dim scelte As List(Of YoutubeOnline.PlaylistOnline) = playlist.Where(Function(p) p.Id <> "LL").ToList()
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Aggiungi a una playlist")
            For i As Integer = 0 To scelte.Count - 1
                lista.aggiungiElemento(DescrizionePlaylistOnline(scelte(i)), i.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Async Sub(id As String)
                                                      Dim n As Integer
                                                      If Not Integer.TryParse(id, n) OrElse n < 0 OrElse n >= scelte.Count Then Return
                                                      lista.abbandona()
                                                      Await AggiungiVideoAPlaylistOnlineAsync(v, scelte(n).Id, scelte(n).Titolo)
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ScegliPlaylistPerVideoOnlineAsync]: {ex.ToString()}")
        End Try
    End Function

    Private Shared Function DescrizionePlaylistOnline(p As YoutubeOnline.PlaylistOnline) As String
        Return p.Titolo & If(String.IsNullOrWhiteSpace(p.Quanti), "", ", " & p.Quanti)
    End Function

#Region "Il tuo account: playlist, video piaciuti, guarda più tardi"

    ' Voce "Il tuo account" del menu YouTube.
    Private Async Sub MostraMenuAccount()
        Try
            If Not WGPaginaNascosta.Disponibile Then
                MessaggioOnline("Per usare il tuo account serve il browser di WinGabriel, che in questo momento non è disponibile.")
                Return
            End If
            Dim acceso As Boolean = Await WGProcessoInCorso.EseguiAsync("Controllo dell'accesso a YouTube", Function() YoutubeOnline.HaAccessoAsync())
            If Not acceso Then
                MessaggioOnline("Nel browser di WinGabriel non hai ancora fatto l'accesso a YouTube. Accedi dal browser e poi riprova.")
                Return
            End If
            Dim m As New WGMenu()
            m.aggiungi("Le tue playlist", Async Sub(s, e) Await ApriPlaylistOnlineAsync())
            m.aggiungi("Video che ti piacciono", Async Sub(s, e) Await ApriPlaylistDellAccountAsync("LL", "Video che ti piacciono"))
            m.aggiungi("Guarda più tardi", Async Sub(s, e) Await ApriPlaylistDellAccountAsync("WL", "Guarda più tardi"))
            m.avvia("Il tuo account", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraMenuAccount]: {ex.ToString()}")
        End Try
    End Sub

    Private Async Function ApriPlaylistOnlineAsync() As Task
        Try
            Dim playlist As List(Of YoutubeOnline.PlaylistOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura delle tue playlist", Function() YoutubeOnline.PlaylistAsync())
            If playlist Is Nothing Then
                MessaggioOnline(YoutubeOnline.UltimoErrore)
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Le tue playlist, {playlist.Count}")
            For i As Integer = 0 To playlist.Count - 1
                lista.aggiungiElemento(DescrizionePlaylistOnline(playlist(i)), i.ToString())
            Next
            Dim scelta As Func(Of YoutubeOnline.PlaylistOnline) = Function()
                                                                      Dim n As Integer
                                                                      If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < playlist.Count Then Return playlist(n)
                                                                      Return Nothing
                                                                  End Function
            AddHandler lista.ElementoSelezionato, Async Sub(id As String)
                                                      Dim p As YoutubeOnline.PlaylistOnline = scelta()
                                                      If p IsNot Nothing Then Await ApriPlaylistDellAccountAsync(p.Id, p.Titolo)
                                                  End Sub
            lista.aggiungiOpzione("Apri la playlist", Async Sub(s, e)
                                                          Dim p As YoutubeOnline.PlaylistOnline = scelta()
                                                          If p IsNot Nothing Then Await ApriPlaylistDellAccountAsync(p.Id, p.Titolo)
                                                      End Sub)
            lista.aggiungiOpzione("Copia l'indirizzo della playlist", Sub(s, e)
                                                                          Dim p As YoutubeOnline.PlaylistOnline = scelta()
                                                                          If p IsNot Nothing Then CopiaIndirizzoOnline(p.Url)
                                                                      End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ApriPlaylistOnlineAsync]: {ex.ToString()}")
        End Try
    End Function

    ' I video di una playlist dell'account (anche "Video che ti piacciono" e "Guarda più tardi").
    Private Async Function ApriPlaylistDellAccountAsync(idPlaylist As String, titolo As String) As Task
        Try
            Dim video As List(Of YoutubeOnline.VideoOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura dei video di " & titolo, Function() YoutubeOnline.VideoDiPlaylistAsync(idPlaylist, VideoPlaylistOnline))
            If video Is Nothing Then
                MessaggioOnline(YoutubeOnline.UltimoErrore)
                Return
            End If
            If video.Count = 0 Then
                MessaggioOnline("In " & titolo & " non ci sono video.")
                Return
            End If
            MostraElencoVideoOnline(ContestoElenco.Playlist, titolo, video, idPlaylist)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ApriPlaylistDellAccountAsync]: {ex.ToString()}")
        End Try
    End Function

#End Region


    Private Shared Function DescrizioneVideoOnline(v As YoutubeOnline.VideoOnline) As String
        Return v.Titolo & If(String.IsNullOrEmpty(v.Canale), "", ", " & v.Canale) & If(String.IsNullOrEmpty(v.Durata), "", ", " & v.Durata) & If(String.IsNullOrEmpty(v.Pubblicato), "", ", " & v.Pubblicato)
    End Function

    ' Le stesse informazioni che si hanno sui video delle altre liste (titolo, canale, durata, descrizione...).
    Private Sub InformazioniVideoOnline(v As YoutubeOnline.VideoOnline)
        Try
            If v Is Nothing Then Return
            AssicuraInizializzazione()
            elementoSelezionatoCorrente = New YoutubeSearchResult() With {.Id = v.Id, .Title = v.Titolo, .Url = v.Url,
                .ItemType = YoutubeItemType.Video, .ChannelId = v.IdCanale, .ChannelName = v.Canale}
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia($"Recupero informazioni: {v.Titolo}")
            AvviaThread(AddressOf LavoroRecuperaInformazioni)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.InformazioniVideoOnline]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SceltaOrdinamentoOnline(attuale As String, applica As Action(Of String))
        Try
            Dim scelte As New List(Of Tuple(Of String, String)) From {
                Tuple.Create("originale", "Come su YouTube"),
                Tuple.Create("recenti", "Pubblicati più di recente prima"),
                Tuple.Create("vecchi", "Pubblicati meno di recente prima"),
                Tuple.Create("az", "Titolo dalla A alla Z"),
                Tuple.Create("za", "Titolo dalla Z alla A"),
                Tuple.Create("canale", "Canale dalla A alla Z"),
                Tuple.Create("lunghi", "Più lunghi prima"),
                Tuple.Create("brevi", "Più brevi prima")}
            Dim m As New WGMenu()
            Dim posizione As Integer = 0
            For i As Integer = 0 To scelte.Count - 1
                Dim chiave As String = scelte(i).Item1
                If chiave = attuale Then posizione = i
                m.aggiungi(scelte(i).Item2 & If(chiave = attuale, " [attuale]", ""), Sub() applica(chiave))
            Next
            m.posiziona(posizione)
            m.avvia("Ordinamento", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.SceltaOrdinamentoOnline]: {ex.ToString()}")
        End Try
    End Sub

    Private Shared Function OrdinePerData(criterio As String) As Boolean
        Return criterio = "recenti" OrElse criterio = "vecchi"
    End Function

    Private Const ChiaveOrdineIscrizioni As String = "youtube_ordine_video_iscrizioni"
    Private Const ChiaveOrdineCronologia As String = "youtube_ordine_video_cronologia"
    Private Const ChiaveOrdinePlaylist As String = "youtube_ordine_video_playlist"

    Private Shared Function ChiaveOrdineOnline(contesto As ContestoElenco) As String
        Select Case contesto
            Case ContestoElenco.Iscrizioni
                Return ChiaveOrdineIscrizioni
            Case ContestoElenco.Cronologia
                Return ChiaveOrdineCronologia
        End Select
        Return ChiaveOrdinePlaylist
    End Function

    ' L'ordine scelto l'ultima volta per questo tipo di elenco. Le iscrizioni partono dai video pubblicati più di recente, la
    ' cronologia e le playlist dall'ordine di YouTube.
    Private Shared Function OrdineSalvatoOnline(contesto As ContestoElenco) As String
        Try
            Dim config As New WGConfig()
            Dim chiave As String = ChiaveOrdineOnline(contesto)
            If config.EsisteId(chiave) Then
                Dim v As String = If(config.LeggiValore(chiave), "").Trim()
                If {"originale", "recenti", "vecchi", "az", "za", "canale", "lunghi", "brevi"}.Contains(v) Then Return v
            End If
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.OrdineSalvatoOnline]: {ex.Message}")
        End Try
        Return If(contesto = ContestoElenco.Iscrizioni, "recenti", "originale")
    End Function

    Private Shared Sub SalvaOrdineOnline(contesto As ContestoElenco, ordine As String)
        Try
            Dim config As New WGConfig()
            config.SalvaValore(ChiaveOrdineOnline(contesto), ordine)
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.SalvaOrdineOnline]: {ex.Message}")
        End Try
    End Sub

    ' YouTube dà solo "3 giorni fa": con venti video dello stesso giorno non si saprebbe quale è uscito prima. La data esatta si prende dal
    ' feed pubblico di ogni canale (gli ultimi quindici video) e, per gli altri, da yt-dlp; chi resta senza ha la data stimata dal "quanto tempo fa".
    Private Shared Async Function CompletaDateAsync(video As List(Of YoutubeOnline.VideoOnline)) As Task
        Try
            Dim canali As List(Of String) = video.Select(Function(v) v.IdCanale).Where(Function(c) YoutubeRss.IdValido(c)).Distinct().ToList()
            If canali.Count = 0 Then Return
            Dim feed As Dictionary(Of String, List(Of YoutubeSearchResult)) = Await Task.Run(Function() YoutubeRss.LeggiPiuCanali(canali))
            Dim date_ As New Dictionary(Of String, DateTime)()
            For Each voci As List(Of YoutubeSearchResult) In feed.Values
                For Each r As YoutubeSearchResult In voci
                    If r.UploadDate.HasValue AndAlso Not String.IsNullOrEmpty(r.Id) Then date_(r.Id) = r.UploadDate.Value
                Next
            Next
            For Each v As YoutubeOnline.VideoOnline In video
                Dim d As DateTime
                If date_.TryGetValue(v.Id, d) Then v.PubblicatoIl = d
            Next
            ' i video più vecchi non stanno più nel feed: la data esatta la dà yt-dlp (al massimo quaranta video, per non aspettare troppo)
            Dim mancanti As List(Of String) = video.Where(Function(v) Not v.PubblicatoIl.HasValue AndAlso Not String.IsNullOrEmpty(v.Id)).Select(Function(v) v.Id).Take(40).ToList()
            If mancanti.Count > 0 Then
                Dim daYtdlp As Dictionary(Of String, DateTime) = Await Task.Run(Function() New YtdlpClient().OttieniDatePubblicazione(mancanti))
                For Each v As YoutubeOnline.VideoOnline In video
                    Dim d As DateTime
                    If Not v.PubblicatoIl.HasValue AndAlso daYtdlp.TryGetValue(v.Id, d) Then v.PubblicatoIl = d
                Next
            End If
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.CompletaDateAsync]: {ex.ToString()}")
        End Try
    End Function

    ' La data esatta se c'è, altrimenti quella stimata da "3 ore fa"; Nothing se non si sa.
    Private Shared Function DataPerOrdine(v As YoutubeOnline.VideoOnline, adesso As DateTime) As DateTime?
        If v.PubblicatoIl.HasValue Then Return v.PubblicatoIl.Value
        Dim minuti As Double = EtaInMinuti(v.Pubblicato)
        If minuti = Double.MaxValue Then Return Nothing
        Return adesso.AddMinutes(-minuti)
    End Function

    ' I video che non si sanno mettere in ordine (data o durata mancante) vanno sempre in fondo.
    Private Shared Function OrdinaVideoOnline(video As List(Of YoutubeOnline.VideoOnline), criterio As String) As List(Of YoutubeOnline.VideoOnline)
        Dim confronto As StringComparer = StringComparer.CurrentCultureIgnoreCase
        Dim adesso As DateTime = DateTime.Now
        Select Case criterio
            Case "recenti"
                Return video.Select(Function(v) New With {.V = v, .D = DataPerOrdine(v, adesso)}).
                    OrderBy(Function(x) Not x.D.HasValue).ThenByDescending(Function(x) If(x.D.HasValue, x.D.Value, DateTime.MinValue)).Select(Function(x) x.V).ToList()
            Case "vecchi"
                Return video.Select(Function(v) New With {.V = v, .D = DataPerOrdine(v, adesso)}).
                    OrderBy(Function(x) Not x.D.HasValue).ThenBy(Function(x) If(x.D.HasValue, x.D.Value, DateTime.MinValue)).Select(Function(x) x.V).ToList()
            Case "az"
                Return video.OrderBy(Function(v) v.Titolo, confronto).ToList()
            Case "za"
                Return video.OrderByDescending(Function(v) v.Titolo, confronto).ToList()
            Case "canale"
                Return video.OrderBy(Function(v) v.Canale, confronto).ThenBy(Function(v) v.Titolo, confronto).ToList()
            Case "lunghi"
                Return video.OrderByDescending(Function(v) DurataInSecondi(v.Durata)).ToList()
            Case "brevi"
                Return video.OrderBy(Function(v) DurataInSecondi(v.Durata) = 0).ThenBy(Function(v) DurataInSecondi(v.Durata)).ToList()
            Case Else
                Return New List(Of YoutubeOnline.VideoOnline)(video)
        End Select
    End Function

    ' "3 ore fa", "2 settimane fa", "Trasmesso 1 giorno fa", "5 months ago": da quanti minuti; Double.MaxValue se non si capisce.
    Private Shared Function EtaInMinuti(testo As String) As Double
        Try
            Dim m As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(If(testo, ""), "(\d+)\s*([^\W\d]+)")
            If Not m.Success Then Return Double.MaxValue
            Dim n As Double = Double.Parse(m.Groups(1).Value, System.Globalization.CultureInfo.InvariantCulture)
            Dim unita As String = m.Groups(2).Value.ToLowerInvariant()
            If unita.StartsWith("sec") Then Return n / 60.0
            If unita.StartsWith("min") Then Return n
            If unita.StartsWith("or") OrElse unita.StartsWith("hou") Then Return n * 60.0
            If unita.StartsWith("gio") OrElse unita.StartsWith("day") Then Return n * 1440.0
            If unita.StartsWith("set") OrElse unita.StartsWith("wee") Then Return n * 10080.0
            If unita.StartsWith("mes") OrElse unita.StartsWith("mon") Then Return n * 43200.0
            If unita.StartsWith("ann") OrElse unita.StartsWith("yea") Then Return n * 525600.0
        Catch ex As Exception
            loggerStatico.ScriviLog($"ERRORE [MenuYoutube.EtaInMinuti]: {ex.Message}")
        End Try
        Return Double.MaxValue
    End Function

    ' "12:34" o "1:02:03": in secondi; 0 se non c'è (per esempio una diretta).
    Private Shared Function DurataInSecondi(testo As String) As Long
        Try
            Dim totale As Long = 0
            For Each parte As String In If(testo, "").Split(":"c)
                Dim n As Integer
                If Not Integer.TryParse(parte.Trim(), n) Then Return 0
                totale = totale * 60 + n
            Next
            Return totale
        Catch ex As Exception
            Return 0
        End Try
    End Function

#End Region

#Region "Iscriversi e annullare l'iscrizione"

    ' Vero se l'azione va fatta anche sull'account YouTube del browser: sempre con "Online", mai con "Offline";
    ' con "Chiedi ogni volta" si chiede, ma solo se nel browser c'è l'accesso.
    Private Async Function UsareAccountAsync(domanda As String) As Task(Of Boolean)
        Try
            Dim fonte As String = FonteIscrizioni()
            If fonte = "offline" OrElse Not WGPaginaNascosta.Disponibile Then Return False
            Dim acceso As Boolean = Await WGProcessoInCorso.EseguiAsync("Controllo dell'accesso a YouTube", Function() YoutubeOnline.HaAccessoAsync())
            If Not acceso Then Return False
            If fonte = "online" Then Return True
            Return Await WGDomanda.ChiediAsync(domanda)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UsareAccountAsync]: {ex.ToString()}")
            Return False
        End Try
    End Function

    ' Iscrive o annulla l'iscrizione sull'account; se YouTube non risponde chiede se riprovare.
    Private Async Function CambiaIscrizioneOnlineAsync(iscriviti As Boolean, idCanale As String) As Task(Of Boolean)
        Do
            Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync(If(iscriviti, "Iscrizione al canale sul tuo account YouTube", "Annullamento dell'iscrizione sul tuo account YouTube"),
                                                                         Function() If(iscriviti, YoutubeOnline.IscriviAsync(idCanale), YoutubeOnline.DisiscriviAsync(idCanale)))
            If riuscito Then Return True
            MessaggioOnline(YoutubeOnline.UltimoErrore)
            Return False
        Loop
    End Function

    ' Annulla l'iscrizione sull'account (dopo una conferma) e la toglie anche da quelle salvate sul computer.
    Private Async Function AnnullaIscrizioneOnlineAsync(idCanale As String, titolo As String) As Task(Of Boolean)
        Try
            If Not YoutubeOnline.IdCanaleValido(idCanale) Then
                MessaggioOnline("Di questo canale non ho l'identificativo che serve a YouTube.")
                Return False
            End If
            If Not Await WGDomanda.ChiediAsync("Annullare l'iscrizione a " & If(String.IsNullOrWhiteSpace(titolo), "questo canale", titolo) & " sul tuo account YouTube?") Then Return False
            If Not Await CambiaIscrizioneOnlineAsync(False, idCanale) Then Return False
            AssicuraInizializzazione()
            _gestoreXml.RimuoviIscrizione(idCanale)
            MessaggioOnline("Iscrizione annullata.")
            Return True
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AnnullaIscrizioneOnlineAsync]: {ex.ToString()}")
            Return False
        End Try
    End Function

    ' "Iscriviti al canale" nel menu del canale: sul tuo account YouTube (se serve) e poi sul computer, dove
    ' WinGabriel tiene l'elenco per segnalare i nuovi video.
    Private Async Sub OpzioneIscrivitiAlCanale()
        Try
            Dim canale As YoutubeSearchResult = elementoSelezionatoCorrente
            If canale Is Nothing Then Return
            If YoutubeOnline.IdCanaleValido(canale.Id) AndAlso Await UsareAccountAsync("Vuoi iscriverti al canale anche sul tuo account YouTube? Se rispondi no, lo segui solo su questo computer.") Then
                If Not Await CambiaIscrizioneOnlineAsync(True, canale.Id) Then
                    If Not Await WGDomanda.ChiediAsync("Vuoi seguire il canale solo su questo computer?") Then Return
                End If
            End If
            elementoSelezionatoCorrente = canale
            IscrivitiSoloSulComputer()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneIscrivitiAlCanale]: {ex.ToString()}")
        End Try
    End Sub

    ' "Annulla iscrizione al canale" nel menu del canale: sul tuo account YouTube (se serve) e sul computer.
    Private Async Sub OpzioneAnnullaIscrizioneCanale()
        Try
            Dim canale As YoutubeSearchResult = elementoSelezionatoCorrente
            If canale Is Nothing Then Return
            If YoutubeOnline.IdCanaleValido(canale.Id) AndAlso Await UsareAccountAsync("Vuoi annullare l'iscrizione anche sul tuo account YouTube? Se rispondi no, la annulli solo su questo computer.") Then
                If Not Await CambiaIscrizioneOnlineAsync(False, canale.Id) Then
                    If Not Await WGDomanda.ChiediAsync("Vuoi annullarla solo su questo computer?") Then Return
                End If
            End If
            elementoSelezionatoCorrente = canale
            AnnullaIscrizioneSoloSulComputer()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneAnnullaIscrizioneCanale]: {ex.ToString()}")
        End Try
    End Sub

#End Region

#Region "Cronologia"

    Private Const VideoCronologiaOnline As Integer = 300

    Private Async Sub ApriCronologiaScegliendoFonte()
        Try
            AssicuraInizializzazione()
            If Not Await FonteOnlineAsync("la cronologia online, cioè quella del tuo account", "quella salvata su questo computer") Then
                EseguiSuThreadUI(AddressOf UI_MostraMenuCronologia)
                Return
            End If
            Dim video As List(Of YoutubeOnline.VideoOnline) = Await WGProcessoInCorso.EseguiAsync("Lettura della cronologia dal tuo account YouTube", Function() YoutubeOnline.CronologiaAsync(VideoCronologiaOnline))
            If video Is Nothing Then
                MessaggioOnline(MotivoOnline() & ".")
                Return
            End If
            If video.Count = 0 Then
                MessaggioOnline("Nella cronologia del tuo account YouTube non ci sono video.")
                Return
            End If
            MostraElencoCronologiaOnline(video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ApriCronologiaScegliendoFonte]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraElencoCronologiaOnline(video As List(Of YoutubeOnline.VideoOnline))
        MostraElencoVideoOnline(False, video)
    End Sub

#End Region

End Class
