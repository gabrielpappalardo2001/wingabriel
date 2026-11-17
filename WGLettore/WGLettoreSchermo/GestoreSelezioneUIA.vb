Imports System.Runtime.InteropServices
Imports UIAutomationClient

' Gestione degli eventi di automazione relativi alla selezione con UIA.
Friend Delegate Sub RicevitoreSelezione(elemento As IUIAutomationElement)

<ComVisible(True)>
Friend NotInheritable Class GestoreSelezioneUia
    Implements IUIAutomationEventHandler

    Private ReadOnly log As New Logger()
    Private ReadOnly _destinazione As RicevitoreSelezione

    Public Sub New(destinazione As RicevitoreSelezione)
        Try
            _destinazione = destinazione
        Catch ex As Exception
            log.ScriviLog("GestoreSelezioneUia.New: " & ex.Message)
        End Try
    End Sub

    Public Sub HandleAutomationEvent(sender As IUIAutomationElement, eventId As Integer) Implements IUIAutomationEventHandler.HandleAutomationEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender)
        Catch ex As Exception
            log.ScriviLog("GestoreSelezioneUia.HandleAutomationEvent: " & ex.Message)
        End Try
    End Sub

End Class
