Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports wgcontrols
Imports System.Windows.Forms
Imports System.Runtime.InteropServices
Imports System.Security
Imports WGDb
Imports System.Xml.Serialization

Public Class Nota
    Implements IDisposable

    Private _secureTesto As New SecureString()
    Private _disposed As Boolean = False

    Public Property Id As Integer
    Public Property Titolo As String

    Public Property Testo As String
        Get
            If _disposed Then Return String.Empty
            Dim unmanagedString As IntPtr = IntPtr.Zero
            Try
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(_secureTesto)
                Return Marshal.PtrToStringUni(unmanagedString)
            Finally
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString)
            End Try
        End Get
        Set(value As String)
            If _disposed Then Return
            _secureTesto.Clear()
            If Not String.IsNullOrEmpty(value) Then
                For Each c As Char In value
                    _secureTesto.AppendChar(c)
                Next
            End If
        End Set
    End Property

    Public Property Protetta As Boolean
    Public Property Sale As String
    Public Property VettoreIV As String
    Public Property HashPassword As String
    Public Property DataCreazione As Date?
    Public Property Formato As FormatoNota = FormatoNota.Testo

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not _disposed Then
            _secureTesto.Dispose()
            _disposed = True
        End If
    End Sub
End Class

Public Enum OrdinamentoNote
    PiuRecenti = 0
    MenoRecenti = 1
    AlfabeticoAZ = 2
    AlfabeticoZA = 3
End Enum

''' <summary>
''' Formato con cui è salvato il contenuto di una nota.
''' Testo: contenuto semplice, salvato così com'è nella colonna Testo.
''' Word: contenuto di un documento .docx (creato con l'editor WGWord),
'''       salvato come stringa Base64 nella stessa colonna Testo, in modo
'''       da riusare senza modifiche la cifratura/protezione già esistente.
''' </summary>
Public Enum FormatoNota
    Testo = 0
    Word = 1
    Html = 2
End Enum

Public Class CLSNote
    Implements IDisposable

    Private PercorsoCartella As String = RisolviCartellaNote()
    Private PercorsoFileDb As String = RisolviFileDbNote()
    Private ConnectionString As String
    Private _connection As SQLiteConnection
    Private ReadOnly _logger As New Logger()

    Private Shared Function RisolviCartellaNote() As String
        Dim cartella As String = WGDatabase.CartellaDati("note")
        Try

            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch ex As Exception

            Dim Logger As New Logger()
            Logger.ScriviLog(ex.Message & ex.StackTrace)
        End Try
        Return cartella & "note"
    End Function

    Private Shared Function RisolviFileDbNote() As String
        Try
            Dim file As String = WGDatabase.PercorsoFileDati("note", "note.db")
            If Not String.IsNullOrWhiteSpace(file) Then Return file
        Catch ex As Exception
            Dim Logger As New Logger()
            Logger.ScriviLog(ex.Message & ex.StackTrace)
        End Try
        Return Path.Combine(RisolviCartellaNote(), "note.db")
    End Function

    Public Sub New()
        Try
            If Not Directory.Exists(PercorsoCartella) Then
                Directory.CreateDirectory(PercorsoCartella)
            End If

            ConnectionString = $"Data Source={PercorsoFileDb};Version=3;Journal Mode=WAL;Cache Size=20000;Page Size=8192;Synchronous=NORMAL;Pooling=True;DateTimeFormat=ISO8601;BusyTimeout=8000;"
            Dim nuovoDb As Boolean = Not File.Exists(PercorsoFileDb)

            _connection = WGDatabase.ApriConnessione(PercorsoFileDb, "Version=3;Journal Mode=WAL;Cache Size=20000;Page Size=8192;Synchronous=NORMAL;Pooling=True;DateTimeFormat=ISO8601;BusyTimeout=8000;")
            If _connection Is Nothing Then
                If nuovoDb Then
                    SQLiteConnection.CreateFile(PercorsoFileDb)
                End If
                _connection = New SQLiteConnection(ConnectionString)
                _connection.Open()
            End If

            Using cmdMmap As New SQLiteCommand("PRAGMA mmap_size=536870912; PRAGMA temp_store=MEMORY;", _connection)
                cmdMmap.ExecuteNonQuery()
            End Using

            If nuovoDb Then
                InizializzaDatabase()
            Else
                Using cmdOpt As New SQLiteCommand("PRAGMA optimize;", _connection)
                    cmdOpt.ExecuteNonQuery()
                End Using
            End If

            Try
                WGDatabase.AssicuraColonnaChiave(_connection, "Note")
            Catch exChiave As Exception
                _logger.ScriviLog($"CLSNote.New chiave: {exChiave}")
            End Try

            Try
                AssicuraColonnaFormatoTesto()
            Catch exFormato As Exception
                _logger.ScriviLog($"CLSNote.New formato: {exFormato}")
            End Try

            Try
                AssicuraTabellaImpostazioni()
            Catch exImpostazioni As Exception
                _logger.ScriviLog($"CLSNote.New impostazioni: {exImpostazioni}")
            End Try

        Catch ex As Exception
            _logger.ScriviLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} CLSNote.New Critical Error: {ex.Message}")
            Throw
        End Try
    End Sub

    Private Sub InizializzaDatabase()
        Dim transazione As SQLiteTransaction = Nothing
        Try
            transazione = _connection.BeginTransaction()

            Using cmdPragma As New SQLiteCommand("PRAGMA auto_vacuum = INCREMENTAL;", _connection, transazione)
                cmdPragma.ExecuteNonQuery()
            End Using

            Dim sqlCreazione As String = "CREATE TABLE IF NOT EXISTS Note (" &
                                         "Id INTEGER PRIMARY KEY AUTOINCREMENT, " &
                                         "Titolo TEXT NOT NULL, " &
                                         "Testo TEXT, " &
                                         "Protetta INTEGER DEFAULT 0, " &
                                         "Sale TEXT, " &
                                         "VettoreIV TEXT, " &
                                         "HashPassword TEXT, " &
                                         "DataCreazione DATETIME, " &
                                         "ChiaveWG TEXT, " &
                                         "FormatoTesto INTEGER DEFAULT 0)"

            Using cmd As New SQLiteCommand(sqlCreazione, _connection, transazione)
                cmd.ExecuteNonQuery()
            End Using

            Using cmdIndex As New SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_data ON Note (DataCreazione)", _connection, transazione)
                cmdIndex.ExecuteNonQuery()
            End Using

            Using cmdIndex As New SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_titolo ON Note (Titolo)", _connection, transazione)
                cmdIndex.ExecuteNonQuery()
            End Using

            Dim sqlImpostazioni As String = "CREATE TABLE IF NOT EXISTS ImpostazioniNote (" &
                                             "Chiave TEXT PRIMARY KEY, " &
                                             "Valore TEXT)"
            Using cmdImp As New SQLiteCommand(sqlImpostazioni, _connection, transazione)
                cmdImp.ExecuteNonQuery()
            End Using

            transazione.Commit()
        Catch ex As Exception
            If transazione IsNot Nothing Then transazione.Rollback()
            _logger.ScriviLog($"InizializzaDatabase Error: {ex.Message}")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Aggiunge la colonna FormatoTesto alla tabella Note se non è già presente
    ''' (database creati prima dell'introduzione dell'editor Word).
    ''' </summary>
    Private Sub AssicuraColonnaFormatoTesto()
        Try
            Dim colonnaPresente As Boolean = False
            Using cmd As New SQLiteCommand("PRAGMA table_info(Note)", _connection)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        If String.Equals(reader("name").ToString(), "FormatoTesto", StringComparison.OrdinalIgnoreCase) Then
                            colonnaPresente = True
                            Exit While
                        End If
                    End While
                End Using
            End Using

            If Not colonnaPresente Then
                Using cmdAlter As New SQLiteCommand("ALTER TABLE Note ADD COLUMN FormatoTesto INTEGER DEFAULT 0", _connection)
                    cmdAlter.ExecuteNonQuery()
                End Using
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Crea la tabella di configurazione delle note, se assente (database creati
    ''' prima dell'introduzione della sezione impostazioni).
    ''' </summary>
    Private Sub AssicuraTabellaImpostazioni()
        Try
            Dim sql As String = "CREATE TABLE IF NOT EXISTS ImpostazioniNote (" &
                             "Chiave TEXT PRIMARY KEY, " &
                             "Valore TEXT)"
            Using cmd As New SQLiteCommand(sql, _connection)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
        End Try
    End Sub

#Region "Configurazione note"

    ''' <summary>
    ''' Legge una impostazione dalla sezione di configurazione delle note.
    ''' Restituisce valoreDefault se la chiave non è mai stata impostata.
    ''' </summary>
    Private Function LeggiImpostazione(chiave As String, valoreDefault As String) As String
        Try
            Using cmd As New SQLiteCommand("SELECT Valore FROM ImpostazioniNote WHERE Chiave = @c", _connection)
                cmd.Parameters.AddWithValue("@c", chiave)
                Dim risultato = cmd.ExecuteScalar()
                If risultato Is Nothing OrElse IsDBNull(risultato) Then Return valoreDefault
                Return risultato.ToString()
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"LeggiImpostazione Error ({chiave}): {ex.Message}")
            Return valoreDefault
        End Try
    End Function

    ''' <summary>
    ''' Scrive (o aggiorna) una impostazione nella sezione di configurazione delle note.
    ''' </summary>
    Private Sub ScriviImpostazione(chiave As String, valore As String)
        Try
            Dim sql As String = "INSERT INTO ImpostazioniNote (Chiave, Valore) VALUES (@c, @v) " &
                                 "ON CONFLICT(Chiave) DO UPDATE SET Valore = @v"
            Using cmd As New SQLiteCommand(sql, _connection)
                cmd.Parameters.AddWithValue("@c", chiave)
                cmd.Parameters.AddWithValue("@v", valore)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"ScriviImpostazione Error ({chiave}): {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Se True, la creazione/modifica delle note tramite l'interfaccia dovrebbe
    ''' usare l'editor Word (tabelle, collegamenti, formattazione) al posto della
    ''' semplice casella di testo. Le note già esistenti mantengono comunque il
    ''' proprio formato originale (vedi Nota.Formato), indipendentemente da questa
    ''' impostazione: questa proprietà riguarda solo le note nuove.
    ''' </summary>
    Public Property UsaEditorWord As Boolean
        Get
            Try
                Return LeggiImpostazione("UsaEditorWord", "0") = "1"
            Catch ex As Exception
                _logger.ScriviLog(ex.Message & ex.StackTrace)
            End Try
        End Get
        Set(value As Boolean)
            Try
                ScriviImpostazione("UsaEditorWord", If(value, "1", "0"))
            Catch ex As Exception
                _logger.ScriviLog(ex.Message & ex.StackTrace)
            End Try
        End Set
    End Property

    ''' <summary>
    ''' Se True, l'apertura in sola lettura delle note già esistenti e salvate in
    ''' formato Testo dovrebbe usare il visualizzatore Word (WGWord) al posto della
    ''' semplice casella di testo, convertendo al volo il contenuto in HTML solo ai
    ''' fini della visualizzazione, senza alterare il formato realmente salvato.
    ''' Le note già salvate in formato Word (Nota.Formato = FormatoNota.Word) vengono
    ''' sempre aperte con il visualizzatore Word in lettura, indipendentemente da
    ''' questa impostazione, perché il loro contenuto non è testo semplice.
    ''' </summary>
    Public Property UsaEditorWordLettura As Boolean
        Get
            Try
                Return LeggiImpostazione("UsaEditorWordLettura", "0") = "1"
            Catch ex As Exception
                _logger.ScriviLog(ex.Message & ex.StackTrace)
            End Try
        End Get
        Set(value As Boolean)
            Try
                ScriviImpostazione("UsaEditorWordLettura", If(value, "1", "0"))
            Catch ex As Exception
                _logger.ScriviLog(ex.Message & ex.StackTrace)
            End Try
        End Set
    End Property

#End Region

    Public Sub ForzaSalvataggioSuDisco()
        Try
            Using cmd As New SQLiteCommand("PRAGMA wal_checkpoint(TRUNCATE);", _connection)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"ForzaSalvataggioSuDisco Error: {ex.Message}")
        End Try
    End Sub

    Public Function CompattaDatabase() As Boolean
        Try
            ForzaSalvataggioSuDisco()

            Using cmdConfig As New SQLiteCommand("PRAGMA temp_store = FILE;", _connection)
                cmdConfig.ExecuteNonQuery()
            End Using

            Using cmdInc As New SQLiteCommand("PRAGMA incremental_vacuum;", _connection)
                cmdInc.ExecuteNonQuery()
            End Using

            Using cmdVacuum As New SQLiteCommand("VACUUM;", _connection)
                cmdVacuum.CommandTimeout = 0
                cmdVacuum.ExecuteNonQuery()
            End Using

            Using cmdRestore As New SQLiteCommand("PRAGMA temp_store = MEMORY;", _connection)
                cmdRestore.ExecuteNonQuery()
            End Using

            Return True
        Catch ex As Exception
            _logger.ScriviLog($"CompattaDatabase Error: {ex.Message}")

            Try
                Using cmdRestore As New SQLiteCommand("PRAGMA temp_store = MEMORY;", _connection)
                    cmdRestore.ExecuteNonQuery()
                End Using
            Catch ex2 As Exception
                _logger.ScriviLog(ex2.Message & ex2.StackTrace)
            End Try
        Catch ex3 As Exception
            _logger.ScriviLog(ex3.Message & ex3.StackTrace)
            Return False
        End Try
    End Function

    Public Function NotaProtetta(id As Integer) As Boolean
        If id <= 0 Then Return False
        Try
            Dim sql As String = "SELECT Protetta FROM Note WHERE Id = @id"
            Using cmd As New SQLiteCommand(sql, _connection)
                cmd.Parameters.AddWithValue("@id", id)
                Dim result = cmd.ExecuteScalar()
                If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                    Return Convert.ToBoolean(Convert.ToInt32(result))
                End If
            End Using
            Return False
        Catch ex As Exception
            _logger.ScriviLog($"NotaProtetta Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function AggiungiNota(titolo As String, testo As String, Optional password As String = "", Optional dataCreazione As Date? = Nothing, Optional formato As FormatoNota = FormatoNota.Testo) As Boolean
        If String.IsNullOrWhiteSpace(titolo) Then Return False

        Dim transazione As SQLiteTransaction = Nothing
        Try
            Dim testoDaSalvare As String = testo
            Dim isProtetta As Boolean = False
            Dim sale As String = String.Empty
            Dim iv As String = String.Empty
            Dim hashPwd As String = String.Empty

            If Not String.IsNullOrEmpty(password) Then
                isProtetta = True
                testoDaSalvare = ProteggiTestoNota(testo, password, sale, iv, hashPwd)
            End If

            transazione = _connection.BeginTransaction()
            Dim sql As String = "INSERT INTO Note (Titolo, Testo, Protetta, Sale, VettoreIV, HashPassword, DataCreazione, ChiaveWG, FormatoTesto) VALUES (@t, @tx, @p, @s, @v, @h, @d, @chiaveWG, @f)"

            Using cmd As New SQLiteCommand(sql, _connection, transazione)
                cmd.Parameters.AddWithValue("@t", titolo.Trim())
                cmd.Parameters.AddWithValue("@tx", testoDaSalvare)
                cmd.Parameters.AddWithValue("@p", If(isProtetta, 1, 0))
                cmd.Parameters.AddWithValue("@s", sale)
                cmd.Parameters.AddWithValue("@v", iv)
                cmd.Parameters.AddWithValue("@h", hashPwd)
                cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                cmd.Parameters.AddWithValue("@f", CInt(formato))

                If dataCreazione.HasValue Then
                    cmd.Parameters.Add("@d", DbType.DateTime).Value = dataCreazione.Value
                Else
                    cmd.Parameters.Add("@d", DbType.DateTime).Value = DBNull.Value
                End If

                cmd.ExecuteNonQuery()
            End Using

            transazione.Commit()
            Return True
        Catch ex As Exception
            If transazione IsNot Nothing Then transazione.Rollback()
            _logger.ScriviLog($"AggiungiNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Crea una nuova nota a partire da un documento Word (.docx) già salvato su disco,
    ''' tipicamente tramite Campi.SalvaWordSuFile su un file temporaneo. Il contenuto
    ''' viene letto e salvato in forma Base64 nella colonna Testo, riusando la stessa
    ''' cifratura delle note testuali se viene fornita una password.
    ''' </summary>
    Public Function AggiungiNotaDocx(titolo As String, percorsoFileDocx As String, Optional password As String = "", Optional dataCreazione As Date? = Nothing) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoFileDocx) OrElse Not File.Exists(percorsoFileDocx) Then Return False
            Dim bytesDocx As Byte() = File.ReadAllBytes(percorsoFileDocx)
            Dim testoBase64 As String = Convert.ToBase64String(bytesDocx)
            Return AggiungiNota(titolo, testoBase64, password, dataCreazione, FormatoNota.Word)
        Catch ex As Exception
            _logger.ScriviLog($"AggiungiNotaDocx Error: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Aggiorna il contenuto di una nota Word esistente a partire da un file .docx su disco
    ''' (es. dopo Campi.SalvaWordSuFile). Non cambia il formato della nota: va usata solo su
    ''' note già salvate come Word (Nota.Formato = FormatoNota.Word).
    ''' </summary>
    Public Function ModificaNotaDocx(id As Integer, percorsoFileDocx As String, Optional password As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoFileDocx) OrElse Not File.Exists(percorsoFileDocx) Then Return False
            Dim bytesDocx As Byte() = File.ReadAllBytes(percorsoFileDocx)
            Dim testoBase64 As String = Convert.ToBase64String(bytesDocx)
            Return ModificaTestoNota(id, testoBase64, password)
        Catch ex As Exception
            _logger.ScriviLog($"ModificaNotaDocx Error: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Decodifica il contenuto di una nota Word e lo scrive su un file .docx, pronto per
    ''' essere caricato in un editor WGWord con Campi.CaricaWordDaFile. Restituisce False
    ''' se la nota non è in formato Word, non esiste, o la password non è corretta.
    ''' </summary>
    Public Function EsportaNotaDocx(id As Integer, percorsoDestinazione As String, Optional password As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoDestinazione) Then Return False
            Dim nota As Nota = RecuperaNota(id, password)
            If nota Is Nothing OrElse nota.Formato <> FormatoNota.Word Then Return False
            If nota.Protetta AndAlso String.IsNullOrEmpty(password) Then Return False

            Dim bytesDocx As Byte() = Convert.FromBase64String(nota.Testo)
            File.WriteAllBytes(percorsoDestinazione, bytesDocx)
            Return True
        Catch ex As Exception
            _logger.ScriviLog($"EsportaNotaDocx Error: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Serializza un WGContenutoPosta (Html, Testo, Immagini con i relativi byte) in
    ''' un'unica stringa XML, pronta per essere salvata nel campo Testo di una nota.
    ''' Le immagini vengono incluse come base64 direttamente dentro l'XML (XmlSerializer
    ''' lo fa automaticamente per le proprietà Byte()), quindi il risultato è autosufficiente.
    ''' </summary>
    Private Function SerializzaContenutoPosta(contenuto As WGContenutoPosta) As String
        Dim serializzatore As New XmlSerializer(GetType(WGContenutoPosta))
        Using sw As New StringWriter()
            serializzatore.Serialize(sw, contenuto)
            Return sw.ToString()
        End Using
    End Function

    ''' <summary>
    ''' Operazione inversa di SerializzaContenutoPosta: ricostruisce l'oggetto
    ''' WGContenutoPosta (Html + immagini) a partire dalla stringa XML salvata nel DB.
    ''' </summary>
    Private Function DeserializzaContenutoPosta(xml As String) As WGContenutoPosta
        Dim serializzatore As New XmlSerializer(GetType(WGContenutoPosta))
        Using sr As New StringReader(xml)
            Return DirectCast(serializzatore.Deserialize(sr), WGContenutoPosta)
        End Using
    End Function

    ''' <summary>
    ''' Aggiunge una nota "stile Word" partendo direttamente dal contenuto in memoria
    ''' generato da Campi.RecuperaContenutoPosta, senza passare da nessun file .docx su
    ''' disco. Elimina completamente il problema di tempistica/lock legato al salvataggio
    ''' su file temporaneo.
    ''' </summary>
    Public Function AggiungiNotaHtml(titolo As String, contenuto As WGContenutoPosta, Optional password As String = "", Optional dataCreazione As Date? = Nothing) As Boolean
        Try
            If contenuto Is Nothing Then Return False
            Dim xml As String = SerializzaContenutoPosta(contenuto)
            Return AggiungiNota(titolo, xml, password, dataCreazione, FormatoNota.Html)
        Catch ex As Exception
            _logger.ScriviLog($"AggiungiNotaHtml Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Aggiorna il contenuto di una nota "stile Word" esistente partendo direttamente
    ''' dal contenuto in memoria (WGContenutoPosta), senza file temporanei.
    ''' </summary>
    Public Function ModificaNotaHtml(id As Integer, contenuto As WGContenutoPosta, Optional password As String = "") As Boolean
        Try
            If contenuto Is Nothing Then Return False
            Dim xml As String = SerializzaContenutoPosta(contenuto)
            Return ModificaTestoNota(id, xml, password)
        Catch ex As Exception
            _logger.ScriviLog($"ModificaNotaHtml Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Recupera una nota "stile Word" (FormatoNota.Html) già ricostruita come
    ''' WGContenutoPosta, pronta per essere passata a Campi.CaricaWordDaHtml.
    ''' Ritorna Nothing se la nota non esiste, non è in questo formato, o la password
    ''' non è corretta.
    ''' </summary>
    Public Function RecuperaContenutoNotaHtml(id As Integer, Optional password As String = "") As WGContenutoPosta
        Try
            Dim nota As Nota = RecuperaNota(id, password)
            If nota Is Nothing OrElse nota.Formato <> FormatoNota.Html Then Return Nothing
            If nota.Protetta AndAlso String.IsNullOrEmpty(password) Then Return Nothing
            If String.IsNullOrWhiteSpace(nota.Testo) Then Return Nothing

            Return DeserializzaContenutoPosta(nota.Testo)
        Catch ex As Exception
            _logger.ScriviLog($"RecuperaContenutoNotaHtml Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function ProteggiNota(id As Integer, password As String) As Boolean
        If id <= 0 OrElse String.IsNullOrEmpty(password) Then Return False

        Dim transazione As SQLiteTransaction = Nothing
        Try
            Dim nota As Nota = RecuperaNota(id)
            If nota Is Nothing OrElse nota.Protetta Then Return False

            Dim sale As String = String.Empty
            Dim iv As String = String.Empty
            Dim hashPwd As String = String.Empty
            Dim testoCriptato As String = ProteggiTestoNota(nota.Testo, password, sale, iv, hashPwd)

            transazione = _connection.BeginTransaction()
            Dim sql As String = "UPDATE Note SET Testo = @tx, Protetta = 1, Sale = @s, VettoreIV = @v, HashPassword = @h WHERE Id = @id"

            Using cmd As New SQLiteCommand(sql, _connection, transazione)
                cmd.Parameters.AddWithValue("@tx", testoCriptato)
                cmd.Parameters.AddWithValue("@s", sale)
                cmd.Parameters.AddWithValue("@v", iv)
                cmd.Parameters.AddWithValue("@h", hashPwd)
                cmd.Parameters.AddWithValue("@id", id)
                cmd.ExecuteNonQuery()
            End Using

            transazione.Commit()
            Return True
        Catch ex As Exception
            If transazione IsNot Nothing Then transazione.Rollback()
            _logger.ScriviLog($"ProteggiNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function RimuoviProtezioneNota(id As Integer, password As String) As Boolean
        If id <= 0 Then Return False

        Dim transazione As SQLiteTransaction = Nothing
        Try
            Dim nota As Nota = RecuperaNota(id)
            If nota Is Nothing OrElse Not nota.Protetta Then Return False

            Dim testoInChiaro As String = ApriTestoProtetto(nota, password, False)

            If testoInChiaro IsNot Nothing Then
                transazione = _connection.BeginTransaction()
                Dim sql As String = "UPDATE Note SET Testo = @tx, Protetta = 0, Sale = NULL, VettoreIV = NULL, HashPassword = NULL WHERE Id = @id"

                Using cmd As New SQLiteCommand(sql, _connection, transazione)
                    cmd.Parameters.AddWithValue("@tx", testoInChiaro)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using

                transazione.Commit()
                Return True
            Else
                Return False
            End If
        Catch ex As Exception
            If transazione IsNot Nothing Then transazione.Rollback()
            _logger.ScriviLog($"RimuoviProtezioneNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function RecuperaNota(id As Integer, Optional password As String = "") As Nota
        If id <= 0 Then Return Nothing

        Try
            Dim risultato As Nota = Nothing
            Dim daConvertire As String = Nothing
            Dim sql As String = "SELECT * FROM Note WHERE Id = @id"
            Using cmd As New SQLiteCommand(sql, _connection)
                cmd.Parameters.AddWithValue("@id", id)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    If reader.Read() Then
                        Dim notaTrovata As New Nota With {
                            .Id = Convert.ToInt32(reader("Id")),
                            .Titolo = reader("Titolo").ToString(),
                            .Testo = reader("Testo").ToString(),
                            .Protetta = Convert.ToBoolean(Convert.ToInt32(reader("Protetta"))),
                            .Sale = If(reader.IsDBNull(reader.GetOrdinal("Sale")), "", reader("Sale").ToString()),
                            .VettoreIV = If(reader.IsDBNull(reader.GetOrdinal("VettoreIV")), "", reader("VettoreIV").ToString()),
                            .HashPassword = If(reader.IsDBNull(reader.GetOrdinal("HashPassword")), "", reader("HashPassword").ToString())
                        }

                        Dim colData As Integer = reader.GetOrdinal("DataCreazione")
                        If Not reader.IsDBNull(colData) Then
                            Try
                                notaTrovata.DataCreazione = reader.GetDateTime(colData)
                            Catch ex As Exception
                                _logger.ScriviLog(ex.Message & ex.StackTrace)
                                Dim dStr As String = reader(colData).ToString()
                                If Date.TryParse(dStr, Nothing) Then
                                    notaTrovata.DataCreazione = Date.Parse(dStr)
                                End If
                            End Try
                        Else
                            notaTrovata.DataCreazione = Nothing
                        End If

                        Try
                            Dim colFormato As Integer = reader.GetOrdinal("FormatoTesto")
                            If Not reader.IsDBNull(colFormato) Then
                                notaTrovata.Formato = CType(Convert.ToInt32(reader(colFormato)), FormatoNota)
                            End If
                        Catch ex As Exception
                            _logger.ScriviLog(ex.Message & ex.StackTrace)
                            notaTrovata.Formato = FormatoNota.Testo
                        End Try

                        If notaTrovata.Protetta AndAlso Not String.IsNullOrEmpty(password) Then
                            Try
                                Dim eraPrecedente As Boolean = Not WGCifraturaNativa.EProtetto(notaTrovata.Testo)
                                Dim chiaro As String = ApriTestoProtetto(notaTrovata, password, False)
                                If chiaro IsNot Nothing Then
                                    notaTrovata.Testo = chiaro
                                    If eraPrecedente Then daConvertire = chiaro
                                End If
                            Catch ex As Exception
                                _logger.ScriviLog($"CLSNote: nota {id} non apribile ({ex.GetType().Name}: {ex.Message.Replace(vbCrLf, " ").Replace(vbLf, " ")})")
                            End Try
                        End If

                        risultato = notaTrovata
                    End If
                End Using
            End Using
            ' la nota era nel formato precedente e la password era giusta: si riscrive con la libreria nativa (a lettore chiuso)
            If daConvertire IsNot Nothing AndAlso risultato IsNot Nothing Then MigraNota(id, daConvertire, password)
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog($"RecuperaNota - Errore: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Function VisualizzaTestoNota(id As Integer, Optional password As String = "") As String
        If id <= 0 Then Return String.Empty
        Try
            Dim nota As Nota = RecuperaNota(id)
            If nota Is Nothing Then Return String.Empty

            If nota.Protetta Then
                If String.IsNullOrEmpty(password) Then Return "Contenuto Protetto"

                Dim chiaro As String = ApriTestoProtetto(nota, password, True)
                If chiaro IsNot Nothing Then
                    Return chiaro
                Else
                    Return "Password Errata"
                End If
            Else
                Return nota.Testo
            End If
        Catch ex As Exception
            _logger.ScriviLog($"VisualizzaTestoNota Error: {ex.Message}")
            Return String.Empty
        End Try
    End Function

    Public Function ModificaTestoNota(id As Integer, nuovoTesto As String, Optional password As String = "") As Boolean
        If id <= 0 Then Return False

        Dim transazione As SQLiteTransaction = Nothing
        Try
            Dim nota As Nota = RecuperaNota(id)
            If nota Is Nothing Then Return False

            Dim testoDaSalvare As String = nuovoTesto
            Dim nuovoSale As String = String.Empty
            Dim nuovoIv As String = String.Empty
            Dim nuovoHash As String = String.Empty

            If nota.Protetta Then
                ' la password si verifica aprendo il testo attuale; una nota del formato precedente passa al nuovo salvandola
                If ApriTestoProtetto(nota, password, False) Is Nothing Then Return False
                testoDaSalvare = ProteggiTestoNota(nuovoTesto, password, nuovoSale, nuovoIv, nuovoHash)
            End If

            transazione = _connection.BeginTransaction()
            Dim sql As String = If(nota.Protetta,
                                   "UPDATE Note SET Testo = @tx, Sale = @s, VettoreIV = @v, HashPassword = @h WHERE Id = @id",
                                   "UPDATE Note SET Testo = @tx WHERE Id = @id")
            Using cmd As New SQLiteCommand(sql, _connection, transazione)
                cmd.Parameters.AddWithValue("@tx", testoDaSalvare)
                If nota.Protetta Then
                    cmd.Parameters.AddWithValue("@s", nuovoSale)
                    cmd.Parameters.AddWithValue("@v", nuovoIv)
                    cmd.Parameters.AddWithValue("@h", nuovoHash)
                End If
                cmd.Parameters.AddWithValue("@id", id)
                cmd.ExecuteNonQuery()
            End Using
            transazione.Commit()
            Return True
        Catch ex As Exception
            If transazione IsNot Nothing Then transazione.Rollback()
            _logger.ScriviLog($"ModificaTestoNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function ModificaTitoloNota(id As Integer, nuovoTitolo As String) As Boolean
        If id <= 0 OrElse String.IsNullOrWhiteSpace(nuovoTitolo) Then Return False

        Try
            Using trans As SQLiteTransaction = _connection.BeginTransaction()
                Dim sql As String = "UPDATE Note SET Titolo = @t WHERE Id = @id"
                Using cmd As New SQLiteCommand(sql, _connection, trans)
                    cmd.Parameters.AddWithValue("@t", nuovoTitolo.Trim())
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
                trans.Commit()
                Return True
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"ModificaTitoloNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function EliminaNota(id As Integer) As Boolean
        If id <= 0 Then Return False

        Try
            Using trans As SQLiteTransaction = _connection.BeginTransaction()
                Dim sql As String = "DELETE FROM Note WHERE Id = @id"
                Using cmd As New SQLiteCommand(sql, _connection, trans)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
                trans.Commit()
                Return True
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"EliminaNota Error: {ex.Message}")
            Return False
        End Try
    End Function

    Public Function ContaNoteTotali(Optional dataDa As Date? = Nothing, Optional dataA As Date? = Nothing) As Integer
        Try
            Dim sb As New StringBuilder("SELECT COUNT(*) FROM Note WHERE 1=1")
            If dataDa.HasValue Then sb.Append(" AND DataCreazione >= @da")
            If dataA.HasValue Then sb.Append(" AND DataCreazione <= @a")

            Using cmd As New SQLiteCommand(sb.ToString(), _connection)
                If dataDa.HasValue Then cmd.Parameters.Add("@da", DbType.DateTime).Value = dataDa.Value
                If dataA.HasValue Then cmd.Parameters.Add("@a", DbType.DateTime).Value = dataA.Value
                Return Convert.ToInt32(cmd.ExecuteScalar())
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"ContaNoteTotali Error: {ex.Message}")
            Return 0
        End Try
    End Function

    Public Function RecuperaNotePaginata(pagina As Integer, quantitaPerPagina As Integer, ordine As OrdinamentoNote, Optional dataDa As Date? = Nothing, Optional dataA As Date? = Nothing) As List(Of ElementoLista)
        Dim lista As New List(Of ElementoLista)
        Try
            If pagina < 1 Then pagina = 1
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina

            Dim sb As New StringBuilder("SELECT Id, Titolo, Protetta, DataCreazione FROM Note WHERE 1=1")

            If dataDa.HasValue Then sb.Append(" AND DataCreazione >= @da")
            If dataA.HasValue Then sb.Append(" AND DataCreazione <= @a")

            Select Case ordine
                Case OrdinamentoNote.PiuRecenti
                    sb.Append(" ORDER BY DataCreazione DESC")
                Case OrdinamentoNote.MenoRecenti
                    sb.Append(" ORDER BY DataCreazione ASC")
                Case OrdinamentoNote.AlfabeticoAZ
                    sb.Append(" ORDER BY Titolo ASC")
                Case OrdinamentoNote.AlfabeticoZA
                    sb.Append(" ORDER BY Titolo DESC")
            End Select

            sb.Append(" LIMIT @lim OFFSET @off")

            Using cmd As New SQLiteCommand(sb.ToString(), _connection)
                cmd.Parameters.AddWithValue("@lim", quantitaPerPagina)
                cmd.Parameters.AddWithValue("@off", offset)

                If dataDa.HasValue Then cmd.Parameters.Add("@da", DbType.DateTime).Value = dataDa.Value
                If dataA.HasValue Then cmd.Parameters.Add("@a", DbType.DateTime).Value = dataA.Value

                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        lista.Add(New ElementoLista(reader("Titolo").ToString(), reader("Id").ToString()))
                    End While
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"RecuperaNotePaginata Error: {ex.Message}")
            Return New List(Of ElementoLista)()
        End Try
    End Function

    Public Function ContaRisultatiRicerca(testoRicerca As String, Optional dataDa As Date? = Nothing, Optional dataA As Date? = Nothing) As Integer
        Try
            If String.IsNullOrWhiteSpace(testoRicerca) AndAlso Not dataDa.HasValue Then Return 0

            Dim sb As New StringBuilder("SELECT COUNT(*) FROM Note WHERE (Titolo LIKE @q OR (Protetta = 0 AND Testo LIKE @q))")

            If dataDa.HasValue Then sb.Append(" AND DataCreazione >= @da")
            If dataA.HasValue Then sb.Append(" AND DataCreazione <= @a")

            Using cmd As New SQLiteCommand(sb.ToString(), _connection)
                cmd.Parameters.AddWithValue("@q", "%" & testoRicerca & "%")
                If dataDa.HasValue Then cmd.Parameters.Add("@da", DbType.DateTime).Value = dataDa.Value
                If dataA.HasValue Then cmd.Parameters.Add("@a", DbType.DateTime).Value = dataA.Value
                Return Convert.ToInt32(cmd.ExecuteScalar())
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"ContaRisultatiRicerca Error: {ex.Message}")
            Return 0
        End Try
    End Function

    Public Function CercaNotePaginata(testoRicerca As String, pagina As Integer, quantitaPerPagina As Integer, ordine As OrdinamentoNote, Optional dataDa As Date? = Nothing, Optional dataA As Date? = Nothing) As List(Of ElementoLista)
        Dim lista As New List(Of ElementoLista)
        Try
            If pagina < 1 Then pagina = 1
            Dim offset As Integer = (pagina - 1) * quantitaPerPagina

            Dim sb As New StringBuilder("SELECT Id, Titolo, Protetta, DataCreazione FROM Note WHERE (Titolo LIKE @q OR (Protetta = 0 AND Testo LIKE @q))")

            If dataDa.HasValue Then sb.Append(" AND DataCreazione >= @da")
            If dataA.HasValue Then sb.Append(" AND DataCreazione <= @a")

            Select Case ordine
                Case OrdinamentoNote.PiuRecenti
                    sb.Append(" ORDER BY DataCreazione DESC")
                Case OrdinamentoNote.MenoRecenti
                    sb.Append(" ORDER BY DataCreazione ASC")
                Case OrdinamentoNote.AlfabeticoAZ
                    sb.Append(" ORDER BY Titolo ASC")
                Case OrdinamentoNote.AlfabeticoZA
                    sb.Append(" ORDER BY Titolo DESC")
            End Select

            sb.Append(" LIMIT @lim OFFSET @off")

            Using cmd As New SQLiteCommand(sb.ToString(), _connection)
                cmd.Parameters.AddWithValue("@q", "%" & testoRicerca & "%")
                cmd.Parameters.AddWithValue("@lim", quantitaPerPagina)
                cmd.Parameters.AddWithValue("@off", offset)

                If dataDa.HasValue Then cmd.Parameters.Add("@da", DbType.DateTime).Value = dataDa.Value
                If dataA.HasValue Then cmd.Parameters.Add("@a", DbType.DateTime).Value = dataA.Value

                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        lista.Add(New ElementoLista(reader("Titolo").ToString(), reader("Id").ToString()))
                    End While
                End Using
            End Using
            Return lista
        Catch ex As Exception
            _logger.ScriviLog($"CercaNotePaginata Error: {ex.Message}")
            Return New List(Of ElementoLista)()
        End Try
    End Function

    ' Le note protette si cifrano con la libreria nativa (AES-GCM, chiave dalla password con PBKDF2): il testo diventa "WGX:..." e
    ' la password giusta e quella con cui il testo si riapre. Sale, vettore e hash restano solo per le note del formato precedente
    ' (PBKDF2-SHA1 + AES-CBC), che passano al formato nuovo la prima volta che si aprono con la password giusta.
    Private Const ScopoNota As String = "nota"
    Private Const MarcatoreFormatoNuovo As String = "WGX2"
    Private Shared _avvisoSistemaPrecedente As Boolean = False

    ' Tentativo con la libreria nativa. Restituisce "" se non e utilizzabile o non riesce: il motivo preciso e nel log
    ' e chi chiama passa al sistema precedente.
    Private Function CifraNota(testo As String, password As String) As String
        Try
            If Not WGCifraturaNativa.Funzionante Then
                ' il motivo e gia nel log (una volta sola, scritto da WGCifraturaNativa): qui non si ripete a ogni nota
                If Not _avvisoSistemaPrecedente Then
                    _avvisoSistemaPrecedente = True
                    _logger.ScriviLog("CLSNote: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), le note protette usano il sistema precedente.")
                End If
                Return ""
            End If
            Dim blob As Byte() = WGCifraturaNativa.Proteggi(ScopoNota, Encoding.UTF8.GetBytes(If(testo, "")), password)
            If blob Is Nothing Then
                _logger.ScriviLog("CLSNote: cifratura nativa non riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return ""
            End If
            ' Prima di accettarla si rilegge con la stessa password: se il testo non torna uguale si usa il sistema precedente.
            Dim esitoRilettura As Integer = 0
            Dim riletto As Byte() = WGCifraturaNativa.Rivela(ScopoNota, blob, password, esitoRilettura)
            If riletto Is Nothing OrElse Not String.Equals(Encoding.UTF8.GetString(riletto), If(testo, ""), StringComparison.Ordinal) Then
                _logger.ScriviLog("CLSNote: la verifica di rilettura della nota cifrata non e riuscita (" & WGCifraturaNativa.DescriviCodice(esitoRilettura) & "; " & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return ""
            End If
            Return WGCifraturaNativa.PrefissoTesto & Convert.ToBase64String(blob)
        Catch ex As Exception
            _logger.ScriviLog("CLSNote: errore nella cifratura nativa (" & ex.GetType().Name & ": " & ex.Message & "), uso il sistema precedente.")
            Return ""
        End Try
    End Function

    ' Cifra il testo di una nota protetta e dice con quali campi salvarlo. Libreria nativa se funziona (sale, vettore e hash restano
    ' vuoti), altrimenti il sistema precedente (PBKDF2-SHA1 + AES-CBC con sale, vettore e hash), che resta leggibile e si converte
    ' piu avanti.
    Private Function ProteggiTestoNota(testo As String, password As String, ByRef sale As String, ByRef iv As String, ByRef hashPwd As String) As String
        Dim nuovo As String = CifraNota(testo, password)
        If nuovo <> "" Then
            sale = String.Empty
            iv = String.Empty
            hashPwd = MarcatoreFormatoNuovo
            Return nuovo
        End If
        Dim saltBytes As Byte() = GeneraBytesCasualiBytes(32)
        sale = Convert.ToBase64String(saltBytes)
        Dim derivedKey As Byte() = DerivaChiave(password, saltBytes)
        Dim ivBytes As Byte() = GeneraBytesCasualiBytes(16)
        iv = Convert.ToBase64String(ivBytes)
        hashPwd = CalcolaHashPassword(password, saltBytes)
        Return CriptaAES(testo, derivedKey, ivBytes)
    End Function

    ''' <summary>
    ''' Restituisce il testo in chiaro di una nota protetta, oppure Nothing se la password e sbagliata.
    ''' Con migra, una nota del formato precedente aperta con la password giusta viene riscritta nel formato nuovo.
    ''' </summary>
    Private Function ApriTestoProtetto(nota As Nota, password As String, migra As Boolean) As String
        If String.IsNullOrEmpty(password) Then Return Nothing
        If WGCifraturaNativa.EProtetto(nota.Testo) Then
            Dim esito As Integer = 0
            Dim chiaroNuovo As String = WGCifraturaNativa.RivelaTesto(ScopoNota, nota.Testo, password, esito)
            If esito <> 0 Then Return Nothing
            Return chiaroNuovo
        End If

        Dim saltBytes As Byte() = Convert.FromBase64String(nota.Sale)
        If CalcolaHashPassword(password, saltBytes) <> nota.HashPassword Then Return Nothing
        Dim chiave As Byte() = DerivaChiave(password, saltBytes)
        Dim chiaro As String = DecriptaAES(nota.Testo, chiave, Convert.FromBase64String(nota.VettoreIV))
        If migra Then MigraNota(nota.Id, chiaro, password)
        Return chiaro
    End Function

    Private Sub MigraNota(id As Integer, chiaro As String, password As String)
        Try
            Dim nuovo As String = CifraNota(chiaro, password)
            If nuovo = "" Then Return ' la nota resta nel sistema precedente, il motivo e nel log
            Using cmd As New SQLiteCommand("UPDATE Note SET Testo = @tx, Sale = '', VettoreIV = '', HashPassword = @h WHERE Id = @id", _connection)
                cmd.Parameters.AddWithValue("@tx", nuovo)
                cmd.Parameters.AddWithValue("@h", MarcatoreFormatoNuovo)
                cmd.Parameters.AddWithValue("@id", id)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog($"CLSNote: conversione della nota non riuscita ({ex.GetType().Name}: {ex.Message.Replace(vbCrLf, " ").Replace(vbLf, " ")}), resta nel sistema precedente.")
        End Try
    End Sub

    Private Function GeneraBytesCasualiBytes(lunghezza As Integer) As Byte()
        Try
            Dim bytes As Byte() = New Byte(lunghezza - 1) {}
            Using rng As New RNGCryptoServiceProvider()
                rng.GetBytes(bytes)
            End Using
            Return bytes
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function DerivaChiave(password As String, sale As Byte()) As Byte()
        Try
            Using pbkdf2 As New Rfc2898DeriveBytes(password, sale, 50000)
                Return pbkdf2.GetBytes(32)
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function CalcolaHashPassword(password As String, sale As Byte()) As String
        Try
            Using sha256 As SHA256 = SHA256.Create()
                Dim passwordBytes As Byte() = Encoding.UTF8.GetBytes(password)
                Dim combinedBytes As Byte() = New Byte(passwordBytes.Length + sale.Length - 1) {}
                Buffer.BlockCopy(passwordBytes, 0, combinedBytes, 0, passwordBytes.Length)
                Buffer.BlockCopy(sale, 0, combinedBytes, passwordBytes.Length, sale.Length)
                Return Convert.ToBase64String(sha256.ComputeHash(combinedBytes))
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function CriptaAES(testoInChiaro As String, key As Byte(), iv As Byte()) As String
Try
        Using aesAlg As Aes = Aes.Create()
                aesAlg.Key = key
                aesAlg.IV = iv
                aesAlg.Mode = CipherMode.CBC
                aesAlg.Padding = PaddingMode.PKCS7

                Dim encryptor As ICryptoTransform = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV)
                Using msEncrypt As New MemoryStream()
                    Using csEncrypt As New CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)
                        Using swEncrypt As New StreamWriter(csEncrypt)
                            swEncrypt.Write(testoInChiaro)
                        End Using
                        Return Convert.ToBase64String(msEncrypt.ToArray())
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Private Function DecriptaAES(testoCriptato As String, key As Byte(), iv As Byte()) As String
try
        Using aesAlg As Aes = Aes.Create()
            aesAlg.Key = key
            aesAlg.IV = iv
            aesAlg.Mode = CipherMode.CBC
            aesAlg.Padding = PaddingMode.PKCS7

            Dim decryptor As ICryptoTransform = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV)
            Dim cipherBytes As Byte() = Convert.FromBase64String(testoCriptato)

            Using msDecrypt As New MemoryStream(cipherBytes)
                Using csDecrypt As New CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read)
                    Using srDecrypt As New StreamReader(csDecrypt)
                        Return srDecrypt.ReadToEnd()
                    End Using
                End Using
            End Using
        End Using
        Catch ex As Exception
        _logger.ScriviLog(Ex.message & ex.StackTrace)
            ' un errore qui non deve dare un risultato vuoto: chi chiama lo salverebbe al posto dei dati
            Throw
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            ForzaSalvataggioSuDisco()
            If _connection IsNot Nothing Then
                If _connection.State = ConnectionState.Open Then
                    _connection.Close()
                End If
                _connection.Dispose()
            End If
        Catch ex As Exception
            _logger.ScriviLog(ex.Message & ex.StackTrace)
        Finally
            _connection = Nothing
        End Try
    End Sub
End Class