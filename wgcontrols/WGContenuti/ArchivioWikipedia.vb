Imports System.Collections.Generic
Imports System.Data
Imports wgcontrols
Imports WGDb

''' <summary>Una voce dell'archivio: una ricerca (Testo) o una pagina (Testo = titolo, PaginaId).</summary>
Public Class VoceArchivioWikipedia
    Public Property Chiave As String = ""
    Public Property Testo As String = ""
    Public Property PaginaId As String = ""
End Class

''' <summary>Ricerche e pagine di Wikipedia, recenti e preferite, salvate nel database di WinGabriel.</summary>
Public NotInheritable Class ArchivioWikipedia
    Public Const RicercheRecenti As String = "RicercheRecenti"
    Public Const RicerchePreferite As String = "RicerchePreferite"
    Public Const PagineRecenti As String = "PagineRecenti"
    Public Const PaginePreferite As String = "PaginePreferite"

    Private Const MassimoRecenti As Integer = 50

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _istanza As ArchivioWikipedia
    Private _db As WGDatabase

    Public Shared ReadOnly Property Istanza As ArchivioWikipedia
        Get
            SyncLock _blocco
                If _istanza Is Nothing Then _istanza = New ArchivioWikipedia()
                Return _istanza
            End SyncLock
        End Get
    End Property

    Private Sub New()
        Try
            _db = New WGDatabase("wikipedia", "wikipedia.sqlite")
            If Not _db.Valido Then
                _log.ScriviLog("ArchivioWikipedia: impossibile aprire il database")
                Return
            End If
            _db.CreaTabellaSeNonEsiste(RicercheRecenti, "Testo TEXT, Data TEXT")
            _db.CreaTabellaSeNonEsiste(RicerchePreferite, "Testo TEXT, Data TEXT")
            _db.CreaTabellaSeNonEsiste(PagineRecenti, "PaginaId TEXT, Testo TEXT, Data TEXT")
            _db.CreaTabellaSeNonEsiste(PaginePreferite, "PaginaId TEXT, Testo TEXT, Data TEXT")
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.New: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function ETabellaDiPagine(tabella As String) As Boolean
        Return tabella = PagineRecenti OrElse tabella = PaginePreferite
    End Function

    Private Function Pronto() As Boolean
        Return _db IsNot Nothing AndAlso _db.Valido
    End Function

    ''' <summary>Aggiunge una voce; se c'è già, la riporta in cima. Restituisce False se era già presente e si tratta dei preferiti.</summary>
    Public Function Aggiungi(tabella As String, testo As String, Optional paginaId As String = "") As Boolean
        Try
            If Not Pronto() OrElse String.IsNullOrWhiteSpace(testo) Then Return False
            Dim recenti As Boolean = tabella = RicercheRecenti OrElse tabella = PagineRecenti
            Dim presente As VoceArchivioWikipedia = Trova(tabella, testo, paginaId)
            If presente IsNot Nothing Then
                If Not recenti Then Return False
                _db.EliminaRecord(tabella, presente.Chiave)
            End If
            Dim valori As New Dictionary(Of String, Object) From {{"Testo", testo.Trim()}, {"Data", WGDatabase.DataOraCorrente()}}
            If ETabellaDiPagine(tabella) Then valori("PaginaId") = paginaId
            _db.InserisciRecord(tabella, valori)
            If recenti Then PotaRecenti(tabella)
            Return True
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.Aggiungi: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub PotaRecenti(tabella As String)
        Try
            Dim voci As List(Of VoceArchivioWikipedia) = Elenco(tabella)
            For i As Integer = MassimoRecenti To voci.Count - 1
                _db.EliminaRecord(tabella, voci(i).Chiave)
            Next
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.PotaRecenti: " & ex.Message)
        End Try
    End Sub

    Public Function Trova(tabella As String, testo As String, Optional paginaId As String = "") As VoceArchivioWikipedia
        For Each voce As VoceArchivioWikipedia In Elenco(tabella)
            If ETabellaDiPagine(tabella) Then
                If voce.PaginaId = paginaId Then Return voce
            ElseIf String.Equals(voce.Testo, testo.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return voce
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>Le voci della tabella, le recenti dalla più nuova, i preferiti in ordine alfabetico.</summary>
    Public Function Elenco(tabella As String) As List(Of VoceArchivioWikipedia)
        Dim risultato As New List(Of VoceArchivioWikipedia)()
        Try
            If Not Pronto() Then Return risultato
            Dim ordine As String = If(tabella = RicercheRecenti OrElse tabella = PagineRecenti, "Data DESC, " & WGDatabase.NomeColonnaChiavePredefinita & " DESC", "Testo COLLATE NOCASE")
            Dim dati As DataTable = _db.OttieniTuttiIRecord(tabella, ordine)
            For Each riga As DataRow In dati.Rows
                Dim voce As New VoceArchivioWikipedia()
                voce.Chiave = Valore(riga, WGDatabase.NomeColonnaChiavePredefinita)
                voce.Testo = Valore(riga, "Testo")
                If ETabellaDiPagine(tabella) Then voce.PaginaId = Valore(riga, "PaginaId")
                risultato.Add(voce)
            Next
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.Elenco: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function Valore(riga As DataRow, colonna As String) As String
        If Not riga.Table.Columns.Contains(colonna) OrElse IsDBNull(riga(colonna)) Then Return ""
        Return CStr(riga(colonna))
    End Function

    Public Sub Elimina(tabella As String, chiave As String)
        Try
            If Pronto() Then _db.EliminaRecord(tabella, chiave)
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.Elimina: " & ex.Message)
        End Try
    End Sub

    Public Sub Svuota(tabella As String)
        Try
            If Pronto() Then _db.EseguiNonQuery("DELETE FROM " & tabella & ";")
        Catch ex As Exception
            _log.ScriviLog("ArchivioWikipedia.Svuota: " & ex.Message)
        End Try
    End Sub
End Class
