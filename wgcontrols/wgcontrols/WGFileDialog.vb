Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports Microsoft.VisualBasic.ApplicationServices
Imports Microsoft.Win32
Imports wgcontrols
Imports System.ComponentModel
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Security.AccessControl

Public Class WGFileDialog
    Public Event selezioneterminata(elementi As List(Of String))
    Public Event singolaselezione(elemento As String)
    Public Event annullata()
    Private percorso As String
    Private lista As New WGMenuLista()
    Private currentDirectory As String
    Private fileecartelle As New List(Of String)
    Private drive As List(Of String)
    Private history As New List(Of NavigazioneStato)()
    Private WithEvents campo As WGCampoTesto
    Private campi As Campi
    Private errore As String
    Private Shared log As New wgcontrols.Logger()
    Private menu As WGMenu
    Public allowedExtensions As New List(Of String)()
    Private menuchiuso As Boolean = False
    Private selectedPaths As New List(Of String)()
    Public Property MultiselezioneConsentita As Boolean = False

    Private Class NavigazioneStato
        Public Property Directory As String
        Public Property IndiceSelezionato As Integer

        Public Sub New(directory As String, indiceSelezionato As Integer)
            Me.Directory = directory
            Me.IndiceSelezionato = indiceSelezionato
        End Sub
    End Class

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CreateDirectory(lpPathName As String, lpSecurityAttributes As IntPtr) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
    Private Structure WIN32_FIND_DATA
        Public dwFileAttributes As UInteger
        Public ftCreationTime As FILETIME
        Public ftLastAccessTime As FILETIME
        Public ftLastWriteTime As FILETIME
        Public nFileSizeHigh As UInteger
        Public nFileSizeLow As UInteger
        Public dwReserved0 As UInteger
        Public dwReserved1 As UInteger
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=260)> Public cFileName As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=14)> Public cAlternateFileName As String
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FILETIME
        Public dwLowDateTime As UInteger
        Public dwHighDateTime As UInteger
    End Structure

    Private Const INVALID_HANDLE_VALUE As Integer = -1
    Private Const FILE_ATTRIBUTE_DIRECTORY As UInteger = &H10
    Private Const S_OK As Integer = 0

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function FindFirstFile(lpFileName As String, ByRef lpFindFileData As WIN32_FIND_DATA) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function FindNextFile(hFindFile As IntPtr, ByRef lpFindFileData As WIN32_FIND_DATA) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function FindClose(hFindFile As IntPtr) As Boolean
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

    <DllImport("mpr.dll", CharSet:=CharSet.Auto)>
    Private Shared Function WNetGetConnection(ByVal lpszLocalName As String, ByVal lpszRemoteName As StringBuilder, ByRef cbRemoteName As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetDiskFreeSpaceEx(ByVal lpDirectoryName As String, ByRef lpFreeBytesAvailableToCaller As ULong, ByRef lpTotalNumberOfBytes As ULong, ByRef lpTotalNumberOfFreeBytes As ULong) As Boolean
    End Function

    Private Const SHGFI_DISPLAYNAME As UInteger = &H200
    Private Const DRIVE_UNKNOWN As UInteger = 0
    Private Const DRIVE_NO_ROOT_DIR As UInteger = 1
    Private Const DRIVE_REMOVABLE As UInteger = 2
    Private Const DRIVE_FIXED As UInteger = 3
    Private Const DRIVE_REMOTE As UInteger = 4
    Private Const DRIVE_CDROM As UInteger = 5
    Private Const DRIVE_RAMDISK As UInteger = 6

    Private Function IsDriveReadyLocale(drivePath As String) As Boolean
        Try
            Dim freeBytesAvailableToCaller As ULong = 0
            Dim totalNumberOfBytes As ULong = 0
            Dim totalNumberOfFreeBytes As ULong = 0
            Return GetDiskFreeSpaceEx(drivePath, freeBytesAvailableToCaller, totalNumberOfBytes, totalNumberOfFreeBytes)
        Catch ex As Exception
            log.ScriviLog("Errore in IsDriveReadyLocale per '" & drivePath & "': " & ex.Message)
            Return False
        End Try
    End Function

    Private Function OttieniDischiConNomi() As List(Of Tuple(Of String, String))
        Dim result As New List(Of Tuple(Of String, String))
        Dim logicalDrives As String()

        Try
            logicalDrives = Environment.GetLogicalDrives()
        Catch ex As Exception
            log.ScriviLog("Errore recupero drive logici in OttieniDischiConNomi: " & ex.Message)
            logicalDrives = New String() {}
        End Try

        Dim addedUncPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each drivePath As String In logicalDrives
            Try
                Dim driveType As UInteger = GetDriveType(drivePath)
                If driveType = DRIVE_UNKNOWN OrElse driveType = DRIVE_NO_ROOT_DIR Then Continue For

                If driveType = DRIVE_REMOVABLE OrElse driveType = DRIVE_FIXED Then
                    If Not IsDriveReadyLocale(drivePath) Then Continue For
                End If

                Dim displayName As String = ""
                Dim shfi As New SHFILEINFO()
                Dim ptr As IntPtr = SHGetFileInfo(drivePath, 0, shfi, CUInt(Marshal.SizeOf(shfi)), SHGFI_DISPLAYNAME)

                If ptr <> IntPtr.Zero Then
                    displayName = shfi.szDisplayName
                Else
                    Dim lastErr As Integer = Marshal.GetLastWin32Error()
                End If

                If String.IsNullOrWhiteSpace(displayName) Then
                    Dim volumeNameBuffer As New StringBuilder(261)
                    Dim fileSystemNameBuffer As New StringBuilder(261)
                    Dim serialNumber As Integer
                    Dim maxComponentLength As Integer
                    Dim fileSystemFlags As Integer

                    Try
                        GetVolumeInformation(drivePath, volumeNameBuffer, volumeNameBuffer.Capacity, serialNumber, maxComponentLength, fileSystemFlags, fileSystemNameBuffer, fileSystemNameBuffer.Capacity)
                    Catch exVol As Exception
                        log.ScriviLog("Errore GetVolumeInformation per " & drivePath & ": " & exVol.Message)
                    End Try

                    If driveType = DRIVE_REMOTE Then
                        Dim uncPath As New StringBuilder(255)
                        Dim size As Integer = uncPath.Capacity
                        Dim callResult As Integer = 0
                        Try
                            callResult = WNetGetConnection(drivePath.Substring(0, 2), uncPath, size)
                        Catch exNet As Exception
                            log.ScriviLog("Errore WNetGetConnection per " & drivePath & ": " & exNet.Message)
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

                result.Add(New Tuple(Of String, String)(displayName, drivePath))
            Catch ex As Exception
                log.ScriviLog("Errore elaborazione drive '" & drivePath & "' in OttieniDischiConNomi: " & ex.Message)
            End Try
        Next

        Return result
    End Function

    Private Function OttieniCartelleCloud() As List(Of Tuple(Of String, String))
        Dim result As New List(Of Tuple(Of String, String))
        Dim percorsiAggiunti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Try
            Dim oneDrivePersonale As String = Environment.GetEnvironmentVariable("OneDriveConsumer")
            If String.IsNullOrWhiteSpace(oneDrivePersonale) Then oneDrivePersonale = Environment.GetEnvironmentVariable("OneDrive")
            If Not String.IsNullOrWhiteSpace(oneDrivePersonale) AndAlso Directory.Exists(oneDrivePersonale) AndAlso percorsiAggiunti.Add(oneDrivePersonale) Then
                result.Add(New Tuple(Of String, String)("OneDrive", oneDrivePersonale))
            End If
        Catch ex As Exception
            log.ScriviLog("Errore rilevamento OneDrive personale in OttieniCartelleCloud: " & ex.Message)
        End Try

        Try
            Dim oneDriveAziendale As String = Environment.GetEnvironmentVariable("OneDriveCommercial")
            If Not String.IsNullOrWhiteSpace(oneDriveAziendale) AndAlso Directory.Exists(oneDriveAziendale) AndAlso percorsiAggiunti.Add(oneDriveAziendale) Then
                result.Add(New Tuple(Of String, String)("OneDrive Aziendale", oneDriveAziendale))
            End If
        Catch ex As Exception
            log.ScriviLog("Errore rilevamento OneDrive aziendale in OttieniCartelleCloud: " & ex.Message)
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
                                        result.Add(New Tuple(Of String, String)(nomeVisualizzato, cartellaUtente))
                                    End If
                                End If
                            End Using
                        Catch exAccount As Exception
                            log.ScriviLog("Errore lettura account OneDrive '" & nomeAccount & "' in OttieniCartelleCloud: " & exAccount.Message)
                        End Try
                    Next
                End If
            End Using
        Catch ex As Exception
            log.ScriviLog("Errore lettura registro OneDrive in OttieniCartelleCloud: " & ex.Message)
        End Try

        Try
            Dim infoDropbox As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dropbox", "info.json")
            If File.Exists(infoDropbox) Then
                Dim contenuto As String = File.ReadAllText(infoDropbox)
                Dim percorsoPersonale As String = EstraiPercorsoDropbox(contenuto, "personal")
                If Not String.IsNullOrWhiteSpace(percorsoPersonale) AndAlso Directory.Exists(percorsoPersonale) AndAlso percorsiAggiunti.Add(percorsoPersonale) Then
                    result.Add(New Tuple(Of String, String)("Dropbox", percorsoPersonale))
                End If
                Dim percorsoAziendale As String = EstraiPercorsoDropbox(contenuto, "business")
                If Not String.IsNullOrWhiteSpace(percorsoAziendale) AndAlso Directory.Exists(percorsoAziendale) AndAlso percorsiAggiunti.Add(percorsoAziendale) Then
                    result.Add(New Tuple(Of String, String)("Dropbox Aziendale", percorsoAziendale))
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("Errore rilevamento Dropbox in OttieniCartelleCloud: " & ex.Message)
        End Try

        Try
            Dim cartellaUtenteBase As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

            Dim googleDriveLegacy As String = Path.Combine(cartellaUtenteBase, "Google Drive")
            If Directory.Exists(googleDriveLegacy) AndAlso percorsiAggiunti.Add(googleDriveLegacy) Then
                result.Add(New Tuple(Of String, String)("Google Drive", googleDriveLegacy))
            End If

            Dim iCloudDrive As String = Path.Combine(cartellaUtenteBase, "iCloudDrive")
            If Directory.Exists(iCloudDrive) AndAlso percorsiAggiunti.Add(iCloudDrive) Then
                result.Add(New Tuple(Of String, String)("iCloud Drive", iCloudDrive))
            End If
        Catch ex As Exception
            log.ScriviLog("Errore rilevamento cartelle cloud aggiuntive in OttieniCartelleCloud: " & ex.Message)
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
            log.ScriviLog("Errore in EstraiPercorsoDropbox: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function OttieniPercorsoSemplice(nomeCartella As String) As String
        Try
            Select Case nomeCartella
                Case "Desktop"
                    Return Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                Case "Documenti"
                    Return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                Case "Immagini"
                    Return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                Case "Musica"
                    Return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                Case "Download"
                    Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                Case Else
                    Return String.Empty
            End Select
        Catch ex As Exception
            log.ScriviLog("Errore in OttieniPercorsoSemplice: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Sub AggiungiEstensione(extension As String)
        Try
            If String.IsNullOrWhiteSpace(extension) Then
                Return
            End If
            Dim extNormalizzata As String = extension.Trim().ToLower()
            If Not extNormalizzata.StartsWith(".") Then
                extNormalizzata = "." & extNormalizzata
            End If
            If Not allowedExtensions.Contains(extNormalizzata) Then
                allowedExtensions.Add(extNormalizzata)
            End If
        Catch ex As ArgumentNullException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in AggiungiEstensione (ArgumentNullException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in AggiungiEstensione.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Public Sub Avvia(Optional directory As String = "")
        Try
            AddHandler lista.ElementoSelezionato, AddressOf Selezionato
            AddHandler lista.ElementiSelezionati, AddressOf selezionati
            AddHandler lista.escPremuto, AddressOf Navigazione

            If String.IsNullOrEmpty(directory) Then
                sceltadrive()
            Else
                CaricaDirectory(directory)
            End If

            lista.avvia()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Avvia (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in Avvia.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Public Function Recupera() As List(Of String)
        Try
            Return selectedPaths
        Catch ex As ObjectDisposedException
            Return New List(Of String)()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in Recupera.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
                Return New List(Of String)()
            End If
        End Try
    End Function

    Private Sub sceltadrive()
        Try
            lista.pulisci()
            lista.impostaTitolo("Scelta risorsa")
            lista.multiselezioneConsentita = False

            Dim elencoDischi As List(Of Tuple(Of String, String)) = Nothing
            Try
                elencoDischi = OttieniDischiConNomi()
            Catch ex As Exception
                log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante il recupero dei dischi con nomi in sceltadrive, uso il metodo di riserva.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
                elencoDischi = Nothing
            End Try

            If elencoDischi IsNot Nothing AndAlso elencoDischi.Count > 0 Then
                For Each disco In elencoDischi
                    Try
                        lista.aggiungiElemento(disco.Item1, disco.Item2)
                    Catch ex As Exception
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante l'aggiunta del disco '{disco.Item2}' in sceltadrive.{Environment.NewLine}Dettaglio: {ex.Message}")
                    End Try
                Next
            Else
                For Each drive As DriveInfo In DriveInfo.GetDrives()
                    Try
                        If drive.IsReady Then
                            lista.aggiungiElemento(drive.Name, drive.RootDirectory.FullName)
                        End If
                    Catch ex As IOException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in sceltadrive (IOException) durante l'accesso al drive '{drive.Name}'.{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As UnauthorizedAccessException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in sceltadrive (UnauthorizedAccessException) durante l'accesso al drive '{drive.Name}'.{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As System.Security.SecurityException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in sceltadrive (SecurityException) durante l'accesso al drive '{drive.Name}'.{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As Exception
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in sceltadrive durante l'accesso al drive '{drive.Name}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
                    End Try
                Next
            End If

            Dim cartelleUtente As String() = {"Desktop", "Documenti", "Immagini", "Musica", "Download"}

            For Each nome In cartelleUtente
                Try
                    Dim folderPath As String = OttieniPercorsoSemplice(nome)
                    If Not String.IsNullOrEmpty(folderPath) AndAlso Directory.Exists(folderPath) Then
                        lista.aggiungiElemento(nome, folderPath)
                    End If
                Catch ex As Exception
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante l'aggiunta della cartella utente '{nome}' in sceltadrive.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
                End Try
            Next

            Dim cartelleCloud As List(Of Tuple(Of String, String)) = Nothing
            Try
                cartelleCloud = OttieniCartelleCloud()
            Catch ex As Exception
                log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante il recupero delle cartelle cloud in sceltadrive.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
                cartelleCloud = Nothing
            End Try

            If cartelleCloud IsNot Nothing Then
                For Each cloud In cartelleCloud
                    Try
                        lista.aggiungiElemento(cloud.Item1, cloud.Item2)
                    Catch ex As Exception
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore durante l'aggiunta della cartella cloud '{cloud.Item1}' in sceltadrive.{Environment.NewLine}Dettaglio: {ex.Message}")
                    End Try
                Next
            End If

            lista.opzioniPulisci()
            lista.aggiungiOpzione("Vai a", AddressOf VaiAPercorso)

        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in sceltadrive (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in sceltadrive (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in sceltadrive.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub VaiAPercorso()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci il percorso di destinazione", "c.percorso", True)
            AddHandler campi.Fine, AddressOf VaiAPercorsoFine
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in VaiAPercorso.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub VaiAPercorsoFine()
        Try
            Dim percorso_digitato As String = campi.RecuperaValore("c.percorso")
            campi = Nothing
            If Directory.Exists(percorso_digitato) Then
                CaricaDirectory(percorso_digitato)
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Il percorso inserito non è valido o non esiste.")
                messaggio.ShowDialog()
                sceltadrive()
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in VaiAPercorsoFine.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub CaricaDirectory(directory As String)
        Try
            If currentDirectory Is Nothing OrElse currentDirectory = "" Then
                currentDirectory = directory
            End If
            lista.pulisci()
            lista.impostaTitolo("Cartella: " & Path.GetFileName(currentDirectory))
            Dim directories() As String = OttieniDirectoryAccessibili(directory)
            Dim files() As String = OttieniFileAccessibili(directory)
            Array.Sort(directories)
            Array.Sort(files)
            For Each cartella In directories
                Try
                    Dim dirInfo As New DirectoryInfo(cartella)
                    lista.aggiungiElemento(dirInfo.Name, dirInfo.FullName)
                Catch ex As UnauthorizedAccessException
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta della cartella '{cartella}' in CaricaDirectory (UnauthorizedAccessException).{Environment.NewLine}Dettaglio: {ex.Message}")
                Catch ex As IOException
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta della cartella '{cartella}' in CaricaDirectory (IOException).{Environment.NewLine}Dettaglio: {ex.Message}")
                Catch ex As PathTooLongException
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta della cartella '{cartella}' in CaricaDirectory (PathTooLongException).{Environment.NewLine}Dettaglio: {ex.Message}")
                Catch ex As Exception
                    log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico durante l'aggiunta della cartella '{cartella}' in CaricaDirectory.{Environment.NewLine}Dettaglio: {ex.Message}")
                End Try
            Next

            If allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                For Each varfile In files
                    Try
                        Dim fileInfo As New FileInfo(varfile)
                        Dim extFile As String = fileInfo.Extension.ToLower()
                        If allowedExtensions.Contains(extFile) Then
                            lista.aggiungiElemento(fileInfo.Name, fileInfo.FullName)
                        End If
                    Catch ex As UnauthorizedAccessException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta del file '{varfile}' in CaricaDirectory (UnauthorizedAccessException).{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As IOException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta del file '{varfile}' in CaricaDirectory (IOException).{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As PathTooLongException
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico durante l'aggiunta del file '{varfile}' in CaricaDirectory (PathTooLongException).{Environment.NewLine}Dettaglio: {ex.Message}")
                    Catch ex As Exception
                        log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico durante l'aggiunta del file '{varfile}' in CaricaDirectory.{Environment.NewLine}Dettaglio: {ex.Message}")
                    End Try
                Next
            End If
            lista.opzioniPulisci()
            lista.aggiungiOpzione("Seleziona voce corrente", AddressOf Seleziona)
            lista.aggiungiOpzione("Crea cartella", AddressOf CreaCartella)

        Catch ex As ObjectDisposedException
            Return
        Catch ex As DirectoryNotFoundException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CaricaDirectory (DirectoryNotFoundException).{Environment.NewLine}Messaggio: La directory '{directory}' non è stata trovata.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As UnauthorizedAccessException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CaricaDirectory (UnauthorizedAccessException).{Environment.NewLine}Messaggio: Accesso negato alla directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CaricaDirectory (IOException).{Environment.NewLine}Messaggio: Errore I/O durante il caricamento della directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CaricaDirectory (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CaricaDirectory (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in CaricaDirectory.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub CreaCartella()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci nome nuova cartella", "c.cartella", True)
            AddHandler campi.Fine, AddressOf campifine
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in CreaCartella.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub campifine()
        Try
            Dim testo As String = campi.RecuperaValore("c.cartella")
            campi = Nothing

            If Not String.IsNullOrWhiteSpace(testo) Then
                Dim nuovoPercorso As String = Path.Combine(currentDirectory, testo)
                Dim creata As Boolean = False

                Try
                    creata = CreateDirectory(nuovoPercorso, IntPtr.Zero)
                Catch ex As Exception
                End Try

                If Not creata AndAlso Not Directory.Exists(nuovoPercorso) Then
                    Try
                        System.IO.Directory.CreateDirectory(nuovoPercorso)
                        creata = True
                    Catch ex As Exception
                    End Try
                End If

                If Not creata AndAlso Not Directory.Exists(nuovoPercorso) Then
                    Try
                        Dim argomentoCartella As String = PercorsoPerCmd(nuovoPercorso)
                        If argomentoCartella Is Nothing Then Exit Try
                        Dim p As New Process()
                        p.StartInfo.FileName = "cmd.exe"
                        p.StartInfo.Arguments = "/c mkdir " & argomentoCartella
                        p.StartInfo.UseShellExecute = False
                        p.StartInfo.CreateNoWindow = True
                        p.Start()
                        p.WaitForExit()
                        If Directory.Exists(nuovoPercorso) Then
                            creata = True
                        End If
                    Catch ex As Exception
                    End Try
                End If

                If Directory.Exists(nuovoPercorso) Then
                    CaricaDirectory(currentDirectory)
                Else
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Impossibile creare la cartella specificata.")
                    messaggio.ShowDialog()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in campifine.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub


    Private Function OttieniDirectoryAccessibili(directory As String) As String()
        Dim directories As New List(Of String)
        Dim searchPattern As String = "*"
        Dim hFind As IntPtr

        Dim findData As New WIN32_FIND_DATA()
        Try
            hFind = FindFirstFile(Path.Combine(directory, searchPattern), findData)
            If hFind <> New IntPtr(-1) Then
                Try
                    Do
                        If (findData.dwFileAttributes And FILE_ATTRIBUTE_DIRECTORY) = FILE_ATTRIBUTE_DIRECTORY Then
                            If findData.cFileName <> "." AndAlso findData.cFileName <> ".." Then
                                directories.Add(Path.Combine(directory, findData.cFileName))
                            End If
                        End If
                    Loop While FindNextFile(hFind, findData)
                Finally
                    FindClose(hFind)
                End Try
            Else
                Try
                    Return System.IO.Directory.GetDirectories(directory)
                Catch ex As Exception
                    Return OttieniDirectoryAccessibiliCmdFailover(directory)
                End Try
            End If
        Catch ex As Win32Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniDirectoryAccessibili (Win32Exception) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} (Codice: {ex.NativeErrorCode}) {ex.StackTrace}")
            Try
                Return System.IO.Directory.GetDirectories(directory)
            Catch failoverEx As Exception
                Return OttieniDirectoryAccessibiliCmdFailover(directory)
            End Try
        Catch ex As ArgumentException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniDirectoryAccessibili (ArgumentException) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Try
                Return System.IO.Directory.GetDirectories(directory)
            Catch failoverEx As Exception
                Return OttieniDirectoryAccessibiliCmdFailover(directory)
            End Try
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniDirectoryAccessibili (IOException) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Try
                Return System.IO.Directory.GetDirectories(directory)
            Catch failoverEx As Exception
                Return OttieniDirectoryAccessibiliCmdFailover(directory)
            End Try
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in OttieniDirectoryAccessibili per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Try
                Return System.IO.Directory.GetDirectories(directory)
            Catch failoverEx As Exception
                Return OttieniDirectoryAccessibiliCmdFailover(directory)
            End Try
        End Try

        Return directories.ToArray()
    End Function

    ' cmd tratta la barra prima della virgoletta di chiusura come escape: dir "C:\" non elenca la radice.
    ' Virgolette, &, | e i reindirizzamenti nel percorso rompono il comando o ne avviano un altro.
    Private Function PercorsoPerCmd(percorso As String) As String
        If String.IsNullOrWhiteSpace(percorso) Then Return Nothing
        Dim p As String = percorso
        If p.EndsWith("\"c) OrElse p.EndsWith("/"c) Then p &= "."
        For Each c As Char In p
            If c = """"c OrElse c = "&"c OrElse c = "|"c OrElse c = ">"c OrElse c = "<"c OrElse Char.IsControl(c) Then Return Nothing
        Next
        Return """" & p.Replace("%", "%%") & """"
    End Function

    Private Function OttieniDirectoryAccessibiliCmdFailover(directory As String) As String()
        Dim directories As New List(Of String)
        Try
            Dim argomento As String = PercorsoPerCmd(directory)
            If argomento Is Nothing Then Return directories.ToArray()
            Using p As New Process()
                p.StartInfo.FileName = "cmd.exe"
                p.StartInfo.Arguments = "/c dir /b /ad " & argomento
                p.StartInfo.UseShellExecute = False
                p.StartInfo.RedirectStandardOutput = True
                p.StartInfo.RedirectStandardError = True
                p.StartInfo.CreateNoWindow = True
                If Not p.Start() Then Return directories.ToArray()
                ' I due flussi vanno svuotati insieme: ReadToEnd sul solo stdout non ha un tempo massimo
                ' e, se stderr si riempie, dir resta bloccato e la finestra di scelta file non torna.
                Dim lettura As System.Threading.Tasks.Task(Of String) = p.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As System.Threading.Tasks.Task(Of String) = p.StandardError.ReadToEndAsync()
                If Not p.WaitForExit(15000) Then
                    Try
                        p.Kill()
                    Catch exArresto As Exception
                        log.ScriviLog("OttieniDirectoryAccessibiliCmdFailover arresto: " & exArresto.Message)
                    End Try
                    p.WaitForExit(2000)
                End If
                Dim output As String = ""
                Try
                    If lettura.Wait(2000) Then output = If(lettura.Result, "")
                    If letturaErrori.Wait(2000) AndAlso Not String.IsNullOrWhiteSpace(letturaErrori.Result) Then
                        log.ScriviLog("OttieniDirectoryAccessibiliCmdFailover stderr: " & letturaErrori.Result)
                    End If
                Catch exLettura As Exception
                    log.ScriviLog("OttieniDirectoryAccessibiliCmdFailover lettura: " & exLettura.Message)
                End Try

                Dim lines() As String = output.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                For Each line In lines
                    directories.Add(Path.Combine(directory, line))
                Next
            End Using
        Catch ex As Exception
        End Try
        Return directories.ToArray()
    End Function

    Private Function OttieniFileAccessibili(directory As String) As String()
        Dim files As New List(Of String)
        Dim searchPattern As String = "*"
        Dim hFind As IntPtr

        Dim findData As New WIN32_FIND_DATA()
        Try
            hFind = FindFirstFile(Path.Combine(directory, searchPattern), findData)
            If hFind <> New IntPtr(-1) Then
                Try
                    Do
                        If (findData.dwFileAttributes And FILE_ATTRIBUTE_DIRECTORY) <> FILE_ATTRIBUTE_DIRECTORY Then
                            If allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                                Dim extFile As String = Path.GetExtension(findData.cFileName)
                                If Not String.IsNullOrEmpty(extFile) AndAlso allowedExtensions.Contains(extFile.ToLower()) Then
                                    files.Add(Path.Combine(directory, findData.cFileName))
                                End If
                            Else
                                files.Add(Path.Combine(directory, findData.cFileName))
                            End If
                        End If
                    Loop While FindNextFile(hFind, findData)
                Finally
                    FindClose(hFind)
                End Try
            Else
                Return OttieniFileAccessibiliFailover(directory)
            End If
        Catch ex As Win32Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniFileAccessibili (Win32Exception) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} (Codice: {ex.NativeErrorCode}) {ex.StackTrace}")
            Return OttieniFileAccessibiliFailover(directory)
        Catch ex As ArgumentException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniFileAccessibili (ArgumentException) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return OttieniFileAccessibiliFailover(directory)
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in OttieniFileAccessibili (IOException) per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return OttieniFileAccessibiliFailover(directory)
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in OttieniFileAccessibili per directory '{directory}'.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return OttieniFileAccessibiliFailover(directory)
        End Try

        Return files.ToArray()
    End Function

    Private Function OttieniFileAccessibiliFailover(directory As String) As String()
        Dim files As New List(Of String)
        Try
            Dim allFiles = System.IO.Directory.GetFiles(directory)
            For Each f In allFiles
                If allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                    Dim extFile As String = Path.GetExtension(f)
                    If Not String.IsNullOrEmpty(extFile) AndAlso allowedExtensions.Contains(extFile.ToLower()) Then
                        files.Add(f)
                    End If
                Else
                    files.Add(f)
                End If
            Next
        Catch ex As Exception
            Return OttieniFileAccessibiliCmdFailover(directory)
        End Try
        Return files.ToArray()
    End Function

    Private Function OttieniFileAccessibiliCmdFailover(directory As String) As String()
        Dim files As New List(Of String)
        Try
            Dim argomento As String = PercorsoPerCmd(directory)
            If argomento Is Nothing Then Return files.ToArray()
            Using p As New Process()
                p.StartInfo.FileName = "cmd.exe"
                p.StartInfo.Arguments = "/c dir /b /a-d " & argomento
                p.StartInfo.UseShellExecute = False
                p.StartInfo.RedirectStandardOutput = True
                p.StartInfo.RedirectStandardError = True
                p.StartInfo.CreateNoWindow = True
                If Not p.Start() Then Return files.ToArray()
                Dim lettura As System.Threading.Tasks.Task(Of String) = p.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As System.Threading.Tasks.Task(Of String) = p.StandardError.ReadToEndAsync()
                If Not p.WaitForExit(15000) Then
                    Try
                        p.Kill()
                    Catch exArresto As Exception
                        log.ScriviLog("OttieniFileAccessibiliCmdFailover arresto: " & exArresto.Message)
                    End Try
                    p.WaitForExit(2000)
                End If
                Dim output As String = ""
                Try
                    If lettura.Wait(2000) Then output = If(lettura.Result, "")
                    If letturaErrori.Wait(2000) AndAlso Not String.IsNullOrWhiteSpace(letturaErrori.Result) Then
                        log.ScriviLog("OttieniFileAccessibiliCmdFailover stderr: " & letturaErrori.Result)
                    End If
                Catch exLettura As Exception
                    log.ScriviLog("OttieniFileAccessibiliCmdFailover lettura: " & exLettura.Message)
                End Try

                Dim lines() As String = output.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                For Each line In lines
                    If allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                        Dim extFile As String = Path.GetExtension(line)
                        If Not String.IsNullOrEmpty(extFile) AndAlso allowedExtensions.Contains(extFile.ToLower()) Then
                            files.Add(Path.Combine(directory, line))
                        End If
                    Else
                        files.Add(Path.Combine(directory, line))
                    End If
                Next
            End Using
        Catch ex As Exception
        End Try
        Return files.ToArray()
    End Function

    Private Sub Selezionato(e As String)
        Try
            CartellaSuccessiva(e)
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Selezionato (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Selezionato (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in Selezionato.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub CartellaSuccessiva(selectedPath As String)
        Try
            If My.Computer.FileSystem.DirectoryExists(selectedPath) Then
                If currentDirectory Is Nothing OrElse currentDirectory = "" OrElse EunaCartellaUtenteNota(currentDirectory) Then
                    history.Add(New NavigazioneStato("sceltadisco", lista.ottieniIndice()))
                    lista.multiselezioneConsentita = MultiselezioneConsentita
                Else
                    history.Add(New NavigazioneStato(currentDirectory, lista.ottieniIndice()))
                End If
                currentDirectory = selectedPath

                If allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                    CaricaDirectory(currentDirectory)
                Else
                    menu = New WGMenu()
                    menu.aggiungi("Vai alla cartella " & currentDirectory, AddressOf cartellasuc)
                    menu.aggiungi("Seleziona la cartella " & currentDirectory, AddressOf selezionacartella)
                    menu.avvia("Menu cosa fare", True)
                End If
            ElseIf allowedExtensions IsNot Nothing AndAlso allowedExtensions.Count > 0 Then
                Dim extFile As String = Path.GetExtension(selectedPath)
                If Not String.IsNullOrEmpty(extFile) AndAlso allowedExtensions.Contains(extFile.ToLower()) Then
                    If MultiselezioneConsentita = True Then
                        If Not selectedPaths.Contains(selectedPath) Then
                            selectedPaths.Add(selectedPath)
                        End If
                        RaiseEvent selezioneterminata(selectedPaths)
                        Me.dispose()
                    Else
                        selectedPaths.Clear()
                        selectedPaths.Add(selectedPath)
                        RaiseEvent selezioneterminata(selectedPaths)
                        Me.dispose()
                    End If
                End If
            Else
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As IOException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (IOException).{Environment.NewLine}Messaggio: {ex.Message}.{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As UnauthorizedAccessException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (UnauthorizedAccessException).{Environment.NewLine}Messaggio: {ex.Message}.{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As ArgumentException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (ArgumentException).{Environment.NewLine}Messaggio: {ex.Message}.{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As NotSupportedException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (NotSupportedException).{Environment.NewLine}Messaggio: {ex.Message}.{Environment.NewLine}Dettaglio: {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaSuccessiva (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in CartellaSuccessiva.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub selezionati(ids As List(Of String))
        Try
            If ids IsNot Nothing AndAlso ids.Count > 0 Then
                selectedPaths.Clear()
                selectedPaths.AddRange(ids)
                RaiseEvent selezioneterminata(selectedPaths)
            End If
            chiudi()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in selezionati (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in selezionati (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in selezionati.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub Navigazione(sender As Object, e As EventArgs)
        Try
            CartellaPrecedente()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Navigazione (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Navigazione (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in Navigazione.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub CartellaPrecedente()
        Try
            If history.Count > 0 Then
                Dim previousState As NavigazioneStato = history(history.Count - 1)
                history.RemoveAt(history.Count - 1)
                currentDirectory = previousState.Directory
                If currentDirectory = "sceltadisco" Then
                    lista.multiselezioneConsentita = False
                    sceltadrive()
                    lista.selezionaElemento(previousState.IndiceSelezionato)
                Else
                    CaricaDirectory(currentDirectory)
                    lista.selezionaElemento(previousState.IndiceSelezionato)
                End If
            Else
                chiudi()
                RaiseEvent annullata()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As ArgumentOutOfRangeException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaPrecedente (ArgumentOutOfRangeException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaPrecedente (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in CartellaPrecedente (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in CartellaPrecedente.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Private Sub Seleziona()
        Try
            If MultiselezioneConsentita Then
                Dim selectedElement As String = lista.ottieniElemento()
                If Not String.IsNullOrEmpty(selectedElement) Then
                    If Not selectedPaths.Contains(selectedElement) Then
                        selectedPaths.Add(selectedElement)
                    End If
                End If
            Else
                selectedPaths.Clear()
                selectedPaths.Add(lista.ottieniElemento())
                RaiseEvent selezioneterminata(selectedPaths)
                chiudi()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Seleziona (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in Seleziona (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in Seleziona.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
            End If
        End Try
    End Sub

    Public Function elencoestensioni() As List(Of String)
        Try
            Return allowedExtensions
        Catch ex As ObjectDisposedException
            Return New List(Of String)()
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in elencoestensioni.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            If Debugger.IsAttached() = True Then
                Throw
            Else
                errore = (ex.Message & ex.StackTrace)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
                messaggio.ShowDialog()
                Return New List(Of String)()
            End If
        End Try
    End Function

    Private Sub cartellasuc()
        Try
            lista.pulisci()
            CaricaDirectory(currentDirectory)
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in scelta (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in scelta (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in scelta.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub selezionacartella()
        Try
            selectedPaths.Clear()
            selectedPaths.Add(currentDirectory)
            RaiseEvent singolaselezione(currentDirectory)
            chiudi()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in scelta (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in scelta (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in scelta.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub chiudi()
        Try
            If lista IsNot Nothing Then
                RemoveHandler lista.ElementoSelezionato, AddressOf Selezionato
                RemoveHandler lista.ElementiSelezionati, AddressOf selezionati
                RemoveHandler lista.escPremuto, AddressOf Navigazione
                lista.abbandona()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in chiudi (InvalidOperationException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As NullReferenceException
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore specifico in chiudi (NullReferenceException).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore generico in chiudi.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        End Try
    End Sub

    Private Sub dispose()
        chiudi()
    End Sub

    Private Function EunaCartellaUtenteNota(path As String) As Boolean
        Try
            Dim cartelleUtente As String() = {"Desktop", "Documenti", "Immagini", "Musica", "Download"}
            For Each nome In cartelleUtente
                Dim p As String = OttieniPercorsoSemplice(nome)
                If String.Equals(p, path) Then
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in EunaCartellaUtenteNota.{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
            Return False
        End Try
    End Function

End Class