Imports System
Imports System.Collections.Generic
Imports System.Media
Imports System.Threading
Imports WGPlugin

''' <summary>
''' Timer, nel menu Altre funzioni: se ne possono avviare più di uno insieme. Alla fine il timer suona e mostra un messaggio di
''' WinGabriel. I timer in corso stanno in un elenco con gli identificatori. I timer non sopravvivono alla chiusura di WinGabriel.
''' </summary>
Public Class PluginTimer
    Inherits WGPluginBase

    Private Class TimerAttivo
        Public Id As String
        Public Durata As TimeSpan
        Public Fine As DateTime
        Public Contatore As System.Threading.Timer
    End Class

    Private _servizi As IWGServiziPlugin
    Private ReadOnly _attivi As New List(Of TimerAttivo)()
    Private ReadOnly _blocco As New Object()
    Private _prossimoId As Integer
    Private Const VoceAnnullaSpegnimento As String = "Annulla lo spegnimento del computer"
    Private _menu As IWGMenuPlugin
    Private _conAnnulla As Boolean

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Timer"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Avvisa quando è passato il tempo scelto."
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
            servizi.AggiungiVoceAltreFunzioni("Timer", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginTimer.Avvia", ex)
        End Try
    End Sub

    Public Overrides Sub Arresta()
        Try
            SyncLock _blocco
                For Each t As TimerAttivo In _attivi
                    t.Contatore.Dispose()
                Next
                _attivi.Clear()
            End SyncLock
        Catch ex As Exception
            If _servizi IsNot Nothing Then _servizi.RegistraErrore("PluginTimer.Arresta", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Timer")
            menu.Aggiungi("Nuovo timer", Sub() _servizi.ChiediTesto("Durata del timer", "", AddressOf AvviaTimer))
            menu.Aggiungi("Timer in corso", AddressOf ElencoInCorso)
            menu.AggiungiOpzione("Spegni il computer", Sub() _servizi.ChiediTesto("Tra quanto tempo", "", AddressOf ProgrammaSpegnimento))
            _conAnnulla = False
            If _servizi.SpegnimentoRimanente() > TimeSpan.Zero Then
                menu.AggiungiOpzione(VoceAnnullaSpegnimento, AddressOf AnnullaSpegnimento)
                _conAnnulla = True
            End If
            _menu = menu
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.ApriMenu", ex)
        End Try
    End Sub

    ' Lo spegnimento vive in WinGabriel, non nel Timer: resta programmato anche uscendo dal menu del Timer.
    Private Async Sub ProgrammaSpegnimento(testo As String)
        Try
            Dim durata As TimeSpan
            If Not AiutoTempo.TentaLeggiDurata(testo, durata) Then
                _servizi.Messaggio("Non ho capito il tempo.")
                Return
            End If
            If Await _servizi.ProgrammaSpegnimentoAsync(durata) Then
                ' "Annulla" compare solo da quando uno spegnimento è programmato.
                If _menu IsNot Nothing AndAlso Not _conAnnulla Then
                    _menu.AggiungiOpzione(VoceAnnullaSpegnimento, AddressOf AnnullaSpegnimento)
                    _conAnnulla = True
                End If
                _servizi.Messaggio("Il computer si spegnerà tra " & AiutoTempo.Descrivi(durata) & ".")
            Else
                _servizi.Messaggio("Lo spegnimento non è stato programmato.")
            End If
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.ProgrammaSpegnimento", ex)
        End Try
    End Sub

    Private Sub AnnullaSpegnimento()
        Try
            Dim programmato As Boolean = _servizi.SpegnimentoRimanente() > TimeSpan.Zero
            If programmato Then _servizi.AnnullaSpegnimento()
            If _menu IsNot Nothing AndAlso _conAnnulla Then
                _menu.RimuoviOpzione(VoceAnnullaSpegnimento)
                _conAnnulla = False
            End If
            _servizi.Messaggio(If(programmato, "Spegnimento annullato.", "Non c'è nessuno spegnimento programmato."))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.AnnullaSpegnimento", ex)
        End Try
    End Sub

    Private Sub AvviaTimer(testo As String)
        Try
            Dim durata As TimeSpan
            If Not AiutoTempo.TentaLeggiDurata(testo, durata) Then
                _servizi.Messaggio("Non ho capito la durata. Scrivi per esempio 5 minuti, 1 ora e 30 oppure 90 secondi.")
                Return
            End If
            Dim nuovo As New TimerAttivo()
            nuovo.Durata = durata
            nuovo.Fine = DateTime.Now + durata
            SyncLock _blocco
                _prossimoId += 1
                nuovo.Id = _prossimoId.ToString()
                _attivi.Add(nuovo)
            End SyncLock
            nuovo.Contatore = New System.Threading.Timer(AddressOf Scaduto, nuovo, durata, TimeSpan.FromMilliseconds(-1))
            _servizi.Messaggio("Timer di " & AiutoTempo.Descrivi(durata) & " avviato")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.AvviaTimer", ex)
        End Try
    End Sub

    ' Chiamato dal timer, su un thread di lavoro: suono e messaggio passano da soli al thread giusto.
    Private Sub Scaduto(stato As Object)
        Try
            Dim t As TimerAttivo = DirectCast(stato, TimerAttivo)
            SyncLock _blocco
                If Not _attivi.Remove(t) Then Return
            End SyncLock
            t.Contatore.Dispose()
            SystemSounds.Exclamation.Play()
            _servizi.Messaggio("Timer finito: " & AiutoTempo.Descrivi(t.Durata))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.Scaduto", ex)
        End Try
    End Sub

    Private Sub ElencoInCorso()
        Try
            Dim copia As List(Of TimerAttivo)
            SyncLock _blocco
                copia = New List(Of TimerAttivo)(_attivi)
            End SyncLock
            If copia.Count = 0 Then
                _servizi.Messaggio("Nessun timer in corso.")
                Return
            End If
            Dim elenco As IWGElencoPlugin = _servizi.NuovoElenco("Timer in corso")
            For Each t As TimerAttivo In copia
                elenco.Aggiungi(t.Id, "Timer di " & AiutoTempo.Descrivi(t.Durata) & ", mancano " & AiutoTempo.Descrivi(t.Fine - DateTime.Now))
            Next
            elenco.AlScelto = AddressOf ChiediSeAnnullare
            elenco.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.ElencoInCorso", ex)
        End Try
    End Sub

    Private Async Sub ChiediSeAnnullare(id As String)
        Try
            Dim scelto As TimerAttivo = Nothing
            SyncLock _blocco
                scelto = _attivi.Find(Function(t) t.Id = id)
            End SyncLock
            If scelto Is Nothing Then
                _servizi.Messaggio("Il timer era già finito")
                Return
            End If
            Dim annulla As Boolean = Await _servizi.ChiediAsync("Vuoi annullare il timer di " & AiutoTempo.Descrivi(scelto.Durata) & "?")
            If Not annulla Then Return
            Dim presente As Boolean
            SyncLock _blocco
                presente = _attivi.Remove(scelto)
            End SyncLock
            If presente Then scelto.Contatore.Dispose()
            _servizi.Messaggio(If(presente, "Timer annullato", "Il timer era già finito"))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginTimer.ChiediSeAnnullare", ex)
        End Try
    End Sub

End Class
