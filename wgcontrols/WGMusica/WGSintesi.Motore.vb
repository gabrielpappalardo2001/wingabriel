' ============================================================================================
' WGSintesi.Motore - il motore audio degli strumenti musicali
' ============================================================================================
'
' Il motore produce il suono a 48000 Hz in stereo con numeri a virgola mobile e lo manda alla scheda audio con
' bassa latenza. I tasti arrivano dal gancio della tastiera di WinGabriel (WGTastiera) e diventano note.
' Il sottofondo (una traccia audio) e la registrazione stanno nello stesso motore, quindi seguono lo stesso orologio:
' nella registrazione il sottofondo è esattamente dove lo si è sentito suonare, senza nessuna deriva.
'
'   MotoreSintesi         voci, riverbero, sottofondo, limitatore e registrazione
'   SottofondoAudio       la traccia di accompagnamento, portata a 48000 Hz stereo
'   RegistratoreFlusso    scrive su un file temporaneo quello che il motore produce
'   WGEsportazioneAudio   dal file temporaneo all'mp3 ad alta qualità
' ============================================================================================
Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.MediaFoundation
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

''' <summary>Uno strumento: come si chiama e come si costruisce la voce di una nota.</summary>
Public NotInheritable Class DefinizioneStrumento

    Public Nome As String
    Public Descrizione As String
    ''' <summary>Costruisce la voce: nota MIDI (o numero del pezzo per la batteria) e velocità da 0 a 1.</summary>
    Public Crea As Func(Of Integer, Double, VoceSintesi)
    Public OttavaIniziale As Integer = 4
    ''' <summary>Batteria e percussioni: i tasti sono pezzi diversi e l'ottava non conta.</summary>
    Public Percussivo As Boolean
    ''' <summary>Quanto riverbero ha lo strumento, da 0 a 1.</summary>
    Public Riverbero As Double = 0.2
    Public Livello As Double = 1.0
    Public MassimoVoci As Integer = 24
    ''' <summary>Dove sta lo strumento: "sf" (suoni ad alta qualità), "orchestra" o "sintetico". Serve a ritrovarlo quando si riapre un progetto.</summary>
    Public Origine As String = ""
    ''' <summary>Lo strumento General MIDI (da 0 a 127), se si conosce; -1 altrimenti.</summary>
    Public Programma As Integer = -1
    ''' <summary>Per le batterie: il nome di ogni pezzo, in ordine di tasto.</summary>
    Public NomiPezzi As String()

End Class

Public NotInheritable Class CategoriaStrumenti

    Public Nome As String
    Public Descrizione As String
    Public Strumenti As New List(Of DefinizioneStrumento)()

End Class

''' <summary>Riverbero a camera (Freeverb): otto filtri a pettine e quattro passa tutto per canale.</summary>
Public NotInheritable Class RiverberoStereo

    Private NotInheritable Class Pettine
        Private ReadOnly _buf() As Single
        Private _idx As Integer
        Private _store As Single
        Public Feedback As Single
        Public Damp1 As Single
        Public Damp2 As Single

        Public Sub New(lunghezza As Integer)
            ReDim _buf(lunghezza - 1)
        End Sub

        Public Function Elabora(ingresso As Single) As Single
            Dim uscita As Single = _buf(_idx)
            _store = uscita * Damp2 + _store * Damp1
            ' il piccolo scarto evita i numeri denormali, che nelle code del riverbero rallentano il calcolo e fanno scattare l'audio
            _buf(_idx) = ingresso + _store * Feedback + 1.0E-20F
            _idx += 1
            If _idx >= _buf.Length Then _idx = 0
            Return uscita
        End Function
    End Class

    Private NotInheritable Class PassaTutto
        Private ReadOnly _buf() As Single
        Private _idx As Integer

        Public Sub New(lunghezza As Integer)
            ReDim _buf(lunghezza - 1)
        End Sub

        Public Function Elabora(ingresso As Single) As Single
            Dim b As Single = _buf(_idx)
            Dim uscita As Single = -ingresso + b
            _buf(_idx) = ingresso + b * 0.5F
            _idx += 1
            If _idx >= _buf.Length Then _idx = 0
            Return uscita
        End Function
    End Class

    Private Shared ReadOnly TaraturePettini() As Integer = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617}
    Private Shared ReadOnly TaraturePassaTutto() As Integer = {556, 441, 341, 225}

    Private ReadOnly _pettiniS(7) As Pettine
    Private ReadOnly _pettiniD(7) As Pettine
    Private ReadOnly _passaS(3) As PassaTutto
    Private ReadOnly _passaD(3) As PassaTutto

    Public Sub New()
        Dim scala As Double = WGDsp.FC / 44100.0
        For i As Integer = 0 To 7
            _pettiniS(i) = New Pettine(CInt(TaraturePettini(i) * scala))
            _pettiniD(i) = New Pettine(CInt((TaraturePettini(i) + 23) * scala))
        Next
        For i As Integer = 0 To 3
            _passaS(i) = New PassaTutto(CInt(TaraturePassaTutto(i) * scala))
            _passaD(i) = New PassaTutto(CInt((TaraturePassaTutto(i) + 23) * scala))
        Next
        Imposta(0.82, 0.45)
    End Sub

    Public Sub Imposta(dimensione As Double, smorzamento As Double)
        Dim feedback As Single = CSng(dimensione * 0.28 + 0.7)
        Dim d1 As Single = CSng(smorzamento * 0.4)
        For i As Integer = 0 To 7
            _pettiniS(i).Feedback = feedback
            _pettiniD(i).Feedback = feedback
            _pettiniS(i).Damp1 = d1
            _pettiniD(i).Damp1 = d1
            _pettiniS(i).Damp2 = 1.0F - d1
            _pettiniD(i).Damp2 = 1.0F - d1
        Next
    End Sub

    ''' <summary>Elabora un campione mono e restituisce la sola parte riverberata, a sinistra e a destra.</summary>
    Public Sub Elabora(ingresso As Single, ByRef sinistra As Single, ByRef destra As Single)
        Dim x As Single = ingresso * 0.015F
        Dim s As Single = 0.0F
        Dim d As Single = 0.0F
        For i As Integer = 0 To 7
            s += _pettiniS(i).Elabora(x)
            d += _pettiniD(i).Elabora(x)
        Next
        For i As Integer = 0 To 3
            s = _passaS(i).Elabora(s)
            d = _passaD(i).Elabora(d)
        Next
        sinistra = s
        destra = d
    End Sub

End Class

''' <summary>La traccia di accompagnamento, portata a 48000 Hz in stereo, con la ripetizione se richiesta.</summary>
Public NotInheritable Class SottofondoAudio
    Implements ISampleProvider, IDisposable

    Private ReadOnly _percorso As String
    Private _lettore As AudioFileReader
    Private _catena As ISampleProvider
    Private ReadOnly _formato As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
    Public Ripeti As Boolean
    Public Terminato As Boolean
    Public ReadOnly Property DurataSecondi As Double

    Public Sub New(percorso As String)
        _percorso = percorso
        CreaCatena()
        Try
            DurataSecondi = _lettore.TotalTime.TotalSeconds
        Catch
            DurataSecondi = 0.0
        End Try
    End Sub

    Public ReadOnly Property Percorso As String
        Get
            Return _percorso
        End Get
    End Property

    Private Sub CreaCatena()
        If _lettore IsNot Nothing Then _lettore.Dispose()
        _lettore = New AudioFileReader(_percorso)
        Dim s As ISampleProvider = _lettore
        If s.WaveFormat.Channels = 1 Then
            s = New MonoToStereoSampleProvider(s)
        ElseIf s.WaveFormat.Channels > 2 Then
            Dim mux As New MultiplexingSampleProvider(New ISampleProvider() {s}, 2)
            mux.ConnectInputToOutput(0, 0)
            mux.ConnectInputToOutput(1, 1)
            s = mux
        End If
        If s.WaveFormat.SampleRate <> 48000 Then s = New WdlResamplingSampleProvider(s, 48000)
        _catena = s
        Terminato = False
    End Sub

    ''' <summary>Riporta la traccia all'inizio.</summary>
    Public Sub Riavvia()
        CreaCatena()
    End Sub

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _formato
        End Get
    End Property

    Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Dim letti As Integer = 0
        If Terminato Then
            Array.Clear(buffer, offset, count)
            Return 0
        End If
        Dim giriVuoti As Integer = 0
        While letti < count
            Dim n As Integer = _catena.Read(buffer, offset + letti, count - letti)
            If n <= 0 Then
                If Ripeti AndAlso giriVuoti < 2 Then
                    giriVuoti += 1
                    CreaCatena()
                    Continue While
                End If
                Terminato = True
                Exit While
            End If
            giriVuoti = 0
            letti += n
        End While
        If letti < count Then Array.Clear(buffer, offset + letti, count - letti)
        Return letti
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _lettore IsNot Nothing Then _lettore.Dispose()
        Catch
        End Try
        _lettore = Nothing
    End Sub

End Class

''' <summary>Scrive su un file temporaneo (a virgola mobile, 48000 Hz, stereo) quello che il motore produce.</summary>
Public NotInheritable Class RegistratoreFlusso

    Private ReadOnly _coda As New BlockingCollection(Of Single())(New ConcurrentQueue(Of Single())())
    Private ReadOnly _scrittore As WaveFileWriter
    Private ReadOnly _thread As Thread
    Private _errore As Boolean
    Public ReadOnly Property Percorso As String
    Public ReadOnly Property Fotogrammi As Long
        Get
            Return Interlocked.Read(_fotogrammi)
        End Get
    End Property
    Private _fotogrammi As Long

    Public Sub New(percorsoWav As String)
        Percorso = percorsoWav
        _scrittore = New WaveFileWriter(percorsoWav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        _thread = New Thread(AddressOf Scrivi)
        _thread.IsBackground = True
        _thread.Name = "WGRegistratoreFlusso"
        _thread.Start()
    End Sub

    ''' <summary>Accoda dei fotogrammi stereo interleaved; il motore chiama questo metodo dal thread audio, quindi copia soltanto.</summary>
    Public Sub Accoda(dati() As Single, campioni As Integer)
        If _coda.IsAddingCompleted Then Return
        Dim copia(campioni - 1) As Single
        System.Buffer.BlockCopy(dati, 0, copia, 0, campioni * 4)
        Try
            _coda.Add(copia)
        Catch ex As InvalidOperationException
        End Try
        Interlocked.Add(_fotogrammi, campioni \ 2)
    End Sub

    Private Sub Scrivi()
        Try
            For Each blocco As Single() In _coda.GetConsumingEnumerable()
                _scrittore.WriteSamples(blocco, 0, blocco.Length)
            Next
        Catch ex As Exception
            _errore = True
        End Try
    End Sub

    ''' <summary>Chiude il file dopo aver scritto tutto quello che era in coda. Falso se la scrittura è andata male.</summary>
    Public Function Chiudi() As Boolean
        Try
            _coda.CompleteAdding()
            _thread.Join()
            _scrittore.Dispose()
        Catch ex As Exception
            _errore = True
        End Try
        Return Not _errore
    End Function

End Class

''' <summary>Dal file temporaneo all'mp3 ad alta qualità (320 kbit/s), con i mezzi di Windows; se mancano, con ffmpeg.</summary>
Public NotInheritable Class WGEsportazioneAudio

    Private Sub New()
    End Sub

    Public Const BitrateMp3 As Integer = 320000

    ''' <summary>Converte un file wav (anche a virgola mobile) in mp3. Se non ci riesce lascia il motivo in messaggio.</summary>
    Public Shared Function ConvertiInMp3(percorsoWav As String, percorsoMp3 As String, ByRef messaggio As String) As Boolean
        messaggio = ""
        Dim erroreWindows As String = ""
        Try
            MediaFoundationApi.Startup()
            Using lettore As New WaveFileReader(percorsoWav)
                Dim campioni As ISampleProvider = lettore.ToSampleProvider()
                Dim pcm As IWaveProvider = New SampleToWaveProvider16(campioni)
                MediaFoundationEncoder.EncodeToMp3(pcm, percorsoMp3, BitrateMp3)
            End Using
            If File.Exists(percorsoMp3) AndAlso New FileInfo(percorsoMp3).Length > 1000 Then Return True
            erroreWindows = "il file mp3 non è stato creato"
        Catch ex As Exception
            erroreWindows = ex.Message
            Try
                If File.Exists(percorsoMp3) Then File.Delete(percorsoMp3)
            Catch
            End Try
        End Try

        Try
            Dim ffmpeg As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            If File.Exists(ffmpeg) Then
                Dim info As New ProcessStartInfo(ffmpeg, "-y -loglevel error -i """ & percorsoWav & """ -c:a libmp3lame -b:a 320k """ & percorsoMp3 & """")
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

        messaggio = "Non è stato possibile creare l'mp3 (" & erroreWindows & ")."
        Return False
    End Function

    ''' <summary>Salva la registrazione come wav a 16 bit, quando l'mp3 non è possibile.</summary>
    Public Shared Function ConvertiInWav16(percorsoOrigine As String, percorsoWav As String) As Boolean
        Try
            Using lettore As New WaveFileReader(percorsoOrigine)
                WaveFileWriter.CreateWaveFile16(percorsoWav, lettore.ToSampleProvider())
            End Using
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

End Class

''' <summary>
''' Il metronomo: un colpo a ogni battito, più acuto e più forte sul primo della battuta. Sta nel motore (si sente insieme agli strumenti)
''' e nel registratore vocale (si mescola alle tracce). Riavvia() lo porta all'istante zero: il primo colpo è subito.
''' </summary>
Public NotInheritable Class GeneratoreMetronomo
    Implements ISampleProvider

    Private ReadOnly _formato As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
    Private _posizione As Long
    Private _riavvia As Integer
    Public Attivo As Boolean
    Public Bpm As Double = 100.0
    Public Battiti As Integer = 4
    Public Volume As Single = 0.5F

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _formato
        End Get
    End Property

    ''' <summary>Riporta il metronomo all'inizio: il prossimo colpo, il primo della battuta, è subito.</summary>
    Public Sub Riavvia()
        Interlocked.Exchange(_riavvia, 1)
    End Sub

    Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Dim fotogrammi As Integer = count \ 2
        If Interlocked.Exchange(_riavvia, 0) = 1 Then _posizione = 0
        If Not Attivo Then
            Array.Clear(buffer, offset, count)
            Return count
        End If
        Dim passo As Double = 60.0 * 48000.0 / Math.Max(20.0, Math.Min(400.0, Bpm))
        Dim battiti As Integer = Math.Max(1, Battiti)
        For i As Integer = 0 To fotogrammi - 1
            Dim p As Double = _posizione + i
            Dim indice As Long = CLng(Math.Floor(p / passo))
            Dim r As Double = p - indice * passo
            Dim valore As Single = 0.0F
            If r < 2400.0 Then
                Dim primo As Boolean = indice Mod battiti = 0
                Dim frequenza As Double = If(primo, 1600.0, 1000.0)
                valore = CSng(Math.Sin(2.0 * Math.PI * frequenza * r / 48000.0) * Math.Exp(-r / 380.0) * If(primo, 1.0, 0.7)) * Volume
            End If
            buffer(offset + i * 2) = valore
            buffer(offset + i * 2 + 1) = valore
        Next
        _posizione += fotogrammi
        Return count
    End Function

End Class

Public NotInheritable Class MotoreSintesi
    Implements IWaveProvider, IDisposable

    Private Enum TipoEvento
        NotaOn
        NotaOff
        Pedale
        Strumento
        Silenzio
        AvviaSottofondo
        FermaSottofondo
        NuovoSottofondo
        Registratore
    End Enum

    Private NotInheritable Class EventoMotore
        Public Tipo As TipoEvento
        Public Chiave As Integer
        Public Nota As Integer
        Public Velocita As Double
        Public Attivo As Boolean
        Public Oggetto As Object
        Public Guadagno As Double = 1.0
    End Class

    Private Const BloccoMassimo As Integer = 128
    ' meno sessanta decibel in trenta millisecondi a 48000 Hz
    Private Shared ReadOnly FattoreTaglio As Double = Math.Exp(Math.Log(0.001) / (0.03 * 48000.0))

    Private ReadOnly _formato As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
    Private ReadOnly _eventi As New ConcurrentQueue(Of EventoMotore)()
    Private ReadOnly _voci As New List(Of VoceSintesi)(64)
    Private ReadOnly _tasti As New Dictionary(Of Integer, VoceSintesi)()
    Private ReadOnly _riverbero As New RiverberoStereo()
    Private ReadOnly _tmp(BloccoMassimo - 1) As Single
    Private ReadOnly _sinistra(BloccoMassimo - 1) As Single
    Private ReadOnly _destra(BloccoMassimo - 1) As Single
    Private ReadOnly _bk(BloccoMassimo * 2 - 1) As Single
    Private ReadOnly _mix(BloccoMassimo * 2 - 1) As Single
    Private ReadOnly _bkMetronomo(BloccoMassimo * 2 - 1) As Single
    Private _strumento As DefinizioneStrumento
    Private _pedale As Boolean
    Private _guadagnoLimitatore As Single = 1.0F
    Private _sottofondo As SottofondoAudio
    Private _sottofondoInRiproduzione As Boolean
    Private _registratore As RegistratoreFlusso
    Private _uscita As WaveOutEvent
    Private _uscitaVeloce As UscitaBassaLatenza
    Private _prioritaImpostata As Boolean
    Private _scartati As Boolean

    ''' <summary>Volume dello strumento, da 0 a 1.</summary>
    Public VolumeStrumento As Single = 0.8F
    ''' <summary>Volume del sottofondo, da 0 a 1.</summary>
    Public VolumeSottofondo As Single = 0.7F
    ''' <summary>Quanto riverbero in più (o in meno) rispetto a quello dello strumento: da 0 a 1.5.</summary>
    Public ScalaRiverbero As Single = 1.0F
    Public SottofondoRipetuto As Boolean
    ''' <summary>Il metronomo si sente insieme agli strumenti; lo si configura e lo si accende da fuori.</summary>
    Public ReadOnly Metronomo As New GeneratoreMetronomo()

    Public Event SottofondoFinito As EventHandler

    Public ReadOnly Property WaveFormat As WaveFormat Implements IWaveProvider.WaveFormat
        Get
            Return _formato
        End Get
    End Property

    ''' <summary>Apre la scheda audio. latenzaMs: tempo totale del buffer (più è basso, più la nota risponde in fretta).</summary>
    Private _modoGc As System.Runtime.GCLatencyMode
    Private _modoGcSalvato As Boolean

    Public Sub Avvia(latenzaMs As Integer)
        Try
            If Not _modoGcSalvato Then
                _modoGc = System.Runtime.GCSettings.LatencyMode
                _modoGcSalvato = True
            End If
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency
        Catch
        End Try
        If latenzaMs <= 0 Then
            SyncLock _bloccoUscita
                _uscitaVeloce = UscitaBassaLatenza.Avvia(Me)
                If _uscitaVeloce IsNot Nothing Then AddHandler _uscitaVeloce.Interrotta, AddressOf OnUscitaInterrotta
            End SyncLock
            If _uscitaVeloce IsNot Nothing Then Return
            latenzaMs = 60
        End If
        ' sotto i sessanta millisecondi l'uscita normale di Windows non riesce a tenere il passo (l'orologio ha scatti di quindici
        ' millisecondi) e il suono scatta: misurato, a trenta millisecondi arrivano richieste con ritardi fino a quaranta.
        _uscita = New WaveOutEvent()
        _uscita.DesiredLatency = Math.Max(60, latenzaMs)
        _uscita.NumberOfBuffers = 2
        _uscita.Init(Me)
        _uscita.Play()
    End Sub

    Private ReadOnly _bloccoUscita As New Object()

    ' Il flusso a bassa latenza si è interrotto: cambiano le cuffie o la scheda audio predefinita. Si riapre sul dispositivo nuovo,
    ' con qualche tentativo perché Windows ci mette un momento a mostrarlo. Se nel frattempo il motore è stato fermato non si fa niente.
    Private Sub OnUscitaInterrotta(mittente As Object, e As EventArgs)
        Task.Run(Sub()
                     For tentativo As Integer = 1 To 5
                         Thread.Sleep(500 * tentativo)
                         SyncLock _bloccoUscita
                             If Not Object.ReferenceEquals(mittente, _uscitaVeloce) Then Return
                             Try
                                 DirectCast(mittente, UscitaBassaLatenza).Dispose()
                             Catch
                             End Try
                             _uscitaVeloce = Nothing
                             Try
                                 _uscitaVeloce = UscitaBassaLatenza.Avvia(Me)
                             Catch
                                 _uscitaVeloce = Nothing
                             End Try
                             If _uscitaVeloce IsNot Nothing Then
                                 AddHandler _uscitaVeloce.Interrotta, AddressOf OnUscitaInterrotta
                                 Return
                             End If
                         End SyncLock
                     Next
                 End Sub)
    End Sub

    Public Sub Ferma()
        Try
            If _modoGcSalvato Then System.Runtime.GCSettings.LatencyMode = _modoGc
        Catch
        End Try
        Try
            SyncLock _bloccoUscita
                If _uscitaVeloce IsNot Nothing Then
                    _uscitaVeloce.Dispose()
                    _uscitaVeloce = Nothing
                End If
            End SyncLock
        Catch
        End Try
        Try
            If _uscita IsNot Nothing Then
                _uscita.Stop()
                _uscita.Dispose()
                _uscita = Nothing
            End If
        Catch
        End Try
    End Sub

    Public ReadOnly Property Registrando As Boolean
        Get
            Return _registratore IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property SottofondoCaricato As Boolean
        Get
            Return _sottofondo IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property SottofondoInRiproduzione As Boolean
        Get
            Return _sottofondoInRiproduzione
        End Get
    End Property

    Private Sub Accoda(e As EventoMotore)
        _eventi.Enqueue(e)
    End Sub

    Public Sub ImpostaStrumento(strumento As DefinizioneStrumento)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.Strumento, .Oggetto = strumento})
    End Sub

    ''' <summary>Premere un tasto. chiave identifica il tasto (per poterlo lasciare), nota è la nota MIDI.</summary>
    Public Sub NotaOn(chiave As Integer, nota As Integer, velocita As Double, Optional guadagno As Double = 1.0)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.NotaOn, .Chiave = chiave, .Nota = nota, .Velocita = velocita, .Guadagno = guadagno})
    End Sub

    ''' <summary>Come NotaOn, ma con lo strumento indicato: serve a suonare più strumenti insieme, come in uno spartito.</summary>
    Public Sub NotaOnStrumento(chiave As Integer, nota As Integer, velocita As Double, strumento As DefinizioneStrumento, Optional guadagno As Double = 1.0)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.NotaOn, .Chiave = chiave, .Nota = nota, .Velocita = velocita, .Oggetto = strumento, .Guadagno = guadagno})
    End Sub

    ''' <summary>Lascia un tasto. Con taglioRapido la nota si stacca subito (in circa trenta millisecondi) invece di seguire il proprio rilascio.</summary>
    Public Sub NotaOff(chiave As Integer, Optional taglioRapido As Boolean = False)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.NotaOff, .Chiave = chiave, .Attivo = taglioRapido})
    End Sub

    Public Sub Pedale(premuto As Boolean)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.Pedale, .Attivo = premuto})
    End Sub

    ''' <summary>Ferma tutte le note.</summary>
    Public Sub SilenziaTutto()
        Accoda(New EventoMotore With {.Tipo = TipoEvento.Silenzio})
    End Sub

    Public Sub CaricaSottofondo(sottofondo As SottofondoAudio)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.NuovoSottofondo, .Oggetto = sottofondo})
    End Sub

    ''' <summary>Fa partire il sottofondo, dall'inizio se daCapo è vero.</summary>
    Public Sub AvviaSottofondo(daCapo As Boolean)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.AvviaSottofondo, .Attivo = daCapo})
    End Sub

    Public Sub FermaSottofondo()
        Accoda(New EventoMotore With {.Tipo = TipoEvento.FermaSottofondo})
    End Sub

    ''' <summary>Comincia a registrare quello che si sente. Con il sottofondo caricato lo fa ripartire dall'inizio, nello stesso istante.</summary>
    Public Sub AvviaRegistrazione(registratore As RegistratoreFlusso)
        Accoda(New EventoMotore With {.Tipo = TipoEvento.Registratore, .Oggetto = registratore, .Attivo = True})
    End Sub

    Public Sub FermaRegistrazione()
        Accoda(New EventoMotore With {.Tipo = TipoEvento.Registratore, .Oggetto = Nothing, .Attivo = False})
    End Sub


    ' ---- rendering (thread audio) --------------------------------------------------------------------

    Public Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer Implements IWaveProvider.Read
        If Not _prioritaImpostata Then
            _prioritaImpostata = True
            Try
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal
            Catch
            End Try
        End If
        Dim fotogrammi As Integer = count \ 8
        Dim fatti As Integer = 0
        While fatti < fotogrammi
            Dim n As Integer = Math.Min(BloccoMassimo, fotogrammi - fatti)
            Try
                RenderizzaBlocco(n)
            Catch ex As Exception
                Array.Clear(_mix, 0, n * 2)
            End Try
            System.Buffer.BlockCopy(_mix, 0, buffer, offset + fatti * 8, n * 8)
            fatti += n
        End While
        Return fotogrammi * 8
    End Function

    Private Sub ElaboraEventi()
        Dim e As EventoMotore = Nothing
        While _eventi.TryDequeue(e)
            Select Case e.Tipo
                Case TipoEvento.Strumento
                    For Each v As VoceSintesi In _voci
                        v.Silenzia()
                    Next
                    _tasti.Clear()
                    _strumento = DirectCast(e.Oggetto, DefinizioneStrumento)
                Case TipoEvento.NotaOn
                    AvviaNota(e)
                Case TipoEvento.NotaOff
                    Dim v As VoceSintesi = Nothing
                    If _tasti.TryGetValue(e.Chiave, v) Then
                        _tasti.Remove(e.Chiave)
                        If _pedale Then
                            v.TenutaDalPedale = True
                        Else
                            v.Rilascia()
                            If e.Attivo Then v.TaglioRapido = True
                        End If
                    End If
                Case TipoEvento.Pedale
                    _pedale = e.Attivo
                    If Not _pedale Then
                        For Each v As VoceSintesi In _voci
                            If v.TenutaDalPedale Then
                                v.TenutaDalPedale = False
                                v.Rilascia()
                            End If
                        Next
                    End If
                Case TipoEvento.Silenzio
                    _pedale = False
                    For Each v As VoceSintesi In _voci
                        v.Silenzia()
                    Next
                    _tasti.Clear()
                Case TipoEvento.NuovoSottofondo
                    Dim vecchio As SottofondoAudio = _sottofondo
                    _sottofondo = DirectCast(e.Oggetto, SottofondoAudio)
                    _sottofondoInRiproduzione = False
                    If vecchio IsNot Nothing Then
                        Dim daChiudere As SottofondoAudio = vecchio
                        Task.Run(Sub() daChiudere.Dispose())
                    End If
                Case TipoEvento.AvviaSottofondo
                    If _sottofondo IsNot Nothing Then
                        If e.Attivo OrElse _sottofondo.Terminato Then _sottofondo.Riavvia()
                        _sottofondo.Ripeti = SottofondoRipetuto
                        _sottofondoInRiproduzione = True
                    End If
                Case TipoEvento.FermaSottofondo
                    _sottofondoInRiproduzione = False
                Case TipoEvento.Registratore
                    If e.Attivo Then
                        If _sottofondo IsNot Nothing Then
                            _sottofondo.Riavvia()
                            _sottofondo.Ripeti = SottofondoRipetuto
                            _sottofondoInRiproduzione = True
                        End If
                        _registratore = DirectCast(e.Oggetto, RegistratoreFlusso)
                    Else
                        _registratore = Nothing
                        _sottofondoInRiproduzione = False
                    End If
            End Select
        End While
    End Sub

    Private Sub AvviaNota(e As EventoMotore)
        Dim strumento As DefinizioneStrumento = If(TryCast(e.Oggetto, DefinizioneStrumento), _strumento)
        If strumento Is Nothing OrElse strumento.Crea Is Nothing Then Return
        Dim v As VoceSintesi = Nothing
        Try
            v = strumento.Crea(e.Nota, e.Velocita)
        Catch ex As Exception
            Return
        End Try
        If v Is Nothing Then Return
        v.Nota = e.Nota
        v.Livello = CSng(v.Livello * strumento.Livello * e.Guadagno)
        If v.GruppoDiSilenzio <> 0 Then
            For Each altra As VoceSintesi In _voci
                If altra.GruppoDiSilenzio = v.GruppoDiSilenzio Then altra.Silenzia()
            Next
        End If
        Dim precedente As VoceSintesi = Nothing
        If _tasti.TryGetValue(e.Chiave, precedente) Then precedente.Rilascia()
        _tasti(e.Chiave) = v
        _voci.Add(v)
        Dim massimo As Integer = Math.Max(4, strumento.MassimoVoci)
        If Not strumento.Percussivo Then massimo = Math.Max(massimo, 48)
        If e.Oggetto IsNot Nothing Then massimo = Math.Max(massimo, 96)
        If _voci.Count > massimo Then
            ' si sacrifica prima una nota che sta già sfumando, poi la più vecchia: le note appena suonate non devono mai tagliarsi a vicenda
            Dim daSpegnere As VoceSintesi = Nothing
            For Each vecchia As VoceSintesi In _voci
                If Object.ReferenceEquals(vecchia, v) Then Continue For
                If vecchia.InRilascio AndAlso Not vecchia.TaglioRapido Then
                    daSpegnere = vecchia
                    Exit For
                End If
            Next
            If daSpegnere Is Nothing Then
                For Each vecchia As VoceSintesi In _voci
                    If Object.ReferenceEquals(vecchia, v) Then Continue For
                    If Not vecchia.InRilascio OrElse vecchia.TenutaDalPedale Then
                        daSpegnere = vecchia
                        Exit For
                    End If
                Next
            End If
            If daSpegnere IsNot Nothing Then daSpegnere.Silenzia()
        End If
    End Sub

    Private Sub RenderizzaBlocco(n As Integer)
        ElaboraEventi()
        Dim strumento As DefinizioneStrumento = _strumento
        Array.Clear(_sinistra, 0, n)
        Array.Clear(_destra, 0, n)
        Dim gVoci As Single = VolumeStrumento * VolumeStrumento * 1.6F

        ' le voci
        Dim i As Integer = _voci.Count - 1
        While i >= 0
            Dim v As VoceSintesi = _voci(i)
            Array.Clear(_tmp, 0, n)
            v.Genera(_tmp, n)
            If v.TaglioRapido Then
                ' tasto sollevato: il suono scende in circa trenta millisecondi (di colpo farebbe uno scatto), poi la voce si spegne
                Dim g As Double = v.GuadagnoTaglio
                For k As Integer = 0 To n - 1
                    _tmp(k) = CSng(_tmp(k) * g)
                    g *= FattoreTaglio
                Next
                If g < 0.001 AndAlso v.GuadagnoTaglio >= 0.001 Then v.Silenzia()
                v.GuadagnoTaglio = If(g < 0.0005, 0.0, g)
            End If
            Dim angolo As Double = (v.Pan + 1.0) * 0.25 * Math.PI
            Dim gs As Single = CSng(Math.Cos(angolo)) * gVoci
            Dim gd As Single = CSng(Math.Sin(angolo)) * gVoci
            For k As Integer = 0 To n - 1
                Dim x As Single = _tmp(k)
                _sinistra(k) += x * gs
                _destra(k) += x * gd
            Next
            If v.Finita Then
                _voci.RemoveAt(i)
                Dim chiaveDaTogliere As Integer = Integer.MinValue
                For Each kv As KeyValuePair(Of Integer, VoceSintesi) In _tasti
                    If kv.Value Is v Then
                        chiaveDaTogliere = kv.Key
                        Exit For
                    End If
                Next
                If chiaveDaTogliere <> Integer.MinValue Then _tasti.Remove(chiaveDaTogliere)
            End If
            i -= 1
        End While

        ' il riverbero
        Dim wet As Single = 0.0F
        If strumento IsNot Nothing Then wet = CSng(WGDsp.Limita(strumento.Riverbero * ScalaRiverbero, 0.0, 1.0))
        If wet > 0.001F Then
            For k As Integer = 0 To n - 1
                Dim rs As Single = 0.0F
                Dim rd As Single = 0.0F
                _riverbero.Elabora((_sinistra(k) + _destra(k)) * 0.5F, rs, rd)
                _sinistra(k) += rs * wet * 3.2F
                _destra(k) += rd * wet * 3.2F
            Next
        End If

        ' il metronomo
        If Metronomo.Attivo Then
            Metronomo.Read(_bkMetronomo, 0, n * 2)
            For k As Integer = 0 To n - 1
                _sinistra(k) += _bkMetronomo(k * 2)
                _destra(k) += _bkMetronomo(k * 2 + 1)
            Next
        End If

        ' il sottofondo
        Dim haSottofondo As Boolean = False
        If _sottofondoInRiproduzione AndAlso _sottofondo IsNot Nothing Then
            _sottofondo.Ripeti = SottofondoRipetuto
            Dim letti As Integer = _sottofondo.Read(_bk, 0, n * 2)
            haSottofondo = True
            If _sottofondo.Terminato AndAlso letti = 0 Then
                _sottofondoInRiproduzione = False
                haSottofondo = False
                Task.Run(Sub() RaiseEvent SottofondoFinito(Me, EventArgs.Empty))
            End If
        End If
        Dim gBk As Single = VolumeSottofondo * VolumeSottofondo
        For k As Integer = 0 To n - 1
            Dim s As Single = _sinistra(k)
            Dim d As Single = _destra(k)
            If haSottofondo Then
                s += _bk(k * 2) * gBk
                d += _bk(k * 2 + 1) * gBk
            End If
            _mix(k * 2) = s
            _mix(k * 2 + 1) = d
        Next

        ' limitatore e arrotondamento morbido dei picchi
        Dim picco As Single = 0.0F
        For k As Integer = 0 To n * 2 - 1
            Dim a As Single = Math.Abs(_mix(k))
            If a > picco Then picco = a
        Next
        Dim guadagnoPrima As Single = _guadagnoLimitatore
        Dim bersaglio As Single = 1.0F
        If picco * _guadagnoLimitatore > 0.92F Then bersaglio = 0.92F / picco
        If bersaglio < _guadagnoLimitatore Then
            _guadagnoLimitatore = bersaglio
        Else
            _guadagnoLimitatore += (1.0F - _guadagnoLimitatore) * 0.0015F * n / 16.0F
            If _guadagnoLimitatore > 1.0F Then _guadagnoLimitatore = 1.0F
        End If
        For k As Integer = 0 To n * 2 - 1
            ' il guadagno cambia poco alla volta dentro il blocco: se cambiasse di colpo, ogni cambio farebbe uno scatto
            Dim y As Single = _mix(k) * (guadagnoPrima + (_guadagnoLimitatore - guadagnoPrima) * ((k \ 2 + 1) / CSng(n)))
            Dim ay As Single = Math.Abs(y)
            If ay > 0.8F Then
                Dim eccesso As Single = (ay - 0.8F) / 0.2F
                ay = 0.8F + 0.2F * CSng(Math.Tanh(eccesso))
                y = If(y < 0.0F, -ay, ay)
            End If
            _mix(k) = y
        Next

        Dim registratore As RegistratoreFlusso = _registratore
        If registratore IsNot Nothing Then registratore.Accoda(_mix, n * 2)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Ferma()
        Try
            If _sottofondo IsNot Nothing Then _sottofondo.Dispose()
        Catch
        End Try
        _sottofondo = Nothing
    End Sub

End Class
