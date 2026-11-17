' ============================================================================================
' ArchivioProgettiVideo - salvare e riaprire i progetti di montaggio
' ============================================================================================
'
' Come i progetti del registratore, un progetto di montaggio è una cartella con il suo elenco (progetto.xml), nei dati di WinGabriel
' (video_progetti), con il nome scelto dall'utente. Nell'elenco ci sono tutte le modifiche: tagli, effetti visivi e sonori, testi,
' voiceover, base musicale, silenzi, segni, velocità, rotazione, ritaglio, correzione automatica e il resto.
'
' I file non si copiano tutti. Il video originale e gli audio scelti dall'utente (base musicale, audio sostitutivo, voiceover da file)
' restano dove sono e il progetto ne ricorda il percorso; se alla riapertura qualcuno manca si avvisa. I voiceover registrati in
' montaggio, che stanno in una cartella temporanea, si copiano dentro il progetto, nella cartella "voci".
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Linq

Public NotInheritable Class ArchivioProgettiVideo

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly Cultura As CultureInfo = CultureInfo.InvariantCulture
    Private Const NomeElenco As String = "progetto.xml"

    Private Sub New()
    End Sub

    ' ---- dove si tengono i progetti ---------------------------------------------------------------------

    Public Shared Function CartellaBase() As String
        Dim c As String
        Try
            c = WGDb.WGDatabase.CartellaDati("video_progetti")
        Catch
            c = ""
        End Try
        If String.IsNullOrWhiteSpace(c) Then c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "video_progetti")
        Try
            Directory.CreateDirectory(c)
        Catch ex As Exception
            _log.ScriviLog("ArchivioProgettiVideo.CartellaBase: " & ex.Message)
        End Try
        Return c
    End Function

    Public Shared Function CartellaDi(nome As String) As String
        Return Path.Combine(CartellaBase(), AiutoMusica.NomeFileSicuro(nome))
    End Function

    ''' <summary>Le cartelle dei progetti salvati, i più recenti per primi.</summary>
    Public Shared Function Elenco() As List(Of String)
        Try
            Return Directory.GetDirectories(CartellaBase()).Where(Function(d) File.Exists(Path.Combine(d, NomeElenco))).
                OrderByDescending(Function(d) File.GetLastWriteTime(Path.Combine(d, NomeElenco))).ToList()
        Catch ex As Exception
            _log.ScriviLog("ArchivioProgettiVideo.Elenco: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    ' ---- l'elenco delle modifiche -----------------------------------------------------------------------

    Private Shared Function N(valore As Double) As String
        Return valore.ToString("R", Cultura)
    End Function

    Private Shared Function Dp(cartella As String, percorso As String) As String
        If percorso = "" Then Return ""
        If cartella <> "" Then
            Dim base As String = Path.GetFullPath(cartella).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim completo As String = Path.GetFullPath(percorso)
            If completo.StartsWith(base, StringComparison.OrdinalIgnoreCase) Then Return completo.Substring(base.Length)
        End If
        Return percorso
    End Function

    Private Shared Function Assoluto(cartella As String, percorso As String) As String
        If percorso = "" Then Return ""
        If Path.IsPathRooted(percorso) Then Return percorso
        Return Path.Combine(cartella, percorso)
    End Function

    ''' <summary>Le modifiche come testo: serve a salvare e a capire se dall'ultimo salvataggio è cambiato qualcosa.</summary>
    Public Shared Function Firma(p As ProgettoVideo, cartella As String) As String
        Return Elementi(p, cartella).ToString(SaveOptions.DisableFormatting)
    End Function

    Private Shared Function Elementi(p As ProgettoVideo, cartella As String) As XElement
        Dim r As New XElement("progetto", New XAttribute("nome", p.Nome), New XAttribute("sorgente", p.Sorgente),
                              New XAttribute("durata", N(p.Durata)), New XAttribute("larghezza", p.Larghezza), New XAttribute("altezza", p.Altezza),
                              New XAttribute("fps", N(p.Fps)), New XAttribute("haAudio", If(p.HaAudio, "true", "false")))
        r.Add(New XAttribute("velocita", N(p.Velocita)), New XAttribute("dissolvenzaAudio", N(p.DissolvenzaAudio)),
              New XAttribute("normalizzaAudio", If(p.NormalizzaAudio, "true", "false")), New XAttribute("abbassaBase", If(p.AbbassaBase, "true", "false")),
              New XAttribute("rotazione", p.Rotazione), New XAttribute("formatoRitaglio", p.FormatoRitaglio),
              New XAttribute("audioSostitutivo", Dp(cartella, p.AudioSostitutivo)), New XAttribute("volumeOriginale", p.VolumeAudioOriginale),
              New XAttribute("baseMusicale", Dp(cartella, p.BaseMusicale)), New XAttribute("volumeBase", p.VolumeBase))
        For Each t As Intervallo In p.Tagli
            r.Add(New XElement("taglio", New XAttribute("inizio", N(t.Inizio)), New XAttribute("fine", N(t.Fine))))
        Next
        For Each t As Intervallo In p.Silenzi
            r.Add(New XElement("silenzio", New XAttribute("inizio", N(t.Inizio)), New XAttribute("fine", N(t.Fine))))
        Next
        For Each s As Double In p.Segni
            r.Add(New XElement("segno", New XAttribute("tempo", N(s))))
        Next
        For Each e As EffettoApplicato In p.EffettiVisivi
            r.Add(New XElement("effettoVisivo", New XAttribute("id", e.Id), New XAttribute("inizio", N(e.Inizio)), New XAttribute("fine", N(e.Fine)), New XAttribute("testo", e.Testo)))
        Next
        For Each e As EffettoApplicato In p.EffettiSonori
            r.Add(New XElement("effettoSonoro", New XAttribute("id", e.Id), New XAttribute("inizio", N(e.Inizio)), New XAttribute("volume", e.Volume)))
        Next
        For Each v As VoceFuoricampo In p.Voci
            r.Add(New XElement("voce", New XAttribute("file", Dp(cartella, v.Percorso)), New XAttribute("inizio", N(v.Inizio)), New XAttribute("durata", N(v.Durata)), New XAttribute("volume", v.Volume)))
        Next
        Dim c As CorrezioneAutomatica = p.Correzione
        If c.Attiva Then
            r.Add(New XElement("correzione", New XAttribute("ritaglio", c.Ritaglio), New XAttribute("stabilizza", If(c.Stabilizza, "true", "false")),
                               New XAttribute("colori", If(c.Colori, "true", "false")), New XAttribute("luminosita", N(c.Luminosita)), New XAttribute("contrasto", N(c.Contrasto)),
                               New XAttribute("saturazione", N(c.Saturazione)), New XAttribute("gamma", N(c.Gamma)), New XAttribute("rosso", N(c.Rosso)),
                               New XAttribute("blu", N(c.Blu)), New XAttribute("ia", If(c.ConIntelligenzaArtificiale, "true", "false"))))
        End If
        Return r
    End Function

    ' ---- salvare ------------------------------------------------------------------------------------------

    ''' <summary>Scrive il progetto nella cartella. I voiceover registrati (in una cartella temporanea) si copiano dentro il progetto.</summary>
    Public Shared Sub Salva(p As ProgettoVideo, cartella As String)
        Directory.CreateDirectory(cartella)
        Dim completa As String = Path.GetFullPath(cartella).TrimEnd(Path.DirectorySeparatorChar)
        Dim cartellaVoci As String = Path.Combine(completa, "voci")
        Dim temporanea As String = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim tenuti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each v As VoceFuoricampo In p.Voci
            Dim daCopiare As Boolean = False
            If File.Exists(v.Percorso) Then
                Dim intero As String = Path.GetFullPath(v.Percorso)
                ' i voiceover registrati stanno in una cartella temporanea; quelli di un altro progetto (salvataggio con un altro nome) si portano qui
                daCopiare = intero.StartsWith(temporanea, StringComparison.OrdinalIgnoreCase) OrElse
                            (intero.StartsWith(Path.GetFullPath(CartellaBase()), StringComparison.OrdinalIgnoreCase) AndAlso Not intero.StartsWith(cartellaVoci, StringComparison.OrdinalIgnoreCase))
            End If
            If daCopiare Then
                Directory.CreateDirectory(cartellaVoci)
                Dim destinazione As String = Path.Combine(cartellaVoci, "v" & Guid.NewGuid().ToString("N") & ".wav")
                File.Copy(v.Percorso, destinazione, True)
                v.Percorso = destinazione
            End If
            If Path.IsPathRooted(v.Percorso) AndAlso Path.GetFullPath(v.Percorso).StartsWith(cartellaVoci, StringComparison.OrdinalIgnoreCase) Then tenuti.Add(Path.GetFullPath(v.Percorso))
        Next
        p.Cartella = completa
        Dim elenco As String = Path.Combine(completa, NomeElenco)
        Dim radice As XElement = Elementi(p, completa)
        radice.Add(New XAttribute("posizione", N(p.Posizione)))
        Dim doc As New XDocument(radice)
        doc.Save(elenco & ".tmp")
        If File.Exists(elenco) Then File.Delete(elenco)
        File.Move(elenco & ".tmp", elenco)
        If Directory.Exists(cartellaVoci) Then
            For Each f As String In Directory.GetFiles(cartellaVoci, "v*.wav")
                If Not tenuti.Contains(Path.GetFullPath(f)) Then
                    Try
                        File.Delete(f)
                    Catch
                    End Try
                End If
            Next
        End If
    End Sub

    ' ---- riaprire -----------------------------------------------------------------------------------------

    Private Shared Function Numero(testo As String, predefinito As Double) As Double
        Dim v As Double
        If Double.TryParse(testo, NumberStyles.Float, Cultura, v) Then Return v
        Return predefinito
    End Function

    Private Shared Function Testo(e As XElement, nome As String) As String
        Return If(CType(e.Attribute(nome), String), "")
    End Function

    ''' <summary>
    ''' Carica il progetto. Gli audio che non ci sono più (base musicale, audio sostitutivo, voiceover) si tolgono dal progetto e il
    ''' loro nome va in mancanti; se manca il video originale restituisce Nothing e nel primo mancante c'è il suo percorso.
    ''' </summary>
    Public Shared Function Carica(cartella As String, mancanti As List(Of String)) As ProgettoVideo
        Dim doc As XDocument = XDocument.Load(Path.Combine(cartella, NomeElenco))
        Dim r As XElement = doc.Root
        Dim p As New ProgettoVideo()
        p.Cartella = cartella
        p.Nome = Testo(r, "nome")
        If p.Nome = "" Then p.Nome = Path.GetFileName(cartella)
        p.Sorgente = Testo(r, "sorgente")
        If Not File.Exists(p.Sorgente) Then
            mancanti.Add(p.Sorgente)
            Return Nothing
        End If
        p.Durata = Numero(Testo(r, "durata"), 0.0)
        p.Larghezza = CInt(Numero(Testo(r, "larghezza"), 0.0))
        p.Altezza = CInt(Numero(Testo(r, "altezza"), 0.0))
        p.Fps = Numero(Testo(r, "fps"), 30.0)
        p.HaAudio = Testo(r, "haAudio") = "true"
        p.Posizione = Numero(Testo(r, "posizione"), 0.0)
        p.Velocita = Numero(Testo(r, "velocita"), 1.0)
        p.DissolvenzaAudio = Numero(Testo(r, "dissolvenzaAudio"), 0.0)
        p.NormalizzaAudio = Testo(r, "normalizzaAudio") = "true"
        p.AbbassaBase = Testo(r, "abbassaBase") <> "false"
        p.Rotazione = CInt(Numero(Testo(r, "rotazione"), 0.0))
        p.FormatoRitaglio = Testo(r, "formatoRitaglio")
        p.VolumeAudioOriginale = CInt(Numero(Testo(r, "volumeOriginale"), 100.0))
        p.VolumeBase = CInt(Numero(Testo(r, "volumeBase"), 40.0))
        Dim audio As String = Assoluto(cartella, Testo(r, "audioSostitutivo"))
        If audio <> "" Then
            If File.Exists(audio) Then p.AudioSostitutivo = audio Else mancanti.Add(audio)
        End If
        Dim baseMusicale As String = Assoluto(cartella, Testo(r, "baseMusicale"))
        If baseMusicale <> "" Then
            If File.Exists(baseMusicale) Then p.BaseMusicale = baseMusicale Else mancanti.Add(baseMusicale)
        End If
        For Each e As XElement In r.Elements("taglio")
            p.Tagli.Add(New Intervallo(Numero(Testo(e, "inizio"), 0.0), Numero(Testo(e, "fine"), 0.0)))
        Next
        For Each e As XElement In r.Elements("silenzio")
            p.Silenzi.Add(New Intervallo(Numero(Testo(e, "inizio"), 0.0), Numero(Testo(e, "fine"), 0.0)))
        Next
        For Each e As XElement In r.Elements("segno")
            p.Segni.Add(Numero(Testo(e, "tempo"), 0.0))
        Next
        For Each e As XElement In r.Elements("effettoVisivo")
            If LibreriaEffetti.Trova(Testo(e, "id")) Is Nothing Then Continue For
            p.EffettiVisivi.Add(New EffettoApplicato() With {.Id = Testo(e, "id"), .Inizio = Numero(Testo(e, "inizio"), 0.0), .Fine = Numero(Testo(e, "fine"), 0.0), .Testo = Testo(e, "testo")})
        Next
        For Each e As XElement In r.Elements("effettoSonoro")
            If LibreriaEffetti.Trova(Testo(e, "id")) Is Nothing Then Continue For
            p.EffettiSonori.Add(New EffettoApplicato() With {.Id = Testo(e, "id"), .Inizio = Numero(Testo(e, "inizio"), 0.0), .Volume = CInt(Numero(Testo(e, "volume"), 100.0))})
        Next
        For Each e As XElement In r.Elements("voce")
            Dim file As String = Assoluto(cartella, Testo(e, "file"))
            If Not IO.File.Exists(file) Then
                mancanti.Add(file)
                Continue For
            End If
            p.Voci.Add(New VoceFuoricampo() With {.Percorso = file, .Inizio = Numero(Testo(e, "inizio"), 0.0), .Durata = Numero(Testo(e, "durata"), 0.0), .Volume = CInt(Numero(Testo(e, "volume"), 100.0))})
        Next
        Dim c As XElement = r.Element("correzione")
        If c IsNot Nothing Then
            With p.Correzione
                .Ritaglio = Testo(c, "ritaglio")
                .Stabilizza = Testo(c, "stabilizza") = "true"
                .Colori = Testo(c, "colori") = "true"
                .Luminosita = Numero(Testo(c, "luminosita"), 0.0)
                .Contrasto = Numero(Testo(c, "contrasto"), 1.0)
                .Saturazione = Numero(Testo(c, "saturazione"), 1.0)
                .Gamma = Numero(Testo(c, "gamma"), 1.0)
                .Rosso = Numero(Testo(c, "rosso"), 0.0)
                .Blu = Numero(Testo(c, "blu"), 0.0)
                .ConIntelligenzaArtificiale = Testo(c, "ia") = "true"
            End With
        End If
        Return p
    End Function

End Class
