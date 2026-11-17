Imports System.Collections.Generic
Imports UIAutomationClient

' Modulo applicativo per la lettura del fuoco e dei risultati all'interno della calcolatrice.
Friend NotInheritable Class CalcolatriceAppModule
    Inherits AppModuleLettore

    Private _ultimoRisultato As String = ""
    Private _ultimoRisultatoTick As Integer

    Public Sub New(lettore As WGLettoreSchermo, nomeApp As String)
        MyBase.New(lettore, nomeApp)
    End Sub

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return testoBase
            Dim descrittore As DescrittoreElementi = Lettore.DescrittoreApp
            If descrittore.Nome(elemento) <> "" Then Return testoBase
            Dim padre As IUIAutomationElement = Lettore.WalkerControlloApp.GetParentElementBuildCache(elemento, Lettore.RichiestaCacheApp)
            If padre Is Nothing Then Return testoBase
            Select Case descrittore.LeggiStringa(padre, DescrittoreElementi.ProprietaIdAutomazione)
                Case "HistoryListView", "MemoryListView"
                Case Else
                    Return testoBase
            End Select
            Dim figli As IUIAutomationElementArray = elemento.FindAllBuildCache(TreeScope.TreeScope_Children, Lettore.AutomazioneApp.CreateTrueCondition(), Lettore.RichiestaCacheApp)
            If figli Is Nothing Then Return testoBase
            Dim parti As New List(Of String)()
            For indice As Integer = 0 To figli.Length - 1
                Dim nome As String = descrittore.Nome(figli.GetElement(indice))
                If nome <> "" Then parti.Add(nome)
            Next
            If parti.Count = 0 Then Return testoBase
            Dim unito As String = String.Join(" ", parti)
            Return If(String.IsNullOrWhiteSpace(testoBase), unito, unito & ", " & testoBase)
        Catch ex As Exception
            Lettore.ScriviLogApp("CalcolatriceAppModule.AllFocus: " & ex.Message)
            Return testoBase
        End Try
    End Function

    Public Overrides Function AllNotifica(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing OrElse evento.Elemento Is Nothing Then Return False
            If Not String.Equals(evento.IdAttivita, "DisplayUpdated", StringComparison.Ordinal) Then Return False
            If Lettore.TastoCifraRecenteApp(600) Then Return True
            Dim risultato As String = TestoRisultato()
            If risultato = "" Then Return False
            If String.Equals(risultato, _ultimoRisultato, StringComparison.CurrentCulture) AndAlso Environment.TickCount - _ultimoRisultatoTick < 300 Then Return True
            _ultimoRisultato = risultato
            _ultimoRisultatoTick = Environment.TickCount
            Lettore.ParlaApp(risultato, True)
            Return True
        Catch ex As Exception
            Lettore.ScriviLogApp("CalcolatriceAppModule.AllNotifica: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TestoRisultato() As String
        Try
            Dim automazione As IUIAutomation = Lettore.AutomazioneApp
            If automazione Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return ""
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return ""
            Dim radice As IUIAutomationElement = automazione.ElementFromHandle(finestra)
            If radice Is Nothing Then Return ""
            Dim pieno As IUIAutomationCondition = automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaIdAutomazione, "CalculatorResults")
            Dim compatto As IUIAutomationCondition = automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaIdAutomazione, "CalculatorAlwaysOnTopResults")
            Dim condizione As IUIAutomationCondition = automazione.CreateOrCondition(pieno, compatto)
            Dim risultato As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, Lettore.RichiestaCacheApp)
            If risultato Is Nothing Then Return ""
            Return Lettore.DescrittoreApp.Nome(risultato)
        Catch ex As Exception
            Lettore.ScriviLogApp("CalcolatriceAppModule.TestoRisultato: " & ex.Message)
            Return ""
        End Try
    End Function

End Class