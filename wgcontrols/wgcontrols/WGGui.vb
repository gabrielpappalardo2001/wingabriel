' ============================================================================================
' WGGui - il database dell'interfaccia
' ============================================================================================
'
' Notifiche, suoni delle notifiche, sessione, file recenti, scrivania, home e ricerca nei menu tengono i loro
' dati in un solo database, dati\gui\gui.sqlite, una tabella per ciascuno. Prima ognuno aveva un file suo
' (notifiche.txt, sessione.txt, scrivania.db...): al primo avvio i dati di quei file si portano nella nuova
' tabella e i vecchi file si tolgono (niente copie di riserva accanto).
'
'   Dim d As WGDatabase = WGGui.Db()
'   WGGui.CreaTabella("Recenti", "Posizione INTEGER, Percorso TEXT")
'   WGGui.CreaTabella("HomeApplicazioni", "Nome TEXT, ...", "home", "home.db", "home")   ' con importazione
' ============================================================================================
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports WGDb

Public NotInheritable Class WGGui

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _db As WGDatabase

    Private Sub New()
    End Sub

    ''' <summary>Il database dell'interfaccia (uno solo per tutto il programma). Nothing se non si riesce ad aprirlo.</summary>
    Public Shared Function Db() As WGDatabase
        SyncLock _blocco
            Try
                If _db Is Nothing Then
                    Dim d As New WGDatabase("gui", "gui.sqlite")
                    If Not d.Valido Then
                        log.ScriviLog("WGGui: impossibile aprire il database dell'interfaccia")
                        Return Nothing
                    End If
                    _db = d
                End If
            Catch ex As Exception
                log.ScriviLog("WGGui.Db: " & ex.Message)
            End Try
            Return _db
        End SyncLock
    End Function

    ''' <summary>
    ''' Crea la tabella se manca. Con vecchioFile indicato, se la tabella è vuota e il vecchio database esiste ne importa
    ''' i dati (stesse colonne, stessi identificatori) e lo toglie.
    ''' </summary>
    Public Shared Function CreaTabella(tabella As String, definizione As String, Optional vecchiaCartella As String = "", Optional vecchioFile As String = "", Optional vecchiaTabella As String = "") As WGDatabase
        Dim d As WGDatabase = Db()
        If d Is Nothing Then Return Nothing
        SyncLock _blocco
            Try
                d.CreaTabellaSeNonEsiste(tabella, definizione)
                If vecchioFile <> "" Then Importa(d, tabella, vecchiaCartella, vecchioFile, If(vecchiaTabella = "", tabella, vecchiaTabella))
            Catch ex As Exception
                log.ScriviLog("WGGui.CreaTabella " & tabella & ": " & ex.Message)
            End Try
        End SyncLock
        Return d
    End Function

    Private Shared Sub Importa(d As WGDatabase, tabella As String, vecchiaCartella As String, vecchioFile As String, vecchiaTabella As String)
        Try
            Dim percorso As String = WGDatabase.PercorsoFileDati(vecchiaCartella, vecchioFile)
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
            Dim contate As Object = d.EseguiScalare("SELECT COUNT(*) FROM " & tabella & ";")
            If contate IsNot Nothing AndAlso Not IsDBNull(contate) AndAlso Convert.ToInt64(contate) > 0 Then Return

            d.EseguiNonQuery("ATTACH DATABASE '" & percorso.Replace("'", "''") & "' AS vecchio;")
            Dim importate As Boolean = False
            Try
                Dim nuove As New List(Of String)()
                For Each riga As DataRow In d.EseguiQuery("PRAGMA main.table_info(" & tabella & ");").Rows
                    nuove.Add(Convert.ToString(riga("name")))
                Next
                Dim comuni As New List(Of String)()
                For Each riga As DataRow In d.EseguiQuery("PRAGMA vecchio.table_info(" & vecchiaTabella & ");").Rows
                    Dim nome As String = Convert.ToString(riga("name"))
                    If nuove.Contains(nome, StringComparer.OrdinalIgnoreCase) Then comuni.Add(nome)
                Next
                If comuni.Count > 0 Then
                    Dim colonne As String = String.Join(", ", comuni)
                    d.EseguiNonQuery("INSERT OR IGNORE INTO main." & tabella & " (" & colonne & ") SELECT " & colonne & " FROM vecchio." & vecchiaTabella & ";")
                    importate = True
                End If
            Finally
                d.EseguiNonQuery("DETACH DATABASE vecchio;")
            End Try
            If importate Then MettiDaParte(percorso)
        Catch ex As Exception
            log.ScriviLog("WGGui.Importa " & tabella & ": " & ex.Message)
        End Try
    End Sub

    ' Toglie un vecchio file già importato (e gli eventuali file accanto del database): i dati stanno ormai nel database.
    Public Shared Sub MettiDaParte(percorso As String)
        For Each suffisso As String In {"", "-wal", "-shm"}
            Try
                Dim file As String = percorso & suffisso
                If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
            Catch ex As Exception
                log.ScriviLog("WGGui.MettiDaParte: " & ex.Message)
            End Try
        Next
    End Sub

    Private Shared ReadOnly _elenchiPronti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' Crea una tabella per un elenco di righe di testo (Posizione, colonna) e, la prima volta, vi porta le righe del vecchio
    ''' file di testo, se c'è. Si può chiamare a ogni uso: lavora una volta sola.
    ''' </summary>
    Public Shared Sub PreparaElenco(tabella As String, colonna As String, Optional vecchiaCartella As String = "", Optional vecchioTesto As String = "")
        Try
            SyncLock _blocco
                If _elenchiPronti.Contains(tabella) Then Return
                Dim d As WGDatabase = CreaTabella(tabella, "Posizione INTEGER, " & colonna & " TEXT")
                If d Is Nothing Then Return
                _elenchiPronti.Add(tabella)
                If vecchioTesto = "" Then Return
                Dim percorso As String = WGDatabase.PercorsoFileDati(vecchiaCartella, vecchioTesto)
                If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
                Dim contate As Object = d.EseguiScalare("SELECT COUNT(*) FROM " & tabella & ";")
                If contate IsNot Nothing AndAlso Not IsDBNull(contate) AndAlso Convert.ToInt64(contate) > 0 Then Return
                ScriviElenco(tabella, colonna, File.ReadAllLines(percorso, System.Text.Encoding.UTF8).Where(Function(r) Not String.IsNullOrWhiteSpace(r)))
                MettiDaParte(percorso)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGGui.PreparaElenco " & tabella & ": " & ex.Message)
        End Try
    End Sub

    ''' <summary>Le righe di testo di una tabella a due colonne ordinate (Posizione, Testo), nell'ordine.</summary>
    Public Shared Function LeggiElenco(tabella As String, colonnaTesto As String) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return risultato
            For Each riga As DataRow In d.EseguiQuery("SELECT " & colonnaTesto & " FROM " & tabella & " ORDER BY Posizione;").Rows
                If Not IsDBNull(riga(0)) Then risultato.Add(Convert.ToString(riga(0)))
            Next
        Catch ex As Exception
            log.ScriviLog("WGGui.LeggiElenco " & tabella & ": " & ex.Message)
        End Try
        Return risultato
    End Function

    ''' <summary>Riscrive l'elenco di una tabella (Posizione, colonnaTesto) in un'unica operazione.</summary>
    Public Shared Sub ScriviElenco(tabella As String, colonnaTesto As String, righe As IEnumerable(Of String))
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return
            SyncLock _blocco
                d.EseguiNonQuery("BEGIN;")
                Try
                    d.EseguiNonQuery("DELETE FROM " & tabella & ";")
                    Dim posizione As Integer = 0
                    For Each riga As String In righe
                        d.InserisciRecord(tabella, New Dictionary(Of String, Object) From {{"Posizione", posizione}, {colonnaTesto, riga}})
                        posizione += 1
                    Next
                    d.EseguiNonQuery("COMMIT;")
                Catch
                    d.EseguiNonQuery("ROLLBACK;")
                    Throw
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGGui.ScriviElenco " & tabella & ": " & ex.Message)
        End Try
    End Sub

End Class
