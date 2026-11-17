Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Net.Sockets
Imports System.Text
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Search
Imports MailKit.Security
Imports MimeKit

Partial Public Class CLSMail

    ' ============================================================================================
    ' CREAZIONE DI CARTELLE SUL SERVER
    ' ============================================================================================

    Public Function CreaCartellaImap(chiaveAccount As String, nomeNuovaCartella As String, percorsoCartellaPadre As String) As Boolean
        _ultimoErrore = ""
        Try
            Dim nome As String = If(nomeNuovaCartella, "").Trim()
            If String.IsNullOrWhiteSpace(nome) Then
                _ultimoErrore = "Il nome della cartella non può essere vuoto."
                Return False
            End If

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If UsaPop3(impostazioni) Then
                _ultimoErrore = "Le cartelle si possono creare solo sugli account IMAP."
                Return False
            End If

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim padre As IMailFolder = Nothing
                If String.IsNullOrWhiteSpace(percorsoCartellaPadre) Then
                    padre = client.GetFolder(client.PersonalNamespaces(0))
                Else
                    padre = client.GetFolder(percorsoCartellaPadre)
                End If

                If padre Is Nothing Then
                    _ultimoErrore = "Cartella di partenza non trovata."
                    Return False
                End If

                ' Il separatore lo decide il server: se l'utente lo ha scritto a mano nel nome
                ' verrebbe interpretato come annidamento, e su alcuni server e' un errore.
                Try
                    If padre.DirectorySeparator <> ChrW(0) Then
                        nome = nome.Replace(padre.DirectorySeparator, " "c).Trim()
                    End If
                Catch exSeparatore As Exception
                    ScriviLog("CLSMail.CreaCartellaImap separatore: " & exSeparatore.ToString())
                End Try

                If String.IsNullOrWhiteSpace(nome) Then
                    _ultimoErrore = "Il nome della cartella non è valido."
                    Return False
                End If

                padre.Create(nome, True)
                AzzeraCacheCartelleSpeciali()
                Return True
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.CreaCartellaImap autenticazione (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.CreaCartellaImap IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SocketException
            ImpostaErrore("CLSMail.CreaCartellaImap rete (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As Exception
            ImpostaErrore("CLSMail.CreaCartellaImap (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

    ' ============================================================================================
    ' SPOSTAMENTO DI MESSAGGI FRA CARTELLE DEL SERVER
    ' ============================================================================================

    Public Function SpostaMessaggiInCartella(chiaveAccount As String, cartellaOrigine As String,
                                             idMessaggi As List(Of String), cartellaDestinazione As String) As Integer
        _ultimoErrore = ""
        Try
            If idMessaggi Is Nothing OrElse idMessaggi.Count = 0 Then Return 0
            If String.IsNullOrWhiteSpace(cartellaDestinazione) Then
                _ultimoErrore = "Cartella di destinazione non indicata."
                Return 0
            End If
            If String.Equals(cartellaOrigine, cartellaDestinazione, StringComparison.OrdinalIgnoreCase) Then Return 0

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return 0
            End If
            If UsaPop3(impostazioni) Then
                _ultimoErrore = "I messaggi si possono spostare solo sugli account IMAP."
                Return 0
            End If

            Dim uid As New List(Of UniqueId)
            For Each id As String In idMessaggi
                Try
                    If String.IsNullOrWhiteSpace(id) Then Continue For
                    Dim numero As UInteger = 0
                    If Not UInteger.TryParse(id.Trim(), numero) Then Continue For
                    uid.Add(New UniqueId(numero))
                Catch exId As Exception
                    ScriviLog("CLSMail.SpostaMessaggiInCartella identificativo (" & If(id, "") & "): " & exId.ToString())
                End Try
            Next
            If uid.Count = 0 Then Return 0

            Dim spostati As Integer = 0
            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim origine As IMailFolder = client.GetFolder(cartellaOrigine)
                If origine Is Nothing Then
                    _ultimoErrore = "Cartella di partenza non trovata."
                    Return 0
                End If
                Dim destinazione As IMailFolder = client.GetFolder(cartellaDestinazione)
                If destinazione Is Nothing Then
                    _ultimoErrore = "Cartella di destinazione non trovata."
                    Return 0
                End If

                If Not origine.IsOpen OrElse origine.Access <> FolderAccess.ReadWrite Then
                    origine.Open(FolderAccess.ReadWrite, Segnale())
                End If

                origine.MoveTo(uid, destinazione)
                spostati = uid.Count
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try

            For Each id As String In idMessaggi
                Try
                    EliminaMessaggioDalDb(chiaveAccount, cartellaOrigine, id)
                Catch exDb As Exception
                    ScriviLog("CLSMail.SpostaMessaggiInCartella cache (" & If(id, "") & "): " & exDb.ToString())
                End Try
            Next

            Return spostati
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.SpostaMessaggiInCartella IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return 0
        Catch ex As SocketException
            ImpostaErrore("CLSMail.SpostaMessaggiInCartella rete (account " & If(chiaveAccount, "") & ")", ex)
            Return 0
        Catch ex As Exception
            ImpostaErrore("CLSMail.SpostaMessaggiInCartella (account " & If(chiaveAccount, "") & ")", ex)
            Return 0
        End Try
    End Function

    Public Function SpostaMessaggiInCartellaSpeciale(chiaveAccount As String, cartellaOrigine As String,
                                                     idMessaggi As List(Of String), tipoSpeciale As String) As Integer
        Try
            Dim destinazione As String = RisolviPercorsoCartellaSpeciale(chiaveAccount, tipoSpeciale)
            If String.IsNullOrWhiteSpace(destinazione) Then
                _ultimoErrore = "Cartella di destinazione non trovata su questo account."
                Return 0
            End If
            Return SpostaMessaggiInCartella(chiaveAccount, cartellaOrigine, idMessaggi, destinazione)
        Catch ex As Exception
            ImpostaErrore("CLSMail.SpostaMessaggiInCartellaSpeciale (account " & If(chiaveAccount, "") & ", " & If(tipoSpeciale, "") & ")", ex)
            Return 0
        End Try
    End Function

    ' ============================================================================================
    ' APPLICAZIONE DELLE REGOLE SUL SERVER
    ' ============================================================================================
    ' Le regole agivano soltanto sulla copia locale dei messaggi, mentre la lista viene letta
    ' dal server: il messaggio restava quindi in posta in arrivo e la regola sembrava non fare
    ' niente. Qui le regole vengono applicate dove i messaggi stanno davvero.

    Private Class EsitoRegolaServer
        Public Property Uid As UniqueId
        Public Property Azione As String = ""
        Public Property Parametro As String = ""
    End Class

    Public Function ApplicaRegoleSulServer(chiaveAccount As String) As Integer
        _ultimoErrore = ""
        Dim applicate As Integer = 0
        Try
            Dim gestoreAvanzato As PostaAvanzata = Avanzata()
            If gestoreAvanzato Is Nothing Then Return 0

            Dim regole As List(Of RegolaMail) = gestoreAvanzato.OttieniRegole(True)
            If regole Is Nothing OrElse regole.Count = 0 Then Return 0

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing OrElse UsaPop3(impostazioni) Then Return 0

            Dim percorsoInbox As String = RisolviPercorsoCartellaSpeciale(chiaveAccount, "inbox")
            If String.IsNullOrWhiteSpace(percorsoInbox) Then percorsoInbox = "INBOX"

            Dim decisioni As New List(Of EsitoRegolaServer)

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim inbox As IMailFolder = client.GetFolder(percorsoInbox)
                If inbox Is Nothing Then Return 0
                If Not inbox.IsOpen Then inbox.Open(FolderAccess.ReadWrite, Segnale())

                ' Solo i messaggi non ancora letti: quelli gia' visti sono stati lasciati li'
                ' dall'utente, e ripassarci sopra ad ogni controllo sposterebbe roba che lui
                ' aveva deciso di tenere.
                Dim daEsaminare As IList(Of UniqueId) = inbox.Search(SearchQuery.NotSeen, Segnale())
                If daEsaminare Is Nothing OrElse daEsaminare.Count = 0 Then Return 0

                Dim riepiloghi As IList(Of IMessageSummary) = inbox.Fetch(daEsaminare,
                    MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope)
                If riepiloghi Is Nothing Then Return 0

                For Each voce As IMessageSummary In riepiloghi
                    Try
                        If voce.Envelope Is Nothing Then Continue For

                        Dim mittente As String = ""
                        If voce.Envelope.From IsNot Nothing AndAlso voce.Envelope.From.Count > 0 Then
                            mittente = voce.Envelope.From(0).ToString()
                        End If

                        Dim destinatari As String = ""
                        Try
                            If voce.Envelope.To IsNot Nothing Then
                                destinatari = String.Join(", ", voce.Envelope.To.Mailboxes.Select(Function(m) m.Address))
                            End If
                        Catch exDest As Exception
                            ScriviLog("CLSMail.ApplicaRegoleSulServer destinatari: " & exDest.ToString())
                        End Try

                        Dim oggetto As String = If(voce.Envelope.Subject, "")

                        For Each regola As RegolaMail In regole
                            Try
                                ' Il corpo non viene scaricato: farlo per ogni messaggio a ogni
                                ' controllo costerebbe troppo. Le regole sul corpo restano quelle
                                ' locali, che agiscono quando il messaggio viene aperto.
                                If String.Equals(If(regola.Campo, "").Trim(), "Corpo", StringComparison.OrdinalIgnoreCase) Then Continue For
                                If Not gestoreAvanzato.RegolaCorrisponde(regola, mittente, oggetto, "", destinatari) Then Continue For

                                Dim azione As String = If(regola.Azione, "").Trim().ToUpperInvariant()
                                If azione <> "SPOSTACARTELLA" AndAlso azione <> "SEGNAINDESIDERATA" AndAlso
                                   azione <> "SEGNALETTO" AndAlso azione <> "ELIMINA" Then Continue For

                                Dim esito As New EsitoRegolaServer()
                                esito.Uid = voce.UniqueId
                                esito.Azione = azione
                                esito.Parametro = If(regola.Parametro, "").Trim()
                                decisioni.Add(esito)

                                If regola.FermaSeApplicata Then Exit For
                            Catch exRegola As Exception
                                ScriviLog("CLSMail.ApplicaRegoleSulServer regola (" & If(regola.Nome, "") & "): " & exRegola.ToString())
                            End Try
                        Next
                    Catch exVoce As Exception
                        ScriviLog("CLSMail.ApplicaRegoleSulServer messaggio: " & exVoce.ToString())
                    End Try
                Next

                applicate = EseguiDecisioniRegole(chiaveAccount, client, inbox, decisioni)
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As Exception
            ImpostaErrore("CLSMail.ApplicaRegoleSulServer (account " & If(chiaveAccount, "") & ")", ex)
            Return 0
        End Try
        Return applicate
    End Function

    Private Function EseguiDecisioniRegole(chiaveAccount As String, client As ImapClient,
                                           inbox As IMailFolder, decisioni As List(Of EsitoRegolaServer)) As Integer
        Dim applicate As Integer = 0
        Try
            If decisioni Is Nothing OrElse decisioni.Count = 0 Then Return 0

            ' I messaggi vengono raggruppati per azione e destinazione, cosi' si manda al
            ' server un comando solo per gruppo invece di uno per messaggio.
            Dim gruppi As New Dictionary(Of String, List(Of UniqueId))(StringComparer.OrdinalIgnoreCase)
            Dim destinazioni As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            For Each esito As EsitoRegolaServer In decisioni
                Try
                    Dim destinazione As String = ""
                    Select Case esito.Azione
                        Case "SPOSTACARTELLA"
                            destinazione = esito.Parametro
                        Case "SEGNAINDESIDERATA"
                            destinazione = RisolviPercorsoCartellaSpeciale(chiaveAccount, "junk")
                        Case "ELIMINA"
                            destinazione = RisolviPercorsoCartellaSpeciale(chiaveAccount, "trash")
                    End Select

                    Dim chiaveGruppo As String = esito.Azione & "|" & destinazione
                    If Not gruppi.ContainsKey(chiaveGruppo) Then
                        gruppi(chiaveGruppo) = New List(Of UniqueId)
                        destinazioni(chiaveGruppo) = destinazione
                    End If
                    gruppi(chiaveGruppo).Add(esito.Uid)
                Catch exRaggruppa As Exception
                    ScriviLog("CLSMail.EseguiDecisioniRegole raggruppamento: " & exRaggruppa.ToString())
                End Try
            Next

            For Each coppia As KeyValuePair(Of String, List(Of UniqueId)) In gruppi
                Try
                    Dim parti() As String = coppia.Key.Split("|"c)
                    Dim azione As String = parti(0)
                    Dim destinazione As String = destinazioni(coppia.Key)

                    If azione = "SEGNALETTO" Then
                        inbox.Store(coppia.Value, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) With {.Silent = True})
                        applicate += coppia.Value.Count
                        Continue For
                    End If

                    If String.IsNullOrWhiteSpace(destinazione) Then Continue For
                    If String.Equals(destinazione, inbox.FullName, StringComparison.OrdinalIgnoreCase) Then Continue For

                    Dim cartellaDestinazione As IMailFolder = client.GetFolder(destinazione)
                    If cartellaDestinazione Is Nothing Then Continue For

                    inbox.MoveTo(coppia.Value, cartellaDestinazione)
                    applicate += coppia.Value.Count
                Catch exGruppo As Exception
                    ScriviLog("CLSMail.EseguiDecisioniRegole gruppo (" & coppia.Key & "): " & DescriviErroreMailKit(exGruppo))
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EseguiDecisioniRegole: " & ex.ToString())
        End Try
        Return applicate
    End Function

    ' ============================================================================================
    ' RINOMINA, SPOSTAMENTO ED ELIMINAZIONE DI UNA CARTELLA
    ' ============================================================================================

    ' Valore convenzionale per dire "lascia la cartella dov'e', cambia solo il nome":
    ' la stringa vuota significa invece "portala alla radice della casella".
    Public Const PadreInvariato As String = "__padre_invariato__"

    Public Function RinominaCartellaImap(chiaveAccount As String, percorsoCartella As String,
                                         nuovoNome As String, percorsoNuovoPadre As String) As Boolean
        _ultimoErrore = ""
        Try
            If String.IsNullOrWhiteSpace(percorsoCartella) Then
                _ultimoErrore = "Cartella non indicata."
                Return False
            End If

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If UsaPop3(impostazioni) Then
                _ultimoErrore = "Le cartelle si possono gestire solo sugli account IMAP."
                Return False
            End If

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorsoCartella)
                If cartella Is Nothing Then
                    _ultimoErrore = "Cartella non trovata."
                    Return False
                End If

                If cartella.Attributes.HasFlag(FolderAttributes.Inbox) Then
                    _ultimoErrore = "La posta in arrivo non si può rinominare né spostare."
                    Return False
                End If

                Dim padre As IMailFolder
                If String.Equals(percorsoNuovoPadre, PadreInvariato, StringComparison.Ordinal) Then
                    padre = cartella.ParentFolder
                ElseIf String.IsNullOrWhiteSpace(percorsoNuovoPadre) Then
                    padre = client.GetFolder(client.PersonalNamespaces(0))
                Else
                    padre = client.GetFolder(percorsoNuovoPadre)
                End If
                If padre Is Nothing Then
                    _ultimoErrore = "Cartella di destinazione non trovata."
                    Return False
                End If

                Dim nome As String = If(nuovoNome, "").Trim()
                If String.IsNullOrWhiteSpace(nome) Then nome = cartella.Name
                If String.IsNullOrWhiteSpace(nome) Then
                    _ultimoErrore = "Il nome della cartella non è valido."
                    Return False
                End If

                Try
                    If padre.DirectorySeparator <> ChrW(0) Then
                        nome = nome.Replace(padre.DirectorySeparator, " "c).Trim()
                    End If
                Catch exSeparatore As Exception
                    ScriviLog("CLSMail.RinominaCartellaImap separatore: " & exSeparatore.ToString())
                End Try

                If cartella.IsOpen Then cartella.Close()
                cartella.Rename(padre, nome)
                AzzeraCacheCartelleSpeciali()
                Return True
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.RinominaCartellaImap IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SocketException
            ImpostaErrore("CLSMail.RinominaCartellaImap rete (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As Exception
            ImpostaErrore("CLSMail.RinominaCartellaImap (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

    Public Function EliminaCartellaImap(chiaveAccount As String, percorsoCartella As String) As Boolean
        _ultimoErrore = ""
        Try
            If String.IsNullOrWhiteSpace(percorsoCartella) Then
                _ultimoErrore = "Cartella non indicata."
                Return False
            End If

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If UsaPop3(impostazioni) Then
                _ultimoErrore = "Le cartelle si possono gestire solo sugli account IMAP."
                Return False
            End If

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorsoCartella)
                If cartella Is Nothing Then
                    _ultimoErrore = "Cartella non trovata."
                    Return False
                End If

                ' Le cartelle di sistema non si toccano: cancellare la posta in arrivo, la posta
                ' inviata o il cestino romperebbe la casella, non solo WinGabriel.
                If cartella.Attributes.HasFlag(FolderAttributes.Inbox) OrElse
                   cartella.Attributes.HasFlag(FolderAttributes.Sent) OrElse
                   cartella.Attributes.HasFlag(FolderAttributes.Drafts) OrElse
                   cartella.Attributes.HasFlag(FolderAttributes.Trash) OrElse
                   cartella.Attributes.HasFlag(FolderAttributes.Junk) Then
                    _ultimoErrore = "Questa è una cartella di sistema della casella e non si può eliminare."
                    Return False
                End If

                Try
                    If cartella.Attributes.HasFlag(FolderAttributes.HasChildren) Then
                        _ultimoErrore = "La cartella contiene altre cartelle: elimina prima quelle."
                        Return False
                    End If
                Catch exFigli As Exception
                    ScriviLog("CLSMail.EliminaCartellaImap figli: " & exFigli.ToString())
                End Try

                If cartella.IsOpen Then cartella.Close()
                cartella.Delete()
                AzzeraCacheCartelleSpeciali()
                Return True
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.EliminaCartellaImap IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SocketException
            ImpostaErrore("CLSMail.EliminaCartellaImap rete (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaCartellaImap (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

End Class
