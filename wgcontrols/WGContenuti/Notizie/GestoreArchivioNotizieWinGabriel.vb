Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

Public Class GestoreArchivioNotizieWinGabriel
    Private Shared ReadOnly _lock As New Object()

    Private Shared Function OttieniPercorsoDatabase() As String
        Try
            Dim percorso As String = WGDb.WGDatabase.PercorsoFileDati("notizie", "notizie.sqlite")
            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch
        End Try
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "notizie", "notizie.sqlite")
    End Function

    Private Shared Function OttieniPercorsoDatabaseVecchio() As String
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NOME_FILE_DATABASE_NOTIZIE)
    End Function

    Private Shared Sub AssicuraCartellaDatabase()
        Try
            WGDb.WGDatabase.CartellaDati("notizie")
        Catch ex As Exception
            ScriviLogAvvio("GestoreArchivioNotizieWinGabriel.AssicuraCartellaDatabase eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub MigraDatabaseVecchioSeNecessario()
        Try
            Dim percorsoNuovo As String = OttieniPercorsoDatabase()

            If File.Exists(percorsoNuovo) Then
                Return
            End If

            Dim percorsoVecchio As String = OttieniPercorsoDatabaseVecchio()

            If Not File.Exists(percorsoVecchio) Then
                Return
            End If

            AssicuraCartellaDatabase()
            File.Move(percorsoVecchio, percorsoNuovo)
        Catch ex As Exception
            log.ScriviLog("GestoreArchivioNotizieWinGabriel.MigraDatabaseVecchioSeNecessario eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function OttieniConnessione() As SQLiteConnection
        AssicuraCartellaDatabase()
        MigraDatabaseVecchioSeNecessario()

        Dim connessione As SQLiteConnection = WGDb.WGDatabase.ApriConnessione(OttieniPercorsoDatabase())

        If connessione Is Nothing Then
            Throw New InvalidOperationException("Impossibile aprire il database delle notizie.")
        End If

        Return connessione
    End Function

    Public Shared Sub InizializzaDatabase()
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand(connessione)
                    comando.CommandText = "CREATE TABLE IF NOT EXISTS NotizieLette (IdArticolo TEXT PRIMARY KEY, Titolo TEXT, Url TEXT, DataLettura TEXT)"
                    comando.ExecuteNonQuery()
                End Using

                Using comando As New SQLiteCommand(connessione)
                    comando.CommandText = "CREATE TABLE IF NOT EXISTS NotizieDaLeggere (IdArticolo TEXT PRIMARY KEY, Titolo TEXT, Testo TEXT, Url TEXT, DataTrovata TEXT)"
                    comando.ExecuteNonQuery()
                End Using

                Using comando As New SQLiteCommand(connessione)
                    comando.CommandText = "CREATE TABLE IF NOT EXISTS ConfigurazioneNotizie (Chiave TEXT PRIMARY KEY, Valore TEXT)"
                    comando.ExecuteNonQuery()
                End Using

                Try
                    WGDb.WGDatabase.AssicuraColonnaChiave(connessione, "NotizieLette")
                    WGDb.WGDatabase.AssicuraColonnaChiave(connessione, "NotizieDaLeggere")
                Catch exChiave As Exception
                    log.ScriviLog("GestoreArchivioNotizieWinGabriel.InizializzaDatabase AssicuraColonnaChiave: " & exChiave.Message)
                End Try
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.InizializzaDatabase eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function OttieniIdArticoliLetti() As HashSet(Of String)
        Dim risultato As New HashSet(Of String)

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("SELECT IdArticolo FROM NotizieLette", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                risultato.Add(lettore.GetString(0))
                            Catch exRiga As Exception
                                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniIdArticoliLetti (riga): " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniIdArticoliLetti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock

        Return risultato
    End Function

    Public Shared Function OttieniIdArticoliConosciuti() As HashSet(Of String)
        Dim risultato As New HashSet(Of String)

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("SELECT IdArticolo FROM NotizieLette UNION SELECT IdArticolo FROM NotizieDaLeggere", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                risultato.Add(lettore.GetString(0))
                            Catch exRiga As Exception
                                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniIdArticoliConosciuti (riga): " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniIdArticoliConosciuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock

        Return risultato
    End Function

    Public Shared Sub SalvaNotizieDaLeggere(articoli As List(Of ArticoloNotiziaWinGabriel))
        If articoli Is Nothing OrElse articoli.Count = 0 Then Return

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                For Each articolo In articoli
                    Try
                        If String.IsNullOrEmpty(articolo.IdArticolo) Then Continue For

                        Using comando As New SQLiteCommand("INSERT OR IGNORE INTO NotizieDaLeggere (IdArticolo, Titolo, Testo, Url, DataTrovata, ChiaveWG) VALUES (@id, @titolo, @testo, @url, @data, @chiavewg)", connessione)
                            comando.Parameters.AddWithValue("@id", articolo.IdArticolo)
                            comando.Parameters.AddWithValue("@titolo", articolo.Titolo)
                            comando.Parameters.AddWithValue("@testo", articolo.Testo)
                            comando.Parameters.AddWithValue("@url", articolo.Url)
                            comando.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            comando.Parameters.AddWithValue("@chiavewg", WGDb.WGDatabase.GeneraChiave())
                            comando.ExecuteNonQuery()
                        End Using
                    Catch exArticolo As Exception
                        log.ScriviLog("GestoreArchivioNotizieWinGabriel.SalvaNotizieDaLeggere (articolo " & articolo.IdArticolo & "): " & exArticolo.Message & Environment.NewLine & exArticolo.StackTrace)
                    End Try
                Next
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.SalvaNotizieDaLeggere eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function OttieniNotizieDaLeggere() As List(Of ArticoloNotiziaWinGabriel)
        Dim risultato As New List(Of ArticoloNotiziaWinGabriel)

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("SELECT IdArticolo, Titolo, Testo, Url FROM NotizieDaLeggere ORDER BY DataTrovata DESC", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim articolo As New ArticoloNotiziaWinGabriel()
                                articolo.IdArticolo = lettore.GetString(0)
                                articolo.Titolo = lettore.GetString(1)
                                articolo.Testo = lettore.GetString(2)
                                articolo.Url = lettore.GetString(3)
                                risultato.Add(articolo)
                            Catch exRiga As Exception
                                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere (riga): " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock

        Return risultato
    End Function

    Public Shared Sub SegnaNotiziaComeLetta(articolo As ArticoloNotiziaWinGabriel)
        If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.IdArticolo) Then Return

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("DELETE FROM NotizieDaLeggere WHERE IdArticolo = @id", connessione)
                    comando.Parameters.AddWithValue("@id", articolo.IdArticolo)
                    comando.ExecuteNonQuery()
                End Using

                Dim chiaveWGEsistente As String = ""
                Using comandoChiave As New SQLiteCommand("SELECT ChiaveWG FROM NotizieLette WHERE IdArticolo = @id", connessione)
                    comandoChiave.Parameters.AddWithValue("@id", articolo.IdArticolo)
                    Dim risultatoChiave As Object = comandoChiave.ExecuteScalar()
                    If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                        chiaveWGEsistente = Convert.ToString(risultatoChiave)
                    End If
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDb.WGDatabase.GeneraChiaveUnivocaPerTabella(connessione, "NotizieLette")

                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO NotizieLette (IdArticolo, Titolo, Url, DataLettura, ChiaveWG) VALUES (@id, @titolo, @url, @data, @chiavewg)", connessione)
                    comando.Parameters.AddWithValue("@id", articolo.IdArticolo)
                    comando.Parameters.AddWithValue("@titolo", articolo.Titolo)
                    comando.Parameters.AddWithValue("@url", articolo.Url)
                    comando.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    comando.Parameters.AddWithValue("@chiavewg", chiaveWGEsistente)
                    comando.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.SegnaNotiziaComeLetta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function OttieniDataUltimoAvviso() As String
        Dim valoreRestituito As String = ""

        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("SELECT Valore FROM ConfigurazioneNotizie WHERE Chiave = 'ultima_data_avviso'", connessione)
                    Dim risultato As Object = comando.ExecuteScalar()
                    If risultato IsNot Nothing Then valoreRestituito = risultato.ToString()
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.OttieniDataUltimoAvviso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock

        Return valoreRestituito
    End Function

    Public Shared Sub SalvaDataUltimoAvviso(data As String)
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()

                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO ConfigurazioneNotizie (Chiave, Valore) VALUES ('ultima_data_avviso', @valore)", connessione)
                    comando.Parameters.AddWithValue("@valore", data)
                    comando.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                log.ScriviLog("GestoreArchivioNotizieWinGabriel.SalvaDataUltimoAvviso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Dispose()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function SelezionaNuove(elenco As List(Of ArticoloNotiziaWinGabriel)) As List(Of ArticoloNotiziaWinGabriel)
        Dim nuove As New List(Of ArticoloNotiziaWinGabriel)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return nuove

            Dim conosciuti As HashSet(Of String) = OttieniIdArticoliConosciuti()
            Dim primaVerifica As Boolean = False

            SyncLock _lock
                Dim connessione As SQLiteConnection = Nothing
                Try
                    InizializzaDatabase()
                    connessione = OttieniConnessione()

                    Dim impostata As Boolean = False
                    Using comando As New SQLiteCommand("SELECT Valore FROM ConfigurazioneNotizie WHERE Chiave = 'prima_verifica'", connessione)
                        Dim risultato As Object = comando.ExecuteScalar()
                        impostata = risultato IsNot Nothing AndAlso Not IsDBNull(risultato) AndAlso Not String.IsNullOrWhiteSpace(Convert.ToString(risultato))
                    End Using

                    If Not impostata Then
                        primaVerifica = conosciuti.Count = 0
                        Using trans As SQLiteTransaction = connessione.BeginTransaction()
                            If primaVerifica Then
                                For Each articolo In elenco
                                    If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.IdArticolo) Then Continue For
                                    Using comando As New SQLiteCommand("INSERT OR IGNORE INTO NotizieLette (IdArticolo, Titolo, Url, DataLettura, ChiaveWG) VALUES (@id, @titolo, @url, @data, @chiavewg)", connessione, trans)
                                        comando.Parameters.AddWithValue("@id", articolo.IdArticolo)
                                        comando.Parameters.AddWithValue("@titolo", articolo.Titolo)
                                        comando.Parameters.AddWithValue("@url", articolo.Url)
                                        comando.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                                        comando.Parameters.AddWithValue("@chiavewg", WGDb.WGDatabase.GeneraChiave())
                                        comando.ExecuteNonQuery()
                                    End Using
                                Next
                            End If
                            Using comando As New SQLiteCommand("INSERT OR REPLACE INTO ConfigurazioneNotizie (Chiave, Valore) VALUES ('prima_verifica', @valore)", connessione, trans)
                                comando.Parameters.AddWithValue("@valore", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                                comando.ExecuteNonQuery()
                            End Using
                            trans.Commit()
                        End Using
                    End If
                Catch ex As Exception
                    log.ScriviLog("GestoreArchivioNotizieWinGabriel.SelezionaNuove (prima verifica) eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                Finally
                    Try
                        If connessione IsNot Nothing Then connessione.Dispose()
                    Catch
                    End Try
                End Try
            End SyncLock

            If primaVerifica Then Return nuove

            For Each articolo In elenco
                If articolo Is Nothing Then Continue For
                If Not String.IsNullOrEmpty(articolo.IdArticolo) AndAlso Not conosciuti.Contains(articolo.IdArticolo) Then nuove.Add(articolo)
            Next
        Catch ex As Exception
            log.ScriviLog("GestoreArchivioNotizieWinGabriel.SelezionaNuove eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return nuove
    End Function
End Class
