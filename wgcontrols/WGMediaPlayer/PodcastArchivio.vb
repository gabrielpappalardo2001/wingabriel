Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports wgcontrols
Imports WGDb

''' <summary>Un episodio ascoltato di recente.</summary>
Friend Class AscoltoPodcast
    Public Property FeedUrl As String = ""
    Public Property Guid As String = ""
    Public Property Titolo As String = ""
    Public Property Podcast As String = ""
    Public Property Url As String = ""
    Public Property DataEpisodio As DateTime? = Nothing
    Public Property DataAscolto As DateTime = DateTime.Now
End Class

''' <summary>Un episodio scaricato sul computer.</summary>
Friend Class EpisodioScaricato
    Public Property FeedUrl As String = ""
    Public Property Guid As String = ""
    Public Property Titolo As String = ""
    Public Property Podcast As String = ""
    Public Property Percorso As String = ""
    Public Property Dimensione As Long = 0
    Public Property DurataSecondi As Integer = 0
    Public Property DataEpisodio As DateTime? = Nothing
    Public Property DataScaricamento As DateTime = DateTime.Now

    Public ReadOnly Property Etichetta As String
        Get
            Dim s As String = Titolo & ", " & Podcast
            If DataEpisodio.HasValue Then s &= ", " & DataEpisodio.Value.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"))
            Dim d As String = PodcastRete.DurataParlata(DurataSecondi)
            If d <> "" Then s &= ", " & d
            Return s
        End Get
    End Property
End Class

''' <summary>
''' I podcast seguiti e preferiti e gli episodi ascoltati, in un database nei dati di WinGabriel (cartella podcast): si sincronizza
''' con il cloud come gli altri dati. Gli episodi scaricati hanno un database a parte, accanto ai file (podcast\episodi), perché i
''' percorsi sono di questo computer e i file non si sincronizzano.
''' </summary>
Friend Class PodcastArchivio

    Private Const SOTTOCARTELLA As String = "Podcast"
    Private Const MASSIMO_RECENTI As Integer = 200

    Private ReadOnly _percorso As String
    Private ReadOnly _percorsoScaricati As String
    Private ReadOnly _log As New Logger()
    Private Shared ReadOnly _blocco As New Object()

    Public Sub New()
        _percorso = WGDatabase.PercorsoFileDati(SOTTOCARTELLA, "Podcast.sqlite")
        _percorsoScaricati = Path.Combine(CartellaEpisodi(), "Scaricati.sqlite")
        Crea()
    End Sub

    ''' <summary>La cartella dei file scaricati: podcast\episodi.</summary>
    Public Shared Function CartellaEpisodi() As String
        Dim c As String = Path.Combine(WGDatabase.CartellaDati(SOTTOCARTELLA), "episodi")
        If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Return c
    End Function

    Private Sub Crea()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return
                    Esegui(c, "CREATE TABLE IF NOT EXISTS Podcast (FeedUrl TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Descrizione TEXT, Sito TEXT, Immagine TEXT, " &
                              "Categoria TEXT, Preferito INTEGER NOT NULL DEFAULT 0, Seguito INTEGER NOT NULL DEFAULT 0, Data TEXT)")
                    Esegui(c, "CREATE TABLE IF NOT EXISTS Ascolti (FeedUrl TEXT NOT NULL, Guid TEXT NOT NULL, Titolo TEXT, Podcast TEXT, Url TEXT, DataEpisodio TEXT, " &
                              "DataAscolto TEXT NOT NULL, PRIMARY KEY (FeedUrl, Guid))")
                End Using
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorsoScaricati)
                    If c Is Nothing Then Return
                    Esegui(c, "CREATE TABLE IF NOT EXISTS Scaricati (FeedUrl TEXT NOT NULL, Guid TEXT NOT NULL, Titolo TEXT, Podcast TEXT, Percorso TEXT NOT NULL, " &
                              "Dimensione INTEGER, Durata INTEGER, DataEpisodio TEXT, DataScaricamento TEXT NOT NULL, PRIMARY KEY (FeedUrl, Guid))")
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Crea: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub Esegui(c As SQLiteConnection, comando As String)
        Using cmd As New SQLiteCommand(comando, c)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Function Testo(valore As Object) As String
        If valore Is Nothing OrElse valore Is DBNull.Value Then Return ""
        Return valore.ToString()
    End Function

    Private Shared Function InTesto(d As DateTime?) As Object
        If d.HasValue Then Return d.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        Return DBNull.Value
    End Function

    Private Shared Function DaTesto(valore As Object) As DateTime?
        Dim t As String = Testo(valore)
        Dim d As DateTime
        If t <> "" AndAlso DateTime.TryParseExact(t, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Return d
        Return Nothing
    End Function

    ' ---- podcast seguiti e preferiti ------------------------------------------------------------------

    ''' <summary>Salva le informazioni del podcast, senza toccare se è seguito o preferito.</summary>
    Private Sub Salva(c As SQLiteConnection, p As PodcastInfo)
        Using cmd As New SQLiteCommand("INSERT INTO Podcast (FeedUrl, Titolo, Autore, Descrizione, Sito, Immagine, Categoria, Data) VALUES (@f, @t, @a, @d, @s, @i, @c, @data) " &
                                       "ON CONFLICT(FeedUrl) DO UPDATE SET Titolo = excluded.Titolo, Autore = excluded.Autore, " &
                                       "Descrizione = CASE WHEN excluded.Descrizione <> '' THEN excluded.Descrizione ELSE Descrizione END, " &
                                       "Sito = CASE WHEN excluded.Sito <> '' THEN excluded.Sito ELSE Sito END, " &
                                       "Immagine = CASE WHEN excluded.Immagine <> '' THEN excluded.Immagine ELSE Immagine END, " &
                                       "Categoria = CASE WHEN excluded.Categoria <> '' THEN excluded.Categoria ELSE Categoria END", c)
            cmd.Parameters.AddWithValue("@f", p.FeedUrl)
            cmd.Parameters.AddWithValue("@t", p.Titolo)
            cmd.Parameters.AddWithValue("@a", p.Autore)
            cmd.Parameters.AddWithValue("@d", p.Descrizione)
            cmd.Parameters.AddWithValue("@s", p.Sito)
            cmd.Parameters.AddWithValue("@i", p.Immagine)
            cmd.Parameters.AddWithValue("@c", p.Categoria)
            cmd.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Sub ImpostaFlag(p As PodcastInfo, colonna As String, valore As Boolean)
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return
                    Salva(c, p)
                    Using cmd As New SQLiteCommand("UPDATE Podcast SET " & colonna & " = @v WHERE FeedUrl = @f", c)
                        cmd.Parameters.AddWithValue("@v", If(valore, 1, 0))
                        cmd.Parameters.AddWithValue("@f", p.FeedUrl)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.ImpostaFlag (" & colonna & "): " & ex.ToString())
        End Try
    End Sub

    Public Sub ImpostaSeguito(p As PodcastInfo, seguito As Boolean)
        ImpostaFlag(p, "Seguito", seguito)
    End Sub

    Public Sub ImpostaPreferito(p As PodcastInfo, preferito As Boolean)
        ImpostaFlag(p, "Preferito", preferito)
    End Sub

    ''' <summary>Gli indirizzi dei podcast che hanno quel flag (Seguito o Preferito).</summary>
    Private Function Indirizzi(colonna As String) As HashSet(Of String)
        Dim insieme As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return insieme
                    Using cmd As New SQLiteCommand("SELECT FeedUrl FROM Podcast WHERE " & colonna & " = 1", c)
                        Using r As SQLiteDataReader = cmd.ExecuteReader()
                            While r.Read()
                                insieme.Add(Testo(r(0)))
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Indirizzi (" & colonna & "): " & ex.ToString())
        End Try
        Return insieme
    End Function

    Public Function IndirizziSeguiti() As HashSet(Of String)
        Return Indirizzi("Seguito")
    End Function

    Public Function IndirizziPreferiti() As HashSet(Of String)
        Return Indirizzi("Preferito")
    End Function

    Private Function Elenco(colonna As String) As List(Of PodcastInfo)
        Dim lista As New List(Of PodcastInfo)()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return lista
                    Using cmd As New SQLiteCommand("SELECT FeedUrl, Titolo, Autore, Descrizione, Sito, Immagine, Categoria FROM Podcast WHERE " & colonna & " = 1 ORDER BY Titolo COLLATE NOCASE", c)
                        Using r As SQLiteDataReader = cmd.ExecuteReader()
                            While r.Read()
                                Dim p As New PodcastInfo()
                                p.FeedUrl = Testo(r(0))
                                p.Titolo = Testo(r(1))
                                p.Autore = Testo(r(2))
                                p.Descrizione = Testo(r(3))
                                p.Sito = Testo(r(4))
                                p.Immagine = Testo(r(5))
                                p.Categoria = Testo(r(6))
                                lista.Add(p)
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Elenco (" & colonna & "): " & ex.ToString())
        End Try
        Return lista
    End Function

    Public Function Seguiti() As List(Of PodcastInfo)
        Return Elenco("Seguito")
    End Function

    Public Function Preferiti() As List(Of PodcastInfo)
        Return Elenco("Preferito")
    End Function

    Public Function CiSonoSeguiti() As Boolean
        Return IndirizziSeguiti().Count > 0
    End Function

    ' ---- episodi ascoltati ---------------------------------------------------------------------------

    Public Sub RegistraAscolto(p As PodcastInfo, ep As EpisodioPodcast)
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return
                    Using cmd As New SQLiteCommand("INSERT INTO Ascolti (FeedUrl, Guid, Titolo, Podcast, Url, DataEpisodio, DataAscolto) VALUES (@f, @g, @t, @p, @u, @de, @da) " &
                                                   "ON CONFLICT(FeedUrl, Guid) DO UPDATE SET DataAscolto = excluded.DataAscolto, Titolo = excluded.Titolo, Podcast = excluded.Podcast, Url = excluded.Url", c)
                        cmd.Parameters.AddWithValue("@f", p.FeedUrl)
                        cmd.Parameters.AddWithValue("@g", ep.Guid)
                        cmd.Parameters.AddWithValue("@t", ep.Titolo)
                        cmd.Parameters.AddWithValue("@p", p.Titolo)
                        cmd.Parameters.AddWithValue("@u", ep.Url)
                        cmd.Parameters.AddWithValue("@de", InTesto(ep.Data))
                        cmd.Parameters.AddWithValue("@da", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.RegistraAscolto: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Gli episodi già ascoltati di quel podcast (i loro identificativi).</summary>
    Public Function Ascoltati(feedUrl As String) As HashSet(Of String)
        Dim insieme As New HashSet(Of String)()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return insieme
                    Using cmd As New SQLiteCommand("SELECT Guid FROM Ascolti WHERE FeedUrl = @f", c)
                        cmd.Parameters.AddWithValue("@f", feedUrl)
                        Using r As SQLiteDataReader = cmd.ExecuteReader()
                            While r.Read()
                                insieme.Add(Testo(r(0)))
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Ascoltati: " & ex.ToString())
        End Try
        Return insieme
    End Function

    Public Function Recenti() As List(Of AscoltoPodcast)
        Dim lista As New List(Of AscoltoPodcast)()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return lista
                    Using cmd As New SQLiteCommand("SELECT FeedUrl, Guid, Titolo, Podcast, Url, DataEpisodio, DataAscolto FROM Ascolti ORDER BY DataAscolto DESC LIMIT " & MASSIMO_RECENTI.ToString(), c)
                        Using r As SQLiteDataReader = cmd.ExecuteReader()
                            While r.Read()
                                Dim a As New AscoltoPodcast()
                                a.FeedUrl = Testo(r(0))
                                a.Guid = Testo(r(1))
                                a.Titolo = Testo(r(2))
                                a.Podcast = Testo(r(3))
                                a.Url = Testo(r(4))
                                a.DataEpisodio = DaTesto(r(5))
                                a.DataAscolto = If(DaTesto(r(6)), DateTime.Now)
                                lista.Add(a)
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Recenti: " & ex.ToString())
        End Try
        Return lista
    End Function

    Public Sub TogliDaiRecenti(feedUrl As String, guid As String)
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return
                    Using cmd As New SQLiteCommand("DELETE FROM Ascolti WHERE FeedUrl = @f AND Guid = @g", c)
                        cmd.Parameters.AddWithValue("@f", feedUrl)
                        cmd.Parameters.AddWithValue("@g", guid)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.TogliDaiRecenti: " & ex.ToString())
        End Try
    End Sub

    Public Sub SvuotaRecenti()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                    If c Is Nothing Then Return
                    Esegui(c, "DELETE FROM Ascolti")
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.SvuotaRecenti: " & ex.ToString())
        End Try
    End Sub

    ' ---- episodi scaricati ----------------------------------------------------------------------------

    ''' <summary>Dove salvare un episodio: podcast\episodi\Podcast\data titolo.estensione.</summary>
    Public Shared Function PercorsoPer(p As PodcastInfo, ep As EpisodioPodcast) As String
        Dim cartella As String = Path.Combine(CartellaEpisodi(), PulisciNome(If(p.Titolo <> "", p.Titolo, "Podcast")))
        Dim nome As String = If(ep.Data.HasValue, ep.Data.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & " ", "") & PulisciNome(ep.Titolo)
        If nome.Length > 100 Then nome = nome.Substring(0, 100).Trim()
        Dim percorso As String = Path.Combine(cartella, nome & PodcastRete.EstensionePer(ep))
        ' due episodi con lo stesso nome nello stesso giorno: il secondo prende un numero
        Dim n As Integer = 2
        While File.Exists(percorso)
            percorso = Path.Combine(cartella, nome & " (" & n.ToString() & ")" & PodcastRete.EstensionePer(ep))
            n += 1
        End While
        Return percorso
    End Function

    Private Shared Function PulisciNome(testo As String) As String
        Dim sb As New StringBuilder()
        Dim vietati As Char() = Path.GetInvalidFileNameChars()
        For Each ch As Char In testo.Trim()
            sb.Append(If(Array.IndexOf(vietati, ch) >= 0, "_"c, ch))
        Next
        Dim s As String = sb.ToString().Trim().TrimEnd("."c).Trim()
        If s.Length > 80 Then s = s.Substring(0, 80).Trim()
        If s = "" Then s = "_"
        Return s
    End Function

    Public Sub RegistraScaricato(p As PodcastInfo, ep As EpisodioPodcast, percorso As String, dimensione As Long)
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorsoScaricati)
                    If c Is Nothing Then Return
                    Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Scaricati (FeedUrl, Guid, Titolo, Podcast, Percorso, Dimensione, Durata, DataEpisodio, DataScaricamento) " &
                                                   "VALUES (@f, @g, @t, @p, @pe, @di, @du, @de, @ds)", c)
                        cmd.Parameters.AddWithValue("@f", p.FeedUrl)
                        cmd.Parameters.AddWithValue("@g", ep.Guid)
                        cmd.Parameters.AddWithValue("@t", ep.Titolo)
                        cmd.Parameters.AddWithValue("@p", p.Titolo)
                        cmd.Parameters.AddWithValue("@pe", percorso)
                        cmd.Parameters.AddWithValue("@di", dimensione)
                        cmd.Parameters.AddWithValue("@du", ep.DurataSecondi)
                        cmd.Parameters.AddWithValue("@de", InTesto(ep.Data))
                        cmd.Parameters.AddWithValue("@ds", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.RegistraScaricato: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Il file scaricato di un episodio, se c'è ancora; altrimenti vuoto.</summary>
    Public Function PercorsoScaricato(feedUrl As String, guid As String) As String
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorsoScaricati)
                    If c Is Nothing Then Return ""
                    Using cmd As New SQLiteCommand("SELECT Percorso FROM Scaricati WHERE FeedUrl = @f AND Guid = @g", c)
                        cmd.Parameters.AddWithValue("@f", feedUrl)
                        cmd.Parameters.AddWithValue("@g", guid)
                        Dim v As Object = cmd.ExecuteScalar()
                        If v IsNot Nothing AndAlso File.Exists(v.ToString()) Then Return v.ToString()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.PercorsoScaricato: " & ex.ToString())
        End Try
        Return ""
    End Function

    ''' <summary>Gli episodi scaricati il cui file esiste ancora, i più recenti per primi.</summary>
    Public Function Scaricati() As List(Of EpisodioScaricato)
        Dim lista As New List(Of EpisodioScaricato)()
        Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorsoScaricati)
                    If c Is Nothing Then Return lista
                    Using cmd As New SQLiteCommand("SELECT FeedUrl, Guid, Titolo, Podcast, Percorso, Dimensione, Durata, DataEpisodio, DataScaricamento FROM Scaricati ORDER BY DataScaricamento DESC", c)
                        Using r As SQLiteDataReader = cmd.ExecuteReader()
                            While r.Read()
                                Dim e As New EpisodioScaricato()
                                e.FeedUrl = Testo(r(0))
                                e.Guid = Testo(r(1))
                                e.Titolo = Testo(r(2))
                                e.Podcast = Testo(r(3))
                                e.Percorso = Testo(r(4))
                                Long.TryParse(Testo(r(5)), e.Dimensione)
                                Integer.TryParse(Testo(r(6)), e.DurataSecondi)
                                e.DataEpisodio = DaTesto(r(7))
                                e.DataScaricamento = If(DaTesto(r(8)), DateTime.Now)
                                If File.Exists(e.Percorso) Then lista.Add(e)
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.Scaricati: " & ex.ToString())
        End Try
        Return lista
    End Function

    Public Function CiSonoScaricati() As Boolean
        Return Scaricati().Count > 0
    End Function

    ''' <summary>Toglie l'episodio dall'elenco e cancella il file.</summary>
    Public Sub EliminaScaricato(feedUrl As String, guid As String, percorso As String)
        Try
            Try
                If percorso <> "" AndAlso File.Exists(percorso) Then File.Delete(percorso)
            Catch ex As Exception
                _log.ScriviLog("PodcastArchivio.EliminaScaricato (file): " & ex.Message)
            End Try
            SyncLock _blocco
                Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorsoScaricati)
                    If c Is Nothing Then Return
                    Using cmd As New SQLiteCommand("DELETE FROM Scaricati WHERE FeedUrl = @f AND Guid = @g", c)
                        cmd.Parameters.AddWithValue("@f", feedUrl)
                        cmd.Parameters.AddWithValue("@g", guid)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PodcastArchivio.EliminaScaricato: " & ex.ToString())
        End Try
    End Sub

End Class
