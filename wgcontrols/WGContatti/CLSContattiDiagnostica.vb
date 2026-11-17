Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Windows.Forms

Partial Public Class CLSContatti

    ' ============================================================================================
    ' RIPARAZIONE AUTOMATICA E DIAGNOSTICA
    ' ============================================================================================
    ' Pensate per quando l'utente non puo' o non vuole dire cosa non funziona: all'avvio la
    ' rubrica sistema da sola le incoerenze piu' comuni, e in caso di errore scrive in un file
    ' tutto cio' che serve per capire il problema (senza contatti, email o password).

    ' Il log si scrive solo in %LocalAppData%\WinGabriel\logs\log.txt, in nessun'altra cartella.
    Private Shared Function CartelleLog() As String()
        Dim elenco As New List(Of String)
        Try
            elenco.Add(WGDb.WGPercorsiApplicazione.CartellaLog())
        Catch
        End Try
        Return elenco.ToArray()
    End Function

    ''' <summary>
    ''' Sistema le incoerenze fra rubriche e contatti: predefinite doppie o mancanti, contatti
    ''' che puntano a una rubrica inesistente, contatti di rubriche locali rimasti legati a un
    ''' account senza essere mai stati sincronizzati. E' ripetibile senza danni. Non tocca mai
    ''' il contenuto dei contatti. Ritorna il resoconto di cio' che ha corretto ("" se nulla).
    ''' </summary>
    Public Function RiparaRubriche() As String
        Dim report As New StringBuilder()
        Try
            If _db Is Nothing Then Return ""
            SyncLock _lockRubriche
                Dim rubriche As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRubriche, "Nome ASC")
                ' Lettura fallita o vuota: non si tocca nulla, altrimenti tutti i contatti
                ' risulterebbero "orfani" e verrebbero spostati.
                If rubriche Is Nothing OrElse rubriche.Rows.Count = 0 Then Return ""

                Dim perAccount As New Dictionary(Of String, List(Of DataRow))(StringComparer.OrdinalIgnoreCase)
                Dim accountDiRubrica As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each r As DataRow In rubriche.Rows
                    Dim acc As String = ValoreColonna(r, "ChiaveAccount").Trim()
                    accountDiRubrica(ValoreColonna(r, "ChiaveWG")) = acc
                    If Not perAccount.ContainsKey(acc) Then perAccount(acc) = New List(Of DataRow)
                    perAccount(acc).Add(r)
                Next

                ' 1) Una e una sola rubrica predefinita per account
                For Each coppia As KeyValuePair(Of String, List(Of DataRow)) In perAccount
                    Dim predefinite As New List(Of DataRow)
                    For Each r As DataRow In coppia.Value
                        If ValoreColonna(r, "Predefinita") = "1" Then predefinite.Add(r)
                    Next
                    predefinite.Sort(Function(a As DataRow, b As DataRow) String.Compare(ValoreColonna(a, "ChiaveWG"), ValoreColonna(b, "ChiaveWG"), StringComparison.Ordinal))

                    If predefinite.Count = 0 Then
                        Dim scelta As DataRow = coppia.Value(0)
                        _db.AggiornaRecord(NomeTabellaRubriche, ValoreColonna(scelta, "ChiaveWG"),
                            New Dictionary(Of String, Object) From {{"Predefinita", 1}})
                        report.AppendLine("Account '" & coppia.Key & "': impostata la rubrica predefinita che mancava.")
                    ElseIf predefinite.Count > 1 Then
                        For i As Integer = 1 To predefinite.Count - 1
                            _db.AggiornaRecord(NomeTabellaRubriche, ValoreColonna(predefinite(i), "ChiaveWG"),
                                New Dictionary(Of String, Object) From {{"Predefinita", 0}})
                        Next
                        report.AppendLine("Account '" & coppia.Key & "': tolte " & (predefinite.Count - 1) & " rubriche predefinite in eccesso.")
                    End If
                Next

                ' 2) Contatti con rubrica inesistente / rubrica locale ma account valorizzato
                Dim contatti As DataTable = _db.EseguiQuery(
                    "SELECT ChiaveWG, ChiaveRubrica, ChiaveAccountOrigine, UidServer FROM " & NomeTabellaContatti & ";", Nothing)
                If contatti IsNot Nothing Then
                    Dim orfani As Integer = 0
                    Dim locali As Integer = 0
                    For Each c As DataRow In contatti.Rows
                        Try
                            Dim chiaveContatto As String = ValoreColonna(c, "ChiaveWG")
                            Dim rub As String = ValoreColonna(c, "ChiaveRubrica").Trim()
                            Dim accContatto As String = ValoreColonna(c, "ChiaveAccountOrigine").Trim()
                            If rub.Length = 0 Then Continue For ' li sistema la migrazione all'avvio

                            If Not accountDiRubrica.ContainsKey(rub) Then
                                Dim destinazione As String = OttieniORCreaRubricaPredefinita(accContatto)
                                If destinazione.Length > 0 AndAlso Not RubricaProtetta(destinazione) Then
                                    _db.AggiornaRecord(NomeTabellaContatti, chiaveContatto,
                                        New Dictionary(Of String, Object) From {{"ChiaveRubrica", destinazione}})
                                    orfani += 1
                                End If
                            ElseIf accountDiRubrica(rub).Length = 0 AndAlso accContatto.Length > 0 AndAlso ValoreColonna(c, "UidServer").Length = 0 Then
                                ' Contatto di una rubrica locale mai arrivato da un server: non deve sincronizzarsi.
                                _db.AggiornaRecord(NomeTabellaContatti, chiaveContatto,
                                    New Dictionary(Of String, Object) From {{"ChiaveAccountOrigine", ""}, {"DaSincronizzare", 0}})
                                locali += 1
                            End If
                        Catch exRiga As Exception
                            ScriviLog("CLSContatti.RiparaRubriche contatto: " & exRiga.ToString())
                        End Try
                    Next
                    If orfani > 0 Then report.AppendLine(orfani & " contatti con rubrica inesistente spostati nella rubrica predefinita.")
                    If locali > 0 Then report.AppendLine(locali & " contatti di rubriche locali staccati dall'account.")
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSContatti.RiparaRubriche: " & ex.ToString())
        End Try
        Return report.ToString()
    End Function

    Private Function ContaRighe(sql As String, Optional parametri As Dictionary(Of String, Object) = Nothing) As String
        Try
            Dim t As DataTable = _db.EseguiQuery(sql, parametri)
            If t Is Nothing OrElse t.Rows.Count = 0 Then Return "?"
            Return t.Rows(0)(0).ToString()
        Catch ex As Exception
            Return "errore (" & ex.Message & ")"
        End Try
    End Function

    ''' <summary>
    ''' Testo di diagnostica da allegare a una segnalazione. Contiene versioni, stato del
    ''' database, elenco delle rubriche con i conteggi e le ultime righe del log. Non contiene
    ''' nomi, email o telefoni dei contatti, ne' password o chiavi.
    ''' </summary>
    Public Function Diagnostica() As String
        Dim sb As New StringBuilder()
        Try
            sb.AppendLine("=== DIAGNOSTICA RUBRICA ===")
            sb.AppendLine("Data: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            Try
                sb.AppendLine("Versione: " & Assembly.GetExecutingAssembly().GetName().Version.ToString())
            Catch
            End Try
            sb.AppendLine("Sistema: " & Environment.OSVersion.ToString() & " | OS 64 bit: " & Environment.Is64BitOperatingSystem & " | processo 64 bit: " & Environment.Is64BitProcess)
            sb.AppendLine(".NET: " & Environment.Version.ToString())
            Try
                sb.AppendLine("Cartella programma: " & Application.StartupPath)
            Catch
            End Try
            sb.AppendLine("Ultimo errore: " & UltimoErrore)
            sb.AppendLine("Account predefinito sincronizzazione: " & If(String.IsNullOrWhiteSpace(_accountPredefinito), "(nessuno)", _accountPredefinito))

            If _db Is Nothing Then
                sb.AppendLine("!! DATABASE NON APERTO (vedi log: CLSContatti.New)")
            Else
                sb.AppendLine()
                sb.AppendLine("--- Database ---")
                sb.AppendLine("Controllo integrita': " & ContaRighe("PRAGMA quick_check;"))

                Dim attese As String() = {
                    NomeTabellaRubriche & ".ChiaveAccount", NomeTabellaRubriche & ".Nome", NomeTabellaRubriche & ".Predefinita",
                    NomeTabellaRubriche & ".Protetta", NomeTabellaRubriche & ".SalePassword", NomeTabellaRubriche & ".ImprontaPassword",
                    NomeTabellaRubriche & ".ChiavePubblica", NomeTabellaRubriche & ".ChiavePrivataCifrata",
                    NomeTabellaContatti & ".ChiaveRubrica", NomeTabellaContatti & ".ChiaveAccountOrigine", NomeTabellaContatti & ".UidServer",
                    NomeTabellaContatti & ".UrlServer", NomeTabellaContatti & ".DaSincronizzare", NomeTabellaContatti & ".IdSync"}
                Dim mancanti As New List(Of String)
                For Each voce As String In attese
                    Dim parti As String() = voce.Split("."c)
                    If Not ColonnaPresente(parti(0), parti(1)) Then mancanti.Add(voce)
                Next
                sb.AppendLine("Colonne mancanti: " & If(mancanti.Count = 0, "nessuna", String.Join(", ", mancanti.ToArray())))

                sb.AppendLine()
                sb.AppendLine("--- Rubriche ---")
                Dim rubriche As DataTable = _db.OttieniTuttiIRecord(NomeTabellaRubriche, "Nome ASC")
                Dim predefinitePerAccount As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                If rubriche Is Nothing Then
                    sb.AppendLine("Impossibile leggere le rubriche.")
                Else
                    sb.AppendLine("Numero rubriche: " & rubriche.Rows.Count)
                    For Each r As DataRow In rubriche.Rows
                        Dim chiave As String = ValoreColonna(r, "ChiaveWG")
                        Dim acc As String = ValoreColonna(r, "ChiaveAccount").Trim()
                        If Not predefinitePerAccount.ContainsKey(acc) Then predefinitePerAccount(acc) = 0
                        If ValoreColonna(r, "Predefinita") = "1" Then predefinitePerAccount(acc) += 1
                        Dim eProtetta As Boolean = ValoreColonna(r, "ImprontaPassword").Length > 0
                        sb.AppendLine("- [" & chiave & "] '" & ValoreColonna(r, "Nome") & "'" &
                            " | account: " & If(acc.Length = 0, "LOCALE", acc) &
                            " | predefinita: " & ValoreColonna(r, "Predefinita") &
                            " | protetta: " & eProtetta &
                            If(eProtetta, " | chiavi RSA: " & (ValoreColonna(r, "ChiavePubblica").Length > 0 AndAlso ValoreColonna(r, "ChiavePrivataCifrata").Length > 0) &
                                " | sbloccata: " & RubricaSbloccata(chiave), "") &
                            " | contatti: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica = @r;",
                                New Dictionary(Of String, Object) From {{"@r", chiave}}))
                    Next
                    For Each coppia As KeyValuePair(Of String, Integer) In predefinitePerAccount
                        If coppia.Value <> 1 Then
                            sb.AppendLine("!! Account '" & If(coppia.Key.Length = 0, "LOCALE", coppia.Key) & "' ha " & coppia.Value & " rubriche predefinite (dovrebbe averne 1)")
                        End If
                    Next
                End If

                sb.AppendLine()
                sb.AppendLine("--- Contatti (solo numeri) ---")
                sb.AppendLine("Totale: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaContatti & ";"))
                sb.AppendLine("Senza rubrica: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica IS NULL OR ChiaveRubrica = '';"))
                sb.AppendLine("Con rubrica inesistente: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaContatti &
                    " WHERE ChiaveRubrica IS NOT NULL AND ChiaveRubrica <> '' AND ChiaveRubrica NOT IN (SELECT ChiaveWG FROM " & NomeTabellaRubriche & ");"))
                sb.AppendLine("Da sincronizzare: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaContatti & " WHERE DaSincronizzare = 1;"))
                sb.AppendLine("Eliminazioni in attesa di sincronizzazione: " & ContaRighe("SELECT COUNT(*) FROM " & NomeTabellaEliminati & ";"))
            End If

            sb.AppendLine()
            sb.AppendLine("--- Cifratura (serve alle rubriche con password) ---")
            Try
                Using rsa As New RSACng(2048)
                    Dim prova As Byte() = New Byte() {1, 2, 3, 4, 5, 6, 7, 8}
                    Dim cifrato As Byte() = rsa.Encrypt(prova, RSAEncryptionPadding.OaepSHA256)
                    Dim chiaro As Byte() = rsa.Decrypt(cifrato, RSAEncryptionPadding.OaepSHA256)
                    sb.AppendLine("RSACng + OAEP-SHA256: " & If(chiaro.Length = prova.Length, "OK", "RISULTATO ANOMALO"))
                End Using
            Catch ex As Exception
                sb.AppendLine("RSACng + OAEP-SHA256: ERRORE " & ex.GetType().Name & ": " & ex.Message)
            End Try

            sb.AppendLine()
            sb.AppendLine("--- Ultime righe del log ---")
            Dim lette As Boolean = False
            For Each cartella As String In CartelleLog()
                Try
                    Dim percorso As String = Path.Combine(cartella, "log.txt")
                    If Not File.Exists(percorso) Then Continue For
                    Dim coda As New Queue(Of String)
                    For Each riga As String In File.ReadLines(percorso)
                        coda.Enqueue(riga)
                        If coda.Count > 40 Then coda.Dequeue()
                    Next
                    sb.AppendLine("(" & percorso & ")")
                    For Each riga As String In coda
                        sb.AppendLine(riga)
                    Next
                    lette = True
                    Exit For
                Catch
                End Try
            Next
            If Not lette Then sb.AppendLine("Nessun file di log leggibile.")
        Catch ex As Exception
            sb.AppendLine("Errore durante la diagnostica: " & ex.ToString())
        End Try
        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Salva la diagnostica in diagnostica.txt accanto al log e ne ritorna il percorso
    ''' ("" se non e' stato possibile scrivere in nessuna cartella).
    ''' </summary>
    Public Function SalvaDiagnostica() As String
        Dim testo As String = Diagnostica()
        For Each cartella As String In CartelleLog()
            Try
                If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                Dim percorso As String = Path.Combine(cartella, "diagnostica.txt")
                File.WriteAllText(percorso, testo, New UTF8Encoding(False))
                Return percorso
            Catch
            End Try
        Next
        Return ""
    End Function

End Class