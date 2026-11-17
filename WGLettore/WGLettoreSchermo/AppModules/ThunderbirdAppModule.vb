Imports UIAutomationClient

' Modulo di supporto per il programma di posta Thunderbird con gestione del fuoco e degli avvisi vocali.
Friend NotInheritable Class ThunderbirdAppModule
    Inherits AppModuleLettore

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "thunderbird")
    End Sub

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return testoBase
            If Lettore.DescrittoreApp.TipoControllo(elemento) <> DescrittoreElementi.ControlloDocumento Then Return testoBase
            Dim valore As Object = elemento.GetCurrentPropertyValue(30100)
            If valore Is Nothing OrElse (Convert.ToInt64(valore) And &H800L) = 0L Then Return testoBase
            Dim barra As String = Lettore.BarraStatoLettore()
            Return If(barra = "", "occupato", "occupato, " & barra)
        Catch ex As Exception
            Lettore.ScriviLogApp("ThunderbirdAppModule.AllFocus: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Public Overrides Function AllAvviso(evento As EventoLettore) As Boolean
        Try
            If Lettore.UltimoElementoApp Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return False
            If Lettore.UltimoInAlberoApp = 0 Then Lettore.UltimoInAlberoApp = If(Lettore.NelControlloCompositoApp(Lettore.UltimoElementoApp), 1, 2)
            Return Lettore.UltimoInAlberoApp = 1
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ThunderbirdAppModule.AllAvviso", exAtteso)
            Return False
        End Try
    End Function

End Class
