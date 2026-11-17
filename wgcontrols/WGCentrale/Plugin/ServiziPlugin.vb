Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb
Imports WGPlugin

''' <summary>
''' I servizi di WinGabriel per un plugin: ogni plugin ne riceve un'istanza sua. Tutto quello che il plugin chiede passa da qui dentro un
''' Try/Catch: un errore del plugin finisce nel registro e non arriva a WinGabriel. Quando il plugin viene disattivato, i suoi servizi
''' smettono di rispondere (Attivo diventa falso): le azioni già registrate non fanno più niente.
''' </summary>
Partial Friend NotInheritable Class ServiziPlugin
    Implements IWGServiziPlugin

    Private Shared ReadOnly log As New wgcontrols.Logger()

    Private ReadOnly _id As String
    Private ReadOnly _firmato As Boolean
    Private _nomeVisibile As String
    Private _attivo As Boolean = True

    Public Sub New(id As String, firmato As Boolean)
        _id = id
        _firmato = firmato
        _nomeVisibile = id
    End Sub

    ''' <summary>Il nome che il plugin dà a se stesso, per i messaggi (si sa solo dopo averlo creato).</summary>
    Friend WriteOnly Property NomeVisibile As String
        Set(value As String)
            If Not String.IsNullOrWhiteSpace(value) Then _nomeVisibile = value
        End Set
    End Property

    ''' <summary>Falso dopo la disattivazione del plugin.</summary>
    Friend ReadOnly Property Attivo As Boolean
        Get
            Return _attivo
        End Get
    End Property

    Friend Sub Disattiva()
        _attivo = False
        RimuoviVociDaiMenuAperti()
    End Sub

    Private ReadOnly _vociInMenu As New List(Of Tuple(Of WeakReference, String, Boolean))()

    ''' <summary>Ricorda una voce (o opzione) messa in un menu, per toglierla subito se il plugin viene disattivato.</summary>
    Friend Sub RicordaVoceInMenu(menu As WGMenu, testo As String, opzione As Boolean)
        Try
            SyncLock _vociInMenu
                _vociInMenu.Add(Tuple.Create(New WeakReference(menu), testo, opzione))
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.RicordaVoceInMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Con il plugin disattivato, le sue voci spariscono subito anche dai menu che sono ancora aperti.</summary>
    Private Sub RimuoviVociDaiMenuAperti()
        Try
            Dim voci As List(Of Tuple(Of WeakReference, String, Boolean))
            SyncLock _vociInMenu
                voci = New List(Of Tuple(Of WeakReference, String, Boolean))(_vociInMenu)
                _vociInMenu.Clear()
            End SyncLock
            For Each voce As Tuple(Of WeakReference, String, Boolean) In voci
                Try
                    Dim menu As WGMenu = TryCast(voce.Item1.Target, WGMenu)
                    If menu Is Nothing Then Continue For
                    If voce.Item3 Then menu.RimuoviOpzione(voce.Item2) Else menu.RimuoviVoce(voce.Item2)
                Catch exVoce As Exception
                    log.ScriviLog("ServiziPlugin.RimuoviVociDaiMenuAperti (voce): " & exVoce.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.RimuoviVociDaiMenuAperti: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Id As String Implements IWGServiziPlugin.Id
        Get
            Return _id
        End Get
    End Property

    Public ReadOnly Property VersioneApi As String Implements IWGServiziPlugin.VersioneApi
        Get
            Return WGPluginApi.Versione
        End Get
    End Property

    Public ReadOnly Property Firmato As Boolean Implements IWGServiziPlugin.Firmato
        Get
            Return _firmato
        End Get
    End Property

    Public ReadOnly Property CartellaDati As String Implements IWGServiziPlugin.CartellaDati
        Get
            Try
                ' I dati dei plugin stanno nella cartella dati di WinGabriel, sotto "plugin": così si sincronizzano nel cloud e si
                ' esportano insieme agli altri dati (la cartella della configurazione, invece, non si sincronizza).
                Dim radice As String = WGDatabase.CartellaBaseDatiEffettiva()
                If String.IsNullOrWhiteSpace(radice) Then radice = WGDatabase.CartellaBaseDatiPredefinita()
                Dim cartella As String = Path.Combine(radice, "plugin", _id)
                If Not Directory.Exists(cartella) Then
                    Directory.CreateDirectory(cartella)
                    SpostaDallaVecchiaCartella(cartella)
                End If
                Return cartella
            Catch ex As Exception
                ScriviErrore("ServiziPlugin.CartellaDati", ex)
                Return ""
            End Try
        End Get
    End Property

    ''' <summary>Una volta sola: i file che il plugin aveva nella vecchia cartella (nella configurazione) passano nella nuova.</summary>
    Private Sub SpostaDallaVecchiaCartella(nuova As String)
        Try
            Dim vecchia As String = Path.Combine(WGPercorsiApplicazione.CartellaConfig(), "plugin", _id)
            If Not Directory.Exists(vecchia) Then Return
            For Each percorso As String In Directory.GetFiles(vecchia)
                Dim destinazione As String = Path.Combine(nuova, Path.GetFileName(percorso))
                If Not System.IO.File.Exists(destinazione) Then System.IO.File.Move(percorso, destinazione)
            Next
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.SpostaDallaVecchiaCartella (" & _id & "): " & ex.Message)
        End Try
    End Sub

#Region "Azioni protette"

    ''' <summary>L'azione del plugin dentro un Try/Catch: se fallisce, errore nel registro e un messaggio, e WinGabriel va avanti.</summary>
    Friend Function Protetta(azione As Action, origine As String) As Action
        If azione Is Nothing Then Return Nothing
        Return Sub()
                   Try
                       If Not _attivo Then Return
                       Dim orologio As Stopwatch = Stopwatch.StartNew()
                       azione.Invoke()
                       ' un'azione che si prende più di 10 secondi conta come un errore: non si può fermare, ma un plugin che
                       ' blocca l'interfaccia di continuo si disattiva da solo
                       If orologio.ElapsedMilliseconds > SogliaAzioneLentaMs Then
                           ScriviErrore(origine & " (azione troppo lenta)", New TimeoutException())
                           ContaErrore()
                       End If
                   Catch ex As Exception
                       ErrorePlugin(origine, ex)
                       MostraErroreAzione()
                   End Try
               End Sub
    End Function

    Friend Sub MostraErroreAzione()
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(TestiPlugin.Con(TestiPlugin.ErroreAzione, _nomeVisibile))
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.MostraErroreAzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Esegue sul thread dell'interfaccia (subito, se ci si trova già).</summary>
    Friend Shared Sub SuInterfaccia(azione As Action)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.InvokeRequired Then
                c.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.SuInterfaccia: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Voce, messaggi, domande, attese"

    Public Sub Parla(testo As String, Optional interrompi As Boolean = False) Implements IWGServiziPlugin.Parla
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(testo) OrElse Not Consenti("parla", 12) Then Return
            sintesivocale.GetInstance().LeggiTesto(testo, False, If(interrompi, "Alta", "Normale"))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.Parla", ex)
        End Try
    End Sub

    Public Sub Messaggio(testo As String) Implements IWGServiziPlugin.Messaggio
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(testo) OrElse Not Consenti("messaggi", 6) Then Return
            SuInterfaccia(Sub()
                              Try
                                  Dim m As New WGMessaggi()
                                  m.Messaggio(testo)
                                  m.ShowDialog()
                              Catch ex As Exception
                                  ScriviErrore("ServiziPlugin.Messaggio", ex)
                              End Try
                          End Sub)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.Messaggio (esterno)", ex)
        End Try
    End Sub

    Public Function ChiediAsync(domanda As String) As Task(Of Boolean) Implements IWGServiziPlugin.ChiediAsync
        Dim esito As New TaskCompletionSource(Of Boolean)()
        Try
            If Not _attivo Then
                esito.TrySetResult(False)
                Return esito.Task
            End If
            SuInterfaccia(Sub()
                              Try
                                  WGDomanda.Chiedi(domanda, Sub() esito.TrySetResult(True), Sub() esito.TrySetResult(False))
                              Catch ex As Exception
                                  ScriviErrore("ServiziPlugin.ChiediAsync", ex)
                                  esito.TrySetResult(False)
                              End Try
                          End Sub)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ChiediAsync (esterno)", ex)
            esito.TrySetResult(False)
        End Try
        Return esito.Task
    End Function

    Public Sub ChiediTesto(titolo As String, predefinito As String, alTermine As Action(Of String)) Implements IWGServiziPlugin.ChiediTesto
        Try
            If Not _attivo OrElse alTermine Is Nothing Then Return
            Dim protetta As Action(Of String) = Sub(testo As String)
                                                    Try
                                                        If Not _attivo Then Return
                                                        alTermine.Invoke(testo)
                                                    Catch ex As Exception
                                                        ErrorePlugin("ServiziPlugin.ChiediTesto (plugin)", ex)
                                                        MostraErroreAzione()
                                                    End Try
                                                End Sub
            SuInterfaccia(Sub() AiutoRevisione.ChiediTesto(titolo, predefinito, protetta))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ChiediTesto", ex)
        End Try
    End Sub

    Public Sub ChiediTestoLungo(titolo As String, predefinito As String, alTermine As Action(Of String)) Implements IWGServiziPlugin.ChiediTestoLungo
        Try
            If Not _attivo OrElse alTermine Is Nothing Then Return
            Dim campo As IWGCampoPlugin = NuovoCampo(titolo, predefinito, False)
            campo.AllaChiusura = Sub() alTermine.Invoke(campo.Testo)
            campo.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ChiediTestoLungo", ex)
        End Try
    End Sub

    Public Function EseguiConAttesaAsync(testoAttesa As String, lavoro As Func(Of Task)) As Task Implements IWGServiziPlugin.EseguiConAttesaAsync
        Try
            If Not _attivo Then Return Task.FromResult(0)
            Return WGProcessoInCorso.EseguiAsync(testoAttesa, lavoro)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.EseguiConAttesaAsync", ex)
            Return Task.FromResult(0)
        End Try
    End Function

#End Region

#Region "Elenchi, menu, campi, griglie"

    Public Sub MostraElenco(titolo As String, voci As IList(Of String), alScelto As Action(Of Integer)) Implements IWGServiziPlugin.MostraElenco
        Try
            If Not _attivo OrElse voci Is Nothing OrElse voci.Count = 0 OrElse alScelto Is Nothing Then Return
            Dim menu As IWGMenuPlugin = NuovoMenu(titolo)
            For indice As Integer = 0 To voci.Count - 1
                Dim posizione As Integer = indice
                menu.Aggiungi(voci(indice), Sub() alScelto.Invoke(posizione))
            Next
            menu.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MostraElenco", ex)
        End Try
    End Sub

    Public Sub MostraElencoConId(titolo As String, voci As IList(Of WGVoceElenco), alScelto As Action(Of String)) Implements IWGServiziPlugin.MostraElencoConId
        Try
            If Not _attivo OrElse voci Is Nothing OrElse voci.Count = 0 OrElse alScelto Is Nothing Then Return
            Dim elenco As IWGElencoPlugin = NuovoElenco(titolo)
            For Each voce As WGVoceElenco In voci
                elenco.Aggiungi(voce.Id, voce.Testo)
            Next
            elenco.AlScelto = alScelto
            elenco.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MostraElencoConId", ex)
        End Try
    End Sub

    Public Sub MostraElencoMultiplo(titolo As String, voci As IList(Of WGVoceElenco), alTermine As Action(Of IList(Of String))) Implements IWGServiziPlugin.MostraElencoMultiplo
        Try
            If Not _attivo OrElse voci Is Nothing OrElse voci.Count = 0 OrElse alTermine Is Nothing Then Return
            Dim elenco As IWGElencoPlugin = NuovoElenco(titolo)
            elenco.Multiplo = True
            For Each voce As WGVoceElenco In voci
                elenco.Aggiungi(voce.Id, voce.Testo)
            Next
            elenco.AlleScelte = alTermine
            elenco.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MostraElencoMultiplo", ex)
        End Try
    End Sub

    Public Function NuovoMenu(titolo As String) As IWGMenuPlugin Implements IWGServiziPlugin.NuovoMenu
        Return New MenuPluginImpl(Me, titolo)
    End Function

    Public Function NuovoElenco(titolo As String) As IWGElencoPlugin Implements IWGServiziPlugin.NuovoElenco
        Return New ElencoPluginImpl(Me, titolo)
    End Function

    Public Function NuovoCampo(titolo As String, testo As String, soloLettura As Boolean) As IWGCampoPlugin Implements IWGServiziPlugin.NuovoCampo
        Return New CampoPluginImpl(Me, titolo, testo, soloLettura, False)
    End Function

    Public Sub MostraTesto(titolo As String, testo As String) Implements IWGServiziPlugin.MostraTesto
        Try
            If Not _attivo Then Return
            Dim campo As IWGCampoPlugin = NuovoCampo(titolo, testo, True)
            campo.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MostraTesto", ex)
        End Try
    End Sub

    Public Sub MostraHtml(titolo As String, html As String) Implements IWGServiziPlugin.MostraHtml
        Try
            If Not _attivo Then Return
            Dim campo As IWGCampoPlugin = New CampoPluginImpl(Me, titolo, html, True, True)
            campo.Mostra()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MostraHtml", ex)
        End Try
    End Sub

    Public Function NuovoModulo() As IWGCampiPlugin Implements IWGServiziPlugin.NuovoModulo
        Return New CampiPluginImpl(Me)
    End Function

    Public Function NuovaGriglia(titolo As String, voci As IList(Of String)) As IWGGrigliaPlugin Implements IWGServiziPlugin.NuovaGriglia
        Return New GrigliaPluginImpl(Me, titolo, voci)
    End Function

#End Region

#Region "Menu principale, scrivania, notifiche"

    Public Sub AggiungiVoceMenuPrincipale(testo As String, azione As Action) Implements IWGServiziPlugin.AggiungiVoceMenuPrincipale
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(testo) OrElse azione Is Nothing Then Return
            WGGestorePlugin.AggiungiVoceMenuPrincipale(_id, testo, Protetta(azione, "ServiziPlugin.VoceMenuPrincipale (plugin)"))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiVoceMenuPrincipale", ex)
        End Try
    End Sub

    Public Sub AggiungiVoceAltreFunzioni(testo As String, azione As Action) Implements IWGServiziPlugin.AggiungiVoceAltreFunzioni
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(testo) OrElse azione Is Nothing Then Return
            ' Altre funzioni è un menu come gli altri: stessa strada, così la voce sparisce subito alla disattivazione.
            WGGestorePlugin.AggiungiAggancioMenu(New AggancioMenu() With {
                .Servizi = Me, .Proprietario = _id, .Titolo = "Altre funzioni", .Testo = testo,
                .Azione = Protetta(azione, "ServiziPlugin.VoceAltreFunzioni (plugin)")})
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiVoceAltreFunzioni", ex)
        End Try
    End Sub

    Public Sub AggiungiVoceAMenu(titoloMenu As String, testo As String, azione As Action, Optional dopoLaVoce As String = "") Implements IWGServiziPlugin.AggiungiVoceAMenu
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(titoloMenu) OrElse String.IsNullOrWhiteSpace(testo) OrElse azione Is Nothing Then Return
            WGGestorePlugin.AggiungiAggancioMenu(New AggancioMenu() With {
                .Servizi = Me, .Proprietario = _id, .Titolo = titoloMenu, .Testo = testo, .Dopo = If(dopoLaVoce, ""),
                .Azione = Protetta(azione, "ServiziPlugin.VoceAMenu (plugin)")})
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiVoceAMenu", ex)
        End Try
    End Sub

    Public Sub AggiungiOpzioneAMenu(titoloMenu As String, testo As String, azione As Action) Implements IWGServiziPlugin.AggiungiOpzioneAMenu
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(titoloMenu) OrElse String.IsNullOrWhiteSpace(testo) OrElse azione Is Nothing Then Return
            WGGestorePlugin.AggiungiAggancioMenu(New AggancioMenu() With {
                .Servizi = Me, .Proprietario = _id, .Titolo = titoloMenu, .Testo = testo, .Opzione = True,
                .Azione = Protetta(azione, "ServiziPlugin.OpzioneAMenu (plugin)")})
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiOpzioneAMenu", ex)
        End Try
    End Sub

    Public Sub AlAperturaDiMenu(titoloMenu As String, azione As Action(Of IWGMenuPlugin)) Implements IWGServiziPlugin.AlAperturaDiMenu
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(titoloMenu) OrElse azione Is Nothing Then Return
            WGGestorePlugin.AggiungiAggancioMenu(New AggancioMenu() With {
                .Servizi = Me, .Proprietario = _id, .Titolo = titoloMenu, .Callback = azione})
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AlAperturaDiMenu", ex)
        End Try
    End Sub

    Public Function MenuAperti() As IList(Of String) Implements IWGServiziPlugin.MenuAperti
        Try
            Return WGGestorePlugin.TitoliMenuAperti()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.MenuAperti", ex)
            Return New List(Of String)()
        End Try
    End Function

    Public Sub RegistraFunzioneScrivania(nome As String, azione As Action) Implements IWGServiziPlugin.RegistraFunzioneScrivania
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(nome) OrElse azione Is Nothing Then Return
            WGScrivania.RegistraFunzione(nome, Protetta(azione, "ServiziPlugin.FunzioneScrivania (plugin)"))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.RegistraFunzioneScrivania", ex)
        End Try
    End Sub

    Public Sub Notifica(testo As String, Optional azione As Action = Nothing) Implements IWGServiziPlugin.Notifica
        Try
            If Not _attivo OrElse String.IsNullOrWhiteSpace(testo) OrElse Not Consenti("notifiche", 6) Then Return
            Dim azioneProtetta As Action = Protetta(azione, "ServiziPlugin.Notifica (plugin)")
            SuInterfaccia(Sub()
                              Try
                                  If Not _attivo Then Return
                                  WGNotifiche.WGCentroNotifiche.Pubblica(_nomeVisibile, testo, azioneProtetta, annuncia:=True)
                              Catch exNotifica As Exception
                                  ScriviErrore("ServiziPlugin.Notifica (pubblicazione)", exNotifica)
                              End Try
                          End Sub)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.Notifica", ex)
        End Try
    End Sub

#End Region

#Region "Impostazioni e registro"

    ' Le impostazioni del plugin stanno sotto "plugindati:<id>:": un prefisso che le chiavi di WinGabriel (consensi, permessi, spento)
    ' non hanno mai, così un plugin non può scrivere le scelte dell'utente né quelle di un altro plugin.
    Private Function ChiaveConfig(chiave As String) As String
        Return "plugindati:" & _id & ":" & chiave
    End Function

    ' Le impostazioni stanno in un file nella cartella dati del plugin (_impostazioni.txt: il plugin non può nominare file che iniziano
    ' con il trattino basso), così si sincronizzano con gli altri dati. Una riga per impostazione: chiave e valore in base 64.
    Private ReadOnly _bloccoImpostazioni As New Object()
    Private _impostazioni As Dictionary(Of String, String)

    Private Function PercorsoImpostazioni() As String
        Dim cartella As String = CartellaDati
        If cartella = "" Then Return ""
        Return Path.Combine(cartella, "_impostazioni.txt")
    End Function

    Private Function ImpostazioniCaricate() As Dictionary(Of String, String)
        If _impostazioni Is Nothing Then
            Dim lette As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Dim percorso As String = PercorsoImpostazioni()
            If percorso <> "" AndAlso System.IO.File.Exists(percorso) Then
                For Each riga As String In System.IO.File.ReadAllLines(percorso)
                    Dim uguale As Integer = riga.IndexOf("="c)
                    If uguale <= 0 Then Continue For
                    Try
                        lette(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(riga.Substring(0, uguale)))) = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(riga.Substring(uguale + 1)))
                    Catch
                    End Try
                Next
            End If
            _impostazioni = lette
        End If
        Return _impostazioni
    End Function

    Public Function Leggi(chiave As String) As String Implements IWGServiziPlugin.Leggi
        Try
            If String.IsNullOrWhiteSpace(chiave) OrElse chiave.Length > 64 Then Return ""
            SyncLock _bloccoImpostazioni
                Dim valore As String = Nothing
                If ImpostazioniCaricate().TryGetValue(chiave, valore) Then Return valore
            End SyncLock
            ' le impostazioni scritte prima che stessero nel file si leggono ancora dalla configurazione
            Return If((New WGConfig()).LeggiValore(ChiaveConfig(chiave)), "")
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.Leggi", ex)
            Return ""
        End Try
    End Function

    Public Sub Scrivi(chiave As String, valore As String) Implements IWGServiziPlugin.Scrivi
        Try
            If String.IsNullOrWhiteSpace(chiave) OrElse chiave.Length > 64 OrElse Not _attivo Then Return
            If valore IsNot Nothing AndAlso valore.Length > 16384 Then Return
            If Not Consenti("impostazioni", 120) Then Return
            SyncLock _bloccoImpostazioni
                Dim tutte As Dictionary(Of String, String) = ImpostazioniCaricate()
                tutte(chiave) = If(valore, "")
                If tutte.Count > 500 Then
                    tutte.Remove(chiave)
                    Return
                End If
                Dim righe As New List(Of String)()
                For Each voce As KeyValuePair(Of String, String) In tutte
                    righe.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(voce.Key)) & "=" & Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(voce.Value)))
                Next
                Dim percorso As String = PercorsoImpostazioni()
                If percorso <> "" Then System.IO.File.WriteAllLines(percorso, righe)
            End SyncLock
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.Scrivi", ex)
        End Try
    End Sub

    ''' <summary>Quello che il plugin chiama nei suoi Try/Catch: nel registro, e conta per l'interruttore automatico.</summary>
    Public Sub RegistraErrore(origine As String, ex As Exception) Implements IWGServiziPlugin.RegistraErrore
        Try
            If Not _attivo Then Return
            ' il testo lo decide il plugin: si accorcia, e il registro non riceve altro
            ScriviErrore("(plugin) " & If(origine, ""), ex)
            ContaErrore()
        Catch
        End Try
    End Sub

    ''' <summary>Un errore dei servizi di WinGabriel: solo nel registro.</summary>
    Friend Sub ScriviErrore(origine As String, ex As Exception)
        Try
            log.ScriviLog("plugin " & _id & ": " & origine & ": " & If(ex IsNot Nothing, ex.ToString(), ""))
        Catch
        End Try
    End Sub

#End Region

End Class
