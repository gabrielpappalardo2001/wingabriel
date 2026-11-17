Imports System.Runtime.InteropServices
Imports System.Threading
Imports UIAutomationClient

' Liste di Windows fatte con il controllo classico SysListView32 (il desktop, per esempio). Per UI Automation sono un ponte verso MSAA
' e ogni proprietà letta costa una chiamata fra processi: a ogni freccia l'annuncio arrivava dopo parecchio tempo. Spostandosi dentro la
' stessa lista si legge la riga direttamente dal controllo, senza passare da UI Automation.
Partial Public NotInheritable Class WGLettoreSchermo

    <StructLayout(LayoutKind.Sequential)>
    Private Structure InfoThreadGuiLista
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
    Private Shared Function GetGUIThreadInfo(idThread As UInteger, ByRef info As InfoThreadGuiLista) As Boolean
    End Function

    Private Const ValiditaListaNativaMs As Long = 4000L
    Private Const DoppioneListaNativaMs As Long = 600L

    Private _listaNativaCorrente As IntPtr = IntPtr.Zero
    Private _listaNativaTick As Integer
    Private _listaNativaIndice As Integer
    Private _listaNativaAnnuncioTick As Integer

    ' Il controllo che ha il fuoco da tastiera nel programma in primo piano, se è una lista classica.
    Private Function ListaNativaConFuoco(Optional finestra As IntPtr = Nothing) As IntPtr
        Try
            Dim primoPiano As IntPtr = If(finestra <> IntPtr.Zero, finestra, NativoLettore.GetForegroundWindow())
            If primoPiano = IntPtr.Zero Then Return IntPtr.Zero
            Dim pid As UInteger = 0UI
            Dim idThread As UInteger = NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
            If idThread = 0UI Then Return IntPtr.Zero
            Dim info As New InfoThreadGuiLista()
            info.Dimensione = Marshal.SizeOf(GetType(InfoThreadGuiLista))
            If Not GetGUIThreadInfo(idThread, info) Then Return IntPtr.Zero
            If info.Fuoco = IntPtr.Zero Then Return IntPtr.Zero
            If Not ControlliNativi.EListaNativa(ControlliNativi.ClasseFinestra(info.Fuoco)) Then Return IntPtr.Zero
            Return info.Fuoco
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ListaNativaConFuoco", ex)
            Return IntPtr.Zero
        End Try
    End Function

    ' Quando una riga di lista classica passa dalla lettura veloce, l'elemento che UI Automation ricordava (per esempio il campo di scrittura
    ' di prima) non è più quello in uso: senza questo, i suoi eventi di testo e il suo "fuoco noto" continuavano a leggere la sua riga.
    Private Sub DimenticaElementoUia()
        Try
            _ultimoElemento = Nothing
            _ultimoNome = ""
            _ultimaParteVariabile = ""
            _istantanea = Nothing
            _uiaFerma = Nothing
            Dim registro As RegistroEventiUia = _registroEventi
            If registro IsNot Nothing Then registro.AggiornaTesto(Nothing)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.DimenticaElementoUia", ex)
        End Try
    End Sub

    ' Cambiando finestra in primo piano la lista di prima non vale più: tornandoci si dice di nuovo l'ingresso.
    Private Sub AzzeraListaNativa()
        _listaNativaCorrente = IntPtr.Zero
        _listaNativaIndice = -1
        _listaNativaAnnuncioTick = Environment.TickCount - 100000
    End Sub

    ' La lista delle icone del desktop, anche quando nessun controllo ha il fuoco da tastiera (per esempio dopo Windows+M).
    Private Function ListaDelDesktopCorrente() As IntPtr
        Try
            Dim contenitore As IntPtr = NativoLettore.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", Nothing)
            While contenitore <> IntPtr.Zero
                Dim vista As IntPtr = NativoLettore.FindWindowEx(contenitore, IntPtr.Zero, "SHELLDLL_DefView", Nothing)
                If vista <> IntPtr.Zero Then
                    Dim lista As IntPtr = NativoLettore.FindWindowEx(vista, IntPtr.Zero, "SysListView32", Nothing)
                    If lista <> IntPtr.Zero Then Return lista
                End If
                contenitore = NativoLettore.FindWindowEx(IntPtr.Zero, contenitore, "WorkerW", Nothing)
            End While
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ListaDelDesktopCorrente", ex)
        End Try
        Return IntPtr.Zero
    End Function

    ' Il tipo di visualizzazione che si dice entrando nel desktop: in Esplora risorse (explorer.exe) è sempre "ad elenco".
    Private Function VistaAnnunciata(lista As IntPtr) As String
        Try
            If ListaDelDesktop(lista) Then Return "visualizzazione ad elenco"
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.VistaAnnunciata", ex)
        End Try
        Return ControlliNativi.NomeVistaLista(lista)
    End Function

    ' La lista classica che un evento di fuoco indica da sé: la maniglia dell'elemento, se è una SysListView32. Una sola chiamata.
    Private Function ListaDaElemento(elemento As IUIAutomationElement) As IntPtr
        Try
            If elemento Is Nothing Then Return IntPtr.Zero
            Dim maniglia As IntPtr = CType(elemento.CurrentNativeWindowHandle, IntPtr)
            If maniglia <> IntPtr.Zero AndAlso ControlliNativi.EListaNativa(ControlliNativi.ClasseFinestra(maniglia)) Then Return maniglia
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ListaDaElemento", ex)
        End Try
        Return IntPtr.Zero
    End Function

    ' Chiamata dal percorso completo: segna che l'ultima voce annunciata stava in una lista classica.
    Private Sub RicordaListaNativa(elemento As IUIAutomationElement)
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati
                Case Else
                    Return
            End Select
            If ElementoWeb(elemento) Then Return
            Dim lista As IntPtr = ControlliNativi.TrovaControllo(elemento, _walkerControllo, _richiestaCache, True)
            If lista = IntPtr.Zero Then Return
            _listaNativaCorrente = lista
            _listaNativaTick = Environment.TickCount
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RicordaListaNativa", ex)
        End Try
    End Sub

    ' Quando il fuoco è in una lista classica si ricorda subito, anche se UI Automation non la descrive come una lista: dal passo dopo
    ' ci si sposta nella stessa lista senza passare da UI Automation.
    Private Sub SegnaListaNativaConFuoco()
        Try
            Dim lista As IntPtr = ListaNativaConFuoco()
            If lista = IntPtr.Zero Then Return
            _listaNativaCorrente = lista
            _listaNativaTick = Environment.TickCount
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SegnaListaNativaConFuoco", ex)
        End Try
    End Sub

    Private Function ListaDelDesktop(lista As IntPtr) As Boolean
        Try
            Dim radice As IntPtr = NativoLettore.GetAncestor(lista, NativoLettore.GA_ROOT)
            If radice = IntPtr.Zero Then Return False
            Dim classe As String = ControlliNativi.ClasseFinestra(radice)
            Return classe = "Progman" OrElse classe = "WorkerW"
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ListaDelDesktop", ex)
            Return False
        End Try
    End Function

    ' Vero se la riga è stata annunciata (o era già stata detta un attimo fa) senza usare UI Automation.
    ' Sul desktop vale anche per l'ingresso: prima "Desktop, visualizzazione elementi, tipo di visualizzazione" e poi la voce.
    ' finestra: la finestra in primo piano se già si sa (al cambio di primo piano il fuoco può non essere ancora aggiornato);
    ' listaIndicata: la lista, se l'evento ricevuto la indica già.
    Private Function ProvaRigaNativaVeloce(Optional finestra As IntPtr = Nothing, Optional listaIndicata As IntPtr = Nothing) As Boolean
        Try
            If FocusSilenziato() Then Return False
            Dim lista As IntPtr = If(listaIndicata <> IntPtr.Zero, listaIndicata, ListaNativaConFuoco(finestra))
            If lista = IntPtr.Zero AndAlso FinestraDesktop(If(finestra <> IntPtr.Zero, finestra, NativoLettore.GetForegroundWindow())) Then lista = ListaDelDesktopCorrente()
            If lista = IntPtr.Zero Then Return False
            Dim giaInLista As Boolean = lista = _listaNativaCorrente AndAlso MillisecondiDa(_listaNativaTick) <= ValiditaListaNativaMs
            Dim sulDesktop As Boolean = ListaDelDesktop(lista)
            If Not giaInLista AndAlso Not sulDesktop Then Return False
            Dim riga As ControlliNativi.RigaNativa = Nothing
            If Not ControlliNativi.LeggiRigaLista(lista, riga) OrElse String.IsNullOrEmpty(riga.Nome) Then Return False
            _listaNativaCorrente = lista
            _listaNativaTick = Environment.TickCount
            DimenticaElementoUia()
            If giaInLista AndAlso riga.Indice = _listaNativaIndice Then
                ' Senza nessun tasto premuto dopo l'annuncio, un secondo evento per la stessa voce è un doppione dell'altro sistema; con un tasto
                ' premuto (per esempio Home due volte) la finestra è breve.
                Dim finestraDoppione As Long = If(MillisecondiDa(Volatile.Read(_ultimoTastoTick)) > MillisecondiDa(_listaNativaAnnuncioTick), 3000L, DoppioneListaNativaMs)
                If MillisecondiDa(_listaNativaAnnuncioTick) < finestraDoppione Then Return True
            End If
            _listaNativaIndice = riga.Indice
            _listaNativaAnnuncioTick = Environment.TickCount
            Dim parti As New System.Collections.Generic.List(Of String)()
            If Not giaInLista AndAlso sulDesktop Then
                parti.Add("Desktop")
                parti.Add("visualizzazione elementi")
                Dim vista As String = VistaAnnunciata(lista)
                If vista <> "" Then parti.Add(vista)
            End If
            parti.Add(riga.Nome)
            If LeggiColonne Then
                Dim colonne As String = ControlliNativi.SoloColonne(riga)
                If colonne <> "" Then parti.Add(colonne)
            End If
            If riga.ConCasella Then parti.Add(If(riga.Spuntata, "spuntato", "non spuntato"))
            If Not riga.Selezionata Then parti.Add("non selezionato")
            If AnnunciaPosizioneElenchi AndAlso riga.Totale > 1 AndAlso riga.Indice > 0 Then parti.Add(riga.Indice.ToString() & " di " & riga.Totale.ToString())
            _ultimoFocusTick = Environment.TickCount
            Parla(String.Join(", ", parti), True)
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ProvaRigaNativaVeloce", ex)
            Return False
        End Try
    End Function

End Class
