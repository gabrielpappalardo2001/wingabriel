Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports WGDb

' I compleanni della rubrica nell'agenda: per ogni contatto con un compleanno c'è un appuntamento annuale, creato e tenuto aggiornato da qui.
' Il legame tra il contatto e il suo appuntamento sta nella tabella CompleanniContatti, così il testo dell'appuntamento resta libero (e può essere
' cifrato in un'agenda protetta). Un appuntamento che l'utente ha cancellato a mano non viene ricreato finché il compleanno resta uguale.
Partial Public Class CLSAgenda

    ''' <summary>Un compleanno da mettere in agenda.</summary>
    Public Class VoceCompleannoAgenda
        Public Property Chiave As String = ""
        Public Property NomeCompleto As String = ""
        Public Property Giorno As Integer
        Public Property Mese As Integer
        ''' <summary>L'anno di nascita, 0 se non si conosce.</summary>
        Public Property Anno As Integer
    End Class

    Private Class LegameCompleanno
        Public IdAppuntamento As Long
        Public Impronta As String = ""
    End Class

    Private Const OraCompleanno As String = "09:00"
    Private Const OraFineCompleanno As String = "09:30"

    Private Sub AssicuraTabellaCompleanni()
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS CompleanniContatti (ChiaveContatto TEXT PRIMARY KEY, IdAppuntamento INTEGER NOT NULL, Impronta TEXT);", conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Function LeggiLegamiCompleanni() As Dictionary(Of String, LegameCompleanno)
        Dim legami As New Dictionary(Of String, LegameCompleanno)(StringComparer.OrdinalIgnoreCase)
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("SELECT ChiaveContatto, IdAppuntamento, Impronta FROM CompleanniContatti;", conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                Using lettore As SQLiteDataReader = cmd.ExecuteReader()
                    While lettore.Read()
                        legami(lettore.GetString(0)) = New LegameCompleanno() With {
                            .IdAppuntamento = lettore.GetInt64(1),
                            .Impronta = If(lettore.IsDBNull(2), "", lettore.GetString(2))}
                    End While
                End Using
            End Using
        End Using
        Return legami
    End Function

    Private Sub SalvaLegameCompleanno(chiaveContatto As String, idAppuntamento As Long, impronta As String)
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO CompleanniContatti (ChiaveContatto, IdAppuntamento, Impronta) VALUES (@c, @i, @h);", conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.Parameters.AddWithValue("@c", chiaveContatto)
                cmd.Parameters.AddWithValue("@i", idAppuntamento)
                cmd.Parameters.AddWithValue("@h", impronta)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Sub EliminaLegameCompleanno(chiaveContatto As String)
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("DELETE FROM CompleanniContatti WHERE ChiaveContatto = @c;", conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.Parameters.AddWithValue("@c", chiaveContatto)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Function AppuntamentoEsiste(id As Long) As Boolean
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Appuntamenti WHERE Id = @id;", conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.Parameters.AddWithValue("@id", id)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        End Using
    End Function

    ' La prossima volta che cade il compleanno, da oggi in poi; il 29 febbraio negli anni non bisestili diventa il 28.
    Private Shared Function ProssimoCompleanno(giorno As Integer, mese As Integer) As DateTime
        Dim oggi As DateTime = DateTime.Today
        For anno As Integer = oggi.Year To oggi.Year + 1
            Dim g As Integer = Math.Min(giorno, DateTime.DaysInMonth(anno, mese))
            Dim data As New DateTime(anno, mese, g)
            If data >= oggi Then Return data
        Next
        Return New DateTime(oggi.Year + 1, mese, Math.Min(giorno, DateTime.DaysInMonth(oggi.Year + 1, mese)))
    End Function

    ' Crea l'appuntamento annuale e ne restituisce l'identificativo (0 se non si è potuto, per esempio con l'agenda protetta e bloccata).
    Private Function CreaAppuntamentoCompleanno(oggetto As String, note As String, preavviso As Integer, data As String, categoria As String) As Long
        Dim idCreato As Long = 0
        Dim dataDb As String = ConvertiDataDB(data)
        If String.IsNullOrWhiteSpace(dataDb) Then Return 0

        EseguiConRetry(Function()
                           Using conn As SQLiteConnection = ApriConnessione()
                               Dim agendaScelta As String = AgendaPerNuoviAppuntamenti()
                               If Not AgendaPuoRicevereTesto(agendaScelta) Then Return False
                               Using cmd As New SQLiteCommand("INSERT INTO Appuntamenti (Oggetto, Nome, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Stato, Note, Preavviso, ChiaveWG, DaSincronizzare, ChiaveAgenda) VALUES (@Oggetto, NULL, @Data, @Data, @OraInizio, @OraFine, @Categoria, @Frequenza, 'attivo', @Note, @Preavviso, @ChiaveWG, 1, @ChiaveAgenda)", conn)
                                   cmd.CommandTimeout = CommandTimeoutSeconds
                                   cmd.Parameters.AddWithValue("@ChiaveAgenda", agendaScelta)
                                   cmd.Parameters.AddWithValue("@Oggetto", TestoDaSalvare(oggetto, agendaScelta))
                                   cmd.Parameters.AddWithValue("@ChiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Appuntamenti"))
                                   cmd.Parameters.AddWithValue("@Data", dataDb)
                                   cmd.Parameters.AddWithValue("@OraInizio", OraCompleanno)
                                   cmd.Parameters.AddWithValue("@OraFine", OraFineCompleanno)
                                   cmd.Parameters.AddWithValue("@Categoria", If(String.IsNullOrWhiteSpace(categoria), DBNull.Value, CObj(categoria)))
                                   cmd.Parameters.AddWithValue("@Frequenza", CType(Frequenza.Annuale, Integer))
                                   cmd.Parameters.AddWithValue("@Note", If(String.IsNullOrWhiteSpace(note), DBNull.Value, CObj(TestoDaSalvare(note, agendaScelta))))
                                   cmd.Parameters.AddWithValue("@Preavviso", preavviso)
                                   If cmd.ExecuteNonQuery() <= 0 Then Return False
                               End Using
                               Using cmdId As New SQLiteCommand("SELECT last_insert_rowid();", conn)
                                   idCreato = Convert.ToInt64(cmdId.ExecuteScalar())
                               End Using
                               Return idCreato > 0
                           End Using
                       End Function, "CreaAppuntamentoCompleanno")
        If idCreato > 0 Then SegnalaScritturaPerCheckpoint()
        Return idCreato
    End Function

    ''' <summary>
    ''' Allinea l'agenda ai compleanni della rubrica: crea gli appuntamenti che mancano, aggiorna quelli cambiati, toglie quelli di contatti spariti
    ''' o senza più compleanno. daConservare sono i contatti che hanno un compleanno ma che ora non si possono leggere (rubrica protetta e
    ''' bloccata): i loro appuntamenti non si toccano. Con un elenco vuoto e nessun contatto da conservare si tolgono tutti i compleanni generati.
    ''' preavvisoMinuti è l'avviso prima dell'appuntamento (0 il giorno stesso, 1440 il giorno prima). Vero se è andato tutto bene.
    ''' </summary>
    Public Function SincronizzaCompleanni(voci As List(Of VoceCompleannoAgenda), daConservare As HashSet(Of String), preavvisoMinuti As Integer, categoria As String) As Boolean
        Try
            If voci Is Nothing Then voci = New List(Of VoceCompleannoAgenda)()
            If daConservare Is Nothing Then daConservare = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If preavvisoMinuti < 0 Then preavvisoMinuti = 0

            AssicuraTabellaCompleanni()
            Dim legami As Dictionary(Of String, LegameCompleanno) = LeggiLegamiCompleanni()
            If voci.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(categoria) Then AggiungiCategoria(categoria)

            Dim presenti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim tuttoBene As Boolean = True

            For Each voce As VoceCompleannoAgenda In voci
                Try
                    presenti.Add(voce.Chiave)
                    Dim oggetto As String = "Compleanno di " & voce.NomeCompleto
                    Dim note As String = "Compleanno preso dalla rubrica."
                    If voce.Anno > 0 Then note &= " Anno di nascita: " & voce.Anno.ToString(CultureInfo.InvariantCulture) & "."
                    Dim impronta As String = String.Join("|", New String() {voce.NomeCompleto, voce.Giorno.ToString(CultureInfo.InvariantCulture), voce.Mese.ToString(CultureInfo.InvariantCulture), voce.Anno.ToString(CultureInfo.InvariantCulture), preavvisoMinuti.ToString(CultureInfo.InvariantCulture), If(categoria, "")})
                    Dim data As String = ProssimoCompleanno(voce.Giorno, voce.Mese).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)

                    Dim legame As LegameCompleanno = Nothing
                    If legami.TryGetValue(voce.Chiave, legame) Then
                        Dim esiste As Boolean = AppuntamentoEsiste(legame.IdAppuntamento)
                        If String.Equals(legame.Impronta, impronta, StringComparison.Ordinal) Then Continue For

                        If esiste Then
                            If ModificaAppuntamento(legame.IdAppuntamento.ToString(CultureInfo.InvariantCulture), oggetto, "", OraCompleanno, OraFineCompleanno, note, preavvisoMinuti, Frequenza.Annuale, data, categoria, data) Then
                                SalvaLegameCompleanno(voce.Chiave, legame.IdAppuntamento, impronta)
                            Else
                                tuttoBene = False
                            End If
                            Continue For
                        End If
                    End If

                    Dim nuovoId As Long = CreaAppuntamentoCompleanno(oggetto, note, preavvisoMinuti, data, categoria)
                    If nuovoId > 0 Then
                        SalvaLegameCompleanno(voce.Chiave, nuovoId, impronta)
                    Else
                        tuttoBene = False
                    End If
                Catch exVoce As Exception
                    tuttoBene = False
                    log.ScriviLog("CLSAgenda.SincronizzaCompleanni voce - " & exVoce.Message)
                End Try
            Next

            For Each coppia As KeyValuePair(Of String, LegameCompleanno) In legami
                Try
                    If presenti.Contains(coppia.Key) OrElse daConservare.Contains(coppia.Key) Then Continue For
                    If AppuntamentoEsiste(coppia.Value.IdAppuntamento) Then EliminaAppuntamento(coppia.Value.IdAppuntamento.ToString(CultureInfo.InvariantCulture))
                    EliminaLegameCompleanno(coppia.Key)
                Catch exTolto As Exception
                    tuttoBene = False
                    log.ScriviLog("CLSAgenda.SincronizzaCompleanni rimozione - " & exTolto.Message)
                End Try
            Next

            Return tuttoBene
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SincronizzaCompleanni - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

End Class
