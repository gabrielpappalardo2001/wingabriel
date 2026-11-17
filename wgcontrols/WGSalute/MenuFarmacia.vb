Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGOcr
''' <summary>
''' Menu accessibile per consultare i farmaci AIFA, leggere il bugiardino
''' e trovare le farmacie aperte.
''' </summary>
Public Class MenuFarmacia

    Private Const ID_CAMPO_RICERCA As String = "ricercaFarmaco"
    Private Const ID_CAMPO_COMUNE As String = "comuneFarmacie"

    Private ReadOnly _db As CLSFarmacie = CLSFarmacie.GetInstance()
    Private ReadOnly _dati As CLSDatabase = CLSDatabase.GetInstance()
    Private ReadOnly _logger As New Logger()

    Private WithEvents _listaRisultati As WGMenuLista
    Private WithEvents _listaInformazioni As WGMenuLista
    Private WithEvents _listaConfezioni As WGMenuLista
    Private WithEvents _listaFarmacie As WGMenuLista
    Private WithEvents _listaDettaglioFarmacia As WGMenuLista
    Private WithEvents _listaRicercheRecenti As WGMenuLista
    Private WithEvents _listaDocumentiScaricati As WGMenuLista
    Private WithEvents _listaSezioniFi As WGMenuLista

    Private _menuPrincipale As WGMenu
    Private _menuFarmaco As WGMenu
    Private _menuOpzioni As WGMenu
    Private _menuRicerca As WGMenu
    Private _campi As Campi
    Private _campoDocumento As WGCampo

    Private _processo As WGProcessoInCorso
    Private _ultimaRicerca As String = ""
    Private _ultimoComune As String = ""
    Private _farmacoCorrente As CLSFarmacie.Farmaco
    Private _soloAperte As Boolean = True
    Private _farmacieCorrenti As New List(Of CLSFarmacie.Farmacia)
    Private _documentiScaricatiCorrenti As New List(Of CLSFarmacie.StampatoScaricato)
    Private _elencoFarmacieOperazione As List(Of CLSFarmacie.Farmacia)
    Private _sezioniFiCorrenti As List(Of CLSFarmacie.SezioneFoglioIllustrativo)

    Private _esitoOperazione As Boolean
    Private _numeroOperazione As Integer
    Private _testoOperazione As String = ""
    Private _tipoStampatoRichiesto As String = "FI"
    Private _tipoStampatoOttenuto As String = "FI"
    Private _apriPdfDopoDownload As Boolean
    Private _mappaCopiaDettaglioFarmacia As Dictionary(Of String, Tuple(Of String, String))
    Private _opzioneCopiaDettaglioFarmacia As String = ""

#Region "Avvio"

    Public Sub Avvia()
        Try
            Task.Run(New Action(AddressOf PreparaInBackground))
            MostraMenuPrincipale()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.Avvia: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire la sezione dei farmaci.")
        End Try
    End Sub

    Private Sub PreparaInBackground()
        Try
            If _db.NumeroFarmaci() > 0 Then
                _db.AggiornaAnagrafica(False, Nothing)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.PreparaInBackground: " & ex.ToString())
        End Try
    End Sub

    Private Function AssicuraArchivio() As Boolean
        Try
            If _db.NumeroFarmaci() > 0 Then Return True

            EseguiConAttesa("Scarico per la prima volta l'archivio dei farmaci AIFA, attendi", New Action(AddressOf OperazioneAggiornaArchivioForzato))

            If Not _esitoOperazione OrElse _db.NumeroFarmaci() = 0 Then
                MostraMessaggio("Non sono riuscito a scaricare l'archivio dei farmaci. Controlla la connessione a internet e riprova.")
                Return False
            End If
            Return True
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.AssicuraArchivio: " & ex.ToString())
            Return False
        End Try
    End Function

#End Region

#Region "Esecuzione con finestra di attesa"

    Private Sub EseguiConAttesa(messaggio As String, operazione As Action)
        Try
            If operazione Is Nothing Then Return

            _processo = New WGProcessoInCorso()
            _processo.Avvia(messaggio, False)

            Dim attivita As Task = Task.Run(operazione)

            While Not attivita.IsCompleted
                Try
                    Application.DoEvents()
                    Thread.Sleep(30)
                Catch exAttesa As Exception
                    _logger.ScriviLog("MenuFarmacia.EseguiConAttesa ciclo: " & exAttesa.Message)
                End Try
            End While

            If attivita.IsFaulted AndAlso attivita.Exception IsNot Nothing Then
            End If

            Try
                _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.EseguiConAttesa: " & ex.ToString())
            Try
                If _processo IsNot Nothing Then _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        End Try
    End Sub

    Private Sub ScriviAvanzamento(testo As String)
        Try
            If _processo Is Nothing Then Return
            If String.IsNullOrWhiteSpace(testo) Then Return
            _processo.AggiornaStato(testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ScriviAvanzamento: " & ex.Message)
        End Try
    End Sub

    Private Sub OperazioneAggiornaArchivioForzato()
        Try
            _esitoOperazione = _db.AggiornaAnagrafica(True, New CLSFarmacie.Avanzamento(AddressOf ScriviAvanzamento))
            _numeroOperazione = _db.NumeroFarmaci()
        Catch ex As Exception
            _esitoOperazione = False
            _logger.ScriviLog("MenuFarmacia.OperazioneAggiornaArchivioForzato: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneAggiornaFarmacie()
        Try
            _numeroOperazione = _db.AggiornaElencoFarmacie(New CLSFarmacie.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = (_numeroOperazione > 0)
        Catch ex As Exception
            _esitoOperazione = False
            _logger.ScriviLog("MenuFarmacia.OperazioneAggiornaFarmacie: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneCercaFarmacieComune()
        Try
            _elencoFarmacieOperazione = _db.OttieniFarmacieComune(_ultimoComune, _soloAperte, New CLSFarmacie.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = True
        Catch ex As Exception
            _esitoOperazione = False
            _elencoFarmacieOperazione = Nothing
            _logger.ScriviLog("MenuFarmacia.OperazioneCercaFarmacieComune: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneScaricaStampato()
        Try
            _testoOperazione = ""
            If _farmacoCorrente Is Nothing Then
                _esitoOperazione = False
                Return
            End If

            _esitoOperazione = _db.AssicuraStampato(_farmacoCorrente.Aic6, _tipoStampatoRichiesto, New CLSFarmacie.Avanzamento(AddressOf ScriviAvanzamento))
            If Not _esitoOperazione Then Return

            If _apriPdfDopoDownload Then
                _testoOperazione = _db.OttieniPercorsoPdf(_farmacoCorrente.Aic6, _tipoStampatoRichiesto)
            Else
                _testoOperazione = _db.OttieniTestoStampato(_farmacoCorrente.Aic6, _tipoStampatoRichiesto)
            End If
        Catch ex As Exception
            _esitoOperazione = False
            _logger.ScriviLog("MenuFarmacia.OperazioneScaricaStampato: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneScaricaFoglioOFallback()
        Try
            _testoOperazione = ""
            _tipoStampatoOttenuto = "FI"
            If _farmacoCorrente Is Nothing Then
                _esitoOperazione = False
                Return
            End If

            Dim tipoOttenuto As String = "FI"
            _esitoOperazione = _db.AssicuraFoglioIllustrativoOFallback(_farmacoCorrente.Aic6, tipoOttenuto, New CLSFarmacie.Avanzamento(AddressOf ScriviAvanzamento))
            _tipoStampatoOttenuto = tipoOttenuto
            If Not _esitoOperazione Then Return

            _testoOperazione = _db.OttieniTestoStampato(_farmacoCorrente.Aic6, tipoOttenuto)
        Catch ex As Exception
            _esitoOperazione = False
            _logger.ScriviLog("MenuFarmacia.OperazioneScaricaFoglioOFallback: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Menu principale"

    Private Sub MostraMenuPrincipale()
        Try
            ChiudiListe()
            ChiudiCampoDocumento()

            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Cerca un farmaco", AddressOf VoceCercaFarmaco)
            _menuPrincipale.aggiungi("Farmaci preferiti", AddressOf VoceFarmaciPreferiti)
            _menuPrincipale.aggiungi("Farmaci consultati di recente", AddressOf VoceFarmaciRecenti)
            _menuPrincipale.aggiungi("Farmacie aperte adesso", AddressOf VoceFarmacieAperte)
            _menuPrincipale.aggiungi("Tutte le farmacie di un comune", AddressOf VoceFarmacieComune)
            _menuPrincipale.aggiungi("Bugiardini scaricati", AddressOf VoceBugiardiniScaricati)
            _menuPrincipale.aggiungi("Opzioni", AddressOf VoceOpzioni)
            _menuPrincipale.avvia("salute")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraMenuPrincipale: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceOpzioni(sender As Object, e As EventArgs)
        Try
            MostraMenuOpzioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceOpzioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMenuOpzioni()
        Try
            _menuOpzioni = New WGMenu()
            _menuOpzioni.aggiungi("Guida ai termini usati in questa sezione", AddressOf VoceGuidaTermini)
            _menuOpzioni.aggiungi("Aggiorna l'archivio dei farmaci", AddressOf VoceAggiornaArchivio)
            _menuOpzioni.aggiungi("Aggiorna l'elenco delle farmacie", AddressOf VoceAggiornaFarmacie)
            _menuOpzioni.aggiungi("Cancella le ricerche recenti", AddressOf VoceCancellaRicercheRecenti)
            _menuOpzioni.avvia("opzioni")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraMenuOpzioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceGuidaTermini(sender As Object, e As EventArgs)
        Try
            MostraGuida()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceGuidaTermini: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraGuida()
        Try
            Dim testo As String = _db.TestoGuida()
            If String.IsNullOrWhiteSpace(testo) Then
                MostraMessaggio("La guida non e' disponibile al momento.")
                Return
            End If

            ChiudiCampoDocumento()

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init("Guida ai termini dei farmaci", testo, True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraGuida: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire la guida.")
        End Try
    End Sub

    Private Sub VoceCercaFarmaco(sender As Object, e As EventArgs)
        Try
            If Not AssicuraArchivio() Then Return
            MostraMenuRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceCercaFarmaco: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMenuRicerca()
        Try
            _menuRicerca = New WGMenu()
            _menuRicerca.aggiungi("Nuova ricerca", AddressOf VoceNuovaRicerca)
            _menuRicerca.aggiungi("Ricerche recenti", AddressOf VoceRicercheRecenti)
            _menuRicerca.avvia("ricerca farmaci")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraMenuRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceNuovaRicerca(sender As Object, e As EventArgs)
        Try
            MostraCampoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceNuovaRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceRicercheRecenti(sender As Object, e As EventArgs)
        Try
            Dim elenco = _dati.OttieniRicercheRecenti()
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggio("Non ci sono ancora ricerche recenti.")
                Return
            End If

            ChiudiListe()
            ChiudiCampoDocumento()

            _listaRicercheRecenti = New WGMenuLista()
            _listaRicercheRecenti.ciclono = True
            _listaRicercheRecenti.selezioneMenu = True
            _listaRicercheRecenti.multiselezioneConsentita = False

            _listaRicercheRecenti.BeginUpdate()
            For Each testo In elenco
                Try
                    _listaRicercheRecenti.aggiungiElemento(testo, testo)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.VoceRicercheRecenti elemento: " & exElemento.Message)
                End Try
            Next
            _listaRicercheRecenti.EndUpdate()

            _listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            _listaRicercheRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRicercheRecenti_ElementoSelezionato(id As String) Handles _listaRicercheRecenti.ElementoSelezionato
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaRicercheRecenti)
            If String.IsNullOrWhiteSpace(identificativo) Then identificativo = id
            ChiudiListaRicercheRecenti()

            If String.IsNullOrWhiteSpace(identificativo) Then Return
            _ultimaRicerca = identificativo.Trim()
            EseguiRicerca(_ultimaRicerca)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaRicercheRecenti_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRicercheRecenti_escPremuto(sender As Object, e As EventArgs) Handles _listaRicercheRecenti.escPremuto
        Try
            ChiudiListaRicercheRecenti()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaRicercheRecenti_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceAggiornaArchivio(sender As Object, e As EventArgs)
        Try
            EseguiConAttesa("Aggiorno l'archivio dei farmaci AIFA, attendi", New Action(AddressOf OperazioneAggiornaArchivioForzato))

            If _esitoOperazione Then
                MostraMessaggio("Archivio aggiornato.")
            Else
                MostraMessaggio("Aggiornamento non riuscito. Continuo a usare l'archivio gia' presente sul computer, se disponibile.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceAggiornaArchivio: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceAggiornaFarmacie(sender As Object, e As EventArgs)
        Try
            EseguiConAttesa("Aggiorno l'elenco nazionale delle farmacie, attendi", New Action(AddressOf OperazioneAggiornaFarmacie))

            If _esitoOperazione Then
                MostraMessaggio("Elenco farmacie aggiornato.")
            Else
                MostraMessaggio("Non sono riuscito ad aggiornare l'elenco delle farmacie.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceAggiornaFarmacie: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceCancellaRicercheRecenti(sender As Object, e As EventArgs)
        Try
            _dati.CancellaRicercheRecenti()
            MostraMessaggio("Le ricerche recenti sono state cancellate.")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceCancellaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Campo di ricerca"

    Private Sub MostraCampoRicerca()
        Try
            RilasciaCampi()

            _campi = New Campi()
            _campi.Aggiungi("Nome, principio attivo, azienda o codice AIC", ID_CAMPO_RICERCA, True, False, _ultimaRicerca)

            AddHandler _campi.Fine, AddressOf RicercaConfermata
            AddHandler _campi.Chiusura, AddressOf RicercaAnnullata

            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraCampoRicerca: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il campo di ricerca.")
        End Try
    End Sub

    Private Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            Dim testo As String = LeggiValoreCampo(ID_CAMPO_RICERCA)
            RilasciaCampi()

            If String.IsNullOrWhiteSpace(testo) Then Return

            _ultimaRicerca = testo.Trim()
            EseguiRicerca(_ultimaRicerca)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.RicercaConfermata: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaAnnullata(sender As Object, e As EventArgs)
        Try
            RilasciaCampi()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.RicercaAnnullata: " & ex.ToString())
        End Try
    End Sub

    Private Function LeggiValoreCampo(identificativo As String) As String
        Try
            If _campi Is Nothing Then Return ""
            Dim contenutoCampo As String = _campi.RecuperaValore(identificativo)
            If contenutoCampo Is Nothing Then Return ""
            Return contenutoCampo.Trim()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.LeggiValoreCampo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RilasciaCampi()
        Try
            If _campi Is Nothing Then Return
            Try
                RemoveHandler _campi.Fine, AddressOf RicercaConfermata
                RemoveHandler _campi.Chiusura, AddressOf RicercaAnnullata
            Catch
            End Try
            Try
                RemoveHandler _campi.Fine, AddressOf ComuneConfermato
                RemoveHandler _campi.Chiusura, AddressOf ComuneAnnullato
            Catch
            End Try
            Try
                
            Catch
            End Try
            _campi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.RilasciaCampi: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Risultati della ricerca"

    Private Sub EseguiRicerca(testo As String)
        Try
            Dim risultati As List(Of CLSFarmacie.Farmaco) = _db.Cerca(testo)

            _dati.RegistraRicerca(testo)

            If risultati Is Nothing OrElse risultati.Count = 0 Then
                MostraMessaggio("Nessun farmaco trovato per " & testo & ".")
                Return
            End If

            ChiudiListe()
            ChiudiCampoDocumento()

            Dim conteggioNomi As Dictionary(Of String, Integer) = ContaNomiDuplicati(risultati)

            _listaRisultati = New WGMenuLista()
            _listaRisultati.ciclono = True
            _listaRisultati.selezioneMenu = True
            _listaRisultati.multiselezioneConsentita = False

            _listaRisultati.BeginUpdate()
            For Each f In risultati
                Try
                    Dim chiaveNome As String = If(f.Nome, "").Trim().ToUpperInvariant()
                    Dim ambiguo As Boolean = conteggioNomi.ContainsKey(chiaveNome) AndAlso conteggioNomi(chiaveNome) > 1
                    _listaRisultati.aggiungiElemento(EtichettaFarmaco(f, ambiguo), f.Aic6)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.EseguiRicerca elemento: " & exElemento.Message)
                End Try
            Next
            _listaRisultati.EndUpdate()

            AggiungiOpzioniListaFarmaci(_listaRisultati)

            _listaRisultati.impostaTitolo("Risultati per " & testo)
            _listaRisultati.avvia()

            AggiornaOpzionePreferitoLista(_listaRisultati, IdentificativoSelezionato(_listaRisultati))
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiOpzioniListaFarmaci(lista As WGMenuLista)
        Try
            If lista Is Nothing Then Return
            lista.aggiungiOpzione("Informazioni", AddressOf OpzioneInformazioni)
            lista.aggiungiOpzione("Leggi il bugiardino", AddressOf OpzioneLeggiBugiardino)
            lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoLista)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.AggiungiOpzioniListaFarmaci: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaOpzionePreferitoLista(lista As WGMenuLista, aic6 As String)
        Try
            If lista Is Nothing OrElse lista.IsDisposed Then Return
            If String.IsNullOrWhiteSpace(aic6) Then Return

            Dim ePreferito As Boolean = _dati.EPreferito(aic6)

            If ePreferito Then
                If lista.opzioneEsiste("Aggiungi ai preferiti") Then
                    lista.sostituisciOpzione("Aggiungi ai preferiti", "Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferitoLista)
                End If
            Else
                If lista.opzioneEsiste("Rimuovi dai preferiti") Then
                    lista.sostituisciOpzione("Rimuovi dai preferiti", "Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoLista)
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.AggiornaOpzionePreferitoLista: " & ex.Message)
        End Try
    End Sub

    Private Sub _listaRisultati_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaRisultati.VoceCambiata
        Try
            AggiornaOpzionePreferitoLista(_listaRisultati, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaRisultati_VoceCambiata: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneAggiungiPreferitoLista(sender As Object, e As EventArgs)
        Try
            Dim f = FarmacoSelezionato()
            If f Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If
            _dati.AggiungiPreferito(f.Aic6, f.Nome)
            AggiornaOpzionePreferitoLista(_listaRisultati, f.Aic6)
            MostraMessaggio(Valore(f.Nome) & " aggiunto ai farmaci preferiti.")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneAggiungiPreferitoLista: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneRimuoviPreferitoLista(sender As Object, e As EventArgs)
        Try
            Dim f = FarmacoSelezionato()
            If f Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If
            _dati.RimuoviPreferito(f.Aic6)
            AggiornaOpzionePreferitoLista(_listaRisultati, f.Aic6)
            MostraMessaggio(Valore(f.Nome) & " rimosso dai farmaci preferiti.")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneRimuoviPreferitoLista: " & ex.ToString())
        End Try
    End Sub

    Private Function ContaNomiDuplicati(risultati As List(Of CLSFarmacie.Farmaco)) As Dictionary(Of String, Integer)
        Dim conteggio As New Dictionary(Of String, Integer)
        Try
            If risultati Is Nothing Then Return conteggio
            For Each f In risultati
                Try
                    If f Is Nothing Then Continue For
                    Dim chiave As String = If(f.Nome, "").Trim().ToUpperInvariant()
                    If chiave.Length = 0 Then Continue For
                    If conteggio.ContainsKey(chiave) Then
                        conteggio(chiave) = conteggio(chiave) + 1
                    Else
                        conteggio(chiave) = 1
                    End If
                Catch
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ContaNomiDuplicati: " & ex.Message)
        End Try
        Return conteggio
    End Function

    Private Function EtichettaFarmaco(f As CLSFarmacie.Farmaco, Optional ambiguo As Boolean = False) As String
        Try
            If f Is Nothing Then Return "Farmaco"
            Dim nome As String = If(String.IsNullOrWhiteSpace(f.Nome), "Farmaco senza nome", f.Nome.Trim())
            Dim etichetta As New StringBuilder()
            etichetta.Append(nome)

            If Not String.IsNullOrWhiteSpace(f.PrincipioAttivo) Then
                etichetta.Append(", ").Append(f.PrincipioAttivo)
            End If

            If Not String.IsNullOrWhiteSpace(f.RegimeFornitura) Then
                etichetta.Append(", ").Append(f.RegimeFornituraLeggibile)
            End If

            If ambiguo Then
                If Not String.IsNullOrWhiteSpace(f.Azienda) Then
                    etichetta.Append(", azienda ").Append(f.Azienda)
                End If
                etichetta.Append(", codice AIC ").Append(Valore(f.Aic6))
            End If

            If f.Carente Then
                etichetta.Append(", temporaneamente carente")
            End If

            Return etichetta.ToString()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.EtichettaFarmaco: " & ex.Message)
            Return "Farmaco"
        End Try
    End Function

    Private Function RisolviFarmaci(riferimenti As List(Of CLSDatabase.RiferimentoFarmaco)) As List(Of CLSFarmacie.Farmaco)
        Dim elenco As New List(Of CLSFarmacie.Farmaco)
        Try
            If riferimenti Is Nothing Then Return elenco
            For Each r In riferimenti
                Try
                    Dim f = _db.TrovaPerAic6(r.Aic6)
                    If f IsNot Nothing Then elenco.Add(f)
                Catch exSingolo As Exception
                    _logger.ScriviLog("MenuFarmacia.RisolviFarmaci elemento: " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.RisolviFarmaci: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Function IdentificativoSelezionato(lista As WGMenuLista) As String
        Try
            If lista Is Nothing OrElse lista.IsDisposed Then Return ""
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                If Not String.IsNullOrWhiteSpace(elementi(0)) Then Return elementi(0).Trim()
            End If
            Dim singolo As String = lista.ottieniElemento()
            If singolo Is Nothing Then Return ""
            Return singolo.Trim()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.IdentificativoSelezionato: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function FarmacoSelezionato() As CLSFarmacie.Farmaco
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaRisultati)
            If String.IsNullOrWhiteSpace(identificativo) Then Return _farmacoCorrente
            Dim f = _db.TrovaPerAic6(identificativo)
            If f IsNot Nothing Then Return f
            Return _farmacoCorrente
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.FarmacoSelezionato: " & ex.ToString())
            Return _farmacoCorrente
        End Try
    End Function

    Private Sub _listaRisultati_ElementoSelezionato(id As String) Handles _listaRisultati.ElementoSelezionato
        Try
            Dim f = FarmacoSelezionato()
            If f Is Nothing AndAlso Not String.IsNullOrWhiteSpace(id) Then
                f = _db.TrovaPerAic6(id)
            End If
            If f Is Nothing Then
                MostraMessaggio("Non riesco a leggere i dati di questo farmaco.")
                Return
            End If
            _farmacoCorrente = f
            MostraMenuFarmaco()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaRisultati_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_escPremuto(sender As Object, e As EventArgs) Handles _listaRisultati.escPremuto
        Try
            ChiudiListe()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaRisultati_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneInformazioni(sender As Object, e As EventArgs)
        Try
            Dim f = FarmacoSelezionato()
            If f Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If
            _farmacoCorrente = f
            MostraInformazioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneInformazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneLeggiBugiardino(sender As Object, e As EventArgs)
        Try
            Dim f = FarmacoSelezionato()
            If f Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If
            _farmacoCorrente = f
            ApriStampato("FI", False)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneLeggiBugiardino: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceFarmaciPreferiti(sender As Object, e As EventArgs)
        Try
            If Not AssicuraArchivio() Then Return

            Dim elenco = RisolviFarmaci(_dati.OttieniPreferiti())
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggio("Non hai ancora farmaci preferiti. Puoi aggiungerne uno dalle opzioni dopo una ricerca.")
                Return
            End If

            ChiudiListe()
            ChiudiCampoDocumento()

            _listaRisultati = New WGMenuLista()
            _listaRisultati.ciclono = True
            _listaRisultati.selezioneMenu = True
            _listaRisultati.multiselezioneConsentita = False

            _listaRisultati.BeginUpdate()
            For Each f In elenco
                Try
                    _listaRisultati.aggiungiElemento(EtichettaFarmaco(f), f.Aic6)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.VoceFarmaciPreferiti elemento: " & exElemento.Message)
                End Try
            Next
            _listaRisultati.EndUpdate()

            AggiungiOpzioniListaFarmaci(_listaRisultati)

            _listaRisultati.impostaTitolo("Farmaci preferiti")
            _listaRisultati.avvia()

            AggiornaOpzionePreferitoLista(_listaRisultati, IdentificativoSelezionato(_listaRisultati))
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceFarmaciPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceFarmaciRecenti(sender As Object, e As EventArgs)
        Try
            If Not AssicuraArchivio() Then Return

            Dim elenco = RisolviFarmaci(_dati.OttieniRecenti())
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggio("Non hai ancora consultato farmaci di recente.")
                Return
            End If

            ChiudiListe()
            ChiudiCampoDocumento()

            _listaRisultati = New WGMenuLista()
            _listaRisultati.ciclono = True
            _listaRisultati.selezioneMenu = True
            _listaRisultati.multiselezioneConsentita = False

            _listaRisultati.BeginUpdate()
            For Each f In elenco
                Try
                    _listaRisultati.aggiungiElemento(EtichettaFarmaco(f), f.Aic6)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.VoceFarmaciRecenti elemento: " & exElemento.Message)
                End Try
            Next
            _listaRisultati.EndUpdate()

            AggiungiOpzioniListaFarmaci(_listaRisultati)

            _listaRisultati.impostaTitolo("Farmaci consultati di recente")
            _listaRisultati.avvia()

            AggiornaOpzionePreferitoLista(_listaRisultati, IdentificativoSelezionato(_listaRisultati))
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceFarmaciRecenti: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Menu del singolo farmaco"

    Private Sub MostraMenuFarmaco()
        Try
            If _farmacoCorrente Is Nothing Then Return

            _dati.RegistraRecente(_farmacoCorrente.Aic6, _farmacoCorrente.Nome)

            _menuFarmaco = New WGMenu()
            _menuFarmaco.aggiungi("Informazioni", AddressOf VoceInformazioni)
            _menuFarmaco.aggiungi("Leggi il bugiardino", AddressOf VoceLeggiBugiardino)
            _menuFarmaco.aggiungi("Foglio illustrativo per sezioni: a cosa serve, posologia, effetti", AddressOf VoceSezioniFoglioIllustrativo)
            _menuFarmaco.aggiungi("Leggi il riassunto delle caratteristiche del prodotto", AddressOf VoceLeggiRcp)
            _menuFarmaco.aggiungi("Confezioni e prezzi", AddressOf VoceConfezioni)

            If _dati.EPreferito(_farmacoCorrente.Aic6) Then
                _menuFarmaco.aggiungi("Rimuovi dai preferiti", AddressOf VoceRimuoviPreferito)
            Else
                _menuFarmaco.aggiungi("Aggiungi ai preferiti", AddressOf VoceAggiungiPreferito)
            End If

            _menuFarmaco.avvia(Valore(_farmacoCorrente.Nome))
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraMenuFarmaco: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceAggiungiPreferito(sender As Object, e As EventArgs)
        Try
            If _farmacoCorrente Is Nothing Then Return
            _dati.AggiungiPreferito(_farmacoCorrente.Aic6, _farmacoCorrente.Nome)
            MostraMessaggio(Valore(_farmacoCorrente.Nome) & " aggiunto ai farmaci preferiti.")
            MostraMenuFarmaco()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceAggiungiPreferito: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceRimuoviPreferito(sender As Object, e As EventArgs)
        Try
            If _farmacoCorrente Is Nothing Then Return
            _dati.RimuoviPreferito(_farmacoCorrente.Aic6)
            MostraMessaggio(Valore(_farmacoCorrente.Nome) & " rimosso dai farmaci preferiti.")
            MostraMenuFarmaco()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceRimuoviPreferito: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceInformazioni(sender As Object, e As EventArgs)
        Try
            MostraInformazioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceInformazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceLeggiBugiardino(sender As Object, e As EventArgs)
        Try
            ApriStampato("FI", False)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceLeggiBugiardino: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceLeggiRcp(sender As Object, e As EventArgs)
        Try
            ApriStampato("RCP", False)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceLeggiRcp: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceSezioniFoglioIllustrativo(sender As Object, e As EventArgs)
        Try
            If _farmacoCorrente Is Nothing Then Return

            EseguiConAttesa("Preparo il foglio illustrativo di " & Valore(_farmacoCorrente.Nome) & ", attendi", New Action(AddressOf OperazioneScaricaFoglioOFallback))

            If Not _esitoOperazione Then
                MostraMessaggio("Non ho trovato né il foglio illustrativo né il riassunto delle caratteristiche del prodotto di " & Valore(_farmacoCorrente.Nome) & ". L'AIFA potrebbe non averli pubblicati per questo farmaco, oppure il sistema dell'AIFA non è raggiungibile in questo momento.")
                Return
            End If

            If String.IsNullOrWhiteSpace(_testoOperazione) Then
                MostraMessaggio("Il documento e' stato scaricato ma non contiene testo leggibile.")
                Return
            End If

            Dim usatoRcp As Boolean = String.Equals(_tipoStampatoOttenuto, "RCP", StringComparison.OrdinalIgnoreCase)

            Dim sezioni = _db.EstraiSezioniFoglioIllustrativo(_testoOperazione)
            If sezioni Is Nothing OrElse sezioni.Count = 0 Then
                If usatoRcp Then
                    MostraMessaggio("Il foglio illustrativo di " & Valore(_farmacoCorrente.Nome) & " non e' disponibile. Ti mostro al suo posto il riassunto delle caratteristiche del prodotto, un documento piu' tecnico per medici e farmacisti, che pero' contiene le stesse informazioni di base.")
                    MostraDocumentoTesto(NomeDocumento("RCP"), _testoOperazione)
                Else
                    MostraMessaggio("Il foglio illustrativo e' disponibile ma non sono riuscito a riconoscerne le sezioni. Te lo apro per intero.")
                    MostraDocumentoTesto(NomeDocumento("FI"), _testoOperazione)
                End If
                Return
            End If

            If usatoRcp Then
                MostraMessaggio("Il foglio illustrativo di " & Valore(_farmacoCorrente.Nome) & " non e' disponibile. Ti mostro le sezioni del riassunto delle caratteristiche del prodotto, un documento piu' tecnico ma con le stesse informazioni di base.")
            End If

            MostraElencoSezioniFi(sezioni)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceSezioniFoglioIllustrativo: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraElencoSezioniFi(sezioni As List(Of CLSFarmacie.SezioneFoglioIllustrativo))
        Try
            _sezioniFiCorrenti = sezioni

            ChiudiListe()
            ChiudiCampoDocumento()

            _listaSezioniFi = New WGMenuLista()
            _listaSezioniFi.ciclono = True
            _listaSezioniFi.selezioneMenu = True
            _listaSezioniFi.multiselezioneConsentita = False

            _listaSezioniFi.BeginUpdate()
            For Each s In sezioni
                Try
                    _listaSezioniFi.aggiungiElemento(s.Numero.ToString() & ". " & s.Titolo, "sez" & s.Numero.ToString())
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.MostraElencoSezioniFi elemento: " & exElemento.Message)
                End Try
            Next
            _listaSezioniFi.EndUpdate()

            _listaSezioniFi.impostaTitolo("Foglio illustrativo di " & Valore(_farmacoCorrente.Nome))
            _listaSezioniFi.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraElencoSezioniFi: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaSezioniFi_ElementoSelezionato(id As String) Handles _listaSezioniFi.ElementoSelezionato
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaSezioniFi)
            If String.IsNullOrWhiteSpace(identificativo) Then identificativo = id
            If String.IsNullOrWhiteSpace(identificativo) Then Return

            Dim numero As Integer = 0
            If Not Integer.TryParse(identificativo.Replace("sez", ""), numero) Then Return

            Dim sezione As CLSFarmacie.SezioneFoglioIllustrativo = Nothing
            If _sezioniFiCorrenti IsNot Nothing Then
                For Each s In _sezioniFiCorrenti
                    If s.Numero = numero Then
                        sezione = s
                        Exit For
                    End If
                Next
            End If

            If sezione Is Nothing Then Return

            ChiudiListaSezioniFi()
            MostraDocumentoTesto(sezione.Numero.ToString() & ". " & sezione.Titolo, sezione.Testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaSezioniFi_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaSezioniFi_escPremuto(sender As Object, e As EventArgs) Handles _listaSezioniFi.escPremuto
        Try
            ChiudiListaSezioniFi()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaSezioniFi_escPremuto: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Informazioni sul farmaco"

    Private Sub MostraInformazioni()
        Try
            If _farmacoCorrente Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If

            Dim f = _farmacoCorrente
            Dim righe As New List(Of String)

            righe.Add("Nome: " & Valore(f.Nome))
            If Not String.IsNullOrWhiteSpace(f.PrincipioAttivo) Then righe.Add("Principio attivo: " & f.PrincipioAttivo)

            If f.NumeroConfezioni > 0 Then righe.Add("Confezioni in commercio: " & f.NumeroConfezioni.ToString())
            If Not String.IsNullOrWhiteSpace(f.Azienda) Then righe.Add("Azienda: " & f.Azienda)
            If Not String.IsNullOrWhiteSpace(f.RegimeFornitura) Then righe.Add("Regime di fornitura: " & f.RegimeFornituraLeggibile)
            If Not String.IsNullOrWhiteSpace(f.Atc) Then
                If String.IsNullOrWhiteSpace(f.AtcDescrizione) Then
                    righe.Add("Categoria ATC: " & f.Atc)
                Else
                    righe.Add("Categoria ATC: " & f.Atc & ", " & f.AtcDescrizione)
                End If
            End If
            If Not String.IsNullOrWhiteSpace(f.Stato) Then righe.Add("Stato: " & f.StatoLeggibile)
            righe.Add("Codice AIC: " & Valore(f.Aic6))
            If f.Carente Then
                Dim nota As String = f.NoteCarenza
                If String.IsNullOrWhiteSpace(nota) Then
                    righe.Add("Attenzione: farmaco temporaneamente carente")
                Else
                    righe.Add("Attenzione: farmaco temporaneamente carente, " & nota)
                End If
            End If
            If _db.HaStampatoConTesto(f.Aic6, "FI") Then
                righe.Add("Bugiardino gia' scaricato sul computer")
            End If

            ChiudiListaInformazioni()

            _listaInformazioni = New WGMenuLista()
            _listaInformazioni.ciclono = True
            _listaInformazioni.selezioneMenu = True
            _listaInformazioni.multiselezioneConsentita = False

            _listaInformazioni.BeginUpdate()
            Dim progressivo As Integer = 0
            For Each riga In righe
                progressivo += 1
                _listaInformazioni.aggiungiElemento(riga, "info" & progressivo.ToString())
            Next
            _listaInformazioni.EndUpdate()

            _listaInformazioni.aggiungiOpzione("Leggi il bugiardino", AddressOf OpzioneInfoLeggiBugiardino)
            _listaInformazioni.aggiungiOpzione("Leggi il riassunto delle caratteristiche del prodotto", AddressOf OpzioneInfoLeggiRcp)
            _listaInformazioni.aggiungiOpzione("Confezioni e prezzi", AddressOf OpzioneInfoConfezioni)

            _listaInformazioni.impostaTitolo("Informazioni su " & Valore(f.Nome))
            _listaInformazioni.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraInformazioni: " & ex.ToString())
            MostraMessaggio("Non riesco a mostrare le informazioni di questo farmaco.")
        End Try
    End Sub

    Private Sub _listaInformazioni_escPremuto(sender As Object, e As EventArgs) Handles _listaInformazioni.escPremuto
        Try
            ChiudiListaInformazioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaInformazioni_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneInfoLeggiBugiardino(sender As Object, e As EventArgs)
        Try
            ApriStampato("FI", False)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneInfoLeggiBugiardino: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneInfoLeggiRcp(sender As Object, e As EventArgs)
        Try
            ApriStampato("RCP", False)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneInfoLeggiRcp: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneInfoConfezioni(sender As Object, e As EventArgs)
        Try
            MostraConfezioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneInfoConfezioni: " & ex.ToString())
        End Try
    End Sub

    Private Function Valore(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return "non disponibile"
            Return testo.Trim()
        Catch
            Return "non disponibile"
        End Try
    End Function

#End Region

#Region "Confezioni e prezzi"

    Private Sub VoceConfezioni(sender As Object, e As EventArgs)
        Try
            MostraConfezioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceConfezioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraConfezioni()
        Try
            If _farmacoCorrente Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If

            Dim confezioni = _db.OttieniConfezioni(_farmacoCorrente.Aic6)
            If confezioni Is Nothing OrElse confezioni.Count = 0 Then
                MostraMessaggio("Non ci sono confezioni registrate per questo farmaco.")
                Return
            End If

            ChiudiListaConfezioni()

            _listaConfezioni = New WGMenuLista()
            _listaConfezioni.ciclono = True
            _listaConfezioni.selezioneMenu = True
            _listaConfezioni.multiselezioneConsentita = False

            _listaConfezioni.BeginUpdate()
            For Each c In confezioni
                Try
                    Dim etichetta As New StringBuilder()
                    Dim descrizioneConfezione As String = If(String.IsNullOrWhiteSpace(c.DescrizioneLeggibile), Valore(c.Descrizione), c.DescrizioneLeggibile)
                    etichetta.Append(descrizioneConfezione)
                    If Not String.IsNullOrWhiteSpace(c.RegimeFornitura) Then
                        etichetta.Append(", ").Append(c.RegimeFornituraLeggibile)
                    End If
                    _listaConfezioni.aggiungiElemento(etichetta.ToString(), c.Aic9)
                Catch exConfezione As Exception
                    _logger.ScriviLog("MenuFarmacia.MostraConfezioni elemento: " & exConfezione.Message)
                End Try
            Next
            _listaConfezioni.EndUpdate()

            _listaConfezioni.impostaTitolo("Confezioni di " & Valore(_farmacoCorrente.Nome))
            _listaConfezioni.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraConfezioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaConfezioni_escPremuto(sender As Object, e As EventArgs) Handles _listaConfezioni.escPremuto
        Try
            ChiudiListaConfezioni()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaConfezioni_escPremuto: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Bugiardino e RCP"

    Private Sub ApriStampato(tipo As String, Optional apriPdf As Boolean = False)
        Try
            If _farmacoCorrente Is Nothing Then
                MostraMessaggio("Nessun farmaco selezionato.")
                Return
            End If

            Dim tipoNorm As String = If(tipo, "FI").Trim().ToUpperInvariant()
            If tipoNorm <> "FI" AndAlso tipoNorm <> "RCP" Then tipoNorm = "FI"

            Dim descrizioneDocumento As String = NomeDocumento(tipoNorm)
            _tipoStampatoRichiesto = tipoNorm
            _apriPdfDopoDownload = apriPdf

            EseguiConAttesa("Scarico il " & descrizioneDocumento & " di " & Valore(_farmacoCorrente.Nome) & ", attendi", New Action(AddressOf OperazioneScaricaStampato))

            If Not _esitoOperazione Then
                MostraMessaggio("Il " & descrizioneDocumento & " di " & Valore(_farmacoCorrente.Nome) & " non e' disponibile. L'AIFA potrebbe non averlo pubblicato per questo farmaco, oppure il sistema AIFA non e' raggiungibile in questo momento.")
                Return
            End If

            If apriPdf Then
                Dim percorsoPdf As String = _testoOperazione
                If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then
                    percorsoPdf = _db.OttieniPercorsoPdf(_farmacoCorrente.Aic6, tipoNorm)
                End If
                If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then
                    MostraMessaggio("Il file PDF non e' disponibile sul computer.")
                    Return
                End If
                ApriDocumentoPdf(percorsoPdf, descrizioneDocumento)
                Return
            End If

            If String.IsNullOrWhiteSpace(_testoOperazione) Then
                Dim percorsoAlternativo As String = _db.OttieniPercorsoPdf(_farmacoCorrente.Aic6, tipoNorm)
                If Not String.IsNullOrWhiteSpace(percorsoAlternativo) AndAlso File.Exists(percorsoAlternativo) Then
                    MostraMessaggio("Il documento e' stato scaricato ma non contiene testo leggibile, nemmeno con il riconoscimento OCR. Te lo apro come file PDF.")
                    ApriDocumentoPdf(percorsoAlternativo, descrizioneDocumento)
                    Return
                End If
                MostraMessaggio("Il " & descrizioneDocumento & " di " & Valore(_farmacoCorrente.Nome) & " non è disponibile: non sono riuscito a trovarlo né a scaricarlo.")
                Return
            End If

            MostraDocumentoTesto(descrizioneDocumento, _testoOperazione)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ApriStampato: " & ex.ToString())
            MostraMessaggio("Errore durante l'apertura del documento.")
        End Try
    End Sub

    Private Function NomeDocumento(tipo As String) As String
        Try
            If String.Equals(tipo, "RCP", StringComparison.OrdinalIgnoreCase) Then
                Return "riassunto delle caratteristiche del prodotto"
            End If
            Return "bugiardino"
        Catch
            Return "documento"
        End Try
    End Function

    Private Sub MostraDocumentoTesto(descrizioneDocumento As String, testo As String)
        Try
            ChiudiCampoDocumento()

            Dim titolo As String = descrizioneDocumento
            If _farmacoCorrente IsNot Nothing Then
                titolo = descrizioneDocumento & " di " & Valore(_farmacoCorrente.Nome)
            End If

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.init(titolo, testo, True, True, False, False)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraDocumentoTesto: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il documento in lettura.")
        End Try
    End Sub

    Private Sub ApriDocumentoPdf(percorso As String, descrizioneDocumento As String)
        Try
            ChiudiCampoDocumento()

            Dim titolo As String = descrizioneDocumento
            If _farmacoCorrente IsNot Nothing Then
                titolo = descrizioneDocumento & " di " & Valore(_farmacoCorrente.Nome)
            End If

            _campoDocumento = New WGCampo()
            AddHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            _campoDocumento.ApriPdf(percorso, titolo, True)
            _campoDocumento.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ApriDocumentoPdf: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il file PDF.")
        End Try
    End Sub

    Private Sub CampoDocumento_Chiusura(sender As Object, e As EventArgs)
        Try
            ChiudiCampoDocumento()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.CampoDocumento_Chiusura: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Farmacie"

    Private Sub VoceFarmacieAperte(sender As Object, e As EventArgs)
        Try
            _soloAperte = True
            MostraCampoComune("Comune o zona in cui cercare le farmacie aperte")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceFarmacieAperte: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceFarmacieComune(sender As Object, e As EventArgs)
        Try
            _soloAperte = False
            MostraCampoComune("Comune di cui elencare tutte le farmacie")
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceFarmacieComune: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraCampoComune(etichetta As String)
        Try
            RilasciaCampi()

            _campi = New Campi()
            _campi.Aggiungi(etichetta, ID_CAMPO_COMUNE, True, False, _ultimoComune)

            AddHandler _campi.Fine, AddressOf ComuneConfermato
            AddHandler _campi.Chiusura, AddressOf ComuneAnnullato

            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraCampoComune: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il campo del comune.")
        End Try
    End Sub

    Private Sub ComuneConfermato(sender As Object, e As EventArgs)
        Try
            Dim comune As String = LeggiValoreCampo(ID_CAMPO_COMUNE)
            RilasciaCampi()

            If String.IsNullOrWhiteSpace(comune) Then Return
            _ultimoComune = comune.Trim()

            If _db.NumeroFarmacie() = 0 Then
                EseguiConAttesa("Scarico l'elenco nazionale delle farmacie, attendi", New Action(AddressOf OperazioneAggiornaFarmacie))
                If Not _esitoOperazione Then
                    MostraMessaggio("Non sono riuscito a scaricare l'elenco delle farmacie.")
                    Return
                End If
            End If

            Dim descrizioneRicerca As String = If(_soloAperte, "Cerco le farmacie aperte a " & _ultimoComune & ", attendi", "Cerco tutte le farmacie di " & _ultimoComune & ", attendi")
            EseguiConAttesa(descrizioneRicerca, New Action(AddressOf OperazioneCercaFarmacieComune))

            Dim elenco = _elencoFarmacieOperazione

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                If _soloAperte Then
                    Dim tutte = _db.OttieniFarmacieComune(_ultimoComune, False)
                    If tutte Is Nothing OrElse tutte.Count = 0 Then
                        MostraMessaggio("Non ho trovato farmacie per " & _ultimoComune & ". Prova a scrivere il comune in modo diverso oppure a indicare anche la provincia.")
                        Return
                    End If
                    MostraMessaggio("In questo momento non risulta aperta nessuna farmacia a " & _ultimoComune & ". Ti mostro tutte le farmacie della zona con orari e numeri di telefono, quando disponibili.")
                    CostruisciListaFarmacie(tutte, "Farmacie a " & _ultimoComune)
                    Return
                End If
                MostraMessaggio("Non ho trovato farmacie per " & _ultimoComune & ". Prova a scrivere il comune in modo diverso oppure a indicare anche la provincia.")
                Return
            End If

            Dim titolo As String = If(_soloAperte, "Farmacie aperte adesso a " & _ultimoComune, "Farmacie di " & _ultimoComune)
            CostruisciListaFarmacie(elenco, titolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ComuneConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub ComuneAnnullato(sender As Object, e As EventArgs)
        Try
            RilasciaCampi()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ComuneAnnullato: " & ex.ToString())
        End Try
    End Sub

    Private Sub CostruisciListaFarmacie(elenco As List(Of CLSFarmacie.Farmacia), titolo As String)
        Try
            ChiudiListaFarmacie()

            _farmacieCorrenti = elenco

            _listaFarmacie = New WGMenuLista()
            _listaFarmacie.ciclono = True
            _listaFarmacie.selezioneMenu = True
            _listaFarmacie.multiselezioneConsentita = False

            _listaFarmacie.BeginUpdate()
            For Each f In elenco
                Try
                    _listaFarmacie.aggiungiElemento(EtichettaFarmacia(f), f.Codice)
                Catch exFarmacia As Exception
                    _logger.ScriviLog("MenuFarmacia.CostruisciListaFarmacie elemento: " & exFarmacia.Message)
                End Try
            Next
            _listaFarmacie.EndUpdate()

            _listaFarmacie.impostaTitolo(titolo)
            _listaFarmacie.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.CostruisciListaFarmacie: " & ex.ToString())
        End Try
    End Sub

    Private Function EtichettaFarmacia(f As CLSFarmacie.Farmacia) As String
        Try
            If f Is Nothing Then Return "Farmacia"
            Dim etichetta As New StringBuilder()
            etichetta.Append(Valore(f.Denominazione))
            If Not String.IsNullOrWhiteSpace(f.Indirizzo) Then
                etichetta.Append(", ").Append(f.Indirizzo)
            End If
            If Not String.IsNullOrWhiteSpace(f.Cap) Then
                etichetta.Append(" ").Append(f.Cap)
            End If
            If Not String.IsNullOrWhiteSpace(f.Comune) Then
                etichetta.Append(", ").Append(f.Comune)
            End If
            If Not String.IsNullOrWhiteSpace(f.Provincia) Then
                etichetta.Append(" (").Append(f.Provincia).Append(")")
            End If
            If f.StatoApertura >= 0 Then
                etichetta.Append(", ").Append(_db.StatoAperturaLeggibile(f.StatoApertura))
            End If
            If Not String.IsNullOrWhiteSpace(f.Telefono) Then
                etichetta.Append(", telefono ").Append(f.Telefono)
            Else
                etichetta.Append(", telefono non disponibile")
            End If
            If Not String.IsNullOrWhiteSpace(f.Orari) Then
                etichetta.Append(", orario ").Append(_db.OrariLeggibili(f.Orari))
            End If
            If f.DistanzaKm > 0 Then
                etichetta.Append(", a circa ").Append(f.DistanzaKm.ToString("N1", New CultureInfo("it-IT"))).Append(" chilometri")
            End If
            Return etichetta.ToString()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.EtichettaFarmacia: " & ex.Message)
            Return "Farmacia"
        End Try
    End Function

    Private Sub _listaFarmacie_ElementoSelezionato(id As String) Handles _listaFarmacie.ElementoSelezionato
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaFarmacie)
            If String.IsNullOrWhiteSpace(identificativo) Then identificativo = id
            If String.IsNullOrWhiteSpace(identificativo) Then Return

            Dim farmacia As CLSFarmacie.Farmacia = Nothing
            If _farmacieCorrenti IsNot Nothing Then
                For Each f In _farmacieCorrenti
                    If String.Equals(f.Codice, identificativo, StringComparison.OrdinalIgnoreCase) Then
                        farmacia = f
                        Exit For
                    End If
                Next
            End If

            If farmacia Is Nothing Then
                MostraMessaggio("Non riesco a leggere i dati di questa farmacia.")
                Return
            End If

            MostraDettaglioFarmacia(farmacia)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaFarmacie_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraDettaglioFarmacia(f As CLSFarmacie.Farmacia)
        Try
            Dim righe = _db.InformazioniFarmacia(f)
            If righe Is Nothing OrElse righe.Count = 0 Then
                MostraMessaggio("Non ci sono informazioni disponibili per questa farmacia.")
                Return
            End If

            ChiudiListaDettaglioFarmacia()

            _listaDettaglioFarmacia = New WGMenuLista()
            _listaDettaglioFarmacia.ciclono = True
            _listaDettaglioFarmacia.selezioneMenu = True
            _listaDettaglioFarmacia.multiselezioneConsentita = False

            _mappaCopiaDettaglioFarmacia = New Dictionary(Of String, Tuple(Of String, String))
            _opzioneCopiaDettaglioFarmacia = ""

            _listaDettaglioFarmacia.BeginUpdate()
            Dim progressivo As Integer = 0
            For Each riga In righe
                Try
                    progressivo += 1
                    Dim idVoce As String = "dett" & progressivo.ToString()
                    _listaDettaglioFarmacia.aggiungiElemento(riga, idVoce)
                    RegistraCopiaDettaglioFarmacia(idVoce, riga)
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuFarmacia.MostraDettaglioFarmacia elemento: " & exRiga.Message)
                End Try
            Next
            _listaDettaglioFarmacia.EndUpdate()

            _listaDettaglioFarmacia.impostaTitolo("Informazioni su " & Valore(f.Denominazione))
            _listaDettaglioFarmacia.avvia()

            AggiornaOpzioneCopiaDettaglioFarmacia(IdentificativoSelezionato(_listaDettaglioFarmacia))
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraDettaglioFarmacia: " & ex.ToString())
        End Try
    End Sub

    Private Sub RegistraCopiaDettaglioFarmacia(idVoce As String, riga As String)
        Try
            If _mappaCopiaDettaglioFarmacia Is Nothing OrElse String.IsNullOrWhiteSpace(riga) Then Return

            Dim prefissi As String() = {"Indirizzo: ", "Telefono: ", "Email: "}
            Dim etichette As String() = {"Copia indirizzo", "Copia numero di telefono", "Copia email"}

            For i As Integer = 0 To prefissi.Length - 1
                If riga.StartsWith(prefissi(i), StringComparison.OrdinalIgnoreCase) Then
                    Dim valore As String = riga.Substring(prefissi(i).Length).Trim()
                    If valore.Length = 0 OrElse
                       String.Equals(valore, "non disponibile", StringComparison.OrdinalIgnoreCase) Then
                        Return
                    End If
                    _mappaCopiaDettaglioFarmacia(idVoce) = Tuple.Create(etichette(i), valore)
                    Return
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.RegistraCopiaDettaglioFarmacia: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaOpzioneCopiaDettaglioFarmacia(idVoce As String)
        Try
            If _listaDettaglioFarmacia Is Nothing OrElse _listaDettaglioFarmacia.IsDisposed Then Return

            Dim nuovaEtichetta As String = ""
            If _mappaCopiaDettaglioFarmacia IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(idVoce) AndAlso _mappaCopiaDettaglioFarmacia.ContainsKey(idVoce) Then
                nuovaEtichetta = _mappaCopiaDettaglioFarmacia(idVoce).Item1
            End If

            If nuovaEtichetta.Length = 0 Then Return

            If _opzioneCopiaDettaglioFarmacia.Length = 0 Then
                _listaDettaglioFarmacia.aggiungiOpzione(nuovaEtichetta, AddressOf OpzioneCopiaDettaglioFarmacia)
            ElseIf _opzioneCopiaDettaglioFarmacia <> nuovaEtichetta AndAlso _listaDettaglioFarmacia.opzioneEsiste(_opzioneCopiaDettaglioFarmacia) Then
                _listaDettaglioFarmacia.sostituisciOpzione(_opzioneCopiaDettaglioFarmacia, nuovaEtichetta, AddressOf OpzioneCopiaDettaglioFarmacia)
            End If
            _opzioneCopiaDettaglioFarmacia = nuovaEtichetta
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.AggiornaOpzioneCopiaDettaglioFarmacia: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCopiaDettaglioFarmacia(sender As Object, e As EventArgs)
        Try
            Dim idVoce As String = IdentificativoSelezionato(_listaDettaglioFarmacia)
            If String.IsNullOrWhiteSpace(idVoce) OrElse _mappaCopiaDettaglioFarmacia Is Nothing OrElse Not _mappaCopiaDettaglioFarmacia.ContainsKey(idVoce) Then
                MostraMessaggio("Non c'e' nulla da copiare per questa voce.")
                Return
            End If

            Dim coppia = _mappaCopiaDettaglioFarmacia(idVoce)
            Clipboard.SetText(coppia.Item2)

            Dim messaggio As String = "Copiato negli appunti."
            Select Case coppia.Item1
                Case "Copia indirizzo"
                    messaggio = "Indirizzo copiato negli appunti."
                Case "Copia numero di telefono"
                    messaggio = "Numero di telefono copiato negli appunti."
                Case "Copia email"
                    messaggio = "Email copiata negli appunti."
            End Select
            MostraMessaggio(messaggio)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneCopiaDettaglioFarmacia: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaDettaglioFarmacia_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaDettaglioFarmacia.VoceCambiata
        Try
            AggiornaOpzioneCopiaDettaglioFarmacia(e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaDettaglioFarmacia_VoceCambiata: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaDettaglioFarmacia_escPremuto(sender As Object, e As EventArgs) Handles _listaDettaglioFarmacia.escPremuto
        Try
            ChiudiListaDettaglioFarmacia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaDettaglioFarmacia_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaFarmacie_escPremuto(sender As Object, e As EventArgs) Handles _listaFarmacie.escPremuto
        Try
            ChiudiListaFarmacie()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaFarmacie_escPremuto: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Bugiardini scaricati"

    Private Sub VoceBugiardiniScaricati(sender As Object, e As EventArgs)
        Try
            VoceElencoDocumentiScaricati(sender, e)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceBugiardiniScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceElencoDocumentiScaricati(sender As Object, e As EventArgs)
        Try
            Dim elenco = _db.OttieniStampatiScaricati()
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggio("Non ci sono ancora documenti scaricati.")
                Return
            End If

            ChiudiListe()
            ChiudiCampoDocumento()

            _documentiScaricatiCorrenti = elenco

            _listaDocumentiScaricati = New WGMenuLista()
            _listaDocumentiScaricati.ciclono = True
            _listaDocumentiScaricati.selezioneMenu = True
            _listaDocumentiScaricati.multiselezioneConsentita = False

            _listaDocumentiScaricati.BeginUpdate()
            For Each s In elenco
                Try
                    Dim etichetta As String = s.NomeFarmaco & ", " & NomeDocumento(s.Tipo)
                    _listaDocumentiScaricati.aggiungiElemento(etichetta, s.Aic6 & "|" & s.Tipo)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuFarmacia.VoceElencoDocumentiScaricati elemento: " & exElemento.Message)
                End Try
            Next
            _listaDocumentiScaricati.EndUpdate()

            _listaDocumentiScaricati.aggiungiOpzione("Elimina questo documento", AddressOf OpzioneEliminaDocumento)
            _listaDocumentiScaricati.aggiungiOpzione("Elimina tutti i documenti scaricati", AddressOf OpzioneEliminaTuttiDocumenti)

            _listaDocumentiScaricati.impostaTitolo("Documenti scaricati (" & elenco.Count.ToString() & ")")
            _listaDocumentiScaricati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.VoceElencoDocumentiScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaDocumentiScaricati_ElementoSelezionato(id As String) Handles _listaDocumentiScaricati.ElementoSelezionato
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaDocumentiScaricati)
            If String.IsNullOrWhiteSpace(identificativo) Then identificativo = id

            Dim documento = TrovaDocumentoScaricato(identificativo)
            If documento Is Nothing Then Return

            If Not String.IsNullOrWhiteSpace(documento.PdfPath) AndAlso File.Exists(documento.PdfPath) Then
                ApriDocumentoPdf(documento.PdfPath, documento.NomeFarmaco & ", " & NomeDocumento(documento.Tipo))
            Else
                MostraMessaggio("Il file di questo documento non e' piu' presente sul computer.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaDocumentiScaricati_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaDocumentiScaricati_escPremuto(sender As Object, e As EventArgs) Handles _listaDocumentiScaricati.escPremuto
        Try
            ChiudiListaDocumentiScaricati()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia._listaDocumentiScaricati_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Function TrovaDocumentoScaricato(identificativo As String) As CLSFarmacie.StampatoScaricato
        Try
            If String.IsNullOrWhiteSpace(identificativo) Then Return Nothing
            If _documentiScaricatiCorrenti Is Nothing Then Return Nothing
            Dim parti = identificativo.Split("|"c)
            If parti.Length <> 2 Then Return Nothing
            For Each s In _documentiScaricatiCorrenti
                If String.Equals(s.Aic6, parti(0), StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(s.Tipo, parti(1), StringComparison.OrdinalIgnoreCase) Then
                    Return s
                End If
            Next
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.TrovaDocumentoScaricato: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneEliminaDocumento(sender As Object, e As EventArgs)
        Try
            Dim identificativo As String = IdentificativoSelezionato(_listaDocumentiScaricati)
            Dim documento = TrovaDocumentoScaricato(identificativo)
            If documento Is Nothing Then
                MostraMessaggio("Seleziona prima un documento da eliminare.")
                Return
            End If

            _db.EliminaStampato(documento.Aic6, documento.Tipo)
            MostraMessaggio("Documento eliminato: " & documento.NomeFarmaco & ", " & NomeDocumento(documento.Tipo) & ".")
            VoceElencoDocumentiScaricati(sender, e)
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneEliminaDocumento: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneEliminaTuttiDocumenti(sender As Object, e As EventArgs)
        Try
            _db.EliminaTuttiGliStampati()
            MostraMessaggio("Tutti i documenti scaricati sono stati eliminati.")
            ChiudiListaDocumentiScaricati()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.OpzioneEliminaTuttiDocumenti: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Chiusure e utilita"

    Private Sub MostraMessaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.MostraMessaggio: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListe()
        Try
            ChiudiListaRisultati()
            ChiudiListaInformazioni()
            ChiudiListaConfezioni()
            ChiudiListaFarmacie()
            ChiudiListaDettaglioFarmacia()
            ChiudiListaRicercheRecenti()
            ChiudiListaDocumentiScaricati()
            ChiudiListaSezioniFi()
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListe: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRisultati()
        Try
            If _listaRisultati Is Nothing Then Return
            Try
                _listaRisultati.abbandona()
            Catch
            End Try
            _listaRisultati = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaInformazioni()
        Try
            If _listaInformazioni Is Nothing Then Return
            Try
                _listaInformazioni.abbandona()
            Catch
            End Try
            _listaInformazioni = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaInformazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaConfezioni()
        Try
            If _listaConfezioni Is Nothing Then Return
            Try
                _listaConfezioni.abbandona()
            Catch
            End Try
            _listaConfezioni = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaConfezioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaFarmacie()
        Try
            If _listaFarmacie Is Nothing Then Return
            Try
                _listaFarmacie.abbandona()
            Catch
            End Try
            _listaFarmacie = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaFarmacie: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaDettaglioFarmacia()
        Try
            If _listaDettaglioFarmacia Is Nothing Then Return
            Try
                _listaDettaglioFarmacia.abbandona()
            Catch
            End Try
            _listaDettaglioFarmacia = Nothing
            _mappaCopiaDettaglioFarmacia = Nothing
            _opzioneCopiaDettaglioFarmacia = ""
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaDettaglioFarmacia: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRicercheRecenti()
        Try
            If _listaRicercheRecenti Is Nothing Then Return
            Try
                _listaRicercheRecenti.abbandona()
            Catch
            End Try
            _listaRicercheRecenti = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaDocumentiScaricati()
        Try
            If _listaDocumentiScaricati Is Nothing Then Return
            Try
                _listaDocumentiScaricati.abbandona()
            Catch
            End Try
            _listaDocumentiScaricati = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaDocumentiScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaSezioniFi()
        Try
            If _listaSezioniFi Is Nothing Then Return
            Try
                _listaSezioniFi.abbandona()
            Catch
            End Try
            _listaSezioniFi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiListaSezioniFi: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiCampoDocumento()
        Try
            If _campoDocumento Is Nothing Then Return
            Try
                RemoveHandler _campoDocumento.chiusura, AddressOf CampoDocumento_Chiusura
            Catch
            End Try
            Try
                If Not _campoDocumento.IsDisposed Then _campoDocumento.Dispose()
            Catch
            End Try
            _campoDocumento = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuFarmacia.ChiudiCampoDocumento: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class