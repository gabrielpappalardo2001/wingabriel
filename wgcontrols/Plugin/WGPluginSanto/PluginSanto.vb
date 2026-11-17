Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports WGPlugin

''' <summary>
''' Il santo del giorno, nel menu Altre funzioni. I santi si leggono dalla pagina del giorno su Wikipedia in italiano (sezione
''' "Feste e ricorrenze", religiose, cristianesimo) e si tengono in una copia nella cartella dei dati del plugin, così lo stesso giorno
''' non si scarica due volte.
''' </summary>
Public Class PluginSanto
    Inherits WGPluginBase

    Private Shared ReadOnly Mesi As String() = {"gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"}

    Private _servizi As IWGServiziPlugin

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Santo del giorno"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "Dice quali santi si ricordano oggi o in un'altra data."
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
            servizi.AggiungiVoceAltreFunzioni("Santo del giorno", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginSanto.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim voci As New List(Of String) From {"Santi di oggi", "Un'altra data"}
            _servizi.MostraElenco("Santo del giorno", voci, AddressOf Scelta)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginSanto.ApriMenu", ex)
        End Try
    End Sub

    Private Sub Scelta(indice As Integer)
        Try
            If indice = 0 Then
                MostraSanti(DateTime.Today.Day, DateTime.Today.Month)
            ElseIf indice = 1 Then
                _servizi.ChiediTesto("Data", "", AddressOf DataScelta)
            End If
        Catch ex As Exception
            _servizi.RegistraErrore("PluginSanto.Scelta", ex)
        End Try
    End Sub

    Private Sub DataScelta(testo As String)
        Try
            Dim giorno, mese As Integer
            If Not TentaLeggiData(testo, giorno, mese) Then
                _servizi.Messaggio("Non ho capito la data. Scrivi per esempio 25 dicembre oppure 25/12.")
                Return
            End If
            MostraSanti(giorno, mese)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginSanto.DataScelta", ex)
        End Try
    End Sub

    Private Async Sub MostraSanti(giorno As Integer, mese As Integer)
        Try
            Dim santi As New List(Of String)()
            Dim errore As Boolean = False
            Await _servizi.EseguiConAttesaAsync("Cerco i santi del giorno",
                                                 Async Function()
                                                     Try
                                                         santi = Await CercaSantiAsync(giorno, mese)
                                                     Catch ex As Exception
                                                         errore = True
                                                         _servizi.RegistraErrore("PluginSanto.MostraSanti (ricerca)", ex)
                                                     End Try
                                                 End Function)
            If errore Then
                _servizi.Messaggio("Non riesco a leggere i santi del giorno. Controlla la connessione a internet.")
                Return
            End If
            If santi.Count = 0 Then
                _servizi.Messaggio("Non ho trovato santi per il " & Etichetta(giorno, mese) & ".")
                Return
            End If
            Dim voci As New List(Of WGVoceElenco)()
            For posizione As Integer = 0 To santi.Count - 1
                voci.Add(New WGVoceElenco(posizione.ToString(), santi(posizione)))
            Next
            _servizi.MostraElencoConId("Santi del " & Etichetta(giorno, mese), voci, Sub(id As String) _servizi.Messaggio(santi(Integer.Parse(id))))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginSanto.MostraSanti", ex)
        End Try
    End Sub

    Private Shared Function Etichetta(giorno As Integer, mese As Integer) As String
        Return giorno.ToString() & " " & Mesi(mese - 1)
    End Function

    ''' <summary>Il titolo della pagina di Wikipedia: "12 novembre", ma "1º gennaio" per il primo del mese.</summary>
    Friend Shared Function TitoloPagina(giorno As Integer, mese As Integer) As String
        Return If(giorno = 1, "1" & ChrW(&HBA), giorno.ToString()) & " " & Mesi(mese - 1)
    End Function

    ''' <summary>Il nome del file di copia nella cartella dei dati del plugin (i file si leggono e si scrivono con i servizi).</summary>
    Private Shared Function NomeCopia(giorno As Integer, mese As Integer) As String
        Return "santi-" & mese.ToString("00") & "-" & giorno.ToString("00") & ".txt"
    End Function

    Private Async Function CercaSantiAsync(giorno As Integer, mese As Integer) As Task(Of List(Of String))
        Dim copia As Byte() = _servizi.LeggiFileDati(NomeCopia(giorno, mese))
        If copia IsNot Nothing Then
            Dim salvati As New List(Of String)()
            For Each riga As String In Encoding.UTF8.GetString(copia).Replace(vbCr, "").Split(ChrW(10))
                If riga <> "" Then salvati.Add(riga)
            Next
            If salvati.Count > 0 Then Return salvati
        End If
        ' la pagina si scarica con il servizio di WinGabriel: il plugin non apre connessioni da solo
        Dim indirizzo As String = "https://it.wikipedia.org/w/index.php?action=raw&title=" & Uri.EscapeDataString(TitoloPagina(giorno, mese))
        Dim wikitesto As String = Await _servizi.ScaricaTestoAsync(indirizzo)
        If wikitesto Is Nothing Then Throw New InvalidOperationException("pagina non disponibile")
        Dim santi As List(Of String) = EstraiSanti(wikitesto)
        If santi.Count > 0 Then _servizi.ScriviFileDati(NomeCopia(giorno, mese), New UTF8Encoding(False).GetBytes(String.Join(vbLf, santi)))
        Return santi
    End Function

    ''' <summary>I santi della sezione "Religiose", blocco del cristianesimo, di una pagina di giorno di Wikipedia (testo wiki).</summary>
    Friend Shared Function EstraiSanti(wikitesto As String) As List(Of String)
        Dim santi As New List(Of String)()
        If String.IsNullOrEmpty(wikitesto) Then Return santi
        Dim righe As String() = wikitesto.Replace(vbCr, "").Split(ChrW(10))
        Dim dentro As Boolean = False
        Dim inCristianesimo As Boolean = False
        Dim trovatoBlocco As Boolean = False
        Dim tutteLeVoci As New List(Of String)()
        For Each riga As String In righe
            If Regex.IsMatch(riga, "^===\s*Religios[ae]\s*===") Then
                dentro = True
                Continue For
            End If
            If Not dentro Then Continue For
            If riga.StartsWith("==") Then Exit For
            Dim t As String = riga.Trim()
            If t = "" Then Continue For
            If t.StartsWith("*") Then
                Dim voce As String = PulisciWiki(t.Substring(1))
                If voce <> "" Then
                    tutteLeVoci.Add(voce)
                    If inCristianesimo Then santi.Add(voce)
                End If
            Else
                ' Una riga che non è una voce dell'elenco apre un nuovo blocco: conta solo quello del cristianesimo.
                inCristianesimo = t.IndexOf("Cristianesimo", StringComparison.OrdinalIgnoreCase) >= 0
                If inCristianesimo Then trovatoBlocco = True
            End If
        Next
        If Not trovatoBlocco Then Return tutteLeVoci
        Return santi
    End Function

    Private Shared Function PulisciWiki(testo As String) As String
        Dim t As String = testo
        t = Regex.Replace(t, "<ref[^>]*/>", "")
        t = Regex.Replace(t, "<ref[^>]*>.*?</ref>", "", RegexOptions.Singleline)
        t = Regex.Replace(t, "\{\{[^{}]*\}\}", "")
        t = Regex.Replace(t, "\[\[[^\]|]*\|([^\]]*)\]\]", "$1")
        t = Regex.Replace(t, "\[\[([^\]]*)\]\]", "$1")
        t = Regex.Replace(t, "\[https?://[^\s\]]+\s*([^\]]*)\]", "$1")
        t = Regex.Replace(t, "<[^>]+>", "")
        t = t.Replace("'''", "").Replace("''", "")
        t = Regex.Replace(t, "\s+", " ").Trim()
        Return t
    End Function

    ''' <summary>Legge una data scritta a mano: "25 dicembre", "25/12", "25-12", "1 gennaio", "primo gennaio".</summary>
    Friend Shared Function TentaLeggiData(testo As String, ByRef giorno As Integer, ByRef mese As Integer) As Boolean
        giorno = 0
        mese = 0
        If String.IsNullOrWhiteSpace(testo) Then Return False
        Dim t As String = testo.Trim().ToLowerInvariant().Replace("primo", "1").Replace("1º", "1").Replace("1°", "1")
        Dim numerica As Match = Regex.Match(t, "^(\d{1,2})\s*[/\-.]\s*(\d{1,2})$")
        If numerica.Success Then
            giorno = Integer.Parse(numerica.Groups(1).Value)
            mese = Integer.Parse(numerica.Groups(2).Value)
        Else
            Dim scritta As Match = Regex.Match(t, "^(\d{1,2})\s+([a-zì]+)$")
            If Not scritta.Success Then Return False
            giorno = Integer.Parse(scritta.Groups(1).Value)
            mese = Array.IndexOf(Mesi, scritta.Groups(2).Value) + 1
        End If
        If mese < 1 OrElse mese > 12 OrElse giorno < 1 Then Return False
        Dim giorniDelMese As Integer = DateTime.DaysInMonth(2024, mese)
        Return giorno <= giorniDelMese
    End Function

End Class
