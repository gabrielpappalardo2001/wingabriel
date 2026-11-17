Imports System.Collections.Generic
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports UIAutomationClient

' Modulo di supporto per Microsoft Excel, che gestisce la lettura delle celle, delle formule, dei titoli di riga e colonna e della
' selezione.
Friend NotInheritable Class ExcelAppModule
    Inherits AppModuleLettore

    Private Const OggettoNativo As Integer = &HFFFFFFF0
    Private Const NessunoExcel As Integer = -4142
    Private Const AutomaticoExcel As Integer = -4105
    Private Const AttesaMassima As Integer = 400
    Private Const PassoAttesa As Integer = 10
    Private Const ValiditaPrecedente As Integer = 4000
    Private Const AttesaFuocoDopoTasto As Integer = 700
    Private Const IdModificaCella As String = "CellEdit"
    Private Const ValiditaNomi As Integer = 5000
    Private Const MassimoCelle As Integer = 16
    Private Const MassimoNomi As Integer = 200

    <StructLayout(LayoutKind.Sequential)>
    Private Structure InfoThreadGui
        Public Dimensione As Integer
        Public Flag As Integer
        Public Attiva As IntPtr
        Public Fuoco As IntPtr
        Public Cattura As IntPtr
        Public Menu As IntPtr
        Public Spostamento As IntPtr
        Public Cursore As IntPtr
        Public Sinistra As Integer
        Public Alto As Integer
        Public Destra As Integer
        Public Basso As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function GetGUIThreadInfo(idThread As UInteger, ByRef info As InfoThreadGui) As Boolean
    End Function

    <DllImport("oleacc.dll")>
    Private Shared Function AccessibleObjectFromWindow(finestra As IntPtr, oggetto As Integer, ByRef iid As Guid, <MarshalAs(UnmanagedType.IDispatch)> ByRef risultato As Object) As Integer
    End Function

    Private NotInheritable Class StatoCella
        Public Foglio As String = ""
        Public Indirizzo As String = ""
        Public Riga As Integer
        Public Colonna As Integer
        Public Selezione As String = ""
        Public Celle As Long
    End Class

    Private NotInheritable Class NomeTitolo
        Public Foglio As String = ""
        Public Riga As Integer
        Public Colonna As Integer
        Public Colonne As Boolean
        Public Righe As Boolean
    End Class

    Private ReadOnly _blocco As New Object()
    Private _finestraOm As Object
    Private _maniglia As IntPtr
    Private _threadOm As Integer
    Private _ultimo As StatoCella
    Private _ultimoTick As Integer
    Private _titoli As New List(Of NomeTitolo)()
    Private _titoliTick As Integer
    Private _titoliCartella As String = ""
    Private _commentiModerni As Boolean = True
    Private _modifica As IUIAutomationElement
    Private _fuocoNelFoglio As Boolean
    Private _ultimoDaFuoco As Boolean
    Private _foglioDetto As String = ""
    Private _regioneChiave As String = ""
    Private _regioneTick As Integer
    Private _regioneColonne As Boolean
    Private _regioneRighe As Boolean

    Public Sub New(lettore As WGLettoreSchermo)
        MyBase.New(lettore, "excel")
    End Sub

    Public Overrides Function PrimoPianoIgnorato(finestra As IntPtr) As Boolean
        Try
            SyncLock _blocco
                _ultimo = Nothing
                _fuocoNelFoglio = False
            End SyncLock
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.PrimoPianoIgnorato", ex)
            Return False
        End Try
    End Function

    Public Overrides Function FocusMsaaIgnorato(finestra As IntPtr) As Boolean
        Try
            Dim classe As String = ControlliNativi.ClasseFinestra(finestra)
            If String.Equals(classe, "EXCEL6", StringComparison.Ordinal) Then Return True
            If Not String.Equals(classe, "EXCEL7", StringComparison.Ordinal) Then Return False
            Return FoglioGestito()
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FocusMsaaIgnorato", ex)
            Return False
        End Try
    End Function

    Public Overrides Function EventoSoppresso(elemento As IUIAutomationElement) As Boolean
        Try
            Return FoglioGestito()
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.EventoSoppresso", ex)
            Return False
        End Try
    End Function

    Public Overrides Function ReindirizzaFuoco(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            Return CellaInModifica(elemento)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.ReindirizzaFuoco", ex)
            Return Nothing
        End Try
    End Function

    Public Overrides Function ElementoTasto(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            Return CellaInModifica(elemento)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.ElementoTasto", ex)
            Return Nothing
        End Try
    End Function

    Public Overrides Function AllFocusSoppresso(elemento As IUIAutomationElement) As Boolean
        Try
            RicordaModifica(elemento)
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then
                SyncLock _blocco
                    _ultimo = Nothing
                    _fuocoNelFoglio = False
                End SyncLock
                Return False
            End If
            Dim veniviDalFoglio As Boolean = _fuocoNelFoglio
            _fuocoNelFoglio = True
            If Not veniviDalFoglio Then Return False
            If Environment.TickCount - Lettore.TickUltimoTastoApp < AttesaFuocoDopoTasto Then Return FoglioGestito()
            SyncLock _blocco
                If _ultimo Is Nothing Then Return False
                Dim stato As StatoCella = LeggiStato(FinestraExcel(maniglia))
                If stato Is Nothing Then Return False
                Return Not Cambiato(_ultimo, stato)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AllFocusSoppresso", ex)
            Return False
        End Try
    End Function

    Public Overrides Function AllFocus(elemento As IUIAutomationElement, testoBase As String, ultimoElemento As IUIAutomationElement) As String
        Try
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return testoBase
            SyncLock _blocco
                Dim finestra As Object = FinestraExcel(maniglia)
                Dim stato As StatoCella = LeggiStato(finestra)
                If stato Is Nothing Then Return testoBase
                Dim precedente As StatoCella = Nothing
                If Environment.TickCount - _ultimoTick < ValiditaPrecedente Then precedente = _ultimo
                Dim frase As String = Componi(finestra, stato, precedente)
                _ultimo = stato
                _ultimoTick = Environment.TickCount
                _ultimoDaFuoco = True
                If frase = "" Then Return testoBase
                Return frase
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AllFocus", ex)
            Return testoBase
        End Try
    End Function

    Public Overrides Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If Not TastoDelFoglio(evento) Then Return False
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return False
            Dim tickTasto As Integer = Lettore.TickUltimoTastoApp
            Dim inizio As Integer = Environment.TickCount
            SyncLock _blocco
                Dim precedente As StatoCella = _ultimo
                If precedente IsNot Nothing AndAlso _ultimoDaFuoco AndAlso _ultimoTick - tickTasto >= 0 Then
                    Dim giaDetto As StatoCella = LeggiStato(FinestraExcel(maniglia))
                    If giaDetto Is Nothing Then Return False
                    If Not Cambiato(precedente, giaDetto) Then Return True
                End If
                If precedente Is Nothing Then Thread.Sleep(PassoAttesa * 3)
                Do
                    Dim finestra As Object = FinestraExcel(maniglia)
                    Dim stato As StatoCella = LeggiStato(finestra)
                    If stato Is Nothing Then Return False
                    If Cambiato(precedente, stato) Then
                        Annuncia(finestra, stato, precedente)
                        Return True
                    End If
                    If Lettore.TickUltimoTastoApp <> tickTasto Then Return True
                    If Environment.TickCount - inizio >= AttesaMassima Then
                        Annuncia(finestra, stato, precedente)
                        Return True
                    End If
                    Thread.Sleep(PassoAttesa)
                Loop
            End SyncLock
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AllTasto", ex)
            Return False
        End Try
    End Function

    Public Overrides Function FormatoApp(elemento As IUIAutomationElement) As String
        Try
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return ""
            SyncLock _blocco
                Dim finestra As Object = FinestraExcel(maniglia)
                If finestra Is Nothing Then Return ""
                Dim cella As Object = finestra.ActiveCell
                If cella Is Nothing Then Return ""
                Dim parti As New List(Of String)()
                AggiungiCarattere(cella, parti)
                AggiungiAspetto(cella, parti)
                AggiungiContenuto(cella, parti)
                Return String.Join(", ", parti)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FormatoApp", ex)
            Dimentica()
            Return ""
        End Try
    End Function

    Public Overrides Function RigaApp(elemento As IUIAutomationElement) As String
        Try
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return ""
            SyncLock _blocco
                Dim finestra As Object = FinestraExcel(maniglia)
                Dim stato As StatoCella = LeggiStato(finestra)
                If stato Is Nothing Then Return ""
                Dim foglio As Object = finestra.ActiveSheet
                Dim regione As Object = finestra.ActiveCell.CurrentRegion
                Dim primaColonna As Integer = CInt(regione.Column)
                Dim colonne As Integer = CInt(regione.Columns.Count)
                Dim limite As Integer = Math.Min(colonne, MassimoCelle)
                Dim testo As New StringBuilder()
                testo.Append("riga ").Append(stato.Riga)
                For indice As Integer = 0 To limite - 1
                    Dim colonna As Integer = primaColonna + indice
                    Dim cella As Object = foglio.Cells(stato.Riga, colonna)
                    Dim valore As String = TestoCella(cella)
                    Dim titolo As String = TitoloColonna(finestra, cella, stato.Foglio, stato.Riga, colonna)
                    testo.Append(", ")
                    If titolo <> "" AndAlso Not String.Equals(titolo, valore, StringComparison.CurrentCulture) Then testo.Append(titolo).Append(" ")
                    testo.Append(If(valore = "", "vuota", valore))
                Next
                If colonne > limite Then testo.Append(", altre colonne")
                Return testo.ToString()
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.RigaApp", ex)
            Dimentica()
            Return ""
        End Try
    End Function

    Public Overrides Function ColonnaApp(elemento As IUIAutomationElement) As String
        Try
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return ""
            SyncLock _blocco
                Dim finestra As Object = FinestraExcel(maniglia)
                Dim stato As StatoCella = LeggiStato(finestra)
                If stato Is Nothing Then Return ""
                Dim foglio As Object = finestra.ActiveSheet
                Dim regione As Object = finestra.ActiveCell.CurrentRegion
                Dim primaRiga As Integer = CInt(regione.Row)
                Dim righe As Integer = CInt(regione.Rows.Count)
                Dim limite As Integer = Math.Min(righe, MassimoCelle)
                Dim testo As New StringBuilder()
                testo.Append("colonna ").Append(LetteraColonna(stato.Indirizzo))
                For indice As Integer = 0 To limite - 1
                    Dim riga As Integer = primaRiga + indice
                    Dim cella As Object = foglio.Cells(riga, stato.Colonna)
                    Dim valore As String = TestoCella(cella)
                    Dim titolo As String = TitoloRiga(finestra, cella, stato.Foglio, riga, stato.Colonna)
                    testo.Append(", ")
                    If titolo <> "" AndAlso Not String.Equals(titolo, valore, StringComparison.CurrentCulture) Then testo.Append(titolo).Append(" ")
                    testo.Append(If(valore = "", "vuota", valore))
                Next
                If righe > limite Then testo.Append(", altre righe")
                Return testo.ToString()
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.ColonnaApp", ex)
            Dimentica()
            Return ""
        End Try
    End Function

    Private Shared Function TastoDelFoglio(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing OrElse evento.Alt Then Return False
            Select Case evento.Azione
                Case AzioneTastoLettore.Navigazione, AzioneTastoLettore.Selezione
                    Return True
                Case AzioneTastoLettore.SoloModulo
                    Return evento.CodiceTasto = &H9 AndAlso Not evento.Ctrl
                Case AzioneTastoLettore.Modifica
                    If evento.Ctrl Then Return evento.CodiceTasto = &H5A OrElse evento.CodiceTasto = &H59
                    Return evento.CodiceTasto = &HD OrElse evento.CodiceTasto = &H2E
            End Select
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TastoDelFoglio", ex)
            Return False
        End Try
    End Function

    Private Sub Annuncia(finestra As Object, stato As StatoCella, precedente As StatoCella)
        Try
            Dim frase As String = Componi(finestra, stato, precedente)
            _ultimo = stato
            _ultimoTick = Environment.TickCount
            _ultimoDaFuoco = False
            If frase <> "" Then Lettore.ParlaApp(frase, True)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.Annuncia", ex)
        End Try
    End Sub

    Private Function Componi(finestra As Object, stato As StatoCella, precedente As StatoCella) As String
        Try
            Dim frase As String
            If stato.Celle > 1 Then
                frase = FraseSelezione(stato)
            Else
                frase = FraseCella(finestra, stato, precedente)
            End If
            If _foglioDetto <> "" AndAlso Not String.Equals(_foglioDetto, stato.Foglio, StringComparison.Ordinal) Then
                frase = "foglio " & stato.Foglio & If(frase = "", "", ", " & frase)
            End If
            _foglioDetto = stato.Foglio
            Return frase
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.Componi", ex)
            Return ""
        End Try
    End Function

    Private Function FraseCella(finestra As Object, stato As StatoCella, precedente As StatoCella) As String
        Try
            Dim cella As Object = finestra.ActiveCell
            If cella Is Nothing Then Return ""
            Dim parti As New List(Of String)()
            Dim valore As String = TestoCella(cella)
            parti.Add(If(valore = "", "vuota", valore))
            parti.Add(stato.Indirizzo)
            Dim stessoFoglio As Boolean = precedente IsNot Nothing AndAlso String.Equals(precedente.Foglio, stato.Foglio, StringComparison.Ordinal)
            Dim nuovaColonna As Boolean = Not stessoFoglio OrElse precedente.Colonna <> stato.Colonna
            Dim nuovaRiga As Boolean = Not stessoFoglio OrElse precedente.Riga <> stato.Riga
            If nuovaColonna Then
                Dim titolo As String = TitoloColonna(finestra, cella, stato.Foglio, stato.Riga, stato.Colonna)
                If titolo <> "" AndAlso Not String.Equals(titolo, valore, StringComparison.CurrentCulture) Then parti.Add("colonna " & titolo)
            End If
            If nuovaRiga Then
                Dim titolo As String = TitoloRiga(finestra, cella, stato.Foglio, stato.Riga, stato.Colonna)
                If titolo <> "" AndAlso Not String.Equals(titolo, valore, StringComparison.CurrentCulture) Then parti.Add("riga " & titolo)
            End If
            AggiungiSegnali(cella, parti)
            Return String.Join(", ", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FraseCella", ex)
            Return ""
        End Try
    End Function

    Private Shared Function FraseSelezione(stato As StatoCella) As String
        Try
            Dim celle As String = If(stato.Celle = 1L, "1 cella selezionata", stato.Celle.ToString(CultureInfo.CurrentCulture) & " celle selezionate")
            Return LeggiIntervallo(stato.Selezione) & ", " & celle
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FraseSelezione", ex)
            Return ""
        End Try
    End Function

    Private Sub AggiungiSegnali(cella As Object, parti As List(Of String))
        Try
            If Booleano(cella.HasFormula) Then parti.Add("formula")
            If HaCommento(cella) Then parti.Add("commento")
            If Booleano(cella.MergeCells) Then
                Dim area As String = Convert.ToString(cella.MergeArea.Address(False, False))
                parti.Add("celle unite " & LeggiIntervallo(area))
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AggiungiSegnali", ex)
        End Try
    End Sub

    Private Sub AggiungiCarattere(cella As Object, parti As List(Of String))
        Try
            Dim carattere As Object = cella.Font
            If carattere Is Nothing Then Return
            Dim nome As String = Convert.ToString(carattere.Name)
            If nome <> "" Then parti.Add(nome)
            Dim dimensione As Object = carattere.Size
            If TypeOf dimensione Is Double OrElse TypeOf dimensione Is Single OrElse TypeOf dimensione Is Integer Then
                parti.Add(Math.Round(Convert.ToDouble(dimensione), 1).ToString("0.#", CultureInfo.CurrentCulture) & " punti")
            End If
            If Booleano(carattere.Bold) Then parti.Add("grassetto")
            If Booleano(carattere.Italic) Then parti.Add("corsivo")
            Dim sottolineato As Integer = Intero(carattere.Underline, NessunoExcel)
            If sottolineato = -4119 Then
                parti.Add("sottolineato doppio")
            ElseIf sottolineato <> NessunoExcel AndAlso sottolineato <> 0 Then
                parti.Add("sottolineato")
            End If
            If Booleano(carattere.Strikethrough) Then parti.Add("barrato")
            If Booleano(carattere.Superscript) Then parti.Add("apice")
            If Booleano(carattere.Subscript) Then parti.Add("pedice")
            Dim colore As Integer = Intero(carattere.Color, -1)
            If colore >= 0 Then
                Dim nomeColore As String = LetturaAttributi.NomeColore(colore)
                If nomeColore <> "" Then parti.Add("colore " & nomeColore)
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AggiungiCarattere", ex)
        End Try
    End Sub

    Private Sub AggiungiAspetto(cella As Object, parti As List(Of String))
        Try
            Dim interno As Object = cella.Interior
            If interno IsNot Nothing Then
                Dim indice As Integer = Intero(interno.ColorIndex, NessunoExcel)
                If indice <> NessunoExcel AndAlso indice <> AutomaticoExcel Then
                    Dim sfondo As Integer = Intero(interno.Color, -1)
                    If sfondo >= 0 Then
                        Dim nomeSfondo As String = LetturaAttributi.NomeColore(sfondo)
                        If nomeSfondo <> "" Then parti.Add("sfondo " & nomeSfondo)
                    End If
                End If
            End If
            Dim allineamento As String = NomeAllineamento(Intero(cella.HorizontalAlignment, 1))
            If allineamento <> "" Then parti.Add(allineamento)
            If Booleano(cella.WrapText) Then parti.Add("testo a capo")
            Dim rientro As Integer = Intero(cella.IndentLevel, 0)
            If rientro > 0 Then parti.Add("rientro " & rientro.ToString(CultureInfo.CurrentCulture))
            Dim formato As String = Convert.ToString(cella.NumberFormatLocal)
            If formato <> "" AndAlso Not String.Equals(formato, "Generale", StringComparison.OrdinalIgnoreCase) AndAlso Not String.Equals(formato, "General", StringComparison.OrdinalIgnoreCase) Then
                parti.Add("formato " & formato)
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AggiungiAspetto", ex)
        End Try
    End Sub

    Private Sub AggiungiContenuto(cella As Object, parti As List(Of String))
        Try
            If Booleano(cella.HasFormula) Then
                Dim formula As String = Convert.ToString(cella.FormulaLocal)
                If formula <> "" Then parti.Add("formula " & formula)
            End If
            If Booleano(cella.MergeCells) Then
                Dim area As String = Convert.ToString(cella.MergeArea.Address(False, False))
                parti.Add("celle unite " & LeggiIntervallo(area))
            End If
            Dim commento As String = TestoCommento(cella)
            If commento <> "" Then parti.Add("commento " & commento)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AggiungiContenuto", ex)
        End Try
    End Sub

    Private Shared Function NomeAllineamento(valore As Integer) As String
        Select Case valore
            Case -4131
                Return "allineato a sinistra"
            Case -4108
                Return "centrato"
            Case -4152
                Return "allineato a destra"
            Case -4130
                Return "giustificato"
            Case 7
                Return "centrato nelle colonne"
            Case 5
                Return "riempimento"
            Case -4117
                Return "distribuito"
        End Select
        Return ""
    End Function

    Private Function HaCommento(cella As Object) As Boolean
        Try
            Dim nota As Object = cella.Comment
            If nota IsNot Nothing Then Return True
            Return DiscussioneCella(cella) IsNot Nothing
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.HaCommento", ex)
            Return False
        End Try
    End Function

    Private Function TestoCommento(cella As Object) As String
        Try
            Dim nota As Object = cella.Comment
            If nota IsNot Nothing Then Return Convert.ToString(nota.Text()).Trim()
            Dim discussione As Object = DiscussioneCella(cella)
            If discussione IsNot Nothing Then Return Convert.ToString(discussione.Text()).Trim()
            Return ""
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TestoCommento", ex)
            Return ""
        End Try
    End Function

    Private Function DiscussioneCella(cella As Object) As Object
        If Not _commentiModerni Then Return Nothing
        Try
            Return cella.CommentThreaded
        Catch exAssente As MissingMemberException
            _commentiModerni = False
            RegistroErrori.Scrivi("ExcelAppModule.DiscussioneCella", exAssente)
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.DiscussioneCella", ex)
            Return Nothing
        End Try
    End Function

    Private Function TitoloColonna(finestra As Object, cella As Object, nomeFoglio As String, riga As Integer, colonna As Integer) As String
        Try
            Dim foglio As Object = finestra.ActiveSheet
            Dim nome As NomeTitolo = TitoloDaNomi(finestra, nomeFoglio, riga, colonna, True)
            If nome IsNot Nothing Then Return TestoCella(foglio.Cells(nome.Riga, colonna))
            Dim tabella As Object = cella.ListObject
            If tabella IsNot Nothing Then
                Dim intestazione As Object = tabella.HeaderRowRange
                If intestazione Is Nothing Then Return ""
                Dim rigaTitoli As Integer = CInt(intestazione.Row)
                If riga <= rigaTitoli Then Return ""
                Return TestoCella(foglio.Cells(rigaTitoli, colonna))
            End If
            Dim regione As Object = cella.CurrentRegion
            Dim primaRiga As Integer = CInt(regione.Row)
            If riga <= primaRiga Then Return ""
            If Not RegioneConTitoli(foglio, regione, nomeFoglio, True) Then Return ""
            Dim titolo As Object = foglio.Cells(primaRiga, colonna)
            If Not TypeOf titolo.Value2 Is String Then Return ""
            Return TestoCella(titolo)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TitoloColonna", ex)
            Return ""
        End Try
    End Function

    Private Function TitoloRiga(finestra As Object, cella As Object, nomeFoglio As String, riga As Integer, colonna As Integer) As String
        Try
            Dim foglio As Object = finestra.ActiveSheet
            Dim nome As NomeTitolo = TitoloDaNomi(finestra, nomeFoglio, riga, colonna, False)
            If nome IsNot Nothing Then Return TestoCella(foglio.Cells(riga, nome.Colonna))
            If cella.ListObject IsNot Nothing Then Return ""
            Dim regione As Object = cella.CurrentRegion
            Dim primaColonna As Integer = CInt(regione.Column)
            If colonna <= primaColonna Then Return ""
            Dim primaRiga As Integer = CInt(regione.Row)
            If riga <= primaRiga Then Return ""
            If Not RegioneConTitoli(foglio, regione, nomeFoglio, False) Then Return ""
            Dim titolo As Object = foglio.Cells(riga, primaColonna)
            If Not TypeOf titolo.Value2 Is String Then Return ""
            Return TestoCella(titolo)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TitoloRiga", ex)
            Return ""
        End Try
    End Function

    Private Function RegioneConTitoli(foglio As Object, regione As Object, nomeFoglio As String, colonne As Boolean) As Boolean
        Try
            Dim chiave As String = nomeFoglio & "!" & Convert.ToString(regione.Address(False, False))
            If Not String.Equals(chiave, _regioneChiave, StringComparison.Ordinal) OrElse Environment.TickCount - _regioneTick > ValiditaNomi Then
                EsaminaRegione(foglio, regione)
                _regioneChiave = chiave
                _regioneTick = Environment.TickCount
            End If
            Return If(colonne, _regioneColonne, _regioneRighe)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.RegioneConTitoli", ex)
            Return False
        End Try
    End Function

    Private Sub EsaminaRegione(foglio As Object, regione As Object)
        _regioneColonne = False
        _regioneRighe = False
        Try
            Dim primaRiga As Integer = CInt(regione.Row)
            Dim primaColonna As Integer = CInt(regione.Column)
            Dim righe As Integer = CInt(regione.Rows.Count)
            Dim colonne As Integer = CInt(regione.Columns.Count)
            If righe < 2 Then Return
            Dim limiteColonne As Integer = Math.Min(colonne, MassimoCelle)
            Dim limiteRighe As Integer = Math.Min(righe, 30)
            Dim titoliTesto As Boolean = True
            Dim titoliPieni As Integer = 0
            Dim numeriSotto As Boolean = False
            For indice As Integer = 0 To limiteColonne - 1
                Dim valore As Object = foglio.Cells(primaRiga, primaColonna + indice).Value2
                If valore Is Nothing OrElse TypeOf valore Is DBNull Then Continue For
                If TypeOf valore Is String Then
                    titoliPieni += 1
                Else
                    titoliTesto = False
                End If
                For rigaDati As Integer = 1 To Math.Min(limiteRighe - 1, 5)
                    Dim dato As Object = foglio.Cells(primaRiga + rigaDati, primaColonna + indice).Value2
                    If TypeOf dato Is Double OrElse TypeOf dato Is Date Then numeriSotto = True
                Next
            Next
            Dim grassetto As Boolean = Booleano(foglio.Cells(primaRiga, primaColonna).Font.Bold)
            _regioneColonne = titoliTesto AndAlso titoliPieni >= Math.Min(2, limiteColonne) AndAlso (numeriSotto OrElse grassetto)
            If Not _regioneColonne OrElse colonne < 2 Then Return
            Dim primaTesto As Boolean = True
            Dim numeriAccanto As Boolean = False
            For rigaDati As Integer = 1 To limiteRighe - 1
                Dim valore As Object = foglio.Cells(primaRiga + rigaDati, primaColonna).Value2
                If valore IsNot Nothing AndAlso Not TypeOf valore Is DBNull AndAlso Not TypeOf valore Is String Then primaTesto = False
                Dim accanto As Object = foglio.Cells(primaRiga + rigaDati, primaColonna + 1).Value2
                If TypeOf accanto Is Double OrElse TypeOf accanto Is Date Then numeriAccanto = True
            Next
            _regioneRighe = primaTesto AndAlso numeriAccanto
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.EsaminaRegione", ex)
        End Try
    End Sub

    Private Function TitoloDaNomi(finestra As Object, nomeFoglio As String, riga As Integer, colonna As Integer, colonne As Boolean) As NomeTitolo
        Try
            AggiornaTitoli(finestra)
            Dim migliore As NomeTitolo = Nothing
            For Each nome As NomeTitolo In _titoli
                If Not String.Equals(nome.Foglio, nomeFoglio, StringComparison.OrdinalIgnoreCase) Then Continue For
                If colonne Then
                    If Not nome.Colonne OrElse nome.Riga >= riga OrElse nome.Colonna > colonna Then Continue For
                    If migliore Is Nothing OrElse nome.Riga > migliore.Riga Then migliore = nome
                Else
                    If Not nome.Righe OrElse nome.Colonna >= colonna OrElse nome.Riga > riga Then Continue For
                    If migliore Is Nothing OrElse nome.Colonna > migliore.Colonna Then migliore = nome
                End If
            Next
            Return migliore
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TitoloDaNomi", ex)
            Return Nothing
        End Try
    End Function

    Private Sub AggiornaTitoli(finestra As Object)
        Try
            Dim cartella As Object = finestra.Parent
            If cartella Is Nothing Then Return
            Dim nomeCartella As String = Convert.ToString(cartella.FullName)
            If String.Equals(nomeCartella, _titoliCartella, StringComparison.OrdinalIgnoreCase) AndAlso Environment.TickCount - _titoliTick < ValiditaNomi Then Return
            _titoliCartella = nomeCartella
            _titoliTick = Environment.TickCount
            Dim nuovi As New List(Of NomeTitolo)()
            Dim nomi As Object = cartella.Names
            Dim totale As Integer = Math.Min(CInt(nomi.Count), MassimoNomi)
            For indice As Integer = 1 To totale
                Dim titolo As NomeTitolo = LeggiNomeTitolo(nomi.Item(indice))
                If titolo IsNot Nothing Then nuovi.Add(titolo)
            Next
            _titoli = nuovi
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.AggiornaTitoli", ex)
        End Try
    End Sub

    Private Shared Function LeggiNomeTitolo(voce As Object) As NomeTitolo
        Try
            If voce Is Nothing Then Return Nothing
            Dim testo As String = Convert.ToString(voce.Name)
            Dim separatore As Integer = testo.LastIndexOf("!"c)
            If separatore >= 0 Then testo = testo.Substring(separatore + 1)
            Dim tipo As Integer = TipoTitolo(testo)
            If tipo = 0 Then Return Nothing
            Dim intervallo As Object = voce.RefersToRange
            If intervallo Is Nothing Then Return Nothing
            Dim titolo As New NomeTitolo()
            titolo.Foglio = Convert.ToString(intervallo.Worksheet.Name)
            titolo.Riga = CInt(intervallo.Row)
            titolo.Colonna = CInt(intervallo.Column)
            titolo.Colonne = (tipo And 1) <> 0
            titolo.Righe = (tipo And 2) <> 0
            Return titolo
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.LeggiNomeTitolo", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function TipoTitolo(nome As String) As Integer
        Dim basso As String = If(nome, "").ToLowerInvariant()
        If basso.StartsWith("columntitle", StringComparison.Ordinal) OrElse basso.StartsWith("titolocolonna", StringComparison.Ordinal) Then Return 1
        If basso.StartsWith("rowtitle", StringComparison.Ordinal) OrElse basso.StartsWith("titoloriga", StringComparison.Ordinal) Then Return 2
        If basso.StartsWith("title", StringComparison.Ordinal) OrElse basso.StartsWith("titolo", StringComparison.Ordinal) Then Return 3
        Return 0
    End Function

    Private Sub RicordaModifica(elemento As IUIAutomationElement)
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return
            If String.Equals(Lettore.DescrittoreApp.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione), IdModificaCella, StringComparison.Ordinal) Then _modifica = elemento
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.RicordaModifica", ex)
        End Try
    End Sub

    Private Function CellaInModifica(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse Lettore.DescrittoreApp Is Nothing Then Return Nothing
            Dim descrittore As DescrittoreElementi = Lettore.DescrittoreApp
            If String.Equals(descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaIdAutomazione), IdModificaCella, StringComparison.Ordinal) Then
                _modifica = elemento
                Return Nothing
            End If
            Dim finestraElemento As IntPtr = IntPtr.Zero
            Try
                finestraElemento = elemento.CurrentNativeWindowHandle
            Catch exManiglia As Exception
                RegistroErrori.Scrivi("ExcelAppModule.CellaInModifica", exManiglia)
                Return Nothing
            End Try
            If Not String.Equals(ControlliNativi.ClasseFinestra(finestraElemento), "EXCEL6", StringComparison.Ordinal) Then Return Nothing
            Dim nota As IUIAutomationElement = _modifica
            If nota IsNot Nothing AndAlso ConFuoco(nota) Then Return nota
            Dim automazione As IUIAutomation = Lettore.AutomazioneApp
            If automazione Is Nothing Then Return Nothing
            Dim radice As IUIAutomationElement = automazione.ElementFromHandle(NativoLettore.GetForegroundWindow())
            If radice Is Nothing Then Return Nothing
            Dim condizione As IUIAutomationCondition = automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaIdAutomazione, IdModificaCella)
            Dim trovato As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, Lettore.RichiestaCacheApp)
            If trovato Is Nothing OrElse Not ConFuoco(trovato) Then Return Nothing
            _modifica = trovato
            Return trovato
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.CellaInModifica", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function ConFuoco(elemento As IUIAutomationElement) As Boolean
        Try
            Return elemento IsNot Nothing AndAlso elemento.CurrentHasKeyboardFocus <> 0
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.ConFuoco", ex)
            Return False
        End Try
    End Function

    Private Function FoglioGestito() As Boolean
        Try
            Dim maniglia As IntPtr = FinestraFoglio()
            If maniglia = IntPtr.Zero Then Return False
            SyncLock _blocco
                Return LeggiStato(FinestraExcel(maniglia)) IsNot Nothing
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FoglioGestito", ex)
            Return False
        End Try
    End Function

    Private Function FinestraFoglio() As IntPtr
        Try
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano = IntPtr.Zero Then Return IntPtr.Zero
            Dim pid As UInteger = 0UI
            Dim idThread As UInteger = NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
            If idThread = 0UI Then Return IntPtr.Zero
            Dim info As New InfoThreadGui()
            info.Dimensione = Marshal.SizeOf(GetType(InfoThreadGui))
            If Not GetGUIThreadInfo(idThread, info) Then Return IntPtr.Zero
            If info.Fuoco = IntPtr.Zero Then Return IntPtr.Zero
            If Not String.Equals(ControlliNativi.ClasseFinestra(info.Fuoco), "EXCEL7", StringComparison.Ordinal) Then Return IntPtr.Zero
            Return info.Fuoco
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FinestraFoglio", ex)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function FinestraExcel(maniglia As IntPtr) As Object
        Try
            If maniglia = IntPtr.Zero Then Return Nothing
            Dim idThread As Integer = Thread.CurrentThread.ManagedThreadId
            If _finestraOm IsNot Nothing AndAlso _maniglia = maniglia AndAlso _threadOm = idThread Then Return _finestraOm
            Dimentica()
            Dim iid As New Guid("00020400-0000-0000-C000-000000000046")
            Dim oggetto As Object = Nothing
            If AccessibleObjectFromWindow(maniglia, OggettoNativo, iid, oggetto) <> 0 OrElse oggetto Is Nothing Then Return Nothing
            _finestraOm = oggetto
            _maniglia = maniglia
            _threadOm = idThread
            Return oggetto
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.FinestraExcel", ex)
            Dimentica()
            Return Nothing
        End Try
    End Function

    Private Sub Dimentica()
        Try
            _finestraOm = Nothing
            _maniglia = IntPtr.Zero
            _threadOm = 0
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.Dimentica", ex)
        End Try
    End Sub

    Private Function LeggiStato(finestra As Object) As StatoCella
        Try
            If finestra Is Nothing Then Return Nothing
            Dim cella As Object = finestra.ActiveCell
            If cella Is Nothing Then Return Nothing
            Dim stato As New StatoCella()
            stato.Indirizzo = Convert.ToString(cella.Address(False, False))
            stato.Riga = CInt(cella.Row)
            stato.Colonna = CInt(cella.Column)
            stato.Foglio = Convert.ToString(finestra.ActiveSheet.Name)
            Dim selezione As Object = finestra.RangeSelection
            If selezione IsNot Nothing Then
                stato.Selezione = Convert.ToString(selezione.Address(False, False))
                stato.Celle = Convert.ToInt64(selezione.CountLarge)
            Else
                stato.Selezione = stato.Indirizzo
                stato.Celle = 1L
            End If
            Return stato
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.LeggiStato", ex)
            Dimentica()
            Return Nothing
        End Try
    End Function

    Private Shared Function Cambiato(primo As StatoCella, secondo As StatoCella) As Boolean
        If primo Is Nothing OrElse secondo Is Nothing Then Return True
        If Not String.Equals(primo.Foglio, secondo.Foglio, StringComparison.Ordinal) Then Return True
        If Not String.Equals(primo.Indirizzo, secondo.Indirizzo, StringComparison.Ordinal) Then Return True
        Return Not String.Equals(primo.Selezione, secondo.Selezione, StringComparison.Ordinal)
    End Function

    Private Shared Function TestoCella(cella As Object) As String
        Try
            If cella Is Nothing Then Return ""
            Dim testo As String = Convert.ToString(cella.Text)
            If testo Is Nothing Then testo = ""
            testo = testo.Trim()
            If testo <> "" AndAlso testo.Trim("#"c) = "" Then
                Dim valore As Object = cella.Value2
                If valore IsNot Nothing AndAlso Not TypeOf valore Is DBNull Then testo = Convert.ToString(valore, CultureInfo.CurrentCulture)
            End If
            Return testo
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.TestoCella", ex)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiIntervallo(indirizzo As String) As String
        Try
            If String.IsNullOrEmpty(indirizzo) Then Return ""
            Dim aree As String() = indirizzo.Split(","c)
            Dim parti As New List(Of String)()
            For Each area As String In aree
                Dim estremi As String() = area.Split(":"c)
                If estremi.Length = 2 Then
                    parti.Add("da " & estremi(0) & " a " & estremi(1))
                Else
                    parti.Add(area)
                End If
            Next
            Return String.Join(", ", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.LeggiIntervallo", ex)
            Return If(indirizzo, "")
        End Try
    End Function

    Private Shared Function LetteraColonna(indirizzo As String) As String
        Try
            Dim testo As New StringBuilder()
            For Each carattere As Char In If(indirizzo, "")
                If Char.IsLetter(carattere) Then
                    testo.Append(carattere)
                Else
                    Exit For
                End If
            Next
            Return testo.ToString()
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.LetteraColonna", ex)
            Return ""
        End Try
    End Function

    Private Shared Function Booleano(valore As Object) As Boolean
        Try
            If TypeOf valore Is Boolean Then Return CBool(valore)
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.Booleano", ex)
            Return False
        End Try
    End Function

    Private Shared Function Intero(valore As Object, mancante As Integer) As Integer
        Try
            If valore Is Nothing OrElse TypeOf valore Is DBNull Then Return mancante
            If TypeOf valore Is Integer Then Return CInt(valore)
            If TypeOf valore Is Double OrElse TypeOf valore Is Single OrElse TypeOf valore Is Long OrElse TypeOf valore Is Short Then
                Dim numero As Double = Convert.ToDouble(valore)
                If numero > Integer.MaxValue OrElse numero < Integer.MinValue Then Return mancante
                Return CInt(numero)
            End If
            Return mancante
        Catch ex As Exception
            RegistroErrori.Scrivi("ExcelAppModule.Intero", ex)
            Return mancante
        End Try
    End Function

End Class