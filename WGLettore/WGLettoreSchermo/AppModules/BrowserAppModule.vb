Imports System.Text.RegularExpressions

' Modulo applicativo specifico per la gestione dei browser web e la lettura dei titoli delle pagine.
Friend NotInheritable Class BrowserAppModule
    Inherits AppModuleLettore

    Private Shared ReadOnly _regexTitolo As New Regex("\s[-\u2013\u2014]\s(Google Chrome|Mozilla Firefox|Microsoft\W+Edge|Brave|Opera|Vivaldi|Chromium|Waterfox|LibreWolf).*$", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    Public Sub New(lettore As WGLettoreSchermo, nomeApp As String)
        MyBase.New(lettore, nomeApp)
    End Sub

    Public Overrides ReadOnly Property EApplicazioneBrowser As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overrides Function TitoloPagina(titolo As String) As String
        Try
            Return _regexTitolo.Replace(If(titolo, ""), "").Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("BrowserAppModule.TitoloPagina", exAtteso)
            Return If(titolo, "")
        End Try
    End Function

End Class
