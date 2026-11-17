Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports WGConnessioni
Imports WGContatti

' Chiamata dei contatti dalla rubrica. Usa MenuTelefono, che verifica il telefono collegato e,
' se il contatto ha piu' numeri, fa scegliere quale chiamare.
Partial Public Class MenuRubrica

    Private _menuTelefono As MenuTelefono

    ''' <summary>
    ''' MenuTelefono usato per le chiamate. Se l'applicazione ne ha gia' uno (cosi' c'e' una sola
    ''' sessione col telefono e un solo menu di chiamata) lo si assegna qui; altrimenti ne viene
    ''' creato uno al primo utilizzo.
    ''' </summary>
    Public Property Telefono As MenuTelefono
        Get
            If _menuTelefono Is Nothing Then _menuTelefono = New MenuTelefono()
            Return _menuTelefono
        End Get
        Set(value As MenuTelefono)
            _menuTelefono = value
        End Set
    End Property

    ' Dal dettaglio del contatto.
    Private Sub OpzioneChiama(sender As Object, e As EventArgs)
        Try
            ChiamaContatto(_chiaveContattoCorrente, _rigaCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneChiama: " & ex.Message)
        End Try
    End Sub

    ' Dall'elenco dei contatti: chiama quello evidenziato.
    Private Sub OpzioneChiamaDaLista(sender As Object, e As EventArgs)
        Try
            If _listaContatti Is Nothing Then Return
            Dim selezione As List(Of String) = _listaContatti.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 OrElse String.IsNullOrWhiteSpace(selezione(0)) Then
                MostraMessaggio("Seleziona un contatto.")
                Return
            End If
            ChiamaContatto(selezione(0), Nothing)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneChiamaDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiamaContatto(chiave As String, riga As DataRow)
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return
            If riga Is Nothing Then riga = _contatti.OttieniContatto(chiave)
            If riga Is Nothing Then
                MostraMessaggio("Contatto non trovato.")
                Return
            End If
            Dim nome As String = (riga("Nome").ToString() & " " & riga("Cognome").ToString()).Trim()
            If String.IsNullOrWhiteSpace(nome) Then nome = "Contatto"
            Telefono.ChiamaContatto(nome, RaccogliNumeriContatto(chiave, riga))
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiamaContatto: " & ex.Message)
            MostraMessaggio("Non è stato possibile avviare la chiamata.")
        End Try
    End Sub

    ' Cellulari e telefoni del contatto (anche quelli multipli), senza duplicati.
    Private Function RaccogliNumeriContatto(chiave As String, riga As DataRow) As List(Of NumeroContatto)
        Dim risultato As New List(Of NumeroContatto)()
        Try
            Dim recapiti As Dictionary(Of String, List(Of String)) = _contatti.OttieniTuttiIRecapiti(chiave)
            AggiungiNumeriContatto(risultato, recapiti, CLSContatti.CategoriaRecapitoCellulare, "Cellulare", ValoreColonna(riga, "Cellulare"))
            AggiungiNumeriContatto(risultato, recapiti, CLSContatti.CategoriaRecapitoTelefono, "Telefono", ValoreColonna(riga, "Telefono"))
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RaccogliNumeriContatto: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Sub AggiungiNumeriContatto(destinazione As List(Of NumeroContatto), recapiti As Dictionary(Of String, List(Of String)), categoria As String, etichetta As String, valorePrincipale As String)
        Try
            Dim numeri As New List(Of String)()
            If recapiti IsNot Nothing AndAlso recapiti.ContainsKey(categoria) AndAlso recapiti(categoria).Count > 0 Then
                numeri.AddRange(recapiti(categoria))
            ElseIf Not String.IsNullOrWhiteSpace(valorePrincipale) Then
                numeri.Add(valorePrincipale)
            End If
            For Each numero As String In numeri
                If String.IsNullOrWhiteSpace(numero) Then Continue For
                Dim n As String = numero.Trim()
                If destinazione.Any(Function(x) StessoNumero(x.Numero, n)) Then Continue For
                Dim voce As New NumeroContatto()
                voce.Etichetta = etichetta
                voce.Numero = n
                destinazione.Add(voce)
            Next
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AggiungiNumeriContatto: " & ex.Message)
        End Try
    End Sub

    Private Function ValoreColonna(riga As DataRow, colonna As String) As String
        Try
            If riga IsNot Nothing AndAlso riga.Table.Columns.Contains(colonna) AndAlso Not riga.IsNull(colonna) Then Return riga(colonna).ToString()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ValoreColonna: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Function CifreDelNumero(numero As String) As String
        Return New String(If(numero, "").Where(Function(c) Char.IsDigit(c)).ToArray())
    End Function

    ' Stesso numero anche se uno ha il prefisso internazionale e l'altro no.
    Private Function StessoNumero(a As String, b As String) As Boolean
        Dim x As String = CifreDelNumero(a)
        Dim y As String = CifreDelNumero(b)
        If x.Length = 0 OrElse y.Length = 0 Then Return False
        If String.Equals(x, y, StringComparison.Ordinal) Then Return True
        If x.Length >= 7 AndAlso y.Length >= 7 Then Return x.EndsWith(y, StringComparison.Ordinal) OrElse y.EndsWith(x, StringComparison.Ordinal)
        Return False
    End Function

End Class