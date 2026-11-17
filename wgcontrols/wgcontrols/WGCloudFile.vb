Imports System
Imports System.IO
Imports System.Runtime.InteropServices
Imports wgcontrols

' Gestione dei file sul cloud, verifica degli attributi e funzioni per assicurare il download dei file.
Public Class WGCloudFile

    Private Const FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS As Integer = &H400000
    Private Const INVALID_FILE_ATTRIBUTES As Integer = -1
    Private Const DIMENSIONE_BUFFER As Integer = 1048576

    Private Shared ReadOnly _logger As New Logger()

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetFileAttributesW(lpFileName As String) As Integer
    End Function

    Public Shared Sub AssicuraFileScaricato(percorsoFile As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) Then Return
            If Not File.Exists(percorsoFile) Then Return

            Dim attributi As Integer = GetFileAttributesW(percorsoFile)
            If attributi = INVALID_FILE_ATTRIBUTES Then Return
            If (attributi And FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) = 0 Then Return

            ScaricaFileCompleto(percorsoFile)
        Catch ex As Exception
            _logger.ScriviLog("WGCloudFile.AssicuraFileScaricato: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScaricaFileCompleto(percorsoFile As String)
        Try
            Using flusso As New FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, DIMENSIONE_BUFFER, FileOptions.SequentialScan)
                Dim buffer(DIMENSIONE_BUFFER - 1) As Byte
                Dim lettiBytes As Integer
                Do
                    lettiBytes = flusso.Read(buffer, 0, buffer.Length)
                Loop While lettiBytes > 0
            End Using
        Catch ex As Exception
            _logger.ScriviLog("WGCloudFile.ScaricaFileCompleto: " & ex.ToString())
        End Try
    End Sub

End Class