' ============================================================================================
' WGScrivania - la scrivania di WinGabriel: collegamenti, file copiati e cartelle
' ============================================================================================
'
' A cosa serve
'   Una scrivania come quella di Windows, prima cosa che si vede all'avvio e applicazione a sé
'   (Control Alt D), con gli elementi su una griglia (WGGriglia). In modalità tablet (WGModalitaTablet)
'   la scrivania non c'è: si chiude quando si entra in modalità tablet e torna quando si esce.
'
' Cosa ci può stare
'   - collegamenti a file, cartelle, funzioni di WinGabriel e voci dei menu;
'   - file e cartelle copiati sulla scrivania (stanno davvero nella cartella dati\scrivania\File);
'   - cartelle della scrivania, che raccolgono collegamenti (cartella virtuale) e anche file copiati
'     (cartella mista), una dentro l'altra quanto si vuole.
'   Ogni elemento ha un identificatore suo nel database dati\gui\gui.sqlite: il nome mostrato
'   si cambia come si vuole senza toccare il riferimento interno (percorso, funzione o voci del menu).
'   I collegamenti alle voci dei menu ricordano i nomi delle voci da scegliere, così aggiungere voci
'   ai menu non richiede di cambiare niente.
'
' Tasti sulla griglia
'   Invio apre, Esc o Backspace escono dalla cartella (Esc sulla scrivania esce da WinGabriel), F2
'   rinomina, Canc elimina, Control C e Control X mettono file e cartelle negli appunti di Windows,
'   gli stessi della gestione file, Control V porta sulla scrivania i file e le cartelle negli appunti.
'
' Come si usa
'   WGScrivania.ApriFile = Sub(p) ...
'   WGScrivania.ApriCartella = Sub(p) ...
'   WGScrivania.RegistraFunzione("Agenda", AddressOf agenda)
'   WGScrivania.Vai()                          ' Control Alt D
'   WGScrivania.AggiungiPercorso(percorso)     ' collegamento dalla gestione file
'   WGScrivania.CopiaSullaScrivania(percorsi)  ' copia dalla gestione file
'   WGScrivania.AggiungiVoceDiMenu(voci)       ' da qualsiasi menu (opzione "Aggiungi alla scrivania")
' ============================================================================================
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGDb
Imports FileVb = Microsoft.VisualBasic.FileIO.FileSystem

Public NotInheritable Class WGScrivania

    Public Const NomeApplicazione As String = "Scrivania"
    Public Const TipoFile As String = "file"
    Public Const TipoCartella As String = "cartella"
    Public Const TipoFunzione As String = "funzione"
    Public Const TipoMenu As String = "menu"
    ' Cartella della scrivania: raccoglie altri elementi, non esiste sul disco.
    Public Const TipoGruppo As String = "gruppo"
    Public Const SeparatoreMenu As String = " > "

    Private Const Tabella As String = "ScrivaniaElementi"
    Private Const Radice As String = ""

    Private Class Elemento
        Public Id As String = ""
        Public Genitore As String = Radice
        Public Tipo As String = ""
        Public Nome As String = ""
        Public Destinazione As String = ""
        Public Ordine As Integer
        ' File o cartella copiati sulla scrivania: appartengono a lei, eliminandoli vanno nel cestino.
        Public Proprio As Boolean

        Public ReadOnly Property SuDisco As Boolean
            Get
                Return Tipo = TipoFile OrElse Tipo = TipoCartella
            End Get
        End Property
    End Class

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _funzioni As New Dictionary(Of String, Action)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _ordineFunzioni As New List(Of String)()
    Private Shared _db As WGDatabase
    Private Shared _griglia As WGGriglia
    Private Shared _sorveglianzaAvviata As Boolean
    ' Cartella della scrivania mostrata ("" = la scrivania).
    Private Shared _cartellaCorrente As String = Radice

    Public Shared Property ApriFile As Action(Of String)
    ' Barra di stato: Invio sulla connessione apre il Wi-Fi, e la voce Bluetooth (le imposta il programma principale).
    Public Shared Property ApriConnessione As Action
    Public Shared Property ApriBluetooth As Action
    Public Shared Property TestoBluetooth As Func(Of String)
    ' Il menu del Wi-Fi e del Bluetooth disegnato dentro la griglia da cui si è premuto Invio (scrivania e home).
    Public Shared Property MenuWifiBarra As Action(Of WGGriglia)
    Public Shared Property MenuBluetoothBarra As Action(Of WGGriglia)
    Public Shared Property ApriCartella As Action(Of String)

    Private Sub New()
    End Sub

    ''' <summary>Funzione che si può mettere sulla scrivania; registrarla di nuovo con lo stesso nome la aggiorna.</summary>
    Public Shared Sub RegistraFunzione(nome As String, azione As Action)
        If String.IsNullOrWhiteSpace(nome) OrElse azione Is Nothing Then Return
        If Not _funzioni.ContainsKey(nome) Then _ordineFunzioni.Add(nome)
        _funzioni(nome) = azione
    End Sub

#Region "Database"

    Private Shared Function Db() As WGDatabase
        If _db IsNot Nothing Then Return _db
        Try
            ' Nel database dell'interfaccia (WGGui); i dati del vecchio scrivania.db si importano la prima volta.
            _db = WGGui.CreaTabella(Tabella, "Genitore TEXT, Tipo TEXT, Nome TEXT, Destinazione TEXT, Ordine INTEGER, Proprio INTEGER", "scrivania", "scrivania.db", "elementi")
            If _db IsNot Nothing Then ImportaVecchiCollegamenti()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Db: " & ex.Message)
        End Try
        Return _db
    End Function

    ''' <summary>Cartella in cui stanno i file e le cartelle copiati sulla scrivania.</summary>
    Public Shared Function CartellaFile() As String
        Try
            Dim cartella As String = Path.Combine(WGDatabase.CartellaDati("scrivania"), "File")
            Directory.CreateDirectory(cartella)
            Return cartella
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CartellaFile: " & ex.Message)
            Return ""
        End Try
    End Function

    ' La prima versione della scrivania teneva i collegamenti in un file di testo: si portano nel database una volta sola.
    Private Shared Sub ImportaVecchiCollegamenti()
        Try
            Dim vecchio As String = Path.Combine(WGDatabase.CartellaDati("scrivania"), "collegamenti.txt")
            If Not File.Exists(vecchio) Then Return
            Dim ordine As Integer = 0
            For Each riga As String In File.ReadAllLines(vecchio, Encoding.UTF8)
                Dim parti As String() = riga.Split(ControlChars.Tab)
                If parti.Length < 3 Then Continue For
                ordine += 1
                _db.InserisciRecord(Tabella, Valori(Radice, parti(0), parti(1), parti(2), ordine, False))
            Next
            File.Delete(vecchio)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ImportaVecchiCollegamenti: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Valori(genitore As String, tipo As String, nome As String, destinazione As String, ordine As Integer, proprio As Boolean) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object) From {
            {"Genitore", genitore}, {"Tipo", tipo}, {"Nome", Pulisci(nome)}, {"Destinazione", destinazione},
            {"Ordine", ordine}, {"Proprio", If(proprio, 1, 0)}}
    End Function

    Private Shared Function Pulisci(testo As String) As String
        Return If(testo, "").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

    Private Shared Function DaRiga(riga As DataRow) As Elemento
        Dim e As New Elemento()
        e.Id = Convert.ToString(riga(WGDatabase.NomeColonnaChiavePredefinita))
        e.Genitore = If(IsDBNull(riga("Genitore")), Radice, Convert.ToString(riga("Genitore")))
        e.Tipo = Convert.ToString(riga("Tipo"))
        e.Nome = Convert.ToString(riga("Nome"))
        e.Destinazione = If(IsDBNull(riga("Destinazione")), "", Convert.ToString(riga("Destinazione")))
        e.Ordine = If(IsDBNull(riga("Ordine")), 0, Convert.ToInt32(riga("Ordine")))
        e.Proprio = Not IsDBNull(riga("Proprio")) AndAlso Convert.ToInt32(riga("Proprio")) <> 0
        Return e
    End Function

    ' Elementi di una cartella della scrivania, in ordine. I file propri spariti (per esempio tagliati e
    ' incollati altrove dalla gestione file) vengono tolti.
    Private Shared Function Contenuto(genitore As String) As List(Of Elemento)
        Dim risultato As New List(Of Elemento)()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return risultato
            Dim tabellaDati As DataTable = d.EseguiQuery("SELECT * FROM " & Tabella & " WHERE IFNULL(Genitore,'') = @g ORDER BY Ordine", New Dictionary(Of String, Object) From {{"@g", genitore}})
            For Each riga As DataRow In tabellaDati.Rows
                Dim e As Elemento = DaRiga(riga)
                If e.Proprio AndAlso Not (File.Exists(e.Destinazione) OrElse Directory.Exists(e.Destinazione)) Then
                    d.EliminaRecord(Tabella, e.Id)
                    Continue For
                End If
                risultato.Add(e)
            Next
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Contenuto: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function Trova(id As String) As Elemento
        Try
            If String.IsNullOrEmpty(id) Then Return Nothing
            Dim riga As DataRow = Db().OttieniRecord(Tabella, id)
            Return If(riga Is Nothing, Nothing, DaRiga(riga))
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Trova: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function ProssimoOrdine(genitore As String) As Integer
        Try
            Dim valore As Object = Db().EseguiScalare("SELECT MAX(Ordine) FROM " & Tabella & " WHERE IFNULL(Genitore,'') = @g", New Dictionary(Of String, Object) From {{"@g", genitore}})
            If valore Is Nothing OrElse IsDBNull(valore) Then Return 1
            Return Convert.ToInt32(valore) + 1
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ProssimoOrdine: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Shared Function Inserisci(genitore As String, tipo As String, nome As String, destinazione As String, proprio As Boolean) As String
        Return Db().InserisciRecord(Tabella, Valori(genitore, tipo, nome, destinazione, ProssimoOrdine(genitore), proprio))
    End Function

    Private Shared Sub Aggiorna(e As Elemento)
        Db().AggiornaRecord(Tabella, e.Id, Valori(e.Genitore, e.Tipo, e.Nome, e.Destinazione, e.Ordine, e.Proprio))
    End Sub

    ' Tutti i file e le cartelle dentro un elemento (anche nelle cartelle della scrivania annidate).
    Private Shared Function PercorsiDi(e As Elemento) As List(Of String)
        Dim risultato As New List(Of String)()
        If e Is Nothing Then Return risultato
        If e.SuDisco Then
            If File.Exists(e.Destinazione) OrElse Directory.Exists(e.Destinazione) Then risultato.Add(e.Destinazione)
        ElseIf e.Tipo = TipoGruppo Then
            For Each figlio As Elemento In Contenuto(e.Id)
                risultato.AddRange(PercorsiDi(figlio))
            Next
        End If
        Return risultato
    End Function

    ' Vero se dentro (anche in profondità) ci sono file propri: la cartella è mista.
    Private Shared Function ContieneFilePropri(e As Elemento) As Boolean
        If e Is Nothing Then Return False
        If e.Proprio Then Return True
        If e.Tipo <> TipoGruppo Then Return False
        Return Contenuto(e.Id).Any(Function(f) ContieneFilePropri(f))
    End Function

#End Region

#Region "Visualizzazione"

    ''' <summary>
    ''' Control Alt D: torna alla scrivania se è aperta, altrimenti la apre come applicazione.
    ''' Falso in modalità tablet, dove la scrivania non c'è.
    ''' </summary>
    Public Shared Function Vai() As Boolean
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return False
            If contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New MethodInvoker(Sub() Vai()))
                Return True
            End If
            AvviaSorveglianzaTablet()
            ' In modalità tablet al posto della scrivania c'è la home, con le sole applicazioni.
            If WGModalitaTablet.Attiva() Then Return WGHome.Vai()
            If Not Attiva Then Return False
            If contenitore.VaiAllApplicazioneConNome(NomeApplicazione) Then Return True
            _griglia = Nothing
            _cartellaCorrente = Radice
            contenitore.ApriComeNuovaApplicazione(NomeApplicazione)
            Mostra(0)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Vai: " & ex.Message)
            Return False
        End Try
    End Function

    Private Const IdConfigurazioneAttiva As String = "scrivania_attiva"

    ''' <summary>
    ''' Scrivania attiva (predefinito). Disattivandola si torna al WinGabriel classico: all'avvio c'è
    ''' il menu e Esc dal menu chiede di uscire. I collegamenti restano nel database.
    ''' </summary>
    Public Shared Property Attiva As Boolean
        Get
            Try
                If Not WGModalita.Smart Then Return False
                Dim config As New WGConfig()
                If Not config.EsisteId(IdConfigurazioneAttiva) Then Return True
                Return Not String.Equals(config.LeggiValore(IdConfigurazioneAttiva), "0", StringComparison.Ordinal)
            Catch ex As Exception
                log.ScriviLog("WGScrivania.Attiva: " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                Dim config As New WGConfig()
                config.AggiungiValore(IdConfigurazioneAttiva, If(value, "1", "0"))
                If Not value Then
                    ' La scrivania aperta si chiude subito.
                    Dim griglia As WGGriglia = _griglia
                    _griglia = Nothing
                    If griglia IsNot Nothing AndAlso Not griglia.IsDisposed Then griglia.Chiudi()
                End If
            Catch ex As Exception
                log.ScriviLog("WGScrivania.Attiva Set: " & ex.Message)
            End Try
        End Set
    End Property

    Private Shared Sub AvviaSorveglianzaTablet()
        If _sorveglianzaAvviata Then Return
        _sorveglianzaAvviata = True
        AddHandler WGModalitaTablet.Cambiata, AddressOf ModalitaTabletCambiata
        WGModalitaTablet.AvviaSorveglianza()
    End Sub

    ' Entrando in modalità tablet la scrivania si chiude e, se era davanti, compare la home; uscendo la
    ' home si chiude e torna la scrivania, se si era sulla home o nel menu applicazioni.
    Private Shared Sub ModalitaTabletCambiata(tablet As Boolean)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            If tablet Then
                Dim griglia As WGGriglia = _griglia
                _griglia = Nothing
                Dim eraDavanti As Boolean = contenitore.ESulMenuApplicazioni()
                If griglia IsNot Nothing AndAlso Not griglia.IsDisposed Then
                    eraDavanti = eraDavanti OrElse griglia.Visible
                    griglia.Chiudi()
                End If
                sintesivocale.GetInstance().LeggiTesto("Modalità tablet")
                If eraDavanti AndAlso Not WGHome.Vai() Then contenitore.VaiAlMenuIniziale()
            Else
                Dim homeDavanti As Boolean = WGHome.Davanti
                WGHome.Chiudi()
                sintesivocale.GetInstance().LeggiTesto("Modalità computer")
                If homeDavanti OrElse contenitore.ESulMenuApplicazioni() Then Vai()
            End If
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ModalitaTabletCambiata: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Descrizione(e As Elemento) As String
        Select Case e.Tipo
            Case TipoFile : Return e.Nome & If(e.Proprio, ", file sulla scrivania", ", collegamento a file")
            Case TipoCartella : Return e.Nome & If(e.Proprio, ", cartella sulla scrivania", ", collegamento a cartella")
            Case TipoGruppo
                Dim quanti As Integer = Contenuto(e.Id).Count
                Return e.Nome & ", cartella della scrivania, " & If(quanti = 0, "vuota", If(quanti = 1, "un elemento", quanti & " elementi"))
            Case Else : Return e.Nome
        End Select
    End Function

    ' Titolo corto: solo il nome e quanti elementi ci sono; i tasti li spiega la guida (F1).
    Private Shared Function Titolo(quanti As Integer) As String
        Dim testoQuanti As String = If(quanti = 0, "nessun elemento", If(quanti = 1, "un elemento", quanti & " elementi"))
        If _cartellaCorrente = Radice Then Return "Scrivania, " & testoQuanti
        Dim cartella As Elemento = Trova(_cartellaCorrente)
        Return If(cartella Is Nothing, "Cartella", cartella.Nome) & ", " & testoQuanti
    End Function

    Private Const TestoGuida As String =
        "Guida della scrivania." & vbCrLf & vbCrLf &
        "La scrivania è la prima cosa che si vede all'avvio di WinGabriel ed è un'applicazione a sé: da qualsiasi punto ci si torna con Control Alt D. " &
        "In modalità tablet, con la tastiera staccata o ripiegata, la scrivania non c'è: si usa il menu applicazioni." & vbCrLf & vbCrLf &
        "Cosa ci può stare:" & vbCrLf &
        "- collegamenti a file, a cartelle, a funzioni di WinGabriel e alle voci dei menu;" & vbCrLf &
        "- file e cartelle copiati sulla scrivania, che stanno davvero nella cartella dei dati di WinGabriel, sotto scrivania, File;" & vbCrLf &
        "- cartelle della scrivania, che raccolgono collegamenti e file copiati e possono stare una dentro l'altra." & vbCrLf &
        "In cima alla scrivania c'è il cestino di Windows. I file e le cartelle aperti di recente sono nel menu opzioni, alla voce Recenti; il centro notifiche, con le notifiche da leggere, è nella barra di stato, che si raggiunge con Tab. " &
        "Nel cestino Invio su un elemento chiede se ripristinarlo nella cartella da cui era stato eliminato, eliminarlo definitivamente o leggerne le informazioni; " &
        "dal menu opzioni si ripristinano tutti gli elementi o si svuota il cestino, sempre con una conferma. I file della scrivania ripristinati tornano sulla scrivania." & vbCrLf &
        "Con pochi elementi la griglia ha due colonne, poi tre e quattro, così le frecce su e giù servono sempre." & vbCrLf & vbCrLf &
        "Tasti:" & vbCrLf &
        "- frecce: spostarsi sulla griglia; Home e Fine: inizio e fine della riga; Control Home e Control Fine: primo e ultimo elemento; una lettera: primo elemento che comincia così;" & vbCrLf &
        "- Invio o Spazio: aprire l'elemento o entrare nella cartella;" & vbCrLf &
        "- Esc o Backspace: uscire dalla cartella; Esc sulla scrivania: uscire da WinGabriel;" & vbCrLf &
        "- Alt: menu opzioni, per aggiungere collegamenti, copiare file, creare cartelle, spostare, rinominare ed eliminare;" & vbCrLf &
        "- F2: rinominare; Canc: eliminare;" & vbCrLf &
        "- Control C: mettere negli appunti un file o una cartella, o tutti i file di una cartella della scrivania, per incollarli nella gestione file;" & vbCrLf &
        "- Control X: tagliare un file copiato sulla scrivania; i collegamenti non si tagliano, così gli originali restano dove sono;" & vbCrLf &
        "- Control V: copiare sulla scrivania i file e le cartelle negli appunti, per esempio copiati dalla gestione file; se erano tagliati vengono spostati;" & vbCrLf &
        "- Insert: rileggere il titolo e l'elemento; F1: questa guida; tasto Windows: menu applicazioni;" & vbCrLf &
        "- Tab: barra di stato, come l'area di notifica di Windows, con l'ora e la data, il volume di sistema, dove si ascolta l'audio, la batteria e la connessione. " &
        "Frecce destra e sinistra per passare da una all'altra; sul volume frecce su e giù per alzarlo e abbassarlo e Invio per il muto; " &
        "su Audio Invio passa dagli altoparlanti alle cuffie e viceversa, frecce su e giù per scegliere tra tutti i dispositivi; " &
        "sulla connessione e sul Bluetooth Invio apre un menu sopra la scrivania: la prima voce attiva o disattiva, la seconda apre l'elenco delle reti o dei dispositivi; " &
        "frecce su e giù per spostarsi, Invio per scegliere, Alt per le opzioni, dove c'è anche Aggiorna elenco, Esc per chiudere il menu; Tab o Esc per tornare alla griglia." & vbCrLf & vbCrLf &
        "Nomi: ogni elemento si rinomina come si vuole, il collegamento continua a portare allo stesso file, funzione o voce di menu. " &
        "Rinominando un file copiato sulla scrivania si rinomina anche il file sul disco." & vbCrLf & vbCrLf &
        "Aggiungere dalla gestione file: nel menu opzioni di un file o di una cartella ci sono Aggiungi alla scrivania, che crea un collegamento, e Copia sulla scrivania, che copia il file." & vbCrLf &
        "Aggiungere dai menu: nel menu opzioni di ogni menu raggiunto dal menu applicazioni c'è Aggiungi alla scrivania per la voce selezionata. " &
        "Il collegamento ripete le scelte nei menu; se una voce viene rinominata o tolta, il collegamento lo dice e va aggiunto di nuovo." & vbCrLf & vbCrLf &
        "Eliminare: i collegamenti e le cartelle vuote si tolgono subito; file copiati e cartelle con qualcosa dentro chiedono conferma, e i file vanno nel cestino."

    Private Shared Sub MostraGuida()
        Try
            If WGContenitore.ApriCapitolo("scrivania") Then Return
            Dim campo As New WGCampo()
            campo.init("Guida della scrivania", TestoGuida, True, True)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.MostraGuida: " & ex.Message)
        End Try
    End Sub

    ' Voci mostrate con il loro identificatore: "cestino", "aggiungi" o l'id dell'elemento.
    ' Sulla scrivania in cima c'è il cestino; i recenti sono nel menu opzioni e il centro notifiche nella
    ' barra di stato, come nell'area di notifica di Windows.
    Private Shared Function VociDellaScrivania(elementi As List(Of Elemento)) As List(Of Tuple(Of String, String))
        Dim risultato As New List(Of Tuple(Of String, String))()
        If _cartellaCorrente = Radice Then risultato.Add(Tuple.Create(VoceCestino(), "cestino"))
        For Each e As Elemento In elementi
            risultato.Add(Tuple.Create(Descrizione(e), e.Id))
        Next
        If elementi.Count = 0 Then risultato.Add(Tuple.Create(If(_cartellaCorrente = Radice, "Aggiungi un collegamento", "Cartella vuota: aggiungi un collegamento"), "aggiungi"))
        Return risultato
    End Function

    Private Shared Sub Scegli(id As String)
        If id = "aggiungi" Then
            ScegliCosaAggiungere()
            Return
        End If
        If id = "cestino" Then
            ApriCestino()
            Return
        End If
        Dim e As Elemento = Trova(id)
        If e Is Nothing Then Return
        If e.Tipo = TipoGruppo Then
            _cartellaCorrente = e.Id
            Mostra(0)
            Return
        End If
        Apri(e)
    End Sub

    ' In cima alla scrivania c'è solo il cestino.
    Private Shared Function VociFisse() As Integer
        Return If(_cartellaCorrente = Radice, 1, 0)
    End Function

    ' Gli ultimi file e cartelle aperti con WinGabriel, dal più recente.
    Private Shared Sub MostraRecenti()
        Try
            Dim elenco As List(Of String) = WGRecenti.Elenco()
            If elenco.Count = 0 Then
                Messaggio("Non ci sono file aperti di recente.")
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Recenti")
            For Each percorso As String In elenco
                Dim nome As String = NomeDaPercorso(percorso)
                lista.aggiungiElemento(nome & If(Directory.Exists(percorso), ", cartella", "") & ", in " & Path.GetDirectoryName(percorso.TrimEnd("\"c)), percorso)
            Next
            AddHandler lista.ElementoSelezionato, Sub(percorso As String)
                                                      If String.IsNullOrEmpty(percorso) Then Return
                                                      lista.abbandona()
                                                      ApriPercorso(percorso)
                                                  End Sub
            lista.aggiungiOpzione("Aggiungi alla scrivania", Sub(s, ev)
                                                                 Dim percorso As String = lista.ottieniElemento()
                                                                 If Not String.IsNullOrEmpty(percorso) Then AggiungiPercorso(percorso)
                                                             End Sub)
            lista.aggiungiOpzione("Svuota l'elenco dei recenti", Sub(s, ev)
                                                                     WGRecenti.Svuota()
                                                                     lista.abbandona()
                                                                     Messaggio("Elenco dei recenti svuotato.")
                                                                 End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, ev As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.MostraRecenti: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre un file o una cartella come applicazione con il suo nome (recenti, ricerca globale).</summary>
    Public Shared Sub ApriPercorso(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Dim nome As String = NomeDaPercorso(percorso)
            If contenitore IsNot Nothing AndAlso contenitore.VaiAllApplicazioneConNome(nome) Then Return
            If Directory.Exists(percorso) Then
                If ApriCartella Is Nothing Then Return
                If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(nome, "percorso:" & percorso)
                WGRecenti.Registra(percorso)
                ApriCartella.Invoke(percorso)
            ElseIf File.Exists(percorso) Then
                If ApriFile Is Nothing Then Return
                If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(nome, "percorso:" & percorso)
                WGRecenti.Registra(percorso)
                ApriFile.Invoke(percorso)
            Else
                Messaggio("Il percorso " & percorso & " non esiste più.")
            End If
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ApriPercorso: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre un elemento della scrivania dal suo identificatore; falso se non c'è più.</summary>
    Public Shared Function ApriPerId(id As String) As Boolean
        Dim e As Elemento = Trova(id)
        If e Is Nothing OrElse e.Tipo = TipoGruppo Then Return False
        Apri(e)
        Return True
    End Function

    ''' <summary>
    ''' Come ApriPerId, ma finisce quando l'apertura è finita: la ripresa della sessione la aspetta, così
    ''' i menu attraversati dal collegamento si aprono tutti mentre si è ancora in sottofondo.
    ''' </summary>
    Public Shared Async Function ApriPerIdAsync(id As String) As Task(Of Boolean)
        Dim e As Elemento = Trova(id)
        If e Is Nothing OrElse e.Tipo = TipoGruppo Then Return False
        Await ApriAsync(e)
        Return True
    End Function

    ''' <summary>Gli elementi della scrivania, anche nelle cartelle, il cui nome contiene il testo (già normalizzato).</summary>
    Public Shared Function CercaElementi(cercato As String) As List(Of Tuple(Of String, Action))
        Dim risultato As New List(Of Tuple(Of String, Action))()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing OrElse String.IsNullOrEmpty(cercato) Then Return risultato
            For Each riga As DataRow In d.EseguiQuery("SELECT * FROM " & Tabella).Rows
                Dim e As Elemento = DaRiga(riga)
                If Not WGRicerca.Normalizza(e.Nome).Contains(cercato) Then Continue For
                Dim scelto As Elemento = e
                If e.Tipo = TipoGruppo Then
                    risultato.Add(Tuple.Create(Of String, Action)(e.Nome & ", cartella", Sub()
                                                                                             If Not Vai() Then Return
                                                                                             _cartellaCorrente = scelto.Id
                                                                                             Mostra(0)
                                                                                         End Sub))
                Else
                    risultato.Add(Tuple.Create(Of String, Action)(e.Nome, Sub() Apri(scelto)))
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CercaElementi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function VoceCestino() As String
        Dim quanti As Integer = WGCestino.Conta()
        Return "Cestino, " & If(quanti = 0, "vuoto", If(quanti = 1, "un elemento", quanti & " elementi"))
    End Function

    Private Shared _cestinoCollegato As Boolean

    ' Il cestino si apre come applicazione a sé; chiudendolo si torna alla scrivania.
    Public Shared Sub ApriCestino()
        Try
            If Not _cestinoCollegato Then
                _cestinoCollegato = True
                AddHandler WGCestino.Cambiato, AddressOf CestinoCambiato
                AddHandler WGCestino.Ripristinato, AddressOf CestinoRipristinato
            End If
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then
                If contenitore.VaiAllApplicazioneConNome(WGCestino.NomeApplicazione) Then Return
                ' Cestino vuoto: lo si dice senza aprire un'applicazione che resterebbe senza finestre.
                If WGCestino.Conta() = 0 Then
                    Messaggio("Il cestino è vuoto.")
                    Return
                End If
                contenitore.ApriComeNuovaApplicazione(WGCestino.NomeApplicazione)
            End If
            WGCestino.Mostra()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ApriCestino: " & ex.Message)
        End Try
    End Sub

    ' Aggiorna il conteggio del cestino sulla griglia senza ridisegnarla (non deve tornare davanti).
    Private Shared Sub CestinoCambiato()
        Try
            If _griglia Is Nothing OrElse _griglia.IsDisposed OrElse _cartellaCorrente <> Radice Then Return
            _griglia.AggiornaVoce(VociFisse() - 1, VoceCestino())
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CestinoCambiato: " & ex.Message)
        End Try
    End Sub

    ' Un file della scrivania ripristinato dal cestino torna sulla scrivania.
    Private Shared Sub CestinoRipristinato(percorso As String)
        Try
            Dim base As String = CartellaFile()
            If base = "" OrElse Not percorso.StartsWith(base, StringComparison.OrdinalIgnoreCase) Then Return
            Inserisci(Radice, If(Directory.Exists(percorso), TipoCartella, TipoFile), NomeDaPercorso(percorso), percorso, True)
            If _griglia IsNot Nothing AndAlso Not _griglia.IsDisposed AndAlso Not _griglia.Visible AndAlso _cartellaCorrente = Radice Then _aggiornaAlRitorno = True
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CestinoRipristinato: " & ex.Message)
        End Try
    End Sub

    ' La griglia va ridisegnata quando torna davanti (qualcosa è cambiato mentre era dietro).
    Private Shared _aggiornaAlRitorno As Boolean
    Private Shared _idAlRitorno As String = ""

    ' La scrivania è dietro a un'altra applicazione: ridisegnarla adesso la porterebbe davanti.
    Private Shared Function Dietro() As Boolean
        Try
            If _griglia Is Nothing OrElse _griglia.IsDisposed Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Return contenitore IsNot Nothing AndAlso WGContenitore.ApplicazioniSeparate AndAlso Not contenitore.EApplicazioneCorrente(_griglia)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Dietro: " & ex.Message)
            Return False
        End Try
    End Function

    ' Esc e Backspace: dalla cartella si torna a quella che la contiene; sulla scrivania Esc esce.
    Private Shared Sub Indietro(conEsc As Boolean)
        If _cartellaCorrente = Radice Then
            If conEsc Then RichiediUscita()
            Return
        End If
        Dim cartella As Elemento = Trova(_cartellaCorrente)
        _cartellaCorrente = If(cartella Is Nothing, Radice, cartella.Genitore)
        Ridisegna(If(cartella Is Nothing, "", cartella.Id))
    End Sub

    Private Shared Sub RichiediUscita()
        Dim c As WGContenitore = WGContenitore.Istanza
        If c IsNot Nothing Then c.RichiediUscita()
    End Sub

    ' Opzioni del menu Alt per l'elemento selezionato (Nothing se non è un elemento).
    Private Shared Function Opzioni(e As Elemento) As List(Of Tuple(Of String, Action))
        Dim risultato As New List(Of Tuple(Of String, Action))()
        risultato.Add(Tuple.Create(Of String, Action)("Aggiungi un collegamento", Sub() ScegliCosaAggiungere()))
        risultato.Add(Tuple.Create(Of String, Action)("Copia file sulla scrivania", Sub() ScegliDaCopiare(False)))
        risultato.Add(Tuple.Create(Of String, Action)("Copia una cartella sulla scrivania", Sub() ScegliDaCopiare(True)))
        risultato.Add(Tuple.Create(Of String, Action)("Nuova cartella della scrivania", Sub() NuovaCartella()))
        risultato.Add(Tuple.Create(Of String, Action)("Ordinamento", Sub() ScegliOrdinamento(e)))
        If e IsNot Nothing Then
            risultato.Add(Tuple.Create(Of String, Action)("Rinomina, F2", Sub() Rinomina(e)))
            risultato.Add(Tuple.Create(Of String, Action)("Sposta in una cartella della scrivania", Sub() ScegliCartellaDestinazione(e)))
            If e.SuDisco OrElse e.Tipo = TipoGruppo Then
                risultato.Add(Tuple.Create(Of String, Action)("Copia negli appunti, Control C", Sub() MettiNegliAppunti(e, False)))
            End If
            If e.Proprio Then
                risultato.Add(Tuple.Create(Of String, Action)("Taglia, Control X", Sub() MettiNegliAppunti(e, True)))
            End If
            If e.SuDisco Then
                risultato.Add(Tuple.Create(Of String, Action)("Mostra nella gestione file", Sub() MostraNellaGestioneFile(e)))
            End If
            risultato.Add(Tuple.Create(Of String, Action)("Elimina, Canc", Sub() ChiediElimina(e)))
        End If
        risultato.Add(Tuple.Create(Of String, Action)("Incolla sulla scrivania, Control V", Sub() Incolla()))
        risultato.Add(Tuple.Create(Of String, Action)("Recenti, file e cartelle aperti di recente", Sub() MostraRecenti()))
        risultato.Add(Tuple.Create(Of String, Action)("Menu applicazioni, tasto Windows", Sub()
                                                                                             Dim c As WGContenitore = WGContenitore.Istanza
                                                                                             If c IsNot Nothing Then c.VaiAlMenuIniziale()
                                                                                         End Sub))
        risultato.Add(Tuple.Create(Of String, Action)("Guida della scrivania, F1", Sub() MostraGuida()))
        risultato.Add(Tuple.Create(Of String, Action)("Guida delle applicazioni", Sub() WGContenitore.MostraGuidaApplicazioni()))
        If WGContenitore.ManualeDisponibile Then risultato.Add(Tuple.Create(Of String, Action)("Manuale, Control F1", Sub() WGContenitore.ApriManuale()))
        risultato.Add(Tuple.Create(Of String, Action)("Esci da WinGabriel", Sub() RichiediUscita()))
        Return risultato
    End Function

    ' Ridisegna la scrivania: la nuova griglia entra nella stessa applicazione prima di chiudere la vecchia,
    ' così l'applicazione Scrivania non si chiude mentre si aggiungono o tolgono elementi.
    Private Shared Sub Mostra(indice As Integer)
        Try
            If WGModalitaTablet.Attiva() Then Return
            If _cartellaCorrente <> Radice AndAlso Trova(_cartellaCorrente) Is Nothing Then _cartellaCorrente = Radice
            Dim precedente As WGGriglia = _griglia
            If precedente IsNot Nothing AndAlso precedente.IsDisposed Then precedente = Nothing
            _griglia = Nothing
            MostraGriglia(Contenuto(_cartellaCorrente), indice, precedente)
            If precedente IsNot Nothing Then precedente.Chiudi()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraGriglia(elementi As List(Of Elemento), indice As Integer, precedente As Control)
        Dim voci As List(Of Tuple(Of String, String)) = VociDellaScrivania(elementi)
        ' Si contano tutti gli elementi della griglia: collegamenti, file, cartelle e cestino.
        Dim griglia As New WGGriglia(Titolo(elementi.Count + VociFisse()), voci.Select(Function(v) v.Item1), indice)
        _griglia = griglia
        Dim scelto As Func(Of Elemento) = Function()
                                              If griglia.IndiceCorrente < 0 OrElse griglia.IndiceCorrente >= voci.Count Then Return Nothing
                                              Dim id As String = voci(griglia.IndiceCorrente).Item2
                                              Return elementi.FirstOrDefault(Function(x) x.Id = id)
                                          End Function
        AddHandler griglia.ElementoScelto, Sub(i As Integer) Scegli(voci(i).Item2)
        AddHandler griglia.EscPremuto, Sub() Indietro(True)
        AddHandler griglia.VisibleChanged, Sub(s As Object, ev As EventArgs)
                                               If Not griglia.Visible OrElse griglia.IsDisposed OrElse Not _aggiornaAlRitorno OrElse griglia.MenuAperto Then Return
                                               _aggiornaAlRitorno = False
                                               Dim idRitorno As String = _idAlRitorno
                                               _idAlRitorno = ""
                                               griglia.BeginInvoke(New MethodInvoker(Sub()
                                                                                         If _griglia IsNot griglia Then Return
                                                                                         If idRitorno <> "" Then Ridisegna(idRitorno) Else Mostra(griglia.IndiceCorrente)
                                                                                     End Sub))
                                           End Sub
        AddHandler griglia.OpzioniRichieste, Sub()
                                                 Dim menu As New WGMenu()
                                                 For Each opzione As Tuple(Of String, Action) In Opzioni(scelto())
                                                     Dim azione As Action = opzione.Item2
                                                     menu.aggiungi(opzione.Item1, Sub(s, ev) azione())
                                                 Next
                                                 menu.avvia("Opzioni", True)
                                             End Sub
        griglia.ImpostaBarraStato(VociBarraStato())
        AddHandler griglia.Comando, Sub(s As Object, richiesta As ComandoGrigliaEventArgs)
                                        richiesta.Gestito = EseguiComando(richiesta.Codice, richiesta.Ctrl, scelto())
                                    End Sub
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If precedente IsNot Nothing AndAlso contenitore IsNot Nothing Then contenitore.MettiNellaStessaApplicazione(griglia, precedente)
        griglia.Avvia()
    End Sub

    Private Shared Function EseguiComando(codice As Keys, ctrl As Boolean, e As Elemento) As Boolean
        If ctrl Then
            Select Case codice
                Case Keys.C
                    If e Is Nothing OrElse Not (e.SuDisco OrElse e.Tipo = TipoGruppo) Then
                        sintesivocale.GetInstance().LeggiTesto("Negli appunti vanno solo file e cartelle")
                    Else
                        MettiNegliAppunti(e, False)
                    End If
                    Return True
                Case Keys.X
                    If e Is Nothing OrElse Not (e.Proprio OrElse e.Tipo = TipoGruppo) Then
                        sintesivocale.GetInstance().LeggiTesto(If(e IsNot Nothing AndAlso e.SuDisco, "È un collegamento: con Control C si copia il file", "Si tagliano solo file e cartelle copiati sulla scrivania"))
                    Else
                        MettiNegliAppunti(e, True)
                    End If
                    Return True
                Case Keys.V
                    Incolla()
                    Return True
            End Select
            Return False
        End If
        Select Case codice
            Case Keys.F1
                MostraGuida()
                Return True
            Case Keys.Back
                Indietro(False)
                Return True
            Case Keys.F2
                If e IsNot Nothing Then Rinomina(e)
                Return True
            Case Keys.Delete
                If e IsNot Nothing Then ChiediElimina(e)
                Return True
        End Select
        Return False
    End Function

    ' Ridisegna selezionando l'elemento indicato (o il primo).
    Private Shared Sub Ridisegna(Optional idDaSelezionare As String = "")
        If _griglia Is Nothing Then Return
        ' Cambiata mentre è dietro (per esempio con Aggiungi alla scrivania da un menu): si ridisegna al ritorno.
        ' Lo stesso con il menu della barra di stato aperto: una griglia nuova lo chiuderebbe.
        If Dietro() OrElse _griglia.MenuAperto Then
            _aggiornaAlRitorno = True
            _idAlRitorno = If(idDaSelezionare, "")
            Return
        End If
        Dim elementi As List(Of Elemento) = Contenuto(_cartellaCorrente)
        Dim posizione As Integer = elementi.FindIndex(Function(x) x.Id = idDaSelezionare)
        If posizione < 0 Then
            posizione = 0
        Else
            posizione += VociFisse()
        End If
        Mostra(posizione)
    End Sub

#End Region

#Region "Barra di stato"

    ' Col Tab dalla griglia si arriva alla barra di stato, come all'area di notifica di Windows:
    ' ora, volume di sistema, uscita audio, batteria, connessione, Bluetooth e centro notifiche.
    Private Shared Function VociBarraStato() As List(Of VoceStatoGriglia)
        AggiornaConnessioneSeServe()
        ' Lo stato di Wi-Fi e Bluetooth, per aprire subito il loro menu.
        WGPannelloControllo.PreparaStatoRadio()
        Dim voci As New List(Of VoceStatoGriglia) From {
            New VoceStatoGriglia() With {.Testo = AddressOf TestoOra},
            New VoceStatoGriglia() With {.Testo = AddressOf TestoVolume,
                                         .Aumenta = Sub() CambiaVolume(5),
                                         .Diminuisci = Sub() CambiaVolume(-5),
                                         .Invio = AddressOf CommutaMuto},
            New VoceStatoGriglia() With {.Testo = Function() "Audio su " & WGUscitaAudio.NomeAttuale(), .ApreFinestra = True,
                                         .Invio = Sub() WGUscitaAudio.PassaAlSuccessivo(),
                                         .Aumenta = Sub() WGUscitaAudio.Scegli(),
                                         .Diminuisci = Sub() WGUscitaAudio.Scegli()},
            New VoceStatoGriglia() With {.Testo = AddressOf TestoBatteria, .ApreFinestra = True, .Invio = Sub() WGStatoDispositivo.MostraBatteria()},
            New VoceStatoGriglia() With {.Testo = AddressOf TestoConnessione, .ApreFinestra = MenuWifiBarra IsNot Nothing OrElse ApriConnessione IsNot Nothing,
                                         .Invio = Sub()
                                                      ' Il menu del Wi-Fi, disegnato dalla griglia: accendi o spegni, e le reti.
                                                      If MenuWifiBarra IsNot Nothing Then
                                                          MenuWifiBarra.Invoke(WGGriglia.PerMenu)
                                                      ElseIf ApriConnessione IsNot Nothing Then
                                                          WGPannelloControllo.MostraMenuRadio(False)
                                                      End If
                                                      ' Tornando si vede subito lo stato nuovo.
                                                      AggiornaConnessioneSeServe(True)
                                                  End Sub}
        }
        ' Senza batteria (computer fisso) la voce della batteria non c'è.
        If Not WGStatoDispositivo.Batteria().Presente Then voci.RemoveAll(Function(v) v.Testo IsNot Nothing AndAlso v.Testo.Method.Name = NameOf(TestoBatteria))
        If TestoBluetooth IsNot Nothing Then
            voci.Add(New VoceStatoGriglia() With {.Testo = Function() TestoBluetooth.Invoke(), .ApreFinestra = MenuBluetoothBarra IsNot Nothing OrElse ApriBluetooth IsNot Nothing,
                                                 .Invio = Sub()
                                                              ' Il menu del Bluetooth, disegnato dalla griglia: accendi o spegni, e i dispositivi.
                                                              If MenuBluetoothBarra IsNot Nothing Then
                                                                  MenuBluetoothBarra.Invoke(WGGriglia.PerMenu)
                                                              ElseIf ApriBluetooth IsNot Nothing Then
                                                                  WGPannelloControllo.MostraMenuRadio(True)
                                                              End If
                                                          End Sub})
        End If
        ' In fondo, come in Windows: il centro notifiche con quante ce ne sono da leggere.
        If _funzioni.ContainsKey("Centro notifiche") Then
            voci.Add(New VoceStatoGriglia() With {.Testo = AddressOf TestoNotifiche, .ApreFinestra = True,
                                                 .Invio = Sub()
                                                              Dim centro As Action = Nothing
                                                              If _funzioni.TryGetValue("Centro notifiche", centro) Then centro()
                                                          End Sub})
        End If
        Return voci
    End Function

    Private Shared ReadOnly Italiano As New System.Globalization.CultureInfo("it-IT")

    Private Shared Function TestoNotifiche() As String
        Dim daLeggere As Integer = WGContenitore.NotificheDaLeggere()
        Return "Centro notifiche" & If(daLeggere = 0, "", If(daLeggere = 1, ", una da leggere", ", " & daLeggere & " da leggere"))
    End Function

    Private Shared Function TestoOra() As String
        Dim adesso As DateTime = DateTime.Now
        Return adesso.ToString("H:mm", Italiano) & ", " & adesso.ToString("dddd d MMMM", Italiano)
    End Function

    Private Shared Function TestoVolume() As String
        Try
            If WGAudio.AltoparlanteDisattivato() Then Return "Volume muto"
            Return "Volume " & WGAudio.VolumeAltoparlante() & " per cento"
        Catch ex As Exception
            log.ScriviLog("WGScrivania.TestoVolume: " & ex.Message)
            Return "Volume non disponibile"
        End Try
    End Function

    Private Shared Sub CambiaVolume(passo As Integer)
        Try
            If WGAudio.AltoparlanteDisattivato() Then WGAudio.ImpostaAltoparlanteDisattivato(False)
            WGAudio.ImpostaVolumeAltoparlante(Math.Max(0, Math.Min(100, WGAudio.VolumeAltoparlante() + passo)))
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CambiaVolume: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub CommutaMuto()
        Try
            WGAudio.ImpostaAltoparlanteDisattivato(Not WGAudio.AltoparlanteDisattivato())
        Catch ex As Exception
            log.ScriviLog("WGScrivania.CommutaMuto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function TestoBatteria() As String
        Try
            ' La stessa descrizione della home: percentuale, carica e, a batteria, il tempo che resta.
            Return WGStatoDispositivo.Batteria().Descrizione
        Catch ex As Exception
            log.ScriviLog("WGScrivania.TestoBatteria: " & ex.Message)
            Return "Batteria non disponibile"
        End Try
    End Function

    Private Shared _testoConnessione As String = "Connessione: controllo in corso"
    Private Shared _istanteConnessione As DateTime = DateTime.MinValue
    Private Shared _aggiornamentoConnessione As Integer

    Private Shared Function TestoConnessione() As String
        AggiornaConnessioneSeServe()
        Return _testoConnessione
    End Function

    ' Il nome della rete Wi-Fi viene da netsh, che impiega un attimo: si legge in sottofondo e si tiene
    ' da parte per venti secondi, così la barra non rallenta il disegno.
    Private Shared Sub AggiornaConnessioneSeServe(Optional forza As Boolean = False)
        If Not forza AndAlso (DateTime.Now - _istanteConnessione).TotalSeconds < 20 Then Return
        If System.Threading.Interlocked.Exchange(_aggiornamentoConnessione, 1) = 1 Then Return
        _istanteConnessione = DateTime.Now
        Task.Run(Sub()
                     Try
                         _testoConnessione = LeggiConnessione()
                     Catch ex As Exception
                         log.ScriviLog("WGScrivania.AggiornaConnessione: " & ex.Message)
                     Finally
                         System.Threading.Interlocked.Exchange(_aggiornamentoConnessione, 0)
                     End Try
                 End Sub)
    End Sub

    Private Shared Function LeggiConnessione() As String
        Dim attive = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(
            Function(n) n.OperationalStatus = System.Net.NetworkInformation.OperationalStatus.Up AndAlso
                        n.NetworkInterfaceType <> System.Net.NetworkInformation.NetworkInterfaceType.Loopback AndAlso
                        n.NetworkInterfaceType <> System.Net.NetworkInformation.NetworkInterfaceType.Tunnel AndAlso
                        Not n.Description.ToLowerInvariant().Contains("virtual")).ToList()
        If attive.Count = 0 Then Return "Nessuna connessione"
        If attive.Any(Function(n) n.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211) Then
            Dim rete As String = ""
            Dim segnale As String = ""
            LeggiWifi(rete, segnale)
            Return "Wi-Fi" & If(rete <> "", " " & rete, "") & If(segnale <> "", ", segnale " & segnale, "")
        End If
        If attive.Any(Function(n) n.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.Ethernet OrElse
                                  n.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet) Then Return "Cavo di rete"
        Return "Connesso"
    End Function

    Private Shared Sub LeggiWifi(ByRef rete As String, ByRef segnale As String)
        Try
            Dim avvio As New ProcessStartInfo("netsh", "wlan show interfaces") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True,
                .StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)}
            Using processo As Process = Process.Start(avvio)
                Dim uscita As String = processo.StandardOutput.ReadToEnd()
                processo.WaitForExit(3000)
                For Each riga As String In uscita.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim pos As Integer = riga.IndexOf(":"c)
                    If pos <= 0 Then Continue For
                    Dim etichetta As String = riga.Substring(0, pos).Trim()
                    Dim valore As String = riga.Substring(pos + 1).Trim()
                    If etichetta = "SSID" AndAlso rete = "" Then rete = valore
                    If (etichetta.StartsWith("Segnale", StringComparison.OrdinalIgnoreCase) OrElse etichetta.StartsWith("Signal", StringComparison.OrdinalIgnoreCase)) AndAlso valore.EndsWith("%") Then segnale = valore.Replace("%", " per cento")
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGScrivania.LeggiWifi: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Apertura"

    ' L'elemento si apre come applicazione con il suo nome; se è già aperta ci si torna.
    Private Shared Sub Apri(e As Elemento)
        Dim apertura As Task = ApriAsync(e)
    End Sub

    Private Shared Async Function ApriAsync(e As Elemento) As Task
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.VaiAllApplicazioneConNome(e.Nome) Then Return
            Select Case e.Tipo
                Case TipoFile
                    If Not File.Exists(e.Destinazione) Then
                        Messaggio("Il file " & e.Destinazione & " non esiste più.")
                        Return
                    End If
                    If ApriFile Is Nothing Then Return
                    If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(e.Nome, "scrivania:" & e.Id)
                    WGRecenti.Registra(e.Destinazione)
                    ApriFile.Invoke(e.Destinazione)
                Case TipoCartella
                    If Not Directory.Exists(e.Destinazione) Then
                        Messaggio("La cartella " & e.Destinazione & " non esiste più.")
                        Return
                    End If
                    If ApriCartella Is Nothing Then Return
                    If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(e.Nome, "scrivania:" & e.Id)
                    WGRecenti.Registra(e.Destinazione)
                    ApriCartella.Invoke(e.Destinazione)
                Case TipoMenu
                    If contenitore Is Nothing Then Return
                    contenitore.ApriComeNuovaApplicazione(e.Nome, "scrivania:" & e.Id)
                    Dim voci As List(Of String) = e.Destinazione.Split(New String() {SeparatoreMenu}, StringSplitOptions.None).ToList()
                    If Not Await contenitore.EseguiPercorsoMenuAsync(voci) Then
                        Messaggio("Il collegamento " & e.Nome & " non si apre più: le voci del menu sono cambiate. Si può eliminarlo e aggiungerlo di nuovo dal menu.")
                    End If
                Case TipoFunzione
                    Dim azione As Action = Nothing
                    If Not _funzioni.TryGetValue(e.Destinazione, azione) Then
                        Messaggio("La funzione " & e.Nome & " non è più disponibile.")
                        Return
                    End If
                    If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(e.Nome, "scrivania:" & e.Id)
                    azione()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Apri: " & ex.Message)
        End Try
    End Function

    Private Shared Sub MostraNellaGestioneFile(e As Elemento)
        Try
            If ApriCartella Is Nothing Then Return
            Dim cartella As String = If(Directory.Exists(e.Destinazione), e.Destinazione, Path.GetDirectoryName(e.Destinazione))
            If String.IsNullOrEmpty(cartella) OrElse Not Directory.Exists(cartella) Then
                Messaggio("La cartella non esiste più.")
                Return
            End If
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(NomeDaPercorso(cartella))
            ApriCartella.Invoke(cartella)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.MostraNellaGestioneFile: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Messaggio: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Aggiunta di collegamenti"

    ''' <summary>Dalla gestione file: aggiunge alla scrivania un collegamento a un file o a una cartella.</summary>
    Public Shared Sub AggiungiPercorso(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            If Directory.Exists(percorso) Then
                Aggiungi(TipoCartella, NomeDaPercorso(percorso), percorso)
            ElseIf File.Exists(percorso) Then
                Aggiungi(TipoFile, NomeDaPercorso(percorso), percorso)
            Else
                Messaggio("Il percorso " & percorso & " non esiste.")
            End If
        Catch ex As Exception
            log.ScriviLog("WGScrivania.AggiungiPercorso: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Da qualsiasi menu: aggiunge alla scrivania la voce, ricordando le voci da scegliere per arrivarci dal menu applicazioni.</summary>
    Public Shared Sub AggiungiVoceDiMenu(voci As List(Of String))
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return
            Aggiungi(TipoMenu, voci(voci.Count - 1), String.Join(SeparatoreMenu, voci))
        Catch ex As Exception
            log.ScriviLog("WGScrivania.AggiungiVoceDiMenu: " & ex.Message)
        End Try
    End Sub

    Private Shared Function NomeDaPercorso(percorso As String) As String
        Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"c))
        Return If(String.IsNullOrEmpty(nome), percorso, nome)
    End Function

    ' Quello che si aggiunge dalla scrivania va nella cartella aperta.
    Private Shared Function CartellaAperta() As String
        Return If(_griglia IsNot Nothing, _cartellaCorrente, Radice)
    End Function

    ' Tutte le funzioni di ordinamento in un menu solo: per nome, per tipo, per data e, su un elemento, spostarlo.
    Private Shared Sub ScegliOrdinamento(e As Elemento)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Per nome", Sub(s, ev) Ordina("nome"))
            menu.aggiungi("Per tipo", Sub(s, ev) Ordina("tipo"))
            menu.aggiungi("Per data di aggiunta", Sub(s, ev) Ordina("data"))
            If e IsNot Nothing Then
                menu.aggiungi("Sposta l'elemento prima", Sub(s, ev) Sposta(e, -1))
                menu.aggiungi("Sposta l'elemento dopo", Sub(s, ev) Sposta(e, 1))
            End If
            menu.avvia("Ordinamento", True)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliOrdinamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliCosaAggiungere()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Collegamento a un file", Sub(s, ev) ScegliCollegamento(False))
            menu.aggiungi("Collegamento a una cartella", Sub(s, ev) ScegliCollegamento(True))
            menu.aggiungi("Collegamento a una funzione di WinGabriel", Sub(s, ev) ScegliFunzione())
            menu.avvia("Aggiungi", True)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliCosaAggiungere: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliCollegamento(cartella As Boolean)
        Try
            Dim genitore As String = CartellaAperta()
            Dim dialogo As New WGFileDialog()
            dialogo.MultiselezioneConsentita = False
            If cartella Then
                AddHandler dialogo.singolaselezione, Sub(percorso As String)
                                                         If Directory.Exists(percorso) Then Aggiungi(TipoCartella, NomeDaPercorso(percorso), percorso, genitore)
                                                     End Sub
            Else
                AddHandler dialogo.selezioneterminata, Sub(scelti As List(Of String))
                                                           If scelti Is Nothing OrElse scelti.Count = 0 Then Return
                                                           Dim percorso As String = scelti(0)
                                                           If File.Exists(percorso) Then
                                                               Aggiungi(TipoFile, NomeDaPercorso(percorso), percorso, genitore)
                                                           ElseIf Directory.Exists(percorso) Then
                                                               Aggiungi(TipoCartella, NomeDaPercorso(percorso), percorso, genitore)
                                                           End If
                                                       End Sub
            End If
            dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliCollegamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliFunzione()
        Try
            If _ordineFunzioni.Count = 0 Then
                Messaggio("Nessuna funzione disponibile.")
                Return
            End If
            Dim genitore As String = CartellaAperta()
            Dim menu As New WGMenu()
            For Each nome As String In _ordineFunzioni
                Dim scelta As String = nome
                menu.aggiungi(scelta, Sub(s, ev) Aggiungi(TipoFunzione, scelta, scelta, genitore))
            Next
            menu.avvia("Funzioni", True)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliFunzione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Aggiungi(tipo As String, nome As String, destinazione As String, Optional genitore As String = Radice)
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return
            Dim esistente As Object = d.EseguiScalare("SELECT " & WGDatabase.NomeColonnaChiavePredefinita & " FROM " & Tabella & " WHERE Tipo = @t AND Destinazione = @d COLLATE NOCASE AND IFNULL(Proprio,0) = 0 AND IFNULL(Genitore,'') = @g",
                                                    New Dictionary(Of String, Object) From {{"@t", tipo}, {"@d", destinazione}, {"@g", genitore}})
            If esistente IsNot Nothing AndAlso Not IsDBNull(esistente) Then
                sintesivocale.GetInstance().LeggiTesto(nome & " è già sulla scrivania")
                Return
            End If
            Dim id As String = Inserisci(genitore, tipo, If(String.IsNullOrWhiteSpace(nome), destinazione, nome), destinazione, False)
            sintesivocale.GetInstance().LeggiTesto("Collegamento " & nome & " aggiunto alla scrivania")
            If genitore = _cartellaCorrente Then Ridisegna(id)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Aggiungi: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "File copiati sulla scrivania e appunti"

    ''' <summary>Copia file e cartelle sulla scrivania (dalla gestione file).</summary>
    Public Shared Sub CopiaSullaScrivania(percorsi As IEnumerable(Of String))
        PortaSullaScrivania(percorsi, False, Radice)
    End Sub

    ' La copia gira in un thread a parte, con l'attesa di WinGabriel. Gli elementi entrano nel database a copia finita.
    Private Shared Async Sub PortaSullaScrivania(percorsi As IEnumerable(Of String), spostare As Boolean, genitore As String)
        Try
            Dim sorgenti As List(Of String) = If(percorsi, Enumerable.Empty(Of String)()).Where(Function(p) File.Exists(p) OrElse Directory.Exists(p)).ToList()
            If sorgenti.Count = 0 Then
                sintesivocale.GetInstance().LeggiTesto("Nessun file o cartella da copiare")
                Return
            End If
            Dim destinazioneBase As String = CartellaFile()
            If destinazioneBase = "" Then Return
            Dim fatti As New List(Of Tuple(Of String, String, String))()
            Dim errori As Integer = 0
            Await WGProcessoInCorso.EseguiAsync(If(spostare, "Spostamento", "Copia") & " sulla scrivania", Function() Task.Run(Sub()
                         For Each sorgente As String In sorgenti
                             Try
                                 Dim nome As String = NomeDaPercorso(sorgente)
                                 Dim arrivo As String = NomeLibero(destinazioneBase, nome)
                                 Dim eCartella As Boolean = Directory.Exists(sorgente)
                                 If eCartella Then
                                     If spostare Then FileVb.MoveDirectory(sorgente, arrivo) Else FileVb.CopyDirectory(sorgente, arrivo)
                                 Else
                                     If spostare Then FileVb.MoveFile(sorgente, arrivo) Else FileVb.CopyFile(sorgente, arrivo)
                                 End If
                                 fatti.Add(Tuple.Create(If(eCartella, TipoCartella, TipoFile), Path.GetFileName(arrivo), arrivo))
                             Catch ex As Exception
                                 errori += 1
                                 log.ScriviLog("WGScrivania.PortaSullaScrivania " & sorgente & ": " & ex.Message)
                             End Try
                         Next
                     End Sub))
            Dim ultimo As String = ""
            For Each f As Tuple(Of String, String, String) In fatti
                ultimo = Inserisci(genitore, f.Item1, f.Item2, f.Item3, True)
            Next
            If fatti.Count = 0 Then
                WGDomanda.Chiedi("Non è stato possibile " & If(spostare, "spostare", "copiare") & " niente sulla scrivania. Vuoi riprovare?", Sub() PortaSullaScrivania(sorgenti, spostare, genitore))
                Return
            End If
            Dim testo As String
            If fatti.Count = 1 Then
                testo = fatti(0).Item2 & If(spostare, " spostato", " copiato") & " sulla scrivania"
            Else
                testo = fatti.Count & If(spostare, " elementi spostati", " elementi copiati") & " sulla scrivania"
            End If
            If errori > 0 Then testo &= ", " & errori & " non riusciti"
            sintesivocale.GetInstance().LeggiTesto(testo)
            If genitore = _cartellaCorrente Then Ridisegna(ultimo)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.PortaSullaScrivania: " & ex.Message)
        End Try
    End Sub

    ' Nome che non c'è ancora nella cartella: "relazione.docx", poi "relazione (2).docx" e così via.
    Private Shared Function NomeLibero(cartella As String, nome As String) As String
        Dim candidato As String = Path.Combine(cartella, nome)
        Dim base As String = Path.GetFileNameWithoutExtension(nome)
        Dim estensione As String = Path.GetExtension(nome)
        Dim numero As Integer = 2
        While File.Exists(candidato) OrElse Directory.Exists(candidato)
            candidato = Path.Combine(cartella, base & " (" & numero & ")" & estensione)
            numero += 1
        End While
        Return candidato
    End Function

    Private Shared Sub ScegliDaCopiare(cartella As Boolean)
        Try
            Dim genitore As String = CartellaAperta()
            Dim dialogo As New WGFileDialog()
            If cartella Then
                dialogo.MultiselezioneConsentita = False
                AddHandler dialogo.singolaselezione, Sub(percorso As String) PortaSullaScrivania({percorso}, False, genitore)
            Else
                dialogo.MultiselezioneConsentita = True
                AddHandler dialogo.selezioneterminata, Sub(scelti As List(Of String)) PortaSullaScrivania(scelti, False, genitore)
            End If
            dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliDaCopiare: " & ex.Message)
        End Try
    End Sub

    ' Gli appunti sono quelli di Windows, nello stesso formato della gestione file: quello che si copia
    ' dalla scrivania si incolla nella gestione file (e in Esplora risorse), e viceversa.
    Private Shared Sub MettiNegliAppunti(e As Elemento, taglia As Boolean)
        Try
            Dim percorsi As List(Of String) = PercorsiDi(e)
            ' Si tagliano solo i file della scrivania, mai gli originali a cui puntano i collegamenti.
            If taglia Then
                Dim base As String = CartellaFile()
                percorsi = percorsi.Where(Function(p) p.StartsWith(base, StringComparison.OrdinalIgnoreCase)).ToList()
            End If
            If percorsi.Count = 0 Then
                sintesivocale.GetInstance().LeggiTesto("Nessun file o cartella da mettere negli appunti")
                Return
            End If
            Dim elenco As New StringCollection()
            elenco.AddRange(percorsi.ToArray())
            Dim dati As New DataObject()
            dati.SetFileDropList(elenco)
            dati.SetData("Preferred DropEffect", New MemoryStream(New Byte() {CByte(If(taglia, 2, 5)), 0, 0, 0}))
            Clipboard.SetDataObject(dati, True, 5, 100)
            sintesivocale.GetInstance().LeggiTesto(e.Nome & If(taglia, " tagliato", " copiato") & If(percorsi.Count > 1, ", " & percorsi.Count & " elementi", ""))
        Catch ex As Exception
            log.ScriviLog("WGScrivania.MettiNegliAppunti: " & ex.Message)
            WGDomanda.Chiedi("Gli appunti di Windows sono occupati da un'altra applicazione. Vuoi riprovare?", Sub() MettiNegliAppunti(e, taglia))
        End Try
    End Sub

    Private Shared Sub Incolla()
        Try
            If Not Clipboard.ContainsFileDropList() Then
                sintesivocale.GetInstance().LeggiTesto("Negli appunti non ci sono file o cartelle")
                Return
            End If
            Dim percorsi As New List(Of String)()
            For Each p As String In Clipboard.GetFileDropList()
                percorsi.Add(p)
            Next
            Dim spostare As Boolean = False
            Dim effetto As MemoryStream = TryCast(Clipboard.GetData("Preferred DropEffect"), MemoryStream)
            If effetto IsNot Nothing AndAlso effetto.Length > 0 Then
                Dim valore As Byte = effetto.ToArray()(0)
                spostare = (valore And 2) = 2 AndAlso (valore And 1) = 0
            End If
            If spostare Then
                ' File della scrivania tagliati e incollati di nuovo qui: basta cambiare cartella nel database.
                Dim base As String = CartellaFile()
                Dim giaQui As List(Of String) = percorsi.Where(Function(p) p.StartsWith(base, StringComparison.OrdinalIgnoreCase)).ToList()
                For Each p As String In giaQui
                    Dim chiave As Object = Db().EseguiScalare("SELECT " & WGDatabase.NomeColonnaChiavePredefinita & " FROM " & Tabella & " WHERE Destinazione = @d COLLATE NOCASE AND IFNULL(Proprio,0) = 1", New Dictionary(Of String, Object) From {{"@d", p}})
                    Dim e As Elemento = If(chiave Is Nothing OrElse IsDBNull(chiave), Nothing, Trova(Convert.ToString(chiave)))
                    If e IsNot Nothing Then
                        e.Genitore = _cartellaCorrente
                        e.Ordine = ProssimoOrdine(_cartellaCorrente)
                        Aggiorna(e)
                    End If
                Next
                percorsi = percorsi.Except(giaQui).ToList()
                Clipboard.Clear()
                If percorsi.Count = 0 Then
                    sintesivocale.GetInstance().LeggiTesto("Spostato")
                    Ridisegna()
                    Return
                End If
            End If
            PortaSullaScrivania(percorsi, spostare, _cartellaCorrente)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Incolla: " & ex.Message)
            WGDomanda.Chiedi("Gli appunti di Windows sono occupati da un'altra applicazione. Vuoi riprovare?", Sub() Incolla())
        End Try
    End Sub

#End Region

#Region "Modifica degli elementi"

    Private Shared Sub NuovaCartella()
        Try
            Dim genitore As String = CartellaAperta()
            ChiediNome("Nome della nuova cartella della scrivania", "Nuova cartella", Sub(nome As String)
                                                                                       Dim id As String = Inserisci(genitore, TipoGruppo, nome, "", False)
                                                                                       sintesivocale.GetInstance().LeggiTesto("Cartella " & nome & " creata")
                                                                                       If genitore = _cartellaCorrente Then Ridisegna(id)
                                                                                   End Sub)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.NuovaCartella: " & ex.Message)
        End Try
    End Sub

    ' Rinominare cambia solo il nome mostrato: il riferimento interno (percorso, funzione, voci del menu) resta.
    ' I file copiati sulla scrivania si rinominano anche sul disco, così negli appunti hanno il nome giusto.
    Private Shared Sub Rinomina(e As Elemento)
        Try
            Dim id As String = e.Id
            ChiediNome("Nuovo nome", e.Nome, Sub(nome As String)
                                                 Dim aggiornato As Elemento = Trova(id)
                                                 If aggiornato Is Nothing Then Return
                                                 aggiornato.Nome = nome
                                                 If aggiornato.Proprio Then RinominaSulDisco(aggiornato, nome)
                                                 Aggiorna(aggiornato)
                                                 sintesivocale.GetInstance().LeggiTesto("Rinominato " & nome)
                                                 Ridisegna(id)
                                             End Sub)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Rinomina: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RinominaSulDisco(e As Elemento, nome As String)
        Try
            Dim nonValidi As Char() = Path.GetInvalidFileNameChars()
            Dim valido As String = New String(nome.Select(Function(ch) If(nonValidi.Contains(ch), "_"c, ch)).ToArray())
            If e.Tipo = TipoFile AndAlso Path.GetExtension(valido) = "" Then valido &= Path.GetExtension(e.Destinazione)
            Dim cartella As String = Path.GetDirectoryName(e.Destinazione)
            If String.Equals(Path.Combine(cartella, valido), e.Destinazione, StringComparison.OrdinalIgnoreCase) Then Return
            Dim arrivo As String = NomeLibero(cartella, valido)
            If e.Tipo = TipoCartella Then Directory.Move(e.Destinazione, arrivo) Else File.Move(e.Destinazione, arrivo)
            e.Destinazione = arrivo
        Catch ex As Exception
            log.ScriviLog("WGScrivania.RinominaSulDisco: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiediNome(etichetta As String, valore As String, fatto As Action(Of String))
        Dim campi As New Campi()
        campi.Aggiungi(etichetta, "nome", True, False, valore)
        AddHandler campi.Fine, Sub(s, ev)
                                   Try
                                       Dim nome As String = Pulisci(campi.RecuperaValore("nome"))
                                       If nome = "" Then Return
                                       fatto(nome)
                                   Catch ex As Exception
                                       log.ScriviLog("WGScrivania.ChiediNome: " & ex.Message)
                                   End Try
                               End Sub
        campi.Avvia()
    End Sub

    ' Riordina la cartella aperta: l'ordine resta salvato, poi si può ancora spostare a mano.
    Private Shared Sub Ordina(criterio As String)
        Try
            Dim elenco As List(Of Elemento) = Contenuto(_cartellaCorrente)
            Dim ordinato As List(Of Elemento)
            Select Case criterio
                Case "tipo"
                    ' Prima le cartelle della scrivania, poi cartelle, file, funzioni e voci di menu; a parità, per nome.
                    Dim ordineTipi As String() = {TipoGruppo, TipoCartella, TipoFile, TipoFunzione, TipoMenu}
                    ordinato = elenco.OrderBy(Function(x) Array.IndexOf(ordineTipi, x.Tipo)).ThenBy(Function(x) x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList()
                Case "data"
                    Dim ordineDate As New List(Of String)()
                    For Each riga As DataRow In Db().EseguiQuery("SELECT " & WGDatabase.NomeColonnaChiavePredefinita & " FROM " & Tabella & " WHERE IFNULL(Genitore,'') = @g ORDER BY " & WGDatabase.NomeColonnaDataCreazione, New Dictionary(Of String, Object) From {{"@g", _cartellaCorrente}}).Rows
                        ordineDate.Add(Convert.ToString(riga(0)))
                    Next
                    ordinato = elenco.OrderBy(Function(x) ordineDate.IndexOf(x.Id)).ToList()
                Case Else
                    ordinato = elenco.OrderBy(Function(x) x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList()
            End Select
            For i As Integer = 0 To ordinato.Count - 1
                If ordinato(i).Ordine <> i + 1 Then
                    ordinato(i).Ordine = i + 1
                    Aggiorna(ordinato(i))
                End If
            Next
            sintesivocale.GetInstance().LeggiTesto(If(criterio = "tipo", "Ordinati per tipo", If(criterio = "data", "Ordinati per data di aggiunta", "Ordinati per nome")))
            Ridisegna()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Ordina: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Sposta(e As Elemento, direzione As Integer)
        Try
            Dim elenco As List(Of Elemento) = Contenuto(e.Genitore)
            Dim posizione As Integer = elenco.FindIndex(Function(x) x.Id = e.Id)
            Dim nuova As Integer = posizione + direzione
            If posizione < 0 OrElse nuova < 0 OrElse nuova >= elenco.Count Then
                sintesivocale.GetInstance().LeggiTesto(If(direzione < 0, "Già il primo", "Già l'ultimo"))
                Return
            End If
            Dim spostato As Elemento = elenco(posizione)
            elenco.RemoveAt(posizione)
            elenco.Insert(nuova, spostato)
            For i As Integer = 0 To elenco.Count - 1
                If elenco(i).Ordine <> i + 1 Then
                    elenco(i).Ordine = i + 1
                    Aggiorna(elenco(i))
                End If
            Next
            Ridisegna(e.Id)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Sposta: " & ex.Message)
        End Try
    End Sub

    ' Le cartelle in cui si può spostare l'elemento: la scrivania e tutte le cartelle della scrivania
    ' tranne l'elemento stesso e quelle al suo interno.
    Private Shared Sub ScegliCartellaDestinazione(e As Elemento)
        Try
            Dim menu As New WGMenu()
            If e.Genitore <> Radice Then menu.aggiungi("Scrivania", Sub(s, ev) SpostaIn(e, Radice))
            AggiungiCartelleAlMenu(menu, Radice, "", e)
            menu.aggiungi("Nuova cartella della scrivania", Sub(s, ev)
                                                                ChiediNome("Nome della nuova cartella della scrivania", "Nuova cartella",
                                                                           Sub(nome As String)
                                                                               Dim id As String = Inserisci(e.Genitore, TipoGruppo, nome, "", False)
                                                                               SpostaIn(e, id)
                                                                           End Sub)
                                                            End Sub)
            menu.avvia("Sposta in", True)
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ScegliCartellaDestinazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub AggiungiCartelleAlMenu(menu As WGMenu, genitore As String, prefisso As String, daSpostare As Elemento)
        For Each c As Elemento In Contenuto(genitore).Where(Function(x) x.Tipo = TipoGruppo AndAlso x.Id <> daSpostare.Id)
            Dim destinazione As String = c.Id
            Dim etichetta As String = prefisso & c.Nome
            If destinazione <> daSpostare.Genitore Then menu.aggiungi(etichetta, Sub(s, ev) SpostaIn(daSpostare, destinazione))
            AggiungiCartelleAlMenu(menu, destinazione, etichetta & SeparatoreMenu, daSpostare)
        Next
    End Sub

    Private Shared Sub SpostaIn(e As Elemento, genitore As String)
        Try
            e.Genitore = genitore
            e.Ordine = ProssimoOrdine(genitore)
            Aggiorna(e)
            Dim cartella As Elemento = Trova(genitore)
            sintesivocale.GetInstance().LeggiTesto(e.Nome & " spostato in " & If(cartella Is Nothing, "scrivania", cartella.Nome))
            Ridisegna()
        Catch ex As Exception
            log.ScriviLog("WGScrivania.SpostaIn: " & ex.Message)
        End Try
    End Sub

    ' I collegamenti e le cartelle vuote si tolgono senza domande; file copiati e cartelle con dentro
    ' qualcosa chiedono conferma, e i file vanno nel cestino.
    Private Shared Sub ChiediElimina(e As Elemento)
        Try
            If Not e.Proprio AndAlso (e.Tipo <> TipoGruppo OrElse Contenuto(e.Id).Count = 0) Then
                If Elimina(e) Then
                    sintesivocale.GetInstance().LeggiTesto(e.Nome & " eliminato")
                    Ridisegna()
                Else
                    WGDomanda.Chiedi("Non è stato possibile eliminare " & e.Nome & ". Vuoi riprovare?", Sub() ChiediElimina(e))
                End If
                Return
            End If
            Dim domanda As String
            If e.Tipo = TipoGruppo Then
                domanda = "Eliminare la cartella " & e.Nome & " con tutto quello che contiene?" & If(ContieneFilePropri(e), " I file copiati sulla scrivania vanno nel cestino", "")
            Else
                domanda = "Eliminare " & e.Nome & "? Va nel cestino"
            End If
            WGDomanda.Chiedi(domanda, Async Sub() Await EliminaConAttesaAsync(e))
        Catch ex As Exception
            log.ScriviLog("WGScrivania.ChiediElimina: " & ex.Message)
        End Try
    End Sub

    ' Con l'attesa; se qualcosa resta (un file aperto in un'altra applicazione) si chiede se riprovare.
    Private Shared Async Function EliminaConAttesaAsync(e As Elemento) As Task
        Try
            ' Il centro di controllo (che manda nel cestino) nasce qui, sul thread dell'interfaccia, non in sottofondo.
            WGCentroDiControllo.GetInstance()
            Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync("Eliminazione di " & e.Nome, Function() Task.Run(Function() Elimina(e)))
            Ridisegna()
            If riuscito Then
                sintesivocale.GetInstance().LeggiTesto(e.Nome & " eliminato")
            Else
                WGDomanda.Chiedi("Non è stato possibile eliminare tutto " & e.Nome & ": forse un file è aperto in un'altra applicazione. Vuoi riprovare?", Async Sub() Await EliminaConAttesaAsync(e))
            End If
        Catch ex As Exception
            log.ScriviLog("WGScrivania.EliminaConAttesaAsync: " & ex.Message)
        End Try
    End Function

    ' Vero se è stato eliminato tutto. I file propri vanno nel cestino senza finestre di Windows; una
    ' cartella della scrivania resta (con quello che non si è potuto eliminare) se qualcosa dentro resta,
    ' così niente rimane nel database senza essere più raggiungibile.
    Private Shared Function Elimina(e As Elemento) As Boolean
        Try
            If e.Tipo = TipoGruppo Then
                Dim tutti As Boolean = True
                For Each figlio As Elemento In Contenuto(e.Id)
                    If Not Elimina(figlio) Then tutti = False
                Next
                If Not tutti Then Return False
            End If
            If e.Proprio AndAlso (Directory.Exists(e.Destinazione) OrElse File.Exists(e.Destinazione)) Then
                If Not WGCentroDiControllo.GetInstance().SpostaNelCestinoSenzaFinestre(e.Destinazione) Then Return False
            End If
            Db().EliminaRecord(Tabella, e.Id)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGScrivania.Elimina: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

End Class
