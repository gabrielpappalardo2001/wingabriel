Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Gestione e navigazione avanzata di tabelle, griglie, celle e relative coordinate.
Friend NotInheritable Class LetturaTabelle

    Private Const SchemaGriglia As Integer = 10006
    Private Const SchemaCella As Integer = 10007
    Private Const SchemaTabella As Integer = 10012
    Private Const SchemaVoceTabella As Integer = 10013

    Private Sub New()
    End Sub

    Public Shared Property SoloSeCambia As Boolean = True
    <ThreadStatic>
    Public Shared Completo As Boolean
    Private Shared _ultimaGriglia As String = ""
    Private Shared _ultimaRiga As Integer = -1
    Private Shared _ultimaColonna As Integer = -1
    Private Shared _ultimoTick As Integer
    Private Shared _ultimaFrase As String = ""

    Private Shared ReadOnly _impaginazioni As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Private Shared ReadOnly _bloccoImpaginazioni As New Object()

    Public Shared Function TabellaDiImpaginazione(tabella As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Try
            If tabella Is Nothing OrElse descrittore Is Nothing Then Return False
            If Not DescrittoreElementi.FrameworkWeb(descrittore.Framework(tabella)) Then Return False
            Select Case descrittore.RuoloAria(tabella)
                Case "grid", "treegrid"
                    Return False
            End Select
            Dim chiave As String = ChiaveElemento(tabella)
            If chiave <> "" Then
                SyncLock _bloccoImpaginazioni
                    Dim noto As Boolean
                    If _impaginazioni.TryGetValue(chiave, noto) Then Return noto
                End SyncLock
            End If
            Dim esito As Boolean = ValutaImpaginazione(tabella, descrittore)
            If chiave <> "" Then
                SyncLock _bloccoImpaginazioni
                    If _impaginazioni.Count > 2000 Then _impaginazioni.Clear()
                    _impaginazioni(chiave) = esito
                End SyncLock
            End If
            Return esito
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.TabellaDiImpaginazione", exAtteso)
            Return False
        End Try
    End Function

    Public Shared Function CellaDiImpaginazione(cella As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Try
            Return TabellaDiImpaginazione(GrigliaDi(cella), descrittore)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.CellaDiImpaginazione", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function ValutaImpaginazione(tabella As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Try
            Dim tavola As IUIAutomationTablePattern = TryCast(tabella.GetCurrentPattern(SchemaTabella), IUIAutomationTablePattern)
            If tavola IsNot Nothing Then
                Dim colonne As IUIAutomationElementArray = tavola.GetCurrentColumnHeaders()
                If colonne IsNot Nothing AndAlso colonne.Length > 0 Then Return False
                Dim righeTitolo As IUIAutomationElementArray = tavola.GetCurrentRowHeaders()
                If righeTitolo IsNot Nothing AndAlso righeTitolo.Length > 0 Then Return False
            End If
            Dim griglia As IUIAutomationGridPattern = PatternGriglia(tabella)
            If griglia IsNot Nothing Then
                Dim limite As Integer = Math.Min(griglia.CurrentColumnCount, 12)
                For colonna As Integer = 0 To limite - 1
                    Dim prima As IUIAutomationElement = Nothing
                    Try
                        prima = griglia.GetItem(0, colonna)
                    Catch exCella As Exception
                        RegistroErrori.Scrivi("LetturaTabelle.ValutaImpaginazione", exCella)
                    End Try
                    If prima Is Nothing Then Continue For
                    Dim ruolo As String = descrittore.RuoloAria(prima)
                    If ruolo = "columnheader" OrElse ruolo = "rowheader" OrElse descrittore.TipoControllo(prima) = DescrittoreElementi.ControlloElementoIntestazione Then Return False
                Next
                If griglia.CurrentColumnCount <= 1 OrElse griglia.CurrentRowCount <= 1 Then Return True
            End If
            Dim condizione As New CUIAutomation()
            Dim interna As IUIAutomationElement = tabella.FindFirst(TreeScope.TreeScope_Descendants, condizione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloTabella))
            Return interna IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ValutaImpaginazione", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function ChiaveElemento(elemento As IUIAutomationElement) As String
        Try
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ChiaveElemento", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function ChiaveGriglia(cella As IUIAutomationElement) As String
        Try
            Dim griglia As IUIAutomationElement = GrigliaDi(cella)
            If griglia Is Nothing Then Return ""
            Dim id As Array = griglia.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ChiaveGriglia", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function DescriviCella(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As String
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return ""
            If Not SembraCella(elemento, descrittore) Then Return ""
            Dim cella As IUIAutomationElement = CellaDi(elemento, descrittore, Nothing)
            If cella Is Nothing Then cella = elemento
            If CellaDiImpaginazione(cella, descrittore) Then Return ""
            Dim parti As New List(Of String)()
            Dim ruolo As String = descrittore.RuoloAria(cella)
            If ruolo = "columnheader" Then
                parti.Add("intestazione di colonna")
            ElseIf ruolo = "rowheader" Then
                parti.Add("intestazione di riga")
            End If
            Dim riga As Integer = 0
            Dim colonna As Integer = 0
            Dim righe As Integer = 0
            Dim colonne As Integer = 0
            Dim conCoordinate As Boolean = Coordinate(cella, riga, colonna, righe, colonne)
            Dim chiave As String = If(conCoordinate, ChiaveGriglia(cella), "")
            Dim adesso As Integer = Environment.TickCount
            Dim continua As Boolean = conCoordinate AndAlso SoloSeCambia AndAlso Not Completo AndAlso chiave <> "" AndAlso
                                      chiave = _ultimaGriglia AndAlso adesso - _ultimoTick < 4000
            Dim stessaRiga As Boolean = continua AndAlso riga = _ultimaRiga
            Dim stessaColonna As Boolean = continua AndAlso colonna = _ultimaColonna
            If stessaRiga AndAlso stessaColonna AndAlso _ultimaFrase <> "" Then
                _ultimoTick = adesso
                Return _ultimaFrase
            End If
            If Not stessaColonna Then
                Dim colonnaNome As String = Intestazioni(cella, descrittore, True)
                If colonnaNome <> "" Then parti.Add("colonna " & colonnaNome)
            End If
            If Not stessaRiga Then
                Dim rigaNome As String = Intestazioni(cella, descrittore, False)
                If rigaNome <> "" Then parti.Add("riga " & rigaNome)
            End If
            If conCoordinate Then
                If continua Then
                    If Not stessaRiga Then parti.Add("riga " & (riga + 1).ToString())
                    If Not stessaColonna Then parti.Add("colonna " & (colonna + 1).ToString())
                Else
                    parti.Add(FrasePosizione("riga", riga + 1, righe))
                    parti.Add(FrasePosizione("colonna", colonna + 1, colonne))
                End If
            End If
            Dim estesa As String = EstensioneCella(cella)
            If estesa <> "" Then parti.Add(estesa)
            Dim frase As String = String.Join(", ", parti)
            If conCoordinate AndAlso chiave <> "" Then
                _ultimaGriglia = chiave
                _ultimaRiga = riga
                _ultimaColonna = colonna
                _ultimoTick = adesso
                _ultimaFrase = frase
            End If
            Return frase
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.DescriviCella", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function FraseRiga(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As String
        Try
            If Not SembraCella(elemento, descrittore) AndAlso CellaDi(elemento, descrittore, automazione) Is Nothing Then Return ""
            Dim cella As IUIAutomationElement = CellaDi(elemento, descrittore, automazione)
            If cella Is Nothing Then Return ""
            Dim riga As Integer = 0
            Dim colonna As Integer = 0
            Dim righe As Integer = 0
            Dim colonne As Integer = 0
            If Not Coordinate(cella, riga, colonna, righe, colonne) Then Return FraseRigaFratelli(cella, descrittore, automazione, cache)
            Dim griglia As IUIAutomationElement = GrigliaDi(cella)
            Dim pattern As IUIAutomationGridPattern = PatternGriglia(griglia)
            If pattern Is Nothing Then Return FraseRigaFratelli(cella, descrittore, automazione, cache)
            Dim testo As New StringBuilder()
            testo.Append("riga ").Append(riga + 1)
            Dim limite As Integer = Math.Min(colonne, 16)
            For indice As Integer = 0 To limite - 1
                AccodaVoce(testo, ValoreCella(pattern, descrittore, riga, indice, If(automazione Is Nothing, Nothing, automazione.ControlViewWalker)))
            Next
            If colonne > limite Then testo.Append(", altre colonne")
            Return testo.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.FraseRiga", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function FraseColonna(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As String
        Try
            Dim cella As IUIAutomationElement = CellaDi(elemento, descrittore, automazione)
            If cella Is Nothing Then Return ""
            Dim riga As Integer = 0
            Dim colonna As Integer = 0
            Dim righe As Integer = 0
            Dim colonne As Integer = 0
            If Not Coordinate(cella, riga, colonna, righe, colonne) Then Return ""
            Dim pattern As IUIAutomationGridPattern = PatternGriglia(GrigliaDi(cella))
            If pattern Is Nothing Then Return ""
            Dim titolo As String = Intestazioni(cella, descrittore, True)
            Dim testo As New StringBuilder()
            testo.Append("colonna ")
            If titolo <> "" Then
                testo.Append(titolo)
            Else
                testo.Append(colonna + 1)
            End If
            Dim limite As Integer = Math.Min(righe, 12)
            For indice As Integer = 0 To limite - 1
                AccodaVoce(testo, ValoreCella(pattern, descrittore, indice, colonna, If(automazione Is Nothing, Nothing, automazione.ControlViewWalker)))
            Next
            If righe > limite Then testo.Append(", altre righe")
            Return testo.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.FraseColonna", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function CellaVicina(elemento As IUIAutomationElement, deltaRiga As Integer, deltaColonna As Integer, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest, ByRef errore As String) As IUIAutomationElement
        errore = ""
        Try
            Dim cella As IUIAutomationElement = CellaDi(elemento, descrittore, automazione)
            If cella Is Nothing Then
                errore = "non sei in una tabella"
                Return Nothing
            End If
            Dim riga As Integer = 0
            Dim colonna As Integer = 0
            Dim righe As Integer = 0
            Dim colonne As Integer = 0
            If Coordinate(cella, riga, colonna, righe, colonne) Then
                Dim destinazioneRiga As Integer = riga + deltaRiga
                Dim destinazioneColonna As Integer = colonna + deltaColonna
                Dim oltre As Boolean = destinazioneRiga < 0 OrElse destinazioneColonna < 0
                If righe > 0 AndAlso destinazioneRiga >= righe Then oltre = True
                If colonne > 0 AndAlso destinazioneColonna >= colonne Then oltre = True
                If oltre Then
                    errore = "bordo della tabella"
                    Return Nothing
                End If
                Dim pattern As IUIAutomationGridPattern = PatternGriglia(GrigliaDi(cella))
                If pattern IsNot Nothing Then
                    Dim trovata As IUIAutomationElement = pattern.GetItem(destinazioneRiga, destinazioneColonna)
                    If trovata Is Nothing Then
                        errore = "cella vuota"
                        Return Nothing
                    End If
                    Return ConCache(trovata, cache)
                End If
            End If
            Dim fratello As IUIAutomationElement = CellaFratello(cella, deltaRiga, deltaColonna, descrittore, automazione, cache)
            If fratello Is Nothing Then errore = "bordo della tabella"
            Return fratello
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.CellaVicina", exAtteso)
            errore = "tabella non disponibile"
            Return Nothing
        End Try
    End Function

    Private Shared Function SembraCella(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Select Case descrittore.RuoloAria(elemento)
            Case "cell", "gridcell", "rowheader", "columnheader", "row", "table", "grid"
                Return True
        End Select
        Select Case descrittore.TipoControllo(elemento)
            Case DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloTabella, DescrittoreElementi.ControlloGrigliaDati,
                 DescrittoreElementi.ControlloIntestazione, DescrittoreElementi.ControlloElementoIntestazione
                Return True
        End Select
        Return False
    End Function

    Private Shared Function CellaDi(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation) As IUIAutomationElement
        Dim corrente As IUIAutomationElement = elemento
        Dim walker As IUIAutomationTreeWalker = AlberoVista(automazione)
        For salto As Integer = 0 To 6
            If corrente Is Nothing Then Return Nothing
            Try
                Dim voce As IUIAutomationGridItemPattern = TryCast(corrente.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
                If voce IsNot Nothing Then Return corrente
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaTabelle.CellaDi", exAtteso)
            End Try
            Dim ruolo As String = descrittore.RuoloAria(corrente)
            If ruolo = "cell" OrElse ruolo = "gridcell" OrElse ruolo = "rowheader" OrElse ruolo = "columnheader" OrElse ruolo = "row" Then Return corrente
            If walker Is Nothing Then Return Nothing
            Try
                corrente = walker.GetParentElement(corrente)
            Catch exAtteso2 As Exception
                RegistroErrori.Scrivi("LetturaTabelle.CellaDi", exAtteso2)
                Return Nothing
            End Try
        Next
        Return Nothing
    End Function

    Private Shared Function Coordinate(cella As IUIAutomationElement, ByRef riga As Integer, ByRef colonna As Integer, ByRef righe As Integer, ByRef colonne As Integer) As Boolean
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(cella.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce IsNot Nothing Then
                riga = voce.CurrentRow
                colonna = voce.CurrentColumn
                Dim pattern As IUIAutomationGridPattern = PatternGriglia(voce.CurrentContainingGrid)
                If pattern Is Nothing Then
                    righe = riga + 1
                    colonne = colonna + 1
                Else
                    righe = Math.Max(1, pattern.CurrentRowCount)
                    colonne = Math.Max(1, pattern.CurrentColumnCount)
                End If
                Return True
            End If
            Dim rigaAria As Integer = LeggiAria(cella, "rowindex")
            Dim colonnaAria As Integer = LeggiAria(cella, "colindex")
            If rigaAria <= 0 AndAlso colonnaAria <= 0 Then Return False
            riga = Math.Max(0, rigaAria - 1)
            colonna = Math.Max(0, colonnaAria - 1)
            righe = LeggiAria(cella, "rowcount")
            colonne = LeggiAria(cella, "colcount")
            If righe > 0 AndAlso riga >= righe Then righe = riga + 1
            If colonne > 0 AndAlso colonna >= colonne Then colonne = colonna + 1
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.Coordinate", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function GrigliaDi(cella As IUIAutomationElement) As IUIAutomationElement
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(cella.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce Is Nothing Then Return Nothing
            Return voce.CurrentContainingGrid
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.GrigliaDi", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function PatternGriglia(griglia As IUIAutomationElement) As IUIAutomationGridPattern
        Try
            If griglia Is Nothing Then Return Nothing
            Return TryCast(griglia.GetCurrentPattern(SchemaGriglia), IUIAutomationGridPattern)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.PatternGriglia", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function Intestazioni(cella As IUIAutomationElement, descrittore As DescrittoreElementi, diColonna As Boolean) As String
        Try
            Dim dalPattern As String = IntestazioniPattern(cella, descrittore, diColonna)
            If dalPattern <> "" Then Return dalPattern
            Return IntestazioneDallaGriglia(cella, descrittore, diColonna)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.Intestazioni", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function IntestazioniPattern(cella As IUIAutomationElement, descrittore As DescrittoreElementi, diColonna As Boolean) As String
        Try
            Dim voce As IUIAutomationTableItemPattern = TryCast(cella.GetCurrentPattern(SchemaVoceTabella), IUIAutomationTableItemPattern)
            If voce Is Nothing Then Return ""
            Dim elenco As IUIAutomationElementArray = If(diColonna, voce.GetCurrentColumnHeaderItems(), voce.GetCurrentRowHeaderItems())
            Return NomiElenco(elenco, descrittore)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.IntestazioniPattern", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function IntestazioneDallaGriglia(cella As IUIAutomationElement, descrittore As DescrittoreElementi, diColonna As Boolean) As String
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(cella.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce Is Nothing Then Return ""
            Dim griglia As IUIAutomationElement = voce.CurrentContainingGrid
            Dim tabella As IUIAutomationTablePattern = TryCast(If(griglia Is Nothing, Nothing, griglia.GetCurrentPattern(SchemaTabella)), IUIAutomationTablePattern)
            If tabella IsNot Nothing Then
                Dim elenco As IUIAutomationElementArray = If(diColonna, tabella.GetCurrentColumnHeaders(), tabella.GetCurrentRowHeaders())
                Dim indice As Integer = If(diColonna, voce.CurrentColumn, voce.CurrentRow)
                If elenco IsNot Nothing AndAlso indice >= 0 AndAlso indice < elenco.Length Then
                    Dim nome As String = descrittore.NomeCorrente(elenco.GetElement(indice))
                    If nome <> "" Then Return nome
                End If
                Dim uniti As String = NomiElenco(elenco, descrittore)
                If uniti <> "" AndAlso (elenco Is Nothing OrElse elenco.Length <= 1) Then Return uniti
            End If
            If Not diColonna OrElse voce.CurrentRow <= 0 Then Return ""
            Dim pattern As IUIAutomationGridPattern = PatternGriglia(griglia)
            If pattern Is Nothing Then Return ""
            Dim prima As IUIAutomationElement = pattern.GetItem(0, voce.CurrentColumn)
            If prima Is Nothing Then Return ""
            Dim ruolo As String = descrittore.RuoloAria(prima)
            Dim tipo As Integer = descrittore.TipoControllo(prima)
            If ruolo <> "columnheader" AndAlso ruolo <> "rowheader" AndAlso tipo <> DescrittoreElementi.ControlloElementoIntestazione AndAlso tipo <> DescrittoreElementi.ControlloIntestazione Then Return ""
            Return descrittore.NomeCorrente(prima)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.IntestazioneDallaGriglia", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomiElenco(elenco As IUIAutomationElementArray, descrittore As DescrittoreElementi) As String
        Try
            If elenco Is Nothing OrElse elenco.Length = 0 OrElse descrittore Is Nothing Then Return ""
            Dim parti As New List(Of String)()
            For indice As Integer = 0 To Math.Min(elenco.Length, 3) - 1
                Dim nome As String = descrittore.NomeCorrente(elenco.GetElement(indice))
                If nome <> "" AndAlso Not parti.Contains(nome) Then parti.Add(nome)
            Next
            Return String.Join(" ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.NomiElenco", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function Sommario(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As String
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return ""
            Dim pattern As IUIAutomationGridPattern = PatternGriglia(elemento)
            If pattern Is Nothing Then pattern = PatternGriglia(GrigliaDi(elemento))
            If pattern IsNot Nothing Then Return FraseDimensioni(pattern.CurrentRowCount, pattern.CurrentColumnCount)
            Return FraseDimensioni(LeggiAria(elemento, "rowcount"), LeggiAria(elemento, "colcount"))
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.Sommario", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function EstensioneCella(cella As IUIAutomationElement) As String
        Try
            Dim righe As Integer = 1
            Dim colonne As Integer = 1
            Dim voce As IUIAutomationGridItemPattern = TryCast(cella.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce IsNot Nothing Then
                righe = voce.CurrentRowSpan
                colonne = voce.CurrentColumnSpan
            Else
                righe = LeggiAria(cella, "rowspan")
                colonne = LeggiAria(cella, "colspan")
            End If
            Dim parti As New List(Of String)()
            If righe > 1 Then parti.Add("occupa " & righe.ToString() & " righe")
            If colonne > 1 Then parti.Add("occupa " & colonne.ToString() & " colonne")
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.EstensioneCella", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function FrasePosizione(nome As String, indice As Integer, totale As Integer) As String
        If indice <= 0 Then Return ""
        If totale > 1 Then Return nome & " " & indice.ToString() & " di " & totale.ToString()
        Return nome & " " & indice.ToString()
    End Function

    Private Shared Function FraseDimensioni(righe As Integer, colonne As Integer) As String
        Dim parti As New List(Of String)()
        If righe = 1 Then
            parti.Add("1 riga")
        ElseIf righe > 1 Then
            parti.Add(righe.ToString() & " righe")
        End If
        If colonne = 1 Then
            parti.Add("1 colonna")
        ElseIf colonne > 1 Then
            parti.Add(colonne.ToString() & " colonne")
        End If
        Return String.Join(", ", parti)
    End Function

    Private Shared Function LeggiAria(elemento As IUIAutomationElement, chiave As String) As Integer
        Try
            If elemento Is Nothing OrElse chiave = "" Then Return 0
            Dim testo As String = elemento.CurrentAriaProperties
            If String.IsNullOrEmpty(testo) Then Return 0
            Dim indice As Integer = testo.IndexOf(chiave & "=", StringComparison.OrdinalIgnoreCase)
            If indice < 0 Then Return 0
            Dim numero As Integer = 0
            Dim trovato As Boolean = False
            For posizione As Integer = indice + chiave.Length + 1 To testo.Length - 1
                Dim carattere As Char = testo(posizione)
                If carattere < "0"c OrElse carattere > "9"c Then Exit For
                trovato = True
                numero = numero * 10 + AscW(carattere) - AscW("0"c)
            Next
            If trovato Then Return numero
            Return 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.LeggiAria", exAtteso)
            Return 0
        End Try
    End Function

    Private Shared Function ValoreCella(pattern As IUIAutomationGridPattern, descrittore As DescrittoreElementi, riga As Integer, colonna As Integer, Optional walker As IUIAutomationTreeWalker = Nothing) As String
        Try
            Dim cella As IUIAutomationElement = pattern.GetItem(riga, colonna)
            If cella Is Nothing Then Return ""
            Dim intestazione As String = Intestazioni(cella, descrittore, True)
            Dim valore As String = descrittore.NomeCorrente(cella)
            If valore = "" Then valore = descrittore.LeggiStringa(cella, DescrittoreElementi.ProprietaValore)
            If valore = "" AndAlso walker IsNot Nothing Then valore = LetturaRigheElenco.TestoVoce(cella, descrittore, walker, Nothing)
            If valore = "" Then Return ""
            If valore.Length > 80 Then valore = valore.Substring(0, 80)
            If intestazione = "" OrElse String.Equals(intestazione, valore, StringComparison.CurrentCultureIgnoreCase) Then Return valore
            Return intestazione & ": " & valore
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ValoreCella", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function FraseRigaFratelli(cella As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As String
        Dim celle As List(Of IUIAutomationElement) = CelleDellaRiga(cella, descrittore, automazione, cache)
        If celle.Count = 0 Then Return ""
        Dim testo As New StringBuilder()
        testo.Append("riga")
        Dim limite As Integer = Math.Min(celle.Count, 16)
        For indice As Integer = 0 To limite - 1
            Dim valore As String = descrittore.NomeCorrente(celle(indice))
            If valore = "" Then valore = descrittore.LeggiStringa(celle(indice), DescrittoreElementi.ProprietaValore)
            If valore.Length > 80 Then valore = valore.Substring(0, 80)
            Dim intestazione As String = Intestazioni(celle(indice), descrittore, True)
            If intestazione <> "" AndAlso Not String.Equals(intestazione, valore, StringComparison.CurrentCultureIgnoreCase) Then valore = intestazione & ": " & valore
            AccodaVoce(testo, valore)
        Next
        Return testo.ToString()
    End Function

    Private Shared Function CellaFratello(cella As IUIAutomationElement, deltaRiga As Integer, deltaColonna As Integer, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Dim celle As List(Of IUIAutomationElement) = CelleDellaRiga(cella, descrittore, automazione, cache)
        Dim indice As Integer = IndiceDi(celle, cella, automazione)
        If indice < 0 Then Return Nothing
        If deltaColonna <> 0 Then
            Dim altro As Integer = indice + deltaColonna
            If altro < 0 OrElse altro >= celle.Count Then Return Nothing
            Return celle(altro)
        End If
        If deltaRiga = 0 Then Return cella
        Dim riga As IUIAutomationElement = ElementoPadre(cella, automazione, cache)
        Dim righe As List(Of IUIAutomationElement) = Fratelli(riga, descrittore, automazione, cache, True)
        Dim indiceRiga As Integer = IndiceDi(righe, riga, automazione)
        If indiceRiga < 0 Then Return Nothing
        Dim destinazioneRiga As Integer = indiceRiga + deltaRiga
        If destinazioneRiga < 0 OrElse destinazioneRiga >= righe.Count Then Return Nothing
        Dim celleDestinazione As List(Of IUIAutomationElement) = CelleDellaRiga(righe(destinazioneRiga), descrittore, automazione, cache)
        If celleDestinazione.Count = 0 Then Return righe(destinazioneRiga)
        Return celleDestinazione(Math.Min(indice, celleDestinazione.Count - 1))
    End Function

    Private Shared Function CelleDellaRiga(cella As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As List(Of IUIAutomationElement)
        Dim ruolo As String = descrittore.RuoloAria(cella)
        If ruolo = "row" OrElse ruolo = "table" OrElse ruolo = "grid" Then Return FigliCella(cella, descrittore, automazione, cache)
        Dim genitore As IUIAutomationElement = ElementoPadre(cella, automazione, cache)
        If genitore Is Nothing Then
            Dim solo As New List(Of IUIAutomationElement)()
            solo.Add(cella)
            Return solo
        End If
        Dim ruoloGenitore As String = descrittore.RuoloAria(genitore)
        If ruoloGenitore = "row" OrElse ruoloGenitore = "table" OrElse ruoloGenitore = "grid" Then Return FigliCella(genitore, descrittore, automazione, cache)
        Return FigliCella(genitore, descrittore, automazione, cache)
    End Function

    Private Shared Function FigliCella(contenitore As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As List(Of IUIAutomationElement)
        Dim esito As New List(Of IUIAutomationElement)()
        Try
            Dim walker As IUIAutomationTreeWalker = AlberoVista(automazione)
            If walker Is Nothing OrElse contenitore Is Nothing Then Return esito
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(contenitore, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 40
                Dim ruolo As String = descrittore.RuoloAria(figlio)
                Dim tipo As Integer = descrittore.TipoControllo(figlio)
                If ruolo = "cell" OrElse ruolo = "gridcell" OrElse ruolo = "columnheader" OrElse ruolo = "rowheader" OrElse
                   tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoIntestazione OrElse tipo = DescrittoreElementi.ControlloTesto Then
                    esito.Add(figlio)
                End If
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.FigliCella", exAtteso)
        End Try
        Return esito
    End Function

    Private Shared Function Fratelli(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, automazione As IUIAutomation, cache As IUIAutomationCacheRequest, soloRighe As Boolean) As List(Of IUIAutomationElement)
        Dim esito As New List(Of IUIAutomationElement)()
        Try
            Dim walker As IUIAutomationTreeWalker = AlberoVista(automazione)
            Dim genitore As IUIAutomationElement = ElementoPadre(elemento, automazione, cache)
            If walker Is Nothing OrElse genitore Is Nothing Then Return esito
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(genitore, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 80
                Dim ruolo As String = descrittore.RuoloAria(figlio)
                If Not soloRighe OrElse ruolo = "row" OrElse ruolo = "" OrElse descrittore.TipoControllo(figlio) = DescrittoreElementi.ControlloElementoDati OrElse descrittore.TipoControllo(figlio) = DescrittoreElementi.ControlloGruppo Then
                    esito.Add(figlio)
                End If
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.Fratelli", exAtteso)
        End Try
        Return esito
    End Function

    Private Shared Function ElementoPadre(elemento As IUIAutomationElement, automazione As IUIAutomation, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            Dim walker As IUIAutomationTreeWalker = AlberoVista(automazione)
            If walker Is Nothing OrElse elemento Is Nothing Then Return Nothing
            Return walker.GetParentElementBuildCache(elemento, cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ElementoPadre", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function AlberoVista(automazione As IUIAutomation) As IUIAutomationTreeWalker
        Try
            If automazione Is Nothing Then Return Nothing
            Return automazione.ControlViewWalker
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.AlberoVista", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function IndiceDi(elenco As List(Of IUIAutomationElement), elemento As IUIAutomationElement, automazione As IUIAutomation) As Integer
        If elenco Is Nothing OrElse elemento Is Nothing Then Return -1
        For indice As Integer = 0 To elenco.Count - 1
            Try
                If automazione IsNot Nothing AndAlso automazione.CompareElements(elenco(indice), elemento) <> 0 Then Return indice
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaTabelle.IndiceDi", exAtteso)
            End Try
        Next
        Return -1
    End Function

    Private Shared Function ConCache(elemento As IUIAutomationElement, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse cache Is Nothing Then Return elemento
            Return elemento.BuildUpdatedCache(cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaTabelle.ConCache", exAtteso)
            Return elemento
        End Try
    End Function

    Private Shared Sub AccodaVoce(testo As StringBuilder, voce As String)
        If String.IsNullOrWhiteSpace(voce) Then Return
        If testo.Length > 0 Then testo.Append(", ")
        testo.Append(voce.Trim())
    End Sub

End Class