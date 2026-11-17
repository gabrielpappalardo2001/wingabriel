' ============================================================================================
' CostruttoreMontaggio - trasforma il progetto nel grafo di filtri di ffmpeg
' ============================================================================================
'
' Il grafo lavora così: prima si prepara l'audio sulla linea del tempo del video di partenza (audio originale o sostitutivo, voiceover,
' base musicale e effetti sonori, mescolati senza che uno copra l'altro), poi si prepara il video (correzione automatica ed effetti
' visivi) e solo alla fine si tolgono i pezzi tagliati, su video e audio insieme. In questo modo gli effetti restano sul punto giusto
' anche se si taglia prima o dopo di loro.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text

Public NotInheritable Class ImpostazioniEsportazione
    ''' <summary>La misura del lato corto in pixel; 0 per quella del video di partenza.</summary>
    Public LatoCorto As Integer
    ''' <summary>"mp4-h264", "mp4-h265" o "mkv-h264".</summary>
    Public Formato As String = "mp4-h264"
    ''' <summary>0 alta, 1 media, 2 bassa.</summary>
    Public Qualita As Integer = 1
    ''' <summary>I fotogrammi al secondo; 0 per quelli del video di partenza.</summary>
    Public Fps As Integer
    Public BitrateAudio As Integer = 192
    Public Destinazione As String = ""

    Public Function Estensione() As String
        Return If(Formato.StartsWith("mkv"), ".mkv", ".mp4")
    End Function

    Public Function Descrizione(p As ProgettoVideo) As String
        Dim t As New StringBuilder()
        t.Append(If(LatoCorto = 0, "risoluzione originale", LatoCorto.ToString() & " punti sul lato corto")).Append(", ")
        t.Append(Formato.Replace("-", " ")).Append(", qualità ").Append(If(Qualita = 0, "alta", If(Qualita = 1, "media", "bassa"))).Append(", ")
        t.Append(If(Fps = 0, "fotogrammi come l'originale", Fps.ToString() & " fotogrammi al secondo")).Append(", audio a ").Append(BitrateAudio.ToString()).Append(" kilobit")
        Return t.ToString()
    End Function
End Class

Public NotInheritable Class Montaggio
    ''' <summary>Gli altri file da dare a ffmpeg, dopo il video di partenza: le opzioni da mettere prima del file e il file.</summary>
    Public ReadOnly Ingressi As New List(Of KeyValuePair(Of String, String))()
    Public Script As String = ""
    ''' <summary>La durata di quello che esce.</summary>
    Public Durata As Double
End Class

Public NotInheritable Class CostruttoreMontaggio

    Private Sub New()
    End Sub

    Private Shared Function Num(valore As Double) As String
        Return valore.ToString("0.###", CultureInfo.InvariantCulture)
    End Function

    Private Const FormatoAudio As String = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo"

    ''' <summary>
    ''' Il montaggio completo (video e audio, con i tagli e le dimensioni scelte) oppure, con soloAnteprimaAudio, solo l'audio sulla
    ''' linea del tempo intera e senza tagli: serve a farlo sentire mentre si monta.
    ''' </summary>
    Public Shared Function Costruisci(p As ProgettoVideo, imp As ImpostazioniEsportazione, soloAnteprimaAudio As Boolean) As Montaggio
        Dim m As New Montaggio()
        Dim parti As New List(Of String)()
        Dim D As String = Num(p.Durata)

        ' ---- audio sulla linea del tempo del video di partenza ----
        Dim etichetteAudio As New List(Of String)()
        If p.AudioSostitutivo <> "" Then
            m.Ingressi.Add(New KeyValuePair(Of String, String)("", p.AudioSostitutivo))
            parti.Add("[" & m.Ingressi.Count.ToString() & ":a:0]" & FormatoAudio & ",apad=whole_dur=" & D & ",atrim=0:" & D & ",asetpts=PTS-STARTPTS[base]")
        ElseIf p.HaAudio Then
            Dim silenzi As New StringBuilder()
            For Each sl As Intervallo In p.Silenzi
                silenzi.Append(",volume=0:enable='between(t,").Append(Num(sl.Inizio)).Append(",").Append(Num(sl.Fine)).Append(")'")
            Next
            parti.Add("[0:a:0]" & FormatoAudio & ",volume=" & Num(p.VolumeAudioOriginale / 100.0) & silenzi.ToString() & ",apad=whole_dur=" & D & ",atrim=0:" & D & ",asetpts=PTS-STARTPTS[base]")
        Else
            parti.Add("anullsrc=r=48000:cl=stereo,atrim=0:" & D & ",asetpts=PTS-STARTPTS[base]")
        End If
        Dim conBase As Boolean = p.BaseMusicale <> "" AndAlso p.AbbassaBase
        If conBase Then
            parti.Add("[base]asplit=2[basemix][basechiave]")
            etichetteAudio.Add("[basemix]")
        Else
            etichetteAudio.Add("[base]")
        End If
        Dim indice As Integer = 0
        For Each v As VoceFuoricampo In p.Voci
            indice += 1
            m.Ingressi.Add(New KeyValuePair(Of String, String)("", v.Percorso))
            Dim ritardo As String = CInt(v.Inizio * 1000).ToString()
            parti.Add("[" & m.Ingressi.Count.ToString() & ":a:0]" & FormatoAudio & ",volume=" & Num(v.Volume / 100.0) & ",adelay=" & ritardo & "|" & ritardo & "[voce" & indice.ToString() & "]")
            etichetteAudio.Add("[voce" & indice.ToString() & "]")
        Next
        If p.BaseMusicale <> "" Then
            m.Ingressi.Add(New KeyValuePair(Of String, String)("-stream_loop -1", p.BaseMusicale))
            Dim dissolvenza As String = If(p.Durata > 4, ",afade=t=out:st=" & Num(p.Durata - 2) & ":d=2", "")
            If conBase Then
                parti.Add("[" & m.Ingressi.Count.ToString() & ":a:0]" & FormatoAudio & ",atrim=0:" & D & ",asetpts=PTS-STARTPTS,volume=" & Num(p.VolumeBase / 100.0) & dissolvenza & "[musicagrezza]")
                parti.Add("[musicagrezza][basechiave]sidechaincompress=threshold=0.03:ratio=8:attack=40:release=600[musica]")
            Else
                parti.Add("[" & m.Ingressi.Count.ToString() & ":a:0]" & FormatoAudio & ",atrim=0:" & D & ",asetpts=PTS-STARTPTS,volume=" & Num(p.VolumeBase / 100.0) & dissolvenza & "[musica]")
            End If
            etichetteAudio.Add("[musica]")
        End If
        indice = 0
        For Each e As EffettoApplicato In p.EffettiSonori
            Dim def As DefinizioneEffetto = e.Definizione
            If def Is Nothing Then Continue For
            indice += 1
            Dim ritardo As String = CInt(e.Inizio * 1000).ToString()
            parti.Add(def.Modello & "," & FormatoAudio & ",volume=" & Num(e.Volume / 100.0) & ",adelay=" & ritardo & "|" & ritardo & "[suono" & indice.ToString() & "]")
            etichetteAudio.Add("[suono" & indice.ToString() & "]")
        Next
        If etichetteAudio.Count = 1 Then
            parti.Add(etichetteAudio(0) & "anull[amix]")
        Else
            parti.Add(String.Concat(etichetteAudio) & "amix=inputs=" & etichetteAudio.Count.ToString() & ":duration=first:normalize=0:dropout_transition=0,alimiter=limit=0.95[amix]")
        End If

        If soloAnteprimaAudio Then
            parti.Add("[amix]atrim=0:" & D & ",asetpts=PTS-STARTPTS[aout]")
            m.Script = String.Join(";", parti)
            m.Durata = p.Durata
            Return m
        End If

        ' ---- video: correzione automatica ed effetti visivi ----
        Dim filtri As New List(Of String)()
        Select Case p.Rotazione
            Case 90
                filtri.Add("transpose=1")
            Case 180
                filtri.Add("hflip,vflip")
            Case 270
                filtri.Add("transpose=2")
        End Select
        If p.FormatoRitaglio <> "" Then
            Dim r() As String = p.FormatoRitaglio.Split(":"c)
            filtri.Add("crop=w='if(gt(iw/ih," & r(0) & "/" & r(1) & "),ih*" & r(0) & "/" & r(1) & ",iw)':h='if(gt(iw/ih," & r(0) & "/" & r(1) & "),ih,iw*" & r(1) & "/" & r(0) & ")'")
        End If
        Dim c As CorrezioneAutomatica = p.Correzione
        If c.Ritaglio <> "" Then filtri.Add("crop=" & c.Ritaglio)
        If c.Stabilizza Then filtri.Add("deshake")
        If c.Colori Then
            filtri.Add("eq=brightness=" & Num(c.Luminosita) & ":contrast=" & Num(c.Contrasto) & ":saturation=" & Num(c.Saturazione) & ":gamma=" & Num(c.Gamma))
            If Math.Abs(c.Rosso) > 0.005 OrElse Math.Abs(c.Blu) > 0.005 Then filtri.Add("colorbalance=rm=" & Num(c.Rosso) & ":bm=" & Num(c.Blu))
        End If
        For Each e As EffettoApplicato In p.EffettiVisivi.OrderBy(Function(x) x.Inizio)
            Dim def As DefinizioneEffetto = e.Definizione
            If def Is Nothing Then Continue For
            filtri.Add(LibreriaEffetti.FiltroVisivo(def, e.Inizio, e.Fine, e.Testo))
        Next
        If filtri.Count = 0 Then filtri.Add("null")
        parti.Add("[0:v:0]" & String.Join(",", filtri) & "[vfx]")

        ' ---- i tagli ----
        Dim pezzi As List(Of Intervallo) = p.PezziTenuti()
        If pezzi.Count = 0 Then Throw New InvalidOperationException("Hai tagliato tutto il video.")
        m.Durata = pezzi.Sum(Function(x) x.Durata)
        Dim intero As Boolean = pezzi.Count = 1 AndAlso pezzi(0).Inizio < 0.01 AndAlso pezzi(0).Fine > p.Durata - 0.01
        Dim vFinale As String
        Dim aFinale As String
        If intero Then
            vFinale = "[vfx]"
            aFinale = "[amix]"
        Else
            Dim n As Integer = pezzi.Count
            Dim sv As New StringBuilder()
            Dim sa As New StringBuilder()
            If n > 1 Then
                Dim usciteV As New StringBuilder()
                Dim usciteA As New StringBuilder()
                For i As Integer = 0 To n - 1
                    usciteV.Append("[sv").Append(i).Append("]")
                    usciteA.Append("[sa").Append(i).Append("]")
                Next
                parti.Add("[vfx]split=" & n.ToString() & usciteV.ToString())
                parti.Add("[amix]asplit=" & n.ToString() & usciteA.ToString())
            End If
            Dim concatenati As New StringBuilder()
            For i As Integer = 0 To n - 1
                Dim ingV As String = If(n > 1, "[sv" & i.ToString() & "]", "[vfx]")
                Dim ingA As String = If(n > 1, "[sa" & i.ToString() & "]", "[amix]")
                parti.Add(ingV & "trim=start=" & Num(pezzi(i).Inizio) & ":end=" & Num(pezzi(i).Fine) & ",setpts=PTS-STARTPTS[tv" & i.ToString() & "]")
                parti.Add(ingA & "atrim=start=" & Num(pezzi(i).Inizio) & ":end=" & Num(pezzi(i).Fine) & ",asetpts=PTS-STARTPTS[ta" & i.ToString() & "]")
                concatenati.Append("[tv").Append(i).Append("][ta").Append(i).Append("]")
            Next
            If n > 1 Then
                parti.Add(concatenati.ToString() & "concat=n=" & n.ToString() & ":v=1:a=1[vc][ac]")
                vFinale = "[vc]"
                aFinale = "[ac]"
            Else
                vFinale = "[tv0]"
                aFinale = "[ta0]"
            End If
        End If

        ' ---- dimensioni, fotogrammi e formato ----
        Dim uscitaV As New List(Of String)()
        If imp.LatoCorto > 0 Then
            Dim lato As Integer = Math.Max(120, imp.LatoCorto)
            ' non si ingrandisce mai: se il video è già più piccolo resta com'è
            uscitaV.Add(If(p.Verticale(), "scale='min(" & lato.ToString() & ",iw)':-2:flags=lanczos", "scale=-2:'min(" & lato.ToString() & ",ih)':flags=lanczos"))
        End If
        uscitaV.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2")
        If imp.Fps > 0 Then uscitaV.Add("fps=" & imp.Fps.ToString())
        uscitaV.Add("format=yuv420p")
        Dim velocita As Double = Math.Max(0.25, Math.Min(4.0, p.Velocita))
        If velocita <> 1.0 Then uscitaV.Insert(0, "setpts=PTS/" & Num(velocita))
        parti.Add(vFinale & String.Join(",", uscitaV) & "[vout]")
        Dim uscitaA As New List(Of String)()
        uscitaA.Add(FormatoAudio)
        If velocita <> 1.0 Then
            Dim resto As Double = velocita
            While resto > 2.0
                uscitaA.Add("atempo=2.0")
                resto /= 2.0
            End While
            While resto < 0.5
                uscitaA.Add("atempo=0.5")
                resto /= 0.5
            End While
            uscitaA.Add("atempo=" & Num(resto))
        End If
        Dim durataUscita As Double = m.Durata / velocita
        If p.DissolvenzaAudio > 0 AndAlso durataUscita > p.DissolvenzaAudio * 2 + 0.5 Then
            uscitaA.Add("afade=t=in:st=0:d=" & Num(p.DissolvenzaAudio))
            uscitaA.Add("afade=t=out:st=" & Num(durataUscita - p.DissolvenzaAudio) & ":d=" & Num(p.DissolvenzaAudio))
        End If
        If p.NormalizzaAudio Then uscitaA.Add("loudnorm=I=-16:TP=-1.5:LRA=11,aformat=sample_rates=48000")
        parti.Add(aFinale & String.Join(",", uscitaA) & "[aout]")
        m.Durata = durataUscita
        m.Script = String.Join(";", parti)
        Return m
    End Function

    ''' <summary>Le opzioni dell'encoder video. gpu è "h264_nvenc", "h264_qsv", "h264_amf" o vuoto per il processore.</summary>
    Public Shared Function OpzioniVideo(imp As ImpostazioniEsportazione, gpu As String) As String
        Dim h265 As Boolean = imp.Formato.Contains("h265")
        Dim q As Integer = If(imp.Qualita = 0, 19, If(imp.Qualita = 1, 23, 28))
        Dim tag As String = If(h265 AndAlso Not imp.Formato.StartsWith("mkv"), " -tag:v hvc1", "")
        If gpu = "" Then
            Return If(h265, "-c:v libx265 -preset medium -crf " & q.ToString() & tag, "-c:v libx264 -preset medium -crf " & q.ToString())
        End If
        Dim nome As String = If(h265, gpu.Replace("h264", "hevc"), gpu)
        If gpu.EndsWith("nvenc") Then Return "-c:v " & nome & " -preset p5 -rc vbr -cq " & q.ToString() & " -b:v 0" & tag
        If gpu.EndsWith("qsv") Then Return "-c:v " & nome & " -global_quality " & q.ToString() & " -preset medium" & tag
        Return "-c:v " & nome & " -quality quality -rc cqp -qp_i " & q.ToString() & " -qp_p " & q.ToString() & " -qp_b " & q.ToString() & tag
    End Function

End Class
