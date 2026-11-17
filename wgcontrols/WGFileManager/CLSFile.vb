Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.IO
Imports System.Linq
Imports System.Management
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports Microsoft.Win32
Imports MediaDevices
Imports WGDati
Imports System.Threading.Tasks
Imports System.Threading
Imports System.Windows.Forms

Public Class CLSFile
    Implements IDisposable
    Public Event FileCreated(path As String)
    Public Event FileDeleted(path As String)
    Public Event FileRenamed(oldPath As String, newPath As String)
    Public Event DirectoryChanged()
    Public Event WatcherError(ex As Exception)
    Public Event AggiornamentoCartella(path As String)
    Public Event DriveAdded(path As String)
    Public Event DriveRemoved(path As String)

    Private _tagManager As New CLSTagMp3()
    Private _fsWatcher As FileSystemWatcher
    Private _driveDetector As DriveDetectorWindow

    Private Shared log As New wgcontrols.Logger()
    Private Const WPD_PREFIX As String = "WPD|"
    Public ReadOnly estensioniArchivio As String() = {".zip", ".7z", ".rar", ".tar", ".gz"}
    Public ReadOnly estensioniAudioVideo As String() = {".mp3", ".mp4", ".wav", ".wma", ".mkv", ".avi", ".flac", ".ogg", ".aac", ".m4a", ".mov", ".wmv", ".flv", ".webm", ".3gp", ".ts", ".m3u", ".m3u8", ".pls", ".mpg", ".mpeg", ".vob", ".asf", ".divx", ".m2ts", ".opus", ".alac", ".aiff", ".ape"}
    Public ReadOnly estensioniTesto As String() = {".txt", ".rtf", ".html", ".htm", ".php", ".asp", ".aspx", ".vb", ".cs", ".js", ".css", ".xml", ".json", ".md", ".log", ".ini", ".docx", ".pdf"}

    Private Shared ReadOnly FOLDERID_Desktop As Guid = New Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")
    Private Shared ReadOnly FOLDERID_Documents As Guid = New Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7")
    Private Shared ReadOnly FOLDERID_Downloads As Guid = New Guid("374DE290-123F-4565-9164-39C4925E467B")
    Private Shared ReadOnly FOLDERID_Pictures As Guid = New Guid("33E28130-4E1E-4676-835A-98395C3BC3BB")
    Private Shared ReadOnly FOLDERID_Music As Guid = New Guid("4BD8D571-6D19-48D3-BE97-422220080E43")
    Private Shared ReadOnly FOLDERID_Videos As Guid = New Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F153523")

    <DllImport("shell32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function SHGetKnownFolderPath(ByRef rfid As Guid, ByVal dwFlags As UInteger, ByVal hToken As IntPtr, ByRef pszPath As IntPtr) As Integer
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Auto)>
    Private Shared Function WNetGetConnection(ByVal lpszLocalName As String, ByVal lpszRemoteName As StringBuilder, ByRef cbRemoteName As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetDiskFreeSpaceEx(ByVal lpDirectoryName As String, ByRef lpFreeBytesAvailableToCaller As ULong, ByRef lpTotalNumberOfBytes As ULong, ByRef lpTotalNumberOfFreeBytes As ULong) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function GetDriveType(ByVal lpRootPathName As String) As UInteger
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function GetVolumeInformation(
        ByVal lpRootPathName As String,
        ByVal lpVolumeNameBuffer As StringBuilder,
        ByVal nVolumeNameSize As Integer,
        ByRef lpVolumeSerialNumber As Integer,
        ByRef lpMaximumComponentLength As Integer,
        ByRef lpFileSystemFlags As Integer,
        ByVal lpFileSystemNameBuffer As StringBuilder,
        ByVal nFileSystemNameSize As Integer) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
    Private Structure SHFILEINFO
        Public hIcon As IntPtr
        Public iIcon As Integer
        Public dwAttributes As UInteger
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=260)>
        Public szDisplayName As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=80)>
        Public szTypeName As String
    End Structure

    <DllImport("shell32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SHGetFileInfo(pszPath As String, dwFileAttributes As UInteger, ByRef psfi As SHFILEINFO, cbFileInfo As UInteger, uFlags As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Shared Function AllocateAndInitializeSid(ByRef pIdentifierAuthority As SID_IDENTIFIER_AUTHORITY, nSubAuthorityCount As Byte, nSubAuthority0 As Integer, nSubAuthority1 As Integer, nSubAuthority2 As Integer, nSubAuthority3 As Integer, nSubAuthority4 As Integer, nSubAuthority5 As Integer, nSubAuthority6 As Integer, nSubAuthority7 As Integer, ByRef pSid As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Shared Function CheckTokenMembership(TokenHandle As IntPtr, SidToCheck As IntPtr, ByRef IsMember As Boolean) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Shared Function FreeSid(pSid As IntPtr) As IntPtr
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function GetNamedSecurityInfo(pObjectName As String, ObjectType As Integer, SecurityInfo As Integer, ByRef ppsidOwner As IntPtr, ByRef ppsidGroup As IntPtr, ByRef ppDacl As IntPtr, ByRef ppSacl As IntPtr, ByRef ppSecurityDescriptor As IntPtr) As Integer
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function ConvertSidToStringSid(Sid As IntPtr, ByRef StringSid As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function LookupAccountSid(lpSystemName As String, lpSid As IntPtr, lpName As StringBuilder, ByRef cchName As Integer, lpReferencedDomainName As StringBuilder, ByRef cchReferencedDomainName As Integer, ByRef peUse As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function LocalFree(hMem As IntPtr) As IntPtr
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SID_IDENTIFIER_AUTHORITY
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=6, ArraySubType:=UnmanagedType.I1)>
        Public Value() As Byte
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CreateWaitableTimer(lpTimerAttributes As IntPtr, bManualReset As Boolean, lpTimerName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetWaitableTimer(hTimer As IntPtr, ByRef pDueTime As Long, lPeriod As Integer, pfnCompletionRoutine As IntPtr, lpArgToCompletionRoutine As IntPtr, fResume As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForSingleObject(hHandle As IntPtr, dwMilliseconds As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThread() As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function SetThreadAffinityMask(hThread As IntPtr, dwThreadAffinityMask As IntPtr) As IntPtr
    End Function

    Private Const SECURITY_NT_AUTHORITY As Integer = 5
    Private Const SECURITY_BUILTIN_DOMAIN_RID As Integer = 32
    Private Const DOMAIN_ALIAS_RID_ADMINS As Integer = 544
    Private Const SE_FILE_OBJECT As Integer = 1
    Private Const OWNER_SECURITY_INFORMATION As Integer = &H1
    Private Const SHGFI_DISPLAYNAME As UInteger = &H200

    Private Const DRIVE_UNKNOWN As UInteger = 0
    Private Const DRIVE_NO_ROOT_DIR As UInteger = 1
    Private Const DRIVE_REMOVABLE As UInteger = 2
    Private Const DRIVE_FIXED As UInteger = 3
    Private Const DRIVE_REMOTE As UInteger = 4
    Private Const DRIVE_CDROM As UInteger = 5
    Private Const DRIVE_RAMDISK As UInteger = 6

    Private _monitorThread As Thread
    Private _stopMonitor As Boolean = False
    Private _lastState As String = String.Empty

    Private _wpdMonitorThread As Thread
    Private _stopWpdMonitor As Boolean = False
    Private _disposed As Boolean = False
    Private _knownDeviceIds As New List(Of String)()

    Public Enum OrdineDiVisualizzazione
        Nome
        DataRecente
        DataMenoRecente
        DimensioneCrescente
        DimensioneDecrescente
    End Enum

    Public Class FileItem
        Public Property Nome As String
        Public Property PercorsoCompleto As String
        Public Property IsDirectory As Boolean
    End Class

    Private Shared Sub ScriviLogCompleto(messaggio As String, ex As Exception)
        Try
            If ex Is Nothing Then
                Return
            End If
            Dim stackTrace As New System.Diagnostics.StackTrace(ex, True)
            Dim frame As System.Diagnostics.StackFrame = stackTrace.GetFrame(0)
            Dim linea As Integer = 0
            Dim nomeFile As String = "N/A"
            Dim metodo As String = "N/A"

            If frame IsNot Nothing Then
                linea = frame.GetFileLineNumber()
                nomeFile = frame.GetFileName()
                metodo = frame.GetMethod()?.Name
            End If
            Dim eccezioneDettagliata As New StringBuilder()

            eccezioneDettagliata.Append($"{messaggio} - ")
            eccezioneDettagliata.Append($"Tipo: {ex.GetType().FullName} | ")
            eccezioneDettagliata.Append($"Messaggio: {ex.Message} | ")
            eccezioneDettagliata.Append($"HResult: 0x{ex.HResult:X8} | ")
            If ex.InnerException IsNot Nothing Then
                eccezioneDettagliata.Append($"InnerException: {ex.InnerException.Message} | ")
            End If
            If TypeOf ex Is Win32Exception Then
                Dim w32ex As Win32Exception = CType(ex, Win32Exception)
                eccezioneDettagliata.Append($"Codice Errore Nativo: {w32ex.NativeErrorCode} | ")
            End If
            If TypeOf ex Is COMException Then
                eccezioneDettagliata.Append($"COM Error: {CType(ex, COMException).ErrorCode} | ")
            End If
            eccezioneDettagliata.Append($"File: {nomeFile}, Linea: {linea}, Metodo: {metodo}")
            If Not String.IsNullOrEmpty(ex.StackTrace) Then
                eccezioneDettagliata.Append($" | StackTrace: {ex.StackTrace}")
            End If
            log.ScriviLog(eccezioneDettagliata.ToString())
        Catch logEx As Exception
            System.Diagnostics.Debug.WriteLine($"CLSFILE LOG FAILURE: {logEx.Message}")
        End Try
    End Sub

    Public Function IsWpdPath(path As String) As Boolean
        Try
            Return Not String.IsNullOrEmpty(path) AndAlso path.StartsWith(WPD_PREFIX)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsWpdPath", ex)
            Return False
        End Try
    End Function

    Public Function IsWpdDirectory(path As String) As Boolean
        Try
            If path.EndsWith("|\") Then Return True
            Dim parts = path.Split("|"c)
            If parts.Length < 3 Then Return True
            Dim lastPart = parts(2)
            Return String.IsNullOrEmpty(System.IO.Path.GetExtension(lastPart))
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsWpdDirectory", ex)
            Return False
        End Try
    End Function

    Public Function GetWpdFileName(path As String) As String
        Try
            Dim parts = path.Split("|"c)
            If parts.Length >= 3 Then
                Return System.IO.Path.GetFileName(parts(2))
            End If
            Return "Unknown"
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.GetWpdFileName", ex)
            Return "Unknown"
        End Try
    End Function

    Public Function IsRootPath(ByVal path As String) As Boolean
        Try
            Return path.Length = 3 AndAlso path.EndsWith(":\")
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsRootPath", ex)
            Return False
        End Try
    End Function

    Public Function IsRadiceRisorsa(ByVal path As String) As Boolean
        Try
            Return IsRootPath(path) OrElse WGRete.IsRadiceRisorsa(path)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsRadiceRisorsa", ex)
            Return False
        End Try
    End Function

    Public Function IsRemotePath(ByVal path As String) As Boolean
        Try
            Return WGRete.IsRemoto(path)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsRemotePath", ex)
            Return False
        End Try
    End Function

    Public Function GetKnownFolderPath(ByRef folderId As Guid) As String
        Dim pszPath As IntPtr = IntPtr.Zero
        Try
            Dim hr As Integer = SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, pszPath)
            If hr = 0 Then
                Return System.Runtime.InteropServices.Marshal.PtrToStringAuto(pszPath)
            ElseIf hr = -2147024894 Then
                Return String.Empty
            Else
                Return String.Empty
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.GetKnownFolderPath", ex)
            Return String.Empty
        Finally
            If pszPath <> IntPtr.Zero Then
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pszPath)
            End If
        End Try
    End Function

    Public Function OttieniDrives() As List(Of FileItem)
        Dim result As New List(Of FileItem)
        Dim logicalDrives As String()

        Try
            logicalDrives = Environment.GetLogicalDrives()
        Catch ex As IOException
            ScriviLogCompleto("CLSFile.OttieniDrives - Errore IO recupero drive logici", ex)
            logicalDrives = New String() {}
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniDrives - Errore Generico recupero drive logici", ex)
            logicalDrives = New String() {}
        End Try

        Dim addedUncPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each drivePath As String In logicalDrives
            Try
                Dim driveType As UInteger = GetDriveType(drivePath)
                If driveType = DRIVE_UNKNOWN OrElse driveType = DRIVE_NO_ROOT_DIR Then Continue For

                If driveType = DRIVE_REMOVABLE OrElse driveType = DRIVE_FIXED Then
                    If Not IsDriveReady(drivePath) Then Continue For
                End If

                If driveType = DRIVE_REMOTE AndAlso Not WGRete.Raggiungibile(drivePath, 2500) Then
                    Dim uncNonRaggiungibile As String = WGRete.RisolviUnc(drivePath)
                    Dim nomeNonRaggiungibile As String = drivePath.TrimEnd("\"c)
                    If Not String.IsNullOrEmpty(uncNonRaggiungibile) Then
                        Dim partiNonRaggiungibile As String() = uncNonRaggiungibile.Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
                        Dim nomeCondivisione As String = If(partiNonRaggiungibile.Length > 0, partiNonRaggiungibile(partiNonRaggiungibile.Length - 1), uncNonRaggiungibile)
                        nomeNonRaggiungibile = nomeCondivisione & " (" & drivePath.TrimEnd("\"c) & ")"
                    End If
                    result.Add(New FileItem With {.Nome = nomeNonRaggiungibile & " - non raggiungibile", .PercorsoCompleto = drivePath, .IsDirectory = True})
                    Continue For
                End If

                Dim displayName As String = ""
                Dim shfi As New SHFILEINFO()
                Dim ptr As IntPtr = SHGetFileInfo(drivePath, 0, shfi, CUInt(Marshal.SizeOf(shfi)), SHGFI_DISPLAYNAME)

                If ptr <> IntPtr.Zero Then
                    displayName = shfi.szDisplayName
                Else
                    Dim lastErr As Integer = Marshal.GetLastWin32Error()
                    ScriviLogCompleto($"CLSFile.OttieniDrives - SHGetFileInfo fallito per {drivePath}", New Win32Exception(lastErr))
                End If

                If String.IsNullOrWhiteSpace(displayName) Then
                    Dim volumeNameBuffer As New StringBuilder(261)
                    Dim fileSystemNameBuffer As New StringBuilder(261)
                    Dim serialNumber As Integer
                    Dim maxComponentLength As Integer
                    Dim fileSystemFlags As Integer
                    Dim gotInfo As Boolean = False

                    Try
                        gotInfo = GetVolumeInformation(drivePath, volumeNameBuffer, volumeNameBuffer.Capacity, serialNumber, maxComponentLength, fileSystemFlags, fileSystemNameBuffer, fileSystemNameBuffer.Capacity)
                        If Not gotInfo Then
                            Dim lastErr As Integer = Marshal.GetLastWin32Error()
                            ScriviLogCompleto($"CLSFile.OttieniDrives - GetVolumeInformation fallito per {drivePath}", New Win32Exception(lastErr))
                        End If
                    Catch exVol As Exception
                        ScriviLogCompleto($"CLSFile.OttieniDrives - Errore GetVolumeInformation per {drivePath}", exVol)
                    End Try

                    If driveType = DRIVE_REMOTE Then
                        Dim uncPath As New StringBuilder(255)
                        Dim size As Integer = uncPath.Capacity
                        Dim callResult As Integer = 0
                        Try
                            callResult = WNetGetConnection(drivePath.Substring(0, 2), uncPath, size)
                        Catch exNet As Exception
                            ScriviLogCompleto($"CLSFile.OttieniDrives - Errore WNetGetConnection per {drivePath}", exNet)
                            callResult = -1
                        End Try

                        If callResult = 0 Then
                            Dim uncPathString As String = uncPath.ToString()
                            If addedUncPaths.Contains(uncPathString) Then Continue For
                            addedUncPaths.Add(uncPathString)
                            Dim parts() As String = uncPathString.Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
                            Dim shareName As String = If(parts.Length > 0, parts(parts.Length - 1), uncPathString)
                            displayName = $"{shareName} ({drivePath.TrimEnd("\")})"
                        Else
                            If callResult <> -1 Then
                            End If
                            displayName = drivePath.TrimEnd("\")
                        End If
                    Else
                        Dim volumeLabel As String = volumeNameBuffer.ToString()
                        If String.IsNullOrWhiteSpace(volumeLabel) Then
                            Select Case driveType
                                Case DRIVE_FIXED
                                    volumeLabel = "Disco Locale"
                                Case DRIVE_REMOVABLE
                                    volumeLabel = "Disco Esterno"
                                Case DRIVE_CDROM
                                    volumeLabel = "Unità Ottica"
                                Case DRIVE_RAMDISK
                                    volumeLabel = "RamDisk"
                                Case Else
                                    volumeLabel = "Disco"
                            End Select
                        End If
                        displayName = $"{volumeLabel} ({drivePath.TrimEnd("\")})"
                    End If
                End If

                result.Add(New FileItem With {.Nome = displayName, .PercorsoCompleto = drivePath, .IsDirectory = True})
            Catch ex As IOException
            Catch ex As UnauthorizedAccessException
            Catch ex As Exception
                ScriviLogCompleto($"CLSFile.OttieniDrives - Errore elaborazione drive '{drivePath}'", ex)
            End Try
        Next

        Try
            Dim devices = MediaDevice.GetDevices().ToList()
            Dim wpdResults As New List(Of FileItem)()
            Dim wpdLock As New Object()
            Dim tasks As New List(Of Task)()

            For Each d In devices
                Dim deviceRef = d
                Dim task As Task = Task.Run(Sub()
                                                 Try
                                                     Dim devFriendlyName As String = ""
                                                     Dim devId As String = ""

                                                     Try
                                                         devFriendlyName = deviceRef.FriendlyName
                                                         devId = deviceRef.DeviceId
                                                     Catch comEx As COMException
                                                         Return
                                                     Catch ex As Exception
                                                         Return
                                                     End Try

                                                     If String.IsNullOrWhiteSpace(devFriendlyName) Then Return

                                                     Dim displayString As String = devFriendlyName
                                                     Dim newItem As New FileItem With {.Nome = displayString, .PercorsoCompleto = WPD_PREFIX & devId & "|\", .IsDirectory = True}

                                                     SyncLock wpdLock
                                                         wpdResults.Add(newItem)
                                                     End SyncLock

                                                 Catch ex As Exception
                                                 End Try
                                             End Sub)
                tasks.Add(task)
            Next

            Try
                Task.WhenAll(tasks).Wait(3000)
            Catch exWait As AggregateException
                ScriviLogCompleto("CLSFile.OttieniDrives - Errore attesa task WPD", exWait)
            End Try

            result.AddRange(wpdResults)

        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniDrives - Errore generale loop WPD", ex)
        End Try

        Try
            Dim desktopPath As String = GetKnownFolderPath(FOLDERID_Desktop)
            If Not String.IsNullOrEmpty(desktopPath) Then result.Add(New FileItem With {.Nome = "Desktop", .PercorsoCompleto = desktopPath, .IsDirectory = True})
            Dim documentsPath As String = GetKnownFolderPath(FOLDERID_Documents)
            If Not String.IsNullOrEmpty(documentsPath) Then result.Add(New FileItem With {.Nome = "Documenti", .PercorsoCompleto = documentsPath, .IsDirectory = True})
            Dim downloadsPath As String = GetKnownFolderPath(FOLDERID_Downloads)
            If Not String.IsNullOrEmpty(downloadsPath) Then result.Add(New FileItem With {.Nome = "Download", .PercorsoCompleto = downloadsPath, .IsDirectory = True})
            Dim picturesPath As String = GetKnownFolderPath(FOLDERID_Pictures)
            If Not String.IsNullOrEmpty(picturesPath) Then result.Add(New FileItem With {.Nome = "Immagini", .PercorsoCompleto = picturesPath, .IsDirectory = True})
            Dim musicPath As String = GetKnownFolderPath(FOLDERID_Music)
            If Not String.IsNullOrEmpty(musicPath) Then result.Add(New FileItem With {.Nome = "Musica", .PercorsoCompleto = musicPath, .IsDirectory = True})
            Dim videosPath As String = GetKnownFolderPath(FOLDERID_Videos)
            If Not String.IsNullOrEmpty(videosPath) Then result.Add(New FileItem With {.Nome = "Video", .PercorsoCompleto = videosPath, .IsDirectory = True})
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniDrives - Errore recupero cartelle note", ex)
        End Try

        Try
            Dim percorsiAggiunti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each cartellaCloud As FileItem In OttieniCartelleCloud()
                Try
                    If Not String.IsNullOrWhiteSpace(cartellaCloud.PercorsoCompleto) AndAlso percorsiAggiunti.Add(cartellaCloud.PercorsoCompleto) Then
                        result.Add(cartellaCloud)
                    End If
                Catch exVoce As Exception
                    ScriviLogCompleto("CLSFile.OttieniDrives - Errore aggiunta cartella cloud", exVoce)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniDrives - Errore recupero cartelle cloud", ex)
        End Try

        Return result
    End Function

    Private Function OttieniCartelleCloud() As List(Of FileItem)
        Dim result As New List(Of FileItem)
        Dim percorsiAggiunti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Try
            Dim oneDrivePersonale As String = Environment.GetEnvironmentVariable("OneDriveConsumer")
            If String.IsNullOrWhiteSpace(oneDrivePersonale) Then oneDrivePersonale = Environment.GetEnvironmentVariable("OneDrive")
            If Not String.IsNullOrWhiteSpace(oneDrivePersonale) AndAlso Directory.Exists(oneDrivePersonale) AndAlso percorsiAggiunti.Add(oneDrivePersonale) Then
                result.Add(New FileItem With {.Nome = "OneDrive", .PercorsoCompleto = oneDrivePersonale, .IsDirectory = True})
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore rilevamento OneDrive personale", ex)
        End Try

        Try
            Dim oneDriveAziendale As String = Environment.GetEnvironmentVariable("OneDriveCommercial")
            If Not String.IsNullOrWhiteSpace(oneDriveAziendale) AndAlso Directory.Exists(oneDriveAziendale) AndAlso percorsiAggiunti.Add(oneDriveAziendale) Then
                result.Add(New FileItem With {.Nome = "OneDrive Aziendale", .PercorsoCompleto = oneDriveAziendale, .IsDirectory = True})
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore rilevamento OneDrive aziendale", ex)
        End Try

        Try
            Using chiaveAccounts As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\OneDrive\Accounts")
                If chiaveAccounts IsNot Nothing Then
                    For Each nomeAccount As String In chiaveAccounts.GetSubKeyNames()
                        Try
                            Using chiaveAccount As RegistryKey = chiaveAccounts.OpenSubKey(nomeAccount)
                                If chiaveAccount IsNot Nothing Then
                                    Dim cartellaUtente As String = TryCast(chiaveAccount.GetValue("UserFolder"), String)
                                    If Not String.IsNullOrWhiteSpace(cartellaUtente) AndAlso Directory.Exists(cartellaUtente) AndAlso percorsiAggiunti.Add(cartellaUtente) Then
                                        Dim nomeVisualizzato As String = TryCast(chiaveAccount.GetValue("DisplayName"), String)
                                        If String.IsNullOrWhiteSpace(nomeVisualizzato) Then nomeVisualizzato = "OneDrive (" & nomeAccount & ")"
                                        result.Add(New FileItem With {.Nome = nomeVisualizzato, .PercorsoCompleto = cartellaUtente, .IsDirectory = True})
                                    End If
                                End If
                            End Using
                        Catch exAccount As Exception
                            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore lettura account OneDrive '" & nomeAccount & "'", exAccount)
                        End Try
                    Next
                End If
            End Using
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore lettura registro OneDrive", ex)
        End Try

        Try
            Dim infoDropbox As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dropbox", "info.json")
            If File.Exists(infoDropbox) Then
                Dim contenuto As String = File.ReadAllText(infoDropbox)
                Dim percorsoPersonale As String = EstraiPercorsoDropbox(contenuto, "personal")
                If Not String.IsNullOrWhiteSpace(percorsoPersonale) AndAlso Directory.Exists(percorsoPersonale) AndAlso percorsiAggiunti.Add(percorsoPersonale) Then
                    result.Add(New FileItem With {.Nome = "Dropbox", .PercorsoCompleto = percorsoPersonale, .IsDirectory = True})
                End If
                Dim percorsoAziendale As String = EstraiPercorsoDropbox(contenuto, "business")
                If Not String.IsNullOrWhiteSpace(percorsoAziendale) AndAlso Directory.Exists(percorsoAziendale) AndAlso percorsiAggiunti.Add(percorsoAziendale) Then
                    result.Add(New FileItem With {.Nome = "Dropbox Aziendale", .PercorsoCompleto = percorsoAziendale, .IsDirectory = True})
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore rilevamento Dropbox", ex)
        End Try

        Try
            Dim cartellaUtenteBase As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

            Dim googleDriveLegacy As String = Path.Combine(cartellaUtenteBase, "Google Drive")
            If Directory.Exists(googleDriveLegacy) AndAlso percorsiAggiunti.Add(googleDriveLegacy) Then
                result.Add(New FileItem With {.Nome = "Google Drive", .PercorsoCompleto = googleDriveLegacy, .IsDirectory = True})
            End If

            Dim iCloudDrive As String = Path.Combine(cartellaUtenteBase, "iCloudDrive")
            If Directory.Exists(iCloudDrive) AndAlso percorsiAggiunti.Add(iCloudDrive) Then
                result.Add(New FileItem With {.Nome = "iCloud Drive", .PercorsoCompleto = iCloudDrive, .IsDirectory = True})
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniCartelleCloud - Errore rilevamento cartelle cloud aggiuntive", ex)
        End Try

        Return result
    End Function

    Private Function EstraiPercorsoDropbox(json As String, sezione As String) As String
        Try
            Dim pattern As String = """" & sezione & """\s*:\s*\{[^}]*?""path""\s*:\s*""((?:[^""\\]|\\.)*)"""
            Dim m As Match = Regex.Match(json, pattern)
            If m.Success Then
                Return m.Groups(1).Value.Replace("\\", "\")
            End If
            Return ""
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.EstraiPercorsoDropbox - Errore", ex)
            Return ""
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
            ScriviLogCompleto("CLSFile.EstensioneCorrispondeFiltro - Errore", ex)
            Return True
        End Try
    End Function

    Public Function OttieniContenutoCartella(directory As String, filter As String, ordinamento As OrdineDiVisualizzazione, Optional cancellationToken As CancellationToken = Nothing) As List(Of FileItem)
        If IsWpdPath(directory) Then
            Return OttieniContenutoWPD(directory, filter, cancellationToken)
        Else
            SetupFileSystemWatcher(directory)
            Return OttieniContenutoLocale(directory, filter, ordinamento, cancellationToken)
        End If
    End Function

    Private Function OttieniContenutoLocale(directory As String, filter As String, ordinamento As OrdineDiVisualizzazione, cancellationToken As CancellationToken) As List(Of FileItem)
        Dim result As New List(Of FileItem)
        Try
            cancellationToken.ThrowIfCancellationRequested()

            Dim directories = System.IO.Directory.GetDirectories(directory).Select(Function(d) System.IO.Path.GetFileName(d)).ToList()
            Dim files = System.IO.Directory.GetFiles(directory).Select(Function(f) System.IO.Path.GetFileName(f)).ToList()

            cancellationToken.ThrowIfCancellationRequested()

            Select Case ordinamento
                Case OrdineDiVisualizzazione.Nome
                    directories.Sort()
                    files.Sort()
                Case OrdineDiVisualizzazione.DataRecente
                    directories = directories.OrderByDescending(Function(d) New DirectoryInfo(System.IO.Path.Combine(directory, d)).CreationTime).ToList()
                    files = files.OrderByDescending(Function(f) New FileInfo(System.IO.Path.Combine(directory, f)).CreationTime).ToList()
                Case OrdineDiVisualizzazione.DataMenoRecente
                    directories = directories.OrderBy(Function(d) New DirectoryInfo(System.IO.Path.Combine(directory, d)).CreationTime).ToList()
                    files = files.OrderBy(Function(f) New FileInfo(System.IO.Path.Combine(directory, f)).CreationTime).ToList()
                Case OrdineDiVisualizzazione.DimensioneCrescente
                    directories.Sort()
                    files = files.OrderBy(Function(f) New FileInfo(System.IO.Path.Combine(directory, f)).Length).ToList()
                Case OrdineDiVisualizzazione.DimensioneDecrescente
                    directories.Sort()
                    files = files.OrderByDescending(Function(f) New FileInfo(System.IO.Path.Combine(directory, f)).Length).ToList()
            End Select

            cancellationToken.ThrowIfCancellationRequested()

            For Each item In directories
                cancellationToken.ThrowIfCancellationRequested()
                result.Add(New FileItem With {.Nome = item, .PercorsoCompleto = System.IO.Path.Combine(directory, item), .IsDirectory = True})
            Next

            For Each item In files
                cancellationToken.ThrowIfCancellationRequested()
                Dim ext = System.IO.Path.GetExtension(item).ToLower()
                If Not String.IsNullOrEmpty(filter) Then
                    If Not EstensioneCorrispondeFiltro(ext, filter) Then Continue For
                End If
                result.Add(New FileItem With {.Nome = item, .PercorsoCompleto = System.IO.Path.Combine(directory, item), .IsDirectory = False})
            Next
        Catch ex As OperationCanceledException
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Operazione annullata: {directory}", ex)
            Throw
        Catch ex As UnauthorizedAccessException
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Accesso negato: {directory}", ex)
            Throw
        Catch ex As DirectoryNotFoundException
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Directory non trovata: {directory}", ex)
            Throw
        Catch ex As PathTooLongException
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Percorso troppo lungo: {directory}", ex)
            Throw
        Catch ex As IOException
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Errore IO: {directory}", ex)
            Throw
        Catch ex As Exception
            ScriviLogCompleto($"CLSFile.OttieniContenutoLocale - Errore generico: {directory}", ex)
            Throw
        End Try
        Return result
    End Function

    Private Function OttieniContenutoWPD(wpdPath As String, filter As String, cancellationToken As CancellationToken) As List(Of FileItem)
        Dim result As New List(Of FileItem)
        Try
            cancellationToken.ThrowIfCancellationRequested()

            Dim parts = wpdPath.Split("|"c)
            Dim deviceId = parts(1)
            Dim objectId As String = If(parts.Length > 2, parts(2), "\")

            Dim devices = MediaDevice.GetDevices()
            Dim device = devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))

            If device Is Nothing Then Return result

            Dim isConnected As Boolean = False
            Dim connectThread As New Thread(Sub()
                                                Try
                                                    device.Connect()
                                                    isConnected = device.IsConnected
                                                Catch
                                                End Try
                                            End Sub)
            connectThread.IsBackground = True
            connectThread.Start()

            Dim cronometro As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
            While connectThread.IsAlive AndAlso cronometro.ElapsedMilliseconds < 2000
                If cancellationToken.IsCancellationRequested Then Exit While
                Thread.Sleep(25)
            End While

            If cancellationToken.IsCancellationRequested Then
                Try
                    connectThread.Abort()
                Catch
                End Try
                cancellationToken.ThrowIfCancellationRequested()
            End If

            If Not isConnected Then
                Try
                    connectThread.Abort()
                Catch
                End Try
                Return result
            End If

            If Not device.IsConnected Then Return result

            Try
                Dim dirInfo As MediaDirectoryInfo = Nothing
                Try
                    dirInfo = device.GetDirectoryInfo(objectId)
                Catch comEx As COMException
                    ScriviLogCompleto($"CLSFile.OttieniContenutoWPD - Dispositivo bloccato o inaccessibile (COM Error: {comEx.ErrorCode})", comEx)
                    Return result
                Catch ex As Exception
                    ScriviLogCompleto($"CLSFile.OttieniContenutoWPD - Errore accesso root", ex)
                    Return result
                End Try

                If dirInfo Is Nothing Then Return result

                cancellationToken.ThrowIfCancellationRequested()

                Dim folders As List(Of MediaDirectoryInfo) = Nothing
                Dim files As List(Of MediaFileInfo) = Nothing

                Try
                    folders = dirInfo.EnumerateDirectories().OrderBy(Function(d) d.Name).ToList()
                    files = dirInfo.EnumerateFiles().OrderBy(Function(f) f.Name).ToList()
                Catch ex As Exception
                    ScriviLogCompleto("CLSFile.OttieniContenutoWPD - Impossibile enumerare (Dispositivo bloccato?)", ex)
                    Return result
                End Try

                For Each f In folders
                    cancellationToken.ThrowIfCancellationRequested()
                    result.Add(New FileItem With {.Nome = f.Name, .PercorsoCompleto = WPD_PREFIX & deviceId & "|" & f.FullName, .IsDirectory = True})
                Next
                For Each f In files
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim ext = System.IO.Path.GetExtension(f.Name).ToLower()
                    If Not String.IsNullOrEmpty(filter) Then
                        If Not EstensioneCorrispondeFiltro(ext, filter) Then Continue For
                    End If
                    result.Add(New FileItem With {.Nome = f.Name, .PercorsoCompleto = WPD_PREFIX & deviceId & "|" & f.FullName, .IsDirectory = False})
                Next
            Finally
                If device.IsConnected Then device.Disconnect()
            End Try
        Catch ex As OperationCanceledException
            ScriviLogCompleto($"CLSFile.OttieniContenutoWPD - Operazione annullata: {wpdPath}", ex)
            Throw
        Catch ex As Exception
            ScriviLogCompleto($"CLSFile.OttieniContenutoWPD - Errore critico: {wpdPath}", ex)
            Throw
        End Try
        Return result
    End Function

    Private _currentWatchedPath As String = String.Empty

    Public Sub SetupFileSystemWatcher(path As String)
        Try
            If _monitorThread IsNot Nothing Then
                _stopMonitor = True
                _monitorThread.Join(500)
                _monitorThread = Nothing
            End If

            If IsWpdPath(path) Then
                If _fsWatcher IsNot Nothing Then
                    _fsWatcher.Dispose()
                    _fsWatcher = Nothing
                End If
                _currentWatchedPath = path
                _stopMonitor = False
                _lastState = String.Empty
                _monitorThread = New Thread(AddressOf MonitorContenutoWpdLoop)
                _monitorThread.IsBackground = True
                _monitorThread.Start(path)
                Return
            End If

            If String.Equals(_currentWatchedPath, path, StringComparison.OrdinalIgnoreCase) AndAlso _fsWatcher IsNot Nothing Then
                Return
            End If

            If _fsWatcher IsNot Nothing Then
                _fsWatcher.Dispose()
                _fsWatcher = Nothing
            End If

            If Not String.IsNullOrEmpty(path) AndAlso WGRete.EsisteDirectory(path) Then
                _currentWatchedPath = path

                _fsWatcher = New FileSystemWatcher(path)
                _fsWatcher.NotifyFilter = NotifyFilters.FileName Or NotifyFilters.DirectoryName Or NotifyFilters.LastWrite Or NotifyFilters.Size Or NotifyFilters.Attributes Or NotifyFilters.Security
                _fsWatcher.IncludeSubdirectories = False
                _fsWatcher.InternalBufferSize = 32768

                AddHandler _fsWatcher.Created, Sub(s, e)
                                                   RaiseEvent FileCreated(e.FullPath)
                                                   RaiseEvent AggiornamentoCartella(e.FullPath)
                                               End Sub
                AddHandler _fsWatcher.Deleted, Sub(s, e)
                                                   RaiseEvent FileDeleted(e.FullPath)
                                                   RaiseEvent AggiornamentoCartella(e.FullPath)
                                               End Sub
                AddHandler _fsWatcher.Renamed, Sub(s, e)
                                                   RaiseEvent FileRenamed(e.OldFullPath, e.FullPath)
                                                   RaiseEvent AggiornamentoCartella(e.OldFullPath)
                                                   RaiseEvent AggiornamentoCartella(e.FullPath)
                                               End Sub
                AddHandler _fsWatcher.Changed, Sub(s, e)
                                                   RaiseEvent AggiornamentoCartella(e.FullPath)
                                               End Sub
                AddHandler _fsWatcher.Error, Sub(s, e)
                                                 ScriviLogCompleto("CLSFile.SetupFileSystemWatcher - Errore Buffer", e.GetException())
                                                 RicreaWatcherDopoErrore(path)
                                             End Sub
                _fsWatcher.EnableRaisingEvents = True
            End If
        Catch ex As UnauthorizedAccessException
            ScriviLogCompleto("CLSFile.SetupFileSystemWatcher - Accesso negato", ex)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.SetupFileSystemWatcher - Errore generico", ex)
        End Try
    End Sub

    Private Sub RicreaWatcherDopoErrore(path As String)
        Try
            Task.Run(Sub()
                         Try
                             Thread.Sleep(1500)
                             If _disposed Then Return
                             If Not String.Equals(_currentWatchedPath, path, StringComparison.OrdinalIgnoreCase) Then Return
                             _currentWatchedPath = String.Empty
                             SetupFileSystemWatcher(path)
                         Catch exInterno As Exception
                             ScriviLogCompleto("CLSFile.RicreaWatcherDopoErrore - Errore interno", exInterno)
                         End Try
                     End Sub)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.RicreaWatcherDopoErrore - Errore", ex)
        End Try
    End Sub

    Private Sub MonitorContenutoWpdLoop(objPath As Object)
        Dim path As String = CStr(objPath)
        Try
            If Environment.ProcessorCount > 1 Then
                SetThreadAffinityMask(GetCurrentThread(), New IntPtr(1 << 1))
            End If
        Catch
        End Try

        Dim hTimer As IntPtr = IntPtr.Zero
        Try
            hTimer = CreateWaitableTimer(IntPtr.Zero, False, Nothing)
            If hTimer = IntPtr.Zero Then
                Return
            End If

            Dim dueTime As Long = -1000000
            Dim wpdDevice As MediaDevice = Nothing

            Try
                Dim parts = path.Split("|"c)
                Dim deviceId = parts(1)
                Dim devices = MediaDevice.GetDevices()
                wpdDevice = devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                If wpdDevice IsNot Nothing Then
                    wpdDevice.Connect()
                End If
            Catch exWpd As Exception
                ScriviLogCompleto($"MonitorContenutoWpdLoop - Errore connessione WPD {path}", exWpd)
            End Try

            While Not _stopMonitor
                If SetWaitableTimer(hTimer, dueTime, 0, IntPtr.Zero, IntPtr.Zero, False) Then
                    WaitForSingleObject(hTimer, -1)
                Else
                    Thread.Sleep(100)
                End If

                If _stopMonitor Then Exit While

                Try
                    Dim currentState As String = ""

                    If wpdDevice IsNot Nothing Then
                        If Not wpdDevice.IsConnected Then
                            Try
                                wpdDevice.Connect()
                            Catch
                            End Try
                        End If

                        If wpdDevice.IsConnected Then
                            Dim parts = path.Split("|"c)
                            Dim objectId = If(parts.Length > 2, parts(2), "\")
                            Dim itemsList As New List(Of String)()
                            Try
                                Dim info = wpdDevice.GetDirectoryInfo(objectId)
                                If info IsNot Nothing Then
                                    Dim files = info.EnumerateFiles()
                                    For Each f In files
                                        itemsList.Add($"{f.Name}|{f.Length}|{f.LastWriteTime}")
                                    Next
                                    Dim dirs = info.EnumerateDirectories()
                                    For Each d In dirs
                                        itemsList.Add($"{d.Name}|DIR|{d.LastWriteTime}")
                                    Next
                                End If
                                itemsList.Sort()
                                Dim sb As New StringBuilder()
                                sb.Append(itemsList.Count.ToString() & "||")
                                For Each item In itemsList
                                    sb.Append(item & "||")
                                Next
                                currentState = sb.ToString()
                            Catch exRead As Exception
                                ScriviLogCompleto($"MonitorContenutoWpdLoop - Errore lettura WPD", exRead)
                                currentState = "ERROR"
                            End Try
                        Else
                            currentState = "DISCONNECTED"
                        End If
                    End If

                    If Not String.IsNullOrEmpty(currentState) AndAlso currentState <> "ERROR" AndAlso currentState <> _lastState Then
                        Dim itemsOld = EstraiNomiDaStato(_lastState)
                        Dim itemsNew = EstraiNomiDaStato(currentState)

                        For Each oldItem In itemsOld
                            If Not itemsNew.Contains(oldItem) Then
                                Dim fullPath As String = path & If(path.EndsWith("\"), "", "\") & oldItem
                                RaiseEvent AggiornamentoCartella(fullPath)
                            End If
                        Next

                        For Each newItem In itemsNew
                            If Not itemsOld.Contains(newItem) Then
                                Dim fullPath As String = path & If(path.EndsWith("\"), "", "\") & newItem
                                RaiseEvent AggiornamentoCartella(fullPath)
                            End If
                        Next

                        _lastState = currentState
                    End If

                Catch exLoop As Exception
                    ScriviLogCompleto($"MonitorContenutoWpdLoop - Errore ciclo controllo per {path}", exLoop)
                End Try
            End While

            If wpdDevice IsNot Nothing AndAlso wpdDevice.IsConnected Then
                wpdDevice.Disconnect()
            End If

        Catch ex As Exception
            ScriviLogCompleto("CLSFile.MonitorContenutoWpdLoop - Errore fatale thread", ex)
        Finally
            If hTimer <> IntPtr.Zero Then CloseHandle(hTimer)
        End Try
    End Sub

    Private Function EstraiNomiDaStato(stateString As String) As List(Of String)
        Dim lista As New List(Of String)
        If String.IsNullOrEmpty(stateString) OrElse stateString = "ERROR" OrElse stateString = "DISCONNECTED" OrElse stateString = "DELETED" Then Return lista
        Dim elements = stateString.Split(New String() {"||"}, StringSplitOptions.RemoveEmptyEntries)
        For i As Integer = 1 To elements.Length - 1
            Dim parts = elements(i).Split("|"c)
            If parts.Length > 0 Then
                lista.Add(parts(0))
            End If
        Next
        Return lista
    End Function

    Public Sub AvviaMonitoraggioDrive()
        Try
            If _driveDetector IsNot Nothing Then Return
            _driveDetector = New DriveDetectorWindow()
            AddHandler _driveDetector.DeviceArrived, Sub(driveLetter)
                                                         RaiseEvent DriveAdded(driveLetter)
                                                     End Sub
            AddHandler _driveDetector.DeviceRemoved, Sub(driveLetter)
                                                         RaiseEvent DriveRemoved(driveLetter)
                                                     End Sub

            If _wpdMonitorThread Is Nothing Then
                _stopWpdMonitor = False
                Try
                    _knownDeviceIds = MediaDevice.GetDevices().Select(Function(d) d.DeviceId).ToList()
                Catch
                    _knownDeviceIds = New List(Of String)()
                End Try
                _wpdMonitorThread = New Thread(AddressOf MonitorWPDLoop)
                _wpdMonitorThread.IsBackground = True
                _wpdMonitorThread.Start()
            End If

        Catch ex As Exception
            ScriviLogCompleto("CLSFile.AvviaMonitoraggioDrive - Errore avvio", ex)
        End Try
    End Sub

    Private Sub MonitorWPDLoop()
        Try
            If Environment.ProcessorCount > 1 Then
                SetThreadAffinityMask(GetCurrentThread(), New IntPtr(1 << 1))
            End If
        Catch
        End Try

        Dim hTimer As IntPtr = IntPtr.Zero
        Try
            hTimer = CreateWaitableTimer(IntPtr.Zero, False, Nothing)
            If hTimer = IntPtr.Zero Then Return

            Dim dueTime As Long = -1000000

            While Not _stopWpdMonitor
                If SetWaitableTimer(hTimer, dueTime, 0, IntPtr.Zero, IntPtr.Zero, False) Then
                    WaitForSingleObject(hTimer, -1)
                Else
                    Thread.Sleep(100)
                End If

                If _stopWpdMonitor Then Exit While

                Try
                    Dim currentDevices = MediaDevice.GetDevices().ToList()
                    Dim currentIds = currentDevices.Select(Function(d) d.DeviceId).ToList()

                    Dim added = currentIds.Except(_knownDeviceIds).ToList()
                    Dim removed = _knownDeviceIds.Except(currentIds).ToList()

                    If added.Count > 0 OrElse removed.Count > 0 Then
                        _knownDeviceIds = currentIds

                        For Each newId In added
                            RaiseEvent DriveAdded(WPD_PREFIX & newId & "|\")
                        Next

                        For Each oldId In removed
                            RaiseEvent DriveRemoved(WPD_PREFIX & oldId & "|\")
                        Next
                    End If
                Catch
                End Try
            End While
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.MonitorWPDLoop", ex)
        Finally
            If hTimer <> IntPtr.Zero Then CloseHandle(hTimer)
        End Try
    End Sub

    Public Function RimuoviDevice(unit As String) As Boolean
        Try
            If Not Regex.IsMatch(unit, "^[a-zA-Z]:\\$") Then Return False

            If Not IsUserAdministrator() Then
                Return False
            End If

            Dim driveLetter As String = unit.TrimEnd("\"c).Replace("'", "''")
            Dim smontaggioRiuscito As Boolean = False

            Try
                Dim query As String = "SELECT * FROM Win32_Volume WHERE DriveLetter = '" & driveLetter & "'"
                Using searcher As New ManagementObjectSearcher(query)
                    Using collezione As ManagementObjectCollection = searcher.Get()
                        For Each volume As ManagementObject In collezione.Cast(Of ManagementObject)()
                            Using volumeCorrente As ManagementObject = volume
                                Try
                                    Dim parametriIngresso As ManagementBaseObject = volumeCorrente.GetMethodParameters("Dismount")
                                    parametriIngresso("Force") = True
                                    parametriIngresso("Permanent") = False
                                    Dim parametriUscita As ManagementBaseObject = volumeCorrente.InvokeMethod("Dismount", parametriIngresso, Nothing)
                                    Dim codiceRitorno As UInteger = Convert.ToUInt32(parametriUscita("ReturnValue"))
                                    smontaggioRiuscito = (codiceRitorno = 0)
                                    If Not smontaggioRiuscito Then
                                    End If
                                Catch exInvoke As ManagementException
                                    ScriviLogCompleto("CLSFile.RimuoviDevice - Errore invocazione Dismount", exInvoke)
                                End Try
                            End Using
                            Exit For
                        Next
                    End Using
                End Using
            Catch exWmi As ManagementException
                ScriviLogCompleto("CLSFile.RimuoviDevice - Errore query WMI", exWmi)
                Return False
            End Try

            If Not smontaggioRiuscito Then Return False

            Return Not System.IO.Directory.Exists(unit)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.RimuoviDevice - Errore generico", ex)
            Return False
        End Try
    End Function

    Public Function IsUserAdministrator() As Boolean
        Try
            Dim sidAuthority As New SID_IDENTIFIER_AUTHORITY()
            sidAuthority.Value = New Byte() {0, 0, 0, 0, 0, SECURITY_NT_AUTHORITY}
            Dim pSid As IntPtr = IntPtr.Zero
            Dim isMember As Boolean = False

            If AllocateAndInitializeSid(sidAuthority, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0, pSid) Then
                If Not CheckTokenMembership(IntPtr.Zero, pSid, isMember) Then
                    isMember = False
                End If
                FreeSid(pSid)
            End If
            Return isMember
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsUserAdministrator - Errore verifica API", ex)
            Return False
        End Try
    End Function

    Public Function OttieniProprietario(path As String) As String
        Dim pSecurityDescriptor As IntPtr = IntPtr.Zero
        Dim pSidOwner As IntPtr = IntPtr.Zero
        Dim pSidGroup As IntPtr = IntPtr.Zero
        Dim pDacl As IntPtr = IntPtr.Zero
        Dim pSacl As IntPtr = IntPtr.Zero
        Dim pStringSid As IntPtr = IntPtr.Zero
        Dim ownerName As String = "Unknown"

        Try
            If IsWpdPath(path) Then Return "WPD Device"

            Dim result As Integer = GetNamedSecurityInfo(path, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, pSidOwner, pSidGroup, pDacl, pSacl, pSecurityDescriptor)

            If result = 0 AndAlso pSidOwner <> IntPtr.Zero Then
                Dim name As New StringBuilder(256)
                Dim domain As New StringBuilder(256)

                Dim cchName As Integer = name.Capacity
                Dim cchDomain As Integer = domain.Capacity
                Dim sidType As Integer = 0

                Dim resultLookup As Boolean = False
                Try
                    resultLookup = LookupAccountSid(Nothing, pSidOwner, name, cchName, domain, cchDomain, sidType)
                Catch
                End Try

                If resultLookup Then
                    ownerName = $"{domain}\{name}"
                Else
                    If ConvertSidToStringSid(pSidOwner, pStringSid) Then
                        ownerName = Marshal.PtrToStringAuto(pStringSid)
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.OttieniProprietario - Errore", ex)
        Finally
            If pStringSid <> IntPtr.Zero Then LocalFree(pStringSid)
            If pSecurityDescriptor <> IntPtr.Zero Then LocalFree(pSecurityDescriptor)
        End Try
        Return ownerName
    End Function

    Public Function ÈDisco(ByVal percorso As String) As Boolean
        Try
            If IsWpdPath(percorso) Then Return False
            If percorso.EndsWith(":\") Then
                percorso = percorso.Substring(0, 3) & "\"
            End If
            Dim drives() As System.IO.DriveInfo = System.IO.DriveInfo.GetDrives()
            For Each drive As System.IO.DriveInfo In drives
                If drive.Name.Equals(percorso, StringComparison.OrdinalIgnoreCase) Then
                    Dim driveType As UInteger = GetDriveType(drive.RootDirectory.FullName)
                    Return driveType = DRIVE_REMOVABLE OrElse driveType = DRIVE_FIXED OrElse driveType = DRIVE_CDROM OrElse driveType = DRIVE_RAMDISK
                End If
            Next
            Return False
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.ÈDisco - Errore", ex)
            Return False
        End Try
    End Function

    Public Function IsExternalDisk(diskPath As String) As Boolean
        Try
            If IsWpdPath(diskPath) Then Return False
            If WGRete.IsUncPath(diskPath) Then Return False
            Dim root As String = System.IO.Path.GetPathRoot(diskPath)
            If String.IsNullOrEmpty(root) Then Return False
            Dim driveInfo As New DriveInfo(root)
            Return driveInfo.DriveType = DriveType.Removable
        Catch ex As ArgumentException
            ScriviLogCompleto("CLSFile.IsExternalDisk - Path non valido", ex)
            Return False
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsExternalDisk - Errore generico", ex)
            Return False
        End Try
    End Function

    Public Function IsSystemDrive(drivePath As String) As Boolean
        Try
            If IsWpdPath(drivePath) Then Return False
            Dim systemDrive As String = Environment.GetEnvironmentVariable("SystemDrive")
            If String.IsNullOrEmpty(systemDrive) Then Return False
            Return drivePath.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsSystemDrive - Errore", ex)
            Return False
        End Try
    End Function

    Public Function IsDriveReady(ByVal drive As String) As Boolean
        Try
            If WGRete.IsRemoto(drive) Then Return WGRete.Raggiungibile(drive)
            Dim freeBytesAvailableToCaller As ULong = 0
            Dim totalNumberOfBytes As ULong = 0
            Dim totalNumberOfFreeBytes As ULong = 0
            Return GetDiskFreeSpaceEx(drive, freeBytesAvailableToCaller, totalNumberOfBytes, totalNumberOfFreeBytes)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsDriveReady - Errore", ex)
            Return False
        End Try
    End Function

    Public Function IsDeviceConnected(path As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(path) Then Return False
            If IsWpdPath(path) Then
                Dim parts = path.Split("|"c)
                If parts.Length < 2 Then Return False
                Dim deviceId = parts(1)
                Dim devices = MediaDevice.GetDevices()
                Dim device = devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                Return device IsNot Nothing AndAlso device.IsConnected
            Else
                Dim root = System.IO.Path.GetPathRoot(path)
                If String.IsNullOrEmpty(root) Then Return False
                Return WGRete.EsisteDirectory(root)
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsDeviceConnected - Errore", ex)
            Return False
        End Try
    End Function


    Public Function GetWpdDetails(path As String) As Dictionary(Of String, String)
        Dim dict As New Dictionary(Of String, String)
        Dim deviceId As String = ""
        Try
            Dim parts = path.Split("|"c)
            deviceId = parts(1)
            Dim objectId = If(parts.Length > 2, parts(2), "\")
            Dim devices = MediaDevice.GetDevices()
            Dim device = devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            If device IsNot Nothing Then
                Dim isConnected As Boolean = False
                Dim connectThread As New Thread(Sub()
                                                    Try
                                                        device.Connect()
                                                        isConnected = device.IsConnected
                                                    Catch
                                                    End Try
                                                End Sub)
                connectThread.IsBackground = True
                connectThread.Start()
                connectThread.Join(2000)

                If Not isConnected Then
                    dict.Add("Name", "Connection Timeout")
                    dict.Add("Size", "0")
                    dict.Add("Type", "Error")
                    Return dict
                End If

                Try
                    If parts.Length <= 2 OrElse objectId = "\" Then
                        dict.Add("Name", device.FriendlyName)
                        dict.Add("Manufacturer", device.Manufacturer)
                        dict.Add("Model", device.Model)
                        Try : dict.Add("SerialNumber", device.SerialNumber) : Catch : End Try
                        Try : dict.Add("FirmwareVersion", device.FirmwareVersion) : Catch : End Try
                        Try : dict.Add("PowerLevel", device.PowerLevel.ToString()) : Catch : End Try
                    Else
                        If IsWpdDirectory(path) Then
                            Dim info = device.GetDirectoryInfo(objectId)
                            If info IsNot Nothing Then
                                dict.Add("Name", info.Name)
                                dict.Add("Type", "Cartella")
                                Try : dict.Add("CreationTime", info.CreationTime.ToString()) : Catch : End Try
                                Try : dict.Add("LastWriteTime", info.LastWriteTime.ToString()) : Catch : End Try
                            Else
                                dict.Add("Name", objectId)
                                dict.Add("Type", "Cartella")
                            End If
                        Else
                            Dim info = device.GetFileInfo(objectId)
                            If info IsNot Nothing Then
                                dict.Add("Name", info.Name)
                                dict.Add("Size", (info.Length / 1024).ToString("N0") & " KB")
                                dict.Add("Type", "File")
                                Try : dict.Add("CreationTime", info.CreationTime.ToString()) : Catch : End Try
                                Try : dict.Add("LastWriteTime", info.LastWriteTime.ToString()) : Catch : End Try
                            Else
                                dict.Add("Name", objectId)
                                dict.Add("Size", "N/A")
                                dict.Add("Type", "Unknown")
                            End If
                        End If
                    End If
                Finally
                    device.Disconnect()
                End Try
            End If
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.GetWpdDetails - Errore", ex)
            dict.Add("Name", "Unknown")
            dict.Add("Size", "0")
            dict.Add("Type", "Unknown")
        End Try
        Return dict
    End Function

    Public Function GetTagManager() As CLSTagMp3
        Return _tagManager
    End Function

    Public Function IsDirectory(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If IsWpdPath(percorso) Then Return IsWpdDirectory(percorso)
            Return WGRete.EsisteDirectory(percorso)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsDirectory - Errore", ex)
            Return False
        End Try
    End Function

    Public Function IsFile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If IsWpdPath(percorso) Then Return Not IsWpdDirectory(percorso)
            Return WGRete.EsisteFile(percorso)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsFile - Errore", ex)
            Return False
        End Try
    End Function

    Public Function IsMediaFile(percorso As String) As Boolean
        Try
            If Not IsWpdPath(percorso) Then
                If Not WGRete.EsisteFile(percorso) Then Return False
            End If
            Dim estensione As String
            If IsWpdPath(percorso) Then
                Dim name = GetWpdFileName(percorso)
                estensione = Path.GetExtension(name).ToLowerInvariant()
            Else
                estensione = Path.GetExtension(percorso).ToLowerInvariant()
            End If
            Return estensioniAudioVideo.Contains(estensione)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.IsMediaFile - Errore", ex)
            Return False
        End Try
    End Function

    Public Async Function CriptaPercorsoAsync(percorso As String, Optional cancellationToken As CancellationToken = Nothing) As Task
        Await Task.Run(Sub()
                           Try
                               cancellationToken.ThrowIfCancellationRequested()
                               If System.IO.Directory.Exists(percorso) Then
                                   Dim info As New DirectoryInfo(percorso)
                                   Try
                                       info.Attributes = info.Attributes Or FileAttributes.Encrypted
                                   Catch ex As Exception
                                       ScriviLogCompleto("CriptaPercorsoAsync - Errore attributi directory", ex)
                                   End Try
                                   For Each f In System.IO.Directory.GetFiles(percorso, "*.*", SearchOption.AllDirectories)
                                       cancellationToken.ThrowIfCancellationRequested()
                                       Try
                                           File.Encrypt(f)
                                       Catch exFile As Exception
                                           ScriviLogCompleto($"CriptaPercorsoAsync - Errore file {f}", exFile)
                                       End Try
                                   Next
                               ElseIf System.IO.File.Exists(percorso) Then
                                   File.Encrypt(percorso)
                               End If
                           Catch ex As OperationCanceledException
                               ScriviLogCompleto("CriptaPercorsoAsync - Operazione annullata", ex)
                               Throw
                           Catch ex As UnauthorizedAccessException
                               ScriviLogCompleto("CriptaPercorsoAsync - Accesso negato", ex)
                               Throw
                           Catch ex As IOException
                               ScriviLogCompleto("CriptaPercorsoAsync - Errore IO", ex)
                               Throw
                           Catch ex As Exception
                               ScriviLogCompleto("CriptaPercorsoAsync - Errore generico", ex)
                               Throw
                           End Try
                       End Sub, cancellationToken)
    End Function

    Public Async Function DecriptaPercorsoAsync(percorso As String, Optional cancellationToken As CancellationToken = Nothing) As Task
        Await Task.Run(Sub()
                           Try
                               cancellationToken.ThrowIfCancellationRequested()
                               If System.IO.Directory.Exists(percorso) Then
                                   Dim info As New DirectoryInfo(percorso)
                                   Try
                                       info.Attributes = info.Attributes And Not FileAttributes.Encrypted
                                   Catch ex As Exception
                                       ScriviLogCompleto("DecriptaPercorsoAsync - Errore attributi directory", ex)
                                   End Try
                                   For Each f In System.IO.Directory.GetFiles(percorso, "*.*", SearchOption.AllDirectories)
                                       cancellationToken.ThrowIfCancellationRequested()
                                       Try
                                           File.Decrypt(f)
                                       Catch exFile As Exception
                                           ScriviLogCompleto($"DecriptaPercorsoAsync - Errore file {f}", exFile)
                                       End Try
                                   Next
                               ElseIf System.IO.File.Exists(percorso) Then
                                   File.Decrypt(percorso)
                               End If
                           Catch ex As OperationCanceledException
                               ScriviLogCompleto("DecriptaPercorsoAsync - Operazione annullata", ex)
                               Throw
                           Catch ex As UnauthorizedAccessException
                               ScriviLogCompleto("DecriptaPercorsoAsync - Accesso negato", ex)
                               Throw
                           Catch ex As IOException
                               ScriviLogCompleto("DecriptaPercorsoAsync - Errore IO", ex)
                               Throw
                           Catch ex As Exception
                               ScriviLogCompleto("DecriptaPercorsoAsync - Errore generico", ex)
                               Throw
                           End Try
                       End Sub, cancellationToken)
    End Function

    Public Function EseguiPulizia(tipo As String) As Long
        Try
            Dim risultato As WGPuliziaPC.RisultatoPulizia = EseguiPuliziaDettagliata(tipo)
            If risultato Is Nothing Then Return 0
            Return risultato.TotaleLiberato
        Catch ex As Exception
            ScriviLogCompleto($"CLSFile.EseguiPulizia ({tipo}) - Errore", ex)
            Return 0
        End Try
    End Function

    Public Function EseguiPuliziaDettagliata(tipo As String) As WGPuliziaPC.RisultatoPulizia
        Try
            Dim pulitore As New WGPuliziaPC()
            Return pulitore.Esegui(tipo)
        Catch ex As Exception
            ScriviLogCompleto($"CLSFile.EseguiPuliziaDettagliata ({tipo}) - Errore", ex)
            Return Nothing
        End Try
    End Function

    Public Function FiltraFilePerClipboard(elementi As List(Of String)) As System.Collections.Specialized.StringCollection
        Try
            Dim fileDropList As New System.Collections.Specialized.StringCollection()
            For Each percorsoFile As String In elementi
                If Not IsWpdPath(percorsoFile) Then
                    fileDropList.Add(percorsoFile)
                End If
            Next
            Return fileDropList
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.FiltraFilePerClipboard - Errore", ex)
            Return New System.Collections.Specialized.StringCollection()
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _disposed Then Return
            _disposed = True

            _stopMonitor = True
            If _monitorThread IsNot Nothing AndAlso _monitorThread.IsAlive Then
                _monitorThread.Join(1000)
            End If
            _monitorThread = Nothing

            _stopWpdMonitor = True
            If _wpdMonitorThread IsNot Nothing AndAlso _wpdMonitorThread.IsAlive Then
                _wpdMonitorThread.Join(1000)
            End If
            _wpdMonitorThread = Nothing

            If _fsWatcher IsNot Nothing Then
                Try
                    _fsWatcher.EnableRaisingEvents = False
                    _fsWatcher.Dispose()
                Catch exWatcher As Exception
                    ScriviLogCompleto("CLSFile.Dispose - Errore rilascio FileSystemWatcher", exWatcher)
                End Try
                _fsWatcher = Nothing
            End If

            If _driveDetector IsNot Nothing Then
                Try
                    _driveDetector.Dispose()
                Catch exDetector As Exception
                    ScriviLogCompleto("CLSFile.Dispose - Errore rilascio DriveDetectorWindow", exDetector)
                End Try
                _driveDetector = Nothing
            End If

            GC.SuppressFinalize(Me)
        Catch ex As Exception
            ScriviLogCompleto("CLSFile.Dispose - Errore generico", ex)
        End Try
    End Sub

    Private Class DriveDetectorWindow
        Inherits NativeWindow
        Implements IDisposable

        Public Event DeviceArrived(driveLetter As String)
        Public Event DeviceRemoved(driveLetter As String)

        Private Const WM_DEVICECHANGE As Integer = &H219
        Private Const DBT_DEVICEARRIVAL As Integer = &H8000
        Private Const DBT_DEVICEREMOVECOMPLETE As Integer = &H8004
        Private Const DBT_DEVTYP_VOLUME As Integer = &H2

        <StructLayout(LayoutKind.Sequential)>
        Private Structure DEV_BROADCAST_HDR
            Public dbch_size As Integer
            Public dbch_devicetype As Integer
            Public dbch_reserved As Integer
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure DEV_BROADCAST_VOLUME
            Public dbcv_size As Integer
            Public dbcv_devicetype As Integer
            Public dbcv_reserved As Integer
            Public dbcv_unitmask As Integer
            Public dbcv_flags As Short
        End Structure

        Public Sub New()
            CreateHandle(New CreateParams())
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_DEVICECHANGE Then
                Select Case m.WParam.ToInt32()
                    Case DBT_DEVICEARRIVAL
                        Dim driveLetter As String = GetDriveLetter(m.LParam)
                        If Not String.IsNullOrEmpty(driveLetter) Then
                            RaiseEvent DeviceArrived(driveLetter)
                        End If
                    Case DBT_DEVICEREMOVECOMPLETE
                        Dim driveLetter As String = GetDriveLetter(m.LParam)
                        If Not String.IsNullOrEmpty(driveLetter) Then
                            RaiseEvent DeviceRemoved(driveLetter)
                        End If
                End Select
            End If
            MyBase.WndProc(m)
        End Sub

        Private Function GetDriveLetter(lpdb As IntPtr) As String
            Try
                If lpdb = IntPtr.Zero Then Return Nothing
                Dim hdr As DEV_BROADCAST_HDR = Marshal.PtrToStructure(Of DEV_BROADCAST_HDR)(lpdb)
                If hdr.dbch_devicetype = DBT_DEVTYP_VOLUME Then
                    Dim vol As DEV_BROADCAST_VOLUME = Marshal.PtrToStructure(Of DEV_BROADCAST_VOLUME)(lpdb)
                    Dim mask As Integer = vol.dbcv_unitmask
                    For i As Integer = 0 To 25
                        If (mask And (1 << i)) <> 0 Then
                            Return Chr(Asc("A") + i) & ":\"
                        End If
                    Next
                End If
            Catch
            End Try
            Return Nothing
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            DestroyHandle()
        End Sub
    End Class

End Class