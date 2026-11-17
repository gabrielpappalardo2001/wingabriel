Imports System.IO
Imports System.Threading
Imports System.Collections.Generic
Imports System.Diagnostics

' Riconoscimento automatico e gestione dei file di spartiti musicali.
Partial Public Class WGCentroDiControllo

    ''' <summary>
    ''' Il riconoscimento di uno spartito lo sa fare la libreria della musica, che il nucleo non conosce: chi la usa dà qui la funzione
    ''' (percorso, avanzamento, annullamento, processo avviato, errore) che restituisce i file MusicXML ottenuti.
    ''' </summary>
    Public Delegate Function RiconoscitoreSpartitiDelegato(percorso As String, avanzamento As Action(Of Integer, String), annullato As Func(Of Boolean),
                                                          registraProcesso As Action(Of Process), ByRef errore As String) As List(Of String)
    Public Shared RiconoscitoreSpartiti As RiconoscitoreSpartitiDelegato

    Private Class LavoroRiconoscimentoSpartiti
        Inherits LavoroBase

        Public Percorso As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public TempoInizio As DateTime = DateTime.Now
        Public Processo As Process
        Public AlTermine As Action(Of List(Of String))
    End Class

    ''' <summary>
    ''' Riconosce uno spartito (PDF o immagine) in sottofondo: il lavoro compare nel centro di controllo con la percentuale.
    ''' Alla fine, se è andato bene, alTermine riceve sull'interfaccia i file MusicXML ottenuti; se è fallito o è stato annullato
    ''' si avvisa con un messaggio e alTermine non viene chiamato.
    ''' </summary>
    Public Sub AggiungiRiconoscimentoSpartito(percorso As String, alTermine As Action(Of List(Of String)))
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                NotificaFineOperazione("Lo spartito da riconoscere non è stato trovato.")
                Return
            End If
            Dim lavoro As New LavoroRiconoscimentoSpartiti()
            lavoro.Percorso = percorso
            lavoro.AlTermine = alTermine
            lavoro.Info = "preparazione"
            lavoro.TempoInizio = DateTime.Now
            lavoro.Annulla = False
            RegistraLavoro(lavoro, "Riconoscimento dello spartito " & Path.GetFileName(percorso), StatoOperazioneCorrente.RiconoscimentoSpartiti)
            lavoro.InPreparazione = False
            AggiungiRisorsa(lavoro, percorso)
            lavoro.ThreadLavoro = New Thread(AddressOf EseguiRiconoscimentoSpartiti)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AggiungiRiconoscimentoSpartito: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiRiconoscimentoSpartiti(parametro As Object)
        Dim lavoro As LavoroRiconoscimentoSpartiti = TryCast(parametro, LavoroRiconoscimentoSpartiti)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            lavoro.TempoInizio = DateTime.Now
            lavoro.InEsecuzione = True

            Dim errore As String = ""
            Dim risultati As List(Of String) = If(RiconoscitoreSpartiti Is Nothing, New List(Of String)(), RiconoscitoreSpartiti(
                lavoro.Percorso,
                Sub(valore As Integer, info As String)
                    lavoro.Progresso = Math.Max(0, Math.Min(100, valore))
                    lavoro.Info = info
                End Sub,
                Function() lavoro.Annulla,
                Sub(p As Process) lavoro.Processo = p,
                errore))

            lavoro.InEsecuzione = False
            If lavoro.Annulla Then
                NotificaFineOperazione("Riconoscimento dello spartito annullato.")
            ElseIf risultati.Count = 0 Then
                NotificaFineOperazione(If(errore <> "", errore, "Non è stato possibile riconoscere lo spartito. Controlla il log."))
            ElseIf lavoro.AlTermine IsNot Nothing Then
                EseguiSuThreadInterfaccia(Sub() lavoro.AlTermine(risultati))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EseguiRiconoscimentoSpartiti: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If lavoro IsNot Nothing Then lavoro.InEsecuzione = False
            Catch ex5 As Exception
                _log.ScriviLog(ex5.Message & ex5.StackTrace)
            End Try
            NotificaFineOperazione("Errore durante il riconoscimento dello spartito.")
        Finally
            Try
                RimuoviLavoro(lavoro)
            Catch exRim As Exception
                _log.ScriviLog("WGCentroDiControllo.EseguiRiconoscimentoSpartiti (rimuovi): " & exRim.Message)
            End Try
        End Try
    End Sub

    Private Function TempoStimatoFineSpartiti(lavoro As LavoroRiconoscimentoSpartiti) As String
        Try
            If lavoro Is Nothing Then Return "in calcolo"
            Dim percentuale As Integer = Math.Max(0, Math.Min(100, lavoro.Progresso))
            If percentuale >= 100 Then Return FormattaTempoDaSecondi(0)
            If percentuale < 3 Then Return "in calcolo"
            Dim trascorsi As Double = (DateTime.Now - lavoro.TempoInizio).TotalSeconds
            If trascorsi < 5 Then Return "in calcolo"
            Return FormattaTempoDaSecondi(Math.Max(0, trascorsi * 100.0 / percentuale - trascorsi))
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.TempoStimatoFineSpartiti: " & ex.Message)
            Return "in calcolo"
        End Try
    End Function

    Private Function EtichettaRiconoscimentoSpartiti(lavoro As LavoroRiconoscimentoSpartiti) As String
        Dim percentuale As Integer = Math.Max(0, Math.Min(100, lavoro.Progresso))
        Dim testo As String = lavoro.Descrizione & ", " & percentuale.ToString() & " per cento"
        If lavoro.Info <> "" Then testo &= ", " & lavoro.Info
        testo &= ", fine tra circa " & TempoStimatoFineSpartiti(lavoro)
        Return testo
    End Function

    Private Function InfoRiconoscimentoSpartiti(lavoro As LavoroRiconoscimentoSpartiti) As String
        Dim percentuale As Integer = Math.Max(0, Math.Min(100, lavoro.Progresso))
        Dim righe As String = "Spartito: " & lavoro.Percorso & vbCrLf
        If lavoro.Info <> "" Then righe &= "Sto facendo: " & lavoro.Info & vbCrLf
        righe &= "Avanzamento: " & percentuale.ToString() & " per cento" & vbCrLf
        righe &= "Fine tra circa: " & TempoStimatoFineSpartiti(lavoro) & vbCrLf
        righe &= "Quando ha finito ti chiedo se vuoi ascoltare lo spartito."
        Return righe
    End Function
End Class
