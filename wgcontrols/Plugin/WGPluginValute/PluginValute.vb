Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports WGPlugin

''' <summary>
''' Valute: converte un importo da una valuta a un'altra con i cambi di riferimento della Banca Centrale Europea, che li pubblica ogni
''' giorno lavorativo. Si scrive la richiesta come si direbbe ("100 dollari in euro", "50 sterline", "200 chf in jpy") e il plugin
''' risponde a voce. I cambi del giorno si scaricano una volta sola e si tengono nei file del plugin: senza internet valgono gli ultimi.
''' </summary>
Public Class PluginValute
    Inherits WGPluginBase

    Private Const IndirizzoBce As String = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml"
    Private Const FileCambi As String = "cambi.txt"

    ' codice -> (singolare, plurale, genere: True = maschile)
    Private Shared ReadOnly Nomi As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase) From {
        {"EUR", New String() {"euro", "euro"}},
        {"USD", New String() {"dollaro statunitense", "dollari statunitensi"}},
        {"GBP", New String() {"sterlina", "sterline"}},
        {"JPY", New String() {"yen", "yen"}},
        {"CHF", New String() {"franco svizzero", "franchi svizzeri"}},
        {"CAD", New String() {"dollaro canadese", "dollari canadesi"}},
        {"AUD", New String() {"dollaro australiano", "dollari australiani"}},
        {"NZD", New String() {"dollaro neozelandese", "dollari neozelandesi"}},
        {"HKD", New String() {"dollaro di Hong Kong", "dollari di Hong Kong"}},
        {"SGD", New String() {"dollaro di Singapore", "dollari di Singapore"}},
        {"CNY", New String() {"yuan cinese", "yuan cinesi"}},
        {"INR", New String() {"rupia indiana", "rupie indiane"}},
        {"IDR", New String() {"rupia indonesiana", "rupie indonesiane"}},
        {"KRW", New String() {"won sudcoreano", "won sudcoreani"}},
        {"BRL", New String() {"real brasiliano", "real brasiliani"}},
        {"MXN", New String() {"peso messicano", "pesos messicani"}},
        {"PHP", New String() {"peso filippino", "pesos filippini"}},
        {"TRY", New String() {"lira turca", "lire turche"}},
        {"SEK", New String() {"corona svedese", "corone svedesi"}},
        {"NOK", New String() {"corona norvegese", "corone norvegesi"}},
        {"DKK", New String() {"corona danese", "corone danesi"}},
        {"CZK", New String() {"corona ceca", "corone ceche"}},
        {"ISK", New String() {"corona islandese", "corone islandesi"}},
        {"PLN", New String() {"zloty polacco", "zloty polacchi"}},
        {"HUF", New String() {"fiorino ungherese", "fiorini ungheresi"}},
        {"RON", New String() {"leu rumeno", "lei rumeni"}},
        {"ILS", New String() {"nuovo shekel israeliano", "nuovi shekel israeliani"}},
        {"THB", New String() {"baht thailandese", "baht thailandesi"}},
        {"MYR", New String() {"ringgit malese", "ringgit malesi"}},
        {"ZAR", New String() {"rand sudafricano", "rand sudafricani"}}}

    ' parole che identificano una valuta, dalla più specifica alla più generica (la prima che compare vince)
    Private Shared ReadOnly Parole As String(,) = {
        {"hong kong", "HKD"}, {"singapor", "SGD"}, {"neozelandes", "NZD"}, {"australian", "AUD"}, {"canades", "CAD"},
        {"svizzer", "CHF"}, {"sterlin", "GBP"}, {"giappones", "JPY"}, {"yen", "JPY"}, {"yuan", "CNY"}, {"renminbi", "CNY"}, {"cines", "CNY"},
        {"indonesian", "IDR"}, {"indian", "INR"}, {"rupi", "INR"}, {"corean", "KRW"}, {"won", "KRW"}, {"brasilian", "BRL"}, {"real", "BRL"},
        {"filippin", "PHP"}, {"messican", "MXN"}, {"peso", "MXN"}, {"pesos", "MXN"}, {"turc", "TRY"}, {"lira", "TRY"}, {"lire", "TRY"},
        {"svedes", "SEK"}, {"norveges", "NOK"}, {"danes", "DKK"}, {"cec", "CZK"}, {"islandes", "ISK"}, {"zloty", "PLN"}, {"polacc", "PLN"},
        {"ungheres", "HUF"}, {"fiorin", "HUF"}, {"rumen", "RON"}, {"leu", "RON"}, {"lei", "RON"}, {"shekel", "ILS"}, {"israelian", "ILS"},
        {"baht", "THB"}, {"thai", "THB"}, {"ringgit", "MYR"}, {"malesi", "MYR"}, {"rand", "ZAR"}, {"sudafrican", "ZAR"},
        {"franc", "CHF"}, {"dollar", "USD"}, {"statunitens", "USD"}, {"american", "USD"}, {"euro", "EUR"}}

    Private _servizi As IWGServiziPlugin
    Private _tassi As Dictionary(Of String, Double)
    Private _dataBce As String = ""

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Valute"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Converte gli importi da una valuta all'altra con i cambi della Banca Centrale Europea."
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
            servizi.AggiungiVoceAltreFunzioni("Valute", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginValute.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Valute")
            menu.Aggiungi("Converti", Sub() _servizi.ChiediTesto("Conversione", "", AddressOf ConvertiRichiesta))
            menu.Aggiungi("Cambi di oggi", AddressOf CambiDiOggi)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.ApriMenu", ex)
        End Try
    End Sub

#Region "Cambi: scaricarli e tenerli"

    ''' <summary>I cambi: quelli già scaricati oggi, oppure quelli nuovi della BCE, oppure (senza internet) gli ultimi che ci sono.</summary>
    Private Async Function CaricaCambiAsync() As Task(Of Boolean)
        Try
            If _tassi IsNot Nothing AndAlso _dataScarico = DateTime.Today Then Return True
            Dim salvato As Byte() = _servizi.LeggiFileDati(FileCambi)
            Dim tassiSalvati As Dictionary(Of String, Double) = Nothing
            Dim dataSalvata As String = ""
            Dim scaricatoIl As Date = Date.MinValue
            If salvato IsNot Nothing Then LeggiCopia(Encoding.UTF8.GetString(salvato), tassiSalvati, dataSalvata, scaricatoIl)
            If tassiSalvati IsNot Nothing AndAlso scaricatoIl = DateTime.Today Then
                _tassi = tassiSalvati
                _dataBce = dataSalvata
                _dataScarico = scaricatoIl
                Return True
            End If
            Dim xml As String = Await _servizi.ScaricaTestoAsync(IndirizzoBce)
            Dim data As String = ""
            Dim nuovi As Dictionary(Of String, Double) = If(xml Is Nothing, Nothing, EstraiTassi(xml, data))
            If nuovi IsNot Nothing AndAlso nuovi.Count > 5 Then
                _tassi = nuovi
                _dataBce = data
                _dataScarico = DateTime.Today
                _servizi.ScriviFileDati(FileCambi, New UTF8Encoding(False).GetBytes(ScriviCopia(nuovi, data, DateTime.Today)))
                Return True
            End If
            If tassiSalvati IsNot Nothing Then
                _tassi = tassiSalvati
                _dataBce = dataSalvata
                _dataScarico = Date.MinValue
                Return True
            End If
            Return False
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.CaricaCambiAsync", ex)
            Return False
        End Try
    End Function

    Private _dataScarico As Date = Date.MinValue

    Private Async Function ConCambiAsync(azione As Action) As Task
        Try
            Dim riuscito As Boolean = False
            Await _servizi.EseguiConAttesaAsync("Scarico i cambi", Async Function()
                                                                       riuscito = Await CaricaCambiAsync()
                                                                   End Function)
            If Not riuscito Then
                _servizi.Messaggio("Non riesco a leggere i cambi. Controlla la connessione a internet.")
                Return
            End If
            azione.Invoke()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.ConCambiAsync", ex)
        End Try
    End Function

    ''' <summary>I cambi dal testo della BCE: ogni valuta con il suo cambio rispetto all'euro, e la data di pubblicazione.</summary>
    Friend Shared Function EstraiTassi(xml As String, ByRef data As String) As Dictionary(Of String, Double)
        data = ""
        Dim tassi As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrEmpty(xml) Then Return tassi
        Dim d As Match = Regex.Match(xml, "<Cube\s+time=['""](\d{4}-\d{2}-\d{2})['""]")
        If d.Success Then data = d.Groups(1).Value
        For Each m As Match In Regex.Matches(xml, "<Cube\s+currency=['""]([A-Z]{3})['""]\s+rate=['""]([0-9.]+)['""]")
            Dim valore As Double
            If Double.TryParse(m.Groups(2).Value, NumberStyles.Float, CultureInfo.InvariantCulture, valore) AndAlso valore > 0 Then
                tassi(m.Groups(1).Value) = valore
            End If
        Next
        tassi("EUR") = 1.0
        Return tassi
    End Function

    Private Shared Function ScriviCopia(tassi As Dictionary(Of String, Double), dataBce As String, scaricatoIl As Date) As String
        Dim testo As New StringBuilder()
        testo.Append(scaricatoIl.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(vbLf)
        testo.Append(dataBce).Append(vbLf)
        For Each voce As KeyValuePair(Of String, Double) In tassi
            testo.Append(voce.Key).Append("=").Append(voce.Value.ToString("R", CultureInfo.InvariantCulture)).Append(vbLf)
        Next
        Return testo.ToString()
    End Function

    Private Shared Sub LeggiCopia(testo As String, ByRef tassi As Dictionary(Of String, Double), ByRef dataBce As String, ByRef scaricatoIl As Date)
        tassi = Nothing
        dataBce = ""
        scaricatoIl = Date.MinValue
        Dim righe As String() = testo.Replace(vbCr, "").Split(ChrW(10))
        If righe.Length < 3 Then Return
        Dim quando As Date
        If Not Date.TryParseExact(righe(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, quando) Then Return
        Dim letti As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
        For indice As Integer = 2 To righe.Length - 1
            Dim parti As String() = righe(indice).Split("="c)
            Dim valore As Double
            If parti.Length = 2 AndAlso Double.TryParse(parti(1), NumberStyles.Float, CultureInfo.InvariantCulture, valore) AndAlso valore > 0 Then letti(parti(0)) = valore
        Next
        If letti.Count < 5 Then Return
        tassi = letti
        dataBce = righe(1)
        scaricatoIl = quando
    End Sub

#End Region

#Region "Capire la richiesta"

    ''' <summary>Un numero scritto all'italiana: "1.234,56", "100", "50,5", "100.5".</summary>
    Friend Shared Function TentaLeggiNumero(testo As String, ByRef valore As Double) As Boolean
        Try
            valore = 0
            Dim t As String = If(testo, "").Trim()
            If t = "" OrElse Not Regex.IsMatch(t, "^[0-9.,]+$") Then Return False
            If t.Contains(",") Then
                t = t.Replace(".", "").Replace(",", ".")
            ElseIf Regex.IsMatch(t, "^\d{1,3}(\.\d{3})+$") Then
                t = t.Replace(".", "")
            ElseIf t.IndexOf("."c) <> t.LastIndexOf("."c) Then
                Return False
            End If
            Return Double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, valore) AndAlso valore >= 0
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Il codice della valuta da "dollari", "usd", "franchi svizzeri", "£"; vuoto se non la riconosce.</summary>
    Friend Shared Function CodiceValuta(testo As String) As String
        Try
            Dim t As String = AiutoNome(testo)
            If t = "" Then Return ""
            If t = "€" Then Return "EUR"
            If t = "$" Then Return "USD"
            If t = ChrW(&HA3) Then Return "GBP"
            If t = ChrW(&HA5) Then Return "JPY"
            If Regex.IsMatch(t, "^[a-z]{3}$") AndAlso Nomi.ContainsKey(t.ToUpperInvariant()) Then Return t.ToUpperInvariant()
            For i As Integer = 0 To Parole.GetLength(0) - 1
                If t.Contains(Parole(i, 0)) Then Return Parole(i, 1)
            Next
            Return ""
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function AiutoNome(testo As String) As String
        Return Regex.Replace(If(testo, "").ToLowerInvariant().Replace("à", "a").Replace("è", "e").Replace("é", "e").Replace("ì", "i").Replace("ò", "o").Replace("ù", "u"), "\s+", " ").Trim()
    End Function

    ''' <summary>
    ''' Legge "100 dollari in euro", "50 sterline" (in euro), "200 chf a jpy", "€ 30 in dollari". Se manca la valuta di arrivo, è l'euro
    ''' (o il dollaro, se la partenza è l'euro).
    ''' </summary>
    Friend Shared Function TentaLeggiRichiesta(testo As String, ByRef importo As Double, ByRef da As String, ByRef a As String, ByRef problema As String) As Boolean
        importo = 0
        da = ""
        a = ""
        problema = ""
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                problema = "Non hai scritto niente."
                Return False
            End If
            Dim t As String = Regex.Replace(testo.Trim(), "\s+", " ")
            Dim parteNumero As String = ""
            Dim parteDa As String = ""
            Dim parteA As String = ""
            ' simbolo davanti: "€ 30", "$50"
            Dim simbolo As Match = Regex.Match(t, "^([€$" & ChrW(&HA3) & ChrW(&HA5) & "])\s*([0-9.,]+)\s*(?:(?:in|a|su|to)\s+(.+))?$", RegexOptions.IgnoreCase)
            If simbolo.Success Then
                parteDa = simbolo.Groups(1).Value
                parteNumero = simbolo.Groups(2).Value
                parteA = simbolo.Groups(3).Value
            Else
                Dim m As Match = Regex.Match(t, "^([0-9.,]+)\s*(.+?)(?:\s+(?:in|a|su|to|verso)\s+(.+))?$", RegexOptions.IgnoreCase)
                If Not m.Success Then
                    problema = "Non ho capito. Scrivi per esempio 100 dollari in euro."
                    Return False
                End If
                parteNumero = m.Groups(1).Value
                parteDa = m.Groups(2).Value
                parteA = m.Groups(3).Value
            End If
            If Not TentaLeggiNumero(parteNumero, importo) Then
                problema = "Non ho capito l'importo."
                Return False
            End If
            da = CodiceValuta(parteDa)
            If da = "" Then
                problema = "Non conosco la valuta " & parteDa.Trim() & "."
                Return False
            End If
            If parteA.Trim() <> "" Then
                a = CodiceValuta(parteA)
                If a = "" Then
                    problema = "Non conosco la valuta " & parteA.Trim() & "."
                    Return False
                End If
            Else
                a = If(da = "EUR", "USD", "EUR")
            End If
            Return True
        Catch ex As Exception
            problema = "Non sono riuscito a capire la richiesta."
            Return False
        End Try
    End Function

#End Region

#Region "Dire le cifre"

    Friend Shared Function NomeValuta(codice As String, quantita As Double) As String
        Dim voce As String() = Nothing
        If Nomi.TryGetValue(codice, voce) Then Return If(Math.Abs(quantita - 1.0) < 0.0000001, voce(0), voce(1))
        Return codice
    End Function

    ''' <summary>Un importo detto all'italiana: "1.234,56". Con meno di 1 si tengono più decimali, per non perdere cifre.</summary>
    Friend Shared Function Cifra(valore As Double) As String
        Dim it As CultureInfo = CultureInfo.GetCultureInfo("it-IT")
        If valore >= 100 OrElse valore = Math.Floor(valore) Then Return valore.ToString("#,##0.##", it)
        If valore >= 1 Then Return valore.ToString("#,##0.00", it)
        Return valore.ToString("0.0000", it)
    End Function

    ''' <summary>Converte con i cambi della BCE (tutti rispetto all'euro).</summary>
    Friend Shared Function Converti(importo As Double, da As String, a As String, tassi As Dictionary(Of String, Double), ByRef risultato As Double) As Boolean
        risultato = 0
        Dim tassoDa As Double
        Dim tassoA As Double
        If Not tassi.TryGetValue(da, tassoDa) OrElse Not tassi.TryGetValue(a, tassoA) Then Return False
        risultato = importo / tassoDa * tassoA
        Return True
    End Function

#End Region

#Region "Azioni"

    Private Async Sub ConvertiRichiesta(testo As String)
        Try
            Dim importo As Double
            Dim da As String = ""
            Dim a As String = ""
            Dim problema As String = ""
            If Not TentaLeggiRichiesta(testo, importo, da, a, problema) Then
                _servizi.Messaggio(problema)
                Return
            End If
            Await ConCambiAsync(Sub()
                                    Dim risultato As Double
                                    If Not Converti(importo, da, a, _tassi, risultato) Then
                                        _servizi.Messaggio("Non ho il cambio per " & NomeValuta(If(_tassi.ContainsKey(da), a, da), 2) & ". La BCE non lo pubblica.")
                                        Return
                                    End If
                                    _servizi.Messaggio(Cifra(importo) & " " & NomeValuta(da, importo) & " sono " & Cifra(risultato) & " " & NomeValuta(a, risultato) &
                                                       ". Cambio della Banca Centrale Europea del " & DataParlata() & ".")
                                End Sub)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.ConvertiRichiesta", ex)
        End Try
    End Sub

    Private Function DataParlata() As String
        Dim d As Date
        If Date.TryParseExact(_dataBce, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
            Return d.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"))
        End If
        Return _dataBce
    End Function

    Private Async Sub CambiDiOggi()
        Try
            Await ConCambiAsync(AddressOf MostraCambi)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.CambiDiOggi", ex)
        End Try
    End Sub

    Private Sub MostraCambi()
        Try
            Dim voci As New List(Of WGVoceElenco)()
            For Each codice As String In New String() {"USD", "GBP", "CHF", "JPY", "CAD", "AUD", "CNY", "TRY", "SEK", "NOK", "DKK", "PLN", "CZK", "HUF", "RON", "INR", "BRL", "MXN", "KRW", "ZAR"}
                Dim tasso As Double
                If _tassi.TryGetValue(codice, tasso) Then
                    voci.Add(New WGVoceElenco(codice, Nomi(codice)(0) & ", 1 euro vale " & Cifra(tasso)))
                End If
            Next
            If voci.Count = 0 Then
                _servizi.Messaggio("Non ci sono cambi da mostrare.")
                Return
            End If
            _servizi.MostraElencoConId("Cambi del " & DataParlata(), voci, AddressOf DettaglioCambio)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.MostraCambi", ex)
        End Try
    End Sub

    Private Sub DettaglioCambio(codice As String)
        Try
            Dim tasso As Double
            If Not _tassi.TryGetValue(codice, tasso) Then Return
            _servizi.Messaggio("1 euro vale " & Cifra(tasso) & " " & NomeValuta(codice, tasso) & ". 1 " & NomeValuta(codice, 1) & " vale " & Cifra(1.0 / tasso) & " euro.")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginValute.DettaglioCambio", ex)
        End Try
    End Sub

#End Region

End Class
