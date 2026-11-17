Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports UIAutomationClient

' Estrazione e lettura di righe, celle e intestazioni all'interno di tabelle ed elenchi.
Friend NotInheritable Class LetturaRigheElenco

    Private Const SchemaTabella As Integer = 10012
    Private Const SchemaVoceTabella As Integer = 10013
    Private Const SchemaCella As Integer = 10007
    Private Const MassimoCelle As Integer = 24
    Private Const MassimoTesto As Integer = 120
    Private Const BudgetMs As Integer = 260
    Private Const ValiditaIntestazioniMs As Integer = 20000

    Private Shared ReadOnly _memoria As New Dictionary(Of String, MemoriaIntestazioni)(StringComparer.Ordinal)
    Private Shared ReadOnly _blocco As New Object()
    <ThreadStatic>
    Private Shared _budgetContenuto As Integer

    Private NotInheritable Class MemoriaIntestazioni
        Public Nomi As List(Of String)
        Public Tick As Integer
    End Class

    Private Sub New()
    End Sub

    Public Shared Function Descrivi(riga As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, soloGrigliaAria As Boolean, ByRef sostituisceNome As Boolean) As String
        sostituisceNome = False
        Try
            If riga Is Nothing OrElse descrittore Is Nothing OrElse walker Is Nothing Then Return ""
            If Not PuoEssereRiga(riga, descrittore) Then Return ""
            Dim ruolo As String = descrittore.RuoloAria(riga)
            If soloGrigliaAria AndAlso ruolo <> "row" Then Return ""
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim celle As List(Of IUIAutomationElement) = CelleDiRiga(riga, descrittore, walker, cache, orologio)
            If celle.Count < 2 Then Return ""
            Dim nomeRiga As String = descrittore.Nome(riga)
            Dim intestazioni As List(Of String) = IntestazioniDi(riga, celle, descrittore, walker, cache)
            Dim voci As New List(Of String)()
            Dim valori As New List(Of String)()
            Dim contenuti As Integer = 0
            For indice As Integer = 0 To celle.Count - 1
                Dim cella As IUIAutomationElement = celle(indice)
                Dim valore As String = TestoCella(cella, descrittore, walker, cache, orologio)
                If valore = "" Then Continue For
                contenuti += 1
                valori.Add(valore)
                Dim colonna As Integer = IndiceColonna(cella, indice)
                Dim intestazione As String = ""
                If intestazioni IsNot Nothing AndAlso colonna >= 0 AndAlso colonna < intestazioni.Count Then intestazione = intestazioni(colonna)
                If intestazione = "" OrElse String.Equals(intestazione, valore, StringComparison.CurrentCultureIgnoreCase) OrElse valore.StartsWith(intestazione & " ", StringComparison.CurrentCultureIgnoreCase) Then
                    voci.Add(valore)
                Else
                    voci.Add(intestazione & ": " & valore)
                End If
            Next
            If contenuti = 0 Then Return ""
            If nomeRiga <> "" Then
                Dim contenute As Integer = 0
                For Each valore As String In valori
                    If nomeRiga.IndexOf(valore, StringComparison.CurrentCultureIgnoreCase) >= 0 Then contenute += 1
                Next
                sostituisceNome = contenute * 2 >= valori.Count AndAlso valori.Count >= 2
                If Not sostituisceNome AndAlso valori.Count > 0 AndAlso String.Equals(valori(0), nomeRiga, StringComparison.CurrentCultureIgnoreCase) Then voci.RemoveAt(0)
            End If
            Dim livello As Integer = descrittore.LeggiIntero(riga, DescrittoreElementi.ProprietaLivello, 0)
            If livello > 1 Then voci.Add("livello " & livello.ToString())
            Return String.Join(", ", voci)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.Descrivi", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function PuoEssereRiga(riga As IUIAutomationElement, descrittore As DescrittoreElementi) As Boolean
        Select Case descrittore.TipoControllo(riga)
            Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloElementoAlbero
                Return True
        End Select
        Dim ruolo As String = descrittore.RuoloAria(riga)
        Return ruolo = "row" OrElse ruolo = "treeitem" OrElse ruolo = "listitem"
    End Function

    Private Shared Function CelleDiRiga(riga As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch) As List(Of IUIAutomationElement)
        Dim celle As New List(Of IUIAutomationElement)()
        Try
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(riga, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < MassimoCelle * 2 AndAlso celle.Count < MassimoCelle AndAlso orologio.ElapsedMilliseconds < BudgetMs
                Dim tipo As Integer = descrittore.TipoControllo(figlio)
                If tipo <> DescrittoreElementi.ControlloBarraScorrimento AndAlso tipo <> DescrittoreElementi.ControlloSeparatore AndAlso Not descrittore.FuoriSchermo(figlio) Then
                    celle.Add(figlio)
                End If
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.CelleDiRiga", exAtteso)
        End Try
        Return celle
    End Function

    Private Shared Function IndiceColonna(cella As IUIAutomationElement, ordinale As Integer) As Integer
        Try
            Dim voce As IUIAutomationGridItemPattern = TryCast(cella.GetCurrentPattern(SchemaCella), IUIAutomationGridItemPattern)
            If voce IsNot Nothing Then
                Dim colonna As Integer = voce.CurrentColumn
                If colonna >= 0 Then Return colonna
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.IndiceColonna", exAtteso)
        End Try
        Return ordinale
    End Function

    Private Shared Function TestoCella(cella As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch) As String
        Try
            Dim testo As String = descrittore.Nome(cella)
            If testo = "" Then testo = descrittore.LeggiStringa(cella, DescrittoreElementi.ProprietaValore)
            If testo = "" AndAlso orologio.ElapsedMilliseconds < BudgetMs Then testo = TestoFigli(cella, descrittore, walker, cache, 2, orologio)
            If testo = "" Then
                If descrittore.LeggiBooleano(cella, DescrittoreElementi.ProprietaAttivazioneDisponibile) AndAlso descrittore.LeggiIntero(cella, DescrittoreElementi.ProprietaStatoAttivazione, 0) = 1 Then testo = "spuntato"
            End If
            testo = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If testo.Length > MassimoTesto Then testo = testo.Substring(0, MassimoTesto)
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.TestoCella", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function Contenuto(voce As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, soloGrigliaAria As Boolean) As String
        Try
            Dim sostituisce As Boolean = False
            Dim dalleCelle As String = Descrivi(voce, descrittore, walker, cache, soloGrigliaAria, sostituisce)
            If dalleCelle <> "" Then Return dalleCelle
            Return TestoVoce(voce, descrittore, walker, cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.Contenuto", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function TestoVoce(voce As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, Optional budgetMs As Integer = 120) As String
        Try
            If voce Is Nothing OrElse descrittore Is Nothing OrElse walker Is Nothing Then Return ""
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim parti As New List(Of String)()
            _budgetContenuto = budgetMs
            RaccogliContenuto(voce, descrittore, walker, cache, 4, orologio, parti)
            Dim testo As String = String.Join(", ", parti).Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If testo.Length > 400 Then testo = testo.Substring(0, 400)
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.TestoVoce", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Sub RaccogliContenuto(nodo As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, profondita As Integer, orologio As Stopwatch, parti As List(Of String))
        If profondita <= 0 OrElse orologio.ElapsedMilliseconds >= _budgetContenuto OrElse parti.Count >= 12 Then Return
        Dim figlio As IUIAutomationElement = If(cache Is Nothing, walker.GetFirstChildElement(nodo), walker.GetFirstChildElementBuildCache(nodo, cache))
        Dim giri As Integer = 0
        While figlio IsNot Nothing AndAlso giri < 24 AndAlso orologio.ElapsedMilliseconds < _budgetContenuto AndAlso parti.Count < 12
            giri += 1
            Dim tipo As Integer = descrittore.TipoControllo(figlio)
            Dim ruolo As String = descrittore.RuoloAria(figlio)
            Dim azione As Boolean = tipo = DescrittoreElementi.ControlloPulsante OrElse tipo = DescrittoreElementi.ControlloBarraStrumenti OrElse
                                    tipo = DescrittoreElementi.ControlloMenu OrElse tipo = DescrittoreElementi.ControlloVoceMenu OrElse
                                    ruolo = "button" OrElse ruolo = "toolbar" OrElse ruolo = "menu" OrElse ruolo = "menuitem" OrElse ruolo = "tooltip"
            If Not azione AndAlso DescrittoreElementi.PrimoPianoTeams() AndAlso (ruolo = "heading" OrElse descrittore.LivelloTitolo(figlio) > 0) Then azione = True
            If Not azione AndAlso Not descrittore.FuoriSchermo(figlio) Then
                Dim nome As String = descrittore.Nome(figlio)
                If nome <> "" AndAlso tipo <> DescrittoreElementi.ControlloGruppo AndAlso tipo <> DescrittoreElementi.ControlloRiquadro AndAlso tipo <> DescrittoreElementi.ControlloPersonalizzato Then
                    If Not parti.Contains(nome) Then parti.Add(nome)
                Else
                    RaccogliContenuto(figlio, descrittore, walker, cache, profondita - 1, orologio, parti)
                End If
            End If
            figlio = If(cache Is Nothing, walker.GetNextSiblingElement(figlio), walker.GetNextSiblingElementBuildCache(figlio, cache))
        End While
    End Sub

    Private Shared Function TestoFigli(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, profondita As Integer, orologio As Stopwatch) As String
        Try
            If profondita <= 0 OrElse orologio.ElapsedMilliseconds >= BudgetMs Then Return ""
            Dim parti As New List(Of String)()
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(elemento, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 8 AndAlso orologio.ElapsedMilliseconds < BudgetMs
                Dim nome As String = descrittore.Nome(figlio)
                If nome = "" Then nome = descrittore.LeggiStringa(figlio, DescrittoreElementi.ProprietaValore)
                If nome = "" Then nome = TestoFigli(figlio, descrittore, walker, cache, profondita - 1, orologio)
                If nome <> "" AndAlso Not parti.Contains(nome) Then parti.Add(nome)
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
            Return String.Join(" ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.TestoFigli", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function IntestazioniDi(riga As IUIAutomationElement, celle As List(Of IUIAutomationElement), descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As List(Of String)
        Try
            Dim contenitore As IUIAutomationElement = TrovaContenitore(riga, descrittore, walker, cache)
            Dim chiave As String = ""
            If contenitore IsNot Nothing Then chiave = ChiaveDi(contenitore)
            Dim adesso As Integer = Environment.TickCount
            If chiave <> "" Then
                SyncLock _blocco
                    Dim noto As MemoriaIntestazioni = Nothing
                    If _memoria.TryGetValue(chiave, noto) AndAlso adesso - noto.Tick < ValiditaIntestazioniMs Then Return noto.Nomi
                End SyncLock
            End If
            Dim nomi As List(Of String) = Nothing
            If contenitore IsNot Nothing Then
                nomi = DaPatternTabella(contenitore, descrittore)
                If nomi Is Nothing OrElse nomi.Count = 0 Then nomi = DaRigaIntestazioni(contenitore, descrittore, walker, cache)
            End If
            If nomi Is Nothing OrElse nomi.Count = 0 Then nomi = DaVociTabella(celle, descrittore)
            If chiave <> "" AndAlso nomi IsNot Nothing AndAlso nomi.Count > 0 Then
                SyncLock _blocco
                    If _memoria.Count > 40 Then _memoria.Clear()
                    Dim nuova As New MemoriaIntestazioni()
                    nuova.Nomi = nomi
                    nuova.Tick = adesso
                    _memoria(chiave) = nuova
                End SyncLock
            End If
            Return nomi
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.IntestazioniDi", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function TrovaContenitore(riga As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            Dim corrente As IUIAutomationElement = walker.GetParentElementBuildCache(riga, cache)
            For salto As Integer = 0 To 3
                If corrente Is Nothing Then Return Nothing
                Dim tipo As Integer = descrittore.TipoControllo(corrente)
                Dim ruolo As String = descrittore.RuoloAria(corrente)
                If tipo = DescrittoreElementi.ControlloElenco OrElse tipo = DescrittoreElementi.ControlloTabella OrElse tipo = DescrittoreElementi.ControlloGrigliaDati OrElse tipo = DescrittoreElementi.ControlloAlbero OrElse
                   ruolo = "grid" OrElse ruolo = "treegrid" OrElse ruolo = "table" OrElse ruolo = "list" OrElse ruolo = "listbox" OrElse ruolo = "tree" Then Return corrente
                If tipo = DescrittoreElementi.ControlloDocumento OrElse tipo = DescrittoreElementi.ControlloFinestra Then Return Nothing
                corrente = walker.GetParentElementBuildCache(corrente, cache)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.TrovaContenitore", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Shared Function DaPatternTabella(contenitore As IUIAutomationElement, descrittore As DescrittoreElementi) As List(Of String)
        Try
            Dim tabella As IUIAutomationTablePattern = TryCast(contenitore.GetCurrentPattern(SchemaTabella), IUIAutomationTablePattern)
            If tabella Is Nothing Then Return Nothing
            Dim elenco As IUIAutomationElementArray = tabella.GetCurrentColumnHeaders()
            If elenco Is Nothing OrElse elenco.Length = 0 Then Return Nothing
            Dim nomi As New List(Of String)()
            For indice As Integer = 0 To Math.Min(elenco.Length, MassimoCelle) - 1
                nomi.Add(descrittore.NomeCorrente(elenco.GetElement(indice)))
            Next
            Return nomi
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.DaPatternTabella", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function DaRigaIntestazioni(contenitore As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As List(Of String)
        Try
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(contenitore, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 8
                Dim tipo As Integer = descrittore.TipoControllo(figlio)
                Dim ruolo As String = descrittore.RuoloAria(figlio)
                If tipo = DescrittoreElementi.ControlloIntestazione OrElse ruolo = "row" Then
                    Dim nomi As List(Of String) = NomiDei(figlio, descrittore, walker, cache)
                    If nomi.Count > 0 Then Return nomi
                End If
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.DaRigaIntestazioni", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Shared Function NomiDei(contenitoreIntestazioni As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As List(Of String)
        Dim nomi As New List(Of String)()
        Try
            Dim intestazioniVere As Integer = 0
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(contenitoreIntestazioni, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < MassimoCelle
                Dim tipo As Integer = descrittore.TipoControllo(figlio)
                Dim ruolo As String = descrittore.RuoloAria(figlio)
                If tipo = DescrittoreElementi.ControlloElementoIntestazione OrElse ruolo = "columnheader" OrElse ruolo = "rowheader" Then intestazioniVere += 1
                nomi.Add(descrittore.Nome(figlio))
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
            If intestazioniVere = 0 Then nomi.Clear()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.NomiDei", exAtteso)
            nomi.Clear()
        End Try
        Return nomi
    End Function

    Private Shared Function DaVociTabella(celle As List(Of IUIAutomationElement), descrittore As DescrittoreElementi) As List(Of String)
        Try
            Dim nomi As New List(Of String)()
            Dim trovata As Boolean = False
            For Each cella As IUIAutomationElement In celle
                Dim nome As String = ""
                Try
                    Dim voce As IUIAutomationTableItemPattern = TryCast(cella.GetCurrentPattern(SchemaVoceTabella), IUIAutomationTableItemPattern)
                    If voce IsNot Nothing Then
                        Dim elenco As IUIAutomationElementArray = voce.GetCurrentColumnHeaderItems()
                        If elenco IsNot Nothing AndAlso elenco.Length > 0 Then
                            nome = descrittore.NomeCorrente(elenco.GetElement(0))
                            If nome <> "" Then trovata = True
                        End If
                    End If
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("LetturaRigheElenco.DaVociTabella", exAtteso)
                End Try
                nomi.Add(nome)
            Next
            If trovata Then Return nomi
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.DaVociTabella", exAtteso2)
        End Try
        Return Nothing
    End Function

    Private Shared Function ChiaveDi(elemento As IUIAutomationElement) As String
        Try
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim testo As New StringBuilder()
            For indice As Integer = 0 To id.Length - 1
                If indice > 0 Then testo.Append("-"c)
                testo.Append(Convert.ToString(id.GetValue(indice)))
            Next
            Return testo.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRigheElenco.ChiaveDi", exAtteso)
            Return ""
        End Try
    End Function

End Class
