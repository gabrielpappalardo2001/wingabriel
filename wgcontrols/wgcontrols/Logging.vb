
' ============================================================================================
' Logger - scrittura del file di log di WinGabriel
' ============================================================================================
'
' A cosa serve
'   E' il registro degli errori usato da tutti i moduli del programma. Ogni messaggio viene
'   messo in coda e scritto su disco da un solo thread di sfondo, cosi' chi chiama non resta
'   mai fermo ad aspettare il disco e due parti del programma non si sovrascrivono a vicenda.
'
' Dove finisce il file
'   dati\logs\log.txt dentro la cartella del programma. Se quella cartella non e' scrivibile
'   (installazione in Programmi senza permessi) il messaggio viene scritto nel file di riserva
'   log.txt della cartella temporanea di Windows.
'
' Come si usa
'   Private ReadOnly log As New Logger()
'   log.ScriviLog("Errore apertura archivio: " & ex.Message)
'
' Metodi pubblici
'   New         crea il registro e la cartella dei log se manca; non solleva mai eccezioni.
'   ScriviLog   accoda il messaggio e ritorna subito. La riga viene scritta con data e ora
'               davanti. Se la scrittura non riesce in nessuno dei quattro modi previsti il
'               messaggio viene perso in silenzio: il log non deve mai fermare il programma.
'
' Attenzione
'   Nessun metodo solleva eccezioni e nessuno restituisce un esito: non c'e' modo di sapere se
'   la riga e' finita davvero su disco, ed e' voluto.
'   La scrittura avviene in coda al file in modo atomico (apertura in sola aggiunta), quindi
'   piu' copie di WinGabriel avviate insieme possono scrivere sullo stesso log senza rovinarlo.
'   Il messaggio viene scritto poco dopo la chiamata, non durante: se il programma viene chiuso
'   subito con Environment.Exit le ultime righe possono mancare.
' ============================================================================================
Imports System
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Collections.Concurrent
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Public Class Logger
    ' La cartella dei log dell'applicazione, per chi non conosce WGDb (nei dati locali se il programma è installato, accanto al programma se è portatile).
    Public Shared Function CartellaLogApplicazione() As String
        Try
            Return WGDb.WGPercorsiApplicazione.CartellaLog()
        Catch
            Return Path.Combine(Application.StartupPath, "logs")
        End Try
    End Function

    Private Const GENERIC_WRITE As UInteger = &H40000000
    Private Const OPEN_ALWAYS As UInteger = 4
    Private Const FILE_ATTRIBUTE_NORMAL As UInteger = &H80
    Private Const FILE_SHARE_READ As UInteger = &H1
    Private Const FILE_SHARE_WRITE As UInteger = &H2
    Private Const FILE_END As UInteger = 2
    Private Const FORMAT_MESSAGE_FROM_SYSTEM As UInteger = &H1000
    Private Const FORMAT_MESSAGE_IGNORE_INSERTS As UInteger = &H200

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CreateFile(
        ByVal lpFileName As String,
        ByVal dwDesiredAccess As UInteger,
        ByVal dwShareMode As UInteger,
        ByVal lpSecurityAttributes As IntPtr,
        ByVal dwCreationDisposition As UInteger,
        ByVal dwFlagsAndAttributes As UInteger,
        ByVal hTemplateFile As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteFile(
        ByVal hFile As IntPtr,
        ByVal lpBuffer As Byte(),
        ByVal nNumberOfBytesToWrite As UInteger,
        ByRef lpNumberOfBytesWritten As UInteger,
        ByVal lpOverlapped As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(ByVal hObject As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetFilePointer(
        hFile As IntPtr,
        lDistanceToMove As Integer,
        lpDistanceToMoveHigh As IntPtr,
        dwMoveMethod As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function FormatMessage(
        ByVal dwFlags As UInteger,
        ByVal lpSource As IntPtr,
        ByVal dwMessageId As UInteger,
        ByVal dwLanguageId As UInteger,
        ByVal lpBuffer As StringBuilder,
        ByVal nSize As Integer,
        ByVal Arguments As IntPtr) As Integer
    End Function

    Private ReadOnly logDirectory As String
    Private ReadOnly logFilePath As String
    Private ReadOnly fallbackLogPath As String
    Private ReadOnly _logQueue As New ConcurrentQueue(Of String)()
    Private _isProcessing As Integer = 0

    Public Sub New()
        Try
            logDirectory = WGDb.WGPercorsiApplicazione.CartellaLog() & "\"
        Catch
            logDirectory = Application.StartupPath & "\dati\logs\"
        End Try
        logFilePath = Path.Combine(logDirectory, "log.txt")

        CreaCartellaSeManca()
    End Sub



    Private Sub CreaCartellaSeManca()
        Try
            If Not Directory.Exists(logDirectory) Then
                Directory.CreateDirectory(logDirectory)
            End If
        Catch
        End Try
    End Sub

    Public Sub ScriviLog(messaggio As String)
        ' L'elemento UIA che non c'è più (0x80040201) non è un errore: Windows lo descrive
        ' come «un evento non è riuscito a richiamare uno dei sottoscrittori». Non si scrive.
        If String.IsNullOrEmpty(messaggio) OrElse NonEUnErrore(messaggio) Then Return
        _logQueue.Enqueue(messaggio)
        If Interlocked.CompareExchange(_isProcessing, 1, 0) = 0 Then
            Task.Run(AddressOf ProcessQueue)
        End If
    End Sub

    Private Shared Function NonEUnErrore(messaggio As String) As Boolean
        If messaggio.IndexOf("80040201", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("sottoscrittor", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("element not available", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        ' Il Bluetooth spento o non pronto (0x800710DF) e l'oggetto COM scollegato (0x800401FD)
        ' non sono guasti: la radio manca o il controllo e' gia' morto, non si scrivono nel log.
        If messaggio.IndexOf("800710df", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("non è pronto all'uso", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("800401fd", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If messaggio.IndexOf("connesso a un server", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return False
    End Function

    Private Sub ProcessQueue()
        Try
            Dim currentMessage As String
            While _logQueue.TryDequeue(currentMessage)
                ScriviMessaggioSuFile(currentMessage)
            End While
        Finally
            Interlocked.Exchange(_isProcessing, 0)
        End Try
    End Sub

    Private Sub ScriviMessaggioSuFile(messaggio As String)
        CreaCartellaSeManca()

        Try
            ScriviConApi(logFilePath, messaggio)
        Catch
            Try
                ScriviConApi(fallbackLogPath, messaggio)
            Catch
                Try
                    ScriviConStreamWriter(logFilePath, messaggio)
                Catch
                    Try
                        ScriviConStreamWriter(fallbackLogPath, messaggio)
                    Catch
                    End Try
                End Try
            End Try
        End Try
    End Sub

    Private Sub ScriviConApi(percorso As String, messaggio As String)
        Dim hFile As IntPtr = CreateFile(percorso, GENERIC_WRITE, FILE_SHARE_READ Or FILE_SHARE_WRITE, IntPtr.Zero, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero)
        If hFile = CType(-1, IntPtr) Then
            Throw New IOException("Errore apertura file", Marshal.GetLastWin32Error())
        End If

        SetFilePointer(hFile, 0, IntPtr.Zero, FILE_END)

        Dim encoding As New UTF8Encoding(False)
        Dim bytes() As Byte = encoding.GetBytes($"{DateTime.Now}: {messaggio}{vbCrLf}")
        Dim bytesWritten As UInteger = 0

        If Not WriteFile(hFile, bytes, CUInt(bytes.Length), bytesWritten, IntPtr.Zero) Then
            CloseHandle(hFile)
            Throw New IOException("Errore scrittura file", Marshal.GetLastWin32Error())
        End If

        CloseHandle(hFile)
    End Sub

    Private Sub ScriviConStreamWriter(percorso As String, messaggio As String)
        Try
            Using writer As New StreamWriter(percorso, True, Encoding.UTF8)
                writer.WriteLine($"{DateTime.Now}: {messaggio}")
            End Using
        Catch ex As Exception
            Throw New IOException("Errore scrittura con StreamWriter", ex)
        End Try
    End Sub

    Private Function GetErrorMessage(errorCode As Integer) As String
        Dim message As New StringBuilder(512)
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM Or FORMAT_MESSAGE_IGNORE_INSERTS, IntPtr.Zero, CUInt(errorCode), 0, message, message.Capacity, IntPtr.Zero)
        Return message.ToString().Trim()
    End Function
End Class
