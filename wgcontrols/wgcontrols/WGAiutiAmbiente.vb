' Aiuti comuni agli ambienti a una riga (musica, registratore, fotocamera, editor video): la scelta della cartella e la richiesta di un testo.
Imports System
Imports System.IO

''' <summary>Scelta della cartella dove esportare, con il selettore di file di WinGabriel: parte dalla scelta delle risorse e non ricorda niente, la cartella si sceglie ogni volta.</summary>
Public NotInheritable Class SelettoreCartellaMusica

    Private ReadOnly _log As New Logger()
    Private _dialogo As WGFileDialog
    Private _alTermine As Action(Of String)

    ''' <summary>Apre il selettore; alTermine riceve la cartella scelta (o la cartella del file scelto), e non viene chiamato se si annulla.</summary>
    Public Sub Scegli(alTermine As Action(Of String))
        Try
            _alTermine = alTermine
            _dialogo = New WGFileDialog()
            AddHandler _dialogo.singolaselezione, AddressOf Scelta
            AddHandler _dialogo.annullata, AddressOf Annullata
            _dialogo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SelettoreCartellaMusica.Scegli: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Scollega()
        Try
            If _dialogo IsNot Nothing Then
                RemoveHandler _dialogo.singolaselezione, AddressOf Scelta
                RemoveHandler _dialogo.annullata, AddressOf Annullata
            End If
        Catch ex As Exception
            _log.ScriviLog("SelettoreCartellaMusica.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub Annullata()
        Scollega()
    End Sub

    Private Sub Scelta(elemento As String)
        Try
            Scollega()
            Dim cartella As String = If(elemento, "").Trim()
            If cartella = "" Then Return
            If Not Directory.Exists(cartella) Then
                Try
                    cartella = Path.GetDirectoryName(cartella)
                Catch
                    cartella = ""
                End Try
            End If
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return
            If _alTermine IsNot Nothing Then _alTermine(cartella)
        Catch ex As Exception
            _log.ScriviLog("SelettoreCartellaMusica.Scelta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

End Class

Public NotInheritable Class AiutoRevisione

    Private Sub New()
    End Sub

    Private Shared Sub SuInterfaccia(azione As Action)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.InvokeRequired Then
                c.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Chiede un testo con il campo di WinGabriel; alTermine riceve il testo (non viene chiamato se si preme Esc).</summary>
    Public Shared Sub ChiediTesto(titolo As String, predefinito As String, alTermine As Action(Of String))
        Dim confermato As Boolean = False
        Dim campo As New WGCampoTesto()
        campo.init(titolo, predefinito)
        AddHandler campo.chiusura, Sub(s As Object, ev As EventArgs)
                                       If confermato Then Return
                                       confermato = True
                                       Dim t As String = ""
                                       Try
                                           t = If(campo.OttieniTesto(), "")
                                       Catch
                                       End Try
                                       Try
                                           campo.chiudi()
                                       Catch
                                       End Try
                                       SuInterfaccia(Sub() alTermine(t))
                                   End Sub
        campo.avvia()
    End Sub

End Class

''' <summary>
''' Il sottofondo, quando c'è, si cambia da una sola voce, "Cambia sottofondo", che apre un menu con due scelte: sceglierne uno nuovo (con il
''' selettore di file già esistente) ed eliminare quello attuale. "Elimina il sottofondo" compare solo se un sottofondo c'è.
''' </summary>
Public NotInheritable Class AiutoSottofondo

    Private Sub New()
    End Sub

    Public Shared Sub MostraCambia(esisteSottofondo As Boolean, scegliNuovo As Action, elimina As Action)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Scegli", Sub()
                                                            Try
                                                                menu.chiudi()
                                                            Catch
                                                            End Try
                                                            scegliNuovo()
                                                        End Sub)
            If esisteSottofondo Then
                menu.aggiungi("Elimina", Sub()
                                                           Try
                                                               menu.chiudi()
                                                           Catch
                                                           End Try
                                                           elimina()
                                                       End Sub)
            End If
            menu.avvia("Cambia sottofondo")
        Catch
        End Try
    End Sub

End Class
