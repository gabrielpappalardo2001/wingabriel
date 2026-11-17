Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports UIAutomationClient

' Un componente aggiuntivo è una libreria che eredita questa classe e sta nella cartella "moduli" accanto al lettore.
' Il lettore la carica all'avvio e la usa per i programmi che dichiara.
Public MustInherit Class ModuloEsternoLettore

    ' Nomi degli eseguibili, senza ".exe", per esempio "notepad++".
    Public MustOverride ReadOnly Property Programmi As String()

    ' Il nome che si legge nell'elenco dei componenti aggiuntivi. Se non lo si ridefinisce è il nome della classe.
    Public Overridable ReadOnly Property Nome As String
        Get
            Return [GetType]().Name
        End Get
    End Property

    ' Chiamato una volta, appena il componente è caricato: qui si tengono i servizi del lettore (parlare, impostazioni, elementi).
    Public Overridable Sub Avvia(servizi As ServiziComponente)
    End Sub

    ' Come AlFuoco, ma con l'elemento dell'interfaccia: si può guardare il tipo, i figli, i testi che contiene. Per impostazione predefinita
    ' chiama AlFuoco.
    Public Overridable Function AlFuocoElemento(elemento As ElementoUi, frase As String) As String
        Return AlFuoco(frase, If(elemento IsNot Nothing, elemento.Nome, ""))
    End Function

    ' Un avviso (notifica, regione viva) che arriva dal programma. Restituisci Nothing per lasciare fare al lettore, una stringa vuota per
    ' non farlo dire, un testo per farlo dire al posto dell'originale.
    Public Overridable Function AlEvento(evento As EventoComponente) As String
        Return Nothing
    End Function

    ' I comandi del componente (tasto con il tasto dei comandi del lettore) e che cosa fare quando si premono.
    Public Overridable Function Comandi() As ComandoComponente()
        Return New ComandoComponente() {}
    End Function

    Public Overridable Sub EseguiComando(id As String)
    End Sub

    ' Voci che il componente aggiunge al menu del lettore.
    Public Overridable Function VociMenu() As VoceMenuComponente()
        Return New VoceMenuComponente() {}
    End Function

    ' Riceve la frase che il lettore dice quando il fuoco arriva su un elemento e restituisce quella da dire.
    Public Overridable Function AlFuoco(frase As String, nomeElemento As String) As String
        Return frase
    End Function

    ' Chiamato a ogni tasto di spostamento o di modifica; se restituisce un testo, il lettore lo dice dopo il resto.
    Public Overridable Function AlTasto(codiceTasto As Integer, ctrl As Boolean, maiuscolo As Boolean, alt As Boolean) As String
        Return ""
    End Function

    ' Formato del testo, quando si chiede il formato.
    Public Overridable Function Formato() As String
        Return ""
    End Function

    ' Barra di stato, quando si chiede.
    Public Overridable Function BarraStato() As String
        Return ""
    End Function

End Class

''' <summary>Un componente aggiuntivo come lo vede l'elenco: il file, il nome, i programmi e se è attivo.</summary>
Public NotInheritable Class InfoComponente
    Public File As String = ""
    Public Nome As String = ""
    Public Programmi As String() = New String() {}
    Public Attivo As Boolean = True
End Class

''' <summary>L'elenco dei componenti aggiuntivi e le azioni dell'utente su di loro: disattivare, riattivare, disinstallare.</summary>
Public NotInheritable Class ComponentiAggiuntivi

    Private Const ChiaveDisattivati As String = "lettore_schermo_componenti_disattivati"
    Private Const PrefissoNome As String = "lettore_schermo_componente_nome_"
    Private Const PrefissoProgrammi As String = "lettore_schermo_componente_programmi_"

    Private Sub New()
    End Sub

    ''' <summary>Vero se il lettore è partito con "riavvio senza componenti aggiuntivi": per questa volta non se ne carica nessuno.</summary>
    Public Shared Property SenzaComponenti As Boolean

    Public Shared Function Cartella() As String
        Try
            Dim cartellaBase As String = Path.GetDirectoryName(GetType(ComponentiAggiuntivi).Assembly.Location)
            Return If(String.IsNullOrEmpty(cartellaBase), "", Path.Combine(cartellaBase, "moduli"))
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.Cartella", ex)
            Return ""
        End Try
    End Function

    Public Shared Function Presenti() As Boolean
        Try
            Dim dove As String = Cartella()
            Return dove <> "" AndAlso Directory.Exists(dove) AndAlso Directory.GetFiles(dove, "*.dll").Length > 0
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.Presenti", ex)
            Return False
        End Try
    End Function

    Friend Shared Function Disattivati() As HashSet(Of String)
        Dim insieme As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            For Each parte As String In ConfigLettore.Leggi(ChiaveDisattivati, "").Split(";"c)
                If parte.Trim() <> "" Then insieme.Add(parte.Trim())
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.Disattivati", ex)
        End Try
        Return insieme
    End Function

    Public Shared Function Elenco() As List(Of InfoComponente)
        Dim risultato As New List(Of InfoComponente)()
        Try
            Dim dove As String = Cartella()
            If dove = "" OrElse Not Directory.Exists(dove) Then Return risultato
            ModuliEsterni.AssicuraCaricati()
            Dim spenti As HashSet(Of String) = Disattivati()
            For Each percorso As String In Directory.GetFiles(dove, "*.dll")
                Dim nomeFile As String = Path.GetFileName(percorso)
                Dim info As New InfoComponente()
                info.File = nomeFile
                info.Attivo = Not spenti.Contains(nomeFile)
                Dim caricato As ModuloEsternoLettore = ModuliEsterni.PrimoCaricato(nomeFile)
                If caricato IsNot Nothing Then
                    info.Nome = caricato.Nome
                    info.Programmi = ModuliEsterni.ProgrammiDi(nomeFile)
                Else
                    info.Nome = ConfigLettore.Leggi(PrefissoNome & nomeFile, "")
                    Dim memoria As String = ConfigLettore.Leggi(PrefissoProgrammi & nomeFile, "")
                    If memoria <> "" Then info.Programmi = memoria.Split(";"c)
                End If
                If String.IsNullOrWhiteSpace(info.Nome) Then info.Nome = Path.GetFileNameWithoutExtension(nomeFile)
                risultato.Add(info)
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.Elenco", ex)
        End Try
        Return risultato
    End Function

    ''' <summary>Disattiva o riattiva un componente: ha effetto subito, senza riavviare.</summary>
    Public Shared Sub ImpostaAttivo(nomeFile As String, attivo As Boolean)
        Try
            Dim spenti As HashSet(Of String) = Disattivati()
            If attivo Then spenti.Remove(nomeFile) Else spenti.Add(nomeFile)
            ConfigLettore.Scrivi(ChiaveDisattivati, String.Join(";", spenti))
            If Not attivo Then ModuliEsterni.Scarica(nomeFile)
            If attivo Then ModuliEsterni.ForzaControllo()
            WGLettoreSchermo.GetInstance().AggiornaComponenti()
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.ImpostaAttivo", ex)
        End Try
    End Sub

    ''' <summary>Cancella il file del componente. Vero se è sparito.</summary>
    Public Shared Function Disinstalla(nomeFile As String) As Boolean
        Try
            ModuliEsterni.Scarica(nomeFile)
            Dim percorso As String = Path.Combine(Cartella(), nomeFile)
            If File.Exists(percorso) Then File.Delete(percorso)
            Dim spenti As HashSet(Of String) = Disattivati()
            If spenti.Remove(nomeFile) Then ConfigLettore.Scrivi(ChiaveDisattivati, String.Join(";", spenti))
            ConfigLettore.Elimina(PrefissoNome & nomeFile)
            ConfigLettore.Elimina(PrefissoProgrammi & nomeFile)
            WGLettoreSchermo.GetInstance().AggiornaComponenti()
            Return Not File.Exists(percorso)
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.Disinstalla", ex)
            Return False
        End Try
    End Function

    ''' <summary>Se il tasto premuto con il tasto dei comandi è di un componente, il suo identificativo (file e comando); altrimenti una stringa vuota.</summary>
    Friend Shared Function CercaComando(codice As Integer, ctrl As Boolean, maiuscolo As Boolean, programma As String) As String
        Try
            If SenzaComponenti Then Return ""
            For Each coppia As KeyValuePair(Of String, ModuloEsternoLettore) In ModuliEsterni.Moduli()
                Dim comandi As ComandoComponente() = coppia.Value.Comandi()
                If comandi Is Nothing Then Continue For
                For Each comando As ComandoComponente In comandi
                    If comando Is Nothing OrElse comando.Tasto <> codice OrElse comando.Ctrl <> ctrl OrElse comando.Maiuscolo <> maiuscolo Then Continue For
                    If Not comando.Globale AndAlso Not ModuliEsterni.DichiaraProgramma(coppia.Value, programma) Then Continue For
                    Return coppia.Key & "|" & comando.Id
                Next
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.CercaComando", ex)
        End Try
        Return ""
    End Function

    Friend Shared Sub EseguiComando(identificativo As String)
        Try
            Dim posizione As Integer = identificativo.IndexOf("|"c)
            If posizione <= 0 Then Return
            Dim nomeFile As String = identificativo.Substring(0, posizione)
            Dim id As String = identificativo.Substring(posizione + 1)
            For Each coppia As KeyValuePair(Of String, ModuloEsternoLettore) In ModuliEsterni.Moduli()
                If String.Equals(coppia.Key, nomeFile, StringComparison.OrdinalIgnoreCase) Then coppia.Value.EseguiComando(id)
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.EseguiComando", ex)
        End Try
    End Sub

    ' Le voci che i componenti attivi aggiungono al menu del lettore.
    Public Shared Function VociDiMenu() As List(Of VoceMenuComponente)
        Dim elenco As New List(Of VoceMenuComponente)()
        Try
            If SenzaComponenti Then Return elenco
            For Each coppia As KeyValuePair(Of String, ModuloEsternoLettore) In ModuliEsterni.Moduli()
                Dim voci As VoceMenuComponente() = coppia.Value.VociMenu()
                If voci Is Nothing Then Continue For
                For Each voce As VoceMenuComponente In voci
                    If voce IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(voce.Testo) AndAlso voce.Azione IsNot Nothing Then elenco.Add(voce)
                Next
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.VociDiMenu", ex)
        End Try
        Return elenco
    End Function

    Friend Shared Sub RicordaNome(nomeFile As String, nome As String, programmi As String())
        Try
            ConfigLettore.Scrivi(PrefissoNome & nomeFile, nome)
            ConfigLettore.Scrivi(PrefissoProgrammi & nomeFile, String.Join(";", programmi))
        Catch ex As Exception
            RegistroErrori.Scrivi("ComponentiAggiuntivi.RicordaNome", ex)
        End Try
    End Sub

End Class

Friend NotInheritable Class ModuliEsterni

    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _perFile As New Dictionary(Of String, List(Of ModuloEsternoLettore))(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _tentati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Private Sub New()
    End Sub

    Public Shared Function Trova(nomeProgramma As String) As ModuloEsternoLettore
        Try
            If String.IsNullOrEmpty(nomeProgramma) OrElse ComponentiAggiuntivi.SenzaComponenti Then Return Nothing
            SyncLock _blocco
                CaricaMancanti()
                Dim trovato As ModuloEsternoLettore = Nothing
                For Each coppia As KeyValuePair(Of String, List(Of ModuloEsternoLettore)) In _perFile
                    For Each modulo As ModuloEsternoLettore In coppia.Value
                        Dim programmi As String() = modulo.Programmi
                        If programmi Is Nothing Then Continue For
                        For Each programma As String In programmi
                            If String.Equals(programma, nomeProgramma, StringComparison.OrdinalIgnoreCase) Then trovato = modulo
                        Next
                    Next
                Next
                Return trovato
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ModuliEsterni.Trova", ex)
            Return Nothing
        End Try
    End Function

    Public Shared Sub AssicuraCaricati()
        SyncLock _blocco
            If Not ComponentiAggiuntivi.SenzaComponenti Then CaricaMancanti()
        End SyncLock
    End Sub

    ' I moduli attivi e caricati, ciascuno con il file da cui viene.
    Public Shared Function Moduli() As List(Of KeyValuePair(Of String, ModuloEsternoLettore))
        Dim elenco As New List(Of KeyValuePair(Of String, ModuloEsternoLettore))()
        SyncLock _blocco
            If ComponentiAggiuntivi.SenzaComponenti Then Return elenco
            CaricaMancanti()
            For Each coppia As KeyValuePair(Of String, List(Of ModuloEsternoLettore)) In _perFile
                For Each modulo As ModuloEsternoLettore In coppia.Value
                    elenco.Add(New KeyValuePair(Of String, ModuloEsternoLettore)(coppia.Key, modulo))
                Next
            Next
        End SyncLock
        Return elenco
    End Function

    Public Shared Function DichiaraProgramma(modulo As ModuloEsternoLettore, programma As String) As Boolean
        Try
            Dim programmi As String() = modulo.Programmi
            If programmi Is Nothing OrElse String.IsNullOrEmpty(programma) Then Return False
            For Each nome As String In programmi
                If String.Equals(nome, programma, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ModuliEsterni.DichiaraProgramma", ex)
        End Try
        Return False
    End Function

    Public Shared Function PrimoCaricato(nomeFile As String) As ModuloEsternoLettore
        SyncLock _blocco
            Dim lista As List(Of ModuloEsternoLettore) = Nothing
            If _perFile.TryGetValue(nomeFile, lista) AndAlso lista.Count > 0 Then Return lista(0)
        End SyncLock
        Return Nothing
    End Function

    Public Shared Function ProgrammiDi(nomeFile As String) As String()
        Dim risultato As New List(Of String)()
        SyncLock _blocco
            Dim lista As List(Of ModuloEsternoLettore) = Nothing
            If _perFile.TryGetValue(nomeFile, lista) Then
                For Each modulo As ModuloEsternoLettore In lista
                    Try
                        If modulo.Programmi IsNot Nothing Then risultato.AddRange(modulo.Programmi)
                    Catch ex As Exception
                        RegistroErrori.Scrivi("ModuliEsterni.ProgrammiDi", ex)
                    End Try
                Next
            End If
        End SyncLock
        Return risultato.ToArray()
    End Function

    Public Shared Sub Scarica(nomeFile As String)
        SyncLock _blocco
            _perFile.Remove(nomeFile)
            _tentati.Remove(nomeFile)
        End SyncLock
    End Sub

    ' Solo la cartella "moduli" accanto al lettore: non si caricano librerie da posti scrivibili da chiunque. La libreria si legge in
    ' memoria, così il file resta libero e si può cancellare con il lettore in funzione. I componenti disattivati non si caricano.
    Private Shared _prossimoControllo As Integer

    Public Shared Sub ForzaControllo()
        SyncLock _blocco
            _prossimoControllo = Environment.TickCount - 1
        End SyncLock
    End Sub

    Private Shared Sub CaricaMancanti()
        Try
            ' La cartella si guarda al massimo ogni due secondi: la domanda arriva anche a ogni tasto.
            If Environment.TickCount - _prossimoControllo < 0 Then Return
            _prossimoControllo = Environment.TickCount + 2000
            Dim cartella As String = ComponentiAggiuntivi.Cartella()
            If cartella = "" OrElse Not Directory.Exists(cartella) Then Return
            Dim spenti As HashSet(Of String) = ComponentiAggiuntivi.Disattivati()
            For Each percorso As String In Directory.GetFiles(cartella, "*.dll")
                Dim nomeFile As String = Path.GetFileName(percorso)
                If spenti.Contains(nomeFile) OrElse _tentati.Contains(nomeFile) Then Continue For
                _tentati.Add(nomeFile)
                CaricaFile(percorso, nomeFile)
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("ModuliEsterni.CaricaMancanti", ex)
        End Try
    End Sub

    Private Shared Sub CaricaFile(percorso As String, nomeFile As String)
        Try
            Dim assieme As Assembly = Assembly.Load(File.ReadAllBytes(percorso))
            Dim lista As New List(Of ModuloEsternoLettore)()
            For Each tipo As Type In assieme.GetTypes()
                If tipo.IsAbstract OrElse Not GetType(ModuloEsternoLettore).IsAssignableFrom(tipo) Then Continue For
                Dim modulo As ModuloEsternoLettore = TryCast(Activator.CreateInstance(tipo), ModuloEsternoLettore)
                If modulo IsNot Nothing Then
                    lista.Add(modulo)
                    Try
                        modulo.Avvia(New ServiziComponente(nomeFile))
                    Catch exAvvia As Exception
                        RegistroErrori.Scrivi("ModuliEsterni.CaricaFile.Avvia", exAvvia)
                    End Try
                End If
            Next
            If lista.Count = 0 Then Return
            _perFile(nomeFile) = lista
            Dim programmi As String() = ProgrammiDi(nomeFile)
            ComponentiAggiuntivi.RicordaNome(nomeFile, lista(0).Nome, programmi)
        Catch ex As Exception
            RegistroErrori.Scrivi("ModuliEsterni.CaricaFile", ex)
        End Try
    End Sub

End Class

Friend NotInheritable Class AppModuleEsterno
    Inherits AppModuleLettore

    Private ReadOnly _esterno As ModuloEsternoLettore

    Public Sub New(lettore As WGLettoreSchermo, nomeApp As String, esterno As ModuloEsternoLettore)
        MyBase.New(lettore, nomeApp)
        _esterno = esterno
    End Sub

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return testoBase
            Dim frase As String = _esterno.AlFuocoElemento(New ElementoUi(elemento, Lettore), testoBase)
            Return If(frase, testoBase)
        Catch ex As Exception
            RegistroErrori.Scrivi("AppModuleEsterno.AllFocus", ex)
            Return testoBase
        End Try
    End Function

    Public Overrides Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If evento Is Nothing Then Return False
            Dim frase As String = _esterno.AlTasto(evento.CodiceTasto, evento.Ctrl, evento.Maiuscolo, evento.Alt)
            If Not String.IsNullOrWhiteSpace(frase) Then Lettore.ParlaApp(frase, False)
        Catch ex As Exception
            RegistroErrori.Scrivi("AppModuleEsterno.AllTasto", ex)
        End Try
        Return False
    End Function

    Public Overrides Function AllNotifica(evento As EventoLettore) As Boolean
        Return Inoltra("notifica", evento)
    End Function

    Public Overrides Function AllAvviso(evento As EventoLettore) As Boolean
        Return Inoltra(If(evento IsNot Nothing AndAlso evento.Tipo = TipoEventoLettore.RegioneViva, "regioneViva", "avviso"), evento)
    End Function

    Private Function Inoltra(tipo As String, evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing Then Return False
            Dim dati As New EventoComponente()
            dati.Tipo = tipo
            dati.Testo = If(evento.Testo, "")
            If evento.Elemento IsNot Nothing Then dati.Elemento = New ElementoUi(evento.Elemento, Lettore)
            Dim risposta As String = _esterno.AlEvento(dati)
            If risposta Is Nothing Then Return False
            If risposta <> "" Then Lettore.ParlaApp(risposta, False)
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("AppModuleEsterno.Inoltra", ex)
            Return False
        End Try
    End Function

    Public Overrides Function FormatoApp(elemento As IUIAutomationElement) As String
        Try
            Return If(_esterno.Formato(), "")
        Catch ex As Exception
            RegistroErrori.Scrivi("AppModuleEsterno.FormatoApp", ex)
            Return ""
        End Try
    End Function

    Public Overrides Function TestoBarraStatoApp(finestra As IntPtr) As String
        Try
            Return If(_esterno.BarraStato(), "")
        Catch ex As Exception
            RegistroErrori.Scrivi("AppModuleEsterno.TestoBarraStatoApp", ex)
            Return ""
        End Try
    End Function

End Class
