Imports System.Collections.Generic

' Filtro e pulizia degli eventi e del testo duplicato o obsoleto.
Friend NotInheritable Class FiltroEventi

    Private ReadOnly _blocco As New Object()
    Private ReadOnly _ultimoTick As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private ReadOnly _ultimoTesto As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Public Function Accetta(chiave As String, finestraMillisecondi As Integer) As Boolean
        Return AccettaTesto(chiave, Nothing, finestraMillisecondi)
    End Function

    Public Function AccettaTesto(chiave As String, testo As String, finestraMillisecondi As Integer) As Boolean
        Try
            If String.IsNullOrEmpty(chiave) Then Return True
            If finestraMillisecondi < 1 Then finestraMillisecondi = 1
            Dim ora As Integer = Environment.TickCount
            SyncLock _blocco
                Dim precedente As Integer = 0
                If _ultimoTick.TryGetValue(chiave, precedente) Then
                    If ora - precedente < finestraMillisecondi Then
                        If testo Is Nothing Then Return False
                        Dim gia As String = Nothing
                        If _ultimoTesto.TryGetValue(chiave, gia) AndAlso String.Equals(gia, testo, StringComparison.CurrentCulture) Then Return False
                        If ora - precedente < finestraMillisecondi \ 3 Then Return False
                    End If
                End If
                _ultimoTick(chiave) = ora
                If testo IsNot Nothing Then _ultimoTesto(chiave) = testo
                If _ultimoTick.Count > 400 Then PulisciVecchi(ora)
            End SyncLock
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("FiltroEventi.AccettaTesto", exAtteso)
            Return True
        End Try
    End Function

    Private Sub PulisciVecchi(ora As Integer)
        Dim morti As New List(Of String)()
        For Each coppia In _ultimoTick
            If ora - coppia.Value > 20000 Then morti.Add(coppia.Key)
        Next
        For Each chiave In morti
            _ultimoTick.Remove(chiave)
            _ultimoTesto.Remove(chiave)
        Next
    End Sub

End Class
