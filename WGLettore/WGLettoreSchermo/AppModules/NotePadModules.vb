Imports System.Collections.Generic
Imports UIAutomationClient

' Modulo applicativo dedicato al Blocco note di Windows per la lettura del fuoco e della barra di stato.
Friend NotInheritable Class NotepadAppModule
    Inherits AppModuleLettore

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "notepad")
    End Sub

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return testoBase
            If Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione) <> "LineNumberBox" Then Return testoBase
            Dim testo As String = If(testoBase, "")
            If testo.IndexOf("finestra di dialogo", StringComparison.CurrentCultureIgnoreCase) >= 0 Then
                Return testo.Replace("finestra di dialogo", "modifica")
            End If
            If testo.IndexOf("modifica", StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return testo
            Return If(testo = "", "modifica", testo & ", modifica")
        Catch ex As Exception
            Lettore.ScriviLogApp("NotepadAppModule.AllFocus: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Public Overrides Function TestoBarraStatoApp(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero OrElse Lettore.AutomazioneApp Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return ""
            Dim automazione As IUIAutomation = Lettore.AutomazioneApp
            Dim radice As IUIAutomationElement = automazione.ElementFromHandle(finestra)
            If radice Is Nothing Then Return ""
            Dim condizione As IUIAutomationCondition = automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaIdAutomazione, "ContentTextBlock")
            Dim blocco As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, Lettore.RichiestaCacheApp)
            If blocco Is Nothing Then Return ""
            Dim barra As IUIAutomationElement = Lettore.WalkerControlloApp.GetParentElementBuildCache(blocco, Lettore.RichiestaCacheApp)
            If barra Is Nothing Then Return ""
            Dim parti As New List(Of String)()
            RaccogliNomi(barra, parti, 0)
            Return String.Join(", ", parti)
        Catch ex As Exception
            Lettore.ScriviLogApp("NotepadAppModule.TestoBarraStatoApp: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RaccogliNomi(elemento As IUIAutomationElement, parti As List(Of String), profondita As Integer)
        Try
            If elemento Is Nothing OrElse profondita > 3 OrElse parti.Count >= 20 Then Return
            Dim figli As IUIAutomationElementArray = elemento.FindAllBuildCache(TreeScope.TreeScope_Children, Lettore.AutomazioneApp.CreateTrueCondition(), Lettore.RichiestaCacheApp)
            If figli Is Nothing Then Return
            For indice As Integer = 0 To figli.Length - 1
                Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                Dim nome As String = Lettore.DescrittoreApp.Nome(figlio)
                If nome <> "" Then
                    If Not parti.Contains(nome) Then parti.Add(nome)
                Else
                    RaccogliNomi(figlio, parti, profondita + 1)
                End If
            Next
        Catch ex As Exception
            Lettore.ScriviLogApp("NotepadAppModule.RaccogliNomi: " & ex.Message)
        End Try
    End Sub

End Class