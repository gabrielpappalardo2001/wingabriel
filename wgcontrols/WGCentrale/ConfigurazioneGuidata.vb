Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' La configurazione guidata di WinGabriel. Parte da sola la prima volta con un profilo nuovo e si ripete quando si vuole da Impostazioni,
''' Sistema. Passi, uno dopo l'altro:
'''   1. installare WinGabriel o tenerlo portable;
'''   2. la cartella dei dati, con la prova di lettura e scrittura;
'''   3. la sintesi vocale;
'''   4. la sincronizzazione dei dati con altri computer: account Filen (crearlo o accedere) e controllo dei dati da sincronizzare;
'''   5. l'installazione, se scelta.
''' Tutto passa dai menu e dai campi di WinGabriel, quindi si sente: niente finestre di Windows. Chiudendo un qualsiasi menu o campo la configurazione si ferma e non riparte da sola.
''' </summary>
Friend NotInheritable Class ConfigurazioneGuidata

    Friend Const IdFatta As String = "configurazione_guidata_fatta"
    ' Messo dall'installazione che porta la configurazione: dalla copia installata la procedura riparte da sintesi e Filen.
    Friend Const IdDaContinuare As String = "configurazione_guidata_da_continuare"

    Private Shared _inCorso As Boolean

    ''' <summary>Lanciata quando si chiude un menu, un campo o una finestra: la procedura si ferma del tutto.</summary>
    Private NotInheritable Class ProceduraAnnullata
        Inherits Exception
    End Class

    Private Sub New()
    End Sub

    Friend Shared Function DaContinuareDopoInstallazione() As Boolean
        Try
            Return (New WGConfig()).LeggiValore(IdDaContinuare) = "1"
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Da un menu: la configurazione guidata, quando la si vuole.</summary>
    Friend Shared Async Sub Avvia()
        Await EseguiAsync()
    End Sub

    Friend Shared Async Function EseguiAsync() As Task
        If _inCorso Then Return
        _inCorso = True
        Try
            Dim continua As Boolean = DaContinuareDopoInstallazione()
            ' Già installato (anche appena riavviato dalla copia installata): non si richiede l'installazione.
            Dim installare As Boolean = If(continua OrElse IntegrazioneWindows.EInstallato(), False, Await ChiediInstallazioneAsync())
            If installare Then
                ' L'installazione sposta il programma: finita, WinGabriel riparte dalla nuova copia e la procedura continua da lì.
                If Await InstallaAsync() Then Return
            Else
                If Not continua AndAlso Not IntegrazioneWindows.EInstallato() Then
                    Await AvvioAmministratoreAsync()
                    Await CartellaDatiAsync()
                ElseIf Not continua Then
                    Await CartellaDatiAsync()
                End If
                Await SintesiAsync()
                Await SincronizzazioneAsync()
            End If
            SegnaFatta()
            Messaggio("La configurazione è finita. Puoi ripeterla quando vuoi da Impostazioni, Sistema, Configurazione guidata.")
        Catch ex As ProceduraAnnullata
            SegnaFatta()
            Messaggio("La configurazione guidata è stata chiusa e non verrà riproposta. È possibile accedere nuovamente alla procedura di configurazione da Impostazioni, Sistema, Configurazione guidata.")
        Catch ex As Exception
            Messaggio("La configurazione guidata si è interrotta.")
        Finally
            _inCorso = False
        End Try
    End Function

    Private Shared Async Function AvvioAmministratoreAsync() As Task
        Dim richiedi As Boolean = Await WGDomanda.ChiediAsync("Vuoi che WinGabriel chieda la conferma di amministratore a ogni avvio? Con i privilegi di amministratore il lettore di schermo legge anche le applicazioni avviate come amministratore. Se rispondi no, WinGabriel parte sempre senza privilegi. Puoi cambiare scelta da Impostazioni, Avvio, Avvio come amministratore.")
        Try
            Dim config As New WGConfig()
            config.SalvaValore(AvvioAmministratore.ID_CONFIG, If(richiedi, "true", "false"))
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("ConfigurazioneGuidata.AvvioAmministratoreAsync: " & ex.Message)
        End Try
    End Function

    Private Shared Sub SegnaFatta()
        Try
            Dim config As New WGConfig()
            config.SalvaValore(IdFatta, "1")
            config.SalvaValore(IdDaContinuare, "0")
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Piccoli aiuti per i passi: ogni domanda si aspetta con Await
    ' ------------------------------------------------------------------------------------------

    Friend Shared Sub Messaggio(testo As String)
        Dim m As New WGMessaggi()
        m.Messaggio(testo)
        m.ShowDialog()
    End Sub

    ''' <summary>Un menu di scelte. Restituisce la posizione scelta, oppure -1 con Esc.</summary>
    Private Shared Function ScegliAsync(titolo As String, voci As String()) As Task(Of Integer)
        Dim esito As New TaskCompletionSource(Of Integer)()
        Dim menu As New WGMenu()
        For i As Integer = 0 To voci.Length - 1
            Dim indice As Integer = i
            menu.aggiungi(voci(i), Sub()
                                       esito.TrySetResult(indice)
                                       Try
                                           menu.abbandona()
                                       Catch
                                       End Try
                                   End Sub)
        Next
        AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult(-1)
        menu.avvia(titolo, True)
        Return Controlla(esito.Task)
    End Function

    Private Shared Async Function Controlla(attesa As Task(Of Integer)) As Task(Of Integer)
        Dim scelta As Integer = Await attesa
        If scelta < 0 Then Throw New ProceduraAnnullata()
        Return scelta
    End Function

    ''' <summary>Dei campi da compilare. Restituisce i valori nell'ordine degli identificativi, oppure Nothing se si annulla.</summary>
    Private Shared Function CampiAsync(configura As Action(Of Campi), identificativi As String()) As Task(Of String())
        Dim esito As New TaskCompletionSource(Of String())()
        Dim campi As New Campi()
        configura(campi)
        AddHandler campi.Fine, Sub()
                                   Try
                                       Dim valori(identificativi.Length - 1) As String
                                       For i As Integer = 0 To identificativi.Length - 1
                                           valori(i) = campi.RecuperaValore(identificativi(i))
                                       Next
                                       esito.TrySetResult(valori)
                                   Catch
                                       esito.TrySetResult(Nothing)
                                   End Try
                               End Sub
        ' La chiusura può arrivare insieme alla conferma: le si dà il tempo di lasciar passare prima i valori.
        AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
        campi.Avvia()
        Return ControllaCampi(esito.Task)
    End Function

    Private Shared Async Function ControllaCampi(attesa As Task(Of String())) As Task(Of String())
        Dim valori As String() = Await attesa
        If valori Is Nothing Then Throw New ProceduraAnnullata()
        Return valori
    End Function

    ' ------------------------------------------------------------------------------------------
    ' 1. Installazione o portable
    ' ------------------------------------------------------------------------------------------

    Private Shared Async Function ChiediInstallazioneAsync() As Task(Of Boolean)
        ' se WinGabriel è già avviato come amministratore Windows non chiede nessuna conferma: inutile dirlo
        Dim attenzione As String = If(AvvioAmministratore.Elevato(), "", " Windows chiederà la conferma una sola volta.")
        Dim scelta As Integer = Await ScegliAsync("Come vuoi usare WinGabriel?", New String() {
            "Installa WinGabriel: le icone sul desktop e nel menu Start lo avviano con i privilegi di amministratore, senza altre richieste." & attenzione,
            "Usalo portatile: nessuna icona e nessuna modifica a Windows, tutto resta nella sua cartella"})
        Return scelta = 0
    End Function

    ''' <summary>
    ''' L'installazione: chiede se copiare la configurazione attuale (e in quel caso la cartella dei dati è obbligatoria), copia il programma
    ''' in Programmi, prepara il profilo e riavvia WinGabriel dalla nuova copia. Restituisce vero se WinGabriel sta ripartendo da lì: chi
    ''' chiama deve fermarsi. Se un menu viene chiuso lancia ProceduraAnnullata.
    ''' </summary>
    Friend Shared Async Function InstallaAsync() As Task(Of Boolean)
        If IntegrazioneWindows.EInstallato() Then
            Dim gia As Integer = Await WGProcessoInCorso.EseguiAsync("Aggiorno l'installazione di WinGabriel, attendi.",
                                                                     Function() Task.Run(Function() IntegrazioneWindows.Installa(False)))
            Messaggio(IntegrazioneWindows.Esito(gia, True))
            Return False
        End If

        Dim copiaConfigurazione As Boolean = Await WGDomanda.ChiediAsync("Vuoi portare nell'installazione la configurazione attuale, con i tuoi dati? Se rispondi no, al primo avvio dovrai scegliere tutto da capo.")
        Dim cartellaDati As String = ""
        If copiaConfigurazione Then
            Messaggio("Scegli la cartella in cui tenere i tuoi dati: va bene una qualsiasi, tranne la cartella dei programmi.")
            Do
                cartellaDati = Await ScegliCartellaAsync()
                cartellaDati = Await ScegliSottocartellaDatiAsync(cartellaDati)
                If cartellaDati = "" Then Throw New ProceduraAnnullata()
                Dim programmi As String = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                If cartellaDati.StartsWith(programmi, StringComparison.OrdinalIgnoreCase) Then
                    Messaggio("Per i dati non va bene la cartella dei programmi. Scegline un'altra.")
                    Continue Do
                End If
                Dim errore As String = ProvaLetturaScrittura(cartellaDati)
                If errore = "" Then
                    WGDb.WGDatabase.PreparaCartellaDati(cartellaDati)
                    Exit Do
                End If
                Messaggio("In " & cartellaDati & " non riesco a leggere e scrivere (" & errore & "). Scegline un'altra.")
            Loop
        End If

        Dim profiloPronto As Boolean = Await WGProcessoInCorso.EseguiAsync("Preparo la configurazione della nuova copia, attendi.",
                                                                           Function() Task.Run(Function() IntegrazioneWindows.PreparaProfilo(copiaConfigurazione, cartellaDati)))
        If Not profiloPronto Then
            Messaggio("Non è stato possibile preparare la configurazione della nuova copia: non è cambiato niente.")
            Return False
        End If
        Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync("Installo WinGabriel, attendi." & If(AvvioAmministratore.Elevato(), "", " Windows potrebbe chiederti una conferma."),
                                                                    Function() Task.Run(Function() IntegrazioneWindows.Installa(True)))
        If codice <> 0 Then
            ' Il profilo preparato punterebbe a una copia che non c'è: si torna a quello di questo programma.
            IntegrazioneWindows.RipristinaProfilo()
            Messaggio(IntegrazioneWindows.Esito(codice, True))
            Return False
        End If
        Messaggio("Fatto. WinGabriel è installato in " & IntegrazioneWindows.CartellaInstallazione() & " e adesso viene riavviato dalla copia installata." & If(copiaConfigurazione, "", " Al primo avvio dovrai scegliere tutto."))
        IntegrazioneWindows.RiavviaDallaCopiaInstallata()
        Return True
    End Function

    ' Da una copia portatile, con WinGabriel già installato: sostituisce il programma installato con questo, senza altre domande, e riparte da lì.
    Friend Shared Async Function AggiornaInstallazioneAsync() As Task
        Try
            Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync("Aggiorno l'installazione di WinGabriel, attendi." & If(AvvioAmministratore.Elevato(), "", " Windows potrebbe chiederti una conferma."),
                                                                        Function() Task.Run(Function() IntegrazioneWindows.Installa(True)))
            If codice <> 0 Then
                Messaggio(IntegrazioneWindows.Esito(codice, True))
                Return
            End If
            Messaggio("L'installazione è aggiornata: adesso WinGabriel viene riavviato dalla copia installata.")
            IntegrazioneWindows.RiavviaDallaCopiaInstallata()
        Catch ex As Exception
            Messaggio("Non è stato possibile aggiornare l'installazione.")
        End Try
    End Function

    ' Toglie l'installazione di WinGabriel dal computer, dopo aver chiesto conferma.
    Friend Shared Async Function DisinstallaAsync() As Task
        Try
            If Not Await WGDomanda.ChiediAsync("Vuoi disinstallare WinGabriel? Vengono tolte l'installazione in Programmi, le icone e l'avvio con i privilegi di amministratore; i tuoi dati e la tua configurazione restano dove sono.") Then Return
            Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync("Disinstallo WinGabriel, attendi." & If(AvvioAmministratore.Elevato(), "", " Windows potrebbe chiederti una conferma."),
                                                                        Function() Task.Run(Function() IntegrazioneWindows.Disinstalla()))
            Messaggio(IntegrazioneWindows.Esito(codice, False))
            ' sta girando dalla cartella appena disinstallata: la si cancella a programma chiuso
            If codice = 0 AndAlso IntegrazioneWindows.EInstallato() Then IntegrazioneWindows.CancellaCartellaInstallazioneDopoUscita()
        Catch ex As Exception
            Messaggio("Non è stato possibile disinstallare WinGabriel.")
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------
    ' 2. Cartella dei dati
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Scrive un file nella cartella, lo rilegge e lo cancella. Restituisce il testo dell'errore, o una stringa vuota se è andata bene.</summary>
    Friend Shared Function ProvaLetturaScrittura(cartella As String) As String
        Try
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Dim prova As String = Path.Combine(cartella, ".wgprova-" & Guid.NewGuid().ToString("N") & ".tmp")
            Dim contenuto As String = "WinGabriel " & Guid.NewGuid().ToString("N")
            File.WriteAllText(prova, contenuto)
            Dim letto As String = File.ReadAllText(prova)
            File.Delete(prova)
            If letto <> contenuto Then Return "il file scritto non corrisponde a quello riletto"
            Return ""
        Catch ex As Exception
            Return ex.Message
        End Try
    End Function

    Private Shared Function HaGiaDati(cartella As String) As Boolean
        Try
            If Not Directory.Exists(cartella) Then Return False
            For Each file As String In Directory.EnumerateFiles(cartella, "*", SearchOption.AllDirectories)
                Dim relativo As String = file.Substring(cartella.Length).ToLowerInvariant()
                If relativo.Contains("\config\") OrElse relativo.Contains("\logs\") OrElse relativo.Contains("\log\") OrElse relativo.Contains("\cache\") Then Continue For
                Return True
            Next
        Catch
        End Try
        Return False
    End Function

    Private Shared Function ScegliCartellaAsync() As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Dim dialogo As New WGFileDialog()
        AddHandler dialogo.singolaselezione, Sub(elemento As String) esito.TrySetResult(elemento)
        AddHandler dialogo.annullata, Sub() esito.TrySetResult("")
        dialogo.Avvia("")
        Return ControllaCartella(esito.Task)
    End Function

    ''' <summary>
    ''' Dopo aver scelto una cartella per i dati: chiede se crearci dentro una sottocartella WinGabriel o usarla direttamente.
    ''' Restituisce la cartella finale (non ancora creata), oppure "" se si chiude il menu. Se la cartella è già una cartella dei dati
    ''' di WinGabriel non chiede niente.
    ''' </summary>
    Friend Shared Function ScegliSottocartellaDatiAsync(cartella As String) As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Try
            If WGDb.WGDatabase.ECartellaDatiWinGabriel(cartella) Then
                esito.TrySetResult(cartella)
                Return esito.Task
            End If
            Dim menu As New WGMenu()
            Dim conSotto As String = Path.Combine(cartella, WGDb.WGDatabase.NomeCartellaContenitoreDati)
            menu.aggiungi("Crea una sottocartella chiamata " & WGDb.WGDatabase.NomeCartellaContenitoreDati & " e salva lì i dati", Sub()
                                                                                                                                    esito.TrySetResult(conSotto)
                                                                                                                                    Try
                                                                                                                                        menu.abbandona()
                                                                                                                                    Catch
                                                                                                                                    End Try
                                                                                                                                End Sub)
            menu.aggiungi("Salva i dati direttamente nella cartella scelta", Sub()
                                                                                 esito.TrySetResult(cartella)
                                                                                 Try
                                                                                     menu.abbandona()
                                                                                 Catch
                                                                                 End Try
                                                                             End Sub)
            AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult("")
            menu.avvia("Come salvare i dati in " & cartella & "?", True)
        Catch
            esito.TrySetResult("")
        End Try
        Return esito.Task
    End Function

    Private Shared Async Function ControllaCartella(attesa As Task(Of String)) As Task(Of String)
        Dim cartella As String = Await attesa
        If String.IsNullOrWhiteSpace(cartella) Then Throw New ProceduraAnnullata()
        Return cartella
    End Function

    Private Shared Async Function CartellaDatiAsync() As Task
        Dim predefinita As String = WGDb.WGDatabase.CartellaBaseDatiPredefinita()
        Dim attuale As String = WGDb.WGDatabase.CartellaBaseDatiEffettiva()
        If String.IsNullOrWhiteSpace(attuale) Then attuale = predefinita
        If HaGiaDati(attuale) Then
            Messaggio("In " & attuale & " ci sono già dei dati di WinGabriel: restano lì. Per spostarli in un'altra cartella usa Impostazioni, Applicazioni, File, Cartella dati dell'applicazione.")
            Return
        End If

        Do
            Dim scelta As Integer = Await ScegliAsync("Dove tenere i dati di WinGabriel?", New String() {
                "Nella cartella predefinita, accanto al programma: " & predefinita,
                "In un'altra cartella, per esempio su un altro disco o in una cartella sincronizzata"})
            Dim cartella As String = predefinita
            If scelta = 1 Then
                cartella = Await ScegliCartellaAsync()
                cartella = Await ScegliSottocartellaDatiAsync(cartella)
                If cartella = "" Then Throw New ProceduraAnnullata()
            End If
            Dim errore As String = ProvaLetturaScrittura(cartella)
            If errore <> "" Then
                Messaggio("La prova di lettura e scrittura in " & cartella & " non è riuscita: " & errore & ". Scegli un'altra cartella.")
                Continue Do
            End If
            Messaggio("La prova di lettura e scrittura in " & cartella & " è riuscita.")
            Try
                If scelta = 1 Then WGDb.WGDatabase.PreparaCartellaDati(cartella)
                Dim config As New WGConfig()
                config.SalvaValore(WGDb.WGDatabase.IdConfigurazioneCartellaBaseDati, If(scelta = 1, cartella, ""))
                WGDb.WGDatabase.AzzeraCacheCartellaBaseDati()
            Catch
                Messaggio("Non è stato possibile salvare la cartella scelta.")
            End Try
            Exit Do
        Loop
    End Function

    ' ------------------------------------------------------------------------------------------
    ' 3. Sintesi vocale
    ' ------------------------------------------------------------------------------------------

    Private Shared Async Function SintesiAsync() As Task
        If Not Await WGDomanda.ChiediAsync("Vuoi scegliere adesso la sintesi vocale, con la voce e la velocità? Per ora WinGabriel usa quella predefinita.") Then Return
        Dim fine As New TaskCompletionSource(Of Boolean)()
        Dim impostazioni As New Impostazioni()
        impostazioni.MostraSintesiPerProcedura(Sub() fine.TrySetResult(True))
        Await fine.Task
    End Function

    ' ------------------------------------------------------------------------------------------
    ' 4. Sincronizzazione con altri computer (account Filen)
    ' ------------------------------------------------------------------------------------------

    Private Shared Async Function SincronizzazioneAsync() As Task
        Dim accountEsistente As Boolean = False
        If Not WGSincronizzazioneCloud.Collegato Then
            If Not Await WGDomanda.ChiediAsync("Vuoi sincronizzare i dati di WinGabriel, come rubrica, agenda e radio, con altri computer? Serve un account Filen.") Then Return
            Dim modo As Integer = Await ScegliAsync("Account Filen", New String() {"Accedi con il mio account Filen", "Crea un nuovo account Filen"})
            Dim email As String = ""
            If modo = 1 Then
                email = Await CreaAccountAsync()
                If email = "" Then Return
                If Not Await WGDomanda.ChiediAsync("Filen ti ha scritto per confermare l'indirizzo. Se l'hai già confermato accedo adesso. Vuoi accedere adesso?") Then
                    Messaggio("Potrai accedere più tardi da Impostazioni, Sistema, Cloud.")
                    Return
                End If
            End If
            If Not Await AccediAsync(email) Then Return
            accountEsistente = (modo = 0)
        End If

        If accountEsistente Then
            Try
                Await RipristinoDaCloudAsync()
            Catch exRipristino As Exception
                Messaggio("Non è stato possibile controllare i backup sul cloud.")
            End Try
        End If

        Try
            WGSincronizzazioneCloud.SincronizzazioneAbilitata = True
        Catch
        End Try
        Await ControllaDatiAsync()
    End Function

    Private Shared Function LeggiBackupCloud() As System.Collections.Generic.List(Of InfoBackupCloud)
        Dim messaggio As String = ""
        Return WGCloudDati.ElencoBackupCloud(messaggio)
    End Function

    Private Shared Function ScaricaUltimoBackupCloud(nome As String) As String
        Dim percorso As String = ""
        Dim messaggio As String = ""
        If WGCloudDati.ScaricaBackupDalCloud(nome, percorso, messaggio) Then Return percorso
        Return ""
    End Function

    Private Shared Function RipristinaBackupScaricato(percorso As String) As String
        Dim messaggio As String = ""
        WGDb.WGBackup.Ripristina(percorso, "", messaggio)
        Return messaggio
    End Function

    Private Shared Async Function RipristinoDaCloudAsync() As Task
        Dim elenco As System.Collections.Generic.List(Of InfoBackupCloud) = Await WGProcessoInCorso.EseguiAsync("Cerco un backup sull'account, attendi",
                                                                          Function() Task.Run(Function() LeggiBackupCloud()))
        If elenco Is Nothing OrElse elenco.Count = 0 Then Return
        Dim backup As InfoBackupCloud = elenco(0)
        If Not Await WGDomanda.ChiediAsync("Sull'account c'è un backup dei dati di WinGabriel, del " & backup.Descrizione & ". Vuoi ripristinarlo adesso? I dati attuali verranno sostituiti, ma prima ne viene salvata una copia di sicurezza.") Then Return

        Dim percorso As String = Await WGProcessoInCorso.EseguiAsync("Scarico il backup dal cloud, attendi",
                                                                      Function() Task.Run(Function() ScaricaUltimoBackupCloud(backup.Nome)))
        If String.IsNullOrWhiteSpace(percorso) Then
            Messaggio("Non è stato possibile scaricare il backup dal cloud.")
            Return
        End If

        Dim esito As String = Await WGProcessoInCorso.EseguiAsync("Ripristino i dati, attendi",
                                                                   Function() Task.Run(Function() RipristinaBackupScaricato(percorso)))
        Messaggio(If(String.IsNullOrWhiteSpace(esito), "Ripristino terminato.", esito))
    End Function

    ''' <summary>Crea l'account. Restituisce l'indirizzo email se riuscito, altrimenti una stringa vuota.</summary>
    Private Shared Async Function CreaAccountAsync() As Task(Of String)
        Dim email As String = ""
        Do
            Dim valori As String() = Await CampiAsync(Sub(c As Campi)
                                                          c.Aggiungi("Indirizzo email per il nuovo account Filen", "g.email", True, False, email)
                                                          c.AggiungiPassword("Password. Filen cifra i dati con questa password: se la perdi, i dati non si recuperano", "g.password", True)
                                                          c.AggiungiPassword("Ripeti la password", "g.password2", True)
                                                      End Sub, New String() {"g.email", "g.password", "g.password2"})
            email = If(valori(0), "").Trim()
            If email = "" OrElse String.IsNullOrEmpty(valori(1)) Then
                Messaggio("Servono l'indirizzo email e la password.")
                Continue Do
            End If
            If Not String.Equals(valori(1), valori(2), StringComparison.Ordinal) Then
                Messaggio("Le due password non sono uguali. Riprova.")
                Continue Do
            End If
            Dim password As String = valori(1)
            Dim riuscita As Boolean = Await WGProcessoInCorso.EseguiAsync("Creazione dell'account in corso, attendi",
                                                                          Function() Task.Run(Function() WGSincronizzazioneCloud.Registra(email, password)))
            If riuscita Then
                Messaggio("Account creato. Filen ha inviato un'email di conferma a " & email & ": aprila e conferma l'indirizzo.")
                Return email
            End If
            Messaggio(TestoErrore("Non è stato possibile creare l'account."))
            If Not Await WGDomanda.ChiediAsync("Vuoi riprovare?") Then Return ""
        Loop
    End Function

    Private Shared Async Function AccediAsync(emailIniziale As String) As Task(Of Boolean)
        Dim email As String = emailIniziale
        Do
            Dim valori As String() = Await CampiAsync(Sub(c As Campi)
                                                          c.Aggiungi("Indirizzo email dell'account Filen", "g.email", True, False, email)
                                                          c.AggiungiPassword("Password", "g.password", True)
                                                          c.Aggiungi("Codice a due fattori, da scrivere solo se è attivo sul tuo account", "g.codice", False)
                                                      End Sub, New String() {"g.email", "g.password", "g.codice"})
            email = If(valori(0), "").Trim()
            If email = "" OrElse String.IsNullOrEmpty(valori(1)) Then
                Messaggio("Servono l'indirizzo email e la password.")
                Continue Do
            End If
            Dim password As String = valori(1)
            Dim codice As String = valori(2)
            Dim riuscita As Boolean = Await WGProcessoInCorso.EseguiAsync("Accesso in corso, attendi",
                                                                          Function() Task.Run(Function() WGSincronizzazioneCloud.Accedi(email, password, codice)))
            If riuscita Then
                Messaggio("Accesso eseguito.")
                Return True
            End If
            Messaggio(TestoErrore("Non è stato possibile eseguire l'accesso."))
            If Not Await WGDomanda.ChiediAsync("Vuoi riprovare?") Then Return False
        Loop
    End Function

    Private Shared Function TestoErrore(predefinito As String) As String
        Dim errore As String = WGSincronizzazioneCloud.UltimoErrore
        Return If(String.IsNullOrWhiteSpace(errore), predefinito, errore)
    End Function

    ''' <summary>Controlla se sull'account ci sono dati da sincronizzare e lo dice.</summary>
    Private Shared Async Function ControllaDatiAsync() As Task
        Try
            Dim esito As WGSincronizzazioneCloud.EsitoSincronizzazione = Await WGProcessoInCorso.EseguiAsync("Controllo se ci sono dati da sincronizzare, attendi",
                                                                          Function() Task.Run(Function() WGSincronizzazioneCloud.Sincronizza()))
            Dim testo As New System.Text.StringBuilder()
            If esito Is Nothing OrElse Not esito.Riuscita Then
                testo.Append("Il controllo non è riuscito. ").Append(TestoErrore(If(esito Is Nothing, "", esito.Messaggio)))
            Else
                testo.Append("Controllo terminato. ")
                If Not String.IsNullOrWhiteSpace(esito.Messaggio) Then testo.Append(esito.Messaggio).Append(" ")
                If esito.FileAggiornato Then testo.Append("I dati sull'account sono stati aggiornati. ")
                For Each avviso As String In esito.Avvisi
                    testo.Append(avviso).Append(" ")
                Next
            End If
            testo.Append(" Per scegliere che cosa sincronizzare, cioè rubrica, agenda, radio e testate dei quotidiani, apri Impostazioni, Sistema, Cloud, Sincronizzazione tra computer.")
            Messaggio(testo.ToString().Trim())
        Catch
            Messaggio("Il controllo dei dati da sincronizzare non è riuscito.")
        End Try
    End Function

End Class
