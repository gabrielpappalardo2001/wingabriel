Imports System
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports wgcontrols
Imports NAudio.Wave
Imports NAudio.Midi
Imports System.Media
Imports System.Threading.Tasks
Imports System.Reflection
Imports System.Collections.Generic

Public Class ElementoLista
    Public Property Testo As String
    Public Property Id As String

    Public Sub New(t As String, i As String)
        Testo = t
        Id = i
    End Sub

    Public Overrides Function ToString() As String
        Return Testo
    End Function
End Class

Friend Class ImpostazioniMenu
    Public Property UltimoIndiceSelezionato As Integer
    Public Property AccessibileRole As AccessibleRole
    Public Property AccessibileName As String
    Public Property Items As New List(Of Object)
    Public Property MultiSelezioneConsentita As Boolean
    Public Property SelezioneAttivata As Boolean
    Public Property UltimoIdSelezionato As String
    Public Property UltimoTestoSelezionato As String
End Class

Public Class WGMenuLista
    Private WithEvents _internal As WGMenuListaInt
    Private scriviLog As New Logger()

    Public Sub New()
        Try
            _internal = New WGMenuListaInt()

            Dim stackTrace As New StackTrace()
            Dim frame As StackFrame
            Dim metodoChiamante As MethodBase

            For i As Integer = 1 To stackTrace.FrameCount - 1
                frame = stackTrace.GetFrame(i)
                metodoChiamante = frame.GetMethod()
                Dim tipoClasse As Type = metodoChiamante.DeclaringType

                If tipoClasse IsNot Nothing AndAlso tipoClasse.Name <> "WGMenuLista" AndAlso tipoClasse.Name <> "WGMenuListaInt" Then
                    _internal.NomeClasseChiamante = tipoClasse.Name.ToLower()
                    Exit For
                End If
            Next
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore nel costruttore WGMenuLista: {ex.Message}")
        End Try
    End Sub

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Try
                Return _internal Is Nothing OrElse _internal.IsDisposed
            Catch ex As Exception
                scriviLog.ScriviLog($"IsDisposed Get - Errore generico: {ex.ToString()}")
                Return True
            End Try
        End Get
    End Property

    Public ReadOnly Property IsHandleCreated As Boolean
        Get
            Try
                Return _internal IsNot Nothing AndAlso _internal.IsHandleCreated
            Catch ex As Exception
                scriviLog.ScriviLog($"IsHandleCreated Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
    End Property

    Public Property RicaricaSenzaNascondere As Boolean
        Get
            Try
                Return _internal.RicaricaSenzaNascondere
            Catch ex As Exception
                scriviLog.ScriviLog($"RicaricaSenzaNascondere Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.RicaricaSenzaNascondere = value
            Catch ex As Exception
                scriviLog.ScriviLog($"RicaricaSenzaNascondere Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property selezioneMenu As Boolean
        Get
            Try
                Return _internal.selezioneMenu
            Catch ex As Exception
                scriviLog.ScriviLog($"selezioneMenu Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.selezioneMenu = value
            Catch ex As Exception
                scriviLog.ScriviLog($"selezioneMenu Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property multiselezioneConsentita As Boolean
        Get
            Try
                Return _internal.multiselezioneConsentita
            Catch ex As Exception
                scriviLog.ScriviLog($"multiselezioneConsentita Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.multiselezioneConsentita = value
            Catch ex As Exception
                scriviLog.ScriviLog($"multiselezioneConsentita Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property invioNonConsentito As Boolean
        Get
            Try
                Return _internal.invioNonConsentito
            Catch ex As Exception
                scriviLog.ScriviLog($"invioNonConsentito Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.invioNonConsentito = value
            Catch ex As Exception
                scriviLog.ScriviLog($"invioNonConsentito Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property ciclono As Boolean
        Get
            Try
                Return _internal.ciclono
            Catch ex As Exception
                scriviLog.ScriviLog($"ciclono Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.ciclono = value
            Catch ex As Exception
                scriviLog.ScriviLog($"ciclono Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public ReadOnly Property TestoSelezionato As String
        Get
            Try
                Return _internal.TestoSelezionato
            Catch ex As Exception
                scriviLog.ScriviLog($"TestoSelezionato Get - Errore generico: {ex.ToString()}")
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property ValoreSelezionato As String
        Get
            Try
                Return _internal.ValoreSelezionato
            Catch ex As Exception
                scriviLog.ScriviLog($"ValoreSelezionato Get - Errore generico: {ex.ToString()}")
                Return String.Empty
            End Try
        End Get
    End Property

    Public Property canPlaySound As Boolean
        Get
            Try
                Return _internal.canPlaySound
            Catch ex As Exception
                scriviLog.ScriviLog($"canPlaySound Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.canPlaySound = value
            Catch ex As Exception
                scriviLog.ScriviLog($"canPlaySound Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    ''' <summary>
    ''' Quando è attiva, gli elementi selezionati e lo stato di multiselezione restano in
    ''' memoria anche se la lista viene svuotata e ricaricata con un altro blocco di elementi.
    ''' Gli elementi già selezionati che tornano a comparire vengono rimostrati selezionati,
    ''' quelli deselezionati restano deselezionati.
    ''' </summary>
    Public Property MantieniSelezioneTraPagine As Boolean
        Get
            Try
                Return _internal.MantieniSelezioneTraPagine
            Catch ex As Exception
                scriviLog.ScriviLog($"MantieniSelezioneTraPagine Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _internal.MantieniSelezioneTraPagine = value
            Catch ex As Exception
                scriviLog.ScriviLog($"MantieniSelezioneTraPagine Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Event inizio As EventHandler
    Public Event fine As EventHandler
    Public Event iniziolista As EventHandler
    Public Event finelista As EventHandler
    Public Event ElementiSelezionati(ids As List(Of String))
    Public Event ElementoSelezionato(id As String)
    Public Event VoceCambiata As EventHandler(Of VoceCambiataEventArgs)
    ''' <summary>Ctrl con una lettera (per esempio Ctrl C, Ctrl X, Ctrl V). Chi ospita l'elenco la gestisce e mette Gestita a vero.</summary>
    Public Event ScorciatoiaCtrl As EventHandler(Of ScorciatoiaCtrlEventArgs)

    Private _escAscoltatori As EventHandler

    ''' <summary>Esc sulla lista. Se qualcuno ascolta l'evento, la chiusura spetta a lui; se nessuno lo ascolta la lista si chiude da sola.</summary>
    Public Custom Event escPremuto As EventHandler
        AddHandler(value As EventHandler)
            _escAscoltatori = DirectCast([Delegate].Combine(_escAscoltatori, value), EventHandler)
        End AddHandler
        RemoveHandler(value As EventHandler)
            _escAscoltatori = DirectCast([Delegate].Remove(_escAscoltatori, value), EventHandler)
        End RemoveHandler
        RaiseEvent(sender As Object, e As EventArgs)
            Dim ascoltatori As EventHandler = _escAscoltatori
            If ascoltatori IsNot Nothing Then ascoltatori.Invoke(sender, e)
        End RaiseEvent
    End Event

    Private Sub _internal_inizio(sender As Object, e As EventArgs) Handles _internal.inizio
        Try
            RaiseEvent inizio(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_inizio: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_iniziolista(sender As Object, e As EventArgs) Handles _internal.iniziolista
        Try
            RaiseEvent iniziolista(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_iniziolista: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_finelista(sender As Object, e As EventArgs) Handles _internal.finelista
        Try
            RaiseEvent finelista(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_finelista: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_fine(sender As Object, e As EventArgs) Handles _internal.fine
        Try
            RaiseEvent fine(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_fine: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_ScorciatoiaCtrl(sender As Object, e As ScorciatoiaCtrlEventArgs) Handles _internal.ScorciatoiaCtrl
        Try
            RaiseEvent ScorciatoiaCtrl(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_ScorciatoiaCtrl: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_ElementiSelezionati(ids As List(Of String)) Handles _internal.ElementiSelezionati
        Try
            RaiseEvent ElementiSelezionati(ids)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_ElementiSelezionati: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_ElementoSelezionato(id As String) Handles _internal.ElementoSelezionato
        Try
            RaiseEvent ElementoSelezionato(id)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_ElementoSelezionato: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_escPremuto(sender As Object, e As EventArgs) Handles _internal.escPremuto
        Try
            If _escAscoltatori Is Nothing Then
                _internal.abbandona()
            Else
                RaiseEvent escPremuto(Me, e)
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_escPremuto: {ex.Message}")
        End Try
    End Sub

    Private Sub _internal_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _internal.VoceCambiata
        Try
            RaiseEvent VoceCambiata(Me, e)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _internal_VoceCambiata: {ex.Message}")
        End Try
    End Sub

    Public Sub aggiungiOpzione(testoVoce As String, evento As EventHandler)
        _internal.aggiungiOpzione(testoVoce, evento)
    End Sub

    ''' <summary>Falso per non dire "menù" prima del titolo, all'ingresso (per esempio nel centro notifiche).</summary>
    Public Property TitoloComeMenu As Boolean
        Get
            Return _internal.TitoloComeMenu
        End Get
        Set(value As Boolean)
            _internal.TitoloComeMenu = value
        End Set
    End Property

    Public Sub aggiungiOpzioneDopo(voceRiferimento As Object, testoNuovaVoce As String, eventoNuovaVoce As EventHandler)
        _internal.aggiungiOpzioneDopo(voceRiferimento, testoNuovaVoce, eventoNuovaVoce)
    End Sub

    Public Sub rimuoviOpzione(testoVoce As String)
        _internal.rimuoviOpzione(testoVoce)
    End Sub

    Public Sub sostituisciOpzione(voceDaSostituire As String, nuovaVoce As String, nuovoEvento As EventHandler)
        _internal.sostituisciOpzione(voceDaSostituire, nuovaVoce, nuovoEvento)
    End Sub

    Public Function opzioneEsiste(nomeOpzione As String) As Boolean
        Return _internal.opzioneEsiste(nomeOpzione)
    End Function

    Public Sub avvia(Optional dialogo As Boolean = False)
        _internal.avvia(dialogo)
    End Sub

    Public Sub pulisci(Optional isSelectionToggle As Boolean = False)
        _internal.pulisci(isSelectionToggle)
    End Sub

    Public Sub svuotaLista()
        _internal.svuotaLista()
    End Sub

    Public Function ottieniElementi() As List(Of String)
        Return _internal.ottieniElementi()
    End Function

    Public Sub opzioniPulisci()
        _internal.opzioniPulisci()
    End Sub

    ''' <summary>Vero mentre il menu delle opzioni (Alt) è aperto sopra la lista.</summary>
    Public ReadOnly Property OpzioniAperte As Boolean
        Get
            Try
                Return _internal.MenuOpzioniAperteAdesso
            Catch ex As Exception
                Return False
            End Try
        End Get
    End Property

    ''' <summary>Vero se la lista è stata chiusa (per esempio dalla chiusura dell'applicazione).</summary>
    Public ReadOnly Property Chiusa As Boolean
        Get
            Try
                Return _internal Is Nothing OrElse _internal.IsDisposed
            Catch ex As Exception
                Return True
            End Try
        End Get
    End Property

    ''' <summary>Vero se la lista è quella che riceve la tastiera adesso: visibile, in primo piano e senza finestre o attese sopra.</summary>
    Public Function PuoRicevereInput() As Boolean
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return False
            Return contenitore.PuoRicevereInput(_internal)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Sub selezionaElemento(indice As Integer)
        _internal.selezionaElemento(indice)
    End Sub

    Public Sub posiziona(id As String)
        _internal.posiziona(id)
    End Sub

    Public Function ottieniIndice() As Integer
        Return _internal.ottieniIndice()
    End Function

    Public Function ottieniElemento() As String
        Return _internal.ottieniElemento()
    End Function

    Public Function impostaTitolo(titolo As String) As Boolean
        Return _internal.impostaTitolo(titolo)
    End Function

    Public Sub abbandona()
        _internal.abbandona()
    End Sub

    Friend ReadOnly Property Interno As System.Windows.Forms.Control
        Get
            Return _internal
        End Get
    End Property

    Public Sub selezionaTuttiGliElementi()
        _internal.selezionaTuttiGliElementi()
    End Sub

    Public Sub selezionaElementiDaIndice(indiceInizio As Integer)
        _internal.selezionaElementiDaIndice(indiceInizio)
    End Sub

    Public Function ottieniElementoIndice(i As Integer) As String
        Return _internal.ottieniElementoIndice(i)
    End Function

    Public Sub selezionaElementoIndice(ByVal indice As Integer)
        _internal.selezionaElementoIndice(indice)
    End Sub

    Public Sub regolaLayout()
        _internal.regolaLayout()
    End Sub

    Public Sub aggiungiElemento(elemento As String, Optional id As String = "", Optional selezionato As Boolean = False)
        _internal.aggiungiElemento(elemento, id, selezionato)
    End Sub

    Public Sub CaricaElementiMassivi(dati As Dictionary(Of String, String))
        _internal.CaricaElementiMassivi(dati)
    End Sub

    Public Sub CaricaElementiMassiviNoID(lista As List(Of String))
        _internal.CaricaElementiMassiviNoID(lista)
    End Sub

    Public Sub CaricaListaDinamica(elementi As List(Of ElementoLista))
        _internal.CaricaListaDinamica(elementi)
    End Sub

    ''' <summary>
    ''' Aggiunge in un colpo solo tutti gli elementi indicati: molto più rapido di una serie di
    ''' chiamate a aggiungiElemento perché aggiorna la lista una volta sola.
    ''' </summary>
    Public Sub AggiungiElementi(elementi As List(Of ElementoLista))
        _internal.AggiungiElementi(elementi)
    End Sub

    ''' <summary>Svuota la memoria degli elementi selezionati.</summary>
    Public Sub AzzeraSelezioneMemorizzata()
        _internal.AzzeraSelezioneMemorizzata()
    End Sub

    ''' <summary>Elementi selezionati, compresi quelli dei blocchi non più a video.</summary>
    Public Function OttieniSelezioneMemorizzata() As List(Of String)
        Return _internal.OttieniSelezioneMemorizzata()
    End Function

    Public Sub BeginUpdate()
        _internal.BeginUpdate()
    End Sub

    Public Sub EndUpdate()
        _internal.EndUpdate()
    End Sub

    Public Sub RimuoviElemento(id As String)
        _internal.RimuoviElemento(id)
    End Sub

    Public Sub SelezionaUltimoElemento()
        _internal.SelezionaUltimoElemento()
    End Sub

    Public Function VoceEsiste(id As String) As Boolean
        Return _internal.VoceEsiste(id)
    End Function

    Public Sub AggiungiElementoDopo(idRiferimento As String, testoNuovo As String, Optional idNuovo As String = "")
        _internal.AggiungiElementoDopo(idRiferimento, testoNuovo, idNuovo)
    End Sub

    Public Sub AggiungiElementoPrima(idRiferimento As String, testoNuovo As String, Optional idNuovo As String = "")
        _internal.AggiungiElementoPrima(idRiferimento, testoNuovo, idNuovo)
    End Sub

    ''' <summary>
    ''' Cambia il testo visualizzato per l'elemento con l'id indicato, senza toccare la sua
    ''' posizione nella lista né la selezione corrente (utile ad es. per aggiornare la dicitura
    ''' "Non letto"/"Letto" di un messaggio già presente in lista).
    ''' </summary>
    Public Sub AggiornaTestoElemento(id As String, nuovoTesto As String)
        _internal.AggiornaTestoElemento(id, nuovoTesto)
    End Sub

    Public Sub DisattivaTimerChiusura()
        _internal.DisattivaTimerChiusura()
    End Sub
End Class

Public Class WGMenuListaInt
    Inherits System.Windows.Forms.UserControl
    Implements IWGChiudibile

    Public Sub RichiediChiusura() Implements IWGChiudibile.RichiediChiusura
        abbandona()
    End Sub

    Public Class ElementoListaWrapper
        Public Property Testo As String
        Public Property Id As String

        Public Sub New(t As String, i As String)
            Testo = t
            Id = i
        End Sub

        Public Overrides Function ToString() As String
            Return Testo
        End Function
    End Class

    Private Class Voce
        Public Property Nome As String
        Public Property Evento As EventHandler
    End Class

    Public Property NomeClasseChiamante As String = ""

    Private WithEvents menuOpzioni As WGListaView
    Private selezioneAttivata As Boolean = False
    Private controlloEseguito As Boolean = False
    Private visibile As Boolean = True
    Private utilita As WGFormUtils = WGFormUtils.GetInstance()
    Private scriviLog As New Logger()
    Private eventiOpzioni As New List(Of Voce)
    Private menuOpzioniAperto As Boolean = False

    Public ReadOnly Property MenuOpzioniAperteAdesso As Boolean
        Get
            Return menuOpzioniAperto
        End Get
    End Property
    Private ultimoElementoOpzioni As Integer = -1
    Private ultimoIndiceMenuPrincipale As Integer = -1

    Private elementiMenuPrincipale As New List(Of ElementoLista)
    Public canPlaySound As Boolean = True
    Private idSelezionatiPrimaDiOpzioni As New List(Of String)()

    Private Const WM_SYSCOMMAND As Integer = &H112
    Private Const SC_KEYMENU As Integer = &HF100
    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5

    Friend WithEvents menu As WGListaView
    Private _titoloComeMenu As Boolean = True

    ' Vale anche per la lista ricreata dopo il menu opzioni.
    Friend Property TitoloComeMenu As Boolean
        Get
            Return _titoloComeMenu
        End Get
        Set(value As Boolean)
            _titoloComeMenu = value
            If menu IsNot Nothing AndAlso Not menu.IsDisposed Then menu.TitoloComeMenu = value
        End Set
    End Property
    Private impostazioniPrecedenti As ImpostazioniMenu
    Private suonoAttesa As New WGSuoni()
    Private _stopwatchSvuotamento As New Stopwatch()
    Private _storicoSelezione As New Dictionary(Of String, String)()
    Private _suonoAttesaIniziato As Boolean = False
    Private _idDaRipristinareDopoToggle As String = Nothing
    Private _titoloOriginalePrimaDelCaricamento As String = ""

    Private _giaCaricato As Boolean = False
    Private _mantieniSelezioneTraPagine As Boolean = False
    Private _ordineSelezioniMemorizzate As New List(Of String)
    Private _insiemeSelezioniMemorizzate As New HashSet(Of String)(StringComparer.Ordinal)

    Private _selezionemenu As Boolean = True
    Private _multiselezioneConsentita As Boolean = False
    Private _invioNonConsentito As Boolean = False
    Private _invioBloccatoAutomaticamente As Boolean = False
    Private _ciclono As Boolean = False
    Private _richiestaSelezioneUltimo As Boolean = False
    Private _bloccoDisattivazioneSelezione As Boolean = False
    Public Property RicaricaSenzaNascondere As Boolean = False

    Public Property DisattivaMonitoraggioSvuotamento As Boolean = False

    Private _threadMonitorVisibilita As Thread
    Private _monitorVisibilitaInEsecuzione As Boolean = True

    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk

    Private _salvataggioItems As New List(Of Object)
    Private _salvataggioIndice As Integer = -1
    Private _salvataggioTitolo As String = ""
    Private _salvataggioSelezioneConsentita As Boolean = False
    Private _salvataggioCicloAttivo As Boolean = False
    Private _salvataggioSelectionMode As SelectionMode = SelectionMode.One
    Private _salvataggioIndiciSelezionati As New List(Of Integer)

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, X As Integer, Y As Integer, cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
    End Function

    Private Const SM_CXSCREEN As Integer = 0
    Private Const SM_CYSCREEN As Integer = 1
    Private Const SWP_SHOWWINDOW As UInteger = &H40
    Private Const WS_POPUP As Integer = &H80000000
    Private Const WS_VISIBLE As Integer = &H10000000
    Private Const GWL_STYLE As Integer = -16

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowLong(hWnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetActiveWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetFocus(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    Public Property selezioneMenu As Boolean
        Get
            Try
                Return _selezionemenu
            Catch ex As Exception
                scriviLog.ScriviLog($"selezioneMenu Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _selezionemenu = value
            Catch ex As Exception
                scriviLog.ScriviLog($"selezioneMenu Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property multiselezioneConsentita As Boolean
        Get
            Try
                Return _multiselezioneConsentita
            Catch ex As Exception
                scriviLog.ScriviLog($"multiselezioneConsentita Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                EseguiConTimeout(Sub() multiselezioneConsentita = value, "multiselezioneConsentita")
                Return
            End If
            Try
                _multiselezioneConsentita = value
                If Not IsControlDisposed(menu) Then
                    menu.SelezioneConsentita = value
                End If
                If value = True Then
                    ciclono = True
                End If
            Catch ex As Exception
                scriviLog.ScriviLog($"multiselezioneConsentita Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property invioNonConsentito As Boolean
        Get
            Try
                Return _invioNonConsentito
            Catch ex As Exception
                scriviLog.ScriviLog($"invioNonConsentito Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _invioNonConsentito = value
                _invioBloccatoAutomaticamente = False
            Catch ex As Exception
                scriviLog.ScriviLog($"invioNonConsentito Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Property MantieniSelezioneTraPagine As Boolean
        Get
            Try
                Return _mantieniSelezioneTraPagine
            Catch ex As Exception
                scriviLog.ScriviLog($"MantieniSelezioneTraPagine Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _mantieniSelezioneTraPagine = value
                If Not value Then
                    _ordineSelezioniMemorizzate.Clear()
                    _insiemeSelezioniMemorizzate.Clear()
                End If
            Catch ex As Exception
                scriviLog.ScriviLog($"MantieniSelezioneTraPagine Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public Sub AzzeraSelezioneMemorizzata()
        Try
            _ordineSelezioniMemorizzate.Clear()
            _insiemeSelezioniMemorizzate.Clear()
        Catch ex As Exception
            scriviLog.ScriviLog($"AzzeraSelezioneMemorizzata: {ex.ToString()}")
        End Try
    End Sub

    Public Function OttieniSelezioneMemorizzata() As List(Of String)
        Try
            AggiornaMemoriaSelezione()
            Return New List(Of String)(_ordineSelezioniMemorizzate)
        Catch ex As Exception
            scriviLog.ScriviLog($"OttieniSelezioneMemorizzata: {ex.ToString()}")
            Return New List(Of String)
        End Try
    End Function

    ''' <summary>
    ''' Allinea la memoria a quanto è selezionato adesso nella lista: gli elementi del blocco a
    ''' video selezionati entrano in memoria, quelli deselezionati ne escono. Gli elementi degli
    ''' altri blocchi restano come erano.
    ''' </summary>
    Private Sub AggiornaMemoriaSelezione()
        Try
            If Not _mantieniSelezioneTraPagine Then Return
            If IsControlDisposed(menu) Then Return
            If menu.SelectionMode <> SelectionMode.MultiSimple AndAlso menu.SelectionMode <> SelectionMode.MultiExtended Then Return

            For i As Integer = 0 To menu.Items.Count - 1
                Dim wrapper As ElementoListaWrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                If wrapper Is Nothing OrElse String.IsNullOrEmpty(wrapper.Id) Then Continue For
                If menu.GetSelected(i) Then
                    If Not _insiemeSelezioniMemorizzate.Contains(wrapper.Id) Then
                        _insiemeSelezioniMemorizzate.Add(wrapper.Id)
                        _ordineSelezioniMemorizzate.Add(wrapper.Id)
                    End If
                Else
                    If _insiemeSelezioniMemorizzate.Contains(wrapper.Id) Then
                        _insiemeSelezioniMemorizzate.Remove(wrapper.Id)
                        _ordineSelezioniMemorizzate.Remove(wrapper.Id)
                    End If
                End If
            Next
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"AggiornaMemoriaSelezione - ObjectDisposedException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"AggiornaMemoriaSelezione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>
    ''' Rimette in evidenza, sugli elementi appena caricati, quelli che risultano selezionati in
    ''' memoria, ripristinando anche la modalità di multiselezione.
    ''' </summary>
    Private Sub ApplicaMemoriaSelezione()
        Try
            If Not _mantieniSelezioneTraPagine Then Return
            If Not _multiselezioneConsentita Then Return
            If IsControlDisposed(menu) Then Return
            If _insiemeSelezioniMemorizzate.Count = 0 Then Return

            Dim trovato As Boolean = False
            menu.BeginUpdate()
            Try
                For i As Integer = 0 To menu.Items.Count - 1
                    Dim wrapper As ElementoListaWrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                    If wrapper Is Nothing OrElse String.IsNullOrEmpty(wrapper.Id) Then Continue For
                    If Not _insiemeSelezioniMemorizzate.Contains(wrapper.Id) Then Continue For
                    If Not trovato Then
                        If menu.SelectionMode <> SelectionMode.MultiSimple AndAlso menu.SelectionMode <> SelectionMode.MultiExtended Then
                            menu.SelectionMode = SelectionMode.MultiSimple
                        End If
                        trovato = True
                    End If
                    menu.SetSelected(i, True)
                Next
            Finally
                menu.EndUpdate()
            End Try

            ' Se in questa pagina non c'e' niente da riselezionare non si tocca niente: la
            ' modalita' di selezione resta quella che era, e le scelte fatte nelle altre pagine
            ' restano in memoria senza comparire qui.
            If trovato Then
                selezioneAttivata = True
                _bloccoDisattivazioneSelezione = True
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"ApplicaMemoriaSelezione - ObjectDisposedException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"ApplicaMemoriaSelezione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Property ciclono As Boolean
        Get
            Try
                Return _ciclono
            Catch ex As Exception
                scriviLog.ScriviLog($"ciclono Get - Errore generico: {ex.ToString()}")
                Return False
            End Try
        End Get
        Set(value As Boolean)
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                EseguiConTimeout(Sub() ciclono = value, "ciclono")
                Return
            End If
            Try
                _ciclono = value
                If Not IsControlDisposed(menu) Then
                    menu.ciclono = value
                End If
            Catch ex As Exception
                scriviLog.ScriviLog($"ciclono Set - Errore generico: {ex.ToString()}")
            End Try
        End Set
    End Property

    Public ReadOnly Property TestoSelezionato As String
        Get
            If Me.InvokeRequired Then
                Return EseguiConTimeout(Of String)(Function() TestoSelezionato, String.Empty, "TestoSelezionato")
            End If
            Try
                If Me.IsDisposed OrElse Me.Disposing OrElse IsControlDisposed(menu) Then
                    Return String.Empty
                End If

                If menuOpzioniAperto Then
                    Return impostazioniPrecedenti.UltimoTestoSelezionato
                End If

                If menu.SelectedIndex > -1 AndAlso menu.SelectedItem IsNot Nothing Then
                    Return menu.SelectedItem.ToString()
                Else
                    Return String.Empty
                End If
            Catch ex As Exception
                scriviLog.ScriviLog($"Errore in TestoSelezionato: {ex.ToString()}")
                Return String.Empty
            End Try
        End Get
    End Property

    Public ReadOnly Property ValoreSelezionato As String
        Get
            If Me.InvokeRequired Then
                Return EseguiConTimeout(Of String)(Function() ValoreSelezionato, String.Empty, "ValoreSelezionato")
            End If
            Try
                If Me.IsDisposed OrElse Me.Disposing OrElse IsControlDisposed(menu) Then
                    Return String.Empty
                End If

                If menuOpzioniAperto Then
                    Return impostazioniPrecedenti.UltimoIdSelezionato
                End If

                If menu.SelectedIndex > -1 AndAlso menu.SelectedItem IsNot Nothing Then
                    Dim wrapper As ElementoListaWrapper = TryCast(menu.SelectedItem, ElementoListaWrapper)
                    If wrapper IsNot Nothing Then
                        Return wrapper.Id
                    Else
                        Return menu.SelectedItem.ToString()
                    End If
                Else
                    Return String.Empty
                End If
            Catch ex As Exception
                scriviLog.ScriviLog($"Errore in ValoreSelezionato: {ex.ToString()}")
                Return String.Empty
            End Try
        End Get
    End Property

    Public Event inizio As EventHandler
    Public Event fine As EventHandler
    Public Event iniziolista As EventHandler
    Public Event finelista As EventHandler
    Public Event ElementiSelezionati(ids As List(Of String))
    Public Event ElementoSelezionato(id As String)
    Public Event escPremuto As EventHandler
    Public Event VoceCambiata As EventHandler(Of VoceCambiataEventArgs)
    Public Event ScorciatoiaCtrl As EventHandler(Of ScorciatoiaCtrlEventArgs)

    Private Const MARGIN_TOP As Integer = 20
    Private Const MARGIN_LEFT As Integer = 20
    Private Const MARGIN_RIGHT As Integer = 20
    Private Const MARGIN_BOTTOM As Integer = 20
    Private Const SPACING_TITLE_MENU As Integer = 10

    Public Sub New()
        menu = New WGListaView()
        menu.TitoloComeMenu = _titoloComeMenu
        menu.Visible = False
        impostazioniPrecedenti = New ImpostazioniMenu()

        _tastiera = WGTastiera.GetInstance()
        _touk = WGTouk.GetInstance()

        AddHandler Me.Resize, AddressOf Me_Resize
        AddHandler menu.SelectedIndexChanged, AddressOf Menu_SelectedIndexChanged

        Me.AccessibleName = " "
        Me.AccessibleRole = AccessibleRole.None

        Try
            If Not Application.RenderWithVisualStyles Then
                Application.EnableVisualStyles()
            End If

            Me.Controls.Add(menu)

            Me.MinimumSize = New Size(600, 400)

            Me.AccessibleRole = AccessibleRole.None
            Me.AccessibleName = " "

            pulisci()

            _threadMonitorVisibilita = New Thread(AddressOf MonitorVisibilitaLoop)
            _threadMonitorVisibilita.IsBackground = True
            _threadMonitorVisibilita.Start()

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"Costruttore WGMenuLista - ObjectDisposedException: {ex.ToString()}")
            Return
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"Costruttore WGMenuLista - Errore InvalidOperation: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"Costruttore WGMenuLista - Errore NullReference: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"Costruttore WGMenuLista - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            If tasto = Keys.None Then Return

            Dim codiceTasto As Keys = tasto And Keys.KeyCode

            ' Ctrl con una lettera: se chi ospita l'elenco la gestisce, il tasto non fa altro
            ' (anche Ctrl con freccia su o giù: chi ospita l'elenco in un campo la usa per passare al campo vicino)
            If ((codiceTasto >= Keys.A AndAlso codiceTasto <= Keys.Z) OrElse codiceTasto = Keys.Up OrElse codiceTasto = Keys.Down) AndAlso Not menuOpzioniAperto Then
                Dim modificatori As Keys = _tastiera.ModificatoriUltimoTasto
                If (modificatori And Keys.Control) = Keys.Control AndAlso (modificatori And Keys.Alt) <> Keys.Alt Then
                    Dim richiesta As New ScorciatoiaCtrlEventArgs(codiceTasto, (modificatori And Keys.Shift) = Keys.Shift)
                    RaiseEvent ScorciatoiaCtrl(Me, richiesta)
                    If richiesta.Gestita Then
                        tasto = Keys.None
                        Return
                    End If
                End If
            End If

            If codiceTasto = Keys.Tab AndAlso My.Computer.Keyboard.AltKeyDown Then
                If menuOpzioniAperto Then
                    chiudiOpzioni()
                End If
                tasto = Keys.None
                Return
            End If

            Select Case codiceTasto
                Case Keys.Enter, Keys.Return, Keys.Escape, Keys.Back, Keys.Left, Keys.Right, Keys.F1
                    tastoPremuto(codiceTasto)
                    tasto = Keys.None
                Case Else
                    tastoPremuto(codiceTasto)
            End Select

        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _tastiera_KeyDown: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            gestisciAlt()
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _tastiera_AltPremuto: {ex.Message}")
        End Try
    End Sub

    Private Sub RiaggancioTouchSuLista(lista As WGListaView)
        Try
            If lista Is Nothing OrElse lista.IsDisposed OrElse Not lista.IsHandleCreated Then Return
            If _touk IsNot Nothing Then _touk.RiassegnaControllo(lista.Handle)
        Catch ex As Exception
            scriviLog.ScriviLog($"RiaggancioTouchSuLista: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_TapDoppio() Handles _touk.TapDoppio
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            tastoPremuto(Keys.Enter)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _touk_TapDoppio: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_EscapeGesto() Handles _touk.EscapeGesto
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            tastoPremuto(Keys.Escape)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _touk_EscapeGesto: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_FlickSu() Handles _touk.FlickSu
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.Visible OrElse WGContenitore.Istanza Is Nothing OrElse Not WGContenitore.Istanza.PuoRicevereInput(Me) Then Return
            AggiornaAzioniTouch()
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in _touk_FlickSu: {ex.Message}")
        End Try
    End Sub


    Private Function IsControlDisposed(ctrl As Control) As Boolean
        Return ctrl Is Nothing OrElse ctrl.IsDisposed
    End Function

    ' ============================================================================================
    ' PASSAGGIO AL THREAD DELL'INTERFACCIA CON TIMEOUT
    ' ============================================================================================
    ' Control.Invoke aspetta senza limiti che il thread dell'interfaccia sia libero di eseguire il
    ' codice passato: se quel thread e' occupato o bloccato altrove, chi chiama resta fermo per
    ' sempre, senza eccezioni e senza log, ed e' esattamente quello che si manifesta come "il
    ' controllo si blocca". Questi metodi fanno lo stesso passaggio ma con un limite di tempo: se
    ' scade, si registra l'accaduto e si prosegue con un valore di default invece di restare fermi.
    Private Const TimeoutMarshalMs As Integer = 5000

    Private Sub EseguiConTimeout(azione As MethodInvoker, nomeChiamante As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            Dim risultato As IAsyncResult = Me.BeginInvoke(azione)
            If Not risultato.AsyncWaitHandle.WaitOne(TimeoutMarshalMs) Then
                scriviLog.ScriviLog($"{nomeChiamante} - timeout di {TimeoutMarshalMs} ms nel passaggio al thread dell'interfaccia: il thread dell'interfaccia risulta occupato o bloccato.")
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"{nomeChiamante} - Errore nel passaggio al thread dell'interfaccia: {ex.ToString()}")
        End Try
    End Sub

    Private Function EseguiConTimeout(Of T)(funzione As Func(Of T), valoreDiDefault As T, nomeChiamante As String) As T
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return valoreDiDefault
            Dim risultato As IAsyncResult = Me.BeginInvoke(funzione)
            If risultato.AsyncWaitHandle.WaitOne(TimeoutMarshalMs) Then
                Return CType(Me.EndInvoke(risultato), T)
            Else
                scriviLog.ScriviLog($"{nomeChiamante} - timeout di {TimeoutMarshalMs} ms nel passaggio al thread dell'interfaccia: il thread dell'interfaccia risulta occupato o bloccato.")
                Return valoreDiDefault
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"{nomeChiamante} - Errore nel passaggio al thread dell'interfaccia: {ex.ToString()}")
            Return valoreDiDefault
        End Try
    End Function

    Private Sub EseguiConTimeoutSu(bersaglio As Control, azione As MethodInvoker, nomeChiamante As String)
        Try
            If bersaglio Is Nothing OrElse bersaglio.IsDisposed OrElse Not bersaglio.IsHandleCreated Then Return
            Dim risultato As IAsyncResult = bersaglio.BeginInvoke(azione)
            If Not risultato.AsyncWaitHandle.WaitOne(TimeoutMarshalMs) Then
                scriviLog.ScriviLog($"{nomeChiamante} - timeout di {TimeoutMarshalMs} ms nel passaggio al thread dell'interfaccia (via WGContenitore): il thread dell'interfaccia risulta occupato o bloccato.")
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"{nomeChiamante} - Errore nel passaggio al thread dell'interfaccia (via WGContenitore): {ex.ToString()}")
        End Try
    End Sub

    Private Function EseguiConTimeoutSu(Of T)(bersaglio As Control, funzione As Func(Of T), valoreDiDefault As T, nomeChiamante As String) As T
        Try
            If bersaglio Is Nothing OrElse bersaglio.IsDisposed OrElse Not bersaglio.IsHandleCreated Then Return valoreDiDefault
            Dim risultato As IAsyncResult = bersaglio.BeginInvoke(funzione)
            If risultato.AsyncWaitHandle.WaitOne(TimeoutMarshalMs) Then
                Return CType(bersaglio.EndInvoke(risultato), T)
            Else
                scriviLog.ScriviLog($"{nomeChiamante} - timeout di {TimeoutMarshalMs} ms nel passaggio al thread dell'interfaccia (via WGContenitore): il thread dell'interfaccia risulta occupato o bloccato.")
                Return valoreDiDefault
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"{nomeChiamante} - Errore nel passaggio al thread dell'interfaccia (via WGContenitore): {ex.ToString()}")
            Return valoreDiDefault
        End Try
    End Function

    Private Sub MonitorVisibilitaLoop()
        Return
    End Sub

    Private Sub Menu_SelectedIndexChanged(sender As Object, e As EventArgs)
        If Me.IsDisposed OrElse Me.Disposing OrElse IsControlDisposed(menu) Then
            Return
        End If

        If menu.SelectionMode = SelectionMode.MultiSimple OrElse menu.SelectionMode = SelectionMode.MultiExtended Then
            Return
        End If

        Try
            Dim wrapper As ElementoListaWrapper = TryCast(menu.SelectedItem, ElementoListaWrapper)
            If wrapper IsNot Nothing AndAlso Not String.IsNullOrEmpty(wrapper.Id) Then
                RaiseEvent VoceCambiata(Me, New VoceCambiataEventArgs(wrapper.Id))
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"Menu_SelectedIndexChanged - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub attivataselezione() Handles menu.SelezioneAttivata
        Try
            selezioneAttivata = True
        Catch ex As Exception
            scriviLog.ScriviLog($"attivataselezione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub disattivataselezione() Handles menu.SelezioneDisattivata
        Try
            selezioneAttivata = False
        Catch ex As Exception
            scriviLog.ScriviLog($"disattivataselezione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub vaiIndietro() Handles menu.Indietro
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            gestisciEsc()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"vaiIndietro - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"vaiIndietro - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub vaiIndietroOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            chiudiOpzioni()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"vaiIndietroOpzioni - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"vaiIndietroOpzioni - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub vaiAvanti() Handles menu.Avanti
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            gestisciEnter()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"vaiAvanti - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"vaiAvanti - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub vaiAvantiOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            gestisciEnter()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"vaiAvantiOpzioni - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"vaiAvantiOpzioni - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>
    ''' Travasa i nomi delle opzioni nella lista principale, che li fa scorrere con il flick
    ''' verticale annunciandoli, senza aprire il menu opzioni.
    ''' </summary>
    Private Sub AggiornaAzioniTouch()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If IsControlDisposed(menu) Then Return

            Dim nomi As New List(Of String)()
            For Each v As Voce In eventiOpzioni
                If v IsNot Nothing AndAlso Not String.IsNullOrEmpty(v.Nome) Then
                    If Not nomi.Contains(v.Nome) Then nomi.Add(v.Nome)
                End If
            Next

            menu.AzioniDisponibili = nomi
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in AggiornaAzioniTouch: {ex.ToString()}")
        End Try
    End Sub

    Private Sub menu_AzioneAttivata(nomeAzione As String) Handles menu.AzioneAttivata
        Try
            If String.IsNullOrEmpty(nomeAzione) Then Return

            Dim voceEvento As Voce = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = nomeAzione)
            If voceEvento Is Nothing OrElse voceEvento.Evento Is Nothing Then Return

            voceEvento.Evento.Invoke(Me, EventArgs.Empty)
        Catch ex As Exception
            scriviLog.ScriviLog($"Errore in menu_AzioneAttivata: {ex.ToString()}")
        End Try
    End Sub

    Private Sub gestisciAlt()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf gestisciAlt, "gestisciAlt")
            Return
        End If
        Try
            If Not menuOpzioniAperto Then
                apriOpzioni()
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"gestisciAlt - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"gestisciAlt - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub inizioMenuLista() Handles menu.InizioListaRaggiunto
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            AggiornaMemoriaSelezione()
            RaiseEvent inizio(Me, Nothing)
            RaiseEvent iniziolista(Me, Nothing)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"inizioMenuLista - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"inizioMenuLista - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub fineMenuLista() Handles menu.FineListaRaggiunta
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            AggiornaMemoriaSelezione()
            RaiseEvent fine(Me, Nothing)
            RaiseEvent finelista(Me, Nothing)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"fineMenuLista - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"fineMenuLista - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub tastoPremuto(KeyData As Keys)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() tastoPremuto(KeyData), "tastoPremuto")
            Return
        End If

        Try
            If menuOpzioniAperto AndAlso Not IsControlDisposed(menuOpzioni) Then
                Select Case KeyData
                    Case Keys.Escape, Keys.Back
                        chiudiOpzioni()
                    Case Keys.Enter, Keys.Right
                        Dim testoVoce As String = If(menuOpzioni.SelectedItem IsNot Nothing, menuOpzioni.SelectedItem.ToString(), Nothing)
                        ' Si ripristina prima la lista principale: all'apertura delle opzioni viene distrutta e chiudiOpzioni
                        ' la ricostruisce dallo snapshot, quindi un'opzione che cambia l'elenco (regolazione affinata) altrimenti
                        ' lavorerebbe su una lista inesistente e il suo effetto andrebbe perso.
                        chiudiOpzioni()
                        If testoVoce IsNot Nothing Then
                            Dim voceEvento = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = testoVoce)
                            If voceEvento IsNot Nothing Then
                                voceEvento.Evento?.Invoke(Me, EventArgs.Empty)
                            End If
                        End If
                End Select
            Else
                Select Case KeyData
                    Case Keys.F1
                        WGGuida.MostraGuidaWGMenuLista()
                    Case Keys.Escape, Keys.Back
                        gestisciEsc()
                    Case Keys.Enter
                        gestisciEnter()
                    Case Keys.Left
                        gestisciEsc()
                    Case Keys.Right
                        gestisciEnter()
                End Select
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"tastoPremuto - ObjectDisposedException: {ex.ToString()}")
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"tastoPremuto - InvalidOperationException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"tastoPremuto - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            MyBase.OnLoad(e)
            regolaLayout()
            _giaCaricato = True

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"OnLoad - ObjectDisposedException: {ex.ToString()}")
            Return
        Catch ex As Win32Exception
            scriviLog.ScriviLog($"OnLoad - Errore Win32: {ex.ToString()}")
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"OnLoad - Errore InvalidOperation: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"OnLoad - Errore NullReference: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"OnLoad - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            If m.Msg = WM_SYSCOMMAND AndAlso (m.WParam.ToInt32() = SC_KEYMENU) Then
                Return
            End If
            MyBase.WndProc(m)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"WndProc - ObjectDisposedException: {ex.ToString()}")
            Return
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"WndProc - Errore NullReference: {ex.ToString()}")
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"WndProc - Errore InvalidOperation: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"WndProc - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub aggiungiOpzione(testoVoce As String, evento As EventHandler)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() aggiungiOpzione(testoVoce, evento), "aggiungiOpzione")
            Return
        End If

        Try
            If Not eventiOpzioni.Any(Function(v) v.Nome = testoVoce) Then
                eventiOpzioni.Add(New Voce With {.Nome = testoVoce, .Evento = evento})
                If Not IsControlDisposed(menuOpzioni) AndAlso Not menuOpzioni.Items.Contains(testoVoce) Then
                    menuOpzioni.Items.Add(testoVoce)
                End If
                AggiornaAzioniTouch()
            End If
            regolaLayout()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"aggiungiOpzione - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"aggiungiOpzione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub aggiungiOpzioneDopo(voceRiferimento As Object, testoNuovaVoce As String, eventoNuovaVoce As EventHandler)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() aggiungiOpzioneDopo(voceRiferimento, testoNuovaVoce, eventoNuovaVoce), "aggiungiOpzioneDopo")
            Return
        End If

        Try
            Dim indexRiferimento As Integer = -1

            If TypeOf voceRiferimento Is String Then
                indexRiferimento = eventiOpzioni.FindIndex(Function(v) v.Nome = CStr(voceRiferimento))
            ElseIf TypeOf voceRiferimento Is Integer Then
                Dim tempIndex As Integer = CInt(voceRiferimento)
                If tempIndex >= 0 AndAlso tempIndex < eventiOpzioni.Count Then
                    indexRiferimento = tempIndex
                End If
            End If

            If indexRiferimento = -1 Then
                If Not eventiOpzioni.Any(Function(v) v.Nome = testoNuovaVoce) Then
                    eventiOpzioni.Add(New Voce With {.Nome = testoNuovaVoce, .Evento = eventoNuovaVoce})
                    If Not IsControlDisposed(menuOpzioni) AndAlso Not menuOpzioni.Items.Contains(testoNuovaVoce) Then
                        menuOpzioni.Items.Add(testoNuovaVoce)
                    End If
                End If
            Else
                If Not eventiOpzioni.Any(Function(v) v.Nome = testoNuovaVoce) Then
                    Dim indiceInserimento As Integer = indexRiferimento + 1
                    Dim nuovoElementoVoce As New Voce With {.Nome = testoNuovaVoce, .Evento = eventoNuovaVoce}
                    eventiOpzioni.Insert(indiceInserimento, nuovoElementoVoce)

                    If Not IsControlDisposed(menuOpzioni) AndAlso Not menuOpzioni.Items.Contains(testoNuovaVoce) Then
                        menuOpzioni.Items.Insert(indiceInserimento, testoNuovaVoce)
                    End If
                End If
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"aggiungiOpzioneDopo - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub rimuoviOpzione(testoVoce As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() rimuoviOpzione(testoVoce), "rimuoviOpzione")
            Return
        End If

        Try
            eventiOpzioni.RemoveAll(Function(v) v.Nome = testoVoce)
            If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains(testoVoce) Then
                menuOpzioni.Items.Remove(testoVoce)
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"rimuoviOpzione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub sostituisciOpzione(voceDaSostituire As String, nuovaVoce As String, nuovoEvento As EventHandler)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() sostituisciOpzione(voceDaSostituire, nuovaVoce, nuovoEvento), "sostituisciOpzione")
            Return
        End If

        Try
            Dim voceEsistente = eventiOpzioni.FirstOrDefault(Function(v) v.Nome = voceDaSostituire)
            If voceEsistente IsNot Nothing Then
                eventiOpzioni.Remove(voceEsistente)
                If Not eventiOpzioni.Any(Function(v) v.Nome = nuovaVoce) Then
                    eventiOpzioni.Add(New Voce With {.Nome = nuovaVoce, .Evento = nuovoEvento})
                Else
                    Dim nuova = eventiOpzioni.First(Function(v) v.Nome = nuovaVoce)
                    nuova.Evento = nuovoEvento
                End If
            End If

            If Not IsControlDisposed(menuOpzioni) Then
                Dim index As Integer = menuOpzioni.Items.IndexOf(voceDaSostituire)
                If index <> -1 Then
                    menuOpzioni.Items(index) = nuovaVoce
                End If
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"sostituisciOpzione - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Function opzioneEsiste(nomeOpzione As String) As Boolean
        If Me.IsDisposed OrElse Me.Disposing Then Return False
        If Me.InvokeRequired Then
            Return EseguiConTimeout(Of Boolean)(Function() opzioneEsiste(nomeOpzione), False, "opzioneEsiste")
        End If

        Try
            Return eventiOpzioni.Any(Function(v) v.Nome = nomeOpzione)
        Catch ex As Exception
            scriviLog.ScriviLog($"opzioneEsiste - Errore generico: {ex.ToString()}")
            Return False
        End Try
    End Function

    Public Sub avvia(Optional dialogo As Boolean = False)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim contenitoreMarshal As WGContenitore = WGContenitore.Istanza
        If contenitoreMarshal IsNot Nothing AndAlso contenitoreMarshal.InvokeRequired Then
            EseguiConTimeoutSu(contenitoreMarshal, Sub() avvia(dialogo), "avvia")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            visibile = True

            If contenitoreMarshal Is Nothing Then
                Return
            End If

            contenitoreMarshal.MostraControllo(Me)
            Me.SuspendLayout()
            Try
                menu.Visible = True
                regolaLayout()
            Finally
                Me.ResumeLayout(True)
            End Try

            menu.BringToFront()
            Try
                menu.Invalidate()
                menu.Update()
            Catch exRefresh As Exception
                scriviLog.ScriviLog($"Avvia - refresh menu: {exRefresh.Message}")
            End Try

            Try
                menu.Select()
                menu.Focus()
                RiaggancioTouchSuLista(menu)
            Catch exFocus As Exception
                scriviLog.ScriviLog($"Avvia - focus menu: {exFocus.Message}")
            End Try

            ' Rete di sicurezza: mezzo secondo dopo, l'elenco che non si è disegnato o annunciato lo fa.
            AssicuraIngressoDopo(menu)

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"Avvia - ObjectDisposedException: {ex.ToString()}")
            Return
        Catch ex As ArgumentNullException
            scriviLog.ScriviLog($"Avvia - Errore ArgumentNull: {ex.ToString()}")
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"Avvia - Errore InvalidOperation: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"Avvia - Errore NullReference: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"Avvia - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Async Sub AssicuraIngressoDopo(elenco As WGListaView)
        Try
            Await System.Threading.Tasks.Task.Delay(500)
            If Me.IsDisposed OrElse Not visibile OrElse IsControlDisposed(elenco) OrElse elenco IsNot menu Then Return
            elenco.AssicuraIngresso()
        Catch ex As Exception
            scriviLog.ScriviLog($"AssicuraIngressoDopo: {ex.Message}")
        End Try
    End Sub

    Private Sub menuOpzioni_SelectedIndexChanged(sender As Object, e As EventArgs)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If IsControlDisposed(menuOpzioni) Then Return

        Try
            If menuOpzioni.SelectedItem IsNot Nothing Then
                ultimoElementoOpzioni = menuOpzioni.SelectedIndex
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"menuOpzioni_SelectedIndexChanged - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"menuOpzioni_SelectedIndexChanged - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub apriOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If IsControlDisposed(menu) Then Return

        Try
            If menuOpzioniAperto Then Return
            If eventiOpzioni.Count = 0 Then
                Try
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Non ci sono opzioni disponibili")
                    messaggio.ShowDialog()
                Catch exMessaggio As Exception
                    scriviLog.ScriviLog($"apriOpzioni - messaggio nessuna opzione: {exMessaggio.ToString()}")
                End Try
                Return
            End If

            Dim menuOpzioniEraGiaCreato As Boolean = (menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed)
            Dim nuovoMenuOpzioni As WGListaView = Nothing

            ' Fase 1: si costruisce e si porta a fuoco il nuovo menu opzioni senza toccare ancora
            ' ne' il segnaposto menuOpzioniAperto ne' la lista principale. Se qualcosa va storto
            ' qui, la lista principale e' ancora li' intatta e funzionante: si ripristina il fuoco
            ' su di essa invece di restare in uno stato a meta', che e' quello che blocca il
            ' controllo agli input finche' non lo si chiude e riapre.
            Try
                AggiornaMemoriaSelezione()
                idSelezionatiPrimaDiOpzioni.Clear()
                _salvataggioItems.Clear()
                _salvataggioIndiciSelezionati.Clear()

                For i As Integer = 0 To menu.Items.Count - 1
                    _salvataggioItems.Add(menu.Items(i))
                    If menu.GetSelected(i) Then
                        _salvataggioIndiciSelezionati.Add(i)
                        Dim wrap = TryCast(menu.Items(i), ElementoListaWrapper)
                        If wrap IsNot Nothing Then idSelezionatiPrimaDiOpzioni.Add(wrap.Id)
                    End If
                Next

                _salvataggioIndice = menu.SelectedIndex
                _salvataggioTitolo = menu.Titolo
                _salvataggioSelezioneConsentita = menu.SelezioneConsentita
                _salvataggioSelectionMode = menu.SelectionMode

                If menuOpzioniEraGiaCreato Then
                    nuovoMenuOpzioni = menuOpzioni
                Else
                    nuovoMenuOpzioni = New WGListaView()
                    nuovoMenuOpzioni.Visible = False
                End If

                nuovoMenuOpzioni.Titolo = "Opzioni " & _salvataggioTitolo

                For Each v As Voce In eventiOpzioni
                    If Not nuovoMenuOpzioni.Items.Contains(v.Nome) Then
                        nuovoMenuOpzioni.Items.Add(v.Nome)
                    End If
                Next

                If Not menuOpzioniEraGiaCreato Then
                    Me.Controls.Add(nuovoMenuOpzioni)
                    AddHandler nuovoMenuOpzioni.SelectedIndexChanged, AddressOf menuOpzioni_SelectedIndexChanged
                    AddHandler nuovoMenuOpzioni.Indietro, AddressOf vaiIndietroOpzioni
                    AddHandler nuovoMenuOpzioni.Avanti, AddressOf vaiAvantiOpzioni
                End If

                nuovoMenuOpzioni.Visible = True
                nuovoMenuOpzioni.BringToFront()

                If ultimoElementoOpzioni >= 0 AndAlso ultimoElementoOpzioni < nuovoMenuOpzioni.Items.Count Then
                    nuovoMenuOpzioni.SelectedIndex = ultimoElementoOpzioni
                Else
                    nuovoMenuOpzioni.SelectedIndex = 0
                End If
                SetFocus(nuovoMenuOpzioni.Handle)
                RiaggancioTouchSuLista(nuovoMenuOpzioni)
            Catch exCostruzione As Exception
                scriviLog.ScriviLog($"apriOpzioni (costruzione menu opzioni): {exCostruzione.ToString()}")
                Try
                    If Not menuOpzioniEraGiaCreato AndAlso nuovoMenuOpzioni IsNot Nothing AndAlso Not nuovoMenuOpzioni.IsDisposed Then
                        RemoveHandler nuovoMenuOpzioni.SelectedIndexChanged, AddressOf menuOpzioni_SelectedIndexChanged
                        RemoveHandler nuovoMenuOpzioni.Indietro, AddressOf vaiIndietroOpzioni
                        RemoveHandler nuovoMenuOpzioni.Avanti, AddressOf vaiAvantiOpzioni
                        If Me.Controls.Contains(nuovoMenuOpzioni) Then Me.Controls.Remove(nuovoMenuOpzioni)
                        nuovoMenuOpzioni.Dispose()
                    End If
                    If Not IsControlDisposed(menu) Then
                        menu.Select()
                        menu.Focus()
                        RiaggancioTouchSuLista(menu)
                    End If
                Catch exRipristino As Exception
                    scriviLog.ScriviLog($"apriOpzioni (ripristino dopo errore): {exRipristino.ToString()}")
                End Try
                Return
            End Try

            ' Fase 2: il menu opzioni e' pronto e a fuoco. Da qui in poi si passa la mano e si
            ' chiude la lista principale: un errore in questa parte non deve piu' annullare il
            ' passaggio gia' fatto, si registra soltanto.
            Try
                menuOpzioni = nuovoMenuOpzioni
                menuOpzioniAperto = True
                regolaLayout()
                Me.Controls.Remove(menu)
                menu.Dispose()
                menu = Nothing
            Catch exChiusuraLista As ObjectDisposedException
                scriviLog.ScriviLog($"apriOpzioni - ObjectDisposedException: {exChiusuraLista.ToString()}")
            Catch exChiusuraLista As NullReferenceException
                scriviLog.ScriviLog($"NullReferenceException: {exChiusuraLista.ToString()}")
            Catch exChiusuraLista As Exception
                scriviLog.ScriviLog($"apriOpzioni (chiusura lista principale): {exChiusuraLista.ToString()}")
            End Try

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"apriOpzioni - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"apriOpzioni - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub chiudiOpzioni()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim contenitoreMarshalChiudi As WGContenitore = WGContenitore.Istanza
        If contenitoreMarshalChiudi IsNot Nothing AndAlso contenitoreMarshalChiudi.InvokeRequired Then
            EseguiConTimeoutSu(contenitoreMarshalChiudi, AddressOf chiudiOpzioni, "chiudiOpzioni")
            Return
        End If

        Dim nuovoMenu As WGListaView = Nothing
        Dim indiceSelezionatoOpzioni As Integer = -1

        ' Fase 1: si costruisce e si porta a fuoco la nuova lista principale senza toccare ancora
        ' ne' il segnaposto menuOpzioniAperto ne' il menu opzioni. Se qualcosa va storto qui, il
        ' menu opzioni e' ancora li' intatto e funzionante: si ripristina il fuoco su di esso
        ' invece di restare in uno stato a meta'.
        Try
            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                Try
                    indiceSelezionatoOpzioni = menuOpzioni.SelectedIndex
                Catch exIndice As Exception
                    scriviLog.ScriviLog($"chiudiOpzioni - lettura indice opzioni: {exIndice.ToString()}")
                End Try
            End If

            nuovoMenu = New WGListaView()
            nuovoMenu.TitoloComeMenu = _titoloComeMenu
            nuovoMenu.Visible = False
            nuovoMenu.Titolo = _salvataggioTitolo
            nuovoMenu.SelezioneConsentita = _salvataggioSelezioneConsentita
            nuovoMenu.SelectionMode = _salvataggioSelectionMode

            nuovoMenu.BeginUpdate()
            For Each itm In _salvataggioItems
                nuovoMenu.Items.Add(itm)
            Next
            nuovoMenu.EndUpdate()

            For Each i In _salvataggioIndiciSelezionati
                If i >= 0 AndAlso i < nuovoMenu.Items.Count Then
                    nuovoMenu.SetSelected(i, True)
                End If
            Next

            Me.Controls.Add(nuovoMenu)
            AddHandler nuovoMenu.SelectedIndexChanged, AddressOf Menu_SelectedIndexChanged

            If _salvataggioIndice >= 0 AndAlso _salvataggioIndice < nuovoMenu.Items.Count Then
                nuovoMenu.SelectedIndex = _salvataggioIndice
            End If

            nuovoMenu.Visible = True
            nuovoMenu.BringToFront()
            SetFocus(nuovoMenu.Handle)
            RiaggancioTouchSuLista(nuovoMenu)
        Catch exCostruzione As Exception
            scriviLog.ScriviLog($"chiudiOpzioni (costruzione lista principale): {exCostruzione.ToString()}")
            Try
                If nuovoMenu IsNot Nothing AndAlso Not nuovoMenu.IsDisposed Then
                    RemoveHandler nuovoMenu.SelectedIndexChanged, AddressOf Menu_SelectedIndexChanged
                    If Me.Controls.Contains(nuovoMenu) Then Me.Controls.Remove(nuovoMenu)
                    nuovoMenu.Dispose()
                End If
                If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                    menuOpzioni.Select()
                    menuOpzioni.Focus()
                    RiaggancioTouchSuLista(menuOpzioni)
                End If
            Catch exRipristino As Exception
                scriviLog.ScriviLog($"chiudiOpzioni (ripristino dopo errore): {exRipristino.ToString()}")
            End Try
            Return
        End Try

        ' Fase 2: la nuova lista e' pronta e a fuoco. Da qui in poi si passa la mano e si chiude
        ' il menu opzioni: un errore in questa parte non deve piu' annullare il passaggio gia'
        ' fatto, si registra soltanto.
        Try
            menu = nuovoMenu
            menuOpzioniAperto = False
            regolaLayout()

            If indiceSelezionatoOpzioni >= 0 Then
                ultimoElementoOpzioni = indiceSelezionatoOpzioni
            End If

            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                Me.Controls.Remove(menuOpzioni)
                menuOpzioni.Dispose()
                menuOpzioni = Nothing
            End If

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"chiudiOpzioni - ObjectDisposedException: {ex.ToString()}")
        Catch ex As InvalidOperationException
            scriviLog.ScriviLog($"chiudiOpzioni - InvalidOperationException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"chiudiOpzioni - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub InitializeComponent()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            Me.SuspendLayout()
            Me.Name = "WGMenuListaInt"
            Me.ResumeLayout(False)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"InitializeComponent - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"InitializeComponent - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub WGMenuListaInt_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() WGMenuListaInt_Load(sender, e), "WGMenuListaInt_Load")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            Dim haElementi As Boolean = False
            For Each item As Object In menu.Items
                If TypeOf item Is ElementoListaWrapper Then
                    haElementi = True
                    Exit For
                End If
            Next

            regolaLayout()
            Me.Focus()
            SetFocus(menu.Handle)

            Dim presentiId As Boolean = False
            For Each itm In menu.Items
                Dim w = TryCast(itm, ElementoListaWrapper)
                If w IsNot Nothing AndAlso Not String.IsNullOrEmpty(w.Id) Then
                    presentiId = True
                    Exit For
                End If
            Next
            If Not presentiId Then
                _invioNonConsentito = True
                _invioBloccatoAutomaticamente = True
                multiselezioneConsentita = False
                menu.SelezioneConsentita = True
            ElseIf _invioBloccatoAutomaticamente Then
                _invioNonConsentito = False
                _invioBloccatoAutomaticamente = False
            End If

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"WGMenuLista_Load - ObjectDisposedException: {ex.ToString()}")
            Return
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"WGMenuLista_Load - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub selezionaVociMenu()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            menu.BeginUpdate()
            For i As Integer = 0 To menu.Items.Count - 1
                menu.SetSelected(i, True)
            Next
            menu.EndUpdate()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"selezionaVociMenu - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"selezionaVociMenu - Errore generico: {ex.ToString()}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Public Sub aggiungiElemento(elemento As String, Optional id As String = "", Optional selezionato As Boolean = False)
        If Me.IsDisposed OrElse Me.Disposing Then
            Return
        End If
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() aggiungiElemento(elemento, id, selezionato), "aggiungiElemento")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() aggiungiElemento(elemento, id, selezionato), "aggiungiElemento")
            Return
        End If
        If IsControlDisposed(menu) Then
            Return
        End If

        Try
            Try
                If selezionato Then
                    _bloccoDisattivazioneSelezione = True
                    If menu.SelectionMode <> SelectionMode.MultiSimple AndAlso menu.SelectionMode <> SelectionMode.MultiExtended Then
                        menu.SelectionMode = SelectionMode.MultiSimple
                    End If
                    selezioneAttivata = True
                End If

                If Not multiselezioneConsentita Then
                    If selezioneMenu Then
                        Try
                            If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                                menuOpzioni.Items.Remove("Seleziona tutto")
                            End If
                        Catch exOpz As Exception
                            scriviLog.ScriviLog($"aggiungiElemento (menuOpzioni): {exOpz.Message}")
                        End Try
                        rimuoviEvento("Seleziona tutto")
                    End If
                End If
            Catch exPre As Exception
                scriviLog.ScriviLog($"aggiungiElemento (pre-add): {exPre.ToString()}")
            End Try

            menu.BeginUpdate()
            Try
                menu.Items.Add(New ElementoListaWrapper(elemento, id), selezionato)
            Finally
                menu.EndUpdate()
            End Try

            If _mantieniSelezioneTraPagine AndAlso Not String.IsNullOrEmpty(id) Then
                If selezionato Then
                    If Not _insiemeSelezioniMemorizzate.Contains(id) Then
                        _insiemeSelezioniMemorizzate.Add(id)
                        _ordineSelezioniMemorizzate.Add(id)
                    End If
                ElseIf _insiemeSelezioniMemorizzate.Contains(id) Then
                    menu.SetSelected(menu.Items.Count - 1, True)
                End If
            End If

            If _invioBloccatoAutomaticamente AndAlso Not String.IsNullOrEmpty(id) Then
                _invioNonConsentito = False
                _invioBloccatoAutomaticamente = False
            End If

            If menu.Items.Count = 0 Then
            End If

            Try
                If menu.Items.Count Mod 500 = 0 AndAlso menu.Items.Count > 0 Then
                    If Me.Enabled AndAlso Me.Focused AndAlso suonoAttesa IsNot Nothing Then
                        suonoAttesa.Attesa()
                    End If
                End If
            Catch exSuono As Exception
                scriviLog.ScriviLog($"aggiungiElemento (suono): {exSuono.Message}")
            End Try
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"aggiungiElemento - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"aggiungiElemento - NullReferenceException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"aggiungiElemento - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub CaricaElementiMassivi(dati As Dictionary(Of String, String))
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() CaricaElementiMassivi(dati), "CaricaElementiMassivi")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() CaricaElementiMassivi(dati), "CaricaElementiMassivi")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If multiselezioneConsentita Then

            Else
                If selezioneMenu Then
                    Try
                        If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                            menuOpzioni.Items.Remove("Seleziona tutto")
                        End If
                    Catch ex As Exception
                        scriviLog.ScriviLog($"Warning: Could not remove 'Seleziona tutto' from menuOpzioni.{ex.Message}")
                    End Try
                    rimuoviEvento("Seleziona tutto")
                End If
            End If

            menu.BeginUpdate()
            For Each kvp In dati
                menu.Items.Add(New ElementoListaWrapper(kvp.Key, kvp.Value))
            Next
            menu.EndUpdate()

            ApplicaMemoriaSelezione()

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"CaricaElementiMassivi - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"CaricaElementiMassivi - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub CaricaElementiMassiviNoID(lista As List(Of String))
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() CaricaElementiMassiviNoID(lista), "CaricaElementiMassiviNoID")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() CaricaElementiMassiviNoID(lista), "CaricaElementiMassiviNoID")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If multiselezioneConsentita Then

            Else
                If selezioneMenu Then
                    Try
                        If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                            menuOpzioni.Items.Remove("Seleziona tutto")
                        End If
                    Catch ex As Exception
                        scriviLog.ScriviLog($"Warning: Could not remove 'Seleziona tutto' from menuOpzioni.{ex.Message}")
                    End Try
                    rimuoviEvento("Seleziona tutto")
                End If
            End If

            menu.BeginUpdate()
            For Each s In lista
                menu.Items.Add(New ElementoListaWrapper(s, ""))
            Next
            menu.EndUpdate()

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"CaricaElementiMassiviNoID - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"CaricaElementiMassiviNoID - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub CaricaListaDinamica(elementi As List(Of ElementoLista))
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() CaricaListaDinamica(elementi), "CaricaListaDinamica")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() CaricaListaDinamica(elementi), "CaricaListaDinamica")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If multiselezioneConsentita Then

            Else
                If selezioneMenu Then
                    Try
                        If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                            menuOpzioni.Items.Remove("Seleziona tutto")
                        End If
                    Catch ex As Exception
                        scriviLog.ScriviLog($"Warning: Could not remove 'Seleziona tutto' from menuOpzioni.{ex.Message}")
                    End Try
                    rimuoviEvento("Seleziona tutto")
                End If
            End If

            menu.BeginUpdate()
            Dim presentiIdCaricati As Boolean = False
            For Each el In elementi
                menu.Items.Add(New ElementoListaWrapper(el.Testo, el.Id))
                If Not String.IsNullOrEmpty(el.Id) Then presentiIdCaricati = True
            Next
            menu.EndUpdate()

            If _invioBloccatoAutomaticamente AndAlso presentiIdCaricati Then
                _invioNonConsentito = False
                _invioBloccatoAutomaticamente = False
            End If

            ApplicaMemoriaSelezione()

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"CaricaListaDinamica - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"CaricaListaDinamica - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub AggiungiElementi(elementi As List(Of ElementoLista))
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() AggiungiElementi(elementi), "AggiungiElementi")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() AggiungiElementi(elementi), "AggiungiElementi")
            Return
        End If
        If IsControlDisposed(menu) Then Return
        If elementi Is Nothing OrElse elementi.Count = 0 Then Return

        Try
            If Not multiselezioneConsentita AndAlso selezioneMenu Then
                Try
                    If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                        menuOpzioni.Items.Remove("Seleziona tutto")
                    End If
                Catch exOpz As Exception
                    scriviLog.ScriviLog($"AggiungiElementi (menuOpzioni): {exOpz.Message}")
                End Try
                rimuoviEvento("Seleziona tutto")
            End If

            Dim presentiIdCaricati As Boolean = False
            menu.BeginUpdate()
            Try
                For Each el As ElementoLista In elementi
                    If el Is Nothing Then Continue For
                    menu.Items.Add(New ElementoListaWrapper(el.Testo, el.Id))
                    If Not String.IsNullOrEmpty(el.Id) Then presentiIdCaricati = True
                Next
            Finally
                menu.EndUpdate()
            End Try

            If _invioBloccatoAutomaticamente AndAlso presentiIdCaricati Then
                _invioNonConsentito = False
                _invioBloccatoAutomaticamente = False
            End If

            ApplicaMemoriaSelezione()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"AggiungiElementi - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"AggiungiElementi - NullReferenceException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"AggiungiElementi - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub pulisci(Optional isSelectionToggle As Boolean = False)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, Sub() pulisci(isSelectionToggle), "pulisci")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(Sub() pulisci(isSelectionToggle), "pulisci")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            AggiornaMemoriaSelezione()

            Dim conservaSelezione As Boolean = _mantieniSelezioneTraPagine AndAlso _multiselezioneConsentita

            _suonoAttesaIniziato = False
            _idDaRipristinareDopoToggle = Nothing
            _richiestaSelezioneUltimo = False
            If Not conservaSelezione Then _bloccoDisattivazioneSelezione = False

            menuOpzioniAperto = False
            If isSelectionToggle AndAlso menu.SelectedIndex > -1 Then
                Dim wrapper = TryCast(menu.SelectedItem, ElementoListaWrapper)
                If wrapper IsNot Nothing Then
                    _idDaRipristinareDopoToggle = wrapper.Id
                End If
            End If

            If Not conservaSelezione Then
                menu.SelectionMode = SelectionMode.One
                selezioneAttivata = False
            End If
            menu.Items.Clear()
            elementiMenuPrincipale.Clear()
            controlloEseguito = False
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"pulisci - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"pulisci - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub svuotaLista()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf svuotaLista, "svuotaLista")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            menu.Items.Clear()
            menu.SelectionMode = SelectionMode.One
            selezioneAttivata = False
            menuOpzioniAperto = False
            menu.Visible = True
        Catch ex As Exception
            scriviLog.ScriviLog($"svuotaLista - Errore: {ex.ToString()}")
        End Try
    End Sub

    Public Function ottieniElementi() As List(Of String)
        If Me.IsDisposed OrElse Me.Disposing Then Return New List(Of String)
        If Me.InvokeRequired Then
            Return EseguiConTimeout(Of List(Of String))(Function() ottieniElementi(), New List(Of String), "ottieniElementi")
        Else
            Try
                Dim listaNascosta As Boolean = menuOpzioniAperto OrElse IsControlDisposed(menu)
                Dim modalita As SelectionMode = SelectionMode.One
                If listaNascosta Then
                    modalita = _salvataggioSelectionMode
                Else
                    modalita = menu.SelectionMode
                End If
                Dim selezioneMultipla As Boolean = (modalita = SelectionMode.MultiSimple OrElse modalita = SelectionMode.MultiExtended)

                If _mantieniSelezioneTraPagine AndAlso selezioneMultipla Then
                    If Not listaNascosta Then AggiornaMemoriaSelezione()
                    If _ordineSelezioniMemorizzate.Count > 0 Then
                        Return New List(Of String)(_ordineSelezioniMemorizzate)
                    End If
                End If

                Dim listaIds As New List(Of String)

                If listaNascosta Then
                    If idSelezionatiPrimaDiOpzioni IsNot Nothing Then
                        listaIds.AddRange(idSelezionatiPrimaDiOpzioni)
                    End If
                    If listaIds.Count = 0 Then
                        Dim wrapperSalvato As ElementoListaWrapper = WrapperElementoSalvato(_salvataggioIndice)
                        If wrapperSalvato IsNot Nothing Then listaIds.Add(wrapperSalvato.Id)
                    End If
                    Return listaIds
                End If

                For i As Integer = 0 To menu.Items.Count - 1
                    If menu.GetSelected(i) Then
                        Dim wrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                        If wrapper IsNot Nothing Then
                            listaIds.Add(wrapper.Id)
                        End If
                    End If
                Next

                If listaIds.Count = 0 AndAlso menu.SelectedIndex >= 0 AndAlso menu.SelectedIndex < menu.Items.Count Then
                    Dim wrapperCorrente = TryCast(menu.Items(menu.SelectedIndex), ElementoListaWrapper)
                    If wrapperCorrente IsNot Nothing Then
                        listaIds.Add(wrapperCorrente.Id)
                    End If
                End If

                Return listaIds
            Catch ex As ObjectDisposedException
                scriviLog.ScriviLog($"ottieniElementi - ObjectDisposedException: {ex.ToString()}")
                Return New List(Of String)()
            Catch ex As NullReferenceException
                scriviLog.ScriviLog(ex.Message & ex.StackTrace)
                Return New List(Of String)()
            Catch ex As Exception
                scriviLog.ScriviLog($"ottieniElementi - Errore generico: {ex.ToString()}")
                Return New List(Of String)()
            End Try
        End If
    End Function

    Private Function WrapperElementoSalvato(indice As Integer) As ElementoListaWrapper
        Try
            If _salvataggioItems Is Nothing Then Return Nothing
            If indice < 0 OrElse indice >= _salvataggioItems.Count Then Return Nothing
            Return TryCast(_salvataggioItems(indice), ElementoListaWrapper)
        Catch ex As Exception
            scriviLog.ScriviLog($"WrapperElementoSalvato - Errore generico: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Public Sub opzioniPulisci()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf opzioniPulisci, "opzioniPulisci")
            Return
        End If

        Try
            eventiOpzioni.Clear()
            If Not IsControlDisposed(menuOpzioni) Then
                menuOpzioni.Items.Clear()
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"opzioniPulisci - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"opzioniPulisci - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub selezionaElemento(indice As Integer)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() selezionaElemento(indice), "selezionaElemento")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If indice >= 0 AndAlso indice < menu.Items.Count Then
                menu.SelectedIndex = indice
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"selezionaElemento - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"selezionaElemento - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub posiziona(id As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() posiziona(id), "posiziona")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If String.IsNullOrEmpty(id) Then Return
            For i As Integer = 0 To menu.Items.Count - 1
                Dim wrapper As ElementoListaWrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                If wrapper IsNot Nothing AndAlso wrapper.Id = id Then
                    menu.SelectedIndex = i
                    menu.LeggiElementoCorrente()
                    Return
                End If
            Next
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"posiziona - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"posiziona - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Function ottieniIndice() As Integer
        If Me.IsDisposed OrElse Me.Disposing Then Return -1
        If IsControlDisposed(menu) Then Return -1
        Try
            Return menu.SelectedIndex
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"ottieniIndice - ObjectDisposedException: {ex.ToString()}")
            Return -1
        Catch ex As NullReferenceException
            Return -1
        Catch ex As Exception
            scriviLog.ScriviLog($"ottieniIndice - Errore generico: {ex.ToString()}")
        End Try
        Return -1
    End Function

    Public Function ottieniElemento() As String
        If Me.IsDisposed OrElse Me.Disposing Then Return String.Empty
        If IsControlDisposed(menu) Then Return String.Empty

        Try
            Dim elementi As List(Of String) = ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return String.Empty
            Return elementi(0)
        Catch ex As Exception
            scriviLog.ScriviLog($"ottieniElemento - Errore generico: {ex.ToString()}")
        End Try
        Return String.Empty
    End Function

    Public Function impostaTitolo(titolo As String) As Boolean
        If Me.IsDisposed OrElse Me.Disposing Then Return False
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            Return EseguiConTimeoutSu(Of Boolean)(ancoraMarshal, Function() impostaTitolo(titolo), False, "impostaTitolo")
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            Return EseguiConTimeout(Of Boolean)(Function() impostaTitolo(titolo), False, "impostaTitolo")
        End If
        If IsControlDisposed(menu) Then Return False

        Try
            menu.Titolo = titolo
            Return True
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"impostaTitolo - ObjectDisposedException: {ex.ToString()}")
            Return False
        Catch ex As NullReferenceException
            Return False
        Catch ex As Exception
            scriviLog.ScriviLog($"impostaTitolo - Errore generico: {ex.ToString()}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore: " & ex.Message)
            messaggio.ShowDialog()
        End Try
        Return False
    End Function

    Public Sub abbandona()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf abbandona, "abbandona")
            Return
        End If

        Try
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
            RemoveHandler Me.Resize, AddressOf Me_Resize

            If _tastiera IsNot Nothing Then
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto

            End If

            If _touk IsNot Nothing Then
                RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                _touk = Nothing
            End If

            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                menuOpzioni.Dispose()
                menuOpzioni = Nothing
            End If
            If menu IsNot Nothing AndAlso Not menu.IsDisposed Then
                menu.Dispose()
                menu = Nothing
            End If

            Me.Dispose()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"abbandona - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"abbandona - Errore generico: {ex.ToString()}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Public Sub selezionaTuttiGliElementi()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf selezionaTuttiGliElementi, "selezionaTuttiGliElementi")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If Not multiselezioneConsentita Then
                Return
            End If

            menu.BeginUpdate()
            If menu.SelectionMode <> SelectionMode.MultiSimple AndAlso menu.SelectionMode <> SelectionMode.MultiExtended Then
                menu.SelectionMode = SelectionMode.MultiSimple
            End If
            For i As Integer = 0 To menu.Items.Count - 1
                menu.SetSelected(i, True)
            Next
            menu.EndUpdate()
            selezioneAttivata = True
            AggiornaMemoriaSelezione()
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"selezionaTuttiGliElementi - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"selezionaTuttiGliElementi - Errore generico: {ex.ToString()}")
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un'errore: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Public Sub selezionaElementiDaIndice(indiceInizio As Integer)
        If Me.IsDisposed OrElse Me.Disposing Then Return

        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() selezionaElementiDaIndice(indiceInizio), "selezionaElementiDaIndice")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If Not multiselezioneConsentita Then Return

            If indiceInizio < 0 OrElse indiceInizio >= menu.Items.Count Then
                Throw New ArgumentOutOfRangeException("indiceInizio", "L'indice deve essere compreso tra 0 e il numero di elementi nel menù.")
            End If

            menu.BeginUpdate()
            Try
                menu.SelectionMode = SelectionMode.MultiSimple
                For i As Integer = indiceInizio To menu.Items.Count - 1
                    menu.SetSelected(i, True)
                Next
            Finally
                menu.EndUpdate()
            End Try
            menu.SelectedIndex = indiceInizio
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"selezionaElementiDaIndice - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"selezionaElementiDaIndice - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub rimuoviEvento(chiave As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            eventiOpzioni.RemoveAll(Function(v) v.Nome = chiave)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"rimuoviEvento - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"rimuoviEvento - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Function ottieniElementoIndice(i As Integer) As String
        If Me.IsDisposed OrElse Me.Disposing Then Return String.Empty
        If IsControlDisposed(menu) Then Return String.Empty

        Try
            If i >= 0 AndAlso i < menu.Items.Count Then
                Dim wrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                If wrapper IsNot Nothing Then
                    Return wrapper.Id
                End If
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"ottieniElementoIndice - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            Return String.Empty
        Catch ex As Exception
            scriviLog.ScriviLog($"ottieniElementoIndice - Errore generico: {ex.ToString()}")
        End Try
        Return String.Empty
    End Function

    Public Sub selezionaElementoIndice(ByVal indice As Integer)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() selezionaElementoIndice(indice), "selezionaElementoIndice")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If indice >= 0 AndAlso indice < menu.Items.Count Then
                If multiselezioneConsentita Then
                    menu.SetSelected(indice, True)
                Else
                    menu.SelectedIndex = indice
                End If
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"selezionaElementoIndice - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"selezionaElementoIndice - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub gestisciEsc()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf gestisciEsc, "gestisciEsc")
            Return
        End If

        Try
            RaiseEvent escPremuto(Me, Nothing)
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"gestisciEsc - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"gestisciEsc - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub gestisciEnter()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim contenitoreMarshalEnter As WGContenitore = WGContenitore.Istanza
        If contenitoreMarshalEnter IsNot Nothing AndAlso contenitoreMarshalEnter.InvokeRequired Then
            EseguiConTimeoutSu(contenitoreMarshalEnter, AddressOf gestisciEnter, "gestisciEnter")
            Return
        End If

        Try
            Dim ids As New List(Of String)()
            Dim wrapper As ElementoListaWrapper

            If menuOpzioniAperto AndAlso Not IsControlDisposed(menuOpzioni) Then
                If menuOpzioni.SelectedItem IsNot Nothing Then
                    Dim opzioneId As String = menuOpzioni.SelectedItem.ToString()
                    chiudiOpzioni()
                    Dim v = eventiOpzioni.FirstOrDefault(Function(x) x.Nome = opzioneId)
                    If v IsNot Nothing Then
                        v.Evento?.Invoke(Me, EventArgs.Empty)
                    End If
                End If
                Return
            End If

            If invioNonConsentito Then
                Return
            End If

            If menu.SelectionMode = SelectionMode.One Then
                If menu.SelectedItem Is Nothing AndAlso menu.Items.Count > 0 Then
                    menu.SelectedIndex = 0
                End If
                If menu.SelectedItem IsNot Nothing Then
                    wrapper = TryCast(menu.SelectedItem, ElementoListaWrapper)
                    If wrapper IsNot Nothing Then
                        RaiseEvent ElementoSelezionato(wrapper.Id)
                    Else
                        RaiseEvent ElementoSelezionato(menu.SelectedItem.ToString())
                    End If
                Else
                End If
            Else
                If _mantieniSelezioneTraPagine Then
                    AggiornaMemoriaSelezione()
                    ids.AddRange(_ordineSelezioniMemorizzate)
                Else
                    For i As Integer = 0 To menu.Items.Count - 1
                        If menu.GetSelected(i) Then
                            wrapper = TryCast(menu.Items(i), ElementoListaWrapper)
                            If wrapper IsNot Nothing Then
                                ids.Add(wrapper.Id)
                            End If
                        End If
                    Next
                End If
                If ids.Count > 0 Then
                    RaiseEvent ElementiSelezionati(ids)
                End If
            End If
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"gestisciEnter - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"gestisciEnter - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Try
                Dim cp As CreateParams = MyBase.CreateParams
                If Me.Parent Is Nothing Then
                    cp.Style = WS_POPUP Or WS_VISIBLE
                End If
                Return cp
            Catch ex As Exception
                scriviLog.ScriviLog($"CreateParams: {ex.ToString()}")
                Return MyBase.CreateParams
            End Try
        End Get
    End Property

    Public Sub regolaLayout()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf regolaLayout, "regolaLayout")
            Return
        End If

        Try
            If Me.Parent Is Nothing Then
                Dim workingArea As Rectangle = Screen.PrimaryScreen.WorkingArea
                SetWindowPos(Me.Handle, IntPtr.Zero, 0, 0, workingArea.Width, workingArea.Height, SWP_SHOWWINDOW)
            Else
                If Me.Dock <> DockStyle.Fill Then
                    Me.Dock = DockStyle.Fill
                End If
            End If

            If Not IsControlDisposed(menu) Then
                menu.Left = MARGIN_LEFT
                menu.Top = MARGIN_TOP
                menu.Width = Me.ClientSize.Width - (MARGIN_LEFT + MARGIN_RIGHT)
                menu.Height = Me.ClientSize.Height - (MARGIN_TOP + MARGIN_BOTTOM)
            End If

            If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                menuOpzioni.Left = MARGIN_LEFT
                menuOpzioni.Top = MARGIN_TOP
                menuOpzioni.Width = Me.ClientSize.Width - (MARGIN_LEFT + MARGIN_RIGHT)
                menuOpzioni.Height = Me.ClientSize.Height - (MARGIN_TOP + MARGIN_BOTTOM)
            End If

        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"regolaLayout - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"NullReferenceException: {ex.ToString()}")
            Return
        Catch ex As Exception
            scriviLog.ScriviLog($"regolaLayout - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Private Sub Me_Resize(sender As Object, e As EventArgs)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Try
            regolaLayout()
        Catch ex As Exception
            scriviLog.ScriviLog($"Me_Resize - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub BeginUpdate()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, AddressOf BeginUpdate, "BeginUpdate")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(AddressOf BeginUpdate, "BeginUpdate")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            menu.BeginUpdate()
        Catch ex As Exception
            scriviLog.ScriviLog($"BeginUpdate - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub EndUpdate()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim ancoraMarshal As WGContenitore = WGContenitore.Istanza
        If ancoraMarshal IsNot Nothing AndAlso ancoraMarshal.InvokeRequired Then
            EseguiConTimeoutSu(ancoraMarshal, AddressOf EndUpdate, "EndUpdate")
            Return
        ElseIf ancoraMarshal Is Nothing AndAlso Me.InvokeRequired Then
            EseguiConTimeout(AddressOf EndUpdate, "EndUpdate")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            menu.EndUpdate()
        Catch ex As Exception
            scriviLog.ScriviLog($"EndUpdate - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub DisattivaTimerChiusura()

    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                Try
                    If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.RimuoviControllo(Me)
                Catch exRimuovi As Exception
                    scriviLog.ScriviLog($"Dispose (RimuoviWGForm): {exRimuovi.ToString()}")
                End Try
                _monitorVisibilitaInEsecuzione = False

                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto

                    _tastiera = Nothing
                End If

                If _touk IsNot Nothing Then
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    _touk = Nothing
                End If

                If menu IsNot Nothing AndAlso Not menu.IsDisposed Then
                    menu.Dispose()
                    menu = Nothing
                End If

                If menuOpzioni IsNot Nothing AndAlso Not menuOpzioni.IsDisposed Then
                    menuOpzioni.Dispose()
                    menuOpzioni = Nothing
                End If
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"Dispose - Errore generico: {ex.ToString()}")
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Public Sub RimuoviElemento(id As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() RimuoviElemento(id), "RimuoviElemento")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            Dim itemToRemove As Object = Nothing
            For Each itm As Object In menu.Items
                Dim wrapper = TryCast(itm, ElementoListaWrapper)
                If wrapper IsNot Nothing AndAlso wrapper.Id = id Then
                    itemToRemove = itm
                    Exit For
                End If
            Next

            If itemToRemove IsNot Nothing Then
                menu.Items.Remove(itemToRemove)
            End If

        Catch ex As Exception
            scriviLog.ScriviLog($"RimuoviElemento - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub SelezionaUltimoElemento()
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(AddressOf SelezionaUltimoElemento, "SelezionaUltimoElemento")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If menu.Items.Count > 0 Then
                menu.SelectedIndex = menu.Items.Count - 1
            End If
        Catch ex As Exception
            scriviLog.ScriviLog($"SelezionaUltimoElemento - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Function VoceEsiste(id As String) As Boolean
        If Me.IsDisposed OrElse Me.Disposing Then Return False
        If Me.InvokeRequired Then
            Return EseguiConTimeout(Of Boolean)(Function() VoceEsiste(id), False, "VoceEsiste")
        End If
        If IsControlDisposed(menu) Then Return False

        Try
            For Each itm As Object In menu.Items
                Dim wrapper = TryCast(itm, ElementoListaWrapper)
                If wrapper IsNot Nothing AndAlso wrapper.Id = id Then
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            scriviLog.ScriviLog($"VoceEsiste - Errore generico: {ex.ToString()}")
            Return False
        End Try
    End Function

    Public Sub AggiungiElementoDopo(idRiferimento As String, testoNuovo As String, Optional idNuovo As String = "")
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() AggiungiElementoDopo(idRiferimento, testoNuovo, idNuovo), "AggiungiElementoDopo")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If multiselezioneConsentita Then

            Else
                If selezioneMenu Then
                    Try
                        If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                            menuOpzioni.Items.Remove("Seleziona tutto")
                        End If
                    Catch ex As Exception
                        scriviLog.ScriviLog($"Warning: Could not remove 'Seleziona tutto' from menuOpzioni.{ex.Message}")
                    End Try
                    rimuoviEvento("Seleziona tutto")
                End If
            End If

            Dim objRiferimento As Object = Nothing
            For i As Integer = 0 To menu.Items.Count - 1
                Dim w = TryCast(menu.Items(i), ElementoListaWrapper)
                If w IsNot Nothing AndAlso w.Id = idRiferimento Then
                    objRiferimento = menu.Items(i)
                    Exit For
                End If
            Next

            If objRiferimento IsNot Nothing Then
                menu.Items.InsertAfter(objRiferimento, New ElementoListaWrapper(testoNuovo, idNuovo))
            End If

        Catch ex As Exception
            scriviLog.ScriviLog($"AggiungiElementoDopo - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    Public Sub AggiungiElementoPrima(idRiferimento As String, testoNuovo As String, Optional idNuovo As String = "")
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() AggiungiElementoPrima(idRiferimento, testoNuovo, idNuovo), "AggiungiElementoPrima")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If multiselezioneConsentita Then

            Else
                If selezioneMenu Then
                    Try
                        If Not IsControlDisposed(menuOpzioni) AndAlso menuOpzioni.Items.Contains("Seleziona tutto") Then
                            menuOpzioni.Items.Remove("Seleziona tutto")
                        End If
                    Catch ex As Exception
                        scriviLog.ScriviLog($"Warning: Could not remove 'Seleziona tutto' from menuOpzioni.{ex.Message}")
                    End Try
                    rimuoviEvento("Seleziona tutto")
                End If
            End If

            Dim objRiferimento As Object = Nothing
            For i As Integer = 0 To menu.Items.Count - 1
                Dim w = TryCast(menu.Items(i), ElementoListaWrapper)
                If w IsNot Nothing AndAlso w.Id = idRiferimento Then
                    objRiferimento = menu.Items(i)
                    Exit For
                End If
            Next

            If objRiferimento IsNot Nothing Then
                menu.Items.InsertBefore(objRiferimento, New ElementoListaWrapper(testoNuovo, idNuovo))
            End If

        Catch ex As Exception
            scriviLog.ScriviLog($"AggiungiElementoPrima - Errore generico: {ex.ToString()}")
        End Try
    End Sub

    ''' <summary>
    ''' Sostituisce il testo dell'elemento con l'id indicato mantenendone posizione e stato di
    ''' selezione: si rimuove il wrapper esistente e se ne inserisce uno nuovo esattamente allo
    ''' stesso punto della lista, così l'utente non perde il punto in cui si trova.
    ''' </summary>
    Public Sub AggiornaTestoElemento(id As String, nuovoTesto As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        If Me.InvokeRequired Then
            EseguiConTimeout(Sub() AggiornaTestoElemento(id, nuovoTesto), "AggiornaTestoElemento")
            Return
        End If
        If IsControlDisposed(menu) Then Return

        Try
            If String.IsNullOrEmpty(id) Then Return

            Dim itemDaAggiornare As Object = Nothing
            Dim indice As Integer = -1
            For i As Integer = 0 To menu.Items.Count - 1
                Dim w = TryCast(menu.Items(i), ElementoListaWrapper)
                If w IsNot Nothing AndAlso w.Id = id Then
                    itemDaAggiornare = menu.Items(i)
                    indice = i
                    Exit For
                End If
            Next
            If itemDaAggiornare Is Nothing Then Return

            ' Se il testo non cambia non c'è nulla da fare.
            Dim wrapperAttuale = TryCast(itemDaAggiornare, ElementoListaWrapper)
            If wrapperAttuale IsNot Nothing AndAlso wrapperAttuale.Testo = nuovoTesto Then Return

            Dim eraSelezionato As Boolean = False
            Try
                eraSelezionato = (menu.SelectedIndex = indice)
            Catch
            End Try

            Dim itemSuccessivo As Object = Nothing
            If indice + 1 < menu.Items.Count Then itemSuccessivo = menu.Items(indice + 1)
            Dim itemPrecedente As Object = Nothing
            If indice > 0 Then itemPrecedente = menu.Items(indice - 1)

            menu.BeginUpdate()
            Try
                menu.Items.Remove(itemDaAggiornare)
                Dim nuovoElemento As New ElementoListaWrapper(nuovoTesto, id)
                If itemSuccessivo IsNot Nothing Then
                    menu.Items.InsertBefore(itemSuccessivo, nuovoElemento)
                ElseIf itemPrecedente IsNot Nothing Then
                    menu.Items.InsertAfter(itemPrecedente, nuovoElemento)
                Else
                    menu.Items.Add(nuovoElemento)
                End If

                If eraSelezionato Then
                    Try
                        menu.SelectedIndex = indice
                    Catch
                    End Try
                End If
            Finally
                menu.EndUpdate()
            End Try
        Catch ex As ObjectDisposedException
            scriviLog.ScriviLog($"AggiornaTestoElemento - ObjectDisposedException: {ex.ToString()}")
        Catch ex As NullReferenceException
            scriviLog.ScriviLog($"AggiornaTestoElemento - NullReferenceException: {ex.ToString()}")
        Catch ex As Exception
            scriviLog.ScriviLog($"AggiornaTestoElemento - Errore generico: {ex.ToString()}")
        End Try
    End Sub
End Class

''' <summary>Una scorciatoia con Ctrl sull'elenco: il tasto, se c'è anche Maiusc, e se chi la riceve l'ha gestita.</summary>
Public Class ScorciatoiaCtrlEventArgs
    Inherits EventArgs

    Public ReadOnly Property Codice As Keys
    Public ReadOnly Property Maiusc As Boolean
    Public Property Gestita As Boolean

    Public Sub New(codice As Keys, maiusc As Boolean)
        Me.Codice = codice
        Me.Maiusc = maiusc
    End Sub
End Class

Public Class VoceCambiataEventArgs
    Inherits EventArgs
    Public ReadOnly Property IdVoce As String
    Public Sub New(id As String)
        IdVoce = id
    End Sub
End Class