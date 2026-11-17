Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports System.Text.RegularExpressions
Imports UIAutomationClient

' Ricerca e lettura dei riquadri collegati, aree di documento e contesti associati.
Friend NotInheritable Class LetturaRiquadroCollegato

    Private Const SchemaTesto As Integer = 10014
    Private Const SchemaTesto2 As Integer = 10024
    Private Const BudgetMs As Integer = 380
    Private Const DurataTrovato As Integer = 5000
    Private Const DurataAssente As Integer = 20000

    Private Shared ReadOnly Spazi As New Regex("\s+", RegexOptions.Compiled)

    Private NotInheritable Class RiquadroNoto
        Public Documento As IUIAutomationElement
        Public InElenco As Boolean
        Public Tick As Integer
    End Class

    Private Shared ReadOnly _noti As New Dictionary(Of String, RiquadroNoto)(StringComparer.Ordinal)

    Private Enum Zona
        Sconosciuta
        Elenco
        Documento
    End Enum

    Private Sub New()
    End Sub

    Public Shared Function InRiquadro(elemento As IUIAutomationElement, automazione As IUIAutomation, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Boolean
        Dim inElenco As Boolean = False
        Dim trovato As IUIAutomationElement = Trova(elemento, automazione, descrittore, walker, cache, inElenco)
        Return trovato IsNot Nothing AndAlso Not inElenco
    End Function

    Public Shared Function ConRiquadroCollegato(elemento As IUIAutomationElement, automazione As IUIAutomation, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Boolean
        Dim inElenco As Boolean = False
        Dim trovato As IUIAutomationElement = Trova(elemento, automazione, descrittore, walker, cache, inElenco)
        Return trovato IsNot Nothing AndAlso inElenco
    End Function

    Public Shared Function Riquadro(elemento As IUIAutomationElement, automazione As IUIAutomation, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Dim inElenco As Boolean = False
        Return Trova(elemento, automazione, descrittore, walker, cache, inElenco)
    End Function

    Public Shared Function TestoDelRiquadro(elemento As IUIAutomationElement, automazione As IUIAutomation, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, massimo As Integer) As String
        Try
            If elemento Is Nothing OrElse massimo < 1 Then Return ""
            Dim doc As IUIAutomationElement = Riquadro(elemento, automazione, descrittore, walker, cache)
            If doc Is Nothing Then Return ""
            Dim dalPattern As String = TestoPatternSicuro(doc, massimo)
            If dalPattern Is Nothing Then dalPattern = ""
            If dalPattern.Length >= 40 Then Return Compatta(dalPattern, massimo)
            Dim daiFigli As String = TestoFigliSicuro(doc, descrittore, walker, cache, massimo)
            If daiFigli IsNot Nothing AndAlso daiFigli.Length > dalPattern.Length Then Return Compatta(daiFigli, massimo)
            If dalPattern <> "" Then Return Compatta(dalPattern, massimo)
            Return Compatta(AccessibilitaMsaa.DescriviElemento(doc), massimo)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.TestoDelRiquadro", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function Anteprima(testo As String) As String
        Dim pulito As String = Compatta(testo, 700)
        If pulito.Length <= 420 Then Return pulito
        Dim taglio As Integer = pulito.LastIndexOfAny(New Char() {"."c, "!"c, "?"c}, 400)
        If taglio < 80 Then taglio = 400
        Return pulito.Substring(0, taglio + 1).Trim()
    End Function

    Private Shared Function Trova(elemento As IUIAutomationElement, automazione As IUIAutomation, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, ByRef inElenco As Boolean) As IUIAutomationElement
        inElenco = False
        Try
            If elemento Is Nothing OrElse descrittore Is Nothing OrElse walker Is Nothing Then Return Nothing
            Dim ancora As IUIAutomationElement = Nothing
            Dim zona As Zona = Zona.Sconosciuta
            zona = ZonaSicura(elemento, descrittore, walker, cache, ancora)
            If zona = Zona.Sconosciuta OrElse ancora Is Nothing Then Return Nothing
            Dim chiave As String = ChiaveElemento(ancora)
            If chiave = "" Then Return Nothing
            Dim voce As String = zona.ToString() & ":" & chiave
            Dim adesso As Integer = Environment.TickCount
            Dim noto As RiquadroNoto = Nothing
            SyncLock _noti
                _noti.TryGetValue(voce, noto)
            End SyncLock
            If noto IsNot Nothing Then
                If noto.Documento Is Nothing Then
                    If adesso - noto.Tick < DurataAssente Then Return Nothing
                ElseIf adesso - noto.Tick < DurataTrovato Then
                    inElenco = noto.InElenco
                    Return noto.Documento
                End If
            End If
            Dim zonaDaCercare As Zona = zona
            Dim trovato As IUIAutomationElement = CercaSicuro(zonaDaCercare, ancora, descrittore, walker, cache)
            SyncLock _noti
                If _noti.Count > 40 Then _noti.Clear()
                Dim nuovo As New RiquadroNoto()
                nuovo.Documento = trovato
                nuovo.InElenco = zona = Zona.Elenco
                nuovo.Tick = adesso
                _noti(voce) = nuovo
            End SyncLock
            inElenco = zona = Zona.Elenco
            Return trovato
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.Trova", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function ZonaDi(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch, ByRef ancora As IUIAutomationElement) As Zona
        ancora = Nothing
        Dim corrente As IUIAutomationElement = elemento
        For salto As Integer = 0 To 18
            If TempoFinito(orologio) OrElse corrente Is Nothing Then Exit For
            Dim tipo As Integer = descrittore.TipoControllo(corrente)
            Dim ruolo As String = descrittore.RuoloAria(corrente)
            If EDocumento(tipo, ruolo) Then
                ancora = corrente
                Return Zona.Documento
            End If
            If EContenitoreElenco(tipo, ruolo) OrElse descrittore.ContenitoreComposito(corrente) Then
                ancora = corrente
                Return Zona.Elenco
            End If
            corrente = Genitore(walker, corrente, cache)
        Next
        Return Zona.Sconosciuta
    End Function

    Private Shared Function Cerca(zona As Zona, ancora As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        If zona = Zona.Documento Then
            If Not HaDocumentoAntenato(ancora, descrittore, walker, cache, orologio) Then Return Nothing
            If Not HaElencoAccanto(ancora, descrittore, walker, cache, orologio) Then Return Nothing
            Return ancora
        End If
        Dim padre As IUIAutomationElement = Genitore(walker, ancora, cache)
        If padre Is Nothing Then Return Nothing
        Return CercaDocumento(padre, descrittore, walker, cache, orologio)
    End Function

    Private Shared Function HaDocumentoAntenato(doc As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch) As Boolean
        Dim corrente As IUIAutomationElement = Genitore(walker, doc, cache)
        For salto As Integer = 0 To 14
            If TempoFinito(orologio) OrElse corrente Is Nothing Then Return False
            If EDocumento(descrittore.TipoControllo(corrente), descrittore.RuoloAria(corrente)) Then Return True
            corrente = Genitore(walker, corrente, cache)
        Next
        Return False
    End Function

    Private Shared Function HaElencoAccanto(doc As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch) As Boolean
        Dim ramo As IUIAutomationElement = doc
        Dim padre As IUIAutomationElement = Genitore(walker, doc, cache)
        Dim visitati As Integer = 0
        For salto As Integer = 0 To 6
            If TempoFinito(orologio) OrElse padre Is Nothing Then Return False
            Dim chiaveRamo As String = ChiaveElemento(ramo)
            Dim figlio As IUIAutomationElement = PrimoFiglio(walker, padre, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 24 AndAlso Not TempoFinito(orologio)
                giri += 1
                If ChiaveElemento(figlio) <> chiaveRamo Then
                    If ContieneElenco(figlio, descrittore, walker, cache, orologio, 3, visitati) Then Return True
                End If
                figlio = Fratello(walker, figlio, cache)
            End While
            ramo = padre
            padre = Genitore(walker, padre, cache)
        Next
        Return False
    End Function

    Private Shared Function ContieneElenco(nodo As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch, profondita As Integer, ByRef visitati As Integer) As Boolean
        If nodo Is Nothing OrElse profondita < 0 OrElse visitati > 90 OrElse TempoFinito(orologio) Then Return False
        visitati += 1
        Dim tipo As Integer = descrittore.TipoControllo(nodo)
        Dim ruolo As String = descrittore.RuoloAria(nodo)
        If EContenitoreElenco(tipo, ruolo) OrElse descrittore.ContenitoreComposito(nodo) Then Return True
        If EChromeDaSaltare(tipo) Then Return False
        Dim figlio As IUIAutomationElement = PrimoFiglio(walker, nodo, cache)
        Dim giri As Integer = 0
        While figlio IsNot Nothing AndAlso giri < 12
            If ContieneElenco(figlio, descrittore, walker, cache, orologio, profondita - 1, visitati) Then Return True
            figlio = Fratello(walker, figlio, cache)
            giri += 1
        End While
        Return False
    End Function

    Private Shared Function CercaDocumento(partenza As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, orologio As Stopwatch) As IUIAutomationElement
        If partenza Is Nothing Then Return Nothing
        Dim pila As New Stack(Of IUIAutomationElement)()
        Dim profondita As New Stack(Of Integer)()
        pila.Push(partenza)
        profondita.Push(0)
        Dim visitati As Integer = 0
        While pila.Count > 0 AndAlso visitati < 70 AndAlso Not TempoFinito(orologio)
            Dim nodo As IUIAutomationElement = pila.Pop()
            Dim livello As Integer = profondita.Pop()
            visitati += 1
            If nodo Is Nothing Then Continue While
            Dim tipo As Integer = descrittore.TipoControllo(nodo)
            Dim ruolo As String = descrittore.RuoloAria(nodo)
            If EContenitoreElenco(tipo, ruolo) OrElse descrittore.ContenitoreComposito(nodo) OrElse EChromeDaSaltare(tipo) Then Continue While
            If Not Object.ReferenceEquals(nodo, partenza) AndAlso EDocumento(tipo, ruolo) Then Return nodo
            If livello >= 7 Then Continue While
            Dim figlio As IUIAutomationElement = PrimoFiglio(walker, nodo, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 24
                pila.Push(figlio)
                profondita.Push(livello + 1)
                figlio = Fratello(walker, figlio, cache)
                giri += 1
            End While
        End While
        Return Nothing
    End Function

    Private Shared Function TestoPattern(elemento As IUIAutomationElement, massimo As Integer) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then pattern = TryCast(elemento.GetCurrentPattern(SchemaTesto2), IUIAutomationTextPattern)
            If pattern Is Nothing OrElse pattern.DocumentRange Is Nothing Then Return ""
            Dim contenuto As String = pattern.DocumentRange.GetText(massimo)
            If contenuto Is Nothing Then Return ""
            Return contenuto
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.TestoPattern", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function TestoFigli(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, massimo As Integer) As String
        Dim sb As New StringBuilder()
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Raccogli(elemento, descrittore, walker, cache, sb, 0, 0, massimo, orologio)
        Return sb.ToString()
    End Function

    Private Shared Function Raccogli(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, sb As StringBuilder, profondita As Integer, nodi As Integer, massimo As Integer, orologio As Stopwatch) As Integer
        If elemento Is Nothing OrElse profondita > 6 OrElse nodi > 48 OrElse sb.Length >= massimo OrElse TempoFinito(orologio) Then Return nodi
        nodi += 1
        Dim tipo As Integer = descrittore.TipoControllo(elemento)
        If EContenitoreElenco(tipo, descrittore.RuoloAria(elemento)) Then Return nodi
        Dim nome As String = descrittore.Nome(elemento)
        If nome <> "" AndAlso tipo <> DescrittoreElementi.ControlloFinestra Then
            Dim gia As String = sb.ToString()
            If gia.IndexOf(nome, StringComparison.CurrentCulture) < 0 Then
                If sb.Length > 0 Then sb.Append(" "c)
                sb.Append(nome)
            End If
        End If
        If sb.Length >= massimo Then Return nodi
        Dim figlio As IUIAutomationElement = PrimoFiglio(walker, elemento, cache)
        Dim giri As Integer = 0
        While figlio IsNot Nothing AndAlso giri < 18 AndAlso sb.Length < massimo
            nodi = Raccogli(figlio, descrittore, walker, cache, sb, profondita + 1, nodi, massimo, orologio)
            figlio = Fratello(walker, figlio, cache)
            giri += 1
        End While
        Return nodi
    End Function

    Private Shared Function Compatta(testo As String, massimo As Integer) As String
        If String.IsNullOrWhiteSpace(testo) Then Return ""
        Dim pulito As String = Spazi.Replace(testo.Replace(vbCr, " ").Replace(vbLf, " "), " ").Trim()
        If massimo > 0 AndAlso pulito.Length > massimo Then pulito = pulito.Substring(0, massimo).Trim()
        Return pulito
    End Function

    Private Shared Function TestoPatternSicuro(doc As IUIAutomationElement, massimo As Integer) As String
        Try
            Return TestoPattern(doc, massimo)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.TestoPatternSicuro", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function TestoFigliSicuro(doc As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, massimo As Integer) As String
        Try
            Return TestoFigli(doc, descrittore, walker, cache, massimo)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.TestoFigliSicuro", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function ZonaSicura(elemento As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, ByRef ancora As IUIAutomationElement) As Zona
        Try
            Return ZonaDi(elemento, descrittore, walker, cache, Stopwatch.StartNew(), ancora)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.ZonaSicura", ex)
            ancora = Nothing
            Return Zona.Sconosciuta
        End Try
    End Function

    Private Shared Function CercaSicuro(zona As Zona, ancora As IUIAutomationElement, descrittore As DescrittoreElementi, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            Return Cerca(zona, ancora, descrittore, walker, cache)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.CercaSicuro", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function EDocumento(tipo As Integer, ruolo As String) As Boolean
        Return tipo = DescrittoreElementi.ControlloDocumento OrElse ruolo = "document"
    End Function

    Private Shared Function EContenitoreElenco(tipo As Integer, ruolo As String) As Boolean
        If tipo = DescrittoreElementi.ControlloAlbero OrElse tipo = DescrittoreElementi.ControlloElenco OrElse tipo = DescrittoreElementi.ControlloTabella Then Return True
        Return ruolo = "tree" OrElse ruolo = "list" OrElse ruolo = "treegrid" OrElse ruolo = "grid" OrElse ruolo = "listbox" OrElse ruolo = "table"
    End Function

    Private Shared Function EChromeDaSaltare(tipo As Integer) As Boolean
        Return tipo = DescrittoreElementi.ControlloBarraStrumenti OrElse tipo = DescrittoreElementi.ControlloBarraMenu OrElse tipo = DescrittoreElementi.ControlloMenu
    End Function

    Private Shared Function ChiaveElemento(elemento As IUIAutomationElement) As String
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
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.ChiaveElemento", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function TempoFinito(orologio As Stopwatch) As Boolean
        Return orologio IsNot Nothing AndAlso orologio.ElapsedMilliseconds > BudgetMs
    End Function

    Private Shared Function Genitore(walker As IUIAutomationTreeWalker, elemento As IUIAutomationElement, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            If walker Is Nothing OrElse elemento Is Nothing Then Return Nothing
            If cache Is Nothing Then Return walker.GetParentElement(elemento)
            Return walker.GetParentElementBuildCache(elemento, cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.Genitore", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function PrimoFiglio(walker As IUIAutomationTreeWalker, elemento As IUIAutomationElement, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            If walker Is Nothing OrElse elemento Is Nothing Then Return Nothing
            If cache Is Nothing Then Return walker.GetFirstChildElement(elemento)
            Return walker.GetFirstChildElementBuildCache(elemento, cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.PrimoFiglio", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function Fratello(walker As IUIAutomationTreeWalker, elemento As IUIAutomationElement, cache As IUIAutomationCacheRequest) As IUIAutomationElement
        Try
            If walker Is Nothing OrElse elemento Is Nothing Then Return Nothing
            If cache Is Nothing Then Return walker.GetNextSiblingElement(elemento)
            Return walker.GetNextSiblingElementBuildCache(elemento, cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaRiquadroCollegato.Fratello", exAtteso)
            Return Nothing
        End Try
    End Function

End Class
