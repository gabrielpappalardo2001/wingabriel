Imports System.Collections.Generic
Imports System.IO
Imports System.Text

' Emoji e caratteri strani nei testi che il lettore sta per dire. Le emoji si dicono con il loro nome italiano (tabella del CLDR di Unicode,
' Dati\emoji-it.txt, incorporata nella libreria); i caratteri decorativi dei nomi (lettere matematiche, cerchiate, a tutta larghezza, maiuscolette,
' lettere con tanti accenti sovrapposti) tornano lettere normali e i caratteri invisibili spariscono.
Friend NotInheritable Class LetturaEmoji

    Private Shared ReadOnly _blocco As New Object()
    Private Shared _nomi As Dictionary(Of String, String)
    Private Shared _lunghezzaMassima As Integer = 1
    Private Shared _caricato As Boolean

    Private Sub New()
    End Sub

    Public Shared Function Converti(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return testo
            Dim risultato As String = NormalizzaCaratteri(testo)
            If Not ConPossibiliEmoji(risultato) Then Return risultato
            Return SostituisciEmoji(risultato)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaEmoji.Converti", ex)
            Return testo
        End Try
    End Function

    Private Shared Function ConPossibiliEmoji(testo As String) As Boolean
        For Each c As Char In testo
            If c >= ChrW(&H2000) Then Return True
        Next
        Return False
    End Function

    Private Shared Sub Carica()
        SyncLock _blocco
            If _caricato Then Return
            _caricato = True
            Try
                Dim nomi As New Dictionary(Of String, String)(StringComparer.Ordinal)
                Using flusso As Stream = GetType(LetturaEmoji).Assembly.GetManifestResourceStream("emoji-it.txt")
                    If flusso Is Nothing Then Return
                    Using lettore As New StreamReader(flusso, Encoding.UTF8)
                        Dim riga As String = lettore.ReadLine()
                        While riga IsNot Nothing
                            Dim tab As Integer = riga.IndexOf(ControlChars.Tab)
                            If tab > 0 Then
                                Dim chiave As String = riga.Substring(0, tab)
                                nomi(chiave) = riga.Substring(tab + 1)
                                If chiave.Length > _lunghezzaMassima Then _lunghezzaMassima = chiave.Length
                            End If
                            riga = lettore.ReadLine()
                        End While
                    End Using
                End Using
                _nomi = nomi
            Catch ex As Exception
                RegistroErrori.Scrivi("LetturaEmoji.Carica", ex)
            End Try
        End SyncLock
    End Sub

    Private Shared Function PuoIniziare(testo As String, indice As Integer) As Boolean
        Dim c As Char = testo(indice)
        If Char.IsHighSurrogate(c) Then Return True
        If c >= ChrW(&H2000) AndAlso c <= ChrW(&H2BFF) Then Return True
        If c = ChrW(&H3030) OrElse c = ChrW(&H303D) OrElse c = ChrW(&H3297) OrElse c = ChrW(&H3299) Then Return True
        If (c = "#"c OrElse c = "*"c OrElse (c >= "0"c AndAlso c <= "9"c)) AndAlso indice + 1 < testo.Length AndAlso testo(indice + 1) = ChrW(&H20E3) Then Return True
        Return False
    End Function

    ' Le emoji diventano il loro nome; fra due emoji di fila c'è una virgola, così non si fondono in una parola sola.
    Private Shared Function SostituisciEmoji(testo As String) As String
        Carica()
        If _nomi Is Nothing Then Return testo
        Dim pulito As String = testo.Replace(ChrW(&HFE0F), "").Replace(ChrW(&HFE0E), "")
        Dim sb As New StringBuilder(pulito.Length + 32)
        Dim precedenteEmoji As Boolean = False
        Dim i As Integer = 0
        While i < pulito.Length
            Dim trovato As Boolean = False
            If PuoIniziare(pulito, i) Then
                For lunghezza As Integer = Math.Min(_lunghezzaMassima, pulito.Length - i) To 1 Step -1
                    Dim nome As String = Nothing
                    If _nomi.TryGetValue(pulito.Substring(i, lunghezza), nome) Then
                        If precedenteEmoji Then
                            While sb.Length > 0 AndAlso Char.IsWhiteSpace(sb(sb.Length - 1))
                                sb.Length -= 1
                            End While
                            sb.Append(", ")
                        ElseIf sb.Length > 0 AndAlso Not Char.IsWhiteSpace(sb(sb.Length - 1)) Then
                            sb.Append(" "c)
                        End If
                        sb.Append(nome)
                        precedenteEmoji = True
                        i += lunghezza
                        trovato = True
                        If i < pulito.Length AndAlso Char.IsLetterOrDigit(pulito(i)) Then sb.Append(" "c)
                        Exit For
                    End If
                Next
            End If
            If trovato Then Continue While
            Dim c As Char = pulito(i)
            sb.Append(c)
            If Not Char.IsWhiteSpace(c) Then precedenteEmoji = False
            i += 1
        End While
        Return sb.ToString()
    End Function

    Private Shared ReadOnly _maiuscolette As Dictionary(Of Char, Char) = CreaMaiuscolette()

    Private Shared Function CreaMaiuscolette() As Dictionary(Of Char, Char)
        Dim d As New Dictionary(Of Char, Char)()
        Dim codici As Integer() = {&H1D00, &H299, &H1D04, &H1D05, &H1D07, &HA730, &H262, &H29C, &H26A, &H1D0A, &H1D0B, &H29F, &H1D0D, &H274, &H1D0F, &H1D18, &H280, &HA731, &H1D1B, &H1D1C, &H1D20, &H1D21, &H28F, &H1D22}
        Dim lettere As String = "abcdefghijklmnoprstuvwyz"
        For indice As Integer = 0 To codici.Length - 1
            d(ChrW(codici(indice))) = lettere(indice)
        Next
        Return d
    End Function

    ' Lettere decorative e caratteri invisibili: tornano normali o spariscono.
    Friend Shared Function NormalizzaCaratteri(testo As String) As String
        Dim daFare As Boolean = False
        For Each c As Char In testo
            If c >= ChrW(&H300) Then
                daFare = True
                Exit For
            End If
        Next
        If Not daFare Then Return testo
        Dim base As String = testo
        Try
            base = testo.Normalize(NormalizationForm.FormC)
        Catch exNormale As ArgumentException
            RegistroErrori.Scrivi("LetturaEmoji.NormalizzaCaratteri", exNormale)
        End Try
        Dim sb As New StringBuilder(base.Length)
        Dim i As Integer = 0
        While i < base.Length
            Dim c As Char = base(i)
            Dim codice As Integer = AscW(c)
            If Char.IsHighSurrogate(c) AndAlso i + 1 < base.Length AndAlso Char.IsLowSurrogate(base(i + 1)) Then
                Dim punto As Integer = Char.ConvertToUtf32(c, base(i + 1))
                If (punto >= &H1D400 AndAlso punto <= &H1D7FF) OrElse (punto >= &H1F130 AndAlso punto <= &H1F149) Then
                    sb.Append(Char.ConvertFromUtf32(punto).Normalize(NormalizationForm.FormKC))
                Else
                    sb.Append(c).Append(base(i + 1))
                End If
                i += 2
                Continue While
            End If
            If codice >= &H300 AndAlso codice <= &H36F Then
                ' Accenti sovrapposti che non si sono fusi in una lettera: si tolgono.
            ElseIf codice = &H200B OrElse codice = &H200C OrElse codice = &H2060 OrElse codice = &HFEFF OrElse codice = &HAD Then
                ' Caratteri invisibili.
            ElseIf (codice >= &H24B6 AndAlso codice <= &H24E9) OrElse (codice >= &H2460 AndAlso codice <= &H24B5) OrElse (codice >= &HFF01 AndAlso codice <= &HFF5E) OrElse (codice >= &H2100 AndAlso codice <= &H214F) Then
                sb.Append(c.ToString().Normalize(NormalizationForm.FormKC))
            ElseIf _maiuscolette.ContainsKey(c) Then
                sb.Append(_maiuscolette(c))
            Else
                sb.Append(c)
            End If
            i += 1
        End While
        Return sb.ToString()
    End Function

End Class
