' ============================================================================================
' ProgettoVideo - quello che si sta montando: il video di partenza e tutte le modifiche
' ============================================================================================
'
' Tutti i tempi sono quelli del video di partenza, in secondi. I tagli tolgono dei pezzi; gli effetti, il sottofondo e il voiceover
' stanno sulla stessa linea del tempo e, se cadono in un pezzo tagliato, spariscono con lui.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq

Public NotInheritable Class Intervallo
    Public Inizio As Double
    Public Fine As Double

    Public Sub New(inizio As Double, fine As Double)
        Me.Inizio = inizio
        Me.Fine = fine
    End Sub

    Public ReadOnly Property Durata As Double
        Get
            Return Fine - Inizio
        End Get
    End Property
End Class

''' <summary>Un effetto messo in un punto della linea del tempo. I suoni usano solo Inizio.</summary>
Public NotInheritable Class EffettoApplicato
    Public Id As String = ""
    Public Inizio As Double
    Public Fine As Double
    Public Volume As Integer = 100
    ''' <summary>Per l'effetto "testo": quello che si scrive sul video.</summary>
    Public Testo As String = ""

    Public ReadOnly Property Definizione As DefinizioneEffetto
        Get
            Return LibreriaEffetti.Trova(Id)
        End Get
    End Property
End Class

''' <summary>Una voce registrata o scelta da un file, che parte a un certo punto del video.</summary>
Public NotInheritable Class VoceFuoricampo
    Public Percorso As String = ""
    Public Inizio As Double
    Public Durata As Double
    Public Volume As Integer = 100
End Class

''' <summary>La correzione automatica calcolata: il ritaglio dei bordi neri, la stabilizzazione e i colori.</summary>
Public NotInheritable Class CorrezioneAutomatica
    ''' <summary>"larghezza:altezza:x:y" oppure vuoto se non c'è niente da ritagliare.</summary>
    Public Ritaglio As String = ""
    Public Stabilizza As Boolean
    Public Colori As Boolean
    Public Luminosita As Double
    Public Contrasto As Double = 1.0
    Public Saturazione As Double = 1.0
    Public Gamma As Double = 1.0
    ''' <summary>Gli spostamenti dei colori (rosso, blu) nei toni medi, da -0,3 a 0,3.</summary>
    Public Rosso As Double
    Public Blu As Double
    Public ConIntelligenzaArtificiale As Boolean

    Public ReadOnly Property Attiva As Boolean
        Get
            Return Ritaglio <> "" OrElse Stabilizza OrElse Colori
        End Get
    End Property
End Class

Public NotInheritable Class ProgettoVideo

    Public Sorgente As String = ""
    ''' <summary>Il nome con cui il progetto è salvato, e la sua cartella; vuoti se non è ancora salvato.</summary>
    Public Nome As String = ""
    Public Cartella As String = ""
    ''' <summary>Il punto in cui ci si trovava all'ultimo salvataggio: alla riapertura si riparte da lì.</summary>
    Public Posizione As Double
    Public Durata As Double
    Public Larghezza As Integer
    Public Altezza As Integer
    Public Fps As Double = 30
    Public HaAudio As Boolean

    Public ReadOnly Tagli As New List(Of Intervallo)()
    Public ReadOnly EffettiVisivi As New List(Of EffettoApplicato)()
    Public ReadOnly EffettiSonori As New List(Of EffettoApplicato)()
    Public ReadOnly Voci As New List(Of VoceFuoricampo)()
    ''' <summary>I tratti in cui l'audio originale è silenziato (il resto dell'audio, effetti e base compresi, si sente).</summary>
    Public ReadOnly Silenzi As New List(Of Intervallo)()
    ''' <summary>I punti segnati per ritrovarli: ci si va con "Segni" nel menu delle modifiche.</summary>
    Public ReadOnly Segni As New List(Of Double)()
    ''' <summary>La velocità di tutto il video: 1 è quella normale, 2 il doppio.</summary>
    Public Velocita As Double = 1.0
    ''' <summary>Secondi di dissolvenza dell'audio all'inizio e alla fine del video; 0 per nessuna.</summary>
    Public DissolvenzaAudio As Double
    Public NormalizzaAudio As Boolean
    ''' <summary>La base musicale si abbassa da sola quando c'è l'audio del video, così non lo copre.</summary>
    Public AbbassaBase As Boolean = True
    ''' <summary>0, 90, 180 o 270 gradi in senso orario.</summary>
    Public Rotazione As Integer
    ''' <summary>Il rapporto a cui ritagliare l'inquadratura al centro, come "9:16", "1:1", "16:9" o "4:5"; vuoto per non ritagliare.</summary>
    Public FormatoRitaglio As String = ""

    ''' <summary>L'audio che prende il posto di quello del video; vuoto se si tiene quello originale.</summary>
    Public AudioSostitutivo As String = ""
    Public VolumeAudioOriginale As Integer = 100
    Public BaseMusicale As String = ""
    Public VolumeBase As Integer = 40
    Public Correzione As New CorrezioneAutomatica()

    ''' <summary>Le dimensioni del video come si vedranno: tengono conto della rotazione e del ritaglio nel formato scelto.</summary>
    Public Sub DimensioniVisibili(ByRef larghezza As Integer, ByRef altezza As Integer)
        larghezza = Me.Larghezza
        altezza = Me.Altezza
        If Rotazione = 90 OrElse Rotazione = 270 Then
            larghezza = Me.Altezza
            altezza = Me.Larghezza
        End If
        If FormatoRitaglio <> "" Then
            Dim p() As String = FormatoRitaglio.Split(":"c)
            Dim a As Double = Double.Parse(p(0), CultureInfo.InvariantCulture)
            Dim b As Double = Double.Parse(p(1), CultureInfo.InvariantCulture)
            If larghezza / CDbl(altezza) > a / b Then
                larghezza = CInt(Math.Floor(altezza * a / b / 2.0)) * 2
            Else
                altezza = CInt(Math.Floor(larghezza * b / a / 2.0)) * 2
            End If
        End If
    End Sub

    Public Function Verticale() As Boolean
        Dim w, h As Integer
        DimensioniVisibili(w, h)
        Return h > w
    End Function

    ' ---- i tagli -----------------------------------------------------------------------------------------

    Public Sub AggiungiTaglio(a As Double, b As Double)
        Dim inizio As Double = Math.Max(0.0, Math.Min(a, b))
        Dim fine As Double = Math.Min(Durata, Math.Max(a, b))
        If fine - inizio < 0.02 Then Return
        Tagli.Add(New Intervallo(inizio, fine))
        UnisciTagli()
    End Sub

    Private Sub UnisciTagli()
        Dim ordinati As List(Of Intervallo) = Tagli.OrderBy(Function(t) t.Inizio).ToList()
        Tagli.Clear()
        For Each t As Intervallo In ordinati
            If Tagli.Count > 0 AndAlso t.Inizio <= Tagli(Tagli.Count - 1).Fine + 0.001 Then
                Tagli(Tagli.Count - 1).Fine = Math.Max(Tagli(Tagli.Count - 1).Fine, t.Fine)
            Else
                Tagli.Add(New Intervallo(t.Inizio, t.Fine))
            End If
        Next
    End Sub

    ''' <summary>I pezzi che restano dopo i tagli.</summary>
    Public Function PezziTenuti() As List(Of Intervallo)
        Dim pezzi As New List(Of Intervallo)()
        Dim cursore As Double = 0.0
        For Each t As Intervallo In Tagli.OrderBy(Function(x) x.Inizio)
            If t.Inizio - cursore > 0.01 Then pezzi.Add(New Intervallo(cursore, t.Inizio))
            cursore = Math.Max(cursore, t.Fine)
        Next
        If Durata - cursore > 0.01 Then pezzi.Add(New Intervallo(cursore, Durata))
        Return pezzi
    End Function

    ''' <summary>La durata del video dopo i tagli.</summary>
    Public Function DurataFinale() As Double
        Return PezziTenuti().Sum(Function(p) p.Durata) / Math.Max(0.25, Velocita)
    End Function

    ''' <summary>Se il punto cade in un pezzo tagliato, dove finisce il taglio; altrimenti il punto stesso.</summary>
    Public Function SaltaTagli(posizione As Double) As Double
        For Each t As Intervallo In Tagli
            If posizione >= t.Inizio AndAlso posizione < t.Fine Then Return t.Fine
        Next
        Return posizione
    End Function

    Public Function HaModifiche() As Boolean
        Return Tagli.Count > 0 OrElse EffettiVisivi.Count > 0 OrElse EffettiSonori.Count > 0 OrElse Voci.Count > 0 OrElse Silenzi.Count > 0 OrElse
               AudioSostitutivo <> "" OrElse BaseMusicale <> "" OrElse Correzione.Attiva OrElse VolumeAudioOriginale <> 100 OrElse
               Velocita <> 1.0 OrElse DissolvenzaAudio > 0 OrElse NormalizzaAudio OrElse Rotazione <> 0 OrElse FormatoRitaglio <> "" OrElse Segni.Count > 0
    End Function

    ''' <summary>Se l'audio cambia rispetto all'originale, l'anteprima sonora va rifatta.</summary>
    Public Function HaAudioModificato() As Boolean
        Return EffettiSonori.Count > 0 OrElse Voci.Count > 0 OrElse Silenzi.Count > 0 OrElse AudioSostitutivo <> "" OrElse BaseMusicale <> "" OrElse VolumeAudioOriginale <> 100
    End Function

    ' ---- il tempo detto a voce ---------------------------------------------------------------------------

    Public Shared Function TestoTempo(secondi As Double) As String
        If secondi < 0 Then secondi = 0
        Dim totaleMs As Long = CLng(Math.Round(secondi * 1000.0))
        Dim ore As Long = totaleMs \ 3600000L
        Dim minuti As Long = (totaleMs \ 60000L) Mod 60L
        Dim sec As Long = (totaleMs \ 1000L) Mod 60L
        Dim ms As Long = totaleMs Mod 1000L
        Dim parti As New List(Of String)()
        If ore > 0 Then parti.Add(ore.ToString() & If(ore = 1, " ora", " ore"))
        If minuti > 0 Then parti.Add(minuti.ToString() & If(minuti = 1, " minuto", " minuti"))
        If sec > 0 OrElse parti.Count = 0 OrElse ms = 0 Then parti.Add(sec.ToString() & If(sec = 1, " secondo", " secondi"))
        If ms > 0 Then parti.Add(ms.ToString() & " millisecondi")
        Return String.Join(" e ", parti)
    End Function

    ''' <summary>Il tempo che manca, detto a grandi linee.</summary>
    Public Shared Function TestoTempoApprossimato(secondi As Double) As String
        If secondi < 15 Then Return "meno di quindici secondi"
        If secondi < 50 Then Return "circa " & (CInt(Math.Round(secondi / 10.0)) * 10).ToString() & " secondi"
        Dim minuti As Integer = CInt(Math.Round(secondi / 60.0))
        If minuti <= 1 Then Return "circa un minuto"
        If minuti < 60 Then Return "circa " & minuti.ToString() & " minuti"
        Dim ore As Integer = minuti \ 60
        Dim resto As Integer = minuti Mod 60
        Return "circa " & ore.ToString() & If(ore = 1, " ora", " ore") & If(resto > 0, " e " & resto.ToString() & " minuti", "")
    End Function

    Public Function Riepilogo() As String
        Dim t As New System.Text.StringBuilder()
        t.Append("Video ").Append(Path.GetFileNameWithoutExtension(Sorgente)).Append(", ").Append(TestoTempo(Durata)).Append(", ")
        t.Append(Larghezza.ToString()).Append(" per ").Append(Altezza.ToString()).Append(If(Verticale(), ", in verticale. ", ", in orizzontale. "))
        If Tagli.Count > 0 Then t.Append(Tagli.Count.ToString()).Append(If(Tagli.Count = 1, " taglio", " tagli")).Append(", il video finale dura ").Append(TestoTempo(DurataFinale())).Append(". ")
        If EffettiVisivi.Count > 0 Then t.Append(EffettiVisivi.Count.ToString()).Append(If(EffettiVisivi.Count = 1, " effetto visivo. ", " effetti visivi. "))
        If EffettiSonori.Count > 0 Then t.Append(EffettiSonori.Count.ToString()).Append(If(EffettiSonori.Count = 1, " effetto sonoro. ", " effetti sonori. "))
        If Voci.Count > 0 Then t.Append(Voci.Count.ToString()).Append(If(Voci.Count = 1, " voiceover. ", " voiceover. "))
        If AudioSostitutivo <> "" Then t.Append("Audio sostituito con ").Append(Path.GetFileNameWithoutExtension(AudioSostitutivo)).Append(". ")
        If BaseMusicale <> "" Then t.Append("Base musicale ").Append(Path.GetFileNameWithoutExtension(BaseMusicale)).Append(", volume ").Append(VolumeBase.ToString()).Append(" per cento. ")
        If Correzione.Attiva Then t.Append("Correzione automatica attiva. ")
        If Silenzi.Count > 0 Then t.Append(Silenzi.Count.ToString()).Append(If(Silenzi.Count = 1, " tratto silenziato. ", " tratti silenziati. "))
        If Velocita <> 1.0 Then t.Append("Velocità ").Append(Velocita.ToString("0.##", CultureInfo.CurrentCulture)).Append(" volte. ")
        If Rotazione <> 0 Then t.Append("Video ruotato di ").Append(Rotazione.ToString()).Append(" gradi. ")
        If FormatoRitaglio <> "" Then t.Append("Inquadratura ritagliata a ").Append(FormatoRitaglio.Replace(":", " su ")).Append(". ")
        If DissolvenzaAudio > 0 Then t.Append("Dissolvenza dell'audio di ").Append(DissolvenzaAudio.ToString("0.##", CultureInfo.CurrentCulture)).Append(" secondi. ")
        If NormalizzaAudio Then t.Append("Volume dell'audio livellato. ")
        If Segni.Count > 0 Then t.Append(Segni.Count.ToString()).Append(If(Segni.Count = 1, " segno. ", " segni. "))
        If Not HaModifiche() Then t.Append("Nessuna modifica.")
        Return t.ToString().Trim()
    End Function

End Class
