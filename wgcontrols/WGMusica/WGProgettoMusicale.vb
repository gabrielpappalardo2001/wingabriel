' ============================================================================================
' WGProgettoMusicale - un progetto degli strumenti musicali: le tracce suonate, ciascuna con il suo strumento
' ============================================================================================
'
' Ogni registrazione fatta negli strumenti è una traccia: l'elenco delle note suonate (quando, quanto, quale e con che forza) e
' lo strumento. Le note, e non il suono, sono quello che si conserva: così il progetto si può riascoltare, completare con altre
' tracce ed esportare in MusicXML, MIDI, mp3 e wav. Il progetto si salva nei dati di WinGabriel, con il nome scelto dall'utente
' quando esce dall'ambiente; l'esportazione in una cartella a scelta è un'altra funzione.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Linq

''' <summary>Una traccia del progetto: lo strumento e le note suonate.</summary>
Public NotInheritable Class TracciaProgetto
    Public Strumento As DefinizioneStrumento
    ''' <summary>Come si ritrova lo strumento quando si riapre il progetto: dove sta (sf, orchestra, sintetico) e come si chiama.</summary>
    Public Origine As String = ""
    Public NomeStrumento As String = ""
    Public Percussivo As Boolean
    Public Programma As Integer = -1
    ''' <summary>Il volume della traccia in percento (100 è quello normale): si alza e si abbassa con AltGr e le frecce.</summary>
    Public Volume As Integer = 100
    Public ReadOnly Note As New List(Of NotaSpartito)()

    Public ReadOnly Property Fine As Double
        Get
            Dim m As Double = 0.0
            For Each n As NotaSpartito In Note
                m = Math.Max(m, n.Inizio + n.Durata)
            Next
            Return m
        End Get
    End Property

    Public Shared Function Da(strumento As DefinizioneStrumento) As TracciaProgetto
        Dim t As New TracciaProgetto()
        t.Strumento = strumento
        t.Origine = If(strumento.Origine, "")
        t.NomeStrumento = strumento.Nome
        t.Percussivo = strumento.Percussivo
        t.Programma = strumento.Programma
        Return t
    End Function
End Class

Public NotInheritable Class ProgettoMusicale

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly Cultura As CultureInfo = CultureInfo.InvariantCulture

    Public Nome As String = ""
    Public ReadOnly Tracce As New List(Of TracciaProgetto)()
    ''' <summary>Il file audio che suonava durante la registrazione, se c'era.</summary>
    Public Sottofondo As String = ""
    Public SottofondoRipetuto As Boolean
    ''' <summary>Il tempo (battiti al minuto) e i battiti per battuta del metronomo con cui si è registrato: servono a scrivere il ritmo nell'esportazione.</summary>
    Public Tempo As Integer = 120
    Public Battiti As Integer = 4

    Public ReadOnly Property Durata As Double
        Get
            Dim m As Double = 0.0
            For Each t As TracciaProgetto In Tracce
                m = Math.Max(m, t.Fine)
            Next
            Return m
        End Get
    End Property

    ''' <summary>Le tracce come uno spartito, per farle suonare dal riproduttore. Le parti seguono l'ordine delle tracce.</summary>
    Public Function InSpartito() As Spartito
        Dim s As New Spartito()
        s.Titolo = Nome
        For Each t As TracciaProgetto In Tracce
            Dim p As New ParteSpartito()
            p.Nome = t.NomeStrumento
            p.Batteria = t.Percussivo
            p.Programma = Math.Max(0, t.Programma)
            p.Note.AddRange(t.Note)
            s.Parti.Add(p)
        Next
        s.Durata = Durata
        s.Battute.Add(0.0)
        Return s
    End Function

    Public Function Strumenti() As List(Of DefinizioneStrumento)
        Return Tracce.Select(Function(t) t.Strumento).ToList()
    End Function

    ''' <summary>Il volume di ogni traccia come moltiplicatore (1 è quello normale).</summary>
    Public Function Guadagni() As List(Of Double)
        Return Tracce.Select(Function(t) t.Volume / 100.0).ToList()
    End Function

    ' ---- dove si tengono i progetti ---------------------------------------------------------------------

    Public Shared Function Cartella() As String
        Dim c As String
        Try
            c = WGDb.WGDatabase.CartellaDati("musica_progetti")
        Catch
            c = ""
        End Try
        If String.IsNullOrWhiteSpace(c) Then c = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "musica_progetti")
        Try
            Directory.CreateDirectory(c)
        Catch ex As Exception
            log.ScriviLog("ProgettoMusicale.Cartella: " & ex.Message)
        End Try
        Return c
    End Function

    ''' <summary>I progetti salvati, i più recenti per primi.</summary>
    Public Shared Function Elenco() As List(Of String)
        Try
            Return Directory.GetFiles(Cartella(), "*.wgprogetto").OrderByDescending(Function(f) File.GetLastWriteTime(f)).ToList()
        Catch ex As Exception
            log.ScriviLog("ProgettoMusicale.Elenco: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    Public Shared Function PercorsoDi(nome As String) As String
        Return Path.Combine(Cartella(), AiutoMusica.NomeFileSicuro(nome) & ".wgprogetto")
    End Function

    ' ---- salvare e riaprire -----------------------------------------------------------------------------

    Public Sub Salva(percorso As String)
        Dim radice As New XElement("progetto", New XAttribute("nome", Nome), New XAttribute("sottofondo", Sottofondo),
                                   New XAttribute("ripetuto", If(SottofondoRipetuto, "true", "false")),
                                   New XAttribute("tempo", Tempo.ToString(Cultura)), New XAttribute("battiti", Battiti.ToString(Cultura)))
        For Each t As TracciaProgetto In Tracce
            Dim e As New XElement("traccia", New XAttribute("strumento", t.NomeStrumento), New XAttribute("origine", t.Origine),
                                  New XAttribute("percussivo", If(t.Percussivo, "true", "false")), New XAttribute("programma", t.Programma.ToString(Cultura)),
                                  New XAttribute("volume", t.Volume.ToString(Cultura)))
            For Each n As NotaSpartito In t.Note
                e.Add(New XElement("n", New XAttribute("i", n.Inizio.ToString("0.####", Cultura)), New XAttribute("d", n.Durata.ToString("0.####", Cultura)),
                                   New XAttribute("n", n.Nota.ToString(Cultura)), New XAttribute("v", n.Velocita.ToString("0.##", Cultura))))
            Next
            radice.Add(e)
        Next
        Dim temporaneo As String = percorso & ".tmp"
        Dim doc As New XDocument(radice)
        doc.Save(temporaneo)
        If File.Exists(percorso) Then File.Delete(percorso)
        File.Move(temporaneo, percorso)
    End Sub

    ''' <summary>Riapre un progetto. risolvi trova lo strumento (origine, nome); può dare Nothing se non è disponibile.</summary>
    Public Shared Function Carica(percorso As String, risolvi As Func(Of String, String, DefinizioneStrumento)) As ProgettoMusicale
        Dim doc As XDocument = XDocument.Load(percorso)
        Dim p As New ProgettoMusicale()
        Dim radice As XElement = doc.Root
        p.Nome = If(CType(radice.Attribute("nome"), String), Path.GetFileNameWithoutExtension(percorso))
        p.Sottofondo = If(CType(radice.Attribute("sottofondo"), String), "")
        p.SottofondoRipetuto = String.Equals(CType(radice.Attribute("ripetuto"), String), "true", StringComparison.OrdinalIgnoreCase)
        Integer.TryParse(CType(radice.Attribute("tempo"), String), NumberStyles.Integer, Cultura, p.Tempo)
        Integer.TryParse(CType(radice.Attribute("battiti"), String), NumberStyles.Integer, Cultura, p.Battiti)
        If p.Tempo < 20 OrElse p.Tempo > 400 Then p.Tempo = 120
        If p.Battiti < 1 OrElse p.Battiti > 16 Then p.Battiti = 4
        For Each e As XElement In radice.Elements("traccia")
            Dim t As New TracciaProgetto()
            t.NomeStrumento = If(CType(e.Attribute("strumento"), String), "")
            t.Origine = If(CType(e.Attribute("origine"), String), "")
            t.Percussivo = String.Equals(CType(e.Attribute("percussivo"), String), "true", StringComparison.OrdinalIgnoreCase)
            Integer.TryParse(CType(e.Attribute("programma"), String), NumberStyles.Integer, Cultura, t.Programma)
            Dim volume As Integer
            If Integer.TryParse(CType(e.Attribute("volume"), String), NumberStyles.Integer, Cultura, volume) Then t.Volume = Math.Max(0, Math.Min(400, volume))
            t.Strumento = risolvi(t.Origine, t.NomeStrumento)
            For Each n As XElement In e.Elements("n")
                Dim nota As New NotaSpartito()
                Double.TryParse(CType(n.Attribute("i"), String), NumberStyles.Float, Cultura, nota.Inizio)
                Double.TryParse(CType(n.Attribute("d"), String), NumberStyles.Float, Cultura, nota.Durata)
                Integer.TryParse(CType(n.Attribute("n"), String), NumberStyles.Integer, Cultura, nota.Nota)
                Double.TryParse(CType(n.Attribute("v"), String), NumberStyles.Float, Cultura, nota.Velocita)
                t.Note.Add(nota)
            Next
            p.Tracce.Add(t)
        Next
        Return p
    End Function

    ''' <summary>Trova lo strumento per origine e nome fra quelli che WinGabriel sa suonare; Nothing se non c'è (per esempio i suoni ad alta qualità non scaricati).</summary>
    Public Shared Function TrovaStrumento(origine As String, nome As String, banco As BancoSoundFont) As DefinizioneStrumento
        Try
            Dim categorie As List(Of CategoriaStrumenti)
            Select Case origine
                Case "sf"
                    If banco Is Nothing Then Return Nothing
                    categorie = CatalogoSoundFont.Categorie(banco)
                Case "orchestra"
                    categorie = OrchestraSonatina.Categorie()
                Case Else
                    categorie = CatalogoStrumenti.Categorie()
            End Select
            For Each c As CategoriaStrumenti In categorie
                For Each s As DefinizioneStrumento In c.Strumenti
                    If s.Nome = nome Then Return s
                Next
            Next
        Catch ex As Exception
            log.ScriviLog("ProgettoMusicale.TrovaStrumento: " & ex.Message)
        End Try
        Return Nothing
    End Function

End Class
