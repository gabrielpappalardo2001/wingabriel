' ============================================================================================
' WGRadio - accendere e spegnere Wi-Fi e Bluetooth con le API di Windows (Windows.Devices.Radios)
' ============================================================================================
'
' Sono le stesse API che usano le impostazioni rapide di Windows: non servono diritti di
' amministratore. Windows può però negare l'accesso alle radio (autorizzazioni delle app): in quel
' caso ConsentiAccesso scrive il consenso dell'utente nel registro (HKCU, senza amministratore), come
' farebbe la pagina di Windows che la sintesi di WinGabriel non legge, e si riprova.
'
'   Dim acceso As Boolean? = Await WGRadio.AccesaAsync(RadioKind.WiFi)   ' Nothing se non c'è
'   Dim errore As String = Await WGRadio.ImpostaAsync(RadioKind.Bluetooth, True)
' ============================================================================================
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.Win32
Imports Windows.Devices.Radios
Imports wgcontrols

Public NotInheritable Class WGRadio

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Private Shared Async Function TrovaAsync(tipo As RadioKind) As Task(Of Radio)
        Try
            Await Radio.RequestAccessAsync()
            Dim elenco As IReadOnlyList(Of Radio) = Await Radio.GetRadiosAsync()
            Return elenco.FirstOrDefault(Function(r) r.Kind = tipo)
        Catch ex As Exception
            log.ScriviLog("WGRadio.TrovaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Vero se la radio è accesa, falso se spenta, Nothing se il computer non ce l'ha.</summary>
    Public Shared Async Function AccesaAsync(tipo As RadioKind) As Task(Of Boolean?)
        Dim r As Radio = Await TrovaAsync(tipo)
        If r Is Nothing Then Return Nothing
        Return r.State = RadioState.On
    End Function

    ''' <summary>Accende o spegne la radio; restituisce "" se riuscito, altrimenti il motivo.</summary>
    Public Shared Async Function ImpostaAsync(tipo As RadioKind, accesa As Boolean) As Task(Of String)
        Try
            Dim r As Radio = Await TrovaAsync(tipo)
            If r Is Nothing Then Return If(tipo = RadioKind.Bluetooth, "Questo computer non ha il Bluetooth", "Questo computer non ha il Wi-Fi")
            Dim esito As RadioAccessStatus = Await r.SetStateAsync(If(accesa, RadioState.On, RadioState.Off))
            Select Case esito
                Case RadioAccessStatus.Allowed
                    Return ""
                Case RadioAccessStatus.DeniedByUser
                    Return "Windows non permette a WinGabriel di cambiare Wi-Fi e Bluetooth: Privacy"
                Case Else
                    Return "Windows non ha permesso di cambiare lo stato della radio"
            End Select
        Catch ex As Exception
            log.ScriviLog("WGRadio.ImpostaAsync: " & ex.Message)
            Return "Errore: " & ex.Message
        End Try
    End Function

    ''' <summary>
    ''' Consente a WinGabriel di accendere e spegnere le radio: consenso dell'utente nel registro (HKCU),
    ''' per tutte le app desktop e per WinGabriel. Vero se scritto.
    ''' </summary>
    Public Shared Function ConsentiAccesso() As Boolean
        Try
            Const chiave As String = "Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\radios"
            For Each percorso As String In {chiave, chiave & "\NonPackaged", chiave & "\NonPackaged\" & System.Windows.Forms.Application.ExecutablePath.Replace("\", "#")}
                Using k As RegistryKey = Registry.CurrentUser.CreateSubKey(percorso)
                    k.SetValue("Value", "Allow", RegistryValueKind.String)
                End Using
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("WGRadio.ConsentiAccesso: " & ex.Message)
            Return False
        End Try
    End Function

End Class
