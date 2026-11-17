Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.RegularExpressions
Imports WGPlugin

''' <summary>
''' Appuntamenti smart: si scrive una frase come "dentista giovedì alle 15" o "cena con Marco il 25 dicembre dalle 20 alle 23" e il
''' plugin crea l'appuntamento nell'agenda. Capisce giorno (oggi, domani, un giorno della settimana, una data, "tra 3 giorni"), ora
''' (alle 15, alle 9 e 30, di sera, a mezzogiorno, dalle 9 alle 10) e durata (per un'ora, per 30 minuti). Quello che resta della frase è
''' l'oggetto. Dice sempre come ha capito la frase. Serve il permesso di aggiungere appuntamenti all'agenda.
''' </summary>
Public Class PluginAppuntamentiSmart
    Inherits WGPluginBase

    ''' <summary>Quello che il plugin ha capito da una frase.</summary>
    Friend Class AppuntamentoLetto
        Public Oggetto As String = ""
        Public Data As Date
        ''' <summary>"HH:mm", oppure vuoto se l'appuntamento è per tutto il giorno.</summary>
        Public OraInizio As String = ""
        Public OraFine As String = ""
        ''' <summary>Vero se l'ora non era scritta e l'ha scelta il plugin dalla parte del giorno (mattina, pomeriggio, sera).</summary>
        Public OraStimata As Boolean
        ''' <summary>Vero se nella frase c'era "ogni ...": il plugin crea un solo appuntamento.</summary>
        Public Ricorrente As Boolean
    End Class

    Private _servizi As IWGServiziPlugin

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Appuntamenti smart"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Crea gli appuntamenti in agenda da una frase, come dentista giovedì alle 15."
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
            ' sta in un posto solo: nel menu Organizer, dopo Agenda
            servizi.AggiungiVoceAMenu("Menù Organizer", "Appuntamenti smart", AddressOf Apri, "Agenda")
        Catch ex As Exception
            servizi.RegistraErrore("PluginAppuntamentiSmart.Avvia", ex)
        End Try
    End Sub

    Private Async Sub Apri()
        Try
            Dim concesso As Boolean = Await _servizi.RichiediAccessoAsync(WGPermessi.AgendaScrittura, "Per aggiungere in agenda gli appuntamenti che scrivi")
            If Not concesso Then
                _servizi.Messaggio("Senza il permesso non posso aggiungere appuntamenti.")
                Return
            End If
            _servizi.ChiediTesto("Appuntamento", "", AddressOf Crea)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginAppuntamentiSmart.Apri", ex)
        End Try
    End Sub

    Private Sub Crea(frase As String)
        Try
            Dim letto As AppuntamentoLetto = Nothing
            Dim problema As String = ""
            If Not Interpreta(frase, DateTime.Now, letto, problema) Then
                _servizi.Messaggio(problema)
                Return
            End If
            Dim riuscito As Boolean = _servizi.AggiungiAppuntamento(letto.Oggetto, letto.Data, letto.OraInizio, letto.OraFine, "")
            If riuscito Then
                _servizi.Messaggio("Appuntamento aggiunto: " & Descrivi(letto, DateTime.Today))
            Else
                _servizi.Messaggio("Non sono riuscito ad aggiungere l'appuntamento. Controlla che l'agenda predefinita non sia protetta da password.")
            End If
        Catch ex As Exception
            _servizi.RegistraErrore("PluginAppuntamentiSmart.Crea", ex)
        End Try
    End Sub

    ''' <summary>Dice l'appuntamento come lo direbbe una persona: "dentista, giovedì 14 novembre, dalle 15:00 alle 16:00".</summary>
    Friend Shared Function Descrivi(letto As AppuntamentoLetto, oggi As Date) As String
        Dim testo As String = letto.Oggetto & ", " & AiutoData.Descrivi(letto.Data, oggi)
        If letto.OraInizio = "" Then
            testo &= ", tutto il giorno"
        ElseIf letto.OraFine <> "" Then
            testo &= ", dalle " & letto.OraInizio & " alle " & letto.OraFine
        Else
            testo &= ", alle " & letto.OraInizio
        End If
        If letto.OraStimata Then testo &= ". L'ora non era scritta: ho messo quella della parte del giorno"
        If letto.Ricorrente Then testo &= ". Ho creato un solo appuntamento: le ripetizioni non le gestisco"
        Return testo & "."
    End Function

#Region "Capire la frase"

    ''' <summary>Cerca la prima corrispondenza, la toglie dalla frase (al suo posto uno spazio) e la restituisce; Nothing se non c'è.</summary>
    Private Shared Function Prendi(ByRef frase As String, modello As String) As Match
        Dim m As Match = Regex.Match(frase, modello, RegexOptions.IgnoreCase)
        If Not m.Success Then Return Nothing
        frase = frase.Substring(0, m.Index) & " " & frase.Substring(m.Index + m.Length)
        Return m
    End Function

    Private Shared Function NumeroDaParola(parola As String) As Integer
        Dim p As String = AiutoData.SenzaAccenti(parola).Trim().Trim("'"c, ChrW(&H2019))
        Dim n As Integer
        If Integer.TryParse(p, n) Then Return n
        Select Case p
            Case "un", "uno", "una" : Return 1
            Case "due" : Return 2
            Case "tre" : Return 3
            Case "quattro" : Return 4
            Case "cinque" : Return 5
            Case "sei" : Return 6
            Case "sette" : Return 7
            Case "otto" : Return 8
            Case "nove" : Return 9
            Case "dieci" : Return 10
            Case "undici" : Return 11
            Case "dodici" : Return 12
        End Select
        Return -1
    End Function

    Private Const NumeroOParola As String = "\d{1,2}|un|uno|una|due|tre|quattro|cinque|sei|sette|otto|nove|dieci|undici|dodici"

    ''' <summary>Minuti dall'inizio del giorno, oppure -1 se l'ora non è valida.</summary>
    Private Shared Function Minuti(ora As Integer, minuto As Integer) As Integer
        If ora < 0 OrElse ora > 24 OrElse minuto < 0 OrElse minuto > 59 Then Return -1
        If ora = 24 Then
            If minuto <> 0 Then Return -1
            Return 0
        End If
        Return ora * 60 + minuto
    End Function

    Private Shared Function InOra(minutiDelGiorno As Integer) As String
        Dim m As Integer = ((minutiDelGiorno Mod 1440) + 1440) Mod 1440
        Return (m \ 60).ToString("00") & ":" & (m Mod 60).ToString("00")
    End Function

    ''' <summary>
    ''' Interpreta la frase. Restituisce Falso, con il problema già scritto in modo che si possa dire all'utente, se manca la data o
    ''' qualcosa non torna.
    ''' </summary>
    Friend Shared Function Interpreta(testo As String, adesso As DateTime, ByRef letto As AppuntamentoLetto, ByRef problema As String) As Boolean
        letto = Nothing
        problema = ""
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                problema = "Non hai scritto niente."
                Return False
            End If
            Dim frase As String = " " & testo.Trim() & " "
            Dim oggi As Date = adesso.Date
            Dim a As New AppuntamentoLetto()

            ' "ogni ...": si crea un solo appuntamento, il primo; "ogni lunedì" vale come "lunedì", "ogni giorno" come oggi
            Dim ogniGiorno As Boolean = False
            If Regex.IsMatch(frase, "\bogni\b", RegexOptions.IgnoreCase) Then
                a.Ricorrente = True
                If Regex.IsMatch(frase, "\bogni\s+giorno\b", RegexOptions.IgnoreCase) Then ogniGiorno = True
                frase = Regex.Replace(frase, "\bogni\s+(?:giorno|settimana|mese|anno)\b", " ", RegexOptions.IgnoreCase)
                frase = Regex.Replace(frase, "\bogni\b", " ", RegexOptions.IgnoreCase)
            End If

            ' --- durata: per un'ora, per 30 minuti, per mezz'ora
            Dim durata As Integer = 0
            Dim mezza As Match = Prendi(frase, "\bper\s+mezz['’]?\s*ora\b|\bper\s+mezza\s+ora\b")
            If mezza IsNot Nothing Then durata = 30
            If durata = 0 Then
                Dim d As Match = Prendi(frase, "\bper\s+(" & NumeroOParola & ")\s*['’]?\s*(or[ae]|minut[io]|min)\b")
                If d IsNot Nothing Then
                    Dim n As Integer = NumeroDaParola(d.Groups(1).Value)
                    If n > 0 Then durata = If(d.Groups(2).Value.ToLowerInvariant().StartsWith("or"), n * 60, n)
                End If
            End If

            ' --- tra un'ora, fra mezz'ora, fra 20 minuti: l'ora è adesso più quel tempo
            Dim fissa As Boolean = False
            Dim relativo As Integer = -1
            Dim dataRelativa As Date = Date.MinValue
            Dim minutiTra As Integer = 0
            If Prendi(frase, "\b(?:tra|fra)\s+(?:mezz['’]?\s*ora|mezza\s+ora)\b") IsNot Nothing Then minutiTra = 30
            If minutiTra = 0 AndAlso Prendi(frase, "\b(?:tra|fra)\s+(?:un\s+)?quarto\s+d['’]?\s*ora\b") IsNot Nothing Then minutiTra = 15
            If minutiTra = 0 Then
                Dim tra As Match = Prendi(frase, "\b(?:tra|fra)\s+(" & NumeroOParola & ")\s*['’]?\s*(or[ae]|minut[io]|min)\b")
                If tra IsNot Nothing Then
                    Dim quanti As Integer = NumeroDaParola(tra.Groups(1).Value)
                    If quanti > 0 Then minutiTra = If(tra.Groups(2).Value.ToLowerInvariant().StartsWith("or"), quanti * 60, quanti)
                End If
            End If
            If minutiTra > 0 Then
                Dim momento As DateTime = adesso.AddMinutes(minutiTra)
                dataRelativa = momento.Date
                relativo = momento.Hour * 60 + momento.Minute
                fissa = True
            End If

            ' --- ora
            Dim inizio As Integer = -1
            Dim fine As Integer = -1
            Dim parteDelGiorno As String = ""
            Dim intervallo As Match = Prendi(frase, "\bdalle\s+(\d{1,2})(?:\s*[:.]\s*(\d{2}))?\s+(?:alle|a)\s+(\d{1,2})(?:\s*[:.]\s*(\d{2}))?\b")
            If intervallo IsNot Nothing Then
                inizio = Minuti(Integer.Parse(intervallo.Groups(1).Value), If(intervallo.Groups(2).Success, Integer.Parse(intervallo.Groups(2).Value), 0))
                fine = Minuti(Integer.Parse(intervallo.Groups(3).Value), If(intervallo.Groups(4).Success, Integer.Parse(intervallo.Groups(4).Value), 0))
                If inizio < 0 OrElse fine < 0 Then
                    problema = "Non ho capito le ore."
                    Return False
                End If
            End If

            ' parte del giorno (anche dentro stasera, stamattina, domattina)
            Dim dataDaParola As Date = Date.MinValue
            Dim hoData As Boolean = False
            If Prendi(frase, "\bstasera\b") IsNot Nothing Then
                dataDaParola = oggi : hoData = True : parteDelGiorno = "sera"
            ElseIf Prendi(frase, "\bstamattina\b") IsNot Nothing Then
                dataDaParola = oggi : hoData = True : parteDelGiorno = "mattina"
            ElseIf Prendi(frase, "\bstanotte\b") IsNot Nothing Then
                dataDaParola = oggi : hoData = True : parteDelGiorno = "notte"
            ElseIf Prendi(frase, "\bdomattina\b") IsNot Nothing Then
                dataDaParola = oggi.AddDays(1) : hoData = True : parteDelGiorno = "mattina"
            End If
            ' "questa sera", "questo pomeriggio": oggi
            If parteDelGiorno = "" Then
                Dim questa As Match = Prendi(frase, "\bquest[ao]\s+(mattina|mattinata|pomeriggio|sera|serata|notte)\b")
                If questa IsNot Nothing Then
                    dataDaParola = oggi : hoData = True
                    parteDelGiorno = NomeParte(questa.Groups(1).Value)
                End If
            End If
            If parteDelGiorno = "" Then
                Dim p As Match = Prendi(frase, "\b(?:di|la|nella|del|nel|in)\s+(mattina|mattinata|pomeriggio|sera|serata|notte)\b")
                If p IsNot Nothing Then
                    Dim v As String = p.Groups(1).Value.ToLowerInvariant()
                    parteDelGiorno = If(v.StartsWith("matt"), "mattina", If(v.StartsWith("pom"), "pomeriggio", If(v.StartsWith("ser"), "sera", "notte")))
                End If
            End If
            If parteDelGiorno = "" Then
                Dim p2 As Match = Prendi(frase, "\b(mattina|pomeriggio|sera|notte)\b")
                If p2 IsNot Nothing Then parteDelGiorno = p2.Groups(1).Value.ToLowerInvariant()
            End If

            If fissa Then inizio = relativo
            If inizio < 0 Then
                If Prendi(frase, "\ba\s+mezzogiorno\b") IsNot Nothing Then
                    inizio = 12 * 60
                ElseIf Prendi(frase, "\ba\s+mezzanotte\b") IsNot Nothing Then
                    inizio = 0
                Else
                    Dim o As Match = Prendi(frase, "\b(?:alle|ore|all['’])\s*(" & NumeroOParola & ")(?:\s*[:.,]\s*(\d{2}))?(?:\s+e\s+(mezza|mezzo|un\s+quarto|tre\s+quarti|\d{1,2}))?\b")
                    If o Is Nothing Then o = Prendi(frase, "\b(\d{1,2})\s*:\s*(\d{2})()\b")
                    If o IsNot Nothing Then
                        Dim ora As Integer = NumeroDaParola(o.Groups(1).Value)
                        Dim minuto As Integer = 0
                        If o.Groups(2).Success AndAlso o.Groups(2).Value <> "" Then minuto = Integer.Parse(o.Groups(2).Value)
                        If o.Groups(3).Success AndAlso o.Groups(3).Value <> "" Then
                            Dim extra As String = AiutoData.SenzaAccenti(o.Groups(3).Value).Replace(" ", "")
                            If extra = "mezza" OrElse extra = "mezzo" Then
                                minuto = 30
                            ElseIf extra = "unquarto" Then
                                minuto = 15
                            ElseIf extra = "trequarti" Then
                                minuto = 45
                            Else
                                minuto = Integer.Parse(extra)
                            End If
                        End If
                        If ora < 0 Then
                            problema = "Non ho capito l'ora."
                            Return False
                        End If
                        inizio = Minuti(ora, minuto)
                        If inizio < 0 Then
                            problema = "Non ho capito l'ora: " & ora.ToString() & " e " & minuto.ToString() & " non è un'ora valida."
                            Return False
                        End If
                        ' ore dette senza "di sera" o "di mattina": dall'una alle sei si intende il pomeriggio
                        If parteDelGiorno = "" AndAlso ora >= 1 AndAlso ora <= 6 Then inizio += 12 * 60
                        If parteDelGiorno = "pomeriggio" OrElse parteDelGiorno = "sera" Then
                            If ora >= 1 AndAlso ora < 12 Then inizio = Minuti(ora + 12, minuto)
                        ElseIf parteDelGiorno = "notte" Then
                            If ora = 12 Then inizio = Minuti(0, minuto)
                            If ora >= 6 AndAlso ora < 12 Then inizio = Minuti(ora + 12, minuto)
                        End If
                    End If
                End If
            ElseIf Not fissa Then
                ' "dalle 9 alle 10 di sera": le due ore seguono la parte del giorno
                If parteDelGiorno = "pomeriggio" OrElse parteDelGiorno = "sera" Then
                    If inizio < 12 * 60 Then inizio += 12 * 60
                    If fine < 12 * 60 Then fine += 12 * 60
                ElseIf parteDelGiorno = "" Then
                    If inizio >= 60 AndAlso inizio < 7 * 60 Then
                        inizio += 12 * 60
                        If fine < 12 * 60 Then fine += 12 * 60
                    End If
                End If
            End If
            If inizio < 0 AndAlso parteDelGiorno <> "" Then
                Select Case parteDelGiorno
                    Case "mattina" : inizio = 9 * 60
                    Case "pomeriggio" : inizio = 15 * 60
                    Case "sera" : inizio = 20 * 60 + 30
                    Case Else : inizio = 22 * 60
                End Select
                a.OraStimata = True
            End If
            ' pranzo, cena e simili senza un'ora scritta: l'ora tipica del pasto (la parola resta nell'oggetto)
            If inizio < 0 AndAlso parteDelGiorno = "" Then
                Dim pasto As Match = Regex.Match(frase, "\b(colazione|brunch|pranzo|aperitivo|cena)\b", RegexOptions.IgnoreCase)
                If pasto.Success Then
                    Select Case pasto.Value.ToLowerInvariant()
                        Case "colazione" : inizio = 8 * 60 + 30
                        Case "brunch" : inizio = 11 * 60
                        Case "pranzo" : inizio = 13 * 60
                        Case "aperitivo" : inizio = 19 * 60
                        Case Else : inizio = 20 * 60
                    End Select
                    a.OraStimata = True
                End If
            End If
            If inizio >= 0 Then
                If fine < 0 AndAlso durata > 0 Then fine = inizio + durata
                If fine >= 0 AndAlso fine <= inizio Then fine += 24 * 60
                If fine > inizio AndAlso fine - inizio > 24 * 60 Then fine = -1
                a.OraInizio = InOra(inizio)
                If fine >= 0 Then a.OraFine = InOra(fine)
            End If

            ' --- data
            Dim data As Date = Date.MinValue
            If fissa Then
                data = dataRelativa
            ElseIf hoData Then
                data = dataDaParola
            Else
                ' "la settimana prossima" (con o senza un giorno), il fine settimana, la fine del mese
                Dim settimanaProssima As Boolean = Prendi(frase, "\b(?:la\s+)?(?:settimana\s+prossima|prossima\s+settimana)\b") IsNot Nothing
                If Prendi(frase, "\b(?:nel\s+|il\s+)?(?:weekend|fine\s+settimana)\b") IsNot Nothing Then
                    data = AiutoData.ProssimoGiorno(oggi, 6, True)
                ElseIf Prendi(frase, "\b(?:a\s+)?fine\s+mese\b") IsNot Nothing Then
                    data = New Date(oggi.Year, oggi.Month, DateTime.DaysInMonth(oggi.Year, oggi.Month))
                End If
                If data = Date.MinValue Then data = DataFesta(frase, oggi)
                Dim fra As Match = Prendi(frase, "\b(?:tra|fra)\s+(" & NumeroOParola & ")\s+(giorni|giorno|settimane|settimana|mesi|mese)\b")
                If fra IsNot Nothing Then
                    Dim n As Integer = NumeroDaParola(fra.Groups(1).Value)
                    Dim unita As String = fra.Groups(2).Value.ToLowerInvariant()
                    If n > 0 Then
                        If unita.StartsWith("gior") Then
                            data = oggi.AddDays(n)
                        ElseIf unita.StartsWith("sett") Then
                            data = oggi.AddDays(7 * n)
                        Else
                            data = oggi.AddMonths(n)
                        End If
                    End If
                ElseIf Prendi(frase, "\bdopo\s?domani\b") IsNot Nothing Then
                    data = oggi.AddDays(2)
                ElseIf Prendi(frase, "\bdomani\b") IsNot Nothing Then
                    data = oggi.AddDays(1)
                ElseIf Prendi(frase, "\boggi\b") IsNot Nothing Then
                    data = oggi
                End If
                If data = Date.MinValue Then
                    Dim num As Match = Prendi(frase, "\b(\d{1,2})\s*[/\-]\s*(\d{1,2})(?:\s*[/\-]\s*(\d{2,4}))?\b")
                    If num IsNot Nothing Then
                        Dim anno As Integer = If(num.Groups(3).Success, Integer.Parse(num.Groups(3).Value), 0)
                        If Not AiutoData.CostruisciData(Integer.Parse(num.Groups(1).Value), Integer.Parse(num.Groups(2).Value), anno, oggi, True, data) Then
                            problema = "Non ho capito la data."
                            Return False
                        End If
                    End If
                End If
                If data = Date.MinValue Then
                    Dim scritta As Match = Prendi(frase, "\b(?:il\s+)?(\d{1,2}|1º|primo)\s+(gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre|gen|feb|mar|apr|mag|giu|lug|ago|set|sett|ott|nov|dic)\b\.?(?:\s+(\d{4}))?")
                    If scritta IsNot Nothing Then
                        Dim g As String = scritta.Groups(1).Value.Replace("º", "")
                        Dim giorno As Integer = If(g.ToLowerInvariant() = "primo", 1, Integer.Parse(g))
                        Dim anno As Integer = If(scritta.Groups(3).Success, Integer.Parse(scritta.Groups(3).Value), 0)
                        If Not AiutoData.CostruisciData(giorno, AiutoData.NumeroMese(scritta.Groups(2).Value), anno, oggi, True, data) Then
                            problema = "Non ho capito la data."
                            Return False
                        End If
                    End If
                End If
                If data = Date.MinValue Then
                    Dim sett As Match = Prendi(frase, "\b(?:(?:di|il|per|questo|prossimo|prossima)\s+)?(luned[iì]|marted[iì]|mercoled[iì]|gioved[iì]|venerd[iì]|sabato|domenica)\b(?:\s+prossim[oa])?")
                    If sett IsNot Nothing Then
                        Dim giornoSettimana As Integer = AiutoData.NumeroGiorno(sett.Groups(1).Value)
                        If settimanaProssima Then
                            ' lunedì della settimana prossima, contando da lunedì
                            Dim finoAlLunedi As Integer = (8 - CInt(oggi.DayOfWeek)) Mod 7
                            If finoAlLunedi = 0 Then finoAlLunedi = 7
                            data = oggi.AddDays(finoAlLunedi + ((giornoSettimana + 6) Mod 7))
                        Else
                            data = AiutoData.ProssimoGiorno(oggi, giornoSettimana, False)
                        End If
                    End If
                End If
                If data = Date.MinValue AndAlso settimanaProssima Then data = oggi.AddDays(7)
                If data = Date.MinValue Then
                    Dim solo As Match = Prendi(frase, "\bil\s+(\d{1,2})\b")
                    If solo IsNot Nothing Then
                        Dim giorno As Integer = Integer.Parse(solo.Groups(1).Value)
                        Dim candidata As Date
                        If Not AiutoData.CostruisciData(giorno, oggi.Month, 0, oggi, False, candidata) OrElse candidata < oggi Then
                            Dim prossimo As Date = oggi.AddMonths(1)
                            If Not AiutoData.CostruisciData(giorno, prossimo.Month, prossimo.Year, oggi, False, candidata) Then
                                problema = "Non ho capito la data."
                                Return False
                            End If
                        End If
                        data = candidata
                    End If
                End If
            End If
            If data = Date.MinValue AndAlso ogniGiorno Then data = oggi
            If data = Date.MinValue Then
                problema = "Non ho capito la data. Scrivi per esempio domani, giovedì o 25 dicembre."
                Return False
            End If
            If data < oggi Then
                problema = "Quella data è già passata."
                Return False
            End If
            a.Data = data

            ' --- oggetto: quello che resta, senza le parole di collegamento
            Dim oggetto As String = Regex.Replace(frase, "\s+", " ").Trim()
            oggetto = Regex.Replace(oggetto, "^(?:(?:ricordami|ricordami di|ricordati di|mettimi|metti|segna|fissa|prendi|aggiungi|crea|nuovo|appuntamento|promemoria)\s+)+", "", RegexOptions.IgnoreCase)
            For giro As Integer = 1 To 4
                oggetto = Regex.Replace(oggetto, "^(?:il|lo|la|le|un|una|di|da|del|dal|dalla|a|ad|alle|ore|per|con la|giorno|alle ore)\s+", "", RegexOptions.IgnoreCase)
                oggetto = Regex.Replace(oggetto, "\s+(?:il|lo|la|di|da|a|ad|alle|ore|per|giorno|del|dal|della|delle|dello|nella|nel|con|e)$", "", RegexOptions.IgnoreCase)
            Next
            oggetto = oggetto.Trim(" "c, ","c, "."c, ";"c, "-"c)
            If oggetto = "" Then oggetto = "Appuntamento"
            a.Oggetto = Char.ToUpper(oggetto(0), Italiano()) & oggetto.Substring(1)
            letto = a
            Return True
        Catch ex As Exception
            problema = "Non sono riuscito a capire la frase."
            letto = Nothing
            Return False
        End Try
    End Function

    Private Shared Function Italiano() As CultureInfo
        Return AiutoData.Italiano
    End Function

    Private Shared Function NomeParte(parola As String) As String
        Dim v As String = If(parola, "").ToLowerInvariant()
        If v.StartsWith("matt") Then Return "mattina"
        If v.StartsWith("pom") Then Return "pomeriggio"
        If v.StartsWith("ser") Then Return "sera"
        Return "notte"
    End Function

    ''' <summary>La data di una festa nominata nella frase (Natale, Capodanno, Ferragosto...); la parola resta nell'oggetto. Date.MinValue se non c'è.</summary>
    Private Shared Function DataFesta(frase As String, oggi As Date) As Date
        Dim feste As String(,) = {
            {"vigilia\s+di\s+natale", "24", "12"}, {"santo\s+stefano", "26", "12"}, {"natale", "25", "12"},
            {"san\s+silvestro|silvestro", "31", "12"}, {"capodanno", "1", "1"}, {"epifania|befana", "6", "1"},
            {"ferragosto", "15", "8"}, {"immacolata", "8", "12"}, {"ognissanti", "1", "11"}}
        For i As Integer = 0 To feste.GetLength(0) - 1
            If Regex.IsMatch(frase, "\b(?:" & feste(i, 0) & ")\b", RegexOptions.IgnoreCase) Then
                Dim data As Date
                If AiutoData.CostruisciData(Integer.Parse(feste(i, 1)), Integer.Parse(feste(i, 2)), 0, oggi, True, data) Then Return data
            End If
        Next
        Return Date.MinValue
    End Function

#End Region

End Class
