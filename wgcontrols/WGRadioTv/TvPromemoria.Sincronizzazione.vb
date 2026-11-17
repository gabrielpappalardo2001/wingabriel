Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports wgcontrols

' La sincronizzazione tra computer dei promemoria e dei programmi seguiti. Stesso formato delle altre sezioni del file di sincronizzazione
' (id, modificato, eliminato), così le stesse voci le leggono anche gli altri programmi (Linux Easy per le radio):
'   promemoriaRadio, promemoriaTv   id = "tipo|canale|inizio", dove inizio è l'ora UTC (aaaa-mm-ggThh:mm:ss)
'   seguitiRadio, seguitiTv         id = "tipo|canale|titolo", dove titolo è il titolo senza accenti, spazi e segni
' Il canale delle radio è l'identificativo di Radio Browser (lo stesso su ogni computer).
Partial Public NotInheritable Class TvPromemoria

    Public Const SezionePromemoriaRadio As String = "promemoriaRadio"
    Public Const SezionePromemoriaTv As String = "promemoriaTv"
    Public Const SezioneSeguitiRadio As String = "seguitiRadio"
    Public Const SezioneSeguitiTv As String = "seguitiTv"

    Private Const GIORNI_CONSERVAZIONE_ELIMINATI As Integer = 180
    Private Const FORMATO_DATA_SYNC As String = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"

    Private Class VoceSync
        Public Property IdSync As String = ""
        Public Property Data As DateTime = DateTime.MinValue
        Public Property Eliminata As Boolean = False
        Public Property RigaId As Long = 0
        Public Property Campi As New Dictionary(Of String, Object)(StringComparer.Ordinal)
    End Class

    Public Shared Function NomeSezione(tipo As String, seguiti As Boolean) As String
        Dim radio As Boolean = String.Equals(tipo, TIPO_RADIO, StringComparison.OrdinalIgnoreCase)
        If seguiti Then Return If(radio, SezioneSeguitiRadio, SezioneSeguitiTv)
        Return If(radio, SezionePromemoriaRadio, SezionePromemoriaTv)
    End Function

    Private Shared Function AdessoSync() As String
        Return DateTime.UtcNow.ToString(FORMATO_DATA_SYNC, CultureInfo.InvariantCulture)
    End Function

    Private Shared Function LeggiDataSync(testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim risultato As DateTime
            If DateTime.TryParse(testo, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
        Catch
        End Try
        Return DateTime.MinValue
    End Function

    Private Shared Function TestoSync(campi As Dictionary(Of String, Object), chiave As String) As String
        Dim valore As Object = Nothing
        If campi Is Nothing OrElse Not campi.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
        Return Convert.ToString(valore, CultureInfo.InvariantCulture).Trim()
    End Function

    Private Shared Function InizioInTesto(inizioUtc As String) As String
        Dim d As DateTime = LeggiDataSync(inizioUtc)
        If d = DateTime.MinValue Then Return ""
        Return d.ToString("yyyy-MM-dd'T'HH:mm:ss'.000Z'", CultureInfo.InvariantCulture)
    End Function

    ' Registra che un promemoria o un programma seguito è stato tolto, perché gli altri computer lo tolgano a loro volta.
    Private Shared Sub RegistraEliminazione(connessione As SQLiteConnection, sezione As String, idSync As String)
        Try
            Using comando As New SQLiteCommand("INSERT OR REPLACE INTO SincEliminati (Sezione, IdSync, Data) VALUES (@sezione, @id, @data)", connessione)
                comando.Parameters.AddWithValue("@sezione", sezione)
                comando.Parameters.AddWithValue("@id", idSync)
                comando.Parameters.AddWithValue("@data", AdessoSync())
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.RegistraEliminazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub TogliEliminazione(connessione As SQLiteConnection, sezione As String, idSync As String)
        Try
            Using comando As New SQLiteCommand("DELETE FROM SincEliminati WHERE Sezione = @sezione AND IdSync = @id", connessione)
                comando.Parameters.AddWithValue("@sezione", sezione)
                comando.Parameters.AddWithValue("@id", idSync)
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.TogliEliminazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiediSincronizzazione()
        Try
            WGSincronizzazioneCloud.RichiediSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.ChiediSincronizzazione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Unisce i promemoria o i programmi seguiti del tipo dato (radio o tv) con le voci arrivate dal file di sincronizzazione: per ogni voce vince
    ''' la modifica più recente, e una voce tolta resta tra le eliminate per qualche mese. Restituisce le voci da riscrivere nel file, Nothing in caso di errore.
    ''' </summary>
    Public Shared Function UnisciSezione(tipo As String, seguiti As Boolean, vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        SyncLock _lock
            Try
                If Not PreparaArchivio() Then Return Nothing
                If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
                Dim sezioneSync As String = NomeSezione(tipo, seguiti)
                Dim limite As DateTime = DateTime.UtcNow.AddDays(-GIORNI_CONSERVAZIONE_ELIMINATI)

                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Dim locali As Dictionary(Of String, VoceSync) = LeggiLocaliSync(connessione, tipo, seguiti)
                    Dim eliminate As Dictionary(Of String, VoceSync) = LeggiEliminateSync(connessione, sezioneSync)
                    Dim remote As Dictionary(Of String, VoceSync) = IndicizzaRemoteSync(vociRemote)

                    Dim identificativi As New HashSet(Of String)(StringComparer.Ordinal)
                    For Each chiave As String In locali.Keys
                        identificativi.Add(chiave)
                    Next
                    For Each chiave As String In eliminate.Keys
                        identificativi.Add(chiave)
                    Next
                    For Each chiave As String In remote.Keys
                        identificativi.Add(chiave)
                    Next

                    Dim finali As New List(Of VoceSync)()
                    Using transazione As SQLiteTransaction = connessione.BeginTransaction()
                        Try
                            For Each identificativo As String In identificativi
                                Try
                                    Dim locale As VoceSync = Nothing
                                    If Not locali.TryGetValue(identificativo, locale) Then eliminate.TryGetValue(identificativo, locale)
                                    Dim remota As VoceSync = Nothing
                                    remote.TryGetValue(identificativo, remota)

                                    If remota IsNot Nothing AndAlso (locale Is Nothing OrElse remota.Data > locale.Data) Then
                                        ApplicaRemotaSync(connessione, transazione, tipo, seguiti, sezioneSync, remota, locali)
                                        finali.Add(remota)
                                    ElseIf locale IsNot Nothing Then
                                        finali.Add(locale)
                                    End If
                                Catch exVoce As Exception
                                    _logger.ScriviLog("TvPromemoria.UnisciSezione voce (" & identificativo & "): " & exVoce.ToString())
                                End Try
                            Next

                            Using comando As New SQLiteCommand("DELETE FROM SincEliminati WHERE Sezione = @sezione AND Data < @limite", connessione, transazione)
                                comando.Parameters.AddWithValue("@sezione", sezioneSync)
                                comando.Parameters.AddWithValue("@limite", limite.ToString(FORMATO_DATA_SYNC, CultureInfo.InvariantCulture))
                                comando.ExecuteNonQuery()
                            End Using
                            transazione.Commit()
                        Catch exTransazione As Exception
                            _logger.ScriviLog("TvPromemoria.UnisciSezione transazione: " & exTransazione.ToString())
                            Try
                                transazione.Rollback()
                            Catch exRollback As Exception
                                _logger.ScriviLog("TvPromemoria.UnisciSezione rollback: " & exRollback.Message)
                            End Try
                            Return Nothing
                        End Try
                    End Using

                    Dim risultato As New List(Of Dictionary(Of String, Object))()
                    For Each voce As VoceSync In finali
                        If voce.Eliminata AndAlso voce.Data < limite Then Continue For
                        Dim campi As New Dictionary(Of String, Object)(voce.Campi, StringComparer.Ordinal)
                        campi("id") = voce.IdSync
                        campi("modificato") = voce.Data.ToString(FORMATO_DATA_SYNC, CultureInfo.InvariantCulture)
                        campi("eliminato") = voce.Eliminata
                        risultato.Add(campi)
                    Next
                    Return risultato
                End Using
            Catch ex As Exception
                _logger.ScriviLog("TvPromemoria.UnisciSezione (" & If(tipo, "") & "): " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Private Shared Function LeggiLocaliSync(connessione As SQLiteConnection, tipo As String, seguiti As Boolean) As Dictionary(Of String, VoceSync)
        Dim risultato As New Dictionary(Of String, VoceSync)(StringComparer.Ordinal)
        Dim senzaData As New List(Of Long)()
        Dim sql As String = If(seguiti,
            "SELECT Id, CanaleId, NomeCanale, TvgId, Titolo, Chiave, Modificato FROM Seguiti WHERE Tipo = @tipo",
            "SELECT Id, CanaleId, NomeCanale, Titolo, InizioUtc, FineUtc, Modificato FROM Promemoria WHERE Tipo = @tipo")
        Using comando As New SQLiteCommand(sql, connessione)
            comando.Parameters.AddWithValue("@tipo", tipo)
            Using lettore As SQLiteDataReader = comando.ExecuteReader()
                While lettore.Read()
                    Dim voce As New VoceSync()
                    voce.RigaId = Convert.ToInt64(lettore("Id"))
                    Dim canale As String = Convert.ToString(lettore("CanaleId"))
                    voce.Campi("tipo") = tipo
                    voce.Campi("canale") = canale
                    voce.Campi("nome") = If(IsDBNull(lettore("NomeCanale")), "", Convert.ToString(lettore("NomeCanale")))
                    voce.Campi("titolo") = If(IsDBNull(lettore("Titolo")), "", Convert.ToString(lettore("Titolo")))
                    If seguiti Then
                        Dim chiave As String = Convert.ToString(lettore("Chiave"))
                        voce.IdSync = tipo & "|" & canale & "|" & chiave
                        voce.Campi("tvgId") = If(IsDBNull(lettore("TvgId")), "", Convert.ToString(lettore("TvgId")))
                        voce.Campi("chiave") = chiave
                    Else
                        Dim inizio As String = Convert.ToString(lettore("InizioUtc"))
                        voce.IdSync = tipo & "|" & canale & "|" & inizio
                        voce.Campi("inizio") = InizioInTesto(inizio)
                        If Not IsDBNull(lettore("FineUtc")) Then voce.Campi("fine") = InizioInTesto(Convert.ToString(lettore("FineUtc")))
                    End If
                    voce.Data = If(IsDBNull(lettore("Modificato")), DateTime.MinValue, LeggiDataSync(Convert.ToString(lettore("Modificato"))))
                    If voce.Data = DateTime.MinValue Then senzaData.Add(voce.RigaId)
                    risultato(voce.IdSync) = voce
                End While
            End Using
        End Using

        ' le righe di prima della sincronizzazione non hanno la data di modifica: si danno adesso, una volta sola
        If senzaData.Count > 0 Then
            Dim adesso As String = AdessoSync()
            For Each riga As Long In senzaData
                Using comando As New SQLiteCommand("UPDATE " & If(seguiti, "Seguiti", "Promemoria") & " SET Modificato = @data WHERE Id = @id", connessione)
                    comando.Parameters.AddWithValue("@data", adesso)
                    comando.Parameters.AddWithValue("@id", riga)
                    comando.ExecuteNonQuery()
                End Using
            Next
            For Each voce As VoceSync In risultato.Values
                If voce.Data = DateTime.MinValue Then voce.Data = LeggiDataSync(adesso)
            Next
        End If
        Return risultato
    End Function

    Private Shared Function LeggiEliminateSync(connessione As SQLiteConnection, nomeSezione As String) As Dictionary(Of String, VoceSync)
        Dim risultato As New Dictionary(Of String, VoceSync)(StringComparer.Ordinal)
        Using comando As New SQLiteCommand("SELECT IdSync, Data FROM SincEliminati WHERE Sezione = @sezione", connessione)
            comando.Parameters.AddWithValue("@sezione", nomeSezione)
            Using lettore As SQLiteDataReader = comando.ExecuteReader()
                While lettore.Read()
                    Dim voce As New VoceSync()
                    voce.IdSync = Convert.ToString(lettore("IdSync"))
                    voce.Data = LeggiDataSync(Convert.ToString(lettore("Data")))
                    voce.Eliminata = True
                    If voce.IdSync.Length > 0 Then risultato(voce.IdSync) = voce
                End While
            End Using
        End Using
        Return risultato
    End Function

    Private Shared Function IndicizzaRemoteSync(voci As List(Of Dictionary(Of String, Object))) As Dictionary(Of String, VoceSync)
        Dim risultato As New Dictionary(Of String, VoceSync)(StringComparer.Ordinal)
        For Each campi As Dictionary(Of String, Object) In voci
            Try
                If campi Is Nothing Then Continue For
                Dim voce As New VoceSync()
                voce.IdSync = TestoSync(campi, "id")
                If voce.IdSync.Length = 0 Then Continue For
                voce.Data = LeggiDataSync(TestoSync(campi, "modificato"))
                Dim eliminato As String = TestoSync(campi, "eliminato").ToLowerInvariant()
                voce.Eliminata = (eliminato = "true" OrElse eliminato = "1")
                For Each coppia As KeyValuePair(Of String, Object) In campi
                    If coppia.Key <> "id" AndAlso coppia.Key <> "modificato" AndAlso coppia.Key <> "eliminato" Then voce.Campi(coppia.Key) = coppia.Value
                Next
                Dim esistente As VoceSync = Nothing
                If risultato.TryGetValue(voce.IdSync, esistente) AndAlso esistente.Data >= voce.Data Then Continue For
                risultato(voce.IdSync) = voce
            Catch ex As Exception
                _logger.ScriviLog("TvPromemoria.IndicizzaRemoteSync: " & ex.Message)
            End Try
        Next
        Return risultato
    End Function

    Private Shared Sub ApplicaRemotaSync(connessione As SQLiteConnection, transazione As SQLiteTransaction, tipo As String, seguiti As Boolean, nomeSezione As String, remota As VoceSync, locali As Dictionary(Of String, VoceSync))
        Dim data As String = remota.Data.ToString(FORMATO_DATA_SYNC, CultureInfo.InvariantCulture)
        Dim locale As VoceSync = Nothing
        locali.TryGetValue(remota.IdSync, locale)
        Dim tabella As String = If(seguiti, "Seguiti", "Promemoria")

        If remota.Eliminata Then
            If locale IsNot Nothing Then
                Using comando As New SQLiteCommand("DELETE FROM " & tabella & " WHERE Id = @id", connessione, transazione)
                    comando.Parameters.AddWithValue("@id", locale.RigaId)
                    comando.ExecuteNonQuery()
                End Using
            End If
            Using comando As New SQLiteCommand("INSERT OR REPLACE INTO SincEliminati (Sezione, IdSync, Data) VALUES (@sezione, @id, @data)", connessione, transazione)
                comando.Parameters.AddWithValue("@sezione", nomeSezione)
                comando.Parameters.AddWithValue("@id", remota.IdSync)
                comando.Parameters.AddWithValue("@data", data)
                comando.ExecuteNonQuery()
            End Using
            ' un promemoria tolto altrove (o già avvisato) non deve essere rimesso da un programma seguito
            If Not seguiti Then
                Using comando As New SQLiteCommand("INSERT OR IGNORE INTO SeguitiMessi (Chiave, InizioUtc) VALUES (@chiave, @inizio)", connessione, transazione)
                    comando.Parameters.AddWithValue("@chiave", remota.IdSync)
                    Dim parti As String() = remota.IdSync.Split("|"c)
                    comando.Parameters.AddWithValue("@inizio", If(parti.Length >= 3, parti(2), ""))
                    comando.ExecuteNonQuery()
                End Using
            End If
            Return
        End If

        Dim canale As String = TestoSync(remota.Campi, "canale")
        If canale.Length = 0 Then Return
        Dim nome As String = TestoSync(remota.Campi, "nome")
        Dim titolo As String = TestoSync(remota.Campi, "titolo")

        If seguiti Then
            Dim chiave As String = TestoSync(remota.Campi, "chiave")
            If chiave.Length = 0 Then chiave = ChiaveTitolo(titolo)
            If chiave.Length = 0 Then Return
            If locale IsNot Nothing Then
                Using comando As New SQLiteCommand("UPDATE Seguiti SET NomeCanale = @nome, TvgId = @tvg, Titolo = @titolo, Modificato = @data WHERE Id = @id", connessione, transazione)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@tvg", TestoSync(remota.Campi, "tvgId"))
                    comando.Parameters.AddWithValue("@titolo", titolo)
                    comando.Parameters.AddWithValue("@data", data)
                    comando.Parameters.AddWithValue("@id", locale.RigaId)
                    comando.ExecuteNonQuery()
                End Using
            Else
                Using comando As New SQLiteCommand("INSERT INTO Seguiti (Tipo, CanaleId, NomeCanale, TvgId, Titolo, Chiave, Modificato) VALUES (@tipo, @canale, @nome, @tvg, @titolo, @chiave, @data)", connessione, transazione)
                    comando.Parameters.AddWithValue("@tipo", tipo)
                    comando.Parameters.AddWithValue("@canale", canale)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@tvg", TestoSync(remota.Campi, "tvgId"))
                    comando.Parameters.AddWithValue("@titolo", titolo)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    comando.Parameters.AddWithValue("@data", data)
                    comando.ExecuteNonQuery()
                End Using
            End If
        Else
            Dim inizio As DateTime = LeggiDataSync(TestoSync(remota.Campi, "inizio"))
            If inizio = DateTime.MinValue OrElse titolo.Length = 0 Then Return
            Dim fine As DateTime = LeggiDataSync(TestoSync(remota.Campi, "fine"))
            ' un promemoria già passato non serve più: non si mette (resta però nel file, per gli altri)
            Dim limiteRitardo As DateTime = DateTime.UtcNow.AddMinutes(-MINUTI_MASSIMI_DI_RITARDO)
            If inizio < limiteRitardo OrElse (fine > DateTime.MinValue AndAlso fine <= DateTime.UtcNow) Then Return

            If locale IsNot Nothing Then
                Using comando As New SQLiteCommand("UPDATE Promemoria SET NomeCanale = @nome, Titolo = @titolo, Modificato = @data WHERE Id = @id", connessione, transazione)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@titolo", titolo)
                    comando.Parameters.AddWithValue("@data", data)
                    comando.Parameters.AddWithValue("@id", locale.RigaId)
                    comando.ExecuteNonQuery()
                End Using
            Else
                Using comando As New SQLiteCommand("INSERT INTO Promemoria (Tipo, CanaleId, NomeCanale, Titolo, InizioUtc, FineUtc, Modificato) VALUES (@tipo, @canale, @nome, @titolo, @inizio, @fine, @data)", connessione, transazione)
                    comando.Parameters.AddWithValue("@tipo", tipo)
                    comando.Parameters.AddWithValue("@canale", canale)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@titolo", titolo)
                    comando.Parameters.AddWithValue("@inizio", Chiave(inizio))
                    comando.Parameters.AddWithValue("@fine", If(fine > inizio, CObj(Chiave(fine)), DBNull.Value))
                    comando.Parameters.AddWithValue("@data", data)
                    comando.ExecuteNonQuery()
                End Using
            End If
        End If

        Using comando As New SQLiteCommand("DELETE FROM SincEliminati WHERE Sezione = @sezione AND IdSync = @id", connessione, transazione)
            comando.Parameters.AddWithValue("@sezione", nomeSezione)
            comando.Parameters.AddWithValue("@id", remota.IdSync)
            comando.ExecuteNonQuery()
        End Using
    End Sub

End Class
