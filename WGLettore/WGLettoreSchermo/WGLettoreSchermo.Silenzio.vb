Imports System.Threading

' Gestione del silenzio della sintesi vocale quando WinGabriel è in primo piano o il lettore è sospeso, per evitare
' sovrapposizioni di voce.
Partial Public NotInheritable Class WGLettoreSchermo

    Private Const ValiditaSilenzioMs As Integer = 40
    Private Const TolleranzaSenzaPrimoPianoMs As Integer = 1200

    Private _silenzioConWinGabriel As Integer = 1
    Private _menuProprioAttivo As Integer
    Private _menuProprioTick As Integer
    Private _winGabrielAttivo As Integer
    Private _winGabrielUltimoTick As Integer
    Private _silenzioControlloTick As Integer
    Private _silenzioEsito As Integer
    Private _silenzioCalcolato As Integer

    Public Property SilenzioConWinGabriel As Boolean
        Get
            Return Volatile.Read(_silenzioConWinGabriel) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_silenzioConWinGabriel, If(value, 1, 0))
            Interlocked.Exchange(_silenzioCalcolato, 0)
        End Set
    End Property

    ''' <summary>
    ''' Vero mentre il programma che ospita il lettore ha aperto un suo menu o una sua schermata e li annuncia da sé: il lettore tace, come con WinGabriel,
    ''' così la stessa voce non viene detta da due sistemi.
    ''' </summary>
    Public Property AnnuncioProprioAttivo As Boolean
        Get
            Return Volatile.Read(_menuProprioAttivo) = 1
        End Get
        Set(value As Boolean)
            Volatile.Write(_menuProprioTick, Environment.TickCount)
            Interlocked.Exchange(_menuProprioAttivo, If(value, 1, 0))
            Interlocked.Exchange(_silenzioCalcolato, 0)
        End Set
    End Property

    Private ReadOnly _cacheWinGabriel As New Dictionary(Of UInteger, Integer())()

    ''' <summary>
    ''' Vero se il processo è WinGabriel, in qualunque cartella sia installato: il lettore è un programma a parte
    ''' e lo riconosce dal nome dell'eseguibile. La risposta si ricorda per 20 secondi (i numeri dei processi si riusano).
    ''' </summary>
    Friend Function EProcessoWinGabriel(pid As UInteger) As Boolean
        Try
            If pid = 0UI Then Return False
            Dim adesso As Integer = Environment.TickCount
            SyncLock _cacheWinGabriel
                Dim voce As Integer() = Nothing
                If _cacheWinGabriel.TryGetValue(pid, voce) AndAlso adesso - voce(1) < 20000 Then Return voce(0) = 1
            End SyncLock
            Dim nome As String = NomeEseguibile(pid)
            Dim esito As Boolean = String.Equals(nome, "wingabriel", StringComparison.OrdinalIgnoreCase)
            SyncLock _cacheWinGabriel
                If _cacheWinGabriel.Count > 256 Then _cacheWinGabriel.Clear()
                _cacheWinGabriel(pid) = New Integer() {If(esito, 1, 0), adesso}
            End SyncLock
            Return esito
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EProcessoWinGabriel", ex)
            Return False
        End Try
    End Function

    ''' <summary>Dice una frase subito, a meno che WinGabriel sia in primo piano o il lettore sia sospeso.</summary>
    Public Sub Annuncia(testo As String)
        Parla(testo, True)
    End Sub

    ''' <summary>Vero se il lettore sta tacendo perché WinGabriel è in primo piano.</summary>
    Public ReadOnly Property TaceperWinGabriel As Boolean
        Get
            Return SilenzioEsterno()
        End Get
    End Property

    Friend Function SilenzioEsterno() As Boolean
        Try
            If Volatile.Read(_menuProprioAttivo) = 1 Then
                If Environment.TickCount - Volatile.Read(_menuProprioTick) < 120000 Then Return True
                Interlocked.Exchange(_menuProprioAttivo, 0)
            End If
            If Volatile.Read(_silenzioConWinGabriel) = 0 Then Return False
            Dim adesso As Integer = Environment.TickCount
            If Volatile.Read(_silenzioCalcolato) = 1 AndAlso adesso - Volatile.Read(_silenzioControlloTick) < ValiditaSilenzioMs Then
                Return Volatile.Read(_silenzioEsito) = 1
            End If
            Dim esito As Boolean = CalcolaSilenzio(adesso)
            Volatile.Write(_silenzioEsito, If(esito, 1, 0))
            Volatile.Write(_silenzioControlloTick, adesso)
            Volatile.Write(_silenzioCalcolato, 1)
            Return esito
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SilenzioEsterno: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CalcolaSilenzio(adesso As Integer) As Boolean
        Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
        If primoPiano = IntPtr.Zero Then
            Return Volatile.Read(_winGabrielAttivo) = 1 AndAlso adesso - Volatile.Read(_winGabrielUltimoTick) < TolleranzaSenzaPrimoPianoMs
        End If
        Dim pid As UInteger = 0UI
        NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
        If pid <> 0UI AndAlso EProcessoWinGabriel(pid) Then
            Volatile.Write(_winGabrielAttivo, 1)
            Volatile.Write(_winGabrielUltimoTick, adesso)
            Return True
        End If
        If Volatile.Read(_winGabrielAttivo) = 0 Then Return False
        If SuperficieDiSistema(primoPiano, pid) Then
            Volatile.Write(_winGabrielUltimoTick, adesso)
            Return True
        End If
        Volatile.Write(_winGabrielAttivo, 0)
        Return False
    End Function

    Private Function SuperficieDiSistema(finestra As IntPtr, pid As UInteger) As Boolean
        Try
            Dim classe As String = ClasseFinestra(finestra)
            Select Case classe
                Case "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
                     "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "TaskSwitcherOverlayWnd",
                     "#32771", "TaskListThumbnailWnd", "TaskListOverlayWnd", "ForegroundStaging", "Shell_InputSwitchTopLevelWindow",
                     "Windows.UI.Core.CoreWindow", "NotificationToast", "ToastChildWindow"
                    Return True
            End Select
            If pid = 0UI Then Return False
            Select Case NomeEseguibile(pid)
                Case "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "searchui", "textinputhost", "shellhost", "lockapp", "sihost"
                    Return True
                Case "explorer"
                    If classe = "CabinetWClass" OrElse classe = "ExploreWClass" Then Return False
                    Return classe = "#32768" OrElse classe = "tooltips_class32" OrElse classe = "TrayNotifyWnd" OrElse classe = "ToolbarWindow32" OrElse classe = "MSTaskListWClass"
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SuperficieDiSistema: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AttendiUscitaWinGabriel(massimoMs As Integer)
        Try
            Dim inizio As Integer = Environment.TickCount
            While PrimoPianoInWinGabriel() AndAlso Environment.TickCount - inizio < massimoMs
                Thread.Sleep(30)
            End While
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AttendiUscitaWinGabriel: " & ex.Message)
        Finally
            Interlocked.Exchange(_silenzioCalcolato, 0)
        End Try
    End Sub

    Private Function EventoDaScartare(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing Then Return True
            If evento.Tipo = TipoEventoLettore.Ricerca Then Return False
            If Not SilenzioEsterno() Then Return False
            Select Case evento.Tipo
                Case TipoEventoLettore.CambioValore
                    Interlocked.Exchange(_cambioValoreInCoda, 0)
                Case TipoEventoLettore.SelezioneTesto
                    Interlocked.Exchange(_selezioneInCoda, 0)
                Case TipoEventoLettore.FocusMsaa
                    Interlocked.Exchange(_focusMsaaInCoda, 0)
            End Select
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EventoDaScartare: " & ex.Message)
            Return False
        End Try
    End Function

End Class
