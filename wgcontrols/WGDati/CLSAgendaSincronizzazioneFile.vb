Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports wgcontrols
Imports WGDb

' Sincronizzazione su file degli appuntamenti dell'agenda e gestione dei doppioni dal server.
Partial Public Class CLSAgenda

    Private Const NomeTabellaAgendaSyncEliminati As String = "AppuntamentiSyncEliminati"
    Private Const GiorniConservazioneEliminatiAgendaSync As Integer = 180
    Private Const EspressioneOraAgendaSync As String = "strftime('%Y-%m-%dT%H:%M:%fZ','now')"
    Private Const PrefissoIdServerAgenda As String = "srv:"
    Private Const ChiaveConfigIdServerAgendaMigrati As String = "AgendaSyncIdServerMigrati"
    Private Const ColonneAgendaSync As String = "Id, IdSync, ModificaSync, ImprontaSync, ChiaveAgenda, UidServer, UidServerFile, ChiaveAccountOrigine, Oggetto, Nome, Note, Luogo, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Preavviso"

    Private Sub PreparaSincronizzazioneFile()
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    AggiungiColonnaSeManca(cmd, "Appuntamenti", "IdSync", "TEXT")
                    AggiungiColonnaSeManca(cmd, "Appuntamenti", "ModificaSync", "TEXT")
                    AggiungiColonnaSeManca(cmd, "Appuntamenti", "ImprontaSync", "TEXT")
                    AggiungiColonnaSeManca(cmd, "Appuntamenti", "UidServerFile", "TEXT")

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS " & NomeTabellaAgendaSyncEliminati & " (IdSync TEXT PRIMARY KEY, Data TEXT NOT NULL, ChiaveAgenda TEXT);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "DROP TRIGGER IF EXISTS trg_appuntamenti_sync_upd;"
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            MigraIdentificativiServerAgenda()

            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds

                    cmd.CommandText = "UPDATE Appuntamenti SET IdSync = lower(hex(randomblob(16))) WHERE IdSync IS NULL OR IdSync = '';"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "UPDATE Appuntamenti SET ModificaSync = " & EspressioneOraAgendaSync & " WHERE ModificaSync IS NULL OR ModificaSync = '';"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_idsync ON Appuntamenti(IdSync);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText =
                        "CREATE TRIGGER IF NOT EXISTS trg_appuntamenti_sync_ins AFTER INSERT ON Appuntamenti " &
                        "WHEN NEW.IdSync IS NULL OR NEW.IdSync = '' OR NEW.ModificaSync IS NULL OR NEW.ModificaSync = '' BEGIN " &
                        "UPDATE Appuntamenti SET " &
                        "IdSync = CASE WHEN NEW.IdSync IS NULL OR NEW.IdSync = '' THEN lower(hex(randomblob(16))) ELSE NEW.IdSync END, " &
                        "ModificaSync = CASE WHEN NEW.ModificaSync IS NULL OR NEW.ModificaSync = '' THEN " & EspressioneOraAgendaSync & " ELSE NEW.ModificaSync END " &
                        "WHERE Id = NEW.Id; END;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText =
                        "CREATE TRIGGER IF NOT EXISTS trg_appuntamenti_sync_del AFTER DELETE ON Appuntamenti " &
                        "WHEN OLD.IdSync IS NOT NULL AND OLD.IdSync <> '' BEGIN " &
                        "INSERT OR REPLACE INTO " & NomeTabellaAgendaSyncEliminati & " (IdSync, Data, ChiaveAgenda) VALUES (OLD.IdSync, " & EspressioneOraAgendaSync & ", OLD.ChiaveAgenda); END;"
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.PreparaSincronizzazioneFile - " & ex.ToString())
        End Try
    End Sub

    Private Sub MigraIdentificativiServerAgenda()
        Try
            Dim configurazione As New WGConfig()
            If configurazione.LeggiValore(ChiaveConfigIdServerAgendaMigrati) = "1" Then Return
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(
                    "UPDATE Appuntamenti SET IdSync = '" & PrefissoIdServerAgenda & "' || UidServer " &
                    "WHERE UidServer IS NOT NULL AND UidServer <> '' AND ChiaveAccountOrigine IS NOT NULL AND ChiaveAccountOrigine <> '';", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            configurazione.SalvaValore(ChiaveConfigIdServerAgendaMigrati, "1")
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.MigraIdentificativiServerAgenda - " & ex.ToString())
        End Try
    End Sub

    Friend Shared Function IdSyncDaUidServerAgenda(uidServer As String) As String
        Try
            Return PrefissoIdServerAgenda & If(uidServer, "").Trim()
        Catch
            Return ""
        End Try
    End Function

    Public Function OttieniAgendeSincronizzabili() As List(Of DescrizioneAgenda)
        Dim risultato As New List(Of DescrizioneAgenda)
        Try
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If voce IsNot Nothing Then risultato.Add(voce)
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAgendeSincronizzabili - " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private _ultimoErroreSincronizzazioneFile As String = ""

    Public ReadOnly Property UltimoErroreSincronizzazioneFile As String
        Get
            Return If(_ultimoErroreSincronizzazioneFile, "")
        End Get
    End Property

    Private Function LeggiAppuntamentiSync(condizione As String, parametri As Dictionary(Of String, Object)) As DataTable
        Dim tabella As New DataTable()
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT " & ColonneAgendaSync & " FROM Appuntamenti WHERE " & condizione & ";", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    If parametri IsNot Nothing Then
                        For Each coppia As KeyValuePair(Of String, Object) In parametri
                            cmd.Parameters.AddWithValue(coppia.Key, coppia.Value)
                        Next
                    End If
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        tabella.Load(reader)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.LeggiAppuntamentiSync - " & ex.ToString())
        End Try
        Return tabella
    End Function

    Private Sub EseguiComandoAgendaSync(sql As String, parametri As Dictionary(Of String, Object))
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(sql, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    If parametri IsNot Nothing Then
                        For Each coppia As KeyValuePair(Of String, Object) In parametri
                            cmd.Parameters.AddWithValue(coppia.Key, coppia.Value)
                        Next
                    End If
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EseguiComandoAgendaSync - " & ex.ToString())
        End Try
    End Sub

    Friend Function TrovaDoppioneAppuntamentoDaServer(chiaveAccount As String, uidServer As String, oggetto As String, dataInizio As String, oraInizio As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) OrElse String.IsNullOrWhiteSpace(uidServer) Then Return ""
            Dim candidati As DataTable = LeggiAppuntamentiSync(
                "(UidServer IS NULL OR UidServer = '') AND ChiaveAgenda IN (SELECT Chiave FROM Agende WHERE ChiaveAccount = @Account)",
                New Dictionary(Of String, Object) From {{"@Account", chiaveAccount}})
            If candidati.Rows.Count = 0 Then Return ""

            Dim trovato As DataRow = Nothing
            Dim idDalServer As String = IdSyncDaUidServerAgenda(uidServer)
            For Each riga As DataRow In candidati.Rows
                If String.Equals(TestoRigaAgendaSync(riga, "UidServerFile"), uidServer.Trim(), StringComparison.Ordinal) OrElse
                   String.Equals(TestoRigaAgendaSync(riga, "IdSync"), idDalServer, StringComparison.Ordinal) Then
                    trovato = riga
                    Exit For
                End If
            Next

            If trovato Is Nothing Then
                Dim chiaveCercata As String = ChiaveOggettoAgendaSync(oggetto)
                Dim oraCercata As String = OraAgendaSync(oraInizio)
                For Each riga As DataRow In candidati.Rows
                    Try
                        If Not String.Equals(TestoRigaAgendaSync(riga, "DataInizio"), If(dataInizio, ""), StringComparison.Ordinal) Then Continue For
                        If Not String.Equals(OraAgendaSync(TestoRigaAgendaSync(riga, "OraInizio")), oraCercata, StringComparison.Ordinal) Then Continue For
                        Dim agenda As String = TestoRigaAgendaSync(riga, "ChiaveAgenda")
                        If Not AgendaPuoInviareTesto(agenda) Then Continue For
                        Dim oggettoLocale As String = TestoLeggibileAgenda(TestoRigaAgendaSync(riga, "Oggetto"), agenda)
                        If chiaveCercata.Length > 0 AndAlso String.Equals(ChiaveOggettoAgendaSync(oggettoLocale), chiaveCercata, StringComparison.Ordinal) Then
                            trovato = riga
                            Exit For
                        End If
                    Catch exRiga As Exception
                        log.ScriviLog("CLSAgenda.TrovaDoppioneAppuntamentoDaServer riga - " & exRiga.ToString())
                    End Try
                Next
            End If
            If trovato Is Nothing Then Return ""

            Dim identificativo As String = TestoRigaAgendaSync(trovato, "Id")
            EseguiComandoAgendaSync("UPDATE Appuntamenti SET UidServer = @Uid, ChiaveAccountOrigine = @Account WHERE Id = @Id;",
                New Dictionary(Of String, Object) From {{"@Uid", uidServer.Trim()}, {"@Account", chiaveAccount}, {"@Id", Convert.ToInt64(identificativo, CultureInfo.InvariantCulture)}})
            Return identificativo
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TrovaDoppioneAppuntamentoDaServer - " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function UnisciSincronizzazioneFile(chiaveAgenda As String, vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        Try
            _ultimoErroreSincronizzazioneFile = ""
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return Nothing
            chiaveAgenda = chiaveAgenda.Trim()

            Dim agendaTrovata As DescrizioneAgenda = Nothing
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If voce IsNot Nothing AndAlso String.Equals(voce.Chiave, chiaveAgenda, StringComparison.Ordinal) Then
                    agendaTrovata = voce
                    Exit For
                End If
            Next
            If agendaTrovata Is Nothing Then
                _ultimoErroreSincronizzazioneFile = "L'agenda scelta per la sincronizzazione non esiste più."
                Return Nothing
            End If
            If Not AgendaPuoRicevereTesto(chiaveAgenda) Then
                _ultimoErroreSincronizzazioneFile = "L'agenda sincronizzata è protetta: aprila una volta con la password per completare la sincronizzazione."
                Return Nothing
            End If
            Dim accountAgenda As String = If(agendaTrovata.ChiaveAccount, "").Trim()

            If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
            PulisciEliminazioniAgendaSync()

            Dim puoEsportare As Boolean = AgendaPuoInviareTesto(chiaveAgenda)
            Dim vociLocali As New Dictionary(Of String, Dictionary(Of String, Object))(StringComparer.Ordinal)
            If puoEsportare Then AggiornaImpronteAgenda(chiaveAgenda, vociLocali)

            Dim locali As New Dictionary(Of String, DataRow)(StringComparer.Ordinal)
            Dim altrove As New HashSet(Of String)(StringComparer.Ordinal)
            Dim eliminati As New Dictionary(Of String, DataRow)(StringComparer.Ordinal)

            For Each riga As DataRow In LeggiAppuntamentiSync("IdSync IS NOT NULL AND IdSync <> ''", Nothing).Rows
                Dim idRiga As String = TestoRigaAgendaSync(riga, "IdSync")
                If idRiga.Length = 0 Then Continue For
                If String.Equals(TestoRigaAgendaSync(riga, "ChiaveAgenda"), chiaveAgenda, StringComparison.Ordinal) Then
                    locali(idRiga) = riga
                Else
                    altrove.Add(idRiga)
                End If
            Next

            Using conn As SQLiteConnection = ApriConnessione()
                Dim tabellaEliminati As New DataTable()
                Using cmd As New SQLiteCommand("SELECT IdSync, Data, ChiaveAgenda FROM " & NomeTabellaAgendaSyncEliminati & ";", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        tabellaEliminati.Load(reader)
                    End Using
                End Using
                For Each riga As DataRow In tabellaEliminati.Rows
                    Dim idRiga As String = TestoRigaAgendaSync(riga, "IdSync")
                    If idRiga.Length > 0 Then eliminati(idRiga) = riga
                Next
            End Using

            Dim remote As Dictionary(Of String, Dictionary(Of String, Object)) = IndicizzaVociAgendaSync(vociRemote)

            Dim ordine As New List(Of String)(remote.Keys)
            ordine.Sort(StringComparer.Ordinal)
            Dim soloLocali As New List(Of String)
            For Each idLocale As String In locali.Keys
                If Not remote.ContainsKey(idLocale) Then soloLocali.Add(idLocale)
            Next
            For Each coppia As KeyValuePair(Of String, DataRow) In eliminati
                If Not remote.ContainsKey(coppia.Key) AndAlso Not locali.ContainsKey(coppia.Key) AndAlso
                   String.Equals(TestoRigaAgendaSync(coppia.Value, "ChiaveAgenda"), chiaveAgenda, StringComparison.Ordinal) Then soloLocali.Add(coppia.Key)
            Next
            soloLocali.Sort(StringComparer.Ordinal)
            ordine.AddRange(soloLocali)

            Dim categorieEsistenti As New HashSet(Of String)(OttieniElencoCategorie(), StringComparer.OrdinalIgnoreCase)
            Dim risultato As New List(Of Dictionary(Of String, Object))
            Dim modificheApplicate As Boolean = False

            For Each identificativo As String In ordine
                Try
                    Dim voceRemota As Dictionary(Of String, Object) = Nothing
                    remote.TryGetValue(identificativo, voceRemota)

                    If altrove.Contains(identificativo) AndAlso Not locali.ContainsKey(identificativo) Then
                        If voceRemota IsNot Nothing Then risultato.Add(voceRemota)
                        Continue For
                    End If

                    If voceRemota IsNot Nothing AndAlso Not locali.ContainsKey(identificativo) AndAlso Not eliminati.ContainsKey(identificativo) AndAlso Not BooleanoVoceAgendaSync(voceRemota, "eliminato") Then
                        Dim idDoppione As String = TrovaDoppioneLocaleAgenda(voceRemota, locali, vociLocali, remote)
                        If idDoppione.Length > 0 Then
                            Dim rigaDoppione As DataRow = locali(idDoppione)
                            EseguiComandoAgendaSync("UPDATE Appuntamenti SET IdSync = @Nuovo WHERE Id = @Id;",
                                New Dictionary(Of String, Object) From {{"@Nuovo", identificativo}, {"@Id", Convert.ToInt64(rigaDoppione("Id"), CultureInfo.InvariantCulture)}})
                            locali.Remove(idDoppione)
                            locali(identificativo) = rigaDoppione
                            If vociLocali.ContainsKey(idDoppione) Then
                                Dim voceSpostata As Dictionary(Of String, Object) = vociLocali(idDoppione)
                                vociLocali.Remove(idDoppione)
                                voceSpostata("id") = identificativo
                                vociLocali(identificativo) = voceSpostata
                            End If
                        End If
                    End If

                    Dim haLocale As Boolean = False
                    Dim localeEliminato As Boolean = False
                    Dim dataLocale As DateTime = DateTime.MinValue
                    Dim rigaLocale As DataRow = Nothing
                    If locali.TryGetValue(identificativo, rigaLocale) Then
                        haLocale = True
                        dataLocale = LeggiDataAgendaSync(TestoRigaAgendaSync(rigaLocale, "ModificaSync"))
                    ElseIf eliminati.ContainsKey(identificativo) Then
                        haLocale = True
                        localeEliminato = True
                        dataLocale = LeggiDataAgendaSync(TestoRigaAgendaSync(eliminati(identificativo), "Data"))
                    End If

                    Dim dataRemota As DateTime = DateTime.MinValue
                    Dim remotaEliminata As Boolean = False
                    If voceRemota IsNot Nothing Then
                        dataRemota = LeggiDataAgendaSync(TestoVoceAgendaSync(voceRemota, "modificato"))
                        remotaEliminata = BooleanoVoceAgendaSync(voceRemota, "eliminato")
                    End If

                    If voceRemota IsNot Nothing AndAlso (Not haLocale OrElse dataRemota > dataLocale) Then
                        If remotaEliminata Then
                            If rigaLocale IsNot Nothing Then EliminaAppuntamentoDaSincronizzazioneFile(rigaLocale)
                            RegistraEliminazioneAgendaSync(identificativo, dataRemota, chiaveAgenda)
                        Else
                            ApplicaAppuntamentoDaSincronizzazioneFile(chiaveAgenda, accountAgenda, identificativo, dataRemota, voceRemota, rigaLocale, categorieEsistenti, puoEsportare)
                        End If
                        modificheApplicate = True
                        risultato.Add(voceRemota)
                    ElseIf localeEliminato Then
                        risultato.Add(VoceEliminataAgendaSync(identificativo, dataLocale))
                    ElseIf rigaLocale IsNot Nothing AndAlso puoEsportare Then
                        Dim voceLocale As Dictionary(Of String, Object) = Nothing
                        If vociLocali.TryGetValue(identificativo, voceLocale) Then
                            voceLocale("modificato") = FormatoDataAgendaSync(dataLocale)
                        Else
                            voceLocale = CostruisciVoceAppuntamentoSync(rigaLocale, chiaveAgenda, identificativo, dataLocale)
                        End If
                        If voceLocale IsNot Nothing Then
                            risultato.Add(voceLocale)
                        ElseIf voceRemota IsNot Nothing Then
                            risultato.Add(voceRemota)
                        End If
                    End If
                Catch exVoce As Exception
                    log.ScriviLog("CLSAgenda.UnisciSincronizzazioneFile voce (" & If(identificativo, "") & ") - " & exVoce.ToString())
                End Try
            Next

            If modificheApplicate Then SegnalaScritturaPerCheckpoint()

            If Not puoEsportare Then
                _ultimoErroreSincronizzazioneFile = "L'agenda sincronizzata è protetta e chiusa: le modifiche fatte su questo computer partiranno quando la aprirai con la password."
                Return Nothing
            End If
            Return risultato
        Catch ex As Exception
            _ultimoErroreSincronizzazioneFile = "Errore durante la sincronizzazione dell'agenda."
            log.ScriviLog("CLSAgenda.UnisciSincronizzazioneFile - " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub AggiornaImpronteAgenda(chiaveAgenda As String, vociLocali As Dictionary(Of String, Dictionary(Of String, Object)))
        Try
            Dim righe As DataTable = LeggiAppuntamentiSync("ChiaveAgenda = @Agenda AND IdSync IS NOT NULL AND IdSync <> ''",
                New Dictionary(Of String, Object) From {{"@Agenda", chiaveAgenda}})
            For Each riga As DataRow In righe.Rows
                Try
                    Dim identificativo As String = TestoRigaAgendaSync(riga, "IdSync")
                    Dim voce As Dictionary(Of String, Object) = CostruisciVoceAppuntamentoSync(riga, chiaveAgenda, identificativo, LeggiDataAgendaSync(TestoRigaAgendaSync(riga, "ModificaSync")))
                    If voce Is Nothing Then Continue For
                    vociLocali(identificativo) = voce
                    Dim impronta As String = ImprontaVoceAgendaSync(voce)
                    Dim precedente As String = TestoRigaAgendaSync(riga, "ImprontaSync")
                    If String.Equals(precedente, impronta, StringComparison.Ordinal) Then Continue For
                    If precedente.Length = 0 Then
                        EseguiComandoAgendaSync("UPDATE Appuntamenti SET ImprontaSync = @Impronta WHERE Id = @Id;",
                            New Dictionary(Of String, Object) From {{"@Impronta", impronta}, {"@Id", Convert.ToInt64(riga("Id"), CultureInfo.InvariantCulture)}})
                    Else
                        EseguiComandoAgendaSync("UPDATE Appuntamenti SET ImprontaSync = @Impronta, ModificaSync = " & EspressioneOraAgendaSync & " WHERE Id = @Id;",
                            New Dictionary(Of String, Object) From {{"@Impronta", impronta}, {"@Id", Convert.ToInt64(riga("Id"), CultureInfo.InvariantCulture)}})
                    End If
                Catch exRiga As Exception
                    log.ScriviLog("CLSAgenda.AggiornaImpronteAgenda riga - " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiornaImpronteAgenda - " & ex.ToString())
        End Try
    End Sub

    Private Function TrovaDoppioneLocaleAgenda(voceRemota As Dictionary(Of String, Object), locali As Dictionary(Of String, DataRow),
                                               vociLocali As Dictionary(Of String, Dictionary(Of String, Object)),
                                               remote As Dictionary(Of String, Dictionary(Of String, Object))) As String
        Try
            Dim uidRemoto As String = TestoVoceAgendaSync(voceRemota, "uidServer")
            If uidRemoto.Length > 0 Then
                For Each coppia As KeyValuePair(Of String, DataRow) In locali
                    If remote.ContainsKey(coppia.Key) Then Continue For
                    If String.Equals(TestoRigaAgendaSync(coppia.Value, "UidServer"), uidRemoto, StringComparison.Ordinal) Then Return coppia.Key
                Next
            End If
            Dim oggettoRemoto As String = ChiaveOggettoAgendaSync(TestoVoceAgendaSync(voceRemota, "oggetto"))
            If oggettoRemoto.Length = 0 Then Return ""
            Dim giornoRemoto As String = DataGiornoAgendaSync(TestoVoceAgendaSync(voceRemota, "dataInizio"))
            Dim oraRemota As String = OraAgendaSync(TestoVoceAgendaSync(voceRemota, "oraInizio"))
            For Each coppia As KeyValuePair(Of String, Dictionary(Of String, Object)) In vociLocali
                If remote.ContainsKey(coppia.Key) OrElse Not locali.ContainsKey(coppia.Key) Then Continue For
                Dim uidLocale As String = TestoRigaAgendaSync(locali(coppia.Key), "UidServer")
                If uidRemoto.Length > 0 AndAlso uidLocale.Length > 0 AndAlso Not String.Equals(uidRemoto, uidLocale, StringComparison.Ordinal) Then Continue For
                If Not String.Equals(DataGiornoAgendaSync(TestoVoceAgendaSync(coppia.Value, "dataInizio")), giornoRemoto, StringComparison.Ordinal) Then Continue For
                If Not String.Equals(OraAgendaSync(TestoVoceAgendaSync(coppia.Value, "oraInizio")), oraRemota, StringComparison.Ordinal) Then Continue For
                If String.Equals(ChiaveOggettoAgendaSync(TestoVoceAgendaSync(coppia.Value, "oggetto")), oggettoRemoto, StringComparison.Ordinal) Then Return coppia.Key
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TrovaDoppioneLocaleAgenda - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ChiaveOggettoAgendaSync(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim scomposto As String = testo.Trim().Normalize(NormalizationForm.FormD)
            Dim pulito As New StringBuilder()
            For Each carattere As Char In scomposto
                If CharUnicodeInfo.GetUnicodeCategory(carattere) = UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(carattere) Then
                    pulito.Append(Char.ToLowerInvariant(carattere))
                Else
                    pulito.Append(" "c)
                End If
            Next
            Return String.Join(" ", pulito.ToString().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries))
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ChiaveOggettoAgendaSync - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ImprontaVoceAgendaSync(voce As Dictionary(Of String, Object)) As String
        Try
            Dim chiavi As New List(Of String)
            For Each chiave As String In voce.Keys
                If chiave = "id" OrElse chiave = "modificato" OrElse chiave = "eliminato" Then Continue For
                chiavi.Add(chiave)
            Next
            chiavi.Sort(StringComparer.Ordinal)
            Dim testo As New StringBuilder()
            For Each chiave As String In chiavi
                testo.Append(chiave).Append(ChrW(30)).Append(Convert.ToString(voce(chiave), CultureInfo.InvariantCulture)).Append(ChrW(29))
            Next
            Using calcolo As SHA256 = SHA256.Create()
                Dim byteImpronta As Byte() = calcolo.ComputeHash(Encoding.UTF8.GetBytes(testo.ToString()))
                Dim esadecimale As New StringBuilder()
                For Each valoreByte As Byte In byteImpronta
                    esadecimale.Append(valoreByte.ToString("x2", CultureInfo.InvariantCulture))
                Next
                Return esadecimale.ToString()
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ImprontaVoceAgendaSync - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub ApplicaAppuntamentoDaSincronizzazioneFile(chiaveAgenda As String, accountAgenda As String, identificativo As String, dataRemota As DateTime,
                                                         voce As Dictionary(Of String, Object), rigaLocale As DataRow, categorieEsistenti As HashSet(Of String),
                                                         puoEsportare As Boolean)
        Try
            Dim dataInizio As String = DataGiornoAgendaSync(TestoVoceAgendaSync(voce, "dataInizio"))
            If dataInizio.Length = 0 Then Return
            Dim dataFine As String = DataGiornoAgendaSync(TestoVoceAgendaSync(voce, "dataFine"))
            If dataFine.Length = 0 Then dataFine = dataInizio

            Dim oggetto As String = If(SanitizzaInput(TestoVoceAgendaSync(voce, "oggetto")), "")
            If oggetto.Length = 0 Then oggetto = "Appuntamento"
            Dim nome As String = If(SanitizzaInput(TestoVoceAgendaSync(voce, "nome")), "")
            Dim note As String = If(SanitizzaNote(TestoVoceAgendaSync(voce, "note")), "")
            Dim luogo As String = If(SanitizzaInput(TestoVoceAgendaSync(voce, "luogo")), "")
            Dim oraInizio As String = OraAgendaSync(TestoVoceAgendaSync(voce, "oraInizio"))
            Dim oraFine As String = OraAgendaSync(TestoVoceAgendaSync(voce, "oraFine"))
            Dim categoria As String = If(SanitizzaInput(TestoVoceAgendaSync(voce, "categoria")), "")
            Dim frequenza As Integer = InteroVoceAgendaSync(voce, "frequenza")
            If frequenza < 0 OrElse frequenza > 5 Then frequenza = 0
            Dim preavviso As Integer = InteroVoceAgendaSync(voce, "preavviso")
            If preavviso < 0 Then preavviso = 0

            If categoria.Length > 0 AndAlso Not categorieEsistenti.Contains(categoria) Then
                AggiungiCategoria(categoria)
                categorieEsistenti.Add(categoria)
            End If

            Dim modificato As String = FormatoDataAgendaSync(dataRemota)
            Dim uidDalFile As String = TestoVoceAgendaSync(voce, "uidServer")
            Dim collegatoAlServer As Boolean = rigaLocale IsNot Nothing AndAlso TestoRigaAgendaSync(rigaLocale, "UidServer").Length > 0 AndAlso accountAgenda.Length > 0
            Dim daSincronizzare As Integer
            If accountAgenda.Length = 0 Then
                daSincronizzare = 1
            ElseIf collegatoAlServer Then
                daSincronizzare = 1
            Else
                daSincronizzare = 0
            End If

            Dim idInserito As Long = 0
            Using conn As SQLiteConnection = ApriConnessione()
                Dim sql As String
                If rigaLocale IsNot Nothing Then
                    sql = "UPDATE Appuntamenti SET Oggetto = @Oggetto, Nome = @Nome, DataInizio = @DataInizio, DataFine = @DataFine, OraInizio = @OraInizio, OraFine = @OraFine, " &
                          "Categoria = @Categoria, Frequenza = @Frequenza, Stato = 'attivo', Note = @Note, Preavviso = @Preavviso, Luogo = @Luogo, ChiaveAgenda = @ChiaveAgenda, " &
                          "DaSincronizzare = @DaSincronizzare, IdSync = @IdSync, ModificaSync = @ModificaSync"
                    If Not collegatoAlServer Then sql &= ", UidServerFile = @UidServerFile"
                    sql &= " WHERE Id = @Id"
                Else
                    sql = "INSERT INTO Appuntamenti (Oggetto, Nome, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Stato, Note, Preavviso, ChiaveWG, DaSincronizzare, ChiaveAgenda, Luogo, IdSync, ModificaSync, ChiaveAccountOrigine, UidServerFile) " &
                          "VALUES (@Oggetto, @Nome, @DataInizio, @DataFine, @OraInizio, @OraFine, @Categoria, @Frequenza, 'attivo', @Note, @Preavviso, @ChiaveWG, @DaSincronizzare, @ChiaveAgenda, @Luogo, @IdSync, @ModificaSync, @Account, @UidServerFile)"
                End If

                Using cmd As New SQLiteCommand(sql, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Oggetto", TestoDaSalvare(oggetto, chiaveAgenda))
                    cmd.Parameters.AddWithValue("@Nome", If(nome.Length = 0, DBNull.Value, CObj(TestoDaSalvare(nome, chiaveAgenda))))
                    cmd.Parameters.AddWithValue("@DataInizio", dataInizio)
                    cmd.Parameters.AddWithValue("@DataFine", dataFine)
                    cmd.Parameters.AddWithValue("@OraInizio", If(oraInizio.Length = 0, DBNull.Value, CObj(oraInizio)))
                    cmd.Parameters.AddWithValue("@OraFine", If(oraFine.Length = 0, DBNull.Value, CObj(oraFine)))
                    cmd.Parameters.AddWithValue("@Categoria", If(categoria.Length = 0, DBNull.Value, CObj(categoria)))
                    cmd.Parameters.AddWithValue("@Frequenza", frequenza)
                    cmd.Parameters.AddWithValue("@Note", If(note.Length = 0, DBNull.Value, CObj(TestoDaSalvare(note, chiaveAgenda))))
                    cmd.Parameters.AddWithValue("@Preavviso", preavviso)
                    cmd.Parameters.AddWithValue("@Luogo", If(luogo.Length = 0, DBNull.Value, CObj(TestoDaSalvare(luogo, chiaveAgenda))))
                    cmd.Parameters.AddWithValue("@ChiaveAgenda", chiaveAgenda)
                    cmd.Parameters.AddWithValue("@DaSincronizzare", daSincronizzare)
                    cmd.Parameters.AddWithValue("@IdSync", identificativo)
                    cmd.Parameters.AddWithValue("@ModificaSync", modificato)
                    If rigaLocale Is Nothing OrElse Not collegatoAlServer Then cmd.Parameters.AddWithValue("@UidServerFile", uidDalFile)
                    If rigaLocale IsNot Nothing Then
                        idInserito = Convert.ToInt64(rigaLocale("Id"), CultureInfo.InvariantCulture)
                        cmd.Parameters.AddWithValue("@Id", idInserito)
                    Else
                        cmd.Parameters.AddWithValue("@ChiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Appuntamenti"))
                        cmd.Parameters.AddWithValue("@Account", accountAgenda)
                    End If
                    cmd.ExecuteNonQuery()
                End Using

                If rigaLocale Is Nothing Then
                    Using cmdId As New SQLiteCommand("SELECT Id FROM Appuntamenti WHERE IdSync = @IdSync ORDER BY Id DESC LIMIT 1;", conn)
                        cmdId.CommandTimeout = CommandTimeoutSeconds
                        cmdId.Parameters.AddWithValue("@IdSync", identificativo)
                        Dim valoreId As Object = cmdId.ExecuteScalar()
                        If valoreId IsNot Nothing AndAlso valoreId IsNot DBNull.Value Then idInserito = Convert.ToInt64(valoreId, CultureInfo.InvariantCulture)
                    End Using
                End If

                Using cmdEliminati As New SQLiteCommand("DELETE FROM " & NomeTabellaAgendaSyncEliminati & " WHERE IdSync = @IdSync;", conn)
                    cmdEliminati.CommandTimeout = CommandTimeoutSeconds
                    cmdEliminati.Parameters.AddWithValue("@IdSync", identificativo)
                    cmdEliminati.ExecuteNonQuery()
                End Using
            End Using

            Dim impronta As String = ""
            If puoEsportare AndAlso idInserito > 0 Then
                Dim righe As DataTable = LeggiAppuntamentiSync("Id = @Id", New Dictionary(Of String, Object) From {{"@Id", idInserito}})
                If righe.Rows.Count > 0 Then
                    Dim voceLocale As Dictionary(Of String, Object) = CostruisciVoceAppuntamentoSync(righe.Rows(0), chiaveAgenda, identificativo, dataRemota)
                    If voceLocale IsNot Nothing Then impronta = ImprontaVoceAgendaSync(voceLocale)
                End If
            End If
            If idInserito > 0 Then
                EseguiComandoAgendaSync("UPDATE Appuntamenti SET ImprontaSync = @Impronta WHERE Id = @Id;",
                    New Dictionary(Of String, Object) From {{"@Impronta", impronta}, {"@Id", idInserito}})
            End If
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ApplicaAppuntamentoDaSincronizzazioneFile (" & If(identificativo, "") & ") - " & ex.ToString())
        End Try
    End Sub

    Private Sub EliminaAppuntamentoDaSincronizzazioneFile(rigaLocale As DataRow)
        Try
            If rigaLocale Is Nothing Then Return
            Dim idVal As Long = Convert.ToInt64(rigaLocale("Id"), CultureInfo.InvariantCulture)
            If TestoRigaAgendaSync(rigaLocale, "UidServer").Length > 0 AndAlso TestoRigaAgendaSync(rigaLocale, "ChiaveAccountOrigine").Length > 0 Then
                RegistraEliminazioneAgenda(idVal)
            End If
            EseguiComandoAgendaSync("DELETE FROM Appuntamenti WHERE Id = @Id;", New Dictionary(Of String, Object) From {{"@Id", idVal}})
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EliminaAppuntamentoDaSincronizzazioneFile - " & ex.ToString())
        End Try
    End Sub

    Private Sub RegistraEliminazioneAgendaSync(identificativo As String, data As DateTime, chiaveAgenda As String)
        Try
            EseguiComandoAgendaSync("INSERT OR REPLACE INTO " & NomeTabellaAgendaSyncEliminati & " (IdSync, Data, ChiaveAgenda) VALUES (@IdSync, @Data, @ChiaveAgenda);",
                New Dictionary(Of String, Object) From {{"@IdSync", identificativo}, {"@Data", FormatoDataAgendaSync(data)}, {"@ChiaveAgenda", If(chiaveAgenda, "")}})
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RegistraEliminazioneAgendaSync (" & If(identificativo, "") & ") - " & ex.ToString())
        End Try
    End Sub

    Private Sub PulisciEliminazioniAgendaSync()
        Try
            EseguiComandoAgendaSync("DELETE FROM " & NomeTabellaAgendaSyncEliminati & " WHERE Data < @Limite;",
                New Dictionary(Of String, Object) From {{"@Limite", FormatoDataAgendaSync(DateTime.UtcNow.AddDays(-GiorniConservazioneEliminatiAgendaSync))}})
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.PulisciEliminazioniAgendaSync - " & ex.ToString())
        End Try
    End Sub

    Private Function CostruisciVoceAppuntamentoSync(riga As DataRow, chiaveAgenda As String, identificativo As String, dataLocale As DateTime) As Dictionary(Of String, Object)
        Try
            Dim frequenza As Integer = 0
            Integer.TryParse(TestoRigaAgendaSync(riga, "Frequenza"), NumberStyles.Integer, CultureInfo.InvariantCulture, frequenza)
            Dim preavviso As Integer = 0
            Integer.TryParse(TestoRigaAgendaSync(riga, "Preavviso"), NumberStyles.Integer, CultureInfo.InvariantCulture, preavviso)
            Dim uid As String = TestoRigaAgendaSync(riga, "UidServer")
            If uid.Length = 0 Then uid = TestoRigaAgendaSync(riga, "UidServerFile")

            Return New Dictionary(Of String, Object) From {
                {"id", identificativo},
                {"modificato", FormatoDataAgendaSync(dataLocale)},
                {"eliminato", False},
                {"oggetto", TestoLeggibileAgenda(TestoRigaAgendaSync(riga, "Oggetto"), chiaveAgenda)},
                {"nome", TestoLeggibileAgenda(TestoRigaAgendaSync(riga, "Nome"), chiaveAgenda)},
                {"note", TestoLeggibileAgenda(TestoRigaAgendaSync(riga, "Note"), chiaveAgenda)},
                {"luogo", TestoLeggibileAgenda(TestoRigaAgendaSync(riga, "Luogo"), chiaveAgenda)},
                {"dataInizio", TestoRigaAgendaSync(riga, "DataInizio")},
                {"dataFine", TestoRigaAgendaSync(riga, "DataFine")},
                {"oraInizio", TestoRigaAgendaSync(riga, "OraInizio")},
                {"oraFine", TestoRigaAgendaSync(riga, "OraFine")},
                {"categoria", TestoRigaAgendaSync(riga, "Categoria")},
                {"frequenza", frequenza},
                {"preavviso", preavviso},
                {"uidServer", uid}
            }
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CostruisciVoceAppuntamentoSync (" & If(identificativo, "") & ") - " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function DataGiornoAgendaSync(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim data As DateTime
            Dim formati As String() = {"yyyy-MM-dd", "dd/MM/yyyy"}
            If DateTime.TryParseExact(testo.Trim(), formati, CultureInfo.InvariantCulture, DateTimeStyles.None, data) Then Return data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.DataGiornoAgendaSync - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function OraAgendaSync(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim ora As DateTime
            Dim formati As String() = {"HH:mm", "H:mm", "HH:mm:ss"}
            If DateTime.TryParseExact(testo.Trim(), formati, CultureInfo.InvariantCulture, DateTimeStyles.None, ora) Then Return ora.ToString("HH:mm", CultureInfo.InvariantCulture)
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OraAgendaSync - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function TestoRigaAgendaSync(riga As DataRow, colonna As String) As String
        Try
            If riga Is Nothing OrElse Not riga.Table.Columns.Contains(colonna) OrElse riga.IsNull(colonna) Then Return ""
            Return Convert.ToString(riga(colonna), CultureInfo.InvariantCulture)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TestoRigaAgendaSync (" & If(colonna, "") & ") - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function VoceEliminataAgendaSync(identificativo As String, data As DateTime) As Dictionary(Of String, Object)
        Try
            Return New Dictionary(Of String, Object) From {
                {"id", identificativo},
                {"modificato", FormatoDataAgendaSync(data)},
                {"eliminato", True}
            }
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.VoceEliminataAgendaSync - " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function IndicizzaVociAgendaSync(voci As List(Of Dictionary(Of String, Object))) As Dictionary(Of String, Dictionary(Of String, Object))
        Dim risultato As New Dictionary(Of String, Dictionary(Of String, Object))(StringComparer.OrdinalIgnoreCase)
        Try
            If voci Is Nothing Then Return risultato
            For Each voce As Dictionary(Of String, Object) In voci
                Try
                    If voce Is Nothing Then Continue For
                    Dim identificativo As String = TestoVoceAgendaSync(voce, "id")
                    If identificativo.Length = 0 Then Continue For
                    Dim esistente As Dictionary(Of String, Object) = Nothing
                    If risultato.TryGetValue(identificativo, esistente) Then
                        If LeggiDataAgendaSync(TestoVoceAgendaSync(voce, "modificato")) <= LeggiDataAgendaSync(TestoVoceAgendaSync(esistente, "modificato")) Then Continue For
                    End If
                    risultato(identificativo) = voce
                Catch exVoce As Exception
                    log.ScriviLog("CLSAgenda.IndicizzaVociAgendaSync voce - " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.IndicizzaVociAgendaSync - " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function TestoVoceAgendaSync(voce As Dictionary(Of String, Object), chiave As String) As String
        Try
            If voce Is Nothing Then Return ""
            Dim valore As Object = Nothing
            If Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
            Return Convert.ToString(valore, CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TestoVoceAgendaSync (" & If(chiave, "") & ") - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function BooleanoVoceAgendaSync(voce As Dictionary(Of String, Object), chiave As String) As Boolean
        Try
            If voce Is Nothing Then Return False
            Dim valore As Object = Nothing
            If Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return False
            If TypeOf valore Is Boolean Then Return CBool(valore)
            Dim testo As String = Convert.ToString(valore, CultureInfo.InvariantCulture).Trim().ToLowerInvariant()
            Return testo = "true" OrElse testo = "1"
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.BooleanoVoceAgendaSync (" & If(chiave, "") & ") - " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function InteroVoceAgendaSync(voce As Dictionary(Of String, Object), chiave As String) As Integer
        Try
            Dim risultato As Integer = 0
            Integer.TryParse(TestoVoceAgendaSync(voce, chiave), NumberStyles.Integer, CultureInfo.InvariantCulture, risultato)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.InteroVoceAgendaSync (" & If(chiave, "") & ") - " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function LeggiDataAgendaSync(testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim risultato As DateTime
            If DateTime.TryParse(testo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.LeggiDataAgendaSync - " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Private Function FormatoDataAgendaSync(data As DateTime) As String
        Try
            Dim utc As DateTime = data
            If utc.Kind = DateTimeKind.Local Then utc = utc.ToUniversalTime()
            Return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.FormatoDataAgendaSync - " & ex.ToString())
            Return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        End Try
    End Function

End Class