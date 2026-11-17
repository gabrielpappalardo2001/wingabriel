' ============================================================================================
' WGPaginaNascosta - leggere una pagina con il browser di WinGabriel senza mostrarla
' ============================================================================================
'
' Chi non conosce il browser (per esempio YouTube) può farsi leggere una pagina dal browser di
' WinGabriel: la pagina si apre in una scheda che non si vede, con lo stesso profilo delle schede
' normali, quindi con gli stessi accessi ai siti. Sulla pagina gira uno script, e qui torna il suo
' risultato. Gli accessi restano nel browser: da qui non escono cookie né password.
'
'   If Await WGPaginaNascosta.HaCookieAsync("https://www.youtube.com", {"SAPISID"}) Then ...
'   Dim json As String = Await WGPaginaNascosta.EseguiAsync(indirizzo, script)
'
' Lo script è un'espressione JavaScript che dà una stringa, anche dopo un'attesa (una promessa).
' I due agganci li imposta il programma principale, che conosce il browser (WGPagineNascoste).
' ============================================================================================
Imports System.Threading.Tasks

Public NotInheritable Class WGPaginaNascosta

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>Indirizzo e script: il risultato dello script, Nothing se la pagina non si è potuta leggere.</summary>
    Public Shared Property Esegui As Func(Of String, String, Task(Of String))

    ''' <summary>Indirizzo del sito e nomi dei cookie: vero se il browser ne ha almeno uno, non scaduto.</summary>
    Public Shared Property HaCookie As Func(Of String, String(), Task(Of Boolean))

    Public Shared ReadOnly Property Disponibile As Boolean
        Get
            Return Esegui IsNot Nothing AndAlso HaCookie IsNot Nothing
        End Get
    End Property

    Public Shared Async Function EseguiAsync(indirizzo As String, script As String) As Task(Of String)
        Try
            If Esegui Is Nothing Then Return Nothing
            Return Await Esegui.Invoke(indirizzo, script)
        Catch ex As Exception
            log.ScriviLog("WGPaginaNascosta.EseguiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Async Function HaCookieAsync(indirizzo As String, nomi As String()) As Task(Of Boolean)
        Try
            If HaCookie Is Nothing Then Return False
            Return Await HaCookie.Invoke(indirizzo, nomi)
        Catch ex As Exception
            log.ScriviLog("WGPaginaNascosta.HaCookieAsync: " & ex.Message)
            Return False
        End Try
    End Function

End Class
