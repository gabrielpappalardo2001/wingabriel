' ============================================================================================
' WGModalitaTablet - sa se il computer è usato come tablet e avvisa quando cambia
' ============================================================================================
'
' Sui convertibili Windows dice se la tastiera è staccata o ripiegata (modalità slate):
' GetSystemMetrics(SM_CONVERTIBLESLATEMODE) vale 0 in modalità tablet. Il valore ha senso solo
' se il dispositivo è convertibile, cosa che Windows scrive nel registro (ConvertibleSlateMode in
' PriorityControl). Sui computer non convertibili non c'è un modo via API: vale come tablet il
' Touk attivo, cioè uno schermo touch in uso.
' Lo stato viene controllato ogni secondo; Cambiata avvisa del passaggio (sul thread dell'interfaccia).
' ============================================================================================
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.Win32

Public NotInheritable Class WGModalitaTablet

    Private Const SM_CONVERTIBLESLATEMODE As Integer = &H2003
    Private Const ChiaveConvertibile As String = "SYSTEM\CurrentControlSet\Control\PriorityControl"

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    Private Shared ReadOnly log As New Logger()
    Private Shared WithEvents _timer As Timer
    Private Shared _ultimoStato As Boolean
    Private Shared _convertibile As Boolean?

    ''' <summary>Passaggio da o verso la modalità tablet; il parametro dice se ora si è in modalità tablet.</summary>
    Public Shared Event Cambiata(tablet As Boolean)

    Private Sub New()
    End Sub

    Private Shared Function Convertibile() As Boolean
        If _convertibile.HasValue Then Return _convertibile.Value
        Try
            Using chiave As RegistryKey = Registry.LocalMachine.OpenSubKey(ChiaveConvertibile)
                _convertibile = chiave IsNot Nothing AndAlso chiave.GetValue("ConvertibleSlateMode") IsNot Nothing
            End Using
        Catch ex As Exception
            log.ScriviLog("WGModalitaTablet.Convertibile: " & ex.Message)
            _convertibile = False
        End Try
        Return _convertibile.Value
    End Function

    ''' <summary>Vero se il computer è usato come tablet.</summary>
    Public Shared Function Attiva() As Boolean
        Try
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk Is Nothing OrElse Not touk.TouchDisponibile Then Return False
            If Convertibile() Then Return GetSystemMetrics(SM_CONVERTIBLESLATEMODE) = 0
            Return touk.TouchAttivo
        Catch ex As Exception
            log.ScriviLog("WGModalitaTablet.Attiva: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Comincia a sorvegliare i passaggi; va chiamata dal thread dell'interfaccia.</summary>
    Public Shared Sub AvviaSorveglianza()
        Try
            If _timer IsNot Nothing Then Return
            _ultimoStato = Attiva()
            _timer = New Timer() With {.Interval = 1000}
            _timer.Start()
        Catch ex As Exception
            log.ScriviLog("WGModalitaTablet.AvviaSorveglianza: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub _timer_Tick(sender As Object, e As EventArgs) Handles _timer.Tick
        Try
            Dim stato As Boolean = Attiva()
            If stato = _ultimoStato Then Return
            _ultimoStato = stato
            RaiseEvent Cambiata(stato)
        Catch ex As Exception
            log.ScriviLog("WGModalitaTablet.Tick: " & ex.Message)
        End Try
    End Sub

End Class
