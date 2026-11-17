' ============================================================================================
' WGRegistratoreVocale - il registratore vocale di WinGabriel
' ============================================================================================
'
' Registra la voce dal microfono e, se si vuole, la mette sopra una traccia di sottofondo che si sente durante la
' registrazione. Ogni registrazione è una traccia di un progetto (vedi WGProgettoVocale): le tracce precedenti si sentono mentre si
' registra quella nuova, si spostano con le frecce, si eliminano con Canc e si esportano in mp3 o wav. La voce e il sottofondo escono da due dispositivi con due orologi diversi: se si unissero semplicemente
' all'inizio si sentirebbe la voce in ritardo, e sempre di più con il passare dei minuti.
'
' Per questo, durante la registrazione, a ogni blocco di voce che arriva si annota anche a che punto era il sottofondo
' (la posizione realmente suonata dalla scheda audio). Alla fine, con una retta che passa per questi punti, si sa
' esattamente dove va ogni campione della voce rispetto al sottofondo, anche se i due orologi corrono in modo un po'
' diverso, e la voce viene rimessa al suo posto con un'interpolazione cubica. Resta solo il ritardo fisso del microfono,
' che si misura con "Calibra la sincronia" oppure si regola a mano.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

''' <summary>La parte che sa mettere insieme la voce e il sottofondo al posto giusto.</summary>
Public NotInheritable Class AllineatoreVoce

    Private Sub New()
    End Sub

    ''' <summary>Retta posizione del sottofondo = a * campione della voce + d, ricavata dai punti annotati.</summary>
    Public Shared Sub RettaDiAllineamento(campioniVoce As List(Of Long), posizioniSottofondo As List(Of Long), frequenzaVoce As Integer, ByRef a As Double, ByRef d As Double)
        Dim nominale As Double = WGDsp.FC / frequenzaVoce
        a = nominale
        d = 0.0
        Dim n As Integer = campioniVoce.Count
        If n = 0 Then Return
        Dim mediaC As Double = 0.0
        Dim mediaP As Double = 0.0
        For i As Integer = 0 To n - 1
            mediaC += campioniVoce(i)
            mediaP += posizioniSottofondo(i)
        Next
        mediaC /= n
        mediaP /= n
        Dim sxx As Double = 0.0
        Dim sxy As Double = 0.0
        For i As Integer = 0 To n - 1
            Dim dc As Double = campioniVoce(i) - mediaC
            sxx += dc * dc
            sxy += dc * (posizioniSottofondo(i) - mediaP)
        Next
        If n >= 20 AndAlso sxx > 1.0E+9 Then
            Dim pendenza As Double = sxy / sxx
            ' due orologi non differiscono mai di più di qualche centinaio di parti per milione
            If Math.Abs(pendenza / nominale - 1.0) < 0.0008 Then a = pendenza
        End If
        d = mediaP - a * mediaC
    End Sub

    Private Shared Function Cubica(x() As Short, n As Integer, t As Double) As Double
        Dim i As Integer = CInt(Math.Floor(t))
        Dim f As Double = t - i
        Dim y0 As Double = Campione(x, n, i - 1)
        Dim y1 As Double = Campione(x, n, i)
        Dim y2 As Double = Campione(x, n, i + 1)
        Dim y3 As Double = Campione(x, n, i + 2)
        Dim c0 As Double = y1
        Dim c1 As Double = 0.5 * (y2 - y0)
        Dim c2 As Double = y0 - 2.5 * y1 + 2.0 * y2 - 0.5 * y3
        Dim c3 As Double = 0.5 * (y3 - y0) + 1.5 * (y1 - y2)
        Return ((c3 * f + c2) * f + c1) * f + c0
    End Function

    Private Shared Function Campione(x() As Short, n As Integer, i As Integer) As Double
        If i < 0 OrElse i >= n Then Return 0.0
        Return x(i) / 32768.0
    End Function

    ''' <summary>
    ''' Scrive in percorsoWav (stereo, 48000 Hz, virgola mobile) la voce al suo posto sopra il sottofondo, se c'è. Restituisce "" se è
    ''' andata bene o il motivo dell'errore. compensazioneMs sposta la voce prima (positivo) o dopo (negativo).
    ''' </summary>
    Public Shared Function Unisci(voce() As Short, campioniVoce As Integer, frequenzaVoce As Integer, a As Double, d As Double, compensazioneMs As Double,
                                  percorsoSottofondo As String, ripetiSottofondo As Boolean, volumeVoce As Double, volumeSottofondo As Double,
                                  percorsoWav As String, Optional daZero As Boolean = False) As String
        Dim sottofondo As SottofondoAudio = Nothing
        Try
            Dim dMod As Double = d - compensazioneMs * WGDsp.FC / 1000.0
            Dim fine As Long = CLng(Math.Ceiling(a * campioniVoce + dMod))
            Dim inizio As Long
            If daZero Then
                ' una traccia di voce comincia sempre dall'istante zero del sottofondo e delle altre tracce: quello che viene prima si scarta
                inizio = 0
            ElseIf String.IsNullOrEmpty(percorsoSottofondo) Then
                inizio = CLng(Math.Floor(dMod))
            Else
                inizio = CLng(Math.Floor(Math.Min(0.0, dMod)))
                sottofondo = New SottofondoAudio(percorsoSottofondo)
                sottofondo.Ripeti = ripetiSottofondo
            End If
            If fine <= inizio + 100 Then Return "La registrazione era troppo breve."
            Dim gV As Double = volumeVoce
            Dim gB As Double = volumeSottofondo * volumeSottofondo
            Dim bk(8191) As Single
            Using scrittore As New WaveFileWriter(percorsoWav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                Dim blocco(8191) As Single
                Dim m As Long = inizio
                While m < fine
                    Dim n As Integer = CInt(Math.Min(4096L, fine - m))
                    Dim conSottofondo As Boolean = False
                    Array.Clear(bk, 0, bk.Length)
                    If sottofondo IsNot Nothing AndAlso m + n > 0 Then
                        ' il sottofondo si legge di seguito dall'inizio; prima di zero non c'è
                        Dim da As Integer = CInt(Math.Max(0L, -m))
                        sottofondo.Read(bk, da * 2, (n - da) * 2)
                        conSottofondo = True
                    End If
                    For k As Integer = 0 To n - 1
                        Dim posizione As Long = m + k
                        Dim t As Double = (posizione - dMod) / a
                        Dim v As Double = 0.0
                        If t >= -2.0 AndAlso t < campioniVoce + 2 Then v = Cubica(voce, campioniVoce, t) * gV
                        Dim s As Double = v
                        Dim dx As Double = v
                        If conSottofondo AndAlso posizione >= 0 Then
                            s += bk(k * 2) * gB
                            dx += bk(k * 2 + 1) * gB
                        End If
                        blocco(k * 2) = CSng(Morbido(s))
                        blocco(k * 2 + 1) = CSng(Morbido(dx))
                    Next
                    scrittore.WriteSamples(blocco, 0, n * 2)
                    m += n
                End While
            End Using
            Return ""
        Catch ex As Exception
            Return "Non è stato possibile unire la voce e il sottofondo: " & ex.Message
        Finally
            If sottofondo IsNot Nothing Then sottofondo.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' Unisce le tracce di voce (e il sottofondo, se c'è) in un solo file stereo a 48000 Hz a virgola mobile, lungo quanto la traccia
    ''' più lunga. Restituisce "" se è andata bene, altrimenti il motivo.
    ''' </summary>
    Public Shared Function UnisciTracce(percorsi As IList(Of String), guadagniTracce As IList(Of Double), fotogrammi As Long, percorsoSottofondo As String, ripetiSottofondo As Boolean,
                                        guadagnoSottofondo As Single, percorsoWav As String) As String
        Dim fonti As New List(Of SottofondoAudio)()
        Dim sottofondo As SottofondoAudio = Nothing
        Try
            For Each p As String In percorsi
                fonti.Add(New SottofondoAudio(p))
            Next
            If percorsoSottofondo <> "" AndAlso File.Exists(percorsoSottofondo) Then
                sottofondo = New SottofondoAudio(percorsoSottofondo)
                sottofondo.Ripeti = ripetiSottofondo
            End If
            Dim totale As Long = fotogrammi * 2
            Dim blocco(8191) As Single
            Dim lettura(8191) As Single
            Using scrittore As New WaveFileWriter(percorsoWav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                Dim fatti As Long = 0
                While fatti < totale
                    Dim n As Integer = CInt(Math.Min(8192L, totale - fatti))
                    Array.Clear(blocco, 0, n)
                    For indiceFonte As Integer = 0 To fonti.Count - 1
                        fonti(indiceFonte).Read(lettura, 0, n)
                        Dim g As Single = CSng(If(indiceFonte < guadagniTracce.Count, guadagniTracce(indiceFonte), 1.0))
                        For k As Integer = 0 To n - 1
                            blocco(k) += lettura(k) * g
                        Next
                    Next
                    If sottofondo IsNot Nothing Then
                        sottofondo.Read(lettura, 0, n)
                        For k As Integer = 0 To n - 1
                            blocco(k) += lettura(k) * guadagnoSottofondo
                        Next
                    End If
                    For k As Integer = 0 To n - 1
                        blocco(k) = CSng(Morbido(blocco(k)))
                    Next
                    scrittore.WriteSamples(blocco, 0, n)
                    fatti += n
                End While
            End Using
            Return ""
        Catch ex As Exception
            Return "Non è stato possibile unire le tracce: " & ex.Message
        Finally
            For Each f As SottofondoAudio In fonti
                Try
                    f.Dispose()
                Catch
                End Try
            Next
            Try
                If sottofondo IsNot Nothing Then sottofondo.Dispose()
            Catch
            End Try
        End Try
    End Function

    Private Shared Function Morbido(x As Double) As Double
        Dim ax As Double = Math.Abs(x)
        If ax <= 0.85 Then Return x
        Dim y As Double = 0.85 + 0.15 * Math.Tanh((ax - 0.85) / 0.15)
        Return If(x < 0.0, -y, y)
    End Function

End Class

''' <summary>Arrotonda i picchi della somma delle tracce: con il volume alzato non si deve sentire il suono che scoppia.</summary>
Friend NotInheritable Class LimitatoreMorbido
    Implements ISampleProvider

    Private ReadOnly _origine As ISampleProvider

    Public Sub New(origine As ISampleProvider)
        _origine = origine
    End Sub

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _origine.WaveFormat
        End Get
    End Property

    Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Dim letti As Integer = _origine.Read(buffer, offset, count)
        For i As Integer = offset To offset + letti - 1
            Dim x As Single = buffer(i)
            Dim ax As Single = Math.Abs(x)
            If ax > 0.85F Then
                Dim y As Single = 0.85F + 0.15F * CSng(Math.Tanh((ax - 0.85F) / 0.15F))
                buffer(i) = If(x < 0.0F, -y, y)
            End If
        Next
        Return letti
    End Function

End Class

''' <summary>Genera i colpi della calibrazione: un clic ogni mezzo secondo e mezzo.</summary>
Friend NotInheritable Class GeneratoreClic
    Implements IWaveProvider

    Private ReadOnly _formato As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
    Private _posizione As Long
    Public Const Distanza As Integer = 28800
    Public Const Anticipo As Integer = 24000
    Public Const NumeroClic As Integer = 6

    Public ReadOnly Property WaveFormat As WaveFormat Implements IWaveProvider.WaveFormat
        Get
            Return _formato
        End Get
    End Property

    Public Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer Implements IWaveProvider.Read
        Dim fotogrammi As Integer = count \ 8
        Dim dati(fotogrammi * 2 - 1) As Single
        For i As Integer = 0 To fotogrammi - 1
            Dim p As Long = _posizione + i
            Dim relativo As Long = p - Anticipo
            Dim valore As Single = 0.0F
            If relativo >= 0 Then
                Dim k As Long = relativo \ Distanza
                Dim r As Long = relativo Mod Distanza
                If k < NumeroClic AndAlso r < 240 Then
                    ' un colpo secco: una sinusoide a 2 kHz che si spegne in pochi millisecondi
                    valore = CSng(Math.Sin(2.0 * Math.PI * 2000.0 * r / 48000.0) * Math.Exp(-r / 60.0) * 0.9)
                End If
            End If
            dati(i * 2) = valore
            dati(i * 2 + 1) = valore
        Next
        _posizione += fotogrammi
        System.Buffer.BlockCopy(dati, 0, buffer, offset, fotogrammi * 8)
        Return fotogrammi * 8
    End Function

End Class

''' <summary>
''' La sessione del registratore vocale: la riga della traccia, le opzioni con Alt, la cattura del microfono, la riproduzione di
''' sottofondo e tracce precedenti e l'unione finale. Ogni registrazione è una traccia di un progetto.
''' </summary>
Public NotInheritable Class SessioneRegistratoreVocale
    Implements IGestoreTastiMusicali, IDisposable

    Private Const VkSpazio As Integer = &H20
    Private Const VkSu As Integer = &H26
    Private Const VkGiu As Integer = &H28
    Private Const VkInserisci As Integer = &H2D
    Private Const VkCanc As Integer = &H2E
    Private Const IdControllo As String = "controllo"
    Private Const FrequenzaCattura As Integer = 48000

    Private ReadOnly _log As New Logger()
    Private _lista As WGMidiRiproduzione
    Private _chiusa As Boolean

    ' progetto e tracce
    Private ReadOnly _progetto As ProgettoVocale
    Private ReadOnly _selezione As New HashSet(Of TracciaVocale)()
    Private ReadOnly _bloccoTracce As New Object()
    ' la riga corrente: una traccia registrata oppure (l'ultima) la traccia nuova da registrare
    Private _indiceRiga As Integer
    Private _modificato As Boolean
    Private _elaborazioni As Integer
    Private ReadOnly _fileTemporanei As New List(Of String)()
    Private ReadOnly _lettoriTracce As New List(Of SottofondoAudio)()
    Private _timerFuoco As System.Threading.Timer
    Private _spazioPremuto As Boolean
    Private _cancPremuto As Boolean
    Private _inserisciPremuto As Boolean
    Private ReadOnly _volumiTracce As New List(Of VolumeSampleProvider)()
    Private _salvaAllUscita As Boolean = True
    Private _uscitaRichiesta As Boolean
    Private _tempoMetronomo As Integer = 100
    Private _battitiMetronomo As Integer = 4
    Private _metronomoInRegistrazione As Boolean

    ' tempo massimo di registrazione
    Private _timerLimite As System.Threading.Timer
    Private _idRegistrazione As Integer
    Private _fermataPerLimite As Boolean

    ' cattura
    Private _ingresso As WaveInEvent
    Private _frequenzaCatturaEffettiva As Integer = FrequenzaCattura
    Private _memoriaVoce As MemoryStream
    Private _campioniCatturati As Long
    Private ReadOnly _puntiVoce As New List(Of Long)()
    Private ReadOnly _puntiSottofondo As New List(Of Long)()
    Private ReadOnly _bloccoPunti As New Object()
    Private _ultimaPosizione As Long
    Private _picco As Single
    Private _inRegistrazione As Boolean
    Private _modoControllo As Boolean

    ' sottofondo
    Private _percorsoSottofondo As String = ""
    Private _nomeSottofondo As String = ""
    Private _uscita As WaveOutEvent
    Private _lettoreSottofondo As SottofondoAudio
    Private _inProva As Boolean

    ' impostazioni
    Private _dispositivo As Integer
    Private _volumeVoce As Integer
    Private _volumeSottofondo As Integer
    Private _compensazioneMs As Integer
    Private _ripetiSottofondo As Boolean
    Private _selettoreCartella As SelettoreCartellaMusica
    Private _dialogoSottofondo As WGFileDialog

    ''' <summary>Un progetto nuovo.</summary>
    Public Sub New()
        _progetto = New ProgettoVocale()
    End Sub

    ''' <summary>Un progetto già salvato che si riapre.</summary>
    Public Sub New(progetto As ProgettoVocale)
        _progetto = progetto
        _indiceRiga = progetto.Tracce.Count
    End Sub

    ''' <summary>
    ''' Rilegge le impostazioni del registratore vocale, che si cambiano anche da fuori (Impostazioni, Applicazioni, Registratore vocale) e dal menu di
    ''' configurazione che si apre da qui: i valori sono sempre quelli salvati, non una copia.
    ''' </summary>
    Private Sub RileggiImpostazioni()
        _volumeVoce = Math.Max(0, Math.Min(400, ImpostazioniMusica.LeggiIntero("voce_volume", 100)))
        _volumeSottofondo = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("voce_volume_sottofondo", 60)))
        _compensazioneMs = Math.Max(-500, Math.Min(500, ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0)))
        If _progetto.Sottofondo = "" Then _ripetiSottofondo = ImpostazioniMusica.LeggiBooleano("voce_ripeti_sottofondo", False)
        If Not _inRegistrazione Then _dispositivo = TrovaDispositivo(ImpostazioniMusica.Leggi("voce_microfono", ""))
    End Sub

    Public Sub Avvia()
        Try
            _volumeVoce = Math.Max(0, Math.Min(400, ImpostazioniMusica.LeggiIntero("voce_volume", 100)))
            _tempoMetronomo = Math.Max(30, Math.Min(300, ImpostazioniMusica.LeggiIntero("musica_metronomo_bpm", 100)))
            _battitiMetronomo = Math.Max(1, Math.Min(12, ImpostazioniMusica.LeggiIntero("musica_metronomo_battiti", 4)))
            _metronomoInRegistrazione = ImpostazioniMusica.LeggiBooleano("musica_metronomo_registrazione", False)
            _volumeSottofondo = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("voce_volume_sottofondo", 60)))
            _compensazioneMs = Math.Max(-500, Math.Min(500, ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0)))
            _ripetiSottofondo = ImpostazioniMusica.LeggiBooleano("voce_ripeti_sottofondo", False)
            _dispositivo = TrovaDispositivo(ImpostazioniMusica.Leggi("voce_microfono", ""))
            If WaveInEvent.DeviceCount = 0 Then
                AiutoMusica.Messaggio("Non c'è nessun microfono collegato al computer.")
                Return
            End If
            ' il sottofondo non si ricorda: a ogni apertura del registratore, anche di un progetto già salvato, si riparte senza
            _progetto.Sottofondo = ""
            _progetto.SottofondoRipetuto = False

            _lista = New WGMidiRiproduzione()
            _lista.multiselezioneConsentita = False
            _lista.impostaTitolo("Registratore vocale")
            RiempiLista()
            AddHandler _lista.escPremuto, AddressOf OnEsc
            AddHandler _lista.ElementoSelezionato, AddressOf OnVoceSelezionata
            AddHandler _lista.AltGrPremuto, AddressOf OnAltGr
            _lista.AggiornaOpzioni = AddressOf DefinisciOpzioni
            _lista.avvia()

            WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Me)
            _timerFuoco = New System.Threading.Timer(AddressOf ControllaFuoco, Nothing, 300, 300)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il registratore vocale.")
        End Try
    End Sub

    Private Function TrovaDispositivo(nome As String) As Integer
        Try
            If nome <> "" Then
                For i As Integer = 0 To WaveInEvent.DeviceCount - 1
                    If String.Equals(WaveInEvent.GetCapabilities(i).ProductName, nome, StringComparison.OrdinalIgnoreCase) Then Return i
                Next
            End If
        Catch
        End Try
        Return 0
    End Function

    Private Function NomeMicrofono() As String
        Try
            Return WaveInEvent.GetCapabilities(_dispositivo).ProductName
        Catch
            Return "predefinito"
        End Try
    End Function

    ' ---- la riga -----------------------------------------------------------------------------------------
    ' Il controllo mostra la riga corrente: una traccia già registrata (con la durata) oppure la traccia nuova. Freccia su e
    ' freccia giù passano da una riga all'altra; la riga si annuncia a voce. Invio registra sempre una traccia nuova.

    Private Function NumeroTracce() As Integer
        SyncLock _bloccoTracce
            Return _progetto.Tracce.Count
        End SyncLock
    End Function

    Private Shared Function TestoVolume(volume As Integer) As String
        Return If(volume = 100, "", ", volume " & volume.ToString() & " per cento")
    End Function

    Private Function TestoControllo() As String
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaVocale) = _progetto.Tracce
            If _indiceRiga >= tracce.Count Then
                Dim t As String = "Nuova traccia di voce" & TestoVolume(_volumeVoce)
                If tracce.Count > 0 Then t &= ". " & If(tracce.Count = 1, "Una traccia registrata", tracce.Count.ToString() & " tracce registrate")
                Return t
            End If
            Dim traccia As TracciaVocale = tracce(_indiceRiga)
            Dim secondi As Integer = CInt(Math.Round(traccia.Secondi))
            Dim durata As String = If(secondi >= 60, (secondi \ 60).ToString() & " minuti e " & (secondi Mod 60).ToString() & " secondi", secondi.ToString() & If(secondi = 1, " secondo", " secondi"))
            Return "Traccia " & (_indiceRiga + 1).ToString() & " di " & tracce.Count.ToString() & ", voce, " & durata & TestoVolume(traccia.Volume) & If(_selezione.Contains(traccia), ", selezionata", "")
        End SyncLock
    End Function

    Private Sub RiempiLista()
        If _lista Is Nothing Then Return
        _lista.pulisci()
        _lista.aggiungiElemento(TestoControllo(), IdControllo)
        _lista.posiziona(IdControllo)
    End Sub

    Private Sub AggiornaRiga()
        Dim lista As WGMidiRiproduzione = _lista
        If lista IsNot Nothing Then lista.aggiungiElemento(TestoControllo(), IdControllo)
    End Sub

    ''' <summary>La riga si aggiorna da sola quando cambia qualcosa che la riguarda.</summary>
    Private Sub AggiornaLista()
        AggiornaRiga()
    End Sub

    ' Le opzioni di Alt: solo quelle che servono adesso. Volumi, ripetizione del sottofondo e sincronia sono nella Configurazione
    ' delle impostazioni del Registratore vocale.
    Private Sub DefinisciOpzioni()
        RileggiImpostazioni()
        _lista.pulisciOpzioni()
        Dim tracce As Integer = NumeroTracce()
        _lista.aggiungiOpzione(If(_inRegistrazione, "Ferma", "Registra"), AddressOf OpzioneRegistrazione)
        _lista.aggiungiOpzione("Stato", AddressOf OpzioneStato)
        If Not _inRegistrazione Then
            If tracce > 0 OrElse _nomeSottofondo <> "" Then
                _lista.aggiungiOpzione(If(_inProva, "Ferma ascolto", "Ascolta"), AddressOf OpzioneProva)
            End If
            If tracce > 0 Then
                _lista.aggiungiOpzione("Elimina", AddressOf OpzioneElimina)
                _lista.aggiungiOpzione("Esporta", AddressOf OpzioneEsporta)
            End If
        End If
        If _nomeSottofondo = "" Then
            If Not _inRegistrazione Then _lista.aggiungiOpzione("Sottofondo", AddressOf OpzioneScegliSottofondo)
        ElseIf Not _inRegistrazione Then
            _lista.aggiungiOpzione("Cambia sottofondo", AddressOf OpzioneCambiaSottofondo)
        End If
        If Not _inRegistrazione Then
            _lista.aggiungiOpzione("Volume", AddressOf OpzioneVolume)
            _lista.aggiungiOpzione("Microfono", AddressOf OpzioneMicrofono)
            _lista.aggiungiOpzione("Livello", AddressOf OpzioneControlloLivello)
            _lista.aggiungiOpzione("Effetti", AddressOf OpzioneEffettiVoce)
            _lista.aggiungiOpzione("Sincronia", AddressOf OpzioneSincronia)
            _lista.aggiungiOpzione("Tempo massimo", AddressOf OpzioneTempoMassimo)
            _lista.aggiungiOpzione("Tempo metronomo", AddressOf OpzioneTempoMetronomo)
            _lista.aggiungiOpzione("Battiti", AddressOf OpzioneBattiti)
            _lista.aggiungiOpzione(If(_metronomoInRegistrazione, "Senza metronomo", "Con metronomo"), AddressOf OpzioneMetronomoRegistrazione)
        End If
        If Not _inRegistrazione Then _lista.aggiungiOpzione("Configurazione", AddressOf OpzioneConfigurazione)
        _lista.aggiungiOpzione("Aiuto", AddressOf OpzioneAiuto)
    End Sub

    Private Function StatoCompleto() As String
        RileggiImpostazioni()
        Dim t As New StringBuilder()
        t.Append(If(_progetto.Nome <> "", "Progetto " & _progetto.Nome & ". ", "Progetto nuovo. "))
        t.Append(If(_inRegistrazione, "Registrazione in corso. ", "Registrazione ferma. "))
        Dim tracce As Integer = NumeroTracce()
        t.Append(If(tracce = 0, "Nessuna traccia registrata. ", If(tracce = 1, "Una traccia registrata. ", tracce.ToString() & " tracce registrate. ")))
        t.Append(TestoControllo()).Append(". ")
        If _nomeSottofondo = "" Then
            t.Append("Nessun sottofondo. ")
        Else
            t.Append("Sottofondo ").Append(_nomeSottofondo).Append(If(_ripetiSottofondo, ", ripetuto", "")).Append(If(_inProva, ", in ascolto", "")).Append(". ")
        End If
        t.Append("Microfono ").Append(NomeMicrofono()).Append(". ")
        t.Append("Effetti sulla voce: ").Append(ImpostazioniEffettiVoce.Leggi().Descrizione()).Append(". ")
        t.Append("Volume della voce ").Append(_volumeVoce.ToString()).Append(" per cento, del sottofondo ").Append(_volumeSottofondo.ToString()).Append(" per cento. ")
        t.Append("Metronomo ").Append(_tempoMetronomo.ToString()).Append(" al minuto, ").Append(_battitiMetronomo.ToString()).Append(" battiti per battuta, ").Append(If(_metronomoInRegistrazione, "si sente mentre registri. ", "non si sente mentre registri. "))
        t.Append("Sincronia: ritardo del microfono ").Append(CInt(LatenzaAudio.BaseMs()).ToString()).Append(" millisecondi, sempre corretto; regolazione fine ").Append(_compensazioneMs.ToString()).Append(" millisecondi.")
        Return t.ToString()
    End Function

    Private Sub OnVoceSelezionata(id As String)
        Try
            AvviaFermaRegistrazione()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OnVoceSelezionata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OpzioneStato(sender As Object, e As EventArgs)
        AiutoMusica.Annuncia(StatoCompleto())
    End Sub

    ''' <summary>Esc: se il progetto ha modifiche da salvare, prima si chiede che cosa fare.</summary>
    Private Sub OnEsc(sender As Object, e As EventArgs)
        Try
            If _modoControllo Then Return
            If _inRegistrazione Then
                ' la traccia si prepara in sottofondo e alla fine si chiede
                _uscitaRichiesta = True
                FermaRegistrazione()
                Return
            End If
            If _elaborazioni > 0 Then
                _uscitaRichiesta = True
                AiutoMusica.AnnunciaInSottofondo("Sto ancora preparando la traccia, poi ti chiedo se vuoi salvare")
                Return
            End If
            ChiediSalvataggio()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OnEsc: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Chiudi(True)
        End Try
    End Sub

    Private Sub ChiediSalvataggio()
        If Not _modificato OrElse NumeroTracce() = 0 Then
            Chiudi(False)
            Return
        End If
        WGDomanda.Chiedi("Vuoi salvare il progetto?", Sub() Chiudi(True), Sub() Chiudi(False))
    End Sub

    ' ---- i tasti ----------------------------------------------------------------------------------------

    Private Function Attiva() As Boolean
        Dim lista As WGMidiRiproduzione = _lista
        If _chiusa OrElse lista Is Nothing Then Return False
        Return lista.PuoRicevereInput() AndAlso Not lista.OpzioniAperte
    End Function

    ''' <summary>Se l'elenco viene chiuso da qualcos'altro, la sessione si chiude con lui.</summary>
    Private Sub ControllaFuoco(stato As Object)
        Try
            If _chiusa Then Return
            Dim lista As WGMidiRiproduzione = _lista
            If lista Is Nothing Then Return
            If lista.Chiusa Then AiutoMusica.SuInterfaccia(Sub() Chiudi())
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.ControllaFuoco: " & ex.Message)
        End Try
    End Sub

    Public Function GestisciTasto(vkCode As Integer, scanCode As Integer, premuto As Boolean, shift As Boolean, modificatori As Boolean) As Boolean Implements IGestoreTastiMusicali.GestisciTasto
        Try
            If _chiusa Then Return False
            Dim nostro As Boolean = vkCode = VkSu OrElse vkCode = VkGiu OrElse vkCode = VkCanc OrElse vkCode = VkInserisci OrElse vkCode = VkSpazio
            If Not nostro Then Return False

            If Not premuto Then
                If vkCode = VkSpazio Then _spazioPremuto = False
                If vkCode = VkCanc Then _cancPremuto = False
                If vkCode = VkInserisci Then _inserisciPremuto = False
                Return Not modificatori AndAlso Attiva()
            End If

            If modificatori OrElse _modoControllo OrElse Not Attiva() Then Return False

            Select Case vkCode
                Case VkSu
                    MuoviRiga(-1, shift)
                Case VkGiu
                    MuoviRiga(1, shift)
                Case VkCanc
                    ' il gancio della tastiera deve restituire subito
                    If Not _cancPremuto Then
                        _cancPremuto = True
                        Task.Run(Sub() EliminaTracce())
                    End If
                Case VkInserisci
                    If Not _inserisciPremuto Then
                        _inserisciPremuto = True
                        AiutoMusica.SuInterfaccia(Sub() ScegliMicrofono())
                    End If
                Case VkSpazio
                    If Not _spazioPremuto Then
                        _spazioPremuto = True
                        Task.Run(Sub() AvviaFermaProva())
                    End If
            End Select
            Return True
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.GestisciTasto: " & ex.Message)
            Return False
        End Try
    End Function

    ' ---- il menu delle modifiche (AltGr, come in Word) -----------------------------------------------------
    ' AltGr da solo apre un menu per modificare le tracce: eliminarle, spostarle, cambiarne il volume. Sopra la traccia nuova
    ' cambia solo il volume con cui si registra la voce.

    Private Sub OnAltGr(sender As Object, e As EventArgs)
        Try
            If _inRegistrazione OrElse _modoControllo OrElse _chiusa Then Return
            Dim indice As Integer
            Dim totale As Integer
            Dim selezionate As Integer
            Dim volume As Integer
            SyncLock _bloccoTracce
                indice = _indiceRiga
                totale = _progetto.Tracce.Count
                selezionate = _selezione.Count
                volume = If(indice < totale, _progetto.Tracce(indice).Volume, _volumeVoce)
            End SyncLock
            Dim suTraccia As Boolean = indice < totale
            Dim menu As New WGMenu()
            If suTraccia OrElse selezionate > 0 Then
                menu.aggiungi(If(selezionate > 1, "Elimina le tracce selezionate", "Elimina traccia"), Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() EliminaTracce()))
            End If
            If suTraccia AndAlso indice > 0 Then menu.aggiungi("Sposta traccia più in alto", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() SpostaTraccia(-1)))
            If suTraccia AndAlso indice < totale - 1 Then menu.aggiungi("Sposta traccia più in basso", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() SpostaTraccia(1)))
            menu.aggiungi("Modifica volume: " & volume.ToString() & " per cento", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() ChiediVolume(volume)))
            menu.avvia(If(suTraccia, "Modifica la traccia " & (indice + 1).ToString(), "Modifica la traccia nuova"))
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OnAltGr: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ChiudiEEsegui(menu As WGMenu, azione As Action)
        Try
            menu.chiudi()
        Catch
        End Try
        azione()
    End Sub

    Private Sub ChiediVolume(attuale As Integer)
        AiutoRevisione.ChiediTesto("Volume in percento, da 0 a 400", attuale.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 0 OrElse valore > 400 Then
                                           AiutoMusica.Messaggio("Il volume va da 0 a 400 per cento.")
                                           Return
                                       End If
                                       ImpostaVolume(valore)
                                   End Sub)
    End Sub

    Private Sub ImpostaVolume(volume As Integer)
        SyncLock _bloccoTracce
            If _indiceRiga < _progetto.Tracce.Count Then
                _progetto.Tracce(_indiceRiga).Volume = volume
                If _indiceRiga < _volumiTracce.Count Then _volumiTracce(_indiceRiga).Volume = volume / 100.0F
                _modificato = True
            Else
                _volumeVoce = volume
                ImpostazioniMusica.Scrivi("voce_volume", _volumeVoce.ToString())
            End If
        End SyncLock
        AggiornaRiga()
        AiutoMusica.Annuncia("Volume " & volume.ToString() & " per cento")
    End Sub

    ''' <summary>Scambia la traccia corrente con quella sopra (delta -1) o sotto (1); la riga la segue.</summary>
    Private Sub SpostaTraccia(delta As Integer)
        Dim spostata As Boolean = False
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaVocale) = _progetto.Tracce
            Dim nuovo As Integer = _indiceRiga + delta
            If _indiceRiga < tracce.Count AndAlso nuovo >= 0 AndAlso nuovo < tracce.Count Then
                Dim t As TracciaVocale = tracce(_indiceRiga)
                tracce(_indiceRiga) = tracce(nuovo)
                tracce(nuovo) = t
                _indiceRiga = nuovo
                _selezione.Clear()
                spostata = True
            End If
        End SyncLock
        If Not spostata Then Return
        _modificato = True
        FermaUscita()
        AggiornaRiga()
        AiutoMusica.Annuncia("Traccia spostata. " & TestoControllo())
    End Sub

    ' ---- metronomo ------------------------------------------------------------------------------------

    Private Sub OpzioneTempoMetronomo(sender As Object, e As EventArgs)
        AiutoRevisione.ChiediTesto("Tempo del metronomo, battiti al minuto, da 30 a 300", _tempoMetronomo.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 30 OrElse valore > 300 Then
                                           AiutoMusica.Messaggio("Il tempo va da 30 a 300 battiti al minuto.")
                                           Return
                                       End If
                                       _tempoMetronomo = valore
                                       ImpostazioniMusica.Scrivi("musica_metronomo_bpm", valore.ToString())
                                       AiutoMusica.Annuncia("Metronomo a " & valore.ToString() & " al minuto")
                                   End Sub)
    End Sub

    Private Sub OpzioneBattiti(sender As Object, e As EventArgs)
        AiutoRevisione.ChiediTesto("Battiti per battuta, da 1 a 12", _battitiMetronomo.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 1 OrElse valore > 12 Then
                                           AiutoMusica.Messaggio("I battiti per battuta vanno da 1 a 12.")
                                           Return
                                       End If
                                       _battitiMetronomo = valore
                                       ImpostazioniMusica.Scrivi("musica_metronomo_battiti", valore.ToString())
                                       AiutoMusica.Annuncia(valore.ToString() & If(valore = 1, " battito per battuta", " battiti per battuta"))
                                   End Sub)
    End Sub

    Private Sub OpzioneMetronomoRegistrazione(sender As Object, e As EventArgs)
        _metronomoInRegistrazione = Not _metronomoInRegistrazione
        ImpostazioniMusica.ScriviBooleano("musica_metronomo_registrazione", _metronomoInRegistrazione)
        AiutoMusica.Annuncia(If(_metronomoInRegistrazione, "Il metronomo si sentirà mentre registri", "Il metronomo non si sentirà mentre registri"))
    End Sub

    ' ---- le tracce: spostarsi, eliminare -----------------------------------------------------------------

    ''' <summary>Freccia su e giù: la riga precedente o la successiva; con Maiusc la traccia lasciata e quella raggiunta si selezionano.</summary>
    Private Sub MuoviRiga(delta As Integer, estendi As Boolean)
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaVocale) = _progetto.Tracce
            Dim righe As Integer = tracce.Count + 1
            Dim nuovo As Integer = _indiceRiga + delta
            If nuovo >= 0 AndAlso nuovo < righe Then
                If estendi Then
                    If _indiceRiga < tracce.Count Then _selezione.Add(tracce(_indiceRiga))
                    If nuovo < tracce.Count Then _selezione.Add(tracce(nuovo))
                Else
                    _selezione.Clear()
                End If
                _indiceRiga = nuovo
            End If
        End SyncLock
        Dim testo As String = TestoControllo()
        AggiornaRiga()
        AiutoMusica.AnnunciaInSottofondo(testo)
    End Sub

    Private Sub EliminaTracce()
        If _inRegistrazione OrElse _modoControllo Then
            AiutoMusica.AnnunciaInSottofondo("Non si può eliminare durante la registrazione")
            Return
        End If
        Dim eliminate As Integer
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaVocale) = _progetto.Tracce
            Dim daTogliere As List(Of TracciaVocale)
            If _selezione.Count > 0 Then
                daTogliere = tracce.Where(Function(t) _selezione.Contains(t)).ToList()
            ElseIf _indiceRiga < tracce.Count Then
                daTogliere = New List(Of TracciaVocale) From {tracce(_indiceRiga)}
            Else
                daTogliere = New List(Of TracciaVocale)()
            End If
            For Each t As TracciaVocale In daTogliere
                tracce.Remove(t)
            Next
            _selezione.Clear()
            _indiceRiga = Math.Min(_indiceRiga, tracce.Count)
            eliminate = daTogliere.Count
        End SyncLock
        If eliminate = 0 Then
            AiutoMusica.AnnunciaInSottofondo("Qui non c'è nessuna traccia da eliminare. " & TestoControllo())
            Return
        End If
        _modificato = True
        FermaUscita()
        AggiornaRiga()
        AiutoMusica.AnnunciaInSottofondo(If(eliminate = 1, "Traccia eliminata. ", eliminate.ToString() & " tracce eliminate. ") & TestoControllo())
    End Sub

    ' ---- opzioni ------------------------------------------------------------------------------------

    Private Sub OpzioneRegistrazione(sender As Object, e As EventArgs)
        AvviaFermaRegistrazione()
    End Sub

    Private Sub OpzioneCambiaSottofondo(sender As Object, e As EventArgs)
        AiutoSottofondo.MostraCambia(_nomeSottofondo <> "", Sub() ScegliSottofondo(), Sub() OpzioneRimuoviSottofondo(Nothing, Nothing))
    End Sub

    Private Sub OpzioneScegliSottofondo(sender As Object, e As EventArgs)
        ScegliSottofondo()
    End Sub

    Private Sub OpzioneProva(sender As Object, e As EventArgs)
        AvviaFermaProva()
    End Sub

    Private Sub OpzioneElimina(sender As Object, e As EventArgs)
        EliminaTracce()
    End Sub

    Private Sub OpzioneEsporta(sender As Object, e As EventArgs)
        Esporta()
    End Sub

    Private Sub OpzioneRimuoviSottofondo(sender As Object, e As EventArgs)
        If _nomeSottofondo = "" Then
            AiutoMusica.Messaggio("Non c'è nessuna traccia di sottofondo.")
            Return
        End If
        If _inRegistrazione Then
            AiutoMusica.Messaggio("Non si può togliere il sottofondo durante la registrazione.")
            Return
        End If
        FermaUscita()
        _percorsoSottofondo = ""
        _nomeSottofondo = ""
        AggiornaLista()
        AiutoMusica.Annuncia("Sottofondo rimosso")
    End Sub

    Private Sub OpzioneRipeti(sender As Object, e As EventArgs)
        _ripetiSottofondo = Not _ripetiSottofondo
        ImpostazioniMusica.ScriviBooleano("voce_ripeti_sottofondo", _ripetiSottofondo)
        If _lettoreSottofondo IsNot Nothing Then _lettoreSottofondo.Ripeti = _ripetiSottofondo
        AggiornaLista()
        AiutoMusica.Annuncia(If(_ripetiSottofondo, "Il sottofondo si ripete", "Il sottofondo non si ripete"))
    End Sub

    Private Sub OpzioneMicrofono(sender As Object, e As EventArgs)
        ScegliMicrofono()
    End Sub

    Private Sub OpzioneControlloLivello(sender As Object, e As EventArgs)
        ControllaLivello()
    End Sub

    Private Sub CambiaVolumeVoce(delta As Integer)
        _volumeVoce = Math.Max(0, Math.Min(200, _volumeVoce + delta))
        ImpostazioniMusica.Scrivi("voce_volume", _volumeVoce.ToString())
        AggiornaLista()
        AiutoMusica.Annuncia("Volume della voce " & _volumeVoce.ToString() & " per cento")
    End Sub

    Private Sub CambiaVolumeSottofondo(delta As Integer)
        _volumeSottofondo = Math.Max(0, Math.Min(100, _volumeSottofondo + delta))
        ImpostazioniMusica.Scrivi("voce_volume_sottofondo", _volumeSottofondo.ToString())
        AggiornaLista()
        AiutoMusica.Annuncia("Volume del sottofondo " & _volumeSottofondo.ToString() & " per cento")
    End Sub

    Private Sub OpzioneVoceSu(sender As Object, e As EventArgs)
        CambiaVolumeVoce(10)
    End Sub

    Private Sub OpzioneVoceGiu(sender As Object, e As EventArgs)
        CambiaVolumeVoce(-10)
    End Sub

    Private Sub OpzioneSottofondoSu(sender As Object, e As EventArgs)
        CambiaVolumeSottofondo(10)
    End Sub

    Private Sub OpzioneSottofondoGiu(sender As Object, e As EventArgs)
        CambiaVolumeSottofondo(-10)
    End Sub

    Private Sub CambiaCompensazione(delta As Integer)
        _compensazioneMs = Math.Max(-500, Math.Min(500, _compensazioneMs + delta))
        ImpostazioniMusica.Scrivi("voce_compensazione_ms", _compensazioneMs.ToString())
        AggiornaLista()
        AiutoMusica.Annuncia("Compensazione " & _compensazioneMs.ToString() & " millisecondi")
    End Sub

    Private Sub OpzioneAnticipa(sender As Object, e As EventArgs)
        CambiaCompensazione(5)
    End Sub

    Private Sub OpzioneRitarda(sender As Object, e As EventArgs)
        CambiaCompensazione(-5)
    End Sub

    Private Sub OpzioneVolume(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.MostraVolume()
    End Sub

    ''' <summary>La sincronia della voce: si misura (calibrazione), si regola a mano in millisecondi o si disattiva (0 millisecondi).</summary>
    Private Sub OpzioneSincronia(sender As Object, e As EventArgs)
        Try
            Dim valore As Integer = ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0)
            Dim menu As New WGMenu()
            menu.aggiungi("Calibra", Sub()
                                                      ChiudiEEsegui(menu, Sub() CalibratoreSincronia.Esegui())
                                                  End Sub)
            menu.aggiungi("Regola a mano", Sub()
                                                              ChiudiEEsegui(menu, Sub() ConfigurazioneMusica.ScegliNumero("Regolazione fine della sincronia", "voce_compensazione_ms", 0, -200, 200, 5, " millisecondi"))
                                                          End Sub)
            If valore <> 0 Then
                menu.aggiungi("Azzera", Sub()
                                                            ImpostazioniMusica.Scrivi("voce_compensazione_ms", "0")
                                                            _compensazioneMs = 0
                                                            ChiudiEEsegui(menu, Sub() AiutoMusica.Messaggio("Regolazione fine azzerata: la voce resta allineata con il ritardo misurato."))
                                                        End Sub)
            End If
            menu.avvia("Sincronia, regolazione fine ora " & valore.ToString() & " millisecondi")
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OpzioneSincronia: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneTempoMassimo(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.ScegliLimite("voce_max_secondi")
    End Sub

    Private Sub OpzioneConfigurazione(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.Mostra()
    End Sub

    Private Sub OpzioneEffettiVoce(sender As Object, e As EventArgs)
        ConfigurazioneRegistratore.MostraEffettiVoce()
    End Sub

    Private Sub OpzioneCalibra(sender As Object, e As EventArgs)
        CalibratoreSincronia.Esegui()
    End Sub

    Private Sub OpzioneAiuto(sender As Object, e As EventArgs)
        MostraAiuto()
    End Sub

    Private Sub MostraAiuto()
        AiutoMusica.Messaggio("Il registratore vocale registra la tua voce dal microfono. Ogni registrazione è una traccia del progetto. " &
                              "Premi Invio per iniziare e per fermare la registrazione di una traccia nuova: mentre registri senti le tracce già fatte e il sottofondo, se l'hai scelto, e la voce finisce al suo posto, senza ritardi. " &
                              "Per non far tornare nel microfono quello che senti, usa le cuffie. " &
                              "Freccia su e freccia giù passano da una traccia all'altra; l'ultima riga è la traccia nuova. Con Maiusc e le frecce selezioni più tracce, con Canc le elimini. " &
                              "La barra spaziatrice ascolta tutte le tracce insieme. Inserisci sceglie il microfono. AltGr da solo apre il menu delle modifiche: elimina traccia, sposta traccia e modifica volume, della traccia corrente o, sulla traccia nuova, della voce che stai per registrare. " &
                              "Quando avvii la registrazione senti solo un beep, senza parole. Da Alt, Esporta, scrivi le tracce unite in un file mp3 o wav, nella cartella che scegli. " &
                              "Dal menu Alt trovi il metronomo: puoi scegliere il tempo e i battiti per battuta e farlo sentire mentre registri. " &
                              "Con Alt apri le opzioni: ascolto, eliminazione, esportazione in mp3 o wav, sottofondo, microfono, livello del microfono e calibrazione della sincronia. " &
                              "Quando esci, il progetto si salva con il nome che scegli, e lo ritrovi aprendo il registratore, in I miei progetti. " &
                              "Microfono, effetti sulla voce, volumi, ripetizione del sottofondo e sincronia a mano si cambiano da Impostazioni, Applicazioni, Registratore vocale. " &
                              "Se la voce risulta un po' in anticipo o in ritardo rispetto al sottofondo, usa Calibra la sincronia, con gli altoparlanti vicino al microfono.")
    End Sub

    ' ---- microfono ----------------------------------------------------------------------------------

    Private Sub ScegliMicrofono()
        Try
            If _inRegistrazione Then
                AiutoMusica.Messaggio("Non si può cambiare il microfono durante la registrazione.")
                Return
            End If
            Dim menu As New WGMenu()
            For i As Integer = 0 To WaveInEvent.DeviceCount - 1
                Dim indice As Integer = i
                Dim nome As String = WaveInEvent.GetCapabilities(i).ProductName
                menu.aggiungi(nome, Sub(sender As Object, e As EventArgs)
                                        _dispositivo = indice
                                        ImpostazioniMusica.Scrivi("voce_microfono", nome)
                                        AggiornaLista()
                                        AiutoMusica.Annuncia("Microfono scelto: " & nome)
                                    End Sub)
            Next
            menu.posiziona(Math.Max(0, _dispositivo))
            menu.avvia("Scegli il microfono")
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.ScegliMicrofono: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre il microfono e comincia a catturare, a 48000 Hz o, se la scheda non li accetta, a 44100 Hz.</summary>
    Private Function ApriCattura(durataBloccoMs As Integer) As WaveInEvent
        Dim ultimoErrore As Exception = Nothing
        For Each frequenza As Integer In New Integer() {48000, 44100}
            Dim ingresso As New WaveInEvent()
            ingresso.DeviceNumber = _dispositivo
            ingresso.BufferMilliseconds = durataBloccoMs
            ingresso.NumberOfBuffers = 4
            ingresso.WaveFormat = New WaveFormat(frequenza, 16, 1)
            _frequenzaCatturaEffettiva = frequenza
            AddHandler ingresso.DataAvailable, AddressOf OnDatiCatturati
            Try
                ingresso.StartRecording()
                Return ingresso
            Catch ex As Exception
                ultimoErrore = ex
                RemoveHandler ingresso.DataAvailable, AddressOf OnDatiCatturati
                Try
                    ingresso.Dispose()
                Catch
                End Try
            End Try
        Next
        Throw ultimoErrore
    End Function

    Private Sub ChiudiCattura()
        Try
            If _ingresso IsNot Nothing Then
                RemoveHandler _ingresso.DataAvailable, AddressOf OnDatiCatturati
                Try
                    _ingresso.StopRecording()
                Catch
                End Try
                _ingresso.Dispose()
                _ingresso = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.ChiudiCattura: " & ex.Message)
        End Try
    End Sub

    Private Sub OnDatiCatturati(sender As Object, e As WaveInEventArgs)
        Try
            Dim byteLetti As Integer = e.BytesRecorded
            If byteLetti <= 0 Then Return
            For i As Integer = 0 To byteLetti - 2 Step 2
                Dim v As Single = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768.0F)
                If v > _picco Then _picco = v
            Next
            If Not _inRegistrazione Then Return
            _memoriaVoce.Write(e.Buffer, 0, byteLetti)
            _campioniCatturati += byteLetti \ 2
            Dim uscita As WaveOutEvent = _uscita
            If uscita IsNot Nothing AndAlso uscita.PlaybackState = PlaybackState.Playing Then
                Dim posizione As Long = uscita.GetPosition() \ 8
                If posizione > _ultimaPosizione AndAlso posizione >= 4800 Then
                    _ultimaPosizione = posizione
                    SyncLock _bloccoPunti
                        _puntiVoce.Add(_campioniCatturati)
                        _puntiSottofondo.Add(posizione)
                    End SyncLock
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OnDatiCatturati: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Ascolta il microfono per tre secondi e dice se il livello va bene.</summary>
    Private Sub ControllaLivello()
        Try
            If _inRegistrazione OrElse _modoControllo Then Return
            _modoControllo = True
            _picco = 0.0F
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Parla come durante la registrazione, ascolto il microfono per tre secondi", False)
            Dim ingresso As WaveInEvent = Nothing
            Try
                ingresso = ApriCattura(20)
            Catch ex As Exception
                _modoControllo = False
                attesa.Fine()
                _log.ScriviLog("SessioneRegistratoreVocale.ControllaLivello: " & ex.Message)
                AiutoMusica.Messaggio("Non è stato possibile usare il microfono.")
                Return
            End Try
            _ingresso = ingresso
            Task.Run(Sub()
                         Thread.Sleep(3000)
                         Dim picco As Single = _picco
                         AiutoMusica.SuInterfaccia(Sub()
                                                       ChiudiCattura()
                                                       _modoControllo = False
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       Dim db As Double = If(picco > 0.00001F, 20.0 * Math.Log10(picco), -90.0)
                                                       Dim giudizio As String
                                                       If picco < 0.02F Then
                                                           giudizio = "Non sento quasi niente: il microfono è spento, troppo lontano o con il volume troppo basso."
                                                       ElseIf picco < 0.1F Then
                                                           giudizio = "Il livello è troppo basso: avvicinati al microfono o alza il volume del microfono in Windows."
                                                       ElseIf picco > 0.95F Then
                                                           giudizio = "Il livello è troppo alto e il suono si rovina: allontanati dal microfono o abbassa il volume del microfono."
                                                       Else
                                                           giudizio = "Il livello è buono."
                                                       End If
                                                       AiutoMusica.Messaggio(giudizio & " Il picco è " & CInt(db).ToString() & " decibel.")
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _modoControllo = False
            _log.ScriviLog("SessioneRegistratoreVocale.ControllaLivello: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' ---- sottofondo ---------------------------------------------------------------------------------

    Private Sub ScegliSottofondo()
        Try
            If _inRegistrazione Then
                AiutoMusica.Messaggio("Non si può cambiare il sottofondo durante la registrazione.")
                Return
            End If
            _dialogoSottofondo = New WGFileDialog()
            _dialogoSottofondo.MultiselezioneConsentita = False
            For Each estensione As String In New String() {".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".aif", ".aiff", ".mp2"}
                _dialogoSottofondo.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            _dialogoSottofondo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.ScegliSottofondo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnSottofondoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            ' si prova ad aprirlo subito: se non va, lo si dice adesso e non alla fine della registrazione
            Try
                Using prova As New SottofondoAudio(percorso)
                End Using
            Catch ex As Exception
                AiutoMusica.Messaggio("Non è stato possibile aprire il file audio: " & ex.Message)
                Return
            End Try
            FermaUscita()
            _percorsoSottofondo = percorso
            _nomeSottofondo = Path.GetFileNameWithoutExtension(percorso)
            AggiornaLista()
            AiutoMusica.Annuncia("Sottofondo scelto: " & _nomeSottofondo)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.OnSottofondoScelto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function HaAudioDaSentire() As Boolean
        Return _nomeSottofondo <> "" OrElse NumeroTracce() > 0
    End Function

    ''' <summary>
    ''' Prepara la riproduzione, dall'inizio: il sottofondo e le tracce già registrate insieme. Mentre si registra l'uscita non si
    ''' ferma quando le tracce finiscono, così la posizione che serve ad allineare la voce continua a correre.
    ''' </summary>
    Private Sub AvviaUscita(registrando As Boolean)
        FermaUscita()
        Dim mix As New MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        mix.ReadFully = registrando
        If _percorsoSottofondo <> "" Then
            _lettoreSottofondo = New SottofondoAudio(_percorsoSottofondo)
            _lettoreSottofondo.Ripeti = _ripetiSottofondo
            Dim volume As New VolumeSampleProvider(_lettoreSottofondo)
            volume.Volume = (_volumeSottofondo / 100.0F) * (_volumeSottofondo / 100.0F)
            mix.AddMixerInput(volume)
        End If
        Dim tracce As List(Of TracciaVocale)
        SyncLock _bloccoTracce
            tracce = New List(Of TracciaVocale)(_progetto.Tracce)
        End SyncLock
        _volumiTracce.Clear()
        For Each traccia As TracciaVocale In tracce
            Dim lettore As New SottofondoAudio(traccia.Percorso)
            _lettoriTracce.Add(lettore)
            Dim volumeTraccia As New VolumeSampleProvider(lettore)
            volumeTraccia.Volume = traccia.Volume / 100.0F
            _volumiTracce.Add(volumeTraccia)
            mix.AddMixerInput(volumeTraccia)
        Next
        If registrando AndAlso _metronomoInRegistrazione Then
            Dim metronomo As New GeneratoreMetronomo()
            metronomo.Bpm = _tempoMetronomo
            metronomo.Battiti = _battitiMetronomo
            metronomo.Attivo = True
            mix.AddMixerInput(metronomo)
        End If
        _ultimaPosizione = 0
        _uscita = New WaveOutEvent()
        _uscita.DesiredLatency = 100
        _uscita.NumberOfBuffers = 3
        _uscita.Init(New SampleToWaveProvider(New LimitatoreMorbido(mix)))
        AddHandler _uscita.PlaybackStopped, AddressOf OnUscitaFerma
    End Sub

    Private Sub OnUscitaFerma(sender As Object, e As StoppedEventArgs)
        If _inProva AndAlso Not _inRegistrazione Then
            _inProva = False
            AggiornaLista()
            If Not _chiusa Then AiutoMusica.AnnunciaInSottofondo("L'ascolto è finito")
        End If
    End Sub

    Private Sub FermaUscita()
        Try
            _inProva = False
            If _uscita IsNot Nothing Then
                RemoveHandler _uscita.PlaybackStopped, AddressOf OnUscitaFerma
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
            For Each lettore As SottofondoAudio In _lettoriTracce
                Try
                    lettore.Dispose()
                Catch
                End Try
            Next
            _lettoriTracce.Clear()
            _volumiTracce.Clear()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.FermaUscita: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Ascolta le tracce registrate e il sottofondo insieme, dall'inizio; di nuovo, si ferma.</summary>
    Private Sub AvviaFermaProva()
        Try
            If _inRegistrazione OrElse _modoControllo Then Return
            If _inProva Then
                FermaUscita()
                AggiornaLista()
                AiutoMusica.AnnunciaInSottofondo("Ascolto fermo")
                Return
            End If
            RileggiImpostazioni()
            If Not HaAudioDaSentire() Then
                AiutoMusica.AnnunciaInSottofondo("Non ci sono tracce né sottofondo da ascoltare")
                Return
            End If
            AvviaUscita(False)
            _uscita.Play()
            _inProva = True
            AggiornaLista()
            AiutoMusica.AnnunciaInSottofondo(If(NumeroTracce() > 0, "Ascolto delle tracce", "Ascolto del sottofondo"))
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.AvviaFermaProva: " & ex.Message)
            AiutoMusica.Messaggio("Non è stato possibile riprodurre l'audio.")
        End Try
    End Sub

    ' ---- registrazione ------------------------------------------------------------------------------

    Private Sub AvviaFermaRegistrazione()
        Try
            If _modoControllo Then Return
            If _inRegistrazione Then
                FermaRegistrazione()
                Return
            End If
            If _elaborazioni > 0 Then
                AiutoMusica.AnnunciaInSottofondo("Sto ancora preparando la traccia di prima")
                Return
            End If
            IniziaRegistrazione()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.AvviaFermaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile avviare la registrazione.")
        End Try
    End Sub

    Private Sub IniziaRegistrazione()
        RileggiImpostazioni()
        IniziaRegistrazioneAudio()
    End Sub

    Private Sub IniziaRegistrazioneAudio()
        FermaUscita()
        ' un beep, senza parole: chi canta non deve essere distratto. Si sente prima che il microfono si apra, quindi non finisce nella traccia.
        AiutoMusica.SegnaleInizio()
        _memoriaVoce = New MemoryStream()
        _campioniCatturati = 0
        _picco = 0.0F
        SyncLock _bloccoPunti
            _puntiVoce.Clear()
            _puntiSottofondo.Clear()
        End SyncLock
        SyncLock _bloccoTracce
            _selezione.Clear()
            _indiceRiga = _progetto.Tracce.Count
        End SyncLock
        _ultimaPosizione = 0
        Dim conUscita As Boolean = HaAudioDaSentire() OrElse _metronomoInRegistrazione
        Dim conTracce As Boolean = NumeroTracce() > 0
        Try
            If conUscita Then AvviaUscita(True)
            _inRegistrazione = True
            _ingresso = ApriCattura(10)
            If conUscita Then _uscita.Play()
            AvviaLimite()
        Catch ex As Exception
            _inRegistrazione = False
            ChiudiCattura()
            FermaUscita()
            _log.ScriviLog("SessioneRegistratoreVocale.IniziaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile usare il microfono.")
            Return
        End Try
        AggiornaRiga()
    End Sub

    ''' <summary>
    ''' Ferma la cattura e prepara la traccia in sottofondo: la voce, al suo posto rispetto a quello che si sentiva (sottofondo e
    ''' tracce precedenti), in un file wav. Non salva il progetto: si salva quando si esce, e si esporta quando si vuole.
    ''' </summary>
    ''' <summary>Se è stato impostato un tempo massimo, la registrazione si ferma da sola quando lo raggiunge: come premere lo stop, senza perdere niente.</summary>
    Private Sub AvviaLimite()
        Try
            FermaLimite()
            _fermataPerLimite = False
            Dim secondi As Integer = ConfigurazioneRegistratore.SecondiLimite("voce_max_secondi")
            If secondi <= 0 Then Return
            Dim id As Integer = Interlocked.Increment(_idRegistrazione)
            _timerLimite = New System.Threading.Timer(Sub(stato As Object)
                                                          AiutoMusica.SuInterfaccia(Sub()
                                                                                        If _chiusa OrElse Not _inRegistrazione OrElse _modoControllo OrElse id <> _idRegistrazione Then Return
                                                                                        _fermataPerLimite = True
                                                                                        FermaRegistrazione()
                                                                                    End Sub)
                                                      End Sub, Nothing, secondi * 1000L, Timeout.Infinite)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.AvviaLimite: " & ex.Message)
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

    Private Sub FermaRegistrazione()
        If Not _inRegistrazione Then Return
        FermaLimite()
        _inRegistrazione = False
        ChiudiCattura()
        Dim usciva As Boolean = _uscita IsNot Nothing
        FermaUscita()
        Dim voce() As Byte = _memoriaVoce.ToArray()
        _memoriaVoce = Nothing
        Dim punteggiVoce As List(Of Long)
        Dim punteggiSottofondo As List(Of Long)
        SyncLock _bloccoPunti
            punteggiVoce = New List(Of Long)(_puntiVoce)
            punteggiSottofondo = New List(Of Long)(_puntiSottofondo)
        End SyncLock
        AggiornaRiga()

        Dim campioni(voce.Length \ 2 - 1) As Short
        System.Buffer.BlockCopy(voce, 0, campioni, 0, campioni.Length * 2)
        Dim frequenza As Integer = _frequenzaCatturaEffettiva
        Dim effetti As ImpostazioniEffettiVoce = ImpostazioniEffettiVoce.Leggi()
        If _nomeSottofondo <> "" Then effetti.Sottofondo = _percorsoSottofondo
        Dim volumeVoce As Double = _volumeVoce / 100.0
        ' la latenza vera del percorso audio si sottrae sempre (ma solo se la voce si è registrata sopra qualcosa che si sentiva); i millisecondi dell'utente sono una regolazione fine in più
        Dim compensazione As Double = If(usciva, LatenzaAudio.BaseMs(), 0.0) + _compensazioneMs
        Dim temporaneo As String = Path.Combine(Path.GetTempPath(), "wg-voce-" & Guid.NewGuid().ToString("N") & ".wav")
        Dim traccia As String = Path.Combine(Path.GetTempPath(), "wg-voce-traccia-" & Guid.NewGuid().ToString("N") & ".wav")
        _elaborazioni += 1
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto preparando la traccia, attendere", False)
        Task.Run(Sub()
                     Dim esito As String = ""
                     Dim fotogrammi As Long = 0
                     Try
                         If campioni.Length < frequenza \ 5 Then
                             esito = "La registrazione era troppo breve."
                         Else
                             ' gli effetti non spostano la voce nel tempo: la sincronia con sottofondo e video resta quella misurata
                             If effetti.Attivo Then campioni = EffettiVoce.Applica(campioni, frequenza, effetti)
                             Dim a As Double = WGDsp.FC / frequenza
                             Dim d As Double = 0.0
                             If usciva AndAlso punteggiVoce.Count > 0 Then AllineatoreVoce.RettaDiAllineamento(punteggiVoce, punteggiSottofondo, frequenza, a, d)
                             esito = AllineatoreVoce.Unisci(campioni, campioni.Length, frequenza, a, d, compensazione, "", False, volumeVoce, 0.0, temporaneo, True)
                             If esito = "" Then
                                 If WGEsportazioneAudio.ConvertiInWav16(temporaneo, traccia) Then
                                     Using lettore As New WaveFileReader(traccia)
                                         fotogrammi = lettore.Length \ lettore.WaveFormat.BlockAlign
                                     End Using
                                 Else
                                     esito = "Non è stato possibile scrivere la traccia."
                                 End If
                             End If
                         End If
                     Catch ex As Exception
                         _log.ScriviLog("SessioneRegistratoreVocale.FermaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         esito = "Non è stato possibile preparare la traccia."
                     Finally
                         Try
                             If File.Exists(temporaneo) Then File.Delete(temporaneo)
                         Catch
                         End Try
                     End Try
                     AiutoMusica.SuInterfaccia(Sub() TracciaPronta(attesa, esito, traccia, fotogrammi))
                 End Sub)
    End Sub

    Private Sub TracciaPronta(attesa As WGProcessoInCorso, esito As String, percorso As String, fotogrammi As Long)
        Try
            Try
                attesa.Fine()
            Catch
            End Try
            _elaborazioni -= 1
            If esito <> "" Then
                Try
                    If File.Exists(percorso) Then File.Delete(percorso)
                Catch
                End Try
                If Not _chiusa Then AiutoMusica.Messaggio(esito)
                If _chiusa AndAlso _elaborazioni = 0 Then SalvaProgettoAllUscita()
                If Not _chiusa AndAlso _uscitaRichiesta AndAlso _elaborazioni = 0 Then
                    _uscitaRichiesta = False
                    ChiediSalvataggio()
                End If
                Return
            End If
            _fileTemporanei.Add(percorso)
            Dim numero As Integer
            SyncLock _bloccoTracce
                _progetto.Tracce.Add(New TracciaVocale() With {.Percorso = percorso, .Fotogrammi = fotogrammi})
                numero = _progetto.Tracce.Count
                _selezione.Clear()
                _indiceRiga = _progetto.Tracce.Count
            End SyncLock
            _modificato = True
            If _chiusa Then
                If _elaborazioni = 0 Then SalvaProgettoAllUscita()
                Return
            End If
            AggiornaRiga()
            Dim perLimite As String = If(_fermataPerLimite, "Tempo massimo raggiunto: la registrazione si è fermata da sola. ", "")
            _fermataPerLimite = False
            AiutoMusica.Annuncia(perLimite & "Traccia " & numero.ToString() & " registrata. " & TestoControllo())
            If _uscitaRichiesta AndAlso _elaborazioni = 0 Then
                _uscitaRichiesta = False
                ChiediSalvataggio()
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.TracciaPronta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' ---- esportazione -----------------------------------------------------------------------------------
    ' Si sceglie la cartella (partendo dalla scelta delle risorse, senza nessuna cartella predefinita), poi il nome e il formato.

    Private Sub Esporta()
        Try
            If _inRegistrazione OrElse _modoControllo Then Return
            RileggiImpostazioni()
            If NumeroTracce() = 0 Then
                AiutoMusica.Messaggio("Non ci sono tracce da esportare.")
                Return
            End If
            _selettoreCartella = New SelettoreCartellaMusica()
            _selettoreCartella.Scegli(Sub(cartella As String) AiutoMusica.SuInterfaccia(Sub() ChiediNomeEsportazione(cartella)))
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.Esporta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediNomeEsportazione(cartella As String)
        Dim predefinito As String = If(_progetto.Nome <> "", _progetto.Nome, "Registrazione vocale")
        AiutoRevisione.ChiediTesto("Nome del file", predefinito, Sub(testo As String)
                                                                     Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                                                     If nome = "" Then nome = AiutoMusica.NomeFileSicuro(predefinito)
                                                                     ScegliFormatoEsportazione(cartella, nome)
                                                                 End Sub)
    End Sub

    Private Sub ScegliFormatoEsportazione(cartella As String, nome As String)
        Try
            Dim menu As New WGMenu()
            For Each formato As String In New String() {"mp3", "wav"}
                Dim scelto As String = formato
                menu.aggiungi(scelto, Sub(s As Object, ev As EventArgs)
                                          Try
                                              menu.chiudi()
                                          Catch
                                          End Try
                                          EseguiEsportazione(cartella, nome, scelto)
                                      End Sub)
            Next
            menu.avvia("Formato di " & nome)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.ScegliFormatoEsportazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PercorsoLibero(cartella As String, nome As String, estensione As String) As String
        Dim percorso As String = Path.Combine(cartella, nome & estensione)
        Dim n As Integer = 2
        While File.Exists(percorso)
            percorso = Path.Combine(cartella, nome & " (" & n.ToString() & ")" & estensione)
            n += 1
        End While
        Return percorso
    End Function

    ''' <summary>Unisce le tracce (e il sottofondo, se c'è) in sottofondo; alla fine dice dove ha scritto il file.</summary>
    Private Sub EseguiEsportazione(cartella As String, nome As String, formato As String)
        Try
            Dim percorsi As List(Of String)
            Dim guadagni As List(Of Double)
            Dim fotogrammi As Long
            SyncLock _bloccoTracce
                percorsi = _progetto.Tracce.Select(Function(t) t.Percorso).ToList()
                guadagni = _progetto.Tracce.Select(Function(t) t.Volume / 100.0).ToList()
                fotogrammi = _progetto.Durata
            End SyncLock
            Dim sottofondo As String = If(_nomeSottofondo <> "", _percorsoSottofondo, "")
            Dim ripetuto As Boolean = _ripetiSottofondo
            Dim guadagno As Single = CSng((_volumeSottofondo / 100.0) * (_volumeSottofondo / 100.0))
            Dim mp3 As Boolean = formato = "mp3"
            Dim destinazione As String = PercorsoLibero(cartella, nome, If(mp3, ".mp3", ".wav"))
            Dim temporaneo As String = Path.Combine(Path.GetTempPath(), "wg-voce-mix-" & Guid.NewGuid().ToString("N") & ".wav")
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Sto esportando, attendere", False)
            Task.Run(Sub()
                         Dim esito As String
                         Try
                             Dim problema As String = AllineatoreVoce.UnisciTracce(percorsi, guadagni, fotogrammi, sottofondo, ripetuto, guadagno, temporaneo)
                             If problema <> "" Then
                                 esito = problema
                             ElseIf mp3 Then
                                 Dim messaggio As String = ""
                                 If WGEsportazioneAudio.ConvertiInMp3(temporaneo, destinazione, messaggio) Then
                                     esito = "Esportato in " & destinazione & "."
                                 Else
                                     Dim alternativo As String = Path.ChangeExtension(destinazione, ".wav")
                                     esito = If(WGEsportazioneAudio.ConvertiInWav16(temporaneo, alternativo), messaggio & " L'ho salvato come file wav in " & alternativo & ".", messaggio & " Non è stato possibile salvare.")
                                 End If
                             Else
                                 esito = If(WGEsportazioneAudio.ConvertiInWav16(temporaneo, destinazione), "Esportato in " & destinazione & ".", "Non è stato possibile scrivere il file wav.")
                             End If
                         Catch ex As Exception
                             _log.ScriviLog("SessioneRegistratoreVocale.EseguiEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                             esito = "Non è stato possibile esportare: " & ex.Message
                         Finally
                             Try
                                 If File.Exists(temporaneo) Then File.Delete(temporaneo)
                             Catch
                             End Try
                         End Try
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       AiutoMusica.Messaggio(esito)
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.EseguiEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' ---- chiusura -----------------------------------------------------------------------------------

    Public Sub Chiudi(Optional salva As Boolean = True)
        If _chiusa Then Return
        _salvaAllUscita = salva
        Try
            ' la registrazione in corso non si perde: diventa l'ultima traccia
            If _inRegistrazione AndAlso Not _modoControllo Then FermaRegistrazione()
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.Chiudi (registrazione): " & ex.Message)
        End Try
        _chiusa = True
        Try
            Try
                WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Nothing)
            Catch
            End Try
            If _timerFuoco IsNot Nothing Then _timerFuoco.Dispose()
            _timerFuoco = Nothing
            _inRegistrazione = False
            ChiudiCattura()
            FermaUscita()
            If _lista IsNot Nothing Then
                Try
                    RemoveHandler _lista.escPremuto, AddressOf OnEsc
                    RemoveHandler _lista.ElementoSelezionato, AddressOf OnVoceSelezionata
                    RemoveHandler _lista.AltGrPremuto, AddressOf OnAltGr
                    _lista.abbandona()
                Catch ex As Exception
                    _log.ScriviLog("SessioneRegistratoreVocale.Chiudi (lista): " & ex.Message)
                End Try
                _lista = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.Chiudi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        ' se una traccia si sta ancora preparando, il progetto si salva quando è pronta
        If _elaborazioni = 0 Then SalvaProgettoAllUscita()
    End Sub

    ''' <summary>I file temporanei si tolgono con calma: qualcuno potrebbe averli ancora aperti per un momento.</summary>
    Private Shared Sub CancellaFile(percorsi As List(Of String))
        Task.Run(Sub()
                     Thread.Sleep(1500)
                     For Each p As String In percorsi
                         Try
                             If File.Exists(p) Then File.Delete(p)
                         Catch
                         End Try
                     Next
                 End Sub)
    End Sub

    ''' <summary>
    ''' All'uscita, se ci sono tracce e qualcosa è cambiato, il progetto si salva subito con un nome provvisorio (così non si perde
    ''' niente) e poi si chiede il nome all'utente: il progetto prende quel nome.
    ''' </summary>
    Private Sub SalvaProgettoAllUscita()
        Dim temporanei As New List(Of String)(_fileTemporanei)
        Try
            If Not _salvaAllUscita OrElse Not _modificato OrElse _progetto.Tracce.Count = 0 Then
                CancellaFile(temporanei)
                Return
            End If
            _progetto.Sottofondo = ""
            _progetto.SottofondoRipetuto = False
            Dim nomeAttuale As String = _progetto.Nome
            Dim aperta As String = _progetto.Cartella
            Dim provvisoria As String = If(aperta <> "", aperta, ProgettoVocale.CartellaDi("Registrazione senza nome " & DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss")))
            If nomeAttuale = "" Then _progetto.Nome = Path.GetFileName(provvisoria)
            _progetto.Salva(provvisoria)
            CancellaFile(temporanei)
            Dim progetto As ProgettoVocale = _progetto
            AiutoMusica.SuInterfaccia(Sub()
                                          AiutoRevisione.ChiediTesto("Nome del progetto", If(nomeAttuale <> "", nomeAttuale, ""),
                                                                     Sub(testo As String)
                                                                         Try
                                                                             Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                                                             If nome = "" Then Return
                                                                             Dim destinazione As String = ProgettoVocale.CartellaDi(nome)
                                                                             If Not String.Equals(Path.GetFullPath(destinazione).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(provvisoria).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
                                                                                 Dim n As Integer = 2
                                                                                 Dim base As String = nome
                                                                                 While Directory.Exists(destinazione)
                                                                                     nome = base & " (" & n.ToString() & ")"
                                                                                     destinazione = ProgettoVocale.CartellaDi(nome)
                                                                                     n += 1
                                                                                 End While
                                                                                 Directory.Move(provvisoria, destinazione)
                                                                             End If
                                                                             Dim salvato As ProgettoVocale = ProgettoVocale.Carica(destinazione)
                                                                             salvato.Nome = nome
                                                                             salvato.Salva(destinazione)
                                                                             AiutoMusica.Annuncia("Progetto salvato con il nome " & nome)
                                                                         Catch ex As Exception
                                                                             _log.ScriviLog("SessioneRegistratoreVocale.SalvaProgettoAllUscita (nome): " & ex.Message)
                                                                             AiutoMusica.Messaggio("Non è stato possibile dare il nome al progetto: " & ex.Message)
                                                                         End Try
                                                                     End Sub)
                                      End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneRegistratoreVocale.SalvaProgettoAllUscita: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile salvare il progetto: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub

End Class

''' <summary>Il menu del registratore vocale: una registrazione nuova o uno dei progetti salvati.</summary>
Public Class MenuRegistratoreVocale

    Private ReadOnly _log As New Logger()

    Public Sub Avvia()
        Try
            If ProgettoVocale.Elenco().Count = 0 Then
                ApriNuovo()
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Registrazione nuova", Sub(s As Object, e As EventArgs)
                                                     Try
                                                         menu.chiudi()
                                                     Catch
                                                     End Try
                                                     ApriNuovo()
                                                 End Sub)
            menu.aggiungi("I miei progetti", AddressOf ApriProgetti)
            menu.avvia("Registratore vocale")
        Catch ex As Exception
            _log.ScriviLog("MenuRegistratoreVocale.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriNuovo()
        Dim sessione As New SessioneRegistratoreVocale()
        sessione.Avvia()
    End Sub

    Private Sub ApriProgetti(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            For Each cartella As String In ProgettoVocale.Elenco()
                Dim percorso As String = cartella
                menu.aggiungi(Path.GetFileName(percorso), Sub(s As Object, ev As EventArgs) ApriProgetto(percorso))
            Next
            menu.avvia("I miei progetti")
        Catch ex As Exception
            _log.ScriviLog("MenuRegistratoreVocale.ApriProgetti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriProgetto(cartella As String)
        Try
            Dim progetto As ProgettoVocale = ProgettoVocale.Carica(cartella)
            Dim sessione As New SessioneRegistratoreVocale(progetto)
            sessione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuRegistratoreVocale.ApriProgetto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il progetto: " & ex.Message)
        End Try
    End Sub

End Class
