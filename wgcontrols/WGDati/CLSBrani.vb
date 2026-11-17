Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Runtime.InteropServices.ComTypes
Imports System.Security
Imports System.Security.Cryptography
Imports System.Text
Imports System.Net.Http
Imports System.Data.SQLite
Imports System.Xml.Serialization
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports WGDb

Public Enum OrdinamentoPreferiti
    Alfabetico
    DataAggiunta
    Durata
    Categoria
End Enum

Public Enum OrdinamentoRecenti
    DataRecente
    Alfabetico
End Enum

Public Class Sessione
    Public Property Id As Long
    Public Property Path As String
    Public Property Position As Long
    Public Property Sha1Hash As String
    Public Property LastPlayed As DateTime
End Class

Public Class BranoPreferito
    Public Property Id As Long
    Public Property Path As String
    Public Property DateAdded As DateTime
    Public Property Duration As Long
    Public Property CategoryId As Long
    Public Property CategoryName As String
End Class

Public Class CategoriaBrano
    Public Property Id As Long
    Public Property Nome As String
End Class

Public Class MetadatiBrano
    Public Property Path As String
    Public Property Titolo As String
    Public Property Artista As String
    Public Property Album As String
    Public Property Anno As String
    Public Property CoverUrl As String
    Public Property Genere As String
    Public Property PreviewUrl As String
    Public Property TrackNumber As Integer
    Public Property TrackCount As Integer
    Public Property DiscNumber As Integer
    Public Property DiscCount As Integer
    Public Property Country As String
    Public Property Explicit As String
    Public Property DataAggiornamento As DateTime
End Class

Public Structure Brano
    Public Nome As String
    Public Path As String
    Public Categoria As String
End Structure

<Serializable>
Public Class LegacySessione
    Public Property Path As String
    Public Property Position As Long
    Public Property Sha1Hash As String
    Public Property LastPlayed As DateTime
End Class

<Serializable>
Public Class LegacyBranoPreferito
    Public Property Path As String
    Public Property DateAdded As DateTime
    Public Property Duration As Long
End Class

Public Class CLSBrani
    Implements IDisposable

    Private Const S_OK As Integer = 0
    Private Const STGM_READ As UInteger = 0
    Private Shared ReadOnly PKEY_Media_Duration_FMTID As New Guid("6446B7A0-009A-4C91-953E-25D20739C3E9")
    Private Const PKEY_Media_Duration_PID As UInteger = 3
    Private Shared ReadOnly IID_IPropertyStore As Guid = New Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")

    Private ReadOnly _connectionString As String
    Private ReadOnly _appDataFolder As String
    Private ReadOnly _httpClient As HttpClient
    Private ReadOnly _dbLock As SemaphoreSlim
    Private _inizializzazioneTask As Task
    Private _disposed As Boolean = False
    Private log As New Logger()

    <StructLayout(LayoutKind.Sequential, Pack:=4)>
    Private Structure PROPERTYKEY
        Public fmtid As Guid
        Public pid As UInteger
    End Structure

    <StructLayout(LayoutKind.Explicit)>
    Private Structure PropVariant
        <FieldOffset(0)> Public vt As Short
        <FieldOffset(8)> Public h As Long
        <FieldOffset(8)> Public filetime As FILETIME
        <FieldOffset(8)> Public lVal As Long
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FILETIME
        Public dwLowDateTime As UInteger
        Public dwHighDateTime As UInteger
    End Structure

    <ComImport>
    <Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPropertyStore
        Function GetCount(<Out> ByRef cProps As UInteger) As Integer
        Function GetAt(iProp As UInteger, <Out> ByRef pkey As PROPERTYKEY) As Integer
        Function GetValue(ByRef key As PROPERTYKEY, <Out> ByRef pv As PropVariant) As Integer
        Function SetValue(ByRef key As PROPERTYKEY, ByRef pv As PropVariant) As Integer
        Function Commit() As Integer
    End Interface

    <DllImport("shell32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Shared Function SHGetPropertyStoreFromParsingName(
        pszPath As String,
        pbc As IntPtr,
        flags As UInteger,
        ByRef riid As Guid,
        <Out> ByRef ppv As IPropertyStore
    ) As Integer
    End Function

    Private Shared Function RisolviCartellaPlayer() As String

        Dim cartella As String = WGDatabase.CartellaDati("player")

        Try
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch ex as Exception

            Dim log As New Logger()
            log.ScriviLog(ex.Message & ex.StackTrace)
        End try
                Return Cartella    
End Function

    Public Sub New()
        Try
            _dbLock = New SemaphoreSlim(1, 1)
            _appDataFolder = RisolviCartellaPlayer()

            If Not System.IO.Directory.Exists(_appDataFolder) Then
                System.IO.Directory.CreateDirectory(_appDataFolder)
            End If

            Dim dbPath As String = System.IO.Path.Combine(_appDataFolder, "library.db")
            _connectionString = $"Data Source={dbPath};Version=3;Journal Mode=WAL;Cache Size=5000;Pooling=True;Max Pool Size=100;Synchronous=Normal;BusyTimeout=8000;"

            _httpClient = New HttpClient()
            _httpClient.Timeout = TimeSpan.FromSeconds(15)

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")

            _inizializzazioneTask = InizializzaEMigraAsync()
        Catch ex As Exception
            log.ScriviLog($"CRITICAL INITIALIZATION ERROR: {ex.Message}")
            Throw
        End Try
    End Sub

    Private Async Function InizializzaEMigraAsync() As Task
        Await InizializzaDatabaseAsync()
        Await MigraVecchiDatiXmlAsync()
    End Function

    Private Async Function AttendiInizializzazioneAsync() As Task
        Try
            If _inizializzazioneTask IsNot Nothing Then
                Await _inizializzazioneTask
            End If
        Catch ex As Exception
            log.ScriviLog($"AttendiInizializzazioneAsync - Errore: {ex.Message}")
        End Try
    End Function

    Private Async Function ExecuteWithRetryAsync(Of T)(action As Func(Of SQLiteConnection, Task(Of T))) As Task(Of T)
        Const maxRetries As Integer = 3
        Dim delay As Integer = 100

        Await _dbLock.WaitAsync()
        Try
            For i As Integer = 1 To maxRetries
                Dim occupato As Boolean = False
                Try
                    Using conn As New SQLiteConnection(_connectionString)
                        Await conn.OpenAsync()
                        Return Await action(conn)
                    End Using
                Catch ex As SQLiteException When ex.ResultCode = SQLiteErrorCode.Busy OrElse ex.ResultCode = SQLiteErrorCode.Locked
                    If i = maxRetries Then Throw
                    occupato = True
                catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
end try
                ' VB non accetta Await dentro Catch: l'attesa sta qui, fuori dal blocco.
                If occupato Then
                    Await Task.Delay(delay)
                    delay *= 2
                End If

            Next
            Return Nothing
        
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
Finally
            _dbLock.Release()
        End Try
    End Function

    Private Async Function ExecuteNonQueryWithRetryAsync(action As Func(Of SQLiteConnection, Task)) As Task
        Await ExecuteWithRetryAsync(Async Function(conn)
                                        Await action(conn)
                                        Return True
                                    End Function)
    End Function

    Private Async Function InizializzaDatabaseAsync() As Task
        Try
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand(conn)
                                                        cmd.CommandText = "PRAGMA journal_mode=WAL;"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Categorie (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT UNIQUE NOT NULL);"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Sessioni (Id INTEGER PRIMARY KEY AUTOINCREMENT, Path TEXT UNIQUE NOT NULL, Position INTEGER, Sha1Hash TEXT, LastPlayed DATETIME);"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_sessioni_path ON Sessioni(Path);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_sessioni_lastplayed ON Sessioni(LastPlayed);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_sessioni_hash ON Sessioni(Sha1Hash);"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Preferiti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Path TEXT UNIQUE NOT NULL, DateAdded DATETIME, Duration INTEGER, CategoryId INTEGER DEFAULT 0, FOREIGN KEY(CategoryId) REFERENCES Categorie(Id));"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_preferiti_path ON Preferiti(Path);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_preferiti_date ON Preferiti(DateAdded);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_preferiti_cat ON Preferiti(CategoryId);"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        Dim createMetaSql As New StringBuilder()
                                                        createMetaSql.Append("CREATE TABLE IF NOT EXISTS Metadati (")
                                                        createMetaSql.Append("Path TEXT PRIMARY KEY, ")
                                                        createMetaSql.Append("Titolo TEXT, ")
                                                        createMetaSql.Append("Artista TEXT, ")
                                                        createMetaSql.Append("Album TEXT, ")
                                                        createMetaSql.Append("Anno TEXT, ")
                                                        createMetaSql.Append("CoverUrl TEXT, ")
                                                        createMetaSql.Append("Genere TEXT, ")
                                                        createMetaSql.Append("PreviewUrl TEXT, ")
                                                        createMetaSql.Append("TrackNumber INTEGER DEFAULT 0, ")
                                                        createMetaSql.Append("TrackCount INTEGER DEFAULT 0, ")
                                                        createMetaSql.Append("DiscNumber INTEGER DEFAULT 0, ")
                                                        createMetaSql.Append("DiscCount INTEGER DEFAULT 0, ")
                                                        createMetaSql.Append("Country TEXT, ")
                                                        createMetaSql.Append("Explicit TEXT, ")
                                                        createMetaSql.Append("DataAggiornamento DATETIME")
                                                        createMetaSql.Append(");")

                                                        cmd.CommandText = createMetaSql.ToString()
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_metadati_path ON Metadati(Path);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_metadati_titolo ON Metadati(Titolo);"
                                                        Await cmd.ExecuteNonQueryAsync()
                                                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_metadati_artista ON Metadati(Artista);"
                                                        Await cmd.ExecuteNonQueryAsync()

                                                        Try
                                                            WGDatabase.AssicuraColonnaChiave(conn, "Categorie")
                                                            WGDatabase.AssicuraColonnaChiave(conn, "Sessioni")
                                                            WGDatabase.AssicuraColonnaChiave(conn, "Preferiti")
                                                            WGDatabase.AssicuraColonnaChiave(conn, "Metadati")
                                                        Catch exChiave As Exception
                                                            log.ScriviLog($"InizializzaDatabaseAsync chiave: {exChiave}")
                                                        End Try

                                                        cmd.CommandText = "INSERT OR IGNORE INTO Categorie (Id, Nome, ChiaveWG) VALUES (0, 'Generale', @chiaveWG);"
                                                        cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog($"Database Initialization Failed: {ex.Message}")
            Throw
        End Try
    End Function

    Private Async Function MigraVecchiDatiXmlAsync() As Task
try
        Dim pathPreferitiXml As String = System.IO.Path.Combine(_appDataFolder, "favorites.xml")
        Dim pathSessioniXml As String = System.IO.Path.Combine(_appDataFolder, "player.xml")

        If System.IO.File.Exists(pathPreferitiXml) Then
            Try
                Dim preferitiLegacy As List(Of LegacyBranoPreferito) = Nothing
                Await Task.Run(Sub()
                                   Using reader As New System.IO.StreamReader(pathPreferitiXml)
                                       Dim serializer As New XmlSerializer(GetType(List(Of LegacyBranoPreferito)))
                                       preferitiLegacy = DirectCast(serializer.Deserialize(reader), List(Of LegacyBranoPreferito))
                                   End Using
                               End Sub)

                If preferitiLegacy IsNot Nothing AndAlso preferitiLegacy.Count > 0 Then
                    Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                            Using transaction = conn.BeginTransaction()
                                                                Try
                                                                    For Each p In preferitiLegacy
                                                                        If Not String.IsNullOrWhiteSpace(p.Path) Then
                                                                            Dim normalizedPath As String = NormalizzaPercorso(p.Path)
                                                                            Using cmd As New SQLiteCommand("INSERT INTO Preferiti (Path, DateAdded, Duration, CategoryId, ChiaveWG) VALUES (@path, @date, @dur, 0, @chiaveWG) ON CONFLICT(Path) DO NOTHING;", conn)
                                                                                cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                                                cmd.Parameters.AddWithValue("@date", p.DateAdded)
                                                                                cmd.Parameters.AddWithValue("@dur", p.Duration)
                                                                                cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                                                                                Await cmd.ExecuteNonQueryAsync()
                                                                            End Using
                                                                        End If
                                                                    Next
                                                                    transaction.Commit()
                                                                
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
                                                                    transaction.Rollback()
                                                                    Throw
                                                                End Try
                                                            End Using
                                                        End Function)
                End If

                System.IO.File.Move(pathPreferitiXml, pathPreferitiXml & ".migrated")
            Catch ex As Exception
                log.ScriviLog($"Migration Error Favorites: {ex.Message}")
                    Try
                        System.IO.File.Move(pathPreferitiXml, pathPreferitiXml & ".error")
                    Catch ex2 As Exception
                        log.ScriviLog(ex2.Message & ex2.StackTrace)
                    End try
            End Try
        End If

        If System.IO.File.Exists(pathSessioniXml) Then
            Try
                Dim sessioniLegacy As List(Of LegacySessione) = Nothing
                Await Task.Run(Sub()
                                   Using reader As New System.IO.StreamReader(pathSessioniXml)
                                       Dim serializer As New XmlSerializer(GetType(List(Of LegacySessione)))
                                       sessioniLegacy = DirectCast(serializer.Deserialize(reader), List(Of LegacySessione))
                                   End Using
                               End Sub)

                If sessioniLegacy IsNot Nothing AndAlso sessioniLegacy.Count > 0 Then
                    Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                            Using transaction = conn.BeginTransaction()
                                                                Try
                                                                    For Each s In sessioniLegacy
                                                                        If Not String.IsNullOrWhiteSpace(s.Path) Then
                                                                            Dim normalizedPath As String = NormalizzaPercorso(s.Path)
                                                                            Using cmd As New SQLiteCommand("INSERT INTO Sessioni (Path, Position, Sha1Hash, LastPlayed, ChiaveWG) VALUES (@path, @pos, @hash, @last, @chiaveWG) ON CONFLICT(Path) DO NOTHING;", conn)
                                                                                cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                                                cmd.Parameters.AddWithValue("@pos", s.Position)
                                                                                cmd.Parameters.AddWithValue("@hash", s.Sha1Hash)
                                                                                cmd.Parameters.AddWithValue("@last", s.LastPlayed)
                                                                                cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                                                                                Await cmd.ExecuteNonQueryAsync()
                                                                            End Using
                                                                        End If
                                                                    Next
                                                                    transaction.Commit()
                                                                catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
                                                                    transaction.Rollback()
                                                                    Throw
                                                                End Try
                                                            End Using
                                                        End Function)
                End If

                System.IO.File.Move(pathSessioniXml, pathSessioniXml & ".migrated")
            Catch ex As Exception
                log.ScriviLog($"Migration Error Sessions: {ex.Message}")
                    Try
                        System.IO.File.Move(pathSessioniXml, pathSessioniXml & ".error")
                    Catch ex2 As Exception
                        log.ScriviLog(ex2.Message & ex2.StackTrace)
                    End try

            End Try
        End If
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
end try
    End Function

    Public Async Function InfoBranoAsync(percorso As String) As Task(Of MetadatiBrano)
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(percorso) Then Return Nothing
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Dim meta As MetadatiBrano = Await GetMetadatiDaDBAsync(normalizedPath)

            If meta Is Nothing Then
                Dim termineRicerca As String = System.IO.Path.GetFileNameWithoutExtension(normalizedPath)
                meta = Await ScaricaEArchiviaMetadatiAsync(normalizedPath, termineRicerca)
            Else
                If meta.DataAggiornamento < DateTime.Now.AddDays(-30) Then
                    Dim termineRicerca As String = System.IO.Path.GetFileNameWithoutExtension(normalizedPath)
                    Dim newMeta = Await ScaricaEArchiviaMetadatiAsync(normalizedPath, termineRicerca)
                    If newMeta IsNot Nothing Then
                        meta = newMeta
                    End If
                End If
            End If

            Return meta
        Catch ex As Exception
            log.ScriviLog($"InfoBranoAsync Error: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Async Function GetMetadatiDaDBAsync(path As String) As Task(Of MetadatiBrano)
try
        Return Await ExecuteWithRetryAsync(Async Function(conn)
                                               Using cmd As New SQLiteCommand("SELECT * FROM Metadati WHERE Path = @path", conn)
                                                   cmd.Parameters.AddWithValue("@path", path)
                                                   Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                       If Await reader.ReadAsync() Then
                                                           Return New MetadatiBrano With {
                                                               .Path = reader("Path").ToString(),
                                                               .Titolo = If(IsDBNull(reader("Titolo")), "", reader("Titolo").ToString()),
                                                               .Artista = If(IsDBNull(reader("Artista")), "", reader("Artista").ToString()),
                                                               .Album = If(IsDBNull(reader("Album")), "", reader("Album").ToString()),
                                                               .Anno = If(IsDBNull(reader("Anno")), "", reader("Anno").ToString()),
                                                               .CoverUrl = If(IsDBNull(reader("CoverUrl")), "", reader("CoverUrl").ToString()),
                                                               .Genere = If(IsDBNull(reader("Genere")), "", reader("Genere").ToString()),
                                                               .PreviewUrl = If(IsDBNull(reader("PreviewUrl")), "", reader("PreviewUrl").ToString()),
                                                               .TrackNumber = If(IsDBNull(reader("TrackNumber")), 0, Convert.ToInt32(reader("TrackNumber"))),
                                                               .TrackCount = If(IsDBNull(reader("TrackCount")), 0, Convert.ToInt32(reader("TrackCount"))),
                                                               .DiscNumber = If(IsDBNull(reader("DiscNumber")), 0, Convert.ToInt32(reader("DiscNumber"))),
                                                               .DiscCount = If(IsDBNull(reader("DiscCount")), 0, Convert.ToInt32(reader("DiscCount"))),
                                                               .Country = If(IsDBNull(reader("Country")), "", reader("Country").ToString()),
                                                               .Explicit = If(IsDBNull(reader("Explicit")), "", reader("Explicit").ToString()),
                                                               .DataAggiornamento = DateTime.Parse(reader("DataAggiornamento").ToString())
                                                           }
                                                       End If
                                                   End Using
                                               End Using
                                               Return Nothing
                                           End Function)   
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
end try
End Function

    Private Async Function ScaricaEArchiviaMetadatiAsync(path As String, termineRicerca As String) As Task(Of MetadatiBrano)
        Try
            Dim url As String = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(termineRicerca)}&media=music&limit=1"
            Dim response As HttpResponseMessage = Await _httpClient.GetAsync(url)

            If response.IsSuccessStatusCode Then
                Dim jsonString As String = Await response.Content.ReadAsStringAsync()
                Dim json As JObject = JObject.Parse(jsonString)

                If json("resultCount").Value(Of Integer)() > 0 Then
                    Dim item = json("results")(0)
                    Dim meta As New MetadatiBrano With {
                        .Path = path,
                        .Titolo = item("trackName")?.ToString(),
                        .Artista = item("artistName")?.ToString(),
                        .Album = item("collectionName")?.ToString(),
                        .Anno = If(item("releaseDate") IsNot Nothing, DateTime.Parse(item("releaseDate").ToString()).Year.ToString(), ""),
                        .CoverUrl = item("artworkUrl100")?.ToString(),
                        .Genere = item("primaryGenreName")?.ToString(),
                        .PreviewUrl = item("previewUrl")?.ToString(),
                        .TrackNumber = If(item("trackNumber") IsNot Nothing, item("trackNumber").Value(Of Integer)(), 0),
                        .TrackCount = If(item("trackCount") IsNot Nothing, item("trackCount").Value(Of Integer)(), 0),
                        .DiscNumber = If(item("discNumber") IsNot Nothing, item("discNumber").Value(Of Integer)(), 0),
                        .DiscCount = If(item("discCount") IsNot Nothing, item("discCount").Value(Of Integer)(), 0),
                        .Country = item("country")?.ToString(),
                        .Explicit = item("trackExplicitness")?.ToString(),
                        .DataAggiornamento = DateTime.Now
                    }

                    Await SalvaMetadatiSuDBAsync(meta)
                    Return meta
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"Metadata Download Error: {ex.Message}")
        End Try
        Return Nothing
    End Function

    Private Async Function SalvaMetadatiSuDBAsync(meta As MetadatiBrano) As Task
        Try
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Dim sqlBuilder As New StringBuilder()
                                                    sqlBuilder.Append("INSERT INTO Metadati (Path, Titolo, Artista, Album, Anno, CoverUrl, Genere, PreviewUrl, TrackNumber, TrackCount, DiscNumber, DiscCount, Country, Explicit, DataAggiornamento, ChiaveWG) ")
                                                    sqlBuilder.Append("VALUES (@path, @titolo, @artista, @album, @anno, @cover, @genere, @preview, @tNum, @tCount, @dNum, @dCount, @country, @expl, @data, @chiaveWG) ")
                                                    sqlBuilder.Append("ON CONFLICT(Path) DO UPDATE SET Titolo=@titolo, Artista=@artista, Album=@album, Anno=@anno, CoverUrl=@cover, Genere=@genere, PreviewUrl=@preview, ")
                                                    sqlBuilder.Append("TrackNumber=@tNum, TrackCount=@tCount, DiscNumber=@dNum, DiscCount=@dCount, Country=@country, Explicit=@expl, DataAggiornamento=@data;")

                                                    Using cmd As New SQLiteCommand(sqlBuilder.ToString(), conn)
                                                        cmd.Parameters.AddWithValue("@path", meta.Path)
                                                        cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Metadati"))
                                                        cmd.Parameters.AddWithValue("@titolo", If(String.IsNullOrEmpty(meta.Titolo), DBNull.Value, meta.Titolo))
                                                        cmd.Parameters.AddWithValue("@artista", If(String.IsNullOrEmpty(meta.Artista), DBNull.Value, meta.Artista))
                                                        cmd.Parameters.AddWithValue("@album", If(String.IsNullOrEmpty(meta.Album), DBNull.Value, meta.Album))
                                                        cmd.Parameters.AddWithValue("@anno", If(String.IsNullOrEmpty(meta.Anno), DBNull.Value, meta.Anno))
                                                        cmd.Parameters.AddWithValue("@cover", If(String.IsNullOrEmpty(meta.CoverUrl), DBNull.Value, meta.CoverUrl))
                                                        cmd.Parameters.AddWithValue("@genere", If(String.IsNullOrEmpty(meta.Genere), DBNull.Value, meta.Genere))
                                                        cmd.Parameters.AddWithValue("@preview", If(String.IsNullOrEmpty(meta.PreviewUrl), DBNull.Value, meta.PreviewUrl))
                                                        cmd.Parameters.AddWithValue("@tNum", meta.TrackNumber)
                                                        cmd.Parameters.AddWithValue("@tCount", meta.TrackCount)
                                                        cmd.Parameters.AddWithValue("@dNum", meta.DiscNumber)
                                                        cmd.Parameters.AddWithValue("@dCount", meta.DiscCount)
                                                        cmd.Parameters.AddWithValue("@country", If(String.IsNullOrEmpty(meta.Country), DBNull.Value, meta.Country))
                                                        cmd.Parameters.AddWithValue("@expl", If(String.IsNullOrEmpty(meta.Explicit), DBNull.Value, meta.Explicit))
                                                        cmd.Parameters.AddWithValue("@data", meta.DataAggiornamento)

                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog($"Save Metadata Error: {ex.Message}")
        End Try
    End Function

    Public Async Function IsPreferitoAsync(percorso As String) As Task(Of Boolean)
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(percorso) Then Return False
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Count(1) FROM Preferiti WHERE Path = @path", conn)
                                                       cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                       Dim result = Await cmd.ExecuteScalarAsync()
                                                       Return Convert.ToInt32(result) > 0
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Public Async Function IsRecenteAsync(percorso As String) As Task(Of Boolean)
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(percorso) Then Return False
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Count(1) FROM Sessioni WHERE Path = @path", conn)
                                                       cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                       Dim result = Await cmd.ExecuteScalarAsync()
                                                       Return Convert.ToInt32(result) > 0
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Public Async Function RimuoviBranoPreferitoAsync(percorso As String) As Task
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("DELETE FROM Preferiti WHERE Path = @path", conn)
                                                        cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function AggiungiBranoPreferitoAsync(percorso As String) As Task
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim normalizedPath As String = NormalizzaPercorso(percorso)

            Dim durata As Long = Await Task.Run(Function() GetMediaDuration(normalizedPath))

            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("INSERT INTO Preferiti (Path, DateAdded, Duration, CategoryId, ChiaveWG) VALUES (@path, @date, @dur, 0, @chiaveWG) ON CONFLICT(Path) DO UPDATE SET DateAdded = @date;", conn)
                                                        cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                        cmd.Parameters.AddWithValue("@date", DateTime.Now)
                                                        cmd.Parameters.AddWithValue("@dur", durata)
                                                        cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Preferiti"))
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)

            Await InfoBranoAsync(normalizedPath)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function SpostaBraniInCategoriaAsync(paths As List(Of String), categoryId As Long) As Task
        Try
            Await AttendiInizializzazioneAsync()
            If paths Is Nothing OrElse paths.Count = 0 Then Return

            Dim percorsiNormalizzati As New List(Of String)()

            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using transaction = conn.BeginTransaction()
                                                        Try
                                                            For Each path In paths
                                                                Dim normalizedPath As String = NormalizzaPercorso(path)
                                                                percorsiNormalizzati.Add(normalizedPath)
                                                                Dim durata As Long = 0

                                                                Using cmdCheck As New SQLiteCommand("SELECT Duration FROM Preferiti WHERE Path = @path", conn)
                                                                    cmdCheck.Parameters.AddWithValue("@path", normalizedPath)
                                                                    Dim res = Await cmdCheck.ExecuteScalarAsync()
                                                                    If res Is Nothing OrElse IsDBNull(res) Then
                                                                        durata = Await Task.Run(Function() GetMediaDuration(normalizedPath))
                                                                    Else
                                                                        durata = Convert.ToInt64(res)
                                                                    End If
                                                                End Using

                                                                Using cmd As New SQLiteCommand("INSERT INTO Preferiti (Path, DateAdded, Duration, CategoryId, ChiaveWG) VALUES (@path, @date, @dur, @cat, @chiaveWG) ON CONFLICT(Path) DO UPDATE SET CategoryId = @cat;", conn)
                                                                    cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                                    cmd.Parameters.AddWithValue("@date", DateTime.Now)
                                                                    cmd.Parameters.AddWithValue("@dur", durata)
                                                                    cmd.Parameters.AddWithValue("@cat", categoryId)
                                                                    cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                                                                    Await cmd.ExecuteNonQueryAsync()
                                                                End Using
                                                            Next
                                                            transaction.Commit()
                                                        
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
                                                            transaction.Rollback()
                                                            Throw
                                                        End Try
                                                    End Using
                                                End Function)

            For Each normalizedPath As String In percorsiNormalizzati
                Try
                    Dim meta As MetadatiBrano = Await GetMetadatiDaDBAsync(normalizedPath)
                    If meta Is Nothing Then
                        Dim termineRicerca As String = System.IO.Path.GetFileNameWithoutExtension(normalizedPath)
                        Await ScaricaEArchiviaMetadatiAsync(normalizedPath, termineRicerca)
                    End If
                Catch ex As Exception
                    log.ScriviLog($"SpostaBraniInCategoriaAsync - Errore recupero metadati per {normalizedPath}: {ex.Message}")
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function GestisciCategoriaAsync(azione As String, nome As String, Optional id As Long = 0) As Task
        Try
            Await AttendiInizializzazioneAsync()
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand(conn)
                                                        Select Case azione.ToLower()
                                                            Case "aggiungi"
                                                                cmd.CommandText = "INSERT OR IGNORE INTO Categorie (Nome, ChiaveWG) VALUES (@nome, @chiaveWG)"
                                                                cmd.Parameters.AddWithValue("@nome", nome)
                                                                cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Categorie"))
                                                                Await cmd.ExecuteNonQueryAsync()
                                                            Case "modifica"
                                                                cmd.CommandText = "UPDATE OR IGNORE Categorie SET Nome = @nome WHERE Id = @id"
                                                                cmd.Parameters.AddWithValue("@nome", nome)
                                                                cmd.Parameters.AddWithValue("@id", id)
                                                                Await cmd.ExecuteNonQueryAsync()
                                                            Case "elimina"
                                                                If id > 0 Then
                                                                    cmd.CommandText = "UPDATE Preferiti SET CategoryId = 0 WHERE CategoryId = @id"
                                                                    cmd.Parameters.AddWithValue("@id", id)
                                                                    Await cmd.ExecuteNonQueryAsync()

                                                                    cmd.CommandText = "DELETE FROM Categorie WHERE Id = @id"
                                                                    Await cmd.ExecuteNonQueryAsync()
                                                                End If
                                                        End Select
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function GetContaBraniPreferitiAsync(Optional categoryId As Long = -1, Optional searchQuery As String = "") As Task(Of Long)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Dim sql As New StringBuilder()
                                                   sql.Append("SELECT Count(1) FROM Preferiti P ")
                                                   If Not String.IsNullOrEmpty(searchQuery) Then
                                                       sql.Append("LEFT JOIN Metadati M ON P.Path = M.Path ")
                                                   End If
                                                   sql.Append("WHERE 1=1 ")

                                                   If categoryId >= 0 Then
                                                       sql.Append("AND P.CategoryId = @catId ")
                                                   End If

                                                   If Not String.IsNullOrEmpty(searchQuery) Then
                                                       sql.Append("AND (P.Path LIKE @search OR M.Titolo LIKE @search OR M.Artista LIKE @search OR M.Album LIKE @search OR M.Genere LIKE @search OR M.Country LIKE @search OR M.Explicit LIKE @search) ")
                                                   End If

                                                   Using cmd As New SQLiteCommand(sql.ToString(), conn)
                                                       If categoryId >= 0 Then
                                                           cmd.Parameters.AddWithValue("@catId", categoryId)
                                                       End If
                                                       If Not String.IsNullOrEmpty(searchQuery) Then
                                                           cmd.Parameters.AddWithValue("@search", "%" & searchQuery & "%")
                                                       End If
                                                       Return Convert.ToInt64(Await cmd.ExecuteScalarAsync())
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Public Async Function GetContaBraniRecentiAsync() As Task(Of Long)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Count(1) FROM Sessioni", conn)
                                                       Return Convert.ToInt64(Await cmd.ExecuteScalarAsync())
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Public Async Function GetContaCategorieAsync() As Task(Of Long)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Count(1) FROM Categorie", conn)
                                                       Return Convert.ToInt64(Await cmd.ExecuteScalarAsync())
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Public Async Function GetPreferitiPaginatiAsync(pagina As Integer, elementiPerPagina As Integer, orderBy As OrdinamentoPreferiti, Optional categoryId As Long = -1, Optional searchQuery As String = "") As Task(Of List(Of Brano))
        Dim risultati As New List(Of Brano)
        Try
            Await AttendiInizializzazioneAsync()
            If pagina < 1 Then pagina = 1
            Dim offset As Integer = (pagina - 1) * elementiPerPagina

            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Dim query As New StringBuilder()
                                                   query.Append("SELECT P.Path, C.Nome as CatNome, M.Titolo, M.Artista, M.Album, M.Genere FROM Preferiti P ")
                                                   query.Append("LEFT JOIN Categorie C ON P.CategoryId = C.Id ")
                                                   query.Append("LEFT JOIN Metadati M ON P.Path = M.Path ")
                                                   query.Append("WHERE 1=1 ")

                                                   If categoryId >= 0 Then
                                                       query.Append("AND P.CategoryId = @catId ")
                                                   End If

                                                   If Not String.IsNullOrEmpty(searchQuery) Then
                                                       query.Append("AND (P.Path LIKE @search OR M.Titolo LIKE @search OR M.Artista LIKE @search OR M.Album LIKE @search OR M.Genere LIKE @search OR M.Country LIKE @search OR M.Explicit LIKE @search) ")
                                                   End If

                                                   Select Case orderBy
                                                       Case OrdinamentoPreferiti.Alfabetico
                                                           query.Append("ORDER BY COALESCE(M.Titolo, P.Path) ASC ")
                                                       Case OrdinamentoPreferiti.DataAggiunta
                                                           query.Append("ORDER BY P.DateAdded DESC ")
                                                       Case OrdinamentoPreferiti.Durata
                                                           query.Append("ORDER BY P.Duration ASC ")
                                                       Case OrdinamentoPreferiti.Categoria
                                                           query.Append("ORDER BY C.Nome ASC, COALESCE(M.Titolo, P.Path) ASC ")
                                                       Case Else
                                                           query.Append("ORDER BY P.Path ASC ")
                                                   End Select

                                                   query.Append("LIMIT @limit OFFSET @offset")

                                                   Using cmd As New SQLiteCommand(query.ToString(), conn)
                                                       If categoryId >= 0 Then
                                                           cmd.Parameters.AddWithValue("@catId", categoryId)
                                                       End If
                                                       If Not String.IsNullOrEmpty(searchQuery) Then
                                                           cmd.Parameters.AddWithValue("@search", "%" & searchQuery & "%")
                                                       End If
                                                       cmd.Parameters.AddWithValue("@limit", elementiPerPagina)
                                                       cmd.Parameters.AddWithValue("@offset", offset)

                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of Brano)
                                                           While Await reader.ReadAsync()
                                                               Dim nomeVisualizzato As String = System.IO.Path.GetFileNameWithoutExtension(reader("Path").ToString())
                                                               Dim titolo As String = If(IsDBNull(reader("Titolo")), "", reader("Titolo").ToString())
                                                               Dim artista As String = If(IsDBNull(reader("Artista")), "", reader("Artista").ToString())

                                                               If Not String.IsNullOrEmpty(titolo) AndAlso Not String.IsNullOrEmpty(artista) Then
                                                                   nomeVisualizzato = $"{artista} - {titolo}"
                                                               ElseIf Not String.IsNullOrEmpty(titolo) Then
                                                                   nomeVisualizzato = titolo
                                                               End If

                                                               localList.Add(New Brano With {
                                                                   .Nome = nomeVisualizzato,
                                                                   .Path = reader("Path").ToString(),
                                                                   .Categoria = If(IsDBNull(reader("CatNome")), "Generale", reader("CatNome").ToString())
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return risultati
    End Function

    Public Async Function GetRecentiPaginatiAsync(pagina As Integer, elementiPerPagina As Integer, orderBy As OrdinamentoRecenti) As Task(Of List(Of Brano))
        Dim risultati As New List(Of Brano)
        Try
            Await AttendiInizializzazioneAsync()
            If pagina < 1 Then pagina = 1
            Dim offset As Integer = (pagina - 1) * elementiPerPagina

            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Dim query As String = "SELECT Path FROM Sessioni"
                                                   Select Case orderBy
                                                       Case OrdinamentoRecenti.Alfabetico
                                                           query &= " ORDER BY Path ASC"
                                                       Case Else
                                                           query &= " ORDER BY LastPlayed DESC"
                                                   End Select
                                                   query &= " LIMIT @limit OFFSET @offset"

                                                   Using cmd As New SQLiteCommand(query, conn)
                                                       cmd.Parameters.AddWithValue("@limit", elementiPerPagina)
                                                       cmd.Parameters.AddWithValue("@offset", offset)
                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of Brano)
                                                           While Await reader.ReadAsync()
                                                               localList.Add(New Brano With {
                                                                   .Nome = System.IO.Path.GetFileNameWithoutExtension(reader("Path").ToString()),
                                                                   .Path = reader("Path").ToString()
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return risultati
    End Function

    Public Async Function GetCategoriePaginateAsync(pagina As Integer, elementiPerPagina As Integer) As Task(Of List(Of CategoriaBrano))
        Dim lista As New List(Of CategoriaBrano)
        Try
            Await AttendiInizializzazioneAsync()
            If pagina < 1 Then pagina = 1
            Dim offset As Integer = (pagina - 1) * elementiPerPagina

            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Id, Nome FROM Categorie ORDER BY Nome LIMIT @limit OFFSET @offset", conn)
                                                       cmd.Parameters.AddWithValue("@limit", elementiPerPagina)
                                                       cmd.Parameters.AddWithValue("@offset", offset)
                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of CategoriaBrano)
                                                           While Await reader.ReadAsync()
                                                               localList.Add(New CategoriaBrano With {
                                                                   .Id = Convert.ToInt64(reader("Id")),
                                                                   .Nome = reader("Nome").ToString()
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return lista
    End Function

    Public Async Function GetCategorieAsync() As Task(Of List(Of CategoriaBrano))
        Dim lista As New List(Of CategoriaBrano)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Id, Nome FROM Categorie ORDER BY Nome", conn)
                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of CategoriaBrano)
                                                           While Await reader.ReadAsync()
                                                               localList.Add(New CategoriaBrano With {
                                                                   .Id = Convert.ToInt64(reader("Id")),
                                                                   .Nome = reader("Nome").ToString()
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return lista
    End Function

    Private Function GetMediaDurationPropertyStore(percorso As String) As Long
try
        If Not System.IO.File.Exists(percorso) Then Return 0
        Dim propertyStore As IPropertyStore = Nothing
        Dim durationKey As New PROPERTYKEY() With {.fmtid = PKEY_Media_Duration_FMTID, .pid = PKEY_Media_Duration_PID}
        Dim propVar As New PropVariant()
        Dim duration As Long = 0

        Try
            Dim hr As Integer = SHGetPropertyStoreFromParsingName(percorso, IntPtr.Zero, STGM_READ, IID_IPropertyStore, propertyStore)
            If hr = S_OK AndAlso propertyStore IsNot Nothing Then
                hr = propertyStore.GetValue(durationKey, propVar)
                If hr = S_OK AndAlso propVar.vt = 20 Then
                    duration = propVar.lVal
                    duration = duration \ 10000
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        Finally
            If propertyStore IsNot Nothing Then
                Marshal.ReleaseComObject(propertyStore)
            End If
        End Try
        Return duration
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
end try

    End Function

    Private Function GetMediaDuration(percorso As String) As Long
try
        If percorso.ToLower().StartsWith("http") OrElse percorso.ToLower().StartsWith("smb:") Then Return 0
        If Not System.IO.File.Exists(percorso) Then Return 0

        Dim duration As Long = GetMediaDurationPropertyStore(percorso)
        If duration > 0 Then Return duration

        Dim shell As Object = Nothing
        Dim folder As Object = Nothing
        Dim fileItem As Object = Nothing

        Try
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"))
            folder = shell.NameSpace(System.IO.Path.GetDirectoryName(percorso))
            fileItem = folder.ParseName(System.IO.Path.GetFileName(percorso))

            If fileItem IsNot Nothing Then
                Dim durataStringa As String = folder.GetDetailsOf(fileItem, 27)
                If Not String.IsNullOrEmpty(durataStringa) AndAlso durataStringa.Contains(":") Then
                    Dim parti() As String = durataStringa.Split(":")
                    If parti.Length = 3 Then
                        Dim ore As Integer = 0
                        Dim minuti As Integer = 0
                        Dim secondi As Integer = 0
                        If Integer.TryParse(parti(0), ore) AndAlso Integer.TryParse(parti(1), minuti) AndAlso Integer.TryParse(parti(2), secondi) Then
                            Dim totaleSecondi As Long = ore * 3600 + minuti * 60 + secondi
                            duration = totaleSecondi * 1000
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        Finally
            If fileItem IsNot Nothing AndAlso Marshal.IsComObject(fileItem) Then Marshal.ReleaseComObject(fileItem)
            If folder IsNot Nothing AndAlso Marshal.IsComObject(folder) Then Marshal.ReleaseComObject(folder)
            If shell IsNot Nothing AndAlso Marshal.IsComObject(shell) Then Marshal.ReleaseComObject(shell)
        End Try

        Return duration
catch ex as exception
log.ScriviLog(Ex.message & ex.StackTrace)
end try
    End Function

    Public Async Function AggiungiBranoRecenteAsync(percorso As String) As Task
        Try
            Await AttendiInizializzazioneAsync()
            Await SalvaSessioneAsync(percorso, 0)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function EliminaBranoRecenteAsync(path As String) As Task
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(path) Then Return
            Dim normalizedPath As String = NormalizzaPercorso(path)
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("DELETE FROM Sessioni WHERE Path = @path", conn)
                                                        cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function RimuoviTuttiIPreferitiAsync() As Task
        Try
            Await AttendiInizializzazioneAsync()
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("DELETE FROM Preferiti", conn)
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function EliminaTuttiIBraniRecentiAsync() As Task
        Try
            Await AttendiInizializzazioneAsync()
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("DELETE FROM Sessioni", conn)
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function GetListaPreferitiAsync(orderBy As OrdinamentoPreferiti) As Task(Of List(Of Brano))
        Dim risultati As New List(Of Brano)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Dim query As String = "SELECT P.Path, C.Nome as CatNome FROM Preferiti P LEFT JOIN Categorie C ON P.CategoryId = C.Id"
                                                   Select Case orderBy
                                                       Case OrdinamentoPreferiti.Alfabetico
                                                           query &= " ORDER BY P.Path ASC"
                                                       Case OrdinamentoPreferiti.DataAggiunta
                                                           query &= " ORDER BY P.DateAdded DESC"
                                                       Case OrdinamentoPreferiti.Durata
                                                           query &= " ORDER BY P.Duration ASC"
                                                       Case OrdinamentoPreferiti.Categoria
                                                           query &= " ORDER BY C.Nome ASC, P.Path ASC"
                                                       Case Else
                                                           query &= " ORDER BY P.Path ASC"
                                                   End Select

                                                   Using cmd As New SQLiteCommand(query, conn)
                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of Brano)
                                                           While Await reader.ReadAsync()
                                                               localList.Add(New Brano With {
                                                                   .Nome = System.IO.Path.GetFileNameWithoutExtension(reader("Path").ToString()),
                                                                   .Path = reader("Path").ToString(),
                                                                   .Categoria = If(IsDBNull(reader("CatNome")), "Generale", reader("CatNome").ToString())
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return risultati
    End Function

    Public Async Function GetListaRecentiAsync(orderBy As OrdinamentoRecenti) As Task(Of List(Of Brano))
        Dim risultati As New List(Of Brano)
        Try
            Await AttendiInizializzazioneAsync()
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Dim query As String = "SELECT Path FROM Sessioni"
                                                   Select Case orderBy
                                                       Case OrdinamentoRecenti.Alfabetico
                                                           query &= " ORDER BY Path ASC"
                                                       Case Else
                                                           query &= " ORDER BY LastPlayed DESC"
                                                   End Select

                                                   Using cmd As New SQLiteCommand(query, conn)
                                                       Using reader As SQLiteDataReader = Await cmd.ExecuteReaderAsync()
                                                           Dim localList As New List(Of Brano)
                                                           While Await reader.ReadAsync()
                                                               localList.Add(New Brano With {
                                                                   .Nome = System.IO.Path.GetFileNameWithoutExtension(reader("Path").ToString()),
                                                                   .Path = reader("Path").ToString()
                                                               })
                                                           End While
                                                           Return localList
                                                       End Using
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
        Return risultati
    End Function

    Public Async Function SalvaSessioneAsync(percorso As String, position As Long) As Task
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(percorso) Then Return
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Await ExecuteNonQueryWithRetryAsync(Async Function(conn)
                                                    Using cmd As New SQLiteCommand("INSERT INTO Sessioni (Path, Position, LastPlayed, ChiaveWG) VALUES (@path, @pos, @last, @chiaveWG) ON CONFLICT(Path) DO UPDATE SET Position = @pos, LastPlayed = @last;", conn)
                                                        cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                        cmd.Parameters.AddWithValue("@pos", position)
                                                        cmd.Parameters.AddWithValue("@last", DateTime.Now)
                                                        cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Sessioni"))
                                                        Await cmd.ExecuteNonQueryAsync()
                                                    End Using
                                                End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Function

    Public Async Function GetUltimaPosizioneAsync(percorso As String) As Task(Of Long)
        Try
            Await AttendiInizializzazioneAsync()
            If String.IsNullOrEmpty(percorso) Then Return 0
            Dim normalizedPath As String = NormalizzaPercorso(percorso)
            Return Await ExecuteWithRetryAsync(Async Function(conn)
                                                   Using cmd As New SQLiteCommand("SELECT Position FROM Sessioni WHERE Path = @path", conn)
                                                       cmd.Parameters.AddWithValue("@path", normalizedPath)
                                                       Dim result = Await cmd.ExecuteScalarAsync()
                                                       If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                                                           Return Convert.ToInt64(result)
                                                       End If
                                                       Return 0
                                                   End Using
                                               End Function)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Private Function NormalizzaPercorso(path As String) As String
        Try
            If String.IsNullOrWhiteSpace(path) Then Return ""
            If path.StartsWith("WPD|", StringComparison.OrdinalIgnoreCase) Then Return path
            If path.ToLowerInvariant().StartsWith("http") Then Return path
            Return System.IO.Path.GetFullPath(path)
        Catch ex As Exception
            log.ScriviLog($"NormalizzaPercorso - Errore: {ex.Message}")
            Return path
        End Try
    End Function

    Private Function CalculateSha1(filePath As String) As String
        If filePath.StartsWith("smb:") OrElse filePath.ToLower().StartsWith("http") Then Return Nothing
        If Not System.IO.File.Exists(filePath) Then Return Nothing
        Try
            Using sha1 As SHA1 = SHA1.Create()
                Using stream As System.IO.FileStream = System.IO.File.OpenRead(filePath)
                    Dim hash As Byte() = sha1.ComputeHash(stream)
                    Return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If _disposed Then Return

        If disposing Then
            If _httpClient IsNot Nothing Then
                _httpClient.Dispose()
            End If
            If _dbLock IsNot Nothing Then
                _dbLock.Dispose()
            End If
        End If

        _disposed = True
    End Sub
End Class