Imports System.IO
Imports System.Text
Imports System.Threading

' Gestione della registrazione degli errori e esecuzione di chiamate tipizzate per il supporto operativo del lettore.
Public NotInheritable Class RegistroErrori

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _codiciAttesi As Integer() = {&H80040201, &H80040200, &H80131505, &H80010108, &H800706BA, &H800706BE, &H800706BF, &H800401FD, &H80004005, &H80070005, &H80004001, &H80004002, &H80010002, &H8001010A, &H80010001, &H80020003, &H80070578, &H80070057, &H80131509, &H800AC472, &H800A03EC}

    Private Sub New()
    End Sub

    Public Shared Function Atteso(ex As Exception) As Boolean
        Try
            If ex Is Nothing Then Return True
            If TypeOf ex Is ThreadAbortException OrElse TypeOf ex Is ThreadInterruptedException OrElse TypeOf ex Is OperationCanceledException Then Return True
            Dim codice As Integer = ex.HResult
            For Each previsto As Integer In _codiciAttesi
                If codice = previsto Then Return True
            Next
            Return False
        Catch errore As Exception
            Return True
        End Try
    End Function

    Private Const FinestraRipetizioniMs As Integer = 30000
    Private Const VociMassime As Integer = 200
    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _ultimi As New Dictionary(Of String, Integer())()

    ''' <summary>
    ''' Scrive l'errore nel log, ma lo stesso errore dello stesso punto non più di una volta ogni 30 secondi:
    ''' un ciclo che fallisce cento volte di fila riempiva il log (e il disco) e nascondeva il resto.
    ''' La prima scrittura dopo una pausa dice quante volte l'errore si è ripetuto.
    ''' </summary>
    Public Shared Sub Scrivi(origine As String, ex As Exception)
        Try
            If Atteso(ex) Then Return
            Dim chiave As String = origine & "|" & ex.GetType().Name & "|" & ex.Message
            Dim ripetuti As Integer = 0
            SyncLock _blocco
                Dim adesso As Integer = Environment.TickCount
                Dim stato As Integer() = Nothing
                If _ultimi.TryGetValue(chiave, stato) Then
                    If adesso - stato(0) < FinestraRipetizioniMs Then
                        stato(1) += 1
                        Return
                    End If
                    ripetuti = stato(1)
                    stato(0) = adesso
                    stato(1) = 0
                Else
                    If _ultimi.Count >= VociMassime Then _ultimi.Clear()
                    _ultimi(chiave) = New Integer() {adesso, 0}
                End If
            End SyncLock
            _log.ScriviLog(origine & ": " & ex.Message & If(ripetuti > 0, " (ripetuto altre " & ripetuti.ToString() & " volte)", ""))
        Catch errore As Exception
            Return
        End Try
    End Sub

End Class

Friend NotInheritable Class Chiamata(Of T)

    Private ReadOnly _azione As Action(Of T)
    Private ReadOnly _valore As T

    Public Sub New(azione As Action(Of T), valore As T)
        _azione = azione
        _valore = valore
    End Sub

    Public Sub Esegui()
        Try
            If _azione IsNot Nothing Then _azione.Invoke(_valore)
        Catch ex As Exception
            RegistroErrori.Scrivi("Chiamata.Esegui", ex)
        End Try
    End Sub

End Class
