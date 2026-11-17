Imports System.Runtime.InteropServices

Friend Module ComLettore

    Public Sub Rilascia(oggetto As Object)
        If oggetto Is Nothing Then Return
        Try
            If Marshal.IsComObject(oggetto) Then Marshal.ReleaseComObject(oggetto)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ComLettore.Rilascia", exAtteso)
        End Try
    End Sub

End Module
