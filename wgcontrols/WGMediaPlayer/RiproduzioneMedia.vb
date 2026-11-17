Imports System
Imports System.Collections.Generic
Imports wgcontrols
Imports WGPlayerMpv

''' <summary>
''' L'avvio dell'ascolto. Rispetta la scelta dell'utente fatta nelle opzioni: ascolto in sottofondo,
''' lettore multimediale o domanda ogni volta.
''' </summary>
Friend Module RiproduzioneMedia

    ''' <param name="titolo">Il titolo del lettore (per esempio il nome dell'album).</param>
    ''' <param name="indirizzi">Percorsi o indirizzi da riprodurre, nell'ordine.</param>
    ''' <param name="nomi">I nomi leggibili, uno per indirizzo (possono mancare).</param>
    Friend Sub Riproduci(titolo As String, indirizzi As List(Of String), nomi As List(Of String))
        Try
            If indirizzi Is Nothing OrElse indirizzi.Count = 0 Then Return

            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing AndAlso centro.CiSonoDownloadDipendenzeInCorso() Then
                MediaUtil.Messaggio("Le librerie per il player sono ancora in fase di download. Attendere il completamento.")
                Return
            End If

            Select Case MediaPlayerImpostazioni.ModalitaApertura()
                Case "Sottofondo"
                    InSottofondo(indirizzi, nomi)
                Case "Player"
                    NelLettore(titolo, indirizzi, nomi)
                Case Else
                    MediaUtil.Scegli("Modalità di ascolto", New List(Of KeyValuePair(Of String, Action)) From {
                        MediaUtil.Voce("Ascolto in background", Sub() InSottofondo(indirizzi, nomi)),
                        MediaUtil.Voce("Apri lettore multimediale", Sub() NelLettore(titolo, indirizzi, nomi))
                    })
            End Select
        Catch ex As Exception
            MediaUtil.Log("RiproduzioneMedia.Riproduci: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore durante l'avvio della riproduzione.")
        End Try
    End Sub

    Friend Sub RiproduciRadio(indirizzo As String)
        Try
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing Then centro.RiproduciStreamRadio(indirizzo)
        Catch ex As Exception
            MediaUtil.Log("RiproduzioneMedia.RiproduciRadio: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore durante l'avvio della radio.")
        End Try
    End Sub

    Private Sub InSottofondo(indirizzi As List(Of String), nomi As List(Of String))
        Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
        If centro IsNot Nothing Then centro.RiproduciFileAudio(indirizzi, nomi)
    End Sub

    Private Sub NelLettore(titolo As String, indirizzi As List(Of String), nomi As List(Of String))
        Dim player As New WGPlayerFormInt()
        For i As Integer = 0 To indirizzi.Count - 1
            Dim nome As String = If(nomi IsNot Nothing AndAlso i < nomi.Count, nomi(i), Nothing)
            player.AggiungiElemento(indirizzi(i), ElementType.File, nome)
        Next
        player.avvia(If(String.IsNullOrWhiteSpace(titolo), "Media player", titolo))
    End Sub

End Module
