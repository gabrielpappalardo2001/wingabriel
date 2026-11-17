' ============================================================================================
' WGOrchestra - gli strumenti dell'orchestra Sonatina, campionati da strumenti veri
' ============================================================================================
'
' La Sonatina Symphonic Orchestra è una raccolta libera (licenza Creative Commons con attribuzione) di campioni di
' un'orchestra vera: archi, legni, ottoni, percussioni, cori, pianoforte e arpa, con le note lunghe, lo staccato e il pizzicato.
' È molto più ricca dei suoni generali: ogni strumento ha il suo file (57 in tutto, circa 490 megabyte).
' WinGabriel la scarica una volta sola, la mette nella cartella dei suoni e la usa per gli strumenti dell'orchestra e,
' dove c'è, per le parti degli spartiti (il violino suona con i violini, il flauto con i flauti e così via).
' Se manca un file, per quella parte si usano i suoni generali.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class OrchestraSonatina

    Private Shared ReadOnly log As New Logger()
    Private Const UrlArchivio As String = "https://archive.org/download/SonatinaSymphonicOrchestraSF2/Sonatina%20Symphonic%20Orchestra%20SF2.zip"
    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _banchi As New Dictionary(Of String, BancoSoundFont)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>File, nome in italiano, categoria, ottava di partenza.</summary>
    Private Shared ReadOnly Tabella()() As String = {
        New String() {"Brass - Bass Trombone Solo", "Trombone basso solo", "Ottoni", "3"},
        New String() {"Brass - Horn Solo", "Corno solo", "Ottoni", "3"},
        New String() {"Brass - Horns Staccato", "Corni, staccato", "Ottoni", "3"},
        New String() {"Brass - Horns Sustain", "Corni, note lunghe", "Ottoni", "3"},
        New String() {"Brass - Tenor Trombone Solo", "Trombone tenore solo", "Ottoni", "3"},
        New String() {"Brass - Trombones Staccato", "Tromboni, staccato", "Ottoni", "3"},
        New String() {"Brass - Trombones Sustain", "Tromboni, note lunghe", "Ottoni", "3"},
        New String() {"Brass - Trumpet Solo", "Tromba solo", "Ottoni", "4"},
        New String() {"Brass - Trumpets Staccato", "Trombe, staccato", "Ottoni", "4"},
        New String() {"Brass - Trumpets Sustain", "Trombe, note lunghe", "Ottoni", "4"},
        New String() {"Brass - Tuba Staccato", "Tuba, staccato", "Ottoni", "1"},
        New String() {"Brass - Tuba Sustain", "Tuba, note lunghe", "Ottoni", "1"},
        New String() {"Chorus - Female", "Coro femminile", "Cori", "3"},
        New String() {"Chorus - Male", "Coro maschile", "Cori", "3"},
        New String() {"Chorus - Mixed", "Coro misto", "Cori", "3"},
        New String() {"Concert Harp", "Arpa da concerto", "Pianoforte e arpa", "3"},
        New String() {"Keys - Grand Piano", "Pianoforte a coda", "Pianoforte e arpa", "4"},
        New String() {"Percussion - All Percussion", "Tutte le percussioni, ogni tasto è uno strumento", "Percussioni", "3"},
        New String() {"Percussion - Bass Drum & Snare", "Grancassa e rullante", "Percussioni", "3"},
        New String() {"Percussion - Chimes", "Campane tubolari", "Percussioni", "4"},
        New String() {"Percussion - Conga", "Conga", "Percussioni", "3"},
        New String() {"Percussion - Cymbals & Tamtam", "Piatti e tam-tam", "Percussioni", "3"},
        New String() {"Percussion - Glockenspiel", "Glockenspiel", "Percussioni", "5"},
        New String() {"Percussion - Timpani", "Timpani", "Percussioni", "2"},
        New String() {"Percussion - Triangle", "Triangolo", "Percussioni", "3"},
        New String() {"Percussion - Various", "Percussioni varie", "Percussioni", "3"},
        New String() {"Percussion - Xylophone", "Xilofono", "Percussioni", "4"},
        New String() {"Strings - 1st Violins Pizzicato", "Violini primi, pizzicato", "Archi", "4"},
        New String() {"Strings - 1st Violins Staccato", "Violini primi, staccato", "Archi", "4"},
        New String() {"Strings - 1st Violins Sustain", "Violini primi, note lunghe", "Archi", "4"},
        New String() {"Strings - 2nd Violins Pizzicato", "Violini secondi, pizzicato", "Archi", "4"},
        New String() {"Strings - 2nd Violins Staccato", "Violini secondi, staccato", "Archi", "4"},
        New String() {"Strings - 2nd Violins Sustain", "Violini secondi, note lunghe", "Archi", "4"},
        New String() {"Strings - Basses Pizzicato", "Contrabbassi, pizzicato", "Archi", "1"},
        New String() {"Strings - Basses Staccato", "Contrabbassi, staccato", "Archi", "1"},
        New String() {"Strings - Basses Sustain", "Contrabbassi, note lunghe", "Archi", "1"},
        New String() {"Strings - Celli Pizzicato", "Violoncelli, pizzicato", "Archi", "2"},
        New String() {"Strings - Celli Staccato", "Violoncelli, staccato", "Archi", "2"},
        New String() {"Strings - Celli Sustain", "Violoncelli, note lunghe", "Archi", "2"},
        New String() {"Strings - Cello Solo", "Violoncello solo", "Archi", "2"},
        New String() {"Strings - Violas Pizzicato", "Viole, pizzicato", "Archi", "3"},
        New String() {"Strings - Violas Sustain", "Viole, note lunghe", "Archi", "3"},
        New String() {"Strings - Violin Solo", "Violino solo", "Archi", "4"},
        New String() {"Woodwinds - Alto Flute Solo", "Flauto contralto solo", "Legni", "5"},
        New String() {"Woodwinds - Bass Clarinet Solo", "Clarinetto basso solo", "Legni", "4"},
        New String() {"Woodwinds - Bassoon Solo", "Fagotto solo", "Legni", "2"},
        New String() {"Woodwinds - Bassoons Sustain", "Fagotti, note lunghe", "Legni", "2"},
        New String() {"Woodwinds - Clarinet Solo", "Clarinetto solo", "Legni", "4"},
        New String() {"Woodwinds - Clarinets Sustain", "Clarinetti, note lunghe", "Legni", "4"},
        New String() {"Woodwinds - Contrabassoon Solo", "Controfagotto solo", "Legni", "1"},
        New String() {"Woodwinds - Cor Anglais Solo", "Corno inglese solo", "Legni", "4"},
        New String() {"Woodwinds - Flute Solo", "Flauto solo", "Legni", "5"},
        New String() {"Woodwinds - Flutes Staccato", "Flauti, staccato", "Legni", "5"},
        New String() {"Woodwinds - Flutes Sustain", "Flauti, note lunghe", "Legni", "5"},
        New String() {"Woodwinds - Oboe Solo", "Oboe solo", "Legni", "4"},
        New String() {"Woodwinds - Oboes Sustain", "Oboi, note lunghe", "Legni", "4"},
        New String() {"Woodwinds - Piccolo Solo", "Ottavino solo", "Legni", "5"}}

    Private Sub New()
    End Sub

    Public Shared Function Cartella() As String
        Dim d As String = Path.Combine(GestoreSuoni.CartellaSuoni(), "orchestra")
        Return d
    End Function

    Public Shared Function Installata() As Boolean
        Try
            Dim d As String = Cartella()
            If Not Directory.Exists(d) Then Return False
            Return Directory.GetFiles(d, "*.sf2").Length >= 40
        Catch
            Return False
        End Try
    End Function

    Private Shared Function PercorsoFile(nomeFile As String) As String
        Return Path.Combine(Cartella(), nomeFile & ".sf2")
    End Function

    Private Shared Function Banco(nomeFile As String) As BancoSoundFont
        SyncLock _blocco
            Dim b As BancoSoundFont = Nothing
            If _banchi.TryGetValue(nomeFile, b) Then Return b
            Dim percorso As String = PercorsoFile(nomeFile)
            If Not File.Exists(percorso) Then Return Nothing
            Try
                b = BancoSoundFont.Carica(percorso)
                If b.Preset.Count = 0 Then Return Nothing
                _banchi(nomeFile) = b
                Return b
            Catch ex As Exception
                log.ScriviLog("OrchestraSonatina.Banco " & nomeFile & ": " & ex.Message)
                Return Nothing
            End Try
        End SyncLock
    End Function

    ''' <summary>Lo strumento dell'orchestra per quel file; Nothing se il file manca o non si legge.</summary>
    Public Shared Function Strumento(nomeFile As String, nome As String, ottavaIniziale As Integer) As DefinizioneStrumento
        Dim b As BancoSoundFont = Banco(nomeFile)
        If b Is Nothing Then Return Nothing
        Dim preset As PresetSf2 = b.Preset(0)
        Dim s As New DefinizioneStrumento()
        s.Nome = nome
        s.Descrizione = "Orchestra Sonatina: " & nomeFile
        s.OttavaIniziale = ottavaIniziale
        s.Origine = "orchestra"
        s.Programma = Spartito.ProgrammaDalNomeNoto(nome)
        s.Riverbero = 0.2
        s.MassimoVoci = 48
        s.Livello = 0.4F
        s.Crea = Function(nota As Integer, vel As Double) As VoceSintesi
                     Return b.CreaVoce(preset, nota, vel)
                 End Function
        Return s
    End Function

    ''' <summary>Gli strumenti dell'orchestra, per categorie, per il menu degli strumenti musicali.</summary>
    Public Shared Function Categorie() As List(Of CategoriaStrumenti)
        Dim elenco As New List(Of CategoriaStrumenti)()
        Dim indice As New Dictionary(Of String, CategoriaStrumenti)()
        For Each voce As String() In Tabella
            If Not File.Exists(PercorsoFile(voce(0))) Then Continue For
            Dim c As CategoriaStrumenti = Nothing
            If Not indice.TryGetValue(voce(2), c) Then
                c = New CategoriaStrumenti()
                c.Nome = voce(2)
                c.Descrizione = "Orchestra: " & voce(2)
                indice(voce(2)) = c
                elenco.Add(c)
            End If
            Dim nomeFile As String = voce(0)
            Dim nome As String = voce(1)
            Dim ottava As Integer = Integer.Parse(voce(3))
            Dim d As New DefinizioneStrumento()
            d.Nome = nome
            d.Descrizione = "Orchestra Sonatina: " & nomeFile
            d.OttavaIniziale = ottava
            d.Riverbero = 0.2
            d.MassimoVoci = 48
            d.Livello = 0.4F
            ' lo strumento si carica quando lo si sceglie, non all'apertura del menu
            Dim inizializzato As Boolean = False
            Dim creatore As Func(Of Integer, Double, VoceSintesi) = Nothing
            d.Crea = Function(nota As Integer, vel As Double) As VoceSintesi
                         If Not inizializzato Then
                             inizializzato = True
                             Dim reale As DefinizioneStrumento = Strumento(nomeFile, nome, ottava)
                             If reale IsNot Nothing Then creatore = reale.Crea
                         End If
                         If creatore Is Nothing Then Return Nothing
                         Return creatore(nota, vel)
                     End Function
            c.Strumenti.Add(d)
        Next
        Return elenco
    End Function

    ''' <summary>Il file dell'orchestra giusto per una parte di spartito, dal nome (violini, flauto...) e dallo strumento General MIDI; vuoto se non c'è.</summary>
    Public Shared Function FilePerParte(nomeParte As String, programma As Integer, batteria As Boolean) As String
        If batteria Then Return ""
        Dim n As String = " " & If(nomeParte, "").ToLowerInvariant() & " "
        Dim ha As Func(Of String, Boolean) = Function(t As String) n.Contains(t)
        ' una sezione (più strumenti uguali) o uno strumento solo: "2 Flutes" e "Violins 2" sono sezioni, "Violino" è uno solista
        Dim plurale As Boolean = System.Text.RegularExpressions.Regex.IsMatch(n, "^\s*[2-9]\s")
        For Each segno As String In New String() {"flauti", "flutes", "flöten", "floeten", "oboi", "oboes", "oboen", "clarinetti", "clarinets", "klarinetten",
                                                  "fagotti", "bassoons", "fagotte", "corni", "horns", "hörner", "hoerner", "cors", "trombe", "trumpets",
                                                  "trompeten", "tromboni", "trombones", "posaunen", "violini", "violins", "violons", "celli", "cellos", "violoncelli", "violoncelles"}
            If ha(segno) Then plurale = True
        Next

        If ha("timpan") Then Return "Percussion - Timpani"
        If ha("arpa") OrElse ha("harp") Then Return "Concert Harp"
        If ha("pianofort") OrElse ha(" piano") Then Return "Keys - Grand Piano"
        If ha("coro") OrElse ha("choir") OrElse ha("chorus") OrElse ha("voice") OrElse ha("voce") Then
            If ha("femmin") OrElse ha("female") OrElse ha("soprano") Then Return "Chorus - Female"
            If ha("masch") OrElse ha("male") OrElse ha("tenor") Then Return "Chorus - Male"
            Return "Chorus - Mixed"
        End If

        ' archi
        If ha("violin") Then
            If ha("second") OrElse ha(" ii") OrElse ha("2nd") OrElse ha(" 2") Then Return If(plurale, "Strings - 2nd Violins Sustain", "Strings - Violin Solo")
            If plurale Then Return "Strings - 1st Violins Sustain"
            Return "Strings - Violin Solo"
        End If
        If ha("violoncell") OrElse ha(" celli") OrElse ha(" cello") Then
            If plurale Then Return "Strings - Celli Sustain"
            Return "Strings - Cello Solo"
        End If
        If ha("viola") OrElse ha("viole") Then Return "Strings - Violas Sustain"
        If ha("contrabbass") OrElse ha("double bass") OrElse ha("basses") OrElse ha(" bass ") Then Return "Strings - Basses Sustain"

        ' legni
        If ha("ottavin") OrElse ha("piccolo") Then Return "Woodwinds - Piccolo Solo"
        If ha("corno inglese") OrElse ha("cor anglais") OrElse ha("english horn") Then Return "Woodwinds - Cor Anglais Solo"
        If ha("flauto contralto") OrElse ha("alto flute") Then Return "Woodwinds - Alto Flute Solo"
        If ha("flaut") OrElse ha("flute") OrElse ha("flöt") OrElse ha("floet") Then Return If(plurale, "Woodwinds - Flutes Sustain", "Woodwinds - Flute Solo")
        If ha("oboe") OrElse ha("oboi") Then Return If(plurale, "Woodwinds - Oboes Sustain", "Woodwinds - Oboe Solo")
        If ha("clarinetto basso") OrElse ha("bass clarinet") Then Return "Woodwinds - Bass Clarinet Solo"
        If ha("clarinett") OrElse ha("clarinet") OrElse ha("klarinett") Then Return If(plurale, "Woodwinds - Clarinets Sustain", "Woodwinds - Clarinet Solo")
        If ha("controfagott") OrElse ha("contrabassoon") Then Return "Woodwinds - Contrabassoon Solo"
        If ha("fagott") OrElse ha("bassoon") OrElse ha("basson") Then Return If(plurale, "Woodwinds - Bassoons Sustain", "Woodwinds - Bassoon Solo")

        ' ottoni
        If ha("trombone basso") OrElse ha("bass trombone") Then Return "Brass - Bass Trombone Solo"
        If ha("trombon") OrElse ha("trombone") OrElse ha("posaun") Then Return If(plurale, "Brass - Trombones Sustain", "Brass - Tenor Trombone Solo")
        If ha("tromb") OrElse ha("trumpet") OrElse ha("trompet") Then Return If(plurale, "Brass - Trumpets Sustain", "Brass - Trumpet Solo")
        If ha("tuba") Then Return "Brass - Tuba Sustain"
        If ha("corn") OrElse ha("horn") OrElse ha("hörn") OrElse ha("hoern") OrElse ha(" cors") Then Return If(plurale, "Brass - Horns Sustain", "Brass - Horn Solo")

        ' se il nome non dice nulla, lo strumento General MIDI
        Select Case programma
            Case 0 To 7 : Return "Keys - Grand Piano"
            Case 40 : Return "Strings - Violin Solo"
            Case 41 : Return "Strings - Violas Sustain"
            Case 42 : Return "Strings - Cello Solo"
            Case 43, 32 To 39 : Return "Strings - Basses Sustain"
            Case 44 To 45, 48 To 51 : Return "Strings - 1st Violins Sustain"
            Case 46 : Return "Concert Harp"
            Case 47 : Return "Percussion - Timpani"
            Case 52 To 54 : Return "Chorus - Mixed"
            Case 56, 59 : Return "Brass - Trumpet Solo"
            Case 57 : Return "Brass - Tenor Trombone Solo"
            Case 58 : Return "Brass - Tuba Sustain"
            Case 60 : Return "Brass - Horn Solo"
            Case 61 To 63 : Return "Brass - Horns Sustain"
            Case 68 : Return "Woodwinds - Oboe Solo"
            Case 69 : Return "Woodwinds - Cor Anglais Solo"
            Case 70 : Return "Woodwinds - Bassoon Solo"
            Case 71 : Return "Woodwinds - Clarinet Solo"
            Case 72 : Return "Woodwinds - Piccolo Solo"
            Case 73, 74 : Return "Woodwinds - Flute Solo"
        End Select
        Return ""
    End Function

    ''' <summary>Lo strumento dell'orchestra per una parte di spartito; Nothing se l'orchestra non c'è o non ha quello strumento.</summary>
    Public Shared Function PerParte(nomeParte As String, programma As Integer, batteria As Boolean) As DefinizioneStrumento
        If Not Installata() Then Return Nothing
        Dim file As String = FilePerParte(nomeParte, programma, batteria)
        If file = "" Then Return Nothing
        Dim ottava As Integer = 4
        Dim nome As String = nomeParte
        For Each voce As String() In Tabella
            If voce(0) = file Then
                ottava = Integer.Parse(voce(3))
                If String.IsNullOrWhiteSpace(nome) Then nome = voce(1)
                Exit For
            End If
        Next
        Return Strumento(file, nome, ottava)
    End Function

    ''' <summary>Scarica l'archivio (circa cinquecento megabyte), lo apre nella cartella dei suoni e dice con alTermine se è andata bene.</summary>
    Public Shared Sub Scarica(alTermine As Action(Of Boolean))
        Dim attesa As New WGProcessoInCorso()
        Dim annullamento As New CancellationTokenSource()
        AddHandler attesa.cancella, Sub(sender As Object, e As EventArgs) annullamento.Cancel()
        attesa.Avvia("Sto scaricando l'orchestra, circa cinquecento megabyte, può richiedere diversi minuti, attendere", True)
        Task.Run(Async Function()
                     Dim riuscito As Boolean = False
                     Dim errore As String = ""
                     Dim cartellaOrchestra As String = Cartella()
                     Dim zip As String = Path.Combine(GestoreSuoni.CartellaSuoni(), "orchestra.scarico")
                     Try
                         Directory.CreateDirectory(cartella)
                         Using client As New HttpClient()
                             client.Timeout = TimeSpan.FromHours(2)
                             client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel")
                             Using risposta As HttpResponseMessage = Await client.GetAsync(UrlArchivio, HttpCompletionOption.ResponseHeadersRead, annullamento.Token)
                                 risposta.EnsureSuccessStatusCode()
                                 Using origine As Stream = Await risposta.Content.ReadAsStreamAsync()
                                     Using destinazione As New FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, 262144)
                                         Await origine.CopyToAsync(destinazione, 262144, annullamento.Token)
                                     End Using
                                 End Using
                             End Using
                         End Using
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                           attesa.Avvia("Sto preparando gli strumenti dell'orchestra, attendere", False)
                                                       Catch
                                                       End Try
                                                   End Sub)
                         Await Task.Run(Sub()
                                            Using archivio As ZipArchive = ZipFile.OpenRead(zip)
                                                For Each voce As ZipArchiveEntry In archivio.Entries
                                                    If Not voce.Name.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase) Then Continue For
                                                    voce.ExtractToFile(Path.Combine(cartellaOrchestra, Path.GetFileName(voce.Name)), True)
                                                Next
                                            End Using
                                        End Sub)
                         SyncLock _blocco
                             _banchi.Clear()
                         End SyncLock
                         If Not Installata() Then Throw New InvalidDataException("l'archivio scaricato è incompleto")
                         riuscito = True
                     Catch ex As OperationCanceledException
                         errore = "Scaricamento annullato."
                     Catch ex As Exception
                         log.ScriviLog("OrchestraSonatina.Scarica: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         errore = "Non è stato possibile scaricare l'orchestra: " & ex.Message & ". Controlla la connessione a internet e lo spazio nel disco."
                     Finally
                         Try
                             If File.Exists(zip) Then File.Delete(zip)
                         Catch
                         End Try
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If errore <> "" Then AiutoMusica.Messaggio(errore)
                                                   alTermine(riuscito)
                                               End Sub)
                 End Function)
    End Sub

End Class
