Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports MailKit
Imports MailKit.Net.Imap
Imports MailKit.Search
Imports MimeKit

' Filtraggio dei messaggi di posta e pulizia dell'oggetto dai prefissi di risposta e inoltro.
Public Enum CriterioFiltroMessaggi
    Mittente = 0
    Oggetto = 1
    Conversazione = 2
    Data = 3
End Enum

Partial Public Class CLSMail

    ' Toglie dall'oggetto i prefissi di risposta e di inoltro, italiani e inglesi, anche
    ' ripetuti. Serve a riconoscere che due messaggi appartengono alla stessa conversazione
    ' senza doverne seguire gli identificativi.
    Public Shared Function OggettoSenzaPrefissi(oggetto As String) As String
        Try
            Dim testo As String = If(oggetto, "").Trim()
            Dim prefissi As String() = New String() {"re:", "r:", "rif:", "fwd:", "fw:", "i:", "inoltro:", "aw:", "sv:", "vs:"}
            Dim tolto As Boolean = True
            While tolto
                tolto = False
                For Each prefisso As String In prefissi
                    If testo.StartsWith(prefisso, StringComparison.OrdinalIgnoreCase) Then
                        testo = testo.Substring(prefisso.Length).Trim()
                        tolto = True
                        Exit For
                    End If
                Next
                If testo.StartsWith("[") AndAlso testo.Contains("]") Then
                    Dim chiusura As Integer = testo.IndexOf("]"c)
                    If chiusura > 0 AndAlso chiusura < 40 Then
                        testo = testo.Substring(chiusura + 1).Trim()
                        tolto = True
                    End If
                End If
            End While
            Return testo
        Catch ex As Exception
            ScriviLog("CLSMail.OggettoSenzaPrefissi: " & ex.Message)
            Return If(oggetto, "")
        End Try
    End Function

    ' Filtra i messaggi di una cartella con un criterio solo. Il lavoro lo fa il server, come
    ' per la ricerca, quindi il filtro vede tutta la cartella e non la sola pagina caricata.
    Public Function CercaMessaggiConCriterio(chiaveAccount As String, nomeCartella As String,
                                             criterio As CriterioFiltroMessaggi, testo As String,
                                             dataDa As DateTime) As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Try
            Dim impostazioni As ImpostazioniAccountMail = OttieniAccount(chiaveAccount)
            If impostazioni Is Nothing Then Return elenco

            If UsaPop3(impostazioni) Then Return FiltraMessaggiSalvati(chiaveAccount, nomeCartella, criterio, testo, dataDa)

            Dim valore As String = If(testo, "").Trim()
            If criterio <> CriterioFiltroMessaggi.Data AndAlso String.IsNullOrWhiteSpace(valore) Then Return elenco

            Dim client As ImapClient = OttieniClientImapDaCache(chiaveAccount, impostazioni)
            Try
                Dim cartella As IMailFolder = RisolviCartellaPerPercorso(client, nomeCartella)
                If cartella Is Nothing Then
                    _ultimoErrore = "Non ho trovato sul server la cartella da filtrare."
                    Return elenco
                End If
                If Not cartella.IsOpen Then cartella.Open(FolderAccess.ReadOnly, Segnale())

                Dim interrogazione As SearchQuery = Nothing
                Select Case criterio
                    Case CriterioFiltroMessaggi.Mittente
                        interrogazione = SearchQuery.FromContains(valore)
                    Case CriterioFiltroMessaggi.Oggetto
                        interrogazione = SearchQuery.SubjectContains(valore)
                    Case CriterioFiltroMessaggi.Conversazione
                        interrogazione = SearchQuery.SubjectContains(OggettoSenzaPrefissi(valore))
                    Case CriterioFiltroMessaggi.Data
                        interrogazione = SearchQuery.DeliveredAfter(dataDa.Date.AddDays(-1))
                    Case Else
                        Return elenco
                End Select

                Dim identificativi As IList(Of UniqueId) = cartella.Search(interrogazione, Segnale())
                If identificativi Is Nothing OrElse identificativi.Count = 0 Then Return elenco

                Dim riepiloghi As IList(Of IMessageSummary) = cartella.Fetch(identificativi,
                    MessageSummaryItems.UniqueId Or MessageSummaryItems.Envelope Or MessageSummaryItems.Flags, Segnale())

                For Each voce As IMessageSummary In riepiloghi
                    Try
                        Dim flagLetto As Boolean = voce.Flags.GetValueOrDefault(MessageFlags.None).HasFlag(MessageFlags.Seen)
                        elenco.Add(New RiepilogoMessaggio() With {
                            .ChiaveAccount = chiaveAccount,
                            .Id = voce.UniqueId.Id.ToString(),
                            .Oggetto = If(voce.Envelope IsNot Nothing, voce.Envelope.Subject, ""),
                            .Mittente = If(voce.Envelope IsNot Nothing AndAlso voce.Envelope.From.Count > 0, voce.Envelope.From(0).ToString(), ""),
                            .Data = If(voce.Envelope IsNot Nothing, voce.Envelope.Date.GetValueOrDefault(), DateTimeOffset.MinValue),
                            .Letto = flagLetto
                        })
                    Catch exVoce As Exception
                        ScriviLog("CLSMail.CercaMessaggiConCriterio voce: " & exVoce.Message)
                    End Try
                Next
            Finally
                RestituisciClientImapAllaCache(chiaveAccount, client)
            End Try
        Catch ex As Exception
            ImpostaErrore("CLSMail.CercaMessaggiConCriterio (account " & If(chiaveAccount, "") & ")", ex)
            ScriviLog("CLSMail.CercaMessaggiConCriterio (account " & If(chiaveAccount, "") & "): " & ex.ToString())
        End Try
        Return elenco
    End Function

    ' Le caselle POP3 non hanno un server da interrogare: il filtro lavora su quello che è
    ' stato scaricato e salvato nel database.
    Private Function FiltraMessaggiSalvati(chiaveAccount As String, nomeCartella As String,
                                           criterio As CriterioFiltroMessaggi, testo As String,
                                           dataDa As DateTime) As List(Of RiepilogoMessaggio)
        Dim elenco As New List(Of RiepilogoMessaggio)
        Try
            Dim salvati As DataTable = OttieniMessaggiSalvati(chiaveAccount, nomeCartella)
            If salvati Is Nothing Then Return elenco
            Dim cercato As String = If(testo, "").Trim().ToLowerInvariant()
            If criterio = CriterioFiltroMessaggi.Conversazione Then cercato = OggettoSenzaPrefissi(cercato).ToLowerInvariant()

            For Each riga As DataRow In salvati.Rows
                Try
                    Dim oggetto As String = riga("Oggetto").ToString()
                    Dim mittente As String = riga("Mittente").ToString()
                    Dim mittenteEmail As String = riga("MittenteEmail").ToString()
                    Dim data As DateTimeOffset = DateTimeOffset.MinValue
                    DateTimeOffset.TryParse(riga("DataMessaggio").ToString(), data)

                    Dim tenuto As Boolean = False
                    Select Case criterio
                        Case CriterioFiltroMessaggi.Mittente
                            tenuto = mittente.ToLowerInvariant().Contains(cercato) OrElse mittenteEmail.ToLowerInvariant().Contains(cercato)
                        Case CriterioFiltroMessaggi.Oggetto
                            tenuto = oggetto.ToLowerInvariant().Contains(cercato)
                        Case CriterioFiltroMessaggi.Conversazione
                            tenuto = OggettoSenzaPrefissi(oggetto).ToLowerInvariant().Contains(cercato)
                        Case CriterioFiltroMessaggi.Data
                            tenuto = data <> DateTimeOffset.MinValue AndAlso data.LocalDateTime.Date >= dataDa.Date
                    End Select
                    If Not tenuto Then Continue For

                    elenco.Add(New RiepilogoMessaggio() With {
                        .ChiaveAccount = chiaveAccount,
                        .Id = riga("UidServer").ToString(),
                        .Oggetto = oggetto,
                        .Mittente = mittente,
                        .Data = data,
                        .Letto = Convert.ToInt32(riga("Letto")) = 1
                    })
                Catch exRiga As Exception
                    ScriviLog("CLSMail.FiltraMessaggiSalvati riga: " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSMail.FiltraMessaggiSalvati: " & ex.ToString())
        End Try
        Return elenco
    End Function

End Class
