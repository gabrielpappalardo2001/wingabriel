Imports System.Globalization
Imports System.Text.RegularExpressions
Imports UIAutomationClient

' Lettura della struttura di documenti, fogli di calcolo e proprietà di stile o indirizzo.
Friend NotInheritable Class LetturaStrutturaDocumento

    Private Const SchemaTesto As Integer = 10014
    Private Const SchemaCella As Integer = 10007
    Private Const SchemaAccessibilita As Integer = 10018
    Private Const AttributoNomeStile As Integer = 40030
    Private Const AttributoIdStile As Integer = 40034
    Private Const TipoModifica As Integer = 50004
    Private Const TipoSchedaElemento As Integer = 50019
    Private Shared ReadOnly IndirizzoCella As New Regex("^[A-Za-z]{1,3}[0-9]{1,7}$", RegexOptions.Compiled)
    Private Shared _ultimoStile As String = ""
    Private Shared _chiaveStile As String = ""
    Private Shared _walker As IUIAutomationTreeWalker

    Private Sub New()
    End Sub

    Public Shared Function Suffisso(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As String
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return ""
            If descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaVoceFoglio) Then Return SuffissoCella(elemento)
            If Not descrittore.HaPatternTesto(elemento) Then Return ""
            Dim tipo As Integer = descrittore.TipoControllo(elemento)
            If tipo <> DescrittoreElementi.ControlloDocumento AndAlso tipo <> TipoModifica Then Return ""
            Return StileParagrafo(elemento, False)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Suffisso", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function LeggiDocumento(elemento As IUIAutomationElement, descrittore As DescrittoreElementi) As String
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return ""
            If descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaVoceFoglio) Then
                Dim cella As String = SuffissoCella(elemento)
                If cella <> "" Then Return cella
            ElseIf descrittore.HaPatternTesto(elemento) Then
                Dim testo As String = TestoDalCursore(elemento)
                Dim stile As String = StileParagrafo(elemento, True)
                If stile <> "" AndAlso testo <> "" Then Return stile & ", " & testo
                If testo <> "" Then Return testo
                If stile <> "" Then Return stile
            End If
            Dim msaa As String = AccessibilitaMsaa.DescriviElemento(elemento)
            If msaa.Length > 400 Then msaa = msaa.Substring(0, 400)
            Return msaa
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.LeggiDocumento", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function SuffissoCella(elemento As IUIAutomationElement) As String
        Dim cella As String = Indirizzo(elemento)
        Dim formulaCella As String = Formula(elemento)
        Dim foglio As String = NomeFoglio(elemento)
        Dim parti As String = ""
        If foglio <> "" Then parti = "foglio " & foglio
        If cella <> "" Then parti = Unisci(parti, "cella " & cella)
        Dim estesa As String = LetturaTabelle.EstensioneCella(elemento)
        If estesa <> "" Then parti = Unisci(parti, estesa)
        If formulaCella <> "" Then parti = Unisci(parti, "formula " & formulaCella)
        If cella <> "" AndAlso ValoreVisibile(elemento) = "" AndAlso formulaCella = "" Then parti = Unisci(parti, "vuota")
        Return parti
    End Function

    Private Shared Function StileParagrafo(elemento As IUIAutomationElement, forza As Boolean) As String
        Try
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return ""
            Dim selezione As IUIAutomationTextRangeArray = pattern.GetSelection()
            If selezione Is Nothing OrElse selezione.Length = 0 Then Return ""
            Dim intervallo As IUIAutomationTextRange = selezione.GetElement(0)
            Dim stile As String = NomeStile(intervallo)
            Dim chiave As String = ChiaveElemento(elemento)
            Dim nuovoElemento As Boolean = chiave <> _chiaveStile
            _chiaveStile = chiave
            If stile = "" Then
                _ultimoStile = ""
                Return ""
            End If
            If Not forza AndAlso Not nuovoElemento AndAlso String.Equals(stile, _ultimoStile, StringComparison.CurrentCultureIgnoreCase) AndAlso Not stile.StartsWith("titolo", StringComparison.CurrentCultureIgnoreCase) Then Return ""
            _ultimoStile = stile
            Return stile
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.StileParagrafo", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomeStile(intervallo As IUIAutomationTextRange) As String
        Try
            Dim identificativo As Object = Attributo(intervallo, AttributoIdStile)
            If identificativo Is Nothing Then identificativo = Attributo(intervallo, 40039)
            Dim numero As Integer
            If identificativo IsNot Nothing AndAlso Integer.TryParse(Convert.ToString(identificativo, CultureInfo.InvariantCulture), numero) Then
                If numero >= 70001 AndAlso numero <= 70009 Then Return "titolo " & (numero - 70000).ToString()
                If numero = 70010 Then Return "titolo"
                If numero = 70011 Then Return "sottotitolo"
                If numero = 70014 Then Return "citazione"
            End If
            Dim nome As Object = intervallo.GetAttributeValue(AttributoNomeStile)
            If TypeOf nome Is String Then
                Dim testo As String = CStr(nome).Trim()
                If testo = "" Then Return ""
                If testo.Equals("Normal", StringComparison.OrdinalIgnoreCase) OrElse testo.Equals("Normale", StringComparison.OrdinalIgnoreCase) Then Return ""
                If testo.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) Then
                    Dim cifra As String = testo.Substring(7).Trim()
                    If cifra <> "" Then Return "titolo " & cifra
                End If
                If testo.StartsWith("Titolo", StringComparison.OrdinalIgnoreCase) Then Return testo
                Return "stile " & testo
            End If
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.NomeStile", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Attributo(intervallo As IUIAutomationTextRange, id As Integer) As Object
        Try
            If intervallo Is Nothing Then Return Nothing
            Dim valore As Object = intervallo.GetAttributeValue(id)
            If valore Is Nothing Then Return Nothing
            Dim testo As String = Convert.ToString(valore)
            If testo Is Nothing Then Return Nothing
            If testo.IndexOf("NotSupported", StringComparison.OrdinalIgnoreCase) >= 0 Then Return Nothing
            If testo.IndexOf("MixedAttribute", StringComparison.OrdinalIgnoreCase) >= 0 Then Return Nothing
            Return valore
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Attributo", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function TestoDalCursore(elemento As IUIAutomationElement) As String
        Try
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return ""
            Dim selezione As IUIAutomationTextRangeArray = pattern.GetSelection()
            If selezione Is Nothing OrElse selezione.Length = 0 Then Return ""
            Dim intervallo As IUIAutomationTextRange = selezione.GetElement(0).Clone()
            intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Paragraph)
            Dim testo As String = intervallo.GetText(800)
            If testo Is Nothing Then Return ""
            testo = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If testo.Length > 500 Then testo = testo.Substring(0, 500)
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.TestoDalCursore", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Indirizzo(elemento As IUIAutomationElement) As String
        Dim candidato As String = TestoIndirizzo(NomeLegacy(elemento))
        If candidato = "" Then
            Try
                candidato = TestoIndirizzo(elemento.CurrentAutomationId)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaStrutturaDocumento.Indirizzo", exAtteso)
            End Try
        End If
        If candidato = "" Then candidato = TestoIndirizzo(DescrizioneLegacy(elemento))
        If candidato = "" Then candidato = TestoIndirizzo(AiutoLegacy(elemento))
        If candidato <> "" Then Return candidato.ToUpperInvariant()
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(elemento.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce Is Nothing Then Return ""
            Return ColonnaFoglio(voce.CurrentColumn) & (voce.CurrentRow + 1).ToString()
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Indirizzo", exAtteso2)
            Return ""
        End Try
    End Function

    Private Shared Function TestoIndirizzo(testo As String) As String
        If String.IsNullOrWhiteSpace(testo) Then Return ""
        Dim pulito As String = testo.Trim()
        If IndirizzoCella.IsMatch(pulito) Then Return pulito
        Dim pezzi As String() = pulito.Split(" "c)
        For Each pezzo As String In pezzi
            If IndirizzoCella.IsMatch(pezzo.Trim()) Then Return pezzo.Trim()
        Next
        Return ""
    End Function

    Private Shared Function ColonnaFoglio(indice As Integer) As String
        Dim numero As Integer = indice + 1
        Dim nome As String = ""
        While numero > 0
            numero -= 1
            nome = ChrW(AscW("A"c) + (numero Mod 26)) & nome
            numero \= 26
        End While
        Return nome
    End Function

    Private Shared Function Formula(elemento As IUIAutomationElement) As String
        Try
            Dim valore As Object = elemento.GetCurrentPropertyValue(30129)
            If Not TypeOf valore Is String Then Return ""
            Dim testo As String = CStr(valore).Trim()
            If testo = "" OrElse testo = "0" Then Return ""
            If Not testo.StartsWith("=") Then Return ""
            If testo.Length > 120 Then testo = testo.Substring(0, 120)
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Formula", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomeFoglio(elemento As IUIAutomationElement) As String
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(elemento.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce IsNot Nothing AndAlso voce.CurrentContainingGrid IsNot Nothing Then
                Dim nomeGriglia As String = voce.CurrentContainingGrid.CurrentName
                If Not String.IsNullOrWhiteSpace(nomeGriglia) AndAlso Not IndirizzoCella.IsMatch(nomeGriglia.Trim()) Then Return nomeGriglia.Trim()
            End If
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Exit For
                Dim tipo As Integer = 0
                Try
                    tipo = corrente.CurrentControlType
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("LetturaStrutturaDocumento.NomeFoglio", exAtteso)
                    tipo = 0
                End Try
                If tipo = TipoSchedaElemento Then
                    Dim nomeScheda As String = corrente.CurrentName
                    If Not String.IsNullOrWhiteSpace(nomeScheda) Then Return nomeScheda.Trim()
                End If
                corrente = Genitore(corrente)
            Next
            Return ""
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.NomeFoglio", exAtteso2)
            Return ""
        End Try
    End Function

    Private Shared Function ValoreVisibile(elemento As IUIAutomationElement) As String
        Try
            Dim valore As String = ""
            Try
                Dim ponte As IUIAutomationLegacyIAccessiblePattern = Legacy(elemento)
                If ponte IsNot Nothing AndAlso ponte.CurrentValue IsNot Nothing Then valore = ponte.CurrentValue.Trim()
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaStrutturaDocumento.ValoreVisibile", exAtteso)
                valore = ""
            End Try
            If valore = "" Then
                Dim nome As String = elemento.CurrentName
                If nome IsNot Nothing Then valore = nome.Trim()
            End If
            If IndirizzoCella.IsMatch(valore) Then Return ""
            Return valore
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.ValoreVisibile", exAtteso2)
            Return ""
        End Try
    End Function

    Private Shared Function Genitore(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            If _walker Is Nothing Then
                Dim automazione As New CUIAutomation()
                _walker = automazione.RawViewWalker
            End If
            If _walker Is Nothing Then Return Nothing
            Return _walker.GetParentElement(elemento)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Genitore", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function Legacy(elemento As IUIAutomationElement) As IUIAutomationLegacyIAccessiblePattern
        Try
            Return TryCast(elemento.GetCurrentPattern(SchemaAccessibilita), IUIAutomationLegacyIAccessiblePattern)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.Legacy", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function DescrizioneLegacy(elemento As IUIAutomationElement) As String
        Try
            Dim pattern As IUIAutomationLegacyIAccessiblePattern = Legacy(elemento)
            If pattern Is Nothing OrElse pattern.CurrentDescription Is Nothing Then Return ""
            Return pattern.CurrentDescription.Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.DescrizioneLegacy", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomeLegacy(elemento As IUIAutomationElement) As String
        Try
            Dim pattern As IUIAutomationLegacyIAccessiblePattern = Legacy(elemento)
            If pattern Is Nothing OrElse pattern.CurrentName Is Nothing Then Return ""
            Return pattern.CurrentName.Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.NomeLegacy", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function AiutoLegacy(elemento As IUIAutomationElement) As String
        Try
            Dim pattern As IUIAutomationLegacyIAccessiblePattern = Legacy(elemento)
            If pattern Is Nothing OrElse pattern.CurrentHelp Is Nothing Then Return ""
            Return pattern.CurrentHelp.Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.AiutoLegacy", exAtteso)
            Return ""
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
            RegistroErrori.Scrivi("LetturaStrutturaDocumento.ChiaveElemento", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Unisci(prima As String, dopo As String) As String
        If dopo = "" Then Return prima
        If prima = "" Then Return dopo
        Return prima & ", " & dopo
    End Function

End Class
