Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb
Imports WGPlugin

''' <summary>Quello che si sa di un file di plugin: dove sta, se è firmato, a che punto è.</summary>
Friend NotInheritable Class InfoPlugin
    Public File As String = ""
    ''' <summary>Il nome del file senza estensione: lo identifica nelle impostazioni.</summary>
    Public Id As String = ""
    ''' <summary>L'impronta del plugin insieme alle sue librerie: è quella a cui si lega il consenso dell'utente.</summary>
    Public Hash As String = ""
    ''' <summary>L'impronta del solo file del plugin: è quella che si firma e che l'elenco dei bloccati può nominare.</summary>
    Public HashFile As String = ""
    Public Firmato As Boolean
    ''' <summary>Se non è vuoto, il codice del plugin usa cose non consentite e il plugin non si carica (il motivo va nel registro).</summary>
    Public MotivoBlocco As String = ""
    ''' <summary>Vero se il plugin è nell'elenco dei bloccati.</summary>
    Public Revocato As Boolean
    Public Nome As String = ""
    Public Descrizione As String = ""
    Public Autore As String = ""
    Public Versione As String = ""
    ''' <summary>Vero se è stato creato e avviato e in questo momento è attivo.</summary>
    Public Caricato As Boolean
    ''' <summary>Vero se in questa esecuzione si è provato a caricarlo.</summary>
    Public Tentato As Boolean
    Public Istanze As New List(Of WGPluginBase)()
    Public Servizi As New List(Of ServiziPlugin)()
End Class

''' <summary>Una voce di menu aggiunta da un plugin.</summary>
Friend NotInheritable Class VocePlugin
    Public Proprietario As String = ""
    Public Testo As String = ""
    Public Azione As Action
    ''' <summary>Vero se la voce è già nel menu principale aperto.</summary>
    Public Applicata As Boolean
End Class

''' <summary>Quello che un plugin vuole mettere in un menu di WinGabriel ogni volta che quel menu si apre.</summary>
Friend NotInheritable Class AggancioMenu
    Public Servizi As ServiziPlugin
    Public Proprietario As String = ""
    Public Titolo As String = ""
    Public Testo As String = ""
    Public Dopo As String = ""
    Public Opzione As Boolean
    Public Azione As Action
    ''' <summary>Se c'è, è lei a modificare il menu (invece di aggiungere una voce).</summary>
    Public Callback As Action(Of IWGMenuPlugin)
End Class

''' <summary>
''' Il sistema dei plugin: trova i file nella cartella dei plugin, controlla la firma, chiede il permesso per quelli non firmati,
''' li crea e li avvia, e offre il menu per attivarli, disattivarli e disinstallarli. Attivare, disattivare e disinstallare
''' hanno effetto subito: le voci di menu compaiono e spariscono senza riaprire WinGabriel.
'''
'''   Cartella: la sottocartella "plugin" della radice dell'applicazione. Ogni plugin è un file .dll in quella cartella; le librerie
'''             di cui ha bisogno stanno in plugin\librerie.
'''   Firma:    accanto a nome.dll c'è nome.dll.firma, con la firma RSA SHA-256 del file (in base 64) fatta con la chiave di WinGabriel.
'''             I plugin firmati partono senza domande. Gli altri partono solo dopo che l'utente ha risposto sì all'avviso: la risposta
'''             vale per quel file, se il file cambia si chiede di nuovo.
'''   Versione: l'interfaccia è sempre la 1.0; si caricano solo i plugin compilati con WGPlugin.dll 1.0.
'''   Sicurezza: un plugin gira dentro WinGabriel con gli stessi permessi dell'utente. L'avvio con /senzaplugin non carica niente.
''' </summary>
Friend NotInheritable Class WGGestorePlugin

    Private Shared ReadOnly log As New wgcontrols.Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _plugin As New List(Of InfoPlugin)()
    Private Shared ReadOnly _vociMenu As New List(Of VocePlugin)()
    Private Shared ReadOnly _vociAltre As New List(Of VocePlugin)()
    Private Shared ReadOnly _agganci As New List(Of AggancioMenu)()
    Private Shared ReadOnly _menuAperti As New SortedSet(Of String)(StringComparer.CurrentCultureIgnoreCase)
    Private Shared ReadOnly _messaggiAvvio As New List(Of String)()
    Private Shared ReadOnly _assemblee As New Dictionary(Of String, Assembly)()
    Private Shared ReadOnly _proprietariAssembly As New Dictionary(Of Assembly, InfoPlugin)()
    Private Shared _timerRevoche As System.Threading.Timer
    Private Const MassimoAvviaMs As Integer = 5000
    Private Const MassimoAgganciPerPlugin As Integer = 40
    Private Shared _resolverAgganciato As Boolean
    Private Shared _caricamentoFatto As Boolean
    Private Shared _senzaPlugin As Boolean

    ''' <summary>Impostati dal menu principale, quando esiste: aggiungono e tolgono una voce dal menu principale aperto.</summary>
    Friend Shared AggiungiAlMenuPrincipale As Action(Of String, Action)
    Friend Shared RimuoviDalMenuPrincipale As Action(Of String)

    Private Sub New()
    End Sub

#Region "File e firma"

    Friend Shared Function CartellaPlugin() As String
        Try
            Return WGPercorsiApplicazione.CartellaComponente("plugin")
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.CartellaPlugin: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ImprontaDi(dati As Byte()) As String
        Using sha As SHA256 = SHA256.Create()
            Dim impronta As Byte() = sha.ComputeHash(dati)
            Dim testo As New StringBuilder(impronta.Length * 2)
            For Each b As Byte In impronta
                testo.Append(b.ToString("x2"))
            Next
            Return testo.ToString()
        End Using
    End Function

    ''' <summary>
    ''' Vero se accanto al file c'è una firma valida, fatta con la chiave di WinGabriel. Il controllo lo fa RilascioFirmato, lo stesso
    ''' che verifica i rilasci: la chiave pubblica sta in un solo posto.
    ''' </summary>
    Private Shared Function FirmaValida(percorso As String, dati As Byte()) As Boolean
        Try
            Dim fileFirma As String = percorso & ".firma"
            If Not File.Exists(fileFirma) Then Return False
            Return VerificaFirma(dati, File.ReadAllText(fileFirma))
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.FirmaValida: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Vero se la firma (base 64) è quella di WinGabriel per questi dati. Serve anche all'elenco dei plugin bloccati.</summary>
    Friend Shared Function VerificaFirma(dati As Byte(), firmaBase64 As String) As Boolean
        Try
            Dim firma As String = If(firmaBase64, "").Trim()
            If firma = "" OrElse dati Is Nothing Then Return False
            Dim metodo As MethodInfo = GetType(RilascioFirmato).GetMethod("FirmaValida", BindingFlags.NonPublic Or BindingFlags.Static)
            If metodo Is Nothing Then Return False
            Return Convert.ToBoolean(metodo.Invoke(Nothing, New Object() {dati, firma}))
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.VerificaFirma: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function CartellaLibrerie() As String
        Return Path.Combine(CartellaPlugin(), "librerie")
    End Function

    ''' <summary>I nomi delle librerie dei plugin (i file in plugin\librerie, senza estensione).</summary>
    Private Shared Function NomiLibrerie() As List(Of String)
        Dim nomi As New List(Of String)()
        Try
            Dim cartella As String = CartellaLibrerie()
            If Directory.Exists(cartella) Then
                For Each percorso As String In Directory.GetFiles(cartella, "*.dll")
                    nomi.Add(Path.GetFileNameWithoutExtension(percorso))
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.NomiLibrerie: " & ex.Message)
        End Try
        Return nomi
    End Function

    ''' <summary>Un'impronta sola per tutte le librerie dei plugin: se una cambia, cambia anche l'impronta di chi le può usare.</summary>
    Private Shared Function ImprontaLibrerie() As String
        Try
            Dim cartella As String = CartellaLibrerie()
            If Not Directory.Exists(cartella) Then Return ""
            Dim file As String() = Directory.GetFiles(cartella, "*.dll")
            Array.Sort(file, StringComparer.OrdinalIgnoreCase)
            Dim testo As New StringBuilder()
            For Each percorso As String In file
                testo.Append(Path.GetFileName(percorso).ToLowerInvariant()).Append(":").Append(ImprontaDi(System.IO.File.ReadAllBytes(percorso))).Append(";")
            Next
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ImprontaLibrerie: " & ex.Message)
            Return "errore"
        End Try
    End Function

    ''' <summary>I file dei plugin della cartella, con firma e impronta, senza caricarli.</summary>
    Private Shared Function Elenca() As List(Of InfoPlugin)
        Dim elenco As New List(Of InfoPlugin)()
        Try
            Dim cartella As String = CartellaPlugin()
            If cartella = "" OrElse Not Directory.Exists(cartella) Then Return elenco
            Dim librerie As String = ImprontaLibrerie()
            Dim nomiDelleLibrerie As List(Of String) = NomiLibrerie()
            For Each percorso As String In Directory.GetFiles(cartella, "*.dll")
                Try
                    Dim dati As Byte() = System.IO.File.ReadAllBytes(percorso)
                    Dim info As New InfoPlugin()
                    info.File = percorso
                    info.Id = Path.GetFileNameWithoutExtension(percorso)
                    info.Nome = info.Id
                    info.HashFile = ImprontaDi(dati)
                    info.Hash = ImprontaDi(Encoding.UTF8.GetBytes(info.HashFile & "|" & librerie))
                    info.Firmato = FirmaValida(percorso, dati)
                    info.Revocato = RevochePlugin.Bloccato(info.Id, info.HashFile)
                    ' il codice si controlla prima di qualsiasi altra cosa, anche per i plugin firmati
                    Dim motivi As New List(Of String)()
                    If Not VerificaCodicePlugin.Controlla(dati, nomiDelleLibrerie, motivi) Then
                        info.MotivoBlocco = String.Join("; ", motivi)
                        log.ScriviLog("WGGestorePlugin: il plugin " & info.Id & " non passa il controllo del codice: " & info.MotivoBlocco)
                    End If
                    elenco.Add(info)
                Catch exFile As Exception
                    log.ScriviLog("WGGestorePlugin.Elenca (" & percorso & "): " & exFile.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Elenca: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>
    ''' Mette in _plugin i file nuovi della cartella (arrivati dopo l'avvio) e toglie quelli spariti. Un plugin già attivo resta com'è anche se
    ''' il suo file cambia: quello in funzione è sempre quello letto all'attivazione.
    ''' </summary>
    Private Shared Sub AggiornaElenco()
        Try
            Dim trovati As List(Of InfoPlugin) = Elenca()
            SyncLock _blocco
                For Each nuovo As InfoPlugin In trovati
                    Dim presente As InfoPlugin = _plugin.FirstOrDefault(Function(p) p.Id = nuovo.Id)
                    If presente Is Nothing Then
                        _plugin.Add(nuovo)
                    ElseIf (presente.Hash <> nuovo.Hash OrElse presente.Revocato <> nuovo.Revocato) AndAlso Not presente.Caricato Then
                        _plugin(_plugin.IndexOf(presente)) = nuovo
                    End If
                Next
                For indice As Integer = _plugin.Count - 1 To 0 Step -1
                    Dim voce As InfoPlugin = _plugin(indice)
                    If Not voce.Caricato AndAlso Not File.Exists(voce.File) Then _plugin.RemoveAt(indice)
                Next
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AggiornaElenco: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Scelte dell'utente (nella configurazione)"

    Private Shared Function ChiaveSpento(id As String) As String
        Return "plugin_spento_" & id
    End Function

    Private Shared Function ChiaveConsenso(id As String) As String
        Return "plugin_consenso_" & id
    End Function

    Private Shared Function Leggi(chiave As String) As String
        Try
            Return If((New WGConfig()).LeggiValore(chiave), "")
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Leggi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub Salva(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore(chiave, valore)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Salva: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Togli(chiave As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.EliminaValore(chiave)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Togli: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Spento(info As InfoPlugin) As Boolean
        Return Leggi(ChiaveSpento(info.Id)) = "1"
    End Function

    ''' <summary>"si", "no" oppure "" (mai deciso) per questo file, con questo contenuto: se il file cambia la risposta non vale più.</summary>
    Private Shared Function Consenso(info As InfoPlugin) As String
        Dim valore As String = Leggi(ChiaveConsenso(info.Id))
        If valore.StartsWith("si:") AndAlso valore.Substring(3) = info.Hash Then Return "si"
        If valore.StartsWith("no:") AndAlso valore.Substring(3) = info.Hash Then Return "no"
        Return ""
    End Function

    ''' <summary>Vero se il plugin può partire: non è spento e, se non è firmato, l'utente ha risposto sì.</summary>
    Private Shared Function Permesso(info As InfoPlugin) As Boolean
        If Spento(info) OrElse info.Revocato OrElse info.MotivoBlocco <> "" Then Return False
        Return info.Firmato OrElse Consenso(info) = "si"
    End Function

#End Region

#Region "Caricamento, attivazione, disattivazione"

    ''' <summary>Si chiama una volta, all'avvio, mentre si costruisce il menu principale.</summary>
    Friend Shared Sub CaricaTutti()
        Try
            SyncLock _blocco
                If _caricamentoFatto Then Return
                _caricamentoFatto = True
            End SyncLock
            For Each argomento As String In Environment.GetCommandLineArgs()
                If String.Equals(argomento, "/senzaplugin", StringComparison.OrdinalIgnoreCase) Then _senzaPlugin = True
            Next
            If _senzaPlugin Then Return
            AggancaRisoluzioneLibrerie()
            RevochePlugin.CaricaDaCopia()
            ' avvio sicuro: se l'ultima volta WinGabriel si è chiuso male mentre caricava i plugin (due volte di fila) si toglie di
            ' mezzo il colpevole, o per questa volta tutti
            Dim saltaTutti As Boolean = False
            Dim colpevole As String = ""
            AvvioSicuroPlugin.Inizia(saltaTutti, colpevole)
            If colpevole <> "" Then Salva(ChiaveSpento(colpevole), "1")
            Dim elenco As List(Of InfoPlugin) = Elenca()
            For Each info As InfoPlugin In elenco
                Try
                    SyncLock _blocco
                        _plugin.Add(info)
                    End SyncLock
                    If colpevole <> "" AndAlso String.Equals(info.Id, colpevole, StringComparison.OrdinalIgnoreCase) Then
                        info.Nome = info.Id
                        AvvisaUnaVolta(info, "avviosicuro", TestiPlugin.AvvioSicuroUno)
                        Continue For
                    End If
                    If info.MotivoBlocco <> "" AndAlso Not Spento(info) Then AvvisaUnaVolta(info, "nonsicuro", TestiPlugin.PluginNonSicuro)
                    If info.Revocato AndAlso Not Spento(info) Then AvvisaUnaVolta(info, "revocato", TestiPlugin.PluginRevocato)
                    If saltaTutti Then Continue For
                    If Permesso(info) Then
                        AvvioSicuroPlugin.Caricando(info.Id)
                        Avvia(info)
                    End If
                Catch exUno As Exception
                    log.ScriviLog("WGGestorePlugin.CaricaTutti (" & info.Id & "): " & exUno.Message)
                End Try
            Next
            If saltaTutti Then
                SyncLock _blocco
                    _messaggiAvvio.Add(TestiPlugin.AvvioSicuroTutti)
                End SyncLock
            End If
            AvvioSicuroPlugin.Finito()
            AvviaControlloRevoche()
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.CaricaTutti: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Un avviso per un plugin, una volta sola per ogni versione del file (per non ripeterlo a ogni avvio).</summary>
    Private Shared Sub AvvisaUnaVolta(info As InfoPlugin, tipo As String, testo As String)
        Try
            Dim chiave As String = "plugin_avviso:" & tipo & ":" & info.Id
            If Leggi(chiave) = info.HashFile Then Return
            Salva(chiave, info.HashFile)
            SyncLock _blocco
                _messaggiAvvio.Add(TestiPlugin.Con(testo, If(info.Nome <> "", info.Nome, info.Id)))
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AvvisaUnaVolta: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Scarica l'elenco dei plugin bloccati ora e poi ogni 12 ore, in sottofondo.</summary>
    Private Shared Sub AvviaControlloRevoche()
        Try
            _timerRevoche = New System.Threading.Timer(Async Sub(stato As Object)
                                                           Try
                                                               If Await RevochePlugin.AggiornaAsync() Then ServiziPlugin.SuInterfaccia(AddressOf ApplicaRevoche)
                                                           Catch ex As Exception
                                                               log.ScriviLog("WGGestorePlugin.AvviaControlloRevoche: " & ex.Message)
                                                           End Try
                                                       End Sub, Nothing, 15000, 12 * 60 * 60 * 1000)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AvviaControlloRevoche: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dopo un nuovo elenco dei bloccati: i plugin nominati si fermano subito, anche se erano attivi.</summary>
    Private Shared Sub ApplicaRevoche()
        Try
            Dim tutti As List(Of InfoPlugin)
            SyncLock _blocco
                tutti = New List(Of InfoPlugin)(_plugin)
            End SyncLock
            For Each info As InfoPlugin In tutti
                Dim bloccato As Boolean = RevochePlugin.Bloccato(info.Id, info.HashFile)
                If bloccato = info.Revocato Then Continue For
                info.Revocato = bloccato
                If bloccato AndAlso info.Caricato Then
                    DisattivaSubito(info)
                    Messaggio(TestiPlugin.Con(TestiPlugin.RevocaElenco, info.Nome))
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ApplicaRevoche: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Un plugin che dà troppi errori, manda troppe richieste o ci mette troppo ad avviarsi: si spegne subito, si ricorda che è
    ''' spento (si riattiva dalle impostazioni) e si avvisa l'utente. Si può chiamare da qualsiasi thread.
    ''' </summary>
    Friend Shared Sub DisattivaPerProblema(id As String, testo As String)
        ServiziPlugin.SuInterfaccia(Sub()
                                        Try
                                            Dim info As InfoPlugin
                                            SyncLock _blocco
                                                info = _plugin.FirstOrDefault(Function(p) String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                                            End SyncLock
                                            If info Is Nothing Then Return
                                            Salva(ChiaveSpento(info.Id), "1")
                                            DisattivaSubito(info)
                                            Messaggio(TestiPlugin.Con(testo, info.Nome))
                                        Catch ex As Exception
                                            log.ScriviLog("WGGestorePlugin.DisattivaPerProblema: " & ex.Message)
                                        End Try
                                    End Sub)
    End Sub

    Private Shared Sub AggancaRisoluzioneLibrerie()
        Try
            If _resolverAgganciato Then Return
            _resolverAgganciato = True
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf RisolviLibreria
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AggancaRisoluzioneLibrerie: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Le librerie di un plugin si cercano solo in plugin\librerie, e solo se a chiederle è un plugin (o un'altra sua libreria). Si
    ''' caricano dai byte, dopo lo stesso controllo del codice dei plugin. Se il plugin è firmato, anche la libreria deve avere la sua
    ''' firma valida (nome.dll.firma): altrimenti basterebbe cambiare la libreria per far girare altro codice con il nome di un plugin
    ''' firmato.
    ''' </summary>
    Private Shared Function RisolviLibreria(mittente As Object, args As ResolveEventArgs) As Assembly
        Try
            Dim nome As String = New AssemblyName(args.Name).Name
            If String.IsNullOrWhiteSpace(nome) OrElse nome.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) Then Return Nothing
            Dim richiedente As InfoPlugin = Nothing
            If args.RequestingAssembly Is Nothing Then Return Nothing
            SyncLock _blocco
                _proprietariAssembly.TryGetValue(args.RequestingAssembly, richiedente)
            End SyncLock
            If richiedente Is Nothing Then Return Nothing
            Dim percorso As String = Path.Combine(CartellaLibrerie(), nome & ".dll")
            If Not File.Exists(percorso) Then Return Nothing
            Dim dati As Byte() = File.ReadAllBytes(percorso)
            Dim motivi As New List(Of String)()
            If Not VerificaCodicePlugin.Controlla(dati, NomiLibrerie(), motivi) Then
                log.ScriviLog("WGGestorePlugin.RisolviLibreria: la libreria " & nome & " non passa il controllo del codice: " & String.Join("; ", motivi))
                Return Nothing
            End If
            If richiedente.Firmato AndAlso Not FirmaValida(percorso, dati) Then
                log.ScriviLog("WGGestorePlugin.RisolviLibreria: la libreria " & nome & " non è firmata e il plugin " & richiedente.Id & " sì")
                Return Nothing
            End If
            Dim assemblea As Assembly = Assembly.Load(dati)
            SyncLock _blocco
                _proprietariAssembly(assemblea) = richiedente
            End SyncLock
            Return assemblea
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.RisolviLibreria: " & ex.Message)
        End Try
        Return Nothing
    End Function

    ''' <summary>Vero se il plugin è stato compilato con la 1.0 di WGPlugin.dll (cioè con l'unica versione che esiste).</summary>
    Private Shared Function VersioneCompatibile(assemblea As Assembly) As Boolean
        Try
            For Each riferita As AssemblyName In assemblea.GetReferencedAssemblies()
                If String.Equals(riferita.Name, "WGPlugin", StringComparison.OrdinalIgnoreCase) Then
                    Dim versione As Version = riferita.Version
                    Dim attesa As Version = New Version(WGPluginApi.Versione & ".0.0")
                    Return versione IsNot Nothing AndAlso versione.Major = attesa.Major AndAlso versione.Minor = attesa.Minor
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.VersioneCompatibile: " & ex.Message)
        End Try
        Return False
    End Function

    ''' <summary>Crea e avvia le classi del plugin. Si può chiamare anche a WinGabriel già in funzione: le voci compaiono subito.</summary>
    Private Shared Sub Avvia(info As InfoPlugin)
        Try
            info.Tentato = True
            ' a ogni avvio il file si rilegge e si ricontrolla: impronta, elenco dei bloccati e codice, sugli stessi byte che si caricano
            Dim dati As Byte() = File.ReadAllBytes(info.File)
            If ImprontaDi(dati) <> info.HashFile Then
                SyncLock _blocco
                    _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.ErroreAvvio, info.Id))
                End SyncLock
                Return
            End If
            If info.Revocato OrElse RevochePlugin.Bloccato(info.Id, info.HashFile) Then
                info.Revocato = True
                SyncLock _blocco
                    _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.PluginRevocato, info.Nome))
                End SyncLock
                Return
            End If
            Dim motiviCodice As New List(Of String)()
            If info.MotivoBlocco <> "" OrElse Not VerificaCodicePlugin.Controlla(dati, NomiLibrerie(), motiviCodice) Then
                If info.MotivoBlocco = "" Then info.MotivoBlocco = String.Join("; ", motiviCodice)
                SyncLock _blocco
                    _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.PluginNonSicuro, info.Nome))
                End SyncLock
                Return
            End If
            ' Si carica dai byte letti ora (gli stessi controllati per firma e permesso); una volta caricato, lo stesso contenuto si riusa.
            Dim chiave As String = info.Id & ":" & info.Hash
            Dim assemblea As Assembly = Nothing
            SyncLock _blocco
                _assemblee.TryGetValue(chiave, assemblea)
            End SyncLock
            If assemblea Is Nothing Then
                assemblea = Assembly.Load(dati)
                SyncLock _blocco
                    _assemblee(chiave) = assemblea
                    _proprietariAssembly(assemblea) = info
                End SyncLock
            End If
            If Not VersioneCompatibile(assemblea) Then
                SyncLock _blocco
                    _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.VersioneNonCompatibile, info.Id))
                End SyncLock
                Return
            End If
            Dim tipi As Type()
            Try
                tipi = assemblea.GetTypes()
            Catch exTipi As ReflectionTypeLoadException
                tipi = exTipi.Types.Where(Function(t) t IsNot Nothing).ToArray()
            End Try
            For Each tipo As Type In tipi
                If Not tipo.IsPublic OrElse tipo.IsAbstract OrElse Not GetType(WGPluginBase).IsAssignableFrom(tipo) Then Continue For
                If tipo.GetConstructor(Type.EmptyTypes) Is Nothing Then Continue For
                AvviaIstanza(info, tipo)
            Next
            info.Caricato = info.Istanze.Count > 0
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Avvia (" & info.Id & "): " & ex.ToString())
            SyncLock _blocco
                _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.ErroreAvvio, info.Id))
            End SyncLock
        End Try
    End Sub

    Private Shared Sub AvviaIstanza(info As InfoPlugin, tipo As Type)
        Dim nome As String = info.Id
        Dim servizi As New ServiziPlugin(info.Id, info.Firmato)
        Try
            Dim plugin As WGPluginBase = CType(Activator.CreateInstance(tipo), WGPluginBase)
            Try
                nome = If(plugin.Nome, info.Id)
                servizi.NomeVisibile = nome
                info.Nome = nome
                info.Descrizione = If(plugin.Descrizione, "")
                info.Autore = If(plugin.Autore, "")
                info.Versione = If(plugin.Versione, "")
            Catch exDati As Exception
                log.ScriviLog("WGGestorePlugin.AvviaIstanza (dati di " & info.Id & "): " & exDati.Message)
            End Try
            info.Servizi.Add(servizi)
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            plugin.Avvia(servizi)
            info.Istanze.Add(plugin)
            ' ricorda com'era il plugin (firmato o no, con quale impronta): serve a capire se è cambiato
            Salva("plugin_noto:" & info.Id, If(info.Firmato, "1", "0") & "|" & info.Hash)
            If orologio.ElapsedMilliseconds > MassimoAvviaMs Then
                log.ScriviLog("WGGestorePlugin.AvviaIstanza: " & info.Id & " ha impiegato " & orologio.ElapsedMilliseconds.ToString() & " ms per avviarsi")
                DisattivaPerProblema(info.Id, TestiPlugin.NonPartitoInTempo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AvviaIstanza (" & info.Id & "): " & ex.ToString())
            servizi.Disattiva()
            RimuoviVociDi(info.Id)
            SyncLock _blocco
                _messaggiAvvio.Add(TestiPlugin.Con(TestiPlugin.ErroreAvvio, nome))
            End SyncLock
        End Try
    End Sub

    ''' <summary>Avvia il plugin subito, a WinGabriel già in funzione.</summary>
    Private Shared Sub AttivaSubito(info As InfoPlugin)
        Try
            If info.Caricato Then Return
            If _senzaPlugin Then Return
            AggancaRisoluzioneLibrerie()
            info.Istanze.Clear()
            info.Servizi.Clear()
            Avvia(info)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AttivaSubito: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Ferma il plugin subito: Arresta, voci di menu tolte, servizi spenti.</summary>
    Private Shared Sub DisattivaSubito(info As InfoPlugin)
        Try
            For Each plugin As WGPluginBase In info.Istanze
                Try
                    plugin.Arresta()
                Catch exArresta As Exception
                    log.ScriviLog("WGGestorePlugin.DisattivaSubito (Arresta di " & info.Id & "): " & exArresta.Message)
                End Try
            Next
            For Each servizi As ServiziPlugin In info.Servizi
                servizi.Disattiva()
            Next
            RimuoviVociDi(info.Id)
            info.Istanze.Clear()
            info.Servizi.Clear()
            info.Caricato = False
            info.Tentato = False
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.DisattivaSubito: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Si chiama alla chiusura di WinGabriel.</summary>
    Friend Shared Sub ArrestaTutti()
        Try
            ' chiusura normale: la spia dell'avvio sicuro si spegne
            If Not _senzaPlugin Then AvvioSicuroPlugin.Spegni()
            For Each info As InfoPlugin In _plugin.ToList()
                For Each plugin As WGPluginBase In info.Istanze
                    Try
                        plugin.Arresta()
                    Catch ex As Exception
                        log.ScriviLog("WGGestorePlugin.ArrestaTutti (" & info.Id & "): " & ex.Message)
                    End Try
                Next
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ArrestaTutti: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Voci di menu dei plugin"

    Friend Shared Sub AggiungiVoceMenuPrincipale(proprietario As String, testo As String, azione As Action)
        Try
            Dim voce As New VocePlugin() With {.Proprietario = proprietario, .Testo = testo, .Azione = azione}
            SyncLock _blocco
                ' un plugin non può riempire il menu principale
                If _vociMenu.Where(Function(v) v.Proprietario = proprietario).Count() >= 5 Then Return
                _vociMenu.Add(voce)
            End SyncLock
            ApplicaVoce(voce)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AggiungiVoceMenuPrincipale: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ApplicaVoce(voce As VocePlugin)
        Try
            Dim aggiungi As Action(Of String, Action) = AggiungiAlMenuPrincipale
            If aggiungi Is Nothing OrElse voce.Applicata Then Return
            aggiungi.Invoke(voce.Testo, voce.Azione)
            voce.Applicata = True
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ApplicaVoce: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dal menu principale appena costruito: mette dentro le voci dei plugin già avviati.</summary>
    Friend Shared Sub ApplicaVociMenuPrincipale()
        Try
            Dim voci As List(Of VocePlugin)
            SyncLock _blocco
                voci = New List(Of VocePlugin)(_vociMenu)
            End SyncLock
            For Each voce As VocePlugin In voci
                voce.Applicata = False
                ApplicaVoce(voce)
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ApplicaVociMenuPrincipale: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RimuoviVociDi(proprietario As String)
        Try
            Dim daTogliere As List(Of VocePlugin)
            SyncLock _blocco
                daTogliere = _vociMenu.Where(Function(v) v.Proprietario = proprietario).ToList()
                _vociMenu.RemoveAll(Function(v) v.Proprietario = proprietario)
                _vociAltre.RemoveAll(Function(v) v.Proprietario = proprietario)
                _agganci.RemoveAll(Function(a) a.Proprietario = proprietario)
            End SyncLock
            Dim rimuovi As Action(Of String) = RimuoviDalMenuPrincipale
            If rimuovi Is Nothing Then Return
            For Each voce As VocePlugin In daTogliere
                Try
                    If voce.Applicata Then rimuovi.Invoke(voce.Testo)
                Catch exVoce As Exception
                    log.ScriviLog("WGGestorePlugin.RimuoviVociDi (voce): " & exVoce.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.RimuoviVociDi: " & ex.Message)
        End Try
    End Sub

    ' --- Qualsiasi menu di WinGabriel -----------------------------------------------------------------------------------------
    ' WGMenu.AllApertura chiama AllAperturaMenu per ogni menu che si apre: così ogni menu, anche uno scritto domani, è già un punto
    ' d'innesto riconosciuto dal titolo, senza elenchi da tenere aggiornati.

    Private Const TitoloMenuPrincipale As String = "Menu principale"

    ''' <summary>Il titolo come lo scrive il plugin, riportato al titolo vero: il menu iniziale ha due titoli (classica e smart).</summary>
    Friend Shared Function NormalizzaTitoloMenu(titolo As String) As String
        Dim t As String = If(titolo, "").Trim()
        For Each alias1 As String In New String() {"Menu applicazioni", "Menù iniziale", "Menu iniziale", "Menù principale", TitoloMenuPrincipale}
            If String.Equals(t, alias1, StringComparison.CurrentCultureIgnoreCase) Then Return TitoloMenuPrincipale
        Next
        Return t
    End Function

    Friend Shared Sub AggiungiAggancioMenu(aggancio As AggancioMenu)
        Try
            aggancio.Titolo = NormalizzaTitoloMenu(aggancio.Titolo)
            ' Il menu principale è costruito una volta sola: la sua voce si mette subito, con il meccanismo dal vivo.
            If aggancio.Callback Is Nothing AndAlso Not aggancio.Opzione AndAlso aggancio.Titolo = TitoloMenuPrincipale Then
                AggiungiVoceMenuPrincipale(aggancio.Proprietario, aggancio.Testo, aggancio.Azione)
                Return
            End If
            SyncLock _blocco
                ' un plugin non può mettere decine di voci nei menu
                If _agganci.Where(Function(a) a.Proprietario = aggancio.Proprietario).Count() >= MassimoAgganciPerPlugin Then Return
                _agganci.Add(aggancio)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AggiungiAggancioMenu: " & ex.Message)
        End Try
    End Sub

    Friend Shared Function TitoliMenuAperti() As List(Of String)
        SyncLock _blocco
            Return _menuAperti.ToList()
        End SyncLock
    End Function

    ''' <summary>Chiamato da WGMenu.AllApertura per ogni menu che sta per aprirsi: ricorda il titolo e applica quello che i plugin vogliono metterci.</summary>
    Friend Shared Sub AllAperturaMenu(menu As WGMenu, titolo As String)
        Try
            Dim normalizzato As String = NormalizzaTitoloMenu(titolo)
            If normalizzato = "" Then Return
            Dim daApplicare As List(Of AggancioMenu)
            SyncLock _blocco
                _menuAperti.Add(normalizzato)
                daApplicare = _agganci.Where(Function(a) String.Equals(a.Titolo, normalizzato, StringComparison.CurrentCultureIgnoreCase)).ToList()
            End SyncLock
            For Each aggancio As AggancioMenu In daApplicare
                Try
                    If Not aggancio.Servizi.Attivo Then Continue For
                    If aggancio.Callback IsNot Nothing Then
                        aggancio.Callback.Invoke(New MenuEsistenteImpl(aggancio.Servizi, menu))
                    ElseIf aggancio.Opzione Then
                        menu.aggiungiOpzione(aggancio.Testo, Sub(mittente As Object, e As EventArgs) aggancio.Azione.Invoke())
                        aggancio.Servizi.RicordaVoceInMenu(menu, aggancio.Testo, True)
                    ElseIf aggancio.Dopo <> "" Then
                        menu.AggiungiDopo(aggancio.Dopo, aggancio.Testo, Sub(mittente As Object, e As EventArgs) aggancio.Azione.Invoke())
                        aggancio.Servizi.RicordaVoceInMenu(menu, aggancio.Testo, False)
                    Else
                        menu.aggiungi(aggancio.Testo, Sub(mittente As Object, e As EventArgs) aggancio.Azione.Invoke())
                        aggancio.Servizi.RicordaVoceInMenu(menu, aggancio.Testo, False)
                    End If
                Catch exAggancio As Exception
                    ' L'errore di un plugin non deve impedire al menu di aprirsi.
                    aggancio.Servizi.ErrorePlugin("WGGestorePlugin.AllAperturaMenu (" & normalizzato & ")", exAggancio)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.AllAperturaMenu: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Domande e messaggi"

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Messaggio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' La domanda per un plugin non firmato: una se è nuovo, un'altra se è cambiato dall'ultima volta che è partito, una più
    ''' seria se prima era firmato da WinGabriel e ora non lo è più.
    ''' </summary>
    Private Shared Function TestoDomanda(info As InfoPlugin) As String
        Dim noto As String = Leggi("plugin_noto:" & info.Id)
        If noto = "" Then Return TestiPlugin.DomandaNonFirmato
        If noto.StartsWith("1|", StringComparison.Ordinal) AndAlso Not info.Firmato Then Return TestiPlugin.DomandaPerdeFirma
        Return TestiPlugin.DomandaAggiornato
    End Function

    ''' <summary>I problemi dei plugin che non sono partiti: si dicono con un messaggio e si cancellano.</summary>
    Private Shared Sub MostraProblemi()
        Try
            Dim problemi As List(Of String)
            SyncLock _blocco
                problemi = New List(Of String)(_messaggiAvvio)
                _messaggiAvvio.Clear()
            End SyncLock
            For Each testo As String In problemi
                Messaggio(testo)
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.MostraProblemi: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Dopo le domande dell'avvio: i problemi dei plugin che non sono partiti e, per ogni plugin non firmato mai deciso, l'avviso e la
    ''' domanda. La risposta vale per quel file. Con sì il plugin parte subito.
    ''' </summary>
    Friend Shared Async Function ChiediConsensiAsync() As Task
        Try
            MostraProblemi()
            If _senzaPlugin Then Return
            For Each info As InfoPlugin In _plugin.ToList()
                If info.Firmato OrElse Spento(info) OrElse Consenso(info) <> "" OrElse info.Revocato OrElse info.MotivoBlocco <> "" Then Continue For
                Dim attivare As Boolean = Await WGDomanda.ChiediAsync(TestiPlugin.Con(TestoDomanda(info), info.Id))
                If attivare Then
                    Salva(ChiaveConsenso(info.Id), "si:" & info.Hash)
                    AttivaSubito(info)
                    If info.Caricato Then Messaggio(TestiPlugin.Con(TestiPlugin.PluginAttivato, info.Nome)) Else MostraProblemi()
                Else
                    Salva(ChiaveConsenso(info.Id), "no:" & info.Hash)
                    Messaggio(TestiPlugin.Con(TestiPlugin.PluginRifiutato, info.Id))
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.ChiediConsensiAsync: " & ex.Message)
        End Try
    End Function

#End Region

#Region "Menu nelle impostazioni"

    Private Shared Function Stato(info As InfoPlugin) As String
        Dim parti As New List(Of String)()
        If Not info.Firmato Then parti.Add("non firmato")
        If info.Revocato Then parti.Add("bloccato")
        If info.MotivoBlocco <> "" Then parti.Add("codice non consentito")
        If info.Caricato Then
            parti.Add("attivo")
        ElseIf info.Tentato Then
            parti.Add("non partito")
        Else
            parti.Add("disattivato")
        End If
        Return String.Join(", ", parti)
    End Function

    ''' <summary>Impostazioni, voce Plugin: l'elenco dei plugin trovati. Senza plugin si dice con un messaggio e l'elenco non si apre.</summary>
    Friend Shared Sub MostraMenu()
        Try
            AggiornaElenco()
            Dim elenco As List(Of InfoPlugin)
            SyncLock _blocco
                elenco = New List(Of InfoPlugin)(_plugin)
            End SyncLock
            If elenco.Count = 0 Then
                Messaggio(TestiPlugin.NessunPlugin)
                Return
            End If
            Dim menu As New WGMenu()
            For Each info As InfoPlugin In elenco
                Dim scelto As InfoPlugin = info
                menu.aggiungi(scelto.Nome & ", " & Stato(scelto), Sub(mittente As Object, e As EventArgs) MostraMenuPlugin(scelto))
            Next
            menu.avvia("Plugin")
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.MostraMenu: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraMenuPlugin(info As InfoPlugin)
        Try
            Dim menu As New WGMenu()
            If info.Caricato Then
                menu.aggiungi("Disattiva", Sub(mittente As Object, e As EventArgs) Disattiva(info))
            Else
                menu.aggiungi("Attiva", Sub(mittente As Object, e As EventArgs) Attiva(info))
            End If
            menu.aggiungi("Permessi", Sub(mittente As Object, e As EventArgs) MostraPermessi(info))
            menu.aggiungi("Disinstalla", Sub(mittente As Object, e As EventArgs) Disinstalla(info))
            menu.aggiungi("Informazioni", Sub(mittente As Object, e As EventArgs) Informazioni(info))
            menu.avvia(info.Nome)
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.MostraMenuPlugin: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' I permessi del plugin: per ognuno si vede se è concesso. Sceglierne uno concesso chiede se revocarlo (la revoca vale subito);
    ''' uno non concesso lo concede solo il plugin, chiedendolo all'utente quando gli serve.
    ''' </summary>
    Private Shared Sub MostraPermessi(info As InfoPlugin)
        Try
            Dim concessi As New List(Of String)()
            For Each permesso As String In PermessiPlugin.Elenco
                If PermessiPlugin.Concesso(info.Id, permesso) Then concessi.Add(permesso)
            Next
            If concessi.Count = 0 Then
                Messaggio(TestiPlugin.Con(TestiPlugin.NessunPermessoPossibile, info.Nome))
                Return
            End If
            Dim menu As New WGMenu()
            For Each permesso As String In concessi
                Dim scelto As String = permesso
                menu.aggiungi(PermessiPlugin.Titolo(scelto) & ", concesso", Sub(mittente As Object, e As EventArgs) RevocaPermesso(info, scelto, menu))
            Next
            menu.avvia("Permessi")
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.MostraPermessi: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub RevocaPermesso(info As InfoPlugin, permesso As String, menu As WGMenu)
        Try
            Dim cosa As String = PermessiPlugin.Cosa(permesso)
            Dim revocare As Boolean = Await WGDomanda.ChiediAsync(TestiPlugin.Con(TestiPlugin.DomandaRevocaPermesso.Replace("{cosa}", cosa), info.Nome))
            If Not revocare Then Return
            PermessiPlugin.Revoca(info.Id, permesso)
            menu.RimuoviVoce(PermessiPlugin.Titolo(permesso) & ", concesso")
            Messaggio(TestiPlugin.Con(TestiPlugin.PermessoRevocato.Replace("{cosa}", cosa), info.Nome))
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.RevocaPermesso: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub Attiva(info As InfoPlugin)
        Try
            If info.Revocato Then
                Messaggio(TestiPlugin.Con(TestiPlugin.PluginRevocato, info.Nome))
                Return
            End If
            If info.MotivoBlocco <> "" Then
                Messaggio(TestiPlugin.Con(TestiPlugin.PluginNonSicuro, info.Nome))
                Return
            End If
            If Not info.Firmato AndAlso Consenso(info) <> "si" Then
                Dim attivare As Boolean = Await WGDomanda.ChiediAsync(TestiPlugin.Con(TestoDomanda(info), info.Nome))
                If Not attivare Then
                    Salva(ChiaveConsenso(info.Id), "no:" & info.Hash)
                    Messaggio(TestiPlugin.Con(TestiPlugin.PluginRifiutato, info.Nome))
                    Return
                End If
                Salva(ChiaveConsenso(info.Id), "si:" & info.Hash)
            End If
            Togli(ChiaveSpento(info.Id))
            AttivaSubito(info)
            If info.Caricato Then Messaggio(TestiPlugin.Con(TestiPlugin.PluginAttivato, info.Nome)) Else MostraProblemi()
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Attiva: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Disattiva(info As InfoPlugin)
        Try
            Salva(ChiaveSpento(info.Id), "1")
            DisattivaSubito(info)
            Messaggio(TestiPlugin.Con(TestiPlugin.PluginDisattivato, info.Nome))
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Disattiva: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Sub Disinstalla(info As InfoPlugin)
        Try
            Dim sicuro As Boolean = Await WGDomanda.ChiediAsync(TestiPlugin.Con(TestiPlugin.DomandaDisinstalla, info.Nome))
            If Not sicuro Then Return
            DisattivaSubito(info)
            Dim riuscito As Boolean = True
            For Each percorso As String In New String() {info.File, info.File & ".firma"}
                Try
                    If File.Exists(percorso) Then File.Delete(percorso)
                Catch exFile As Exception
                    riuscito = False
                    log.ScriviLog("WGGestorePlugin.Disinstalla (" & percorso & "): " & exFile.Message)
                End Try
            Next
            If riuscito Then
                Togli(ChiaveSpento(info.Id))
                Togli(ChiaveConsenso(info.Id))
                Togli("plugin_noto:" & info.Id)
                PermessiPlugin.RevocaTutti(info.Id)
                SyncLock _blocco
                    _plugin.Remove(info)
                End SyncLock
                Messaggio(TestiPlugin.Con(TestiPlugin.PluginDisinstallato, info.Nome))
            Else
                Messaggio(TestiPlugin.Con(TestiPlugin.DisinstallazioneFallita, info.Nome))
            End If
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Disinstalla: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Informazioni(info As InfoPlugin)
        Try
            Dim testo As New StringBuilder()
            testo.Append(info.Nome).Append(". ")
            If info.Descrizione <> "" Then testo.Append(info.Descrizione.TrimEnd("."c)).Append(". ")
            If info.Autore <> "" Then testo.Append("Autore: ").Append(info.Autore).Append(". ")
            If info.Versione <> "" Then testo.Append("Versione ").Append(info.Versione).Append(". ")
            testo.Append(If(info.Firmato, "Firmato da WinGabriel. ", "Non firmato. "))
            testo.Append("Stato: ").Append(Stato(info)).Append(".")
            Messaggio(testo.ToString())
        Catch ex As Exception
            log.ScriviLog("WGGestorePlugin.Informazioni: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
