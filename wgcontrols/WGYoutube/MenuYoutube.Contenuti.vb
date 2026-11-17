' ============================================================================================
' MenuYoutube - descrizione dei video e commenti
' ============================================================================================
'
' Descrizione: nelle informazioni di un video la voce "Descrizione" apre l'elenco dei paragrafi; nelle opzioni ci sono i
' link (Invio li apre nel browser), i punti del video con l'ora (Invio riproduce il video da quel punto), gli hashtag
' (Invio cerca i video con quell'hashtag), copiare la descrizione e leggerla tutta in un campo di testo.
' Commenti: ogni commento dice se è fissato o del creatore, quando è stato scritto, i mi piace e quante risposte ha.
' Invio su un commento con risposte apre la conversazione: il commento e le sue risposte, una per voce. Nelle opzioni
' dei commenti: cambiare l'ordine (più rilevanti o più recenti), leggerli per intero e copiarli.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGPlayerMpv

Partial Public Class MenuYoutube

    ' "top" i commenti più rilevanti, "new" i più recenti.
    Private _ordineCommenti As String = "top"
    Private _secondiInizialiPlayer As Double

#Region "Descrizione"

    ' La voce "Descrizione" delle informazioni di un video.
    Private Sub MostraDescrizioneStrutturata(details As YoutubeItemDetails)
        Try
            If details Is Nothing OrElse String.IsNullOrWhiteSpace(details.Description) Then Return
            Dim d As DescrizioneVideo = DescrizioneVideo.Analizza(details.Description)
            If d.Paragrafi.Count = 0 AndAlso d.Link.Count = 0 AndAlso d.Punti.Count = 0 AndAlso d.Hashtag.Count = 0 Then
                MostraDescrizioneCompleta(details)
                Return
            End If

            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Descrizione: {details.Title}")
            If d.Paragrafi.Count = 0 Then lista.aggiungiElemento("Nella descrizione non c'è testo, solo link, punti del video o hashtag: sono nelle opzioni", "VUOTO")
            For i As Integer = 0 To d.Paragrafi.Count - 1
                Dim testo As String = d.Paragrafi(i)
                lista.aggiungiElemento(If(testo.Length > SOGLIA_TESTO_LUNGO, testo.Substring(0, SOGLIA_TESTO_LUNGO) & "...", testo), "P" & i.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      ' Un paragrafo lungo si legge per intero in un campo di testo.
                                                      If String.IsNullOrEmpty(id) OrElse id(0) <> "P"c Then Return
                                                      Dim n As Integer
                                                      If Integer.TryParse(id.Substring(1), n) AndAlso n >= 0 AndAlso n < d.Paragrafi.Count AndAlso d.Paragrafi(n).Length > SOGLIA_TESTO_LUNGO Then
                                                          MostraTestoInCampo("Descrizione, paragrafo " & (n + 1).ToString(), d.Paragrafi(n))
                                                      End If
                                                  End Sub
            If d.Link.Count > 0 Then lista.aggiungiOpzione($"Link nella descrizione, {d.Link.Count}", Sub(s, e) MostraLinkDescrizione(d))
            If d.Punti.Count > 0 Then lista.aggiungiOpzione($"Punti del video, {d.Punti.Count}", Sub(s, e) MostraPuntiDescrizione(d, details))
            If d.Hashtag.Count > 0 Then lista.aggiungiOpzione($"Hashtag, {d.Hashtag.Count}", Sub(s, e) MostraHashtagDescrizione(d))
            lista.aggiungiOpzione("Leggi tutta la descrizione in un campo di testo", Sub(s, e) MostraDescrizioneCompleta(details))
            lista.aggiungiOpzione("Copia la descrizione", Sub(s, e) CopiaTesto(details.Description, "Descrizione copiata negli appunti."))
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraDescrizioneStrutturata]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub MostraTestoInCampo(titolo As String, testo As String)
        Try
            campoTestoLungo = New WGCampo()
            AddHandler campoTestoLungo.chiusura, AddressOf ChiusuraCampoTestoLungo
            campoTestoLungo.init(titolo, testo, True, True)
            campoTestoLungo.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraTestoInCampo]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub CopiaTesto(testo As String, conferma As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Clipboard.SetText(testo)
            Dim msg As New WGMessaggi()
            msg.Messaggio(conferma)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CopiaTesto]: {ex.ToString()}")
        End Try
    End Sub

    ' Invio apre il link nel browser, Alt lo copia.
    Private Sub MostraLinkDescrizione(d As DescrizioneVideo)
        Try
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Link nella descrizione, {d.Link.Count}")
            For i As Integer = 0 To d.Link.Count - 1
                Dim l As DescrizioneVideo.Collegamento = d.Link(i)
                lista.aggiungiElemento(l.Sito & If(String.IsNullOrWhiteSpace(l.Contesto), "", ", " & l.Contesto), i.ToString())
            Next
            Dim scelto As Func(Of DescrizioneVideo.Collegamento) = Function()
                                                                       Dim n As Integer
                                                                       If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < d.Link.Count Then Return d.Link(n)
                                                                       Return Nothing
                                                                   End Function
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim l As DescrizioneVideo.Collegamento = scelto()
                                                      If l IsNot Nothing Then WGImpostazioniComuni.ApriLinkConBrowserPreferito(l.Url)
                                                  End Sub
            lista.aggiungiOpzione("Apri nel browser", Sub(s, e)
                                                          Dim l As DescrizioneVideo.Collegamento = scelto()
                                                          If l IsNot Nothing Then WGImpostazioniComuni.ApriLinkConBrowserPreferito(l.Url)
                                                      End Sub)
            lista.aggiungiOpzione("Copia l'indirizzo", Sub(s, e)
                                                           Dim l As DescrizioneVideo.Collegamento = scelto()
                                                           If l IsNot Nothing Then CopiaTesto(l.Url, "Indirizzo copiato negli appunti.")
                                                       End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraLinkDescrizione]: {ex.ToString()}")
        End Try
    End Sub

    ' Invio riproduce il video da quel punto.
    Private Sub MostraPuntiDescrizione(d As DescrizioneVideo, details As YoutubeItemDetails)
        Try
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Punti del video, {d.Punti.Count}")
            For i As Integer = 0 To d.Punti.Count - 1
                lista.aggiungiElemento(d.Punti(i).Ora & ", " & d.Punti(i).Etichetta, i.ToString())
            Next
            Dim scelto As Func(Of DescrizioneVideo.PuntoVideo) = Function()
                                                                     Dim n As Integer
                                                                     If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < d.Punti.Count Then Return d.Punti(n)
                                                                     Return Nothing
                                                                 End Function
            Dim riproduci As Action = Sub()
                                          Dim p As DescrizioneVideo.PuntoVideo = scelto()
                                          If p Is Nothing OrElse String.IsNullOrWhiteSpace(details.Url) Then Return
                                          AvviaPlayerDa(details.Title, details.Url, p.Secondi)
                                      End Sub
            AddHandler lista.ElementoSelezionato, Sub(id As String) riproduci()
            lista.aggiungiOpzione("Riproduci da questo punto", Sub(s, e) riproduci())
            lista.aggiungiOpzione("Copia l'indirizzo con il tempo", Sub(s, e)
                                                                        Dim p As DescrizioneVideo.PuntoVideo = scelto()
                                                                        If p IsNot Nothing Then CopiaTesto(details.Url & If(details.Url.Contains("?"), "&", "?") & "t=" & p.Secondi.ToString() & "s", "Indirizzo copiato negli appunti.")
                                                                    End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraPuntiDescrizione]: {ex.ToString()}")
        End Try
    End Sub

    ' Invio cerca i video con quell'hashtag.
    Private Sub MostraHashtagDescrizione(d As DescrizioneVideo)
        Try
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Hashtag, {d.Hashtag.Count}")
            For i As Integer = 0 To d.Hashtag.Count - 1
                lista.aggiungiElemento(d.Hashtag(i), i.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim n As Integer
                                                      If Not Integer.TryParse(id, n) OrElse n < 0 OrElse n >= d.Hashtag.Count Then Return
                                                      lista.abbandona()
                                                      AssicuraInizializzazione()
                                                      queryRicerca = d.Hashtag(n)
                                                      PrepThreadRicerca(YoutubeItemType.Video, "Ricerca dei video con " & d.Hashtag(n) & " in corso, attendere...")
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraHashtagDescrizione]: {ex.ToString()}")
        End Try
    End Sub

    ' Riproduce il video partendo da un punto: il lettore parte, e appena sa la durata ci si sposta.
    Private Sub AvviaPlayerDa(titolo As String, url As String, secondi As Double)
        Try
            _secondiInizialiPlayer = secondi
            AvviaPlayer(titolo, New List(Of String) From {url})
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AvviaPlayerDa]: {ex.ToString()}")
        End Try
    End Sub

    ' Chiamata dopo l'avvio del lettore (UI_LaunchPlayer): se si era chiesto un punto di partenza aspetta che il
    ' video abbia la durata (fino a mezzo minuto) e ci si sposta.
    Private Sub PianificaSaltoIniziale()
        Try
            Dim secondi As Double = _secondiInizialiPlayer
            _secondiInizialiPlayer = 0
            If secondi <= 0 OrElse _playerForm Is Nothing Then Return
            Dim lettore As WGPlayerFormInt = _playerForm
            Task.Run(Async Function()
                         Try
                             For tentativo As Integer = 1 To 150
                                 Await Task.Delay(200)
                                 Dim pronto As Boolean = False
                                 EseguiSuThreadUI(Sub() pronto = lettore.PuoSaltare())
                                 If pronto Then
                                     EseguiSuThreadUI(Sub() lettore.SaltaA(secondi))
                                     Return
                                 End If
                             Next
                         Catch ex As Exception
                             logger.ScriviLog($"ERRORE [MenuYoutube.PianificaSaltoIniziale]: {ex.ToString()}")
                         End Try
                     End Function)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.PianificaSaltoIniziale]: {ex.ToString()}")
        End Try
    End Sub

#End Region

#Region "Commenti"

    ' L'etichetta di un commento: se è fissato o del creatore, chi l'ha scritto, quando, i mi piace e le risposte, poi il testo.
    Private Function EtichettaCommento(c As YoutubeComment, prefisso As String) As String
        Try
            Dim parti As New List(Of String)()
            If c.IsPinned Then parti.Add("fissato")
            If c.AuthorIsUploader Then parti.Add("del creatore")
            Dim intestazione As String = prefisso & If(String.IsNullOrWhiteSpace(c.Author), "Autore sconosciuto", c.Author)
            If Not String.IsNullOrWhiteSpace(c.Timestamp) Then intestazione &= ", " & c.Timestamp
            If c.LikeCount > 0 Then intestazione &= ", " & c.LikeCount.ToString("N0") & " mi piace"
            If c.Replies.Count > 0 Then intestazione &= If(c.Replies.Count = 1, ", una risposta", ", " & c.Replies.Count.ToString() & " risposte")
            Dim testo As String = If(c.Text, "").Replace(Environment.NewLine, " ").Replace(vbLf, " ")
            Dim etichetta As String = If(parti.Count > 0, String.Join(", ", parti) & ". ", "") & intestazione & ": " & testo
            If etichetta.Length > SOGLIA_TESTO_LUNGO Then etichetta = etichetta.Substring(0, SOGLIA_TESTO_LUNGO) & "..."
            Return etichetta
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.EtichettaCommento]: {ex.ToString()}")
            Return If(c Is Nothing, "", c.Text)
        End Try
    End Function

    ' Il commento e le sue risposte, una per voce.
    Private Sub MostraConversazione(principale As YoutubeComment)
        Try
            Dim voci As New List(Of YoutubeComment) From {principale}
            voci.AddRange(principale.Replies)
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo($"Conversazione di {principale.Author}, {principale.Replies.Count} risposte")
            For i As Integer = 0 To voci.Count - 1
                lista.aggiungiElemento(EtichettaCommento(voci(i), If(i = 0, "Commento di ", "Risposta di ")), i.ToString())
            Next
            Dim scelto As Func(Of YoutubeComment) = Function()
                                                        Dim n As Integer
                                                        If Integer.TryParse(lista.ottieniElemento(), n) AndAlso n >= 0 AndAlso n < voci.Count Then Return voci(n)
                                                        Return Nothing
                                                    End Function
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim c As YoutubeComment = scelto()
                                                      If c IsNot Nothing Then MostraCommentoCompleto(c)
                                                  End Sub
            lista.aggiungiOpzione("Leggi il commento per intero", Sub(s, e)
                                                                       Dim c As YoutubeComment = scelto()
                                                                       If c IsNot Nothing Then MostraCommentoCompleto(c)
                                                                   End Sub)
            lista.aggiungiOpzione("Copia il testo del commento", Sub(s, e)
                                                                     Dim c As YoutubeComment = scelto()
                                                                     If c IsNot Nothing Then CopiaTesto(c.Text, "Commento copiato negli appunti.")
                                                                 End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.MostraConversazione]: {ex.ToString()}")
        End Try
    End Sub

    Private Function CommentoSelezionato() As YoutubeComment
        Try
            If listaCommenti Is Nothing OrElse listaCommenti.IsDisposed OrElse risultatiCommentiCorrenti Is Nothing Then Return Nothing
            Dim ids As List(Of String) = listaCommenti.ottieniElementi()
            If ids Is Nothing OrElse ids.Count = 0 OrElse String.IsNullOrEmpty(ids(0)) OrElse Not ids(0).StartsWith("C") Then Return Nothing
            Dim n As Integer
            If Integer.TryParse(ids(0).Substring(1), n) AndAlso n >= 0 AndAlso n < risultatiCommentiCorrenti.Count Then Return risultatiCommentiCorrenti(n)
        Catch
        End Try
        Return Nothing
    End Function

    Private Sub OpzioneLeggiCommentoPerIntero()
        Dim c As YoutubeComment = CommentoSelezionato()
        If c IsNot Nothing Then MostraCommentoCompleto(c)
    End Sub

    Private Sub OpzioneCopiaCommento()
        Dim c As YoutubeComment = CommentoSelezionato()
        If c IsNot Nothing Then CopiaTesto(c.Text, "Commento copiato negli appunti.")
    End Sub

    ' Cambia l'ordine dei commenti e li ricarica.
    Private Sub OpzioneCambiaOrdineCommenti()
        Try
            _ordineCommenti = If(_ordineCommenti = "new", "top", "new")
            If listaCommenti IsNot Nothing AndAlso Not listaCommenti.IsDisposed Then listaCommenti.abbandona()
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia(If(_ordineCommenti = "new", "Caricamento dei commenti più recenti...", "Caricamento dei commenti più rilevanti..."))
            AvviaThread(AddressOf LavoroVisualizzaCommenti)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneCambiaOrdineCommenti]: {ex.ToString()}")
        End Try
    End Sub

#End Region

End Class
