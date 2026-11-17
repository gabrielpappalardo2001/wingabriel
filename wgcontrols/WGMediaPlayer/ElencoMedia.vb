Imports System
Imports System.Collections.Generic
Imports wgcontrols

''' <summary>
''' Un elenco a schermo (artisti, album, brani, playlist) con le sue azioni. Avvolge WGMenuLista
''' in modo che Esc lo chiuda sempre e che le schermate si impilino: chiuso l'elenco figlio,
''' riappare quello da cui si veniva.
''' </summary>
Friend Class ElencoMedia

    Private ReadOnly _lista As New WGMenuLista()
    Private _aperto As Boolean

    ''' <summary>Invio su un elemento (selezione singola).</summary>
    Public Event Scelto(id As String)
    ''' <summary>Invio con più elementi segnati.</summary>
    Public Event SceltiPiu(ids As List(Of String))
    ''' <summary>Raggiunta la fine dell'elenco: serve a caricare altri elementi.</summary>
    Public Event FineElenco()

    Public Sub New(titolo As String, multiselezione As Boolean)
        _lista.ciclono = True
        _lista.multiselezioneConsentita = multiselezione
        _lista.selezioneMenu = Not multiselezione
        _lista.impostaTitolo(titolo)
        AddHandler _lista.ElementoSelezionato, Sub(id As String) RaiseEvent Scelto(id)
        AddHandler _lista.ElementiSelezionati, Sub(ids As List(Of String)) RaiseEvent SceltiPiu(ids)
        AddHandler _lista.fine, Sub() RaiseEvent FineElenco()
        AddHandler _lista.escPremuto, Sub() Chiudi()
    End Sub

    Public Sub Titolo(testo As String)
        Try
            _lista.impostaTitolo(testo)
        Catch ex As Exception
            MediaUtil.Log("ElencoMedia.Titolo: " & ex.ToString())
        End Try
    End Sub

    Public Sub Aggiungi(testo As String, id As String)
        _lista.aggiungiElemento(testo, id)
    End Sub

    ''' <summary>Aggiunge molti elementi in un colpo solo, senza ridisegnare l'elenco a ogni voce.</summary>
    Public Sub AggiungiMolti(elementi As IEnumerable(Of KeyValuePair(Of String, String)))
        Try
            _lista.BeginUpdate()
            For Each e As KeyValuePair(Of String, String) In elementi
                _lista.aggiungiElemento(e.Key, e.Value)
            Next
        Finally
            _lista.EndUpdate()
        End Try
    End Sub

    Public Sub Opzione(testo As String, azione As Action)
        _lista.aggiungiOpzione(testo, Sub(mittente As Object, e As EventArgs)
                                          Try
                                              azione()
                                          Catch ex As Exception
                                              MediaUtil.Log("ElencoMedia.Opzione " & testo & ": " & ex.ToString())
                                          End Try
                                      End Sub)
    End Sub

    Public Function Selezionati() As List(Of String)
        Try
            Return _lista.ottieniElementi()
        Catch ex As Exception
            MediaUtil.Log("ElencoMedia.Selezionati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Sub Mostra()
        _aperto = True
        _lista.avvia()
    End Sub

    Public Sub Chiudi()
        Try
            If Not _aperto Then Return
            _aperto = False
            _lista.abbandona()
        Catch ex As Exception
            MediaUtil.Log("ElencoMedia.Chiudi: " & ex.ToString())
        End Try
    End Sub

End Class
