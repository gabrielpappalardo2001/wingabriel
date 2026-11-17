Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports wgcontrols
Imports WGNavidrome

''' <summary>
''' I brani scaricati da Navidrome, conservati sul computer. Si ascoltano anche senza collegamento al
''' server. L'elenco sta nel database Scaricati nei dati di WinGabriel.
''' </summary>
Friend Class MenuScaricati

    Private ReadOnly _archivio As New NavidromeScaricati()

    Public Sub Avvia()
        Try
            Dim brani As List(Of BranoScaricato) = _archivio.Elenco()
            If brani.Count = 0 Then
                MediaUtil.Messaggio("Non hai ancora scaricato brani. Da Navidrome scegli Scarica su un brano, oppure Scarica gli album selezionati.")
                Return
            End If

            Dim elenco As New ElencoMedia("Brani scaricati, " & brani.Count.ToString() & If(brani.Count = 1, " brano", " brani"), True)
            elenco.AggiungiMolti(brani.Select(Function(b, i) New KeyValuePair(Of String, String)(b.Descrizione, i.ToString())))

            ' Invio su un brano riproduce quel brano e basta; con più brani segnati, quelli.
            AddHandler elenco.Scelto, Sub(id As String)
                                          Dim scelti As List(Of BranoScaricato) = DaIds(brani, elenco.Selezionati())
                                          If scelti.Count = 0 Then scelti = DaIds(brani, New List(Of String) From {id})
                                          If scelti.Count > 0 Then Riproduci(New List(Of BranoScaricato) From {scelti(0)})
                                      End Sub
            AddHandler elenco.SceltiPiu, Sub(ids As List(Of String)) Riproduci(DaIds(brani, ids))

            elenco.Opzione("Riproduci selezione", Sub() Riproduci(DaIds(brani, elenco.Selezionati())))
            elenco.Opzione("Riproduci tutti", Sub() Riproduci(brani))
            elenco.Opzione("Riproduci tutti in ordine casuale", Sub() Riproduci(Rimescola(brani)))
            elenco.Opzione("Informazioni", Sub() Informazioni(DaIds(brani, elenco.Selezionati())))
            elenco.Opzione("Elimina dal computer", Sub() Elimina(DaIds(brani, elenco.Selezionati()), elenco))
            elenco.Mostra()
        Catch ex As Exception
            MediaUtil.Log("MenuScaricati.Avvia: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire i brani scaricati.")
        End Try
    End Sub

    Private Shared Function DaIds(brani As List(Of BranoScaricato), ids As List(Of String)) As List(Of BranoScaricato)
        Dim risultato As New List(Of BranoScaricato)
        If ids Is Nothing Then Return risultato
        For Each id As String In ids
            Dim posizione As Integer
            If Integer.TryParse(id, posizione) AndAlso posizione >= 0 AndAlso posizione < brani.Count Then risultato.Add(brani(posizione))
        Next
        Return risultato
    End Function

    Private Shared ReadOnly _casuale As New Random()

    Private Shared Function Rimescola(brani As List(Of BranoScaricato)) As List(Of BranoScaricato)
        Dim copia As New List(Of BranoScaricato)(brani)
        For i As Integer = copia.Count - 1 To 1 Step -1
            Dim j As Integer = _casuale.Next(i + 1)
            Dim t As BranoScaricato = copia(i)
            copia(i) = copia(j)
            copia(j) = t
        Next
        Return copia
    End Function

    Private Shared Sub Riproduci(brani As List(Of BranoScaricato))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            RiproduzioneMedia.Riproduci("Brani scaricati", brani.Select(Function(b) b.Percorso).ToList(), brani.Select(Function(b) b.Nome).ToList())
        Catch ex As Exception
            MediaUtil.Log("MenuScaricati.Riproduci: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub Informazioni(brani As List(Of BranoScaricato))
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            Dim b As BranoScaricato = brani(0)
            Dim righe As New List(Of String)
            righe.Add("Titolo: " & b.Titolo)
            If b.Artista <> "" Then righe.Add("Artista: " & b.Artista)
            If b.Album <> "" Then righe.Add("Album: " & b.Album)
            If b.Anno > 0 Then righe.Add("Anno: " & b.Anno.ToString())
            If b.Genere <> "" Then righe.Add("Genere: " & b.Genere)
            If b.Formato <> "" Then righe.Add("Formato: " & b.Formato)
            If b.Dimensione > 0 Then righe.Add("Dimensione: " & (b.Dimensione / 1048576.0).ToString("0.0") & " megabyte")
            righe.Add("Scaricato il " & b.DataScaricamento.ToString("dd/MM/yyyy"))
            righe.Add("Si trova in " & b.Percorso)
            MediaUtil.Messaggio(String.Join(vbCrLf, righe))
        Catch ex As Exception
            MediaUtil.Log("MenuScaricati.Informazioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub Elimina(brani As List(Of BranoScaricato), elenco As ElencoMedia)
        Try
            If brani Is Nothing OrElse brani.Count = 0 Then Return
            WGDomanda.Chiedi(If(brani.Count = 1, "Eliminare dal computer il brano " & brani(0).Titolo & "?", "Eliminare dal computer " & brani.Count.ToString() & " brani?"),
                             Sub()
                                 Try
                                     For Each b As BranoScaricato In brani
                                         _archivio.Elimina(b)
                                     Next
                                     elenco.Chiudi()
                                     MediaUtil.Messaggio(If(brani.Count = 1, "Brano eliminato.", "Brani eliminati."))
                                 Catch ex As Exception
                                     MediaUtil.Log("MenuScaricati.Elimina: " & ex.ToString())
                                     MediaUtil.Messaggio("Non è stato possibile eliminare tutti i file.")
                                 End Try
                             End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuScaricati.Elimina: " & ex.ToString())
        End Try
    End Sub

End Class
