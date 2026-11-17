Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports MailKit.Security
Imports wgcontrols
Imports WGContatti
Imports WGFileManager
Imports WGStampante
Partial Public Class MenuPosta

    Private Enum ScopoAccount
        Nessuno
        Modifica
        AttivaDisattiva
        Eliminazione
        ImpostazioniAccount
        SceltaCasellaSingola
        SceltaCaselleMultiple
        ControllaAltro
        ChiediPostaInArrivo
        ChiediInvio
        ChiediBozze
        ChiediCartelle
        ChiediRegola
    End Enum

    Private Enum ScopoSicurezza
        Imap
        Smtp
        Pop3
    End Enum

    Private Enum ModalitaCaselle
        Chiedi
        Tutte
        Singola
        Multiple
    End Enum

    Private Enum AzioneSuMessaggio
        Nessuna
        Rispondi
        RispondiTutti
        Inoltra
        Info
        RiassumiAi
        RispondiAi
        RispondiTuttiAi
    End Enum

    Private _azioneDopoCaricamento As AzioneSuMessaggio = AzioneSuMessaggio.Nessuna
    Private _chiaveMessaggioDaCaricare As String = ""
    Private _idMessaggioDaCaricare As String = ""
    Private _cartellaMessaggioDaCaricare As String = ""
    Private _messaggioCaricatoPerAzione As MessaggioCompleto = Nothing
    Private _caricamentoMessaggioPerAzioneInCorso As Boolean = False

    Private ReadOnly log As New wgcontrols.Logger()
    Private ReadOnly _posta As CLSMail
    Private ReadOnly _postaEsterna As Boolean = False
    Private _contatti As CLSContatti = Nothing

    Private ReadOnly Property Rubrica As CLSContatti
        Get
            Try
                If _contatti Is Nothing Then _contatti = New CLSContatti()
                Return _contatti
            Catch ex As Exception
                log.ScriviLog("MenuPosta.Rubrica: " & ex.Message)
                Return Nothing
            End Try
        End Get
    End Property
    Private _marshal As System.Windows.Forms.Control

    Private _menuPrincipale As WGMenu
    Private _menuImpostazioni As WGMenu
    Private _menuSicurezza As WGMenu
    Private _menuConferma As WGMenu
    Private _menuCaselle As WGMenu
    Private _menuOpzioniAccount As WGMenu
    Private _listaAccount As WGMenuLista
    Private _listaMessaggi As WGMenuLista
    Private _listaAllegati As WGMenuLista
    Private _listaAllegatiMessaggio As WGMenuLista
    Private _listaInfoMessaggio As WGMenuLista
    Private _listaStampantiPosta As WGMenuLista
    Private _listaCartelle As WGMenuLista
    Private _listaDestinatari As WGMenuLista
    Private _listaDestinatariRecenti As WGMenuLista
    Private _campiComposizione As Campi
    Private _campiMessaggioInviato As Campi
    Private _campiAccount As Campi
    Private _campoMessaggio As WGCampo
    Private _attesa As WGProcessoInCorso
    Private _dialogAllegati As WGFileDialog
    Private _dialogSalvaAllegato As WGFileDialog
    Private _ultimoTestoDestinatario As String = ""
    Private _domandaAutomatico As WGDomanda
    Private _domandaElimina As WGDomanda

    Private _chiuso As Boolean = False
    Private _scopoAccount As ScopoAccount = ScopoAccount.Nessuno
    Private _scopoSicurezza As ScopoSicurezza = ScopoSicurezza.Imap
    Private _modalitaCaselle As ModalitaCaselle = ModalitaCaselle.Chiedi
    Private _tipoCartellaDaAprire As String = ""
    Private _chiaveAccountCorrente As String = ""
    Private _chiaviAccountFiltro As New List(Of String)
    Private _cartellaCorrente As String = "INBOX"
    Private _tipoCartellaSpeciale As String = "inbox"
    Private _elencoAccount As New List(Of ImpostazioniAccountMail)
    Private _elencoMessaggi As New List(Of RiepilogoMessaggio)
    Private _messaggioCorrente As MessaggioCompleto
    Private _accountInModifica As ImpostazioniAccountMail
    Private _soloNonLetti As Boolean = False
    Private _testoRicerca As String = ""
    Private _configurazioneManuale As Boolean = False
    Private _mostraHtmlOriginale As Boolean = False
    Private _cartelleAccount As New List(Of CartellaImap)
    Private _idDaEliminare As New List(Of String)
    Private _cartellaDaEliminare As String = ""
    Private _eliminazioneDefinitiva As Boolean = False
    Private _eliminati As Integer = 0
    Private _salvaSincronizzazioneSubito As Boolean = False
    Private _esitoProva As EsitoProvaConnessione
    Private _totaleMessaggi As Integer = 0
    Private _caricamentoMessaggiInCorso As Boolean = False
    Private _listaMessaggiGiaMostrata As Boolean = False
    Private _listaMessaggiRisultatiRicerca As Boolean = False
    Private _timerNuoviMessaggi As System.Threading.Timer
    Private _controlloNuoviMessaggiInCorso As Boolean = False
    Private Const IntervalloControlloNuoviMessaggiMs As Integer = 30000
    Private _domandaEliminaTutti As WGDomanda

    Private _posizioneDaRipristinare As String = ""
    Private _elencoDalServer As List(Of RiepilogoMessaggio) = Nothing
    Private _totaleDalServer As Integer = 0
    Private _menuRicerca As WGMenu
    Private _listaRicercheRecenti As WGMenuLista

    Private _composizioneDestinatari As String = ""
    Private _composizioneCc As String = ""
    Private _composizioneCcn As String = ""
    Private _composizioneOggetto As String = ""
    Private _composizioneCorpo As String = ""
    Private _allegatiComposizione As New List(Of String)
    Private _invioDestinatari As List(Of String)
    Private _invioCc As List(Of String)
    Private _invioCcn As List(Of String)
    Private _invioOggetto As String = ""
    Private _invioCorpo As String = ""
    Private _invioAllegati As List(Of String)
    Private _invioRiuscito As Boolean = False
    Private _composizioneInRispostaA As String = ""
    Private _composizioneRiferimenti As String = ""
    Private _ordinamento As OrdinamentoMessaggi = OrdinamentoMessaggi.DataRecente
    Private _soloElencoDaLeggere As Boolean = False
    Private _menuOrdina As WGMenu
    Private _menuFiltra As WGMenu
    Private _menuFiltraData As WGMenu
    Private _filtroCriterio As CriterioFiltroMessaggi = CriterioFiltroMessaggi.Mittente
    Private _filtroTesto As String = ""
    Private _filtroData As DateTime = DateTime.MinValue
    Private _descrizioneFiltro As String = ""
    Private _titoloRisultati As String = ""

    Public Event Uscita As EventHandler

    Private Shared _postaCondivisa As CLSMail = Nothing
    Private Shared ReadOnly _lockPostaCondivisa As New Object()

    Public Shared Function PostaCondivisa() As CLSMail
        Try
            SyncLock _lockPostaCondivisa
                If _postaCondivisa Is Nothing OrElse _postaCondivisa.Smaltita Then _postaCondivisa = New CLSMail()
                Return _postaCondivisa
            End SyncLock
        Catch ex As Exception
            CLSMail.ScriviLog("MenuPosta.PostaCondivisa: " & ex.Message)
            Return New CLSMail()
        End Try
    End Function

    Public Sub New()
        Try
            _posta = PostaCondivisa()
            _postaEsterna = True
            PreparaMarshal()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.New: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub New(posta As CLSMail)
        Try
            If posta Is Nothing Then
                _posta = PostaCondivisa()
                _postaEsterna = True
            Else
                _posta = posta
                _postaEsterna = True
            End If
            PreparaMarshal()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.New (istanza esterna): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PreparaMarshal()
        Try
            _marshal = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshal.Handle
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaMarshal: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSuInterfaccia(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _marshal Is Nothing OrElse _marshal.IsDisposed OrElse Not _marshal.IsHandleCreated Then
                azione.Invoke()
                Return
            End If
            If _marshal.InvokeRequired Then
                _marshal.BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch
        End Try
    End Sub

    Private Sub AvviaLavoro(lavoro As Action)
        Try
            If lavoro Is Nothing Then Return
            Task.Run(lavoro)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaLavoro: " & ex.Message)
        End Try
    End Sub

    Private _annullamento As System.Threading.CancellationTokenSource
    Private _ultimaOperazioneAnnullata As Boolean = False

    Private Sub MostraAttesa(testo As String)
        MostraAttesa(testo, True)
    End Sub

    Private Sub MostraAttesa(testo As String, annullabile As Boolean)
        Try
            ChiudiAttesa()

            _ultimaOperazioneAnnullata = False
            _annullamento = New System.Threading.CancellationTokenSource()
            Try
                _posta.ImpostaAnnullamento(_annullamento.Token)
            Catch exToken As Exception
                log.ScriviLog("MenuPosta.MostraAttesa (annullamento): " & exToken.Message)
            End Try

            _attesa = New WGProcessoInCorso()
            If annullabile Then AddHandler _attesa.cancella, AddressOf AttesaAnnullata
            _attesa.Avvia(testo, annullabile)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub AttesaAnnullata(sender As Object, e As EventArgs)
        Try
            _ultimaOperazioneAnnullata = True
            _attesa = Nothing

            Try
                If _annullamento IsNot Nothing Then _annullamento.Cancel()
            Catch exCancella As Exception
                log.ScriviLog("MenuPosta.AttesaAnnullata (cancella): " & exCancella.Message)
            End Try

            _caricamentoMessaggiInCorso = False
            _controlloNuoviMessaggiInCorso = False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AttesaAnnullata: " & ex.Message)
        End Try
    End Sub

    Private Function OperazioneAnnullata() As Boolean
        Try
            If _ultimaOperazioneAnnullata Then Return True
            If _annullamento Is Nothing Then Return False
            Return _annullamento.IsCancellationRequested
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OperazioneAnnullata: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ChiudiAttesa()
        Try
            If _attesa IsNot Nothing Then
                Try
                    RemoveHandler _attesa.cancella, AddressOf AttesaAnnullata
                Catch exRimozione As Exception
                    log.ScriviLog("MenuPosta.ChiudiAttesa (handler): " & exRimozione.Message)
                End Try
                _attesa.Fine()
                _attesa = Nothing
            End If
        Catch exAttesa As Exception
            log.ScriviLog("MenuPosta.ChiudiAttesa: " & exAttesa.Message)
            _attesa = Nothing
        End Try

        Try
            _posta.AzzeraAnnullamento()
            _annullamento = Nothing
        Catch exToken As Exception
            log.ScriviLog("MenuPosta.ChiudiAttesa (annullamento): " & exToken.Message)
            _annullamento = Nothing
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggi As New WGMessaggi()
            messaggi.Messaggio(testo)
            messaggi.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiDomandaAutomatico()
        Try
            If _domandaAutomatico Is Nothing Then Return
            RemoveHandler _domandaAutomatico.rispostadomanda, AddressOf RispostaConfiguraAutomatico
            _domandaAutomatico = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiDomandaAutomatico: " & ex.Message)
            _domandaAutomatico = Nothing
        End Try
    End Sub

    Private Sub ChiudiDomandaElimina()
        Try
            If _domandaElimina Is Nothing Then Return
            RemoveHandler _domandaElimina.rispostadomanda, AddressOf RispostaEliminaMessaggi
            _domandaElimina = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiDomandaElimina: " & ex.Message)
            _domandaElimina = Nothing
        End Try
    End Sub

    Private Sub ChiudiDomandaEliminaTutti()
        Try
            If _domandaEliminaTutti Is Nothing Then Return
            RemoveHandler _domandaEliminaTutti.rispostadomanda, AddressOf RispostaEliminaTutti
            _domandaEliminaTutti = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiDomandaEliminaTutti: " & ex.Message)
            _domandaEliminaTutti = Nothing
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _chiuso = False
            CaricaElencoAccount()
            If String.IsNullOrWhiteSpace(_chiaveAccountCorrente) AndAlso _elencoAccount.Count > 0 Then
                _chiaveAccountCorrente = _elencoAccount(0).Chiave
            End If
            MostraMenuPrincipale()
            If _elencoAccount.Count = 0 Then
                MostraMessaggio("Nessun account configurato. Aggiungine uno dalle impostazioni.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.Avvia: " & ex.Message)
            MostraMessaggio("Non è stato possibile avviare la posta: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            If _chiuso Then Return
            _chiuso = True
            FermaTimerNuoviMessaggi()
            ChiudiAttesa()
            ChiudiDomandaAutomatico()
            ChiudiDomandaElimina()
            ChiudiDomandaEliminaTutti()
            If Not _postaEsterna AndAlso _posta IsNot Nothing Then _posta.Dispose()
            If _contatti IsNot Nothing Then _contatti.Dispose()
            If _marshal IsNot Nothing AndAlso Not _marshal.IsDisposed Then _marshal.Dispose()
            RaiseEvent Uscita(Me, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Function CiSonoNuoviMessaggi(Optional chiaveAccount As String = "") As Boolean
        Try
            Return _posta.CiSonoNuoviMessaggi(chiaveAccount)
        Catch
            Return False
        End Try
    End Function

    Public Shared Function CiSonoNuoviMessaggiSenzaInterfaccia(Optional chiaveAccount As String = "") As Boolean
        Dim posta As CLSMail = Nothing
        Try
            posta = New CLSMail()
            Return posta.CiSonoNuoviMessaggi(chiaveAccount)
        Catch
            Return False
        Finally
            Try
                If posta IsNot Nothing Then posta.Dispose()
            Catch
            End Try
        End Try
    End Function

    Public Function IntervalloControlloMinuti() As Integer
        Try
            Return _posta.OttieniIntervalloControlloMinuti()
        Catch
            Return 15
        End Try
    End Function

    Public Shared Function IntervalloControlloMinutiSenzaInterfaccia() As Integer
        Dim posta As CLSMail = Nothing
        Try
            posta = New CLSMail()
            Return posta.OttieniIntervalloControlloMinuti()
        Catch
            Return 15
        Finally
            Try
                If posta IsNot Nothing Then posta.Dispose()
            Catch
            End Try
        End Try
    End Function

    Public Shared Sub ApriElencoDaLeggere()
        Try
            Dim finestra As New MenuPosta()
            finestra.AvviaSoloDaLeggere()
        Catch ex As Exception
            CLSMail.ScriviLog("MenuPosta.ApriElencoDaLeggere: " & ex.ToString())
        End Try
    End Sub

    Public Sub AvviaSoloDaLeggere()
        Try
            _chiuso = False
            _soloElencoDaLeggere = True
            CaricaElencoAccount()
            If _elencoAccount Is Nothing OrElse _elencoAccount.Count = 0 Then
                MostraMessaggio("Nessun account configurato.")
                Return
            End If

            Dim chiavi As New List(Of String)
            For Each impostazioni As ImpostazioniAccountMail In _elencoAccount
                If impostazioni IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(impostazioni.Chiave) Then chiavi.Add(impostazioni.Chiave)
            Next
            If chiavi.Count = 0 Then Return

            _modalitaCaselle = ModalitaCaselle.Multiple
            _chiaviAccountFiltro = chiavi
            _chiaveAccountCorrente = chiavi(0)
            MostraMessaggiDaLeggere()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaSoloDaLeggere: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraMessaggiDaLeggere(Optional chiaveAccount As String = "")
        Try
            _soloNonLetti = True
            _tipoCartellaSpeciale = "inbox"
            _cartellaCorrente = "INBOX"
            If Not String.IsNullOrWhiteSpace(chiaveAccount) Then
                _chiaveAccountCorrente = chiaveAccount
                _modalitaCaselle = ModalitaCaselle.Singola
                _chiaviAccountFiltro = New List(Of String) From {chiaveAccount}
            End If
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMessaggiDaLeggere: " & ex.Message)
        End Try
    End Sub

    Private Sub CaricaElencoAccount()
        Try
            _elencoAccount = _posta.OttieniElencoAccount(False)
            If _elencoAccount Is Nothing Then _elencoAccount = New List(Of ImpostazioniAccountMail)
        Catch
            _elencoAccount = New List(Of ImpostazioniAccountMail)
        End Try
    End Sub

    Private Function DescriviAccount(impostazioni As ImpostazioniAccountMail) As String
        Try
            If impostazioni Is Nothing Then Return ""
            Dim d As String = impostazioni.NomeVisualizzato
            If String.IsNullOrWhiteSpace(d) Then d = impostazioni.IndirizzoEmail
            If Not impostazioni.Attivo Then d &= " (disattivato)"
            Return d
        Catch
            Return ""
        End Try
    End Function

    Private Function OttieniImpostazioniAccount(chiave As String) As ImpostazioniAccountMail
        Try
            Return _posta.OttieniAccount(chiave)
        Catch
            Return Nothing
        End Try
    End Function

    Private Sub MostraMenuPrincipale()
        Try
            If _chiuso Then Return
            _soloNonLetti = False
            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Nuovo messaggio", AddressOf VoceNuovoMessaggio)
            _menuPrincipale.aggiungi("Posta in arrivo", AddressOf VocePostaInArrivo)
            _menuPrincipale.aggiungi("Posta inviata", AddressOf VocePostaInviata)
            _menuPrincipale.aggiungi("Bozze", AddressOf VoceBozze)
            _menuPrincipale.aggiungi("Posta eliminata", AddressOf VocePostaEliminata)
            _menuPrincipale.aggiungi("Posta indesiderata", AddressOf VocePostaIndesiderata)
            _menuPrincipale.aggiungi("Altre cartelle", AddressOf VoceAltreCartelle)
            _menuPrincipale.aggiungi("Cerca", AddressOf VoceCerca)
            _menuPrincipale.aggiungi("Destinatari recenti", AddressOf VoceDestinatariRecenti)
            _menuPrincipale.aggiungi("Impostazioni", AddressOf VoceImpostazioni)
            _menuPrincipale.avvia("Mail")
            AvviaConnessioneIniziale()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuPrincipale: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaConnessioneIniziale()
        Try
            AvviaLavoro(New Action(AddressOf LavoroConnessioneIniziale))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaConnessioneIniziale: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroConnessioneIniziale()
        Try
            For Each chiave As String In ChiaviDaConsultare()
                Try
                    Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(chiave)
                    If impostazioni IsNot Nothing Then _posta.ProvaConnessioneAccount(impostazioni)
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LavoroConnessioneIniziale (" & If(chiave, "") & "): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroConnessioneIniziale: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceNuovoMessaggio(sender As Object, e As EventArgs)
        Try
            AzzeraRubricaDestinatari()
            _composizioneDestinatari = ""
            _composizioneCc = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcn = ""
            _composizioneOggetto = ""
            _composizioneCorpo = ""
            _allegatiComposizione = New List(Of String)
            _composizioneInRispostaA = ""
            _composizioneRiferimenti = ""

            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo per l'invio.")
                Return
            End If
            If attivi.Count > 1 Then
                MostraSceltaAccountPer(ScopoAccount.ChiediInvio, "Casella da cui inviare")
                Return
            End If
            _chiaveAccountCorrente = attivi(0).Chiave
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceNuovoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceDestinatariRecenti(sender As Object, e As EventArgs)
        Try
            Dim recenti As DataTable = _posta.OttieniDestinatariRecenti("", 50)
            If recenti Is Nothing OrElse recenti.Rows.Count = 0 Then
                ' niente da elencare: un messaggio, e l'elenco non si apre
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ci sono destinatari recenti.")
                msg.ShowDialog()
                Return
            End If
            _listaDestinatariRecenti = New WGMenuLista()
            _listaDestinatariRecenti.multiselezioneConsentita = False
            _listaDestinatariRecenti.impostaTitolo("Destinatari recenti")
            For Each riga As DataRow In recenti.Rows
                Dim email As String = riga("Email").ToString()
                Dim nome As String = riga("NomeVisualizzato").ToString()
                _listaDestinatariRecenti.aggiungiElemento(If(String.IsNullOrWhiteSpace(nome), email, nome & " <" & email & ">"), email)
            Next
            _listaDestinatariRecenti.aggiungiOpzione("Svuota recenti", AddressOf OpzioneSvuotaDestinatariRecenti)
            AddHandler _listaDestinatariRecenti.ElementoSelezionato, AddressOf DestinatarioRecenteSelezionato
            AddHandler _listaDestinatariRecenti.escPremuto, AddressOf ListaDestinatariRecentiEsc
            _listaDestinatariRecenti.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceDestinatariRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaDestinatariRecentiEsc(sender As Object, e As EventArgs)
        Try
            If _listaDestinatariRecenti IsNot Nothing Then _listaDestinatariRecenti.abbandona()
            _listaDestinatariRecenti = Nothing
        Catch
            _listaDestinatariRecenti = Nothing
        End Try
    End Sub

    Private Sub DestinatarioRecenteSelezionato(id As String)
        Try
            If _listaDestinatariRecenti IsNot Nothing Then _listaDestinatariRecenti.abbandona()
            _listaDestinatariRecenti = Nothing
            If String.IsNullOrWhiteSpace(id) Then Return

            _composizioneDestinatari = id
            _composizioneCc = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcn = ""
            _composizioneOggetto = ""
            _composizioneCorpo = ""
            _allegatiComposizione = New List(Of String)
            _composizioneInRispostaA = ""
            _composizioneRiferimenti = ""

            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo per l'invio.")
                Return
            End If
            If attivi.Count > 1 Then
                MostraSceltaAccountPer(ScopoAccount.ChiediInvio, "Casella da cui inviare")
                Return
            End If
            _chiaveAccountCorrente = attivi(0).Chiave
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DestinatarioRecenteSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSvuotaDestinatariRecenti(sender As Object, e As EventArgs)
        Try
            _posta.SvuotaDestinatariRecenti()
            If _listaDestinatariRecenti IsNot Nothing Then _listaDestinatariRecenti.abbandona()
            _listaDestinatariRecenti = Nothing
            MostraMessaggio("Destinatari recenti svuotati.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSvuotaDestinatariRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub VocePostaInArrivo(sender As Object, e As EventArgs)
        ApriCartellaConScelta("inbox")
    End Sub

    Private Const TipoRicerca As String = "__cerca__"

    Public Enum OrdinamentoMessaggi
        DataRecente = 0
        DataVecchia = 1
        Oggetto = 2
        Mittente = 3
    End Enum

    Private Sub ApriCartellaConScelta(tipo As String)
        Try
            _tipoCartellaDaAprire = If(tipo, "").Trim().ToLowerInvariant()

            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo.")
                Return
            End If

            If attivi.Count = 1 Then
                _chiaveAccountCorrente = attivi(0).Chiave
                _chiaviAccountFiltro = New List(Of String) From {attivi(0).Chiave}
                EseguiAperturaInAttesa()
                Return
            End If

            If _modalitaCaselle = ModalitaCaselle.Chiedi Then
                MostraSceltaCaselleDaConsultare(attivi)
                Return
            End If

            EseguiAperturaInAttesa()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCartellaConScelta: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiAperturaInAttesa()
        Try
            Dim tipo As String = _tipoCartellaDaAprire
            _tipoCartellaDaAprire = ""
            If String.IsNullOrWhiteSpace(tipo) Then tipo = "inbox"

            If String.Equals(tipo, TipoRicerca, StringComparison.OrdinalIgnoreCase) Then
                MostraMenuRicerca()
                Return
            End If

            If String.Equals(tipo, "inbox", StringComparison.OrdinalIgnoreCase) Then
                MostraMenuFiltroPostaInArrivo()
                Return
            End If

            ApriCartellaSpeciale(tipo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.EseguiAperturaInAttesa: " & ex.Message)
        End Try
    End Sub

    Private Const ChiaveTutteLeCaselle As String = "__TUTTE__"

    Private Sub MostraSceltaCaselleDaConsultare(attivi As List(Of ImpostazioniAccountMail))
        Try
            _listaAccount = New WGMenuLista()
            _listaAccount.multiselezioneConsentita = True
            _listaAccount.impostaTitolo("Caselle di posta da consultare")
            _listaAccount.aggiungiElemento("Tutte", ChiaveTutteLeCaselle)
            For Each impostazioni As ImpostazioniAccountMail In attivi
                _listaAccount.aggiungiElemento(DescriviAccount(impostazioni), impostazioni.Chiave)
            Next
            AddHandler _listaAccount.ElementoSelezionato, AddressOf CasellaDaConsultareSelezionata
            AddHandler _listaAccount.ElementiSelezionati, AddressOf CaselleDaConsultareSelezionate
            AddHandler _listaAccount.escPremuto, AddressOf ListaAccountEsc
            _listaAccount.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaCaselleDaConsultare: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaMenuCaselle()
        Try
            If _listaAccount Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaAccount
            _listaAccount = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf CasellaDaConsultareSelezionata
                RemoveHandler precedente.ElementiSelezionati, AddressOf CaselleDaConsultareSelezionate
                RemoveHandler precedente.escPremuto, AddressOf ListaAccountEsc
            Catch exHandler As Exception
                log.ScriviLog("MenuPosta.AbbandonaMenuCaselle (handler): " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AbbandonaMenuCaselle: " & ex.Message)
            _listaAccount = Nothing
        End Try
    End Sub

    Private Sub CasellaDaConsultareSelezionata(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            AbbandonaMenuCaselle()
            If id = ChiaveTutteLeCaselle Then
                Dim tutte As New List(Of String)
                For Each a As ImpostazioniAccountMail In _posta.OttieniElencoAccount(True)
                    tutte.Add(a.Chiave)
                Next
                _chiaviAccountFiltro = tutte
                _chiaveAccountCorrente = If(tutte.Count > 0, tutte(0), "")
            Else
                _chiaviAccountFiltro = New List(Of String) From {id}
                _chiaveAccountCorrente = id
            End If
            EseguiAperturaInAttesa()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CasellaDaConsultareSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub CaselleDaConsultareSelezionate(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return
            AbbandonaMenuCaselle()
            Dim chiavi As New List(Of String)
            If ids.Contains(ChiaveTutteLeCaselle) Then
                For Each a As ImpostazioniAccountMail In _posta.OttieniElencoAccount(True)
                    chiavi.Add(a.Chiave)
                Next
            Else
                chiavi.AddRange(ids)
            End If
            _chiaviAccountFiltro = chiavi
            _chiaveAccountCorrente = If(chiavi.Count > 0, chiavi(0), "")
            EseguiAperturaInAttesa()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CaselleDaConsultareSelezionate: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuFiltroPostaInArrivo()
        Try
            Dim menuFiltro As New WGMenu()
            menuFiltro.aggiungi("Messaggi non letti", AddressOf OpzioneSoloNonLetti)
            menuFiltro.aggiungi("Tutti i messaggi in arrivo", AddressOf OpzioneTuttiMessaggiInArrivo)
            menuFiltro.avvia("Filtra messaggi", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuFiltroPostaInArrivo: " & ex.Message)
            ApriCartellaSpeciale("inbox")
        End Try
    End Sub

    Private Sub OpzioneSoloNonLetti(sender As Object, e As EventArgs)
        Try
            _soloNonLetti = True
            ApriCartellaSpeciale("inbox")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSoloNonLetti: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneTuttiMessaggiInArrivo(sender As Object, e As EventArgs)
        Try
            _soloNonLetti = False
            ApriCartellaSpeciale("inbox")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneTuttiMessaggiInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraSceltaAccountPer(scopo As ScopoAccount, titolo As String)
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo.")
                Return
            End If
            _scopoAccount = scopo
            _listaAccount = New WGMenuLista()
            _listaAccount.multiselezioneConsentita = False
            _listaAccount.impostaTitolo(titolo)
            For Each impostazioni As ImpostazioniAccountMail In attivi
                _listaAccount.aggiungiElemento(DescriviAccount(impostazioni), impostazioni.Chiave)
            Next
            AddHandler _listaAccount.ElementoSelezionato, AddressOf AccountSelezionato
            AddHandler _listaAccount.escPremuto, AddressOf ListaAccountEsc
            _listaAccount.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaAccountPer: " & ex.Message)
        End Try
    End Sub

    Private Sub VocePostaInviata(sender As Object, e As EventArgs)
        ApriCartellaConScelta("sent")
    End Sub

    Private Sub VoceBozze(sender As Object, e As EventArgs)
        ApriCartellaConScelta("drafts")
    End Sub

    Private Sub VocePostaEliminata(sender As Object, e As EventArgs)
        ApriCartellaConScelta("trash")
    End Sub

    Private Sub VocePostaIndesiderata(sender As Object, e As EventArgs)
        ApriCartellaConScelta("junk")
    End Sub

    Private Sub VoceAltreCartelle(sender As Object, e As EventArgs)
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo.")
                Return
            End If

            If attivi.Count = 1 Then
                _chiaveAccountCorrente = attivi(0).Chiave
                AvviaLetturaCartelle()
                Return
            End If

            Dim daConsultare As List(Of String) = ChiaviDaConsultare()
            If daConsultare IsNot Nothing AndAlso daConsultare.Count = 1 AndAlso _modalitaCaselle <> ModalitaCaselle.Chiedi Then
                _chiaveAccountCorrente = daConsultare(0)
                AvviaLetturaCartelle()
                Return
            End If

            MostraSceltaAccountPer(ScopoAccount.ChiediCartelle, "Casella di cui vedere le cartelle")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceAltreCartelle: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaLetturaCartelle()
        Try
            MostraAttesa("Lettura cartelle in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCartelle))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaLetturaCartelle: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroCartelle()
        Try
            _cartelleAccount = _posta.OttieniElencoCartelleImap(_chiaveAccountCorrente)
            If _cartelleAccount Is Nothing Then _cartelleAccount = New List(Of CartellaImap)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCartelle: " & ex.Message)
            _cartelleAccount = New List(Of CartellaImap)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCartelle)
        End Try
    End Sub

    Public Const ChiaveRadiceCasella As String = "__radice__"

    Private _cartellaPadreNuova As String = ""
    Private _cartellaSuCuiAgire As String = ""

    Private Function CartellaEvidenziata() As String
        Try
            If _listaCartelle Is Nothing OrElse _listaCartelle.IsDisposed Then Return ""
            Dim elementi As List(Of String) = _listaCartelle.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Dim elemento As String = elementi(0)
            If String.Equals(elemento, ChiaveRadiceCasella, StringComparison.Ordinal) Then Return ""
            Return If(elemento, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaEvidenziata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub OpzioneCreaCartella(sender As Object, e As EventArgs)
        Try
            _cartellaPadreNuova = ""
            ChiediNomeNuovaCartella("Nome della nuova cartella")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneCreaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediNomeNuovaCartella(titolo As String)
        Try
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi(titolo, "cartella.nome", True, False, "")
            AddHandler _campiComposizione.Fine, AddressOf CampiNuovaCartellaFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiediNomeNuovaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiNuovaCartellaFine(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiComposizione IsNot Nothing Then nome = _campiComposizione.RecuperaValore("cartella.nome")
            DisposeCampiComposizione()

            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della cartella non può essere vuoto.")
                Return
            End If

            _nomeNuovaCartella = nome.Trim()
            MostraAttesa("Creazione della cartella in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCreaCartella))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiNuovaCartellaFine: " & ex.Message)
        End Try
    End Sub

    Private _nomeNuovaCartella As String = ""
    Private _cartellaCreata As Boolean = False

    Private Sub LavoroCreaCartella()
        Try
            _cartellaCreata = _posta.CreaCartellaImap(_chiaveAccountCorrente, _nomeNuovaCartella, _cartellaPadreNuova)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCreaCartella: " & ex.Message)
            _cartellaCreata = False
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCreaCartella)
        End Try
    End Sub

    Private Sub CompletaCreaCartella()
        Try
            ChiudiAttesa()
            If Not _cartellaCreata Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaCreaCartella errore: " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(descrizione), "Cartella non creata.", descrizione))
                Return
            End If

            MostraMessaggio("Cartella " & _nomeNuovaCartella & " creata.")
            _nomeNuovaCartella = ""
            _cartellaPadreNuova = ""
            AvviaLetturaCartelle()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCreaCartella: " & ex.Message)
        End Try
    End Sub

    Private _nuovoNomeCartella As String = ""
    Private _nuovoPadreCartella As String = ""
    Private _esitoOperazioneCartella As Boolean = False
    Private _descrizioneOperazioneCartella As String = ""
    Private _domandaEliminaCartella As WGDomanda

    Private Function ScegliCartellaSuCuiAgire(azione As String) As Boolean
        Try
            Dim percorso As String = CartellaEvidenziata()
            If String.IsNullOrWhiteSpace(percorso) Then
                MostraMessaggio("Mettiti prima sulla cartella da " & azione & ".")
                Return False
            End If
            _cartellaSuCuiAgire = percorso
            Return True
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ScegliCartellaSuCuiAgire: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub OpzioneRinominaCartella(sender As Object, e As EventArgs)
        Try
            If Not ScegliCartellaSuCuiAgire("rinominare") Then Return
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi("Nuovo nome di " & _cartellaSuCuiAgire, "cartella.nuovonome", True, False, "")
            AddHandler _campiComposizione.Fine, AddressOf CampiRinominaCartellaFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRinominaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiRinominaCartellaFine(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiComposizione IsNot Nothing Then nome = _campiComposizione.RecuperaValore("cartella.nuovonome")
            DisposeCampiComposizione()

            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della cartella non può essere vuoto.")
                Return
            End If

            _nuovoNomeCartella = nome.Trim()
            _nuovoPadreCartella = CLSMail.PadreInvariato
            _descrizioneOperazioneCartella = "Cartella rinominata in " & _nuovoNomeCartella & "."
            MostraAttesa("Rinomina della cartella in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroRinominaCartella))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiRinominaCartellaFine: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSpostaCartella(sender As Object, e As EventArgs)
        Try
            If Not ScegliCartellaSuCuiAgire("spostare") Then Return
            _scopoElencoCartelle = "spostacartella"
            AvviaLetturaCartelle()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSpostaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaSpostamentoCartella(nuovoPadre As String)
        Try
            _nuovoNomeCartella = ""
            _nuovoPadreCartella = If(nuovoPadre, "")
            If String.IsNullOrWhiteSpace(_nuovoPadreCartella) Then
                _descrizioneOperazioneCartella = "Cartella spostata fra le cartelle principali."
            Else
                _descrizioneOperazioneCartella = "Cartella spostata dentro " & _nuovoPadreCartella & "."
            End If
            MostraAttesa("Spostamento della cartella in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroRinominaCartella))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaSpostamentoCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRinominaCartella()
        Try
            _esitoOperazioneCartella = _posta.RinominaCartellaImap(_chiaveAccountCorrente, _cartellaSuCuiAgire, _nuovoNomeCartella, _nuovoPadreCartella)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroRinominaCartella: " & ex.Message)
            _esitoOperazioneCartella = False
        Finally
            EseguiSuInterfaccia(AddressOf CompletaOperazioneCartella)
        End Try
    End Sub

    Private Sub OpzioneEliminaCartella(sender As Object, e As EventArgs)
        Try
            If Not ScegliCartellaSuCuiAgire("eliminare") Then Return
            ChiudiDomandaEliminaCartella()
            _domandaEliminaCartella = New WGDomanda()
            AddHandler _domandaEliminaCartella.rispostadomanda, AddressOf RispostaEliminaCartella
            _domandaEliminaCartella.avvia("Vuoi eliminare la cartella " & _cartellaSuCuiAgire & " con tutti i messaggi che contiene? L'operazione non è reversibile.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiDomandaEliminaCartella()
        Try
            If _domandaEliminaCartella Is Nothing Then Return
            Try
                RemoveHandler _domandaEliminaCartella.rispostadomanda, AddressOf RispostaEliminaCartella
            Catch exRimozione As Exception
                log.ScriviLog("MenuPosta.ChiudiDomandaEliminaCartella (handler): " & exRimozione.Message)
            End Try
            _domandaEliminaCartella = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiDomandaEliminaCartella: " & ex.Message)
            _domandaEliminaCartella = Nothing
        End Try
    End Sub

    Private Sub RispostaEliminaCartella(risposta As DialogResult)
        Try
            ChiudiDomandaEliminaCartella()
            If risposta <> DialogResult.Yes Then Return
            _descrizioneOperazioneCartella = "Cartella eliminata."
            MostraAttesa("Eliminazione della cartella in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroEliminaCartella))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RispostaEliminaCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroEliminaCartella()
        Try
            _esitoOperazioneCartella = _posta.EliminaCartellaImap(_chiaveAccountCorrente, _cartellaSuCuiAgire)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroEliminaCartella: " & ex.Message)
            _esitoOperazioneCartella = False
        Finally
            EseguiSuInterfaccia(AddressOf CompletaOperazioneCartella)
        End Try
    End Sub

    Private Sub CompletaOperazioneCartella()
        Try
            ChiudiAttesa()
            _cartellaSuCuiAgire = ""
            _nuovoNomeCartella = ""
            _nuovoPadreCartella = ""

            If Not _esitoOperazioneCartella Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaOperazioneCartella errore: " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(descrizione), "Operazione non riuscita.", descrizione))
                Return
            End If

            MostraMessaggio(If(String.IsNullOrWhiteSpace(_descrizioneOperazioneCartella), "Operazione completata.", _descrizioneOperazioneCartella))
            _descrizioneOperazioneCartella = ""
            AvviaLetturaCartelle()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaOperazioneCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaCartelle()
        Try
            ChiudiAttesa()
            If _cartelleAccount Is Nothing Then _cartelleAccount = New List(Of CartellaImap)
            _listaCartelle = New WGMenuLista()
            _listaCartelle.multiselezioneConsentita = False
            Dim titoloElenco As String = "Cartelle"
            If String.Equals(_scopoElencoCartelle, "sposta", StringComparison.OrdinalIgnoreCase) Then
                titoloElenco = "Cartella in cui spostare i messaggi"
            ElseIf String.Equals(_scopoElencoCartelle, "spostacartella", StringComparison.OrdinalIgnoreCase) Then
                titoloElenco = "Dove spostare " & _cartellaSuCuiAgire
            End If
            _listaCartelle.impostaTitolo(titoloElenco)
            If String.Equals(_scopoElencoCartelle, "spostacartella", StringComparison.OrdinalIgnoreCase) Then
                _listaCartelle.aggiungiElemento("Fra le cartelle principali", ChiaveRadiceCasella)
            End If
            For Each cartella As CartellaImap In _cartelleAccount
                _listaCartelle.aggiungiElemento(DescriviCartella(cartella), cartella.PercorsoCompleto)
            Next
            _listaCartelle.aggiungiOpzione("Crea una cartella", AddressOf OpzioneCreaCartella)
            _listaCartelle.aggiungiOpzione("Rinomina la cartella", AddressOf OpzioneRinominaCartella)
            _listaCartelle.aggiungiOpzione("Sposta la cartella", AddressOf OpzioneSpostaCartella)
            _listaCartelle.aggiungiOpzione("Elimina la cartella", AddressOf OpzioneEliminaCartella)
            AddHandler _listaCartelle.ElementoSelezionato, AddressOf CartellaSelezionata
            AddHandler _listaCartelle.escPremuto, AddressOf ListaCartelleEsc
            _listaCartelle.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCartelle: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviCartella(cartella As CartellaImap) As String
        Try
            If cartella Is Nothing Then Return ""
            Dim d As String = cartella.Nome
            If String.IsNullOrWhiteSpace(d) Then d = cartella.PercorsoCompleto
            If Not String.IsNullOrWhiteSpace(cartella.Speciale) Then d = cartella.Speciale
            If cartella.NumeroNonLetti > 0 Then d &= ", " & cartella.NumeroNonLetti.ToString() & " da leggere"
            Return d
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviCartella: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ListaCartelleEsc(sender As Object, e As EventArgs)
        Try
            _scopoElencoCartelle = ""
            If _listaCartelle IsNot Nothing Then _listaCartelle.abbandona()
            _listaCartelle = Nothing
        Catch
            _listaCartelle = Nothing
        End Try
    End Sub

    Private _scopoElencoCartelle As String = ""

    Private Sub CartellaSelezionata(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return

            If String.Equals(_scopoElencoCartelle, "spostacartella", StringComparison.OrdinalIgnoreCase) Then
                _scopoElencoCartelle = ""
                Dim nuovoPadre As String = percorso
                If String.Equals(nuovoPadre, ChiaveRadiceCasella, StringComparison.Ordinal) Then nuovoPadre = ""
                If String.Equals(nuovoPadre, _cartellaSuCuiAgire, StringComparison.OrdinalIgnoreCase) Then
                    MostraMessaggio("Non puoi spostare una cartella dentro se stessa.")
                    Return
                End If
                AvviaSpostamentoCartella(nuovoPadre)
                Return
            End If

            If String.Equals(percorso, ChiaveRadiceCasella, StringComparison.Ordinal) Then
                MostraMessaggio("Questa è la radice della casella: contiene le cartelle, non i messaggi.")
                Return
            End If

            If String.Equals(_scopoElencoCartelle, "sposta", StringComparison.OrdinalIgnoreCase) Then
                _scopoElencoCartelle = ""
                If String.Equals(percorso, _cartellaCorrente, StringComparison.OrdinalIgnoreCase) Then
                    MostraMessaggio("I messaggi sono già in questa cartella.")
                    Return
                End If
                _tipoSpostamentoSpeciale = ""
                _cartellaSpostamento = percorso
                MostraAttesa("Spostamento in corso, attendere")
                AvviaLavoro(New Action(AddressOf LavoroSposta))
                Return
            End If

            _soloNonLetti = False
            _tipoCartellaSpeciale = ""
            _cartellaCorrente = percorso
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaSelezionata: " & ex.Message)
        End Try
    End Sub

    Friend Function PercorsoCartellaPerAccount(chiaveAccount As String) As String
        Try
            If String.IsNullOrWhiteSpace(_tipoCartellaSpeciale) Then Return _cartellaCorrente
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return _cartellaCorrente
            Dim percorso As String = _posta.RisolviPercorsoCartellaSpeciale(chiaveAccount, _tipoCartellaSpeciale)
            If String.IsNullOrWhiteSpace(percorso) Then Return _cartellaCorrente
            Return percorso
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PercorsoCartellaPerAccount: " & ex.Message)
            Return _cartellaCorrente
        End Try
    End Function

    Private Sub ApriCartellaSpeciale(tipo As String)
        Try
            Dim tipoNormalizzato As String = If(tipo, "").Trim().ToLowerInvariant()
            If Not String.Equals(tipoNormalizzato, "inbox", StringComparison.OrdinalIgnoreCase) Then
                _soloNonLetti = False
            End If
            _tipoCartellaSpeciale = tipoNormalizzato
            _cartellaCorrente = ""
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCartellaSpeciale: " & ex.Message)
        End Try
    End Sub

    Private Function PercorsoCartellaSpeciale(tipo As String) As String
        Try
            Dim chiave As String = _chiaveAccountCorrente
            If String.IsNullOrWhiteSpace(chiave) Then
                Dim attivi = _posta.OttieniElencoAccount(True)
                If attivi IsNot Nothing AndAlso attivi.Count > 0 Then chiave = attivi(0).Chiave
            End If
            If String.IsNullOrWhiteSpace(chiave) Then
                Select Case tipo
                    Case "inbox" : Return "INBOX"
                    Case "sent" : Return "Sent"
                    Case "drafts" : Return "Drafts"
                    Case "trash" : Return "Trash"
                    Case "junk" : Return "Junk"
                    Case Else : Return tipo
                End Select
            End If
            Return _posta.RisolviPercorsoCartellaSpeciale(chiave, tipo)
        Catch
            Return tipo
        End Try
    End Function

    Private Function TitoloCartellaCorrente() As String
        If _soloNonLetti Then Return "Da leggere"
        Select Case If(_tipoCartellaSpeciale, "").Trim().ToLowerInvariant()
            Case "inbox" : Return "Posta in arrivo"
            Case "sent" : Return "Posta inviata"
            Case "drafts" : Return "Bozze"
            Case "trash" : Return "Posta eliminata"
            Case "junk" : Return "Posta indesiderata"
        End Select
        Dim p As String = If(_cartellaCorrente, "").ToLowerInvariant()
        If p.Contains("inbox") OrElse p = "posta in arrivo" Then Return "Posta in arrivo"
        If p.Contains("sent") OrElse p.Contains("inviata") Then Return "Posta inviata"
        If p.Contains("draft") OrElse p.Contains("bozz") Then Return "Bozze"
        If p.Contains("trash") OrElse p.Contains("cestino") OrElse p.Contains("eliminat") Then Return "Posta eliminata"
        If p.Contains("junk") OrElse p.Contains("spam") OrElse p.Contains("indesider") Then Return "Posta indesiderata"
        If _soloNonLetti Then Return "Da leggere"
        If Not String.IsNullOrWhiteSpace(_cartellaCorrente) Then Return _cartellaCorrente
        Return "Posta"
    End Function

    Private Sub VoceCerca(sender As Object, e As EventArgs)
        ApriCartellaConScelta(TipoRicerca)
    End Sub

    Private Sub MostraMenuRicerca()
        Try
            _menuRicerca = New WGMenu()
            _menuRicerca.aggiungi("Nuova ricerca", AddressOf VoceNuovaRicerca)

            Dim recenti As List(Of String) = Nothing
            Try
                recenti = _posta.OttieniRicercheRecenti()
            Catch exRecenti As Exception
                log.ScriviLog("MenuPosta.MostraMenuRicerca recenti: " & exRecenti.Message)
            End Try
            If recenti IsNot Nothing AndAlso recenti.Count > 0 Then
                _menuRicerca.aggiungi("Ricerche recenti", AddressOf VoceRicercheRecenti)
            End If

            _menuRicerca.avvia("Cerca nella posta")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuRicerca()
        Try
            If _menuRicerca IsNot Nothing Then _menuRicerca.chiudi()
            _menuRicerca = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiMenuRicerca: " & ex.Message)
            _menuRicerca = Nothing
        End Try
    End Sub

    Private Sub VoceNuovaRicerca(sender As Object, e As EventArgs)
        Try
            ChiudiMenuRicerca()
            ApriCampoRicerca()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceNuovaRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRicercheRecenti(sender As Object, e As EventArgs)
        Try
            Dim recenti As List(Of String) = _posta.OttieniRicercheRecenti()
            If recenti Is Nothing OrElse recenti.Count = 0 Then
                MostraMessaggio("Non c'è ancora nessuna ricerca da riprendere.")
                Return
            End If

            ChiudiMenuRicerca()
            AbbandonaListaRicercheRecenti()
            _listaRicercheRecenti = New WGMenuLista()
            _listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            For Each termine As String In recenti
                _listaRicercheRecenti.aggiungiElemento(termine, termine)
            Next
            _listaRicercheRecenti.aggiungiOpzione("Svuota le ricerche recenti", AddressOf OpzioneSvuotaRicercheRecenti)
            AddHandler _listaRicercheRecenti.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
            AddHandler _listaRicercheRecenti.escPremuto, AddressOf ListaRicercheRecentiEsc
            _listaRicercheRecenti.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceRicercheRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaListaRicercheRecenti()
        Try
            If _listaRicercheRecenti Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaRicercheRecenti
            _listaRicercheRecenti = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
                RemoveHandler precedente.escPremuto, AddressOf ListaRicercheRecentiEsc
            Catch exHandler As Exception
                log.ScriviLog("MenuPosta.AbbandonaListaRicercheRecenti (handler): " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AbbandonaListaRicercheRecenti: " & ex.Message)
            _listaRicercheRecenti = Nothing
        End Try
    End Sub

    Private Sub ListaRicercheRecentiEsc(sender As Object, e As EventArgs)
        Try
            AbbandonaListaRicercheRecenti()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaRicercheRecentiEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSvuotaRicercheRecenti(sender As Object, e As EventArgs)
        Try
            _posta.SvuotaRicercheRecenti()
            AbbandonaListaRicercheRecenti()
            MostraMessaggio("Elenco delle ricerche svuotato.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSvuotaRicercheRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub RicercaRecenteSelezionata(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            AbbandonaListaRicercheRecenti()
            _descrizioneFiltro = ""
            _titoloRisultati = ""
            _testoRicerca = id
            Try
                _posta.SalvaRicercaRecente(id)
            Catch exSalva As Exception
                log.ScriviLog("MenuPosta.RicercaRecenteSelezionata salvataggio: " & exSalva.Message)
            End Try
            MostraAttesa("Ricerca in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCerca))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RicercaRecenteSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoRicerca()
        Try
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi("Testo da cercare", "cerca.testo", True, False, "")
            AddHandler _campiComposizione.Fine, AddressOf CampiCercaFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampoRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiCercaFine(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ""
            If _campiComposizione IsNot Nothing Then testo = _campiComposizione.RecuperaValore("cerca.testo")
            DisposeCampiComposizione()
            If String.IsNullOrWhiteSpace(testo) Then
                MostraMessaggio("Inserisci un testo da cercare.")
                Return
            End If
            _testoRicerca = testo
            _descrizioneFiltro = ""
            _titoloRisultati = ""
            Try
                _posta.SalvaRicercaRecente(testo)
            Catch exSalva As Exception
                log.ScriviLog("MenuPosta.CampiCercaFine salvataggio: " & exSalva.Message)
            End Try
            MostraAttesa("Ricerca in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCerca))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiCercaFine: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroCerca()
        Try
            _elencoMessaggi = New List(Of RiepilogoMessaggio)
            Dim chiavi As List(Of String) = ChiaviDaConsultare()
            If chiavi Is Nothing OrElse chiavi.Count = 0 Then
                log.ScriviLog("MenuPosta.LavoroCerca: nessuna casella da consultare")
                Return
            End If
            For Each chiave As String In chiavi
                Try
                    Dim cartellaDiRicerca As String = PercorsoCartellaPerAccount(chiave)
                    If String.IsNullOrWhiteSpace(cartellaDiRicerca) Then cartellaDiRicerca = _posta.RisolviPercorsoCartellaSpeciale(chiave, "inbox")
                    If String.IsNullOrWhiteSpace(cartellaDiRicerca) Then cartellaDiRicerca = "INBOX"
                    Dim trovati As List(Of RiepilogoMessaggio) = _posta.CercaMessaggi(chiave, _testoRicerca, cartellaDiRicerca)
                    If trovati IsNot Nothing Then _elencoMessaggi.AddRange(trovati)
                Catch exChiave As Exception
                    log.ScriviLog("MenuPosta.LavoroCerca (" & If(chiave, "") & "): " & exChiave.ToString())
                End Try
            Next
            OrdinaElenco(_elencoMessaggi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCerca: " & ex.ToString())
        Finally
            EseguiSuInterfaccia(AddressOf CompletaRicerca)
        End Try
    End Sub

    Private Sub CompletaRicerca()
        Try
            ChiudiAttesa()
            _soloNonLetti = False
            _totaleMessaggi = If(_elencoMessaggi Is Nothing, 0, _elencoMessaggi.Count)

            If _elencoMessaggi Is Nothing OrElse _elencoMessaggi.Count = 0 Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaRicerca errore: " & exErrore.Message)
                End Try
                If String.IsNullOrWhiteSpace(descrizione) Then
                    If Not String.IsNullOrWhiteSpace(_descrizioneFiltro) Then
                        descrizione = "Nessun messaggio corrisponde al filtro."
                    Else
                        descrizione = "Nessun messaggio contiene " & Chr(34) & If(_testoRicerca, "") & Chr(34) & "."
                    End If
                End If
                MostraMessaggio(descrizione)
                Return
            End If

            _listaMessaggiGiaMostrata = False
            MostraListaMessaggi(If(String.IsNullOrWhiteSpace(_titoloRisultati), "Risultati della ricerca", _titoloRisultati))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub OrdinaElenco(elenco As List(Of RiepilogoMessaggio))
        Try
            If elenco Is Nothing OrElse elenco.Count < 2 Then Return
            Select Case _ordinamento
                Case OrdinamentoMessaggi.DataVecchia
                    elenco.Sort(AddressOf ConfrontaMessaggiPerData)
                Case OrdinamentoMessaggi.Oggetto
                    elenco.Sort(AddressOf ConfrontaMessaggiPerOggetto)
                Case OrdinamentoMessaggi.Mittente
                    elenco.Sort(AddressOf ConfrontaMessaggiPerMittente)
                Case Else
                    elenco.Sort(AddressOf ConfrontaMessaggiPerDataDiscendente)
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OrdinaElenco: " & ex.Message)
        End Try
    End Sub

    Private Function ConfrontaMessaggiPerDataDiscendente(a As RiepilogoMessaggio, b As RiepilogoMessaggio) As Integer
        Try
            Return ConfrontaMessaggiPerData(b, a)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfrontaMessaggiPerDataDiscendente: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function ConfrontaMessaggiPerOggetto(a As RiepilogoMessaggio, b As RiepilogoMessaggio) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Dim primo As String = CLSMail.OggettoSenzaPrefissi(If(a.Oggetto, ""))
            Dim secondo As String = CLSMail.OggettoSenzaPrefissi(If(b.Oggetto, ""))
            Dim esito As Integer = String.Compare(primo, secondo, StringComparison.CurrentCultureIgnoreCase)
            If esito <> 0 Then Return esito
            Return ConfrontaMessaggiPerDataDiscendente(a, b)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfrontaMessaggiPerOggetto: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function ConfrontaMessaggiPerMittente(a As RiepilogoMessaggio, b As RiepilogoMessaggio) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Dim esito As Integer = String.Compare(If(a.Mittente, ""), If(b.Mittente, ""), StringComparison.CurrentCultureIgnoreCase)
            If esito <> 0 Then Return esito
            Return ConfrontaMessaggiPerDataDiscendente(a, b)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfrontaMessaggiPerMittente: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub OpzioneOrdina(sender As Object, e As EventArgs)
        Try

            _menuOrdina = New WGMenu()
            _menuOrdina.aggiungi("Dal più recente", AddressOf OrdinaDataRecente)
            _menuOrdina.aggiungi("Dal più vecchio", AddressOf OrdinaDataVecchia)
            _menuOrdina.aggiungi("Per oggetto", AddressOf OrdinaOggetto)
            _menuOrdina.aggiungi("Per mittente", AddressOf OrdinaMittente)
            _menuOrdina.avvia("Ordina i messaggi")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneOrdina: " & ex.Message)
        End Try
    End Sub

    Private Sub OrdinaDataRecente(sender As Object, e As EventArgs)
        ApplicaOrdinamento(OrdinamentoMessaggi.DataRecente, "Ordinati dal più recente.")
    End Sub

    Private Sub OrdinaDataVecchia(sender As Object, e As EventArgs)
        ApplicaOrdinamento(OrdinamentoMessaggi.DataVecchia, "Ordinati dal più vecchio.")
    End Sub

    Private Sub OrdinaOggetto(sender As Object, e As EventArgs)
        ApplicaOrdinamento(OrdinamentoMessaggi.Oggetto, "Ordinati per oggetto.")
    End Sub

    Private Sub OrdinaMittente(sender As Object, e As EventArgs)
        ApplicaOrdinamento(OrdinamentoMessaggi.Mittente, "Ordinati per mittente.")
    End Sub

    Private Sub ApplicaOrdinamento(nuovo As OrdinamentoMessaggi, annuncio As String)
        Try
            _ordinamento = nuovo
            OrdinaElenco(_elencoMessaggi)
            If _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed Then
                _posizioneDaRipristinare = ""
                AggiornaElementiListaMessaggi()
            End If
            MostraMessaggio(annuncio)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApplicaOrdinamento: " & ex.Message)
        End Try
    End Sub

    Private Function MessaggioEvidenziato() As RiepilogoMessaggio
        Try
            Dim voce As String = ""
            If Not UnSoloMessaggioSelezionato(voce) Then Return Nothing
            If _elencoMessaggi Is Nothing Then Return Nothing
            For Each r As RiepilogoMessaggio In _elencoMessaggi
                If String.Equals(r.ChiaveAccount & "|" & r.Id, voce, StringComparison.Ordinal) Then Return r
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MessaggioEvidenziato: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneFiltra(sender As Object, e As EventArgs)
        Try
            _menuFiltra = New WGMenu()
            _menuFiltra.aggiungi("Di questo mittente", AddressOf FiltraMittente)
            _menuFiltra.aggiungi("Con questo oggetto", AddressOf FiltraOggetto)
            _menuFiltra.aggiungi("Di questa conversazione", AddressOf FiltraConversazione)
            _menuFiltra.aggiungi("Per data", AddressOf FiltraPerData)
            _menuFiltra.aggiungi("Togli il filtro", AddressOf TogliFiltro)
            _menuFiltra.avvia("Filtra i messaggi", true)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneFiltra: " & ex.Message)
        End Try
    End Sub

    Private Sub FiltraMittente(sender As Object, e As EventArgs)
        Try
            Dim messaggio As RiepilogoMessaggio = MessaggioEvidenziato()
            If messaggio Is Nothing Then Return
            Dim indirizzo As String = SoloIndirizzo(messaggio.Mittente)
            If String.IsNullOrWhiteSpace(indirizzo) Then indirizzo = If(messaggio.Mittente, "").Trim()
            If String.IsNullOrWhiteSpace(indirizzo) Then
                MostraMessaggio("Di questo messaggio non conosco il mittente.")
                Return
            End If
            AvviaFiltro(CriterioFiltroMessaggi.Mittente, indirizzo, DateTime.MinValue, "Messaggi di " & indirizzo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FiltraMittente: " & ex.Message)
        End Try
    End Sub

    Private Sub FiltraOggetto(sender As Object, e As EventArgs)
        Try
            
            Dim messaggio As RiepilogoMessaggio = MessaggioEvidenziato()
            If messaggio Is Nothing Then Return
            Dim oggetto As String = If(messaggio.Oggetto, "").Trim()
            If String.IsNullOrWhiteSpace(oggetto) Then
                MostraMessaggio("Questo messaggio non ha oggetto.")
                Return
            End If
            AvviaFiltro(CriterioFiltroMessaggi.Oggetto, oggetto, DateTime.MinValue, "Messaggi con oggetto " & oggetto)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FiltraOggetto: " & ex.Message)
        End Try
    End Sub

    Private Sub FiltraConversazione(sender As Object, e As EventArgs)
        Try
            Dim messaggio As RiepilogoMessaggio = MessaggioEvidenziato()
            If messaggio Is Nothing Then Return
            Dim oggetto As String = CLSMail.OggettoSenzaPrefissi(If(messaggio.Oggetto, ""))
            If String.IsNullOrWhiteSpace(oggetto) Then
                MostraMessaggio("Questo messaggio non ha un oggetto da cui riconoscere la conversazione.")
                Return
            End If
            AvviaFiltro(CriterioFiltroMessaggi.Conversazione, oggetto, DateTime.MinValue, "Conversazione " & oggetto)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FiltraConversazione: " & ex.Message)
        End Try
    End Sub

    Private Sub FiltraPerData(sender As Object, e As EventArgs)
        Try
            
            _menuFiltraData = New WGMenu()
            _menuFiltraData.aggiungi("Di oggi", AddressOf FiltraDataOggi)
            _menuFiltraData.aggiungi("Degli ultimi sette giorni", AddressOf FiltraDataSetteGiorni)
            _menuFiltraData.aggiungi("Degli ultimi trenta giorni", AddressOf FiltraDataTrentaGiorni)
            _menuFiltraData.aggiungi("A partire da una data che indico", AddressOf FiltraDataScelta)
            _menuFiltraData.avvia("Filtra per data")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FiltraPerData: " & ex.Message)
        End Try
    End Sub

    Private Sub FiltraDataOggi(sender As Object, e As EventArgs)
        
        AvviaFiltro(CriterioFiltroMessaggi.Data, "", DateTime.Today, "Messaggi di oggi")
    End Sub

    Private Sub FiltraDataSetteGiorni(sender As Object, e As EventArgs)
        
        AvviaFiltro(CriterioFiltroMessaggi.Data, "", DateTime.Today.AddDays(-7), "Messaggi degli ultimi sette giorni")
    End Sub

    Private Sub FiltraDataTrentaGiorni(sender As Object, e As EventArgs)
        
        AvviaFiltro(CriterioFiltroMessaggi.Data, "", DateTime.Today.AddDays(-30), "Messaggi degli ultimi trenta giorni")
    End Sub

    Private Sub FiltraDataScelta(sender As Object, e As EventArgs)
        Try
            
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi("Data di partenza, giorno barra mese barra anno", "filtro.data", True, False, DateTime.Today.ToString("dd/MM/yyyy"))
            AddHandler _campiComposizione.Fine, AddressOf CampiFiltroDataFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FiltraDataScelta: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiFiltroDataFine(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ""
            If _campiComposizione IsNot Nothing Then testo = _campiComposizione.RecuperaValore("filtro.data")
            DisposeCampiComposizione()
            Dim data As DateTime
            If Not DateTime.TryParse(If(testo, "").Trim(), data) Then
                MostraMessaggio("Non ho capito la data. Scrivila nella forma giorno barra mese barra anno.")
                Return
            End If
            AvviaFiltro(CriterioFiltroMessaggi.Data, "", data.Date, "Messaggi dal " & data.ToString("dd/MM/yyyy"))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiFiltroDataFine: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliFiltro(sender As Object, e As EventArgs)
        Try
            
            If Not _listaMessaggiRisultatiRicerca Then
                MostraMessaggio("Nessun filtro attivo.")
                Return
            End If
            _descrizioneFiltro = ""
            _titoloRisultati = ""
            _listaMessaggiRisultatiRicerca = False
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TogliFiltro: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaFiltro(criterio As CriterioFiltroMessaggi, testo As String, data As DateTime, titolo As String)
        Try
            _filtroCriterio = criterio
            _filtroTesto = If(testo, "")
            _filtroData = data
            _descrizioneFiltro = If(titolo, "")
            _titoloRisultati = If(titolo, "Messaggi filtrati")
            MostraAttesa("Filtro in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroFiltra))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaFiltro: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroFiltra()
        Try
            _elencoMessaggi = New List(Of RiepilogoMessaggio)
            Dim chiavi As List(Of String) = ChiaviDaConsultare()
            If chiavi Is Nothing OrElse chiavi.Count = 0 Then Return
            For Each chiave As String In chiavi
                Try
                    Dim cartella As String = PercorsoCartellaPerAccount(chiave)
                    If String.IsNullOrWhiteSpace(cartella) Then cartella = "INBOX"
                    Dim trovati As List(Of RiepilogoMessaggio) = _posta.CercaMessaggiConCriterio(chiave, cartella, _filtroCriterio, _filtroTesto, _filtroData)
                    If trovati IsNot Nothing Then _elencoMessaggi.AddRange(trovati)
                Catch exChiave As Exception
                    log.ScriviLog("MenuPosta.LavoroFiltra (" & If(chiave, "") & "): " & exChiave.ToString())
                End Try
            Next
            OrdinaElenco(_elencoMessaggi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroFiltra: " & ex.ToString())
        Finally
            EseguiSuInterfaccia(AddressOf CompletaRicerca)
        End Try
    End Sub

    Private Sub VoceImpostazioni(sender As Object, e As EventArgs)
        Try
            MostraMenuImpostazioni()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuImpostazioni()
        Try
            _menuImpostazioni = New WGMenu()
            _menuImpostazioni.aggiungi("Nuovo account", AddressOf VoceAggiungiAccount)
            _menuImpostazioni.aggiungi("Account", AddressOf VoceElencoAccount)
            If _posta.OttieniElencoAccount(True).Count > 1 Then
                _menuImpostazioni.aggiungi("Caselle da consultare", AddressOf VoceCaselleDaConsultare)
            End If
            _menuImpostazioni.aggiungi("Firme", AddressOf VoceFirme)
            _menuImpostazioni.aggiungi("Regole", AddressOf VoceRegole)
            _menuImpostazioni.avvia("Impostazioni")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAggiungiAccount(sender As Object, e As EventArgs)
        Try
            _accountInModifica = New ImpostazioniAccountMail()
            MostraSceltaProtocollo()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceAggiungiAccount: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoAccount(sender As Object, e As EventArgs)
        Try
            _scopoAccount = ScopoAccount.ImpostazioniAccount
            MostraElencoAccount(False)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceElencoAccount: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCaselleDaConsultare(sender As Object, e As EventArgs)
        Try
            MostraMenuCaselle()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceCaselleDaConsultare: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuCaselle()
        Try
            _menuCaselle = New WGMenu()
            _menuCaselle.aggiungi("Chiedi sempre quale casella usare", AddressOf ImpostaCaselleChiedi)
            _menuCaselle.aggiungi("Tutte le caselle insieme, in ordine di data", AddressOf ImpostaCaselleTutte)
            _menuCaselle.aggiungi("Una sola casella", AddressOf ImpostaCaselleSingola)
            _menuCaselle.aggiungi("Alcune caselle", AddressOf ImpostaCaselleMultiple)
            _menuCaselle.avvia("Caselle da consultare", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuCaselle: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaCaselleChiedi(sender As Object, e As EventArgs)
        Try
            _modalitaCaselle = ModalitaCaselle.Chiedi
            _chiaviAccountFiltro = New List(Of String)
            MostraMessaggio("All'apertura della posta in arrivo verrà chiesto ogni volta quale casella consultare.")
        Catch
        End Try
    End Sub

    Private Sub ImpostaCaselleTutte(sender As Object, e As EventArgs)
        Try
            _modalitaCaselle = ModalitaCaselle.Tutte
            _chiaviAccountFiltro = New List(Of String)
            MostraMessaggio("I messaggi di tutte le caselle verranno mischiati in un'unica lista, in ordine di data.")
        Catch
        End Try
    End Sub

    Private Sub ImpostaCaselleSingola(sender As Object, e As EventArgs)
        Try
            _scopoAccount = ScopoAccount.SceltaCasellaSingola
            MostraElencoAccount(False)
        Catch
        End Try
    End Sub

    Private Sub MostraSceltaProtocollo()
        Try
            Dim menuProto As New WGMenu()
            menuProto.aggiungi("IMAP", AddressOf ScegliImap)
            menuProto.aggiungi("POP3", AddressOf ScegliPop3)
            menuProto.avvia("Protocollo in arrivo", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaProtocollo: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliImap(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            _accountInModifica.ProtocolloRicezione = "IMAP"
            ApriCampiIdentita()
        Catch
        End Try
    End Sub

    Private Sub ScegliPop3(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            _accountInModifica.ProtocolloRicezione = "POP3"
            ApriCampiIdentita()
        Catch
        End Try
    End Sub

    Private Sub ImpostaCaselleMultiple(sender As Object, e As EventArgs)
        Try
            _scopoAccount = ScopoAccount.SceltaCaselleMultiple
            MostraElencoAccount(True)
        Catch
        End Try
    End Sub

    Private Sub MostraOpzioniAccountSelezionato(chiave As String)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(chiave)
            If impostazioni Is Nothing Then
                MostraMessaggio("Account non trovato.")
                Return
            End If
            _accountInModifica = impostazioni
            _menuOpzioniAccount = New WGMenu()
            _menuOpzioniAccount.aggiungi(If(impostazioni.ControlloAutomaticoRicezione, "Disattiva controllo automatico", "Attiva controllo automatico"), AddressOf ToggleControlloAutomatico)
            _menuOpzioniAccount.aggiungi(If(impostazioni.CancellaMessaggiDalServer, "Non cancellare dal server", "Cancella dal server"), AddressOf ToggleCancellaDalServer)
            If Not _posta.AccountEGoogle(impostazioni) Then
                If _posta.AccountSupportaRubrica(impostazioni) Then
                    _menuOpzioniAccount.aggiungi(If(impostazioni.SincronizzaRubrica, "Disattiva sincronizzazione contatti", "Attiva sincronizzazione contatti"), AddressOf ToggleSincronizzaRubrica)
                End If
                _menuOpzioniAccount.aggiungi("Sincronizzazione contatti e agenda", AddressOf VoceSincronizzazioneAccount)
                _menuOpzioniAccount.aggiungi("Cerca sincronizzazione in automatico", AddressOf VoceScopriSincronizzazione)
            End If
            _menuOpzioniAccount.aggiungi("Intervallo controllo: " & impostazioni.IntervalloControlloMinuti.ToString() & " minuti", AddressOf VoceIntervalloControllo)
            _menuOpzioniAccount.avvia(DescriviAccount(impostazioni), True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraOpzioniAccountSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceSincronizzazioneAccount(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            _configurazioneManuale = False
            ApriCampiSincronizzazione(True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceSincronizzazioneAccount: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceScopriSincronizzazione(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            MostraAttesa("Ricerca sincronizzazione in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroScopriSincronizzazione))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceScopriSincronizzazione: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroScopriSincronizzazione()
        Try
            If _accountInModifica Is Nothing Then Return
            _accountInModifica.UrlRubricaCardDav = ""
            _accountInModifica.UrlAgendaCalDav = ""
            _posta.ConfiguraSincronizzazioneAutomatica(_accountInModifica)
            _posta.AggiornaAccount(_accountInModifica)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroScopriSincronizzazione: " & ex.Message)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaScopriSincronizzazione)
        End Try
    End Sub

    Private Sub CompletaScopriSincronizzazione()
        Try
            ChiudiAttesa()
            If _accountInModifica Is Nothing Then Return
            Dim rubrica As Boolean = Not String.IsNullOrWhiteSpace(_accountInModifica.UrlRubricaCardDav)
            Dim agenda As Boolean = Not String.IsNullOrWhiteSpace(_accountInModifica.UrlAgendaCalDav)
            If rubrica AndAlso agenda Then
                MostraMessaggio("Trovata la sincronizzazione dei contatti e dell'agenda.")
            ElseIf rubrica Then
                MostraMessaggio("Trovata la sincronizzazione dei contatti.")
            ElseIf agenda Then
                MostraMessaggio("Trovata solo la sincronizzazione dell'agenda.")
            Else
                MostraMessaggio("Nessuna sincronizzazione disponibile per questa casella.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaScopriSincronizzazione: " & ex.Message)
        End Try
    End Sub

    Private Sub ToggleControlloAutomatico(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            _accountInModifica.ControlloAutomaticoRicezione = Not _accountInModifica.ControlloAutomaticoRicezione
            _posta.AggiornaAccount(_accountInModifica)
            MostraMessaggio(If(_accountInModifica.ControlloAutomaticoRicezione, "Controllo automatico attivo.", "Controllo automatico disattivo."))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ToggleControlloAutomatico: " & ex.Message)
        End Try
    End Sub

    Private Sub ToggleCancellaDalServer(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            _accountInModifica.CancellaMessaggiDalServer = Not _accountInModifica.CancellaMessaggiDalServer
            _posta.AggiornaAccount(_accountInModifica)
            MostraMessaggio(If(_accountInModifica.CancellaMessaggiDalServer, "Cancellazione dal server attiva.", "Cancellazione dal server disattiva."))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ToggleCancellaDalServer: " & ex.Message)
        End Try
    End Sub

    Private Sub ToggleSincronizzaRubrica(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            _accountInModifica.SincronizzaRubrica = Not _accountInModifica.SincronizzaRubrica
            _posta.AggiornaAccount(_accountInModifica)
            MostraMessaggio(If(_accountInModifica.SincronizzaRubrica, "Sync rubrica attiva.", "Sync rubrica disattiva."))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ToggleSincronizzaRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoAccount(Optional multi As Boolean = False)
        Try
            CaricaElencoAccount()
            _listaAccount = New WGMenuLista()
            _listaAccount.multiselezioneConsentita = multi
            _listaAccount.impostaTitolo("Account")
            If _elencoAccount.Count = 0 Then
                _listaAccount.aggiungiElemento("Nessun account")
            Else
                For Each impostazioni As ImpostazioniAccountMail In _elencoAccount
                    _listaAccount.aggiungiElemento(DescriviAccount(impostazioni), impostazioni.Chiave)
                Next
            End If
            If Not multi Then
                _listaAccount.aggiungiOpzione("Modifica", AddressOf OpzioneAccountModifica)
                _listaAccount.aggiungiOpzione("Attiva o disattiva", AddressOf OpzioneAccountAttiva)
                _listaAccount.aggiungiOpzione("Elimina", AddressOf OpzioneAccountElimina)
                _listaAccount.aggiungiOpzione("Opzioni", AddressOf OpzioneAccountOpzioni)
            End If
            AddHandler _listaAccount.ElementoSelezionato, AddressOf AccountSelezionato
            AddHandler _listaAccount.escPremuto, AddressOf ListaAccountEsc
            If multi Then
                AddHandler _listaAccount.ElementiSelezionati, AddressOf AccountMultiSelezionati
            End If
            _listaAccount.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraElencoAccount: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAccountEsc(sender As Object, e As EventArgs)
        Try
            If _listaAccount IsNot Nothing Then _listaAccount.abbandona()
            _listaAccount = Nothing
        Catch
            _listaAccount = Nothing
        End Try
    End Sub

    Private Function ChiaveAccountSelezionatoInLista() As String
        Try
            If _listaAccount Is Nothing OrElse _listaAccount.IsDisposed Then Return ""
            Dim elementi As List(Of String) = _listaAccount.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Dim elemento As String = elementi(0)
            Return If(elemento, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaveAccountSelezionatoInLista: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub OpzioneAccountModifica(sender As Object, e As EventArgs)
        Try
            Dim id As String = ChiaveAccountSelezionatoInLista()
            If String.IsNullOrWhiteSpace(id) Then Return
            _accountInModifica = OttieniImpostazioniAccount(id)
            If _accountInModifica Is Nothing Then Return
            _configurazioneManuale = False
            ApriCampiIdentita()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAccountModifica: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAccountAttiva(sender As Object, e As EventArgs)
        Try
            Dim id As String = ChiaveAccountSelezionatoInLista()
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(id)
            If impostazioni Is Nothing Then Return
            impostazioni.Attivo = Not impostazioni.Attivo
            _posta.AggiornaAccount(impostazioni)
            MostraMessaggio(If(impostazioni.Attivo, "Account attivato.", "Account disattivato."))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAccountAttiva: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAccountElimina(sender As Object, e As EventArgs)
        Try
            Dim id As String = ChiaveAccountSelezionatoInLista()
            If String.IsNullOrWhiteSpace(id) Then Return
            _accountInModifica = OttieniImpostazioniAccount(id)
            If _accountInModifica Is Nothing Then Return
            _menuConferma = New WGMenu()
            _menuConferma.aggiungi("No", AddressOf ConfermaEliminaNo)
            _menuConferma.aggiungi("Sì, elimina", AddressOf ConfermaEliminaSi)
            _menuConferma.avvia("Eliminare " & DescriviAccount(_accountInModifica) & "?", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAccountElimina: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAccountOpzioni(sender As Object, e As EventArgs)
        Try
            Dim id As String = ChiaveAccountSelezionatoInLista()
            If String.IsNullOrWhiteSpace(id) Then Return
            MostraOpzioniAccountSelezionato(id)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAccountOpzioni: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceIntervalloControllo(sender As Object, e As EventArgs)
        Try
            Dim menuInt As New WGMenu()
            menuInt.aggiungi("5 minuti", AddressOf Intervallo5)
            menuInt.aggiungi("10 minuti", AddressOf Intervallo10)
            menuInt.aggiungi("15 minuti", AddressOf Intervallo15)
            menuInt.aggiungi("30 minuti", AddressOf Intervallo30)
            menuInt.aggiungi("60 minuti", AddressOf Intervallo60)
            menuInt.avvia("Intervallo controllo", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceIntervalloControllo: " & ex.Message)
        End Try
    End Sub

    Private Sub Intervallo5(sender As Object, e As EventArgs)
        ImpostaIntervallo(5)
    End Sub
    Private Sub Intervallo10(sender As Object, e As EventArgs)
        ImpostaIntervallo(10)
    End Sub
    Private Sub Intervallo15(sender As Object, e As EventArgs)
        ImpostaIntervallo(15)
    End Sub
    Private Sub Intervallo30(sender As Object, e As EventArgs)
        ImpostaIntervallo(30)
    End Sub
    Private Sub Intervallo60(sender As Object, e As EventArgs)
        ImpostaIntervallo(60)
    End Sub

    Private Sub ImpostaIntervallo(minuti As Integer)
        Try
            If _accountInModifica Is Nothing Then Return
            _accountInModifica.IntervalloControlloMinuti = minuti
            _posta.AggiornaAccount(_accountInModifica)
            MostraMessaggio("Intervallo impostato a " & minuti.ToString() & " minuti.")
        Catch
        End Try
    End Sub

    Private Sub AccountSelezionato(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            Select Case _scopoAccount
                Case ScopoAccount.Modifica
                    _accountInModifica = OttieniImpostazioniAccount(id)
                    If _accountInModifica Is Nothing Then
                        MostraMessaggio("Account non trovato.")
                        Return
                    End If
                    ApriCampiIdentita()
                Case ScopoAccount.AttivaDisattiva
                    Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(id)
                    If impostazioni Is Nothing Then Return
                    impostazioni.Attivo = Not impostazioni.Attivo
                    _posta.AggiornaAccount(impostazioni)
                    MostraMessaggio(If(impostazioni.Attivo, "Account attivato.", "Account disattivato."))
                Case ScopoAccount.Eliminazione
                    _accountInModifica = OttieniImpostazioniAccount(id)
                    If _accountInModifica Is Nothing Then Return
                    _menuConferma = New WGMenu()
                    _menuConferma.aggiungi("No", AddressOf ConfermaEliminaNo)
                    _menuConferma.aggiungi("Sì, elimina", AddressOf ConfermaEliminaSi)
                    _menuConferma.avvia("Eliminare " & DescriviAccount(_accountInModifica) & "?", True)
                Case ScopoAccount.ImpostazioniAccount
                    MostraOpzioniAccountSelezionato(id)
                Case ScopoAccount.SceltaCasellaSingola
                    _modalitaCaselle = ModalitaCaselle.Singola
                    _chiaveAccountCorrente = id
                    _chiaviAccountFiltro = New List(Of String) From {id}
                    MostraMessaggio("Si consulterà una sola casella.")
                Case ScopoAccount.ControllaAltro
                    AbbandonaSceltaAccount()
                    _chiaveAccountCorrente = id
                    _tipoCartellaSpeciale = "inbox"
                    _cartellaCorrente = ""
                    AvviaCaricamentoMessaggi()
                Case ScopoAccount.ChiediPostaInArrivo
                    AbbandonaSceltaAccount()
                    _chiaveAccountCorrente = id
                    MostraMenuFiltroPostaInArrivo()
                Case ScopoAccount.ChiediInvio
                    AbbandonaSceltaAccount()
                    _chiaveAccountCorrente = id
                    ApriComposizione()
                Case ScopoAccount.ChiediBozze
                    AbbandonaSceltaAccount()
                    _chiaveAccountCorrente = id
                    _chiaviAccountFiltro = New List(Of String) From {id}
                    ApriCartellaSpeciale("drafts")
                Case ScopoAccount.ChiediCartelle
                    AbbandonaSceltaAccount()
                    _chiaveAccountCorrente = id
                    AvviaLetturaCartelle()
                Case ScopoAccount.ChiediRegola
                    AbbandonaSceltaAccount()
                    If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
                    _regolaInModifica.ChiaveAccount = id
                    MostraSceltaCampoRegola()
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AccountSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaSceltaAccount()
        Try
            If _listaAccount Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaAccount
            _listaAccount = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf AccountSelezionato
                RemoveHandler precedente.escPremuto, AddressOf ListaAccountEsc
            Catch exHandler As Exception
                log.ScriviLog("MenuPosta.AbbandonaSceltaAccount (handler): " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AbbandonaSceltaAccount: " & ex.Message)
            _listaAccount = Nothing
        End Try
    End Sub

    Private Sub AccountMultiSelezionati(ids As List(Of String))
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then
                MostraMessaggio("Nessun account selezionato.")
                Return
            End If
            _modalitaCaselle = ModalitaCaselle.Multiple
            _chiaviAccountFiltro = New List(Of String)(ids)
            _chiaveAccountCorrente = ids(0)
            MostraMessaggio("Si consulteranno " & ids.Count.ToString() & " caselle.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AccountMultiSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaEliminaNo(sender As Object, e As EventArgs)
    End Sub

    Private Sub ConfermaEliminaSi(sender As Object, e As EventArgs)
        Try
            If _accountInModifica Is Nothing Then Return
            Dim chiave As String = _accountInModifica.Chiave
            If _posta.EliminaAccount(chiave, True) Then
                If _chiaveAccountCorrente = chiave Then _chiaveAccountCorrente = ""
                CaricaElencoAccount()
                MostraMessaggio("Account eliminato.")
            Else
                MostraMessaggio("Eliminazione non riuscita.")
            End If
            _accountInModifica = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfermaEliminaSi: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiIdentita()
        Try
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            _campiAccount = New Campi()
            _campiAccount.SalvaSuEscape = False
            _campiAccount.Aggiungi("Nome visualizzato", "acc.nome", True, False, _accountInModifica.NomeVisualizzato)
            _campiAccount.Aggiungi("Email", "acc.email", True, False, _accountInModifica.IndirizzoEmail)
            _campiAccount.Aggiungi("Utente", "acc.utente", True, False, _accountInModifica.NomeUtente)
            _campiAccount.AggiungiPassword("Password", "acc.password", False, False, _accountInModifica.Password)
            AddHandler _campiAccount.Fine, AddressOf CampiIdentitaConfermato
            AddHandler _campiAccount.Chiusura, AddressOf CampiAccountChiusura
            _campiAccount.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiIdentita: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiServer(Optional vuoti As Boolean = False)
        Try
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            _campiAccount = New Campi()
            _campiAccount.SalvaSuEscape = False
            If String.Equals(_accountInModifica.ProtocolloRicezione, "POP3", StringComparison.OrdinalIgnoreCase) Then
                _campiAccount.AggiungiHost("Server POP3", "acc.pop3", True, False, If(vuoti, "", _accountInModifica.ServerPop3))
                _campiAccount.AggiungiNumerico("Porta POP3", "acc.portapop3", True, False, If(vuoti, "995", _accountInModifica.PortaPop3.ToString()))
            Else
                _campiAccount.AggiungiHost("Server IMAP", "acc.imap", True, False, If(vuoti, "", _accountInModifica.ServerImap))
                _campiAccount.AggiungiNumerico("Porta IMAP", "acc.portaimap", True, False, If(vuoti, "993", _accountInModifica.PortaImap.ToString()))
            End If
            _campiAccount.AggiungiHost("Server SMTP", "acc.smtp", True, False, If(vuoti, "", _accountInModifica.ServerSmtp))
            _campiAccount.AggiungiNumerico("Porta SMTP", "acc.portasmtp", True, False, If(vuoti, "587", _accountInModifica.PortaSmtp.ToString()))
            AddHandler _campiAccount.Fine, AddressOf CampiServerConfermato
            AddHandler _campiAccount.Chiusura, AddressOf CampiAccountChiusura
            _campiAccount.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiServer: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiSincronizzazione(Optional salvaSubito As Boolean = False)
        Try
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            _salvaSincronizzazioneSubito = salvaSubito
            _campiAccount = New Campi()
            _campiAccount.SalvaSuEscape = False
            _campiAccount.Aggiungi("Indirizzo sincronizzazione contatti", "acc.carddav", False, False, _accountInModifica.UrlRubricaCardDav)
            _campiAccount.Aggiungi("Indirizzo sincronizzazione agenda", "acc.caldav", False, False, _accountInModifica.UrlAgendaCalDav)
            AddHandler _campiAccount.Fine, AddressOf CampiSincronizzazioneConfermato
            AddHandler _campiAccount.Chiusura, AddressOf CampiAccountChiusura
            _campiAccount.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiSincronizzazione: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiSincronizzazioneConfermato(sender As Object, e As EventArgs)
        Try
            If _campiAccount Is Nothing OrElse _accountInModifica Is Nothing Then Return
            _accountInModifica.UrlRubricaCardDav = If(_campiAccount.RecuperaValore("acc.carddav"), "").Trim()
            _accountInModifica.UrlAgendaCalDav = If(_campiAccount.RecuperaValore("acc.caldav"), "").Trim()
            _accountInModifica.SincronizzaRubrica = Not String.IsNullOrWhiteSpace(_accountInModifica.UrlRubricaCardDav)
            DisposeCampiAccount()
            If _salvaSincronizzazioneSubito Then
                _salvaSincronizzazioneSubito = False
                If _posta.AggiornaAccount(_accountInModifica) Then
                    MostraMessaggio("Sincronizzazione aggiornata.")
                Else
                    MostraMessaggio("Salvataggio non riuscito.")
                End If
                Return
            End If
            MostraAttesa("Prova connessione in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroProvaConnessione))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiSincronizzazioneConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub DisposeCampiAccount()
        Try
            If _campiAccount Is Nothing Then Return
            Try
                RemoveHandler _campiAccount.Fine, AddressOf CampiIdentitaConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiAccount.Fine, AddressOf CampiServerConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiAccount.Fine, AddressOf CampiSincronizzazioneConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiAccount.Chiusura, AddressOf CampiAccountChiusura
            Catch
            End Try
            
            _campiAccount = Nothing
        Catch
            _campiAccount = Nothing
        End Try
    End Sub

    Private Sub CampiAccountChiusura(sender As Object, e As EventArgs)
        Try
            DisposeCampiAccount()
        Catch
        End Try
    End Sub

    Private Sub CampiIdentitaConfermato(sender As Object, e As EventArgs)
        Try
            If _campiAccount Is Nothing OrElse _accountInModifica Is Nothing Then Return
            _accountInModifica.NomeVisualizzato = _campiAccount.RecuperaValore("acc.nome")
            _accountInModifica.IndirizzoEmail = _campiAccount.RecuperaValore("acc.email")
            _accountInModifica.NomeUtente = _campiAccount.RecuperaValore("acc.utente")
            _accountInModifica.Password = _campiAccount.RecuperaValore("acc.password")
            If String.IsNullOrWhiteSpace(_accountInModifica.NomeUtente) Then _accountInModifica.NomeUtente = _accountInModifica.IndirizzoEmail
            DisposeCampiAccount()
            MostraAttesa("Ricerca configurazione in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroDiscovery))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiIdentitaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroDiscovery()
        Try
            Dim email As String = If(_accountInModifica Is Nothing, "", _accountInModifica.IndirizzoEmail)
            Dim scoperta As ImpostazioniAccountMail = _posta.ScopriConfigurazioneProvider(email)
            If _accountInModifica Is Nothing Then _accountInModifica = New ImpostazioniAccountMail()
            If Not String.IsNullOrWhiteSpace(scoperta.ServerImap) Then
                _accountInModifica.ServerImap = scoperta.ServerImap
                _accountInModifica.PortaImap = scoperta.PortaImap
                _accountInModifica.SicurezzaImap = scoperta.SicurezzaImap
            End If
            If Not String.IsNullOrWhiteSpace(scoperta.ServerPop3) Then
                _accountInModifica.ServerPop3 = scoperta.ServerPop3
                _accountInModifica.PortaPop3 = scoperta.PortaPop3
                _accountInModifica.SicurezzaPop3 = scoperta.SicurezzaPop3
            End If
            If Not String.IsNullOrWhiteSpace(scoperta.ServerSmtp) Then
                _accountInModifica.ServerSmtp = scoperta.ServerSmtp
                _accountInModifica.PortaSmtp = scoperta.PortaSmtp
                _accountInModifica.SicurezzaSmtp = scoperta.SicurezzaSmtp
            End If
            If Not String.IsNullOrWhiteSpace(scoperta.NomeUtente) AndAlso String.IsNullOrWhiteSpace(_accountInModifica.NomeUtente) Then
                _accountInModifica.NomeUtente = scoperta.NomeUtente
            End If
            If Not String.IsNullOrWhiteSpace(scoperta.UrlRubricaCardDav) Then
                _accountInModifica.UrlRubricaCardDav = scoperta.UrlRubricaCardDav
            End If
            If Not String.IsNullOrWhiteSpace(scoperta.UrlAgendaCalDav) Then
                _accountInModifica.UrlAgendaCalDav = scoperta.UrlAgendaCalDav
            End If
            _accountInModifica.SincronizzaRubrica = Not String.IsNullOrWhiteSpace(_accountInModifica.UrlRubricaCardDav)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroDiscovery: " & ex.Message)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaDiscovery)
        End Try
    End Sub

    Private Sub CompletaDiscovery()
        Try
            ChiudiAttesa()
            Dim trovata As Boolean = _accountInModifica IsNot Nothing AndAlso
                (Not String.IsNullOrWhiteSpace(_accountInModifica.ServerImap) OrElse Not String.IsNullOrWhiteSpace(_accountInModifica.ServerPop3)) AndAlso
                Not String.IsNullOrWhiteSpace(_accountInModifica.ServerSmtp)
            If trovata Then
                ChiudiDomandaAutomatico()
                _domandaAutomatico = New WGDomanda()
                AddHandler _domandaAutomatico.rispostadomanda, AddressOf RispostaConfiguraAutomatico
                _domandaAutomatico.avvia(DescriviConfigurazioneTrovata())
            Else
                _configurazioneManuale = True
                ApriCampiServer(True)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaDiscovery: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviConfigurazioneTrovata() As String
        Try
            If _accountInModifica Is Nothing Then Return "Vuoi configurare l'account in automatico?"
            Dim testo As New StringBuilder()
            testo.Append("Ho trovato la configurazione per ")
            testo.Append(If(_accountInModifica.IndirizzoEmail, ""))
            testo.Append(". ")
            If String.Equals(_accountInModifica.ProtocolloRicezione, "POP3", StringComparison.OrdinalIgnoreCase) Then
                testo.Append("Posta in arrivo POP3 ")
                testo.Append(If(_accountInModifica.ServerPop3, ""))
            Else
                testo.Append("Posta in arrivo IMAP ")
                testo.Append(If(_accountInModifica.ServerImap, ""))
            End If
            testo.Append(", posta in uscita SMTP ")
            testo.Append(If(_accountInModifica.ServerSmtp, ""))
            testo.Append(". ")
            If Not String.IsNullOrWhiteSpace(_accountInModifica.UrlRubricaCardDav) Then
                testo.Append("E' disponibile anche la sincronizzazione automatica dei contatti")
                If Not String.IsNullOrWhiteSpace(_accountInModifica.UrlAgendaCalDav) Then
                    testo.Append(" e dell'agenda")
                End If
                testo.Append(". ")
            End If
            testo.Append("Vuoi configurare l'account in automatico?")
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviConfigurazioneTrovata: " & ex.Message)
            Return "Vuoi configurare l'account in automatico?"
        End Try
    End Function

    Private Sub RispostaConfiguraAutomatico(risposta As DialogResult)
        Try
            ChiudiDomandaAutomatico()
            If risposta = DialogResult.Yes Then
                ConfiguraAutomaticoSi()
            Else
                ConfiguraAutomaticoNo()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RispostaConfiguraAutomatico: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfiguraAutomaticoSi()
        Try
            If _accountInModifica Is Nothing Then Return
            _configurazioneManuale = False
            MostraAttesa("Configurazione automatica in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSincronizzazioneAutomatica))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfiguraAutomaticoSi: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSincronizzazioneAutomatica()
        Try
            If _accountInModifica Is Nothing Then Return
            _posta.ConfiguraSincronizzazioneAutomatica(_accountInModifica)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroSincronizzazioneAutomatica: " & ex.Message)
        Finally
            Try
                _esitoProva = _posta.ProvaConnessioneAccount(_accountInModifica)
                EseguiSuInterfaccia(AddressOf CompletaProvaConnessioneMemorizzata)
            Catch exProva As Exception
                log.ScriviLog("MenuPosta.LavoroSincronizzazioneAutomatica prova: " & exProva.Message)
                EseguiSuInterfaccia(AddressOf SegnalaProvaNonRiuscita)
            End Try
        End Try
    End Sub

    Private Sub CompletaProvaConnessioneMemorizzata()
        Try
            CompletaProvaConnessione(_esitoProva)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaProvaConnessioneMemorizzata: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnalaProvaNonRiuscita()
        Try
            ChiudiAttesa()
            MostraMessaggio("Prova connessione non riuscita.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SegnalaProvaNonRiuscita: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfiguraAutomaticoNo()
        Try
            If _accountInModifica IsNot Nothing Then
                _accountInModifica.ServerImap = ""
                _accountInModifica.ServerPop3 = ""
                _accountInModifica.ServerSmtp = ""
                _accountInModifica.UrlRubricaCardDav = ""
                _accountInModifica.UrlAgendaCalDav = ""
                _accountInModifica.SincronizzaRubrica = False
            End If
            _configurazioneManuale = True
            ApriCampiServer(True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfiguraAutomaticoNo: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiServerConfermato(sender As Object, e As EventArgs)
        Try
            If _campiAccount Is Nothing OrElse _accountInModifica Is Nothing Then Return
            If String.Equals(_accountInModifica.ProtocolloRicezione, "POP3", StringComparison.OrdinalIgnoreCase) Then
                _accountInModifica.ServerPop3 = _campiAccount.RecuperaValore("acc.pop3")
                _accountInModifica.PortaPop3 = ConvertiInIntero(_campiAccount.RecuperaValore("acc.portapop3"), 995)
            Else
                _accountInModifica.ServerImap = _campiAccount.RecuperaValore("acc.imap")
                _accountInModifica.PortaImap = ConvertiInIntero(_campiAccount.RecuperaValore("acc.portaimap"), 993)
            End If
            _accountInModifica.ServerSmtp = _campiAccount.RecuperaValore("acc.smtp")
            _accountInModifica.PortaSmtp = ConvertiInIntero(_campiAccount.RecuperaValore("acc.portasmtp"), 587)
            DisposeCampiAccount()
            If String.Equals(_accountInModifica.ProtocolloRicezione, "POP3", StringComparison.OrdinalIgnoreCase) Then
                _scopoSicurezza = ScopoSicurezza.Pop3
            Else
                _scopoSicurezza = ScopoSicurezza.Imap
            End If
            MostraMenuSicurezza()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiServerConfermato: " & ex.Message)
        End Try
    End Sub

    Private Function ConvertiInIntero(testo As String, predefinito As Integer) As Integer
        Try
            Dim valore As Integer = 0
            If Integer.TryParse(testo, valore) AndAlso valore > 0 Then Return valore
            Return predefinito
        Catch
            Return predefinito
        End Try
    End Function

    Private Sub MostraMenuSicurezza()
        Try
            Dim titolo As String = "Sicurezza ricezione"
            If _scopoSicurezza = ScopoSicurezza.Smtp Then titolo = "Sicurezza SMTP"
            If _scopoSicurezza = ScopoSicurezza.Pop3 Then titolo = "Sicurezza POP3"
            _menuSicurezza = New WGMenu()
            _menuSicurezza.aggiungi("Automatica", AddressOf SicurezzaAuto)
            _menuSicurezza.aggiungi("SSL", AddressOf SicurezzaSsl)
            _menuSicurezza.aggiungi("STARTTLS", AddressOf SicurezzaStartTls)
            _menuSicurezza.aggiungi("STARTTLS se disponibile", AddressOf SicurezzaStartTlsDisponibile)
            _menuSicurezza.aggiungi("Nessuna", AddressOf SicurezzaNessuna)
            _menuSicurezza.avvia(titolo, True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuSicurezza: " & ex.Message)
        End Try
    End Sub

    Private Sub SicurezzaAuto(sender As Object, e As EventArgs)
        ImpostaSicurezza(SecureSocketOptions.Auto)
    End Sub
    Private Sub SicurezzaSsl(sender As Object, e As EventArgs)
        ImpostaSicurezza(SecureSocketOptions.SslOnConnect)
    End Sub
    Private Sub SicurezzaStartTls(sender As Object, e As EventArgs)
        ImpostaSicurezza(SecureSocketOptions.StartTls)
    End Sub
    Private Sub SicurezzaStartTlsDisponibile(sender As Object, e As EventArgs)
        ImpostaSicurezza(SecureSocketOptions.StartTlsWhenAvailable)
    End Sub
    Private Sub SicurezzaNessuna(sender As Object, e As EventArgs)
        ImpostaSicurezza(SecureSocketOptions.None)
    End Sub

    Private Sub ImpostaSicurezza(valore As SecureSocketOptions)
        Try
            If _accountInModifica Is Nothing Then Return
            If _scopoSicurezza = ScopoSicurezza.Smtp Then
                _accountInModifica.SicurezzaSmtp = valore
                If _configurazioneManuale Then
                    _configurazioneManuale = False
                    ApriCampiSincronizzazione(False)
                    Return
                End If
                MostraAttesa("Prova connessione in corso, attendere")
                AvviaLavoro(New Action(AddressOf LavoroProvaConnessione))
            ElseIf _scopoSicurezza = ScopoSicurezza.Pop3 OrElse String.Equals(_accountInModifica.ProtocolloRicezione, "POP3", StringComparison.OrdinalIgnoreCase) Then
                _accountInModifica.SicurezzaPop3 = valore
                _scopoSicurezza = ScopoSicurezza.Smtp
                MostraMenuSicurezza()
            Else
                _accountInModifica.SicurezzaImap = valore
                _scopoSicurezza = ScopoSicurezza.Smtp
                MostraMenuSicurezza()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ImpostaSicurezza: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroProvaConnessione()
        Try
            Dim esito As EsitoProvaConnessione = _posta.ProvaConnessioneAccount(_accountInModifica)
            EseguiSuInterfaccia(Sub() CompletaProvaConnessione(esito))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroProvaConnessione: " & ex.Message)
            EseguiSuInterfaccia(Sub()
                                    ChiudiAttesa()
                                    MostraMessaggio("Prova connessione non riuscita.")
                                End Sub)
        End Try
    End Sub

    Private Sub CompletaProvaConnessione(esito As EsitoProvaConnessione)
        Try
            ChiudiAttesa()
            If esito Is Nothing OrElse Not esito.TuttoOk Then
                Dim d As String = ""
                If esito IsNot Nothing Then d = esito.MessaggioRicezione & " " & esito.MessaggioSmtp
                MostraMessaggio("Connessione non riuscita. " & d.Trim())
                Return
            End If
            Dim ok As Boolean
            If String.IsNullOrWhiteSpace(_accountInModifica.Chiave) Then
                Dim chiave As String = _posta.AggiungiAccount(_accountInModifica)
                ok = Not String.IsNullOrWhiteSpace(chiave)
                If ok Then
                    _accountInModifica.Chiave = chiave
                    _chiaveAccountCorrente = chiave
                End If
            Else
                ok = _posta.AggiornaAccount(_accountInModifica)
            End If
            CaricaElencoAccount()
            If ok Then
                MostraMessaggio("Account salvato.")
            Else
                MostraMessaggio("Salvataggio non riuscito.")
            End If
            _accountInModifica = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaProvaConnessione: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaCaricamentoMessaggi()
        Try
            _totaleMessaggi = 0
            _listaMessaggiGiaMostrata = False
            _caricamentoMessaggiInCorso = False
            _descrizioneFiltro = ""
            _titoloRisultati = ""
            _elencoDalServer = Nothing
            _totaleDalServer = 0
            _posizioneDaRipristinare = ""
            MostraAttesa("Caricamento messaggi in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCaricaMessaggi))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaCaricamentoMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Function ChiaviDaConsultare() As List(Of String)
        Dim chiavi As New List(Of String)
        Try
            Select Case _modalitaCaselle
                Case ModalitaCaselle.Singola
                    If Not String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then chiavi.Add(_chiaveAccountCorrente)
                Case ModalitaCaselle.Chiedi
                    If _chiaviAccountFiltro IsNot Nothing AndAlso _chiaviAccountFiltro.Count > 1 Then
                        chiavi.AddRange(_chiaviAccountFiltro)
                    ElseIf Not String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then
                        chiavi.Add(_chiaveAccountCorrente)
                    End If
                Case ModalitaCaselle.Multiple
                    chiavi.AddRange(_chiaviAccountFiltro)
                Case Else
                    For Each a As ImpostazioniAccountMail In _posta.OttieniElencoAccount(True)
                        chiavi.Add(a.Chiave)
                    Next
            End Select
            If chiavi.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then chiavi.Add(_chiaveAccountCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaviDaConsultare: " & ex.Message)
        End Try
        Return chiavi
    End Function

    Private Class LavoroPagina
        Public Property Chiave As String = ""
        Public Property Cartella As String = ""
        Public Property SoloNonLetti As Boolean = False
        Public Property Posta As CLSMail = Nothing
        Public Property Registro As wgcontrols.Logger = Nothing
        Public Property Risultato As PaginaMessaggi = Nothing

        Public Sub Esegui()
            Try
                If Posta Is Nothing Then Return
                Risultato = Posta.OttieniPaginaMessaggi(Chiave, Cartella, SoloNonLetti)
            Catch ex As Exception
                If Registro IsNot Nothing Then Registro.ScriviLog("LavoroPagina.Esegui (" & If(Chiave, "") & "): " & ex.Message)
                Risultato = Nothing
            End Try
        End Sub
    End Class

    Private Function LeggiMessaggi(ByRef totale As Integer) As List(Of RiepilogoMessaggio)
        Dim risultato As New List(Of RiepilogoMessaggio)
        totale = 0
        Try
            Dim chiavi As List(Of String) = ChiaviDaConsultare()
            If chiavi Is Nothing OrElse chiavi.Count = 0 Then Return risultato

            Dim lavori As New List(Of LavoroPagina)
            For Each chiave As String In chiavi
                Dim lavoro As New LavoroPagina()
                lavoro.Chiave = chiave
                lavoro.Cartella = PercorsoCartellaPerAccount(chiave)
                lavoro.SoloNonLetti = _soloNonLetti
                lavoro.Posta = _posta
                lavoro.Registro = log
                lavori.Add(lavoro)
            Next

            If lavori.Count = 1 Then
                lavori(0).Esegui()
            Else
                Dim compiti(lavori.Count - 1) As Task
                For indice As Integer = 0 To lavori.Count - 1
                    compiti(indice) = Task.Run(New Action(AddressOf lavori(indice).Esegui))
                Next
                Try
                    Task.WaitAll(compiti)
                Catch exAttesa As Exception
                    log.ScriviLog("MenuPosta.LeggiMessaggi attesa: " & exAttesa.Message)
                End Try
            End If

            For Each lavoro As LavoroPagina In lavori
                Try
                    Dim pagina As PaginaMessaggi = lavoro.Risultato
                    If pagina Is Nothing Then Continue For
                    If pagina.Messaggi IsNot Nothing Then risultato.AddRange(pagina.Messaggi)
                    If pagina.Totale > totale Then totale = pagina.Totale
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LeggiMessaggi (" & If(lavoro.Chiave, "") & "): " & exSingolo.Message)
                End Try
            Next

            OrdinaElenco(risultato)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LeggiMessaggi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function ConfrontaMessaggiPerData(a As RiepilogoMessaggio, b As RiepilogoMessaggio) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Return a.Data.CompareTo(b.Data)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfrontaMessaggiPerData: " & ex.Message)
            Return 0
        End Try
    End Function

    Private _regoleInCorso As Boolean = False
    Private _messaggiSpostatiDalleRegole As Integer = 0

    Private Sub ProgrammaApplicazioneRegole()
        Try
            If _regoleInCorso Then Return
            If Not String.Equals(_tipoCartellaSpeciale, "inbox", StringComparison.OrdinalIgnoreCase) Then Return
            If _listaMessaggiRisultatiRicerca Then Return

            _regoleInCorso = True
            AvviaLavoro(New Action(AddressOf LavoroApplicaRegole))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ProgrammaApplicazioneRegole: " & ex.Message)
            _regoleInCorso = False
        End Try
    End Sub

    Private Sub LavoroApplicaRegole()
        Try
            _messaggiSpostatiDalleRegole = 0
            For Each chiave As String In ChiaviDaConsultare()
                Try
                    _messaggiSpostatiDalleRegole += _posta.ApplicaRegoleSulServer(chiave)
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LavoroApplicaRegole (" & If(chiave, "") & "): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroApplicaRegole: " & ex.Message)
        Finally
            Try
                EseguiSuInterfaccia(AddressOf CompletaApplicaRegole)
            Catch exRientro As Exception
                log.ScriviLog("MenuPosta.LavoroApplicaRegole rientro: " & exRientro.Message)
                _regoleInCorso = False
            End Try
        End Try
    End Sub

    Private Sub CompletaApplicaRegole()
        Try
            _regoleInCorso = False
            If _messaggiSpostatiDalleRegole <= 0 Then Return
            _messaggiSpostatiDalleRegole = 0

            If Not _listaMessaggiGiaMostrata Then Return
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed Then Return
            If _caricamentoMessaggiInCorso Then Return

            MostraAttesa("Aggiornamento dell'elenco in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCaricaMessaggi))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaApplicaRegole: " & ex.Message)
            _regoleInCorso = False
        End Try
    End Sub

    Private Sub LavoroCaricaMessaggi()
        Try
            If String.IsNullOrWhiteSpace(_cartellaCorrente) AndAlso Not String.IsNullOrWhiteSpace(_tipoCartellaSpeciale) Then
                Try
                    _cartellaCorrente = PercorsoCartellaSpeciale(_tipoCartellaSpeciale)
                Catch exPercorso As Exception
                    log.ScriviLog("MenuPosta.LavoroCaricaMessaggi percorso: " & exPercorso.Message)
                    _cartellaCorrente = _tipoCartellaSpeciale
                End Try
            End If
            Dim totale As Integer = 0
            _elencoDalServer = LeggiMessaggi(totale)
            _totaleDalServer = totale
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCaricaMessaggi: " & ex.Message)
            _elencoDalServer = New List(Of RiepilogoMessaggio)
            _totaleDalServer = 0
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCaricamentoMessaggi)
        End Try
    End Sub

    Private Sub CompletaCaricamentoMessaggi()
        Try
            ChiudiAttesa()

            If _elencoDalServer IsNot Nothing Then
                _elencoMessaggi = _elencoDalServer
                _totaleMessaggi = _totaleDalServer
                _elencoDalServer = Nothing
            End If

            Dim listaViva As Boolean = _listaMessaggiGiaMostrata AndAlso _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed
            Dim senzaMessaggi As Boolean = _elencoMessaggi Is Nothing OrElse _elencoMessaggi.Count = 0

            If senzaMessaggi AndAlso Not listaViva AndAlso Not OperazioneAnnullata() Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaCaricamentoMessaggi errore: " & exErrore.Message)
                End Try
                If String.IsNullOrWhiteSpace(descrizione) Then
                    descrizione = If(_soloNonLetti, "Non c'è posta da leggere.", "Questa cartella non contiene messaggi.")
                End If
                MostraMessaggio(descrizione)
                Return
            End If

            If listaViva Then
                Dim posizionePrecedente As String = ""
                Try
                    Dim elementi As List(Of String) = _listaMessaggi.ottieniElementi()
                    If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                        Dim elemento As String = elementi(0)
                        posizionePrecedente = If(elemento, "")
                    End If
                Catch exPosizione As Exception
                    log.ScriviLog("MenuPosta.CompletaCaricamentoMessaggi posizione: " & exPosizione.Message)
                End Try
                _posizioneDaRipristinare = posizionePrecedente
                AggiornaElementiListaMessaggi()
            Else
                MostraListaMessaggi()
            End If

            ProgrammaApplicazioneRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCaricamentoMessaggi: " & ex.Message)
        Finally
            _caricamentoMessaggiInCorso = False
        End Try
    End Sub

    Private Function CostruisciElementiMessaggi(elenco As List(Of RiepilogoMessaggio)) As List(Of ElementoLista)
        Dim elementi As New List(Of ElementoLista)
        Try
            If elenco Is Nothing Then Return elementi
            For Each r As RiepilogoMessaggio In elenco
                elementi.Add(New ElementoLista(DescriviRiepilogo(r), r.ChiaveAccount & "|" & r.Id))
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CostruisciElementiMessaggi: " & ex.Message)
        End Try
        Return elementi
    End Function

    Private Function PaginaCorrenteHaSelezioni() As Boolean
        Try
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed Then Return False
            If _elencoMessaggi Is Nothing OrElse _elencoMessaggi.Count = 0 Then Return False

            Dim memorizzati As List(Of String) = _listaMessaggi.OttieniSelezioneMemorizzata()
            If memorizzati Is Nothing OrElse memorizzati.Count = 0 Then Return False

            Dim insieme As New HashSet(Of String)(memorizzati, StringComparer.Ordinal)
            For Each r As RiepilogoMessaggio In _elencoMessaggi
                If insieme.Contains(r.ChiaveAccount & "|" & r.Id) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PaginaCorrenteHaSelezioni: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiornaElementiListaMessaggi()
        Try
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed Then Return
            _listaMessaggi.pulisci()
            _listaMessaggi.impostaTitolo(TitoloCartellaCorrente())
            If _elencoMessaggi Is Nothing OrElse _elencoMessaggi.Count = 0 Then
                _listaMessaggi.aggiungiElemento(If(_soloNonLetti, "Nessun messaggio da leggere", "Nessun messaggio"))
            Else
                _listaMessaggi.AggiungiElementi(CostruisciElementiMessaggi(_elencoMessaggi))
            End If
            If Not String.IsNullOrWhiteSpace(_posizioneDaRipristinare) Then
                Dim idPrecedente As String = _posizioneDaRipristinare
                _posizioneDaRipristinare = ""
                Try
                    _listaMessaggi.posiziona(idPrecedente)
                Catch exRiposiziona As Exception
                    log.ScriviLog("MenuPosta.AggiornaElementiListaMessaggi riposizionamento: " & exRiposiziona.Message)
                End Try
                Dim elementoCorrente As String = ""
                Try
                    Dim elementi As List(Of String) = _listaMessaggi.ottieniElementi()
                    If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                        Dim elemento As String = elementi(0)
                        elementoCorrente = If(elemento, "")
                    End If
                Catch exElemento As Exception
                    log.ScriviLog("MenuPosta.AggiornaElementiListaMessaggi elemento corrente: " & exElemento.Message)
                End Try
                If String.IsNullOrWhiteSpace(elementoCorrente) AndAlso Not PaginaCorrenteHaSelezioni() Then
                    _listaMessaggi.selezionaElemento(0)
                End If
            ElseIf Not PaginaCorrenteHaSelezioni() Then
                _listaMessaggi.selezionaElemento(0)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiornaElementiListaMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbandonaListaMessaggiPrecedente()
        Try
            If _listaMessaggi Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaMessaggi
            _listaMessaggi = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf MessaggioSelezionato
                RemoveHandler precedente.ElementiSelezionati, AddressOf MessaggiListaSelezionati
                RemoveHandler precedente.escPremuto, AddressOf ListaMessaggiEsc
            Catch exRimozione As Exception
                log.ScriviLog("MenuPosta.AbbandonaListaMessaggiPrecedente (handler): " & exRimozione.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AbbandonaListaMessaggiPrecedente: " & ex.Message)
            _listaMessaggi = Nothing
        End Try
    End Sub

    Private Sub MostraListaMessaggi(Optional titolo As String = "")
        Try
            _listaMessaggiRisultatiRicerca = Not String.IsNullOrWhiteSpace(titolo)
            AbbandonaListaMessaggiPrecedente()
            _listaMessaggi = New WGMenuLista()
            _listaMessaggi.multiselezioneConsentita = True
            _listaMessaggi.ciclono = True
            _listaMessaggi.MantieniSelezioneTraPagine = True
            _listaMessaggi.AzzeraSelezioneMemorizzata()
            _listaMessaggi.impostaTitolo(If(String.IsNullOrWhiteSpace(titolo), TitoloCartellaCorrente(), titolo))
            If _elencoMessaggi Is Nothing OrElse _elencoMessaggi.Count = 0 Then
                _listaMessaggi.aggiungiElemento(If(_soloNonLetti, "Nessun messaggio da leggere", "Nessun messaggio"))
            Else
                _listaMessaggi.AggiungiElementi(CostruisciElementiMessaggi(_elencoMessaggi))
            End If
            _listaMessaggi.aggiungiOpzione("Apri messaggio", AddressOf OpzioneApriMessaggio)
            _listaMessaggi.aggiungiOpzione("Aggiorna messaggi", AddressOf OpzioneAggiornaMessaggi)
            _listaMessaggi.aggiungiOpzione("Segna tutti come letti", AddressOf OpzioneSegnaTuttiLetti)
            _listaMessaggi.aggiungiOpzione("Rispondi", AddressOf OpzioneRispondiDaLista)
            _listaMessaggi.aggiungiOpzione("Rispondi a tutti", AddressOf OpzioneRispondiTuttiDaLista)
            _listaMessaggi.aggiungiOpzione("Inoltra", AddressOf OpzioneInoltraDaLista)
            AggiungiOpzioniAiLista(_listaMessaggi)
            _listaMessaggi.aggiungiOpzione("Info messaggio", AddressOf OpzioneInfoMessaggioDaLista)
            _listaMessaggi.aggiungiOpzione("Ordina i messaggi", AddressOf OpzioneOrdina)
            _listaMessaggi.aggiungiOpzione("Filtra i messaggi", AddressOf OpzioneFiltra)
            _listaMessaggi.aggiungiOpzione("Cerca in questa cartella", AddressOf VoceCerca)
            If CartellaCorrenteELaIndesiderata() Then
                _listaMessaggi.aggiungiOpzione("Non è indesiderata", AddressOf OpzioneNonIndesiderata)
            Else
                _listaMessaggi.aggiungiOpzione("Segna come indesiderata", AddressOf OpzioneSegnaIndesiderata)
            End If
            _listaMessaggi.aggiungiOpzione("Sposta in una cartella", AddressOf OpzioneSpostaInCartella)
            _listaMessaggi.aggiungiOpzione("Elimina", AddressOf OpzioneElimina)
            _listaMessaggi.aggiungiOpzione("Elimina tutti i messaggi", AddressOf OpzioneEliminaTuttiIMessaggi)
            _listaMessaggi.aggiungiOpzione("Altre cartelle", AddressOf VoceAltreCartelle)
            If _posta.OttieniElencoAccount(True).Count > 1 Then
                _listaMessaggi.aggiungiOpzione("Controlla messaggi su altro account", AddressOf OpzioneAltroAccount)
            End If
            AddHandler _listaMessaggi.ElementoSelezionato, AddressOf MessaggioSelezionato
            AddHandler _listaMessaggi.ElementiSelezionati, AddressOf MessaggiListaSelezionati
            AddHandler _listaMessaggi.escPremuto, AddressOf ListaMessaggiEsc
            _listaMessaggiGiaMostrata = True
            _listaMessaggi.avvia()

            If _listaMessaggiRisultatiRicerca Then
                FermaTimerNuoviMessaggi()
            Else
                AvviaTimerNuoviMessaggi()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraListaMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaMessaggiEsc(sender As Object, e As EventArgs)
        Try
            FermaTimerNuoviMessaggi()
            If _listaMessaggi IsNot Nothing Then _listaMessaggi.abbandona()
            _listaMessaggi = Nothing
        Catch
            _listaMessaggi = Nothing
        End Try

        Try
            If _soloElencoDaLeggere Then Chiudi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaMessaggiEsc chiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAggiornaMessaggi(sender As Object, e As EventArgs)
        Try
            If _listaMessaggiRisultatiRicerca Then
                MostraMessaggio("Per aggiornare i risultati, rilancia la ricerca.")
                Return
            End If
            If _caricamentoMessaggiInCorso OrElse _controlloNuoviMessaggiInCorso Then Return
            _controlloNuoviMessaggiInCorso = True
            MostraAttesa("Controllo nuovi messaggi in corso, attendere")
            AvviaLavoro(Sub() EseguiControlloNuoviMessaggi(False))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAggiornaMessaggi: " & ex.Message)
            _controlloNuoviMessaggiInCorso = False
        End Try
    End Sub

    Private Sub AvviaTimerNuoviMessaggi()
        Try
            If _chiuso Then Return
            If _timerNuoviMessaggi IsNot Nothing Then Return
            _timerNuoviMessaggi = New System.Threading.Timer(AddressOf TimerNuoviMessaggiTick, Nothing, IntervalloControlloNuoviMessaggiMs, IntervalloControlloNuoviMessaggiMs)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaTimerNuoviMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaTimerNuoviMessaggi()
        Try
            If _timerNuoviMessaggi IsNot Nothing Then
                _timerNuoviMessaggi.Dispose()
                _timerNuoviMessaggi = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.FermaTimerNuoviMessaggi: " & ex.Message)
            _timerNuoviMessaggi = Nothing
        End Try
    End Sub

    Private Sub TimerNuoviMessaggiTick(stato As Object)
        Try
            If _chiuso Then Return
            If _controlloNuoviMessaggiInCorso Then Return
            If _caricamentoMessaggiInCorso Then Return
            If _listaMessaggiRisultatiRicerca Then Return
            If _campoMessaggio IsNot Nothing Then Return
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed OrElse Not _listaMessaggiGiaMostrata Then Return
            _controlloNuoviMessaggiInCorso = True
            EseguiControlloNuoviMessaggi(True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TimerNuoviMessaggiTick: " & ex.Message)
            _controlloNuoviMessaggiInCorso = False
        End Try
    End Sub

    Private Sub EseguiControlloNuoviMessaggi(silenzioso As Boolean)
        Dim nuovi As New List(Of RiepilogoMessaggio)
        Dim totale As Integer = 0
        Dim erroreVerificatosi As Boolean = False
        Try
            Dim aggiornati As List(Of RiepilogoMessaggio) = LeggiMessaggi(totale)
            Dim esistenti As New HashSet(Of String)(StringComparer.Ordinal)
            If _elencoMessaggi IsNot Nothing Then
                For Each r As RiepilogoMessaggio In _elencoMessaggi
                    esistenti.Add(r.ChiaveAccount & "|" & r.Id)
                Next
            End If
            If aggiornati IsNot Nothing Then
                For Each r As RiepilogoMessaggio In aggiornati
                    If Not esistenti.Contains(r.ChiaveAccount & "|" & r.Id) Then nuovi.Add(r)
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.EseguiControlloNuoviMessaggi: " & ex.Message)
            erroreVerificatosi = True
        Finally
            EseguiSuInterfaccia(Sub() CompletaControlloNuoviMessaggi(nuovi, totale, silenzioso, erroreVerificatosi))
        End Try
    End Sub

    Private Sub CompletaControlloNuoviMessaggi(nuovi As List(Of RiepilogoMessaggio), totale As Integer, silenzioso As Boolean, erroreVerificatosi As Boolean)
        Try
            If Not silenzioso Then ChiudiAttesa()
            If _chiuso Then Return

            If erroreVerificatosi Then
                If Not silenzioso Then MostraMessaggio("Non è stato possibile controllare i nuovi messaggi.")
                Return
            End If

            If nuovi Is Nothing OrElse nuovi.Count = 0 Then
                If Not silenzioso Then MostraMessaggio("Nessun nuovo messaggio.")
                Return
            End If

            Dim primoIdPrecedente As String = ""
            If _elencoMessaggi IsNot Nothing AndAlso _elencoMessaggi.Count > 0 Then
                primoIdPrecedente = _elencoMessaggi(0).ChiaveAccount & "|" & _elencoMessaggi(0).Id
            End If

            Dim listaAggiornata As New List(Of RiepilogoMessaggio)(nuovi)
            If _elencoMessaggi IsNot Nothing Then listaAggiornata.AddRange(_elencoMessaggi)
            OrdinaElenco(listaAggiornata)
            _elencoMessaggi = listaAggiornata
            _totaleMessaggi = If(totale > _totaleMessaggi, totale, _totaleMessaggi + nuovi.Count)

            If _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed AndAlso _listaMessaggiGiaMostrata Then
                If _ordinamento <> OrdinamentoMessaggi.DataRecente OrElse String.IsNullOrWhiteSpace(primoIdPrecedente) OrElse Not _listaMessaggi.VoceEsiste(primoIdPrecedente) Then
                    AggiornaElementiListaMessaggi()
                Else
                    For Each r As RiepilogoMessaggio In nuovi
                        _listaMessaggi.AggiungiElementoPrima(primoIdPrecedente, DescriviRiepilogo(r), r.ChiaveAccount & "|" & r.Id)
                    Next
                End If
            End If

            If Not silenzioso Then
                MostraMessaggio(If(nuovi.Count = 1, "1 nuovo messaggio.", nuovi.Count.ToString() & " nuovi messaggi."))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaControlloNuoviMessaggi: " & ex.Message)
        Finally
            _controlloNuoviMessaggiInCorso = False
        End Try
    End Sub

    Private Function DescriviRiepilogo(r As RiepilogoMessaggio) As String
        Try
            If r Is Nothing Then Return "Messaggio"
            Dim o As String = If(String.IsNullOrWhiteSpace(r.Oggetto), "Senza oggetto", r.Oggetto)
            Dim m As String = If(r.Mittente, "")
            Dim stato As String = If(r.Letto, "Letto", "Non letto")
            Dim data As String = ""
            If r.Data <> DateTimeOffset.MinValue Then data = r.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm")

            Dim parti As New List(Of String) From {stato, o}
            If Not String.IsNullOrWhiteSpace(m) Then parti.Add("Da: " & m)
            If Not String.IsNullOrWhiteSpace(data) Then parti.Add("Data: " & data)
            Return String.Join(". ", parti)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviRiepilogo: " & ex.Message)
            Return "Messaggio"
        End Try
    End Function

    Private Sub MessaggiListaSelezionati(elementi As List(Of String))
        Try
            MostraMessaggio("Con la multiselezione attiva non è possibile aprire in lettura o rispondere a nessun messaggio. Premere il tasto shift per levare la modalità di selezione multipla per poter aprire i messaggi.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MessaggiListaSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub MessaggioSelezionato(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) OrElse Not id.Contains("|") Then Return
            Dim parti() As String = id.Split("|"c)
            _chiaveAccountCorrente = parti(0)
            Dim idMsg As String = parti(1)
            _mostraHtmlOriginale = False
            AzzeraForzaturaLettura()
            MostraAttesa("Apertura messaggio in corso, attendere")
            AvviaLavoro(Sub()
                            Try
                                _messaggioCorrente = _posta.ApriMessaggio(_chiaveAccountCorrente, PercorsoCartellaPerAccount(_chiaveAccountCorrente), idMsg)
                            Catch
                                _messaggioCorrente = Nothing
                            Finally
                                EseguiSuInterfaccia(AddressOf CompletaAperturaMessaggio)
                            End Try
                        End Sub)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MessaggioSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaAperturaMessaggio()
        Try
            ChiudiAttesa()
            If _messaggioCorrente Is Nothing Then
                Dim d As String = ""
                Try
                    d = _posta.UltimoErrore
                Catch
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(d), "Messaggio non apribile.", d))
                Return
            End If
            AggiornaStatoLocaleMessaggioLetto()
            MostraCorpoMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaAperturaMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaStatoLocaleMessaggioLetto()
        Try
            If _messaggioCorrente Is Nothing Then Return
            If Not _messaggioCorrente.Letto Then Return
            If _elencoMessaggi Is Nothing Then Return

            Dim corrispondente As RiepilogoMessaggio = Nothing
            For Each r As RiepilogoMessaggio In _elencoMessaggi
                If String.Equals(r.ChiaveAccount, _messaggioCorrente.ChiaveAccount, StringComparison.Ordinal) AndAlso
                   String.Equals(r.Id, _messaggioCorrente.Id, StringComparison.Ordinal) Then
                    corrispondente = r
                    Exit For
                End If
            Next
            If corrispondente Is Nothing OrElse corrispondente.Letto Then Return

            corrispondente.Letto = True

            Try
                _posta.SegnaLettoNellaCacheElenco(corrispondente.ChiaveAccount, PercorsoCartellaPerAccount(corrispondente.ChiaveAccount), corrispondente.Id, True)
            Catch exCache As Exception
                log.ScriviLog("MenuPosta.AggiornaStatoLocaleMessaggioLetto copia locale: " & exCache.Message)
            End Try

            If _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed Then
                Dim id As String = corrispondente.ChiaveAccount & "|" & corrispondente.Id
                _listaMessaggi.AggiornaTestoElemento(id, DescriviRiepilogo(corrispondente))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiornaStatoLocaleMessaggioLetto: " & ex.Message)
        End Try
    End Sub

    Private Function IntestazioneMessaggioCorrente() As String
        Try
            If _messaggioCorrente Is Nothing Then Return ""
            Dim righe As New List(Of String)

            Dim daNome As String = If(_messaggioCorrente.Mittente, "")
            Dim daEmail As String = If(_messaggioCorrente.MittenteEmail, "")
            If Not String.IsNullOrWhiteSpace(daEmail) AndAlso Not String.Equals(daNome, daEmail, StringComparison.OrdinalIgnoreCase) Then
                righe.Add("Da: " & daNome & " <" & daEmail & ">")
            Else
                righe.Add("Da: " & If(String.IsNullOrWhiteSpace(daNome), daEmail, daNome))
            End If

            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Destinatari) Then
                righe.Add("A: " & _messaggioCorrente.Destinatari)
            End If
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Cc) Then
                righe.Add("Cc: " & _messaggioCorrente.Cc)
            End If
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.RispondiA) AndAlso
               Not String.Equals(_messaggioCorrente.RispondiA, _messaggioCorrente.MittenteEmail, StringComparison.OrdinalIgnoreCase) Then
                righe.Add("Le risposte vanno a: " & _messaggioCorrente.RispondiA)
            End If
            If _messaggioCorrente.ArrivaDaUnaLista Then
                Dim descrizioneLista As String = If(_messaggioCorrente.NomeLista, "")
                If String.IsNullOrWhiteSpace(descrizioneLista) Then descrizioneLista = If(_messaggioCorrente.IndirizzoLista, "")
                If Not String.IsNullOrWhiteSpace(_messaggioCorrente.IndirizzoLista) AndAlso
                   Not String.Equals(descrizioneLista, _messaggioCorrente.IndirizzoLista, StringComparison.OrdinalIgnoreCase) Then
                    descrizioneLista &= " <" & _messaggioCorrente.IndirizzoLista & ">"
                End If
                righe.Add("Lista di discussione: " & descrizioneLista)
            End If
            righe.Add("Oggetto: " & If(String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), "Senza oggetto", _messaggioCorrente.Oggetto))
            If _messaggioCorrente.Data <> DateTimeOffset.MinValue Then
                righe.Add("Data: " & _messaggioCorrente.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm"))
            End If
            If _messaggioCorrente.Allegati IsNot Nothing AndAlso _messaggioCorrente.Allegati.Count > 0 Then
                righe.Add("Allegati: " & _messaggioCorrente.Allegati.Count.ToString())
            End If

            Return String.Join(Environment.NewLine, righe) & Environment.NewLine & Environment.NewLine
        Catch ex As Exception
            log.ScriviLog("MenuPosta.IntestazioneMessaggioCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function CartellaCorrenteELaPostaInviata() As Boolean
        Try
            If String.Equals(If(_tipoCartellaSpeciale, "").Trim(), "sent", StringComparison.OrdinalIgnoreCase) Then Return True
            Dim percorso As String = If(_cartellaCorrente, "").ToLowerInvariant()
            Return percorso.Contains("sent") OrElse percorso.Contains("inviata")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaCorrenteELaPostaInviata: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub MostraMessaggioInviatoACampi()
        Try
            If _messaggioCorrente Is Nothing Then Return
            ChiudiCampiMessaggioInviato()

            Dim data As String = ""
            If _messaggioCorrente.Data <> DateTimeOffset.MinValue Then data = _messaggioCorrente.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm")
            Dim numeroAllegati As Integer = If(_messaggioCorrente.Allegati Is Nothing, 0, _messaggioCorrente.Allegati.Count)

            _campiMessaggioInviato = New Campi()
            _campiMessaggioInviato.SalvaSuEscape = False
            _campiMessaggioInviato.Aggiungi("A", "inviata.a", False, False, If(_messaggioCorrente.Destinatari, ""))
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Cc) Then _campiMessaggioInviato.Aggiungi("Cc", "inviata.cc", False, False, _messaggioCorrente.Cc)
            _campiMessaggioInviato.Aggiungi("Oggetto", "inviata.oggetto", False, False, If(String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), "Senza oggetto", _messaggioCorrente.Oggetto))
            _campiMessaggioInviato.Aggiungi("Data", "inviata.data", False, False, data)
            If numeroAllegati > 0 Then _campiMessaggioInviato.Aggiungi("Allegati", "inviata.allegati", False, False, numeroAllegati.ToString())
            _campiMessaggioInviato.AggiungiMultiriga("Messaggio", "inviata.corpo", False, False, TestoDelMessaggioCorrente())
            _campiMessaggioInviato.AggiungiOpzione("Stampa", AddressOf OpzioneStampaMessaggio)
            _campiMessaggioInviato.AggiungiOpzione("Inoltra", AddressOf OpzioneInoltraMessaggioInviato)
            _campiMessaggioInviato.AggiungiOpzione("Info messaggio", AddressOf OpzioneInfoMessaggio)
            If numeroAllegati > 0 Then _campiMessaggioInviato.AggiungiOpzione("Allegati", AddressOf OpzioneAllegatiMessaggio)
            _campiMessaggioInviato.AggiungiOpzione("Elimina messaggio", AddressOf OpzioneEliminaMessaggioInviato)
            AddHandler _campiMessaggioInviato.Fine, AddressOf CampiMessaggioInviatoFine
            AddHandler _campiMessaggioInviato.Chiusura, AddressOf CampiMessaggioInviatoChiusura
            _campiMessaggioInviato.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMessaggioInviatoACampi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiCampiMessaggioInviato()
        Try
            If _campiMessaggioInviato Is Nothing Then Return
            Dim campi As Campi = _campiMessaggioInviato
            _campiMessaggioInviato = Nothing
            RemoveHandler campi.Fine, AddressOf CampiMessaggioInviatoFine
            RemoveHandler campi.Chiusura, AddressOf CampiMessaggioInviatoChiusura
            campi.Chiudi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiCampiMessaggioInviato: " & ex.Message)
            _campiMessaggioInviato = Nothing
        End Try
    End Sub

    Private Sub CampiMessaggioInviatoFine(sender As Object, e As EventArgs)
        Try
            ChiudiCampiMessaggioInviato()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiMessaggioInviatoFine: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiMessaggioInviatoChiusura(sender As Object, e As EventArgs)
        Try
            If _campiMessaggioInviato Is Nothing Then Return
            RemoveHandler _campiMessaggioInviato.Fine, AddressOf CampiMessaggioInviatoFine
            RemoveHandler _campiMessaggioInviato.Chiusura, AddressOf CampiMessaggioInviatoChiusura
            _campiMessaggioInviato = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiMessaggioInviatoChiusura: " & ex.Message)
            _campiMessaggioInviato = Nothing
        End Try
    End Sub

    Private Sub OpzioneInoltraMessaggioInviato(sender As Object, e As EventArgs)
        Try
            ChiudiCampiMessaggioInviato()
            OpzioneInoltra(sender, e)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInoltraMessaggioInviato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaMessaggioInviato(sender As Object, e As EventArgs)
        Try
            ChiudiCampiMessaggioInviato()
            OpzioneEliminaMessaggioAperto(sender, e)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaMessaggioInviato: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCorpoMessaggio()
        Try
            If _messaggioCorrente Is Nothing Then Return
            If CartellaCorrenteELaPostaInviata() Then
                MostraMessaggioInviatoACampi()
                Return
            End If
            If LetturaMessaggiNelBrowser() AndAlso Not LetturaTestualeForzata() AndAlso MessaggioHaHtml() AndAlso WGLettoreHtml.Disponibile Then
                AvviaLetturaBrowser()
                Return
            End If
            If LetturaMessaggiConWord() AndAlso Not LetturaTestualeForzata() Then
                AvviaLetturaDocumento()
                Return
            End If
            Dim titolo As String = If(String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), "Messaggio", _messaggioCorrente.Oggetto)
            Dim corpo As String = IntestazioneMessaggioCorrente() & TestoDelMessaggioCorrente()
            _campoMessaggio = New WGCampo()
            _campoMessaggio.init(titolo, If(corpo, ""), True, True, False, False)
            _campoMessaggio.aggiungiopzione("Stampa", AddressOf OpzioneStampaMessaggio)
            _campoMessaggio.aggiungiopzione("Rispondi", AddressOf OpzioneRispondi)
            _campoMessaggio.aggiungiopzione("Rispondi a tutti", AddressOf OpzioneRispondiTutti)
            _campoMessaggio.aggiungiopzione("Inoltra", AddressOf OpzioneInoltra)
            AggiungiOpzioniAiCampo(_campoMessaggio)
            _campoMessaggio.aggiungiopzione("Info messaggio", AddressOf OpzioneInfoMessaggio)
            If _messaggioCorrente.Allegati IsNot Nothing AndAlso _messaggioCorrente.Allegati.Count > 0 Then
                _campoMessaggio.aggiungiopzione("Allegati", AddressOf OpzioneAllegatiMessaggio)
            End If
            If MessaggioHaHtml() Then
                _campoMessaggio.aggiungiopzione(If(_mostraHtmlOriginale, "Mostra il testo leggibile", "Mostra il codice HTML originale"), AddressOf OpzioneAlternaHtml)
                _campoMessaggio.aggiungiopzione("Apri in modalità documento", AddressOf OpzioneApriInModalitaDocumento)
            End If
            If CartellaCorrenteSonoLeBozze() Then
                _campoMessaggio.aggiungiopzione("Riprendi la scrittura", AddressOf OpzioneRiprendiBozza)
            End If
            _campoMessaggio.aggiungiopzione("Elimina messaggio", AddressOf OpzioneEliminaMessaggioAperto)
            AddHandler _campoMessaggio.chiusura, AddressOf CampoMessaggioChiusura
            _campoMessaggio.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraCorpoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneStampaMessaggio(sender As Object, e As EventArgs)
        Try
            log.ScriviLog("MenuPosta.OpzioneStampaMessaggio")
            Dim nomeStampante As String = New WGImpostazioniComuni().NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                MostraMessaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                Return
            End If
            StampantePostaSelezionata(nomeStampante)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneStampaMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaStampantiPostaEsc()
        Try
            If _listaStampantiPosta IsNot Nothing Then _listaStampantiPosta.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaStampantiPostaEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub StampantePostaSelezionata(nomeStampante As String)
        Try
            log.ScriviLog("MenuPosta.StampantePostaSelezionata: " & nomeStampante)
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            Dim ok As Boolean = False
            Dim titoloDoc As String = "Messaggio"
            If _messaggioCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto) Then
                titoloDoc = _messaggioCorrente.Oggetto
            End If
            Dim editorWord As WGWord = Nothing
            If _campiLettura IsNot Nothing Then
                editorWord = _campiLettura.RecuperaControlloWord("msg.lettura")
            End If
            If editorWord IsNot Nothing AndAlso Not editorWord.IsDisposed Then
                ok = editorWord.Stampa(nomeStampante, titoloDoc)
            Else
                Dim testo As String = ""
                If _campoMessaggio IsNot Nothing Then
                    testo = _campoMessaggio.ottienitesto()
                End If
                If String.IsNullOrWhiteSpace(testo) Then
                    testo = IntestazioneMessaggioCorrente() & TestoDelMessaggioCorrente()
                End If
                Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
                ok = Global.WGStampante.WGStampante.StampaTesto(testo, nomeStampante, titoloDoc, impostazioniStampa)
            End If
            If _listaStampantiPosta IsNot Nothing Then _listaStampantiPosta.abbandona()
            If ok Then
                MostraMessaggio("Stampa avviata.")
            Else
                Dim errore As String = Global.WGStampante.WGStampante.UltimoErrore()
                If String.IsNullOrWhiteSpace(errore) Then errore = "La stampa non è riuscita."
                MostraMessaggio(errore)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.StampantePostaSelezionata: " & ex.Message)
            Try
                If _listaStampantiPosta IsNot Nothing Then _listaStampantiPosta.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Function MessaggioHaHtml() As Boolean
        Try
            If _messaggioCorrente Is Nothing Then Return False
            Return Not String.IsNullOrWhiteSpace(_messaggioCorrente.CorpoHtml)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MessaggioHaHtml: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TestoDelMessaggioCorrente() As String
        Try
            If _messaggioCorrente Is Nothing Then Return ""
            If _mostraHtmlOriginale AndAlso Not String.IsNullOrWhiteSpace(_messaggioCorrente.CorpoHtml) Then
                Return _messaggioCorrente.CorpoHtml
            End If
            Dim testo As String = CLSMail.CorpoLeggibile(_messaggioCorrente.CorpoTesto, _messaggioCorrente.CorpoHtml)
            If String.IsNullOrWhiteSpace(testo) AndAlso Not String.IsNullOrWhiteSpace(_messaggioCorrente.CorpoHtml) Then
                testo = CLSMail.HtmlVersoTesto(_messaggioCorrente.CorpoHtml)
            End If
            If String.IsNullOrWhiteSpace(testo) Then Return "Messaggio senza testo."
            testo = AvanzataMP().TestoConLink(testo, _messaggioCorrente.CorpoTesto, _messaggioCorrente.CorpoHtml)
            Return testo
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TestoDelMessaggioCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub OpzioneAlternaHtml(sender As Object, e As EventArgs)
        Try
            _mostraHtmlOriginale = Not _mostraHtmlOriginale
            ChiudiCampoMessaggio()
            MostraCorpoMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAlternaHtml: " & ex.Message)
        End Try
    End Sub

    Private Function MessaggiSelezionati() As List(Of String)
        Dim validi As New List(Of String)
        Try
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed Then Return validi
            Dim elementi As List(Of String) = _listaMessaggi.ottieniElementi()
            If elementi IsNot Nothing Then
                For Each voce As String In elementi
                    If String.IsNullOrWhiteSpace(voce) Then Continue For
                    If Not voce.Contains("|") Then Continue For
                    validi.Add(voce)
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MessaggiSelezionati: " & ex.Message)
        End Try
        Return validi
    End Function

    Private Function UnSoloMessaggioSelezionato(ByRef voce As String, Optional ultimoSePiuDiUno As Boolean = False) As Boolean
        voce = ""
        Try
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed Then
                MostraMessaggio("Seleziona prima un messaggio.")
                Return False
            End If
            Dim elementi As List(Of String) = _listaMessaggi.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                MostraMessaggio("Seleziona prima un messaggio.")
                Return False
            End If
            Dim elemento As String = If(ultimoSePiuDiUno, elementi(elementi.Count - 1), elementi(0))
            If String.IsNullOrWhiteSpace(elemento) OrElse Not elemento.Contains("|") Then
                MostraMessaggio("Seleziona prima un messaggio.")
                Return False
            End If
            voce = elemento
            Return True
        Catch ex As Exception
            log.ScriviLog("MenuPosta.UnSoloMessaggioSelezionato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CartellaCorrenteELaIndesiderata() As Boolean
        Try
            If String.Equals(_tipoCartellaSpeciale, "junk", StringComparison.OrdinalIgnoreCase) Then Return True
            Dim p As String = If(_cartellaCorrente, "").ToLowerInvariant()
            If p.Contains("junk") OrElse p.Contains("spam") OrElse p.Contains("indesiderat") Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaCorrenteELaIndesiderata: " & ex.Message)
            Return False
        End Try
    End Function

    Private _tipoSpostamentoSpeciale As String = ""
    Private _cartellaSpostamento As String = ""
    Private _idDaSpostare As New List(Of String)
    Private _spostati As Integer = 0

    Private Function PreparaSelezionePerSpostamento(ByRef chiave As String) As Boolean
        chiave = ""
        Try
            Dim selezionati As List(Of String) = MessaggiSelezionati()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then
                MostraMessaggio("Seleziona prima i messaggi.")
                Return False
            End If

            _idDaSpostare = New List(Of String)
            For Each voce As String In selezionati
                Dim parti() As String = voce.Split("|"c)
                If parti.Length < 2 Then Continue For
                If String.IsNullOrWhiteSpace(chiave) Then chiave = parti(0)
                If Not String.Equals(chiave, parti(0), StringComparison.Ordinal) Then Continue For
                _idDaSpostare.Add(parti(1))
            Next

            If _idDaSpostare.Count = 0 Then
                MostraMessaggio("Seleziona prima i messaggi.")
                Return False
            End If

            If _idDaSpostare.Count < selezionati.Count Then
                MostraMessaggio("Si possono spostare insieme solo i messaggi della stessa casella. Ne verranno spostati " & _idDaSpostare.Count.ToString() & ".")
            End If

            Return True
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaSelezionePerSpostamento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub OpzioneSegnaIndesiderata(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ""
            If Not PreparaSelezionePerSpostamento(chiave) Then Return
            _chiaveAccountCorrente = chiave
            _tipoSpostamentoSpeciale = "junk"
            _cartellaSpostamento = ""
            MostraAttesa("Spostamento nella posta indesiderata in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSposta))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSegnaIndesiderata: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneNonIndesiderata(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ""
            If Not PreparaSelezionePerSpostamento(chiave) Then Return
            _chiaveAccountCorrente = chiave
            _tipoSpostamentoSpeciale = "inbox"
            _cartellaSpostamento = ""
            MostraAttesa("Spostamento nella posta in arrivo in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSposta))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneNonIndesiderata: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSpostaInCartella(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ""
            If Not PreparaSelezionePerSpostamento(chiave) Then Return
            _chiaveAccountCorrente = chiave
            _tipoSpostamentoSpeciale = ""
            _scopoElencoCartelle = "sposta"
            AvviaLetturaCartelle()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSpostaInCartella: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSposta()
        Try
            If Not String.IsNullOrWhiteSpace(_tipoSpostamentoSpeciale) Then
                _spostati = _posta.SpostaMessaggiInCartellaSpeciale(_chiaveAccountCorrente, _cartellaCorrente, _idDaSpostare, _tipoSpostamentoSpeciale)
            Else
                _spostati = _posta.SpostaMessaggiInCartella(_chiaveAccountCorrente, _cartellaCorrente, _idDaSpostare, _cartellaSpostamento)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroSposta: " & ex.Message)
            _spostati = 0
        Finally
            EseguiSuInterfaccia(AddressOf CompletaSposta)
        End Try
    End Sub

    Private Sub CompletaSposta()
        Try
            ChiudiAttesa()
            Dim quanti As Integer = _spostati
            _idDaSpostare = New List(Of String)
            _tipoSpostamentoSpeciale = ""
            _cartellaSpostamento = ""

            If quanti <= 0 Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaSposta errore: " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(descrizione), "Nessun messaggio spostato.", descrizione))
                Return
            End If

            If _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed Then
                Try
                    _listaMessaggi.AzzeraSelezioneMemorizzata()
                Catch exSelezione As Exception
                    log.ScriviLog("MenuPosta.CompletaSposta azzeramento selezione: " & exSelezione.Message)
                End Try
            End If

            MostraMessaggio(If(quanti = 1, "Messaggio spostato.", quanti.ToString() & " messaggi spostati."))
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaSposta: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneElimina(sender As Object, e As EventArgs)
        Try
            Dim selezionati As List(Of String) = MessaggiSelezionati()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then
                MostraMessaggio("Seleziona prima i messaggi da eliminare.")
                Return
            End If

            Dim chiave As String = ""
            Dim identificativi As New List(Of String)
            For Each voce As String In selezionati
                Dim parti() As String = voce.Split("|"c)
                If parti.Length < 2 Then Continue For
                If String.IsNullOrWhiteSpace(chiave) Then chiave = parti(0)
                If Not String.Equals(chiave, parti(0), StringComparison.Ordinal) Then Continue For
                identificativi.Add(parti(1))
            Next

            If identificativi.Count = 0 Then
                MostraMessaggio("Seleziona prima i messaggi da eliminare.")
                Return
            End If

            If identificativi.Count < selezionati.Count Then
                MostraMessaggio("Si possono eliminare insieme solo i messaggi della stessa casella. Ne verranno eliminati " & identificativi.Count.ToString() & ".")
            End If

            PreparaEliminazione(chiave, identificativi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneElimina: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaMessaggioAperto(sender As Object, e As EventArgs)
        Try
            If _messaggioCorrente Is Nothing Then Return
            ChiudiCampoMessaggio()
            PreparaEliminazione(_messaggioCorrente.ChiaveAccount, New List(Of String) From {_messaggioCorrente.Id})
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaMessaggioAperto: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaTuttiIMessaggi(sender As Object, e As EventArgs)
        Try
            If String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then
                Dim chiavi As List(Of String) = ChiaviDaConsultare()
                If chiavi Is Nothing OrElse chiavi.Count = 0 Then
                    MostraMessaggio("Nessuna casella da cui eliminare.")
                    Return
                End If
                _chiaveAccountCorrente = chiavi(0)
            End If
            _cartellaDaEliminare = _cartellaCorrente
            _eliminazioneDefinitiva = CartellaCorrenteEIlCestino()

            Dim testo As String
            If _eliminazioneDefinitiva Then
                testo = "Vuoi eliminare definitivamente tutti i messaggi di " & TitoloCartellaCorrente() & "? L'operazione non è reversibile."
            Else
                testo = "Vuoi spostare nel cestino tutti i messaggi di " & TitoloCartellaCorrente() & "?"
            End If

            ChiudiDomandaEliminaTutti()
            _domandaEliminaTutti = New WGDomanda()
            AddHandler _domandaEliminaTutti.rispostadomanda, AddressOf RispostaEliminaTutti
            _domandaEliminaTutti.avvia(testo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaTuttiIMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaEliminaTutti(risposta As DialogResult)
        Try
            ChiudiDomandaEliminaTutti()
            If risposta <> DialogResult.Yes Then Return
            MostraAttesa("Eliminazione in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroEliminaTutti))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RispostaEliminaTutti: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroEliminaTutti()
        Try
            _eliminati = 0
            For Each chiave As String In ChiaviDaConsultare()
                Try
                    _eliminati += _posta.EliminaTuttiIMessaggi(chiave, _cartellaDaEliminare, _eliminazioneDefinitiva)
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LavoroEliminaTutti (" & If(chiave, "") & "): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroEliminaTutti: " & ex.Message)
            _eliminati = 0
        Finally
            EseguiSuInterfaccia(AddressOf CompletaEliminaTutti)
        End Try
    End Sub

    Private Sub CompletaEliminaTutti()
        Try
            ChiudiAttesa()
            If _eliminati <= 0 Then
                Dim d As String = ""
                Try
                    d = _posta.UltimoErrore
                Catch
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(d), "Eliminazione non riuscita.", d))
                Return
            End If
            Dim quanti As Integer = _eliminati
            _messaggioCorrente = Nothing
            _idDaEliminare = New List(Of String)
            MostraMessaggio(If(_eliminazioneDefinitiva, quanti.ToString() & " messaggi eliminati.", quanti.ToString() & " messaggi spostati nel cestino."))
            AvviaCaricamentoMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaEliminaTutti: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaEliminazione(chiaveAccount As String, identificativi As List(Of String))
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) OrElse identificativi Is Nothing OrElse identificativi.Count = 0 Then Return
            _chiaveAccountCorrente = chiaveAccount
            _idDaEliminare = New List(Of String)(identificativi)
            _cartellaDaEliminare = _cartellaCorrente
            _eliminazioneDefinitiva = CartellaCorrenteEIlCestino()

            Dim testo As String
            If _idDaEliminare.Count = 1 Then
                testo = If(_eliminazioneDefinitiva, "Vuoi eliminare definitivamente questo messaggio? L'operazione non è reversibile.", "Vuoi spostare questo messaggio nel cestino?")
            Else
                testo = If(_eliminazioneDefinitiva,
                           "Vuoi eliminare definitivamente " & _idDaEliminare.Count.ToString() & " messaggi? L'operazione non è reversibile.",
                           "Vuoi spostare nel cestino " & _idDaEliminare.Count.ToString() & " messaggi?")
            End If

            ChiudiDomandaElimina()
            _domandaElimina = New WGDomanda()
            AddHandler _domandaElimina.rispostadomanda, AddressOf RispostaEliminaMessaggi
            _domandaElimina.avvia(testo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaEliminazione: " & ex.Message)
        End Try
    End Sub

    Private Function CartellaCorrenteEIlCestino() As Boolean
        Try
            Dim p As String = If(_cartellaCorrente, "").ToLowerInvariant()
            If p.Contains("trash") OrElse p.Contains("cestino") OrElse p.Contains("eliminat") OrElse p.Contains("deleted") Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaCorrenteEIlCestino: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RispostaEliminaMessaggi(risposta As DialogResult)
        Try
            ChiudiDomandaElimina()
            If risposta <> DialogResult.Yes Then
                _idDaEliminare = New List(Of String)
                Return
            End If
            MostraAttesa("Eliminazione in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroElimina))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RispostaEliminaMessaggi: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroElimina()
        Try
            _eliminati = _posta.EliminaMessaggi(_chiaveAccountCorrente, _cartellaDaEliminare, _idDaEliminare, _eliminazioneDefinitiva)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroElimina: " & ex.Message)
            _eliminati = 0
        Finally
            EseguiSuInterfaccia(AddressOf CompletaElimina)
        End Try
    End Sub

    Private Sub CompletaElimina()
        Try
            ChiudiAttesa()
            If _eliminati <= 0 Then
                Dim d As String = ""
                Try
                    d = _posta.UltimoErrore
                Catch
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(d), "Eliminazione non riuscita.", d))
                _idDaEliminare = New List(Of String)
                Return
            End If

            If _elencoMessaggi IsNot Nothing Then
                Dim rimasti As New List(Of RiepilogoMessaggio)
                For Each r As RiepilogoMessaggio In _elencoMessaggi
                    If String.Equals(r.ChiaveAccount, _chiaveAccountCorrente, StringComparison.Ordinal) AndAlso _idDaEliminare.Contains(r.Id) Then Continue For
                    rimasti.Add(r)
                Next
                _elencoMessaggi = rimasti
            End If

            Dim quanti As Integer = _eliminati
            Try
                _posta.RimuoviDallaCacheElenco(_chiaveAccountCorrente, _cartellaDaEliminare, _idDaEliminare)
            Catch exCache As Exception
                log.ScriviLog("MenuPosta.CompletaElimina copia locale: " & exCache.Message)
            End Try
            _idDaEliminare = New List(Of String)
            _messaggioCorrente = Nothing
            If _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed Then
                Try
                    _listaMessaggi.AzzeraSelezioneMemorizzata()
                Catch exSelezione As Exception
                    log.ScriviLog("MenuPosta.CompletaElimina azzeramento selezione: " & exSelezione.Message)
                End Try
            End If
            If quanti = 1 Then
                MostraMessaggio(If(_eliminazioneDefinitiva, "Messaggio eliminato.", "Messaggio spostato nel cestino."))
            Else
                MostraMessaggio(If(_eliminazioneDefinitiva, quanti.ToString() & " messaggi eliminati.", quanti.ToString() & " messaggi spostati nel cestino."))
            End If
            If _listaMessaggiGiaMostrata AndAlso _listaMessaggi IsNot Nothing AndAlso Not _listaMessaggi.IsDisposed Then
                AggiornaElementiListaMessaggi()
            Else
                MostraListaMessaggi()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaElimina: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoMessaggioChiusura(sender As Object, e As EventArgs)
        Try
            If _campoMessaggio IsNot Nothing Then
                RemoveHandler _campoMessaggio.chiusura, AddressOf CampoMessaggioChiusura
                _campoMessaggio.chiudi()
                _campoMessaggio = Nothing
            End If
        Catch
            _campoMessaggio = Nothing
        End Try
    End Sub

    Private Sub ChiudiCampoMessaggio()
        ChiudiLettoreHtml()
        Try

            If _campoMessaggio Is Nothing Then Return
            RemoveHandler _campoMessaggio.chiusura, AddressOf CampoMessaggioChiusura
            _campoMessaggio.chiudi()
            _campoMessaggio = Nothing
        Catch
            _campoMessaggio = Nothing
        End Try
    End Sub

    Private Sub OpzioneApriMessaggio(sender As Object, e As EventArgs)
        Try
            Dim voce As String = ""
            If Not UnSoloMessaggioSelezionato(voce) Then Return
            MessaggioSelezionato(voce)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneApriMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function DestinatarioDiRisposta() As String
        Try
            If _messaggioCorrente Is Nothing Then Return ""
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.IndirizzoLista) Then Return _messaggioCorrente.IndirizzoLista
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.RispondiA) Then Return _messaggioCorrente.RispondiA
            Return If(_messaggioCorrente.MittenteEmail, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DestinatarioDiRisposta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IndirizzoDellAccountCorrente() As String
        Try
            Dim chiave As String = _chiaveAccountCorrente
            If _messaggioCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_messaggioCorrente.ChiaveAccount) Then chiave = _messaggioCorrente.ChiaveAccount
            Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(chiave)
            If impostazioni Is Nothing Then Return ""
            Return If(impostazioni.IndirizzoEmail, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.IndirizzoDellAccountCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function UnisciIndirizzi(elenchi As List(Of String), daEscludere As List(Of String)) As String
        Dim risultato As New List(Of String)
        Try
            Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If daEscludere IsNot Nothing Then
                For Each escluso As String In daEscludere
                    If String.IsNullOrWhiteSpace(escluso) Then Continue For
                    For Each pezzoEscluso As String In escluso.Split(New Char() {","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
                        Dim ripulito As String = SoloIndirizzo(pezzoEscluso)
                        If Not String.IsNullOrWhiteSpace(ripulito) Then visti.Add(ripulito)
                    Next
                Next
            End If

            If elenchi IsNot Nothing Then
                For Each elenco As String In elenchi
                    If String.IsNullOrWhiteSpace(elenco) Then Continue For
                    For Each pezzo As String In elenco.Split(New Char() {","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
                        Dim indirizzo As String = SoloIndirizzo(pezzo)
                        If String.IsNullOrWhiteSpace(indirizzo) Then Continue For
                        If visti.Contains(indirizzo) Then Continue For
                        visti.Add(indirizzo)
                        risultato.Add(indirizzo)
                    Next
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.UnisciIndirizzi: " & ex.Message)
        End Try
        Return String.Join(", ", risultato.ToArray())
    End Function

    Private Function SoloIndirizzo(testo As String) As String
        Try
            Dim valore As String = If(testo, "").Trim()
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim apertura As Integer = valore.LastIndexOf("<"c)
            Dim chiusura As Integer = valore.LastIndexOf(">"c)
            If apertura >= 0 AndAlso chiusura > apertura Then valore = valore.Substring(apertura + 1, chiusura - apertura - 1)
            valore = valore.Trim().Trim(Chr(34))
            If Not valore.Contains("@") Then Return ""
            Return valore
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SoloIndirizzo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub PreparaCatenaRisposta()
        Try
            _composizioneInRispostaA = ""
            _composizioneRiferimenti = ""
            If _messaggioCorrente Is Nothing Then Return
            _composizioneInRispostaA = If(_messaggioCorrente.Identificativo, "")
            _composizioneRiferimenti = If(_messaggioCorrente.Riferimenti, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaCatenaRisposta: " & ex.Message)
        End Try
    End Sub

    Private Function CorpoCitato() As String
        Try
            If _messaggioCorrente Is Nothing Then Return ""
            Return Environment.NewLine & Environment.NewLine & "---" & Environment.NewLine & CLSMail.CorpoLeggibile(_messaggioCorrente.CorpoTesto, _messaggioCorrente.CorpoHtml)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CorpoCitato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function OggettoDiRisposta() As String
        Try
            Dim oggetto As String = ""
            If _messaggioCorrente IsNot Nothing Then oggetto = If(_messaggioCorrente.Oggetto, "")
            oggetto = oggetto.Trim()
            If oggetto.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) Then Return oggetto
            If oggetto.StartsWith("R:", StringComparison.OrdinalIgnoreCase) Then Return oggetto
            Return "Re: " & oggetto
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OggettoDiRisposta: " & ex.Message)
            Return "Re: "
        End Try
    End Function

    Private Sub OpzioneRispondi(sender As Object, e As EventArgs)
        Try
            ChiudiCampoMessaggio()
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            ComponiRisposta()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondi: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaIntestazioneRisposta(aTutti As Boolean)
        Try
            If _messaggioCorrente Is Nothing Then Return
            If Not aTutti Then
                _composizioneDestinatari = UnisciIndirizzi(New List(Of String) From {DestinatarioDiRisposta()}, Nothing)
                _composizioneCc = ""
            Else
                Dim mioIndirizzo As String = IndirizzoDellAccountCorrente()
                Dim esclusi As New List(Of String)
                If Not String.IsNullOrWhiteSpace(mioIndirizzo) Then esclusi.Add(mioIndirizzo)

                _composizioneDestinatari = UnisciIndirizzi(New List(Of String) From {DestinatarioDiRisposta()}, esclusi)
                If String.IsNullOrWhiteSpace(_composizioneDestinatari) Then
                    _composizioneDestinatari = UnisciIndirizzi(New List(Of String) From {DestinatarioDiRisposta()}, Nothing)
                End If

                Dim giaPresenti As New List(Of String)(esclusi)
                giaPresenti.Add(_composizioneDestinatari)
                _composizioneCc = UnisciIndirizzi(New List(Of String) From {_messaggioCorrente.Destinatari, _messaggioCorrente.Cc}, giaPresenti)
            End If
            _composizioneCcn = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcLista = SpezzaIndirizzi(_composizioneCc)
            _composizioneOggetto = OggettoDiRisposta()
            _allegatiComposizione = New List(Of String)
            PreparaCatenaRisposta()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaIntestazioneRisposta: " & ex.Message)
        End Try
    End Sub

    Private Sub ComponiRisposta()
        Try
            If _messaggioCorrente Is Nothing Then Return
            PreparaIntestazioneRisposta(False)
            _composizioneCorpo = CorpoCitato()
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComponiRisposta: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiTutti(sender As Object, e As EventArgs)
        Try
            ChiudiCampoMessaggio()
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            ComponiRispostaATutti()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiTutti: " & ex.Message)
        End Try
    End Sub

    Private Sub ComponiRispostaATutti()
        Try
            If _messaggioCorrente Is Nothing Then Return
            PreparaIntestazioneRisposta(True)
            _composizioneCorpo = CorpoCitato()
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComponiRispostaATutti: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneInoltra(sender As Object, e As EventArgs)
        Try
            ChiudiCampoMessaggio()
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            ComponiInoltro()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInoltra: " & ex.Message)
        End Try
    End Sub

    Private Sub ComponiInoltro()
        Try
            If _messaggioCorrente Is Nothing Then Return
            _composizioneDestinatari = ""
            _composizioneCc = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcn = ""
            _composizioneInRispostaA = ""
            _composizioneRiferimenti = ""
            _composizioneOggetto = "Fwd: " & If(_messaggioCorrente.Oggetto, "")
            _composizioneCorpo = Environment.NewLine & Environment.NewLine & "--- Messaggio inoltrato ---" & Environment.NewLine & CLSMail.CorpoLeggibile(_messaggioCorrente.CorpoTesto, _messaggioCorrente.CorpoHtml)
            _allegatiComposizione = New List(Of String)
            If _messaggioCorrente.Allegati IsNot Nothing Then
                For Each a As AllegatoMessaggio In _messaggioCorrente.Allegati
                    If Not String.IsNullOrWhiteSpace(a.Percorso) AndAlso File.Exists(a.Percorso) Then
                        _allegatiComposizione.Add(a.Percorso)
                    End If
                Next
            End If
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComponiInoltro: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneInfoMessaggio(sender As Object, e As EventArgs)
        Try
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            MostraInfoMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInfoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraInfoMessaggio()
        Try
            If _messaggioCorrente Is Nothing Then Return
            _listaInfoMessaggio = New WGMenuLista()
            _listaInfoMessaggio.multiselezioneConsentita = False
            _listaInfoMessaggio.impostaTitolo("Info messaggio")
            _listaInfoMessaggio.aggiungiElemento("Da: " & If(_messaggioCorrente.Mittente, "") & " <" & If(_messaggioCorrente.MittenteEmail, "") & ">")
            _listaInfoMessaggio.aggiungiElemento("A: " & If(_messaggioCorrente.Destinatari, ""))
            _listaInfoMessaggio.aggiungiElemento("Cc: " & If(_messaggioCorrente.Cc, ""))
            _listaInfoMessaggio.aggiungiElemento("Oggetto: " & If(_messaggioCorrente.Oggetto, ""))
            _listaInfoMessaggio.aggiungiElemento("Data: " & _messaggioCorrente.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm"))
            _listaInfoMessaggio.aggiungiElemento("Cartella: " & _messaggioCorrente.Cartella)
            _listaInfoMessaggio.aggiungiElemento("Letto: " & If(_messaggioCorrente.Letto, "sì", "no"))
            Dim n As Integer = 0
            If _messaggioCorrente.Allegati IsNot Nothing Then n = _messaggioCorrente.Allegati.Count
            _listaInfoMessaggio.aggiungiElemento("Allegati: " & n.ToString())
            If _messaggioCorrente.Allegati IsNot Nothing Then
                For Each a As AllegatoMessaggio In _messaggioCorrente.Allegati
                    _listaInfoMessaggio.aggiungiElemento("File: " & a.Nome)
                Next
            End If
            AddHandler _listaInfoMessaggio.escPremuto, AddressOf ListaInfoEsc
            _listaInfoMessaggio.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraInfoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.Rispondi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiTuttiDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.RispondiTutti)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiTuttiDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneInoltraDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.Inoltra)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInoltraDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneInfoMessaggioDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.Info)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInfoMessaggioDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaAzioneSuMessaggioInLista(azione As AzioneSuMessaggio)
        Try
            If azione = AzioneSuMessaggio.Nessuna Then Return
            If _caricamentoMessaggioPerAzioneInCorso Then Return
            Dim voce As String = ""
            If Not UnSoloMessaggioSelezionato(voce, AzioneSulMessaggioPiuRecente(azione)) Then Return
            Dim parti() As String = voce.Split("|"c)
            If parti.Length < 2 Then
                MostraMessaggio("Seleziona prima un messaggio.")
                Return
            End If
            Dim chiave As String = parti(0)
            Dim idMsg As String = parti(1)
            If String.IsNullOrWhiteSpace(chiave) OrElse String.IsNullOrWhiteSpace(idMsg) Then
                MostraMessaggio("Seleziona prima un messaggio.")
                Return
            End If
            _chiaveAccountCorrente = chiave
            If _messaggioCorrente IsNot Nothing AndAlso
               String.Equals(_messaggioCorrente.ChiaveAccount, chiave, StringComparison.Ordinal) AndAlso
               String.Equals(_messaggioCorrente.Id, idMsg, StringComparison.Ordinal) Then
                EseguiAzioneSuMessaggio(azione)
                Return
            End If
            _azioneDopoCaricamento = azione
            _chiaveMessaggioDaCaricare = chiave
            _idMessaggioDaCaricare = idMsg
            _cartellaMessaggioDaCaricare = PercorsoCartellaPerAccount(chiave)
            _messaggioCaricatoPerAzione = Nothing
            _caricamentoMessaggioPerAzioneInCorso = True
            MostraAttesa("Caricamento del messaggio in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCaricaMessaggioPerAzione))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PreparaAzioneSuMessaggioInLista: " & ex.Message)
            _caricamentoMessaggioPerAzioneInCorso = False
            _azioneDopoCaricamento = AzioneSuMessaggio.Nessuna
        End Try
    End Sub

    Private Function AzioneSulMessaggioPiuRecente(azione As AzioneSuMessaggio) As Boolean
        Try
            Select Case azione
                Case AzioneSuMessaggio.Rispondi, AzioneSuMessaggio.RispondiTutti, AzioneSuMessaggio.Inoltra, AzioneSuMessaggio.RiassumiAi, AzioneSuMessaggio.RispondiAi, AzioneSuMessaggio.RispondiTuttiAi
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzioneSulMessaggioPiuRecente: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LavoroCaricaMessaggioPerAzione()
        Try
            _messaggioCaricatoPerAzione = _posta.ApriMessaggio(_chiaveMessaggioDaCaricare, _cartellaMessaggioDaCaricare, _idMessaggioDaCaricare)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCaricaMessaggioPerAzione: " & ex.Message)
            _messaggioCaricatoPerAzione = Nothing
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCaricamentoMessaggioPerAzione)
        End Try
    End Sub

    Private Sub CompletaCaricamentoMessaggioPerAzione()
        Try
            _caricamentoMessaggioPerAzioneInCorso = False
            ChiudiAttesa()
            Dim azione As AzioneSuMessaggio = _azioneDopoCaricamento
            _azioneDopoCaricamento = AzioneSuMessaggio.Nessuna
            Dim caricato As MessaggioCompleto = _messaggioCaricatoPerAzione
            _messaggioCaricatoPerAzione = Nothing
            If OperazioneAnnullata() Then Return
            If caricato Is Nothing Then
                Dim d As String = ""
                Try
                    d = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaCaricamentoMessaggioPerAzione errore: " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(d), "Messaggio non caricabile.", d))
                Return
            End If
            _messaggioCorrente = caricato
            _mostraHtmlOriginale = False
            AggiornaStatoLocaleMessaggioLetto()
            EseguiAzioneSuMessaggio(azione)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCaricamentoMessaggioPerAzione: " & ex.Message)
            _caricamentoMessaggioPerAzioneInCorso = False
        End Try
    End Sub

    Private Sub EseguiAzioneSuMessaggio(azione As AzioneSuMessaggio)
        Try
            If _messaggioCorrente Is Nothing Then Return
            Select Case azione
                Case AzioneSuMessaggio.Rispondi
                    ComponiRisposta()
                Case AzioneSuMessaggio.RispondiTutti
                    ComponiRispostaATutti()
                Case AzioneSuMessaggio.Inoltra
                    ComponiInoltro()
                Case AzioneSuMessaggio.Info
                    MostraInfoMessaggio()
                Case AzioneSuMessaggio.RiassumiAi
                    AvviaRiassuntoAi()
                Case AzioneSuMessaggio.RispondiAi
                    ChiediIstruzioniRispostaAi(False)
                Case AzioneSuMessaggio.RispondiTuttiAi
                    ChiediIstruzioniRispostaAi(True)
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuPosta.EseguiAzioneSuMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaInfoEsc(sender As Object, e As EventArgs)
        Try
            If _listaInfoMessaggio IsNot Nothing Then _listaInfoMessaggio.abbandona()
            _listaInfoMessaggio = Nothing
        Catch
            _listaInfoMessaggio = Nothing
        End Try
    End Sub

    Private _percorsiAllegatiScelti As New List(Of String)
    Private _menuAzioniAllegato As WGMenu

    Private Sub OpzioneAllegatiMessaggio(sender As Object, e As EventArgs)
        Try
            If _messaggioCorrente Is Nothing OrElse _messaggioCorrente.Allegati Is Nothing OrElse _messaggioCorrente.Allegati.Count = 0 Then
                MostraMessaggio("Nessun allegato.")
                Return
            End If
            _listaAllegatiMessaggio = New WGMenuLista()
            _listaAllegatiMessaggio.multiselezioneConsentita = True
            _listaAllegatiMessaggio.impostaTitolo("Allegati")
            For Each a As AllegatoMessaggio In _messaggioCorrente.Allegati
                _listaAllegatiMessaggio.aggiungiElemento(a.Nome, a.Percorso)
            Next
            AddHandler _listaAllegatiMessaggio.ElementoSelezionato, AddressOf AllegatoMessaggioSelezionato
            AddHandler _listaAllegatiMessaggio.ElementiSelezionati, AddressOf AllegatiMessaggioSelezionati
            AddHandler _listaAllegatiMessaggio.escPremuto, AddressOf ListaAllegatiMessaggioEsc
            _listaAllegatiMessaggio.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAllegatiMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAllegatiMessaggioEsc(sender As Object, e As EventArgs)
        Try
            If _listaAllegatiMessaggio IsNot Nothing Then _listaAllegatiMessaggio.abbandona()
            _listaAllegatiMessaggio = Nothing
        Catch
            _listaAllegatiMessaggio = Nothing
        End Try
    End Sub

    Private Sub AllegatoMessaggioSelezionato(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            MostraMenuApriSalvaAllegati(New List(Of String) From {percorso})
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AllegatoMessaggioSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub AllegatiMessaggioSelezionati(percorsi As List(Of String))
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return
            MostraMenuApriSalvaAllegati(percorsi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AllegatiMessaggioSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuApriSalvaAllegati(percorsi As List(Of String))
        Try
            _percorsiAllegatiScelti = New List(Of String)(percorsi)
            _menuAzioniAllegato = New WGMenu()
            Dim titolo As String
            If percorsi.Count = 1 Then
                titolo = Path.GetFileName(percorsi(0))
                _menuAzioniAllegato.aggiungi("Apri", AddressOf OpzioneApriAllegatoMessaggio)
                _menuAzioniAllegato.aggiungi("Salva", AddressOf OpzioneSalvaAllegatoMessaggio)
                _menuAzioniAllegato.aggiungi("Stampa", AddressOf OpzioneStampaAllegatoMessaggio)
            Else
                titolo = percorsi.Count.ToString() & " allegati selezionati"
                _menuAzioniAllegato.aggiungi("Salva", AddressOf OpzioneSalvaAllegatoMessaggio)
                _menuAzioniAllegato.aggiungi("Stampa", AddressOf OpzioneStampaAllegatoMessaggio)
            End If
            _menuAzioniAllegato.avvia(titolo, True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraMenuApriSalvaAllegati: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneApriAllegatoMessaggio(sender As Object, e As EventArgs)
        Try
            If _percorsiAllegatiScelti Is Nothing OrElse _percorsiAllegatiScelti.Count <> 1 Then
                MostraMessaggio("Allegato non disponibile.")
                Return
            End If
            Dim percorsoOriginale As String = _percorsiAllegatiScelti(0)
            If String.IsNullOrWhiteSpace(percorsoOriginale) OrElse Not System.IO.File.Exists(percorsoOriginale) Then
                MostraMessaggio("Allegato non disponibile.")
                Return
            End If

            Dim cartellaTemp As String = Path.Combine(Path.GetTempPath(), "WGPostaAllegati")
            If Not Directory.Exists(cartellaTemp) Then Directory.CreateDirectory(cartellaTemp)
            Dim percorsoTemp As String = Path.Combine(cartellaTemp, Path.GetFileName(percorsoOriginale))
            Try
                System.IO.File.Copy(percorsoOriginale, percorsoTemp, True)
            Catch exCopia As Exception
                log.ScriviLog("MenuPosta.OpzioneApriAllegatoMessaggio copia: " & exCopia.Message)
                MostraMessaggio("Impossibile copiare l'allegato in una cartella temporanea.")
                Return
            End Try

            Dim file As New MenuFile()
            file.Apri(percorsoTemp)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneApriAllegatoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneStampaAllegatoMessaggio(sender As Object, e As EventArgs)
        Try
            If _percorsiAllegatiScelti Is Nothing OrElse _percorsiAllegatiScelti.Count = 0 Then
                MostraMessaggio("Allegato non disponibile.")
                Return
            End If
            Dim esistenti As New List(Of String)
            For Each percorso As String In _percorsiAllegatiScelti
                Try
                    If Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) Then esistenti.Add(percorso)
                Catch exFile As Exception
                    log.ScriviLog("MenuPosta.OpzioneStampaAllegatoMessaggio verifica (" & If(percorso, "") & "): " & exFile.Message)
                End Try
            Next
            If esistenti.Count = 0 Then
                MostraMessaggio("Allegato non disponibile.")
                Return
            End If
            Dim gestioneFile As New MenuFile()
            gestioneFile.StampaFile(esistenti)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneStampaAllegatoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSalvaAllegatoMessaggio(sender As Object, e As EventArgs)
        Try
            If _percorsiAllegatiScelti Is Nothing OrElse _percorsiAllegatiScelti.Count = 0 Then
                MostraMessaggio("Allegato non disponibile.")
                Return
            End If
            _dialogSalvaAllegato = New WGFileDialog()
            _dialogSalvaAllegato.MultiselezioneConsentita = False
            AddHandler _dialogSalvaAllegato.singolaselezione, AddressOf CartellaSalvataggioAllegatoScelta
            _dialogSalvaAllegato.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSalvaAllegatoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub CartellaSalvataggioAllegatoScelta(cartellaDestinazione As String)
        Try
            If _dialogSalvaAllegato IsNot Nothing Then
                RemoveHandler _dialogSalvaAllegato.singolaselezione, AddressOf CartellaSalvataggioAllegatoScelta
                _dialogSalvaAllegato = Nothing
            End If
            If String.IsNullOrWhiteSpace(cartellaDestinazione) OrElse Not Directory.Exists(cartellaDestinazione) Then Return
            If _percorsiAllegatiScelti Is Nothing OrElse _percorsiAllegatiScelti.Count = 0 Then Return

            Dim daCopiare As New List(Of String)
            For Each percorsoOriginale As String In _percorsiAllegatiScelti
                Try
                    If String.IsNullOrWhiteSpace(percorsoOriginale) OrElse Not File.Exists(percorsoOriginale) Then Continue For
                    daCopiare.Add(percorsoOriginale)
                Catch exVerifica As Exception
                    log.ScriviLog("MenuPosta.CartellaSalvataggioAllegatoScelta verifica (" & If(percorsoOriginale, "") & "): " & exVerifica.Message)
                End Try
            Next
            If daCopiare.Count = 0 Then
                MostraMessaggio("Salvataggio non riuscito.")
                _percorsiAllegatiScelti = New List(Of String)
                Return
            End If
            Try
                WGCentroDiControllo.GetInstance.AggiungiCopia(daCopiare, cartellaDestinazione)
                WGCentroDiControllo.GetInstance.AvviaOperazioniFile()
            Catch exCopia As Exception
                log.ScriviLog("MenuPosta.CartellaSalvataggioAllegatoScelta copia: " & exCopia.Message & Environment.NewLine & exCopia.StackTrace)
                MostraMessaggio("Salvataggio non riuscito.")
            End Try
            _percorsiAllegatiScelti = New List(Of String)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaSalvataggioAllegatoScelta: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAltroAccount(sender As Object, e As EventArgs)
        Try
            _scopoAccount = ScopoAccount.ControllaAltro
            MostraElencoAccount(False)
        Catch
        End Try
    End Sub

    Private Sub ApriComposizione()
        Try
            ApriComposizioneDestinatario()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriComposizione: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriComposizioneDestinatario()
        Try
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi("A", "msg.a", True, False, _composizioneDestinatari)
            AddHandler _campiComposizione.Fine, AddressOf CampiDestinatarioFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriComposizioneDestinatario: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiDestinatarioFine(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ""
            If _campiComposizione IsNot Nothing Then testo = If(_campiComposizione.RecuperaValore("msg.a"), "").Trim()
            DisposeCampiComposizione()

            If testo.Contains("@") Then
                _composizioneDestinatari = testo
                ApriComposizioneDettagli()
                Return
            End If

            _ultimoTestoDestinatario = testo
            CercaDestinatario(testo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiDestinatarioFine: " & ex.Message)
        End Try
    End Sub

    Private Sub CercaDestinatario(testo As String)
        Try
            PreparaRubricaDestinatari(testo)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CercaDestinatario: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraRisultatiDestinatario(testo As String)
        Try
            _listaDestinatari = New WGMenuLista()
            _listaDestinatari.multiselezioneConsentita = False
            _listaDestinatari.impostaTitolo("Scegli destinatario")

            Dim gia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            Dim recenti As DataTable = _posta.OttieniDestinatariRecenti(testo, 15)
            If recenti IsNot Nothing Then
                For Each riga As DataRow In recenti.Rows
                    Dim email As String = riga("Email").ToString()
                    If String.IsNullOrWhiteSpace(email) OrElse Not gia.Add(email.Trim()) Then Continue For
                    Dim nome As String = riga("NomeVisualizzato").ToString()
                    _listaDestinatari.aggiungiElemento(If(String.IsNullOrWhiteSpace(nome), email, nome & " <" & email & ">"), email)
                Next
            End If

            Dim rubrica As CLSContatti = Me.Rubrica
            If rubrica Is Nothing Then Return
            Dim risultatiContatti As DataTable
            If String.IsNullOrWhiteSpace(_rubricaDestinatari) Then
                risultatiContatti = rubrica.CercaConEmail(testo, "", "")
            Else
                risultatiContatti = rubrica.CercaConEmail(testo, "", _rubricaDestinatari)
            End If
            If risultatiContatti IsNot Nothing Then
                For Each riga As DataRow In risultatiContatti.Rows
                    Dim email As String = riga("Email").ToString()
                    If String.IsNullOrWhiteSpace(email) OrElse Not gia.Add(email.Trim()) Then Continue For
                    Dim nome As String = (If(riga("Nome").ToString(), "") & " " & If(riga("Cognome").ToString(), "")).Trim()
                    _listaDestinatari.aggiungiElemento(If(String.IsNullOrWhiteSpace(nome), email, nome & " <" & email & ">"), email)
                Next
            End If

            If gia.Count = 0 Then
                _listaDestinatari.aggiungiElemento("Nessun risultato per """ & testo & """")
            End If

            _listaDestinatari.aggiungiOpzione("Usa il testo scritto (""" & testo & """)", AddressOf OpzioneUsaTestoDestinatario)
            _listaDestinatari.aggiungiOpzione("Cerca in un'altra rubrica", AddressOf OpzioneCambiaRubricaDestinatari)
            AddHandler _listaDestinatari.ElementoSelezionato, AddressOf DestinatarioSelezionato
            AddHandler _listaDestinatari.escPremuto, AddressOf ListaDestinatariEsc
            _listaDestinatari.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CercaDestinatario: " & ex.Message)
        End Try
    End Sub

    Private Sub DestinatarioSelezionato(id As String)
        Try
            If _listaDestinatari IsNot Nothing Then _listaDestinatari.abbandona()
            _listaDestinatari = Nothing
            If String.IsNullOrWhiteSpace(id) Then Return
            _composizioneDestinatari = id
            ApriComposizioneDettagli()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DestinatarioSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneUsaTestoDestinatario(sender As Object, e As EventArgs)
        Try
            If _listaDestinatari IsNot Nothing Then _listaDestinatari.abbandona()
            _listaDestinatari = Nothing
            _composizioneDestinatari = _ultimoTestoDestinatario
            ApriComposizioneDettagli()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneUsaTestoDestinatario: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaDestinatariEsc(sender As Object, e As EventArgs)
        Try
            If _listaDestinatari IsNot Nothing Then _listaDestinatari.abbandona()
            _listaDestinatari = Nothing
            ApriComposizioneDestinatario()
        Catch
            _listaDestinatari = Nothing
        End Try
    End Sub

    Private Sub ApriComposizioneDettagli()
        Try
            _campiComposizione = New Campi()
            _campiComposizione.SalvaSuEscape = False
            _campiComposizione.Aggiungi("A", "msg.a", True, False, _composizioneDestinatari)
            _campiComposizione.AggiungiMultiConValori("Altri destinatari", "msg.a2", ValoriIniziali(_composizioneDestinatariMulti), False, 0)
            _campiComposizione.AggiungiMultiConValori("Cc", "msg.cc", ValoriIniziali(_composizioneCcLista), False, 0)
            _campiComposizione.AggiungiMultiConValori("Ccn", "msg.ccn", ValoriIniziali(_composizioneCcnLista), False, 0)
            _campiComposizione.Aggiungi("Oggetto", "msg.oggetto", False, False, _composizioneOggetto)
            If ScritturaMessaggiConWord() Then
                _campiComposizione.AggiungiWord("Messaggio", "msg.corpo", False, False)
            Else
                _campiComposizione.AggiungiMultiriga("Messaggio", "msg.corpo", False, False, _composizioneCorpo)
            End If
            _campiComposizione.AggiungiOpzione("Cerca il destinatario in rubrica", AddressOf OpzioneCambiaDestinatario)
            _campiComposizione.AggiungiOpzione("Allegati", AddressOf OpzioneGestisciAllegati)
            If IntelligenzaArtificialeDisponibile() Then
                _campiComposizione.AggiungiOpzione("Scrivi il testo con l'intelligenza artificiale", AddressOf OpzioneScriviTestoAi)
            End If
            _campiComposizione.AggiungiOpzione("Invia", AddressOf OpzioneInviaDaCampi)
            RegistraAzioniUscitaComposizione(_campiComposizione)
            AddHandler _campiComposizione.Fine, AddressOf CampiComposizioneFine
            AddHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura
            _campiComposizione.Avvia()
            RipristinaCorpoWord(_campiComposizione)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriComposizioneDettagli: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraDestinatariAggiuntivi()
        Try
            _composizioneDestinatariMulti = New List(Of String)
            _composizioneCcLista = New List(Of String)
            _composizioneCcnLista = New List(Of String)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzzeraDestinatariAggiuntivi: " & ex.Message)
        End Try
    End Sub

    Private Function ValoriIniziali(valori As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If valori Is Nothing Then Return risultato
            For Each valore As String In valori
                If Not String.IsNullOrWhiteSpace(valore) Then risultato.Add(valore.Trim())
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ValoriIniziali: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Sub OpzioneCambiaDestinatario(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            DisposeCampiComposizione()
            ApriComposizioneDestinatario()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneCambiaDestinatario: " & ex.Message)
        End Try
    End Sub

    Private Sub DisposeCampiComposizione()
        Try
            If _campiComposizione Is Nothing Then Return
            Try
                RemoveHandler _campiComposizione.Fine, AddressOf CampiDestinatarioFine
            Catch
            End Try
            Try
                RemoveHandler _campiComposizione.Fine, AddressOf CampiComposizioneFine
            Catch
            End Try
            Try
                RemoveHandler _campiComposizione.Fine, AddressOf CampiCercaFine
            Catch
            End Try
            RemoveHandler _campiComposizione.Chiusura, AddressOf CampiComposizioneChiusura

            _campiComposizione = Nothing
        Catch
            _campiComposizione = Nothing
        End Try
    End Sub

    Private Sub CampiComposizioneChiusura(sender As Object, e As EventArgs)
        Try
            DisposeCampiComposizione()
        Catch
        End Try
    End Sub

    Private Sub CampiComposizioneFine(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            DisposeCampiComposizione()
            _campiComposizione = Nothing
            AvviaInvioMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiComposizioneFine: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaDatiComposizioneDaCampi()
        Try
            If _campiComposizione Is Nothing Then Return
            _composizioneDestinatariMulti = _campiComposizione.RecuperaListaValori("msg.a2")
            _composizioneCcLista = _campiComposizione.RecuperaListaValori("msg.cc")
            _composizioneCcnLista = _campiComposizione.RecuperaListaValori("msg.ccn")
            _composizioneOggetto = _campiComposizione.RecuperaValore("msg.oggetto")
            CatturaCorpoWord(_campiComposizione)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SalvaDatiComposizioneDaCampi: " & ex.Message)
        End Try
        Try
            If _campiComposizione Is Nothing Then Return
            Dim destinatarioScritto As String = If(_campiComposizione.RecuperaValore("msg.a"), "").Trim()
            If destinatarioScritto <> "" Then _composizioneDestinatari = IndirizzoMinuscolo(destinatarioScritto)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SalvaDatiComposizioneDaCampi (destinatario): " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneInviaDaCampi(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            If _campiComposizione IsNot Nothing Then _campiComposizione.Chiudi()
            DisposeCampiComposizione()
            AvviaInvioMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneInviaDaCampi: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneGestisciAllegati(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            _listaAllegati = New WGMenuLista()
            _listaAllegati.multiselezioneConsentita = False
            _listaAllegati.impostaTitolo("Allegati")
            If _allegatiComposizione Is Nothing OrElse _allegatiComposizione.Count = 0 Then
                _listaAllegati.aggiungiElemento("Nessun allegato")
            Else
                For Each percorso As String In _allegatiComposizione
                    _listaAllegati.aggiungiElemento(Path.GetFileName(percorso), percorso)
                Next
            End If
            _listaAllegati.aggiungiOpzione("Aggiungi allegato", AddressOf OpzioneAggiungiAllegato)
            _listaAllegati.aggiungiOpzione("Rimuovi allegato selezionato", AddressOf OpzioneRimuoviAllegato)
            AddHandler _listaAllegati.escPremuto, AddressOf ListaAllegatiEsc
            _listaAllegati.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneGestisciAllegati: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAllegatiEsc(sender As Object, e As EventArgs)
        Try
            If _listaAllegati IsNot Nothing Then _listaAllegati.abbandona()
            _listaAllegati = Nothing
        Catch
            _listaAllegati = Nothing
        End Try
    End Sub

    Private Sub OpzioneAggiungiAllegato(sender As Object, e As EventArgs)
        Try
            _dialogAllegati = New WGFileDialog()
            _dialogAllegati.MultiselezioneConsentita = True
            For Each estensione As String In EstensioniConsentite()
                _dialogAllegati.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogAllegati.selezioneterminata, AddressOf AllegatiSelezionati
            _dialogAllegati.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAggiungiAllegato: " & ex.Message)
        End Try
    End Sub

    Private Sub AllegatiSelezionati(elementi As List(Of String))
        Try
            If _dialogAllegati IsNot Nothing Then
                RemoveHandler _dialogAllegati.selezioneterminata, AddressOf AllegatiSelezionati
                _dialogAllegati = Nothing
            End If
            If elementi Is Nothing Then Return
            If _allegatiComposizione Is Nothing Then _allegatiComposizione = New List(Of String)
            For Each f As String In elementi
                If Not String.IsNullOrWhiteSpace(f) AndAlso File.Exists(f) AndAlso Not _allegatiComposizione.Contains(f) Then
                    _allegatiComposizione.Add(f)
                End If
            Next
            AggiornaListaAllegati()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AllegatiSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaListaAllegati()
        Try
            If _listaAllegati Is Nothing Then Return
            _listaAllegati.pulisci()
            If _allegatiComposizione Is Nothing OrElse _allegatiComposizione.Count = 0 Then
                _listaAllegati.aggiungiElemento("Nessun allegato")
            Else
                For Each percorso As String In _allegatiComposizione
                    _listaAllegati.aggiungiElemento(Path.GetFileName(percorso), percorso)
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiornaListaAllegati: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRimuoviAllegato(sender As Object, e As EventArgs)
        Try
            If _listaAllegati Is Nothing OrElse _listaAllegati.IsDisposed Then Return
            Dim elementi As List(Of String) = _listaAllegati.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim elemento As String = elementi(0)
            If String.IsNullOrWhiteSpace(elemento) Then Return
            If _allegatiComposizione IsNot Nothing Then _allegatiComposizione.Remove(elemento)
            AggiornaListaAllegati()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRimuoviAllegato: " & ex.Message)
        End Try
    End Sub

    Private Function EstensioniConsentite() As String()
        Return New String() {
            ".pdf", ".doc", ".docx", ".odt", ".txt", ".rtf", ".xls", ".xlsx", ".csv",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
            ".mp3", ".mp4", ".wav", ".avi", ".mkv",
            ".zip", ".rar", ".7z", ".vb", ".cs", ".exe", ".dll", ".json", ".xml", ".sql", ".eml"
        }
    End Function

    Private Sub AvviaInvioMessaggio()
        Try
            If String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then
                CaricaElencoAccount()
                Dim attivi = _posta.OttieniElencoAccount(True)
                If attivi Is Nothing OrElse attivi.Count = 0 Then
                    MostraMessaggio("Nessun account attivo per l'invio.")
                    Return
                End If
                _chiaveAccountCorrente = attivi(0).Chiave
            End If
            Dim destinatari As New List(Of String)
            destinatari.AddRange(SpezzaIndirizzi(_composizioneDestinatari))
            destinatari.AddRange(SpezzaListaIndirizzi(_composizioneDestinatariMulti))
            _invioDestinatari = destinatari
            _invioCc = SpezzaListaIndirizzi(_composizioneCcLista)
            _invioCcn = SpezzaListaIndirizzi(_composizioneCcnLista)
            _invioOggetto = _composizioneOggetto
            _invioCorpo = AvanzataMP().ApplicaFirma(_composizioneCorpo)
            _invioAllegati = New List(Of String)(_allegatiComposizione)
            If _invioDestinatari.Count = 0 Then
                MostraMessaggio("Indica almeno un destinatario.")
                Return
            End If
            MostraAttesa("Invio messaggio in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroInvio))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaInvioMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function SpezzaIndirizzi(testo As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return risultato
            For Each pezzo As String In testo.Split(New Char() {";"c, ","c}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = pezzo.Trim()
                If t <> "" Then risultato.Add(t)
            Next
        Catch
        End Try
        Return risultato
    End Function

    Private Sub LavoroInvio()
        Try
            _invioRiuscito = _posta.InviaMessaggioCompleto(_chiaveAccountCorrente, CostruisciContenutoInvio())
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroInvio: " & ex.Message)
            _invioRiuscito = False
        Finally
            EseguiSuInterfaccia(AddressOf CompletaInvio)
        End Try
    End Sub

    Private Sub CompletaInvio()
        Try
            ChiudiAttesa()
            If _invioRiuscito Then
                MostraMessaggio("Messaggio inviato.")
                _allegatiComposizione = New List(Of String)
                _composizioneDestinatari = ""
                _composizioneCc = ""
                _composizioneCcn = ""
                _composizioneDestinatariMulti = New List(Of String)
                _composizioneCcLista = New List(Of String)
                _composizioneCcnLista = New List(Of String)
                _composizioneOggetto = ""
                _composizioneCorpo = ""
                PulisciBozzaComposizione()
                AggiornaPostaInviataDopoInvio()
            Else
                Dim d As String = ""
                Try
                    d = _posta.UltimoErrore
                    log.ScriviLog(d)
                Catch
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(d), "Invio non riuscito.", d))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaInvio: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaPostaInviataDopoInvio()
        Try
            If Not String.Equals(_tipoCartellaSpeciale, "sent", StringComparison.OrdinalIgnoreCase) Then Return
            If _listaMessaggiRisultatiRicerca Then Return
            If _listaMessaggi Is Nothing OrElse _listaMessaggi.IsDisposed OrElse Not _listaMessaggiGiaMostrata Then Return
            If _caricamentoMessaggiInCorso OrElse _controlloNuoviMessaggiInCorso Then Return
            _controlloNuoviMessaggiInCorso = True
            AvviaLavoro(Sub() EseguiControlloNuoviMessaggi(True))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiornaPostaInviataDopoInvio: " & ex.Message)
            _controlloNuoviMessaggiInCorso = False
        End Try
    End Sub

    Public Sub InviaMessaggioConRichiestaDati(Optional destinatario As String = "", Optional oggetto As String = "", Optional corpo As String = "")
        Try
            _composizioneDestinatari = If(destinatario, "")
            _composizioneCc = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcn = ""
            _composizioneOggetto = If(oggetto, "")
            _composizioneCorpo = If(corpo, "")
            _allegatiComposizione = New List(Of String)

            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo per l'invio.")
                Return
            End If
            If attivi.Count > 1 Then
                MostraSceltaAccountPer(ScopoAccount.ChiediInvio, "Casella da cui inviare")
                Return
            End If
            _chiaveAccountCorrente = attivi(0).Chiave
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.InviaMessaggioConRichiestaDati: " & ex.Message)
        End Try
    End Sub

#Region "Comandi vocali"

    Public Class DestinatarioVocale
        Public Property Nome As String = ""
        Public Property Email As String = ""
        Public Property Punteggio As Integer = 0

        Public ReadOnly Property Descrizione As String
            Get
                Try
                    If String.IsNullOrWhiteSpace(Nome) Then Return Email
                    Return Nome & " <" & Email & ">"
                Catch
                    Return Email
                End Try
            End Get
        End Property
    End Class

    Private _destinatariVocaliCorrenti As New List(Of DestinatarioVocale)
    Private _listaDestinatariVocali As WGMenuLista
    Private _domandaInvioVocale As WGDomanda
    Private _invioVocaleSenzaConferma As Boolean = False
    Private _apriPerControlloVocale As Boolean = False
    Private _descrizioneDestinatarioVocale As String = ""
    Private _soloNonLettiVocale As Boolean = True
    Private _massimoMessaggiVocali As Integer = 10
    Private _testoPostaVocale As String = ""
    Private _nessunMessaggioVocale As Boolean = False

    Public Sub ComandoInviaEmail(destinatario As String, Optional oggetto As String = "", Optional corpo As String = "", Optional inviaSenzaConferma As Boolean = False, Optional apriPerControllo As Boolean = False)
        Try
            _chiuso = False
            _composizioneDestinatari = ""
            _composizioneCc = ""
            AzzeraDestinatariAggiuntivi()
            _composizioneCcn = ""
            _composizioneOggetto = If(oggetto, "").Trim()
            _composizioneCorpo = If(corpo, "").Trim()
            _allegatiComposizione = New List(Of String)
            _invioVocaleSenzaConferma = inviaSenzaConferma AndAlso Not apriPerControllo
            _apriPerControlloVocale = apriPerControllo
            _descrizioneDestinatarioVocale = ""

            Dim chiave As String = ChiaveAccountPerComandoVocale()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Nessun account attivo per l'invio.")
                Return
            End If
            _chiaveAccountCorrente = chiave

            If String.IsNullOrWhiteSpace(destinatario) Then
                ApriComposizioneDestinatario()
                Return
            End If

            Dim trovati As List(Of DestinatarioVocale) = TrovaDestinatariPerNome(destinatario)
            If trovati.Count = 0 Then
                _composizioneDestinatari = IndirizzoMinuscolo(destinatario)
                If _apriPerControlloVocale Then
                    _apriPerControlloVocale = False
                    _descrizioneDestinatarioVocale = destinatario.Trim()
                    ApriComposizioneDettagli()
                    MostraMessaggio("Nessun indirizzo trovato per " & destinatario.Trim() & ", né in rubrica né tra i destinatari recenti. Correggere il destinatario con la voce Destinatario prima di inviare.")
                    Return
                End If
                ApriComposizioneDestinatario()
                MostraMessaggio("Nessun indirizzo trovato per " & destinatario.Trim() & ", né in rubrica né tra i destinatari recenti. Correggere l'indirizzo nel campo A.")
                Return
            End If

            Dim scelto As DestinatarioVocale = SceltaDestinatarioAutomatica(trovati)
            If scelto IsNot Nothing Then
                ProseguiInvioVocale(scelto)
                Return
            End If

            MostraSceltaDestinatarioVocale(destinatario.Trim(), trovati)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComandoInviaEmail: " & ex.Message & Environment.NewLine & ex.StackTrace)
            MostraMessaggio("Non è stato possibile preparare il messaggio.")
        End Try
    End Sub

    Private Function ChiaveAccountPerComandoVocale() As String
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then Return ""

            If Not String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then
                For Each account As ImpostazioniAccountMail In attivi
                    If account IsNot Nothing AndAlso String.Equals(account.Chiave, _chiaveAccountCorrente, StringComparison.Ordinal) Then Return account.Chiave
                Next
            End If

            For Each account As ImpostazioniAccountMail In attivi
                If account IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(account.Chiave) Then Return account.Chiave
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaveAccountPerComandoVocale: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function TrovaDestinatariPerNome(nome As String) As List(Of DestinatarioVocale)
        Dim risultato As New List(Of DestinatarioVocale)
        Try
            Dim testo As String = If(nome, "").Trim()
            If testo = "" Then Return risultato

            If testo.Contains("@") Then
                Dim indirizzo As String = SoloIndirizzo(testo)
                If String.IsNullOrWhiteSpace(indirizzo) Then indirizzo = testo.Replace(" ", "")
                risultato.Add(New DestinatarioVocale With {.Nome = "", .Email = IndirizzoMinuscolo(indirizzo), .Punteggio = 3})
                Return risultato
            End If

            Dim gia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            CercaDestinatariVocali(testo, risultato, gia)

            If risultato.Count = 0 Then
                Dim parole As String() = testo.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                If parole.Length > 1 Then
                    Dim parolaPiuLunga As String = parole(0)
                    For Each parola As String In parole
                        If parola.Length > parolaPiuLunga.Length Then parolaPiuLunga = parola
                    Next
                    Dim parziali As New List(Of DestinatarioVocale)
                    CercaDestinatariVocali(parolaPiuLunga, parziali, gia)
                    For Each candidato As DestinatarioVocale In parziali
                        If ContieneTutteLeParole(candidato.Nome, parole) Then risultato.Add(candidato)
                    Next
                End If
            End If

            For Each candidato As DestinatarioVocale In risultato
                candidato.Punteggio = PunteggioDestinatario(testo, candidato.Nome)
            Next
            OrdinaDestinatariVocali(risultato)

            If risultato.Count = 0 Then CercaDestinatariFonetici(testo, risultato)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TrovaDestinatariPerNome: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Const SOMIGLIANZA_MINIMA_DESTINATARIO As Double = 0.75
    Private Const SOMIGLIANZA_MINIMA_INDIRIZZO As Double = 0.85
    Private Const MASSIMO_DESTINATARI_FONETICI As Integer = 5
    Private Const MASSIMO_RECENTI_FONETICI As Integer = 500

    Private Function ChiaveFoneticaDestinatario(testo As String) As String
        Try
            Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim senzaAccenti As New StringBuilder()
            For Each carattere As Char In scomposto
                If Char.GetUnicodeCategory(carattere) <> System.Globalization.UnicodeCategory.NonSpacingMark Then senzaAccenti.Append(carattere)
            Next
            Dim chiave As String = System.Text.RegularExpressions.Regex.Replace(senzaAccenti.ToString(), "[^a-z0-9]", "")
            For Each coppia As String() In New String()() {
                New String() {"sch", "s"}, New String() {"ph", "f"}, New String() {"th", "t"}, New String() {"sh", "s"},
                New String() {"ck", "c"}, New String() {"gh", "g"}, New String() {"ch", "c"}, New String() {"k", "c"},
                New String() {"q", "c"}, New String() {"y", "i"}, New String() {"w", "u"}, New String() {"j", "g"},
                New String() {"x", "cs"}, New String() {"h", ""}}
                chiave = chiave.Replace(coppia(0), coppia(1))
            Next
            Return System.Text.RegularExpressions.Regex.Replace(chiave, "(.)\1+", "$1")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaveFoneticaDestinatario: " & ex.Message)
            Return If(testo, "").ToLowerInvariant()
        End Try
    End Function

    Private Function SomiglianzaFonetica(primo As String, secondo As String) As Double
        Try
            Dim a As String = ChiaveFoneticaDestinatario(primo)
            Dim b As String = ChiaveFoneticaDestinatario(secondo)
            If a.Length = 0 OrElse b.Length = 0 Then Return 0.0
            Dim precedente(b.Length) As Integer
            Dim corrente(b.Length) As Integer
            For j As Integer = 0 To b.Length
                precedente(j) = j
            Next
            For i As Integer = 1 To a.Length
                corrente(0) = i
                For j As Integer = 1 To b.Length
                    Dim costo As Integer = If(a(i - 1) = b(j - 1), 0, 1)
                    corrente(j) = Math.Min(Math.Min(corrente(j - 1) + 1, precedente(j) + 1), precedente(j - 1) + costo)
                Next
                Dim scambio As Integer() = precedente
                precedente = corrente
                corrente = scambio
            Next
            Return 1.0 - precedente(b.Length) / CDbl(Math.Max(a.Length, b.Length))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SomiglianzaFonetica: " & ex.Message)
            Return 0.0
        End Try
    End Function

    Private Function ParteLocaleIndirizzo(email As String) As String
        Try
            Dim valore As String = If(email, "").Trim()
            Dim chiocciola As Integer = valore.IndexOf("@"c)
            Return If(chiocciola > 0, valore.Substring(0, chiocciola), valore)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ParteLocaleIndirizzo: " & ex.Message)
            Return If(email, "")
        End Try
    End Function

    Private Function TuttiIDestinatariConosciuti() As List(Of DestinatarioVocale)
        Dim tutti As New List(Of DestinatarioVocale)
        Try
            Dim gia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            CercaDestinatariVocali("", tutti, gia)

            Dim recenti As DataTable = Nothing
            Try
                recenti = _posta.OttieniDestinatariRecenti("", MASSIMO_RECENTI_FONETICI)
            Catch exRecenti As Exception
                log.ScriviLog("MenuPosta.TuttiIDestinatariConosciuti (recenti): " & exRecenti.Message)
            End Try
            If recenti IsNot Nothing Then
                For Each riga As DataRow In recenti.Rows
                    Try
                        Dim email As String = Convert.ToString(riga("Email")).Trim()
                        If String.IsNullOrWhiteSpace(email) OrElse Not gia.Add(email) Then Continue For
                        tutti.Add(New DestinatarioVocale With {.Nome = Convert.ToString(riga("NomeVisualizzato")).Trim(), .Email = email})
                    Catch exRiga As Exception
                        log.ScriviLog("MenuPosta.TuttiIDestinatariConosciuti (recente): " & exRiga.Message)
                    End Try
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TuttiIDestinatariConosciuti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return tutti
    End Function

    Private Sub CercaDestinatariFonetici(testo As String, risultato As List(Of DestinatarioVocale))
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse ChiaveFoneticaDestinatario(testo).Length < 3 Then Return
            Dim valutati As New List(Of Tuple(Of Double, DestinatarioVocale))
            For Each candidato As DestinatarioVocale In TuttiIDestinatariConosciuti()
                Dim locale As String = ParteLocaleIndirizzo(candidato.Email)
                Dim punteggio As Double = Math.Max(SomiglianzaFonetica(testo, candidato.Nome), Math.Max(SomiglianzaFonetica(testo, locale), SomiglianzaFonetica(testo, System.Text.RegularExpressions.Regex.Replace(locale, "[0-9]", ""))))
                If punteggio >= SOMIGLIANZA_MINIMA_DESTINATARIO Then valutati.Add(New Tuple(Of Double, DestinatarioVocale)(punteggio, candidato))
            Next
            valutati.Sort(Function(a As Tuple(Of Double, DestinatarioVocale), b As Tuple(Of Double, DestinatarioVocale)) b.Item1.CompareTo(a.Item1))
            For indice As Integer = 0 To Math.Min(valutati.Count, MASSIMO_DESTINATARI_FONETICI) - 1
                Dim punteggio As Double = valutati(indice).Item1
                Dim candidato As DestinatarioVocale = valutati(indice).Item2
                candidato.Punteggio = If(punteggio >= 0.92, 3, If(punteggio >= 0.85, 2, 1))
                risultato.Add(candidato)
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CercaDestinatariFonetici: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function IndirizzoMinuscolo(indirizzo As String) As String
        Try
            Dim valore As String = If(indirizzo, "").Trim()
            If Not valore.Contains("@") Then Return valore
            Return valore.Replace(" ", "").ToLowerInvariant()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.IndirizzoMinuscolo: " & ex.Message)
            Return If(indirizzo, "")
        End Try
    End Function

    Private Sub CercaDestinatariVocali(testo As String, destinazione As List(Of DestinatarioVocale), gia As HashSet(Of String))
        Try
            Dim rubrica As CLSContatti = Me.Rubrica
            If rubrica IsNot Nothing Then
                AggiungiDestinatariDaTabellaContatti(SicuroCercaConEmail(rubrica, testo, ""), destinazione, gia)

                Dim rubriche As DataTable = Nothing
                Try
                    rubriche = rubrica.OttieniRubriche()
                Catch exRubriche As Exception
                    log.ScriviLog("MenuPosta.CercaDestinatariVocali (rubriche): " & exRubriche.Message)
                End Try

                If rubriche IsNot Nothing Then
                    For Each riga As DataRow In rubriche.Rows
                        Try
                            Dim chiaveRubrica As String = Convert.ToString(riga("ChiaveWG"))
                            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Continue For
                            AggiungiDestinatariDaTabellaContatti(SicuroCercaConEmail(rubrica, testo, chiaveRubrica), destinazione, gia)
                        Catch exRiga As Exception
                            log.ScriviLog("MenuPosta.CercaDestinatariVocali (rubrica): " & exRiga.Message)
                        End Try
                    Next
                End If
            End If

            Dim recenti As DataTable = Nothing
            Try
                recenti = _posta.OttieniDestinatariRecenti(testo, 15)
            Catch exRecenti As Exception
                log.ScriviLog("MenuPosta.CercaDestinatariVocali (recenti): " & exRecenti.Message)
            End Try

            If recenti IsNot Nothing Then
                For Each riga As DataRow In recenti.Rows
                    Try
                        Dim email As String = Convert.ToString(riga("Email")).Trim()
                        If String.IsNullOrWhiteSpace(email) OrElse Not gia.Add(email) Then Continue For
                        Dim nomeVisualizzato As String = Convert.ToString(riga("NomeVisualizzato")).Trim()
                        destinazione.Add(New DestinatarioVocale With {.Nome = nomeVisualizzato, .Email = email})
                    Catch exRiga As Exception
                        log.ScriviLog("MenuPosta.CercaDestinatariVocali (recente): " & exRiga.Message)
                    End Try
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CercaDestinatariVocali: " & ex.Message)
        End Try
    End Sub

    Private Function SicuroCercaConEmail(rubrica As CLSContatti, testo As String, chiaveRubrica As String) As DataTable
        Try
            Return rubrica.CercaConEmail(testo, "", chiaveRubrica)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SicuroCercaConEmail: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub AggiungiDestinatariDaTabellaContatti(tabella As DataTable, destinazione As List(Of DestinatarioVocale), gia As HashSet(Of String))
        Try
            If tabella Is Nothing Then Return
            For Each riga As DataRow In tabella.Rows
                Try
                    Dim email As String = Convert.ToString(riga("Email")).Trim()
                    If String.IsNullOrWhiteSpace(email) OrElse Not gia.Add(email) Then Continue For
                    Dim nomeContatto As String = (Convert.ToString(riga("Nome")) & " " & Convert.ToString(riga("Cognome"))).Trim()
                    destinazione.Add(New DestinatarioVocale With {.Nome = nomeContatto, .Email = email})
                Catch exRiga As Exception
                    log.ScriviLog("MenuPosta.AggiungiDestinatariDaTabellaContatti (riga): " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiungiDestinatariDaTabellaContatti: " & ex.Message)
        End Try
    End Sub

    Private Function NormalizzaNomeVocale(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim scomposto As String = testo.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim risultato As New StringBuilder(scomposto.Length)
            Dim ultimoSpazio As Boolean = True
            For Each carattere As Char In scomposto
                If Char.GetUnicodeCategory(carattere) = System.Globalization.UnicodeCategory.NonSpacingMark Then Continue For
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
            log.ScriviLog("MenuPosta.NormalizzaNomeVocale: " & ex.Message)
            Return If(testo, "").ToLowerInvariant().Trim()
        End Try
    End Function

    Private Function ContieneTutteLeParole(nome As String, parole As String()) As Boolean
        Try
            Dim normalizzato As String = " " & NormalizzaNomeVocale(nome) & " "
            For Each parola As String In parole
                Dim p As String = NormalizzaNomeVocale(parola)
                If p = "" Then Continue For
                If Not normalizzato.Contains(" " & p) Then Return False
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ContieneTutteLeParole: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PunteggioDestinatario(richiesta As String, nome As String) As Integer
        Try
            Dim r As String = NormalizzaNomeVocale(richiesta)
            Dim n As String = NormalizzaNomeVocale(nome)
            If r = "" OrElse n = "" Then Return 0
            If String.Equals(r, n, StringComparison.Ordinal) Then Return 3

            Dim paroleNome As String() = n.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If paroleNome.Length > 1 Then
                Dim invertito As String = paroleNome(paroleNome.Length - 1) & " " & String.Join(" ", paroleNome, 0, paroleNome.Length - 1)
                If String.Equals(r, invertito, StringComparison.Ordinal) Then Return 3
            End If

            If n.StartsWith(r & " ", StringComparison.Ordinal) Then Return 2
            If ContieneTutteLeParole(nome, r.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)) Then Return 1
            Return 0
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PunteggioDestinatario: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub OrdinaDestinatariVocali(elenco As List(Of DestinatarioVocale))
        Try
            If elenco Is Nothing OrElse elenco.Count < 2 Then Return
            For i As Integer = 1 To elenco.Count - 1
                Dim corrente As DestinatarioVocale = elenco(i)
                Dim j As Integer = i - 1
                While j >= 0 AndAlso elenco(j).Punteggio < corrente.Punteggio
                    elenco(j + 1) = elenco(j)
                    j -= 1
                End While
                elenco(j + 1) = corrente
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OrdinaDestinatariVocali: " & ex.Message)
        End Try
    End Sub

    Private Function SceltaDestinatarioAutomatica(trovati As List(Of DestinatarioVocale)) As DestinatarioVocale
        Try
            If trovati Is Nothing OrElse trovati.Count = 0 Then Return Nothing
            If trovati.Count = 1 Then Return trovati(0)
            If trovati(0).Punteggio >= 2 AndAlso trovati(1).Punteggio < trovati(0).Punteggio Then Return trovati(0)
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SceltaDestinatarioAutomatica: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub MostraSceltaDestinatarioVocale(nome As String, trovati As List(Of DestinatarioVocale))
        Try
            ChiudiListaDestinatariVocali()
            _destinatariVocaliCorrenti = trovati

            _listaDestinatariVocali = New WGMenuLista()
            _listaDestinatariVocali.multiselezioneConsentita = False
            _listaDestinatariVocali.impostaTitolo("Destinatari trovati per " & nome)
            For Each candidato As DestinatarioVocale In trovati
                _listaDestinatariVocali.aggiungiElemento(candidato.Descrizione, candidato.Email)
            Next
            AddHandler _listaDestinatariVocali.ElementoSelezionato, AddressOf DestinatarioVocaleSelezionato
            AddHandler _listaDestinatariVocali.escPremuto, AddressOf ListaDestinatariVocaliEsc
            _listaDestinatariVocali.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaDestinatarioVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaDestinatariVocali()
        Try
            If _listaDestinatariVocali Is Nothing Then Return
            RemoveHandler _listaDestinatariVocali.ElementoSelezionato, AddressOf DestinatarioVocaleSelezionato
            RemoveHandler _listaDestinatariVocali.escPremuto, AddressOf ListaDestinatariVocaliEsc
            _listaDestinatariVocali.abbandona()
            _listaDestinatariVocali = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiListaDestinatariVocali: " & ex.Message)
            _listaDestinatariVocali = Nothing
        End Try
    End Sub

    Private Sub DestinatarioVocaleSelezionato(id As String)
        Try
            ChiudiListaDestinatariVocali()
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim scelto As DestinatarioVocale = Nothing
            If _destinatariVocaliCorrenti IsNot Nothing Then
                For Each candidato As DestinatarioVocale In _destinatariVocaliCorrenti
                    If candidato IsNot Nothing AndAlso String.Equals(candidato.Email, id, StringComparison.OrdinalIgnoreCase) Then
                        scelto = candidato
                        Exit For
                    End If
                Next
            End If
            If scelto Is Nothing Then scelto = New DestinatarioVocale With {.Nome = "", .Email = id}

            ProseguiInvioVocale(scelto)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DestinatarioVocaleSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaDestinatariVocaliEsc(sender As Object, e As EventArgs)
        Try
            ChiudiListaDestinatariVocali()
            _destinatariVocaliCorrenti = New List(Of DestinatarioVocale)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaDestinatariVocaliEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub ProseguiInvioVocale(scelto As DestinatarioVocale)
        Try
            If scelto Is Nothing OrElse String.IsNullOrWhiteSpace(scelto.Email) Then Return

            _composizioneDestinatari = IndirizzoMinuscolo(scelto.Email)
            _descrizioneDestinatarioVocale = scelto.Descrizione

            If _apriPerControlloVocale Then
                _apriPerControlloVocale = False
                ApriComposizioneDettagli()
                Return
            End If

            If String.IsNullOrWhiteSpace(_composizioneCorpo) Then
                ApriComposizioneDettagli()
                Return
            End If

            If _invioVocaleSenzaConferma Then
                AvviaInvioMessaggio()
                Return
            End If

            ChiediConfermaInvioVocale()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ProseguiInvioVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediConfermaInvioVocale()
        Try
            ChiudiDomandaInvioVocale()

            Dim testo As New StringBuilder()
            testo.Append("Invio a ").Append(_descrizioneDestinatarioVocale).Append(".")
            testo.Append(Environment.NewLine)
            If String.IsNullOrWhiteSpace(_composizioneOggetto) Then
                testo.Append("Senza oggetto.")
            Else
                testo.Append("Oggetto: ").Append(_composizioneOggetto).Append(".")
            End If
            testo.Append(Environment.NewLine)
            Dim anteprima As String = _composizioneCorpo
            If anteprima.Length > 400 Then anteprima = anteprima.Substring(0, 400) & "..."
            testo.Append("Testo: ").Append(anteprima)
            testo.Append(Environment.NewLine)
            testo.Append("Confermi l'invio?")

            _domandaInvioVocale = New WGDomanda()
            AddHandler _domandaInvioVocale.rispostadomanda, AddressOf RispostaInvioVocale
            _domandaInvioVocale.avvia(testo.ToString())
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiediConfermaInvioVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiDomandaInvioVocale()
        Try
            If _domandaInvioVocale Is Nothing Then Return
            RemoveHandler _domandaInvioVocale.rispostadomanda, AddressOf RispostaInvioVocale
            _domandaInvioVocale = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiDomandaInvioVocale: " & ex.Message)
            _domandaInvioVocale = Nothing
        End Try
    End Sub

    Private Sub RispostaInvioVocale(risposta As DialogResult)
        Try
            ChiudiDomandaInvioVocale()
            If risposta = DialogResult.Yes Then
                AvviaInvioMessaggio()
            Else
                ApriComposizioneDettagli()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RispostaInvioVocale: " & ex.Message)
        End Try
    End Sub

    Public Sub ComandoLeggiPostaInArrivo(Optional soloNonLetti As Boolean = True, Optional massimo As Integer = 10)
        Try
            _chiuso = False
            _soloNonLettiVocale = soloNonLetti
            _massimoMessaggiVocali = If(massimo < 1, 10, massimo)
            _testoPostaVocale = ""
            MostraAttesa("Controllo della posta in arrivo, attendere")
            AvviaLavoro(New Action(AddressOf LavoroLeggiPostaVocale))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComandoLeggiPostaInArrivo: " & ex.Message)
            ChiudiAttesa()
        End Try
    End Sub

    Private Sub LavoroLeggiPostaVocale()
        Try
            _testoPostaVocale = TestoPostaInArrivo(_soloNonLettiVocale, _massimoMessaggiVocali)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroLeggiPostaVocale: " & ex.Message)
            _testoPostaVocale = ""
        Finally
            EseguiSuInterfaccia(AddressOf CompletaLeggiPostaVocale)
        End Try
    End Sub

    Private Sub CompletaLeggiPostaVocale()
        Try
            Dim annullata As Boolean = OperazioneAnnullata()
            ChiudiAttesa()
            If annullata Then Return
            If String.IsNullOrWhiteSpace(_testoPostaVocale) Then
                MostraMessaggio("Non è stato possibile leggere la posta in arrivo.")
                Return
            End If
            MostraMessaggio(_testoPostaVocale)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaLeggiPostaVocale: " & ex.Message)
        End Try
    End Sub

    Public Function TestoPostaInArrivo(Optional soloNonLetti As Boolean = True, Optional massimo As Integer = 10) As String
        Try
            Dim totale As Integer = 0
            Dim messaggi As List(Of RiepilogoMessaggio) = LeggiPostaInArrivoVocale(soloNonLetti, totale)
            If messaggi Is Nothing Then Return "Non è stato possibile leggere la posta in arrivo."

            If messaggi.Count = 0 Then
                If soloNonLetti Then Return "Non ci sono messaggi da leggere nella posta in arrivo."
                Return "La posta in arrivo è vuota."
            End If

            If massimo < 1 Then massimo = 10
            Dim numero As Integer = Math.Max(totale, messaggi.Count)
            Dim frasi As New List(Of String)

            If soloNonLetti Then
                If numero = 1 Then
                    frasi.Add("Hai un messaggio da leggere.")
                Else
                    frasi.Add("Hai " & numero.ToString() & " messaggi da leggere.")
                End If
            Else
                If numero = 1 Then
                    frasi.Add("Nella posta in arrivo c'è un messaggio.")
                Else
                    frasi.Add("Nella posta in arrivo ci sono " & numero.ToString() & " messaggi. Ecco i più recenti.")
                End If
            End If

            Dim letti As Integer = 0
            For Each r As RiepilogoMessaggio In messaggi
                If letti >= massimo Then Exit For
                frasi.Add(DescriviMessaggioVocale(r, Not soloNonLetti))
                letti += 1
            Next

            Dim restanti As Integer = numero - letti
            If restanti = 1 Then
                frasi.Add("C'è un altro messaggio.")
            ElseIf restanti > 1 Then
                frasi.Add("Ci sono altri " & restanti.ToString() & " messaggi.")
            End If

            Return String.Join(Environment.NewLine, frasi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TestoPostaInArrivo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile leggere la posta in arrivo."
        End Try
    End Function

    Private Function LeggiPostaInArrivoVocale(soloNonLetti As Boolean, ByRef totale As Integer) As List(Of RiepilogoMessaggio)
        totale = 0
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing Then Return Nothing

            Dim lavori As New List(Of LavoroPagina)
            For Each account As ImpostazioniAccountMail In attivi
                If account Is Nothing OrElse String.IsNullOrWhiteSpace(account.Chiave) Then Continue For
                Dim lavoro As New LavoroPagina()
                lavoro.Chiave = account.Chiave
                lavoro.Cartella = PercorsoPostaInArrivo(account.Chiave)
                lavoro.SoloNonLetti = soloNonLetti
                lavoro.Posta = _posta
                lavoro.Registro = log
                lavori.Add(lavoro)
            Next

            Dim risultato As New List(Of RiepilogoMessaggio)
            If lavori.Count = 0 Then Return risultato

            If lavori.Count = 1 Then
                lavori(0).Esegui()
            Else
                Dim compiti(lavori.Count - 1) As Task
                For indice As Integer = 0 To lavori.Count - 1
                    compiti(indice) = Task.Run(New Action(AddressOf lavori(indice).Esegui))
                Next
                Try
                    Task.WaitAll(compiti)
                Catch exAttesa As Exception
                    log.ScriviLog("MenuPosta.LeggiPostaInArrivoVocale attesa: " & exAttesa.Message)
                End Try
            End If

            Dim almenoUnaRiuscita As Boolean = False
            For Each lavoro As LavoroPagina In lavori
                Try
                    Dim pagina As PaginaMessaggi = lavoro.Risultato
                    If pagina Is Nothing Then Continue For
                    almenoUnaRiuscita = True
                    If pagina.Messaggi IsNot Nothing Then
                        For Each r As RiepilogoMessaggio In pagina.Messaggi
                            If r Is Nothing Then Continue For
                            If soloNonLetti AndAlso r.Letto Then Continue For
                            risultato.Add(r)
                        Next
                    End If
                    totale += Math.Max(0, pagina.Totale)
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LeggiPostaInArrivoVocale (" & If(lavoro.Chiave, "") & "): " & exSingolo.Message)
                End Try
            Next

            If Not almenoUnaRiuscita Then Return Nothing
            If totale < risultato.Count Then totale = risultato.Count

            risultato.Sort(AddressOf ConfrontaMessaggiPiuRecentiPrima)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LeggiPostaInArrivoVocale: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function PercorsoPostaInArrivo(chiaveAccount As String) As String
        Try
            Dim percorso As String = _posta.RisolviPercorsoCartellaSpeciale(chiaveAccount, "inbox")
            If String.IsNullOrWhiteSpace(percorso) Then Return "INBOX"
            Return percorso
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PercorsoPostaInArrivo: " & ex.Message)
            Return "INBOX"
        End Try
    End Function

    Private Function ConfrontaMessaggiPiuRecentiPrima(a As RiepilogoMessaggio, b As RiepilogoMessaggio) As Integer
        Try
            If a Is Nothing AndAlso b Is Nothing Then Return 0
            If a Is Nothing Then Return 1
            If b Is Nothing Then Return -1
            Return b.Data.CompareTo(a.Data)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfrontaMessaggiPiuRecentiPrima: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function DescriviMessaggioVocale(r As RiepilogoMessaggio, indicaStato As Boolean) As String
        Try
            If r Is Nothing Then Return ""
            Dim parti As New StringBuilder()
            If indicaStato AndAlso Not r.Letto Then parti.Append("Non letto. ")
            parti.Append("Da ").Append(NomeMittenteVocale(r.Mittente))
            Dim quando As String = QuandoVocale(r.Data)
            If Not String.IsNullOrWhiteSpace(quando) Then parti.Append(", ").Append(quando)
            parti.Append(". ")
            If String.IsNullOrWhiteSpace(r.Oggetto) Then
                parti.Append("Senza oggetto.")
            Else
                Dim oggetto As String = r.Oggetto.Trim()
                parti.Append("Oggetto: ").Append(oggetto)
                If Not oggetto.EndsWith(".", StringComparison.Ordinal) AndAlso Not oggetto.EndsWith("?", StringComparison.Ordinal) AndAlso Not oggetto.EndsWith("!", StringComparison.Ordinal) Then parti.Append(".")
            End If
            Return parti.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviMessaggioVocale: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeMittenteVocale(mittente As String) As String
        Try
            Dim valore As String = If(mittente, "").Trim()
            If valore = "" Then Return "mittente sconosciuto"
            Dim apertura As Integer = valore.IndexOf("<"c)
            If apertura > 0 Then
                Dim nome As String = valore.Substring(0, apertura).Trim().Trim(Chr(34), "'"c).Trim()
                If nome <> "" Then Return nome
            End If
            Dim indirizzo As String = SoloIndirizzo(valore)
            If indirizzo <> "" Then Return indirizzo
            Return valore.Trim(Chr(34))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.NomeMittenteVocale: " & ex.Message)
            Return If(mittente, "")
        End Try
    End Function

    Private Function QuandoVocale(data As DateTimeOffset) As String
        Try
            If data = DateTimeOffset.MinValue Then Return ""
            Dim cultura As System.Globalization.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("it-IT")
            Dim locale As DateTime = data.LocalDateTime
            Dim ora As String = locale.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            Dim giorni As Integer = CInt((DateTime.Today - locale.Date).TotalDays)
            If giorni = 0 Then Return "oggi alle " & ora
            If giorni = 1 Then Return "ieri alle " & ora
            If giorni > 1 AndAlso giorni < 7 Then Return locale.ToString("dddd", cultura) & " alle " & ora
            If locale.Year = DateTime.Today.Year Then Return "il " & locale.ToString("d MMMM", cultura)
            Return "il " & locale.ToString("d MMMM yyyy", cultura)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.QuandoVocale: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub ComandoLeggiUltimoMessaggio(Optional soloNonLetti As Boolean = True)
        Try
            _chiuso = False
            _soloNonLettiVocale = soloNonLetti
            _nessunMessaggioVocale = False
            _messaggioCorrente = Nothing
            _mostraHtmlOriginale = False
            _tipoCartellaSpeciale = "inbox"
            _cartellaCorrente = "INBOX"
            AzzeraForzaturaLettura()
            MostraAttesa("Apertura dell'ultimo messaggio, attendere")
            AvviaLavoro(New Action(AddressOf LavoroLeggiUltimoMessaggioVocale))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ComandoLeggiUltimoMessaggio: " & ex.Message)
            ChiudiAttesa()
        End Try
    End Sub

    Private Sub LavoroLeggiUltimoMessaggioVocale()
        Try
            Dim totale As Integer = 0
            Dim messaggi As List(Of RiepilogoMessaggio) = LeggiPostaInArrivoVocale(_soloNonLettiVocale, totale)
            If messaggi Is Nothing OrElse messaggi.Count = 0 Then
                _nessunMessaggioVocale = (messaggi IsNot Nothing)
                _messaggioCorrente = Nothing
                Return
            End If

            Dim piuRecente As RiepilogoMessaggio = messaggi(0)
            _chiaveAccountCorrente = piuRecente.ChiaveAccount
            Dim percorso As String = PercorsoPostaInArrivo(piuRecente.ChiaveAccount)
            _messaggioCorrente = _posta.ApriMessaggio(piuRecente.ChiaveAccount, percorso, piuRecente.Id)

            If _messaggioCorrente IsNot Nothing AndAlso _messaggioCorrente.Letto AndAlso Not piuRecente.Letto Then
                Try
                    _posta.SegnaLettoNellaCacheElenco(piuRecente.ChiaveAccount, percorso, piuRecente.Id, True)
                Catch exCache As Exception
                    log.ScriviLog("MenuPosta.LavoroLeggiUltimoMessaggioVocale (cache): " & exCache.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroLeggiUltimoMessaggioVocale: " & ex.Message)
            _messaggioCorrente = Nothing
        Finally
            EseguiSuInterfaccia(AddressOf CompletaLeggiUltimoMessaggioVocale)
        End Try
    End Sub

    Private Sub CompletaLeggiUltimoMessaggioVocale()
        Try
            If OperazioneAnnullata() Then
                ChiudiAttesa()
                Return
            End If

            If _nessunMessaggioVocale Then
                ChiudiAttesa()
                MostraMessaggio(If(_soloNonLettiVocale, "Non ci sono messaggi da leggere nella posta in arrivo.", "La posta in arrivo è vuota."))
                Return
            End If

            CompletaAperturaMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaLeggiUltimoMessaggioVocale: " & ex.Message)
        End Try
    End Sub

#End Region

End Class