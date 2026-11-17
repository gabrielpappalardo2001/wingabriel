Imports System.Windows.Forms
Imports wgcontrols

Public Class WGGuida

    Private Shared ReadOnly _log As New Logger()

    Public Shared Sub MostraGuidaWGMenu()
        Try
            MostraTesto("Guida al menu", CostruisciGuidaWGMenu())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaWGMenu: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaWGMenuLista()
        Try
            MostraTesto("Guida alla lista", CostruisciGuidaWGMenuLista())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaWGMenuLista: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaCampi()
        Try
            MostraTesto("Guida al modulo", CostruisciGuidaCampi())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaCampi: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaCampoTesto()
        Try
            MostraTesto("Guida al campo di testo", CostruisciGuidaCampoTesto())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaWord()
        Try
            MostraTesto("Guida alla modalità Word", CostruisciGuidaWord())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaWord: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaPlayer()
        Try
            MostraTesto("Guida al player", CostruisciGuidaPlayer())
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaPlayer: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub MostraGuidaTipoCampo(tipoTag As String)
        Try
            MostraTesto("Guida al campo", CostruisciGuidaTipoCampo(tipoTag))
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraGuidaTipoCampo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraTesto(titolo As String, testo As String)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.Invoke(New Action(Of String, String)(AddressOf MostraTesto), titolo, testo)
                Return
            End If

            Dim campo As New WGCampo()
            Try
                campo.init(titolo, testo, multilinea:=True, solalettura:=True)
                campo.avvia()
            Catch exApertura As Exception
                _log.ScriviLog("WGGuida.MostraTesto (apertura): " & exApertura.Message)
            End Try
        Catch ex As Exception
            _log.ScriviLog("WGGuida.MostraTesto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CostruisciGuidaWGMenu() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida al menu di WinGabriel.")
            testo.AppendLine("Ti trovi in un menu con un elenco di voci. Quando raggiungi una voce, il suo testo viene letto automaticamente.")
            testo.AppendLine("Freccia su e freccia giù spostano alla voce precedente o successiva.")
            testo.AppendLine("Invio, oppure freccia destra, confermano la voce selezionata ed eseguono l'azione collegata.")
            testo.AppendLine("Escape, oppure freccia sinistra, oppure Backspace, chiudono il menu e tornano al menu precedente senza fare nulla.")
            testo.AppendLine("Il tasto Alt, quando disponibile, apre un menu con altre opzioni per la voce selezionata.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaWGMenu: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaWGMenuLista() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida alla lista di WinGabriel.")
            testo.AppendLine("Ti trovi in un elenco di elementi tra cui muoverti con le frecce. L'elemento corrente viene letto automaticamente.")
            testo.AppendLine("Freccia su e freccia giù spostano all'elemento precedente o successivo.")
            testo.AppendLine("Invio, oppure freccia destra, aprono o confermano l'elemento selezionato.")
            testo.AppendLine("Escape, oppure freccia sinistra, oppure Backspace, chiudono la lista o tornano indietro di un livello.")
            testo.AppendLine("Il tasto Alt apre il menu delle opzioni disponibili per l'elemento selezionato, se presenti.")
            testo.AppendLine("Alcune liste, come quella di una cartella o quella dei risultati di YouTube, permettono di selezionare più elementi insieme: sono le liste con multiselezione.")
            testo.AppendLine("In una lista con multiselezione, il tasto Shift attiva la selezione multipla.")
            testo.AppendLine("Se la lista non permette la selezione multipla, premendo Shift viene comunicato un messaggio di errore.")
            testo.AppendLine("Il tasto Spazio seleziona o deseleziona un elemento alla volta.")
            testo.AppendLine("Ctrl più A seleziona tutti gli elementi della lista.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaWGMenuLista: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaCampi() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida al modulo di WinGabriel.")
            testo.AppendLine("Questo modulo contiene più campi da compilare uno dopo l'altro. Quando un campo riceve il focus, vengono letti automaticamente il suo titolo, se è obbligatorio e il contenuto già presente.")
            testo.AppendLine("Invio conferma il campo corrente: se ci sono altri campi si passa al successivo, se è l'ultimo si conferma tutto il modulo.")
            testo.AppendLine("Freccia giù funziona come Invio: conferma il campo corrente e passa al successivo.")
            testo.AppendLine("Freccia su torna al campo precedente senza confermare quello corrente.")
            testo.AppendLine("Escape chiude il modulo. Se è stato scritto qualcosa in almeno un campo e il modulo lo prevede, viene chiesto se salvare le modifiche prima di uscire.")
            testo.AppendLine("Il tasto Alt, oppure il tasto Menu della tastiera, aprono il menu delle opzioni del campo corrente: taglia, copia, incolla, seleziona tutto, svuota il campo e, nei campi password, mostra o nascondi la password.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida generale al modulo.")
            testo.AppendLine("F2, quando il focus è su un campo di modifica, apre una spiegazione su cosa scrivere in quel campo.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaCampi: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaCampoTesto() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida al campo di testo di WinGabriel.")
            testo.AppendLine("Freccia sinistra e freccia destra muovono il cursore di un carattere. Con Ctrl ci si sposta di una parola intera. Con Shift si estende la selezione.")
            testo.AppendLine("Freccia su e freccia giù, nei campi con più righe, muovono il cursore alla riga precedente o successiva.")
            testo.AppendLine("Home e Fine portano il cursore all'inizio o alla fine della riga corrente. Con Ctrl portano all'inizio o alla fine di tutto il testo del campo.")
            testo.AppendLine("Pagina su e pagina giù muovono il cursore di una schermata di testo.")
            testo.AppendLine("Backspace e Canc cancellano il carattere prima o dopo il cursore, oppure il testo selezionato.")
            testo.AppendLine("Ctrl più A seleziona tutto il testo del campo.")
            testo.AppendLine("Ctrl più C copia il testo selezionato. Ctrl più X lo taglia. Ctrl più V incolla il testo copiato o tagliato.")
            testo.AppendLine("Ctrl più Z annulla l'ultima modifica. Ctrl più Y la ripristina.")
            testo.AppendLine("Inserisci più freccia giù legge ad alta voce il testo dal punto in cui si trova il cursore fino alla fine del campo.")
            testo.AppendLine("Nei campi di sola lettura, come questa guida, non è possibile modificare il testo, ma si può comunque leggerlo e spostare il cursore normalmente.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaCampoTesto: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaWord() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida alla modalità Word di WinGabriel.")
            testo.AppendLine()
            testo.AppendLine("Come muoversi nel documento.")
            testo.AppendLine("Le frecce muovono il cursore un carattere o una riga alla volta. Con Ctrl ci si sposta di parola in parola. Con Shift si estende la selezione.")
            testo.AppendLine("Home e Fine portano il cursore all'inizio o alla fine della riga corrente.")
            testo.AppendLine("Pagina su e pagina giù scorrono il documento di una schermata.")
            testo.AppendLine("Tab, dentro una tabella, passa alla cella successiva. Shift più Tab torna alla cella precedente.")
            testo.AppendLine()
            testo.AppendLine("Come modificare il testo.")
            testo.AppendLine("Backspace e Canc cancellano il carattere prima o dopo il cursore, oppure il testo selezionato.")
            testo.AppendLine("Invio crea un nuovo paragrafo, andando a capo.")
            testo.AppendLine("Ctrl più Z annulla l'ultima modifica. Ctrl più Y la ripristina.")
            testo.AppendLine("Ctrl più C copia il testo selezionato mantenendo la formattazione. Ctrl più X lo taglia. Ctrl più V lo incolla.")
            testo.AppendLine("Ctrl più A seleziona tutto il documento.")
            testo.AppendLine()
            testo.AppendLine("Formattazione del testo.")
            testo.AppendLine("Ctrl più G rende grassetto il testo selezionato.")
            testo.AppendLine("Ctrl più S sottolinea il testo selezionato.")
            testo.AppendLine("Ctrl più I legge le informazioni di formattazione nel punto in cui si trova il cursore.")
            testo.AppendLine("Ctrl più J allinea il paragrafo corrente giustificato.")
            testo.AppendLine()
            testo.AppendLine("Altre funzioni.")
            testo.AppendLine("Ctrl più T apre il menu trova e sostituisci.")
            testo.AppendLine("Ctrl più H apre il menu di intestazione, piè di pagina e note.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida.")
            testo.AppendLine("Shift più F1 legge il numero della nota nel punto in cui si trova il cursore.")
            testo.AppendLine("F5 avvia la lettura continua del documento dal punto in cui si trova il cursore. F8 avvia o interrompe la lettura continua.")
            testo.AppendLine("F6 legge la riga corrente.")
            testo.AppendLine("F7, se il documento non è di sola lettura, mostra i suggerimenti ortografici per la parola alla posizione del cursore.")
            testo.AppendLine("Il tasto Alt, oppure il tasto Menu della tastiera, aprono il menu delle opzioni di formattazione e di salvataggio del documento.")
            testo.AppendLine("Se il documento è stato aperto in sola lettura, non è possibile modificarlo o formattarlo, ma si può comunque leggerlo e spostare il cursore normalmente.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaWord: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaPlayer() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            testo.AppendLine("Guida al lettore multimediale di WinGabriel.")
            testo.AppendLine("Spazio avvia o mette in pausa la riproduzione.")
            testo.AppendLine("Freccia destra e freccia sinistra spostano avanti o indietro nel brano corrente, di un breve intervallo che si può cambiare nelle impostazioni.")
            testo.AppendLine("Pagina su e pagina giù spostano indietro o avanti nel brano corrente, di un intervallo più lungo, anch'esso configurabile nelle impostazioni.")
            testo.AppendLine("Freccia su e freccia giù alzano o abbassano il volume.")
            testo.AppendLine("Home porta all'inizio del brano corrente. Fine porta quasi alla fine del brano corrente.")
            testo.AppendLine("Ctrl più B, se è aperta una playlist con più elementi, passa al brano precedente. Ctrl più F passa al brano successivo.")
            testo.AppendLine("F10 passa dalla riproduzione integrata alla riproduzione con il player di YouTube nel browser, quando disponibile.")
            testo.AppendLine("Escape chiude il player.")
            testo.AppendLine("Il tasto Alt apre il menu delle opzioni del player: ripeti traccia, ripeti playlist, riproduzione casuale e, se la playlist ha più elementi, traccia precedente e traccia successiva.")
            testo.AppendLine("Su una web radio in streaming non è possibile spostarsi avanti o indietro nel tempo.")
            testo.AppendLine("F1 apre in qualsiasi momento questa guida.")
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaPlayer: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

    Private Shared Function CostruisciGuidaTipoCampo(tipoTag As String) As String
        Try
            Dim tipo As String = If(tipoTag, "").Trim()

            Select Case tipo
                Case "Alfanumerico"
                    Return "Campo di testo libero. Puoi scrivere lettere, numeri e simboli. Se il campo è obbligatorio, deve contenere almeno una lettera per poter proseguire."
                Case "Numerico"
                    Return "Campo numerico. Deve contenere solo cifre, senza lettere né altri simboli, a parte il separatore decimale se previsto dal formato numerico del sistema."
                Case "Password"
                    Return "Campo password. Quello che scrivi viene nascosto durante la lettura e sullo schermo. Aprendo il menu opzioni con Alt puoi scegliere di mostrare o nascondere la password, oppure di attivare o disattivare la lettura mentre scrivi."
                Case "Data"
                    Return "Campo data. Scrivi una data valida nel formato riconosciuto dal sistema, ad esempio giorno, mese e anno separati da una barra. Quando esci dal campo, la data viene sistemata automaticamente nel formato breve standard."
                Case "Orario"
                    Return "Campo orario. Scrivi un orario valido con ore e minuti, ad esempio 14 e 30. Quando esci dal campo, l'orario viene sistemato automaticamente con due cifre per le ore e due per i minuti."
                Case "CartaDiCredito"
                    Return "Campo numero di carta di credito. Deve contenere esattamente sedici cifre, senza spazi né trattini."
                Case "CVC"
                    Return "Campo codice di sicurezza della carta, il CVC. Deve contenere esattamente tre cifre."
                Case "DataScadenza"
                    Return "Campo data di scadenza della carta. Scrivi mese e anno a due cifre separati da una barra, ad esempio zero uno barra ventisette per gennaio duemilaventisette."
                Case "Multiriga"
                    Return "Campo di testo libero su più righe. Puoi scrivere più paragrafi andando a capo con Invio. Premendo Invio tre volte di seguito, il campo viene confermato e si passa oltre, come negli altri campi."
                Case "Multiplo"
                    Return "Campo che fa parte di un gruppo di valori dello stesso tipo, ad esempio più numeri di telefono o più indirizzi email. Ogni valore viene richiesto separatamente, uno alla volta."
                Case ""
                    Return "Non è stato possibile capire il tipo di questo campo. È comunque un campo di modifica: usa le frecce per muovere il cursore, Invio per confermare e passare al campo successivo, Escape per chiudere il modulo."
                Case Else
                    Return "Campo di tipo " & tipo & ". Usa le frecce per muovere il cursore, Invio per confermare e passare al campo successivo, Escape per chiudere il modulo."
            End Select
        Catch ex As Exception
            _log.ScriviLog("WGGuida.CostruisciGuidaTipoCampo: " & ex.Message)
            Return "Guida non disponibile a causa di un errore interno."
        End Try
    End Function

End Class