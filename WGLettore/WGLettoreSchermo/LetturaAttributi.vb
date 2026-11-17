Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Estrazione e formattazione degli attributi di stile, colore, allineamento e proprietà dei controlli.
Friend NotInheritable Class LetturaAttributi

    Private Const AttributoNomeCarattere As Integer = 40005
    Private Const AttributoDimensione As Integer = 40006
    Private Const AttributoPeso As Integer = 40007
    Private Const AttributoCultura As Integer = 40004
    Private Const AttributoCorsivo As Integer = 40014
    Private Const AttributoColoreSfondo As Integer = 40001
    Private Const AttributoColoreTesto As Integer = 40008
    Private Const AttributoAllineamento As Integer = 40009
    Private Const AttributoRientroPrimaRiga As Integer = 40010
    Private Const AttributoRientroIniziale As Integer = 40011
    Private Const AttributoRientroFinale As Integer = 40012
    Private Const AttributoPedice As Integer = 40016
    Private Const AttributoApice As Integer = 40017
    Private Const AttributoBarrato As Integer = 40026
    Private Const AttributoSottolineato As Integer = 40030
    Private Const AttributoAnnotazioni As Integer = 40031
    Private Const AttributoStile As Integer = 40033
    Private Const AttributoCollegamento As Integer = 40035
    Private Const ErroreOrtografico As Integer = 60001
    Private Const ErroreGrammatica As Integer = 60002

    Private ReadOnly log As New Logger()
    Private ReadOnly _ultimoFormato As String = ""
    Private _firmaFormato As String = ""
    Private _vociPrecedenti As List(Of String())

    Public Function DescriviFormato(elemento As IUIAutomationElement, Optional completo As Boolean = True) As String
        Try
            Dim intervallo As IUIAutomationTextRange = IntervalloCursore(elemento)
            If intervallo Is Nothing Then Return ""
            Try
                Return Componi(intervallo, completo)
            Finally
                ComLettore.Rilascia(intervallo)
            End Try
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.DescriviFormato: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function FormatoCambiato(elemento As IUIAutomationElement) As String
        Try
            Dim intervallo As IUIAutomationTextRange = IntervalloCursore(elemento)
            If intervallo Is Nothing Then Return ""
            Dim voci As List(Of String()) = Nothing
            Dim iniziale As String = ""
            Try
                voci = VociFormato(intervallo)
                If _vociPrecedenti Is Nothing Then iniziale = Componi(intervallo, False)
            Finally
                ComLettore.Rilascia(intervallo)
            End Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return ""
            Dim firma As New StringBuilder()
            For Each voce As String() In voci
                firma.Append(voce(0)).Append("="c).Append(voce(1)).Append(";"c)
            Next
            Dim testoFirma As String = firma.ToString()
            If String.Equals(testoFirma, _firmaFormato, StringComparison.Ordinal) Then Return ""
            Dim testo As String = If(_vociPrecedenti Is Nothing, iniziale, Differenze(_vociPrecedenti, voci))
            _firmaFormato = testoFirma
            _vociPrecedenti = voci
            Return testo
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.FormatoCambiato: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function UnitaAlCursore(elemento As IUIAutomationElement, unita As TextUnit) As String
        Try
            Dim intervallo As IUIAutomationTextRange = IntervalloCursore(elemento)
            If intervallo Is Nothing Then Return ""
            Try
                Dim selezionato As String = TestoSelezione(intervallo)
                If selezionato <> "" AndAlso unita <> TextUnit.TextUnit_Character Then Return selezionato
                intervallo.ExpandToEnclosingUnit(unita)
                Dim testo As String = intervallo.GetText(2000)
                If testo Is Nothing Then Return ""
                Return testo.Trim(ChrW(13), ChrW(10), ChrW(11), " "c)
            Finally
                ComLettore.Rilascia(intervallo)
            End Try
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.UnitaAlCursore: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function PreparaLetturaContinua(elemento As IUIAutomationElement) As IUIAutomationTextRange
        Try
            Dim intervallo As IUIAutomationTextRange = IntervalloCursore(elemento)
            If intervallo Is Nothing Then Return Nothing
            Try
                intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaAttributi.PreparaLetturaContinua", exAtteso)
            End Try
            Return intervallo
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.PreparaLetturaContinua: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function TestoRiga(intervallo As IUIAutomationTextRange) As String
        Try
            If intervallo Is Nothing Then Return ""
            Dim testo As String = intervallo.GetText(4000)
            If testo Is Nothing Then Return ""
            Return testo.Trim(ChrW(13), ChrW(10), ChrW(11), " "c)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.TestoRiga", exAtteso)
            Return ""
        End Try
    End Function

    Public Function AvanzaRiga(intervallo As IUIAutomationTextRange, Optional spostaCursore As Boolean = True) As Boolean
        Dim copia As IUIAutomationTextRange = Nothing
        Try
            If intervallo Is Nothing Then Return False
            Dim mosso As Integer = intervallo.Move(TextUnit.TextUnit_Line, 1)
            If mosso = 0 Then Return False
            intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
            If Not spostaCursore Then Return True
            copia = intervallo.Clone()
            copia.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, copia, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            copia.Select()
            Return True
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.AvanzaRiga: " & ex.Message)
            Return False
        Finally
            ComLettore.Rilascia(copia)
        End Try
    End Function

    Public Function HaPattern(elemento As IUIAutomationElement) As Boolean
        Dim intervallo As IUIAutomationTextRange = Nothing
        Try
            intervallo = IntervalloCursore(elemento)
            Return intervallo IsNot Nothing
        Finally
            ComLettore.Rilascia(intervallo)
        End Try
    End Function

    Public Shared Function ProviderSembraRotto(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return True
            If descrittore.Nome(elemento) <> "" Then Return False
            If descrittore.HaPatternTesto(elemento) OrElse descrittore.HaValore(elemento) Then Return False
            Dim tipo As Integer = descrittore.TipoControllo(elemento)
            If tipo <> 0 AndAlso tipo <> DescrittoreElementi.ControlloPersonalizzato AndAlso tipo <> DescrittoreElementi.ControlloRiquadro AndAlso tipo <> DescrittoreElementi.ControlloGruppo AndAlso tipo <> DescrittoreElementi.ControlloImmagine Then Return False
            Dim descrizione As String = ""
            Try
                descrizione = If(elemento.CurrentProviderDescription, "")
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaAttributi.ProviderSembraRotto", exAtteso)
                Return True
            End Try
            If descrizione = "" Then Return True
            Dim basso As String = descrizione.ToLowerInvariant()
            If basso.Contains("unresponsive") OrElse basso.Contains("timeout") OrElse basso.Contains("disconnected") Then Return True
            Return tipo = DescrittoreElementi.ControlloImmagine OrElse tipo = DescrittoreElementi.ControlloPersonalizzato
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaAttributi.ProviderSembraRotto", exAtteso2)
            Return True
        End Try
    End Function

    Private Function IntervalloCursore(elemento As IUIAutomationElement) As IUIAutomationTextRange
        Try
            If elemento Is Nothing Then Return Nothing
            Dim pattern2 As IUIAutomationTextPattern2 = Nothing
            Try
                pattern2 = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto2), IUIAutomationTextPattern2)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaAttributi.IntervalloCursore", exAtteso)
                pattern2 = Nothing
            End Try
            If pattern2 IsNot Nothing Then
                Dim attivo As Integer = 0
                Dim cursore As IUIAutomationTextRange = pattern2.GetCaretRange(attivo)
                ComLettore.Rilascia(pattern2)
                If cursore IsNot Nothing Then Return cursore
            End If
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return Nothing
            Dim selezioni As IUIAutomationTextRangeArray = pattern.GetSelection()
            ComLettore.Rilascia(pattern)
            If selezioni Is Nothing OrElse selezioni.Length = 0 Then
                ComLettore.Rilascia(selezioni)
                Return Nothing
            End If
            Dim primo As IUIAutomationTextRange = selezioni.GetElement(0)
            ComLettore.Rilascia(selezioni)
            Return primo
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaAttributi.IntervalloCursore", exAtteso2)
            Return Nothing
        End Try
    End Function

    Private Function TestoSelezione(intervallo As IUIAutomationTextRange) As String
        Try
            If intervallo.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) = 0 Then Return ""
            Dim testo As String = intervallo.GetText(300)
            If testo Is Nothing Then Return ""
            Return testo.Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.TestoSelezione", exAtteso)
            Return ""
        End Try
    End Function

    Private Function Componi(intervallo As IUIAutomationTextRange, completo As Boolean) As String
        Dim parti As New List(Of String)()
        Dim nome As String = LeggiStringa(intervallo, AttributoNomeCarattere)
        If nome <> "" Then parti.Add(nome)
        Dim dimensione As String = LeggiNumero(intervallo, AttributoDimensione)
        If dimensione <> "" Then parti.Add(dimensione & " punti")
        Dim peso As Integer = LeggiIntero(intervallo, AttributoPeso)
        If peso >= 700 Then parti.Add("grassetto")
        If LeggiBool(intervallo, AttributoCorsivo) Then parti.Add("corsivo")
        Dim sottolineato As Integer = LeggiIntero(intervallo, AttributoSottolineato)
        If sottolineato = 3 Then
            parti.Add("sottolineato doppio")
        ElseIf sottolineato <> 0 Then
            parti.Add("sottolineato")
        End If
        Dim barrato As Integer = LeggiIntero(intervallo, AttributoBarrato)
        If barrato = 3 Then
            parti.Add("barrato doppio")
        ElseIf barrato <> 0 Then
            parti.Add("barrato")
        End If
        If LeggiBool(intervallo, AttributoApice) Then parti.Add("apice")
        If LeggiBool(intervallo, AttributoPedice) Then parti.Add("pedice")
        Dim colore As Integer = LeggiColore(intervallo, AttributoColoreTesto)
        If colore >= 0 Then
            Dim testoColore As String = NomeColore(colore)
            If testoColore <> "" AndAlso (completo OrElse testoColore <> "nero") Then parti.Add("colore " & testoColore)
        End If
        Dim sfondo As Integer = LeggiColore(intervallo, AttributoColoreSfondo)
        If sfondo >= 0 Then
            Dim nomeSfondo As String = NomeColore(sfondo)
            If nomeSfondo <> "" AndAlso (completo OrElse nomeSfondo <> "bianco") Then parti.Add("sfondo " & nomeSfondo)
        End If
        Dim stile As String = LeggiStringa(intervallo, AttributoStile)
        If stile <> "" Then parti.Add(stile)
        If HaAnnotazione(intervallo, ErroreOrtografico) Then parti.Add("errore di ortografia")
        If HaAnnotazione(intervallo, ErroreGrammatica) Then parti.Add("errore di grammatica")
        If HaCollegamento(intervallo) Then parti.Add("collegamento")
        Dim allineamento As String = NomeAllineamento(LeggiIntero(intervallo, AttributoAllineamento, -1))
        If allineamento <> "" AndAlso (completo OrElse allineamento <> "allineato a sinistra") Then parti.Add(allineamento)
        If completo Then AggiungiRientri(intervallo, parti)
        Dim cultura As Integer = LeggiIntero(intervallo, AttributoCultura)
        If cultura > 0 AndAlso cultura <> 1040 AndAlso cultura <> 16 Then
            Try
                parti.Add(New System.Globalization.CultureInfo(cultura).DisplayName)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaAttributi.Componi", exAtteso)
            End Try
        End If
        Return String.Join(", ", parti)
    End Function

    Private Function VociFormato(intervallo As IUIAutomationTextRange) As List(Of String())
        Dim voci As New List(Of String())()
        Try
            Dim nome As String = LeggiStringa(intervallo, AttributoNomeCarattere)
            If nome <> "" Then voci.Add({"nome", nome, ""})
            Dim dimensione As String = LeggiNumero(intervallo, AttributoDimensione)
            If dimensione <> "" Then voci.Add({"dimensione", dimensione & " punti", ""})
            If LeggiIntero(intervallo, AttributoPeso) >= 700 Then voci.Add({"grassetto", "grassetto", "non grassetto"})
            If LeggiBool(intervallo, AttributoCorsivo) Then voci.Add({"corsivo", "corsivo", "non corsivo"})
            Dim sottolineato As Integer = LeggiIntero(intervallo, AttributoSottolineato)
            If sottolineato <> 0 Then voci.Add({"sottolineato", If(sottolineato = 3, "sottolineato doppio", "sottolineato"), "non sottolineato"})
            Dim barrato As Integer = LeggiIntero(intervallo, AttributoBarrato)
            If barrato <> 0 Then voci.Add({"barrato", If(barrato = 3, "barrato doppio", "barrato"), "non barrato"})
            If LeggiBool(intervallo, AttributoApice) Then voci.Add({"apice", "apice", "non apice"})
            If LeggiBool(intervallo, AttributoPedice) Then voci.Add({"pedice", "pedice", "non pedice"})
            Dim colore As Integer = LeggiColore(intervallo, AttributoColoreTesto)
            If colore >= 0 Then
                Dim testoColore As String = NomeColore(colore)
                If testoColore <> "" Then voci.Add({"colore", "colore " & testoColore, ""})
            End If
            Dim sfondo As Integer = LeggiColore(intervallo, AttributoColoreSfondo)
            If sfondo >= 0 Then
                Dim nomeSfondo As String = NomeColore(sfondo)
                If nomeSfondo <> "" Then voci.Add({"sfondo", "sfondo " & nomeSfondo, ""})
            End If
            Dim stile As String = LeggiStringa(intervallo, AttributoStile)
            If stile <> "" Then voci.Add({"stile", stile, ""})
            If HaAnnotazione(intervallo, ErroreOrtografico) Then voci.Add({"ortografia", "errore di ortografia", ""})
            If HaAnnotazione(intervallo, ErroreGrammatica) Then voci.Add({"grammatica", "errore di grammatica", ""})
            If HaCollegamento(intervallo) Then voci.Add({"collegamento", "collegamento", "fuori dal collegamento"})
            Dim allineamento As String = NomeAllineamento(LeggiIntero(intervallo, AttributoAllineamento, -1))
            If allineamento <> "" Then voci.Add({"allineamento", allineamento, ""})
            Dim cultura As Integer = LeggiIntero(intervallo, AttributoCultura)
            If cultura > 0 AndAlso cultura <> 1040 AndAlso cultura <> 16 Then
                Try
                    voci.Add({"lingua", New System.Globalization.CultureInfo(cultura).DisplayName, "italiano"})
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("LetturaAttributi.VociFormato", exAtteso)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("LetturaAttributi.VociFormato: " & ex.Message)
        End Try
        Return voci
    End Function

    Private Shared Function Differenze(precedenti As List(Of String()), attuali As List(Of String())) As String
        Dim parti As New List(Of String)()
        Try
            For Each voce As String() In attuali
                Dim vecchia As String() = CercaVoce(precedenti, voce(0))
                If vecchia Is Nothing OrElse Not String.Equals(vecchia(1), voce(1), StringComparison.Ordinal) Then parti.Add(voce(1))
            Next
            For Each voce As String() In precedenti
                If voce(2) <> "" AndAlso CercaVoce(attuali, voce(0)) Is Nothing Then parti.Add(voce(2))
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaAttributi.Differenze", ex)
        End Try
        Return String.Join(", ", parti)
    End Function

    Private Shared Function CercaVoce(voci As List(Of String()), chiave As String) As String()
        If voci Is Nothing Then Return Nothing
        For Each voce As String() In voci
            If String.Equals(voce(0), chiave, StringComparison.Ordinal) Then Return voce
        Next
        Return Nothing
    End Function

    Private Sub AggiungiRientri(intervallo As IUIAutomationTextRange, parti As List(Of String))
        Try
            Dim primaRiga As Double = LeggiDoppio(intervallo, AttributoRientroPrimaRiga)
            If primaRiga >= 0.5 Then
                parti.Add("rientro prima riga " & Punti(primaRiga))
            ElseIf primaRiga <= -0.5 Then
                parti.Add("rientro sporgente " & Punti(-primaRiga))
            End If
            Dim sinistro As Double = LeggiDoppio(intervallo, AttributoRientroIniziale)
            If sinistro >= 0.5 Then parti.Add("rientro sinistro " & Punti(sinistro))
            Dim destro As Double = LeggiDoppio(intervallo, AttributoRientroFinale)
            If destro >= 0.5 Then parti.Add("rientro destro " & Punti(destro))
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.AggiungiRientri", exAtteso)
        End Try
    End Sub

    Private Shared Function Punti(valore As Double) As String
        Try
            Dim arrotondato As Double = Math.Round(valore, 1)
            Return arrotondato.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) & " punti"
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.Punti", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomeAllineamento(valore As Integer) As String
        Select Case valore
            Case 0
                Return "allineato a sinistra"
            Case 1
                Return "centrato"
            Case 2
                Return "allineato a destra"
            Case 3
                Return "giustificato"
        End Select
        Return ""
    End Function

    Public Shared Function NomeColore(colore As Integer) As String
        Try
            If colore < 0 OrElse colore > &HFFFFFF Then Return ""
            Dim rosso As Integer = colore And &HFF
            Dim verde As Integer = (colore >> 8) And &HFF
            Dim blu As Integer = (colore >> 16) And &HFF
            Dim migliore As String = ""
            Dim distanzaMinima As Double = Double.MaxValue
            For Each voce As VoceColore In _tavolozza
                Dim media As Double = (rosso + voce.Rosso) / 2.0
                Dim dr As Double = rosso - voce.Rosso
                Dim dv As Double = verde - voce.Verde
                Dim db As Double = blu - voce.Blu
                Dim distanza As Double = (2.0 + media / 256.0) * dr * dr + 4.0 * dv * dv + (2.0 + (255.0 - media) / 256.0) * db * db
                If distanza < distanzaMinima Then
                    distanzaMinima = distanza
                    migliore = voce.Nome
                End If
            Next
            Return migliore
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.NomeColore", exAtteso)
            Return ""
        End Try
    End Function

    Private NotInheritable Class VoceColore
        Public ReadOnly Nome As String
        Public ReadOnly Rosso As Integer
        Public ReadOnly Verde As Integer
        Public ReadOnly Blu As Integer

        Public Sub New(nome As String, rosso As Integer, verde As Integer, blu As Integer)
            Me.Nome = nome
            Me.Rosso = rosso
            Me.Verde = verde
            Me.Blu = blu
        End Sub
    End Class

    Private Shared ReadOnly _tavolozza As VoceColore() = {
        New VoceColore("nero", 0, 0, 0),
        New VoceColore("bianco", 255, 255, 255),
        New VoceColore("grigio", 128, 128, 128),
        New VoceColore("grigio chiaro", 200, 200, 200),
        New VoceColore("grigio scuro", 64, 64, 64),
        New VoceColore("rosso", 255, 0, 0),
        New VoceColore("rosso scuro", 160, 0, 0),
        New VoceColore("bordeaux", 110, 0, 30),
        New VoceColore("rosa", 255, 182, 193),
        New VoceColore("salmone", 250, 128, 114),
        New VoceColore("fucsia", 255, 0, 255),
        New VoceColore("viola", 128, 0, 128),
        New VoceColore("lilla", 200, 162, 200),
        New VoceColore("indaco", 75, 0, 130),
        New VoceColore("blu", 0, 0, 255),
        New VoceColore("blu", 68, 114, 196),
        New VoceColore("blu scuro", 0, 0, 139),
        New VoceColore("blu notte", 0, 32, 96),
        New VoceColore("azzurro", 0, 127, 255),
        New VoceColore("celeste", 135, 206, 235),
        New VoceColore("ciano", 0, 255, 255),
        New VoceColore("turchese", 64, 224, 208),
        New VoceColore("acquamarina", 127, 255, 212),
        New VoceColore("verde", 0, 128, 0),
        New VoceColore("verde", 112, 173, 71),
        New VoceColore("verde chiaro", 144, 238, 144),
        New VoceColore("verde brillante", 0, 255, 0),
        New VoceColore("verde scuro", 0, 80, 0),
        New VoceColore("verde oliva", 128, 128, 0),
        New VoceColore("giallo", 255, 255, 0),
        New VoceColore("giallo chiaro", 255, 255, 200),
        New VoceColore("oro", 255, 192, 0),
        New VoceColore("arancione", 255, 165, 0),
        New VoceColore("arancione", 237, 125, 49),
        New VoceColore("arancione scuro", 255, 110, 0),
        New VoceColore("marrone", 139, 69, 19),
        New VoceColore("marrone chiaro", 196, 140, 90),
        New VoceColore("beige", 245, 235, 210)
    }

    Private Function LeggiStringa(intervallo As IUIAutomationTextRange, attributo As Integer) As String
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If TypeOf valore Is String Then Return CStr(valore).Trim()
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiStringa", exAtteso)
            Return ""
        End Try
    End Function

    Private Function LeggiIntero(intervallo As IUIAutomationTextRange, attributo As Integer, Optional mancante As Integer = 0) As Integer
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If valore Is Nothing Then Return mancante
            If TypeOf valore Is Integer Then Return CInt(valore)
            If TypeOf valore Is Double Then Return CInt(CDbl(valore))
            If TypeOf valore Is String Then
                Dim numero As Integer = 0
                If Integer.TryParse(CStr(valore), numero) Then Return numero
            End If
            Return mancante
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiIntero", exAtteso)
            Return mancante
        End Try
    End Function

    Private Function LeggiColore(intervallo As IUIAutomationTextRange, attributo As Integer) As Integer
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If TypeOf valore Is Integer Then Return CInt(valore) And &HFFFFFF
            If TypeOf valore Is Double Then Return CInt(CDbl(valore)) And &HFFFFFF
            Return -1
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiColore", exAtteso)
            Return -1
        End Try
    End Function

    Private Function LeggiDoppio(intervallo As IUIAutomationTextRange, attributo As Integer) As Double
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If TypeOf valore Is Double Then Return CDbl(valore)
            If TypeOf valore Is Single Then Return CDbl(CSng(valore))
            If TypeOf valore Is Integer Then Return CDbl(CInt(valore))
            Return 0.0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiDoppio", exAtteso)
            Return 0.0
        End Try
    End Function

    Private Function HaCollegamento(intervallo As IUIAutomationTextRange) As Boolean
        Try
            Dim valore As Object = intervallo.GetAttributeValue(AttributoCollegamento)
            If valore Is Nothing Then Return False
            If TypeOf valore Is Boolean Then Return CBool(valore)
            Dim collegamento As IUIAutomationTextRange = TryCast(valore, IUIAutomationTextRange)
            Return collegamento IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.HaCollegamento", exAtteso)
            Return False
        End Try
    End Function

    Private Function LeggiNumero(intervallo As IUIAutomationTextRange, attributo As Integer) As String
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If valore Is Nothing Then Return ""
            If TypeOf valore Is Double Then Return Math.Round(CDbl(valore)).ToString()
            If TypeOf valore Is Integer Then Return CInt(valore).ToString()
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiNumero", exAtteso)
            Return ""
        End Try
    End Function

    Private Function LeggiBool(intervallo As IUIAutomationTextRange, attributo As Integer) As Boolean
        Try
            Dim valore As Object = intervallo.GetAttributeValue(attributo)
            If TypeOf valore Is Boolean Then Return CBool(valore)
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.LeggiBool", exAtteso)
            Return False
        End Try
    End Function

    Private Function HaAnnotazione(intervallo As IUIAutomationTextRange, cercata As Integer) As Boolean
        Try
            Dim valore As Object = intervallo.GetAttributeValue(AttributoAnnotazioni)
            Dim vettore As Array = TryCast(valore, Array)
            If vettore Is Nothing Then Return False
            For Each voce As Object In vettore
                If TypeOf voce Is Integer AndAlso CInt(voce) = cercata Then Return True
            Next
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAttributi.HaAnnotazione", exAtteso)
            Return False
        End Try
    End Function

End Class