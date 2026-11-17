' ============================================================================================
' SessioneEditorVideo - l'ambiente di montaggio: la linea del tempo, i tasti e l'ascolto
' ============================================================================================
'
' L'ambiente è una sola riga che dice a che punto del video ci si trova. Freccia destra e sinistra spostano di 1 secondo, con
' Shift di 100 millisecondi, con Ctrl di 10 secondi. Spazio avvia e ferma l'ascolto (la posizione si sposta da sola mentre suona).
' Invio apre il menu del punto: "Inizia taglio" ed "Esegui modifica". Con il taglio iniziato ci si muove avanti o indietro e un
' secondo Invio, dove ci si ferma, taglia il pezzo; Esc lo annulla. Alt sinistro apre l'esportazione.
'
' Questo file ha la parte principale; i menu delle modifiche sono in SessioneEditorVideo.Menu.vb e l'esportazione in
' SessioneEditorVideo.Esporta.vb.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.Wave

Partial Public NotInheritable Class SessioneEditorVideo
    Implements IGestoreTastiMusicali, IDisposable

    <DllImport("user32.dll")>
    Private Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    Private Const VkSinistra As Integer = &H25
    Private Const VkDestra As Integer = &H27
    Private Const VkSpazio As Integer = &H20
    Private Const VkCtrl As Integer = &H11
    Private Const VkAlt As Integer = &H12
    Private Const IdControllo As String = "controllo"

    Private ReadOnly _log As New Logger()
    Private ReadOnly _p As ProgettoVideo
    Private _lista As WGMidiRiproduzione
    Private _chiusa As Boolean
    Private _timerFuoco As System.Threading.Timer
    Private _spazioPremuto As Boolean
    Private ReadOnly _bloccoPosizione As New Object()
    Private _posizione As Double
    Private ReadOnly _cartellaLavoro As String = Path.Combine(Path.GetTempPath(), "wg-editor-video-" & Guid.NewGuid().ToString("N"))

    ' il taglio
    Private _inTaglio As Boolean
    Private _inizioTaglio As Double

    ' l'ascolto
    Private _wavAnteprima As String = ""
    Private _anteprimaPronta As Boolean
    Private _uscita As WaveOutEvent
    Private _lettore As AudioFileReader
    Private _inAscolto As Boolean
    Private _timerAscolto As System.Threading.Timer
    Private _ultimoAggiornamentoRiga As Long

    ' il voiceover
    Private _registrandoVoce As Boolean
    Private _ingressoVoce As WaveInEvent
    Private _scrittoreVoce As WaveFileWriter
    Private _percorsoVoce As String = ""
    Private _inizioVoce As Double

    Private _esportando As Boolean
    Private ReadOnly _selettoreFile As New SelettoreFile()
    Private _selettoreCartella As SelettoreCartellaMusica

    Public Sub New(progetto As ProgettoVideo)
        _p = progetto
    End Sub

    Public Sub Avvia()
        Try
            Directory.CreateDirectory(_cartellaLavoro)
            _lista = New WGMidiRiproduzione()
            _lista.multiselezioneConsentita = False
            _lista.impostaTitolo("Editor video")
            _lista.aggiungiElemento(TestoRiga(), IdControllo)
            AddHandler _lista.escPremuto, AddressOf OnEsc
            AddHandler _lista.ElementoSelezionato, AddressOf OnInvio
            _lista.AltSinistroDiretto = Sub() AvviaEsportazione()
            _lista.avvia()
            WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Me)
            _timerFuoco = New System.Threading.Timer(AddressOf ControllaFuoco, Nothing, 300, 300)
            ImpostaPosizione(_p.Posizione)
            _lista.aggiungiElemento(TestoRiga(), IdControllo)
            MemorizzaFirma()
            AiutoMusica.AnnunciaInSottofondo(If(_p.Nome <> "", "Progetto " & _p.Nome & ". ", "") & _p.Riepilogo())
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire l'editor video.")
        End Try
    End Sub

    ' ---- la riga -----------------------------------------------------------------------------------------

    Private Function TestoRiga() As String
        Dim posizione As Double
        SyncLock _bloccoPosizione
            posizione = _posizione
        End SyncLock
        Dim t As String = "Posizione " & ProgettoVideo.TestoTempo(posizione) & " su " & ProgettoVideo.TestoTempo(_p.Durata)
        If _inTaglio Then t &= ". Taglio iniziato a " & ProgettoVideo.TestoTempo(_inizioTaglio)
        If _registrandoVoce Then
            t &= ". Registrazione del voiceover in corso"
        ElseIf _inAscolto Then
            t &= ", in ascolto"
        End If
        Return t
    End Function

    Private Sub AggiornaRiga()
        Dim lista As WGMidiRiproduzione = _lista
        If lista IsNot Nothing Then lista.aggiungiElemento(TestoRiga(), IdControllo)
    End Sub

    Private Function PosizioneCorrente() As Double
        SyncLock _bloccoPosizione
            Return _posizione
        End SyncLock
    End Function

    Private Sub ImpostaPosizione(valore As Double)
        SyncLock _bloccoPosizione
            _posizione = Math.Max(0.0, Math.Min(_p.Durata, valore))
        End SyncLock
    End Sub

    ' ---- i tasti -------------------------------------------------------------------------------------------

    Private Function Attiva() As Boolean
        Dim lista As WGMidiRiproduzione = _lista
        If _chiusa OrElse lista Is Nothing Then Return False
        Return lista.PuoRicevereInput() AndAlso Not lista.OpzioniAperte
    End Function

    Private Shared Function Premuto(codice As Integer) As Boolean
        Return (GetAsyncKeyState(codice) And &H8000) <> 0
    End Function

    Public Function GestisciTasto(vkCode As Integer, scanCode As Integer, premuto As Boolean, shift As Boolean, modificatori As Boolean) As Boolean Implements IGestoreTastiMusicali.GestisciTasto
        Try
            If _chiusa Then Return False
            If vkCode <> VkSinistra AndAlso vkCode <> VkDestra AndAlso vkCode <> VkSpazio Then Return False
            Dim alt As Boolean = SessioneEditorVideo.Premuto(VkAlt)
            Dim ctrl As Boolean = SessioneEditorVideo.Premuto(VkCtrl)

            If Not premuto Then
                If vkCode = VkSpazio Then _spazioPremuto = False
                Return Not alt AndAlso Attiva()
            End If
            If alt OrElse _esportando OrElse Not Attiva() Then Return False

            If vkCode = VkSpazio Then
                If Not _spazioPremuto Then
                    _spazioPremuto = True
                    AiutoMusica.SuInterfaccia(Sub() AvviaFermaAscolto())
                End If
                Return True
            End If
            Dim passo As Double = If(ctrl, 10.0, If(shift, 0.1, 1.0))
            Muovi(If(vkCode = VkDestra, passo, -passo))
            Return True
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.GestisciTasto: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Sposta la posizione. Il gancio della tastiera deve restituire subito: qui si fa solo il necessario e si parla in sottofondo.</summary>
    Private Sub Muovi(delta As Double)
        Dim prima As Double = PosizioneCorrente()
        Dim dopo As Double = Math.Max(0.0, Math.Min(_p.Durata, Math.Round((prima + delta) * 1000.0) / 1000.0))
        ImpostaPosizione(dopo)
        If _inAscolto Then
            Try
                Dim lettore As AudioFileReader = _lettore
                If lettore IsNot Nothing Then lettore.CurrentTime = TimeSpan.FromSeconds(dopo)
            Catch ex As Exception
                _log.ScriviLog("SessioneEditorVideo.Muovi (ascolto): " & ex.Message)
            End Try
        End If
        AggiornaRiga()
        Dim testo As String = ProgettoVideo.TestoTempo(dopo)
        If dopo <= 0.0 AndAlso delta < 0 Then
            testo = "Inizio del video"
        ElseIf dopo >= _p.Durata AndAlso delta > 0 Then
            testo = "Fine del video, " & ProgettoVideo.TestoTempo(dopo)
        End If
        If _inTaglio Then testo &= ", taglio di " & ProgettoVideo.TestoTempo(Math.Abs(dopo - _inizioTaglio))
        AiutoMusica.AnnunciaInSottofondo(testo)
    End Sub

    Private Sub ControllaFuoco(stato As Object)
        Try
            If _chiusa Then Return
            Dim lista As WGMidiRiproduzione = _lista
            If lista Is Nothing Then Return
            If lista.Chiusa Then AiutoMusica.SuInterfaccia(Sub() Chiudi())
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.ControllaFuoco: " & ex.Message)
        End Try
    End Sub

    ' ---- l'ascolto -------------------------------------------------------------------------------------------

    ''' <summary>Spazio: avvia l'ascolto dal punto in cui si è, o lo ferma. L'audio che si sente è quello del montaggio (effetti, voiceover e base compresi).</summary>
    Private Async Sub AvviaFermaAscolto()
        Try
            If _chiusa OrElse _esportando Then Return
            If _registrandoVoce Then
                TerminaRegistrazioneVoce()
                Return
            End If
            If _inAscolto Then
                FermaAscolto()
                AiutoMusica.AnnunciaInSottofondo("Ascolto fermo, " & ProgettoVideo.TestoTempo(PosizioneCorrente()))
                Return
            End If
            If Not Await PreparaAnteprimaAsync() Then Return
            If PosizioneCorrente() >= _p.Durata - 0.05 Then
                AiutoMusica.AnnunciaInSottofondo("Sei alla fine del video")
                Return
            End If
            AvviaAscolto()
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.AvviaFermaAscolto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile riprodurre il video.")
        End Try
    End Sub

    ''' <summary>Prepara (se serve) il file con l'audio del montaggio. Restituisce falso se non è riuscito e ha già avvisato.</summary>
    Private Async Function PreparaAnteprimaAsync() As Task(Of Boolean)
        If _anteprimaPronta AndAlso File.Exists(_wavAnteprima) Then Return True
        Dim destinazione As String = Path.Combine(_cartellaLavoro, "anteprima-" & Guid.NewGuid().ToString("N") & ".wav")
        Dim errore As String = Await WGProcessoInCorso.EseguiAsync("Sto preparando l'audio, attendere", Function() Task.Run(Function() EsportazioneVideo.PreparaAnteprimaAudio(_p, destinazione)))
        If errore <> "" Then
            AiutoMusica.Messaggio(errore)
            Return False
        End If
        Dim vecchio As String = _wavAnteprima
        _wavAnteprima = destinazione
        _anteprimaPronta = True
        If vecchio <> "" Then CancellaConCalma(vecchio)
        Return True
    End Function

    Private Shared Sub CancellaConCalma(percorso As String)
        Task.Run(Sub()
                     Thread.Sleep(1500)
                     Try
                         If File.Exists(percorso) Then File.Delete(percorso)
                     Catch
                     End Try
                 End Sub)
    End Sub

    ''' <summary>L'audio è cambiato: l'anteprima si rifà la prossima volta che serve.</summary>
    Friend Sub AudioCambiato()
        If _inAscolto Then FermaAscolto()
        _anteprimaPronta = False
    End Sub

    Private Sub AvviaAscolto()
        FermaAscolto()
        Dim inizio As Double = _p.SaltaTagli(PosizioneCorrente())
        ImpostaPosizione(inizio)
        _lettore = New AudioFileReader(_wavAnteprima)
        _lettore.CurrentTime = TimeSpan.FromSeconds(inizio)
        _uscita = New WaveOutEvent()
        _uscita.DesiredLatency = 100
        _uscita.Init(_lettore)
        _uscita.Play()
        _inAscolto = True
        _timerAscolto = New System.Threading.Timer(AddressOf OnTimerAscolto, Nothing, 100, 100)
        AggiornaRiga()
        AiutoMusica.AnnunciaInSottofondo("Ascolto")
    End Sub

    Private Sub FermaAscolto()
        Try
            _inAscolto = False
            If _timerAscolto IsNot Nothing Then
                _timerAscolto.Dispose()
                _timerAscolto = Nothing
            End If
            If _uscita IsNot Nothing Then
                Try
                    _uscita.Stop()
                Catch
                End Try
                _uscita.Dispose()
                _uscita = Nothing
            End If
            If _lettore IsNot Nothing Then
                _lettore.Dispose()
                _lettore = Nothing
            End If
            AggiornaRiga()
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.FermaAscolto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Mentre suona la posizione segue l'audio; i pezzi tagliati si saltano e alla fine l'ascolto si ferma.</summary>
    Private Sub OnTimerAscolto(stato As Object)
        Try
            Dim lettore As AudioFileReader = _lettore
            If Not _inAscolto OrElse lettore Is Nothing Then Return
            Dim attuale As Double = lettore.CurrentTime.TotalSeconds
            Dim saltata As Double = _p.SaltaTagli(attuale)
            If saltata > attuale + 0.001 Then
                lettore.CurrentTime = TimeSpan.FromSeconds(saltata)
                attuale = saltata
            End If
            ImpostaPosizione(attuale)
            If attuale >= _p.Durata - 0.05 OrElse (_uscita IsNot Nothing AndAlso _uscita.PlaybackState = PlaybackState.Stopped) Then
                AiutoMusica.SuInterfaccia(Sub()
                                              If Not _inAscolto Then Return
                                              FermaAscolto()
                                              ImpostaPosizione(_p.Durata)
                                              AggiornaRiga()
                                              AiutoMusica.AnnunciaInSottofondo("Fine del video")
                                          End Sub)
                Return
            End If
            Dim adesso As Long = Environment.TickCount
            If adesso - _ultimoAggiornamentoRiga > 500 Then
                _ultimoAggiornamentoRiga = adesso
                AggiornaRiga()
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.OnTimerAscolto: " & ex.Message)
        End Try
    End Sub

    ' ---- il taglio ---------------------------------------------------------------------------------------------

    Private Sub IniziaTaglio()
        _inTaglio = True
        _inizioTaglio = PosizioneCorrente()
        AggiornaRiga()
        AiutoMusica.Annuncia("Taglio iniziato a " & ProgettoVideo.TestoTempo(_inizioTaglio) & ". Spostati avanti o indietro e premi Invio per tagliare, Esc per annullare")
    End Sub

    ''' <summary>Invio con il taglio iniziato: il pezzo tra il punto di inizio e quello in cui ci si è fermati si taglia.</summary>
    Private Sub TerminaTaglio()
        Dim fine As Double = PosizioneCorrente()
        Dim inizio As Double = _inizioTaglio
        _inTaglio = False
        If Math.Abs(fine - inizio) < 0.02 Then
            AggiornaRiga()
            AiutoMusica.Messaggio("Non hai tagliato niente: il punto di fine è uguale a quello di inizio.")
            Return
        End If
        Dim da As Double = Math.Min(inizio, fine)
        Dim a As Double = Math.Max(inizio, fine)
        _p.AggiungiTaglio(da, a)
        AudioCambiato()
        ImpostaPosizione(da)
        AggiornaRiga()
        AiutoMusica.Annuncia("Tagliato il pezzo da " & ProgettoVideo.TestoTempo(da) & " a " & ProgettoVideo.TestoTempo(a) & ", " & ProgettoVideo.TestoTempo(a - da) & ". Il video ora dura " & ProgettoVideo.TestoTempo(_p.DurataFinale()))
    End Sub

    ' ---- il voiceover registrato -------------------------------------------------------------------------------

    Friend Async Sub IniziaRegistrazioneVoce()
        Try
            If _registrandoVoce Then Return
            If WaveInEvent.DeviceCount = 0 Then
                AiutoMusica.Messaggio("Non c'è nessun microfono collegato al computer.")
                Return
            End If
            If Not Await PreparaAnteprimaAsync() Then Return
            _inizioVoce = _p.SaltaTagli(PosizioneCorrente())
            _percorsoVoce = Path.Combine(_cartellaLavoro, "voce-" & Guid.NewGuid().ToString("N") & ".wav")
            AiutoMusica.SegnaleInizio()
            _ingressoVoce = New WaveInEvent()
            _ingressoVoce.WaveFormat = New WaveFormat(48000, 16, 1)
            _ingressoVoce.BufferMilliseconds = 50
            _scrittoreVoce = New WaveFileWriter(_percorsoVoce, _ingressoVoce.WaveFormat)
            AddHandler _ingressoVoce.DataAvailable, Sub(s As Object, e As WaveInEventArgs)
                                                        Try
                                                            Dim scrittore As WaveFileWriter = _scrittoreVoce
                                                            If scrittore IsNot Nothing Then scrittore.Write(e.Buffer, 0, e.BytesRecorded)
                                                        Catch
                                                        End Try
                                                    End Sub
            _registrandoVoce = True
            _ingressoVoce.StartRecording()
            ImpostaPosizione(_inizioVoce)
            AvviaAscolto()
            AggiornaRiga()
            AiutoMusica.AnnunciaInSottofondo("Registrazione del voiceover dal punto " & ProgettoVideo.TestoTempo(_inizioVoce) & ". Premi Spazio per fermarla")
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.IniziaRegistrazioneVoce: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ChiudiRegistrazioneVoce()
            AiutoMusica.Messaggio("Non è stato possibile usare il microfono.")
        End Try
    End Sub

    Private Sub ChiudiRegistrazioneVoce()
        _registrandoVoce = False
        Try
            If _ingressoVoce IsNot Nothing Then
                Try
                    _ingressoVoce.StopRecording()
                Catch
                End Try
                _ingressoVoce.Dispose()
                _ingressoVoce = Nothing
            End If
            Dim scrittore As WaveFileWriter = _scrittoreVoce
            _scrittoreVoce = Nothing
            If scrittore IsNot Nothing Then scrittore.Dispose()
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.ChiudiRegistrazioneVoce: " & ex.Message)
        End Try
    End Sub

    Private Sub TerminaRegistrazioneVoce()
        Dim fine As Double = PosizioneCorrente()
        FermaAscolto()
        ChiudiRegistrazioneVoce()
        Dim durata As Double = Math.Max(0.0, fine - _inizioVoce)
        Try
            If durata < 0.3 OrElse Not File.Exists(_percorsoVoce) Then
                AiutoMusica.Messaggio("La registrazione era troppo breve.")
                Return
            End If
            _p.Voci.Add(New VoceFuoricampo() With {.Percorso = _percorsoVoce, .Inizio = _inizioVoce, .Durata = durata})
            AudioCambiato()
            AggiornaRiga()
            AiutoMusica.Annuncia("Voiceover aggiunto, da " & ProgettoVideo.TestoTempo(_inizioVoce) & " per " & ProgettoVideo.TestoTempo(durata))
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.TerminaRegistrazioneVoce: " & ex.Message)
        End Try
    End Sub

    ' ---- uscita ------------------------------------------------------------------------------------------------

    Private Sub OnEsc(sender As Object, e As EventArgs)
        Try
            If _esportando Then Return
            If _registrandoVoce Then
                FermaAscolto()
                ChiudiRegistrazioneVoce()
                AggiornaRiga()
                AiutoMusica.Annuncia("Registrazione del voiceover annullata")
                Return
            End If
            If _inTaglio Then
                _inTaglio = False
                AggiornaRiga()
                AiutoMusica.Annuncia("Taglio annullato")
                Return
            End If
            If _inAscolto Then FermaAscolto()
            If Not Modificato() Then
                Chiudi()
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Salva il progetto", Sub(s As Object, ev As EventArgs)
                                                   ChiudiMenu(menu)
                                                   SalvaProgetto(Sub() Chiudi())
                                               End Sub)
            menu.aggiungi("Esci senza salvare", Sub(s As Object, ev As EventArgs)
                                                    ChiudiMenu(menu)
                                                    Chiudi()
                                                End Sub)
            menu.aggiungi("Torna all'editor", Sub(s As Object, ev As EventArgs) ChiudiMenu(menu))
            menu.avvia("Salvare il progetto?")
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.OnEsc: " & ex.Message)
            Chiudi()
        End Try
    End Sub

    Friend Shared Sub ChiudiMenu(menu As WGMenu)
        Try
            menu.chiudi()
        Catch
        End Try
    End Sub

    Public Sub Chiudi()
        If _chiusa Then Return
        _chiusa = True
        Try
            Try
                WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Nothing)
            Catch
            End Try
            If _timerFuoco IsNot Nothing Then _timerFuoco.Dispose()
            _timerFuoco = Nothing
            FermaAscolto()
            ChiudiRegistrazioneVoce()
            If _lista IsNot Nothing Then
                Try
                    RemoveHandler _lista.escPremuto, AddressOf OnEsc
                    RemoveHandler _lista.ElementoSelezionato, AddressOf OnInvio
                    _lista.AltSinistroDiretto = Nothing
                    _lista.abbandona()
                Catch ex As Exception
                    _log.ScriviLog("SessioneEditorVideo.Chiudi (lista): " & ex.Message)
                End Try
                _lista = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.Chiudi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        If Not _esportando Then PulisciLavoro()
    End Sub

    Private Sub PulisciLavoro()
        Dim cartella As String = _cartellaLavoro
        Task.Run(Sub()
                     Thread.Sleep(1500)
                     Try
                         If Directory.Exists(cartella) Then Directory.Delete(cartella, True)
                     Catch
                     End Try
                 End Sub)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub

End Class
