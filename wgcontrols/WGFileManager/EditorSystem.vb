Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Threading ' Necessario per accedere al thread corrente
Imports System.Windows.Forms ' Necessario per il controllo del thread della GUI
Imports Ude
Imports wgcontrols
Friend Class EditorSystem
    Private Shared log As New wgcontrols.Logger()

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
    Private Shared Function ReadFile(
        ByVal hFile As IntPtr,
        ByVal lpBuffer As IntPtr,
        ByVal nNumberOfBytesToRead As UInteger,
        ByRef lpNumberOfBytesRead As UInteger,
        ByVal lpOverlapped As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteFile(
        ByVal hFile As IntPtr,
        ByVal lpBuffer As IntPtr,
        ByVal nNumberOfBytesToWrite As UInteger,
        ByRef lpNumberOfBytesWritten As UInteger,
        ByVal lpOverlapped As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(ByVal hObject As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function MultiByteToWideChar(
        ByVal CodePage As UInteger,
        ByVal dwFlags As UInteger,
        ByVal lpMultiByteStr As IntPtr,
        ByVal cbMultiByte As Integer,
        ByVal lpWideCharStr As IntPtr,
        ByVal cchWideChar As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WideCharToMultiByte(
        ByVal CodePage As UInteger,
        ByVal dwFlags As UInteger,
        ByVal lpWideCharStr As IntPtr,
        ByVal cchWideChar As Integer,
        ByVal lpMultiByteStr As IntPtr,
        ByVal cbMultiByte As Integer,
        ByVal lpDefaultChar As IntPtr,
        ByVal lpUsedDefaultChar As IntPtr) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetFileSize(
        ByVal hFile As IntPtr,
        ByRef lpFileSizeHigh As UInteger) As UInteger
    End Function

    Private Const GENERIC_READ As UInteger = &H80000000UI
    Private Const GENERIC_WRITE As UInteger = &H40000000UI
    Private Const OPEN_EXISTING As UInteger = 3
    Private Const CREATE_ALWAYS As UInteger = 2
    Private Const FILE_SHARE_READ As UInteger = &H1
    Private Const FILE_SHARE_WRITE As UInteger = &H2
    Private Const CP_UTF8 As UInteger = 65001
    Private Const CP_ACP As UInteger = 0
    Private Const TRUNCATE_EXISTING As UInteger = 5

    Private Const WIN_GABRIEL_BASE_DIR As String = "C:\WinGabriel\"
    Private Const WIN_GABRIEL_TEMP_SUBDIR As String = "temp\"

    Private Sub MostraMessaggioErroreGUI(ByVal messaggioErrore As String)
        If Application.OpenForms.Count > 0 Then
            Dim mainForm As Form = Application.OpenForms(0)
            If mainForm.InvokeRequired Then
                mainForm.Invoke(Sub()
                                    Dim messaggio As New WGMessaggi()
                                    messaggio.messaggio(messaggioErrore)
                                    messaggio.ShowDialog()
                                End Sub)
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.messaggio(messaggioErrore)
                messaggio.ShowDialog()
            End If
        Else
            Dim messaggio As New WGMessaggi()
            messaggio.messaggio(messaggioErrore)
            messaggio.ShowDialog()
        End If
    End Sub

    Private Function DetectEncoding(ByVal buffer As Byte(), ByVal bytesRead As Integer) As UInteger
        Try
            If bytesRead >= 3 AndAlso buffer(0) = &HEF AndAlso buffer(1) = &HBB AndAlso buffer(2) = &HBF Then
                Return CP_UTF8
            ElseIf bytesRead >= 2 AndAlso buffer(0) = &HFF AndAlso buffer(1) = &HFE Then
                Return 1200
            ElseIf bytesRead >= 2 AndAlso buffer(0) = &HFE AndAlso buffer(1) = &HFF Then
                Return 1201
            ElseIf bytesRead >= 4 AndAlso buffer(0) = 0 AndAlso buffer(1) = 0 AndAlso buffer(2) = &HFE AndAlso buffer(3) = &HFF Then
                Return 12001
            ElseIf bytesRead >= 4 AndAlso buffer(0) = &HFF AndAlso buffer(1) = &HFE AndAlso buffer(2) = 0 AndAlso buffer(3) = 0 Then
                Return 12000
            Else
                Return CP_ACP
            End If
        Catch ex As ArgumentNullException
            log.ScriviLog($"EditorSystem.DetectEncoding - Errore (ArgumentNullException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore nella rilevazione della codifica: {ex.Message}")
            Return CP_ACP
        Catch ex As ArgumentOutOfRangeException
            log.ScriviLog($"EditorSystem.DetectEncoding - Errore (ArgumentOutOfRangeException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore nella rilevazione della codifica: {ex.Message}")
            Return CP_ACP
        Catch ex As ArgumentException
            log.ScriviLog($"EditorSystem.DetectEncoding - Errore (ArgumentException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore nella rilevazione della codifica: {ex.Message}")
            Return CP_ACP
        Catch ex As IndexOutOfRangeException
            log.ScriviLog($"EditorSystem.DetectEncoding - Errore (IndexOutOfRangeException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore nella rilevazione della codifica: {ex.Message}")
            Return CP_ACP
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.DetectEncoding - Errore generico: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore nella rilevazione della codifica: {ex.Message}")
            Return CP_ACP
        End Try
    End Function

    Public Function LeggiFile(ByVal percorso As String, ByRef contenuto As String) As Boolean
        Dim hFile As IntPtr = IntPtr.Zero
        contenuto = String.Empty
        Try
            If Not WGRete.EsisteFile(percorso, 8000) Then
                MostraMessaggioErroreGUI($"File non trovato o non raggiungibile: '{percorso}'.")
                Return False
            End If

            hFile = CreateFile(percorso, GENERIC_READ, FILE_SHARE_READ Or FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)
            If hFile = New IntPtr(-1) Then hFile = IntPtr.Zero
            If hFile = IntPtr.Zero Then
                Dim win32Error As Integer = Marshal.GetLastWin32Error()
                Dim errorMessage As String = New System.ComponentModel.Win32Exception(win32Error).Message
                MostraMessaggioErroreGUI($"Impossibile aprire il file '{percorso}'. Errore: {errorMessage}")
                Return False
            End If

            Dim fileSizeHigh As UInteger
            Dim fileSizeLow As UInteger = GetFileSize(hFile, fileSizeHigh)
            If fileSizeLow = &HFFFFFFFFUI AndAlso Marshal.GetLastWin32Error() <> 0 Then
                Dim win32Error As Integer = Marshal.GetLastWin32Error()
                Dim errorMessage As String = New System.ComponentModel.Win32Exception(win32Error).Message
                MostraMessaggioErroreGUI($"Impossibile determinare la dimensione del file '{percorso}'. Errore: {errorMessage}")
                Return False
            End If

            Dim fileSize As Long = CLng(fileSizeHigh) << 32 Or fileSizeLow
            If fileSize = 0 Then
                Return True
            End If

            Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(fileSize))
            Dim bytesRead As UInteger

            If Not ReadFile(hFile, buffer, CUInt(fileSize), bytesRead, IntPtr.Zero) Then
                Dim win32Error As Integer = Marshal.GetLastWin32Error()
                Dim errorMessage As String = New System.ComponentModel.Win32Exception(win32Error).Message
                Marshal.FreeHGlobal(buffer)
                MostraMessaggioErroreGUI($"Impossibile leggere il contenuto del file '{percorso}'. Errore: {errorMessage}")
                Return False
            End If

            Dim byteArray(bytesRead - 1) As Byte
            Marshal.Copy(buffer, byteArray, 0, CInt(bytesRead))
            Marshal.FreeHGlobal(buffer)

            Dim encoding As Encoding = Nothing
            Dim detector As New Ude.CharsetDetector()
            detector.Feed(byteArray, 0, byteArray.Length)
            detector.DataEnd()

            If detector.Charset IsNot Nothing Then
                Try
                    encoding = Encoding.GetEncoding(detector.Charset)
                Catch ex As ArgumentException
                    log.ScriviLog($"EditorSystem.LeggiFile - Charset '{detector.Charset}' non supportato. Fallback su UTF8. Errore: {ex.Message} (HResult: {ex.HResult})")
                    MostraMessaggioErroreGUI($"Charset '{detector.Charset}' non supportato per il file '{percorso}'. Verrà usato UTF8 come fallback. Errore: {ex.Message}")
                    encoding = Nothing
                Catch ex As Exception
                    log.ScriviLog($"EditorSystem.LeggiFile - Errore nel recupero dell'encoding dal CharsetDetector: {ex.Message} (HResult: {ex.HResult})")
                    MostraMessaggioErroreGUI($"Errore nel rilevare l'encoding del file '{percorso}'. Errore: {ex.Message}")
                    encoding = Nothing
                End Try
            End If

            If encoding Is Nothing Then
                Dim fallbackEncodings As Encoding() = New Encoding() {
                    Encoding.GetEncoding(1252),
                    Encoding.Default,
                    Encoding.UTF8,
                    Encoding.ASCII
                }

                For Each enc As Encoding In fallbackEncodings
                    Try
                        Dim test As String = enc.GetString(byteArray)
                        If test.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(test) Then
                            encoding = enc
                            Exit For
                        End If
                    Catch ex As Exception
                    End Try
                Next
            End If

            If encoding Is Nothing Then
                encoding = Encoding.UTF8
            End If

            contenuto = encoding.GetString(byteArray)
            Return True
        Catch ex As FileNotFoundException
            log.ScriviLog($"EditorSystem.LeggiFile - File non trovato: '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"File non trovato: '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As DirectoryNotFoundException
            log.ScriviLog($"EditorSystem.LeggiFile - Directory non trovata per il percorso: '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Directory non trovata per il percorso: '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As UnauthorizedAccessException
            log.ScriviLog($"EditorSystem.LeggiFile - Accesso negato al file '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Accesso negato al file '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As PathTooLongException
            log.ScriviLog($"EditorSystem.LeggiFile - Il percorso del file è troppo lungo: '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Il percorso del file è troppo lungo: '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As NotSupportedException
            log.ScriviLog($"EditorSystem.LeggiFile - Operazione non supportata per il percorso: '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Operazione non supportata per il percorso: '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As Security.SecurityException
            log.ScriviLog($"EditorSystem.LeggiFile - Errore di sicurezza durante l'accesso al file '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore di sicurezza durante l'accesso al file '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As ArgumentException
            log.ScriviLog($"EditorSystem.LeggiFile - Argomento non valido per il percorso: '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Argomento non valido per il percorso: '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As System.ComponentModel.Win32Exception
            log.ScriviLog($"EditorSystem.LeggiFile - Errore Win32 durante la lettura del file '{percorso}': {ex.Message} (Codice: {ex.NativeErrorCode})")
            MostraMessaggioErroreGUI($"Errore Win32 durante la lettura del file '{percorso}': {ex.Message} (Codice: {ex.NativeErrorCode})")
            Return False
        Catch ex As OutOfMemoryException
            log.ScriviLog($"EditorSystem.LeggiFile - Memoria insufficiente durante la lettura del file '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Memoria insufficiente durante la lettura del file '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As IOException
            log.ScriviLog($"EditorSystem.LeggiFile - Errore I/O durante la lettura del file '{percorso}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore I/O durante la lettura del file '{percorso}'. Errore: {ex.Message}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.LeggiFile - Errore generico durante la lettura del file '{percorso}': {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore generico durante la lettura del file '{percorso}': {ex.Message}")
            Return False
        Finally
            If hFile <> IntPtr.Zero Then
                CloseHandle(hFile)
            End If
        End Try
    End Function

    Public Function ScriviFile(ByVal percorso As String, ByVal contenuto As String) As Boolean
        Dim success As Boolean = False
        Dim lastException As Exception = Nothing
        Dim currentAttemptDescription As String = ""

        Try
            currentAttemptDescription = "Scrittura tramite API Win32"
            Dim hFile As IntPtr = IntPtr.Zero
            Try
                If Not WGRete.EsisteFile(percorso, 8000) Then
                    hFile = CreateFile(percorso, GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, CREATE_ALWAYS, 0, IntPtr.Zero)
                Else
                    hFile = CreateFile(percorso, GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, TRUNCATE_EXISTING, 0, IntPtr.Zero)
                    If hFile = New IntPtr(-1) Then hFile = IntPtr.Zero
                    If hFile = IntPtr.Zero Then
                        hFile = CreateFile(percorso, GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)
                    End If
                End If
                If hFile = New IntPtr(-1) Then hFile = IntPtr.Zero

                If hFile = IntPtr.Zero Then
                    Dim win32Error As Integer = Marshal.GetLastWin32Error()
                    Dim errorMessage As String = New System.ComponentModel.Win32Exception(win32Error).Message
                    Throw New System.ComponentModel.Win32Exception(win32Error, $"CreateFile API ha fallito: {errorMessage}")
                End If

                Dim wideCharBuffer As IntPtr = Marshal.StringToHGlobalUni(contenuto)
                Dim byteBuffer As IntPtr = IntPtr.Zero

                Try
                    Dim byteLength As Integer = WideCharToMultiByte(CP_UTF8, 0, wideCharBuffer, -1, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero)
                    If byteLength = 0 Then Throw New System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WideCharToMultiByte (ottenimento dimensione) ha fallito.")

                    byteBuffer = Marshal.AllocHGlobal(byteLength)
                    Dim actualByteLength As Integer = WideCharToMultiByte(CP_UTF8, 0, wideCharBuffer, -1, byteBuffer, byteLength, IntPtr.Zero, IntPtr.Zero)
                    If actualByteLength = 0 Then Throw New System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WideCharToMultiByte (conversione) ha fallito.")

                    Dim bytesWritten As UInteger
                    Dim byteDaScrivere As Integer = actualByteLength - 1
                    If byteDaScrivere > 0 Then
                        If Not WriteFile(hFile, byteBuffer, CUInt(byteDaScrivere), bytesWritten, IntPtr.Zero) Then
                            Throw New System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WriteFile API ha fallito.")
                        End If
                    End If
                    success = True
                Finally
                    If wideCharBuffer <> IntPtr.Zero Then Marshal.FreeHGlobal(wideCharBuffer)
                    If byteBuffer <> IntPtr.Zero Then Marshal.FreeHGlobal(byteBuffer)
                End Try
            Finally
                If hFile <> IntPtr.Zero Then CloseHandle(hFile)
            End Try
        Catch ex As UnauthorizedAccessException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Accesso negato: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As PathTooLongException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Percorso troppo lungo: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As DirectoryNotFoundException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Directory non trovata: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As IOException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore I/O: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As System.ComponentModel.Win32Exception
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore Win32: {ex.Message} (Codice: {ex.NativeErrorCode})")
        Catch ex As Security.SecurityException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore di sicurezza: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As ArgumentException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Argomento non valido: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As OutOfMemoryException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Memoria insufficiente: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Exception
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore generico: {ex.Message} (HResult: {ex.HResult})")
        End Try

        If success Then Return True

        Try
            currentAttemptDescription = "Scrittura tramite StreamWriter"
            Using sw As New StreamWriter(percorso, False, Encoding.UTF8)
                sw.Write(contenuto)
            End Using
            success = True
        Catch ex As UnauthorizedAccessException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Accesso negato: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As PathTooLongException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Percorso troppo lungo: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As DirectoryNotFoundException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Directory non trovata: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As IOException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore I/O: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Security.SecurityException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore di sicurezza: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As ArgumentException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Argomento non valido: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As OutOfMemoryException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Memoria insufficiente: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Exception
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore generico: {ex.Message} (HResult: {ex.HResult})")
        End Try

        If success Then Return True

        Try
            currentAttemptDescription = "Scrittura tramite My.Computer.FileSystem.WriteAllText"
            My.Computer.FileSystem.WriteAllText(percorso, contenuto, False, Encoding.UTF8)
            success = True
        Catch ex As UnauthorizedAccessException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Accesso negato: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As PathTooLongException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Percorso troppo lungo: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As DirectoryNotFoundException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Directory non trovata: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As IOException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore I/O: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Security.SecurityException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore di sicurezza: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As ArgumentException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Argomento non valido: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As OutOfMemoryException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Memoria insufficiente: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Exception
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore generico: {ex.Message} (HResult: {ex.HResult})")
        End Try

        If success Then Return True

        Try
            currentAttemptDescription = "Scrittura tramite File.WriteAllText o File.Open"
            If Not WGRete.EsisteFile(percorso, 8000) Then
                File.WriteAllText(percorso, contenuto, Encoding.UTF8)
            Else
                Using fs As FileStream = File.Open(percorso, FileMode.Truncate, FileAccess.Write)
                    Using sw As New StreamWriter(fs, Encoding.UTF8)
                        sw.Write(contenuto)
                    End Using
                End Using
            End If
            success = True
        Catch ex As UnauthorizedAccessException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Accesso negato: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As PathTooLongException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Percorso troppo lungo: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As DirectoryNotFoundException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Directory non trovata: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As IOException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore I/O: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Security.SecurityException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore di sicurezza: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As ArgumentException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Argomento non valido: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As OutOfMemoryException
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Memoria insufficiente: {ex.Message} (HResult: {ex.HResult})")
        Catch ex As Exception
            lastException = ex
            log.ScriviLog($"EditorSystem.ScriviFile - Tentativo fallito: {currentAttemptDescription}. Errore generico: {ex.Message} (HResult: {ex.HResult})")
        End Try

        If success Then Return True

        If Not success Then
            Dim dettaglio As String = If(lastException IsNot Nothing, lastException.Message, "sconosciuto")
            If TypeOf lastException Is UnauthorizedAccessException OrElse TypeOf lastException Is Security.SecurityException Then
                MostraMessaggioErroreGUI($"Il file '{percorso}' è protetto e WinGabriel non ha il permesso di modificarlo. Per salvarlo avvia WinGabriel come amministratore.")
            Else
                MostraMessaggioErroreGUI($"Errore fatale: Impossibile scrivere il file '{percorso}'. Ultimo errore: {dettaglio}")
            End If
        End If
        Return success
    End Function

    Private Function DetectEncodingFromFile(ByVal filePath As String, ByRef detectedEncoding As Encoding) As Boolean
        detectedEncoding = Nothing
        Try
            If Not WGRete.EsisteFile(filePath, 8000) Then
                MostraMessaggioErroreGUI($"File non trovato per la rilevazione della codifica: '{filePath}'.")
                detectedEncoding = Encoding.UTF8
                Return False
            End If

            Dim detector As New CharsetDetector()
            Using fs As FileStream = File.OpenRead(filePath)
                detector.Feed(fs)
                detector.DataEnd()
            End Using

            If detector.Charset IsNot Nothing Then
                Try
                    detectedEncoding = Encoding.GetEncoding(detector.Charset)
                    Return True
                Catch ex As ArgumentException
                    log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Charset '{detector.Charset}' non supportato. Fallback su UTF8. Errore: {ex.Message} (HResult: {ex.HResult})")
                    MostraMessaggioErroreGUI($"Charset '{detector.Charset}' non supportato per il file '{filePath}'. Verrà usato UTF8 come fallback. Errore: {ex.Message}")
                    detectedEncoding = Encoding.UTF8
                    Return False
                Catch ex As Exception
                    log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Errore nel recupero dell'encoding dal CharsetDetector: {ex.Message} (HResult: {ex.HResult})")
                    MostraMessaggioErroreGUI($"Errore nel rilevare l'encoding dal file '{filePath}'. Errore: {ex.Message}")
                    detectedEncoding = Encoding.UTF8
                    Return False
                End Try
            Else
                detectedEncoding = Encoding.UTF8
                Return False
            End If
        Catch ex As FileNotFoundException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - File non trovato: '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"File non trovato per la rilevazione della codifica: '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As DirectoryNotFoundException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Directory non trovata per il percorso: '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Directory non trovata per il percorso: '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As UnauthorizedAccessException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Accesso negato al file '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Accesso negato al file '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As PathTooLongException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Il percorso del file è troppo lungo: '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Il percorso del file è troppo lungo: '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As NotSupportedException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Operazione non supportata per il percorso: '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Operazione non supportata per il percorso: '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As Security.SecurityException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Errore di sicurezza durante l'accesso al file '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore di sicurezza durante l'accesso al file '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As ArgumentException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Argomento non valido per il percorso: '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Argomento non valido per il percorso: '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As IOException
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Errore I/O durante il rilevamento encoding del file '{filePath}'. Errore: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore I/O durante il rilevamento encoding del file '{filePath}'. Errore: {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.DetectEncodingFromFile - Errore generico durante il rilevamento encoding del file '{filePath}': {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore generico durante il rilevamento encoding del file '{filePath}': {ex.Message}")
            detectedEncoding = Encoding.UTF8
            Return False
        End Try
    End Function

    Private Function ConvertToUtf16(ByVal text As String, ByVal encoding As Encoding, ByRef convertedText As String) As Boolean
        convertedText = text
        Try
            If encoding Is Encoding.Unicode Then
                Return True
            End If
            Dim bytes As Byte() = encoding.GetBytes(text)
            convertedText = Encoding.Unicode.GetString(Encoding.Convert(encoding, Encoding.Unicode, bytes))
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"EditorSystem.ConvertToUtf16 - Errore (ArgumentNullException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a UTF-16: {ex.Message}")
            Return False
        Catch ex As ArgumentException
            log.ScriviLog($"EditorSystem.ConvertToUtf16 - Errore (ArgumentException) durante la conversione a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a UTF-16: {ex.Message}")
            Return False
        Catch ex As NotSupportedException
            log.ScriviLog($"EditorSystem.ConvertToUtf16 - Errore (NotSupportedException) durante la conversione a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a UTF-16: {ex.Message}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.ConvertToUtf16 - Errore generico durante la conversione a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a UTF-16: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function ConvertUtf8ToUtf16(ByVal utf8Text As String, ByRef convertedText As String) As Boolean
        convertedText = utf8Text
        Try
            Dim utf8Bytes As Byte() = Encoding.UTF8.GetBytes(utf8Text)
            convertedText = Encoding.Unicode.GetString(Encoding.Convert(Encoding.UTF8, Encoding.Unicode, utf8Bytes))
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"EditorSystem.ConvertUtf8ToUtf16 - Errore (ArgumentNullException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da UTF8 a UTF-16: {ex.Message}")
            Return False
        Catch ex As ArgumentException
            log.ScriviLog($"EditorSystem.ConvertUtf8ToUtf16 - Errore (ArgumentException) durante la conversione da UTF8 a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da UTF8 a UTF-16: {ex.Message}")
            Return False
        Catch ex As NotSupportedException
            log.ScriviLog($"EditorSystem.ConvertUtf8ToUtf16 - Errore (NotSupportedException) durante la conversione da UTF8 a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da UTF8 a UTF-16: {ex.Message}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.ConvertUtf8ToUtf16 - Errore generico durante la conversione da UTF8 a UTF-16: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da UTF8 a UTF-16: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function ConvertToRtf(ByVal text As String, ByRef rtfText As String) As Boolean
        rtfText = ""
        Try
            Dim utf16Text As String = ""
            If Not ConvertUtf8ToUtf16(text, utf16Text) Then
                MostraMessaggioErroreGUI("Impossibile convertire il testo in UTF-16 per la conversione RTF.")
                Return False
            End If

            Dim sb As New StringBuilder()
            sb.Append("{\rtf1\ansi ")
            For Each c As Char In utf16Text
                If AscW(c) <= &H7F Then
                    sb.Append(c)
                Else
                    sb.Append("\u" & Convert.ToUInt32(c) & "?")
                End If
            Next
            sb.Append("}")
            rtfText = sb.ToString()
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"EditorSystem.ConvertToRtf - Errore (ArgumentNullException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a RTF: {ex.Message}")
            Return False
        Catch ex As OutOfMemoryException
            log.ScriviLog($"EditorSystem.ConvertToRtf - Errore (OutOfMemoryException) durante la conversione a RTF: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a RTF: {ex.Message}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.ConvertToRtf - Errore generico durante la conversione a RTF: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione a RTF: {ex.Message}")
            Return False
        End Try
    End Function

    Private Function ConvertFromRtf(ByVal rtf As String, ByRef plainText As String) As Boolean
        plainText = ""
        Try
            Dim sb As New StringBuilder()
            Dim inControlWord As Boolean = False
            Dim unicodeValue As Integer = 0
            Dim controlWord As String = ""

            Dim i As Integer
            For i = 0 To rtf.Length - 1
                Dim c As Char = rtf(i)
                If c = "\"c Then
                    inControlWord = True
                    unicodeValue = 0
                    controlWord = ""
                ElseIf inControlWord Then
                    If Char.IsLetterOrDigit(c) OrElse c = "-"c Then
                        controlWord &= c
                    Else
                        inControlWord = False
                        If controlWord.StartsWith("u") Then
                            If Integer.TryParse(controlWord.Substring(1), unicodeValue) Then
                                sb.Append(ChrW(unicodeValue))
                            Else
                                sb.Append("\" & controlWord & c)
                            End If
                        Else
                            sb.Append("\" & controlWord & c)
                        End If
                    End If
                Else
                    sb.Append(c)
                End If
            Next
            plainText = sb.ToString()
            Return True
        Catch ex As ArgumentNullException
            log.ScriviLog($"EditorSystem.ConvertFromRtf - Errore (ArgumentNullException): {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da RTF: {ex.Message}")
            Return False
        Catch ex As FormatException
            log.ScriviLog($"EditorSystem.ConvertFromRtf - Errore (FormatException) durante la conversione da RTF: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da RTF: {ex.Message}")
            Return False
        Catch ex As OverflowException
            log.ScriviLog($"EditorSystem.ConvertFromRtf - Errore (OverflowException) durante la conversione da RTF: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da RTF: {ex.Message}")
            Return False
        Catch ex As Exception
            log.ScriviLog($"EditorSystem.ConvertFromRtf - Errore generico durante la conversione da RTF: {ex.Message} (HResult: {ex.HResult})")
            MostraMessaggioErroreGUI($"Errore durante la conversione da RTF: {ex.Message}")
            Return False
        End Try
    End Function

End Class