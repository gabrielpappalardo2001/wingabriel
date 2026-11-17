Imports UIAutomationClient

' Gestione specifica delle voci di elenco e dei movimenti interni alle liste per le applicazioni supportate.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Function EVoceDiElenco(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return False
            Dim tipo As Integer = _descrittore.TipoControllo(elemento)
            If tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero Then Return True
            If _descrittore.VoceDiComposito(elemento) OrElse _descrittore.EVoceAlbero(elemento) Then Return True
            Select Case _descrittore.RuoloAria(elemento)
                Case "listitem", "option", "treeitem", "row", "gridcell"
                    Return True
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EVoceDiElenco", exAtteso)
        End Try
        Return False
    End Function

    Private Function MovimentoInternoElenco(precedente As IUIAutomationElement, attuale As IUIAutomationElement) As Boolean
        Try
            If precedente Is Nothing OrElse attuale Is Nothing Then Return False
            If Not EVoceDiElenco(attuale) OrElse Not EVoceDiElenco(precedente) Then Return False
            Dim primo As IUIAutomationElement = ContenitoreVoci(precedente)
            Dim secondo As IUIAutomationElement = ContenitoreVoci(attuale)
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            Dim chiavePrimo As String = ChiaveElemento(primo)
            Dim chiaveSecondo As String = ChiaveElemento(secondo)
            If chiavePrimo = "" OrElse chiaveSecondo = "" Then Return StessoElemento(primo, secondo)
            Return String.Equals(chiavePrimo, chiaveSecondo, StringComparison.Ordinal)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.MovimentoInternoElenco", exAtteso)
            Return False
        End Try
    End Function

End Class
