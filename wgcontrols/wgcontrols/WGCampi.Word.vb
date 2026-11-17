Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Linq
Imports System.Windows.Forms

Partial Public Class Campi

    Public Sub AggiungiWord(titolo As String, Optional id As String = "", Optional obbligatorio As Boolean = False, Optional solaLettura As Boolean = False)
        Try
            If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return
            _motoreGrafico.AggiungiCampoWord(titolo, id, obbligatorio, solaLettura)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiWord: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiAzioneUscita(testoVoce As String, evento As EventHandler)
        Try
            If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return
            _motoreGrafico.AggiungiAzioneUscita(testoVoce, evento)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiAzioneUscita: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaAzioniUscita()
        Try
            If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return
            _motoreGrafico.SvuotaAzioniUscita()
        Catch ex As Exception
            _log.ScriviLog("Campi.SvuotaAzioniUscita: " & ex.Message)
        End Try
    End Sub

    Public Property EtichettaAnnullaUscita As String
        Get
            Try
                If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return ""
                Return _motoreGrafico.EtichettaAnnullaUscita
            Catch ex As Exception
                _log.ScriviLog("Campi.EtichettaAnnullaUscita Get: " & ex.Message)
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return
                _motoreGrafico.EtichettaAnnullaUscita = value
            Catch ex As Exception
                _log.ScriviLog("Campi.EtichettaAnnullaUscita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property TitoloMenuUscita As String
        Get
            Try
                If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return ""
                Return _motoreGrafico.TitoloMenuUscita
            Catch ex As Exception
                _log.ScriviLog("Campi.TitoloMenuUscita Get: " & ex.Message)
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return
                _motoreGrafico.TitoloMenuUscita = value
            Catch ex As Exception
                _log.ScriviLog("Campi.TitoloMenuUscita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Function RecuperaControlloWord(id As String) As WGWord
        Try
            If _motoreGrafico Is Nothing OrElse _motoreGrafico.IsDisposed Then Return Nothing
            Return _motoreGrafico.ControlloWordPerId(id)
        Catch ex As Exception
            _log.ScriviLog("Campi.RecuperaControlloWord: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function RecuperaContenutoPosta(id As String) As WGContenutoPosta
        Try
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return New WGContenutoPosta()
            Return controllo.GeneraContenutoPosta()
        Catch ex As Exception
            _log.ScriviLog("Campi.RecuperaContenutoPosta: " & ex.Message)
            Return New WGContenutoPosta()
        End Try
    End Function

    Public Function WordVuoto(id As String) As Boolean
        Try
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return True
            Return controllo.DocumentoVuoto()
        Catch ex As Exception
            _log.ScriviLog("Campi.WordVuoto: " & ex.Message)
            Return True
        End Try
    End Function

    Public Sub CaricaWordDaFile(id As String, percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            If Not System.IO.File.Exists(percorso) Then Return
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            controllo.LoadDocx(percorso)
        Catch ex As Exception
            _log.ScriviLog("Campi.CaricaWordDaFile: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiVoceMenuDocumento(id As String, testoVoce As String, evento As EventHandler)
        Try
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            controllo.AggiungiVoceMenuDocumento(testoVoce, evento)
        Catch ex As Exception
            _log.ScriviLog("Campi.AggiungiVoceMenuDocumento: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaVociMenuDocumento(id As String)
        Try
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            controllo.SvuotaVociMenuDocumento()
        Catch ex As Exception
            _log.ScriviLog("Campi.SvuotaVociMenuDocumento: " & ex.Message)
        End Try
    End Sub

    Public Function CaricaWordDaHtml(id As String, html As String, Optional immaginiInline As Dictionary(Of String, Byte()) = Nothing, Optional titolo As String = "") As Boolean
        Try
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return False
            Dim esito As Boolean = controllo.ImportaHtml(html, titolo, immaginiInline)

            ' ImportaHtml ricostruisce il contenuto del documento e può far perdere il
            ' focus assegnato in precedenza da Avvia(): lo ripristiniamo esplicitamente
            ' sul campo appena caricato. Lo facciamo con BeginInvoke, non subito, perché
            ' chiamare Focus() nello stesso istante in cui il controllo sta ancora
            ' rifacendo il proprio layout spesso non ha effetto.
            If controllo.IsHandleCreated Then
                controllo.BeginInvoke(New MethodInvoker(Sub()
                                                             Try
                                                                 controllo.Select()
                                                                 controllo.Focus()
                                                             Catch
                                                             End Try
                                                         End Sub))
            Else
                controllo.Select()
                controllo.Focus()
            End If

            Return esito
        Catch ex As Exception
            _log.ScriviLog("Campi.CaricaWordDaHtml: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SalvaWordSuFile(id As String, percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim controllo As WGWord = RecuperaControlloWord(id)
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            controllo.SaveDocx(percorso)
        Catch ex As Exception
            _log.ScriviLog("Campi.SalvaWordSuFile: " & ex.Message)
        End Try
    End Sub

End Class

Partial Friend Class CampiFormInterno

    Private Class AzioneUscita
        Private ReadOnly _padre As CampiFormInterno
        Private ReadOnly _nome As String
        Private ReadOnly _evento As EventHandler
        Private ReadOnly _registro As Logger

        Public Sub New(padre As CampiFormInterno, nome As String, evento As EventHandler)
            _padre = padre
            _nome = nome
            _evento = evento
            _registro = New Logger()
        End Sub

        Public ReadOnly Property Nome As String
            Get
                Return _nome
            End Get
        End Property

        Public ReadOnly Property Evento As EventHandler
            Get
                Return _evento
            End Get
        End Property

        Public Sub Esegui(sender As Object, e As EventArgs)
            Try
                If _padre Is Nothing OrElse _padre.IsDisposed Then Return
                _padre.EseguiAzioneUscita(Me)
            Catch ex As Exception
                _registro.ScriviLog("AzioneUscita.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Friend Property EtichettaAnnullaUscita As String = "Continua a scrivere"
    Friend Property TitoloMenuUscita As String = "Cosa vuoi fare?"

    Private _azioniUscita As New List(Of AzioneUscita)()
    Private _menuUscita As WGMenu
    Private _indiceRipristinoUscita As Integer = -1

    Friend Sub AggiungiAzioneUscita(testoVoce As String, evento As EventHandler)
        Try
            If String.IsNullOrWhiteSpace(testoVoce) OrElse evento Is Nothing Then Return
            _azioniUscita.Add(New AzioneUscita(Me, testoVoce, evento))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiAzioneUscita: " & ex.Message)
        End Try
    End Sub

    Friend Sub SvuotaAzioniUscita()
        Try
            _azioniUscita.Clear()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SvuotaAzioniUscita: " & ex.Message)
        End Try
    End Sub

    Friend Sub MostraMenuUscita()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf MostraMenuUscita))
            Return
        End If
        Try
            If _azioniUscita Is Nothing OrElse _azioniUscita.Count = 0 Then Return
            If _menuUscita IsNot Nothing Then Return

            SalvaTuttiIValori()
            _indiceRipristinoUscita = IndiceCampoAttivo()

            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing AndAlso datiWord.ControlloWord IsNot Nothing AndAlso Not datiWord.ControlloWord.IsDisposed Then
                Try
                    datiWord.ControlloWord.disattivahook()
                Catch exHook As Exception
                    _logger.ScriviLog("CampiFormInterno.MostraMenuUscita (hook): " & exHook.Message)
                End Try
            End If

            _menuUscita = New WGMenu()
            AddHandler _menuUscita.abbandonato, AddressOf MenuUscitaAbbandonato
            For Each azione As AzioneUscita In _azioniUscita
                _menuUscita.aggiungi(azione.Nome, AddressOf azione.Esegui)
            Next
            _menuUscita.aggiungi(If(String.IsNullOrWhiteSpace(EtichettaAnnullaUscita), "Continua a scrivere", EtichettaAnnullaUscita), AddressOf AnnullaMenuUscita)
            _menuUscita.avvia(If(String.IsNullOrWhiteSpace(TitoloMenuUscita), "Cosa vuoi fare?", TitoloMenuUscita), True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraMenuUscita: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiAzioneUscita(azione As AzioneUscita)
        Try
            If _menuUscita IsNot Nothing Then
                Try
                    RemoveHandler _menuUscita.abbandonato, AddressOf MenuUscitaAbbandonato
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.EseguiAzioneUscita (rimozione): " & exRimozione.Message)
                End Try
            End If
            _menuUscita = Nothing
            _indiceRipristinoUscita = -1
            If azione Is Nothing OrElse azione.Evento Is Nothing Then Return
            SalvaTuttiIValori()

            _chiusoCorrettamente = True
            azione.Evento.Invoke(Me, EventArgs.Empty)
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiAzioneUscita: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuUscitaAbbandonato(sender As Object, e As EventArgs)
        Try
            If _menuUscita Is Nothing Then Return
            AnnullaMenuUscita(sender, e)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MenuUscitaAbbandonato: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaMenuUscita(sender As Object, e As EventArgs)
        Try
            If _menuUscita IsNot Nothing Then
                Try
                    RemoveHandler _menuUscita.abbandonato, AddressOf MenuUscitaAbbandonato
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.AnnullaMenuUscita (rimozione): " & exRimozione.Message)
                End Try
            End If
            _menuUscita = Nothing
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim indice As Integer = _indiceRipristinoUscita
            _indiceRipristinoUscita = -1
            If indice < 0 OrElse indice >= _listaCampi.Count Then indice = 0
            _indiceRientroDifferito = indice
            Try
                Me.BeginInvoke(New MethodInvoker(AddressOf RientraNelCampoDifferito))
            Catch exRinvio As Exception
                _logger.ScriviLog("CampiFormInterno.AnnullaMenuUscita (rinvio): " & exRinvio.Message)
                RientraNelCampoDifferito()
            End Try
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AnnullaMenuUscita: " & ex.Message)
        End Try
    End Sub

    Private _indiceRientroDifferito As Integer = -1

    Private Sub RientraNelCampoDifferito()
        Try
            Dim indice As Integer = _indiceRientroDifferito
            _indiceRientroDifferito = -1
            If indice < 0 Then Return
            AttivaCampoPerIndice(indice)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RientraNelCampoDifferito: " & ex.Message)
        End Try
    End Sub

    Friend Function ControlloWordPerId(id As String) As WGWord
        Try
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.EWord AndAlso x.Identificativo = id)
            If dati Is Nothing Then Return Nothing
            Return dati.ControlloWord
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ControlloWordPerId: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function CampoWordAttivo() As MetadatiCampo
        Try
            If _listaCampi Is Nothing OrElse _listaCampi.Count = 0 Then Return Nothing
            Dim controllo As WGWord = TryCast(Me.ActiveControl, WGWord)
            If controllo Is Nothing Then Return Nothing
            Return _listaCampi.FirstOrDefault(Function(x) x.EWord AndAlso x.ControlloWord Is controllo)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordAttivo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Friend Function CampoWordAttivoConFocus() As Boolean
        Try
            Dim dati As MetadatiCampo = CampoWordAttivo()
            If dati Is Nothing OrElse dati.ControlloWord Is Nothing Then Return False
            If dati.ControlloWord.IsDisposed Then Return False
            Return dati.ControlloWord.ContainsFocus
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordAttivoConFocus: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Function IndiceCampoAttivo() As Integer
        Try
            Dim datiWord As MetadatiCampo = CampoWordAttivo()
            If datiWord IsNot Nothing Then Return _listaCampi.IndexOf(datiWord)
            Dim casella As WGCampiMovimento = TryCast(Me.ActiveControl, WGCampiMovimento)
            If casella Is Nothing Then Return -1
            Return OttieniIndiceCampoDaControllo(casella)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.IndiceCampoAttivo: " & ex.Message)
            Return -1
        End Try
    End Function

    Private _indiceCampoRicordato As Integer = -1
    Private _uscitoDaVistaCampi As Boolean = False

    Private Sub GestisciVisibilitaCampiForm(sender As Object, e As EventArgs) Handles Me.VisibleChanged
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Not Me.Visible Then
                _uscitoDaVistaCampi = True
                SospendiCampiWord()
                Return
            End If

            If Not _uscitoDaVistaCampi Then Return
            _uscitoDaVistaCampi = False
            If Not Me.IsHandleCreated Then Return

            Try
                Me.BeginInvoke(New MethodInvoker(AddressOf RiattivaCampoCorrente))
            Catch exRinvio As Exception
                _logger.ScriviLog("CampiFormInterno.GestisciVisibilitaCampiForm (rinvio): " & exRinvio.Message)
                RiattivaCampoCorrente()
            End Try
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestisciVisibilitaCampiForm: " & ex.Message)
        End Try
    End Sub

    Friend Sub RicordaIndiceCampo(indice As Integer)
        Try
            If indice < 0 Then Return
            _indiceCampoRicordato = indice
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RicordaIndiceCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub SospendiCampiWord()
        Try
            If _listaCampi Is Nothing Then Return
            For Each dati As MetadatiCampo In _listaCampi
                If Not dati.EWord Then Continue For
                If dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Continue For
                Try
                    dati.ControlloWord.disattivahook()
                Catch exHook As Exception
                    _logger.ScriviLog("CampiFormInterno.SospendiCampiWord (hook): " & exHook.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SospendiCampiWord: " & ex.Message)
        End Try
    End Sub

    Friend Sub RiattivaCampoCorrente()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return
            If _listaCampi Is Nothing OrElse _listaCampi.Count = 0 Then Return
            If menuOpzioniAperto Then Return
            If _menuUscita IsNot Nothing Then Return
            If _messaggioErroreAttivo Then Return

            Dim indice As Integer = _indiceCampoRicordato
            If indice < 0 Then indice = IndiceCampoAttivo()
            If indice < 0 Then indice = 0
            If indice >= _listaCampi.Count Then indice = _listaCampi.Count - 1

            Dim dati As MetadatiCampo = _listaCampi(indice)

            _ultimoCampoWordAnnunciato = Nothing
            _tickUltimoAnnuncioWord = 0
            _ultimaCasellaAnnunciata = Nothing
            _tickUltimoAnnuncioCampo = 0

            AttivaCampoPerIndice(indice)

            If Not dati.EWord AndAlso dati.Controllo IsNot Nothing AndAlso Not dati.Controllo.IsDisposed Then
                LeggiCampoAttivo(dati.Controllo)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RiattivaCampoCorrente: " & ex.Message)
        End Try
    End Sub

    Private Delegate Sub DelegatoIndiceCampo(indice As Integer)
    Private Delegate Sub DelegatoCampoWord(editor As WGWord)
    Private Delegate Sub DelegatoCampoWordDirezione(editor As WGWord, direzione As Integer)

    Friend Sub AttivaCampoPerIndice(indice As Integer)
        Try
            If Me.IsDisposed OrElse _listaCampi Is Nothing OrElse _listaCampi.Count = 0 Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New DelegatoIndiceCampo(AddressOf AttivaCampoPerIndice), indice)
                Return
            End If
            If indice < 0 OrElse indice >= _listaCampi.Count Then Return

            Dim dati As MetadatiCampo = _listaCampi(indice)

            ' Vedi ForzaRipristinoFocusFinestra in WGCAmpi.vb: questo metodo viene invocato anche
            ' per rientrare in un campo dopo la chiusura di un form separato (es. il menu di
            ' uscita aperto da MostraMenuUscita/RientraNelCampoDifferito). Senza questa chiamata,
            ' Windows a volte non restituisce subito il focus reale a questa finestra pur
            ' rendendola di nuovo visibile, e il campo (Word compreso) resta apparentemente
            ' selezionato ma sordo alla tastiera. Stesso fix usato in chiudiOpzioni e
            ' RipristinaFocusDopoMessaggio.
            ForzaRipristinoFocusFinestra()

            If dati.EWord Then
                If dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Return
                _indiceCampoRicordato = indice
                Dim silenzioso As Boolean = dati.ControlloWord.ConsumaSoppressioneAnnuncioRientro()
                DisponiSoloCampoWord(indice)
                RipristinaFocusSuCampo(dati.ControlloWord)
                dati.ControlloWord.RiprendiFocus(False)
                Me.Invalidate()
                If Not silenzioso Then LeggiCampoWordAttivo(dati)
                Return
            End If

            Dim controlloTarget As WGCampiMovimento = dati.Controllo
            If controlloTarget Is Nothing OrElse controlloTarget.IsDisposed Then Return
            _indiceCampoRicordato = indice
            AggiornaVisibilitaCampi(controlloTarget)
            RipristinaFocusSuCampo(controlloTarget)
            controlloTarget.SelectAll()
            ApriElencoDateAlIngresso(controlloTarget)
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.AttivaCampoPerIndice (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AttivaCampoPerIndice: " & ex.Message)
        End Try
    End Sub

    Friend Sub AggiungiCampoWord(titolo As String, id As String, obbligatorio As Boolean, solaLettura As Boolean)
        Try
            Dim dati As New MetadatiCampo()
            dati.TitoloBase = titolo
            dati.Identificativo = id
            dati.Obbligatorio = obbligatorio
            dati.Tipo = TipoCampo.Word
            dati.EWord = True
            dati.EMultiplo = False
            dati.IdBaseMultiplo = id
            dati.LimiteMultiplo = 0

            Dim segnaposto As New WGCampiMovimento()
            segnaposto.Width = 350
            segnaposto.ReadOnly = True
            segnaposto.TabStop = False
            segnaposto.Visible = False
            segnaposto.AccessibleRole = AccessibleRole.None
            segnaposto.AccessibleName = Nothing
            dati.Controllo = segnaposto

            Dim editor As New WGWord()
            editor.ModalitaCampo = True
            editor.SolaLettura = solaLettura
            editor.TabStop = False
            editor.Visible = False
            editor.AccessibleRole = AccessibleRole.None
            editor.AccessibleName = Nothing

            Try
                editor.NuovoDocumento(If(String.IsNullOrWhiteSpace(titolo), "Messaggio", titolo))
            Catch exNuovo As Exception
                _logger.ScriviLog("CampiFormInterno.AggiungiCampoWord (NuovoDocumento): " & exNuovo.Message)
            End Try

            AddHandler editor.RichiestaCampoAdiacente, AddressOf CampoWordRichiedeAdiacente
            AddHandler editor.RichiestaConfermaCampo, AddressOf CampoWordRichiedeConferma
            AddHandler editor.RichiestaMenuUscita, AddressOf CampoWordRichiedeMenuUscita
            AddHandler editor.RichiestaDettatura, AddressOf CampoWordRichiedeDettatura
            AddHandler editor.GotFocus, AddressOf GestoreIngressoCampoWord
            AddHandler editor.LostFocus, AddressOf GestoreUscitaCampoWord

            editor.AggiungiVoceMenuDocumento("Riassumi il testo", AddressOf OpzioneRiassumiTestoWord)

            dati.ControlloWord = editor

            _listaCampi.Add(dati)
            _mappaIndiciCampi.Add(segnaposto, dati)
            Me.Controls.Add(segnaposto)
            Me.Controls.Add(editor)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.AggiungiCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Async Sub OpzioneRiassumiTestoWord(sender As Object, e As EventArgs)
        Try
            Dim dati As MetadatiCampo = CampoWordAttivo()
            If dati Is Nothing OrElse dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Return

            Dim contenuto As WGContenutoPosta = dati.ControlloWord.GeneraContenutoPosta()
            Dim testo As String = If(contenuto IsNot Nothing, contenuto.Testo, "")
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Il campo è vuoto, non c'è nulla da riassumere.")
                msgVuoto.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msgConfig As New WGMessaggi()
                msgConfig.Messaggio("Nessuna chiave API configurata per l'intelligenza artificiale. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msgConfig.ShowDialog()
                Return
            End If

            Dim processo As New WGProcessoInCorso()
            processo.Avvia("Sto elaborando il riassunto, attendere", False)

            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiTestoAsync(testo)

            processo.Fine()

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Non è stato possibile ottenere il riassunto: " & If(risultato IsNot Nothing, risultato.Item2, "errore sconosciuto"))
                msgErrore.ShowDialog()
                Return
            End If

            Dim campoRisultato As New WGCampo()
            campoRisultato.init("Riassunto", risultato.Item1, True, True, False, False)
            campoRisultato.avvia()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OpzioneRiassumiTestoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub GestoreIngressoCampoWord(sender As Object, e As EventArgs)
        Try
            If _messaggioErroreAttivo Then Return
            Dim editor As WGWord = TryCast(sender, WGWord)
            If editor Is Nothing OrElse editor.IsDisposed Then Return
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.EWord AndAlso x.ControlloWord Is editor)
            If dati Is Nothing Then Return
            Dim indice As Integer = _listaCampi.IndexOf(dati)
            If indice < 0 Then Return
            DisponiSoloCampoWord(indice)
            _editorWordConTastiera = editor
            AggiornaTastieraTouk()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreIngressoCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub GestoreUscitaCampoWord(sender As Object, e As EventArgs)
        Try
            If FuocoPassatoAllaTastieraTouchWord() Then
                Dim editorUscente As WGWord = TryCast(sender, WGWord)
                If editorUscente IsNot Nothing AndAlso Not editorUscente.IsDisposed AndAlso Me.IsHandleCreated Then
                    Me.BeginInvoke(New MethodInvoker(Sub() RiprendiFuocoCampoWord(editorUscente)))
                End If
                Return
            End If

            _editorWordConTastiera = Nothing
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.GestoreUscitaCampoWord: " & ex.Message)
        End Try
    End Sub

    Private _editorWordConTastiera As WGWord

    Private Sub MostraTastieraTouchPerWord(editor As WGWord)
        Try
            _editorWordConTastiera = editor
            AggiornaTastieraTouk()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.MostraTastieraTouchPerWord: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastieraTouchPerWord()
        Try
            _editorWordConTastiera = Nothing
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NascondiTastieraTouchPerWord: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Vero se il fuoco e' passato alla tastiera a schermo. Toccando un tasto l'editor perde il
    ''' fuoco pur restando quello su cui si scrive: senza questa verifica l'uscita dal campo
    ''' nasconderebbe la tastiera al primo tasto premuto.
    ''' </summary>
    Private Function FuocoPassatoAllaTastieraTouchWord() As Boolean
        Try
            If _tastieraTouch Is Nothing OrElse _tastieraTouch.IsDisposed Then Return False

            Dim formaOspite As Form = Me.FindForm()
            If formaOspite Is Nothing Then Return False

            Dim controlloAttivo As Control = formaOspite.ActiveControl
            While controlloAttivo IsNot Nothing
                If ReferenceEquals(controlloAttivo, _tastieraTouch) Then Return True
                If TypeOf controlloAttivo Is WGTastieraTouch Then Return True
                controlloAttivo = controlloAttivo.Parent
            End While

            Return False
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.FuocoPassatoAllaTastieraTouchWord: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RiprendiFuocoCampoWord(editor As WGWord)
        Try
            If editor Is Nothing OrElse editor.IsDisposed Then Return
            If Not editor.IsHandleCreated OrElse Not editor.Visible OrElse Not editor.Enabled Then Return
            If editor.Focused Then Return
            editor.Focus()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RiprendiFuocoCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnCarattereInseritoDaTastieraWord(carattere As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnCarattereInseritoDaTastieraWord), carattere)
                Return
            End If
            If String.IsNullOrEmpty(carattere) Then Return
            If _editorWordConTastiera Is Nothing OrElse _editorWordConTastiera.IsDisposed Then Return
            _editorWordConTastiera.InserisciCarattereConLettura(carattere(0))
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnCarattereInseritoDaTastieraWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnEliminaRichiestoDaTastieraWord()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnEliminaRichiestoDaTastieraWord))
                Return
            End If
            If _editorWordConTastiera Is Nothing OrElse _editorWordConTastiera.IsDisposed Then Return
            _editorWordConTastiera.GestisciKeyDown(Keys.Back)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnEliminaRichiestoDaTastieraWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnInvioRichiestoDaTastieraWord()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnInvioRichiestoDaTastieraWord))
                Return
            End If
            If _editorWordConTastiera Is Nothing OrElse _editorWordConTastiera.IsDisposed Then Return
            _editorWordConTastiera.GestisciKeyDown(Keys.Enter)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnInvioRichiestoDaTastieraWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiudiRichiestoDaTastieraWord()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnChiudiRichiestoDaTastieraWord))
                Return
            End If
            NascondiTastieraTouchPerWord()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnChiudiRichiestoDaTastieraWord: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTestoDettatoDaTastieraWord(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTestoDettatoDaTastieraWord), testo)
                Return
            End If
            If String.IsNullOrEmpty(testo) Then Return
            If _editorWordConTastiera Is Nothing OrElse _editorWordConTastiera.IsDisposed Then Return
            For Each carattereTesto As Char In testo
                _editorWordConTastiera.GestisciChar(carattereTesto)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnTestoDettatoDaTastieraWord: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoWordRichiedeAdiacente(sender As Object, direzione As Integer)
        Try
            Dim editor As WGWord = TryCast(sender, WGWord)
            If editor Is Nothing OrElse Me.IsDisposed Then Return
            If Not Me.IsHandleCreated Then Return
            Try
                Me.BeginInvoke(New DelegatoCampoWordDirezione(AddressOf EseguiCampoWordAdiacente), editor, direzione)
            Catch exRinvio As Exception
                _logger.ScriviLog("CampiFormInterno.CampoWordRichiedeAdiacente (rinvio): " & exRinvio.Message)
                EseguiCampoWordAdiacente(editor, direzione)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordRichiedeAdiacente: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiCampoWordAdiacente(editor As WGWord, direzione As Integer)
        Try
            If editor Is Nothing Then Return
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.EWord AndAlso x.ControlloWord Is editor)
            If dati Is Nothing Then Return
            Dim indice As Integer = _listaCampi.IndexOf(dati)
            If indice < 0 Then Return

            SalvaTuttiIValori()

            If direzione < 0 Then
                If indice = 0 Then
                    _sintesiVocale.LeggiTesto("inizio documento")
                Else
                    AttivaCampoPerIndice(indice - 1)
                End If
                Return
            End If

            If indice >= _listaCampi.Count - 1 Then
                _sintesiVocale.LeggiTesto("fine documento")
            Else
                AttivaCampoPerIndice(indice + 1)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiCampoWordAdiacente: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoWordRichiedeConferma(sender As Object)
        Try
            Dim editor As WGWord = TryCast(sender, WGWord)
            If editor Is Nothing OrElse Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New DelegatoCampoWord(AddressOf EseguiCampoWordConferma), editor)
                Return
            End If
            EseguiCampoWordConferma(editor)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordRichiedeConferma: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiCampoWordConferma(editor As WGWord)
        Try
            If _azioniUscita IsNot Nothing AndAlso _azioniUscita.Count > 0 Then
                MostraMenuUscita()
                Return
            End If

            If editor Is Nothing Then Return
            Dim dati As MetadatiCampo = _listaCampi.FirstOrDefault(Function(x) x.EWord AndAlso x.ControlloWord Is editor)
            If dati Is Nothing Then Return
            Dim indice As Integer = _listaCampi.IndexOf(dati)
            If indice < 0 Then Return

            SalvaTuttiIValori()

            If indice = _listaCampi.Count - 1 Then
                EseguiInvioFinale()
            Else
                AttivaCampoPerIndice(indice + 1)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.EseguiCampoWordConferma: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoWordRichiedeMenuUscita(sender As Object)
        Try
            MostraMenuUscita()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.CampoWordRichiedeMenuUscita: " & ex.Message)
        End Try
    End Sub

    Friend Sub NascondiTuttiICampiWord()
        Try
            If _listaCampi Is Nothing Then Return
            For Each dati As MetadatiCampo In _listaCampi
                If Not dati.EWord Then Continue For
                If dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Continue For
                If dati.ControlloWord.Visible Then
                    Try
                        dati.ControlloWord.disattivahook()
                    Catch exHook As Exception
                        _logger.ScriviLog("CampiFormInterno.NascondiTuttiICampiWord (hook): " & exHook.Message)
                    End Try
                    dati.ControlloWord.Visible = False
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.NascondiTuttiICampiWord: " & ex.Message)
        End Try
    End Sub

    Friend Sub DisponiSoloCampoWord(indice As Integer)
        Try
            If Me.IsDisposed OrElse _listaCampi Is Nothing Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New DelegatoIndiceCampo(AddressOf DisponiSoloCampoWord), indice)
                Return
            End If
            If indice < 0 OrElse indice >= _listaCampi.Count Then Return
            Dim dati As MetadatiCampo = _listaCampi(indice)
            If Not dati.EWord OrElse dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Return

            Me.SuspendLayout()

            SalvaTuttiIValori()

            For Each altro As MetadatiCampo In _listaCampi
                If altro.Controllo IsNot Nothing AndAlso Not altro.Controllo.IsDisposed Then
                    altro.Controllo.Visible = False
                End If
                If altro.EWord AndAlso Not Object.ReferenceEquals(altro, dati) Then
                    If altro.ControlloWord IsNot Nothing AndAlso Not altro.ControlloWord.IsDisposed Then
                        Try
                            altro.ControlloWord.disattivahook()
                        Catch exHook As Exception
                            _logger.ScriviLog("CampiFormInterno.DisponiSoloCampoWord (hook): " & exHook.Message)
                        End Try
                        altro.ControlloWord.Visible = False
                    End If
                End If
            Next

            SincronizzaOpzioniCampoWord(dati)

            PosizionaCampoWord(dati.ControlloWord)
            dati.ControlloWord.Visible = True
            dati.ControlloWord.BringToFront()

            Try
                dati.ControlloWord.attivahook()
            Catch exAttiva As Exception
                _logger.ScriviLog("CampiFormInterno.DisponiSoloCampoWord (attivahook): " & exAttiva.Message)
            End Try

            Me.ResumeLayout()
            Me.Invalidate()
        Catch ex As ObjectDisposedException
            _logger.ScriviLog("CampiFormInterno.DisponiSoloCampoWord (ObjectDisposed): " & ex.Message)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.DisponiSoloCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub SincronizzaOpzioniCampoWord(dati As MetadatiCampo)
        Try
            If dati Is Nothing OrElse dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Return
            dati.ControlloWord.SvuotaVociMenuDocumento()
            If eventiOpzioni Is Nothing Then Return
            For Each voce As Voce In eventiOpzioni
                If voce Is Nothing OrElse voce.Evento Is Nothing Then Continue For
                dati.ControlloWord.AggiungiVoceMenuDocumento(voce.Nome, voce.Evento)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.SincronizzaOpzioniCampoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub PosizionaCampoWord(editor As WGWord)
        Try
            If editor Is Nothing OrElse editor.IsDisposed Then Return
            Dim areaCampi As Rectangle = AreaCampoGdiTouk()
            Dim alto As Integer = areaCampi.Top + SPAZIO_SUPERIORE + TITOLO_ALTEZZA + 5
            Dim larghezza As Integer = Me.ClientSize.Width - (SPAZIO_SINISTRA * 2)
            Dim altezza As Integer = areaCampi.Bottom - alto - SPAZIO_SUPERIORE
            If larghezza < 100 Then larghezza = 100
            If altezza < 100 Then altezza = 100
            editor.Left = SPAZIO_SINISTRA
            editor.Top = alto
            editor.Width = larghezza
            editor.Height = altezza
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.PosizionaCampoWord: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            DisponiTastieraToukECampi()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.OnResize: " & ex.Message)
        End Try
    End Sub

    Private Sub DisegnaTitoloCampoWord(g As Graphics, dati As MetadatiCampo)
        Try
            If g Is Nothing OrElse dati Is Nothing Then Return
            Dim larghezza As Integer = Me.ClientSize.Width - (SPAZIO_SINISTRA * 2)
            If larghezza < 200 Then larghezza = 200
            Dim rectTitolo As New Rectangle(SPAZIO_SINISTRA, AreaCampoGdiTouk().Top + SPAZIO_SUPERIORE, larghezza, TITOLO_ALTEZZA)
            Using lgb As New LinearGradientBrush(rectTitolo, Color.FromArgb(60, 60, 60), Color.Black, LinearGradientMode.Vertical)
                g.FillRectangle(lgb, rectTitolo)
            End Using

            Dim testo As String = If(dati.TitoloBase, "")
            If dati.Obbligatorio Then testo &= " *"

            Using f As New Font("Arial", DIMENSIONE_FONT_ETICHETTA, FontStyle.Bold)
                Using sf As New StringFormat()
                    sf.Alignment = StringAlignment.Near
                    sf.LineAlignment = StringAlignment.Center
                    g.DrawString(testo, f, Brushes.White, rectTitolo, sf)
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.DisegnaTitoloCampoWord: " & ex.Message)
        End Try
    End Sub

    Private _ultimoCampoWordAnnunciato As MetadatiCampo = Nothing
    Private _tickUltimoAnnuncioWord As Integer = 0

    Private Sub LeggiCampoWordAttivo(dati As MetadatiCampo)
        Try
            If dati Is Nothing Then Return
            If Object.ReferenceEquals(dati, _ultimoCampoWordAnnunciato) AndAlso (Environment.TickCount - _tickUltimoAnnuncioWord) < 900 Then Return
            _ultimoCampoWordAnnunciato = dati
            _tickUltimoAnnuncioWord = Environment.TickCount

            Dim testoLettura As String = If(dati.TitoloBase, "").Trim() & ". "
            If dati.Obbligatorio Then
                testoLettura &= "documento obbligatorio"
            Else
                testoLettura &= "documento"
            End If

            _sintesiVocale.LeggiTesto(testoLettura, escludiPunteggiatura:=True)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.LeggiCampoWordAttivo: " & ex.Message)
        End Try
    End Sub

    Private Function TestoDocumentoWord(dati As MetadatiCampo) As String
        Try
            If dati Is Nothing OrElse dati.ControlloWord Is Nothing OrElse dati.ControlloWord.IsDisposed Then Return String.Empty
            Return dati.ControlloWord.TestoDocumento()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.TestoDocumentoWord: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Friend Sub RilasciaCampiWord()
        Try
            If _listaCampi Is Nothing Then Return
            For Each dati As MetadatiCampo In _listaCampi
                If Not dati.EWord Then Continue For
                Dim editor As WGWord = dati.ControlloWord
                If editor Is Nothing Then Continue For
                Try
                    RemoveHandler editor.RichiestaCampoAdiacente, AddressOf CampoWordRichiedeAdiacente
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (adiacente): " & exRimozione.Message)
                End Try
                Try
                    RemoveHandler editor.RichiestaConfermaCampo, AddressOf CampoWordRichiedeConferma
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (conferma): " & exRimozione.Message)
                End Try
                Try
                    RemoveHandler editor.RichiestaMenuUscita, AddressOf CampoWordRichiedeMenuUscita
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (menu): " & exRimozione.Message)
                End Try
                Try
                    RemoveHandler editor.RichiestaDettatura, AddressOf CampoWordRichiedeDettatura
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (dettatura): " & exRimozione.Message)
                End Try
                Try
                    RemoveHandler editor.GotFocus, AddressOf GestoreIngressoCampoWord
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (focus): " & exRimozione.Message)
                End Try
                Try
                    RemoveHandler editor.LostFocus, AddressOf GestoreUscitaCampoWord
                Catch exRimozione As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (lostfocus): " & exRimozione.Message)
                End Try
                Try
                    If Not editor.IsDisposed Then editor.disattivahook()
                Catch exHook As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (hook): " & exHook.Message)
                End Try
                Try
                    If Not editor.IsDisposed Then editor.Dispose()
                Catch exDispose As Exception
                    _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord (dispose): " & exDispose.Message)
                End Try
                dati.ControlloWord = Nothing
            Next
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.RilasciaCampiWord: " & ex.Message)
        End Try
    End Sub

End Class