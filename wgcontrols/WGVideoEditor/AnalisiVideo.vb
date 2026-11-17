' ============================================================================================
' AnalisiVideo - legge le caratteristiche di un video e calcola la correzione automatica
' ============================================================================================
'
' Le caratteristiche (durata, dimensioni, audio) si leggono da ffmpeg. La correzione automatica guarda i primi quaranta secondi:
' trova i bordi neri da ritagliare e misura luminosità, contrasto, saturazione e dominante di colore; da qui calcola i valori
' per riequilibrarli. Se è configurata un'intelligenza artificiale, le si mandano le misure e i valori calcolati e si usano quelli
' che restituisce, rimessi entro limiti prudenti; altrimenti restano quelli calcolati dal programma.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public NotInheritable Class AnalisiVideo

    Private Shared ReadOnly _log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>Lancia ffmpeg e restituisce quello che scrive sul canale degli errori e su quello normale.</summary>
    Friend Shared Function Lancia(argomenti As String, ByRef standardOutput As String, attesaMassimaMs As Integer) As String
        standardOutput = ""
        If FfmpegUtil.Percorso() = "" Then Return ""
        Using p As Process = Process.Start(FfmpegUtil.Prepara(argomenti))
            Dim errori As Task(Of String) = p.StandardError.ReadToEndAsync()
            Dim uscita As Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Try
                p.StandardInput.Close()
            Catch
            End Try
            If Not p.WaitForExit(attesaMassimaMs) Then
                Try
                    p.Kill()
                Catch
                End Try
            End If
            standardOutput = uscita.Result
            Return errori.Result
        End Using
    End Function

    Private Shared Function Numero(testo As String) As Double
        Dim v As Double
        If Double.TryParse(testo, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return 0.0
    End Function

    ''' <summary>Legge durata, dimensioni, fotogrammi al secondo e presenza dell'audio. Restituisce "" se è andata bene, altrimenti il motivo.</summary>
    Public Shared Function LeggiInfo(percorso As String, progetto As ProgettoVideo) As String
        Try
            If FfmpegUtil.Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            Dim vuoto As String = ""
            Dim testo As String = Lancia("-hide_banner -i " & FfmpegUtil.Virgolette(percorso), vuoto, 30000)
            Dim d As Match = Regex.Match(testo, "Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")
            If Not d.Success Then Return "Non è stato possibile leggere il video."
            progetto.Durata = Numero(d.Groups(1).Value) * 3600 + Numero(d.Groups(2).Value) * 60 + Numero(d.Groups(3).Value)
            Dim v As Match = Regex.Match(testo, "Stream #[^\r\n]*Video:[^\r\n]*?(\d{2,5})x(\d{2,5})")
            If Not v.Success Then Return "Il file non contiene un video."
            progetto.Larghezza = CInt(Numero(v.Groups(1).Value))
            progetto.Altezza = CInt(Numero(v.Groups(2).Value))
            Dim riga As Match = Regex.Match(testo, "Stream #[^\r\n]*Video:[^\r\n]*")
            Dim f As Match = Regex.Match(riga.Value, "(\d+(?:\.\d+)?)\s*fps")
            If f.Success AndAlso Numero(f.Groups(1).Value) >= 1 Then progetto.Fps = Numero(f.Groups(1).Value)
            Dim rotazione As Match = Regex.Match(testo, "rotation of (-?\d+(?:\.\d+)?)")
            If rotazione.Success Then
                Dim gradi As Integer = Math.Abs(CInt(Numero(rotazione.Groups(1).Value))) Mod 180
                If gradi = 90 Then
                    Dim x As Integer = progetto.Larghezza
                    progetto.Larghezza = progetto.Altezza
                    progetto.Altezza = x
                End If
            End If
            progetto.HaAudio = Regex.IsMatch(testo, "Stream #[^\r\n]*Audio:")
            If progetto.Durata < 0.2 Then Return "Il video è troppo breve."
            Return ""
        Catch ex As Exception
            _log.ScriviLog("AnalisiVideo.LeggiInfo: " & ex.Message)
            Return "Non è stato possibile leggere il video: " & ex.Message
        End Try
    End Function

    Private NotInheritable Class Misure
        Public Y As Double = 118
        Public U As Double = 128
        Public V As Double = 128
        Public Sat As Double = 50
        Public YBasso As Double = 20
        Public YAlto As Double = 220
        Public Ritaglio As String = ""
    End Class

    Private Shared Function Media(valori As Dictionary(Of String, List(Of Double)), chiave As String, predefinito As Double) As Double
        Dim l As List(Of Double) = Nothing
        If valori.TryGetValue(chiave, l) AndAlso l.Count > 0 Then Return l.Average()
        Return predefinito
    End Function

    Private Shared Function Misura(progetto As ProgettoVideo) As Misure
        Dim m As New Misure()
        Dim uscita As String = ""
        Dim errori As String = Lancia("-hide_banner -t 40 -i " & FfmpegUtil.Virgolette(progetto.Sorgente) &
                                      " -an -vf ""fps=2,cropdetect=limit=24:round=2:reset=0,signalstats,metadata=mode=print:file=-"" -f null -", uscita, 300000)
        Dim valori As New Dictionary(Of String, List(Of Double))()
        For Each riga As String In uscita.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim r As Match = Regex.Match(riga, "lavfi\.signalstats\.(\w+)=(-?\d+(?:\.\d+)?)")
            If r.Success Then
                If Not valori.ContainsKey(r.Groups(1).Value) Then valori(r.Groups(1).Value) = New List(Of Double)()
                valori(r.Groups(1).Value).Add(Numero(r.Groups(2).Value))
            End If
        Next
        m.Y = Media(valori, "YAVG", 118)
        m.U = Media(valori, "UAVG", 128)
        m.V = Media(valori, "VAVG", 128)
        m.Sat = Media(valori, "SATAVG", 50)
        m.YBasso = Media(valori, "YLOW", 20)
        m.YAlto = Media(valori, "YHIGH", 220)
        Dim conteggio As New Dictionary(Of String, Integer)()
        For Each c As Match In Regex.Matches(errori & vbLf & uscita, "crop=(\d+:\d+:\d+:\d+)")
            Dim k As String = c.Groups(1).Value
            conteggio(k) = If(conteggio.ContainsKey(k), conteggio(k) + 1, 1)
        Next
        If conteggio.Count > 0 Then
            Dim migliore As String = conteggio.OrderByDescending(Function(kv) kv.Value).First().Key
            Dim p() As String = migliore.Split(":"c)
            Dim w As Integer = CInt(Numero(p(0)))
            Dim h As Integer = CInt(Numero(p(1)))
            If w >= progetto.Larghezza * 0.5 AndAlso h >= progetto.Altezza * 0.5 AndAlso (w < progetto.Larghezza * 0.97 OrElse h < progetto.Altezza * 0.97) Then m.Ritaglio = migliore
        End If
        Return m
    End Function

    Private Shared Function Limita(v As Double, minimo As Double, massimo As Double) As Double
        Return Math.Max(minimo, Math.Min(massimo, v))
    End Function

    ''' <summary>
    ''' Calcola la correzione. soloColori e soloInquadratura restringono il lavoro: l'inquadratura è il ritaglio dei bordi neri e la
    ''' stabilizzazione. Resta vuota (non attiva) se il video non ha niente da correggere.
    ''' </summary>
    Public Shared Async Function CalcolaAsync(progetto As ProgettoVideo, colori As Boolean, inquadratura As Boolean) As Task(Of CorrezioneAutomatica)
        Dim m As Misure = Await Task.Run(Function() Misura(progetto))
        Dim c As New CorrezioneAutomatica()
        If inquadratura Then
            c.Ritaglio = m.Ritaglio
            c.Stabilizza = True
        End If
        If colori Then
            c.Colori = True
            c.Luminosita = Limita((120 - m.Y) / 255.0 * 0.7, -0.15, 0.15)
            c.Contrasto = Limita(170.0 / Math.Max(m.YAlto - m.YBasso, 60.0), 0.92, 1.35)
            c.Saturazione = Limita(55.0 / Math.Max(m.Sat, 15.0), 0.85, 1.4)
            c.Rosso = Limita(-(m.V - 128) / 128.0 * 0.6, -0.25, 0.25)
            c.Blu = Limita(-(m.U - 128) / 128.0 * 0.6, -0.25, 0.25)
            Try
                If WGAi.AlmenoUnProviderConfigurato() Then Await AffinaConIntelligenzaArtificiale(c, m)
            Catch ex As Exception
                _log.ScriviLog("AnalisiVideo.CalcolaAsync (IA): " & ex.Message)
            End Try
        End If
        Return c
    End Function

    Private Shared Async Function AffinaConIntelligenzaArtificiale(c As CorrezioneAutomatica, m As Misure) As Task
        Dim d As Func(Of Double, String) = Function(x) x.ToString("0.###", CultureInfo.InvariantCulture)
        Dim domanda As New StringBuilder()
        domanda.AppendLine("Sei un coloratore di video. Di un video ho misurato, sulla scala 0-255 dei segnali YUV, queste medie: luminosità " & d(m.Y) &
                           ", componente blu U " & d(m.U) & " (128 è neutro), componente rossa V " & d(m.V) & " (128 è neutro), saturazione " & d(m.Sat) &
                           ", livello di nero " & d(m.YBasso) & ", livello di bianco " & d(m.YAlto) & ".")
        domanda.AppendLine("Con il filtro eq di ffmpeg (brightness da -0.15 a 0.15, contrast da 0.9 a 1.4, saturation da 0.8 a 1.5, gamma da 0.8 a 1.3) e colorbalance (rosso e blu dei toni medi, da -0.25 a 0.25) i valori calcolati dal programma sono: " &
                           "luminosita " & d(c.Luminosita) & ", contrasto " & d(c.Contrasto) & ", saturazione " & d(c.Saturazione) & ", gamma 1, rosso " & d(c.Rosso) & ", blu " & d(c.Blu) & ".")
        domanda.AppendLine("Correggili se ti sembra opportuno per avere un'immagine naturale e bilanciata. Rispondi solo con un oggetto JSON con le chiavi luminosita, contrasto, saturazione, gamma, rosso, blu e numeri decimali con il punto, senza altro testo.")
        Dim risposta As Tuple(Of String, String) = Await WGAi.OttieniRispostaAsync(domanda.ToString())
        If risposta Is Nothing OrElse String.IsNullOrWhiteSpace(risposta.Item1) Then Return
        Dim testo As String = risposta.Item1
        Dim trovati As Integer = 0
        Dim leggi As Func(Of String, Double, Double, Double, Double) =
            Function(chiave As String, predefinito As Double, minimo As Double, massimo As Double)
                Dim r As Match = Regex.Match(testo, """?" & chiave & """?\s*:\s*(-?\d+(?:\.\d+)?)")
                If Not r.Success Then Return predefinito
                trovati += 1
                Return Limita(Numero(r.Groups(1).Value), minimo, massimo)
            End Function
        Dim luminosita As Double = leggi("luminosita", c.Luminosita, -0.15, 0.15)
        Dim contrasto As Double = leggi("contrasto", c.Contrasto, 0.9, 1.4)
        Dim saturazione As Double = leggi("saturazione", c.Saturazione, 0.8, 1.5)
        Dim gamma As Double = leggi("gamma", c.Gamma, 0.8, 1.3)
        Dim rosso As Double = leggi("rosso", c.Rosso, -0.25, 0.25)
        Dim blu As Double = leggi("blu", c.Blu, -0.25, 0.25)
        If trovati < 4 Then Return
        c.Luminosita = luminosita
        c.Contrasto = contrasto
        c.Saturazione = saturazione
        c.Gamma = gamma
        c.Rosso = rosso
        c.Blu = blu
        c.ConIntelligenzaArtificiale = True
    End Function

End Class
