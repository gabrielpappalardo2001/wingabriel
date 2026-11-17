' ============================================================================================
' WGRecenti - gli ultimi file e cartelle aperti con WinGabriel
' ============================================================================================
'
' La gestione file e la scrivania registrano ogni file o cartella aperti; la scrivania li mostra
' nell'elemento fisso "Recenti" e la ricerca globale li trova per nome. Si tengono gli ultimi 30,
' dal più recente, in dati\gui\gui.sqlite; i percorsi che non esistono più vengono saltati.
'
'   WGRecenti.Registra(percorso)
'   For Each p As String In WGRecenti.Elenco() ...
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports WGDb

Public NotInheritable Class WGRecenti

    Private Const Massimo As Integer = 30
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _blocco As New Object()

    Private Sub New()
    End Sub

    Private Const Tabella As String = "Recenti"

    Private Shared Function Leggi() As List(Of String)
        Try
            WGGui.PreparaElenco(Tabella, "Percorso", "sistema", "recenti.txt")
            Return WGGui.LeggiElenco(Tabella, "Percorso").Where(Function(r) Not String.IsNullOrWhiteSpace(r)).ToList()
        Catch ex As Exception
            log.ScriviLog("WGRecenti.Leggi: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>Segna il file o la cartella come appena aperti.</summary>
    Public Shared Sub Registra(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            If Not File.Exists(percorso) AndAlso Not Directory.Exists(percorso) Then Return
            SyncLock _blocco
                Dim elenco As List(Of String) = Leggi()
                elenco.RemoveAll(Function(p) String.Equals(p, percorso, StringComparison.OrdinalIgnoreCase))
                elenco.Insert(0, percorso)
                If elenco.Count > Massimo Then elenco = elenco.Take(Massimo).ToList()
                WGGui.ScriviElenco(Tabella, "Percorso", elenco)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRecenti.Registra: " & ex.Message)
        End Try
    End Sub

    ''' <summary>I file e le cartelle recenti ancora esistenti, dal più recente.</summary>
    Public Shared Function Elenco() As List(Of String)
        SyncLock _blocco
            Return Leggi().Where(AddressOf Esiste).ToList()
        End SyncLock
    End Function

    ' Controllare un percorso di rete irraggiungibile può bloccare per decine di secondi: quelli si
    ' tengono senza controllo (aprendoli, se non ci sono, lo si dice). Chiavette e dischi staccati
    ' si saltano subito, senza toccarli.
    Private Shared Function Esiste(percorso As String) As Boolean
        Try
            If percorso.StartsWith("\\", StringComparison.Ordinal) Then Return True
            Dim radice As String = Path.GetPathRoot(percorso)
            If String.IsNullOrEmpty(radice) Then Return False
            Dim disco As New DriveInfo(radice)
            If disco.DriveType = DriveType.Network Then Return True
            If Not disco.IsReady Then Return False
            Return File.Exists(percorso) OrElse Directory.Exists(percorso)
        Catch
            Return False
        End Try
    End Function

    Public Shared Sub Svuota()
        Try
            SyncLock _blocco
                WGGui.PreparaElenco(Tabella, "Percorso", "sistema", "recenti.txt")
                WGGui.ScriviElenco(Tabella, "Percorso", New List(Of String)())
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRecenti.Svuota: " & ex.Message)
        End Try
    End Sub

End Class
