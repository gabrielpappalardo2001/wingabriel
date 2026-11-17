' ============================================================================================
' WGPosizione - il permesso Posizione di Windows, attivato da WinGabriel senza passare dalle impostazioni
' ============================================================================================
'
' Windows 11 mostra le reti Wi-Fi solo ai programmi con il permesso Posizione. La pagina delle
' impostazioni è scomoda per chi non vede e, se un criterio di gruppo ha spento la posizione (lo fanno
' molti programmi "per la privacy" o che alleggeriscono Windows), non si può nemmeno cambiare da lì.
' Quando l'utente lo sceglie, WinGabriel mette a posto tutto quello che serve:
'   senza diritti di amministratore (HKCU, ConsentStore\location):
'     - consenso dell'utente, app desktop e WinGabriel stesso = Allow;
'   con la conferma dell'amministratore (UAC), in un solo passaggio e solo se serve:
'     - criterio di gruppo LocationAndSensors: DisableLocation e DisableWindowsLocationProvider tolti;
'     - Servizi di localizzazione del computer (HKLM, ConsentStore\location) = Allow;
'     - sensore di posizione (Sensor\Overrides, SensorPermissionState) = 1;
'     - servizio di geolocalizzazione (lfsvc) avviato e riavviato, perché legga le impostazioni nuove.
' Se il criterio viene da un'organizzazione (computer di lavoro in dominio), Windows può rimetterlo.
' ============================================================================================
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.Win32
Imports wgcontrols

Public NotInheritable Class WGPosizione

    Private Const Chiave As String = "Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location"
    Private Const ChiaveCriterio As String = "SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors"
    Private Const ChiaveSensore As String = "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}"
    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Private Shared Function LeggiValore(radice As RegistryKey, percorso As String, nome As String) As Object
        Try
            Using k As RegistryKey = radice.OpenSubKey(percorso)
                If k Is Nothing Then Return Nothing
                Return k.GetValue(nome, Nothing)
            End Using
        Catch ex As Exception
            log.ScriviLog("WGPosizione.LeggiValore " & percorso & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub Scrivi(radice As RegistryKey, percorso As String, valore As String)
        Using k As RegistryKey = radice.CreateSubKey(percorso)
            k.SetValue("Value", valore, RegistryValueKind.String)
        End Using
    End Sub

    ' Nome della chiave di WinGabriel sotto NonPackaged: il percorso dell'eseguibile con # al posto di \.
    Private Shared Function ChiaveApplicazione() As String
        Return Chiave & "\NonPackaged\" & Application.ExecutablePath.Replace("\", "#")
    End Function

    ''' <summary>Un criterio di gruppo spegne la posizione per tutto il computer.</summary>
    Public Shared ReadOnly Property BloccataDaCriterio As Boolean
        Get
            Return Convert.ToString(LeggiValore(Registry.LocalMachine, ChiaveCriterio, "DisableLocation")) = "1" OrElse
                   Convert.ToString(LeggiValore(Registry.LocalMachine, ChiaveCriterio, "DisableWindowsLocationProvider")) = "1"
        End Get
    End Property

    ''' <summary>Vero se serve la conferma dell'amministratore: criterio, interruttore del computer o sensore spenti.</summary>
    Public Shared ReadOnly Property ServeAmministratore As Boolean
        Get
            If BloccataDaCriterio Then Return True
            If String.Equals(Convert.ToString(LeggiValore(Registry.LocalMachine, Chiave, "Value")), "Deny", StringComparison.OrdinalIgnoreCase) Then Return True
            Return Convert.ToString(LeggiValore(Registry.LocalMachine, ChiaveSensore, "SensorPermissionState")) = "0"
        End Get
    End Property

    ''' <summary>
    ''' Attiva la posizione per WinGabriel. Restituisce "" se riuscito, altrimenti il motivo.
    ''' Se serve, chiede una sola volta la conferma dell'amministratore.
    ''' </summary>
    Public Shared Async Function ConsentiAsync() As Task(Of String)
        Try
            ' Parte dell'utente: nessun diritto particolare.
            Scrivi(Registry.CurrentUser, Chiave, "Allow")
            Scrivi(Registry.CurrentUser, Chiave & "\NonPackaged", "Allow")
            Scrivi(Registry.CurrentUser, ChiaveApplicazione(), "Allow")
            If Not ServeAmministratore Then Return ""
            Dim riuscito As Boolean = Await Task.Run(Function() EseguiScriptAmministratore(ScriptComputer()))
            If Not riuscito Then Return "la conferma dell'amministratore non è stata data, oppure Windows non ha accettato il cambiamento"
            If BloccataDaCriterio Then Return "un criterio di gruppo continua a spegnere la posizione: probabilmente lo impone l'organizzazione che gestisce il computer"
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGPosizione.ConsentiAsync: " & ex.Message)
            Return ex.Message
        End Try
    End Function

    ' Tutto quello che richiede l'amministratore, in un solo script: una sola conferma per l'utente.
    Private Shared Function ScriptComputer() As String
        Dim s As New StringBuilder()
        s.AppendLine("$ErrorActionPreference = 'SilentlyContinue'")
        s.AppendLine("$criterio = 'HKLM:\" & ChiaveCriterio & "'")
        s.AppendLine("if (Test-Path $criterio) { Remove-ItemProperty -Path $criterio -Name DisableLocation, DisableWindowsLocationProvider -Force }")
        s.AppendLine("$posizione = 'HKLM:\" & Chiave & "'")
        s.AppendLine("New-Item -Path $posizione -Force | Out-Null")
        s.AppendLine("Set-ItemProperty -Path $posizione -Name Value -Value 'Allow' -Type String")
        s.AppendLine("$sensore = 'HKLM:\" & ChiaveSensore & "'")
        s.AppendLine("New-Item -Path $sensore -Force | Out-Null")
        s.AppendLine("Set-ItemProperty -Path $sensore -Name SensorPermissionState -Value 1 -Type DWord")
        s.AppendLine("$configurazione = 'HKLM:\SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration'")
        s.AppendLine("New-Item -Path $configurazione -Force | Out-Null")
        s.AppendLine("Set-ItemProperty -Path $configurazione -Name Status -Value 1 -Type DWord")
        s.AppendLine("Set-Service -Name lfsvc -StartupType Manual")
        s.AppendLine("Restart-Service -Name lfsvc -Force")
        s.AppendLine("if (Test-Path $criterio) { $v = Get-ItemProperty -Path $criterio -Name DisableLocation; if ($v -and $v.DisableLocation -eq 1) { exit 2 } }")
        s.AppendLine("exit 0")
        Return s.ToString()
    End Function

    ' Lancia PowerShell come amministratore (Windows chiede la conferma) e aspetta che finisca.
    Private Shared Function EseguiScriptAmministratore(script As String) As Boolean
        Try
            Dim codificato As String = Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
            Dim avvio As New ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " & codificato) With {
                .UseShellExecute = True, .Verb = "runas", .WindowStyle = ProcessWindowStyle.Hidden}
            Using processo As Process = Process.Start(avvio)
                If processo Is Nothing Then Return False
                processo.WaitForExit(90000)
                Return processo.HasExited AndAlso processo.ExitCode = 0
            End Using
        Catch ex As System.ComponentModel.Win32Exception
            ' La conferma dell'amministratore è stata negata.
            log.ScriviLog("WGPosizione.EseguiScriptAmministratore annullato: " & ex.Message)
            Return False
        Catch ex As Exception
            log.ScriviLog("WGPosizione.EseguiScriptAmministratore: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Toglie il permesso Posizione a WinGabriel (solo a lui).</summary>
    Public Shared Function Revoca() As Boolean
        Try
            Scrivi(Registry.CurrentUser, ChiaveApplicazione(), "Deny")
            Return True
        Catch ex As Exception
            log.ScriviLog("WGPosizione.Revoca: " & ex.Message)
            Return False
        End Try
    End Function

End Class
