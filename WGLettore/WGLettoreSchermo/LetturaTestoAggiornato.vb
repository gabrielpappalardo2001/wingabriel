Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Confronto e gestione delle variazioni di testo per rilevare il contenuto aggiornato.
Friend NotInheritable Class LetturaTestoAggiornato

    Private Const SchemaTesto As Integer = 10014
    Private Const MassimoCaratteri As Integer = 30000
    Private Const MassimoRighe As Integer = 6

    Private Shared _chiave As String = ""
    Private Shared _righe As List(Of String)

    Private Sub New()
    End Sub

    Public Shared Function ESuperficieDiTesto(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return False
            If Not descrittore.HaPatternTesto(elemento) Then Return False
            If DescrittoreElementi.FrameworkWeb(descrittore.Framework(elemento)) Then Return False
            If descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaValoreDisponibile) AndAlso Not descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaSolaLettura) Then Return False
            Select Case descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloTesto, DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloRiquadro,
                     DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloModifica
                    Return True
            End Select
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTestoAggiornato.ESuperficieDiTesto", exAtteso)
            Return False
        End Try
    End Function

    Public Shared Function NuovoTesto(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return ""
            Dim testo As String = pattern.DocumentRange.GetText(MassimoCaratteri)
            If testo Is Nothing Then Return ""
            Dim nuove As List(Of String) = Dividi(testo)
            Dim chiave As String = ChiaveElemento(elemento)
            Dim precedenti As List(Of String) = Nothing
            If chiave = _chiave Then precedenti = _righe
            _chiave = chiave
            _righe = nuove
            If precedenti Is Nothing Then Return ""
            Return Differenza(precedenti, nuove, False)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTestoAggiornato.NuovoTesto", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Sub Dimentica()
        _chiave = ""
        _righe = Nothing
    End Sub

    Private Shared Function Dividi(testo As String) As List(Of String)
        Dim righe As New List(Of String)()
        For Each riga As String In testo.Replace(vbCr, "").Split(vbLf(0))
            righe.Add(riga.TrimEnd())
        Next
        While righe.Count > 0 AndAlso righe(righe.Count - 1) = ""
            righe.RemoveAt(righe.Count - 1)
        End While
        Return righe
    End Function

    Friend Shared Function Differenza(vecchie As List(Of String), nuove As List(Of String), Optional consentiRiscrittura As Boolean = True) As String
        If vecchie.Count = 0 Then Return Unisci(nuove, 0)
        Dim ultimaStabile As Integer = vecchie.Count - 1
        Dim sovrapposte As Integer = -1
        For scarto As Integer = 0 To vecchie.Count - 1
            Dim lunghezza As Integer = vecchie.Count - scarto
            If lunghezza > nuove.Count Then Continue For
            Dim uguali As Boolean = True
            For indice As Integer = 0 To lunghezza - 2
                If Not String.Equals(vecchie(scarto + indice), nuove(indice), StringComparison.Ordinal) Then
                    uguali = False
                    Exit For
                End If
            Next
            If uguali Then
                sovrapposte = lunghezza
                Exit For
            End If
        Next
        If sovrapposte >= 1 Then
            Dim inizio As Integer = sovrapposte
            If sovrapposte <= nuove.Count AndAlso Not String.Equals(vecchie(vecchie.Count - 1), nuove(sovrapposte - 1), StringComparison.Ordinal) Then
                If nuove.Count > sovrapposte Then inizio = sovrapposte - 1 Else inizio = nuove.Count
            End If
            Return Unisci(nuove, inizio)
        End If
        If Not consentiRiscrittura Then Return ""
        Dim viste As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each riga As String In vecchie
            Dim n As Integer = 0
            viste.TryGetValue(riga, n)
            viste(riga) = n + 1
        Next
        Dim aggiunte As New List(Of String)()
        For Each riga As String In nuove
            Dim n As Integer = 0
            If viste.TryGetValue(riga, n) AndAlso n > 0 Then
                viste(riga) = n - 1
            ElseIf riga <> "" Then
                aggiunte.Add(riga)
            End If
        Next
        Return Unisci(aggiunte, 0)
    End Function

    Private Shared Function Unisci(righe As List(Of String), da As Integer) As String
        If da < 0 Then da = 0
        Dim scelte As New List(Of String)()
        For indice As Integer = da To righe.Count - 1
            If righe(indice) <> "" Then scelte.Add(righe(indice))
        Next
        If scelte.Count > MassimoRighe Then scelte = scelte.GetRange(scelte.Count - MassimoRighe, MassimoRighe)
        Return String.Join(". ", scelte)
    End Function

    Private Shared Function ChiaveElemento(elemento As IUIAutomationElement) As String
        Try
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing Then Return ""
            Dim testo As New StringBuilder()
            For indice As Integer = 0 To id.Length - 1
                If indice > 0 Then testo.Append("-"c)
                testo.Append(Convert.ToString(id.GetValue(indice)))
            Next
            Return testo.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTestoAggiornato.ChiaveElemento", exAtteso)
            Return ""
        End Try
    End Function

End Class
