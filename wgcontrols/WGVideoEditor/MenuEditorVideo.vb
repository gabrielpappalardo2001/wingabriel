' ============================================================================================
' MenuEditorVideo - l'ingresso dell'editor video: un montaggio nuovo o uno dei progetti salvati
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.IO

Public Class MenuEditorVideo

    Private ReadOnly _log As New Logger()
    Private ReadOnly _selettore As New SelettoreFile()

    Public Sub Avvia()
        Try
            If FfmpegUtil.Percorso() = "" Then
                AiutoMusica.Messaggio("Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco.")
                Return
            End If
            If ArchivioProgettiVideo.Elenco().Count = 0 Then
                ScegliVideo()
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Progetto nuovo", Sub(s As Object, e As EventArgs)
                                                Try
                                                    menu.chiudi()
                                                Catch
                                                End Try
                                                ScegliVideo()
                                            End Sub)
            menu.aggiungi("I miei progetti", AddressOf ApriElencoProgetti)
            menu.avvia("Editor video")
        Catch ex As Exception
            _log.ScriviLog("MenuEditorVideo.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Si sceglie il video da montare, con il selettore di file di WinGabriel.</summary>
    Private Sub ScegliVideo()
        _selettore.Scegli(SelettoreFile.EstensioniVideo, AddressOf ApriVideo)
    End Sub

    Private Sub ApriElencoProgetti(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            For Each cartella As String In ArchivioProgettiVideo.Elenco()
                Dim percorso As String = cartella
                menu.aggiungi(Path.GetFileName(percorso), Sub(s As Object, ev As EventArgs) ApriProgetto(percorso))
            Next
            menu.avvia("I miei progetti")
        Catch ex As Exception
            _log.ScriviLog("MenuEditorVideo.ApriElencoProgetti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Apre l'editor su un montaggio nuovo del video indicato, per esempio un video registrato con il registratore.</summary>
    Public Async Sub ApriVideo(percorso As String)
        Try
            Dim progetto As New ProgettoVideo()
            progetto.Sorgente = percorso
            Dim errore As String = Await WGProcessoInCorso.EseguiAsync("Sto leggendo il video, attendere", Function() Task.Run(Function() AnalisiVideo.LeggiInfo(percorso, progetto)))
            If errore <> "" Then
                AiutoMusica.Messaggio(errore)
                Return
            End If
            Dim sessione As New SessioneEditorVideo(progetto)
            sessione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuEditorVideo.ApriVideo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il video: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Riapre un progetto salvato. Il video originale e gli audio usati si cercano dove stavano: se il video manca il progetto non si apre
    ''' (e lo si dice), se manca un audio si apre senza e si dice quale.
    ''' </summary>
    Private Async Sub ApriProgetto(cartella As String)
        Try
            Dim mancanti As New List(Of String)()
            Dim progetto As ProgettoVideo = Nothing
            Dim errore As String = Await WGProcessoInCorso.EseguiAsync("Sto aprendo il progetto, attendere", Function() Task.Run(Function() CaricaProgetto(cartella, mancanti, progetto)))
            If errore <> "" Then
                AiutoMusica.Messaggio(errore)
                Return
            End If
            If mancanti.Count > 0 Then
                AiutoMusica.Messaggio("Nel progetto mancano questi file, che ho tolto dal montaggio: " & String.Join("; ", mancanti.Select(Function(m) Path.GetFileName(m))) & ". Se li ritrovi, aggiungili di nuovo.")
            End If
            Dim sessione As New SessioneEditorVideo(progetto)
            sessione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuEditorVideo.ApriProgetto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il progetto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CaricaProgetto(cartella As String, mancanti As List(Of String), ByRef progetto As ProgettoVideo) As String
        progetto = ArchivioProgettiVideo.Carica(cartella, mancanti)
        If progetto Is Nothing Then
            Dim video As String = If(mancanti.Count > 0, mancanti(0), "")
            mancanti.Clear()
            Return "Il video originale del progetto non c'è più" & If(video <> "", ": " & video, "") & ". Rimettilo dov'era per riaprire il progetto."
        End If
        ' le caratteristiche del video si rileggono: se il file è stato cambiato il progetto lo sa
        Dim posizione As Double = progetto.Posizione
        Dim problema As String = AnalisiVideo.LeggiInfo(progetto.Sorgente, progetto)
        progetto.Posizione = Math.Min(posizione, progetto.Durata)
        Return problema
    End Function

End Class
