' ============================================================================================
' WGConnessioni - Wi-Fi e Bluetooth per chi usa WinGabriel
' ============================================================================================
'
' Wi-Fi (MostraWifi): l'elenco ha solo le reti, dalla più forte, con segnale, protezione e se sono
' salvate. Invio su una rete ci si collega (chiedendo la password se serve), sulla rete collegata
' ne legge le informazioni. Nel menu opzioni: collegarsi o disconnettersi secondo la rete scelta,
' informazioni dettagliate, dimenticare la rete salvata, aggiornare l'elenco, spegnere il Wi-Fi.
' Se il Wi-Fi è spento, se non ci sono reti o se Windows non le mostra per la posizione spenta, si
' fa una domanda invece di aprire un elenco vuoto.
' Bluetooth (MostraBluetooth): i dispositivi associati e "Associa un nuovo dispositivo"; nel menu
' opzioni informazioni, dissociazione, aggiornamento, spegnimento del Bluetooth.
' Ogni attesa passa da WGProcessoInCorso; gli errori diventano domande (riprovare? consentire?),
' mai voci di menu che mostrano solo testo. Niente rimanda alle impostazioni di Windows, che la sintesi
' di WinGabriel non legge: quello che serve (permessi, reti aziendali, reti nascoste) si fa da qui.
'
'   WGConnessioni.MostraWifi()
'   WGConnessioni.MostraBluetooth()
'   WGConnessioni.MostraConnessioni()          ' menu con tutte e due
'   WGConnessioni.DescrizioneBluetooth()       ' per la barra di stato della scrivania
'   WGConnessioni.MenuWifiNellaGriglia(g)      ' dalla barra di stato: il menu lo disegna e lo legge la griglia
'   WGConnessioni.MenuBluetoothNellaGriglia(g)
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports Windows.Devices.Radios
Imports wgcontrols

Public NotInheritable Class WGConnessioni

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Private Shared Sub Leggi(testo As String)
        sintesivocale.GetInstance().LeggiTesto(testo)
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Dim m As New WGMessaggi()
        m.Messaggio(testo)
        m.ShowDialog()
    End Sub

    ''' <summary>Menu con Wi-Fi e Bluetooth.</summary>
    Public Shared Sub MostraConnessioni()
        Dim menu As New WGMenu()
        menu.aggiungi("Wi-Fi", Sub(s, e) MostraWifi())
        menu.aggiungi("Bluetooth", Sub(s, e) MostraBluetooth())
        menu.avvia("Connessioni")
    End Sub

    ' Errore delle radio: se Windows lo nega, WinGabriel scrive da sé il consenso e riprova.
    Private Shared Sub ErroreRadio(errore As String, riprova As Action)
        If errore.Contains("Privacy") Then
            WGDomanda.Chiedi("Windows non permette a WinGabriel di accendere e spegnere Wi-Fi e Bluetooth. Vuoi consentirlo?",
                             Sub()
                                 If WGRadio.ConsentiAccesso() Then
                                     riprova()
                                 Else
                                     Messaggio("Non è stato possibile consentirlo")
                                 End If
                             End Sub)
        Else
            Messaggio(errore)
        End If
    End Sub

    ' Accende o spegne una radio con l'attesa; vero se riuscito (l'errore diventa una domanda).
    Private Shared Async Function CambiaRadioAsync(tipo As RadioKind, accendere As Boolean, nome As String) As Task(Of Boolean)
        Dim errore As String = Await WGProcessoInCorso.EseguiAsync(If(accendere, "Accensione del ", "Spegnimento del ") & nome,
                                                                  Async Function()
                                                                      Dim esito As String = Await WGRadio.ImpostaAsync(tipo, accendere)
                                                                      ' La radio impiega un attimo a partire davvero.
                                                                      If esito = "" AndAlso accendere Then Await Task.Delay(2000)
                                                                      Return esito
                                                                  End Function)
        If errore <> "" Then
            ErroreRadio(errore, Async Sub() Await CambiaRadioAsync(tipo, accendere, nome))
            Return False
        End If
        Leggi(nome & If(accendere, " acceso", " spento"))
        Return True
    End Function

    ''' <summary>Stato della radio per il centro di controllo: acceso, spento, Nothing se non c'è.</summary>
    Public Shared Function StatoRadioAsync(bluetooth As Boolean) As Task(Of Boolean?)
        Return WGRadio.AccesaAsync(If(bluetooth, RadioKind.Bluetooth, RadioKind.WiFi))
    End Function

    ''' <summary>
    ''' Interruttore del centro di controllo: accende o spegne con l'attesa (e le domande se Windows nega);
    ''' restituisce lo stato nuovo, Nothing se non è cambiato.
    ''' </summary>
    Public Shared Async Function CommutaRadioAsync(bluetooth As Boolean) As Task(Of Boolean?)
        Try
            Dim tipo As RadioKind = If(bluetooth, RadioKind.Bluetooth, RadioKind.WiFi)
            Dim acceso As Boolean? = Await WGRadio.AccesaAsync(tipo)
            If Not acceso.HasValue Then
                Messaggio(If(bluetooth, "Questo computer non ha il Bluetooth.", "Questo computer non ha il Wi-Fi."))
                Return Nothing
            End If
            If Await CambiaRadioAsync(tipo, Not acceso.Value, If(bluetooth, "Bluetooth", "Wi-Fi")) Then Return Not acceso.Value
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.CommutaRadioAsync: " & ex.Message)
        End Try
        Return Nothing
    End Function

#Region "Wi-Fi"

    Private Const OpzioneCollegati As String = "Collegati alla rete"
    Private Const OpzioneDisconnetti As String = "Disconnetti dalla rete"
    Private Const OpzioneDimentica As String = "Dimentica la rete"
    Private Const OpzioneInformazioniRete As String = "Informazioni sulla rete"

    ''' <summary>Apre il Wi-Fi: controlla che sia acceso, cerca le reti (con l'attesa), poi mostra l'elenco.</summary>
    Public Shared Sub MostraWifi()
        MostraWifiAsync(False)
    End Sub

    Private Shared Async Sub MostraWifiAsync(posizioneGiaProvata As Boolean)
        Try
            If Not WGWifi.Disponibile() Then
                Messaggio("Questo computer non ha una scheda Wi-Fi, oppure è disattivata in Windows.")
                Return
            End If
            Dim acceso As Boolean? = Await WGProcessoInCorso.EseguiAsync("Controllo del Wi-Fi", Function() WGRadio.AccesaAsync(RadioKind.WiFi))
            If acceso.HasValue AndAlso Not acceso.Value Then
                WGDomanda.Chiedi("Il Wi-Fi è spento. Vuoi accenderlo?", Async Sub()
                                                                            If Await CambiaRadioAsync(RadioKind.WiFi, True, "Wi-Fi") Then MostraWifiAsync(posizioneGiaProvata)
                                                                        End Sub)
                Return
            End If
            Dim reti As List(Of ReteWifi) = Await WGProcessoInCorso.EseguiAsync("Ricerca delle reti Wi-Fi", Function() WGWifi.CercaRetiAsync())
            If WGWifi.ServePermessoPosizione Then
                If posizioneGiaProvata Then
                    Messaggio("Le reti Wi-Fi non sono ancora disponibili: Windows non dà ancora la posizione a WinGabriel")
                Else
                    WGDomanda.Chiedi("Le reti Wi-Fi non sono disponibili perché in Windows la posizione è spenta. Vuoi provare ad attivare la posizione?",
                                     Sub() AttivaPosizioneEMostraWifi())
                End If
                Return
            End If
            If reti.Count = 0 Then
                Messaggio("Nessuna rete Wi-Fi trovata qui intorno")
                Return
            End If
            MostraElencoWifi(reti)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.MostraWifiAsync: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub AttivaPosizioneEMostraWifi(Optional poi As Action = Nothing)
        Try
            Dim avviso As String = If(WGPosizione.ServeAmministratore,
                "Attivazione della posizione: Windows chiederà la conferma dell'amministratore, premi Alt S per dire sì",
                "Attivazione della posizione")
            Leggi(avviso)
            Dim errore As String = Await WGProcessoInCorso.EseguiAsync(avviso, Function() WGPosizione.ConsentiAsync())
            If errore <> "" Then
                Messaggio("Non è stato possibile attivare la posizione: " & errore)
                Return
            End If
            Leggi("Posizione attivata")
            If poi IsNot Nothing Then poi() Else MostraWifiAsync(True)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.AttivaPosizioneEMostraWifi: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraElencoWifi(retiIniziali As List(Of ReteWifi))
        Dim aperta As Boolean = True
        Dim reti As New List(Of ReteWifi)(retiIniziali)
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = False
        Dim chiudi As Action = Sub()
                                   aperta = False
                                   lista.abbandona()
                               End Sub
        AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) chiudi()
        Dim scelta As Func(Of ReteWifi) = Function()
                                              Dim id As String = lista.ottieniElemento()
                                              Dim numero As Integer
                                              If id Is Nothing OrElse Not id.StartsWith("rete|") OrElse Not Integer.TryParse(id.Substring(5), numero) OrElse numero >= reti.Count Then Return Nothing
                                              Return reti(numero)
                                          End Function
        ' Ricarica l'elenco senza nuova scansione (con cerca, con la scansione); se non resta niente, si chiude.
        Dim gestori As New Dictionary(Of String, EventHandler)()
        Dim riempi As Func(Of Boolean, Task) = Async Function(cerca As Boolean)
                                                   Dim nuove As List(Of ReteWifi) = Await WGProcessoInCorso.EseguiAsync(If(cerca, "Ricerca delle reti Wi-Fi", "Aggiornamento dell'elenco"),
                                                                                                                       Function() If(cerca, WGWifi.CercaRetiAsync(), Task.Run(Function() WGWifi.Reti())))
                                                   If Not aperta Then Return
                                                   If nuove.Count = 0 Then
                                                       chiudi()
                                                       Messaggio("Nessuna rete Wi-Fi trovata qui intorno")
                                                       Return
                                                   End If
                                                   reti.Clear()
                                                   reti.AddRange(nuove)
                                                   RiempiWifi(lista, reti)
                                                   AggiornaOpzioniRete(lista, scelta(), gestori)
                                               End Function
        Dim disconnetti As Action(Of ReteWifi) = Nothing
        disconnetti = Async Sub(rete As ReteWifi)
                          Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync("Disconnessione da " & rete.NomeMostrato,
                                                                                       Async Function()
                                                                                           Dim esito As Boolean = WGWifi.Disconnetti()
                                                                                           Await Task.Delay(1500)
                                                                                           Return esito
                                                                                       End Function)
                          If Not riuscito Then
                              Messaggio("Disconnessione da " & rete.NomeMostrato & " non riuscita")
                              Return
                          End If
                          Leggi("Disconnesso da " & rete.NomeMostrato)
                          Await riempi(False)
                      End Sub
        Dim dimentica As Action(Of ReteWifi) = Nothing
        dimentica = Async Sub(rete As ReteWifi)
                        If Not WGWifi.DimenticaProfilo(rete.Profilo) Then
                            Messaggio("Non è stato possibile dimenticare " & rete.NomeMostrato)
                            Return
                        End If
                        Leggi(rete.NomeMostrato & " dimenticata")
                        Await riempi(False)
                    End Sub
        ' Le opzioni che dipendono dalla rete scelta cambiano quando ci si sposta nell'elenco.
        gestori(OpzioneCollegati) = Sub(s, e)
                                        Dim rete As ReteWifi = scelta()
                                        If rete IsNot Nothing Then Collega(rete, Function() riempi(False))
                                    End Sub
        gestori(OpzioneDisconnetti) = Sub(s, e)
                                          Dim rete As ReteWifi = scelta()
                                          If rete IsNot Nothing Then disconnetti(rete)
                                      End Sub
        gestori(OpzioneDimentica) = Sub(s, e)
                                        Dim rete As ReteWifi = scelta()
                                        If rete Is Nothing OrElse rete.Profilo = "" Then Return
                                        WGDomanda.Chiedi("Dimenticare la rete " & rete.NomeMostrato & "? La password salvata verrà cancellata", Sub() dimentica(rete))
                                    End Sub
        AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                  Dim rete As ReteWifi = scelta()
                                                  If rete Is Nothing Then Return
                                                  If rete.Connessa Then
                                                      MostraInformazioniRete(rete)
                                                  Else
                                                      Collega(rete, Function() riempi(False))
                                                  End If
                                              End Sub
        AddHandler lista.VoceCambiata, Sub(s As Object, e As VoceCambiataEventArgs) AggiornaOpzioniRete(lista, scelta(), gestori)
        lista.aggiungiOpzione(OpzioneInformazioniRete, Sub(s, e)
                                                           Dim rete As ReteWifi = scelta()
                                                           If rete IsNot Nothing Then MostraInformazioniRete(rete)
                                                       End Sub)
        lista.aggiungiOpzione("Aggiorna l'elenco delle reti", Async Sub(s, e) Await riempi(True))
        lista.aggiungiOpzione("Collegati a una rete nascosta", Sub(s, e) ChiediReteNascosta(0, Function() riempi(False)))
        lista.aggiungiOpzione("Spegni il Wi-Fi", Async Sub(s, e)
                                                     If Await CambiaRadioAsync(RadioKind.WiFi, False, "Wi-Fi") Then chiudi()
                                                 End Sub)
        RiempiWifi(lista, reti)
        AggiornaOpzioniRete(lista, scelta(), gestori)
        lista.avvia()
    End Sub

    ' Subito dopo "Informazioni": Collegati o Disconnetti secondo la rete scelta, Dimentica solo per le reti salvate.
    Private Shared Sub AggiornaOpzioniRete(lista As WGMenuLista, rete As ReteWifi, gestori As Dictionary(Of String, EventHandler))
        Try
            Dim metti As Action(Of String, Boolean) = Sub(nome As String, serve As Boolean)
                                                          Dim esiste As Boolean = lista.opzioneEsiste(nome)
                                                          If serve AndAlso Not esiste Then
                                                              lista.aggiungiOpzioneDopo(OpzioneInformazioniRete, nome, gestori(nome))
                                                          ElseIf Not serve AndAlso esiste Then
                                                              lista.rimuoviOpzione(nome)
                                                          End If
                                                      End Sub
            metti(OpzioneDimentica, rete IsNot Nothing AndAlso rete.Profilo <> "")
            metti(OpzioneDisconnetti, rete IsNot Nothing AndAlso rete.Connessa)
            metti(OpzioneCollegati, rete IsNot Nothing AndAlso Not rete.Connessa)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.AggiornaOpzioniRete: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RiempiWifi(lista As WGMenuLista, reti As List(Of ReteWifi))
        Dim collegata As ReteWifi = reti.FirstOrDefault(Function(r) r.Connessa)
        Dim indice As Integer = Math.Max(0, lista.ottieniIndice())
        lista.svuotaLista()
        lista.impostaTitolo("Elenco reti Wi-Fi" & If(collegata IsNot Nothing, ", connesso a " & collegata.NomeMostrato, ""))
        For i As Integer = 0 To reti.Count - 1
            lista.aggiungiElemento(reti(i).Descrizione, "rete|" & i)
        Next
        If indice > 0 Then lista.selezionaElemento(Math.Min(indice, reti.Count - 1))
    End Sub

    Private Shared Sub MostraInformazioniRete(rete As ReteWifi)
        Dim righe As New List(Of String) From {rete.NomeMostrato}
        If rete.Connessa Then
            Dim ssid As String = ""
            Dim segnaleAttuale As Integer = rete.Segnale
            WGWifi.ConnessioneCorrente(ssid, segnaleAttuale)
            righe.Add("Stato: connessa")
            righe.Add("Segnale: " & segnaleAttuale & " per cento")
        Else
            righe.Add("Stato: non connessa")
            righe.Add("Segnale: " & rete.Segnale & " per cento")
        End If
        righe.Add("Protezione: " & DescriviProtezione(rete))
        righe.Add("Cifratura: " & DescriviCifratura(rete))
        righe.Add("Salvata in Windows: " & If(rete.Profilo <> "", "sì, come " & rete.Profilo, "no"))
        righe.Add("Collegamento possibile: " & If(rete.Collegabile, "sì", "no, Windows la considera non raggiungibile"))
        If rete.Aziendale Then righe.Add("È una rete aziendale: ci si collega con nome utente e password")
        Messaggio(String.Join(vbCrLf, righe))
    End Sub

    Private Shared Sub Collega(rete As ReteWifi, aggiorna As Func(Of Task))
        ' Rete che non trasmette il nome: si chiede il nome, la protezione è già nota.
        If rete.Ssid = "" Then
            ChiediReteNascosta(rete.Autenticazione, aggiorna)
            Return
        End If
        ' Rete aziendale non ancora salvata: nome utente e password.
        If rete.Aziendale AndAlso rete.Profilo = "" Then
            ChiediCredenzialiAziendali(rete, aggiorna)
            Return
        End If
        If rete.Protetta AndAlso rete.Profilo = "" Then
            ChiediPasswordEConnetti(rete, aggiorna)
            Return
        End If
        ConnettiAsync(rete, "", aggiorna)
    End Sub

    ' Rete nascosta: nome scritto dall'utente, poi la protezione (se non la si conosce già) e la password.
    Private Shared Sub ChiediReteNascosta(autenticazione As Integer, aggiorna As Func(Of Task))
        Dim campi As New Campi()
        campi.Aggiungi("Nome della rete nascosta, esattamente come è scritto nel modem, con maiuscole e minuscole", "nome", True)
        AddHandler campi.Fine, Sub(s, e)
                                   Dim nome As String = If(campi.RecuperaValore("nome"), "").Trim()
                                   If nome = "" Then Return
                                   If System.Text.Encoding.UTF8.GetByteCount(nome) > 32 Then
                                       Messaggio("Il nome di una rete Wi-Fi può avere al massimo 32 caratteri")
                                       Return
                                   End If
                                   If autenticazione <> 0 Then
                                       CollegaReteNascosta(ReteWifi.Nuova(nome, autenticazione), aggiorna)
                                       Return
                                   End If
                                   Dim menu As New WGMenu()
                                   menu.aggiungi("WPA2 personale, la più comune", Sub() CollegaReteNascosta(ReteWifi.Nuova(nome, WGWifi.AUTH_RSNA_PSK), aggiorna))
                                   menu.aggiungi("WPA3 personale", Sub() CollegaReteNascosta(ReteWifi.Nuova(nome, WGWifi.AUTH_WPA3_SAE), aggiorna))
                                   menu.aggiungi("WPA personale, reti vecchie", Sub() CollegaReteNascosta(ReteWifi.Nuova(nome, WGWifi.AUTH_WPA_PSK), aggiorna))
                                   menu.aggiungi("WPA2 aziendale, con nome utente e password", Sub() CollegaReteNascosta(ReteWifi.Nuova(nome, WGWifi.AUTH_RSNA), aggiorna))
                                   menu.aggiungi("Aperta, senza password", Sub() CollegaReteNascosta(ReteWifi.Nuova(nome, WGWifi.AUTH_OPEN), aggiorna))
                                   menu.avvia("Protezione", True)
                               End Sub
        campi.Avvia()
    End Sub

    Private Shared Sub CollegaReteNascosta(rete As ReteWifi, aggiorna As Func(Of Task))
        If rete.Aziendale Then
            ChiediCredenzialiAziendali(rete, aggiorna)
        ElseIf rete.Protetta Then
            ChiediPasswordEConnetti(rete, aggiorna)
        Else
            ConnettiAsync(rete, "", aggiorna)
        End If
    End Sub

    ' Reti aziendali, come eduroam: nome utente (anche DOMINIO\utente o nome@ente.it) e password.
    Private Shared Sub ChiediCredenzialiAziendali(rete As ReteWifi, aggiorna As Func(Of Task))
        Dim campi As New Campi()
        campi.Aggiungi("Nome utente per la rete aziendale " & rete.NomeMostrato & ", per esempio nome@universita.it oppure DOMINIO barra rovesciata nome", "utente", True)
        campi.AggiungiPassword("Password", "password", True)
        AddHandler campi.Fine, Async Sub(s, e)
                                   Try
                                       Dim utente As String = If(campi.RecuperaValore("utente"), "").Trim()
                                       Dim password As String = campi.RecuperaValore("password")
                                       If utente = "" OrElse String.IsNullOrEmpty(password) Then Return
                                       Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Collegamento a " & rete.NomeMostrato, Function() WGWifi.ConnettiAziendaleAsync(rete, utente, password))
                                       If esito = "" Then
                                           Leggi("Connesso a " & rete.NomeMostrato)
                                           Await aggiorna()
                                           Return
                                       End If
                                       Messaggio(esito)
                                   Catch ex As Exception
                                       log.ScriviLog("WGConnessioni.ChiediCredenzialiAziendali: " & ex.Message)
                                   End Try
                               End Sub
        campi.Avvia()
    End Sub

    Private Shared Sub ChiediPasswordEConnetti(rete As ReteWifi, aggiorna As Func(Of Task))
        Dim campi As New Campi()
        campi.AggiungiPassword("Password della rete " & rete.NomeMostrato, "password", True)
        AddHandler campi.Fine, Sub(s, e)
                                   Dim password As String = campi.RecuperaValore("password")
                                   If String.IsNullOrEmpty(password) Then Return
                                   ConnettiAsync(rete, password, aggiorna)
                               End Sub
        campi.Avvia()
    End Sub

    ' Se il collegamento non riesce si chiede se riprovare, riscrivendo la password per le reti protette.
    Private Shared Async Sub ConnettiAsync(rete As ReteWifi, password As String, aggiorna As Func(Of Task))
        Try
            Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Collegamento a " & rete.NomeMostrato, Function() WGWifi.ConnettiAsync(rete, password))
            If esito = "" Then
                Leggi("Connesso a " & rete.NomeMostrato)
                Await aggiorna()
                Return
            End If
            If rete.Aziendale Then
                Messaggio(esito)
            ElseIf rete.Protetta Then
                Messaggio(esito)
            Else
                Messaggio(esito)
            End If
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.ConnettiAsync: " & ex.Message)
        End Try
    End Sub

    Private Shared Function DescriviProtezione(rete As ReteWifi) As String
        Select Case rete.Autenticazione
            Case WGWifi.AUTH_OPEN : Return If(rete.Protetta, "WEP", "nessuna, rete aperta")
            Case WGWifi.AUTH_SHARED : Return "WEP condivisa"
            Case WGWifi.AUTH_WPA_PSK : Return "WPA personale"
            Case WGWifi.AUTH_RSNA_PSK : Return "WPA2 personale"
            Case WGWifi.AUTH_WPA3_SAE : Return "WPA3 personale"
            Case WGWifi.AUTH_OWE : Return "rete aperta cifrata"
            Case WGWifi.AUTH_WPA : Return "WPA aziendale"
            Case WGWifi.AUTH_RSNA : Return "WPA2 aziendale"
            Case WGWifi.AUTH_WPA3_ENT, WGWifi.AUTH_WPA3_ENT192 : Return "WPA3 aziendale"
            Case Else : Return "altra"
        End Select
    End Function

    Private Shared Function DescriviCifratura(rete As ReteWifi) As String
        Select Case rete.Cifratura
            Case 0 : Return "nessuna"
            Case 1, 5, &H101 : Return "WEP"
            Case 2 : Return "TKIP"
            Case 4, 8, 9, &HA : Return "AES"
            Case Else : Return "altra"
        End Select
    End Function

#End Region

#Region "Bluetooth"

    Private Shared _pinCollegato As Boolean

    ' Il PIN da scrivere si chiede con un campo, sul thread dell'interfaccia.
    Private Shared Sub CollegaRichiestaPin()
        If _pinCollegato Then Return
        _pinCollegato = True
        WGBluetooth.ChiediPin = Function(testo As String)
                                    Dim attesa As New TaskCompletionSource(Of String)()
                                    Dim mostra As Action = Sub()
                                                               Dim campi As New Campi()
                                                               campi.Aggiungi(testo, "pin", True)
                                                               AddHandler campi.Fine, Sub(s, e) attesa.TrySetResult(campi.RecuperaValore("pin"))
                                                               campi.Avvia()
                                                           End Sub
                                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                                    If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then contenitore.BeginInvoke(mostra) Else mostra()
                                    ' Senza risposta entro due minuti l'associazione si annulla.
                                    Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(Sub(t) attesa.TrySetResult(""))
                                    Return attesa.Task
                                End Function
    End Sub

    Private Class StatoBluetooth
        Public Acceso As Boolean?
        Public Dispositivi As New List(Of DispositivoBluetooth)()
    End Class

    Private Shared Async Function LeggiBluetoothAsync() As Task(Of StatoBluetooth)
        Dim stato As New StatoBluetooth()
        stato.Acceso = Await WGRadio.AccesaAsync(RadioKind.Bluetooth)
        If stato.Acceso.GetValueOrDefault() Then stato.Dispositivi = Await WGBluetooth.AssociatiAsync()
        Return stato
    End Function

    Public Shared Async Sub MostraBluetooth()
        Try
            CollegaRichiestaPin()
            Dim iniziale As StatoBluetooth = Await WGProcessoInCorso.EseguiAsync("Lettura del Bluetooth", Function() LeggiBluetoothAsync())
            If iniziale.Acceso Is Nothing Then
                Messaggio("Questo computer non ha il Bluetooth, oppure è disattivato in Windows.")
                Return
            End If
            AggiornaStatoBluetooth(iniziale.Acceso, iniziale.Dispositivi)
            If Not iniziale.Acceso.Value Then
                WGDomanda.Chiedi("Il Bluetooth è spento. Vuoi accenderlo?", Async Sub()
                                                                               If Await CambiaRadioAsync(RadioKind.Bluetooth, True, "Bluetooth") Then MostraBluetooth()
                                                                           End Sub)
                Return
            End If
            MostraElencoBluetooth(iniziale.Dispositivi)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.MostraBluetooth: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraElencoBluetooth(dispositiviIniziali As List(Of DispositivoBluetooth))
        Dim aperta As Boolean = True
        Dim dispositivi As New List(Of DispositivoBluetooth)(dispositiviIniziali)
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = False
        Dim chiudi As Action = Sub()
                                   aperta = False
                                   lista.abbandona()
                               End Sub
        AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) chiudi()
        Dim scelto As Func(Of DispositivoBluetooth) = Function()
                                                          Dim id As String = lista.ottieniElemento()
                                                          Dim numero As Integer
                                                          If id Is Nothing OrElse Not id.StartsWith("disp|") OrElse Not Integer.TryParse(id.Substring(5), numero) OrElse numero >= dispositivi.Count Then Return Nothing
                                                          Return dispositivi(numero)
                                                      End Function
        Dim riempi As Func(Of Task) = Async Function()
                                          Dim stato As StatoBluetooth = Await WGProcessoInCorso.EseguiAsync("Lettura dei dispositivi Bluetooth", Function() LeggiBluetoothAsync())
                                          If Not aperta Then Return
                                          AggiornaStatoBluetooth(stato.Acceso, stato.Dispositivi)
                                          If Not stato.Acceso.GetValueOrDefault() Then
                                              chiudi()
                                              Leggi("Il Bluetooth è spento")
                                              Return
                                          End If
                                          dispositivi.Clear()
                                          dispositivi.AddRange(stato.Dispositivi)
                                          RiempiBluetooth(lista, dispositivi)
                                      End Function
        AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                  If id = "nuovo" Then
                                                      AssociaNuovo(riempi)
                                                      Return
                                                  End If
                                                  Dim dispositivo As DispositivoBluetooth = scelto()
                                                  If dispositivo Is Nothing Then Return
                                                  Dim audio As UscitaBluetooth = UscitaAudioDi(dispositivo)
                                                  If audio IsNot Nothing AndAlso Not audio.Collegata Then
                                                      WGUscitaAudio.CollegaEUsa(audio)
                                                  ElseIf Not dispositivo.Connesso Then
                                                      CollegaDispositivo(dispositivo, riempi)
                                                  Else
                                                      MostraInformazioniDispositivo(dispositivo)
                                                  End If
                                              End Sub
        lista.aggiungiOpzione("Informazioni sul dispositivo", Sub(s, e)
                                                                  Dim dispositivo As DispositivoBluetooth = scelto()
                                                                  If dispositivo IsNot Nothing Then MostraInformazioniDispositivo(dispositivo)
                                                              End Sub)
        lista.aggiungiOpzione("Collega o scollega l'audio del dispositivo", Sub(s, e)
                                                                                Dim dispositivo As DispositivoBluetooth = scelto()
                                                                                If dispositivo Is Nothing Then Return
                                                                                Dim audio As UscitaBluetooth = UscitaAudioDi(dispositivo)
                                                                                If audio Is Nothing Then
                                                                                    Messaggio(dispositivo.Nome & " non è un dispositivo audio, come cuffie o casse.")
                                                                                ElseIf audio.Collegata Then
                                                                                    WGUscitaAudio.Scollega(audio)
                                                                                Else
                                                                                    WGUscitaAudio.CollegaEUsa(audio)
                                                                                End If
                                                                            End Sub)
        lista.aggiungiOpzione("Dissocia il dispositivo", Sub(s, e)
                                                             Dim dispositivo As DispositivoBluetooth = scelto()
                                                             If dispositivo IsNot Nothing Then Dissocia(dispositivo, riempi)
                                                         End Sub)
        lista.aggiungiOpzione("Passa l'audio dagli altoparlanti alle cuffie e viceversa", Sub(s, e) WGUscitaAudio.PassaAlSuccessivo())
        lista.aggiungiOpzione("Associa un nuovo dispositivo", Sub(s, e) AssociaNuovo(riempi))
        lista.aggiungiOpzione("Aggiorna", Async Sub(s, e) Await riempi())
        lista.aggiungiOpzione("Spegni il Bluetooth", Async Sub(s, e)
                                                         If Await CambiaRadioAsync(RadioKind.Bluetooth, False, "Bluetooth") Then
                                                             AggiornaStatoBluetooth(False, New List(Of DispositivoBluetooth)())
                                                             chiudi()
                                                         End If
                                                     End Sub)
        RiempiBluetooth(lista, dispositivi)
        lista.avvia()
    End Sub

    Private Shared Sub RiempiBluetooth(lista As WGMenuLista, dispositivi As List(Of DispositivoBluetooth))
        Dim connessi As Integer = dispositivi.Where(Function(d) d.Connesso).Count()
        Dim indice As Integer = Math.Max(0, lista.ottieniIndice())
        lista.svuotaLista()
        lista.impostaTitolo("Elenco dispositivi Bluetooth")
        For i As Integer = 0 To dispositivi.Count - 1
            lista.aggiungiElemento(dispositivi(i).Descrizione, "disp|" & i)
        Next
        lista.aggiungiElemento("Associa un nuovo dispositivo", "nuovo")
        If indice > 0 Then lista.selezionaElemento(Math.Min(indice, dispositivi.Count))
    End Sub

    ' L'uscita audio stereo del dispositivo (non il vivavoce), se è cuffie o casse.
    Private Shared Function UscitaAudioDi(dispositivo As DispositivoBluetooth) As UscitaBluetooth
        Try
            Dim uscite As List(Of UscitaBluetooth) = WGCuffieBluetooth.DiDispositivo(dispositivo.Nome)
            Return If(uscite.FirstOrDefault(Function(u) Not u.Vivavoce), uscite.FirstOrDefault())
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.UscitaAudioDi: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub MostraInformazioniDispositivo(dispositivo As DispositivoBluetooth)
        Messaggio(dispositivo.Nome & vbCrLf & If(dispositivo.Connesso, "Connesso", "Non connesso") & vbCrLf & "Associato a questo computer" & vbCrLf & If(dispositivo.BassoConsumo, "Bluetooth a basso consumo", "Bluetooth classico"))
    End Sub

    Private Shared Async Sub CollegaDispositivo(dispositivo As DispositivoBluetooth, aggiorna As Func(Of Task))
        Try
            Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Collegamento a " & dispositivo.Nome, Function() WGBluetooth.ConnettiAsync(dispositivo))
            If esito <> "" Then
                Messaggio(esito)
                Return
            End If
            Leggi("Connesso a " & dispositivo.Nome)
            Dim audio As UscitaBluetooth = UscitaAudioDi(dispositivo)
            If audio IsNot Nothing AndAlso Not audio.Collegata Then WGUscitaAudio.CollegaEUsa(audio)
            Await aggiorna()
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.CollegaDispositivo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Dissocia(dispositivo As DispositivoBluetooth, aggiorna As Func(Of Task))
        WGDomanda.Chiedi("Dissociare " & dispositivo.Nome & "? Per usarlo di nuovo andrà associato un'altra volta",
                         Async Sub()
                             Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Dissociazione di " & dispositivo.Nome, Function() WGBluetooth.DissociaAsync(dispositivo.Id))
                             If esito <> "" Then
                                 Messaggio(esito)
                                 Return
                             End If
                             Leggi(dispositivo.Nome & " dissociato")
                             Await aggiorna()
                         End Sub)
    End Sub

    Private Shared Async Sub AssociaNuovo(aggiorna As Func(Of Task))
        Try
            Dim vicini As List(Of DispositivoBluetooth) = Await WGProcessoInCorso.EseguiAsync("Ricerca dei dispositivi Bluetooth vicini, circa dieci secondi", Function() WGBluetooth.CercaVicinAsync(10))
            If vicini.Count = 0 Then
                Messaggio("Nessun dispositivo trovato: controlla che sia acceso e in modalità di associazione")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Dispositivi vicini")
            For i As Integer = 0 To vicini.Count - 1
                lista.aggiungiElemento(vicini(i).Descrizione & If(vicini(i).BassoConsumo, ", basso consumo", ""), i.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim numero As Integer
                                                      If Not Integer.TryParse(id, numero) OrElse numero >= vicini.Count Then Return
                                                      lista.abbandona()
                                                      Associa(vicini(numero), aggiorna)
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.AssociaNuovo: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub Associa(dispositivo As DispositivoBluetooth, aggiorna As Func(Of Task))
        Try
            Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Associazione con " & dispositivo.Nome, Function() WGBluetooth.AssociaAsync(dispositivo.Id))
            If esito <> "" Then
                Messaggio(esito)
                Return
            End If
            Leggi(dispositivo.Nome & " associato")
            Await aggiorna()
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.Associa: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Menu nella barra di stato"

    ' Dalla barra di stato della scrivania e della home, Wi-Fi e Bluetooth non aprono un elenco in un'altra
    ' finestra: il menu lo disegna e lo legge la griglia stessa (WGGriglia.ApriMenu), che ha già il fuoco.
    ' Nel menu ci sono due voci: l'interruttore ("Disattiva il Wi-Fi") e "Elenco reti Wi-Fi" (o dei
    ' dispositivi), che apre l'elenco in un secondo menu. Alt (o un colpo in su) apre le opzioni: quelle della
    ' voce scelta e, su ogni voce, "Aggiorna elenco" e le altre generali. Attese, domande, campi e messaggi
    ' sono gli stessi dell'elenco normale.

    ''' <summary>Menu del Wi-Fi disegnato nella griglia: attiva o disattiva e le reti; nelle opzioni aggiorna elenco e rete nascosta.</summary>
    Public Shared Sub MenuWifiNellaGriglia(griglia As WGGriglia)
        If griglia Is Nothing Then
            MostraWifi()
            Return
        End If
        Dim avviato As Task = CaricaMenuWifi(griglia, True, True)
    End Sub

    Private Const TitoloElencoWifi As String = "Elenco reti Wi-Fi"
    Private Const TitoloElencoBluetooth As String = "Elenco dispositivi Bluetooth"

    ' Legge lo stato e le reti (con cerca, facendo la scansione) e apre il menu o ne cambia il contenuto.
    ' Il menu ha due voci: l'interruttore e "Elenco reti Wi-Fi", che apre le reti in un secondo menu. Se
    ' l'elenco è aperto quando il contenuto cambia, resta aperto sulla stessa posizione.
    Private Shared Async Function CaricaMenuWifi(griglia As WGGriglia, cerca As Boolean, apri As Boolean) As Task
        Try
            If Not WGWifi.Disponibile() Then
                If apri Then Messaggio("Questo computer non ha una scheda Wi-Fi, oppure è disattivata in Windows.")
                Return
            End If
            Dim acceso As Boolean? = Await WGProcessoInCorso.EseguiAsync("Controllo del Wi-Fi", Function() WGRadio.AccesaAsync(RadioKind.WiFi))
            Dim spento As Boolean = acceso.HasValue AndAlso Not acceso.Value
            Dim reti As New List(Of ReteWifi)()
            If Not spento Then
                reti = Await WGProcessoInCorso.EseguiAsync(If(cerca, "Ricerca delle reti Wi-Fi", "Aggiornamento dell'elenco"),
                                                           Function() If(cerca, WGWifi.CercaRetiAsync(), Task.Run(Function() WGWifi.Reti())))
            End If
            If griglia.IsDisposed Then Return
            Dim aggiorna As Func(Of Task) = Function() CaricaMenuWifi(griglia, False, False)
            Dim cercaDiNuovo As Action = Async Sub() Await CaricaMenuWifi(griglia, True, False)
            Dim voci As New List(Of VoceMenuGriglia)()
            Dim elenco As New List(Of VoceMenuGriglia)()
            Dim titolo As String
            If spento Then
                titolo = "Wi-Fi spento"
                voci.Add(New VoceMenuGriglia("Attiva il Wi-Fi", Async Sub()
                                                                    If Await CambiaRadioAsync(RadioKind.WiFi, True, "Wi-Fi") Then cercaDiNuovo()
                                                                End Sub))
            Else
                ' Le opzioni che non dipendono dalla rete scelta: ci sono su ogni voce, in fondo.
                Dim generali As Func(Of List(Of VoceMenuGriglia)) =
                    Function() New List(Of VoceMenuGriglia) From {
                        New VoceMenuGriglia("Aggiorna elenco", cercaDiNuovo),
                        New VoceMenuGriglia("Collegati a una rete nascosta", Sub() ChiediReteNascosta(0, aggiorna))}
                Dim collegata As ReteWifi = reti.FirstOrDefault(Function(r) r.Connessa)
                titolo = "Wi-Fi, " & If(collegata IsNot Nothing, "connesso a " & collegata.NomeMostrato, "non connesso")
                voci.Add(New VoceMenuGriglia("Disattiva il Wi-Fi", Async Sub()
                                                                       If Await CambiaRadioAsync(RadioKind.WiFi, False, "Wi-Fi") Then Await aggiorna()
                                                                   End Sub, generali))
                For Each trovata As ReteWifi In reti
                    Dim rete As ReteWifi = trovata
                    elenco.Add(New VoceMenuGriglia(rete.Descrizione,
                                                   Sub()
                                                       ' Come nell'elenco: Invio collega, sulla rete collegata ne dà le informazioni.
                                                       If rete.Connessa Then
                                                           MostraInformazioniRete(rete)
                                                           griglia.RileggiMenu()
                                                       Else
                                                           Collega(rete, aggiorna)
                                                       End If
                                                   End Sub,
                                                   Function() OpzioniReteNelMenu(griglia, rete, aggiorna).Concat(generali()).ToList()))
                Next
                ' L'elenco delle reti è una voce a sé. Senza reti da mostrare non apre un elenco vuoto: fa una domanda.
                Dim servePosizione As Boolean = WGWifi.ServePermessoPosizione
                voci.Add(New VoceMenuGriglia(TitoloElencoWifi,
                                             Sub()
                                                 If servePosizione Then
                                                     WGDomanda.Chiedi("Le reti Wi-Fi non sono disponibili perché in Windows la posizione è spenta. Vuoi provare ad attivare la posizione?",
                                                                      Sub() AttivaPosizioneEMostraWifi(cercaDiNuovo))
                                                 ElseIf elenco.Count = 0 Then
                                                     Messaggio("Nessuna rete Wi-Fi trovata qui intorno")
                                                 Else
                                                     griglia.ApriSottomenu(TitoloElencoWifi, elenco)
                                                 End If
                                             End Sub, generali))
            End If
            If apri Then
                griglia.ApriMenu(titolo, voci)
                Return
            End If
            ' Con l'elenco aperto si resta nell'elenco, sulla stessa posizione.
            Dim posizione As Integer = griglia.PosizioneNelMenu(1)
            If posizione >= 0 AndAlso elenco.Count > 0 Then
                griglia.AggiornaMenu(titolo, voci, False)
                griglia.ApriSottomenu(TitoloElencoWifi, elenco, posizione)
            Else
                griglia.AggiornaMenu(titolo, voci)
            End If
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.CaricaMenuWifi: " & ex.Message)
        End Try
    End Function

    ' Le opzioni di una rete: collegati o disconnetti, informazioni, dimentica (solo per le reti salvate).
    Private Shared Function OpzioniReteNelMenu(griglia As WGGriglia, rete As ReteWifi, aggiorna As Func(Of Task)) As List(Of VoceMenuGriglia)
        Dim opzioni As New List(Of VoceMenuGriglia)()
        If rete.Connessa Then
            opzioni.Add(New VoceMenuGriglia(OpzioneDisconnetti, Sub() DisconnettiNelMenu(rete, aggiorna)))
        Else
            opzioni.Add(New VoceMenuGriglia(OpzioneCollegati, Sub() Collega(rete, aggiorna)))
        End If
        opzioni.Add(New VoceMenuGriglia(OpzioneInformazioniRete, Sub()
                                                                     MostraInformazioniRete(rete)
                                                                     griglia.RileggiMenu()
                                                                 End Sub))
        If rete.Profilo <> "" Then
            opzioni.Add(New VoceMenuGriglia(OpzioneDimentica, Sub() WGDomanda.Chiedi("Dimenticare la rete " & rete.NomeMostrato & "? La password salvata verrà cancellata",
                                                                                     Sub() DimenticaNelMenu(rete, aggiorna))))
        End If
        Return opzioni
    End Function

    Private Shared Async Sub DisconnettiNelMenu(rete As ReteWifi, aggiorna As Func(Of Task))
        Try
            Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync("Disconnessione da " & rete.NomeMostrato,
                                                                         Async Function()
                                                                             Dim esito As Boolean = WGWifi.Disconnetti()
                                                                             Await Task.Delay(1500)
                                                                             Return esito
                                                                         End Function)
            If Not riuscito Then
                Messaggio("Disconnessione da " & rete.NomeMostrato & " non riuscita")
                Return
            End If
            Leggi("Disconnesso da " & rete.NomeMostrato)
            Await aggiorna()
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.DisconnettiNelMenu: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub DimenticaNelMenu(rete As ReteWifi, aggiorna As Func(Of Task))
        Try
            If Not WGWifi.DimenticaProfilo(rete.Profilo) Then
                Messaggio("Non è stato possibile dimenticare " & rete.NomeMostrato)
                Return
            End If
            Leggi(rete.NomeMostrato & " dimenticata")
            Await aggiorna()
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.DimenticaNelMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Menu del Bluetooth disegnato nella griglia: attiva o disattiva, i dispositivi e la nuova associazione; nelle opzioni aggiorna elenco e l'audio.</summary>
    Public Shared Sub MenuBluetoothNellaGriglia(griglia As WGGriglia)
        If griglia Is Nothing Then
            MostraBluetooth()
            Return
        End If
        Dim avviato As Task = CaricaMenuBluetooth(griglia, True)
    End Sub

    ' Come il Wi-Fi: l'interruttore e "Elenco dispositivi Bluetooth", che apre i dispositivi in un secondo menu.
    Private Shared Async Function CaricaMenuBluetooth(griglia As WGGriglia, apri As Boolean) As Task
        Try
            CollegaRichiestaPin()
            Dim stato As StatoBluetooth = Await WGProcessoInCorso.EseguiAsync("Lettura del Bluetooth", Function() LeggiBluetoothAsync())
            If stato.Acceso Is Nothing Then
                If apri Then Messaggio("Questo computer non ha il Bluetooth, oppure è disattivato in Windows.")
                Return
            End If
            AggiornaStatoBluetooth(stato.Acceso, stato.Dispositivi)
            If griglia.IsDisposed Then Return
            Dim aggiorna As Func(Of Task) = Function() CaricaMenuBluetooth(griglia, False)
            Dim voci As New List(Of VoceMenuGriglia)()
            Dim elenco As New List(Of VoceMenuGriglia)()
            If Not stato.Acceso.Value Then
                voci.Add(New VoceMenuGriglia("Attiva il Bluetooth", Async Sub()
                                                                        If Await CambiaRadioAsync(RadioKind.Bluetooth, True, "Bluetooth") Then Await aggiorna()
                                                                    End Sub))
            Else
                ' Le opzioni che non dipendono dal dispositivo scelto: ci sono su ogni voce, in fondo.
                Dim generali As Func(Of List(Of VoceMenuGriglia)) =
                    Function() New List(Of VoceMenuGriglia) From {
                        New VoceMenuGriglia("Aggiorna elenco", Async Sub() Await aggiorna()),
                        New VoceMenuGriglia("Passa l'audio dagli altoparlanti alle cuffie e viceversa", Sub() WGUscitaAudio.PassaAlSuccessivo())}
                voci.Add(New VoceMenuGriglia("Disattiva il Bluetooth", Async Sub()
                                                                           If Await CambiaRadioAsync(RadioKind.Bluetooth, False, "Bluetooth") Then Await aggiorna()
                                                                       End Sub, generali))
                For Each trovato As DispositivoBluetooth In stato.Dispositivi
                    Dim dispositivo As DispositivoBluetooth = trovato
                    elenco.Add(New VoceMenuGriglia(dispositivo.Descrizione,
                                                   Sub()
                                                       Dim audio As UscitaBluetooth = UscitaAudioDi(dispositivo)
                                                       If audio IsNot Nothing AndAlso Not audio.Collegata Then
                                                           WGUscitaAudio.CollegaEUsa(audio)
                                                       ElseIf Not dispositivo.Connesso Then
                                                           CollegaDispositivo(dispositivo, aggiorna)
                                                       Else
                                                           MostraInformazioniDispositivo(dispositivo)
                                                           griglia.RileggiMenu()
                                                       End If
                                                   End Sub,
                                                   Function() OpzioniDispositivoNelMenu(griglia, dispositivo, aggiorna).Concat(generali()).ToList()))
                Next
                elenco.Add(New VoceMenuGriglia("Associa un nuovo dispositivo", Sub() AssociaNuovoNelMenu(griglia, aggiorna), generali))
                voci.Add(New VoceMenuGriglia(TitoloElencoBluetooth, Sub() griglia.ApriSottomenu(TitoloElencoBluetooth, elenco), generali))
            End If
            ' Il titolo è lo stato, lo stesso della barra: "Bluetooth acceso, connesso a ...".
            If apri Then
                griglia.ApriMenu(_statoBluetooth, voci)
                Return
            End If
            ' Con l'elenco aperto si resta nell'elenco, sulla stessa posizione.
            Dim posizione As Integer = griglia.PosizioneNelMenu(1)
            If posizione >= 0 AndAlso elenco.Count > 0 Then
                griglia.AggiornaMenu(_statoBluetooth, voci, False)
                griglia.ApriSottomenu(TitoloElencoBluetooth, elenco, posizione)
            Else
                griglia.AggiornaMenu(_statoBluetooth, voci)
            End If
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.CaricaMenuBluetooth: " & ex.Message)
        End Try
    End Function

    ' Le opzioni di un dispositivo: informazioni, l'audio (solo per cuffie e casse), dissocia.
    Private Shared Function OpzioniDispositivoNelMenu(griglia As WGGriglia, dispositivo As DispositivoBluetooth, aggiorna As Func(Of Task)) As List(Of VoceMenuGriglia)
        Dim opzioni As New List(Of VoceMenuGriglia)()
        opzioni.Add(New VoceMenuGriglia("Informazioni sul dispositivo", Sub()
                                                                            MostraInformazioniDispositivo(dispositivo)
                                                                            griglia.RileggiMenu()
                                                                        End Sub))
        Dim audio As UscitaBluetooth = UscitaAudioDi(dispositivo)
        If audio IsNot Nothing Then
            If audio.Collegata Then
                opzioni.Add(New VoceMenuGriglia("Scollega l'audio del dispositivo", Sub() WGUscitaAudio.Scollega(audio)))
            Else
                opzioni.Add(New VoceMenuGriglia("Collega l'audio del dispositivo", Sub() WGUscitaAudio.CollegaEUsa(audio)))
            End If
        End If
        opzioni.Add(New VoceMenuGriglia("Dissocia il dispositivo", Sub() Dissocia(dispositivo, aggiorna)))
        Return opzioni
    End Function

    ' Cerca i dispositivi vicini e li mostra come sottomenu: Invio su uno lo associa, Esc torna al Bluetooth.
    Private Shared Async Sub AssociaNuovoNelMenu(griglia As WGGriglia, aggiorna As Func(Of Task))
        Try
            Dim vicini As List(Of DispositivoBluetooth) = Await WGProcessoInCorso.EseguiAsync("Ricerca dei dispositivi Bluetooth vicini, circa dieci secondi", Function() WGBluetooth.CercaVicinAsync(10))
            If vicini.Count = 0 Then
                Messaggio("Nessun dispositivo trovato: controlla che sia acceso e in modalità di associazione")
                Return
            End If
            If griglia.IsDisposed Then Return
            Dim voci As New List(Of VoceMenuGriglia)()
            For Each trovato As DispositivoBluetooth In vicini
                Dim vicino As DispositivoBluetooth = trovato
                voci.Add(New VoceMenuGriglia(vicino.Descrizione & If(vicino.BassoConsumo, ", basso consumo", ""),
                                             Sub()
                                                 griglia.ChiudiSottomenu()
                                                 Associa(vicino, aggiorna)
                                             End Sub))
            Next
            griglia.ApriSottomenu("Dispositivi vicini", voci)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.AssociaNuovoNelMenu: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Stato per la scrivania"

    Private Shared _statoBluetooth As String = ""
    Private Shared _istanteStatoBluetooth As DateTime = DateTime.MinValue
    Private Shared _aggiornamentoInCorso As Boolean

    Private Shared Sub AggiornaStatoBluetooth(acceso As Boolean?, dispositivi As List(Of DispositivoBluetooth))
        If acceso Is Nothing Then
            _statoBluetooth = "Bluetooth non presente"
        ElseIf Not acceso.Value Then
            _statoBluetooth = "Bluetooth spento"
        Else
            Dim connessi As List(Of DispositivoBluetooth) = dispositivi.Where(Function(d) d.Connesso).ToList()
            _statoBluetooth = "Bluetooth acceso" & If(connessi.Count = 0, "", If(connessi.Count = 1, ", connesso a " & connessi(0).Nome, ", " & connessi.Count & " dispositivi connessi"))
        End If
        _istanteStatoBluetooth = DateTime.Now
    End Sub

    ''' <summary>Stato del Bluetooth in breve; si aggiorna in sottofondo ogni venti secondi (non è un'attesa dell'utente).</summary>
    Public Shared Function DescrizioneBluetooth() As String
        If (DateTime.Now - _istanteStatoBluetooth).TotalSeconds > 20 AndAlso Not _aggiornamentoInCorso Then AggiornaStatoBluetoothAsync()
        Return If(_statoBluetooth = "", "Bluetooth: controllo in corso", _statoBluetooth)
    End Function

    Private Shared Async Sub AggiornaStatoBluetoothAsync()
        _aggiornamentoInCorso = True
        Try
            Dim stato As StatoBluetooth = Await LeggiBluetoothAsync()
            AggiornaStatoBluetooth(stato.Acceso, stato.Dispositivi)
        Catch ex As Exception
            log.ScriviLog("WGConnessioni.AggiornaStatoBluetoothAsync: " & ex.Message)
        Finally
            _aggiornamentoInCorso = False
        End Try
    End Sub

#End Region

End Class