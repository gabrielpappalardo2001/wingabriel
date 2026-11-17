Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Security.Principal
Imports System.Text
Imports wgcontrols

''' <summary>
''' L'installazione di WinGabriel in Windows: un'attività pianificata "WinGabriel" che lo avvia con i privilegi di amministratore senza
''' chiedere conferma ogni volta, e le icone (desktop e menu Start) che fanno partire quella attività. Con l'icona si ha sempre il
''' controllo account utente già "concesso": la conferma di Windows si dà una volta sola, quando si crea l'attività.
''' Il programma non si sposta: l'installazione registra WinGabriel dove si trova, quindi un WinGabriel portable resta portable finché
''' non si sceglie di installarlo. Per cambiare cartella basta rifare l'installazione.
''' </summary>
Public NotInheritable Class IntegrazioneWindows

    Public Const NomeAttivita As String = "WinGabriel"
    Private Const CodiceAnnullato As Integer = 1223

    Private Sub New()
    End Sub

    Private Shared Function EseguiConUscita(programma As String, argomenti As String, ByRef uscita As String) As Integer
        Try
            Dim info As New ProcessStartInfo(programma, argomenti)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            info.RedirectStandardOutput = True
            info.RedirectStandardError = True
            info.StandardOutputEncoding = Encoding.UTF8
            Using processo As Process = Process.Start(info)
                If processo Is Nothing Then
                    uscita = "processo non avviato"
                    Return -1
                End If
                ' I due flussi vanno svuotati insieme: leggerli in fila blocca schtasks
                ' quando il buffer dell'altro si riempie, e l'installazione non torna più.
                Dim letturaUscita As System.Threading.Tasks.Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As System.Threading.Tasks.Task(Of String) = processo.StandardError.ReadToEndAsync()
                If Not processo.WaitForExit(30000) Then
                    Try
                        processo.Kill()
                    Catch exArresto As Exception
                        uscita = exArresto.Message
                    End Try
                    processo.WaitForExit(5000)
                End If
                Dim fuori As String = ""
                Dim errori As String = ""
                Try
                    If letturaUscita.Wait(5000) Then fuori = If(letturaUscita.Result, "")
                Catch exUscita As Exception
                    fuori = exUscita.Message
                End Try
                Try
                    If letturaErrori.Wait(5000) Then errori = If(letturaErrori.Result, "")
                Catch exErrori As Exception
                    errori = exErrori.Message
                End Try
                uscita = fuori & errori
                If Not processo.HasExited Then Return -1
                Return processo.ExitCode
            End Using
        Catch ex As Exception
            uscita = ex.Message
            Return -1
        End Try
    End Function

    Public Shared Function EseguibileCorrente() As String
        Return Application.ExecutablePath
    End Function

    ' Il programma che attività e icone avviano: quello installato (durante l'installazione) oppure questo.
    Private Shared _eseguibileDaAvviare As String = ""

    Private Shared Function EseguibileDaAvviare() As String
        Return If(_eseguibileDaAvviare = "", EseguibileCorrente(), _eseguibileDaAvviare)
    End Function

    Public Shared Function CartellaInstallazione() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGabriel")
    End Function

    ''' <summary>Vero se questo WinGabriel gira già dalla cartella di installazione.</summary>
    Public Shared Function EInstallato() As Boolean
        Try
            Return String.Equals(Path.GetDirectoryName(EseguibileCorrente()).TrimEnd("\"c), CartellaInstallazione().TrimEnd("\"c), StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Dove stanno configurazione e dati di un'installazione senza copia: in Programmi non si può scrivere.</summary>
    Public Shared Function CartellaConfigurazioneNuova() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel")
    End Function

    Private Shared Sub CopiaCartella(origine As String, destinazione As String, escludiDati As Boolean, ByRef falliti As Integer)
        Directory.CreateDirectory(destinazione)
        For Each percorso As String In Directory.GetFiles(origine)
            Try
                File.Copy(percorso, Path.Combine(destinazione, Path.GetFileName(percorso)), True)
            Catch
                falliti += 1
            End Try
        Next
        For Each cartella As String In Directory.GetDirectories(origine)
            Dim nome As String = Path.GetFileName(cartella)
            ' i dati, la configurazione e i log sono di chi usa il programma: la copia installata non li porta con sé
            If escludiDati AndAlso (String.Equals(nome, "dati", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(nome, "config", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(nome, "logs", StringComparison.OrdinalIgnoreCase)) Then Continue For
            CopiaCartella(cartella, Path.Combine(destinazione, nome), False, falliti)
        Next
    End Sub

    ''' <summary>
    ''' Prepara il profilo della copia installata, con l'utente normale (il registro e i file sono i suoi). Con la copia: la configurazione
    ''' attuale e i dati vanno nella cartella scelta e la configurazione parte già finita. Senza: configurazione vuota, quindi al primo avvio
    ''' si sceglie tutto. In ogni caso la configurazione condivisa è registrata nel registro dell'utente, perché in Programmi non si scrive.
    ''' </summary>
    Public Shared Function PreparaProfilo(copiaConfigurazione As Boolean, cartellaDati As String) As Boolean
        Try
            ' la copia installata tiene la configurazione nei dati locali dell'utente (e non nei dati scelti): quella è la sua cartella di configurazione
            Dim cartellaConfig As String = Path.Combine(CartellaConfigurazioneNuova(), "config")
            Directory.CreateDirectory(cartellaConfig)
            Dim nuovo As String = Path.Combine(cartellaConfig, "config.xml")
            If copiaConfigurazione Then
                Dim origineDati As String = WGDb.WGDatabase.CartellaBaseDatiEffettiva()
                Dim attuale As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
                If Not File.Exists(attuale) Then attuale = Path.Combine(origineDati, "config", "config.xml")
                ' Prima i dati, poi la configurazione, così quella nuova non viene sovrascritta.
                If Directory.Exists(origineDati) AndAlso Not String.Equals(Path.GetFullPath(origineDati).TrimEnd("\"c), Path.GetFullPath(cartellaDati).TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then
                    Dim falliti As Integer = 0
                    CopiaCartella(origineDati, cartellaDati, False, falliti)
                End If
                Dim doc As New System.Xml.XmlDocument()
                doc.XmlResolver = Nothing
                If File.Exists(attuale) Then doc.Load(attuale) Else doc.LoadXml("<Configurazioni />")
                ImpostaValore(doc, WGDb.WGDatabase.IdConfigurazioneCartellaBaseDati, cartellaDati)
                ImpostaValore(doc, "configurazione_guidata_da_continuare", "1")
                ImpostaValore(doc, "crea_icona_desktop", "true")
                doc.Save(nuovo)
            Else
                File.WriteAllText(nuovo, "<?xml version=""1.0"" encoding=""UTF-8""?><Configurazioni />", New UTF8Encoding(False))
            End If
            Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\WinGabriel")
                _puntatorePrecedente = Convert.ToString(chiave.GetValue("PercorsoConfigurazione"))
                chiave.SetValue("PercorsoConfigurazione", nuovo)
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared _puntatorePrecedente As String = ""

    ''' <summary>Se l'installazione non è riuscita, il puntatore alla configurazione torna a quello di prima.</summary>
    Public Shared Sub RipristinaProfilo()
        Try
            If _puntatorePrecedente = "" Then Return
            Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\WinGabriel")
                chiave.SetValue("PercorsoConfigurazione", _puntatorePrecedente)
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>Avvia la copia installata tramite l'attività (dopo che questo programma è uscito, per via dell'istanza unica) ed esce.</summary>
    Public Shared Sub RiavviaDallaCopiaInstallata()
        Try
            Dim info As New ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & schtasks.exe /Run /TN " & NomeAttivita)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            Process.Start(info)
        Catch
        End Try
        Application.Exit()
        Environment.Exit(0)
    End Sub

    Private Shared Sub ImpostaValore(doc As System.Xml.XmlDocument, id As String, valore As String)
        Dim nodo As System.Xml.XmlElement = Nothing
        For Each n As System.Xml.XmlNode In doc.DocumentElement.ChildNodes
            Dim e As System.Xml.XmlElement = TryCast(n, System.Xml.XmlElement)
            If e IsNot Nothing AndAlso e.GetAttribute("id") = id Then
                nodo = e
                Exit For
            End If
        Next
        If nodo Is Nothing Then
            nodo = doc.CreateElement("Configurazione")
            nodo.SetAttribute("id", id)
            doc.DocumentElement.AppendChild(nodo)
        End If
        nodo.RemoveAll()
        nodo.SetAttribute("id", id)
        Dim v As System.Xml.XmlElement = doc.CreateElement("Valore")
        v.InnerText = valore
        nodo.AppendChild(v)
    End Sub

    ''' <summary>Con i privilegi di amministratore: copia il programma (tutto tranne i dati) in Programmi. 0 se riuscito.</summary>
    Private Shared Function CopiaProgramma(destinazione As String) As Integer
        Try
            Dim origine As String = Path.GetDirectoryName(EseguibileCorrente())
            If String.Equals(origine.TrimEnd("\"c), destinazione.TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then Return 0
            ' Una copia già installata e in esecuzione va chiusa, altrimenti i suoi file non si possono sostituire.
            Dim proprio As Integer = Process.GetCurrentProcess().Id
            Dim installato As String = Path.Combine(destinazione, Path.GetFileName(EseguibileCorrente()))
            For Each p As Process In Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EseguibileCorrente()))
                Try
                    If p.Id <> proprio AndAlso String.Equals(p.MainModule.FileName, installato, StringComparison.OrdinalIgnoreCase) Then
                        p.Kill()
                        p.WaitForExit(5000)
                    End If
                Catch
                End Try
            Next
            Dim falliti As Integer = 0
            CopiaCartella(origine, destinazione, True, falliti)
            Return If(falliti = 0, 0, 3)
        Catch
            Return 3
        End Try
    End Function

    ''' <summary>Vero se l'attività esiste.</summary>
    Public Shared Function AttivitaEsiste() As Boolean
        Dim uscita As String = ""
        Return EseguiConUscita("schtasks.exe", "/Query /TN " & NomeAttivita, uscita) = 0
    End Function

    ''' <summary>Vero se l'attività esiste e avvia proprio questo WinGabriel (non una copia che poi è stata spostata o cancellata).</summary>
    Public Shared Function AttivitaPuntaQui() As Boolean
        Try
            Dim xml As String = ""
            If EseguiConUscita("schtasks.exe", "/Query /TN " & NomeAttivita & " /XML", xml) <> 0 Then Return False
            Return xml.IndexOf(EseguibileCorrente(), StringComparison.OrdinalIgnoreCase) >= 0
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Fa partire WinGabriel tramite l'attività (elevato, senza conferma). Vero se Windows l'ha avviata.</summary>
    Public Shared Function AvviaTramiteAttivita() As Boolean
        Dim uscita As String = ""
        Return EseguiConUscita("schtasks.exe", "/Run /TN " & NomeAttivita, uscita) = 0
    End Function

    Public Shared Function Installata() As Boolean
        Return AttivitaEsiste()
    End Function

    Private Shared Function Escape(testo As String) As String
        Return System.Security.SecurityElement.Escape(testo)
    End Function

    Private Shared Function CreaAttivita() As Boolean
        Dim utente As String = WindowsIdentity.GetCurrent().Name
        Dim eseguibile As String = EseguibileDaAvviare()
        Dim xml As New StringBuilder()
        xml.AppendLine("<?xml version=""1.0"" encoding=""UTF-16""?>")
        xml.AppendLine("<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">")
        xml.AppendLine("  <RegistrationInfo><Description>Avvia WinGabriel con i privilegi di amministratore, senza chiedere conferma.</Description></RegistrationInfo>")
        xml.AppendLine("  <Triggers />")
        xml.AppendLine("  <Principals><Principal id=""Author""><UserId>" & Escape(utente) & "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>")
        xml.AppendLine("  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled><AllowStartOnDemand>true</AllowStartOnDemand></Settings>")
        xml.AppendLine("  <Actions Context=""Author""><Exec><Command>" & Escape(eseguibile) & "</Command><Arguments>/nonelevare</Arguments><WorkingDirectory>" & Escape(Path.GetDirectoryName(eseguibile)) & "</WorkingDirectory></Exec></Actions>")
        xml.AppendLine("</Task>")
        Dim percorsoXml As String = Path.Combine(Path.GetTempPath(), "wingabriel-attivita-" & Guid.NewGuid().ToString("N") & ".xml")
        File.WriteAllText(percorsoXml, xml.ToString(), Encoding.Unicode)
        Try
            Dim uscita As String = ""
            Return EseguiConUscita("schtasks.exe", "/Create /TN " & NomeAttivita & " /XML """ & percorsoXml & """ /F", uscita) = 0
        Finally
            Try
                File.Delete(percorsoXml)
            Catch
            End Try
        End Try
    End Function

    Private Shared Function PercorsoIconaDesktop() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinGabriel.lnk")
    End Function

    Private Shared Function PercorsoIconaStart() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinGabriel.lnk")
    End Function

    ''' <summary>Crea un'icona che avvia l'attività. Con l'attività, l'icona ha sempre i privilegi senza chiedere la conferma.</summary>
    Public Shared Sub CreaIcona(percorso As String)
        Dim shell As Object = Nothing
        Dim collegamento As Object = Nothing
        Try
            shell = CreateObject("WScript.Shell")
            collegamento = shell.CreateShortcut(percorso)
            collegamento.TargetPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe")
            collegamento.Arguments = "/Run /TN " & NomeAttivita
            collegamento.WorkingDirectory = Path.GetDirectoryName(EseguibileDaAvviare())
            collegamento.WindowStyle = 7
            collegamento.IconLocation = EseguibileDaAvviare() & ",0"
            collegamento.Description = "WinGabriel"
            collegamento.Save()
        Finally
            If collegamento IsNot Nothing Then System.Runtime.InteropServices.Marshal.ReleaseComObject(collegamento)
            If shell IsNot Nothing Then System.Runtime.InteropServices.Marshal.ReleaseComObject(shell)
        End Try
    End Sub

    ''' <summary>
    ''' Vero se il collegamento è l'icona dell'installazione, quella che avvia l'attività pianificata (e quindi WinGabriel con i privilegi,
    ''' senza conferma). Una copia portatile non deve mai sostituirla né toglierla.
    ''' </summary>
    Public Shared Function CollegamentoDellAttivita(percorso As String) As Boolean
        Dim shell As Object = Nothing
        Dim collegamento As Object = Nothing
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return False
            shell = CreateObject("WScript.Shell")
            collegamento = shell.CreateShortcut(percorso)
            Dim destinazione As String = Convert.ToString(collegamento.TargetPath)
            Dim argomenti As String = Convert.ToString(collegamento.Arguments)
            Return destinazione.EndsWith("schtasks.exe", StringComparison.OrdinalIgnoreCase) AndAlso argomenti.IndexOf("/TN " & NomeAttivita, StringComparison.OrdinalIgnoreCase) >= 0
        Catch
            Return False
        Finally
            If collegamento IsNot Nothing Then System.Runtime.InteropServices.Marshal.ReleaseComObject(collegamento)
            If shell IsNot Nothing Then System.Runtime.InteropServices.Marshal.ReleaseComObject(shell)
        End Try
    End Function

    ''' <summary>
    ''' Solo nella copia installata: se sul desktop o nel menu Start c'è l'icona di WinGabriel ma non avvia l'attività pianificata (per esempio
    ''' punta a una copia fissa, magari portatile, o ha argomenti sbagliati) la ricrea. Non crea le icone che non ci sono: se sono state tolte
    ''' restano tolte.
    ''' </summary>
    Public Shared Sub RiparaIconeSeInstallato()
        Try
            If Not EInstallato() Then Return
            For Each percorso As String In New String() {PercorsoIconaDesktop(), PercorsoIconaStart()}
                Try
                    If File.Exists(percorso) AndAlso Not CollegamentoDellAttivita(percorso) Then
                        If AttivitaPuntaQui() Then CreaIcona(percorso)
                    End If
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    Private Shared Sub CreaIcone()
        CreaIcona(PercorsoIconaDesktop())
        CreaIcona(PercorsoIconaStart())
        ' L'impostazione è della copia installata: se questo codice gira in una copia portatile (che lancia l'installazione) la sua
        ' configurazione non cambia. La copia installata la riceve dal profilo preparato (PreparaProfilo, in WinGabriel).
        Try
            If EInstallato() Then
                Dim config As New WGConfig()
                config.SalvaValore("crea_icona_desktop", "true")
            End If
        Catch
        End Try
    End Sub

    Private Shared Sub RimuoviIcone()
        For Each percorso As String In New String() {PercorsoIconaDesktop(), PercorsoIconaStart()}
            Try
                If File.Exists(percorso) Then File.Delete(percorso)
            Catch
            End Try
        Next
        Try
            If EInstallato() Then
                Dim config As New WGConfig()
                config.SalvaValore("crea_icona_desktop", "false")
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Installa l'integrazione: attività e icone. Serve l'amministratore: se non c'è, il programma si rilancia chiedendolo (la conferma di
    ''' Windows). 0 fatto, 1223 conferma negata, altro: errore.
    ''' </summary>
    Public Shared Function Installa() As Integer
        Return Installa(False)
    End Function

    ''' <summary>Con la copia il programma va in Programmi e attività e icone puntano lì.</summary>
    Public Shared Function Installa(copiaInProgrammi As Boolean) As Integer
        If Not AvvioAmministratore.Elevato() Then Return AvvioAmministratore.EseguiComeAmministratore("/integrazione:installa" & If(copiaInProgrammi, " /copia", ""))
        Try
            If copiaInProgrammi Then
                Dim destinazione As String = CartellaInstallazione()
                Dim esito As Integer = CopiaProgramma(destinazione)
                If esito <> 0 Then Return esito
                _eseguibileDaAvviare = Path.Combine(destinazione, Path.GetFileName(EseguibileCorrente()))
            End If
            If Not CreaAttivita() Then Return 2
            CreaIcone()
            Return 0
        Catch
            Return 1
        End Try
    End Function

    Public Shared Function Rimuovi() As Integer
        If Not AvvioAmministratore.Elevato() Then Return AvvioAmministratore.EseguiComeAmministratore("/integrazione:rimuovi")
        Try
            Dim uscita As String = ""
            EseguiConUscita("schtasks.exe", "/Delete /TN " & NomeAttivita & " /F", uscita)
            RimuoviIcone()
            Return 0
        Catch
            Return 1
        End Try
    End Function

    ''' <summary>
    ''' Se WinGabriel è stato rilanciato con /integrazione:installa o /integrazione:rimuovi, fa il lavoro e restituisce il codice di uscita;
    ''' altrimenti restituisce -1 e l'avvio prosegue normalmente.
    ''' </summary>
    Public Shared Function EseguiDaRigaDiComando() As Integer
        Try
            For Each argomento As String In Environment.GetCommandLineArgs()
                If String.Equals(argomento, "/integrazione:installa", StringComparison.OrdinalIgnoreCase) Then Return Installa(Array.Exists(Environment.GetCommandLineArgs(), Function(a) String.Equals(a, "/copia", StringComparison.OrdinalIgnoreCase)))
                If String.Equals(argomento, "/integrazione:rimuovi", StringComparison.OrdinalIgnoreCase) Then Return Rimuovi()
                If String.Equals(argomento, "/integrazione:disinstalla", StringComparison.OrdinalIgnoreCase) Then Return Disinstalla()
            Next
        Catch
            Return 1
        End Try
        Return -1
    End Function

    ''' <summary>Il menu "Installazione di WinGabriel" delle impostazioni.</summary>
    ' Vero se in Programmi\WinGabriel c'è una copia installata, o se l'attività che la avvia esiste ancora.
    Public Shared Function InstallatoNelSistema() As Boolean
        Try
            If File.Exists(Path.Combine(CartellaInstallazione(), Path.GetFileName(EseguibileCorrente()))) Then Return True
        Catch
        End Try
        Return Installata()
    End Function

    ' Chiude i WinGabriel in esecuzione da una cartella, tranne questo.
    Private Shared Sub ChiudiProcessiDaCartella(cartella As String)
        Dim proprio As Integer = Process.GetCurrentProcess().Id
        For Each p As Process In Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EseguibileCorrente()))
            Try
                If p.Id <> proprio AndAlso p.MainModule.FileName.StartsWith(cartella.TrimEnd("\"c) & "\", StringComparison.OrdinalIgnoreCase) Then
                    p.Kill()
                    p.WaitForExit(5000)
                End If
            Catch
            End Try
        Next
    End Sub

    ' Disinstalla: toglie l'attività e le icone e cancella la copia in Programmi. Se questo programma sta girando proprio da lì la cartella non si
    ' può cancellare subito: lo si fa dopo l'uscita (vedi CancellaCartellaInstallazioneDopoUscita). 0 fatto, 1223 conferma negata, altro: errore.
    Public Shared Function Disinstalla() As Integer
        If Not AvvioAmministratore.Elevato() Then Return AvvioAmministratore.EseguiComeAmministratore("/integrazione:disinstalla")
        Try
            Dim uscita As String = ""
            EseguiConUscita("schtasks.exe", "/Delete /TN " & NomeAttivita & " /F", uscita)
            RimuoviIcone()
            Dim cartella As String = CartellaInstallazione()
            If Directory.Exists(cartella) AndAlso Not EInstallato() Then
                ChiudiProcessiDaCartella(cartella)
                Try
                    Directory.Delete(cartella, True)
                Catch
                    Return 3
                End Try
            End If
            Return 0
        Catch
            Return 1
        End Try
    End Function

    ' Chiude WinGabriel e, a programma chiuso, cancella la cartella di installazione (un programma non può cancellare i propri file).
    Public Shared Sub CancellaCartellaInstallazioneDopoUscita()
        Try
            Dim info As New ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q """ & CartellaInstallazione() & """")
            info.UseShellExecute = False
            info.CreateNoWindow = True
            Process.Start(info)
        Catch
        End Try
        Application.Exit()
        Environment.Exit(0)
    End Sub

    Public Shared Function Esito(codice As Integer, comeInstallazione As Boolean) As String
        Select Case codice
            Case 0
                Return If(comeInstallazione, "Fatto. WinGabriel è installato: le icone sul desktop e nel menu Start lo avviano con i privilegi di amministratore, senza altre richieste.", "Fatto. WinGabriel è stato disinstallato: l'attività e le icone sono state tolte.")
            Case CodiceAnnullato
                Return "L'operazione con i privilegi di amministratore non è stata confermata: non è cambiato niente."
            Case 3
                Return "Non è stato possibile cancellare tutti i file di WinGabriel in Programmi, forse perché sono in uso. Chiudi WinGabriel e riprova."
            Case Else
                Return "L'operazione non è riuscita. Codice " & codice.ToString() & "."
        End Select
    End Function

End Class
