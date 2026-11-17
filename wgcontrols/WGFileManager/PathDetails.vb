Imports System.IO
Imports System.Threading

' Analisi dettagliata di percorsi di file, cartelle e unità disco, con calcolo delle dimensioni e delle statistiche di utilizzo.
Public Class PathDetails
    Private _fileSystemInfo As FileSystemInfo
    Private _driveInfo As DriveInfo
    Private _isDirectory As Boolean
    Private _isDrive As Boolean
    Private _sizeInBytes As Long = 0
    Private _fileCount As Long = 0
    Private _subFolderCount As Long = 0
    Private _statsCalculated As Boolean = False
    Private _isShare As Boolean = False
    Private _shareReady As Boolean = False
    Private _shareTotal As Long = 0
    Private _shareFree As Long = 0

    Public Sub New(path As String)
        Try
            Dim percorsoPulito As String = path.Trim()
            If WGRete.IsUncPath(percorsoPulito) AndAlso WGRete.IsShareRoot(percorsoPulito) Then
                If Not WGRete.EsisteDirectory(percorsoPulito, 10000) Then
                    Throw New IOException($"Il percorso specificato non esiste: {path}")
                End If
                _isShare = True
                _isDrive = True
                _isDirectory = True
                _fileSystemInfo = New DirectoryInfo(percorsoPulito.TrimEnd("\"c))
                Dim spazio As WGRete.InfoSpazio = WGRete.LeggiSpazio(percorsoPulito, 10000)
                _shareReady = spazio.Disponibile
                _shareTotal = spazio.Totale
                _shareFree = spazio.Libero
                Return
            End If

            ' Controlla se il percorso è la root di un disco (es. "C:\")
            Dim rootPath As String = IO.Path.GetPathRoot(path)
            If Not String.IsNullOrEmpty(rootPath) AndAlso path.Trim().Equals(rootPath, StringComparison.OrdinalIgnoreCase) Then
                ' Verifica che il disco esista effettivamente nel sistema
                Dim drives = DriveInfo.GetDrives()
                For Each d In drives
                    If d.Name.Equals(rootPath, StringComparison.OrdinalIgnoreCase) Then
                        _isDrive = True
                        _isDirectory = True ' Un disco è trattato anche come directory per la lettura dei file
                        _driveInfo = d
                        _fileSystemInfo = New DirectoryInfo(path)
                        Exit For
                    End If
                Next
            End If

            ' Se non è un disco (o non è stato trovato tra i dischi), controlla file o directory standard
            If Not _isDrive Then
                If WGRete.EsisteFile(path, 10000) Then
                    _isDirectory = False
                    _isDrive = False
                    _fileSystemInfo = New FileInfo(path)
                ElseIf WGRete.EsisteDirectory(path, 10000) Then
                    _isDirectory = True
                    _isDrive = False
                    _fileSystemInfo = New DirectoryInfo(path)
                Else
                    Throw New IOException($"Il percorso specificato non esiste: {path}")
                End If
            End If
        Catch ex As Exception
            Throw
        End Try
    End Sub

    Public ReadOnly Property Name As String
        Get
            Try
                Return _fileSystemInfo.Name
            Catch ex As Exception
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property FullPath As String
        Get
            Try
                Return _fileSystemInfo.FullName
            Catch ex As Exception
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property LastModified As Date
        Get
            Try
                Return _fileSystemInfo.LastWriteTime
            Catch ex As Exception
                Return Date.MinValue
            End Try
        End Get
    End Property

    Public ReadOnly Property CreationTime As Date
        Get
            Try
                Return _fileSystemInfo.CreationTime
            Catch ex As Exception
                Return Date.MinValue
            End Try
        End Get
    End Property

    Public ReadOnly Property IsDirectory As Boolean
        Get
            Try
                Return _isDirectory
            Catch ex As Exception
                Return False
            End Try
        End Get
    End Property

    ' --- NUOVE PROPRIETÀ PER I DISCHI ---

    Public ReadOnly Property IsDrive As Boolean
        Get
            Return _isDrive
        End Get
    End Property

    Public ReadOnly Property IsNetworkShare As Boolean
        Get
            Return _isShare
        End Get
    End Property

    Public ReadOnly Property NetworkPath As String
        Get
            Try
                If _fileSystemInfo Is Nothing Then Return String.Empty
                Return WGRete.RisolviUnc(_fileSystemInfo.FullName)
            Catch ex As Exception
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property DriveIsReady As Boolean
        Get
            Try
                If _isShare Then Return _shareReady
                If _isDrive Then Return _driveInfo.IsReady
                Return False
            Catch ex As Exception
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property DriveFormat As String
        Get
            Try
                If _isShare Then Return String.Empty
                If _isDrive AndAlso _driveInfo.IsReady Then Return _driveInfo.DriveFormat
                Return String.Empty
            Catch ex As Exception
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property DriveType As DriveType
        Get
            Try
                If _isShare Then Return DriveType.Network
                If _isDrive Then Return _driveInfo.DriveType
                Return DriveType.Unknown
            Catch ex As Exception
                Return DriveType.Unknown
            End Try
        End Get
    End Property

    Public ReadOnly Property VolumeLabel As String
        Get
            Try
                If _isShare Then Return String.Empty
                If _isDrive AndAlso _driveInfo.IsReady Then Return _driveInfo.VolumeLabel
                Return String.Empty
            Catch ex As Exception
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property TotalDriveSize As Long
        Get
            Try
                If _isShare Then Return _shareTotal
                If _isDrive AndAlso _driveInfo.IsReady Then Return _driveInfo.TotalSize
                Return 0
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property TotalFreeSpace As Long
        Get
            Try
                If _isShare Then Return _shareFree
                If _isDrive AndAlso _driveInfo.IsReady Then Return _driveInfo.TotalFreeSpace
                Return 0
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property AvailableFreeSpace As Long
        Get
            Try
                If _isShare Then Return _shareFree
                If _isDrive AndAlso _driveInfo.IsReady Then Return _driveInfo.AvailableFreeSpace
                Return 0
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    ' --- FINE PROPRIETÀ DISCHI ---

    Public ReadOnly Property SizeInBytes As Long
        Get
            Try
                If Not _statsCalculated Then
                    CalcolaStatistiche()
                End If
                Return Interlocked.Read(_sizeInBytes)
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property FileCount As Long
        Get
            Try
                If Not _statsCalculated Then
                    CalcolaStatistiche()
                End If
                Return Interlocked.Read(_fileCount)
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property DirectoryCount As Long
        Get
            Try
                If Not _statsCalculated Then
                    CalcolaStatistiche()
                End If
                Return Interlocked.Read(_subFolderCount)
            Catch ex As Exception
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property FormattedSize As String
        Get
            Try
                Dim bytes As Long = Me.SizeInBytes
                If bytes = 0 Then Return "0 B"
                Dim sizeNames() As String = {"B", "KB", "MB", "GB", "TB"}
                Dim i As Integer = CInt(Math.Floor(Math.Log(bytes) / Math.Log(1024)))
                Dim sizeReadable As Double = Math.Round(bytes / Math.Pow(1024, i), 2)
                Return $"{sizeReadable} {sizeNames(i)}"
            Catch ex As Exception
                Return "0 B"
            End Try
        End Get
    End Property

    Public ReadOnly Property FormattedTotalDriveSize As String
        Get
            Try
                If Not _isDrive Then Return "0 B"
                Dim bytes As Long = Me.TotalDriveSize
                If bytes = 0 Then Return "0 B"
                Dim sizeNames() As String = {"B", "KB", "MB", "GB", "TB"}
                Dim i As Integer = CInt(Math.Floor(Math.Log(bytes) / Math.Log(1024)))
                Dim sizeReadable As Double = Math.Round(bytes / Math.Pow(1024, i), 2)
                Return $"{sizeReadable} {sizeNames(i)}"
            Catch ex As Exception
                Return "0 B"
            End Try
        End Get
    End Property

    Public ReadOnly Property FormattedFreeSpace As String
        Get
            Try
                If Not _isDrive Then Return "0 B"
                Dim bytes As Long = Me.TotalFreeSpace
                If bytes = 0 Then Return "0 B"
                Dim sizeNames() As String = {"B", "KB", "MB", "GB", "TB"}
                Dim i As Integer = CInt(Math.Floor(Math.Log(bytes) / Math.Log(1024)))
                Dim sizeReadable As Double = Math.Round(bytes / Math.Pow(1024, i), 2)
                Return $"{sizeReadable} {sizeNames(i)}"
            Catch ex As Exception
                Return "0 B"
            End Try
        End Get
    End Property

    Private Sub CalcolaStatistiche()
        If _statsCalculated Then Return
        _sizeInBytes = 0
        _fileCount = 0
        _subFolderCount = 0

        If _isDirectory Then
            Try
                CalcolaParallelo(New DirectoryInfo(_fileSystemInfo.FullName))
            Catch
            End Try
        Else
            _sizeInBytes = DirectCast(_fileSystemInfo, FileInfo).Length
        End If
        _statsCalculated = True
    End Sub

    Private Sub CalcolaParallelo(root As DirectoryInfo)
        Try
            Dim files = root.GetFiles()
            Interlocked.Add(_fileCount, files.Length)
            For Each f In files
                Interlocked.Add(_sizeInBytes, f.Length)
            Next
        Catch
        End Try

        Try
            Dim subDirs = root.GetDirectories()
            Interlocked.Add(_subFolderCount, subDirs.Length)

            Parallel.ForEach(subDirs, Sub(d)
                                          CalcolaParallelo(d)
                                      End Sub)
        Catch
        End Try
    End Sub
End Class