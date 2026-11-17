Imports System.ServiceProcess
Imports Microsoft.Win32
Imports WGLettoreSchermo

''' <summary>
''' L'installazione del lettore di schermo. Nel sistema copia i file in Programmi, registra il servizio per la schermata
''' di accesso, di blocco e del controllo account utente, e crea l'attività che lo avvia all'accesso. In uso portatile
''' lascia i file dov'erano e scrive solo l'avvio dell'utente. Quando il lettore entra nel sistema, se NVDA è registrato
''' sul controllo account utente viene tolto da lì.
''' </summary>
Friend Module Installazione

    Private Const NomeServizio As String = "WGLettoreServizio"
    Private Const NomeVoce As String = "WGLettore"
    Private Const ChiaveAvvio As String = "Software\Microsoft\Windows\CurrentVersion\Run"

    Friend Function ServizioInstallato() As Boolean
        Try
            Using servizio As New ServiceController(NomeServizio)
                Dim stato As ServiceControllerStatus = servizio.Status
                Return True
            End Using
        Catch exErrore1 As Exception
            Trace.WriteLine("Installazione: " & exErrore1.Message)
            Return False
        End Try
    End Function

    Friend Function CartellaDatiComune() As String
        Return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WGLettore")
    End Function

    Friend Sub CopiaImpostazioniPerSchermataDiAccesso()
        Try
            If Not System.IO.Directory.Exists(CartellaDatiComune()) Then Return
            ConfigLettore.EsportaPrefisso("lettore_schermo_", System.IO.Path.Combine(CartellaDatiComune(), "config.xml"))
        Catch exErrore2 As Exception
            Trace.WriteLine("Installazione: " & exErrore2.Message)
        End Try
    End Sub

    Friend Function CartellaInstallazione() As String
        Return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WGLettore")
    End Function

    Friend Function InstallatoInProgrammi() As Boolean
        Try
            Return System.IO.File.Exists(System.IO.Path.Combine(CartellaInstallazione(), "WGLettore.exe"))
        Catch exErrore0 As Exception
            Trace.WriteLine("Installazione: " & exErrore0.Message)
            Return False
        End Try
    End Function

    ' Con conAvvio a falso copia solo i file in Programmi: niente servizio, niente attività pianificata, il lettore non parte da solo.
    Friend Function InstallaServizio(Optional conAvvio As Boolean = True) As Integer
        If Not NativoApp.Elevato() Then Return RilanciaComeAmministratore(If(conAvvio, "/installa:sistema", "/installa:sistema-senzaavvio"))
        Try
            Dim origine As String = NativoApp.CartellaProgramma().TrimEnd(System.IO.Path.DirectorySeparatorChar)
            Dim destinazione As String = CartellaInstallazione()
            System.IO.Directory.CreateDirectory(destinazione)
            Dim servizio As String = System.IO.Path.Combine(destinazione, "WGLettoreServizio.exe")
            If System.IO.File.Exists(servizio) Then EseguiProgramma(servizio, "--uninstall")
            Dim eraAttivo As Boolean = ChiudiLettoreInstallato(destinazione)
            If Not String.Equals(origine, destinazione.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
                For Each nome As String In New String() {"WGLettore.exe", "WGLettore.exe.config", "WGLettoreSchermo.dll", "WGLettoreVoce.exe", "WGLettoreServizio.exe",
                                                         "WGAiutoLettore32.dll", "WGAiutoLettore64.dll", "WGAiutoLettoreCarica32.exe", "WGAiutoLettoreCarica64.exe", "WGEspeak.dll",
                                                         "Microsoft.Win32.Registry.dll", "System.Runtime.WindowsRuntime.dll", "System.Security.AccessControl.dll", "System.Security.Principal.Windows.dll"}
                    Dim percorso As String = System.IO.Path.Combine(origine, nome)
                    If System.IO.File.Exists(percorso) Then System.IO.File.Copy(percorso, System.IO.Path.Combine(destinazione, nome), True)
                Next
                For Each percorso As String In System.IO.Directory.GetFiles(origine, "NAudio*.dll")
                    System.IO.File.Copy(percorso, System.IO.Path.Combine(destinazione, System.IO.Path.GetFileName(percorso)), True)
                Next
                CopiaCartella(System.IO.Path.Combine(origine, "espeak"), System.IO.Path.Combine(destinazione, "espeak"))
                CopiaCartella(System.IO.Path.Combine(origine, "moduli"), System.IO.Path.Combine(destinazione, "moduli"))
            End If
            System.IO.Directory.CreateDirectory(CartellaDatiComune())
            Dim sicurezza As System.Security.AccessControl.DirectorySecurity = System.IO.Directory.GetAccessControl(CartellaDatiComune())
            sicurezza.AddAccessRule(New System.Security.AccessControl.FileSystemAccessRule(New System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, Nothing),
                                    System.Security.AccessControl.FileSystemRights.Modify,
                                    System.Security.AccessControl.InheritanceFlags.ContainerInherit Or System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                                    System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow))
            System.IO.Directory.SetAccessControl(CartellaDatiComune(), sicurezza)
            If Not conAvvio Then
                ConfigLettore.Scrivi("lettore_schermo_modalita", "sistema-senza-avvio")
                Return 0
            End If
            Dim esito As Integer = EseguiProgramma(servizio, "--install")
            If esito = 0 Then
                CreaAttivitaPianificata(System.Security.Principal.WindowsIdentity.GetCurrent().Name)
                RimuoviVoceDiAvvio()
                ConfigLettore.Scrivi("lettore_schermo_avvio_automatico", "true")
                ConfigLettore.Scrivi("lettore_schermo_modalita", "sistema")
                If TogliNvdaDalControlloAccount() Then Trace.WriteLine("Installazione: NVDA tolto dal controllo account utente.")
            End If
            If eraAttivo Then
                Try
                    Dim riavvio As New ProcessStartInfo(System.IO.Path.Combine(destinazione, "WGLettore.exe"), "/avvio")
                    riavvio.UseShellExecute = False
                    riavvio.WorkingDirectory = destinazione
                    Process.Start(riavvio)
                Catch exErrore3 As Exception
                    Trace.WriteLine("Installazione: " & exErrore3.Message)
                End Try
            End If
            Return esito
        Catch exErrore4 As Exception
            Trace.WriteLine("Installazione: " & exErrore4.Message)
            Return 1
        End Try
    End Function

    Private Sub CopiaCartella(origine As String, destinazione As String)
        Try
            If Not System.IO.Directory.Exists(origine) Then Return
            System.IO.Directory.CreateDirectory(destinazione)
            For Each file As String In System.IO.Directory.GetFiles(origine)
                System.IO.File.Copy(file, System.IO.Path.Combine(destinazione, System.IO.Path.GetFileName(file)), True)
            Next
            For Each cartella As String In System.IO.Directory.GetDirectories(origine)
                CopiaCartella(cartella, System.IO.Path.Combine(destinazione, System.IO.Path.GetFileName(cartella)))
            Next
        Catch exErrore16 As Exception
            Trace.WriteLine("Installazione: " & exErrore16.Message)
        End Try
    End Sub

    Friend Function DisinstallaServizio() As Integer
        If Not NativoApp.Elevato() Then Return RilanciaComeAmministratore("/disinstalla")
        Try
            Dim servizio As String = System.IO.Path.Combine(CartellaInstallazione(), "WGLettoreServizio.exe")
            Dim esito As Integer = 0
            If System.IO.File.Exists(servizio) Then esito = EseguiProgramma(servizio, "--uninstall")
            EseguiConUscita("schtasks.exe", "/Delete /TN " & NomeAttivita & " /F")
            ChiudiLettoreInstallato(CartellaInstallazione())
            Return esito
        Catch exErrore5 As Exception
            Trace.WriteLine("Installazione: " & exErrore5.Message)
            Return 1
        End Try
    End Function

    Private Function ChiudiLettoreInstallato(cartella As String) As Boolean
        Dim chiuso As Boolean = False
        Dim mio As Integer = Process.GetCurrentProcess().Id
        For Each p As Process In Process.GetProcessesByName("WGLettore")
            Try
                If p.Id = mio Then Continue For
                Dim percorso As String = p.MainModule.FileName
                If String.Equals(System.IO.Path.GetDirectoryName(percorso), cartella.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
                    p.Kill()
                    p.WaitForExit(3000)
                    chiuso = True
                End If
            Catch exErrore6 As Exception
                Trace.WriteLine("Installazione: " & exErrore6.Message)
            Finally
                p.Dispose()
            End Try
        Next
        Return chiuso
    End Function

    Private Function EseguiProgramma(programma As String, argomenti As String) As Integer
        Try
            Dim info As New ProcessStartInfo(programma, argomenti)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            Using processo As Process = Process.Start(info)
                If processo Is Nothing Then Return -1
                ' ExitCode prima che il processo sia uscito solleva InvalidOperationException.
                If Not processo.WaitForExit(30000) Then Return -1
                Return processo.ExitCode
            End Using
        Catch exErrore7 As Exception
            Trace.WriteLine("Installazione: " & exErrore7.Message)
            Return -1
        End Try
    End Function

    Private Function RilanciaComeAmministratore(argomenti As String) As Integer
        Try
            Dim info As New ProcessStartInfo(Application.ExecutablePath, argomenti)
            info.UseShellExecute = True
            info.Verb = "runas"
            Using processo As Process = Process.Start(info)
                If processo Is Nothing Then Return 1223
                processo.WaitForExit()
                Return processo.ExitCode
            End Using
        Catch ex As Win32Exception
            Return 1223
        Catch exErrore8 As Exception
            Trace.WriteLine("Installazione: " & exErrore8.Message)
            Return 1
        End Try
    End Function

    Private Const NomeAttivita As String = "WGLettore"

    Private Function EseguiConUscita(programma As String, argomenti As String) As Integer
        Try
            Dim info As New ProcessStartInfo(programma, argomenti)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            info.RedirectStandardOutput = True
            info.RedirectStandardError = True
            Using processo As Process = Process.Start(info)
                If processo Is Nothing Then Return -1
                ' I due flussi vanno svuotati insieme: leggerli in fila blocca il processo
                ' quando il buffer dell'altro si riempie, e l'attesa non torna più.
                Dim letturaUscita As System.Threading.Tasks.Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As System.Threading.Tasks.Task(Of String) = processo.StandardError.ReadToEndAsync()
                If Not processo.WaitForExit(30000) Then
                    Try
                        processo.Kill()
                    Catch exArresto As Exception
                        Trace.WriteLine("Installazione: " & exArresto.Message)
                    End Try
                    processo.WaitForExit(5000)
                End If
                Try
                    System.Threading.Tasks.Task.WaitAll(New System.Threading.Tasks.Task() {letturaUscita, letturaErrori}, 5000)
                Catch exLettura As Exception
                    Trace.WriteLine("Installazione: " & exLettura.Message)
                End Try
                If Not processo.HasExited Then Return -1
                Return processo.ExitCode
            End Using
        Catch exErrore9 As Exception
            Trace.WriteLine("Installazione: " & exErrore9.Message)
            Return -1
        End Try
    End Function

    Friend Function AttivitaPianificataEsiste() As Boolean
        Return EseguiConUscita("schtasks.exe", "/Query /TN " & NomeAttivita) = 0
    End Function

    Private Sub CreaAttivitaPianificata(utente As String)
        Dim xml As New System.Text.StringBuilder()
        xml.AppendLine("<?xml version=""1.0"" encoding=""UTF-16""?>")
        xml.AppendLine("<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">")
        xml.AppendLine("  <RegistrationInfo><Description>Avvia il lettore di schermo quando si accede a Windows.</Description></RegistrationInfo>")
        xml.AppendLine("  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" & Escape(utente) & "</UserId></LogonTrigger></Triggers>")
        xml.AppendLine("  <Principals><Principal id=""Author""><UserId>" & Escape(utente) & "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>")
        xml.AppendLine("  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled><AllowStartOnDemand>true</AllowStartOnDemand></Settings>")
        xml.AppendLine("  <Actions Context=""Author""><Exec><Command>" & Escape(System.IO.Path.Combine(CartellaInstallazione(), "WGLettore.exe")) & "</Command><Arguments>/avvio</Arguments></Exec></Actions>")
        xml.AppendLine("</Task>")
        Dim file As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wglettore-attivita-" & Guid.NewGuid().ToString("N") & ".xml")
        System.IO.File.WriteAllText(file, xml.ToString(), System.Text.Encoding.Unicode)
        Try
            EseguiConUscita("schtasks.exe", "/Create /TN " & NomeAttivita & " /XML """ & file & """ /F")
        Finally
            Try
                System.IO.File.Delete(file)
            Catch exErrore10 As Exception
                Trace.WriteLine("Installazione: " & exErrore10.Message)
            End Try
        End Try
    End Sub

    Private Function Escape(testo As String) As String
        Return System.Security.SecurityElement.Escape(testo)
    End Function

    Private Sub RimuoviVoceDiAvvio()
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.CreateSubKey(ChiaveAvvio)
                chiave.DeleteValue(NomeVoce, False)
            End Using
        Catch exErrore11 As Exception
            Trace.WriteLine("Installazione: " & exErrore11.Message)
        End Try
    End Sub

    Friend Function AvvioAutomaticoAttivo() As Boolean
        Try
            If AttivitaPianificataEsiste() Then
                Return Not String.Equals(ConfigLettore.Leggi("lettore_schermo_avvio_automatico", "true").Trim(), "false", StringComparison.OrdinalIgnoreCase)
            End If
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey(ChiaveAvvio)
                Return chiave IsNot Nothing AndAlso chiave.GetValue(NomeVoce) IsNot Nothing
            End Using
        Catch exErrore12 As Exception
            Trace.WriteLine("Installazione: " & exErrore12.Message)
            Return False
        End Try
    End Function

    Friend Function CambiaAvvioConAttivita(attivo As Boolean) As Integer
        If Not NativoApp.Elevato() Then Return RilanciaComeAmministratore(If(attivo, "/avvioauto:on", "/avvioauto:off"))
        Dim esito As Integer = EseguiConUscita("schtasks.exe", "/Change /TN " & NomeAttivita & If(attivo, " /ENABLE", " /DISABLE"))
        If esito = 0 Then ConfigLettore.Scrivi("lettore_schermo_avvio_automatico", If(attivo, "true", "false"))
        Return esito
    End Function

    Friend Sub ImpostaAvvioAutomatico(attivo As Boolean)
        Try
            If AttivitaPianificataEsiste() Then
                CambiaAvvioConAttivita(attivo)
                Return
            End If
            Using chiave As RegistryKey = Registry.CurrentUser.CreateSubKey(ChiaveAvvio)
                If attivo Then
                    chiave.SetValue(NomeVoce, """" & Application.ExecutablePath & """ /avvio")
                Else
                    chiave.DeleteValue(NomeVoce, False)
                End If
            End Using
            ConfigLettore.Scrivi("lettore_schermo_avvio_automatico", If(attivo, "true", "false"))
        Catch exErrore13 As Exception
            Trace.WriteLine("Installazione: " & exErrore13.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Uso portatile: non copia i file e non registra il servizio. Il lettore resta nella cartella da cui è stato avviato
    ''' e parte all'accesso dell'utente. Non va sul controllo account utente.
    ''' </summary>
    Friend Function InstallaPortatile() As Integer
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.CreateSubKey(ChiaveAvvio)
                chiave.SetValue(NomeVoce, """" & Application.ExecutablePath & """ /avvio")
            End Using
            ConfigLettore.Scrivi("lettore_schermo_modalita", "portatile")
            ConfigLettore.Scrivi("lettore_schermo_avvio_automatico", "true")
            Return 0
        Catch exErrore14 As Exception
            Trace.WriteLine("Installazione: " & exErrore14.Message)
            Return 1
        End Try
    End Function

    ' NVDA si registra fra gli strumenti di accessibilità di Windows. La voce Configuration della chiave di sistema
    ' elenca quelli che partono sul desktop sicuro: è lo stesso desktop del controllo account utente (consent.exe).
    ' I nomi usati da NVDA sono nvda_nvda_v1 e, nelle copie più vecchie, nvda.
    Private ReadOnly NomiNvdaAccessibilita As String() = {"nvda_nvda_v1", "nvda"}
    Private Const ChiaveAccessibilita As String = "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility"

    ''' <summary>
    ''' Se NVDA è registrato sul controllo account utente, lo toglie da lì. Non disinstalla NVDA e non chiude il processo.
    ''' Va chiamata con i privilegi di amministratore. Restituisce vero se ha cambiato il registro.
    ''' </summary>
    Friend Function TogliNvdaDalControlloAccount() As Boolean
        If Not NativoApp.Elevato() Then Return False
        Dim tolto As Boolean = TogliNvdaDaVista(RegistryView.Registry64)
        tolto = TogliNvdaDaVista(RegistryView.Registry32) Or tolto
        Return tolto
    End Function

    Private Function TogliNvdaDaVista(vista As RegistryView) As Boolean
        Dim tolto As Boolean = False
        Try
            Using base As RegistryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, vista)
                Using chiave As RegistryKey = base.OpenSubKey(ChiaveAccessibilita, True)
                    If chiave IsNot Nothing Then
                        Dim testo As String = TryCast(chiave.GetValue("Configuration"), String)
                        If Not String.IsNullOrEmpty(testo) Then
                            Dim tenuti As New List(Of String)
                            Dim cambiato As Boolean = False
                            For Each parte As String In testo.Split(","c)
                                Dim nome As String = parte.Trim()
                                If nome = "" Then Continue For
                                If NomeDiNvda(nome) Then
                                    cambiato = True
                                Else
                                    tenuti.Add(nome)
                                End If
                            Next
                            If cambiato Then
                                chiave.SetValue("Configuration", String.Join(",", tenuti.ToArray()), RegistryValueKind.String)
                                tolto = True
                            End If
                        End If
                    End If
                End Using
                For Each noto As String In NomiNvdaAccessibilita
                    Using strumento As RegistryKey = base.OpenSubKey(ChiaveAccessibilita & "\ATs\" & noto, True)
                        If strumento Is Nothing Then Continue For
                        Dim attuale As String = TryCast(strumento.GetValue("SecureDesktopAccommodation"), String)
                        If Not String.Equals(attuale, "none", StringComparison.OrdinalIgnoreCase) Then
                            strumento.SetValue("SecureDesktopAccommodation", "none", RegistryValueKind.String)
                            tolto = True
                        End If
                    End Using
                Next
            End Using
        Catch exErrore15 As Exception
            Trace.WriteLine("Installazione: " & exErrore15.Message)
        End Try
        Return tolto
    End Function

    Private Function NomeDiNvda(nome As String) As Boolean
        For Each noto As String In NomiNvdaAccessibilita
            If String.Equals(nome, noto, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

End Module
