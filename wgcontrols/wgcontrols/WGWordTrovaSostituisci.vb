' Funzionalità di ricerca e sostituzione del testo all'interno del documento.
Partial Public Class WGWord
    Private _ultimoTestoCercato As String = ""
    Private _campiTrovaSostituisci As Campi

    Public Sub MostraMenuTrovaSostituisci()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Trova", AddressOf trovaMenu)
            If Not SolaLettura Then
                menu.aggiungi("Sostituisci", AddressOf sostituisciMenu)
            End If
            menu.avvia(If(SolaLettura, "Trova", "Trova e sostituisci"), True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuTrovaSostituisci: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub trovaMenu(sender As Object, e As EventArgs)
        Try
            Trova()
        Catch ex As Exception
            logger.ScriviLog("trovaMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub sostituisciMenu(sender As Object, e As EventArgs)
        Try
            Sostituisci()
        Catch ex As Exception
            logger.ScriviLog("sostituisciMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub Trova()
        Try
            _campiTrovaSostituisci = New Campi()
            _campiTrovaSostituisci.Aggiungi("Testo da cercare", "c.trovaTesto", True)
            AddHandler _campiTrovaSostituisci.Fine, AddressOf FineCampiTrova
            _campiTrovaSostituisci.Avvia()
        Catch ex As Exception
            logger.ScriviLog("Trova: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCampiTrova(sender As Object, e As EventArgs)
        Try
            Dim testoCercato As String = _campiTrovaSostituisci.RecuperaValore("c.trovaTesto")
            RemoveHandler _campiTrovaSostituisci.Fine, AddressOf FineCampiTrova

            If Not String.IsNullOrEmpty(testoCercato) Then
                _ultimoTestoCercato = testoCercato
                Dim trovato As Boolean = CercaTesto(testoCercato)
                If Not trovato Then
                    If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna corrispondenza trovata per " & testoCercato)
                End If
            End If

            If Me.IsHandleCreated Then SetFocus(Me.Handle)
        Catch ex As Exception
            logger.ScriviLog("FineCampiTrova: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub TrovaSuccessivo()
        Try
            If String.IsNullOrEmpty(_ultimoTestoCercato) Then
                Trova()
                Return
            End If

            Dim trovato As Boolean = CercaTesto(_ultimoTestoCercato)
            If Not trovato Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna corrispondenza trovata per " & _ultimoTestoCercato)
            End If
        Catch ex As Exception
            logger.ScriviLog("TrovaSuccessivo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub Sostituisci()
        Try
            If SolaLettura Then Return

            _campiTrovaSostituisci = New Campi()
            _campiTrovaSostituisci.Aggiungi("Testo da cercare", "c.sostCerca", True)
            _campiTrovaSostituisci.Aggiungi("Sostituisci con", "c.sostCon", True)
            AddHandler _campiTrovaSostituisci.Fine, AddressOf FineCampiSostituisci
            _campiTrovaSostituisci.Avvia()
        Catch ex As Exception
            logger.ScriviLog("Sostituisci: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCampiSostituisci(sender As Object, e As EventArgs)
        Try
            Dim testoCercato As String = _campiTrovaSostituisci.RecuperaValore("c.sostCerca")
            Dim testoSostituto As String = _campiTrovaSostituisci.RecuperaValore("c.sostCon")
            RemoveHandler _campiTrovaSostituisci.Fine, AddressOf FineCampiSostituisci

            If Not String.IsNullOrEmpty(testoCercato) Then
                _ultimoTestoCercato = testoCercato
                SaveUndoState()
                SostituisciTesto(testoCercato, testoSostituto)
            End If

            If Me.IsHandleCreated Then SetFocus(Me.Handle)
        Catch ex As Exception
            logger.ScriviLog("FineCampiSostituisci: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub
End Class