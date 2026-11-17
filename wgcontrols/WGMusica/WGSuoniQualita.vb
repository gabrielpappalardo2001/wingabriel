' ============================================================================================
' WGSuoniQualita - i suoni ad alta qualità (SoundFont): dove sono, come si scaricano, come diventano strumenti
' ============================================================================================
Imports System
Imports WGDb
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class GestoreSuoni

    ''' <summary>GeneralUser GS: un SoundFont completo (circa 32 megabyte) di libero uso, anche in programmi propri.</summary>
    Public Const UrlSoundFont As String = "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main/GeneralUser-GS.sf2"
    Public Const IdConfigurazione As String = "musica_soundfont"

    Private Shared ReadOnly log As New Logger()
    Private Shared _banco As BancoSoundFont
    Private Shared ReadOnly _blocco As New Object()

    Private Sub New()
    End Sub

    Public Shared Function CartellaSuoni() As String
        Try
            ' i suoni stanno nella radice dell'applicazione, che può essere diversa dalla cartella del programma, e non nei dati
            Dim cartella As String = WGPercorsiApplicazione.CartellaComponente("suoni")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch ex As Exception
            log.ScriviLog("GestoreSuoni.CartellaSuoni: " & ex.Message)
        End Try
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "suoni")
    End Function

    Public Shared Function PercorsoPredefinito() As String
        Return Path.Combine(CartellaSuoni(), "GeneralUser-GS.sf2")
    End Function

    ''' <summary>Il file SoundFont in uso: quello scelto dall'utente, altrimenti quello scaricato; vuoto se non ce ne sono.</summary>
    Public Shared Function PercorsoCorrente() As String
        Try
            Dim scelto As String = ImpostazioniMusica.Leggi(IdConfigurazione, "")
            If scelto <> "" AndAlso File.Exists(scelto) Then Return scelto
            Dim predefinito As String = PercorsoPredefinito()
            If File.Exists(predefinito) AndAlso New FileInfo(predefinito).Length > 1000000L Then Return predefinito
        Catch ex As Exception
            log.ScriviLog("GestoreSuoni.PercorsoCorrente: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Shared Function Disponibile() As Boolean
        Return PercorsoCorrente() <> ""
    End Function

    Public Shared Sub ImpostaFile(percorso As String)
        ImpostazioniMusica.Scrivi(IdConfigurazione, percorso)
        SyncLock _blocco
            _banco = Nothing
        End SyncLock
    End Sub

    ''' <summary>Chiama alTermine con i suoni caricati (o Nothing, dopo aver detto l'errore). Se servono un po' di secondi mostra l'attesa.</summary>
    Public Shared Sub Carica(alTermine As Action(Of BancoSoundFont))
        Dim percorso As String = PercorsoCorrente()
        If percorso = "" Then
            alTermine(Nothing)
            Return
        End If
        SyncLock _blocco
            If _banco IsNot Nothing AndAlso String.Equals(_banco.Percorso, percorso, StringComparison.OrdinalIgnoreCase) Then
                alTermine(_banco)
                Return
            End If
        End SyncLock
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto caricando i suoni, attendere", False)
        Task.Run(Sub()
                     Dim banco As BancoSoundFont = Nothing
                     Dim errore As String = ""
                     Try
                         banco = BancoSoundFont.Carica(percorso)
                         SyncLock _blocco
                             _banco = banco
                         End SyncLock
                     Catch ex As Exception
                         log.ScriviLog("GestoreSuoni.Carica: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         errore = "Non è stato possibile leggere il file dei suoni: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If errore <> "" Then AiutoMusica.Messaggio(errore)
                                                   alTermine(banco)
                                               End Sub)
                 End Sub)
    End Sub

    Private Shared _dialogo As WGFileDialog
    Private Shared _alTermineScelta As Action(Of Boolean)

    ''' <summary>Fa scegliere un file .sf2 col selettore di WinGabriel, lo carica e dice con alTermine se è andata bene.</summary>
    Public Shared Sub ScegliFile(alTermine As Action(Of Boolean))
        Try
            _alTermineScelta = alTermine
            _dialogo = New WGFileDialog()
            _dialogo.MultiselezioneConsentita = False
            _dialogo.AggiungiEstensione(".sf2")
            AddHandler _dialogo.selezioneterminata, AddressOf FileScelto
            _dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("GestoreSuoni.ScegliFile: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub FileScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogo.selezioneterminata, AddressOf FileScelto
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            ImpostaFile(percorso)
            Dim alTermine As Action(Of Boolean) = _alTermineScelta
            Carica(Sub(banco As BancoSoundFont)
                       If banco Is Nothing Then ImpostaFile("")
                       If alTermine IsNot Nothing Then alTermine(banco IsNot Nothing)
                   End Sub)
        Catch ex As Exception
            log.ScriviLog("GestoreSuoni.FileScelto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Scarica il SoundFont nella cartella "suoni" del programma. alTermine dice se è andata bene.</summary>
    Public Shared Sub Scarica(alTermine As Action(Of Boolean))
        Dim attesa As New WGProcessoInCorso()
        Dim annullamento As New CancellationTokenSource()
        AddHandler attesa.cancella, Sub(sender As Object, e As EventArgs) annullamento.Cancel()
        attesa.Avvia("Sto scaricando i suoni ad alta qualità, circa trentadue megabyte, attendere", True)
        Task.Run(Async Function()
                     Dim riuscito As Boolean = False
                     Dim errore As String = ""
                     Dim temporaneo As String = PercorsoPredefinito() & ".scarico"
                     Try
                         Directory.CreateDirectory(CartellaSuoni())
                         Using client As New HttpClient()
                             client.Timeout = TimeSpan.FromMinutes(30)
                             Using risposta As HttpResponseMessage = Await client.GetAsync(UrlSoundFont, HttpCompletionOption.ResponseHeadersRead, annullamento.Token)
                                 risposta.EnsureSuccessStatusCode()
                                 Using origine As Stream = Await risposta.Content.ReadAsStreamAsync()
                                     Using destinazione As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
                                         Await origine.CopyToAsync(destinazione, 81920, annullamento.Token)
                                     End Using
                                 End Using
                             End Using
                         End Using
                         If New FileInfo(temporaneo).Length < 1000000L Then Throw New InvalidDataException("il file scaricato è incompleto")
                         Dim prova As BancoSoundFont = BancoSoundFont.Carica(temporaneo)
                         If prova.Preset.Count = 0 Then Throw New InvalidDataException("il file scaricato non contiene strumenti")
                         If File.Exists(PercorsoPredefinito()) Then File.Delete(PercorsoPredefinito())
                         File.Move(temporaneo, PercorsoPredefinito())
                         ImpostazioniMusica.Scrivi(IdConfigurazione, "")
                         SyncLock _blocco
                             _banco = Nothing
                         End SyncLock
                         riuscito = True
                     Catch ex As OperationCanceledException
                         errore = "Scaricamento annullato."
                     Catch ex As Exception
                         log.ScriviLog("GestoreSuoni.Scarica: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         errore = "Non è stato possibile scaricare i suoni: " & ex.Message & ". Controlla la connessione a internet."
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
                                                   If errore <> "" Then AiutoMusica.Messaggio(errore)
                                                   alTermine(riuscito)
                                               End Sub)
                 End Function)
    End Sub

End Class

''' <summary>Dagli strumenti di un SoundFont alle categorie di WinGabriel (le famiglie del General MIDI).</summary>
Public NotInheritable Class CatalogoSoundFont

    Private Sub New()
    End Sub

    Private Shared ReadOnly Famiglie() As String = {
        "Pianoforti", "Percussioni intonate", "Organi e fisarmonica", "Chitarre", "Bassi", "Archi", "Cori e orchestre", "Ottoni",
        "Ance e sassofoni", "Flauti", "Sintetizzatori solisti", "Sintetizzatori di sfondo", "Effetti sintetici", "Strumenti etnici",
        "Percussioni varie", "Effetti sonori"}

    Private Shared ReadOnly NomiProgrammi() As String = {
        "Pianoforte a coda", "Pianoforte brillante", "Pianoforte a coda elettrico", "Pianoforte honky-tonk", "Piano elettrico 1", "Piano elettrico 2", "Clavicembalo", "Clavicordo",
        "Celesta", "Glockenspiel", "Carillon", "Vibrafono", "Marimba", "Xilofono", "Campane tubolari", "Dulcimer",
        "Organo Hammond", "Organo a percussione", "Organo rock", "Organo da chiesa", "Organo ad ance", "Fisarmonica", "Armonica a bocca", "Bandoneon",
        "Chitarra classica", "Chitarra acustica", "Chitarra elettrica jazz", "Chitarra elettrica pulita", "Chitarra elettrica con sordina", "Chitarra overdrive", "Chitarra distorta", "Armonici di chitarra",
        "Basso acustico", "Basso elettrico con le dita", "Basso elettrico con il plettro", "Basso senza tasti", "Basso slap 1", "Basso slap 2", "Basso sintetico 1", "Basso sintetico 2",
        "Violino", "Viola", "Violoncello", "Contrabbasso", "Archi tremolo", "Archi pizzicati", "Arpa", "Timpani",
        "Orchestra d'archi 1", "Orchestra d'archi 2", "Archi sintetici 1", "Archi sintetici 2", "Coro aah", "Voci ooh", "Voce sintetica", "Colpo d'orchestra",
        "Tromba", "Trombone", "Tuba", "Tromba con sordina", "Corno francese", "Sezione di ottoni", "Ottoni sintetici 1", "Ottoni sintetici 2",
        "Sassofono soprano", "Sassofono contralto", "Sassofono tenore", "Sassofono baritono", "Oboe", "Corno inglese", "Fagotto", "Clarinetto",
        "Ottavino", "Flauto traverso", "Flauto dolce", "Flauto di Pan", "Bottiglia soffiata", "Shakuhachi", "Fischio", "Ocarina",
        "Solista a onda quadra", "Solista a dente di sega", "Solista calliope", "Solista chiff", "Solista charang", "Solista voce", "Solista a quinte", "Solista basso e solista",
        "Sfondo nuova era", "Sfondo caldo", "Sfondo polisintetizzatore", "Sfondo corale", "Sfondo ad arco", "Sfondo metallico", "Sfondo alone", "Sfondo spazzata",
        "Pioggia", "Colonna sonora", "Cristalli", "Atmosfera", "Luminosità", "Folletti", "Echi", "Fantascienza",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Cornamusa", "Violino popolare", "Shanai",
        "Campanelli tintinnanti", "Agogo", "Steel drum", "Blocco di legno", "Tamburo taiko", "Tom melodico", "Tamburo sintetico", "Piatto al contrario",
        "Rumore di tasto di chitarra", "Respiro", "Riva del mare", "Cinguettio di uccelli", "Squillo di telefono", "Elicottero", "Applauso", "Sparo"}

    ''' <summary>Le 32 note della batteria General MIDI assegnate ai tasti, nell'ordine dei tasti.</summary>
    ''' <summary>La nota General MIDI del pezzo di batteria che ha quel numero d'ordine (quello dei tasti).</summary>
    Public Shared Function NotaGmBatteria(indice As Integer) As Integer
        Return NoteBatteria(((indice Mod NoteBatteria.Length) + NoteBatteria.Length) Mod NoteBatteria.Length)
    End Function

    Private Shared ReadOnly NoteBatteria() As Integer = {36, 35, 38, 37, 42, 46, 44, 50, 39, 47, 56, 45, 49, 55, 51, 52, 53,
                                                         54, 57, 48, 59, 43, 60, 61, 63, 64, 65, 66, 67, 68, 69, 70}

    Private Shared Function NomeNotaBatteria(nota As Integer) As String
        Select Case nota
            Case 35 : Return "Cassa acustica"
            Case 36 : Return "Cassa"
            Case 37 : Return "Colpo sul bordo"
            Case 38 : Return "Rullante"
            Case 39 : Return "Battimani"
            Case 40 : Return "Rullante elettrico"
            Case 41 : Return "Tom basso"
            Case 42 : Return "Charleston chiuso"
            Case 43 : Return "Tom del pavimento"
            Case 44 : Return "Charleston a pedale"
            Case 45 : Return "Tom grave"
            Case 46 : Return "Charleston aperto"
            Case 47 : Return "Tom medio"
            Case 48 : Return "Tom medio alto"
            Case 49 : Return "Piatto crash"
            Case 50 : Return "Tom alto"
            Case 51 : Return "Piatto ride"
            Case 52 : Return "Piatto cinese"
            Case 53 : Return "Campana del ride"
            Case 54 : Return "Tamburello"
            Case 55 : Return "Splash"
            Case 56 : Return "Campanaccio"
            Case 57 : Return "Secondo crash"
            Case 59 : Return "Secondo ride"
            Case 60 : Return "Bongo alto"
            Case 61 : Return "Bongo basso"
            Case 63 : Return "Conga alta"
            Case 64 : Return "Conga bassa"
            Case 65 : Return "Timbale alto"
            Case 66 : Return "Timbale basso"
            Case 67 : Return "Agogo alto"
            Case 68 : Return "Agogo basso"
            Case 69 : Return "Cabasa"
            Case 70 : Return "Maracas"
            Case Else : Return "Percussione " & nota.ToString()
        End Select
    End Function

    Private Shared Function NomeBatteria(p As PresetSf2) As String
        Select Case p.Programma
            Case 0 : Return "Batteria standard"
            Case 8 : Return "Batteria room"
            Case 16 : Return "Batteria power"
            Case 24 : Return "Batteria elettronica"
            Case 25 : Return "Batteria TR-808"
            Case 32 : Return "Batteria jazz"
            Case 40 : Return "Batteria con le spazzole"
            Case 48 : Return "Percussioni orchestrali"
            Case 56 : Return "Effetti sonori"
            Case Else : Return "Batteria " & p.Nome
        End Select
    End Function

    Private Shared Function OttavaIniziale(programma As Integer) As Integer
        Select Case programma
            Case 32 To 39, 43, 58 : Return 2
            Case 16 To 31, 41, 42, 56 To 57, 60, 61 : Return 3
            Case 0 To 15, 44 To 47 : Return 4
            Case Else : Return 4
        End Select
    End Function

    ''' <summary>Lo strumento General MIDI col numero indicato (da 0 a 127), o la batteria: in quel caso la nota che si suona è la nota MIDI della percussione.</summary>
    Public Shared Function NomiFamiglie() As String()
        Return DirectCast(Famiglie.Clone(), String())
    End Function

    Public Shared Function NomeProgramma(programma As Integer) As String
        Return NomiProgrammi(Math.Max(0, Math.Min(127, programma)))
    End Function

    Public Shared Function StrumentoGM(banco As BancoSoundFont, programma As Integer, batteria As Boolean) As DefinizioneStrumento
        Dim scelto As PresetSf2 = Nothing
        If batteria Then
            For Each p As PresetSf2 In banco.Preset
                If p.Banca = 128 AndAlso (scelto Is Nothing OrElse (p.Programma <= programma AndAlso p.Programma > scelto.Programma) OrElse (scelto.Programma > programma AndAlso p.Programma < scelto.Programma)) Then scelto = p
            Next
        Else
            programma = Math.Max(0, Math.Min(127, programma))
            For Each p As PresetSf2 In banco.Preset
                If p.Banca = 0 AndAlso p.Programma = programma Then
                    scelto = p
                    Exit For
                End If
            Next
            If scelto Is Nothing Then
                For Each p As PresetSf2 In banco.Preset
                    If p.Banca = 0 AndAlso p.Programma = (programma \ 8) * 8 Then
                        scelto = p
                        Exit For
                    End If
                Next
            End If
            If scelto Is Nothing Then
                For Each p As PresetSf2 In banco.Preset
                    If p.Banca = 0 Then
                        scelto = p
                        Exit For
                    End If
                Next
            End If
        End If
        If scelto Is Nothing Then Return Nothing
        Dim strumento As New DefinizioneStrumento()
        strumento.Nome = If(batteria, "Batteria", NomiProgrammi(programma))
        strumento.Descrizione = scelto.Nome
        strumento.Percussivo = batteria
        strumento.Riverbero = 0.12
        strumento.MassimoVoci = 40
        Dim preset As PresetSf2 = scelto
        strumento.Crea = Function(nota As Integer, vel As Double) As VoceSintesi
                             Return banco.CreaVoce(preset, nota, vel)
                         End Function
        Return strumento
    End Function

    Public Shared Function Categorie(banco As BancoSoundFont) As List(Of CategoriaStrumenti)
        Dim elenco As New List(Of CategoriaStrumenti)()
        Dim perFamiglia(Famiglie.Length - 1) As CategoriaStrumenti
        For i As Integer = 0 To Famiglie.Length - 1
            Dim c As New CategoriaStrumenti()
            c.Nome = Famiglie(i)
            c.Descrizione = "Strumenti campionati: " & Famiglie(i)
            perFamiglia(i) = c
        Next
        Dim batterie As New CategoriaStrumenti()
        batterie.Nome = "Batteria e percussioni"
        batterie.Descrizione = "Batterie e percussioni: ogni tasto è un pezzo diverso"

        For Each preset As PresetSf2 In banco.Preset
            If preset.Banca = 0 AndAlso preset.Programma >= 0 AndAlso preset.Programma < 128 Then
                Dim programma As Integer = preset.Programma
                Dim strumento As New DefinizioneStrumento()
                strumento.Nome = NomiProgrammi(programma)
                strumento.Descrizione = preset.Nome
                Dim scelto As PresetSf2 = preset
                strumento.Crea = Function(nota As Integer, vel As Double) As VoceSintesi
                                     Return banco.CreaVoce(scelto, nota, vel)
                                 End Function
                strumento.OttavaIniziale = OttavaIniziale(programma)
                strumento.Origine = "sf"
                strumento.Programma = programma
                strumento.Riverbero = 0.12
                strumento.MassimoVoci = 40
                perFamiglia(programma \ 8).Strumenti.Add(strumento)
            ElseIf preset.Banca = 128 Then
                Dim strumento As New DefinizioneStrumento()
                strumento.Nome = NomeBatteria(preset)
                strumento.Descrizione = preset.Nome
                strumento.Percussivo = True
                strumento.Origine = "sf"
                strumento.Riverbero = 0.1
                strumento.MassimoVoci = 40
                Dim nomi(NoteBatteria.Length - 1) As String
                For i As Integer = 0 To NoteBatteria.Length - 1
                    nomi(i) = NomeNotaBatteria(NoteBatteria(i))
                Next
                strumento.NomiPezzi = nomi
                Dim scelto As PresetSf2 = preset
                strumento.Crea = Function(offset As Integer, vel As Double) As VoceSintesi
                                     Dim indice As Integer = ((offset Mod NoteBatteria.Length) + NoteBatteria.Length) Mod NoteBatteria.Length
                                     Return banco.CreaVoce(scelto, NoteBatteria(indice), vel)
                                 End Function
                batterie.Strumenti.Add(strumento)
            End If
        Next

        For Each c As CategoriaStrumenti In perFamiglia
            If c.Strumenti.Count > 0 Then elenco.Add(c)
        Next
        If batterie.Strumenti.Count > 0 Then
            batterie.Strumenti.Sort(Function(a As DefinizioneStrumento, b As DefinizioneStrumento) String.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase))
            elenco.Add(batterie)
        End If
        Return elenco
    End Function

End Class
