' ============================================================================================
' WGProgettoVocale - un progetto del registratore: le tracce di voce registrate, una sopra l'altra
' ============================================================================================
'
' Ogni registrazione del registratore è una traccia: la voce già messa al suo posto rispetto al sottofondo e alle tracce
' che si sentivano mentre la si registrava. Le tracce sono file wav; il progetto è una cartella con le tracce e un elenco
' (progetto.xml), nei dati di WinGabriel, con il nome scelto dall'utente quando esce. L'esportazione in mp3 o wav, in una
' cartella a scelta, è un'altra funzione.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Linq

''' <summary>Una traccia di voce: un file wav stereo a 48000 Hz.</summary>
Public NotInheritable Class TracciaVocale
    Public Percorso As String = ""
    Public Fotogrammi As Long
    ''' <summary>Il volume della traccia in percento (100 è quello normale): si alza e si abbassa con AltGr e le frecce.</summary>
    Public Volume As Integer = 100

    Public ReadOnly Property Secondi As Double
        Get
            Return Fotogrammi / 48000.0
        End Get
    End Property
End Class

Public NotInheritable Class ProgettoVocale

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly Cultura As CultureInfo = CultureInfo.InvariantCulture
    Private Const NomeElenco As String = "progetto.xml"

    Public Nome As String = ""
    ''' <summary>Il file audio che si sentiva durante la registrazione, se c'era.</summary>
    Public Sottofondo As String = ""
    Public SottofondoRipetuto As Boolean
    ''' <summary>Il video registrato insieme alla voce, se c'è (mp4 senza audio), e di quanti millisecondi parte prima dell'audio.</summary>
    Public Video As String = ""
    Public VideoRitardoMs As Integer
    Public VideoVerticale As Boolean
    Public ReadOnly Tracce As New List(Of TracciaVocale)()
    ''' <summary>La cartella in cui il progetto è salvato; vuota se non lo è ancora.</summary>
    Public Cartella As String = ""

    Public ReadOnly Property Durata As Long
        Get
            Dim m As Long = 0
            For Each t As TracciaVocale In Tracce
                m = Math.Max(m, t.Fotogrammi)
            Next
            Return m
        End Get
    End Property

    ' ---- dove si tengono i progetti ---------------------------------------------------------------------

    Public Shared Function CartellaBase() As String
        Dim c As String
        Try
            c = WGDb.WGDatabase.CartellaDati("voce_progetti")
        Catch
            c = ""
        End Try
        If String.IsNullOrWhiteSpace(c) Then c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "voce_progetti")
        Try
            Directory.CreateDirectory(c)
        Catch ex As Exception
            log.ScriviLog("ProgettoVocale.CartellaBase: " & ex.Message)
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
            log.ScriviLog("ProgettoVocale.Elenco: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    ' ---- salvare e riaprire -----------------------------------------------------------------------------

    ''' <summary>Scrive il progetto nella cartella: le tracce che stanno altrove (i file temporanei) vengono copiate dentro, quelle tolte si cancellano.</summary>
    Public Sub Salva(cartella As String)
        Directory.CreateDirectory(cartella)
        Dim completa As String = Path.GetFullPath(cartella).TrimEnd(Path.DirectorySeparatorChar)
        Dim radice As New XElement("progetto", New XAttribute("nome", Nome), New XAttribute("sottofondo", Sottofondo),
                                   New XAttribute("ripetuto", If(SottofondoRipetuto, "true", "false")))
        If Video <> "" AndAlso File.Exists(Video) Then
            Dim cartellaVideo As String = Path.GetFullPath(Path.GetDirectoryName(Video)).TrimEnd(Path.DirectorySeparatorChar)
            If Not String.Equals(cartellaVideo, completa, StringComparison.OrdinalIgnoreCase) Then
                File.Copy(Video, Path.Combine(completa, "video.mp4"), True)
                Video = Path.Combine(completa, "video.mp4")
            End If
            radice.Add(New XAttribute("video", Path.GetFileName(Video)), New XAttribute("videoRitardoMs", VideoRitardoMs.ToString(Cultura)),
                       New XAttribute("videoVerticale", If(VideoVerticale, "true", "false")))
        End If
        Dim tenuti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each t As TracciaVocale In Tracce
            Dim nomeFile As String
            Dim cartellaTraccia As String = Path.GetFullPath(Path.GetDirectoryName(t.Percorso)).TrimEnd(Path.DirectorySeparatorChar)
            If String.Equals(cartellaTraccia, completa, StringComparison.OrdinalIgnoreCase) Then
                nomeFile = Path.GetFileName(t.Percorso)
            Else
                nomeFile = "t" & Guid.NewGuid().ToString("N") & ".wav"
                File.Copy(t.Percorso, Path.Combine(completa, nomeFile), True)
                t.Percorso = Path.Combine(completa, nomeFile)
            End If
            tenuti.Add(nomeFile)
            radice.Add(New XElement("traccia", New XAttribute("file", nomeFile), New XAttribute("fotogrammi", t.Fotogrammi.ToString(Cultura)),
                                  New XAttribute("volume", t.Volume.ToString(Cultura))))
        Next
        Dim elenco As String = Path.Combine(completa, NomeElenco)
        Dim doc As New XDocument(radice)
        doc.Save(elenco & ".tmp")
        If File.Exists(elenco) Then File.Delete(elenco)
        File.Move(elenco & ".tmp", elenco)
        For Each f As String In Directory.GetFiles(completa, "t*.wav")
            If Not tenuti.Contains(Path.GetFileName(f)) Then
                Try
                    File.Delete(f)
                Catch
                End Try
            End If
        Next
        Cartella = completa
    End Sub

    Public Shared Function Carica(cartella As String) As ProgettoVocale
        Dim doc As XDocument = XDocument.Load(Path.Combine(cartella, NomeElenco))
        Dim p As New ProgettoVocale()
        Dim radice As XElement = doc.Root
        p.Cartella = cartella
        p.Nome = If(CType(radice.Attribute("nome"), String), Path.GetFileName(cartella))
        p.Sottofondo = If(CType(radice.Attribute("sottofondo"), String), "")
        p.SottofondoRipetuto = String.Equals(CType(radice.Attribute("ripetuto"), String), "true", StringComparison.OrdinalIgnoreCase)
        Dim nomeVideo As String = If(CType(radice.Attribute("video"), String), "")
        If nomeVideo <> "" AndAlso File.Exists(Path.Combine(cartella, nomeVideo)) Then
            p.Video = Path.Combine(cartella, nomeVideo)
            Integer.TryParse(CType(radice.Attribute("videoRitardoMs"), String), NumberStyles.Integer, Cultura, p.VideoRitardoMs)
            p.VideoVerticale = String.Equals(CType(radice.Attribute("videoVerticale"), String), "true", StringComparison.OrdinalIgnoreCase)
        End If
        For Each e As XElement In radice.Elements("traccia")
            Dim percorso As String = Path.Combine(cartella, If(CType(e.Attribute("file"), String), ""))
            If Not File.Exists(percorso) Then Continue For
            Dim t As New TracciaVocale() With {.Percorso = percorso}
            Long.TryParse(CType(e.Attribute("fotogrammi"), String), NumberStyles.Integer, Cultura, t.Fotogrammi)
            Dim volume As Integer
            If Integer.TryParse(CType(e.Attribute("volume"), String), NumberStyles.Integer, Cultura, volume) Then t.Volume = Math.Max(0, Math.Min(400, volume))
            p.Tracce.Add(t)
        Next
        Return p
    End Function

End Class
