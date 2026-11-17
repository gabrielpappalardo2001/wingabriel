Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' Menu "Nel Quotidiano": l'elenco degli articoli di nelquotidiano.news raccolti sul sito, dal più recente. Mentre si scarica l'elenco
''' c'è il WGProcessoInCorso; con Invio su un articolo se ne scarica il testo (di nuovo con il WGProcessoInCorso) e si legge in un WGCampo.
''' </summary>
Public Class MenuNelQuotidiano

    Private Const TITOLO_MENU As String = "Nel Quotidiano"

    Private ReadOnly _logger As New Logger()
    Private _lista As WGMenuLista
    Private _campoArticolo As WGCampo
    Private _articoli As New Dictionary(Of String, NelQuotidianoApi.VoceArticolo)(StringComparer.Ordinal)

    ' Il menu di Nel Quotidiano: l'elenco degli articoli e la configurazione.
    Public Sub Avvia()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Elenco articoli", AddressOf ApriElencoArticoli)
            menu.aggiungi("Configurazione", AddressOf MostraConfigurazione)
            menu.avvia(TITOLO_MENU, True)
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.Avvia: " & ex.ToString())
            Messaggio("Non è stato possibile aprire Nel Quotidiano.")
        End Try
    End Sub

    ' Scarica l'elenco degli articoli e lo mostra. Si apre anche direttamente, dalla notifica di un nuovo articolo.
    Public Sub ApriElencoArticoli()
        Try
            Dim elenco As List(Of NelQuotidianoApi.VoceArticolo) = ScaricaElenco()
            If elenco Is Nothing Then Return
            If elenco.Count = 0 Then
                Messaggio("Non ci sono ancora articoli.")
                Return
            End If
            MostraElenco(elenco)
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.ApriElencoArticoli: " & ex.ToString())
            Messaggio("Non è stato possibile aprire gli articoli di Nel Quotidiano.")
        End Try
    End Sub

#Region "Elenco"

    ' Scarica l'elenco con il WGProcessoInCorso. Nothing se è successo un errore (già detto all'utente).
    Private Function ScaricaElenco() As List(Of NelQuotidianoApi.VoceArticolo)
        Dim risultato As List(Of NelQuotidianoApi.VoceArticolo) = Nothing
        Dim errore As String = ""
        Dim proc As New WGProcessoInCorso()
        proc.Avvia("Sto scaricando l'elenco degli articoli, attendi", False)
        Try
            risultato = Task.Run(Function() NelQuotidianoApi.ElencaAsync()).GetAwaiter().GetResult()
        Catch ex As Exception
            errore = MotivoErrore(ex)
            _logger.ScriviLog("MenuNelQuotidiano.ScaricaElenco: " & ex.ToString())
            risultato = Nothing
        Finally
            Try
                proc.Fine()
            Catch exFine As Exception
                _logger.ScriviLog("MenuNelQuotidiano.ScaricaElenco fine: " & exFine.Message)
            End Try
        End Try
        If risultato Is Nothing Then
            Messaggio(If(errore.Length > 0, errore, "Non è stato possibile scaricare l'elenco degli articoli. Controlla la connessione a internet e riprova."))
        End If
        Return risultato
    End Function

    Private Sub MostraElenco(elenco As List(Of NelQuotidianoApi.VoceArticolo))
        Try
            ChiudiElenco()
            _articoli.Clear()
            Dim voci As New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each a As NelQuotidianoApi.VoceArticolo In elenco
                Dim etichetta As String = EtichettaArticolo(a)
                If voci.ContainsKey(etichetta) Then etichetta &= " (" & a.Id.ToString(CultureInfo.InvariantCulture) & ")"
                voci.Add(etichetta, a.Id.ToString(CultureInfo.InvariantCulture))
                _articoli(a.Id.ToString(CultureInfo.InvariantCulture)) = a
            Next

            _lista = New WGMenuLista()
            _lista.ciclono = True
            _lista.selezioneMenu = True
            _lista.multiselezioneConsentita = False
            _lista.CaricaElementiMassivi(voci)
            AddHandler _lista.ElementoSelezionato, AddressOf ArticoloSelezionato
            AddHandler _lista.escPremuto, AddressOf ElencoEsc
            _lista.impostaTitolo(TITOLO_MENU)
            _lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.MostraElenco: " & ex.ToString())
            Messaggio("Non è stato possibile mostrare l'elenco degli articoli.")
        End Try
    End Sub

    Private Shared Function EtichettaArticolo(a As NelQuotidianoApi.VoceArticolo) As String
        Dim titolo As String = If(String.IsNullOrWhiteSpace(a.Titolo), "Senza titolo", a.Titolo.Trim())
        If a.Data = DateTime.MinValue Then Return titolo
        Return titolo & ", " & a.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
    End Function

    Private Sub ElencoEsc(mittente As Object, e As EventArgs)
        ChiudiElenco()
    End Sub

    Private Sub ChiudiElenco()
        Try
            If _lista IsNot Nothing Then
                RemoveHandler _lista.ElementoSelezionato, AddressOf ArticoloSelezionato
                RemoveHandler _lista.escPremuto, AddressOf ElencoEsc
                _lista.abbandona()
                _lista = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.ChiudiElenco: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Configurazione"

    ' Il menu della configurazione: per ora la scelta dell'avviso per i nuovi articoli.
    Private Sub MostraConfigurazione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Avviso quando viene pubblicato un nuovo articolo, " & If(NelQuotidianoAvvisi.Attivo, "attivo", "non attivo"), AddressOf SceltaAvviso)
            menu.avvia("Configurazione", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.MostraConfigurazione: " & ex.ToString())
            Messaggio("Non è stato possibile aprire la configurazione.")
        End Try
    End Sub

    Private Sub SceltaAvviso()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Avviso attivo", Sub() ImpostaAvviso(True))
            menu.aggiungi("Avviso non attivo", Sub() ImpostaAvviso(False))
            menu.posiziona(If(NelQuotidianoAvvisi.Attivo, 0, 1))
            menu.avvia("Avviso quando viene pubblicato un nuovo articolo", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.SceltaAvviso: " & ex.ToString())
            Messaggio("Non è stato possibile aprire la scelta.")
        End Try
    End Sub

    Private Sub ImpostaAvviso(attivo As Boolean)
        Try
            NelQuotidianoAvvisi.Imposta(attivo)
            Messaggio(If(attivo, "Verrai avvisato quando viene pubblicato un nuovo articolo.", "Non verrai più avvisato dei nuovi articoli."))
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.ImpostaAvviso: " & ex.ToString())
            Messaggio("Non è stato possibile salvare la scelta.")
        End Try
    End Sub

#End Region

#Region "Lettura di un articolo"

    Private Sub ArticoloSelezionato(id As String)
        Try
            Dim voce As NelQuotidianoApi.VoceArticolo = Nothing
            If Not _articoli.TryGetValue(id, voce) Then Return
            Dim articolo As NelQuotidianoApi.Articolo = ScaricaArticolo(voce)
            If articolo Is Nothing Then Return
            LeggiArticolo(articolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.ArticoloSelezionato: " & ex.ToString())
            Messaggio("Non è stato possibile aprire l'articolo.")
        End Try
    End Sub

    ' Scarica il testo con il WGProcessoInCorso. Nothing se è successo un errore o l'articolo non c'è più (già detto all'utente).
    Private Function ScaricaArticolo(voce As NelQuotidianoApi.VoceArticolo) As NelQuotidianoApi.Articolo
        Dim risultato As NelQuotidianoApi.Articolo = Nothing
        Dim errore As String = ""
        Dim proc As New WGProcessoInCorso()
        proc.Avvia("Sto scaricando l'articolo, attendi", False)
        Try
            Dim id As Long = voce.Id
            risultato = Task.Run(Function() NelQuotidianoApi.LeggiAsync(id)).GetAwaiter().GetResult()
        Catch ex As Exception
            errore = MotivoErrore(ex)
            _logger.ScriviLog("MenuNelQuotidiano.ScaricaArticolo: " & ex.ToString())
            risultato = Nothing
        Finally
            Try
                proc.Fine()
            Catch exFine As Exception
                _logger.ScriviLog("MenuNelQuotidiano.ScaricaArticolo fine: " & exFine.Message)
            End Try
        End Try
        If risultato Is Nothing Then
            Messaggio(If(errore.Length > 0, errore, "Questo articolo non è più disponibile."))
        End If
        Return risultato
    End Function

    ' Il testo si legge in un WGCampo di sola lettura; Esc lo chiude e si torna all'elenco.
    Private Sub LeggiArticolo(articolo As NelQuotidianoApi.Articolo)
        Try
            ChiudiCampo()
            Dim testo As New StringBuilder()
            Dim intestazione As String = ""
            If Not String.IsNullOrWhiteSpace(articolo.Autore) Then intestazione = articolo.Autore.Trim()
            If articolo.Data <> DateTime.MinValue Then
                intestazione = (If(intestazione.Length > 0, intestazione & ", ", "")) & articolo.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            End If
            If intestazione.Length > 0 Then testo.Append(intestazione).Append(vbCrLf).Append(vbCrLf)
            testo.Append(articolo.Testo.Replace(vbCr, "").Replace(vbLf, vbCrLf))

            _campoArticolo = New WGCampo()
            AddHandler _campoArticolo.chiusura, AddressOf CampoArticolo_Chiusura
            _campoArticolo.init(If(String.IsNullOrWhiteSpace(articolo.Titolo), "Articolo", articolo.Titolo.Trim()), testo.ToString(), True, True, False, False)
            _campoArticolo.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.LeggiArticolo: " & ex.ToString())
            Messaggio("Non riesco ad aprire il testo dell'articolo.")
        End Try
    End Sub

    Private Sub CampoArticolo_Chiusura(mittente As Object, e As EventArgs)
        _campoArticolo = Nothing
    End Sub

    Private Sub ChiudiCampo()
        Try
            If _campoArticolo IsNot Nothing Then
                RemoveHandler _campoArticolo.chiusura, AddressOf CampoArticolo_Chiusura
                _campoArticolo.chiudi()
                _campoArticolo = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuNelQuotidiano.ChiudiCampo: " & ex.ToString())
        End Try
    End Sub

#End Region

    Private Shared Function MotivoErrore(ex As Exception) As String
        Dim piatto As Exception = ex
        Dim aggregato As AggregateException = TryCast(ex, AggregateException)
        If aggregato IsNot Nothing AndAlso aggregato.InnerException IsNot Nothing Then piatto = aggregato.InnerException
        If TypeOf piatto Is InvalidOperationException Then Return piatto.Message
        Return ""
    End Function

    Private Shared Sub Messaggio(testo As String)
        Dim msg As New WGMessaggi()
        msg.Messaggio(testo)
        msg.ShowDialog()
    End Sub

End Class
