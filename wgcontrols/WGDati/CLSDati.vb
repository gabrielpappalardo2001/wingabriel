Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Windows.Forms
Imports System.Linq
Imports System.Data.SQLite
Imports System.Collections.Generic
Imports wgcontrols
Imports WGDb
Imports System.Runtime.Remoting.Messaging

' Gestione dei dati riservati cifrati con password, configurazioni protette e migrazione a formati di sicurezza avanzati.
Public Class Valore
    Public Nome As String
    Public Valore As String
    Public ChiaveWG As String = ""
End Class

Public Class Dato
    Public Id As Integer
    Public Nome As String
    Public Tipo As String = "Standard"
    Public ChiaveWG As String = ""
    Public Valori As New List(Of Valore)
End Class

Public Class elencodato
    Public Id As Integer
    Public Nome As String
    Public Tipo As String
    Public ChiaveWG As String = ""
End Class

Public Class CLSDati
    Private ReadOnly dbPath As String
    Private connectionString As String
    Private ReadOnly _logger As New Logger()
    Private Const Iterations As Integer = 100000
    Private Const KeySize As Integer = 32
    ' Formato 2: chiave e verificatore derivati con la libreria nativa (PBKDF2-SHA256), dati cifrati con AES-GCM nativo.
    ' Il verificatore salvato non e piu la chiave. Il formato precedente (PBKDF2-SHA1 + AES-CBC) si converte alla prima
    ' apertura con la password giusta.
    Private Const IterazioniFormato2 As Integer = 200000
    Private _formatoNuovo As Integer = -1
    Private _conversioneFallita As Boolean = False

    Private _cachedPassword As String = String.Empty
    Private _cachedKey As Byte() = Nothing
    Private _cachedHash As Byte() = Nothing

    Public Sub New()
        Try
            dbPath = RisolviPercorsoDatiRiservati()
            connectionString = $"Data Source={dbPath};Version=3;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Pooling=True;BusyTimeout=8000;"
            AssicuraCartelleEsiste()
            InizializzaDatabase()
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}CLSDati.New - Errore: {ex.Message}{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RisolviPercorsoDatiRiservati() As String
        Dim percorso As String = WGDatabase.PercorsoFileDati("riservati", "dati.db")
        Try

            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch ex As Exception
            Dim Log As New Logger()
            Log.ScriviLog(ex.Message & ex.StackTrace)
        End Try
        Return percorso
    End Function

    Private Sub AssicuraCartelleEsiste()
        Try
            Dim dbDir As String = Path.GetDirectoryName(dbPath)
            If Not String.IsNullOrWhiteSpace(dbDir) AndAlso Not Directory.Exists(dbDir) Then Directory.CreateDirectory(dbDir)
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}AssicuraCartelleEsiste - Errore: {ex.Message}")
        End Try
    End Sub

    Private Function ApriConnessioneDati() As SQLiteConnection
        Try
            Dim conn As SQLiteConnection = WGDatabase.ApriConnessione(dbPath, "Version=3;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Pooling=True;BusyTimeout=8000;")
            If conn IsNot Nothing Then
                ' Senza questo pragma ON DELETE CASCADE sui valori non scatta e restano righe orfane.
                WGDatabase.EseguiPragma(conn, "PRAGMA foreign_keys=ON;")
                Return conn
            End If
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ApriConnessioneDati - Errore: {ex.Message}")
        End Try
        Try
            Dim connFallback As New SQLiteConnection(connectionString)
            connFallback.Open()
            WGDatabase.EseguiPragma(connFallback, "PRAGMA foreign_keys=ON;")
            WGDatabase.EseguiPragma(connFallback, "PRAGMA busy_timeout=8000;")
            Return connFallback

        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Sub InizializzaDatabase()
        Try
            If Not File.Exists(dbPath) Then SQLiteConnection.CreateFile(dbPath)
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Dati (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome BLOB NOT NULL, Tipo BLOB);"
                    cmd.ExecuteNonQuery()

                    Dim esisteTipo As Boolean = False
                    cmd.CommandText = "PRAGMA table_info(Dati)"
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            If reader("name").ToString() = "Tipo" Then
                                esisteTipo = True
                                Exit While
                            End If
                        End While
                    End Using

                    If Not esisteTipo Then
                        cmd.CommandText = "ALTER TABLE Dati ADD COLUMN Tipo BLOB"
                        cmd.ExecuteNonQuery()
                    End If

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Valori (Id INTEGER PRIMARY KEY AUTOINCREMENT, DatoId INTEGER NOT NULL, Nome BLOB NOT NULL, Valore BLOB NOT NULL, FOREIGN KEY(DatoId) REFERENCES Dati(Id) ON DELETE CASCADE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Configurazione (Chiave TEXT PRIMARY KEY, Valore BLOB);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_DatoId ON Valori(DatoId);"
                    cmd.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "Dati")
                    WGDatabase.AssicuraColonnaChiave(conn, "Valori")
                Catch exChiave As Exception
                    _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}InizializzaDatabase chiave - Errore: {exChiave.Message}")
                End Try
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}InizializzaDatabase - Errore: {ex.Message}")
        End Try
    End Sub

    Public Function ImpostaPassword(nuovaPassword As String) As Boolean
        Try
            If Not VerificaSicurezzaPassword(nuovaPassword) Then
                Return False
            End If

            Dim salt As Byte() = GeneraSalt()
            _cachedPassword = String.Empty
            _cachedKey = Nothing
            _cachedHash = Nothing
            Dim nuovoFormato As Boolean = WGCifraturaNativa.Funzionante
            If Not nuovoFormato Then _logger.ScriviLog("CLSDati.ImpostaPassword: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), si usa il sistema precedente.")
            _formatoNuovo = If(nuovoFormato, 1, 0)
            Dim hash As Byte() = CalcolaHashPassword(nuovaPassword, salt)

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Try
                        SalvaConfigurazione(conn, "Salt", salt)
                        SalvaConfigurazione(conn, "Hash", hash)
                        If nuovoFormato Then SalvaConfigurazione(conn, "Formato", New Byte() {50})
                        trans.Commit()
                        Return True
                    Catch ex As Exception
                        trans.Rollback()
                        _formatoNuovo = -1
                        Throw
                    End Try
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ImpostaPassword - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function ModificaPassword(vecchiaPassword As String, nuovaPassword As String) As Boolean
        Try
            If Not VerificaPasswordCorretta(vecchiaPassword) Then
                Return False
            End If
            If Not VerificaSicurezzaPassword(nuovaPassword) Then
                Return False
            End If

            Dim tuttiDati As List(Of Dato) = Nothing
            Dim vecchioSalt As Byte() = LeggiConfigurazione("Salt")

            Using conn As SQLiteConnection = ApriConnessioneDati()
                tuttiDati = CaricaTuttiDatiInterno(conn, DerivaChiave(vecchiaPassword, vecchioSalt))
            End Using

            Dim nuovoSalt As Byte() = GeneraSalt()
            _cachedPassword = String.Empty
            _cachedKey = Nothing
            _cachedHash = Nothing
            Dim nuovoHash As Byte() = CalcolaHashPassword(nuovaPassword, nuovoSalt)
            Dim nuovaChiave As Byte() = DerivaChiave(nuovaPassword, nuovoSalt)

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Try
                        Using cmd As New SQLiteCommand("DELETE FROM Dati", conn)
                            cmd.ExecuteNonQuery()
                        End Using

                        SalvaConfigurazione(conn, "Salt", nuovoSalt)
                        SalvaConfigurazione(conn, "Hash", nuovoHash)

                        For Each d As Dato In tuttiDati
                            InserisciDatoInterno(conn, d, nuovaChiave)
                        Next
                        trans.Commit()
                        Return True
                    Catch exTrans As Exception
                        trans.Rollback()
                        Throw exTrans
                    End Try
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ModificaPassword - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function PasswordImpostata() As Boolean
        Try
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Configurazione WHERE Chiave = 'Hash'", conn)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}PasswordImpostata - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function VerificaPasswordCorretta(pwd As String) As Boolean
        Try
            Dim storedSalt As Byte() = LeggiConfigurazione("Salt")
            Dim storedHash As Byte() = LeggiConfigurazione("Hash")

            If storedSalt Is Nothing OrElse storedHash Is Nothing Then Return False

            Dim computedHash As Byte() = CalcolaHashPassword(pwd, storedSalt)
            Dim corretta As Boolean = storedHash.Length = computedHash.Length
            Dim differenza As Integer = 0
            If corretta Then
                For i As Integer = 0 To storedHash.Length - 1
                    differenza = differenza Or (storedHash(i) Xor computedHash(i))
                Next
                corretta = differenza = 0
            End If
            ' alla prima apertura con la password giusta i dati del formato precedente passano alla libreria nativa
            If corretta AndAlso Not UsaFormatoNuovo() Then MigraAlFormatoNuovo(pwd)
            Return corretta
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}VerificaPasswordCorretta - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function AggiungiDato(dato As Dato, pwd As String) As Boolean
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return False
            If dato Is Nothing Then Return False

            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Try
                        InserisciDatoInterno(conn, dato, key)
                        trans.Commit()
                        Return True
                    Catch ex As Exception
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}AggiungiDato - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub InserisciDatoInterno(conn As SQLiteConnection, dato As Dato, key As Byte())
        Dim idDato As Long
        Dim chiaveDato As String = dato.ChiaveWG
        If String.IsNullOrWhiteSpace(chiaveDato) Then
            Try
                chiaveDato = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Dati")
            Catch exChiave As Exception
                _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}InserisciDatoInterno chiave dato - Errore: {exChiave.Message}")
                chiaveDato = WGDatabase.GeneraChiave()
            End Try
        End If

        Using cmd As New SQLiteCommand("INSERT INTO Dati (Nome, Tipo, ChiaveWG) VALUES (@Nome, @Tipo, @ChiaveWG); SELECT last_insert_rowid();", conn)
            cmd.Parameters.AddWithValue("@Nome", Cripta(dato.Nome, key))
            cmd.Parameters.AddWithValue("@Tipo", Cripta(If(String.IsNullOrEmpty(dato.Tipo), "Standard", dato.Tipo), key))
            cmd.Parameters.AddWithValue("@ChiaveWG", chiaveDato)
            idDato = CLng(cmd.ExecuteScalar())
        End Using
        dato.ChiaveWG = chiaveDato

        If dato.Valori IsNot Nothing Then
            For Each val As Valore In dato.Valori
                Dim chiaveVal As String = val.ChiaveWG
                If String.IsNullOrWhiteSpace(chiaveVal) Then
                    Try
                        chiaveVal = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Valori")
                    Catch exChiave As Exception
                        _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}InserisciDatoInterno chiave valore - Errore: {exChiave.Message}")
                        chiaveVal = WGDatabase.GeneraChiave()
                    End Try
                End If
                Using cmdVal As New SQLiteCommand("INSERT INTO Valori (DatoId, Nome, Valore, ChiaveWG) VALUES (@DatoId, @Nome, @Valore, @ChiaveWG)", conn)
                    cmdVal.Parameters.AddWithValue("@DatoId", idDato)
                    cmdVal.Parameters.AddWithValue("@Nome", Cripta(val.Nome, key))
                    cmdVal.Parameters.AddWithValue("@Valore", Cripta(val.Valore, key))
                    cmdVal.Parameters.AddWithValue("@ChiaveWG", chiaveVal)
                    cmdVal.ExecuteNonQuery()
                End Using
                val.ChiaveWG = chiaveVal
            Next
        End If
        dato.Id = CInt(idDato)
    End Sub

    Public Function AggiungiValore(idDato As Integer, valore As Valore, pwd As String) As Boolean
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return False
            If valore Is Nothing Then Return False

            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Dim chiaveVal As String = valore.ChiaveWG
                If String.IsNullOrWhiteSpace(chiaveVal) Then
                    Try
                        chiaveVal = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Valori")
                    Catch exChiave As Exception
                        _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}AggiungiValore chiave - Errore: {exChiave.Message}")
                        chiaveVal = WGDatabase.GeneraChiave()
                    End Try
                End If
                Using cmd As New SQLiteCommand("INSERT INTO Valori (DatoId, Nome, Valore, ChiaveWG) VALUES (@DatoId, @Nome, @Valore, @ChiaveWG)", conn)
                    cmd.Parameters.AddWithValue("@DatoId", idDato)
                    cmd.Parameters.AddWithValue("@Nome", Cripta(valore.Nome, key))
                    cmd.Parameters.AddWithValue("@Valore", Cripta(valore.Valore, key))
                    cmd.Parameters.AddWithValue("@ChiaveWG", chiaveVal)
                    If cmd.ExecuteNonQuery() > 0 Then
                        valore.ChiaveWG = chiaveVal
                        Return True
                    End If
                End Using
            End Using
            Return False
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}AggiungiValore - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function ModificaDato(id As Integer, nuovoDato As Dato, pwd As String) As Boolean
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return False
            If nuovoDato Is Nothing Then Return False

            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Try
                        Using cmdCheck As New SQLiteCommand("SELECT COUNT(1) FROM Dati WHERE Id = @Id", conn, trans)
                            cmdCheck.Parameters.AddWithValue("@Id", id)
                            If Convert.ToInt32(cmdCheck.ExecuteScalar()) = 0 Then Return False
                        End Using

                        Using cmdUpdate As New SQLiteCommand("UPDATE Dati SET Nome = @Nome, Tipo = @Tipo WHERE Id = @Id", conn, trans)
                            cmdUpdate.Parameters.AddWithValue("@Nome", Cripta(nuovoDato.Nome, key))
                            cmdUpdate.Parameters.AddWithValue("@Tipo", Cripta(If(String.IsNullOrEmpty(nuovoDato.Tipo), "Standard", nuovoDato.Tipo), key))
                            cmdUpdate.Parameters.AddWithValue("@Id", id)
                            cmdUpdate.ExecuteNonQuery()
                        End Using

                        Dim chiaviValoriEsistenti As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                        Try
                            Using cmdVecchi As New SQLiteCommand("SELECT Nome, ChiaveWG FROM Valori WHERE DatoId = @DatoId", conn, trans)
                                cmdVecchi.Parameters.AddWithValue("@DatoId", id)
                                Using readerVecchi As SQLiteDataReader = cmdVecchi.ExecuteReader()
                                    While readerVecchi.Read()
                                        Try
                                            Dim nomeVecchio As String = Decripta(DirectCast(readerVecchi("Nome"), Byte()), key)
                                            Dim chiaveVecchia As String = If(IsDBNull(readerVecchi("ChiaveWG")), "", readerVecchi("ChiaveWG").ToString())
                                            If Not String.IsNullOrWhiteSpace(nomeVecchio) AndAlso Not String.IsNullOrWhiteSpace(chiaveVecchia) AndAlso Not chiaviValoriEsistenti.ContainsKey(nomeVecchio) Then
                                                chiaviValoriEsistenti.Add(nomeVecchio, chiaveVecchia)
                                            End If
                                        Catch exLettura As Exception
                                            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ModificaDato lettura chiave valore - Errore: {exLettura.Message}")
                                        End Try
                                    End While
                                End Using
                            End Using
                        Catch exVecchi As Exception
                            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ModificaDato lettura chiavi esistenti - Errore: {exVecchi.Message}")
                        End Try

                        Using cmdDel As New SQLiteCommand("DELETE FROM Valori WHERE DatoId = @DatoId", conn, trans)
                            cmdDel.Parameters.AddWithValue("@DatoId", id)
                            cmdDel.ExecuteNonQuery()
                        End Using

                        If nuovoDato.Valori IsNot Nothing Then
                            For Each val As Valore In nuovoDato.Valori
                                Dim chiaveVal As String = val.ChiaveWG
                                If String.IsNullOrWhiteSpace(chiaveVal) AndAlso Not String.IsNullOrWhiteSpace(val.Nome) AndAlso chiaviValoriEsistenti.ContainsKey(val.Nome) Then
                                    chiaveVal = chiaviValoriEsistenti(val.Nome)
                                End If
                                If String.IsNullOrWhiteSpace(chiaveVal) Then
                                    Try
                                        chiaveVal = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Valori")
                                    Catch exChiave As Exception
                                        _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ModificaDato chiave nuovo valore - Errore: {exChiave.Message}")
                                        chiaveVal = WGDatabase.GeneraChiave()
                                    End Try
                                End If
                                Using cmdIns As New SQLiteCommand("INSERT INTO Valori (DatoId, Nome, Valore, ChiaveWG) VALUES (@DatoId, @Nome, @Valore, @ChiaveWG)", conn, trans)
                                    cmdIns.Parameters.AddWithValue("@DatoId", id)
                                    cmdIns.Parameters.AddWithValue("@Nome", Cripta(val.Nome, key))
                                    cmdIns.Parameters.AddWithValue("@Valore", Cripta(val.Valore, key))
                                    cmdIns.Parameters.AddWithValue("@ChiaveWG", chiaveVal)
                                    cmdIns.ExecuteNonQuery()
                                End Using
                                val.ChiaveWG = chiaveVal
                            Next
                        End If

                        trans.Commit()
                        Return True
                    Catch ex As Exception
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ModificaDato - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function EliminaDato(id As Integer, pwd As String) As Boolean
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return False
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("DELETE FROM Dati WHERE Id = @Id", conn)
                    cmd.Parameters.AddWithValue("@Id", id)
                    If cmd.ExecuteNonQuery() > 0 Then Return True
                End Using
            End Using
            Return False
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}EliminaDato - Errore: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function VisualizzaDato(id As Integer, pwd As String) As Dato
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return Nothing
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))
            Dim d As New Dato With {.Id = id, .Valori = New List(Of Valore)}

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT Nome, Tipo, ChiaveWG FROM Dati WHERE Id = @Id", conn)
                    cmd.Parameters.AddWithValue("@Id", id)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            d.Nome = Decripta(DirectCast(reader("Nome"), Byte()), key)
                            If Not IsDBNull(reader("Tipo")) Then
                                d.Tipo = Decripta(DirectCast(reader("Tipo"), Byte()), key)
                            End If
                            Try
                                If Not IsDBNull(reader("ChiaveWG")) Then d.ChiaveWG = reader("ChiaveWG").ToString()
                            Catch
                            End Try
                        Else
                            Return Nothing
                        End If
                    End Using
                End Using

                Using cmdVal As New SQLiteCommand("SELECT Nome, Valore, ChiaveWG FROM Valori WHERE DatoId = @DatoId", conn)
                    cmdVal.Parameters.AddWithValue("@DatoId", id)
                    Using reader As SQLiteDataReader = cmdVal.ExecuteReader()
                        While reader.Read()
                            Dim v As New Valore
                            v.Nome = Decripta(DirectCast(reader("Nome"), Byte()), key)
                            v.Valore = Decripta(DirectCast(reader("Valore"), Byte()), key)
                            Try
                                If Not IsDBNull(reader("ChiaveWG")) Then v.ChiaveWG = reader("ChiaveWG").ToString()

                            Catch ex As Exception
                                _logger.ScriviLog(ex.Message & ex.StackTrace)
                            End Try

                            d.Valori.Add(v)
                        End While
                    End Using
                End Using
            End Using
            Return d
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}VisualizzaDato - Errore: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Function ElencoDati(pwd As String) As List(Of elencodato)
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return New List(Of elencodato)()
            Dim lista As New List(Of elencodato)
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT Id, Nome, Tipo, ChiaveWG FROM Dati", conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim ed As New elencodato
                            ed.Id = Convert.ToInt32(reader("Id"))
                            ed.Nome = Decripta(DirectCast(reader("Nome"), Byte()), key)
                            If Not IsDBNull(reader("Tipo")) Then
                                ed.Tipo = Decripta(DirectCast(reader("Tipo"), Byte()), key)
                            End If
                            Try
                                If Not IsDBNull(reader("ChiaveWG")) Then ed.ChiaveWG = reader("ChiaveWG").ToString()
                            Catch ex As Exception
                                _logger.ScriviLog(ex.Message & ex.StackTrace)
                            End Try

                            lista.Add(ed)
                        End While
                    End Using
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ElencoDati - Errore: {ex.Message}")
            Return New List(Of elencodato)()
        End Try
    End Function

    Public Function ElencoDatiPaginato(pwd As String, numeroPagina As Integer, elementiPerPagina As Integer) As List(Of elencodato)
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return New List(Of elencodato)()
            If numeroPagina < 1 Then numeroPagina = 1
            If elementiPerPagina < 1 Then elementiPerPagina = 100

            Dim lista As New List(Of elencodato)
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))
            Dim offset As Integer = (numeroPagina - 1) * elementiPerPagina

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT Id, Nome, Tipo, ChiaveWG FROM Dati ORDER BY Id ASC LIMIT @Limit OFFSET @Offset", conn)
                    cmd.Parameters.AddWithValue("@Limit", elementiPerPagina)
                    cmd.Parameters.AddWithValue("@Offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim ed As New elencodato
                            ed.Id = Convert.ToInt32(reader("Id"))
                            ed.Nome = Decripta(DirectCast(reader("Nome"), Byte()), key)
                            If Not IsDBNull(reader("Tipo")) Then
                                ed.Tipo = Decripta(DirectCast(reader("Tipo"), Byte()), key)
                            End If
                            Try
                                If Not IsDBNull(reader("ChiaveWG")) Then ed.ChiaveWG = reader("ChiaveWG").ToString()
                            Catch
                            End Try
                            lista.Add(ed)
                        End While
                    End Using
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ElencoDatiPaginato - Errore: {ex.Message}")
            Return New List(Of elencodato)()
        End Try
    End Function

    Public Function ContaDati(pwd As String) As Integer
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return -1
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT COUNT(Id) FROM Dati", conn)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ContaDati - Errore: {ex.Message}")
            Return -1
        End Try
    End Function

    Public Function CercaDatiPaginato(testoDaCercare As String, pwd As String, numeroPagina As Integer, elementiPerPagina As Integer) As List(Of elencodato)
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return New List(Of elencodato)()
            If String.IsNullOrWhiteSpace(testoDaCercare) Then Return New List(Of elencodato)()
            If numeroPagina < 1 Then numeroPagina = 1
            If elementiPerPagina < 1 Then elementiPerPagina = 100

            Dim lista As New List(Of elencodato)
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))

            Dim skip As Integer = (numeroPagina - 1) * elementiPerPagina
            Dim take As Integer = elementiPerPagina
            Dim matchCount As Integer = 0
            Dim addedCount As Integer = 0

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Dim query As String = "SELECT D.Id, D.Nome AS NomeDato, D.Tipo, D.ChiaveWG, V.Valore FROM Dati D LEFT JOIN Valori V ON D.Id = V.DatoId ORDER BY D.Id"

                Using cmd As New SQLiteCommand(query, conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        Dim currentId As Integer = -1
                        Dim currentName As String = ""
                        Dim currentTipo As String = ""
                        Dim currentChiave As String = ""
                        Dim isMatch As Boolean = False

                        While reader.Read()
                            Dim id As Integer = Convert.ToInt32(reader("Id"))

                            If id <> currentId Then
                                If currentId <> -1 AndAlso isMatch Then
                                    matchCount += 1
                                    If matchCount > skip Then
                                        lista.Add(New elencodato With {.Id = currentId, .Nome = currentName, .Tipo = currentTipo, .ChiaveWG = currentChiave})
                                        addedCount += 1
                                        If addedCount >= take Then Return lista
                                    End If
                                End If

                                currentId = id
                                isMatch = False
                                Dim nomeBytes = DirectCast(reader("NomeDato"), Byte())
                                currentName = Decripta(nomeBytes, key)

                                If Not IsDBNull(reader("Tipo")) Then
                                    currentTipo = Decripta(DirectCast(reader("Tipo"), Byte()), key)
                                End If

                                currentChiave = ""
                                Try
                                    If Not IsDBNull(reader("ChiaveWG")) Then currentChiave = reader("ChiaveWG").ToString()
                                Catch ex As Exception
                                    _logger.ScriviLog(ex.Message & ex.StackTrace)
                                End Try

                                If currentName.IndexOf(testoDaCercare, StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    isMatch = True
                                End If
                            End If

                            If Not isMatch AndAlso Not IsDBNull(reader("Valore")) Then
                                Dim valBytes = DirectCast(reader("Valore"), Byte())
                                Dim valDecrypted As String = Decripta(valBytes, key)
                                If valDecrypted.IndexOf(testoDaCercare, StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    isMatch = True
                                End If
                            End If
                        End While

                        If currentId <> -1 AndAlso isMatch Then
                            matchCount += 1
                            If matchCount > skip AndAlso addedCount < take Then
                                lista.Add(New elencodato With {.Id = currentId, .Nome = currentName, .Tipo = currentTipo, .ChiaveWG = currentChiave})
                            End If
                        End If
                    End Using
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}CercaDatiPaginato - Errore: {ex.Message}")
            Return New List(Of elencodato)()
        End Try
    End Function

    Public Function ContaRisultatiRicerca(testoDaCercare As String, pwd As String) As Integer
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return 0
            If String.IsNullOrWhiteSpace(testoDaCercare) Then Return 0

            Dim count As Integer = 0
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Dim query As String = "SELECT D.Id, D.Nome AS NomeDato, V.Valore FROM Dati D LEFT JOIN Valori V ON D.Id = V.DatoId ORDER BY D.Id"

                Using cmd As New SQLiteCommand(query, conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        Dim currentId As Integer = -1
                        Dim isMatch As Boolean = False

                        While reader.Read()
                            Dim id As Integer = Convert.ToInt32(reader("Id"))

                            If id <> currentId Then
                                If currentId <> -1 AndAlso isMatch Then count += 1
                                currentId = id
                                isMatch = False

                                Dim nomeBytes = DirectCast(reader("NomeDato"), Byte())
                                Dim nome As String = Decripta(nomeBytes, key)
                                If nome.IndexOf(testoDaCercare, StringComparison.OrdinalIgnoreCase) >= 0 Then isMatch = True
                            End If

                            If Not isMatch AndAlso Not IsDBNull(reader("Valore")) Then
                                Dim valBytes = DirectCast(reader("Valore"), Byte())
                                Dim val As String = Decripta(valBytes, key)
                                If val.IndexOf(testoDaCercare, StringComparison.OrdinalIgnoreCase) >= 0 Then isMatch = True
                            End If
                        End While

                        If currentId <> -1 AndAlso isMatch Then count += 1
                    End Using
                End Using
            End Using
            Return count
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}ContaRisultatiRicerca - Errore: {ex.Message}")
            Return 0
        End Try
    End Function

    Public Function VisualizzaValori(id As Integer, pwd As String) As List(Of Valore)
        Try
            If Not VerificaPasswordCorretta(pwd) Then Return New List(Of Valore)()
            Dim lista As New List(Of Valore)
            Dim key As Byte() = DerivaChiave(pwd, LeggiConfigurazione("Salt"))

            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT Nome, Valore, ChiaveWG FROM Valori WHERE DatoId = @DatoId", conn)
                    cmd.Parameters.AddWithValue("@DatoId", id)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim v As New Valore
                            v.Nome = Decripta(DirectCast(reader("Nome"), Byte()), key)
                            v.Valore = Decripta(DirectCast(reader("Valore"), Byte()), key)
                            Try
                                If Not IsDBNull(reader("ChiaveWG")) Then v.ChiaveWG = reader("ChiaveWG").ToString()
                            Catch ex As Exception
                                _logger.ScriviLog(ex.Message & ex.StackTrace)
                            End Try
                            lista.Add(v)
                        End While
                    End Using
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}VisualizzaValori - Errore: {ex.Message}")
            Return New List(Of Valore)()
        End Try
    End Function

    Private Sub SalvaConfigurazione(conn As SQLiteConnection, chiave As String, valore As Byte())
        Try
            Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Configurazione (Chiave, Valore) VALUES (@Chiave, @Valore)", conn)
                cmd.Parameters.AddWithValue("@Chiave", chiave)
                cmd.Parameters.AddWithValue("@Valore", valore)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
        End Try
    End Sub

    Private Function LeggiConfigurazione(chiave As String) As Byte()
        Try
            Using conn As SQLiteConnection = ApriConnessioneDati()
                Using cmd As New SQLiteCommand("SELECT Valore FROM Configurazione WHERE Chiave = @Chiave", conn)
                    cmd.Parameters.AddWithValue("@Chiave", chiave)
                    Dim res = cmd.ExecuteScalar()
                    If res IsNot Nothing AndAlso Not IsDBNull(res) Then
                        Return DirectCast(res, Byte())
                    End If
                    Return Nothing
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}LeggiConfigurazione - Errore: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Function CaricaTuttiDatiInterno(conn As SQLiteConnection, vecchioKey As Byte()) As List(Of Dato)
        Try
            Dim lista As New List(Of Dato)
            Using cmd As New SQLiteCommand("SELECT Id, Nome, Tipo, ChiaveWG FROM Dati", conn)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        Dim d As New Dato
                        d.Id = Convert.ToInt32(reader("Id"))
                        d.Nome = Decripta(DirectCast(reader("Nome"), Byte()), vecchioKey)
                        If Not IsDBNull(reader("Tipo")) Then
                            d.Tipo = Decripta(DirectCast(reader("Tipo"), Byte()), vecchioKey)
                        End If
                        Try
                            If Not IsDBNull(reader("ChiaveWG")) Then d.ChiaveWG = reader("ChiaveWG").ToString()
                        Catch ex As Exception
                            _logger.ScriviLog(ex.Message & ex.StackTrace)
                        End Try
                        d.Valori = New List(Of Valore)()
                        lista.Add(d)
                    End While
                End Using
            End Using

            For Each d As Dato In lista
                Using cmdVal As New SQLiteCommand("SELECT Nome, Valore, ChiaveWG FROM Valori WHERE DatoId = @DatoId", conn)
                    cmdVal.Parameters.AddWithValue("@DatoId", d.Id)
                    Using readerVal As SQLiteDataReader = cmdVal.ExecuteReader()
                        While readerVal.Read()
                            Dim v As New Valore
                            v.Nome = Decripta(DirectCast(readerVal("Nome"), Byte()), vecchioKey)
                            v.Valore = Decripta(DirectCast(readerVal("Valore"), Byte()), vecchioKey)
                            Try
                                If Not IsDBNull(readerVal("ChiaveWG")) Then v.ChiaveWG = readerVal("ChiaveWG").ToString()
                            Catch ex As Exception
                                _logger.ScriviLog(ex.Message & ex.StackTrace)
                            End Try
                            d.Valori.Add(v)
                        End While
                    End Using
                End Using
            Next
            Return lista
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function GeneraSalt() As Byte()
        Try
            Dim salt(31) As Byte
            Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
                rng.GetBytes(salt)
            End Using
            Return salt
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function UsaFormatoNuovo() As Boolean
        If _formatoNuovo >= 0 Then Return _formatoNuovo = 1
        Dim f As Byte() = LeggiConfigurazione("Formato")
        _formatoNuovo = If(f IsNot Nothing AndAlso f.Length > 0 AndAlso f(0) = 50, 1, 0)
        Return _formatoNuovo = 1
    End Function

    ' Deriva con la libreria nativa 64 byte: i primi 32 sono la chiave dei dati, gli altri 32 il verificatore della password.
    Private Sub DerivaFormatoNuovo(password As String, salt As Byte())
        Dim materiale As Byte() = WGCifraturaNativa.DerivaPbkdf2(password, salt, IterazioniFormato2, 64)
        If materiale Is Nothing Then Throw New InvalidOperationException("Derivazione della chiave non riuscita (" & WGCifraturaNativa.DettaglioNativo() & ")")
        Dim chiave(31) As Byte
        Dim verificatore(31) As Byte
        Array.Copy(materiale, 0, chiave, 0, 32)
        Array.Copy(materiale, 32, verificatore, 0, 32)
        Array.Clear(materiale, 0, materiale.Length)
        _cachedPassword = password
        _cachedKey = chiave
        _cachedHash = verificatore
    End Sub

    Private Function CalcolaHashPassword(password As String, salt As Byte()) As Byte()
        If Not UsaFormatoNuovo() Then Return CalcolaHashPasswordPrecedente(password, salt)
        Try
            If Not (password = _cachedPassword AndAlso _cachedHash IsNot Nothing) Then DerivaFormatoNuovo(password, salt)
            Return _cachedHash
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function DerivaChiave(password As String, salt As Byte()) As Byte()
        If Not UsaFormatoNuovo() Then Return DerivaChiavePrecedente(password, salt)
        Try
            If Not (password = _cachedPassword AndAlso _cachedKey IsNot Nothing) Then DerivaFormatoNuovo(password, salt)
            Return _cachedKey
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function Cripta(testoInChiaro As String, chiave As Byte()) As Byte()
        If Not UsaFormatoNuovo() Then Return CriptaPrecedente(testoInChiaro, chiave)
        Try
            If String.IsNullOrEmpty(testoInChiaro) Then Return New Byte() {}
            Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(chiave, Encoding.UTF8.GetBytes(testoInChiaro))
            If cifrato Is Nothing Then Throw New InvalidOperationException("Cifratura non riuscita (" & WGCifraturaNativa.DettaglioNativo() & ")")
            Return cifrato
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function Decripta(datiCifrati As Byte(), chiave As Byte()) As String
        If Not UsaFormatoNuovo() Then Return DecriptaPrecedente(datiCifrati, chiave)
        Try
            If datiCifrati Is Nothing OrElse datiCifrati.Length < 28 Then Return String.Empty
            Dim chiaro As Byte() = WGCifraturaNativa.DecifraConChiave(chiave, datiCifrati)
            If chiaro Is Nothing Then Return String.Empty
            Return Encoding.UTF8.GetString(chiaro)
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            Return String.Empty
        End Try
    End Function

    ' Impronta del contenuto (nomi, tipi, valori), senza id e chiavi: serve a confrontare i dati prima e dopo la conversione.
    Private Function FirmaDati(elenco As List(Of Dato)) As String
        Dim righe As New List(Of String)()
        For Each d As Dato In elenco
            Dim parti As New List(Of String)()
            If d.Valori IsNot Nothing Then
                For Each v As Valore In d.Valori
                    parti.Add(If(v.Nome, "") & "=" & If(v.Valore, ""))
                Next
            End If
            parti.Sort(StringComparer.Ordinal)
            righe.Add(If(d.Nome, "") & "|" & If(String.IsNullOrEmpty(d.Tipo), "Standard", d.Tipo) & "|" & String.Join(vbLf, parti))
        Next
        righe.Sort(StringComparer.Ordinal)
        Return String.Join(vbCrLf, righe)
    End Function

    ''' <summary>
    ''' Converte i dati dal formato precedente al formato 2 con la password appena verificata: si rilegge tutto, si ricifra con la
    ''' libreria nativa e si riscrive in una sola transazione. Se qualcosa non torna non si tocca nulla.
    ''' </summary>
    Private Sub MigraAlFormatoNuovo(pwd As String)
        Try
            If _conversioneFallita Then Return
            If Not WGCifraturaNativa.Funzionante Then
                _conversioneFallita = True
                _logger.ScriviLog("CLSDati.MigraAlFormatoNuovo: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), i dati restano nel sistema precedente.")
                Return
            End If
            Dim vecchioSalt As Byte() = LeggiConfigurazione("Salt")
            If vecchioSalt Is Nothing Then Return
            Dim chiaveVecchia As Byte() = DerivaChiave(pwd, vecchioSalt)

            Dim tutti As List(Of Dato)
            Using conn As SQLiteConnection = ApriConnessioneDati()
                tutti = CaricaTuttiDatiInterno(conn, chiaveVecchia)
            End Using
            ' La lettura del formato precedente restituisce vuoto quando non riesce: con un nome vuoto non si converte.
            For Each d As Dato In tutti
                If String.IsNullOrEmpty(d.Nome) Then
                    _conversioneFallita = True
                    _logger.ScriviLog("CLSDati.MigraAlFormatoNuovo: un dato non si legge (id " & d.Id & "), i dati restano nel sistema precedente.")
                    Return
                End If
                For Each v As Valore In d.Valori
                    If String.IsNullOrEmpty(v.Nome) Then
                        _conversioneFallita = True
                        _logger.ScriviLog("CLSDati.MigraAlFormatoNuovo: un valore non si legge (dato id " & d.Id & "), i dati restano nel sistema precedente.")
                        Return
                    End If
                Next
            Next

            Dim nuovoSalt As Byte() = GeneraSalt()
            _cachedPassword = String.Empty
            _cachedKey = Nothing
            _cachedHash = Nothing
            _formatoNuovo = 1
            Try
                Dim nuovoHash As Byte() = CalcolaHashPassword(pwd, nuovoSalt)
                Dim nuovaChiave As Byte() = DerivaChiave(pwd, nuovoSalt)
                Using conn As SQLiteConnection = ApriConnessioneDati()
                    Using trans As SQLiteTransaction = conn.BeginTransaction()
                        Try
                            Using cmd As New SQLiteCommand("DELETE FROM Dati", conn)
                                cmd.ExecuteNonQuery()
                            End Using
                            SalvaConfigurazione(conn, "Salt", nuovoSalt)
                            SalvaConfigurazione(conn, "Hash", nuovoHash)
                            SalvaConfigurazione(conn, "Formato", New Byte() {50})
                            For Each d As Dato In tutti
                                InserisciDatoInterno(conn, d, nuovaChiave)
                            Next
                            ' Prima di confermare si rilegge tutto con la chiave nuova e si confronta con gli originali:
                            ' se qualcosa non torna la transazione si annulla e i dati restano nel sistema precedente.
                            Dim riletti As List(Of Dato) = CaricaTuttiDatiInterno(conn, nuovaChiave)
                            If FirmaDati(riletti) <> FirmaDati(tutti) Then
                                Throw New InvalidOperationException("Verifica di rilettura dopo la conversione non riuscita: i dati riletti non coincidono con gli originali (" & riletti.Count & " riletti, " & tutti.Count & " originali).")
                            End If
                            trans.Commit()
                        Catch exTrans As Exception
                            trans.Rollback()
                            Throw
                        End Try
                    End Using
                End Using
            Catch exScrittura As Exception
                _conversioneFallita = True
                _logger.ScriviLog("CLSDati: conversione annullata, i dati restano nel sistema precedente (" & exScrittura.GetType().Name & ": " & exScrittura.Message.Replace(vbCrLf, " ").Replace(vbLf, " ") & ")")
                _formatoNuovo = 0
                _cachedPassword = String.Empty
                _cachedKey = Nothing
                _cachedHash = Nothing
            End Try
        Catch ex As Exception
            _conversioneFallita = True
            _logger.ScriviLog("CLSDati: conversione non riuscita, i dati restano nel sistema precedente (" & ex.GetType().Name & ": " & ex.Message.Replace(vbCrLf, " ").Replace(vbLf, " ") & ")")
            _formatoNuovo = -1
            _cachedPassword = String.Empty
            _cachedKey = Nothing
            _cachedHash = Nothing
        End Try
    End Sub

    Private Function CalcolaHashPasswordPrecedente(password As String, salt As Byte()) As Byte()
        Try
            If password = _cachedPassword AndAlso _cachedHash IsNot Nothing Then
                Return _cachedHash
            End If

            Using pbkdf2 As New Rfc2898DeriveBytes(password, salt, Iterations)
                Dim hash = pbkdf2.GetBytes(32)
                If password = _cachedPassword Then
                    _cachedHash = hash
                End If
                Return hash
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function DerivaChiavePrecedente(password As String, salt As Byte()) As Byte()
        Try
            If password = _cachedPassword AndAlso _cachedKey IsNot Nothing Then
                Return _cachedKey
            End If

            Using pbkdf2 As New Rfc2898DeriveBytes(password, salt, Iterations)
                Dim key = pbkdf2.GetBytes(KeySize)
                _cachedPassword = password
                _cachedKey = key
                _cachedHash = Nothing
                Return key
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function CriptaPrecedente(testoInChiaro As String, chiave As Byte()) As Byte()
        Try
            If String.IsNullOrEmpty(testoInChiaro) Then Return New Byte() {}
            Dim inputBytes As Byte() = Encoding.UTF8.GetBytes(testoInChiaro)

            Using aesAlg As Aes = Aes.Create()
                aesAlg.KeySize = 256
                aesAlg.Mode = CipherMode.CBC
                aesAlg.Padding = PaddingMode.PKCS7
                aesAlg.Key = chiave
                aesAlg.GenerateIV()
                Dim iv As Byte() = aesAlg.IV

                Using encryptor As ICryptoTransform = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV)
                    Using msEncrypt As New MemoryStream()
                        msEncrypt.Write(iv, 0, iv.Length)
                        Using csEncrypt As New CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)
                            csEncrypt.Write(inputBytes, 0, inputBytes.Length)
                        End Using
                        Return msEncrypt.ToArray()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function DecriptaPrecedente(datiCifrati As Byte(), chiave As Byte()) As String
        Try
            If datiCifrati Is Nothing OrElse datiCifrati.Length < 17 Then Return String.Empty

            Using aesAlg As Aes = Aes.Create()
                aesAlg.KeySize = 256
                aesAlg.Mode = CipherMode.CBC
                aesAlg.Padding = PaddingMode.PKCS7
                aesAlg.Key = chiave

                Dim iv(15) As Byte
                Array.Copy(datiCifrati, 0, iv, 0, iv.Length)
                aesAlg.IV = iv

                Using decryptor As ICryptoTransform = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV)
                    Using msDecrypt As New MemoryStream(datiCifrati, 16, datiCifrati.Length - 16)
                        Using csDecrypt As New CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read)
                            Using msPlain As New MemoryStream()
                                csDecrypt.CopyTo(msPlain)
                                Return Encoding.UTF8.GetString(msPlain.ToArray())
                            End Using
                        End Using
                    End Using
                End Using
            End Using

        Catch ex As Exception
            _logger.ScriviLog("CLSDati: un valore non si decifra con il sistema precedente (" & ex.GetType().Name & ")")
            Return String.Empty
        End Try
    End Function

    Private Function VerificaSicurezzaPassword(password As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(password) Then Return False
            Dim hasUpperCase As Boolean = password.Any(Function(c) Char.IsUpper(c))
            Dim hasLowerCase As Boolean = password.Any(Function(c) Char.IsLower(c))
            Dim hasDigit As Boolean = password.Count(Function(c) Char.IsDigit(c)) >= 2
            Dim hasSymbol As Boolean = password.Any(Function(c) Not Char.IsLetterOrDigit(c))
            Dim isLongEnough As Boolean = password.Length >= 8
            Return hasUpperCase AndAlso hasLowerCase AndAlso hasDigit AndAlso hasSymbol AndAlso isLongEnough
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            Return False
        End Try
    End Function
End Class
