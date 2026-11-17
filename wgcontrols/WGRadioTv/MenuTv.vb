Imports System
Imports System.Collections.Generic
Imports System.Net.NetworkInformation
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGDati
Imports wgcontrols
Imports WGPlayerMpv

' Gestione della visione dei canali televisivi, dei preferiti, dei recenti e della consultazione dei programmi con relativi
' promemoria.
Public Class MenuTv

    Private Const TESTO_AGGIUNGI_PREFERITI As String = "Aggiungi ai preferiti"
    Private Const TESTO_RIMUOVI_PREFERITI As String = "Rimuovi dai preferiti"

    Private ReadOnly _db As TvCanaliDb = TvCanaliDb.GetInstance()
    Private ReadOnly _raccolta As RadioTvDatabase = RadioTvDatabase.GetInstance()
    Private ReadOnly _logger As New Logger()

    Private WithEvents _menuPrincipale As WGMenu
    Private WithEvents _lista As WGMenuLista
    Private WithEvents _listaPreferiti As WGMenuLista
    Private WithEvents _listaRecenti As WGMenuLista
    Private WithEvents _listaProgrammi As WGMenuLista

    Private _programmiCorrenti As New List(Of TvProgrammazione.VoceProgramma)
    Private _nomeCanaleProgrammi As String = ""
    Private _idCanaleProgrammi As String = ""

    Private _canaliCorrenti As New List(Of TvCanaliDb.CanaleTv)
    Private _testoOpzionePreferitoAttuale As String = TESTO_AGGIUNGI_PREFERITI

#Region "Avvio"

    Public Sub Avvia()
        Try
            If Not ConnessioneDisponibile() Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Non risulta nessuna connessione di rete attiva. Controlla il Wi-Fi o il cavo di rete e riprova.")
                avviso.ShowDialog()
                Return
            End If

            TvPromemoria.Avvia()
            If Not AssicuraDatiCaricati() Then Return

            AvviaVerificaInSottofondo()
            MostraMenuPrincipale()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.Avvia: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile aprire il menu Web TV.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Function ConnessioneDisponibile() As Boolean
        Try
            If Not NetworkInterface.GetIsNetworkAvailable() Then Return False

            Dim interfacce As NetworkInterface() = NetworkInterface.GetAllNetworkInterfaces()
            If interfacce Is Nothing OrElse interfacce.Length = 0 Then Return False

            For i As Integer = 0 To interfacce.Length - 1
                Try
                    Dim scheda As NetworkInterface = interfacce(i)
                    If scheda.OperationalStatus <> OperationalStatus.Up Then Continue For
                    If scheda.NetworkInterfaceType = NetworkInterfaceType.Loopback Then Continue For
                    If scheda.NetworkInterfaceType = NetworkInterfaceType.Tunnel Then Continue For

                    If scheda.NetworkInterfaceType = NetworkInterfaceType.Wireless80211 OrElse
                       scheda.NetworkInterfaceType = NetworkInterfaceType.Ethernet OrElse
                       scheda.NetworkInterfaceType = NetworkInterfaceType.Ethernet3Megabit OrElse
                       scheda.NetworkInterfaceType = NetworkInterfaceType.GigabitEthernet Then
                        Return True
                    End If
                Catch exScheda As Exception
                    _logger.ScriviLog("MenuTv.ConnessioneDisponibile scheda: " & exScheda.Message)
                End Try
            Next

            Return False
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ConnessioneDisponibile: " & ex.ToString())
            Return True
        End Try
    End Function

    Private Function AssicuraDatiCaricati() As Boolean
        Try
            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando l'elenco dei canali aggiornato, attendi", False)
            Try
                Dim ok As Boolean = _db.Carica()
                proc.Fine()
                If Not ok OrElse _db.NumeroCanali = 0 Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Non e' stato possibile scaricare l'elenco dei canali. Controlla la connessione a internet e riprova.")
                    msg.ShowDialog()
                    Return False
                End If
                Return True
            Catch exInterno As Exception
                _logger.ScriviLog("MenuTv.AssicuraDatiCaricati interno: " & exInterno.ToString())
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuTv.AssicuraDatiCaricati fine: " & exFine.Message)
                End Try
                Return False
            End Try
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.AssicuraDatiCaricati: " & ex.ToString())
            Return False
        End Try
    End Function

#End Region

#Region "Verifica in sottofondo"

    Private Sub AvviaVerificaInSottofondo()
        Try
            Task.Run(New Action(AddressOf EseguiVerificaSilenziosa))
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.AvviaVerificaInSottofondo: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub EseguiVerificaSilenziosa()
        Try
            If _db.VerificaInCorso Then Return
            Await _db.VerificaTuttiAsync()
            RimuoviPreferitiNonPiuDisponibili()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.EseguiVerificaSilenziosa: " & ex.ToString())
        End Try
    End Sub

    Private Sub RimuoviPreferitiNonPiuDisponibili()
        Try
            Dim nonFunzionanti As List(Of TvCanaliDb.CanaleTv) = _db.OttieniCanaliNonFunzionanti()
            For i As Integer = 0 To nonFunzionanti.Count - 1
                Try
                    Dim c As TvCanaliDb.CanaleTv = nonFunzionanti(i)
                    If _raccolta.EPreferito(RadioTvDatabase.TipoElemento.Tv, c.Id) Then
                        _raccolta.RimuoviPreferito(RadioTvDatabase.TipoElemento.Tv, c.Id)
                    End If
                Catch exCanale As Exception
                    _logger.ScriviLog("MenuTv.RimuoviPreferitiNonPiuDisponibili canale: " & exCanale.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.RimuoviPreferitiNonPiuDisponibili: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Menu principale"

    Private Sub MostraMenuPrincipale()
        Try
            ChiudiTutteLeListe()

            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Elenco Canali", AddressOf VoceElencoCanali)
            _menuPrincipale.aggiungi("Canali Preferiti", AddressOf VoceCanaliPreferiti)
            _menuPrincipale.aggiungi("Canali Recenti", AddressOf VoceCanaliRecenti)
            _menuPrincipale.aggiungi("In onda adesso", Sub() ApriVistaPalinsesto(1))
            _menuPrincipale.aggiungi("Cerca nei palinsesti", Sub() ApriVistaPalinsesto(2))
            _menuPrincipale.aggiungi("Programmi seguiti", Sub() ApriVistaPalinsesto(3))
            _menuPrincipale.avvia("Web TV")
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.MostraMenuPrincipale: " & ex.ToString())
        End Try
    End Sub

    ' Le viste sulla programmazione di tutti i canali insieme: 1 in onda adesso, 2 ricerca nei palinsesti, 3 programmi seguiti.
    Private Sub ApriVistaPalinsesto(vista As Integer)
        Try
            Dim palinsesto As New MenuPalinsesto(TvPromemoria.TIPO_TV)
            Select Case vista
                Case 1 : palinsesto.InOndaAdesso()
                Case 2 : palinsesto.Cerca()
                Case Else : palinsesto.Seguiti()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ApriVistaPalinsesto: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceElencoCanali(sender As Object, e As EventArgs)
        Try
            ApriElenco()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.VoceElencoCanali: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceCanaliPreferiti(sender As Object, e As EventArgs)
        Try
            ApriPreferiti(sender, e)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.VoceCanaliPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceCanaliRecenti(sender As Object, e As EventArgs)
        Try
            ApriRecenti(sender, e)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.VoceCanaliRecenti: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Elenco canali"

    Private Sub ApriElenco()
        Try
            Dim canali As List(Of TvCanaliDb.CanaleTv) = _db.OttieniTuttiICanali()
            If canali Is Nothing OrElse canali.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun canale disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            _canaliCorrenti = canali

            ChiudiListeSecondarie()
            ChiudiLista()

            _lista = New WGMenuLista()
            _lista.ciclono = True
            _lista.selezioneMenu = True
            _lista.multiselezioneConsentita = False

            RiempiLista(_lista, canali)
            ConfiguraOpzioniBase(_lista, canali)

            _lista.impostaTitolo("Elenco Canali")
            _lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ApriElenco: " & ex.ToString())
        End Try
    End Sub

    Private Sub _lista_ElementoSelezionato(id As String) Handles _lista.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._lista_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _lista_escPremuto(sender As Object, e As EventArgs) Handles _lista.escPremuto
        Try
            ChiudiLista()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._lista_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _lista_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _lista.VoceCambiata
        Try
            AggiornaOpzionePreferito(_lista, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._lista_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Canali preferiti"

    Private Sub ApriPreferiti(sender As Object, e As EventArgs)
        Try
            Dim voci As List(Of RadioTvDatabase.VoceRaccolta) = _raccolta.OttieniPreferiti(RadioTvDatabase.TipoElemento.Tv)
            Dim canali As List(Of TvCanaliDb.CanaleTv) = RisolviCanaliDaVoci(voci)

            If canali Is Nothing OrElse canali.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai ancora nessun canale tra i preferiti.")
                msg.ShowDialog()
                Return
            End If

            _canaliCorrenti = canali

            ChiudiListeSecondarie()

            _listaPreferiti = New WGMenuLista()
            _listaPreferiti.ciclono = True
            _listaPreferiti.selezioneMenu = True
            _listaPreferiti.multiselezioneConsentita = False

            RiempiLista(_listaPreferiti, canali)
            ConfiguraOpzioniBase(_listaPreferiti, canali)

            _listaPreferiti.impostaTitolo("Canali preferiti")
            _listaPreferiti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ApriPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferiti_ElementoSelezionato(id As String) Handles _listaPreferiti.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaPreferiti_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferiti_escPremuto(sender As Object, e As EventArgs) Handles _listaPreferiti.escPremuto
        Try
            If _listaPreferiti IsNot Nothing Then
                _listaPreferiti.abbandona()
                _listaPreferiti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaPreferiti_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferiti_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaPreferiti.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaPreferiti, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaPreferiti_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Canali recenti"

    Private Sub ApriRecenti(sender As Object, e As EventArgs)
        Try
            Dim voci As List(Of RadioTvDatabase.VoceRaccolta) = _raccolta.OttieniRecenti(RadioTvDatabase.TipoElemento.Tv)
            Dim canali As List(Of TvCanaliDb.CanaleTv) = RisolviCanaliDaVoci(voci)

            If canali Is Nothing OrElse canali.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai ancora guardato nessun canale.")
                msg.ShowDialog()
                Return
            End If

            _canaliCorrenti = canali

            ChiudiListeSecondarie()

            _listaRecenti = New WGMenuLista()
            _listaRecenti.ciclono = True
            _listaRecenti.selezioneMenu = True
            _listaRecenti.multiselezioneConsentita = False

            RiempiLista(_listaRecenti, canali)
            ConfiguraOpzioniBase(_listaRecenti, canali)
            _listaRecenti.aggiungiOpzione("Rimuovi dai recenti", AddressOf OpzioneRimuoviRecente)

            _listaRecenti.impostaTitolo("Canali guardati di recente")
            _listaRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ApriRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneRimuoviRecente(sender As Object, e As EventArgs)
        Try
            Dim id As String = IdSelezionatoCorrente()
            If String.IsNullOrWhiteSpace(id) Then Return

            _raccolta.RimuoviRecente(RadioTvDatabase.TipoElemento.Tv, id)

            If _listaRecenti IsNot Nothing AndAlso Not _listaRecenti.IsDisposed Then
                _listaRecenti.RimuoviElemento(id)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzioneRimuoviRecente: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_ElementoSelezionato(id As String) Handles _listaRecenti.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaRecenti_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_escPremuto(sender As Object, e As EventArgs) Handles _listaRecenti.escPremuto
        Try
            If _listaRecenti IsNot Nothing Then
                _listaRecenti.abbandona()
                _listaRecenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaRecenti_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaRecenti.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaRecenti, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaRecenti_VoceCambiata: " & ex.Message)
        End Try
    End Sub

    Private Function RisolviCanaliDaVoci(voci As List(Of RadioTvDatabase.VoceRaccolta)) As List(Of TvCanaliDb.CanaleTv)
        Dim risultato As New List(Of TvCanaliDb.CanaleTv)
        Try
            If voci Is Nothing Then Return risultato

            For i As Integer = 0 To voci.Count - 1
                Dim canale As TvCanaliDb.CanaleTv = _db.TrovaPerId(voci(i).Id)
                If canale Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(canale.Url) Then Continue For
                risultato.Add(canale)
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.RisolviCanaliDaVoci: " & ex.ToString())
        End Try
        Return risultato
    End Function

#End Region

#Region "Opzioni comuni e preferito dinamico"

    Private Sub ConfiguraOpzioniBase(lista As WGMenuLista, canali As List(Of TvCanaliDb.CanaleTv))
        Try
            If lista Is Nothing Then Return

            lista.aggiungiOpzione("Riproduci", AddressOf OpzioneRiproduci)
            lista.aggiungiOpzione("Programmazione", AddressOf OpzioneProgrammazione)
            lista.aggiungiOpzione("Copia indirizzo", AddressOf OpzioneCopiaIndirizzo)

            Dim testoIniziale As String = TESTO_AGGIUNGI_PREFERITI
            If canali IsNot Nothing AndAlso canali.Count > 0 Then
                testoIniziale = TestoOpzionePreferito(canali(0).Id)
            End If

            lista.aggiungiOpzione(testoIniziale, AddressOf OpzionePreferito)
            _testoOpzionePreferitoAttuale = testoIniziale
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ConfiguraOpzioniBase: " & ex.ToString())
        End Try
    End Sub

    Private Function TestoOpzionePreferito(id As String) As String
        Try
            If _raccolta.EPreferito(RadioTvDatabase.TipoElemento.Tv, id) Then Return TESTO_RIMUOVI_PREFERITI
            Return TESTO_AGGIUNGI_PREFERITI
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.TestoOpzionePreferito: " & ex.Message)
            Return TESTO_AGGIUNGI_PREFERITI
        End Try
    End Function

    Private Sub AggiornaOpzionePreferito(lista As WGMenuLista, id As String)
        Try
            If lista Is Nothing OrElse lista.IsDisposed Then Return
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim nuovoTesto As String = TestoOpzionePreferito(id)
            If String.Equals(nuovoTesto, _testoOpzionePreferitoAttuale, StringComparison.Ordinal) Then Return

            lista.sostituisciOpzione(_testoOpzionePreferitoAttuale, nuovoTesto, AddressOf OpzionePreferito)
            _testoOpzionePreferitoAttuale = nuovoTesto
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.AggiornaOpzionePreferito: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzionePreferito(sender As Object, e As EventArgs)
        Try
            Dim lista As WGMenuLista = ListaCanaliAttiva()
            Dim id As String = ""
            If lista IsNot Nothing Then id = lista.ValoreSelezionato

            If String.IsNullOrWhiteSpace(id) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun canale selezionato.")
                msg.ShowDialog()
                Return
            End If

            Dim canale As TvCanaliDb.CanaleTv = TrovaCanale(id)
            If canale Is Nothing Then Return

            Dim nuovoStato As Boolean = _raccolta.CommutaPreferito(RadioTvDatabase.TipoElemento.Tv, id, canale.Nome)
            Dim nuovoTesto As String = TESTO_AGGIUNGI_PREFERITI
            If nuovoStato Then nuovoTesto = TESTO_RIMUOVI_PREFERITI

            If lista IsNot Nothing Then
                lista.sostituisciOpzione(_testoOpzionePreferitoAttuale, nuovoTesto, AddressOf OpzionePreferito)
                _testoOpzionePreferitoAttuale = nuovoTesto

                If lista Is _listaPreferiti AndAlso Not nuovoStato Then
                    lista.RimuoviElemento(id)
                End If
            End If

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                If nuovoStato Then
                    lettura.LeggiTesto(canale.Nome & " aggiunto ai preferiti.")
                Else
                    lettura.LeggiTesto(canale.Nome & " rimosso dai preferiti.")
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzionePreferito: " & ex.ToString())
        End Try
    End Sub

    Private Function ListaCanaliAttiva() As WGMenuLista
        Try
            If _listaPreferiti IsNot Nothing AndAlso Not _listaPreferiti.IsDisposed Then Return _listaPreferiti
            If _listaRecenti IsNot Nothing AndAlso Not _listaRecenti.IsDisposed Then Return _listaRecenti
            If _lista IsNot Nothing AndAlso Not _lista.IsDisposed Then Return _lista
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ListaCanaliAttiva: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneRiproduci(sender As Object, e As EventArgs)
        Try
            Dim id As String = IdSelezionatoCorrente()
            If String.IsNullOrWhiteSpace(id) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun canale selezionato.")
                msg.ShowDialog()
                Return
            End If
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzioneRiproduci: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneCopiaIndirizzo(sender As Object, e As EventArgs)
        Try
            Dim canale As TvCanaliDb.CanaleTv = CanaleSelezionato()
            If canale Is Nothing OrElse String.IsNullOrWhiteSpace(canale.Url) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Indirizzo non disponibile per questo canale.")
                msg.ShowDialog()
                Return
            End If

            Clipboard.SetText(canale.Url)

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto("Indirizzo di " & canale.Nome & " copiato negli appunti.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzioneCopiaIndirizzo: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile copiare l'indirizzo.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Function IdSelezionatoCorrente() As String
        Try
            Dim lista As WGMenuLista = ListaCanaliAttiva()
            If lista Is Nothing Then Return ""
            Dim v As String = lista.ValoreSelezionato
            If String.IsNullOrWhiteSpace(v) Then Return ""
            Return v
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.IdSelezionatoCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function CanaleSelezionato() As TvCanaliDb.CanaleTv
        Try
            Return TrovaCanale(IdSelezionatoCorrente())
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.CanaleSelezionato: " & ex.ToString())
            Return Nothing
        End Try
    End Function

#End Region

#Region "Programmazione"

    Private Sub OpzioneProgrammazione(sender As Object, e As EventArgs)
        Try
            Dim canale As TvCanaliDb.CanaleTv = CanaleSelezionato()
            If canale Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun canale selezionato.")
                msg.ShowDialog()
                Return
            End If

            If Not TvProgrammazione.ProgrammazioneDisponibile(canale.TvgId, canale.Nome) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per " & canale.Nome & " la programmazione non è disponibile.")
                msg.ShowDialog()
                Return
            End If

            ' L'attesa è sempre indicata da WGProcessoInCorso, subito e fino a download finito: il menu o il messaggio
            ' compaiono solo dopo che WGProcessoInCorso si è chiuso.
            Dim tvgId As String = canale.TvgId
            Dim nomeCanale As String = canale.Nome
            Dim programmi As List(Of TvProgrammazione.VoceProgramma) = Nothing
            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando la programmazione di " & canale.Nome & ", attendi.", False)
            Try
                programmi = Task.Run(Function() TvProgrammazione.OttieniAsync(tvgId, nomeCanale)).GetAwaiter().GetResult()
            Catch exScarico As Exception
                _logger.ScriviLog("MenuTv.OpzioneProgrammazione scarico: " & exScarico.ToString())
                programmi = Nothing
            Finally
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuTv.OpzioneProgrammazione fine: " & exFine.Message)
                End Try
            End Try

            If programmi Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile scaricare la programmazione. Controlla la connessione a internet e riprova.")
                msg.ShowDialog()
                Return
            End If

            If programmi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ho trovato la programmazione di " & canale.Nome & ": nessuna fonte la pubblica oppure non ci sono programmi.")
                msg.ShowDialog()
                Return
            End If

            ApriProgrammazione(canale.Id, canale.Nome, programmi)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzioneProgrammazione: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile mostrare la programmazione.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub ApriProgrammazione(idCanale As String, nomeCanale As String, programmi As List(Of TvProgrammazione.VoceProgramma))
        Try
            ChiudiListaProgrammi()

            _programmiCorrenti = programmi
            _nomeCanaleProgrammi = nomeCanale
            _idCanaleProgrammi = idCanale

            _listaProgrammi = New WGMenuLista()
            _listaProgrammi.ciclono = True
            _listaProgrammi.selezioneMenu = True
            _listaProgrammi.multiselezioneConsentita = False

            _listaProgrammi.BeginUpdate()
            For i As Integer = 0 To programmi.Count - 1
                _listaProgrammi.aggiungiElemento(EtichettaProgramma(programmi(i), i = 0), i.ToString())
            Next
            _listaProgrammi.EndUpdate()

            _listaProgrammi.impostaTitolo("Programmazione di " & nomeCanale)
            _listaProgrammi.aggiungiOpzione("Avvisami quando inizia", AddressOf OpzioneAvvisamiProgramma)
            _listaProgrammi.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ApriProgrammazione: " & ex.ToString())
        End Try
    End Sub

    Private Function EtichettaProgramma(p As TvProgrammazione.VoceProgramma, inOnda As Boolean) As String
        Try
            Dim ora As String = p.Inizio.ToString("HH:mm")
            If inOnda AndAlso p.Inizio <= DateTime.Now Then
                Return "In onda ora, dalle " & ora & ", " & p.Titolo
            End If
            If p.Inizio.Date = DateTime.Today Then Return "Alle " & ora & ", " & p.Titolo
            If p.Inizio.Date = DateTime.Today.AddDays(1) Then Return "Domani alle " & ora & ", " & p.Titolo
            Return p.Inizio.ToString("dddd d MMMM") & " alle " & ora & ", " & p.Titolo
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.EtichettaProgramma: " & ex.Message)
            Return "Programma"
        End Try
    End Function

    ' Invio su un programma: un menu con le informazioni e, se non è ancora iniziato, il promemoria; se è in onda, si può guardare il canale.
    Private Sub _listaProgrammi_ElementoSelezionato(id As String) Handles _listaProgrammi.ElementoSelezionato
        Try
            Dim indice As Integer
            If Not Integer.TryParse(id, indice) Then Return
            If indice < 0 OrElse indice >= _programmiCorrenti.Count Then Return

            Dim p As TvProgrammazione.VoceProgramma = _programmiCorrenti(indice)
            Dim idCanale As String = _idCanaleProgrammi
            Dim nomeCanale As String = _nomeCanaleProgrammi

            Dim menu As New WGMenu()
            menu.aggiungi("Informazioni", Sub() MostraInformazioniProgramma(p))

            If p.Inizio > DateTime.Now Then
                If TvPromemoria.Esiste(idCanale, p.Inizio) Then
                    menu.aggiungi("Rimuovi il promemoria", Sub() RimuoviPromemoria(idCanale, p))
                Else
                    menu.aggiungi("Imposta un promemoria", Sub() ImpostaPromemoria(idCanale, nomeCanale, p))
                End If
            Else
                menu.aggiungi("Guarda il canale adesso", Sub() TvPromemoria.AvviaCanale(idCanale))
            End If

            menu.avvia(p.Titolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaProgrammi_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraInformazioniProgramma(p As TvProgrammazione.VoceProgramma)
        Try
            Dim testo As String = p.Titolo & ", dalle " & p.Inizio.ToString("HH:mm") & " alle " & p.Fine.ToString("HH:mm") & "."
            If Not String.IsNullOrWhiteSpace(p.Descrizione) Then testo = testo & " " & p.Descrizione

            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.MostraInformazioniProgramma: " & ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaPromemoria(idCanale As String, nomeCanale As String, p As TvProgrammazione.VoceProgramma)
        Try
            Dim tvgId As String = ""
            Dim canale As TvCanaliDb.CanaleTv = _db.TrovaPerId(idCanale)
            If canale IsNot Nothing Then tvgId = canale.TvgId
            AzioniProgramma.ImpostaPromemoria(TvPromemoria.TIPO_TV, idCanale, nomeCanale, tvgId, p)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ImpostaPromemoria: " & ex.ToString())
        End Try
    End Sub

    Private Sub RimuoviPromemoria(idCanale As String, p As TvProgrammazione.VoceProgramma)
        AzioniProgramma.RimuoviPromemoria(TvPromemoria.TIPO_TV, idCanale, p)
    End Sub

    ' Opzione "Avvisami quando inizia" dell'elenco dei programmi: un promemoria solo per quella volta.
    Private Sub OpzioneAvvisamiProgramma(sender As Object, e As EventArgs)
        Try
            If _listaProgrammi Is Nothing Then Return
            Dim indice As Integer
            If Not Integer.TryParse(_listaProgrammi.ValoreSelezionato, indice) OrElse indice < 0 OrElse indice >= _programmiCorrenti.Count Then Return
            AzioniProgramma.AvvisamiQuandoInizia(TvPromemoria.TIPO_TV, _idCanaleProgrammi, _nomeCanaleProgrammi, _programmiCorrenti(indice))
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.OpzioneAvvisamiProgramma: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaProgrammi_escPremuto(sender As Object, e As EventArgs) Handles _listaProgrammi.escPremuto
        Try
            ChiudiListaProgrammi()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv._listaProgrammi_escPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaProgrammi()
        Try
            If _listaProgrammi Is Nothing Then Return
            Try
                _listaProgrammi.abbandona()
            Catch exAbb As Exception
                _logger.ScriviLog("MenuTv.ChiudiListaProgrammi abbandona: " & exAbb.Message)
            End Try
            _listaProgrammi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ChiudiListaProgrammi: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Riproduzione"

    Private Function TrovaCanale(id As String) As TvCanaliDb.CanaleTv
        Try
            If String.IsNullOrWhiteSpace(id) Then Return Nothing

            Dim canale As TvCanaliDb.CanaleTv = _db.TrovaPerId(id)
            If canale IsNot Nothing Then Return canale

            For i As Integer = 0 To _canaliCorrenti.Count - 1
                If String.Equals(_canaliCorrenti(i).Id, id, StringComparison.OrdinalIgnoreCase) Then
                    Return _canaliCorrenti(i)
                End If
            Next

            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.TrovaCanale: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub RiproduciPerId(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim canale As TvCanaliDb.CanaleTv = TrovaCanale(id)
            If canale Is Nothing OrElse String.IsNullOrWhiteSpace(canale.Url) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Canale non trovato o senza indirizzo di streaming.")
                msg.ShowDialog()
                Return
            End If

            RiproduciCanale(canale)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.RiproduciPerId: " & ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciCanale(canale As TvCanaliDb.CanaleTv)
        Try
            If canale Is Nothing OrElse String.IsNullOrWhiteSpace(canale.Url) Then Return

            _raccolta.AggiungiRecente(RadioTvDatabase.TipoElemento.Tv, canale.Id, canale.Nome)

            ' sottofondo o lettore a schermo, come nelle impostazioni (o con la richiesta ogni volta)
            WGModalitaAscolto.Avvia(canale.Nome,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciStreamTv(canale.Url, canale.Nome, canale.UserAgent, canale.Referer),
                                    Sub()
                                        Dim player As New WGPlayerFormInt()
                                        player.AggiungiElemento(canale.Url, ElementType.StreamTv, canale.Nome, canale.UserAgent, canale.Referer)
                                        player.avvia(canale.Nome)
                                    End Sub)
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.RiproduciCanale: " & ex.ToString())
            Dim nome As String = "questo canale"
            Try
                If canale IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(canale.Nome) Then nome = canale.Nome
            Catch exNome As Exception
                _logger.ScriviLog("MenuTv.RiproduciCanale nome: " & exNome.Message)
            End Try
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile avviare la riproduzione di " & nome & ".")
            msg.ShowDialog()
        End Try
    End Sub

#End Region

#Region "Utilita"

    Private Sub RiempiLista(lista As WGMenuLista, canali As List(Of TvCanaliDb.CanaleTv))
        Try
            If lista Is Nothing OrElse canali Is Nothing Then Return

            lista.BeginUpdate()
            For i As Integer = 0 To canali.Count - 1
                lista.aggiungiElemento(CreaEtichetta(canali(i)), canali(i).Id)
            Next
            lista.EndUpdate()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.RiempiLista: " & ex.ToString())
        End Try
    End Sub

    Private Function CreaEtichetta(c As TvCanaliDb.CanaleTv) As String
        Try
            If c Is Nothing Then Return "Canale"

            Dim nome As String = "Canale senza nome"
            If Not String.IsNullOrWhiteSpace(c.Nome) Then nome = c.Nome.Trim()

            If c.Numero > 0 Then Return c.Numero.ToString() & ", " & nome

            Return nome
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.CreaEtichetta: " & ex.Message)
            Return "Canale"
        End Try
    End Function

    Private Sub ChiudiListeSecondarie()
        Try
            If _listaPreferiti IsNot Nothing Then
                Try
                    _listaPreferiti.abbandona()
                Catch exA As Exception
                    _logger.ScriviLog("MenuTv.ChiudiListeSecondarie preferiti: " & exA.Message)
                End Try
                _listaPreferiti = Nothing
            End If
            If _listaRecenti IsNot Nothing Then
                Try
                    _listaRecenti.abbandona()
                Catch exB As Exception
                    _logger.ScriviLog("MenuTv.ChiudiListeSecondarie recenti: " & exB.Message)
                End Try
                _listaRecenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ChiudiListeSecondarie: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiLista()
        Try
            If _lista Is Nothing Then Return
            Try
                _lista.abbandona()
            Catch exAbb As Exception
                _logger.ScriviLog("MenuTv.ChiudiLista abbandona: " & exAbb.Message)
            End Try
            _lista = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ChiudiLista: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiTutteLeListe()
        Try
            ChiudiListaProgrammi()
            ChiudiLista()
            ChiudiListeSecondarie()
        Catch ex As Exception
            _logger.ScriviLog("MenuTv.ChiudiTutteLeListe: " & ex.Message)
        End Try
    End Sub

#End Region

End Class