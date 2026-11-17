Imports System.Data.SQLite
Imports System.IO
Imports System.Collections.Generic
Imports WGDb

Public Class WGWordSqlite
    Private _dbPath As String
    Private _connectionString As String
    Private logger As New Logger()

    Public Sub New()
        Try
            Dim baseDir As String = RisolviCartellaWord()
            If Not Directory.Exists(baseDir) Then
                Directory.CreateDirectory(baseDir)
            End If
            _dbPath = Path.Combine(baseDir, "recovery.sqlite")
            _connectionString = "Data Source=" & _dbPath & ";Version=3;"
            InizializzaDb()
        Catch ex As Exception
            logger.ScriviLog("WGWordSqlite New Error: " & ex.Message)
        End Try
    End Sub

    Private Shared Function RisolviCartellaWord() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("word")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "word")
        Catch
            Return "dati_word"
        End Try
    End Function

    Private Sub InizializzaDb()
        Try
            If Not File.Exists(_dbPath) Then
                SQLiteConnection.CreateFile(_dbPath)
            End If
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Recupero (Id VARCHAR(255) PRIMARY KEY, Contenuto TEXT, DataUltimaModifica DATETIME)", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            PulisciVecchi()
        Catch ex As Exception
            logger.ScriviLog("InizializzaDb Error: " & ex.Message)
        End Try
    End Sub

    Public Sub Salva(id As String, contenuto As String)
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Recupero (Id, Contenuto, DataUltimaModifica) VALUES (@id, @cont, @data)", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.Parameters.AddWithValue("@cont", contenuto)
                    cmd.Parameters.AddWithValue("@data", DateTime.Now)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("Salva Error: " & ex.Message)
        End Try
    End Sub

    Public Sub Elimina(id As String)
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM Recupero WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("Elimina Error: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniTutti() As Dictionary(Of String, DateTime)
        Dim dict As New Dictionary(Of String, DateTime)()
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Id, DataUltimaModifica FROM Recupero", conn)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            dict.Add(reader("Id").ToString(), Convert.ToDateTime(reader("DataUltimaModifica")))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("OttieniTutti Error: " & ex.Message)
        End Try
        Return dict
    End Function

    Public Function OttieniContenuto(id As String) As String
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Contenuto FROM Recupero WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    Dim res = cmd.ExecuteScalar()
                    If res IsNot Nothing Then
                        Return res.ToString()
                    End If
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("OttieniContenuto Error: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Function CiSonoTesti() As Boolean
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Recupero", conn)
                    Dim count As Integer = Convert.ToInt32(cmd.ExecuteScalar())
                    Return count > 0
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("CiSonoTesti Error: " & ex.Message)
        End Try
        Return False
    End Function

    Private Sub PulisciVecchi()
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM Recupero WHERE DataUltimaModifica < @data", conn)
                    cmd.Parameters.AddWithValue("@data", DateTime.Now.AddDays(-30))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("PulisciVecchi Error: " & ex.Message)
        End Try
    End Sub
End Class