' ============================================================================================
' WGFotocamera - la fotocamera di WinGabriel: foto e video, con l'anteprima a tutto schermo
' ============================================================================================
'
' Si apre con l'anteprima della videocamera a tutto schermo (orizzontale o verticale, come si è scelto) e una riga di testo in basso.
' Invio scatta una foto o, in modalità video, avvia e ferma la registrazione, sempre con un suono. Alt apre le opzioni: modalità, sottofondo
' di registrazione, sincronia, videocamera, orientamento, risoluzione, controlli avanzati della videocamera e Configurazione.
'
' Il video porta anche la voce del microfono (con gli effetti scelti) e, se si è scelto, un sottofondo che si sente mentre si registra: la voce
' si rimette al suo posto rispetto al sottofondo e al video con gli stessi sistemi del registratore vocale (retta di allineamento, compensazione
' in millisecondi, calibrazione), e il risultato si unisce al video in un mp4.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

Public Class MenuFotocamera

    Public Sub Avvia()
        Dim sessione As New SessioneFotocamera()
        sessione.Avvia()
    End Sub

End Class

Public NotInheritable Class SessioneFotocamera
    Implements IDisposable

    Private Const IdControllo As String = "controllo"

    Private ReadOnly _log As New Logger()
    Private _lista As WGMidiRiproduzione
    Private _motore As MotoreFotocamera
    Private _chiusa As Boolean
    Private _occupato As Boolean
    Private _inRegistrazione As Boolean
    Private _timerFuoco As System.Threading.Timer
    Private ReadOnly _cartellaLavoro As String = Path.Combine(Path.GetTempPath(), "wg-fotocamera-" & Guid.NewGuid().ToString("N"))
    Private _firmaMotore As String = ""
    Private _inizioRegistrazione As Long

    ' tempo massimo di registrazione
    Private _timerLimite As System.Threading.Timer
    Private _idRegistrazione As Integer
    Private _fermataPerLimite As Boolean

    ' registrazione del video
    Private _acquisizione As AcquisizioneVoce
    Private _uscita As WaveOutEvent
    Private _lettoreSottofondo As SottofondoAudio
    Private _istanteAudio As Long
    Private _fileVideo As String = ""
    ''' <summary>L'ultimo video registrato in questa sessione: Spazio lo fa vedere e sentire.</summary>
    Private _ultimoVideo As String = ""
    Private _senzaAudio As Boolean
    Private _uscitaUsata As Boolean

    ' i file registrati o scattati che aspettano di essere salvati con "Salva"; stanno in una cartella temporanea che non si cancella
    Private ReadOnly _cartellaNonSalvati As String = Path.Combine(Path.GetTempPath(), "WinGabriel-fotocamera")
    Private ReadOnly _inSospeso As New List(Of String)()
    Private _selettoreCartella As SelettoreCartellaMusica

    ' l'aiuto per posizionarsi: con l'intelligenza artificiale che guarda l'inquadratura e dice come mettere videocamera e computer
    Private Const RichiesteMassimeAiuto As Integer = 40
    Private _aiuto As Boolean
    Private _aiutoInCorso As Boolean
    Private _aiutoRichieste As Integer
    Private _aiutoNota As String = ""
    Private ReadOnly _aiutoConsigli As New List(Of String)()

    ' le videocamere collegate (si leggono all'apertura): servono a "Passa alla fotocamera"
    Private _camere As List(Of String)

    ' ---- impostazioni ---------------------------------------------------------------------------------------

    Private Function Foto() As Boolean
        Return ConfigurazioneFotocamera.ModalitaFoto()
    End Function

    ''' <summary>Quello che serve per sapere se la videocamera va riaperta: se cambia una di queste cose lo stato dell'anteprima non vale più.</summary>
    Private Function FirmaImpostazioni() As String
        Return String.Join("|", ImpostazioniMusica.Leggi("cam_modalita", "video"), ConfigurazioneFotocamera.NomeCamera(), ConfigurazioneFotocamera.Verticale().ToString(),
                           ImpostazioniMusica.Leggi("cam_risoluzione", ""), ImpostazioniMusica.Leggi("cam_fps", "30"), ImpostazioniMusica.Leggi("cam_qualita", "1"))
    End Function

    Private Function CostruisciRipresa(conVideo As Boolean) As RipresaFotocamera
        Dim r As New RipresaFotocamera()
        r.Camera = ConfigurazioneFotocamera.NomeCamera()
        If r.Camera = "" Then
            Dim camere As List(Of String) = FfmpegUtil.Videocamere()
            If camere.Count = 0 Then Throw New InvalidOperationException("Non ho trovato nessuna videocamera.")
            r.Camera = camere(0)
        End If
        r.Verticale = ConfigurazioneFotocamera.Verticale()
        r.Qualita = Math.Max(0, Math.Min(2, ImpostazioniMusica.LeggiIntero("cam_qualita", 1)))
        Dim voluta As String = ImpostazioniMusica.Leggi("cam_risoluzione", "")
        Dim formati As List(Of FormatoCamera) = ControlliFotocamera.Formati(r.Camera)
        Dim scelto As FormatoCamera = Nothing
        If voluta <> "" Then scelto = formati.FirstOrDefault(Function(f) f.Larghezza.ToString() & "x" & f.Altezza.ToString() = voluta)
        If scelto Is Nothing Then
            ' la prima volta: la più grande che arrivi a 30 fotogrammi, ma non oltre il Full HD (le anteprime e i file restano leggeri)
            scelto = formati.FirstOrDefault(Function(f) f.Larghezza <= 1920 AndAlso f.FpsMassimi >= 29.0)
            If scelto Is Nothing AndAlso formati.Count > 0 Then scelto = formati(formati.Count - 1)
        End If
        If scelto IsNot Nothing Then
            r.Larghezza = scelto.Larghezza
            r.Altezza = scelto.Altezza
            r.Codec = scelto.Codec
            r.FormatoPixel = scelto.FormatoPixel
            r.Fps = CInt(Math.Max(5, Math.Min(ImpostazioniMusica.LeggiIntero("cam_fps", 30), Math.Floor(scelto.FpsMassimi))))
        Else
            r.Fps = ImpostazioniMusica.LeggiIntero("cam_fps", 30)
        End If
        If conVideo Then r.FileVideo = Path.Combine(_cartellaLavoro, "video-" & Guid.NewGuid().ToString("N") & ".mp4")
        If Foto() AndAlso Not conVideo Then r.FileFotogramma = Path.Combine(_cartellaLavoro, "fotogramma.jpg")
        Return r
    End Function

    ' ---- apertura ------------------------------------------------------------------------------------------------

    Public Async Sub Avvia()
        Try
            If FfmpegUtil.Percorso() = "" Then
                AiutoMusica.Messaggio("Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco.")
                Return
            End If
            Directory.CreateDirectory(_cartellaLavoro)
            Directory.CreateDirectory(_cartellaNonSalvati)
            ' il sottofondo scelto non si ricorda: a ogni apertura della fotocamera si riparte senza
            ImpostazioniMusica.Scrivi("cam_sottofondo", "")
            _lista = New WGMidiRiproduzione()
            _lista.multiselezioneConsentita = False
            _lista.impostaTitolo("Fotocamera")
            _lista.AnteprimaAttiva = True
            ' quello che si sente all'apertura è "Fotocamera pronta", detto quando la videocamera dà davvero le immagini
            _lista.aggiungiElemento("Fotocamera pronta", IdControllo)
            AddHandler _lista.escPremuto, AddressOf OnEsc
            AddHandler _lista.ElementoSelezionato, AddressOf OnInvio
            AddHandler _lista.SpazioPremuto, AddressOf OnSpazio
            AddHandler _lista.OpzioniChiuse, AddressOf OnOpzioniChiuse
            _lista.AggiornaOpzioni = AddressOf DefinisciOpzioni
            Dim errore As String = Await AvviaMotoreAsync(False)
            If _chiusa Then Return
            If errore <> "" Then
                AiutoMusica.Messaggio(errore)
                Chiudi()
                Return
            End If
            _lista.avvia()
            AggiornaRiga()
            _timerFuoco = New System.Threading.Timer(AddressOf ControllaStato, Nothing, 500, 500)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire la fotocamera.")
            Chiudi()
        End Try
    End Sub

    ''' <summary>Chiuso il menu delle opzioni con Esc si dice "Fotocamera"; scegliendo una voce parla già la voce scelta.</summary>
    Private Sub OnOpzioniChiuse(daSelezione As Boolean)
        If Not daSelezione AndAlso Not _chiusa Then AiutoMusica.AnnunciaInSottofondo("Fotocamera")
    End Sub

    ''' <summary>Apre la videocamera (con l'attesa) e collega l'anteprima. Restituisce "" se va bene, altrimenti il motivo.</summary>
    Private Async Function AvviaMotoreAsync(conVideo As Boolean) As Task(Of String)
        Return Await WGProcessoInCorso.EseguiAsync("Sto aprendo la fotocamera, attendere", Function() Task.Run(Function() AvviaMotore(conVideo)))
    End Function

    Private Function AvviaMotore(conVideo As Boolean) As String
        Try
            FermaMotore()
            If _camere Is Nothing Then _camere = FfmpegUtil.Videocamere()
            Dim r As RipresaFotocamera = CostruisciRipresa(conVideo)
            If conVideo Then _fileVideo = r.FileVideo
            Dim motore As New MotoreFotocamera()
            AddHandler motore.Fotogramma, Sub(img As Drawing.Bitmap)
                                              Dim lista As WGMidiRiproduzione = _lista
                                              If lista Is Nothing OrElse _chiusa Then
                                                  img.Dispose()
                                              Else
                                                  lista.ImpostaAnteprima(img)
                                              End If
                                          End Sub
            AddHandler motore.Interrotta, AddressOf OnMotoreInterrotto
            Dim errore As String = motore.Avvia(r)
            If errore <> "" Then Return errore
            _motore = motore
            _firmaMotore = FirmaImpostazioni()
            ' i controlli salvati (luminosità, esposizione, messa a fuoco...) si rimettono a videocamera aperta
            ControlliFotocamera.ApplicaSalvati(r.Camera)
            Return ""
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.AvviaMotore: " & ex.Message)
            Return ex.Message
        End Try
    End Function

    Private Function FermaMotore() As Boolean
        Dim m As MotoreFotocamera = _motore
        _motore = Nothing
        If m Is Nothing Then Return False
        Try
            RemoveHandler m.Interrotta, AddressOf OnMotoreInterrotto
        Catch
        End Try
        Return m.Ferma()
    End Function

    Private Sub OnMotoreInterrotto()
        Try
            If _chiusa OrElse _occupato Then Return
            AiutoMusica.SuInterfaccia(Sub()
                                          If _chiusa OrElse _occupato Then Return
                                          If _inRegistrazione Then
                                              TerminaRegistrazione()
                                          Else
                                              AiutoMusica.Messaggio("La videocamera si è fermata: forse è stata scollegata o la usa un altro programma.")
                                          End If
                                      End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OnMotoreInterrotto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Se dalle impostazioni è cambiato qualcosa che riguarda la videocamera, la si riapre con i valori nuovi.</summary>
    Private Async Sub ControllaStato(stato As Object)
        Try
            If _chiusa OrElse _occupato OrElse _inRegistrazione Then Return
            If _inRiproduzione Then
                AggiornaRiga()
                Return
            End If
            Dim lista As WGMidiRiproduzione = _lista
            If lista Is Nothing Then Return
            If lista.Chiusa Then
                AiutoMusica.SuInterfaccia(Sub() Chiudi())
                Return
            End If
            If lista.OpzioniAperte OrElse Not lista.PuoRicevereInput() Then Return
            If FirmaImpostazioni() = _firmaMotore Then Return
            _occupato = True
            AiutoMusica.SuInterfaccia(Async Sub()
                                          Try
                                              Dim errore As String = Await AvviaMotoreAsync(False)
                                              AggiornaRiga()
                                              If errore <> "" Then
                                                  AiutoMusica.Messaggio(errore)
                                              Else
                                                  AiutoMusica.Annuncia("Fotocamera pronta")
                                              End If
                                          Finally
                                              _occupato = False
                                          End Try
                                      End Sub)
        Catch ex As Exception
            _occupato = False
            _log.ScriviLog("SessioneFotocamera.ControllaStato: " & ex.Message)
        End Try
    End Sub

    ' ---- la riga -------------------------------------------------------------------------------------------------

    Private Function TestoRiga() As String
        If _inRegistrazione Then
            Dim secondi As Integer = CInt((Stopwatch.GetTimestamp() - _inizioRegistrazione) / Stopwatch.Frequency)
            Return "Registrazione in corso, " & secondi.ToString() & If(secondi = 1, " secondo", " secondi")
        End If
        If _inRiproduzione Then Return "Video, " & CInt(If(_inPausa, _posizionePausa, If(_lettore IsNot Nothing, _lettore.Posizione, 0.0))).ToString() & " secondi" & If(_inPausa, ", in pausa", "")
        If _aiuto Then Return "Aiuto posizione, richiesta " & Math.Min(_aiutoRichieste + 1, RichiesteMassimeAiuto).ToString() & " di " & RichiesteMassimeAiuto.ToString()
        Return "Fotocamera"
    End Function

    Private Sub AggiornaRiga()
        Dim lista As WGMidiRiproduzione = _lista
        If lista IsNot Nothing Then lista.aggiungiElemento(TestoRiga(), IdControllo)
    End Sub

    ' ---- Invio: foto o video -------------------------------------------------------------------------------------------

    Private Sub OnInvio(id As String)
        Try
            If _chiusa OrElse _occupato Then Return
            If _inRiproduzione Then
                Return
            ElseIf _aiuto Then
                ChiediConsiglio()
            ElseIf _inRegistrazione Then
                TerminaRegistrazione()
            ElseIf Foto() Then
                Scatta()
            Else
                IniziaRegistrazione()
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OnInvio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub


    ''' <summary>Lo scatto: il suono e il file nella cartella temporanea; si salva poi con "Salva" dal menu delle opzioni.</summary>
    Private Sub Scatta()
        Try
            Dim sorgente As String = Path.Combine(_cartellaLavoro, "fotogramma.jpg")
            If Not File.Exists(sorgente) Then
                AiutoMusica.Messaggio("La videocamera non ha ancora dato un fotogramma: aspetta un momento e riprova.")
                Return
            End If
            SuoniFotocamera.SuonaScatto()
            Directory.CreateDirectory(_cartellaNonSalvati)
            Dim destinazione As String = AiutoMusica.PercorsoNuovo(_cartellaNonSalvati, "Foto", ".jpg")
            File.Copy(sorgente, destinazione, False)
            _inSospeso.Add(destinazione)
            AiutoMusica.AnnunciaInSottofondo("Foto scattata")
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.Scatta: " & ex.Message)
            AiutoMusica.Messaggio("Non è stato possibile scattare la foto: " & ex.Message)
        End Try
    End Sub

    ' La luminosità media di un fotogramma, da 0 (nero) a 255; -1 se non si legge.
    Private Shared Function LuminositaMedia(immagine As Drawing.Bitmap) As Double
        Try
            Using piccola As New Drawing.Bitmap(immagine, 16, 9)
                Dim somma As Double = 0
                For y As Integer = 0 To 8
                    For x As Integer = 0 To 15
                        Dim c As Drawing.Color = piccola.GetPixel(x, y)
                        somma += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B
                    Next
                Next
                Return somma / 144.0
            End Using
        Catch
            Return -1
        End Try
    End Function

    ' Aspetta (al massimo massimoMs) che l'esposizione della videocamera si assesti, cioè che la luminosità smetta di cambiare per circa
    ' mezzo secondo. Restituisce l'ultima luminosità misurata (-1 se non c'è stato nessun fotogramma). Una stanza davvero buia dà una
    ' luminosità bassa ma stabile: in quel caso non si aspetta oltre.
    Private Function AspettaEsposizioneStabile(massimoMs As Integer) As Double
        Dim inizio As Long = Stopwatch.GetTimestamp()
        Dim precedente As Double = -1
        Dim ultima As Double = -1
        Dim stabili As Integer = 0
        Try
            Do
                Dim lista As WGMidiRiproduzione = _lista
                If lista Is Nothing OrElse _chiusa Then Exit Do
                Using immagine As Drawing.Bitmap = lista.CopiaAnteprima()
                    If immagine IsNot Nothing Then
                        Dim l As Double = LuminositaMedia(immagine)
                        If l >= 0 Then
                            ultima = l
                            If precedente >= 0 AndAlso Math.Abs(l - precedente) < 2.5 Then stabili += 1 Else stabili = 0
                            precedente = l
                            If stabili >= 5 Then Exit Do
                        End If
                    End If
                End Using
                If (Stopwatch.GetTimestamp() - inizio) * 1000L \ Stopwatch.Frequency >= massimoMs Then Exit Do
                System.Threading.Thread.Sleep(120)
            Loop
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.AspettaEsposizioneStabile: " & ex.Message)
        End Try
        Return ultima
    End Function

    Private Async Sub IniziaRegistrazione()
        _occupato = True
        Dim ripristina As Boolean = False
        Try
            ' la videocamera si riapre con la registrazione (l'anteprima si ferma per un attimo)
            Dim errore As String = Await AvviaMotoreAsync(True)
            If errore <> "" Then
                AiutoMusica.Messaggio(errore)
                ripristina = True
                Exit Try
            End If
            ' appena riaperta, la videocamera dà immagini scure finché l'esposizione non si assesta: si aspetta, e quell'inizio non finisce nel video
            Await Task.Run(Sub() AspettaEsposizioneStabile(4000))
            ' il suono di inizio si sente prima che il microfono si apra, quindi non finisce nel video
            Await Task.Run(Sub() SuoniFotocamera.SuonaInizioVideo())
            Dim percorsoSottofondo As String = ImpostazioniMusica.Leggi("cam_sottofondo", "")
            _senzaAudio = WaveInEvent.DeviceCount = 0
            If Not _senzaAudio Then
                Dim dispositivo As Integer = TrovaMicrofono()
                Dim uscita As WaveOutEvent = Nothing
                If percorsoSottofondo <> "" AndAlso File.Exists(percorsoSottofondo) Then
                    _lettoreSottofondo = New SottofondoAudio(percorsoSottofondo)
                    _lettoreSottofondo.Ripeti = ImpostazioniMusica.LeggiBooleano("voce_ripeti_sottofondo", False)
                    Dim volume As New VolumeSampleProvider(_lettoreSottofondo)
                    Dim v As Single = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("voce_volume_sottofondo", 60))) / 100.0F
                    volume.Volume = v * v
                    Dim mix As New MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                    mix.ReadFully = True
                    mix.AddMixerInput(volume)
                    uscita = New WaveOutEvent()
                    uscita.DesiredLatency = 100
                    uscita.NumberOfBuffers = 3
                    uscita.Init(New SampleToWaveProvider(New LimitatoreMorbido(mix)))
                    _uscita = uscita
                End If
                _acquisizione = New AcquisizioneVoce()
                _istanteAudio = Stopwatch.GetTimestamp()
                _acquisizione.Avvia(dispositivo, uscita)
                If uscita IsNot Nothing Then uscita.Play()
            End If
            _inRegistrazione = True
            _inizioRegistrazione = Stopwatch.GetTimestamp()
            AvviaLimite()
            AggiornaRiga()
            AiutoMusica.AnnunciaInSottofondo("Registrazione")
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.IniziaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ChiudiAudio()
            AiutoMusica.Messaggio("Non è stato possibile avviare la registrazione: " & ex.Message)
            ripristina = True
        Finally
            _occupato = False
        End Try
        If ripristina Then
            _occupato = True
            Try
                Await AvviaMotoreAsync(False)
            Finally
                _occupato = False
            End Try
        End If
    End Sub

    ''' <summary>Se è stato impostato un tempo massimo, il video si ferma da solo quando lo raggiunge: come premere Invio, senza perdere niente.</summary>
    Private Sub AvviaLimite()
        Try
            FermaLimite()
            Dim secondi As Integer = ConfigurazioneRegistratore.SecondiLimite("cam_max_secondi")
            If secondi <= 0 Then Return
            Dim id As Integer = Interlocked.Increment(_idRegistrazione)
            _timerLimite = New System.Threading.Timer(Sub(stato As Object)
                                                          AiutoMusica.SuInterfaccia(Sub()
                                                                                        If _chiusa OrElse Not _inRegistrazione OrElse id <> _idRegistrazione Then Return
                                                                                        _fermataPerLimite = True
                                                                                        TerminaRegistrazione()
                                                                                    End Sub)
                                                      End Sub, Nothing, secondi * 1000L, Timeout.Infinite)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.AvviaLimite: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaLimite()
        Try
            Interlocked.Increment(_idRegistrazione)
            If _timerLimite IsNot Nothing Then
                _timerLimite.Dispose()
                _timerLimite = Nothing
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Il ritardo della videocamera: il fotogramma che arriva a ffmpeg mostra quello che c'era davanti qualche decina di millisecondi prima. Non si
    ''' può misurare senza un lampo e un suono, quindi si stima: tre periodi di fotogramma (a 30 al secondo, 100 ms), tra esposizione, trasferimento
    ''' e consegna del fotogramma all'anteprima.
    ''' </summary>
    Private Function LatenzaVideoStimataMs() As Double
        Dim fps As Integer = Math.Max(5, ImpostazioniMusica.LeggiIntero("cam_fps", 30))
        Return 3000.0 / fps
    End Function

    Private Function TrovaMicrofono() As Integer
        Try
            Dim nome As String = ImpostazioniMusica.Leggi("voce_microfono", "")
            If nome <> "" Then
                For i As Integer = 0 To WaveInEvent.DeviceCount - 1
                    If String.Equals(WaveInEvent.GetCapabilities(i).ProductName, nome, StringComparison.OrdinalIgnoreCase) Then Return i
                Next
            End If
        Catch
        End Try
        Return 0
    End Function

    Private Sub ChiudiAudio()
        Try
            If _uscita IsNot Nothing Then
                Try
                    _uscita.Stop()
                Catch
                End Try
                _uscita.Dispose()
                _uscita = Nothing
            End If
            If _lettoreSottofondo IsNot Nothing Then
                _lettoreSottofondo.Dispose()
                _lettoreSottofondo = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.ChiudiAudio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Ferma la registrazione e prepara il video in sottofondo, con l'attesa: voce al suo posto, effetti, sottofondo e unione con il video.</summary>
    Private Async Sub TerminaRegistrazione()
        If Not _inRegistrazione Then Return
        FermaLimite()
        _occupato = True
        _inRegistrazione = False
        Dim acquisizione As AcquisizioneVoce = _acquisizione
        _acquisizione = Nothing
        Dim campioni() As Short = Nothing
        Dim a As Double = 0.0
        Dim d As Double = 0.0
        Dim frequenza As Integer = 48000
        Try
            If acquisizione IsNot Nothing Then
                campioni = acquisizione.Ferma()
                frequenza = acquisizione.Frequenza
                _uscitaUsata = _uscita IsNot Nothing
                If _uscitaUsata Then acquisizione.Allineamento(a, d)
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.TerminaRegistrazione (audio): " & ex.Message)
        End Try
        _uscitaUsata = _uscita IsNot Nothing
        ChiudiAudio()
        Dim conSottofondo As Boolean = ImpostazioniMusica.Leggi("cam_sottofondo", "") <> "" AndAlso File.Exists(ImpostazioniMusica.Leggi("cam_sottofondo", ""))
        Dim ritardoAudioMs As Integer = If(_motore IsNot Nothing, _motore.MillisecondiPrimaDi(_istanteAudio), 0)
        ' la latenza vera del microfono si sottrae sempre, ma solo se la voce si è registrata sopra un sottofondo che si sentiva
        Dim latenzaBase As Double = If(conSottofondo AndAlso _uscitaUsata, LatenzaAudio.BaseMs(), 0.0)
        Dim latenzaVideo As Double = LatenzaVideoStimataMs()

        Dim sottofondo As String = ImpostazioniMusica.Leggi("cam_sottofondo", "")
        If sottofondo <> "" AndAlso Not File.Exists(sottofondo) Then sottofondo = ""
        Dim ripeti As Boolean = ImpostazioniMusica.LeggiBooleano("voce_ripeti_sottofondo", False)
        Dim guadagnoSottofondo As Single = CSng((ImpostazioniMusica.LeggiIntero("voce_volume_sottofondo", 60) / 100.0) ^ 2)
        Dim volumeVoce As Double = Math.Max(0, Math.Min(400, ImpostazioniMusica.LeggiIntero("voce_volume", 100))) / 100.0
        Dim regolazioneFine As Double = Math.Max(-500, Math.Min(500, ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0)))
        Dim effetti As ImpostazioniEffettiVoce = ImpostazioniEffettiVoce.Leggi()
        effetti.Sottofondo = sottofondo
        Dim perLimite As Boolean = _fermataPerLimite
        _fermataPerLimite = False
        Dim senzaAudio As Boolean = _senzaAudio OrElse campioni Is Nothing OrElse campioni.Length < frequenza \ 5
        AggiornaRiga()
        Dim esito As String = ""
        Dim destinazione As String = ""
        Try
            Task.Run(Sub() SuoniFotocamera.SuonaFineVideo())
            Await WGProcessoInCorso.EseguiAsync("Sto preparando il video, attendere",
                Function() Task.Run(Sub()
                                        Dim tempi As New List(Of String)()
                                        Try
                                            Dim ok As Boolean = FermaMotore()
                                            ' l'anteprima riparte subito, mentre si prepara il video
                                            AvviaMotore(False)
                                            If Not ok Then
                                                esito = "Il video non è stato salvato."
                                                Return
                                            End If
                                            Dim sorgenteVideo As String = _fileVideo
                                            Directory.CreateDirectory(_cartellaNonSalvati)
                                            destinazione = AiutoMusica.PercorsoNuovo(_cartellaNonSalvati, "Video", ".mp4")
                                            If senzaAudio Then
                                                File.Copy(sorgenteVideo, destinazione, False)
                                                Return
                                            End If
                                            If effetti.Attivo Then campioni = EffettiVoce.Applica(campioni, frequenza, effetti)
                                            Dim voceWav As String = Path.Combine(_cartellaLavoro, "voce-" & Guid.NewGuid().ToString("N") & ".wav")
                                            Dim mixWav As String = Path.Combine(_cartellaLavoro, "mix-" & Guid.NewGuid().ToString("N") & ".wav")
                                            tempi.Add(voceWav)
                                            tempi.Add(mixWav)
                                            Dim problema As String = AllineatoreVoce.Unisci(campioni, campioni.Length, frequenza, a, d, latenzaBase + regolazioneFine, "", False, volumeVoce, 0.0, voceWav, True)
                                            If problema <> "" Then
                                                esito = problema
                                                Return
                                            End If
                                            Dim fotogrammi As Long
                                            Using lettore As New WaveFileReader(voceWav)
                                                fotogrammi = lettore.Length \ lettore.WaveFormat.BlockAlign
                                            End Using
                                            problema = AllineatoreVoce.UnisciTracce(New List(Of String)() From {voceWav}, New List(Of Double)() From {1.0}, fotogrammi, sottofondo, ripeti, guadagnoSottofondo, mixWav)
                                            If problema <> "" Then
                                                esito = problema
                                                Return
                                            End If
                                            ' lo zero dell'audio unito è l'inizio del sottofondo, non l'apertura del microfono: si toglie dove la retta lo mette (d, meno la latenza), e si somma il ritardo della videocamera
                                            Dim zeroSottofondoMs As Double = If(_uscitaUsata, d * 1000.0 / WGDsp.FC - latenzaBase, 0.0)
                                            problema = FfmpegUtil.UnisciVideoAudio(sorgenteVideo, CInt(Math.Round(ritardoAudioMs - zeroSottofondoMs + latenzaVideo)), mixWav, destinazione)
                                            If problema <> "" Then esito = problema
                                        Catch ex As Exception
                                            _log.ScriviLog("SessioneFotocamera.TerminaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                            esito = "Non è stato possibile preparare il video: " & ex.Message
                                        Finally
                                            For Each t As String In tempi
                                                Try
                                                    If File.Exists(t) Then File.Delete(t)
                                                Catch
                                                End Try
                                            Next
                                        End Try
                                    End Sub))
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.TerminaRegistrazione (attesa): " & ex.Message)
            esito = "Non è stato possibile preparare il video."
        Finally
            _occupato = False
        End Try
        If _chiusa Then Return
        AggiornaRiga()
        If esito <> "" Then
            AiutoMusica.Messaggio(esito)
        Else
            _inSospeso.Add(destinazione)
            _ultimoVideo = destinazione
            AiutoMusica.Annuncia(If(perLimite, "Tempo massimo raggiunto. ", "") & "Video registrato")
        End If
    End Sub

    ' ---- le opzioni con Alt ---------------------------------------------------------------------------------------------

    ' Il menu con Alt ha solo quello che serve mentre si inquadra: orientamento, controlli avanzati e risoluzione, e, se le videocamere sono
    ' più d'una, "Passa alla fotocamera", più le tre cose della voce: sottofondo, effetti e sincronia (come nel registratore vocale). Il resto (modalità
    ' foto o video, scelta della videocamera da un elenco, cartella...) sta in Impostazioni, Applicazioni, Fotocamera.
    Private Sub DefinisciOpzioni()
        _lista.pulisciOpzioni()
        If _inRegistrazione OrElse _occupato Then Return
        If _inRiproduzione Then _lista.aggiungiOpzione("Dall'inizio", AddressOf OpzioneDaCapo)
        _lista.aggiungiOpzione("Aiuto posizione", AddressOf OpzioneAiutoPosizione)
        _lista.aggiungiOpzione("Orientamento", AddressOf OpzioneOrientamento)
        _lista.aggiungiOpzione("Avanzate", AddressOf OpzioneAvanzate)
        _lista.aggiungiOpzione("Risoluzione", AddressOf OpzioneRisoluzione)
        _lista.aggiungiOpzione(ConfigurazioneFotocamera.TestoVoceSottofondo(), AddressOf OpzioneSottofondo)
        _lista.aggiungiOpzione("Volume", AddressOf OpzioneVolume)
        _lista.aggiungiOpzione("Effetti", AddressOf OpzioneEffettiVoce)
        _lista.aggiungiOpzione("Sincronia", AddressOf OpzioneSincronia)
        _lista.aggiungiOpzione("Tempo massimo", AddressOf OpzioneTempoMassimo)
        If FileInSospeso().Count > 0 Then _lista.aggiungiOpzione("Salva", AddressOf OpzioneSalva)
        If _camere IsNot Nothing AndAlso _camere.Count > 1 Then _lista.aggiungiOpzione("Fotocamera", AddressOf OpzionePassaFotocamera)
    End Sub

    Private Sub OpzioneVolume(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.MostraVolume()
    End Sub

    Private Sub OpzioneTempoMassimo(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.ScegliLimite("cam_max_secondi")
    End Sub

    Private Sub OpzioneSottofondo(sender As Object, e As EventArgs)
        ConfigurazioneFotocamera.GestisciSottofondo()
    End Sub

    Private Sub OpzioneEffettiVoce(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.MostraEffettiVoce()
    End Sub

    Private Sub OpzioneSincronia(sender As Object, e As EventArgs)
        ConfigurazioneFotocamera.MostraSincronia()
    End Sub

    ' ---- Salva ---------------------------------------------------------------------------------------------------------
    ' Foto e video restano in una cartella temporanea finché non si sceglie "Salva": con la cartella dell'utente si chiede solo il nome, altrimenti
    ' prima la cartella (con il selettore di file di WinGabriel, che parte dalle unità del computer) e poi il nome, file per file.

    Private Function FileInSospeso() As List(Of String)
        _inSospeso.RemoveAll(Function(f) Not File.Exists(f))
        Return _inSospeso
    End Function

    ''' <summary>Quello che si fa quando il salvataggio è finito (all'uscita: chiudere la fotocamera).</summary>
    Private _dopoSalvataggio As Action

    Private Sub ScartaInSospeso()
        For Each f As String In New List(Of String)(_inSospeso)
            Try
                If File.Exists(f) Then File.Delete(f)
            Catch ex As Exception
                _log.ScriviLog("SessioneFotocamera.ScartaInSospeso: " & ex.Message)
            End Try
        Next
        _inSospeso.Clear()
        _ultimoVideo = ""
    End Sub

    Private Sub OpzioneSalva(sender As Object, e As EventArgs)
        Try
            Dim file As List(Of String) = New List(Of String)(FileInSospeso())
            If file.Count = 0 Then
                AiutoMusica.Messaggio("Non c'è niente da salvare.")
                Return
            End If
            If file.Count = 1 Then
                IniziaSalvataggio(file)
                Return
            End If
            ' più file registrati: si sceglie quali salvare
            Dim menu As New WGMenu()
            menu.aggiungi("Tutti", Sub()
                                       menu.chiudi()
                                       IniziaSalvataggio(file)
                                   End Sub)
            menu.aggiungi("Solo l'ultimo", Sub()
                                               menu.chiudi()
                                               IniziaSalvataggio(New List(Of String)() From {file(file.Count - 1)})
                                           End Sub)
            menu.aggiungi("A scelta", Sub()
                                          menu.chiudi()
                                          ScegliFileDaSalvare(file)
                                      End Sub)
            menu.avvia("Salva, " & file.Count.ToString() & " file")
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OpzioneSalva: " & ex.Message)
        End Try
    End Sub

    ''' <summary>"A scelta": l'elenco dei file registrati, con la selezione multipla come gli altri elenchi di WinGabriel; Invio salva quelli scelti.</summary>
    Private Sub ScegliFileDaSalvare(file As List(Of String))
        Try
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = True
            lista.impostaTitolo("A scelta")
            For i As Integer = 0 To file.Count - 1
                lista.aggiungiElemento(Path.GetFileNameWithoutExtension(file(i)), file(i))
            Next
            AddHandler lista.ElementiSelezionati, Sub(ids As List(Of String))
                                                      If ids Is Nothing OrElse ids.Count = 0 Then Return
                                                      lista.abbandona()
                                                      IniziaSalvataggio(file.Where(Function(f) ids.Contains(f)).ToList())
                                                  End Sub
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      If String.IsNullOrEmpty(id) Then Return
                                                      lista.abbandona()
                                                      IniziaSalvataggio(New List(Of String)() From {id})
                                                  End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.ScegliFileDaSalvare: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Salva i file scelti. Con la cartella dell'utente si chiede solo il nome; altrimenti prima la cartella. Con un solo file di un tipo si
    ''' chiede il suo nome; con più file dello stesso tipo si chiede un nome solo e i file prendono quel nome con un numero progressivo
    ''' ("Video 2026-11-02 18.30 1", "... 2"), nell'ordine in cui sono stati registrati.
    ''' </summary>
    Private Sub IniziaSalvataggio(scelti As List(Of String))
        Try
            Dim gruppi As New Queue(Of List(Of String))()
            Dim video As List(Of String) = scelti.Where(Function(f) Not String.Equals(Path.GetExtension(f), ".jpg", StringComparison.OrdinalIgnoreCase)).ToList()
            Dim foto As List(Of String) = scelti.Where(Function(f) String.Equals(Path.GetExtension(f), ".jpg", StringComparison.OrdinalIgnoreCase)).ToList()
            If video.Count > 0 Then gruppi.Enqueue(video)
            If foto.Count > 0 Then gruppi.Enqueue(foto)
            If ConfigurazioneFotocamera.DoveSalvareUtente() Then
                SalvaGruppo("", gruppi)
            Else
                _selettoreCartella = New SelettoreCartellaMusica()
                _selettoreCartella.Scegli(Sub(cartella As String) AiutoMusica.SuInterfaccia(Sub() SalvaGruppo(cartella, gruppi)))
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.IniziaSalvataggio: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaGruppo(cartellaScelta As String, gruppi As Queue(Of List(Of String)))
        Try
            If gruppi.Count = 0 Then
                AiutoMusica.Annuncia("Salvataggio finito")
                Dim dopo As Action = _dopoSalvataggio
                _dopoSalvataggio = Nothing
                If dopo IsNot Nothing Then dopo()
                Return
            End If
            Dim gruppo As List(Of String) = gruppi.Dequeue()
            Dim eFoto As Boolean = String.Equals(Path.GetExtension(gruppo(0)), ".jpg", StringComparison.OrdinalIgnoreCase)
            Dim cartella As String = If(cartellaScelta <> "", cartellaScelta, ConfigurazioneFotocamera.CartellaUtente(eFoto))
            Dim predefinito As String = If(gruppo.Count = 1, Path.GetFileNameWithoutExtension(gruppo(0)), If(eFoto, "Foto ", "Video ") & DateTime.Now.ToString("yyyy-MM-dd HH.mm"))
            Dim titolo As String = If(gruppo.Count = 1, If(eFoto, "Nome della foto", "Nome del video"), If(eFoto, "Nome delle foto, con un numero in fondo", "Nome dei video, con un numero in fondo"))
            AiutoRevisione.ChiediTesto(titolo, predefinito,
                                       Sub(testo As String)
                                           Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                           If nome = "" Then nome = predefinito
                                           SpostaGruppo(gruppo, cartella, nome, cartellaScelta, gruppi)
                                       End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.SalvaGruppo: " & ex.Message)
            AiutoMusica.Messaggio("Non è stato possibile salvare: " & ex.Message)
        End Try
    End Sub

    Private Async Sub SpostaGruppo(gruppo As List(Of String), cartella As String, nome As String, cartellaScelta As String, gruppi As Queue(Of List(Of String)))
        Try
            Dim destinazioni As New List(Of String)()
            For i As Integer = 0 To gruppo.Count - 1
                Dim estensione As String = Path.GetExtension(gruppo(i))
                Dim base As String = If(gruppo.Count = 1, nome, nome & " " & (i + 1).ToString())
                Dim destinazione As String = Path.Combine(cartella, base & estensione)
                Dim n As Integer = 2
                While File.Exists(destinazione) OrElse destinazioni.Contains(destinazione)
                    destinazione = Path.Combine(cartella, base & " (" & n.ToString() & ")" & estensione)
                    n += 1
                End While
                destinazioni.Add(destinazione)
            Next
            Dim origini As List(Of String) = New List(Of String)(gruppo)
            Dim fatte As List(Of String) = New List(Of String)(destinazioni)
            Await WGProcessoInCorso.EseguiAsync("Sto salvando, attendere", Function() Task.Run(Sub()
                                                                                                  Directory.CreateDirectory(cartella)
                                                                                                  For i As Integer = 0 To origini.Count - 1
                                                                                                      Try
                                                                                                          File.Move(origini(i), fatte(i))
                                                                                                      Catch
                                                                                                          File.Copy(origini(i), fatte(i), False)
                                                                                                          File.Delete(origini(i))
                                                                                                      End Try
                                                                                                  Next
                                                                                              End Sub))
            For Each o As String In origini
                _inSospeso.Remove(o)
            Next
            Dim posizioneUltimo As Integer = origini.FindIndex(Function(o) String.Equals(o, _ultimoVideo, StringComparison.OrdinalIgnoreCase))
            If posizioneUltimo >= 0 Then _ultimoVideo = fatte(posizioneUltimo)
            AiutoMusica.AnnunciaInSottofondo(If(destinazioni.Count = 1, "Salvato in " & destinazioni(0), destinazioni.Count.ToString() & " file salvati in " & cartella))
            SalvaGruppo(cartellaScelta, gruppi)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.SpostaGruppo: " & ex.Message)
            AiutoMusica.Messaggio("Non è stato possibile salvare: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Passa alla videocamera successiva e dice il nome di quella attiva; la videocamera si riapre da sola (ControllaStato).</summary>
    Private Sub OpzionePassaFotocamera(sender As Object, e As EventArgs)
        Dim camere As List(Of String) = _camere
        If camere Is Nothing OrElse camere.Count < 2 Then Return
        Dim indice As Integer = camere.FindIndex(Function(c) String.Equals(c, ConfigurazioneFotocamera.NomeCamera(), StringComparison.OrdinalIgnoreCase))
        Dim prossima As String = camere((indice + 1) Mod camere.Count)
        ' cambiando videocamera le misure e i controlli della vecchia non valgono più
        ImpostazioniMusica.Scrivi("cam_risoluzione", "")
        ImpostazioniMusica.Scrivi("voce_camera", prossima)
        AiutoMusica.Annuncia("Fotocamera " & prossima)
    End Sub

    Private Sub OpzioneOrientamento(sender As Object, e As EventArgs)
        ConfigurazioneFotocamera.ScegliOrientamentoVideo()
    End Sub

    Private Sub OpzioneRisoluzione(sender As Object, e As EventArgs)
        ConfigurazioneFotocamera.ScegliRisoluzione()
    End Sub

    Private Sub OpzioneAvanzate(sender As Object, e As EventArgs)
        ConfigurazioneFotocamera.MostraControlliAvanzati()
    End Sub

    ' ---- Spazio: guardare l'ultimo video, dentro la fotocamera ---------------------------------------------------------------------
    ' Il video si vede e si sente nella stessa schermata della fotocamera (non in un'altra finestra), con le stesse opzioni di Alt. Spazio
    ' lo avvia, e poi lo mette in pausa e lo riprende; Esc lo ferma e riporta all'anteprima della videocamera, che durante il video è chiusa.

    Private _lettore As LettoreVideoInterno
    Private _inRiproduzione As Boolean
    Private _inPausa As Boolean
    Private _posizionePausa As Double
    Private _larghezzaVideo As Integer
    Private _altezzaVideo As Integer

    Private Sub OnSpazio(sender As Object, e As EventArgs)
        Try
            If _chiusa OrElse _occupato OrElse _inRegistrazione OrElse _aiuto Then Return
            If _inRiproduzione Then
                If _inPausa Then
                    AvviaLettore(_posizionePausa)
                Else
                    _posizionePausa = If(_lettore IsNot Nothing, _lettore.Posizione, 0.0)
                    FermaLettore()
                    _inPausa = True
                    AggiornaRiga()
                End If
                Return
            End If
            IniziaRiproduzione()
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OnSpazio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il video.")
        End Try
    End Sub

    Private Async Sub IniziaRiproduzione()
        Dim video As String = _ultimoVideo
        If video = "" OrElse Not File.Exists(video) Then
            AiutoMusica.Messaggio("Non c'è ancora nessun video registrato da guardare.")
            Return
        End If
        _occupato = True
        Dim errore As String = ""
        Try
            errore = Await WGProcessoInCorso.EseguiAsync("Sto aprendo il video, attendere",
                Function() Task.Run(Function() As String
                                        Dim durata As Double
                                        If Not LettoreVideoInterno.Informazioni(video, durata, _larghezzaVideo, _altezzaVideo) Then Return "Non è stato possibile leggere il video."
                                        FermaMotore()
                                        Return ""
                                    End Function))
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.IniziaRiproduzione: " & ex.Message)
            errore = "Non è stato possibile aprire il video."
        End Try
        If _chiusa Then Return
        If errore <> "" Then
            _occupato = False
            AiutoMusica.Messaggio(errore)
            Return
        End If
        _inRiproduzione = True
        _inPausa = False
        AvviaLettore(0.0)
        _occupato = False
        AggiornaRiga()
    End Sub

    Private Sub AvviaLettore(daSecondi As Double)
        FermaLettore()
        _inPausa = False
        Dim lettore As New LettoreVideoInterno()
        AddHandler lettore.Fotogramma, Sub(img As Drawing.Bitmap)
                                           Dim lista As WGMidiRiproduzione = _lista
                                           If lista Is Nothing OrElse _chiusa OrElse Not Object.ReferenceEquals(_lettore, lettore) Then
                                               img.Dispose()
                                           Else
                                               lista.ImpostaAnteprima(img)
                                           End If
                                       End Sub
        AddHandler lettore.Finito, Sub()
                                       AiutoMusica.SuInterfaccia(Sub()
                                                                     If Not _chiusa AndAlso _inRiproduzione AndAlso Object.ReferenceEquals(_lettore, lettore) Then FineRiproduzione()
                                                                 End Sub)
                                   End Sub
        _lettore = lettore
        Dim errore As String = lettore.Avvia(_ultimoVideo, daSecondi, _larghezzaVideo, _altezzaVideo)
        If errore <> "" Then
            AiutoMusica.Messaggio(errore)
            FineRiproduzione()
        End If
    End Sub

    Private Sub OpzioneDaCapo(sender As Object, e As EventArgs)
        If _inRiproduzione Then AvviaLettore(0.0)
    End Sub

    Private Sub FermaLettore()
        Dim l As LettoreVideoInterno = _lettore
        _lettore = Nothing
        If l IsNot Nothing Then l.Ferma()
    End Sub

    ''' <summary>Il video finisce (da solo o con Esc): si torna all'anteprima della videocamera.</summary>
    Private Async Sub FineRiproduzione()
        If Not _inRiproduzione Then Return
        FermaLettore()
        _inRiproduzione = False
        _inPausa = False
        _occupato = True
        Try
            Dim errore As String = Await AvviaMotoreAsync(False)
            If errore <> "" Then AiutoMusica.Messaggio(errore)
        Finally
            _occupato = False
        End Try
        If Not _chiusa Then
            AggiornaRiga()
            AiutoMusica.Annuncia("Fotocamera")
        End If
    End Sub

    ' ---- l'aiuto per posizionarsi --------------------------------------------------------------------------------------------
    ' Chi non vede lo schermo non sa come sta inquadrato. Con questo aiuto l'intelligenza artificiale (Gemini, che sa guardare le immagini)
    ' guarda l'inquadratura di adesso e dice come spostare la videocamera, il computer e sé stessi. Una richiesta alla volta: parte solo
    ' quando si preme Invio, così tra una prova e l'altra si ha il tempo di muoversi. Con Alt si scrive una nota per spiegare la propria
    ' situazione (per esempio "sono seduto sul letto, il computer è su uno sgabello"), che si rimanda a ogni richiesta, insieme ai consigli
    ' già dati, così l'aiuto vede che cosa è cambiato. Le richieste sono al massimo quaranta per ogni apertura dell'aiuto.

    Private Sub OpzioneAiutoPosizione(sender As Object, e As EventArgs)
        Try
            If _chiusa OrElse _lista Is Nothing Then Return
            If Not WGAi.ChiaveApiConfigurata(WGAi.ProviderAi.Gemini) Then
                AiutoMusica.Messaggio("L'aiuto per posizionarti usa Gemini, che sa guardare le immagini, e manca la sua chiave: si inserisce tra le impostazioni dell'intelligenza artificiale.")
                Return
            End If
            _aiuto = True
            _aiutoInCorso = False
            _aiutoRichieste = 0
            _aiutoNota = ""
            _aiutoConsigli.Clear()
            _lista.AltSinistroDiretto = AddressOf ScriviNotaAiuto
            AggiornaRiga()
            AiutoMusica.Annuncia("Aiuto posizione")
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OpzioneAiutoPosizione: " & ex.Message)
            _aiuto = False
        End Try
    End Sub

    Private Sub EsciDaAiuto()
        _aiuto = False
        _aiutoInCorso = False
        Try
            If _lista IsNot Nothing Then _lista.AltSinistroDiretto = Nothing
        Catch
        End Try
        AggiornaRiga()
        AiutoMusica.Annuncia("Fotocamera")
    End Sub

    Private Sub ScriviNotaAiuto()
        Try
            If _chiusa OrElse Not _aiuto OrElse _aiutoInCorso Then Return
            AiutoRevisione.ChiediTesto("Nota per l'aiuto: spiega la tua situazione", _aiutoNota,
                                       Sub(testo As String)
                                           _aiutoNota = If(testo, "").Trim()
                                           AiutoMusica.AnnunciaInSottofondo(If(_aiutoNota = "", "Nota tolta", "Nota salvata"))
                                       End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.ScriviNotaAiuto: " & ex.Message)
        End Try
    End Sub

    Private Function DomandaAiuto() As String
        Dim d As New System.Text.StringBuilder()
        d.AppendLine("Sei la guida di una persona cieca che sta per usare la videocamera del suo computer per fare " & If(Foto(), "una foto", "un video") & ". Non vede lo schermo, quindi tutto quello che dici lo ascolta con una sintesi vocale.")
        d.AppendLine("L'immagine allegata è quello che la videocamera inquadra adesso, in formato " & If(ConfigurazioneFotocamera.Verticale(), "verticale", "orizzontale") & ".")
        d.AppendLine("Dimmi in italiano, in modo breve e concreto, che cosa deve fare per stare bene inquadrata: se si vede o no, se è al centro, quanto spazio c'è sopra la testa, se la videocamera è troppo alta o troppo bassa o inclinata, se è troppo vicina o lontana, se la luce è buona o c'è una finestra o una lampada alle spalle, se lo sfondo è in ordine.")
        d.AppendLine("Dai al massimo tre o quattro istruzioni, in ordine di importanza, con misure facili da eseguire a occhi chiusi (per esempio: sposta il computer di una spanna a sinistra, inclina lo schermo un poco all'indietro, avvicinati di un braccio teso, ruota la sedia verso la finestra). Parla sempre dal punto di vista della persona, non dell'immagine: destra e sinistra sono le sue.")
        d.AppendLine("Se la posizione è già buona, dillo con la frase ""Posizione ottima"" e non aggiungere altro che un eventuale piccolo ritocco. Non usare elenchi puntati, simboli o formattazione, solo frasi semplici.")
        If _aiutoNota <> "" Then d.AppendLine("La persona spiega la sua situazione così: " & _aiutoNota)
        If _aiutoConsigli.Count > 0 Then
            d.AppendLine("Finora hai già dato questi consigli, dal più vecchio al più recente: ")
            For i As Integer = Math.Max(0, _aiutoConsigli.Count - 4) To _aiutoConsigli.Count - 1
                d.AppendLine((i + 1).ToString() & ". " & _aiutoConsigli(i))
            Next
            d.AppendLine("Guarda che cosa è cambiato rispetto a quei consigli e di' se la persona ha fatto progressi, senza ripetere quello che ormai va bene.")
        End If
        Return d.ToString()
    End Function

    Private Async Sub ChiediConsiglio()
        If _aiutoInCorso OrElse _chiusa OrElse Not _aiuto Then Return
        If _aiutoRichieste >= RichiesteMassimeAiuto Then
            AiutoMusica.Messaggio("Hai usato tutte le " & RichiesteMassimeAiuto.ToString() & " richieste dell'aiuto. Esci con Esc e riaprilo dalle opzioni per ricominciare.")
            Return
        End If
        _aiutoInCorso = True
        _occupato = True
        Dim percorso As String = Path.Combine(_cartellaLavoro, "aiuto-" & Guid.NewGuid().ToString("N") & ".jpg")
        Try
            Dim lista As WGMidiRiproduzione = _lista
            ' se la videocamera è appena stata aperta l'immagine può essere ancora scura: si aspetta che l'esposizione si assesti
            Await Task.Run(Sub() AspettaEsposizioneStabile(3000))
            Dim immagine As Drawing.Bitmap = If(lista Is Nothing, Nothing, lista.CopiaAnteprima())
            If immagine Is Nothing Then
                AiutoMusica.Messaggio("La videocamera non ha ancora dato un'immagine: aspetta un momento e riprova.")
                Return
            End If
            Using immagine
                Dim codec As Drawing.Imaging.ImageCodecInfo = Drawing.Imaging.ImageCodecInfo.GetImageEncoders().FirstOrDefault(Function(c) c.MimeType = "image/jpeg")
                Using parametri As New Drawing.Imaging.EncoderParameters(1)
                    parametri.Param(0) = New Drawing.Imaging.EncoderParameter(Drawing.Imaging.Encoder.Quality, 85L)
                    immagine.Save(percorso, codec, parametri)
                End Using
            End Using
            Dim domanda As String = DomandaAiuto()
            _aiutoRichieste += 1
            Dim risposta As Tuple(Of String, String) = Await WGProcessoInCorso.EseguiAsync("Sto guardando l'inquadratura, attendere",
                Function() WGAi.OttieniRispostaAsync(domanda, New List(Of String)() From {percorso}, WGAi.ProviderAi.Gemini))
            If _chiusa Then Return
            If risposta Is Nothing OrElse String.IsNullOrWhiteSpace(risposta.Item1) Then
                AiutoMusica.Messaggio("Non ho avuto risposta dall'intelligenza artificiale" & If(risposta IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risposta.Item2), ": " & risposta.Item2, "") & ".")
            Else
                Dim consiglio As String = risposta.Item1.Trim()
                _aiutoConsigli.Add(consiglio)
                Dim resto As Integer = RichiesteMassimeAiuto - _aiutoRichieste
                AiutoMusica.Messaggio(consiglio & vbCrLf & vbCrLf & "Richieste rimaste: " & resto.ToString() & ".")
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.ChiediConsiglio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile chiedere il consiglio: " & ex.Message)
        Finally
            Try
                If File.Exists(percorso) Then File.Delete(percorso)
            Catch
            End Try
            _aiutoInCorso = False
            _occupato = False
            If Not _chiusa Then AggiornaRiga()
        End Try
    End Sub

    ' ---- uscita -----------------------------------------------------------------------------------------------------------

    Private Sub OnEsc(sender As Object, e As EventArgs)
        Try
            If _occupato Then Return
            If _inRiproduzione Then
                FineRiproduzione()
                Return
            End If
            If _aiuto Then
                EsciDaAiuto()
                Return
            End If
            If _inRegistrazione Then
                ' la registrazione in corso non si perde: si ferma e il video si prepara
                TerminaRegistrazione()
                Return
            End If
            Dim nonSalvati As Integer = FileInSospeso().Count
            If nonSalvati > 0 Then
                ' all'uscita si chiede se salvare quello che si è registrato o scattato: sì lo salva e poi esce, no lo butta
                Dim solo As String = FileInSospeso()(0)
                Dim eFoto As Boolean = String.Equals(Path.GetExtension(solo), ".jpg", StringComparison.OrdinalIgnoreCase)
                Dim testo As String = If(nonSalvati = 1, If(eFoto, "Vuoi salvare la foto?", "Vuoi salvare il video?"), "Vuoi salvare i " & nonSalvati.ToString() & " file registrati?")
                WGDomanda.Chiedi(testo,
                                 Sub()
                                     _dopoSalvataggio = AddressOf Chiudi
                                     IniziaSalvataggio(New List(Of String)(FileInSospeso()))
                                 End Sub,
                                 Sub()
                                     ScartaInSospeso()
                                     Chiudi()
                                 End Sub)
                Return
            End If
            Chiudi()
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.OnEsc: " & ex.Message)
            Chiudi()
        End Try
    End Sub

    Public Sub Chiudi()
        If _chiusa Then Return
        _chiusa = True
        Try
            If _timerFuoco IsNot Nothing Then _timerFuoco.Dispose()
            _timerFuoco = Nothing
            FermaLimite()
            If _inRegistrazione Then
                _inRegistrazione = False
                Try
                    If _acquisizione IsNot Nothing Then _acquisizione.Ferma()
                Catch
                End Try
            End If
            FermaLettore()
            ChiudiAudio()
            Task.Run(Sub()
                         FermaMotore()
                         Thread.Sleep(1000)
                         Try
                             If Directory.Exists(_cartellaLavoro) Then Directory.Delete(_cartellaLavoro, True)
                         Catch
                         End Try
                     End Sub)
            If _lista IsNot Nothing Then
                Try
                    RemoveHandler _lista.escPremuto, AddressOf OnEsc
                    RemoveHandler _lista.ElementoSelezionato, AddressOf OnInvio
                    RemoveHandler _lista.SpazioPremuto, AddressOf OnSpazio
                    _lista.AltSinistroDiretto = Nothing
                    _lista.AnteprimaAttiva = False
                    _lista.abbandona()
                Catch ex As Exception
                    _log.ScriviLog("SessioneFotocamera.Chiudi (lista): " & ex.Message)
                End Try
                _lista = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneFotocamera.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub

End Class
