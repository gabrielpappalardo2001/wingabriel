Imports System
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports wgcontrols

Partial Public Class MenuPosta

    Private _postaAvanzata As PostaAvanzata
    Private _menuFirme As WGMenu
    Private _listaFirme As WGMenuLista
    Private _campiFirma As Campi
    Private _firmaInModifica As FirmaMail
    Private _menuRegole As WGMenu
    Private _listaRegole As WGMenuLista
    Private _campiRegola As Campi
    Private _regolaInModifica As RegolaMail
    Private _menuSceltaRegola As WGMenu

    Private Function AvanzataMP() As PostaAvanzata
        Try
            If _postaAvanzata Is Nothing Then _postaAvanzata = New PostaAvanzata()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvanzataMP: " & ex.Message)
        End Try
        Return _postaAvanzata
    End Function

    Private Sub VoceFirme(sender As Object, e As EventArgs)
        Try
            MostraElencoFirme()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceFirme: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRegole(sender As Object, e As EventArgs)
        Try
            MostraElencoRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.VoceRegole: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoFirme()
        Try
            Dim giaAperta As Boolean = _listaFirme IsNot Nothing AndAlso Not _listaFirme.IsDisposed
            If Not giaAperta Then
                _listaFirme = New WGMenuLista()
                _listaFirme.multiselezioneConsentita = False
                _listaFirme.impostaTitolo("Firme")
            Else
                _listaFirme.pulisci()
                _listaFirme.impostaTitolo("Firme")
            End If
            Dim firme As List(Of FirmaMail) = AvanzataMP().OttieniFirme()
            If firme Is Nothing OrElse firme.Count = 0 Then
                _listaFirme.aggiungiElemento("Nessuna firma")
            Else
                For Each f As FirmaMail In firme
                    _listaFirme.aggiungiElemento(DescriviFirma(f), f.Chiave)
                Next
            End If
            If Not giaAperta Then
                _listaFirme.aggiungiOpzione("Nuova firma", AddressOf OpzioneNuovaFirma)
                _listaFirme.aggiungiOpzione("Modifica", AddressOf OpzioneModificaFirma)
                _listaFirme.aggiungiOpzione("Imposta come automatica", AddressOf OpzioneFirmaAutomatica)
                _listaFirme.aggiungiOpzione("Elimina", AddressOf OpzioneEliminaFirma)
                AddHandler _listaFirme.escPremuto, AddressOf ListaFirmeEsc
                _listaFirme.avvia()
            End If
            log.ScriviLog("MenuPosta.MostraElencoFirme")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraElencoFirme: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviFirma(f As FirmaMail) As String
        Try
            If f Is Nothing Then Return ""
            Dim nome As String = If(String.IsNullOrWhiteSpace(f.Nome), "Firma senza nome", f.Nome)
            If f.Predefinita Then Return nome & " (automatica)"
            Return nome
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviFirma: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ListaFirmeEsc(sender As Object, e As EventArgs)
        Try
            If _listaFirme IsNot Nothing Then _listaFirme.abbandona()
            log.ScriviLog("MenuPosta.ListaFirmeEsc")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaFirmeEsc: " & ex.Message)
        End Try
    End Sub

    Private Function ChiaveFirmaSelezionata() As String
        Try
            If _listaFirme Is Nothing Then Return ""
            Dim elementi As List(Of String) = _listaFirme.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Return If(elementi(0), "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaveFirmaSelezionata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub OpzioneNuovaFirma(sender As Object, e As EventArgs)
        Try
            _firmaInModifica = New FirmaMail()
            ApriCampiFirma()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneNuovaFirma: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneModificaFirma(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveFirmaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una firma.")
                Return
            End If
            _firmaInModifica = AvanzataMP().OttieniFirma(chiave)
            If _firmaInModifica Is Nothing Then
                MostraMessaggio("Firma non trovata.")
                Return
            End If
            ApriCampiFirma()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneModificaFirma: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneFirmaAutomatica(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveFirmaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una firma.")
                Return
            End If
            If AvanzataMP().ImpostaFirmaPredefinita(chiave) Then
                MostraMessaggio("Firma automatica impostata.")
            Else
                MostraMessaggio("Impostazione non riuscita.")
            End If
            MostraElencoFirme()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneFirmaAutomatica: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaFirma(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveFirmaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una firma.")
                Return
            End If
            If AvanzataMP().EliminaFirma(chiave) Then
                MostraMessaggio("Firma eliminata.")
            Else
                MostraMessaggio("Eliminazione non riuscita.")
            End If
            MostraElencoFirme()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaFirma: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiFirma()
        Try
            If _firmaInModifica Is Nothing Then _firmaInModifica = New FirmaMail()
            _campiFirma = New Campi()
            _campiFirma.SalvaSuEscape = False
            _campiFirma.Aggiungi("Nome firma", "firma.nome", True, False, If(_firmaInModifica.Nome, ""))
            _campiFirma.AggiungiMultiriga("Testo della firma", "firma.testo", False, False, If(_firmaInModifica.Testo, ""))
            AddHandler _campiFirma.Fine, AddressOf CampiFirmaConfermato
            AddHandler _campiFirma.Chiusura, AddressOf CampiFirmaChiusura
            _campiFirma.Avvia()
            log.ScriviLog("MenuPosta.ApriCampiFirma")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiFirma: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiFirmaConfermato(sender As Object, e As EventArgs)
        Try
            If _campiFirma Is Nothing Then Return
            Dim nome As String = If(_campiFirma.RecuperaValore("firma.nome"), "").Trim()
            Dim testo As String = If(_campiFirma.RecuperaValore("firma.testo"), "")
            DisposeCampiFirma()
            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Dai un nome alla firma.")
                Return
            End If
            If _firmaInModifica Is Nothing Then _firmaInModifica = New FirmaMail()
            _firmaInModifica.Nome = nome
            _firmaInModifica.Testo = testo
            AvanzataMP().SalvaFirma(_firmaInModifica)
            MostraMessaggio("Firma salvata.")
            MostraElencoFirme()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiFirmaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiFirmaChiusura(sender As Object, e As EventArgs)
        Try
            DisposeCampiFirma()
            log.ScriviLog("MenuPosta.CampiFirmaChiusura")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiFirmaChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub DisposeCampiFirma()
        Try
            If _campiFirma Is Nothing Then Return
            Try
                RemoveHandler _campiFirma.Fine, AddressOf CampiFirmaConfermato
            Catch exFine As Exception
                log.ScriviLog("MenuPosta.DisposeCampiFirma Fine: " & exFine.Message)
            End Try
            Try
                RemoveHandler _campiFirma.Chiusura, AddressOf CampiFirmaChiusura
            Catch exChiusura As Exception
                log.ScriviLog("MenuPosta.DisposeCampiFirma Chiusura: " & exChiusura.Message)
            End Try
            
            _campiFirma = Nothing
            log.ScriviLog("MenuPosta.DisposeCampiFirma")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DisposeCampiFirma: " & ex.Message)
            _campiFirma = Nothing
        End Try
    End Sub

    ' Una sola lista per tutta la gestione delle regole: se e' gia' aperta (perche' si
    ' torna qui dopo una modifica) si riusa e si aggiorna il contenuto, invece di crearne
    ' una nuova ogni volta. Si chiude sempre e solo con l'evento escPremuto.
    Private Sub MostraElencoRegole()
        Try
            Dim giaAperta As Boolean = _listaRegole IsNot Nothing AndAlso Not _listaRegole.IsDisposed
            If Not giaAperta Then
                _listaRegole = New WGMenuLista()
                _listaRegole.multiselezioneConsentita = False
                _listaRegole.impostaTitolo("Regole")
            Else
                _listaRegole.pulisci()
                _listaRegole.impostaTitolo("Regole")
            End If
            Dim regole As List(Of RegolaMail) = AvanzataMP().OttieniRegole(False)
            If regole Is Nothing OrElse regole.Count = 0 Then
                _listaRegole.aggiungiElemento("Nessuna regola")
            Else
                For Each r As RegolaMail In regole
                    _listaRegole.aggiungiElemento(DescriviRegola(r), r.Chiave)
                Next
            End If
            If Not giaAperta Then
                _listaRegole.aggiungiOpzione("Nuova regola", AddressOf OpzioneNuovaRegola)
                _listaRegole.aggiungiOpzione("Modifica", AddressOf OpzioneModificaRegola)
                _listaRegole.aggiungiOpzione("Attiva o disattiva", AddressOf OpzioneAttivaDisattivaRegola)
                _listaRegole.aggiungiOpzione("Elimina", AddressOf OpzioneEliminaRegola)
                AddHandler _listaRegole.escPremuto, AddressOf ListaRegoleEsc
                _listaRegole.avvia()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraElencoRegole: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviRegola(r As RegolaMail) As String
        Try
            If r Is Nothing Then Return ""
            Dim nome As String = If(String.IsNullOrWhiteSpace(r.Nome), "Regola senza nome", r.Nome)
            Dim stato As String = If(r.Attiva, "attiva", "disattiva")
            Dim casella As String = DescriviCasellaRegola(r.ChiaveAccount)
            Dim descrizione As String = nome & " (" & DescriviCampo(r.Campo) & " " & DescriviOperatore(r.Operatore) & " " & If(r.Valore, "") & " -> " & DescriviAzione(r.Azione) & ") [" & stato & "]"
            If Not String.IsNullOrWhiteSpace(casella) Then descrizione &= " - " & casella
            Return descrizione
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviRegola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function DescriviCasellaRegola(chiaveAccount As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return ""
            Dim impostazioni As ImpostazioniAccountMail = OttieniImpostazioniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return ""
            Return DescriviAccount(impostazioni)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviCasellaRegola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function DescriviCampo(campo As String) As String
        Select Case If(campo, "").Trim().ToUpperInvariant()
            Case "OGGETTO" : Return "Oggetto"
            Case "CORPO" : Return "Corpo"
            Case "DESTINATARIO" : Return "Destinatario"
            Case Else : Return "Mittente"
        End Select
    End Function

    Private Function DescriviOperatore(operatore As String) As String
        Select Case If(operatore, "").Trim().ToUpperInvariant()
            Case "UGUALE" : Return "uguale a"
            Case "INIZIACON" : Return "inizia con"
            Case "FINISCECON" : Return "finisce con"
            Case "REGEX" : Return "regex"
            Case Else : Return "contiene"
        End Select
    End Function

    Private Function DescriviAzione(azione As String) As String
        Select Case If(azione, "").Trim().ToUpperInvariant()
            Case "SEGNANONLETTO" : Return "segna come non letto"
            Case "SPOSTACARTELLA" : Return "sposta in cartella"
            Case "ELIMINA" : Return "elimina"
            Case Else : Return "segna come letto"
        End Select
    End Function

    Private Sub ListaRegoleEsc(sender As Object, e As EventArgs)
        Try
            If _listaRegole IsNot Nothing Then _listaRegole.abbandona()
            _listaRegole = Nothing
        Catch
            _listaRegole = Nothing
        End Try
    End Sub

    Private Function ChiaveRegolaSelezionata() As String
        Try
            If _listaRegole Is Nothing Then Return ""
            Dim elementi As List(Of String) = _listaRegole.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Return If(elementi(0), "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiaveRegolaSelezionata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub OpzioneNuovaRegola(sender As Object, e As EventArgs)
        Try
            _regolaInModifica = New RegolaMail()
            AvviaSceltaCasellaPerRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneNuovaRegola: " & ex.Message)
        End Try
    End Sub

    ' Una regola vale per una sola casella: va chiesto subito a quale si applica, prima
    ' ancora di chiedere campo/operatore/azione, cosi' il motore che la esegue sa dove
    ' agire e non rischia di applicarla per errore a un account diverso.
    Private Sub AvviaSceltaCasellaPerRegola()
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
            If attivi Is Nothing OrElse attivi.Count = 0 Then
                MostraMessaggio("Nessun account attivo per cui creare una regola.")
                Return
            End If
            If attivi.Count = 1 Then
                If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
                _regolaInModifica.ChiaveAccount = attivi(0).Chiave
                MostraSceltaCampoRegola()
                Return
            End If
            MostraSceltaAccountPer(ScopoAccount.ChiediRegola, "Casella a cui applicare la regola")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaSceltaCasellaPerRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneModificaRegola(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveRegolaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una regola.")
                Return
            End If
            _regolaInModifica = AvanzataMP().OttieniRegola(chiave)
            If _regolaInModifica Is Nothing Then
                MostraMessaggio("Regola non trovata.")
                Return
            End If
            MostraSceltaCampoRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneModificaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAttivaDisattivaRegola(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveRegolaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una regola.")
                Return
            End If
            Dim attiva As Boolean = AvanzataMP().AttivaDisattivaRegola(chiave)
            MostraMessaggio(If(attiva, "Regola attivata.", "Regola disattivata."))
            MostraElencoRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneAttivaDisattivaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaRegola(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = ChiaveRegolaSelezionata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona prima una regola.")
                Return
            End If
            If AvanzataMP().EliminaRegola(chiave) Then
                MostraMessaggio("Regola eliminata.")
            Else
                MostraMessaggio("Eliminazione non riuscita.")
            End If
            MostraElencoRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneEliminaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraSceltaCampoRegola()
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _menuSceltaRegola = New WGMenu()
            _menuSceltaRegola.aggiungi("Mittente", AddressOf CampoRegolaMittente)
            _menuSceltaRegola.aggiungi("Oggetto", AddressOf CampoRegolaOggetto)
            _menuSceltaRegola.aggiungi("Corpo", AddressOf CampoRegolaCorpo)
            _menuSceltaRegola.aggiungi("Destinatario", AddressOf CampoRegolaDestinatario)
            _menuSceltaRegola.avvia("Su quale parte del messaggio", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaCampoRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoRegolaMittente(sender As Object, e As EventArgs)
        ImpostaCampoRegola("Mittente")
    End Sub

    Private Sub CampoRegolaOggetto(sender As Object, e As EventArgs)
        ImpostaCampoRegola("Oggetto")
    End Sub

    Private Sub CampoRegolaCorpo(sender As Object, e As EventArgs)
        ImpostaCampoRegola("Corpo")
    End Sub

    Private Sub CampoRegolaDestinatario(sender As Object, e As EventArgs)
        ImpostaCampoRegola("Destinatario")
    End Sub

    Private Sub ImpostaCampoRegola(campo As String)
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Campo = campo
            MostraSceltaOperatoreRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ImpostaCampoRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraSceltaOperatoreRegola()
        Try
            _menuSceltaRegola = New WGMenu()
            _menuSceltaRegola.aggiungi("Contiene", AddressOf OperatoreRegolaContiene)
            _menuSceltaRegola.aggiungi("Uguale a", AddressOf OperatoreRegolaUguale)
            _menuSceltaRegola.aggiungi("Inizia con", AddressOf OperatoreRegolaIniziaCon)
            _menuSceltaRegola.aggiungi("Finisce con", AddressOf OperatoreRegolaFinisceCon)
            _menuSceltaRegola.aggiungi("Espressione regolare", AddressOf OperatoreRegolaRegex)
            _menuSceltaRegola.avvia("Come confrontare", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaOperatoreRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub OperatoreRegolaContiene(sender As Object, e As EventArgs)
        ImpostaOperatoreRegola("Contiene")
    End Sub

    Private Sub OperatoreRegolaUguale(sender As Object, e As EventArgs)
        ImpostaOperatoreRegola("Uguale")
    End Sub

    Private Sub OperatoreRegolaIniziaCon(sender As Object, e As EventArgs)
        ImpostaOperatoreRegola("IniziaCon")
    End Sub

    Private Sub OperatoreRegolaFinisceCon(sender As Object, e As EventArgs)
        ImpostaOperatoreRegola("FinisceCon")
    End Sub

    Private Sub OperatoreRegolaRegex(sender As Object, e As EventArgs)
        ImpostaOperatoreRegola("Regex")
    End Sub

    Private Sub ImpostaOperatoreRegola(operatore As String)
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Operatore = operatore
            ApriCampiValoreRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ImpostaOperatoreRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiValoreRegola()
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _campiRegola = New Campi()
            _campiRegola.SalvaSuEscape = False
            _campiRegola.Aggiungi("Nome della regola", "regola.nome", True, False, If(_regolaInModifica.Nome, ""))
            _campiRegola.Aggiungi("Testo da confrontare", "regola.valore", True, False, If(_regolaInModifica.Valore, ""))
            AddHandler _campiRegola.Fine, AddressOf CampiValoreRegolaConfermato
            AddHandler _campiRegola.Chiusura, AddressOf CampiRegolaChiusura
            _campiRegola.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiValoreRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiValoreRegolaConfermato(sender As Object, e As EventArgs)
        Try
            If _campiRegola Is Nothing Then Return
            Dim nome As String = If(_campiRegola.RecuperaValore("regola.nome"), "").Trim()
            Dim valore As String = If(_campiRegola.RecuperaValore("regola.valore"), "").Trim()
            DisposeCampiRegola()
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Nome = nome
            _regolaInModifica.Valore = valore
            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(valore) Then
                MostraMessaggio("Servono sia il nome sia il testo da confrontare.")
                MostraElencoRegole()
                Return
            End If
            MostraSceltaAzioneRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiValoreRegolaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraSceltaAzioneRegola()
        Try
            _menuSceltaRegola = New WGMenu()
            _menuSceltaRegola.aggiungi("Segna come letto", AddressOf AzioneRegolaSegnaLetto)
            _menuSceltaRegola.aggiungi("Segna come non letto", AddressOf AzioneRegolaSegnaNonLetto)
            _menuSceltaRegola.aggiungi("Sposta in una cartella", AddressOf AzioneRegolaSposta)
            _menuSceltaRegola.aggiungi("Segna come indesiderata", AddressOf AzioneRegolaIndesiderata)
            _menuSceltaRegola.aggiungi("Elimina", AddressOf AzioneRegolaElimina)
            _menuSceltaRegola.avvia("Cosa fare quando corrisponde", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraSceltaAzioneRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub AzioneRegolaSegnaLetto(sender As Object, e As EventArgs)
        ImpostaAzioneRegola("SegnaLetto")
    End Sub

    Private Sub AzioneRegolaSegnaNonLetto(sender As Object, e As EventArgs)
        ImpostaAzioneRegola("SegnaNonLetto")
    End Sub

    Private Sub AzioneRegolaSposta(sender As Object, e As EventArgs)
        ImpostaAzioneRegola("SpostaCartella")
    End Sub

    Private Sub AzioneRegolaIndesiderata(sender As Object, e As EventArgs)
        ImpostaAzioneRegola("SegnaIndesiderata")
    End Sub

    Private Sub AzioneRegolaElimina(sender As Object, e As EventArgs)
        ImpostaAzioneRegola("Elimina")
    End Sub

    Private Sub ImpostaAzioneRegola(azione As String)
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Azione = azione
            If azione = "SpostaCartella" Then
                AvviaSceltaCartellaRegola()
            Else
                _regolaInModifica.Parametro = ""
                SalvaRegolaCorrente()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ImpostaAzioneRegola: " & ex.Message)
        End Try
    End Sub

    ' La cartella di destinazione va scelta dall'elenco vero del server: scritta a mano
    ' bisognerebbe indovinare il percorso esatto (su Gmail, per dire, "[Gmail]/Etichetta"),
    ' e una regola con un percorso sbagliato non sposta niente senza dire perche'.
    Private _cartellePerRegola As List(Of CartellaImap)
    Private _listaCartelleRegola As WGMenuLista

    Private Sub AvviaSceltaCartellaRegola()
        Try
            Dim chiave As String = _chiaveAccountCorrente
            If String.IsNullOrWhiteSpace(chiave) Then
                Dim chiavi As List(Of String) = ChiaviDaConsultare()
                If chiavi IsNot Nothing AndAlso chiavi.Count > 0 Then chiave = chiavi(0)
            End If
            If String.IsNullOrWhiteSpace(chiave) Then
                Dim attivi As List(Of ImpostazioniAccountMail) = _posta.OttieniElencoAccount(True)
                If attivi IsNot Nothing AndAlso attivi.Count > 0 Then chiave = attivi(0).Chiave
            End If

            If String.IsNullOrWhiteSpace(chiave) Then
                ApriCampiParametroRegola()
                Return
            End If

            _chiaveAccountCorrente = chiave
            MostraAttesa("Lettura cartelle in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCartelleRegola))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaSceltaCartellaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroCartelleRegola()
        Try
            _cartellePerRegola = _posta.OttieniElencoCartelleImap(_chiaveAccountCorrente)
            If _cartellePerRegola Is Nothing Then _cartellePerRegola = New List(Of CartellaImap)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCartelleRegola: " & ex.Message)
            _cartellePerRegola = New List(Of CartellaImap)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCartelleRegola)
        End Try
    End Sub

    Private Sub CompletaCartelleRegola()
        Try
            ChiudiAttesa()
            MostraListaCartelleRegola()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCartelleRegola: " & ex.Message)
        End Try
    End Sub

    ' L'elenco delle cartelle vere del server include sempre l'opzione per crearne una
    ' nuova al volo, cosi' non serve uscire dal flusso della regola per andare a creare
    ' la cartella altrove e poi tornare indietro a scegliere.
    Private Sub MostraListaCartelleRegola()
        Try
            DisponiListaCartelleRegola()
            _listaCartelleRegola = New WGMenuLista()
            _listaCartelleRegola.multiselezioneConsentita = False
            _listaCartelleRegola.impostaTitolo("Cartella in cui spostare i messaggi")
            If _cartellePerRegola IsNot Nothing AndAlso _cartellePerRegola.Count > 0 Then
                For Each cartella As CartellaImap In _cartellePerRegola
                    _listaCartelleRegola.aggiungiElemento(DescriviCartella(cartella), cartella.PercorsoCompleto)
                Next
            Else
                _listaCartelleRegola.aggiungiElemento("Nessuna cartella trovata")
            End If
            _listaCartelleRegola.aggiungiOpzione("Crea una nuova cartella", AddressOf OpzioneCreaCartellaRegola)
            AddHandler _listaCartelleRegola.ElementoSelezionato, AddressOf CartellaRegolaSelezionata
            AddHandler _listaCartelleRegola.escPremuto, AddressOf ListaCartelleRegolaEsc
            _listaCartelleRegola.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraListaCartelleRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCreaCartellaRegola(sender As Object, e As EventArgs)
        Try
            DisponiListaCartelleRegola()
            _campiRegola = New Campi()
            _campiRegola.SalvaSuEscape = False
            _campiRegola.Aggiungi("Nome della nuova cartella", "regola.nuovacartella", True, False, "")
            AddHandler _campiRegola.Fine, AddressOf CampiNuovaCartellaRegolaConfermato
            AddHandler _campiRegola.Chiusura, AddressOf CampiRegolaChiusura
            _campiRegola.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneCreaCartellaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiNuovaCartellaRegolaConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiRegola IsNot Nothing Then nome = If(_campiRegola.RecuperaValore("regola.nuovacartella"), "").Trim()
            DisposeCampiRegola()
            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della cartella non può essere vuoto.")
                MostraListaCartelleRegola()
                Return
            End If
            _nomeNuovaCartellaRegola = nome
            MostraAttesa("Creazione della cartella in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroCreaCartellaRegola))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiNuovaCartellaRegolaConfermato: " & ex.Message)
        End Try
    End Sub

    Private _nomeNuovaCartellaRegola As String = ""
    Private _cartellaRegolaCreata As Boolean = False

    Private Sub LavoroCreaCartellaRegola()
        Try
            _cartellaRegolaCreata = _posta.CreaCartellaImap(_chiaveAccountCorrente, _nomeNuovaCartellaRegola, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroCreaCartellaRegola: " & ex.Message)
            _cartellaRegolaCreata = False
        Finally
            EseguiSuInterfaccia(AddressOf CompletaCreaCartellaRegola)
        End Try
    End Sub

    Private Sub CompletaCreaCartellaRegola()
        Try
            ChiudiAttesa()
            If Not _cartellaRegolaCreata Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaCreaCartellaRegola errore: " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(descrizione), "Cartella non creata.", descrizione))
                _nomeNuovaCartellaRegola = ""
                MostraListaCartelleRegola()
                Return
            End If
            ' Si rilegge l'elenco vero dal server invece di comporre il percorso a mano:
            ' dipende dal provider (su Gmail, per dire, cambia rispetto a un IMAP qualsiasi)
            ' e una regola con un percorso indovinato male non sposta niente senza dire perche'.
            MostraAttesa("Lettura cartelle in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroRileggiCartelleRegola))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaCreaCartellaRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRileggiCartelleRegola()
        Try
            _cartellePerRegola = _posta.OttieniElencoCartelleImap(_chiaveAccountCorrente)
            If _cartellePerRegola Is Nothing Then _cartellePerRegola = New List(Of CartellaImap)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroRileggiCartelleRegola: " & ex.Message)
            _cartellePerRegola = New List(Of CartellaImap)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaRilettureCartelleRegola)
        End Try
    End Sub

    Private Sub CompletaRilettureCartelleRegola()
        Try
            ChiudiAttesa()
            Dim creata As CartellaImap = Nothing
            If _cartellePerRegola IsNot Nothing Then
                For Each cartella As CartellaImap In _cartellePerRegola
                    If String.Equals(cartella.Nome, _nomeNuovaCartellaRegola, StringComparison.OrdinalIgnoreCase) Then
                        creata = cartella
                        Exit For
                    End If
                Next
            End If
            _nomeNuovaCartellaRegola = ""
            If creata IsNot Nothing Then
                If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
                _regolaInModifica.Parametro = creata.PercorsoCompleto
                SalvaRegolaCorrente()
            Else
                MostraMessaggio("Cartella creata.")
                MostraListaCartelleRegola()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaRilettureCartelleRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub DisponiListaCartelleRegola()
        Try
            If _listaCartelleRegola Is Nothing Then Return
            Try
                RemoveHandler _listaCartelleRegola.ElementoSelezionato, AddressOf CartellaRegolaSelezionata
                RemoveHandler _listaCartelleRegola.escPremuto, AddressOf ListaCartelleRegolaEsc
            Catch exRimozione As Exception
                log.ScriviLog("MenuPosta.DisponiListaCartelleRegola (handler): " & exRimozione.Message)
            End Try
            If Not _listaCartelleRegola.IsDisposed Then _listaCartelleRegola.abbandona()
            _listaCartelleRegola = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DisponiListaCartelleRegola: " & ex.Message)
            _listaCartelleRegola = Nothing
        End Try
    End Sub

    Private Sub ListaCartelleRegolaEsc(sender As Object, e As EventArgs)
        Try
            DisponiListaCartelleRegola()
            MostraElencoRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ListaCartelleRegolaEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub CartellaRegolaSelezionata(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            DisponiListaCartelleRegola()
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Parametro = percorso
            SalvaRegolaCorrente()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaRegolaSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiParametroRegola()
        Try
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _campiRegola = New Campi()
            _campiRegola.SalvaSuEscape = False
            _campiRegola.Aggiungi("Nome della cartella di destinazione", "regola.parametro", True, False, If(_regolaInModifica.Parametro, ""))
            AddHandler _campiRegola.Fine, AddressOf CampiParametroRegolaConfermato
            AddHandler _campiRegola.Chiusura, AddressOf CampiRegolaChiusura
            _campiRegola.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiParametroRegola: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiParametroRegolaConfermato(sender As Object, e As EventArgs)
        Try
            If _campiRegola Is Nothing Then Return
            Dim parametro As String = If(_campiRegola.RecuperaValore("regola.parametro"), "").Trim()
            DisposeCampiRegola()
            If _regolaInModifica Is Nothing Then _regolaInModifica = New RegolaMail()
            _regolaInModifica.Parametro = parametro
            If String.IsNullOrWhiteSpace(parametro) Then
                MostraMessaggio("Indica il nome della cartella di destinazione.")
                MostraElencoRegole()
                Return
            End If
            SalvaRegolaCorrente()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiParametroRegolaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaRegolaCorrente()
        Try
            If _regolaInModifica Is Nothing Then Return
            AvanzataMP().SalvaRegola(_regolaInModifica)
            MostraMessaggio("Regola salvata.")
            MostraElencoRegole()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SalvaRegolaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiRegolaChiusura(sender As Object, e As EventArgs)
        Try
            DisposeCampiRegola()
        Catch
        End Try
    End Sub

    Private Sub DisposeCampiRegola()
        Try
            If _campiRegola Is Nothing Then Return
            Try
                RemoveHandler _campiRegola.Fine, AddressOf CampiValoreRegolaConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiRegola.Fine, AddressOf CampiParametroRegolaConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiRegola.Fine, AddressOf CampiNuovaCartellaRegolaConfermato
            Catch
            End Try
            Try
                RemoveHandler _campiRegola.Chiusura, AddressOf CampiRegolaChiusura
            Catch
            End Try
            
            _campiRegola = Nothing
        Catch
            _campiRegola = Nothing
        End Try
    End Sub


    Private Sub OpzioneSegnaTuttiLetti(sender As Object, e As EventArgs)
        Try
            If _listaMessaggiRisultatiRicerca Then
                MostraMessaggio("Non disponibile sui risultati di ricerca.")
                Return
            End If
            MostraAttesa("Segno i messaggi come letti, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSegnaTuttiLetti))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneSegnaTuttiLetti: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSegnaTuttiLetti()
        Try
            For Each chiave As String In ChiaviDaConsultare()
                Try
                    _posta.MarcaCartellaComeLetta(chiave, _cartellaCorrente)
                Catch exSingolo As Exception
                    log.ScriviLog("MenuPosta.LavoroSegnaTuttiLetti (" & If(chiave, "") & "): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroSegnaTuttiLetti: " & ex.Message)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaSegnaTuttiLetti)
        End Try
    End Sub

    Private Sub CompletaSegnaTuttiLetti()
        Try
            ChiudiAttesa()
            SegnaElencoLocaleComeLetto()
            AggiornaElementiListaMessaggi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaSegnaTuttiLetti: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnaElencoLocaleComeLetto()
        Try
            If _elencoMessaggi Is Nothing Then Return
            For Each riepilogo As RiepilogoMessaggio In _elencoMessaggi
                If riepilogo Is Nothing Then Continue For
                riepilogo.Letto = True
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SegnaElencoLocaleComeLetto: " & ex.Message)
        End Try
    End Sub


    Private _composizioneDestinatariMulti As New List(Of String)
    Private _composizioneCcLista As New List(Of String)
    Private _composizioneCcnLista As New List(Of String)

    Private Function SpezzaListaIndirizzi(lista As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If lista Is Nothing Then Return risultato
            For Each voce As String In lista
                risultato.AddRange(SpezzaIndirizzi(voce))
            Next
        Catch ex As Exception
            log.ScriviLog("MenuPosta.SpezzaListaIndirizzi: " & ex.Message)
        End Try
        Return risultato
    End Function

End Class