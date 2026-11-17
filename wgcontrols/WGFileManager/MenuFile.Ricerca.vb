' ============================================================================================
' MenuFile - ricerca di file e cartelle
' ============================================================================================
'
' "Cerca file e cartelle" (menu opzioni) cerca nel nome, nella cartella aperta e nelle sue sottocartelle
' oppure, dalla scelta della risorsa, in tutti i dischi. La ricerca parte in sottofondo, senza attesa: un
' messaggio dice che i risultati si consultano dal centro di controllo, alla voce "Ricerca file", e alla
' fine la ricerca stessa annuncia quanti risultati ha trovato. Da lì i risultati si aprono, anche parziali,
' come una cartella virtuale ("ricerca:numero"), e si aggiornano man mano. Ogni file e cartella trovati
' si usano come nella gestione file (apri, apri la cartella che lo contiene, copia, taglia, elimina, rinomina...), Invio su
' una cartella ci entra ed Esc torna ai risultati. La ricerca continua anche uscendo dai risultati e
' si ritrova nel centro di controllo, alla voce "Ricerca file", per tutta la sessione.
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports wgcontrols

Partial Public Class MenuFile

    Private Const PrefissoRicerca As String = "ricerca:"
    Private _ricercaMostrata As WGRicercaFile
    Private _risultatiMostrati As Integer
    Private _fineRicercaAnnunciata As Boolean
    Private _timerRicerca As System.Windows.Forms.Timer
    ' Ricerca da mostrare all'avvio (dal centro di controllo), al posto della scelta della risorsa.
    Private _ricercaDaAprire As WGRicercaFile

    ''' <summary>Apre la gestione file direttamente sui risultati di una ricerca (dal centro di controllo).</summary>
    Public Sub AvviaConRicerca(ricerca As WGRicercaFile)
        _ricercaDaAprire = ricerca
        Avvia()
    End Sub

    ' Collega il centro di controllo alla gestione file: da lì si riaprono i risultati e si fanno nuove ricerche.
    Private Shared Sub RegistraRicercaNelCentro()
        WGRicercaFile.ApriRisultati = Sub(r)
                                          If r.Conteggio = 0 Then
                                              If r.Finita Then
                                                  NessunRisultato(r, Sub()
                                                                         Dim nuova As New MenuFile()
                                                                         nuova.AvviaNuovaRicercaDaSola()
                                                                     End Sub)
                                              Else
                                                  Messaggio("La ricerca di " & r.Testo & " non ha ancora trovato niente e continua in sottofondo: alla fine si sente quanti risultati ha trovato.")
                                              End If
                                              Return
                                          End If
                                          Dim gestione As New MenuFile()
                                          gestione.AvviaConRicerca(r)
                                      End Sub
        WGRicercaFile.NuovaRicercaDalCentro = Sub()
                                                  Dim gestione As New MenuFile()
                                                  gestione.AvviaNuovaRicercaDaSola()
                                              End Sub
    End Sub

    Private Function EsisteRicercaMostrata() As Boolean
        Return Not String.IsNullOrEmpty(currentDirectory) AndAlso currentDirectory.StartsWith(PrefissoRicerca, StringComparison.Ordinal)
    End Function

    ' Dove cercare: la cartella aperta, oppure tutti i dischi dalla scelta della risorsa.
    Private Function CartelleDaCercare() As List(Of String)
        If Not String.IsNullOrEmpty(currentDirectory) AndAlso WGRete.EsisteDirectory(currentDirectory, 8000) Then Return New List(Of String) From {currentDirectory}
        Return DriveInfo.GetDrives().Where(Function(d) d.IsReady AndAlso (d.DriveType = DriveType.Fixed OrElse d.DriveType = DriveType.Removable)).Select(Function(d) d.RootDirectory.FullName).ToList()
    End Function

    Private Shared Sub Messaggio(testo As String)
        Dim m As New WGMessaggi()
        m.Messaggio(testo)
        m.ShowDialog()
    End Sub

    ' La ricerca è partita: si lavora intanto, i risultati si consultano dal centro di controllo.
    Private Shared Sub AvvisaRicercaInSottofondo(ricerca As WGRicercaFile)
        Messaggio("La ricerca di " & ricerca.Testo & If(ricerca.NelContenuto, ", anche nel contenuto dei documenti,", "") &
                  " continua in sottofondo: puoi fare altro. Per consultare i risultati, anche mentre arrivano, apri il centro di controllo, voce Ricerca file. Alla fine si sente quanti risultati ha trovato.")
    End Sub

    ' Nessun risultato: se l'utente non l'ha fermata, si chiede se cercare di nuovo.
    Private Shared Sub NessunRisultato(ricerca As WGRicercaFile, nuovaRicerca As Action)
        If ricerca.Annullata Then
            sintesivocale.GetInstance().LeggiTesto("Ricerca fermata, nessun risultato")
            Return
        End If
        WGDomanda.Chiedi("Nessun risultato per " & ricerca.Testo & ". Vuoi fare un'altra ricerca?", nuovaRicerca)
    End Sub

    Private Sub AvviaRicercaDaOpzioni()
        AvviaRicercaIn(CartelleDaCercare())
    End Sub

    Private Sub AvviaRicercaIn(cartelle As List(Of String))
        Try
            Dim dove As String = If(cartelle.Count = 1, Path.GetFileName(cartelle(0).TrimEnd("\"c)), "tutti i dischi")
            If String.IsNullOrEmpty(dove) Then dove = cartelle(0)
            Dim campiRicerca As New Campi()
            campiRicerca.Aggiungi("Cerca file e cartelle in " & dove & ", sottocartelle comprese. Scrivi una o più parole del nome, oppure un modello con asterisco e punto interrogativo, come asterisco punto docx", "c.ricerca", True)
            AddHandler campiRicerca.Fine, Sub(s, e)
                                              Try
                                                  Dim testo As String = campiRicerca.RecuperaValore("c.ricerca")
                                                  If String.IsNullOrWhiteSpace(testo) Then Return
                                                  ChiediModoRicerca(testo, Sub(nelContenuto As Boolean)
                                                                              AvvisaRicercaInSottofondo(WGRicercaFile.Nuova(testo, cartelle, nelContenuto))
                                                                          End Sub)
                                              Catch exFine As Exception
                                                  ScriviLogCompleto("AvviaRicercaDaOpzioni fine", exFine)
                                              End Try
                                          End Sub
            campiRicerca.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("AvviaRicercaIn - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Ricerca in tutto il computer senza passare dalla gestione file (menu Gestione file, centro di controllo):
    ''' chiede cosa cercare e come, poi apre la gestione file sui risultati.
    ''' </summary>
    Public Sub CercaNelComputer()
        AvviaNuovaRicercaDaSola()
    End Sub

    ''' <summary>Come CercaNelComputer, con il testo già scritto (dalla ricerca globale, Windows S).</summary>
    Public Sub CercaNelComputer(testo As String)
        If String.IsNullOrWhiteSpace(testo) Then
            AvviaNuovaRicercaDaSola()
            Return
        End If
        AvviaRicercaNelComputer(testo.Trim())
    End Sub

    ' Chiede come cercare, poi cerca in tutti i dischi in sottofondo.
    Private Sub AvviaRicercaNelComputer(testo As String)
        ChiediModoRicerca(testo, Sub(nelContenuto As Boolean)
                                    currentDirectory = ""
                                    AvvisaRicercaInSottofondo(WGRicercaFile.Nuova(testo, CartelleDaCercare(), nelContenuto))
                                End Sub)
    End Sub

    Private Sub AvviaNuovaRicercaDaSola()
        Try
            Dim campiRicerca As New Campi()
            campiRicerca.Aggiungi("Cerca file e cartelle in tutto il computer. Scrivi una o più parole del nome, oppure un modello con asterisco e punto interrogativo, come asterisco punto docx", "c.ricerca", True)
            AddHandler campiRicerca.Fine, Sub(s, e)
                                              Dim testo As String = campiRicerca.RecuperaValore("c.ricerca")
                                              If String.IsNullOrWhiteSpace(testo) Then Return
                                              AvviaRicercaNelComputer(testo.Trim())
                                          End Sub
            campiRicerca.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("AvviaNuovaRicercaDaSola - Errore", ex)
        End Try
    End Sub

    ' Ricerca smart: solo nel nome, oppure anche dentro i documenti (più lenta). Esc annulla.
    Private Sub ChiediModoRicerca(testo As String, avvio As Action(Of Boolean))
        Dim menuModo As New WGMenu()
        menuModo.aggiungi("Solo nel nome", Sub(s, e) avvio(False))
        menuModo.aggiungi("Nel nome e nel contenuto dei documenti: testo, Word, OpenDocument, PDF, RTF e pagine web. Più lenta", Sub(s, e) avvio(True))
        menuModo.avvia("Dove cercare", True)
    End Sub

    ''' <summary>Mostra i risultati come una cartella; con conStoria, Esc torna dove si era prima.</summary>
    Private Sub MostraRisultatiRicerca(ricerca As WGRicercaFile, conStoria As Boolean)
        Try
            If ricerca Is Nothing OrElse lista Is Nothing Then Return
            If conStoria Then history.Add(New NavigazioneStato(If(String.IsNullOrEmpty(currentDirectory), "sceltadisco", currentDirectory), Math.Max(0, lista.ottieniIndice())))
            currentDirectory = PrefissoRicerca & ricerca.Id
            ' Si aspetta che il menu opzioni o il campo di ricerca abbiano finito di chiudersi.
            uiContext.Post(Sub(state) RiempiRisultatiRicerca(ricerca), Nothing)
        Catch ex As Exception
            ScriviLogCompleto("MostraRisultatiRicerca - Errore", ex)
        End Try
    End Sub

    ' Chiamata da PreparaCaricamentoDirectory per "ricerca:numero" (Aggiorna, ritorno con Esc, dopo le operazioni).
    Private Sub RicaricaRisultatiRicerca(percorsoVirtuale As String)
        Dim numero As Integer
        Dim ricerca As WGRicercaFile = Nothing
        If Integer.TryParse(percorsoVirtuale.Substring(PrefissoRicerca.Length), numero) Then ricerca = WGRicercaFile.Trova(numero)
        If ricerca Is Nothing Then
            CartellaPrecedente()
            Return
        End If
        ricerca.RimuoviInesistenti()
        RiempiRisultatiRicerca(ricerca)
    End Sub

    Private Sub RiempiRisultatiRicerca(ricerca As WGRicercaFile)
        Try
            If lista Is Nothing Then Return
            If Not String.Equals(currentDirectory, PrefissoRicerca & ricerca.Id, StringComparison.Ordinal) Then Return
            ' Tutti i risultati eliminati o spostati: niente elenco vuoto, si torna indietro.
            If ricerca.Conteggio = 0 Then
                sintesivocale.GetInstance().LeggiTesto("Non è rimasto nessun risultato" & If(ricerca.Finita, "", ": la ricerca continua nel centro di controllo"))
                CartellaPrecedente()
                Return
            End If
            AnnullaCaricamentoDirectoryInCorso()
            isDriveSelectionMode = False
            ' Nei risultati nessuna cartella è sorvegliata: i cambiamenti si vedono con Aggiorna.
            logic.SetupFileSystemWatcher("")
            wgInternalList.Pulisci()
            lista.pulisci()
            InizializzaOpzioniRicerca(ricerca)
            _ricercaMostrata = ricerca
            _risultatiMostrati = 0
            _fineRicercaAnnunciata = ricerca.Finita
            ricerca.Mostrata = True
            AggiungiNuoviRisultati()
            If _timerRicerca Is Nothing Then
                _timerRicerca = New System.Windows.Forms.Timer() With {.Interval = 500}
                AddHandler _timerRicerca.Tick, AddressOf TimerRicerca_Tick
            End If
            If Not ricerca.Finita Then _timerRicerca.Start()
        Catch ex As Exception
            ScriviLogCompleto("RiempiRisultatiRicerca - Errore", ex)
        End Try
    End Sub

    Private Sub InizializzaOpzioniRicerca(ricerca As WGRicercaFile)
        lista.opzioniPulisci()
        lista.multiselezioneConsentita = True
        lista.aggiungiOpzione("Apri", AddressOf ApriElementoSelezionato)
        lista.aggiungiOpzione("Apri la cartella che lo contiene", AddressOf ApriCartellaDelRisultato)
        lista.aggiungiOpzione("Taglia", AddressOf tagliaevent)
        lista.aggiungiOpzione("Copia", AddressOf copiaevent)
        lista.aggiungiOpzione("Elimina", AddressOf eliminaevent)
        lista.aggiungiOpzione("Rinomina", AddressOf rinomina)
        lista.aggiungiOpzione("Comprimi...", AddressOf AvviaCompressione)
        lista.aggiungiOpzione("Aggiungi ai Preferiti", AddressOf AggiungiRimuoviPreferiti)
        lista.aggiungiOpzione("Informazioni", AddressOf informazioni)
        lista.aggiungiOpzione("Aggiorna", AddressOf AggiornaCorrente)
        lista.aggiungiOpzione("Ferma la ricerca", Sub(s, e)
                                                      If ricerca.Finita Then
                                                          sintesivocale.GetInstance().LeggiTesto("La ricerca è già finita")
                                                          Return
                                                      End If
                                                      ricerca.Annulla()
                                                  End Sub)
        lista.aggiungiOpzione("Nuova ricerca", Sub(s, e)
                                                   ' Si cerca di nuovo dove cercava questa ricerca.
                                                   Dim dove As String = If(ricerca.Cartelle.Count = 1, ricerca.Cartelle(0), "")
                                                   Dim precedente As String = currentDirectory
                                                   currentDirectory = dove
                                                   AvviaRicercaDaOpzioni()
                                                   currentDirectory = precedente
                                               End Sub)
    End Sub

    Private Function TitoloRisultati(ricerca As WGRicercaFile) As String
        Return "Risultati" & If(ricerca.Finita, If(ricerca.Annullata, ", ricerca fermata", ""), ", ricerca in corso")
    End Function

    ' Aggiunge in fondo i risultati arrivati dall'ultima volta.
    Private Sub AggiungiNuoviRisultati()
        Dim ricerca As WGRicercaFile = _ricercaMostrata
        If ricerca Is Nothing OrElse lista Is Nothing Then Return
        Dim nuovi As List(Of String) = ricerca.Risultati(_risultatiMostrati)
        If nuovi.Count > 0 Then
            lista.invioNonConsentito = False
            For Each percorso As String In nuovi
                Try
                    Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"c))
                    Dim cartella As String = Path.GetDirectoryName(percorso.TrimEnd("\"c))
                    wgInternalList.Aggiungi(percorso)
                    lista.aggiungiElemento(nome & If(WGRete.EsisteDirectory(percorso, 8000), ", cartella", "") & If(ricerca.TrovatoNelContenuto(percorso), ", trovato nel contenuto", "") & ", in " & cartella, percorso)
                Catch exVoce As Exception
                    ScriviLogCompleto("AggiungiNuoviRisultati voce", exVoce)
                End Try
            Next
            _risultatiMostrati += nuovi.Count
        End If
        lista.impostaTitolo(TitoloRisultati(ricerca))
    End Sub

    Private Sub TimerRicerca_Tick(sender As Object, e As EventArgs)
        Try
            Dim ricerca As WGRicercaFile = _ricercaMostrata
            If ricerca Is Nothing OrElse lista Is Nothing OrElse Not String.Equals(currentDirectory, PrefissoRicerca & ricerca.Id, StringComparison.Ordinal) Then
                _timerRicerca.Stop()
                If ricerca IsNot Nothing Then ricerca.Mostrata = False
                Return
            End If
            AggiungiNuoviRisultati()
            If ricerca.Finita Then
                _timerRicerca.Stop()
                If Not _fineRicercaAnnunciata Then
                    _fineRicercaAnnunciata = True
                    Dim quanti As Integer = ricerca.Conteggio
                    sintesivocale.GetInstance().LeggiTesto(If(ricerca.Annullata, "Ricerca fermata: ", "Ricerca finita: ") & If(quanti = 1, "un risultato", quanti & " risultati"))
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("TimerRicerca_Tick - Errore", ex)
        End Try
    End Sub

    ' Porta nella cartella del risultato, con il risultato selezionato; Esc torna ai risultati.
    Private Sub ApriCartellaDelRisultato()
        Try
            Dim selezionati As List(Of String) = lista.ottieniElementi()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then Return
            Dim percorso As String = selezionati(0)
            Dim cartella As String = Path.GetDirectoryName(percorso.TrimEnd("\"c))
            If String.IsNullOrEmpty(cartella) OrElse Not WGRete.EsisteDirectory(cartella, 8000) Then Return
            Dim daEvidenziare As String = percorso
            uiContext.Post(Sub(state)
                               _percorsoDaEvidenziareDopoCaricamento = daEvidenziare
                               CartellaSuccessiva(cartella)
                           End Sub, Nothing)
        Catch ex As Exception
            ScriviLogCompleto("ApriCartellaDelRisultato - Errore", ex)
        End Try
    End Sub

End Class
