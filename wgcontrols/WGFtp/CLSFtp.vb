Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Data.SQLite
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Net.Sockets
Imports FluentFTP
Imports Renci.SshNet
Imports Renci.SshNet.Common
Imports WGDb

' Gestione delle sessioni di comunicazione FTP e SFTP, trasferimento file, cifratura sicura delle password e gestione dei log.
Public Enum TipoProtocolloFtp
    Ftp
    Ftps
    Sftp
End Enum

Public Class InfoServerFtp
    Public Property Id As Integer
    Public Property Nome As String = ""
    Public Property Host As String = ""
    Public Property Porta As Integer = 21
    Public Property Utente As String = ""
    Public Property PasswordCriptata As String = ""
    Public Property Protocollo As TipoProtocolloFtp = TipoProtocolloFtp.Ftp
    Public Property CartellaIniziale As String = "/"
    Public Property UltimoUtilizzo As DateTime = DateTime.MinValue
    Public Property LimiteVelocitaKBs As Integer = 0
    Public Property SalvaPassword As Boolean = True
    Public Property UltimaCartella As String = ""
End Class

Public Class InfoImpostazioniFtp
    Public Property LimiteVelocitaKBs As Integer = 0
    Public Property SalvaSempreServer As Boolean = False
    Public Property SalvaSemprePassword As Boolean = False
    ''' <summary>
    ''' Modalita' dati FTP: vero per la modalita' attiva (PORT), falso per la passiva (PASV),
    ''' che e' quella predefinita e funziona dietro la maggior parte dei router.
    ''' </summary>
    Public Property ModalitaAttivaFtp As Boolean = False
    ''' <summary>
    ''' Nome della codifica da imporre ai nomi file FTP (per esempio "windows-1252" o
    ''' "iso-8859-1") sui server vecchi che non annunciano UTF-8. Vuoto significa automatica.
    ''' </summary>
    Public Property CodificaNomiFile As String = ""
    ''' <summary>
    ''' Attiva la registrazione diagnostica delle operazioni remote nel log.
    ''' </summary>
    Public Property LogProtocollo As Boolean = False
    ''' <summary>
    ''' Invia periodicamente un comando di mantenimento per evitare che il server chiuda le
    ''' sessioni persistenti dopo qualche minuto di inattivita'.
    ''' </summary>
    Public Property MantieniConnessioneAttiva As Boolean = True
    ''' <summary>
    ''' Minuti da sommare alle date restituite dal server per riportarle all'ora locale,
    ''' quando il server usa un fuso diverso e non lo dichiara.
    ''' </summary>
    Public Property OffsetOrarioServerMinuti As Integer = 0
End Class

Public Class InfoElementoFtp
    Public Property Nome As String = ""
    Public Property PercorsoCompleto As String = ""
    Public Property EDirectory As Boolean = False
    Public Property Dimensione As Long = 0
    Public Property DataModifica As DateTime = DateTime.MinValue
    Public Property Permessi As String = ""
    Public Property Nascosto As Boolean = False
    ''' <summary>
    ''' Vero quando la voce e' un collegamento simbolico. EDirectory riporta comunque la
    ''' natura della destinazione, cosi' l'elemento si comporta come cio' a cui punta.
    ''' </summary>
    Public Property ECollegamento As Boolean = False
    Public Property DestinazioneCollegamento As String = ""
    Public Property Proprietario As String = ""
    Public Property Gruppo As String = ""
End Class

''' <summary>
''' Riepilogo del contenuto di una cartella remota: quanti file, quante sottocartelle e
''' quanti byte in tutto. Serve a preparare un progresso attendibile prima di trasferire.
''' </summary>
Public Class InfoContenutoCartella
    Public Property FileTotali As Integer = 0
    Public Property CartelleTotali As Integer = 0
    Public Property ByteTotali As Long = 0
End Class

''' <summary>
''' Informazioni generali sul server remoto (non sul singolo file/cartella):
''' sistema operativo e stato dello spazio disco.
''' </summary>
Public Class InfoServerRemoto
    Public Property SistemaOperativo As String = ""
    Public Property SpazioTotale As Long? = Nothing
    Public Property SpazioDisponibile As Long? = Nothing
    Public ReadOnly Property SpazioUtilizzato As Long?
        Get
            If SpazioTotale.HasValue AndAlso SpazioDisponibile.HasValue Then
                Return Math.Max(0L, SpazioTotale.Value - SpazioDisponibile.Value)
            End If
            Return Nothing
        End Get
    End Property
    ''' <summary>
    ''' Eventuale nota da mostrare all'utente quando alcune informazioni non sono
    ''' disponibili (es. server FTP che non espone lo spazio disco).
    ''' </summary>
    Public Property MessaggioErrore As String = ""
    ''' <summary>
    ''' Vero quando il server remoto e' stato riconosciuto come Windows: in quel caso i
    ''' comandi Unix (uname, df, chmod) non esistono e vanno usate le alternative native
    ''' del sistema (fsutil, wmic, PowerShell) oppure le estensioni del protocollo SFTP.
    ''' </summary>
    Public Property ServerWindows As Boolean = False
End Class

Public Class SessioneFtp
    Implements IDisposable

    Friend ClientFtp As FtpClient
    Friend ClientSftp As SftpClient
    Friend Server As InfoServerFtp
    Public Property ImprontaSftp As String = ""
    ''' <summary>
    ''' Esito memorizzato del riconoscimento del sistema del server (Windows oppure no),
    ''' calcolato la prima volta che serve per non ripetere l'interrogazione a ogni elenco.
    ''' </summary>
    Friend SistemaWindowsRilevato As Boolean? = Nothing
    Private _disposed As Boolean = False

    ''' <summary>
    ''' Oggetto di sincronizzazione. FtpClient/SftpClient non sono thread-safe: qualunque
    ''' codice (UI, monitor in background, ecc.) che usa questa sessione da thread diversi
    ''' deve avvolgere le chiamate in "SyncLock sessione.LockOggetto".
    ''' </summary>
    Public ReadOnly Property LockOggetto As New Object()

    Public ReadOnly Property Connessa As Boolean
        Get
            Try
                If Server Is Nothing Then Return False
                If Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Return ClientSftp IsNot Nothing AndAlso ClientSftp.IsConnected
                Else
                    Return ClientFtp IsNot Nothing AndAlso ClientFtp.IsConnected
                End If
            Catch ex As Exception
                CLSFtp.ScriviLog("SessioneFtp.Connessa - Errore", ex)
                Return False
            End Try
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _disposed Then Return
            _disposed = True
            Try
                If ClientFtp IsNot Nothing Then
                    ClientFtp.Dispose()
                End If
            Catch ex As Exception
                CLSFtp.ScriviLog("SessioneFtp.Dispose - Errore chiusura client FTP", ex)
            End Try
            Try
                If ClientSftp IsNot Nothing Then
                    Try
                        If ClientSftp.IsConnected Then ClientSftp.Disconnect()
                    Catch ex As Exception
                        CLSFtp.ScriviLog("SessioneFtp.Dispose - Errore disconnessione client SFTP", ex)
                    End Try
                    ClientSftp.Dispose()
                End If
            Catch ex As Exception
                CLSFtp.ScriviLog("SessioneFtp.Dispose - Errore chiusura client SFTP", ex)
            End Try
        Catch ex As Exception
            CLSFtp.ScriviLog("SessioneFtp.Dispose - Errore", ex)
        End Try
    End Sub
End Class

Friend Class ThrottledStream
    Inherits Stream

    Private ReadOnly _streamBase As Stream
    Private ReadOnly _bytesAlSecondo As Long
    Private _ultimoOrario As DateTime
    Private _byteContati As Long

    Public Sub New(streamBase As Stream, bytesAlSecondo As Long)
        _streamBase = streamBase
        _bytesAlSecondo = bytesAlSecondo
        _ultimoOrario = DateTime.Now
        _byteContati = 0
    End Sub

    Private Sub Limita(quantita As Integer)
        Try
            If _bytesAlSecondo <= 0 Then Return
            _byteContati += quantita
            Dim secondiTrascorsi As Double = (DateTime.Now - _ultimoOrario).TotalSeconds
            Dim secondiAttesi As Double = _byteContati / _bytesAlSecondo
            If secondiAttesi > secondiTrascorsi Then
                Dim attesaMs As Integer = CInt(Math.Min((secondiAttesi - secondiTrascorsi) * 1000.0R, 2000.0R))
                If attesaMs > 0 Then Thread.Sleep(attesaMs)
            End If
            If secondiTrascorsi > 1 Then
                _ultimoOrario = DateTime.Now
                _byteContati = 0
            End If
        Catch ex As Exception
            CLSFtp.ScriviLog("ThrottledStream.Limita - Errore", ex)
        End Try
    End Sub

    Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
        Try
            Dim letti As Integer = _streamBase.Read(buffer, offset, count)
            Limita(letti)
            Return letti
        Catch ex As Exception
            CLSFtp.ScriviLog("ThrottledStream.Read - Errore", ex)
            Throw
        End Try
    End Function

    Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
        Try
            _streamBase.Write(buffer, offset, count)
            Limita(count)
        Catch ex As Exception
            CLSFtp.ScriviLog("ThrottledStream.Write - Errore", ex)
            Throw
        End Try
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return _streamBase.CanRead
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return _streamBase.CanWrite
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return _streamBase.CanSeek
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Return _streamBase.Length
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Return _streamBase.Position
        End Get
        Set(value As Long)
            _streamBase.Position = value
        End Set
    End Property

    Public Overrides Sub Flush()
        _streamBase.Flush()
    End Sub

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Return _streamBase.Seek(offset, origin)
    End Function

    Public Overrides Sub SetLength(value As Long)
        _streamBase.SetLength(value)
    End Sub
End Class

Public Class CLSFtp
    Private Const CARTELLA_DATI_FTP As String = "Dati\Ftp"
    Private Const PROFONDITA_MASSIMA_RICORSIONE As Integer = 40
    Private Const DIMENSIONE_BUFFER_TRASFERIMENTO As Integer = 65536
    Private Const INTERVALLO_KEEPALIVE_MS As Integer = 30000
    ' Connessione FTP: 15 secondi come la connessione SFTP (ConnectionInfo.Timeout), con un
    ' tentativo in più dopo una pausa se l'errore è di rete (tempo scaduto o socket non raggiungibile).
    Private Const TIMEOUT_CONNESSIONE_FTP_MS As Integer = 15000
    Private Const TENTATIVI_CONNESSIONE_FTP As Integer = 2
    Private Const ATTESA_TRA_TENTATIVI_MS As Integer = 1000
    Public Const PORTA_FTPS_IMPLICITO As Integer = 990
    Private Const NOME_FILE_DB_FTP As String = "ftpservers.db"
    ' Cartella dei log: %LocalAppData%\WinGabriel\logs\logs.txt
    Private Const CARTELLA_LOG_FTP As String = ""
    Private Const NOME_FILE_LOG_FTP As String = "logs.txt"
    Private Const MAX_SERVER_RECENTI As Integer = 15
    Private Shared ReadOnly EntropiaFtp As Byte() = {84, 21, 233, 12, 189, 77, 5, 240, 156, 63, 91, 18, 202, 47, 111, 9}
    Private Shared ReadOnly LockLog As New Object()

    Friend Shared Sub ScriviLog(messaggio As String)
        Try
            SyncLock LockLog
                Dim cartella As String = WGDb.WGPercorsiApplicazione.CartellaLog()
                If Not Directory.Exists(cartella) Then
                    Directory.CreateDirectory(cartella)
                End If
                Dim pathLog As String = Path.Combine(cartella, NOME_FILE_LOG_FTP)
                Dim riga As String = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {messaggio}{Environment.NewLine}"
                File.AppendAllText(pathLog, riga, Encoding.UTF8)
            End SyncLock
        Catch
        End Try
    End Sub

    Friend Shared Sub ScriviLog(messaggio As String, ex As Exception)
        Try
            If ex Is Nothing Then
                ScriviLog(messaggio)
                Return
            End If
            ScriviLog(messaggio & " - Tipo: " & ex.GetType().FullName & " | Messaggio: " & ex.Message)
        Catch
        End Try
    End Sub

    Friend Shared Sub ScriviLogMigrazione(messaggio As String, ex As Exception)
        Try
            If ex IsNot Nothing AndAlso Not String.IsNullOrEmpty(ex.Message) AndAlso ex.Message.IndexOf("duplicate column", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return
            End If
        Catch
        End Try
    End Sub

    Private ReadOnly Property ConnectionStringFtp As String
        Get
            Try
                Dim pathDb As String = ""
                Try
                    pathDb = WGDatabase.PercorsoFileDati("Ftp", NOME_FILE_DB_FTP)
                Catch
                    pathDb = ""
                End Try
                If String.IsNullOrWhiteSpace(pathDb) Then
                    Dim cartella As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_FTP)
                    If Not Directory.Exists(cartella) Then
                        Directory.CreateDirectory(cartella)
                    End If
                    pathDb = Path.Combine(cartella, NOME_FILE_DB_FTP)
                End If
                Return $"Data Source={pathDb};Version=3;"
            Catch ex As Exception
                ScriviLog("CLSFtp.ConnectionStringFtp - Errore", ex)
                Return ""
            End Try
        End Get
    End Property

    Private Sub InizializzaDbFtp()
        Try
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS ServerPreferiti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Host TEXT, Porta INTEGER, Utente TEXT, PasswordCriptata TEXT, Protocollo INTEGER, CartellaIniziale TEXT, UltimoUtilizzo TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS ServerRecenti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Host TEXT, Porta INTEGER, Utente TEXT, PasswordCriptata TEXT, Protocollo INTEGER, CartellaIniziale TEXT, UltimoUtilizzo TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS ServerSalvati (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Host TEXT, Porta INTEGER, Utente TEXT, PasswordCriptata TEXT, Protocollo INTEGER, CartellaIniziale TEXT, UltimoUtilizzo TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS ImpronteHostSftp (Host TEXT, Porta INTEGER, Impronta TEXT, UltimaVerifica TEXT, PRIMARY KEY (Host, Porta));" &
                                      "CREATE TABLE IF NOT EXISTS ImpostazioniFtp (Id INTEGER PRIMARY KEY CHECK (Id = 1), LimiteVelocitaKBs INTEGER, SalvaSempreServer INTEGER, SalvaSemprePassword INTEGER);" &
                                      "CREATE TABLE IF NOT EXISTS ChiaviFtp (Nome TEXT PRIMARY KEY, Valore TEXT);"
                    cmd.ExecuteNonQuery()
                End Using
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerPreferiti ADD COLUMN Protocollo INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerPreferiti ADD COLUMN Protocollo INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerRecenti ADD COLUMN Protocollo INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerRecenti ADD COLUMN Protocollo INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerPreferiti ADD COLUMN LimiteVelocitaKBs INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerPreferiti ADD COLUMN LimiteVelocitaKBs INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerRecenti ADD COLUMN LimiteVelocitaKBs INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerRecenti ADD COLUMN LimiteVelocitaKBs INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerPreferiti ADD COLUMN SalvaPassword INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerPreferiti ADD COLUMN SalvaPassword INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerRecenti ADD COLUMN SalvaPassword INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerRecenti ADD COLUMN SalvaPassword INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerPreferiti ADD COLUMN UltimaCartella TEXT", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerPreferiti ADD COLUMN UltimaCartella TEXT", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerRecenti ADD COLUMN UltimaCartella TEXT", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerRecenti ADD COLUMN UltimaCartella TEXT", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerSalvati ADD COLUMN LimiteVelocitaKBs INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerSalvati ADD COLUMN LimiteVelocitaKBs INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerSalvati ADD COLUMN SalvaPassword INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerSalvati ADD COLUMN SalvaPassword INTEGER", ex)
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ServerSalvati ADD COLUMN UltimaCartella TEXT", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch ex As Exception
                    ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ServerSalvati ADD COLUMN UltimaCartella TEXT", ex)
                End Try
                Dim colonneImpostazioni As String() = {
                    "ModalitaAttivaFtp INTEGER",
                    "CodificaNomiFile TEXT",
                    "LogProtocollo INTEGER",
                    "MantieniConnessioneAttiva INTEGER",
                    "OffsetOrarioServerMinuti INTEGER"
                }
                For Each colonna As String In colonneImpostazioni
                    Try
                        Using cmdMigra As New SQLiteCommand("ALTER TABLE ImpostazioniFtp ADD COLUMN " & colonna, conn)
                            cmdMigra.ExecuteNonQuery()
                        End Using
                    Catch ex As Exception
                        ScriviLogMigrazione("CLSFtp.InizializzaDbFtp - Migrazione: ALTER TABLE ImpostazioniFtp ADD COLUMN " & colonna, ex)
                    End Try
                Next

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "ServerPreferiti")
                    WGDatabase.AssicuraColonnaChiave(conn, "ServerRecenti")
                    WGDatabase.AssicuraColonnaChiave(conn, "ServerSalvati")
                    WGDatabase.AssicuraColonnaChiave(conn, "ImpronteHostSftp")
                Catch exChiave As Exception
                    ScriviLog("CLSFtp.InizializzaDbFtp chiave", exChiave)
                End Try
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.InizializzaDbFtp - Errore", ex)
        End Try
    End Sub

    ' =====================================================================================
    ' Nuovo schema di cifratura delle password, portabile tra PC e sistemi operativi.
    '
    ' La vecchia implementazione usava la DPAPI di Windows (CryptProtectData/
    ' CryptUnprotectData): i dati cifrati potevano essere letti SOLO sullo stesso PC e dallo
    ' stesso account utente Windows che li aveva salvati, quindi copiando il programma e il
    ' suo database altrove (altro PC, altro utente, altro sistema operativo) le password
    ' salvate diventavano illeggibili.
    '
    ' Il nuovo schema usa AES-256-CBC con IV casuale + HMAC-SHA256 (encrypt-then-MAC), con
    ' una chiave a 64 byte generata casualmente al primo utilizzo e salvata in un file
    ' ("ftpkey.bin") nella stessa cartella dati del database. Spostando l'intera cartella
    ' dati (database + file chiave) - o l'intero programma - su un altro PC, anche con
    ' sistema operativo diverso da Windows, le password restano leggibili: non c'e' alcun
    ' legame ne' con l'account utente ne' con Windows.
    '
    ' Nota sulla sicurezza: la protezione si basa sulla riservatezza del file della chiave,
    ' non piu' sull'account utente Windows. Chi ha accesso alla cartella dati del programma
    ' (db + ftpkey.bin) puo' decifrare le password: e' il compromesso necessario per rendere
    ' il programma portabile. Va quindi trattata come una cartella da proteggere (permessi
    ' del sistema operativo, backup cifrati, ecc.), esattamente come si farebbe con un
    ' qualunque "password manager" portabile.
    ' =====================================================================================

    Private Const NOME_FILE_CHIAVE_FTP As String = "ftpkey.bin"
    Private Const DIMENSIONE_CHIAVE_AES As Integer = 32 ' AES-256
    Private Const DIMENSIONE_CHIAVE_HMAC As Integer = 32 ' HMAC-SHA256
    Private Const PREFISSO_FORMATO_V2 As String = "V2:"
    Private Shared ReadOnly LockChiaveFtp As New Object()
    Private Shared _chiaveCriptazioneCache As Byte() = Nothing

    Private Const NOME_CHIAVE_NEL_DB As String = "chiave_password"

    Private ReadOnly Property PercorsoFileChiaveFtp As String
        Get
            Try
                Dim percorso As String = WGDatabase.PercorsoFileDati("Ftp", NOME_FILE_CHIAVE_FTP)
                If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
            Catch ex As Exception
                ScriviLog("CLSFtp.PercorsoFileChiaveFtp - Errore", ex)
            End Try
            Try
                Dim cartella As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_FTP)
                If Not Directory.Exists(cartella) Then
                    Directory.CreateDirectory(cartella)
                End If
                Return Path.Combine(cartella, NOME_FILE_CHIAVE_FTP)
            Catch ex As Exception
                ScriviLog("CLSFtp.PercorsoFileChiaveFtp ripiego - Errore", ex)
                Return ""
            End Try
        End Get
    End Property

    Private ReadOnly Property PercorsoFileChiaveFtpStorico As String
        Get
            Try
                Dim cartella As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_FTP)
                Return Path.Combine(cartella, NOME_FILE_CHIAVE_FTP)
            Catch ex As Exception
                ScriviLog("CLSFtp.PercorsoFileChiaveFtpStorico - Errore", ex)
                Return ""
            End Try
        End Get
    End Property

    Private Sub AssicuraTabellaChiaviFtp(conn As SQLiteConnection)
        Try
            If conn Is Nothing Then Return
            Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS ChiaviFtp (Nome TEXT PRIMARY KEY, Valore TEXT);", conn)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.AssicuraTabellaChiaviFtp - Errore", ex)
        End Try
    End Sub

    Private Function LeggiChiaveDalDb() As Byte()
        Try
            Dim stringaConnessione As String = ConnectionStringFtp
            If String.IsNullOrWhiteSpace(stringaConnessione) Then Return Nothing
            Using conn As New SQLiteConnection(stringaConnessione)
                conn.Open()
                AssicuraTabellaChiaviFtp(conn)
                Using cmd As New SQLiteCommand("SELECT Valore FROM ChiaviFtp WHERE Nome = @nome LIMIT 1;", conn)
                    cmd.Parameters.AddWithValue("@nome", NOME_CHIAVE_NEL_DB)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    If risultato Is Nothing OrElse risultato Is DBNull.Value Then Return Nothing
                    Dim testo As String = Convert.ToString(risultato)
                    If String.IsNullOrWhiteSpace(testo) Then Return Nothing
                    Dim dati As Byte() = Convert.FromBase64String(testo.Trim())
                    If dati Is Nothing OrElse dati.Length <> DIMENSIONE_CHIAVE_AES + DIMENSIONE_CHIAVE_HMAC Then Return Nothing
                    Return dati
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.LeggiChiaveDalDb - Errore", ex)
            Return Nothing
        End Try
    End Function

    Private Function ScriviChiaveNelDb(chiave As Byte()) As Boolean
        Try
            If chiave Is Nothing OrElse chiave.Length <> DIMENSIONE_CHIAVE_AES + DIMENSIONE_CHIAVE_HMAC Then Return False
            Dim stringaConnessione As String = ConnectionStringFtp
            If String.IsNullOrWhiteSpace(stringaConnessione) Then Return False
            Using conn As New SQLiteConnection(stringaConnessione)
                conn.Open()
                AssicuraTabellaChiaviFtp(conn)
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO ChiaviFtp (Nome, Valore) VALUES (@nome, @valore);", conn)
                    cmd.Parameters.AddWithValue("@nome", NOME_CHIAVE_NEL_DB)
                    cmd.Parameters.AddWithValue("@valore", Convert.ToBase64String(chiave))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.ScriviChiaveNelDb - Errore", ex)
            Return False
        End Try
    End Function

    Private Function LeggiChiaveDaFile(percorso As String) As Byte()
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return Nothing
            Dim dati As Byte() = File.ReadAllBytes(percorso)
            If dati Is Nothing OrElse dati.Length <> DIMENSIONE_CHIAVE_AES + DIMENSIONE_CHIAVE_HMAC Then Return Nothing
            Return dati
        Catch ex As Exception
            ScriviLog("CLSFtp.LeggiChiaveDaFile - Errore", ex)
            Return Nothing
        End Try
    End Function

    Private Function ChiaviUguali(prima As Byte(), seconda As Byte()) As Boolean
        Try
            If prima Is Nothing OrElse seconda Is Nothing Then Return False
            If prima.Length <> seconda.Length Then Return False
            For indice As Integer = 0 To prima.Length - 1
                If prima(indice) <> seconda(indice) Then Return False
            Next
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.ChiaviUguali - Errore", ex)
            Return False
        End Try
    End Function

    Private Sub ArchiviaFileChiaveMigrato(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
            Dim destinazione As String = percorso & ".migrato"
            If File.Exists(destinazione) Then Return
            Try
                File.SetAttributes(percorso, FileAttributes.Normal)
            Catch ex As Exception
                ScriviLog("CLSFtp.ArchiviaFileChiaveMigrato attributi - Errore", ex)
            End Try
            File.Move(percorso, destinazione)
            Try
                File.SetAttributes(destinazione, File.GetAttributes(destinazione) Or FileAttributes.Hidden)
            Catch ex As Exception
                ScriviLog("CLSFtp.ArchiviaFileChiaveMigrato nascondi - Errore", ex)
            End Try
        Catch ex As Exception
            ScriviLog("CLSFtp.ArchiviaFileChiaveMigrato - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Restituisce la chiave simmetrica (64 byte: 32 per AES-256 + 32 per HMAC-SHA256) usata
    ''' per criptare/decriptare le password. Se il file della chiave non esiste ancora viene
    ''' generato con un generatore casuale crittografico e salvato accanto al database: da
    ''' quel momento la cartella dati puo' essere copiata su un altro PC o sistema operativo
    ''' e le password salvate restano leggibili.
    ''' </summary>
    Private Function OttieniChiaveCriptazioneFtp() As Byte()
        SyncLock LockChiaveFtp
            Try
                If _chiaveCriptazioneCache IsNot Nothing Then Return _chiaveCriptazioneCache

                Dim chiaveNelDb As Byte() = LeggiChiaveDalDb()
                If chiaveNelDb IsNot Nothing Then
                    _chiaveCriptazioneCache = chiaveNelDb
                    Return _chiaveCriptazioneCache
                End If

                Dim percorsiStorici As New List(Of String)()
                Dim percorsoAttuale As String = PercorsoFileChiaveFtp
                If Not String.IsNullOrWhiteSpace(percorsoAttuale) Then percorsiStorici.Add(percorsoAttuale)
                Dim percorsoVecchio As String = PercorsoFileChiaveFtpStorico
                If Not String.IsNullOrWhiteSpace(percorsoVecchio) AndAlso
                   Not String.Equals(percorsoVecchio, percorsoAttuale, StringComparison.OrdinalIgnoreCase) Then
                    percorsiStorici.Add(percorsoVecchio)
                End If

                For Each percorsoFile As String In percorsiStorici
                    Dim chiaveDaFile As Byte() = LeggiChiaveDaFile(percorsoFile)
                    If chiaveDaFile Is Nothing Then Continue For
                    If ScriviChiaveNelDb(chiaveDaFile) AndAlso ChiaviUguali(LeggiChiaveDalDb(), chiaveDaFile) Then
                        ArchiviaFileChiaveMigrato(percorsoFile)
                    End If
                    _chiaveCriptazioneCache = chiaveDaFile
                    Return _chiaveCriptazioneCache
                Next

                Dim nuovaChiave(DIMENSIONE_CHIAVE_AES + DIMENSIONE_CHIAVE_HMAC - 1) As Byte
                Using rng = RandomNumberGenerator.Create()
                    rng.GetBytes(nuovaChiave)
                End Using
                If Not ScriviChiaveNelDb(nuovaChiave) Then
                    Return Nothing
                End If
                _chiaveCriptazioneCache = nuovaChiave
                Return _chiaveCriptazioneCache
            Catch ex As Exception
                ScriviLog("CLSFtp.OttieniChiaveCriptazioneFtp - Errore", ex)
                Return Nothing
            End Try
        End SyncLock
    End Function

    ''' <summary>
    ''' Protegge una password con la libreria nativa di cifratura (AES-256-GCM, prefisso "WGX:"), senza password dell'utente:
    ''' si legge su qualunque computer e passa nelle esportazioni dei dati.
    ''' </summary>
    Public Function CriptaPasswordFtp(passwordChiaro As String) As String
        Try
            If String.IsNullOrEmpty(passwordChiaro) Then Return ""
            Return WGDb.WGCifraturaNativa.ProteggiTesto("ftp", passwordChiaro)
        Catch ex As Exception
            ScriviLog("CLSFtp.CriptaPasswordFtp - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Decripta una password. Riconosce automaticamente il nuovo formato portabile
    ''' (prefisso "V2:") e, solo per compatibilita' con i database creati dalle versioni
    ''' precedenti del programma, anche il vecchio formato DPAPI (leggibile pero' solo su
    ''' Windows e solo dallo stesso account che l'aveva salvata). Per convertire in modo
    ''' permanente le vecchie password al nuovo formato portabile usare una tantum
    ''' ConvertiPasswordDaDpapiAPortabile, su Windows, prima di spostare il programma.
    ''' </summary>
    Public Function DecriptaPasswordFtp(passwordCriptata As String) As String
        Try
            If String.IsNullOrEmpty(passwordCriptata) Then Return ""

            If WGDb.WGCifraturaNativa.EProtetto(passwordCriptata) Then
                Return WGDb.WGCifraturaNativa.RivelaTesto("ftp", passwordCriptata)
            End If

            If passwordCriptata.StartsWith(PREFISSO_FORMATO_V2, StringComparison.Ordinal) Then
                Return DecriptaPasswordFtpV2(passwordCriptata.Substring(PREFISSO_FORMATO_V2.Length))
            End If

            Return DecriptaPasswordFtpLegacyDpapi(passwordCriptata)
        Catch ex As Exception
            ScriviLog("CLSFtp.DecriptaPasswordFtp - Errore", ex)
            Return ""
        End Try
    End Function

    Private Function DecriptaPasswordFtpV2(base64SenzaPrefisso As String) As String
        Try
            Dim chiave As Byte() = OttieniChiaveCriptazioneFtp()
            If chiave Is Nothing Then Return ""
            Dim chiaveAes(DIMENSIONE_CHIAVE_AES - 1) As Byte
            Dim chiaveHmac(DIMENSIONE_CHIAVE_HMAC - 1) As Byte
            Array.Copy(chiave, 0, chiaveAes, 0, DIMENSIONE_CHIAVE_AES)
            Array.Copy(chiave, DIMENSIONE_CHIAVE_AES, chiaveHmac, 0, DIMENSIONE_CHIAVE_HMAC)

            Dim tutti As Byte() = Convert.FromBase64String(base64SenzaPrefisso)
            Const dimIv As Integer = 16
            Const dimHmac As Integer = 32
            If tutti.Length < dimIv + dimHmac Then
                Return ""
            End If

            Dim iv(dimIv - 1) As Byte
            Dim hmacRicevuto(dimHmac - 1) As Byte
            Dim datiCriptati(tutti.Length - dimIv - dimHmac - 1) As Byte
            Buffer.BlockCopy(tutti, 0, iv, 0, dimIv)
            Buffer.BlockCopy(tutti, dimIv, hmacRicevuto, 0, dimHmac)
            Buffer.BlockCopy(tutti, dimIv + dimHmac, datiCriptati, 0, datiCriptati.Length)

            Dim datiDaAutenticare(iv.Length + datiCriptati.Length - 1) As Byte
            Buffer.BlockCopy(iv, 0, datiDaAutenticare, 0, iv.Length)
            Buffer.BlockCopy(datiCriptati, 0, datiDaAutenticare, iv.Length, datiCriptati.Length)

            Dim hmacCalcolato As Byte()
            Using h As New HMACSHA256(chiaveHmac)
                hmacCalcolato = h.ComputeHash(datiDaAutenticare)
            End Using

            If Not ConfrontoTempoCostante(hmacCalcolato, hmacRicevuto) Then
                Return ""
            End If

            Using aes As Aes = Aes.Create()
                aes.KeySize = 256
                aes.Mode = CipherMode.CBC
                aes.Padding = PaddingMode.PKCS7
                aes.Key = chiaveAes
                aes.IV = iv
                Using decryptor = aes.CreateDecryptor()
                    Dim datiChiaro As Byte() = decryptor.TransformFinalBlock(datiCriptati, 0, datiCriptati.Length)
                    Return Encoding.UTF8.GetString(datiChiaro)
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.DecriptaPasswordFtpV2 - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Confronto a tempo costante tra due array di byte (evita timing attack sulla verifica
    ''' dell'HMAC). Scritto a mano per restare compatibile sia con .NET Framework sia con
    ''' .NET/.NET Core, dove non e' sempre disponibile CryptographicOperations.FixedTimeEquals.
    ''' </summary>
    Private Function ConfrontoTempoCostante(a As Byte(), b As Byte()) As Boolean
        If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return False
        Dim differenza As Integer = 0
        For i As Integer = 0 To a.Length - 1
            differenza = differenza Or (a(i) Xor b(i))
        Next
        Return differenza = 0
    End Function

    ' ---- Vecchio schema DPAPI (solo Windows), mantenuto SOLO per poter convertire le -----
    ' ---- password gia' salvate dalle versioni precedenti del programma (vedi sotto). -----

    <StructLayout(LayoutKind.Sequential)>
    Private Structure DATA_BLOB
        Public cbData As Integer
        Public pbData As IntPtr
    End Structure

    <DllImport("crypt32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CryptUnprotectData(ByRef pDataIn As DATA_BLOB, ppszDataDescr As IntPtr, ByRef pOptionalEntropy As DATA_BLOB, pvReserved As IntPtr, pPromptStruct As IntPtr, dwFlags As Integer, ByRef pDataOut As DATA_BLOB) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function LocalFree(hMem As IntPtr) As IntPtr
    End Function

    Private Const CRYPTPROTECT_UI_FORBIDDEN As Integer = &H1

    Private Function CreaDataBlob(dati As Byte()) As DATA_BLOB
        Dim blob As New DATA_BLOB()
        If dati Is Nothing OrElse dati.Length = 0 Then
            blob.pbData = Marshal.AllocHGlobal(1)
            blob.cbData = 0
            Return blob
        End If
        blob.pbData = Marshal.AllocHGlobal(dati.Length)
        Marshal.Copy(dati, 0, blob.pbData, dati.Length)
        blob.cbData = dati.Length
        Return blob
    End Function

    ''' <summary>
    ''' Decripta una password nel vecchio formato DPAPI. Funziona solo su Windows e solo se
    ''' eseguita dallo stesso account utente che l'aveva salvata: e' esattamente il motivo
    ''' per cui, con il vecchio schema, il programma non poteva essere spostato. Usata solo
    ''' per compatibilita' in lettura e come passaggio della migrazione una tantum.
    ''' </summary>
    Private Function DecriptaPasswordFtpLegacyDpapi(passwordCriptata As String) As String
        If Not RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            Return ""
        End If

        Dim blobIn As New DATA_BLOB()
        Dim blobEntropia As New DATA_BLOB()
        Dim blobOut As New DATA_BLOB()
        Try
            Dim datiCriptati As Byte() = Convert.FromBase64String(passwordCriptata)
            blobIn = CreaDataBlob(datiCriptati)
            blobEntropia = CreaDataBlob(EntropiaFtp)

            Dim esito As Boolean = CryptUnprotectData(blobIn, IntPtr.Zero, blobEntropia, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, blobOut)
            If Not esito Then
                Return ""
            End If

            Dim datiChiaro(blobOut.cbData - 1) As Byte
            Marshal.Copy(blobOut.pbData, datiChiaro, 0, blobOut.cbData)
            Return Encoding.UTF8.GetString(datiChiaro)
        Catch ex As Exception
            ScriviLog("CLSFtp.DecriptaPasswordFtpLegacyDpapi - Errore", ex)
            Return ""
        Finally
            Try
                If blobIn.pbData <> IntPtr.Zero Then Marshal.FreeHGlobal(blobIn.pbData)
                If blobEntropia.pbData <> IntPtr.Zero Then Marshal.FreeHGlobal(blobEntropia.pbData)
                If blobOut.pbData <> IntPtr.Zero Then LocalFree(blobOut.pbData)
            Catch exPulizia As Exception
                ScriviLog("CLSFtp.DecriptaPasswordFtpLegacyDpapi - Errore pulizia memoria", exPulizia)
            End Try
        End Try
    End Function

    ''' <summary>
    ''' Converte tutte le password salvate nei tre elenchi (Preferiti, Recenti, Salvati) dal
    ''' vecchio formato DPAPI (legato a Windows e al singolo account utente) al nuovo formato
    ''' portabile AES/HMAC. Da eseguire UNA TANTUM, su Windows, con lo stesso account che ha
    ''' salvato le password, PRIMA di spostare il programma su un'altra macchina o su un
    ''' sistema operativo diverso. Le password gia' nel nuovo formato vengono lasciate
    ''' invariate. Restituisce un riepilogo testuale dell'esito da mostrare all'utente.
    ''' </summary>
    Public Function ConvertiPasswordDaDpapiAPortabile() As String
        Dim convertite As Integer = 0
        Dim fallite As Integer = 0
        Dim saltate As Integer = 0
        Try
            If Not RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return "Nessuna conversione eseguita: le vecchie password (formato DPAPI) si possono leggere solo su Windows, con lo stesso account utente che le ha salvate. Eseguire questa conversione sulla macchina Windows originale prima di spostare il programma altrove."
            End If

            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                For Each tabella As String In New String() {"ServerPreferiti", "ServerRecenti", "ServerSalvati"}
                    Dim righe As New List(Of Tuple(Of Integer, String))
                    Using cmd As New SQLiteCommand($"SELECT Id, PasswordCriptata FROM {tabella}", conn)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim id As Integer = Convert.ToInt32(reader("Id"))
                                Dim pwd As String = Convert.ToString(reader("PasswordCriptata"))
                                righe.Add(New Tuple(Of Integer, String)(id, pwd))
                            End While
                        End Using
                    End Using

                    For Each riga In righe
                        Try
                            Dim id As Integer = riga.Item1
                            Dim pwdCriptata As String = riga.Item2
                            If String.IsNullOrEmpty(pwdCriptata) Then
                                saltate += 1
                                Continue For
                            End If
                            If pwdCriptata.StartsWith(PREFISSO_FORMATO_V2, StringComparison.Ordinal) OrElse WGDb.WGCifraturaNativa.EProtetto(pwdCriptata) Then
                                saltate += 1
                                Continue For
                            End If

                            Dim pwdChiaro As String = DecriptaPasswordFtpLegacyDpapi(pwdCriptata)
                            If String.IsNullOrEmpty(pwdChiaro) Then
                                fallite += 1
                                Continue For
                            End If

                            Dim pwdNuova As String = CriptaPasswordFtp(pwdChiaro)
                            If String.IsNullOrEmpty(pwdNuova) Then
                                fallite += 1
                                Continue For
                            End If

                            Using cmdUpdate As New SQLiteCommand($"UPDATE {tabella} SET PasswordCriptata = @pwd WHERE Id = @id", conn)
                                cmdUpdate.Parameters.AddWithValue("@pwd", pwdNuova)
                                cmdUpdate.Parameters.AddWithValue("@id", id)
                                cmdUpdate.ExecuteNonQuery()
                            End Using
                            convertite += 1
                        Catch ex As Exception
                            fallite += 1
                            ScriviLog($"CLSFtp.ConvertiPasswordDaDpapiAPortabile - Errore riga tabella {tabella}", ex)
                        End Try
                    Next
                Next
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.ConvertiPasswordDaDpapiAPortabile - Errore", ex)
            Return $"Conversione interrotta da un errore: {ex.Message}"
        End Try
        Return $"Conversione completata: {convertite} password convertite al nuovo formato portabile, {fallite} fallite, {saltate} gia' nel nuovo formato o vuote."
    End Function

    Private Function LeggiServerDaLettore(reader As SQLiteDataReader) As InfoServerFtp
        Dim s As New InfoServerFtp()
        Try
            s.Id = Convert.ToInt32(reader("Id"))
            s.Nome = Convert.ToString(reader("Nome"))
            s.Host = Convert.ToString(reader("Host"))
            s.Porta = Convert.ToInt32(reader("Porta"))
            s.Utente = Convert.ToString(reader("Utente"))
            s.PasswordCriptata = Convert.ToString(reader("PasswordCriptata"))
            Try
                Dim valoreProtocollo As Object = reader("Protocollo")
                If valoreProtocollo IsNot Nothing AndAlso Not IsDBNull(valoreProtocollo) Then
                    s.Protocollo = CType(Convert.ToInt32(valoreProtocollo), TipoProtocolloFtp)
                End If
            Catch ex As Exception
                ScriviLog("CLSFtp.LeggiServerDaLettore - Errore lettura Protocollo", ex)
                s.Protocollo = TipoProtocolloFtp.Ftp
            End Try
            s.CartellaIniziale = Convert.ToString(reader("CartellaIniziale"))
            Try
                Dim valoreLimite As Object = reader("LimiteVelocitaKBs")
                If valoreLimite IsNot Nothing AndAlso Not IsDBNull(valoreLimite) Then
                    s.LimiteVelocitaKBs = Convert.ToInt32(valoreLimite)
                End If
            Catch ex As Exception
                ScriviLog("CLSFtp.LeggiServerDaLettore - Errore lettura LimiteVelocitaKBs", ex)
                s.LimiteVelocitaKBs = 0
            End Try
            Try
                Dim valoreSalvaPassword As Object = reader("SalvaPassword")
                If valoreSalvaPassword IsNot Nothing AndAlso Not IsDBNull(valoreSalvaPassword) Then
                    s.SalvaPassword = (Convert.ToInt32(valoreSalvaPassword) <> 0)
                Else
                    s.SalvaPassword = True
                End If
            Catch ex As Exception
                ScriviLog("CLSFtp.LeggiServerDaLettore - Errore lettura SalvaPassword", ex)
                s.SalvaPassword = True
            End Try
            Try
                Dim valoreUltimaCartella As Object = reader("UltimaCartella")
                If valoreUltimaCartella IsNot Nothing AndAlso Not IsDBNull(valoreUltimaCartella) Then
                    s.UltimaCartella = Convert.ToString(valoreUltimaCartella)
                End If
            Catch ex As Exception
                ScriviLog("CLSFtp.LeggiServerDaLettore - Errore lettura UltimaCartella", ex)
                s.UltimaCartella = ""
            End Try
            Dim testoData As String = Convert.ToString(reader("UltimoUtilizzo"))
            Dim dataConvertita As DateTime
            If DateTime.TryParse(testoData, CultureInfo.InvariantCulture, DateTimeStyles.None, dataConvertita) Then
                s.UltimoUtilizzo = dataConvertita
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.LeggiServerDaLettore - Errore", ex)
        End Try
        Return s
    End Function

    Public Function OttieniServerPreferiti() As List(Of InfoServerFtp)
        Dim risultato As New List(Of InfoServerFtp)()
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT * FROM ServerPreferiti ORDER BY Nome COLLATE NOCASE", conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            risultato.Add(LeggiServerDaLettore(reader))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniServerPreferiti - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Function AggiungiServerPreferito(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM ServerPreferiti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo LIMIT 1", conn)
                    cmdChiave.Parameters.AddWithValue("@host", server.Host)
                    cmdChiave.Parameters.AddWithValue("@utente", server.Utente)
                    cmdChiave.Parameters.AddWithValue("@porta", server.Porta)
                    cmdChiave.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    Dim valoreChiave As Object = cmdChiave.ExecuteScalar()
                    If valoreChiave IsNot Nothing AndAlso Not IsDBNull(valoreChiave) Then chiaveWGEsistente = Convert.ToString(valoreChiave)
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "ServerPreferiti")
                Using cmdDel As New SQLiteCommand("DELETE FROM ServerPreferiti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo", conn)
                    cmdDel.Parameters.AddWithValue("@host", server.Host)
                    cmdDel.Parameters.AddWithValue("@utente", server.Utente)
                    cmdDel.Parameters.AddWithValue("@porta", server.Porta)
                    cmdDel.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmdDel.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("INSERT INTO ServerPreferiti (Nome, Host, Porta, Utente, PasswordCriptata, Protocollo, CartellaIniziale, UltimoUtilizzo, LimiteVelocitaKBs, SalvaPassword, ChiaveWG) VALUES (@nome, @host, @porta, @utente, @pwd, @protocollo, @cartella, @ultimo, @limite, @salvapwd, @chiaveWG)", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@ultimo", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@chiaveWG", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiungiServerPreferito - Errore", ex)
            Return False
        End Try
    End Function

    Public Function RimuoviServerPreferito(id As Integer) As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM ServerPreferiti WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.RimuoviServerPreferito - Errore", ex)
            Return False
        End Try
    End Function

    Public Function AggiornaServerPreferito(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse server.Id <= 0 OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("UPDATE ServerPreferiti SET Nome = @nome, Host = @host, Porta = @porta, Utente = @utente, PasswordCriptata = @pwd, Protocollo = @protocollo, CartellaIniziale = @cartella, LimiteVelocitaKBs = @limite, SalvaPassword = @salvapwd WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@id", server.Id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiornaServerPreferito - Errore", ex)
            Return False
        End Try
    End Function

    Public Function OttieniServerRecenti() As List(Of InfoServerFtp)
        Dim risultato As New List(Of InfoServerFtp)()
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT * FROM ServerRecenti ORDER BY UltimoUtilizzo DESC", conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            risultato.Add(LeggiServerDaLettore(reader))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniServerRecenti - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Function AggiungiServerRecente(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM ServerRecenti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo LIMIT 1", conn)
                    cmdChiave.Parameters.AddWithValue("@host", server.Host)
                    cmdChiave.Parameters.AddWithValue("@utente", server.Utente)
                    cmdChiave.Parameters.AddWithValue("@porta", server.Porta)
                    cmdChiave.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    Dim valoreChiave As Object = cmdChiave.ExecuteScalar()
                    If valoreChiave IsNot Nothing AndAlso Not IsDBNull(valoreChiave) Then chiaveWGEsistente = Convert.ToString(valoreChiave)
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "ServerRecenti")
                Using cmdDel As New SQLiteCommand("DELETE FROM ServerRecenti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo", conn)
                    cmdDel.Parameters.AddWithValue("@host", server.Host)
                    cmdDel.Parameters.AddWithValue("@utente", server.Utente)
                    cmdDel.Parameters.AddWithValue("@porta", server.Porta)
                    cmdDel.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmdDel.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("INSERT INTO ServerRecenti (Nome, Host, Porta, Utente, PasswordCriptata, Protocollo, CartellaIniziale, UltimoUtilizzo, LimiteVelocitaKBs, SalvaPassword, ChiaveWG) VALUES (@nome, @host, @porta, @utente, @pwd, @protocollo, @cartella, @ultimo, @limite, @salvapwd, @chiaveWG)", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@ultimo", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@chiaveWG", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmdConta As New SQLiteCommand("SELECT COUNT(*) FROM ServerRecenti", conn)
                    Dim totale As Integer = Convert.ToInt32(cmdConta.ExecuteScalar())
                    If totale > MAX_SERVER_RECENTI Then
                        Using cmdElimina As New SQLiteCommand($"DELETE FROM ServerRecenti WHERE Id IN (SELECT Id FROM ServerRecenti ORDER BY UltimoUtilizzo ASC LIMIT {totale - MAX_SERVER_RECENTI})", conn)
                            cmdElimina.ExecuteNonQuery()
                        End Using
                    End If
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiungiServerRecente - Errore", ex)
            Return False
        End Try
    End Function

    Public Function RimuoviServerRecente(id As Integer) As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM ServerRecenti WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.RimuoviServerRecente - Errore", ex)
            Return False
        End Try
    End Function

    Public Function SvuotaServerRecenti() As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM ServerRecenti", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.SvuotaServerRecenti - Errore", ex)
            Return False
        End Try
    End Function

    Public Function AggiornaServerRecente(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse server.Id <= 0 OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("UPDATE ServerRecenti SET Nome = @nome, Host = @host, Porta = @porta, Utente = @utente, PasswordCriptata = @pwd, Protocollo = @protocollo, CartellaIniziale = @cartella, LimiteVelocitaKBs = @limite, SalvaPassword = @salvapwd WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@id", server.Id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiornaServerRecente - Errore", ex)
            Return False
        End Try
    End Function

    Public Sub AggiornaUltimaCartellaPreferito(id As Integer, percorso As String)
        AggiornaUltimaCartellaInterno("ServerPreferiti", id, percorso)
    End Sub

    Public Sub AggiornaUltimaCartellaRecente(id As Integer, percorso As String)
        AggiornaUltimaCartellaInterno("ServerRecenti", id, percorso)
    End Sub

    Public Sub AggiornaUltimaCartellaSalvato(id As Integer, percorso As String)
        AggiornaUltimaCartellaInterno("ServerSalvati", id, percorso)
    End Sub

    Public Function OttieniImpostazioniFtp() As InfoImpostazioniFtp
        Dim risultato As New InfoImpostazioniFtp()
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT LimiteVelocitaKBs, SalvaSempreServer, SalvaSemprePassword, ModalitaAttivaFtp, CodificaNomiFile, LogProtocollo, MantieniConnessioneAttiva, OffsetOrarioServerMinuti FROM ImpostazioniFtp WHERE Id = 1", conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            If Not IsDBNull(reader("LimiteVelocitaKBs")) Then risultato.LimiteVelocitaKBs = Convert.ToInt32(reader("LimiteVelocitaKBs"))
                            If Not IsDBNull(reader("SalvaSempreServer")) Then risultato.SalvaSempreServer = (Convert.ToInt32(reader("SalvaSempreServer")) <> 0)
                            If Not IsDBNull(reader("SalvaSemprePassword")) Then risultato.SalvaSemprePassword = (Convert.ToInt32(reader("SalvaSemprePassword")) <> 0)
                            If Not IsDBNull(reader("ModalitaAttivaFtp")) Then risultato.ModalitaAttivaFtp = (Convert.ToInt32(reader("ModalitaAttivaFtp")) <> 0)
                            If Not IsDBNull(reader("CodificaNomiFile")) Then risultato.CodificaNomiFile = Convert.ToString(reader("CodificaNomiFile"))
                            If Not IsDBNull(reader("LogProtocollo")) Then risultato.LogProtocollo = (Convert.ToInt32(reader("LogProtocollo")) <> 0)
                            If Not IsDBNull(reader("MantieniConnessioneAttiva")) Then risultato.MantieniConnessioneAttiva = (Convert.ToInt32(reader("MantieniConnessioneAttiva")) <> 0)
                            If Not IsDBNull(reader("OffsetOrarioServerMinuti")) Then risultato.OffsetOrarioServerMinuti = Convert.ToInt32(reader("OffsetOrarioServerMinuti"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniImpostazioniFtp - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Function SalvaImpostazioniFtp(impostazioni As InfoImpostazioniFtp) As Boolean
        Try
            If impostazioni Is Nothing Then Return False
            _offsetOrarioServerMinuti = Nothing
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("INSERT INTO ImpostazioniFtp (Id, LimiteVelocitaKBs, SalvaSempreServer, SalvaSemprePassword, ModalitaAttivaFtp, CodificaNomiFile, LogProtocollo, MantieniConnessioneAttiva, OffsetOrarioServerMinuti) " &
                                                "VALUES (1, @limite, @salvaserver, @salvapwd, @attiva, @codifica, @logprot, @keepalive, @offset) " &
                                                "ON CONFLICT(Id) DO UPDATE SET LimiteVelocitaKBs = @limite, SalvaSempreServer = @salvaserver, SalvaSemprePassword = @salvapwd, " &
                                                "ModalitaAttivaFtp = @attiva, CodificaNomiFile = @codifica, LogProtocollo = @logprot, MantieniConnessioneAttiva = @keepalive, OffsetOrarioServerMinuti = @offset", conn)
                    cmd.Parameters.AddWithValue("@limite", impostazioni.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvaserver", If(impostazioni.SalvaSempreServer, 1, 0))
                    cmd.Parameters.AddWithValue("@salvapwd", If(impostazioni.SalvaSemprePassword, 1, 0))
                    cmd.Parameters.AddWithValue("@attiva", If(impostazioni.ModalitaAttivaFtp, 1, 0))
                    cmd.Parameters.AddWithValue("@codifica", If(impostazioni.CodificaNomiFile, ""))
                    cmd.Parameters.AddWithValue("@logprot", If(impostazioni.LogProtocollo, 1, 0))
                    cmd.Parameters.AddWithValue("@keepalive", If(impostazioni.MantieniConnessioneAttiva, 1, 0))
                    cmd.Parameters.AddWithValue("@offset", impostazioni.OffsetOrarioServerMinuti)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.SalvaImpostazioniFtp - Errore", ex)
            Return False
        End Try
    End Function

    Public Function OttieniServerSalvati() As List(Of InfoServerFtp)
        Dim risultato As New List(Of InfoServerFtp)()
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT * FROM ServerSalvati ORDER BY UltimoUtilizzo DESC", conn)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            risultato.Add(LeggiServerDaLettore(reader))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniServerSalvati - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Function AggiungiServerSalvato(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM ServerSalvati WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo LIMIT 1", conn)
                    cmdChiave.Parameters.AddWithValue("@host", server.Host)
                    cmdChiave.Parameters.AddWithValue("@utente", server.Utente)
                    cmdChiave.Parameters.AddWithValue("@porta", server.Porta)
                    cmdChiave.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    Dim valoreChiave As Object = cmdChiave.ExecuteScalar()
                    If valoreChiave IsNot Nothing AndAlso Not IsDBNull(valoreChiave) Then chiaveWGEsistente = Convert.ToString(valoreChiave)
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "ServerSalvati")
                Using cmdDel As New SQLiteCommand("DELETE FROM ServerSalvati WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo", conn)
                    cmdDel.Parameters.AddWithValue("@host", server.Host)
                    cmdDel.Parameters.AddWithValue("@utente", server.Utente)
                    cmdDel.Parameters.AddWithValue("@porta", server.Porta)
                    cmdDel.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmdDel.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("INSERT INTO ServerSalvati (Nome, Host, Porta, Utente, PasswordCriptata, Protocollo, CartellaIniziale, UltimoUtilizzo, LimiteVelocitaKBs, SalvaPassword, ChiaveWG) VALUES (@nome, @host, @porta, @utente, @pwd, @protocollo, @cartella, @ultimo, @limite, @salvapwd, @chiaveWG)", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@ultimo", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@chiaveWG", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiungiServerSalvato - Errore", ex)
            Return False
        End Try
    End Function

    Public Function RimuoviServerSalvato(id As Integer) As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM ServerSalvati WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.RimuoviServerSalvato - Errore", ex)
            Return False
        End Try
    End Function

    Public Function AggiornaServerSalvato(server As InfoServerFtp) As Boolean
        Try
            If server Is Nothing OrElse server.Id <= 0 OrElse String.IsNullOrWhiteSpace(server.Host) Then Return False
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("UPDATE ServerSalvati SET Nome = @nome, Host = @host, Porta = @porta, Utente = @utente, PasswordCriptata = @pwd, Protocollo = @protocollo, CartellaIniziale = @cartella, LimiteVelocitaKBs = @limite, SalvaPassword = @salvapwd WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@nome", server.Nome)
                    cmd.Parameters.AddWithValue("@host", server.Host)
                    cmd.Parameters.AddWithValue("@porta", server.Porta)
                    cmd.Parameters.AddWithValue("@utente", server.Utente)
                    cmd.Parameters.AddWithValue("@pwd", server.PasswordCriptata)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(server.Protocollo))
                    cmd.Parameters.AddWithValue("@cartella", server.CartellaIniziale)
                    cmd.Parameters.AddWithValue("@limite", server.LimiteVelocitaKBs)
                    cmd.Parameters.AddWithValue("@salvapwd", If(server.SalvaPassword, 1, 0))
                    cmd.Parameters.AddWithValue("@id", server.Id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiornaServerSalvato - Errore", ex)
            Return False
        End Try
    End Function

    Public Function EsisteServerPreferito(host As String, utente As String, porta As Integer, protocollo As TipoProtocolloFtp) As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM ServerPreferiti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo", conn)
                    cmd.Parameters.AddWithValue("@host", host)
                    cmd.Parameters.AddWithValue("@utente", utente)
                    cmd.Parameters.AddWithValue("@porta", porta)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(protocollo))
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.EsisteServerPreferito - Errore", ex)
            Return False
        End Try
    End Function

    Public Function RimuoviServerPreferitoPerCorrispondenza(host As String, utente As String, porta As Integer, protocollo As TipoProtocolloFtp) As Boolean
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("DELETE FROM ServerPreferiti WHERE Host = @host AND Utente = @utente AND Porta = @porta AND Protocollo = @protocollo", conn)
                    cmd.Parameters.AddWithValue("@host", host)
                    cmd.Parameters.AddWithValue("@utente", utente)
                    cmd.Parameters.AddWithValue("@porta", porta)
                    cmd.Parameters.AddWithValue("@protocollo", CInt(protocollo))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.RimuoviServerPreferitoPerCorrispondenza - Errore", ex)
            Return False
        End Try
    End Function

    Private Sub AggiornaUltimaCartellaInterno(tabella As String, id As Integer, percorso As String)
        Try
            If id <= 0 Then Return
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand($"UPDATE {tabella} SET UltimaCartella = @percorso WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@percorso", percorso)
                    cmd.Parameters.AddWithValue("@id", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiornaUltimaCartellaInterno - Errore", ex)
        End Try
    End Sub
    Public Function PortaPredefinita(protocollo As TipoProtocolloFtp) As Integer
        Try
            If protocollo = TipoProtocolloFtp.Sftp Then Return 22
            Return 21
        Catch ex As Exception
            ScriviLog("CLSFtp.PortaPredefinita - Errore", ex)
            Return 21
        End Try
    End Function

    Private Function OttieniImprontaSalvataSftp(host As String, porta As Integer) As String
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Impronta FROM ImpronteHostSftp WHERE Host = @host AND Porta = @porta", conn)
                    cmd.Parameters.AddWithValue("@host", host)
                    cmd.Parameters.AddWithValue("@porta", porta)
                    Dim valore As Object = cmd.ExecuteScalar()
                    If valore IsNot Nothing AndAlso Not IsDBNull(valore) Then Return Convert.ToString(valore)
                End Using
            End Using
            Return ""
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniImprontaSalvataSftp - Errore", ex)
            Return ""
        End Try
    End Function

    Private Sub SalvaImprontaSftp(host As String, porta As Integer, impronta As String)
        Try
            InizializzaDbFtp()
            Using conn As New SQLiteConnection(ConnectionStringFtp)
                conn.Open()
                Dim chiaveWGEsistente As String = ""
                Using cmdChiave As New SQLiteCommand("SELECT ChiaveWG FROM ImpronteHostSftp WHERE Host = @host AND Porta = @porta LIMIT 1", conn)
                    cmdChiave.Parameters.AddWithValue("@host", host)
                    cmdChiave.Parameters.AddWithValue("@porta", porta)
                    Dim valoreChiave As Object = cmdChiave.ExecuteScalar()
                    If valoreChiave IsNot Nothing AndAlso Not IsDBNull(valoreChiave) Then chiaveWGEsistente = Convert.ToString(valoreChiave)
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGEsistente) Then chiaveWGEsistente = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "ImpronteHostSftp")
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO ImpronteHostSftp (Host, Porta, Impronta, UltimaVerifica, ChiaveWG) VALUES (@host, @porta, @impronta, @ultimo, @chiaveWG)", conn)
                    cmd.Parameters.AddWithValue("@host", host)
                    cmd.Parameters.AddWithValue("@porta", porta)
                    cmd.Parameters.AddWithValue("@impronta", impronta)
                    cmd.Parameters.AddWithValue("@ultimo", DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
                    cmd.Parameters.AddWithValue("@chiaveWG", chiaveWGEsistente)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.SalvaImprontaSftp - Errore", ex)
        End Try
    End Sub

    Public Function VerificaImprontaHostSftp(server As InfoServerFtp, improntaAttuale As String, ByRef messaggioAvviso As String) As Boolean
        messaggioAvviso = ""
        Try
            If server.Protocollo <> TipoProtocolloFtp.Sftp Then Return True

            If String.IsNullOrEmpty(improntaAttuale) Then
                messaggioAvviso = $"Non è stato possibile verificare l'identità del server SFTP ""{server.Host}:{server.Porta}"". Procedi solo se ti fidi di questa connessione."
                Return False
            End If

            Dim improntaSalvata As String = OttieniImprontaSalvataSftp(server.Host, server.Porta)
            If String.IsNullOrEmpty(improntaSalvata) Then
                messaggioAvviso = $"Prima connessione al server SFTP ""{server.Host}:{server.Porta}"". Non è ancora possibile verificare se la chiave presentata dal server è autentica. Procedi solo se ti fidi di questa connessione."
                Return False
            End If

            If Not String.Equals(improntaSalvata, improntaAttuale, StringComparison.Ordinal) Then
                messaggioAvviso = $"Attenzione: la chiave del server SFTP ""{server.Host}:{server.Porta}"" è cambiata rispetto all'ultima connessione. Potrebbe trattarsi di una normale rigenerazione della chiave da parte dell'amministratore, oppure di un tentativo di intercettazione della connessione. Procedi solo se sei certo che il cambiamento sia legittimo."
                Return False
            End If

            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.VerificaImprontaHostSftp - Errore", ex)
            messaggioAvviso = $"Non è stato possibile verificare l'identità del server SFTP ""{server.Host}:{server.Porta}"". Procedi solo se ti fidi di questa connessione."
            Return False
        End Try
    End Function

    Public Sub AggiornaImprontaHostSftp(server As InfoServerFtp, improntaAttuale As String)
        Try
            If server.Protocollo <> TipoProtocolloFtp.Sftp Then Return
            If Not String.IsNullOrEmpty(improntaAttuale) Then
                SalvaImprontaSftp(server.Host, server.Porta, improntaAttuale)
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.AggiornaImprontaHostSftp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Si connette al server SSH via socket grezzo (senza passare da SSH.NET) e legge il primo
    ''' pacchetto SSH_MSG_KEXINIT per scoprire quali algoritmi di key-exchange offre davvero il
    ''' server. Utile per diagnosticare automaticamente l'errore "Failed to negotiate key exchange
    ''' algorithm.", perché ci dice esattamente cosa propone il server anche quando SSH.NET
    ''' non riesce a completare la connessione.
    ''' </summary>
    Private Function OttieniAlgoritmiKexServer(host As String, porta As Integer) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            Using tcp As New TcpClient()
                tcp.ReceiveTimeout = 8000
                tcp.SendTimeout = 8000
                tcp.Connect(host, porta)
                Using stream As NetworkStream = tcp.GetStream()
                    ' 1) Legge la riga di identificazione del server (es. "SSH-2.0-OpenSSH_9.9\r\n")
                    Dim rigaId As New List(Of Byte)
                    Dim b As Integer
                    Do
                        b = stream.ReadByte()
                        If b = -1 Then Exit Do
                        rigaId.Add(CByte(b))
                    Loop Until b = Asc(vbLf)

                    ' 2) Invia la propria identificazione (richiesta dal protocollo SSH)
                    Dim idClient As Byte() = Encoding.ASCII.GetBytes("SSH-2.0-CLSFtpDiag_1.0" & vbCrLf)
                    stream.Write(idClient, 0, idClient.Length)

                    ' 3) Legge il primo pacchetto binario (SSH_MSG_KEXINIT), non ancora cifrato
                    Dim intestazione As Byte() = LeggiEsatto(stream, 5)
                    Dim lunghezzaPacchetto As UInteger = (CUInt(intestazione(0)) << 24) Or (CUInt(intestazione(1)) << 16) Or (CUInt(intestazione(2)) << 8) Or CUInt(intestazione(3))
                    Dim lunghezzaPadding As Byte = intestazione(4)
                    Dim payload As Byte() = LeggiEsatto(stream, CInt(lunghezzaPacchetto) - 1)

                    ' payload(0) = codice messaggio (20 = SSH_MSG_KEXINIT), poi 16 byte di cookie casuale
                    Dim offset As Integer = 1 + 16

                    ' 4) Legge il primo name-list del KEXINIT: kex_algorithms
                    Dim lunghezzaLista As UInteger = (CUInt(payload(offset)) << 24) Or (CUInt(payload(offset + 1)) << 16) Or (CUInt(payload(offset + 2)) << 8) Or CUInt(payload(offset + 3))
                    offset += 4
                    Dim listaTestuale As String = Encoding.ASCII.GetString(payload, offset, CInt(lunghezzaLista))
                    risultato.AddRange(listaTestuale.Split(","c))
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniAlgoritmiKexServer - Errore lettura algoritmi KEX del server", ex)
        End Try
        Return risultato
    End Function

    Private Function LeggiEsatto(stream As NetworkStream, numeroByte As Integer) As Byte()
        Dim buffer(numeroByte - 1) As Byte
        Dim letti As Integer = 0
        Do While letti < numeroByte
            Dim n As Integer = stream.Read(buffer, letti, numeroByte - letti)
            If n <= 0 Then Throw New IO.EndOfStreamException("Connessione chiusa durante la lettura del pacchetto SSH.")
            letti += n
        Loop
        Return buffer
    End Function

    ''' <summary>
    ''' Apre una nuova sessione verso il server indicato. La creazione dei client e' delegata
    ''' a CreaClientSftp e CreaClientFtp, gli stessi usati da AssicuraConnessione per riaprire
    ''' una sessione caduta senza doverla ricreare da fuori.
    ''' </summary>
    Public Function ApriSessione(server As InfoServerFtp, ByRef messaggioErrore As String) As SessioneFtp
        Dim autenticazioneFallita As Boolean = False
        Return ApriSessione(server, messaggioErrore, autenticazioneFallita)
    End Function

    ''' <summary>
    ''' Come ApriSessione, ma dice anche se il fallimento e' dovuto a credenziali non
    ''' accettate: cosi' l'interfaccia puo' proporre di reinserire la password invece di
    ''' mostrare un generico errore di connessione.
    ''' </summary>
    Public Function ApriSessione(server As InfoServerFtp, ByRef messaggioErrore As String, ByRef autenticazioneFallita As Boolean) As SessioneFtp
        messaggioErrore = ""
        autenticazioneFallita = False
        Try
            If server Is Nothing Then
                messaggioErrore = "Server non valido."
                Return Nothing
            End If

            Dim sessione As New SessioneFtp()
            sessione.Server = server

            If server.Protocollo = TipoProtocolloFtp.Sftp Then
                sessione.ClientSftp = CreaClientSftp(server, sessione, messaggioErrore, autenticazioneFallita)
                If sessione.ClientSftp Is Nothing Then Return Nothing
            Else
                sessione.ClientFtp = CreaClientFtp(server, messaggioErrore, autenticazioneFallita)
                If sessione.ClientFtp Is Nothing Then Return Nothing
            End If

            Return sessione
        Catch ex As SshAuthenticationException
            ScriviLog("CLSFtp.ApriSessione - autenticazione SFTP fallita", ex)
            messaggioErrore = "Autenticazione SFTP non riuscita. Verificare utente e password."
            Return Nothing
        Catch ex As SshConnectionException
            ScriviLog("CLSFtp.ApriSessione - connessione SFTP fallita", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Connessione SFTP non riuscita.", ex.Message)
            Return Nothing
        Catch ex As FluentFTP.Exceptions.FtpAuthenticationException
            ScriviLog("CLSFtp.ApriSessione - autenticazione FTP fallita", ex)
            messaggioErrore = "Autenticazione FTP non riuscita. Verificare utente e password."
            Return Nothing
        Catch ex As SocketException
            ScriviLog("CLSFtp.ApriSessione - SocketException", ex)
            messaggioErrore = "Impossibile raggiungere il server (rete o porta non disponibili)."
            Return Nothing
        Catch ex As FileNotFoundException
            ScriviLog("CLSFtp.ApriSessione - assembly mancante", ex)
            messaggioErrore = "Componente mancante per la connessione (verificare le dipendenze, es. Microsoft.Extensions.Logging.Abstractions)."
            Return Nothing
        Catch ex As IOException
            ScriviLog("CLSFtp.ApriSessione - IOException", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Errore di I/O durante la connessione.", ex.Message)
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSFtp.ApriSessione - Errore", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Connessione non riuscita.", ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub ChiudiSessione(sessione As SessioneFtp)
        Try
            If sessione Is Nothing Then Return
            sessione.Dispose()
        Catch ex As Exception
            ScriviLog("CLSFtp.ChiudiSessione - Errore", ex)
        End Try
    End Sub

    Public Function TestaConnessioneFtp(server As InfoServerFtp, ByRef improntaSftp As String) As Boolean
        improntaSftp = ""
        Try
            Dim messaggioErrore As String = ""
            Dim sessione As SessioneFtp = ApriSessione(server, messaggioErrore)
            If sessione Is Nothing Then Return False
            improntaSftp = sessione.ImprontaSftp
            ChiudiSessione(sessione)
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.TestaConnessioneFtp - Errore", ex)
            Return False
        End Try
    End Function

    Private Function EstensioneCorrispondeFiltro(estensione As String, filter As String) As Boolean
        Try
            If String.IsNullOrEmpty(filter) Then Return True
            If filter.Contains("|") Then
                Dim elenco As String() = filter.Split("|"c)
                For Each f In elenco
                    If String.Equals(estensione, f.Trim(), StringComparison.OrdinalIgnoreCase) Then Return True
                Next
                Return False
            End If
            Return String.Equals(estensione, filter, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLog("CLSFtp.EstensioneCorrispondeFiltro - Errore", ex)
            Return True
        End Try
    End Function

    Private Function FiltraElementiPerEstensione(elementi As List(Of InfoElementoFtp), filtroEstensioni As String) As List(Of InfoElementoFtp)
        Try
            If String.IsNullOrEmpty(filtroEstensioni) Then Return elementi
            Return elementi.Where(Function(e) e.EDirectory OrElse EstensioneCorrispondeFiltro(EstensioneDaNomeRemoto(e.Nome), filtroEstensioni)).ToList()
        Catch ex As Exception
            ScriviLog("CLSFtp.FiltraElementiPerEstensione - Errore", ex)
            Return elementi
        End Try
    End Function

    ''' <summary>
    ''' Riporta un percorso remoto alla forma canonica usata in tutta la classe, valida
    ''' sia per i server Unix sia per quelli Windows: separatori con la barra normale,
    ''' barra iniziale sempre presente, nessuna barra doppia, nessuna barra finale salvo
    ''' che per le radici. Su Windows la radice di un'unita' resta nella forma "/C:/",
    ''' che e' quella restituita da OpenSSH per Windows.
    ''' </summary>
    Public Function NormalizzaPercorsoRemoto(percorso As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return "/"
            Dim p As String = percorso.Replace("\", "/").Trim()
            If String.IsNullOrWhiteSpace(p) Then Return "/"
            While p.Contains("//")
                p = p.Replace("//", "/")
            End While
            If p.Length >= 2 AndAlso Char.IsLetter(p(0)) AndAlso p(1) = ":"c Then p = "/" & p
            While p.Length > 1 AndAlso p.EndsWith("/")
                p = p.Substring(0, p.Length - 1)
            End While
            If String.IsNullOrEmpty(p) Then Return "/"
            If p.Length = 3 AndAlso p(0) = "/"c AndAlso Char.IsLetter(p(1)) AndAlso p(2) = ":"c Then Return p & "/"
            Return p
        Catch ex As Exception
            ScriviLog("CLSFtp.NormalizzaPercorsoRemoto - Errore", ex)
            Return percorso
        End Try
    End Function

    ''' <summary>
    ''' Restituisce il solo nome dell'elemento a partire da quello che il server ha messo
    ''' nell'elenco: alcuni server FTP scrivono il percorso completo al posto del nome, e
    ''' i server Windows usano il backslash come separatore.
    ''' </summary>
    Private Function NomeSoloDaVoceRemota(nome As String) As String
        Try
            If String.IsNullOrEmpty(nome) Then Return ""
            Dim n As String = nome.Replace("\", "/").TrimEnd("/"c)
            If String.IsNullOrEmpty(n) Then Return ""
            Dim posizione As Integer = n.LastIndexOf("/"c)
            If posizione >= 0 Then n = n.Substring(posizione + 1)
            Return n
        Catch ex As Exception
            ScriviLog("CLSFtp.NomeSoloDaVoceRemota - Errore", ex)
            Return nome
        End Try
    End Function

    ''' <summary>
    ''' Ricava l'estensione di un nome remoto senza passare da System.IO.Path, che sui nomi
    ''' leciti su Linux ma non su Windows (due punti, asterischi, barre verticali) solleva
    ''' un'eccezione invece di rispondere.
    ''' </summary>
    Private Function EstensioneDaNomeRemoto(nome As String) As String
        Try
            Dim n As String = NomeSoloDaVoceRemota(nome)
            If String.IsNullOrEmpty(n) Then Return ""
            Dim posizione As Integer = n.LastIndexOf("."c)
            If posizione <= 0 OrElse posizione = n.Length - 1 Then Return ""
            Return n.Substring(posizione).ToLowerInvariant()
        Catch ex As Exception
            ScriviLog("CLSFtp.EstensioneDaNomeRemoto - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Vero se il percorso e' una radice: la radice assoluta "/" dei server Unix oppure
    ''' la radice di un'unita' Windows ("/C:/"), sopra le quali non si puo' risalire.
    ''' </summary>
    Public Function EPercorsoRadiceRemoto(percorso As String) As Boolean
        Try
            Dim p As String = NormalizzaPercorsoRemoto(percorso)
            If String.IsNullOrEmpty(p) OrElse p = "/" OrElse p = "." Then Return True
            If p.Length = 4 AndAlso p(0) = "/"c AndAlso Char.IsLetter(p(1)) AndAlso p(2) = ":"c AndAlso p(3) = "/"c Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.EPercorsoRadiceRemoto - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Restituisce la cartella superiore di un percorso remoto. Dalla radice di un'unita'
    ''' Windows si risale a "/", che sui server Windows mostra l'elenco delle unita'.
    ''' </summary>
    Public Function CartellaPadreRemota(percorso As String) As String
        Try
            Dim p As String = NormalizzaPercorsoRemoto(percorso)
            If EPercorsoRadiceRemoto(p) Then Return "/"
            Dim senzaFinale As String = p.TrimEnd("/"c)
            Dim posizione As Integer = senzaFinale.LastIndexOf("/"c)
            If posizione <= 0 Then Return "/"
            Return NormalizzaPercorsoRemoto(senzaFinale.Substring(0, posizione))
        Catch ex As Exception
            ScriviLog("CLSFtp.CartellaPadreRemota - Errore", ex)
            Return "/"
        End Try
    End Function

    ''' <summary>
    ''' Restituisce solo il nome dell'ultimo elemento di un percorso remoto, senza il
    ''' percorso che lo precede.
    ''' </summary>
    Public Function NomeElementoRemoto(percorso As String) As String
        Try
            Dim p As String = NormalizzaPercorsoRemoto(percorso)
            If p = "/" Then Return "/"
            Dim senzaFinale As String = p.TrimEnd("/"c)
            Dim posizione As Integer = senzaFinale.LastIndexOf("/"c)
            If posizione < 0 Then Return senzaFinale
            Return senzaFinale.Substring(posizione + 1)
        Catch ex As Exception
            ScriviLog("CLSFtp.NomeElementoRemoto - Errore", ex)
            Return percorso
        End Try
    End Function

    ''' <summary>
    ''' Riconosce un server FTP/FTPS Windows dalle informazioni di sistema negoziate da
    ''' FluentFTP (risposte a SYST e FEAT).
    ''' </summary>
    Private Function ServerFtpEWindows(sessione As SessioneFtp) As Boolean
        Try
            If sessione Is Nothing OrElse sessione.ClientFtp Is Nothing Then Return False
            Dim testo As String = ""
            SyncLock sessione.LockOggetto
                Try
                    testo = sessione.ClientFtp.ServerOS.ToString() & " " & sessione.ClientFtp.ServerType.ToString()
                Catch ex As Exception
                    ScriviLog("CLSFtp.ServerFtpEWindows - lettura tipo server", ex)
                End Try
            End SyncLock
            Return TestoIndicaWindows(testo)
        Catch ex As Exception
            ScriviLog("CLSFtp.ServerFtpEWindows - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Stabilisce se il server della sessione gira su Windows, qualunque sia il protocollo.
    ''' L'esito viene memorizzato nella sessione: serve a decidere quali comandi remoti hanno
    ''' senso e se i permessi in stile Unix sono gestibili.
    ''' </summary>
    Public Function ServerRemotoEWindows(sessione As SessioneFtp) As Boolean
        Try
            If sessione Is Nothing Then Return False
            If sessione.SistemaWindowsRilevato.HasValue Then Return sessione.SistemaWindowsRilevato.Value
            Dim esito As Boolean = False
            If sessione.Server IsNot Nothing AndAlso sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                esito = ServerSftpEWindows(sessione)
            Else
                esito = ServerFtpEWindows(sessione)
            End If
            sessione.SistemaWindowsRilevato = esito
            Return esito
        Catch ex As Exception
            ScriviLog("CLSFtp.ServerRemotoEWindows - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Indica se sul server ha senso leggere e modificare i permessi in stile Unix.
    ''' Sui server Windows non esistono: l'interfaccia puo' usare questo esito per non
    ''' proporre affatto la voce di modifica dei permessi.
    ''' </summary>
    Public Function PermessiSupportati(sessione As SessioneFtp) As Boolean
        Try
            If sessione Is Nothing OrElse Not sessione.Connessa Then Return False
            Return Not ServerRemotoEWindows(sessione)
        Catch ex As Exception
            ScriviLog("CLSFtp.PermessiSupportati - Errore", ex)
            Return False
        End Try
    End Function

    Public Function CombinaPercorsoRemoto(base_ As String, nome As String) As String
        Try
            Dim percorso As String = NormalizzaPercorsoRemoto(base_)
            If percorso.EndsWith("/") Then Return NormalizzaPercorsoRemoto(percorso & nome)
            Return NormalizzaPercorsoRemoto(percorso & "/" & nome)
        Catch ex As Exception
            ScriviLog("CLSFtp.CombinaPercorsoRemoto - Errore", ex)
            Return nome
        End Try
    End Function

    Public Function ElencaFileECartelle(sessione As SessioneFtp, percorsoRemoto As String, Optional filtroEstensioni As String = "") As List(Of InfoElementoFtp)
        Dim risultato As New List(Of InfoElementoFtp)()
        Try
            If Not SessionePronta(sessione) Then Return risultato
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            Dim permessiDisponibili As Boolean = PermessiSupportati(sessione)
            Dim minutiOffsetServer As Integer = OffsetOrarioServerMinuti()

            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Dim elementi = sessione.ClientSftp.ListDirectory(percorso)
                    For Each item In elementi
                        Try
                            Dim nome As String = NomeSoloDaVoceRemota(item.Name)
                            If String.IsNullOrEmpty(nome) Then Continue For
                            If nome = "." OrElse nome = ".." Then Continue For
                            Dim percorsoElemento As String = CombinaPercorsoRemoto(percorso, nome)
                            Dim eCartella As Boolean = item.IsDirectory
                            Dim dimensione As Long = item.Attributes.Size
                            Dim eCollegamento As Boolean = item.IsSymbolicLink
                            If item.IsSymbolicLink Then
                                Try
                                    Dim attributiRisolti = sessione.ClientSftp.GetAttributes(percorsoElemento)
                                    If attributiRisolti IsNot Nothing Then
                                        eCartella = attributiRisolti.IsDirectory
                                        dimensione = attributiRisolti.Size
                                    End If
                                Catch exLink As Exception
                                    ScriviLog($"CLSFtp.ElencaFileECartelle - collegamento non risolvibile '{percorsoElemento}'", exLink)
                                End Try
                            End If
                            If eCartella OrElse dimensione < 0 Then dimensione = 0
                            Dim elemento As New InfoElementoFtp() With {
                                .Nome = nome,
                                .PercorsoCompleto = percorsoElemento,
                                .EDirectory = eCartella,
                                .Dimensione = dimensione,
                                .DataModifica = DataRemotaNormalizzata(item.LastWriteTime, minutiOffsetServer),
                                .Nascosto = nome.StartsWith("."),
                                .Permessi = If(permessiDisponibili, PermessiRwxDaAttributiSftp(item.Attributes), ""),
                                .ECollegamento = eCollegamento,
                                .Proprietario = item.Attributes.UserId.ToString(CultureInfo.InvariantCulture),
                                .Gruppo = item.Attributes.GroupId.ToString(CultureInfo.InvariantCulture)
                            }
                            risultato.Add(elemento)
                        Catch ex As Exception
                            ScriviLog("CLSFtp.ElencaFileECartelle - Errore elemento SFTP", ex)
                        End Try
                    Next
                Else
                    Dim elementi As FtpListItem() = sessione.ClientFtp.GetListing(percorso)
                    If elementi Is Nothing Then elementi = New FtpListItem() {}
                    For Each item In elementi
                        Try
                            Dim nome As String = NomeSoloDaVoceRemota(item.Name)
                            If String.IsNullOrEmpty(nome) Then Continue For
                            If nome = "." OrElse nome = ".." Then Continue For
                            Dim eCartella As Boolean = (item.Type = FtpObjectType.Directory)
                            Dim eCollegamento As Boolean = (item.Type = FtpObjectType.Link)
                            If item.Type = FtpObjectType.Link Then
                                Try
                                    If item.LinkObject IsNot Nothing Then
                                        eCartella = (item.LinkObject.Type = FtpObjectType.Directory)
                                    ElseIf sessione.ClientFtp.DirectoryExists(CombinaPercorsoRemoto(percorso, nome)) Then
                                        eCartella = True
                                    End If
                                Catch exLink As Exception
                                    ScriviLog($"CLSFtp.ElencaFileECartelle - collegamento FTP non risolvibile '{nome}'", exLink)
                                End Try
                            End If
                            Dim dimensione As Long = item.Size
                            If eCartella OrElse dimensione < 0 Then dimensione = 0
                            Dim elemento As New InfoElementoFtp() With {
                                .Nome = nome,
                                .PercorsoCompleto = CombinaPercorsoRemoto(percorso, nome),
                                .EDirectory = eCartella,
                                .Dimensione = dimensione,
                                .DataModifica = DataRemotaNormalizzata(item.Modified, minutiOffsetServer),
                                .Nascosto = nome.StartsWith("."),
                                .Permessi = If(Not permessiDisponibili, "", If(Not String.IsNullOrEmpty(item.RawPermissions), item.RawPermissions, PermessiRwxDaChmod(item.Chmod))),
                                .ECollegamento = eCollegamento,
                                .DestinazioneCollegamento = If(item.LinkTarget, ""),
                                .Proprietario = If(item.RawOwner, ""),
                                .Gruppo = If(item.RawGroup, "")
                            }
                            risultato.Add(elemento)
                        Catch ex As Exception
                            ScriviLog("CLSFtp.ElencaFileECartelle - Errore elemento FTP", ex)
                        End Try
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSFtp.ElencaFileECartelle - Errore", ex)
        End Try
        Return FiltraElementiPerEstensione(risultato, filtroEstensioni)
    End Function

    ''' <summary>
    ''' Elenca ricorsivamente il contenuto di una cartella remota. I collegamenti simbolici
    ''' che puntano a cartelle gia' visitate o a un antenato del percorso creerebbero una
    ''' ricorsione infinita: le cartelle gia' viste vengono quindi memorizzate e la
    ''' profondita' e' comunque limitata.
    ''' </summary>
    Public Function ElencaFileECartelleRicorsivo(sessione As SessioneFtp, percorsoRemoto As String) As List(Of InfoElementoFtp)
        Try
            Dim visitate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Return ElencaFileECartelleRicorsivoInterno(sessione, percorsoRemoto, visitate, 0)
        Catch ex As Exception
            ScriviLog("CLSFtp.ElencaFileECartelleRicorsivo - Errore", ex)
            Return New List(Of InfoElementoFtp)()
        End Try
    End Function

    Private Function ElencaFileECartelleRicorsivoInterno(sessione As SessioneFtp, percorsoRemoto As String, visitate As HashSet(Of String), profondita As Integer) As List(Of InfoElementoFtp)
        Dim risultato As New List(Of InfoElementoFtp)()
        Try
            If profondita > PROFONDITA_MASSIMA_RICORSIONE Then
                Return risultato
            End If
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            If Not visitate.Add(percorso) Then Return risultato

            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelle(sessione, percorso)
            For Each elemento As InfoElementoFtp In elementi
                risultato.Add(elemento)
                If elemento.EDirectory Then
                    risultato.AddRange(ElencaFileECartelleRicorsivoInterno(sessione, elemento.PercorsoCompleto, visitate, profondita + 1))
                End If
            Next
        Catch ex As Exception
            ScriviLog($"CLSFtp.ElencaFileECartelleRicorsivoInterno - Errore '{percorsoRemoto}'", ex)
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Costruisce una stringa di permessi in stile Unix ("rwxr-xr-x") a partire dagli
    ''' attributi di un elemento SFTP.
    ''' </summary>
    Private Function PermessiRwxDaAttributiSftp(attributi As Renci.SshNet.Sftp.SftpFileAttributes) As String
        Try
            If attributi Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            sb.Append(If(attributi.OwnerCanRead, "r", "-"))
            sb.Append(If(attributi.OwnerCanWrite, "w", "-"))
            sb.Append(If(attributi.OwnerCanExecute, "x", "-"))
            sb.Append(If(attributi.GroupCanRead, "r", "-"))
            sb.Append(If(attributi.GroupCanWrite, "w", "-"))
            sb.Append(If(attributi.GroupCanExecute, "x", "-"))
            sb.Append(If(attributi.OthersCanRead, "r", "-"))
            sb.Append(If(attributi.OthersCanWrite, "w", "-"))
            sb.Append(If(attributi.OthersCanExecute, "x", "-"))
            Return sb.ToString()
        Catch ex As Exception
            ScriviLog("CLSFtp.PermessiRwxDaAttributiSftp - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Converte un valore chmod ottale "impacchettato in cifre decimali" (es. 754, dove
    ''' ogni cifra rappresenta un gruppo owner/group/other) in una stringa "rwxr-xr-x".
    ''' Usata sia come ripiego per FTP quando il server fornisce solo il valore numerico,
    ''' sia per costruire un'anteprima leggibile prima di applicare nuovi permessi.
    ''' </summary>
    Public Function PermessiRwxDaChmod(chmod As Integer) As String
        Try
            If chmod <= 0 Then Return ""
            Dim cifre As String = chmod.ToString(CultureInfo.InvariantCulture)
            If cifre.Length > 3 Then cifre = cifre.Substring(cifre.Length - 3)
            cifre = cifre.PadLeft(3, "0"c)
            For Each carattere As Char In cifre
                If carattere < "0"c OrElse carattere > "7"c Then Return ""
            Next
            Dim sb As New StringBuilder()
            For Each c As Char In cifre
                Dim n As Integer
                If Not Integer.TryParse(c.ToString(), n) Then Continue For
                sb.Append(If((n And 4) > 0, "r", "-"))
                sb.Append(If((n And 2) > 0, "w", "-"))
                sb.Append(If((n And 1) > 0, "x", "-"))
            Next
            Return sb.ToString()
        Catch ex As Exception
            ScriviLog("CLSFtp.PermessiRwxDaChmod - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Traduce una stringa di permessi in stile Unix (es. "rwxr-xr-x", con o senza il
    ''' carattere iniziale di tipo file "-"/"d") in una descrizione leggibile in italiano,
    ''' invece di mostrare i bit così come sono impostati.
    ''' </summary>
    Public Function PermessiLeggibili(permessiRaw As String) As String
        Try
            If String.IsNullOrEmpty(permessiRaw) Then Return "Non disponibili"
            Dim raw As String = permessiRaw.Trim()
            ' Rimuove l'eventuale carattere iniziale di tipo file (es. "-rwxr-xr-x", "drwxr-xr-x")
            If raw.Length = 10 Then raw = raw.Substring(1)
            If raw.Length <> 9 Then Return permessiRaw ' Formato non riconosciuto: mostro il valore originale

            Dim proprietario As String = DescriviTernaPermessi(raw.Substring(0, 3))
            Dim gruppo As String = DescriviTernaPermessi(raw.Substring(3, 3))
            Dim altri As String = DescriviTernaPermessi(raw.Substring(6, 3))

            Return $"Proprietario: {proprietario}. Gruppo: {gruppo}. Altri utenti: {altri}."
        Catch ex As Exception
            ScriviLog("CLSFtp.PermessiLeggibili - Errore", ex)
            Return permessiRaw
        End Try
    End Function

    Private Function DescriviTernaPermessi(terna As String) As String
        Try
            Dim azioni As New List(Of String)()
            If terna.Length >= 1 AndAlso terna(0) = "r"c Then azioni.Add("lettura")
            If terna.Length >= 2 AndAlso terna(1) = "w"c Then azioni.Add("scrittura")
            If terna.Length >= 3 AndAlso (terna(2) = "x"c OrElse terna(2) = "s"c OrElse terna(2) = "t"c) Then azioni.Add("esecuzione")
            If azioni.Count = 0 Then Return "nessun permesso"
            Return String.Join(", ", azioni)
        Catch ex As Exception
            ScriviLog("CLSFtp.DescriviTernaPermessi - Errore", ex)
            Return terna
        End Try
    End Function

    ''' <summary>
    ''' Recupera le informazioni generali del server connesso nella sessione data:
    ''' sistema operativo, spazio totale e spazio disponibile.
    ''' Per SFTP viene aperta una connessione SSH "gemella" (stesse credenziali già
    ''' negoziate) per eseguire comandi remoti (uname, df); per FTP/FTPS si usano
    ''' le informazioni di sistema già negoziate dal client (SYST/FEAT), dato che il
    ''' protocollo FTP standard non prevede un comando per lo spazio disco.
    ''' </summary>
    ''' <summary>
    ''' Imposta i permessi (in stile Unix, es. 754) su un file o cartella remoti.
    ''' Per SFTP usa il comando SFTP nativo di modifica permessi; per FTP/FTPS invia
    ''' il comando non standard "SITE CHMOD", supportato dalla maggior parte dei server
    ''' Unix ma NON da tutti i server FTP (es. molti server Windows/IIS non lo supportano):
    ''' in tal caso l'operazione fallisce e viene restituito il messaggio d'errore del server.
    ''' </summary>
    ''' <param name="chmodOttale">Permessi in notazione ottale "impacchettata" in cifre decimali, es. 754.</param>
    Public Function ImpostaPermessi(sessione As SessioneFtp, percorsoRemoto As String, chmodOttale As Integer, ByRef messaggioErrore As String) As Boolean
        messaggioErrore = ""
        Try
            If Not SessionePronta(sessione) Then
                messaggioErrore = "Sessione non connessa."
                Return False
            End If
            If chmodOttale < 0 OrElse chmodOttale > 777 Then
                messaggioErrore = "Valore permessi non valido."
                Return False
            End If
            If Not PermessiSupportati(sessione) Then
                messaggioErrore = "Il server remoto e' su Windows e non gestisce i permessi in stile Unix: l'operazione non e' supportata."
                Return False
            End If

            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Dim testoOttale As String = chmodOttale.ToString().PadLeft(3, "0"c)
                    sessione.ClientSftp.ChangePermissions(percorso, Convert.ToInt16(testoOttale, 8))
                Else
                    sessione.ClientFtp.Chmod(percorso, chmodOttale)
                End If
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.ImpostaPermessi - Errore", ex)
            messaggioErrore = If(String.IsNullOrEmpty(ex.Message), "Errore durante l'impostazione dei permessi.", ex.Message)
            Try
                If sessione IsNot Nothing AndAlso sessione.Server IsNot Nothing AndAlso sessione.Server.Protocollo = TipoProtocolloFtp.Sftp AndAlso ServerSftpEWindows(sessione) Then
                    messaggioErrore = "Il server SFTP e' su Windows e non gestisce i permessi in stile Unix: l'operazione non e' supportata. Dettaglio: " & messaggioErrore
                End If
            Catch exNota As Exception
                ScriviLog("CLSFtp.ImpostaPermessi - nota server Windows", exNota)
            End Try
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Recupera le informazioni generali del server connesso (OS e spazio disco)
    ''' usando tutte le strategie disponibili, senza interrompere la sessione e
    ''' senza segnalare come errore i limiti normali del protocollo/server.
    ''' I dettagli tecnici finiscono solo nel log; MessaggioErrore resta vuoto
    ''' salvo casi eccezionali non applicati qui.
    ''' </summary>
    Public Function OttieniInfoServer(sessione As SessioneFtp) As InfoServerRemoto
        Dim risultato As New InfoServerRemoto()
        Try
            If Not SessionePronta(sessione) Then
                Return risultato
            End If

            If sessione.Server IsNot Nothing AndAlso sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                RecuperaInfoServerSftp(sessione, risultato)
            Else
                RecuperaInfoServerFtp(sessione, risultato)
            End If
        Catch ex As SocketException
            ScriviLog("CLSFtp.OttieniInfoServer - SocketException", ex)
        Catch ex As SshConnectionException
            ScriviLog("CLSFtp.OttieniInfoServer - SshConnectionException", ex)
        Catch ex As SshAuthenticationException
            ScriviLog("CLSFtp.OttieniInfoServer - SshAuthenticationException", ex)
        Catch ex As SshException
            ScriviLog("CLSFtp.OttieniInfoServer - SshException", ex)
        Catch ex As IOException
            ScriviLog("CLSFtp.OttieniInfoServer - IOException", ex)
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniInfoServer - Errore generico", ex)
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Esegue un comando SSH e restituisce stdout (o stderr se stdout vuoto),
    ''' senza propagare eccezioni. Ritorna stringa vuota se fallisce.
    ''' </summary>
    Private Function EseguiComandoSshSicuro(ssh As SshClient, comando As String) As String
        Try
            If ssh Is Nothing OrElse Not ssh.IsConnected Then Return ""
            Dim cmd = ssh.RunCommand(comando)
            If cmd Is Nothing Then Return ""
            Dim outp As String = If(cmd.Result, "").Trim()
            If String.IsNullOrWhiteSpace(outp) Then outp = If(cmd.Error, "").Trim()
            If String.IsNullOrWhiteSpace(outp) Then Return ""
            Dim low As String = outp.ToLowerInvariant()
            If low.Contains("not found") OrElse low.Contains("no such file") Then Return ""
            If low.Contains("permission denied") OrElse low.Contains("not allowed") Then Return ""
            If low.Contains("command not found") OrElse low.Contains("is not recognized") Then Return ""
            If low.Contains("term is not recognized") OrElse low.Contains("commandnotfoundexception") Then Return ""
            If low.Contains("non " & Chr(232) & " riconosciuto") OrElse low.Contains("non e' riconosciuto") Then Return ""
            If low.Contains("cannot find the path") OrElse low.Contains("impossibile trovare il percorso") Then Return ""
            Return outp
        Catch ex As SshConnectionException
            ScriviLog("EseguiComandoSshSicuro - SshConnectionException: " & comando, ex)
            Return ""
        Catch ex As SshOperationTimeoutException
            ScriviLog("EseguiComandoSshSicuro - timeout: " & comando, ex)
            Return ""
        Catch ex As SocketException
            ScriviLog("EseguiComandoSshSicuro - SocketException: " & comando, ex)
            Return ""
        Catch ex As Exception
            ScriviLog("EseguiComandoSshSicuro - " & comando, ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Accetta la chiave host nelle connessioni SSH ausiliarie usate solo per leggere
    ''' le informazioni del server: l'impronta viene gia' verificata dalla sessione SFTP
    ''' principale, qui non va richiesta una seconda conferma all'utente.
    ''' </summary>
    Private Sub AccettaChiaveHostAusiliaria(sender As Object, e As HostKeyEventArgs)
        Try
            e.CanTrust = True
        Catch ex As Exception
            ScriviLog("CLSFtp.AccettaChiaveHostAusiliaria - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Riconosce dal testo (versione SSH annunciata dal server, output di un comando)
    ''' se il server remoto e' un sistema Windows o un server SFTP tipicamente Windows.
    ''' </summary>
    Private Function TestoIndicaWindows(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim low As String = testo.ToLowerInvariant()
            Dim indizi As String() = {
                "windows",
                "microsoft",
                "bitvise",
                "winsshd",
                "serv-u",
                "servu",
                "syncplify",
                "titan",
                "coreftp",
                "core ftp",
                "wing ftp",
                "rebex",
                "solarwinds",
                "powershell",
                "freesshd",
                "moveit"
            }
            For Each indizio As String In indizi
                If low.Contains(indizio) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.TestoIndicaWindows - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Riconosce dai percorsi remoti se il server e' Windows: OpenSSH per Windows espone
    ''' percorsi del tipo "/C:/Users/nome", altri server usano direttamente il backslash.
    ''' </summary>
    Private Function PercorsoIndicaWindows(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If percorso.Contains("\") Then Return True
            Dim p As String = percorso.TrimStart("/"c)
            If p.Length >= 2 AndAlso Char.IsLetter(p(0)) AndAlso p(1) = ":"c Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.PercorsoIndicaWindows - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Stabilisce se il server SFTP della sessione indicata gira su Windows, senza aprire
    ''' connessioni aggiuntive: usa la versione SSH annunciata dal server e la forma della
    ''' cartella di lavoro remota.
    ''' </summary>
    Public Function ServerSftpEWindows(sessione As SessioneFtp) As Boolean
        Try
            If sessione Is Nothing OrElse sessione.ClientSftp Is Nothing Then Return False
            Dim versione As String = ""
            Dim cartella As String = ""
            SyncLock sessione.LockOggetto
                Try
                    If sessione.ClientSftp.ConnectionInfo IsNot Nothing Then
                        versione = If(sessione.ClientSftp.ConnectionInfo.ServerVersion, "")
                    End If
                Catch ex As Exception
                    ScriviLog("CLSFtp.ServerSftpEWindows - ServerVersion", ex)
                End Try
                Try
                    cartella = If(sessione.ClientSftp.WorkingDirectory, "")
                Catch ex As Exception
                    ScriviLog("CLSFtp.ServerSftpEWindows - WorkingDirectory", ex)
                End Try
            End SyncLock
            If TestoIndicaWindows(versione) Then Return True
            If PercorsoIndicaWindows(cartella) Then Return True
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.ServerSftpEWindows - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Ricava la lettera di unita' Windows ("C:") da un percorso remoto in stile OpenSSH
    ''' per Windows ("/C:/Users/nome"). Ripiega su "C:" se non riconosciuta.
    ''' </summary>
    Private Function LetteraUnitaDaPercorsoRemoto(percorso As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return "C:"
            Dim p As String = percorso.Replace("\", "/").TrimStart("/"c)
            If p.Length >= 2 AndAlso Char.IsLetter(p(0)) AndAlso p(1) = ":"c Then
                Return Char.ToUpperInvariant(p(0)) & ":"
            End If
            Return "C:"
        Catch ex As Exception
            ScriviLog("CLSFtp.LetteraUnitaDaPercorsoRemoto - Errore", ex)
            Return "C:"
        End Try
    End Function

    ''' <summary>
    ''' Legge in modo tollerante una proprieta' numerica di un oggetto restituito dalla
    ''' libreria SSH: i nomi dei tipi e delle proprieta' cambiano fra le versioni di
    ''' SSH.NET, quindi si usa la reflection invece di legare il codice a un tipo preciso.
    ''' </summary>
    Private Function ValoreNumericoProprieta(oggetto As Object, nomeProprieta As String) As ULong
        Try
            If oggetto Is Nothing Then Return 0UL
            Dim proprieta As System.Reflection.PropertyInfo = oggetto.GetType().GetProperty(nomeProprieta)
            If proprieta Is Nothing Then Return 0UL
            Dim valore As Object = proprieta.GetValue(oggetto, Nothing)
            If valore Is Nothing Then Return 0UL
            Return Convert.ToUInt64(valore, CultureInfo.InvariantCulture)
        Catch ex As Exception
            ScriviLog("CLSFtp.ValoreNumericoProprieta - " & nomeProprieta, ex)
            Return 0UL
        End Try
    End Function

    ''' <summary>
    ''' Chiede lo spazio disco direttamente al protocollo SFTP tramite l'estensione
    ''' statvfs@openssh.com: e' l'unica strada che funziona anche quando il server non
    ''' offre nessuna shell (server solo-SFTP, ForceCommand internal-sftp, Windows).
    ''' </summary>
    Private Function ProvaSpazioDiscoSftpNativo(sessione As SessioneFtp, percorso As String, risultato As InfoServerRemoto) As Boolean
        Try
            If sessione Is Nothing OrElse sessione.ClientSftp Is Nothing Then Return False
            Dim percorsi As New List(Of String)()
            If Not String.IsNullOrWhiteSpace(percorso) Then percorsi.Add(percorso)
            Dim radiceUnita As String = ""
            If Not String.IsNullOrWhiteSpace(percorso) Then
                Dim normalizzato As String = NormalizzaPercorsoRemoto(percorso)
                If normalizzato.Length >= 3 AndAlso normalizzato(0) = "/"c AndAlso Char.IsLetter(normalizzato(1)) AndAlso normalizzato(2) = ":"c Then
                    radiceUnita = normalizzato.Substring(0, 3) & "/"
                End If
            End If
            If Not String.IsNullOrEmpty(radiceUnita) AndAlso Not percorsi.Contains(radiceUnita) Then percorsi.Add(radiceUnita)
            If Not percorsi.Contains(".") Then percorsi.Add(".")
            If Not percorsi.Contains("/") Then percorsi.Add("/")
            For Each p As String In percorsi
                Try
                    Dim stato As Object = Nothing
                    SyncLock sessione.LockOggetto
                        If Not sessione.Connessa Then Return False
                        stato = sessione.ClientSftp.GetStatus(p)
                    End SyncLock
                    If stato Is Nothing Then Continue For
                    Dim dimensioneBlocco As ULong = ValoreNumericoProprieta(stato, "FragmentSize")
                    If dimensioneBlocco = 0UL Then dimensioneBlocco = ValoreNumericoProprieta(stato, "BlockSize")
                    If dimensioneBlocco = 0UL Then Continue For
                    Dim blocchiTotali As ULong = ValoreNumericoProprieta(stato, "TotalBlocks")
                    If blocchiTotali = 0UL Then Continue For
                    Dim blocchiDisponibili As ULong = ValoreNumericoProprieta(stato, "AvailableBlocks")
                    If blocchiDisponibili = 0UL Then blocchiDisponibili = ValoreNumericoProprieta(stato, "FreeBlocks")
                    Dim totale As ULong = blocchiTotali * dimensioneBlocco
                    Dim disponibile As ULong = blocchiDisponibili * dimensioneBlocco
                    If totale = 0UL OrElse totale > CULng(Long.MaxValue) Then Continue For
                    If disponibile > totale Then disponibile = totale
                    risultato.SpazioTotale = CLng(totale)
                    risultato.SpazioDisponibile = CLng(disponibile)
                    Return True
                Catch ex As Exception
                    ScriviLog("CLSFtp.ProvaSpazioDiscoSftpNativo - statvfs non disponibile per " & p, ex)
                End Try
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.ProvaSpazioDiscoSftpNativo - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Estrae il valore in byte da una riga di "fsutil volume diskfree", che a seconda
    ''' della versione di Windows scrive il numero puro oppure "50,0 GB (53.687.091.200)".
    ''' </summary>
    Private Function EstraiByteDaTestoWindows(valoreTesto As String) As Long
        Try
            If String.IsNullOrWhiteSpace(valoreTesto) Then Return 0
            Dim corrispondenza As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(valoreTesto, "\(([^)]*)\)")
            Dim candidato As String = valoreTesto
            If corrispondenza.Success Then
                candidato = corrispondenza.Groups(1).Value
            ElseIf System.Text.RegularExpressions.Regex.IsMatch(valoreTesto, "[A-Za-z]") Then
                Return 0
            End If
            Dim sb As New StringBuilder()
            For Each c As Char In candidato
                If Char.IsDigit(c) Then sb.Append(c)
            Next
            If sb.Length = 0 OrElse sb.Length > 18 Then Return 0
            Dim valore As Long
            If Long.TryParse(sb.ToString(), valore) Then Return valore
            Return 0
        Catch ex As Exception
            ScriviLog("CLSFtp.EstraiByteDaTestoWindows - Errore", ex)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Interpreta l'output di "fsutil volume diskfree", equivalente Windows di df.
    ''' Riconosce sia le etichette inglesi sia quelle italiane.
    ''' </summary>
    Private Sub ParseOutputFsutil(output As String, risultato As InfoServerRemoto)
        Try
            If String.IsNullOrWhiteSpace(output) Then Return
            Dim totale As Long = 0
            Dim libero As Long = 0
            Dim liberoDisponibile As Long = 0
            For Each riga As String In output.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = riga.Trim()
                If t.Length = 0 Then Continue For
                Dim posizione As Integer = t.LastIndexOf(":"c)
                If posizione <= 0 Then Continue For
                Dim etichetta As String = t.Substring(0, posizione).ToLowerInvariant()
                Dim valoreTesto As String = t.Substring(posizione + 1).Trim()
                If etichetta.Contains("quota") Then Continue For
                Dim valore As Long = EstraiByteDaTestoWindows(valoreTesto)
                If valore <= 0 Then Continue For
                If etichetta.Contains("avail") OrElse etichetta.Contains("disponibil") Then
                    liberoDisponibile = valore
                ElseIf etichetta.Contains("free") OrElse etichetta.Contains("liber") Then
                    libero = valore
                ElseIf etichetta.Contains("byte") Then
                    totale = valore
                End If
            Next
            If liberoDisponibile <= 0 Then liberoDisponibile = libero
            If totale > 0 Then
                risultato.SpazioTotale = totale
                risultato.SpazioDisponibile = Math.Max(0L, Math.Min(liberoDisponibile, totale))
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ParseOutputFsutil - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Interpreta l'output di "wmic logicaldisk get DeviceID,Size,FreeSpace /format:list"
    ''' cercando l'unita' indicata (es. "C:").
    ''' </summary>
    Private Sub ParseOutputWmicLogicalDisk(output As String, unita As String, risultato As InfoServerRemoto)
        Try
            If String.IsNullOrWhiteSpace(output) Then Return
            Dim unitaCercata As String = If(String.IsNullOrWhiteSpace(unita), "C:", unita.Trim().ToUpperInvariant())
            Dim deviceCorrente As String = ""
            Dim dimensione As Long = 0
            Dim libero As Long = 0
            For Each riga As String In output.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = riga.Trim()
                If t.Length = 0 Then Continue For
                Dim posizione As Integer = t.IndexOf("="c)
                If posizione <= 0 Then Continue For
                Dim chiave As String = t.Substring(0, posizione).Trim().ToLowerInvariant()
                Dim valore As String = t.Substring(posizione + 1).Trim()
                Select Case chiave
                    Case "deviceid"
                        If String.Equals(deviceCorrente, unitaCercata, StringComparison.OrdinalIgnoreCase) AndAlso dimensione > 0 Then
                            risultato.SpazioTotale = dimensione
                            risultato.SpazioDisponibile = Math.Max(0L, Math.Min(libero, dimensione))
                            Return
                        End If
                        deviceCorrente = valore.ToUpperInvariant()
                        dimensione = 0
                        libero = 0
                    Case "size"
                        Dim n As Long
                        If Long.TryParse(valore, n) Then dimensione = n
                    Case "freespace"
                        Dim n As Long
                        If Long.TryParse(valore, n) Then libero = n
                End Select
            Next
            If String.Equals(deviceCorrente, unitaCercata, StringComparison.OrdinalIgnoreCase) AndAlso dimensione > 0 Then
                risultato.SpazioTotale = dimensione
                risultato.SpazioDisponibile = Math.Max(0L, Math.Min(libero, dimensione))
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ParseOutputWmicLogicalDisk - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Interpreta l'output del comando PowerShell di ripiego, che stampa le due righe
    ''' "TOT=numero" e "LIB=numero".
    ''' </summary>
    Private Sub ParseOutputSpazioPowerShell(output As String, risultato As InfoServerRemoto)
        Try
            If String.IsNullOrWhiteSpace(output) Then Return
            Dim totale As Long = 0
            Dim libero As Long = 0
            For Each riga As String In output.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = riga.Trim()
                If t.StartsWith("TOT=", StringComparison.OrdinalIgnoreCase) Then
                    Dim n As Long
                    If Long.TryParse(t.Substring(4).Trim(), n) Then totale = n
                ElseIf t.StartsWith("LIB=", StringComparison.OrdinalIgnoreCase) Then
                    Dim n As Long
                    If Long.TryParse(t.Substring(4).Trim(), n) Then libero = n
                End If
            Next
            If totale > 0 Then
                risultato.SpazioTotale = totale
                risultato.SpazioDisponibile = Math.Max(0L, Math.Min(libero, totale))
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ParseOutputSpazioPowerShell - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Estrae il nome del sistema operativo dall'output in formato lista di
    ''' "wmic os get Caption /value" (riga "Caption=Microsoft Windows Server 2022").
    ''' </summary>
    Private Function EstraiCaptionWindows(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            For Each riga As String In testo.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = riga.Trim()
                If t.StartsWith("Caption=", StringComparison.OrdinalIgnoreCase) Then
                    Return t.Substring("Caption=".Length).Trim()
                End If
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSFtp.EstraiCaptionWindows - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Interpreta l'output di un comando di rilevamento del sistema operativo scegliendo
    ''' il criterio giusto in base al comando che lo ha prodotto.
    ''' </summary>
    Private Function InterpretaOutputSistemaOperativo(comando As String, testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            If comando.IndexOf("os-release", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return EstraiPrettyNameOsRelease(testo)
            End If
            If comando.IndexOf("/value", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return EstraiCaptionWindows(testo)
            End If
            For Each parte As String In testo.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim t As String = parte.Trim()
                If t.Length > 0 Then Return t
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSFtp.InterpretaOutputSistemaOperativo - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Applica l'output di un comando di spazio disco al risultato, scegliendo il parser
    ''' adatto (df per Unix, fsutil / wmic / PowerShell per Windows).
    ''' </summary>
    Private Sub InterpretaOutputSpazioDisco(comando As String, testo As String, unita As String, risultato As InfoServerRemoto)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If comando.IndexOf("fsutil", StringComparison.OrdinalIgnoreCase) >= 0 Then
                ParseOutputFsutil(testo, risultato)
            ElseIf comando.IndexOf("logicaldisk", StringComparison.OrdinalIgnoreCase) >= 0 Then
                ParseOutputWmicLogicalDisk(testo, unita, risultato)
            ElseIf comando.IndexOf("Get-PSDrive", StringComparison.OrdinalIgnoreCase) >= 0 Then
                ParseOutputSpazioPowerShell(testo, risultato)
            Else
                ParseOutputDf(testo, risultato)
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.InterpretaOutputSpazioDisco - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Elenco dei comandi di rilevamento del sistema operativo, ordinato mettendo per
    ''' primi quelli adatti alla famiglia di sistema riconosciuta.
    ''' </summary>
    Private Function ComandiSistemaOperativo(eWindows As Boolean) As List(Of String)
        Dim comandi As New List(Of String)()
        Try
            Dim windows As String() = {
                "cmd /c ver",
                "powershell -NoProfile -NonInteractive -Command " & Chr(34) & "(Get-CimInstance Win32_OperatingSystem).Caption" & Chr(34),
                "cmd /c wmic os get Caption /value",
                "ver"
            }
            Dim unix As String() = {
                "uname -srmo",
                "uname -a",
                "cat /etc/os-release",
                "hostnamectl --static"
            }
            If eWindows Then
                comandi.AddRange(windows)
                comandi.AddRange(unix)
            Else
                comandi.AddRange(unix)
                comandi.AddRange(windows)
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ComandiSistemaOperativo - Errore", ex)
        End Try
        Return comandi
    End Function

    ''' <summary>
    ''' Elenco dei comandi per lo spazio disco, ordinato in base alla famiglia di sistema
    ''' riconosciuta. Su Windows non esiste df: si usano fsutil, wmic e PowerShell.
    ''' </summary>
    Private Function ComandiSpazioDisco(eWindows As Boolean, percorsoLavoro As String, unita As String) As List(Of String)
        Dim comandi As New List(Of String)()
        Try
            Dim lettera As String = unita.Replace(":", "")
            Dim windows As String() = {
                "cmd /c fsutil volume diskfree " & unita,
                "cmd /c wmic logicaldisk get DeviceID,Size,FreeSpace /format:list",
                "powershell -NoProfile -NonInteractive -Command " & Chr(34) & "$d = Get-PSDrive -Name " & lettera & "; Write-Output ('TOT=' + [string]($d.Used + $d.Free)); Write-Output ('LIB=' + [string]$d.Free)" & Chr(34)
            }
            Dim unix As String() = {
                "df -Pk " & Chr(34) & percorsoLavoro & Chr(34),
                "df -Pk .",
                "df -Pk /",
                "df -k /",
                "df -P"
            }
            If eWindows Then
                comandi.AddRange(windows)
                comandi.AddRange(unix)
            Else
                comandi.AddRange(unix)
                comandi.AddRange(windows)
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ComandiSpazioDisco - Errore", ex)
        End Try
        Return comandi
    End Function

    ''' <summary>
    ''' Recupera sistema operativo e spazio disco di un server SFTP usando, in ordine: i
    ''' dati della sessione SFTP gia' aperta, l'estensione SFTP statvfs@openssh.com (che
    ''' funziona anche senza shell) e infine una connessione SSH ausiliaria con i comandi
    ''' adatti al sistema riconosciuto, Unix oppure Windows. Nessuna fase e' bloccante per
    ''' le altre: se una strada non e' disponibile si prosegue con le successive e i
    ''' dettagli tecnici finiscono solo nel log.
    ''' </summary>
    Private Sub RecuperaInfoServerSftp(sessione As SessioneFtp, risultato As InfoServerRemoto)
        Try
            Dim percorsoLavoro As String = "/"
            Dim verSsh As String = ""

            Try
                SyncLock sessione.LockOggetto
                    If sessione.ClientSftp IsNot Nothing Then
                        Try
                            If sessione.ClientSftp.ConnectionInfo IsNot Nothing Then
                                verSsh = If(sessione.ClientSftp.ConnectionInfo.ServerVersion, "").Trim()
                                If Not String.IsNullOrWhiteSpace(verSsh) Then
                                    risultato.SistemaOperativo = verSsh
                                End If
                            End If
                        Catch ex As Exception
                            ScriviLog("CLSFtp.RecuperaInfoServerSftp - ServerVersion", ex)
                        End Try
                        Try
                            Dim wd As String = sessione.ClientSftp.WorkingDirectory
                            If Not String.IsNullOrWhiteSpace(wd) Then percorsoLavoro = wd
                        Catch ex As Exception
                            ScriviLog("CLSFtp.RecuperaInfoServerSftp - WorkingDirectory", ex)
                        End Try
                    End If
                End SyncLock
            Catch ex As Exception
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - lettura sessione SFTP", ex)
            End Try

            Dim eWindows As Boolean = TestoIndicaWindows(verSsh) OrElse PercorsoIndicaWindows(percorsoLavoro)

            percorsoLavoro = percorsoLavoro.Replace("\", "/")
            If String.IsNullOrWhiteSpace(percorsoLavoro) OrElse percorsoLavoro = "." Then
                percorsoLavoro = If(String.IsNullOrEmpty(sessione.Server.CartellaIniziale), "/", sessione.Server.CartellaIniziale)
                percorsoLavoro = percorsoLavoro.Replace("\", "/")
                If PercorsoIndicaWindows(percorsoLavoro) Then eWindows = True
            End If
            percorsoLavoro = percorsoLavoro.Replace(Chr(34), "")
            If String.IsNullOrWhiteSpace(percorsoLavoro) Then percorsoLavoro = "/"

            risultato.ServerWindows = eWindows
            sessione.SistemaWindowsRilevato = eWindows
            Dim unita As String = LetteraUnitaDaPercorsoRemoto(percorsoLavoro)

            ProvaSpazioDiscoSftpNativo(sessione, percorsoLavoro, risultato)

            Try
                Dim password As String = DecriptaPasswordFtp(sessione.Server.PasswordCriptata)
                Using ssh As New SshClient(sessione.Server.Host, sessione.Server.Porta, sessione.Server.Utente, password)
                    ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10)
                    AddHandler ssh.HostKeyReceived, AddressOf AccettaChiaveHostAusiliaria
                    Dim shellDisponibile As Boolean = True
                    Try
                        ssh.Connect()
                    Catch exAuth As SshAuthenticationException
                        ScriviLog("CLSFtp.RecuperaInfoServerSftp - auth SSH ausiliaria fallita", exAuth)
                        shellDisponibile = False
                    Catch exKex As SshConnectionException
                        ScriviLog("CLSFtp.RecuperaInfoServerSftp - connessione SSH ausiliaria fallita", exKex)
                        shellDisponibile = False
                    Catch exSock As SocketException
                        ScriviLog("CLSFtp.RecuperaInfoServerSftp - socket SSH ausiliaria", exSock)
                        shellDisponibile = False
                    Catch exIo As IOException
                        ScriviLog("CLSFtp.RecuperaInfoServerSftp - IO SSH ausiliaria", exIo)
                        shellDisponibile = False
                    Catch ex As Exception
                        ScriviLog("CLSFtp.RecuperaInfoServerSftp - connect SSH ausiliaria", ex)
                        shellDisponibile = False
                    End Try

                    If shellDisponibile Then
                        Try
                            Dim nomeOsTrovato As String = ""
                            For Each comandoOs As String In ComandiSistemaOperativo(eWindows)
                                Dim testoOs As String = EseguiComandoSshSicuro(ssh, comandoOs)
                                If String.IsNullOrWhiteSpace(testoOs) Then Continue For
                                Dim nomeOs As String = InterpretaOutputSistemaOperativo(comandoOs, testoOs)
                                If String.IsNullOrWhiteSpace(nomeOs) Then Continue For
                                nomeOsTrovato = nomeOs
                                Exit For
                            Next
                            If Not String.IsNullOrWhiteSpace(nomeOsTrovato) Then
                                risultato.SistemaOperativo = nomeOsTrovato
                                If TestoIndicaWindows(nomeOsTrovato) Then
                                    eWindows = True
                                    risultato.ServerWindows = True
                                    sessione.SistemaWindowsRilevato = True
                                End If
                            End If

                            If Not risultato.SpazioTotale.HasValue Then
                                For Each comandoSpazio As String In ComandiSpazioDisco(eWindows, percorsoLavoro, unita)
                                    Dim outputSpazio As String = EseguiComandoSshSicuro(ssh, comandoSpazio)
                                    If String.IsNullOrWhiteSpace(outputSpazio) Then Continue For
                                    InterpretaOutputSpazioDisco(comandoSpazio, outputSpazio, unita, risultato)
                                    If risultato.SpazioTotale.HasValue Then Exit For
                                Next
                            End If
                        Finally
                            Try
                                If ssh.IsConnected Then ssh.Disconnect()
                            Catch exChiusura As Exception
                                ScriviLog("CLSFtp.RecuperaInfoServerSftp - chiusura SSH ausiliaria", exChiusura)
                            End Try
                        End Try
                    End If
                End Using
            Catch ex As SshConnectionException
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - SshConnectionException ausiliaria", ex)
            Catch ex As SshAuthenticationException
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - SshAuthenticationException ausiliaria", ex)
            Catch ex As SocketException
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - SocketException ausiliaria", ex)
            Catch ex As IOException
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - IOException ausiliaria", ex)
            Catch ex As Exception
                ScriviLog("CLSFtp.RecuperaInfoServerSftp - shell non disponibile (normale su solo-SFTP)", ex)
            End Try

            If String.IsNullOrWhiteSpace(risultato.SistemaOperativo) AndAlso Not String.IsNullOrWhiteSpace(verSsh) Then
                risultato.SistemaOperativo = verSsh
            End If

            If Not risultato.SpazioTotale.HasValue Then
                If risultato.ServerWindows Then
                    risultato.MessaggioErrore = "Server SFTP su Windows: lo spazio disco non e' disponibile perche' il server non supporta l'estensione SFTP statvfs e non consente di eseguire fsutil, wmic o PowerShell."
                Else
                    risultato.MessaggioErrore = "Lo spazio disco non e' disponibile: il server non supporta l'estensione SFTP statvfs e non consente di eseguire comandi di shell."
                End If
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.RecuperaInfoServerSftp - Errore", ex)
        End Try
    End Sub

    Private Function EstraiPrettyNameOsRelease(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim pretty As String = ""
            Dim name As String = ""
            For Each riga As String In testo.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim r As String = riga.Trim()
                If r.StartsWith("PRETTY_NAME=", StringComparison.OrdinalIgnoreCase) Then
                    pretty = r.Substring("PRETTY_NAME=".Length).Trim().Trim(Chr(34))
                ElseIf r.StartsWith("NAME=", StringComparison.OrdinalIgnoreCase) Then
                    name = r.Substring("NAME=".Length).Trim().Trim(Chr(34))
                End If
            Next
            If Not String.IsNullOrWhiteSpace(pretty) Then Return pretty
            Return name
        Catch ex As Exception
            ScriviLog("CLSFtp.EstraiPrettyNameOsRelease - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Interpreta l'output del comando Unix df -Pk (formato POSIX, blocchi da 1KB).
    ''' </summary>
    Private Sub ParseOutputDf(output As String, risultato As InfoServerRemoto)
        Try
            If String.IsNullOrEmpty(output) Then Return
            Dim righe As String() = output.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            For Each riga As String In righe
                Dim t As String = riga.Trim()
                If t.Length = 0 Then Continue For
                If t.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase) Then Continue For
                If t.StartsWith("df:", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim campi As String() = t.Split(New Char() {" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)
                If campi.Length < 4 Then Continue For
                Dim totaleKb As Long
                Dim disponibileKb As Long
                If Long.TryParse(campi(1), totaleKb) AndAlso Long.TryParse(campi(3), disponibileKb) Then
                    If totaleKb > 0 AndAlso disponibileKb >= 0 Then
                        risultato.SpazioTotale = totaleKb * 1024L
                        risultato.SpazioDisponibile = disponibileKb * 1024L
                        Return
                    End If
                End If
                If campi.Length >= 5 Then
                    Dim idxAvail As Integer = campi.Length - 3
                    Dim idxTotal As Integer = campi.Length - 5
                    If idxTotal >= 1 AndAlso Long.TryParse(campi(idxTotal), totaleKb) AndAlso Long.TryParse(campi(idxAvail), disponibileKb) Then
                        If totaleKb > 0 AndAlso disponibileKb >= 0 Then
                            risultato.SpazioTotale = totaleKb * 1024L
                            risultato.SpazioDisponibile = disponibileKb * 1024L
                            Return
                        End If
                    End If
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSFtp.ParseOutputDf - Errore", ex)
        End Try
    End Sub

    Private Sub RecuperaInfoServerFtp(sessione As SessioneFtp, risultato As InfoServerRemoto)
        Try
            risultato.ServerWindows = ServerRemotoEWindows(sessione)
            SyncLock sessione.LockOggetto
                Try
                    Dim os = sessione.ClientFtp.ServerOS
                    Dim tipo = sessione.ClientFtp.ServerType
                    Dim testoOs As String = $"{os} ({tipo})".Trim()
                    If testoOs <> "()" AndAlso Not String.IsNullOrWhiteSpace(testoOs) Then
                        risultato.SistemaOperativo = testoOs
                    End If
                Catch ex As Exception
                    ScriviLog("RecuperaInfoServerFtp - ServerOS", ex)
                End Try

                If String.IsNullOrWhiteSpace(risultato.SistemaOperativo) Then
                    Try
                        Dim syst = sessione.ClientFtp.Execute("SYST")
                        If Not String.IsNullOrWhiteSpace(syst.Message) Then
                            risultato.SistemaOperativo = syst.Message.Trim()
                        End If
                    Catch ex As Exception
                        ScriviLog("RecuperaInfoServerFtp - SYST", ex)
                    End Try
                End If

                Dim comandiSpazio As String() = {"SITE DSPACE", "AVBL", "SITE QUOTA"}
                For Each cmdSpazio As String In comandiSpazio
                    If risultato.SpazioDisponibile.HasValue Then Exit For
                    Try
                        Dim risposta = sessione.ClientFtp.Execute(cmdSpazio)
                        If Not risposta.Success Then Continue For
                        Dim msg As String = If(risposta.Message, "")
                        Dim numeri = System.Text.RegularExpressions.Regex.Matches(msg, "\d{3,}")
                        If numeri.Count = 0 Then Continue For
                        Dim n0 As Long
                        If Not Long.TryParse(numeri(0).Value, n0) OrElse n0 <= 0 Then Continue For
                        If numeri.Count >= 2 Then
                            Dim n1 As Long
                            If Long.TryParse(numeri(1).Value, n1) AndAlso n1 >= 0 Then
                                If n0 >= n1 Then
                                    risultato.SpazioTotale = n0
                                    risultato.SpazioDisponibile = n1
                                Else
                                    risultato.SpazioDisponibile = n0
                                End If
                            Else
                                risultato.SpazioDisponibile = n0
                            End If
                        Else
                            risultato.SpazioDisponibile = n0
                        End If
                    Catch ex As Exception
                        ScriviLog("CLSFtp.RecuperaInfoServerFtp - Errore", ex)
                    End Try
                Next
            End SyncLock

            If Not risultato.SpazioTotale.HasValue AndAlso Not risultato.SpazioDisponibile.HasValue Then
                risultato.MessaggioErrore = "Il protocollo FTP non prevede un comando standard per lo spazio disco e questo server non espone le estensioni non standard usate come ripiego."
            End If
        Catch ex As SocketException
            ScriviLog("CLSFtp.RecuperaInfoServerFtp - SocketException", ex)
        Catch ex As Exception
            ScriviLog("CLSFtp.RecuperaInfoServerFtp - Errore", ex)
        End Try
    End Sub


    ''' <summary>
    ''' Crea una cartella remota. Per SFTP crea ricorsivamente tutti i segmenti mancanti
    ''' del percorso (il protocollo SFTP non crea i genitori automaticamente).
    ''' Usa il lock della sessione perche' SftpClient/FtpClient non sono thread-safe.
    ''' Se la cartella esiste gia' viene considerata un successo.
    ''' </summary>
    Public Function CreaCartellaFtp(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            If String.IsNullOrWhiteSpace(percorsoRemoto) Then Return True

            Dim percorsoNorm As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            If EPercorsoRadiceRemoto(percorsoNorm) Then Return True

            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Try
                        If sessione.ClientSftp.Exists(percorsoNorm) Then Return True
                    Catch exVerifica As Exception
                        ScriviLog($"CLSFtp.CreaCartellaFtp - verifica esistenza non riuscita '{percorsoNorm}'", exVerifica)
                    End Try

                    Dim prefissoUnita As String = ""
                    Dim resto As String = percorsoNorm
                    If percorsoNorm.Length >= 3 AndAlso percorsoNorm(0) = "/"c AndAlso Char.IsLetter(percorsoNorm(1)) AndAlso percorsoNorm(2) = ":"c Then
                        prefissoUnita = percorsoNorm.Substring(0, 3)
                        resto = percorsoNorm.Substring(3)
                    End If
                    Dim assoluto As Boolean = percorsoNorm.StartsWith("/")
                    Dim segmenti As String() = resto.TrimStart("/"c).Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
                    Dim corrente As String = prefissoUnita
                    For Each segmento As String In segmenti
                        If String.IsNullOrEmpty(corrente) AndAlso Not assoluto Then
                            corrente = segmento
                        Else
                            corrente &= "/" & segmento
                        End If
                        Try
                            If Not sessione.ClientSftp.Exists(corrente) Then
                                sessione.ClientSftp.CreateDirectory(corrente)
                            End If
                        Catch exPath As SftpPathNotFoundException
                            Try
                                If sessione.ClientSftp.Exists(corrente) Then Continue For
                            Catch exVerificaSegmento As Exception
                                ScriviLog($"CLSFtp.CreaCartellaFtp - verifica segmento '{corrente}'", exVerificaSegmento)
                            End Try
                            ScriviLog($"CLSFtp.CreaCartellaFtp - PathNotFound {corrente}", exPath)
                            Return False
                        Catch exSftp As SshException
                            Try
                                If sessione.ClientSftp.Exists(corrente) Then Continue For
                            Catch exVerificaSegmento As Exception
                                ScriviLog($"CLSFtp.CreaCartellaFtp - verifica segmento '{corrente}'", exVerificaSegmento)
                            End Try
                            ScriviLog($"CLSFtp.CreaCartellaFtp - SshException {corrente}", exSftp)
                            Return False
                        Catch exSeg As Exception
                            Try
                                If sessione.ClientSftp.Exists(corrente) Then Continue For
                            Catch exVerificaSegmento As Exception
                                ScriviLog($"CLSFtp.CreaCartellaFtp - verifica segmento '{corrente}'", exVerificaSegmento)
                            End Try
                            ScriviLog($"CLSFtp.CreaCartellaFtp - Errore creazione {corrente}", exSeg)
                            Return False
                        End Try
                    Next
                    Return True
                Else
                    ' FluentFTP CreateDirectory e' gia' ricorsivo sulla maggior parte dei server
                    Try
                        If sessione.ClientFtp.DirectoryExists(percorsoNorm) Then Return True
                    Catch exVerificaFtp As Exception
                        ScriviLog($"CLSFtp.CreaCartellaFtp - verifica esistenza FTP '{percorsoNorm}'", exVerificaFtp)
                    End Try
                    sessione.ClientFtp.CreateDirectory(percorsoNorm)
                    Return True
                End If
            End SyncLock
        Catch ex As SftpPathNotFoundException
            ScriviLog($"CLSFtp.CreaCartellaFtp - PathNotFound {percorsoRemoto}", ex)
            Return False
        Catch ex As SshException
            ScriviLog($"CLSFtp.CreaCartellaFtp - SshException {percorsoRemoto}", ex)
            Return False
        Catch ex As SocketException
            ScriviLog($"CLSFtp.CreaCartellaFtp - SocketException {percorsoRemoto}", ex)
            Return False
        Catch ex As Exception
            ScriviLog($"CLSFtp.CreaCartellaFtp - Errore creazione {percorsoRemoto}", ex)
            Return False
        End Try
    End Function

    Public Function CreaCartellaFtpRicorsiva(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Return CreaCartellaFtp(sessione, percorsoRemoto)
    End Function

    Public Function EliminaFileFtp(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    sessione.ClientSftp.DeleteFile(percorso)
                Else
                    sessione.ClientFtp.DeleteFile(percorso)
                End If
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.EliminaFileFtp - Errore eliminazione {percorsoRemoto}", ex)
            Return False
        End Try
    End Function

    Public Function EliminaCartellaFtpVuota(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    sessione.ClientSftp.DeleteDirectory(percorso)
                Else
                    sessione.ClientFtp.DeleteDirectory(percorso, FtpListOption.Auto)
                End If
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.EliminaCartellaFtpVuota - Errore eliminazione {percorsoRemoto}", ex)
            Return False
        End Try
    End Function

    Public Function EliminaCartellaFtpRicorsiva(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Dim elementi = sessione.ClientSftp.ListDirectory(percorso)
                    For Each item In elementi
                        Dim nome As String = NomeSoloDaVoceRemota(item.Name)
                        If String.IsNullOrEmpty(nome) Then Continue For
                        If nome = "." OrElse nome = ".." Then Continue For
                        Dim percorsoFiglio As String = CombinaPercorsoRemoto(percorso, nome)
                        If item.IsDirectory AndAlso Not item.IsSymbolicLink Then
                            EliminaCartellaFtpRicorsiva(sessione, percorsoFiglio)
                        Else
                            sessione.ClientSftp.DeleteFile(percorsoFiglio)
                        End If
                    Next
                    sessione.ClientSftp.DeleteDirectory(percorso)
                Else
                    sessione.ClientFtp.DeleteDirectory(percorso)
                End If
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.EliminaCartellaFtpRicorsiva - Errore", ex)
            Return False
        End Try
    End Function

    Public Function RinominaFtp(sessione As SessioneFtp, percorsoRemotoVecchio As String, percorsoRemotoNuovo As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorsoVecchio As String = NormalizzaPercorsoRemoto(percorsoRemotoVecchio)
            Dim percorsoNuovo As String = NormalizzaPercorsoRemoto(percorsoRemotoNuovo)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    sessione.ClientSftp.RenameFile(percorsoVecchio, percorsoNuovo)
                Else
                    sessione.ClientFtp.Rename(percorsoVecchio, percorsoNuovo)
                End If
            End SyncLock
            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.RinominaFtp - Errore rinomina {percorsoRemotoVecchio} -> {percorsoRemotoNuovo}", ex)
            Return False
        End Try
    End Function

    Public Function OttieniDimensioneFileFtp(sessione As SessioneFtp, percorsoRemoto As String) As Long
        Try
            If Not SessionePronta(sessione) Then Return -1
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Dim attr = sessione.ClientSftp.GetAttributes(percorso)
                    If attr Is Nothing Then Return -1
                    Return attr.Size
                Else
                    Return sessione.ClientFtp.GetFileSize(percorso)
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSFtp.OttieniDimensioneFileFtp - Errore", ex)
            Return -1
        End Try
    End Function

    Public Property RegistraDiagnostica As Boolean = False
    Private _offsetOrarioServerMinuti As Integer? = Nothing

    ''' <summary>
    ''' Minuti da sommare alle date restituite dal server, letti una sola volta dalle
    ''' impostazioni e poi tenuti in memoria per non interrogare il database a ogni elenco.
    ''' </summary>
    Private Function OffsetOrarioServerMinuti() As Integer
        Try
            If Not _offsetOrarioServerMinuti.HasValue Then
                Dim impostazioni As InfoImpostazioniFtp = OttieniImpostazioniFtp()
                If impostazioni Is Nothing Then
                    _offsetOrarioServerMinuti = 0
                Else
                    _offsetOrarioServerMinuti = impostazioni.OffsetOrarioServerMinuti
                End If
            End If
            Return _offsetOrarioServerMinuti.Value
        Catch ex As Exception
            ScriviLog("CLSFtp.OffsetOrarioServerMinuti - Errore", ex)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Riporta una data restituita dal server all'ora locale applicando l'eventuale
    ''' scostamento configurato, lasciando intatte le date non valorizzate.
    ''' </summary>
    Private Function DataRemotaNormalizzata(data As DateTime, minutiOffset As Integer) As DateTime
        Try
            If minutiOffset = 0 Then Return data
            If data = DateTime.MinValue Then Return data
            Return data.AddMinutes(minutiOffset)
        Catch ex As Exception
            ScriviLog("CLSFtp.DataRemotaNormalizzata - Errore", ex)
            Return data
        End Try
    End Function

    ''' <summary>
    ''' Scrive nel log una riga di diagnostica sulle operazioni remote. Attiva solo quando
    ''' RegistraDiagnostica e' vero: serve a capire cosa e' stato realmente chiesto al server
    ''' quando questo si comporta in modo inatteso.
    ''' </summary>
    Private Sub Diagnostica(messaggio As String)
        Try
            If Not RegistraDiagnostica Then Return
        Catch ex As Exception
            ScriviLog("CLSFtp.Diagnostica - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Applica alla sessione le impostazioni generali salvate (codifica dei nomi file,
    ''' modalita' attiva o passiva, mantenimento della connessione, diagnostica).
    ''' </summary>
    Private Sub ApplicaImpostazioniClientFtp(client As FtpClient)
        Try
            If client Is Nothing Then Return
            Dim impostazioni As InfoImpostazioniFtp = OttieniImpostazioniFtp()
            If impostazioni Is Nothing Then Return
            RegistraDiagnostica = impostazioni.LogProtocollo
            ImpostaCodificaClientFtp(client, impostazioni.CodificaNomiFile)
            If impostazioni.ModalitaAttivaFtp Then
                client.Config.DataConnectionType = FtpDataConnectionType.AutoActive
            Else
                client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive
            End If
            If impostazioni.MantieniConnessioneAttiva Then
                client.Config.NoopInterval = INTERVALLO_KEEPALIVE_MS
            Else
                client.Config.NoopInterval = 0
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ApplicaImpostazioniClientFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Impone al client FTP la codifica da usare per i nomi dei file. Il nome della
    ''' proprieta' cambia fra le versioni di FluentFTP (in alcune e' Encoding sul client, in
    ''' altre EncodingOverride nella configurazione), quindi viene cercata a runtime invece
    ''' di legare il codice a una versione precisa della libreria.
    ''' </summary>
    Private Sub ImpostaCodificaClientFtp(client As FtpClient, nomeCodifica As String)
        Try
            If client Is Nothing OrElse String.IsNullOrWhiteSpace(nomeCodifica) Then Return

            Dim codifica As Encoding = Nothing
            Try
                codifica = Encoding.GetEncoding(nomeCodifica.Trim())
            Catch exCodifica As Exception
                ScriviLog("CLSFtp.ImpostaCodificaClientFtp - codifica non valida: " & nomeCodifica, exCodifica)
                Return
            End Try
            If codifica Is Nothing Then Return

            Try
                Dim proprietaClient As System.Reflection.PropertyInfo = client.GetType().GetProperty("Encoding")
                If proprietaClient IsNot Nothing AndAlso proprietaClient.CanWrite AndAlso proprietaClient.PropertyType.IsAssignableFrom(GetType(Encoding)) Then
                    proprietaClient.SetValue(client, codifica, Nothing)
                    Diagnostica("Codifica dei nomi file impostata sul client: " & codifica.WebName)
                    Return
                End If
            Catch exClient As Exception
                ScriviLog("CLSFtp.ImpostaCodificaClientFtp - proprieta' Encoding del client", exClient)
            End Try

            Try
                Dim configurazione As Object = client.Config
                If configurazione Is Nothing Then Return
                Dim proprietaConfig As System.Reflection.PropertyInfo = configurazione.GetType().GetProperty("EncodingOverride")
                If proprietaConfig Is Nothing Then proprietaConfig = configurazione.GetType().GetProperty("Encoding")
                If proprietaConfig IsNot Nothing AndAlso proprietaConfig.CanWrite AndAlso proprietaConfig.PropertyType.IsAssignableFrom(GetType(Encoding)) Then
                    proprietaConfig.SetValue(configurazione, codifica, Nothing)
                    Diagnostica("Codifica dei nomi file impostata nella configurazione: " & codifica.WebName)
                    Return
                End If
            Catch exConfig As Exception
                ScriviLog("CLSFtp.ImpostaCodificaClientFtp - proprieta' di configurazione", exConfig)
            End Try

        Catch ex As Exception
            ScriviLog("CLSFtp.ImpostaCodificaClientFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Crea e connette il client SFTP di una sessione. L'impronta dell'host viene registrata
    ''' nella sessione stessa. In caso di fallimento della negoziazione degli algoritmi viene
    ''' composto un messaggio dettagliato per capire da che parte sta il problema.
    ''' </summary>
    ''' <summary>
    ''' Riconosce dai messaggi di errore se la connessione e' fallita perche' le credenziali
    ''' non sono state accettate, invece che per un problema di rete o di configurazione.
    ''' Serve all'interfaccia per proporre il reinserimento della password.
    ''' </summary>
    Public Shared Function MessaggioIndicaAutenticazione(messaggio As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(messaggio) Then Return False
            Dim low As String = messaggio.ToLowerInvariant()
            Dim indizi As String() = {
                "530",
                "not logged in",
                "login authentication failed",
                "authentication failed",
                "authentication error",
                "permission denied",
                "invalid credentials",
                "incorrect password",
                "wrong password",
                "bad password",
                "user cannot log in",
                "login incorrect",
                "access denied",
                "autenticazione",
                "password non",
                "accesso negato"
            }
            For Each indizio As String In indizi
                If low.Contains(indizio) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("CLSFtp.MessaggioIndicaAutenticazione - Errore", ex)
            Return False
        End Try
    End Function

    Private Function CreaClientSftp(server As InfoServerFtp, sessione As SessioneFtp, ByRef messaggioErrore As String, ByRef autenticazioneFallita As Boolean) As SftpClient
        messaggioErrore = ""
        autenticazioneFallita = False
        Try
            Dim password As String = DecriptaPasswordFtp(server.PasswordCriptata)
            Dim client As New SftpClient(server.Host, server.Porta, server.Utente, password)
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15)
            client.OperationTimeout = TimeSpan.FromSeconds(60)
            Try
                Dim impostazioni As InfoImpostazioniFtp = OttieniImpostazioniFtp()
                If impostazioni IsNot Nothing Then
                    RegistraDiagnostica = impostazioni.LogProtocollo
                    If impostazioni.MantieniConnessioneAttiva Then
                        client.KeepAliveInterval = TimeSpan.FromMilliseconds(INTERVALLO_KEEPALIVE_MS)
                    End If
                End If
            Catch exImpostazioni As Exception
                ScriviLog("CLSFtp.CreaClientSftp - lettura impostazioni", exImpostazioni)
            End Try

            AddHandler client.HostKeyReceived, Sub(sender As Object, e As HostKeyEventArgs)
                                                   Try
                                                       If sessione IsNot Nothing Then
                                                           sessione.ImprontaSftp = Convert.ToBase64String(e.FingerPrint)
                                                       End If
                                                   Catch exImpronta As Exception
                                                       CLSFtp.ScriviLog("CLSFtp.CreaClientSftp - Errore calcolo impronta", exImpronta)
                                                   End Try
                                                   e.CanTrust = True
                                               End Sub

            Try
                client.Connect()
            Catch exKex As Renci.SshNet.Common.SshConnectionException When exKex.Message.IndexOf("key exchange", StringComparison.OrdinalIgnoreCase) >= 0
                Dim algoritmiServer As List(Of String) = OttieniAlgoritmiKexServer(server.Host, server.Porta)
                Dim algoritmiClient As List(Of String) = client.ConnectionInfo.KeyExchangeAlgorithms.Keys.ToList()
                Dim versioneAssembly As String = GetType(SftpClient).Assembly.GetName().Version.ToString()
                messaggioErrore = "Il server SFTP e il client non hanno nessun algoritmo di key-exchange in comune. " &
                                   "Assembly Renci.SshNet caricato: versione " & versioneAssembly & ". " &
                                   "Algoritmi KEX supportati dal client: " & String.Join(", ", algoritmiClient) & ". " &
                                   "Algoritmi KEX offerti dal server: " & String.Join(", ", algoritmiServer) & ". " &
                                   "Se tra gli algoritmi del client NON compaiono mlkem768x25519-sha256 o sntrup761x25519-sha512, " &
                                   "significa che a runtime NON e' caricata la versione 2025.0.0 o successiva di SSH.NET " &
                                   "(quella che ha introdotto il supporto post-quantum), anche se il pacchetto NuGet scaricato e' piu' recente: " &
                                   "verificare riferimenti duplicati o vecchi nel progetto, cartelle bin e obj non pulite, o versioni in conflitto."
                ScriviLog("CLSFtp.CreaClientSftp - Negoziazione KEX fallita. " & messaggioErrore, exKex)
                Try
                    client.Dispose()
                Catch exChiusura As Exception
                    ScriviLog("CLSFtp.CreaClientSftp - chiusura dopo KEX fallito", exChiusura)
                End Try
                Return Nothing
            End Try

            Diagnostica("Sessione SFTP aperta verso " & server.Host & ":" & server.Porta)
            Return client
        Catch exAuth As SshAuthenticationException
            ScriviLog("CLSFtp.CreaClientSftp - Autenticazione non riuscita", exAuth)
            autenticazioneFallita = True
            messaggioErrore = "Nome utente o password non accettati dal server."
            Return Nothing
        Catch ex As Exception
            ScriviLog("CLSFtp.CreaClientSftp - Errore", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Connessione SFTP non riuscita.", ex.Message)
            autenticazioneFallita = MessaggioIndicaAutenticazione(messaggioErrore)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Crea e connette il client FTP o FTPS di una sessione. Il TLS implicito viene scelto
    ''' automaticamente quando la porta e' la 990, che e' la porta convenzionale dell'FTPS
    ''' implicito; su tutte le altre porte si usa il TLS esplicito.
    ''' </summary>
    Private Function CreaClientFtp(server As InfoServerFtp, ByRef messaggioErrore As String, ByRef autenticazioneFallita As Boolean) As FtpClient
        messaggioErrore = ""
        autenticazioneFallita = False
        Dim ultimoErrore As Exception = Nothing
        For tentativo As Integer = 1 To TENTATIVI_CONNESSIONE_FTP
            Dim client As FtpClient = Nothing
            Try
                Dim password As String = DecriptaPasswordFtp(server.PasswordCriptata)
                client = New FtpClient(server.Host, server.Utente, password, server.Porta)
                ' 15 secondi come la connessione SFTP: con 8 secondi sulle reti lente la
                ' connessione veniva data per scaduta (TimeoutException nel log di logs.txt).
                client.Config.ConnectTimeout = TIMEOUT_CONNESSIONE_FTP_MS
                client.Config.ReadTimeout = 60000
                client.Config.DataConnectionConnectTimeout = TIMEOUT_CONNESSIONE_FTP_MS
                client.Config.DataConnectionReadTimeout = 60000
                If server.Protocollo = TipoProtocolloFtp.Ftps Then
                    If server.Porta = PORTA_FTPS_IMPLICITO Then
                        client.Config.EncryptionMode = FtpEncryptionMode.Implicit
                    Else
                        client.Config.EncryptionMode = FtpEncryptionMode.Explicit
                    End If
                    client.Config.ValidateAnyCertificate = True
                End If
                If server.LimiteVelocitaKBs > 0 Then
                    client.Config.DownloadRateLimit = server.LimiteVelocitaKBs
                    client.Config.UploadRateLimit = server.LimiteVelocitaKBs
                End If
                ApplicaImpostazioniClientFtp(client)
                client.Connect()
                Diagnostica("Sessione FTP aperta verso " & server.Host & ":" & server.Porta)
                Return client
            Catch ex As Exception
                Try
                    If client IsNot Nothing Then client.Dispose()
                Catch exChiusura As Exception
                    ScriviLog("CLSFtp.CreaClientFtp - chiusura dopo errore", exChiusura)
                End Try
                ultimoErrore = ex
                ' Si riprova una volta solo se l'errore è di rete (tempo scaduto o socket non
                ' raggiungibile): con credenziali sbagliate un secondo tentativo è tempo perso.
                If Not ErroreDiRete(ex) OrElse tentativo = TENTATIVI_CONNESSIONE_FTP Then Exit For
                Thread.Sleep(ATTESA_TRA_TENTATIVI_MS)
            End Try
        Next
        Dim messaggioEccezione As String = If(ultimoErrore Is Nothing, "", ultimoErrore.Message)
        ScriviLog("CLSFtp.CreaClientFtp - Errore", ultimoErrore)
        If TypeOf ultimoErrore Is TimeoutException Then
            ' Prima il messaggio grezzo della libreria ("Timed out trying to connect to IP #1"):
            ' l'utente non capiva che il server non rispondeva.
            messaggioErrore = "Tempo scaduto per la connessione al server FTP. Verificare indirizzo, porta e connessione di rete."
        Else
            messaggioErrore = If(String.IsNullOrWhiteSpace(messaggioEccezione), "Connessione FTP non riuscita.", messaggioEccezione)
        End If
        autenticazioneFallita = MessaggioIndicaAutenticazione(messaggioEccezione) OrElse MessaggioIndicaAutenticazione(If(ultimoErrore Is Nothing, "", ultimoErrore.ToString()))
        If autenticazioneFallita Then
            messaggioErrore = "Nome utente o password non accettati dal server."
        End If
        Return Nothing
    End Function

    ''' <summary>
    ''' Vero se l'errore di connessione è di rete (tempo scaduto, socket non raggiungibile,
    ''' connessione caduta): vale la pena riprovare. Gli errori di autenticazione o di
    ''' protocollo non cambiano al secondo tentativo.
    ''' </summary>
    Private Shared Function ErroreDiRete(ex As Exception) As Boolean
        Try
            If ex Is Nothing Then Return False
            If TypeOf ex Is TimeoutException Then Return True
            If TypeOf ex Is SocketException Then Return True
            Dim io As IOException = TryCast(ex, IOException)
            If io IsNot Nothing Then Return ErroreDiRete(io.InnerException)
            Dim ftp As FluentFTP.Exceptions.FtpException = TryCast(ex, FluentFTP.Exceptions.FtpException)
            If ftp IsNot Nothing Then Return ErroreDiRete(ftp.InnerException)
            Return False
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Verifica che la sessione sia connessa e, se non lo e' piu', la riapre sul posto
    ''' mantenendo lo stesso oggetto sessione. Tutte le operazioni della classe passano da
    ''' qui, quindi una caduta di connessione non fa piu' fallire l'operazione in corso.
    ''' </summary>
    Public Function AssicuraConnessione(sessione As SessioneFtp, ByRef messaggioErrore As String) As Boolean
        messaggioErrore = ""
        Try
            If sessione Is Nothing OrElse sessione.Server Is Nothing Then
                messaggioErrore = "Sessione non valida."
                Return False
            End If
            SyncLock sessione.LockOggetto
                If sessione.Connessa Then Return True
                Diagnostica("Riconnessione in corso verso " & sessione.Server.Host)
                sessione.SistemaWindowsRilevato = Nothing
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Try
                        If sessione.ClientSftp IsNot Nothing Then sessione.ClientSftp.Dispose()
                    Catch exChiusura As Exception
                        ScriviLog("CLSFtp.AssicuraConnessione - chiusura vecchio client SFTP", exChiusura)
                    End Try
                    Dim autenticazioneFallitaSftp As Boolean = False
                    sessione.ClientSftp = CreaClientSftp(sessione.Server, sessione, messaggioErrore, autenticazioneFallitaSftp)
                    Return sessione.ClientSftp IsNot Nothing AndAlso sessione.ClientSftp.IsConnected
                Else
                    Try
                        If sessione.ClientFtp IsNot Nothing Then sessione.ClientFtp.Dispose()
                    Catch exChiusura As Exception
                        ScriviLog("CLSFtp.AssicuraConnessione - chiusura vecchio client FTP", exChiusura)
                    End Try
                    Dim autenticazioneFallitaFtp As Boolean = False
                    sessione.ClientFtp = CreaClientFtp(sessione.Server, messaggioErrore, autenticazioneFallitaFtp)
                    Return sessione.ClientFtp IsNot Nothing AndAlso sessione.ClientFtp.IsConnected
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSFtp.AssicuraConnessione - Errore", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Riconnessione non riuscita.", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Scorciatoia usata come guardia all'inizio di ogni operazione remota: riconnette se
    ''' necessario e dice se si puo' procedere.
    ''' </summary>
    Private Function SessionePronta(sessione As SessioneFtp) As Boolean
        Try
            Dim messaggioErrore As String = ""
            Return AssicuraConnessione(sessione, messaggioErrore)
        Catch ex As Exception
            ScriviLog("CLSFtp.SessionePronta - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Dice se un percorso remoto esiste, senza dover elencare l'intera cartella che lo
    ''' contiene.
    ''' </summary>
    Public Function EsisteRemoto(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    Return sessione.ClientSftp.Exists(percorso)
                Else
                    If sessione.ClientFtp.FileExists(percorso) Then Return True
                    Return sessione.ClientFtp.DirectoryExists(percorso)
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog($"CLSFtp.EsisteRemoto - Errore '{percorsoRemoto}'", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Dice se un percorso remoto esiste ed e' una cartella.
    ''' </summary>
    Public Function ECartellaRemota(sessione As SessioneFtp, percorsoRemoto As String) As Boolean
        Try
            If Not SessionePronta(sessione) Then Return False
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            SyncLock sessione.LockOggetto
                If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                    If Not sessione.ClientSftp.Exists(percorso) Then Return False
                    Dim attributi = sessione.ClientSftp.GetAttributes(percorso)
                    If attributi Is Nothing Then Return False
                    Return attributi.IsDirectory
                Else
                    Return sessione.ClientFtp.DirectoryExists(percorso)
                End If
            End SyncLock
        Catch ex As Exception
            ScriviLog($"CLSFtp.ECartellaRemota - Errore '{percorsoRemoto}'", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Restituisce le informazioni di un singolo elemento remoto senza elencare tutta la
    ''' cartella. Restituisce Nothing se l'elemento non esiste.
    ''' </summary>
    Public Function OttieniInfoElemento(sessione As SessioneFtp, percorsoRemoto As String) As InfoElementoFtp
        Try
            If Not SessionePronta(sessione) Then Return Nothing
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            If EPercorsoRadiceRemoto(percorso) Then
                Return New InfoElementoFtp() With {.Nome = NomeElementoRemoto(percorso), .PercorsoCompleto = percorso, .EDirectory = True}
            End If
            Dim cartella As String = CartellaPadreRemota(percorso)
            Dim nome As String = NomeElementoRemoto(percorso)
            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelle(sessione, cartella)
            For Each elemento As InfoElementoFtp In elementi
                If String.Equals(elemento.Nome, nome, StringComparison.Ordinal) Then Return elemento
            Next
            For Each elemento As InfoElementoFtp In elementi
                If String.Equals(elemento.Nome, nome, StringComparison.OrdinalIgnoreCase) Then Return elemento
            Next
            Return Nothing
        Catch ex As Exception
            ScriviLog($"CLSFtp.OttieniInfoElemento - Errore '{percorsoRemoto}'", ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Calcola il contenuto complessivo di una cartella remota: numero di file, numero di
    ''' sottocartelle e byte totali. Utile per mostrare un progresso attendibile prima di
    ''' avviare un trasferimento.
    ''' </summary>
    Public Function CalcolaContenutoCartellaRemota(sessione As SessioneFtp, percorsoRemoto As String, token As CancellationToken) As InfoContenutoCartella
        Dim risultato As New InfoContenutoCartella()
        Try
            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelleRicorsivo(sessione, percorsoRemoto)
            For Each elemento As InfoElementoFtp In elementi
                If token.IsCancellationRequested Then Exit For
                If elemento.EDirectory Then
                    risultato.CartelleTotali += 1
                Else
                    risultato.FileTotali += 1
                    If elemento.Dimensione > 0 Then risultato.ByteTotali += elemento.Dimensione
                End If
            Next
        Catch ex As Exception
            ScriviLog($"CLSFtp.CalcolaContenutoCartellaRemota - Errore '{percorsoRemoto}'", ex)
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Cerca ricorsivamente sul server gli elementi il cui nome contiene il testo indicato,
    ''' eventualmente filtrando per estensioni. Il numero massimo di risultati evita ricerche
    ''' che non finiscono piu' su alberi molto grandi.
    ''' </summary>
    Public Function CercaRemoto(sessione As SessioneFtp, percorsoRemoto As String, testoCercato As String, filtroEstensioni As String, massimoRisultati As Integer, token As CancellationToken) As List(Of InfoElementoFtp)
        Dim risultato As New List(Of InfoElementoFtp)()
        Try
            If Not SessionePronta(sessione) Then Return risultato
            Dim limite As Integer = massimoRisultati
            If limite <= 0 Then limite = 500
            Dim visitate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            CercaRemotoInterno(sessione, NormalizzaPercorsoRemoto(percorsoRemoto), testoCercato, filtroEstensioni, limite, token, visitate, 0, risultato)
        Catch ex As Exception
            ScriviLog($"CLSFtp.CercaRemoto - Errore '{percorsoRemoto}'", ex)
        End Try
        Return risultato
    End Function

    Private Sub CercaRemotoInterno(sessione As SessioneFtp, percorsoRemoto As String, testoCercato As String, filtroEstensioni As String, massimoRisultati As Integer, token As CancellationToken, visitate As HashSet(Of String), profondita As Integer, risultato As List(Of InfoElementoFtp))
        Try
            If risultato.Count >= massimoRisultati Then Return
            If token.IsCancellationRequested Then Return
            If profondita > PROFONDITA_MASSIMA_RICORSIONE Then Return
            If Not visitate.Add(percorsoRemoto) Then Return

            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelle(sessione, percorsoRemoto)
            For Each elemento As InfoElementoFtp In elementi
                If token.IsCancellationRequested Then Return
                If risultato.Count >= massimoRisultati Then Return
                Dim nomeCorrisponde As Boolean = String.IsNullOrEmpty(testoCercato) OrElse elemento.Nome.IndexOf(testoCercato, StringComparison.OrdinalIgnoreCase) >= 0
                Dim estensioneCorrisponde As Boolean = elemento.EDirectory OrElse EstensioneCorrispondeFiltro(EstensioneDaNomeRemoto(elemento.Nome), filtroEstensioni)
                If nomeCorrisponde AndAlso estensioneCorrisponde Then risultato.Add(elemento)
                If elemento.EDirectory Then
                    CercaRemotoInterno(sessione, elemento.PercorsoCompleto, testoCercato, filtroEstensioni, massimoRisultati, token, visitate, profondita + 1, risultato)
                End If
            Next
        Catch ex As Exception
            ScriviLog($"CLSFtp.CercaRemotoInterno - Errore '{percorsoRemoto}'", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Sposta un elemento all'interno dello stesso server usando la rinomina, che e'
    ''' immediata perche' non trasferisce dati. Restituisce False quando il server non lo
    ''' consente (tipicamente fra volumi diversi): in quel caso il chiamante deve ripiegare
    ''' su scarica e ricarica.
    ''' </summary>
    Public Function SpostaRemoto(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String) As Boolean
        messaggioErrore = ""
        Try
            If Not SessionePronta(sessione) Then
                messaggioErrore = "Sessione non connessa."
                Return False
            End If
            Dim origine As String = NormalizzaPercorsoRemoto(percorsoOrigine)
            Dim destinazione As String = NormalizzaPercorsoRemoto(percorsoDestinazione)
            If String.Equals(origine, destinazione, StringComparison.Ordinal) Then Return True

            Dim cartellaDestinazione As String = CartellaPadreRemota(destinazione)
            If Not EPercorsoRadiceRemoto(cartellaDestinazione) Then
                CreaCartellaFtp(sessione, cartellaDestinazione)
            End If

            Diagnostica($"Spostamento remoto '{origine}' verso '{destinazione}'")
            If RinominaFtp(sessione, origine, destinazione) Then Return True
            messaggioErrore = "Il server non ha consentito lo spostamento diretto."
            Return False
        Catch ex As Exception
            ScriviLog($"CLSFtp.SpostaRemoto - Errore '{percorsoOrigine}' -> '{percorsoDestinazione}'", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Spostamento remoto non riuscito.", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Sposta un singolo file all'interno dello stesso server. Prova prima la rinomina
    ''' diretta con SpostaRemoto (istantanea, nessun trasferimento dati); se il server la
    ''' rifiuta (tipicamente fra volumi diversi) ripiega su una copia completa seguita
    ''' dall'eliminazione dell'origine, eseguita solo se la copia e' andata a buon fine, cosi'
    ''' da non perdere mai il file in caso di errore a meta' strada.
    ''' </summary>
    Public Function SpostaFileRemotoConFallback(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As Boolean
        messaggioErrore = ""
        Try
            Dim erroreRename As String = ""
            If SpostaRemoto(sessione, percorsoOrigine, percorsoDestinazione, erroreRename) Then Return True

            Diagnostica($"Spostamento diretto file non riuscito ('{erroreRename}'), ripiego su copia+eliminazione: '{percorsoOrigine}' -> '{percorsoDestinazione}'")

            Dim erroreCopia As String = ""
            If Not CopiaFileRemoto(sessione, percorsoOrigine, percorsoDestinazione, erroreCopia, callbackProgresso, token) Then
                messaggioErrore = If(String.IsNullOrWhiteSpace(erroreCopia), "Spostamento file non riuscito.", erroreCopia)
                Return False
            End If

            If Not EliminaFileFtp(sessione, percorsoOrigine) Then
                messaggioErrore = "Il file è stato copiato nella nuova posizione, ma non è stato possibile eliminare l'originale."
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.SpostaFileRemotoConFallback - Errore '{percorsoOrigine}' -> '{percorsoDestinazione}'", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Spostamento file non riuscito.", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Sposta una cartella e tutto il suo contenuto all'interno dello stesso server. Prova
    ''' prima la rinomina diretta con SpostaRemoto (istantanea); se il server la rifiuta
    ''' ripiega su una copia completa della cartella (CopiaCartellaRemota) seguita
    ''' dall'eliminazione ricorsiva dell'origine, eseguita solo se la copia e' andata
    ''' interamente a buon fine.
    ''' </summary>
    Public Function SpostaCartellaRemota(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As Boolean
        messaggioErrore = ""
        Try
            If Not SessionePronta(sessione) Then
                messaggioErrore = "Sessione non valida o non connessa."
                Return False
            End If
            Dim origine As String = NormalizzaPercorsoRemoto(percorsoOrigine)
            Dim destinazione As String = NormalizzaPercorsoRemoto(percorsoDestinazione)
            If String.Equals(origine, destinazione, StringComparison.Ordinal) Then Return True
            If destinazione.StartsWith(origine.TrimEnd("/"c) & "/", StringComparison.Ordinal) Then
                messaggioErrore = "Non è possibile spostare una cartella dentro se stessa."
                Return False
            End If

            Dim erroreRename As String = ""
            If SpostaRemoto(sessione, origine, destinazione, erroreRename) Then Return True

            Diagnostica($"Spostamento diretto cartella non riuscito ('{erroreRename}'), ripiego su copia+eliminazione: '{origine}' -> '{destinazione}'")

            Dim erroreCopia As String = ""
            If Not CopiaCartellaRemota(sessione, origine, destinazione, erroreCopia, callbackProgresso, token) Then
                messaggioErrore = If(String.IsNullOrWhiteSpace(erroreCopia), "Spostamento cartella non riuscito.", erroreCopia)
                Return False
            End If

            If Not EliminaCartellaFtpRicorsiva(sessione, origine) Then
                messaggioErrore = "La cartella è stata copiata nella nuova posizione, ma non è stato possibile eliminare l'originale."
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.SpostaCartellaRemota - Errore '{percorsoOrigine}' -> '{percorsoDestinazione}'", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Spostamento cartella non riuscito.", ex.Message)
            Return False
        End Try
    End Function


    ''' <summary>
    ''' Copia i dati da un flusso all'altro rispettando annullamento e limite di velocita',
    ''' segnalando l'avanzamento. E' il cuore condiviso dei trasferimenti SFTP con ripresa.
    ''' </summary>
    Private Function CopiaFlussoConControllo(origine As Stream, destinazione As Stream, byteGiaTrasferiti As Long, dimensioneTotale As Long, limiteKBs As Integer, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As Boolean
        Try
            Dim buffer(DIMENSIONE_BUFFER_TRASFERIMENTO - 1) As Byte
            Dim trasferiti As Long = byteGiaTrasferiti
            Dim orarioRiferimento As DateTime = DateTime.Now
            Dim byteFinestra As Long = 0
            Dim bytesAlSecondo As Long = 0
            If limiteKBs > 0 Then bytesAlSecondo = CLng(limiteKBs) * 1024L

            Do
                If token.IsCancellationRequested Then Return False
                Dim letti As Integer = origine.Read(buffer, 0, buffer.Length)
                If letti <= 0 Then Exit Do
                destinazione.Write(buffer, 0, letti)
                trasferiti += letti
                byteFinestra += letti

                If callbackProgresso IsNot Nothing Then
                    Try
                        callbackProgresso(trasferiti, dimensioneTotale)
                    Catch exCallback As Exception
                        ScriviLog("CLSFtp.CopiaFlussoConControllo - Errore callback progresso", exCallback)
                    End Try
                End If

                If bytesAlSecondo > 0 Then
                    Dim secondiTrascorsi As Double = (DateTime.Now - orarioRiferimento).TotalSeconds
                    Dim secondiAttesi As Double = byteFinestra / bytesAlSecondo
                    If secondiAttesi > secondiTrascorsi Then
                        Dim attesaMs As Integer = CInt(Math.Min((secondiAttesi - secondiTrascorsi) * 1000.0R, 2000.0R))
                        If attesaMs > 0 Then
                            If token.CanBeCanceled Then
                                If token.WaitHandle.WaitOne(attesaMs) Then Return False
                            Else
                                Thread.Sleep(attesaMs)
                            End If
                        End If
                    End If
                    If secondiTrascorsi > 1 Then
                        orarioRiferimento = DateTime.Now
                        byteFinestra = 0
                    End If
                End If
            Loop

            destinazione.Flush()
            Return True
        Catch ex As Exception
            ScriviLog("CLSFtp.CopiaFlussoConControllo - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Copia un file all'interno dello stesso server, senza eliminare l'origine. Su SFTP i
    ''' dati vengono trasferiti direttamente da un flusso remoto all'altro, senza mai
    ''' toccare il disco locale. Su FTP "classico" il protocollo non offre una copia lato
    ''' server ne' regge due trasferimenti dati simultanei sulla stessa connessione: si
    ''' ripiega quindi su un download in un file temporaneo seguito dal caricamento nella
    ''' destinazione, cancellando comunque il temporaneo alla fine.
    ''' </summary>
    Public Function CopiaFileRemoto(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As Boolean
        messaggioErrore = ""
        Try
            If Not SessionePronta(sessione) Then
                messaggioErrore = "Sessione non connessa."
                Return False
            End If
            If token.IsCancellationRequested Then
                messaggioErrore = "Operazione annullata."
                Return False
            End If
            Dim origine As String = NormalizzaPercorsoRemoto(percorsoOrigine)
            Dim destinazione As String = NormalizzaPercorsoRemoto(percorsoDestinazione)
            If String.Equals(origine, destinazione, StringComparison.Ordinal) Then
                messaggioErrore = "L'origine e la destinazione della copia coincidono."
                Return False
            End If

            Dim cartellaDestinazione As String = CartellaPadreRemota(destinazione)
            If Not EPercorsoRadiceRemoto(cartellaDestinazione) Then
                CreaCartellaFtpRicorsiva(sessione, cartellaDestinazione)
            End If

            Diagnostica($"Copia remota '{origine}' -> '{destinazione}'")

            If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                Dim dimensioneTotale As Long = OttieniDimensioneFileFtp(sessione, origine)
                If dimensioneTotale < 0 Then dimensioneTotale = 0
                SyncLock sessione.LockOggetto
                    Using flussoOrigine As Stream = sessione.ClientSftp.OpenRead(origine)
                        Using flussoDestinazione As Stream = sessione.ClientSftp.Open(destinazione, FileMode.Create, FileAccess.Write)
                            Dim esito As Boolean = CopiaFlussoConControllo(flussoOrigine, flussoDestinazione, 0, dimensioneTotale, sessione.Server.LimiteVelocitaKBs, callbackProgresso, token)
                            If Not esito Then
                                messaggioErrore = If(token.IsCancellationRequested, "Operazione annullata.", "Copia remota non riuscita.")
                                Return False
                            End If
                            Return True
                        End Using
                    End Using
                End SyncLock
            Else
                Dim percorsoTemp As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinGabriel_Ftp_Copia_" & Guid.NewGuid().ToString("N") & EstensioneDaNomeRemoto(origine))
                Try
                    Dim erroreDownload As String = ""
                    If Not ScaricaFile(sessione, origine, percorsoTemp, erroreDownload, callbackProgresso, token, False) Then
                        messaggioErrore = If(String.IsNullOrWhiteSpace(erroreDownload), "Copia remota non riuscita durante lo scaricamento.", erroreDownload)
                        Return False
                    End If
                    Dim erroreUpload As String = ""
                    If Not CaricaFile(sessione, percorsoTemp, destinazione, erroreUpload, callbackProgresso, token, False) Then
                        messaggioErrore = If(String.IsNullOrWhiteSpace(erroreUpload), "Copia remota non riuscita durante il caricamento.", erroreUpload)
                        Return False
                    End If
                    Return True
                Finally
                    Try
                        If File.Exists(percorsoTemp) Then File.Delete(percorsoTemp)
                    Catch exPulizia As Exception
                        ScriviLog("CLSFtp.CopiaFileRemoto - Errore pulizia file temporaneo", exPulizia)
                    End Try
                End Try
            End If
        Catch ex As Exception
            ScriviLog($"CLSFtp.CopiaFileRemoto - Errore '{percorsoOrigine}' -> '{percorsoDestinazione}'", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Copia remota non riuscita.", ex.Message)
            Return False
        End Try
    End Function

    Public Function CopiaFileRemoto(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String) As Boolean
        Return CopiaFileRemoto(sessione, percorsoOrigine, percorsoDestinazione, messaggioErrore, Nothing, CancellationToken.None)
    End Function

    ''' <summary>
    ''' Copia una cartella e tutto il suo contenuto in un'altra posizione dello stesso
    ''' server, ricreando la struttura delle sottocartelle sotto la destinazione. Il
    ''' progresso passato al callback e' complessivo su tutti i file della cartella.
    ''' </summary>
    Public Function CopiaCartellaRemota(sessione As SessioneFtp, percorsoOrigine As String, percorsoDestinazione As String, ByRef messaggioErrore As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As Boolean
        messaggioErrore = ""
        Try
            If Not SessionePronta(sessione) Then
                messaggioErrore = "Sessione non valida o non connessa."
                Return False
            End If

            Dim radiceOrigine As String = NormalizzaPercorsoRemoto(percorsoOrigine)
            Dim radiceDestinazione As String = NormalizzaPercorsoRemoto(percorsoDestinazione)
            If radiceDestinazione.StartsWith(radiceOrigine.TrimEnd("/"c) & "/", StringComparison.Ordinal) OrElse String.Equals(radiceOrigine, radiceDestinazione, StringComparison.Ordinal) Then
                messaggioErrore = "Non è possibile copiare una cartella dentro se stessa."
                Return False
            End If

            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelleRicorsivo(sessione, radiceOrigine)
            Dim byteTotali As Long = 0
            For Each elemento As InfoElementoFtp In elementi
                If Not elemento.EDirectory AndAlso elemento.Dimensione > 0 Then byteTotali += elemento.Dimensione
            Next

            Dim cartellaPadreDestinazione As String = CartellaPadreRemota(radiceDestinazione)
            If Not EPercorsoRadiceRemoto(cartellaPadreDestinazione) Then
                CreaCartellaFtpRicorsiva(sessione, cartellaPadreDestinazione)
            End If
            CreaCartellaFtp(sessione, radiceDestinazione)

            Dim byteCompletati As Long = 0
            For Each elemento As InfoElementoFtp In elementi
                If token.IsCancellationRequested Then
                    messaggioErrore = "Operazione annullata."
                    Return False
                End If
                Dim relativo As String = PercorsoRelativoRemoto(radiceOrigine, elemento.PercorsoCompleto)
                If String.IsNullOrEmpty(relativo) Then Continue For
                Dim destinazione As String = CombinaPercorsoRemoto(radiceDestinazione, relativo)

                If elemento.EDirectory Then
                    CreaCartellaFtp(sessione, destinazione)
                    Continue For
                End If

                Dim byteIniziali As Long = byteCompletati
                Dim callbackFile As Action(Of Long, Long) = Sub(trasferiti As Long, totali As Long)
                                                                Try
                                                                    If callbackProgresso IsNot Nothing Then
                                                                        callbackProgresso(byteIniziali + trasferiti, byteTotali)
                                                                    End If
                                                                Catch exProgresso As Exception
                                                                    ScriviLog("CLSFtp.CopiaCartellaRemota - Errore callback progresso", exProgresso)
                                                                End Try
                                                            End Sub

                Dim erroreFile As String = ""
                If Not CopiaFileRemoto(sessione, elemento.PercorsoCompleto, destinazione, erroreFile, callbackFile, token) Then
                    messaggioErrore = erroreFile
                    Return False
                End If
                byteCompletati += Math.Max(0L, elemento.Dimensione)
            Next

            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.CopiaCartellaRemota - Errore '{percorsoOrigine}' -> '{percorsoDestinazione}'", ex)
            messaggioErrore = If(String.IsNullOrWhiteSpace(ex.Message), "Copia remota non riuscita.", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Esito di un'operazione massiva (copia/spostamento/eliminazione) su piu' elementi
    ''' remoti in un colpo solo: quanti sono andati a buon fine e, per ciascun fallimento,
    ''' il percorso dell'elemento e il relativo messaggio di errore. L'operazione non si
    ''' interrompe al primo errore: prosegue con gli elementi restanti.
    ''' </summary>
    Public Class RisultatoOperazioneMultipla
        Public Property Completati As Integer = 0
        Public Property Errori As New List(Of Tuple(Of String, String))()
        Public ReadOnly Property TuttoOk As Boolean
            Get
                Return Errori.Count = 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Calcola, per un elenco di percorsi remoti (file e/o cartelle), la dimensione totale
    ''' in byte di ciascuno (per le cartelle e' la somma ricorsiva dei file contenuti) e il
    ''' totale complessivo. Serve a preparare un progresso unico su un'intera operazione
    ''' multipla prima di eseguirla.
    ''' </summary>
    Private Function CalcolaDimensioniPerOperazioneMultipla(sessione As SessioneFtp, percorsi As List(Of String), ByRef byteTotali As Long) As Dictionary(Of String, Long)
        Dim dimensioni As New Dictionary(Of String, Long)()
        byteTotali = 0
        For Each percorso In percorsi
            Try
                Dim info As InfoElementoFtp = OttieniInfoElemento(sessione, percorso)
                If info IsNot Nothing AndAlso info.EDirectory Then
                    Dim elementi = ElencaFileECartelleRicorsivo(sessione, NormalizzaPercorsoRemoto(percorso))
                    Dim somma As Long = 0
                    For Each elemento In elementi
                        If Not elemento.EDirectory Then somma += Math.Max(0L, elemento.Dimensione)
                    Next
                    dimensioni(percorso) = somma
                Else
                    dimensioni(percorso) = Math.Max(0L, OttieniDimensioneFileFtp(sessione, percorso))
                End If
            Catch exDimensione As Exception
                ScriviLog("CLSFtp.CalcolaDimensioniPerOperazioneMultipla - Errore calcolo dimensione " & percorso, exDimensione)
                dimensioni(percorso) = 0
            End Try
            byteTotali += dimensioni(percorso)
        Next
        Return dimensioni
    End Function

    ''' <summary>
    ''' Copia piu' elementi (file e/o cartelle, anche misti) all'interno dello stesso
    ''' server, tutti nella stessa cartella di destinazione. Il progresso passato al
    ''' callback e' complessivo su tutti i byte di tutti gli elementi insieme.
    ''' </summary>
    Public Function CopiaMultipliRemoti(sessione As SessioneFtp, percorsiOrigine As List(Of String), cartellaDestinazione As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As RisultatoOperazioneMultipla
        Dim risultato As New RisultatoOperazioneMultipla()
        Try
            If Not SessionePronta(sessione) Then
                risultato.Errori.Add(Tuple.Create("", "Sessione non valida o non connessa."))
                Return risultato
            End If
            Dim destinazioneBase As String = NormalizzaPercorsoRemoto(cartellaDestinazione)
            Dim byteTotali As Long = 0
            Dim dimensioni As Dictionary(Of String, Long) = CalcolaDimensioniPerOperazioneMultipla(sessione, percorsiOrigine, byteTotali)

            Dim byteCompletati As Long = 0
            For Each percorsoOrigine In percorsiOrigine
                If token.IsCancellationRequested Then
                    risultato.Errori.Add(Tuple.Create(percorsoOrigine, "Operazione annullata."))
                    Continue For
                End If
                Dim destinazione As String = CombinaPercorsoRemoto(destinazioneBase, NomeElementoRemoto(NormalizzaPercorsoRemoto(percorsoOrigine)))
                Dim byteIniziali As Long = byteCompletati
                Dim callbackElemento As Action(Of Long, Long) = Sub(trasferiti As Long, totali As Long)
                                                                    Try
                                                                        If callbackProgresso IsNot Nothing Then
                                                                            callbackProgresso(byteIniziali + trasferiti, byteTotali)
                                                                        End If
                                                                    Catch exProgresso As Exception
                                                                        ScriviLog("CLSFtp.CopiaMultipliRemoti - Errore callback progresso", exProgresso)
                                                                    End Try
                                                                End Sub

                Dim errore As String = ""
                Dim info As InfoElementoFtp = Nothing
                Try
                    info = OttieniInfoElemento(sessione, percorsoOrigine)
                Catch exInfo As Exception
                    ScriviLog("CLSFtp.CopiaMultipliRemoti - Errore lettura informazioni elemento", exInfo)
                End Try

                Dim esito As Boolean
                If info IsNot Nothing AndAlso info.EDirectory Then
                    esito = CopiaCartellaRemota(sessione, percorsoOrigine, destinazione, errore, callbackElemento, token)
                Else
                    esito = CopiaFileRemoto(sessione, percorsoOrigine, destinazione, errore, callbackElemento, token)
                End If

                If esito Then
                    risultato.Completati += 1
                Else
                    risultato.Errori.Add(Tuple.Create(percorsoOrigine, errore))
                End If

                byteCompletati += If(dimensioni.ContainsKey(percorsoOrigine), dimensioni(percorsoOrigine), 0L)
            Next
        Catch ex As Exception
            ScriviLog("CLSFtp.CopiaMultipliRemoti - Errore generale", ex)
            risultato.Errori.Add(Tuple.Create("", If(String.IsNullOrWhiteSpace(ex.Message), "Errore durante la copia multipla.", ex.Message)))
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Sposta piu' elementi (file e/o cartelle, anche misti) all'interno dello stesso
    ''' server, tutti nella stessa cartella di destinazione. Per ciascun elemento prova
    ''' prima la rinomina diretta e ripiega su copia+eliminazione solo se necessario (vedi
    ''' SpostaFileRemotoConFallback e SpostaCartellaRemota).
    ''' </summary>
    Public Function SpostaMultipliRemoti(sessione As SessioneFtp, percorsiOrigine As List(Of String), cartellaDestinazione As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken) As RisultatoOperazioneMultipla
        Dim risultato As New RisultatoOperazioneMultipla()
        Try
            If Not SessionePronta(sessione) Then
                risultato.Errori.Add(Tuple.Create("", "Sessione non valida o non connessa."))
                Return risultato
            End If
            Dim destinazioneBase As String = NormalizzaPercorsoRemoto(cartellaDestinazione)
            Dim byteTotali As Long = 0
            Dim dimensioni As Dictionary(Of String, Long) = CalcolaDimensioniPerOperazioneMultipla(sessione, percorsiOrigine, byteTotali)

            Dim byteCompletati As Long = 0
            For Each percorsoOrigine In percorsiOrigine
                If token.IsCancellationRequested Then
                    risultato.Errori.Add(Tuple.Create(percorsoOrigine, "Operazione annullata."))
                    Continue For
                End If
                Dim destinazione As String = CombinaPercorsoRemoto(destinazioneBase, NomeElementoRemoto(NormalizzaPercorsoRemoto(percorsoOrigine)))
                Dim byteIniziali As Long = byteCompletati
                Dim callbackElemento As Action(Of Long, Long) = Sub(trasferiti As Long, totali As Long)
                                                                    Try
                                                                        If callbackProgresso IsNot Nothing Then
                                                                            callbackProgresso(byteIniziali + trasferiti, byteTotali)
                                                                        End If
                                                                    Catch exProgresso As Exception
                                                                        ScriviLog("CLSFtp.SpostaMultipliRemoti - Errore callback progresso", exProgresso)
                                                                    End Try
                                                                End Sub

                Dim errore As String = ""
                Dim info As InfoElementoFtp = Nothing
                Try
                    info = OttieniInfoElemento(sessione, percorsoOrigine)
                Catch exInfo As Exception
                    ScriviLog("CLSFtp.SpostaMultipliRemoti - Errore lettura informazioni elemento", exInfo)
                End Try

                Dim esito As Boolean
                If info IsNot Nothing AndAlso info.EDirectory Then
                    esito = SpostaCartellaRemota(sessione, percorsoOrigine, destinazione, errore, callbackElemento, token)
                Else
                    esito = SpostaFileRemotoConFallback(sessione, percorsoOrigine, destinazione, errore, callbackElemento, token)
                End If

                If esito Then
                    risultato.Completati += 1
                Else
                    risultato.Errori.Add(Tuple.Create(percorsoOrigine, errore))
                End If

                byteCompletati += If(dimensioni.ContainsKey(percorsoOrigine), dimensioni(percorsoOrigine), 0L)
            Next
        Catch ex As Exception
            ScriviLog("CLSFtp.SpostaMultipliRemoti - Errore generale", ex)
            risultato.Errori.Add(Tuple.Create("", If(String.IsNullOrWhiteSpace(ex.Message), "Errore durante lo spostamento multiplo.", ex.Message)))
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Elimina piu' elementi (file e/o cartelle, anche misti) in un colpo solo. Le cartelle
    ''' vengono eliminate ricorsivamente con tutto il contenuto.
    ''' </summary>
    Public Function EliminaMultipliRemoti(sessione As SessioneFtp, percorsi As List(Of String)) As RisultatoOperazioneMultipla
        Dim risultato As New RisultatoOperazioneMultipla()
        Try
            If Not SessionePronta(sessione) Then
                risultato.Errori.Add(Tuple.Create("", "Sessione non valida o non connessa."))
                Return risultato
            End If
            For Each percorso In percorsi
                Dim info As InfoElementoFtp = Nothing
                Try
                    info = OttieniInfoElemento(sessione, percorso)
                Catch exInfo As Exception
                    ScriviLog("CLSFtp.EliminaMultipliRemoti - Errore lettura informazioni elemento", exInfo)
                End Try

                Dim esito As Boolean
                If info IsNot Nothing AndAlso info.EDirectory Then
                    esito = EliminaCartellaFtpRicorsiva(sessione, percorso)
                Else
                    esito = EliminaFileFtp(sessione, percorso)
                End If

                If esito Then
                    risultato.Completati += 1
                Else
                    risultato.Errori.Add(Tuple.Create(percorso, "Eliminazione non riuscita."))
                End If
            Next
        Catch ex As Exception
            ScriviLog("CLSFtp.EliminaMultipliRemoti - Errore generale", ex)
            risultato.Errori.Add(Tuple.Create("", If(String.IsNullOrWhiteSpace(ex.Message), "Errore durante l'eliminazione multipla.", ex.Message)))
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Scarica un file dal server nella posizione locale indicata. Con riprendi a True, se
    ''' esiste gia' un file locale parziale il trasferimento riparte dal punto raggiunto
    ''' invece di ricominciare da capo. Il token consente di interrompere subito il
    ''' trasferimento senza aspettarne la fine.
    ''' </summary>
    Public Function ScaricaFile(sessione As SessioneFtp, percorsoRemoto As String, percorsoLocale As String, ByRef standardError As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken, riprendi As Boolean) As Boolean
        standardError = ""
        Try
            If Not SessionePronta(sessione) Then
                standardError = "Sessione non valida o non connessa."
                Return False
            End If
            If token.IsCancellationRequested Then
                standardError = "Operazione annullata."
                Return False
            End If

            Dim cartellaLocale As String = System.IO.Path.GetDirectoryName(percorsoLocale)
            If Not String.IsNullOrEmpty(cartellaLocale) AndAlso Not Directory.Exists(cartellaLocale) Then
                Directory.CreateDirectory(cartellaLocale)
            End If

            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            Dim dimensioneTotale As Long = OttieniDimensioneFileFtp(sessione, percorso)
            If dimensioneTotale < 0 Then dimensioneTotale = 0

            Dim offset As Long = 0
            If riprendi AndAlso File.Exists(percorsoLocale) Then
                Try
                    offset = New FileInfo(percorsoLocale).Length
                Catch exOffset As Exception
                    ScriviLog("CLSFtp.ScaricaFile - lettura file parziale", exOffset)
                    offset = 0
                End Try
                If dimensioneTotale > 0 AndAlso offset >= dimensioneTotale Then
                    Diagnostica($"Download gia' completo, nessun dato da trasferire: {percorso}")
                    Return True
                End If
            End If

            Diagnostica($"Download di '{percorso}' (ripresa da {offset} byte)")

            If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                SyncLock sessione.LockOggetto
                    Using flussoRemoto As Stream = sessione.ClientSftp.OpenRead(percorso)
                        If offset > 0 Then flussoRemoto.Seek(offset, SeekOrigin.Begin)
                        Dim modalita As FileMode = FileMode.Create
                        If offset > 0 Then modalita = FileMode.Append
                        Using flussoLocale As New FileStream(percorsoLocale, modalita, FileAccess.Write)
                            Dim esito As Boolean = CopiaFlussoConControllo(flussoRemoto, flussoLocale, offset, dimensioneTotale, sessione.Server.LimiteVelocitaKBs, callbackProgresso, token)
                            If Not esito Then
                                standardError = If(token.IsCancellationRequested, "Operazione annullata.", "Download non riuscito.")
                                Return False
                            End If
                            Return True
                        End Using
                    End Using
                End SyncLock
            Else
                Dim annullatoDaCallback As Boolean = False
                Dim progress As Action(Of FtpProgress) = Sub(avanzamento As FtpProgress)
                                                             If token.IsCancellationRequested Then
                                                                 annullatoDaCallback = True
                                                                 Throw New OperationCanceledException()
                                                             End If
                                                             Try
                                                                 If callbackProgresso IsNot Nothing Then
                                                                     callbackProgresso(avanzamento.TransferredBytes, dimensioneTotale)
                                                                 End If
                                                             Catch exProgresso As Exception
                                                                 ScriviLog("CLSFtp.ScaricaFile - Errore callback progresso FTP", exProgresso)
                                                             End Try
                                                         End Sub
                Dim esistenzaLocale As FtpLocalExists = FtpLocalExists.Overwrite
                If riprendi AndAlso offset > 0 Then esistenzaLocale = FtpLocalExists.Resume
                Try
                    SyncLock sessione.LockOggetto
                        Dim esito As FtpStatus = sessione.ClientFtp.DownloadFile(percorsoLocale, percorso, esistenzaLocale, FtpVerify.None, progress)
                        If esito = FtpStatus.Success Then Return True
                        If esito = FtpStatus.Skipped Then Return True
                    End SyncLock
                Catch exAnnullato As OperationCanceledException
                    standardError = "Operazione annullata."
                    ScriviLog("CLSFtp.ScaricaFile - download annullato dall'utente", exAnnullato)
                    Return False
                End Try
                If annullatoDaCallback Then
                    standardError = "Operazione annullata."
                Else
                    standardError = "Download non riuscito."
                End If
                Return False
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.ScaricaFile - Errore", ex)
            standardError = ex.Message
            Return False
        End Try
    End Function

    Public Function ScaricaFile(sessione As SessioneFtp, percorsoRemoto As String, percorsoLocale As String, ByRef standardError As String, Optional callbackProgresso As Action(Of Long, Long) = Nothing) As Boolean
        Return ScaricaFile(sessione, percorsoRemoto, percorsoLocale, standardError, callbackProgresso, CancellationToken.None, False)
    End Function

    ''' <summary>
    ''' Carica un file locale sul server. Con riprendi a True, se sul server esiste gia' un
    ''' file parziale il trasferimento continua da dove si era interrotto. Il token consente
    ''' di interrompere subito il trasferimento.
    ''' </summary>
    Public Function CaricaFile(sessione As SessioneFtp, percorsoLocale As String, percorsoRemoto As String, ByRef standardError As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken, riprendi As Boolean) As Boolean
        standardError = ""
        Try
            If Not SessionePronta(sessione) Then
                standardError = "Sessione non valida o non connessa."
                Return False
            End If
            If Not File.Exists(percorsoLocale) Then
                standardError = "File locale non trovato."
                Return False
            End If
            If token.IsCancellationRequested Then
                standardError = "Operazione annullata."
                Return False
            End If

            Dim dimensioneTotale As Long = New FileInfo(percorsoLocale).Length
            Dim percorso As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            Dim cartellaRemota As String = CartellaPadreRemota(percorso)
            If Not EPercorsoRadiceRemoto(cartellaRemota) Then
                CreaCartellaFtpRicorsiva(sessione, cartellaRemota)
            End If

            Dim offset As Long = 0
            If riprendi Then
                Try
                    If EsisteRemoto(sessione, percorso) Then
                        Dim dimensioneRemota As Long = OttieniDimensioneFileFtp(sessione, percorso)
                        If dimensioneRemota > 0 AndAlso dimensioneRemota < dimensioneTotale Then
                            offset = dimensioneRemota
                        ElseIf dimensioneRemota >= dimensioneTotale AndAlso dimensioneTotale > 0 Then
                            Diagnostica($"Upload gia' completo, nessun dato da trasferire: {percorso}")
                            Return True
                        End If
                    End If
                Catch exOffset As Exception
                    ScriviLog("CLSFtp.CaricaFile - verifica file remoto parziale", exOffset)
                    offset = 0
                End Try
            End If

            Diagnostica($"Upload di '{percorso}' (ripresa da {offset} byte)")

            If sessione.Server.Protocollo = TipoProtocolloFtp.Sftp Then
                SyncLock sessione.LockOggetto
                    Using flussoLocale As New FileStream(percorsoLocale, FileMode.Open, FileAccess.Read)
                        If offset > 0 Then flussoLocale.Seek(offset, SeekOrigin.Begin)
                        Dim modalita As FileMode = FileMode.Create
                        If offset > 0 Then modalita = FileMode.Append
                        Using flussoRemoto As Stream = sessione.ClientSftp.Open(percorso, modalita, FileAccess.Write)
                            Dim esito As Boolean = CopiaFlussoConControllo(flussoLocale, flussoRemoto, offset, dimensioneTotale, sessione.Server.LimiteVelocitaKBs, callbackProgresso, token)
                            If Not esito Then
                                standardError = If(token.IsCancellationRequested, "Operazione annullata.", "Upload non riuscito.")
                                Return False
                            End If
                            Return True
                        End Using
                    End Using
                End SyncLock
            Else
                Dim annullatoDaCallback As Boolean = False
                Dim progress As Action(Of FtpProgress) = Sub(avanzamento As FtpProgress)
                                                             If token.IsCancellationRequested Then
                                                                 annullatoDaCallback = True
                                                                 Throw New OperationCanceledException()
                                                             End If
                                                             Try
                                                                 If callbackProgresso IsNot Nothing Then
                                                                     callbackProgresso(avanzamento.TransferredBytes, dimensioneTotale)
                                                                 End If
                                                             Catch exProgresso As Exception
                                                                 ScriviLog("CLSFtp.CaricaFile - Errore callback progresso FTP", exProgresso)
                                                             End Try
                                                         End Sub
                Dim esistenzaRemota As FtpRemoteExists = FtpRemoteExists.Overwrite
                If riprendi AndAlso offset > 0 Then esistenzaRemota = FtpRemoteExists.Resume
                Try
                    SyncLock sessione.LockOggetto
                        Dim esito As FtpStatus = sessione.ClientFtp.UploadFile(percorsoLocale, percorso, esistenzaRemota, True, FtpVerify.None, progress)
                        If esito = FtpStatus.Success Then Return True
                        If esito = FtpStatus.Skipped Then Return True
                    End SyncLock
                Catch exAnnullato As OperationCanceledException
                    standardError = "Operazione annullata."
                    ScriviLog("CLSFtp.CaricaFile - upload annullato dall'utente", exAnnullato)
                    Return False
                End Try
                If annullatoDaCallback Then
                    standardError = "Operazione annullata."
                Else
                    standardError = "Upload non riuscito."
                End If
                Return False
            End If
        Catch ex As Exception
            ScriviLog("CLSFtp.CaricaFile - Errore", ex)
            standardError = ex.Message
            Return False
        End Try
    End Function

    Public Function CaricaFile(sessione As SessioneFtp, percorsoLocale As String, percorsoRemoto As String, ByRef standardError As String, Optional callbackProgresso As Action(Of Long, Long) = Nothing) As Boolean
        Return CaricaFile(sessione, percorsoLocale, percorsoRemoto, standardError, callbackProgresso, CancellationToken.None, False)
    End Function

    ''' <summary>
    ''' Scarica un'intera cartella remota, ricreando la struttura delle sottocartelle sotto
    ''' la cartella locale indicata. Il progresso e' complessivo su tutti i file, e la
    ''' funzione di conferma viene interpellata quando un file locale esiste gia': se
    ''' risponde False quel file viene saltato.
    ''' </summary>
    Public Function ScaricaCartella(sessione As SessioneFtp, percorsoRemoto As String, cartellaLocale As String, ByRef standardError As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken, riprendi As Boolean, confermaSovrascrittura As Func(Of String, Boolean)) As Boolean
        standardError = ""
        Try
            If Not SessionePronta(sessione) Then
                standardError = "Sessione non valida o non connessa."
                Return False
            End If

            Dim radiceRemota As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            Dim elementi As List(Of InfoElementoFtp) = ElencaFileECartelleRicorsivo(sessione, radiceRemota)
            Dim byteTotali As Long = 0
            For Each elemento As InfoElementoFtp In elementi
                If Not elemento.EDirectory AndAlso elemento.Dimensione > 0 Then byteTotali += elemento.Dimensione
            Next

            If Not Directory.Exists(cartellaLocale) Then Directory.CreateDirectory(cartellaLocale)

            Dim byteCompletati As Long = 0
            For Each elemento As InfoElementoFtp In elementi
                If token.IsCancellationRequested Then
                    standardError = "Operazione annullata."
                    Return False
                End If
                Dim relativo As String = PercorsoRelativoRemoto(radiceRemota, elemento.PercorsoCompleto)
                If String.IsNullOrEmpty(relativo) Then Continue For
                Dim destinazione As String = System.IO.Path.Combine(cartellaLocale, relativo.Replace("/", "\"))

                If elemento.EDirectory Then
                    If Not Directory.Exists(destinazione) Then Directory.CreateDirectory(destinazione)
                    Continue For
                End If

                If File.Exists(destinazione) AndAlso Not riprendi Then
                    Dim sovrascrivi As Boolean = True
                    If confermaSovrascrittura IsNot Nothing Then
                        Try
                            sovrascrivi = confermaSovrascrittura(destinazione)
                        Catch exConferma As Exception
                            ScriviLog("CLSFtp.ScaricaCartella - Errore richiesta conferma", exConferma)
                            sovrascrivi = False
                        End Try
                    End If
                    If Not sovrascrivi Then
                        byteCompletati += Math.Max(0L, elemento.Dimensione)
                        Continue For
                    End If
                End If

                Dim byteIniziali As Long = byteCompletati
                Dim callbackFile As Action(Of Long, Long) = Sub(trasferiti As Long, totali As Long)
                                                                Try
                                                                    If callbackProgresso IsNot Nothing Then
                                                                        callbackProgresso(byteIniziali + trasferiti, byteTotali)
                                                                    End If
                                                                Catch exProgresso As Exception
                                                                    ScriviLog("CLSFtp.ScaricaCartella - Errore callback progresso", exProgresso)
                                                                End Try
                                                            End Sub

                Dim erroreFile As String = ""
                If Not ScaricaFile(sessione, elemento.PercorsoCompleto, destinazione, erroreFile, callbackFile, token, riprendi) Then
                    standardError = erroreFile
                    Return False
                End If
                byteCompletati += Math.Max(0L, elemento.Dimensione)
            Next

            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.ScaricaCartella - Errore '{percorsoRemoto}'", ex)
            standardError = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Carica un'intera cartella locale sul server, ricreando la struttura delle
    ''' sottocartelle. La funzione di conferma viene interpellata quando un file remoto
    ''' esiste gia': se risponde False quel file viene saltato.
    ''' </summary>
    Public Function CaricaCartella(sessione As SessioneFtp, cartellaLocale As String, percorsoRemoto As String, ByRef standardError As String, callbackProgresso As Action(Of Long, Long), token As CancellationToken, riprendi As Boolean, confermaSovrascrittura As Func(Of String, Boolean)) As Boolean
        standardError = ""
        Try
            If Not SessionePronta(sessione) Then
                standardError = "Sessione non valida o non connessa."
                Return False
            End If
            If Not Directory.Exists(cartellaLocale) Then
                standardError = "Cartella locale non trovata."
                Return False
            End If

            Dim radiceRemota As String = NormalizzaPercorsoRemoto(percorsoRemoto)
            CreaCartellaFtpRicorsiva(sessione, radiceRemota)

            Dim infoCartella As New DirectoryInfo(cartellaLocale)
            Dim sottocartelle As DirectoryInfo() = infoCartella.GetDirectories("*", SearchOption.AllDirectories)
            For Each sottocartella As DirectoryInfo In sottocartelle
                If token.IsCancellationRequested Then
                    standardError = "Operazione annullata."
                    Return False
                End If
                Dim relativo As String = sottocartella.FullName.Substring(infoCartella.FullName.Length).Replace("\", "/").TrimStart("/"c)
                CreaCartellaFtpRicorsiva(sessione, CombinaPercorsoRemoto(radiceRemota, relativo))
            Next

            Dim file As FileInfo() = infoCartella.GetFiles("*", SearchOption.AllDirectories)
            Dim byteTotali As Long = 0
            For Each elemento As FileInfo In file
                byteTotali += elemento.Length
            Next

            Dim byteCompletati As Long = 0
            For Each elemento As FileInfo In file
                If token.IsCancellationRequested Then
                    standardError = "Operazione annullata."
                    Return False
                End If
                Dim relativo As String = elemento.FullName.Substring(infoCartella.FullName.Length).Replace("\", "/").TrimStart("/"c)
                Dim destinazione As String = CombinaPercorsoRemoto(radiceRemota, relativo)

                If Not riprendi AndAlso EsisteRemoto(sessione, destinazione) Then
                    Dim sovrascrivi As Boolean = True
                    If confermaSovrascrittura IsNot Nothing Then
                        Try
                            sovrascrivi = confermaSovrascrittura(destinazione)
                        Catch exConferma As Exception
                            ScriviLog("CLSFtp.CaricaCartella - Errore richiesta conferma", exConferma)
                            sovrascrivi = False
                        End Try
                    End If
                    If Not sovrascrivi Then
                        byteCompletati += elemento.Length
                        Continue For
                    End If
                End If

                Dim byteIniziali As Long = byteCompletati
                Dim callbackFile As Action(Of Long, Long) = Sub(trasferiti As Long, totali As Long)
                                                                Try
                                                                    If callbackProgresso IsNot Nothing Then
                                                                        callbackProgresso(byteIniziali + trasferiti, byteTotali)
                                                                    End If
                                                                Catch exProgresso As Exception
                                                                    ScriviLog("CLSFtp.CaricaCartella - Errore callback progresso", exProgresso)
                                                                End Try
                                                            End Sub

                Dim erroreFile As String = ""
                If Not CaricaFile(sessione, elemento.FullName, destinazione, erroreFile, callbackFile, token, riprendi) Then
                    standardError = erroreFile
                    Return False
                End If
                byteCompletati += elemento.Length
            Next

            Return True
        Catch ex As Exception
            ScriviLog($"CLSFtp.CaricaCartella - Errore '{cartellaLocale}'", ex)
            standardError = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Restituisce la parte di percorso remoto che segue la radice indicata, senza barra
    ''' iniziale. Restituisce stringa vuota se il percorso coincide con la radice.
    ''' </summary>
    Private Function PercorsoRelativoRemoto(radice As String, percorso As String) As String
        Try
            Dim r As String = NormalizzaPercorsoRemoto(radice).TrimEnd("/"c)
            Dim p As String = NormalizzaPercorsoRemoto(percorso)
            If p.Length <= r.Length Then Return ""
            If Not p.StartsWith(r, StringComparison.Ordinal) Then Return NomeElementoRemoto(p)
            Return p.Substring(r.Length).TrimStart("/"c)
        Catch ex As Exception
            ScriviLog("CLSFtp.PercorsoRelativoRemoto - Errore", ex)
            Return ""
        End Try
    End Function

End Class

''' <summary>
''' Argomenti dell'evento sollevato quando il monitor rileva un cambiamento
''' nell'elenco di file/cartelle di un percorso remoto.
''' </summary>
Public Class ElencoFtpAggiornatoEventArgs
    Inherits EventArgs

    Public Property PercorsoRemoto As String = ""
    Public Property Elenco As List(Of InfoElementoFtp)

    Public Sub New(percorsoRemoto As String, elenco As List(Of InfoElementoFtp))
        Me.PercorsoRemoto = percorsoRemoto
        Me.Elenco = elenco
    End Sub
End Class

''' <summary>
''' Argomenti dell'evento sollevato quando il monitor incontra un errore
''' durante il polling (es. connessione caduta).
''' </summary>
Public Class ErroreMonitoraggioFtpEventArgs
    Inherits EventArgs

    Public Property Messaggio As String = ""
    Public Property Eccezione As Exception

    Public Sub New(messaggio As String, eccezione As Exception)
        Me.Messaggio = messaggio
        Me.Eccezione = eccezione
    End Sub
End Class

''' <summary>
''' Monitora periodicamente (polling) una cartella remota FTP/FTPS/SFTP e solleva
''' l'evento ElencoAggiornato con il nuovo elenco solo quando rileva una differenza
''' rispetto all'ultima lettura (file/cartelle aggiunti, rimossi, rinominati o con
''' dimensione/data di modifica cambiate).
'''
''' Nota: FTP/FTPS/SFTP non offrono notifiche push native come FileSystemWatcher,
''' quindi il confronto avviene per confronto di elenchi a intervalli regolari.
''' La sessione passata viene sincronizzata tramite sessione.LockOggetto, quindi puo'
''' essere condivisa con l'uso "normale" fatto dall'interfaccia utente sullo stesso
''' thread principale, purche' anche quel codice usi ElencaFileECartelle (che ora e'
''' gia' sincronizzato) o comunque avvolga le chiamate al client in SyncLock sessione.LockOggetto.
''' </summary>
Public Class MonitorCartellaFtp
    Implements IDisposable

    Public Event ElencoAggiornato As EventHandler(Of ElencoFtpAggiornatoEventArgs)
    Public Event ErroreMonitoraggio As EventHandler(Of ErroreMonitoraggioFtpEventArgs)

    Private ReadOnly _clsFtp As CLSFtp
    Private ReadOnly _sessione As SessioneFtp
    Private ReadOnly _percorsoRemoto As String
    Private ReadOnly _ricorsivo As Boolean
    Private ReadOnly _intervalloMs As Integer

    Private ReadOnly _lockElenco As New Object()
    Private _elencoPrecedente As List(Of InfoElementoFtp)
    Private _cts As CancellationTokenSource
    Private _task As Task
    Private _inEsecuzione As Boolean = False

    Public ReadOnly Property InEsecuzione As Boolean
        Get
            Return _inEsecuzione
        End Get
    End Property

    ''' <param name="clsFtp">Istanza di CLSFtp da usare per interrogare il server.</param>
    ''' <param name="sessione">Sessione FTP/FTPS/SFTP gia' aperta e connessa.</param>
    ''' <param name="percorsoRemoto">Cartella remota da monitorare.</param>
    ''' <param name="intervalloMs">Intervallo di polling in millisecondi (default 5000).</param>
    ''' <param name="ricorsivo">Se True confronta anche il contenuto delle sottocartelle.</param>
    Public Sub New(clsFtp As CLSFtp, sessione As SessioneFtp, percorsoRemoto As String, Optional intervalloMs As Integer = 5000, Optional ricorsivo As Boolean = False)
        _clsFtp = clsFtp
        _sessione = sessione
        _percorsoRemoto = percorsoRemoto
        _intervalloMs = Math.Max(intervalloMs, 1000)
        _ricorsivo = ricorsivo
    End Sub

    ''' <summary>
    ''' Avvia il polling in background. Se e' gia' in esecuzione non fa nulla.
    ''' </summary>
    Public Sub Avvia()
        Try
            If _inEsecuzione Then Return
            _cts = New CancellationTokenSource()
            _inEsecuzione = True
            Dim token As CancellationToken = _cts.Token
            _task = Task.Run(Sub() CicloMonitoraggio(token), token)
        Catch ex As Exception
            _inEsecuzione = False
            CLSFtp.ScriviLog("MonitorCartellaFtp.Avvia - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Ferma il polling. Attende al massimo 3 secondi il termine del ciclo in corso.
    ''' </summary>
    Public Sub Ferma()
        Try
            If Not _inEsecuzione Then Return
            _cts?.Cancel()
            Try
                Dim daAttendere As Task = _task
                Dim idCorrente As Integer = -1
                If Task.CurrentId.HasValue Then idCorrente = Task.CurrentId.Value
                If daAttendere IsNot Nothing AndAlso daAttendere.Id <> idCorrente Then
                    daAttendere.Wait(3000)
                End If
            Catch ex As AggregateException
                CLSFtp.ScriviLog("MonitorCartellaFtp.Ferma - attesa interrotta", ex)
            Catch ex As Exception
                CLSFtp.ScriviLog("MonitorCartellaFtp.Ferma - attesa non riuscita", ex)
            End Try
        Catch ex As Exception
            CLSFtp.ScriviLog("MonitorCartellaFtp.Ferma - Errore", ex)
        Finally
            _inEsecuzione = False
            _task = Nothing
            If _cts IsNot Nothing Then
                _cts.Dispose()
                _cts = Nothing
            End If
        End Try
    End Sub

    ''' <summary>
    ''' Forza subito una lettura/confronto fuori dal normale ciclo di polling
    ''' (es. dopo un'operazione di upload/delete fatta dall'utente).
    ''' </summary>
    Public Sub AggiornaImmediatamente()
        Try
            EseguiLetturaEConfronto()
        Catch ex As Exception
            CLSFtp.ScriviLog("MonitorCartellaFtp.AggiornaImmediatamente - Errore", ex)
        End Try
    End Sub

    Private Sub CicloMonitoraggio(token As CancellationToken)
        Try
            While Not token.IsCancellationRequested
                EseguiLetturaEConfronto()
                Try
                    Task.Delay(_intervalloMs, token).Wait(token)
                Catch ex As OperationCanceledException
                    Exit While
                Catch ex As AggregateException
                    Exit While
                End Try
            End While
        Catch ex As Exception
            CLSFtp.ScriviLog("MonitorCartellaFtp.CicloMonitoraggio - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiLetturaEConfronto()
        Try
            If _sessione Is Nothing OrElse Not _sessione.Connessa Then Return

            Dim elencoAttuale As List(Of InfoElementoFtp) = If(
                _ricorsivo,
                _clsFtp.ElencaFileECartelleRicorsivo(_sessione, _percorsoRemoto),
                _clsFtp.ElencaFileECartelle(_sessione, _percorsoRemoto))
            If elencoAttuale Is Nothing Then Return

            Dim daNotificare As Boolean = False
            SyncLock _lockElenco
                If SonoElenchiDiversi(_elencoPrecedente, elencoAttuale) Then
                    _elencoPrecedente = elencoAttuale
                    daNotificare = True
                End If
            End SyncLock

            If daNotificare Then
                RaiseEvent ElencoAggiornato(Me, New ElencoFtpAggiornatoEventArgs(_percorsoRemoto, elencoAttuale))
            End If
        Catch ex As Exception
            CLSFtp.ScriviLog("MonitorCartellaFtp.EseguiLetturaEConfronto - Errore", ex)
            Try
                RaiseEvent ErroreMonitoraggio(Me, New ErroreMonitoraggioFtpEventArgs("Errore durante il controllo della cartella remota.", ex))
            Catch exEvento As Exception
                CLSFtp.ScriviLog("MonitorCartellaFtp.EseguiLetturaEConfronto - Errore notifica", exEvento)
            End Try
        End Try
    End Sub

    Private Function SonoElenchiDiversi(vecchio As List(Of InfoElementoFtp), nuovo As List(Of InfoElementoFtp)) As Boolean
        Try
            If nuovo Is Nothing Then Return False
            If vecchio Is Nothing Then Return True
            If vecchio.Count <> nuovo.Count Then Return True

            Dim mappaVecchio As New Dictionary(Of String, InfoElementoFtp)(StringComparer.Ordinal)
            For Each elem In vecchio
                mappaVecchio(elem.PercorsoCompleto) = elem
            Next

            For Each elemNuovo In nuovo
                Dim elemVecchio As InfoElementoFtp = Nothing
                If Not mappaVecchio.TryGetValue(elemNuovo.PercorsoCompleto, elemVecchio) Then Return True
                If elemVecchio.EDirectory <> elemNuovo.EDirectory Then Return True
                If elemVecchio.Dimensione <> elemNuovo.Dimensione Then Return True
                If elemVecchio.DataModifica <> elemNuovo.DataModifica Then Return True
            Next

            Return False
        Catch ex As Exception
            CLSFtp.ScriviLog("MonitorCartellaFtp.SonoElenchiDiversi - Errore", ex)
            Return True
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Ferma()
    End Sub
End Class