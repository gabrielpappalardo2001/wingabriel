Imports System.Collections.Generic
Imports System.Text
Imports UIAutomationClient

' Gestione della lettura dei campi di testo, cursori, selezioni ed eco dei tasti digitati.
Friend NotInheritable Class DifferenzaTesto
    Public Rimosso As String = ""
    Public Aggiunto As String = ""
    Public Sconosciuta As Boolean
End Class

Friend NotInheritable Class IstantaneaCampo
    Public TestoPrima As String = ""
    Public TestoDopo As String = ""
    Public Selezione As String = ""
    Public Valore As String = ""
    Public HaCursore As Boolean
    Public Protetto As Boolean

    Public Function Uguale(altra As IstantaneaCampo) As Boolean
        If altra Is Nothing Then Return False
        Return HaCursore = altra.HaCursore AndAlso Protetto = altra.Protetto AndAlso
            StessoTesto(altra) AndAlso Valore = altra.Valore
    End Function

    Public Function StessoTesto(altra As IstantaneaCampo) As Boolean
        If altra Is Nothing Then Return False
        Return HaCursore = altra.HaCursore AndAlso
            TestoPrima = altra.TestoPrima AndAlso TestoDopo = altra.TestoDopo AndAlso
            Selezione = altra.Selezione
    End Function

    Public Function Clona() As IstantaneaCampo
        Dim copia As New IstantaneaCampo()
        copia.TestoPrima = TestoPrima
        copia.TestoDopo = TestoDopo
        copia.Selezione = Selezione
        copia.Valore = Valore
        copia.HaCursore = HaCursore
        copia.Protetto = Protetto
        Return copia
    End Function
End Class

Friend NotInheritable Class LetturaCampi

    Private Const VK_BACK As Integer = &H8
    Private Const VK_RETURN As Integer = &HD
    Private Const VK_SPACE As Integer = &H20
    Private Const VK_LEFT As Integer = &H25
    Private Const VK_UP As Integer = &H26
    Private Const VK_RIGHT As Integer = &H27
    Private Const VK_DOWN As Integer = &H28
    Private Const VK_HOME As Integer = &H24
    Private Const VK_END As Integer = &H23
    Private Const VK_DELETE As Integer = &H2E
    Private Const VK_A As Integer = &H41
    Private Const VK_V As Integer = &H56
    Private Const VK_X As Integer = &H58
    Private Const FinestraCursore As Integer = 500
    Private Const MassimoSelezione As Integer = 2000
    Private Const MassimoValore As Integer = 8000
    Private Const SchemaValore As Integer = 10002
    Private Const SchemaSelezione As Integer = 10001

    Private ReadOnly log As New Logger()
    Private ReadOnly _automazione As IUIAutomation
    Private ReadOnly _descrittore As DescrittoreElementi
    Private _ultimoRientro As String = ""
    Private _rientroElemento As IUIAutomationElement

    Public Shared Property RientriApplicati As Boolean = True
    Private ReadOnly _walker As IUIAutomationTreeWalker
    Private _limite As IUIAutomationTextRange

    Public Sub New(automazione As IUIAutomation, descrittore As DescrittoreElementi)
        Try
            _automazione = automazione
            _descrittore = descrittore
            If automazione IsNot Nothing Then _walker = automazione.ControlViewWalker
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.New: " & ex.Message)
        End Try
    End Sub

    Public Function HaTesto(elemento As IUIAutomationElement) As Boolean
        Try
            Return SorgenteTesto(elemento) IsNot Nothing
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.HaTesto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Cattura(elemento As IUIAutomationElement) As IstantaneaCampo
        Dim istantanea As New IstantaneaCampo()
        Try
            If elemento Is Nothing Then Return istantanea
            istantanea.Protetto = _descrittore.Protetto(elemento)
            Dim origine As IUIAutomationElement = SorgenteTesto(elemento)
            If origine Is Nothing Then origine = elemento
            ImpostaLimite(elemento, origine)
            Dim cursore As IUIAutomationTextRange = IntervalloCursore(origine)
            If cursore IsNot Nothing Then
                istantanea.HaCursore = True
                istantanea.Selezione = TestoNonCollassato(cursore)
                istantanea.TestoPrima = TestoVerso(cursore, True)
                istantanea.TestoDopo = TestoVerso(cursore, False)
            ElseIf Not ProvaEditNativo(elemento, istantanea) Then
                ProvaEditNativo(origine, istantanea)
            End If
            Dim valore As String = LeggiValore(origine)
            If valore.Length > MassimoValore Then valore = valore.Substring(0, MassimoValore)
            If ValoreSegnaposto(elemento, origine, valore, istantanea) Then valore = ""
            If istantanea.Valore = "" Then istantanea.Valore = valore
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.Cattura: " & ex.Message)
        End Try
        Return istantanea
    End Function

    Private Function ValoreSegnaposto(elemento As IUIAutomationElement, origine As IUIAutomationElement, valore As String, istantanea As IstantaneaCampo) As Boolean
        Try
            If String.IsNullOrWhiteSpace(valore) OrElse istantanea Is Nothing Then Return False
            If (istantanea.TestoPrima & istantanea.Selezione & istantanea.TestoDopo).Trim() <> "" Then Return False
            Dim pulito As String = valore.Trim()
            For Each candidato As IUIAutomationElement In New IUIAutomationElement() {elemento, origine}
                If candidato Is Nothing Then Continue For
                If String.Equals(pulito, _descrittore.Nome(candidato).Trim(), StringComparison.CurrentCultureIgnoreCase) Then Return True
                If String.Equals(pulito, _descrittore.LeggiStringa(candidato, 30013).Trim(), StringComparison.CurrentCultureIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaCampi.ValoreSegnaposto", ex)
            Return False
        End Try
    End Function

    Public Function DescriviModifica(precedente As IstantaneaCampo, attuale As IstantaneaCampo, codice As Integer, ctrl As Boolean, maiuscolo As Boolean, ecoCaratteri As Boolean, ecoParole As Boolean, leggiPassword As Boolean) As String
        Try
            If attuale Is Nothing OrElse precedente Is Nothing Then Return ""
            If precedente.Uguale(attuale) Then Return ""
            Dim protetto As Boolean = attuale.Protetto AndAlso Not leggiPassword
            If precedente.Selezione <> "" AndAlso attuale.Selezione = "" AndAlso precedente.TestoPrima = attuale.TestoPrima AndAlso precedente.TestoDopo = attuale.TestoDopo Then
                If codice = VK_BACK OrElse codice = VK_DELETE OrElse (ctrl AndAlso codice = VK_X) Then
                    Return FraseCancellazione(precedente.Selezione, protetto, codice, ctrl, maiuscolo, True)
                End If
                Return "deselezionato"
            End If
            Dim differenza As DifferenzaTesto = Nothing
            If precedente.HaCursore AndAlso attuale.HaCursore Then
                differenza = DifferenzaCursore(precedente, attuale)
            End If
            If differenza Is Nothing OrElse differenza.Sconosciuta OrElse (differenza.Rimosso = "" AndAlso differenza.Aggiunto = "") Then
                If precedente.Valore <> attuale.Valore Then differenza = DiffValore(precedente.Valore, attuale.Valore)
            End If
            If differenza Is Nothing OrElse (differenza.Rimosso = "" AndAlso differenza.Aggiunto = "") Then
                If precedente.Selezione <> attuale.Selezione Then Return DescriviCambioSelezione(precedente.Selezione, attuale.Selezione)
                Return ""
            End If
            If differenza.Rimosso <> "" AndAlso differenza.Aggiunto = "" Then
                Dim rimosso As String = differenza.Rimosso
                If Not ctrl AndAlso precedente.HaCursore AndAlso precedente.Selezione = "" Then
                    If codice = VK_BACK Then
                        Dim atteso As String = If(String.IsNullOrEmpty(precedente.TestoPrima), "", Bordo(precedente.TestoPrima, True))
                        If atteso <> "" AndAlso Not rimosso.EndsWith(atteso, StringComparison.Ordinal) Then rimosso = atteso
                    ElseIf codice = VK_DELETE AndAlso Not maiuscolo Then
                        Dim atteso As String = If(String.IsNullOrEmpty(precedente.TestoDopo), "", Bordo(precedente.TestoDopo, False))
                        If atteso <> "" AndAlso Not rimosso.StartsWith(atteso, StringComparison.Ordinal) Then rimosso = atteso
                    End If
                End If
                Return FraseCancellazione(rimosso, protetto, codice, ctrl, maiuscolo)
            End If
            If differenza.Aggiunto <> "" AndAlso differenza.Rimosso = "" Then
                Return FraseInserimento(differenza.Aggiunto, attuale.TestoPrima, protetto, ecoCaratteri, ecoParole, codice, ctrl, maiuscolo)
            End If
            If protetto Then Return If(ecoCaratteri, "stella", "")
            If ecoCaratteri OrElse differenza.Aggiunto.Length > 1 Then
                Return FraseInserimento(differenza.Aggiunto, attuale.TestoPrima, False, True, ecoParole, codice, ctrl, maiuscolo)
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.DescriviModifica: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function DescriviNavigazione(elemento As IUIAutomationElement, codice As Integer, ctrl As Boolean, alt As Boolean, maiuscolo As Boolean, selezionePrecedente As String) As String
        Try
            If maiuscolo Then
                Dim selezione As String = TestoSelezionato(elemento)
                Dim frase As String = DescriviCambioSelezione(selezionePrecedente, selezione)
                If frase <> "" Then Return frase
            End If
            Return LeggiUnita(elemento, codice, ctrl, alt)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.DescriviNavigazione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function DescriviCambioSelezione(precedente As String, attuale As String) As String
        Try
            If precedente Is Nothing Then precedente = ""
            If attuale Is Nothing Then attuale = ""
            If precedente = attuale Then Return ""
            If attuale = "" Then Return "nessuna selezione"
            If precedente = "" Then Return MessaggioSelezione(attuale)
            If attuale.StartsWith(precedente, StringComparison.Ordinal) Then
                Return Pezzo(attuale.Substring(precedente.Length)) & " selezionato"
            End If
            If precedente.StartsWith(attuale, StringComparison.Ordinal) Then
                Return Pezzo(precedente.Substring(attuale.Length)) & " deselezionato"
            End If
            If attuale.EndsWith(precedente, StringComparison.Ordinal) Then
                Return Pezzo(attuale.Substring(0, attuale.Length - precedente.Length)) & " selezionato"
            End If
            If precedente.EndsWith(attuale, StringComparison.Ordinal) Then
                Return Pezzo(precedente.Substring(0, precedente.Length - attuale.Length)) & " deselezionato"
            End If
            Return MessaggioSelezione(attuale)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.DescriviCambioSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function MessaggioSelezione(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return "nessuna selezione"
            If testo.Length >= 512 Then Return "selezionati " & testo.Length.ToString() & " caratteri"
            If testo.Length = 1 Then Return _descrittore.NomeCarattere(testo) & " selezionato"
            Return _descrittore.TestoRiga(testo) & " selezionato"
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.MessaggioSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function TestoSelezionato(elemento As IUIAutomationElement) As String
        Try
            Dim parti As New List(Of String)()
            RaccogliSelezioneTesto(elemento, parti, 0)
            If parti.Count > 0 Then Return String.Join(" ", parti)
            Dim nativa As String = SelezioneNativa(elemento)
            If nativa <> "" Then Return nativa
            Dim voci As String = SelezioneElenco(elemento)
            Return voci
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.TestoSelezionato: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiSelezione(elemento As IUIAutomationElement) As String
        Try
            Dim testo As String = TestoSelezionato(elemento)
            If testo = "" Then Return "nessuna selezione"
            If testo.StartsWith("selezionat", StringComparison.CurrentCultureIgnoreCase) Then Return testo
            If testo.Length = 1 Then Return _descrittore.NomeCarattere(testo) & " selezionato"
            If testo.Length >= 512 Then Return "selezionati " & testo.Length.ToString() & " caratteri"
            Return _descrittore.TestoRiga(testo) & " selezionato"
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiCarattere(elemento As IUIAutomationElement) As String
        Try
            If _descrittore.Protetto(elemento) Then Return "stella"
            Dim origine As IUIAutomationElement = SorgenteTesto(elemento)
            ImpostaLimite(elemento, origine)
            Dim cursore As IUIAutomationTextRange = IntervalloCursore(origine)
            If cursore Is Nothing Then Return ""
            Dim prima As String = EstraiUnita(cursore, TextUnit.TextUnit_Character, True)
            If prima <> "" Then Return _descrittore.NomeCarattere(prima)
            Dim dopo As String = EstraiUnita(cursore, TextUnit.TextUnit_Character, False)
            If dopo <> "" Then Return _descrittore.NomeCarattere(dopo)
            Return "vuoto"
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiCarattere: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiParola(elemento As IUIAutomationElement) As String
        Try
            If _descrittore.Protetto(elemento) Then Return "protetto"
            Dim origine As IUIAutomationElement = SorgenteTesto(elemento)
            ImpostaLimite(elemento, origine)
            Dim cursore As IUIAutomationTextRange = IntervalloCursore(origine)
            If cursore Is Nothing Then
                Dim valore As String = LeggiValore(elemento)
                Dim parola As String = UltimaParola(valore)
                If parola = "" Then Return "vuoto"
                Return parola
            End If
            Dim intervallo As IUIAutomationTextRange = cursore.Clone()
            Collassa(intervallo, True)
            intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Word)
            Limita(intervallo)
            Dim testo As String = Pulisci(intervallo.GetText(200))
            If testo = "" Then testo = Pulisci(EstraiUnita(cursore, TextUnit.TextUnit_Word, True))
            If testo = "" Then Return "vuoto"
            If testo.Length = 1 Then Return _descrittore.NomeCarattere(testo)
            Return testo
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiParola: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiRiga(elemento As IUIAutomationElement) As String
        Try
            Return LeggiUnita(elemento, VK_UP, False, False)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiRiga: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiFinoAllaFine(elemento As IUIAutomationElement) As String
        Try
            Dim origine As IUIAutomationElement = SorgenteTesto(elemento)
            ImpostaLimite(elemento, origine)
            Dim pattern As IUIAutomationTextPattern = PatternTesto(origine)
            If pattern Is Nothing Then Return ""
            Dim selezioni As IUIAutomationTextRangeArray = pattern.GetSelection()
            If selezioni Is Nothing OrElse selezioni.Length = 0 Then Return ""
            Dim intervallo As IUIAutomationTextRange = selezioni.GetElement(0).Clone()
            intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, pattern.DocumentRange, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            Limita(intervallo)
            Dim testo As String = intervallo.GetText(100000)
            If String.IsNullOrWhiteSpace(testo) Then Return "vuoto"
            Return testo
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiFinoAllaFine: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ConRientro(origine As IUIAutomationElement, riga As String) As String
        Dim testo As String = ""
        Try
            testo = _descrittore.TestoRiga(riga)
            If Not RientriApplicati OrElse String.IsNullOrWhiteSpace(riga) Then Return testo
            Dim rientro As String = DescriviRientro(riga)
            Dim stesso As Boolean = StessoCampoRientro(origine)
            If stesso AndAlso rientro = _ultimoRientro Then Return testo
            _ultimoRientro = rientro
            _rientroElemento = origine
            If Not stesso AndAlso rientro = "" Then Return testo
            Dim frase As String = If(rientro = "", "nessun rientro", rientro)
            If testo = "" Then Return frase
            Return frase & ", " & testo
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.ConRientro: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function StessoCampoRientro(origine As IUIAutomationElement) As Boolean
        Try
            Dim precedente As IUIAutomationElement = _rientroElemento
            If origine Is Nothing OrElse precedente Is Nothing OrElse _automazione Is Nothing Then Return False
            Return _automazione.CompareElements(origine, precedente) <> 0
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaCampi.StessoCampoRientro", ex)
            Return False
        End Try
    End Function

    Private Shared Function DescriviRientro(riga As String) As String
        Try
            If String.IsNullOrEmpty(riga) Then Return ""
            Dim spazi As Integer = 0
            Dim tabulazioni As Integer = 0
            For Each carattere As Char In riga
                If carattere = " "c OrElse carattere = ChrW(160) Then
                    spazi += 1
                ElseIf carattere = ChrW(9) Then
                    tabulazioni += 1
                Else
                    Exit For
                End If
            Next
            Dim parti As New List(Of String)()
            If tabulazioni > 0 Then parti.Add(tabulazioni.ToString() & If(tabulazioni = 1, " tabulazione", " tabulazioni"))
            If spazi > 0 Then parti.Add(spazi.ToString() & If(spazi = 1, " spazio", " spazi"))
            Return String.Join(" e ", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaCampi.DescriviRientro", ex)
            Return ""
        End Try
    End Function

    Private Function LeggiUnita(elemento As IUIAutomationElement, codice As Integer, ctrl As Boolean, alt As Boolean) As String
        Try
            If elemento IsNot Nothing AndAlso _descrittore.Protetto(elemento) Then
                If codice = VK_LEFT OrElse codice = VK_RIGHT Then Return "stella"
                Return "protetto"
            End If
            Dim origine As IUIAutomationElement = SorgenteTesto(elemento)
            ImpostaLimite(elemento, origine)
            Dim cursore As IUIAutomationTextRange = IntervalloCursore(origine)
            If cursore Is Nothing Then
                Dim valore As String = LeggiValore(elemento)
                If valore = "" Then Return ""
                If valore.Length > 180 Then Return valore.Substring(0, 180)
                Return valore
            End If
            If alt AndAlso Not ctrl AndAlso (codice = VK_UP OrElse codice = VK_DOWN) Then
                Dim frase As String = LeggiFrase(cursore)
                If frase <> "" Then Return frase
            End If
            Dim unita As TextUnit = UnitaDi(codice, ctrl)
            If ctrl AndAlso (codice = &H24 OrElse codice = &H23) Then
                Return TestoEstremoDocumento(cursore, codice = &H24)
            End If
            Dim primaDelCursore As Boolean = False
            If unita = TextUnit.TextUnit_Word Then
                Dim parola As IUIAutomationTextRange = cursore.Clone()
                Collassa(parola, True)
                parola.ExpandToEnclosingUnit(TextUnit.TextUnit_Word)
                Limita(parola)
                Dim letta As String = Pulisci(parola.GetText(200))
                If letta = "" Then letta = Pulisci(EstraiUnita(cursore, TextUnit.TextUnit_Word, primaDelCursore))
                If letta = "" Then Return "vuoto"
                If letta.Length = 1 Then Return _descrittore.NomeCarattere(letta)
                Return letta
            End If
            If unita = TextUnit.TextUnit_Line OrElse unita = TextUnit.TextUnit_Paragraph OrElse unita = TextUnit.TextUnit_Document Then
                Dim riga As IUIAutomationTextRange = cursore.Clone()
                Collassa(riga, True)
                riga.ExpandToEnclosingUnit(unita)
                Limita(riga)
                Dim testoRiga As String = riga.GetText(4000)
                If String.IsNullOrWhiteSpace(testoRiga) AndAlso unita = TextUnit.TextUnit_Line Then
                    Dim paragrafo As IUIAutomationTextRange = cursore.Clone()
                    Collassa(paragrafo, True)
                    paragrafo.ExpandToEnclosingUnit(TextUnit.TextUnit_Paragraph)
                    Limita(paragrafo)
                    testoRiga = paragrafo.GetText(4000)
                    If String.IsNullOrWhiteSpace(testoRiga) Then
                        Dim valoreCampo As String = LeggiValore(origine)
                        If Not String.IsNullOrWhiteSpace(valoreCampo) AndAlso valoreCampo.IndexOf(ChrW(10)) < 0 AndAlso valoreCampo.IndexOf(ChrW(13)) < 0 Then testoRiga = valoreCampo
                    End If
                End If
                If unita = TextUnit.TextUnit_Document Then Return _descrittore.TestoRiga(testoRiga)
                Return ConRientro(origine, testoRiga)
            End If
            Dim testo As String = EstraiUnita(cursore, unita, primaDelCursore)
            If unita = TextUnit.TextUnit_Character Then Return _descrittore.NomeCarattere(testo)
            testo = Pulisci(testo)
            If testo = "" Then Return "vuoto"
            If testo.Length = 1 Then Return _descrittore.NomeCarattere(testo)
            Return testo
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiUnita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function UnitaDi(codice As Integer, ctrl As Boolean) As TextUnit
        Select Case codice
            Case VK_UP, VK_DOWN, &H21, &H22
                If ctrl Then Return TextUnit.TextUnit_Paragraph
                Return TextUnit.TextUnit_Line
            Case &H23, &H24
                If ctrl Then Return TextUnit.TextUnit_Document
                Return TextUnit.TextUnit_Character
            Case VK_LEFT, VK_RIGHT
                If ctrl Then Return TextUnit.TextUnit_Word
                Return TextUnit.TextUnit_Character
            Case Else
                Return TextUnit.TextUnit_Character
        End Select
    End Function

    Private Function LeggiFrase(cursore As IUIAutomationTextRange) As String
        Try
            If cursore Is Nothing Then Return ""
            Dim prima As String = TestoVerso(cursore, True)
            Dim dopo As String = TestoVerso(cursore, False)
            If prima = "" AndAlso dopo = "" Then Return ""
            Dim inizio As Integer = 0
            Dim fine As Integer = -1
            For i As Integer = prima.Length - 1 To 0 Step -1
                Dim c As Char = prima(i)
                If c = "."c OrElse c = "!"c OrElse c = "?"c OrElse c = ChrW(&H2026) Then
                    If i = prima.Length - 1 OrElse Char.IsWhiteSpace(prima(i + 1)) Then
                        inizio = i + 1
                        Exit For
                    End If
                End If
            Next
            For i As Integer = 0 To dopo.Length - 1
                Dim c As Char = dopo(i)
                If c = "."c OrElse c = "!"c OrElse c = "?"c OrElse c = ChrW(&H2026) Then
                    If i = dopo.Length - 1 OrElse Char.IsWhiteSpace(dopo(i + 1)) Then
                        fine = i
                        Exit For
                    End If
                End If
            Next
            Dim frase As String = ""
            If inizio > 0 AndAlso fine >= 0 Then
                frase = prima.Substring(inizio) & dopo.Substring(0, fine + 1)
            ElseIf inizio > 0 Then
                frase = prima.Substring(inizio)
            ElseIf fine >= 0 Then
                frase = dopo.Substring(0, fine + 1)
            End If
            frase = Pulisci(frase)
            If frase = "" Then Return ""
            If frase.Length > 300 Then frase = frase.Substring(0, 300)
            Return _descrittore.TestoRiga(frase)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.LeggiFrase: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoEstremoDocumento(cursore As IUIAutomationTextRange, inizio As Boolean) As String
        Try
            Dim intervallo As IUIAutomationTextRange = cursore.Clone()
            Dim passi As Integer = If(inizio, -100000, 100000)
            intervallo.Move(TextUnit.TextUnit_Document, passi)
            intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
            Limita(intervallo)
            Dim testo As String = intervallo.GetText(4000)
            If String.IsNullOrWhiteSpace(testo) Then Return If(inizio, "inizio", "fine")
            Return _descrittore.TestoRiga(testo)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.TestoEstremoDocumento: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiUnita(cursore As IUIAutomationTextRange, unitaTesto As TextUnit, primaDelCursore As Boolean) As String
        Try
            Dim intervallo As IUIAutomationTextRange = cursore.Clone()
            Collassa(intervallo, True)
            Dim estremita As TextPatternRangeEndpoint = If(primaDelCursore, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            Dim passi As Integer = If(primaDelCursore, -1, 1)
            Dim spostati As Integer = intervallo.MoveEndpointByUnit(estremita, unitaTesto, passi)
            If spostati = 0 Then Return ""
            Limita(intervallo)
            Return intervallo.GetText(400)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.Unita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function DifferenzaCursore(precedente As IstantaneaCampo, attuale As IstantaneaCampo) As DifferenzaTesto
        Dim coda As DifferenzaTesto = DiffCoda(precedente.TestoPrima, attuale.TestoPrima)
        Dim testa As DifferenzaTesto = DiffTesta(precedente.TestoDopo, attuale.TestoDopo)
        If coda.Sconosciuta AndAlso testa.Sconosciuta Then
            Dim sconosciuta As New DifferenzaTesto()
            sconosciuta.Sconosciuta = True
            Return sconosciuta
        End If
        Dim codaFerma As Boolean = coda.Rimosso = "" AndAlso coda.Aggiunto = "" AndAlso Not coda.Sconosciuta
        Dim testaFerma As Boolean = testa.Rimosso = "" AndAlso testa.Aggiunto = "" AndAlso Not testa.Sconosciuta
        If Not codaFerma AndAlso testaFerma Then Return coda
        If codaFerma AndAlso Not testaFerma Then Return testa
        If coda.Aggiunto <> "" AndAlso coda.Rimosso = "" AndAlso testa.Rimosso <> "" AndAlso testa.Aggiunto = "" Then
            Return coda
        End If
        If coda.Rimosso <> "" AndAlso testa.Aggiunto <> "" AndAlso coda.Aggiunto = "" AndAlso testa.Rimosso = "" Then
            Return coda
        End If
        If coda.Rimosso <> "" OrElse coda.Aggiunto <> "" Then Return coda
        Return testa
    End Function

    Private Function DiffCoda(vecchio As String, nuovo As String) As DifferenzaTesto
        Dim esito As New DifferenzaTesto()
        If vecchio Is Nothing Then vecchio = ""
        If nuovo Is Nothing Then nuovo = ""
        If vecchio = nuovo Then Return esito
        If nuovo.StartsWith(vecchio, StringComparison.Ordinal) Then
            esito.Aggiunto = nuovo.Substring(vecchio.Length)
            Return esito
        End If
        If vecchio.StartsWith(nuovo, StringComparison.Ordinal) Then
            esito.Rimosso = vecchio.Substring(nuovo.Length)
            Return esito
        End If
        If vecchio.Length = nuovo.Length AndAlso vecchio.Length >= 8 Then
            Dim limite As Integer = Math.Min(120, vecchio.Length - 1)
            For k As Integer = 1 To limite
                If vecchio.Substring(k) = nuovo.Substring(0, nuovo.Length - k) Then
                    esito.Aggiunto = nuovo.Substring(nuovo.Length - k)
                    Return esito
                End If
                If nuovo.Substring(k) = vecchio.Substring(0, vecchio.Length - k) Then
                    esito.Rimosso = vecchio.Substring(vecchio.Length - k)
                    Return esito
                End If
            Next
        End If
        esito.Sconosciuta = True
        Return esito
    End Function

    Private Function DiffTesta(vecchio As String, nuovo As String) As DifferenzaTesto
        Dim esito As New DifferenzaTesto()
        If vecchio Is Nothing Then vecchio = ""
        If nuovo Is Nothing Then nuovo = ""
        If vecchio = nuovo Then Return esito
        If vecchio.Length > nuovo.Length AndAlso vecchio.EndsWith(nuovo, StringComparison.Ordinal) Then
            esito.Rimosso = vecchio.Substring(0, vecchio.Length - nuovo.Length)
            Return esito
        End If
        If nuovo.Length > vecchio.Length AndAlso nuovo.EndsWith(vecchio, StringComparison.Ordinal) Then
            esito.Aggiunto = nuovo.Substring(0, nuovo.Length - vecchio.Length)
            Return esito
        End If
        If vecchio.Length = nuovo.Length AndAlso vecchio.Length >= 8 Then
            Dim limite As Integer = Math.Min(120, vecchio.Length - 1)
            For k As Integer = 1 To limite
                If vecchio.Substring(k) = nuovo.Substring(0, nuovo.Length - k) Then
                    esito.Rimosso = vecchio.Substring(0, k)
                    Return esito
                End If
                If nuovo.Substring(k) = vecchio.Substring(0, vecchio.Length - k) Then
                    esito.Aggiunto = nuovo.Substring(0, k)
                    Return esito
                End If
            Next
        End If
        esito.Sconosciuta = True
        Return esito
    End Function

    Private Function DiffValore(vecchio As String, nuovo As String) As DifferenzaTesto
        Dim esito As New DifferenzaTesto()
        If vecchio Is Nothing Then vecchio = ""
        If nuovo Is Nothing Then nuovo = ""
        If vecchio = nuovo Then Return esito
        Dim i As Integer = 0
        Dim massimo As Integer = Math.Min(vecchio.Length, nuovo.Length)
        While i < massimo AndAlso vecchio(i) = nuovo(i)
            i += 1
        End While
        Dim j As Integer = 0
        While j < vecchio.Length - i AndAlso j < nuovo.Length - i AndAlso vecchio(vecchio.Length - 1 - j) = nuovo(nuovo.Length - 1 - j)
            j += 1
        End While
        If vecchio.Length - j - i > 0 Then esito.Rimosso = vecchio.Substring(i, vecchio.Length - j - i)
        If nuovo.Length - j - i > 0 Then esito.Aggiunto = nuovo.Substring(i, nuovo.Length - j - i)
        Return esito
    End Function

    Private Function FraseCancellazione(testo As String, protetto As Boolean, codice As Integer, ctrl As Boolean, maiuscolo As Boolean, Optional daSelezione As Boolean = False) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim taglia As Boolean = (ctrl AndAlso codice = VK_X) OrElse (maiuscolo AndAlso codice = VK_DELETE)
            If protetto Then
                If testo.Length = 1 Then Return "stella"
                If taglia Then Return "tagliato"
                Return If(daSelezione, "testo selezionato cancellato", "")
            End If
            If Not daSelezione AndAlso Not taglia Then testo = PezzoCancellato(testo, codice, ctrl)
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim soloACapo As String = testo.Trim(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2028), ChrW(&H2029))
            If soloACapo.Length = 0 Then Return "a capo"
            If testo.Length = 1 OrElse (testo.Length = 2 AndAlso Char.IsHighSurrogate(testo(0))) Then Return _descrittore.NomeCarattere(testo)
            If daSelezione Then Return If(taglia, "tagliato", "testo selezionato cancellato")
            If taglia Then
                Dim pulitoTaglia As String = testo.Trim()
                If pulitoTaglia.Length > 80 Then Return "tagliati " & pulitoTaglia.Length.ToString() & " caratteri"
                Return "tagliato, " & pulitoTaglia
            End If
            Dim pulito As String = testo.Trim()
            If pulito.Length = 0 Then Return _descrittore.NomeCarattere(testo.Substring(0, 1))
            If pulito.Length > 80 Then pulito = pulito.Substring(0, 80)
            Return _descrittore.TestoRiga(pulito)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.FraseCancellazione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PezzoCancellato(testo As String, codice As Integer, ctrl As Boolean) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.Length = 1 Then Return testo
            If codice <> VK_BACK AndAlso codice <> VK_DELETE Then Return testo
            If Not ctrl Then
                If codice = VK_BACK Then
                    If Char.IsHighSurrogate(testo(0)) AndAlso testo.Length >= 2 AndAlso Char.IsLowSurrogate(testo(1)) Then Return testo.Substring(0, 2)
                    Return testo.Substring(0, 1)
                End If
                Dim ultimo As Integer = testo.Length - 1
                If ultimo >= 1 AndAlso Char.IsLowSurrogate(testo(ultimo)) AndAlso Char.IsHighSurrogate(testo(ultimo - 1)) Then Return testo.Substring(ultimo - 1, 2)
                Return testo.Substring(ultimo, 1)
            End If
            Dim parole As String() = testo.Split(New Char() {" "c, ChrW(9), ChrW(10), ChrW(13), ChrW(160)}, StringSplitOptions.RemoveEmptyEntries)
            If parole.Length <= 1 Then Return testo
            Return If(codice = VK_BACK, parole(0), parole(parole.Length - 1))
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaCampi.PezzoCancellato", ex)
            Return testo
        End Try
    End Function

    Private Function FraseInserimento(testo As String, testoPrima As String, protetto As Boolean, ecoCaratteri As Boolean, ecoParole As Boolean, codice As Integer, ctrl As Boolean, maiuscolo As Boolean) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim incolla As Boolean = (ctrl AndAlso codice = VK_V) OrElse (maiuscolo AndAlso codice = &H2D)
            If protetto Then
                If Not ecoCaratteri AndAlso testo.Length = 1 AndAlso Not incolla Then Return ""
                If testo.Length = 1 Then Return "stella"
                Return "incollato"
            End If
            Dim fineParola As String = FraseFineParola(testo, testoPrima, ecoCaratteri, ecoParole, incolla)
            If fineParola <> "" Then Return fineParola
            Dim soloACapo As String = testo.Trim(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2028), ChrW(&H2029))
            If soloACapo.Length = 0 Then Return "a capo"
            Dim spazi As Boolean = testo.Trim().Length = 0
            If spazi Then Return "spazio"
            If testo.Length = 1 Then
                If Not ecoCaratteri AndAlso Not ecoParole Then Return ""
                Return _descrittore.NomeCarattere(testo)
            End If
            Dim pulito As String = testo.Trim()
            If pulito.Length = 0 Then
                If ecoCaratteri Then Return _descrittore.NomeCarattere(testo)
                Return ""
            End If
            If pulito.Length > 80 Then Return If(incolla, "incollati ", "inseriti ") & pulito.Length.ToString() & " caratteri"
            If incolla Then Return "incollato, " & pulito
            Return pulito
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.FraseInserimento: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function FraseFineParola(testo As String, testoPrima As String, ecoCaratteri As Boolean, ecoParole As Boolean, incolla As Boolean) As String
        Try
            If Not ecoParole OrElse incolla OrElse String.IsNullOrEmpty(testo) OrElse testo.Length > 4 Then Return ""
            Dim ultimo As Char = testo(testo.Length - 1)
            Dim aCapo As Boolean = ultimo = ChrW(13) OrElse ultimo = ChrW(10) OrElse ultimo = ChrW(11) OrElse ultimo = ChrW(&H2028) OrElse ultimo = ChrW(&H2029)
            Dim spazio As Boolean = Not aCapo AndAlso Char.IsWhiteSpace(ultimo)
            Dim segno As Boolean = Not aCapo AndAlso Not spazio AndAlso ChiudeParola(ultimo)
            If Not aCapo AndAlso Not spazio AndAlso Not segno Then Return ""
            Dim prima As String = If(testoPrima, "")
            If aCapo Then prima = prima.TrimEnd(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2028), ChrW(&H2029))
            If aCapo OrElse spazio Then
                Dim senzaSpazi As String = prima.TrimEnd(" "c, ChrW(9), ChrW(10), ChrW(13), ChrW(160))
                If senzaSpazi.Length > 0 AndAlso ChiudeParola(senzaSpazi(senzaSpazi.Length - 1)) Then Return ""
            End If
            Dim parola As String = ParolaDaDire(UltimaParola(prima))
            If parola = "" OrElse Not ConLettere(parola) Then Return ""
            If aCapo Then Return parola & " a capo"
            If Not ecoCaratteri Then Return parola
            If spazio Then Return parola & " spazio"
            Return parola & " " & _descrittore.NomeCarattere(ultimo.ToString())
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.FraseFineParola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ChiudeParola(carattere As Char) As Boolean
        Select Case carattere
            Case "."c, ","c, ";"c, ":"c, "!"c, "?"c, ")"c, "]"c, "}"c
                Return True
        End Select
        Return False
    End Function

    Private Shared Function ConLettere(testo As String) As Boolean
        For Each carattere As Char In If(testo, "")
            If Char.IsLetterOrDigit(carattere) Then Return True
        Next
        Return False
    End Function

    Private Function ParolaDaDire(parola As String) As String
        Try
            If String.IsNullOrEmpty(parola) Then Return ""
            Dim pulita As String = parola.TrimEnd(","c, ";"c, ":"c, "."c, "!"c, "?"c)
            If pulita = "" Then Return parola
            Return pulita
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.ParolaDaDire: " & ex.Message)
            Return If(parola, "")
        End Try
    End Function

    Private Function UltimaParola(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim pulito As String = testo.TrimEnd(" "c, ChrW(9), ChrW(10), ChrW(13), ChrW(160))
            If pulito.Length = 0 Then Return ""
            Dim separatori() As Char = {" "c, ChrW(9), ChrW(10), ChrW(13), ChrW(160)}
            Dim indice As Integer = pulito.LastIndexOfAny(separatori)
            Dim parola As String = If(indice < 0, pulito, pulito.Substring(indice + 1))
            If parola.Length > 80 Then Return ""
            Return parola
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.UltimaParola: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Pezzo(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return "vuoto"
            If testo.Length = 1 Then Return _descrittore.NomeCarattere(testo)
            If testo.Length >= 512 Then Return testo.Length.ToString() & " caratteri"
            Return _descrittore.TestoRiga(testo)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.Pezzo: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function PrevediSelezione(precedente As IstantaneaCampo, codice As Integer, ctrl As Boolean, ByRef aggiornata As IstantaneaCampo) As String
        aggiornata = Nothing
        Try
            If precedente Is Nothing Then Return ""
            If ctrl AndAlso codice = VK_A Then
                Dim tutto As String = TestoCompleto(precedente)
                aggiornata = precedente.Clona()
                aggiornata.HaCursore = True
                aggiornata.Selezione = tutto
                aggiornata.TestoPrima = ""
                aggiornata.TestoDopo = ""
                AllineaValore(aggiornata, precedente)
                If tutto = "" Then Return "selezionato tutto"
                Return MessaggioSelezione(tutto)
            End If
            If Not precedente.HaCursore Then Return ""
            Dim pezzoTesto As String = ""
            Dim avanti As Boolean = True
            Select Case codice
                Case VK_LEFT
                    avanti = False
                    pezzoTesto = If(ctrl, CodaParola(precedente.TestoPrima), Bordo(precedente.TestoPrima, True))
                Case VK_RIGHT
                    pezzoTesto = If(ctrl, TestaParola(precedente.TestoDopo), Bordo(precedente.TestoDopo, False))
                Case VK_HOME
                    avanti = False
                    pezzoTesto = If(ctrl, precedente.TestoPrima, DopoACapo(precedente.TestoPrima))
                Case VK_END
                    pezzoTesto = If(ctrl, precedente.TestoDopo, PrimaDiACapo(precedente.TestoDopo))
                Case Else
                    Return ""
            End Select
            If pezzoTesto = "" Then Return ""
            aggiornata = precedente.Clona()
            If avanti Then
                If Not precedente.TestoDopo.StartsWith(pezzoTesto, StringComparison.Ordinal) Then
                    aggiornata = Nothing
                    Return ""
                End If
                aggiornata.Selezione = precedente.Selezione & pezzoTesto
                aggiornata.TestoDopo = precedente.TestoDopo.Substring(pezzoTesto.Length)
            Else
                If Not precedente.TestoPrima.EndsWith(pezzoTesto, StringComparison.Ordinal) Then
                    aggiornata = Nothing
                    Return ""
                End If
                aggiornata.Selezione = pezzoTesto & precedente.Selezione
                aggiornata.TestoPrima = precedente.TestoPrima.Substring(0, precedente.TestoPrima.Length - pezzoTesto.Length)
            End If
            AllineaValore(aggiornata, precedente)
            LimitaFinestre(aggiornata)
            Return Pezzo(pezzoTesto) & " selezionato"
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.PrevediSelezione: " & ex.Message)
            aggiornata = Nothing
            Return ""
        End Try
    End Function

    Public Function PrevediCancellazione(precedente As IstantaneaCampo, codice As Integer, ctrl As Boolean, maiuscolo As Boolean, leggiPassword As Boolean, ByRef aggiornata As IstantaneaCampo) As String
        aggiornata = Nothing
        Try
            If precedente Is Nothing Then Return ""
            Dim protetto As Boolean = precedente.Protetto AndAlso Not leggiPassword
            Dim taglia As Boolean = (ctrl AndAlso codice = VK_X) OrElse (maiuscolo AndAlso codice = VK_DELETE)
            If precedente.Selezione <> "" AndAlso (codice = VK_BACK OrElse codice = VK_DELETE OrElse taglia) Then
                aggiornata = precedente.Clona()
                aggiornata.Selezione = ""
                AllineaValore(aggiornata, precedente)
                Return FraseCancellazione(precedente.Selezione, protetto, codice, ctrl, maiuscolo, True)
            End If
            If Not precedente.HaCursore Then
                Return ""
            End If
            Dim rimosso As String = ""
            Dim dallaCoda As Boolean = True
            If codice = VK_BACK Then
                rimosso = If(ctrl, CodaParola(precedente.TestoPrima), Bordo(precedente.TestoPrima, True))
            ElseIf codice = VK_DELETE Then
                rimosso = If(ctrl, TestaParola(precedente.TestoDopo), Bordo(precedente.TestoDopo, False))
                dallaCoda = False
            Else
                Return ""
            End If
            If rimosso = "" Then
                If codice = VK_BACK Then Return "inizio campo"
                If codice = VK_DELETE Then Return "fine campo"
                Return ""
            End If
            If protetto Then Return FraseCancellazione(rimosso, True, codice, ctrl, maiuscolo)
            aggiornata = precedente.Clona()
            If dallaCoda Then
                aggiornata.TestoPrima = precedente.TestoPrima.Substring(0, precedente.TestoPrima.Length - rimosso.Length)
            Else
                aggiornata.TestoDopo = precedente.TestoDopo.Substring(rimosso.Length)
            End If
            AllineaValore(aggiornata, precedente)
            LimitaFinestre(aggiornata)
            Return FraseCancellazione(rimosso, False, codice, ctrl, maiuscolo)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.PrevediCancellazione: " & ex.Message)
            aggiornata = Nothing
            Return ""
        End Try
    End Function

    Public Function FraseCancellazioneDaValori(precedente As String, attuale As String, protetto As Boolean, codice As Integer, ctrl As Boolean, maiuscolo As Boolean) As String
        Try
            If precedente Is Nothing Then precedente = ""
            If attuale Is Nothing Then attuale = ""
            If precedente = attuale Then Return ""
            Dim differenza As DifferenzaTesto = DiffValore(precedente, attuale)
            If differenza.Rimosso = "" Then Return ""
            Return FraseCancellazione(differenza.Rimosso, protetto, codice, ctrl, maiuscolo)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.FraseCancellazioneDaValori: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function PrevediInserimento(precedente As IstantaneaCampo, codice As Integer, maiuscolo As Boolean, ecoCaratteri As Boolean, ecoParole As Boolean, leggiPassword As Boolean, ByRef aggiornata As IstantaneaCampo) As String
        aggiornata = Nothing
        Try
            If precedente Is Nothing OrElse Not precedente.HaCursore Then Return ""
            Dim carattere As String = CarattereSemplice(codice, maiuscolo)
            If carattere = "" Then Return ""
            Dim protetto As Boolean = precedente.Protetto AndAlso Not leggiPassword
            If protetto Then Return FraseInserimento(carattere, precedente.TestoPrima, True, ecoCaratteri, ecoParole, codice, False, maiuscolo)
            aggiornata = precedente.Clona()
            Dim fraseRimossa As String = ""
            If precedente.Selezione <> "" Then
                fraseRimossa = FraseCancellazione(precedente.Selezione, False, VK_DELETE, False, False, True)
                aggiornata.Selezione = ""
            End If
            aggiornata.TestoPrima = aggiornata.TestoPrima & carattere
            AllineaValore(aggiornata, precedente)
            LimitaFinestre(aggiornata)
            Dim fraseNuova As String = FraseInserimento(carattere, aggiornata.TestoPrima, False, ecoCaratteri, ecoParole, codice, False, maiuscolo)
            If fraseRimossa = "" Then Return fraseNuova
            If fraseNuova = "" Then Return fraseRimossa
            Return fraseRimossa & ", " & fraseNuova
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.PrevediInserimento: " & ex.Message)
            aggiornata = Nothing
            Return ""
        End Try
    End Function

    Private Function TestoCompleto(istantanea As IstantaneaCampo) As String
        If istantanea Is Nothing Then Return ""
        Dim unito As String = istantanea.TestoPrima & istantanea.Selezione & istantanea.TestoDopo
        If istantanea.Valore <> "" AndAlso istantanea.Valore.Length >= unito.Length Then Return istantanea.Valore
        Return unito
    End Function

    Private Sub AllineaValore(destinazione As IstantaneaCampo, origine As IstantaneaCampo)
        If destinazione Is Nothing OrElse origine Is Nothing Then Return
        Dim vecchio As String = origine.TestoPrima & origine.Selezione & origine.TestoDopo
        Dim nuovo As String = destinazione.TestoPrima & destinazione.Selezione & destinazione.TestoDopo
        If origine.Valore = "" OrElse origine.Valore = vecchio Then
            destinazione.Valore = nuovo
            Return
        End If
        If origine.TestoPrima.Length < FinestraCursore AndAlso origine.Valore.StartsWith(vecchio, StringComparison.Ordinal) Then
            destinazione.Valore = nuovo & origine.Valore.Substring(vecchio.Length)
            Return
        End If
        If origine.TestoDopo.Length < FinestraCursore AndAlso vecchio.Length > 0 AndAlso origine.Valore.EndsWith(vecchio, StringComparison.Ordinal) Then
            destinazione.Valore = origine.Valore.Substring(0, origine.Valore.Length - vecchio.Length) & nuovo
            Return
        End If
        destinazione.Valore = origine.Valore
    End Sub

    Private Sub LimitaFinestre(istantanea As IstantaneaCampo)
        If istantanea Is Nothing Then Return
        If istantanea.TestoPrima IsNot Nothing AndAlso istantanea.TestoPrima.Length > FinestraCursore Then
            istantanea.TestoPrima = istantanea.TestoPrima.Substring(istantanea.TestoPrima.Length - FinestraCursore)
        End If
        If istantanea.TestoDopo IsNot Nothing AndAlso istantanea.TestoDopo.Length > FinestraCursore Then
            istantanea.TestoDopo = istantanea.TestoDopo.Substring(0, FinestraCursore)
        End If
    End Sub

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function ToUnicodeEx(tasto As UInteger, scansione As UInteger, stato As Byte(), <System.Runtime.InteropServices.Out()> testo As StringBuilder, lunghezza As Integer, opzioni As UInteger, layout As IntPtr) As Integer
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function MapVirtualKeyEx(codice As UInteger, tipo As UInteger, layout As IntPtr) As UInteger
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetKeyboardLayout(idThread As UInteger) As IntPtr
    End Function

    Private Function CarattereSemplice(codice As Integer, maiuscolo As Boolean) As String
        Try
            Dim grande As Boolean = maiuscolo Xor NativoLettore.BlocMaiuscoleAttivo()
            If codice >= &H41 AndAlso codice <= &H5A Then
                Dim lettera As String = ChrW(codice).ToString()
                If Not grande Then lettera = lettera.ToLowerInvariant()
                Return lettera
            End If
            If Not maiuscolo AndAlso codice >= &H30 AndAlso codice <= &H39 Then Return ChrW(codice).ToString()
            If codice >= &H60 AndAlso codice <= &H69 Then Return ChrW(&H30 + (codice - &H60)).ToString()
            If codice = VK_SPACE Then Return " "
            If (codice >= &HBA AndAlso codice <= &HC0) OrElse (codice >= &HDB AndAlso codice <= &HDF) OrElse codice = &HE2 OrElse (codice >= &H6A AndAlso codice <= &H6F) OrElse (maiuscolo AndAlso codice >= &H30 AndAlso codice <= &H39) Then
                Return CarattereDalLayout(codice, maiuscolo, False)
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.CarattereSemplice: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function CarattereDalLayout(codice As Integer, maiuscolo As Boolean, altGr As Boolean) As String
        Try
            Dim pid As UInteger = 0UI
            Dim thread As UInteger = NativoLettore.GetWindowThreadProcessId(NativoLettore.GetForegroundWindow(), pid)
            Dim layout As IntPtr = GetKeyboardLayout(thread)
            Dim stato(255) As Byte
            If maiuscolo Then
                stato(&H10) = &H80
                stato(&HA0) = &H80
            End If
            If NativoLettore.BlocMaiuscoleAttivo() Then stato(&H14) = 1
            If altGr Then
                stato(&H11) = &H80
                stato(&H12) = &H80
                stato(&HA2) = &H80
                stato(&HA5) = &H80
            End If
            Dim scansione As UInteger = MapVirtualKeyEx(CUInt(codice), 0UI, layout)
            Dim testo As New StringBuilder(8)
            Dim esito As Integer = ToUnicodeEx(CUInt(codice), scansione, stato, testo, testo.Capacity, 4UI, layout)
            If esito <> 1 Then Return ""
            Dim carattere As String = testo.ToString(0, 1)
            If carattere = "" OrElse Char.IsControl(carattere(0)) Then Return ""
            Return carattere
        Catch ex As Exception
            RegistroErrori.Scrivi("LetturaCampi.CarattereDalLayout", ex)
            Return ""
        End Try
    End Function

    Public Function EcoTasto(codice As Integer, maiuscolo As Boolean, altGr As Boolean, protetto As Boolean) As String
        Try
            Dim carattere As String = If(altGr, CarattereDalLayout(codice, maiuscolo, True), CarattereSemplice(codice, maiuscolo))
            If carattere = "" OrElse carattere = " " Then Return ""
            If Not altGr AndAlso Not Char.IsLetterOrDigit(carattere(0)) Then Return ""
            Return FraseInserimento(carattere, "", protetto, True, False, codice, False, maiuscolo)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.EcoTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Bordo(testo As String, dallaFine As Boolean) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        If dallaFine Then
            If testo.Length >= 2 AndAlso Char.IsLowSurrogate(testo(testo.Length - 1)) AndAlso Char.IsHighSurrogate(testo(testo.Length - 2)) Then
                Return testo.Substring(testo.Length - 2, 2)
            End If
            Return testo.Substring(testo.Length - 1, 1)
        End If
        If testo.Length >= 2 AndAlso Char.IsHighSurrogate(testo(0)) AndAlso Char.IsLowSurrogate(testo(1)) Then Return testo.Substring(0, 2)
        Return testo.Substring(0, 1)
    End Function

    Private Function CodaParola(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Dim indice As Integer = testo.Length - 1
        While indice >= 0 AndAlso Char.IsWhiteSpace(testo(indice))
            indice -= 1
        End While
        While indice >= 0 AndAlso Not Char.IsWhiteSpace(testo(indice))
            indice -= 1
        End While
        Return testo.Substring(indice + 1)
    End Function

    Private Function TestaParola(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Dim indice As Integer = 0
        While indice < testo.Length AndAlso Char.IsWhiteSpace(testo(indice))
            indice += 1
        End While
        While indice < testo.Length AndAlso Not Char.IsWhiteSpace(testo(indice))
            indice += 1
        End While
        If indice = 0 Then Return Bordo(testo, False)
        Return testo.Substring(0, indice)
    End Function

    Private Function DopoACapo(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Dim indice As Integer = Math.Max(testo.LastIndexOf(ChrW(10)), testo.LastIndexOf(ChrW(13)))
        If indice < 0 Then Return testo
        If indice >= testo.Length - 1 Then Return ""
        Return testo.Substring(indice + 1)
    End Function

    Private Function PrimaDiACapo(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Dim indiceA As Integer = testo.IndexOf(ChrW(10))
        Dim indiceR As Integer = testo.IndexOf(ChrW(13))
        Dim indice As Integer = indiceA
        If indice < 0 OrElse (indiceR >= 0 AndAlso indiceR < indice) Then indice = indiceR
        If indice < 0 Then Return testo
        If indice = 0 Then Return ""
        Return testo.Substring(0, indice)
    End Function

    Private Sub RaccogliSelezioneTesto(elemento As IUIAutomationElement, parti As List(Of String), salto As Integer)
        Try
            If elemento Is Nothing OrElse salto > 8 OrElse parti.Count >= 4 Then Return
            Dim pattern As IUIAutomationTextPattern = PatternTesto(elemento)
            If pattern IsNot Nothing Then
                Dim selezioni As IUIAutomationTextRangeArray = pattern.GetSelection()
                If selezioni IsNot Nothing Then
                    Dim limite As Integer = Math.Min(selezioni.Length, 4)
                    For indice As Integer = 0 To limite - 1
                        Dim testo As String = TestoNonCollassato(selezioni.GetElement(indice))
                        If testo <> "" Then parti.Add(testo)
                    Next
                End If
                If parti.Count > 0 Then Return
            End If
            If _walker IsNot Nothing AndAlso salto < 2 Then
                Dim figlio As IUIAutomationElement = _walker.GetFirstChildElement(elemento)
                Dim giri As Integer = 0
                While figlio IsNot Nothing AndAlso giri < 6 AndAlso parti.Count = 0
                    RaccogliSelezioneTesto(figlio, parti, salto + 1)
                    figlio = _walker.GetNextSiblingElement(figlio)
                    giri += 1
                End While
                If parti.Count > 0 Then Return
            End If
            If _walker Is Nothing OrElse salto >= 8 Then Return
            RaccogliSelezioneTesto(_walker.GetParentElement(elemento), parti, salto + 1)
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.RaccogliSelezioneTesto: " & ex.Message)
        End Try
    End Sub

    Private Function SelezioneElenco(elemento As IUIAutomationElement) As String
        Try
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 4
                If corrente Is Nothing Then Exit For
                Dim testo As String = NomiSelezionati(corrente)
                If testo <> "" Then Return testo
                If _walker Is Nothing Then Exit For
                corrente = _walker.GetParentElement(corrente)
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.SelezioneElenco: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomiSelezionati(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim pattern As IUIAutomationSelectionPattern = TryCast(elemento.GetCurrentPattern(SchemaSelezione), IUIAutomationSelectionPattern)
            If pattern Is Nothing Then Return ""
            Dim selezionati As IUIAutomationElementArray = pattern.GetCurrentSelection()
            If selezionati Is Nothing OrElse selezionati.Length = 0 Then Return ""
            Dim nomi As New List(Of String)()
            Dim limite As Integer = Math.Min(selezionati.Length, 8)
            For indice As Integer = 0 To limite - 1
                Dim nome As String = _descrittore.NomeCorrente(selezionati.GetElement(indice))
                If nome <> "" Then nomi.Add(nome)
            Next
            If nomi.Count = 0 Then Return ""
            Dim testo As String = String.Join(", ", nomi)
            If selezionati.Length > limite Then testo &= ", e altri"
            If nomi.Count = 1 Then Return "selezionato " & testo
            Return "selezionati " & testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.NomiSelezionati", exAtteso)
            Return ""
        End Try
    End Function

    Private Function SorgenteTesto(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            If HaCursoreTesto(elemento) Then Return elemento
            If _walker IsNot Nothing Then
                Dim figlio As IUIAutomationElement = _walker.GetFirstChildElement(elemento)
                Dim giri As Integer = 0
                While figlio IsNot Nothing AndAlso giri < 6
                    If HaCursoreTesto(figlio) Then Return figlio
                    figlio = _walker.GetNextSiblingElement(figlio)
                    giri += 1
                End While
                Dim corrente As IUIAutomationElement = elemento
                For salto As Integer = 1 To 8
                    corrente = _walker.GetParentElement(corrente)
                    If corrente Is Nothing Then Exit For
                    If HaCursoreTesto(corrente) Then Return corrente
                Next
            End If
            If _descrittore.HaValore(elemento) OrElse _descrittore.HaPatternTesto(elemento) Then Return elemento
            Return Nothing
        Catch ex As System.Runtime.InteropServices.COMException
            RegistroErrori.Scrivi("LetturaCampi.SorgenteTesto", ex)
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.Origine: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function HaCursoreTesto(elemento As IUIAutomationElement) As Boolean
        Try
            If _descrittore.HaPatternTesto(elemento) Then Return True
            Return PatternTesto(elemento) IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.HaCursoreTesto", exAtteso)
            Return False
        End Try
    End Function

    Private Function ProvaEditNativo(elemento As IUIAutomationElement, istantanea As IstantaneaCampo) As Boolean
        Try
            If elemento Is Nothing OrElse istantanea Is Nothing OrElse istantanea.HaCursore Then Return False
            Dim finestra As IntPtr = IntPtr.Zero
            Try
                finestra = elemento.CurrentNativeWindowHandle
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaCampi.ProvaEditNativo", exAtteso)
                Return False
            End Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As New StringBuilder(128)
            NativoLettore.GetClassName(finestra, classe, classe.Capacity)
            Dim nomeClasse As String = classe.ToString()
            Dim edit As Boolean = nomeClasse.Equals("Edit", StringComparison.OrdinalIgnoreCase) OrElse nomeClasse.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
            If Not edit Then Return False
            Dim inizio As Integer = 0
            Dim fine As Integer = 0
            If Not NativoLettore.LeggiSelezioneEdit(finestra, inizio, fine) Then Return False
            Dim testo As String = NativoLettore.LeggiTestoFinestra(finestra, MassimoValore)
            If testo Is Nothing Then testo = ""
            If inizio > testo.Length Then inizio = testo.Length
            If fine < inizio Then fine = inizio
            If fine > testo.Length Then fine = testo.Length
            Dim da As Integer = Math.Max(0, inizio - FinestraCursore)
            istantanea.HaCursore = True
            istantanea.TestoPrima = testo.Substring(da, inizio - da)
            Dim dopo As Integer = Math.Min(FinestraCursore, testo.Length - fine)
            If dopo < 0 Then dopo = 0
            istantanea.TestoDopo = testo.Substring(fine, dopo)
            istantanea.Selezione = testo.Substring(inizio, fine - inizio)
            istantanea.Valore = testo
            Return True
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.ProvaEditNativo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function SelezioneNativa(elemento As IUIAutomationElement) As String
        Try
            Dim istantanea As New IstantaneaCampo()
            If Not ProvaEditNativo(elemento, istantanea) Then Return ""
            Return istantanea.Selezione
        Catch ex As Exception
            log.ScriviLog("LetturaCampi.SelezioneNativa: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function PatternTesto(elemento As IUIAutomationElement) As IUIAutomationTextPattern
        Try
            If elemento Is Nothing Then Return Nothing
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern IsNot Nothing Then Return pattern
            Return TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto2), IUIAutomationTextPattern)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.PatternTesto", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function IntervalloCursore(elemento As IUIAutomationElement) As IUIAutomationTextRange
        Try
            Dim pattern As IUIAutomationTextPattern = PatternTesto(elemento)
            If pattern IsNot Nothing Then
                Dim selezioni As IUIAutomationTextRangeArray = pattern.GetSelection()
                If selezioni IsNot Nothing AndAlso selezioni.Length > 0 Then Return selezioni.GetElement(0)
            End If
            Dim pattern2 As IUIAutomationTextPattern2 = Nothing
            Try
                pattern2 = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto2), IUIAutomationTextPattern2)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaCampi.IntervalloCursore", exAtteso)
                pattern2 = Nothing
            End Try
            If pattern2 Is Nothing Then Return Nothing
            Dim attivo As Integer = 0
            Return pattern2.GetCaretRange(attivo)
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("LetturaCampi.IntervalloCursore", exAtteso2)
            Return Nothing
        End Try
    End Function

    Private Function LeggiValore(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim pattern As IUIAutomationValuePattern = TryCast(elemento.GetCurrentPattern(SchemaValore), IUIAutomationValuePattern)
            If pattern Is Nothing Then Return ""
            If pattern.CurrentIsReadOnly <> 0 AndAlso pattern.CurrentValue Is Nothing Then Return ""
            Dim valore As String = pattern.CurrentValue
            If valore Is Nothing Then Return ""
            Return valore
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.LeggiValore", exAtteso)
            Return ""
        End Try
    End Function

    Private Function TestoNonCollassato(intervallo As IUIAutomationTextRange) As String
        Try
            If intervallo Is Nothing Then Return ""
            If intervallo.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) = 0 Then Return ""
            Dim testo As String = intervallo.GetText(-1)
            If testo Is Nothing Then Return ""
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.TestoNonCollassato", exAtteso)
            Return ""
        End Try
    End Function

    Private Function TestoVerso(cursore As IUIAutomationTextRange, versoInizio As Boolean) As String
        Try
            Dim intervallo As IUIAutomationTextRange = cursore.Clone()
            If versoInizio Then
                Collassa(intervallo, True)
                intervallo.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -FinestraCursore)
            Else
                Collassa(intervallo, False)
                intervallo.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, FinestraCursore)
            End If
            Limita(intervallo)
            Dim testo As String = intervallo.GetText(FinestraCursore + 20)
            If testo Is Nothing Then Return ""
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.TestoVerso", exAtteso)
            Return ""
        End Try
    End Function

    Private Sub ImpostaLimite(elemento As IUIAutomationElement, origine As IUIAutomationElement)
        _limite = Nothing
        Try
            If elemento Is Nothing OrElse origine Is Nothing Then Return
            Dim pattern As IUIAutomationTextPattern = PatternTesto(origine)
            If pattern Is Nothing Then Return
            Dim stesso As Boolean = False
            Try
                stesso = _automazione.CompareElements(elemento, origine) <> 0
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("LetturaCampi.ImpostaLimite", exAtteso)
                stesso = False
            End Try
            If Not stesso Then
                Try
                    _limite = pattern.RangeFromChild(elemento)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("LetturaCampi.ImpostaLimite", exAtteso2)
                    _limite = Nothing
                End Try
                If _limite IsNot Nothing Then Return
            End If
            _limite = pattern.DocumentRange
        Catch ex As Exception
            _limite = Nothing
            log.ScriviLog("LetturaCampi.ImpostaLimite: " & ex.Message)
        End Try
    End Sub

    Private Sub Limita(intervallo As IUIAutomationTextRange)
        Try
            If intervallo Is Nothing OrElse _limite Is Nothing Then Return
            If intervallo.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, _limite, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0 Then
                intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, _limite, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            End If
            If intervallo.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, _limite, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) > 0 Then
                intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, _limite, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaCampi.Limita", exAtteso)
        End Try
    End Sub

    Private Sub Collassa(intervallo As IUIAutomationTextRange, allInizio As Boolean)
        If allInizio Then
            intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
        Else
            intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
        End If
    End Sub

    Private Function Pulisci(testo As String) As String
        If testo Is Nothing Then Return ""
        Return testo.Trim(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2028), ChrW(&H2029), " "c)
    End Function

End Class