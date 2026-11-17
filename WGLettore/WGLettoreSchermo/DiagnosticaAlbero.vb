Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports UIAutomationClient

' Scrittura e analisi diagnostica dell'albero degli elementi dell'interfaccia.
Friend NotInheritable Class DiagnosticaAlbero

    Private ReadOnly log As New Logger()
    Private Shared ReadOnly PatternNoti As Integer() = {
        10000, 10002, 10005, 10006, 10007, 10010, 10011, 10013, 10014, 10015, 10016, 10017, 10018, 10024, 10025
    }
    Private Shared ReadOnly NomiPattern As String() = {
        "Invoke", "Value", "ExpandCollapse", "Grid", "GridItem", "SelectionItem", "Selection", "TableItem", "Text", "Toggle", "Window", "ScrollItem", "Legacy", "Text2", "Styles"
    }

    Public Function Scrivi(automazione As IUIAutomation, descrittore As DescrittoreElementi, cache As IUIAutomationCacheRequest, elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim cartella As String = ConfigLettore.CartellaDati("lettore")
            Dim percorso As String = Path.Combine(cartella, "albero-" & DateTime.Now.ToString("yyyyMMdd-HHmmss") & ".txt")
            Dim testo As New StringBuilder()
            testo.AppendLine("WGLettoreSchermo, albero dell'elemento con il fuoco")
            testo.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            testo.AppendLine()
            ScriviElemento(testo, descrittore, elemento, "fuoco")
            testo.AppendLine()
            testo.AppendLine("Antenati, dal più vicino")
            If automazione IsNot Nothing Then
                Dim walker As IUIAutomationTreeWalker = automazione.ControlViewWalker
                Dim corrente As IUIAutomationElement = elemento
                For livello As Integer = 1 To 14
                    If walker Is Nothing Then Exit For
                    corrente = walker.GetParentElementBuildCache(corrente, cache)
                    If corrente Is Nothing Then Exit For
                    ScriviElemento(testo, descrittore, corrente, "antenato " & livello.ToString())
                Next
                testo.AppendLine()
                testo.AppendLine("Figli")
                Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(elemento, cache)
                Dim indice As Integer = 0
                While figlio IsNot Nothing AndAlso indice < 40
                    indice += 1
                    ScriviElemento(testo, descrittore, figlio, "figlio " & indice.ToString())
                    figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                End While
            End If
            File.WriteAllText(percorso, testo.ToString(), New UTF8Encoding(True))
            Return percorso
        Catch ex As Exception
            log.ScriviLog("DiagnosticaAlbero.Scrivi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ScriviElemento(testo As StringBuilder, descrittore As DescrittoreElementi, elemento As IUIAutomationElement, titolo As String)
        Try
            testo.AppendLine("--- " & titolo & " ---")
            testo.AppendLine("ruolo numerico: " & descrittore.TipoControllo(elemento).ToString())
            testo.AppendLine("ruolo localizzato: " & descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaTipoControlloLocalizzato))
            testo.AppendLine("ruolo aria: " & descrittore.RuoloAria(elemento))
            testo.AppendLine("nome: " & descrittore.Nome(elemento))
            testo.AppendLine("nome esteso: " & descrittore.NomeEsteso(elemento))
            testo.AppendLine("valore: " & descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore))
            testo.AppendLine("aiuto: " & descrittore.TestoAiuto(elemento))
            testo.AppendLine("framework: " & descrittore.Framework(elemento))
            testo.AppendLine("classe: " & descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse))
            testo.AppendLine("id automazione: " & descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione))
            testo.AppendLine("abilitato: " & descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaAbilitato).ToString())
            testo.AppendLine("fuoco da tastiera: " & descrittore.FuocoDaTastiera(elemento).ToString())
            testo.AppendLine("sola lettura: " & descrittore.SolaLettura(elemento).ToString())
            testo.AppendLine("fuori schermo: " & descrittore.FuoriSchermo(elemento).ToString())
            testo.AppendLine("processo: " & descrittore.IdProcesso(elemento).ToString())
            testo.AppendLine("stati: " & descrittore.ParteVariabile(elemento))
            testo.AppendLine("pattern: " & PatternDi(elemento))
            Try
                testo.AppendLine("provider: " & elemento.CurrentProviderDescription)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("DiagnosticaAlbero.ScriviElemento", exAtteso)
                testo.AppendLine("provider: non leggibile")
            End Try
            Try
                testo.AppendLine("runtime: " & String.Join("-", DaArray(elemento.GetRuntimeId())))
            Catch exAtteso2 As Exception
                RegistroErrori.Scrivi("DiagnosticaAlbero.ScriviElemento", exAtteso2)
            End Try
        Catch ex As Exception
            RegistroErrori.Scrivi("DiagnosticaAlbero.ScriviElemento", ex)
            testo.AppendLine("elemento non leggibile: " & ex.Message)
        End Try
    End Sub

    Private Function PatternDi(elemento As IUIAutomationElement) As String
        Dim trovati As New List(Of String)()
        For indice As Integer = 0 To PatternNoti.Length - 1
            Try
                Dim pattern As Object = elemento.GetCurrentPattern(PatternNoti(indice))
                If pattern IsNot Nothing Then
                    trovati.Add(NomiPattern(indice))
                    ComLettore.Rilascia(pattern)
                End If
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("DiagnosticaAlbero.PatternDi", exAtteso)
            End Try
        Next
        If trovati.Count = 0 Then Return "nessuno"
        Return String.Join(", ", trovati)
    End Function

    Private Function DaArray(id As Array) As String()
        If id Is Nothing Then Return New String() {}
        Dim parti(id.Length - 1) As String
        For indice As Integer = 0 To id.Length - 1
            parti(indice) = Convert.ToString(id.GetValue(indice))
        Next
        Return parti
    End Function

End Class
