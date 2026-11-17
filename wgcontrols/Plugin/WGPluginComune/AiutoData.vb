Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.RegularExpressions

''' <summary>
''' Aiuti sulle date, in comune ai plugin che leggono date scritte a mano (Appuntamenti, Vangelo): ogni progetto ne include il file.
''' Capire "domani", "giovedì", "25 dicembre", "25/12/2026" e dire una data come la direbbe una persona.
''' </summary>
Friend Module AiutoData

    Friend ReadOnly Italiano As New CultureInfo("it-IT")

    Private ReadOnly Mesi As String() = {"gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"}
    Private ReadOnly MesiBrevi As String() = {"gen", "feb", "mar", "apr", "mag", "giu", "lug", "ago", "set", "ott", "nov", "dic"}
    ' nell'ordine di DayOfWeek: domenica = 0
    Private ReadOnly Giorni As String() = {"domenica", "lunedi", "martedi", "mercoledi", "giovedi", "venerdi", "sabato"}

    ''' <summary>Toglie gli accenti, per confrontare "giovedì" e "giovedi".</summary>
    Friend Function SenzaAccenti(testo As String) As String
        Return If(testo, "").ToLowerInvariant().Replace("à", "a").Replace("è", "e").Replace("é", "e").Replace("ì", "i").Replace("ò", "o").Replace("ù", "u")
    End Function

    ''' <summary>Il numero del mese (1-12) per "dicembre", "dic", "dic."; 0 se non è un mese.</summary>
    Friend Function NumeroMese(parola As String) As Integer
        Try
            Dim p As String = SenzaAccenti(parola).Trim().Trim("."c)
            If p = "" Then Return 0
            For i As Integer = 0 To 11
                If p = Mesi(i) OrElse p = MesiBrevi(i) Then Return i + 1
            Next
            If p = "sett" Then Return 9
            Return 0
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>Il giorno della settimana (come DayOfWeek: domenica 0) per "giovedì" o "giovedi"; -1 se non è un giorno.</summary>
    Friend Function NumeroGiorno(parola As String) As Integer
        Try
            Dim p As String = SenzaAccenti(parola).Trim()
            For i As Integer = 0 To 6
                If p = Giorni(i) Then Return i
            Next
            Return -1
        Catch
            Return -1
        End Try
    End Function

    ''' <summary>Il primo giorno con quel nome DOPO oggi (con "oggi" vero, anche oggi).</summary>
    Friend Function ProssimoGiorno(oggi As Date, giornoSettimana As Integer, includiOggi As Boolean) As Date
        Dim differenza As Integer = (giornoSettimana - CInt(oggi.DayOfWeek) + 7) Mod 7
        If differenza = 0 AndAlso Not includiOggi Then differenza = 7
        Return oggi.AddDays(differenza)
    End Function

    ''' <summary>Costruisce una data con giorno e mese; senza anno, quello di oggi o (se futuro è richiesto e la data è passata) il prossimo.</summary>
    Friend Function CostruisciData(giorno As Integer, mese As Integer, anno As Integer, oggi As Date, soloFuture As Boolean, ByRef data As Date) As Boolean
        Try
            If mese < 1 OrElse mese > 12 OrElse giorno < 1 Then Return False
            If anno > 0 Then
                If anno < 100 Then anno += 2000
                If giorno > DateTime.DaysInMonth(anno, mese) Then Return False
                data = New Date(anno, mese, giorno)
                Return True
            End If
            Dim a As Integer = oggi.Year
            If giorno > DateTime.DaysInMonth(a, mese) Then Return False
            Dim candidata As New Date(a, mese, giorno)
            If soloFuture AndAlso candidata < oggi Then
                If giorno > DateTime.DaysInMonth(a + 1, mese) Then Return False
                candidata = New Date(a + 1, mese, giorno)
            End If
            data = candidata
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Legge una data scritta da sola: "oggi", "domani", "dopodomani", "ieri", "giovedì", "25/12", "25/12/2026", "25 dicembre",
    ''' "il 1º gennaio 2027". soloFuture porta le date senza anno già passate all'anno prossimo.
    ''' </summary>
    Friend Function TentaLeggiData(testo As String, oggi As Date, soloFuture As Boolean, ByRef data As Date) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim t As String = SenzaAccenti(testo).Trim().Replace("º", "").Replace("°", "")
            t = Regex.Replace(t, "^(il|del|per)\s+", "")
            t = Regex.Replace(t, "\s+", " ")
            Select Case t
                Case "oggi" : data = oggi : Return True
                Case "domani" : data = oggi.AddDays(1) : Return True
                Case "dopodomani", "dopo domani" : data = oggi.AddDays(2) : Return True
                Case "ieri" : data = oggi.AddDays(-1) : Return True
            End Select
            Dim numerica As Match = Regex.Match(t, "^(\d{1,2})\s*[/\-.]\s*(\d{1,2})(?:\s*[/\-.]\s*(\d{2,4}))?$")
            If numerica.Success Then
                Dim anno As Integer = If(numerica.Groups(3).Success, Integer.Parse(numerica.Groups(3).Value), 0)
                Return CostruisciData(Integer.Parse(numerica.Groups(1).Value), Integer.Parse(numerica.Groups(2).Value), anno, oggi, soloFuture, data)
            End If
            Dim scritta As Match = Regex.Match(t, "^(\d{1,2}|primo)\s+([a-z\.]+)(?:\s+(\d{4}))?$")
            If scritta.Success Then
                Dim mese As Integer = NumeroMese(scritta.Groups(2).Value)
                If mese = 0 Then Return False
                Dim giorno As Integer = If(scritta.Groups(1).Value = "primo", 1, Integer.Parse(scritta.Groups(1).Value))
                Dim anno As Integer = If(scritta.Groups(3).Success, Integer.Parse(scritta.Groups(3).Value), 0)
                Return CostruisciData(giorno, mese, anno, oggi, soloFuture, data)
            End If
            Dim settimana As Match = Regex.Match(t, "^(?:prossimo\s+)?([a-z]+)(?:\s+prossimo)?$")
            If settimana.Success Then
                Dim giornoSettimana As Integer = NumeroGiorno(settimana.Groups(1).Value)
                If giornoSettimana >= 0 Then
                    data = ProssimoGiorno(oggi, giornoSettimana, False)
                    Return True
                End If
            End If
            Return False
        Catch
            Return False
        End Try
    End Function

    ''' <summary>"giovedì 14 novembre", e con l'anno se non è quello in corso.</summary>
    Friend Function Descrivi(data As Date, oggi As Date) As String
        Try
            Dim testo As String = data.ToString("dddd d MMMM", Italiano)
            If data.Year <> oggi.Year Then testo &= " " & data.Year.ToString()
            Return testo
        Catch
            Return data.ToString("dd/MM/yyyy")
        End Try
    End Function

End Module
