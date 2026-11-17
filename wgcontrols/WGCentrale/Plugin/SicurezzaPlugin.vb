Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb
Imports WGPlugin

''' <summary>
''' I permessi che l'utente ha concesso ai plugin per leggere i dati di WinGabriel. Si salvano nella configurazione, con una chiave che
''' un plugin non può scrivere (i suoi dati stanno sotto "plugindati:"). Si controllano a ogni richiesta: revocare un permesso ha
''' effetto subito.
''' </summary>
Friend NotInheritable Class PermessiPlugin

    Private Sub New()
    End Sub

    Friend Shared ReadOnly Elenco As String() = {
        WGPermessi.Note, WGPermessi.NoteScrittura, WGPermessi.NoteProtette,
        WGPermessi.Rubrica, WGPermessi.RubricaScrittura, WGPermessi.RubricaProtette,
        WGPermessi.Agenda, WGPermessi.AgendaScrittura, WGPermessi.AgendaProtette, WGPermessi.Spegnimento}

    Friend Shared Function Valido(permesso As String) As Boolean
        Return Array.IndexOf(Elenco, permesso) >= 0
    End Function

    ''' <summary>Quello che il permesso consente, all'infinito, per le frasi: "leggere le tue note", "aggiungere note".</summary>
    Friend Shared Function Cosa(permesso As String) As String
        Select Case permesso
            Case WGPermessi.Note : Return "leggere le tue note"
            Case WGPermessi.NoteScrittura : Return "aggiungere note"
            Case WGPermessi.NoteProtette : Return "leggere le note protette da password, con la password che scrivi tu ogni volta"
            Case WGPermessi.Rubrica : Return "leggere la rubrica"
            Case WGPermessi.RubricaScrittura : Return "aggiungere contatti alla rubrica"
            Case WGPermessi.RubricaProtette : Return "leggere le rubriche protette da password, con la password che scrivi tu ogni volta"
            Case WGPermessi.Agenda : Return "leggere l'agenda"
            Case WGPermessi.AgendaScrittura : Return "aggiungere appuntamenti all'agenda"
            Case WGPermessi.AgendaProtette : Return "leggere le agende protette da password, con la password che scrivi tu ogni volta"
            Case WGPermessi.Spegnimento : Return "spegnere il computer dopo il tempo che scegli tu"
        End Select
        Return permesso
    End Function

    ''' <summary>Il nome della voce nelle impostazioni.</summary>
    Friend Shared Function Titolo(permesso As String) As String
        Select Case permesso
            Case WGPermessi.Note : Return "Note, lettura"
            Case WGPermessi.NoteScrittura : Return "Note, aggiunta"
            Case WGPermessi.NoteProtette : Return "Note protette da password"
            Case WGPermessi.Rubrica : Return "Rubrica, lettura"
            Case WGPermessi.RubricaScrittura : Return "Rubrica, aggiunta di contatti"
            Case WGPermessi.RubricaProtette : Return "Rubriche protette da password"
            Case WGPermessi.Agenda : Return "Agenda, lettura"
            Case WGPermessi.AgendaScrittura : Return "Agenda, aggiunta di appuntamenti"
            Case WGPermessi.AgendaProtette : Return "Agende protette da password"
            Case WGPermessi.Spegnimento : Return "Spegnimento del computer"
        End Select
        Return permesso
    End Function

    Private Shared Function Chiave(id As String, permesso As String) As String
        Return "plugin_permesso:" & id & ":" & permesso
    End Function

    Friend Shared Function Concesso(id As String, permesso As String) As Boolean
        If Not Valido(permesso) Then Return False
        Return If((New WGConfig()).LeggiValore(Chiave(id, permesso)), "") = "1"
    End Function

    Friend Shared Sub Concedi(id As String, permesso As String)
        If Not Valido(permesso) Then Return
        Dim configurazione As New WGConfig()
        configurazione.SalvaValore(Chiave(id, permesso), "1")
    End Sub

    Friend Shared Sub Revoca(id As String, permesso As String)
        If Not Valido(permesso) Then Return
        Dim configurazione As New WGConfig()
        configurazione.EliminaValore(Chiave(id, permesso))
    End Sub

    Friend Shared Sub RevocaTutti(id As String)
        For Each permesso As String In Elenco
            Revoca(id, permesso)
        Next
    End Sub

End Class

''' <summary>
''' L'elenco dei plugin bloccati, scritto da chi sviluppa WinGabriel e firmato con la stessa chiave dei rilasci. Si scarica a ogni
''' avvio (e ogni 12 ore) da wingabriel.net; senza internet vale l'ultima copia verificata. Un plugin che è nell'elenco (per
''' impronta del file o per nome) non parte, e se era già attivo si disattiva subito. Se la firma dell'elenco non è valida, o se il
''' numero non è più alto di quello già visto, l'elenco scaricato si scarta: nessuno può togliere dall'elenco un plugin bloccato
''' rimettendo una copia vecchia.
''' </summary>
Friend NotInheritable Class RevochePlugin

    Private Const UrlLista As String = "https://www.wingabriel.net/plugin/revoche.json"
    Private Const MassimoByte As Integer = 262144

    Private Shared ReadOnly log As New wgcontrols.Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _voci As New List(Of Tuple(Of String, String, String))()
    Private Shared _numero As Long
    Private Shared _caricata As Boolean

    Private Sub New()
    End Sub

    Private Shared Function PercorsoLista() As String
        Dim cartella As String = Path.Combine(WGPercorsiApplicazione.CartellaConfig(), "plugin")
        If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
        Return Path.Combine(cartella, "revoche.json")
    End Function

    ''' <summary>Legge la copia salvata (se la firma è valida). Si chiama una volta, prima di caricare i plugin.</summary>
    Friend Shared Sub CaricaDaCopia()
        Try
            SyncLock _blocco
                If _caricata Then Return
                _caricata = True
            End SyncLock
            Dim percorso As String = PercorsoLista()
            If Not File.Exists(percorso) OrElse Not File.Exists(percorso & ".firma") Then Return
            Dim dati As Byte() = File.ReadAllBytes(percorso)
            If Not WGGestorePlugin.VerificaFirma(dati, File.ReadAllText(percorso & ".firma")) Then Return
            Applica(dati)
        Catch ex As Exception
            log.ScriviLog("RevochePlugin.CaricaDaCopia: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Testo(valore As JToken) As String
        If valore Is Nothing OrElse valore.Type = JTokenType.Null Then Return ""
        Return valore.ToString()
    End Function

    ''' <summary>Legge il contenuto (già verificato) e lo rende quello in vigore. Restituisce il numero dell'elenco.</summary>
    Private Shared Function Applica(dati As Byte()) As Long
        Dim radice As JObject = JObject.Parse(Encoding.UTF8.GetString(dati))
        Dim numero As Long = radice("numero").Value(Of Long)()
        Dim voci As New List(Of Tuple(Of String, String, String))()
        Dim bloccati As JArray = TryCast(radice("bloccati"), JArray)
        If bloccati IsNot Nothing Then
            For Each voce As JToken In bloccati
                Dim id As String = Testo(voce("id")).Trim()
                Dim impronta As String = Testo(voce("hash")).Trim().ToLowerInvariant()
                Dim motivo As String = Testo(voce("motivo"))
                If id <> "" OrElse impronta <> "" Then voci.Add(Tuple.Create(id, impronta, motivo))
            Next
        End If
        SyncLock _blocco
            _voci = voci
            _numero = numero
        End SyncLock
        Return numero
    End Function

    ''' <summary>Vero se il plugin (per nome del file o per impronta del file) è nell'elenco dei bloccati.</summary>
    Friend Shared Function Bloccato(id As String, improntaFile As String) As Boolean
        SyncLock _blocco
            For Each voce As Tuple(Of String, String, String) In _voci
                If voce.Item2 <> "" Then
                    If String.Equals(voce.Item2, improntaFile, StringComparison.OrdinalIgnoreCase) Then Return True
                ElseIf String.Equals(voce.Item1, id, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next
        End SyncLock
        Return False
    End Function

    Private Shared Async Function ScaricaAsync(url As String) As Task(Of Byte())
        ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
        Using cliente As New HttpClient()
            cliente.Timeout = TimeSpan.FromSeconds(10)
            cliente.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0")
            cliente.DefaultRequestHeaders.CacheControl = New System.Net.Http.Headers.CacheControlHeaderValue() With {.NoCache = True}
            Using risposta As HttpResponseMessage = Await cliente.GetAsync(url)
                If Not risposta.IsSuccessStatusCode Then Return Nothing
                Dim dati As Byte() = Await risposta.Content.ReadAsByteArrayAsync()
                If dati Is Nothing OrElse dati.Length > MassimoByte Then Return Nothing
                Return dati
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Scarica l'elenco, ne controlla la firma e il numero, lo salva e lo rende valido. Vero se è arrivato un elenco nuovo. Non
    ''' dice niente all'utente: se non riesce, resta quello che c'era.
    ''' </summary>
    Friend Shared Async Function AggiornaAsync() As Task(Of Boolean)
        Try
            Dim dati As Byte() = Await ScaricaAsync(UrlLista)
            Dim firmaDati As Byte() = Await ScaricaAsync(UrlLista & ".firma")
            If dati Is Nothing OrElse firmaDati Is Nothing Then Return False
            Dim firma As String = Encoding.UTF8.GetString(firmaDati).Trim()
            If Not WGGestorePlugin.VerificaFirma(dati, firma) Then
                log.ScriviLog("RevochePlugin.AggiornaAsync: firma non valida, elenco scartato")
                Return False
            End If
            Dim nuovoNumero As Long = JObject.Parse(Encoding.UTF8.GetString(dati))("numero").Value(Of Long)()
            Dim attuale As Long
            SyncLock _blocco
                attuale = _numero
            End SyncLock
            If nuovoNumero <= attuale Then Return False
            Dim percorso As String = PercorsoLista()
            File.WriteAllBytes(percorso, dati)
            File.WriteAllText(percorso & ".firma", firma, New UTF8Encoding(False))
            Applica(dati)
            Return True
        Catch ex As Exception
            log.ScriviLog("RevochePlugin.AggiornaAsync: " & ex.Message)
            Return False
        End Try
    End Function

End Class

''' <summary>
''' L'avvio sicuro. Mentre i plugin si caricano (e per i 20 secondi dopo) una spia resta accesa nella configurazione, con il nome del
''' plugin che si sta caricando. Se WinGabriel si chiude male in quel tempo, la spia resta accesa: due avvii falliti di fila e il
''' plugin che si stava caricando viene disattivato; se non si sa quale, per quell'avvio non si carica nessun plugin. Con una
''' chiusura normale la spia si spegne.
''' </summary>
Friend NotInheritable Class AvvioSicuroPlugin

    Private Const ChiaveInCorso As String = "plugin_avvio_in_corso"
    Private Const ChiaveId As String = "plugin_avvio_id"
    Private Const ChiaveFalliti As String = "plugin_avvii_falliti"

    Private Shared ReadOnly log As New wgcontrols.Logger()
    Private Shared _timer As System.Threading.Timer

    Private Sub New()
    End Sub

    Private Shared Function Leggi(chiave As String) As String
        Return If((New WGConfig()).LeggiValore(chiave), "")
    End Function

    Private Shared Sub Salva(chiave As String, valore As String)
        Dim configurazione As New WGConfig()
        configurazione.SalvaValore(chiave, valore)
    End Sub

    ''' <summary>
    ''' All'inizio dell'avvio. Restituisce se vanno saltati tutti i plugin e, se si sa quale è il colpevole, il suo identificatore
    ''' (che va disattivato).
    ''' </summary>
    Friend Shared Sub Inizia(ByRef saltaTutti As Boolean, ByRef colpevole As String)
        saltaTutti = False
        colpevole = ""
        Try
            Dim eraInCorso As Boolean = Leggi(ChiaveInCorso) = "1"
            Dim falliti As Integer = 0
            Integer.TryParse(Leggi(ChiaveFalliti), falliti)
            If eraInCorso Then
                falliti += 1
                If falliti >= 2 Then
                    colpevole = Leggi(ChiaveId)
                    saltaTutti = (colpevole = "")
                    falliti = 0
                End If
            Else
                falliti = 0
            End If
            Salva(ChiaveFalliti, falliti.ToString())
            Salva(ChiaveId, "")
            Salva(ChiaveInCorso, "1")
        Catch ex As Exception
            log.ScriviLog("AvvioSicuroPlugin.Inizia: " & ex.Message)
        End Try
    End Sub

    Friend Shared Sub Caricando(id As String)
        Try
            Salva(ChiaveId, If(id, ""))
        Catch ex As Exception
            log.ScriviLog("AvvioSicuroPlugin.Caricando: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Alla fine del caricamento: tra 20 secondi, se WinGabriel è ancora vivo, l'avvio si considera riuscito.</summary>
    Friend Shared Sub Finito()
        Try
            Salva(ChiaveId, "")
            _timer = New System.Threading.Timer(Sub(stato As Object) Spegni(), Nothing, 20000, System.Threading.Timeout.Infinite)
        Catch ex As Exception
            log.ScriviLog("AvvioSicuroPlugin.Finito: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Avvio riuscito o chiusura normale: la spia si spegne.</summary>
    Friend Shared Sub Spegni()
        Try
            Salva(ChiaveInCorso, "")
            Salva(ChiaveId, "")
            Salva(ChiaveFalliti, "0")
        Catch ex As Exception
            log.ScriviLog("AvvioSicuroPlugin.Spegni: " & ex.Message)
        End Try
    End Sub

End Class
