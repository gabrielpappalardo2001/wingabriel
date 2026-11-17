Imports System.Threading
Imports UIAutomationClient

' Gestione dei moduli applicativi, recupero degli elementi con focus, navigazione nell'albero di automazione e supporto per i
' menu.
Partial Public NotInheritable Class WGLettoreSchermo

    Private ReadOnly _gestoreAppModule As New GestoreAppModule(Me)

    Friend Function OttieniModuloApp(pid As UInteger) As AppModuleLettore
        Try
            Return _gestoreAppModule.OttieniModulo(pid)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.OttieniModuloApp", exAtteso)
            Return AppModuleLettore.Crea("", Me)
        End Try
    End Function

    Friend Function OttieniModuloApp(elemento As IUIAutomationElement) As AppModuleLettore
        Try
            Return _gestoreAppModule.OttieniModulo(elemento)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.OttieniModuloApp", exAtteso)
            Return AppModuleLettore.Crea("", Me)
        End Try
    End Function

    Friend Function OttieniModuloApp(finestra As IntPtr) As AppModuleLettore
        Try
            If finestra = IntPtr.Zero Then Return AppModuleLettore.Crea("", Me)
            Dim pid As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(finestra, pid)
            Return OttieniModuloApp(pid)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.OttieniModuloApp", exAtteso)
            Return AppModuleLettore.Crea("", Me)
        End Try
    End Function

    Friend Function OttieniModuloForegroundApp() As AppModuleLettore
        Try
            Return OttieniModuloApp(NativoLettore.GetForegroundWindow())
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.OttieniModuloForegroundApp", exAtteso)
            Return AppModuleLettore.Crea("", Me)
        End Try
    End Function

    Friend ReadOnly Property DescrittoreApp As DescrittoreElementi
        Get
            Return _descrittore
        End Get
    End Property

    Friend ReadOnly Property WalkerControlloApp As IUIAutomationTreeWalker
        Get
            Return _walkerControllo
        End Get
    End Property

    Friend ReadOnly Property RichiestaCacheApp As IUIAutomationCacheRequest
        Get
            Return _richiestaCache
        End Get
    End Property

    Friend ReadOnly Property AutomazioneApp As IUIAutomation
        Get
            Return _automazione
        End Get
    End Property

    Friend ReadOnly Property OperazioniApp As CodaOperazioni
        Get
            Return _operazioni
        End Get
    End Property

    Friend ReadOnly Property UltimoElementoApp As IUIAutomationElement
        Get
            Return _ultimoElemento
        End Get
    End Property

    Friend Property UltimoInAlberoApp As Integer
        Get
            Return Volatile.Read(_ultimoInAlbero)
        End Get
        Set(value As Integer)
            Volatile.Write(_ultimoInAlbero, value)
        End Set
    End Property

    Friend ReadOnly Property UltimoNomeApp As String
        Get
            Return _ultimoNome
        End Get
    End Property

    Friend ReadOnly Property UltimoFocusTickApp As Integer
        Get
            Return Volatile.Read(_ultimoFocusTick)
        End Get
    End Property

    Friend ReadOnly Property AnnunciaPosizioneElenchiApp As Boolean
        Get
            Return Volatile.Read(_annunciaPosizioneElenchi) = 1
        End Get
    End Property

    Friend Function NomeEseguibileApp(pid As UInteger) As String
        Return NomeEseguibile(pid)
    End Function

    Friend Function ChiaveElementoApp(elemento As IUIAutomationElement) As String
        Return ChiaveElemento(elemento)
    End Function

    Friend Function ContenitoreVociApp(elemento As IUIAutomationElement) As IUIAutomationElement
        Return ContenitoreVoci(elemento)
    End Function

    Friend Sub PosizioneTraFratelliApp(elemento As IUIAutomationElement, ByRef posizione As Integer, ByRef totale As Integer)
        PosizioneTraFratelli(elemento, posizione, totale)
    End Sub

    Friend Function NelControlloCompositoApp(elemento As IUIAutomationElement) As Boolean
        Return NelControlloComposito(elemento)
    End Function

    Friend Function ElementoConFocusApp(seMancaUsaUltimo As Boolean) As IUIAutomationElement
        Return ElementoConFocus(seMancaUsaUltimo)
    End Function

    Friend Function EVoceDiMenuApp(elemento As IUIAutomationElement) As Boolean
        Return EVoceDiMenu(elemento)
    End Function

    Friend Sub ParlaApp(testo As String, interrompi As Boolean)
        Parla(testo, interrompi)
    End Sub

    Friend Sub AccodaApp(evento As EventoLettore)
        Accoda(evento)
    End Sub

    Private NotInheritable Class RichiestaSelezione
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _evento As EventoLettore

        Public Sub New(lettore As WGLettoreSchermo, evento As EventoLettore)
            _lettore = lettore
            _evento = evento
        End Sub

        Public Sub Esegui()
            Try
                _lettore.ElaboraSelezioneElemento(_evento)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaSelezione.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Sub AccodaSelezioneElemento(evento As EventoLettore)
        Try
            Dim richiesta As New RichiestaSelezione(Me, evento)
            EseguiSuOperazioni("selezione", AddressOf richiesta.Esegui, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaSelezioneElemento: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraSelezioneElemento(evento As EventoLettore)
        Try
            If evento Is Nothing OrElse evento.Elemento Is Nothing OrElse _descrittore Is Nothing Then Return
            If FuocoDaRiquadroGecko() Then Return
            If SelezioneSuperata(evento) Then Return
            ' Una riga di una lista classica (il desktop, Esplora risorse) la dice solo la lettura veloce: l'evento di selezione è lo stesso fatto visto da UI Automation.
            If ProvaRigaNativaVeloce(IntPtr.Zero, ListaDaElemento(evento.Elemento)) Then Return
            If OttieniModuloApp(evento.Elemento).EventoSoppresso(evento.Elemento) Then Return
            Dim elemento As IUIAutomationElement = CompletaCache(evento.Elemento)
            Dim frase As String = OttieniModuloApp(elemento).AllSelezione(elemento)
            If frase = "" Then
                AnnunciaVoceAlbero(elemento, False, evento)
                Return
            End If
            If SelezioneSuperata(evento) Then Return
            Dim nome As String = _descrittore.Nome(elemento)
            Dim variabile As String = _descrittore.ParteVariabile(elemento)
            If StessaVoce(elemento, _ultimoElemento) AndAlso nome = _ultimoNome AndAlso variabile = _ultimaParteVariabile Then Return
            MemorizzaElemento(elemento, nome, variabile)
            _revisione = elemento
            _ultimoFocusTick = Environment.TickCount
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraSelezioneElemento: " & ex.Message)
        End Try
    End Sub

    Private _ultimoCodiceTasto As Integer

    Private _ultimoTastoConAlt As Integer
    Private _ultimoCodiceTastoTick As Integer

    Friend Sub SegnaCodiceTasto(codice As Integer)
        Try
            Volatile.Write(_ultimoCodiceTasto, codice)
            Volatile.Write(_ultimoCodiceTastoTick, Environment.TickCount)
            Volatile.Write(_ultimoTastoConAlt, If((CInt(NativoLettore.GetAsyncKeyState(&H12)) And &H8000) <> 0, 1, 0))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SegnaCodiceTasto: " & ex.Message)
        End Try
    End Sub

    Friend Function TastoCambioCartellaApp(millisecondi As Integer, ByRef tickTasto As Integer) As Boolean
        tickTasto = 0
        Try
            tickTasto = Volatile.Read(_ultimoCodiceTastoTick)
            If MillisecondiDa(tickTasto) > CLng(millisecondi) Then Return False
            Dim codice As Integer = Volatile.Read(_ultimoCodiceTasto)
            Select Case codice
                Case &HD, &H8, &HA6, &HA7
                    Return True
                Case &H25, &H26, &H27
                    Return Volatile.Read(_ultimoTastoConAlt) = 1
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoCambioCartellaApp: " & ex.Message)
            Return False
        End Try
    End Function

    Friend ReadOnly Property TickUltimoTastoApp As Integer
        Get
            Return Volatile.Read(_ultimoCodiceTastoTick)
        End Get
    End Property

    Friend Function TastoCifraRecenteApp(millisecondi As Integer) As Boolean
        Try
            If Not TastoRecente(millisecondi) Then Return False
            Dim codice As Integer = Volatile.Read(_ultimoCodiceTasto)
            Return (codice >= &H30 AndAlso codice <= &H39) OrElse (codice >= &H60 AndAlso codice <= &H69)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TastoCifraRecenteApp: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Function StessoElementoApp(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Return StessoElemento(primo, secondo)
    End Function

    Friend Function FinestraContenitoreApp(finestra As IntPtr) As Boolean
        Try
            Return ClasseFinestra(finestra) = "ApplicationFrameWindow"
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FinestraContenitoreApp: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Function BarraStatoLettore() As String
        Try
            Dim testo As String = TestoBarraStato()
            If testo.StartsWith("Barra di stato", StringComparison.CurrentCultureIgnoreCase) Then Return ""
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.BarraStatoLettore: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Sub ScriviLogApp(messaggio As String)
        Try
            log.ScriviLog(messaggio)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ScriviLogApp", exAtteso)
        End Try
    End Sub

End Class