Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports UIAutomationClient

' Risoluzione dei nomi dei controlli e degli elementi dell'interfaccia grafica, gestendo etichette vicine, tooltip e
' identificatori per la lettura vocale.
Friend NotInheritable Class NomiAffidabili

    Private Const ProfonditaMassima As Integer = 2
    Private Const FratelliMassimi As Integer = 8
    Private ReadOnly log As New Logger()
    Private ReadOnly _automazione As IUIAutomation
    Private ReadOnly _descrittore As DescrittoreElementi
    Private ReadOnly _cache As IUIAutomationCacheRequest

    Public Sub New(automazione As IUIAutomation, descrittore As DescrittoreElementi, cache As IUIAutomationCacheRequest)
        _automazione = automazione
        _descrittore = descrittore
        _cache = cache
    End Sub

    Public Function Risolvi(elemento As IUIAutomationElement) As String
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return ""
            If orologio.ElapsedMilliseconds > 40 Then Return ""
            Dim daEtichetta As String = DaEtichettatoDa(elemento)
            If daEtichetta <> "" Then Return daEtichetta
            Dim classico As String = NomeClassico(elemento)
            If classico <> "" Then Return classico
            Dim aiuto As String = _descrittore.TestoAiuto(elemento)
            If aiuto <> "" AndAlso aiuto.Length <= 180 Then Return aiuto
            Dim contenuto As String = TestoContenuto(elemento, 0, orologio)
            If contenuto <> "" Then Return contenuto
            Dim vicina As String = EtichettaVicina(elemento, orologio)
            If vicina <> "" Then Return vicina
            Dim suggerimento As String = Tooltip(elemento)
            If suggerimento <> "" Then Return suggerimento
            If DescrittoreElementi.ProlissitaApplicata >= 2 Then
                Dim id As String = Identificatore(elemento)
                If id <> "" Then Return "identificatore " & id
            End If
            Dim idBasso As String = Identificatore(elemento)
            If idBasso <> "" AndAlso SembraEtichetta(idBasso) Then Return idBasso.Replace("_"c, " "c)
            Return ""
        Catch ex As Exception
            log.ScriviLog("NomiAffidabili.Risolvi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeClassico(elemento As IUIAutomationElement) As String
        Try
            Dim legacy As IUIAutomationLegacyIAccessiblePattern = TryCast(elemento.GetCurrentPattern(10018), IUIAutomationLegacyIAccessiblePattern)
            If legacy Is Nothing Then Return ""
            Dim nome As String = legacy.CurrentName
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Return nome.Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NomiAffidabili.NomeClassico", exAtteso)
            Return ""
        End Try
    End Function

    Private Function DaEtichettatoDa(elemento As IUIAutomationElement) As String
        Try
            Dim valore As Object = Nothing
            Try
                valore = elemento.GetCachedPropertyValue(DescrittoreElementi.ProprietaEtichettatoDa)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NomiAffidabili.DaEtichettatoDa", exAtteso)
                valore = elemento.GetCurrentPropertyValue(DescrittoreElementi.ProprietaEtichettatoDa)
            End Try
            Dim etichetta As IUIAutomationElement = TryCast(valore, IUIAutomationElement)
            If etichetta Is Nothing Then Return ""
            Dim nome As String = _descrittore.Nome(etichetta)
            If nome = "" Then
                Try
                    nome = If(etichetta.CurrentName, "")
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NomiAffidabili.DaEtichettatoDa", exAtteso2)
                    nome = ""
                End Try
            End If
            Return nome.Trim()
        Catch exAtteso3 As Exception
            RegistroErrori.Scrivi("NomiAffidabili.DaEtichettatoDa", exAtteso3)
            Return ""
        End Try
    End Function

    Private Function TestoContenuto(elemento As IUIAutomationElement, profondita As Integer, orologio As Stopwatch) As String
        Try
            If profondita > ProfonditaMassima OrElse orologio.ElapsedMilliseconds > 35 Then Return ""
            If _descrittore.HaPatternTesto(elemento) Then
                Dim pattern As IUIAutomationTextPattern = Nothing
                Try
                    pattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("NomiAffidabili.TestoContenuto", exAtteso)
                    pattern = Nothing
                End Try
                If pattern IsNot Nothing AndAlso pattern.DocumentRange IsNot Nothing Then
                    Dim testo As String = pattern.DocumentRange.GetText(160)
                    ComLettore.Rilascia(pattern)
                    If testo IsNot Nothing Then
                        testo = testo.Trim()
                        If testo <> "" AndAlso testo.Length <= 160 Then Return testo
                    End If
                End If
            End If
            If _automazione Is Nothing OrElse _cache Is Nothing Then Return ""
            Dim walker As IUIAutomationTreeWalker = _automazione.ControlViewWalker
            If walker Is Nothing Then Return ""
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(elemento, _cache)
            Dim parti As New List(Of String)()
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 6 AndAlso parti.Count < 3 AndAlso orologio.ElapsedMilliseconds < 35
                Dim nomeFiglio As String = _descrittore.Nome(figlio)
                If nomeFiglio = "" Then nomeFiglio = TestoContenuto(figlio, profondita + 1, orologio)
                If nomeFiglio <> "" Then parti.Add(nomeFiglio)
                Dim successivo As IUIAutomationElement = walker.GetNextSiblingElementBuildCache(figlio, _cache)
                figlio = successivo
                giri += 1
            End While
            If parti.Count = 0 Then Return ""
            Return String.Join(" ", parti)
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("NomiAffidabili.TestoContenuto", exAtteso2)
            Return ""
        End Try
    End Function

    Private Function EtichettaVicina(elemento As IUIAutomationElement, orologio As Stopwatch) As String
        Try
            If _automazione Is Nothing OrElse orologio.ElapsedMilliseconds > 35 Then Return ""
            Dim walker As IUIAutomationTreeWalker = _automazione.RawViewWalker
            If walker Is Nothing Then Return ""
            Dim genitore As IUIAutomationElement = walker.GetParentElementBuildCache(elemento, _cache)
            If genitore Is Nothing Then Return ""
            Dim mio As RettangoloLettore = RettangoloDi(elemento)
            Dim migliore As String = ""
            Dim miglioreDistanza As Integer = Integer.MaxValue
            Dim fratello As IUIAutomationElement = walker.GetFirstChildElementBuildCache(genitore, _cache)
            Dim giri As Integer = 0
            While fratello IsNot Nothing AndAlso giri < 24 AndAlso orologio.ElapsedMilliseconds < 40
                If _automazione.CompareElements(fratello, elemento) = 0 Then
                    Dim nome As String = _descrittore.Nome(fratello)
                    If nome <> "" AndAlso SembraEtichetta(nome) Then
                        Dim suo As RettangoloLettore = RettangoloDi(fratello)
                        Dim aSinistra As Boolean = suo.Destra <= mio.Sinistra + 8 AndAlso suo.Destra > 0 AndAlso Math.Abs(suo.Alto - mio.Alto) < Math.Max(40, mio.Altezza + 8)
                        Dim sopra As Boolean = suo.Basso <= mio.Alto + 8 AndAlso suo.Basso > 0 AndAlso Math.Abs(suo.Sinistra - mio.Sinistra) < Math.Max(80, mio.Larghezza)
                        If aSinistra OrElse sopra Then
                            Dim distanza As Integer = If(aSinistra, mio.Sinistra - suo.Destra, mio.Alto - suo.Basso)
                            If distanza < miglioreDistanza Then
                                miglioreDistanza = distanza
                                migliore = nome.Trim().TrimEnd(":"c).Trim()
                            End If
                        End If
                    End If
                End If
                fratello = walker.GetNextSiblingElementBuildCache(fratello, _cache)
                giri += 1
            End While
            Return migliore
        Catch ex As Exception
            log.ScriviLog("NomiAffidabili.EtichettaVicina: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Tooltip(elemento As IUIAutomationElement) As String
        Try
            Dim aiuto As String = _descrittore.TestoAiuto(elemento)
            If aiuto <> "" Then Return aiuto
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NomiAffidabili.Tooltip", exAtteso)
            Return ""
        End Try
    End Function

    Private Function Identificatore(elemento As IUIAutomationElement) As String
        Try
            Dim id As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione)
            If id <> "" Then Return id
            Try
                id = If(elemento.CurrentAutomationId, "")
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NomiAffidabili.Identificatore", exAtteso)
                id = ""
            End Try
            Return If(id, "").Trim()
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("NomiAffidabili.Identificatore", exAtteso2)
            Return ""
        End Try
    End Function

    Private Function SembraEtichetta(testo As String) As Boolean
        If String.IsNullOrWhiteSpace(testo) Then Return False
        If testo.Length > 80 Then Return False
        Dim lettere As Integer = 0
        For Each carattere As Char In testo
            If Char.IsLetter(carattere) Then lettere += 1
        Next
        Return lettere >= 2
    End Function

    Private Function RettangoloDi(elemento As IUIAutomationElement) As RettangoloLettore
        Dim esito As New RettangoloLettore()
        Try
            If _automazione Is Nothing OrElse elemento Is Nothing Then Return esito
            Dim rettangolo As tagRECT = _automazione.VariantToRect(elemento.GetCurrentPropertyValue(DescrittoreElementi.ProprietaRettangolo))
            esito.Sinistra = rettangolo.left
            esito.Alto = rettangolo.top
            esito.Destra = rettangolo.right
            esito.Basso = rettangolo.bottom
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NomiAffidabili.RettangoloDi", exAtteso)
        End Try
        Return esito
    End Function

    Private Structure RettangoloLettore
        Public Sinistra As Integer
        Public Alto As Integer
        Public Destra As Integer
        Public Basso As Integer
        Public ReadOnly Property Larghezza As Integer
            Get
                Return Math.Max(0, Destra - Sinistra)
            End Get
        End Property
        Public ReadOnly Property Altezza As Integer
            Get
                Return Math.Max(0, Basso - Alto)
            End Get
        End Property
    End Structure

End Class
