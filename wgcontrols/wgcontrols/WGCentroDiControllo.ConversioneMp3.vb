Imports System.IO
Imports System.Threading
Partial Public Class WGCentroDiControllo
    Private Class LavoroConversioneMp3
        Inherits LavoroBase

        Public Id As String = Guid.NewGuid().ToString("N")
        Public Testo As String = ""
        Public PercorsoFile As String = ""
        Public PercorsoDestinazione As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public TempoInizio As DateTime = DateTime.Now
    End Class

    Public Sub AggiungiConversioneMp3DaTesto(testo As String, percorsoDestinazione As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                NotificaFineOperazione("Nessun testo da convertire in mp3.")
                Return
            End If
            If String.IsNullOrWhiteSpace(percorsoDestinazione) Then
                NotificaFineOperazione("Percorso di salvataggio mp3 non indicato.")
                Return
            End If

            Dim lavoro As New LavoroConversioneMp3()
            lavoro.Testo = testo
            lavoro.PercorsoDestinazione = percorsoDestinazione
            lavoro.Info = "Conversione in mp3"
            AvviaConversioneMp3(lavoro)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AggiungiConversioneMp3DaTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiConversioneMp3DaFile(percorsoFile As String, percorsoDestinazione As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                NotificaFineOperazione("File da convertire in mp3 non trovato.")
                Return
            End If
            If String.IsNullOrWhiteSpace(percorsoDestinazione) Then
                NotificaFineOperazione("Percorso di salvataggio mp3 non indicato.")
                Return
            End If

            Dim lavoro As New LavoroConversioneMp3()
            lavoro.PercorsoFile = percorsoFile
            lavoro.PercorsoDestinazione = percorsoDestinazione
            lavoro.Info = "Conversione in mp3 di " & Path.GetFileName(percorsoFile)
            AvviaConversioneMp3(lavoro)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AggiungiConversioneMp3DaFile: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaConversioneMp3(lavoro As LavoroConversioneMp3)
        Try
            lavoro.Annulla = False
            lavoro.Progresso = 0
            lavoro.TempoInizio = DateTime.Now
            lavoro.Descrizione = "Conversione in mp3"
            RegistraLavoro(lavoro, lavoro.Descrizione, StatoOperazioneCorrente.ConversioneMp3)
            lavoro.InPreparazione = False
            If Not String.IsNullOrEmpty(lavoro.PercorsoFile) Then AggiungiRisorsa(lavoro, lavoro.PercorsoFile)
            AggiungiRisorsa(lavoro, lavoro.PercorsoDestinazione)
            lavoro.ThreadLavoro = New Thread(AddressOf EseguiConversioneMp3)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AvviaConversioneMp3: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiConversioneMp3(parametro As Object)
        Dim lavoro As LavoroConversioneMp3 = TryCast(parametro, LavoroConversioneMp3)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            lavoro.TempoInizio = DateTime.Now
            lavoro.InEsecuzione = True

            Dim convertitore As New WGConversioneMp3()
            Dim annulla As Func(Of Boolean) = Function() lavoro.Annulla
            Dim progresso As Action(Of Integer, String) = Sub(valore As Integer, info As String)
                                                              lavoro.Progresso = valore
                                                              lavoro.Info = info
                                                          End Sub
            Dim ok As Boolean
            If Not String.IsNullOrWhiteSpace(lavoro.PercorsoFile) Then
                ok = convertitore.ConvertiFile(lavoro.PercorsoFile, lavoro.PercorsoDestinazione, annulla, progresso)
            Else
                ok = convertitore.ConvertiTesto(lavoro.Testo, lavoro.PercorsoDestinazione, annulla, progresso)
            End If

            lavoro.InEsecuzione = False
            If lavoro.Annulla Then
                NotificaFineOperazione("Conversione in mp3 annullata.")
            ElseIf ok Then
                NotificaFineOperazione("Conversione in mp3 completata.")
            Else
                NotificaFineOperazione("Conversione in mp3 non riuscita. Controlla il log.")
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EseguiConversioneMp3: " & ex.Message)
            Try
                If lavoro IsNot Nothing Then lavoro.InEsecuzione = False
            Catch ex5 As Exception
                _log.ScriviLog(ex5.Message & ex5.StackTrace)
            End Try
            NotificaFineOperazione("Errore durante la conversione in mp3.")
        Finally
            Try
                RimuoviLavoro(lavoro)
            Catch exRim As Exception
                _log.ScriviLog("WGCentroDiControllo.EseguiConversioneMp3 (rimuovi): " & exRim.Message)
            End Try
        End Try
    End Sub
End Class