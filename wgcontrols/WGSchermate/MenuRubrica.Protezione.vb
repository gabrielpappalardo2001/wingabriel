Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports WGContatti
Imports WGMail
Imports wgcontrols

' Gestione delle rubriche e della loro sicurezza. Permette l'accesso tramite password, il collegamento di account esterni e
' l'importazione o esportazione di contatti vCard.
Partial Public Class MenuRubrica

    Private Enum ScopoPassword
        Apertura = 0
        Impostazione = 1
        Rimozione = 2
        Cambio = 3
    End Enum

    Private _campiPassword As Campi
    Private _scopoPassword As ScopoPassword = ScopoPassword.Apertura
    Private _rubricaInAttesaDiPassword As String = ""
    Private _sceltaFileVCard As WGFileDialog
    Private _sceltaCartellaEsportazione As WGFileDialog
    Private _nomeFileEsportazione As String = ""
    Private _campiEsportazione As Campi
    Private _menuRubricaNuova As WGMenu
    Private _listaAccountCollegamento As WGMenuLista

    ' ============================================================================================
    ' ELENCO DELLE RUBRICHE
    ' ============================================================================================

    ' Descrive una rubrica nell'elenco: nome, account di appartenenza oppure la parola locale,
    ' e lo stato della protezione.
    Private Function DescriviRubrica(riga As DataRow) As String
        Try
            Dim parti As New List(Of String)
            parti.Add(riga("Nome").ToString())

            Dim account As String = ""
            Try
                If Not riga.IsNull("ChiaveAccount") Then account = riga("ChiaveAccount").ToString().Trim()
            Catch exAccount As Exception
                log.ScriviLog("MenuRubrica.DescriviRubrica account: " & exAccount.Message)
            End Try

            If account.Length = 0 Then
                parti.Add("locale")
            Else
                parti.Add(NomeAccountLeggibile(account))
            End If

            Try
                If Convert.ToInt32(riga("Predefinita")) = 1 Then parti.Add("predefinita")
            Catch exPredefinita As Exception
                log.ScriviLog("MenuRubrica.DescriviRubrica predefinita: " & exPredefinita.Message)
            End Try

            Dim chiave As String = riga("ChiaveWG").ToString()
            If _contatti.RubricaProtetta(chiave) Then
                parti.Add(If(_contatti.RubricaSbloccata(chiave), "protetta, aperta", "protetta, chiusa"))
            End If
            If String.Equals(chiave, _rubricaPredefinitaCorrente, StringComparison.Ordinal) Then
                parti.Add("predefinita per le operazioni")
            End If

            Return String.Join(", ", parti.ToArray())
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.DescriviRubrica: " & ex.Message)
            Return "Rubrica"
        End Try
    End Function

    Private Function NomeAccountLeggibile(chiaveAccount As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return "locale"
            Dim impostazioni As ImpostazioniAccountMail = _posta.OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return "account non più presente"
            If Not String.IsNullOrWhiteSpace(impostazioni.NomeVisualizzato) Then Return impostazioni.NomeVisualizzato
            Return If(impostazioni.IndirizzoEmail, chiaveAccount)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.NomeAccountLeggibile: " & ex.Message)
            Return chiaveAccount
        End Try
    End Function

    ' ============================================================================================
    ' APERTURA CON PASSWORD
    ' ============================================================================================

    Private Sub ChiediPassword(chiaveRubrica As String, scopo As ScopoPassword, titolo As String)
        Try
            MostraAttesa("Caricamento della rubrica in corso, attendere")
            _rubricaInAttesaDiPassword = If(chiaveRubrica, "")
            _scopoPassword = scopo
            _campiPassword = New Campi()
            _campiPassword.SalvaSuEscape = False
            _campiPassword.AggiungiPassword(titolo, "rub.password", True, False, "")
            AddHandler _campiPassword.Fine, AddressOf CampiPasswordConfermati
            AddHandler _campiPassword.Chiusura, AddressOf CampiPasswordAnnullati
            _campiPassword.Avvia()
            ChiudiAttesa()
        Catch ex As Exception
            ChiudiAttesa()
            log.ScriviLog("MenuRubrica.ChiediPassword: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiPassword()
        Try
            If _campiPassword Is Nothing Then Return
            RemoveHandler _campiPassword.Fine, AddressOf CampiPasswordConfermati
            RemoveHandler _campiPassword.Chiusura, AddressOf CampiPasswordAnnullati
            _campiPassword = Nothing
        Catch ex As Exception
            log.ScriviLog(ex.Message & ex.StackTrace)
            _campiPassword = Nothing
        End Try
    End Sub

    Private Sub CampiPasswordAnnullati(sender As Object, e As EventArgs)
        Try
            LiberaCampiPassword()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiPasswordAnnullati: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiPasswordConfermati(sender As Object, e As EventArgs)
        Try
            Dim password As String = ""
            If _campiPassword IsNot Nothing Then password = _campiPassword.RecuperaValore("rub.password")
            LiberaCampiPassword()

            If String.IsNullOrEmpty(password) Then
                MostraMessaggio("Password non indicata.")
                Return
            End If

            MostraAttesa("Caricamento della rubrica in corso, attendere")
            Try
                Select Case _scopoPassword
                    Case ScopoPassword.Apertura
                        ApriConPassword(password)
                    Case ScopoPassword.Impostazione
                        If _contatti.ImpostaPasswordRubrica(_rubricaInAttesaDiPassword, password) Then
                            MostraMessaggio("Rubrica protetta. I contatti sono stati cifrati e resteranno leggibili finché la rubrica è aperta.")
                        Else
                            MostraMessaggio(MessaggioErroreContatti("Non è stato possibile proteggere la rubrica."))
                        End If
                    Case ScopoPassword.Rimozione
                        If Not _contatti.SbloccaRubrica(_rubricaInAttesaDiPassword, password) Then
                            MostraMessaggio("Password errata.")
                        ElseIf _contatti.TogliPasswordRubrica(_rubricaInAttesaDiPassword) Then
                            MostraMessaggio("Password tolta. I contatti sono di nuovo in chiaro.")
                        Else
                            MostraMessaggio(MessaggioErroreContatti("Non è stato possibile togliere la password."))
                        End If
                    Case ScopoPassword.Cambio
                        If _contatti.CambiaPasswordRubrica(_rubricaInAttesaDiPassword, password) Then
                            MostraMessaggio("Password cambiata.")
                        Else
                            MostraMessaggio(MessaggioErroreContatti("Non è stato possibile cambiare la password."))
                        End If
                End Select
            Finally
                ChiudiAttesa()
            End Try
        Catch ex As Exception
            ChiudiAttesa()
            log.ScriviLog("MenuRubrica.CampiPasswordConfermati: " & ex.Message)
        End Try
    End Sub

    ' La password digitata viene provata su tutte le rubriche protette. Se appartiene a una
    ' rubrica diversa da quella scelta, si apre quella: la password identifica la rubrica.
    Private Sub ApriConPassword(password As String)
        Try
            Dim trovata As String = _contatti.TrovaRubricaPerPassword(password)
            If String.IsNullOrWhiteSpace(trovata) Then
                MostraMessaggio("Password errata.")
                Return
            End If

            If Not String.Equals(trovata, _rubricaInAttesaDiPassword, StringComparison.Ordinal) Then
                MostraMessaggio("Questa password apre la rubrica " & _contatti.NomeRubrica(trovata) & ".")
            End If

            RubricaSbloccataERisolta(trovata)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ApriConPassword: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' RUBRICA DI LAVORO PREDEFINITA (usata da inserimento, elenco, ricerca, categorie, modifica)
    ' ============================================================================================
    ' A differenza di ImpostaRubricaPredefinita (che sceglie, per account, la rubrica dove
    ' arrivano i contatti sincronizzati), questa e' la rubrica su cui l'utente sta lavorando in
    ' questa e nelle prossime sessioni: viene ricordata in configurazione, e se protetta va
    ' sbloccata con la password prima di poter operare.

    ' Punto di ingresso comune per inserimento, elenco, ricerca e consultazione delle categorie:
    ' se non c'e' ancora una rubrica di lavoro la si chiede, se e' protetta e chiusa si chiede la
    ' password, altrimenti l'operazione richiesta parte subito.
    Private Sub UsaRubricaEPoi(azione As Action)
        Try
            If String.IsNullOrWhiteSpace(_rubricaPredefinitaCorrente) Then
                _operazionePendenteRubrica = azione
                MostraElencoRubriche(_chiaveAccountRubricaCorrente)
                Return
            End If
            If _contatti.RubricaProtetta(_rubricaPredefinitaCorrente) AndAlso Not _contatti.RubricaSbloccata(_rubricaPredefinitaCorrente) Then
                _operazionePendenteRubrica = azione
                ChiediPassword(_rubricaPredefinitaCorrente, ScopoPassword.Apertura,
                    "Password della rubrica " & _contatti.NomeRubrica(_rubricaPredefinitaCorrente))
                Return
            End If
            _filtroRubrica = _rubricaPredefinitaCorrente
            azione()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.UsaRubricaEPoi: " & ex.Message)
        End Try
    End Sub

    ' Una rubrica e' stata scelta ed e' sbloccata (o non protetta): diventa la predefinita, e se
    ' c'era un'operazione in attesa la esegue adesso; altrimenti avvisa soltanto che e' stata
    ' impostata, senza mostrarne i contatti.
    Private Sub RubricaSbloccataERisolta(chiave As String)
        Try
            ImpostaRubricaComePredefinitaCorrente(chiave)
            Dim azione As Action = _operazionePendenteRubrica
            _operazionePendenteRubrica = Nothing
            If azione IsNot Nothing Then
                _filtroRubrica = chiave
                azione()
            Else
                MostraMessaggio("Rubrica """ & _contatti.NomeRubrica(chiave) & """ impostata come predefinita per le operazioni.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RubricaSbloccataERisolta: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaRubricaComePredefinitaCorrente(chiave As String)
        Try
            _rubricaPredefinitaCorrente = If(chiave, "")
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore("RubricaCorrentePredefinita", _rubricaPredefinitaCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ImpostaRubricaComePredefinitaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub CaricaRubricaPredefinitaCorrente()
        Try
            _rubricaPredefinitaCorrente = ""
            Dim configurazione As New WGConfig()
            Dim chiave As String = configurazione.LeggiValore("RubricaCorrentePredefinita")
            If String.IsNullOrWhiteSpace(chiave) Then Return
            If String.IsNullOrWhiteSpace(_contatti.NomeRubrica(chiave)) Then Return
            _rubricaPredefinitaCorrente = chiave
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CaricaRubricaPredefinitaCorrente: " & ex.Message)
            _rubricaPredefinitaCorrente = ""
        End Try
    End Sub

    Private Function MessaggioErroreContatti(predefinito As String) As String
        Try
            Dim descrizione As String = _contatti.UltimoErrore
            If String.IsNullOrWhiteSpace(descrizione) Then Return predefinito
            Return descrizione
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MessaggioErroreContatti: " & ex.Message)
            Return predefinito
        End Try
    End Function

    ' ============================================================================================
    ' OPZIONI DI PROTEZIONE
    ' ============================================================================================

    Private Sub OpzionePasswordRubrica(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Scegli prima una rubrica.")
                Return
            End If

            If Not _contatti.RubricaProtetta(chiave) Then
                ChiediPassword(chiave, ScopoPassword.Impostazione, "Password con cui proteggere la rubrica")
                Return
            End If

            _rubricaInAttesaDiPassword = chiave
            _menuRubricaNuova = New WGMenu()
            _menuRubricaNuova.aggiungi("Cambia la password", AddressOf VoceCambiaPassword)
            _menuRubricaNuova.aggiungi("Togli la password", AddressOf VoceTogliPassword)
            _menuRubricaNuova.avvia("Protezione della rubrica", true)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzionePasswordRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCambiaPassword(sender As Object, e As EventArgs)
        Try
            If Not _contatti.RubricaSbloccata(_rubricaInAttesaDiPassword) Then
                MostraMessaggio("Apri prima la rubrica con la password attuale.")
                Return
            End If
            ChiediPassword(_rubricaInAttesaDiPassword, ScopoPassword.Cambio, "Nuova password della rubrica")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCambiaPassword: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceTogliPassword(sender As Object, e As EventArgs)
        Try
            ChiediPassword(_rubricaInAttesaDiPassword, ScopoPassword.Rimozione, "Password attuale della rubrica")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceTogliPassword: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' RUBRICHE LOCALI E COLLEGAMENTO A UN ACCOUNT
    ' ============================================================================================

    Private Sub OpzioneNuovaRubricaLocale(sender As Object, e As EventArgs)
        Try
            _chiaveAccountRubricaCorrente = ""
            VoceNuovaRubricaDaLista(sender, e)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneNuovaRubricaLocale: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCollegaRubrica(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Scegli prima una rubrica.")
                Return
            End If

            Dim account As List(Of ImpostazioniAccountMail) = _posta.OttieniAccountConRubrica(True)
            If account Is Nothing OrElse account.Count = 0 Then
                MostraMessaggio("Nessun account con la sincronizzazione dei contatti.")
                Return
            End If
            _rubricaInAttesaDiPassword = chiave
            _listaAccountCollegamento = New WGMenuLista()
            _listaAccountCollegamento.multiselezioneConsentita = False
            _listaAccountCollegamento.impostaTitolo("Account a cui collegare la rubrica")
            _listaAccountCollegamento.aggiungiElemento("Nessun account, rubrica locale", "__locale__")
            For Each impostazioni As ImpostazioniAccountMail In account
                Dim etichetta As String = impostazioni.NomeVisualizzato
                If String.IsNullOrWhiteSpace(etichetta) Then etichetta = impostazioni.IndirizzoEmail
                _listaAccountCollegamento.aggiungiElemento(etichetta, impostazioni.Chiave)
            Next
            AddHandler _listaAccountCollegamento.ElementoSelezionato, AddressOf AccountCollegamentoScelto
            AddHandler _listaAccountCollegamento.escPremuto, AddressOf ListaCollegamentoAbbandonata
            _listaAccountCollegamento.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneCollegaRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaCollegamentoAbbandonata(sender As Object, e As EventArgs)
        Try
            If _listaAccountCollegamento IsNot Nothing Then _listaAccountCollegamento.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaCollegamentoAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub AccountCollegamentoScelto(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaAccountCollegamento, idEvento)
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim account As String = id
            If String.Equals(id, "__locale__", StringComparison.Ordinal) Then account = ""
            If _contatti.CollegaRubricaAdAccount(_rubricaInAttesaDiPassword, account) Then
                MostraMessaggio(If(account.Length = 0, "Rubrica resa locale.", "Rubrica collegata all'account."))
            Else
                MostraMessaggio(MessaggioErroreContatti("Non è stato possibile collegare la rubrica."))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AccountCollegamentoScelto: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' IMPORTAZIONE ED ESPORTAZIONE
    ' ============================================================================================

    Private Sub OpzioneImportaVCard(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Scegli prima la rubrica in cui importare i contatti.")
                Return
            End If
            If _contatti.RubricaProtetta(chiave) AndAlso Not _contatti.RubricaSbloccata(chiave) Then
                MostraMessaggio("Apri prima la rubrica con la password.")
                Return
            End If

            _rubricaInAttesaDiPassword = chiave
            _sceltaFileVCard = New WGFileDialog()
            _sceltaFileVCard.AggiungiEstensione("vcf")
            _sceltaFileVCard.MultiselezioneConsentita = False
            AddHandler _sceltaFileVCard.selezioneterminata, AddressOf FileVCardScelto
            _sceltaFileVCard.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneImportaVCard: " & ex.Message)
        End Try
    End Sub

    Private Sub FileVCardScelto(elementi As List(Of String))
        Try
            Try
                If _sceltaFileVCard IsNot Nothing Then RemoveHandler _sceltaFileVCard.selezioneterminata, AddressOf FileVCardScelto
            Catch exHandler As Exception
                log.ScriviLog("MenuRubrica.FileVCardScelto handler: " & exHandler.Message)
            End Try
            _sceltaFileVCard = Nothing

            If elementi Is Nothing OrElse elementi.Count = 0 Then
                Return
            End If

            Dim importati As Integer = _contatti.ImportaVCard(elementi(0), _rubricaInAttesaDiPassword)
            If importati > 0 Then
                MostraMessaggio(importati.ToString() & If(importati = 1, " contatto importato.", " contatti importati."))
            Else
                MostraMessaggio(MessaggioErroreContatti("Nessun contatto importato."))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.FileVCardScelto: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEsportaVCard(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Scegli prima la rubrica da esportare.")
                Return
            End If
            If _contatti.RubricaProtetta(chiave) AndAlso Not _contatti.RubricaSbloccata(chiave) Then
                MostraMessaggio("Apri prima la rubrica con la password.")
                Return
            End If
            _rubricaInAttesaDiPassword = chiave
            Dim proposto As String = NomeFileProposto(_contatti.NomeRubrica(chiave))
            _campiEsportazione = New Campi()
            _campiEsportazione.SalvaSuEscape = False
            _campiEsportazione.Aggiungi("Nome del file, verrà salvato nella cartella che sceglierai", "rub.file", True, False, proposto)
            AddHandler _campiEsportazione.Fine, AddressOf CampiEsportazioneConfermati
            AddHandler _campiEsportazione.Chiusura, AddressOf CampiEsportazioneAnnullati
            _campiEsportazione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneEsportaVCard: " & ex.Message)
        End Try
    End Sub

    Private Function NomeFileProposto(nomeRubrica As String) As String
        Try
            Dim nome As String = If(nomeRubrica, "rubrica").Trim()
            For Each carattere As Char In Path.GetInvalidFileNameChars()
                nome = nome.Replace(carattere, "_"c)
            Next
            If nome.Length = 0 Then nome = "rubrica"
            Return nome
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.NomeFileProposto: " & ex.Message)
            Return "rubrica"
        End Try
    End Function

    Private Sub LiberaCampiEsportazione()
        Try
            If _campiEsportazione Is Nothing Then Return
            RemoveHandler _campiEsportazione.Fine, AddressOf CampiEsportazioneConfermati
            RemoveHandler _campiEsportazione.Chiusura, AddressOf CampiEsportazioneAnnullati
            _campiEsportazione = Nothing
        Catch ex As Exception
            log.ScriviLog(ex.Message & ex.StackTrace)
            _campiEsportazione = Nothing
        End Try
    End Sub

    Private Sub CampiEsportazioneAnnullati(sender As Object, e As EventArgs)
        Try
            LiberaCampiEsportazione()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiEsportazioneAnnullati: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiEsportazioneConfermati(sender As Object, e As EventArgs)
        Try
            Dim nomeFile As String = ""
            If _campiEsportazione IsNot Nothing Then nomeFile = _campiEsportazione.RecuperaValore("rub.file")
            LiberaCampiEsportazione()

            nomeFile = If(nomeFile, "").Trim()
            If nomeFile.Length = 0 Then
                MostraMessaggio("Nome del file non indicato.")
                Return
            End If
            If Not nomeFile.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) Then nomeFile &= ".vcf"

            _nomeFileEsportazione = nomeFile
            _sceltaCartellaEsportazione = New WGFileDialog()
            _sceltaCartellaEsportazione.MultiselezioneConsentita = False
            AddHandler _sceltaCartellaEsportazione.selezioneterminata, AddressOf CartellaEsportazioneScelta
            _sceltaCartellaEsportazione.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiEsportazioneConfermati: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaSceltaCartellaEsportazione()
        Try
            If _sceltaCartellaEsportazione Is Nothing Then Return
            RemoveHandler _sceltaCartellaEsportazione.selezioneterminata, AddressOf CartellaEsportazioneScelta
            _sceltaCartellaEsportazione = Nothing
        Catch ex As Exception
            log.ScriviLog(ex.Message & ex.StackTrace)
            _sceltaCartellaEsportazione = Nothing
        End Try
    End Sub

    Private Sub CartellaEsportazioneScelta(elementi As List(Of String))
        Try
            LiberaSceltaCartellaEsportazione()

            If elementi Is Nothing OrElse elementi.Count = 0 Then
                _nomeFileEsportazione = ""
                Return
            End If

            Dim percorso As String = Path.Combine(elementi(0), _nomeFileEsportazione)
            _nomeFileEsportazione = ""
            Dim esportati As Integer = _contatti.EsportaVCard(percorso, _rubricaInAttesaDiPassword)
            If esportati > 0 Then
                MostraMessaggio(esportati.ToString() & If(esportati = 1, " contatto esportato in ", " contatti esportati in ") & percorso)
            Else
                MostraMessaggio(MessaggioErroreContatti("Nessun contatto esportato."))
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CartellaEsportazioneScelta: " & ex.Message)
        End Try
    End Sub

End Class