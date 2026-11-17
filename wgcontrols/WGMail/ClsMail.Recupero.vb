Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Security
Imports MimeKit

' Gestione e recupero delle bozze di posta e dei messaggi grezzi. Estrae allegati, immagini inline e parti del messaggio
' convertendo il testo in HTML.
Public Class BozzaRecuperata
    Public Property Identificativo As String = ""
    Public Property Destinatari As List(Of String) = New List(Of String)()
    Public Property Cc As List(Of String) = New List(Of String)()
    Public Property Ccn As List(Of String) = New List(Of String)()
    Public Property Oggetto As String = ""
    Public Property CorpoHtml As String = ""
    Public Property CorpoTesto As String = ""
    Public Property ImmaginiInline As Dictionary(Of String, Byte()) = New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
    Public Property PercorsiAllegati As List(Of String) = New List(Of String)()
End Class

Partial Public Class CLSMail

    Private Function CartellaAllegatiTemporanei() As String
        Try
            Dim cartella As String = ""
            Try
                cartella = WGDb.WGDatabase.CartellaDati(Path.Combine("posta", "allegati_bozza"))
            Catch exConfigurata As Exception
                ScriviLog("CLSMail.CartellaAllegatiTemporanei (cartella configurata): " & exConfigurata.Message)
                cartella = ""
            End Try
            If String.IsNullOrWhiteSpace(cartella) Then
                cartella = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "posta", "allegati_bozza")
            End If
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return cartella
        Catch ex As Exception
            ScriviLog("CLSMail.CartellaAllegatiTemporanei: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeFileSicuro(nome As String, indice As Integer) As String
        Try
            Dim proposto As String = If(nome, "").Trim()
            If String.IsNullOrWhiteSpace(proposto) Then proposto = "allegato" & indice.ToString(System.Globalization.CultureInfo.InvariantCulture)
            For Each carattere As Char In Path.GetInvalidFileNameChars()
                proposto = proposto.Replace(carattere, "_"c)
            Next
            If proposto.Length > 120 Then proposto = proposto.Substring(0, 120)
            Return proposto
        Catch ex As Exception
            ScriviLog("CLSMail.NomeFileSicuro: " & ex.Message)
            Return "allegato" & indice.ToString(System.Globalization.CultureInfo.InvariantCulture)
        End Try
    End Function

    Private Sub EstraiPartiDaMessaggio(mimeMsg As MimeMessage, risultato As BozzaRecuperata)
        Try
            If mimeMsg Is Nothing OrElse risultato Is Nothing Then Return

            Dim cartella As String = CartellaAllegatiTemporanei()
            Dim indice As Integer = 0

            For Each parte As MimeEntity In mimeMsg.BodyParts
                Try
                    indice += 1
                    Dim mimeParte As MimePart = TryCast(parte, MimePart)
                    If mimeParte Is Nothing Then Continue For

                    Dim eTesto As Boolean = TypeOf mimeParte Is TextPart AndAlso Not mimeParte.IsAttachment
                    If eTesto Then Continue For

                    Dim identificativo As String = If(mimeParte.ContentId, "")
                    identificativo = identificativo.Trim().TrimStart("<"c).TrimEnd(">"c).Trim()

                    Dim eImmagine As Boolean = False
                    Try
                        eImmagine = mimeParte.ContentType IsNot Nothing AndAlso
                                    String.Equals(mimeParte.ContentType.MediaType, "image", StringComparison.OrdinalIgnoreCase)
                    Catch exTipo As Exception
                        ScriviLog("CLSMail.EstraiPartiDaMessaggio tipo: " & exTipo.Message)
                    End Try

                    Dim eInline As Boolean = eImmagine AndAlso Not String.IsNullOrWhiteSpace(identificativo)

                    If eInline Then
                        Using memoria As New MemoryStream()
                            mimeParte.Content.DecodeTo(memoria)
                            Dim dati As Byte() = memoria.ToArray()
                            If dati IsNot Nothing AndAlso dati.Length > 0 AndAlso Not risultato.ImmaginiInline.ContainsKey(identificativo) Then
                                risultato.ImmaginiInline.Add(identificativo, dati)
                            End If
                        End Using
                        Continue For
                    End If

                    If Not mimeParte.IsAttachment AndAlso String.IsNullOrWhiteSpace(mimeParte.FileName) Then Continue For
                    If String.IsNullOrWhiteSpace(cartella) Then Continue For

                    Dim nomeFile As String = NomeFileSicuro(mimeParte.FileName, indice)
                    Dim percorso As String = Path.Combine(cartella, nomeFile)
                    Using flusso As New FileStream(percorso, FileMode.Create, FileAccess.Write)
                        mimeParte.Content.DecodeTo(flusso)
                    End Using
                    risultato.PercorsiAllegati.Add(percorso)
                Catch exParte As Exception
                    ScriviLog("CLSMail.EstraiPartiDaMessaggio parte: " & DescriviErroreMailKit(exParte))
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.EstraiPartiDaMessaggio: " & ex.Message)
        End Try
    End Sub

    Public Function RecuperaMessaggioGrezzo(chiaveAccount As String, nomeCartella As String, identificativo As String) As BozzaRecuperata
        _ultimoErrore = ""
        Dim risultato As New BozzaRecuperata()
        Try
            If String.IsNullOrWhiteSpace(identificativo) Then
                _ultimoErrore = "Messaggio non indicato."
                Return Nothing
            End If

            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then
                _ultimoErrore = "Account non trovato."
                Return Nothing
            End If
            If UsaPop3(impostazioni) Then
                _ultimoErrore = "L'account non usa IMAP."
                Return Nothing
            End If

            Dim numero As UInteger = 0
            If Not UInteger.TryParse(identificativo.Trim(), numero) Then
                _ultimoErrore = "Identificativo del messaggio non valido."
                Return Nothing
            End If

            Dim percorsoCartella As String = nomeCartella
            If String.IsNullOrWhiteSpace(percorsoCartella) Then percorsoCartella = RisolviPercorsoCartellaSpeciale(impostazioni.Chiave, "drafts")
            If String.IsNullOrWhiteSpace(percorsoCartella) Then percorsoCartella = "Drafts"

            Dim mimeMsg As MimeMessage = Nothing
            Dim client As ImapClient = OttieniClientImapDaCache(impostazioni.Chiave, impostazioni)
            Try
                Dim cartella As IMailFolder = client.GetFolder(percorsoCartella)
                cartella.Open(FolderAccess.ReadOnly, Segnale())
                mimeMsg = cartella.GetMessage(New UniqueId(numero))
                cartella.Close()
            Finally
                RestituisciClientImapAllaCache(impostazioni.Chiave, client)
            End Try

            If mimeMsg Is Nothing Then
                _ultimoErrore = "Messaggio non trovato sul server."
                Return Nothing
            End If

            risultato.Identificativo = identificativo.Trim()
            risultato.Oggetto = If(mimeMsg.Subject, "")
            risultato.CorpoHtml = If(mimeMsg.HtmlBody, "")
            risultato.CorpoTesto = If(mimeMsg.TextBody, "")

            For Each indirizzo As MailboxAddress In mimeMsg.To.Mailboxes
                If Not String.IsNullOrWhiteSpace(indirizzo.Address) Then risultato.Destinatari.Add(indirizzo.Address)
            Next
            For Each indirizzo As MailboxAddress In mimeMsg.Cc.Mailboxes
                If Not String.IsNullOrWhiteSpace(indirizzo.Address) Then risultato.Cc.Add(indirizzo.Address)
            Next
            For Each indirizzo As MailboxAddress In mimeMsg.Bcc.Mailboxes
                If Not String.IsNullOrWhiteSpace(indirizzo.Address) Then risultato.Ccn.Add(indirizzo.Address)
            Next

            EstraiPartiDaMessaggio(mimeMsg, risultato)

            If String.IsNullOrWhiteSpace(risultato.CorpoHtml) AndAlso Not String.IsNullOrWhiteSpace(risultato.CorpoTesto) Then
                risultato.CorpoHtml = TestoSempliceInHtml(risultato.CorpoTesto)
            End If

            _ultimoErrore = ""
            Return risultato
        Catch ex As Exception
            ImpostaErrore("CLSMail.RecuperaMessaggioGrezzo (account " & If(chiaveAccount, "") & ")", ex)
            Return Nothing
        End Try
    End Function

    Public Shared Function TestoSempliceInHtml(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim sb As New StringBuilder()
            sb.Append("<html><body>")
            Dim righe() As String = testo.Replace(vbCrLf, vbLf).Replace(ChrW(13), ChrW(10)).Split(ChrW(10))
            For Each riga As String In righe
                sb.Append("<p>")
                Dim sicuro As String = riga.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                If String.IsNullOrWhiteSpace(sicuro) Then
                    sb.Append("&nbsp;")
                Else
                    sb.Append(sicuro)
                End If
                sb.Append("</p>")
            Next
            sb.Append("</body></html>")
            Return sb.ToString()
        Catch ex As Exception
            ScriviLog("CLSMail.TestoSempliceInHtml: " & ex.Message)
            Return ""
        End Try
    End Function

End Class