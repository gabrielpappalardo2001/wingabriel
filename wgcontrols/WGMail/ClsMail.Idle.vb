Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Search

' Gestione della posta in arrivo tramite il comando IMAP IDLE. Mantiene la connessione aperta per ricevere i messaggi in tempo
' reale o esegue un controllo periodico leggero.
Partial Public Class CLSMail

    ' Sorveglianza della posta in arrivo con il comando IDLE dell'IMAP. Invece di chiedere al
    ' server ogni tot minuti se sia arrivato qualcosa, la connessione resta aperta e il server
    ' avvisa non appena il messaggio arriva. Si consuma molta meno rete e la notizia arriva
    ' nell'istante giusto. Sui server che non conoscono IDLE si ripiega su un controllo
    ' periodico leggero, che non scarica niente e guarda soltanto i contatori.
    Private Class SorveglianzaCasella
        Public Property Chiave As String = ""
        Public Property Filo As Thread = Nothing
        Public Property Interruzione As CancellationTokenSource = Nothing
        Public Property NonLettiNoti As Integer = -1
    End Class

    Private ReadOnly _sorveglianze As New Dictionary(Of String, SorveglianzaCasella)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockSorveglianza As New Object()
    Private Const DurataMassimaIdleMs As Integer = 540000
    Private Const IntervalloControlloSenzaIdleMs As Integer = 60000

    Public Event PostaNuova As EventHandler(Of String)

    Public Sub AvviaSorveglianzaPosta()
        Try
            Dim attivi As List(Of ImpostazioniAccountMail) = OttieniElencoAccount(True)
            If attivi Is Nothing Then Return
            For Each impostazioni As ImpostazioniAccountMail In attivi
                Try
                    If impostazioni Is Nothing Then Continue For
                    If UsaPop3(impostazioni) Then Continue For
                    AvviaSorveglianzaCasella(impostazioni.Chiave)
                Catch exSingola As Exception
                    ScriviLog("CLSMail.AvviaSorveglianzaPosta singola: " & exSingola.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.AvviaSorveglianzaPosta: " & ex.ToString())
        End Try
    End Sub

    Public Sub AvviaSorveglianzaCasella(chiaveAccount As String)
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return
            SyncLock _lockSorveglianza
                If _sorveglianze.ContainsKey(chiaveAccount) Then Return
                Dim stato As New SorveglianzaCasella()
                stato.Chiave = chiaveAccount
                stato.Interruzione = New CancellationTokenSource()
                stato.Filo = New Thread(New ParameterizedThreadStart(AddressOf CicloSorveglianza))
                stato.Filo.IsBackground = True
                _sorveglianze.Add(chiaveAccount, stato)
                stato.Filo.Start(stato)
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSMail.AvviaSorveglianzaCasella (" & If(chiaveAccount, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Sub FermaSorveglianzaPosta()
        Try
            Dim elenco As New List(Of SorveglianzaCasella)
            SyncLock _lockSorveglianza
                elenco.AddRange(_sorveglianze.Values)
                _sorveglianze.Clear()
            End SyncLock
            For Each stato As SorveglianzaCasella In elenco
                Try
                    If stato.Interruzione IsNot Nothing Then stato.Interruzione.Cancel()
                Catch exInterruzione As Exception
                    ScriviLog("CLSMail.FermaSorveglianzaPosta interruzione: " & exInterruzione.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.FermaSorveglianzaPosta: " & ex.ToString())
        End Try
    End Sub

    Private Sub CicloSorveglianza(parametro As Object)
        Dim stato As SorveglianzaCasella = TryCast(parametro, SorveglianzaCasella)
        If stato Is Nothing Then Return

        While True
            Dim client As ImapClient = Nothing
            Try
                If stato.Interruzione Is Nothing OrElse stato.Interruzione.IsCancellationRequested Then Exit While
                If _smaltito Then Exit While

                Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(stato.Chiave)
                If impostazioni Is Nothing Then Exit While

                client = CreaClientImap(impostazioni)
                If client Is Nothing OrElse Not client.IsConnected OrElse Not client.IsAuthenticated Then
                    AttendiPrimaDiRiprovare(stato, 60000)
                    Continue While
                End If

                Dim inbox As IMailFolder = client.Inbox
                inbox.Open(FolderAccess.ReadOnly, stato.Interruzione.Token)
                stato.NonLettiNoti = ContaNonLetti(inbox, stato)

                While Not stato.Interruzione.IsCancellationRequested
                    Dim primaDelControllo As Integer = inbox.Count

                    If client.Capabilities.Contains(ImapCapability.Idle) Then
                        Using finestra As New CancellationTokenSource()
                            finestra.CancelAfter(DurataMassimaIdleMs)
                            Try
                                client.Idle(finestra.Token, stato.Interruzione.Token)
                            Catch exIdle As OperationCanceledException
                                ScriviLog("CLSMail.CicloSorveglianza idle interrotto (" & stato.Chiave & ")")
                            End Try
                        End Using
                    Else
                        If stato.Interruzione.Token.WaitHandle.WaitOne(IntervalloControlloSenzaIdleMs) Then Exit While
                        Try
                            client.NoOp(stato.Interruzione.Token)
                        Catch exNoOp As Exception
                            ScriviLog("CLSMail.CicloSorveglianza NoOp (" & stato.Chiave & "): " & exNoOp.Message)
                            Exit While
                        End Try
                    End If

                    If stato.Interruzione.IsCancellationRequested Then Exit While

                    Dim nonLetti As Integer = ContaNonLetti(inbox, stato)
                    If nonLetti > stato.NonLettiNoti AndAlso stato.NonLettiNoti >= 0 Then
                        SegnalaPostaNuova(stato.Chiave)
                    End If
                    If nonLetti >= 0 Then stato.NonLettiNoti = nonLetti
                    If inbox.Count < primaDelControllo Then stato.NonLettiNoti = nonLetti
                End While

                Exit While
            Catch exCiclo As Exception
                If Not ErroreConnessioneImapPersa(exCiclo) Then
                    ScriviLog("CLSMail.CicloSorveglianza (" & stato.Chiave & "): " & exCiclo.Message)
                End If
                AttendiPrimaDiRiprovare(stato, 120000)
            Finally
                Try
                    If client IsNot Nothing Then
                        If client.IsConnected Then client.Disconnect(True)
                        client.Dispose()
                    End If
                Catch exChiusura As Exception
                    ScriviLog("CLSMail.CicloSorveglianza chiusura (" & stato.Chiave & "): " & exChiusura.Message)
                End Try
            End Try

            If stato.Interruzione Is Nothing OrElse stato.Interruzione.IsCancellationRequested Then Exit While
        End While

        Try
            SyncLock _lockSorveglianza
                If _sorveglianze.ContainsKey(stato.Chiave) Then _sorveglianze.Remove(stato.Chiave)
            End SyncLock
        Catch exRimozione As Exception
            ScriviLog("CLSMail.CicloSorveglianza rimozione (" & stato.Chiave & "): " & exRimozione.Message)
        End Try
    End Sub

    Private Sub AttendiPrimaDiRiprovare(stato As SorveglianzaCasella, millisecondi As Integer)
        Try
            If stato Is Nothing OrElse stato.Interruzione Is Nothing Then Return
            stato.Interruzione.Token.WaitHandle.WaitOne(millisecondi)
        Catch ex As Exception
            ScriviLog("CLSMail.AttendiPrimaDiRiprovare: " & ex.Message)
        End Try
    End Sub

    Private Function ContaNonLetti(inbox As IMailFolder, stato As SorveglianzaCasella) As Integer
        Try
            If inbox Is Nothing Then Return -1
            Dim trovati As IList(Of UniqueId) = inbox.Search(SearchQuery.NotSeen, stato.Interruzione.Token)
            If trovati Is Nothing Then Return 0
            Return trovati.Count
        Catch ex As Exception
            ScriviLog("CLSMail.ContaNonLetti (" & stato.Chiave & "): " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub SegnalaPostaNuova(chiaveAccount As String)
        Try
            RaiseEvent PostaNuova(Me, chiaveAccount)
        Catch ex As Exception
            ScriviLog("CLSMail.SegnalaPostaNuova (" & If(chiaveAccount, "") & "): " & ex.ToString())
        End Try
    End Sub

End Class
