Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Console di Windows, Windows Terminal, PuTTY e simili: lettura dello schermo, revisione per righe, righe nuove senza ingorghi.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const IdProprietaTestoDisponibile As Integer = 30040
    Private Const SchemaTestoTerminale As Integer = 10014
    Private Const MassimoCaratteriTerminale As Integer = 60000
    Private Const ScadenzaRevisioneTerminaleMs As Integer = 6000

    Private _terminaleIndice As Integer
    Private _terminaleTick As Integer

    Friend Function TerminaleInPrimoPiano() As Boolean
        Try
            Return FinestraTerminale(NativoLettore.GetForegroundWindow())
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TerminaleInPrimoPiano", ex)
            Return False
        End Try
    End Function

    Private Function SuperficieTerminale() As IUIAutomationElement
        Try
            Dim fuoco As IUIAutomationElement = ElementoConFocus(False)
            If fuoco IsNot Nothing AndAlso _descrittore IsNot Nothing AndAlso _descrittore.HaPatternTesto(fuoco) Then Return fuoco
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero OrElse _automazione Is Nothing Then Return Nothing
            Dim radice As IUIAutomationElement = _automazione.ElementFromHandle(finestra)
            If radice Is Nothing Then Return Nothing
            If _descrittore IsNot Nothing AndAlso _descrittore.HaPatternTesto(radice) Then Return radice
            Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(IdProprietaTestoDisponibile, True)
            Return radice.FindFirst(TreeScope.TreeScope_Descendants, condizione)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SuperficieTerminale", ex)
            Return Nothing
        End Try
    End Function

    ' Righe visibili dello schermo, senza gli spazi in coda; indiceCursore è la riga dove sta il cursore (-1 se non si sa).
    Private Function RigheSchermoTerminale(ByRef indiceCursore As Integer) As List(Of String)
        indiceCursore = -1
        Dim righe As New List(Of String)()
        Try
            Dim superficie As IUIAutomationElement = SuperficieTerminale()
            If superficie Is Nothing Then Return righe
            Dim modello As IUIAutomationTextPattern = TryCast(superficie.GetCurrentPattern(SchemaTestoTerminale), IUIAutomationTextPattern)
            If modello Is Nothing Then Return righe
            Dim visibile As IUIAutomationTextRange = Nothing
            Dim visibili As IUIAutomationTextRangeArray = modello.GetVisibleRanges()
            If visibili IsNot Nothing AndAlso visibili.Length > 0 Then visibile = visibili.GetElement(0)
            If visibile Is Nothing Then visibile = modello.DocumentRange
            Dim testo As String = visibile.GetText(MassimoCaratteriTerminale)
            If testo Is Nothing Then Return righe
            For Each riga As String In testo.Replace(vbCr, "").Split(ChrW(10))
                righe.Add(riga.TrimEnd())
            Next
            While righe.Count > 0 AndAlso righe(righe.Count - 1) = ""
                righe.RemoveAt(righe.Count - 1)
            End While
            Try
                Dim selezioni As IUIAutomationTextRangeArray = modello.GetSelection()
                If selezioni IsNot Nothing AndAlso selezioni.Length > 0 Then
                    Dim fino As IUIAutomationTextRange = visibile.Clone()
                    fino.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, selezioni.GetElement(0), TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                    Dim prima As String = fino.GetText(MassimoCaratteriTerminale)
                    If prima IsNot Nothing Then
                        Dim conta As Integer = 0
                        For Each c As Char In prima
                            If c = ChrW(10) Then conta += 1
                        Next
                        indiceCursore = conta
                    End If
                End If
            Catch ex As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.RigheSchermoTerminale.Cursore", ex)
            End Try
            If righe.Count > 0 AndAlso indiceCursore >= righe.Count Then indiceCursore = righe.Count - 1
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RigheSchermoTerminale", ex)
        End Try
        Return righe
    End Function

    Friend Function RigaCursoreTerminaleApp() As String
        Try
            Dim cursore As Integer
            Dim righe As List(Of String) = RigheSchermoTerminale(cursore)
            If cursore >= 0 AndAlso cursore < righe.Count Then Return righe(cursore)
            Return ""
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RigaCursoreTerminaleApp", ex)
            Return ""
        End Try
    End Function

    ' Revisione per righe senza muovere il cursore: precedente, successiva, prima dello schermo, ultima.
    Private Function SpostaRevisioneTerminale(comando As ComandoLettore) As String
        Try
            Dim cursore As Integer
            Dim righe As List(Of String) = RigheSchermoTerminale(cursore)
            If righe.Count = 0 Then Return "schermo vuoto"
            Dim recente As Boolean = _terminaleTick <> 0 AndAlso Environment.TickCount - _terminaleTick < ScadenzaRevisioneTerminaleMs
            Dim indice As Integer = If(recente, _terminaleIndice, If(cursore >= 0, cursore, righe.Count - 1))
            Select Case comando
                Case ComandoLettore.Precedente
                    indice -= 1
                Case ComandoLettore.Successivo
                    indice += 1
                Case ComandoLettore.Contenitore
                    indice = 0
                Case ComandoLettore.Contenuto
                    indice = righe.Count - 1
                Case Else
                    Return ""
            End Select
            If indice < 0 Then
                If _codaVoce IsNot Nothing Then _codaVoce.SuonaLimite()
                _terminaleIndice = 0
                _terminaleTick = Environment.TickCount
                Return "inizio dello schermo"
            End If
            If indice >= righe.Count Then
                If _codaVoce IsNot Nothing Then _codaVoce.SuonaLimite()
                _terminaleIndice = righe.Count - 1
                _terminaleTick = Environment.TickCount
                Return "fine dello schermo"
            End If
            _terminaleIndice = indice
            _terminaleTick = Environment.TickCount
            Dim testo As String = righe(indice)
            Return If(testo = "", "riga vuota", testo)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SpostaRevisioneTerminale", ex)
            Return ""
        End Try
    End Function

    ' Tutto quello che si vede nel terminale, dalla prima all'ultima riga non vuota.
    Private Function LeggiSchermoTerminale(titolo As String) As String
        Try
            Dim cursore As Integer
            Dim righe As List(Of String) = RigheSchermoTerminale(cursore)
            Dim parti As New List(Of String)()
            If titolo <> "" Then parti.Add(titolo)
            For Each riga As String In righe
                If Not String.IsNullOrWhiteSpace(riga) Then parti.Add(riga)
            Next
            If parti.Count = 0 OrElse (parti.Count = 1 AndAlso titolo <> "") Then Return "Schermo vuoto"
            Return String.Join(". ", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.LeggiSchermoTerminale", ex)
            Return ""
        End Try
    End Function

    Friend Sub InterrompiVoceApp()
        RichiediInterruzione()
    End Sub

End Class
