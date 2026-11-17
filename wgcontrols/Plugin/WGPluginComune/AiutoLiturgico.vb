Imports System
Imports System.Collections.Generic
Imports System.Globalization

''' <summary>
''' Calcoli del calendario liturgico della Chiesa cattolica di rito romano (forma ordinaria, per l'Italia), in comune ai plugin Liturgia e
''' Rosario: il tempo liturgico di un giorno, la settimana, il colore, le solennità e le feste del Signore, l'anno A, B o C. Tutto offline.
''' Non ci sono le memorie dei santi (cambiano di giorno in giorno): per quelle c'è il Santo del giorno.
''' </summary>
Friend Module AiutoLiturgico

    Friend Class GiornoLiturgico
        Public Data As Date
        ''' <summary>Per esempio "Giovedì della 32ª settimana del Tempo Ordinario".</summary>
        Public Titolo As String = ""
        ''' <summary>Avvento, Natale, Tempo Ordinario, Quaresima, Triduo pasquale, Tempo di Pasqua.</summary>
        Public Tempo As String = ""
        Public Colore As String = ""
        ''' <summary>Una solennità o una festa del giorno ("Immacolata Concezione"); vuoto se non ce n'è.</summary>
        Public Ricorrenza As String = ""
        ''' <summary>"Anno B, ciclo feriale II".</summary>
        Public Anno As String = ""
    End Class

    Private ReadOnly Italiano As CultureInfo = CultureInfo.GetCultureInfo("it-IT")

    Friend Function NomeGiorno(d As Date) As String
        Return AiutoCalendario.Maiuscola(d.ToString("dddd", Italiano))
    End Function

    ''' <summary>La prima domenica di Avvento dell'anno (comincia l'anno liturgico successivo).</summary>
    Friend Function Avvento(anno As Integer) As Date
        Dim natale As New Date(anno, 12, 25)
        Dim giorni As Integer = If(natale.DayOfWeek = DayOfWeek.Sunday, 7, CInt(natale.DayOfWeek))
        Return natale.AddDays(-giorni - 21)
    End Function

    ''' <summary>La domenica dopo il 6 gennaio: il Battesimo del Signore.</summary>
    Friend Function Battesimo(anno As Integer) As Date
        Dim epifania As New Date(anno, 1, 6)
        Return epifania.AddDays(7 - CInt(epifania.DayOfWeek))
    End Function

    ''' <summary>L'anno liturgico (A, B o C) e il ciclo feriale (I o II) del giorno.</summary>
    Friend Function AnnoLiturgico(d As Date) As String
        Dim fine As Integer = If(d >= Avvento(d.Year), d.Year + 1, d.Year)
        Dim lettera As String = New String() {"C", "A", "B"}(fine Mod 3)
        Dim ciclo As String = If(d.Year Mod 2 = 1, "I", "II")
        ' dall'Avvento al 31 dicembre il ciclo feriale è quello dell'anno che comincia
        If d >= Avvento(d.Year) Then ciclo = If((d.Year + 1) Mod 2 = 1, "I", "II")
        Return "Anno " & lettera & ", ciclo feriale " & ciclo
    End Function

    Private Function Ord(n As Integer) As String
        Return n.ToString() & "ª"
    End Function

    ''' <summary>La domenica che apre la settimana del giorno (se il giorno è una domenica, il giorno stesso).</summary>
    Private Function DomenicaDellaSettimana(d As Date) As Date
        Return d.AddDays(-CInt(d.DayOfWeek))
    End Function

    ''' <summary>Solennità e feste del Signore a data fissa (hanno la precedenza sulle domeniche del Tempo Ordinario): nome, giorno, mese, colore.</summary>
    Private ReadOnly Fisse As String(,) = {
        {"Maria Santissima Madre di Dio", "1", "1", "bianco"}, {"Epifania del Signore", "6", "1", "bianco"},
        {"Presentazione del Signore", "2", "2", "bianco"}, {"San Giuseppe, sposo della Beata Vergine Maria", "19", "3", "bianco"},
        {"Annunciazione del Signore", "25", "3", "bianco"}, {"Natività di San Giovanni Battista", "24", "6", "bianco"},
        {"Santi Pietro e Paolo, apostoli", "29", "6", "rosso"}, {"Trasfigurazione del Signore", "6", "8", "bianco"},
        {"Assunzione della Beata Vergine Maria", "15", "8", "bianco"}, {"Esaltazione della Santa Croce", "14", "9", "rosso"},
        {"Tutti i Santi", "1", "11", "bianco"}, {"Commemorazione di tutti i fedeli defunti", "2", "11", "viola"},
        {"Dedicazione della Basilica Lateranense", "9", "11", "bianco"}, {"Immacolata Concezione della Beata Vergine Maria", "8", "12", "bianco"},
        {"Natale del Signore", "25", "12", "bianco"}}

    ''' <summary>
    ''' Le solennità e le feste fisse dell'anno con la data in cui si celebrano davvero: quelle che cadono in una domenica di Avvento,
    ''' Quaresima o Pasqua, nella Settimana Santa o nell'Ottava di Pasqua si spostano.
    ''' </summary>
    Friend Function CelebrazioniFisse(anno As Integer) As List(Of KeyValuePair(Of Date, String()))
        Dim risultato As New List(Of KeyValuePair(Of Date, String()))()
        Dim pasqua As Date = AiutoCalendario.Pasqua(anno)
        For i As Integer = 0 To Fisse.GetLength(0) - 1
            Dim data As New Date(anno, Integer.Parse(Fisse(i, 2)), Integer.Parse(Fisse(i, 1)))
            Dim nome As String = Fisse(i, 0)
            Dim domenica As Boolean = data.DayOfWeek = DayOfWeek.Sunday
            Dim tempo As String = TempoDi(data, anno)
            If nome.StartsWith("Immacolata") AndAlso domenica AndAlso tempo = "Avvento" Then data = data.AddDays(1)
            If nome.StartsWith("San Giuseppe") Then
                If domenica AndAlso tempo = "Quaresima" Then
                    data = data.AddDays(1)
                ElseIf data > pasqua.AddDays(-8) AndAlso data < pasqua Then
                    data = pasqua.AddDays(-8)
                End If
            End If
            If nome.StartsWith("Annunciazione") Then
                If domenica AndAlso tempo = "Quaresima" Then
                    data = data.AddDays(1)
                ElseIf data >= pasqua.AddDays(-7) AndAlso data <= pasqua.AddDays(7) Then
                    data = pasqua.AddDays(8)
                End If
            End If
            risultato.Add(New KeyValuePair(Of Date, String())(data, New String() {nome, Fisse(i, 3)}))
        Next
        Return risultato
    End Function

    ''' <summary>Il tempo liturgico del giorno (nell'anno civile dato).</summary>
    Friend Function TempoDi(d As Date, anno As Integer) As String
        Dim pasqua As Date = AiutoCalendario.Pasqua(anno)
        If d >= Avvento(anno) Then Return If(d < New Date(anno, 12, 25), "Avvento", "Natale")
        If d <= Battesimo(anno) Then Return "Natale"
        If d < pasqua.AddDays(-46) Then Return "Tempo Ordinario"
        If d < pasqua.AddDays(-3) Then Return "Quaresima"
        If d < pasqua Then Return "Triduo pasquale"
        If d <= pasqua.AddDays(49) Then Return "Tempo di Pasqua"
        Return "Tempo Ordinario"
    End Function

    ''' <summary>Il tempo liturgico (Avvento, Natale, Quaresima, Pasqua o Tempo Ordinario) del giorno.</summary>
    Friend Function Tempo(d As Date) As String
        Return TempoDi(d, d.Year)
    End Function

    ''' <summary>Tutto quello che si sa del giorno liturgico.</summary>
    Friend Function Descrivi(d As Date) As GiornoLiturgico
        Dim g As New GiornoLiturgico()
        g.Data = d.Date
        Dim anno As Integer = d.Year
        Dim pasqua As Date = AiutoCalendario.Pasqua(anno)
        Dim pentecoste As Date = pasqua.AddDays(49)
        Dim avvento1 As Date = Avvento(anno)
        Dim cristoRe As Date = avvento1.AddDays(-7)
        Dim battesimo1 As Date = Battesimo(anno)
        Dim giorno As String = NomeGiorno(d)
        Dim domenica As Boolean = d.DayOfWeek = DayOfWeek.Sunday
        Dim inizioSettimana As Date = DomenicaDellaSettimana(d)
        g.Tempo = TempoDi(d, anno)
        g.Anno = AnnoLiturgico(d)

        Select Case g.Tempo
            Case "Avvento"
                Dim n As Integer = CInt((inizioSettimana - avvento1).TotalDays) \ 7 + 1
                g.Colore = If(domenica AndAlso n = 3, "rosa", "viola")
                If domenica Then
                    g.Titolo = Ord(n) & " domenica di Avvento" & If(n = 3, " (domenica Gaudete)", "")
                ElseIf d.Month = 12 AndAlso d.Day >= 17 Then
                    g.Titolo = "Feria di Avvento, novena di Natale"
                Else
                    g.Titolo = giorno & " della " & Ord(n) & " settimana di Avvento"
                End If
            Case "Natale"
                g.Colore = "bianco"
                Dim natale As New Date(If(d.Month = 12, anno, anno - 1), 12, 25)
                If d.Month = 12 AndAlso d.Day = 25 Then
                    g.Titolo = "Natale del Signore"
                ElseIf d.Month = 12 Then
                    Dim santaFamiglia As Date = If(natale.DayOfWeek = DayOfWeek.Sunday, New Date(anno, 12, 30), natale.AddDays(7 - CInt(natale.DayOfWeek)))
                    If d = santaFamiglia Then
                        g.Titolo = "Santa Famiglia di Gesù, Maria e Giuseppe"
                    ElseIf d.Day = 26 Then
                        g.Titolo = "Santo Stefano, primo martire"
                        g.Colore = "rosso"
                    ElseIf d.Day = 27 Then
                        g.Titolo = "San Giovanni, apostolo ed evangelista"
                    ElseIf d.Day = 28 Then
                        g.Titolo = "Santi Innocenti, martiri"
                        g.Colore = "rosso"
                    Else
                        g.Titolo = giorno & " dell'Ottava di Natale"
                    End If
                ElseIf d = New Date(anno, 1, 1) Then
                    g.Titolo = "Maria Santissima Madre di Dio"
                ElseIf d = New Date(anno, 1, 6) Then
                    g.Titolo = "Epifania del Signore"
                ElseIf d = battesimo1 Then
                    g.Titolo = "Battesimo del Signore"
                ElseIf domenica AndAlso d.Day <= 5 Then
                    g.Titolo = "2ª domenica dopo Natale"
                ElseIf d < New Date(anno, 1, 6) Then
                    g.Titolo = giorno & " del Tempo di Natale"
                Else
                    g.Titolo = giorno & " dopo l'Epifania"
                End If
            Case "Quaresima"
                g.Colore = "viola"
                Dim primaDomenica As Date = pasqua.AddDays(-42)
                If d = pasqua.AddDays(-46) Then
                    g.Titolo = "Mercoledì delle Ceneri"
                ElseIf d < primaDomenica Then
                    g.Titolo = giorno & " dopo le Ceneri"
                ElseIf d = pasqua.AddDays(-7) Then
                    g.Titolo = "Domenica delle Palme e della Passione del Signore"
                    g.Colore = "rosso"
                ElseIf d > pasqua.AddDays(-7) Then
                    g.Titolo = giorno & " della Settimana Santa"
                Else
                    Dim n As Integer = CInt((inizioSettimana - primaDomenica).TotalDays) \ 7 + 1
                    If domenica Then
                        g.Titolo = Ord(n) & " domenica di Quaresima" & If(n = 4, " (domenica Laetare)", "")
                        If n = 4 Then g.Colore = "rosa"
                    Else
                        g.Titolo = giorno & " della " & Ord(n) & " settimana di Quaresima"
                    End If
                End If
            Case "Triduo pasquale"
                If d = pasqua.AddDays(-3) Then
                    g.Titolo = "Giovedì santo, Messa nella Cena del Signore"
                    g.Colore = "bianco"
                ElseIf d = pasqua.AddDays(-2) Then
                    g.Titolo = "Venerdì santo, Passione del Signore"
                    g.Colore = "rosso"
                Else
                    g.Titolo = "Sabato santo, la sera la Veglia pasquale"
                    g.Colore = "bianco"
                End If
            Case "Tempo di Pasqua"
                g.Colore = "bianco"
                If d = pasqua Then
                    g.Titolo = "Pasqua di Risurrezione del Signore"
                ElseIf d < pasqua.AddDays(7) Then
                    g.Titolo = giorno & " dell'Ottava di Pasqua"
                ElseIf d = pentecoste Then
                    g.Titolo = "Domenica di Pentecoste"
                    g.Colore = "rosso"
                ElseIf d = pasqua.AddDays(42) Then
                    g.Titolo = "Ascensione del Signore (7ª domenica di Pasqua)"
                ElseIf d = pasqua.AddDays(7) Then
                    g.Titolo = "2ª domenica di Pasqua, della Divina Misericordia"
                Else
                    Dim n As Integer = CInt((inizioSettimana - pasqua).TotalDays) \ 7 + 1
                    If domenica Then g.Titolo = Ord(n) & " domenica di Pasqua" Else g.Titolo = giorno & " della " & Ord(n) & " settimana di Pasqua"
                End If
            Case Else
                g.Colore = "verde"
                Dim n As Integer
                If d < pasqua Then
                    n = CInt((inizioSettimana - battesimo1).TotalDays) \ 7 + 1
                Else
                    n = 34 - CInt((cristoRe - inizioSettimana).TotalDays) \ 7
                End If
                If d = cristoRe Then
                    g.Titolo = "Nostro Signore Gesù Cristo Re dell'Universo (34ª domenica del Tempo Ordinario)"
                    g.Colore = "bianco"
                ElseIf d = pentecoste.AddDays(7) Then
                    g.Titolo = "Santissima Trinità"
                    g.Colore = "bianco"
                ElseIf d = pentecoste.AddDays(14) Then
                    g.Titolo = "Santissimo Corpo e Sangue di Cristo (Corpus Domini)"
                    g.Colore = "bianco"
                ElseIf d = pentecoste.AddDays(19) Then
                    g.Titolo = "Sacratissimo Cuore di Gesù"
                    g.Colore = "bianco"
                ElseIf d = pentecoste.AddDays(1) Then
                    g.Titolo = "Beata Vergine Maria, Madre della Chiesa"
                    g.Colore = "bianco"
                ElseIf domenica Then
                    g.Titolo = Ord(n) & " domenica del Tempo Ordinario"
                Else
                    g.Titolo = giorno & " della " & Ord(n) & " settimana del Tempo Ordinario"
                End If
        End Select

        ' solennità e feste fisse: contano se non sono spostate e non cadono dove la precedenza è del tempo
        For Each c As KeyValuePair(Of Date, String()) In CelebrazioniFisse(anno)
            If c.Key <> d.Date Then Continue For
            Dim inTriduoOOttava As Boolean = d >= pasqua.AddDays(-7) AndAlso d <= pasqua.AddDays(7)
            If inTriduoOOttava Then Continue For
            If domenica AndAlso (g.Tempo = "Avvento" OrElse g.Tempo = "Quaresima" OrElse g.Tempo = "Tempo di Pasqua") Then Continue For
            g.Colore = c.Value(1)
            ' se il titolo del giorno dice già la stessa cosa (Natale, Epifania...) non si ripete come ricorrenza
            If g.Titolo.IndexOf(c.Value(0).Substring(0, Math.Min(12, c.Value(0).Length)), StringComparison.OrdinalIgnoreCase) < 0 Then g.Ricorrenza = c.Value(0)
            Exit For
        Next
        Return g
    End Function

    ''' <summary>Le solennità e le feste principali di un anno, in ordine di data: nome e colore.</summary>
    Friend Function CelebrazioniDelAnno(anno As Integer) As List(Of KeyValuePair(Of Date, String()))
        Dim elenco As New List(Of KeyValuePair(Of Date, String()))(CelebrazioniFisse(anno))
        Dim pasqua As Date = AiutoCalendario.Pasqua(anno)
        elenco.Add(New KeyValuePair(Of Date, String())(Avvento(anno), New String() {"1ª domenica di Avvento, inizio dell'anno liturgico", "viola"}))
        elenco.Add(New KeyValuePair(Of Date, String())(Battesimo(anno), New String() {"Battesimo del Signore", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(-46), New String() {"Mercoledì delle Ceneri, inizio della Quaresima", "viola"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(-7), New String() {"Domenica delle Palme e della Passione del Signore", "rosso"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(-3), New String() {"Giovedì santo", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(-2), New String() {"Venerdì santo", "rosso"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua, New String() {"Pasqua di Risurrezione del Signore", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(7), New String() {"Domenica della Divina Misericordia", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(42), New String() {"Ascensione del Signore", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(49), New String() {"Pentecoste", "rosso"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(56), New String() {"Santissima Trinità", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(63), New String() {"Corpus Domini", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(pasqua.AddDays(68), New String() {"Sacratissimo Cuore di Gesù", "bianco"}))
        elenco.Add(New KeyValuePair(Of Date, String())(Avvento(anno).AddDays(-7), New String() {"Cristo Re dell'Universo", "bianco"}))
        elenco.Sort(Function(x, y) x.Key.CompareTo(y.Key))
        Return elenco
    End Function

End Module
