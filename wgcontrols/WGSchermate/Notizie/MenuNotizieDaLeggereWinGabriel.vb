Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Elenco delle notizie recenti da leggere. Permette la visualizzazione e la selezione delle notizie per la consultazione.
Public Class MenuNotizieDaLeggereWinGabriel
    Private WithEvents menuLista As WGMenuLista
    Private elenco As New List(Of ArticoloNotiziaWinGabriel)
    Private ReadOnly logger As New Logger()

    Public Sub New()
        Mostra()
    End Sub

    Private Sub Mostra()
        Try
            elenco = GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere()

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ci sono notizie da leggere al momento.")
                msg.ShowDialog()
                Return
            End If

            menuLista = New WGMenuLista()
            menuLista.multiselezioneConsentita = False
            menuLista.impostaTitolo("Notizie da leggere")

            For i As Integer = 0 To elenco.Count - 1
                menuLista.aggiungiElemento(elenco(i).Titolo, i.ToString())
            Next

            AddHandler menuLista.escPremuto, AddressOf Chiudi
            AddHandler menuLista.ElementoSelezionato, AddressOf Selezionata

            menuLista.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuNotizieDaLeggereWinGabriel.Mostra eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Chiudi(sender As Object, e As EventArgs)
        Try
            If menuLista IsNot Nothing Then menuLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog("MenuNotizieDaLeggereWinGabriel.Chiudi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Selezionata(id As String)
        Try
            If menuLista Is Nothing Then Return

            Dim elementi As List(Of String) = menuLista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim indice As Integer = -1
            If Not Integer.TryParse(elementi(0), indice) Then Return
            If indice < 0 OrElse indice >= elenco.Count Then Return

            Dim articolo As ArticoloNotiziaWinGabriel = elenco(indice)

            Try
                menuLista.abbandona()
            Catch exAbbandona As Exception
                logger.ScriviLog("MenuNotizieDaLeggereWinGabriel.Selezionata abbandona eccezione: " & exAbbandona.Message)
            End Try

            Try
                GestoreArchivioNotizieWinGabriel.SegnaNotiziaComeLetta(articolo)
            Catch exArchivio As Exception
                logger.ScriviLog("MenuNotizieDaLeggereWinGabriel.Selezionata (archiviazione lettura): " & exArchivio.Message & Environment.NewLine & exArchivio.StackTrace)
            End Try

            Dim campoArticolo As New WGCampo()
            campoArticolo.init(articolo.Titolo, articolo.Testo, True, True)
            campoArticolo.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuNotizieDaLeggereWinGabriel.Selezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub
End Class
