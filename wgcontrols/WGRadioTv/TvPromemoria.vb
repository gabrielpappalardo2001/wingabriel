Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb
Imports WGPlayerMpv

' Promemoria dei programmi televisivi e radiofonici. Si imposta dal menu di un programma nella programmazione di un canale o di una radio; restano salvati tra un avvio e
' l'altro e, quando il programma inizia, una domanda sì o no (WGDomanda) chiede se lo si vuole ascoltare.
' Se WinGabriel era chiuso all'ora giusta, il promemoria arriva comunque al prossimo avvio purché il programma sia iniziato da non più di mezz'ora.
Partial Public NotInheritable Class TvPromemoria

    Public Class VocePromemoria
        Public Property Id As Long
        Public Property Tipo As String = TIPO_TV
        Public Property CanaleId As String = ""
        Public Property NomeCanale As String = ""
        Public Property Titolo As String = ""
        Public Property Inizio As DateTime
        ''' <summary>Quando finisce il programma; DateTime.MinValue se non si sa (promemoria salvati prima).</summary>
        Public Property Fine As DateTime = DateTime.MinValue
    End Class

    ''' <summary>Il promemoria è di un canale televisivo.</summary>
    Public Const TIPO_TV As String = "tv"
    ''' <summary>Il promemoria è di una radio.</summary>
    Public Const TIPO_RADIO As String = "radio"

    Private Const SECONDI_TRA_DUE_CONTROLLI As Integer = 5
    Private Const MINUTI_MASSIMI_DI_RITARDO As Integer = 30
    Private Const MINUTI_PER_DIRE_ADESSO As Integer = 2

    Private Shared ReadOnly _logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared _timer As System.Threading.Timer
    Private Shared _timerSeguiti As System.Threading.Timer
    Private Shared _inScansione As Integer = 0
    Private Const ORE_TRA_DUE_SCANSIONI As Integer = 3
    Private Shared _stringaConnessione As String = ""
    Private Shared _pronto As Boolean = False
    Private Shared _inControllo As Integer = 0

    Private Sub New()
    End Sub

#Region "Avvio e archivio"

    ' Da chiamare una volta all'avvio di WinGabriel (e, senza danno, ogni volta che si apre la Web TV): fa partire il controllo dei promemoria.
    Public Shared Sub Avvia()
        Try
            SyncLock _lock
                If _timer IsNot Nothing Then Return
                If Not PreparaArchivio() Then Return
                _timer = New System.Threading.Timer(New TimerCallback(AddressOf ControllaScadenze), Nothing, 3000, SECONDI_TRA_DUE_CONTROLLI * 1000)
                _timerSeguiti = New System.Threading.Timer(New TimerCallback(AddressOf ScansionaSeguitiDalTimer), Nothing, 45000, ORE_TRA_DUE_SCANSIONI * 3600 * 1000)
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Avvia: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function PreparaArchivio() As Boolean
        Try
            If _pronto Then Return True

            Dim cartella As String = ""
            Try
                cartella = WGDatabase.CartellaDati("tv")
            Catch
            End Try
            If String.IsNullOrWhiteSpace(cartella) Then cartella = Path.Combine(Application.StartupPath, "dati", "tv")
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)

            Dim fileDb As String = Path.Combine(cartella, "promemoria.db")
            If Not File.Exists(fileDb) Then SQLiteConnection.CreateFile(fileDb)
            _stringaConnessione = "Data Source=" & fileDb & ";Version=3;"

            Using connessione As New SQLiteConnection(_stringaConnessione)
                connessione.Open()
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Promemoria (Id INTEGER PRIMARY KEY AUTOINCREMENT, CanaleId TEXT NOT NULL, NomeCanale TEXT, Titolo TEXT, InizioUtc TEXT NOT NULL)", connessione)
                    comando.ExecuteNonQuery()
                End Using
                ' i promemoria salvati prima che esistessero quelli delle radio sono tutti televisivi
                Dim haTipo As Boolean = False
                Using comando As New SQLiteCommand("PRAGMA table_info(Promemoria)", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            If String.Equals(Convert.ToString(lettore("name")), "Tipo", StringComparison.OrdinalIgnoreCase) Then haTipo = True
                        End While
                    End Using
                End Using
                If Not haTipo Then
                    Using comando As New SQLiteCommand("ALTER TABLE Promemoria ADD COLUMN Tipo TEXT NOT NULL DEFAULT 'tv'", connessione)
                        comando.ExecuteNonQuery()
                    End Using
                End If
                ' la fine del programma: se all'avvio di WinGabriel il programma è già finito, non si avvisa
                Dim haFine As Boolean = False
                Using comando As New SQLiteCommand("PRAGMA table_info(Promemoria)", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            If String.Equals(Convert.ToString(lettore("name")), "FineUtc", StringComparison.OrdinalIgnoreCase) Then haFine = True
                        End While
                    End Using
                End Using
                If Not haFine Then
                    Using comando As New SQLiteCommand("ALTER TABLE Promemoria ADD COLUMN FineUtc TEXT", connessione)
                        comando.ExecuteNonQuery()
                    End Using
                End If
                ' i programmi seguiti, e quelli per cui il promemoria è già stato messo da sé (per non rimetterlo se l'utente lo toglie)
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Seguiti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Tipo TEXT NOT NULL, CanaleId TEXT NOT NULL, NomeCanale TEXT, TvgId TEXT, Titolo TEXT NOT NULL, Chiave TEXT NOT NULL)", connessione)
                    comando.ExecuteNonQuery()
                End Using
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS SeguitiMessi (Chiave TEXT PRIMARY KEY, InizioUtc TEXT)", connessione)
                    comando.ExecuteNonQuery()
                End Using
                ' per la sincronizzazione tra computer: la data di modifica di ogni riga e l'elenco di quello che è stato tolto
                For Each tabellaSync As String In New String() {"Promemoria", "Seguiti"}
                    Dim haModificato As Boolean = False
                    Using comando As New SQLiteCommand("PRAGMA table_info(" & tabellaSync & ")", connessione)
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            While lettore.Read()
                                If String.Equals(Convert.ToString(lettore("name")), "Modificato", StringComparison.OrdinalIgnoreCase) Then haModificato = True
                            End While
                        End Using
                    End Using
                    If Not haModificato Then
                        Using comando As New SQLiteCommand("ALTER TABLE " & tabellaSync & " ADD COLUMN Modificato TEXT", connessione)
                            comando.ExecuteNonQuery()
                        End Using
                    End If
                Next
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS SincEliminati (Sezione TEXT NOT NULL, IdSync TEXT NOT NULL, Data TEXT NOT NULL, PRIMARY KEY (Sezione, IdSync))", connessione)
                    comando.ExecuteNonQuery()
                End Using
            End Using

            _pronto = True
            Return True
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.PreparaArchivio: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function Chiave(inizio As DateTime) As String
        Return inizio.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Shared Function Esiste(canaleId As String, inizio As DateTime, Optional tipo As String = TIPO_TV) As Boolean
        Try
            SyncLock _lock
                If Not PreparaArchivio() Then Return False
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("SELECT COUNT(*) FROM Promemoria WHERE Tipo = @tipo AND CanaleId = @canale AND InizioUtc = @inizio", connessione)
                        comando.Parameters.AddWithValue("@tipo", tipo)
                        comando.Parameters.AddWithValue("@canale", canaleId)
                        comando.Parameters.AddWithValue("@inizio", Chiave(inizio))
                        Return Convert.ToInt32(comando.ExecuteScalar()) > 0
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Esiste: " & ex.Message)
            Return False
        End Try
    End Function

    ' Aggiunge il promemoria; se per quel canale e quell'ora c'è già non ne mette un secondo. Vero se alla fine c'è.
    Public Shared Function Aggiungi(canaleId As String, nomeCanale As String, titolo As String, inizio As DateTime, Optional tipo As String = TIPO_TV, Optional fine As DateTime = Nothing) As Boolean
        Try
            If String.IsNullOrWhiteSpace(canaleId) Then Return False
            If Esiste(canaleId, inizio, tipo) Then Return True
            SyncLock _lock
                If Not PreparaArchivio() Then Return False
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("INSERT INTO Promemoria (Tipo, CanaleId, NomeCanale, Titolo, InizioUtc, FineUtc, Modificato) VALUES (@tipo, @canale, @nome, @titolo, @inizio, @fine, @modificato)", connessione)
                        comando.Parameters.AddWithValue("@modificato", AdessoSync())
                        comando.Parameters.AddWithValue("@fine", If(fine > inizio, CObj(Chiave(fine)), DBNull.Value))
                        comando.Parameters.AddWithValue("@tipo", tipo)
                        comando.Parameters.AddWithValue("@canale", canaleId)
                        comando.Parameters.AddWithValue("@nome", If(nomeCanale, ""))
                        comando.Parameters.AddWithValue("@titolo", If(titolo, ""))
                        comando.Parameters.AddWithValue("@inizio", Chiave(inizio))
                        comando.ExecuteNonQuery()
                    End Using
                    TogliEliminazione(connessione, NomeSezione(tipo, False), tipo & "|" & canaleId & "|" & Chiave(inizio))
                End Using
            End SyncLock
            Avvia()
            ChiediSincronizzazione()
            Return True
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Aggiungi: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Sub Rimuovi(canaleId As String, inizio As DateTime, Optional tipo As String = TIPO_TV)
        Try
            SyncLock _lock
                If Not PreparaArchivio() Then Return
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("DELETE FROM Promemoria WHERE Tipo = @tipo AND CanaleId = @canale AND InizioUtc = @inizio", connessione)
                        comando.Parameters.AddWithValue("@tipo", tipo)
                        comando.Parameters.AddWithValue("@canale", canaleId)
                        comando.Parameters.AddWithValue("@inizio", Chiave(inizio))
                        comando.ExecuteNonQuery()
                    End Using
                    RegistraEliminazione(connessione, NomeSezione(tipo, False), tipo & "|" & canaleId & "|" & Chiave(inizio))
                End Using
            End SyncLock
            ChiediSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Rimuovi: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RimuoviPerId(id As Long)
        Try
            SyncLock _lock
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Dim tipoRiga As String = ""
                    Dim canaleRiga As String = ""
                    Dim inizioRiga As String = ""
                    Using comando As New SQLiteCommand("SELECT Tipo, CanaleId, InizioUtc FROM Promemoria WHERE Id = @id", connessione)
                        comando.Parameters.AddWithValue("@id", id)
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            If lettore.Read() Then
                                tipoRiga = Convert.ToString(lettore("Tipo"))
                                canaleRiga = Convert.ToString(lettore("CanaleId"))
                                inizioRiga = Convert.ToString(lettore("InizioUtc"))
                            End If
                        End Using
                    End Using
                    Using comando As New SQLiteCommand("DELETE FROM Promemoria WHERE Id = @id", connessione)
                        comando.Parameters.AddWithValue("@id", id)
                        comando.ExecuteNonQuery()
                    End Using
                    If tipoRiga.Length > 0 Then RegistraEliminazione(connessione, NomeSezione(tipoRiga, False), tipoRiga & "|" & canaleRiga & "|" & inizioRiga)
                End Using
            End SyncLock
            ChiediSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.RimuoviPerId: " & ex.Message)
        End Try
    End Sub

    ' I promemoria già arrivati all'ora: si leggono e, ricordati o scaduti, si tolgono dall'archivio.
    Private Shared Function LeggiScaduti() As List(Of VocePromemoria)
        Dim risultato As New List(Of VocePromemoria)()
        Try
            SyncLock _lock
                If Not PreparaArchivio() Then Return risultato
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("SELECT Id, Tipo, CanaleId, NomeCanale, Titolo, InizioUtc, FineUtc FROM Promemoria WHERE InizioUtc <= @adesso ORDER BY InizioUtc", connessione)
                        comando.Parameters.AddWithValue("@adesso", Chiave(DateTime.Now))
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            While lettore.Read()
                                Try
                                    Dim v As New VocePromemoria()
                                    v.Id = Convert.ToInt64(lettore("Id"))
                                    v.Tipo = If(String.Equals(Convert.ToString(lettore("Tipo")), TIPO_RADIO, StringComparison.OrdinalIgnoreCase), TIPO_RADIO, TIPO_TV)
                                    v.CanaleId = Convert.ToString(lettore("CanaleId"))
                                    v.NomeCanale = Convert.ToString(lettore("NomeCanale"))
                                    v.Titolo = Convert.ToString(lettore("Titolo"))
                                    v.Inizio = DateTime.SpecifyKind(DateTime.ParseExact(Convert.ToString(lettore("InizioUtc")), "yyyy-MM-dd'T'HH:mm:ss", Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc).ToLocalTime()
                                    If Not IsDBNull(lettore("FineUtc")) Then
                                        v.Fine = DateTime.SpecifyKind(DateTime.ParseExact(Convert.ToString(lettore("FineUtc")), "yyyy-MM-dd'T'HH:mm:ss", Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc).ToLocalTime()
                                    End If
                                    risultato.Add(v)
                                Catch exRiga As Exception
                                    _logger.ScriviLog("TvPromemoria.LeggiScaduti riga: " & exRiga.Message)
                                End Try
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.LeggiScaduti: " & ex.Message)
        End Try
        Return risultato
    End Function

#End Region

#Region "Scadenze"

    Private Shared Sub ControllaScadenze(stato As Object)
        ' un controllo alla volta: se il precedente non è finito si salta questo giro
        If Interlocked.Exchange(_inControllo, 1) = 1 Then Return
        Try
            ' la domanda si fa dal thread dell'interfaccia: se non c'è ancora (WinGabriel sta partendo) si riprova al prossimo giro
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed OrElse Not contenitore.IsHandleCreated Then Return

            Dim scaduti As List(Of VocePromemoria) = LeggiScaduti()
            For i As Integer = 0 To scaduti.Count - 1
                Dim v As VocePromemoria = scaduti(i)
                RimuoviPerId(v.Id)
                Try
                    ' se il programma è già finito (WinGabriel era chiuso) non si avvisa; senza la fine nota vale il limite di mezz'ora
                    Dim giaFinito As Boolean = v.Fine > DateTime.MinValue AndAlso v.Fine <= DateTime.Now
                    If Not giaFinito AndAlso (DateTime.Now - v.Inizio).TotalMinutes <= MINUTI_MASSIMI_DI_RITARDO Then
                        contenitore.BeginInvoke(New MethodInvoker(Sub() Chiedi(v)))
                    End If
                Catch exAvviso As Exception
                    _logger.ScriviLog("TvPromemoria.ControllaScadenze avviso: " & exAvviso.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.ControllaScadenze: " & ex.Message)
        Finally
            Interlocked.Exchange(_inControllo, 0)
        End Try
    End Sub

    ' "Sta iniziando X su Y, vuoi ascoltarlo?": se sì parte l'ascolto, nel modo scelto nelle impostazioni (sottofondo, lettore o richiesta);
    ' se no, non succede altro.
    Private Shared Sub Chiedi(v As VocePromemoria)
        Try
            Dim eRadio As Boolean = String.Equals(v.Tipo, TIPO_RADIO, StringComparison.OrdinalIgnoreCase)
            Dim nomeCanale As String = v.NomeCanale
            If String.IsNullOrWhiteSpace(nomeCanale) Then nomeCanale = If(eRadio, "la radio", "il canale")

            Dim domanda As String
            If (DateTime.Now - v.Inizio).TotalMinutes < MINUTI_PER_DIRE_ADESSO Then
                domanda = "Sta iniziando " & v.Titolo & " su " & nomeCanale & ". Vuoi ascoltarlo?"
            Else
                domanda = "È iniziato alle " & v.Inizio.ToString("HH:mm") & " " & v.Titolo & " su " & nomeCanale & ". Vuoi ascoltarlo?"
            End If

            Dim canaleId As String = v.CanaleId
            WGDomanda.Chiedi(domanda, Sub()
                                          If eRadio Then AvviaRadio(canaleId) Else AvviaCanale(canaleId)
                                      End Sub)
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Chiedi: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Programmi seguiti"

    ' Un programma seguito: ogni volta che va in onda su quel canale (o su quella radio) si mette da solo il promemoria.
    Public Class ProgrammaSeguito
        Public Property Id As Long
        Public Property Tipo As String = TIPO_TV
        Public Property CanaleId As String = ""
        Public Property NomeCanale As String = ""
        Public Property TvgId As String = ""
        Public Property Titolo As String = ""
    End Class

    ''' <summary>
    ''' Il titolo del programma senza la puntata: "Tv7 - Puntata del 06/11/2026" diventa "Tv7", "GR 1: Giornale della mezzanotte" diventa "GR 1".
    ''' </summary>
    Public Shared Function TitoloBase(titolo As String) As String
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return ""
            Dim t As String = titolo.Trim()
            Dim taglio As Integer = -1
            For Each separatore As String In New String() {" - ", " – ", ": "}
                Dim posizione As Integer = t.IndexOf(separatore, StringComparison.Ordinal)
                If posizione > 0 AndAlso (taglio < 0 OrElse posizione < taglio) Then taglio = posizione
            Next
            If taglio > 0 Then t = t.Substring(0, taglio)
            Return t.Trim()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.TitoloBase: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Il titolo ridotto a minuscole, lettere e cifre, per riconoscerlo anche se cambiano maiuscole, accenti o segni.
    Private Shared Function ChiaveTitolo(titolo As String) As String
        If String.IsNullOrWhiteSpace(titolo) Then Return ""
        Dim scomposto As String = titolo.Normalize(System.Text.NormalizationForm.FormD)
        Dim sb As New System.Text.StringBuilder()
        For Each c As Char In scomposto
            If Globalization.CharUnicodeInfo.GetUnicodeCategory(c) = Globalization.UnicodeCategory.NonSpacingMark Then Continue For
            If Char.IsLetterOrDigit(c) Then sb.Append(Char.ToLowerInvariant(c))
        Next
        Return sb.ToString()
    End Function

    Public Shared Function Segue(tipo As String, canaleId As String, titoloBase As String) As Boolean
        Try
            Dim chiave As String = ChiaveTitolo(titoloBase)
            If chiave.Length = 0 OrElse String.IsNullOrWhiteSpace(canaleId) Then Return False
            SyncLock _lock
                If Not PreparaArchivio() Then Return False
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("SELECT COUNT(*) FROM Seguiti WHERE Tipo = @tipo AND CanaleId = @canale AND Chiave = @chiave", connessione)
                        comando.Parameters.AddWithValue("@tipo", tipo)
                        comando.Parameters.AddWithValue("@canale", canaleId)
                        comando.Parameters.AddWithValue("@chiave", chiave)
                        Return Convert.ToInt32(comando.ExecuteScalar()) > 0
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Segue: " & ex.Message)
            Return False
        End Try
    End Function

    ' Comincia a seguire un programma; se lo si segue già non ne mette un secondo. Vero se alla fine lo si segue.
    Public Shared Function Segui(tipo As String, canaleId As String, nomeCanale As String, tvgId As String, titoloBase As String) As Boolean
        Try
            Dim chiave As String = ChiaveTitolo(titoloBase)
            If chiave.Length = 0 OrElse String.IsNullOrWhiteSpace(canaleId) Then Return False
            If Segue(tipo, canaleId, titoloBase) Then Return True
            SyncLock _lock
                If Not PreparaArchivio() Then Return False
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("INSERT INTO Seguiti (Tipo, CanaleId, NomeCanale, TvgId, Titolo, Chiave, Modificato) VALUES (@tipo, @canale, @nome, @tvg, @titolo, @chiave, @modificato)", connessione)
                        comando.Parameters.AddWithValue("@modificato", AdessoSync())
                        comando.Parameters.AddWithValue("@tipo", tipo)
                        comando.Parameters.AddWithValue("@canale", canaleId)
                        comando.Parameters.AddWithValue("@nome", If(nomeCanale, ""))
                        comando.Parameters.AddWithValue("@tvg", If(tvgId, ""))
                        comando.Parameters.AddWithValue("@titolo", titoloBase.Trim())
                        comando.Parameters.AddWithValue("@chiave", chiave)
                        comando.ExecuteNonQuery()
                    End Using
                    TogliEliminazione(connessione, NomeSezione(tipo, True), tipo & "|" & canaleId & "|" & chiave)
                End Using
            End SyncLock
            Avvia()
            ChiediSincronizzazione()
            Return True
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.Segui: " & ex.ToString())
            Return False
        End Try
    End Function

    ' I programmi seguiti del tipo dato, oppure tutti con tipo vuoto.
    Public Shared Function ElencoSeguiti(Optional tipo As String = "") As List(Of ProgrammaSeguito)
        Dim risultato As New List(Of ProgrammaSeguito)()
        Try
            SyncLock _lock
                If Not PreparaArchivio() Then Return risultato
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Dim sql As String = "SELECT Id, Tipo, CanaleId, NomeCanale, TvgId, Titolo FROM Seguiti"
                    If Not String.IsNullOrEmpty(tipo) Then sql &= " WHERE Tipo = @tipo"
                    sql &= " ORDER BY Titolo COLLATE NOCASE"
                    Using comando As New SQLiteCommand(sql, connessione)
                        If Not String.IsNullOrEmpty(tipo) Then comando.Parameters.AddWithValue("@tipo", tipo)
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            While lettore.Read()
                                risultato.Add(New ProgrammaSeguito() With {
                                    .Id = Convert.ToInt64(lettore("Id")),
                                    .Tipo = Convert.ToString(lettore("Tipo")),
                                    .CanaleId = Convert.ToString(lettore("CanaleId")),
                                    .NomeCanale = Convert.ToString(lettore("NomeCanale")),
                                    .TvgId = Convert.ToString(lettore("TvgId")),
                                    .Titolo = Convert.ToString(lettore("Titolo"))})
                            End While
                        End Using
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.ElencoSeguiti: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Sub SmettiDiSeguire(id As Long)
        Try
            SyncLock _lock
                If Not PreparaArchivio() Then Return
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Dim tipoRiga As String = ""
                    Dim canaleRiga As String = ""
                    Dim chiaveRiga As String = ""
                    Using comando As New SQLiteCommand("SELECT Tipo, CanaleId, Chiave FROM Seguiti WHERE Id = @id", connessione)
                        comando.Parameters.AddWithValue("@id", id)
                        Using lettore As SQLiteDataReader = comando.ExecuteReader()
                            If lettore.Read() Then
                                tipoRiga = Convert.ToString(lettore("Tipo"))
                                canaleRiga = Convert.ToString(lettore("CanaleId"))
                                chiaveRiga = Convert.ToString(lettore("Chiave"))
                            End If
                        End Using
                    End Using
                    Using comando As New SQLiteCommand("DELETE FROM Seguiti WHERE Id = @id", connessione)
                        comando.Parameters.AddWithValue("@id", id)
                        comando.ExecuteNonQuery()
                    End Using
                    If tipoRiga.Length > 0 Then RegistraEliminazione(connessione, NomeSezione(tipoRiga, True), tipoRiga & "|" & canaleRiga & "|" & chiaveRiga)
                End Using
            End SyncLock
            ChiediSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.SmettiDiSeguire: " & ex.Message)
        End Try
    End Sub

    ' Cerca nella programmazione i programmi seguiti che vanno in onda a breve e mette i promemoria: una volta all'avvio, ogni poche ore,
    ' e subito dopo che si comincia a seguire un programma. Un promemoria tolto a mano non viene rimesso.
    Public Shared Sub ScansionaSeguitiSubito()
        Try
            Avvia()
            Task.Run(Sub() ScansionaSeguiti())
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.ScansionaSeguitiSubito: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScansionaSeguitiDalTimer(stato As Object)
        ScansionaSeguiti()
    End Sub

    Private Shared Sub ScansionaSeguiti()
        If Interlocked.Exchange(_inScansione, 1) = 1 Then Return
        Try
            ScansionaSeguitiAsync().GetAwaiter().GetResult()
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.ScansionaSeguiti: " & ex.Message)
        Finally
            Interlocked.Exchange(_inScansione, 0)
        End Try
    End Sub

    Private Shared Async Function ScansionaSeguitiAsync() As Task
        Dim seguiti As List(Of ProgrammaSeguito) = ElencoSeguiti()
        If seguiti.Count = 0 Then Return

        PulisciAvvisati()
        For Each s As ProgrammaSeguito In seguiti
            Try
                Dim programmi As List(Of TvProgrammazione.VoceProgramma)
                If String.Equals(s.Tipo, TIPO_RADIO, StringComparison.OrdinalIgnoreCase) Then
                    programmi = Await RadioProgrammazione.OttieniAsync(s.NomeCanale).ConfigureAwait(False)
                Else
                    programmi = Await TvProgrammazione.OttieniAsync(s.TvgId, s.NomeCanale).ConfigureAwait(False)
                End If
                If programmi Is Nothing Then Continue For

                Dim chiaveSeguito As String = ChiaveTitolo(s.Titolo)
                For Each p As TvProgrammazione.VoceProgramma In programmi
                    If p.Inizio <= DateTime.Now.AddMinutes(1) Then Continue For
                    If ChiaveTitolo(TitoloBase(p.Titolo)) <> chiaveSeguito Then Continue For

                    Dim chiaveMessa As String = s.Tipo & "|" & s.CanaleId & "|" & Chiave(p.Inizio)
                    If GiaMesso(chiaveMessa) Then Continue For
                    If Not Esiste(s.CanaleId, p.Inizio, s.Tipo) Then Aggiungi(s.CanaleId, s.NomeCanale, p.Titolo, p.Inizio, s.Tipo, p.Fine)
                    SegnaMesso(chiaveMessa, p.Inizio)
                Next
            Catch ex As Exception
                _logger.ScriviLog("TvPromemoria.ScansionaSeguitiAsync (" & s.Titolo & "): " & ex.Message)
            End Try
        Next
    End Function

    Private Shared Function GiaMesso(chiaveMessa As String) As Boolean
        Try
            SyncLock _lock
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("SELECT COUNT(*) FROM SeguitiMessi WHERE Chiave = @chiave", connessione)
                        comando.Parameters.AddWithValue("@chiave", chiaveMessa)
                        Return Convert.ToInt32(comando.ExecuteScalar()) > 0
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.GiaMesso: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Sub SegnaMesso(chiaveMessa As String, inizio As DateTime)
        Try
            SyncLock _lock
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("INSERT OR IGNORE INTO SeguitiMessi (Chiave, InizioUtc) VALUES (@chiave, @inizio)", connessione)
                        comando.Parameters.AddWithValue("@chiave", chiaveMessa)
                        comando.Parameters.AddWithValue("@inizio", Chiave(inizio))
                        comando.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.SegnaMesso: " & ex.Message)
        End Try
    End Sub

    ' Dei promemoria messi da sé si ricorda solo quelli recenti: i programmi passati da più di un giorno non servono più.
    Private Shared Sub PulisciAvvisati()
        Try
            SyncLock _lock
                Using connessione As New SQLiteConnection(_stringaConnessione)
                    connessione.Open()
                    Using comando As New SQLiteCommand("DELETE FROM SeguitiMessi WHERE InizioUtc < @limite", connessione)
                        comando.Parameters.AddWithValue("@limite", Chiave(DateTime.Now.AddDays(-1)))
                        comando.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.PulisciAvvisati: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Avvio del canale"

    ' Guarda un canale dato il suo identificativo, anche se la Web TV non è mai stata aperta in questa sessione: in quel caso prima scarica
    ' l'elenco dei canali, con WGProcessoInCorso. Da chiamare dal thread dell'interfaccia.
    Public Shared Sub AvviaCanale(canaleId As String)
        Try
            If String.IsNullOrWhiteSpace(canaleId) Then Return

            Dim db As TvCanaliDb = TvCanaliDb.GetInstance()
            If db.NumeroCanali = 0 Then
                Dim proc As New WGProcessoInCorso()
                proc.Avvia("Sto scaricando l'elenco dei canali, attendi.", False)
                Try
                    db.Carica()
                Finally
                    Try
                        proc.Fine()
                    Catch exFine As Exception
                        _logger.ScriviLog("TvPromemoria.AvviaCanale fine: " & exFine.Message)
                    End Try
                End Try
            End If

            Dim canale As TvCanaliDb.CanaleTv = db.TrovaPerId(canaleId)
            If canale Is Nothing OrElse String.IsNullOrWhiteSpace(canale.Url) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il canale non è disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            RadioTvDatabase.GetInstance().AggiungiRecente(RadioTvDatabase.TipoElemento.Tv, canale.Id, canale.Nome)

            WGModalitaAscolto.Avvia(canale.Nome,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciStreamTv(canale.Url, canale.Nome, canale.UserAgent, canale.Referer),
                                    Sub()
                                        Dim player As New WGPlayerFormInt()
                                        player.AggiungiElemento(canale.Url, ElementType.StreamTv, canale.Nome, canale.UserAgent, canale.Referer)
                                        player.avvia(canale.Nome)
                                    End Sub)
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.AvviaCanale: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile avviare il canale.")
            msg.ShowDialog()
        End Try
    End Sub

    ' Ascolta una radio dato il suo identificativo, anche se la sezione radio non è mai stata aperta in questa sessione: in quel caso prima
    ' carica l'elenco delle radio, con WGProcessoInCorso. Da chiamare dal thread dell'interfaccia.
    Public Shared Sub AvviaRadio(stazioneId As String)
        Try
            If String.IsNullOrWhiteSpace(stazioneId) Then Return

            Dim db As RadioDb = RadioDb.GetInstance()
            If Not db.IsCaricato Then
                Dim proc As New WGProcessoInCorso()
                proc.Avvia("Sto caricando l'elenco delle radio, attendi.", False)
                Try
                    db.Carica(False)
                Finally
                    Try
                        proc.Fine()
                    Catch exFine As Exception
                        _logger.ScriviLog("TvPromemoria.AvviaRadio fine: " & exFine.Message)
                    End Try
                End Try
            End If

            Dim stazione As RadioDb.StazioneRadio = db.TrovaPerId(stazioneId)
            If stazione Is Nothing OrElse String.IsNullOrWhiteSpace(stazione.UrlRiproduzione) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La radio non è disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            db.SegnalaAscolto(stazione.Id)
            RadioTvDatabase.GetInstance().AggiungiRecente(RadioTvDatabase.TipoElemento.Radio, stazione.Id, stazione.Nome)

            WGModalitaAscolto.Avvia(stazione.Nome,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciStreamRadio(stazione.UrlRiproduzione, stazione.Nome),
                                    Sub()
                                        Dim player As New WGPlayerFormInt()
                                        AddHandler player.ErroreRadio, Sub(mittente As Object, e As ErroreRadioPlayerEventArgs)
                                                                           Try
                                                                               If e Is Nothing OrElse String.IsNullOrWhiteSpace(e.Url) Then Return
                                                                               Dim trovata As RadioDb.StazioneRadio = db.TrovaPerUrl(e.Url)
                                                                               If trovata IsNot Nothing Then db.SegnalaErroreStazione(trovata.Id)
                                                                           Catch exErrore As Exception
                                                                               _logger.ScriviLog("TvPromemoria.AvviaRadio errore radio: " & exErrore.Message)
                                                                           End Try
                                                                       End Sub
                                        player.AggiungiElemento(stazione.UrlRiproduzione, ElementType.WebRadio, stazione.Nome)
                                        player.avvia(stazione.Nome)
                                    End Sub)
        Catch ex As Exception
            _logger.ScriviLog("TvPromemoria.AvviaRadio: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile avviare la radio.")
            msg.ShowDialog()
        End Try
    End Sub

#End Region

End Class
