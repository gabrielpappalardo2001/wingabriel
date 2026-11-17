''' <summary>
''' La voce che il lettore di schermo usa per parlare. Il programma che ospita il lettore fornisce l'implementazione:
''' la sintesi di WinGabriel (wgcontrols e WGSapi) se sta nella stessa cartella, altrimenti il motore vocale proprio del lettore.
''' </summary>
Public Interface IVoceLettore

    Event OnFineLettura()
    Event OnStatoParlatoCambiato(staParlando As Boolean)

    ReadOnly Property StaParlando As Boolean

    ''' <param name="priorita">"Normale" accoda dopo quello che sta dicendo, "Alta" interrompe.</param>
    Sub LeggiTesto(testo As String, Optional priorita As String = "Normale", Optional escludiPunteggiatura As Boolean = False)

    Sub Ferma()

End Interface

Public NotInheritable Class VoceLettore

    Private Sub New()
    End Sub

    Public Shared Property Fornitore As Func(Of IVoceLettore)

    Friend Shared Function Corrente() As IVoceLettore
        Dim f As Func(Of IVoceLettore) = Fornitore
        If f Is Nothing Then Return Nothing
        Return f()
    End Function

End Class
