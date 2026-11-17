Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.Wave
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Sistema di dettatura vocale con supporto a provider locali e cloud, trascrizione e gestione delle chiavi API.
Public Class WGDettatura
    Implements IDisposable

    Private Shared log As New Logger()
    Private Shared _puliziaOrfaniEseguita As Boolean = False

    Private Const FrequenzaCampionamento As Integer = 16000
    Private Const NumeroCanali As Integer = 1
    Private Const BitPerCampione As Integer = 16
    Private Const NumeroMassimoTentativi As Integer = 4
    Private Const DurataMassimaRegistrazioneMs As Integer = 10 * 60 * 1000
    Private Const SogliaVolumeMicrofonoPercento As Integer = 70
    Private Const ObiettivoVolumeMicrofonoPercento As Integer = 100
    Private Const DurataSegmentoTrascrizioneSecondi As Integer = 600
    Private Const DurataMinimaSegmentoSecondi As Double = 1.0
    Private Const FinestraTaglioSegmentoSecondi As Integer = 30
    Private Const SogliaRmsTaglioSegmento As Double = 400.0
    Private Const DurataFrameAnalisiMs As Integer = 30
    Private Const FramePaddingParlato As Integer = 10
    Private Const FrameMassimiSilenzioInterno As Integer = 13
    Private Const DurataMinimaParlatoMs As Integer = 200
    Private Const LunghezzaMassimaPrompt As Integer = 600
    Private Const LunghezzaContestoPrecedente As Integer = 400
    Private Const SogliaNoSpeechProb As Double = 0.6
    Private Const SogliaAvgLogprob As Double = -1.0
    Private Const LunghezzaMassimaBloccoRipetuto As Integer = 20
    Private Const PiccoObiettivoAnalisi As Integer = 27000
    Private Const GuadagnoMassimoAnalisi As Double = 12.0
    Private Const FrequenzaTaglioLocale As Double = 90.0
    Private Const DurataBloccoLivelloLocaleMs As Integer = 20
    Private Const RmsObiettivoLocale As Double = 0.05
    Private Const RmsMinimoVoceLocale As Double = 0.004
    Private Const GuadagnoMinimoLocale As Double = 1.0
    Private Const GuadagnoMassimoLocale As Double = 4.0
    Private Const PiccoMassimoLocale As Double = 0.9

    Private Const ChiaveConfigApiGemini As String = "gemini_api_key_enc"
    Private Const ChiaveConfigApiGroq As String = "groq_api_key_enc"
    Private Const ChiaveConfigProviderPreferito As String = "dettatura_provider_preferito"
    Private Const ModelloTrascrizioneGemini As String = "gemini-3.5-flash-lite"
    Private Const RevisioneApiGemini As String = "2026-05-20"
    Private Const ModelloTrascrizioneGroq As String = "whisper-large-v3"
    Private Const ChiaveConfigVocabolario As String = "dettatura_vocabolario"
    Private Const PromptBaseItaliano As String = "Buongiorno, ti scrivo per confermare l'appuntamento di domani. Va bene alle dieci? Grazie mille, a presto."

    Private Const PassphraseCifraturaChiavi As String = "WinGabriel-Dettatura-Chiavi-Locali-Rev1"

    Public Enum ProviderDettatura
        Gemini
        Groq
        Locale
    End Enum

    Private _waveIn As WaveInEvent
    Private _waveWriter As WaveFileWriter
    Private _percorsoFileTemporaneo As String
    Private _inRegistrazioneInterno As Boolean = False
    Private _disposedValue As Boolean = False
    Private _timerLimiteRegistrazione As Timer
    Private _ctsRichiesta As CancellationTokenSource

    Public Event TrascrizioneCompletata(testo As String)
    Public Event TrascrizioneFallita(messaggioErrore As String)
    Public Shared Event StatoDettaturaCambiato(attiva As Boolean)

    Private Shared _registrazioniAttive As Integer = 0
    Private _registrazioneConteggiata As Integer = 0

    Public Shared ReadOnly Property DettaturaInCorso As Boolean
        Get
            Try
                Return Interlocked.CompareExchange(_registrazioniAttive, 0, 0) > 0
            Catch ex As Exception
                log.ScriviLog($"WGDettatura.DettaturaInCorso: {ex.Message}")
                Return False
            End Try
        End Get
    End Property

    Private Sub SegnaInizioRegistrazione()
        Try
            If Interlocked.Exchange(_registrazioneConteggiata, 1) <> 0 Then Exit Sub
            If Interlocked.Increment(_registrazioniAttive) = 1 Then RaiseEvent StatoDettaturaCambiato(True)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SegnaInizioRegistrazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub SegnaFineRegistrazione()
        Try
            If Interlocked.Exchange(_registrazioneConteggiata, 0) = 0 Then Exit Sub
            Dim rimaste As Integer = Interlocked.Decrement(_registrazioniAttive)
            If rimaste < 0 Then
                Interlocked.Exchange(_registrazioniAttive, 0)
                rimaste = 0
            End If
            If rimaste = 0 Then RaiseEvent StatoDettaturaCambiato(False)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SegnaFineRegistrazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub New()
        Try
            EseguiPuliziaFileOrfaniUnaVolta()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared ReadOnly Property SistemaSupportato As Boolean
        Get
            Try
                Return Environment.Is64BitProcess
            Catch ex As Exception
                log.ScriviLog($"WGDettatura.SistemaSupportato Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return False
            End Try
        End Get
    End Property

    ' Le chiavi API si proteggono con la libreria nativa, senza password dell'utente: restano leggibili su un altro computer
    ' e passano con la configurazione esportata.
    Private Shared Function CifraTesto(testoChiaro As String) As String
        Try
            If String.IsNullOrEmpty(testoChiaro) Then Return ""
            Return WGDb.WGCifraturaNativa.ProteggiTesto("chiavi-api", testoChiaro)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CifraTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function DecifraTesto(testoCifratoBase64 As String) As String
        Try
            If String.IsNullOrWhiteSpace(testoCifratoBase64) Then Return ""
            If WGDb.WGCifraturaNativa.EProtetto(testoCifratoBase64) Then Return WGDb.WGCifraturaNativa.RivelaTesto("chiavi-api", testoCifratoBase64)

            Dim datiCompleti() As Byte = Convert.FromBase64String(testoCifratoBase64)
            If datiCompleti.Length < 32 Then Return ""

            Dim sale(15) As Byte
            Dim vettoreInizializzazione(15) As Byte
            Array.Copy(datiCompleti, 0, sale, 0, 16)
            Array.Copy(datiCompleti, 16, vettoreInizializzazione, 0, 16)

            Dim lunghezzaDatiCifrati As Integer = datiCompleti.Length - 32
            Dim datiCifrati(lunghezzaDatiCifrati - 1) As Byte
            Array.Copy(datiCompleti, 32, datiCifrati, 0, lunghezzaDatiCifrati)

            Using derivatore As New Rfc2898DeriveBytes(PassphraseCifraturaChiavi, sale, 100000, HashAlgorithmName.SHA256)
                Dim chiaveDerivata() As Byte = derivatore.GetBytes(32)

                Using aes As Aes = Aes.Create()
                    aes.Key = chiaveDerivata
                    aes.IV = vettoreInizializzazione
                    aes.Mode = CipherMode.CBC
                    aes.Padding = PaddingMode.PKCS7

                    Using decrittografo As ICryptoTransform = aes.CreateDecryptor()
                        Using memoria As New MemoryStream(datiCifrati)
                            Using flussoChiaro As New CryptoStream(memoria, decrittografo, CryptoStreamMode.Read)
                                Using lettore As New StreamReader(flussoChiaro, Encoding.UTF8)
                                    Return lettore.ReadToEnd()
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.DecifraTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Const ChiaveConfigApiGeminiVecchiaInChiaro As String = "gemini_api_key"

    Public Shared Function LeggiChiaveApiGemini() As String
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = cfg.LeggiValore(ChiaveConfigApiGemini)
            If Not String.IsNullOrWhiteSpace(valoreCifrato) Then
                Return DecifraTesto(valoreCifrato)
            End If

            Dim valoreVecchioInChiaro As String = cfg.LeggiValore(ChiaveConfigApiGeminiVecchiaInChiaro)
            If String.IsNullOrWhiteSpace(valoreVecchioInChiaro) Then Return ""

            SalvaChiaveApiGemini(valoreVecchioInChiaro)
            Try
                cfg.ModificaValore(ChiaveConfigApiGeminiVecchiaInChiaro, "")
            Catch exPulizia As Exception
                log.ScriviLog($"WGDettatura.LeggiChiaveApiGemini (pulizia vecchia chiave): {exPulizia.Message}")
            End Try

            Return valoreVecchioInChiaro
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiChiaveApiGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Sub SalvaChiaveApiGemini(chiave As String)
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = CifraTesto(chiave)
            If Not cfg.EsisteId(ChiaveConfigApiGemini) Then
                cfg.AggiungiValore(ChiaveConfigApiGemini, valoreCifrato)
            Else
                cfg.ModificaValore(ChiaveConfigApiGemini, valoreCifrato)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SalvaChiaveApiGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ChiaveApiGeminiConfigurata() As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(LeggiChiaveApiGemini())
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ChiaveApiGeminiConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function LeggiChiaveApiGroq() As String
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = cfg.LeggiValore(ChiaveConfigApiGroq)
            If String.IsNullOrWhiteSpace(valoreCifrato) Then Return ""
            Return DecifraTesto(valoreCifrato)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiChiaveApiGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Sub SalvaChiaveApiGroq(chiave As String)
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = CifraTesto(chiave)
            If Not cfg.EsisteId(ChiaveConfigApiGroq) Then
                cfg.AggiungiValore(ChiaveConfigApiGroq, valoreCifrato)
            Else
                cfg.ModificaValore(ChiaveConfigApiGroq, valoreCifrato)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SalvaChiaveApiGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ChiaveApiGroqConfigurata() As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(LeggiChiaveApiGroq())
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ChiaveApiGroqConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Const ChiaveConfigOvhAttivo As String = "ovh_attivo"


    ''' <summary>Vero se l'utente ha scelto di usare OVHcloud, che non richiede nessuna chiave: senza una scelta esplicita i testi non vi vengono mandati.</summary>
    Public Shared Function OvhAttivo() As Boolean
        Try
            Dim cfg As New WGConfig()
            Return String.Equals(cfg.LeggiValore(ChiaveConfigOvhAttivo), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.OvhAttivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaOvhAttivo(attivo As Boolean)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(attivo, "true", "false")
            If Not cfg.EsisteId(ChiaveConfigOvhAttivo) Then
                cfg.AggiungiValore(ChiaveConfigOvhAttivo, valore)
            Else
                cfg.ModificaValore(ChiaveConfigOvhAttivo, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ImpostaOvhAttivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ChiaveApiOvhConfigurata() As Boolean
        Try
            Return OvhAttivo()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ChiaveApiOvhConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Const ChiaveConfigHordeAttivo As String = "horde_attivo"

    ''' <summary>Vero se l'utente ha scelto di usare AI Horde, che si usa con la chiave anonima: senza una scelta esplicita i testi non vi vengono mandati.</summary>
    Public Shared Function HordeAttivo() As Boolean
        Try
            Dim cfg As New WGConfig()
            Return String.Equals(cfg.LeggiValore(ChiaveConfigHordeAttivo), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.HordeAttivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaHordeAttivo(attivo As Boolean)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(attivo, "true", "false")
            If Not cfg.EsisteId(ChiaveConfigHordeAttivo) Then
                cfg.AggiungiValore(ChiaveConfigHordeAttivo, valore)
            Else
                cfg.ModificaValore(ChiaveConfigHordeAttivo, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ImpostaHordeAttivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ChiaveApiHordeConfigurata() As Boolean
        Try
            Return HordeAttivo()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ChiaveApiHordeConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function LeggiProviderPreferito() As ProviderDettatura
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigProviderPreferito)
            If String.Equals(valore, "groq", StringComparison.OrdinalIgnoreCase) Then
                Return ProviderDettatura.Groq
            End If
            If String.Equals(valore, "locale", StringComparison.OrdinalIgnoreCase) Then
                Return ProviderDettatura.Locale
            End If
            Return ProviderDettatura.Gemini
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiProviderPreferito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ProviderDettatura.Gemini
        End Try
    End Function

    Public Shared Sub SalvaProviderPreferito(provider As ProviderDettatura)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = "gemini"
            If provider = ProviderDettatura.Groq Then valore = "groq"
            If provider = ProviderDettatura.Locale Then valore = "locale"
            If Not cfg.EsisteId(ChiaveConfigProviderPreferito) Then
                cfg.AggiungiValore(ChiaveConfigProviderPreferito, valore)
            Else
                cfg.ModificaValore(ChiaveConfigProviderPreferito, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SalvaProviderPreferito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function LeggiVocabolarioDettatura() As String
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigVocabolario)
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Return valore.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiVocabolarioDettatura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Sub SalvaVocabolarioDettatura(vocabolario As String)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(vocabolario, "").Trim()
            If Not cfg.EsisteId(ChiaveConfigVocabolario) Then
                cfg.AggiungiValore(ChiaveConfigVocabolario, valore)
            Else
                cfg.ModificaValore(ChiaveConfigVocabolario, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SalvaVocabolarioDettatura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function AlmenoUnProviderConfigurato() As Boolean
        Try
            Return AlmenoUnProviderCloudConfigurato() OrElse WGTrascrittoreLocale.ModelloUtilizzabile()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AlmenoUnProviderConfigurato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function AlmenoUnProviderCloudConfigurato() As Boolean
        Try
            Return ChiaveApiGeminiConfigurata() OrElse ChiaveApiGroqConfigurata()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AlmenoUnProviderCloudConfigurato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function ProviderDisponibile(provider As ProviderDettatura) As Boolean
        Try
            Select Case provider
                Case ProviderDettatura.Gemini
                    Return ChiaveApiGeminiConfigurata()
                Case ProviderDettatura.Groq
                    Return ChiaveApiGroqConfigurata()
                Case Else
                    Return WGTrascrittoreLocale.ModelloUtilizzabile()
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ProviderDisponibile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function OrdineProviderDaProvare() As List(Of ProviderDettatura)
        Try
            Dim elenco As New List(Of ProviderDettatura)
            Dim preferito As ProviderDettatura = LeggiProviderPreferito()
            If ProviderDisponibile(preferito) Then elenco.Add(preferito)
            For Each provider As ProviderDettatura In New ProviderDettatura() {ProviderDettatura.Gemini, ProviderDettatura.Groq, ProviderDettatura.Locale}
                If provider <> preferito AndAlso ProviderDisponibile(provider) Then elenco.Add(provider)
            Next
            Return elenco
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.OrdineProviderDaProvare: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of ProviderDettatura)
        End Try
    End Function

    Public ReadOnly Property ModelloDisponibile As Boolean
        Get
            Try
                Return AlmenoUnProviderConfigurato()
            Catch ex As Exception
                log.ScriviLog($"WGDettatura.ModelloDisponibile Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property InRegistrazione As Boolean
        Get
            Try
                Return _inRegistrazioneInterno
            Catch ex As Exception
                log.ScriviLog($"WGDettatura.InRegistrazione Get: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return False
            End Try
        End Get
    End Property

    Private Shared Sub EseguiPuliziaFileOrfaniUnaVolta()
        Try
            If _puliziaOrfaniEseguita Then Return
            _puliziaOrfaniEseguita = True

            Dim cartellaTemp As String = Path.GetTempPath()
            If Not Directory.Exists(cartellaTemp) Then Return

            Dim fileOrfani() As String = Directory.GetFiles(cartellaTemp, "wgdettatura_*.wav")
            For Each percorsoFile As String In fileOrfani
                Try
                    Dim infoFile As New FileInfo(percorsoFile)
                    If DateTime.Now.Subtract(infoFile.LastWriteTime).TotalHours >= 1 Then
                        File.Delete(percorsoFile)
                    End If
                Catch exSingolo As Exception
                    log.ScriviLog($"WGDettatura.EseguiPuliziaFileOrfaniUnaVolta (file {percorsoFile}): {exSingolo.Message}")
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.EseguiPuliziaFileOrfaniUnaVolta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AttivaMicrofonoSeChiuso()
        Try
            WGAudio.AttivaMicrofonoPredefinitoSeChiuso()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AttivaMicrofonoSeChiuso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AumentaVolumeMicrofonoSeBasso()
        Try
            WGAudio.AumentaVolumeMicrofonoPredefinitoSeBasso(SogliaVolumeMicrofonoPercento, ObiettivoVolumeMicrofonoPercento)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AumentaVolumeMicrofonoSeBasso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub IniziaRegistrazione()
        Try
            If _inRegistrazioneInterno Then Exit Sub
            If Not SistemaSupportato Then
                RaiseEvent TrascrizioneFallita("La dettatura richiede un sistema a 64 bit")
                Exit Sub
            End If
            If Not AlmenoUnProviderConfigurato() Then
                RaiseEvent TrascrizioneFallita("Nessun motore di dettatura disponibile: imposta una chiave API o scarica il modello vocale locale dalle impostazioni di digitazione vocale")
                Exit Sub
            End If

            AttivaMicrofonoSeChiuso()
            AumentaVolumeMicrofonoSeBasso()

            _ctsRichiesta = New CancellationTokenSource()

            _percorsoFileTemporaneo = Path.Combine(Path.GetTempPath(), "wgdettatura_" & Guid.NewGuid().ToString("N") & ".wav")

            _waveIn = New WaveInEvent()
            _waveIn.DeviceNumber = -1
            _waveIn.WaveFormat = New WaveFormat(FrequenzaCampionamento, BitPerCampione, NumeroCanali)
            AddHandler _waveIn.DataAvailable, AddressOf OnDatiAudioDisponibili
            AddHandler _waveIn.RecordingStopped, AddressOf OnRegistrazioneFermata

            _waveWriter = New WaveFileWriter(_percorsoFileTemporaneo, _waveIn.WaveFormat)

            SegnaInizioRegistrazione()
            _waveIn.StartRecording()
            _inRegistrazioneInterno = True

            _timerLimiteRegistrazione = New Timer(AddressOf OnLimiteDurataRaggiunto, Nothing, DurataMassimaRegistrazioneMs, Timeout.Infinite)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.IniziaRegistrazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            RaiseEvent TrascrizioneFallita("Impossibile avviare la registrazione: " & ex.Message)
            _inRegistrazioneInterno = False
            SegnaFineRegistrazione()
        End Try
    End Sub

    Private Sub OnLimiteDurataRaggiunto(stato As Object)
        Try
            FermaRegistrazione()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.OnLimiteDurataRaggiunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub FermaTimerLimiteRegistrazione()
        Try
            If _timerLimiteRegistrazione IsNot Nothing Then
                _timerLimiteRegistrazione.Dispose()
                _timerLimiteRegistrazione = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.FermaTimerLimiteRegistrazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub FermaRegistrazione()
        Try
            If Not _inRegistrazioneInterno Then Exit Sub
            If _waveIn IsNot Nothing Then
                _waveIn.StopRecording()
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.FermaRegistrazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            RaiseEvent TrascrizioneFallita("Errore durante l'arresto della registrazione: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla()
        Try
            _inRegistrazioneInterno = False
            SegnaFineRegistrazione()
            FermaTimerLimiteRegistrazione()
            Try
                If _ctsRichiesta IsNot Nothing Then _ctsRichiesta.Cancel()
            Catch exCancellazione As Exception
                log.ScriviLog($"WGDettatura.Annulla (cancellazione richiesta): {exCancellazione.Message}")
            End Try
            If _waveIn IsNot Nothing Then
                RemoveHandler _waveIn.DataAvailable, AddressOf OnDatiAudioDisponibili
                RemoveHandler _waveIn.RecordingStopped, AddressOf OnRegistrazioneFermata
                _waveIn.Dispose()
                _waveIn = Nothing
            End If
            If _waveWriter IsNot Nothing Then
                _waveWriter.Dispose()
                _waveWriter = Nothing
            End If
            If Not String.IsNullOrEmpty(_percorsoFileTemporaneo) AndAlso File.Exists(_percorsoFileTemporaneo) Then
                File.Delete(_percorsoFileTemporaneo)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.Annulla: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnDatiAudioDisponibili(sender As Object, e As WaveInEventArgs)
        Try
            If _waveWriter IsNot Nothing Then
                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.OnDatiAudioDisponibili: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub OnRegistrazioneFermata(sender As Object, e As StoppedEventArgs)
        Try
            _inRegistrazioneInterno = False
            SegnaFineRegistrazione()
            FermaTimerLimiteRegistrazione()

            If _waveWriter IsNot Nothing Then
                _waveWriter.Dispose()
                _waveWriter = Nothing
            End If
            If _waveIn IsNot Nothing Then
                RemoveHandler _waveIn.DataAvailable, AddressOf OnDatiAudioDisponibili
                RemoveHandler _waveIn.RecordingStopped, AddressOf OnRegistrazioneFermata
                _waveIn.Dispose()
                _waveIn = Nothing
            End If

            If e.Exception IsNot Nothing Then
                RaiseEvent TrascrizioneFallita("Errore del microfono: " & e.Exception.Message)
                Exit Sub
            End If

            Await TrascriviFileAsync(_percorsoFileTemporaneo, _ctsRichiesta.Token)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.OnRegistrazioneFermata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            RaiseEvent TrascrizioneFallita("Errore durante la trascrizione: " & ex.Message)
        End Try
    End Sub

    Private Async Function TrascriviFileAsync(percorsoFile As String, token As CancellationToken) As Task
        Try
            If Not File.Exists(percorsoFile) Then
                RaiseEvent TrascrizioneFallita("File audio non trovato")
                Exit Function
            End If

            If Not RiduciSilenzi(percorsoFile) Then
                Try
                    If File.Exists(percorsoFile) Then File.Delete(percorsoFile)
                Catch exPuliziaSilenzio As Exception
                    log.ScriviLog($"WGDettatura.TrascriviFileAsync (pulizia file senza parlato): {exPuliziaSilenzio.Message}{Environment.NewLine}{exPuliziaSilenzio.StackTrace}")
                End Try
                If Not token.IsCancellationRequested Then RaiseEvent TrascrizioneFallita("Nessun parlato rilevato")
                Exit Function
            End If

            If OrdineProviderDaProvare().Count = 0 Then
                RaiseEvent TrascrizioneFallita("Nessun motore di dettatura disponibile")
                Exit Function
            End If

            Dim risultatoProvider As Tuple(Of String, String) = Await TrascriviConProviderDisponibiliAsync(percorsoFile, token)
            Dim testoFinale As String = risultatoProvider.Item1
            Dim ultimoMessaggioErrore As String = If(String.IsNullOrEmpty(risultatoProvider.Item2), "Nessun testo riconosciuto", risultatoProvider.Item2)

            Try
                If File.Exists(percorsoFile) Then File.Delete(percorsoFile)
            Catch exPulizia As Exception
                log.ScriviLog($"WGDettatura.TrascriviFileAsync (pulizia file): {exPulizia.Message}{Environment.NewLine}{exPulizia.StackTrace}")
            End Try

            If token.IsCancellationRequested Then
                Exit Function
            End If

            If String.IsNullOrEmpty(testoFinale) Then
                RaiseEvent TrascrizioneFallita(ultimoMessaggioErrore)
            Else
                Dim testoPulito As String = FinalizzaTesto(testoFinale, True)
                If String.IsNullOrWhiteSpace(testoPulito) Then
                    RaiseEvent TrascrizioneFallita("Nessun testo riconosciuto")
                Else
                    RaiseEvent TrascrizioneCompletata(testoPulito)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviFileAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            If Not token.IsCancellationRequested Then
                RaiseEvent TrascrizioneFallita("Errore durante la trascrizione: " & ex.Message)
            End If
        End Try
    End Function

    Private Async Function TrascriviConProviderDisponibiliAsync(percorsoWav As String, token As CancellationToken, Optional contestoPrecedente As String = "", Optional perDettatura As Boolean = True) As Task(Of Tuple(Of String, String))
        Dim ultimoMessaggioErrore As String = Nothing
        Try
            For Each provider As ProviderDettatura In OrdineProviderDaProvare()
                If token.IsCancellationRequested Then Exit For

                Dim risultatoProvider As Tuple(Of String, String)
                If provider = ProviderDettatura.Gemini Then
                    risultatoProvider = Await TrascriviConGeminiAsync(percorsoWav, token, contestoPrecedente, perDettatura)
                ElseIf provider = ProviderDettatura.Groq Then
                    risultatoProvider = Await TrascriviConGroqAsync(percorsoWav, token, contestoPrecedente, perDettatura)
                Else
                    risultatoProvider = Await TrascriviConLocaleAsync(percorsoWav, token)
                End If

                If token.IsCancellationRequested Then Exit For

                If risultatoProvider IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultatoProvider.Item1) Then
                    Return New Tuple(Of String, String)(risultatoProvider.Item1, Nothing)
                End If

                If risultatoProvider IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultatoProvider.Item2) Then
                    ultimoMessaggioErrore = risultatoProvider.Item2
                End If
            Next
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviConProviderDisponibiliAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            ultimoMessaggioErrore = "Errore durante la trascrizione: " & ex.Message
        End Try
        Return New Tuple(Of String, String)(Nothing, ultimoMessaggioErrore)
    End Function

    Private Async Function TrascriviConLocaleAsync(percorsoWav As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Return Await Task.Factory.StartNew(Of Tuple(Of String, String))(AddressOf TrascriviLocaleDaStato, percorsoWav, token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviConLocaleAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore del riconoscimento vocale locale: " & ex.Message)
        End Try
    End Function

    Private Shared Function TrascriviLocaleDaStato(stato As Object) As Tuple(Of String, String)
        Dim percorsoPreparato As String = Nothing
        Try
            Dim percorsoOriginale As String = CStr(stato)
            percorsoPreparato = CreaCopiaPerLocale(percorsoOriginale)
            Dim percorsoDaUsare As String = If(String.IsNullOrEmpty(percorsoPreparato), percorsoOriginale, percorsoPreparato)
            Dim errore As String = ""
            Dim testo As String = WGTrascrittoreLocale.TrascriviFile(percorsoDaUsare, errore)
            If String.IsNullOrWhiteSpace(testo) Then
                Return New Tuple(Of String, String)(Nothing, If(String.IsNullOrEmpty(errore), "Nessun testo riconosciuto", errore))
            End If
            Return New Tuple(Of String, String)(testo.Trim(), Nothing)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviLocaleDaStato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore del riconoscimento vocale locale: " & ex.Message)
        Finally
            Try
                If Not String.IsNullOrEmpty(percorsoPreparato) AndAlso File.Exists(percorsoPreparato) Then File.Delete(percorsoPreparato)
            Catch exPulizia As Exception
                log.ScriviLog($"WGDettatura.TrascriviLocaleDaStato (pulizia copia preparata): {exPulizia.Message}{Environment.NewLine}{exPulizia.StackTrace}")
            End Try
        End Try
    End Function

    Private Shared Function CreaCopiaPerLocale(percorsoWav As String) As String
        Dim percorsoCopia As String = Nothing
        Try
            If String.IsNullOrEmpty(percorsoWav) OrElse Not File.Exists(percorsoWav) Then Return Nothing
            Dim formato As WaveFormat = Nothing
            Dim campioni() As Short = LeggiCampioniWav(percorsoWav, formato)
            If campioni Is Nothing OrElse campioni.Length = 0 OrElse formato Is Nothing Then Return Nothing
            If formato.BitsPerSample <> 16 OrElse formato.Channels <> 1 Then Return Nothing
            Dim preparati() As Short = PreparaCampioniPerLocale(campioni, formato.SampleRate)
            If preparati Is Nothing OrElse preparati.Length = 0 Then Return Nothing
            percorsoCopia = Path.Combine(Path.GetTempPath(), "wgdettatura_" & Guid.NewGuid().ToString("N") & ".wav")
            If Not ScriviCampioniWav(percorsoCopia, formato, preparati) Then
                Try
                    If File.Exists(percorsoCopia) Then File.Delete(percorsoCopia)
                Catch exPulizia As Exception
                    log.ScriviLog($"WGDettatura.CreaCopiaPerLocale (pulizia): {exPulizia.Message}")
                End Try
                Return Nothing
            End If
            Return percorsoCopia
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CreaCopiaPerLocale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Try
                If Not String.IsNullOrEmpty(percorsoCopia) AndAlso File.Exists(percorsoCopia) Then File.Delete(percorsoCopia)
            Catch exPulizia As Exception
                log.ScriviLog($"WGDettatura.CreaCopiaPerLocale (pulizia dopo errore): {exPulizia.Message}")
            End Try
            Return Nothing
        End Try
    End Function

    Private Shared Function PreparaCampioniPerLocale(campioni() As Short, frequenza As Integer) As Short()
        Try
            If campioni Is Nothing OrElse campioni.Length = 0 OrElse frequenza <= 0 Then Return campioni
            Dim coefficiente As Double = 1.0 / (1.0 + 2.0 * Math.PI * FrequenzaTaglioLocale / frequenza)
            Dim filtrati(campioni.Length - 1) As Double
            Dim ingressoPrecedente As Double = 0.0
            Dim uscitaPrecedente As Double = 0.0
            Dim picco As Double = 0.0
            For indice As Integer = 0 To campioni.Length - 1
                Dim ingresso As Double = campioni(indice) / 32768.0
                Dim uscita As Double = coefficiente * (uscitaPrecedente + ingresso - ingressoPrecedente)
                ingressoPrecedente = ingresso
                uscitaPrecedente = uscita
                filtrati(indice) = uscita
                If Math.Abs(uscita) > picco Then picco = Math.Abs(uscita)
            Next

            Dim campioniBlocco As Integer = Math.Max(1, frequenza * DurataBloccoLivelloLocaleMs \ 1000)
            Dim livelliBlocchi As New List(Of Double)()
            Dim posizione As Integer = 0
            While posizione < filtrati.Length
                Dim quanti As Integer = Math.Min(campioniBlocco, filtrati.Length - posizione)
                Dim somma As Double = 0.0
                For indice As Integer = posizione To posizione + quanti - 1
                    somma += filtrati(indice) * filtrati(indice)
                Next
                livelliBlocchi.Add(Math.Sqrt(somma / quanti))
                posizione += quanti
            End While
            If livelliBlocchi.Count = 0 Then Return campioni

            Dim ordinati As New List(Of Double)(livelliBlocchi)
            ordinati.Sort()
            Dim rumore As Double = ordinati(Math.Min(ordinati.Count - 1, ordinati.Count \ 10))
            Dim sogliaVoce As Double = Math.Max(rumore * 3.0, RmsMinimoVoceLocale)

            Dim sommaVoce As Double = 0.0
            Dim bloccoVoce As Integer = 0
            Dim sommaTotale As Double = 0.0
            For Each livello As Double In livelliBlocchi
                sommaTotale += livello * livello
                If livello > sogliaVoce Then
                    sommaVoce += livello * livello
                    bloccoVoce += 1
                End If
            Next
            Dim livelloVoce As Double = If(bloccoVoce > 0, Math.Sqrt(sommaVoce / bloccoVoce), Math.Sqrt(sommaTotale / livelliBlocchi.Count))
            If livelloVoce <= 0.0 OrElse picco <= 0.0 Then Return campioni

            Dim guadagno As Double = Math.Max(GuadagnoMinimoLocale, Math.Min(GuadagnoMassimoLocale, RmsObiettivoLocale / livelloVoce))
            guadagno = Math.Min(guadagno, PiccoMassimoLocale / picco)

            Dim risultato(campioni.Length - 1) As Short
            For indice As Integer = 0 To filtrati.Length - 1
                Dim valore As Double = Math.Max(-PiccoMassimoLocale, Math.Min(PiccoMassimoLocale, filtrati(indice) * guadagno))
                risultato(indice) = CShort(Math.Max(-32768.0, Math.Min(32767.0, Math.Round(valore * 32767.0))))
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.PreparaCampioniPerLocale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return campioni
        End Try
    End Function

    Public Async Function TrascriviFileConProviderAsync(percorsoWav As String, provider As ProviderDettatura, Optional token As CancellationToken = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(percorsoWav) OrElse Not File.Exists(percorsoWav) Then Return New Tuple(Of String, String)(Nothing, "File audio non trovato")
            Select Case provider
                Case ProviderDettatura.Gemini
                    Return Await TrascriviConGeminiAsync(percorsoWav, token, "", True)
                Case ProviderDettatura.Groq
                    Return Await TrascriviConGroqAsync(percorsoWav, token, "", True)
                Case Else
                    Return New Tuple(Of String, String)(Nothing, "Il servizio di trascrizione richiesto non è disponibile")
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviFileConProviderAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore di trascrizione: " & ex.Message)
        End Try
    End Function

    Public Async Function TrascriviFileAudioAsync(percorsoAudio As String, Optional progresso As Action(Of Integer, Integer) = Nothing, Optional token As CancellationToken = Nothing) As Task(Of Tuple(Of String, String))
        Dim segmenti As New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(percorsoAudio) OrElse Not File.Exists(percorsoAudio) Then
                Return New Tuple(Of String, String)(Nothing, "File audio non trovato")
            End If
            If Not AlmenoUnProviderConfigurato() Then
                Return New Tuple(Of String, String)(Nothing, "Per trascrivere i file audio serve una chiave API di Gemini o di Groq, oppure il modello vocale locale: impostali dalle impostazioni di digitazione vocale")
            End If

            Dim risultatoPreparazione As Tuple(Of List(Of String), String) = Await Task.Factory.StartNew(Of Tuple(Of List(Of String), String))(AddressOf PreparaSegmentiAudioDaStato, percorsoAudio, token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            If risultatoPreparazione.Item1 IsNot Nothing Then segmenti.AddRange(risultatoPreparazione.Item1)
            If segmenti.Count = 0 Then
                Return New Tuple(Of String, String)(Nothing, If(String.IsNullOrEmpty(risultatoPreparazione.Item2), "Il file non contiene audio da trascrivere", risultatoPreparazione.Item2))
            End If

            Dim testoCompleto As New StringBuilder()
            Dim segmentiFalliti As Integer = 0
            Dim segmentiSenzaParlato As Integer = 0
            Dim ultimoErrore As String = Nothing
            Dim ultimoTestoRiconosciuto As String = ""

            For indice As Integer = 0 To segmenti.Count - 1
                If token.IsCancellationRequested Then Exit For
                Try
                    If progresso IsNot Nothing Then progresso(indice + 1, segmenti.Count)
                Catch exProgresso As Exception
                    log.ScriviLog($"WGDettatura.TrascriviFileAudioAsync (avanzamento): {exProgresso.Message}")
                End Try

                Dim percorsoSegmento As String = segmenti(indice)

                Dim contieneParlato As Boolean = True
                Try
                    contieneParlato = RiduciSilenzi(percorsoSegmento)
                Catch exSilenzi As Exception
                    log.ScriviLog($"WGDettatura.TrascriviFileAudioAsync (riduzione silenzi): {exSilenzi.Message}{Environment.NewLine}{exSilenzi.StackTrace}")
                End Try
                If Not contieneParlato Then
                    segmentiSenzaParlato += 1
                    Continue For
                End If

                Dim contesto As String = UltimiCaratteri(ultimoTestoRiconosciuto, LunghezzaContestoPrecedente)
                Dim risultatoSegmento As Tuple(Of String, String) = Await TrascriviConProviderDisponibiliAsync(percorsoSegmento, token, contesto, False)
                If token.IsCancellationRequested Then Exit For
                If risultatoSegmento Is Nothing Then risultatoSegmento = New Tuple(Of String, String)(Nothing, "Nessun servizio ha restituito una risposta")

                If Not String.IsNullOrEmpty(risultatoSegmento.Item1) Then
                    Dim testoSegmento As String = risultatoSegmento.Item1.Trim()
                    If testoCompleto.Length > 0 Then testoCompleto.Append(" ")
                    testoCompleto.Append(testoSegmento)
                    ultimoTestoRiconosciuto = testoSegmento
                Else
                    segmentiFalliti += 1
                    If Not String.IsNullOrEmpty(risultatoSegmento.Item2) Then ultimoErrore = risultatoSegmento.Item2
                    log.ScriviLog($"WGDettatura.TrascriviFileAudioAsync: parte {indice + 1} di {segmenti.Count} di '{percorsoAudio}' non trascritta: {risultatoSegmento.Item2}")
                    If segmenti.Count > 1 Then
                        If testoCompleto.Length > 0 Then testoCompleto.AppendLine()
                        testoCompleto.AppendLine($"[Parte {indice + 1} di {segmenti.Count} non trascritta]")
                    End If
                End If
            Next

            If token.IsCancellationRequested Then
                Return New Tuple(Of String, String)(Nothing, "Trascrizione annullata")
            End If

            If segmentiSenzaParlato = segmenti.Count Then
                Return New Tuple(Of String, String)(Nothing, "Il file non contiene parlato da trascrivere")
            End If

            If segmentiFalliti + segmentiSenzaParlato = segmenti.Count Then
                Return New Tuple(Of String, String)(Nothing, If(String.IsNullOrEmpty(ultimoErrore), "Nessun testo riconosciuto", ultimoErrore))
            End If

            Dim testoFinale As String = FinalizzaTesto(testoCompleto.ToString(), False)
            If String.IsNullOrWhiteSpace(testoFinale) Then
                Return New Tuple(Of String, String)(Nothing, "Nessun testo riconosciuto")
            End If

            Return New Tuple(Of String, String)(testoFinale, Nothing)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviFileAudioAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la trascrizione: " & ex.Message)
        Finally
            For Each percorsoSegmento As String In segmenti
                Try
                    If File.Exists(percorsoSegmento) Then File.Delete(percorsoSegmento)
                Catch exPulizia As Exception
                    log.ScriviLog($"WGDettatura.TrascriviFileAudioAsync (pulizia segmento {percorsoSegmento}): {exPulizia.Message}")
                End Try
            Next
        End Try
    End Function

    Private Function PreparaSegmentiAudioDaStato(stato As Object) As Tuple(Of List(Of String), String)
        Dim segmenti As New List(Of String)()
        Dim scrittore As WaveFileWriter = Nothing
        Try
            Dim percorsoAudio As String = CStr(stato)
            Dim formatoDestinazione As New WaveFormat(FrequenzaCampionamento, BitPerCampione, NumeroCanali)
            Dim bytesPerSegmento As Long = CLng(formatoDestinazione.AverageBytesPerSecond) * DurataSegmentoTrascrizioneSecondi
            Dim bytesMinimi As Long = CLng(formatoDestinazione.AverageBytesPerSecond * DurataMinimaSegmentoSecondi)
            Dim bytesInizioFinestraTaglio As Long = bytesPerSegmento - CLng(formatoDestinazione.AverageBytesPerSecond) * FinestraTaglioSegmentoSecondi

            Using lettore As New MediaFoundationReader(percorsoAudio)
                Using convertitore As New MediaFoundationResampler(lettore, formatoDestinazione)
                    convertitore.ResamplerQuality = 60
                    Dim dimensioneBuffer As Integer = (formatoDestinazione.AverageBytesPerSecond \ 10) - ((formatoDestinazione.AverageBytesPerSecond \ 10) Mod formatoDestinazione.BlockAlign)
                    Dim buffer(dimensioneBuffer - 1) As Byte
                    Dim scrittiNelSegmento As Long = 0
                    Do
                        Dim letti As Integer = convertitore.Read(buffer, 0, buffer.Length)
                        If letti <= 0 Then Exit Do
                        If scrittore Is Nothing Then
                            Dim percorsoSegmento As String = Path.Combine(Path.GetTempPath(), "wgdettatura_" & Guid.NewGuid().ToString("N") & ".wav")
                            segmenti.Add(percorsoSegmento)
                            scrittore = New WaveFileWriter(percorsoSegmento, formatoDestinazione)
                            scrittiNelSegmento = 0
                        End If
                        scrittore.Write(buffer, 0, letti)
                        scrittiNelSegmento += letti

                        Dim tagliaQui As Boolean = False
                        If scrittiNelSegmento >= bytesPerSegmento Then
                            tagliaQui = True
                        ElseIf scrittiNelSegmento >= bytesInizioFinestraTaglio AndAlso CalcolaRmsBuffer(buffer, letti) < SogliaRmsTaglioSegmento Then
                            tagliaQui = True
                        End If

                        If tagliaQui Then
                            scrittore.Dispose()
                            scrittore = Nothing
                        End If
                    Loop
                    If scrittore IsNot Nothing Then
                        scrittore.Dispose()
                        scrittore = Nothing
                    End If
                    If segmenti.Count > 1 AndAlso scrittiNelSegmento < bytesMinimi Then
                        Dim ultimo As String = segmenti(segmenti.Count - 1)
                        segmenti.RemoveAt(segmenti.Count - 1)
                        Try
                            File.Delete(ultimo)
                        Catch exUltimo As Exception
                            log.ScriviLog($"WGDettatura.PreparaSegmentiAudioDaStato (eliminazione segmento finale): {exUltimo.Message}")
                        End Try
                    End If
                End Using
            End Using
            Return New Tuple(Of List(Of String), String)(segmenti, Nothing)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.PreparaSegmentiAudioDaStato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Try
                If scrittore IsNot Nothing Then scrittore.Dispose()
            Catch exChiusura As Exception
                log.ScriviLog($"WGDettatura.PreparaSegmentiAudioDaStato (chiusura file): {exChiusura.Message}")
            End Try
            For Each percorsoSegmento As String In segmenti
                Try
                    If File.Exists(percorsoSegmento) Then File.Delete(percorsoSegmento)
                Catch exPulizia As Exception
                    log.ScriviLog($"WGDettatura.PreparaSegmentiAudioDaStato (pulizia): {exPulizia.Message}")
                End Try
            Next
            Return New Tuple(Of List(Of String), String)(New List(Of String)(), "Impossibile leggere il file audio: formato non supportato o file danneggiato")
        End Try
    End Function

    Private Async Function TrascriviConGeminiAsync(percorsoWav As String, token As CancellationToken, Optional contestoPrecedente As String = "", Optional perDettatura As Boolean = True) As Task(Of Tuple(Of String, String))
        Dim uriFileCaricato As String = Nothing
        Dim nomeFileCaricato As String = Nothing
        Try
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, Nothing)

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(10)

                Dim risultatoCaricamento As Tuple(Of String, String) = Await CaricaFileAudioGeminiAsync(client, percorsoWav, chiave, token)
                If risultatoCaricamento Is Nothing Then
                    Return New Tuple(Of String, String)(Nothing, "Impossibile caricare l'audio su Gemini")
                End If

                uriFileCaricato = risultatoCaricamento.Item1
                nomeFileCaricato = risultatoCaricamento.Item2

                Dim risultatoTrascrizione As Tuple(Of String, String) = Await RichiediTrascrizioneGeminiAsync(client, uriFileCaricato, chiave, token, CostruisciIstruzioniGemini(contestoPrecedente, perDettatura))

                If Not String.IsNullOrEmpty(nomeFileCaricato) Then
                    EliminaFileCaricatoGeminiInBackground(nomeFileCaricato, chiave)
                End If

                Return risultatoTrascrizione
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviConGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    Private Async Function CaricaFileAudioGeminiAsync(client As HttpClient, percorsoWav As String, chiave As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim bytesAudio() As Byte = File.ReadAllBytes(percorsoWav)

            Dim urlAvvioCaricamento As String = "https://generativelanguage.googleapis.com/upload/v1beta/files"

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False

                Try
                    Using richiestaAvvio As New HttpRequestMessage(HttpMethod.Post, urlAvvioCaricamento)
                        richiestaAvvio.Headers.Add("x-goog-api-key", chiave)
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Protocol", "resumable")
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Command", "start")
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Header-Content-Length", bytesAudio.Length.ToString())
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Header-Content-Type", "audio/wav")
                        Dim corpoMetadati As New JObject From {
                            {"file", New JObject From {{"display_name", "dettatura"}}}
                        }
                        richiestaAvvio.Content = New StringContent(corpoMetadati.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")

                        Using rispostaAvvio As HttpResponseMessage = Await client.SendAsync(richiestaAvvio, token)
                        WGAi.RegistraRichiestaGemini()
                            If Not rispostaAvvio.IsSuccessStatusCode Then
                                Dim corpoErrore As String = Await rispostaAvvio.Content.ReadAsStringAsync()
                                eTemporaneo = (rispostaAvvio.StatusCode = Net.HttpStatusCode.ServiceUnavailable)
                                log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, avvio caricamento fallito, HTTP {CInt(rispostaAvvio.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpoErrore}")
                            ElseIf Not rispostaAvvio.Headers.Contains("X-Goog-Upload-URL") Then
                                log.ScriviLog("WGDettatura.CaricaFileAudioGeminiAsync: intestazione X-Goog-Upload-URL assente nella risposta di avvio")
                            Else
                                Dim urlCaricamentoBytes As String = rispostaAvvio.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault()

                                Using richiestaCaricamento As New HttpRequestMessage(HttpMethod.Post, urlCaricamentoBytes)
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Offset", "0")
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Command", "upload, finalize")
                                    richiestaCaricamento.Content = New ByteArrayContent(bytesAudio)
                                    richiestaCaricamento.Content.Headers.ContentLength = bytesAudio.Length

                                    Using rispostaCaricamento As HttpResponseMessage = Await client.SendAsync(richiestaCaricamento, token)
                                    WGAi.RegistraRichiestaGemini()
                                        Dim corpoRispostaCaricamento As String = Await rispostaCaricamento.Content.ReadAsStringAsync()

                                        If Not rispostaCaricamento.IsSuccessStatusCode Then
                                            eTemporaneo = (rispostaCaricamento.StatusCode = Net.HttpStatusCode.ServiceUnavailable)
                                            log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, caricamento byte fallito, HTTP {CInt(rispostaCaricamento.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                        Else
                                            Dim risultatoJson As JObject = JObject.Parse(corpoRispostaCaricamento)
                                            Dim gettoneUri As JToken = risultatoJson.SelectToken("file.uri")
                                            Dim gettoneNome As JToken = risultatoJson.SelectToken("file.name")

                                            If gettoneUri Is Nothing Then
                                                log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: uri del file assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                                Return Nothing
                                            End If

                                            Return New Tuple(Of String, String)(gettoneUri.ToString(), If(gettoneNome IsNot Nothing, gettoneNome.ToString(), ""))
                                        End If
                                    End Using
                                End Using
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = CInt(Math.Pow(2, tentativo))
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                Return Nothing
            Next

            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CaricaFileAudioGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Async Function RichiediTrascrizioneGeminiAsync(client As HttpClient, uriFile As String, chiave As String, token As CancellationToken, istruzioni As String) As Task(Of Tuple(Of String, String))
        Try
            Dim corpoRichiesta As New JObject From {
                {"model", ModelloTrascrizioneGemini},
                {"input", New JArray From {
                    New JObject From {
                        {"type", "text"},
                        {"text", istruzioni}
                    },
                    New JObject From {
                        {"type", "audio"},
                        {"uri", uriFile},
                        {"mime_type", "audio/wav"}
                    }
                }}
            }
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://generativelanguage.googleapis.com/v1beta/interactions"

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False
                Dim messaggioErroreTentativo As String = Nothing

                Try
                    Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                        richiesta.Headers.Add("x-goog-api-key", chiave)
                        richiesta.Headers.Add("Api-Revision", RevisioneApiGemini)
                        richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                        Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                        WGAi.RegistraRichiestaGemini()
                            Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                            If Not risposta.IsSuccessStatusCode Then
                                eTemporaneo = (risposta.StatusCode = Net.HttpStatusCode.ServiceUnavailable)
                                messaggioErroreTentativo = If(CInt(risposta.StatusCode) = 429, "Quota di Gemini esaurita", "Errore da Gemini: " & risposta.StatusCode.ToString())
                                log.ScriviLog($"WGDettatura.RichiediTrascrizioneGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                            Else
                                Dim testo As String = EstraiTestoDaRispostaInteractionGemini(corpoRisposta)
                                If testo Is Nothing Then
                                    log.ScriviLog($"WGDettatura.RichiediTrascrizioneGeminiAsync: impossibile estrarre il testo dalla risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                    Return New Tuple(Of String, String)(Nothing, Nothing)
                                End If
                                Dim testoPulito As String = PulisciRispostaGemini(testo)
                                If String.IsNullOrWhiteSpace(testoPulito) Then
                                    Return New Tuple(Of String, String)(Nothing, "Nessun parlato riconosciuto")
                                End If
                                Return New Tuple(Of String, String)(testoPulito, Nothing)
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Errore di connessione verso Gemini: " & exRete.Message
                    log.ScriviLog($"WGDettatura.RichiediTrascrizioneGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    messaggioErroreTentativo = "Timeout della richiesta verso Gemini"
                    log.ScriviLog($"WGDettatura.RichiediTrascrizioneGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = CInt(Math.Pow(2, tentativo))
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                If eTemporaneo Then
                    Return New Tuple(Of String, String)(Nothing, "Gemini è momentaneamente sovraccarico")
                End If

                Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "Gemini non ha risposto dopo diversi tentativi"))
            Next

            Return New Tuple(Of String, String)(Nothing, "Gemini non ha risposto dopo diversi tentativi")
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.RichiediTrascrizioneGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    Private Function EstraiTestoDaRispostaInteractionGemini(corpoRisposta As String) As String
        Try
            Dim risultato As JObject = JObject.Parse(corpoRisposta)

            Dim passi As JArray = TryCast(risultato("steps"), JArray)
            If passi IsNot Nothing Then
                For Each passo As JObject In passi
                    Dim tipoPasso As String = CStr(passo("type"))
                    If tipoPasso = "model_output" Then
                        Dim contenuti As JArray = TryCast(passo("content"), JArray)
                        If contenuti IsNot Nothing Then
                            For Each elemento As JObject In contenuti
                                If CStr(elemento("type")) = "text" Then
                                    Dim gettoneTesto As JToken = elemento("text")
                                    If gettoneTesto IsNot Nothing Then Return gettoneTesto.ToString()
                                End If
                            Next
                        End If
                    End If
                Next
            End If

            Dim gettoneOutputs As JToken = risultato.SelectToken("outputs[-1].text")
            If gettoneOutputs IsNot Nothing Then Return gettoneOutputs.ToString()

            Dim gettoneOutputText As JToken = risultato("output_text")
            If gettoneOutputText IsNot Nothing Then Return gettoneOutputText.ToString()

            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.EstraiTestoDaRispostaInteractionGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Async Sub EliminaFileCaricatoGeminiInBackground(nomeFile As String, chiave As String)
        Try
            If String.IsNullOrEmpty(nomeFile) OrElse String.IsNullOrEmpty(chiave) Then Return
            Using client As New HttpClient()
                Dim url As String = "https://generativelanguage.googleapis.com/v1beta/" & nomeFile
                Using richiesta As New HttpRequestMessage(HttpMethod.Delete, url)
                    richiesta.Headers.Add("x-goog-api-key", chiave)
                    Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                        WGAi.RegistraRichiestaGemini()
                        If Not risposta.IsSuccessStatusCode Then
                            Dim corpoErrore As String = Await risposta.Content.ReadAsStringAsync()
                            log.ScriviLog($"WGDettatura.EliminaFileCaricatoGeminiInBackground: eliminazione fallita, HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpoErrore}")
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.EliminaFileCaricatoGeminiInBackground: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Async Function TrascriviConGroqAsync(percorsoWav As String, token As CancellationToken, Optional contestoPrecedente As String = "", Optional perDettatura As Boolean = True) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = LeggiChiaveApiGroq()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, Nothing)

            Dim prompt As String = CostruisciPromptGroq(contestoPrecedente, perDettatura)

            Dim bytesAudio() As Byte = File.ReadAllBytes(percorsoWav)
            Dim url As String = "https://api.groq.com/openai/v1/audio/transcriptions"

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(10)

                For tentativo As Integer = 1 To NumeroMassimoTentativi
                    Dim eTemporaneo As Boolean = False
                    Dim messaggioErroreTentativo As String = Nothing

                    Try
                        Using contenutoMultipart As New MultipartFormDataContent()
                            Dim contenutoFile As New ByteArrayContent(bytesAudio)
                            contenutoFile.Headers.ContentType = New Net.Http.Headers.MediaTypeHeaderValue("audio/wav")
                            contenutoMultipart.Add(contenutoFile, "file", "dettatura.wav")
                            contenutoMultipart.Add(New StringContent(ModelloTrascrizioneGroq), "model")
                            If perDettatura Then
                                contenutoMultipart.Add(New StringContent("it"), "language")
                            End If
                            contenutoMultipart.Add(New StringContent("verbose_json"), "response_format")
                            contenutoMultipart.Add(New StringContent("0"), "temperature")
                            If Not String.IsNullOrWhiteSpace(prompt) Then
                                contenutoMultipart.Add(New StringContent(prompt, Encoding.UTF8), "prompt")
                            End If

                            Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                                richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", chiave)
                                richiesta.Content = contenutoMultipart

                                Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                WGAi.AggiornaStatoGroqDaRisposta(risposta)
                                    Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                    If Not risposta.IsSuccessStatusCode Then
                                        eTemporaneo = (risposta.StatusCode = Net.HttpStatusCode.ServiceUnavailable) OrElse (CInt(risposta.StatusCode) = 429)
                                        messaggioErroreTentativo = "Errore da Groq: " & risposta.StatusCode.ToString()
                                        log.ScriviLog($"WGDettatura.TrascriviConGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                    Else
                                        Dim testoEstratto As String = EstraiTestoDaRispostaGroq(corpoRisposta)
                                        If testoEstratto Is Nothing Then
                                            log.ScriviLog($"WGDettatura.TrascriviConGroqAsync: testo assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                            Return New Tuple(Of String, String)(Nothing, Nothing)
                                        End If
                                        If String.IsNullOrWhiteSpace(testoEstratto) Then
                                            Return New Tuple(Of String, String)(Nothing, "Nessun parlato riconosciuto")
                                        End If
                                        Dim testoSenzaRipetizioni As String = RimuoviRipetizioni(testoEstratto.Trim())
                                        If String.IsNullOrWhiteSpace(testoSenzaRipetizioni) Then
                                            Return New Tuple(Of String, String)(Nothing, "Nessun parlato riconosciuto")
                                        End If
                                        Return New Tuple(Of String, String)(testoSenzaRipetizioni, Nothing)
                                    End If
                                End Using
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Errore di connessione verso Groq: " & exRete.Message
                        log.ScriviLog($"WGDettatura.TrascriviConGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        messaggioErroreTentativo = "Timeout della richiesta verso Groq"
                        log.ScriviLog($"WGDettatura.TrascriviConGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                    End Try

                    If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                        Dim attesaSecondi As Integer = CInt(Math.Pow(2, tentativo))
                        Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                        Continue For
                    End If

                    If eTemporaneo Then
                        Return New Tuple(Of String, String)(Nothing, "Groq è momentaneamente sovraccarico")
                    End If

                    Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "Groq non ha risposto dopo diversi tentativi"))
                Next

                Return New Tuple(Of String, String)(Nothing, "Groq non ha risposto dopo diversi tentativi")
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.TrascriviConGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Private Shared Function CostruisciIstruzioniGemini(contestoPrecedente As String, perDettatura As Boolean) As String
        Try
            Dim istruzioni As New StringBuilder()
            If perDettatura Then
                istruzioni.Append("Trascrivi l'audio allegato, registrato al microfono da una persona che detta un testo in italiano. ")
                istruzioni.Append("Restituisci soltanto il testo trascritto: nessuna introduzione, nessun commento, nessuna spiegazione, nessun titolo, nessuna virgoletta attorno al testo, nessuna indicazione di tempo, nessuna descrizione di suoni o rumori. ")
                istruzioni.Append("Scrivi con punteggiatura e maiuscole corrette. ")
                istruzioni.Append("Se chi parla pronuncia a voce comandi di punteggiatura come virgola, punto, punto interrogativo, a capo o nuovo paragrafo, riportali come parole, esattamente come sono stati detti. ")
                istruzioni.Append("Non correggere, non riassumere e non completare quello che viene detto. ")
                istruzioni.Append("Se nell'audio non c'è parlato, restituisci una risposta vuota.")
            Else
                istruzioni.Append("Trascrivi l'audio allegato nella lingua in cui è parlato o cantato, senza tradurlo. ")
                istruzioni.Append("Restituisci soltanto il testo trascritto: nessuna introduzione, nessun commento, nessuna spiegazione, nessun titolo, nessuna virgoletta attorno al testo, nessuna indicazione di tempo, nessuna descrizione di suoni, rumori o musica. ")
                istruzioni.Append("Scrivi con punteggiatura e maiuscole corrette. ")
                istruzioni.Append("Se l'audio contiene una canzone, trascrivi solo le parole cantate, un verso per riga, ignorando la parte strumentale, e riporta i ritornelli ogni volta che vengono cantati. ")
                istruzioni.Append("Se parlano più persone, trascrivi il parlato di seguito senza indicare chi parla. ")
                istruzioni.Append("Non correggere, non riassumere e non completare quello che viene detto. ")
                istruzioni.Append("Se nell'audio non c'è né parlato né canto, restituisci una risposta vuota.")
            End If

            Dim vocabolario As String = LeggiVocabolarioDettatura()
            If Not String.IsNullOrWhiteSpace(vocabolario) Then
                istruzioni.Append(" Nell'audio possono comparire questi nomi e termini, da scrivere esattamente così: ")
                istruzioni.Append(vocabolario.Trim().TrimEnd("."c))
                istruzioni.Append(".")
            End If

            If Not String.IsNullOrWhiteSpace(contestoPrecedente) Then
                istruzioni.Append(" Questo audio continua una registrazione più lunga, la cui trascrizione finora termina con queste parole: «")
                istruzioni.Append(UltimiCaratteri(contestoPrecedente, LunghezzaContestoPrecedente))
                istruzioni.Append("». Non ripetere quelle parole e trascrivi solo il nuovo audio.")
            End If

            Return istruzioni.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CostruisciIstruzioniGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Trascrivi l'audio allegato e restituisci soltanto il testo trascritto, senza commenti."
        End Try
    End Function

    Private Shared Function PulisciRispostaGemini(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""

            Dim risultato As String = testo.Trim()
            risultato = Regex.Replace(risultato, "^```[a-zA-Z]*\s*", "")
            risultato = Regex.Replace(risultato, "\s*```$", "")
            risultato = Regex.Replace(risultato, "^(?:ecco|di seguito|questa è|questa e')[^\r\n]{0,80}?(?:trascrizione|testo)[^\r\n]*?:\s*", "", RegexOptions.IgnoreCase)
            risultato = Regex.Replace(risultato, "^(?:trascrizione|testo trascritto)\s*:\s*", "", RegexOptions.IgnoreCase)
            risultato = Regex.Replace(risultato, "[\[\(](?:musica|applausi|risate|silenzio|rumore|rumori|inaudibile|incomprensibile|strumentale|pausa)[^\]\)\r\n]{0,40}[\]\)]", "", RegexOptions.IgnoreCase)
            risultato = risultato.Trim()

            If risultato.Length >= 2 Then
                Dim tolteVirgolette As Boolean = (risultato.StartsWith("""") AndAlso risultato.EndsWith("""")) OrElse (risultato.StartsWith("«") AndAlso risultato.EndsWith("»"))
                If tolteVirgolette Then risultato = risultato.Substring(1, risultato.Length - 2).Trim()
            End If

            If risultato.Length < 120 AndAlso Regex.IsMatch(risultato, "^[\[\(]?\s*(?:nessun[oa]?\s+(?:parlato|testo|audio|voce|contenuto)|non (?:c'è|c'e'|è presente|e' presente|ci sono|contiene|si sente)[^\r\n]{0,40}(?:parlato|testo|voce|audio|parole)|l'audio (?:non|è vuoto|e' vuoto|è silenzioso)|audio (?:vuoto|silenzioso)|silenzio[\]\)]?\.?$)", RegexOptions.IgnoreCase) Then
                Return ""
            End If

            risultato = Regex.Replace(risultato, "\r?\n", vbCrLf)
            risultato = Regex.Replace(risultato, "[ \t]{2,}", " ")
            risultato = Regex.Replace(risultato, "(\r\n){3,}", vbCrLf & vbCrLf)
            Return risultato.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.PulisciRispostaGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Shared Function CostruisciPromptGroq(contestoPrecedente As String, perDettatura As Boolean) As String
        Try
            Dim vocabolario As String = LeggiVocabolarioDettatura()
            Dim parteVocabolario As String = ""
            If Not String.IsNullOrWhiteSpace(vocabolario) Then
                parteVocabolario = vocabolario.Trim().TrimEnd("."c, ","c, ";"c) & ". "
                If parteVocabolario.Length > LunghezzaMassimaPrompt - 150 Then
                    parteVocabolario = parteVocabolario.Substring(0, LunghezzaMassimaPrompt - 150)
                    Dim ultimaSeparazione As Integer = parteVocabolario.LastIndexOfAny(New Char() {","c, " "c})
                    If ultimaSeparazione > 0 Then parteVocabolario = parteVocabolario.Substring(0, ultimaSeparazione)
                    parteVocabolario = parteVocabolario.Trim().TrimEnd(","c) & ". "
                End If
            End If

            Dim parteContesto As String = ""
            If Not String.IsNullOrWhiteSpace(contestoPrecedente) Then
                parteContesto = contestoPrecedente.Trim()
            ElseIf perDettatura Then
                parteContesto = PromptBaseItaliano
            End If
            Dim spazioDisponibile As Integer = LunghezzaMassimaPrompt - parteVocabolario.Length
            If spazioDisponibile < 150 Then spazioDisponibile = 150

            Return (parteVocabolario & UltimiCaratteri(parteContesto, spazioDisponibile)).Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CostruisciPromptGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(perDettatura, PromptBaseItaliano, "")
        End Try
    End Function

    Private Shared Function UltimiCaratteri(testo As String, massimo As Integer) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim testoPulito As String = testo.Trim()
            If testoPulito.Length <= massimo Then Return testoPulito
            Dim coda As String = testoPulito.Substring(testoPulito.Length - massimo)
            Dim primoSpazio As Integer = coda.IndexOf(" "c)
            If primoSpazio >= 0 AndAlso primoSpazio < coda.Length - 1 Then coda = coda.Substring(primoSpazio + 1)
            Return coda.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.UltimiCaratteri: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function LeggiNumeroDaSegmento(segmento As JToken, nomeCampo As String, valorePredefinito As Double) As Double
        Try
            Dim gettone As JToken = segmento(nomeCampo)
            If gettone Is Nothing OrElse gettone.Type = JTokenType.Null Then Return valorePredefinito
            Return gettone.ToObject(Of Double)()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiNumeroDaSegmento ({nomeCampo}): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return valorePredefinito
        End Try
    End Function

    Private Shared Function EstraiTestoDaRispostaGroq(corpoRisposta As String) As String
        Try
            Dim risultato As JObject = JObject.Parse(corpoRisposta)
            Dim segmentiRisposta As JArray = TryCast(risultato("segments"), JArray)

            If segmentiRisposta Is Nothing OrElse segmentiRisposta.Count = 0 Then
                Dim gettoneTesto As JToken = risultato("text")
                If gettoneTesto Is Nothing Then Return Nothing
                Return gettoneTesto.ToString().Trim()
            End If

            Dim testoFissoPrompt As String = NormalizzaPerConfronto(PromptBaseItaliano & " " & LeggiVocabolarioDettatura())
            Dim testoRisultante As New StringBuilder()

            For Each segmento As JToken In segmentiRisposta
                Dim gettoneSegmento As JToken = segmento("text")
                If gettoneSegmento Is Nothing Then Continue For
                Dim testoSegmento As String = gettoneSegmento.ToString().Trim()
                If testoSegmento.Length = 0 Then Continue For

                Dim probabilitaSilenzio As Double = LeggiNumeroDaSegmento(segmento, "no_speech_prob", 0.0)
                Dim probabilitaMedia As Double = LeggiNumeroDaSegmento(segmento, "avg_logprob", 0.0)
                If probabilitaSilenzio > SogliaNoSpeechProb AndAlso probabilitaMedia < SogliaAvgLogprob Then Continue For

                Dim segmentoNormalizzato As String = NormalizzaPerConfronto(testoSegmento)
                If segmentoNormalizzato.Length >= 8 AndAlso testoFissoPrompt.Contains(segmentoNormalizzato) Then Continue For

                If testoRisultante.Length > 0 Then testoRisultante.Append(" ")
                testoRisultante.Append(testoSegmento)
            Next

            Return testoRisultante.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.EstraiTestoDaRispostaGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function NormalizzaPerConfronto(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim risultato As New StringBuilder(testo.Length)
            Dim ultimoEraSpazio As Boolean = True
            For Each carattere As Char In testo.ToLowerInvariant()
                If Char.IsLetterOrDigit(carattere) Then
                    risultato.Append(carattere)
                    ultimoEraSpazio = False
                ElseIf Not ultimoEraSpazio Then
                    risultato.Append(" "c)
                    ultimoEraSpazio = True
                End If
            Next
            Return risultato.ToString().Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.NormalizzaPerConfronto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function FinalizzaTesto(testo As String, applicaComandi As Boolean) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim risultato As String = RimuoviAllucinazioni(testo)
            If applicaComandi Then risultato = ApplicaComandiVocali(risultato)
            risultato = AggiungiACapoDopoPunto(risultato)
            Return risultato.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.FinalizzaTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(testo, "").Trim()
        End Try
    End Function

    Private Shared ReadOnly ModelliAllucinazioni As String() = {
        "sottotitoli[^\r\n.]{0,60}?amara\.org\.?",
        "sottotitoli[^\r\n.]{0,60}?qtss\.?",
        "\bamara\.org\b\.?",
        "\bqtss\b\.?",
        "(?:grazie (?:a tutti )?per la visione|grazie per aver guardato(?: il video)?|iscriviti al canale)[.!]?\s*$"
    }

    Private Shared Function RimuoviAllucinazioni(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim risultato As String = testo
            For Each modello As String In ModelliAllucinazioni
                risultato = Regex.Replace(risultato, modello, " ", RegexOptions.IgnoreCase)
            Next
            risultato = Regex.Replace(risultato, "[ \t]{2,}", " ")
            Return risultato.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.RimuoviAllucinazioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function ChiaveParola(parola As String) As String
        Try
            If String.IsNullOrEmpty(parola) Then Return ""
            Dim risultato As New StringBuilder(parola.Length)
            For Each carattere As Char In parola.ToLowerInvariant()
                If Char.IsLetterOrDigit(carattere) Then risultato.Append(carattere)
            Next
            Return risultato.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ChiaveParola: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function BlocchiUguali(chiavi As List(Of String), inizioPrimo As Integer, inizioSecondo As Integer, lunghezza As Integer) As Boolean
        Try
            For scostamento As Integer = 0 To lunghezza - 1
                Dim chiavePrima As String = chiavi(inizioPrimo + scostamento)
                Dim chiaveSeconda As String = chiavi(inizioSecondo + scostamento)
                If chiavePrima.Length = 0 OrElse Not String.Equals(chiavePrima, chiaveSeconda, StringComparison.Ordinal) Then Return False
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.BlocchiUguali: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function RimuoviRipetizioni(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""

            Dim parole As New List(Of String)()
            For Each parola As String In Regex.Split(testo.Trim(), "[ \t]+")
                If parola.Length > 0 Then parole.Add(parola)
            Next
            If parole.Count < 2 Then Return testo.Trim()

            Dim chiavi As New List(Of String)(parole.Count)
            For Each parola As String In parole
                chiavi.Add(ChiaveParola(parola))
            Next

            Dim modificato As Boolean = True
            Dim giri As Integer = 0
            Do While modificato AndAlso giri < 10
                modificato = False
                giri += 1
                Dim lunghezzaMassima As Integer = Math.Min(LunghezzaMassimaBloccoRipetuto, chiavi.Count \ 2)
                For lunghezza As Integer = lunghezzaMassima To 1 Step -1
                    Dim posizione As Integer = 0
                    Do While posizione + 2 * lunghezza <= chiavi.Count
                        If BlocchiUguali(chiavi, posizione, posizione + lunghezza, lunghezza) Then
                            Dim ripetizioni As Integer = 2
                            Do While posizione + (ripetizioni + 1) * lunghezza <= chiavi.Count AndAlso BlocchiUguali(chiavi, posizione, posizione + ripetizioni * lunghezza, lunghezza)
                                ripetizioni += 1
                            Loop
                            If lunghezza >= 3 OrElse ripetizioni >= 3 Then
                                Dim daRimuovere As Integer = (ripetizioni - 1) * lunghezza
                                parole.RemoveRange(posizione, daRimuovere)
                                chiavi.RemoveRange(posizione, daRimuovere)
                                modificato = True
                                Continue Do
                            End If
                        End If
                        posizione += 1
                    Loop
                Next
            Loop

            Return String.Join(" ", parole)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.RimuoviRipetizioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared ReadOnly EspressionePuntoFineFrase As New Regex("(?<!\.)(?<!\b(?:sig|sigg|dott|prof|ing|avv|ecc|pag|pagg|es|nr|tel|art|artt|vol|fig|geom|rag|arch|sen|mons|cfr|etc))\.(?!\.)[ \t]*(?=[^\s\d""'»)\]])", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    Private Shared Function AggiungiACapoDopoPunto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return testo
            Dim risultato As String = EspressionePuntoFineFrase.Replace(testo, "." & vbCrLf)
            risultato = Regex.Replace(risultato, "\n[ \t]+", vbLf)
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AggiungiACapoDopoPunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared ReadOnly ComandiVocali As Tuple(Of String, String)() = {
        New Tuple(Of String, String)("punto e a capo", "." & vbCrLf),
        New Tuple(Of String, String)("punto a capo", "." & vbCrLf),
        New Tuple(Of String, String)("nuovo paragrafo", vbCrLf & vbCrLf),
        New Tuple(Of String, String)("vai a capo", vbCrLf),
        New Tuple(Of String, String)("a capo", vbCrLf),
        New Tuple(Of String, String)("punto e virgola", "; "),
        New Tuple(Of String, String)("punto di domanda", "? "),
        New Tuple(Of String, String)("punto domanda", "? "),
        New Tuple(Of String, String)("punto interrogativo", "? "),
        New Tuple(Of String, String)("punto di esclamazione", "! "),
        New Tuple(Of String, String)("punto esclamativo", "! "),
        New Tuple(Of String, String)("puntini di sospensione", "... "),
        New Tuple(Of String, String)("tre puntini", "... "),
        New Tuple(Of String, String)("due punti", ": "),
        New Tuple(Of String, String)("apri virgolette", " """),
        New Tuple(Of String, String)("chiudi virgolette", """ "),
        New Tuple(Of String, String)("apri parentesi", " ("),
        New Tuple(Of String, String)("chiudi parentesi", ") "),
        New Tuple(Of String, String)("trattino basso", "_"),
        New Tuple(Of String, String)("virgola", ", "),
        New Tuple(Of String, String)("punto", ". "),
        New Tuple(Of String, String)("trattino", "-")
    }

    Public Shared Function ApplicaPunteggiaturaDettata(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return If(testo, "")

            Dim risultato As String = testo
            For Each comando As Tuple(Of String, String) In ComandiVocali
                Dim parole As String = Regex.Escape(comando.Item1).Replace("\ ", "\s+")
                Dim modello As String = "[\s,;:]*\b" & parole & "\b[\s,.;:!?]*"
                If comando.Item2 = "_" OrElse comando.Item2 = "-" Then modello = "\s*\b" & parole & "\b\s*"
                Dim sostituto As String = comando.Item2
                risultato = Regex.Replace(risultato, modello, Function(m As Match) sostituto, RegexOptions.IgnoreCase)
            Next

            risultato = Regex.Replace(risultato, "[ \t]+([,.;:!?)])", "$1")
            risultato = Regex.Replace(risultato, "([,;:!?])\1+", "$1")
            risultato = Regex.Replace(risultato, "([?!])\.+", "$1")
            risultato = Regex.Replace(risultato, "\.([,;:])", "$1")
            risultato = Regex.Replace(risultato, "([,.;:!?])(?=[\p{L}\p{N}])", "$1 ")
            risultato = Regex.Replace(risultato, "\(\s+", "(")
            risultato = Regex.Replace(risultato, "[ \t]{2,}", " ")
            risultato = Regex.Replace(risultato, "[ \t]*(\r\n|\n)[ \t]*", "$1")
            risultato = Regex.Replace(risultato, "(^|[.!?][ \t]+|[.!?]?\r?\n+)(\p{Ll})", Function(m As Match) m.Groups(1).Value & m.Groups(2).Value.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("it-IT")))

            Return risultato.Trim(" "c, ControlChars.Tab)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ApplicaPunteggiaturaDettata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function ApplicaComandiVocali(testo As String) As String
        Try
            Return ApplicaPunteggiaturaDettata(testo)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ApplicaComandiVocali: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function CalcolaRmsBuffer(buffer() As Byte, lunghezza As Integer) As Double
        Try
            Dim numeroCampioni As Integer = lunghezza \ 2
            If numeroCampioni <= 0 Then Return 0.0
            Dim sommaQuadrati As Double = 0.0
            For indice As Integer = 0 To numeroCampioni - 1
                Dim campione As Double = BitConverter.ToInt16(buffer, indice * 2)
                sommaQuadrati += campione * campione
            Next
            Return Math.Sqrt(sommaQuadrati / numeroCampioni)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CalcolaRmsBuffer: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Double.MaxValue
        End Try
    End Function

    Private Shared Function LeggiCampioniWav(percorsoFile As String, ByRef formato As WaveFormat) As Short()
        Try
            Using lettore As New WaveFileReader(percorsoFile)
                formato = lettore.WaveFormat
                Dim lunghezzaTotale As Integer = CInt(lettore.Length)
                Dim datiGrezzi(lunghezzaTotale - 1) As Byte
                Dim lettiTotali As Integer = 0
                Do While lettiTotali < datiGrezzi.Length
                    Dim letti As Integer = lettore.Read(datiGrezzi, lettiTotali, lunghezzaTotale - lettiTotali)
                    If letti <= 0 Then Exit Do
                    lettiTotali += letti
                Loop
                Dim campioni(lettiTotali \ 2 - 1) As Short
                If campioni.Length > 0 Then System.Buffer.BlockCopy(datiGrezzi, 0, campioni, 0, campioni.Length * 2)
                Return campioni
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.LeggiCampioniWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ScriviCampioniWav(percorsoFile As String, formato As WaveFormat, campioni() As Short) As Boolean
        Try
            Dim bytesCampioni(campioni.Length * 2 - 1) As Byte
            If campioni.Length > 0 Then System.Buffer.BlockCopy(campioni, 0, bytesCampioni, 0, bytesCampioni.Length)
            Using scrittore As New WaveFileWriter(percorsoFile, formato)
                scrittore.Write(bytesCampioni, 0, bytesCampioni.Length)
            End Using
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.ScriviCampioniWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Sub SostituisciFile(percorsoOriginale As String, percorsoNuovo As String)
        Try
            File.Delete(percorsoOriginale)
            File.Move(percorsoNuovo, percorsoOriginale)
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.SostituisciFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Try
                If File.Exists(percorsoNuovo) Then File.Delete(percorsoNuovo)
            Catch exPulizia As Exception
                log.ScriviLog($"WGDettatura.SostituisciFile (pulizia): {exPulizia.Message}")
            End Try
        End Try
    End Sub

    Private Shared Function CalcolaGuadagnoAnalisi(campioni() As Short) As Double
        Try
            If campioni Is Nothing OrElse campioni.Length = 0 Then Return 1.0
            Dim picco As Integer = 0
            For Each campione As Short In campioni
                Dim valoreAssoluto As Integer = Math.Abs(CInt(campione))
                If valoreAssoluto > picco Then picco = valoreAssoluto
            Next
            If picco <= 0 OrElse picco >= PiccoObiettivoAnalisi Then Return 1.0
            Return Math.Max(1.0, Math.Min(GuadagnoMassimoAnalisi, PiccoObiettivoAnalisi / CDbl(picco)))
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.CalcolaGuadagnoAnalisi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 1.0
        End Try
    End Function

    Private Function RiduciSilenzi(percorsoFile As String) As Boolean
        Try
            Dim formato As WaveFormat = Nothing
            Dim campioni() As Short = LeggiCampioniWav(percorsoFile, formato)
            If campioni Is Nothing OrElse formato Is Nothing Then Return True
            If formato.BitsPerSample <> 16 OrElse formato.Channels <> 1 Then Return True

            Dim campioniPerFrame As Integer = formato.SampleRate * DurataFrameAnalisiMs \ 1000
            If campioniPerFrame <= 0 Then Return True

            Dim numeroFrame As Integer = campioni.Length \ campioniPerFrame
            If numeroFrame = 0 Then Return False

            Dim guadagnoAnalisi As Double = CalcolaGuadagnoAnalisi(campioni)
            Dim energie(numeroFrame - 1) As Double
            For frame As Integer = 0 To numeroFrame - 1
                Dim sommaQuadrati As Double = 0.0
                Dim inizio As Integer = frame * campioniPerFrame
                For indice As Integer = inizio To inizio + campioniPerFrame - 1
                    Dim valore As Double = campioni(indice)
                    sommaQuadrati += valore * valore
                Next
                energie(frame) = Math.Sqrt(sommaQuadrati / campioniPerFrame) * guadagnoAnalisi
            Next

            Dim energieOrdinate() As Double = CType(energie.Clone(), Double())
            Array.Sort(energieOrdinate)
            Dim rumoreDiFondo As Double = energieOrdinate(CInt(Math.Floor((numeroFrame - 1) * 0.1)))
            Dim livelloMassimo As Double = energieOrdinate(numeroFrame - 1)

            If livelloMassimo < 150.0 Then Return False

            Dim soglia As Double = Math.Max(rumoreDiFondo * 2.5, 150.0)
            If soglia > livelloMassimo * 0.3 Then Return True

            Dim parlato(numeroFrame - 1) As Boolean
            Dim frameParlati As Integer = 0
            For frame As Integer = 0 To numeroFrame - 1
                If energie(frame) > soglia Then
                    parlato(frame) = True
                    frameParlati += 1
                End If
            Next

            If frameParlati * DurataFrameAnalisiMs < DurataMinimaParlatoMs Then Return False

            Dim daConservare(numeroFrame - 1) As Boolean
            For frame As Integer = 0 To numeroFrame - 1
                If parlato(frame) Then
                    Dim primo As Integer = Math.Max(0, frame - FramePaddingParlato)
                    Dim ultimo As Integer = Math.Min(numeroFrame - 1, frame + FramePaddingParlato)
                    For vicino As Integer = primo To ultimo
                        daConservare(vicino) = True
                    Next
                End If
            Next

            Dim risultato As New List(Of Short)(campioni.Length)
            Dim posizioneFrame As Integer = 0
            Do While posizioneFrame < numeroFrame
                If daConservare(posizioneFrame) Then
                    AggiungiFrame(risultato, campioni, posizioneFrame, 1, campioniPerFrame)
                    posizioneFrame += 1
                Else
                    Dim fineSilenzio As Integer = posizioneFrame
                    Do While fineSilenzio < numeroFrame AndAlso Not daConservare(fineSilenzio)
                        fineSilenzio += 1
                    Loop
                    If posizioneFrame > 0 AndAlso fineSilenzio < numeroFrame Then
                        Dim frameDaTenere As Integer = Math.Min(fineSilenzio - posizioneFrame, FrameMassimiSilenzioInterno)
                        AggiungiFrame(risultato, campioni, posizioneFrame, frameDaTenere, campioniPerFrame)
                    End If
                    posizioneFrame = fineSilenzio
                End If
            Loop

            If daConservare(numeroFrame - 1) Then
                For indice As Integer = numeroFrame * campioniPerFrame To campioni.Length - 1
                    risultato.Add(campioni(indice))
                Next
            End If

            If risultato.Count >= campioni.Length * 0.95 Then Return True

            Dim percorsoRidotto As String = percorsoFile & ".ridotto"
            If ScriviCampioniWav(percorsoRidotto, formato, risultato.ToArray()) Then
                SostituisciFile(percorsoFile, percorsoRidotto)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.RiduciSilenzi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return True
        End Try
    End Function

    Private Shared Sub AggiungiFrame(destinazione As List(Of Short), campioni() As Short, primoFrame As Integer, numeroFrameDaAggiungere As Integer, campioniPerFrame As Integer)
        Try
            Dim inizio As Integer = primoFrame * campioniPerFrame
            Dim fine As Integer = Math.Min(campioni.Length, inizio + numeroFrameDaAggiungere * campioniPerFrame)
            For indice As Integer = inizio To fine - 1
                destinazione.Add(campioni(indice))
            Next
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.AggiungiFrame: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not _disposedValue Then
                If disposing Then
                    Annulla()
                End If
                _disposedValue = True
            End If
        Catch ex As Exception
            log.ScriviLog($"WGDettatura.Dispose: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class