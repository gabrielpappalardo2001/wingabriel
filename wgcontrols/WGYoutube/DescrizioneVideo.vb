' ============================================================================================
' DescrizioneVideo - la descrizione di un video divisa nelle sue parti
' ============================================================================================
'
' La descrizione di YouTube è un blocco di testo con dentro di tutto: la presentazione, i link ai social, l'elenco
' dei punti del video con l'ora ("05:23 Introduzione"), gli hashtag. Letta tutta di fila è una fatica, soprattutto
' con la sintesi vocale che si mette a spiegare gli indirizzi lettera per lettera. Qui la si divide in paragrafi
' (i link dentro il testo diventano "link a instagram"), nell'elenco dei link, in quello dei punti del video e nei
' suoi hashtag, così ognuno si legge e si usa a parte.
'
'   Dim d As DescrizioneVideo = DescrizioneVideo.Analizza(testoDellaDescrizione)
'   d.Paragrafi, d.Link, d.Punti, d.Hashtag
' ============================================================================================
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions

Friend NotInheritable Class DescrizioneVideo

    Public Class Collegamento
        Public Url As String
        ''' <summary>Il sito, senza "www": "instagram.com".</summary>
        Public Sito As String
        ''' <summary>Il testo accanto al link ("Seguimi su Instagram"), per capire a cosa serve.</summary>
        Public Contesto As String
    End Class

    Public Class PuntoVideo
        Public Secondi As Integer
        Public Etichetta As String
        ''' <summary>L'ora come è scritta ("1:02:03").</summary>
        Public Ora As String
    End Class

    Public ReadOnly Paragrafi As New List(Of String)()
    Public ReadOnly Link As New List(Of Collegamento)()
    Public ReadOnly Punti As New List(Of PuntoVideo)()
    Public ReadOnly Hashtag As New List(Of String)()

    Private Shared ReadOnly EspressioneLink As New Regex("https?://[^\s<>""']+", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly EspressioneOra As New Regex("^\s*[\(\[]?((?:\d{1,2}:)?\d{1,2}:\d{2})[\)\]]?\s*[-–—:·.]?\s*(.*)$", RegexOptions.Compiled)
    Private Shared ReadOnly EspressioneHashtag As New Regex("(?<![\p{L}\p{N}_])#[\p{L}\p{N}_]{2,}", RegexOptions.Compiled)

    ''' <summary>Vero se c'è più di un semplice blocco di testo: link, punti del video, hashtag o più paragrafi.</summary>
    Public ReadOnly Property HaStruttura As Boolean
        Get
            Return Link.Count > 0 OrElse Punti.Count > 0 OrElse Hashtag.Count > 0 OrElse Paragrafi.Count > 1
        End Get
    End Property

    Public Shared Function Analizza(testo As String) As DescrizioneVideo
        Dim d As New DescrizioneVideo()
        If String.IsNullOrWhiteSpace(testo) Then Return d
        Dim righe As String() = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(vbLf(0))
        Dim corrente As New List(Of String)()
        Dim ultimaRiga As String = ""

        Dim chiudiParagrafo As Action = Sub()
                                            If corrente.Count > 0 Then
                                                d.Paragrafi.Add(String.Join(" ", corrente))
                                                corrente.Clear()
                                            End If
                                        End Sub

        For Each rigaGrezza As String In righe
            Dim riga As String = rigaGrezza.Trim()
            If riga = "" Then
                chiudiParagrafo()
                Continue For
            End If

            ' Punto del video: la riga comincia con un'ora.
            Dim ora As Match = EspressioneOra.Match(riga)
            If ora.Success Then
                Dim secondi As Integer = SecondiDa(ora.Groups(1).Value)
                If secondi >= 0 Then
                    chiudiParagrafo()
                    d.Punti.Add(New PuntoVideo() With {.Secondi = secondi, .Ora = ora.Groups(1).Value, .Etichetta = If(ora.Groups(2).Value.Trim() = "", "Punto del video", ora.Groups(2).Value.Trim())})
                    Continue For
                End If
            End If

            ' I link: si raccolgono e nel testo diventano "link a ...".
            Dim senzaLink As String = riga
            For Each m As Match In EspressioneLink.Matches(riga)
                Dim indirizzo As String = m.Value.TrimEnd("."c, ","c, ";"c, ":"c, ")"c, "!"c, "?"c, "]"c)
                Dim sito As String = SitoDi(indirizzo)
                Dim contesto As String = riga.Replace(m.Value, " ").Trim().TrimEnd(":"c, "-"c, "–"c, "—"c, " "c)
                If contesto = "" Then contesto = ultimaRiga
                If Not d.Link.Any(Function(l) l.Url = indirizzo) Then d.Link.Add(New Collegamento() With {.Url = indirizzo, .Sito = sito, .Contesto = contesto})
                senzaLink = senzaLink.Replace(m.Value, "link a " & sito)
            Next

            ' Righe fatte solo di hashtag: non sono un paragrafo.
            Dim senzaTag As String = EspressioneHashtag.Replace(riga, "").Trim()
            For Each m As Match In EspressioneHashtag.Matches(riga)
                If Not d.Hashtag.Any(Function(h) String.Equals(h, m.Value, StringComparison.OrdinalIgnoreCase)) Then d.Hashtag.Add(m.Value)
            Next
            If senzaTag = "" AndAlso EspressioneHashtag.IsMatch(riga) Then
                chiudiParagrafo()
                ultimaRiga = riga
                Continue For
            End If

            corrente.Add(senzaLink)
            ultimaRiga = senzaLink
        Next
        chiudiParagrafo()
        Return d
    End Function

    Private Shared Function SitoDi(indirizzo As String) As String
        Try
            Dim host As String = New Uri(indirizzo).Host.ToLowerInvariant()
            Return If(host.StartsWith("www."), host.Substring(4), host)
        Catch
            Return "un sito"
        End Try
    End Function

    ''' <summary>"1:02:03" o "05:23" in secondi; -1 se l'ora non è plausibile.</summary>
    Friend Shared Function SecondiDa(ora As String) As Integer
        Try
            Dim parti As String() = ora.Split(":"c)
            Dim totale As Integer = 0
            For Each parte As String In parti
                totale = totale * 60 + Integer.Parse(parte, CultureInfo.InvariantCulture)
            Next
            If parti.Length >= 2 AndAlso Integer.Parse(parti(parti.Length - 1), CultureInfo.InvariantCulture) > 59 Then Return -1
            Return totale
        Catch
            Return -1
        End Try
    End Function

End Class
