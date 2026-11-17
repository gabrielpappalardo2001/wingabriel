Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Sockets
Imports System.Text
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Net.Smtp
Imports MailKit.Security
Imports MimeKit
Imports MimeKit.Text
Imports MimeKit.Utils

' Gestione della creazione, dell'invio e del salvataggio delle bozze dei messaggi di posta.
Public Class ImmagineInlineMessaggio
    Public Property Cid As String = ""
    Public Property NomeFile As String = ""
    Public Property TipoMime As String = ""
    Public Property Dati As Byte() = Nothing
End Class

Public Class ContenutoMessaggio
    Public Property Destinatari As List(Of String) = New List(Of String)()
    Public Property Cc As List(Of String) = New List(Of String)()
    Public Property Ccn As List(Of String) = New List(Of String)()
    Public Property Oggetto As String = ""
    Public Property CorpoHtml As String = ""
    Public Property CorpoTesto As String = ""
    Public Property ImmaginiInline As List(Of ImmagineInlineMessaggio) = New List(Of ImmagineInlineMessaggio)()
    Public Property PercorsiAllegati As List(Of String) = New List(Of String)()
    Public Property InRispostaA As String = ""
    Public Property Riferimenti As String = ""
End Class

Partial Public Class CLSMail

    Private Function CostruisciMessaggioPosta(impostazioni As ImpostazioniAccountMail, contenuto As ContenutoMessaggio, richiedeDestinatari As Boolean) As MimeMessage
        Try
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return Nothing
            End If
            If contenuto Is Nothing Then
                _ultimoErrore = "Nessun contenuto da inviare."
                Return Nothing
            End If

            Dim messaggio As New MimeMessage()

            Try
                messaggio.From.Add(New MailboxAddress(
                    If(String.IsNullOrWhiteSpace(impostazioni.NomeVisualizzato), impostazioni.IndirizzoEmail, impostazioni.NomeVisualizzato),
                    impostazioni.IndirizzoEmail))
            Catch exFrom As Exception
                ImpostaErrore("CLSMail.CostruisciMessaggioPosta mittente", exFrom)
                Return Nothing
            End Try

            Dim destinatariValidi As Integer = 0
            If contenuto.Destinatari IsNot Nothing Then
                For Each destinatario As String In contenuto.Destinatari
                    If AggiungiIndirizzoSicuro(messaggio.To, destinatario, "CLSMail.CostruisciMessaggioPosta To") Then destinatariValidi += 1
                Next
            End If

            If richiedeDestinatari AndAlso destinatariValidi = 0 Then
                _ultimoErrore = "Nessun destinatario valido."
                Return Nothing
            End If

            If contenuto.Cc IsNot Nothing Then
                For Each indirizzo As String In contenuto.Cc
                    AggiungiIndirizzoSicuro(messaggio.Cc, indirizzo, "CLSMail.CostruisciMessaggioPosta Cc")
                Next
            End If
            If contenuto.Ccn IsNot Nothing Then
                For Each indirizzo As String In contenuto.Ccn
                    AggiungiIndirizzoSicuro(messaggio.Bcc, indirizzo, "CLSMail.CostruisciMessaggioPosta Ccn")
                Next
            End If

            messaggio.Subject = If(contenuto.Oggetto, "")
            messaggio.Date = DateTimeOffset.Now
            Try
                messaggio.MessageId = MimeUtils.GenerateMessageId(OttieniDominioDaEmail(impostazioni.IndirizzoEmail))
            Catch exIdentificativo As Exception
                ScriviLog("CLSMail.CostruisciMessaggioPosta identificativo: " & exIdentificativo.Message)
            End Try

            AgganciaAllaConversazione(messaggio, contenuto.InRispostaA, contenuto.Riferimenti)

            Dim haHtml As Boolean = Not String.IsNullOrWhiteSpace(contenuto.CorpoHtml)
            Dim haImmagini As Boolean = contenuto.ImmaginiInline IsNot Nothing AndAlso contenuto.ImmaginiInline.Count > 0
            Dim haAllegati As Boolean = contenuto.PercorsiAllegati IsNot Nothing AndAlso contenuto.PercorsiAllegati.Count > 0

            If Not haHtml AndAlso Not haImmagini AndAlso Not haAllegati Then
                Dim corpoSemplice As New TextPart(TextFormat.Plain)
                corpoSemplice.SetText(Encoding.UTF8, If(contenuto.CorpoTesto, ""))
                corpoSemplice.ContentTransferEncoding = ContentEncoding.QuotedPrintable
                messaggio.Body = corpoSemplice
                Return messaggio
            End If

            Dim generatore As New BodyBuilder()

            If haHtml Then
                generatore.HtmlBody = If(contenuto.CorpoHtml, "")
                generatore.TextBody = If(String.IsNullOrWhiteSpace(contenuto.CorpoTesto), TestoAlternativoDaHtml(contenuto.CorpoHtml), contenuto.CorpoTesto)
            Else
                generatore.TextBody = If(contenuto.CorpoTesto, "")
            End If

            If haImmagini Then
                For Each immagine As ImmagineInlineMessaggio In contenuto.ImmaginiInline
                    Try
                        If immagine Is Nothing OrElse immagine.Dati Is Nothing OrElse immagine.Dati.Length = 0 Then Continue For
                        Dim nomeRisorsa As String = If(String.IsNullOrWhiteSpace(immagine.NomeFile), "immagine.jpg", immagine.NomeFile)
                        Dim risorsa As MimeEntity = generatore.LinkedResources.Add(nomeRisorsa, immagine.Dati)
                        If risorsa Is Nothing Then Continue For
                        If Not String.IsNullOrWhiteSpace(immagine.TipoMime) Then
                            Try
                                Dim partiTipo() As String = immagine.TipoMime.Split("/"c)
                                If partiTipo.Length = 2 AndAlso risorsa.ContentType IsNot Nothing Then
                                    risorsa.ContentType.MediaType = partiTipo(0).Trim()
                                    risorsa.ContentType.MediaSubtype = partiTipo(1).Trim()
                                End If
                            Catch exTipo As Exception
                                ScriviLog("CLSMail.CostruisciMessaggioPosta tipo immagine: " & exTipo.Message)
                            End Try
                        End If
                        If Not String.IsNullOrWhiteSpace(immagine.Cid) Then risorsa.ContentId = immagine.Cid
                        If risorsa.ContentDisposition Is Nothing Then
                            risorsa.ContentDisposition = New ContentDisposition(ContentDisposition.Inline)
                        Else
                            risorsa.ContentDisposition.Disposition = ContentDisposition.Inline
                        End If
                    Catch exImmagine As Exception
                        ScriviLog("CLSMail.CostruisciMessaggioPosta immagine inline: " & exImmagine.Message)
                    End Try
                Next
            End If

            If haAllegati Then
                For Each percorsoFile As String In contenuto.PercorsiAllegati
                    Try
                        Dim allegato As MimeEntity = CostruisciAllegatoMime(percorsoFile)
                        If allegato IsNot Nothing Then generatore.Attachments.Add(allegato)
                    Catch exAllegato As Exception
                        ImpostaErrore("CLSMail.CostruisciMessaggioPosta allegato (" & If(percorsoFile, "") & ")", exAllegato)
                    End Try
                Next
            End If

            messaggio.Body = generatore.ToMessageBody()
            ForzaCharsetUtf8(messaggio.Body)

            Return messaggio
        Catch ex As Exception
            ImpostaErrore("CLSMail.CostruisciMessaggioPosta", ex)
            Return Nothing
        End Try
    End Function

    Private Function TestoAlternativoDaHtml(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""
            Return HtmlVersoTesto(html)
        Catch ex As Exception
            ScriviLog("CLSMail.TestoAlternativoDaHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function InviaMessaggioCompleto(chiaveAccount As String, contenuto As ContenutoMessaggio) As Boolean
        _ultimoErrore = ""
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If contenuto Is Nothing OrElse contenuto.Destinatari Is Nothing OrElse contenuto.Destinatari.Count = 0 Then
                _ultimoErrore = "Nessun destinatario indicato."
                Return False
            End If

            Dim messaggio As MimeMessage = CostruisciMessaggioPosta(impostazioni, contenuto, True)
            If messaggio Is Nothing Then
                If String.IsNullOrWhiteSpace(_ultimoErrore) Then _ultimoErrore = "Impossibile comporre il messaggio."
                Return False
            End If

            Using client As SmtpClient = CreaClientSmtp(impostazioni)
                Try
                    client.Send(messaggio)
                Catch exSend As SmtpCommandException
                    ImpostaErrore("CLSMail.InviaMessaggioCompleto SMTP comando", exSend)
                    Try
                        client.Disconnect(True)
                    Catch exChiusura As Exception
                        ScriviLog("CLSMail.InviaMessaggioCompleto disconnect: " & exChiusura.Message)
                    End Try
                    Return False
                Catch exProto As SmtpProtocolException
                    ImpostaErrore("CLSMail.InviaMessaggioCompleto SMTP protocollo", exProto)
                    Try
                        client.Disconnect(True)
                    Catch exChiusura As Exception
                        ScriviLog("CLSMail.InviaMessaggioCompleto disconnect: " & exChiusura.Message)
                    End Try
                    Return False
                End Try
                Try
                    client.Disconnect(True)
                Catch exDisc As Exception
                    ScriviLog("CLSMail.InviaMessaggioCompleto disconnect: " & DescriviErroreMailKit(exDisc))
                End Try
            End Using

            If Not UsaPop3(impostazioni) Then
                Try
                    SalvaMessaggioInviatoSulServer(impostazioni, messaggio)
                Catch exSent As Exception
                    ScriviLog("CLSMail.InviaMessaggioCompleto salvataggio inviata: " & DescriviErroreMailKit(exSent))
                End Try
            End If

            Try
                RegistraDestinatariRecenti(messaggio.To.Mailboxes.
                    Concat(messaggio.Cc.Mailboxes).
                    Concat(messaggio.Bcc.Mailboxes))
            Catch exRecenti As Exception
                ScriviLog("CLSMail.InviaMessaggioCompleto destinatari recenti: " & exRecenti.Message)
            End Try

            _ultimoErrore = ""
            Return True
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto autenticazione (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto SSL (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SmtpCommandException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto SMTP (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SmtpProtocolException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto SMTP (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As ParseException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto MIME (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As SocketException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto rete (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As IOException
            ImpostaErrore("CLSMail.InviaMessaggioCompleto IO (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        Catch ex As Exception
            ImpostaErrore("CLSMail.InviaMessaggioCompleto (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

    Public Function SalvaBozzaSulServer(chiaveAccount As String, contenuto As ContenutoMessaggio, identificativoPrecedente As String) As String
        _ultimoErrore = ""
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return ""
            End If

            If UsaPop3(impostazioni) Then
                _ultimoErrore = "L'account non usa IMAP: la bozza non puo' essere salvata sul server."
                Return ""
            End If

            Dim messaggio As MimeMessage = CostruisciMessaggioPosta(impostazioni, contenuto, False)
            If messaggio Is Nothing Then
                If String.IsNullOrWhiteSpace(_ultimoErrore) Then _ultimoErrore = "Impossibile comporre la bozza."
                Return ""
            End If

            Dim percorso As String = RisolviPercorsoCartellaSpeciale(impostazioni.Chiave, "drafts")
            If String.IsNullOrWhiteSpace(percorso) Then percorso = "Drafts"

            Dim identificativoNuovo As String = ""

            Dim client As ImapClient = OttieniClientImapDaCache(impostazioni.Chiave, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorso)
                If Not cartella.IsOpen Then cartella.Open(FolderAccess.ReadWrite, Segnale())

                Dim aggiunto As Nullable(Of UniqueId) = cartella.Append(messaggio, MessageFlags.Draft Or MessageFlags.Seen)
                If aggiunto.HasValue Then identificativoNuovo = aggiunto.Value.Id.ToString()

                ' Eliminazione asincrona della bozza precedente: non blocca il ritorno dell'ID della nuova bozza.
                If Not String.IsNullOrWhiteSpace(identificativoPrecedente) Then
                    Dim azione As New Action(Sub()
                        Try
                            Dim numero As UInteger = 0
                            If UInteger.TryParse(identificativoPrecedente.Trim(), numero) Then
                                Dim daRimuovere As New List(Of UniqueId)()
                                daRimuovere.Add(New UniqueId(numero))
                                cartella.Store(daRimuovere, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) With {.Silent = True})
                            End If
                        Catch exVecchia As Exception
                            ScriviLog("CLSMail.SalvaBozzaSulServer rimozione bozza precedente (asincrona): " & DescriviErroreMailKit(exVecchia))
                        End Try
                    End Sub)
                    System.Threading.Tasks.Task.Run(azione)
                End If

                ' Non chiudere la cartella: lasciandola selezionata nella connessione in cache si evitano round-trip IMAP aggiuntivi per salvataggi ripetuti.
            Finally
                RestituisciClientImapAllaCache(impostazioni.Chiave, client)
            End Try

            _ultimoErrore = ""
            Return identificativoNuovo
        Catch ex As AuthenticationException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer autenticazione (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As SslHandshakeException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer SSL (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As FolderNotFoundException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer cartella (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As ImapCommandException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As ImapProtocolException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer IMAP (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As SocketException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer rete (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As IOException
            ImpostaErrore("CLSMail.SalvaBozzaSulServer IO (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        Catch ex As Exception
            ImpostaErrore("CLSMail.SalvaBozzaSulServer (account " & If(chiaveAccount, "") & ")", ex)
            Return ""
        End Try
    End Function

    Public Function EliminaBozzaSulServer(chiaveAccount As String, identificativoBozza As String) As Boolean
        _ultimoErrore = ""
        Try
            If String.IsNullOrWhiteSpace(identificativoBozza) Then Return False

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return False
            End If
            If UsaPop3(impostazioni) Then Return False

            Dim numero As UInteger = 0
            If Not UInteger.TryParse(identificativoBozza.Trim(), numero) Then Return False

            Dim percorso As String = RisolviPercorsoCartellaSpeciale(impostazioni.Chiave, "drafts")
            If String.IsNullOrWhiteSpace(percorso) Then percorso = "Drafts"

            Dim client As ImapClient = OttieniClientImapDaCache(impostazioni.Chiave, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorso)
                cartella.Open(FolderAccess.ReadWrite, Segnale())
                Dim daRimuovere As New List(Of UniqueId)()
                daRimuovere.Add(New UniqueId(numero))
                cartella.Store(daRimuovere, New StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) With {.Silent = True})
                cartella.Expunge(daRimuovere)
                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(impostazioni.Chiave, client)
            End Try

            Return True
        Catch ex As Exception
            ImpostaErrore("CLSMail.EliminaBozzaSulServer (account " & If(chiaveAccount, "") & ")", ex)
            Return False
        End Try
    End Function

End Class