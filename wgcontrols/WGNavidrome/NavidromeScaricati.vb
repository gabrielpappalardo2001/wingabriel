Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports wgcontrols
Imports WGDb

''' <summary>Un brano scaricato da Navidrome e conservato sul computer.</summary>
Public Class BranoScaricato
    Public Property Id As Long
    Public Property IdServer As String = ""
    Public Property BranoId As String = ""
    Public Property Titolo As String = ""
    Public Property Artista As String = ""
    Public Property Album As String = ""
    Public Property Genere As String = ""
    Public Property Anno As Integer
    Public Property Traccia As Integer
    Public Property Disco As Integer
    Public Property DurataSecondi As Integer
    Public Property Formato As String = ""
    Public Property Dimensione As Long
    Public Property Percorso As String = ""
    Public Property DataScaricamento As DateTime

    Public ReadOnly Property Descrizione As String
        Get
            Dim s As String = Titolo
            If Artista <> "" Then s &= ", " & Artista
            If Album <> "" Then s &= ", " & Album
            Dim d As String = NavidromeJson.Durata(DurataSecondi)
            If d <> "" Then s &= ", " & d
            Return s
        End Get
    End Property

    ''' <summary>Il nome letto dal lettore: titolo e artista.</summary>
    Public ReadOnly Property Nome As String
        Get
            If Artista = "" Then Return Titolo
            Return Titolo & ", " & Artista
        End Get
    End Property
End Class

''' <summary>
''' La libreria "Scaricati": l'elenco dei brani scaricati da Navidrome, in un database SQLite nei dati di
''' WinGabriel (cartella Navidrome, file Scaricati.sqlite). I file stanno nella cartella Navidrome\Scaricati,
''' divisi per artista e album.
''' </summary>
Public Class NavidromeScaricati

    Private Const SOTTOCARTELLA As String = "Navidrome"
    Private Const NOME_FILE As String = "Scaricati.sqlite"
    Private Const NOME_CARTELLA_MUSICA As String = "Scaricati"

    Private ReadOnly _percorso As String
    Private ReadOnly _log As New Logger()

    Public Sub New()
        _percorso = WGDatabase.PercorsoFileDati(SOTTOCARTELLA, NOME_FILE)
        Crea()
    End Sub

    Private Sub Crea()
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return
                Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Scaricati (" &
                        "Id INTEGER PRIMARY KEY AUTOINCREMENT, IdServer TEXT NOT NULL, BranoId TEXT NOT NULL, " &
                        "Titolo TEXT, Artista TEXT, Album TEXT, Genere TEXT, Anno INTEGER, Traccia INTEGER, Disco INTEGER, " &
                        "Durata INTEGER, Formato TEXT, Dimensione INTEGER, Percorso TEXT NOT NULL, Data TEXT NOT NULL, " &
                        "UNIQUE (IdServer, BranoId))", c)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.Crea: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>La cartella dove finiscono i file scaricati.</summary>
    Public Shared Function CartellaMusica() As String
        Dim c As String = Path.Combine(WGDatabase.CartellaDati(SOTTOCARTELLA), NOME_CARTELLA_MUSICA)
        If Not Directory.Exists(c) Then Directory.CreateDirectory(c)
        Return c
    End Function

    ''' <summary>Dove salvare un brano: Scaricati\Artista\Album\Traccia - Titolo.formato.</summary>
    Public Shared Function PercorsoPer(b As NavidromeBrano) As String
        Dim artista As String = PulisciNome(If(b.Artista <> "", b.Artista, "Artista sconosciuto"))
        Dim album As String = PulisciNome(If(b.Album <> "", b.Album, "Album sconosciuto"))
        Dim titolo As String = PulisciNome(If(b.Titolo <> "", b.Titolo, b.Id))
        If b.Traccia > 0 Then titolo = b.Traccia.ToString("00", CultureInfo.InvariantCulture) & " - " & titolo
        Dim estensione As String = PulisciNome(If(b.Formato <> "", b.Formato, "mp3")).Trim("."c)
        Dim cartella As String = Path.Combine(CartellaMusica(), artista, album)
        Dim percorso As String = Path.Combine(cartella, titolo & "." & estensione)
        ' Due brani diversi con lo stesso nome nella stessa cartella: il secondo prende l'identificativo.
        If File.Exists(percorso) Then percorso = Path.Combine(cartella, titolo & " [" & PulisciNome(b.Id) & "]." & estensione)
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

    ''' <summary>Vero se il brano è già stato scaricato e il file c'è ancora.</summary>
    Public Function EGiaScaricato(idServer As String, idBrano As String) As Boolean
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return False
                Using cmd As New SQLiteCommand("SELECT Percorso FROM Scaricati WHERE IdServer = @s AND BranoId = @b", c)
                    cmd.Parameters.AddWithValue("@s", idServer)
                    cmd.Parameters.AddWithValue("@b", idBrano)
                    Dim v As Object = cmd.ExecuteScalar()
                    Return v IsNot Nothing AndAlso File.Exists(v.ToString())
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.EGiaScaricato: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>Vero se c'è almeno un brano scaricato il cui file esiste ancora (controllo leggero, per il menu).</summary>
    Public Function CiSono() As Boolean
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return False
                Using cmd As New SQLiteCommand("SELECT Percorso FROM Scaricati", c)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            If File.Exists(Testo(r, "Percorso")) Then Return True
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.CiSono: " & ex.ToString())
        End Try
        Return False
    End Function

    Public Sub Registra(idServer As String, b As NavidromeBrano, percorso As String)
        Try
            Dim dimensione As Long = 0
            Try
                dimensione = New FileInfo(percorso).Length
            Catch
            End Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Scaricati (IdServer, BranoId, Titolo, Artista, Album, Genere, Anno, Traccia, Disco, Durata, Formato, Dimensione, Percorso, Data) " &
                                               "VALUES (@s, @b, @ti, @ar, @al, @ge, @an, @tr, @di, @du, @fo, @si, @pe, @da)", c)
                    cmd.Parameters.AddWithValue("@s", idServer)
                    cmd.Parameters.AddWithValue("@b", b.Id)
                    cmd.Parameters.AddWithValue("@ti", b.Titolo)
                    cmd.Parameters.AddWithValue("@ar", b.Artista)
                    cmd.Parameters.AddWithValue("@al", b.Album)
                    cmd.Parameters.AddWithValue("@ge", b.Genere)
                    cmd.Parameters.AddWithValue("@an", b.Anno)
                    cmd.Parameters.AddWithValue("@tr", b.Traccia)
                    cmd.Parameters.AddWithValue("@di", b.Disco)
                    cmd.Parameters.AddWithValue("@du", b.DurataSecondi)
                    cmd.Parameters.AddWithValue("@fo", b.Formato)
                    cmd.Parameters.AddWithValue("@si", dimensione)
                    cmd.Parameters.AddWithValue("@pe", percorso)
                    cmd.Parameters.AddWithValue("@da", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.Registra: " & ex.ToString())
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' I brani scaricati, in ordine di artista, album e traccia. Quelli il cui file è stato cancellato
    ''' a mano dal computer vengono tolti dall'elenco.
    ''' </summary>
    Public Function Elenco() As List(Of BranoScaricato)
        Dim risultato As New List(Of BranoScaricato)
        Dim mancanti As New List(Of Long)
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return risultato
                Using cmd As New SQLiteCommand("SELECT * FROM Scaricati ORDER BY Artista COLLATE NOCASE, Album COLLATE NOCASE, Disco, Traccia, Titolo COLLATE NOCASE", c)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim b As New BranoScaricato()
                            b.Id = Numero(r, "Id")
                            b.IdServer = Testo(r, "IdServer")
                            b.BranoId = Testo(r, "BranoId")
                            b.Titolo = Testo(r, "Titolo")
                            b.Artista = Testo(r, "Artista")
                            b.Album = Testo(r, "Album")
                            b.Genere = Testo(r, "Genere")
                            b.Anno = CInt(Numero(r, "Anno"))
                            b.Traccia = CInt(Numero(r, "Traccia"))
                            b.Disco = CInt(Numero(r, "Disco"))
                            b.DurataSecondi = CInt(Numero(r, "Durata"))
                            b.Formato = Testo(r, "Formato")
                            b.Dimensione = Numero(r, "Dimensione")
                            b.Percorso = Testo(r, "Percorso")
                            Dim d As DateTime
                            If DateTime.TryParse(Testo(r, "Data"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, d) Then b.DataScaricamento = d
                            If File.Exists(b.Percorso) Then risultato.Add(b) Else mancanti.Add(b.Id)
                        End While
                    End Using
                End Using
                For Each id As Long In mancanti
                    Using cmd As New SQLiteCommand("DELETE FROM Scaricati WHERE Id = @i", c)
                        cmd.Parameters.AddWithValue("@i", id)
                        cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.Elenco: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>Toglie il brano dall'elenco e cancella il file dal computer.</summary>
    Public Sub Elimina(b As BranoScaricato)
        Try
            Try
                If File.Exists(b.Percorso) Then File.Delete(b.Percorso)
            Catch exFile As Exception
                _log.ScriviLog("NavidromeScaricati.Elimina file: " & exFile.ToString())
                Throw
            End Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return
                Using cmd As New SQLiteCommand("DELETE FROM Scaricati WHERE Id = @i", c)
                    cmd.Parameters.AddWithValue("@i", b.Id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            PulisciCartelleVuote(Path.GetDirectoryName(b.Percorso))
        Catch ex As Exception
            _log.ScriviLog("NavidromeScaricati.Elimina: " & ex.ToString())
            Throw
        End Try
    End Sub

    ''' <summary>Toglie le cartelle rimaste vuote (album, poi artista), senza mai uscire da Scaricati.</summary>
    Private Shared Sub PulisciCartelleVuote(cartella As String)
        Try
            Dim radice As String = CartellaMusica().TrimEnd(Path.DirectorySeparatorChar)
            Dim corrente As String = cartella
            While Not String.IsNullOrEmpty(corrente) AndAlso corrente.StartsWith(radice & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                If Directory.Exists(corrente) AndAlso Directory.GetFileSystemEntries(corrente).Length = 0 Then
                    Directory.Delete(corrente)
                    corrente = Path.GetDirectoryName(corrente)
                Else
                    Exit While
                End If
            End While
        Catch
        End Try
    End Sub

    Private Shared Function Testo(r As SQLiteDataReader, colonna As String) As String
        Dim v As Object = r(colonna)
        Return If(v Is Nothing OrElse v Is DBNull.Value, "", v.ToString())
    End Function

    Private Shared Function Numero(r As SQLiteDataReader, colonna As String) As Long
        Dim v As Object = r(colonna)
        If v Is Nothing OrElse v Is DBNull.Value Then Return 0
        Return Convert.ToInt64(v, CultureInfo.InvariantCulture)
    End Function

End Class
