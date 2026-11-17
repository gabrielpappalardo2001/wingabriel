Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports WGPlugin

''' <summary>
''' Calendario liturgico: per oggi o per un'altra data dice il tempo liturgico, la settimana, il colore, l'anno (A, B o C) e il ciclo
''' feriale, la solennità o la festa del giorno; elenca le prossime solennità e feste. Tutto offline, per il rito romano in Italia. Le
''' memorie dei santi non ci sono: per quelle c'è il Santo del giorno.
''' </summary>
Public Class PluginLiturgia
    Inherits WGPluginBase

    Private _servizi As IWGServiziPlugin

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Calendario liturgico"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Tempo liturgico, colore, anno e solennità di oggi o di un'altra data."
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
            servizi.AggiungiVoceAltreFunzioni("Calendario liturgico", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginLiturgia.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Calendario liturgico")
            menu.Aggiungi("Oggi", Sub() Dici(DateTime.Today))
            menu.Aggiungi("Un'altra data", Sub() _servizi.ChiediTesto("Data", "", AddressOf DataScelta))
            menu.Aggiungi("Prossime solennità e feste", AddressOf Prossime)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginLiturgia.ApriMenu", ex)
        End Try
    End Sub

    ''' <summary>Il giorno detto a voce: "Giovedì 12 novembre. 32ª settimana del Tempo Ordinario. Colore verde. Anno A, ciclo feriale II."</summary>
    Friend Shared Function Racconta(data As Date, oggi As Date) As String
        Dim g As AiutoLiturgico.GiornoLiturgico = AiutoLiturgico.Descrivi(data)
        Dim testo As String = AiutoCalendario.Maiuscola(AiutoCalendario.DescriviData(data, oggi)) & ". " & g.Titolo & "."
        If g.Ricorrenza <> "" Then testo &= " Ricorrenza: " & g.Ricorrenza & "."
        testo &= " Colore liturgico " & g.Colore & ". " & g.Anno & "."
        Return testo
    End Function

    Private Sub Dici(data As Date)
        Try
            _servizi.Messaggio(Racconta(data, DateTime.Today))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginLiturgia.Dici", ex)
        End Try
    End Sub

    Private Sub DataScelta(testo As String)
        Try
            Dim data As Date
            If Not AiutoData.TentaLeggiData(testo, DateTime.Today, False, data) Then
                _servizi.Messaggio("Non ho capito la data. Scrivi per esempio domani, 25 dicembre oppure 25/12/2026.")
                Return
            End If
            Dici(data)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginLiturgia.DataScelta", ex)
        End Try
    End Sub

    Private Sub Prossime()
        Try
            Dim oggi As Date = DateTime.Today
            Dim trovate As New List(Of KeyValuePair(Of Date, String()))()
            For anno As Integer = oggi.Year To oggi.Year + 1
                For Each c As KeyValuePair(Of Date, String()) In AiutoLiturgico.CelebrazioniDelAnno(anno)
                    If c.Key >= oggi AndAlso trovate.Count < 12 Then trovate.Add(c)
                Next
            Next
            Dim voci As New List(Of WGVoceElenco)()
            For indice As Integer = 0 To trovate.Count - 1
                Dim c As KeyValuePair(Of Date, String()) = trovate(indice)
                voci.Add(New WGVoceElenco(c.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.Value(0) & ", " & AiutoCalendario.DescriviData(c.Key, oggi) & ", " & AiutoCalendario.QuandoE(c.Key, oggi)))
            Next
            _servizi.MostraElencoConId("Prossime solennità e feste", voci,
                                       Sub(id As String)
                                           Dim d As Date
                                           If Date.TryParseExact(id, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Dici(d)
                                       End Sub)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginLiturgia.Prossime", ex)
        End Try
    End Sub

End Class
