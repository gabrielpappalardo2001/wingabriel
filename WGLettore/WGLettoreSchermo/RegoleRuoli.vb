Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports UIAutomationClient

' Caricamento e gestione delle regole per interpretare i ruoli dei controlli nei diversi framework dell'interfaccia utente.
Friend NotInheritable Class RegoleRuoli

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _istanza As RegoleRuoli

    Private ReadOnly log As New Logger()
    Private ReadOnly _righe As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private _caricato As Boolean

    Public Shared ReadOnly Property Istanza As RegoleRuoli
        Get
            SyncLock _blocco
                If _istanza Is Nothing Then
                    _istanza = New RegoleRuoli()
                    _istanza.Carica()
                End If
                Return _istanza
            End SyncLock
        End Get
    End Property

    Public Sub Carica()
        Try
            _righe.Clear()
            For Each riga In RuoliPredefiniti.Voci
                Dim parti() As String = riga.Split("|"c)
                If parti.Length < 3 Then Continue For
                Dim chiave As String = parti(0).Trim().ToLowerInvariant() & "|" & parti(1).Trim().ToLowerInvariant()
                Dim ruolo As String = parti(2).Trim()
                If chiave <> "|" AndAlso ruolo <> "" AndAlso Not _righe.ContainsKey(chiave) Then _righe(chiave) = ruolo
            Next
            _caricato = True
        Catch ex As Exception
            log.ScriviLog("RegoleRuoli.Carica: " & ex.Message)
        End Try
    End Sub

    Public Function Ruolo(descrittore As DescrittoreElementi, elemento As IUIAutomationElement) As String
        Try
            If Not _caricato Then Carica()
            If descrittore Is Nothing OrElse elemento Is Nothing OrElse _righe.Count = 0 Then Return ""
            Dim framework As String = NormalizzaFramework(descrittore.Framework(elemento))
            Dim classe As String = ""
            Dim localizzato As String = ""
            classe = descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse)
            If classe = "" Then
                Try
                    classe = If(elemento.CurrentClassName, "")
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("RegoleRuoli.Ruolo", exAtteso)
                    classe = ""
                End Try
            End If
            localizzato = descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaTipoControlloLocalizzato)
            Dim aria As String = descrittore.RuoloAria(elemento)
            Dim trovato As String = Cerca(framework, classe)
            If trovato = "" Then trovato = Cerca(framework, NomeSemplice(classe))
            If trovato = "" Then trovato = Cerca(framework, aria)
            If trovato = "" Then trovato = Cerca(framework, localizzato)
            If trovato = "" Then trovato = Cerca("*", classe)
            If trovato = "" Then trovato = Cerca("*", NomeSemplice(classe))
            If trovato = "" Then trovato = Cerca("*", aria)
            Return trovato
        Catch ex As Exception
            log.ScriviLog("RegoleRuoli.Ruolo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Cerca(framework As String, chiave As String) As String
        If String.IsNullOrWhiteSpace(framework) OrElse String.IsNullOrWhiteSpace(chiave) Then Return ""
        Dim ruolo As String = Nothing
        If _righe.TryGetValue(framework.ToLowerInvariant() & "|" & chiave.Trim().ToLowerInvariant(), ruolo) Then Return ruolo
        Return ""
    End Function

    Private Shared Function NomeSemplice(classe As String) As String
        If String.IsNullOrWhiteSpace(classe) Then Return ""
        Dim punto As Integer = classe.LastIndexOf("."c)
        If punto < 0 OrElse punto >= classe.Length - 1 Then Return classe.Trim()
        Return classe.Substring(punto + 1).Trim()
    End Function

    Public Shared Function NormalizzaFramework(framework As String) As String
        Dim testo As String = If(framework, "").Trim().ToLowerInvariant()
        Select Case testo
            Case "win32" : Return "win32"
            Case "winform", "winforms" : Return "winform"
            Case "wpf" : Return "wpf"
            Case "xaml", "winui", "uwp" : Return "xaml"
            Case "qt" : Return "qt"
            Case "java", "swing" : Return "java"
            Case "chrome", "electron", "cef" : Return "chrome"
            Case "mozilla", "gecko" : Return "mozilla"
            Case "microsoftedge", "edge" : Return "chrome"
            Case Else : Return testo
        End Select
    End Function

End Class
