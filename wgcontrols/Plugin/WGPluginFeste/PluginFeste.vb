Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports WGPlugin

''' <summary>
''' Calendario delle feste: le prossime festività e ricorrenze italiane, la Pasqua e le feste mobili di un anno, i ponti, i giorni tra due
''' date (con i giorni lavorativi), il conto alla rovescia, "che giorno sarà tra...", l'età e le date da ricordare (compleanni e scadenze,
''' che si ripetono ogni anno se si scrivono senza l'anno). Tutto offline.
''' </summary>
Public Class PluginFeste
    Inherits WGPluginBase

    Private Const FileDate As String = "date.txt"

    Private _servizi As IWGServiziPlugin
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _date As New List(Of DataDaRicordare)()

    Private Class DataDaRicordare
        Public Id As String = ""
        Public Nome As String = ""
        Public Data As Date
        Public OgniAnno As Boolean
    End Class

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Calendario delle feste"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Feste, Pasqua, ponti, giorni lavorativi tra due date, conto alla rovescia, età e date da ricordare."
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
            CaricaDate()
            servizi.AggiungiVoceAltreFunzioni("Calendario delle feste", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginFeste.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Calendario delle feste")
            menu.Aggiungi("Prossime festività", AddressOf ProssimeFestivita)
            menu.Aggiungi("Pasqua e feste mobili", Sub() _servizi.ChiediTesto("Anno", "", AddressOf MostraFesteMobili))
            menu.Aggiungi("Ponti", Sub() _servizi.ChiediTesto("Anno", "", AddressOf MostraPonti))
            menu.Aggiungi("Giorni tra due date", AddressOf GiorniTraDueDate)
            menu.Aggiungi("Conto alla rovescia", Sub() _servizi.ChiediTesto("Data", "", AddressOf ContoAllaRovescia))
            menu.Aggiungi("Che giorno sarà", Sub() _servizi.ChiediTesto("Tra quanto", "", AddressOf CheGiornoSara))
            menu.Aggiungi("Età", Sub() _servizi.ChiediTesto("Data di nascita", "", AddressOf CalcolaEta))
            menu.Aggiungi("Date da ricordare", AddressOf ElencoDate)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.ApriMenu", ex)
        End Try
    End Sub

#Region "Feste"

    Private Shared Function DescriviFesta(f As AiutoCalendario.Festa, oggi As Date) As String
        Return f.Nome & ", " & AiutoCalendario.DescriviData(f.Data, oggi) & ", " & AiutoCalendario.QuandoE(f.Data, oggi) & If(f.Festivo, ", giorno festivo", "")
    End Function

    ''' <summary>Le prossime feste e ricorrenze da oggi (se oggi è festa, anche oggi), fino a un massimo.</summary>
    Friend Shared Function Prossime(oggi As Date, quante As Integer) As List(Of AiutoCalendario.Festa)
        Dim risultato As New List(Of AiutoCalendario.Festa)()
        For anno As Integer = oggi.Year To oggi.Year + 1
            For Each f As AiutoCalendario.Festa In AiutoCalendario.Feste(anno)
                If f.Data >= oggi.Date AndAlso risultato.Count < quante Then risultato.Add(f)
            Next
        Next
        Return risultato
    End Function

    Private Sub ProssimeFestivita()
        Try
            Dim oggi As Date = DateTime.Today
            Dim imminenti As List(Of AiutoCalendario.Festa) = Prossime(oggi, 14)
            Dim voci As New List(Of WGVoceElenco)()
            For indice As Integer = 0 To imminenti.Count - 1
                voci.Add(New WGVoceElenco(indice.ToString(), DescriviFesta(imminenti(indice), oggi)))
            Next
            _servizi.MostraElencoConId("Prossime festività", voci, Sub(id As String) _servizi.Messaggio(DescriviFesta(imminenti(Integer.Parse(id)), oggi) & "."))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.ProssimeFestivita", ex)
        End Try
    End Sub

    ''' <summary>L'anno scritto dall'utente; vuoto vale l'anno in corso.</summary>
    Friend Shared Function TentaLeggiAnno(testo As String, oggi As Date, ByRef anno As Integer) As Boolean
        If String.IsNullOrWhiteSpace(testo) Then
            anno = oggi.Year
            Return True
        End If
        If Integer.TryParse(testo.Trim(), anno) AndAlso anno >= 1583 AndAlso anno <= 9999 Then Return True
        Return False
    End Function

    Private Sub MostraFesteMobili(testo As String)
        Try
            Dim oggi As Date = DateTime.Today
            Dim anno As Integer
            If Not TentaLeggiAnno(testo, oggi, anno) Then
                _servizi.Messaggio("Scrivi un anno, da 1583 in poi.")
                Return
            End If
            Dim p As Date = AiutoCalendario.Pasqua(anno)
            Dim righe As New List(Of String)()
            righe.Add("Pasqua " & anno.ToString() & ": " & AiutoCalendario.DescriviData(p, oggi))
            For Each f As AiutoCalendario.Festa In AiutoCalendario.Feste(anno)
                If f.Data >= p.AddDays(-47) AndAlso f.Data <= p.AddDays(63) AndAlso f.Data <> p AndAlso (f.Nome.StartsWith("Martedì grasso") OrElse f.Nome.StartsWith("Mercoledì delle") OrElse f.Nome.StartsWith("Domenica delle") OrElse f.Nome.StartsWith("Giovedì santo") OrElse f.Nome.StartsWith("Venerdì santo") OrElse f.Nome.StartsWith("Lunedì dell") OrElse f.Nome = "Ascensione" OrElse f.Nome = "Pentecoste" OrElse f.Nome = "Corpus Domini") Then
                    righe.Add(f.Nome & ": " & AiutoCalendario.DescriviData(f.Data, oggi))
                End If
            Next
            _servizi.MostraTesto("Pasqua e feste mobili " & anno.ToString(), String.Join(vbLf, righe))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.MostraFesteMobili", ex)
        End Try
    End Sub

    ''' <summary>I ponti dell'anno: le feste nazionali di martedì e giovedì (ponte il giorno prima o dopo) e quelle di lunedì e venerdì (weekend lungo).</summary>
    Friend Shared Function PontiDelAnno(anno As Integer, oggi As Date) As List(Of String)
        Dim righe As New List(Of String)()
        For Each f As AiutoCalendario.Festa In AiutoCalendario.Feste(anno)
            If Not f.Festivo Then Continue For
            Dim data As String = f.Nome & ", " & AiutoCalendario.DescriviData(f.Data, oggi)
            Select Case f.Data.DayOfWeek
                Case DayOfWeek.Tuesday
                    righe.Add(data & ": facendo ponte lunedì " & f.Data.AddDays(-1).ToString("d MMMM", AiutoCalendario.Italiano) & " stai a casa 4 giorni, da sabato a martedì.")
                Case DayOfWeek.Thursday
                    righe.Add(data & ": facendo ponte venerdì " & f.Data.AddDays(1).ToString("d MMMM", AiutoCalendario.Italiano) & " stai a casa 4 giorni, da giovedì a domenica.")
                Case DayOfWeek.Monday
                    righe.Add(data & ": weekend lungo di 3 giorni, da sabato a lunedì.")
                Case DayOfWeek.Friday
                    righe.Add(data & ": weekend lungo di 3 giorni, da venerdì a domenica.")
                Case DayOfWeek.Wednesday
                    righe.Add(data & ": a metà settimana, per un ponte servono due giorni di ferie.")
                Case Else
                    righe.Add(data & ": cade nel fine settimana, nessun giorno in più.")
            End Select
        Next
        Return righe
    End Function

    Private Sub MostraPonti(testo As String)
        Try
            Dim oggi As Date = DateTime.Today
            Dim anno As Integer
            If Not TentaLeggiAnno(testo, oggi, anno) Then
                _servizi.Messaggio("Scrivi un anno, da 1583 in poi.")
                Return
            End If
            _servizi.MostraTesto("Ponti " & anno.ToString(), String.Join(vbLf, PontiDelAnno(anno, oggi)))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.MostraPonti", ex)
        End Try
    End Sub

#End Region

#Region "Calcoli sulle date"

    Private Sub GiorniTraDueDate()
        Try
            Dim modulo As IWGCampiPlugin = _servizi.NuovoModulo()
            modulo.AggiungiTesto("Dalla data", "da", True, False, "")
            modulo.AggiungiTesto("Alla data", "a", True, False, "")
            modulo.AlConferma = Sub()
                                    Dim oggi As Date = DateTime.Today
                                    Dim da, a As Date
                                    If Not AiutoData.TentaLeggiData(modulo.Valore("da"), oggi, False, da) Then
                                        _servizi.Messaggio("Non ho capito la prima data.")
                                        Return
                                    End If
                                    If Not AiutoData.TentaLeggiData(modulo.Valore("a"), oggi, False, a) Then
                                        _servizi.Messaggio("Non ho capito la seconda data.")
                                        Return
                                    End If
                                    modulo.Chiudi()
                                    _servizi.Messaggio(DescriviDistanza(da, a, oggi))
                                End Sub
            modulo.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.GiorniTraDueDate", ex)
        End Try
    End Sub

    ''' <summary>Quanto passa tra due date e quanti sono i giorni lavorativi (dal primo all'ultimo giorno, compresi).</summary>
    Friend Shared Function DescriviDistanza(da As Date, a As Date, oggi As Date) As String
        Dim primo As Date = If(da <= a, da, a)
        Dim secondo As Date = If(da <= a, a, da)
        Dim giorni As Integer = CInt((secondo - primo).TotalDays)
        If giorni = 0 Then Return "Sono lo stesso giorno, " & AiutoCalendario.DescriviData(primo, oggi) & "."
        Return "Da " & AiutoCalendario.DescriviData(primo, oggi) & " a " & AiutoCalendario.DescriviData(secondo, oggi) & " passano " &
               AiutoCalendario.DescriviGiorni(giorni) & ". I giorni lavorativi, dal primo all'ultimo compresi, sono " &
               AiutoCalendario.GiorniLavorativi(primo, secondo).ToString() & "."
    End Function

    Private Sub ContoAllaRovescia(testo As String)
        Try
            Dim oggi As Date = DateTime.Today
            Dim data As Date
            If Not AiutoData.TentaLeggiData(testo, oggi, True, data) Then
                _servizi.Messaggio("Non ho capito la data. Scrivi per esempio domani, 25 dicembre o 25/12/2026.")
                Return
            End If
            _servizi.Messaggio(DescriviConto(data, oggi))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.ContoAllaRovescia", ex)
        End Try
    End Sub

    Friend Shared Function DescriviConto(data As Date, oggi As Date) As String
        Dim giorni As Integer = CInt((data.Date - oggi.Date).TotalDays)
        Dim quando As String = AiutoCalendario.DescriviData(data, oggi)
        If giorni = 0 Then Return "È oggi, " & quando & "."
        If giorni > 0 Then Return "Mancano " & AiutoCalendario.DescriviGiorni(giorni) & " a " & quando & "."
        Return "Sono passati " & AiutoCalendario.DescriviGiorni(giorni) & " da " & quando & "."
    End Function

    ''' <summary>Legge "30 giorni", "2 settimane", "3 mesi", "1 anno", "10 giorni fa", "-5 giorni": la data a quella distanza da oggi.</summary>
    Friend Shared Function TentaLeggiTraQuanto(testo As String, oggi As Date, ByRef data As Date) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim t As String = AiutoData.SenzaAccenti(testo).Trim()
            Dim m As Match = Regex.Match(t, "^(?:tra|fra)?\s*(-)?\s*(\d{1,5}|un|una)\s*(giorn[oi]|settiman[ae]|mes[ei]|ann[oi])(\s+fa)?$")
            If Not m.Success Then Return False
            Dim n As Integer = If(m.Groups(2).Value = "un" OrElse m.Groups(2).Value = "una", 1, Integer.Parse(m.Groups(2).Value))
            If m.Groups(1).Success OrElse m.Groups(4).Success Then n = -n
            Dim unita As String = m.Groups(3).Value
            If unita.StartsWith("giorn") Then
                data = oggi.AddDays(n)
            ElseIf unita.StartsWith("sett") Then
                data = oggi.AddDays(7 * n)
            ElseIf unita.StartsWith("mes") Then
                data = oggi.AddMonths(n)
            Else
                data = oggi.AddYears(n)
            End If
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub CheGiornoSara(testo As String)
        Try
            Dim oggi As Date = DateTime.Today
            Dim data As Date
            If Not TentaLeggiTraQuanto(testo, oggi, data) Then
                _servizi.Messaggio("Non ho capito. Scrivi per esempio 30 giorni, 2 settimane, 3 mesi oppure 10 giorni fa.")
                Return
            End If
            Dim festa As String = ""
            For Each f As AiutoCalendario.Festa In AiutoCalendario.Feste(data.Year)
                If f.Data = data Then festa = " È " & f.Nome & "."
            Next
            _servizi.Messaggio(AiutoCalendario.Maiuscola(AiutoCalendario.DescriviData(data, oggi)) & ", " & AiutoCalendario.QuandoE(data, oggi) & "." & festa)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.CheGiornoSara", ex)
        End Try
    End Sub

    ''' <summary>L'età in anni, mesi e giorni, e quando è il prossimo compleanno.</summary>
    Friend Shared Function DescriviEta(nascita As Date, oggi As Date) As String
        If nascita > oggi Then Return "Quella data è nel futuro."
        Dim anni As Integer = oggi.Year - nascita.Year
        If oggi < nascita.AddYears(anni) Then anni -= 1
        Dim dopoGliAnni As Date = nascita.AddYears(anni)
        Dim mesi As Integer = 0
        While dopoGliAnni.AddMonths(mesi + 1) <= oggi
            mesi += 1
        End While
        Dim giorni As Integer = CInt((oggi - dopoGliAnni.AddMonths(mesi)).TotalDays)
        Dim prossimo As Date = nascita.AddYears(anni + 1)
        Dim testo As String = "Hai " & AiutoCalendario.Unita(anni, "anno", "anni") & ", " & AiutoCalendario.Unita(mesi, "mese", "mesi") & " e " & AiutoCalendario.Unita(giorni, "giorno", "giorni") & ". "
        If prossimo = oggi.Date Then
            testo &= "Oggi è il tuo compleanno."
        Else
            testo &= "Compirai " & (anni + 1).ToString() & " anni " & AiutoCalendario.QuandoE(prossimo, oggi) & ", " & AiutoCalendario.DescriviData(prossimo, oggi) & "."
        End If
        Return testo
    End Function

    Private Sub CalcolaEta(testo As String)
        Try
            Dim oggi As Date = DateTime.Today
            Dim nascita As Date
            If Not AiutoData.TentaLeggiData(testo, oggi, False, nascita) Then
                _servizi.Messaggio("Non ho capito la data. Scrivi il giorno, il mese e l'anno, per esempio 4/3/1980.")
                Return
            End If
            _servizi.Messaggio(DescriviEta(nascita, oggi))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.CalcolaEta", ex)
        End Try
    End Sub

#End Region

#Region "Date da ricordare"

    Private Sub CaricaDate()
        Try
            SyncLock _blocco
                _date.Clear()
                Dim dati As Byte() = _servizi.LeggiFileDati(FileDate)
                If dati Is Nothing Then Return
                For Each riga As String In Encoding.UTF8.GetString(dati).Replace(vbCr, "").Split(ChrW(10))
                    Dim parti As String() = riga.Split(New Char() {"|"c}, 4)
                    Dim data As Date
                    If parti.Length < 4 OrElse Not Date.TryParseExact(parti(2), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, data) Then Continue For
                    _date.Add(New DataDaRicordare() With {.Id = parti(0), .OgniAnno = parti(1) = "1", .Data = data, .Nome = parti(3)})
                Next
            End SyncLock
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.CaricaDate", ex)
        End Try
    End Sub

    Private Sub SalvaDate()
        Try
            Dim testo As New StringBuilder()
            SyncLock _blocco
                For Each d As DataDaRicordare In _date
                    testo.Append(d.Id).Append("|").Append(If(d.OgniAnno, "1", "0")).Append("|").Append(d.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("|").Append(d.Nome).Append(vbLf)
                Next
            End SyncLock
            _servizi.ScriviFileDati(FileDate, New UTF8Encoding(False).GetBytes(testo.ToString()))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.SalvaDate", ex)
        End Try
    End Sub

    ''' <summary>La prossima volta che cade: per una data che si ripete ogni anno il prossimo compleanno, altrimenti la data stessa.</summary>
    Friend Shared Function Prossima(data As Date, ogniAnno As Boolean, oggi As Date) As Date
        If Not ogniAnno Then Return data
        Dim anno As Integer = oggi.Year
        For giro As Integer = 0 To 8
            Dim candidata As Date
            If data.Month = 2 AndAlso data.Day = 29 AndAlso Not DateTime.IsLeapYear(anno + giro) Then Continue For
            candidata = New Date(anno + giro, data.Month, data.Day)
            If candidata >= oggi.Date Then Return candidata
        Next
        Return data
    End Function

    ''' <summary>Vero se la data scritta ha l'anno (4 cifre, oppure dopo la seconda barra): senza anno si ripete ogni anno.</summary>
    Friend Shared Function HaAnno(testo As String) As Boolean
        Dim t As String = If(testo, "")
        Return Regex.IsMatch(t, "\d{4}") OrElse Regex.IsMatch(t, "^\s*\d{1,2}\s*[/\-.]\s*\d{1,2}\s*[/\-.]\s*\d{2}\s*$")
    End Function

    Private Function TestoDi(d As DataDaRicordare, oggi As Date) As String
        Dim quando As Date = Prossima(d.Data, d.OgniAnno, oggi)
        Dim testo As String = d.Nome & ", " & AiutoCalendario.QuandoE(quando, oggi) & ", " & AiutoCalendario.DescriviData(quando, oggi)
        If d.OgniAnno Then testo &= ", ogni anno"
        Return testo
    End Function

    Private Sub ElencoDate()
        Try
            Dim oggi As Date = DateTime.Today
            Dim ordinate As New List(Of DataDaRicordare)()
            SyncLock _blocco
                ordinate.AddRange(_date)
            End SyncLock
            ordinate.Sort(Function(x, y) Prossima(x.Data, x.OgniAnno, oggi).CompareTo(Prossima(y.Data, y.OgniAnno, oggi)))
            Dim elenco As IWGElencoPlugin = _servizi.NuovoElenco("Date da ricordare")
            For Each d As DataDaRicordare In ordinate
                elenco.Aggiungi(d.Id, TestoDi(d, oggi))
            Next
            elenco.AlScelto = Sub(id As String)
                                  Dim d As DataDaRicordare = Trova(id)
                                  If d IsNot Nothing Then _servizi.Messaggio(TestoDi(d, DateTime.Today) & ".")
                              End Sub
            elenco.AggiungiOpzione("Aggiungi una data", Sub() AggiungiData(elenco))
            elenco.AggiungiOpzioneSuVoce("Elimina", Sub(id As String) EliminaData(id, elenco))
            elenco.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.ElencoDate", ex)
        End Try
    End Sub

    Private Function Trova(id As String) As DataDaRicordare
        SyncLock _blocco
            For Each d As DataDaRicordare In _date
                If d.Id = id Then Return d
            Next
        End SyncLock
        Return Nothing
    End Function

    Private Sub AggiungiData(elenco As IWGElencoPlugin)
        Try
            Dim modulo As IWGCampiPlugin = _servizi.NuovoModulo()
            modulo.AggiungiTesto("Nome", "nome", True, False, "")
            modulo.AggiungiTesto("Data", "data", True, False, "")
            modulo.AlConferma = Sub()
                                    Dim nome As String = Regex.Replace(modulo.Valore("nome").Replace("|", "/"), "\s+", " ").Trim()
                                    If nome = "" Then
                                        _servizi.Messaggio("Scrivi il nome.")
                                        Return
                                    End If
                                    Dim oggi As Date = DateTime.Today
                                    Dim testoData As String = modulo.Valore("data")
                                    Dim data As Date
                                    If Not AiutoData.TentaLeggiData(testoData, oggi, False, data) Then
                                        _servizi.Messaggio("Non ho capito la data. Senza l'anno si ripete ogni anno.")
                                        Return
                                    End If
                                    Dim d As New DataDaRicordare() With {.Id = Guid.NewGuid().ToString("N").Substring(0, 8), .Nome = nome, .Data = data, .OgniAnno = Not HaAnno(testoData)}
                                    SyncLock _blocco
                                        _date.Add(d)
                                    End SyncLock
                                    SalvaDate()
                                    elenco.Aggiungi(d.Id, TestoDi(d, oggi))
                                    modulo.Chiudi()
                                    _servizi.Messaggio("Aggiunta: " & TestoDi(d, oggi) & ".")
                                End Sub
            modulo.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.AggiungiData", ex)
        End Try
    End Sub

    Private Async Sub EliminaData(id As String, elenco As IWGElencoPlugin)
        Try
            Dim d As DataDaRicordare = Trova(id)
            If d Is Nothing Then Return
            Dim elimina As Boolean = Await _servizi.ChiediAsync("Vuoi eliminare " & d.Nome & "?")
            If Not elimina Then Return
            SyncLock _blocco
                _date.Remove(d)
            End SyncLock
            SalvaDate()
            elenco.Rimuovi(id)
            _servizi.Messaggio("Eliminata: " & d.Nome & ".")
        Catch ex As Exception
            _servizi.RegistraErrore("PluginFeste.EliminaData", ex)
        End Try
    End Sub

#End Region

End Class
