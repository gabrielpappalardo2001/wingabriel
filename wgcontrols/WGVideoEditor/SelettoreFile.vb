' ============================================================================================
' SelettoreFile - la scelta di un file con il selettore di WinGabriel
' ============================================================================================
'
' Parte dalla scelta delle risorse (le unità del computer), senza una cartella di partenza, e non ricorda niente.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.IO

Public NotInheritable Class SelettoreFile

    Private ReadOnly _log As New Logger()
    Private _dialogo As WGFileDialog
    Private _alTermine As Action(Of String)

    ''' <summary>Apre il selettore; alTermine riceve il file scelto e non viene chiamato se si annulla.</summary>
    Public Sub Scegli(estensioni As IEnumerable(Of String), alTermine As Action(Of String))
        Try
            _alTermine = alTermine
            _dialogo = New WGFileDialog()
            _dialogo.MultiselezioneConsentita = False
            For Each estensione As String In estensioni
                _dialogo.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogo.selezioneterminata, AddressOf Terminata
            AddHandler _dialogo.annullata, AddressOf Annullata
            _dialogo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SelettoreFile.Scegli: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Scollega()
        Try
            If _dialogo IsNot Nothing Then
                RemoveHandler _dialogo.selezioneterminata, AddressOf Terminata
                RemoveHandler _dialogo.annullata, AddressOf Annullata
            End If
        Catch ex As Exception
            _log.ScriviLog("SelettoreFile.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub Annullata()
        Scollega()
    End Sub

    Private Sub Terminata(elementi As List(Of String))
        Try
            Scollega()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            If _alTermine IsNot Nothing Then AiutoMusica.SuInterfaccia(Sub() _alTermine(percorso))
        Catch ex As Exception
            _log.ScriviLog("SelettoreFile.Terminata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared ReadOnly EstensioniVideo() As String = {".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".ts", ".3gp", ".flv"}
    Public Shared ReadOnly EstensioniAudio() As String = {".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".aif", ".aiff", ".mp2"}

End Class
