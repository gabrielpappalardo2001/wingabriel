Imports System
Imports System.Collections.Generic
Imports System.Text.RegularExpressions

''' <summary>
''' Piccoli aiuti sul tempo, in comune ai plugin di esempio (Cronometro, Timer, Promemoria): ogni progetto ne include il file.
''' Dire una durata come la direbbe una persona, e capire una durata scritta come la scriverebbe una persona.
''' </summary>
Friend Module AiutoTempo

    Private Function Unita(valore As Integer, singolare As String, plurale As String) As String
        Return valore.ToString() & " " & If(valore = 1, singolare, plurale)
    End Function

    ''' <summary>"1 ora, 2 minuti e 5 secondi", "30 secondi", "zero secondi".</summary>
    Friend Function Descrivi(durata As TimeSpan) As String
        Try
            Dim totale As Integer = CInt(Math.Round(Math.Max(0, durata.TotalSeconds)))
            Dim ore As Integer = totale \ 3600
            Dim minuti As Integer = (totale Mod 3600) \ 60
            Dim secondi As Integer = totale Mod 60
            Dim parti As New List(Of String)()
            If ore > 0 Then parti.Add(Unita(ore, "ora", "ore"))
            If minuti > 0 Then parti.Add(Unita(minuti, "minuto", "minuti"))
            If secondi > 0 OrElse parti.Count = 0 Then parti.Add(Unita(secondi, "secondo", "secondi"))
            If parti.Count = 1 Then Return parti(0)
            Return String.Join(", ", parti.GetRange(0, parti.Count - 1)) & " e " & parti(parti.Count - 1)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Legge una durata scritta a mano: "5" (minuti), "5 minuti", "1 ora e 30", "90 secondi", "1h30m", "5:30" (minuti e secondi),
    ''' "1:05:00" (ore, minuti e secondi). Un numero senza unità vale minuti; dopo le ore vale minuti, dopo i minuti vale secondi.
    ''' </summary>
    Friend Function TentaLeggiDurata(testo As String, ByRef durata As TimeSpan) As Boolean
        Try
            durata = TimeSpan.Zero
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim t As String = testo.Trim().ToLowerInvariant()
            Dim a As Match = Regex.Match(t, "^(\d{1,3}):(\d{2})$")
            If a.Success Then
                durata = TimeSpan.FromSeconds(Integer.Parse(a.Groups(1).Value) * 60 + Integer.Parse(a.Groups(2).Value))
                Return durata > TimeSpan.Zero
            End If
            Dim b As Match = Regex.Match(t, "^(\d{1,2}):(\d{2}):(\d{2})$")
            If b.Success Then
                durata = TimeSpan.FromSeconds(Integer.Parse(b.Groups(1).Value) * 3600 + Integer.Parse(b.Groups(2).Value) * 60 + Integer.Parse(b.Groups(3).Value))
                Return durata > TimeSpan.Zero
            End If
            Dim secondiTotali As Double = 0
            Dim ultima As String = ""
            Dim trovati As MatchCollection = Regex.Matches(t, "(\d+)\s*([a-zà']*)")
            If trovati.Count = 0 Then Return False
            For Each m As Match In trovati
                Dim numero As Integer = Integer.Parse(m.Groups(1).Value)
                Dim parola As String = m.Groups(2).Value
                Dim unita As String
                If parola.StartsWith("or") OrElse parola = "h" Then
                    unita = "ore"
                ElseIf parola.StartsWith("mi") OrElse parola = "m" Then
                    unita = "minuti"
                ElseIf parola.StartsWith("se") OrElse parola = "s" Then
                    unita = "secondi"
                ElseIf parola = "" OrElse parola = "e" Then
                    unita = If(ultima = "minuti", "secondi", "minuti")
                Else
                    Return False
                End If
                Select Case unita
                    Case "ore" : secondiTotali += numero * 3600.0
                    Case "minuti" : secondiTotali += numero * 60.0
                    Case Else : secondiTotali += numero
                End Select
                ultima = unita
            Next
            If secondiTotali <= 0 OrElse secondiTotali > 100 * 3600 Then Return False
            durata = TimeSpan.FromSeconds(secondiTotali)
            Return True
        Catch
            durata = TimeSpan.Zero
            Return False
        End Try
    End Function

End Module
