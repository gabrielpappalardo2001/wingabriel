Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports UIAutomationClient

' Word e l'editor dei messaggi di Outlook: pagina, stile, elenchi, tabelle e modifiche registrate mentre ci si sposta nel documento.
Friend NotInheritable Class WordAppModule
    Inherits AppModuleLettore

    Private Const OggettoNativo As Integer = &HFFFFFFF0
    Private Const ClasseDocumento As String = "_WwG"
    Private Const AttesaMassima As Integer = 300
    Private Const PassoAttesa As Integer = 12
    Private Const ValiditaPrecedente As Integer = 4000

    Private Const InfoPagina As Integer = 3
    Private Const InfoPagine As Integer = 4
    Private Const InfoColonna As Integer = 9
    Private Const InfoRiga As Integer = 10
    Private Const InfoInTabella As Integer = 12
    Private Const InfoRigaTabella As Integer = 13
    Private Const InfoColonnaTabella As Integer = 16

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

    Private NotInheritable Class StatoWord
        Public Inizio As Integer
        Public Fine As Integer
        Public Pagina As Integer
        Public Pagine As Integer
        Public Riga As Integer
        Public Colonna As Integer
        Public InTabella As Boolean
        Public RigaTabella As Integer
        Public ColonnaTabella As Integer
        Public Stile As String = ""
        Public Elenco As String = ""
        Public Revisione As Boolean
    End Class

    Private ReadOnly _blocco As New Object()
    Private _finestraOm As Object
    Private _maniglia As IntPtr
    Private _threadOm As Integer
    Private _ultimo As StatoWord
    Private _ultimoTick As Integer

    Public Sub New(lettore As WGLettoreSchermo, nomeApp As String)
        MyBase.New(lettore, nomeApp)
    End Sub

    Public Overrides Function PrimoPianoIgnorato(finestra As IntPtr) As Boolean
        Try
            SyncLock _blocco
                _ultimo = Nothing
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.PrimoPianoIgnorato", ex)
        End Try
        Return False
    End Function

    Public Overrides Function AllTasto(evento As EventoLettore, elemento As IUIAutomationElement) As Boolean
        Try
            If Not TastoDelDocumento(evento) Then Return False
            Dim maniglia As IntPtr = FinestraDocumento()
            If maniglia = IntPtr.Zero Then Return False
            Dim tickTasto As Integer = Lettore.TickUltimoTastoApp
            Dim inizio As Integer = Environment.TickCount
            SyncLock _blocco
                Dim finestra As Object = FinestraWord(maniglia)
                If finestra Is Nothing Then Return False
                Dim prima As StatoWord = _ultimo
                If prima Is Nothing OrElse Environment.TickCount - _ultimoTick > ValiditaPrecedente Then prima = LeggiStato(finestra)
                If prima Is Nothing Then Return False
                Do
                    If Lettore.TickUltimoTastoApp <> tickTasto Then Return False
                    Dim stato As StatoWord = LeggiStato(FinestraWord(maniglia))
                    If stato Is Nothing Then Return False
                    If stato.Inizio <> prima.Inizio OrElse stato.Fine <> prima.Fine OrElse stato.Stile <> prima.Stile Then
                        _ultimo = stato
                        _ultimoTick = Environment.TickCount
                        Dim frase As String = Differenze(prima, stato)
                        If frase <> "" Then Lettore.ParlaApp(frase, False)
                        Return False
                    End If
                    If Environment.TickCount - inizio >= AttesaMassima Then
                        _ultimo = stato
                        _ultimoTick = Environment.TickCount
                        Return False
                    End If
                    Thread.Sleep(PassoAttesa)
                Loop
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.AllTasto", ex)
            Return False
        End Try
    End Function

    Public Overrides Function FormatoApp(elemento As IUIAutomationElement) As String
        Try
            Dim maniglia As IntPtr = FinestraDocumento()
            If maniglia = IntPtr.Zero Then Return ""
            SyncLock _blocco
                Dim finestra As Object = FinestraWord(maniglia)
                If finestra Is Nothing Then Return ""
                Dim selezione As Object = finestra.Selection
                If selezione Is Nothing Then Return ""
                Dim parti As New List(Of String)()
                Dim carattere As Object = selezione.Font
                Dim nome As String = Convert.ToString(carattere.Name)
                If nome <> "" Then parti.Add(nome)
                Dim dimensione As Double = Convert.ToDouble(carattere.Size)
                If dimensione > 0 AndAlso dimensione < 1000 Then parti.Add(dimensione.ToString("0.#", Globalization.CultureInfo.CurrentCulture) & " punti")
                If Convert.ToInt32(carattere.Bold) <> 0 Then parti.Add("grassetto")
                If Convert.ToInt32(carattere.Italic) <> 0 Then parti.Add("corsivo")
                If Convert.ToInt32(carattere.Underline) <> 0 Then parti.Add("sottolineato")
                If Convert.ToInt32(carattere.StrikeThrough) <> 0 Then parti.Add("barrato")
                If Convert.ToInt32(carattere.Superscript) <> 0 Then parti.Add("apice")
                If Convert.ToInt32(carattere.Subscript) <> 0 Then parti.Add("pedice")
                Select Case Convert.ToInt32(selezione.ParagraphFormat.Alignment)
                    Case 0 : parti.Add("allineato a sinistra")
                    Case 1 : parti.Add("centrato")
                    Case 2 : parti.Add("allineato a destra")
                    Case 3 : parti.Add("giustificato")
                End Select
                Dim stile As String = NomeStile(selezione)
                If stile <> "" Then parti.Add("stile " & stile)
                Return String.Join(", ", parti)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.FormatoApp", ex)
            Dimentica()
            Return ""
        End Try
    End Function

    Public Overrides Function TestoBarraStatoApp(finestra As IntPtr) As String
        Try
            Dim maniglia As IntPtr = FinestraDocumento()
            If maniglia = IntPtr.Zero Then Return ""
            SyncLock _blocco
                Dim stato As StatoWord = LeggiStato(FinestraWord(maniglia))
                If stato Is Nothing Then Return ""
                Dim parti As New List(Of String)()
                If stato.Pagina > 0 Then parti.Add("pagina " & stato.Pagina.ToString() & If(stato.Pagine > 0, " di " & stato.Pagine.ToString(), ""))
                If stato.Riga > 0 Then parti.Add("riga " & stato.Riga.ToString())
                If stato.Colonna > 0 Then parti.Add("colonna " & stato.Colonna.ToString())
                If stato.InTabella Then parti.Add("tabella, riga " & stato.RigaTabella.ToString() & ", colonna " & stato.ColonnaTabella.ToString())
                Return String.Join(", ", parti)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.TestoBarraStatoApp", ex)
            Dimentica()
            Return ""
        End Try
    End Function

    Private Shared Function TastoDelDocumento(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing OrElse evento.Alt Then Return False
            Select Case evento.Azione
                Case AzioneTastoLettore.Navigazione, AzioneTastoLettore.Selezione
                    Return True
                Case AzioneTastoLettore.Modifica
                    Return evento.CodiceTasto = &HD OrElse evento.CodiceTasto = &H8 OrElse evento.CodiceTasto = &H2E
            End Select
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.TastoDelDocumento", ex)
            Return False
        End Try
    End Function

    Private Shared Function Differenze(prima As StatoWord, stato As StatoWord) As String
        Dim parti As New List(Of String)()
        If stato.InTabella AndAlso Not prima.InTabella Then
            parti.Add("tabella, riga " & stato.RigaTabella.ToString() & ", colonna " & stato.ColonnaTabella.ToString())
        ElseIf stato.InTabella AndAlso prima.InTabella Then
            If stato.RigaTabella <> prima.RigaTabella Then parti.Add("riga " & stato.RigaTabella.ToString())
            If stato.ColonnaTabella <> prima.ColonnaTabella Then parti.Add("colonna " & stato.ColonnaTabella.ToString())
        ElseIf prima.InTabella AndAlso Not stato.InTabella Then
            parti.Add("fuori dalla tabella")
        End If
        If stato.Pagina > 0 AndAlso stato.Pagina <> prima.Pagina Then parti.Add("pagina " & stato.Pagina.ToString())
        If stato.Stile <> "" AndAlso stato.Stile <> prima.Stile Then parti.Add(stato.Stile)
        If stato.Elenco <> "" AndAlso stato.Elenco <> prima.Elenco Then
            Dim primo As Char = stato.Elenco(0)
            parti.Add(If(Char.IsLetterOrDigit(primo), "elenco " & stato.Elenco, "punto elenco"))
        End If
        If stato.Revisione AndAlso Not prima.Revisione Then parti.Add("modifica registrata")
        Return String.Join(", ", parti)
    End Function

    Private Shared Function NomeStile(selezione As Object) As String
        Try
            Dim stile As Object = selezione.Paragraphs(1).Style
            Return Convert.ToString(stile.NameLocal)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WordAppModule.NomeStile", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Intero(valore As Object) As Integer
        Try
            If valore Is Nothing Then Return 0
            Return Convert.ToInt32(valore)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WordAppModule.Intero", exAtteso)
            Return 0
        End Try
    End Function

    Private Function LeggiStato(finestra As Object) As StatoWord
        Try
            If finestra Is Nothing Then Return Nothing
            Dim selezione As Object = finestra.Selection
            If selezione Is Nothing Then Return Nothing
            Dim stato As New StatoWord()
            stato.Inizio = Intero(selezione.Start)
            stato.Fine = Intero(selezione.End)
            stato.Pagina = Intero(selezione.Information(InfoPagina))
            stato.Pagine = Intero(selezione.Information(InfoPagine))
            stato.Riga = Intero(selezione.Information(InfoRiga))
            stato.Colonna = Intero(selezione.Information(InfoColonna))
            stato.InTabella = Convert.ToBoolean(selezione.Information(InfoInTabella))
            If stato.InTabella Then
                stato.RigaTabella = Intero(selezione.Information(InfoRigaTabella))
                stato.ColonnaTabella = Intero(selezione.Information(InfoColonnaTabella))
            End If
            stato.Stile = NomeStile(selezione)
            Try
                stato.Elenco = Convert.ToString(selezione.Range.ListFormat.ListString)
            Catch exElenco As Exception
                RegistroErrori.Scrivi("WordAppModule.LeggiStato.Elenco", exElenco)
            End Try
            Try
                stato.Revisione = Intero(selezione.Range.Revisions.Count) > 0
            Catch exRevisioni As Exception
                RegistroErrori.Scrivi("WordAppModule.LeggiStato.Revisioni", exRevisioni)
            End Try
            Return stato
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.LeggiStato", ex)
            Dimentica()
            Return Nothing
        End Try
    End Function

    Private Function FinestraDocumento() As IntPtr
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
            If Not String.Equals(ControlliNativi.ClasseFinestra(info.Fuoco), ClasseDocumento, StringComparison.Ordinal) Then Return IntPtr.Zero
            Return info.Fuoco
        Catch ex As Exception
            RegistroErrori.Scrivi("WordAppModule.FinestraDocumento", ex)
            Return IntPtr.Zero
        End Try
    End Function

    Private Function FinestraWord(maniglia As IntPtr) As Object
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
            RegistroErrori.Scrivi("WordAppModule.FinestraWord", ex)
            Dimentica()
            Return Nothing
        End Try
    End Function

    Private Sub Dimentica()
        _finestraOm = Nothing
        _maniglia = IntPtr.Zero
        _threadOm = 0
        _ultimo = Nothing
    End Sub

End Class
