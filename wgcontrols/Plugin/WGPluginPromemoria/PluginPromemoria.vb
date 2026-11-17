Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports WGPlugin

''' <summary>
''' Un promemoria che si ripete a intervalli (per esempio la pausa dallo schermo), con il testo e l'intervallo a scelta, che si
''' impostano con un modulo di WinGabriel (due campi). Le scelte restano salvate: se era attivo, riparte da solo all'avvio.
''' Quando scatta, il promemoria è un messaggio di WinGabriel.
''' </summary>
Public Class PluginPromemoria
    Inherits WGPluginBase

    Private Const TestoPredefinito As String = "È ora di fare una pausa"
    Private Const IntervalloPredefinito As Integer = 60

    Private _servizi As IWGServiziPlugin
    Private _contatore As System.Threading.Timer
    Private _attivo As Boolean
    Private _intervallo As Integer = IntervalloPredefinito
    Private _testo As String = TestoPredefinito

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Promemoria"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Ripete un promemoria a intervalli regolari."
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
            Dim letto As Integer
            If Integer.TryParse(servizi.Leggi("intervallo"), letto) AndAlso letto >= 1 AndAlso letto <= 1440 Then _intervallo = letto
            Dim testo As String = servizi.Leggi("testo")
            If Not String.IsNullOrWhiteSpace(testo) Then _testo = testo
            _attivo = servizi.Leggi("attivo") = "1"
            ' sta in un posto solo: nel menu Organizer
            ' Il promemoria sta bene anche nel menu Organizer, dopo l'Agenda: ogni menu di WinGabriel si può estendere dal titolo.
            servizi.AggiungiVoceAMenu("Menù Organizer", "Promemoria a intervalli", AddressOf ApriMenu, "Agenda")
            If _attivo Then AvviaContatore()
        Catch ex As Exception
            servizi.RegistraErrore("PluginPromemoria.Avvia", ex)
        End Try
    End Sub

    Public Overrides Sub Arresta()
        Try
            FermaContatore()
        Catch ex As Exception
            If _servizi IsNot Nothing Then _servizi.RegistraErrore("PluginPromemoria.Arresta", ex)
        End Try
    End Sub

    Private Sub AvviaContatore()
        FermaContatore()
        Dim periodo As TimeSpan = TimeSpan.FromMinutes(_intervallo)
        _contatore = New System.Threading.Timer(AddressOf Scatta, Nothing, periodo, periodo)
    End Sub

    Private Sub FermaContatore()
        If _contatore IsNot Nothing Then
            _contatore.Dispose()
            _contatore = Nothing
        End If
    End Sub

    ' Chiamato dal timer su un thread di lavoro: il messaggio passa da solo al thread dell'interfaccia.
    Private Sub Scatta(stato As Object)
        Try
            _servizi.Messaggio(_testo)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginPromemoria.Scatta", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Promemoria")
            menu.Aggiungi("Attiva o disattiva", AddressOf AttivaODisattiva)
            menu.Aggiungi("Impostazioni", AddressOf ApriImpostazioni)
            menu.Aggiungi("Stato", AddressOf DiciStato)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginPromemoria.ApriMenu", ex)
        End Try
    End Sub

    Private Sub AttivaODisattiva()
        Try
            _attivo = Not _attivo
            _servizi.Scrivi("attivo", If(_attivo, "1", "0"))
            If _attivo Then
                AvviaContatore()
                _servizi.Messaggio("Promemoria attivato, " & DescriviIntervallo())
            Else
                FermaContatore()
                _servizi.Messaggio("Promemoria disattivato")
            End If
        Catch ex As Exception
            _servizi.RegistraErrore("PluginPromemoria.AttivaODisattiva", ex)
        End Try
    End Sub

    ' Il modulo con due campi: il testo (anche su più righe) e l'intervallo in minuti.
    Private Sub ApriImpostazioni()
        Try
            Dim modulo As IWGCampiPlugin = _servizi.NuovoModulo()
            modulo.AggiungiTestoLungo("Testo del promemoria", "testo", True, False, _testo)
            modulo.AggiungiNumero("Ogni quanti minuti", "intervallo", True, False, _intervallo.ToString())
            modulo.AlConferma = Sub()
                                    Dim minuti As Integer
                                    If Not Integer.TryParse(modulo.Valore("intervallo").Trim(), minuti) OrElse minuti < 1 OrElse minuti > 1440 Then
                                        _servizi.Messaggio("Scrivi un numero di minuti da 1 a 1440.")
                                        Return
                                    End If
                                    Dim testo As String = modulo.Valore("testo").Trim()
                                    If testo = "" Then
                                        _servizi.Messaggio("Il testo del promemoria non può essere vuoto.")
                                        Return
                                    End If
                                    _testo = testo
                                    _intervallo = minuti
                                    _servizi.Scrivi("testo", _testo)
                                    _servizi.Scrivi("intervallo", minuti.ToString())
                                    If _attivo Then AvviaContatore()
                                    modulo.Chiudi()
                                    _servizi.Messaggio("Impostazioni salvate. " & DescriviIntervallo())
                                End Sub
            modulo.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginPromemoria.ApriImpostazioni", ex)
        End Try
    End Sub

    Private Sub DiciStato()
        Try
            _servizi.Messaggio(If(_attivo, "Attivo, " & DescriviIntervallo(), "Disattivato") & ". Testo: " & _testo)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginPromemoria.DiciStato", ex)
        End Try
    End Sub

    Private Function DescriviIntervallo() As String
        If _intervallo = 1 Then Return "ogni minuto"
        If _intervallo = 60 Then Return "ogni ora"
        Return "ogni " & AiutoTempo.Descrivi(TimeSpan.FromMinutes(_intervallo))
    End Function

End Class
