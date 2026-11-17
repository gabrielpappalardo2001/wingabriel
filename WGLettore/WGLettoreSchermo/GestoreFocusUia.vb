Imports System.Runtime.InteropServices
Imports UIAutomationClient

' Gestione degli eventi di cambio del fuoco tramite le API UIA.
Friend Delegate Sub RicevitoreFocus(elemento As IUIAutomationElement)

<ComVisible(True)>
Friend NotInheritable Class GestoreFocusUia
    Implements IUIAutomationFocusChangedEventHandler

    Private ReadOnly log As New Logger()
    Private ReadOnly _destinazione As RicevitoreFocus

    Public Sub New(destinazione As RicevitoreFocus)
        Try
            _destinazione = destinazione
        Catch ex As Exception
            log.ScriviLog("GestoreFocusUia.New: " & ex.Message)
        End Try
    End Sub

    Public Sub HandleFocusChangedEvent(sender As IUIAutomationElement) Implements IUIAutomationFocusChangedEventHandler.HandleFocusChangedEvent
        Try
            If _destinazione IsNot Nothing Then _destinazione.Invoke(sender)
        Catch ex As Exception
            log.ScriviLog("GestoreFocusUia.HandleFocusChangedEvent: " & ex.Message)
        End Try
    End Sub

End Class
