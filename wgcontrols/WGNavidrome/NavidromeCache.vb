Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports wgcontrols
Imports WGDb

''' <summary>
''' La copia locale dell'elenco di tutti i brani di un server, in un database SQLite nei dati di
''' WinGabriel (cartella Navidrome, file Cache.sqlite). Serve per aprire subito "Tutti i brani" senza
''' riscaricare ogni volta decine di migliaia di righe. Ogni server e utente ha il suo elenco.
''' </summary>
Public Class NavidromeCache

    Private Const SOTTOCARTELLA As String = "Navidrome"
    Private Const NOME_FILE As String = "Cache.sqlite"

    Private ReadOnly _percorso As String
    Private ReadOnly _idServer As String
    Private ReadOnly _log As New Logger()

    Public Sub New(idServer As String)
        _idServer = If(idServer, "")
        _percorso = WGDatabase.PercorsoFileDati(SOTTOCARTELLA, NOME_FILE)
        Crea()
    End Sub

    Private Sub Crea()
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return
                Esegui(c, "CREATE TABLE IF NOT EXISTS Brani (" &
                          "IdServer TEXT NOT NULL, Id TEXT NOT NULL, Titolo TEXT, Artista TEXT, ArtistaId TEXT, " &
                          "Album TEXT, AlbumId TEXT, Genere TEXT, Anno INTEGER, Traccia INTEGER, Disco INTEGER, " &
                          "Durata INTEGER, Formato TEXT, BitRate INTEGER, Dimensione INTEGER, Preferito INTEGER, " &
                          "Voto INTEGER, Ascolti INTEGER, PRIMARY KEY (IdServer, Id))")
                Esegui(c, "CREATE TABLE IF NOT EXISTS Aggiornamenti (IdServer TEXT PRIMARY KEY, Data TEXT NOT NULL)")
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeCache.Crea: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub Esegui(c As SQLiteConnection, sql As String)
        Using cmd As New SQLiteCommand(sql, c)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Quando l'elenco è stato scaricato dal server l'ultima volta; Nothing se non c'è una copia.</summary>
    Public Function DataAggiornamento() As DateTime?
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return Nothing
                Using cmd As New SQLiteCommand("SELECT Data FROM Aggiornamenti WHERE IdServer = @s", c)
                    cmd.Parameters.AddWithValue("@s", _idServer)
                    Dim v As Object = cmd.ExecuteScalar()
                    Dim d As DateTime
                    If v IsNot Nothing AndAlso DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, d) Then Return d
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeCache.DataAggiornamento: " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Public Function LeggiBrani() As List(Of NavidromeBrano)
        Dim risultato As New List(Of NavidromeBrano)
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return risultato
                Using cmd As New SQLiteCommand("SELECT * FROM Brani WHERE IdServer = @s ORDER BY Titolo COLLATE NOCASE, Artista COLLATE NOCASE", c)
                    cmd.Parameters.AddWithValue("@s", _idServer)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim b As New NavidromeBrano()
                            b.Id = Testo(r, "Id")
                            b.Titolo = Testo(r, "Titolo")
                            b.Artista = Testo(r, "Artista")
                            b.ArtistaId = Testo(r, "ArtistaId")
                            b.Album = Testo(r, "Album")
                            b.AlbumId = Testo(r, "AlbumId")
                            b.Genere = Testo(r, "Genere")
                            b.Anno = CInt(Numero(r, "Anno"))
                            b.Traccia = CInt(Numero(r, "Traccia"))
                            b.Disco = CInt(Numero(r, "Disco"))
                            b.DurataSecondi = CInt(Numero(r, "Durata"))
                            b.Formato = Testo(r, "Formato")
                            b.BitRate = CInt(Numero(r, "BitRate"))
                            b.Dimensione = Numero(r, "Dimensione")
                            b.Preferito = Numero(r, "Preferito") <> 0
                            b.Voto = CInt(Numero(r, "Voto"))
                            b.NumeroAscolti = CInt(Numero(r, "Ascolti"))
                            risultato.Add(b)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeCache.LeggiBrani: " & ex.ToString())
            risultato.Clear()
        End Try
        Return risultato
    End Function

    ''' <summary>Sostituisce la copia locale con l'elenco appena scaricato, in un'unica transazione.</summary>
    Public Sub SalvaBrani(brani As List(Of NavidromeBrano))
        Try
            Using c As SQLiteConnection = WGDatabase.ApriConnessione(_percorso)
                If c Is Nothing Then Return
                Using t As SQLiteTransaction = c.BeginTransaction()
                    Using cmd As New SQLiteCommand("DELETE FROM Brani WHERE IdServer = @s", c, t)
                        cmd.Parameters.AddWithValue("@s", _idServer)
                        cmd.ExecuteNonQuery()
                    End Using
                    Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Brani (IdServer, Id, Titolo, Artista, ArtistaId, Album, AlbumId, Genere, Anno, Traccia, Disco, Durata, Formato, BitRate, Dimensione, Preferito, Voto, Ascolti) " &
                                                   "VALUES (@s, @id, @ti, @ar, @arid, @al, @alid, @ge, @an, @tr, @di, @du, @fo, @br, @si, @pr, @vo, @as)", c, t)
                        Dim pId = cmd.Parameters.Add("@id", DbType.String)
                        Dim pTi = cmd.Parameters.Add("@ti", DbType.String)
                        Dim pAr = cmd.Parameters.Add("@ar", DbType.String)
                        Dim pArId = cmd.Parameters.Add("@arid", DbType.String)
                        Dim pAl = cmd.Parameters.Add("@al", DbType.String)
                        Dim pAlId = cmd.Parameters.Add("@alid", DbType.String)
                        Dim pGe = cmd.Parameters.Add("@ge", DbType.String)
                        Dim pAn = cmd.Parameters.Add("@an", DbType.Int32)
                        Dim pTr = cmd.Parameters.Add("@tr", DbType.Int32)
                        Dim pDi = cmd.Parameters.Add("@di", DbType.Int32)
                        Dim pDu = cmd.Parameters.Add("@du", DbType.Int32)
                        Dim pFo = cmd.Parameters.Add("@fo", DbType.String)
                        Dim pBr = cmd.Parameters.Add("@br", DbType.Int32)
                        Dim pSi = cmd.Parameters.Add("@si", DbType.Int64)
                        Dim pPr = cmd.Parameters.Add("@pr", DbType.Int32)
                        Dim pVo = cmd.Parameters.Add("@vo", DbType.Int32)
                        Dim pAs = cmd.Parameters.Add("@as", DbType.Int32)
                        cmd.Parameters.AddWithValue("@s", _idServer)
                        For Each b As NavidromeBrano In brani
                            pId.Value = b.Id
                            pTi.Value = b.Titolo
                            pAr.Value = b.Artista
                            pArId.Value = b.ArtistaId
                            pAl.Value = b.Album
                            pAlId.Value = b.AlbumId
                            pGe.Value = b.Genere
                            pAn.Value = b.Anno
                            pTr.Value = b.Traccia
                            pDi.Value = b.Disco
                            pDu.Value = b.DurataSecondi
                            pFo.Value = b.Formato
                            pBr.Value = b.BitRate
                            pSi.Value = b.Dimensione
                            pPr.Value = If(b.Preferito, 1, 0)
                            pVo.Value = b.Voto
                            pAs.Value = b.NumeroAscolti
                            cmd.ExecuteNonQuery()
                        Next
                    End Using
                    Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Aggiornamenti (IdServer, Data) VALUES (@s, @d)", c, t)
                        cmd.Parameters.AddWithValue("@s", _idServer)
                        cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                        cmd.ExecuteNonQuery()
                    End Using
                    t.Commit()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("NavidromeCache.SalvaBrani: " & ex.ToString())
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
