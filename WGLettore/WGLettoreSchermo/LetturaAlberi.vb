Imports System.Collections.Generic
Imports System.Diagnostics
Imports UIAutomationClient

' Lettura e navigazione degli elementi ad albero, gestione dei livelli e dello stato di espansione.
Friend NotInheritable Class LetturaAlberi

    Private Const SchemaSelezione As Integer = 10001
    Private Const SchemaEspansione As Integer = 10005

    Private Sub New()
    End Sub

    Public Shared Function NelAlbero(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Boolean
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return False
            If descrittore.EVoceAlbero(elemento) OrElse descrittore.EAlbero(elemento) OrElse AlberoNativo(descrittore, elemento) Then Return True
            If walker Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = walker.GetParentElementBuildCache(elemento, cache)
            For salto As Integer = 0 To 6
                If corrente Is Nothing Then Return False
                If descrittore.EVoceAlbero(corrente) OrElse descrittore.EAlbero(corrente) OrElse AlberoNativo(descrittore, corrente) Then Return True
                Dim tipo As Integer = descrittore.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloDocumento OrElse tipo = DescrittoreElementi.ControlloFinestra Then Return False
                corrente = walker.GetParentElementBuildCache(corrente, cache)
            Next
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.NelAlbero", exAtteso)
            Return False
        End Try
    End Function

    Public Shared Function ElementoDaAnnunciare(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, Optional preferisciFuoco As Boolean = False) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return elemento
            If descrittore.EVoceAlbero(elemento) Then Return elemento
            If Not descrittore.EAlbero(elemento) AndAlso Not AlberoNativo(descrittore, elemento) AndAlso Not EContenitoreDiVoci(descrittore, elemento) Then Return elemento
            If preferisciFuoco Then
                Dim conFuoco As IUIAutomationElement = FiglioConFuoco(elemento, descrittore, walker, cache, 300, 90)
                If conFuoco IsNot Nothing Then Return conFuoco
            End If
            Dim voce As IUIAutomationElement = VoceSelezionata(elemento, descrittore, walker, cache)
            If voce IsNot Nothing Then Return voce
            voce = FiglioConFuoco(elemento, descrittore, walker, cache)
            If voce IsNot Nothing Then Return voce
            Return elemento
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.ElementoDaAnnunciare", exAtteso)
            Return elemento
        End Try
    End Function

    Public Shared Property LivelloSoloSeCambia As Boolean = True
    Private Shared _ultimoLivello As Integer
    Private Shared _ultimoLivelloPid As Integer
    Private Shared _ultimoLivelloTick As Integer

    Public Shared Function Arricchisci(testo As String, elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, Optional completo As Boolean = False) As String
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing Then Return If(testo, "")
            Dim frase As String = If(testo, "")
            Dim voceAlbero As Boolean = descrittore.EVoceAlbero(elemento)
            If Not voceAlbero AndAlso Not (EVoceElenco(descrittore, elemento) AndAlso NelAlbero(elemento, descrittore, walker, cache)) Then Return frase
            Dim livelloTolto As Boolean = False
            If voceAlbero AndAlso LivelloSoloSeCambia Then frase = TogliLivelloUguale(frase, elemento, descrittore, completo, livelloTolto)
            Dim cartelle As Boolean = voceAlbero OrElse ContenitoreCartelle(elemento, descrittore, walker, cache)
            If cartelle AndAlso Not livelloTolto AndAlso frase.IndexOf("livello ", StringComparison.CurrentCultureIgnoreCase) < 0 Then
                Dim livello As Integer = descrittore.LivelloAlbero(elemento)
                If livello <= 0 Then livello = LivelloDaGenitori(elemento, descrittore, walker, cache)
                If livello > 0 Then frase = Accoda(frase, "livello " & livello.ToString())
            End If
            If cartelle AndAlso frase.IndexOf("aperto", StringComparison.CurrentCultureIgnoreCase) < 0 AndAlso frase.IndexOf("chiuso", StringComparison.CurrentCultureIgnoreCase) < 0 AndAlso frase.IndexOf("espanso", StringComparison.CurrentCultureIgnoreCase) < 0 AndAlso frase.IndexOf("compresso", StringComparison.CurrentCultureIgnoreCase) < 0 Then
                Dim stato As String = StatoEspansione(elemento)
                If stato <> "" Then frase = Accoda(frase, stato)
            End If
            If frase.IndexOf(" di ", StringComparison.Ordinal) < 0 Then
                Dim posizione As String = PosizioneFraFratelli(elemento, descrittore, walker, cache)
                If posizione <> "" Then frase = Accoda(frase, posizione)
            End If
            Return frase
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.Arricchisci", exAtteso)
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function TogliLivelloUguale(frase As String, elemento As IUIAutomationElement, descrittore As DescrittoreElementi, completo As Boolean, ByRef tolto As Boolean) As String
        tolto = False
        Try
            Dim livello As Integer = descrittore.LivelloAlbero(elemento)
            If livello <= 0 Then Return frase
            Dim pid As Integer = descrittore.IdProcesso(elemento)
            Dim adesso As Integer = Environment.TickCount
            Dim invariato As Boolean = Not completo AndAlso livello = _ultimoLivello AndAlso pid = _ultimoLivelloPid AndAlso adesso - _ultimoLivelloTick < 4000
            _ultimoLivello = livello
            _ultimoLivelloPid = pid
            _ultimoLivelloTick = adesso
            If Not invariato Then Return frase
            Dim pezzo As String = "livello " & livello.ToString()
            If frase.Contains(", " & pezzo) Then
                tolto = True
                Return frase.Replace(", " & pezzo, "")
            End If
            If frase.StartsWith(pezzo & ", ", StringComparison.Ordinal) Then
                tolto = True
                Return frase.Substring(pezzo.Length + 2)
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.TogliLivelloUguale", exAtteso)
        End Try
        Return frase
    End Function

    Friend Shared Function EContenitoreDiVoci(descrittore As DescrittoreElementi, elemento As IUIAutomationElement) As Boolean
        Try
            Dim tipo As Integer = descrittore.TipoControllo(elemento)
            If tipo = DescrittoreElementi.ControlloElenco OrElse tipo = DescrittoreElementi.ControlloGrigliaDati OrElse tipo = DescrittoreElementi.ControlloTabella Then Return True
            Select Case descrittore.RuoloAria(elemento)
                Case "listbox", "list", "grid", "treegrid"
                    Return True
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.EContenitoreDiVoci", exAtteso)
        End Try
        Return False
    End Function

    Private Shared Function FiglioConFuoco(contenitore As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, Optional massimoFigli As Integer = 80, Optional massimoMs As Integer = 60) As IUIAutomationElement
        If walker Is Nothing Then Return Nothing
        Try
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(contenitore, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < massimoFigli AndAlso orologio.ElapsedMilliseconds < massimoMs
                Try
                    If figlio.CurrentHasKeyboardFocus <> 0 Then Return figlio
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("LetturaAlberi.FiglioConFuoco", exAtteso)
                End Try
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaAlberi.FiglioConFuoco", exAtteso2)
        End Try
        Return Nothing
    End Function

    Private Shared Function VoceSelezionata(albero As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            Dim selezione As IUIAutomationSelectionPattern = TryCast(albero.GetCurrentPattern(SchemaSelezione), IUIAutomationSelectionPattern)
            If selezione IsNot Nothing Then
                Dim scelte As IUIAutomationElementArray = selezione.GetCurrentSelection()
                If scelte IsNot Nothing AndAlso scelte.Length > 0 Then
                    Dim prima As IUIAutomationElement = scelte.GetElement(0)
                    If prima IsNot Nothing AndAlso descrittore.Nome(prima) <> "" Then Return prima
                End If
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.VoceSelezionata", exAtteso)
        End Try
        If walker Is Nothing Then Return Nothing
        Try
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(albero, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 80 AndAlso orologio.ElapsedMilliseconds < 50
                If (descrittore.EVoceAlbero(figlio) OrElse EVoceElenco(descrittore, figlio)) AndAlso descrittore.Nome(figlio) <> "" AndAlso descrittore.LeggiBooleano(figlio, DescrittoreElementi.ProprietaSelezionato) Then Return figlio
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaAlberi.VoceSelezionata", exAtteso2)
        End Try
        Return Nothing
    End Function

    Private Shared Function LivelloDaGenitori(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Integer
        If walker Is Nothing Then Return 0
        Dim livello As Integer = 1
        Try
            Dim corrente As IUIAutomationElement = walker.GetParentElementBuildCache(elemento, cache)
            For salto As Integer = 0 To 16
                If corrente Is Nothing Then Exit For
                If descrittore.EAlbero(corrente) OrElse AlberoNativo(descrittore, corrente) Then Exit For
                If descrittore.EVoceAlbero(corrente) OrElse EVoceElenco(descrittore, corrente) Then livello += 1
                corrente = walker.GetParentElementBuildCache(corrente, cache)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.LivelloDaGenitori", exAtteso)
            Return 0
        End Try
        Return livello
    End Function

    Private Shared Function StatoEspansione(elemento As IUIAutomationElement) As String
        Try
            Dim espansione As IUIAutomationExpandCollapsePattern = TryCast(elemento.GetCurrentPattern(SchemaEspansione), IUIAutomationExpandCollapsePattern)
            If espansione Is Nothing Then Return ""
            Select Case CInt(espansione.CurrentExpandCollapseState)
                Case 0 : Return "chiuso"
                Case 1 : Return "aperto"
                Case 2 : Return "parzialmente aperto"
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.StatoEspansione", exAtteso)
        End Try
        Return ""
    End Function

    Private NotInheritable Class FratelliNoti
        Public Chiavi As New List(Of String)()
        Public Completo As Boolean
        Public Tick As Integer
    End Class

    Private Shared ReadOnly _fratelli As New Dictionary(Of String, FratelliNoti)(StringComparer.Ordinal)
    Private Shared ReadOnly _bloccoFratelli As New Object()

    Private Shared Function ChiaveUia(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.ChiaveUia", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function PosizioneFraFratelli(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As String
        Try
            Dim posizioneUia As Integer = descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            Dim totaleUia As Integer = descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
            If posizioneUia > 0 AndAlso totaleUia > 1 AndAlso posizioneUia <= totaleUia Then Return posizioneUia.ToString() & " di " & totaleUia.ToString()
            If walker Is Nothing Then Return ""
            Dim genitore As IUIAutomationElement = walker.GetParentElementBuildCache(elemento, cache)
            If genitore Is Nothing Then Return ""
            Dim chiaveGenitore As String = ChiaveUia(genitore)
            Dim chiaveElemento As String = ChiaveUia(elemento)
            If chiaveGenitore = "" OrElse chiaveElemento = "" Then Return ""
            Dim adesso As Integer = Environment.TickCount
            Dim noto As FratelliNoti = Nothing
            SyncLock _bloccoFratelli
                _fratelli.TryGetValue(chiaveGenitore, noto)
            End SyncLock
            Dim indice As Integer = -1
            If noto IsNot Nothing AndAlso adesso - noto.Tick < 4000 Then indice = noto.Chiavi.IndexOf(chiaveElemento)
            Dim incompletoRecente As Boolean = noto IsNot Nothing AndAlso Not noto.Completo AndAlso adesso - noto.Tick < 30000
            If indice < 0 AndAlso Not incompletoRecente AndAlso (noto Is Nothing OrElse adesso - noto.Tick >= 500) Then
                noto = LeggiFratelli(genitore, descrittore, walker, cache)
                SyncLock _bloccoFratelli
                    If _fratelli.Count > 30 Then _fratelli.Clear()
                    _fratelli(chiaveGenitore) = noto
                End SyncLock
                indice = noto.Chiavi.IndexOf(chiaveElemento)
            End If
            If indice >= 0 AndAlso noto IsNot Nothing AndAlso noto.Completo AndAlso noto.Chiavi.Count > 1 Then Return (indice + 1).ToString() & " di " & noto.Chiavi.Count.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.PosizioneFraFratelli", exAtteso)
        End Try
        Return ""
    End Function

    Private Shared Function LeggiFratelli(genitore As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As FratelliNoti
        Dim esito As New FratelliNoti()
        esito.Tick = Environment.TickCount
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(genitore, cache)
        Dim giri As Integer = 0
        esito.Completo = True
        While figlio IsNot Nothing
            If giri >= 5000 OrElse orologio.ElapsedMilliseconds >= 350 Then
                esito.Completo = False
                Exit While
            End If
            If (descrittore.EVoceAlbero(figlio) OrElse EVoceElenco(descrittore, figlio)) AndAlso descrittore.Nome(figlio) <> "" Then esito.Chiavi.Add(ChiaveUia(figlio))
            figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
            giri += 1
        End While
        Return esito
    End Function

    Private Shared Function EVoceElenco(descrittore As DescrittoreElementi, elemento As IUIAutomationElement) As Boolean
        Try
            Dim tipo As Integer = descrittore.TipoControllo(elemento)
            If tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoAlbero Then Return True
            Dim ruolo As String = descrittore.RuoloAria(elemento)
            Return ruolo = "listitem" OrElse ruolo = "row" OrElse ruolo = "treeitem" OrElse ruolo = "outlineitem" OrElse ruolo = "option"
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.EVoceElenco", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function ContenitoreCartelle(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Boolean
        If walker Is Nothing Then Return False
        Try
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Return False
                Dim classe As String = descrittore.LeggiStringa(corrente, DescrittoreElementi.ProprietaNomeClasse)
                If classe.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase) Then Return True
                If descrittore.EAlbero(corrente) Then Return Not descrittore.RuoloAria(corrente).Equals("treegrid", StringComparison.OrdinalIgnoreCase)
                Dim tipo As Integer = descrittore.TipoControllo(corrente)
                If tipo = DescrittoreElementi.ControlloDocumento OrElse tipo = DescrittoreElementi.ControlloFinestra Then Return False
                corrente = walker.GetParentElementBuildCache(corrente, cache)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.ContenitoreCartelle", exAtteso)
        End Try
        Return False
    End Function

    Private Shared Function AlberoNativo(descrittore As DescrittoreElementi, elemento As IUIAutomationElement) As Boolean
        Try
            Dim classe As String = descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse)
            Return classe.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.AlberoNativo", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function Accoda(frase As String, pezzo As String) As String
        If pezzo = "" Then Return frase
        If frase = "" Then Return pezzo
        Return frase & ", " & pezzo
    End Function

    Private Shared Function Stesso(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            Dim a As Array = primo.GetRuntimeId()
            Dim b As Array = secondo.GetRuntimeId()
            If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return False
            For indice As Integer = 0 To a.Length - 1
                If Not Object.Equals(a.GetValue(indice), b.GetValue(indice)) Then Return False
            Next
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaAlberi.Stesso", exAtteso)
            Return False
        End Try
    End Function

End Class
