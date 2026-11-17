Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports wgcontrols
Partial Public Class CLSContatti

    Private Const NomeTabellaSyncEliminati As String = "contatti_sync_eliminati"
    Private Const GiorniConservazioneEliminatiSync As Integer = 180
    Private Const EspressioneOraSync As String = "strftime('%Y-%m-%dT%H:%M:%fZ','now')"
    Private Const PrefissoIdServer As String = "srv:"
    Private Const ChiaveConfigIdServerMigrati As String = "RubricaSyncIdServerMigrati"

    Private Shared ReadOnly _lockIstanzeAperte As New Object()
    Private Shared ReadOnly _istanzeAperte As New List(Of WeakReference)

    Private Sub PreparaSincronizzazioneFile()
        Try
            AggiungiColonnaSeManca(NomeTabellaContatti, "IdSync", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "ModificaSync", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "ImprontaSync", "TEXT")
            AggiungiColonnaSeManca(NomeTabellaContatti, "UidServerFile", "TEXT")
            _db.EseguiNonQuery("CREATE TABLE IF NOT EXISTS " & NomeTabellaSyncEliminati & " (IdSync TEXT PRIMARY KEY, Data TEXT NOT NULL, ChiaveRubrica TEXT);", Nothing)

            _db.EseguiNonQuery("DROP TRIGGER IF EXISTS trg_contatti_sync_upd;", Nothing)
            _db.EseguiNonQuery("DROP TRIGGER IF EXISTS trg_recapiti_sync_ins;", Nothing)
            _db.EseguiNonQuery("DROP TRIGGER IF EXISTS trg_recapiti_sync_del;", Nothing)

            MigraIdentificativiServer()

            _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET IdSync = lower(hex(randomblob(16))) WHERE IdSync IS NULL OR IdSync = '';", Nothing)
            _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ModificaSync = " & EspressioneOraSync & " WHERE ModificaSync IS NULL OR ModificaSync = '';", Nothing)
            _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS idx_contatti_idsync ON " & NomeTabellaContatti & "(IdSync);", Nothing)

            _db.EseguiNonQuery(
                "CREATE TRIGGER IF NOT EXISTS trg_contatti_sync_ins AFTER INSERT ON " & NomeTabellaContatti & " " &
                "WHEN NEW.IdSync IS NULL OR NEW.IdSync = '' OR NEW.ModificaSync IS NULL OR NEW.ModificaSync = '' BEGIN " &
                "UPDATE " & NomeTabellaContatti & " SET " &
                "IdSync = CASE WHEN NEW.IdSync IS NULL OR NEW.IdSync = '' THEN lower(hex(randomblob(16))) ELSE NEW.IdSync END, " &
                "ModificaSync = CASE WHEN NEW.ModificaSync IS NULL OR NEW.ModificaSync = '' THEN " & EspressioneOraSync & " ELSE NEW.ModificaSync END " &
                "WHERE rowid = NEW.rowid; END;", Nothing)

            _db.EseguiNonQuery(
                "CREATE TRIGGER IF NOT EXISTS trg_contatti_sync_del AFTER DELETE ON " & NomeTabellaContatti & " " &
                "WHEN OLD.IdSync IS NOT NULL AND OLD.IdSync <> '' BEGIN " &
                "INSERT OR REPLACE INTO " & NomeTabellaSyncEliminati & " (IdSync, Data, ChiaveRubrica) VALUES (OLD.IdSync, " & EspressioneOraSync & ", OLD.ChiaveRubrica); END;", Nothing)
        Catch ex As Exception
            ScriviLog("CLSContatti.PreparaSincronizzazioneFile: " & ex.ToString())
        End Try
    End Sub

    Private Sub MigraIdentificativiServer()
        Try
            Dim configurazione As New WGConfig()
            If configurazione.LeggiValore(ChiaveConfigIdServerMigrati) = "1" Then Return
            _db.EseguiNonQuery(
                "UPDATE " & NomeTabellaContatti & " SET IdSync = '" & PrefissoIdServer & "' || UidServer " &
                "WHERE UidServer IS NOT NULL AND UidServer <> '' AND ChiaveAccountOrigine IS NOT NULL AND ChiaveAccountOrigine <> '';", Nothing)
            configurazione.SalvaValore(ChiaveConfigIdServerMigrati, "1")
        Catch ex As Exception
            ScriviLog("CLSContatti.MigraIdentificativiServer: " & ex.ToString())
        End Try
    End Sub

    Friend Shared Function IdSyncDaUidServer(uidServer As String) As String
        Try
            Return PrefissoIdServer & If(uidServer, "").Trim()
        Catch ex As Exception
            ScriviLog("CLSContatti.IdSyncDaUidServer: " & ex.ToString())
            Return ""
        End Try
    End Function

    Friend Sub RegistraIstanzaAperta()
        Try
            SyncLock _lockIstanzeAperte
                For indice As Integer = _istanzeAperte.Count - 1 To 0 Step -1
                    Dim esistente As Object = _istanzeAperte(indice).Target
                    If esistente Is Nothing Then
                        _istanzeAperte.RemoveAt(indice)
                    ElseIf Object.ReferenceEquals(esistente, Me) Then
                        Return
                    End If
                Next
                _istanzeAperte.Add(New WeakReference(Me))
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSContatti.RegistraIstanzaAperta: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function IstanzaConRubricaAperta(chiaveRubrica As String) As CLSContatti
        Try
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            Dim candidati As New List(Of CLSContatti)
            SyncLock _lockIstanzeAperte
                For indice As Integer = _istanzeAperte.Count - 1 To 0 Step -1
                    Dim istanza As CLSContatti = TryCast(_istanzeAperte(indice).Target, CLSContatti)
                    If istanza Is Nothing OrElse istanza._smaltito Then
                        _istanzeAperte.RemoveAt(indice)
                    Else
                        candidati.Add(istanza)
                    End If
                Next
            End SyncLock
            For Each istanza As CLSContatti In candidati
                If istanza.RubricaSbloccata(chiaveRubrica) AndAlso istanza.RubricaPuoInviareTesto(chiaveRubrica) Then Return istanza
            Next
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSContatti.IstanzaConRubricaAperta: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniRubricheSincronizzabili() As DataTable
        Try
            Return _db.EseguiQuery("SELECT * FROM " & NomeTabellaRubriche & " ORDER BY ChiaveAccount ASC, Predefinita DESC, Nome ASC;", Nothing)
        Catch ex As Exception
            ImpostaErrore("CLSContatti.OttieniRubricheSincronizzabili", ex)
            Return New DataTable()
        End Try
    End Function

    Friend Function TrovaDoppioneDaServer(chiaveAccount As String, uidServer As String, nome As String, cognome As String,
                                          email As List(Of String), telefono As List(Of String), cellulare As List(Of String)) As DataRow
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) OrElse String.IsNullOrWhiteSpace(uidServer) Then Return Nothing
            Dim candidati As DataTable = _db.EseguiQuery(
                "SELECT * FROM " & NomeTabellaContatti & " WHERE (UidServer IS NULL OR UidServer = '') AND ChiaveRubrica IN " &
                "(SELECT ChiaveWG FROM " & NomeTabellaRubriche & " WHERE ChiaveAccount = @account);",
                New Dictionary(Of String, Object) From {{"@account", chiaveAccount}})
            If candidati Is Nothing OrElse candidati.Rows.Count = 0 Then Return Nothing

            Dim idDalServer As String = IdSyncDaUidServer(uidServer)
            For Each riga As DataRow In candidati.Rows
                If String.Equals(ValoreColonna(riga, "UidServerFile"), uidServer.Trim(), StringComparison.Ordinal) OrElse
                   String.Equals(ValoreColonna(riga, "IdSync"), idDalServer, StringComparison.Ordinal) Then Return riga
            Next

            Dim cercato As New Dictionary(Of String, Object) From {
                {"nome", If(nome, "")},
                {"cognome", If(cognome, "")},
                {"email", If(email, New List(Of String))},
                {"telefoni", If(telefono, New List(Of String))},
                {"cellulari", If(cellulare, New List(Of String))}
            }
            For Each riga As DataRow In candidati.Rows
                Try
                    Dim chiave As String = ValoreColonna(riga, "ChiaveWG")
                    Dim rubrica As String = ValoreColonna(riga, "ChiaveRubrica")
                    If RubricaProtetta(rubrica) AndAlso Not RubricaPuoInviareTesto(rubrica) Then Continue For
                    Dim voce As Dictionary(Of String, Object) = CostruisciVoceContattoSync(riga, ValoreColonna(riga, "IdSync"), DateTime.UtcNow)
                    If voce IsNot Nothing AndAlso StessoContattoSync(voce, cercato) Then Return OttieniContattoGrezzo(chiave)
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.TrovaDoppioneDaServer riga: " & exRiga.ToString())
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSContatti.TrovaDoppioneDaServer (" & If(uidServer, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function UnisciSincronizzazioneFile(chiaveRubrica As String, vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        Try
            _ultimoErrore = ""
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            chiaveRubrica = chiaveRubrica.Trim()

            Dim rigaRubrica As DataRow = _db.OttieniRecord(NomeTabellaRubriche, chiaveRubrica)
            If rigaRubrica Is Nothing Then
                _ultimoErrore = "La rubrica scelta per la sincronizzazione non esiste più."
                Return Nothing
            End If
            If RubricaProtetta(chiaveRubrica) AndAlso Not RubricaPuoRicevereTesto(chiaveRubrica) Then
                _ultimoErrore = "La rubrica sincronizzata è protetta: aprila una volta con la password per completare la sincronizzazione."
                Return Nothing
            End If
            Dim accountRubrica As String = ValoreColonna(rigaRubrica, "ChiaveAccount")

            If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
            PulisciEliminazioniSincronizzazioneFile()

            Dim puoEsportare As Boolean = RubricaPuoInviareTesto(chiaveRubrica)
            Dim vociLocali As New Dictionary(Of String, Dictionary(Of String, Object))(StringComparer.Ordinal)
            If puoEsportare Then AggiornaImpronteContatti(chiaveRubrica, vociLocali)

            Dim locali As New Dictionary(Of String, DataRow)(StringComparer.Ordinal)
            Dim altrove As New HashSet(Of String)(StringComparer.Ordinal)
            Dim tutti As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG, IdSync, ModificaSync, ChiaveRubrica, UidServer, ChiaveAccountOrigine FROM " & NomeTabellaContatti & " WHERE IdSync IS NOT NULL AND IdSync <> '';", Nothing)
            If tutti IsNot Nothing Then
                For Each riga As DataRow In tutti.Rows
                    Dim idRiga As String = ValoreColonna(riga, "IdSync")
                    If idRiga.Length = 0 Then Continue For
                    If String.Equals(ValoreColonna(riga, "ChiaveRubrica"), chiaveRubrica, StringComparison.OrdinalIgnoreCase) Then
                        locali(idRiga) = riga
                    Else
                        altrove.Add(idRiga)
                    End If
                Next
            End If

            Dim eliminati As New Dictionary(Of String, DataRow)(StringComparer.Ordinal)
            Dim tabellaEliminati As DataTable = _db.EseguiQuery("SELECT IdSync, Data, ChiaveRubrica FROM " & NomeTabellaSyncEliminati & ";", Nothing)
            If tabellaEliminati IsNot Nothing Then
                For Each riga As DataRow In tabellaEliminati.Rows
                    Dim idRiga As String = ValoreColonna(riga, "IdSync")
                    If idRiga.Length > 0 Then eliminati(idRiga) = riga
                Next
            End If

            Dim remote As Dictionary(Of String, Dictionary(Of String, Object)) = IndicizzaVociSync(vociRemote)

            Dim ordine As New List(Of String)(remote.Keys)
            ordine.Sort(StringComparer.Ordinal)
            Dim soloLocali As New List(Of String)
            For Each idLocale As String In locali.Keys
                If Not remote.ContainsKey(idLocale) Then soloLocali.Add(idLocale)
            Next
            For Each coppia As KeyValuePair(Of String, DataRow) In eliminati
                If Not remote.ContainsKey(coppia.Key) AndAlso Not locali.ContainsKey(coppia.Key) AndAlso
                   String.Equals(ValoreColonna(coppia.Value, "ChiaveRubrica"), chiaveRubrica, StringComparison.OrdinalIgnoreCase) Then soloLocali.Add(coppia.Key)
            Next
            soloLocali.Sort(StringComparer.Ordinal)
            ordine.AddRange(soloLocali)

            Dim risultato As New List(Of Dictionary(Of String, Object))

            For Each identificativo As String In ordine
                Try
                    Dim voceRemota As Dictionary(Of String, Object) = Nothing
                    remote.TryGetValue(identificativo, voceRemota)

                    If altrove.Contains(identificativo) AndAlso Not locali.ContainsKey(identificativo) Then
                        If voceRemota IsNot Nothing Then risultato.Add(voceRemota)
                        Continue For
                    End If

                    If voceRemota IsNot Nothing AndAlso Not locali.ContainsKey(identificativo) AndAlso Not eliminati.ContainsKey(identificativo) AndAlso Not BooleanoVoceSync(voceRemota, "eliminato") Then
                        Dim idDoppione As String = TrovaDoppioneLocale(voceRemota, locali, vociLocali, remote)
                        If idDoppione.Length > 0 Then
                            Dim rigaDoppione As DataRow = locali(idDoppione)
                            _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET IdSync = @nuovo WHERE ChiaveWG = @chiave;",
                                New Dictionary(Of String, Object) From {{"@nuovo", identificativo}, {"@chiave", ValoreColonna(rigaDoppione, "ChiaveWG")}})
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
                        dataLocale = LeggiDataSync(ValoreColonna(rigaLocale, "ModificaSync"))
                    ElseIf eliminati.ContainsKey(identificativo) Then
                        haLocale = True
                        localeEliminato = True
                        dataLocale = LeggiDataSync(ValoreColonna(eliminati(identificativo), "Data"))
                    End If

                    Dim dataRemota As DateTime = DateTime.MinValue
                    Dim remotaEliminata As Boolean = False
                    If voceRemota IsNot Nothing Then
                        dataRemota = LeggiDataSync(TestoVoceSync(voceRemota, "modificato"))
                        remotaEliminata = BooleanoVoceSync(voceRemota, "eliminato")
                    End If

                    If voceRemota IsNot Nothing AndAlso (Not haLocale OrElse dataRemota > dataLocale) Then
                        If remotaEliminata Then
                            If rigaLocale IsNot Nothing Then
                                EliminaContattoDaSincronizzazioneFile(rigaLocale, identificativo, dataRemota, chiaveRubrica)
                            Else
                                RegistraEliminazioneSincronizzazioneFile(identificativo, dataRemota, chiaveRubrica)
                            End If
                        Else
                            ApplicaContattoDaSincronizzazioneFile(chiaveRubrica, accountRubrica, identificativo, dataRemota, voceRemota, rigaLocale, puoEsportare)
                        End If
                        risultato.Add(voceRemota)
                    ElseIf localeEliminato Then
                        risultato.Add(VoceEliminataSync(identificativo, dataLocale))
                    ElseIf rigaLocale IsNot Nothing AndAlso puoEsportare Then
                        Dim voceLocale As Dictionary(Of String, Object) = Nothing
                        If vociLocali.TryGetValue(identificativo, voceLocale) Then
                            voceLocale("modificato") = FormatoDataSync(dataLocale)
                        Else
                            voceLocale = CostruisciVoceContattoSync(rigaLocale, identificativo, dataLocale)
                        End If
                        If voceLocale IsNot Nothing Then
                            risultato.Add(voceLocale)
                        ElseIf voceRemota IsNot Nothing Then
                            risultato.Add(voceRemota)
                        End If
                    End If
                Catch exVoce As Exception
                    ScriviLog("CLSContatti.UnisciSincronizzazioneFile voce (" & If(identificativo, "") & "): " & exVoce.ToString())
                End Try
            Next

            If Not puoEsportare Then
                _ultimoErrore = "La rubrica sincronizzata è protetta e chiusa: le modifiche fatte su questo computer partiranno quando la aprirai con la password."
                Return Nothing
            End If
            Return risultato
        Catch ex As Exception
            ImpostaErrore("CLSContatti.UnisciSincronizzazioneFile", ex)
            ScriviLog("CLSContatti.UnisciSincronizzazioneFile: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub AggiornaImpronteContatti(chiaveRubrica As String, vociLocali As Dictionary(Of String, Dictionary(Of String, Object)))
        Try
            Dim righe As DataTable = _db.EseguiQuery(
                "SELECT ChiaveWG, IdSync, ModificaSync, ImprontaSync FROM " & NomeTabellaContatti & " WHERE ChiaveRubrica = @rubrica AND IdSync IS NOT NULL AND IdSync <> '';",
                New Dictionary(Of String, Object) From {{"@rubrica", chiaveRubrica}})
            If righe Is Nothing Then Return
            For Each riga As DataRow In righe.Rows
                Try
                    Dim identificativo As String = ValoreColonna(riga, "IdSync")
                    Dim voce As Dictionary(Of String, Object) = CostruisciVoceContattoSync(riga, identificativo, LeggiDataSync(ValoreColonna(riga, "ModificaSync")))
                    If voce Is Nothing Then Continue For
                    vociLocali(identificativo) = voce
                    Dim impronta As String = ImprontaVoceSync(voce)
                    Dim precedente As String = ValoreColonna(riga, "ImprontaSync")
                    If String.Equals(precedente, impronta, StringComparison.Ordinal) Then Continue For
                    If precedente.Length = 0 Then
                        _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ImprontaSync = @impronta WHERE ChiaveWG = @chiave;",
                            New Dictionary(Of String, Object) From {{"@impronta", impronta}, {"@chiave", ValoreColonna(riga, "ChiaveWG")}})
                    Else
                        _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ImprontaSync = @impronta, ModificaSync = " & EspressioneOraSync & " WHERE ChiaveWG = @chiave;",
                            New Dictionary(Of String, Object) From {{"@impronta", impronta}, {"@chiave", ValoreColonna(riga, "ChiaveWG")}})
                    End If
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.AggiornaImpronteContatti riga: " & exRiga.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.AggiornaImpronteContatti: " & ex.ToString())
        End Try
    End Sub

    Private Function TrovaDoppioneLocale(voceRemota As Dictionary(Of String, Object), locali As Dictionary(Of String, DataRow),
                                         vociLocali As Dictionary(Of String, Dictionary(Of String, Object)),
                                         remote As Dictionary(Of String, Dictionary(Of String, Object))) As String
        Try
            Dim uidRemoto As String = TestoVoceSync(voceRemota, "uidServer")
            If uidRemoto.Length > 0 Then
                For Each coppia As KeyValuePair(Of String, DataRow) In locali
                    If remote.ContainsKey(coppia.Key) Then Continue For
                    If String.Equals(ValoreColonna(coppia.Value, "UidServer"), uidRemoto, StringComparison.Ordinal) Then Return coppia.Key
                Next
            End If
            For Each coppia As KeyValuePair(Of String, Dictionary(Of String, Object)) In vociLocali
                If remote.ContainsKey(coppia.Key) OrElse Not locali.ContainsKey(coppia.Key) Then Continue For
                Dim uidLocale As String = ValoreColonna(locali(coppia.Key), "UidServer")
                If uidRemoto.Length > 0 AndAlso uidLocale.Length > 0 AndAlso Not String.Equals(uidRemoto, uidLocale, StringComparison.Ordinal) Then Continue For
                If StessoContattoSync(coppia.Value, voceRemota) Then Return coppia.Key
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSContatti.TrovaDoppioneLocale: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function StessoContattoSync(prima As Dictionary(Of String, Object), seconda As Dictionary(Of String, Object)) As Boolean
        Try
            Dim nomePrima As String = ChiaveNomeSync(TestoVoceSync(prima, "nome") & " " & TestoVoceSync(prima, "cognome"), TestoVoceSync(prima, "azienda"))
            Dim nomeSeconda As String = ChiaveNomeSync(TestoVoceSync(seconda, "nome") & " " & TestoVoceSync(seconda, "cognome"), TestoVoceSync(seconda, "azienda"))
            If nomePrima.Length = 0 OrElse Not String.Equals(nomePrima, nomeSeconda, StringComparison.Ordinal) Then Return False

            Dim telefoniPrima As HashSet(Of String) = TelefoniSync(prima)
            Dim telefoniSeconda As HashSet(Of String) = TelefoniSync(seconda)
            Dim emailPrima As HashSet(Of String) = EmailSync(prima)
            Dim emailSeconda As HashSet(Of String) = EmailSync(seconda)

            If telefoniPrima.Count = 0 AndAlso telefoniSeconda.Count = 0 AndAlso emailPrima.Count = 0 AndAlso emailSeconda.Count = 0 Then Return True
            For Each telefono As String In telefoniPrima
                If telefoniSeconda.Contains(telefono) Then Return True
            Next
            For Each indirizzo As String In emailPrima
                If emailSeconda.Contains(indirizzo) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSContatti.StessoContattoSync: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function ChiaveNomeSync(nomeCompleto As String, azienda As String) As String
        Try
            Dim testo As String = If(nomeCompleto, "").Trim()
            If testo.Length = 0 Then testo = If(azienda, "").Trim()
            If testo.Length = 0 Then Return ""
            Dim scomposto As String = testo.Normalize(NormalizationForm.FormD)
            Dim pulito As New StringBuilder()
            For Each carattere As Char In scomposto
                If CharUnicodeInfo.GetUnicodeCategory(carattere) = UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(carattere) Then
                    pulito.Append(Char.ToLowerInvariant(carattere))
                Else
                    pulito.Append(" "c)
                End If
            Next
            Dim parole As New List(Of String)(pulito.ToString().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries))
            parole.Sort(StringComparer.Ordinal)
            Return String.Join(" ", parole.ToArray())
        Catch ex As Exception
            ScriviLog("CLSContatti.ChiaveNomeSync: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function TelefoniSync(voce As Dictionary(Of String, Object)) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.Ordinal)
        Try
            Dim tutti As New List(Of String)(ListaVoceSync(voce, "telefoni"))
            tutti.AddRange(ListaVoceSync(voce, "cellulari"))
            For Each numero As String In tutti
                Dim cifre As New StringBuilder()
                For Each carattere As Char In numero
                    If Char.IsDigit(carattere) Then cifre.Append(carattere)
                Next
                Dim testo As String = cifre.ToString()
                If testo.Length > 9 Then testo = testo.Substring(testo.Length - 9)
                If testo.Length >= 5 Then risultato.Add(testo)
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.TelefoniSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function EmailSync(voce As Dictionary(Of String, Object)) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.Ordinal)
        Try
            For Each indirizzo As String In ListaVoceSync(voce, "email")
                Dim testo As String = indirizzo.Trim().ToLowerInvariant()
                If testo.Length > 0 Then risultato.Add(testo)
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.EmailSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function ImprontaVoceSync(voce As Dictionary(Of String, Object)) As String
        Try
            Dim chiavi As New List(Of String)
            For Each chiave As String In voce.Keys
                If chiave = "id" OrElse chiave = "modificato" OrElse chiave = "eliminato" Then Continue For
                chiavi.Add(chiave)
            Next
            chiavi.Sort(StringComparer.Ordinal)
            Dim testo As New StringBuilder()
            For Each chiave As String In chiavi
                testo.Append(chiave).Append(ChrW(30))
                Dim valore As Object = voce(chiave)
                If TypeOf valore Is Boolean Then
                    testo.Append(If(CBool(valore), "1", "0"))
                ElseIf TypeOf valore Is String Then
                    testo.Append(CStr(valore))
                ElseIf TypeOf valore Is IEnumerable Then
                    For Each elemento As Object In CType(valore, IEnumerable)
                        testo.Append(Convert.ToString(elemento, CultureInfo.InvariantCulture)).Append(ChrW(31))
                    Next
                ElseIf valore IsNot Nothing Then
                    testo.Append(Convert.ToString(valore, CultureInfo.InvariantCulture))
                End If
                testo.Append(ChrW(29))
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
            ScriviLog("CLSContatti.ImprontaVoceSync: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Sub ApplicaContattoDaSincronizzazioneFile(chiaveRubrica As String, accountRubrica As String, identificativo As String, dataRemota As DateTime,
                                                     voce As Dictionary(Of String, Object), rigaLocale As DataRow, puoEsportare As Boolean)
        Try
            Dim listaEmail As List(Of String) = NormalizzaListaRecapiti(ListaVoceSync(voce, "email"))
            Dim listaTelefono As List(Of String) = NormalizzaListaRecapiti(ListaVoceSync(voce, "telefoni"))
            Dim listaCellulare As List(Of String) = NormalizzaListaRecapiti(ListaVoceSync(voce, "cellulari"))
            Dim listaIndirizzo As List(Of String) = NormalizzaListaRecapiti(ListaVoceSync(voce, "indirizzi"))
            Dim listaSito As List(Of String) = NormalizzaListaRecapiti(ListaVoceSync(voce, "siti"))
            Dim modificato As String = FormatoDataSync(dataRemota)

            Dim valori As New Dictionary(Of String, Object) From {
                {"Nome", TestoVoceSync(voce, "nome")},
                {"Cognome", TestoVoceSync(voce, "cognome")},
                {"Email", PrimoValoreLista(listaEmail)},
                {"Telefono", PrimoValoreLista(listaTelefono)},
                {"Cellulare", PrimoValoreLista(listaCellulare)},
                {"Azienda", TestoVoceSync(voce, "azienda")},
                {"ChiaveCategoria", ChiaveCategoriaPerNomeSync(TestoVoceSync(voce, "categoria"))},
                {"Note", TestoVoceSync(voce, "note")},
                {"Preferito", If(BooleanoVoceSync(voce, "preferito"), 1, 0)},
                {"ChiaveRubrica", chiaveRubrica},
                {"IdSync", identificativo},
                {"ModificaSync", modificato}
            }

            Dim collegatoAlServer As Boolean = rigaLocale IsNot Nothing AndAlso ValoreColonna(rigaLocale, "UidServer").Length > 0 AndAlso accountRubrica.Length > 0
            If rigaLocale Is Nothing Then
                valori("ChiaveAccountOrigine") = accountRubrica
                valori("DaSincronizzare") = 0
                valori("UidServerFile") = TestoVoceSync(voce, "uidServer")
            ElseIf collegatoAlServer Then
                valori("DaSincronizzare") = 1
            ElseIf accountRubrica.Length = 0 Then
                valori("ChiaveAccountOrigine") = ""
                valori("DaSincronizzare") = 0
            Else
                valori("UidServerFile") = TestoVoceSync(voce, "uidServer")
            End If
            valori = CifraValoriContatto(chiaveRubrica, valori)

            Dim chiaveContatto As String = ""
            If rigaLocale IsNot Nothing Then
                chiaveContatto = ValoreColonna(rigaLocale, "ChiaveWG")
                If Not _db.AggiornaRecord(NomeTabellaContatti, chiaveContatto, valori) Then Return
            Else
                chiaveContatto = _db.InserisciRecord(NomeTabellaContatti, valori)
            End If
            If String.IsNullOrWhiteSpace(chiaveContatto) Then Return

            SostituisciRecapiti(chiaveContatto, CategoriaRecapitoEmail, listaEmail, chiaveRubrica)
            SostituisciRecapiti(chiaveContatto, CategoriaRecapitoTelefono, listaTelefono, chiaveRubrica)
            SostituisciRecapiti(chiaveContatto, CategoriaRecapitoCellulare, listaCellulare, chiaveRubrica)
            SostituisciRecapiti(chiaveContatto, CategoriaRecapitoIndirizzo, listaIndirizzo, chiaveRubrica)
            SostituisciRecapiti(chiaveContatto, CategoriaRecapitoSito, listaSito, chiaveRubrica)

            Dim impronta As String = ""
            If puoEsportare Then
                Dim rigaAggiornata As DataRow = OttieniContattoGrezzo(chiaveContatto)
                If rigaAggiornata IsNot Nothing Then
                    Dim voceLocale As Dictionary(Of String, Object) = CostruisciVoceContattoSync(rigaAggiornata, identificativo, dataRemota)
                    If voceLocale IsNot Nothing Then impronta = ImprontaVoceSync(voceLocale)
                End If
            End If

            _db.EseguiNonQuery("UPDATE " & NomeTabellaContatti & " SET ModificaSync = @modificato, ImprontaSync = @impronta WHERE ChiaveWG = @chiave;",
                New Dictionary(Of String, Object) From {{"@modificato", modificato}, {"@impronta", impronta}, {"@chiave", chiaveContatto}})
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaSyncEliminati & " WHERE IdSync = @id;",
                New Dictionary(Of String, Object) From {{"@id", identificativo}})
        Catch ex As Exception
            ScriviLog("CLSContatti.ApplicaContattoDaSincronizzazioneFile (" & If(identificativo, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub EliminaContattoDaSincronizzazioneFile(rigaLocale As DataRow, identificativo As String, dataRemota As DateTime, chiaveRubrica As String)
        Try
            Dim chiaveContatto As String = ValoreColonna(rigaLocale, "ChiaveWG")
            If chiaveContatto.Length > 0 Then
                Dim collegatoAlServer As Boolean = ValoreColonna(rigaLocale, "UidServer").Length > 0 AndAlso ValoreColonna(rigaLocale, "ChiaveAccountOrigine").Length > 0
                If collegatoAlServer Then
                    EliminaContatto(chiaveContatto)
                Else
                    EliminaRecapitiContatto(chiaveContatto)
                    _db.EliminaRecord(NomeTabellaContatti, chiaveContatto)
                End If
            End If
            RegistraEliminazioneSincronizzazioneFile(identificativo, dataRemota, chiaveRubrica)
        Catch ex As Exception
            ScriviLog("CLSContatti.EliminaContattoDaSincronizzazioneFile (" & If(identificativo, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub RegistraEliminazioneSincronizzazioneFile(identificativo As String, data As DateTime, chiaveRubrica As String)
        Try
            _db.EseguiNonQuery("INSERT OR REPLACE INTO " & NomeTabellaSyncEliminati & " (IdSync, Data, ChiaveRubrica) VALUES (@id, @data, @rubrica);",
                New Dictionary(Of String, Object) From {{"@id", identificativo}, {"@data", FormatoDataSync(data)}, {"@rubrica", If(chiaveRubrica, "")}})
        Catch ex As Exception
            ScriviLog("CLSContatti.RegistraEliminazioneSincronizzazioneFile (" & If(identificativo, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub PulisciEliminazioniSincronizzazioneFile()
        Try
            Dim limite As String = FormatoDataSync(DateTime.UtcNow.AddDays(-GiorniConservazioneEliminatiSync))
            _db.EseguiNonQuery("DELETE FROM " & NomeTabellaSyncEliminati & " WHERE Data < @limite;",
                New Dictionary(Of String, Object) From {{"@limite", limite}})
        Catch ex As Exception
            ScriviLog("CLSContatti.PulisciEliminazioniSincronizzazioneFile: " & ex.ToString())
        End Try
    End Sub

    Private Function CostruisciVoceContattoSync(rigaGrezza As DataRow, identificativo As String, dataLocale As DateTime) As Dictionary(Of String, Object)
        Try
            Dim chiaveContatto As String = ValoreColonna(rigaGrezza, "ChiaveWG")
            If chiaveContatto.Length = 0 Then Return Nothing
            Dim riga As DataRow = OttieniContatto(chiaveContatto)
            If riga Is Nothing Then Return Nothing

            Dim recapiti As Dictionary(Of String, List(Of String)) = OttieniTuttiIRecapiti(chiaveContatto)
            Dim telefoni As List(Of String) = RecapitiConRiservaSync(recapiti, CategoriaRecapitoTelefono, ValoreColonna(riga, "Telefono"))
            Dim cellulari As List(Of String) = RecapitiConRiservaSync(recapiti, CategoriaRecapitoCellulare, ValoreColonna(riga, "Cellulare"))
            Dim email As List(Of String) = RecapitiConRiservaSync(recapiti, CategoriaRecapitoEmail, ValoreColonna(riga, "Email"))
            Dim indirizzi As List(Of String) = RecapitiConRiservaSync(recapiti, CategoriaRecapitoIndirizzo, "")
            Dim siti As List(Of String) = RecapitiConRiservaSync(recapiti, CategoriaRecapitoSito, "")

            Return New Dictionary(Of String, Object) From {
                {"id", identificativo},
                {"modificato", FormatoDataSync(dataLocale)},
                {"eliminato", False},
                {"nome", ValoreColonna(riga, "Nome")},
                {"cognome", ValoreColonna(riga, "Cognome")},
                {"azienda", ValoreColonna(riga, "Azienda")},
                {"note", ValoreColonna(riga, "Note")},
                {"preferito", ValoreColonna(riga, "Preferito") = "1"},
                {"categoria", NomeCategoria(ValoreColonna(riga, "ChiaveCategoria"))},
                {"telefoni", telefoni},
                {"cellulari", cellulari},
                {"email", email},
                {"indirizzi", indirizzi},
                {"siti", siti},
                {"uidServer", If(ValoreColonna(riga, "UidServer").Length > 0, ValoreColonna(riga, "UidServer"), ValoreColonna(riga, "UidServerFile"))}
            }
        Catch ex As Exception
            ScriviLog("CLSContatti.CostruisciVoceContattoSync (" & If(identificativo, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function RecapitiConRiservaSync(recapiti As Dictionary(Of String, List(Of String)), categoria As String, valoreRiserva As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If recapiti IsNot Nothing AndAlso recapiti.ContainsKey(categoria) AndAlso recapiti(categoria) IsNot Nothing Then
                risultato.AddRange(recapiti(categoria))
            End If
            If risultato.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(valoreRiserva) Then risultato.Add(valoreRiserva.Trim())
        Catch ex As Exception
            ScriviLog("CLSContatti.RecapitiConRiservaSync (" & If(categoria, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ChiaveCategoriaPerNomeSync(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim cercato As String = nome.Trim()
            Dim categorie As DataTable = OttieniCategorie()
            If categorie IsNot Nothing Then
                For Each riga As DataRow In categorie.Rows
                    If String.Equals(ValoreColonna(riga, "Nome").Trim(), cercato, StringComparison.OrdinalIgnoreCase) Then Return ValoreColonna(riga, "ChiaveWG")
                Next
            End If
            Return AggiungiCategoria(cercato)
        Catch ex As Exception
            ScriviLog("CLSContatti.ChiaveCategoriaPerNomeSync (" & If(nome, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function VoceEliminataSync(identificativo As String, data As DateTime) As Dictionary(Of String, Object)
        Try
            Return New Dictionary(Of String, Object) From {
                {"id", identificativo},
                {"modificato", FormatoDataSync(data)},
                {"eliminato", True}
            }
        Catch ex As Exception
            ScriviLog("CLSContatti.VoceEliminataSync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function IndicizzaVociSync(voci As List(Of Dictionary(Of String, Object))) As Dictionary(Of String, Dictionary(Of String, Object))
        Dim risultato As New Dictionary(Of String, Dictionary(Of String, Object))(StringComparer.Ordinal)
        Try
            If voci Is Nothing Then Return risultato
            For Each voce As Dictionary(Of String, Object) In voci
                Try
                    If voce Is Nothing Then Continue For
                    Dim identificativo As String = TestoVoceSync(voce, "id")
                    If identificativo.Length = 0 Then Continue For
                    Dim esistente As Dictionary(Of String, Object) = Nothing
                    If risultato.TryGetValue(identificativo, esistente) Then
                        If LeggiDataSync(TestoVoceSync(voce, "modificato")) <= LeggiDataSync(TestoVoceSync(esistente, "modificato")) Then Continue For
                    End If
                    risultato(identificativo) = voce
                Catch exVoce As Exception
                    ScriviLog("CLSContatti.IndicizzaVociSync voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.IndicizzaVociSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function TestoVoceSync(voce As Dictionary(Of String, Object), chiave As String) As String
        Try
            If voce Is Nothing Then Return ""
            Dim valore As Object = Nothing
            If Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
            Return Convert.ToString(valore, CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            ScriviLog("CLSContatti.TestoVoceSync (" & If(chiave, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function BooleanoVoceSync(voce As Dictionary(Of String, Object), chiave As String) As Boolean
        Try
            If voce Is Nothing Then Return False
            Dim valore As Object = Nothing
            If Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return False
            If TypeOf valore Is Boolean Then Return CBool(valore)
            Dim testo As String = Convert.ToString(valore, CultureInfo.InvariantCulture).Trim().ToLowerInvariant()
            Return testo = "true" OrElse testo = "1" OrElse testo = "si" OrElse testo = "sì"
        Catch ex As Exception
            ScriviLog("CLSContatti.BooleanoVoceSync (" & If(chiave, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function ListaVoceSync(voce As Dictionary(Of String, Object), chiave As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If voce Is Nothing Then Return risultato
            Dim valore As Object = Nothing
            If Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return risultato
            If TypeOf valore Is String Then
                Dim testoSingolo As String = CStr(valore).Trim()
                If testoSingolo.Length > 0 Then risultato.Add(testoSingolo)
                Return risultato
            End If
            Dim elenco As IEnumerable = TryCast(valore, IEnumerable)
            If elenco Is Nothing Then Return risultato
            For Each elemento As Object In elenco
                If elemento Is Nothing Then Continue For
                Dim testo As String = Convert.ToString(elemento, CultureInfo.InvariantCulture).Trim()
                If testo.Length > 0 Then risultato.Add(testo)
            Next
        Catch ex As Exception
            ScriviLog("CLSContatti.ListaVoceSync (" & If(chiave, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function LeggiDataSync(testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim risultato As DateTime
            If DateTime.TryParse(testo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            ScriviLog("CLSContatti.LeggiDataSync (" & If(testo, "") & "): " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Private Shared Function FormatoDataSync(data As DateTime) As String
        Try
            Dim utc As DateTime = data
            If utc.Kind = DateTimeKind.Local Then utc = utc.ToUniversalTime()
            Return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        Catch ex As Exception
            ScriviLog("CLSContatti.FormatoDataSync: " & ex.ToString())
            Return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        End Try
    End Function

End Class