Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Integrazione del sintetizzatore vocale eSpeak per la lettura dello schermo e la conversione dei testi in file audio MP3.
Partial Public Class sintesivocale

    Private _espeakAttivo As Boolean = False
    Private _motoreEspeak As WGMotoreEspeak
    Private ReadOnly _bloccoMotoreVocale As New Object()
    ' Stop e nuova frase non si devono incrociare: altrimenti lo stop cancella la frase appena partita.
    Private ReadOnly _bloccoEspeakComandi As New Object()
    Private _eventiEspeakCollegati As Boolean = False
    ' Vero se la lettura in corso vuole anche la posizione delle parole (OnParolaLetta).
    Private _tracciaParoleEspeak As Boolean = False

    Public ReadOnly Property UsaEspeak As Boolean
        Get
            Try
                Return _espeakAttivo
            Catch ex As Exception
                log.ScriviLog("UsaEspeak: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Function CanaleSapiScrivibile() As Boolean
        Try
            If _espeakAttivo Then Return _motoreEspeak IsNot Nothing
            If sapiTransport = "TCP" OrElse sapiTransport = "PIPE" Then Return sapiWriter IsNot Nothing
            If sapiTransport = "SHM" Then Return sapiShmAccessor IsNot Nothing
            Return False
        Catch ex As Exception
            log.ScriviLog("CanaleSapiScrivibile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub ApplicaMotoreVocale()
        Try
            Task.Run(New Action(AddressOf CambiaMotoreVocale))
        Catch ex As Exception
            log.ScriviLog("ApplicaMotoreVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub CambiaMotoreVocale()
        Try
            SyncLock _bloccoMotoreVocale
                Dim usare As Boolean = WGEspeak.DaUsare()
                Dim precedente As Boolean = _espeakAttivo
                If sapiInizializzato AndAlso usare = precedente Then Return
                FermaVoce()
                ChiudiSapi()
                _sapiVoci.Clear()
                _sapiVociIndicizzate.Clear()
                _sapiAudioDevices.Clear()
                If usare <> precedente Then AzzeraVocePerNuovoMotore(usare)
                RicaricaConfigurazione()
                If modalitaCorrente = "SAPI" AndAlso Not sapiInizializzato Then InizializzaSapi()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CambiaMotoreVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraVocePerNuovoMotore(usaEspeakOra As Boolean)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(usaEspeakOra, "0", "")
            If cfg.EsisteId("SapiVoice") Then
                cfg.ModificaValore("SapiVoice", valore)
            Else
                cfg.AggiungiValore("SapiVoice", valore)
            End If
        Catch ex As Exception
            log.ScriviLog("AzzeraVocePerNuovoMotore: " & ex.Message)
        End Try
    End Sub

    Private Function ProvaAvviaEspeak() As Boolean
        Try
            If Not WGEspeak.DaUsare() Then Return False
            Dim motore As WGMotoreEspeak = WGMotoreEspeak.Istanza()
            If motore Is Nothing Then Return False
            _motoreEspeak = motore
            If Not _eventiEspeakCollegati Then
                AddHandler motore.FineMessaggio, AddressOf AlFineMessaggioEspeak
                AddHandler motore.Attivita, AddressOf AllAttivitaEspeak
                AddHandler motore.ParolaLetta, AddressOf AlParolaLettaEspeak
                _eventiEspeakCollegati = True
            End If
            _espeakAttivo = True
            sapiInizializzato = True
            SegnaleEspeak()
            CaricaVociEspeak()
            ApplicaImpostazioniSapi()
            SegnalaCanalePronto()
            Return True
        Catch ex As Exception
            log.ScriviLog("ProvaAvviaEspeak: " & ex.Message)
            _espeakAttivo = False
            Return False
        End Try
    End Function

    Private Sub ChiudiEspeak()
        Try
            If Not _espeakAttivo Then Return
            _espeakAttivo = False
            If _motoreEspeak IsNot Nothing Then _motoreEspeak.Ferma()
        Catch ex As Exception
            log.ScriviLog("ChiudiEspeak: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraComandoEspeak(comando As String)
        Try
            Dim motore As WGMotoreEspeak = _motoreEspeak
            If motore Is Nothing OrElse String.IsNullOrEmpty(comando) Then Return
            Dim parti As String() = comando.Split(New Char() {"|"c}, 6)
            Dim valore As Integer
            Select Case parti(0)
                Case "SPEAK"
                    If parti.Length < 6 Then Return
                    Dim messaggioId As Integer
                    If Not Integer.TryParse(parti(1), messaggioId) Then Return
                    motore.Accoda(messaggioId, parti(3) = "1", parti(5))
                    SegnaleEspeak()
                Case "STOP"
                    motore.Ferma()
                Case "SET_RATE"
                    If parti.Length > 1 AndAlso Integer.TryParse(parti(1).Trim(), valore) Then motore.ImpostaVelocita(valore)
                Case "SET_VOLUME"
                    If parti.Length > 1 AndAlso Integer.TryParse(parti(1).Trim(), valore) Then motore.ImpostaVolume(valore)
                Case "SET_VOICE"
                    If parti.Length > 1 Then motore.ImpostaVoce(IndiceVoceEspeak(parti(1)))
                Case "GET_VOICES"
                    CaricaVociEspeak()
                Case "GET_AUDIO_DEVICES"
                    _sapiAudioDevices("PREDEFINITO") = "Dispositivo predefinito di sistema"
                Case "PING"
                    SegnaleEspeak()
            End Select
        Catch ex As Exception
            log.ScriviLog("ElaboraComandoEspeak: " & ex.Message)
        End Try
    End Sub

    Private Function IndiceVoceEspeak(valore As String) As String
        Try
            Dim pulito As String = If(valore, "").Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            Dim numero As Integer
            If Integer.TryParse(pulito, numero) Then Return numero.ToString()
            For Each coppia As KeyValuePair(Of String, String) In _sapiVociIndicizzate
                If String.Equals(coppia.Value, pulito, StringComparison.OrdinalIgnoreCase) Then Return coppia.Key
            Next
            Return "0"
        Catch ex As Exception
            log.ScriviLog("IndiceVoceEspeak: " & ex.Message)
            Return "0"
        End Try
    End Function

    Private Sub CaricaVociEspeak()
        Try
            Dim motore As WGMotoreEspeak = _motoreEspeak
            If motore Is Nothing Then Return
            Dim voci As List(Of VoceEspeak) = motore.Voci()
            _sapiVoci.Clear()
            _sapiVociIndicizzate.Clear()
            For indice As Integer = 0 To voci.Count - 1
                Dim nome As String = voci(indice).Nome
                If String.IsNullOrWhiteSpace(nome) Then Continue For
                _sapiVoci(nome) = nome
                _sapiVociIndicizzate(indice.ToString()) = nome
            Next
            ApplicaVocePredefinitaSeNecessario()
        Catch ex As Exception
            log.ScriviLog("CaricaVociEspeak: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnaleEspeak()
        Try
            SyncLock sapiSyncLock
                sapiUltimoSegnaleTick = TickCorrenti()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("SegnaleEspeak: " & ex.Message)
        End Try
    End Sub

    Private Sub AlFineMessaggioEspeak(messaggioId As Integer)
        Try
            If Not _espeakAttivo Then Return
            SyncLock sapiSyncLock
                sapiDoneMsgId = messaggioId
                sapiUltimoSegnaleTick = TickCorrenti()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AlFineMessaggioEspeak: " & ex.Message)
        End Try
    End Sub

    ' La frase va al motore subito, sullo stesso thread di chi ha chiesto di leggere.
    ' Il giro Task.Run più coda comandi aggiungeva il ritardo prima della prima sillaba.
    Private Sub ParlaSubitoEspeak(testo As String, tracciaParole As Boolean)
        Dim motore As WGMotoreEspeak = _motoreEspeak
        If motore Is Nothing OrElse String.IsNullOrWhiteSpace(testo) Then Return
        Dim myMsgId As Integer
        SyncLock _bloccoEspeakComandi
            myMsgId = Interlocked.Increment(currentSapiTestoId)
            _tracciaParoleEspeak = tracciaParole
            SyncLock sapiSyncLock
                sapiDoneMsgId = -1
            End SyncLock
            motore.Accoda(myMsgId, True, testo)
        End SyncLock
        Task.Run(Sub()
                     Try
                         If currentSapiTestoId <> myMsgId Then Return
                         Dim budgetMs As Integer = CInt(Math.Min(1800000L, 20000L + CLng(testo.Length) * 200L))
                         Dim arrivato As Boolean = AspettaDoneOAlive(myMsgId, budgetMs)
                         If currentSapiTestoId <> myMsgId Then Return
                         AzzeraParlato()
                         DispatchOnFineLettura()
                         If Not arrivato AndAlso Not masterCts.IsCancellationRequested Then
                             sapiInizializzato = False
                             Task.Run(Sub()
                                          Try
                                              InizializzaSapiInterno()
                                          Catch ex As Exception
                                              log.ScriviLog("ParlaSubitoEspeak (Riavvio): " & ex.Message)
                                          End Try
                                      End Sub)
                         End If
                     Catch ex As Exception
                         log.ScriviLog("ParlaSubitoEspeak: " & ex.Message)
                     End Try
                 End Sub, masterCts.Token)
    End Sub

    ' Il motore ha sintetizzato una parola: come gli eventi WORD del canale SAPI, la posizione
    ' (in caratteri, dall'inizio del testo dato al motore) arriva a chi segue il parlato.
    Private Sub AlParolaLettaEspeak(messaggioId As Integer, posizione As Integer, lunghezza As Integer)
        Try
            If Not _espeakAttivo Then Return
            If Not _tracciaParoleEspeak Then Return
            If messaggioId <> currentSapiTestoId Then Return
            SyncLock sapiSyncLock
                sapiUltimoSegnaleTick = TickCorrenti()
            End SyncLock
            DispatchOnParolaLetta(posizione, lunghezza)
        Catch ex As Exception
            log.ScriviLog("AlParolaLettaEspeak: " & ex.Message)
        End Try
    End Sub

    Private Sub AllAttivitaEspeak()
        Try
            If Not _espeakAttivo Then Return
            SegnaleEspeak()
        Catch ex As Exception
            log.ScriviLog("AllAttivitaEspeak: " & ex.Message)
        End Try
    End Sub

#Region "File mp3 con eSpeak"

    ' La conversione di un testo in mp3 con eSpeak: stessa interfaccia della conversione con le voci di Windows (AvviaConversioneMp3 e gli eventi
    ' OnProgressoMp3 e OnFineConversioneMp3, che WGConversioneMp3 sorveglia), ma la sintesi la fa la libreria di eSpeak dentro WinGabriel e l'mp3
    ' lo crea Windows (Media Foundation), con ffmpeg come ripiego. I pezzi di testo si sintetizzano a poco a poco, lasciando spazio al parlato.
    Private Const MASSIMO_CARATTERI_PER_SINTESI_ESPEAK As Integer = 1800
    Private Const BITRATE_MP3_ESPEAK As Integer = 64000
    Private Const FREQUENZA_MP3_ESPEAK As Integer = 44100
    Private ReadOnly _lavoriMp3Espeak As New Dictionary(Of String, CancellationTokenSource)()
    Private _anteprimaMp3Espeak As System.Media.SoundPlayer
    Private _annullaAnteprimaMp3Espeak As CancellationTokenSource

    Private Function AvviaConversioneMp3Espeak(idLavoro As String, voce As String, velocita As Integer, volume As Integer, percorsoMp3 As String, pezzi As List(Of String)) As Boolean
        Try
            If String.IsNullOrWhiteSpace(idLavoro) OrElse String.IsNullOrWhiteSpace(percorsoMp3) OrElse pezzi Is Nothing OrElse pezzi.Count = 0 Then
                log.ScriviLog("AvviaConversioneMp3Espeak: dati mancanti")
                Return False
            End If
            Dim motore As WGMotoreEspeak = _motoreEspeak
            If motore Is Nothing Then
                log.ScriviLog("AvviaConversioneMp3Espeak: motore eSpeak non pronto")
                Return False
            End If

            Dim token As New CancellationTokenSource()
            SyncLock _lavoriMp3Espeak
                _lavoriMp3Espeak(idLavoro) = token
            End SyncLock

            Dim copiaPezzi As New List(Of String)(pezzi)
            Task.Run(Sub() EseguiConversioneMp3Espeak(motore, idLavoro, voce, velocita, volume, percorsoMp3, copiaPezzi, token))
            Return True
        Catch ex As Exception
            log.ScriviLog("AvviaConversioneMp3Espeak: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function IdentificatoreVoceEspeak(motore As WGMotoreEspeak, voce As String) As String
        Try
            Dim voci As List(Of VoceEspeak) = motore.Voci()
            If voci.Count = 0 Then Return "it"
            Dim richiesta As String = If(voce, "").Trim()
            If richiesta = "" Then richiesta = If(New WGConfig().LeggiValore("SapiVoice"), "").Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            Dim numero As Integer
            If Integer.TryParse(IndiceVoceEspeak(richiesta), numero) AndAlso numero >= 0 AndAlso numero < voci.Count Then Return voci(numero).Identificatore
        Catch ex As Exception
            log.ScriviLog("IdentificatoreVoceEspeak: " & ex.Message)
        End Try
        Return "it"
    End Function

    ' Divide un pezzo in blocchi di una certa lunghezza, andando a capo dove finisce una frase: tra un blocco e l'altro il parlato può usare il motore.
    Private Shared Function DividiPerSintesi(testo As String) As List(Of String)
        Dim blocchi As New List(Of String)()
        Dim resto As String = If(testo, "").Trim()
        While resto.Length > 0
            If resto.Length <= MASSIMO_CARATTERI_PER_SINTESI_ESPEAK Then
                blocchi.Add(resto)
                Exit While
            End If
            Dim taglio As Integer = -1
            For i As Integer = MASSIMO_CARATTERI_PER_SINTESI_ESPEAK - 1 To MASSIMO_CARATTERI_PER_SINTESI_ESPEAK \ 2 Step -1
                Dim c As Char = resto(i)
                If c = "."c OrElse c = "!"c OrElse c = "?"c OrElse c = ";"c OrElse c = ":"c Then
                    taglio = i + 1
                    Exit For
                End If
            Next
            If taglio < 0 Then taglio = resto.LastIndexOf(" "c, MASSIMO_CARATTERI_PER_SINTESI_ESPEAK - 1)
            If taglio <= 0 Then taglio = MASSIMO_CARATTERI_PER_SINTESI_ESPEAK
            blocchi.Add(resto.Substring(0, taglio).Trim())
            resto = resto.Substring(taglio).Trim()
        End While
        Return blocchi
    End Function

    Private Sub EseguiConversioneMp3Espeak(motore As WGMotoreEspeak, idLavoro As String, voce As String, velocita As Integer, volume As Integer, percorsoMp3 As String, pezzi As List(Of String), token As CancellationTokenSource)
        Dim percorsoWav As String = Path.Combine(Path.GetTempPath(), "WinGabriel-espeak-" & idLavoro & ".wav")
        Dim esito As Boolean = False
        Dim messaggio As String = ""
        Dim cuore As System.Threading.Timer = Nothing
        Try
            DispatchOnProgressoMp3(idLavoro, 0)

            Dim identificatore As String = IdentificatoreVoceEspeak(motore, voce)
            Dim parole As Integer = WGMotoreEspeak.ParoleDaVelocita(velocita)
            Dim ampiezza As Integer = WGMotoreEspeak.AmpiezzaDaVolume(volume)
            Dim frequenza As Integer = motore.Frequenza

            Dim blocchi As New List(Of String)()
            For Each pezzo As String In pezzi
                blocchi.AddRange(DividiPerSintesi(pezzo))
            Next
            Dim caratteriTotali As Long = 0
            For Each b As String In blocchi
                caratteriTotali += b.Length
            Next
            If blocchi.Count = 0 OrElse caratteriTotali = 0 Then
                messaggio = "nessun testo da convertire"
            Else
                Dim annullato As Func(Of Boolean) = Function() token.IsCancellationRequested
                Using wav As New FileStream(percorsoWav, FileMode.Create, FileAccess.Write, FileShare.Read)
                    ' intestazione WAV provvisoria, completata alla fine quando si conosce la lunghezza
                    wav.Write(New Byte(43) {}, 0, 44)
                    Dim fatti As Long = 0
                    For Each blocco As String In blocchi
                        If token.IsCancellationRequested Then Exit For
                        If Not motore.SintetizzaSuFlusso(blocco, identificatore, parole, ampiezza, wav, annullato) Then
                            If Not token.IsCancellationRequested Then messaggio = "la sintesi eSpeak non è riuscita"
                            Exit For
                        End If
                        fatti += blocco.Length
                        DispatchOnProgressoMp3(idLavoro, CInt(fatti * 85L \ caratteriTotali))
                    Next
                    ScriviIntestazioneWav(wav, frequenza)
                End Using

                If token.IsCancellationRequested Then
                    messaggio = "conversione annullata"
                ElseIf messaggio = "" Then
                    ' mentre Windows crea l'mp3 (una chiamata sola) si segnala ogni secondo che il lavoro va avanti
                    cuore = New System.Threading.Timer(Sub(stato) DispatchOnProgressoMp3(idLavoro, 90), Nothing, 1000, 1000)
                    Dim erroreMp3 As String = ""
                    If CodificaMp3Espeak(percorsoWav, percorsoMp3, erroreMp3) Then
                        esito = True
                    Else
                        messaggio = erroreMp3
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("EseguiConversioneMp3Espeak: " & ex.ToString())
            messaggio = ex.Message
        Finally
            Try
                If cuore IsNot Nothing Then cuore.Dispose()
            Catch
            End Try
            Try
                SyncLock _lavoriMp3Espeak
                    _lavoriMp3Espeak.Remove(idLavoro)
                End SyncLock
                token.Dispose()
            Catch
            End Try
            Try
                If File.Exists(percorsoWav) Then File.Delete(percorsoWav)
            Catch
            End Try
        End Try

        If Not esito Then
            Try
                If File.Exists(percorsoMp3) Then File.Delete(percorsoMp3)
            Catch
            End Try
        Else
            DispatchOnProgressoMp3(idLavoro, 100)
        End If
        DispatchOnFineConversioneMp3(idLavoro, esito, messaggio)
    End Sub

    Private Shared Sub ScriviIntestazioneWav(wav As FileStream, frequenza As Integer)
        Dim lunghezzaDati As Long = wav.Length - 44
        wav.Seek(0, SeekOrigin.Begin)
        Using scrittore As New BinaryWriter(wav, System.Text.Encoding.ASCII, True)
            scrittore.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"))
            scrittore.Write(CInt(36 + lunghezzaDati))
            scrittore.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "))
            scrittore.Write(16)
            scrittore.Write(CShort(1))
            scrittore.Write(CShort(1))
            scrittore.Write(frequenza)
            scrittore.Write(frequenza * 2)
            scrittore.Write(CShort(2))
            scrittore.Write(CShort(16))
            scrittore.Write(System.Text.Encoding.ASCII.GetBytes("data"))
            scrittore.Write(CInt(lunghezzaDati))
        End Using
        wav.Flush()
    End Sub

    ' Dal wav di eSpeak (22050 Hz, mono) all'mp3: portato a 44100 Hz perché il codificatore di Windows non accetta altre frequenze.
    Private Function CodificaMp3Espeak(percorsoWav As String, percorsoMp3 As String, ByRef errore As String) As Boolean
        errore = ""
        Dim erroreWindows As String = ""
        Try
            NAudio.MediaFoundation.MediaFoundationApi.Startup()
            Using lettore As New NAudio.Wave.WaveFileReader(percorsoWav)
                Dim campioni As NAudio.Wave.ISampleProvider = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(lettore)
                Dim ricampionato As New NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(campioni, FREQUENZA_MP3_ESPEAK)
                Dim pcm As NAudio.Wave.IWaveProvider = New NAudio.Wave.SampleProviders.SampleToWaveProvider16(ricampionato)
                NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(pcm, percorsoMp3, BITRATE_MP3_ESPEAK)
            End Using
            If File.Exists(percorsoMp3) AndAlso New FileInfo(percorsoMp3).Length > 1000 Then Return True
            erroreWindows = "il file mp3 non è stato creato"
        Catch ex As Exception
            erroreWindows = ex.Message
            log.ScriviLog("CodificaMp3Espeak (Windows): " & ex.Message)
            Try
                If File.Exists(percorsoMp3) Then File.Delete(percorsoMp3)
            Catch
            End Try
        End Try

        Try
            Dim ffmpeg As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            If File.Exists(ffmpeg) Then
                Dim info As New ProcessStartInfo(ffmpeg, "-y -loglevel error -i """ & percorsoWav & """ -ar 44100 -c:a libmp3lame -b:a 64k """ & percorsoMp3 & """")
                info.UseShellExecute = False
                info.CreateNoWindow = True
                Using p As Process = Process.Start(info)
                    p.WaitForExit()
                    If p.ExitCode = 0 AndAlso File.Exists(percorsoMp3) Then Return True
                End Using
            End If
        Catch ex As Exception
            erroreWindows &= "; ffmpeg: " & ex.Message
        End Try

        errore = "non è stato possibile creare l'mp3 (" & erroreWindows & ")"
        Return False
    End Function

    Private Function AnnullaConversioneMp3Espeak(idLavoro As String) As Boolean
        Try
            Dim token As CancellationTokenSource = Nothing
            SyncLock _lavoriMp3Espeak
                If Not _lavoriMp3Espeak.TryGetValue(idLavoro, token) Then Return False
            End SyncLock
            Try
                token.Cancel()
            Catch exAnnulla As ObjectDisposedException
            End Try
            Return True
        Catch ex As Exception
            log.ScriviLog("AnnullaConversioneMp3Espeak: " & ex.Message)
            Return False
        End Try
    End Function

    ' L'anteprima della voce nelle impostazioni dell'mp3: si sintetizza una frase con la voce, la velocità e il volume scelti e la si suona.
    Private Sub AnteprimaVoceMp3Espeak(voce As String, velocita As Integer, volume As Integer, testo As String)
        Try
            Dim motore As WGMotoreEspeak = _motoreEspeak
            If motore Is Nothing OrElse String.IsNullOrWhiteSpace(testo) Then Return
            FermaAnteprimaVoceMp3Espeak()
            Dim token As New CancellationTokenSource()
            _annullaAnteprimaMp3Espeak = token
            Task.Run(Sub()
                         Try
                             Dim memoria As New MemoryStream()
                             memoria.Write(New Byte(43) {}, 0, 44)
                             Dim identificatore As String = IdentificatoreVoceEspeak(motore, voce)
                             If Not motore.SintetizzaSuFlusso(testo, identificatore, WGMotoreEspeak.ParoleDaVelocita(velocita), WGMotoreEspeak.AmpiezzaDaVolume(volume), memoria, Function() token.IsCancellationRequested) Then Return
                             If token.IsCancellationRequested Then Return
                             Dim lunghezzaDati As Integer = CInt(memoria.Length - 44)
                             Dim frequenza As Integer = motore.Frequenza
                             Dim intestazione As New MemoryStream()
                             Using scrittore As New BinaryWriter(intestazione, System.Text.Encoding.ASCII, True)
                                 scrittore.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"))
                                 scrittore.Write(36 + lunghezzaDati)
                                 scrittore.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "))
                                 scrittore.Write(16)
                                 scrittore.Write(CShort(1))
                                 scrittore.Write(CShort(1))
                                 scrittore.Write(frequenza)
                                 scrittore.Write(frequenza * 2)
                                 scrittore.Write(CShort(2))
                                 scrittore.Write(CShort(16))
                                 scrittore.Write(System.Text.Encoding.ASCII.GetBytes("data"))
                                 scrittore.Write(lunghezzaDati)
                             End Using
                             Dim dati As Byte() = memoria.ToArray()
                             Buffer.BlockCopy(intestazione.ToArray(), 0, dati, 0, 44)
                             SyncLock _lavoriMp3Espeak
                                 If token.IsCancellationRequested Then Return
                                 _anteprimaMp3Espeak = New System.Media.SoundPlayer(New MemoryStream(dati))
                                 _anteprimaMp3Espeak.Play()
                             End SyncLock
                         Catch ex As Exception
                             log.ScriviLog("AnteprimaVoceMp3Espeak (sintesi): " & ex.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("AnteprimaVoceMp3Espeak: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaAnteprimaVoceMp3Espeak()
        Try
            Dim token As CancellationTokenSource = _annullaAnteprimaMp3Espeak
            If token IsNot Nothing Then
                Try
                    token.Cancel()
                Catch exAnnulla As ObjectDisposedException
                End Try
            End If
            SyncLock _lavoriMp3Espeak
                If _anteprimaMp3Espeak IsNot Nothing Then
                    Try
                        _anteprimaMp3Espeak.Stop()
                        _anteprimaMp3Espeak.Dispose()
                    Catch exStop As Exception
                        log.ScriviLog("FermaAnteprimaVoceMp3Espeak (stop): " & exStop.Message)
                    End Try
                    _anteprimaMp3Espeak = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("FermaAnteprimaVoceMp3Espeak: " & ex.Message)
        End Try
    End Sub

#End Region

End Class