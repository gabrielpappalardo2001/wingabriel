Imports System
Imports System.Collections.Generic
Imports wgcontrols
Imports WGFileManager
Imports WGNavidrome

''' <summary>
''' Il Media player di WinGabriel: la libreria multimediale. Riunisce i brani preferiti e recenti
''' che prima stavano in Gestione file, sotto "Ascolto file multimediali", e aggiunge la libreria di
''' un server Navidrome, che si collega dalle opzioni avanzate.
''' </summary>
Partial Public Class MenuMediaPlayer

    Private Const VOCE_NAVIDROME As String = "Navidrome"
    Private Const VOCE_RIFERIMENTO As String = "Brani recenti"
    Private Const VOCE_SCARICATI As String = "Brani scaricati"

    Private _menu As WGMenu
    Private _scaricatiVisibile As Boolean

    Public Sub Avvia()
        Try
            _menu = New WGMenu()
            _menu.aggiungi("Brani preferiti", AddressOf BraniPreferiti)
            _menu.aggiungi(VOCE_RIFERIMENTO, AddressOf BraniRecenti)
            If NavidromeConfigurazione.Carica().Configurato Then
                _menu.aggiungi(VOCE_NAVIDROME, AddressOf ApriNavidrome)
            End If
            ' "Brani scaricati" c'è soltanto se ci sono brani scaricati; si aggiorna ogni volta che si torna al menu.
            _scaricatiVisibile = New NavidromeScaricati().CiSono()
            If _scaricatiVisibile Then _menu.aggiungi(VOCE_SCARICATI, AddressOf ApriScaricati)
            _menu.aggiungi("Opzioni", AddressOf MostraOpzioni)
            AddHandler _menu.MenuRiattivato, Sub() AggiornaVoceScaricati()
            _menu.avvia("Media player")
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.Avvia: " & ex.ToString())
        End Try
    End Sub

    Private Sub BraniPreferiti()
        Try
            Dim gestore As New GestoreBraniPreferitiERecenti()
            gestore.VisualizzaBraniPreferiti()
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.BraniPreferiti: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire i brani preferiti.")
        End Try
    End Sub

    Private Sub BraniRecenti()
        Try
            Dim gestore As New GestoreBraniPreferitiERecenti()
            gestore.VisualizzaBraniRecenti()
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.BraniRecenti: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire i brani recenti.")
        End Try
    End Sub

    Private Sub AggiornaVoceScaricati()
        Try
            If _menu Is Nothing Then Return
            Dim ceNeSono As Boolean = New NavidromeScaricati().CiSono()
            If ceNeSono = _scaricatiVisibile Then Return
            _scaricatiVisibile = ceNeSono
            If ceNeSono Then
                Dim riferimento As String = VOCE_NAVIDROME
                _menu.AggiungiDopo(riferimento, VOCE_SCARICATI, AddressOf ApriScaricati)
            Else
                _menu.RimuoviVoce(VOCE_SCARICATI)
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.AggiornaVoceScaricati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriScaricati()
        Try
            Dim scaricati As New MenuScaricati()
            scaricati.Avvia()
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ApriScaricati: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire i brani scaricati.")
        End Try
    End Sub

    Private Sub ApriNavidrome()
        Try
            Dim configurazione As NavidromeConfigurazione = NavidromeConfigurazione.Carica()
            If Not configurazione.Configurato Then
                MediaUtil.Messaggio("Il server Navidrome non è collegato. Collegalo da Opzioni, Opzioni avanzate, Server Navidrome.")
                Return
            End If
            Dim navidrome As New MenuNavidrome(configurazione)
            navidrome.Avvia()
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ApriNavidrome: " & ex.ToString())
            MediaUtil.Messaggio("Impossibile aprire Navidrome.")
        End Try
    End Sub

    ''' <summary>La voce Navidrome compare o scompare dal menu appena si collega o scollega il server.</summary>
    Friend Sub AggiornaVoceNavidrome(collegato As Boolean)
        Try
            If _menu Is Nothing Then Return
            Try
                _menu.RimuoviVoce(VOCE_NAVIDROME)
            Catch
            End Try
            _menu.AggiungiDopo(VOCE_RIFERIMENTO, VOCE_NAVIDROME, AddressOf ApriNavidrome)
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.AggiornaVoceNavidrome: " & ex.ToString())
        End Try
    End Sub

End Class
