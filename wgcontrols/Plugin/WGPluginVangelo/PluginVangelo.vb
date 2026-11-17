Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports WGPlugin

''' <summary>
''' Vangelo del giorno: la ricorrenza liturgica, il Vangelo, le letture e il commento dei Papi di oggi o di un'altra data, dalla pagina
''' "Parola del giorno" di Vatican News. Si scarica una volta per giorno e si tiene nei file del plugin (si tengono gli ultimi dieci giorni).
''' </summary>
Public Class PluginVangelo
    Inherits WGPluginBase

    ''' <summary>Quello che si legge in una pagina del giorno.</summary>
    Friend Class GiornoLiturgico
        Public Ricorrenza As String = ""
        Public Letture As String = ""
        Public Vangelo As String = ""
        Public Commento As String = ""
    End Class

    Private Const Indirizzo As String = "https://www.vaticannews.va/it/vangelo-del-giorno-e-parola-del-giorno/"
    Private Const GiorniDaTenere As Integer = 10

    Private _servizi As IWGServiziPlugin

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Vangelo del giorno"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "La ricorrenza, il Vangelo e le letture del giorno, di oggi o di un'altra data."
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
            servizi.AggiungiVoceAltreFunzioni("Vangelo del giorno", AddressOf ApriOggi)
            PulisciVecchi()
        Catch ex As Exception
            servizi.RegistraErrore("PluginVangelo.Avvia", ex)
        End Try
    End Sub

#Region "Menu"

    Private Sub ApriOggi()
        Try
            ApriGiorno(DateTime.Today)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.ApriOggi", ex)
        End Try
    End Sub

    Private Sub ApriGiorno(data As Date)
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Vangelo del giorno")
            menu.Aggiungi("Ricorrenza", Sub() Mostra(data, "ricorrenza"))
            menu.Aggiungi("Vangelo", Sub() Mostra(data, "vangelo"))
            menu.Aggiungi("Letture", Sub() Mostra(data, "letture"))
            menu.Aggiungi("Commento dei Papi", Sub() Mostra(data, "commento"))
            menu.Aggiungi("Un'altra data", AddressOf ChiediData)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.ApriGiorno", ex)
        End Try
    End Sub

    Private Sub ChiediData()
        Try
            _servizi.ChiediTesto("Data", "", AddressOf DataScelta)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.ChiediData", ex)
        End Try
    End Sub

    Private Sub DataScelta(testo As String)
        Try
            Dim data As Date
            If Not AiutoData.TentaLeggiData(testo, DateTime.Today, False, data) Then
                _servizi.Messaggio("Non ho capito la data. Scrivi per esempio domani, 25 dicembre oppure 25/12/2026.")
                Return
            End If
            ApriGiorno(data)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.DataScelta", ex)
        End Try
    End Sub

    Private Async Sub Mostra(data As Date, cosa As String)
        Try
            Dim giorno As GiornoLiturgico = Nothing
            Dim errore As Boolean = False
            Await _servizi.EseguiConAttesaAsync("Cerco le letture del giorno",
                                                 Async Function()
                                                     Try
                                                         giorno = Await CaricaGiornoAsync(data)
                                                     Catch ex As Exception
                                                         errore = True
                                                         _servizi.RegistraErrore("PluginVangelo.Mostra (ricerca)", ex)
                                                     End Try
                                                 End Function)
            If errore OrElse giorno Is Nothing Then
                _servizi.Messaggio("Non riesco a leggere le letture del giorno. Controlla la connessione a internet.")
                Return
            End If
            Dim quando As String = AiutoData.Descrivi(data, DateTime.Today)
            Select Case cosa
                Case "ricorrenza"
                    If giorno.Ricorrenza = "" Then
                        _servizi.Messaggio("Non ho trovato la ricorrenza del " & quando & ".")
                    Else
                        _servizi.Messaggio(quando & ": " & giorno.Ricorrenza & ".")
                    End If
                Case "vangelo"
                    VediTesto("Vangelo del " & quando, giorno.Ricorrenza, giorno.Vangelo, "il Vangelo")
                Case "letture"
                    VediTesto("Letture del " & quando, giorno.Ricorrenza, giorno.Letture, "le letture")
                Case Else
                    VediTesto("Commento dei Papi, " & quando, giorno.Ricorrenza, giorno.Commento, "il commento")
            End Select
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.Mostra", ex)
        End Try
    End Sub

    Private Sub VediTesto(titolo As String, ricorrenza As String, testo As String, nome As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                _servizi.Messaggio("Non ho trovato " & nome & " per quel giorno.")
                Return
            End If
            _servizi.MostraTesto(titolo, If(ricorrenza <> "", ricorrenza & vbLf & vbLf, "") & testo)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.VediTesto", ex)
        End Try
    End Sub

#End Region

#Region "Pagina del giorno"

    Private Shared Function NomeFile(data As Date) As String
        Return "vangelo-" & data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & ".txt"
    End Function

    Private Async Function CaricaGiornoAsync(data As Date) As Task(Of GiornoLiturgico)
        Dim salvato As Byte() = _servizi.LeggiFileDati(NomeFile(data))
        If salvato IsNot Nothing Then
            Dim copia As GiornoLiturgico = LeggiCopia(Encoding.UTF8.GetString(salvato))
            If copia IsNot Nothing Then Return copia
        End If
        Dim indirizzoPagina As String = Indirizzo & data.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) & ".html"
        Dim html As String = Await _servizi.ScaricaTestoAsync(indirizzoPagina)
        If html Is Nothing Then Throw New InvalidOperationException("pagina non disponibile")
        Dim giorno As GiornoLiturgico = EstraiGiorno(html)
        If giorno.Vangelo <> "" OrElse giorno.Letture <> "" Then
            _servizi.ScriviFileDati(NomeFile(data), New UTF8Encoding(False).GetBytes(ScriviCopia(giorno)))
        End If
        Return giorno
    End Function

    ''' <summary>Le parti della pagina di Vatican News: ricorrenza, letture, Vangelo e commento dei Papi.</summary>
    Friend Shared Function EstraiGiorno(html As String) As GiornoLiturgico
        Dim giorno As New GiornoLiturgico()
        If String.IsNullOrEmpty(html) Then Return giorno
        Dim r As Match = Regex.Match(html, "class=""indicazioneLiturgica"">\s*<span>(.*?)</span>", RegexOptions.Singleline Or RegexOptions.IgnoreCase)
        If r.Success Then giorno.Ricorrenza = HtmlATesto(r.Groups(1).Value).Trim()
        giorno.Letture = Sezione(html, "Letture del Giorno")
        giorno.Vangelo = Sezione(html, "Vangelo del Giorno")
        giorno.Commento = Sezione(html, "Le Parole dei Papi")
        Return giorno
    End Function

    Private Shared Function Sezione(html As String, titolo As String) As String
        Dim m As Match = Regex.Match(html, "<h2>\s*" & Regex.Escape(titolo) & "\s*</h2>.*?<div class=""section__content"">(.*?)</div>", RegexOptions.Singleline Or RegexOptions.IgnoreCase)
        If Not m.Success Then Return ""
        Return HtmlATesto(m.Groups(1).Value)
    End Function

    ''' <summary>Dal codice della pagina al testo da leggere: a capo dove c'erano <br> e paragrafi, niente marcatori.</summary>
    Friend Shared Function HtmlATesto(html As String) As String
        Dim t As String = If(html, "")
        t = Regex.Replace(t, "<br\s*/?>", vbLf, RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "</p>", vbLf & vbLf, RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "<[^>]+>", "")
        t = WebUtility.HtmlDecode(t)
        t = t.Replace(ChrW(&HA0), " ").Replace(vbCr, "")
        Dim righe As String() = t.Split(ChrW(10))
        Dim risultato As New StringBuilder()
        Dim vuote As Integer = 0
        For Each riga As String In righe
            Dim pulita As String = riga.Trim()
            If pulita = "" Then
                vuote += 1
                If vuote = 1 AndAlso risultato.Length > 0 Then risultato.Append(vbLf)
            Else
                vuote = 0
                risultato.Append(pulita).Append(vbLf)
            End If
        Next
        Return risultato.ToString().Trim()
    End Function

    Private Const Separatore As String = "====="

    Private Shared Function ScriviCopia(giorno As GiornoLiturgico) As String
        Return Separatore & "ricorrenza" & vbLf & giorno.Ricorrenza & vbLf &
               Separatore & "letture" & vbLf & giorno.Letture & vbLf &
               Separatore & "vangelo" & vbLf & giorno.Vangelo & vbLf &
               Separatore & "commento" & vbLf & giorno.Commento & vbLf
    End Function

    Private Shared Function LeggiCopia(testo As String) As GiornoLiturgico
        Dim giorno As New GiornoLiturgico()
        Dim corrente As String = ""
        Dim parti As New Dictionary(Of String, StringBuilder)()
        For Each riga As String In testo.Replace(vbCr, "").Split(ChrW(10))
            If riga.StartsWith(Separatore, StringComparison.Ordinal) Then
                corrente = riga.Substring(Separatore.Length)
                parti(corrente) = New StringBuilder()
            ElseIf corrente <> "" Then
                parti(corrente).Append(riga).Append(vbLf)
            End If
        Next
        If Not parti.ContainsKey("vangelo") AndAlso Not parti.ContainsKey("letture") Then Return Nothing
        If parti.ContainsKey("ricorrenza") Then giorno.Ricorrenza = parti("ricorrenza").ToString().Trim()
        If parti.ContainsKey("letture") Then giorno.Letture = parti("letture").ToString().Trim()
        If parti.ContainsKey("vangelo") Then giorno.Vangelo = parti("vangelo").ToString().Trim()
        If parti.ContainsKey("commento") Then giorno.Commento = parti("commento").ToString().Trim()
        Return giorno
    End Function

    ''' <summary>Si tengono solo i file degli ultimi giorni: gli altri si cancellano.</summary>
    Private Sub PulisciVecchi()
        Try
            Dim limite As Date = DateTime.Today.AddDays(-GiorniDaTenere)
            For Each nome As String In _servizi.ElencaFileDati()
                Dim m As Match = Regex.Match(nome, "^vangelo-(\d{4}-\d{2}-\d{2})\.txt$")
                Dim data As Date
                If m.Success AndAlso Date.TryParseExact(m.Groups(1).Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, data) AndAlso data < limite Then
                    _servizi.EliminaFileDati(nome)
                End If
            Next
        Catch ex As Exception
            _servizi.RegistraErrore("PluginVangelo.PulisciVecchi", ex)
        End Try
    End Sub

#End Region

End Class
