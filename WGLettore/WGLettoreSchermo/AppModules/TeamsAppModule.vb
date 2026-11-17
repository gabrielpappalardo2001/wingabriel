Imports UIAutomationClient

' Modulo applicativo per la gestione dell'accessibilità e la lettura dei messaggi e dei menu in Microsoft Teams.
Friend NotInheritable Class TeamsAppModule
    Inherits AppModuleLettore

    Private _ricercaInCorso As Integer

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "ms-teams")
    End Sub

    Public Overrides Function AllMenuAperto(evento As EventoLettore) As Boolean
        Return DescrittoreElementi.PrimoPianoTeams()
    End Function

    Public Overrides Function AllFocusSoppresso(elemento As IUIAutomationElement) As Boolean
        Return PulsanteDelMessaggio(elemento)
    End Function

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            If Lettore.DescrittoreApp Is Nothing Then Return testoBase
            If Lettore.DescrittoreApp.MessaggioTeams(elemento, Lettore.WalkerControlloApp, Lettore.RichiestaCacheApp) Then Return TestoMessaggio(elemento, testoBase)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("TeamsAppModule.AllFocus", exAtteso)
        End Try
        Return testoBase
    End Function

    Public Overrides Function FraseElencoApp(lista As IUIAutomationElement, frase As String) As String
        If DescrittoreElementi.PrimoPianoTeams() Then Return frase.Substring(0, frase.Length - 6) & "casella a elenco"
        Return ""
    End Function

    Private NotInheritable Class RicercaMenuMessaggio
        Private ReadOnly _modulo As TeamsAppModule
        Private ReadOnly _messaggio As IUIAutomationElement

        Public Sub New(modulo As TeamsAppModule, messaggio As IUIAutomationElement)
            _modulo = modulo
            _messaggio = messaggio
        End Sub

        Public Sub Esegui()
            Try
                _modulo.CercaMenuMessaggio(_messaggio)
            Catch ex As Exception
                RegistroErrori.Scrivi("TeamsAppModule.RicercaMenuMessaggio.Esegui", ex)
            End Try
        End Sub
    End Class

    Private Sub CercaMenuMessaggio(messaggio As IUIAutomationElement)
        Try
            Dim trovato As IUIAutomationElement = Lettore.TrovaMenuMessaggioTeams(messaggio)
            If trovato Is Nothing Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.MenuTeamsTrovato
            evento.Elemento = trovato
            Lettore.AccodaApp(evento)
        Catch ex As Exception
            Lettore.ScriviLogApp("TeamsAppModule.CercaMenuMessaggio: " & ex.Message)
        Finally
            System.Threading.Volatile.Write(_ricercaInCorso, 0)
        End Try
    End Sub

    Public Overrides Sub AllTastoAlt()
        Try
            Dim elemento As IUIAutomationElement = Lettore.UltimoElementoApp
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.OperazioniApp Is Nothing Then Return
            Dim messaggio As IUIAutomationElement = TrovaMessaggio(elemento)
            If messaggio Is Nothing Then Return
            If System.Threading.Interlocked.CompareExchange(_ricercaInCorso, 1, 0) <> 0 Then Return
            Dim ricerca As New RicercaMenuMessaggio(Me, messaggio)
            System.Threading.Tasks.Task.Run(AddressOf ricerca.Esegui)
        Catch ex As Exception
            Lettore.ScriviLogApp("TeamsAppModule.AllTastoAlt: " & ex.Message)
        End Try
    End Sub

    Private Function TestoMessaggio(elemento As IUIAutomationElement, testo As String) As String
        Try
            Dim nome As String = Lettore.DescrittoreApp.Nome(elemento)
            If nome = "" Then Return testo
            Dim posizione As Integer = Lettore.DescrittoreApp.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            Dim totale As Integer = Lettore.DescrittoreApp.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
            If posizione > 0 AndAlso totale > 0 Then Return nome & ", " & posizione.ToString() & " di " & totale.ToString()
            Return nome
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("TeamsAppModule.TestoMessaggio", exAtteso)
            Return testo
        End Try
    End Function

    Private Function TrovaMessaggio(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing OrElse Lettore.WalkerControlloApp Is Nothing Then Return Nothing
            If PulsanteDelMessaggio(elemento) Then Return elemento
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 1 To 6
                If Lettore.DescrittoreApp.MessaggioTeams(corrente, Lettore.WalkerControlloApp, Lettore.RichiestaCacheApp) Then Return corrente
                corrente = Lettore.WalkerControlloApp.GetParentElementBuildCache(corrente, Lettore.RichiestaCacheApp)
                If corrente Is Nothing Then Return Nothing
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("TeamsAppModule.TrovaMessaggio", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Function PulsanteDelMessaggio(elemento As IUIAutomationElement) As Boolean
        Try
            If Not DescrittoreElementi.PrimoPianoTeams() OrElse Lettore.WalkerControlloApp Is Nothing Then Return False
            If Lettore.EVoceDiMenuApp(elemento) Then Return False
            Dim tipo As Integer = Lettore.DescrittoreApp.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloPulsante AndAlso tipo <> DescrittoreElementi.ControlloPulsanteDiviso AndAlso Lettore.DescrittoreApp.RuoloAria(elemento) <> "button" Then Return False
            Dim corrente As IUIAutomationElement = Lettore.WalkerControlloApp.GetParentElementBuildCache(elemento, Lettore.RichiestaCacheApp)
            For salto As Integer = 1 To 6
                If corrente Is Nothing Then Return False
                If Lettore.DescrittoreApp.MessaggioTeams(corrente, Lettore.WalkerControlloApp, Lettore.RichiestaCacheApp) Then Return True
                corrente = Lettore.WalkerControlloApp.GetParentElementBuildCache(corrente, Lettore.RichiestaCacheApp)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("TeamsAppModule.PulsanteDelMessaggio", exAtteso)
        End Try
        Return False
    End Function

End Class
