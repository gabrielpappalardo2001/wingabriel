Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports WGPlugin

''' <summary>
''' Un cronometro con i giri, nel menu Altre funzioni. Continua a contare anche con il menu chiuso: si riapre per sapere il tempo.
''' Usa il menu di WinGabriel con un'opzione (Azzera, si apre con Alt) e l'elenco con gli identificatori per i giri.
''' Ogni risposta è un messaggio di WinGabriel.
''' </summary>
Public Class PluginCronometro
    Inherits WGPluginBase

    Private _servizi As IWGServiziPlugin
    Private ReadOnly _orologio As New Stopwatch()
    Private ReadOnly _giri As New List(Of TimeSpan)()
    Private _totaleUltimoGiro As TimeSpan = TimeSpan.Zero

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Cronometro"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Misura il tempo che passa e segna i giri."
        End Get
    End Property

    Public Overrides ReadOnly Property Autore As String
        Get
            Return "WinGabriel"
        End Get
    End Property

    Public Overrides Sub Avvia(servizi As IWGServiziPlugin)
        Try
            _servizi = servizi
            servizi.AggiungiVoceAltreFunzioni("Cronometro", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginCronometro.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Cronometro")
            menu.Aggiungi("Avvia o ferma", AddressOf AvviaOFerma)
            menu.Aggiungi("Tempo", AddressOf DiciTempo)
            menu.Aggiungi("Segna un giro", AddressOf SegnaGiro)
            menu.Aggiungi("Elenco dei giri", AddressOf ElencoGiri)
            menu.AggiungiOpzione("Azzera", AddressOf Azzera)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.ApriMenu", ex)
        End Try
    End Sub

    Private Sub AvviaOFerma()
        Try
            If _orologio.IsRunning Then
                _orologio.Stop()
                _servizi.Messaggio("Fermato. " & AiutoTempo.Descrivi(_orologio.Elapsed))
            Else
                Dim ripreso As Boolean = _orologio.Elapsed > TimeSpan.Zero
                _orologio.Start()
                _servizi.Messaggio(If(ripreso, "Ripreso", "Avviato"))
            End If
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.AvviaOFerma", ex)
        End Try
    End Sub

    Private Sub DiciTempo()
        Try
            _servizi.Messaggio(AiutoTempo.Descrivi(_orologio.Elapsed) & If(_orologio.IsRunning, "", ". Fermo"))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.DiciTempo", ex)
        End Try
    End Sub

    Private Sub SegnaGiro()
        Try
            If _orologio.Elapsed = TimeSpan.Zero Then
                _servizi.Messaggio("Il cronometro non è ancora partito.")
                Return
            End If
            Dim totale As TimeSpan = _orologio.Elapsed
            Dim giro As TimeSpan = totale - _totaleUltimoGiro
            _totaleUltimoGiro = totale
            _giri.Add(giro)
            _servizi.Messaggio("Giro " & _giri.Count.ToString() & ": " & AiutoTempo.Descrivi(giro))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.SegnaGiro", ex)
        End Try
    End Sub

    Private Sub ElencoGiri()
        Try
            If _giri.Count = 0 Then
                _servizi.Messaggio("Nessun giro segnato.")
                Return
            End If
            Dim elenco As IWGElencoPlugin = _servizi.NuovoElenco("Giri")
            Dim testi As New Dictionary(Of String, String)()
            Dim totale As TimeSpan = TimeSpan.Zero
            For indice As Integer = 0 To _giri.Count - 1
                totale += _giri(indice)
                Dim id As String = (indice + 1).ToString()
                Dim testo As String = "Giro " & id & ", " & AiutoTempo.Descrivi(_giri(indice)) & ", in tutto " & AiutoTempo.Descrivi(totale)
                testi(id) = testo
                elenco.Aggiungi(id, testo)
            Next
            elenco.AlScelto = Sub(id As String) _servizi.Messaggio(testi(id))
            elenco.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.ElencoGiri", ex)
        End Try
    End Sub

    Private Sub Azzera()
        Try
            _orologio.Reset()
            _giri.Clear()
            _totaleUltimoGiro = TimeSpan.Zero
            _servizi.Messaggio("Azzerato")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginCronometro.Azzera", ex)
        End Try
    End Sub

End Class
