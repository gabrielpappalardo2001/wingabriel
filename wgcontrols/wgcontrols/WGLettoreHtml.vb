' ============================================================================================
' WGLettoreHtml - pagine HTML lette nel browser di WinGabriel ridotto a lettore
' ============================================================================================
'
' Chi deve mostrare un contenuto HTML (i messaggi della posta scritti con la grafica) non conosce il
' browser, che sta in una libreria a parte: prepara la richiesta con il documento, le sue azioni e
' cosa fare alla chiusura, e il programma principale collega Apri al browser.
'
'   Dim r As New WGRichiestaLettoreHtml With {.Titolo = oggetto, .Html = documento}
'   r.Opzioni.Add(Tuple.Create(Of String, Action)("Rispondi", AddressOf Rispondi))
'   r.Chiusura = Sub() _lettore = Nothing
'   _lettore = WGLettoreHtml.Mostra(r)      ' Nothing se il browser non c'è: si usa il campo di testo
'   _lettore.RichiediChiusura()             ' per chiuderlo da fuori
' ============================================================================================

''' <summary>Il documento da leggere nel lettore, con le azioni che compaiono in cima al menu delle opzioni.</summary>
Public Class WGRichiestaLettoreHtml
    ''' <summary>Titolo annunciato all'apertura e nel ritorno alla pagina.</summary>
    Public Property Titolo As String = ""
    ''' <summary>Documento HTML completo; le risorse in rete non vengono caricate, i collegamenti si aprono fuori.</summary>
    Public Property Html As String = ""
    ''' <summary>Azioni di chi apre il lettore, nell'ordine in cui compaiono nel menu delle opzioni.</summary>
    Public Property Opzioni As New List(Of Tuple(Of String, Action))()
    ''' <summary>Chiamata quando il lettore si chiude, con Esc o da fuori.</summary>
    Public Property Chiusura As Action
End Class

Public NotInheritable Class WGLettoreHtml

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>Apre il lettore e lo restituisce, oppure Nothing se non si può aprire. Lo collega il programma principale.</summary>
    Public Shared Property Apri As Func(Of WGRichiestaLettoreHtml, IWGChiudibile)

    Public Shared ReadOnly Property Disponibile As Boolean
        Get
            Return Apri IsNot Nothing
        End Get
    End Property

    ''' <summary>Apre il lettore; Nothing se il browser non è disponibile o non è riuscito ad aprirsi.</summary>
    Public Shared Function Mostra(richiesta As WGRichiestaLettoreHtml) As IWGChiudibile
        Try
            Dim apertura As Func(Of WGRichiestaLettoreHtml, IWGChiudibile) = Apri
            If apertura Is Nothing OrElse richiesta Is Nothing OrElse String.IsNullOrWhiteSpace(richiesta.Html) Then Return Nothing
            Return apertura(richiesta)
        Catch ex As Exception
            log.ScriviLog("WGLettoreHtml.Mostra: " & ex.Message)
            Return Nothing
        End Try
    End Function

End Class
