Imports System.Collections.Concurrent
Imports System.Runtime.InteropServices
Imports System.Threading

' Gestione della coda di esecuzione delle operazioni in background con controllo dei thread e annullamento.
Friend NotInheritable Class CodaOperazioni

    Private Class RichiestaOperazione
        Public Chiave As String
        Public Azione As Action
    End Class

    <DllImport("ole32.dll")>
    Private Shared Function CoEnableCallCancellation(riservato As IntPtr) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Shared Function CoCancelCall(idThread As UInteger, attesa As UInteger) As Integer
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    Private Const AttesaAnnullamentoMs As Integer = 3000
    Private Const AttesaSostituzioneMs As Integer = 8000
    Private Const AttesaBloccoMs As Integer = 3000
    Private Const SostituzioniMassime As Integer = 20

    Private ReadOnly log As New Logger()
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _inAttesa As New Dictionary(Of String, RichiestaOperazione)(StringComparer.Ordinal)
    Private _coda As BlockingCollection(Of RichiestaOperazione)
    Private _thread As Thread
    Private _sorveglianza As Thread
    Private _attivo As Integer
    Private _generazione As Integer
    Private _operazioneInCorso As Integer
    Private _inizioOperazione As Integer
    Private _idThreadNativo As UInteger
    Private _annullamentoTentato As Integer
    Private _sostituzioni As Integer

    Public Property BloccoEsecuzione As Object

    Public Property Precedenza As CodaOperazioni

    Public Sub Avvia()
        Try
            If Interlocked.CompareExchange(_attivo, 1, 0) <> 0 Then Return
            SyncLock _blocco
                _inAttesa.Clear()
            End SyncLock
            _coda = New BlockingCollection(Of RichiestaOperazione)()
            _sostituzioni = 0
            AvviaLavoratore()
            _sorveglianza = New Thread(AddressOf Sorveglia)
            _sorveglianza.IsBackground = True
            _sorveglianza.Name = "WGLettoreSchermoSorveglianza"
            _sorveglianza.Start()
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Avvia: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaLavoratore()
        Try
            Dim generazione As Integer = Interlocked.Increment(_generazione)
            Volatile.Write(_operazioneInCorso, 0)
            Dim lavoratore As New Thread(AddressOf Ciclo)
            lavoratore.IsBackground = True
            lavoratore.Name = "WGLettoreSchermoOperazioni"
            lavoratore.SetApartmentState(ApartmentState.MTA)
            _thread = lavoratore
            lavoratore.Start(generazione)
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.AvviaLavoratore: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            Interlocked.Exchange(_attivo, 0)
            If _coda IsNot Nothing Then _coda.CompleteAdding()
            SyncLock _blocco
                _inAttesa.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Ferma: " & ex.Message)
        End Try
    End Sub

    Public Sub Attendi()
        Try
            Dim lavoratore As Thread = _thread
            If lavoratore IsNot Nothing Then
                lavoratore.Join(1500)
                _thread = Nothing
            End If
            Dim controllo As Thread = _sorveglianza
            If controllo IsNot Nothing Then
                controllo.Join(1000)
                _sorveglianza = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Attendi: " & ex.Message)
        End Try
    End Sub

    Public Sub Esegui(chiave As String, azione As Action, Optional sostituisci As Boolean = False)
        Try
            If Volatile.Read(_attivo) = 0 OrElse azione Is Nothing Then Return
            If _coda Is Nothing Then Return
            Dim richiesta As New RichiestaOperazione()
            richiesta.Chiave = If(chiave, "")
            richiesta.Azione = azione
            SyncLock _blocco
                If richiesta.Chiave <> "" Then
                    Dim precedente As RichiestaOperazione = Nothing
                    If _inAttesa.TryGetValue(richiesta.Chiave, precedente) Then
                        If sostituisci AndAlso precedente IsNot Nothing Then
                            precedente.Azione = azione
                        End If
                        Return
                    End If
                    _inAttesa(richiesta.Chiave) = richiesta
                End If
            End SyncLock
            _coda.TryAdd(richiesta)
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Esegui: " & ex.Message)
        End Try
    End Sub

    Public Sub Svuota()
        Try
            Dim coda As BlockingCollection(Of RichiestaOperazione) = _coda
            If coda Is Nothing Then Return
            Dim scarto As RichiestaOperazione = Nothing
            While coda.TryTake(scarto)
            End While
            SyncLock _blocco
                _inAttesa.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Svuota: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Occupata As Boolean
        Get
            Try
                Dim coda As BlockingCollection(Of RichiestaOperazione) = _coda
                If coda Is Nothing Then Return False
                SyncLock _blocco
                    If _inAttesa.Count > 0 Then Return True
                End SyncLock
                Return coda.Count > 0
            Catch ex As Exception
                log.ScriviLog("CodaOperazioni.Occupata: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Sub Ciclo(stato As Object)
        Dim generazione As Integer = 0
        Dim annullabile As Boolean = False
        Try
            If TypeOf stato Is Integer Then generazione = CInt(stato)
        Catch ex As Exception
            log.ScriviLog("CodaOperazioni.Ciclo (avvio): " & ex.Message)
        End Try
        While Volatile.Read(_attivo) = 1 AndAlso generazione = Volatile.Read(_generazione)
            Dim richiesta As RichiestaOperazione = Nothing
            Try
                If Not _coda.TryTake(richiesta, 200) Then Continue While
            Catch ex As Exception
                If Volatile.Read(_attivo) = 1 Then log.ScriviLog("CodaOperazioni.Ciclo (coda): " & ex.Message)
                Exit While
            End Try
            If richiesta Is Nothing Then Continue While
            SyncLock _blocco
                If richiesta.Chiave <> "" Then _inAttesa.Remove(richiesta.Chiave)
            End SyncLock
            Try
                If Not annullabile Then
                    Try
                        annullabile = CoEnableCallCancellation(IntPtr.Zero) >= 0
                    Catch exAnnullo As Exception
                        log.ScriviLog("CodaOperazioni.Ciclo (annullamento): " & exAnnullo.Message)
                        annullabile = True
                    End Try
                End If
                Dim precedenza As CodaOperazioni = Me.Precedenza
                If precedenza IsNot Nothing Then
                    Dim attese As Integer = 0
                    While attese < 10 AndAlso precedenza.Occupata
                        Thread.Sleep(5)
                        attese += 1
                    End While
                End If
                Volatile.Write(_idThreadNativo, GetCurrentThreadId())
                Volatile.Write(_inizioOperazione, Environment.TickCount)
                Volatile.Write(_annullamentoTentato, 0)
                Volatile.Write(_operazioneInCorso, 1)
                Dim blocco As Object = BloccoEsecuzione
                If blocco Is Nothing Then
                    richiesta.Azione.Invoke()
                ElseIf Monitor.TryEnter(blocco, AttesaBloccoMs) Then
                    Try
                        richiesta.Azione.Invoke()
                    Finally
                        Monitor.Exit(blocco)
                    End Try
                End If
            Catch ex As Exception
                log.ScriviLog("CodaOperazioni.Ciclo: " & ex.Message)
            Finally
                If generazione = Volatile.Read(_generazione) Then Volatile.Write(_operazioneInCorso, 0)
            End Try
        End While
    End Sub

    Private Sub Sorveglia()
        While Volatile.Read(_attivo) = 1
            Try
                Thread.Sleep(250)
                If Volatile.Read(_operazioneInCorso) = 0 Then Continue While
                Dim durata As Integer = Environment.TickCount - Volatile.Read(_inizioOperazione)
                If durata < 0 Then Continue While
                If durata > AttesaAnnullamentoMs AndAlso Interlocked.CompareExchange(_annullamentoTentato, 1, 0) = 0 Then
                    Dim idThread As UInteger = Volatile.Read(_idThreadNativo)
                    If idThread <> 0UI Then CoCancelCall(idThread, 0UI)
                End If
                If durata > AttesaSostituzioneMs AndAlso _sostituzioni < SostituzioniMassime Then
                    _sostituzioni += 1
                    AvviaLavoratore()
                End If
            Catch ex As Exception
                log.ScriviLog("CodaOperazioni.Sorveglia: " & ex.Message)
            End Try
        End While
    End Sub

End Class
