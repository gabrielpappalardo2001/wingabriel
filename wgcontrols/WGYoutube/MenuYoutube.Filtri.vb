' ============================================================================================
' MenuYoutube - filtri della ricerca
' ============================================================================================
'
' Le ricerche di video hanno i filtri di YouTube: quando è stato pubblicato il video (ultima ora, oggi, questa
' settimana, questo mese, quest'anno) e le funzioni (solo dirette, con sottotitoli, alta definizione, 4K, Creative
' Commons), che applica YouTube stesso; in più "Nascondi gli Short", che si applica sui risultati. Il filtro per
' durata e l'ordinamento c'erano già e restano.
' I filtri predefiniti si scelgono in Impostazioni, Ordinamento e filtri, "Filtri predefiniti della ricerca"; dall'elenco
' dei risultati, nelle opzioni, "Filtri" li cambia per la ricerca in corso e la rifà.
' Qui c'è anche il filtro degli Short per il controllo dei nuovi video (Impostazioni, Iscrizioni e nuovi video).
' ============================================================================================
Imports System.Collections.Generic
Imports wgcontrols

Partial Public Class MenuYoutube

    Private _filtriRicerca As New FiltriRicerca()
    ' Vero se la lista dei contenuti mostra i risultati di una ricerca dentro un canale o una playlist: ordinamento e
    ' filtro durata devono rifare quella ricerca, non riaprire tutti i video.
    Private _inRicercaInterna As Boolean

    Private Const ChiaveFiltroData As String = "youtube_filtro_data"
    Private Const ChiaveFiltroDirette As String = "youtube_filtro_dirette"
    Private Const ChiaveFiltroSottotitoli As String = "youtube_filtro_sottotitoli"
    Private Const ChiaveFiltroAltaDefinizione As String = "youtube_filtro_hd"
    Private Const ChiaveFiltroQuattroK As String = "youtube_filtro_4k"
    Private Const ChiaveFiltroCreativeCommons As String = "youtube_filtro_cc"
    Private Const ChiaveFiltroShort As String = "youtube_nascondi_short"
    Private Const ChiaveShortNuoviVideo As String = "youtube_ignora_short_nuovi_video"

#Region "I filtri salvati"

    Private Shared Function LeggiBool(config As WGConfig, chiave As String) As Boolean
        Try
            Return String.Equals(If(config.LeggiValore(chiave), "").Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>I filtri predefiniti delle ricerche di video, dalle impostazioni.</summary>
    Private Function LeggiFiltriPredefiniti() As FiltriRicerca
        Dim f As New FiltriRicerca()
        Try
            Dim config As New WGConfig()
            Dim data As Integer
            If Integer.TryParse(If(config.LeggiValore(ChiaveFiltroData), "").Trim(), data) AndAlso data >= 0 AndAlso data <= 5 Then f.Data = CType(data, FiltroData)
            f.SoloDirette = LeggiBool(config, ChiaveFiltroDirette)
            f.ConSottotitoli = LeggiBool(config, ChiaveFiltroSottotitoli)
            f.AltaDefinizione = LeggiBool(config, ChiaveFiltroAltaDefinizione)
            f.QuattroK = LeggiBool(config, ChiaveFiltroQuattroK)
            f.CreativeCommons = LeggiBool(config, ChiaveFiltroCreativeCommons)
            f.NascondiShort = LeggiBool(config, ChiaveFiltroShort)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.LeggiFiltriPredefiniti]: {ex.ToString()}")
        End Try
        Return f
    End Function

    ''' <summary>Vero se tra i nuovi video non vanno segnalati gli Short (si riconoscono dal feed RSS dei canali).</summary>
    Public Shared Function IgnoraShortNeiNuoviVideo() As Boolean
        Try
            Return LeggiBool(New WGConfig(), ChiaveShortNuoviVideo)
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "Filtri della ricerca in corso"

    Private Shared Function NomeData(d As FiltroData) As String
        Select Case d
            Case FiltroData.UltimaOra : Return "ultima ora"
            Case FiltroData.Oggi : Return "oggi"
            Case FiltroData.QuestaSettimana : Return "questa settimana"
            Case FiltroData.QuestoMese : Return "questo mese"
            Case FiltroData.QuestoAnno : Return "quest'anno"
            Case Else : Return "qualsiasi"
        End Select
    End Function

    Private Shared Function SiNo(valore As Boolean) As String
        Return If(valore, "sì", "no")
    End Function

    ' Opzione "Filtri" dell'elenco dei risultati.
    Private Sub OpzioneFiltriRicerca()
        Try
            Dim f As FiltriRicerca = _filtriRicerca
            Dim m As New WGMenu()
            m.aggiungi("Data di pubblicazione: " & NomeData(f.Data), AddressOf OpzioneFiltroDataRicerca)
            m.aggiungi("Solo dirette: " & SiNo(f.SoloDirette), Sub() CambiaFiltriRicerca(Sub(n) n.SoloDirette = Not n.SoloDirette))
            m.aggiungi("Con sottotitoli: " & SiNo(f.ConSottotitoli), Sub() CambiaFiltriRicerca(Sub(n) n.ConSottotitoli = Not n.ConSottotitoli))
            m.aggiungi("Alta definizione: " & SiNo(f.AltaDefinizione), Sub() CambiaFiltriRicerca(Sub(n) n.AltaDefinizione = Not n.AltaDefinizione))
            m.aggiungi("4K: " & SiNo(f.QuattroK), Sub() CambiaFiltriRicerca(Sub(n) n.QuattroK = Not n.QuattroK))
            m.aggiungi("Creative Commons: " & SiNo(f.CreativeCommons), Sub() CambiaFiltriRicerca(Sub(n) n.CreativeCommons = Not n.CreativeCommons))
            m.aggiungi("Nascondi gli Short, i video sotto il minuto: " & SiNo(f.NascondiShort), Sub() CambiaFiltriRicerca(Sub(n) n.NascondiShort = Not n.NascondiShort))
            m.aggiungi("Azzera tutti i filtri", Sub() CambiaFiltriRicerca(Sub(n)
                                                                              n.Data = FiltroData.Qualsiasi
                                                                              n.SoloDirette = False
                                                                              n.ConSottotitoli = False
                                                                              n.AltaDefinizione = False
                                                                              n.QuattroK = False
                                                                              n.CreativeCommons = False
                                                                              n.NascondiShort = False
                                                                          End Sub))
            m.avvia("Filtri della ricerca", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneFiltriRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub OpzioneFiltroDataRicerca()
        Try
            Dim m As New WGMenu()
            For Each d As FiltroData In New FiltroData() {FiltroData.Qualsiasi, FiltroData.UltimaOra, FiltroData.Oggi, FiltroData.QuestaSettimana, FiltroData.QuestoMese, FiltroData.QuestoAnno}
                Dim scelta As FiltroData = d
                m.aggiungi(StringaPrimaMaiuscola(NomeData(d)) & If(_filtriRicerca.Data = d, " [attuale]", ""), Sub() CambiaFiltriRicerca(Sub(n) n.Data = scelta))
            Next
            m.posiziona(CInt(_filtriRicerca.Data))
            m.avvia("Data di pubblicazione", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.OpzioneFiltroDataRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Shared Function StringaPrimaMaiuscola(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return testo
        Return Char.ToUpper(testo(0)) & testo.Substring(1)
    End Function

    ' Cambia i filtri della ricerca in corso e la rifà con quelli nuovi.
    Private Sub CambiaFiltriRicerca(modifica As Action(Of FiltriRicerca))
        Try
            Dim nuovi As FiltriRicerca = _filtriRicerca.Copia()
            modifica(nuovi)
            _filtriRicerca = nuovi
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Applicazione dei filtri: " & nuovi.Descrizione())
            AvviaThread(AddressOf ThreadApplicaFiltriRicerca)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.CambiaFiltriRicerca]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub ThreadApplicaFiltriRicerca()
        Try
            risultatiRicercaCorrente = searcher.IniziaRicercaPaginata(SESSION_RICERCA, queryRicerca, DIMENSIONE_PAGINA_RICERCA, _tipoRicercaCorrente, _sortByRicerca, _minDurataRicerca, _maxDurataRicerca, _filtriRicerca)
            EseguiSuThreadUI(AddressOf UI_RicaricaListaRicercaDopoFiltri)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.ThreadApplicaFiltriRicerca]: {ex.ToString()}")
            EseguiSuThreadUI(AddressOf UI_ChiudiProcesso)
        End Try
    End Sub

    Private Sub UI_RicaricaListaRicercaDopoFiltri()
        Try
            ChiudiProcessoInCorso()
            If elencorisultati Is Nothing OrElse elencorisultati.IsDisposed Then Return
            If risultatiRicercaCorrente Is Nothing OrElse risultatiRicercaCorrente.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun risultato con questi filtri: " & _filtriRicerca.Descrizione() & ".")
                msg.ShowDialog()
                Return
            End If
            AggiornaListaRisultati(elencorisultati, risultatiRicercaCorrente)
            AggiornaOpzionePaginaPrecedente()
            elencorisultati.selezionaElemento(0)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_RicaricaListaRicercaDopoFiltri]: {ex.ToString()}")
        End Try
    End Sub

    ' Dopo un ordinamento o un filtro durata la lista dei contenuti si ricarica: se è il risultato di una ricerca dentro un
    ' canale o una playlist si rifà quella ricerca, altrimenti si riaprono i video del canale o della playlist.
    Private Function CaricaContenutiCorrenti() As List(Of YoutubeSearchResult)
        If _inRicercaInterna AndAlso Not String.IsNullOrWhiteSpace(_queryRicercaInterna) AndAlso elementoSelezionatoCorrente IsNot Nothing Then
            If _contestoRicercaInterna = "playlist" Then
                Return searcher.IniziaRicercaPlaylistPaginata(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, _queryRicercaInterna, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
            End If
            Return searcher.IniziaRicercaCanalePaginata(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, _queryRicercaInterna, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
        End If
        If elementoSelezionatoCorrente IsNot Nothing AndAlso elementoSelezionatoCorrente.ItemType = YoutubeItemType.Channel Then
            Return searcher.IniziaVideoCanalePaginato(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
        End If
        Return searcher.IniziaVideoPlaylistPaginato(SESSION_CONTENUTI, elementoSelezionatoCorrente.Id, DIMENSIONE_PAGINA_RICERCA, _sortByContenuti, _minDurataContenuti, _maxDurataContenuti)
    End Function

#End Region

#Region "Impostazioni"

    ' Impostazioni, Ordinamento e filtri, "Filtri predefiniti della ricerca".
    Private Sub UI_MenuFiltriPredefiniti()
        Try
            Dim f As FiltriRicerca = LeggiFiltriPredefiniti()
            Dim m As New WGMenu()
            m.aggiungi("Data di pubblicazione: " & NomeData(f.Data), AddressOf UI_MenuFiltroDataPredefinito)
            m.aggiungi("Solo dirette: " & SiNo(f.SoloDirette), Sub() AlternaFiltroPredefinito(ChiaveFiltroDirette, "Solo dirette"))
            m.aggiungi("Con sottotitoli: " & SiNo(f.ConSottotitoli), Sub() AlternaFiltroPredefinito(ChiaveFiltroSottotitoli, "Con sottotitoli"))
            m.aggiungi("Alta definizione: " & SiNo(f.AltaDefinizione), Sub() AlternaFiltroPredefinito(ChiaveFiltroAltaDefinizione, "Alta definizione"))
            m.aggiungi("4K: " & SiNo(f.QuattroK), Sub() AlternaFiltroPredefinito(ChiaveFiltroQuattroK, "4K"))
            m.aggiungi("Creative Commons: " & SiNo(f.CreativeCommons), Sub() AlternaFiltroPredefinito(ChiaveFiltroCreativeCommons, "Creative Commons"))
            m.aggiungi("Nascondi gli Short, i video sotto il minuto: " & SiNo(f.NascondiShort), Sub() AlternaFiltroPredefinito(ChiaveFiltroShort, "Nascondi gli Short"))
            m.avvia("Filtri predefiniti della ricerca", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuFiltriPredefiniti]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AlternaFiltroPredefinito(chiave As String, nome As String)
        Try
            Dim config As New WGConfig()
            Dim nuovo As Boolean = Not LeggiBool(config, chiave)
            UI_SalvaImpostazioneYoutubeConMessaggio(config, chiave, If(nuovo, "true", "false"), nome & If(nuovo, ": attivo", ": spento") & " per le prossime ricerche di video.")
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.AlternaFiltroPredefinito]: {ex.ToString()}")
        End Try
    End Sub

    Private Sub UI_MenuFiltroDataPredefinito()
        Try
            Dim config As New WGConfig()
            Dim attuale As FiltriRicerca = LeggiFiltriPredefiniti()
            Dim m As New WGMenu()
            For Each d As FiltroData In New FiltroData() {FiltroData.Qualsiasi, FiltroData.UltimaOra, FiltroData.Oggi, FiltroData.QuestaSettimana, FiltroData.QuestoMese, FiltroData.QuestoAnno}
                Dim scelta As FiltroData = d
                m.aggiungi(StringaPrimaMaiuscola(NomeData(d)) & If(attuale.Data = d, " [attuale]", ""),
                           Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, ChiaveFiltroData, CInt(scelta).ToString(), "Data di pubblicazione per le prossime ricerche di video: " & NomeData(scelta) & "."))
            Next
            m.posiziona(CInt(attuale.Data))
            m.avvia("Data di pubblicazione", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuFiltroDataPredefinito]: {ex.ToString()}")
        End Try
    End Sub

    ' Impostazioni, Iscrizioni e nuovi video, "Short nei nuovi video".
    Private Sub UI_MenuShortNuoviVideo()
        Try
            Dim config As New WGConfig()
            Dim ignora As Boolean = IgnoraShortNeiNuoviVideo()
            Dim m As New WGMenu()
            m.aggiungi("Segnala anche gli Short" & If(Not ignora, " [attuale]", ""),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, ChiaveShortNuoviVideo, "false", "Tra i nuovi video verranno segnalati anche gli Short."))
            m.aggiungi("Ignora gli Short" & If(ignora, " [attuale]", ""),
                       Sub() UI_SalvaImpostazioneYoutubeConMessaggio(config, ChiaveShortNuoviVideo, "true", "Gli Short non verranno segnalati tra i nuovi video."))
            m.posiziona(If(ignora, 1, 0))
            m.avvia("Short nei nuovi video", True)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [MenuYoutube.UI_MenuShortNuoviVideo]: {ex.ToString()}")
        End Try
    End Sub

#End Region

End Class
