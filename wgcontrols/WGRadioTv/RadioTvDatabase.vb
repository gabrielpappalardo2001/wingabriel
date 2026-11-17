Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDati
Imports WGDb

' Gestione del database per preferiti e recenti di radio e televisione. Sincronizza le voci della raccolta e gestisce la
' cronologia degli ascolti.
Public Class RadioTvDatabase

#Region "Modelli"

    Public Enum TipoElemento
        Radio
        Tv
    End Enum

    Public Class VoceRaccolta
        Public Property Id As String = ""
        Public Property Nome As String = ""
        Public Property Data As DateTime = DateTime.MinValue

        Public Overrides Function ToString() As String
            Return Nome
        End Function
    End Class

    Private Class ConfrontoVoceNome
        Implements IComparer(Of VoceRaccolta)

        Public Function Confronta(x As VoceRaccolta, y As VoceRaccolta) As Integer Implements IComparer(Of VoceRaccolta).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

#End Region

#Region "Campi"

    Private Shared ReadOnly _lock As New Object()
    Private Shared _istanza As RadioTvDatabase

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _cartellaDati As String
    Private ReadOnly _fileDati As String
    Private ReadOnly _stringaConnessione As String

    Private Const MASSIMO_RECENTI As Integer = 40

#End Region

#Region "Singleton"

    Public Shared Function GetInstance() As RadioTvDatabase
        Try
            If _istanza Is Nothing Then
                SyncLock _lock
                    If _istanza Is Nothing Then
                        _istanza = New RadioTvDatabase()
                    End If
                End SyncLock
            End If
            Return _istanza
        Catch
            Return _istanza
        End Try
    End Function

    Private Sub New()
        Try
            _cartellaDati = RisolviCartellaPreferiti()
            If Not Directory.Exists(_cartellaDati) Then
                Directory.CreateDirectory(_cartellaDati)
            End If
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.New: " & ex.ToString())
            _cartellaDati = Path.GetTempPath()
        End Try

        Try
            _fileDati = Path.Combine(_cartellaDati, "raccolta.db")
            _stringaConnessione = "Data Source=" & _fileDati & ";Version=3;"
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.New percorso: " & ex.ToString())
        End Try

        InizializzaDatabase()
    End Sub

    Private Shared Function RisolviCartellaPreferiti() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("preferiti")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(Application.StartupPath, "dati", "preferiti")
        Catch
            Return Path.GetTempPath()
        End Try
    End Function

    Private Sub InizializzaDatabase()
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(_fileDati) Then Return
                If Not File.Exists(_fileDati) Then
                    SQLiteConnection.CreateFile(_fileDati)
                End If

                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()

                    Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Preferiti (Tipo TEXT NOT NULL, Id TEXT NOT NULL, Nome TEXT NOT NULL, Data TEXT NOT NULL, PRIMARY KEY (Tipo, Id))", connessione)
                        comando.ExecuteNonQuery()
                    End Using

                    Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Recenti (Tipo TEXT NOT NULL, Id TEXT NOT NULL, Nome TEXT NOT NULL, Data TEXT NOT NULL, PRIMARY KEY (Tipo, Id))", connessione)
                        comando.ExecuteNonQuery()
                    End Using

                    Using comando As New SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_recenti_tipo_data ON Recenti (Tipo, Data)", connessione)
                        comando.ExecuteNonQuery()
                    End Using

                    Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS PreferitiEliminati (Tipo TEXT NOT NULL, Id TEXT NOT NULL, Data TEXT NOT NULL, PRIMARY KEY (Tipo, Id))", connessione)
                        comando.ExecuteNonQuery()
                    End Using

                    Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS RecentiEliminati (Tipo TEXT NOT NULL, Id TEXT NOT NULL, Data TEXT NOT NULL, PRIMARY KEY (Tipo, Id))", connessione)
                        comando.ExecuteNonQuery()
                    End Using

                    Try
                        WGDatabase.AssicuraColonnaChiave(connessione, "Preferiti")
                        WGDatabase.AssicuraColonnaChiave(connessione, "Recenti")
                    Catch exChiave As Exception
                        _logger.ScriviLog("RadioTvDatabase.InizializzaDatabase AssicuraColonnaChiave: " & exChiave.ToString())
                    End Try
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.InizializzaDatabase: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

#End Region

#Region "Utilita interne"

    Private Function TipoTesto(tipo As TipoElemento) As String
        Try
            If tipo = TipoElemento.Radio Then Return "radio"
            Return "tv"
        Catch
            Return "radio"
        End Try
    End Function

    Private Function DataAStringa(valore As DateTime) As String
        Try
            Return valore.ToString("o", CultureInfo.InvariantCulture)
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.DataAStringa: " & ex.Message)
            Return DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
        End Try
    End Function

    Private Function StringaAData(valore As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return DateTime.MinValue
            Return DateTime.Parse(valore, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.StringaAData: " & ex.Message)
            Return DateTime.MinValue
        End Try
    End Function

    Private Function ApriConnessione() As SQLiteConnection
        Dim connessione As New SQLiteConnection(_stringaConnessione)
        connessione.Open()
        Return connessione
    End Function

#End Region

#Region "Preferiti"

    Public Function EPreferito(tipo As TipoElemento, id As String) As Boolean
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return False

                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("SELECT COUNT(*) FROM Preferiti WHERE Tipo = @tipo AND Id = @id", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comando.Parameters.AddWithValue("@id", id)
                        Dim conteggio As Long = Convert.ToInt64(comando.ExecuteScalar())
                        Return conteggio > 0
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.EPreferito: " & ex.ToString())
                Return False
            End Try
        End SyncLock
    End Function

    Public Sub AggiungiPreferito(tipo As TipoElemento, id As String, nome As String)
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return

                Dim nomeSalvato As String = nome
                If String.IsNullOrWhiteSpace(nomeSalvato) Then nomeSalvato = id

                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("INSERT OR IGNORE INTO Preferiti (Tipo, Id, Nome, Data, ChiaveWG) VALUES (@tipo, @id, @nome, @data, @chiavewg)", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comando.Parameters.AddWithValue("@id", id)
                        comando.Parameters.AddWithValue("@nome", nomeSalvato)
                        comando.Parameters.AddWithValue("@data", DataAStringa(DateTime.Now))
                        comando.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(connessione, "Preferiti"))
                        comando.ExecuteNonQuery()
                    End Using
                    Using comandoEliminati As New SQLiteCommand("DELETE FROM PreferitiEliminati WHERE Tipo = @tipo AND Id = @id", connessione)
                        comandoEliminati.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comandoEliminati.Parameters.AddWithValue("@id", id)
                        comandoEliminati.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.AggiungiPreferito: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Sub RimuoviPreferito(tipo As TipoElemento, id As String)
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return

                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("DELETE FROM Preferiti WHERE Tipo = @tipo AND Id = @id", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comando.Parameters.AddWithValue("@id", id)
                        comando.ExecuteNonQuery()
                    End Using
                    Using comandoEliminati As New SQLiteCommand("INSERT OR REPLACE INTO PreferitiEliminati (Tipo, Id, Data) VALUES (@tipo, @id, @data)", connessione)
                        comandoEliminati.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comandoEliminati.Parameters.AddWithValue("@id", id)
                        comandoEliminati.Parameters.AddWithValue("@data", DataAStringa(DateTime.Now))
                        comandoEliminati.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.RimuoviPreferito: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Function CommutaPreferito(tipo As TipoElemento, id As String, nome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then Return False

            If EPreferito(tipo, id) Then
                RimuoviPreferito(tipo, id)
                Return False
            End If

            AggiungiPreferito(tipo, id, nome)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.CommutaPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniPreferiti(tipo As TipoElemento) As List(Of VoceRaccolta)
        SyncLock _lock
            Dim risultato As New List(Of VoceRaccolta)
            Try
                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("SELECT Id, Nome, Data FROM Preferiti WHERE Tipo = @tipo", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            While lettore.Read()
                                Dim voce As New VoceRaccolta()
                                voce.Id = Convert.ToString(lettore("Id"))
                                voce.Nome = Convert.ToString(lettore("Nome"))
                                voce.Data = StringaAData(Convert.ToString(lettore("Data")))
                                risultato.Add(voce)
                            End While
                        End Using
                    End Using
                End Using

                risultato.Sort(New ConfrontoVoceNome())
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.OttieniPreferiti: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

#End Region

#Region "Recenti"

    Public Sub AggiungiRecente(tipo As TipoElemento, id As String, nome As String)
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return

                Dim nomeSalvato As String = nome
                If String.IsNullOrWhiteSpace(nomeSalvato) Then nomeSalvato = id

                Using connessione As SQLiteConnection = ApriConnessione()
                    Using transazione As SQLiteTransaction = connessione.BeginTransaction()
                        Try
                            Dim chiaveWGEsistente As String = ""
                            Using comandoChiave As New SQLiteCommand("SELECT ChiaveWG FROM Recenti WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                                comandoChiave.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                                comandoChiave.Parameters.AddWithValue("@id", id)
                                Dim risultatoChiave As Object = comandoChiave.ExecuteScalar()
                                If risultatoChiave IsNot Nothing AndAlso Not IsDBNull(risultatoChiave) Then
                                    chiaveWGEsistente = Convert.ToString(risultatoChiave)
                                End If
                            End Using
                            If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(connessione, "Recenti")

                            Using comandoElimina As New SQLiteCommand("DELETE FROM Recenti WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                                comandoElimina.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                                comandoElimina.Parameters.AddWithValue("@id", id)
                                comandoElimina.ExecuteNonQuery()
                            End Using

                            Using comandoInserisci As New SQLiteCommand("INSERT INTO Recenti (Tipo, Id, Nome, Data, ChiaveWG) VALUES (@tipo, @id, @nome, @data, @chiavewg)", connessione, transazione)
                                comandoInserisci.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                                comandoInserisci.Parameters.AddWithValue("@id", id)
                                comandoInserisci.Parameters.AddWithValue("@nome", nomeSalvato)
                                comandoInserisci.Parameters.AddWithValue("@data", DataAStringa(DateTime.Now))
                                comandoInserisci.Parameters.AddWithValue("@chiavewg", chiaveWGEsistente)
                                comandoInserisci.ExecuteNonQuery()
                            End Using

                            Using comandoEliminati As New SQLiteCommand("DELETE FROM RecentiEliminati WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                                comandoEliminati.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                                comandoEliminati.Parameters.AddWithValue("@id", id)
                                comandoEliminati.ExecuteNonQuery()
                            End Using

                            Using comandoTaglio As New SQLiteCommand("DELETE FROM Recenti WHERE Tipo = @tipo AND Id NOT IN (SELECT Id FROM Recenti WHERE Tipo = @tipo ORDER BY Data DESC LIMIT @massimo)", connessione, transazione)
                                comandoTaglio.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                                comandoTaglio.Parameters.AddWithValue("@massimo", MASSIMO_RECENTI)
                                comandoTaglio.ExecuteNonQuery()
                            End Using

                            transazione.Commit()
                        Catch exInterno As Exception
                            _logger.ScriviLog("RadioTvDatabase.AggiungiRecente transazione: " & exInterno.ToString())
                            Try
                                transazione.Rollback()
                            Catch exRollback As Exception
                                _logger.ScriviLog("RadioTvDatabase.AggiungiRecente rollback: " & exRollback.Message)
                            End Try
                        End Try
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.AggiungiRecente: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Sub RimuoviRecente(tipo As TipoElemento, id As String)
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return

                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("DELETE FROM Recenti WHERE Tipo = @tipo AND Id = @id", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comando.Parameters.AddWithValue("@id", id)
                        comando.ExecuteNonQuery()
                    End Using
                    Using comandoEliminati As New SQLiteCommand("INSERT OR REPLACE INTO RecentiEliminati (Tipo, Id, Data) VALUES (@tipo, @id, @data)", connessione)
                        comandoEliminati.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comandoEliminati.Parameters.AddWithValue("@id", id)
                        comandoEliminati.Parameters.AddWithValue("@data", DataAStringa(DateTime.Now))
                        comandoEliminati.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.RimuoviRecente: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Sub SvuotaRecenti(tipo As TipoElemento)
        SyncLock _lock
            Try
                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comandoEliminati As New SQLiteCommand("INSERT OR REPLACE INTO RecentiEliminati (Tipo, Id, Data) SELECT Tipo, Id, @data FROM Recenti WHERE Tipo = @tipo", connessione)
                        comandoEliminati.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comandoEliminati.Parameters.AddWithValue("@data", DataAStringa(DateTime.Now))
                        comandoEliminati.ExecuteNonQuery()
                    End Using
                    Using comando As New SQLiteCommand("DELETE FROM Recenti WHERE Tipo = @tipo", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        comando.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.SvuotaRecenti: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Function OttieniRecenti(tipo As TipoElemento) As List(Of VoceRaccolta)
        SyncLock _lock
            Dim risultato As New List(Of VoceRaccolta)
            Try
                Using connessione As SQLiteConnection = ApriConnessione()
                    Using comando As New SQLiteCommand("SELECT Id, Nome, Data FROM Recenti WHERE Tipo = @tipo ORDER BY Data DESC", connessione)
                        comando.Parameters.AddWithValue("@tipo", TipoTesto(tipo))
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            While lettore.Read()
                                Dim voce As New VoceRaccolta()
                                voce.Id = Convert.ToString(lettore("Id"))
                                voce.Nome = Convert.ToString(lettore("Nome"))
                                voce.Data = StringaAData(Convert.ToString(lettore("Data")))
                                risultato.Add(voce)
                            End While
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.OttieniRecenti: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

#End Region
#Region "Sincronizzazione file"

    Private Const TIPO_RADIO_SYNC As String = "radio"
    Private Const GIORNI_CONSERVAZIONE_ELIMINATI_SYNC As Integer = 180

    Private Class StatoVoceRadioSync
        Public Property Id As String = ""
        Public Property Nome As String = ""
        Public Property Data As DateTime = DateTime.MinValue
        Public Property Eliminata As Boolean = False
        Public Property ChiaveWG As String = ""
    End Class

    Private Class ConfrontoStatoRadioPerData
        Implements IComparer(Of StatoVoceRadioSync)

        Public Function Confronta(x As StatoVoceRadioSync, y As StatoVoceRadioSync) As Integer Implements IComparer(Of StatoVoceRadioSync).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                Return DateTime.Compare(y.Data, x.Data)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Public Function UnisciPreferitiRadioFile(vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        Try
            Return UnisciRaccoltaRadioFile("Preferiti", "PreferitiEliminati", vociRemote, 0)
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.UnisciPreferitiRadioFile: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function UnisciRecentiRadioFile(vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        Try
            Return UnisciRaccoltaRadioFile("Recenti", "RecentiEliminati", vociRemote, MASSIMO_RECENTI)
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.UnisciRecentiRadioFile: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function UnisciRaccoltaRadioFile(tabella As String, tabellaEliminati As String, vociRemote As List(Of Dictionary(Of String, Object)), massimo As Integer) As List(Of Dictionary(Of String, Object))
        SyncLock _lock
            Try
                If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
                Dim limiteEliminati As DateTime = DateTime.UtcNow.AddDays(-GIORNI_CONSERVAZIONE_ELIMINATI_SYNC)

                Using connessione As SQLiteConnection = ApriConnessione()
                    Dim locali As Dictionary(Of String, StatoVoceRadioSync) = LeggiVociRadioSync(connessione, tabella, False)
                    Dim eliminati As Dictionary(Of String, StatoVoceRadioSync) = LeggiVociRadioSync(connessione, tabellaEliminati, True)
                    Dim remote As Dictionary(Of String, StatoVoceRadioSync) = IndicizzaVociRadioSync(vociRemote)

                    Dim identificativi As New HashSet(Of String)(StringComparer.Ordinal)
                    For Each chiave As String In locali.Keys
                        identificativi.Add(chiave)
                    Next
                    For Each chiave As String In eliminati.Keys
                        identificativi.Add(chiave)
                    Next
                    For Each chiave As String In remote.Keys
                        identificativi.Add(chiave)
                    Next

                    Dim finali As New Dictionary(Of String, StatoVoceRadioSync)(StringComparer.Ordinal)

                    Using transazione As SQLiteTransaction = connessione.BeginTransaction()
                        Try
                            For Each identificativo As String In identificativi
                                Try
                                    Dim locale As StatoVoceRadioSync = Nothing
                                    If Not locali.TryGetValue(identificativo, locale) Then eliminati.TryGetValue(identificativo, locale)
                                    Dim remota As StatoVoceRadioSync = Nothing
                                    remote.TryGetValue(identificativo, remota)

                                    If remota IsNot Nothing AndAlso (locale Is Nothing OrElse remota.Data > locale.Data) Then
                                        ApplicaVoceRadioSync(connessione, transazione, tabella, tabellaEliminati, remota, locali)
                                        finali(identificativo) = remota
                                    ElseIf locale IsNot Nothing Then
                                        finali(identificativo) = locale
                                    End If
                                Catch exVoce As Exception
                                    _logger.ScriviLog("RadioTvDatabase.UnisciRaccoltaRadioFile voce (" & If(identificativo, "") & "): " & exVoce.ToString())
                                End Try
                            Next

                            Using comandoPulizia As New SQLiteCommand("DELETE FROM " & tabellaEliminati & " WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                                comandoPulizia.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                                comandoPulizia.Parameters.Add("@id", System.Data.DbType.String)
                                For Each voce As StatoVoceRadioSync In finali.Values
                                    If voce.Eliminata AndAlso voce.Data < limiteEliminati Then
                                        comandoPulizia.Parameters("@id").Value = voce.Id
                                        comandoPulizia.ExecuteNonQuery()
                                    End If
                                Next
                            End Using

                            If massimo > 0 Then
                                Using comandoTaglio As New SQLiteCommand("DELETE FROM " & tabella & " WHERE Tipo = @tipo AND Id NOT IN (SELECT Id FROM " & tabella & " WHERE Tipo = @tipo ORDER BY Data DESC LIMIT @massimo)", connessione, transazione)
                                    comandoTaglio.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                                    comandoTaglio.Parameters.AddWithValue("@massimo", massimo)
                                    comandoTaglio.ExecuteNonQuery()
                                End Using
                            End If

                            transazione.Commit()
                        Catch exTransazione As Exception
                            _logger.ScriviLog("RadioTvDatabase.UnisciRaccoltaRadioFile transazione: " & exTransazione.ToString())
                            Try
                                transazione.Rollback()
                            Catch exRollback As Exception
                                _logger.ScriviLog("RadioTvDatabase.UnisciRaccoltaRadioFile rollback: " & exRollback.Message)
                            End Try
                            Return Nothing
                        End Try
                    End Using

                    Dim presenti As New List(Of StatoVoceRadioSync)
                    Dim risultato As New List(Of Dictionary(Of String, Object))
                    For Each voce As StatoVoceRadioSync In finali.Values
                        If voce.Eliminata Then
                            If voce.Data >= limiteEliminati Then risultato.Add(VoceRadioSync(voce))
                        Else
                            presenti.Add(voce)
                        End If
                    Next
                    If massimo > 0 Then
                        presenti.Sort(New ConfrontoStatoRadioPerData())
                        If presenti.Count > massimo Then presenti.RemoveRange(massimo, presenti.Count - massimo)
                    End If
                    For Each voce As StatoVoceRadioSync In presenti
                        risultato.Add(VoceRadioSync(voce))
                    Next
                    Return risultato
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioTvDatabase.UnisciRaccoltaRadioFile (" & If(tabella, "") & "): " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Private Sub ApplicaVoceRadioSync(connessione As SQLiteConnection, transazione As SQLiteTransaction, tabella As String, tabellaEliminati As String, remota As StatoVoceRadioSync, locali As Dictionary(Of String, StatoVoceRadioSync))
        Try
            Dim dataLocale As String = DataAStringa(remota.Data.ToLocalTime())
            If remota.Eliminata Then
                Using comando As New SQLiteCommand("DELETE FROM " & tabella & " WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                    comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                    comando.Parameters.AddWithValue("@id", remota.Id)
                    comando.ExecuteNonQuery()
                End Using
                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO " & tabellaEliminati & " (Tipo, Id, Data) VALUES (@tipo, @id, @data)", connessione, transazione)
                    comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                    comando.Parameters.AddWithValue("@id", remota.Id)
                    comando.Parameters.AddWithValue("@data", dataLocale)
                    comando.ExecuteNonQuery()
                End Using
                Return
            End If

            Dim nome As String = remota.Nome
            If String.IsNullOrWhiteSpace(nome) Then nome = remota.Id

            If locali.ContainsKey(remota.Id) Then
                Using comando As New SQLiteCommand("UPDATE " & tabella & " SET Nome = @nome, Data = @data WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@data", dataLocale)
                    comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                    comando.Parameters.AddWithValue("@id", remota.Id)
                    comando.ExecuteNonQuery()
                End Using
            Else
                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO " & tabella & " (Tipo, Id, Nome, Data, ChiaveWG) VALUES (@tipo, @id, @nome, @data, @chiavewg)", connessione, transazione)
                    comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                    comando.Parameters.AddWithValue("@id", remota.Id)
                    comando.Parameters.AddWithValue("@nome", nome)
                    comando.Parameters.AddWithValue("@data", dataLocale)
                    comando.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(connessione, tabella))
                    comando.ExecuteNonQuery()
                End Using
            End If

            Using comando As New SQLiteCommand("DELETE FROM " & tabellaEliminati & " WHERE Tipo = @tipo AND Id = @id", connessione, transazione)
                comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                comando.Parameters.AddWithValue("@id", remota.Id)
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.ApplicaVoceRadioSync (" & If(remota Is Nothing, "", remota.Id) & "): " & ex.ToString())
        End Try
    End Sub

    Private Function LeggiVociRadioSync(connessione As SQLiteConnection, tabella As String, eliminate As Boolean) As Dictionary(Of String, StatoVoceRadioSync)
        Dim risultato As New Dictionary(Of String, StatoVoceRadioSync)(StringComparer.Ordinal)
        Try
            Dim sql As String = "SELECT Id, Data FROM " & tabella & " WHERE Tipo = @tipo"
            If Not eliminate Then sql = "SELECT Id, Nome, Data FROM " & tabella & " WHERE Tipo = @tipo"
            Using comando As New SQLiteCommand(sql, connessione)
                comando.Parameters.AddWithValue("@tipo", TIPO_RADIO_SYNC)
                Using lettore As SQLiteDataReader = comando.ExecuteReader()
                    While lettore.Read()
                        Dim voce As New StatoVoceRadioSync()
                        voce.Id = Convert.ToString(lettore("Id"))
                        If String.IsNullOrWhiteSpace(voce.Id) Then Continue While
                        If Not eliminate Then voce.Nome = Convert.ToString(lettore("Nome"))
                        voce.Data = DataUtcRadioSync(StringaAData(Convert.ToString(lettore("Data"))))
                        voce.Eliminata = eliminate
                        risultato(voce.Id) = voce
                    End While
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.LeggiVociRadioSync (" & If(tabella, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function IndicizzaVociRadioSync(voci As List(Of Dictionary(Of String, Object))) As Dictionary(Of String, StatoVoceRadioSync)
        Dim risultato As New Dictionary(Of String, StatoVoceRadioSync)(StringComparer.Ordinal)
        Try
            For Each voce As Dictionary(Of String, Object) In voci
                Try
                    If voce Is Nothing Then Continue For
                    Dim stato As New StatoVoceRadioSync()
                    stato.Id = TestoVoceRadioSync(voce, "id")
                    If stato.Id.Length = 0 Then Continue For
                    stato.Nome = TestoVoceRadioSync(voce, "nome")
                    stato.Data = LeggiDataRadioSync(TestoVoceRadioSync(voce, "modificato"))
                    Dim eliminato As String = TestoVoceRadioSync(voce, "eliminato").ToLowerInvariant()
                    stato.Eliminata = eliminato = "true" OrElse eliminato = "1"
                    Dim esistente As StatoVoceRadioSync = Nothing
                    If risultato.TryGetValue(stato.Id, esistente) AndAlso esistente.Data >= stato.Data Then Continue For
                    risultato(stato.Id) = stato
                Catch exVoce As Exception
                    _logger.ScriviLog("RadioTvDatabase.IndicizzaVociRadioSync voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.IndicizzaVociRadioSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function VoceRadioSync(voce As StatoVoceRadioSync) As Dictionary(Of String, Object)
        Try
            Dim risultato As New Dictionary(Of String, Object)
            risultato("id") = voce.Id
            risultato("modificato") = voce.Data.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
            risultato("eliminato") = voce.Eliminata
            If Not voce.Eliminata Then risultato("nome") = If(voce.Nome, "")
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.VoceRadioSync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function TestoVoceRadioSync(voce As Dictionary(Of String, Object), chiave As String) As String
        Try
            Dim valore As Object = Nothing
            If voce Is Nothing OrElse Not voce.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
            Return Convert.ToString(valore, CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.TestoVoceRadioSync (" & If(chiave, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function LeggiDataRadioSync(testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim risultato As DateTime
            If DateTime.TryParse(testo, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.LeggiDataRadioSync: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Private Function DataUtcRadioSync(valore As DateTime) As DateTime
        Try
            If valore = DateTime.MinValue Then Return DateTime.MinValue
            If valore.Kind = DateTimeKind.Utc Then Return valore
            If valore.Kind = DateTimeKind.Unspecified Then Return DateTime.SpecifyKind(valore, DateTimeKind.Local).ToUniversalTime()
            Return valore.ToUniversalTime()
        Catch ex As Exception
            _logger.ScriviLog("RadioTvDatabase.DataUtcRadioSync: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

#End Region

End Class
