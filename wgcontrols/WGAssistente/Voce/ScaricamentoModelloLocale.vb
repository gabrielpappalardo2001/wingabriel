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

Public Class ScaricamentoModelloLocale

    Private Shared _inCorso As Boolean = False

    Public Shared ReadOnly Property InCorso As Boolean
        Get
            Return _inCorso
        End Get
    End Property

    ' Lo scaricamento va avanti nel centro di controllo, senza attese: finito (o fallito: il centro lo comunica con un messaggio) si chiama
    ' completamento.
    Public Shared Sub Avvia(completamento As Action(Of Boolean))
        Try
            If _inCorso Then
                Mostra("Lo scaricamento del modello vocale locale è già in corso: lo trovi nel centro di controllo.")
                Exit Sub
            End If

            Dim motivo As String = ""
            If Not WGTrascrittoreLocale.SistemaSupportato(motivo) Then
                Mostra("Il riconoscimento vocale locale non è disponibile su questo computer: " & motivo & ".")
                Exit Sub
            End If

            If Not WGTrascrittoreLocale.SpazioLiberoSufficiente() Then
                Mostra("Lo spazio libero sul disco del programma non basta: per il modello vocale locale " & WGTrascrittoreLocale.NomeVariante(WGTrascrittoreLocale.VarianteScelta) & " servono almeno " & WGTrascrittoreLocale.DescrizioneSpazioNecessario & " liberi.")
                Exit Sub
            End If

            _inCorso = True
            Dim errore As String = WGTrascrittoreLocale.ScaricaModello(Sub(esito As String)
                                                                          _inCorso = False
                                                                          Try
                                                                              If completamento IsNot Nothing Then completamento(String.IsNullOrEmpty(esito))
                                                                          Catch exCompletamento As Exception
                                                                              log.ScriviLog("ScaricamentoModelloLocale.Avvia (completamento): " & exCompletamento.Message & Environment.NewLine & exCompletamento.StackTrace)
                                                                          End Try
                                                                      End Sub)
            If errore <> "" Then
                _inCorso = False
                Mostra(errore)
                Exit Sub
            End If
            If _inCorso Then Mostra("Ho avviato lo scaricamento del modello vocale locale, " & WGTrascrittoreLocale.DESCRIZIONE_DIMENSIONE & ". Puoi continuare a lavorare: lo trovi nel centro di controllo, e quando è pronto ti avviso.")
        Catch ex As Exception
            _inCorso = False
            log.ScriviLog("ScaricamentoModelloLocale.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub Mostra(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("ScaricamentoModelloLocale.Mostra: " & ex.Message)
        End Try
    End Sub

End Class
