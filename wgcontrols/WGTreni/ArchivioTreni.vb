Imports System.Data.SQLite
Imports System.Globalization
Imports wgcontrols
Imports WGDb

' ============================================================================================
' ArchivioTreni - ricerche recenti, corse preferite e corse recenti
' ============================================================================================
'
' Stanno in un database SQLite nella cartella dati di WinGabriel, come il resto: dati\treni\treni.db. Così si sincronizzano con
' tutto il resto. Ogni tabella ha la colonna di chiave di sincronizzazione ChiaveWG, creata e riempita come negli altri moduli
' (WGDatabase.AssicuraColonnaChiave e GeneraChiaveUnivocaPerTabella).
'
'   RicercheRecenti   le ultime ricerche fatte (da, a, mezzi, tipo di treni, passeggeri), le più recenti per prime;
'   CorsePreferite    le corse che l'utente ha segnato: partenza, arrivo, ora di partenza e nome del treno o del pullman;
'   CorseRecenti      le ultime corse aperte nel dettaglio.
' Le più recenti sono al massimo trenta per tabella. I dati dei giocatori e delle chat non c'entrano: qui ci sono solo stazioni e orari.
' ============================================================================================

Friend Class RicercaSalvata
    Public Id As String = ""
    Public Da As String = ""
    Public CodiceDa As String = ""
    Public A As String = ""
    Public CodiceA As String = ""
    Public Mezzi As Integer
    Public Tipo As Integer
    Public Passeggeri As Integer = 1
    Public UltimoUso As DateTime = DateTime.Now

    Public Function Descrizione() As String
        Dim testoMezzi As String = If(Me.Mezzi = 1, "solo treni", If(Me.Mezzi = 2, "solo pullman", "treni e pullman"))
        Dim testoTipo As String
        Select Case Me.Tipo
            Case 1 : testoTipo = "solo alta velocità"
            Case 2 : testoTipo = "senza regionali"
            Case 3 : testoTipo = "solo regionali"
            Case Else : testoTipo = "tutti i treni"
        End Select
        Return "Da " & Da & " a " & A & ", " & testoMezzi & ", " & testoTipo & ", " & Passeggeri & If(Passeggeri = 1, " passeggero", " passeggeri")
    End Function
End Class

Friend Class CorsaSalvata
    Public Id As String = ""
    Public Da As String = ""
    Public CodiceDa As String = ""
    Public A As String = ""
    Public CodiceA As String = ""
    ''' <summary>L'ora di partenza, nel formato HH:mm.</summary>
    Public Ora As String = ""
    Public Nome As String = ""
    Public Operatore As String = ""
    Public UltimoUso As DateTime = DateTime.Now

    Public Function Descrizione() As String
        Dim d As String = If(Nome <> "", Nome & ", ", "") & "da " & Da & " a " & A & ", parte alle " & Ora
        If Operatore <> "" AndAlso Nome.IndexOf(Operatore, StringComparison.OrdinalIgnoreCase) < 0 Then d &= ", " & Operatore
        Return Char.ToUpper(d(0)) & d.Substring(1)
    End Function
End Class

Friend NotInheritable Class ArchivioTreni

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _inizializzato As Boolean

    Private Const MassimoRecenti As Integer = 30
    Private Const TabellaRicerche As String = "RicercheRecenti"
    Private Const TabellaPreferite As String = "CorsePreferite"
    Private Const TabellaRecenti As String = "CorseRecenti"

    Private Sub New()
    End Sub

    Private Shared Function Apri() As SQLiteConnection
        Dim percorso As String = WGDatabase.PercorsoFileDati("treni", "treni.db")
        Dim conn As SQLiteConnection = WGDatabase.ApriConnessione(percorso)
        If conn Is Nothing Then
            conn = New SQLiteConnection("Data Source=" & percorso & ";Version=3;")
            conn.Open()
        End If
        Inizializza(conn)
        Return conn
    End Function

    Private Shared Sub Inizializza(conn As SQLiteConnection)
        SyncLock _blocco
            If _inizializzato Then Return
            Try
                Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS " & TabellaRicerche & " (Id TEXT PRIMARY KEY, Da TEXT NOT NULL, CodiceDa TEXT NOT NULL DEFAULT '', A TEXT NOT NULL, CodiceA TEXT NOT NULL DEFAULT '', Mezzi INTEGER NOT NULL DEFAULT 0, Tipo INTEGER NOT NULL DEFAULT 0, Passeggeri INTEGER NOT NULL DEFAULT 1, UltimoUso TEXT NOT NULL);", conn)
                    cmd.ExecuteNonQuery()
                End Using
                For Each tabella As String In New String() {TabellaPreferite, TabellaRecenti}
                    Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS " & tabella & " (Id TEXT PRIMARY KEY, Da TEXT NOT NULL, CodiceDa TEXT NOT NULL DEFAULT '', A TEXT NOT NULL, CodiceA TEXT NOT NULL DEFAULT '', Ora TEXT NOT NULL DEFAULT '', Nome TEXT NOT NULL DEFAULT '', Operatore TEXT NOT NULL DEFAULT '', UltimoUso TEXT NOT NULL);", conn)
                        cmd.ExecuteNonQuery()
                    End Using
                Next
                For Each tabella As String In New String() {TabellaRicerche, TabellaPreferite, TabellaRecenti}
                    Try
                        WGDatabase.AssicuraColonnaChiave(conn, tabella)
                    Catch exChiave As Exception
                        _log.ScriviLog("ArchivioTreni.Inizializza chiave " & tabella & ": " & exChiave.Message)
                    End Try
                Next
                _inizializzato = True
            Catch ex As Exception
                _log.ScriviLog("ArchivioTreni.Inizializza: " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
        End SyncLock
    End Sub

    ''' <summary>La chiave di sincronizzazione della riga se esiste già, altrimenti una nuova.</summary>
    Private Shared Function ChiavePer(conn As SQLiteConnection, tabella As String, id As String) As String
        Try
            Using cmd As New SQLiteCommand("SELECT ChiaveWG FROM " & tabella & " WHERE Id = @Id;", conn)
                cmd.Parameters.AddWithValue("@Id", id)
                Dim r As Object = cmd.ExecuteScalar()
                If r IsNot Nothing AndAlso Not IsDBNull(r) AndAlso r.ToString() <> "" Then Return r.ToString()
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.ChiavePer: " & ex.Message)
        End Try
        Return WGDatabase.GeneraChiaveUnivocaPerTabella(conn, tabella)
    End Function

    Private Shared Sub PotaRecenti(conn As SQLiteConnection, tabella As String)
        Using cmd As New SQLiteCommand("DELETE FROM " & tabella & " WHERE rowid NOT IN (SELECT rowid FROM " & tabella & " ORDER BY UltimoUso DESC LIMIT " & MassimoRecenti & ");", conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Function Testo(d As DateTime) As String
        Return d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function Data(s As Object) As DateTime
        Dim d As DateTime
        If DateTime.TryParse(Convert.ToString(s), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, d) Then Return d.ToLocalTime()
        Return DateTime.Now
    End Function

#Region "Ricerche recenti"

    Public Shared Function IdRicerca(r As RicercaSalvata) As String
        Return (r.CodiceDa & "|" & r.Da & "|" & r.CodiceA & "|" & r.A & "|" & r.Mezzi & "|" & r.Tipo & "|" & r.Passeggeri).ToLowerInvariant()
    End Function

    Public Shared Sub SalvaRicerca(r As RicercaSalvata)
        Try
            r.Id = IdRicerca(r)
            r.UltimoUso = DateTime.Now
            Using conn As SQLiteConnection = Apri()
                Dim chiave As String = ChiavePer(conn, TabellaRicerche, r.Id)
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO " & TabellaRicerche & " (Id, Da, CodiceDa, A, CodiceA, Mezzi, Tipo, Passeggeri, UltimoUso, ChiaveWG) VALUES (@Id, @Da, @CodiceDa, @A, @CodiceA, @Mezzi, @Tipo, @Passeggeri, @UltimoUso, @Chiave);", conn)
                    cmd.Parameters.AddWithValue("@Id", r.Id)
                    cmd.Parameters.AddWithValue("@Da", r.Da)
                    cmd.Parameters.AddWithValue("@CodiceDa", r.CodiceDa)
                    cmd.Parameters.AddWithValue("@A", r.A)
                    cmd.Parameters.AddWithValue("@CodiceA", r.CodiceA)
                    cmd.Parameters.AddWithValue("@Mezzi", r.Mezzi)
                    cmd.Parameters.AddWithValue("@Tipo", r.Tipo)
                    cmd.Parameters.AddWithValue("@Passeggeri", r.Passeggeri)
                    cmd.Parameters.AddWithValue("@UltimoUso", Testo(r.UltimoUso))
                    cmd.Parameters.AddWithValue("@Chiave", chiave)
                    cmd.ExecuteNonQuery()
                End Using
                PotaRecenti(conn, TabellaRicerche)
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.SalvaRicerca: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function LeggiRicerche() As List(Of RicercaSalvata)
        Dim lista As New List(Of RicercaSalvata)()
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("SELECT Id, Da, CodiceDa, A, CodiceA, Mezzi, Tipo, Passeggeri, UltimoUso FROM " & TabellaRicerche & " ORDER BY UltimoUso DESC;", conn)
                    Using rdr As SQLiteDataReader = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim r As New RicercaSalvata()
                            r.Id = rdr.GetString(0)
                            r.Da = rdr.GetString(1)
                            r.CodiceDa = rdr.GetString(2)
                            r.A = rdr.GetString(3)
                            r.CodiceA = rdr.GetString(4)
                            r.Mezzi = rdr.GetInt32(5)
                            r.Tipo = rdr.GetInt32(6)
                            r.Passeggeri = rdr.GetInt32(7)
                            r.UltimoUso = Data(rdr.GetValue(8))
                            lista.Add(r)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.LeggiRicerche: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Shared Sub EliminaRicerca(id As String)
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("DELETE FROM " & TabellaRicerche & " WHERE Id = @Id;", conn)
                    cmd.Parameters.AddWithValue("@Id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.EliminaRicerca: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SvuotaRicerche()
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("DELETE FROM " & TabellaRicerche & ";", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.SvuotaRicerche: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Corse preferite e recenti"

    Public Shared Function IdCorsa(c As CorsaSalvata) As String
        Return (c.CodiceDa & "|" & c.Da & "|" & c.CodiceA & "|" & c.A & "|" & c.Ora & "|" & c.Nome).ToLowerInvariant()
    End Function

    Private Shared Function Tabella(preferite As Boolean) As String
        Return If(preferite, TabellaPreferite, TabellaRecenti)
    End Function

    Public Shared Sub SalvaCorsa(preferita As Boolean, c As CorsaSalvata)
        Try
            c.Id = IdCorsa(c)
            c.UltimoUso = DateTime.Now
            Dim nomeTabella As String = Tabella(preferita)
            Using conn As SQLiteConnection = Apri()
                Dim chiave As String = ChiavePer(conn, nomeTabella, c.Id)
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO " & nomeTabella & " (Id, Da, CodiceDa, A, CodiceA, Ora, Nome, Operatore, UltimoUso, ChiaveWG) VALUES (@Id, @Da, @CodiceDa, @A, @CodiceA, @Ora, @Nome, @Operatore, @UltimoUso, @Chiave);", conn)
                    cmd.Parameters.AddWithValue("@Id", c.Id)
                    cmd.Parameters.AddWithValue("@Da", c.Da)
                    cmd.Parameters.AddWithValue("@CodiceDa", c.CodiceDa)
                    cmd.Parameters.AddWithValue("@A", c.A)
                    cmd.Parameters.AddWithValue("@CodiceA", c.CodiceA)
                    cmd.Parameters.AddWithValue("@Ora", c.Ora)
                    cmd.Parameters.AddWithValue("@Nome", c.Nome)
                    cmd.Parameters.AddWithValue("@Operatore", c.Operatore)
                    cmd.Parameters.AddWithValue("@UltimoUso", Testo(c.UltimoUso))
                    cmd.Parameters.AddWithValue("@Chiave", chiave)
                    cmd.ExecuteNonQuery()
                End Using
                If Not preferita Then PotaRecenti(conn, nomeTabella)
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.SalvaCorsa: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function LeggiCorse(preferite As Boolean) As List(Of CorsaSalvata)
        Dim lista As New List(Of CorsaSalvata)()
        Try
            Using conn As SQLiteConnection = Apri()
                Dim ordine As String = If(preferite, "Da ASC, A ASC, Ora ASC", "UltimoUso DESC")
                Using cmd As New SQLiteCommand("SELECT Id, Da, CodiceDa, A, CodiceA, Ora, Nome, Operatore, UltimoUso FROM " & Tabella(preferite) & " ORDER BY " & ordine & ";", conn)
                    Using rdr As SQLiteDataReader = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim c As New CorsaSalvata()
                            c.Id = rdr.GetString(0)
                            c.Da = rdr.GetString(1)
                            c.CodiceDa = rdr.GetString(2)
                            c.A = rdr.GetString(3)
                            c.CodiceA = rdr.GetString(4)
                            c.Ora = rdr.GetString(5)
                            c.Nome = rdr.GetString(6)
                            c.Operatore = rdr.GetString(7)
                            c.UltimoUso = Data(rdr.GetValue(8))
                            lista.Add(c)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.LeggiCorse: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Shared Sub EliminaCorsa(preferita As Boolean, id As String)
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("DELETE FROM " & Tabella(preferita) & " WHERE Id = @Id;", conn)
                    cmd.Parameters.AddWithValue("@Id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.EliminaCorsa: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SvuotaCorse(preferite As Boolean)
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("DELETE FROM " & Tabella(preferite) & ";", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.SvuotaCorse: " & ex.Message)
        End Try
    End Sub

    Public Shared Function EPreferita(id As String) As Boolean
        Try
            Using conn As SQLiteConnection = Apri()
                Using cmd As New SQLiteCommand("SELECT 1 FROM " & TabellaPreferite & " WHERE Id = @Id;", conn)
                    cmd.Parameters.AddWithValue("@Id", id)
                    Return cmd.ExecuteScalar() IsNot Nothing
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("ArchivioTreni.EPreferita: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

End Class
