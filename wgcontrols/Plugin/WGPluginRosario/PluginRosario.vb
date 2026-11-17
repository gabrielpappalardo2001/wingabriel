Imports System
Imports System.Collections.Generic
Imports System.Text
Imports WGPlugin

''' <summary>
''' Santo Rosario: i misteri del giorno (gaudiosi, luminosi, dolorosi o gloriosi), il rosario completo da leggere, i cinque misteri con il
''' brano della Scrittura e le preghiere (segno della croce, Credo, Padre nostro, Ave Maria, Gloria, giaculatoria di Fatima, Salve Regina
''' e preghiera finale). Tutto offline: i testi sono quelli tradizionali della preghiera.
''' </summary>
Public Class PluginRosario
    Inherits WGPluginBase

    Private Const SegnoDellaCroce As String = "Nel nome del Padre e del Figlio e dello Spirito Santo. Amen."

    Private Const Credo As String = "Credo in Dio, Padre onnipotente, creatore del cielo e della terra; e in Gesù Cristo, suo unico Figlio, nostro Signore, il quale fu concepito di Spirito Santo, nacque da Maria Vergine, patì sotto Ponzio Pilato, fu crocifisso, morì e fu sepolto; discese agli inferi; il terzo giorno risuscitò da morte; salì al cielo, siede alla destra di Dio Padre onnipotente: di là verrà a giudicare i vivi e i morti. Credo nello Spirito Santo, la santa Chiesa cattolica, la comunione dei santi, la remissione dei peccati, la risurrezione della carne, la vita eterna. Amen."

    Private Const PadreNostro As String = "Padre nostro, che sei nei cieli, sia santificato il tuo nome, venga il tuo regno, sia fatta la tua volontà, come in cielo così in terra. Dacci oggi il nostro pane quotidiano, e rimetti a noi i nostri debiti come noi li rimettiamo ai nostri debitori, e non ci indurre in tentazione, ma liberaci dal male. Amen."

    Private Const AveMaria As String = "Ave, o Maria, piena di grazia, il Signore è con te. Tu sei benedetta fra le donne e benedetto è il frutto del tuo seno, Gesù. Santa Maria, Madre di Dio, prega per noi peccatori, adesso e nell'ora della nostra morte. Amen."

    Private Const Gloria As String = "Gloria al Padre e al Figlio e allo Spirito Santo, com'era nel principio, e ora e sempre, nei secoli dei secoli. Amen."

    Private Const Fatima As String = "Gesù mio, perdona le nostre colpe, preservaci dal fuoco dell'inferno, porta in cielo tutte le anime, specialmente le più bisognose della tua misericordia."

    Private Const SalveRegina As String = "Salve, Regina, madre di misericordia, vita, dolcezza e speranza nostra, salve. A te ricorriamo, esuli figli di Eva; a te sospiriamo, gementi e piangenti in questa valle di lacrime. Orsù dunque, avvocata nostra, rivolgi a noi gli occhi tuoi misericordiosi. E mostraci, dopo questo esilio, Gesù, il frutto benedetto del tuo seno. O clemente, o pia, o dolce Vergine Maria."

    Private Const PreghieraFinale As String = "Preghiamo. O Dio, il tuo Unigenito con la sua vita, morte e risurrezione ci ha acquistato i beni della salvezza eterna; concedi a noi, che con il Santo Rosario della beata Vergine Maria abbiamo meditato questi misteri, di imitare ciò che contengono e di ottenere ciò che promettono. Per Cristo nostro Signore. Amen."

    ' per ogni serie: nome, poi cinque misteri (titolo e brano della Scrittura)
    Private Shared ReadOnly Misteri As Dictionary(Of String, String()) = New Dictionary(Of String, String())(StringComparer.Ordinal) From {
        {"gaudiosi", New String() {"gaudiosi",
            "L'Annunciazione dell'angelo a Maria|Lc 1,26-38", "La visita di Maria a santa Elisabetta|Lc 1,39-56",
            "La nascita di Gesù a Betlemme|Lc 2,1-20", "La presentazione di Gesù al Tempio|Lc 2,22-38",
            "Il ritrovamento di Gesù nel Tempio|Lc 2,41-52"}},
        {"luminosi", New String() {"luminosi",
            "Il Battesimo di Gesù nel Giordano|Mt 3,13-17", "Le nozze di Cana|Gv 2,1-12",
            "L'annuncio del Regno di Dio e l'invito alla conversione|Mc 1,14-15", "La Trasfigurazione|Lc 9,28-36",
            "L'istituzione dell'Eucaristia|Mt 26,26-29"}},
        {"dolorosi", New String() {"dolorosi",
            "L'agonia di Gesù nell'orto del Getsemani|Mt 26,36-46", "La flagellazione di Gesù|Gv 19,1",
            "L'incoronazione di spine|Mt 27,27-31", "Gesù porta la croce fino al Calvario|Gv 19,16-17",
            "La crocifissione e la morte di Gesù|Lc 23,33-46"}},
        {"gloriosi", New String() {"gloriosi",
            "La risurrezione di Gesù|Mt 28,1-10", "L'ascensione di Gesù al cielo|At 1,6-11",
            "La discesa dello Spirito Santo|At 2,1-13", "L'assunzione di Maria al cielo|Ap 12,1",
            "L'incoronazione di Maria Regina del cielo e della terra|Ap 12,1"}}}

    Private _servizi As IWGServiziPlugin

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Santo Rosario"
        End Get
    End Property

    Public Overrides ReadOnly Property Descrizione As String
        Get
            Return "I misteri del giorno, il rosario da leggere e le preghiere."
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
            servizi.AggiungiVoceAltreFunzioni("Santo Rosario", AddressOf ApriMenu)
        Catch ex As Exception
            servizi.RegistraErrore("PluginRosario.Avvia", ex)
        End Try
    End Sub

    Private Sub ApriMenu()
        Try
            Dim menu As IWGMenuPlugin = _servizi.NuovoMenu("Santo Rosario")
            menu.Aggiungi("Rosario di oggi", Sub() MostraRosario(MisteriDelGiorno(DateTime.Today)))
            menu.Aggiungi("Misteri di oggi", Sub() ElencoMisteri(MisteriDelGiorno(DateTime.Today)))
            menu.Aggiungi("Tutti i misteri", AddressOf TuttiIMisteri)
            menu.Aggiungi("Preghiere", AddressOf ElencoPreghiere)
            menu.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginRosario.ApriMenu", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Le serie dei misteri per giorno: lunedì e sabato gaudiosi, martedì e venerdì dolorosi, mercoledì gloriosi, giovedì luminosi,
    ''' domenica gloriosi (nel tempo di Avvento e di Natale i gaudiosi, in Quaresima i dolorosi).
    ''' </summary>
    Friend Shared Function MisteriDelGiorno(giorno As Date) As String
        Select Case giorno.DayOfWeek
            Case DayOfWeek.Monday, DayOfWeek.Saturday : Return "gaudiosi"
            Case DayOfWeek.Tuesday, DayOfWeek.Friday : Return "dolorosi"
            Case DayOfWeek.Wednesday : Return "gloriosi"
            Case DayOfWeek.Thursday : Return "luminosi"
            Case Else
                Dim tempo As String = AiutoLiturgico.Tempo(giorno)
                If tempo = "Avvento" OrElse tempo = "Natale" Then Return "gaudiosi"
                If tempo = "Quaresima" Then Return "dolorosi"
                Return "gloriosi"
        End Select
    End Function

    Private Shared Function Ordinale(posizione As Integer) As String
        Select Case posizione
            Case 1 : Return "Primo"
            Case 2 : Return "Secondo"
            Case 3 : Return "Terzo"
            Case 4 : Return "Quarto"
            Case Else : Return "Quinto"
        End Select
    End Function

    Private Shared Function TitoloMistero(serie As String, posizione As Integer) As String
        Return Misteri(serie)(posizione).Split("|"c)(0)
    End Function

    Private Shared Function BranoMistero(serie As String, posizione As Integer) As String
        Return Misteri(serie)(posizione).Split("|"c)(1)
    End Function

    ''' <summary>Il rosario intero di una serie di misteri, da leggere dall'inizio alla fine.</summary>
    Friend Shared Function TestoRosario(serie As String) As String
        Dim t As New StringBuilder()
        t.Append("Santo Rosario, misteri ").Append(serie).Append(vbLf).Append(vbLf)
        t.Append("Segno della croce").Append(vbLf).Append(SegnoDellaCroce).Append(vbLf).Append(vbLf)
        t.Append("Credo").Append(vbLf).Append(Credo).Append(vbLf).Append(vbLf)
        t.Append("Padre nostro").Append(vbLf).Append(PadreNostro).Append(vbLf).Append(vbLf)
        t.Append("Tre Ave Maria").Append(vbLf).Append(AveMaria).Append(vbLf).Append(vbLf)
        t.Append("Gloria").Append(vbLf).Append(Gloria).Append(vbLf).Append(vbLf)
        For posizione As Integer = 1 To 5
            t.Append(Ordinale(posizione)).Append(" mistero ").Append(serie).Append(": ").Append(TitoloMistero(serie, posizione)).Append(". (").Append(BranoMistero(serie, posizione)).Append(")").Append(vbLf)
            t.Append("Padre nostro. Dieci Ave Maria. Gloria. Giaculatoria: ").Append(Fatima).Append(vbLf).Append(vbLf)
        Next
        t.Append("Salve Regina").Append(vbLf).Append(SalveRegina).Append(vbLf).Append(vbLf)
        t.Append("Preghiera finale").Append(vbLf).Append(PreghieraFinale).Append(vbLf).Append(vbLf)
        t.Append("Segno della croce").Append(vbLf).Append(SegnoDellaCroce)
        Return t.ToString()
    End Function

    Private Sub MostraRosario(serie As String)
        Try
            _servizi.MostraTesto("Rosario, misteri " & serie, TestoRosario(serie))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginRosario.MostraRosario", ex)
        End Try
    End Sub

    Private Sub ElencoMisteri(serie As String)
        Try
            Dim voci As New List(Of WGVoceElenco)()
            For posizione As Integer = 1 To 5
                voci.Add(New WGVoceElenco(posizione.ToString(), Ordinale(posizione) & " mistero: " & TitoloMistero(serie, posizione)))
            Next
            _servizi.MostraElencoConId("Misteri " & serie, voci,
                                       Sub(id As String)
                                           Dim p As Integer = Integer.Parse(id)
                                           _servizi.Messaggio(Ordinale(p) & " mistero " & serie & ": " & TitoloMistero(serie, p) & ". Brano: " & BranoMistero(serie, p) & ".")
                                       End Sub)
        Catch ex As Exception
            _servizi.RegistraErrore("PluginRosario.ElencoMisteri", ex)
        End Try
    End Sub

    Private Sub TuttiIMisteri()
        Try
            Dim voci As New List(Of WGVoceElenco)()
            voci.Add(New WGVoceElenco("gaudiosi", "Misteri gaudiosi, lunedì e sabato"))
            voci.Add(New WGVoceElenco("luminosi", "Misteri luminosi, giovedì"))
            voci.Add(New WGVoceElenco("dolorosi", "Misteri dolorosi, martedì e venerdì"))
            voci.Add(New WGVoceElenco("gloriosi", "Misteri gloriosi, mercoledì e domenica"))
            Dim elenco As IWGElencoPlugin = _servizi.NuovoElenco("Tutti i misteri")
            For Each v As WGVoceElenco In voci
                elenco.Aggiungi(v.Id, v.Testo)
            Next
            elenco.AlScelto = Sub(id As String) ElencoMisteri(id)
            elenco.AggiungiOpzioneSuVoce("Rosario completo", Sub(id As String) MostraRosario(id))
            elenco.Mostra()
        Catch ex As Exception
            _servizi.RegistraErrore("PluginRosario.TuttiIMisteri", ex)
        End Try
    End Sub

    Private Sub ElencoPreghiere()
        Try
            Dim preghiere As New Dictionary(Of String, String)()
            preghiere("Segno della croce") = SegnoDellaCroce
            preghiere("Credo") = Credo
            preghiere("Padre nostro") = PadreNostro
            preghiere("Ave Maria") = AveMaria
            preghiere("Gloria") = Gloria
            preghiere("Giaculatoria di Fatima") = Fatima
            preghiere("Salve Regina") = SalveRegina
            preghiere("Preghiera finale del rosario") = PreghieraFinale
            Dim voci As New List(Of WGVoceElenco)()
            For Each nome As String In preghiere.Keys
                voci.Add(New WGVoceElenco(nome, nome))
            Next
            _servizi.MostraElencoConId("Preghiere", voci, Sub(id As String) _servizi.MostraTesto(id, preghiere(id)))
        Catch ex As Exception
            _servizi.RegistraErrore("PluginRosario.ElencoPreghiere", ex)
        End Try
    End Sub

End Class
