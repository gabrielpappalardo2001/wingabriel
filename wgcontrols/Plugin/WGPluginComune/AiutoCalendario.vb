Imports System
Imports System.Collections.Generic
Imports System.Globalization

''' <summary>
''' Calcoli di calendario in comune ai plugin (Feste, Liturgia, Rosario): la Pasqua, le festività italiane, i giorni lavorativi, le
''' distanze fra date. Tutto offline. Ogni progetto ne include il file.
''' </summary>
Friend Module AiutoCalendario

    Friend ReadOnly Italiano As New CultureInfo("it-IT")

    ''' <summary>Una festa o una ricorrenza di un anno: la data, e se è un giorno festivo (non si lavora).</summary>
    Friend Class Festa
        Public Nome As String = ""
        Public Data As Date
        Public Festivo As Boolean
    End Class

    ''' <summary>La domenica di Pasqua dell'anno (calendario gregoriano, algoritmo di Meeus).</summary>
    Friend Function Pasqua(anno As Integer) As Date
        Dim a As Integer = anno Mod 19
        Dim b As Integer = anno \ 100
        Dim c As Integer = anno Mod 100
        Dim d As Integer = b \ 4
        Dim e As Integer = b Mod 4
        Dim f As Integer = (b + 8) \ 25
        Dim g As Integer = (b - f + 1) \ 3
        Dim h As Integer = (19 * a + b - d - g + 15) Mod 30
        Dim i As Integer = c \ 4
        Dim k As Integer = c Mod 4
        Dim l As Integer = (32 + 2 * e + 2 * i - h - k) Mod 7
        Dim m As Integer = (a + 11 * h + 22 * l) \ 451
        Dim mese As Integer = (h + l - 7 * m + 114) \ 31
        Dim giorno As Integer = ((h + l - 7 * m + 114) Mod 31) + 1
        Return New Date(anno, mese, giorno)
    End Function

    ''' <summary>L'ultima domenica del mese.</summary>
    Friend Function UltimaDomenica(anno As Integer, mese As Integer) As Date
        Dim d As New Date(anno, mese, DateTime.DaysInMonth(anno, mese))
        Return d.AddDays(-CInt(d.DayOfWeek))
    End Function

    ''' <summary>La n-esima domenica del mese (1 = la prima).</summary>
    Friend Function NesimaDomenica(anno As Integer, mese As Integer, n As Integer) As Date
        Dim primo As New Date(anno, mese, 1)
        Dim prima As Date = primo.AddDays((7 - CInt(primo.DayOfWeek)) Mod 7)
        Return prima.AddDays(7 * (n - 1))
    End Function

    Private Function Nuova(nome As String, data As Date, festivo As Boolean) As Festa
        Dim f As New Festa()
        f.Nome = nome
        f.Data = data
        f.Festivo = festivo
        Return f
    End Function

    ''' <summary>Le feste e le ricorrenze dell'anno, in ordine di data. Quelle con Festivo sono i giorni di festa nazionale.</summary>
    Friend Function Feste(anno As Integer) As List(Of Festa)
        Dim p As Date = Pasqua(anno)
        Dim elenco As New List(Of Festa)()
        elenco.Add(Nuova("Capodanno", New Date(anno, 1, 1), True))
        elenco.Add(Nuova("Epifania", New Date(anno, 1, 6), True))
        elenco.Add(Nuova("San Valentino", New Date(anno, 2, 14), False))
        elenco.Add(Nuova("Martedì grasso", p.AddDays(-47), False))
        elenco.Add(Nuova("Mercoledì delle Ceneri", p.AddDays(-46), False))
        elenco.Add(Nuova("Festa del papà", New Date(anno, 3, 19), False))
        elenco.Add(Nuova("Cambio dell'ora: inizia l'ora legale, le lancette vanno avanti di un'ora", UltimaDomenica(anno, 3), False))
        elenco.Add(Nuova("Domenica delle Palme", p.AddDays(-7), False))
        elenco.Add(Nuova("Giovedì santo", p.AddDays(-3), False))
        elenco.Add(Nuova("Venerdì santo", p.AddDays(-2), False))
        elenco.Add(Nuova("Pasqua", p, True))
        elenco.Add(Nuova("Lunedì dell'Angelo, Pasquetta", p.AddDays(1), True))
        elenco.Add(Nuova("Festa della Liberazione", New Date(anno, 4, 25), True))
        elenco.Add(Nuova("Festa dei lavoratori", New Date(anno, 5, 1), True))
        elenco.Add(Nuova("Festa della mamma", NesimaDomenica(anno, 5, 2), False))
        elenco.Add(Nuova("Ascensione", p.AddDays(42), False))
        elenco.Add(Nuova("Pentecoste", p.AddDays(49), False))
        elenco.Add(Nuova("Festa della Repubblica", New Date(anno, 6, 2), True))
        elenco.Add(Nuova("Corpus Domini", p.AddDays(63), False))
        elenco.Add(Nuova("Ferragosto", New Date(anno, 8, 15), True))
        elenco.Add(Nuova("Cambio dell'ora: inizia l'ora solare, le lancette tornano indietro di un'ora", UltimaDomenica(anno, 10), False))
        elenco.Add(Nuova("Halloween", New Date(anno, 10, 31), False))
        elenco.Add(Nuova("Ognissanti", New Date(anno, 11, 1), True))
        elenco.Add(Nuova("Commemorazione dei defunti", New Date(anno, 11, 2), False))
        elenco.Add(Nuova("Immacolata Concezione", New Date(anno, 12, 8), True))
        elenco.Add(Nuova("Vigilia di Natale", New Date(anno, 12, 24), False))
        elenco.Add(Nuova("Natale", New Date(anno, 12, 25), True))
        elenco.Add(Nuova("Santo Stefano", New Date(anno, 12, 26), True))
        elenco.Add(Nuova("San Silvestro", New Date(anno, 12, 31), False))
        elenco.Sort(Function(x, y) x.Data.CompareTo(y.Data))
        Return elenco
    End Function

    ''' <summary>Vero se il giorno è una festa nazionale (si sta a casa dal lavoro).</summary>
    Friend Function EFestaNazionale(giorno As Date) As Boolean
        For Each f As Festa In Feste(giorno.Year)
            If f.Festivo AndAlso f.Data = giorno.Date Then Return True
        Next
        Return False
    End Function

    ''' <summary>Giorni lavorativi dal primo al secondo giorno compresi: non contano sabato, domenica e le feste nazionali.</summary>
    Friend Function GiorniLavorativi(da As Date, a As Date) As Integer
        Dim inizio As Date = If(da <= a, da.Date, a.Date)
        Dim fine As Date = If(da <= a, a.Date, da.Date)
        Dim totale As Integer = 0
        Dim giorno As Date = inizio
        While giorno <= fine
            If giorno.DayOfWeek <> DayOfWeek.Saturday AndAlso giorno.DayOfWeek <> DayOfWeek.Sunday AndAlso Not EFestaNazionale(giorno) Then totale += 1
            giorno = giorno.AddDays(1)
        End While
        Return totale
    End Function

    ' --- dire le cose all'italiana

    Friend Function Unita(valore As Integer, singolare As String, plurale As String) As String
        Return valore.ToString() & " " & If(valore = 1, singolare, plurale)
    End Function

    ''' <summary>"43 giorni, cioè 6 settimane e 1 giorno".</summary>
    Friend Function DescriviGiorni(giorni As Integer) As String
        Dim n As Integer = Math.Abs(giorni)
        Dim testo As String = Unita(n, "giorno", "giorni")
        If n >= 14 Then
            Dim settimane As Integer = n \ 7
            Dim resto As Integer = n Mod 7
            testo &= ", cioè " & Unita(settimane, "settimana", "settimane")
            If resto > 0 Then testo &= " e " & Unita(resto, "giorno", "giorni")
        End If
        Return testo
    End Function

    ''' <summary>"giovedì 14 novembre", con l'anno se non è quello di oggi.</summary>
    Friend Function DescriviData(data As Date, oggi As Date) As String
        Dim testo As String = data.ToString("dddd d MMMM", Italiano)
        If data.Year <> oggi.Year Then testo &= " " & data.Year.ToString()
        Return testo
    End Function

    ''' <summary>"tra 3 giorni", "domani", "oggi", "ieri", "10 giorni fa".</summary>
    Friend Function QuandoE(data As Date, oggi As Date) As String
        Dim differenza As Integer = CInt((data.Date - oggi.Date).TotalDays)
        If differenza = 0 Then Return "oggi"
        If differenza = 1 Then Return "domani"
        If differenza = 2 Then Return "dopodomani"
        If differenza = -1 Then Return "ieri"
        If differenza > 0 Then Return "tra " & Unita(differenza, "giorno", "giorni")
        Return Unita(-differenza, "giorno", "giorni") & " fa"
    End Function

    ''' <summary>La prima lettera maiuscola.</summary>
    Friend Function Maiuscola(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Return Char.ToUpper(testo(0), Italiano) & testo.Substring(1)
    End Function

End Module
