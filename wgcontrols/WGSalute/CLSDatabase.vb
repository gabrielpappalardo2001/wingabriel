Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb

''' <summary>
''' Database indipendente per i dati personali dell'utente legati ai farmaci:
''' preferiti, farmaci consultati di recente e ricerche recenti.
''' Non dipende da CLSFarmacie: memorizza solo Aic6 e Nome, la risoluzione
''' del farmaco completo spetta a chi la usa (tipicamente MenuFarmacia).
''' </summary>
Public Class CLSDatabase

#Region "Tipi pubblici"

    Public Class RiferimentoFarmaco
        Public Property Aic6 As String
        Public Property Nome As String
        Public Property Data As String
    End Class

#End Region

#Region "Singleton e campi"

    Private Shared _istanza As CLSDatabase
    Private Shared ReadOnly _lockIstanza As New Object()

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _cartellaDati As String
    Private ReadOnly _dbPath As String
    Private ReadOnly _connString As String
    Private ReadOnly _lockDb As New Object()

    Private _conn As SQLiteConnection

    Private Const MASSIMO_RECENTI As Integer = 30
    Private Const MASSIMO_RICERCHE_RECENTI As Integer = 20

    Private Sub New()
        Try
            _cartellaDati = RisolviCartellaFarmacie()
            Directory.CreateDirectory(_cartellaDati)
            _dbPath = Path.Combine(_cartellaDati, "preferenze.db")
            _connString = "Data Source=" & _dbPath & ";Version=3;Journal Mode=WAL;Pooling=True;"
            InizializzaDatabase()
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.New: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function RisolviCartellaFarmacie() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("farmacie")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(Application.StartupPath, "dati", "farmacie")
        Catch
            Return "dati_farmacie"
        End Try
    End Function

    Public Shared Function GetInstance() As CLSDatabase
        Try
            SyncLock _lockIstanza
                If _istanza Is Nothing Then
                    _istanza = New CLSDatabase()
                End If
                Return _istanza
            End SyncLock
        Catch
            Return _istanza
        End Try
    End Function

#End Region

#Region "Database"

    Private Function Connessione() As SQLiteConnection
        Try
            If _conn Is Nothing OrElse _conn.State <> ConnectionState.Open Then
                _conn = New SQLiteConnection(_connString)
                _conn.Open()
                ApplicaPragma(_conn)
            End If
            Return _conn
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.Connessione: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub ApplicaPragma(conn As SQLiteConnection)
        Try
            If conn Is Nothing Then Return
            Using cmd As New SQLiteCommand(conn)
                cmd.CommandText = "PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;PRAGMA temp_store=MEMORY;PRAGMA busy_timeout=8000;"
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.ApplicaPragma: " & ex.ToString())
        End Try
    End Sub

    Private Sub InizializzaDatabase()
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return

                Dim sql As New StringBuilder()
                sql.Append("CREATE TABLE IF NOT EXISTS Preferiti (")
                sql.Append("Aic6 TEXT PRIMARY KEY,")
                sql.Append("Nome TEXT,")
                sql.Append("DataAggiunta TEXT);")

                sql.Append("CREATE TABLE IF NOT EXISTS Recenti (")
                sql.Append("Aic6 TEXT PRIMARY KEY,")
                sql.Append("Nome TEXT,")
                sql.Append("DataVisita TEXT);")

                sql.Append("CREATE TABLE IF NOT EXISTS RicercheRecenti (")
                sql.Append("Testo TEXT PRIMARY KEY COLLATE NOCASE,")
                sql.Append("DataRicerca TEXT);")

                Using cmd As New SQLiteCommand(sql.ToString(), conn)
                    cmd.ExecuteNonQuery()
                End Using

                Dim sqlIdx As New StringBuilder()
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_recenti_data ON Recenti(DataVisita);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_preferiti_data ON Preferiti(DataAggiunta);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_ricerche_data ON RicercheRecenti(DataRicerca);")
                Using cmdIdx As New SQLiteCommand(sqlIdx.ToString(), conn)
                    cmdIdx.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "Preferiti")
                    WGDatabase.AssicuraColonnaChiave(conn, "Recenti")
                    WGDatabase.AssicuraColonnaChiave(conn, "RicercheRecenti")
                Catch exChiave As Exception
                    _logger.ScriviLog("CLSDatabase.InizializzaDatabase AssicuraColonnaChiave: " & exChiave.ToString())
                End Try
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.InizializzaDatabase: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Utilita"

    Private Function EstraiAic6(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim solo As New StringBuilder()
            For Each c In valore
                If Char.IsDigit(c) Then solo.Append(c)
            Next
            Dim s As String = solo.ToString()
            If s.Length = 0 Then Return ""
            If s.Length >= 6 Then Return s.Substring(0, 6)
            Return s.PadLeft(6, "0"c)
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.EstraiAic6: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Preferiti"

    Public Function EPreferito(aic6 As String) As Boolean
        Try
            Dim codice As String = EstraiAic6(aic6)
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return False
                Using cmd As New SQLiteCommand("SELECT 1 FROM Preferiti WHERE Aic6=@a LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    Return cmd.ExecuteScalar() IsNot Nothing
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.EPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AggiungiPreferito(aic6 As String, nome As String)
        Try
            Dim codice As String = EstraiAic6(aic6)
            If codice.Length <> 6 Then Return
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM Preferiti WHERE Aic6=@a", conn)
                    cmdChiave.Parameters.AddWithValue("@a", codice)
                    Dim risultatoChiave As Object = cmdChiave.ExecuteScalar()
                    If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                        chiaveWGEsistente = Convert.ToString(risultatoChiave)
                    End If
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Preferiti")
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Preferiti (Aic6, Nome, DataAggiunta, ChiaveWG) VALUES (@a,@n,@d,@c)", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@n", If(nome, ""))
                    cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    cmd.Parameters.AddWithValue("@c", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.AggiungiPreferito: " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviPreferito(aic6 As String)
        Try
            Dim codice As String = EstraiAic6(aic6)
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Using cmd As New SQLiteCommand("DELETE FROM Preferiti WHERE Aic6=@a", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.RimuoviPreferito: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniPreferiti() As List(Of RiferimentoFarmaco)
        Dim elenco As New List(Of RiferimentoFarmaco)
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT Aic6, Nome, DataAggiunta FROM Preferiti ORDER BY DataAggiunta DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim r As New RiferimentoFarmaco()
                                r.Aic6 = rdr(0).ToString()
                                r.Nome = If(IsDBNull(rdr(1)), "", rdr(1).ToString())
                                r.Data = If(IsDBNull(rdr(2)), "", rdr(2).ToString())
                                elenco.Add(r)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.OttieniPreferiti: " & ex.ToString())
        End Try
        Return elenco
    End Function

#End Region

#Region "Recenti"

    Public Sub RegistraRecente(aic6 As String, nome As String)
        Try
            Dim codice As String = EstraiAic6(aic6)
            If codice.Length <> 6 Then Return
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM Recenti WHERE Aic6=@a", conn)
                    cmdChiave.Parameters.AddWithValue("@a", codice)
                    Dim risultatoChiave As Object = cmdChiave.ExecuteScalar()
                    If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                        chiaveWGEsistente = Convert.ToString(risultatoChiave)
                    End If
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Recenti")
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Recenti (Aic6, Nome, DataVisita, ChiaveWG) VALUES (@a,@n,@d,@c)", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@n", If(nome, ""))
                    cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    cmd.Parameters.AddWithValue("@c", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmdPulisci As New SQLiteCommand(
                    "DELETE FROM Recenti WHERE Aic6 NOT IN (SELECT Aic6 FROM Recenti ORDER BY DataVisita DESC LIMIT " & MASSIMO_RECENTI.ToString() & ")", conn)
                    cmdPulisci.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.RegistraRecente: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniRecenti(Optional massimo As Integer = MASSIMO_RECENTI) As List(Of RiferimentoFarmaco)
        Dim elenco As New List(Of RiferimentoFarmaco)
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT Aic6, Nome, DataVisita FROM Recenti ORDER BY DataVisita DESC LIMIT @m", conn)
                    cmd.Parameters.AddWithValue("@m", massimo)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim r As New RiferimentoFarmaco()
                                r.Aic6 = rdr(0).ToString()
                                r.Nome = If(IsDBNull(rdr(1)), "", rdr(1).ToString())
                                r.Data = If(IsDBNull(rdr(2)), "", rdr(2).ToString())
                                elenco.Add(r)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.OttieniRecenti: " & ex.ToString())
        End Try
        Return elenco
    End Function

#End Region

#Region "Ricerche recenti"

    Public Sub RegistraRicerca(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim testoPulito As String = testo.Trim()
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM RicercheRecenti WHERE Testo=@t", conn)
                    cmdChiave.Parameters.AddWithValue("@t", testoPulito)
                    Dim risultatoChiave As Object = cmdChiave.ExecuteScalar()
                    If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                        chiaveWGEsistente = Convert.ToString(risultatoChiave)
                    End If
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "RicercheRecenti")
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO RicercheRecenti (Testo, DataRicerca, ChiaveWG) VALUES (@t,@d,@c)", conn)
                    cmd.Parameters.AddWithValue("@t", testoPulito)
                    cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    cmd.Parameters.AddWithValue("@c", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmdPulisci As New SQLiteCommand(
                    "DELETE FROM RicercheRecenti WHERE Testo NOT IN (SELECT Testo FROM RicercheRecenti ORDER BY DataRicerca DESC LIMIT " & MASSIMO_RICERCHE_RECENTI.ToString() & ")", conn)
                    cmdPulisci.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.RegistraRicerca: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniRicercheRecenti(Optional massimo As Integer = MASSIMO_RICERCHE_RECENTI) As List(Of String)
        Dim elenco As New List(Of String)
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT Testo FROM RicercheRecenti ORDER BY DataRicerca DESC LIMIT @m", conn)
                    cmd.Parameters.AddWithValue("@m", massimo)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            elenco.Add(rdr(0).ToString())
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.OttieniRicercheRecenti: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Public Sub CancellaRicercheRecenti()
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Using cmd As New SQLiteCommand("DELETE FROM RicercheRecenti", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.CancellaRicercheRecenti: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Chiusura"

    Public Sub Chiudi()
        Try
            SyncLock _lockDb
                If _conn IsNot Nothing Then
                    Try
                        If _conn.State = ConnectionState.Open Then _conn.Close()
                    Catch
                    End Try
                    Try
                        _conn.Dispose()
                    Catch
                    End Try
                    _conn = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSDatabase.Chiudi: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class