Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb

' Gestione del catalogo e dei download dei libri audio da LibriVox, con ricerca per autore e lingua e gestione delle ultime
' aggiunte.
Public Enum OrdinamentoLibri
    AlfabeticoAZ
    PiuRecente
    MenoRecente
End Enum

Public Class DatiElenco
    Public Property Id As String
    Public Property Titolo As String

    Public Sub New(idVal As String, titoloVal As String)
        Me.Id = idVal
        Me.Titolo = titoloVal
    End Sub
End Class

Public Class DettagliLibro
    Public Property Titolo As String
    Public Property Autore As String
    Public Property DataPubblicazione As String
    Public Property NumeroCapitoli As Integer
    Public Property Scaricato As Boolean
End Class

Public Class CLSLibriVox
    Private Const CARTELLA_DATI_LIBRI As String = "dati\LibriVox"
    Private Const NOME_FILE_DB As String = "db.sqlite"
    Private Const MASSIMO_RICERCHE_RECENTI As Integer = 50
    Private ReadOnly loggerIstanza As wgcontrols.Logger

    Public Sub New()
        loggerIstanza = New wgcontrols.Logger()
        Try
            WGBackup.RegistraOrigine("LibriVox", Path.Combine(Application.StartupPath, CARTELLA_DATI_LIBRI))
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore registrazione origine backup LibriVox: " & ex.Message)
        End Try
    End Sub

    Private ReadOnly Property ConnectionString As String
        Get
            Try
                Dim pathDb As String = Path.Combine(Application.StartupPath, CARTELLA_DATI_LIBRI, NOME_FILE_DB)
                Return "Data Source=" & pathDb & ";Version=3;"
            Catch ex As Exception
                loggerIstanza.ScriviLog("Errore risoluzione percorso database libri: " & ex.Message)
                Return "Data Source=" & Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_LIBRI, NOME_FILE_DB) & ";Version=3;"
            End Try
        End Get
    End Property

    Private Function GetSqlOrder(ord As OrdinamentoLibri, Optional tablePrefix As String = "") As String
        Dim colTitolo As String = If(String.IsNullOrEmpty(tablePrefix), "Titolo", tablePrefix & ".Titolo")
        Dim colId As String = If(String.IsNullOrEmpty(tablePrefix), "id", tablePrefix & ".id")

        Select Case ord
            Case OrdinamentoLibri.AlfabeticoAZ
                Return $" ORDER BY {colTitolo} ASC"
            Case OrdinamentoLibri.PiuRecente
                Return $" ORDER BY CAST({colId} AS INTEGER) DESC"
            Case OrdinamentoLibri.MenoRecente
                Return $" ORDER BY CAST({colId} AS INTEGER) ASC"
            Case Else
                Return $" ORDER BY {colTitolo} ASC"
        End Select
    End Function

    Private Function GetSqlOrderUltime(ord As OrdinamentoLibri) As String
        Select Case ord
            Case OrdinamentoLibri.AlfabeticoAZ
                Return " ORDER BY Titolo ASC"
            Case OrdinamentoLibri.PiuRecente
                Return " ORDER BY DataInserimento DESC"
            Case OrdinamentoLibri.MenoRecente
                Return " ORDER BY DataInserimento ASC"
            Case Else
                Return " ORDER BY DataInserimento DESC"
        End Select
    End Function

    Public Sub AvviaAggiornamentoCatalogo()
        Try
            Dim centroControllo As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centroControllo IsNot Nothing Then
                centroControllo.AggiornaCatalogoLibriVox()
            End If
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore avvio aggiornamento catalogo: " & ex.Message)
        End Try
    End Sub

    Public Sub AvviaDownloadLibri(idsLibri As List(Of String), cartellaDestinazione As String)
        Try
            Dim centroControllo As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centroControllo IsNot Nothing Then
                centroControllo.ScaricaLibriLibriVox(idsLibri, cartellaDestinazione)
            End If
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore avvio download libri: " & ex.Message)
        End Try
    End Sub

    Public Sub AvviaEliminazioneLibri(idsLibri As List(Of String))
        Try
            Dim centroControllo As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centroControllo IsNot Nothing Then
                centroControllo.EliminaLibri(idsLibri)
            End If
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore avvio eliminazione libri: " & ex.Message)
        End Try
    End Sub

    Public Function GetDettagliLibro(idLibro As String) As DettagliLibro
        Dim dettagli As New DettagliLibro()
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()

                Dim sqlInfo As String = "SELECT l.Titolo, l.Autore, u.DataInserimento " &
                                        "FROM Libri l " &
                                        "LEFT JOIN UltimeAggiunte u ON l.id = u.IdLibro " &
                                        "WHERE l.id = @id"

                Using cmd As SQLiteCommand = New SQLiteCommand(sqlInfo, conn)
                    cmd.Parameters.AddWithValue("@id", idLibro)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            dettagli.Titolo = reader("Titolo").ToString()
                            dettagli.Autore = reader("Autore").ToString()
                            If Not IsDBNull(reader("DataInserimento")) Then
                                dettagli.DataPubblicazione = Convert.ToDateTime(reader("DataInserimento")).ToString("dd/MM/yyyy")
                            Else
                                dettagli.DataPubblicazione = "Non disponibile"
                            End If
                        Else
                            dettagli.Titolo = "Sconosciuto"
                            dettagli.Autore = "Sconosciuto"
                            dettagli.DataPubblicazione = "N/D"
                        End If
                    End Using
                End Using

                Dim sqlCaps As String = "SELECT COUNT(*) FROM CapitoliScaricati WHERE IdLibro = @id"
                Using cmdCaps As SQLiteCommand = New SQLiteCommand(sqlCaps, conn)
                    cmdCaps.Parameters.AddWithValue("@id", idLibro)
                    Dim count As Long = Convert.ToInt64(cmdCaps.ExecuteScalar())
                    dettagli.NumeroCapitoli = CInt(count)
                    dettagli.Scaricato = (count > 0)
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero dettagli libro: " & ex.Message)
            dettagli.Titolo = "Errore"
        End Try
        Return dettagli
    End Function

    Public Function CountLibriScaricati() As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM LibriScaricati"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio libri scaricati: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetElencoLibriScaricati(ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Dim orderBy As String = ""
            Select Case ordinamento
                Case OrdinamentoLibri.AlfabeticoAZ : orderBy = " ORDER BY Titolo ASC"
                Case OrdinamentoLibri.PiuRecente : orderBy = " ORDER BY CAST(IdLibro AS INTEGER) DESC"
                Case OrdinamentoLibri.MenoRecente : orderBy = " ORDER BY CAST(IdLibro AS INTEGER) ASC"
            End Select

            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT IdLibro, Titolo, Autore FROM LibriScaricati" & orderBy & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("IdLibro").ToString()
                            Dim titolo As String = reader("Titolo").ToString()
                            Dim autore As String = reader("Autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero libri scaricati: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountCatalogoCompleto() As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM Libri"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio catalogo completo: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetCatalogoCompleto(ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT id, titolo, autore FROM Libri" & GetSqlOrder(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("id").ToString()
                            Dim titolo As String = reader("titolo").ToString()
                            Dim autore As String = reader("autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero catalogo completo: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountElencoAutori() As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(DISTINCT autore) FROM Libri WHERE autore IS NOT NULL AND autore != ''"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio autori: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetElencoAutori(ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim orderBy As String = " ORDER BY autore ASC"
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT DISTINCT autore FROM Libri WHERE autore IS NOT NULL AND autore != ''" & orderBy & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim autore As String = reader("autore").ToString()
                            lista.Add(New DatiElenco(autore, autore))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero elenco autori: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountAutoriPerLingua(lingua As String) As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(DISTINCT autore) FROM Libri WHERE lingua = @l AND autore IS NOT NULL AND autore != ''"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@l", lingua)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio autori per lingua: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetAutoriPerLingua(lingua As String, ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim orderBy As String = " ORDER BY autore ASC"
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT DISTINCT autore FROM Libri WHERE lingua = @l AND autore IS NOT NULL AND autore != ''" & orderBy & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@l", lingua)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim autore As String = reader("autore").ToString()
                            lista.Add(New DatiElenco(autore, autore))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero autori per lingua: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountElencoLingue() As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(DISTINCT lingua) FROM Libri WHERE lingua IS NOT NULL AND lingua != ''"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio lingue: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetElencoLingue(Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT DISTINCT lingua FROM Libri WHERE lingua IS NOT NULL AND lingua != '' ORDER BY lingua ASC LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim lingua As String = reader("lingua").ToString()
                            lista.Add(New DatiElenco(lingua, lingua))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero elenco lingue: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountRicercaNelCatalogo(termine As String) As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM Libri WHERE titolo LIKE @q OR autore LIKE @q"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@q", "%" & termine & "%")
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio ricerca catalogo: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function RicercaNelCatalogo(termine As String, ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT id, titolo, autore FROM Libri WHERE titolo LIKE @q OR autore LIKE @q" & GetSqlOrder(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@q", "%" & termine & "%")
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("id").ToString()
                            Dim titolo As String = reader("titolo").ToString()
                            Dim autore As String = reader("autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore ricerca nel catalogo: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountRicercaPerAutore(autore As String) As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM Libri WHERE autore LIKE @a"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@a", "%" & autore & "%")
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio ricerca autore: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function RicercaPerAutore(autore As String, ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT id, titolo, autore FROM Libri WHERE autore LIKE @a" & GetSqlOrder(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@a", "%" & autore & "%")
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("id").ToString()
                            Dim titolo As String = reader("titolo").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore ricerca per autore: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountLibriPerLingua(lingua As String) As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM Libri WHERE lingua = @l"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@l", lingua)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio libri per lingua: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetLibriPerLingua(lingua As String, ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 100) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT id, titolo, autore FROM Libri WHERE lingua = @l" & GetSqlOrder(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@l", lingua)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("id").ToString()
                            Dim titolo As String = reader("titolo").ToString()
                            Dim autore As String = reader("autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore libri per lingua: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function GetCapitoliLibroScaricato(idLibro As String) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT Id, TitoloCapitolo FROM CapitoliScaricati WHERE IdLibro = @id ORDER BY Id ASC"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@id", idLibro)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("Id").ToString()
                            Dim titolo As String = reader("TitoloCapitolo").ToString()
                            lista.Add(New DatiElenco(id, titolo))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero capitoli scaricati: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function GetPercorsoFileCapitolo(idCapitolo As Integer) As String
        Dim percorso As String = ""
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT PercorsoFile FROM CapitoliScaricati WHERE Id = @id"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@id", idCapitolo)
                    Dim result As Object = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        percorso = result.ToString()
                    End If
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero percorso file capitolo: " & ex.Message)
        End Try
        Return percorso
    End Function

    Public Function GetPercorsiFileCapitoli(idsCapitoli As List(Of Integer)) As List(Of String)
        Dim listaPercorsi As List(Of String) = New List(Of String)()
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                For Each id As Integer In idsCapitoli
                    Dim sql As String = "SELECT PercorsoFile FROM CapitoliScaricati WHERE Id = @id"
                    Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                        cmd.Parameters.AddWithValue("@id", id)
                        Dim result As Object = cmd.ExecuteScalar()
                        If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                            listaPercorsi.Add(result.ToString())
                        End If
                    End Using
                Next
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero percorsi file capitoli massivo: " & ex.Message)
        End Try
        Return listaPercorsi
    End Function

    Public Function CountUltimeAggiunte() As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM UltimeAggiunte"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio ultime aggiunte: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetUltimeAggiunte(ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 50) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT IdLibro, Titolo, Autore FROM UltimeAggiunte" & GetSqlOrderUltime(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("IdLibro").ToString()
                            Dim titolo As String = reader("Titolo").ToString()
                            Dim autore As String = reader("Autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero ultime aggiunte: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function CountUltimeAggiuntePerLingua(lingua As String) As Long
        Dim count As Long = 0
        Try
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM UltimeAggiunte WHERE Lingua = @lingua"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@lingua", lingua)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio ultime aggiunte per lingua: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetUltimeAggiuntePerLingua(lingua As String, ordinamento As OrdinamentoLibri, Optional pagina As Integer = 1, Optional quantitaPerPagina As Integer = 50) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                Dim sql As String = "SELECT IdLibro, Titolo, Autore FROM UltimeAggiunte WHERE Lingua = @lingua" & GetSqlOrderUltime(ordinamento) & " LIMIT @limit OFFSET @offset"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@lingua", lingua)
                    cmd.Parameters.AddWithValue("@limit", quantitaPerPagina)
                    cmd.Parameters.AddWithValue("@offset", offset)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim id As String = reader("IdLibro").ToString()
                            Dim titolo As String = reader("Titolo").ToString()
                            Dim autore As String = reader("Autore").ToString()
                            Dim visual As String = titolo & " - " & autore
                            lista.Add(New DatiElenco(id, visual))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero ultime aggiunte per lingua: " & ex.Message)
        End Try
        Return lista
    End Function

    Private Sub AssicuraCartellaDati()
        Try
            Dim cartella As String = Path.Combine(Application.StartupPath, CARTELLA_DATI_LIBRI)
            If Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore creazione cartella dati libri: " & ex.Message)
        End Try
    End Sub

    Private Sub AssicuraTabellaRicercheRecenti(conn As SQLiteConnection)
        Try
            If conn Is Nothing Then Return
            Dim sql As String = "CREATE TABLE IF NOT EXISTS RicercheRecenti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Termine TEXT NOT NULL COLLATE NOCASE UNIQUE, DataRicerca TEXT NOT NULL)"
            Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
            Try
                WGDatabase.AssicuraColonnaChiave(conn, "RicercheRecenti")
            Catch exChiave As Exception
                loggerIstanza.ScriviLog("Errore assicurazione chiave ricerche recenti: " & exChiave.Message)
            End Try
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore creazione tabella ricerche recenti: " & ex.Message)
        End Try
    End Sub

    Private Function GetSqlOrderRicerche(ord As OrdinamentoLibri) As String
        Select Case ord
            Case OrdinamentoLibri.AlfabeticoAZ
                Return " ORDER BY Termine ASC"
            Case OrdinamentoLibri.PiuRecente
                Return " ORDER BY DataRicerca DESC, Id DESC"
            Case OrdinamentoLibri.MenoRecente
                Return " ORDER BY DataRicerca ASC, Id ASC"
            Case Else
                Return " ORDER BY DataRicerca DESC, Id DESC"
        End Select
    End Function

    Public Sub AggiungiRicercaRecente(termine As String)
        Try
            If String.IsNullOrWhiteSpace(termine) Then Return
            Dim testo As String = termine.Trim()
            AssicuraCartellaDati()
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                AssicuraTabellaRicercheRecenti(conn)

                Dim chiaveWGRicerca As String = ""
                Try
                    Using cmdLookup As SQLiteCommand = New SQLiteCommand("SELECT ChiaveWG FROM RicercheRecenti WHERE Termine = @t COLLATE NOCASE", conn)
                        cmdLookup.Parameters.AddWithValue("@t", testo)
                        Dim risultatoLookup As Object = cmdLookup.ExecuteScalar()
                        If risultatoLookup IsNot Nothing AndAlso Not IsDBNull(risultatoLookup) Then
                            chiaveWGRicerca = risultatoLookup.ToString()
                        End If
                    End Using
                Catch exLookup As Exception
                    loggerIstanza.ScriviLog("Errore lookup chiave ricerca recente: " & exLookup.Message)
                End Try
                If String.IsNullOrWhiteSpace(chiaveWGRicerca) Then chiaveWGRicerca = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "RicercheRecenti")

                Using cmdDel As SQLiteCommand = New SQLiteCommand("DELETE FROM RicercheRecenti WHERE Termine = @t COLLATE NOCASE", conn)
                    cmdDel.Parameters.AddWithValue("@t", testo)
                    cmdDel.ExecuteNonQuery()
                End Using

                Using cmdIns As SQLiteCommand = New SQLiteCommand("INSERT INTO RicercheRecenti (Termine, DataRicerca, ChiaveWG) VALUES (@t, @d, @chiaveWG)", conn)
                    cmdIns.Parameters.AddWithValue("@t", testo)
                    cmdIns.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                    cmdIns.Parameters.AddWithValue("@chiaveWG", chiaveWGRicerca)
                    cmdIns.ExecuteNonQuery()
                End Using

                Using cmdTrim As SQLiteCommand = New SQLiteCommand("DELETE FROM RicercheRecenti WHERE Id NOT IN (SELECT Id FROM RicercheRecenti ORDER BY DataRicerca DESC, Id DESC LIMIT @max)", conn)
                    cmdTrim.Parameters.AddWithValue("@max", MASSIMO_RICERCHE_RECENTI)
                    cmdTrim.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore salvataggio ricerca recente: " & ex.Message)
        End Try
    End Sub

    Public Function CountRicercheRecenti() As Long
        Dim count As Long = 0
        Try
            AssicuraCartellaDati()
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                AssicuraTabellaRicercheRecenti(conn)
                Using cmd As SQLiteCommand = New SQLiteCommand("SELECT COUNT(*) FROM RicercheRecenti", conn)
                    count = Convert.ToInt64(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore conteggio ricerche recenti: " & ex.Message)
        End Try
        Return count
    End Function

    Public Function GetRicercheRecenti(Optional ordinamento As OrdinamentoLibri = OrdinamentoLibri.PiuRecente, Optional quantita As Integer = MASSIMO_RICERCHE_RECENTI) As List(Of DatiElenco)
        Dim lista As List(Of DatiElenco) = New List(Of DatiElenco)()
        Try
            Dim limite As Integer = quantita
            If limite <= 0 Then limite = MASSIMO_RICERCHE_RECENTI
            AssicuraCartellaDati()
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                AssicuraTabellaRicercheRecenti(conn)
                Dim sql As String = "SELECT Termine FROM RicercheRecenti" & GetSqlOrderRicerche(ordinamento) & " LIMIT @limit"
                Using cmd As SQLiteCommand = New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@limit", limite)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim termine As String = reader("Termine").ToString()
                            lista.Add(New DatiElenco(termine, termine))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore recupero ricerche recenti: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Function EliminaRicercaRecente(termine As String) As Boolean
        Dim esito As Boolean = False
        Try
            If String.IsNullOrWhiteSpace(termine) Then Return False
            AssicuraCartellaDati()
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                AssicuraTabellaRicercheRecenti(conn)
                Using cmd As SQLiteCommand = New SQLiteCommand("DELETE FROM RicercheRecenti WHERE Termine = @t COLLATE NOCASE", conn)
                    cmd.Parameters.AddWithValue("@t", termine.Trim())
                    esito = (cmd.ExecuteNonQuery() > 0)
                End Using
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore eliminazione ricerca recente: " & ex.Message)
        End Try
        Return esito
    End Function

    Public Function SvuotaRicercheRecenti() As Boolean
        Dim esito As Boolean = False
        Try
            AssicuraCartellaDati()
            Using conn As SQLiteConnection = New SQLiteConnection(ConnectionString)
                conn.Open()
                AssicuraTabellaRicercheRecenti(conn)
                Using cmd As SQLiteCommand = New SQLiteCommand("DELETE FROM RicercheRecenti", conn)
                    cmd.ExecuteNonQuery()
                End Using
                Try
                    Using cmdSeq As SQLiteCommand = New SQLiteCommand("DELETE FROM sqlite_sequence WHERE name = 'RicercheRecenti'", conn)
                        cmdSeq.ExecuteNonQuery()
                    End Using
                Catch exSeq As Exception
                    loggerIstanza.ScriviLog("Avviso reset sequenza ricerche recenti: " & exSeq.Message)
                End Try
                esito = True
            End Using
        Catch ex As Exception
            loggerIstanza.ScriviLog("Errore svuotamento ricerche recenti: " & ex.Message)
        End Try
        Return esito
    End Function

End Class