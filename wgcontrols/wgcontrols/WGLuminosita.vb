' ============================================================================================
' WGLuminosita - luminosità dello schermo del portatile o del tablet
' ============================================================================================
'
' Con WMI (root\WMI, WmiMonitorBrightness e WmiMonitorBrightnessMethods), senza diritti di
' amministratore e senza finestre di Windows. Funziona sugli schermi integrati (portatili, tablet);
' i monitor esterni di solito non la permettono: allora Disponibile è falso.
'
'   Dim valore As Integer = WGLuminosita.Leggi()     ' -1 se non regolabile
'   WGLuminosita.Imposta(70)
' ============================================================================================
Imports System.Management

Public NotInheritable Class WGLuminosita

    Private Shared ReadOnly log As New Logger()
    Private Shared _disponibile As Boolean? = Nothing

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property Disponibile As Boolean
        Get
            If Not _disponibile.HasValue Then Leggi()
            Return _disponibile.GetValueOrDefault()
        End Get
    End Property

    ''' <summary>Luminosità in percentuale; -1 se lo schermo non la fa regolare.</summary>
    Public Shared Function Leggi() As Integer
        Try
            Using ricerca As New ManagementObjectSearcher("root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness")
                For Each schermo As ManagementObject In ricerca.Get()
                    Dim valore As Integer = Convert.ToInt32(schermo("CurrentBrightness"))
                    _disponibile = True
                    Return valore
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGLuminosita.Leggi: " & ex.Message)
        End Try
        _disponibile = False
        Return -1
    End Function

    ''' <summary>Imposta la luminosità (da 0 a 100); vero se riuscito.</summary>
    Public Shared Function Imposta(percentuale As Integer) As Boolean
        Try
            Dim valore As Byte = CByte(Math.Max(0, Math.Min(100, percentuale)))
            Using ricerca As New ManagementObjectSearcher("root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods")
                For Each schermo As ManagementObject In ricerca.Get()
                    schermo.InvokeMethod("WmiSetBrightness", New Object() {CUInt(1), valore})
                    Return True
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGLuminosita.Imposta: " & ex.Message)
        End Try
        Return False
    End Function

End Class
