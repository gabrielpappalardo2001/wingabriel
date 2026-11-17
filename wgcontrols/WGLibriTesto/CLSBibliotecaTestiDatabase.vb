Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb

Public Class CLSBibliotecaTestiDatabase

    Private Const NOME_ORIGINE_BACKUP As String = "BibliotecaTesti"
    Private Const SOTTOCARTELLA_DATI As String = "BibliotecaTesti"
    Private Const NOME_FILE_DATABASE As String = "LibriCatalogo.sqlite"

    Private ReadOnly _percorsoDatabase As String
    Private ReadOnly _logger As New Logger()

    Public Sub New(Optional percorsoDatabase As String = "")
        Try
            Dim cartellaLocale As String = WGDatabase.CartellaDati(SOTTOCARTELLA_DATI)
            _percorsoDatabase = Path.Combine(cartellaLocale, NOME_FILE_DATABASE)

            Try
                WGBackup.RegistraOrigine(NOME_ORIGINE_BACKUP, cartellaLocale)
            Catch exBackup As Exception
                _logger.ScriviLog("CLSBibliotecaTestiDatabase.New RegistraOrigine: " & exBackup.ToString())
            End Try

            CreaDatabaseSeNecessario()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.New: " & ex.ToString())
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
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS LibriCatalogo (Id INTEGER PRIMARY KEY AUTOINCREMENT, Titolo TEXT, Autore TEXT, Genere TEXT, Fonte TEXT, IdGutendex INTEGER, UrlTestoPiano TEXT, TitoloPaginaWikisource TEXT, HaCapitoli INTEGER, Lingua TEXT, ChiaveUnivoca TEXT UNIQUE)", connessione)
                    comando.ExecuteNonQuery()
                End Using

                Try
                    Using comandoMigrazione As New SQLiteCommand("ALTER TABLE LibriCatalogo ADD COLUMN HaCapitoli INTEGER", connessione)
                        comandoMigrazione.ExecuteNonQuery()
                    End Using
                Catch
                End Try

                Using cmdIdx As New SQLiteCommand(connessione)
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxAutore ON LibriCatalogo (Autore COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxGenere ON LibriCatalogo (Genere COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxAutoreTitolo ON LibriCatalogo (Autore, Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxGenereTitolo ON LibriCatalogo (Genere, Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                    cmdIdx.CommandText = "CREATE INDEX IF NOT EXISTS IdxTitolo ON LibriCatalogo (Titolo COLLATE NOCASE);"
                    cmdIdx.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(connessione, "LibriCatalogo")
                Catch exChiave As Exception
                    _logger.ScriviLog("CLSBibliotecaTestiDatabase.CreaDatabaseSeNecessario AssicuraColonnaChiave: " & exChiave.ToString())
                End Try
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.CreaDatabaseSeNecessario: " & ex.ToString())
        End Try
    End Sub

    Private Function LibroDaLettoreVeloce(lettore As SQLiteDataReader) As LibroTesto
        Dim libro As New LibroTesto()
        Try
            libro.Titolo = If(lettore.IsDBNull(0), "", lettore.GetString(0))
            libro.Autore = If(lettore.IsDBNull(1), "", lettore.GetString(1))
            libro.Genere = If(lettore.IsDBNull(2), "", lettore.GetString(2))

            Dim fonteTesto As String = If(lettore.IsDBNull(3), "", lettore.GetString(3))
            Dim fonteValore As FonteLibro
            If [Enum].TryParse(Of FonteLibro)(fonteTesto, fonteValore) Then
                libro.Fonte = fonteValore
            End If

            libro.TitoloPaginaWikisource = If(lettore.IsDBNull(4), "", lettore.GetString(4))

            If lettore.IsDBNull(5) Then
                libro.HaCapitoli = False
            Else
                libro.HaCapitoli = (Convert.ToInt32(lettore.GetValue(5)) = 1)
            End If

            libro.Lingua = If(lettore.IsDBNull(6), "", lettore.GetString(6))
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.LibroDaLettoreVeloce: " & ex.ToString())
        End Try
        Return libro
    End Function

    Public Function OttieniAutori() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT DISTINCT Autore FROM LibriCatalogo WHERE Autore IS NOT NULL AND Autore <> '' ORDER BY Autore COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(If(lettore.IsDBNull(0), "", lettore.GetString(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.OttieniAutori: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniGeneri() As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT DISTINCT Genere FROM LibriCatalogo WHERE Genere IS NOT NULL AND Genere <> '' ORDER BY Genere COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultati.Add(If(lettore.IsDBNull(0), "", lettore.GetString(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.OttieniGeneri: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniLibriPerAutore(autore As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of LibroTesto)
        Dim risultati As New List(Of LibroTesto)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(autore) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Fonte, TitoloPaginaWikisource, HaCapitoli, Lingua, Id FROM LibriCatalogo " &
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
                            risultati.Add(LibroDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(7)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.OttieniLibriPerAutore: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniLibriPerGenere(genere As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of LibroTesto)
        Dim risultati As New List(Of LibroTesto)(Math.Min(count, 500))
        titoloUltimoElemento = titoloCursore
        idUltimoElemento = idCursore
        Try
            If String.IsNullOrWhiteSpace(genere) OrElse count <= 0 Then Return risultati
            Dim haCursore As Boolean = Not String.IsNullOrEmpty(titoloCursore)
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand(
                    "SELECT Titolo, Autore, Genere, Fonte, TitoloPaginaWikisource, HaCapitoli, Lingua, Id FROM LibriCatalogo " &
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
                            risultati.Add(LibroDaLettoreVeloce(lettore))
                            titoloUltimoElemento = If(lettore.IsDBNull(0), "", lettore.GetString(0))
                            idUltimoElemento = lettore.GetInt64(7)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.OttieniLibriPerGenere: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function ContaLibri() As Integer
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT COUNT(*) FROM LibriCatalogo", connessione)
                    Dim risultato As Object = comando.ExecuteScalar()
                    If risultato IsNot Nothing AndAlso Not IsDBNull(risultato) Then
                        Return Convert.ToInt32(risultato)
                    End If
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.ContaLibri: " & ex.ToString())
        End Try
        Return 0
    End Function

    Public Sub SvuotaCatalogo()
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using comando As New SQLiteCommand("DELETE FROM LibriCatalogo", connessione)
                    comando.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.SvuotaCatalogo: " & ex.ToString())
        End Try
    End Sub

    Public Sub RicostruisciIndiciOttimizzati()
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                Using cmd As New SQLiteCommand(connessione)
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxAutore;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxGenere;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxAutoreTitolo;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxGenereTitolo;"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "DROP INDEX IF EXISTS IdxTitolo;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxAutore ON LibriCatalogo (Autore COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxGenere ON LibriCatalogo (Genere COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxAutoreTitolo ON LibriCatalogo (Autore, Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxGenereTitolo ON LibriCatalogo (Genere, Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxTitolo ON LibriCatalogo (Titolo COLLATE NOCASE);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "ANALYZE;"
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTestiDatabase.RicostruisciIndiciOttimizzati: " & ex.ToString())
        End Try
    End Sub

End Class