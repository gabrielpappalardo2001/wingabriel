Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports WGDb

' Gestione avanzata di firme e regole locali per i messaggi. Include i metodi per il salvataggio sul database e l'applicazione dei
' filtri alle caselle.
Public Class FirmaMail
    Public Property Chiave As String = ""
    Public Property Nome As String = ""
    Public Property Testo As String = ""
    Public Property Predefinita As Boolean = False
End Class

Public Class RegolaMail
    Public Property Chiave As String = ""
    Public Property Nome As String = ""
    Public Property Attiva As Boolean = True
    Public Property Ordine As Integer = 0
    Public Property Campo As String = "Mittente"
    Public Property Operatore As String = "Contiene"
    Public Property Valore As String = ""
    Public Property Azione As String = "SegnaLetto"
    Public Property Parametro As String = ""
    Public Property FermaSeApplicata As Boolean = True
    ' Casella a cui si applica la regola. Vuota per compatibilita' con le regole create
    ' prima di questa proprieta': in quel caso la regola vale per tutte le caselle.
    Public Property ChiaveAccount As String = ""
End Class

Public Class PostaAvanzata
    Implements IDisposable

    Private Const NomeTabellaFirme As String = "firme_mail"
    Private Const NomeTabellaRegole As String = "regole_mail"
    Private Const NomeTabellaMessaggi As String = "messaggi"
    Private Const NomeTabellaAllegati As String = "allegati"

    Private ReadOnly _db As WGDatabase
    Private ReadOnly _cartellaAllegati As String
    Private _smaltito As Boolean = False

    Public Shared ReadOnly CampiDisponibili As String() = New String() {"Mittente", "Oggetto", "Corpo", "Destinatario"}
    Public Shared ReadOnly OperatoriDisponibili As String() = New String() {"Contiene", "Uguale", "IniziaCon", "FinisceCon", "Regex"}
    Public Shared ReadOnly AzioniDisponibili As String() = New String() {"SegnaLetto", "SegnaNonLetto", "SpostaCartella", "SegnaIndesiderata", "Elimina"}

    Public Sub New()
        Try
            _db = New WGDatabase("mail", "wg-mail.sqlite")
            _cartellaAllegati = WGDatabase.CartellaDati(Path.Combine("mail", "allegati"))
            CreaTabelle()
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.New: " & ex.ToString())
        End Try
    End Sub

    Private Sub CreaTabelle()
        Try
            _db.CreaTabellaSeNonEsiste(NomeTabellaFirme,
                "Nome TEXT, Testo TEXT, Predefinita INTEGER DEFAULT 0")
            _db.CreaTabellaSeNonEsiste(NomeTabellaRegole,
                "Nome TEXT, Attiva INTEGER DEFAULT 1, Ordine INTEGER DEFAULT 0, " &
                "Campo TEXT, Operatore TEXT, Valore TEXT, Azione TEXT, Parametro TEXT, FermaSeApplicata INTEGER DEFAULT 1, " &
                "ChiaveAccount TEXT")
            AggiungiColonnaSeManca(NomeTabellaRegole, "ChiaveAccount", "TEXT")
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.CreaTabelle: " & ex.ToString())
        End Try
    End Sub

    ' Stesso schema di migrazione leggera usato in CLSMail: se la colonna manca gia' (database
    ' creato prima di questa proprieta') la si aggiunge senza toccare i dati esistenti.
    Private Function ColonnaPresente(nomeTabella As String, nomeColonna As String) As Boolean
        Try
            If _db Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            Dim info As DataTable = _db.EseguiQuery("PRAGMA table_info(" & nomeTabella & ");", Nothing)
            If info Is Nothing Then Return False
            For Each riga As DataRow In info.Rows
                If String.Equals(riga("name").ToString(), nomeColonna, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
        Catch
        End Try
        Return False
    End Function

    Private Sub AggiungiColonnaSeManca(nomeTabella As String, nomeColonna As String, definizioneSql As String)
        Try
            If ColonnaPresente(nomeTabella, nomeColonna) Then Return
            _db.EseguiNonQuery("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " " & definizioneSql & ";", Nothing)
        Catch
        End Try
    End Sub

    Public Function OttieniFirme() As List(Of FirmaMail)
        Dim risultato As New List(Of FirmaMail)
        Try
            Dim tabella As DataTable = _db.OttieniTuttiIRecord(NomeTabellaFirme, "Nome ASC")
            If tabella Is Nothing Then Return risultato
            For Each riga As DataRow In tabella.Rows
                Try
                    risultato.Add(RigaVersoFirma(riga))
                Catch exRiga As Exception
                    CLSMail.ScriviLog("PostaAvanzata.OttieniFirme riga: " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.OttieniFirme: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniFirma(chiave As String) As FirmaMail
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaFirme, chiave)
            If riga Is Nothing Then Return Nothing
            Return RigaVersoFirma(riga)
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.OttieniFirma (" & chiave & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniFirmaPredefinita() As FirmaMail
        Try
            Dim tabella As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaFirme & " WHERE Predefinita=1 LIMIT 1;", Nothing)
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return Nothing
            Return RigaVersoFirma(tabella.Rows(0))
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.OttieniFirmaPredefinita: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function SalvaFirma(firma As FirmaMail) As String
        Try
            If firma Is Nothing Then Return ""
            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", If(firma.Nome, "")},
                {"Testo", If(firma.Testo, "")},
                {"Predefinita", If(firma.Predefinita, 1, 0)}
            }
            If String.IsNullOrWhiteSpace(firma.Chiave) Then
                Dim nuova As String = _db.InserisciRecord(NomeTabellaFirme, valori)
                If firma.Predefinita AndAlso Not String.IsNullOrWhiteSpace(nuova) Then ImpostaFirmaPredefinita(nuova)
                Return nuova
            Else
                _db.AggiornaRecord(NomeTabellaFirme, firma.Chiave, valori)
                If firma.Predefinita Then ImpostaFirmaPredefinita(firma.Chiave)
                Return firma.Chiave
            End If
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.SalvaFirma: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function EliminaFirma(chiave As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return False
            Return _db.EliminaRecord(NomeTabellaFirme, chiave)
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.EliminaFirma (" & chiave & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function ImpostaFirmaPredefinita(chiave As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return False
            _db.EseguiNonQuery("UPDATE " & NomeTabellaFirme & " SET Predefinita=0;", Nothing)
            _db.EseguiNonQuery("UPDATE " & NomeTabellaFirme & " SET Predefinita=1 WHERE ChiaveWG=@c;",
                New Dictionary(Of String, Object) From {{"@c", chiave}})
            Return True
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.ImpostaFirmaPredefinita (" & chiave & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function ApplicaFirma(corpo As String, Optional chiaveFirma As String = "") As String
        Try
            Dim firma As FirmaMail
            If String.IsNullOrWhiteSpace(chiaveFirma) Then
                firma = OttieniFirmaPredefinita()
            Else
                firma = OttieniFirma(chiaveFirma)
            End If
            If firma Is Nothing OrElse String.IsNullOrWhiteSpace(firma.Testo) Then Return If(corpo, "")
            Dim testoCorpo As String = If(corpo, "")
            Return testoCorpo & vbCrLf & vbCrLf & "-- " & vbCrLf & firma.Testo
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.ApplicaFirma: " & ex.ToString())
            Return If(corpo, "")
        End Try
    End Function

    Private Function RigaVersoFirma(riga As DataRow) As FirmaMail
        Dim firma As New FirmaMail()
        Try
            firma.Chiave = riga("ChiaveWG").ToString()
            firma.Nome = riga("Nome").ToString()
            firma.Testo = riga("Testo").ToString()
            firma.Predefinita = Convert.ToInt32(riga("Predefinita")) = 1
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.RigaVersoFirma: " & ex.ToString())
        End Try
        Return firma
    End Function

    Public Function EstraiLink(corpoTesto As String, corpoHtml As String) As List(Of String)
        Dim risultato As New List(Of String)
        Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim aggiungi As Action(Of String) =
                Sub(u As String)
                    Try
                        Dim pulito As String = PulisciLink(u)
                        If String.IsNullOrWhiteSpace(pulito) Then Return
                        If visti.Contains(pulito) Then Return
                        visti.Add(pulito)
                        risultato.Add(pulito)
                    Catch
                    End Try
                End Sub

            If Not String.IsNullOrWhiteSpace(corpoHtml) Then
                Try
                    For Each corr As Match In Regex.Matches(corpoHtml, "href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)
                        If corr.Groups.Count > 1 Then aggiungi(corr.Groups(1).Value)
                    Next
                Catch exHtml As Exception
                    CLSMail.ScriviLog("PostaAvanzata.EstraiLink html: " & exHtml.ToString())
                End Try
            End If

            Dim baseTesto As String = If(String.IsNullOrWhiteSpace(corpoTesto), "", corpoTesto)
            Try
                For Each corr As Match In Regex.Matches(baseTesto, "https?://[^\s<>""')\]]+", RegexOptions.IgnoreCase)
                    aggiungi(corr.Value)
                Next
                For Each corr As Match In Regex.Matches(baseTesto, "(^|\s)(www\.[^\s<>""')\]]+)", RegexOptions.IgnoreCase)
                    If corr.Groups.Count > 2 Then aggiungi(corr.Groups(2).Value)
                Next
            Catch exTesto As Exception
                CLSMail.ScriviLog("PostaAvanzata.EstraiLink testo: " & exTesto.ToString())
            End Try
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.EstraiLink: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function TestoConLink(testo As String, corpoTesto As String, corpoHtml As String) As String
        Try
            Dim link As List(Of String) = EstraiLink(corpoTesto, corpoHtml)
            If link Is Nothing OrElse link.Count = 0 Then Return If(testo, "")
            Dim sb As New StringBuilder()
            sb.Append(If(testo, ""))
            sb.Append(vbCrLf & vbCrLf & "Link nel messaggio:" & vbCrLf)
            Dim indice As Integer = 1
            For Each u As String In link
                sb.Append(indice.ToString() & ". " & u & vbCrLf)
                indice += 1
            Next
            Return sb.ToString()
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.TestoConLink: " & ex.ToString())
            Return If(testo, "")
        End Try
    End Function

    Private Function PulisciLink(u As String) As String
        Try
            If String.IsNullOrWhiteSpace(u) Then Return ""
            Dim v As String = u.Trim()
            While v.Length > 0 AndAlso ".,;:!?)".IndexOf(v(v.Length - 1)) >= 0
                v = v.Substring(0, v.Length - 1)
            End While
            If v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) Then Return ""
            If v.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) Then Return ""
            If v.StartsWith("#") Then Return ""
            Return v
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.PulisciLink: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function OttieniRegole(Optional soloAttive As Boolean = False) As List(Of RegolaMail)
        Dim risultato As New List(Of RegolaMail)
        Try
            Dim tabella As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRegole, "Ordine ASC, Nome ASC")
            If tabella Is Nothing Then Return risultato
            For Each riga As DataRow In tabella.Rows
                Try
                    Dim regola As RegolaMail = RigaVersoRegola(riga)
                    If Not soloAttive OrElse regola.Attiva Then risultato.Add(regola)
                Catch exRiga As Exception
                    CLSMail.ScriviLog("PostaAvanzata.OttieniRegole riga: " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.OttieniRegole: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniRegola(chiave As String) As RegolaMail
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(NomeTabellaRegole, chiave)
            If riga Is Nothing Then Return Nothing
            Return RigaVersoRegola(riga)
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.OttieniRegola (" & chiave & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function SalvaRegola(regola As RegolaMail) As String
        Try
            If regola Is Nothing Then Return ""
            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", If(regola.Nome, "")},
                {"Attiva", If(regola.Attiva, 1, 0)},
                {"Ordine", regola.Ordine},
                {"Campo", If(regola.Campo, "Mittente")},
                {"Operatore", If(regola.Operatore, "Contiene")},
                {"Valore", If(regola.Valore, "")},
                {"Azione", If(regola.Azione, "SegnaLetto")},
                {"Parametro", If(regola.Parametro, "")},
                {"FermaSeApplicata", If(regola.FermaSeApplicata, 1, 0)},
                {"ChiaveAccount", If(regola.ChiaveAccount, "")}
            }
            If String.IsNullOrWhiteSpace(regola.Chiave) Then
                Return _db.InserisciRecord(NomeTabellaRegole, valori)
            Else
                _db.AggiornaRecord(NomeTabellaRegole, regola.Chiave, valori)
                Return regola.Chiave
            End If
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.SalvaRegola: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function EliminaRegola(chiave As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return False
            Return _db.EliminaRecord(NomeTabellaRegole, chiave)
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.EliminaRegola (" & chiave & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function AttivaDisattivaRegola(chiave As String) As Boolean
        Try
            Dim regola As RegolaMail = OttieniRegola(chiave)
            If regola Is Nothing Then Return False
            regola.Attiva = Not regola.Attiva
            SalvaRegola(regola)
            Return regola.Attiva
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.AttivaDisattivaRegola (" & chiave & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function RigaVersoRegola(riga As DataRow) As RegolaMail
        Dim regola As New RegolaMail()
        Try
            regola.Chiave = riga("ChiaveWG").ToString()
            regola.Nome = riga("Nome").ToString()
            regola.Attiva = Convert.ToInt32(riga("Attiva")) = 1
            regola.Ordine = Convert.ToInt32(riga("Ordine"))
            regola.Campo = riga("Campo").ToString()
            regola.Operatore = riga("Operatore").ToString()
            regola.Valore = riga("Valore").ToString()
            regola.Azione = riga("Azione").ToString()
            regola.Parametro = riga("Parametro").ToString()
            regola.FermaSeApplicata = Convert.ToInt32(riga("FermaSeApplicata")) = 1
            If riga.Table.Columns.Contains("ChiaveAccount") AndAlso Not IsDBNull(riga("ChiaveAccount")) Then
                regola.ChiaveAccount = riga("ChiaveAccount").ToString()
            End If
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.RigaVersoRegola: " & ex.ToString())
        End Try
        Return regola
    End Function

    Public Function RegolaCorrisponde(regola As RegolaMail, mittente As String, oggetto As String, corpo As String, destinatari As String) As Boolean
        Try
            If regola Is Nothing Then Return False
            Dim testoCampo As String
            Select Case If(regola.Campo, "").Trim().ToUpperInvariant()
                Case "OGGETTO"
                    testoCampo = If(oggetto, "")
                Case "CORPO"
                    testoCampo = If(corpo, "")
                Case "DESTINATARIO"
                    testoCampo = If(destinatari, "")
                Case Else
                    testoCampo = If(mittente, "")
            End Select

            Dim valore As String = If(regola.Valore, "")
            If String.IsNullOrEmpty(valore) Then Return False

            Select Case If(regola.Operatore, "").Trim().ToUpperInvariant()
                Case "UGUALE"
                    Return String.Equals(testoCampo.Trim(), valore.Trim(), StringComparison.OrdinalIgnoreCase)
                Case "INIZIACON"
                    Return testoCampo.TrimStart().StartsWith(valore, StringComparison.OrdinalIgnoreCase)
                Case "FINISCECON"
                    Return testoCampo.TrimEnd().EndsWith(valore, StringComparison.OrdinalIgnoreCase)
                Case "REGEX"
                    Try
                        Return Regex.IsMatch(testoCampo, valore, RegexOptions.IgnoreCase)
                    Catch exRegex As Exception
                        CLSMail.ScriviLog("PostaAvanzata.RegolaCorrisponde regex (" & valore & "): " & exRegex.ToString())
                        Return False
                    End Try
                Case Else
                    Return testoCampo.IndexOf(valore, StringComparison.OrdinalIgnoreCase) >= 0
            End Select
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.RegolaCorrisponde: " & ex.ToString())
            Return False
        End Try
    End Function

    ' chiaveAccount identifica la casella del messaggio appena arrivato: si applicano solo le
    ' regole di quella casella, piu' le regole senza casella assegnata (create prima che questa
    ' proprieta' esistesse), che restano valide per tutti gli account per non spezzare
    ' l'automazione gia' impostata da chi le aveva create.
    Public Function ApplicaRegoleAlMessaggioLocale(chiaveMessaggio As String, chiaveAccount As String, mittente As String, oggetto As String, corpo As String, destinatari As String) As Boolean
        Dim qualcosaApplicato As Boolean = False
        Try
            If String.IsNullOrWhiteSpace(chiaveMessaggio) Then Return False
            Dim regole As List(Of RegolaMail) = OttieniRegole(True)
            If regole Is Nothing OrElse regole.Count = 0 Then Return False

            For Each regola As RegolaMail In regole
                Try
                    If Not String.IsNullOrWhiteSpace(regola.ChiaveAccount) AndAlso
                       Not String.Equals(regola.ChiaveAccount, chiaveAccount, StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not RegolaCorrisponde(regola, mittente, oggetto, corpo, destinatari) Then Continue For
                    Dim eseguita As Boolean = EseguiAzioneLocale(regola, chiaveMessaggio)
                    If eseguita Then qualcosaApplicato = True
                    If eseguita AndAlso regola.FermaSeApplicata Then Exit For
                    If regola.Azione IsNot Nothing AndAlso regola.Azione.Trim().ToUpperInvariant() = "ELIMINA" AndAlso eseguita Then Exit For
                Catch exRegola As Exception
                    CLSMail.ScriviLog("PostaAvanzata.ApplicaRegoleAlMessaggioLocale regola (" & regola.Nome & "): " & exRegola.ToString())
                End Try
            Next
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.ApplicaRegoleAlMessaggioLocale (" & chiaveMessaggio & "): " & ex.ToString())
        End Try
        Return qualcosaApplicato
    End Function

    Private Function EseguiAzioneLocale(regola As RegolaMail, chiaveMessaggio As String) As Boolean
        Try
            Select Case If(regola.Azione, "").Trim().ToUpperInvariant()
                Case "SEGNALETTO"
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaMessaggi & " SET Letto=1 WHERE ChiaveWG=@c;",
                        New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
                    Return True
                Case "SEGNANONLETTO"
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaMessaggi & " SET Letto=0 WHERE ChiaveWG=@c;",
                        New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
                    Return True
                Case "SPOSTACARTELLA"
                    Dim cartella As String = If(regola.Parametro, "").Trim()
                    If String.IsNullOrWhiteSpace(cartella) Then Return False
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaMessaggi & " SET Cartella=@f WHERE ChiaveWG=@c;",
                        New Dictionary(Of String, Object) From {{"@f", cartella}, {"@c", chiaveMessaggio}})
                    Return True
                Case "SEGNAINDESIDERATA"
                    _db.EseguiNonQuery("UPDATE " & NomeTabellaMessaggi & " SET Cartella=@f WHERE ChiaveWG=@c;",
                        New Dictionary(Of String, Object) From {{"@f", "Posta indesiderata"}, {"@c", chiaveMessaggio}})
                    Return True
                Case "ELIMINA"
                    EliminaMessaggioLocale(chiaveMessaggio)
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.EseguiAzioneLocale (" & chiaveMessaggio & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub EliminaMessaggioLocale(chiaveMessaggio As String)
        Try
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaAllegati & " WHERE ChiaveMessaggio=@c;",
                New Dictionary(Of String, Object) From {{"@c", chiaveMessaggio}})
            Try
                Dim cartella As String = Path.Combine(_cartellaAllegati, chiaveMessaggio)
                If Directory.Exists(cartella) Then Directory.Delete(cartella, True)
            Catch exCartella As Exception
                CLSMail.ScriviLog("PostaAvanzata.EliminaMessaggioLocale allegati (" & chiaveMessaggio & "): " & exCartella.ToString())
            End Try
            _db.EliminaRecord(NomeTabellaMessaggi, chiaveMessaggio)
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.EliminaMessaggioLocale (" & chiaveMessaggio & "): " & ex.ToString())
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _smaltito Then Return
            _smaltito = True
            If _db IsNot Nothing Then _db.Dispose()
        Catch ex As Exception
            CLSMail.ScriviLog("PostaAvanzata.Dispose: " & ex.ToString())
        End Try
    End Sub

End Class