Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports wgcontrols
Imports WGContatti
Imports WGDati
Imports WGMail

' Sincronizzazione automatica in sottofondo della rubrica e del calendario con i server remoti.
Public Class SincronizzazioneWG

    Public Const ChiaveRubricaAttiva As String = "SincronizzazioneRubricaAutomatica"
    Public Const ChiaveRubricaMinuti As String = "SincronizzazioneRubricaMinuti"
    Public Const ChiaveAgendaAttiva As String = "SincronizzazioneAgendaAutomatica"
    Public Const ChiaveAgendaMinuti As String = "SincronizzazioneAgendaMinuti"

    Private Const MinutiMinimi As Integer = 5
    Private Const MinutiPredefiniti As Integer = 30

    Private Shared ReadOnly _lockEsecuzione As New Object()
    Private Shared _rubricaInCorso As Boolean = False
    Private Shared _agendaInCorso As Boolean = False
    Private Shared _ultimoMessaggio As String = ""

    Public Shared ReadOnly Property UltimoMessaggio As String
        Get
            Return If(_ultimoMessaggio, "")
        End Get
    End Property

    Public Shared ReadOnly Property RubricaInCorso As Boolean
        Get
            SyncLock _lockEsecuzione
                Return _rubricaInCorso
            End SyncLock
        End Get
    End Property

    Public Shared ReadOnly Property AgendaInCorso As Boolean
        Get
            SyncLock _lockEsecuzione
                Return _agendaInCorso
            End SyncLock
        End Get
    End Property

    Private Shared Sub Registra(messaggio As String)
        Try
            Dim registro As New Logger()
        Catch
        End Try
    End Sub

    Private Shared Sub RegistraErrore(contesto As String, ex As Exception)
        Try
            Dim registro As New Logger()
            If ex Is Nothing Then
                Return
            End If
        Catch
        End Try
    End Sub

    Private Shared Function LeggiConfigurazione(chiave As String) As String
        Try
            Dim configurazione As New WGConfig()
            Return If(configurazione.LeggiValore(chiave), "")
        Catch ex As Exception
            RegistraErrore("LeggiConfigurazione (" & If(chiave, "") & ")", ex)
            Return ""
        End Try
    End Function

    Private Shared Sub SalvaConfigurazione(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore(chiave, valore)
        Catch ex As Exception
            RegistraErrore("SalvaConfigurazione (" & If(chiave, "") & ")", ex)
        End Try
    End Sub

    Private Shared Function LeggiMinuti(chiave As String) As Integer
        Try
            Dim testo As String = LeggiConfigurazione(chiave)
            Dim minuti As Integer = 0
            If Not Integer.TryParse(testo, minuti) Then Return MinutiPredefiniti
            If minuti < MinutiMinimi Then Return MinutiMinimi
            Return minuti
        Catch ex As Exception
            RegistraErrore("LeggiMinuti (" & If(chiave, "") & ")", ex)
            Return MinutiPredefiniti
        End Try
    End Function

    Public Shared Function RubricaAutomaticaAttiva() As Boolean
        Try
            Return LeggiConfigurazione(ChiaveRubricaAttiva) = "1"
        Catch ex As Exception
            RegistraErrore("RubricaAutomaticaAttiva", ex)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaRubricaAutomatica(attiva As Boolean)
        Try
            SalvaConfigurazione(ChiaveRubricaAttiva, If(attiva, "1", "0"))
        Catch ex As Exception
            RegistraErrore("ImpostaRubricaAutomatica", ex)
        End Try
    End Sub

    Public Shared Function IntervalloRubricaMinuti() As Integer
        Return LeggiMinuti(ChiaveRubricaMinuti)
    End Function

    Public Shared Sub ImpostaIntervalloRubricaMinuti(minuti As Integer)
        Try
            Dim valore As Integer = minuti
            If valore < MinutiMinimi Then valore = MinutiMinimi
            SalvaConfigurazione(ChiaveRubricaMinuti, valore.ToString())
        Catch ex As Exception
            RegistraErrore("ImpostaIntervalloRubricaMinuti", ex)
        End Try
    End Sub

    Public Shared Function AgendaAutomaticaAttiva() As Boolean
        Try
            Return LeggiConfigurazione(ChiaveAgendaAttiva) = "1"
        Catch ex As Exception
            RegistraErrore("AgendaAutomaticaAttiva", ex)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaAgendaAutomatica(attiva As Boolean)
        Try
            SalvaConfigurazione(ChiaveAgendaAttiva, If(attiva, "1", "0"))
        Catch ex As Exception
            RegistraErrore("ImpostaAgendaAutomatica", ex)
        End Try
    End Sub

    Public Shared Function IntervalloAgendaMinuti() As Integer
        Return LeggiMinuti(ChiaveAgendaMinuti)
    End Function

    Public Shared Sub ImpostaIntervalloAgendaMinuti(minuti As Integer)
        Try
            Dim valore As Integer = minuti
            If valore < MinutiMinimi Then valore = MinutiMinimi
            SalvaConfigurazione(ChiaveAgendaMinuti, valore.ToString())
        Catch ex As Exception
            RegistraErrore("ImpostaIntervalloAgendaMinuti", ex)
        End Try
    End Sub

    Public Shared Function EsisteRubricaSincronizzabile() As Boolean
        Try
            Using posta As New CLSMail()
                Return posta.EsisteAccountConRubrica()
            End Using
        Catch ex As Exception
            RegistraErrore("EsisteRubricaSincronizzabile", ex)
            Return False
        End Try
    End Function

    Public Shared Function EsisteAgendaSincronizzabile() As Boolean
        Try
            Using posta As New CLSMail()
                Return posta.EsisteAccountConAgenda()
            End Using
        Catch ex As Exception
            RegistraErrore("EsisteAgendaSincronizzabile", ex)
            Return False
        End Try
    End Function

    Public Shared Function AccountPredefinitoRubrica() As String
        Try
            Using posta As New CLSMail()
                Dim elenco As List(Of ImpostazioniAccountMail) = posta.OttieniAccountConRubrica(True)
                If elenco Is Nothing OrElse elenco.Count = 0 Then Return ""
                Return elenco(0).Chiave
            End Using
        Catch ex As Exception
            RegistraErrore("AccountPredefinitoRubrica", ex)
            Return ""
        End Try
    End Function

    Public Shared Function SincronizzaRubrica(Optional contattiEsterni As CLSContatti = Nothing) As Integer
        Dim totale As Integer = 0
        Dim contatti As CLSContatti = contattiEsterni
        Dim contattiCreati As Boolean = False

        SyncLock _lockEsecuzione
            If _rubricaInCorso Then Return 0
            _rubricaInCorso = True
        End SyncLock

        Try
            If contatti Is Nothing Then
                contatti = New CLSContatti()
                contattiCreati = True
            End If

            Dim avviso As String = ""
            Using posta As New CLSMail()
                If Not posta.EsisteAccountConRubrica() Then
                    _ultimoMessaggio = "Nessun account con il supporto alla sincronizzazione dei contatti configurato."
                    Return 0
                End If
                totale = posta.SincronizzaRubricaCompleta(contatti, "")
                avviso = posta.UltimoErrore
                If String.IsNullOrWhiteSpace(avviso) Then
                    _ultimoMessaggio = "Rubrica sincronizzata. Contatti sincronizzati: " & totale.ToString() & "."
                Else
                    _ultimoMessaggio = "Rubrica sincronizzata con avvisi. Contatti sincronizzati: " & totale.ToString() & ". " & avviso
                End If
            End Using

            ' Logghiamo solo quando e' successo qualcosa (contatti sincronizzati o un avviso): un
            ' ciclo automatico senza nulla da fare non deve riempire il log ogni pochi minuti.
            If totale > 0 OrElse Not String.IsNullOrWhiteSpace(avviso) Then
                Registra("SincronizzaRubrica: " & _ultimoMessaggio)
            End If
        Catch ex As Exception
            RegistraErrore("SincronizzaRubrica", ex)
            _ultimoMessaggio = "Errore durante la sincronizzazione della rubrica: " & ex.Message
        Finally
            Try
                If contattiCreati AndAlso contatti IsNot Nothing Then contatti.Dispose()
            Catch exChiusura As Exception
                RegistraErrore("SincronizzaRubrica (chiusura rubrica)", exChiusura)
            End Try
            SyncLock _lockEsecuzione
                _rubricaInCorso = False
            End SyncLock
        End Try

        Return totale
    End Function

    Public Shared Function SincronizzaAgenda() As Integer
        Dim totale As Integer = 0

        SyncLock _lockEsecuzione
            If _agendaInCorso Then Return 0
            _agendaInCorso = True
        End SyncLock

        Try
            Dim agenda As CLSAgenda = CLSAgenda.GetInstance()
            If agenda Is Nothing Then
                _ultimoMessaggio = "Agenda non disponibile."
                Return 0
            End If

            Using posta As New CLSMail()
                Dim account As New List(Of ImpostazioniAccountMail)
                For Each impostazioni As ImpostazioniAccountMail In posta.OttieniElencoAccount(True)
                    If posta.AccountSupportaAgenda(impostazioni) Then
                        account.Add(impostazioni)
                    Else
                        agenda.RimuoviAgendaAccount(impostazioni.Chiave)
                    End If
                Next
                If account.Count = 0 Then
                    _ultimoMessaggio = "Nessun account configurato con il supporto alla sincronizzazione dei calendari configurato."
                    Return 0
                End If

                                For Each impostazioni As ImpostazioniAccountMail In account
                    Try
                        totale += EliminaEventiSulServer(posta, agenda, impostazioni.Chiave)
                        totale += InviaEventiAlServer(posta, agenda, impostazioni.Chiave)
                        totale += ScaricaEventiDalServer(posta, agenda, impostazioni.Chiave)
                    Catch exAccount As Exception

                    End Try
                Next
            End Using

            _ultimoMessaggio = "Agenda sincronizzata. Elementi sincronizzati: " & totale.ToString() & "."
            ' Come per la rubrica: un ciclo automatico senza nulla da sincronizzare non deve
            ' generare una riga di log ogni volta, altrimenti in poco tempo seppellisce gli
            ' avvisi/errori veri scritti da EliminaEventiSulServer/InviaEventiAlServer/CLSMail.
            If totale > 0 Then
                Registra("SincronizzaAgenda: " & _ultimoMessaggio)
            End If
        Catch ex As Exception
            RegistraErrore("SincronizzaAgenda", ex)
            _ultimoMessaggio = "Errore durante la sincronizzazione dell'agenda: " & ex.Message
        Finally
            SyncLock _lockEsecuzione
                _agendaInCorso = False
            End SyncLock
        End Try

        Return totale
    End Function

    Private Shared Function EliminaEventiSulServer(posta As CLSMail, agenda As CLSAgenda, chiaveAccount As String) As Integer
        Dim contatore As Integer = 0
        Try
            For Each elemento As CLSAgenda.ElementoSincronizzazioneAgenda In agenda.OttieniEliminazioniAgendaDaSincronizzare(chiaveAccount)
                Try
                    If String.IsNullOrWhiteSpace(elemento.UidServer) AndAlso String.IsNullOrWhiteSpace(elemento.UrlServer) Then
                        agenda.RimuoviEliminazioneAgendaSincronizzata(elemento.Id)
                        Continue For
                    End If
                    If posta.EliminaEventoDalServer(chiaveAccount, elemento.UidServer, elemento.UrlServer) Then
                        agenda.RimuoviEliminazioneAgendaSincronizzata(elemento.Id)
                        contatore += 1
                    End If
                Catch exElemento As Exception
                    RegistraErrore("EliminaEventiSulServer (elemento " & If(elemento.Id, "") & ")", exElemento)
                End Try
            Next
        Catch ex As Exception
            RegistraErrore("EliminaEventiSulServer", ex)
        End Try
        Return contatore
    End Function

    Private Shared Function InviaEventiAlServer(posta As CLSMail, agenda As CLSAgenda, chiaveAccount As String) As Integer
        Dim contatore As Integer = 0
        Try
            For Each elemento As CLSAgenda.ElementoSincronizzazioneAgenda In agenda.OttieniAppuntamentiDaSincronizzare(chiaveAccount)
                Try
                    Dim evento As New EventoDav()
                    evento.Uid = If(elemento.UidServer, "")
                    evento.Href = If(elemento.UrlServer, "")
                    evento.Titolo = If(elemento.Oggetto, "")
                    evento.Luogo = If(elemento.Luogo, "")
                    evento.Descrizione = ComponiDescrizione(elemento)

                    Dim tuttoIlGiorno As Boolean = False
                    evento.Inizio = ComponiDataOra(elemento.DataInizio, elemento.OraInizio, tuttoIlGiorno)
                    If evento.Inizio = DateTime.MinValue Then
                        RegistraErrore("InviaEventiAlServer (elemento " & If(elemento.Id, "") & "): data/ora di inizio non valida (""" &
                                       If(elemento.DataInizio, "") & " " & If(elemento.OraInizio, "") & """), invio saltato.", Nothing)
                        Continue For
                    End If

                    Dim dataFine As String = elemento.DataFine
                    If String.IsNullOrWhiteSpace(dataFine) Then dataFine = elemento.DataInizio
                    Dim tuttoIlGiornoFine As Boolean = False
                    Dim oraFine As String = elemento.OraFine
                    If String.IsNullOrWhiteSpace(oraFine) Then oraFine = elemento.OraInizio
                    evento.Fine = ComponiDataOra(dataFine, oraFine, tuttoIlGiornoFine)
                    If evento.Fine = DateTime.MinValue Then evento.Fine = evento.Inizio

                    evento.TuttoIlGiorno = tuttoIlGiorno
                    If Not evento.TuttoIlGiorno AndAlso evento.Fine <= evento.Inizio Then
                        evento.Fine = evento.Inizio.AddHours(1)
                    End If

                    evento.RegolaRicorrenza = RegolaRicorrenzaDaFrequenza(elemento.Frequenza)

                    Dim urlSalvato As String = posta.InviaEventoAlServer(chiaveAccount, evento)
                    If String.IsNullOrWhiteSpace(urlSalvato) Then
                        RegistraErrore("InviaEventiAlServer (elemento " & If(elemento.Id, "") & "): CLSMail non ha restituito un URL " &
                                       "(vedi sopra nel log il motivo esatto scritto da CLSMail.InviaEventoAlServer), invio saltato.", Nothing)
                        Continue For
                    End If

                    agenda.SegnaAppuntamentoSincronizzato(elemento.Id, evento.Uid, urlSalvato, chiaveAccount)
                    contatore += 1
                Catch exElemento As Exception
                    RegistraErrore("InviaEventiAlServer (elemento " & If(elemento.Id, "") & ")", exElemento)
                End Try
            Next
        Catch ex As Exception
            RegistraErrore("InviaEventiAlServer", ex)
        End Try
        Return contatore
    End Function

    Private Shared Function ScaricaEventiDalServer(posta As CLSMail, agenda As CLSAgenda, chiaveAccount As String) As Integer
        Dim contatore As Integer = 0
        Try
            ' Se l'utente ha scelto esplicitamente uno o piu' calendari da sincronizzare per questo
            ' account (menu Impostazioni Appuntamenti > Sincronizzazione calendari), leggiamo solo
            ' quelli; altrimenti CLSMail mantiene il comportamento di sempre (un solo calendario
            ' predefinito per l'account).
            Dim calendariSelezionati As List(Of String) = agenda.OttieniCalendariSelezionati(chiaveAccount)

            For Each evento As EventoDav In posta.OttieniEventiDalServer(chiaveAccount, calendariSelezionati)
                Try
                    If evento Is Nothing OrElse String.IsNullOrWhiteSpace(evento.Uid) Then Continue For
                    If evento.Inizio = DateTime.MinValue Then Continue For

                    Dim dataInizio As String = evento.Inizio.ToString("dd/MM/yyyy")
                    Dim dataFine As String = evento.Fine.ToString("dd/MM/yyyy")
                    If evento.Fine = DateTime.MinValue Then dataFine = dataInizio

                    Dim oraInizio As String = ""
                    Dim oraFine As String = ""
                    If Not evento.TuttoIlGiorno Then
                        oraInizio = evento.Inizio.ToString("HH:mm")
                        If evento.Fine <> DateTime.MinValue Then oraFine = evento.Fine.ToString("HH:mm")
                    End If

                    If agenda.ImportaAppuntamentoDaSincronizzazione(chiaveAccount, evento.Uid, evento.Href,
                                                                     evento.Titolo, evento.Descrizione, evento.Luogo,
                                                                     dataInizio, dataFine, oraInizio, oraFine,
                                                                     evento.UrlCalendario) Then
                        contatore += 1
                    End If
                Catch exEvento As Exception
                    RegistraErrore("ScaricaEventiDalServer (evento)", exEvento)
                End Try
            Next
        Catch ex As Exception
            RegistraErrore("ScaricaEventiDalServer", ex)
        End Try
        Return contatore
    End Function

    ' Converte l'enum locale CLSAgenda.Frequenza in una RRULE RFC 5545 da mettere nel VEVENT.
    ' Prima di questa correzione questo passaggio non esisteva affatto: la ricorrenza scelta
    ' nell'agenda locale (Giornaliera/Settimanale/ecc.) non veniva mai tradotta e trasmessa al
    ' server, quindi ANCHE quando l'invio andava a buon fine l'evento arrivava a iCloud come
    ' evento singolo, senza nessuna ripetizione, a prescindere da cosa fosse stato impostato
    ' nell'agenda.
    Private Shared Function RegolaRicorrenzaDaFrequenza(freq As CLSAgenda.Frequenza) As String
        Select Case freq
            Case CLSAgenda.Frequenza.Giornaliera
                Return "FREQ=DAILY"
            Case CLSAgenda.Frequenza.Settimanale
                Return "FREQ=WEEKLY"
            Case CLSAgenda.Frequenza.Bisettimanale
                Return "FREQ=WEEKLY;INTERVAL=2"
            Case CLSAgenda.Frequenza.Mensile
                Return "FREQ=MONTHLY"
            Case CLSAgenda.Frequenza.Annuale
                Return "FREQ=YEARLY"
            Case Else
                Return ""
        End Select
    End Function

    Private Shared Function ComponiDescrizione(elemento As CLSAgenda.ElementoSincronizzazioneAgenda) As String
        Try
            Dim testo As String = If(elemento.Note, "")
            If Not String.IsNullOrWhiteSpace(elemento.Nome) Then
                If String.IsNullOrWhiteSpace(testo) Then
                    testo = "Con: " & elemento.Nome
                Else
                    testo = testo & Environment.NewLine & "Con: " & elemento.Nome
                End If
            End If
            Return testo
        Catch ex As Exception
            RegistraErrore("ComponiDescrizione", ex)
            Return ""
        End Try
    End Function

    Private Shared Function ComponiDataOra(data As String, ora As String, ByRef tuttoIlGiorno As Boolean) As DateTime
        tuttoIlGiorno = False
        Try
            If String.IsNullOrWhiteSpace(data) Then Return DateTime.MinValue

            Dim giorno As DateTime
            If Not DateTime.TryParseExact(data.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, giorno) Then
                If Not DateTime.TryParse(data.Trim(), giorno) Then Return DateTime.MinValue
            End If

            If String.IsNullOrWhiteSpace(ora) Then
                tuttoIlGiorno = True
                Return giorno.Date
            End If

            Dim orario As DateTime
            If DateTime.TryParseExact(ora.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, orario) Then
                Return giorno.Date.Add(orario.TimeOfDay)
            End If
            If DateTime.TryParseExact(ora.Trim(), "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, orario) Then
                Return giorno.Date.Add(orario.TimeOfDay)
            End If
            If DateTime.TryParse(ora.Trim(), orario) Then
                Return giorno.Date.Add(orario.TimeOfDay)
            End If

            tuttoIlGiorno = True
            Return giorno.Date
        Catch ex As Exception
            RegistraErrore("ComponiDataOra (" & If(data, "") & " " & If(ora, "") & ")", ex)
            Return DateTime.MinValue
        End Try
    End Function

    Public Shared Function SincronizzaTutto() As Integer
        Dim totale As Integer = 0
        Try
            totale += SincronizzaRubrica()
            totale += SincronizzaAgenda()
        Catch ex As Exception
            RegistraErrore("SincronizzaTutto", ex)
        End Try
        Return totale
    End Function

    Public Shared Sub SincronizzaRubricaInSottofondo()
        Try
            Dim lavoro As New Thread(AddressOf EseguiRubricaInSottofondo)
            lavoro.IsBackground = True
            lavoro.Start()
        Catch ex As Exception
            RegistraErrore("SincronizzaRubricaInSottofondo", ex)
        End Try
    End Sub

    Private Shared Sub EseguiRubricaInSottofondo()
        Try
            SincronizzaRubrica()
        Catch ex As Exception
            RegistraErrore("EseguiRubricaInSottofondo", ex)
        End Try
    End Sub

    Public Shared Sub SincronizzaAgendaInSottofondo()
        Try
            Dim lavoro As New Thread(AddressOf EseguiAgendaInSottofondo)
            lavoro.IsBackground = True
            lavoro.Start()
        Catch ex As Exception
            RegistraErrore("SincronizzaAgendaInSottofondo", ex)
        End Try
    End Sub

    Private Shared Sub EseguiAgendaInSottofondo()
        Try
            SincronizzaAgenda()
        Catch ex As Exception
            RegistraErrore("EseguiAgendaInSottofondo", ex)
        End Try
    End Sub

    Public Shared Sub SincronizzaTuttoInSottofondo()
        Try
            Dim lavoro As New Thread(AddressOf EseguiTuttoInSottofondo)
            lavoro.IsBackground = True
            lavoro.Start()
        Catch ex As Exception
            RegistraErrore("SincronizzaTuttoInSottofondo", ex)
        End Try
    End Sub

    Private Shared Sub EseguiTuttoInSottofondo()
        Try
            SincronizzaTutto()
        Catch ex As Exception
            RegistraErrore("EseguiTuttoInSottofondo", ex)
        End Try
    End Sub

End Class