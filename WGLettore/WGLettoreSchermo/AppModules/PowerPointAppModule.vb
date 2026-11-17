Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Threading
Imports UIAutomationClient

' PowerPoint: a ogni cambio di diapositiva, in modifica e in presentazione, dice numero, totale e titolo.
Friend NotInheritable Class PowerPointAppModule
    Inherits AppModuleLettore

    Private Const AttesaMassima As Integer = 300
    Private Const PassoAttesa As Integer = 15

    Private ReadOnly _blocco As New Object()
    Private _ultimaDiapositiva As Integer = -1

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "powerpnt")
    End Sub

    Public Overrides Function PrimoPianoIgnorato(finestra As IntPtr) As Boolean
        _ultimaDiapositiva = -1
        Return False
    End Function

    Public Overrides Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If evento Is Nothing OrElse evento.Alt Then Return False
            If evento.Azione = AzioneTastoLettore.Modifica Then Return False
            Dim tickTasto As Integer = Lettore.TickUltimoTastoApp
            Dim inizio As Integer = Environment.TickCount
            SyncLock _blocco
                Dim applicazione As Object = Nothing
                Try
                    applicazione = Marshal.GetActiveObject("PowerPoint.Application")
                Catch exAtteso As COMException
                    RegistroErrori.Scrivi("PowerPointAppModule.AllTasto.Collega", exAtteso)
                    Return False
                End Try
                If applicazione Is Nothing Then Return False
                Dim prima As Integer = If(_ultimaDiapositiva >= 0, _ultimaDiapositiva, NumeroDiapositiva(applicazione))
                _ultimaDiapositiva = prima
                Do
                    If Lettore.TickUltimoTastoApp <> tickTasto Then Return False
                    Dim adesso As Integer = NumeroDiapositiva(applicazione)
                    If adesso > 0 AndAlso adesso <> prima Then
                        _ultimaDiapositiva = adesso
                        Lettore.ParlaApp(FraseDiapositiva(applicazione, adesso), False)
                        Return False
                    End If
                    If Environment.TickCount - inizio >= AttesaMassima Then Return False
                    Thread.Sleep(PassoAttesa)
                Loop
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("PowerPointAppModule.AllTasto", ex)
            Return False
        End Try
    End Function

    Public Overrides Function TestoBarraStatoApp(finestra As IntPtr) As String
        Try
            SyncLock _blocco
                Dim applicazione As Object = Marshal.GetActiveObject("PowerPoint.Application")
                Dim numero As Integer = NumeroDiapositiva(applicazione)
                If numero <= 0 Then Return ""
                Return FraseDiapositiva(applicazione, numero)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("PowerPointAppModule.TestoBarraStatoApp", ex)
            Return ""
        End Try
    End Function

    Private Shared Function NumeroDiapositiva(applicazione As Object) As Integer
        Try
            If applicazione Is Nothing Then Return 0
            Dim presentazioni As Object = applicazione.SlideShowWindows
            If Convert.ToInt32(presentazioni.Count) > 0 Then
                Return Convert.ToInt32(presentazioni(1).View.CurrentShowPosition)
            End If
            Return Convert.ToInt32(applicazione.ActiveWindow.View.Slide.SlideIndex)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("PowerPointAppModule.NumeroDiapositiva", exAtteso)
            Return 0
        End Try
    End Function

    Private Shared Function FraseDiapositiva(applicazione As Object, numero As Integer) As String
        Try
            Dim parti As New List(Of String)()
            Dim totale As Integer = Convert.ToInt32(applicazione.ActivePresentation.Slides.Count)
            parti.Add("diapositiva " & numero.ToString() & If(totale > 0, " di " & totale.ToString(), ""))
            Dim diapositiva As Object = applicazione.ActivePresentation.Slides(numero)
            If Convert.ToBoolean(diapositiva.Shapes.HasTitle) Then
                Dim titolo As String = Convert.ToString(diapositiva.Shapes.Title.TextFrame.TextRange.Text)
                If Not String.IsNullOrWhiteSpace(titolo) Then parti.Add(titolo.Trim())
            End If
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("PowerPointAppModule.FraseDiapositiva", exAtteso)
            Return "diapositiva " & numero.ToString()
        End Try
    End Function

End Class
