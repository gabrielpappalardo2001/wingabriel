Imports System.Threading

' Frasi che il programma ospite chiede di dire (avvio, menu del lettore).
Partial Public NotInheritable Class WGLettoreSchermo

    Private ReadOnly _bloccoPrefisso As New Object()

    Friend Function NomeProgrammaPrimoPiano() As String
        Return NomeProgrammaFinestra(NativoLettore.GetForegroundWindow())
    End Function

    Friend Sub AccodaComandoComponente(identificativo As String)
        Try
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Comando
            evento.Comando = ComandoLettore.ComponenteEsterno
            evento.Testo = identificativo
            Accoda(evento)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AccodaComandoComponente", ex)
        End Try
    End Sub

    Private Sub EseguiComandoComponente(identificativo As String)
        Try
            Volatile.Write(_origineUtente, 1)
            ComponentiAggiuntivi.EseguiComando(identificativo)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.EseguiComandoComponente", ex)
        Finally
            Volatile.Write(_origineUtente, 0)
        End Try
    End Sub

    ' I componenti aggiuntivi sono cambiati (disattivato, riattivato, disinstallato): i moduli già creati si rifanno al prossimo uso.
    Public Sub AggiornaComponenti()
        Try
            _gestoreAppModule.Svuota()
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AggiornaComponenti", ex)
        End Try
    End Sub

    ' Avvio del lettore: finché la frase di avvio non è stata detta, tutto il resto aspetta (e poi si dice, in ordine).
    Private _avvioInAttesa As Integer
    Private _avvioInizio As Integer
    Private ReadOnly _tenutiPerAvvio As New System.Collections.Generic.List(Of String)()

    Public Sub PreparaAvvio()
        Try
            SyncLock _bloccoPrefisso
                _tenutiPerAvvio.Clear()
                _avvioInizio = Environment.TickCount
                _avvioInAttesa = 1
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.PreparaAvvio", ex)
        End Try
    End Sub

    ' Dice la frase di avvio per prima, poi le frasi che nel frattempo erano state tenute da parte.
    Public Sub AnnunciaAvvio(testo As String)
        Try
            Dim tenuti As String()
            SyncLock _bloccoPrefisso
                _avvioInAttesa = 0
                tenuti = _tenutiPerAvvio.ToArray()
                _tenutiPerAvvio.Clear()
            End SyncLock
            If Not String.IsNullOrWhiteSpace(testo) Then Parla(testo, False)
            For Each frase As String In tenuti
                Parla(frase, False)
            Next
            ' Detta la frase di avvio, si legge la finestra che in quel momento è in primo piano: per il lettore sarebbe una finestra "già vista".
            If Not String.IsNullOrWhiteSpace(testo) Then ThreadPool.QueueUserWorkItem(AddressOf LeggiPrimoPianoDopoAvvio)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AnnunciaAvvio", ex)
        End Try
    End Sub

    ' Il programma ospite ha chiuso una sua schermata o un suo menu senza scegliere niente: si dice dove si è tornati.
    Public Sub RileggiPrimoPiano()
        Try
            ThreadPool.QueueUserWorkItem(AddressOf RileggiPrimoPianoLavoro)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RileggiPrimoPiano", ex)
        End Try
    End Sub

    Private Sub RileggiPrimoPianoLavoro(stato As Object)
        Try
            Thread.Sleep(250)
            If Volatile.Read(_inEsecuzione) = 0 Then Return
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return
            _ultimoElemento = Nothing
            _ultimoNome = ""
            _ultimaParteVariabile = ""
            AccodaPrimoPiano(finestra)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RileggiPrimoPianoLavoro", ex)
        End Try
    End Sub

    Private Sub LeggiPrimoPianoDopoAvvio(stato As Object)
        Try
            Thread.Sleep(300)
            Dim inizio As Integer = Environment.TickCount
            Dim fermoDa As Integer = 0
            While Environment.TickCount - inizio < 6000
                Dim coda As CodaVoce = _codaVoce
                If coda Is Nothing Then Exit While
                If coda.StaParlando OrElse coda.HaFrasiInAttesa Then
                    fermoDa = 0
                ElseIf fermoDa = 0 Then
                    fermoDa = Environment.TickCount
                ElseIf Environment.TickCount - fermoDa >= 200 Then
                    Exit While
                End If
                Thread.Sleep(50)
            End While
            If Volatile.Read(_inEsecuzione) = 0 Then Return
            Dim finestra As IntPtr = NativoLettore.GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return
            ' Se gli eventi dell'avvio hanno già fatto dire un elemento (e si sono sentiti, in ordine, dopo la frase di avvio) la finestra è già stata letta:
            ' la lettura è una sola. Solo se nessun elemento è stato detto, la finestra in primo piano si legge da capo.
            If _ultimoElemento IsNot Nothing OrElse _listaNativaCorrente <> IntPtr.Zero Then Return
            AccodaPrimoPiano(finestra)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.LeggiPrimoPianoDopoAvvio", ex)
        End Try
    End Sub

    Private Function TieniPerAvvio(testo As String) As Boolean
        Try
            SyncLock _bloccoPrefisso
                If _avvioInAttesa = 0 Then Return False
                ' Se la frase di avvio non arriva, dopo qualche secondo si torna a parlare normalmente.
                If Environment.TickCount - _avvioInizio > 5000 Then
                    _avvioInAttesa = 0
                    Return False
                End If
                If _tenutiPerAvvio.Count < 20 Then _tenutiPerAvvio.Add(testo)
                Return True
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TieniPerAvvio", ex)
            Return False
        End Try
    End Function

    ' Una frase del programma che ospita il lettore (per esempio il suo menu): passa dalla coda della voce senza il controllo del
    ' silenzio, perché in quel momento il lettore tace e parla solo il programma.
    Public Sub AnnunciaDalProgramma(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If Sospeso Then Return
            If _codaVoce IsNot Nothing Then
                _codaVoce.Accoda(testo, PrioritaAnnuncio.Interruzione, True, True, True)
                Return
            End If
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura IsNot Nothing Then lettura.LeggiTesto(testo, priorita:="Alta")
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AnnunciaDalProgramma", ex)
        End Try
    End Sub

End Class
