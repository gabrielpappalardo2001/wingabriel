' ============================================================================================
' WGFotocameraCalibrazione - la latenza vera del percorso audio, sottratta sempre
' ============================================================================================
'
' Il microfono consegna la voce con un ritardo: i buffer di ingresso, il motore audio di Windows, il tempo che passa dal suono suonato
' al suono che si sente. Se non si corregge, la voce registrata sopra un sottofondo (o sopra un video) risulta sempre in ritardo di
' quel tanto. Questa latenza si sottrae SEMPRE, a ogni registrazione: i millisecondi che sceglie l'utente (voce_compensazione_ms) sono
' solo una regolazione fine in più, e con 0 l'allineamento è già quello corretto.
'
' Da dove viene il valore, senza fare nessun suono:
'   - la stima: si leggono dal motore audio di Windows (WASAPI) i periodi dichiarati dall'uscita predefinita e dall'ingresso del microfono
'     in uso, e si calcola uno e mezzo per ciascuno (la voce passa per il motore e per un periodo di consegna) più il buffer di ingresso
'     del registratore (10 ms); su questo computer dà circa 40 ms, e la misura con i colpi ne dava circa 44;
'   - "Calibra" (solo se l'utente lo sceglie, e allora i colpi si sentono): suona sei colpi dagli altoparlanti e li ascolta con il
'     microfono, passando per lo stesso percorso della registrazione; il ritardo trovato, oltre a quello già corretto dalla retta di
'     allineamento, è la latenza misurata, che si conserva (voce_latenza_ms, voce_latenza_mic) e prende il posto della stima.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.CoreAudioApi
Imports NAudio.Wave

Public NotInheritable Class LatenzaAudio

    Private Shared ReadOnly _log As New Logger()
    Private Shared _inCorso As Boolean

    ''' <summary>Il buffer con cui il registratore apre il microfono (ms).</summary>
    Public Const BufferIngressoMs As Double = 10.0
    ''' <summary>La stima di riserva se il motore audio non risponde.</summary>
    Public Const StimaDiRiservaMs As Double = 40.0

    Private Sub New()
    End Sub

    Public Shared Function NomeMicrofono(dispositivo As Integer) As String
        Try
            Return WaveInEvent.GetCapabilities(dispositivo).ProductName
        Catch
            Return ""
        End Try
    End Function

    Public Shared Function DispositivoSalvato() As Integer
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

    ''' <summary>La latenza conservata per il microfono in uso, in millisecondi; Double.NaN se non è ancora stata misurata (o stimata) per lui.</summary>
    Public Shared Function Conservata() As Double
        Dim v As Double
        If ImpostazioniMusica.Leggi("voce_latenza_mic", "") <> NomeMicrofono(DispositivoSalvato()) Then Return Double.NaN
        If Double.TryParse(ImpostazioniMusica.Leggi("voce_latenza_ms", ""), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then Return v
        Return Double.NaN
    End Function

    ''' <summary>La latenza da sottrarre sempre: quella misurata con "Calibra" per questo microfono, se c'è, altrimenti la stima silenziosa.</summary>
    Public Shared Function BaseMs() As Double
        Dim v As Double = Conservata()
        If Double.IsNaN(v) Then Return StimaSilenziosaMs()
        Return v
    End Function

    ''' <summary>La stima dai periodi dichiarati dal motore audio di Windows (nessun suono): 1,5 volte il periodo di uscita e di ingresso, più il buffer di ingresso.</summary>
    Public Shared Function StimaSilenziosaMs() As Double
        Try
            Dim en As New MMDeviceEnumerator()
            Dim periodoUscita As Double = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).AudioClient.DefaultDevicePeriod / 10000.0
            Dim nome As String = NomeMicrofono(DispositivoSalvato())
            Dim ingresso As MMDevice = Nothing
            For Each d As MMDevice In en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                If nome <> "" AndAlso d.FriendlyName.Contains(nome) Then
                    ingresso = d
                    Exit For
                End If
            Next
            If ingresso Is Nothing Then ingresso = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            Dim periodoIngresso As Double = ingresso.AudioClient.DefaultDevicePeriod / 10000.0
            Dim v As Double = 1.5 * (periodoUscita + periodoIngresso) + BufferIngressoMs
            If v < 5.0 OrElse v > 200.0 Then Return StimaDiRiservaMs
            Return v
        Catch ex As Exception
            _log.ScriviLog("LatenzaAudio.StimaSilenziosaMs: " & ex.Message)
            Return StimaDiRiservaMs
        End Try
    End Function

    Private Shared Sub Conserva(dispositivo As Integer, ms As Double)
        ImpostazioniMusica.Scrivi("voce_latenza_mic", NomeMicrofono(dispositivo))
        ImpostazioniMusica.Scrivi("voce_latenza_ms", ms.ToString("0.0", Globalization.CultureInfo.InvariantCulture))
    End Sub

    ''' <summary>
    ''' Misura la latenza: suona sei colpi e li ascolta (circa tre secondi e mezzo, bloccante). Restituisce i millisecondi, o Double.NaN con il
    ''' motivo se i colpi non si sentono. Non conserva niente.
    ''' </summary>
    Public Shared Function Misura(dispositivo As Integer, ByRef motivo As String) As Double
        motivo = ""
        Dim acquisizione As New AcquisizioneVoce()
        Dim uscita As New WaveOutEvent()
        Dim x() As Short
        Try
            uscita.DesiredLatency = 100
            uscita.NumberOfBuffers = 3
            uscita.Init(New GeneratoreClic())
            acquisizione.Avvia(dispositivo, uscita)
            uscita.Play()
            Thread.Sleep(CInt((GeneratoreClic.Anticipo + GeneratoreClic.Distanza * GeneratoreClic.NumeroClic) / 48.0) + 700)
            x = acquisizione.Ferma()
            Try
                uscita.Stop()
            Catch
            End Try
        Catch ex As Exception
            _log.ScriviLog("LatenzaAudio.Misura: " & ex.Message)
            motivo = "Non è stato possibile avviare la misura."
            Try
                acquisizione.Ferma()
            Catch
            End Try
            Return Double.NaN
        Finally
            Try
                uscita.Dispose()
            Catch
            End Try
        End Try
        Return Analizza(acquisizione, x, motivo)
    End Function

    Private Shared Function Analizza(acquisizione As AcquisizioneVoce, x() As Short, ByRef motivo As String) As Double
        If acquisizione.PuntiVoce.Count < 5 Then
            motivo = "Non è stato possibile misurare la sincronia: riprova."
            Return Double.NaN
        End If
        Dim n As Integer = x.Length
        Dim frequenza As Integer = acquisizione.Frequenza
        Dim a As Double = 0.0
        Dim d As Double = 0.0
        acquisizione.Allineamento(a, d)
        Dim ritardi As New List(Of Double)()
        Dim finestra As Integer = frequenza \ 4
        For k As Integer = 0 To GeneratoreClic.NumeroClic - 1
            Dim posizioneClic As Double = GeneratoreClic.Anticipo + CDbl(k) * GeneratoreClic.Distanza
            Dim attesa As Double = (posizioneClic - d) / a
            Dim da As Integer = CInt(Math.Max(0.0, attesa - finestra))
            Dim fine As Integer = CInt(Math.Min(n - 1, attesa + finestra))
            If fine <= da Then Continue For
            Dim picco As Integer = 0
            Dim indice As Integer = da
            Dim somma As Double = 0.0
            For i As Integer = da To fine
                Dim v As Integer = Math.Abs(CInt(x(i)))
                somma += v
                If v > picco Then
                    picco = v
                    indice = i
                End If
            Next
            Dim media As Double = somma / (fine - da + 1)
            If picco < 600 OrElse picco < media * 8.0 Then Continue For
            ' l'inizio del colpo, non il suo punto più alto: si risale fino a un decimo del picco
            Dim inizio As Integer = indice
            While inizio > da AndAlso Math.Abs(CInt(x(inizio - 1))) > picco * 0.1
                inizio -= 1
            End While
            ritardi.Add((inizio - attesa) * 1000.0 / frequenza)
        Next
        If ritardi.Count < 3 Then
            motivo = "Non ho sentito i colpi con chiarezza. Avvicina gli altoparlanti al microfono e alza il volume; con le cuffie questa misura non funziona."
            Return Double.NaN
        End If
        ritardi.Sort()
        Return ritardi(ritardi.Count \ 2)
    End Function

    ''' <summary>"Calibra": rifà la misura e conserva il risultato (la regolazione fine dell'utente non cambia).</summary>
    Public Shared Async Sub CalibraAsync()
        Try
            If _inCorso Then Return
            If WaveInEvent.DeviceCount = 0 Then
                AiutoMusica.Messaggio("Non c'è nessun microfono collegato al computer.")
                Return
            End If
            _inCorso = True
            Dim dispositivo As Integer = DispositivoSalvato()
            Dim motivo As String = ""
            Dim ms As Double = Await WGProcessoInCorso.EseguiAsync("Calibrazione della sincronia, resta in silenzio: ascolto sei colpi dagli altoparlanti", Function() Task.Run(Function() Misura(dispositivo, motivo)))
            _inCorso = False
            If Double.IsNaN(ms) OrElse ms < -50.0 OrElse ms > 400.0 Then
                AiutoMusica.Messaggio(If(motivo <> "", motivo, "Il valore trovato non è credibile: riprova."))
                Return
            End If
            Conserva(dispositivo, ms)
            AiutoMusica.Messaggio("Sincronia calibrata: il ritardo misurato è " & CInt(Math.Round(ms)).ToString() & " millisecondi e la voce viene sempre anticipata di quel tanto. La regolazione fine resta " & ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0).ToString() & " millisecondi.")
        Catch ex As Exception
            _inCorso = False
            _log.ScriviLog("LatenzaAudio.CalibraAsync: " & ex.Message)
        End Try
    End Sub

End Class

''' <summary>Il comando "Calibra" delle impostazioni della fotocamera e del registratore.</summary>
Friend NotInheritable Class CalibratoreSincronia

    Private Sub New()
    End Sub

    Public Shared Sub Esegui()
        LatenzaAudio.CalibraAsync()
    End Sub

End Class
