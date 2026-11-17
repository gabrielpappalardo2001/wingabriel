Imports System.Collections.Generic
Imports UIAutomationClient

' Gestione del terminale con comandi rapidi per interrompere la voce e ricevere subito le nuove righe.
Friend NotInheritable Class TerminaleAppModule
    Inherits AppModuleLettore

    Private Const TastoInvio As Integer = &HD
    Private Const TastoC As Integer = &H43
    Private Const TastoInterruzione As Integer = &H3

    Public Sub New(lettore As WGLettoreSchermo, nomeApp As String)
        MyBase.New(lettore, nomeApp)
    End Sub

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return testoBase
            If Not LetturaTestoAggiornato.ESuperficieDiTesto(elemento, Lettore.DescrittoreApp) Then Return testoBase
            Dim parti As New List(Of String)()
            parti.Add("terminale")
            Dim nome As String = Lettore.DescrittoreApp.Nome(elemento)
            If nome <> "" Then parti.Add(nome)
            Dim riga As String = Lettore.RigaCursoreTerminaleApp()
            If riga <> "" Then parti.Add(riga)
            Return String.Join(", ", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("TerminaleAppModule.AllFocus", ex)
            Return testoBase
        End Try
    End Function

    ' Invio e Ctrl+C zittiscono quello che la voce stava ancora dicendo: le righe nuove arrivano subito dopo.
    Public Overrides Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If evento Is Nothing Then Return False
            If evento.CodiceTasto = TastoInvio AndAlso Not evento.Ctrl AndAlso Not evento.Alt Then
                Lettore.InterrompiVoceApp()
            ElseIf evento.Ctrl AndAlso (evento.CodiceTasto = TastoC OrElse evento.CodiceTasto = TastoInterruzione) Then
                Lettore.InterrompiVoceApp()
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("TerminaleAppModule.AllTasto", ex)
        End Try
        Return False
    End Function

End Class
