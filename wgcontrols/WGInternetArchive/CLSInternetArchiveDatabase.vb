Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb

' Gestione del database locale per la catalogazione e la ricerca degli elementi di Internet Archive.
Public Class CLSInternetArchiveDatabase

    Private Const NOME_ORIGINE_BACKUP As String = "InternetArchive"
    Private Const SOTTOCARTELLA_DATI As String = "InternetArchive"
    Private Const NOME_FILE_DATABASE As String = "ArchivioCatalogo.sqlite"

    Private ReadOnly _percorsoDatabase As String
    Private ReadOnly _logger As New Logger()

    Public Sub New(Optional percorsoDatabase As String = "")
        Try
            Dim cartellaLocale As String = WGDatabase.CartellaDati(SOTTOCARTELLA_DATI)
            _percorsoDatabase = Path.Combine(cartellaLocale, NOME_FILE_DATABASE)

            Try
                WGBackup.RegistraOrigine(NOME_ORIGINE_BACKUP, cartellaLocale)
            Catch exBackup As Exception
                _logger.ScriviLog("CLSInternetArchiveDatabase.New RegistraOrigine: " & exBackup.ToString())
            End Try

            CreaDatabaseSeNecessario()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.New: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniConnessione() As SQLiteConnection
        Dim cs As String =
            "Data Source=" & _percorsoDatabase &
            ";Version=3;" &
            "Journal Mode=WAL;" &
            "Synchronous=Normal;" &
            "Cache Size=-131072;" &
            "Temp Store=Memory;" &
            "Pooling=True;" &
            "Default Timeout=30;"
        Dim connessione As New SQLiteConnection(cs)
        connessione.Open()
        Using comandoPragma As New SQLiteCommand("PRAGMA mmap_size=268435456;", connessione)
            comandoPragma.ExecuteNonQuery()
        End Using
        Return connessione
    End Function

    Private Sub CreaDatabaseSeNecessario()
        Try
            Dim cartella As String = Path.GetDirectoryName(_percorsoDatabase)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            If Not File.Exists(_percorsoDatabase) Then
                SQLiteConnection.CreateFile(_percorsoDatabase)
            End If

            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "CREATE TABLE IF NOT EXISTS ArchivioCatalogo (" &
                    "Id INTEGER PRIMARY KEY AUTOINCREMENT, " &
                    "Titolo TEXT, " &
                    "Autore TEXT, " &
                    "Genere TEXT, " &
                    "Identificatore TEXT, " &
                    "TipoSupporto TEXT, " &
                    "Lingua TEXT, " &
                    "ChiaveUnivoca TEXT UNIQUE)", connessione)
                    comando.ExecuteNonQuery()
                End Using

                Using cmdIdx As New SQLiteCommand(connessione)
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioSupportoTitolo ON ArchivioCatalogo (TipoSupporto, Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioAutoreTitolo ON ArchivioCatalogo (Autore, Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioGenereTitolo ON ArchivioCatalogo (Genere, Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioAutore ON ArchivioCatalogo (Autore COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioGenere ON ArchivioCatalogo (Genere COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioSupporto ON ArchivioCatalogo (TipoSupporto COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioTitolo ON ArchivioCatalogo (Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(connessione, "ArchivioCatalogo")
                Catch exChiave As Exception
                    _logger.ScriviLog("CLSInternetArchiveDatabase.CreaDatabaseSeNecessario AssicuraColonnaChiave: " & exChiave.ToString())
                End Try
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.CreaDatabaseSeNecessario: " & ex.ToString())
        End Try
    End Sub

    Private Function ElementoDaLettoreVeloce(lettore As SQLiteDataReader) As ElementoArchivio
        Dim e As New ElementoArchivio()
        Try
            e.Titolo = If(lettore.IsDBNull(0), "", lettore.GetString(0))
            e.Autore = If(lettore.IsDBNull(1), "", lettore.GetString(1))
            e.Genere = If(lettore.IsDBNull(2), "", lettore.GetString(2))
            e.Identificatore = If(lettore.IsDBNull(3), "", lettore.GetString(3))
            Dim tipo As String = If(lettore.IsDBNull(4), "", lettore.GetString(4))
            e.EAudio = String.Equals(tipo, "Audio", StringComparison.OrdinalIgnoreCase)
            e.Lingua = If(lettore.IsDBNull(5), "", lettore.GetString(5))
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.ElementoDaLettoreVeloce: " & ex.ToString())
        End Try
        Return e
    End Function

    Public Function OttieniAutori() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT DISTINCT Autore FROM ArchivioCatalogo WHERE Autore IS NOT NULL AND Autore <> '' ORDER BY Autore COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(If(lettore.IsDBNull(0), "", lettore.GetString(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniAutori: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniGeneri() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT DISTINCT Genere FROM ArchivioCatalogo WHERE Genere IS NOT NULL AND Genere <> '' ORDER BY Genere COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(If(lettore.IsDBNull(0), "", lettore.GetString(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniGeneri: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniSupporti() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT DISTINCT TipoSupporto FROM ArchivioCatalogo WHERE TipoSupporto IS NOT NULL AND TipoSupporto <> '' ORDER BY TipoSupporto COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(If(lettore.IsDBNull(0), "", lettore.GetString(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniSupporti: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniElementiPerAutore(autore As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Dim risultati As New List(Of ElementoArchivio)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(autore) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Identificatore, TipoSupporto, Lingua, Id FROM ArchivioCatalogo " &
                    "WHERE Autore = @autore " &
                    "AND (@haCursore = 0 OR Titolo COLLATE NOCASE > @ultimoTitolo OR (Titolo COLLATE NOCASE = @ultimoTitolo AND Id > @ultimoId)) " &
                    "ORDER BY Titolo COLLATE NOCASE, Id LIMIT @count", connessione)
                    comando.Parameters.AddWithValue("@autore", autore)
                    comando.Parameters.AddWithValue("@haCursore", If(haCursore, 1, 0))
                    comando.Parameters.AddWithValue("@ultimoTitolo", If(titoloCursore, ""))
                    comando.Parameters.AddWithValue("@ultimoId", idCursore)
                    comando.Parameters.AddWithValue("@count", count)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(ElementoDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(6)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniElementiPerAutore: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniElementiPerGenere(genere As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Dim risultati As New List(Of ElementoArchivio)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(genere) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Identificatore, TipoSupporto, Lingua, Id FROM ArchivioCatalogo " &
                    "WHERE Genere = @genere " &
                    "AND (@haCursore = 0 OR Titolo COLLATE NOCASE > @ultimoTitolo OR (Titolo COLLATE NOCASE = @ultimoTitolo AND Id > @ultimoId)) " &
                    "ORDER BY Titolo COLLATE NOCASE, Id LIMIT @count", connessione)
                    comando.Parameters.AddWithValue("@genere", genere)
                    comando.Parameters.AddWithValue("@haCursore", If(haCursore, 1, 0))
                    comando.Parameters.AddWithValue("@ultimoTitolo", If(titoloCursore, ""))
                    comando.Parameters.AddWithValue("@ultimoId", idCursore)
                    comando.Parameters.AddWithValue("@count", count)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(ElementoDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(6)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniElementiPerGenere: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniElementiPerSupporto(tipoSupporto As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Dim risultati As New List(Of ElementoArchivio)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(tipoSupporto) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Identificatore, TipoSupporto, Lingua, Id FROM ArchivioCatalogo " &
                    "WHERE TipoSupporto = @tipo " &
                    "AND (@haCursore = 0 OR Titolo COLLATE NOCASE > @ultimoTitolo OR (Titolo COLLATE NOCASE = @ultimoTitolo AND Id > @ultimoId)) " &
                    "ORDER BY Titolo COLLATE NOCASE, Id LIMIT @count", connessione)
                    comando.Parameters.AddWithValue("@tipo", tipoSupporto)
                    comando.Parameters.AddWithValue("@haCursore", If(haCursore, 1, 0))
                    comando.Parameters.AddWithValue("@ultimoTitolo", If(titoloCursore, ""))
                    comando.Parameters.AddWithValue("@ultimoId", idCursore)
                    comando.Parameters.AddWithValue("@count", count)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(ElementoDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(6)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.OttieniElementiPerSupporto: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function CercaTitolo(testo As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Dim risultati As New List(Of ElementoArchivio)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Identificatore, TipoSupporto, Lingua, Id FROM ArchivioCatalogo " &
                    "WHERE Titolo LIKE @q COLLATE NOCASE " &
                    "AND (@haCursore = 0 OR Titolo COLLATE NOCASE > @ultimoTitolo OR (Titolo COLLATE NOCASE = @ultimoTitolo AND Id > @ultimoId)) " &
                    "ORDER BY Titolo COLLATE NOCASE, Id LIMIT @count", connessione)
                    comando.Parameters.AddWithValue("@q", "%" & testo.Trim() & "%")
                    comando.Parameters.AddWithValue("@haCursore", If(haCursore, 1, 0))
                    comando.Parameters.AddWithValue("@ultimoTitolo", If(titoloCursore, ""))
                    comando.Parameters.AddWithValue("@ultimoId", idCursore)
                    comando.Parameters.AddWithValue("@count", count)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(ElementoDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(6)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.CercaTitolo: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function ContaElementi() As Integer
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT COUNT(*) FROM ArchivioCatalogo", connessione)
                    Dim risultato As Object = comando.ExecuteScalar()
                    If risultato IsNot Nothing AndAlso Not IsDBNull(risultato) Then
                        Return Convert.ToInt32(risultato)
                    End If
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.ContaElementi: " & ex.ToString())
        End Try
        Return 0
    End Function

    Public Sub RicostruisciIndiciOttimizzati()
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using cmd As New SQLiteCommand(connessione)
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioAutore;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioGenere;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioSupporto;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioTitolo;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioSupportoTitolo;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioAutoreTitolo;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxArchivioGenereTitolo;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioSupportoTitolo ON ArchivioCatalogo (TipoSupporto, Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioAutoreTitolo ON ArchivioCatalogo (Autore, Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioGenereTitolo ON ArchivioCatalogo (Genere, Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioAutore ON ArchivioCatalogo (Autore COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioGenere ON ArchivioCatalogo (Genere COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioSupporto ON ArchivioCatalogo (TipoSupporto COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxArchivioTitolo ON ArchivioCatalogo (Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "ANALYZE;"
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchiveDatabase.RicostruisciIndiciOttimizzati: " & ex.ToString())
        End Try
    End Sub

End Class