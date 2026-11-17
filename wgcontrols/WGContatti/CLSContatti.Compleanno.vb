Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Text.RegularExpressions

' Il compleanno (o data di nascita) di un contatto. Si salva in forma canonica: "aaaa-mm-gg" se si conosce l'anno, "--mm-gg" se no.
' Nelle rubriche protette si cifra come gli altri dati del contatto (vedi CampiRiservati).
Partial Public Class CLSContatti

    ''' <summary>Un compleanno letto dalla rubrica: chi è, e il giorno, il mese e (se noto) l'anno di nascita.</summary>
    Public Class VoceCompleanno
        Public Property Chiave As String = ""
        Public Property NomeCompleto As String = ""
        Public Property Giorno As Integer
        Public Property Mese As Integer
        ''' <summary>L'anno di nascita, 0 se non si conosce.</summary>
        Public Property Anno As Integer
    End Class

    ' Apple e altri programmi scrivono un anno inventato quando l'anno di nascita non si conosce.
    Private Const ANNO_SEGNAPOSTO_VCARD As Integer = 1604

    ''' <summary>
    ''' Riconosce una data scritta come "15/03/1980", "15/03", "15 marzo 1980", "1980-03-15", "--03-15" o "19800315".
    ''' Giorno, mese e anno escono dai parametri (anno 0 se non c'è). Falso se la data non è valida.
    ''' </summary>
    Public Shared Function AnalizzaCompleanno(testo As String, ByRef giorno As Integer, ByRef mese As Integer, ByRef anno As Integer) As Boolean
        giorno = 0 : mese = 0 : anno = 0
        Try
            Dim t As String = If(testo, "").Trim()
            If t.Length = 0 Then Return False

            Dim m As Match = Regex.Match(t, "^--(\d{2})-?(\d{2})$")
            If m.Success Then
                mese = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
                giorno = Integer.Parse(m.Groups(2).Value, CultureInfo.InvariantCulture)
                Return DataValida(giorno, mese, 0)
            End If

            m = Regex.Match(t, "^(\d{4})-?(\d{2})-?(\d{2})(?:T.*)?$")
            If m.Success Then
                anno = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
                mese = Integer.Parse(m.Groups(2).Value, CultureInfo.InvariantCulture)
                giorno = Integer.Parse(m.Groups(3).Value, CultureInfo.InvariantCulture)
                If anno = ANNO_SEGNAPOSTO_VCARD Then anno = 0
                Return DataValida(giorno, mese, anno)
            End If

            ' giorno mese [anno], separati da barre, trattini, punti o spazi; il mese può essere scritto a parole
            Dim parti As String() = Regex.Split(t.ToLowerInvariant(), "[^\p{L}\p{N}]+")
            Dim valori As New List(Of String)()
            For Each p As String In parti
                If p.Length > 0 Then valori.Add(p)
            Next
            If valori.Count < 2 OrElse valori.Count > 3 Then Return False

            If Not Integer.TryParse(valori(0), NumberStyles.None, CultureInfo.InvariantCulture, giorno) Then Return False
            If Not Integer.TryParse(valori(1), NumberStyles.None, CultureInfo.InvariantCulture, mese) Then
                mese = NumeroMeseItaliano(valori(1))
                If mese = 0 Then Return False
            End If
            If valori.Count = 3 Then
                If valori(2).Length <> 4 OrElse Not Integer.TryParse(valori(2), NumberStyles.None, CultureInfo.InvariantCulture, anno) Then Return False
            End If
            Return DataValida(giorno, mese, anno)
        Catch
            giorno = 0 : mese = 0 : anno = 0
            Return False
        End Try
    End Function

    Private Shared Function NumeroMeseItaliano(testo As String) As Integer
        Try
            Dim cultura As CultureInfo = CultureInfo.GetCultureInfo("it-IT")
            For numero As Integer = 1 To 12
                Dim intero As String = cultura.DateTimeFormat.GetMonthName(numero).ToLowerInvariant()
                Dim abbreviato As String = cultura.DateTimeFormat.GetAbbreviatedMonthName(numero).ToLowerInvariant().TrimEnd("."c)
                If testo = intero OrElse testo = abbreviato Then Return numero
            Next
        Catch
        End Try
        Return 0
    End Function

    Private Shared Function DataValida(giorno As Integer, mese As Integer, anno As Integer) As Boolean
        If mese < 1 OrElse mese > 12 OrElse giorno < 1 Then Return False
        ' senza anno si controlla su un anno bisestile, così il 29 febbraio è ammesso
        Dim annoDiControllo As Integer = If(anno = 0, 2000, anno)
        If annoDiControllo < 1 OrElse annoDiControllo > DateTime.Today.Year Then Return False
        Return giorno <= DateTime.DaysInMonth(annoDiControllo, mese)
    End Function

    ''' <summary>
    ''' La forma da salvare: "aaaa-mm-gg", "--mm-gg" o "" se il testo è vuoto. Nothing se la data non è valida.
    ''' </summary>
    Public Shared Function NormalizzaCompleanno(testo As String) As String
        If String.IsNullOrWhiteSpace(testo) Then Return ""
        Dim giorno, mese, anno As Integer
        If Not AnalizzaCompleanno(testo, giorno, mese, anno) Then Return Nothing
        If anno > 0 Then Return anno.ToString("0000", CultureInfo.InvariantCulture) & "-" & mese.ToString("00", CultureInfo.InvariantCulture) & "-" & giorno.ToString("00", CultureInfo.InvariantCulture)
        Return "--" & mese.ToString("00", CultureInfo.InvariantCulture) & "-" & giorno.ToString("00", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Il compleanno come si legge: "15 marzo 1980" oppure "15 marzo"; vuoto se non c'è o non è valido.</summary>
    Public Shared Function DescriviCompleanno(canonico As String) As String
        Try
            Dim giorno, mese, anno As Integer
            If Not AnalizzaCompleanno(canonico, giorno, mese, anno) Then Return ""
            Dim nomeMese As String = CultureInfo.GetCultureInfo("it-IT").DateTimeFormat.GetMonthName(mese)
            Return giorno.ToString(CultureInfo.InvariantCulture) & " " & nomeMese & If(anno > 0, " " & anno.ToString(CultureInfo.InvariantCulture), "")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>La riga BDAY di un vCard per il compleanno dato (senza a capo); vuota se non c'è o non è valido.</summary>
    Public Shared Function RigaVCardCompleanno(canonico As String) As String
        Dim giorno, mese, anno As Integer
        If Not AnalizzaCompleanno(canonico, giorno, mese, anno) Then Return ""
        Dim mm As String = mese.ToString("00", CultureInfo.InvariantCulture)
        Dim gg As String = giorno.ToString("00", CultureInfo.InvariantCulture)
        If anno > 0 Then Return "BDAY:" & anno.ToString("0000", CultureInfo.InvariantCulture) & "-" & mm & "-" & gg
        Return "BDAY;X-APPLE-OMIT-YEAR=" & ANNO_SEGNAPOSTO_VCARD.ToString(CultureInfo.InvariantCulture) & ":" & ANNO_SEGNAPOSTO_VCARD.ToString(CultureInfo.InvariantCulture) & "-" & mm & "-" & gg
    End Function

    ''' <summary>
    ''' I compleanni di tutta la rubrica. nonLeggibili contiene i contatti che hanno un compleanno ma che adesso non si possono leggere
    ''' (rubrica protetta e bloccata): chi li usa non deve crederli spariti. Nothing se la lettura non è riuscita.
    ''' </summary>
    Public Function OttieniCompleanni(ByRef nonLeggibili As HashSet(Of String)) As List(Of VoceCompleanno)
        nonLeggibili = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            ' EseguiQuery con il database occupato restituisce una tabella vuota: si conta prima, per non scambiare un errore per "nessun compleanno".
            Dim conteggio As Object = _db.EseguiScalare("SELECT COUNT(1) FROM " & NomeTabellaContatti & " WHERE IFNULL(Compleanno, '') <> '';", Nothing)
            If conteggio Is Nothing OrElse conteggio Is DBNull.Value Then Return Nothing
            Dim attesi As Integer = Convert.ToInt32(conteggio)

            Dim risultato As New List(Of VoceCompleanno)()
            If attesi = 0 Then Return risultato

            Dim tabella As DataTable = _db.EseguiQuery("SELECT ChiaveWG, Nome, Cognome, Compleanno, ChiaveRubrica FROM " & NomeTabellaContatti & " WHERE IFNULL(Compleanno, '') <> '';", Nothing)
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then Return Nothing

            For Each riga As DataRow In tabella.Rows
                nonLeggibili.Add(ValoreColonna(riga, "ChiaveWG"))
            Next

            ' toglie dalla tabella i contatti delle rubriche protette e bloccate, e decifra gli altri
            Dim leggibili As DataTable = PreparaContatti(tabella)
            If leggibili Is Nothing Then Return Nothing

            For Each riga As DataRow In leggibili.Rows
                Try
                    Dim chiave As String = ValoreColonna(riga, "ChiaveWG")
                    Dim giorno, mese, anno As Integer
                    If Not AnalizzaCompleanno(ValoreColonna(riga, "Compleanno"), giorno, mese, anno) Then Continue For

                    Dim nomeCompleto As String = (ValoreColonna(riga, "Nome") & " " & ValoreColonna(riga, "Cognome")).Trim()
                    If nomeCompleto.Length = 0 Then Continue For

                    nonLeggibili.Remove(chiave)
                    risultato.Add(New VoceCompleanno() With {.Chiave = chiave, .NomeCompleto = nomeCompleto, .Giorno = giorno, .Mese = mese, .Anno = anno})
                Catch exRiga As Exception
                    ScriviLog("CLSContatti.OttieniCompleanni riga: " & exRiga.Message)
                End Try
            Next
            Return risultato
        Catch ex As Exception
            ScriviLog("CLSContatti.OttieniCompleanni: " & ex.ToString())
            Return Nothing
        End Try
    End Function

End Class
